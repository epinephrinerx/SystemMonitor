using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// Builds the Main window's module pages (R-030 §2b). The order is the sidebar's:
/// CPU, Memory, one page per GPU, one per physical disk, one per adapter, one
/// per battery. Every page has the same shape — header, options, Usage charts
/// with their peaks, Sensors and Properties — and every value that could not be
/// read says "N/A" rather than guessing.
/// </summary>
public static class FullBoard
{
    private const string RailSeparator = "  ·  ";
    private const string FactSeparator = " · ";

    public static FullModel Build(
        Snapshot? snapshot,
        AppConfig config,
        Lang lang,
        Palette palette,
        HistoryBoard history,
        FullSelection selection,
        string rememberedTab,
        PeakTracker? peaks = null)
    {
        var tabs = new List<FullTab>();
        if (snapshot is not null)
        {
            if (config.ShowCpu)
            {
                tabs.Add(CpuPage(snapshot, config, lang, history, peaks));
            }
            if (config.ShowRam)
            {
                tabs.Add(RamPage(snapshot, lang, history, peaks));
            }
            if (config.ShowGpu)
            {
                AddGpuPages(tabs, snapshot, lang, history, peaks);
            }
            if (config.ShowDisk)
            {
                AddDiskPages(tabs, snapshot, config, lang, history, peaks);
            }
            if (config.ShowNetwork)
            {
                AddNetworkPages(tabs, snapshot, config, lang, history, peaks);
            }
            if (config.ShowBattery)
            {
                AddBatteryPages(tabs, snapshot, lang, palette, history, peaks);
            }
        }

        string? selected = selection.Reconcile(tabs, rememberedTab);
        return new FullModel { Tabs = tabs, SelectedKey = selected };
    }

    // ------------------------------------------------------------------ CPU

    private static FullTab CpuPage(
        Snapshot snapshot, AppConfig config, Lang lang, HistoryBoard history, PeakTracker? peaks)
    {
        CpuInfo info = snapshot.CpuInfo;
        int cores = info.Cores > 0 ? info.Cores : snapshot.Cores.Count;
        int logical = info.Logical > 0 ? info.Logical : snapshot.Cores.Count;
        bool perCore = IsSeparated(config.CpuMode);

        var properties = new List<FullFact>();
        AddFact(properties, lang, lang["max_frequency"], Mhz(info.MaxMhz));
        AddFact(properties, lang, lang["logical_cores"], logical > 0 ? Format.Number(logical) : null);
        AddFact(properties, lang, lang["physical_cores"], cores > 0 ? Format.Number(cores) : null);
        AddFact(properties, lang, lang["sockets"], info.Sockets > 0 ? Format.Number(info.Sockets) : null);
        AddFact(properties, lang, lang["uptime"],
            snapshot.Uptime is { } uptime ? Format.Duration(uptime) : null);
        AddFact(properties, lang, lang["virtualisation"], Virtualisation(info, lang));
        AddFact(properties, lang, lang["architecture"], info.Architecture);
        AddFact(properties, lang, "L2", info.L2Kb > 0 ? Format.Kilobytes(info.L2Kb) : null);
        AddFact(properties, lang, "L3", info.L3Kb > 0 ? Format.Kilobytes(info.L3Kb) : null);

        var options = new[]
        {
            new ModuleOption("cpu_mode", "opt_logical_cpus", perCore),
            new ModuleOption("cpu_temperature", "cpu_temp", config.CpuTemperature),
        };

        string count = cores == logical || logical == 0
            ? $"{cores}C"
            : $"{cores}C/{logical}T";
        string detail = logical > 0 ? $"{logical} {lang["cores"]}" : string.Empty;
        if (config.CpuTemperature && snapshot.CpuTemp is int celsius)
        {
            detail = Format.Join(detail, Format.Temperature(celsius, snapshot.CpuTempEstimated));
        }

        var small = new List<FullCard>();
        if (perCore)
        {
            for (int i = 0; i < snapshot.Cores.Count; i++)
            {
                Core core = snapshot.Cores[i];
                // R-030 fix 4: the card's own line carries everything the core
                // reports — usage, the temperature when it is read, and the
                // clock — while the title is the short "C0" the Overall rows
                // already use. "Core 0" beside that value did not fit an 89 px
                // card.
                string value = Format.Join(
                    Format.Percent(core.Usage),
                    config.CpuTemperature && core.Temp is { } coreTemp
                        ? Format.Temperature(coreTemp)
                        : null,
                    Clock(core.Mhz));
                small.Add(new FullCard
                {
                    Key = "core" + i.ToString(CultureInfo.InvariantCulture),
                    Title = "C" + i.ToString(CultureInfo.InvariantCulture),
                    Value = value,
                    Accent = Accents.Cpu,
                    History = history.Core(i),
                    Small = true,
                });
            }
        }

        var sensors = new List<FullCard>();
        if (config.CpuTemperature && snapshot.CpuTemp is int cpuTemp)
        {
            sensors.Add(SensorCard(
                "cpu:temp",
                lang["temp_label"],
                Format.Temperature(cpuTemp, snapshot.CpuTempEstimated),
                cpuTemp,
                history.CpuTemp,
                peaks,
                lang,
                Accents.Cpu));
        }

        return new FullTab
        {
            Key = PageKeys.Cpu,
            Title = lang["cpu"],
            Label = lang["cpu"],
            Detail = Format.Join(count, info.BaseMhz > 0 ? Format.Decimal(info.BaseMhz / 1000.0, 2) + " GHz" : null),
            Summary = Format.Percent(snapshot.CpuTotal),
            Hardware = info.Name,
            Options = options,
            Properties = properties,
            BigCards = new[]
            {
                Card(
                    PageKeys.Cpu, lang["cpu"], Format.Percent(snapshot.CpuTotal), detail,
                    lang["utilisation"], 100, Accents.Cpu, history.Cpu,
                    peaks, lang, snapshot.CpuTotal, Percent),
            },
            SmallCards = small,
            Sensors = sensors,
        };
    }

