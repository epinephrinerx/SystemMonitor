using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SysMonitor.Linux.Sensors
{
    public readonly record struct DiskCounters(string Name, long BytesRead, long BytesWritten);

    public static class DiskStats
    {
        public static IReadOnlyList<DiskCounters> Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<DiskCounters>();

            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<DiskCounters>();

            foreach (var line in lines)
            {
                var fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 10)
                    continue;

                if (!TryParseNonNegative(fields[0], out long major) || !TryParseNonNegative(fields[1], out long minor))
                    continue;

                var name = fields[2];
                if (!TryParseNonNegative(fields[3], out long readsCompleted) ||
                    !TryParseNonNegative(fields[4], out long readsMerged) ||
                    !TryParseNonNegative(fields[5], out long sectorsRead) ||
                    !TryParseNonNegative(fields[6], out long msReading) ||
                    !TryParseNonNegative(fields[7], out long writesCompleted) ||
                    !TryParseNonNegative(fields[8], out long writesMerged) ||
                    !TryParseNonNegative(fields[9], out long sectorsWritten))
                    continue;

                long bytesRead = sectorsRead > long.MaxValue / 512 ? long.MaxValue : sectorsRead * 512;
                long bytesWritten = sectorsWritten > long.MaxValue / 512 ? long.MaxValue : sectorsWritten * 512;

                result.Add(new DiskCounters(name, bytesRead, bytesWritten));
            }

            return result;
        }

        public static (double ReadMb, double WriteMb) Rate(DiskCounters previous, DiskCounters current, double seconds)
        {
            if (double.IsNaN(seconds) || seconds <= 0)
                return (0, 0);

            double dr = Math.Max(0.0, (double)current.BytesRead - (double)previous.BytesRead);
            double dw = Math.Max(0.0, (double)current.BytesWritten - (double)previous.BytesWritten);

            double r = dr / seconds / 1048576.0;
            double w = dw / seconds / 1048576.0;

            if (double.IsNaN(r) || double.IsInfinity(r))
                r = 0;

            if (double.IsNaN(w) || double.IsInfinity(w))
                w = 0;

            return (r, w);
        }

        private static bool TryParseNonNegative(string s, out long v)
        {
            return long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v) && v >= 0 && !s.StartsWith("-", StringComparison.Ordinal);
        }
    }
}
