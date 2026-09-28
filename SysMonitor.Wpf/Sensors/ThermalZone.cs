using SysMonitor.Native;

namespace SysMonitor.Sensors;

/// <summary>One ACPI thermal zone as Windows reports it.</summary>
internal readonly record struct Zone(string Name, int Celsius);

/// <summary>
/// CPU temperature from the ACPI thermal zones.
///
/// There is no driver-free way to read the CPU die sensor on Windows: that
/// lives behind an MSR, which needs a kernel driver. The thermal zone is the
/// closest thing available to an unprivileged process, and on most machines
/// the zone the firmware calls TZ00 tracks the CPU package closely enough to
/// be worth showing. It is a measurement, not a model -- so the widget shows
/// it without the '~' it puts on the load-derived estimate.
///
/// Source matters here. The obvious class, MSAcpi_ThermalZoneTemperature in
/// root\WMI, returns "access denied" to a normal user, which is why the
/// Python build's opt-in setting never produced a reading on this machine.
/// The performance-counter class in root\cimv2 exposes the same zones and
/// needs no elevation.
/// </summary>
internal static class ThermalZone
{
    private const string Namespace = @"root\cimv2";
    private const string Query =
        "SELECT Name, Temperature, HighPrecisionTemperature FROM " +
        "Win32_PerfFormattedData_Counters_ThermalZoneInformation";

    /// <summary>Kelvin to Celsius, rejecting readings no room ever sees.</summary>
    public static int? FromKelvin(double kelvin)
    {
        double celsius = kelvin - 273.15;
        return celsius is > -20 and < 130 ? (int)Math.Round(celsius) : null;
    }

    /// <summary>
    /// Pick the zone to show. A name the firmware marked as the CPU wins;
    /// otherwise the hottest zone, because a machine reporting several is
    /// usually reporting the CPU as one of them and the warmest is the one
    /// worth warning about.
    /// </summary>
    public static Zone? Pick(IReadOnlyList<Zone> zones)
    {
        if (zones.Count == 0)
        {
            return null;
        }
        foreach (Zone zone in zones)
        {
            string name = zone.Name.ToUpperInvariant();
            if (name.Contains("CPU") || name.EndsWith("TZ00") || name.EndsWith("TZ0"))
            {
                return zone;
            }
        }
        return zones.OrderByDescending(z => z.Celsius).First();
    }

    /// <summary>
    /// Read every zone. HighPrecisionTemperature is in tenths of a Kelvin and
    /// is preferred where the firmware provides it; Temperature is whole
    /// Kelvin, which quantises the reading to one degree steps.
    /// </summary>
    public static List<Zone> Read()
    {
        var zones = new List<Zone>();
        foreach (var row in Wmi.Query(Namespace, Query,
                                      "Name", "Temperature", "HighPrecisionTemperature"))
        {
            string name = row.GetValueOrDefault("Name")?.ToString() ?? string.Empty;
            int? celsius = null;

            if (ToDouble(row.GetValueOrDefault("HighPrecisionTemperature")) is double tenths
                && tenths > 0)
            {
                celsius = FromKelvin(tenths / 10.0);
            }
            if (celsius is null
                && ToDouble(row.GetValueOrDefault("Temperature")) is double kelvin
                && kelvin > 0)
            {
                celsius = FromKelvin(kelvin);
            }
            if (celsius is not null)
            {
                zones.Add(new Zone(name, celsius.Value));
            }
        }
        return zones;
    }

    private static double? ToDouble(object? value) => value switch
    {
        null => null,
        double d => d,
        float f => f,
        int i => i,
        uint u => u,
        long l => l,
        ulong u => u,
        // WMI hands numbers back as strings more often than it should.
        string s when double.TryParse(s, out double parsed) => parsed,
        _ => null,
    };
}
