namespace SysMonitor.Linux.Sensors;

/// <summary>One temperature input of a hwmon sensor.</summary>
public sealed record HwmonTemp(string Label, int? Celsius);

/// <summary>
/// One hwmon chip and its temperature inputs.  <c>Path</c> is the chip's real
/// sysfs directory (the <c>/sys/class/hwmon</c> link resolved), so a caller may
/// walk its parents to find the device it hangs under.
/// </summary>
public sealed record HwmonSensor(string Name, string Path, IReadOnlyList<HwmonTemp> Temps);

/// <summary>
/// /sys/class/hwmon.  The chip's <c>name</c> says what it measures; the
/// <c>tempN_label</c> files say which part.
/// </summary>
public static class Hwmon
{
    public static IReadOnlyList<HwmonSensor> Enumerate(string root = "/")
    {
        var sensors = new List<HwmonSensor>();

        foreach (string dir in SysFs.Directories(root, "sys", "class", "hwmon"))
        {
            if (!Path.GetFileName(dir).StartsWith("hwmon", StringComparison.Ordinal))
            {
                continue;
            }

            string? name = SysFs.ReadTrimmed(dir, "name");
            if (name is null)
            {
                continue;
            }

            var temps = new List<HwmonTemp>();
            foreach (string file in SysFs.Files(dir))
            {
                string fileName = Path.GetFileName(file);
                if (!fileName.StartsWith("temp", StringComparison.Ordinal)
                    || !fileName.EndsWith("_input", StringComparison.Ordinal))
                {
                    continue;
                }

                string number = fileName["temp".Length..^"_input".Length];
                if (!int.TryParse(number, out _))
                {
                    continue;
                }

                long? milli = SysFs.ReadLong(dir, fileName);
                string label = SysFs.ReadTrimmed(dir, $"temp{number}_label") ?? string.Empty;
                temps.Add(new HwmonTemp(label, SysFs.Celsius(milli)));
            }

            // /sys/class/hwmon/hwmonN is a link into /sys/devices; keep the
            // resolved target so callers that walk up reach coretemp.N rather
            // than /sys/class.
            string path = SysFs.ResolvedPath(dir);
            sensors.Add(new HwmonSensor(name, path.Length > 0 ? path : dir, temps));
        }

        return sensors;
    }

    /// <summary>The first reading with a usable value, by preference order.</summary>
    public static int? Pick(HwmonSensor sensor, params string[] labelPreference)
    {
        foreach (string wanted in labelPreference)
        {
            foreach (HwmonTemp temp in sensor.Temps)
            {
                if (string.Equals(temp.Label, wanted, StringComparison.OrdinalIgnoreCase)
                    && temp.Celsius is not null)
                {
                    return temp.Celsius;
                }
            }
        }

        foreach (HwmonTemp temp in sensor.Temps)
        {
            if (temp.Celsius is not null)
            {
                return temp.Celsius;
            }
        }

        return null;
    }

    /// <summary>
    /// One exact label's reading, or null.  Unlike <see cref="Pick"/> there is
    /// no fallback: a caller that asks for "Package id 1" must not be handed a
    /// core temperature instead.
    /// </summary>
    public static int? Exact(HwmonSensor sensor, string label)
    {
        foreach (HwmonTemp temp in sensor.Temps)
        {
            if (string.Equals(temp.Label, label, StringComparison.OrdinalIgnoreCase)
                && temp.Celsius is int celsius)
            {
                return celsius;
            }
        }

        return null;
    }

    /// <summary>
    /// temp1_input of the first hwmon chip attached to a device directory.
    /// A PCI device hangs its chip directly (device/hwmon3); a SCSI target
    /// nests it one level (device/hwmon/hwmon3).
    /// </summary>
    public static int? DeviceTemperature(string devicePath)
    {
        if (!Directory.Exists(devicePath))
        {
            return null;
        }

        foreach (string child in SysFs.Directories(devicePath))
        {
            if (Path.GetFileName(child).StartsWith("hwmon", StringComparison.Ordinal))
            {
                int? celsius = SysFs.Celsius(SysFs.ReadLong(child, "temp1_input"));
                if (celsius is not null)
                {
                    return celsius;
                }
            }
        }

        foreach (string child in SysFs.Directories(devicePath, "hwmon"))
        {
            if (Path.GetFileName(child).StartsWith("hwmon", StringComparison.Ordinal))
            {
                int? celsius = SysFs.Celsius(SysFs.ReadLong(child, "temp1_input"));
                if (celsius is not null)
                {
                    return celsius;
                }
            }
        }

        return null;
    }
}
