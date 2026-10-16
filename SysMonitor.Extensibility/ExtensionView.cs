namespace SysMonitor.Extensibility;

/// <summary>
/// A name-value pair describing a hardware property or metric.
/// </summary>
public sealed record Fact(string Name, string Value);

/// <summary>
/// Describes one rotating widget page for an extension.
/// If Message is not null or empty, the page displays the message text without meter bars.
/// </summary>
public sealed record WidgetPage(
    string Title,
    string Verdict,
    string Line,
    int? TemperatureC,
    int Percent,
    Severity Severity,
    string? Message = null);

/// <summary>
/// Describes one row in an extension's overview section.
/// </summary>
public sealed record OverviewRow(
    string Title,
    string Detail,
    string ValueText,
    int Percent,
    Severity Severity,
    int? TemperatureC);

/// <summary>
/// Describes the overview section for an extension in the overall view.
/// </summary>
public sealed record OverviewSection(
    string Note,
    IReadOnlyList<OverviewRow> Rows);

/// <summary>
/// Describes a tab in the full detail view for an extension.
/// </summary>
public sealed record DetailTab(
    string Key,
    string Title,
    string Subtitle,
    string Detail,
    string Hardware,
    IReadOnlyList<Fact> Facts);

/// <summary>
/// Aggregates all UI views provided by an extension across widget, overall, and full views.
/// </summary>
public sealed record ExtensionView(
    IReadOnlyList<WidgetPage> WidgetPages,
    OverviewSection? Overview,
    IReadOnlyList<DetailTab> Tabs)
{
    /// <summary>
    /// An empty view with no widget pages, no overview section, and no detail tabs.
    /// </summary>
    public static ExtensionView Empty { get; } = new(
        Array.Empty<WidgetPage>(),
        null,
        Array.Empty<DetailTab>());
}
