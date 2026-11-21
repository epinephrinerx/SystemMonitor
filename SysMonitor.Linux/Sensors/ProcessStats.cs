using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// Per-process CPU, memory, user, command line and cgroup, with the GPU figures
/// from <see cref="ProcessGpu"/> merged in. CPU is a delta between two calls,
/// so the instance keeps a cache keyed by (PID, start time): a recycled PID
/// never inherits the earlier process's counters.
///
/// The first sight of a process reports a null CPU percentage rather than zero,
/// because there is no earlier reading to subtract.
/// </summary>
public sealed class ProcessStats
{
    private readonly string _root;
    private readonly int _logicalCpus;
    private readonly long _clockTicks;
    private readonly ProcessGpu _gpu;

    private readonly Dictionary<(int Pid, ulong? StartTime), CpuMark> _cpu = new();

    private Dictionary<int, string>? _users;

    public ProcessStats(
        string root = "/",
        int logicalCpus = 0,
        long clockTicks = 100,
        ProcessGpu? gpu = null)
    {
        _root = string.IsNullOrEmpty(root) ? "/" : root;
        _logicalCpus = logicalCpus > 0 ? logicalCpus : Environment.ProcessorCount;
        _clockTicks = clockTicks > 0 ? clockTicks : 100;
        _gpu = gpu ?? new ProcessGpu(_root);
    }

    /// <summary>
    /// Every process as the Apps and Processes pages show it, sorted by PID.
    /// <paramref name="serviceByPid"/> maps a user unit's MainPID to the unit
    /// name; it is filled from the existing services read, not from a new
    /// systemctl call per row.
    /// </summary>
    public IReadOnlyList<ProcessRow> Read(
        long stampMs, IReadOnlyDictionary<int, string>? serviceByPid = null)
    {
        var rows = new List<ProcessRow>();
        var identities = new List<ProcessIdentity>();
        var seen = new HashSet<(int Pid, ulong? StartTime)>();

        foreach (string dir in SysFs.Directories(_root, "proc"))
        {
            string pidText = Path.GetFileName(dir);
            if (!int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
            {
                continue;
            }

            string? status = SysFs.ReadText(Path.Combine(dir, "status"));
            string? stat = SysFs.ReadText(Path.Combine(dir, "stat"));
            string exeName = ProcessReader.ExeName(dir);
            ulong? start = stat is null ? null : ProcessReader.ParseStartTime(stat);
            long? ticks = stat is null ? null : ProcessReader.ParseCpuTicks(stat);

            string name;
            bool readable;
            long memory = 0;
            int? uid;
            int parent;
            bool kernelThread;

            if (status is null)
            {
                // The directory exists but the kernel will not describe it.
                // Keep the process visible without inventing numbers.
                name = exeName.Length > 0 ? exeName : $"pid {pid}";
                readable = false;
                uid = null;
                parent = 0;
                kernelThread = false;
            }
            else
            {
                name = ProcessReader.StatusName(StatusField(status, "Name"), exeName);
                readable = name.Length > 0;
                memory = ProcessReader.ResidentBytes(status);
                uid = ProcessReader.UidField(status);
                parent = ProcessReader.ParentPid(status);
                kernelThread = ProcessReader.IsKernelThread(
                    pid, parent, ProcessReader.HasCommandLine(dir));
            }

            if (name.Length == 0)
            {
                name = $"pid {pid}";
            }

            double? cpu = Percent(pid, start, ticks, stampMs);
            if (start is not null && ticks is not null)
            {
                seen.Add((pid, start));
            }

            string? service = null;
            serviceByPid?.TryGetValue(pid, out service);

            rows.Add(new ProcessRow
            {
                Pid = pid,
                Name = name,
                StartTime = start,
                Uid = uid,
                User = UserName(uid),
                ParentPid = parent,
                KernelThread = kernelThread,
                Readable = readable,
                CpuPercent = cpu,
                MemoryBytes = memory,
                Command = ProcessReader.CommandLine(dir),
                CgroupPath = ProcessReader.CgroupPath(dir),
                ServiceUnit = string.IsNullOrEmpty(service) ? null : service,
            });

            identities.Add(new ProcessIdentity(pid, start));
        }

        // Drop the marks of processes that are gone, so the cache cannot grow
        // without bound on a long-running machine.
        foreach ((int Pid, ulong? StartTime) key in _cpu.Keys.ToList())
        {
            if (!seen.Contains(key))
            {
                _cpu.Remove(key);
            }
        }

        IReadOnlyDictionary<int, GpuUsage> gpu = _gpu.Read(stampMs, identities);
        for (int i = 0; i < rows.Count; i++)
        {
            if (gpu.TryGetValue(rows[i].Pid, out GpuUsage usage))
            {
                rows[i] = rows[i] with
                {
                    GpuPercent = usage.Percent,
                    GpuMemoryBytes = usage.MemoryBytes,
                };
            }
        }

        rows.Sort((a, b) => a.Pid.CompareTo(b.Pid));
        return rows;
    }

    /// <summary>
    /// The delta between this reading and the previous one for the same
    /// (PID, start time), as a share of one logical CPU's time over the interval,
    /// scaled by the machine's logical CPUs. Null on the first sight.
    /// </summary>
    private double? Percent(int pid, ulong? start, long? ticks, long stampMs)
    {
        if (ticks is not long current || start is null)
        {
            return null;
        }

        var key = (pid, start);
        double? percent = null;
        if (_cpu.TryGetValue(key, out CpuMark earlier)
            && stampMs > earlier.Stamp
            && current >= earlier.Ticks)
        {
            double elapsedSeconds = (stampMs - earlier.Stamp) / 1000.0;
            double capacity = elapsedSeconds * _clockTicks * _logicalCpus;
            if (capacity > 0)
            {
                percent = Math.Round(
                    Math.Clamp((current - earlier.Ticks) / capacity * 100, 0, 100),
                    1,
                    MidpointRounding.AwayFromZero);
            }
        }

        _cpu[key] = new CpuMark(current, stampMs);
        return percent;
    }

    /// <summary>
    /// UID to account name from <c>/etc/passwd</c>, parsed once. An unknown UID
    /// is shown as its number, which is still more useful than a blank cell.
    /// </summary>
    private string UserName(int? uid)
    {
        if (uid is not int value)
        {
            return string.Empty;
        }

        _users ??= ReadUsers();
        return _users.TryGetValue(value, out string? name)
            ? name
            : value.ToString(CultureInfo.InvariantCulture);
    }

    private Dictionary<int, string> ReadUsers()
    {
        var users = new Dictionary<int, string>();
        string? text = SysFs.ReadText(_root, "etc", "passwd");
        if (text is null)
        {
            return users;
        }

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split(':');
            if (fields.Length > 2
                && fields[0].Length > 0
                && int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int uid)
                && uid >= 0)
            {
                users[uid] = fields[0];
            }
        }
        return users;
    }

    private static string StatusField(string status, string key)
    {
        foreach (string line in status.Split('\n'))
        {
            if (line.StartsWith(key + ":", StringComparison.Ordinal))
            {
                return line[(key.Length + 1)..].Trim();
            }
        }
        return string.Empty;
    }

    private readonly record struct CpuMark(long Ticks, long Stamp);
}
