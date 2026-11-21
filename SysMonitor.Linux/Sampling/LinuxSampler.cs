using SysMonitor.Linux.Presentation;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sampling;

/// <summary>
/// Fills one <see cref="Snapshot"/> per tick from /proc and /sys.  Every source
/// is read inside its own catch: a missing file or a permission denial empties
/// that section and leaves the rest of the sample intact.
///
/// The first call reports <see cref="Snapshot.Ready"/> false because a usage
/// percentage needs two readings; from the second call on it is true.
/// </summary>
public sealed class LinuxSampler
{
    private readonly string _root;
    private readonly Func<long> _clock;
    private readonly DiskReader _disks;
    private readonly NetworkReader _network;
    private readonly GpuReader _gpus;
    private readonly ServiceReader _services;
    private readonly CpuInfo _cpuInfo;
    private readonly BatteryReader _battery;
    private readonly UPowerReader _upower;
    private readonly MemoryLayout _memoryLayout;
    private readonly ProcessStats _processStats;
    private AppResolver _appResolver;
    private string _language;

    private ProcStatReading? _previousCpu;
    private bool _ready;

    /// <summary>The last service read; attached to every fresh process list.</summary>
    private IReadOnlyList<ServiceEntry> _cachedServices = Array.Empty<ServiceEntry>();

    /// <summary>The last process read, republished between the 3 s reads.</summary>
    private IReadOnlyList<ProcessEntry> _cachedProcesses = Array.Empty<ProcessEntry>();
    private long _cachedMemoryStamp;

    /// <summary>The last grid read, republished between the 2 s reads.</summary>
    private IReadOnlyList<ProcessRow> _cachedProcessRows = ProcessRow.Empty;
    private IReadOnlyList<AppEntry> _cachedApps = Array.Empty<AppEntry>();

    public LinuxSampler(
        string root = "/",
        ICommandRunner? runner = null,
        Func<long>? clock = null,
        Diagnostics? diagnostics = null,
        string language = "en")
    {
        _root = string.IsNullOrEmpty(root) ? "/" : root;
        _clock = clock ?? (() => Environment.TickCount64);

        ICommandRunner commands = runner ?? new ProcessCommandRunner();
        PciDatabase pci = PciDatabase.Load(_root);
        _cpuInfo = CpuInfoReader.Read(_root);
        _disks = new DiskReader(_root);
        _network = new NetworkReader(_root, commands, pci, diagnostics);
        _gpus = new GpuReader(_root, commands, pci);
        _services = new ServiceReader(commands);
        _battery = new BatteryReader(_root);
        _upower = new UPowerReader(commands, diagnostics);
        _memoryLayout = ReadMemoryLayout();

        // One instance of each for the life of the sampler: the CPU and GPU
        // percentages are deltas, so a fresh reader every tick would report
        // null forever.
        _processStats = new ProcessStats(_root, _cpuInfo.Logical);
        _language = string.Equals(language, "th", StringComparison.OrdinalIgnoreCase) ? "th" : "en";
        _appResolver = new AppResolver(_root, language: _language);
    }

    public string Root => _root;

    /// <summary>
    /// The language the app names are resolved in. Changing it rebuilds only
    /// the desktop resolver, so the CPU and GPU deltas and the disk rates are
    /// not lost with it.
    /// </summary>
    public string Language
    {
        get => _language;
        set
        {
            string language = string.Equals(value, "th", StringComparison.OrdinalIgnoreCase)
                ? "th"
                : "en";
            if (string.Equals(language, _language, StringComparison.Ordinal))
            {
                return;
            }
            _language = language;
            _appResolver = new AppResolver(_root, language: language);
        }
    }

