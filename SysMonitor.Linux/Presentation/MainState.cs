using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The page keys Main can show: the three fixed pages first, then the per-module
/// pages the FullBoard tabs already use. One place, so the tab builder, the main
/// state and the histories cannot disagree about what a device is called.
/// </summary>
public static class PageKeys
{
    public const string Overall = "overall";
    public const string Apps = "apps";
    public const string Processes = "processes";
    public const string Cpu = "cpu";
    public const string Ram = "ram";

    /// <summary>The grouped virtual-drive page, when a mount has no physical disk.</summary>
    public const string DiskVirtual = "diskvirtual";

    /// <summary>The grouped network-drive page.</summary>
    public const string DiskNetwork = "disknetwork";

    private const string DiskPrefix = "disk:";
    private const string NetPrefix = "net";
    private const string BatteryPrefix = "battery:";
    private const string GpuPrefix = "gpu";
    private const string GpuIdentityPrefix = "gpu:";
    private const string GpuDevicePrefix = "gpu:dev:";
    private const string GpuNamePrefix = "gpu:name:";

    /// <summary>"disk:&lt;kernel name&gt;", the stable identity of a physical disk.</summary>
    public static string Disk(PhysicalDisk disk) =>
        disk.Name.Length > 0
            ? DiskPrefix + disk.Name
            : DiskPrefix + disk.Number.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A GPU's page key, by identity rather than position: the PCI address when
    /// the card has one, else the name of its resolved <c>device</c> link (a
    /// platform GPU with no PCI slot), else the model name with an ordinal
    /// counted among earlier cards of the same name. A card's page, history and
    /// remembered selection therefore stay with it when another card is added
    /// or removed.
    /// </summary>
    /// <param name="gpus">The adapters of one snapshot, in display order.</param>
    /// <param name="index">Where the card sits in <paramref name="gpus"/>. Only
    /// the same-name ordinal of the last fallback reads it; it is never the
    /// identity itself.</param>
    public static string Gpu(IReadOnlyList<Gpu> gpus, int index)
    {
        Gpu gpu = gpus[index];

        if (gpu.PciSlot.Length > 0)
        {
            return GpuIdentityPrefix + gpu.PciSlot.Trim().ToLowerInvariant();
        }

        if (gpu.DeviceId.Length > 0)
        {
            return GpuDevicePrefix + gpu.DeviceId.Trim();
        }

        int ordinal = 0;
        for (int i = 0; i < index; i++)
        {
            if (string.Equals(gpus[i].Name, gpu.Name, StringComparison.Ordinal))
            {
                ordinal++;
            }
        }
        return GpuNamePrefix + gpu.Name + "#" + ordinal.ToString(CultureInfo.InvariantCulture);
    }

    public static string Net(string id) => NetPrefix + id;

    public static string Battery(string id) => BatteryPrefix + id;

    /// <summary>
    /// True for every GPU key shape: the legacy positional "gpu0" (kept so an
    /// old config still parses; no page has that shape any more, so it
    /// reconciles to the overall page), "gpu:&lt;pci address&gt;",
    /// "gpu:dev:&lt;device id&gt;" and "gpu:name:&lt;name&gt;#&lt;n&gt;".
    /// </summary>
    public static bool IsGpuKey(string? key)
    {
        if (key is null || !key.StartsWith(GpuPrefix, StringComparison.Ordinal))
        {
            return false;
        }
        return (key.Length > GpuPrefix.Length
                && int.TryParse(key[GpuPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out _))
               || (key.StartsWith(GpuIdentityPrefix, StringComparison.Ordinal)
                   && key.Length > GpuIdentityPrefix.Length);
    }

    /// <summary>
    /// Whether a value has the shape of a page key. Every key
    /// <see cref="FullBoard"/> can produce is accepted — the fixed pages, the
    /// physical and grouped drives, GPUs, adapters, batteries and extension
    /// keys — so a remembered page from an older file is not thrown away. A
    /// key whose device is gone is still well formed; reconciliation is the
    /// page's business, not the file's.
    /// </summary>
    public static bool IsPageKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (key is Overall or Apps or Processes or Cpu or Ram or DiskVirtual or DiskNetwork)
        {
            return true;
        }

        if (HasBody(key, DiskPrefix) || HasBody(key, BatteryPrefix))
        {
            return true;
        }

        if (IsGpuKey(key))
        {
            return true;
        }

        if (key.StartsWith(NetPrefix, StringComparison.Ordinal) && key.Length > NetPrefix.Length)
        {
            return true;
        }

        return FullSelection.ExtensionPrefix(key).Length > 0;

        static bool HasBody(string value, string prefix) =>
            value.StartsWith(prefix, StringComparison.Ordinal) && value.Length > prefix.Length;
    }
}

