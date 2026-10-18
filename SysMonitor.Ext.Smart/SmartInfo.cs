namespace SysMonitor.Ext.Smart;

/// <summary>Why the SMART extension has, or has not, got disks to show.</summary>
internal enum SmartState
{
    /// <summary>Able to read, but the first scan has not come back yet.</summary>
    Waiting,
    Ok,
    /// <summary>DiskInfoCore.dll cannot read disks without administrator rights.</summary>
    NeedAdmin,
    /// <summary>DiskInfoCore.dll is not in the extension folder, or is not a version we speak.</summary>
    Unavailable,
    /// <summary>The last scan failed; any older reading was thrown away.</summary>
    Failed,
}

/// <summary>A disk's overall verdict, as CrystalDiskInfo's "Good / Caution / Bad".</summary>
internal enum DiskHealth { Unknown = 0, Good = 1, Caution = 2, Bad = 3 }

/// <summary>
/// One physical disk as SMART describes it. A null number means the disk has no
/// such value; it is never a stand-in for zero.
/// </summary>
internal sealed record SmartDisk(
    int Drive,
    string Model,
    string Serial,
    string Firmware,
    string Interface,
    string Media,
    int? TempC,
    int? LifePercent,
    DiskHealth Health,
    long? PowerOnHours,
    bool Full)
{
    /// <summary>"Disk 2", the same name the Disk tabs use.</summary>
    public string Title => $"Disk {Drive}";
}

/// <summary>What the SMART extension has: a state and, when Ok, the disks.</summary>
internal sealed record SmartInfo(SmartState State, IReadOnlyList<SmartDisk> Disks)
{
    public static readonly SmartInfo Waiting = new(SmartState.Waiting, Array.Empty<SmartDisk>());

    /// <summary>Is there anything to draw as a disk?</summary>
    public bool HasDisks => State == SmartState.Ok && Disks.Count > 0;
}
