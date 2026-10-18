namespace SysMonitor.Ext.Smart;

/// <summary>
/// Turns what DiskInfoCore returned into what the SMART extension shows. Pure, so
/// the rules (what "needs administrator" means, when a number is a number) are
/// testable without a disk.
/// </summary>
internal static class SmartBoard
{
    /// <summary>
    /// The extension's state and disks for one tick.
    /// </summary>
    /// <param name="available">Is DiskInfoCore.dll here and a version we speak?</param>
    /// <param name="elevated">Is the process running as administrator?</param>
    /// <param name="last">How the last scan ended; null until one has.</param>
    /// <param name="readings">The disks of the last good scan, keyed by drive number.</param>
    public static SmartInfo Build(bool available, bool elevated,
                                  SmartScanStatus? last,
                                  IReadOnlyDictionary<int, SmartReading> readings)
    {
        if (!available)
        {
            return new SmartInfo(SmartState.Unavailable, Array.Empty<SmartDisk>());
        }
        if (!elevated)
        {
            return new SmartInfo(SmartState.NeedAdmin, Array.Empty<SmartDisk>());
        }

        switch (last)
        {
            case null:
                return new SmartInfo(SmartState.Waiting, Array.Empty<SmartDisk>());
            case SmartScanStatus.NeedAdmin:
                return new SmartInfo(SmartState.NeedAdmin, Array.Empty<SmartDisk>());
            case SmartScanStatus.Unavailable:
                return new SmartInfo(SmartState.Unavailable, Array.Empty<SmartDisk>());
            case SmartScanStatus.Failed:
                return new SmartInfo(SmartState.Failed, Array.Empty<SmartDisk>());
        }

        SmartDisk[] disks = readings.Values
            .OrderBy(r => r.Drive)
            .Select(ToDisk)
            .ToArray();
        return new SmartInfo(SmartState.Ok, disks);
    }

    internal static SmartDisk ToDisk(SmartReading r) => new(
        r.Drive,
        r.Model,
        r.Serial,
        r.Firmware,
        InterfaceName(r.InterfaceKind),
        MediaName(r.Media),
        // Zero is not a reading, the same rule the storage-stack temperature follows (> 0 and < 150).
        r.TempC is > 0 and < 150 ? r.TempC : null,
        r.LifePercent is >= 0 and <= 100 ? r.LifePercent : null,
        (DiskHealth)(int)r.Health,
        r.PowerOnHours,
        r.Full);

    /// <summary>DI_IF_* from core_api.h. Product names, so they are not translated.</summary>
    internal static string InterfaceName(int kind) => kind switch
    {
        1 => "PATA",
        2 => "SATA",
        3 => "USB",
        4 => "IEEE 1394",
        5 => "SCSI",
        6 => "NVMe",
        _ => string.Empty,
    };

    /// <summary>DI_MEDIA_* from core_api.h.</summary>
    internal static string MediaName(int media) => media switch
    {
        1 => "HDD",
        2 => "SSD",
        _ => string.Empty,
    };
}
