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
