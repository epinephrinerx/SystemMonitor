using System.Globalization;

namespace SysMonitor.Linux.Sensors;

/// <summary>What the thermal sources said about the processor.</summary>
public sealed class CpuThermalReading
{
    public int? CpuTemp { get; init; }

    /// <summary>True when the number came from the board sensor, not the CPU.</summary>
    public bool Estimated { get; init; }

    /// <summary>One entry per logical CPU, in cpu order. Null where unknown.</summary>
    public IReadOnlyList<int?> CoreTemps { get; init; } = Array.Empty<int?>();
}

/// <summary>
/// Package and per-core temperatures.  Preference: Intel coretemp, then AMD
/// k10temp/zenpower, then the ACPI thermal zone, then the board sensor marked
/// as an estimate.  Nothing here needs root: hwmon and thermal_zone are
/// world-readable.
/// </summary>
public static class ThermalReader
{
    public static CpuThermalReading Read(string root = "/", int logicalCount = 0)
    {
        IReadOnlyList<HwmonSensor> sensors = Hwmon.Enumerate(root);
        IReadOnlyList<HwmonSensor> coretemps = sensors
            .Where(sensor => string.Equals(sensor.Name, "coretemp", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (coretemps.Count > 0)
        {
            return new CpuThermalReading
            {
                CpuTemp = PackageTemp(coretemps),
                CoreTemps = CoreTemps(root, coretemps, logicalCount),
            };
        }

        HwmonSensor? amd = Find(sensors, "k10temp") ?? Find(sensors, "zenpower");
        if (amd is not null)
        {
            return new CpuThermalReading
            {
                CpuTemp = Hwmon.Pick(amd, "Tctl", "Tdie"),
                CoreTemps = Empty(logicalCount),
            };
        }

        int? packageZone = ThermalZone(root, "x86_pkg_temp");
        if (packageZone is not null)
        {
            return new CpuThermalReading
            {
                CpuTemp = packageZone,
                CoreTemps = Empty(logicalCount),
            };
        }

        HwmonSensor? acpi = Find(sensors, "acpitz");
        int? board = acpi is null ? ThermalZone(root, "acpitz") : Hwmon.Pick(acpi);
        return new CpuThermalReading
        {
            CpuTemp = board,
            Estimated = board is not null,
            CoreTemps = Empty(logicalCount),
        };
    }

    /// <summary>
    /// The socket-0 package temperature when there is one, otherwise the first
    /// package the coretemp chips report.  Each chip names its own package in
    /// its label, so a second socket's "Package id 1" is never mistaken for
    /// the one to show.
    /// </summary>
    public static int? PackageTemp(IReadOnlyList<HwmonSensor> coretemps)
    {
        int? first = null;

        foreach (HwmonSensor sensor in coretemps)
        {
            int package = PackageOf(sensor);
            int? celsius = Hwmon.Exact(sensor, $"Package id {package}");
            if (celsius is null)
            {
                continue;
            }

            if (package == 0)
            {
                return celsius;
            }

            first ??= celsius;
        }

        return first;
    }

    /// <summary>
    /// The core temperature for one chip: core labels are matched per socket.
    /// </summary>
    public static IReadOnlyList<int?> CoreTemps(string root, HwmonSensor coretemp, int logicalCount) =>
        CoreTemps(root, new[] { coretemp }, logicalCount);

    /// <summary>
    /// "Core 3" labels give the temperature of physical core 3 of that chip's
    /// package; both hyper-threads of the core carry it.  A CPU is matched on
    /// the pair (physical_package_id, core_id), so two sockets with identically
    /// named cores keep their own readings.  A machine without core labels has
    /// no per-core reading, so every entry stays null.
    /// </summary>
    public static IReadOnlyList<int?> CoreTemps(
        string root, IReadOnlyList<HwmonSensor> coretemps, int logicalCount)
    {
        var byCore = new Dictionary<(int Package, int Core), int>();
        foreach (HwmonSensor sensor in coretemps)
        {
            int package = PackageOf(sensor);
            foreach (HwmonTemp temp in sensor.Temps)
            {
                if (CoreNumber(temp.Label) is int core && temp.Celsius is int celsius)
                {
                    byCore[(package, core)] = celsius;
                }
            }
        }

        var result = new int?[Math.Max(0, logicalCount)];
        if (byCore.Count == 0)
        {
            return result;
        }

        for (int cpu = 0; cpu < result.Length; cpu++)
        {
            int? coreId = SysFs.ReadInt(root, "sys", "devices", "system", "cpu",
                                        $"cpu{cpu}", "topology", "core_id");
            if (coreId is not int id)
            {
                continue;
            }

            int package = SysFs.ReadInt(root, "sys", "devices", "system", "cpu",
                                        $"cpu{cpu}", "topology", "physical_package_id") ?? 0;
            if (byCore.TryGetValue((package, id), out int celsius))
            {
                result[cpu] = celsius;
            }
        }

        return result;
    }

    /// <summary>
    /// Which package a coretemp chip measures: its "Package id N" label when
    /// present, otherwise the <c>coretemp.N</c> platform device it hangs under
    /// (the <c>/sys/class/hwmon</c> link is resolved before walking up).
    /// Zero when neither says anything, which is the single-socket default.
    /// </summary>
    public static int PackageOf(HwmonSensor sensor)
    {
        foreach (HwmonTemp temp in sensor.Temps)
        {
            if (PackageNumber(temp.Label) is int fromLabel)
            {
                return fromLabel;
            }
        }

        // The class directory is a link into /sys/devices; walking up the link
        // itself would only ever reach /sys/class.  Resolve it when it still is
        // one, so a sensor built from a /sys/class path works too.
        string path = sensor.Path.TrimEnd('/');
        string resolved = SysFs.ResolvedPath(path);
        if (resolved.Length > 0)
        {
            path = resolved;
        }

        string? dir = Path.GetDirectoryName(path);
        for (int depth = 0; depth < 3 && !string.IsNullOrEmpty(dir); depth++)
        {
            if (PackageNumber(Path.GetFileName(dir)) is int fromDevice)
            {
                return fromDevice;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return 0;
    }

    /// <summary>Number of logical CPUs the kernel exposes below /sys.</summary>
    public static int LogicalCount(string root = "/")
    {
        int count = 0;
        foreach (string dir in SysFs.Directories(root, "sys", "devices", "system", "cpu"))
        {
            if (CpuInfoReader.IsCpu(Path.GetFileName(dir)))
            {
                count++;
            }
        }
        return count;
    }

    public static int? ThermalZone(string root, string type)
    {
        foreach (string zone in SysFs.Directories(root, "sys", "class", "thermal"))
        {
            if (!Path.GetFileName(zone).StartsWith("thermal_zone", StringComparison.Ordinal))
            {
                continue;
            }

            string? zoneType = SysFs.ReadTrimmed(zone, "type");
            if (!string.Equals(zoneType, type, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            long? milli = SysFs.ReadLong(zone, "temp");
            int? celsius = SysFs.Celsius(milli);
            if (celsius is not null)
            {
                return celsius;
            }
        }

        return null;
    }

    private static int? PackageNumber(string label)
    {
        const string packagePrefix = "Package id ";
        if (label.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(label[packagePrefix.Length..].Trim(), NumberStyles.None,
                            CultureInfo.InvariantCulture, out int package))
        {
            return package;
        }

        const string devicePrefix = "coretemp.";
        if (label.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(label[devicePrefix.Length..], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int device))
        {
            return device;
        }

        return null;
    }

    private static int? CoreNumber(string label)
    {
        const string prefix = "Core ";
        if (!label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(label[prefix.Length..].Trim(), NumberStyles.None,
                            CultureInfo.InvariantCulture, out int number)
            ? number
            : null;
    }

    private static HwmonSensor? Find(IReadOnlyList<HwmonSensor> sensors, string name)
    {
        foreach (HwmonSensor sensor in sensors)
        {
            if (string.Equals(sensor.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return sensor;
            }
        }
        return null;
    }

    private static IReadOnlyList<int?> Empty(int logicalCount) =>
        new int?[Math.Max(0, logicalCount)];
}
