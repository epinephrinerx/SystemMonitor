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
/// snapshot whatever page is showing. A chart opened later already has a past.
///
/// R-030 adds the series its pages draw: swap, CPU and GPU temperatures, the
/// per-physical-disk rates and activity, both directions of every adapter, and
/// each battery's charge and power. A series that already held the same data is
/// reused; the rest are keyed by the same page key the tab uses, so a replugged
/// device finds its own past again.
/// </summary>
public sealed class HistoryBoard
{
    public const int Capacity = 72;

    private readonly Dictionary<string, History> _disks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _adapters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _diskIo = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _adapterRate = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _adapterDown = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _adapterUp = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _gpuUsage = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _gpuEncoder = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _gpuDecoder = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _gpuMemory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _gpuTemp = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _engines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _diskActivity = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _diskRead = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _diskWrite = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _diskTemp = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _batteryCharge = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _batteryPower = new(StringComparer.Ordinal);
    private readonly Dictionary<string, History> _processes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<History> _cores = new();
    private long _lastMemoryStamp = long.MinValue;

    public History Cpu { get; } = new(Capacity);
    public History Ram { get; } = new(Capacity);
    public History Gpu { get; } = new(Capacity);
    public History Swap { get; } = new(Capacity);
    public History CpuTemp { get; } = new(Capacity);

    public IReadOnlyDictionary<string, History> Disks => _disks;
    public IReadOnlyDictionary<string, History> Adapters => _adapters;
    public IReadOnlyDictionary<string, History> DiskIo => _diskIo;
    public IReadOnlyDictionary<string, History> AdapterRates => _adapterRate;
    public IReadOnlyList<History> Cores => _cores;
    public IReadOnlyDictionary<string, History> ProcessGroups => _processes;

    public History Disk(string letter) => Get(_disks, letter);

    public History Adapter(string id) => Get(_adapters, id);

    /// <summary>Throughput in MB/s, for a volume card.</summary>
    public History DiskThroughput(string mount) => Get(_diskIo, mount);

    /// <summary>Throughput in MB/s, for a network card.</summary>
    public History AdapterThroughput(string id) => Get(_adapterRate, id);

    /// <summary>One adapter's download rate in MB/s.</summary>
    public History AdapterDown(string id) => Get(_adapterDown, id);

    /// <summary>One adapter's upload rate in MB/s.</summary>
    public History AdapterUp(string id) => Get(_adapterUp, id);

    /// <summary>One GPU's overall usage, by its page key.</summary>
    public History GpuUsage(string key) => Get(_gpuUsage, key);

    /// <summary>One GPU's video-encoder use.</summary>
    public History GpuEncoder(string key) => Get(_gpuEncoder, key);

    /// <summary>One GPU's video-decoder use.</summary>
    public History GpuDecoder(string key) => Get(_gpuDecoder, key);

    /// <summary>One GPU's dedicated video memory, as a percentage of its capacity.</summary>
    public History GpuMemory(string key) => Get(_gpuMemory, key);

    /// <summary>One GPU's temperature.</summary>
    public History GpuTemp(string key) => Get(_gpuTemp, key);

    /// <summary>One engine of one GPU, by the GPU's page key and the engine name.</summary>
    public History Engine(string gpuKey, string name) =>
        Get(_engines, gpuKey + "\u001f" + name);

    /// <summary>One physical disk's busy share, by its page key.</summary>
    public History DiskActivity(string key) => Get(_diskActivity, key);

    /// <summary>One physical disk's read rate in MB/s.</summary>
    public History DiskRead(string key) => Get(_diskRead, key);

    /// <summary>One physical disk's write rate in MB/s.</summary>
    public History DiskWrite(string key) => Get(_diskWrite, key);

    /// <summary>One physical disk's temperature.</summary>
    public History DiskTemp(string key) => Get(_diskTemp, key);

    /// <summary>One battery's charge in percent.</summary>
    public History BatteryCharge(string id) => Get(_batteryCharge, id);

    /// <summary>One battery's power in watts.</summary>
    public History BatteryPower(string id) => Get(_batteryPower, id);

    /// <summary>One core's usage, by position.</summary>
    public History Core(int index)
    {
        while (_cores.Count <= index)
        {
            _cores.Add(new History(Capacity));
        }
        return _cores[index];
    }

    /// <summary>
    /// One process group's working set in MB. Filled only while process
    /// sampling is on, so a group that is picked for the graph already has a
    /// past and the histories do not grow while the RAM tab is closed.
    /// </summary>
    public History ProcessGroup(string name) => Get(_processes, name);

