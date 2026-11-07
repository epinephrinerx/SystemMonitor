using System;
using System.Collections.Generic;
using System.Linq;

namespace SysMonitor.Linux.Presentation
{
    public sealed record ChartSpec(string Title, string Key, string ColorHex, double? FixedMax);

    public static class TabCharts
    {
        public static IReadOnlyList<ChartSpec> For(string? tabKey, IEnumerable<string>? historyKeys)
        {
            var res = new List<ChartSpec>();
            if (tabKey == null) return res;

            var keys = new List<string>();
            if (historyKeys != null)
            {
                foreach (var k in historyKeys)
                {
                    if (k != null) keys.Add(k);
                }
            }

            if (string.Equals(tabKey, "cpu", StringComparison.Ordinal))
            {
                res.Add(new ChartSpec("CPU", "cpu", "#3b82f6", 100.0));
                foreach (var k in keys)
                {
                    if (k.StartsWith("core:", StringComparison.Ordinal))
                    {
                        res.Add(new ChartSpec("Core " + k.Substring(5), k, "#3b82f6", 100.0));
                    }
                }
            }
            else if (string.Equals(tabKey, "memory", StringComparison.Ordinal))
            {
                res.Add(new ChartSpec("Memory", "ram", "#a855f7", 100.0));
            }
            else if (string.Equals(tabKey, "disk", StringComparison.Ordinal))
            {
                foreach (var k in keys)
                {
                    if (k.StartsWith("disk:", StringComparison.Ordinal))
                    {
                        string suffix = k.Substring(5);
                        if (suffix.IndexOf(':') < 0)
                        {
                            res.Add(new ChartSpec(suffix, k, "#10b981", 100.0));
                        }
                    }
                }
            }
            else if (tabKey.StartsWith("disk:", StringComparison.Ordinal))
            {
                res.Add(new ChartSpec("Usage", tabKey, "#10b981", 100.0));
                res.Add(new ChartSpec("Read", tabKey + ":read", "#3b82f6", null));
                res.Add(new ChartSpec("Write", tabKey + ":write", "#f59e0b", null));
            }

            return res;
        }
    }
}
