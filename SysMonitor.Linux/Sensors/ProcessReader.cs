using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// Running processes from /proc/&lt;pid&gt;.  Nothing here needs root: the
/// kernel hides another user's memory counters as zeros, which is reported as
/// unreadable rather than as a real zero.
/// </summary>
public static class ProcessReader
{
    public static IReadOnlyList<ProcessEntry> Read(string root = "/")
    {
        var result = new List<ProcessEntry>();

        foreach (string dir in SysFs.Directories(root, "proc"))
        {
            string pidText = Path.GetFileName(dir);
            if (!int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
            {
                continue;
            }

            string? status = SysFs.ReadText(dir, "status");
            string exeName = ExeName(dir);

            if (status is null)
            {
                // The directory exists but the kernel will not describe it.
                // Keep the process visible without inventing numbers.
                result.Add(new ProcessEntry
                {
                    Pid = pid,
                    Name = exeName.Length > 0 ? exeName : $"pid {pid}",
                    Readable = false,
                });
                continue;
            }

            string statusName = Field(status, "Name");
            string name = StatusName(statusName, exeName);
            long rss = KiloBytes(status, "VmRSS");
            long anonymous = KiloBytes(status, "RssAnon");

            result.Add(new ProcessEntry
            {
                Pid = pid,
                Name = name.Length > 0 ? name : $"pid {pid}",
                WorkingSet = rss,
                PrivateBytes = anonymous,
                Readable = name.Length > 0,
            });
        }

        result.Sort((a, b) => a.Pid.CompareTo(b.Pid));
        return result;
    }

    /// <summary>
    /// The name to show: the kernel's <c>Name</c>, replaced by the executable's
    /// basename only when the kernel cut it at 15 characters (the basename then
    /// starts with the truncated name).  A name changed for another reason, such
    /// as a process title set by the program, is left as the kernel reports it.
    /// </summary>
    public static string StatusName(string statusName, string exeName) =>
        statusName.Length == 15
        && exeName.Length > 0
        && exeName.StartsWith(statusName, StringComparison.Ordinal)
            ? exeName
            : statusName;

    /// <summary>
    /// The executable's basename, or empty when the link cannot be read.
    /// </summary>
    public static string ExeName(string processDirectory)
    {
        string name = SysFs.LinkTargetName(Path.Combine(processDirectory, "exe"));
        return name.Length > 0 ? name : string.Empty;
    }

    private static string Field(string status, string key)
    {
        foreach (string line in status.Split('\n'))
        {
            if (!line.StartsWith(key + ":", StringComparison.Ordinal))
            {
                continue;
            }
            return line[(key.Length + 1)..].Trim();
        }
        return string.Empty;
    }

    /// <summary>VmRSS and RssAnon are reported in kB; the model uses bytes.</summary>
    private static long KiloBytes(string status, string key)
    {
        string value = Field(status, key);
        if (value.Length == 0)
        {
            return 0;
        }

        string[] fields = SysFs.Fields(value);
        return fields.Length > 0 && SysFs.TryParseLong(fields[0], out long kb) && kb > 0
            ? kb * 1024
            : 0;
    }
}
