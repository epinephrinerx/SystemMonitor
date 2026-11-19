using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>One process as the memory tab lists it, with the kill decision made.</summary>
public sealed class MemProcess
{
    public required int Pid { get; init; }
    public required string Name { get; init; }

    /// <summary>
    /// Field 22 of the row's stat file; sent with the PID and the name when
    /// killing. Null when the row had none, which is never a match.
    /// </summary>
    public ulong? StartTime { get; init; }

    public long WorkingSet { get; init; }
    public long PrivateBytes { get; init; }
    public bool Readable { get; init; }
    public bool Protected { get; init; }

    public bool CanKill => Readable && !Protected;

    public string SizeText => Readable ? MemoryBoard.Size(WorkingSet) : string.Empty;

    public string PrivateText(Lang lang) =>
        Readable ? lang.Get("mem_private", MemoryBoard.Size(PrivateBytes)) : lang["mem_no_access"];
}

/// <summary>Processes of one name, summed, as one expandable line.</summary>
public sealed class MemProcessGroup
{
    public required string Name { get; init; }
    public required IReadOnlyList<MemProcess> Members { get; init; }
    public long WorkingSet { get; init; }
    public bool Graphed { get; init; }

    public int Count => Members.Count;
    public string SizeText => MemoryBoard.Size(WorkingSet);

    /// <summary>At least one member may be ended; the group kill button shows then.</summary>
    public bool CanKill => Members.Any(m => m.CanKill);

    public int KillableCount => Members.Count(m => m.CanKill);
}

/// <summary>One systemd unit as the memory tab lists it.</summary>
public sealed class MemService
{
    public required ServiceEntry Entry { get; init; }

    public string Name => Entry.Name;
    public string Display => Entry.Display.Length > 0 ? Entry.Display : Entry.Name;
    public bool Running => Entry.Running;
    public int Pid => Entry.Pid;
    public long WorkingSet => Entry.WorkingSet;
    public int SharedWith => Entry.SharedWith;
    public ServiceStart Start => Entry.Start;
    public bool UserUnit => Entry.UserUnit;
    public string State => Entry.State;

    /// <summary>The spec's three states; anything else is "changing".</summary>
    public string StateText(Lang lang) =>
        Running ? lang["mem_running"]
        : State is "inactive" or "failed" or "dead" ? lang["mem_stopped"]
        : lang["mem_pending"];

    public string StartupText(Lang lang) => Start switch
    {
        ServiceStart.Automatic => lang["mem_auto"],
        ServiceStart.Manual => lang["mem_manual"],
        ServiceStart.Disabled => lang["mem_disabled"],
        _ => lang["mem_other"],
    };

    public string SizeText => MemoryBoard.Size(WorkingSet);

    /// <summary>A stopped, startable unit gets the start button.</summary>
    public bool CanStart => !Running && Start != ServiceStart.Disabled;

    public bool CanStop => Running;
}

/// <summary>
/// The RAM tab's process and service lists (spec section 5c): filtering, caps,
/// sort order, protection and the two service scopes. Drawing lives in the app.
/// </summary>
public sealed class MemoryTabModel
{
    public IReadOnlyList<MemProcessGroup> Groups { get; init; } = Array.Empty<MemProcessGroup>();
    public IReadOnlyList<MemService> UserServices { get; init; } = Array.Empty<MemService>();
    public IReadOnlyList<MemService> SystemServices { get; init; } = Array.Empty<MemService>();
    public string GroupNote { get; init; } = string.Empty;
    public string ServiceNote { get; init; } = string.Empty;

    /// <summary>All units grouped by host PID, for the kill dialog's host warning.</summary>
    public IReadOnlyDictionary<int, int> ServiceHosts { get; init; } =
        new Dictionary<int, int>();

    /// <summary>The graphed group while it is still running, even when filtered out.</summary>
    public string? GraphedGroup { get; init; }

    public long GraphedBytes { get; init; }
    public int GraphedCount { get; init; }

    public int TotalServices => UserServices.Count + SystemServices.Count;

    public MemProcessGroup? Group(string? name) =>
        name is null
            ? null
            : Groups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));

    public MemService? Service(string name) =>
        UserServices.Concat(SystemServices)
            .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));

    /// <summary>How many services run inside any of these processes.</summary>
    public int HostedServices(IEnumerable<int> pids)
    {
        int count = 0;
        foreach (int pid in pids.Distinct())
        {
            if (ServiceHosts.TryGetValue(pid, out int shared))
            {
                count += shared;
            }
        }
        return count;
    }
}

/// <summary>Which processes may not be ended, and why.</summary>
public static class ProcessProtection
{
    /// <summary>
    /// Session-critical programs. Ending one of these ends the desktop, the
    /// session bus or the login session, so they are read-only.
    /// </summary>
    public static readonly IReadOnlyList<string> SessionNames = new[]
    {
        "systemd",
        "init",
        "kwin_wayland",
        "kwin_x11",
        "plasmashell",
        "ksmserver",
        "kded6",
        "Xwayland",
        "Xorg",
        "gnome-shell",
        "gnome-session-binary",
        "mutter",
        "dbus-daemon",
        "dbus-broker",
        "pipewire",
        "wireplumber",
        "pulseaudio",
        "sddm",
        "gdm",
        "polkitd",
        "systemd-logind",
    };

    /// <summary>
    /// PID 1 and 2, kernel threads, our own process, another user's processes,
    /// entries the kernel would not describe or whose owner it did not report,
    /// and the session names above.
    /// </summary>
    public static bool IsProtected(ProcessEntry process, int ownPid, int ownUid) =>
        IsProtected(
            process.Pid,
            process.Name,
            process.Uid,
            process.Readable,
            process.KernelThread,
            ownPid,
            ownUid);

