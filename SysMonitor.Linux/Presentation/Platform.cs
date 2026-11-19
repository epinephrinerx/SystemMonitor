namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The single-instance guard (spec section 9, Linux decision): an exclusive
/// lock on a file under the runtime directory, waiting up to five seconds so a
/// restart can hand over.
/// </summary>
public sealed class SingleInstanceLock : IDisposable
{
    public const string LockFileName = "sysmonitor.lock";

    private FileStream? _stream;

    private SingleInstanceLock(FileStream? stream)
    {
        _stream = stream;
    }

    public bool Acquired => _stream is not null;

    /// <summary><c>$XDG_RUNTIME_DIR/sysmonitor.lock</c>, falling back to the config directory.</summary>
    public static string DefaultPath
    {
        get
        {
            string? runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            string directory = !string.IsNullOrWhiteSpace(runtime)
                ? runtime
                : AppConfig.ConfigDirectory;
            return Path.Combine(directory, LockFileName);
        }
    }

    /// <summary>
    /// Try to take the lock. The wait is spent in short sleeps so a restart
    /// that is still shutting down can release it first.
    /// </summary>
    public static SingleInstanceLock Acquire(TimeSpan wait, string? path = null)
    {
        string file = path ?? DefaultPath;
        string? directory = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception)
            {
                return new SingleInstanceLock(null);
            }
        }

        DateTime deadline = DateTime.UtcNow + wait;
        do
        {
            try
            {
                var stream = new FileStream(
                    file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new SingleInstanceLock(stream);
            }
            catch (IOException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return new SingleInstanceLock(null);
                }
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                return new SingleInstanceLock(null);
            }
        }
        while (DateTime.UtcNow < deadline);

        return new SingleInstanceLock(null);
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}

/// <summary>
/// Finds the disk tools the spec replaces with Linux equivalents: the first
/// executable on PATH wins, and no candidate means the menu item is hidden.
/// </summary>
public static class ToolFinder
{
    /// <summary>Scan PATH for the first of <paramref name="names"/> that exists.</summary>
    public static string? FirstOnPath(IEnumerable<string> names, string? path = null)
    {
        string searchPath = path ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string[] directories = searchPath.Split(
            Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string name in names)
        {
            foreach (string directory in directories)
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    /// <summary>filelight or baobab: what "Disk usage" opens.</summary>
    public static string? DiskUsageTool(string? path = null) =>
        FirstOnPath(new[] { "filelight", "baobab" }, path);

    /// <summary>partitionmanager or gnome-disks: what "Disks" opens.</summary>
    public static string? DiskManagerTool(string? path = null) =>
        FirstOnPath(new[] { "partitionmanager", "gnome-disks" }, path);
}
