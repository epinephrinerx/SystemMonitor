using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SysMonitor.Linux.Sensors
{
    public readonly record struct MountEntry(string MountPoint, string Device, string FileSystem);

    public static class MountInfo
    {
        public static IReadOnlyList<MountEntry> Parse(string mountinfoText)
        {
            if (string.IsNullOrEmpty(mountinfoText))
                return Array.Empty<MountEntry>();

            var parsed = new List<MountEntry>();
            var shortest = new Dictionary<string, MountEntry>();
            var lines = mountinfoText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var maybe = ParseLine(line);
                if (maybe is not MountEntry entry) continue;

                parsed.Add(entry);
                if (!shortest.ContainsKey(entry.Device) || entry.MountPoint.Length < shortest[entry.Device].MountPoint.Length)
                {
                    shortest[entry.Device] = entry;
                }
            }

            // Now build the ordered list based on first appearance
            var orderedEntries = new List<MountEntry>();
            var seenDevices = new HashSet<string>();

            foreach (var entry in parsed)
            {
                if (!seenDevices.Contains(entry.Device))
                {
                    seenDevices.Add(entry.Device);
                    orderedEntries.Add(shortest[entry.Device]);
                }
            }

            return orderedEntries;
        }

        private static MountEntry? ParseLine(string line)
        {
            var fields = line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var sep = Array.IndexOf(fields, "-");

            if (sep < 6 || sep + 2 >= fields.Length)
                return null;

            if (!long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                !fields[2].Contains(':') || fields[2].Split(':').Length != 2 ||
                !long.TryParse(fields[2].Split(':')[0], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                !long.TryParse(fields[2].Split(':')[1], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return null;
            }

            var mountPoint = DecodeOctalEscapes(fields[4]);
            var fstype = fields[sep + 1];
            var source = fields[sep + 2];

            if (!source.StartsWith("/dev/", StringComparison.Ordinal) || source.StartsWith("/dev/loop", StringComparison.Ordinal) || fstype == "squashfs")
                return null;

            return new MountEntry(mountPoint, source, fstype);
        }

        private static string DecodeOctalEscapes(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '\\' && i + 3 < input.Length
                    && input[i + 1] >= '0' && input[i + 1] <= '7'
                    && input[i + 2] >= '0' && input[i + 2] <= '7'
                    && input[i + 3] >= '0' && input[i + 3] <= '7')
                {
                    result.Append((char)Convert.ToInt32(input.Substring(i + 1, 3), 8));
                    i += 3;
                    continue;
                }
                result.Append(input[i]);
            }
            return result.ToString();
        }
    }
}
