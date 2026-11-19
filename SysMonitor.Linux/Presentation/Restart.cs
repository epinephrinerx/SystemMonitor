using System.Diagnostics;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The process a restart starts (Amendment 2, item 6): the same executable,
/// the same arguments, no shell. It is a pure function so a test can inspect
/// every field without starting anything, and <see cref="ProcessRestarter"/>
/// is the only caller that turns it into a process.
/// </summary>
public static class Restart
{
    public static ProcessStartInfo BuildStartInfo(
        string processPath, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }
        return info;
    }
}

/// <summary>
/// Restart from spec section 9: start a second copy of this executable, with
/// the same arguments, and let the caller shut the first one down. The single
/// instance lock in <see cref="SingleInstanceLock"/> waits up to five seconds,
/// which is how the new copy takes over from the old one.
///
/// The arguments reach the new process one at a time
/// (<see cref="ProcessStartInfo.ArgumentList"/>); no shell is involved, so a
/// path with spaces or quotes cannot become two arguments.
/// </summary>
public sealed class ProcessRestarter : IRestarter
{
    private readonly string _executable;
    private readonly IReadOnlyList<string> _arguments;
    private readonly Func<string, IReadOnlyList<string>, bool> _start;

    public ProcessRestarter(
        string? executable = null,
        IReadOnlyList<string>? arguments = null,
        Func<string, IReadOnlyList<string>, bool>? start = null)
    {
        _executable = string.IsNullOrWhiteSpace(executable)
            ? Environment.ProcessPath ?? "sysmonitor"
            : executable!;
        _arguments = arguments?.ToArray() ?? Array.Empty<string>();
        _start = start ?? Start;
    }

    public string Executable => _executable;

    public IReadOnlyList<string> Arguments => _arguments;

    public bool Restart() => _start(_executable, _arguments);

    /// <summary>The process may outlive us, so its handle is not kept.</summary>
    private static bool Start(string file, IReadOnlyList<string> arguments)
    {
        try
        {
            using Process? process =
                Process.Start(global::SysMonitor.Linux.Presentation.Restart.BuildStartInfo(file, arguments));
            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
