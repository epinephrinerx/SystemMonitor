using System.Globalization;
using SysMonitor.Linux.Presentation;
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
            string? stat = SysFs.ReadText(dir, "stat");
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
            int parent = IntField(status, "PPid");
            int? uid = UidField(status);

            result.Add(new ProcessEntry
            {
                Pid = pid,
                Name = name.Length > 0 ? name : $"pid {pid}",
                StartTime = stat is null ? null : ParseStartTime(stat),
                WorkingSet = rss,
                PrivateBytes = anonymous,
                Readable = name.Length > 0,
                Uid = uid,
                ParentPid = parent,
                KernelThread = IsKernelThread(pid, parent, HasCommandLine(dir)),
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

    /// <summary>
    /// True when the process still has a command line. Kernel threads and
    /// exited processes have none, and only the command line tells them apart.
    /// </summary>
    public static bool HasCommandLine(string processDirectory)
    {
        string? cmdline = SysFs.ReadText(Path.Combine(processDirectory, "cmdline"));
        return cmdline is not null && cmdline.Trim('\0', ' ', '\t', '\r', '\n').Length > 0;
    }

    /// <summary>PID 2 (kthreadd) and its children are kernel threads.</summary>
    public static bool IsKernelThread(int pid, int parentPid, bool hasCommandLine) =>
        !hasCommandLine && (pid == 2 || parentPid == 2);

    /// <summary>
    /// The name the model would show for one PID, or null when the process is
    /// already gone. The kill flow re-reads this immediately before signalling
    /// so a recycled PID is never ended in place of the one that was picked.
    /// </summary>
    public static string? NameOf(string root, int pid)
    {
        string dir = SysFs.Path(root, "proc", pid.ToString(CultureInfo.InvariantCulture));
        string? status = SysFs.ReadText(Path.Combine(dir, "status"));
        if (status is null)
        {
            return null;
        }
        string name = StatusName(Field(status, "Name"), ExeName(dir));
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// Everything the kill rules check at action time, read fresh from
    /// <c>/proc/&lt;pid&gt;</c>: name, start time, owner, parent and the
    /// kernel-thread flag. Null when the process no longer exists. The fields
    /// are read the same way <see cref="Read"/> reads them, so the re-check
    /// compares like with like.
    /// </summary>
    public static ProcessFacts? FactsOf(string root, int pid)
    {
        string dir = SysFs.Path(root, "proc", pid.ToString(CultureInfo.InvariantCulture));
        string? status = SysFs.ReadText(Path.Combine(dir, "status"));
        if (status is null)
        {
            return null;
        }

        string name = StatusName(Field(status, "Name"), ExeName(dir));
        int parent = IntField(status, "PPid");
        return new ProcessFacts
        {
            Pid = pid,
            Name = name.Length > 0 ? name : $"pid {pid}",
            StartTime = StartTimeOf(dir),
            Uid = UidField(status),
            ParentPid = parent,
            KernelThread = IsKernelThread(pid, parent, HasCommandLine(dir)),
            Readable = name.Length > 0,
        };
    }

    /// <summary>
    /// Field 22 of <c>/proc/&lt;pid&gt;/stat</c>: clock ticks since boot, the
    /// half of the identity a PID alone cannot carry. Parsed after the last
    /// <c>)</c> because the command name (field 2) may contain spaces and
    /// parentheses. Null when the file does not hold a stat line, so an unknown
    /// start time can never equal a real one (Amendment 3).
    /// </summary>
    public static ulong? ParseStartTime(string stat)
    {
        int close = stat.LastIndexOf(')');
        if (close < 0 || close + 1 >= stat.Length)
        {
            return null;
        }

        // fields[0] is field 3 (state), so field 22 is fields[19].
        string[] fields = SysFs.Fields(stat[(close + 1)..]);
        return fields.Length > 19
            && SysFs.TryParseLong(fields[19], out long ticks)
            && ticks > 0
                ? (ulong)ticks
                : null;
    }

    private static ulong? StartTimeOf(string processDirectory)
    {
        string? stat = SysFs.ReadText(Path.Combine(processDirectory, "stat"));
        return stat is null ? null : ParseStartTime(stat);
    }

    /// <summary>The real UID from a status file, or null when it is not there.</summary>
    public static int? UidField(string status)
    {
        string value = Field(status, "Uid");
        if (value.Length == 0)
        {
            return null;
        }
        string[] fields = SysFs.Fields(value);
        return fields.Length > 0 && SysFs.TryParseInt(fields[0], out int uid) && uid >= 0
            ? uid
            : null;
    }

    private static int IntField(string status, string key)
    {
        string value = Field(status, key);
        return value.Length > 0 && SysFs.TryParseInt(value, out int number) ? number : 0;
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
