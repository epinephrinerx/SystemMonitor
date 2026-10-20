using SysMonitor.Extensibility;

namespace SysMonitor.Ext.Sample;

/// <summary>
/// Sample third-party extension demonstrating external extension loading, UAC approval flow,
/// and process isolation without native code or host dependencies.
/// </summary>
public sealed class SampleExtension : IExtension, IViewProvider
{
    private IExtensionContext? _context;

    public void Initialize(IExtensionContext context)
    {
        _context = context;
    }

    public void Shutdown()
    {
        _context = null;
    }

    public string GetDisplayName(string language) =>
        SampleStrings.Get("name", language);

    public ExtensionView GetView(string language)
    {
        string langCode = string.Equals(language, "th", StringComparison.OrdinalIgnoreCase) ? "th" : "en";

        var widgetPages = new[]
        {
            new WidgetPage(
                Title: SampleStrings.Get("widget_title", language),
                Verdict: SampleStrings.Get("widget_verdict", language),
                Line: SampleStrings.Get("widget_line", language),
                TemperatureC: null,
                Percent: 100,
                Severity: Severity.Neutral,
                Message: string.Format(SampleStrings.Get("widget_message", language), langCode))
        };

        var overview = new OverviewSection(
            Note: string.Format(SampleStrings.Get("overview_note", language), langCode),
            Rows: new[]
            {
                new OverviewRow(
                    Title: SampleStrings.Get("overview_row_title", language),
                    Detail: string.Format(SampleStrings.Get("overview_row_detail", language), langCode),
                    ValueText: SampleStrings.Get("overview_row_val", language),
                    Percent: 100,
                    Severity: Severity.Good,
                    TemperatureC: null)
            });

        var tabs = new[]
        {
            new DetailTab(
                Key: "sample_tab",
                Title: SampleStrings.Get("tab_title", language),
                Subtitle: SampleStrings.Get("tab_subtitle", language),
                Detail: SampleStrings.Get("tab_detail", language),
                Hardware: SampleStrings.Get("tab_hw", language),
                Facts: new[]
                {
                    new Fact(SampleStrings.Get("fact_status", language), SampleStrings.Get("fact_status_val", language)),
                    new Fact(SampleStrings.Get("fact_lang", language), langCode),
                    new Fact(SampleStrings.Get("fact_type", language), SampleStrings.Get("fact_type_val", language))
                })
        };

        return new ExtensionView(widgetPages, overview, tabs);
    }
}
