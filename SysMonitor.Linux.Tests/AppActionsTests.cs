using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class AppActionsTests
{
    private sealed class FakeProcessSignals : IProcessSignals
    {
        public Dictionary<int, ProcessFacts> Facts { get; } = new();
        public List<(int Pid, bool Force)> SignalsSent { get; } = new();

        public ProcessFacts? Read(int pid) => Facts.TryGetValue(pid, out ProcessFacts? f) ? f : null;

        public PinnedResult Pin(int pid)
        {
            if (!Facts.ContainsKey(pid))
            {
                return PinnedResult.Failed("No such process");
            }
            return PinnedResult.Pinned(new PinnedProc(this, pid));
        }

        public string? Signal(int pid, bool force)
        {
            SignalsSent.Add((pid, force));
            return null;
        }

        private sealed class PinnedProc : IPinnedProcess
        {
            private readonly FakeProcessSignals _owner;
            private readonly int _pid;

            public PinnedProc(FakeProcessSignals owner, int pid)
            {
                _owner = owner;
                _pid = pid;
            }

            public ProcessFacts? Read() => _owner.Read(_pid);

            public string? Signal(bool force) => _owner.Signal(_pid, force);

            public void Dispose()
            {
            }
        }
    }

    private static string MakeStat(int pid, string comm, ulong starttime = 1000) =>
        $"{pid} ({comm}) S 1 1 1 0 0 0 0 0 0 0 0 0 0 0 0 0 1 0 {starttime}";

    private static string MakeStatus(string name, int uid = 1000, int ppid = 1) =>
        $"Name:\t{name}\n" +
        $"PPid:\t{ppid}\n" +
        $"Uid:\t{uid}\t{uid}\t{uid}\t{uid}\n" +
        $"VmRSS:\t1024 kB\n";

    private static ProcessRow Row(int pid, ulong? starttime = 1000, string name = "proc", string? service = null) =>
        new()
        {
            Pid = pid,
            StartTime = starttime,
            Name = name,
            ServiceUnit = service,
        };

    private static AppEntry MakeApp(string name, string? exec, params ProcessRow[] processes) =>
        new()
        {
            Id = name.ToLowerInvariant(),
            Name = name,
            Exec = exec,
            Instances = new[]
            {
                new AppInstance
                {
                    Unit = $"app-{name.ToLowerInvariant()}-1.scope",
                    Processes = processes,
                },
            },
        };

    [TestMethod]
    public void CloseApp_TerminatesEveryMemberThroughKillFlow()
    {
        var signals = new FakeProcessSignals();
        signals.Facts[100] = new ProcessFacts { Pid = 100, Name = "app-worker", StartTime = 1000, Uid = 1000, ParentPid = 1, Readable = true };
        signals.Facts[101] = new ProcessFacts { Pid = 101, Name = "app-worker", StartTime = 1000, Uid = 1000, ParentPid = 1, Readable = true };

        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: 1000);
        var actions = new AppActions(kill, clock: clock.Now, sleep: ms => clock.Advance(ms));

        AppEntry app = MakeApp("worker", "worker", Row(100, name: "app-worker"), Row(101, name: "app-worker"));

        ActionResult result = actions.CloseApp(app);

        // Both members terminated with SIGTERM
        Assert.AreEqual(2, signals.SignalsSent.Count);
        Assert.AreEqual(100, signals.SignalsSent[0].Pid);
        Assert.IsFalse(signals.SignalsSent[0].Force);
        Assert.AreEqual(101, signals.SignalsSent[1].Pid);
        Assert.IsFalse(signals.SignalsSent[1].Force);
    }

    [TestMethod]
    public void CloseApp_ProtectedMemberIsNotSignalled()
    {
        var signals = new FakeProcessSignals();
        // PID 200 belongs to UID 0 (root/other user) -> protected under ownUid: 1000
        signals.Facts[200] = new ProcessFacts { Pid = 200, Name = "root-daemon", StartTime = 1000, Uid = 0, ParentPid = 1, Readable = true };

        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: 1000);
        var actions = new AppActions(kill, clock: clock.Now, sleep: ms => clock.Advance(ms));

        AppEntry app = MakeApp("daemon", "daemon", Row(200, name: "root-daemon"));

        ActionResult result = actions.CloseApp(app);

        // Protected process must NOT receive any signal
        Assert.AreEqual(0, signals.SignalsSent.Count);
        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
    }

    [TestMethod]
    public void RestartApp_WithNullOrUnparsableExec_ReturnsFailedWithoutSignallingOrLaunching()
    {
        var signals = new FakeProcessSignals();
        signals.Facts[300] = new ProcessFacts { Pid = 300, Name = "no-exec", StartTime = 1000, Uid = 1000, ParentPid = 1, Readable = true };

        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: 1000);

        bool launcherCalled = false;
        var actions = new AppActions(kill, launcher: argv => { launcherCalled = true; return null; }, clock: clock.Now, sleep: ms => clock.Advance(ms));

        AppEntry app = MakeApp("noexec", null, Row(300, name: "no-exec"));

        ActionResult result = actions.RestartApp(app);

        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
        Assert.IsFalse(launcherCalled);
        Assert.AreEqual(0, signals.SignalsSent.Count);
    }

    [TestMethod]
    public void RestartApp_WhenMembersExit_CallsLauncherOnceWithParsedArgv()
    {
        var signals = new FakeProcessSignals();
        signals.Facts[400] = new ProcessFacts { Pid = 400, Name = "quick-exit", StartTime = 1000, Uid = 1000, ParentPid = 1, Readable = true };

        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: 1000);

        IReadOnlyList<string>? launchedArgv = null;
        int launchCount = 0;
        Func<IReadOnlyList<string>, string?> launcher = argv =>
        {
            launchedArgv = argv;
            launchCount++;
            return null;
        };

        // isAlive reports false immediately, simulating exit
        var actions = new AppActions(
            kill,
            launcher: launcher,
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: target => false);

        AppEntry app = MakeApp("quick", "quick-exit %u --run", Row(400, name: "quick-exit"));

        ActionResult result = actions.RestartApp(app);

        Assert.AreEqual(ActionOutcome.Done, result.Outcome);
        Assert.AreEqual(1, launchCount);
        Assert.IsNotNull(launchedArgv);
        Assert.AreEqual(2, launchedArgv.Count);
        Assert.AreEqual("quick-exit", launchedArgv[0]);
        Assert.AreEqual("--run", launchedArgv[1]);
    }

    [TestMethod]
    public void RestartApp_WhenMembersStillAliveAfter5Seconds_DoesNotLaunch()
    {
        var signals = new FakeProcessSignals();
        signals.Facts[500] = new ProcessFacts { Pid = 500, Name = "stubborn", StartTime = 1000, Uid = 1000, ParentPid = 1, Readable = true };

        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: 1000);

        bool launcherCalled = false;
        var actions = new AppActions(
            kill,
            launcher: argv => { launcherCalled = true; return null; },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: target => true); // Always alive!

        AppEntry app = MakeApp("stubborn", "stubborn", Row(500, name: "stubborn"));

        ActionResult result = actions.RestartApp(app);

        Assert.AreEqual(ActionOutcome.NeedsForce, result.Outcome);
        Assert.IsFalse(launcherCalled, "Launcher must NOT be called when processes did not exit");
        Assert.AreEqual(1, result.Stubborn.Count);
        Assert.AreEqual(500, result.Stubborn[0].Target.Pid);
    }

    [TestMethod]
    public void RestartService_WhenServiceUnitSet_CallsServiceControlRestart()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/600/status", MakeStatus("my-service", uid: 1000));
        fake.WriteFile("proc/600/stat", MakeStat(600, "my-service", starttime: 1000));
        fake.WriteFile("proc/600/cmdline", "my-service\0");

        var runner = new FakeCommandRunner();
        runner.CommandOutputs["systemctl --user show -p MainPID -- my-service.service"] = "MainPID=600\n";
        runner.CommandOutputs["systemctl --user restart -- my-service.service"] = "";

        var signals = new FakeProcessSignals();
        var kill = new KillFlow(signals, ownPid: 9999, ownUid: 1000);
        var actions = new AppActions(kill, commands: runner, root: fake.Root, ownUid: 1000, ownPid: 9999);

        ProcessRow rowWithService = Row(600, starttime: 1000, name: "my-service", service: "my-service.service");
        ActionResult okResult = actions.RestartService(rowWithService);

        Assert.AreEqual(ActionOutcome.Done, okResult.Outcome);
        Assert.AreEqual(2, runner.Executions.Count);
        Assert.AreEqual("systemctl", runner.Executions[0].File);
        Assert.AreEqual("--user show -p MainPID -- my-service.service", runner.Executions[0].Args);
        Assert.AreEqual("systemctl", runner.Executions[1].File);
        Assert.AreEqual("--user restart -- my-service.service", runner.Executions[1].Args);

        // When ServiceUnit is null -> refused without running any commands
        ProcessRow rowWithoutService = Row(601, starttime: 1000, name: "my-service", service: null);
        ActionResult failedResult = actions.RestartService(rowWithoutService);

        Assert.AreEqual(ActionOutcome.Failed, failedResult.Outcome);
        Assert.AreEqual(2, runner.Executions.Count); // No new command executed
    }

    [TestMethod]
    public void RestartService_RefusesWhenStartTimeDiffersOrNull()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/600/status", MakeStatus("my-service", uid: 1000));
        fake.WriteFile("proc/600/stat", MakeStat(600, "my-service", starttime: 1000));
        fake.WriteFile("proc/600/cmdline", "my-service\0");

        var runner = new FakeCommandRunner();
        var signals = new FakeProcessSignals();
        var kill = new KillFlow(signals, ownPid: 9999, ownUid: 1000);
        var actions = new AppActions(kill, commands: runner, root: fake.Root, ownUid: 1000, ownPid: 9999);

        // 1. StartTime differs (row: 2000 vs proc: 1000)
        ProcessRow rowDifferentStart = Row(600, starttime: 2000, name: "my-service", service: "my-service.service");
        ActionResult resultDiff = actions.RestartService(rowDifferentStart);

        Assert.AreEqual(ActionOutcome.Failed, resultDiff.Outcome);
        Assert.AreEqual(0, runner.Executions.Count, "No command must run when StartTime differs");

        // 2. StartTime is null
        ProcessRow rowNullStart = Row(600, starttime: null, name: "my-service", service: "my-service.service");
        ActionResult resultNull = actions.RestartService(rowNullStart);

        Assert.AreEqual(ActionOutcome.Failed, resultNull.Outcome);
        Assert.AreEqual(0, runner.Executions.Count, "No command must run when StartTime is null");
    }

    [TestMethod]
    public void RestartService_RefusesWhenNameDiffers()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/600/status", MakeStatus("my-service", uid: 1000));
        fake.WriteFile("proc/600/stat", MakeStat(600, "my-service", starttime: 1000));
        fake.WriteFile("proc/600/cmdline", "my-service\0");

        var runner = new FakeCommandRunner();
        var signals = new FakeProcessSignals();
        var kill = new KillFlow(signals, ownPid: 9999, ownUid: 1000);
        var actions = new AppActions(kill, commands: runner, root: fake.Root, ownUid: 1000, ownPid: 9999);

        ProcessRow rowDifferentName = Row(600, starttime: 1000, name: "other-name", service: "my-service.service");
        ActionResult result = actions.RestartService(rowDifferentName);

        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
        Assert.AreEqual(0, runner.Executions.Count, "No command must run when Name differs");
    }

    [TestMethod]
    public void RestartService_RefusesWhenFreshMainPidDiffers()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/600/status", MakeStatus("my-service", uid: 1000));
        fake.WriteFile("proc/600/stat", MakeStat(600, "my-service", starttime: 1000));
        fake.WriteFile("proc/600/cmdline", "my-service\0");

        var runner = new FakeCommandRunner();
        // systemctl show returns MainPID=999 (different from row's 600)
        runner.CommandOutputs["systemctl --user show -p MainPID -- my-service.service"] = "MainPID=999\n";

        var signals = new FakeProcessSignals();
        var kill = new KillFlow(signals, ownPid: 9999, ownUid: 1000);
        var actions = new AppActions(kill, commands: runner, root: fake.Root, ownUid: 1000, ownPid: 9999);

        ProcessRow row = Row(600, starttime: 1000, name: "my-service", service: "my-service.service");
        ActionResult result = actions.RestartService(row);

        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
        Assert.AreEqual(1, runner.Executions.Count);
        Assert.AreEqual("--user show -p MainPID -- my-service.service", runner.Executions[0].Args);
        // Crucially, systemctl restart was NOT called!
    }

    [TestMethod]
    public void RestartService_RefusesWhenProtectedAndNotOwnedByUs()
    {
        using var fake = new FakeRoot();
        // Process belongs to root (UID 0), our UID is 1000
        fake.WriteFile("proc/600/status", MakeStatus("root-service", uid: 0));
        fake.WriteFile("proc/600/stat", MakeStat(600, "root-service", starttime: 1000));
        fake.WriteFile("proc/600/cmdline", "root-service\0");

        var runner = new FakeCommandRunner();
        var signals = new FakeProcessSignals();
        var kill = new KillFlow(signals, ownPid: 9999, ownUid: 1000);
        var actions = new AppActions(kill, commands: runner, root: fake.Root, ownUid: 1000, ownPid: 9999);

        ProcessRow row = Row(600, starttime: 1000, name: "root-service", service: "root-service.service");
        ActionResult result = actions.RestartService(row);

        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
        Assert.AreEqual(0, runner.Executions.Count, "Protected process not owned by us must refuse restart before systemctl");
    }

    [TestMethod]
    public void RestartService_OnSessionCriticalNameOwnedByOurUid_WithMatchingMainPid_FailsWithoutCallingRestart()
    {
        using var fake = new FakeRoot();
        // plasmashell is session-critical (ProcessProtection), owned by our own UID (1000)
        fake.WriteFile("proc/600/status", MakeStatus("plasmashell", uid: 1000));
        fake.WriteFile("proc/600/stat", MakeStat(600, "plasmashell", starttime: 1000));
        fake.WriteFile("proc/600/cmdline", "plasmashell\0");

        var runner = new FakeCommandRunner();
        runner.CommandOutputs["systemctl --user show -p MainPID -- plasma-plasmashell.service"] = "MainPID=600\n";
        runner.CommandOutputs["systemctl --user restart -- plasma-plasmashell.service"] = "";

        var signals = new FakeProcessSignals();
        var kill = new KillFlow(signals, ownPid: 9999, ownUid: 1000);
        var actions = new AppActions(kill, commands: runner, root: fake.Root, ownUid: 1000, ownPid: 9999);

        ProcessRow row = Row(600, starttime: 1000, name: "plasmashell", service: "plasma-plasmashell.service");
        ActionResult result = actions.RestartService(row);

        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
        Assert.AreEqual(0, runner.Executions.Count, "Protected session-critical process must refuse restart before systemctl");
        Assert.IsFalse(runner.Executions.Any(e => e.Args.Contains("restart")), "systemctl restart must never be called");
    }

    [TestMethod]
    public void RestartService_RefusesWhenProcessVanished()
    {
        using var fake = new FakeRoot();
        // No /proc/600 exists in fake root

        var runner = new FakeCommandRunner();
        var signals = new FakeProcessSignals();
        var kill = new KillFlow(signals, ownPid: 9999, ownUid: 1000);
        var actions = new AppActions(kill, commands: runner, root: fake.Root, ownUid: 1000, ownPid: 9999);

        ProcessRow row = Row(600, starttime: 1000, name: "my-service", service: "my-service.service");
        ActionResult result = actions.RestartService(row);

        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
        Assert.AreEqual(0, runner.Executions.Count);
    }
}
