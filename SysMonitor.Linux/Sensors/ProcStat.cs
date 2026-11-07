using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SysMonitor.Linux.Sensors
{
    public readonly record struct CpuTimes(long Busy, long Total);

    public static class ProcStat
    {
        private static bool IsCoreLabel(string t)
        {
            return t.Length > 3 && t.StartsWith("cpu", StringComparison.Ordinal) && t.Substring(3).All(c => c >= '0' && c <= '9');
        }

        private static CpuTimes? FromFields(string[] parts)
        {
            long user = long.TryParse(parts.Length > 1 ? parts[1] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out long userValue) ? userValue : 0;
            long nice = long.TryParse(parts.Length > 2 ? parts[2] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out long niceValue) ? niceValue : 0;
            long system = long.TryParse(parts.Length > 3 ? parts[3] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out long systemValue) ? systemValue : 0;
            long idle = long.TryParse(parts.Length > 4 ? parts[4] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out long idleValue) ? idleValue : 0;
            long iowait = long.TryParse(parts.Length > 5 ? parts[5] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out long iowaitValue) ? iowaitValue : 0;
            long irq = long.TryParse(parts.Length > 6 ? parts[6] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out long irqValue) ? irqValue : 0;
            long softirq = long.TryParse(parts.Length > 7 ? parts[7] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out long softirqValue) ? softirqValue : 0;
            long steal = long.TryParse(parts.Length > 8 ? parts[8] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out long stealValue) ? stealValue : 0;

            long busy = user + nice + system + irq + softirq + steal;
            long total = busy + idle + iowait;

            return new CpuTimes(busy, total);
        }

        public static IReadOnlyList<CpuTimes> ParseCores(string text)
        {
            if (text == null) return new List<CpuTimes>();

            var lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var cpuTimes = new List<CpuTimes>();

            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                if (IsCoreLabel(parts[0]))
                {
                    var cpuTime = FromFields(parts);
                    if (cpuTime.HasValue)
                    {
                        cpuTimes.Add(cpuTime.Value);
                    }
                }
            }

            return cpuTimes;
        }

        public static CpuTimes ParseTotal(string text)
        {
            if (text == null) return default;

            var lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && parts[0] == "cpu")
                {
                    var cpuTime = FromFields(parts);
                    if (cpuTime.HasValue)
                    {
                        return cpuTime.Value;
                    }
                }
            }

            return default;
        }

        public static int Usage(CpuTimes previous, CpuTimes current)
        {
            long busyDelta = current.Busy - previous.Busy;
            long totalDelta = current.Total - previous.Total;

            if (totalDelta <= 0) return 0;

            int usage = (int)Math.Round((double)busyDelta * 100 / totalDelta, MidpointRounding.AwayFromZero);
            return Math.Clamp(usage, 0, 100);
        }
    }
}
