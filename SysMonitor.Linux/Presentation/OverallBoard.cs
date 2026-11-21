using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// Builds the Overall view's section cards (spec section 4).
///
/// Sections come in a fixed order -- CPU, RAM, disk, GPU, network, battery --
/// with the module switches deciding what appears at all. Every adapter in
/// <see cref="Snapshot.Gpus"/> gets its own GPU card, titled with its name.
/// Rows are the same meter model the widget uses, so a bar behaves the same in
/// both views.
/// </summary>
public static class OverallBoard
{
    /// <summary>The hard-coded "no reading" text the spec keeps untranslated.</summary>
    private const string NotAvailable = "n/a";

    public static OverallModel Build(
        Snapshot? snapshot, AppConfig config, Lang lang, Palette palette, DiskAlertTracker alerts)
    {
        var sections = new List<SectionModel>();

        if (snapshot is not null)
        {
            if (config.ShowCpu)
            {
                sections.Add(CpuSection(snapshot, config, lang, palette));
            }
            if (config.ShowRam)
            {
                sections.Add(RamSection(snapshot, lang, palette));
            }
            if (config.ShowDisk)
            {
                sections.Add(DiskSection(snapshot, config, lang, palette, alerts));
            }
            if (config.ShowGpu)
            {
                foreach (Gpu gpu in snapshot.Gpus)
                {
                    sections.Add(GpuSection(gpu, lang, palette));
                }
            }
            if (config.ShowNetwork && snapshot.Adapters.Count > 0)
            {
                sections.Add(NetworkSection(snapshot, config, lang));
            }
            if (config.ShowBattery && snapshot.Batteries.Count > 0)
            {
                sections.Add(BatterySection(snapshot, lang, palette));
            }
        }

        return new OverallModel
        {
            Sections = sections,
            Legend = lang["temp_legend"],
            Message = sections.Count > 0
                ? string.Empty
                : snapshot is null || !snapshot.Ready ? lang["connecting"] : lang["no_data"],
        };
    }

    private static SectionModel CpuSection(
        Snapshot snapshot, AppConfig config, Lang lang, Palette palette)
    {
        IReadOnlyList<Core> cores = snapshot.Cores;
        bool perCore = string.Equals(config.CpuMode, "separated", StringComparison.OrdinalIgnoreCase)
                       && cores.Count > 0;

        // cpu_temperature = false means no reading: no note and no badge
        // anywhere in the CPU section, including the per-core one.
        TempBadge? badge = config.CpuTemperature
            ? palette.Badge(snapshot.CpuTemp, snapshot.CpuTempEstimated)
            : null;
        string note = !config.CpuTemperature || snapshot.CpuTemp is null
            ? string.Empty
            : Format.Temperature(snapshot.CpuTemp, snapshot.CpuTempEstimated);

        if (perCore)
        {
            var rows = new List<MeterRow>(cores.Count);
            for (int i = 0; i < cores.Count; i++)
            {
                rows.Add(new MeterRow
                {
                    Title = "C" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Percent = cores[i].Usage,
                    BarColour = Palette.LoadColour(cores[i].Usage, Accents.Cpu),
                });
            }

            return new SectionModel
            {
                Key = "cpu",
                Title = lang["cpu"],
                Note = note,
                Columns = 2,
                Rows = rows,
            };
        }

        int logical = cores.Count > 0 ? cores.Count : snapshot.CpuInfo.Logical;
        return new SectionModel
        {
            Key = "cpu",
            Title = lang["cpu"],
            Note = note,
            Columns = 1,
            Rows = new[]
            {
                new MeterRow
                {
                    Title = lang["cpu"],
                    Percent = snapshot.CpuTotal,
                    BarColour = Palette.LoadColour(snapshot.CpuTotal, Accents.Cpu),
                    Detail = logical > 0 ? $"{logical} {lang["cores"]}" : string.Empty,
                    Badge = badge,
                },
            },
        };
    }

