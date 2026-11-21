using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// The board's memory slots, from systemd-udev's decoded SMBIOS type 17 in
/// <c>/run/udev/data/+dmi:id</c>. The file is world-readable, so no root is
/// needed, and it does not change while the machine runs.
///
/// Serial number, asset tag and part number are skipped while parsing: they
/// identify the machine and are never stored or shown.
/// </summary>
public static class MemoryModules
{
    private static readonly HashSet<string> Private = new(StringComparer.Ordinal)
    {
        "SERIAL_NUMBER",
        "ASSET_TAG",
        "PART_NUMBER",
    };

    /// <summary>
    /// Real DIMMs are configured between 0.5 V and 5 V. Firmware sometimes writes
    /// a placeholder here instead (this machine reports <c>1</c> mV), so anything
    /// outside that window is treated as unknown rather than shown as "0.00 V".
    /// </summary>
    private const long MinMillivolts = 500;

    private const long MaxMillivolts = 5000;

    public static MemoryLayout Read(string root = "/") =>
        Parse(SysFs.ReadText(SysFs.Path(root, "run", "udev", "data", "+dmi:id")));

    public static MemoryLayout Parse(string? text)
    {
        if (text is null || text.Length == 0)
        {
            return MemoryLayout.Empty;
        }

        var array = new Dictionary<string, string>(StringComparer.Ordinal);
        var devices = new SortedDictionary<int, Dictionary<string, string>>();

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (!line.StartsWith("E:", StringComparison.Ordinal))
            {
                // Only the decoded environment lines matter; I: is a device id.
                continue;
            }

            int equals = line.IndexOf('=', 2);
            if (equals < 0)
            {
                continue;
            }

            string key = line[2..equals].Trim();
            string value = line[(equals + 1)..].Trim();

            if (key.StartsWith("MEMORY_ARRAY_", StringComparison.Ordinal))
            {
                array[key["MEMORY_ARRAY_".Length..]] = value;
                continue;
            }

            if (!key.StartsWith("MEMORY_DEVICE_", StringComparison.Ordinal))
            {
                continue;
            }

            int underscore = key.IndexOf('_', "MEMORY_DEVICE_".Length);
            if (underscore < 0
                || !int.TryParse(
                    key["MEMORY_DEVICE_".Length..underscore],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int index)
                || index < 0)
            {
                continue;
            }

            string field = key[(underscore + 1)..];
            if (Private.Contains(field))
            {
                // Never keep what the parser must not leak.
                continue;
            }

            if (!devices.TryGetValue(index, out Dictionary<string, string>? fields))
            {
                fields = new Dictionary<string, string>(StringComparer.Ordinal);
                devices[index] = fields;
            }
            fields[field] = value;
        }

        var modules = new List<MemoryModule>();
        foreach (Dictionary<string, string> fields in devices.Values)
        {
            long? size = Number(fields, "SIZE");
            if (size is not long bytes || bytes <= 0)
            {
                // An empty slot is counted in the total but is not a module.
                continue;
            }

            modules.Add(new MemoryModule
            {
                Slot = Text(fields, "LOCATOR"),
                SizeBytes = bytes,
                Type = Text(fields, "TYPE"),
                TypeDetail = Text(fields, "TYPE_DETAIL"),
                FormFactor = Text(fields, "FORM_FACTOR"),
                ConfiguredSpeedMts = Integer(fields, "CONFIGURED_SPEED_MTS"),
                SpeedMts = Integer(fields, "SPEED_MTS"),
                Manufacturer = Text(fields, "MANUFACTURER"),
                Rank = Integer(fields, "RANK"),
                ConfiguredVoltage = Millivolts(fields, "CONFIGURED_VOLTAGE"),
            });
        }

        int? numDevices = Integer(array, "NUM_DEVICES");
        long? maxCapacity = Number(array, "MAX_CAPACITY");
        if (maxCapacity is <= 0)
        {
            maxCapacity = null;
        }

        return new MemoryLayout
        {
            SlotsUsed = modules.Count,
            SlotsTotal = numDevices is int total && total > 0 ? total : devices.Count,
            MaxCapacityBytes = maxCapacity,
            Modules = modules,
        };
    }

    /// <summary>"Unknown" and "Not Specified" are absent values, not text to show.</summary>
    private static string Text(Dictionary<string, string> fields, string key)
    {
        string value = fields.GetValueOrDefault(key) ?? string.Empty;
        return string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Not Specified", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : value;
    }

    private static long? Number(Dictionary<string, string> fields, string key)
    {
        string value = Text(fields, key);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)
            ? number
            : null;
    }

    private static int? Integer(Dictionary<string, string> fields, string key)
    {
        string value = Text(fields, key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number
            : null;
    }

    /// <summary>SMBIOS writes the configured voltage in millivolts.</summary>
    private static double? Millivolts(Dictionary<string, string> fields, string key)
    {
        if (Number(fields, key) is not long millivolts
            || millivolts < MinMillivolts
            || millivolts > MaxMillivolts)
        {
            // Out of the plausible range, so it is a placeholder, not a reading.
            return null;
        }
        return millivolts / 1000.0;
    }
}
