using System.Diagnostics;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>How one Apps/Processes action ended.</summary>
public enum ActionOutcome
{
    Done,

    /// <summary>Something survived SIGTERM; the caller must ask about forcing it.</summary>
    NeedsForce,

    Failed,
}

/// <summary>
/// The result of a close or restart, with the tokens the force prompt owns. A
/// token is handed back unchanged from <see cref="KillFlow"/>, never replaced.
/// </summary>
public readonly record struct ActionResult(
    ActionOutcome Outcome, IReadOnlyList<PendingKill> Stubborn, string? Error)
{
    public static ActionResult Done =>
        new(ActionOutcome.Done, Array.Empty<PendingKill>(), null);

    public static ActionResult NeedsForce(IReadOnlyList<PendingKill> stubborn, string? error = null) =>
        new(ActionOutcome.NeedsForce, stubborn, error);

    public static ActionResult Failed(string error) =>
        new(ActionOutcome.Failed, Array.Empty<PendingKill>(), error);
}

/// <summary>
/// Closing and restarting apps and processes. Every signal goes through
/// <see cref="KillFlow"/>, so the identity check and the protection rules are
/// the same ones the memory tab already uses; nothing here calls <c>kill</c>.
///
/// A restart closes and waits, and only then starts the desktop entry once. It
/// never starts a program to replace a still-running process.
/// </summary>
public sealed class AppActions
{
    public const int RestartWaitMs = 5000;
    public const int RestartStepMs = 100;

    /// <summary>Timeout of the one <c>systemctl show</c> a service restart adds.</summary>
    public const int ShowTimeoutMs = 4000;

    private readonly KillFlow _kill;
    private readonly Func<IReadOnlyList<string>, string?> _launcher;
    private readonly Func<long> _clock;
    private readonly Action<int> _sleep;
    private readonly Func<ProcessTarget, bool> _alive;
    private readonly ServiceControl _services;
    private readonly Lang _lang;
    private readonly ICommandRunner _commands;
    private readonly int _ownPid;
    private readonly int _ownUid;
    private readonly string _root;

    /// <param name="kill">The one kill flow, shared with the memory tab.</param>
    /// <param name="launcher">
    /// Starts an argv. Null means success, otherwise the error text. The default
    /// uses <see cref="ProcessStartInfo.ArgumentList"/> and no shell, and does
    /// not wait for the program. A test passes one that only records the argv.
    /// </param>
    /// <param name="clock">Milliseconds source, for the restart wait.</param>
    /// <param name="sleep">Milliseconds sleep, for the restart wait.</param>
    /// <param name="isAlive">
    /// Identity-aware "is this exact process still there", for the restart wait.
    /// </param>
    /// <param name="commands">Runs <c>systemctl</c>.</param>
    /// <param name="root">Where <c>/proc</c> lives, for the default alive check.</param>
    /// <param name="lang">The strings table.</param>
    /// <param name="ownUid">
    /// This user's real UID, for the protection re-check of a service restart.
    /// Null reads it from the process.
    /// </param>
    /// <param name="ownPid">This process's PID, for the protection re-check.</param>
    public AppActions(
        KillFlow kill,
        Func<IReadOnlyList<string>, string?>? launcher = null,
        Func<long>? clock = null,
        Action<int>? sleep = null,
        Func<ProcessTarget, bool>? isAlive = null,
        ICommandRunner? commands = null,
        string root = "/",
        Lang? lang = null,
        int? ownUid = null,
        int ownPid = 0)
    {
        _kill = kill;
        _launcher = launcher ?? Launch;
        _clock = clock ?? (() => Environment.TickCount64);
        _sleep = sleep ?? Thread.Sleep;
        _root = string.IsNullOrEmpty(root) ? "/" : root;
        _alive = isAlive ?? (target => Alive(_root, target));
        _lang = lang ?? new Lang("en");
        _ownUid = ownUid ?? Posix.Uid;
        _ownPid = ownPid > 0 ? ownPid : Environment.ProcessId;

        ICommandRunner runner = commands ?? new ProcessCommandRunner();
        _commands = runner;
        _services = new ServiceControl(args =>
        {
            CommandResult? completed = runner.RunResult("systemctl", args, 15000);
            if (completed is not { } result)
            {
                return "-1";
            }
            if (result.Ok)
            {
                return null;
            }
            string line = FirstLine(result.Error);
            return line.Length > 0 ? line : result.ExitCode.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        });
    }

