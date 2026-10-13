using System.Windows.Media;
using SysMonitor.Model;

namespace SysMonitor.ViewModels;

/// <summary>
/// The SMART module: one rotating widget page, one overall section and one
/// full-view tab per physical disk. It is built from the same parts as the other
/// modules (tabs with facts, meter rows, the heading line) so it follows the
/// theme, the font scale and the language without any code of its own.
/// </summary>
public sealed partial class WidgetViewModel
{
    // ------------------------------------------------------------ wording
    private string HealthText(DiskHealth health) => health switch
    {
        DiskHealth.Good => _lang["smart_good"],
        DiskHealth.Caution => _lang["smart_caution"],
        DiskHealth.Bad => _lang["smart_bad"],
        _ => _lang["smart_unknown"],
    };

    /// <summary>Why there is no disk to show, in a sentence.</summary>
    private string SmartStatusText(SmartInfo info) => info.State switch
    {
        // Off here means the module was just switched on and the snapshot predates it.
        SmartState.Off or SmartState.Waiting => _lang["smart_waiting"],
        SmartState.NeedAdmin => _lang["smart_need_admin"],
        SmartState.Unavailable => _lang["smart_missing_core"],
        SmartState.Failed => _lang["smart_failed"],
        _ => _lang["smart_none"],
    };

    /// <summary>The colour a verdict is drawn in. Not theme-dependent, like the temperature badges.</summary>
    private static Color HealthColor(DiskHealth health) => health switch
    {
        DiskHealth.Good => Palette.TempOk,
        DiskHealth.Caution => Palette.TempWarm,
        DiskHealth.Bad => Palette.TempHot,
        _ => Palette.AccentDisk,
    };

