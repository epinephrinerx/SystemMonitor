namespace SysMonitor.Model;

/// <summary>One running process and what it holds in memory.</summary>
public sealed class ProcessEntry
{
    public required int Pid { get; init; }
    public required string Name { get; init; }

    /// <summary>RAM the process has resident right now.</summary>
    public long WorkingSet { get; init; }

    /// <summary>Memory only this process owns (commit), shared pages left out.</summary>
    public long PrivateBytes { get; init; }

    /// <summary>False when Windows would not say; the sizes are then zero.</summary>
    public bool Readable { get; init; } = true;
}

/// <summary>How a service starts: the three settings a person can choose.</summary>
public enum ServiceStart
{
    Automatic,
    Manual,
    Disabled,
    Other,
}

/// <summary>One Windows service, with the process it runs inside.</summary>
public sealed class ServiceEntry
{
    public required string Name { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool Running { get; init; }

    /// <summary>The state as Windows words it: Running, Stopped, Start Pending...</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Zero when the service is not running.</summary>
    public int Pid { get; init; }

    public ServiceStart Start { get; init; } = ServiceStart.Other;

    /// <summary>Resident memory of the host process; zero when stopped or unreadable.</summary>
    public long WorkingSet { get; init; }

    /// <summary>
    /// How many services share this process, itself included. More than one is
    /// the svchost case: the memory belongs to the whole group, not to this one.
    /// </summary>
    public int SharedWith { get; init; } = 1;
}

/// <summary>Processes of one name, summed. "chrome" thirty times is one line.</summary>
public sealed class ProcessGroup
{
    public required string Name { get; init; }
    public required IReadOnlyList<ProcessEntry> Members { get; init; }

    public long WorkingSet { get; init; }
    public int Count => Members.Count;
}

/// <summary>
/// Turns raw readings into what the memory tab lists. Pure, so grouping and
/// the "shared with" count can be checked without a machine to read.
/// </summary>
public static class MemoryBoard
{
    public static IReadOnlyList<ProcessGroup> Group(IEnumerable<ProcessEntry> processes) =>
        processes
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProcessGroup
            {
                Name = g.First().Name,
                Members = g.OrderByDescending(p => p.WorkingSet).ThenBy(p => p.Pid).ToList(),
                WorkingSet = g.Sum(p => p.WorkingSet),
            })
            .OrderByDescending(g => g.WorkingSet)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Put each service's host-process memory on it and count how many
    /// services share that process.
    /// </summary>
    public static IReadOnlyList<ServiceEntry> Attach(
        IEnumerable<ServiceEntry> services, IEnumerable<ProcessEntry> processes)
    {
        var memory = new Dictionary<int, long>();
        foreach (ProcessEntry process in processes)
        {
            memory[process.Pid] = process.WorkingSet;
        }

        List<ServiceEntry> list = services.ToList();
        var sharing = list.Where(s => s.Pid > 0)
                          .GroupBy(s => s.Pid)
                          .ToDictionary(g => g.Key, g => g.Count());

        return list.Select(s => new ServiceEntry
        {
            Name = s.Name,
            Display = s.Display,
            Running = s.Running,
            State = s.State,
            Pid = s.Pid,
            Start = s.Start,
            WorkingSet = s.Pid > 0 && memory.TryGetValue(s.Pid, out long bytes) ? bytes : 0,
            SharedWith = s.Pid > 0 && sharing.TryGetValue(s.Pid, out int n) ? n : 1,
        }).ToList();
    }

    /// <summary>"1.2 GB", "340 MB", "12 MB": the unit that keeps the number short.</summary>
    public static string Size(long bytes)
    {
        double mb = bytes / 1048576.0;
        return mb >= 1024 ? $"{mb / 1024:F1} GB"
            : mb >= 100 ? $"{mb:F0} MB"
            : $"{mb:F1} MB";
    }
}