    /// <summary>SIGTERM to every process in the instance.</summary>
    public ActionResult CloseInstance(AppInstance instance) =>
        Close(instance.Processes);

    /// <summary>SIGTERM to every process in every instance of the app.</summary>
    public ActionResult CloseApp(AppEntry app) => Close(app.Instances.SelectMany(i => i.Processes));

    /// <summary>
    /// Close every instance, wait up to five seconds for every member to be
    /// gone, then launch the app once from its <c>.desktop</c> <c>Exec</c>. A
    /// missing or unparsable Exec is refused before anything is closed.
    /// </summary>
    public ActionResult RestartApp(AppEntry app)
    {
        IReadOnlyList<string>? argv = DesktopEntry.ParseExec(app.Exec);
        if (argv is null)
        {
            return ActionResult.Failed(_lang["app_restart_unavailable"]);
        }

        List<ProcessTarget> targets = app.Instances
            .SelectMany(instance => instance.Processes)
            .Select(Target)
            .ToList();

        if (targets.Count == 0)
        {
            return ActionResult.Failed(_lang["app_restart_unavailable"]);
        }

        (IReadOnlyList<PendingKill> stubborn, string? killError) =
            _kill.TerminateAll(targets);
        List<PendingKill> tokens = stubborn.ToList();

        long deadline = _clock() + RestartWaitMs;
        while (targets.Any(_alive) && _clock() < deadline)
        {
            _sleep(RestartStepMs);
        }

        // A token whose process left during the wait has nothing to force.
        var stillStubborn = new List<PendingKill>();
        foreach (PendingKill token in tokens)
        {
            if (_alive(token.Target))
            {
                stillStubborn.Add(token);
            }
            else
            {
                _kill.Decline(token);
            }
        }

        if (targets.Any(_alive))
        {
            return ActionResult.NeedsForce(
                stillStubborn, _lang["app_restart_still_running"]);
        }

        string? error = _launcher(argv);
        return error is null ? ActionResult.Done : ActionResult.Failed(error);
    }

    /// <summary>
    /// Restart only a process that is the MainPID of a systemd user unit. Any
    /// other process has no safe restart and gets a failed result.
    ///
    /// The restart is a systemd action, but it still has to name the same
    /// process the row named (Amendment 2): a fresh <see cref="ProcessFacts"/>
    /// must carry the row's start time (a real one) and name, the protection
    /// rules must allow it, and a fresh <c>systemctl --user show -p MainPID</c>
    /// must still return this PID. Any difference means the process changed
    /// while the menu was open, and nothing is restarted.
    ///
    /// <see cref="ProcessProtection"/> applies in full, with no ownership
    /// exception (Amendment 3): the fact that the MainPID belongs to this user
    /// never makes a protected process (for example <c>plasmashell</c> or a
    /// session bus) restartable.
    /// </summary>
    public ActionResult RestartService(ProcessRow row)
    {
        if (string.IsNullOrEmpty(row.ServiceUnit))
        {
            return ActionResult.Failed(_lang["app_restart_unavailable"]);
        }

        if (!ServiceControl.IsValidUnitName(row.ServiceUnit))
        {
            return ActionResult.Failed(_lang["app_restart_unavailable"]);
        }

        ProcessFacts? facts = ProcessReader.FactsOf(_root, row.Pid);
        if (facts is null
            || row.StartTime is not > 0
            || facts.StartTime is not > 0
            || facts.StartTime != row.StartTime
            || !string.Equals(facts.Name, row.Name, StringComparison.Ordinal))
        {
            return ActionResult.Failed(_lang.Get(
                "mem_pid_changed", row.Pid, facts?.Name ?? row.Name));
        }

        // Protection is re-read from the fresh facts, never from the row, and it
        // applies in full: the own-UID exception of Amendment 2 is withdrawn
        // (Amendment 3), so a protected process is refused even when its unit
        // runs as this user. systemd would happily restart it; this feature will
        // not, because restarting the session's shell or bus ends the session.
        if (ProcessProtection.IsProtected(facts, _ownPid, _ownUid))
        {
            return ActionResult.Failed(_lang["mem_protected"]);
        }

        if (!IsStillMainPid(row.ServiceUnit, row.Pid))
        {
            return ActionResult.Failed(_lang["app_restart_unavailable"]);
        }

        ServiceResult result = _services.Apply(ServiceControl.Restart, row.ServiceUnit);
        return result.Ok ? ActionResult.Done : ActionResult.Failed(result.Error ?? string.Empty);
    }