    // -------------------------------------------------------------- memory

    private static FullTab RamPage(
        Snapshot snapshot, Lang lang, HistoryBoard history, PeakTracker? peaks)
    {
        Ram ram = snapshot.Ram;
        string detail = ram.TotalGb > 0
            ? Format.Gigabytes(ram.UsedGb) + " / " + Format.Gigabytes(ram.TotalGb)
            : string.Empty;
        string summary = ram.TotalGb > 0
            ? $"{Format.Decimal(ram.UsedGb, 1)}/{Format.Decimal(ram.TotalGb, 1)} GB"
            : Format.Percent(ram.Usage);

        var big = new List<FullCard>
        {
            Card(
                "memory", lang["memory"], Format.Percent(ram.Usage),
                ram.TotalGb > 0
                    ? $"{Format.Decimal(ram.UsedGb, 1)} / {Format.Decimal(ram.TotalGb, 1)} GB {lang["in_use"]}"
                    : string.Empty,
                lang["utilisation"], 100, Accents.Ram, history.Ram,
                peaks, lang, ram.Usage, Percent),
        };

        if (ram.SwapTotalBytes > 0)
        {
            double usedGb = ram.SwapUsedBytes / 1073741824.0;
            double totalGb = ram.SwapTotalBytes / 1073741824.0;
            big.Add(Card(
                "swap", lang["swap"], Format.Percent(ram.SwapUsage),
                $"{Format.Decimal(usedGb, 1)} / {Format.Decimal(totalGb, 1)} GB",
                lang["utilisation"], 100, Accents.Ram, history.Swap,
                peaks, lang, ram.SwapUsage, Percent));
        }

        var properties = new List<FullFact>();
        MemoryLayout layout = snapshot.MemoryLayout;
        if (layout.Modules.Count == 0 && layout.SlotsTotal is null)
        {
            properties.Add(new FullFact
            {
                Title = lang["ram_properties"],
                Value = lang["hw_unavailable"],
            });
        }
        else
        {
            int used = layout.SlotsUsed ?? layout.Modules.Count;
            AddFact(properties, lang, lang["slots_used"],
                layout.SlotsTotal is int slots ? $"{used} {lang["of_slots"]} {slots}" : null);
            AddFact(properties, lang, lang["max_capacity"],
                layout.MaxCapacityBytes is long capacity ? Format.Bytes(capacity) : null);
            for (int i = 0; i < layout.Modules.Count; i++)
            {
                MemoryModule module = layout.Modules[i];
                properties.Add(new FullFact
                {
                    Title = module.Slot.Length > 0
                        ? module.Slot
                        : lang.Get("module_n", i + 1),
                    Value = ModuleDetail(module, lang),
                });
            }
        }

        if (ram.SwapTotalBytes <= 0)
        {
            properties.Add(new FullFact { Title = lang["swap"], Value = lang["swap_none"] });
        }
        else
        {
            foreach (SwapDevice device in snapshot.SwapDevices)
            {
                properties.Add(new FullFact
                {
                    Title = device.Name,
                    Value = string.Join(
                        FactSeparator,
                        device.Type,
                        $"{Format.Bytes(device.UsedBytes)} / {Format.Bytes(device.SizeBytes)}",
                        $"{lang["priority"]} {device.Priority.ToString(CultureInfo.InvariantCulture)}"),
                });
            }
        }

        string hardware = layout.SlotsTotal is int total
            ? $"{layout.SlotsUsed ?? layout.Modules.Count} {lang["of_slots"]} {total} {lang["slots"]}"
            : Module.Summarise(snapshot.Modules);

        return new FullTab
        {
            Key = PageKeys.Ram,
            Title = lang["memory"],
            Label = lang["module_memory"],
            Detail = detail,
            Summary = summary,
            Hardware = hardware,
            Properties = properties,
            BigCards = big,
        };
    }

