using System.Globalization;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// Number and unit formatting shared by every view. Everything goes through
/// the invariant culture so the text does not change shape with the locale --
/// the string table already carries the language.
/// </summary>
public static class Format
{
    /// <summary>"N%" -- percentages carry no decimals anywhere in the UI.</summary>
    public static string Percent(int value) =>
        value.ToString(CultureInfo.InvariantCulture) + "%";

    public static string Number(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    public static string Decimal(double value, int digits) =>
        value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>Disk and RAM sizes in the detail lines: "12.3 GB".</summary>
    public static string Gigabytes(double value, int digits = 1) =>
        Decimal(value, digits) + " GB";

    /// <summary>
    /// Speeds follow the Windows format: at 100 MB/s and above the decimal is
    /// noise, below it the decimal is the information.
    /// </summary>
    public static string Speed(double megabytesPerSecond) =>
        megabytesPerSecond >= 100
            ? Decimal(megabytesPerSecond, 0)
            : Decimal(megabytesPerSecond, 1);

    /// <summary>"R 1.2 | W 0.4 MB/s" (localised read/write labels).</summary>
    public static string DiskSpeedText(double read, double write, Lang lang) =>
        $"{lang["read"]} {Speed(read)} | {lang["write"]} {Speed(write)} MB/s";

    /// <summary>"↓ 1.2 | ↑ 0.4 MB/s".</summary>
    public static string NetworkSpeedText(double down, double up) =>
        $"↓ {Speed(down)} | ↑ {Speed(up)} MB/s";

    /// <summary>"2.1/7.7 GB in use".</summary>
    public static string MemoryUse(double usedGb, double totalGb, Lang lang) =>
        $"{Decimal(usedGb, 1)}/{Decimal(totalGb, 1)} GB {lang["in_use"]}";

    /// <summary>The spec's link-usage figure: both directions against the link rate.</summary>
    public static int LinkUsage(double downMb, double upMb, double linkMbps)
    {
        if (linkMbps <= 0)
        {
            return 0;
        }
        return (int)Math.Clamp(Math.Round((downMb + upMb) * 8 / linkMbps * 100), 0, 100);
    }

    /// <summary>The combined figure across adapters; 0 when no link speed is known.</summary>
    public static int TotalLinkUsage(IEnumerable<Model.Adapter> adapters)
    {
        double down = 0, up = 0, link = 0;
        foreach (Model.Adapter adapter in adapters)
        {
            down += adapter.DownMb;
            up += adapter.UpMb;
            link += adapter.SpeedMbps;
        }
        return LinkUsage(down, up, link);
    }

    /// <summary>"52°C", "~52°C" or "n/a".</summary>
    public static string Temperature(int? celsius, bool estimated = false) =>
        celsius is { } value
            ? (estimated ? "~" : string.Empty) + Number(value) + "°C"
            : "n/a";

    /// <summary>
    /// "x GB" only when there is something to say. The GPU detail leaves out a
    /// figure of zero and the parts it cannot read.
    /// </summary>
    public static string GigabytesIfReal(double value) =>
        value > 0.01 ? Decimal(value, 1) + " GB" : string.Empty;

    /// <summary>"128 KB" or "8.0 MB": cache sizes use the unit that stays short.</summary>
    public static string Kilobytes(int kilobytes) =>
        kilobytes >= 1024 ? Decimal(kilobytes / 1024.0, 1) + " MB" : Number(kilobytes) + " KB";

    /// <summary>Join the non-empty parts with the UI's middle dot.</summary>
    public static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!));

    /// <summary>Join with " · " but keep a labelled part's own separator.</summary>
    public static string JoinWith(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!));
}
