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
    ///
    /// Before the launch the cgroups are read again (Amendment 2): every
    /// instance's <c>cgroup.procs</c> recursively, plus any other live instance
    /// of the same app id under the user's <c>app.slice</c>. Nothing is started
    /// unless all of them are empty, because the snapshot the button was clicked
    /// on cannot prove that a process which started afterwards is absent.
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

        // Amendment 2: the snapshot's members are gone, but a fresh read of the
        // cgroups decides whether anything else of this app is still running.
        if (!AllInstancesEmpty(app))
        {
            return ActionResult.Failed(_lang["app_restart_still_running"]);
        }

        string? error = _launcher(argv);
        return error is null ? ActionResult.Done : ActionResult.Failed(error);
    }

    /// <summary>
    /// The second half of a restart that needed force (Amendment 1): the caller
    /// has already had its force kill confirmed, so this waits up to five
    /// seconds for every member of the app to be gone and only then starts the
    /// desktop entry once. It never launches beside a living process: a member
    /// that is still there — or a missing/unparsable Exec, or no known member
    /// at all to check — is a failed result and nothing is started.
    ///
    /// Like <see cref="RestartApp"/>, the last check before the launch is a
    /// fresh cgroup read (Amendment 2), not the PIDs captured when the button
    /// was clicked.
    /// </summary>
    public ActionResult CompleteRestart(AppEntry app)
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

        // Nothing to check means nothing proves the app is gone, so a launch
        // here could sit beside a process this snapshot never saw.
        if (targets.Count == 0)
        {
            return ActionResult.Failed(_lang["app_restart_unavailable"]);
        }

        long deadline = _clock() + RestartWaitMs;
        while (targets.Any(_alive) && _clock() < deadline)
        {
            _sleep(RestartStepMs);
        }

        if (targets.Any(_alive))
        {
            return ActionResult.Failed(_lang["app_restart_still_running"]);
        }

        if (!AllInstancesEmpty(app))
        {
            return ActionResult.Failed(_lang["app_restart_still_running"]);
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
    ///
    /// The identity is the PID and the start time only (Amendment 2): a process
    /// keeps both across an <c>exec</c> or a <c>comm</c> rename, so a changed
    /// name is still the same process and still blocks the launch.
    /// </summary>
    private static bool Alive(string root, ProcessTarget target)
    {
        if (target.StartTime is not > 0)
        {
            return true;
        }

        ProcessFacts? facts = ProcessReader.FactsOf(root, target.Pid);
        return facts is not null && facts.StartTime == target.StartTime;
    }

    /// <summary>
    /// Amendment 2: true only when no process at all remains for this app. It
    /// reads the app's cgroups now instead of trusting the PIDs the click
    /// captured: every known instance's <c>cgroup.procs</c> recursively, then
    /// every other top-level unit under the user's <c>app.slice</c> whose id is
    /// this app's. A cgroup that exists but cannot be read is not proof that it
    /// is empty, so it counts as running and the launch is refused.
    /// </summary>
    private bool AllInstancesEmpty(AppEntry app)
    {
        string appSlice = AppSlice();

        foreach (AppInstance instance in app.Instances)
        {
            // An unusable unit name cannot be resolved to a directory, so it
            // cannot prove the instance is gone.
            if (!IsSafeUnit(instance.Unit)
                || CgroupHasProcesses(Path.Combine(appSlice, instance.Unit)))
            {
                return false;
            }
        }

        return !OtherInstanceHasProcesses(app, appSlice);
    }

    /// <summary>
    /// The ids one app's units may carry. The desktop id is the obvious one;
    /// the stem of each unit already in the snapshot is added because the same
    /// desktop entry is launched under more than one spelling
    /// (<c>app-dolphin@…</c> and <c>app-org.kde.dolphin@…</c>).
    /// </summary>
    private static HashSet<string> AppIds(AppEntry app)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { app.Id };
        foreach (AppInstance instance in app.Instances)
        {
            if (AppResolver.ParseUnit(instance.Unit) is { } parsed)
            {
                ids.Add(parsed.Id);
            }
        }
        return ids;
    }

    /// <summary>
    /// A live top-level unit of the same app that the snapshot did not list.
    /// Units already checked by <see cref="AllInstancesEmpty"/> are skipped so
    /// each cgroup is read once.
    ///
    /// Amendment 3: the slice is listed with <see cref="ListDirectories"/>, so
    /// a slice that exists but cannot be read counts as "something may be
    /// running" instead of "no instance".
    /// </summary>
    private bool OtherInstanceHasProcesses(AppEntry app, string appSlice)
    {
        HashSet<string> ids = AppIds(app);
        var known = new HashSet<string>(
            app.Instances.Select(instance => instance.Unit), StringComparer.Ordinal);

        List<string>? directories = ListDirectories(appSlice);
        if (directories is null)
        {
            return true;
        }

        foreach (string directory in directories)
        {
            string unit = Path.GetFileName(directory);
            if (known.Contains(unit))
            {
                continue;
            }

            if (AppResolver.ParseUnit(unit) is not { } parsed || !ids.Contains(parsed.Id))
            {
                continue;
            }

            if (CgroupHasProcesses(directory))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="directory"/> or any cgroup below it still has a
    /// process. A missing directory is empty; a directory that is there but
    /// whose <c>cgroup.procs</c> cannot be read is not, because nothing then
    /// proves the instance ended.
    ///
    /// Amendment 3: a listing that fails anywhere in the walk also counts as
    /// "has processes", because an unreadable child cgroup could hide one.
    /// Symbolic links are never followed, and a symlinked instance path cannot
    /// prove the instance ended, so it counts as running.
    /// </summary>
    private static bool CgroupHasProcesses(string directory)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(directory);

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            if (!seen.Add(current) || !Directory.Exists(current))
            {
                continue;
            }

            // Never follow a link: a cgroup is a real directory, and a link
            // could leave the slice or loop. If its status cannot be read, the
            // walk has failed and the instance is treated as still running.
            if (IsLink(current) is not bool isLink)
            {
                return true;
            }
            if (isLink)
            {
                return true;
            }

            string? procs = SysFs.ReadText(Path.Combine(current, "cgroup.procs"));
            if (procs is null)
            {
                return true;
            }

            foreach (string raw in procs.Split('\n'))
            {
                if (int.TryParse(
                        raw.Trim(),
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out int pid)
                    && pid > 0)
                {
                    return true;
                }
            }

            List<string>? children = ListDirectories(current);
            if (children is null)
            {
                return true;
            }

            foreach (string child in children)
            {
                stack.Push(child);
            }
        }

        return false;
    }

    /// <summary>
    /// Amendment 3: a directory listing that distinguishes "listed, empty" from
    /// "could not list". <see cref="SysFs.Directories"/> returns an empty list on
    /// an error, which in the restart check would read as "no instance" and let
    /// a launch sit beside a live process; this helper is used here only and
    /// leaves that shared helper unchanged.
    ///
    /// A missing directory is an empty list (no unit can be running without its
    /// slice). A directory that exists but cannot be read returns null. Links
    /// are skipped so the walk never leaves the slice or loops; a link whose
    /// status cannot be read also makes the whole listing null, because nothing
    /// then proves what is underneath it.
    /// </summary>
    private static List<string>? ListDirectories(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return new List<string>();
        }
        catch (DirectoryNotFoundException)
        {
            return new List<string>();
        }
        catch (Exception)
        {
            return null;
        }

        if ((attributes & FileAttributes.Directory) == 0)
        {
            return null;
        }

        try
        {
            var directories = new List<string>();
            foreach (string directory in Directory.EnumerateDirectories(path))
            {
                if (IsLink(directory) is not bool isLink)
                {
                    return null;
                }
                if (isLink)
                {
                    continue;
                }
                directories.Add(directory);
            }

            directories.Sort(StringComparer.Ordinal);
            return directories;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// True for a symbolic link, false for a real directory. Null means the link
    /// status could not be read, which the listing and the walk count as a
    /// failure rather than as proof that nothing is there.
    /// </summary>
    private static bool? IsLink(string path)
    {
        try
        {
            return new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A unit name is a single directory entry under <c>app.slice</c>. A
    /// backslash is legal in a systemd escaped unit (<c>\x2d</c>), so it is not
    /// rejected here; only a path separator or NUL could leave the slice.
    /// </summary>
    private static bool IsSafeUnit(string unit) =>
        unit.Length > 0
        && unit != "."
        && unit != ".."
        && !unit.Contains('/')
        && !unit.Contains('\0');

    /// <summary>This user's session <c>app.slice</c>, under the injected root.</summary>
    private string AppSlice() =>
        Path.Combine(
            _root,
            "sys",
            "fs",
            "cgroup",
            "user.slice",
            $"user-{_ownUid}.slice",
            $"user@{_ownUid}.service",
            "app.slice");

    private static string FirstLine(string text)
    {
        int newline = text.IndexOf('\n');
        string line = newline >= 0 ? text[..newline] : text;
        return line.Trim();
    }
}