/// <summary>
/// The highest value a card has shown since its page was selected.
///
/// A chart's "Highest" line must survive the rebuild that every sample causes,
/// but start again when the person opens another page. The first build after a
/// reset seeds from the history already on screen, so the figure is right even
/// when the page opens with 72 points behind it.
/// </summary>
public sealed class PeakTracker
{
    private readonly Dictionary<string, double> _highest = new(StringComparer.Ordinal);

    /// <summary>Forget every peak: a new page starts its own maxima.</summary>
    public void Reset() => _highest.Clear();

    public int Count => _highest.Count;

    public bool Contains(string key) => _highest.ContainsKey(key);

    /// <summary>The stored peak for a key, or null when nothing was tracked yet.</summary>
    public double? Highest(string key) => _highest.TryGetValue(key, out double value) ? value : null;

    /// <summary>
    /// Record one reading and return the highest so far. The first reading after
    /// a reset takes the visible history's own maximum, which is at least the
    /// current value.
    /// </summary>
    public double Track(string key, double value, double historyMax)
    {
        if (!double.IsFinite(value))
        {
            value = 0;
        }
        if (!double.IsFinite(historyMax))
        {
            historyMax = 0;
        }

        if (!_highest.TryGetValue(key, out double highest))
        {
            highest = Math.Max(historyMax, value);
            _highest[key] = highest;
            return highest;
        }

        if (value > highest)
        {
            highest = value;
            _highest[key] = highest;
        }
        return highest;
    }
}

/// <summary>
/// What the Main window is showing, apart from the pages' content: the selected
/// page, whether Settings is expanded and how wide the sidebar is.
///
/// Kept apart from the window for the same reason <see cref="ViewState"/> is:
/// the rules can be exercised without a display.
/// </summary>
public sealed class MainState
{
    public const int MinSidebarWidth = 200;
    public const int MaxSidebarWidth = 320;
    public const int DefaultSidebarWidth = 230;

    private string _current;
    private string _remembered;

    public MainState(
        string? page = null,
        bool settingsOpen = false,
        int sidebarWidth = DefaultSidebarWidth)
    {
        _remembered = string.IsNullOrWhiteSpace(page) ? PageKeys.Overall : page!;
        _current = _remembered;
        SettingsOpen = settingsOpen;
        SidebarWidth = ClampWidth(sidebarWidth);
    }

    /// <summary>The page in the right frame.</summary>
    public string Page => _current;

    /// <summary>The remembered key, kept even while its device is unplugged.</summary>
    public string RememberedPage => _remembered;

    /// <summary>The Settings expander, collapsed by default.</summary>
    public bool SettingsOpen { get; private set; }

    /// <summary>The sidebar width in DIPs, clamped 200-320 (default 230).</summary>
    public int SidebarWidth { get; private set; }

    /// <summary>Chart maxima since the page was selected (§2b "Highest").</summary>
    public PeakTracker Peaks { get; } = new();

    /// <summary>The person picked a page: it wins until the device disappears.</summary>
    public void Select(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }
        _current = key;
        _remembered = key;
        Peaks.Reset();
    }

    /// <summary>
    /// Reconcile with the pages that exist now. A vanished module falls back to
    /// the overall page, and the remembered key is kept so the page comes back
    /// when the device is replugged. Returns the page to show.
    /// </summary>
    public string Reconcile(IReadOnlyList<string> pages, string? remembered)
    {
        if (!string.IsNullOrWhiteSpace(remembered))
        {
            _remembered = remembered!;
        }

        if (pageListContains(pages, _remembered))
        {
            if (!string.Equals(_current, _remembered, StringComparison.Ordinal))
            {
                _current = _remembered;
                Peaks.Reset();
            }
            return _current;
        }

        if (!string.Equals(_current, PageKeys.Overall, StringComparison.Ordinal))
        {
            _current = pageListContains(pages, PageKeys.Overall)
                ? PageKeys.Overall
                : pages.Count > 0 ? pages[0] : PageKeys.Overall;
            Peaks.Reset();
        }
        return _current;

        static bool pageListContains(IReadOnlyList<string> keys, string key)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], key, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public void SetSettingsOpen(bool open) => SettingsOpen = open;

    public void SetSidebarWidth(int width) => SidebarWidth = ClampWidth(width);

    /// <summary>200-320, with a non-positive value meaning the default 230.</summary>
    public static int ClampWidth(int width) =>
        width <= 0 ? DefaultSidebarWidth : Math.Clamp(width, MinSidebarWidth, MaxSidebarWidth);
}
