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

        var extList = extensions
            .Where(e => e.View.Tabs != null && e.View.Tabs.Count > 0)
            .OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // The same placement rule as the overview sections (PlanExtensionPlacement).
        var (placed, appended) = PlanExtensionPlacement(
            extList, e => e.Id, e => e.PlaceAfter, idx => wanted.Any(t => TabBelongsToModule(t.Key, idx)));

        // Track how many extension tabs have already been inserted after each anchor (-1 = start)
        var insertedCounts = new Dictionary<int, int>();

        foreach (var (ext, anchor) in placed)
        {
            List<PlannedTab> tabs = BuildExtensionTabs(ext);
            int basePos = anchor < 0 ? 0 : wanted.FindLastIndex(t => TabBelongsToModule(t.Key, anchor)) + 1;
            insertedCounts.TryGetValue(anchor, out int already);
            wanted.InsertRange(basePos + already, tabs);
            insertedCounts[anchor] = already + tabs.Count;
        }

        foreach (ExtensionViewData ext in appended)
        {
            wanted.AddRange(BuildExtensionTabs(ext));
        }
    }

    private static bool TabBelongsToModule(string key, int module) => module switch
    {
        0 => key == "cpu",
        1 => key == "ram",
        2 => key.StartsWith("disk", StringComparison.Ordinal),
        3 => key.StartsWith("gpu", StringComparison.Ordinal),
        4 => key.StartsWith("net", StringComparison.Ordinal),
        _ => false,
    };

    private static List<PlannedTab> BuildExtensionTabs(ExtensionViewData ext)
    {
        var tabs = new List<PlannedTab>();
        foreach (DetailTab tab in ext.View.Tabs!)
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

            tabs.Add(plannedTab);
        }

        return tabs;
    }

    /// <summary>The five core modules in the order the app shows them; place_after names one of them.</summary>
    private static readonly string[] CanonicalModules = { "cpu", "ram", "disk", "gpu", "net" };

    private static int CanonicalIndex(string? placeAfter) => placeAfter?.ToLowerInvariant() switch
    {
        "cpu" => 0,
        "ram" => 1,
        "disk" => 2,
        "gpu" => 3,
        "network" or "net" => 4,
        _ => -1,
    };

    /// <summary>
    /// The one placement rule for overview sections and full-view tabs. An extension is anchored to its
    /// target module if that module is present, else to the last present module that precedes it in
    /// <see cref="CanonicalModules"/> (-1 means the start). Anchored extensions are returned ordered by the
    /// canonical position of their TARGET, then by id, so they can be inserted one after another behind
    /// their anchor. Extensions without a valid place_after go last, in id order.
    /// </summary>
    private static (List<(T Item, int Anchor)> Placed, List<T> Appended) PlanExtensionPlacement<T>(
        IEnumerable<T> items,
        Func<T, string> idOf,
        Func<T, string?> placeAfterOf,
        Func<int, bool> isPresent)
    {
        var anchored = new List<(T Item, int Anchor, int Target)>();
        var appended = new List<T>();

        foreach (T item in items)
        {
            int target = CanonicalIndex(placeAfterOf(item));
            if (target < 0)
            {
                appended.Add(item);
                continue;
            }

            int anchor = target;
            while (anchor >= 0 && !isPresent(anchor))
            {
                anchor--;
            }
            anchored.Add((item, anchor, target));
        }

        var placed = anchored
            .OrderBy(a => a.Target)
            .ThenBy(a => idOf(a.Item), StringComparer.OrdinalIgnoreCase)
            .Select(a => (a.Item, a.Anchor))
            .ToList();

        return (placed, appended.OrderBy(idOf, StringComparer.OrdinalIgnoreCase).ToList());
    }
}
