using SysMonitor.Extensibility;

namespace SysMonitor.Extensions;

internal sealed class DemandGate
{
    public readonly object Lock = new();
    public volatile bool Dirty;
}

/// <summary>
/// Lifetime gate for a loaded extension instance.
/// Tracks concurrent in-flight usage and ensures safe teardown after retirement.
/// </summary>
internal sealed class ExtensionSlot
{
    private readonly LoadedExtension _extension;
    private readonly Action<string>? _log;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _zeroUsers = new(false);
    private int _userCount;
    private bool _retired;
    private bool _disposed;
    private volatile bool _demandApplied;

    /// <summary>
    /// Serialises demand delivery to this instance. It belongs to the slot, not the id, so a callback
    /// that blocks on a retired instance can never keep its replacement from receiving its demand.
    /// </summary>
    public DemandGate DemandGate { get; } = new();

    public LoadedExtension Extension => _extension;

    /// <summary>
    /// True if demand has been applied to this slot or the instance does not implement <see cref="IDemandAware"/>.
    /// </summary>
    public bool DemandApplied
    {
        get => _demandApplied;
        set => _demandApplied = value;
    }

    public ExtensionSlot(LoadedExtension extension, Action<string>? log)
    {
        _extension = extension ?? throw new ArgumentNullException(nameof(extension));
        _log = log;
        _demandApplied = extension.Instance is not IDemandAware;
    }

    /// <summary>
    /// Attempts to increment the in-flight user count if the slot has not been retired.
    /// Returns true if successfully acquired; otherwise false.
    /// </summary>
    public bool TryAcquire()
    {
        lock (_gate)
        {
            if (_retired)
            {
                return false;
            }
            _userCount++;
            return true;
        }
    }

    /// <summary>
    /// Decrements the in-flight user count. If retired and count reaches zero, signals waiting teardown.
    /// </summary>
    public void Release()
    {
        lock (_gate)
        {
            _userCount--;
            if (_retired && _userCount == 0 && !_disposed)
            {
                _zeroUsers.Set();
            }
        }
    }

    /// <summary>
    /// Marks the slot as retired, waits up to <paramref name="wait"/> for all active callers to exit,
    /// and invokes Dispose on the underlying LoadedExtension.
    /// </summary>
    public void RetireAndDispose(TimeSpan wait)
    {
        bool needsWait;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _retired = true;
            needsWait = _userCount > 0;
            if (!needsWait)
            {
                _zeroUsers.Set();
            }
        }

        if (needsWait)
        {
            if (!_zeroUsers.Wait(wait))
            {
                _log.SafeLog($"Extension '{_extension.Manifest.Id}' in-flight calls did not complete within {wait.TotalSeconds}s; disposing anyway.");
            }
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }

        try
        {
            _extension.Dispose();
        }
        catch (Exception ex)
        {
            _log.SafeLog($"Error disposing extension '{_extension.Manifest.Id}': {ex.Describe()}");
        }
    }
}
