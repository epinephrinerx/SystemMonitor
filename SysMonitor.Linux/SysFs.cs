using System.Globalization;

namespace SysMonitor.Linux;

/// <summary>
/// Small, total file helpers.  Every reader goes through these so a missing
/// file, an unreadable file or a deleted directory is one empty value rather
/// than an exception that ends the sample.
/// </summary>
internal static class SysFs
{
    private static readonly char[] Separators = { ' ', '\t', '\r' };

    public static string Path(string root, params string[] parts)
    {
        string path = string.IsNullOrEmpty(root) ? "/" : root;
        foreach (string part in parts)
        {
            path = System.IO.Path.Combine(path, part);
        }
        return path;
    }

    public static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? ReadText(string root, params string[] parts) =>
        ReadText(Path(root, parts));

    public static string? ReadTrimmed(string path) => ReadText(path)?.Trim();

    public static string? ReadTrimmed(string root, params string[] parts) =>
        ReadText(root, parts)?.Trim();

    public static long? ReadLong(string root, params string[] parts)
    {
        string? text = ReadTrimmed(root, parts);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : null;
    }

    public static int? ReadInt(string root, params string[] parts)
    {
        string? text = ReadTrimmed(root, parts);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    public static bool DirectoryExists(string root, params string[] parts)
    {
        try
        {
            return Directory.Exists(Path(root, parts));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Sensible, order-independent directory listing.  A directory that cannot
    /// be read yields an empty list.
    /// </summary>
    public static IReadOnlyList<string> Directories(string path)
    {
        try
        {
            string[] dirs = Directory.GetDirectories(path);
            Array.Sort(dirs, StringComparer.Ordinal);
            return dirs;
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public static IReadOnlyList<string> Directories(string root, params string[] parts) =>
        Directories(Path(root, parts));

    public static IReadOnlyList<string> Files(string path)
    {
        try
        {
            string[] files = Directory.GetFiles(path);
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Split a /proc or /sys line on runs of whitespace.  No allocation of an
    /// options array: these are read every tick.
    /// </summary>
    public static string[] Fields(string line) =>
        line.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Reject a name that could walk out of the root directory.</summary>
    public static bool IsSafeName(string name) =>
        name.Length > 0
        && name != "."
        && name != ".."
        && !name.Contains('/')
        && !name.Contains('\\')
        && !name.Contains('\0');

    public static bool TryParseLong(string text, out long value) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public static bool TryParseInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Decode the kernel's octal escapes: mountinfo writes a space as
    /// <c>\040</c> and a backslash as <c>\134</c>.
    /// </summary>
    public static string DecodeOctal(string text)
    {
        if (text.Length == 0 || text.IndexOf('\\') < 0)
        {
            return text;
        }

        var result = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\'
                && i + 3 < text.Length
                && text[i + 1] >= '0' && text[i + 1] <= '7'
                && text[i + 2] >= '0' && text[i + 2] <= '7'
                && text[i + 3] >= '0' && text[i + 3] <= '7')
            {
                result.Append((char)Convert.ToInt32(text.Substring(i + 1, 3), 8));
                i += 3;
                continue;
            }
            result.Append(text[i]);
        }
        return result.ToString();
    }

    /// <summary>Millidegrees to whole degrees, rejecting values no sensor can mean.</summary>
    public static int? Celsius(long? milliCelsius)
    {
        if (milliCelsius is not long milli || milli <= -20000 || milli >= 130000)
        {
            return null;
        }
        return (int)Math.Round(milli / 1000.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The basename of a symlink's final target.  Used for driver links and
    /// /proc/&lt;pid&gt;/exe; an unreadable link is empty, never an exception.
    /// </summary>
    public static string LinkTargetName(string linkPath)
    {
        string? target = LinkTarget(linkPath);
        if (string.IsNullOrEmpty(target))
        {
            return string.Empty;
        }

        string trimmed = target.EndsWith(" (deleted)", StringComparison.Ordinal)
            ? target[..^" (deleted)".Length]
            : target;
        trimmed = trimmed.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    public static string? LinkTarget(string linkPath)
    {
        try
        {
            return new FileInfo(linkPath).LinkTarget;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Real path of a directory link, or empty when it cannot be resolved.</summary>
    public static string ResolvedPath(string path)
    {
        try
        {
            FileSystemInfo? target = new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true);
            return target?.FullName ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