    /// <summary>
    /// One sample. Processes are read only when asked; services are read when
    /// asked and otherwise the last read is re-attached to the fresh process
    /// list, so the two tables can keep their 3 s and 10 s cadences. With
    /// <paramref name="keepMemory"/> the last process list is republished when
    /// this sample does not read a new one, so the RAM tab does not empty
    /// between reads.
    ///
    /// With <paramref name="includeGrid"/> the Apps and Processes grids are
    /// read (their own 2 s cadence); otherwise the last grid lists are
    /// republished, so the grid does not blank between reads.
    /// </summary>
    public Snapshot Sample(
        bool includeProcesses,
        bool includeServices = false,
        bool keepMemory = false,
        bool includeGrid = false)
    {
        long stamp = _clock();
        bool wasReady = _ready;

        (IReadOnlyList<Core> Cores, int Total) cpu = ReadCpu();
        Ram ram = ReadRam();
        IReadOnlyList<SwapDevice> swaps = ReadSwaps();
        CpuThermalReading thermal = ReadThermal(cpu.Cores.Count);
        IReadOnlyList<Disk> disks = ReadDisks(stamp);
        IReadOnlyList<Adapter> adapters = ReadAdapters(stamp);
        IReadOnlyList<Gpu> gpus = ReadGpus(stamp);
        PowerSupplyReading power = ReadPowerSupply();
        IReadOnlyList<PeripheralBattery> peripherals = ReadPeripherals(stamp, power);

        if (includeServices)
        {
            try
            {
                _cachedServices = _services.Read();
            }
            catch (Exception)
            {
                _cachedServices = Array.Empty<ServiceEntry>();
            }
        }

        IReadOnlyList<ProcessEntry> processes = Array.Empty<ProcessEntry>();
        IReadOnlyList<ServiceEntry> services = Array.Empty<ServiceEntry>();
        long memoryStamp = 0;
        if (includeProcesses)
        {
            try
            {
                processes = ProcessReader.Read(_root);
                _cachedProcesses = processes;
                _cachedMemoryStamp = _clock();
                memoryStamp = _cachedMemoryStamp;
                services = _cachedServices.Count > 0
                    ? MemoryBoard.Attach(_cachedServices, processes)
                    : Array.Empty<ServiceEntry>();
            }
            catch (Exception)
            {
                processes = Array.Empty<ProcessEntry>();
                services = Array.Empty<ServiceEntry>();
                memoryStamp = 0;
            }
        }
        else if (keepMemory)
        {
            processes = _cachedProcesses;
            memoryStamp = _cachedMemoryStamp;
            services = _cachedServices.Count > 0 && processes.Count > 0
                ? MemoryBoard.Attach(_cachedServices, processes)
                : Array.Empty<ServiceEntry>();
        }

        // The grid has its own 2 s cadence. A failed read keeps the previous
        // lists rather than blanking a page that was showing data.
        if (includeGrid)
        {
            try
            {
                IReadOnlyDictionary<int, string>? serviceByPid = ServicesByPid(_cachedServices);
                _cachedProcessRows = _processStats.Read(stamp, serviceByPid);
                _cachedApps = _appResolver.Read(_cachedProcessRows);
            }
            catch (Exception)
            {
                // Keep what the grid last showed.
            }
        }

        var cores = new List<Core>(cpu.Cores.Count);
        for (int i = 0; i < cpu.Cores.Count; i++)
        {
            cores.Add(new Core
            {
                Usage = cpu.Cores[i].Usage,
                Temp = i < thermal.CoreTemps.Count ? thermal.CoreTemps[i] : null,
                Mhz = cpu.Cores[i].Mhz,
            });
        }

        _ready = true;

        return new Snapshot
        {
            Cores = cores,
            Ram = ram,
            Disks = disks,
            Adapters = adapters,
            Gpus = gpus,
            Modules = Array.Empty<Module>(),
            Processes = processes,
            Services = services,
            ProcessRows = _cachedProcessRows,
            Apps = _cachedApps,
            Batteries = power.Batteries,
            PowerSources = power.PowerSources,
            Peripherals = peripherals,
            SwapDevices = swaps,
            MemoryLayout = _memoryLayout,
            CpuTotal = cpu.Total,
            CpuInfo = _cpuInfo,
            Gpu = gpus.Count > 0 ? gpus[0] : new Gpu(),
            MemorySlots = 0,
            CpuTemp = thermal.CpuTemp,
            CpuTempEstimated = thermal.Estimated,
            MemoryStamp = memoryStamp,
            Uptime = ReadUptime(),
            Ready = wasReady,
        };
    }

    /// <summary>
    /// The service units by the PID that runs them, from the last service read
    /// (no extra <c>systemctl</c> call). Only per-user units are mapped: those
    /// are the only ones the app may restart.
    /// </summary>
    private static IReadOnlyDictionary<int, string>? ServicesByPid(
        IReadOnlyList<ServiceEntry> services)
    {
        Dictionary<int, string>? map = null;
        foreach (ServiceEntry service in services)
        {
            if (!service.UserUnit || service.Pid <= 0 || service.Name.Length == 0)
            {
                continue;
            }
            map ??= new Dictionary<int, string>();
            map[service.Pid] = service.Name;
        }
        return map;
    }