    /// <summary>One module's line: size, type, form factor, speed, maker, rank, voltage.</summary>
    private static string ModuleDetail(MemoryModule module, Lang lang)
    {
        string? speed = module.ConfiguredSpeedMts is int configured
            ? module.SpeedMts is int rated && rated > 0
                ? $"{configured} MT/s (rated {rated})"
                : $"{configured} MT/s"
            : module.SpeedMts is int only ? $"{only} MT/s" : null;

        var parts = new List<string?>
        {
            module.SizeBytes is long size ? Format.Bytes(size) : null,
            Format.Join(module.Type, module.TypeDetail),
            module.FormFactor,
            speed,
            module.Manufacturer,
            module.Rank is int rank ? $"{lang["rank"]} {rank.ToString(CultureInfo.InvariantCulture)}" : null,
            module.ConfiguredVoltage is double volts ? Format.Volts(volts) : null,
        };
        return JoinParts(parts);
    }

    // ----------------------------------------------------------------- disks

    private static void AddDiskPages(
        List<FullTab> tabs,
        Snapshot snapshot,
        AppConfig config,
        Lang lang,
        HistoryBoard history,
        PeakTracker? peaks)
    {
        IReadOnlyList<Disk> disks = DeviceFilter.VisibleDisks(snapshot, config);

        // Two mounts of one disk share a page. The kernel name is the identity,
        // so a disk keeps its page key and its remembered selection after a
        // replug; Number and Model alone would both move around.
        var physical = new Dictionary<string, DiskGroup>(StringComparer.Ordinal);
        var virtualDrives = new List<Disk>();
        var networkDrives = new List<Disk>();

        foreach (Disk disk in disks)
        {
            if (disk.IsNetwork)
            {
                networkDrives.Add(disk);
            }
            else if (disk.Detail?.Disk is { } hardware)
            {
                string key = PageKeys.Disk(hardware);
                if (!physical.TryGetValue(key, out DiskGroup? group))
                {
                    group = new DiskGroup(hardware, new List<Disk>());
                    physical[key] = group;
                }
                group.Mounts.Add(disk);
            }
            else
            {
                virtualDrives.Add(disk);
            }
        }

        // Root disk first, then the other internal disks, then removable/USB;
        // each group by kernel name.
        List<DiskGroup> ordered = physical.Values
            .OrderBy(group => DiskRank(group.Mounts))
            .ThenBy(group => group.Disk.Name, StringComparer.Ordinal)
            .ThenBy(group => group.Disk.Number)
            .ToList();

        for (int index = 0; index < ordered.Count; index++)
        {
            PhysicalDisk hardware = ordered[index].Disk;
            List<Disk> mounts = ordered[index].Mounts
                .OrderBy(d => d.Letter, StringComparer.Ordinal)
                .ToList();
            string key = PageKeys.Disk(hardware);
            string diskName = lang.Get("module_disk_n", index);

            var properties = new List<FullFact>();
            AddFact(properties, lang, lang["type"],
                hardware.Kind.Length > 0 ? hardware.Kind : hardware.Media);
            AddFact(properties, lang, lang["device"], hardware.Name);
            AddFact(properties, lang, lang["capacity"],
                hardware.Bytes > 0 ? Format.Bytes(hardware.Bytes) : null);
            AddFact(properties, lang, lang["writable"], YesNo(hardware.Writable, lang));
            AddFact(properties, lang, lang["removable"], YesNo(hardware.Removable, lang));
            AddFact(properties, lang, lang["link"], hardware.Link);
            AddFact(properties, lang, lang["total_read"],
                hardware.TotalReadBytes is long read ? Format.Bytes(read) : null);
            AddFact(properties, lang, lang["total_written"],
                hardware.TotalWrittenBytes is long written ? Format.Bytes(written) : null);

            var options = new[]
            {
                new ModuleOption("disk_mode", "per_drive", IsSeparated(config.DiskMode)),
                new ModuleOption("include_network", "show_network_drives", config.IncludeNetwork),
            };

            double readMb = hardware.ReadBytesPerSec / 1048576.0;
            double writeMb = hardware.WriteBytesPerSec / 1048576.0;
            double? activity = hardware.ActivityPercent;

            var big = new List<FullCard>
            {
                Card(
                    key + ":activity", lang["disk_activity"],
                    activity is double percent ? Percent(percent) : lang["na"],
                    string.Empty, lang["utilisation"], 100, Accents.Disk,
                    history.DiskActivity(key),
                    peaks, lang, activity ?? double.NaN, Percent),
                Card(
                    key + ":read", lang["read_speed"],
                    Format.Speed(readMb) + " MB/s", string.Empty, "MB/s", 0, Accents.Disk,
                    history.DiskRead(key),
                    peaks, lang, readMb, Rate),
                Card(
                    key + ":write", lang["write_speed"],
                    Format.Speed(writeMb) + " MB/s", string.Empty, "MB/s", 0, Accents.Disk,
                    history.DiskWrite(key),
                    peaks, lang, writeMb, Rate),
            };

            int? hottest = null;
            foreach (Disk mount in mounts)
            {
                if (mount.Temp is int temp && (hottest is null || temp > hottest))
                {
                    hottest = temp;
                }
            }
            var sensors = new List<FullCard>();
            if (hottest is int celsius)
            {
                sensors.Add(SensorCard(
                    key + ":temp", lang["temp_label"], Format.Temperature(celsius),
                    celsius, history.DiskTemp(key), peaks, lang, Accents.Disk));
            }

            tabs.Add(new FullTab
            {
                Key = key,
                Title = "Disk " + index.ToString(CultureInfo.InvariantCulture),
                Label = diskName,
                Detail = DiskRailDetail(hardware, lang),
                Summary = string.Join(
                    FactSeparator,
                    $"{lang["read"]} {Format.ByteRate(hardware.ReadBytesPerSec)}",
                    $"{lang["write"]} {Format.ByteRate(hardware.WriteBytesPerSec)}"),
                Hardware = hardware.Model,
                Options = options,
                Properties = properties,
                BigCards = big,
                SmallCards = mounts.Select(mount => DriveCard(mount, lang, history)).ToList(),
                Sensors = sensors,
                IsDisk = true,
                Mounts = mounts.Select(m => m.Letter).ToList(),
            });
        }

        if (virtualDrives.Count > 0)
        {
            tabs.Add(DriveGroupTab(
                PageKeys.DiskVirtual, lang["virtual_drives"], virtualDrives, lang, history));
        }
        if (networkDrives.Count > 0)
        {
            tabs.Add(DriveGroupTab(
                PageKeys.DiskNetwork, lang["network_drives"], networkDrives, lang, history));
        }
    }

