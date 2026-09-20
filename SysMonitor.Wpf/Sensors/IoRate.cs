namespace SysMonitor.Sensors;

/// <summary>
/// Turning lifetime byte counters into a per-second rate.
///
/// Pulled out on its own because the baseline has to belong to the drive's
/// own last successful read.  Dividing by a shared tick meant a drive whose
/// probe had been skipped for five minutes reported five minutes of
/// accumulated bytes as one interval's worth: 300 MB over 300 s read as
/// 300 MB/s instead of 1 MB/s.
/// </summary>
internal static class IoRate
{
    private const double Mb = 1024 * 1024;

    public static (double ReadMb, double WriteMb) PerSecond(
        (long Read, long Written) previous, long previousStamp,
        (long Read, long Written) current, long stamp)
    {
        double elapsed = (stamp - previousStamp) / 1000.0;
        if (elapsed <= 0)
        {
            return (0, 0);
        }
        // Counters reset when a volume is remounted; a negative delta is not
        // a negative rate.
        double read = Math.Max(0, current.Read - previous.Read);
        double written = Math.Max(0, current.Written - previous.Written);
        return (read / elapsed / Mb, written / elapsed / Mb);
    }
}