    /// <summary>
    /// The same rules against a fresh action-time reading, so protection is
    /// never taken from the row that was clicked.
    /// </summary>
    public static bool IsProtected(ProcessFacts facts, int ownPid, int ownUid) =>
        IsProtected(
            facts.Pid,
            facts.Name,
            facts.Uid,
            facts.Readable,
            facts.KernelThread,
            ownPid,
            ownUid);

    private static bool IsProtected(
        int pid,
        string name,
        int? uid,
        bool readable,
        bool kernelThread,
        int ownPid,
        int ownUid)
    {
        if (pid <= 2 || pid == ownPid)
        {
            return true;
        }
        if (!readable || kernelThread)
        {
            return true;
        }

        // An unknown owner is treated like an unreadable entry: the process
        // could belong to anyone, so it is not ours to end.
        if (uid is not { } owner)
        {
            return true;
        }
        if (ownUid >= 0 && owner != ownUid)
        {
            return true;
        }
        return SessionNames.Any(session =>
            string.Equals(session, name, StringComparison.OrdinalIgnoreCase));
    }
}

public static class MemoryTabBoard
{
    public const int GroupCap = 40;
    public const int GroupCapSearching = 120;
    public const int ServiceCap = 60;
    public const int ServiceCapSearching = 120;

    public static MemoryTabModel Build(
        Snapshot? snapshot,
        Lang lang,
        string? query,
        int ownPid,
        int ownUid,
        string? graphedGroup)
    {
        string search = (query ?? string.Empty).Trim();
        bool searching = search.Length > 0;

        List<ProcessGroup> all = snapshot is { Processes.Count: > 0 }
            ? MemoryBoard.Group(snapshot.Processes).ToList()
            : new List<ProcessGroup>();
        ProcessGroup? picked = graphedGroup is null
            ? null
            : all.FirstOrDefault(g =>
                string.Equals(g.Name, graphedGroup, StringComparison.OrdinalIgnoreCase));

        List<MemProcessGroup> groups = BuildGroups(
            all, search, searching, ownPid, ownUid, picked?.Name, out int groupTotal);

        List<MemService> services = BuildServices(snapshot, search, searching, out int serviceTotal);
        int serviceCap = searching ? ServiceCapSearching : ServiceCap;
        var shown = services.Take(serviceCap).ToList();

        var hosts = new Dictionary<int, int>();
        if (snapshot is not null)
        {
            foreach (ServiceEntry entry in snapshot.Services)
            {
                if (entry.Pid > 0)
                {
                    hosts[entry.Pid] = hosts.TryGetValue(entry.Pid, out int n) ? n + 1 : 1;
                }
            }
        }

        return new MemoryTabModel
        {
            Groups = groups,
            UserServices = shown.Where(s => s.UserUnit).ToList(),
            SystemServices = shown.Where(s => !s.UserUnit).ToList(),
            GroupNote = groupTotal > groups.Count
                ? lang.Get("mem_group_note", groups.Count, groupTotal)
                : string.Empty,
            ServiceNote = serviceTotal > shown.Count
                ? lang.Get("mem_service_note", shown.Count, serviceTotal)
                : string.Empty,
            GraphedGroup = picked?.Name,
            GraphedBytes = picked?.WorkingSet ?? 0,
            GraphedCount = picked?.Count ?? 0,
            ServiceHosts = hosts,
        };
    }

    private static List<MemProcessGroup> BuildGroups(
        IReadOnlyList<ProcessGroup> all,
        string search,
        bool searching,
        int ownPid,
        int ownUid,
        string? graphed,
        out int total)
    {
        total = 0;
        IEnumerable<ProcessGroup> source = all;
        if (searching)
        {
            source = source.Where(g =>
                g.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        List<ProcessGroup> filtered = source.ToList();
        total = filtered.Count;

        int cap = searching ? GroupCapSearching : GroupCap;
        var result = new List<MemProcessGroup>(Math.Min(cap, filtered.Count));
        foreach (ProcessGroup group in filtered.Take(cap))
        {
            result.Add(new MemProcessGroup
            {
                Name = group.Name,
                WorkingSet = group.WorkingSet,
                Graphed = string.Equals(group.Name, graphed, StringComparison.OrdinalIgnoreCase),
                Members = group.Members.Select(member => new MemProcess
                {
                    Pid = member.Pid,
                    Name = member.Name,
                    StartTime = member.StartTime,
                    WorkingSet = member.WorkingSet,
                    PrivateBytes = member.PrivateBytes,
                    Readable = member.Readable,
                    Protected = ProcessProtection.IsProtected(member, ownPid, ownUid),
                }).ToList(),
            });
        }
        return result;
    }

    private static List<MemService> BuildServices(
        Snapshot? snapshot, string search, bool searching, out int total)
    {
        total = 0;
        if (snapshot is null || snapshot.Services.Count == 0)
        {
            return new List<MemService>();
        }

        // Memory belongs to the host process, and several units can share it.
        IEnumerable<ServiceEntry> attached = MemoryBoard.Attach(
            snapshot.Services, snapshot.Processes);
        if (searching)
        {
            attached = attached.Where(s =>
                s.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || s.Display.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        List<ServiceEntry> filtered = attached
            .OrderByDescending(s => s.Running)
            .ThenByDescending(s => s.WorkingSet)
            .ThenBy(s => s.Display.Length > 0 ? s.Display : s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        total = filtered.Count;
        return filtered.Select(s => new MemService { Entry = s }).ToList();
    }
}
