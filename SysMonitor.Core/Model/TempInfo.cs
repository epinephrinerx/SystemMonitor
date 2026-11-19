namespace SysMonitor.Model;

/// <summary>Where a disk's temperature came from.</summary>
public enum TempSource
{
    None,
    /// <summary>The Windows storage stack. Needs no elevation.</summary>
    Storage,
    /// <summary>From an extension (e.g. SMART through DiskInfoCore).</summary>
    Smart,
}

public readonly record struct TempResult(int? Celsius, TempSource Source);
