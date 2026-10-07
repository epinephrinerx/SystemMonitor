namespace SysMonitor;

/// <summary>
/// What the About page says about who made the program and on what terms.
///
/// In one place so a change of owner, year or licence is a one-line edit, and
/// so the page cannot drift from what the repository says. Nothing here is
/// legal text the project has not stated: the repository carries no licence
/// file, so the terms line is the default that applies when none is granted.
/// </summary>
internal static class AboutInfo
{
    public const string Product = "System Monitor";
    public const string Developer = "Apichart Chantanis";
    public const int Year = 2026;
    public const string Repository = "https://github.com/epinephrinerx/SystemMonitor";

    /// <summary>The project this one grew out of, credited by name and address.</summary>
    public const string UpstreamName = "epinephrinerx/SystemMonitor";

    public static string Copyright => $"© {Year} {Developer}";
}
