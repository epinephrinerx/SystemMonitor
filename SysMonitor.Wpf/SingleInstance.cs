namespace SysMonitor;

/// <summary>
/// One System Monitor per Windows session.
///
/// Logon can start the app twice (a stale Run entry, a restored session), and
/// two copies would each sample the hardware and draw their own window. The
/// second one waits briefly -- <c>Restart()</c> starts the new copy before the
/// old one has finished closing -- and, if the first still holds on, steps
/// aside instead of killing it.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>Per-session, so a second signed-in user is not blocked.</summary>
    public const string DefaultName = @"Local\SystemMonitor.SingleInstance";

    private Mutex? _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// Take the lock, waiting up to <paramref name="wait"/> for a previous
    /// owner to let go. Null means another copy is running.
    /// </summary>
    public static SingleInstance? TryAcquire(string name, TimeSpan wait)
    {
        var mutex = new Mutex(initiallyOwned: false, name);
        bool owned;
        try
        {
            owned = mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died without releasing it. The lock is ours.
            owned = true;
        }

        if (!owned)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex);
    }

    /// <summary>Must run on the thread that acquired it.</summary>
    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from another thread: the process is going away anyway.
        }
        _mutex.Dispose();
        _mutex = null;
    }
}
