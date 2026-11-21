namespace SysMonitor.Linux.Presentation;

/// <summary>
/// An 8-bit RGBA colour. The presentation layer carries colours as values so
/// the whole theme can be checked without a window or a UI toolkit.
/// </summary>
public readonly record struct Colour(byte R, byte G, byte B, byte A = 255)
{
    public static Colour FromHex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        string text = hex.TrimStart('#');
        if (text.Length == 8)
        {
            return new Colour(
                Convert.ToByte(text[..2], 16),
                Convert.ToByte(text.Substring(2, 2), 16),
                Convert.ToByte(text.Substring(4, 2), 16),
                Convert.ToByte(text.Substring(6, 2), 16));
        }
        if (text.Length != 6)
        {
            throw new FormatException($"Not a colour: {hex}");
        }
        return new Colour(
            Convert.ToByte(text[..2], 16),
            Convert.ToByte(text.Substring(2, 2), 16),
            Convert.ToByte(text.Substring(4, 2), 16));
    }

    public string ToHex() => $"#{R:x2}{G:x2}{B:x2}";

    public string ToHexWithAlpha() => $"#{R:x2}{G:x2}{B:x2}{A:x2}";

    /// <summary>R+G+B, the test the spec uses to call a plot light or dark.</summary>
    public int Sum => R + G + B;

    public bool IsDarkPlot => Sum < 384;

    /// <summary>
    /// Replace the alpha with the panel opacity. The floor of 40/255 is there
    /// so the window never becomes invisible and ungrabbable.
    /// </summary>
    public Colour WithOpacity(double opacity)
    {
        double value = Math.Clamp(opacity, 0.35, 1.0);
        byte alpha = (byte)Math.Clamp(Math.Round(value * 255), 40, 255);
        return this with { A = alpha };
    }

    public Colour WithAlpha(byte alpha) => this with { A = alpha };

    public Colour Mix(Colour target, double amount)
    {
        double t = Math.Clamp(amount, 0, 1);
        byte Mix(byte a, byte b) => (byte)Math.Clamp(Math.Round(a + (b - a) * t), 0, 255);
        return new Colour(Mix(R, target.R), Mix(G, target.G), Mix(B, target.B), Mix(A, target.A));
    }
}

/// <summary>The theme tokens from spec section 6.</summary>
public sealed class Palette
{
    public required Colour Panel { get; init; }
    public required Colour Sidebar { get; init; }
    public required Colour Control { get; init; }
    public required Colour Chip { get; init; }
    public required Colour BarEmpty { get; init; }
    public required Colour Text { get; init; }
    public required Colour Muted { get; init; }
    public required Colour Label { get; init; }
    public required Colour Border { get; init; }
    public required Colour Tag { get; init; }
    public required Colour BadgeOk { get; init; }
    public required Colour BadgeWarm { get; init; }
    public required Colour BadgeHot { get; init; }
    public required Colour Plot { get; init; }
    public required Colour Grid { get; init; }

    /// <summary>Icon buttons, spec section 2.</summary>
    public static Colour IconButton => Colour.FromHex("#94a3b8");

    public static Colour Selection => Colour.FromHex("#3b82f6");

    public static Colour TabText => Colour.FromHex("#64748b");

    public static Colour Hover => Colour.FromHex("#33808080");

    public static Colour Pressed => Colour.FromHex("#55808080");

    public static Colour ScrollThumb => Colour.FromHex("#55808080");

    public static Colour ScrollThumbHover => Colour.FromHex("#99808080");

    public static Colour Start => Colour.FromHex("#22c55e");

    public static Colour Stop => Colour.FromHex("#ef4444");

