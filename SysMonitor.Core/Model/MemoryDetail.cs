namespace SysMonitor.Model;

/// <summary>One line of <c>/proc/swaps</c>.</summary>
public sealed record SwapDevice
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public long SizeBytes { get; init; }
    public long UsedBytes { get; init; }
    public int Priority { get; init; }
}

/// <summary>
/// One installed memory module, from systemd-udev's decoded SMBIOS type 17.
/// Serial, asset tag and part number are deliberately absent: they identify the
/// machine and are never read.
/// </summary>
public sealed record MemoryModule
{
    public string Slot { get; init; } = string.Empty;
    public long? SizeBytes { get; init; }
    public string Type { get; init; } = string.Empty;
    public string TypeDetail { get; init; } = string.Empty;
    public string FormFactor { get; init; } = string.Empty;
    public int? ConfiguredSpeedMts { get; init; }
    public int? SpeedMts { get; init; }
    public string Manufacturer { get; init; } = string.Empty;
    public int? Rank { get; init; }

    /// <summary>
    /// Configured voltage in volts, or null when SMBIOS reports no reading or a
    /// placeholder outside the plausible 0.5–5 V range.
    /// </summary>
    public double? ConfiguredVoltage { get; init; }
}

/// <summary>What the board reports about its memory slots and modules.</summary>
public sealed record MemoryLayout
{
    public int? SlotsUsed { get; init; }
    public int? SlotsTotal { get; init; }
    public long? MaxCapacityBytes { get; init; }
    public IReadOnlyList<MemoryModule> Modules { get; init; } = Array.Empty<MemoryModule>();

    public static MemoryLayout Empty { get; } = new();
}
