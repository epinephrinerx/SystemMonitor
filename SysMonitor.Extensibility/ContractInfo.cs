namespace SysMonitor.Extensibility;

/// <summary>
/// Information about the extensibility contract version.
/// </summary>
public static class ContractInfo
{
    /// <summary>
    /// Current contract version (1.1).
    /// </summary>
    public static readonly Version Version = new(1, 1);

    /// <summary>
    /// Alias representing the current contract version.
    /// </summary>
    public static Version Current => Version;
}
