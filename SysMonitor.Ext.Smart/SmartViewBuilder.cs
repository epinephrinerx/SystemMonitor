using SysMonitor.Extensibility;

namespace SysMonitor.Ext.Smart;

/// <summary>
/// Pure builder that converts <see cref="SmartInfo"/> and a language string into an <see cref="ExtensionView"/>.
/// </summary>
internal static class SmartViewBuilder
{
    public static ExtensionView Build(SmartInfo info, string? lang)
    {
        string HealthText(DiskHealth health) => health switch
        {
            DiskHealth.Good => SmartStrings.Get("smart_good", lang),
            DiskHealth.Caution => SmartStrings.Get("smart_caution", lang),
            DiskHealth.Bad => SmartStrings.Get("smart_bad", lang),
            _ => SmartStrings.Get("smart_unknown", lang),
        };

        Severity HealthSeverity(DiskHealth health) => health switch
        {
            DiskHealth.Good => Severity.Good,
            DiskHealth.Caution => Severity.Caution,
            DiskHealth.Bad => Severity.Bad,
            _ => Severity.Neutral,
        };

        string HoursText(long hours) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture,
                          SmartStrings.Get("smart_hours_fmt", lang), hours, hours / 24);

        string SmartLine(SmartDisk disk)
        {
            var parts = new List<string>();
            if (disk.LifePercent is int life)
            {
                parts.Add($"{SmartStrings.Get("smart_life", lang)} {life}%");
            }
            if (disk.PowerOnHours is long hours)
            {
                parts.Add(HoursText(hours));
            }
            return string.Join("  ·  ", parts);
        }

        string KindText(SmartDisk disk) =>
            string.Join(" ", new[] { disk.Interface, disk.Media }.Where(s => s.Length > 0));

        string StatusText(SmartInfo sInfo) => sInfo.State switch
        {
            SmartState.Waiting => SmartStrings.Get("smart_waiting", lang),
            SmartState.NeedAdmin => SmartStrings.Get("smart_need_admin", lang),
            SmartState.Unavailable => SmartStrings.Get("smart_missing_core", lang),
            SmartState.Failed => SmartStrings.Get("smart_failed", lang),
            _ => SmartStrings.Get("smart_none", lang),
        };

        // 1. WidgetPages
        var widgetPages = new List<WidgetPage>();
        if (!info.HasDisks)
        {
            widgetPages.Add(new WidgetPage(
                Title: "SMART",
                Verdict: string.Empty,
                Line: string.Empty,
                TemperatureC: null,
                Percent: 0,
                Severity: Severity.Neutral,
                Message: StatusText(info)));
        }
        else
        {
            foreach (SmartDisk disk in info.Disks)
            {
                widgetPages.Add(new WidgetPage(
                    Title: $"SMART {disk.Title}",
                    Verdict: HealthText(disk.Health),
                    Line: SmartLine(disk),
                    TemperatureC: disk.TempC,
                    Percent: disk.LifePercent ?? 100,
                    Severity: HealthSeverity(disk.Health),
                    Message: null));
            }
        }

        // 2. OverviewSection
        OverviewSection overview;
        if (!info.HasDisks)
        {
            overview = new OverviewSection(
                Note: StatusText(info),
                Rows: Array.Empty<OverviewRow>());
        }
        else
        {
            var rows = new List<OverviewRow>(info.Disks.Count);
            foreach (SmartDisk disk in info.Disks)
            {
                string detail = string.Join("  ·  ", new[] { disk.Model, KindText(disk), SmartLine(disk) }
                                                        .Where(s => s.Length > 0));
                rows.Add(new OverviewRow(
                    Title: disk.Title,
                    Detail: detail,
                    ValueText: HealthText(disk.Health),
                    Percent: disk.LifePercent ?? 100,
                    Severity: HealthSeverity(disk.Health),
                    TemperatureC: disk.TempC));
            }
            overview = new OverviewSection(Note: string.Empty, Rows: rows);
        }

        // 3. DetailTabs
        var tabs = new List<DetailTab>();
        if (!info.HasDisks)
        {
            string why = StatusText(info);
            tabs.Add(new DetailTab(
                Key: "status",
                Title: "SMART",
                Subtitle: string.Empty,
                Detail: why,
                Hardware: string.Empty,
                Facts: new[] { new Fact("SMART", why) }));
        }
        else
        {
            foreach (SmartDisk disk in info.Disks)
            {
                string detail = disk.TempC is int temp
                    ? $"{temp}°C" + (disk.LifePercent is int l ? $"  ·  {l}%" : string.Empty)
                    : (disk.LifePercent is int life ? $"{life}%" : string.Empty);

                var facts = new List<Fact>
                {
                    new(SmartStrings.Get("smart_health", lang), HealthText(disk.Health))
                };

                if (disk.TempC is int t)
                {
                    facts.Add(new Fact(SmartStrings.Get("temp_label", lang), $"{t}°C"));
                }
                if (disk.LifePercent is int remaining)
                {
                    facts.Add(new Fact(SmartStrings.Get("smart_life", lang), remaining + "%"));
                }
                if (disk.PowerOnHours is long hours)
                {
                    facts.Add(new Fact(SmartStrings.Get("smart_hours", lang), HoursText(hours)));
                }
                if (KindText(disk).Length > 0)
                {
                    facts.Add(new Fact(SmartStrings.Get("type", lang), KindText(disk)));
                }
                if (disk.Serial.Length > 0)
                {
                    facts.Add(new Fact(SmartStrings.Get("smart_serial", lang), disk.Serial));
                }
                if (disk.Firmware.Length > 0)
                {
                    facts.Add(new Fact(SmartStrings.Get("smart_firmware", lang), disk.Firmware));
                }
                if (!disk.Full)
                {
                    facts.Add(new Fact("SMART", SmartStrings.Get("smart_partial", lang)));
                }

                tabs.Add(new DetailTab(
                    Key: $"disk{disk.Drive}",
                    Title: $"SMART {disk.Drive}",
                    Subtitle: HealthText(disk.Health),
                    Detail: detail,
                    Hardware: disk.Model,
                    Facts: facts));
            }
        }

        return new ExtensionView(widgetPages, overview, tabs);
    }
}