    private (IReadOnlyList<Core> Cores, int Total) ReadCpu()
    {
        try
        {
            string? text = SysFs.ReadText(_root, "proc", "stat");
            if (text is null)
            {
                _previousCpu = null;
                return (Array.Empty<Core>(), 0);
            }

            ProcStatReading current = ProcStat.Parse(text);
            ProcStatReading? previous = _previousCpu;
            _previousCpu = current;

            if (previous is null || previous.Cores.Count != current.Cores.Count)
            {
                var idle = current.Cores
                    .Select((_, index) => new Core { Mhz = CpuInfoReader.CurrentMhz(_root, index) })
                    .ToList();
                return (idle, 0);
            }

            var cores = new List<Core>(current.Cores.Count);
            for (int i = 0; i < current.Cores.Count; i++)
            {
                cores.Add(new Core
                {
                    Usage = ProcStat.Usage(previous.Cores[i], current.Cores[i]),
                    Mhz = CpuInfoReader.CurrentMhz(_root, i),
                });
            }

            return (cores, ProcStat.Usage(previous.Total, current.Total));
        }
        catch (Exception)
        {
            _previousCpu = null;
            return (Array.Empty<Core>(), 0);
        }
    }

    /// <summary>
    /// Time since boot from <c>/proc/uptime</c>, whose first field is seconds
    /// with two decimals.  Null when the file is missing or unreadable.
    /// </summary>
    private TimeSpan? ReadUptime()
    {
        try
        {
            string? text = SysFs.ReadText(_root, "proc", "uptime");
            if (text is null)
            {
                return null;
            }

            string[] fields = SysFs.Fields(text);
            return fields.Length > 0
                && double.TryParse(fields[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double seconds)
                && seconds >= 0
                ? TimeSpan.FromSeconds(seconds)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Ram ReadRam()
    {
        try
        {
            string? text = SysFs.ReadText(_root, "proc", "meminfo");
            return text is null ? new Ram() : MemInfo.Parse(text);
        }
        catch (Exception)
        {
            return new Ram();
        }
    }

    /// <summary>Swap devices are one small file, read every sample.</summary>
    private IReadOnlyList<SwapDevice> ReadSwaps()
    {
        try
        {
            return SwapsReader.Parse(SysFs.ReadText(_root, "proc", "swaps"));
        }
        catch (Exception)
        {
            return Array.Empty<SwapDevice>();
        }
    }

    /// <summary>
    /// The board's memory layout does not change while the machine runs, so it
    /// is read once with the sampler and carried on every snapshot.
    /// </summary>
    private MemoryLayout ReadMemoryLayout()
    {
        try
        {
            return MemoryModules.Read(_root);
        }
        catch (Exception)
        {
            return MemoryLayout.Empty;
        }
    }

    private CpuThermalReading ReadThermal(int logicalCount)
    {
        try
        {
            int count = logicalCount > 0 ? logicalCount : _cpuInfo.Logical;
            return ThermalReader.Read(_root, count);
        }
        catch (Exception)
        {
            return new CpuThermalReading
            {
                CoreTemps = new int?[Math.Max(0, logicalCount)],
            };
        }
    }

    private IReadOnlyList<Disk> ReadDisks(long stamp)
    {
        try
        {
            string? text = SysFs.ReadText(_root, "proc", "self", "mountinfo");
            if (text is null)
            {
                return Array.Empty<Disk>();
            }

            return _disks.Read(MountInfo.Real(MountInfo.Parse(text)), stamp);
        }
        catch (Exception)
        {
            return Array.Empty<Disk>();
        }
    }

    private IReadOnlyList<Adapter> ReadAdapters(long stamp)
    {
        try
        {
            return _network.Read(stamp);
        }
        catch (Exception)
        {
            return Array.Empty<Adapter>();
        }
    }

    private IReadOnlyList<Gpu> ReadGpus(long stamp)
    {
        try
        {
            return _gpus.Read(stamp);
        }
        catch (Exception)
        {
            return Array.Empty<Gpu>();
        }
    }

    /// <summary>
    /// Batteries and adapters are a handful of tiny sysfs reads, so they are
    /// always sampled; there is no setting that makes this pass expensive.
    /// </summary>
    private PowerSupplyReading ReadPowerSupply()
    {
        try
        {
            return _battery.Read();
        }
        catch (Exception)
        {
            return PowerSupplyReading.Empty;
        }
    }

    /// <summary>Peripherals come from UPower at most every 30 s, cached in between.</summary>
    private IReadOnlyList<PeripheralBattery> ReadPeripherals(long stamp, PowerSupplyReading power)
    {
        try
        {
            return _upower.Read(stamp, power);
        }
        catch (Exception)
        {
            return Array.Empty<PeripheralBattery>();
        }
    }
}
