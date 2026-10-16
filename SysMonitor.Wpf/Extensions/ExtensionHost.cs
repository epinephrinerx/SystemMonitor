using System.IO;
using SysMonitor.Extensibility;

namespace SysMonitor.Extensions;

/// <summary>
/// Lifecycle manager for all extensions in the host application.
/// Manages background discovery, loading, toggling, and view collection.
/// </summary>
public sealed class ExtensionHost : IDisposable
{
    public const int MaxWidgetPages = 32;
    public const int MaxOverviewRows = 64;
    public const int MaxDetailTabs = 32;
    public const int MaxFactsPerTab = 64;
    public const int MaxTextLength = 512;
    public const int MinTemperatureC = -50;
    public const int MaxTemperatureC = 150;

    /// <summary>
    /// Represents an extension entry reported to the UI (e.g. sidebar).
    /// </summary>
    public sealed record ExtensionEntry(
        string Id,
        string Name,
        string Version,
        ExtensionStatus Status,
        string Detail,
        bool Enabled);

    private readonly AppConfig _config;
    private readonly ExtensionLoaderOptions _baseOptions;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Dictionary<string, ExtensionSlot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExtensionLoadResult> _lastResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _enabled = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Language, string Name)> _cachedDisplayNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loggedErrors = new(StringComparer.Ordinal);
    private IReadOnlyList<ExtensionEntry> _entries = Array.Empty<ExtensionEntry>();
    private bool _disposed;

    public ExtensionHost(AppConfig config, ExtensionLoaderOptions baseOptions)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _baseOptions = baseOptions ?? throw new ArgumentNullException(nameof(baseOptions));

        if (_config.Extensions != null)
        {
            foreach (var (id, setting) in _config.Extensions)
            {
                if (id != null && setting != null)
                {
                    _enabled[id] = setting.Enabled;
                }
            }
        }
    }

    /// <summary>
    /// All discovered extension entries, readable from any thread.
    /// Updated via volatile swap when loading or toggling completes.
    /// </summary>
    public IReadOnlyList<ExtensionEntry> Entries => Volatile.Read(ref _entries);

    /// <summary>
    /// Starts background discovery and loads all enabled extensions without blocking the UI thread.
    /// </summary>
    public Task StartAsync()
    {
        return Task.Run(async () =>
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                ScanAndLoadAll();
            }
            finally
            {
                _lifecycle.Release();
            }
        });
    }

    private void ScanAndLoadAll()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
        }

        // Hashing, loading, and Initialize run outside _lock
        ExtensionLoaderOptions options = CreateOptions();
        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        var newSlots = new Dictionary<string, ExtensionSlot>(StringComparer.OrdinalIgnoreCase);
        var initialNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (ExtensionLoadResult res in results)
        {
            string id = res.Id ?? res.FolderName;
            if (res.Status == ExtensionStatus.Loaded && res.LoadedExtension != null)
            {
                var slot = new ExtensionSlot(res.LoadedExtension, _baseOptions.Log);
                newSlots[id] = slot;

                if (res.LoadedExtension.Instance is IViewProvider provider && slot.TryAcquire())
                {
                    try
                    {
                        string dn = SanitizeText(provider.GetDisplayName(_config.Lang));
                        if (!string.IsNullOrWhiteSpace(dn))
                        {
                            initialNames[id] = dn;
                        }
                    }
                    catch
                    {
                        // Fall back to manifest name
                    }
                    finally
                    {
                        slot.Release();
                    }
                }
            }
        }

        List<ExtensionSlot> oldSlots = null!;
        bool disposed;
        lock (_lock)
        {
            disposed = _disposed;
            if (!disposed)
            {
                oldSlots = _slots.Values.ToList();
                _slots.Clear();
                _lastResults.Clear();
                _cachedDisplayNames.Clear();

                foreach (ExtensionLoadResult res in results)
                {
                    string id = res.Id ?? res.FolderName;
                    _lastResults[id] = res;
                }

                foreach (var (id, slot) in newSlots)
                {
                    _slots[id] = slot;
                }

                foreach (var (id, name) in initialNames)
                {
                    _cachedDisplayNames[id] = (_config.Lang, name);
                }

                Volatile.Write(ref _entries, BuildEntriesListLocked());
            }
        }

        if (disposed)
        {
            foreach (var slot in newSlots.Values)
            {
                slot.RetireAndDispose(TimeSpan.FromSeconds(5));
            }
            return;
        }

        // Retire any replaced slots outside _lock
        foreach (var old in oldSlots)
        {
            old.RetireAndDispose(TimeSpan.FromSeconds(5));
        }
    }

    private bool IsEnabledInConfig(string id)
    {
        lock (_lock)
        {
            return _enabled.TryGetValue(id, out bool enabled) && enabled;
        }
    }

    private ExtensionLoaderOptions CreateOptions()
    {
        return new ExtensionLoaderOptions
        {
            ExtensionsRoot = _baseOptions.ExtensionsRoot,
            HostVersion = _baseOptions.HostVersion,
            HostIsElevated = _baseOptions.HostIsElevated,
            Language = _config.Lang,
            FirstParty = _baseOptions.FirstParty,
            Approvals = _baseOptions.Approvals,
            Log = _baseOptions.Log,
            IsEnabled = IsEnabledInConfig
        };
    }

    private IReadOnlyList<ExtensionEntry> BuildEntriesListLocked()
    {
        var list = new List<ExtensionEntry>();

        var sortedKeys = _lastResults.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
        foreach (string key in sortedKeys)
        {
            ExtensionLoadResult res = _lastResults[key];
            string id = res.Id ?? res.FolderName;
            ExtensionManifest? manifest = res.Manifest;

            string name = manifest?.Name ?? id;
            if (_cachedDisplayNames.TryGetValue(id, out var cached) && !string.IsNullOrWhiteSpace(cached.Name))
            {
                name = cached.Name;
            }

            string version = manifest?.Version ?? string.Empty;
            bool enabled = _enabled.TryGetValue(id, out bool en) && en;

            list.Add(new ExtensionEntry(
                Id: id,
                Name: name,
                Version: version,
                Status: res.Status,
                Detail: res.Detail,
                Enabled: enabled));
        }

        return list;
    }

    /// <summary>
    /// Enables or disables an extension. Persists setting to config and reconciles
    /// the extension immediately on a background thread.
    /// </summary>
    public Task SetEnabled(string id, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(id);

        lock (_lock)
        {
            _enabled[id] = enabled;
        }

        // Saving is the caller's job: AppConfig.Save writes to the one real profile file, so a
        // host that saved on its own would overwrite the user's settings from any test.
        _config.Extensions[id] = new ExtensionSetting { Enabled = enabled };

        return Task.Run(async () =>
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                ReconcileExtension(id);
            }
            finally
            {
                _lifecycle.Release();
            }
        });
    }

    private void ReconcileExtension(string id)
    {
        bool desired;
        bool isLoaded;
        ExtensionSlot? slotToRetire = null;

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            desired = _enabled.TryGetValue(id, out bool en) && en;
            isLoaded = _slots.ContainsKey(id);

            if (!desired)
            {
                if (_slots.Remove(id, out var s))
                {
                    slotToRetire = s;
                }
                _cachedDisplayNames.Remove(id);

                // When disabling, replace previous result with Disabled only if previous was Loaded or Disabled (or none).
                // If it was a failure status (NotApproved, NotTrustedWhenElevated, HashMismatch, ...), keep it unchanged.
                bool hasPrev = _lastResults.TryGetValue(id, out var prev);
                if (!hasPrev || prev!.Status is ExtensionStatus.Loaded or ExtensionStatus.Disabled)
                {
                    ExtensionManifest? manifest = prev?.Manifest;
                    string? hash = prev?.ManifestSha256;
                    _lastResults[id] = new ExtensionLoadResult(
                        id,
                        id,
                        ExtensionStatus.Disabled,
                        "Extension is disabled in settings",
                        hash,
                        null,
                        manifest);
                }

                Volatile.Write(ref _entries, BuildEntriesListLocked());
            }
        }

        if (!desired)
        {
            // Retire outside _lock
            slotToRetire?.RetireAndDispose(TimeSpan.FromSeconds(5));
            return;
        }

        // Desired is enabled. If already loaded, stale worker finds nothing to do.
        if (isLoaded)
        {
            return;
        }

        // Desired enabled and not loaded -> load outside _lock
        string folderPath = Path.Combine(_baseOptions.ExtensionsRoot, id);
        ExtensionLoadResult result = ExtensionLoader.LoadFolder(folderPath, CreateOptions());
        ExtensionSlot? newSlot = null;
        string? displayName = null;

        if (result.Status == ExtensionStatus.Loaded && result.LoadedExtension != null)
        {
            newSlot = new ExtensionSlot(result.LoadedExtension, _baseOptions.Log);
            if (result.LoadedExtension.Instance is IViewProvider vp && newSlot.TryAcquire())
            {
                try
                {
                    string dn = SanitizeText(vp.GetDisplayName(_config.Lang));
                    if (!string.IsNullOrWhiteSpace(dn))
                    {
                        displayName = dn;
                    }
                }
                catch
                {
                }
                finally
                {
                    newSlot.Release();
                }
            }
        }

        ExtensionSlot? slotToRetireOnAbort = null;
        lock (_lock)
        {
            if (_disposed || !_enabled.TryGetValue(id, out bool en) || !en)
            {
                slotToRetireOnAbort = newSlot;
            }
            else
            {
                _lastResults[id] = result;
                if (newSlot != null)
                {
                    _slots[id] = newSlot;
                    if (displayName != null)
                    {
                        _cachedDisplayNames[id] = (_config.Lang, displayName);
                    }
                }

                Volatile.Write(ref _entries, BuildEntriesListLocked());
            }
        }

        // Retire outside _lock if aborted
        slotToRetireOnAbort?.RetireAndDispose(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Collects views from all currently loaded extensions that implement <see cref="IViewProvider"/>.
    /// Sanitizes all inputs against named ceilings and clamps out-of-range values.
    /// Safe to call on the sampler background thread without stalling on lifecycle locks.
    /// </summary>
    public IReadOnlyList<ExtensionViewData> CollectViews(string language)
    {
        List<ExtensionSlot> active;
        lock (_lock)
        {
            if (_disposed || _slots.Count == 0)
            {
                return Array.Empty<ExtensionViewData>();
            }
            active = _slots.Values.ToList();
        }

        var views = new List<ExtensionViewData>(active.Count);
        bool anyNameChanged = false;

        foreach (ExtensionSlot slot in active)
        {
            if (!slot.TryAcquire())
            {
                continue;
            }

            try
            {
                LoadedExtension ext = slot.Extension;
                if (ext.Instance is not IViewProvider provider)
                {
                    continue;
                }

                string id = ext.Manifest.Id;
                string displayName;
                ExtensionView rawView;

                try
                {
                    displayName = SanitizeText(provider.GetDisplayName(language));
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = SanitizeText(ext.Manifest.Name);
                    }

                    lock (_lock)
                    {
                        if (!_cachedDisplayNames.TryGetValue(id, out var curr)
                            || curr.Language != language
                            || curr.Name != displayName)
                        {
                            _cachedDisplayNames[id] = (language, displayName);
                            anyNameChanged = true;
                        }
                    }

                    rawView = provider.GetView(language);
                    ExtensionView sanitized = SanitizeView(rawView ?? ExtensionView.Empty);

                    views.Add(new ExtensionViewData(id, displayName, sanitized));
                }
                catch (Exception ex)
                {
                    SafeLogOnce(id, ex, "IViewProvider");
                }
            }
            finally
            {
                slot.Release();
            }
        }

        if (anyNameChanged)
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    Volatile.Write(ref _entries, BuildEntriesListLocked());
                }
            }
        }

        return views;
    }

    private void SafeLogOnce(string id, Exception ex, string context)
    {
        string logKey = $"{id}:{ex.GetType().Name}";
        bool shouldLog = false;
        lock (_loggedErrors)
        {
            if (!_loggedErrors.Contains(logKey))
            {
                if (_loggedErrors.Count < 256)
                {
                    _loggedErrors.Add(logKey);
                    shouldLog = true;
                }
            }
        }

        if (shouldLog)
        {
            _baseOptions.Log.SafeLog($"Extension '{id}' threw {ex.GetType().Name} in {context}: {ex.Describe()}");
        }
    }

    private static ExtensionView SanitizeView(ExtensionView view)
    {
        // 1. Sanitize WidgetPages (up to MaxWidgetPages)
        var widgetPages = new List<WidgetPage>();
        if (view.WidgetPages != null)
        {
            int pageCount = Math.Min(view.WidgetPages.Count, MaxWidgetPages);
            for (int i = 0; i < pageCount; i++)
            {
                WidgetPage p = view.WidgetPages[i];
                if (p == null) continue;

                int? temp = p.TemperatureC is >= MinTemperatureC and <= MaxTemperatureC
                    ? p.TemperatureC
                    : null;
                int percent = Math.Clamp(p.Percent, 0, 100);
                string? message = p.Message != null ? SanitizeText(p.Message) : null;

                widgetPages.Add(new WidgetPage(
                    Title: SanitizeText(p.Title),
                    Verdict: SanitizeText(p.Verdict),
                    Line: SanitizeText(p.Line),
                    TemperatureC: temp,
                    Percent: percent,
                    Severity: p.Severity,
                    Message: message));
            }
        }

        // 2. Sanitize OverviewSection (up to MaxOverviewRows)
        OverviewSection? overview = null;
        if (view.Overview != null)
        {
            var rows = new List<OverviewRow>();
            if (view.Overview.Rows != null)
            {
                int rowCount = Math.Min(view.Overview.Rows.Count, MaxOverviewRows);
                for (int i = 0; i < rowCount; i++)
                {
                    OverviewRow r = view.Overview.Rows[i];
                    if (r == null) continue;

                    int? temp = r.TemperatureC is >= MinTemperatureC and <= MaxTemperatureC
                        ? r.TemperatureC
                        : null;
                    int percent = Math.Clamp(r.Percent, 0, 100);

                    rows.Add(new OverviewRow(
                        Title: SanitizeText(r.Title),
                        Detail: SanitizeText(r.Detail),
                        ValueText: SanitizeText(r.ValueText),
                        Percent: percent,
                        Severity: r.Severity,
                        TemperatureC: temp));
                }
            }
            overview = new OverviewSection(SanitizeText(view.Overview.Note), rows);
        }

        // 3. Sanitize DetailTabs (up to MaxDetailTabs, unique non-empty keys, up to MaxFactsPerTab)
        var tabs = new List<DetailTab>();
        if (view.Tabs != null)
        {
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            int inspected = 0;
            const int maxInspect = MaxDetailTabs * 4;

            if (view.Tabs is IReadOnlyList<DetailTab> tabList)
            {
                int count = tabList.Count;
                int limit = Math.Min(count, maxInspect);
                for (int i = 0; i < limit; i++)
                {
                    DetailTab t = tabList[i];
                    SanitizeTab(t, tabs, seenKeys);
                    if (tabs.Count >= MaxDetailTabs) break;
                }
            }
            else
            {
                foreach (DetailTab t in view.Tabs)
                {
                    if (++inspected > maxInspect) break;
                    SanitizeTab(t, tabs, seenKeys);
                    if (tabs.Count >= MaxDetailTabs) break;
                }
            }
        }

        return new ExtensionView(widgetPages, overview, tabs);
    }

    private static void SanitizeTab(DetailTab? t, List<DetailTab> tabs, HashSet<string> seenKeys)
    {
        if (t == null)
        {
            return;
        }

        string key = SanitizeText(t.Key);
        if (string.IsNullOrWhiteSpace(key) || !seenKeys.Add(key))
        {
            return;
        }

        var facts = new List<Fact>();
        if (t.Facts != null)
        {
            if (t.Facts is IReadOnlyList<Fact> factList)
            {
                int fCount = factList.Count;
                int fLimit = Math.Min(fCount, MaxFactsPerTab);
                for (int f = 0; f < fLimit; f++)
                {
                    Fact fact = factList[f];
                    if (fact == null) continue;
                    facts.Add(new Fact(SanitizeText(fact.Name), SanitizeText(fact.Value)));
                }
            }
            else
            {
                int fInspected = 0;
                foreach (Fact fact in t.Facts)
                {
                    if (fact != null)
                    {
                        facts.Add(new Fact(SanitizeText(fact.Name), SanitizeText(fact.Value)));
                        if (facts.Count >= MaxFactsPerTab) break;
                    }
                    if (++fInspected >= MaxFactsPerTab * 4) break;
                }
            }
        }

        tabs.Add(new DetailTab(
            Key: key,
            Title: SanitizeText(t.Title),
            Subtitle: SanitizeText(t.Subtitle),
            Detail: SanitizeText(t.Detail),
            Hardware: SanitizeText(t.Hardware),
            Facts: facts));
    }

    private static string SanitizeText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length > MaxTextLength ? text[..MaxTextLength] : text;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }

        bool acquired = false;
        try
        {
            acquired = _lifecycle.Wait(TimeSpan.FromSeconds(5));
        }
        catch (ObjectDisposedException)
        {
        }

        List<ExtensionSlot> slotsToRetire;
        lock (_lock)
        {
            slotsToRetire = _slots.Values.ToList();
            _slots.Clear();
            _lastResults.Clear();
            _cachedDisplayNames.Clear();
            Volatile.Write(ref _entries, Array.Empty<ExtensionEntry>());
        }

        foreach (ExtensionSlot slot in slotsToRetire)
        {
            slot.RetireAndDispose(TimeSpan.FromSeconds(5));
        }

        if (acquired)
        {
            try
            {
                _lifecycle.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
