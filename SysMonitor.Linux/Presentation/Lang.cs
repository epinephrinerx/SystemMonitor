using System.Text;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The eight font sizes from spec section 1, multiplied by the user's font
/// scale. The scale is clamped 0.8-1.6 and rounded to 0.1 so the ramp cannot
/// produce a fractional-pixel soup of one-off sizes.
/// </summary>
public sealed class TypeRamp
{
    public const double MinScale = 0.8;
    public const double MaxScale = 1.6;

    public TypeRamp(double scale)
    {
        Scale = ClampScale(scale);
    }

    public double Scale { get; }

    public double Tiny => Sized(9);
    public double Small => Sized(10);
    public double Label => Sized(10);
    public double Body => Sized(11);
    public double Value => Sized(14);
    public double Heading => Sized(15);
    public double Nav => Sized(12);
    public double NavIcon => Sized(13);

    public static double ClampScale(double scale)
    {
        if (double.IsNaN(scale) || scale <= 0)
        {
            return 1.0;
        }
        return Math.Round(Math.Clamp(scale, MinScale, MaxScale), 1);
    }

    private double Sized(double baseSize) => Math.Round(baseSize * Scale, 1);
}

/// <summary>
/// The string table. Lookup is current language, then English, then the key
/// itself, so a missing translation is visible instead of blank.
/// </summary>
public sealed class Lang
{
    private readonly Dictionary<string, string> _thai = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _english = new(StringComparer.Ordinal);

    public Lang(string? code = "th")
    {
        foreach ((string key, string thai, string english) in StringData.Rows)
        {
            _thai[key] = thai;
            _english[key] = english;
        }
        foreach ((string key, string thai, string english) in StringData.LinuxExtras)
        {
            _thai[key] = thai;
            _english[key] = english;
        }
        foreach ((string key, string thai, string english) in StringData.BatteryKeys)
        {
            _thai[key] = thai;
            _english[key] = english;
        }
        foreach ((string key, string thai, string english) in StringData.ReleaseKeys)
        {
            _thai[key] = thai;
            _english[key] = english;
        }
        Code = IsKnown(code) ? code! : "th";
    }

    /// <summary>"th" or "en".</summary>
    public string Code { get; private set; }

    public static int KeyCount => StringData.Rows.Length;

    public static bool IsKnown(string? code) =>
        string.Equals(code, "th", StringComparison.OrdinalIgnoreCase)
        || string.Equals(code, "en", StringComparison.OrdinalIgnoreCase);

    /// <summary>Only "th" and "en" are accepted; anything else leaves the language alone.</summary>
    public bool Set(string? code)
    {
        if (!IsKnown(code))
        {
            return false;
        }
        Code = string.Equals(code, "th", StringComparison.OrdinalIgnoreCase) ? "th" : "en";
        return true;
    }

    /// <summary>True for a key the table knows, whether or not the current language has it.</summary>
    public bool Has(string key) =>
        string.Equals(Code, "th", StringComparison.OrdinalIgnoreCase)
            ? _thai.ContainsKey(key)
            : _english.ContainsKey(key);

    public string this[string key]
    {
        get
        {
            Dictionary<string, string> current =
                string.Equals(Code, "th", StringComparison.OrdinalIgnoreCase) ? _thai : _english;
            if (current.TryGetValue(key, out string? text))
            {
                return text;
            }
            if (_english.TryGetValue(key, out string? fallback))
            {
                return fallback;
            }
            return key;
        }
    }

    /// <summary>
    /// A formatted entry. Placeholders are the spec's {0}, {1} ... and numbers
    /// already formatted by <see cref="Format"/> stay language-neutral.
    /// </summary>
    public string Get(string key, params object?[] args)
    {
        string text = this[key];
        if (args.Length == 0)
        {
            return text;
        }
        try
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, text, args);
        }
        catch (FormatException)
        {
            return text;
        }
    }

    /// <summary>The widget title for a page, ready for the view's upper-casing.</summary>
    public string Title(string key) => this[key];
}

/// <summary>
/// Thai has no spaces between words, so a long label wraps wherever the
/// renderer feels like it -- usually mid-word. Inserting a zero-width space
/// before a leading vowel gives the breaker a legal place after the previous
/// syllable, which is where Thai readers expect it.
/// </summary>
public static class ThaiWrap
{
    private const char ZeroWidthSpace = '\u200B';

    public static string Soften(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        var builder = new StringBuilder(text.Length + 8);
        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            if (i > 0 && IsLeadingVowel(current) && IsThaiLetter(text[i - 1]))
            {
                builder.Append(ZeroWidthSpace);
            }
            builder.Append(current);
        }
        return builder.ToString();
    }

    /// <summary>เ แ โ ใ ไ -- the vowels that stand before their consonant.</summary>
    private static bool IsLeadingVowel(char value) => value is '\u0E40' or '\u0E41' or '\u0E42' or '\u0E43' or '\u0E44';

    /// <summary>
    /// A Thai consonant or vowel sign. The leading vowels are excluded: two of
    /// them in a row is not a syllable boundary, and a space before the first
    /// one is pointless.
    /// </summary>
    private static bool IsThaiLetter(char value) =>
        (value >= '\u0E01' && value <= '\u0E3A') || (value >= '\u0E47' && value <= '\u0E4E');
}
