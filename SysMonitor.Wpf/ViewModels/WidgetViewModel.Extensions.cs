using System.Windows.Media;
using SysMonitor.Extensibility;
using SysMonitor.Extensions;
using SysMonitor.Model;

namespace SysMonitor.ViewModels;

public sealed partial class WidgetViewModel
{
    private static Color SeverityColor(Severity severity) => severity switch
    {
        Severity.Good => Palette.TempOk,
        Severity.Caution => Palette.TempWarm,
        Severity.Bad => Palette.TempHot,
        _ => Palette.AccentDisk,
    };

    private void AddExtensionViews(Snapshot snap)
    {
        if (snap.Extensions == null || snap.Extensions.Count == 0)
        {
            return;
        }

        foreach (ExtensionViewData ext in snap.Extensions)
        {
            if (ext.View.WidgetPages != null && ext.View.WidgetPages.Count > 0)
            {
                for (int i = 0; i < ext.View.WidgetPages.Count; i++)
                {
                    _views.Add(("ext:" + ext.Id, i, null));
                }
            }
        }
    }

    private bool ExtensionViewsStale(Snapshot snap)
    {
        var have = _views.Where(v => v.Kind.StartsWith("ext:", StringComparison.Ordinal))
                         .Select(v => (v.Kind, v.From))
                         .ToList();

        var want = new List<(string Kind, int? From)>();
        if (snap.Extensions != null)
        {
            foreach (ExtensionViewData ext in snap.Extensions)
            {
                if (ext.View.WidgetPages != null && ext.View.WidgetPages.Count > 0)
                {
                    for (int i = 0; i < ext.View.WidgetPages.Count; i++)
                    {
                        want.Add(("ext:" + ext.Id, i));
                    }
                }
            }
        }

        return !have.SequenceEqual(want);
    }

    private void UpdateExtensionPage(Snapshot snap, string kind, int? index)
    {
        string extId = kind["ext:".Length..];
        ExtensionViewData? ext = snap.Extensions?.FirstOrDefault(e => string.Equals(e.Id, extId, StringComparison.OrdinalIgnoreCase));
        if (ext == null || ext.View.WidgetPages == null || index is null || index.Value < 0 || index.Value >= ext.View.WidgetPages.Count)
        {
            Message = _lang["no_data"];
            ShowMessage = true;
            return;
        }

        WidgetPage page = ext.View.WidgetPages[index.Value];
        if (!string.IsNullOrEmpty(page.Message))
        {
            Message = page.Message;
            ShowMessage = true;
            return;
        }

        Head(page.Title, page.Verdict, page.Line, page.TemperatureC, false);
        WidgetColumns = 1;
        Fill(WidgetRows, 1);
        Meter(WidgetRows[0], string.Empty, page.Percent, SeverityColor(page.Severity),
              string.Empty, null, false, compact: true);
    }

    private void FillExtensionSection(Section section, OverviewSection overview)
    {
        section.Columns = 1;
        section.Note = overview.Note ?? string.Empty;

        var rows = overview.Rows ?? Array.Empty<OverviewRow>();
        Fill(section.Rows, rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            OverviewRow item = rows[i];
            MeterRow row = section.Rows[i];
            Meter(row, item.Title, item.Percent, SeverityColor(item.Severity),
                  item.Detail ?? string.Empty, item.TemperatureC, false);
            row.ValueText = string.IsNullOrEmpty(item.ValueText) ? $"{item.Percent:F0}%" : item.ValueText;
        }
    }

    private void AddExtensionTabs(List<PlannedTab> wanted, IReadOnlyList<ExtensionViewData>? extensions)
    {
        if (extensions == null || extensions.Count == 0)
        {
            return;
        }

        foreach (ExtensionViewData ext in extensions)
        {
            if (ext.View.Tabs == null || ext.View.Tabs.Count == 0)
            {
                continue;
            }

            foreach (DetailTab tab in ext.View.Tabs)
            {
                string tabKey = $"ext:{ext.Id}:{tab.Key}";
                string title = tab.Title;
                string subtitle = tab.Subtitle ?? string.Empty;
                string detail = tab.Detail ?? string.Empty;
                string hardware = tab.Hardware ?? string.Empty;

                var plannedTab = new PlannedTab(tabKey, title, subtitle, detail: detail, hardware: hardware);

                if (tab.Facts != null && tab.Facts.Count > 0)
                {
                    foreach (Fact fact in tab.Facts)
                    {
                        plannedTab.Facts.Add(new DeviceFact
                        {
                            Name = fact.Name,
                            Value = fact.Value,
                        });
                    }
                }

                wanted.Add(plannedTab);
            }
        }
    }
}