    /// <summary>0 = holds "/", 1 = another internal disk, 2 = removable/USB.</summary>
    private static int DiskRank(IReadOnlyList<Disk> mounts)
    {
        if (mounts.Any(m => m.Detail?.IsBoot == true
                            || string.Equals(m.Letter, "/", StringComparison.Ordinal)))
        {
            return 0;
        }

        return mounts.Any(m =>
            string.Equals(m.Bus, "USB", StringComparison.OrdinalIgnoreCase)
            || string.Equals(m.Media, "Removable", StringComparison.OrdinalIgnoreCase))
            ? 2
            : 1;
    }

    private sealed record DiskGroup(PhysicalDisk Disk, List<Disk> Mounts);

    private static FullTab DriveGroupTab(
        string key, string title, List<Disk> drives, Lang lang, HistoryBoard history)
    {
        List<Disk> ordered = drives
            .OrderBy(d => d.Letter, StringComparer.Ordinal)
            .ToList();
        return new FullTab
        {
            Key = key,
            Title = title,
            Label = title,
            Detail = $"{ordered.Count} {lang["drives"]}",
            Summary = string.Join(FactSeparator, ordered.Select(d => d.Letter)),
            BigCards = ordered.Select(drive => DriveCard(drive, lang, history)).ToList(),
        };
    }

    /// <summary>
    /// The rail line: media (bus), size, online and, when there is any, the
    /// unallocated tail. The model's own numbers, one line.
    /// </summary>
    public static string DiskRailDetail(PhysicalDisk disk, Lang lang)
    {
        string media = disk.Media.Length > 0 ? disk.Media : "Disk";
        string bus = disk.Bus.Length > 0 ? disk.Bus : "Unknown";
        var parts = new List<string>
        {
            $"{media} ({bus})",
            Format.Decimal(disk.Gb, 1) + " GB",
            lang[disk.Online ? "online" : "offline"],
        };
        if (disk.UnallocatedGb > 0.05)
        {
            parts.Add(Format.Decimal(disk.UnallocatedGb, 1) + " GB " + lang["unallocated"]);
        }
        return string.Join(RailSeparator, parts);
    }

