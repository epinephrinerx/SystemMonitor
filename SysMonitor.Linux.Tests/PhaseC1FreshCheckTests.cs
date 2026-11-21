using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux;
using SysMonitor.Linux.Presentation;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

/// <summary>
/// Round 3 and Round 4 tests for R-030 Phase C Part 1:
/// - Round 3 (Amendment 2): fresh cgroup check, Alive identity, Cancelled force.
/// - Round 4 (Amendment 3): unlistable cgroups block launch, symlinks skipped, SysFs unchanged.
/// </summary>
[TestClass]
public sealed class PhaseC1FreshCheckTests
{
    private const int TestUid = 1000;

    private static string RelativeAppSlice =>
        Path.Combine("sys", "fs", "cgroup", "user.slice", $"user-{TestUid}.slice", $"user@{TestUid}.service", "app.slice");

    private sealed class FakeProcessSignals : IProcessSignals
    {
        public Dictionary<int, ProcessFacts> Facts { get; } = new();
        public List<(int Pid, bool Force)> SignalsSent { get; } = new();
        public Func<int, bool, string?>? OnSignal { get; set; }

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
            return OnSignal?.Invoke(pid, force);
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

    private static string MakeStatus(string name, int uid = TestUid, int ppid = 1) =>
        $"Name:\t{name}\n" +
        $"PPid:\t{ppid}\n" +
        $"Uid:\t{uid}\t{uid}\t{uid}\t{uid}\n" +
        $"VmRSS:\t1024 kB\n";

    private static ProcessRow Row(int pid, string name = "proc", ulong? starttime = 1000) =>
        new()
        {
            Pid = pid,
            StartTime = starttime,
            Name = name,
        };

    private static AppEntry MakeApp(string id, string name, string? exec, string unit, params ProcessRow[] processes) =>
        new()
        {
            Id = id,
            Name = name,
            Exec = exec,
            Instances = new[]
            {
                new AppInstance
                {
                    Unit = unit,
                    Processes = processes,
                },
            },
        };

    // ------------------------------------------------------------------------
    // 1. RestartApp: known instance's cgroup.procs has a PID -> Failed, recorder empty
    // ------------------------------------------------------------------------
    [TestMethod]
    public void RestartApp_WhenKnownInstanceCgroupHasPid_ReturnsFailedAndRecorderEmpty()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;

        // Known instance cgroup still contains a living PID (e.g. 500)
        fake.WriteFile(Path.Combine(appSlice, "app-testapp-1.scope", "cgroup.procs"), "500\n");

        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        var actions = new AppActions(
            kill,
            launcher: argv => { recordedArgvs.Add(argv); return null; },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false, // snapshot members are gone
            root: fake.Root,
            lang: lang,
            ownUid: TestUid,
            ownPid: 9999);

        AppEntry app = MakeApp("testapp", "Test App", "testapp --run", "app-testapp-1.scope", Row(100, "testapp"));

        ActionResult result = actions.RestartApp(app);

        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
        Assert.AreEqual(lang["app_restart_still_running"], result.Error);
        Assert.AreEqual(0, recordedArgvs.Count, "Launcher must not be called when cgroup still has a process");
        Assert.AreEqual(0, result.Stubborn.Count, "Blocked fresh check must return Stubborn.Count == 0 (no force tokens)");
    }

    // ------------------------------------------------------------------------
    // 2. RestartApp: nested child cgroup has a PID -> same, proving recursion
    // ------------------------------------------------------------------------
    [TestMethod]
    public void RestartApp_WhenNestedChildCgroupHasPid_ReturnsFailedAndRecorderEmpty_ProvingRecursion()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;

        // Top-level instance cgroup.procs is empty
        fake.WriteFile(Path.Combine(appSlice, "app-testapp-1.scope", "cgroup.procs"), "");
        // But a nested child cgroup contains a PID (e.g. tab.scope with PID 501)
        fake.WriteFile(Path.Combine(appSlice, "app-testapp-1.scope", "tab.scope", "cgroup.procs"), "501\n");

        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        var actions = new AppActions(
            kill,
            launcher: argv => { recordedArgvs.Add(argv); return null; },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false, // snapshot members are gone
            root: fake.Root,
            lang: lang,
            ownUid: TestUid,
            ownPid: 9999);

        AppEntry app = MakeApp("testapp", "Test App", "testapp --run", "app-testapp-1.scope", Row(100, "testapp"));