    private static SectionModel RamSection(Snapshot snapshot, Lang lang, Palette palette)
    {
        Ram ram = snapshot.Ram;
        string memory = ram.TotalGb > 0
            ? $"{Format.Decimal(ram.UsedGb, 1)} / {Format.Decimal(ram.TotalGb, 1)} GB {lang["in_use"]}"
            : string.Empty;
        string modules = Module.Summarise(snapshot.Modules);

        return new SectionModel
        {
            Key = "ram",
            Title = lang["memory"],
            Columns = 1,
            Rows = new[]
            {
                new MeterRow
                {
                    Title = lang["memory"],
                    Percent = ram.Usage,
                    BarColour = Palette.LoadColour(ram.Usage, Accents.Ram),
                    Detail = Format.Join(memory, modules),
                    Badge = palette.Badge(null),
                },
            },
        };
    }

    private static SectionModel DiskSection(
        Snapshot snapshot, AppConfig config, Lang lang, Palette palette, DiskAlertTracker alerts)
    {
        IReadOnlyList<Disk> disks = DeviceFilter.VisibleDisks(snapshot, config);
        bool perDrive = string.Equals(config.DiskMode, "separated", StringComparison.OrdinalIgnoreCase)
                        && disks.Count > 0;

        if (perDrive)
        {
            var rows = new List<MeterRow>(disks.Count);
            foreach (Disk disk in disks)
            {
                DiskAlert? alert = alerts.For(disk.Letter);
                string detail = Format.Join(
                    disk.Media,
                    disk.Bus,
                    disk.TotalGb > 0
                        ? $"{Format.Decimal(disk.UsedGb, 0)}/{Format.Decimal(disk.TotalGb, 0)} GB ({Format.Percent(disk.Usage)})"
                        : string.Empty,
                    Format.DiskSpeedText(disk.ReadMb, disk.WriteMb, lang));

                rows.Add(new MeterRow
                {
                    Title = disk.Title,
                    Percent = disk.Usage,
                    BarColour = Palette.LoadColour(disk.Usage, Accents.Disk),
                    Detail = detail,
                    Badge = palette.Badge(disk.Temp, disk.Estimated),
                    Alert = alert,
                    DriveKey = disk.Letter,
                });
            }

            return new SectionModel
            {
                Key = "disk",
                Title = lang["disk"],
                Columns = 1,
                Rows = rows,
            };
        }

        double used = 0, total = 0, read = 0, write = 0;
        int? maxTemp = null;
        bool estimated = false;
        foreach (Disk disk in disks)
        {
            used += disk.UsedGb;
            total += disk.TotalGb;
            read += disk.ReadMb;
            write += disk.WriteMb;
            if (disk.Temp is { } temp && (maxTemp is null || temp > maxTemp))
            {
                maxTemp = temp;
                estimated = disk.Estimated;
            }
        }
        int percent = total > 0 ? (int)Math.Clamp(Math.Round(used / total * 100), 0, 100) : 0;

        return new SectionModel
        {
            Key = "disk",
            Title = lang["disk"],
            Columns = 1,
            Rows = new[]
            {
                new MeterRow
                {
                    Title = lang["all_drives"],
                    Percent = percent,
                    // With no capacity to divide by there is no percentage to
                    // show, and 0% would be invented; the widget shows n/a too.
                    ValueText = total > 0 ? null : NotAvailable,
                    BarColour = Palette.LoadColour(percent, Accents.Disk),
                    Detail = total > 0 ? Format.DiskSpeedText(read, write, lang) : string.Empty,
                    Badge = palette.Badge(maxTemp, estimated),
                },
            },
        };
    }

    private static SectionModel GpuSection(Gpu gpu, Lang lang, Palette palette)
    {
        string note = gpu.Temp is null
            ? string.Empty
            : Format.Temperature(gpu.Temp);

        // With a capacity to divide by, the memory reads as a fraction of the
        // card's own memory; otherwise only the used figure is worth showing.
        string dedicated = gpu.DedicatedTotalGb > 0
            ? $"{Format.Decimal(gpu.DedicatedGb, 1)} / {Format.Decimal(gpu.DedicatedTotalGb, 1)} GB {lang["dedicated"]}"
            : Prefixed(Format.GigabytesIfReal(gpu.DedicatedGb), lang["dedicated"]);

        var rows = new List<MeterRow>
        {
            new()
            {
                Title = lang["gpu"],
                Percent = gpu.Usage,
                // The GPU has no accent of its own below 70; spec says CPU blue.
                BarColour = Palette.LoadColour(gpu.Usage, Accents.Cpu),
                Detail = Format.Join(
                    dedicated,
                    Prefixed(Format.GigabytesIfReal(gpu.SharedGb), lang["shared"])),
                Badge = gpu.Temp is null ? null : palette.Badge(gpu.Temp),
            },
        };

        foreach (GpuEngine engine in gpu.Engines)
        {
            if (engine.Usage <= 0)
            {
                continue;
            }
            rows.Add(new MeterRow
            {
                Title = engine.Name,
                Percent = engine.Usage,
                BarColour = Palette.LoadColour(engine.Usage, Accents.Cpu),
            });
        }

        return new SectionModel
        {
            Key = "gpu",
            Title = gpu.Name.Length > 0 ? gpu.Name : lang["gpu"],
            Note = note,
            Columns = 1,
            Rows = rows,
        };
    }

