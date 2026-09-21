namespace SysMonitor.Model;

/// <summary>
/// One reading of the machine.  A snapshot is built by the sampler, published
/// as a whole, and never mutated afterwards, so the UI thread can read the
/// latest one without a lock.
/// </summary>
public sealed class Snapshot
{
    private readonly IReadOnlyList<Core> _cores = Array.Empty<Core>();
    private readonly IReadOnlyList<Disk> _disks = Array.Empty<Disk>();
    private readonly IReadOnlyList<Adapter> _adapters = Array.Empty<Adapter>();
    private readonly IReadOnlyList<Module> _modules = Array.Empty<Module>();

    public IReadOnlyList<Core> Cores
    {
        get => _cores;
        init => _cores = Seal(value);
    }

    public Ram Ram { get; init; } = new();

    public IReadOnlyList<Disk> Disks
    {
        get => _disks;
        init => _disks = Seal(value);
    }

    public IReadOnlyList<Adapter> Adapters
    {
        get => _adapters;
        init => _adapters = Seal(value);
    }

    public IReadOnlyList<Module> Modules
    {
        get => _modules;
        init => _modules = Seal(value);
    }

    /// <summary>
    /// Take a copy the caller cannot reach and hand it out read-only.
    ///
    /// A snapshot crosses from the sampler's thread to the UI thread and is
    /// meant to be fixed once published. Two things stood in the way of that
    /// being true: `IReadOnlyList&lt;T&gt;` does not stop a `List&lt;T&gt;`
    /// behind it being cast back and written to, and `AsReadOnly` is a live
    /// view of the original, so a producer still holding the input could
    /// change what the UI was reading after the fact.
    ///
    /// The copy is what makes it a snapshot. It costs an array of a few dozen
    /// references every couple of seconds.
    /// </summary>
    private static IReadOnlyList<T> Seal<T>(IReadOnlyList<T> values)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<T>();
        }
        var copy = new T[values.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = values[i];
        }
        return Array.AsReadOnly(copy);
    }
    public int CpuTotal { get; init; }

    /// <summary>What the processor is. Empty until the first slow pass.</summary>
    public CpuInfo CpuInfo { get; init; } = new();

    /// <summary>The graphics adapter. Not present on a machine with no WDDM counters.</summary>
    public Gpu Gpu { get; init; } = new();

    /// <summary>
    /// How many memory slots the board has, filled or not. Zero until the
    /// first slow pass, and on a board that will not report it.
    /// </summary>
    public int MemorySlots { get; init; }

    public int? CpuTemp { get; init; }
    public bool CpuTempEstimated { get; init; }

    /// <summary>False until the first successful sample has landed.</summary>
    public bool Ready { get; init; }
}

public sealed class Core
{
    public int Usage { get; init; }
    public int? Temp { get; init; }
    public bool Estimated { get; init; }
}

public sealed class Ram
{
    public int Usage { get; init; }
    public double UsedGb { get; init; }
    public double TotalGb { get; init; }

    /// <summary>No consumer board exposes a DIMM sensor, so this stays null.</summary>
    public int? Temp => null;
    public bool Estimated => false;
}

public sealed class Disk
{
    public required string Letter { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Media { get; init; } = "Disk";
    public string Bus { get; init; } = "Unknown";

    /// <summary>A mapped share. It has no physical disk and never will.</summary>
    public bool IsNetwork { get; init; }
    public int Usage { get; init; }
    public double UsedGb { get; init; }
    public double TotalGb { get; init; }
    public double ReadMb { get; init; }
    public double WriteMb { get; init; }
    public int? Temp { get; init; }
    public bool Estimated { get; init; }

    /// <summary>
    /// Which physical disk this letter lives on and how. Null until the first
    /// slow pass has read it, and on a drive with no physical disk behind it.
    /// </summary>
    public DriveDetail? Detail { get; init; }

    public string Title => Letter + ":" + (Label.Length > 0 ? $" ({Label})" : string.Empty);
}

/// <summary>A wired or wireless adapter that is currently up.</summary>
public sealed class Adapter
{
    public required string Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool Wireless { get; init; }

