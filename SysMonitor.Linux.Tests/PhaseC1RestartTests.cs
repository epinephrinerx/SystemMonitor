using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

/// <summary>
/// Round 2 tests for R-030 Phase C Part 1: Amendment 1 item 1 (AppActions.CompleteRestart).
/// Tests all model-level behavior using the fake launcher, clock, sleep, and isAlive seams in AppActions.
/// </summary>
[TestClass]
public sealed class PhaseC1RestartTests
{
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

    private static ProcessRow Row(int pid, string name = "proc", ulong? starttime = 1000) =>
        new()
        {
            Pid = pid,
            StartTime = starttime,
            Name = name,
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

    // ------------------------------------------------------------------------
    // 1. CompleteRestart launches exactly once when every member is gone
    // ------------------------------------------------------------------------
    [TestMethod]
    public void CompleteRestart_WhenEveryMemberIsGone_LaunchesExactlyOnceWithRecordedArgvAndReturnsDone()
    {
        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms));
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        var actions = new AppActions(
            kill,
            launcher: argv =>
            {
                recordedArgvs.Add(argv);
                return null;
            },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false, // every member is gone immediately
            lang: lang);

        AppEntry app = MakeApp("gedit", "gedit %U --new-window", Row(100, "gedit"));

        ActionResult result = actions.CompleteRestart(app);

