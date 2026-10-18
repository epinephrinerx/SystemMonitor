namespace SysMonitor.Extensibility;

/// <summary>
/// Bitmask describing what information the host is currently interested in
/// receiving from an extension. Extensions implementing <see cref="IDemandAware"/>
/// can use this signal to avoid expensive background polling when no host
/// feature requires their data.
/// </summary>
[Flags]
public enum ExtensionDemand
{
    /// <summary>
    /// Neither views nor background metrics are required by the host.
    /// The extension should pause background polling where possible.
    /// </summary>
    None = 0,

    /// <summary>
    /// The host is rendering or can switch to the extension's views.
    /// Extensions implementing <see cref="IViewProvider"/> should keep view models fresh.
    /// </summary>
    Views = 1 << 0,

    /// <summary>
    /// The host needs disk temperatures to enrich its storage readings.
    /// Extensions implementing <see cref="IDiskTemperatureSource"/> should sample drive temperatures.
    /// </summary>
    DiskTemperature = 1 << 1,
}

/// <summary>
/// Optional interface an extension can implement to receive host demand changes.
/// </summary>
public interface IDemandAware
{
    /// <summary>
    /// Called by the host when the set of requested features changes.
    /// Always called at least once before the first view read or metric query.
    /// </summary>
    /// <param name="demand">Current active demand flags.</param>
    void SetDemand(ExtensionDemand demand);
}
