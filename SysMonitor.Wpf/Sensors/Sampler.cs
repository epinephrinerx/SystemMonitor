using SysMonitor.Model;
using SysMonitor.Native;

namespace SysMonitor.Sensors;

/// <summary>
/// Reads the machine's counters on a background task and publishes whole
/// snapshots for the UI thread to pick up.
///
/// Cheap counters (CPU, RAM, disk I/O) are read every tick; expensive or
/// slow-moving ones (free space, temperatures, drive identity) get their own
/// longer cadences.  That split is where most of the CPU saving comes from.
///
/// The Python build had to push this into a separate *process*, because a
/// second thread in the same process was the leading suspect for a Tcl
/// allocator panic.  .NET has no such hazard: one background task is enough,
/// and it costs no extra process.
/// </summary>
public sealed class Sampler : IDisposable
{
    private const double Gb = 1024.0 * 1024 * 1024;

    /// <summary>
    /// A probe slower than this marks the device sluggish and shelves it.
    /// This is backoff, not a timeout: a stuck cloud mount is skipped on
    /// later ticks rather than being allowed to stall every future sample.
    /// </summary>
    private static readonly TimeSpan SlowProbe = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SlowBackoff = TimeSpan.FromMinutes(5);

    private readonly AppConfig _config;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private Task? _loop;

    private Snapshot _snapshot = new();

    private (long Idle, long Kernel, long User)[]? _prevCpu;
    private readonly Dictionary<string, ((long Read, long Written) Counters, long Stamp)> _prevIo = new();
    private readonly Dictionary<string, (string Media, string Bus, int? Number, string Label)> _hardware = new();
    private readonly Dictionary<string, (ulong Used, ulong Total)> _space = new();
    private readonly Dictionary<string, int?> _temps = new();
    private readonly Dictionary<string, long> _slowUntil = new();
    private List<string> _drives = new();

    private long _nextSpace;
    private long _nextTemp;
    private long _nextDrives;

    public Sampler(AppConfig config) => _config = config;

    /// <summary>The most recent complete reading.  Safe to call from any thread.</summary>
    public Snapshot Current => Volatile.Read(ref _snapshot);

    public void Start()
    {
        Win32.QuietErrorDialogs();
        _prevCpu = Win32.CpuTimes();
        _loop = Task.Run(RunAsync);
    }

    /// <summary>Apply a changed refresh rate without waiting out the current sleep.</summary>
    public void Nudge()
    {
        if (_wake.CurrentCount == 0)
        {
            try
            {
                _wake.Release();
            }
            catch (SemaphoreFullException)
            {
                // Another nudge won the race; one wake-up is all we need.
            }
        }
    }

    public void Stop()
    {
        if (!_stop.IsCancellationRequested)
        {
            _stop.Cancel();
        }
        Nudge();
    }

    public void Dispose()
    {
        Stop();
        _stop.Dispose();
        _wake.Dispose();
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                Collect();
            }
            catch (Exception error)
            {
                // A widget must never die because a counter misbehaved.
                Diag.ReportException("Sampler", error);
            }

