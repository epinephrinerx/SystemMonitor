namespace SysMonitor.Linux.Sensors;

/// <summary>Busy and total jiffies for one CPU line of /proc/stat.</summary>
public readonly record struct CpuTimes(long Busy, long Total);

/// <summary>One reading of /proc/stat: the aggregate plus one entry per core.</summary>
public sealed class ProcStatReading
{
    public CpuTimes Total { get; init; }
    public IReadOnlyList<CpuTimes> Cores { get; init; } = Array.Empty<CpuTimes>();
}

/// <summary>
/// Reads the CPU time counters.  Pure: hand it the text of /proc/stat.
/// </summary>
public static class ProcStat
{
    public static ProcStatReading Parse(string text)
    {
        CpuTimes total = default;
        var cores = new List<(int Index, CpuTimes Times)>();

        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = SysFs.Fields(line);
            if (fields.Length == 0)
            {
                continue;
            }

            if (fields[0] == "cpu")
            {
                total = FromFields(fields);
                continue;
            }

            if (fields[0].Length > 3
                && fields[0].StartsWith("cpu", StringComparison.Ordinal)
                && int.TryParse(fields[0][3..], System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out int index))
            {
                cores.Add((index, FromFields(fields)));
            }
        }

        // /proc/stat lists cpu0..cpuN in order, but sorting makes the mapping
        // to a core index explicit and survives a reordered fake file.
        cores.Sort((a, b) => a.Index.CompareTo(b.Index));

        return new ProcStatReading
        {
            Total = total,
            Cores = cores.Select(c => c.Times).ToList(),
        };
    }

    /// <summary>
    /// Busy jiffies over elapsed jiffies, clamped.  A counter that went
    /// backwards (a hot-plugged CPU) is one idle interval, not a negative rate.
    /// </summary>
    public static int Usage(CpuTimes previous, CpuTimes current)
    {
        long busy = current.Busy - previous.Busy;
        long total = current.Total - previous.Total;
        if (total <= 0)
        {
            return 0;
        }

        double usage = (double)busy * 100 / total;
        return (int)Math.Clamp(Math.Round(usage, MidpointRounding.AwayFromZero), 0, 100);
    }

    private static CpuTimes FromFields(string[] fields)
    {
        long user = Field(fields, 1);
        long nice = Field(fields, 2);
        long system = Field(fields, 3);
        long idle = Field(fields, 4);
        long iowait = Field(fields, 5);
        long irq = Field(fields, 6);
        long softirq = Field(fields, 7);
        long steal = Field(fields, 8);

        // guest/guest_nice are already counted in user and nice.
        long busy = user + nice + system + irq + softirq + steal;
        return new CpuTimes(busy, busy + idle + iowait);
    }

    private static long Field(string[] fields, int index) =>
        index < fields.Length && SysFs.TryParseLong(fields[index], out long value) ? value : 0;
}