        Assert.AreEqual(ActionOutcome.Done, result.Outcome);
        Assert.IsNull(result.Error);
        Assert.AreEqual(0, result.Stubborn.Count);
        Assert.AreEqual(1, recordedArgvs.Count);
        Assert.AreEqual(2, recordedArgvs[0].Count);
        Assert.AreEqual("gedit", recordedArgvs[0][0]);
        Assert.AreEqual("--new-window", recordedArgvs[0][1]);
    }

    // ------------------------------------------------------------------------
    // 2. CompleteRestart does not launch while a member reports alive at deadline
    // ------------------------------------------------------------------------
    [TestMethod]
    public void CompleteRestart_WhenMemberAliveAtDeadline_FailsWithStillRunningAndRecorderEmptyAndClockAdvancedByWaitMs()
    {
        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms));
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        var actions = new AppActions(
            kill,
            launcher: argv =>
            {
                recordedArgvs.Add(argv);
                return null;
            },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => true, // member reports alive through deadline
            lang: lang);

        AppEntry app = MakeApp("stubborn", "stubborn-app --stay", Row(200, "stubborn"));

        long startTicks = clock.Ticks;
        ActionResult result = actions.CompleteRestart(app);

        Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
        Assert.AreEqual(lang["app_restart_still_running"], result.Error);
        Assert.AreEqual(0, recordedArgvs.Count, "Launcher must not be called when process is still running");
        long elapsed = clock.Ticks - startTicks;
        Assert.IsTrue(elapsed >= AppActions.RestartWaitMs,
            $"Fake clock must advance by at least RestartWaitMs (5000 ms), but elapsed was {elapsed} ms");
    }

    // ------------------------------------------------------------------------
    // 3. CompleteRestart waits for a member that disappears mid-wait and launches once
    // ------------------------------------------------------------------------
    [TestMethod]
    public void CompleteRestart_WaitsForMemberDisappearingMidWait_AndThenLaunchesOnce()
    {
        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms));
        var lang = new Lang("en");

        var recordedArgvs = new List<IReadOnlyList<string>>();
        long startTicks = clock.Ticks;
        // Alive until clock advances by 300 ms (mid-wait), then false
        var actions = new AppActions(
            kill,
            launcher: argv =>
            {
                recordedArgvs.Add(argv);
                return null;
            },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => clock.Ticks < startTicks + 300,
            lang: lang);

        AppEntry app = MakeApp("slow-exit", "slow-exit --daemon", Row(300, "slow-exit"));

        ActionResult result = actions.CompleteRestart(app);

        Assert.AreEqual(ActionOutcome.Done, result.Outcome);
        Assert.IsNull(result.Error);
        Assert.AreEqual(1, recordedArgvs.Count, "Must launch once after member exits mid-wait");
        long elapsed = clock.Ticks - startTicks;
        Assert.IsTrue(elapsed >= 300, $"Must have waited for exit, elapsed: {elapsed} ms");
        Assert.IsTrue(elapsed < AppActions.RestartWaitMs,
            $"Must exit wait early once member disappears, elapsed: {elapsed} ms < {AppActions.RestartWaitMs} ms");
    }

    // ------------------------------------------------------------------------
    // 4. CompleteRestart refuses an unusable Exec without calling launcher or sleep
    // ------------------------------------------------------------------------
    [TestMethod]
    public void CompleteRestart_RefusesUnusableExec_WithUnavailableAndNeverCallsLauncherOrSleep()
    {
        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms));
        var lang = new Lang("en");

        string?[] unusableExecs =
        {
            null,
            "",
            "   ",
            "%u %f",
            "/bin/echo \"unclosed quote",
            "/bin/echo \"foo\"bar",
        };

        foreach (string? exec in unusableExecs)
        {
            bool launcherCalled = false;
            bool sleepCalled = false;
            bool isAliveCalled = false;

            var actions = new AppActions(
                kill,
                launcher: _ => { launcherCalled = true; return null; },
                clock: clock.Now,
                sleep: ms => { sleepCalled = true; clock.Advance(ms); },
                isAlive: _ => { isAliveCalled = true; return false; },
                lang: lang);

            AppEntry app = MakeApp("unusable", exec, Row(400, "unusable"));

            ActionResult result = actions.CompleteRestart(app);

            Assert.AreEqual(ActionOutcome.Failed, result.Outcome, $"Exec '{exec}' must fail");
            Assert.AreEqual(lang["app_restart_unavailable"], result.Error, $"Exec '{exec}' error text mismatch");
            Assert.IsFalse(launcherCalled, $"Launcher must not be called for unusable Exec '{exec}'");
            Assert.IsFalse(sleepCalled, $"Sleep must not be called for unusable Exec '{exec}'");
            Assert.IsFalse(isAliveCalled, $"isAlive must not be called for unusable Exec '{exec}'");
        }
    }

    // ------------------------------------------------------------------------
    // 5. CompleteRestart with an app having no instances is refused
    // ------------------------------------------------------------------------
    [TestMethod]
    public void CompleteRestart_WithAppHavingNoInstancesOrProcesses_RefusesWithUnavailableAndNeverCallsLauncherOrSleep()
    {
        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms));
        var lang = new Lang("en");

        AppEntry[] appsWithoutMembers =
        {
            // No instances at all
            new()
            {
                Id = "no-inst",
                Name = "No Instances",
                Exec = "valid-command --run",
                Instances = Array.Empty<AppInstance>(),
            },
            // Instance exists but has empty processes list
            new()
            {
                Id = "empty-inst",
                Name = "Empty Instances",
                Exec = "valid-command --run",
                Instances = new[]
                {
                    new AppInstance { Unit = "app-empty-1.scope", Processes = Array.Empty<ProcessRow>() }
                },
            },
        };

        foreach (AppEntry app in appsWithoutMembers)
        {
            bool launcherCalled = false;
            bool sleepCalled = false;

            var actions = new AppActions(
                kill,
                launcher: _ => { launcherCalled = true; return null; },
                clock: clock.Now,
                sleep: ms => { sleepCalled = true; clock.Advance(ms); },
                isAlive: _ => false,
                lang: lang);

            ActionResult result = actions.CompleteRestart(app);

            Assert.AreEqual(ActionOutcome.Failed, result.Outcome);
            Assert.AreEqual(lang["app_restart_unavailable"], result.Error);
            Assert.IsFalse(launcherCalled, "Launcher must not be called when app has no members");
            Assert.IsFalse(sleepCalled, "Sleep must not be called when app has no members");
        }
    }

    // ------------------------------------------------------------------------
    // 6. Launcher error returned as Failed(<error>) and not retried; second call
    //    is not guarded at model level (view busy flag prevents it)
    // ------------------------------------------------------------------------
    [TestMethod]
    public void CompleteRestart_WhenLauncherReturnsError_ReturnsFailedAndIsNotRetried_ViewBusyFlagGuardsSecondCall()
    {
        var signals = new FakeProcessSignals();
        var clock = new FakeClock();
        var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms));
        var lang = new Lang("en");

        int launchAttempts = 0;
        const string launcherError = "Failed to execute: permission denied";

        var actions = new AppActions(
            kill,
            launcher: _ =>
            {
                launchAttempts++;
                return launcherError;
            },
            clock: clock.Now,
            sleep: ms => clock.Advance(ms),
            isAlive: _ => false,
            lang: lang);

        AppEntry app = MakeApp("fail-app", "fail-app --launch", Row(600, "fail-app"));

        // First call fails with launcher error and is NOT retried internally
        ActionResult firstResult = actions.CompleteRestart(app);

        Assert.AreEqual(ActionOutcome.Failed, firstResult.Outcome);
        Assert.AreEqual(launcherError, firstResult.Error);
        Assert.AreEqual(1, launchAttempts, "Launcher error is not retried inside CompleteRestart");

        // A second call to CompleteRestart is NOT guarded at model level;
        // per architectural spec, the view's busy flag is what prevents multiple simultaneous calls in the UI.
        ActionResult secondResult = actions.CompleteRestart(app);

        Assert.AreEqual(ActionOutcome.Failed, secondResult.Outcome);
        Assert.AreEqual(launcherError, secondResult.Error);
        Assert.AreEqual(2, launchAttempts,
            "Model has no latch against second call; AppsView busy flag is documented to prevent duplicate UI clicks");
    }

    // ------------------------------------------------------------------------
    // 7. Sequence at model level: RestartApp -> NeedsForce token -> ForceKill -> CompleteRestart
    //    records exactly one launch; the declined branch has no launch.
    // ------------------------------------------------------------------------
    [TestMethod]
    public void ModelSequence_RestartApp_NeedsForce_ForceKill_CompleteRestart_LaunchesOnce_AndDeclinedBranchHasNoLaunch()
    {
        var lang = new Lang("en");

        // Note: The UI for confirming or declining the force question lives in AppsView (SysMonitor.Linux.App),
        // which cannot be referenced from the test project. This test verifies the entire sequence at model level.

        // --- Sequence A: Confirmed branch (RestartApp -> NeedsForce -> ForceKill -> CompleteRestart) ---
        {
            var signals = new FakeProcessSignals();
            signals.Facts[700] = new ProcessFacts
            {
                Pid = 700,
                Name = "stubborn-app",
                StartTime = 1000,
                Uid = 1000,
                ParentPid = 1,
                Readable = true,
            };

            var clock = new FakeClock();
            bool processAlive = true;

            // When Force (SIGKILL) is received, the process dies
            signals.OnSignal = (pid, force) =>
            {
                if (force)
                {
                    processAlive = false;
                    signals.Facts.Remove(pid);
                }
                return null;
            };

            var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), lang: lang, ownPid: 9999, ownUid: 1000);
            var launchedArgvs = new List<IReadOnlyList<string>>();

            var actions = new AppActions(
                kill,
                launcher: argv =>
                {
                    launchedArgvs.Add(argv);
                    return null;
                },
                clock: clock.Now,
                sleep: ms => clock.Advance(ms),
                isAlive: target => processAlive,
                lang: lang);

            AppEntry app = MakeApp("stubborn", "stubborn-app --work", Row(700, "stubborn-app"));

            // 1. RestartApp is initiated
            ActionResult restartResult = actions.RestartApp(app);

            Assert.AreEqual(ActionOutcome.NeedsForce, restartResult.Outcome);
            Assert.AreEqual(lang["app_restart_still_running"], restartResult.Error);
            Assert.AreEqual(1, restartResult.Stubborn.Count);
            Assert.AreEqual(0, launchedArgvs.Count, "Launcher must NOT be called before force confirmation");

            PendingKill token = restartResult.Stubborn[0];
            Assert.AreEqual(700, token.Target.Pid);
            Assert.IsFalse(token.IsDisposed);

            // 2. User confirms force kill in UI -> view calls Force on token
            KillResult forceResult = kill.Force(token);
            Assert.AreEqual(KillState.Gone, forceResult.State);
            Assert.IsTrue(token.IsDisposed);
            Assert.IsFalse(processAlive);

            // 3. View calls CompleteRestart
            ActionResult completeResult = actions.CompleteRestart(app);

            Assert.AreEqual(ActionOutcome.Done, completeResult.Outcome);
            Assert.IsNull(completeResult.Error);
            Assert.AreEqual(1, launchedArgvs.Count, "Exactly one launch recorded after CompleteRestart");
            Assert.AreEqual("stubborn-app", launchedArgvs[0][0]);
            Assert.AreEqual("--work", launchedArgvs[0][1]);
        }

        // --- Sequence B: Declined branch (RestartApp -> NeedsForce -> Decline -> no CompleteRestart) ---
        {
            var signals = new FakeProcessSignals();
            signals.Facts[800] = new ProcessFacts
            {
                Pid = 800,
                Name = "declined-app",
                StartTime = 1000,
                Uid = 1000,
                ParentPid = 1,
                Readable = true,
            };

            var clock = new FakeClock();
            bool processAlive = true;

            var kill = new KillFlow(signals, clock.Now, ms => clock.Advance(ms), lang: lang, ownPid: 9999, ownUid: 1000);
            var launchedArgvs = new List<IReadOnlyList<string>>();

            var actions = new AppActions(
                kill,
                launcher: argv =>
                {
                    launchedArgvs.Add(argv);
                    return null;
                },
                clock: clock.Now,
                sleep: ms => clock.Advance(ms),
                isAlive: target => processAlive,
                lang: lang);

            AppEntry app = MakeApp("declined", "declined-app --work", Row(800, "declined-app"));

            // 1. RestartApp is initiated
            ActionResult restartResult = actions.RestartApp(app);

            Assert.AreEqual(ActionOutcome.NeedsForce, restartResult.Outcome);
            Assert.AreEqual(1, restartResult.Stubborn.Count);
            Assert.AreEqual(0, launchedArgvs.Count, "Launcher must NOT be called before force confirmation");

            PendingKill token = restartResult.Stubborn[0];
            Assert.AreEqual(800, token.Target.Pid);

            // 2. User declines in UI -> view calls Decline / CancelKill
            kill.Decline(token);
            Assert.IsTrue(token.IsDisposed, "Declined token must be disposed");

            // 3. On decline, the UI does NOT call CompleteRestart
            // Result: zero launches recorded
            Assert.AreEqual(0, launchedArgvs.Count, "Declined branch must have no launches");

            // Verify that attempting to force a declined token fails
            KillResult forceAttempt = kill.Force(token);
            Assert.AreEqual(KillState.Cancelled, forceAttempt.State);
            Assert.AreEqual(0, launchedArgvs.Count);
        }
    }
}
