namespace SysMonitor.Model;

/// <summary>
/// One reading of the machine.  A snapshot is built by the sampler, published
/// as a whole, and never mutated afterwards, so the UI thread can read the
/// latest one without a lock.
/// </summary>
public sealed class Snapshot
{
    public IReadOnlyList<Core> Cores { get; init; } = Array.Empty<Core>();
    public Ram Ram { get; init; } = new();
    public IReadOnlyList<Disk> Disks { get; init; } = Array.Empty<Disk>();
    public IReadOnlyList<Adapter> Adapters { get; init; } = Array.Empty<Adapter>();
    public IReadOnlyList<Module> Modules { get; init; } = Array.Empty<Module>();
    public int CpuTotal { get; init; }
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
    public int Usage { get; init; }
    public double UsedGb { get; init; }
    public double TotalGb { get; init; }
    public double ReadMb { get; init; }
    public double WriteMb { get; init; }
    public int? Temp { get; init; }
    public bool Estimated { get; init; }

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
