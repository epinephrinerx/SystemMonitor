using System.Globalization;
using System.Text;
using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The small text log from spec section 9. The Windows build trims the working
/// set on each heartbeat; that part has no Linux meaning and is left out.
///
/// Path: <c>$XDG_STATE_HOME/SystemMonitor/sysmonitor.log</c>, defaulting to
/// <c>~/.local/state/SystemMonitor/sysmonitor.log</c>. Writing is best effort:
/// a read-only home directory must not stop the widget.
/// </summary>
public sealed class Diagnostics
{
    private const long MaxBytes = 256 * 1024;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _gate = new();

    public Diagnostics(string? path = null, bool enabled = true)
    {
        Path = path ?? DefaultPath;
        Enabled = enabled;
    }

    public string Path { get; }
    public bool Enabled { get; set; }

    public static string StateDirectory
    {
        get
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            if (!string.IsNullOrWhiteSpace(xdg))
            {
                return System.IO.Path.Combine(xdg, "SystemMonitor");
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetEnvironmentVariable("HOME") ?? ".";
            }
            return System.IO.Path.Combine(home, ".local", "state", "SystemMonitor");
        }
    }

    public static string DefaultPath => System.IO.Path.Combine(StateDirectory, "sysmonitor.log");

    /// <summary>One timestamped line. The timestamp is invariant, never Buddhist era.</summary>
    public void Line(string message)
    {
        if (!Enabled)
        {
            return;
        }

        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        lock (_gate)
        {
            try
            {
                string? directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                Trim();
                File.AppendAllText(Path, stamp + "  " + message + Environment.NewLine, Utf8);
            }
            catch (Exception)
            {
                // Diagnostics must never be the reason the app fails.
            }
        }
    }

    /// <summary>The ten-minute heartbeat: what the app is showing, in one line.</summary>
    public void Heartbeat(Snapshot? snapshot, string view)
    {
        if (snapshot is null || !snapshot.Ready)
        {
            Line($"heartbeat view={view} waiting for the first sample");
            return;
        }

        var text = new StringBuilder();
        text.Append("heartbeat view=").Append(view);
        text.Append(" cpu=").Append(snapshot.CpuTotal.ToString(CultureInfo.InvariantCulture)).Append('%');
        text.Append(" ram=").Append(snapshot.Ram.Usage.ToString(CultureInfo.InvariantCulture)).Append('%');
        if (snapshot.CpuTemp is { } temp)
        {
            text.Append(" cpu_temp=").Append(temp.ToString(CultureInfo.InvariantCulture));
            if (snapshot.CpuTempEstimated)
            {
                text.Append('~');
            }
        }
        text.Append(" disks=").Append(snapshot.Disks.Count.ToString(CultureInfo.InvariantCulture));
        text.Append(" adapters=").Append(snapshot.Adapters.Count.ToString(CultureInfo.InvariantCulture));
        Line(text.ToString());
    }

    /// <summary>Keep the file bounded: over 256 KiB, keep the newer half.</summary>
    private void Trim()
    {
        var info = new FileInfo(Path);
        if (!info.Exists || info.Length <= MaxBytes)
        {
            return;
        }

        string[] lines = File.ReadAllLines(Path);
        int keep = Math.Max(1, lines.Length / 2);
        File.WriteAllLines(Path, lines.Skip(lines.Length - keep), Utf8);
    }
}