    /// <summary>Push one fresh snapshot into every series.</summary>
    public void Push(Snapshot snapshot)
    {
        Cpu.Add(snapshot.CpuTotal);
        Ram.Add(snapshot.Ram.Usage);
        Gpu.Add(snapshot.Gpu.Present ? snapshot.Gpu.Usage : 0);
        Swap.Add(snapshot.Ram.SwapUsage);
        CpuTemp.Add(snapshot.CpuTemp ?? 0);

        for (int i = 0; i < snapshot.Cores.Count; i++)
        {
            Core(i).Add(snapshot.Cores[i].Usage);
        }

        var liveGpus = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < snapshot.Gpus.Count; i++)
        {
            Gpu gpu = snapshot.Gpus[i];
            string key = PageKeys.Gpu(snapshot.Gpus, i);
            liveGpus.Add(key);

            GpuUsage(key).Add(gpu.Usage);
            GpuEncoder(key).Add(gpu.EncoderUsage ?? 0);
            GpuDecoder(key).Add(gpu.DecoderUsage ?? 0);
            GpuMemory(key).Add(gpu.DedicatedTotalGb > 0
                ? Math.Clamp(gpu.DedicatedGb / gpu.DedicatedTotalGb * 100, 0, 100)
                : 0);
            GpuTemp(key).Add(gpu.Temp ?? 0);
            foreach (GpuEngine engine in gpu.Engines)
            {
                Engine(key, engine.Name).Add(engine.Usage);
            }
        }
        DropGpuSeries(liveGpus);

        PushDisks(snapshot);

        foreach (Adapter adapter in snapshot.Adapters)
        {
            Adapter(adapter.Id).Add(adapter.Usage);
            AdapterThroughput(adapter.Id).Add(adapter.DownMb + adapter.UpMb);
            AdapterDown(adapter.Id).Add(adapter.DownMb);
            AdapterUp(adapter.Id).Add(adapter.UpMb);
        }

        foreach (Battery battery in snapshot.Batteries)
        {
            BatteryCharge(battery.Id).Add(battery.Percent ?? 0);
            BatteryPower(battery.Id).Add(battery.PowerW ?? 0);
        }

        // An empty process list means sampling is off; the old series stay as
        // they are rather than falling to zero. A republished list carries the
        // same MemoryStamp, so it is not pushed a second time: the history
        // follows reads, not redraws.
        if (snapshot.Processes.Count > 0
            && (snapshot.MemoryStamp <= 0 || snapshot.MemoryStamp != _lastMemoryStamp))
        {
            _lastMemoryStamp = snapshot.MemoryStamp;
            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProcessGroup group in MemoryBoard.Group(snapshot.Processes))
            {
                live.Add(group.Name);
                ProcessGroup(group.Name).Add(group.WorkingSet / 1048576.0);
            }
            foreach (string name in _processes.Keys.ToList())
            {
                if (!live.Contains(name))
                {
                    _processes.Remove(name);
                }
            }
        }
    }

    /// <summary>
    /// A mount's series are by mount point; the physical disk's own series are
    /// by page key. One disk with two mounts is pushed once.
    /// </summary>
    private void PushDisks(Snapshot snapshot)
    {
        var temperatures = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Disk disk in snapshot.Disks)
        {
            if (disk.Detail?.Disk is not { } hardware)
            {
                continue;
            }
            string key = PageKeys.Disk(hardware);
            if (disk.Temp is int celsius
                && (!temperatures.TryGetValue(key, out int highest) || celsius > highest))
            {
                temperatures[key] = celsius;
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Disk disk in snapshot.Disks)
        {
            Disk(disk.Letter).Add(disk.Usage);
            DiskThroughput(disk.Letter).Add(disk.ReadMb + disk.WriteMb);

            if (disk.Detail?.Disk is not { } hardware)
            {
                continue;
            }

            string key = PageKeys.Disk(hardware);
            if (!seen.Add(key))
            {
                continue;
            }

            DiskActivity(key).Add(hardware.ActivityPercent ?? 0);
            DiskRead(key).Add(hardware.ReadBytesPerSec / 1048576.0);
            DiskWrite(key).Add(hardware.WriteBytesPerSec / 1048576.0);
            DiskTemp(key).Add(temperatures.TryGetValue(key, out int temp) ? temp : 0);
        }
    }

    /// <summary>
    /// Forget every GPU series whose card is no longer in the snapshot. A
    /// replugged card comes back under the same PCI address, so it finds its
    /// own past again; a card that is gone leaves no stale graph behind.
    /// </summary>
    private void DropGpuSeries(HashSet<string> live)
    {
        Prune(_gpuUsage);
        Prune(_gpuEncoder);
        Prune(_gpuDecoder);
        Prune(_gpuMemory);
        Prune(_gpuTemp);

        foreach (string engineKey in _engines.Keys.ToList())
        {
            int separator = engineKey.IndexOf('\u001f');
            string gpuKey = separator < 0 ? engineKey : engineKey[..separator];
            if (!live.Contains(gpuKey))
            {
                _engines.Remove(engineKey);
            }
        }

        void Prune(Dictionary<string, History> series)
        {
            foreach (string key in series.Keys.ToList())
            {
                if (!live.Contains(key))
                {
                    series.Remove(key);
                }
            }
        }
    }

    private static History Get(Dictionary<string, History> map, string key)
    {
        if (!map.TryGetValue(key, out History? history))
        {
            history = new History(Capacity);
            map[key] = history;
        }
        return history;
    }
}
