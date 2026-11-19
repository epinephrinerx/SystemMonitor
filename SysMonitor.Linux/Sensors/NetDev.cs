namespace SysMonitor.Linux.Sensors;

/// <summary>Lifetime byte counters for one network interface.</summary>
public readonly record struct NetCounters(string Name, long Received, long Sent);

/// <summary>/proc/net/dev: the two counter columns plus the totals.</summary>
public static class NetDev
{
    public static IReadOnlyList<NetCounters> Parse(string text)
    {
        var result = new List<NetCounters>();

        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            string name = line[..colon].Trim();
            if (name.Length == 0 || name == "lo" || !SysFs.IsSafeName(name))
            {
                continue;
            }

            string[] fields = SysFs.Fields(line[(colon + 1)..]);
            if (fields.Length < 9
                || !SysFs.TryParseLong(fields[0], out long received)
                || !SysFs.TryParseLong(fields[8], out long sent)
                || received < 0 || sent < 0)
            {
                continue;
            }

            result.Add(new NetCounters(name, received, sent));
        }

        return result;
    }

    public static (double DownMb, double UpMb) Rate(
        NetCounters previous, NetCounters current, long previousStamp, long stamp)
    {
        double elapsed = (stamp - previousStamp) / 1000.0;
        if (elapsed <= 0)
        {
            return (0, 0);
        }

        double down = Math.Max(0, current.Received - previous.Received);
        double up = Math.Max(0, current.Sent - previous.Sent);
        return (down / elapsed / (1024 * 1024), up / elapsed / (1024 * 1024));
    }
}