        ActionResult result = actions.RestartApp(app);

        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
        Assert.AreEqual(lang["app_restart_still_running"], result.Error);
        Assert.AreEqual(0, recordedArgvs.Count, "Launcher must not be called when nested child cgroup still has a process");
        Assert.AreEqual(0, result.Stubborn.Count, "Blocked fresh check must return Stubborn.Count == 0");
    }

    // ------------------------------------------------------------------------
    // 3. RestartApp: another top-level app-<id>@other.service or app-<id>-2.scope has a PID -> Failed, recorder empty
    // ------------------------------------------------------------------------
    [TestMethod]
    public void RestartApp_WhenOtherTopLevelServiceOrScopeHasPid_ReturnsFailedAndRecorderEmpty()
    {
        // 3a. Another top-level service unit (app-editor@other.service)
        {
            using var fake = new FakeRoot();
            string appSlice = RelativeAppSlice;

            // Known instance is empty
            fake.WriteFile(Path.Combine(appSlice, "app-editor-1.scope", "cgroup.procs"), "");
            // Another top-level service of the same app id has a PID
            fake.WriteFile(Path.Combine(appSlice, "app-editor@other.service", "cgroup.procs"), "502\n");

            var signals = new FakeProcessSignals();
            var clock = new FakeClock();
            var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
            var lang = new Lang("en");

            var recordedArgvs = new List<IReadOnlyList<string>>();
            var actions = new AppActions(
                kill,
                launcher: argv => { recordedArgvs.Add(argv); return null; },
                clock: clock.Now,
                sleep: ms => clock.Advance(ms),
                isAlive: _ => false,
                root: fake.Root,
                lang: lang,
                ownUid: TestUid,
                ownPid: 9999);

            AppEntry app = MakeApp("editor", "Editor", "editor %u", "app-editor-1.scope", Row(100, "editor"));

            ActionResult result = actions.RestartApp(app);

            Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
            Assert.AreEqual(lang["app_restart_still_running"], result.Error);
            Assert.AreEqual(0, recordedArgvs.Count, "Must not launch when other top-level service has a process");
            Assert.AreEqual(0, result.Stubborn.Count);
        }

        // 3b. Another top-level scope unit (app-editor-2.scope)
        {
            using var fake = new FakeRoot();
            string appSlice = RelativeAppSlice;

            fake.WriteFile(Path.Combine(appSlice, "app-editor-1.scope", "cgroup.procs"), "");
            fake.WriteFile(Path.Combine(appSlice, "app-editor-2.scope", "cgroup.procs"), "503\n");

            var signals = new FakeProcessSignals();
            var clock = new FakeClock();
            var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
            var lang = new Lang("en");

            var recordedArgvs = new List<IReadOnlyList<string>>();
            var actions = new AppActions(
                kill,
                launcher: argv => { recordedArgvs.Add(argv); return null; },
                clock: clock.Now,
                sleep: ms => clock.Advance(ms),
                isAlive: _ => false,
                root: fake.Root,
                lang: lang,
                ownUid: TestUid,
                ownPid: 9999);

            AppEntry app = MakeApp("editor", "Editor", "editor %u", "app-editor-1.scope", Row(100, "editor"));

            ActionResult result = actions.RestartApp(app);

            Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
            Assert.AreEqual(lang["app_restart_still_running"], result.Error);
            Assert.AreEqual(0, recordedArgvs.Count, "Must not launch when other top-level scope has a process");
            Assert.AreEqual(0, result.Stubborn.Count);
        }
    }

    // ------------------------------------------------------------------------
    // 4. RestartApp: an escaped unit (app-google\x2dchrome@x.service) with a PID is detected under app.Id = "google-chrome"
    // ------------------------------------------------------------------------
    [TestMethod]
    public void RestartApp_WhenEscapedUnitHasPid_DetectedUnderUnescapedAppId_ReturnsFailedAndRecorderEmpty()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;

        // Known instance is empty
        fake.WriteFile(Path.Combine(appSlice, "app-google-chrome-1.scope", "cgroup.procs"), "");
        // Other unit uses systemd \x2d escaping for dash: app-google\x2dchrome@instance.service
        fake.WriteFile(Path.Combine(appSlice, @"app-google\x2dchrome@instance.service", "cgroup.procs"), "504\n");

        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        var actions = new AppActions(
            kill,
            launcher: argv => { recordedArgvs.Add(argv); return null; },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false,
            root: fake.Root,
            lang: lang,
            ownUid: TestUid,
            ownPid: 9999);

        // App Id is unescaped "google-chrome"
        AppEntry app = MakeApp("google-chrome", "Google Chrome", "google-chrome %U", "app-google-chrome-1.scope", Row(100, "chrome"));

        ActionResult result = actions.RestartApp(app);

        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
        Assert.AreEqual(lang["app_restart_still_running"], result.Error);
        Assert.AreEqual(0, recordedArgvs.Count, "Must not launch when escaped unit matching app id has a process");
        Assert.AreEqual(0, result.Stubborn.Count);
    }

    // ------------------------------------------------------------------------
    // 5. RestartApp: every cgroup missing or with empty cgroup.procs -> Done, exactly one recorded argv
    // ------------------------------------------------------------------------
    [TestMethod]
    public void RestartApp_WhenEveryCgroupMissingOrEmpty_ReturnsDoneWithExactlyOneRecordedArgv()
    {
        // 5a. Existing cgroup directory with empty cgroup.procs
        {
            using var fake = new FakeRoot();
            string appSlice = RelativeAppSlice;

            fake.WriteFile(Path.Combine(appSlice, "app-clean-1.scope", "cgroup.procs"), "");

            var signals = new FakeProcessSignals();
            var clock = new FakeClock();
            var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
            var lang = new Lang("en");

            var recordedArgvs = new List<IReadOnlyList<string>>();
            var actions = new AppActions(
                kill,
                launcher: argv => { recordedArgvs.Add(argv); return null; },
                clock: clock.Now,
                sleep: ms => clock.Advance(ms),
                isAlive: _ => false,
                root: fake.Root,
                lang: lang,
                ownUid: TestUid,
                ownPid: 9999);

            AppEntry app = MakeApp("clean", "Clean App", "clean-app --started", "app-clean-1.scope", Row(100, "clean-app"));

            ActionResult result = actions.RestartApp(app);

            Assert.AreEqual(ActionOutcome.Done, result.Outcome);
            Assert.IsNull(result.Error);
            Assert.AreEqual(1, recordedArgvs.Count);
            Assert.AreEqual("clean-app", recordedArgvs[0][0]);
            Assert.AreEqual("--started", recordedArgvs[0][1]);
        }

        // 5b. Completely missing cgroup directory (e.g. systemd already cleaned up the unit directory)
        {
            using var fake = new FakeRoot();
            // Do not create app-clean-1.scope at all

            var signals = new FakeProcessSignals();
            var clock = new FakeClock();
            var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
            var lang = new Lang("en");

            var recordedArgvs = new List<IReadOnlyList<string>>();
            var actions = new AppActions(
                kill,
                launcher: argv => { recordedArgvs.Add(argv); return null; },
                clock: clock.Now,
                sleep: ms => clock.Advance(ms),
                isAlive: _ => false,
                root: fake.Root,
                lang: lang,
                ownUid: TestUid,
                ownPid: 9999);

            AppEntry app = MakeApp("clean", "Clean App", "clean-app --started", "app-clean-1.scope", Row(100, "clean-app"));

            ActionResult result = actions.RestartApp(app);

            Assert.AreEqual(ActionOutcome.Done, result.Outcome);
            Assert.IsNull(result.Error);
            Assert.AreEqual(1, recordedArgvs.Count);
            Assert.AreEqual("clean-app", recordedArgvs[0][0]);
            Assert.AreEqual("--started", recordedArgvs[0][1]);
        }
    }

    // ------------------------------------------------------------------------
    // 6. RestartApp: a cgroup directory exists but has no readable cgroup.procs -> Failed, no launch
    // ------------------------------------------------------------------------
    [TestMethod]
    public void RestartApp_WhenCgroupDirectoryExistsWithoutReadableCgroupProcs_ReturnsFailedAndNoLaunch()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;

        // Create cgroup directory without creating cgroup.procs inside it
        fake.CreateDirectory(Path.Combine(appSlice, "app-unreadable-1.scope"));

        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        var actions = new AppActions(
            kill,
            launcher: argv => { recordedArgvs.Add(argv); return null; },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false,
            root: fake.Root,
            lang: lang,
            ownUid: TestUid,
            ownPid: 9999);

        AppEntry app = MakeApp("unreadable", "Unreadable App", "unreadable --start", "app-unreadable-1.scope", Row(100, "unreadable"));

        ActionResult result = actions.RestartApp(app);

        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
        Assert.AreEqual(lang["app_restart_still_running"], result.Error);
        Assert.AreEqual(0, recordedArgvs.Count, "Must not launch when cgroup directory has unreadable/missing cgroup.procs");
        Assert.AreEqual(0, result.Stubborn.Count);
    }

    // ------------------------------------------------------------------------
    // 7. CompleteRestart: a process appears in the cgroup after the snapshot while
    //    the captured PIDs are gone -> Failed, no launch (Amendment 2 core regression)
    // ------------------------------------------------------------------------
    [TestMethod]
    public void CompleteRestart_WhenProcessAppearsInCgroupAfterSnapshotWhileCapturedPidsGone_ReturnsFailedAndNoLaunch()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;

        // A new PID (777) appeared in the cgroup that wasn't in the original snapshot
        fake.WriteFile(Path.Combine(appSlice, "app-runner-1.scope", "cgroup.procs"), "777\n");

        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        var actions = new AppActions(
            kill,
            launcher: argv => { recordedArgvs.Add(argv); return null; },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false, // captured snapshot PID (100) is gone
            root: fake.Root,
            lang: lang,
            ownUid: TestUid,
            ownPid: 9999);

        AppEntry app = MakeApp("runner", "Runner App", "runner --run", "app-runner-1.scope", Row(100, "runner"));

        ActionResult result = actions.CompleteRestart(app);

        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
        Assert.AreEqual(lang["app_restart_still_running"], result.Error);
        Assert.AreEqual(0, recordedArgvs.Count, "CompleteRestart must refuse to launch when cgroup is not empty");
        Assert.AreEqual(0, result.Stubborn.Count);
    }

    // ------------------------------------------------------------------------
    // 8. Alive identity: fake /proc/<pid> has the row's PID and start time but a
    //    different Name -> CompleteRestart returns Failed(app_restart_still_running)
    //    and does not launch; RestartApp returns NeedsForce, no launch.
    //    (Fails on the previous name-comparing implementation).
    // ------------------------------------------------------------------------
    [TestMethod]
    public void AliveIdentity_WhenProcHasMatchingPidAndStartTimeButDifferentName_CompleteRestartFailsAndRestartAppNeedsForce_NoLaunch()
    {
        using var fake = new FakeRoot();

        // Process in /proc/600 has matching PID (600) and start time (54321),
        // but Name has changed to "renamed-worker" (e.g. via exec or comm change).
        fake.WriteFile("proc/600/status", MakeStatus("renamed-worker", uid: TestUid));
        fake.WriteFile("proc/600/stat", MakeStat(600, "renamed-worker", starttime: 54321));
        fake.WriteFile("proc/600/cmdline", "renamed-worker\0");

        var signals = new FakeProcessSignals();
        // Signal target facts also carry the renamed process
        signals.Facts[600] = new ProcessFacts
        {
            Pid = 600,
            Name = "renamed-worker",
            StartTime = 54321,
            Uid = TestUid,
            ParentPid = 1,
            Readable = true,
        };

        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();

        // Construct AppActions with isAlive = null (default implementation uses Alive(fake.Root, target))
        var actions = new AppActions(
            kill,
            launcher: argv => { recordedArgvs.Add(argv); return null; },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: null,
            root: fake.Root,
            lang: lang,
            ownUid: TestUid,
            ownPid: 9999);

        // Snapshot row recorded "original-worker"
        ProcessRow row = Row(600, name: "original-worker", starttime: 54321);
        AppEntry app = MakeApp("worker", "Worker App", "worker --daemon", "app-worker-1.scope", row);

        // 8a. CompleteRestart check:
        // Because PID and StartTime match, Alive considers the process still alive despite Name difference.
        // CompleteRestart must return Failed(app_restart_still_running) and NOT launch.
        ActionResult completeResult = actions.CompleteRestart(app);

        Assert.AreEqual(ActionOutcome.Failed, completeResult.Outcome);
        Assert.AreEqual(lang["app_restart_still_running"], completeResult.Error);
        Assert.AreEqual(0, recordedArgvs.Count, "CompleteRestart must not launch when process identity still lives");

        // 8b. RestartApp check:
        // RestartApp must return NeedsForce and NOT launch.
        ActionResult restartResult = actions.RestartApp(app);

        Assert.AreEqual(ActionOutcome.NeedsForce, restartResult.Outcome);
        Assert.AreEqual(lang["app_restart_still_running"], restartResult.Error);
        Assert.AreEqual(0, recordedArgvs.Count, "RestartApp must not launch when process identity still lives");
    }

    // ------------------------------------------------------------------------
    // 9. Assert blocked fresh checks return Stubborn.Count == 0 (no force tokens)
    // ------------------------------------------------------------------------
    [TestMethod]
    public void BlockedFreshChecks_ReturnStubbornCountZero_NoForceTokens()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;

        // Set up cgroup with an unrecorded PID (888)
        fake.WriteFile(Path.Combine(appSlice, "app-service-1.scope", "cgroup.procs"), "888\n");

        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
        var lang = new Lang("en");

        var actions = new AppActions(
            kill,
            launcher: _ => null,
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false, // captured snapshot members report gone
            root: fake.Root,
            lang: lang,
            ownUid: TestUid,
            ownPid: 9999);

        AppEntry app = MakeApp("service", "Service App", "service-bin", "app-service-1.scope", Row(100, "service-bin"));

        // RestartApp blocked by fresh check
        ActionResult restartResult = actions.RestartApp(app);
        Assert.AreEqual(ActionOutcome.Failed, restartResult.Outcome);
        Assert.IsNotNull(restartResult.Stubborn);
        Assert.AreEqual(0, restartResult.Stubborn.Count, "RestartApp blocked fresh check must return Stubborn.Count == 0");

        // CompleteRestart blocked by fresh check
        ActionResult completeResult = actions.CompleteRestart(app);
        Assert.AreEqual(ActionOutcome.Failed, completeResult.Outcome);
        Assert.IsNotNull(completeResult.Stubborn);
        Assert.AreEqual(0, completeResult.Stubborn.Count, "CompleteRestart blocked fresh check must return Stubborn.Count == 0");
    }

    // ------------------------------------------------------------------------
    // 10. KillFlow.Force after Decline/CancelAll returns Cancelled with no SIGKILL;
    //     ShellModel.ForceKill propagates Cancelled ("only Gone continues").
    // ------------------------------------------------------------------------
    [TestMethod]
    public void KillFlow_Force_AfterDeclineOrCancelAll_ReturnsCancelledWithNoSigkill_AndShellModelForceKillPropagatesCancelled()
    {
        var lang = new Lang("en");

        // --- 10a. KillFlow.Force after Decline ---
        {
            var signals = new FakeProcessSignals();
            signals.Facts[100] = new ProcessFacts
            {
                Pid = 100,
                Name = "proc100",
                StartTime = 1000,
                Uid = TestUid,
                ParentPid = 1,
                Readable = true,
            };

            var clock = new FakeClock();
            var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), lang: lang, ownPid: 9999, ownUid: TestUid);

            KillResult termResult = kill.Terminate(new ProcessTarget(100, 1000, "proc100"));
            Assert.AreEqual(KillState.NeedsForce, termResult.State);
            Assert.IsNotNull(termResult.Pending);
            PendingKill token = termResult.Pending;

            // One SIGTERM sent, no SIGKILL yet
            Assert.AreEqual(1, signals.SignalsSent.Count);
            Assert.AreEqual(100, signals.SignalsSent[0].Pid);
            Assert.IsFalse(signals.SignalsSent[0].Force);

            // User declines the force kill
            kill.Decline(token);
            Assert.IsTrue(token.IsDisposed);

            // Calling Force on declined token returns Cancelled and sends NO SIGKILL
            KillResult forceResult = kill.Force(token);
            Assert.AreEqual(KillState.Cancelled, forceResult.State);
            Assert.AreEqual(1, signals.SignalsSent.Count, "No SIGKILL must be sent for declined token");
            Assert.IsFalse(signals.SignalsSent.Any(s => s.Force));
        }

        // --- 10b. KillFlow.Force after CancelAll ---
        {
            var signals = new FakeProcessSignals();
            signals.Facts[200] = new ProcessFacts
            {
                Pid = 200,
                Name = "proc200",
                StartTime = 1000,
                Uid = TestUid,
                ParentPid = 1,
                Readable = true,
            };

            var clock = new FakeClock();
            var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), lang: lang, ownPid: 9999, ownUid: TestUid);

            KillResult termResult = kill.Terminate(new ProcessTarget(200, 1000, "proc200"));
            Assert.AreEqual(KillState.NeedsForce, termResult.State);
            Assert.IsNotNull(termResult.Pending);
            PendingKill token = termResult.Pending;

            // View closes -> CancelAll
            kill.CancelAll();
            Assert.IsTrue(token.IsDisposed);

            // Calling Force after CancelAll returns Cancelled and sends NO SIGKILL
            KillResult forceResult = kill.Force(token);
            Assert.AreEqual(KillState.Cancelled, forceResult.State);
            Assert.AreEqual(1, signals.SignalsSent.Count, "No SIGKILL must be sent after CancelAll");
            Assert.IsFalse(signals.SignalsSent.Any(s => s.Force));
        }

        // --- 10c. ShellModel.ForceKill propagates Cancelled ---
        {
            var signals = new FakeProcessSignals();
            signals.Facts[300] = new ProcessFacts
            {
                Pid = 300,
                Name = "stubborn300",
                StartTime = 1000,
                Uid = TestUid,
                ParentPid = 1,
                Readable = true,
            };
            signals.Facts[400] = new ProcessFacts
            {
                Pid = 400,
                Name = "stubborn400",
                StartTime = 1000,
                Uid = TestUid,
                ParentPid = 1,
                Readable = true,
            };

            var model = new ShellModel(killTargets: signals, ownUid: TestUid, ownPid: 9999);

            // Token 300: close via CloseApp, decline via ShellModel.CancelKill, then ForceKill
            AppEntry app300 = MakeApp("app300", "App 300", "app300", "app-app300-1.scope", Row(300, "stubborn300"));
            ActionResult closeResult300 = model.AppActions.CloseApp(app300);
            Assert.AreEqual(ActionOutcome.NeedsForce, closeResult300.Outcome);
            Assert.AreEqual(1, closeResult300.Stubborn.Count);
            PendingKill token300 = closeResult300.Stubborn[0];

            model.CancelKill(token300);
            KillResult forceResult300 = model.ForceKill(token300);
            Assert.AreEqual(KillState.Cancelled, forceResult300.State, "ShellModel.ForceKill must propagate Cancelled after CancelKill");

            // Token 400: close via CloseApp, cancel all via ShellModel.CancelKills, then ForceKill
            AppEntry app400 = MakeApp("app400", "App 400", "app400", "app-app400-1.scope", Row(400, "stubborn400"));
            ActionResult closeResult400 = model.AppActions.CloseApp(app400);
            Assert.AreEqual(ActionOutcome.NeedsForce, closeResult400.Outcome);
            Assert.AreEqual(1, closeResult400.Stubborn.Count);
            PendingKill token400 = closeResult400.Stubborn[0];

            model.CancelKills();
            KillResult forceResult400 = model.ForceKill(token400);
            Assert.AreEqual(KillState.Cancelled, forceResult400.State, "ShellModel.ForceKill must propagate Cancelled after CancelKills");

            // Verify that across all signals, no force signal (SIGKILL) was ever sent
            Assert.IsFalse(signals.SignalsSent.Any(s => s.Force), "No SIGKILL must be sent for cancelled operations");
        }
    }

    // ========================================================================
    // Round 4 — Amendment 3 (unlistable cgroups block launch, symlinks skipped)
    // ========================================================================

    // ------------------------------------------------------------------------
    // 1. RestartApp: app.slice replaced by a regular file while known instance
    //    cgroup is empty -> Failed(app_restart_still_running), launcher not
    //    called, Stubborn.Count == 0.
    // ------------------------------------------------------------------------
    [TestMethod]
    public void RestartApp_WhenAppSliceIsRegularFile_ReturnsFailedWithNoLaunchAndStubbornEmpty()
    {
        using var fake = new FakeRoot();
        // Replace app.slice with a regular file
        fake.WriteFile(RelativeAppSlice, "regular file instead of directory\n");

        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        var actions = new AppActions(
            kill,
            launcher: argv => { recordedArgvs.Add(argv); return null; },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false,
            root: fake.Root,
            lang: lang,
            ownUid: TestUid,
            ownPid: 9999);

        AppEntry app = MakeApp("testapp", "Test App", "testapp --run", "app-testapp-1.scope", Row(100, "testapp"));

        ActionResult result = actions.RestartApp(app);

        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
        Assert.AreEqual(lang["app_restart_still_running"], result.Error);
        Assert.AreEqual(0, recordedArgvs.Count, "Launcher must not be called when app.slice is an unlistable regular file");
        Assert.AreEqual(0, result.Stubborn.Count, "Stubborn.Count must be 0 for blocked fresh check");
    }

    // ------------------------------------------------------------------------
    // 2. RestartApp: app.slice directory mode 000 -> same result (guarded with
    //    Posix.Uid != 0, since root ignores permission bits; otherwise Inconclusive).
    // ------------------------------------------------------------------------
    [TestMethod]
    public void RestartApp_WhenAppSliceDirectoryMode000_ReturnsFailedWithNoLaunchAndStubbornEmpty()
    {
        if (Posix.Uid == 0)
        {
            Assert.Inconclusive("Running as root ignores permission bits (mode 000).");
        }

        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;
        // Known instance directory with empty cgroup.procs
        fake.WriteFile(Path.Combine(appSlice, "app-testapp-1.scope", "cgroup.procs"), "");
        string appSliceFull = Path.Combine(fake.Root, appSlice);

        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        var actions = new AppActions(
            kill,
            launcher: argv => { recordedArgvs.Add(argv); return null; },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false,
            root: fake.Root,
            lang: lang,
            ownUid: TestUid,
            ownPid: 9999);

        AppEntry app = MakeApp("testapp", "Test App", "testapp --run", "app-testapp-1.scope", Row(100, "testapp"));

        try
        {
            File.SetUnixFileMode(appSliceFull, UnixFileMode.None);

            ActionResult result = actions.RestartApp(app);

            Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
            Assert.AreEqual(lang["app_restart_still_running"], result.Error);
            Assert.AreEqual(0, recordedArgvs.Count, "Launcher must not be called when app.slice directory mode is 000");
            Assert.AreEqual(0, result.Stubborn.Count, "Stubborn.Count must be 0 for blocked fresh check");
        }
        finally
        {
            File.SetUnixFileMode(appSliceFull, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // ------------------------------------------------------------------------
    // 3. CompleteRestart: same unlistable-slice fixtures -> same failure, no launch.
    // ------------------------------------------------------------------------
    [TestMethod]
    public void CompleteRestart_WhenAppSliceUnlistable_ReturnsFailedAndNoLaunch()
    {
        // 3a. app.slice replaced by a regular file
        {
            using var fake = new FakeRoot();
            fake.WriteFile(RelativeAppSlice, "regular file instead of directory\n");

            var signals = new FakeProcessSignals();
            var clock = new FakeClock();
            var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
            var lang = new Lang("en");

            var recordedArgvs = new List<IReadOnlyList<string>>();
            var actions = new AppActions(
                kill,
                launcher: argv => { recordedArgvs.Add(argv); return null; },
                clock: clock.Now,
                sleep: ms => clock.Advance(ms),
                isAlive: _ => false,
                root: fake.Root,
                lang: lang,
                ownUid: TestUid,
                ownPid: 9999);

            AppEntry app = MakeApp("testapp", "Test App", "testapp --run", "app-testapp-1.scope", Row(100, "testapp"));

            ActionResult result = actions.CompleteRestart(app);

            Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
            Assert.AreEqual(lang["app_restart_still_running"], result.Error);
            Assert.AreEqual(0, recordedArgvs.Count, "CompleteRestart must not launch when app.slice is regular file");
            Assert.AreEqual(0, result.Stubborn.Count);
        }

        // 3b. app.slice directory mode 000
        if (Posix.Uid != 0)
        {
            using var fake = new FakeRoot();
            string appSlice = RelativeAppSlice;
            fake.WriteFile(Path.Combine(appSlice, "app-testapp-1.scope", "cgroup.procs"), "");
            string appSliceFull = Path.Combine(fake.Root, appSlice);

            var signals = new FakeProcessSignals();
            var clock = new FakeClock();
            var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
            var lang = new Lang("en");

            var recordedArgvs = new List<IReadOnlyList<string>>();
            var actions = new AppActions(
                kill,
                launcher: argv => { recordedArgvs.Add(argv); return null; },
                clock: clock.Now,
                sleep: ms => clock.Advance(ms),
                isAlive: _ => false,
                root: fake.Root,
                lang: lang,
                ownUid: TestUid,
                ownPid: 9999);

            AppEntry app = MakeApp("testapp", "Test App", "testapp --run", "app-testapp-1.scope", Row(100, "testapp"));

            try
            {
                File.SetUnixFileMode(appSliceFull, UnixFileMode.None);

                ActionResult result = actions.CompleteRestart(app);

                Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
                Assert.AreEqual(lang["app_restart_still_running"], result.Error);
                Assert.AreEqual(0, recordedArgvs.Count, "CompleteRestart must not launch when app.slice is mode 000");
                Assert.AreEqual(0, result.Stubborn.Count);
            }
            finally
            {
                File.SetUnixFileMode(appSliceFull, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    // ------------------------------------------------------------------------
    // 4. A nested child cgroup with mode 111 (listable path traversable,
    //    cgroup.procs readable, but EnumerateDirectories fails) -> Failed,
    //    no launch; proves the child listing failure path specifically.
    // ------------------------------------------------------------------------
    [TestMethod]
    public void RestartApp_WhenNestedChildCgroupMode111_ChildListingFails_ReturnsFailedAndNoLaunch()
    {
        if (Posix.Uid == 0)
        {
            Assert.Inconclusive("Running as root ignores permission bits (mode 111).");
        }

        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;

        // Top level cgroup.procs is empty
        fake.WriteFile(Path.Combine(appSlice, "app-testapp-1.scope", "cgroup.procs"), "");

        // Nested child directory has empty cgroup.procs
        string childRelative = Path.Combine(appSlice, "app-testapp-1.scope", "child.scope");
        string childFull = fake.CreateDirectory(childRelative);
        string childProcsFull = fake.WriteFile(Path.Combine(childRelative, "cgroup.procs"), "");
        File.SetUnixFileMode(childProcsFull, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        var actions = new AppActions(
            kill,
            launcher: argv => { recordedArgvs.Add(argv); return null; },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false,
            root: fake.Root,
            lang: lang,
            ownUid: TestUid,
            ownPid: 9999);

        AppEntry app = MakeApp("testapp", "Test App", "testapp --run", "app-testapp-1.scope", Row(100, "testapp"));

        try
        {
            // Mode 111 (--x--x--x): traversable so cgroup.procs is readable, but directory cannot be enumerated (no r)
            File.SetUnixFileMode(childFull, UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

            ActionResult result = actions.RestartApp(app);

            Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
            Assert.AreEqual(lang["app_restart_still_running"], result.Error);
            Assert.AreEqual(0, recordedArgvs.Count, "Must not launch when child cgroup cannot be listed (mode 111)");
            Assert.AreEqual(0, result.Stubborn.Count);
        }
        finally
        {
            File.SetUnixFileMode(childFull, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // ------------------------------------------------------------------------
    // 5. Symlink skipped, not followed: app.slice/app-<id>-2.scope is a symlink to
    //    a real directory containing a live cgroup.procs -> Done, exactly one launch.
    // ------------------------------------------------------------------------
    [TestMethod]
    public void RestartApp_WhenOtherTopLevelUnitIsSymlinkToLiveCgroup_SymlinkIsSkipped_ReturnsDoneWithOneLaunch()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;

        // Known instance is empty
        fake.WriteFile(Path.Combine(appSlice, "app-testapp-1.scope", "cgroup.procs"), "");

        // Target directory containing a live process in cgroup.procs
        string targetDir = fake.CreateDirectory("other-live-target");
        fake.WriteFile("other-live-target/cgroup.procs", "999\n");

        // Symlink app.slice/app-testapp-2.scope -> targetDir
        fake.CreateDirectorySymlink(Path.Combine(appSlice, "app-testapp-2.scope"), targetDir);

        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        var actions = new AppActions(
            kill,
            launcher: argv => { recordedArgvs.Add(argv); return null; },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false,
            root: fake.Root,
            lang: lang,
            ownUid: TestUid,
            ownPid: 9999);

        AppEntry app = MakeApp("testapp", "Test App", "testapp --run", "app-testapp-1.scope", Row(100, "testapp"));

        ActionResult result = actions.RestartApp(app);

        Assert.AreEqual(ActionOutcome.Done, result.Outcome);
        Assert.IsNull(result.Error);
        Assert.AreEqual(1, recordedArgvs.Count, "Symlinked unit in slice must be skipped without following, allowing launch");
        Assert.AreEqual("testapp", recordedArgvs[0][0]);
    }

    // ------------------------------------------------------------------------
    // 6. Same for a symlinked nested child inside the known instance cgroup -> Done, one launch.
    // ------------------------------------------------------------------------
    [TestMethod]
    public void RestartApp_WhenNestedChildIsSymlinkToLiveCgroup_SymlinkIsSkipped_ReturnsDoneWithOneLaunch()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;

        // Known instance cgroup.procs is empty
        fake.WriteFile(Path.Combine(appSlice, "app-testapp-1.scope", "cgroup.procs"), "");

        // Target directory containing a live process
        string targetDir = fake.CreateDirectory("nested-live-target");
        fake.WriteFile("nested-live-target/cgroup.procs", "999\n");

        // Symlinked nested child inside known instance cgroup: app.slice/app-testapp-1.scope/nested.scope -> targetDir
        fake.CreateDirectorySymlink(Path.Combine(appSlice, "app-testapp-1.scope", "nested.scope"), targetDir);

        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        var actions = new AppActions(
            kill,
            launcher: argv => { recordedArgvs.Add(argv); return null; },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false,
            root: fake.Root,
            lang: lang,
            ownUid: TestUid,
            ownPid: 9999);

        AppEntry app = MakeApp("testapp", "Test App", "testapp --run", "app-testapp-1.scope", Row(100, "testapp"));

        ActionResult result = actions.RestartApp(app);

        Assert.AreEqual(ActionOutcome.Done, result.Outcome);
        Assert.IsNull(result.Error);
        Assert.AreEqual(1, recordedArgvs.Count, "Symlinked child directory must be skipped without following, allowing launch");
    }

    // ------------------------------------------------------------------------
    // 7. Symlinked instance path (app.slice/app-<id>-1.scope -> real dir) -> Failed,
    //    no launch (documented conservative choice).
    // ------------------------------------------------------------------------
    [TestMethod]
    public void RestartApp_WhenKnownInstancePathIsSymlink_ReturnsFailedAndNoLaunch()
    {
        using var fake = new FakeRoot();
        string appSlice = RelativeAppSlice;

        // Target directory has empty cgroup.procs
        string targetDir = fake.CreateDirectory("instance-target");
        fake.WriteFile("instance-target/cgroup.procs", "");

        // Known instance path ITSELF is a symlink: app.slice/app-testapp-1.scope -> targetDir
        fake.CreateDirectorySymlink(Path.Combine(appSlice, "app-testapp-1.scope"), targetDir);

        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        var actions = new AppActions(
            kill,
            launcher: argv => { recordedArgvs.Add(argv); return null; },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false,
            root: fake.Root,
            lang: lang,
            ownUid: TestUid,
            ownPid: 9999);

        AppEntry app = MakeApp("testapp", "Test App", "testapp --run", "app-testapp-1.scope", Row(100, "testapp"));

        ActionResult result = actions.RestartApp(app);

        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
        Assert.AreEqual(lang["app_restart_still_running"], result.Error);
        Assert.AreEqual(0, recordedArgvs.Count, "Symlinked instance path must be rejected conservatively with no launch");
        Assert.AreEqual(0, result.Stubborn.Count);
    }

    // ------------------------------------------------------------------------
    // 8. Regression guard: with the whole app.slice missing, still Done + one launch.
    // ------------------------------------------------------------------------
    [TestMethod]
    public void RestartApp_WhenWholeAppSliceMissing_RegressionGuard_ReturnsDoneWithOneLaunch()
    {
        using var fake = new FakeRoot();
        // Do not create RelativeAppSlice at all

        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), ownPid: 9999, ownUid: TestUid);
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        var actions = new AppActions(
            kill,
            launcher: argv => { recordedArgvs.Add(argv); return null; },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false,
            root: fake.Root,
            lang: lang,
            ownUid: TestUid,
            ownPid: 9999);

        AppEntry app = MakeApp("clean", "Clean App", "clean-app --started", "app-clean-1.scope", Row(100, "clean-app"));

        ActionResult result = actions.RestartApp(app);

        Assert.AreEqual(ActionOutcome.Done, result.Outcome);
        Assert.IsNull(result.Error);
        Assert.AreEqual(1, recordedArgvs.Count);
        Assert.AreEqual("clean-app", recordedArgvs[0][0]);
    }

    // ------------------------------------------------------------------------
    // 9. SysFs.Directories still returns an empty (non-null) list for an unreadable
    //    directory — i.e. it was not changed.
    // ------------------------------------------------------------------------
    [TestMethod]
    public void SysFs_Directories_ReturnsEmptyNonNullList_ForUnreadableDirectory()
    {
        using var fake = new FakeRoot();

        // 9a. Non-existent path returns empty (non-null)
        IReadOnlyList<string> missing = SysFs.Directories(fake.Root, "nonexistent");
        Assert.IsNotNull(missing);
        Assert.AreEqual(0, missing.Count);

        // 9b. Regular file returns empty (non-null)
        string file = fake.WriteFile("regular.txt", "content");
        IReadOnlyList<string> regularFile = SysFs.Directories(file);
        Assert.IsNotNull(regularFile);
        Assert.AreEqual(0, regularFile.Count);

        // 9c. Directory with mode 000 returns empty (non-null)
        if (Posix.Uid != 0)
        {
            string noPermDir = fake.CreateDirectory("noperm");
            try
            {
                File.SetUnixFileMode(noPermDir, UnixFileMode.None);
                IReadOnlyList<string> noPerm = SysFs.Directories(noPermDir);
                Assert.IsNotNull(noPerm);
                Assert.AreEqual(0, noPerm.Count);
            }
            finally
            {
                File.SetUnixFileMode(noPermDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }
}