    /// <summary>One mounted volume under its disk page: a small card.</summary>
    private static FullCard DriveCard(Disk disk, Lang lang, HistoryBoard history)
    {
        string volume = disk.Detail?.FileSystem ?? string.Empty;
        string space = disk.TotalGb > 0
            ? $"{Format.Decimal(disk.UsedGb, 0)} / {Format.Decimal(disk.TotalGb, 0)} GB ({Format.Percent(disk.Usage)})"
            : string.Empty;
        string? temperature = disk.Temp is null
            ? null
            : Format.Temperature(disk.Temp, disk.Estimated);

        string detail = Format.Join(
            volume,
            space,
            Format.DiskSpeedText(disk.ReadMb, disk.WriteMb, lang),
            temperature);

        return new FullCard
        {
            Key = "diskio" + disk.Letter,
            Title = disk.Title,
            Value = Format.Speed(disk.ReadMb + disk.WriteMb) + " MB/s",
            Detail = detail,
            Unit = "MB/s",
            Ceiling = ChartMath.Ceiling(0, history.DiskThroughput(disk.Letter).Max, "MB/s"),
            Maximum = 0,
            Accent = Accents.Disk,
            History = history.DiskThroughput(disk.Letter),
            Small = true,
        };
    }

    // ------------------------------------------------------------------ GPU

    private static void AddGpuPages(
        List<FullTab> tabs, Snapshot snapshot, Lang lang, HistoryBoard history, PeakTracker? peaks)
    {
        for (int index = 0; index < snapshot.Gpus.Count; index++)
        {
            Gpu gpu = snapshot.Gpus[index];
            if (!gpu.Present && gpu.Name.Length == 0)
            {
                continue;
            }

            string key = PageKeys.Gpu(snapshot.Gpus, index);
            var big = new List<FullCard>
            {
                Card(
                    key, lang["gpu"], Format.Percent(gpu.Usage),
                    string.Join(RailSeparator, gpu.Engines.Select(e => $"{e.Name} {Format.Percent(e.Usage)}")),
                    lang["utilisation"], 100, Accents.Gpu, history.GpuUsage(key),
                    peaks, lang, gpu.Usage, Percent),
            };

            if (gpu.EncoderUsage is int encoder)
            {
                big.Add(Card(
                    key + ":encoder", lang["video_encoder"], Format.Percent(encoder),
                    string.Empty, lang["utilisation"], 100, Accents.Gpu,
                    history.GpuEncoder(key), peaks, lang, encoder, Percent));
            }
            if (gpu.DecoderUsage is int decoder)
            {
                big.Add(Card(
                    key + ":decoder", lang["video_decoder"], Format.Percent(decoder),
                    string.Empty, lang["utilisation"], 100, Accents.Gpu,
                    history.GpuDecoder(key), peaks, lang, decoder, Percent));
            }
            if (gpu.DedicatedTotalGb > 0)
            {
                int used = (int)Math.Clamp(
                    Math.Round(gpu.DedicatedGb / gpu.DedicatedTotalGb * 100), 0, 100);
                big.Add(Card(
                    key + ":memory", lang["video_memory"], Format.Percent(used),
                    $"{Format.Decimal(gpu.DedicatedGb, 1)} / {Format.Decimal(gpu.DedicatedTotalGb, 1)} GB",
                    lang["utilisation"], 100, Accents.Gpu, history.GpuMemory(key),
                    peaks, lang, used, Percent));
            }

            var properties = new List<FullFact>();
            AddFact(properties, lang, lang["manufacturer"], gpu.Manufacturer);
            AddFact(properties, lang, lang["pci_slot"], gpu.PciSlot);
            AddFact(properties, lang, lang["driver"], gpu.Driver);
            if (gpu.DedicatedTotalGb > 0)
            {
                properties.Add(new FullFact
                {
                    Title = lang["dedicated"],
                    Value = $"{Format.Decimal(gpu.DedicatedGb, 1)} / {Format.Decimal(gpu.DedicatedTotalGb, 1)} GB",
                });
            }
            else if (gpu.DedicatedGb > 0.01)
            {
                properties.Add(new FullFact
                {
                    Title = lang["dedicated"],
                    Value = Format.GigabytesIfReal(gpu.DedicatedGb),
                });
            }
            AddFact(properties, lang, lang["gpu_clock"], Mhz(gpu.CoreClockMhz));
            AddFact(properties, lang, lang["memory_clock"], Mhz(gpu.MemoryClockMhz));
            AddFact(properties, lang, lang["power_usage"],
                gpu.PowerW is double power ? Format.Watts(power) : null);
            AddFact(properties, lang, lang["power_cap"],
                gpu.PowerCapW is double cap ? Format.Watts(cap) : null);
            AddFact(properties, lang, lang["link"], GpuLink(gpu));
            if (gpu.Temp is int temp)
            {
                // The temperature is a Sensor in §2b; the old fact stays as a
                // property only when there is a reading to state.
                properties.Add(new FullFact
                {
                    Title = lang["temp_label"],
                    Value = Format.Temperature(temp),
                });
            }

            var small = gpu.Engines
                .Select((engine, engineIndex) => new FullCard
                {
                    Key = $"engine{index}:{engineIndex}",
                    Title = engine.Name,
                    Value = Format.Percent(engine.Usage),
                    Accent = Accents.Gpu,
                    History = history.Engine(key, engine.Name),
                    Small = true,
                })
                .ToList();

            var sensors = new List<FullCard>();
            if (gpu.Temp is int gpuTemp)
            {
                sensors.Add(SensorCard(
                    key + ":temp", lang["temp_label"], Format.Temperature(gpuTemp),
                    gpuTemp, history.GpuTemp(key), peaks, lang, Accents.Gpu));
            }

            tabs.Add(new FullTab
            {
                Key = key,
                Title = gpu.Name.Length > 0 ? gpu.Name : lang["gpu"],
                Label = lang.Get("gpu_n", index + 1),
                Detail = gpu.DedicatedTotalGb > 0
                    ? $"{Format.Decimal(gpu.DedicatedGb, 1)} / {Format.Decimal(gpu.DedicatedTotalGb, 1)} GB"
                    : Format.GigabytesIfReal(gpu.DedicatedGb),
                Summary = Format.Percent(gpu.Usage),
                Hardware = gpu.Name,
                Properties = properties,
                BigCards = big,
                SmallCards = small,
                Sensors = sensors,
            });
        }
    }

