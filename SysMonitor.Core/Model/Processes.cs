namespace SysMonitor.Model;

/// <summary>
/// One process as the Apps and Processes pages show it. Platform-neutral: the
/// Linux reader fills the fields it can and the Windows reader (R-031) will fill
/// the same ones from its own counters.
/// </summary>
public sealed record ProcessRow
{
    public required int Pid { get; init; }
    public required string Name { get; init; }

    /// <summary>Clock ticks since boot, or null when the stat file would not say.</summary>
    public ulong? StartTime { get; init; }

    /// <summary>The real UID when it can be read.</summary>
    public int? Uid { get; init; }

    /// <summary>The account name, or the UID as text when no passwd entry matches.</summary>
    public string User { get; init; } = string.Empty;

    public int ParentPid { get; init; }
    public bool KernelThread { get; init; }

    /// <summary>False when the kernel hides the process; the sizes are then zero.</summary>
    public bool Readable { get; init; } = true;

    /// <summary>Null on the first sight of a process, not zero: it is unknown, not idle.</summary>
    public double? CpuPercent { get; init; }

    /// <summary>Resident memory in bytes.</summary>
    public long MemoryBytes { get; init; }

    /// <summary>Null when the driver cannot report per-process GPU use.</summary>
    public double? GpuPercent { get; init; }

    /// <summary>Null when the driver reports no per-process video memory.</summary>
    public long? GpuMemoryBytes { get; init; }

    /// <summary>The command line, NULs turned into spaces, or empty when unreadable.</summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>The unified (<c>0::</c>) cgroup path, or empty.</summary>
    public string CgroupPath { get; init; } = string.Empty;

    /// <summary>The user systemd unit whose MainPID this process is, or null.</summary>
    public string? ServiceUnit { get; init; }

    public static IReadOnlyList<ProcessRow> Empty { get; } = Array.Empty<ProcessRow>();
}

/// <summary>
/// One app instance: a top-level <c>app-*.scope</c> or <c>app-*.service</c>
/// cgroup and the processes under it.
/// </summary>
public sealed record AppInstance
{
    public required string Unit { get; init; }
    public required IReadOnlyList<ProcessRow> Processes { get; init; }

    /// <summary>Sum of the members; null only when every member is unknown.</summary>
    public double? CpuPercent { get; init; }

    public long MemoryBytes { get; init; }

    /// <summary>Sum of the members; null only when every member is unknown.</summary>
    public double? GpuPercent { get; init; }

    public long? GpuMemoryBytes { get; init; }
}

/// <summary>
/// One app, with every instance that resolves to the same desktop entry (or,
/// when it does not resolve, the same desktop id) grouped together.
/// </summary>
public sealed record AppEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Icon { get; init; }
    public string? DesktopFile { get; init; }
    public string? Exec { get; init; }

    /// <summary>True when one of its units is an <c>@autostart</c> service.</summary>
    public bool Autostart { get; init; }

    public required IReadOnlyList<AppInstance> Instances { get; init; }

    /// <summary>Sum over every instance; null only when every member is unknown.</summary>
    public double? CpuPercent { get; init; }

    public long MemoryBytes { get; init; }

    /// <summary>Sum over every instance; null only when every member is unknown.</summary>
    public double? GpuPercent { get; init; }

    public long? GpuMemoryBytes { get; init; }

    public int InstanceCount => Instances.Count;
}
