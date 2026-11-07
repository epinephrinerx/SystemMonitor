using System;
using System.Collections.Generic;
using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation
{
    public sealed record TabModel(string Key, string Title, IReadOnlyList<string> Lines);

    public static class FullTabs
    {
        private static string Inv(int n) => n.ToString(CultureInfo.InvariantCulture);
        private static string OrNA(string? t)
        {
            var c = MetricBuilder.Clean(t);
            return c.Length == 0 ? "N/A" : c;
        }

        public static IReadOnlyList<TabModel> Build(Snapshot? s)
        {
            if (s == null)
            {
                return new[]
                {
                    new TabModel("cpu", "CPU", new[] { "No data" }),
                    new TabModel("memory", "Memory", new[] { "No data" }),
                    new TabModel("disk", "Disk", new[] { "No data" })
                };
            }

            var cpu = new List<string>();
            cpu.Add("Total: " + Fmt.Percent(s.CpuTotal));
            string t = "Temperature: " + Fmt.Temp(s.CpuTemp);
            if (s.CpuTempEstimated)
            {
                t += " (estimated)";
            }
            cpu.Add(t);
            if (s.Cores != null)
            {
                for (int i = 0; i < s.Cores.Count; i++)
                {
                    var c = s.Cores[i];
                    if (c == null) continue;
                    string line = "Core " + Inv(i) + ": " + Fmt.Percent(c.Usage);
                    if (c.Temp.HasValue)
                    {
                        line += "  " + Fmt.Temp(c.Temp);
                        if (c.Estimated)
                        {
                            line += " (estimated)";
                        }
                    }
                    cpu.Add(line);
                }
            }

            var mem = new List<string>();
            var ram = s.Ram;
            int u = ram == null ? 0 : ram.Usage;
            double used = ram == null ? 0 : ram.UsedGb;
            double total = ram == null ? 0 : ram.TotalGb;
            mem.Add("Usage: " + Fmt.Percent(u));
            mem.Add("Used: " + Fmt.Size(used));
            mem.Add("Total: " + Fmt.Size(total));
            int moduleLines = 0;
            if (s.Modules != null)
            {
                foreach (var m in s.Modules)
                {
                    if (m != null)
                    {
                        mem.Add("Slot " + MetricBuilder.Clean(m.Slot) + ": " + Fmt.Size(m.Gb) + " " + MetricBuilder.Clean(m.Kind) + " " + Inv(m.Mhz) + " MHz");
                        moduleLines++;
                    }
                }
            }
            if (moduleLines == 0)
            {
                mem.Add("Modules: N/A (needs root)");
            }

            var disks = MetricBuilder.OrderedDisks(s);

            var dl = new List<string>();
            foreach (var (letter, d) in disks)
            {
                dl.Add(letter + ": " + Fmt.Percent(d.Usage) + " of " + Fmt.Size(d.TotalGb));
            }

            var result = new List<TabModel>
            {
                new TabModel("cpu", "CPU", cpu),
                new TabModel("memory", "Memory", mem),
                new TabModel("disk", "Disk", dl)
            };

            foreach (var (letter, d) in disks)
            {
                var det = d.Detail;
                string tabLabel = MetricBuilder.Clean(d.Label);
                string tabTitle = tabLabel.Length == 0 ? letter : tabLabel;
                string media = OrNA(d.Media);
                string bus = MetricBuilder.Clean(d.Bus);
                string mediaLine = "Media: " + media;
                if (bus.Length > 0 && !string.Equals(bus, "Unknown", StringComparison.Ordinal)) { mediaLine += ", " + bus; }
                result.Add(new TabModel("disk:" + letter, tabTitle, new[]
                {
                    "Mount: " + letter,
                    "Filesystem: " + OrNA(det?.FileSystem),
                    "Used: " + Fmt.Size(d.UsedGb) + " of " + Fmt.Size(d.TotalGb),
                    "Read: " + Fmt.Rate(d.ReadMb),
                    "Write: " + Fmt.Rate(d.WriteMb),
                    "Physical disk: " + (det != null && det.DiskNumber.HasValue ? "Disk " + Inv(det.DiskNumber.Value) : "N/A"),
                    "Partition: " + (det != null && det.PartitionNumber > 0 ? Inv(det.PartitionNumber) : "N/A"),
                    "Model: " + OrNA(det?.Disk?.Model),
                    mediaLine
                }));
            }

            return result;
        }
    }
}
