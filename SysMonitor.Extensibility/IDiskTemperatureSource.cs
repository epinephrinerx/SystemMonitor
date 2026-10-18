namespace SysMonitor.Extensibility;

/// <summary>
/// Optional interface an extension can implement to supply disk temperatures
/// (e.g. from S.M.A.R.T., NVMe pass-through, or proprietary drivers) to the host.
/// </summary>
public interface IDiskTemperatureSource
{
    /// <summary>
    /// Returns the current temperature in degrees Celsius keyed by zero-based physical drive index.
    /// The host bounds valid drives to 0..255 and valid temperatures to 1..149 °C.
    /// Non-compliant entries will be discarded by the host.
    /// </summary>
    /// <returns>A dictionary mapping drive index to Celsius temperature.</returns>
    IReadOnlyDictionary<int, int> GetDiskTemperatures();
}
