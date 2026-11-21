using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// <c>/proc/swaps</c>: one line per swap file, partition or zram device, with
/// sizes in KiB. The kernel escapes spaces in a file name as <c>\040</c>.
/// </summary>
public static class SwapsReader
{
    public static IReadOnlyList<SwapDevice> Parse(string? text)
    {
        if (text is null)
        {
            return Array.Empty<SwapDevice>();
        }

        var devices = new List<SwapDevice>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            string[] fields = SysFs.Fields(line);
            if (fields.Length < 5
                || !long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sizeKb)
                || !long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long usedKb)
                || !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int priority))
            {
                // The header and anything malformed are skipped: a device that
                // cannot be read is not a device with zero bytes.
                continue;
            }

            devices.Add(new SwapDevice
            {
                Name = SysFs.DecodeOctal(fields[0]),
                Type = fields[1],
                SizeBytes = Math.Max(0, sizeKb) * 1024,
                UsedBytes = Math.Max(0, usedKb) * 1024,
                Priority = priority,
            });
        }
        return devices;
    }
}
