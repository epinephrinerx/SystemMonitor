using System.Windows;

namespace SysMonitor;

/// <summary>
/// The type ramp the font-size slider scales.
///
/// One table, used by the code that rewrites the resources and by the tests
/// that check nothing draws text outside it. Anything that sizes its text
/// from a literal instead of one of these keys does not follow the slider.
/// </summary>
internal static class FontRamp
{
    public const double Min = 0.8;
    public const double Max = 1.6;

    public static readonly (string Key, double Size)[] Entries =
    {
        ("FontTiny", 9), ("FontSmall", 10), ("FontLabel", 10),
        ("FontBody", 11), ("FontValue", 14), ("FontHeading", 15),
        // Sidebar buttons: the label and the glyph beside it.
        ("FontNav", 12), ("FontNavIcon", 13),
    };

    public static double Clamp(double scale) => Math.Clamp(scale, Min, Max);

    public static double Scaled(double size, double scale) =>
        Math.Round(size * Clamp(scale), 1);

    public static void Apply(ResourceDictionary resources, double scale)
    {
        foreach ((string key, double size) in Entries)
        {
            resources[key] = Scaled(size, scale);
        }
    }
}
