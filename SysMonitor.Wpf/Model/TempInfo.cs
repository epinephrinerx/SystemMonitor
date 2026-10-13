namespace SysMonitor.Model;

/// <summary>Where a disk's temperature came from.</summary>
public enum TempSource
{
    None,
    /// <summary>The Windows storage stack. Needs no elevation.</summary>
    Storage,
    /// <summary>SMART through DiskInfoCore. Needs an elevated process.</summary>
    Smart,
}

public readonly record struct TempResult(int? Celsius, TempSource Source);
