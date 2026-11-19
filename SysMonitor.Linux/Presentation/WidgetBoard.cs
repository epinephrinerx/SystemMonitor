using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// Builds the widget's rotation pages from one snapshot (spec section 3).
///
/// The page list is CPU, RAM, disk and network in that order; the modes
/// (separated/total) decide how many pages each group contributes. There is no
/// GPU page in the widget. An empty list becomes one "no data" page so the
/// view always has something to show.
/// </summary>
public static class WidgetBoard
{
    /// <summary>The hard-coded "no reading" text the spec keeps untranslated.</summary>
    private const string NotAvailable = "n/a";

    public static IReadOnlyList<WidgetPage> Build(
        Snapshot? snapshot, AppConfig config, Lang lang, Palette palette, DiskAlertTracker alerts)
    {
        if (snapshot is null || !snapshot.Ready)
        {
            return new[]
            {
                new WidgetPage
                {
                    Key = "connecting",
                    Kind = WidgetPageKind.Empty,
                    Message = lang["connecting"],
                },
            };
        }

        var pages = new List<WidgetPage>();
        IReadOnlyList<Core> cores = snapshot.Cores;
        IReadOnlyList<Disk> disks = DeviceFilter.VisibleDisks(snapshot, config);
        IReadOnlyList<Adapter> adapters = snapshot.Adapters;

        if (config.ShowCpu)
        {
            AddCpuPages(pages, snapshot, cores, config, lang, palette);
        }

        if (config.ShowRam)
        {
            pages.Add(RamPage(snapshot, lang, palette));
        }

        if (config.ShowDisk)
        {
            if (string.Equals(config.DiskMode, "separated", StringComparison.OrdinalIgnoreCase))
            {
                // One page per drive. No drives, no page: the same rule the
                // network group follows, and the one the Windows code uses.
                foreach (Disk disk in disks)
                {
                    pages.Add(DiskPage(disk, lang, palette, alerts));
                }
            }
            else
            {
                pages.Add(DiskTotalPage(disks, lang, palette));
            }
        }

        if (config.ShowNetwork)
        {
            if (string.Equals(config.NetworkMode, "separated", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Adapter adapter in adapters)
                {
                    pages.Add(NetworkPage(adapter));
                }
                if (adapters.Count == 0)
                {
                    // "one page if there are any adapters": no adapters, no page.
                }
            }
            else if (adapters.Count > 0)
            {
                pages.Add(NetworkTotalPage(snapshot, lang));
            }
        }

        if (pages.Count == 0)
        {
            pages.Add(new WidgetPage
            {
                Key = "empty",
                Kind = WidgetPageKind.Empty,
                Message = lang["no_data"],
            });
        }

        return pages;
    }

    private static void AddCpuPages(
        List<WidgetPage> pages, Snapshot snapshot, IReadOnlyList<Core> cores,
        AppConfig config, Lang lang, Palette palette)
    {
        // cpu_temperature = false means the reading is not taken: no badge at
        // all, not an n/a badge (the same as the Windows build).
        TempBadge? badge = config.CpuTemperature
            ? palette.Badge(snapshot.CpuTemp, snapshot.CpuTempEstimated)
            : null;

        if (string.Equals(config.CpuMode, "separated", StringComparison.OrdinalIgnoreCase)
            && cores.Count > 0)
        {
            for (int from = 0; from < cores.Count; from += 4)
            {
                int to = Math.Min(from + 4, cores.Count);
                var rows = new List<MeterRow>(to - from);
                for (int i = from; i < to; i++)
                {
                    rows.Add(new MeterRow
                    {
                        Title = "C" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Percent = cores[i].Usage,
                        BarColour = Palette.LoadColour(cores[i].Usage, Accents.Cpu),
                        Compact = true,
                    });
                }

                pages.Add(new WidgetPage
                {
                    Key = $"cpu:{from}-{to - 1}",
                    Kind = WidgetPageKind.CpuGroup,
                    Title = $"{lang["cpu"]}  C{from}-{to - 1}",
                    Value = Format.Percent(snapshot.CpuTotal),
                    Badge = badge,
                    Columns = to - from > 2 ? 2 : 1,
                    Rows = rows,
                });
            }
            return;
        }

        var totalRows = new List<MeterRow>();
        if (cores.Count > 0)
        {
            // The page title already names the module, so the one compact bar
            // carries no label of its own -- the same as the Windows layout.
            totalRows.Add(new MeterRow
            {
                Title = string.Empty,
                Percent = snapshot.CpuTotal,
                BarColour = Palette.LoadColour(snapshot.CpuTotal, Accents.Cpu),
                Compact = true,
            });
        }

        pages.Add(new WidgetPage
        {
            Key = "cpu",
            Kind = WidgetPageKind.CpuTotal,
            Title = $"{lang["cpu"]} ({lang["total"]})",
            Value = cores.Count > 0 ? Format.Percent(snapshot.CpuTotal) : NotAvailable,
            Detail = cores.Count > 0 ? $"{cores.Count} {lang["cores"]}" : string.Empty,
            Badge = badge,
            Columns = 1,
            Rows = totalRows,
        });
    }

