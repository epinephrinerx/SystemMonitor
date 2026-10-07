using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SysMonitor.Model;

namespace SysMonitor.ViewModels;

public abstract class Notifier : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaiseChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>One process under its group: pid, size, an end button.</summary>
public sealed class ProcessVm : Notifier
{
    private string _size = string.Empty;
    private string _detail = string.Empty;

    public required int Pid { get; init; }
    public required string Name { get; init; }
    public ProcessEntry Entry { get; set; } = null!;
    public string Label => "PID " + Pid;

    public string Size
    {
        get => _size;
        set => Set(ref _size, value);
    }

    /// <summary>Private memory, or "no access".</summary>
    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    public bool CanKill { get; set; }
}

/// <summary>Every process of one name: one line, expandable into its pids.</summary>
public sealed class ProcessGroupVm : Notifier
{
    private string _size = string.Empty;
    private string _count = string.Empty;
    private bool _expanded;
    private bool _selected;

    public required string Name { get; init; }
    public IReadOnlyList<ProcessEntry> Entries { get; set; } = Array.Empty<ProcessEntry>();
    public ObservableCollection<ProcessVm> Members { get; } = new();

    public string Size
    {
        get => _size;
        set => Set(ref _size, value);
    }

    public string Count
    {
        get => _count;
        set => Set(ref _count, value);
    }

    public bool Expanded
    {
        get => _expanded;
        set
        {
            Set(ref _expanded, value);
            RaiseChanged(nameof(Arrow));
        }
    }

    public string Arrow => _expanded ? "" : "";

    /// <summary>The group whose graph is showing above the lists.</summary>
    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public bool CanKill { get; set; }
}

/// <summary>One service row.</summary>
public sealed class ServiceVm : Notifier
{
    private string _state = string.Empty;
    private string _memory = string.Empty;
    private string _shared = string.Empty;
    private string _startup = string.Empty;
    private bool _running;
    private bool _canStart;

    public required string Name { get; init; }
    public ServiceEntry Entry { get; set; } = null!;
    private string _display = string.Empty;

    public string Display
    {
        get => _display;
        set => Set(ref _display, value);
    }

    public string State
    {
        get => _state;
        set => Set(ref _state, value);
    }

    /// <summary>" · PID 1234 · 45 MB", or empty for a stopped service; it follows the state on one line.</summary>
    public string Memory
    {
        get => _memory;
        set => Set(ref _memory, value);
    }

    /// <summary>Says the memory is the host process's when other services live in it too.</summary>
    public string Shared
    {
        get => _shared;
        set => Set(ref _shared, value);
    }

    public string Startup
    {
        get => _startup;
        set => Set(ref _startup, value);
    }

    public bool Running
    {
        get => _running;
        set => Set(ref _running, value);
    }

    public bool CanStart
    {
        get => _canStart;
        set => Set(ref _canStart, value);
    }
}

/// <summary>
/// What the memory tab lists: process groups and services, filtered by one
/// search box, updated in place so an open group stays open and a button
/// under the mouse is still the same button when the mouse comes up.
/// </summary>
public sealed class MemoryViewModel : Notifier
{
    internal const int GroupCap = 40;
    internal const int ServiceCap = 60;
    internal const int SearchCap = 120;

    private readonly Dictionary<string, History> _history = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ProcessGroup> _groups = Array.Empty<ProcessGroup>();
    private IReadOnlyList<ServiceEntry> _services = Array.Empty<ServiceEntry>();
    private string _filter = string.Empty;
    private string _selected = string.Empty;
    private string _groupNote = string.Empty;
    private string _serviceNote = string.Empty;

    public ObservableCollection<ProcessGroupVm> Groups { get; } = new();
    public ObservableCollection<ServiceVm> Services { get; } = new();

