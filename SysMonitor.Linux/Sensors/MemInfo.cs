using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// /proc/meminfo to the shared <see cref="Ram"/> model.  Used memory is
/// MemTotal - MemAvailable, which is the number the kernel considers free for
/// new work; MemFree alone counts reclaimable cache as used.
/// </summary>
public static class MemInfo
{
    public static Ram Parse(string text)
    {
        long totalKb = 0;
        long availableKb = 0;
        bool hasAvailable = false;
        long freeKb = 0;
        long buffersKb = 0;
        long cachedKb = 0;

        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            string key = line[..colon];
            long kb = KiloBytes(line[(colon + 1)..]);

            switch (key)
            {
                case "MemTotal":
                    totalKb = kb;
                    break;
                case "MemAvailable":
                    availableKb = kb;
                    hasAvailable = true;
                    break;
                case "MemFree":
                    freeKb = kb;
                    break;
                case "Buffers":
                    buffersKb = kb;
                    break;
                case "Cached":
                    cachedKb = kb;
                    break;
            }
        }

        if (totalKb <= 0)
        {
            return new Ram();
        }

        long available = hasAvailable ? availableKb : freeKb + buffersKb + cachedKb;
        long usedKb = Math.Clamp(totalKb - available, 0, totalKb);
        int usage = (int)Math.Clamp(
            Math.Round(usedKb * 100.0 / totalKb, MidpointRounding.AwayFromZero), 0, 100);

        return new Ram
        {
            Usage = usage,
            UsedGb = usedKb / 1024.0 / 1024.0,
            TotalGb = totalKb / 1024.0 / 1024.0,
        };
    }

    private static long KiloBytes(string value)
    {
        string[] fields = SysFs.Fields(value);
        return fields.Length > 0 && SysFs.TryParseLong(fields[0], out long kb) ? kb : 0;
    }
}
