using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SysMonitor.Linux.Sensors
{
    public static class HwmonThermal
    {
        public static int? PickCpu(IReadOnlyList<(string Name, int MilliCelsius)> zones)
        {
            if (zones == null) return null;

            var preferredNames = new[] { "coretemp", "k10temp", "zenpower", "x86_pkg_temp", "cpu_thermal", "cpu-thermal" };
            var validZones = zones
                .Where(z => z.MilliCelsius > -20000 && z.MilliCelsius < 130000)
                .Select(z => (z.Name, Celsius: (int)Math.Round(z.MilliCelsius / 1000.0, MidpointRounding.AwayFromZero)))
                .ToList();

            var preferredZone = validZones.FirstOrDefault(z => preferredNames.Any(pn => string.Equals(z.Name?.Trim(), pn, StringComparison.OrdinalIgnoreCase)));
            if (preferredZone.Name != null)
            {
                return preferredZone.Celsius;
            }

            if (validZones.Any())
            {
                return validZones.Max(z => z.Celsius);
            }

            return null;
        }
    }
}
