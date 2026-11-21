using SysMonitor.Sensors;

namespace SysMonitor.Linux.Sensors;

/// <summary>Lifetime byte counters for one block device line.</summary>
public readonly record struct DiskCounters(
    string Name,
    long BytesRead,
    long BytesWritten,
    long? IoMs = null);

/// <summary>
/// /proc/diskstats.  One line per disk and per partition, so a partition can
/// be charged for its own I/O instead of its parent disk's.
/// </summary>
public static class DiskStats
{
    public static IReadOnlyList<DiskCounters> Parse(string text)
    {
        var result = new List<DiskCounters>();

        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = SysFs.Fields(line);
            if (fields.Length < 10)
            {
                continue;
            }

            // major minor name reads merges sectors-read ms writes merges sectors-written ...
            if (!SysFs.TryParseLong(fields[0], out long major)
                || !SysFs.TryParseLong(fields[1], out long minor)
                || !SysFs.TryParseLong(fields[5], out long sectorsRead)
                || !SysFs.TryParseLong(fields[9], out long sectorsWritten)
                || major < 0 || minor < 0 || sectorsRead < 0 || sectorsWritten < 0)
            {
                continue;
            }

            // Field 13 of /proc/diskstats (index 12) is io_ticks: the
            // milliseconds the device spent with I/O in flight.  Older kernels
            // stop at field 11, and a reset can leave a value that does not
            // parse, so a missing or invalid field is null -- an unknown, never
            // a zero that would read as "idle".
            long? ioMs = fields.Length > 12
                         && SysFs.TryParseLong(fields[12], out long ticks)
                         && ticks >= 0
                ? ticks
                : null;

            result.Add(new DiskCounters(
                fields[2],
                sectorsRead > long.MaxValue / 512 ? long.MaxValue : sectorsRead * 512,
                sectorsWritten > long.MaxValue / 512 ? long.MaxValue : sectorsWritten * 512,
                ioMs));
        }

        return result;
    }

    /// <summary>Megabytes per second between two readings of the same device.</summary>
    public static (double ReadMb, double WriteMb) Rate(
        DiskCounters previous, DiskCounters current, long previousStamp, long stamp) =>
        IoRate.PerSecond(
            (previous.BytesRead, previous.BytesWritten), previousStamp,
            (current.BytesRead, current.BytesWritten), stamp);
}
