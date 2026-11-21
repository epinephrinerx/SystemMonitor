using System.Text.Json;
using System.Text.Json.Serialization;
using SysMonitor.Linux.Presentation;
using SysMonitor.Linux.Sampling;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Probe;

/// <summary>
/// Prints what the sampler sees, so the data layer can be checked without a
/// window.  Three samples at 0 s, 1 s and 2.5 s: the first has no rates yet,
/// the middle one falls inside the two-second fdinfo scan window, and the last
/// is a real scan whose delta covers the whole interval.
///
/// Processes are a privacy-sensitive read, so they are only sampled when
/// <c>--processes</c> is given, and the disk serial numbers that come out of
/// sysfs are cut to their last four characters in both output formats.
/// Peripheral battery ids are left out of both formats as well.
/// </summary>
internal static class Program
{
    private const int ProcessLines = 20;
    private const int AppTop = 15;
    private const int ProcessTop = 15;

    private static int Main(string[] args)
    {
        bool json = Has(args, "--json");
        bool includeProcesses = Has(args, "--processes");
        bool apps = Has(args, "--apps");

        if (apps)
        {
            PrintApps("/");
            return 0;
        }

        var sampler = new LinuxSampler();
        sampler.Sample(includeProcesses);
        Thread.Sleep(1000);
        sampler.Sample(includeProcesses);
        Thread.Sleep(1500);
        Snapshot snapshot = Masked(sampler.Sample(includeProcesses));

        if (json)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters =
                {
                    new JsonStringEnumConverter(),
                    new PeripheralBatteryJsonConverter(),
                },
            };
            Console.WriteLine(JsonSerializer.Serialize(snapshot, options));
        }
        else
        {
            Print(sampler.Root, snapshot, includeProcesses);
        }

        return 0;
    }

    private static bool Has(string[] args, string flag) =>
        args.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));

    private static void Print(string root, Snapshot snapshot, bool includeProcesses)
    {
        Console.WriteLine("System Monitor - Linux probe");
        Console.WriteLine($"  Root    : {root}");
        Console.WriteLine($"  Ready   : {(snapshot.Ready ? "yes" : "no")}");
        Console.WriteLine();

        PrintCpu(snapshot);
        PrintRam(snapshot);
        PrintSwap(snapshot);
        PrintMemoryLayout(snapshot.MemoryLayout);
        PrintDisks(snapshot);
        PrintAdapters(snapshot);
        PrintGpus(snapshot);
        PrintBatteries(snapshot);
        PrintProcesses(snapshot, includeProcesses);
    }

    private static void PrintCpu(Snapshot snapshot)
    {
        CpuInfo info = snapshot.CpuInfo;

        Console.WriteLine("CPU");
        Console.WriteLine($"  Name           : {Text(info.Name)}");
        Console.WriteLine($"  Vendor         : {Text(info.Vendor)}");
        Console.WriteLine($"  Sockets        : {Number(info.Sockets)} ({Number(info.Cores)} cores, {Number(info.Logical)} logical)");
        Console.WriteLine($"  Base clock     : {Mhz(info.BaseMhz)}");
        Console.WriteLine($"  L2 / L3        : {Kb(info.L2Kb)} / {Kb(info.L3Kb)}");
        Console.WriteLine($"  Virtualization : {(info.Virtualization is bool v ? (v ? "yes" : "no") : "N/A")}");
        Console.WriteLine($"  Total usage    : {Number(snapshot.CpuTotal)} %");
        Console.WriteLine($"  Package temp   : {Celsius(snapshot.CpuTemp)}{(snapshot.CpuTempEstimated ? " (estimated)" : string.Empty)}");

        for (int i = 0; i < snapshot.Cores.Count; i++)
        {
            Core core = snapshot.Cores[i];
            Console.WriteLine($"  CPU {i,-2}         : {Number(core.Usage),3} %   {Celsius(core.Temp)}");
        }

        if (snapshot.Cores.Count == 0)
        {
            Console.WriteLine("  Cores          : N/A");
        }

        Console.WriteLine("  Properties");
        if (snapshot.Cores.Count > 0)
        {
            var clocks = new List<string>(snapshot.Cores.Count);
            for (int i = 0; i < snapshot.Cores.Count; i++)
            {
                clocks.Add($"CPU{i} {Clock(snapshot.Cores[i].Mhz)}");
            }
            Console.WriteLine($"    Core clocks    : {string.Join(" · ", clocks)}");
        }
        Console.WriteLine($"    Max clock      : {Clock(info.MaxMhz)}");
        Console.WriteLine($"    Virtualization : {Text(info.VirtualizationType)}");
        Console.WriteLine($"    Architecture   : {Text(info.Architecture)}");
        Console.WriteLine($"    Uptime         : {Uptime(snapshot.Uptime)}");
        Console.WriteLine();
    }

    private static void PrintRam(Snapshot snapshot)
    {
        Ram ram = snapshot.Ram;

        Console.WriteLine("RAM");
        Console.WriteLine($"  Used / total   : {Gb(ram.UsedGb)} / {Gb(ram.TotalGb)} ({Number(ram.Usage)} %)");
        Console.WriteLine($"  Temperature    : {Celsius(ram.Temp)}");
        Console.WriteLine($"  Modules        : {(snapshot.Modules.Count > 0 ? Module.Summarise(snapshot.Modules) : "N/A")} ({Number(snapshot.MemorySlots)} slots)");
        Console.WriteLine();
    }

    private static void PrintSwap(Snapshot snapshot)
    {
        Ram ram = snapshot.Ram;
        Console.WriteLine("Swap");
        if (ram.SwapTotalBytes <= 0)
        {
            Console.WriteLine("  N/A (no swap)");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"  Used / total   : {Gb2(ram.SwapUsedBytes)} / {Gb2(ram.SwapTotalBytes)} ({Number(ram.SwapUsage)} %)");
        foreach (SwapDevice device in snapshot.SwapDevices)
        {
            Console.WriteLine(
                $"  {device.Name} ({device.Type}) : {Gb2(device.UsedBytes)} used of {Gb2(device.SizeBytes)}, priority {device.Priority}");
        }
        Console.WriteLine();
    }

    private static void PrintMemoryLayout(MemoryLayout layout)
    {
        Console.WriteLine("RAM modules");
        if (layout.Modules.Count == 0 && layout.SlotsTotal is null)
        {
            Console.WriteLine("  N/A (hardware details not available)");
            Console.WriteLine();
            return;
        }

        Console.WriteLine(
            $"  Slots          : {(layout.SlotsUsed is int used ? Number(used) : "N/A")} of {(layout.SlotsTotal is int total ? Number(total) : "N/A")}");
        Console.WriteLine($"  Max capacity   : {Gb2(layout.MaxCapacityBytes)}");

        foreach (MemoryModule module in layout.Modules)
        {
            var parts = new List<string>();
            if (module.SizeBytes is long size)
            {
                parts.Add(Gb2(size));
            }
            if (module.Type.Length > 0)
            {
                parts.Add(module.Type);
            }
            if (module.TypeDetail.Length > 0)
            {
                parts.Add(module.TypeDetail);
            }
            if (module.FormFactor.Length > 0)
            {
                parts.Add(module.FormFactor);
            }
            if (module.ConfiguredSpeedMts is int configured)
            {
                parts.Add(module.SpeedMts is int rated
                    ? $"{configured} MT/s (rated {rated})"
                    : $"{configured} MT/s");
            }
            if (module.Manufacturer.Length > 0)
            {
                parts.Add(module.Manufacturer);
            }
            if (module.Rank is int rank)
            {
                parts.Add($"rank {rank}");
            }
            if (module.ConfiguredVoltage is double volts)
            {
                parts.Add($"{volts:F2} V");
            }

            string slot = module.Slot.Length > 0 ? module.Slot : "module";
            Console.WriteLine($"  {slot,-22} : {string.Join(" · ", parts)}");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// The top apps and processes by memory, from two samples <b>4.5 s apart</b>
    /// so the CPU deltas are real and the second sample is also a real GPU scan
    /// (<see cref="ProcessGpu.IntervalMs"/> is 4 s), which is what lets a GPU
    /// percentage appear at all. No command line is printed and no serial number
    /// exists in these models; the output is meant to be pasted into a report.
    /// </summary>
    private static void PrintApps(string root)
    {
        const int sampleGapMs = 4500;

        var stats = new ProcessStats(root, Environment.ProcessorCount, 100);
        long first = Environment.TickCount64;
        stats.Read(first);

        Console.WriteLine("System Monitor - Linux probe (apps)");
        Console.WriteLine($"  Root    : {root}");
        Console.WriteLine(
            $"  Waiting {sampleGapMs / 1000.0:0.0} s for the second sample, so CPU and GPU deltas are real…");
        Console.WriteLine();

        Thread.Sleep(sampleGapMs);
        IReadOnlyList<ProcessRow> rows = stats.Read(Environment.TickCount64);

        var resolver = new AppResolver(root, language: "en");
        IReadOnlyList<AppEntry> apps = resolver.Read(rows);

        Console.WriteLine($"Apps — top {Math.Min(AppTop, apps.Count)} by memory");
        Console.WriteLine($"  {"instances",9}  {"CPU",6}  {"memory",10}  {"GPU",6}  name");
        foreach (AppEntry app in ProcessGrid.Sort(apps, GridColumn.Memory, descending: true).Take(AppTop))
        {
            Console.WriteLine(
                $"  {app.Instances.Count,9}  {Percent(app.CpuPercent),6}  {MemoryBoard.Size(app.MemoryBytes),10}  {Percent(app.GpuPercent),6}  {app.Name}");
        }
        if (apps.Count == 0)
        {
            Console.WriteLine("  N/A");
        }
        Console.WriteLine();

        Console.WriteLine($"Processes — top {Math.Min(ProcessTop, rows.Count)} by memory");
        Console.WriteLine($"  {"pid",7}  {"user",-14}  {"CPU",6}  {"memory",10}  {"GPU",6}  name");
        foreach (ProcessRow row in ProcessGrid.Sort(rows, GridColumn.Memory, descending: true).Take(ProcessTop))
        {
            Console.WriteLine(
                $"  {row.Pid,7}  {Short(row.User),-14}  {Percent(row.CpuPercent),6}  {MemoryBoard.Size(row.MemoryBytes),10}  {Percent(row.GpuPercent),6}  {row.Name}");
        }
        if (rows.Count == 0)
        {
            Console.WriteLine("  N/A");
        }
        Console.WriteLine();

        string? swapText = ReadText(Path.Combine(root, "proc", "swaps"));
        IReadOnlyList<SwapDevice> swaps = SwapsReader.Parse(swapText);
        Ram ram = new();
        string? meminfo = ReadText(Path.Combine(root, "proc", "meminfo"));
        if (meminfo is not null)
        {
            ram = MemInfo.Parse(meminfo);
        }

        PrintSwap(new Snapshot { Ram = ram, SwapDevices = swaps });
        PrintMemoryLayout(MemoryModules.Read(root));
    }

    private static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Percent(double? value) =>
        value is double percent ? $"{percent:F1} %" : "—";

    private static string Short(string text) =>
        text.Length <= 14 ? text : text[..14];

    private static void PrintDisks(Snapshot snapshot)
    {
        Console.WriteLine("Disks");
        if (snapshot.Disks.Count == 0)
        {
            Console.WriteLine("  N/A");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Disk disk in snapshot.Disks)
        {
            Console.WriteLine($"  {disk.Title}");
            Console.WriteLine($"    Media / bus : {disk.Media} / {disk.Bus}{(disk.IsNetwork ? " (network)" : string.Empty)}");
            Console.WriteLine($"    Space       : {Gb(disk.UsedGb)} used of {Gb(disk.TotalGb)} ({Number(disk.Usage)} %)");
            Console.WriteLine($"    I/O         : {Rate(disk.ReadMb)} read, {Rate(disk.WriteMb)} write");
            Console.WriteLine($"    Temperature : {Celsius(disk.Temp)} ({disk.TempSource})");

            if (disk.Detail is DriveDetail detail)
            {
                Console.WriteLine($"    Detail      : {DiskLine(detail)}");
                Console.WriteLine($"    Partition   : {PartitionLine(detail)}");
            }
            else
            {
                Console.WriteLine("    Detail      : N/A");
                Console.WriteLine("    Partition   : N/A");
            }

            // The drive's own properties, once per physical disk even when two
            // filesystems live on it.
            PhysicalDisk? hardware = disk.Detail?.Disk;
            string key = hardware?.DeviceName is { Length: > 0 } name
                ? name
                : hardware?.Name ?? string.Empty;
            if (seen.Add(key))
            {
                PrintDiskProperties(hardware);
            }
        }
        Console.WriteLine();
    }

    private static void PrintDiskProperties(PhysicalDisk? disk)
    {
        Console.WriteLine("    Properties");
        Console.WriteLine($"      Device       : {Text(disk?.DeviceName ?? string.Empty)}");
        Console.WriteLine($"      Type         : {Text(disk?.Kind ?? string.Empty)}");
        Console.WriteLine($"      Removable    : {YesNo(disk?.Removable)}");
        Console.WriteLine($"      Writable     : {YesNo(disk?.Writable)}");
        Console.WriteLine($"      Link         : {Text(disk?.Link ?? string.Empty)}");
        Console.WriteLine($"      Activity     : {PercentageDouble(disk?.ActivityPercent)}");
        Console.WriteLine($"      Read speed   : {ByteRate(disk?.ReadBytesPerSec)}");
        Console.WriteLine($"      Write speed  : {ByteRate(disk?.WriteBytesPerSec)}");
        Console.WriteLine($"      Total read   : {OptionalBytes(disk?.TotalReadBytes)}");
        Console.WriteLine($"      Total write  : {OptionalBytes(disk?.TotalWrittenBytes)}");
    }

    private static void PrintAdapters(Snapshot snapshot)
    {
        Console.WriteLine("Adapters");
        if (snapshot.Adapters.Count == 0)
        {
            Console.WriteLine("  N/A");
        }

        foreach (Adapter adapter in snapshot.Adapters)
        {
            Console.WriteLine($"  {adapter.Id}{(adapter.Wireless ? " (wireless)" : string.Empty)}");
            Console.WriteLine($"    Link speed  : {(adapter.SpeedMbps > 0 ? $"{adapter.SpeedMbps:F1} Mbps" : "N/A")}");
            Console.WriteLine($"    Throughput  : {Rate(adapter.DownMb)} down, {Rate(adapter.UpMb)} up");
            Console.WriteLine($"    Totals      : {Bytes(adapter.TotalReceived)} received, {Bytes(adapter.TotalSent)} sent");
            Console.WriteLine($"    Description : {Text(adapter.Description)}");
            Console.WriteLine("    Properties");
            Console.WriteLine($"      Interface    : {Text(adapter.Interface)}");
            Console.WriteLine($"      Type         : {Text(adapter.Kind)}");
            Console.WriteLine($"      Driver       : {Text(adapter.Driver)}");
            Console.WriteLine("      MAC          : not read (privacy)");
            Console.WriteLine($"      IPv4         : {Addresses(adapter.Ipv4)}");
            Console.WriteLine($"      IPv6         : {Addresses(adapter.Ipv6)}");
            Console.WriteLine($"      Link speed   : {Mbps(adapter.LinkSpeedMbps)}");
            // The SSID is model-only: say that there is one, never which.
            Console.WriteLine($"      SSID         : {(adapter.Ssid.Length > 0 ? "(hidden)" : "N/A")}");
            Console.WriteLine($"      Signal       : {Signal(adapter.SignalDbm)}");
            Console.WriteLine($"      Frequency    : {Frequency(adapter.FrequencyMhz)}");
            Console.WriteLine($"      Tx bitrate   : {Mbps(adapter.TxBitrateMbps)}");
        }
        Console.WriteLine();
    }

    private static void PrintGpus(Snapshot snapshot)
    {
        Console.WriteLine("GPUs");
        if (snapshot.Gpus.Count == 0)
        {
            Console.WriteLine("  N/A");
        }

        for (int i = 0; i < snapshot.Gpus.Count; i++)
        {
            Gpu gpu = snapshot.Gpus[i];
            string primary = i == 0 ? " (primary)" : string.Empty;
            Console.WriteLine($"  {i + 1}. {Text(gpu.Name)}{primary}");
            Console.WriteLine($"    Driver      : {Text(gpu.Driver)}");
            Console.WriteLine($"    Usage       : {Number(gpu.Usage)} %");
            Console.WriteLine($"    Engines     : {Engines(gpu)}");
            Console.WriteLine($"    Dedicated   : {Dedicated(gpu)}");
            Console.WriteLine($"    Temp        : {Celsius(gpu.Temp)}");
            Console.WriteLine("    Properties");
            Console.WriteLine($"      Manufacturer : {Text(gpu.Manufacturer)}");
            Console.WriteLine($"      PCI slot     : {Text(gpu.PciSlot)}");
            Console.WriteLine($"      Core clock   : {Clock(gpu.CoreClockMhz)}");
            Console.WriteLine($"      Memory clock : {Clock(gpu.MemoryClockMhz)}");
            Console.WriteLine($"      Power        : {Power(gpu.PowerW)}");
            Console.WriteLine($"      Power cap    : {Power(gpu.PowerCapW)}");
            Console.WriteLine($"      Link current : {Text(gpu.LinkCurrent)}");
            Console.WriteLine($"      Link max     : {Text(gpu.LinkMax)}");
            Console.WriteLine($"      Encoder      : {Percentage(gpu.EncoderUsage)}");
            Console.WriteLine($"      Decoder      : {Percentage(gpu.DecoderUsage)}");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Batteries, adapters and peripheral batteries. Every value is printed as
    /// read: a field the driver did not publish says "unknown" instead of a
    /// zero. Peripheral ids are deliberately absent -- the model carries an
    /// opaque hash, and this output is meant to be pasted into a report.
    /// </summary>
    private static void PrintBatteries(Snapshot snapshot)
    {
        Console.WriteLine("Battery");
        if (snapshot.Batteries.Count == 0)
        {
            Console.WriteLine("  N/A");
        }

        foreach (Battery battery in snapshot.Batteries)
        {
            Console.WriteLine($"  {battery.Id}{Title(battery.Model)}");
            Console.WriteLine($"    Manufacturer : {Known(battery.Manufacturer)}");
            Console.WriteLine($"    Technology   : {Known(battery.Technology)}");
            Console.WriteLine($"    Charge       : {(battery.Percent is int percent ? Number(percent) + " %" : "unknown")} ({State(battery.State)})");
            Console.WriteLine($"    Power        : {Watts(battery.PowerW)}");
            Console.WriteLine($"    Energy now   : {WattHours(battery.EnergyNowWh)}");
            Console.WriteLine($"    Energy full  : {WattHours(battery.EnergyFullWh)}");
            Console.WriteLine($"    Design       : {WattHours(battery.EnergyDesignWh)}");
            Console.WriteLine($"    Health       : {Health(battery.HealthPercent)}");
            Console.WriteLine($"    Cycles       : {Count(battery.CycleCount)}");
            Console.WriteLine($"    Voltage      : {Voltage(battery.VoltageV)}");
            Console.WriteLine($"    Time left    : {Duration(battery.TimeToEmpty)}");
            Console.WriteLine($"    Time to full : {Duration(battery.TimeToFull)}");
        }

        Console.WriteLine("  Power sources");
        if (snapshot.PowerSources.Count == 0)
        {
            Console.WriteLine("    N/A");
        }
        foreach (PowerSource source in snapshot.PowerSources)
        {
            Console.WriteLine($"    {source.Id,-28} : {source.Kind} ({(source.Online ? "online" : "offline")})");
        }

        Console.WriteLine("  Devices");
        if (snapshot.Peripherals.Count == 0)
        {
            Console.WriteLine("    N/A");
        }
        foreach (PeripheralBattery device in snapshot.Peripherals)
        {
            string percent = device.Percent is int value ? Number(value) + " %" : "unknown";
            Console.WriteLine($"    {device.Kind,-12} : {Known(device.Model)}  {percent}  {State(device.State)}");
        }

        Console.WriteLine($"  On AC power  : {(snapshot.OnAcPower ? "yes" : "no")}");
        Console.WriteLine();
    }

    private static void PrintProcesses(Snapshot snapshot, bool includeProcesses)
    {
        Console.WriteLine("Processes");
        if (!includeProcesses)
        {
            Console.WriteLine("  Process list: not read (use --processes)");
            Console.WriteLine();
            return;
        }

        if (snapshot.Processes.Count == 0)
        {
            Console.WriteLine("  N/A");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"  {snapshot.Processes.Count} readable entries, read at {snapshot.MemoryStamp}; top {Math.Min(ProcessLines, snapshot.Processes.Count)} by memory");
        Console.WriteLine($"  {"pid",7}  {"resident",10}  {"private",10}  name");

        foreach (ProcessEntry process in snapshot.Processes
                     .OrderByDescending(p => p.WorkingSet)
                     .ThenBy(p => p.Pid)
                     .Take(ProcessLines))
        {
            Console.WriteLine(
                $"  {process.Pid,7}  {MemoryBoard.Size(process.WorkingSet),10}  {MemoryBoard.Size(process.PrivateBytes),10}  {process.Name}{(process.Readable ? string.Empty : " (unreadable)")}");
        }
        Console.WriteLine();
    }

    private static string DiskLine(DriveDetail detail)
    {
        PhysicalDisk? disk = detail.Disk;
        if (disk is null)
        {
            return "N/A";
        }

        var parts = new List<string>();
        parts.Add(disk.Model.Length > 0 ? disk.Model : $"Disk {disk.Number}");
        if (disk.Serial.Length > 0)
        {
            parts.Add("SN " + disk.Serial);
        }
        if (disk.Firmware.Length > 0)
        {
            parts.Add("FW " + disk.Firmware);
        }
        parts.Add($"{disk.Gb:F1} GB");
        return string.Join(" · ", parts);
    }

    private static string PartitionLine(DriveDetail detail)
    {
        var parts = new List<string>();
        if (detail.PartitionNumber > 0)
        {
            int count = detail.Disk?.PartitionCount ?? 0;
            parts.Add($"partition {detail.PartitionNumber}" + (count > 0 ? $" of {count}" : string.Empty));
        }
        else
        {
            parts.Add("whole disk");
        }

        parts.Add($"{detail.PartitionGb:F1} GB");
        if (detail.PartitionOffset > 0)
        {
            parts.Add($"at {detail.PartitionOffset / 1073741824.0:F1} GB");
        }
        if (detail.FileSystem.Length > 0)
        {
            parts.Add(detail.FileSystem);
        }
        if (detail.Label.Length > 0)
        {
            parts.Add("label " + detail.Label);
        }
        if (detail.IsBoot)
        {
            parts.Add("boot");
        }
        return string.Join(", ", parts);
    }

    private static string Engines(Gpu gpu)
    {
        if (gpu.Engines.Count == 0)
        {
            return "N/A";
        }
        return string.Join(", ", gpu.Engines.Select(e => $"{e.Name} {e.Usage} %"));
    }

    /// <summary>
    /// A copy of the snapshot with every disk serial reduced to its last four
    /// characters and every <c>enx&lt;MAC&gt;</c> interface name reduced to
    /// <c>enx…</c>.  Both identify the machine's hardware, and this output is
    /// meant to be pasted into a report.  The model keeps the real names.
    /// </summary>
    private static Snapshot Masked(Snapshot snapshot)
    {
        var disks = new List<Disk>(snapshot.Disks.Count);
        foreach (Disk disk in snapshot.Disks)
        {
            if (disk.Detail?.Disk is not PhysicalDisk physical)
            {
                disks.Add(disk);
                continue;
            }

            var masked = new PhysicalDisk
            {
                Number = physical.Number,
                Name = physical.Name,
                DeviceName = physical.DeviceName,
                Model = physical.Model,
                Serial = MaskSerial(physical.Serial),
                Firmware = physical.Firmware,
                Bus = physical.Bus,
                Interface = physical.Interface,
                Media = physical.Media,
                Kind = physical.Kind,
                Removable = physical.Removable,
                Writable = physical.Writable,
                Link = physical.Link,
                PartitionStyle = physical.PartitionStyle,
                Bytes = physical.Bytes,
                PartitionCount = physical.PartitionCount,
                Rpm = physical.Rpm,
                Healthy = physical.Healthy,
                Online = physical.Online,
                AllocatedBytes = physical.AllocatedBytes,
                ActivityPercent = physical.ActivityPercent,
                ReadBytesPerSec = physical.ReadBytesPerSec,
                WriteBytesPerSec = physical.WriteBytesPerSec,
                TotalReadBytes = physical.TotalReadBytes,
                TotalWrittenBytes = physical.TotalWrittenBytes,
            };

            disks.Add(new Disk
            {
                Letter = disk.Letter,
                Label = disk.Label,
                Media = disk.Media,
                Bus = disk.Bus,
                IsNetwork = disk.IsNetwork,
                Usage = disk.Usage,
                UsedGb = disk.UsedGb,
                TotalGb = disk.TotalGb,
                ReadMb = disk.ReadMb,
                WriteMb = disk.WriteMb,
                Temp = disk.Temp,
                Estimated = disk.Estimated,
                TempSource = disk.TempSource,
                Detail = new DriveDetail
                {
                    Letter = disk.Detail.Letter,
                    DiskNumber = disk.Detail.DiskNumber,
                    PartitionNumber = disk.Detail.PartitionNumber,
                    PartitionBytes = disk.Detail.PartitionBytes,
                    PartitionOffset = disk.Detail.PartitionOffset,
                    IsBoot = disk.Detail.IsBoot,
                    FileSystem = disk.Detail.FileSystem,
                    Label = disk.Detail.Label,
                    Disk = masked,
                },
            });
        }

        // systemd names a USB Ethernet adapter after its MAC address.  The
        // probe prints the prefix only and numbers two such adapters (#1, #2)
        // so they stay distinguishable; Id, Name, Interface and any description
        // that repeats the name are all redacted.
        var primaries = new List<string>(snapshot.Adapters.Count);
        foreach (Adapter adapter in snapshot.Adapters)
        {
            primaries.Add(adapter.Interface.Length > 0 ? adapter.Interface : adapter.Id);
        }
        IReadOnlyList<string> maskedNames = Privacy.MaskInterfaces(primaries);

        var adapters = new List<Adapter>(snapshot.Adapters.Count);
        for (int i = 0; i < snapshot.Adapters.Count; i++)
        {
            Adapter adapter = snapshot.Adapters[i];
            string maskedName = maskedNames[i];

            string MaskField(string value) =>
                string.Equals(value, primaries[i], StringComparison.Ordinal)
                    ? maskedName
                    : Privacy.MaskInterface(value);

            // The SSID says where the machine is, so the JSON says only that
            // there was one. The MAC address was never read.
            adapters.Add(new Adapter
            {
                Id = MaskField(adapter.Id),
                Name = MaskField(adapter.Name),
                Description = MaskDescription(adapter.Description, primaries, maskedNames),
                Wireless = adapter.Wireless,
                Interface = MaskField(adapter.Interface),
                Kind = adapter.Kind,
                Driver = adapter.Driver,
                Ipv4 = adapter.Ipv4,
                Ipv6 = adapter.Ipv6,
                LinkSpeedMbps = adapter.LinkSpeedMbps,
                Ssid = adapter.Ssid.Length > 0 ? "(hidden)" : string.Empty,
                SignalDbm = adapter.SignalDbm,
                FrequencyMhz = adapter.FrequencyMhz,
                TxBitrateMbps = adapter.TxBitrateMbps,
                SpeedMbps = adapter.SpeedMbps,
                DownMb = adapter.DownMb,
                UpMb = adapter.UpMb,
                TotalReceived = adapter.TotalReceived,
                TotalSent = adapter.TotalSent,
            });
        }

        return new Snapshot
        {
            Cores = snapshot.Cores,
            Ram = snapshot.Ram,
            Disks = disks,
            Adapters = adapters,
            Gpus = snapshot.Gpus,
            Modules = snapshot.Modules,
            Processes = snapshot.Processes,
            Services = snapshot.Services,
            Batteries = snapshot.Batteries,
            PowerSources = snapshot.PowerSources,
            Peripherals = snapshot.Peripherals,
            SwapDevices = snapshot.SwapDevices,
            MemoryLayout = snapshot.MemoryLayout,
            MemoryStamp = snapshot.MemoryStamp,
            Uptime = snapshot.Uptime,
            CpuTotal = snapshot.CpuTotal,
            CpuInfo = snapshot.CpuInfo,
            Gpu = snapshot.Gpu,
            MemorySlots = snapshot.MemorySlots,
            CpuTemp = snapshot.CpuTemp,
            CpuTempEstimated = snapshot.CpuTempEstimated,
            Ready = snapshot.Ready,
        };
    }

    /// <summary>
    /// Replace every masked interface name wherever a description repeats it,
    /// so no field of the report carries the MAC-bearing name.
    /// </summary>
    private static string MaskDescription(
        string description,
        IReadOnlyList<string> names,
        IReadOnlyList<string> masked)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (!string.Equals(names[i], masked[i], StringComparison.Ordinal))
            {
                description = description.Replace(names[i], masked[i], StringComparison.Ordinal);
            }
        }
        return description;
    }

    /// <summary>"S5R2NX0R123456" to "…3456".  An empty serial stays empty; a
    /// serial of four characters or fewer keeps all of it behind the marker.</summary>
    private static string MaskSerial(string serial)
    {
        if (serial.Length == 0)
        {
            return string.Empty;
        }
        return "…" + (serial.Length <= 4 ? serial : serial[^4..]);
    }

    private static string Text(string value) => value.Length > 0 ? value : "N/A";

    /// <summary>
    /// A missing value in the battery section says "unknown", not "N/A": the
    /// driver not saying is the fact, not a failure.
    /// </summary>
    private static string Known(string? value) => string.IsNullOrEmpty(value) ? "unknown" : value;

    private static string Title(string? model) =>
        string.IsNullOrEmpty(model) ? string.Empty : " · " + model;

    private static string State(BatteryState state) => state switch
    {
        BatteryState.Charging => "charging",
        BatteryState.Discharging => "discharging",
        BatteryState.Full => "full",
        BatteryState.NotCharging => "not charging",
        _ => "unknown",
    };

    private static string Watts(double? value) => value is double watts ? $"{watts:F2} W" : "unknown";

    private static string WattHours(double? value) => value is double wh ? $"{wh:F2} Wh" : "unknown";

    private static string Health(double? value) => value is double percent ? $"{percent:F1} %" : "unknown";

    private static string Count(int? value) => value is int count ? Number(count) : "unknown";

    private static string Voltage(double? value) => value is double volts ? $"{volts:F2} V" : "unknown";

    private static string Duration(TimeSpan? value)
    {
        if (value is not TimeSpan span)
        {
            return "unknown";
        }

        int hours = (int)span.TotalHours;
        int minutes = span.Minutes;
        if (hours > 0)
        {
            return minutes > 0 ? $"{hours}h {minutes:00}m" : hours + "h";
        }
        return minutes > 0 ? minutes + "m" : "<1m";
    }

    private static string Number(int value) => value.ToString();

    private static string Celsius(int? value) => value is int celsius ? $"{celsius} °C" : "N/A";

    private static string Gb(double value) => value > 0 ? $"{value:F1} GB" : "N/A";

    private static string Gb2(long? bytes) =>
        bytes is long value && value > 0
            ? $"{value / 1073741824.0:F1} GB"
            : "N/A";

    /// <summary>
    /// An integrated adapter has no dedicated memory, so it prints N/A.  With a
    /// capacity reported, show what is in use of it.
    /// </summary>
    private static string Dedicated(Gpu gpu)
    {
        if (gpu.DedicatedTotalGb > 0)
        {
            return $"{gpu.DedicatedGb:F1} / {gpu.DedicatedTotalGb:F1} GB in use";
        }
        return gpu.DedicatedGb > 0 ? $"{gpu.DedicatedGb:F1} GB in use" : "N/A";
    }

    private static string Mhz(int value) => value > 0 ? $"{value} MHz" : "N/A";

    private static string Clock(int? value) => value is int mhz ? $"{mhz} MHz" : "N/A";

    private static string Percentage(int? value) =>
        value is int percent ? Number(percent) + " %" : "N/A";

    private static string PercentageDouble(double? value) =>
        value is double percent ? $"{percent:F1} %" : "N/A";

    private static string Power(double? value) => value is double watts ? $"{watts:F2} W" : "N/A";

    private static string Mbps(double? value) =>
        value is double mbps && mbps > 0 ? $"{mbps:F1} Mbps" : "N/A";

    private static string Signal(int? value) => value is int dbm ? $"{dbm} dBm" : "N/A";

    private static string Frequency(int? value) => value is int mhz ? $"{mhz} MHz" : "N/A";

    private static string YesNo(bool? value) => value is bool flag ? (flag ? "yes" : "no") : "N/A";

    private static string Addresses(IReadOnlyList<string> values) =>
        values.Count > 0 ? string.Join(" · ", values) : "N/A";

    private static string ByteRate(double? value)
    {
        if (value is not double bytes)
        {
            return "N/A";
        }
        return bytes >= 1048576 ? $"{bytes / 1048576:F1} MB/s"
            : bytes >= 1024 ? $"{bytes / 1024:F0} KB/s"
            : $"{bytes:F0} B/s";
    }

    private static string OptionalBytes(long? value) =>
        value is long bytes ? MemoryBoard.Size(bytes) : "N/A";

    /// <summary>"1d 08h 03m", or the hours and minutes when it is a short uptime.</summary>
    private static string Uptime(TimeSpan? value)
    {
        if (value is not TimeSpan span)
        {
            return "N/A";
        }

        int days = (int)span.TotalDays;
        return days > 0
            ? $"{days}d {span.Hours:00}h {span.Minutes:00}m"
            : span.Hours > 0
                ? $"{span.Hours}h {span.Minutes:00}m"
                : $"{span.Minutes}m";
    }

    private static string Kb(int value) => value > 0 ? $"{value} KB" : "N/A";

    private static string Rate(double value) => $"{value:F2} MB/s";

    private static string Bytes(long value) => MemoryBoard.Size(value);
}