    /// <summary>Link speed, or 0 when the adapter reports none.</summary>
    public double SpeedMbps { get; init; }

    public double DownMb { get; init; }
    public double UpMb { get; init; }
    public long TotalReceived { get; init; }
    public long TotalSent { get; init; }

    /// <summary>
    /// Throughput against the link rate, for the bar. Both directions count:
    /// a saturated uplink matters as much as a saturated downlink.
    /// </summary>
    public int Usage => SpeedMbps <= 0 ? 0
        : (int)Math.Clamp(Math.Round((DownMb + UpMb) * 8 / SpeedMbps * 100), 0, 100);
}

/// <summary>
/// An installed memory module. There is no per-module usage here because
/// Windows does not report one -- the controller interleaves across channels,
/// so the quantity does not exist to be read.
/// </summary>
public sealed class Module
{
    public string Slot { get; init; } = string.Empty;
    public string Bank { get; init; } = string.Empty;
    public double Gb { get; init; }
    public int Mhz { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string PartNumber { get; init; } = string.Empty;

    public string Title => Slot.Length > 0 ? Slot : Bank;

    /// <summary>
    /// What is fitted, in one line: "2 x 16 GB DDR4 2667 MHz". Mixed sizes are
    /// listed rather than averaged, because a machine with one 16 and one 8 is
    /// a fact worth seeing.
    ///
    /// There is deliberately no per-module usage here. Windows reports none,
    /// and no API exposes one: the controller interleaves across channels, so
    /// the quantity does not exist to be read.
    /// </summary>
    public static string Summarise(IReadOnlyList<Module> modules)
    {
        if (modules.Count == 0)
        {
            return string.Empty;
        }

        var sizes = modules.GroupBy(m => m.Gb)
            .OrderByDescending(g => g.Key)
            .Select(g => g.Count() > 1 ? $"{g.Count()} x {g.Key:F0} GB" : $"{g.Key:F0} GB");
        var parts = new List<string> { string.Join(" + ", sizes) };

        // Only state the type and speed when every module agrees; otherwise
        // the single figure would be a guess about which one won.
        string kind = modules[0].Kind;
        if (kind.Length > 0 && modules.All(m => m.Kind == kind))
        {
            parts.Add(kind);
        }
        int mhz = modules[0].Mhz;
        if (mhz > 0 && modules.All(m => m.Mhz == mhz))
        {
            parts.Add($"{mhz} MHz");
        }
        return string.Join(" ", parts);
    }

    public string Detail
    {
        get
        {
            var parts = new List<string> { $"{Gb:F0} GB" };
            if (Kind.Length > 0)
            {
                parts.Add(Kind);
            }
            if (Mhz > 0)
            {
                parts.Add($"{Mhz} MHz");
            }
            if (Manufacturer.Length > 0)
            {
                parts.Add(Manufacturer);
            }
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// A physical disk, as Windows describes it. Static for the life of the
/// machine, so it is read once and cached.
/// </summary>
public sealed class PhysicalDisk
{
    public int Number { get; init; }
    public string Model { get; set; } = string.Empty;
    public string Serial { get; set; } = string.Empty;
    public string Firmware { get; set; } = string.Empty;
    public string Bus { get; set; } = string.Empty;
    public string Interface { get; set; } = string.Empty;
    public string Media { get; set; } = string.Empty;
    public string PartitionStyle { get; set; } = string.Empty;
    public long Bytes { get; init; }
    public int PartitionCount { get; set; }

    /// <summary>Zero for an SSD, and zero for a disk that will not say.</summary>
    public int Rpm { get; set; }

    public bool Healthy { get; set; } = true;

    /// <summary>
    /// Offline disks exist and are worth saying so: a disk that is present but
    /// not online has no drive letters, so it would otherwise be invisible.
    /// </summary>
    public bool Online { get; set; } = true;

    /// <summary>
    /// How much of the disk is inside a partition. The rest is unallocated --
    /// the one thing about a disk's layout that is worth a line, because it
    /// says the disk is not fully in use and nothing else reports it.
    /// </summary>
    public long AllocatedBytes { get; set; }

    public double Gb => Bytes / (1024.0 * 1024 * 1024);

    public double UnallocatedGb =>
        Bytes > 0 && AllocatedBytes > 0 && Bytes > AllocatedBytes
            ? (Bytes - AllocatedBytes) / (1024.0 * 1024 * 1024)
            : 0;

    /// <summary>"Disk 1  ·  WDS250G3X0C-00SJG0" -- what to head a panel with.</summary>
    public string Title => Model.Length > 0 ? $"Disk {Number}  ·  {Model}" : $"Disk {Number}";
}

/// <summary>
/// Where a drive letter actually lives: which physical disk, which partition
/// of it, and how the filesystem on it is set up.
/// </summary>
public sealed class DriveDetail
{
    public required string Letter { get; init; }
    public int? DiskNumber { get; init; }
    public int PartitionNumber { get; init; }
    public long PartitionBytes { get; init; }
    public long PartitionOffset { get; init; }
    public bool IsBoot { get; init; }
    public string FileSystem { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public PhysicalDisk? Disk { get; init; }

    public double PartitionGb => PartitionBytes / (1024.0 * 1024 * 1024);

    /// <summary>
    /// How much of the physical disk this partition takes. A drive that is one
    /// partition of four reads very differently from one that has the disk to
    /// itself, and the number says which.
    /// </summary>
    public double ShareOfDisk => Disk is { Bytes: > 0 }
        ? Math.Clamp(PartitionBytes * 100.0 / Disk.Bytes, 0, 100)
        : 0;
}

/// <summary>
/// What the processor is, rather than what it is doing.
///
/// Read once and carried on every snapshot: the CPU tab had nothing but a core
/// count and a temperature on it, which is the one tab a person opens expecting
/// to be told which chip this machine has.
/// </summary>
public sealed class CpuInfo
{
    public string Name { get; init; } = string.Empty;
    public string Vendor { get; init; } = string.Empty;
    public int Sockets { get; init; }
    public int Cores { get; init; }
    public int Logical { get; init; }

    /// <summary>The rated clock, in MHz. Zero when WMI would not say.</summary>
    public int BaseMhz { get; init; }

    public int L2Kb { get; init; }
    public int L3Kb { get; init; }

    /// <summary>Null when the firmware does not report it either way.</summary>
    public bool? Virtualization { get; init; }

    /// <summary>True once anything at all was read.</summary>
    public bool Known => Name.Length > 0 || Logical > 0;
}

/// <summary>
/// The graphics adapter, from the WDDM performance counters.
///
/// `Present` is false on a machine whose driver publishes no counters at all,
/// which is a remote session or a very old adapter; the UI leaves the section
/// out rather than drawing an empty graph.
/// </summary>
public sealed class Gpu
{
    private readonly IReadOnlyList<GpuEngine> _engines = Array.Empty<GpuEngine>();

    public bool Present { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Driver { get; init; } = string.Empty;

    /// <summary>The busiest engine type, which is what Task Manager calls "GPU".</summary>
    public int Usage { get; init; }

    public IReadOnlyList<GpuEngine> Engines
    {
        get => _engines;
        init => _engines = value is null || value.Count == 0
            ? Array.Empty<GpuEngine>()
            : value.ToArray();
    }

    /// <summary>Dedicated video memory in use. Zero on an adapter with none.</summary>
    public double DedicatedGb { get; init; }

    /// <summary>System memory the adapter has borrowed.</summary>
    public double SharedGb { get; init; }

    /// <summary>
    /// No consumer iGPU exposes a temperature Windows will hand out, and a
    /// discrete card needs an undocumented WDDM call to ask. Task Manager
    /// shows "N/A" on this machine for the same reason.
    /// </summary>
    public int? Temp => null;
}

/// <summary>One kind of work the adapter does: 3D, Copy, Video decode.</summary>
public sealed class GpuEngine
{
    public required string Name { get; init; }
    public int Usage { get; init; }
}
