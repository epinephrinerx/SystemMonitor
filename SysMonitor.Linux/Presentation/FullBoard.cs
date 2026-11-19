using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// Builds the Full view's tabs (spec sections 5, 5a). The order is the spec's:
/// CPU, RAM, one tab per physical disk, virtual drives, network drives, one tab
/// per GPU, one tab per adapter. Every headline, fact and chart series comes
/// from the snapshot and the 72-point history.
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
        MemoryTabModel? memory = null)
    {
        var tabs = new List<FullTab>();
        if (snapshot is not null)
        {
            if (config.ShowCpu)
            {
                tabs.Add(CpuTab(snapshot, config, lang, history));
            }
            if (config.ShowRam)
            {
                tabs.Add(RamTab(snapshot, lang, history, memory));
            }
            if (config.ShowDisk)
            {
                AddDiskTabs(tabs, snapshot, config, lang, history);
            }
            if (config.ShowGpu)
            {
                AddGpuTabs(tabs, snapshot, lang, history);
            }
            if (config.ShowNetwork)
            {
                AddNetworkTabs(tabs, snapshot, lang, history);
            }
        }

        string? selected = selection.Reconcile(tabs, rememberedTab);
        return new FullModel { Tabs = tabs, SelectedKey = selected };
    }

    // ------------------------------------------------------------------ CPU

    private static FullTab CpuTab(
        Snapshot snapshot, AppConfig config, Lang lang, HistoryBoard history)
    {
        CpuInfo info = snapshot.CpuInfo;
        int cores = info.Cores > 0 ? info.Cores : snapshot.Cores.Count;
        int logical = info.Logical > 0 ? info.Logical : snapshot.Cores.Count;

        string count = cores == logical || logical == 0
            ? $"{cores}C"
            : $"{cores}C/{logical}T";
        string clock = info.BaseMhz > 0
            ? Format.Decimal(info.BaseMhz / 1000.0, 2) + " GHz"
            : string.Empty;

        string? temperature = config.CpuTemperature
            ? TemperatureText(snapshot.CpuTemp, snapshot.CpuTempEstimated)
            : null;

        var facts = new List<FullFact>();
        if (info.Virtualization is { } virtualization)
        {
            facts.Add(new FullFact
            {
                Title = lang["virtualisation"],
                Value = virtualization ? lang["enabled"] : lang["disabled"],
            });
        }
        if (info.L2Kb > 0)
        {
            facts.Add(new FullFact { Title = "L2", Value = Format.Kilobytes(info.L2Kb) });
        }
        if (info.L3Kb > 0)
        {
            facts.Add(new FullFact { Title = "L3", Value = Format.Kilobytes(info.L3Kb) });
        }

        var small = new List<FullCard>();
        bool perCore = string.Equals(config.CpuMode, "separated", StringComparison.OrdinalIgnoreCase);
        if (perCore)
        {
            for (int i = 0; i < snapshot.Cores.Count; i++)
            {
                Core core = snapshot.Cores[i];
                string value = Format.Percent(core.Usage);
                if (config.CpuTemperature && core.Temp is { } coreTemp)
                {
                    value += FactSeparator + Format.Temperature(coreTemp);
                }
                small.Add(new FullCard
                {
                    Key = "core" + i,
                    Title = "Core " + i,
                    Value = value,
                    Accent = Accents.Cpu,
                    History = history.Core(i),
                    Small = true,
                });
            }
        }

        string bigDetail = logical > 0
            ? $"{logical} {lang["cores"]}"
            : string.Empty;
        if (temperature is not null)
        {
            bigDetail = bigDetail.Length > 0 ? bigDetail + RailSeparator + temperature : temperature;
        }

        return new FullTab
        {
            Key = "cpu",
            Title = lang["cpu"],
            Detail = clock.Length > 0 ? count + RailSeparator + clock : count,
            Summary = temperature is null ? Format.Percent(snapshot.CpuTotal)
                : Format.Percent(snapshot.CpuTotal) + RailSeparator + temperature,
            Hardware = info.Name,
            Facts = facts,
            BigCards = new[]
            {
                Card(
                    "cpu", lang["cpu"], Format.Percent(snapshot.CpuTotal), bigDetail,
                    lang["utilisation"], 100, Accents.Cpu, history.Cpu),
            },
            SmallCards = small,
        };
    }

    // ------------------------------------------------------------------ RAM

    private static FullTab RamTab(
        Snapshot snapshot, Lang lang, HistoryBoard history, MemoryTabModel? memory)
    {
        Ram ram = snapshot.Ram;
        string detail = ram.TotalGb > 0
            ? Format.Gigabytes(ram.UsedGb) + " / " + Format.Gigabytes(ram.TotalGb)
            : string.Empty;

        var facts = new List<FullFact>();
        foreach (Module module in snapshot.Modules)
        {
            facts.Add(new FullFact { Title = module.Title, Value = module.Detail });
        }

        string hardware = snapshot.Modules.Count > 0 && snapshot.MemorySlots > 0
            ? $"{snapshot.Modules.Count} {lang["of_slots"]} {snapshot.MemorySlots} {lang["slots"]}"
            : string.Empty;

        string bigDetail = ram.TotalGb > 0
            ? $"{Format.Decimal(ram.UsedGb, 1)} / {Format.Decimal(ram.TotalGb, 1)} GB {lang["in_use"]}"
            : string.Empty;

        var big = new List<FullCard>
        {
            Card(
                "memory", lang["memory"], Format.Percent(ram.Usage), bigDetail,
                lang["utilisation"], 100, Accents.Ram, history.Ram),
        };

        // The picked process group adds its own chart under the memory card,
        // even while a search hides its row.
        if (memory?.GraphedGroup is { } graphed)
        {
            big.Add(Card(
                "proc:" + graphed,
                graphed,
                MemoryBoard.Size(memory.GraphedBytes),
                lang.Get("mem_count", memory.GraphedCount),
                "MB",
                0,
                Accents.Ram,
                history.ProcessGroup(graphed)));
        }

        return new FullTab
        {
            Key = "ram",
            Title = lang["memory"],
            Detail = detail,
            Summary = Format.Percent(ram.Usage),
            Hardware = hardware,
            Facts = facts,
            BigCards = big,
        };
    }

    // ------------------------------------------------------------------ disks

    private static void AddDiskTabs(
        List<FullTab> tabs,
        Snapshot snapshot,
        AppConfig config,
        Lang lang,
        HistoryBoard history)
    {
        IReadOnlyList<Disk> disks = DeviceFilter.VisibleDisks(snapshot, config);

        // Two mounts of one disk share a tab. The kernel name is the identity,
        // so a disk keeps its tab key and its remembered selection after a
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
                string id = hardware.Name.Length > 0
                    ? "name:" + hardware.Name
                    : "number:" + hardware.Number.ToString(
                        System.Globalization.CultureInfo.InvariantCulture) + ":" + hardware.Model;
                if (!physical.TryGetValue(id, out DiskGroup? group))
                {
                    group = new DiskGroup(hardware, new List<Disk>());
                    physical[id] = group;
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

            tabs.Add(new FullTab
            {
                Key = hardware.Name.Length > 0 ? "disk:" + hardware.Name : "disk:" + index,
                Title = "Disk " + index,
                Detail = DiskRailDetail(hardware, lang),
                Summary = hardware.Model,
                Hardware = hardware.Model,
                BigCards = mounts.Select(mount => DriveCard(mount, lang, history)).ToList(),
                IsDisk = true,
                Mounts = mounts.Select(m => m.Letter).ToList(),
            });
        }

        if (virtualDrives.Count > 0)
        {
            tabs.Add(DriveGroupTab(
                "diskvirtual", lang["virtual_drives"], virtualDrives, lang, history));
        }
        if (networkDrives.Count > 0)
        {
            tabs.Add(DriveGroupTab(
                "disknetwork", lang["network_drives"], networkDrives, lang, history));
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

        return Card(
            "diskio" + disk.Letter,
            disk.Title,
            Format.Speed(disk.ReadMb + disk.WriteMb) + " MB/s",
            detail,
            "MB/s",
            0,
            Accents.Disk,
            history.DiskThroughput(disk.Letter));
    }

    // ------------------------------------------------------------------ GPU

    private static void AddGpuTabs(
        List<FullTab> tabs, Snapshot snapshot, Lang lang, HistoryBoard history)
    {
        for (int index = 0; index < snapshot.Gpus.Count; index++)
        {
            Gpu gpu = snapshot.Gpus[index];
            if (!gpu.Present && gpu.Name.Length == 0)
            {
                continue;
            }

            string memory = gpu.DedicatedTotalGb > 0
                ? $"{Format.Decimal(gpu.DedicatedGb, 1)} / {Format.Decimal(gpu.DedicatedTotalGb, 1)} GB"
                : Format.GigabytesIfReal(gpu.DedicatedGb);

            var facts = new List<FullFact>();
            if (gpu.Driver.Length > 0)
            {
                facts.Add(new FullFact { Title = "Driver", Value = gpu.Driver });
            }
            if (gpu.DedicatedTotalGb > 0)
            {
                facts.Add(new FullFact
                {
                    Title = lang["dedicated"],
                    Value = $"{Format.Decimal(gpu.DedicatedGb, 1)} / {Format.Decimal(gpu.DedicatedTotalGb, 1)} GB",
                });
            }
            else if (gpu.DedicatedGb > 0.01)
            {
                facts.Add(new FullFact
                {
                    Title = lang["dedicated"],
                    Value = Format.GigabytesIfReal(gpu.DedicatedGb),
                });
            }
            if (gpu.Temp is { } temp)
            {
                facts.Add(new FullFact
                {
                    Title = lang["temp_label"],
                    Value = Format.Temperature(temp),
                });
            }

            string engines = string.Join(
                RailSeparator,
                gpu.Engines.Select(e => $"{e.Name} {Format.Percent(e.Usage)}"));

            var small = gpu.Engines
                .Select((engine, engineIndex) => new FullCard
                {
                    Key = $"engine{index}:{engineIndex}",
                    Title = engine.Name,
                    Value = Format.Percent(engine.Usage),
                    Accent = Accents.Gpu,
                    History = history.Engine(index, engine.Name),
                    Small = true,
                })
                .ToList();

            tabs.Add(new FullTab
            {
                Key = "gpu" + index,
                Title = gpu.Name.Length > 0 ? gpu.Name : lang["gpu"],
                Detail = memory,
                Summary = Format.Percent(gpu.Usage),
                Hardware = gpu.Name,
                Facts = facts,
                BigCards = new[]
                {
                    Card(
                        "gpu" + index, lang["gpu"], Format.Percent(gpu.Usage), engines,
                        lang["utilisation"], 100, Accents.Gpu, history.GpuUsage(index)),
                },
                SmallCards = small,
            });
        }
    }

    // -------------------------------------------------------------- network

    private static void AddNetworkTabs(
        List<FullTab> tabs, Snapshot snapshot, Lang lang, HistoryBoard history)
    {
        foreach (Adapter adapter in snapshot.Adapters)
        {
            string name = adapter.Name.Length > 0 ? adapter.Name : adapter.Id;
            string total = Format.Speed(adapter.DownMb + adapter.UpMb) + " MB/s";
            string link = adapter.SpeedMbps > 0
                ? Format.Number((int)Math.Round(adapter.SpeedMbps)) + " Mbps"
                : lang["no_link"];

            var facts = new List<FullFact>
            {
                new()
                {
                    Title = lang["network"],
                    Value = adapter.Wireless ? lang["wireless"] : lang["wired"],
                },
                new() { Title = "Link", Value = link },
            };
            if (adapter.Description.Length > 0)
            {
                facts.Add(new FullFact { Title = "Adapter", Value = adapter.Description });
            }

            tabs.Add(new FullTab
            {
                Key = "net" + adapter.Id,
                Title = name,
                Detail = adapter.Wireless ? lang["wireless"] : lang["wired"],
                Summary = total,
                Hardware = adapter.Description,
                Facts = facts,
                BigCards = new[]
                {
                    Card(
                        "net" + adapter.Id,
                        name,
                        total,
                        Format.NetworkSpeedText(adapter.DownMb, adapter.UpMb),
                        "MB/s",
                        0,
                        Accents.Net,
                        history.AdapterThroughput(adapter.Id)),
                },
            });
        }
    }

    // ---------------------------------------------------------------- pieces

    private static FullCard Card(
        string key,
        string title,
        string value,
        string detail,
        string unit,
        double maximum,
        Colour accent,
        History history) => new()
    {
        Key = key,
        Title = title,
        Value = value,
        Detail = detail,
        Unit = unit,
        Ceiling = ChartMath.Ceiling(maximum, history.Max, unit),
        Maximum = maximum,
        Accent = accent,
        History = history,
    };

    private static string? TemperatureText(int? celsius, bool estimated) =>
        celsius is null ? null : Format.Temperature(celsius, estimated);
}
