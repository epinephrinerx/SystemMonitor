using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace SysMonitor;

/// <summary>
/// Flight recorder for a widget nobody is watching: a start banner, a line
/// every ten minutes, and every caught exception with its stack.
///
/// The Tk build needed this because a native allocator panic left no Python
/// traceback at all.  Keep it: an unexplained disappearance still has to leave
/// evidence behind.
/// </summary>
public static class Diag
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateTime> LastReport = new();
    private static bool _enabled = true;

    public static string Path =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SysMonitor", "sysmonitor-wpf.log");

    public static void SetEnabled(bool enabled) => _enabled = enabled;

    public static void Write(string line)
    {
        if (!_enabled)
        {
            return;
        }
        lock (Gate)
        {
            try
            {
                string path = Path;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    // Keep the tail: the interesting part is always the end.
                    string[] kept = File.ReadAllLines(path);
                    File.WriteAllLines(path, kept.Skip(kept.Length / 2));
                }
                // Invariant culture on purpose: under a Thai locale the default
                // formatter writes Buddhist-era years, which makes the log
                // impossible to line up with anything else on the machine.
                File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    + "  " + line + Environment.NewLine);
            }
            catch (Exception)
            {
                // Diagnostics must never be the thing that takes the app down.
            }
        }
    }

    public static void Start(bool enabled)
    {
        SetEnabled(enabled);
        var process = Process.GetCurrentProcess();
        Write($"--- start v{typeof(Diag).Assembly.GetName().Version} " +
              $"clr {Environment.Version} wpf " +
              $"rss={process.WorkingSet64 / 1024.0 / 1024.0:F1}MB");
        Write($"process pid={process.Id} exe={Environment.ProcessPath}");
    }

    public static void Heartbeat(string mode, string detail)
    {
        double rss = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;
        Write($"alive  mode={mode,-8} rss={rss,6:F1}MB {detail}");
    }

    /// <summary>
    /// Log a caught exception, at most once a minute per context, so a probe
    /// that fails every tick cannot fill the log.
    /// </summary>
    public static void ReportException(string context, Exception error)
    {
        lock (Gate)
        {
            if (LastReport.TryGetValue(context, out DateTime last)
                && DateTime.UtcNow - last < TimeSpan.FromMinutes(1))
            {
                return;
            }
            LastReport[context] = DateTime.UtcNow;
        }
        Write($"!! {context}: {error.GetType().Name}: {error.Message}");
        Write(error.StackTrace ?? "(no stack)");
    }
}