    private static string? GpuLink(Gpu gpu)
    {
        if (gpu.LinkCurrent.Length > 0 && gpu.LinkMax.Length > 0)
        {
            return $"{gpu.LinkCurrent} / {gpu.LinkMax}";
        }
        return gpu.LinkCurrent.Length > 0 ? gpu.LinkCurrent
            : gpu.LinkMax.Length > 0 ? gpu.LinkMax
            : null;
    }

    // -------------------------------------------------------------- network

    private static void AddNetworkPages(
        List<FullTab> tabs, Snapshot snapshot, AppConfig config, Lang lang,
        HistoryBoard history, PeakTracker? peaks)
    {
        foreach (Adapter adapter in snapshot.Adapters)
        {
            string name = adapter.Name.Length > 0 ? adapter.Name : adapter.Id;
            string key = PageKeys.Net(adapter.Id);
            string kind = adapter.Kind.Length > 0
                ? adapter.Kind
                : adapter.Wireless ? lang["wireless"] : lang["wired"];

            var options = new[]
            {
                new ModuleOption("network_mode", "per_adapter", IsSeparated(config.NetworkMode)),
            };

            var properties = new List<FullFact>();
            AddFact(properties, lang, lang["interface"], adapter.Interface);
            AddFact(properties, lang, lang["type"],
                adapter.Wireless ? lang["wireless"] : lang["wired"]);
            AddFact(properties, lang, lang["driver"], adapter.Driver);
            AddFact(properties, lang, lang["ipv4"],
                adapter.Ipv4.Count > 0 ? string.Join(", ", adapter.Ipv4) : null);
            AddFact(properties, lang, lang["ipv6"],
                adapter.Ipv6.Count > 0 ? string.Join(", ", adapter.Ipv6) : null);

            if (adapter.Wireless)
            {
                AddFact(properties, lang, lang["ssid"],
                    adapter.Ssid.Length > 0 ? adapter.Ssid : null, priv: true);
                AddFact(properties, lang, lang["signal"],
                    adapter.SignalDbm is int dbm
                        ? $"{dbm.ToString(CultureInfo.InvariantCulture)} dBm"
                        : null);
                AddFact(properties, lang, lang["frequency"],
                    adapter.FrequencyMhz is int mhz
                        ? $"{mhz.ToString(CultureInfo.InvariantCulture)} MHz"
                        : null);
            }
            else
            {
                AddFact(properties, lang, lang["link_speed"], LinkSpeed(adapter));
            }

            AddFact(properties, lang, lang["total_received"],
                adapter.TotalReceived > 0 ? Format.Bytes(adapter.TotalReceived) : null);
            AddFact(properties, lang, lang["total_sent"],
                adapter.TotalSent > 0 ? Format.Bytes(adapter.TotalSent) : null);

            tabs.Add(new FullTab
            {
                Key = key,
                Title = name,
                Label = kind,
                Detail = adapter.Wireless ? lang["wireless"] : lang["wired"],
                Summary = Format.Join(
                    $"↓ {Format.Speed(adapter.DownMb)} MB/s",
                    $"↑ {Format.Speed(adapter.UpMb)} MB/s"),
                Hardware = adapter.Description,
                Options = options,
                Properties = properties,
                BigCards = new[]
                {
                    Card(
                        key + ":down", lang["download"], Format.Speed(adapter.DownMb) + " MB/s",
                        string.Empty, "MB/s", 0, Accents.Net, history.AdapterDown(adapter.Id),
                        peaks, lang, adapter.DownMb, Rate),
                    Card(
                        key + ":up", lang["upload"], Format.Speed(adapter.UpMb) + " MB/s",
                        string.Empty, "MB/s", 0, Accents.Net, history.AdapterUp(adapter.Id),
                        peaks, lang, adapter.UpMb, Rate),
                },
            });
        }
    }

