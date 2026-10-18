using System.IO;
using System.Runtime.InteropServices;
using SysMonitor.Extensibility;

namespace SysMonitor.Ext.Smart;

/// <summary>
/// Immutable snapshot of one scan result and associated temperature readings.
/// </summary>
internal sealed record ScanState(SmartInfo Info, IReadOnlyDictionary<int, int> Temps);

/// <summary>
/// First-party extension providing S.M.A.R.T. disk health views and disk temperature readings
/// via DiskInfoCore.dll.
/// </summary>
public sealed class SmartExtension : IExtension, IViewProvider, IDemandAware, IDiskTemperatureSource, IDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(2);
    private static readonly IReadOnlyDictionary<int, int> EmptyTemps = new Dictionary<int, int>();

    private readonly ISmartScanner _scanner;
    private readonly Func<long>? _clock;
    private readonly bool? _elevatedOverride;
    private readonly Func<string, bool>? _pinLibrary;
    private readonly Action? _onLoopPass;
    private readonly Action<ExtensionDemand, long>? _onPassProcessed;

    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly AutoResetEvent _wake = new(false);
    private Thread? _scanThread;
    private IExtensionContext? _context;
    private bool _elevated;
    private ExtensionDemand _demand = ExtensionDemand.None;
    private volatile bool _rescanRequested;
    // Bumped (under _lock) whenever SetDemand discards the state, so a scan that began before the
    // discard can tell that its result is stale and must not be published over it.
    private int _generation;

    private ScanState _state = new(SmartInfo.Waiting, EmptyTemps);
    private bool _disposed;

    /// <summary>
    /// Public parameterless constructor required by extension loader.
    /// </summary>
    public SmartExtension() : this(RealSmartScanner.Instance, null, null, null)
    {
    }

    /// <summary>
    /// Internal constructor for unit testing with custom scanner, clock, elevation, or library pin seam.
    /// </summary>
    internal SmartExtension(
        ISmartScanner scanner,
        Func<long>? clock = null,
        bool? elevatedOverride = null,
        Func<string, bool>? pinLibrary = null,
        Action? onLoopPass = null,
        Action<ExtensionDemand, long>? onPassProcessed = null)
    {
        _onPassProcessed = onPassProcessed;
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _clock = clock;
        _elevatedOverride = elevatedOverride;
        _pinLibrary = pinLibrary;
        _onLoopPass = onLoopPass;
    }

    public void Initialize(IExtensionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _elevated = _elevatedOverride ?? context.HostIsElevated;

        if (_elevated)
        {
            _scanThread = new Thread(ScanLoop)
            {
                IsBackground = true,
                Name = "smart-scan"
            };
            if (OperatingSystem.IsWindows())
            {
                _scanThread.SetApartmentState(ApartmentState.STA);
            }
            _scanThread.Start();
        }
        else
        {
            // When not elevated, build the NeedAdmin state immediately so views show the correct message
            SmartInfo adminInfo = SmartBoard.Build(_scanner.IsAvailable, _elevated, SmartScanStatus.NeedAdmin,
                new Dictionary<int, SmartReading>());
            Volatile.Write(ref _state, new ScanState(adminInfo, EmptyTemps));
        }
    }

    public string GetDisplayName(string language) => SmartStrings.Get("ext_name", language);

    public ExtensionView GetView(string language)
    {
        ScanState current = Volatile.Read(ref _state);
        return SmartViewBuilder.Build(current.Info, language);
    }

    public void SetDemand(ExtensionDemand demand)
    {
        lock (_lock)
        {
            ExtensionDemand oldDemand = _demand;
            _demand = demand;

            bool viewsTurnedOn = !oldDemand.HasFlag(ExtensionDemand.Views) && demand.HasFlag(ExtensionDemand.Views);
            if (viewsTurnedOn)
            {
                // Not elevated there is no scan thread: the NeedAdmin/Unavailable state set by
                // Initialize is final, and a "waiting" page would never be replaced.
                if (_elevated)
                {
                    _generation++;
                    Volatile.Write(ref _state, new ScanState(SmartInfo.Waiting, EmptyTemps));
                }
                _rescanRequested = true;
            }
        }

        try
        {
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public IReadOnlyDictionary<int, int> GetDiskTemperatures()
    {
        ScanState current = Volatile.Read(ref _state);
        return current.Temps;
    }

    private void ScanLoop()
    {
        try
        {
            SmartScanStatus last = SmartScanStatus.Ok;
            long next = 0;

            var waitHandles = new WaitHandle[] { _cts.Token.WaitHandle, _wake };

            while (!_cts.IsCancellationRequested)
            {
                bool hasDemand;
                int generation;
                ExtensionDemand demandSeen;
                lock (_lock)
                {
                    // Consumed together with the demand and generation it belongs to: SetDemand sets them
                    // under the same lock, so a pass never sees the new demand without its rescan request.
                    if (_rescanRequested)
                    {
                        _rescanRequested = false;
                        next = 0;
                    }

                    demandSeen = _demand;
                    hasDemand = demandSeen != ExtensionDemand.None;
                    generation = _generation;
                }

                long now = _clock != null ? _clock() : Environment.TickCount64;
                bool skipWait = false;

                if (hasDemand && now >= next)
                {
                    next = now + (long)ScanInterval.TotalMilliseconds;
                    try
                    {
                        var (status, disks) = _scanner.Scan();

                        var readingDict = (status == SmartScanStatus.Ok && disks != null)
                            ? disks.GroupBy(d => d.Drive).ToDictionary(g => g.Key, g => g.First())
                            : new Dictionary<int, SmartReading>();

                        SmartInfo info = SmartBoard.Build(_scanner.IsAvailable, _elevated, status, readingDict);

                        var tempDict = new Dictionary<int, int>();
                        if (status == SmartScanStatus.Ok && disks != null)
                        {
                            foreach (var disk in disks)
                            {
                                if (disk.TempC is > 0 and < 150)
                                {
                                    tempDict[disk.Drive] = disk.TempC.Value;
                                }
                            }
                        }

                        if (PublishIfCurrent(generation, new ScanState(info, tempDict)))
                        {
                            if (status != last)
                            {
                                _context?.Log($"SMART scan: {status} ({disks?.Count ?? 0} disks)");
                                last = status;
                            }
                        }
                        else
                        {
                            // Stale: the state was discarded while this scan ran. Scan again at once.
                            next = 0;
                            skipWait = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        SmartInfo failInfo = SmartBoard.Build(_scanner.IsAvailable, _elevated, SmartScanStatus.Failed,
                            new Dictionary<int, SmartReading>());
                        if (PublishIfCurrent(generation, new ScanState(failInfo, EmptyTemps)))
                        {
                            _context?.Log($"SMART scan failed: {ex.Message}");
                        }
                        else
                        {
                            next = 0;
                            skipWait = true;
                        }
                    }
                }

                // Tells a test which demand and clock value this pass decided on, before the loop sleeps.
                _onPassProcessed?.Invoke(demandSeen, now);

                if (!skipWait)
                {
                    WaitHandle.WaitAny(waitHandles, 3000);
                }
                _onLoopPass?.Invoke();
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    // The check and the write share _lock with SetDemand, so a discard can never be overwritten by a scan
    // that started before it.
    private bool PublishIfCurrent(int generation, ScanState state)
    {
        lock (_lock)
        {
            if (generation != _generation)
            {
                return false;
            }
            Volatile.Write(ref _state, state);
            return true;
        }
    }

    public void Shutdown()
    {
        Dispose();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }

        try
        {
            _cts.Cancel();
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
        }

        if (_scanThread != null && _scanThread.IsAlive)
        {
            bool joined = false;
            try
            {
                joined = _scanThread.Join(TimeSpan.FromSeconds(4));
            }
            catch
            {
            }

            if (!joined)
            {
                PinLibrary();
            }
        }

        try
        {
            _wake.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        _cts.Dispose();
    }

    private void PinLibrary()
    {
        try
        {
            string extDir = _context?.ExtensionDirectory ?? string.Empty;
            string dllPath = Path.Combine(extDir, "DiskInfoCore.dll");

            bool pinned;
            if (_pinLibrary != null)
            {
                pinned = _pinLibrary(dllPath);
            }
            else
            {
                pinned = NativeLibrary.TryLoad(dllPath, out _);
            }

            _context?.Log($"SMART scan join timed out; library pinned ({pinned}): {dllPath}");
        }
        catch (Exception ex)
        {
            _context?.Log($"SMART pin library failed: {ex.Message}");
        }
    }
}
