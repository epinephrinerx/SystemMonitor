using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>One name/value pair in the facts card.</summary>
public sealed class FullFact
{
    public required string Title { get; init; }
    public required string Value { get; init; }
}

/// <summary>
/// A chart card: the title and figures above, and the 72-point series behind.
/// Small cards (per core, per engine) carry only the title and value.
/// </summary>
public sealed class FullCard
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string Value { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Ceiling { get; init; } = string.Empty;
    public Colour Accent { get; init; } = Accents.Cpu;

    /// <summary>The fixed top of the scale (100 for a percentage), or 0 for auto.</summary>
    public double Maximum { get; init; }

    public required History History { get; init; }
    public bool Small { get; init; }
}

/// <summary>One tab of the Full view: rail text, header hardware and content.</summary>
public sealed class FullTab
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string Detail { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Hardware { get; init; } = string.Empty;
    public IReadOnlyList<FullFact> Facts { get; init; } = Array.Empty<FullFact>();
    public IReadOnlyList<FullCard> BigCards { get; init; } = Array.Empty<FullCard>();
    public IReadOnlyList<FullCard> SmallCards { get; init; } = Array.Empty<FullCard>();

    /// <summary>True for a physical-disk tab: double-click and right-click use the drive tools.</summary>
    public bool IsDisk { get; init; }

    /// <summary>The mounts behind a disk tab, for the drive menu.</summary>
    public IReadOnlyList<string> Mounts { get; init; } = Array.Empty<string>();
}

/// <summary>The tab list and the tab that is showing.</summary>
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