    private static WidgetPage RamPage(Snapshot snapshot, Lang lang, Palette palette)
    {
        Ram ram = snapshot.Ram;
        string value = ram.TotalGb > 0 ? Format.Percent(ram.Usage) : NotAvailable;
        var rows = new List<MeterRow>
        {
            new()
            {
                Title = string.Empty,
                Percent = ram.Usage,
                BarColour = Palette.LoadColour(ram.Usage, Accents.Ram),
                Compact = true,
            },
        };

        return new WidgetPage
        {
            Key = "ram",
            Kind = WidgetPageKind.Ram,
            Title = lang["memory"],
            Value = value,
            Detail = ram.TotalGb > 0
                ? Format.MemoryUse(ram.UsedGb, ram.TotalGb, lang)
                : string.Empty,
            // RAM temperature does not exist on a consumer board: always n/a.
            Badge = palette.Badge(null),
            Rows = rows,
        };
    }

    private static WidgetPage DiskPage(
        Disk disk, Lang lang, Palette palette, DiskAlertTracker alerts)
    {
        DiskAlert? alert = alerts.For(disk.Letter);
        string detail = alert?.Text(lang)
            ?? Format.DiskSpeedText(disk.ReadMb, disk.WriteMb, lang);

        return new WidgetPage
        {
            Key = "disk:" + disk.Letter,
            Kind = WidgetPageKind.DiskOne,
            Title = $"{lang["drive"]} {disk.Title}",
            Value = Format.Percent(disk.Usage),
            Detail = detail,
            Badge = palette.Badge(disk.Temp, disk.Estimated),
            Alert = alert,
            DriveKey = disk.Letter,
            Rows = new[]
            {
                new MeterRow
                {
                    Title = string.Empty,
                    Percent = disk.Usage,
                    BarColour = Palette.LoadColour(disk.Usage, Accents.Disk),
                    Compact = true,
                },
            },
        };
    }

    private static WidgetPage DiskTotalPage(IReadOnlyList<Disk> disks, Lang lang, Palette palette)
    {
        (double used, double total, double read, double write) = DiskTotals(disks);
        int percent = total > 0 ? (int)Math.Clamp(Math.Round(used / total * 100), 0, 100) : 0;
        int? maxTemp = MaxTemperature(disks, out bool estimated);

        var rows = new List<MeterRow>();
        if (total > 0)
        {
            rows.Add(new MeterRow
            {
                Title = string.Empty,
                Percent = percent,
                BarColour = Palette.LoadColour(percent, Accents.Disk),
                Compact = true,
            });
        }

        return new WidgetPage
        {
            Key = "disk",
            Kind = WidgetPageKind.DiskTotal,
            Title = $"{lang["disk"]} ({lang["all_drives"]})",
            Value = total > 0 ? Format.Percent(percent) : NotAvailable,
            Detail = total > 0 ? Format.DiskSpeedText(read, write, lang) : string.Empty,
            Badge = palette.Badge(maxTemp, estimated),
            Rows = rows,
        };
    }

    private static WidgetPage NetworkPage(Adapter adapter)
    {
        string name = adapter.Name.Length > 0 ? adapter.Name : adapter.Id;
        return new WidgetPage
        {
            Key = "net:" + adapter.Id,
            Kind = WidgetPageKind.NetworkOne,
            Title = name,
            Value = Format.Percent(adapter.Usage),
            Detail = Format.NetworkSpeedText(adapter.DownMb, adapter.UpMb),
            Rows = new[]
            {
                new MeterRow
                {
                    Title = string.Empty,
                    Percent = adapter.Usage,
                    BarColour = Palette.LoadColour(adapter.Usage, Accents.Net),
                    Compact = true,
                },
            },
        };
    }

    private static WidgetPage NetworkTotalPage(Snapshot snapshot, Lang lang)
    {
        int percent = Format.TotalLinkUsage(snapshot.Adapters);
        double down = snapshot.Adapters.Sum(a => a.DownMb);
        double up = snapshot.Adapters.Sum(a => a.UpMb);

        return new WidgetPage
        {
            Key = "net",
            Kind = WidgetPageKind.NetworkTotal,
            Title = $"{lang["network"]} ({lang["all_adapters"]})",
            Value = Format.Percent(percent),
            Detail = Format.NetworkSpeedText(down, up),
            Rows = new[]
            {
                new MeterRow
                {
                    Title = string.Empty,
                    Percent = percent,
                    BarColour = Palette.LoadColour(percent, Accents.Net),
                    Compact = true,
                },
            },
        };
    }

    private static (double Used, double Total, double Read, double Write) DiskTotals(IReadOnlyList<Disk> disks)
    {
        double used = 0, total = 0, read = 0, write = 0;
        foreach (Disk disk in disks)
        {
            used += disk.UsedGb;
            total += disk.TotalGb;
            read += disk.ReadMb;
            write += disk.WriteMb;
        }
        return (used, total, read, write);
    }

    private static int? MaxTemperature(IReadOnlyList<Disk> disks, out bool estimated)
    {
        int? max = null;
        estimated = false;
        foreach (Disk disk in disks)
        {
            if (disk.Temp is { } temp && (max is null || temp > max))
            {
                max = temp;
                estimated = disk.Estimated;
            }
        }
        return max;
    }
}
