using System.Text.RegularExpressions;

namespace SysMonitor.Linux;

/// <summary>
/// Names that carry a hardware identifier, and how the probe prints them.
/// systemd derives a USB Ethernet adapter's name from its MAC address
/// (<c>enx00e04c68221a</c>).  The model keeps the real name because the window
/// on the owner's machine shows it, the way Resources does; the probe output is
/// meant to be pasted into a report, so it keeps only the <c>enx</c> prefix.
/// </summary>
public static class Privacy
{
    /// <summary>What an <c>enx&lt;MAC&gt;</c> name becomes: the prefix and an ellipsis.</summary>
    public const string MaskedInterface = "enx…";

    private static readonly Regex MacEmbeddedName = new(
        "^enx[0-9a-f]{12}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// <c>enx00e04c68221a</c> and <c>ENX00E04C68221A</c> → <c>enx…</c>; every
    /// other name, including a short <c>enx123</c>, is returned unchanged.
    /// </summary>
    public static string MaskInterface(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        return MacEmbeddedName.IsMatch(name) ? MaskedInterface : name;
    }

    /// <summary>
    /// The same masking for a list of adapters' names, in their order.  Two
    /// masks would read alike, so they get <c>#1</c>, <c>#2</c> appended; a
    /// single one stays bare.
    /// </summary>
    public static IReadOnlyList<string> MaskInterfaces(IReadOnlyList<string> names)
    {
        int masked = 0;
        for (int i = 0; i < names.Count; i++)
        {
            if (MacEmbeddedName.IsMatch(names[i]))
            {
                masked++;
            }
        }

        var result = new string[names.Count];
        int seen = 0;
        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            if (!MacEmbeddedName.IsMatch(name))
            {
                result[i] = name;
                continue;
            }

            seen++;
            result[i] = masked > 1 ? $"{MaskedInterface}#{seen}" : MaskedInterface;
        }

        return result;
    }
}