    /// <summary>What the search box holds. Changing it re-lists at once.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (_filter == value)
            {
                return;
            }
            Set(ref _filter, value);
            Refill();
        }
    }

    public string GroupNote
    {
        get => _groupNote;
        private set => Set(ref _groupNote, value);
    }

    public string ServiceNote
    {
        get => _serviceNote;
        private set => Set(ref _serviceNote, value);
    }

    /// <summary>The name of the group whose graph is shown; empty for none.</summary>
    public string Selected => _selected;

    public IReadOnlyList<ProcessGroup> All => _groups;

    private Lang _lang = new("th");
    private string _status = string.Empty;

    /// <summary>The words on the tab, so a binding like Text[mem_kill] follows the language.</summary>
    public Lang Text => _lang;

    /// <summary>The outcome of the last kill, stop or start; empty when there is nothing to say.</summary>
    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    /// <summary>Take a reading. Returns nothing; the collections change in place.</summary>
    public void Update(Snapshot snap, Lang lang)
    {
        if (!ReferenceEquals(_lang, lang))
        {
            _lang = lang;
            RaiseChanged(nameof(Text));
        }
        _groups = MemoryBoard.Group(snap.Processes);
        _services = snap.Services;

        // Each group's recent size, kept for as long as it is running, so
        // picking one shows its past and not a graph that starts from empty.
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ProcessGroup group in _groups)
        {
            present.Add(group.Name);
            if (!_history.TryGetValue(group.Name, out History? history))
            {
                history = new History(ChartCard.Points);
                _history[group.Name] = history;
            }
            history.Add(group.WorkingSet / 1048576.0);
        }
        foreach (string gone in _history.Keys.Where(k => !present.Contains(k)).ToList())
        {
            _history.Remove(gone);
        }
        if (_selected.Length > 0 && !present.Contains(_selected))
        {
            _selected = string.Empty;
        }
        Refill();
    }

    public History? HistoryOf(string name) =>
        _history.TryGetValue(name, out History? history) ? history : null;

    public ProcessGroup? SelectedGroup =>
        _selected.Length == 0 ? null
            : _groups.FirstOrDefault(g => string.Equals(g.Name, _selected, StringComparison.OrdinalIgnoreCase));

    /// <summary>Show this group's graph, or hide it if it is already showing.</summary>
    public void Toggle(string name)
    {
        _selected = string.Equals(_selected, name, StringComparison.OrdinalIgnoreCase)
            ? string.Empty : name;
        foreach (ProcessGroupVm vm in Groups)
        {
            vm.Selected = string.Equals(vm.Name, _selected, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>How many services run inside this process. More than a few is a svchost.</summary>
    public int ServicesOn(int pid) => _services.Count(s => s.Pid == pid);

    /// <summary>"chrome ×12 · 2.3 GB": the biggest thing in memory, in one line.</summary>
    public static string TopLine(IReadOnlyList<ProcessGroup> groups)
    {
        if (groups.Count == 0)
        {
            return string.Empty;
        }
        ProcessGroup top = groups[0];
        return (top.Count > 1 ? $"{top.Name} ×{top.Count}" : top.Name)
             + " · " + MemoryBoard.Size(top.WorkingSet);
    }

    // ---------------------------------------------------------------- listing
    private void Refill()
    {
        bool searching = _filter.Trim().Length > 0;

        IEnumerable<ProcessGroup> groups = _groups;
        if (searching)
        {
            groups = groups.Where(g => g.Name.Contains(_filter.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        List<ProcessGroup> allGroups = groups.ToList();
        int groupCap = searching ? SearchCap : GroupCap;
        List<ProcessGroup> shownGroups = allGroups.Take(groupCap).ToList();
        GroupNote = allGroups.Count > shownGroups.Count
            ? string.Format(_lang["mem_group_note"], shownGroups.Count, allGroups.Count)
            : string.Empty;

        Sync(Groups, shownGroups, g => g.Name, StringComparer.OrdinalIgnoreCase,
             g => new ProcessGroupVm { Name = g.Name }, FillGroup);

        IEnumerable<ServiceEntry> services = _services;
        if (searching)
        {
            string text = _filter.Trim();
            services = services.Where(s =>
                s.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || s.Display.Contains(text, StringComparison.OrdinalIgnoreCase));
        }
        List<ServiceEntry> allServices = services
            .OrderByDescending(s => s.Running)
            .ThenByDescending(s => s.WorkingSet)
            .ThenBy(s => s.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int serviceCap = searching ? SearchCap : ServiceCap;
        List<ServiceEntry> shownServices = allServices.Take(serviceCap).ToList();
        ServiceNote = allServices.Count > shownServices.Count
            ? string.Format(_lang["mem_service_note"], shownServices.Count, allServices.Count)
            : string.Empty;

        Sync(Services, shownServices, s => s.Name, StringComparer.OrdinalIgnoreCase,
             s => new ServiceVm { Name = s.Name }, FillService);
    }

    private void FillGroup(ProcessGroupVm vm, ProcessGroup group)
    {
        vm.Entries = group.Members;
        vm.Size = MemoryBoard.Size(group.WorkingSet);
        vm.Count = group.Count > 1 ? string.Format(_lang["mem_count"], group.Count) : string.Empty;
        vm.CanKill = group.Members.Any(MemoryTools.CanKill);
        vm.Selected = string.Equals(vm.Name, _selected, StringComparison.OrdinalIgnoreCase);

        Sync(vm.Members, group.Members.ToList(), p => p.Pid.ToString(), StringComparer.Ordinal,
             p => new ProcessVm { Pid = p.Pid, Name = p.Name }, (member, p) =>
             {
                 member.Entry = p;
                 member.Size = p.Readable ? MemoryBoard.Size(p.WorkingSet) : _lang["mem_no_access"];
                 member.Detail = p.Readable
                     ? string.Format(_lang["mem_private"], MemoryBoard.Size(p.PrivateBytes))
                     : string.Empty;
                 member.CanKill = MemoryTools.CanKill(p);
             });
    }

    private void FillService(ServiceVm vm, ServiceEntry s)
    {
        vm.Entry = s;
        vm.Display = s.Display;
        vm.Running = s.Running;
        vm.State = s.State switch
        {
            "Running" => _lang["mem_running"],
            "Stopped" => _lang["mem_stopped"],
            _ => _lang["mem_pending"],
        };
        vm.Memory = s.Pid > 0
            ? $" · PID {s.Pid} · " + MemoryBoard.Size(s.WorkingSet)
            : string.Empty;
        vm.Shared = s.SharedWith > 1
            ? string.Format(_lang["mem_shared"], s.SharedWith)
            : string.Empty;
        vm.Startup = s.Start switch
        {
            ServiceStart.Automatic => _lang["mem_auto"],
            ServiceStart.Manual => _lang["mem_manual"],
            ServiceStart.Disabled => _lang["mem_disabled"],
            _ => _lang["mem_other"],
        };
        vm.CanStart = !s.Running && s.Start != ServiceStart.Disabled;
    }

    /// <summary>
    /// Make a collection match a list without clearing it: rows that are still
    /// wanted are kept (and moved if the order changed), the rest are added or
    /// dropped.
    /// </summary>
    internal static void Sync<TVm, TData>(ObservableCollection<TVm> target, IReadOnlyList<TData> wanted,
        Func<TData, string> keyOfData, IEqualityComparer<string> comparer,
        Func<TData, TVm> create, Action<TVm, TData> fill)
        where TVm : Notifier
    {
        Func<TVm, string> keyOfVm = vm => vm switch
        {
            ProcessGroupVm g => g.Name,
            ProcessVm p => p.Pid.ToString(),
            ServiceVm s => s.Name,
            _ => string.Empty,
        };

        var keys = new HashSet<string>(wanted.Select(keyOfData), comparer);
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(keyOfVm(target[i])))
            {
                target.RemoveAt(i);
            }
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            string key = keyOfData(wanted[i]);
            int at = -1;
            for (int j = i; j < target.Count; j++)
            {
                if (comparer.Equals(keyOfVm(target[j]), key))
                {
                    at = j;
                    break;
                }
            }
            if (at < 0)
            {
                target.Insert(i, create(wanted[i]));
            }
            else if (at != i)
            {
                target.Move(at, i);
            }
            fill(target[i], wanted[i]);
        }
    }
}