    private static string? LinkSpeed(Adapter adapter)
    {
        double? rate = adapter.LinkSpeedMbps ?? (adapter.SpeedMbps > 0 ? adapter.SpeedMbps : null);
        return rate is double mbps && mbps > 0
            ? Format.Number((int)Math.Round(mbps)) + " Mbps"
            : null;
    }

    // -------------------------------------------------------------- battery

    private static void AddBatteryPages(
        List<FullTab> tabs, Snapshot snapshot, Lang lang, Palette palette,
        HistoryBoard history, PeakTracker? peaks)
    {
        foreach (Battery battery in snapshot.Batteries)
        {
            string key = PageKeys.Battery(battery.Id);
            string state = BatteryStateText(battery.State, lang);
            // R-030 fix 4: no lightning glyph in the text — IBM Plex Sans Thai
            // has no such glyph. The sidebar item and the page header draw the
            // same power icon as the fix-3 badge instead.
            string summary = battery.Percent is int percent ? Format.Percent(percent) : lang["na"];

            var properties = new List<FullFact>();
            AddFact(properties, lang, lang["battery_state"], state);
            AddFact(properties, lang, lang["battery_health"],
                battery.HealthPercent is double health ? Percent(health) : null);
            AddFact(properties, lang, lang["energy_now"],
                battery.EnergyNowWh is double now ? WattHours(now) : null);
            AddFact(properties, lang, lang["energy_full"],
                battery.EnergyFullWh is double full ? WattHours(full) : null);
            AddFact(properties, lang, lang["energy_design"],
                battery.EnergyDesignWh is double design ? WattHours(design) : null);
            AddFact(properties, lang, lang["battery_cycles"],
                battery.CycleCount is int cycles ? cycles.ToString(CultureInfo.InvariantCulture) : null);
            AddFact(properties, lang, lang["technology"], battery.Technology);
            AddFact(properties, lang, lang["manufacturer"], battery.Manufacturer);
            AddFact(properties, lang, lang["model"], battery.Model);
            AddFact(properties, lang, lang["voltage"],
                battery.VoltageV is double volts ? Format.Volts(volts) : null);
            AddFact(properties, lang, lang["battery_time_left"],
                battery.TimeToEmpty is { } empty ? Format.Duration(empty) : null);
            AddFact(properties, lang, lang["battery_time_to_full"],
                battery.TimeToFull is { } toFull ? Format.Duration(toFull) : null);
            AddFact(properties, lang, lang["power_sources"], PowerSources(snapshot, lang));

            foreach (PeripheralBattery peripheral in snapshot.Peripherals)
            {
                properties.Add(new FullFact
                {
                    Title = BatteryText.PeripheralName(peripheral.Kind, lang),
                    Value = JoinParts(new[]
                    {
                        peripheral.Model,
                        peripheral.Percent is int level ? Format.Percent(level) : null,
                    }),
                });
            }

            var big = new List<FullCard>
            {
                Card(
                    key, lang["battery"],
                    battery.Percent is int charge ? Format.Percent(charge) : lang["na"],
                    state, lang["utilisation"], 100, Accents.Battery,
                    history.BatteryCharge(battery.Id),
                    peaks, lang, battery.Percent ?? double.NaN, Percent),
            };
            if (battery.PowerW is double watts)
            {
                big.Add(Card(
                    key + ":power", lang["battery_power"], Format.Watts(watts),
                    string.Empty, "W", 0, Accents.Battery,
                    history.BatteryPower(battery.Id), peaks, lang, watts, v => Format.Watts(v)));
            }

            tabs.Add(new FullTab
            {
                Key = key,
                Title = battery.Model is { Length: > 0 } title ? title : battery.Id,
                Label = lang["battery"],
                Detail = state,
                Summary = summary,
                Badge = BatteryText.AcBadge(battery, snapshot, palette),
                Hardware = Format.Join(battery.Manufacturer, battery.Model) is { Length: > 0 } hardware
                    ? hardware
                    : battery.Id,
                Properties = properties,
                BigCards = big,
            });
        }
    }