    /// <summary>
    /// The one fresh <c>systemctl --user show -p MainPID -- &lt;unit&gt;</c> of a
    /// restart. False when the call fails, prints nothing usable, or now names
    /// another PID: in each case the unit no longer runs the process the row
    /// described, so a restart would act on something else.
    /// </summary>
    private bool IsStillMainPid(string unit, int pid)
    {
        var arguments = new[]
        {
            "--user",
            "show",
            "-p",
            "MainPID",
            "--",
            ServiceControl.UnitFile(unit),
        };

        string? text = _commands.Run("systemctl", arguments, ShowTimeoutMs);
        if (text is null)
        {
            return false;
        }

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (!line.StartsWith("MainPID=", StringComparison.Ordinal))
            {
                continue;
            }

            return int.TryParse(
                    line["MainPID=".Length..].Trim(),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int mainPid)
                && mainPid == pid;
        }

        return false;
    }

    /// <summary>Starts argv without a shell and without waiting for it.</summary>
    public static string? Launch(IReadOnlyList<string> argv)
    {
        if (argv.Count == 0 || string.IsNullOrEmpty(argv[0]))
        {
            return "empty command";
        }

        try
        {
            var start = new ProcessStartInfo(argv[0])
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            for (int i = 1; i < argv.Count; i++)
            {
                start.ArgumentList.Add(argv[i]);
            }

            using Process? process = Process.Start(start);
            return process is null ? "process did not start" : null;
        }
        catch (Exception error)
        {
            return error.Message;
        }
    }

    private ActionResult Close(IEnumerable<ProcessRow> rows)
    {
        var targets = rows.Select(Target).ToList();
        (IReadOnlyList<PendingKill> stubborn, string? error) = _kill.TerminateAll(targets);
        if (stubborn.Count > 0)
        {
            return ActionResult.NeedsForce(stubborn, error);
        }
        return error is null ? ActionResult.Done : ActionResult.Failed(error);
    }

    private static ProcessTarget Target(ProcessRow row) =>
        new(row.Pid, row.StartTime, row.Name);

    /// <summary>
    /// True only for the exact process the target names. A missing start time
    /// cannot prove identity, so it is treated as still there and the restart
    /// refuses to launch rather than risk replacing the wrong thing.
    /// </summary>
    private static bool Alive(string root, ProcessTarget target)
    {
        if (target.StartTime is not > 0)
        {
            return true;
        }

        ProcessFacts? facts = ProcessReader.FactsOf(root, target.Pid);
        return facts is not null
            && facts.StartTime == target.StartTime
            && string.Equals(facts.Name, target.Name, StringComparison.Ordinal);
    }

    private static string FirstLine(string text)
    {
        int newline = text.IndexOf('\n');
        string line = newline >= 0 ? text[..newline] : text;
        return line.Trim();
    }
}
