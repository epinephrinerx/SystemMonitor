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
        bool Enabled,
        string ManifestSha256 = "");

    private readonly AppConfig _config;
    private readonly ExtensionLoaderOptions _baseOptions;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Dictionary<string, ExtensionSlot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExtensionLoadResult> _lastResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _enabled = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Language, string Name)> _cachedDisplayNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loggedErrors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ExtensionSlot Slot, ExtensionDemand Demand)> _lastPushedDemand = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _diskTemperatureDemand;
    private (string Id, ExtensionSlot Slot)[] _sortedSlotsSnapshot = Array.Empty<(string Id, ExtensionSlot Slot)>();
    private IReadOnlyList<ExtensionEntry> _entries = Array.Empty<ExtensionEntry>();
    private bool _disposed;
    internal Action<string>? OnReloadRetiring { get; set; }

    /// <summary>
    /// How long <see cref="Reload"/> waits for in-flight calls on the old instance before disposing it anyway.
    /// Internal so a test can shorten it.
    /// </summary>
    internal TimeSpan ReloadRetireWait { get; set; } = TimeSpan.FromSeconds(5);

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
                _lastPushedDemand.Clear();

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

                UpdateSortedSlotsSnapshotLocked();
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

        // Push initial demand to IDemandAware after Initialize and before first GetView
        foreach (var id in newSlots.Keys)
        {
            PushDemand(id);
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
            IsEnabled = IsEnabledInConfig,
            KeepLoaded = IsKeptLoadedManifest
        };
    }

    private static bool IsKeptLoadedManifest(ExtensionManifest manifest, bool isFirstParty)
    {
        return isFirstParty &&
               manifest.Provides != null &&
               manifest.Provides.Contains("disk-temperature", StringComparer.Ordinal);
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
            string sha = res.ManifestSha256 ?? string.Empty;

            list.Add(new ExtensionEntry(
                Id: id,
                Name: name,
                Version: version,
                Status: res.Status,
                Detail: res.Detail,
                Enabled: enabled,
                ManifestSha256: sha));
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
                // Check if this extension is loaded and should remain loaded even when disabled
                ExtensionSlot? slot = null;
                bool isKeptLoaded = false;
                lock (_lock)
                {
                    if (_slots.TryGetValue(id, out var s) &&
                        _lastResults.TryGetValue(id, out var res) &&
                        res.Manifest != null &&
                        IsKeptLoadedManifest(res.Manifest, res.IsFirstParty))
                    {
                        slot = s;
                        isKeptLoaded = true;
                    }
                }

                if (isKeptLoaded && slot != null)
                {
                    // Extension stays loaded in its slot. Push updated demand and republish entries.
                    PushDemand(id);
                    lock (_lock)
                    {
                        if (!_disposed)
                        {
                            Volatile.Write(ref _entries, BuildEntriesListLocked());
                        }
                    }
                    return;
                }

                ReconcileExtension(id, refresh: false);
            }
            finally
            {
                _lifecycle.Release();
            }
        });
    }

    /// <summary>
    /// Reloads an extension by re-evaluating trust via LoadFolder, updating its load status,
    /// and reloading it if enabled. A reload of an unknown id is a no-op.
    /// Serialized through the lifecycle lock.
    /// </summary>
    public Task Reload(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return Task.Run(async () =>
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                ExtensionSlot? slotToRetire = null;

                lock (_lock)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    bool isKnown = _lastResults.ContainsKey(id) || _slots.ContainsKey(id);
                    if (!isKnown)
                    {
                        return;
                    }

                    _lastPushedDemand.Remove(id);
                    if (_slots.Remove(id, out var s))
                    {
                        slotToRetire = s;
                        UpdateSortedSlotsSnapshotLocked();
                    }
                    _cachedDisplayNames.Remove(id);
                }

                OnReloadRetiring?.Invoke(id);
                slotToRetire?.RetireAndDispose(ReloadRetireWait);

                ReconcileExtension(id, refresh: true);
            }
            finally
            {
                _lifecycle.Release();
            }
        });
    }

    private void ReconcileExtension(string id, bool refresh)
    {
        bool desired;
        ExtensionSlot? slotToRetire = null;

        if (!refresh)
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                bool en = _enabled.TryGetValue(id, out bool e) && e;
                bool isKept = _lastResults.TryGetValue(id, out var prevRes) &&
                             prevRes.Manifest != null &&
                             IsKeptLoadedManifest(prevRes.Manifest, prevRes.IsFirstParty);
                desired = en || isKept;

                if (desired && _slots.ContainsKey(id))
                {
                    // desired and a slot exists -> return, do nothing (no Shutdown, no Initialize)
                    return;
                }

                if (!desired)
                {
                    _lastPushedDemand.Remove(id);
                    if (_slots.Remove(id, out var s))
                    {
                        slotToRetire = s;
                        UpdateSortedSlotsSnapshotLocked();
                    }
                    _cachedDisplayNames.Remove(id);

                    if (_lastResults.TryGetValue(id, out var prev))
                    {
                        if (prev.Status is ExtensionStatus.Loaded or ExtensionStatus.Disabled)
                        {
                            _lastResults[id] = new ExtensionLoadResult(
                                prev.FolderName,
                                prev.Id ?? id,
                                ExtensionStatus.Disabled,
                                "Extension is disabled in settings",
                                prev.ManifestSha256,
                                null,
                                prev.Manifest,
                                prev.IsFirstParty);
                        }
                    }
                    else
                    {
                        _lastResults[id] = new ExtensionLoadResult(
                            id,
                            id,
                            ExtensionStatus.Disabled,
                            "Extension is disabled in settings",
                            null,
                            null,
                            null);
                    }

                    Volatile.Write(ref _entries, BuildEntriesListLocked());
                }
            }

            if (!desired)
            {
                slotToRetire?.RetireAndDispose(TimeSpan.FromSeconds(5));
                return;
            }
        }
        else
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }
                bool en = _enabled.TryGetValue(id, out bool e) && e;
                bool isKept = _lastResults.TryGetValue(id, out var prevRes) &&
                             prevRes.Manifest != null &&
                             IsKeptLoadedManifest(prevRes.Manifest, prevRes.IsFirstParty);
                desired = en || isKept;
            }
        }

        // desired and no slot (refresh == false), OR refresh == true:
        // Always call LoadFolder (also when not desired, so the trust status is refreshed;
        // the loader checks trust before Disabled) and publish.
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
            bool curEn = _enabled.TryGetValue(id, out bool ce) && ce;
            bool curKept = result.Manifest != null && IsKeptLoadedManifest(result.Manifest, result.IsFirstParty);
            bool curDesired = curEn || curKept;

            if (refresh && !_disposed)
            {
                // Reload already removed the slot and the old result no longer says anything: whatever
                // the loader found now (including a failure) is the truth and must be published.
                ExtensionLoadResult toStore = result;
                ExtensionSlot? slotToUse = newSlot;
                if (newSlot != null && !curDesired)
                {
                    slotToRetireOnAbort = newSlot;
                    slotToUse = null;
                    toStore = new ExtensionLoadResult(
                        result.FolderName,
                        result.Id ?? id,
                        ExtensionStatus.Disabled,
                        "Extension is disabled in settings",
                        result.ManifestSha256,
                        null,
                        result.Manifest,
                        result.IsFirstParty);
                }

                _lastResults[id] = toStore;
                if (slotToUse != null)
                {
                    _lastPushedDemand.Remove(id);
                    _slots[id] = slotToUse;
                    UpdateSortedSlotsSnapshotLocked();
                    if (displayName != null)
                    {
                        _cachedDisplayNames[id] = (_config.Lang, displayName);
                    }
                }

                Volatile.Write(ref _entries, BuildEntriesListLocked());
            }
            else if (_disposed || (!refresh && (result.Status == ExtensionStatus.Loaded ? !curDesired : curDesired != desired)))
            {
                slotToRetireOnAbort = newSlot;
            }
            else
            {
                _lastResults[id] = result;
                if (newSlot != null)
                {
                    _lastPushedDemand.Remove(id);
                    _slots[id] = newSlot;
                    UpdateSortedSlotsSnapshotLocked();
                    if (displayName != null)
                    {
                        _cachedDisplayNames[id] = (_config.Lang, displayName);
                    }
                }

                Volatile.Write(ref _entries, BuildEntriesListLocked());
            }
        }

        if (newSlot != null && slotToRetireOnAbort == null)
        {
            PushDemand(id);
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
            if (!slot.DemandApplied)
            {
                continue;
            }

            if (!slot.TryAcquire())
            {
                continue;
            }

            try
            {
                LoadedExtension ext = slot.Extension;
                string id = ext.Manifest.Id;

                // Extensions kept loaded for background metrics must not produce views when disabled
                lock (_lock)
                {
                    if (!_enabled.TryGetValue(id, out bool en) || !en)
                    {
                        continue;
                    }
                }

                if (ext.Instance is not IViewProvider provider)
                {
                    continue;
                }

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

                    views.Add(new ExtensionViewData(id, displayName, sanitized, ext.Manifest.PlaceAfter));
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

    private void UpdateSortedSlotsSnapshotLocked()
    {
        _sortedSlotsSnapshot = _slots
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (kv.Key, kv.Value))
            .ToArray();
    }

    /// <summary>
    /// Collects disk temperatures from all loaded extensions implementing <see cref="IDiskTemperatureSource"/>.
    /// Iterates extensions in ordinal ID order, bounded to drive 0..255 and temperature 1..149 °C,
    /// max 64 entries per extension. First extension to report a drive wins.
    /// Safe to call on the sampler thread without stalling on lifecycle locks.
    /// </summary>
    public IReadOnlyDictionary<int, int> GetDiskTemperatures()
    {
        (string Id, ExtensionSlot Slot)[] active;
        lock (_lock)
        {
            if (_disposed || _sortedSlotsSnapshot.Length == 0)
            {
                return new Dictionary<int, int>();
            }
            active = _sortedSlotsSnapshot;
        }

        var result = new Dictionary<int, int>();

        foreach (var (id, slot) in active)
        {
            if (!slot.DemandApplied)
            {
                continue;
            }

            if (!slot.TryAcquire())
            {
                continue;
            }

            try
            {
                LoadedExtension ext = slot.Extension;
                if (ext.Instance is not IDiskTemperatureSource tempSource)
                {
                    continue;
                }

                try
                {
                    IEnumerable<KeyValuePair<int, int>>? dict = tempSource.GetDiskTemperatures();
                    if (dict != null)
                    {
                        int inspected = 0;
                        int validCount = 0;
                        foreach (var kv in dict)
                        {
                            if (++inspected > 256)
                            {
                                break;
                            }
                            int drive = kv.Key;
                            int celsius = kv.Value;
                            if (drive is >= 0 and <= 255 && celsius is >= 1 and <= 149)
                            {
                                if (result.TryAdd(drive, celsius))
                                {
                                    if (++validCount >= 64)
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    SafeLogOnce(id, ex, "IDiskTemperatureSource");
                }
            }
            finally
            {
                slot.Release();
            }
        }

        return result;
    }

    /// <summary>
    /// Updates the host's demand for disk temperature.
    /// When demand changes for an extension, <see cref="IDemandAware.SetDemand"/> is called.
    /// </summary>
    public void SetDemand(bool diskTemperatureNeeded)
    {
        List<string> activeIds;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _diskTemperatureDemand = diskTemperatureNeeded;
            activeIds = _slots.Keys.ToList();
        }

        foreach (string id in activeIds)
        {
            PushDemand(id);
        }
    }

    private void PushDemand(string id)
    {
        ExtensionSlot? target;
        lock (_lock)
        {
            if (_disposed || !_slots.TryGetValue(id, out target))
            {
                return;
            }
        }

        // The gate belongs to the slot that is current now, so a blocked callback on a retired
        // instance never holds the gate of its replacement.
        DemandGate gate = target.DemandGate;

        // Marked before the attempt: a holder that is about to leave must still see it.
        gate.Dirty = true;
        if (!Monitor.TryEnter(gate.Lock))
        {
            return;
        }

        try
        {
            while (true)
            {
                gate.Dirty = false;

                ExtensionSlot? slot = null;
                bool isEnabled = false;
                bool tempDemand = _diskTemperatureDemand;
                lock (_lock)
                {
                    if (_disposed)
                    {
                        return;
                    }
                    // Only the slot whose gate this is: if it was replaced, the replacement's own push covers it.
                    if (_slots.TryGetValue(id, out var s) && ReferenceEquals(s, target))
                    {
                        slot = s;
                        isEnabled = _enabled.TryGetValue(id, out bool en) && en;
                    }
                }

                if (slot == null || slot.Extension.Instance is not IDemandAware demandAware)
                {
                    break;
                }

                ExtensionDemand desired = ExtensionDemand.None;
                if (isEnabled)
                {
                    desired |= ExtensionDemand.Views;
                }
                if (tempDemand)
                {
                    desired |= ExtensionDemand.DiskTemperature;
                }

                bool alreadyApplied = false;
                lock (_lock)
                {
                    if (_lastPushedDemand.TryGetValue(id, out var tracked) &&
                        ReferenceEquals(tracked.Slot, slot) &&
                        tracked.Demand == desired)
                    {
                        alreadyApplied = true;
                    }
                }

                if (!alreadyApplied)
                {
                    if (slot.TryAcquire())
                    {
                        try
                        {
                            demandAware.SetDemand(desired);
                            lock (_lock)
                            {
                                // A callback that returns after its slot was replaced must not record itself as current.
                                if (_slots.TryGetValue(id, out var currentSlot) && ReferenceEquals(currentSlot, slot))
                                {
                                    _lastPushedDemand[id] = (slot, desired);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            SafeLogOnce(id, ex, "IDemandAware");
                        }
                        finally
                        {
                            slot.DemandApplied = true;
                            slot.Release();
                        }
                    }
                }

                if (!gate.Dirty)
                {
                    break;
                }
            }
        }
        finally
        {
            Monitor.Exit(gate.Lock);
        }

        // Re-check once more in case a thread set Dirty just as we were releasing the lock
        if (gate.Dirty)
        {
            PushDemand(id);
        }
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
            _lastPushedDemand.Clear();
            _sortedSlotsSnapshot = Array.Empty<(string Id, ExtensionSlot Slot)>();
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