    private static string? PowerSources(Snapshot snapshot, Lang lang)
    {
        if (snapshot.PowerSources.Count == 0)
        {
            return null;
        }
        return string.Join(", ", snapshot.PowerSources.Select(source =>
            $"{source.Id} ({PowerSourceText(source.Kind, lang)} · " +
            $"{(source.Online ? lang["online"] : lang["offline"])})"));
    }

    internal static string PowerSourceText(PowerSourceKind kind, Lang lang) => kind switch
    {
        PowerSourceKind.Mains => lang["power_mains"],
        PowerSourceKind.Usb => lang["power_usb"],
        _ => lang["power_other"],
    };

    internal static string BatteryStateText(BatteryState state, Lang lang) => state switch
    {
        BatteryState.Charging => lang["battery_charging"],
        BatteryState.Discharging => lang["battery_discharging"],
        BatteryState.Full => lang["battery_full"],
        BatteryState.NotCharging => lang["battery_not_charging"],
        _ => lang["unknown"],
    };

    private static string WattHours(double value) => Format.Decimal(value, 1) + " Wh";

    // ---------------------------------------------------------------- pieces

    private static FullCard Card(
        string key,
        string title,
        string value,
        string detail,
        string unit,
        double maximum,
        Colour accent,
        History history,
        PeakTracker? peaks = null,
        Lang? lang = null,
        double peakValue = double.NaN,
        Func<double, string>? peakFormat = null)
    {
        return new FullCard
        {
            Key = key,
            Title = title,
            Value = value,
            Peak = Peak(peaks, lang, key, peakValue, history, peakFormat ?? Decimal),
            Detail = detail,
            Unit = unit,
            Ceiling = ChartMath.Ceiling(maximum, history.Max, unit),
            Maximum = maximum,
            Accent = accent,
            History = history,
        };
    }

    private static FullCard SensorCard(
        string key,
        string title,
        string value,
        double reading,
        History history,
        PeakTracker? peaks,
        Lang lang,
        Colour accent)
    {
        return new FullCard
        {
            Key = key,
            Title = title,
            Value = value,
            Peak = Peak(peaks, lang, key, reading, history,
                v => Format.Temperature((int)Math.Round(v))),
            Unit = "°C",
            Ceiling = ChartMath.Ceiling(0, history.Max, "°C"),
            Maximum = 0,
            Accent = accent,
            History = history,
        };
    }

    /// <summary>"Highest: 42%": the peak since the page was selected.</summary>
    private static string Peak(
        PeakTracker? peaks,
        Lang? lang,
        string key,
        double value,
        History history,
        Func<double, string> format)
    {
        if (peaks is null || lang is null || double.IsNaN(value))
        {
            return string.Empty;
        }
        double highest = peaks.Track(key, value, history.Max);
        return lang.Get("highest", format(highest));
    }

    private static void AddFact(
        List<FullFact> facts, Lang lang, string title, string? value, bool priv = false)
    {
        facts.Add(new FullFact
        {
            Title = title,
            Value = string.IsNullOrWhiteSpace(value) ? lang["na"] : value!,
            Private = priv,
        });
    }

    private static string JoinParts(IEnumerable<string?> parts) =>
        string.Join(FactSeparator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!));

    private static string Percent(double value) =>
        Format.Percent((int)Math.Round(value));

    private static string Rate(double megabytesPerSecond) =>
        Format.Speed(megabytesPerSecond) + " MB/s";

    private static string Decimal(double value) => Format.Decimal(value, 1);

    private static string? Mhz(int? value) =>
        value is int mhz && mhz > 0
            ? mhz.ToString(CultureInfo.InvariantCulture) + " MHz"
            : null;

    /// <summary>
    /// "1.2 GHz" from a core's current clock, in the unit the card has room
    /// for; null when the driver reports no frequency (R-030 fix 4).
    /// </summary>
    private static string? Clock(int? value) =>
        value is int mhz && mhz > 0
            ? Format.Decimal(mhz / 1000.0, 1) + " GHz"
            : null;

    private static string? Virtualisation(CpuInfo info, Lang lang)
    {
        if (info.VirtualizationType.Length > 0)
        {
            return info.VirtualizationType;
        }
        return info.Virtualization is bool enabled
            ? lang[enabled ? "enabled" : "disabled"]
            : null;
    }

    private static string? YesNo(bool? value, Lang lang) =>
        value is bool flag ? lang[flag ? "yes" : "no"] : null;

    private static bool IsSeparated(string mode) =>
        string.Equals(mode, "separated", StringComparison.OrdinalIgnoreCase);
}
