using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>One name/value pair in a page's Properties list.</summary>
public sealed class FullFact
{
    public required string Title { get; init; }
    public required string Value { get; init; }

    /// <summary>
    /// A value the probe must not print: the Wi-Fi network name names a place.
    /// The model keeps it, the probe prints "(hidden)".
    /// </summary>
    public bool Private { get; init; }
}

/// <summary>
/// A chart card: the title and figures above, and the 72-point series behind.
/// Small cards (per core, per engine, per volume) carry only the title and
/// value.
/// </summary>
public sealed class FullCard
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string Value { get; init; } = string.Empty;

    /// <summary>"Highest: 42%": the peak of the visible history (§2b).</summary>
    public string Peak { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Ceiling { get; init; } = string.Empty;
    public Colour Accent { get; init; } = Accents.Cpu;

    /// <summary>The fixed top of the scale (100 for a percentage), or 0 for auto.</summary>
    public double Maximum { get; init; }

    public required History History { get; init; }
    public bool Small { get; init; }
}

/// <summary>One module option in a page's options bar (§2).</summary>
/// <param name="ConfigKey">The config key the option edits, e.g. "cpu_mode".</param>
/// <param name="LabelKey">The string-table key of its label.</param>
/// <param name="Value">The current setting.</param>
public sealed record ModuleOption(string ConfigKey, string LabelKey, bool Value);

/// <summary>One module page: header, options, Usage, Sensors and Properties (§2b).</summary>
public sealed class FullTab
{
    public required string Key { get; init; }

    /// <summary>The device name, e.g. "NVIDIA GeForce MX250".</summary>
    public required string Title { get; init; }

    /// <summary>The module label under the device name, e.g. "GPU 1", "Disk 2".</summary>
    public string Label { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// The page's one badge: the battery's power pill while the cell charges or
    /// a power source feeds the machine. The sidebar item and the page header
    /// draw it, so the icon follows the AC state on both (R-030 fix 4). Null on
    /// every other page.
    /// </summary>
    public TempBadge? Badge { get; init; }
    public string Hardware { get; init; } = string.Empty;

    /// <summary>The options bar: empty for a module with no options.</summary>
    public IReadOnlyList<ModuleOption> Options { get; init; } = Array.Empty<ModuleOption>();

    /// <summary>The §2b Properties list; every unreadable value is "N/A".</summary>
    public IReadOnlyList<FullFact> Properties { get; init; } = Array.Empty<FullFact>();

    /// <summary>Temperature charts with their peaks; empty when there is no sensor.</summary>
    public IReadOnlyList<FullCard> Sensors { get; init; } = Array.Empty<FullCard>();

    /// <summary>The usage charts at the top of the page.</summary>
    public IReadOnlyList<FullCard> BigCards { get; init; } = Array.Empty<FullCard>();

    public IReadOnlyList<FullCard> SmallCards { get; init; } = Array.Empty<FullCard>();

    /// <summary>The old name for <see cref="Properties"/>, kept for the phase-B1 views.</summary>
    public IReadOnlyList<FullFact> Facts => Properties;

    /// <summary>True for a physical-disk page: double-click and right-click use the drive tools.</summary>
    public bool IsDisk { get; init; }

    /// <summary>The mounts behind a disk page, for the drive menu.</summary>
    public IReadOnlyList<string> Mounts { get; init; } = Array.Empty<string>();
}

/// <summary>The page list and the page that is showing.</summary>
public sealed class FullModel
{
    public IReadOnlyList<FullTab> Tabs { get; init; } = Array.Empty<FullTab>();
    public string? SelectedKey { get; init; }
    public FullTab? Selected => Tabs.FirstOrDefault(t => t.Key == SelectedKey);
}

/// <summary>
/// Tab selection and reconciliation (spec section 5a). The remembered key wins
/// on first use, an extension key falls back to any tab of that extension, and
/// a fallback switches to the remembered tab as soon as it appears. A tab the
/// person picked stays picked.
///
/// R-030 keeps this reconciler for the module pages, whose keys and extension
/// fallback do not change; the page around them lives in <see cref="MainState"/>.
/// </summary>
public sealed class FullSelection
{
    private string? _current;
    private bool _fallback;

    public string? Current => _current;
    public bool UsedFallback => _fallback;

    /// <summary>The person clicked a tab; that choice now wins until it disappears.</summary>
    public void Select(string? key)
    {
        _current = key;
        _fallback = false;
    }

    /// <summary>Reconcile with the tabs that exist now and return the key to show.</summary>
    public string? Reconcile(IReadOnlyList<FullTab> tabs, string? remembered)
    {
        if (tabs.Count == 0)
        {
            _current = null;
            _fallback = false;
            return null;
        }

        string wanted = remembered ?? string.Empty;
        FullTab? rememberedTab = Find(tabs, wanted)
            ?? FindExtension(tabs, wanted);

        if (_current is not null && Find(tabs, _current) is not null)
        {
            // A fallback yields as soon as the remembered tab shows up.
            if (_fallback && rememberedTab is not null && rememberedTab.Key != _current)
            {
                _current = rememberedTab.Key;
                _fallback = false;
            }
            return _current;
        }

        if (rememberedTab is not null)
        {
            _current = rememberedTab.Key;
            _fallback = wanted.Length > 0 && rememberedTab.Key != wanted;
            return _current;
        }

        _current = tabs[0].Key;
        _fallback = wanted.Length > 0 && tabs[0].Key != wanted;
        return _current;
    }

    private static FullTab? Find(IReadOnlyList<FullTab> tabs, string key) =>
        key.Length == 0
            ? null
            : tabs.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// "ext:&lt;id&gt;:&lt;sub&gt;" falls back to any tab of the same extension,
    /// as the spec's extension rule says.
    /// </summary>
    private static FullTab? FindExtension(IReadOnlyList<FullTab> tabs, string key)
    {
        string prefix = ExtensionPrefix(key);
        return prefix.Length == 0
            ? null
            : tabs.FirstOrDefault(t => t.Key.StartsWith(prefix, StringComparison.Ordinal));
    }

    public static string ExtensionPrefix(string? key)
    {
        if (key is null || !key.StartsWith("ext:", StringComparison.Ordinal))
        {
            return string.Empty;
        }
        int colon = key.IndexOf(':', 4);
        return colon < 0 ? "ext:" : key[..(colon + 1)];
    }
}