            try
            {
                await _wake.WaitAsync(_config.Intervals.Metrics, _stop.Token)
                           .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // ------------------------------------------------------------ slow guard
    /// <summary>
    /// Run a probe and shelve the device for a while if it drags.  Returns
    /// false when the probe was skipped or threw, so callers keep their last
    /// good value instead of publishing a zero.
    /// </summary>
    private bool Guard<T>(string key, Func<T> probe, out T? result)
    {
        result = default;
        long now = Environment.TickCount64;
        if (_slowUntil.TryGetValue(key, out long until) && now < until)
        {
            return false;
        }

        long started = Environment.TickCount64;
        try
        {
            result = probe();
        }
        catch (Exception error)
        {
            Diag.ReportException("Probe " + key, error);
            _slowUntil[key] = Environment.TickCount64 + (long)SlowBackoff.TotalMilliseconds;
            return false;
        }

        if (Environment.TickCount64 - started > SlowProbe.TotalMilliseconds)
        {
            _slowUntil[key] = Environment.TickCount64 + (long)SlowBackoff.TotalMilliseconds;
            Diag.Write($"Slow probe {key}; backing off for {SlowBackoff.TotalSeconds:F0}s");
        }
        return true;
    }

    // --------------------------------------------------------------- collect
    private void Collect()
    {
        long now = Environment.TickCount64;

        if (now >= _nextDrives)
        {
            _nextDrives = now + 60_000;
            RefreshDriveList();
        }
        if (now >= _nextSpace)
        {
            _nextSpace = now + (long)_config.Intervals.Space.TotalMilliseconds;
            RefreshSpace();
        }
        if (now >= _nextTemp)
        {
            _nextTemp = now + (long)_config.Intervals.Temps.TotalMilliseconds;
            RefreshTemperatures();
        }

        (IReadOnlyList<Core> cores, int total) = CollectCpu();
        int? cpuTemp = null;
        bool estimated = false;
        if (_config.TempEstimate)
        {
            cpuTemp = EstimateCpuTemp(total);
            estimated = true;
            cores = cores.Select(c => new Core
            {
                Usage = c.Usage,
                Temp = EstimateCpuTemp(c.Usage),
                Estimated = true,
            }).ToList();
        }

        Volatile.Write(ref _snapshot, new Snapshot
        {
            Cores = cores,
            CpuTotal = total,
            CpuTemp = cpuTemp,
            CpuTempEstimated = estimated,
            Ram = CollectRam(),
            Disks = CollectDisks(),
            Ready = true,
        });
    }

    /// <summary>
    /// The model the Electron builds used.  Kept so the alert thresholds have
    /// something to act on, but the UI flags it with '~' as an estimate.
    /// </summary>
    private static int EstimateCpuTemp(int usage) => (int)(40 + usage * 0.4);

    // ------------------------------------------------------------------- cpu
    private (IReadOnlyList<Core> Cores, int Total) CollectCpu()
    {
        var times = Win32.CpuTimes();
        if (times is null)
        {
            return (Array.Empty<Core>(), 0);
        }
        if (_prevCpu is null || _prevCpu.Length != times.Length)
        {
            _prevCpu = times;
            return (Array.Empty<Core>(), 0);
        }

        var cores = new List<Core>(times.Length);
        for (int i = 0; i < times.Length; i++)
        {
            long idle = times[i].Idle - _prevCpu[i].Idle;
            // KernelTime already includes IdleTime, so busy = total - idle.
            long total = (times[i].Kernel - _prevCpu[i].Kernel)
                       + (times[i].User - _prevCpu[i].User);
            int usage = total <= 0 ? 0 : (int)Math.Round(100.0 * (total - idle) / total);
            cores.Add(new Core { Usage = Math.Clamp(usage, 0, 100) });
        }
        _prevCpu = times;

        int average = cores.Count == 0 ? 0
            : (int)Math.Round(cores.Average(c => c.Usage));
        return (cores, average);
    }

    // ------------------------------------------------------------------- ram
    private static Ram CollectRam()
    {
        var memory = Win32.MemoryStatus();
        if (memory is null)
        {
            return new Ram();
        }
        (ulong used, ulong total) = memory.Value;
        return new Ram
        {
            Usage = (int)Math.Round(100.0 * used / total),
            UsedGb = used / Gb,
            TotalGb = total / Gb,
        };
    }

    // ----------------------------------------------------------------- disks
    private void RefreshDriveList()
    {
        if (!Guard("enum", () => Win32.LogicalDrives(_config.IncludeRemovable),
                   out List<string>? letters) || letters is null)
        {
            return;
        }
        _drives = letters;

        foreach (string letter in letters)
        {
            if (_hardware.ContainsKey(letter))
            {
                continue;
            }
            if (!Guard("num:" + letter, () => Win32.PhysicalDriveNumber(letter),
                       out int? number))
            {
                continue;
            }

            (string Media, string Bus)? hardware = null;
            if (number is not null)
            {
                Guard($"hw:{number}", () => Win32.DriveHardware(number.Value), out hardware);
            }
            Guard("label:" + letter, () => Win32.VolumeLabel(letter), out string? label);

            _hardware[letter] = (hardware?.Media ?? "Disk", hardware?.Bus ?? "Unknown",
                                 number, label ?? string.Empty);
        }

        // A drive that went away must not leave stale readings behind.
        Forget(_hardware, letters);
        Forget(_prevIo, letters);
        Forget(_space, letters);
        Forget(_temps, letters);
    }

    private static void Forget<T>(Dictionary<string, T> cache, List<string> keep)
    {
        foreach (string stale in cache.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            cache.Remove(stale);
        }
    }

    private void RefreshSpace()
    {
        foreach (string letter in _drives)
        {
            if (Guard("space:" + letter, () => Win32.DiskSpace(letter),
                      out (ulong Used, ulong Total)? space)
                && space is not null)
            {
                _space[letter] = space.Value;
            }
        }
    }

    private void RefreshTemperatures()
    {
        var seen = new Dictionary<int, int?>();
        foreach (string letter in _drives)
        {
            int? number = _hardware.TryGetValue(letter, out var hardware) ? hardware.Number : null;
            if (number is null)
            {
                continue;
            }
            if (!seen.ContainsKey(number.Value))
            {
                seen[number.Value] =
                    Guard($"temp:{number}", () => Win32.DriveTemperature(number.Value),
                          out int? value) ? value : null;
            }
            _temps[letter] = seen[number.Value];
        }
    }

    private IReadOnlyList<Disk> CollectDisks()
    {
        var disks = new List<Disk>(_drives.Count);
        foreach (string letter in _drives)
        {
            _hardware.TryGetValue(letter, out var hardware);
            double usedGb = 0, totalGb = 0;
            int usage = 0;
            if (_space.TryGetValue(letter, out (ulong Used, ulong Total) space))
            {
                usedGb = space.Used / Gb;
                totalGb = space.Total / Gb;
                usage = (int)Math.Round(100.0 * space.Used / space.Total);
            }

            double readMb = 0, writeMb = 0;
            // This one runs every tick on every drive, so it is guarded too.
            if (Guard("io:" + letter, () => Win32.VolumeIoCounters(letter),
                      out (long Read, long Written)? counters)
                && counters is not null)
            {
                long stamp = Environment.TickCount64;
                if (_prevIo.TryGetValue(letter, out var previous))
                {
                    // The baseline belongs to this drive's last successful read.
                    // Dividing by a global tick would report a skipped interval's
                    // accumulated bytes as a huge instantaneous rate.
                    double elapsed = (stamp - previous.Stamp) / 1000.0;
                    if (elapsed > 0)
                    {
                        readMb = Math.Max(0, counters.Value.Read - previous.Counters.Read)
                                 / elapsed / (1024 * 1024);
                        writeMb = Math.Max(0, counters.Value.Written - previous.Counters.Written)
                                  / elapsed / (1024 * 1024);
                    }
                }
                _prevIo[letter] = (counters.Value, stamp);
            }

            disks.Add(new Disk
            {
                Letter = letter,
                Label = hardware.Label ?? string.Empty,
                Media = hardware.Media ?? "Disk",
                Bus = hardware.Bus ?? "Unknown",
                Usage = usage,
                UsedGb = usedGb,
                TotalGb = totalGb,
                ReadMb = readMb,
                WriteMb = writeMb,
                Temp = _temps.TryGetValue(letter, out int? temp) ? temp : null,
            });
        }
        return disks;
    }
}