    private string HoursText(long hours) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture,
                      _lang["smart_hours_fmt"], hours, hours / 24);

    /// <summary>"96% · 947 h": the figures that fit on a line under a heading.</summary>
    private string SmartLine(SmartDisk disk)
    {
        var parts = new List<string>();
        if (disk.LifePercent is int life)
        {
            parts.Add($"{_lang["smart_life"]} {life}%");
        }
        if (disk.PowerOnHours is long hours)
        {
            parts.Add(HoursText(hours));
        }
        return string.Join("  ·  ", parts);
    }

    private static string KindText(SmartDisk disk) =>
        string.Join(" ", new[] { disk.Interface, disk.Media }.Where(s => s.Length > 0));

    // --------------------------------------------------------------- pages
    /// <summary>How many rotating pages the module has: one per disk, or one saying why not.</summary>
    private static int SmartPageCount(Snapshot snap) =>
        snap.Smart.HasDisks ? snap.Smart.Disks.Count : 1;

    private void AddSmartViews(Snapshot snap)
    {
        if (snap.Smart.HasDisks)
        {
            for (int i = 0; i < snap.Smart.Disks.Count; i++)
            {
                _views.Add(("smart", i, null));
            }
        }
        else
        {
            _views.Add(("smart", null, null));
        }
    }

    /// <summary>
    /// The disks arrive after the first scan, and come and go with it. The page
    /// list was built before they did, so it is rebuilt when it no longer matches.
    /// Comparing the pages themselves and not just how many there are matters:
    /// one status page (no disk index) and one disk page are both "one page".
    /// </summary>
    private bool SmartViewsStale(Snapshot snap)
    {
        IEnumerable<int?> have = _views.Where(v => v.Kind == "smart").Select(v => v.From);
        if (!_config.ShowSmart)
        {
            return have.Any();
        }
        IEnumerable<int?> want = snap.Smart.HasDisks
            ? Enumerable.Range(0, snap.Smart.Disks.Count).Select(i => (int?)i)
            : new int?[] { null };
        return !have.SequenceEqual(want);
    }

    private void UpdateSmartPage(Snapshot snap, int? index)
    {
        SmartInfo info = snap.Smart;
        if (!info.HasDisks || index is null || index.Value >= info.Disks.Count)
        {
            // No row to draw: the page says why, the way the empty page does.
            Message = SmartStatusText(info);
            ShowMessage = true;
            return;
        }

        SmartDisk disk = info.Disks[index.Value];
        Head($"SMART {disk.Title}", HealthText(disk.Health), SmartLine(disk), disk.TempC);
        WidgetColumns = 1;
        Fill(WidgetRows, 1);
        Meter(WidgetRows[0], string.Empty, disk.LifePercent ?? 100, HealthColor(disk.Health),
              string.Empty, null, false, compact: true);
    }

    // ------------------------------------------------------------- overall
    private void FillSmartSection(Section section, SmartInfo info)
    {
        section.Columns = 1;
        if (!info.HasDisks)
        {
            section.Note = SmartStatusText(info);
            Fill(section.Rows, 0);
            return;
        }

        section.Note = string.Empty;
        Fill(section.Rows, info.Disks.Count);
        for (int i = 0; i < info.Disks.Count; i++)
        {
            SmartDisk disk = info.Disks[i];
            MeterRow row = section.Rows[i];
            string detail = string.Join("  ·  ", new[] { disk.Model, KindText(disk), SmartLine(disk) }
                                                    .Where(s => s.Length > 0));
            // The bar is the life left where the disk reports it. Where it does not (a
            // hard disk has no wear gauge) the bar is full in the verdict's colour, and the
            // text beside it says the verdict rather than a percentage.
            Meter(row, disk.Title, disk.LifePercent ?? 100, HealthColor(disk.Health),
                  detail, disk.TempC, false);
            row.ValueText = HealthText(disk.Health);
        }
    }

    // ---------------------------------------------------------- full view
    private void AddSmartTabs(List<PlannedTab> wanted, SmartInfo info)
    {
        if (!info.HasDisks)
        {
            string why = SmartStatusText(info);
            var status = new PlannedTab("smart", "SMART", string.Empty, detail: why);
            status.Facts.Add(new DeviceFact { Name = "SMART", Value = why });
            wanted.Add(status);
            return;
        }

        foreach (SmartDisk disk in info.Disks)
        {
            string detail = disk.TempC is int temp
                ? Palette.TempText(temp, false) + (disk.LifePercent is int l ? $"  ·  {l}%" : string.Empty)
                : (disk.LifePercent is int life ? $"{life}%" : string.Empty);

            var tab = new PlannedTab("smart" + disk.Drive, $"SMART {disk.Drive}",
                                     HealthText(disk.Health), detail: detail,
                                     hardware: disk.Model);

            tab.Facts.Add(new DeviceFact { Name = _lang["smart_health"], Value = HealthText(disk.Health) });
            if (disk.TempC is int t)
            {
                tab.Facts.Add(new DeviceFact { Name = _lang["temp_label"], Value = Palette.TempText(t, false) });
            }
            if (disk.LifePercent is int remaining)
            {
                tab.Facts.Add(new DeviceFact { Name = _lang["smart_life"], Value = remaining + "%" });
            }
            if (disk.PowerOnHours is long hours)
            {
                tab.Facts.Add(new DeviceFact { Name = _lang["smart_hours"], Value = HoursText(hours) });
            }
            if (KindText(disk).Length > 0)
            {
                tab.Facts.Add(new DeviceFact { Name = _lang["type"], Value = KindText(disk) });
            }
            if (disk.Serial.Length > 0)
            {
                tab.Facts.Add(new DeviceFact { Name = _lang["smart_serial"], Value = disk.Serial });
            }
            if (disk.Firmware.Length > 0)
            {
                tab.Facts.Add(new DeviceFact { Name = _lang["smart_firmware"], Value = disk.Firmware });
            }
            if (!disk.Full)
            {
                tab.Facts.Add(new DeviceFact { Name = "SMART", Value = _lang["smart_partial"] });
            }
            wanted.Add(tab);
        }
    }
}
