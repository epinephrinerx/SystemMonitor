using System.Windows.Media;

namespace SysMonitor;

/// <summary>
/// Colours, carried over from the Tk build.
///
/// One thing changes here: Tk could only make a whole window uniformly
/// translucent, so the panel colours were pre-flattened and a colour key
/// supplied the transparency -- which is what made the resize grip
/// click-through.  WPF composites real per-pixel alpha, so the panel can be
/// translucent on its own and every pixel still takes input.
/// </summary>
public static class Palette
{
    public sealed record Colours(
        Color Panel, Color Sidebar, Color Control, Color Chip, Color BarEmpty,
        Color Text, Color Muted, Color Label, Color Border, Color Tag,
        Color BadgeOk, Color BadgeWarm, Color BadgeHot,
        Color Plot, Color Grid);

    private static Color Hex(string value) =>
        (Color)ColorConverter.ConvertFromString(value)!;

    public static readonly Colours Dark = new(
        Panel: Hex("#0f172a"),
        Sidebar: Hex("#0b1220"),
        Control: Hex("#1a2438"),     // cards and boxes sitting on the panel
        Chip: Hex("#1e293b"),        // controls sitting on the darker sidebar
        BarEmpty: Hex("#070c17"),
        Text: Hex("#ffffff"),
        Muted: Hex("#cbd5e1"),
        Label: Hex("#94a3b8"),
        Border: Hex("#2b374b"),
        Tag: Hex("#243044"),
        BadgeOk: Hex("#16351f"),
        BadgeWarm: Hex("#3a3212"),
        BadgeHot: Hex("#3d1717"),
        Plot: Hex("#0b1220"),
        Grid: Hex("#22304a"));

    public static readonly Colours Light = new(
        Panel: Hex("#ffffff"),
        Sidebar: Hex("#f3f5f9"),
        Control: Hex("#eef1f6"),
        Chip: Hex("#e6eaf1"),
        BarEmpty: Hex("#dfe4ec"),
        Text: Hex("#1e293b"),
        Muted: Hex("#64748b"),
        Label: Hex("#475569"),
        Border: Hex("#d5dae1"),
        Tag: Hex("#e8ecf1"),
        BadgeOk: Hex("#dcfce7"),
        BadgeWarm: Hex("#fef3c7"),
        BadgeHot: Hex("#fee2e2"),
        Plot: Hex("#ffffff"),
        Grid: Hex("#e2e6ec"));

    public static readonly Color AccentCpu = Hex("#3b82f6");
    public static readonly Color AccentRam = Hex("#a855f7");
    public static readonly Color AccentDisk = Hex("#10b981");
    public static readonly Color AccentNet = Hex("#06b6d4");

    /// <summary>Amber, the one hue not already spoken for by the other four.</summary>
    public static readonly Color AccentGpu = Hex("#f59e0b");

    // Temperature. These and the thresholds below belong to the thermometer
    // badge alone; the usage meters have their own set so a change to one can
    // never move the other.
    public static readonly Color TempOk = Hex("#22c55e");
    public static readonly Color TempWarm = Hex("#eab308");
    public static readonly Color TempHot = Hex("#ef4444");

    public const int WarmAt = 65;
    public const int HotAt = 80;

    // Usage: CPU, memory and disk bars.
    public static readonly Color LoadWarm = Hex("#eab308");
    public static readonly Color LoadHot = Hex("#ef4444");

    public const int LoadWarmAt = 70;
    public const int LoadHotAt = 90;

    public static Colours For(string name) => name == "light" ? Light : Dark;

    private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new();

    /// <summary>
    /// Above this the cache is emptied and refilled. The palette itself is a
    /// couple of dozen colours, so the cap is never reached in practice -- but
    /// the chart's colours come from public properties, and an animated or
    /// continually recoloured brush would otherwise add an entry per frame for
    /// the life of the process.
    /// </summary>
    private const int CacheLimit = 512;

    /// <summary>
    /// A frozen brush per colour.  The bars and badges re-evaluate their
    /// colours several times a second; allocating a brush each time pushes
    /// work onto the GC and forces WPF to re-realise the same resource.
    ///
    /// Locked because this is reached from the chart's render path as well as
    /// from the view model, and nothing stops a second WPF dispatcher having a
    /// chart of its own. An unsynchronised dictionary does not merely give the
    /// wrong answer under concurrent writes, it corrupts.
    /// </summary>
    public static SolidColorBrush Brush(Color color)
    {
        lock (BrushCache)
        {
            if (BrushCache.TryGetValue(color, out SolidColorBrush? brush))
            {
                return brush;
            }
            if (BrushCache.Count >= CacheLimit)
            {
                BrushCache.Clear();
            }
            brush = new SolidColorBrush(color);
            brush.Freeze();
            BrushCache[color] = brush;
            return brush;
        }
    }

    /// <summary>How many brushes are held. For tests; the cache is private.</summary>
    internal static int CachedBrushCount
    {
        get
        {
            lock (BrushCache)
            {
                return BrushCache.Count;
            }
        }
    }

    /// <summary>
    /// Bar colour by load: the meter's own accent until 70%, amber to 90%,
    /// then red. Every usage bar goes through this, so a drive at 79% reads
    /// as a warning rather than as a normal green bar that happens to be long.
    /// Nothing here is shared with the temperature badge.
    /// </summary>
    public static Color LoadColor(double value, Color? accent = null) =>
        value >= LoadHotAt ? LoadHot
        : value >= LoadWarmAt ? LoadWarm
        : accent ?? AccentCpu;

    /// <summary>(foreground, background) for a temperature badge.</summary>
    public static (Color Fore, Color Back) TempColors(int? temp, Colours pal)
    {
        if (temp is null)
        {
            return (pal.Label, pal.Tag);
        }
        if (temp >= HotAt)
        {
            return (TempHot, pal.BadgeHot);
        }
        if (temp >= WarmAt)
        {
            return (TempWarm, pal.BadgeWarm);
        }
        return (TempOk, pal.BadgeOk);
    }

    /// <summary>The label the widget shows for a temperature, or "n/a".</summary>
    public static string TempText(int? temp, bool estimated) =>
        temp is null ? "n/a" : (estimated ? "~" : string.Empty) + temp + "°C";

    public static bool IsHot(int? temp) => temp is not null && temp >= HotAt;
}
