using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

public enum DiskAlertLevel
{
    None,
    Warm,
    Hot,
}

/// <summary>One drive's warning, with the numbers it was raised on.</summary>
public sealed class DiskAlert
{
    public required string Letter { get; init; }
    public required DiskAlertLevel Level { get; init; }
    public required int Usage { get; init; }

    public bool IsHot => Level == DiskAlertLevel.Hot;

    public string Text(Lang lang) =>
        lang.Get(IsHot ? "disk_hot" : "disk_warn", Letter, Usage);

    /// <summary>Amber for warm, red for hot; the same pair in both themes.</summary>
    public Colour Colour => Colour.FromHex(IsHot ? "#ef4444" : "#eab308");
}

/// <summary>
/// Disk-warning state from spec section 4b.
///
/// Levels are Warm at 70% and Hot at 90%, with two points of hysteresis on the
/// way down (Hot holds to 88, Warm to 68). A warning is pending when a drive is
/// first seen at Warm/Hot or when its level rises; dismissing clears it until
/// the level rises again; falling to Ok clears it; a drive that disappears is
/// forgotten.
///
/// Only local fixed drives are tracked: a mapped network share is skipped, and
/// so is removable media, which the Linux snapshot marks as
/// <see cref="Disk.Media"/> = "Removable" rather than through a flag of its own.
/// </summary>
public sealed class DiskAlertTracker
{
    private sealed class State
    {
        public DiskAlertLevel Level;
        public bool Pending;
        public int Usage;
    }

    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);

    /// <summary>The alerts raised by the latest update.</summary>
    public IReadOnlyList<DiskAlert> Alerts { get; private set; } = Array.Empty<DiskAlert>();

    public IReadOnlyList<DiskAlert> Update(IEnumerable<Disk> disks)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var alerts = new List<DiskAlert>();

        foreach (Disk disk in disks)
        {
            if (disk.IsNetwork || IsRemovable(disk))
            {
                continue;
            }

            string letter = disk.Letter;
            seen.Add(letter);
            int usage = Math.Clamp(disk.Usage, 0, 100);

            if (!_states.TryGetValue(letter, out State? state))
            {
                state = new State { Level = DiskAlertLevel.None };
                _states[letter] = state;
                DiskAlertLevel level = Classify(usage, DiskAlertLevel.None);
                state.Level = level;
                state.Usage = usage;
                state.Pending = level != DiskAlertLevel.None;
            }
            else
            {
                DiskAlertLevel level = Classify(usage, state.Level);
                if (level > state.Level)
                {
                    state.Pending = true;
                }
                else if (level == DiskAlertLevel.None)
                {
                    state.Pending = false;
                }
                state.Level = level;
                state.Usage = usage;
            }

            if (state.Pending && state.Level != DiskAlertLevel.None)
            {
                alerts.Add(new DiskAlert
                {
                    Letter = letter,
                    Level = state.Level,
                    Usage = state.Usage,
                });
            }
        }

        foreach (string letter in _states.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _states.Remove(letter);
        }

        Alerts = alerts;
        return alerts;
    }

    /// <summary>Clear the pending warning for one drive until its level rises again.</summary>
    public bool Dismiss(string letter)
    {
        if (!_states.TryGetValue(letter, out State? state) || !state.Pending)
        {
            return false;
        }
        state.Pending = false;
        Alerts = Alerts.Where(a => !string.Equals(a.Letter, letter, StringComparison.Ordinal)).ToList();
        return true;
    }

    public DiskAlert? For(string letter) =>
        Alerts.FirstOrDefault(a => string.Equals(a.Letter, letter, StringComparison.Ordinal));

    public DiskAlertLevel LevelOf(string letter) =>
        _states.TryGetValue(letter, out State? state) ? state.Level : DiskAlertLevel.None;

    public void Reset()
    {
        _states.Clear();
        Alerts = Array.Empty<DiskAlert>();
    }

    private static bool IsRemovable(Disk disk) =>
        string.Equals(disk.Media, "Removable", StringComparison.OrdinalIgnoreCase);

    private static DiskAlertLevel Classify(int usage, DiskAlertLevel previous)
    {
        if (usage >= 90)
        {
            return DiskAlertLevel.Hot;
        }
        if (usage >= 88 && previous == DiskAlertLevel.Hot)
        {
            return DiskAlertLevel.Hot;
        }
        if (usage >= 70)
        {
            return DiskAlertLevel.Warm;
        }
        if (usage >= 68 && previous is DiskAlertLevel.Warm or DiskAlertLevel.Hot)
        {
            return DiskAlertLevel.Warm;
        }
        return DiskAlertLevel.None;
    }
}

/// <summary>
/// The 72-point graph history from spec section 5b, filled on every fresh
/// snapshot whatever view is showing. Phase 2a has no charts, but the history
/// has to start filling now so the Full view opens with one.
/// </summary>
public sealed class HistoryBoard
{
    public const int Capacity = 72;

    private readonly Dictionary<string, History> _disks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _adapters = new(StringComparer.Ordinal);

    public History Cpu { get; } = new(Capacity);
    public History Ram { get; } = new(Capacity);
    public History Gpu { get; } = new(Capacity);

    public IReadOnlyDictionary<string, History> Disks => _disks;
    public IReadOnlyDictionary<string, History> Adapters => _adapters;

    public History Disk(string letter)
    {
        if (!_disks.TryGetValue(letter, out History? history))
        {
            history = new History(Capacity);
            _disks[letter] = history;
        }
        return history;
    }

    public History Adapter(string id)
    {
        if (!_adapters.TryGetValue(id, out History? history))
        {
            history = new History(Capacity);
            _adapters[id] = history;
        }
        return history;
    }

    /// <summary>Push one fresh snapshot into every series.</summary>
    public void Push(Snapshot snapshot)
    {
        Cpu.Add(snapshot.CpuTotal);
        Ram.Add(snapshot.Ram.Usage);
        Gpu.Add(snapshot.Gpu.Present ? snapshot.Gpu.Usage : 0);

        foreach (Disk disk in snapshot.Disks)
        {
            Disk(disk.Letter).Add(disk.Usage);
        }

        foreach (Adapter adapter in snapshot.Adapters)
        {
            Adapter(adapter.Id).Add(adapter.Usage);
        }
    }
}
