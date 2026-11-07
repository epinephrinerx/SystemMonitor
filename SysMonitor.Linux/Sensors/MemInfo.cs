using System;
using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors
{
    public static class MemInfo
    {
        public static Ram Parse(string text)
        {
            if (text == null) return new Ram();

            var lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            long memTotal = 0;
            long memAvailable = 0;
            long memFree = 0;
            long buffers = 0;
            long cached = 0;
            bool hasAvailable = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    memTotal = ParseValue(line);
                }
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                {
                    memAvailable = ParseValue(line);
                    hasAvailable = true;
                }
                else if (line.StartsWith("MemFree:", StringComparison.Ordinal))
                {
                    memFree = ParseValue(line);
                }
                else if (line.StartsWith("Buffers:", StringComparison.Ordinal))
                {
                    buffers = ParseValue(line);
                }
                else if (line.StartsWith("Cached:", StringComparison.Ordinal))
                {
                    cached = ParseValue(line);
                }
            }

            if (memTotal <= 0) return new Ram();

            Int128 avail = hasAvailable ? memAvailable : (Int128)memFree + buffers + cached;
            Int128 usedWide = (Int128)memTotal - avail;
            long used = usedWide < 0 ? 0 : usedWide > memTotal ? memTotal : (long)usedWide;
            double totalGb = memTotal / 1024.0 / 1024.0;
            double usedGb = used / 1024.0 / 1024.0;
            int usage = (int)Math.Round(used * 100.0 / memTotal, MidpointRounding.AwayFromZero);
            usage = Math.Clamp(usage, 0, 100);

            return new Ram { Usage = usage, UsedGb = usedGb, TotalGb = totalGb };
        }

        private static long ParseValue(string line)
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return 0;

            if (long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                return value;
            }

            return 0;
        }
    }
}
