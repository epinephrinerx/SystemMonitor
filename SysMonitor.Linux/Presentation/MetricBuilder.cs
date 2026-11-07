using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation
{
    public static class MetricBuilder
    {
        internal static string Clean(string? t)
        {
            if (t == null) return string.Empty;
            if (t.Length == 0 || char.IsWhiteSpace(t[0]) || char.IsWhiteSpace(t[t.Length - 1]))
            {
                return CleanSlowPath(t);
            }
            foreach (char c in t)
            {
                if (char.IsControl(c))
                {
                    return CleanSlowPath(t);
                }
            }
            return t;
        }

        private static string CleanSlowPath(string t)
        {
            var sb = new StringBuilder();
            foreach (char c in t)
            {
                if (char.IsControl(c))
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Trim();
        }

        internal static IReadOnlyList<(string Letter, Disk Disk)> OrderedDisks(Snapshot? s)
        {
            var tmp = new List<(string Letter, Disk D, int Index)>();
            if (s == null || s.Disks == null) return new List<(string Letter, Disk Disk)>();
            for (int i = 0; i < s.Disks.Count; i++)
            {
                var d = s.Disks[i];
                if (d != null && d.TotalGb > 0)
                {
                    tmp.Add((Clean(d.Letter), d, i));
                }
            }
            tmp.Sort((a, b) =>
            {
                int ar = a.Letter == "/" ? 0 : 1;
                int br = b.Letter == "/" ? 0 : 1;
                if (ar != br) return ar.CompareTo(br);
                int c = string.CompareOrdinal(a.Letter, b.Letter);
                return c != 0 ? c : a.Index.CompareTo(b.Index);
            });
            var res = new List<(string Letter, Disk Disk)>();
            foreach (var e in tmp)
            {
                res.Add((e.Letter, e.D));
            }
            return res;
        }

        internal static IReadOnlyList<(string Key, Adapter Adapter)> IncludedAdapters(Snapshot? s)
        {
            var res = new List<(string Key, Adapter Adapter)>();
            if (s == null || s.Adapters == null) return res;
            foreach (var a in s.Adapters)
            {
                if (a == null) continue;
                string id = Clean(a.Id);
                bool excluded = StartsWithAny(id, "docker", "veth", "br-", "virbr", "lo") ||
                               (a.SpeedMbps <= 0 && a.TotalReceived == 0 && a.TotalSent == 0);
                if (!excluded)
                {
                    res.Add(("net:" + id, a));
                }
            }
            return res;
        }

        public static IReadOnlyList<MetricItem> Build(Snapshot? s)
        {
            var list = new List<MetricItem>();
            if (s == null) return list;

            int Cl(int v) => Math.Clamp(v, 0, 100);

            const string cpuAccent = "#3b82f6";
            const string ramAccent = "#a855f7";
            const string diskAccent = "#10b981";
            const string netAccent = "#06b6d4";

            // CPU
            int cores = s.Cores == null ? 0 : s.Cores.Count;
            string sub = cores == 1 ? "1 core" : cores.ToString(CultureInfo.InvariantCulture) + " cores";
            int cpuUsage = Cl(s.CpuTotal);
            string cpuValue = Fmt.Percent(cpuUsage) + (s.CpuTemp.HasValue ? " " + Fmt.Temp(s.CpuTemp) : "");
            list.Add(new MetricItem(MetricKind.Cpu, "cpu", "CPU", sub, cpuUsage, cpuValue, LoadColor.Hex(cpuUsage, cpuAccent), cpuAccent));

            // RAM
            var ram = s.Ram;
            double used = ram == null ? 0 : ram.UsedGb;
            double total = ram == null ? 0 : ram.TotalGb;
            int ramUsage = ram == null ? 0 : Cl(ram.Usage);
            list.Add(new MetricItem(MetricKind.Ram, "ram", "Memory", Fmt.Size(used) + " / " + Fmt.Size(total), ramUsage, Fmt.Percent(ramUsage), LoadColor.Hex(ramUsage, ramAccent), ramAccent));

            // Disks
            foreach (var (letter, d) in OrderedDisks(s))
            {
                string label = Clean(d.Label);
                string title = label.Length == 0 ? letter : label;
                int diskUsage = Cl(d.Usage);
                list.Add(new MetricItem(MetricKind.Disk, "disk:" + letter, title, letter + " - " + Fmt.Size(d.UsedGb) + " / " + Fmt.Size(d.TotalGb), diskUsage, Fmt.Percent(diskUsage), LoadColor.Hex(diskUsage, diskAccent), diskAccent));
            }

            // Adapters
            foreach (var (key, a) in IncludedAdapters(s))
            {
                string id = Clean(a.Id);
                double down = Math.Max(0.0, a.DownMb);
                double up = Math.Max(0.0, a.UpMb);
                int netUsage = 0;
                if (a.SpeedMbps > 0)
                {
                    double x = (down + up) * 8.388608 / a.SpeedMbps * 100.0;
                    if (double.IsNaN(x)) netUsage = 0;
                    else if (x >= 100) netUsage = 100;
                    else netUsage = Cl((int)Math.Round(x, MidpointRounding.AwayFromZero));
                }
                list.Add(new MetricItem(MetricKind.Network, key, id, (a.Wireless ? "Wi-Fi" : "LAN") + " " + Fmt.Speed(a.SpeedMbps), netUsage, "Down " + Fmt.Rate(a.DownMb) + "  Up " + Fmt.Rate(a.UpMb), LoadColor.Hex(netUsage, netAccent), netAccent));
            }

            return list;
        }

        private static bool StartsWithAny(string value, params string[] prefixes)
        {
            foreach (var prefix in prefixes)
            {
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