    public static Palette Dark { get; } = new()
    {
        Panel = Colour.FromHex("#0f172a"),
        Sidebar = Colour.FromHex("#0b1220"),
        Control = Colour.FromHex("#1a2438"),
        Chip = Colour.FromHex("#1e293b"),
        BarEmpty = Colour.FromHex("#070c17"),
        Text = Colour.FromHex("#ffffff"),
        Muted = Colour.FromHex("#cbd5e1"),
        Label = Colour.FromHex("#94a3b8"),
        Border = Colour.FromHex("#2b374b"),
        Tag = Colour.FromHex("#243044"),
        BadgeOk = Colour.FromHex("#16351f"),
        BadgeWarm = Colour.FromHex("#3a3212"),
        BadgeHot = Colour.FromHex("#3d1717"),
        Plot = Colour.FromHex("#0b1220"),
        Grid = Colour.FromHex("#22304a"),
    };

    public static Palette Light { get; } = new()
    {
        Panel = Colour.FromHex("#ffffff"),
        Sidebar = Colour.FromHex("#f3f5f9"),
        Control = Colour.FromHex("#eef1f6"),
        Chip = Colour.FromHex("#e6eaf1"),
        BarEmpty = Colour.FromHex("#dfe4ec"),
        Text = Colour.FromHex("#1e293b"),
        Muted = Colour.FromHex("#64748b"),
        Label = Colour.FromHex("#475569"),
        Border = Colour.FromHex("#d5dae1"),
        Tag = Colour.FromHex("#e8ecf1"),
        BadgeOk = Colour.FromHex("#dcfce7"),
        BadgeWarm = Colour.FromHex("#fef3c7"),
        BadgeHot = Colour.FromHex("#fee2e2"),
        Plot = Colour.FromHex("#ffffff"),
        Grid = Colour.FromHex("#e2e6ec"),
    };

    /// <summary>Light for "light", dark for anything else, per spec section 6.</summary>
    public static Palette For(string? theme) =>
        string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase) ? Light : Dark;

    /// <summary>The panel colour with the configured opacity folded into alpha.</summary>
    public Colour PanelBrush(double opacity) => Panel.WithOpacity(opacity);

    /// <summary>The sidebar colour with the same opacity.</summary>
    public Colour SidebarBrush(double opacity) => Sidebar.WithOpacity(opacity);

    /// <summary>
    /// Usage-bar colour. 90 and 70 are the spec's thresholds and apply to every
    /// meter; a bar below 70 keeps its module accent.
    /// </summary>
    public static Colour LoadColour(int value, Colour accent) =>
        value >= 90 ? Colour.FromHex("#ef4444")
        : value >= 70 ? Colour.FromHex("#eab308")
        : accent;

    /// <summary>The temperature badge: colour pair and text for one reading.</summary>
    public TempBadge Badge(int? celsius, bool estimated = false)
    {
        if (celsius is not { } value)
        {
            return new TempBadge(Label, Tag, "n/a", false, false);
        }
        string text = (estimated ? "~" : string.Empty) + Format.Number(value) + "°C";
        if (value >= 80)
        {
            return new TempBadge(Colour.FromHex("#ef4444"), BadgeHot, text, true, estimated);
        }
        if (value >= 65)
        {
            return new TempBadge(Colour.FromHex("#eab308"), BadgeWarm, text, false, estimated);
        }
        return new TempBadge(Colour.FromHex("#22c55e"), BadgeOk, text, false, estimated);
    }
}

/// <summary>What a badge stands for, so the view can choose its glyph.</summary>
public enum BadgeKind
{
    /// <summary>A temperature reading: thermometer glyph plus "42°C".</summary>
    Temperature,

    /// <summary>A power source: lightning glyph, no text.</summary>
    Power,
}

/// <summary>One rendered badge.</summary>
public readonly record struct TempBadge(
    Colour Fore,
    Colour Back,
    string Text,
    bool Hot,
    bool Estimated,
    BadgeKind Kind = BadgeKind.Temperature);

/// <summary>The module accents. They do not change with the theme.</summary>
public static class Accents
{
    public static Colour Cpu => Colour.FromHex("#3b82f6");
    public static Colour Ram => Colour.FromHex("#a855f7");
    public static Colour Disk => Colour.FromHex("#10b981");
    public static Colour Net => Colour.FromHex("#06b6d4");
    public static Colour Gpu => Colour.FromHex("#f59e0b");
    public static Colour Battery => Colour.FromHex("#22c55e");
}
