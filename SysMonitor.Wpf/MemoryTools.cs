using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using SysMonitor.Model;
using SysMonitor.Native;

namespace SysMonitor;

/// <summary>What happened to a kill, stop, start or startup change.</summary>
internal readonly record struct OpResult(bool Ok, bool Cancelled, string Message)
{
    public static OpResult Success => new(true, false, string.Empty);
    public static OpResult UserCancelled => new(false, true, string.Empty);
    public static OpResult Failure(string message) => new(false, false, message);
}

internal enum ServiceOp
{
    Stop,
    Start,
    Restart,
    SetStart,
}

/// <summary>
/// Ending processes and changing services, for the memory tab.
///
/// Everything is tried with the rights the program already has. Only when
/// Windows says "access denied" is a second copy of this program started
/// through UAC to do that one job and exit -- the program itself never runs
/// elevated. What that copy is asked to do is built and checked as plain
/// arguments first, so it can be tested without touching a process.
/// </summary>
internal static class MemoryTools
{
    /// <summary>The switch that makes a copy of the program do one job and exit.</summary>
    public const string ElevatedSwitch = "--elevated-op";

    private const int ExitDenied = 5;
    private const int ExitFailed = 1;
    private const int UacCancelled = 1223;

    /// <summary>
    /// Processes Windows depends on. Ending one is a blue screen or a forced
    /// log-off, not a way to free memory, so the tab will not offer it.
    /// </summary>
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "lsm", "memory compression", "secure system",
        "system interrupts", "dwm",
    };

    private static readonly Regex SafeName = new(@"^[^\x00-\x1f""]{1,260}$", RegexOptions.Compiled);

    public static bool IsProtected(int pid, string name) =>
        pid is <= 4 || pid == Environment.ProcessId || Critical.Contains(name);

    public static bool CanKill(ProcessEntry process) =>
        process.Readable && !IsProtected(process.Pid, process.Name);

    public static bool ValidName(string? name) => name is not null && SafeName.IsMatch(name);

    // ------------------------------------------------------------------- kill
    /// <summary>
    /// End one process, if it is still the process we were told about. A pid
    /// can be reused once its owner exits, so the name is checked first.
    /// </summary>
    internal static int KillLocal(int pid, string name)
    {
        if (IsProtected(pid, name))
        {
            return ExitFailed;
        }
        try
        {
            using Process process = Process.GetProcessById(pid);
            if (!string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase))
            {
                return 0;   // that pid is someone else now: ours is already gone
            }
            process.Kill();
            process.WaitForExit(3000);
            return 0;
        }
        catch (ArgumentException)
        {
            return 0;   // no such process any more
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
        catch (Win32Exception error) when (error.NativeErrorCode == ExitDenied)
        {
            return ExitDenied;
        }
        catch (Exception)
        {
            return ExitFailed;
        }
    }

    public static Task<OpResult> KillAsync(IEnumerable<ProcessEntry> targets) =>
        Task.Run(() =>
        {
            List<ProcessEntry> list = targets.Where(CanKill).ToList();
            if (list.Count == 0)
            {
                return OpResult.Failure("protected");
            }

            var denied = new List<ProcessEntry>();
            foreach (ProcessEntry process in list)
            {
                int code = KillLocal(process.Pid, process.Name);
                if (code == ExitDenied)
                {
                    denied.Add(process);
                }
                else if (code != 0)
                {
                    return OpResult.Failure(process.Name);
                }
            }
            return denied.Count == 0 ? OpResult.Success : Elevated(KillArguments(denied));
        });

    /// <summary>The arguments for the elevated copy: one "pid:name" per process.</summary>
    internal static List<string> KillArguments(IEnumerable<ProcessEntry> targets)
    {
        var args = new List<string> { ElevatedSwitch, "kill" };
        args.AddRange(targets.Select(t => $"{t.Pid}:{t.Name}"));
        return args;
    }

    // ---------------------------------------------------------------- service
    public static Task<OpResult> ServiceAsync(ServiceOp op, string name,
                                              ServiceStart start = ServiceStart.Other) =>
        Task.Run(() =>
        {
            if (!ValidName(name))
            {
                return OpResult.Failure("name");
            }
            ServiceControl.Result result = ServiceLocal(op, name, start);
            return result.Ok ? OpResult.Success
                : result.Denied ? Elevated(ServiceArguments(op, name, start))
                : OpResult.Failure(result.Message);
        });

    internal static ServiceControl.Result ServiceLocal(ServiceOp op, string name, ServiceStart start) =>
        op switch
        {
            ServiceOp.Stop => ServiceControl.Stop(name),
            ServiceOp.Start => ServiceControl.Start(name),
            ServiceOp.Restart => ServiceControl.Restart(name),
            _ => ServiceControl.SetStart(name, start),
        };

    internal static List<string> ServiceArguments(ServiceOp op, string name, ServiceStart start)
    {
        var args = new List<string> { ElevatedSwitch, "service", op.ToString().ToLowerInvariant(), name };
        if (op == ServiceOp.SetStart)
        {
            args.Add(start.ToString().ToLowerInvariant());
        }
        return args;
    }

    // --------------------------------------------------------------- elevation
    /// <summary>The launch of the elevated copy, before it is launched.</summary>
    internal static ProcessStartInfo ElevatedStart(string exe, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        return start;
    }

    private static OpResult Elevated(List<string> args)
    {
        string? exe = Environment.ProcessPath;
        if (exe is null)
        {
            return OpResult.Failure("no path");
        }
        try
        {
            using Process? helper = Process.Start(ElevatedStart(exe, args));
            if (helper is null)
            {
                return OpResult.Failure("not started");
            }
            helper.WaitForExit(60_000);
            if (!helper.HasExited)
            {
                return OpResult.Failure("timeout");
            }
            return helper.ExitCode == 0
                ? OpResult.Success
                : OpResult.Failure(new Win32Exception(helper.ExitCode).Message);
        }
        catch (Win32Exception error) when (error.NativeErrorCode == UacCancelled)
        {
            return OpResult.UserCancelled;
        }
        catch (Exception error)
        {
            return OpResult.Failure(error.Message);
        }
    }

    /// <summary>
    /// The elevated copy's whole life: do what the arguments say, return the
    /// exit code. Anything it does not recognise is refused, not guessed at.
    /// </summary>
    public static int RunElevated(string[] args)
    {
        int at = Array.IndexOf(args, ElevatedSwitch);
        string[] job = at < 0 ? Array.Empty<string>() : args[(at + 1)..];
        try
        {
            if (job.Length >= 2 && job[0] == "kill")
            {
                int worst = 0;
                foreach (string item in job[1..])
                {
                    int colon = item.IndexOf(':');
                    if (colon <= 0 || !int.TryParse(item[..colon], out int pid)
                        || !ValidName(item[(colon + 1)..]))
                    {
                        return ExitFailed;
                    }
                    worst = Math.Max(worst, KillLocal(pid, item[(colon + 1)..]));
                }
                return worst;
            }

            if (job.Length >= 3 && job[0] == "service"
                && Enum.TryParse(job[1], true, out ServiceOp op)
                && ValidName(job[2]))
            {
                ServiceStart start = ServiceStart.Other;
                if (op == ServiceOp.SetStart
                    && !(job.Length >= 4 && Enum.TryParse(job[3], true, out start)))
                {
                    return ExitFailed;
                }
                return ServiceLocal(op, job[2], start).Code;
            }
        }
        catch (Exception)
        {
            return ExitFailed;
        }
        return ExitFailed;
    }
}
