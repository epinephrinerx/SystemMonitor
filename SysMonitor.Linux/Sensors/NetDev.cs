using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SysMonitor.Linux.Sensors
{
    public readonly record struct NetCounters(string Name, long Received, long Sent);

    public static class NetDev
    {
        public static IReadOnlyList<NetCounters> Parse(string text)
        {
            if (text == null) return new List<NetCounters>();

            var lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var netCounters = new List<NetCounters>();

            foreach (var line in lines)
            {
                int colon = line.IndexOf(':');
                if (colon < 0) continue;

                string name = line.Substring(0, colon).Trim();
                if (name == "lo" || string.IsNullOrEmpty(name)) continue;

                string[] numbers = line.Substring(colon + 1).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (numbers.Length < 9) continue;

                if (long.TryParse(numbers[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long received) &&
                    long.TryParse(numbers[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sent) &&
                    received >= 0 && sent >= 0)
                {
                    netCounters.Add(new NetCounters(name, received, sent));
                }
            }

            return netCounters;
        }

        public static (double DownMb, double UpMb) Rate(NetCounters previous, NetCounters current, double seconds)
        {
            if (seconds <= 0) return (0, 0);

            long receivedDelta = Math.Max(current.Received - previous.Received, 0);
            long sentDelta = Math.Max(current.Sent - previous.Sent, 0);

            double downMb = receivedDelta / (1024.0 * 1024.0) / seconds;
            double upMb = sentDelta / (1024.0 * 1024.0) / seconds;

            return (downMb, upMb);
        }

        public static bool IsWireless(string sysClassNetDir, string name)
        {
            if (string.IsNullOrEmpty(sysClassNetDir) || string.IsNullOrEmpty(name)) return false;

            string wirelessPath = Path.Combine(sysClassNetDir, name, "wireless");
            return Directory.Exists(wirelessPath);
        }
    }
}