    private static SectionModel NetworkSection(Snapshot snapshot, AppConfig config, Lang lang)
    {
        IReadOnlyList<Adapter> adapters = snapshot.Adapters;
        bool perAdapter = string.Equals(config.NetworkMode, "separated", StringComparison.OrdinalIgnoreCase);

        if (perAdapter)
        {
            var rows = new List<MeterRow>(adapters.Count);
            foreach (Adapter adapter in adapters)
            {
                string name = adapter.Name.Length > 0 ? adapter.Name : adapter.Id;
                string link = adapter.SpeedMbps > 0
                    ? Format.Number((int)Math.Round(adapter.SpeedMbps)) + " Mbps"
                    : lang["no_link"];
                rows.Add(new MeterRow
                {
                    Title = name,
                    Percent = adapter.Usage,
                    BarColour = Palette.LoadColour(adapter.Usage, Accents.Net),
                    Detail = Format.Join(
                        adapter.Wireless ? lang["wireless"] : lang["wired"],
                        link,
                        Format.NetworkSpeedText(adapter.DownMb, adapter.UpMb)),
                });
            }

            return new SectionModel
            {
                Key = "network",
                Title = lang["network"],
                Columns = 1,
                Rows = rows,
            };
        }

        int percent = Format.TotalLinkUsage(adapters);
        double down = adapters.Sum(a => a.DownMb);
        double up = adapters.Sum(a => a.UpMb);

        return new SectionModel
        {
            Key = "network",
            Title = lang["network"],
            Columns = 1,
            Rows = new[]
            {
                new MeterRow
                {
                    Title = lang["all_adapters"],
                    Percent = percent,
                    BarColour = Palette.LoadColour(percent, Accents.Net),
                    Detail = Format.NetworkSpeedText(down, up),
                },
            },
        };
    }

    private static SectionModel BatterySection(Snapshot snapshot, Lang lang, Palette palette)
    {
        var rows = new List<MeterRow>(snapshot.Batteries.Count + snapshot.Peripherals.Count);

        foreach (Battery battery in snapshot.Batteries)
        {
            rows.Add(new MeterRow
            {
                // The model names the cell when the driver knows it; otherwise
                // the module name is all there is to say.
                Title = battery.Model is { Length: > 0 } model ? model : lang["battery"],
                Percent = battery.Percent ?? 0,
                // A full cell is not a hot one, so the bar keeps the battery
                // accent at every level; LoadColour's 70/90 thresholds would
                // paint a healthy 100% red.
                BarColour = Accents.Battery,
                ValueText = BatteryText.Charge(battery, lang),
                Badge = BatteryText.AcBadge(battery, snapshot, palette),
                Detail = BatteryText.Detail(battery, snapshot, lang),
            });
        }

        // Peripheral cells are a footnote to the machine's own battery: one
        // small row each, kind first, the model under it and the charge on the
        // bar. They only appear when the section does.
        foreach (PeripheralBattery peripheral in snapshot.Peripherals)
        {
            rows.Add(new MeterRow
            {
                Title = BatteryText.PeripheralName(peripheral.Kind, lang),
                Percent = peripheral.Percent ?? 0,
                BarColour = Accents.Battery,
                ValueText = peripheral.Percent is int level ? Format.Percent(level) : lang["na"],
                Detail = peripheral.Model ?? string.Empty,
            });
        }

        return new SectionModel
        {
            Key = "battery",
            Title = lang["battery"],
            Columns = 1,
            Rows = rows,
        };
    }

    private static string Prefixed(string value, string prefix) =>
        value.Length == 0 ? string.Empty : $"{value} {prefix}";
}
