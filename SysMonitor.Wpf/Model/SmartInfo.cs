namespace SysMonitor.Model;

/// <summary>Why the SMART module has, or has not, got disks to show.</summary>
public enum SmartState
{
    /// <summary>The module is switched off in the display settings. Nothing is read.</summary>
    Off,
    /// <summary>On and able to read, but the first scan has not come back yet.</summary>
    Waiting,
    Ok,
    /// <summary>DiskInfoCore.dll cannot read disks without administrator rights.</summary>
    NeedAdmin,
    /// <summary>DiskInfoCore.dll is not next to the exe, or is not a version we speak.</summary>
    Unavailable,
    /// <summary>The last scan failed; any older reading was thrown away.</summary>
    Failed,
}

/// <summary>A disk's overall verdict, as CrystalDiskInfo's "Good / Caution / Bad".</summary>
public enum DiskHealth { Unknown = 0, Good = 1, Caution = 2, Bad = 3 }

/// <summary>
/// One physical disk as SMART describes it. A null number means the disk has no
/// such value; it is never a stand-in for zero.
/// </summary>
public sealed record SmartDisk(
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

/// <summary>What the SMART module has this tick: a state and, when Ok, the disks.</summary>
public sealed record SmartInfo(SmartState State, IReadOnlyList<SmartDisk> Disks)
{
    public static readonly SmartInfo Off = new(SmartState.Off, Array.Empty<SmartDisk>());

    /// <summary>Is there anything to draw as a disk?</summary>
    public bool HasDisks => State == SmartState.Ok && Disks.Count > 0;
}
