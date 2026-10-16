using SysMonitor.Extensibility;

namespace SysMonitor.Extensions;

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

    public LoadedExtension Extension => _extension;

    public ExtensionSlot(LoadedExtension extension, Action<string>? log)
    {
        _extension = extension ?? throw new ArgumentNullException(nameof(extension));
        _log = log;
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
