using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class KillFlowTests
{
    private sealed class FakeTargets : IProcessSignals
    {
        public Dictionary<int, string> Running { get; } = new();
        public Dictionary<int, ProcessFacts> CustomFacts { get; } = new();
        public List<(int Pid, bool Force)> SignalsSent { get; } = new();
        public Func<int, bool, string?>? OnSignal { get; set; }
        public Func<int, PinnedResult>? OnPin { get; set; }
        public Dictionary<int, int> ExitAfterMs { get; } = new();
        public List<int> PinCalls { get; } = new();
        public List<PinnedProcess> PinnedInstances { get; } = new();

        public FakeClock Clock { get; } = new();

        public ProcessFacts? Read(int pid) => Facts(pid);

        /// <summary>Still used by the assertions that check the fake's state.</summary>
        public bool IsAlive(int pid) => Running.ContainsKey(pid) || CustomFacts.ContainsKey(pid);

        public PinnedResult Pin(int pid)
        {
            PinCalls.Add(pid);
            if (OnPin is not null)
            {
                return OnPin(pid);
            }
            var pinned = new PinnedProcess(this, pid);
            PinnedInstances.Add(pinned);
            return PinnedResult.Pinned(pinned);
        }

        public string? Signal(int pid, bool force) => Send(pid, force);

        private ProcessFacts? Facts(int pid)
        {
            if (CustomFacts.TryGetValue(pid, out ProcessFacts? custom))
            {
                return custom;
            }
            return Running.TryGetValue(pid, out string? name)
                ? new ProcessFacts
                {
                    Pid = pid,
                    Name = name,
                    // A real start time: a target without one is never
                    // signalled, so a fixture that used 0 could not be killed.
                    StartTime = 1000,
                    Uid = 1000,
                    ParentPid = 1,
                    Readable = true,
                }
                : null;
        }

        private string? Send(int pid, bool force)
        {
            SignalsSent.Add((pid, force));

            if (OnSignal is not null)
            {
                string? err = OnSignal(pid, force);
                if (err is not null)
                {
                    return err;
                }
            }

            if (force)
            {
                Running.Remove(pid);
                CustomFacts.Remove(pid);
                return null;
            }

            if (ExitAfterMs.TryGetValue(pid, out int delayMs) && delayMs <= 0)
            {
                Running.Remove(pid);
                CustomFacts.Remove(pid);
            }

            return null;
        }

        public sealed class PinnedProcess : IPinnedProcess
        {
            private readonly FakeTargets _owner;
            private readonly int _pid;

            public PinnedProcess(FakeTargets owner, int pid)
            {
                _owner = owner;
                _pid = pid;
            }

            public int Pid => _pid;
            public int DisposeCount { get; private set; }
            public List<bool> Signals { get; } = new();

            public ProcessFacts? Read() => _owner.Facts(_pid);

            public string? Signal(bool force)
            {
                Signals.Add(force);
                return _owner.Send(_pid, force);
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        public void Step(int ms)
        {
            Clock.Advance(ms);
            // Check if any process should exit after delay
            foreach ((int pid, int delay) in ExitAfterMs)
            {
                if (Clock.Ticks >= delay)
                {
                    Running.Remove(pid);
                    CustomFacts.Remove(pid);
                }
            }
        }
    }

    [TestMethod]
    public void Terminate_SendsSigtermFirst_AndReturnsGoneIfProcessExitsPromptly()
    {
        var fake = new FakeTargets();
        fake.Running[100] = "gedit";
        // Process exits 200 ms after SIGTERM
        fake.ExitAfterMs[100] = 1200; // start clock is 1000, exits at 1200

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);

        KillResult result = flow.Terminate(new ProcessTarget(100, 1000, "gedit"));

        Assert.AreEqual(KillState.Gone, result.State);
        Assert.IsNull(result.Error);

        // Verify SIGTERM was sent (force = false)
        Assert.AreEqual(1, fake.SignalsSent.Count);
        Assert.AreEqual(100, fake.SignalsSent[0].Pid);
        Assert.IsFalse(fake.SignalsSent[0].Force, "Initial signal must be SIGTERM (force = false)");
        Assert.AreEqual(1, fake.PinCalls.Count);
        Assert.AreEqual(1, fake.PinnedInstances[0].DisposeCount, "Dispose must be called once when process exits promptly");
    }

    [TestMethod]
    public void Terminate_WhenProcessDoesNotExitWithin3Seconds_ReturnsNeedsForce()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "stubborn-app";
        // Never exits voluntarily (or exits at 10000ms > 3000ms deadline)
        fake.ExitAfterMs[200] = 10000;

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);

        KillResult result = flow.Terminate(new ProcessTarget(200, 1000, "stubborn-app"));

        Assert.AreEqual(KillState.NeedsForce, result.State);
        Assert.IsNull(result.Error);
        Assert.IsTrue(fake.IsAlive(200));

        // Exactly 1 signal sent so far (SIGTERM)
        Assert.AreEqual(1, fake.SignalsSent.Count);
        Assert.IsFalse(fake.SignalsSent[0].Force);

        // Clock advanced at least 3000 ms during wait
        Assert.IsTrue(fake.Clock.Ticks >= 4000);
        Assert.AreEqual(1, fake.PinCalls.Count);
        Assert.AreEqual(0, fake.PinnedInstances[0].DisposeCount, "Handle must remain open while waiting for force confirmation");
    }

    [TestMethod]
    public void Force_SendsSigkillDirectly_AndProcessIsGone()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "stubborn-app";
        fake.ExitAfterMs[200] = 10000; // survives the 3 s SIGTERM wait

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);

        // Terminate first: SIGTERM's handle is owned by the returned token.
        KillResult termResult = flow.Terminate(new ProcessTarget(200, 1000, "stubborn-app"));
        Assert.AreEqual(KillState.NeedsForce, termResult.State);

        // User confirmed prompt -> Force through that token
        KillResult result = flow.Force(termResult.Pending);

        Assert.AreEqual(KillState.Gone, result.State);
        Assert.IsNull(result.Error);
        Assert.IsFalse(fake.IsAlive(200));

        // Last signal sent with force = true (SIGKILL)
        Assert.AreEqual(2, fake.SignalsSent.Count);
        Assert.AreEqual(200, fake.SignalsSent[1].Pid);
        Assert.IsTrue(fake.SignalsSent[1].Force, "Force must send SIGKILL (force = true)");
        Assert.AreEqual(1, fake.PinCalls.Count);
        Assert.AreEqual(1, fake.PinnedInstances[0].DisposeCount, "The token owns the handle and disposes it once");
    }

    [TestMethod]
    public void Terminate_NameRecheckAborts_WhenPidNameChanged_WithoutSignalling()
    {
        var fake = new FakeTargets();
        var lang = new Lang("en");

        // PID 300 was originally "calculator", but when kill is triggered it has been recycled into "python"
        fake.Running[300] = "python";

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, lang);

        KillResult result = flow.Terminate(new ProcessTarget(300, 1000, "calculator"));

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual("PID 300 is now \"python\"", result.Error);

        // Crucial safety check: NO signal was sent to the recycled PID!
        Assert.AreEqual(0, fake.SignalsSent.Count, "Must not send signal when process name changed");
        Assert.IsTrue(fake.IsAlive(300));
    }

    [TestMethod]
    public void Terminate_NameRecheckAborts_InThai_WhenPidNameChanged()
    {
        var fake = new FakeTargets();
        var lang = new Lang("th");

        fake.Running[300] = "python";

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, lang);

        KillResult result = flow.Terminate(new ProcessTarget(300, 1000, "calculator"));

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual("PID 300 เปลี่ยนเป็น \"python\" แล้ว", result.Error);
        Assert.AreEqual(0, fake.SignalsSent.Count);
    }

    [TestMethod]
    public void Terminate_WhenProcessAlreadyGone_ReturnsGoneWithoutSignalling()
    {
        var fake = new FakeTargets();
        // PID 400 is not in Running

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);

        KillResult result = flow.Terminate(new ProcessTarget(400, 1000, "old-proc"));

        Assert.AreEqual(KillState.Gone, result.State);
        Assert.IsNull(result.Error);
        Assert.AreEqual(0, fake.SignalsSent.Count);
    }

    [TestMethod]
    public void Terminate_WhenSignalReturnsError_ReturnsFailed()
    {
        var fake = new FakeTargets();
        fake.Running[500] = "root-proc";
        fake.OnSignal = (pid, force) => "Operation not permitted"; // EPERM

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);

        KillResult result = flow.Terminate(new ProcessTarget(500, 1000, "root-proc"));

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual("Operation not permitted", result.Error);
    }

    [TestMethod]
    public void Force_WhenSignalReturnsError_ReturnsFailed()
    {
        var fake = new FakeTargets();
        fake.Running[500] = "root-proc";
        fake.ExitAfterMs[500] = 10000; // SIGTERM accepted, process stays
        fake.OnSignal = (pid, force) => force ? "Permission denied" : null;

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);

        KillResult termResult = flow.Terminate(new ProcessTarget(500, 1000, "root-proc"));
        Assert.AreEqual(KillState.NeedsForce, termResult.State);

        KillResult result = flow.Force(termResult.Pending);

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual("Permission denied", result.Error);
    }

    [TestMethod]
    public void TerminateAll_ProcessesMultipleTargets_AndReportsStubbornPids()
    {
        var fake = new FakeTargets();
        fake.Running[10] = "worker-1"; // exits quickly
        fake.ExitAfterMs[10] = 1100;

        fake.Running[20] = "worker-2"; // stubborn
        fake.ExitAfterMs[20] = 9000;

        fake.Running[30] = "worker-3"; // name changed
        fake.Running[30] = "recycled";

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);

        var targets = new[]
        {
            new ProcessTarget(10, 1000, "worker-1"),
            new ProcessTarget(20, 1000, "worker-2"),
            new ProcessTarget(30, 1000, "worker-3"),
        };

        (IReadOnlyList<PendingKill> stubborn, string? error) = flow.TerminateAll(targets);

        // worker-2 is stubborn and needs force
        CollectionAssert.AreEqual(
            new[] { 20 },
            (System.Collections.ICollection)stubborn.Select(t => t.Target.Pid).ToArray());

        // worker-3 failed due to changed name
        Assert.IsNotNull(error);
        StringAssert.Contains(error, "PID 30 is now \"recycled\"");
    }

    [TestMethod]
    public void Terminate_RecheckAborts_WhenStartTimeDiffers_EvenWithSameName_WithoutSignalling()
    {
        var fake = new FakeTargets();
        var lang = new Lang("en");

        // PID 600 was captured with StartTime = 1000 and Name = "worker"
        // But the PID has been recycled by another process with the SAME Name "worker" and StartTime = 2000
        fake.CustomFacts[600] = new ProcessFacts
        {
            Pid = 600,
            Name = "worker",
            StartTime = 2000,
            Uid = 1000,
            ParentPid = 1,
            Readable = true,
        };

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, lang);

        KillResult result = flow.Terminate(new ProcessTarget(600, StartTime: 1000, "worker"));

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual("PID 600 is now \"worker\"", result.Error);
        Assert.AreEqual(0, fake.SignalsSent.Count, "Must not send signal when StartTime differs (PID reused)");
    }

    [TestMethod]
    public void Force_RecheckAborts_WhenStartTimeDiffers_EvenWithSameName_WithoutSignalling()
    {
        var fake = new FakeTargets();
        var lang = new Lang("en");

        fake.CustomFacts[600] = new ProcessFacts
        {
            Pid = 600,
            Name = "worker",
            StartTime = 1000,
            Uid = 1000,
            ParentPid = 1,
            Readable = true,
        };

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, lang);

        KillResult termResult = flow.Terminate(new ProcessTarget(600, StartTime: 1000, "worker"));
        Assert.AreEqual(KillState.NeedsForce, termResult.State);

        // While the force question is open the PID is recycled: same name, new start time
        fake.CustomFacts[600] = new ProcessFacts
        {
            Pid = 600,
            Name = "worker",
            StartTime = 9999,
            Uid = 1000,
            ParentPid = 1,
            Readable = true,
        };

        KillResult result = flow.Force(termResult.Pending);

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual("PID 600 is now \"worker\"", result.Error);
        Assert.IsFalse(
            fake.SignalsSent.Any(s => s.Force),
            "Force must not send SIGKILL when StartTime differs");
    }

    [TestMethod]
    public void Terminate_StaleRow_AbortsWithMemProtected_WhenPidNowBelongsToOtherUid_WithoutSignalling()
    {
        var fake = new FakeTargets();
        var lang = new Lang("en");

        // Action-time fresh read reveals process now belongs to UID 0 (other user)
        fake.CustomFacts[700] = new ProcessFacts
        {
            Pid = 700,
            Name = "daemon",
            StartTime = 5000,
            Uid = 0, // Root UID != ownUid 1000
            ParentPid = 1,
            Readable = true,
        };

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, lang, ownPid: 5000, ownUid: 1000);

        KillResult result = flow.Terminate(new ProcessTarget(700, StartTime: 5000, "daemon"));

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual(lang["mem_protected"], result.Error);
        Assert.AreEqual(0, fake.SignalsSent.Count, "Must not signal a process that is protected at action time");
    }

    [TestMethod]
    public void Terminate_StaleRow_AbortsWithMemProtected_WhenPidNowBelongsToSessionCriticalName_WithoutSignalling()
    {
        var fake = new FakeTargets();
        var lang = new Lang("en");

        // Process has matching StartTime and own UID, but its name is a session-critical name
        fake.CustomFacts[701] = new ProcessFacts
        {
            Pid = 701,
            Name = "kwin_wayland",
            StartTime = 5000,
            Uid = 1000,
            ParentPid = 1,
            Readable = true,
        };

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, lang, ownPid: 5000, ownUid: 1000);

        KillResult result = flow.Terminate(new ProcessTarget(701, StartTime: 5000, "kwin_wayland"));

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual(lang["mem_protected"], result.Error);
        Assert.AreEqual(0, fake.SignalsSent.Count);
    }

    [TestMethod]
    public void Terminate_StaleRow_AbortsWithMemProtected_WhenUidIsNull_WithoutSignalling()
    {
        var fake = new FakeTargets();
        var lang = new Lang("en");

        // Fresh reading has unknown owner (Uid == null) -> protected
        fake.CustomFacts[702] = new ProcessFacts
        {
            Pid = 702,
            Name = "mystery-app",
            StartTime = 5000,
            Uid = null,
            ParentPid = 1,
            Readable = true,
        };

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, lang, ownPid: 5000, ownUid: 1000);

        KillResult result = flow.Terminate(new ProcessTarget(702, StartTime: 5000, "mystery-app"));

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual(lang["mem_protected"], result.Error);
        Assert.AreEqual(0, fake.SignalsSent.Count);
    }

    [TestMethod]
    public void Force_StaleRow_AbortsWithMemProtected_WithoutSignalling()
    {
        var fake = new FakeTargets();
        var lang = new Lang("en");

        fake.CustomFacts[703] = new ProcessFacts
        {
            Pid = 703,
            Name = "worker",
            StartTime = 5000,
            Uid = 1000,
            ParentPid = 1,
            Readable = true,
        };

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, lang, ownPid: 5000, ownUid: 1000);

        KillResult termResult = flow.Terminate(new ProcessTarget(703, StartTime: 5000, "worker"));
        Assert.AreEqual(KillState.NeedsForce, termResult.State);

        // While the force question is open the process falls under another UID
        fake.CustomFacts[703] = new ProcessFacts
        {
            Pid = 703,
            Name = "worker",
            StartTime = 5000,
            Uid = 0,
            ParentPid = 1,
            Readable = true,
        };

        KillResult result = flow.Force(termResult.Pending);

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual(lang["mem_protected"], result.Error);
        Assert.IsFalse(
            fake.SignalsSent.Any(s => s.Force),
            "Force must not signal protected process");
    }

    [TestMethod]
    public void Terminate_WhenPidfdUnsupported_FallsBackToRecheckAndKill()
    {
        var fake = new FakeTargets();
        // Kernel returns ENOSYS on pidfd_open -> reported as PinState.Unsupported
        fake.OnPin = pid => PinnedResult.Unsupported;
        fake.Running[800] = "fallback-app";
        fake.ExitAfterMs[800] = 1100; // exits shortly after kill(2)

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);

        KillResult result = flow.Terminate(new ProcessTarget(800, 1000, "fallback-app"));

        Assert.AreEqual(KillState.Gone, result.State);
        Assert.AreEqual(1, fake.SignalsSent.Count);
        Assert.AreEqual(800, fake.SignalsSent[0].Pid);
        Assert.IsFalse(fake.SignalsSent[0].Force);
    }

    [TestMethod]
    public void Terminate_WhenPinStateFailed_ReturnsFailedDirectlyWithoutSignalling()
    {
        var fake = new FakeTargets();
        fake.OnPin = pid => PinnedResult.Failed("No such process");
        fake.Running[900] = "app";

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);

        KillResult result = flow.Terminate(new ProcessTarget(900, 1000, "app"));

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual("No such process", result.Error);
        Assert.AreEqual(0, fake.SignalsSent.Count);
    }

    [TestMethod]
    public void Terminate_WhenTargetStartTimeIsNull_AbortsWithMemPidChanged_WithoutSignalling()
    {
        var fake = new FakeTargets();
        var lang = new Lang("en");
        fake.Running[100] = "app";

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, lang);

        KillResult result = flow.Terminate(new ProcessTarget(100, StartTime: null, "app"));

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual("PID 100 is now \"app\"", result.Error);
        Assert.AreEqual(0, fake.SignalsSent.Count, "Must not send signal when target StartTime is null");
        Assert.AreEqual(0, fake.PinCalls.Count, "Must not attempt to pin when row has no identity");
    }

    [TestMethod]
    public void Terminate_WhenTargetStartTimeIsZero_AbortsWithMemPidChanged_WithoutSignalling()
    {
        var fake = new FakeTargets();
        var lang = new Lang("en");
        fake.Running[100] = "app";

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, lang);

        KillResult result = flow.Terminate(new ProcessTarget(100, StartTime: 0, "app"));

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual("PID 100 is now \"app\"", result.Error);
        Assert.AreEqual(0, fake.SignalsSent.Count, "Must not send signal when target StartTime is 0");
        Assert.AreEqual(0, fake.PinCalls.Count, "Must not attempt to pin when target StartTime is 0");
    }

    [TestMethod]
    public void Terminate_WhenActionTimeStartTimeIsNull_AbortsWithMemPidChanged_WithoutSignalling()
    {
        var fake = new FakeTargets();
        var lang = new Lang("en");
        fake.CustomFacts[100] = new ProcessFacts
        {
            Pid = 100,
            Name = "app",
            StartTime = null,
            Uid = 1000,
            ParentPid = 1,
            Readable = true,
        };

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, lang);

        KillResult result = flow.Terminate(new ProcessTarget(100, StartTime: 1000, "app"));

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual("PID 100 is now \"app\"", result.Error);
        Assert.AreEqual(0, fake.SignalsSent.Count, "Must not send signal when action-time StartTime is null");
        Assert.AreEqual(1, fake.PinnedInstances[0].DisposeCount, "Pinned handle must be disposed after failed check");
    }

    [TestMethod]
    public void Terminate_WhenActionTimeStartTimeIsZero_AbortsWithMemPidChanged_WithoutSignalling()
    {
        var fake = new FakeTargets();
        var lang = new Lang("en");
        fake.CustomFacts[100] = new ProcessFacts
        {
            Pid = 100,
            Name = "app",
            StartTime = 0,
            Uid = 1000,
            ParentPid = 1,
            Readable = true,
        };

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, lang);

        KillResult result = flow.Terminate(new ProcessTarget(100, StartTime: 1000, "app"));

        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual("PID 100 is now \"app\"", result.Error);
        Assert.AreEqual(0, fake.SignalsSent.Count, "Must not send signal when action-time StartTime is 0");
        Assert.AreEqual(1, fake.PinnedInstances[0].DisposeCount, "Pinned handle must be disposed after failed check");
    }

    [TestMethod]
    public void Force_WithoutToken_OrTargetWithoutStartTime_AbortsWithMemPidChanged_WithoutSignalling()
    {
        var fake = new FakeTargets();
        var lang = new Lang("en");
        fake.Running[100] = "app";

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, lang);

        // No Terminate, so no token: Force has nothing to act on.
        KillResult noToken = flow.Force(null);
        Assert.AreEqual(KillState.Cancelled, noToken.State);

        // A row without an identity never earns a token either.
        KillResult nullResult = flow.Terminate(new ProcessTarget(100, StartTime: null, "app"));
        Assert.AreEqual(KillState.Failed, nullResult.State);
        Assert.AreEqual("PID 100 is now \"app\"", nullResult.Error);

        KillResult zeroResult = flow.Terminate(new ProcessTarget(100, StartTime: 0, "app"));
        Assert.AreEqual(KillState.Failed, zeroResult.State);
        Assert.AreEqual("PID 100 is now \"app\"", zeroResult.Error);

        Assert.AreEqual(0, fake.SignalsSent.Count);
        Assert.AreEqual(0, fake.PinCalls.Count);
    }

    [TestMethod]
    public void Force_WhenActionTimeStartTimeNullOrZero_AbortsWithMemPidChanged_WithoutSignalling()
    {
        var fake = new FakeTargets();
        var lang = new Lang("en");
        fake.CustomFacts[100] = new ProcessFacts
        {
            Pid = 100,
            Name = "app",
            StartTime = 1000,
            Uid = 1000,
            ParentPid = 1,
            Readable = true,
        };

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, lang);

        KillResult termResult = flow.Terminate(new ProcessTarget(100, StartTime: 1000, "app"));
        Assert.AreEqual(KillState.NeedsForce, termResult.State);

        // The stat file loses the start time while the force question is open
        fake.CustomFacts[100] = new ProcessFacts
        {
            Pid = 100,
            Name = "app",
            StartTime = null,
            Uid = 1000,
            ParentPid = 1,
            Readable = true,
        };

        KillResult result = flow.Force(termResult.Pending);
        Assert.AreEqual(KillState.Failed, result.State);
        Assert.AreEqual("PID 100 is now \"app\"", result.Error);

        fake.CustomFacts[101] = new ProcessFacts
        {
            Pid = 101,
            Name = "app",
            StartTime = 1000,
            Uid = 1000,
            ParentPid = 1,
            Readable = true,
        };

        KillResult secondTerm = flow.Terminate(new ProcessTarget(101, StartTime: 1000, "app"));
        Assert.AreEqual(KillState.NeedsForce, secondTerm.State);

        fake.CustomFacts[101] = new ProcessFacts
        {
            Pid = 101,
            Name = "app",
            StartTime = 0,
            Uid = 1000,
            ParentPid = 1,
            Readable = true,
        };

        KillResult zeroResult = flow.Force(secondTerm.Pending);
        Assert.AreEqual(KillState.Failed, zeroResult.State);
        Assert.AreEqual("PID 101 is now \"app\"", zeroResult.Error);

        Assert.IsFalse(
            fake.SignalsSent.Any(s => s.Force),
            "Force must not send SIGKILL when the action-time StartTime is null or 0");
    }

    [TestMethod]
    public void Terminate_NeedsForce_Force_PinsOnce_ReusesSameHandleForBothSignals_AndDisposesExactlyOnce()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "stubborn-app";
        fake.ExitAfterMs[200] = 10000; // does not exit within 3 s

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);
        var target = new ProcessTarget(200, 1000, "stubborn-app");

        // 1. Terminate -> NeedsForce
        KillResult termResult = flow.Terminate(target);
        Assert.AreEqual(KillState.NeedsForce, termResult.State);
        Assert.AreEqual(1, fake.PinCalls.Count, "Pin must be called once during Terminate");
        Assert.AreEqual(1, fake.PinnedInstances.Count);

        FakeTargets.PinnedProcess handle = fake.PinnedInstances[0];
        Assert.AreEqual(1, handle.Signals.Count);
        Assert.IsFalse(handle.Signals[0], "First signal from pinned handle must be SIGTERM");
        Assert.AreEqual(0, handle.DisposeCount, "Pinned handle must remain open while waiting for force decision");

        // 2. Force confirmed -> Force
        KillResult forceResult = flow.Force(termResult.Pending);
        Assert.AreEqual(KillState.Gone, forceResult.State);

        // Verify Pin was NOT called a second time
        Assert.AreEqual(1, fake.PinCalls.Count, "Pin must NOT be called a second time on Force");
        Assert.AreEqual(1, fake.PinnedInstances.Count);

        // Verify the SAME handle signalled SIGKILL
        Assert.AreEqual(2, handle.Signals.Count, "Same handle must signal twice");
        Assert.IsTrue(handle.Signals[1], "Second signal from same handle must be SIGKILL");

        // Verify Dispose called exactly once
        Assert.AreEqual(1, handle.DisposeCount, "Pinned handle must be disposed exactly once after Force");
    }

    [TestMethod]
    public void Terminate_NeedsForce_WhenUserDeclines_DisposesHandleOnce_WithoutSendingSigkill()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "stubborn-app";
        fake.ExitAfterMs[200] = 10000;

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);
        var target = new ProcessTarget(200, 1000, "stubborn-app");

        KillResult termResult = flow.Terminate(target);
        Assert.AreEqual(KillState.NeedsForce, termResult.State);
        Assert.AreEqual(1, fake.PinCalls.Count);

        FakeTargets.PinnedProcess handle = fake.PinnedInstances[0];
        Assert.AreEqual(0, handle.DisposeCount, "Handle must remain open before user decision");

        // User declines -> Cancel
        flow.Cancel(termResult.Pending);

        Assert.AreEqual(1, handle.DisposeCount, "Handle must be disposed exactly once when user cancels");
        Assert.AreEqual(1, handle.Signals.Count, "No second signal should be sent via handle");
        Assert.AreEqual(1, fake.SignalsSent.Count, "No SIGKILL signal was sent overall");
        Assert.IsFalse(fake.SignalsSent[0].Force, "Only SIGTERM was ever sent");
        Assert.IsTrue(fake.IsAlive(200), "Process should still be running");
    }

    [TestMethod]
    public void Terminate_WhenProcessExitsWithin3Seconds_DisposesHandleExactlyOnce()
    {
        var fake = new FakeTargets();
        fake.Running[100] = "quick-exit";
        fake.ExitAfterMs[100] = 1200; // exits 200 ms after SIGTERM (within 3000 ms)

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);
        var target = new ProcessTarget(100, 1000, "quick-exit");

        KillResult result = flow.Terminate(target);

        Assert.AreEqual(KillState.Gone, result.State);
        Assert.AreEqual(1, fake.PinCalls.Count);

        FakeTargets.PinnedProcess handle = fake.PinnedInstances[0];
        Assert.AreEqual(1, handle.Signals.Count);
        Assert.IsFalse(handle.Signals[0], "Signal was SIGTERM");
        Assert.AreEqual(1, handle.DisposeCount, "Pinned handle must be disposed exactly once when process exits within 3 s");
    }

    [TestMethod]
    public void Force_WhenProcessExitedWhileAwaitingConfirmation_DisposesHandleOnce_AndReturnsGone()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "stubborn-then-exits";
        fake.ExitAfterMs[200] = 5000; // survives 3s wait (ends at 4000ms), but exits at 5000ms

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);
        var target = new ProcessTarget(200, 1000, "stubborn-then-exits");

        KillResult termResult = flow.Terminate(target);
        Assert.AreEqual(KillState.NeedsForce, termResult.State);

        FakeTargets.PinnedProcess handle = fake.PinnedInstances[0];
        Assert.AreEqual(0, handle.DisposeCount);

        // Advance clock past 5000ms so process exits
        fake.Step(2000); // clock reaches 6000ms, process exits

        // Now user confirms Force
        KillResult forceResult = flow.Force(termResult.Pending);
        Assert.AreEqual(KillState.Gone, forceResult.State);
        Assert.AreEqual(1, handle.DisposeCount, "Handle must be disposed when process exit is detected");
        Assert.AreEqual(1, fake.SignalsSent.Count, "No SIGKILL needed because process already exited");
    }

    [TestMethod]
    public void CancelAll_DisposesAllPendingHandles()
    {
        var fake = new FakeTargets();
        fake.Running[201] = "app-1";
        fake.Running[202] = "app-2";
        fake.ExitAfterMs[201] = 10000;
        fake.ExitAfterMs[202] = 10000;

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);
        flow.Terminate(new ProcessTarget(201, 1000, "app-1"));
        flow.Terminate(new ProcessTarget(202, 1000, "app-2"));

        Assert.AreEqual(2, fake.PinnedInstances.Count);
        Assert.AreEqual(0, fake.PinnedInstances[0].DisposeCount);
        Assert.AreEqual(0, fake.PinnedInstances[1].DisposeCount);

        flow.CancelAll();

        Assert.AreEqual(1, fake.PinnedInstances[0].DisposeCount);
        Assert.AreEqual(1, fake.PinnedInstances[1].DisposeCount);
    }

    [TestMethod]
    public void Terminate_WhenNewKillReplacesPendingKillForSamePid_DisposesPreviousHandle()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "app";
        fake.ExitAfterMs[200] = 10000;

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);
        flow.Terminate(new ProcessTarget(200, 1000, "app"));

        FakeTargets.PinnedProcess handle1 = fake.PinnedInstances[0];
        Assert.AreEqual(0, handle1.DisposeCount);

        // A second Terminate for the same PID replaces the pending one
        flow.Terminate(new ProcessTarget(200, 1000, "app"));

        Assert.AreEqual(1, handle1.DisposeCount, "Previous pending handle must be disposed when replaced");
        Assert.AreEqual(2, fake.PinnedInstances.Count);
    }

    [TestMethod]
    public void CancelAll_ThenForce_DoesNotPinOrSignal_ReturnsCancelled()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "app";
        fake.ExitAfterMs[200] = 10000;

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);
        KillResult termResult = flow.Terminate(new ProcessTarget(200, 1000, "app"));
        Assert.AreEqual(KillState.NeedsForce, termResult.State);
        Assert.IsNotNull(termResult.Pending);
        PendingKill token = termResult.Pending;

        Assert.AreEqual(1, fake.PinCalls.Count);
        Assert.AreEqual(1, fake.SignalsSent.Count); // SIGTERM
        FakeTargets.PinnedProcess handle = fake.PinnedInstances[0];
        Assert.AreEqual(0, handle.DisposeCount);

        flow.CancelAll();

        Assert.IsTrue(token.IsDisposed);
        Assert.AreEqual(1, handle.DisposeCount);

        KillResult forceResult = flow.Force(token);

        Assert.AreEqual(KillState.Cancelled, forceResult.State);
        Assert.IsNull(forceResult.Error);
        Assert.AreEqual(1, fake.PinCalls.Count, "CancelAll then Force must not Pin");
        Assert.AreEqual(1, fake.SignalsSent.Count, "CancelAll then Force must not send any signal");
        Assert.AreEqual(1, handle.Signals.Count, "Handle must not send any signal on cancelled Force");
    }

    [TestMethod]
    public void Terminate_TwoCallsForSamePid_FirstTokenDisposed_ForceFirstDoesNothing_ForceSecondSignalsSecondHandleOnly()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "app";
        fake.ExitAfterMs[200] = 10000;

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);
        KillResult firstTerm = flow.Terminate(new ProcessTarget(200, 1000, "app"));
        Assert.AreEqual(KillState.NeedsForce, firstTerm.State);
        Assert.IsNotNull(firstTerm.Pending);
        PendingKill firstToken = firstTerm.Pending;

        FakeTargets.PinnedProcess firstHandle = fake.PinnedInstances[0];
        Assert.IsFalse(firstToken.IsDisposed);
        Assert.AreEqual(0, firstHandle.DisposeCount);

        // A second Terminate call for the same PID: the first token is disposed when the second is created
        KillResult secondTerm = flow.Terminate(new ProcessTarget(200, 1000, "app"));
        Assert.AreEqual(KillState.NeedsForce, secondTerm.State);
        Assert.IsNotNull(secondTerm.Pending);
        PendingKill secondToken = secondTerm.Pending;

        FakeTargets.PinnedProcess secondHandle = fake.PinnedInstances[1];
        Assert.AreNotSame(firstToken, secondToken);
        Assert.AreNotSame(firstHandle, secondHandle);

        Assert.IsTrue(firstToken.IsDisposed, "First token is disposed when the second is created");
        Assert.AreEqual(1, firstHandle.DisposeCount, "First handle closes when replaced");
        Assert.IsFalse(secondToken.IsDisposed, "Second token remains live");
        Assert.AreEqual(0, secondHandle.DisposeCount);

        // Force(firstToken) does nothing
        KillResult firstForceResult = flow.Force(firstToken);
        Assert.AreEqual(KillState.Cancelled, firstForceResult.State);
        Assert.AreEqual(1, firstHandle.Signals.Count, "First handle must not send SIGKILL");
        Assert.AreEqual(1, firstHandle.DisposeCount);

        // Force(secondToken) signals through the second handle only
        KillResult secondForceResult = flow.Force(secondToken);
        Assert.AreEqual(KillState.Gone, secondForceResult.State);
        Assert.AreEqual(2, secondHandle.Signals.Count, "Second handle must send SIGKILL");
        Assert.IsTrue(secondHandle.Signals[1], "Second signal on second handle must be SIGKILL");
        Assert.AreEqual(1, secondHandle.DisposeCount, "Second handle closes once after Force");
        Assert.IsTrue(secondToken.IsDisposed);

        // Pin called once per Terminate, never during Force
        Assert.AreEqual(2, fake.PinCalls.Count);
        // Exactly 3 signals sent: SIGTERM to first, SIGTERM to second, SIGKILL to second
        Assert.AreEqual(3, fake.SignalsSent.Count);
        Assert.IsFalse(fake.SignalsSent[0].Force);
        Assert.IsFalse(fake.SignalsSent[1].Force);
        Assert.IsTrue(fake.SignalsSent[2].Force);
        Assert.AreEqual(200, fake.SignalsSent[2].Pid);
    }

    [TestMethod]
    public void PendingKill_DisposeCalledTwice_HandleClosesOnce()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "app";
        fake.ExitAfterMs[200] = 10000;

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);
        KillResult termResult = flow.Terminate(new ProcessTarget(200, 1000, "app"));
        Assert.AreEqual(KillState.NeedsForce, termResult.State);
        Assert.IsNotNull(termResult.Pending);
        PendingKill token = termResult.Pending;

        FakeTargets.PinnedProcess handle = fake.PinnedInstances[0];
        Assert.AreEqual(0, handle.DisposeCount);

        token.Dispose();
        Assert.IsTrue(token.IsDisposed);
        Assert.AreEqual(1, handle.DisposeCount);

        token.Dispose();
        Assert.IsTrue(token.IsDisposed);
        Assert.AreEqual(1, handle.DisposeCount, "Handle must close exactly once even when token.Dispose() is called twice");
    }

    [TestMethod]
    public void PendingKill_ConcurrentDispose_HandleClosesOnce()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "app";
        fake.ExitAfterMs[200] = 10000;

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);
        KillResult termResult = flow.Terminate(new ProcessTarget(200, 1000, "app"));
        Assert.AreEqual(KillState.NeedsForce, termResult.State);
        Assert.IsNotNull(termResult.Pending);
        PendingKill token = termResult.Pending;

        FakeTargets.PinnedProcess handle = fake.PinnedInstances[0];
        Assert.AreEqual(0, handle.DisposeCount);

        using var barrier = new Barrier(2);
        var t1 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            token.Dispose();
        });
        var t2 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            token.Dispose();
        });

        Task.WaitAll(t1, t2);

        Assert.IsTrue(token.IsDisposed);
        Assert.AreEqual(1, handle.DisposeCount, "Handle must close exactly once when two threads call Dispose concurrently");
    }

    [TestMethod]
    public void Terminate_TwoCallsForSamePid_SecondWaitsForGate_ExactlyOneTokenLive_EarlierDisposedOnce()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "app";
        fake.ExitAfterMs[200] = 10000;

        using var call1InSigterm = new ManualResetEventSlim(false);
        using var call1Release = new ManualResetEventSlim(false);
        using var call2TerminateRequested = new ManualResetEventSlim(false);
        using var call2Pinned = new ManualResetEventSlim(false);

        int pinCount = 0;
        fake.OnPin = pid =>
        {
            int count = Interlocked.Increment(ref pinCount);
            if (count == 2)
            {
                call2Pinned.Set();
            }
            var pinned = new FakeTargets.PinnedProcess(fake, pid);
            fake.PinnedInstances.Add(pinned);
            return PinnedResult.Pinned(pinned);
        };

        fake.OnSignal = (pid, force) =>
        {
            if (!force && pinCount == 1)
            {
                call1InSigterm.Set();
                bool proceeded = call1Release.Wait(TimeSpan.FromSeconds(5));
                Assert.IsTrue(proceeded, "Call 1 release timed out");
            }
            return null;
        };

        int terminateRequestCount = 0;
        var flow = new KillFlow(
            fake,
            fake.Clock.Now,
            fake.Step,
            onTerminateRequested: pid =>
            {
                if (Interlocked.Increment(ref terminateRequestCount) == 2)
                {
                    call2TerminateRequested.Set();
                }
            });
        var target = new ProcessTarget(200, 1000, "app");

        // 1. Hold call 1 inside its SIGTERM callback
        var task1 = Task.Run(() => flow.Terminate(target));
        bool call1AtSigterm = call1InSigterm.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(call1AtSigterm, "Call 1 did not reach SIGTERM callback within 5 s");

        // 2. Start call 2 and wait (5 s timeout) for onTerminateRequested from call 2,
        // while call 1 is still held inside its SIGTERM callback.
        var task2 = Task.Run(() => flow.Terminate(target));
        bool call2Requested = call2TerminateRequested.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(call2Requested, "Call 2 onTerminateRequested hook was not called within 5 s");

        // Assert that call 2 has not pinned while call 1 held the gate.
        // A short negative wait shows call 2 is blocked on the gate rather than not yet run.
        bool call2PinnedWhileCall1HeldGate = call2Pinned.Wait(TimeSpan.FromMilliseconds(50));
        Assert.IsFalse(call2PinnedWhileCall1HeldGate, "Call 2 must not pin while Call 1 holds the gate");
        Assert.AreEqual(1, fake.PinCalls.Count, "Only Call 1 should have pinned while Call 1 held the gate");

        // 3. Release call 1. Both calls complete within 5 s.
        call1Release.Set();
        bool completed = Task.WaitAll(new[] { task1, task2 }, TimeSpan.FromSeconds(5));
        Assert.IsTrue(completed, "Both Terminate calls must complete within 5 s");

        KillResult res1 = task1.Result;
        KillResult res2 = task2.Result;

        // 4. Afterwards there are 2 pins, call 1's token is disposed exactly once
        // (replaced), and call 2's token is live, or is Cancelled/NeedsForce per the results.
        Assert.AreEqual(2, fake.PinCalls.Count);
        Assert.AreEqual(2, fake.PinnedInstances.Count);

        FakeTargets.PinnedProcess handle1 = fake.PinnedInstances[0];
        FakeTargets.PinnedProcess handle2 = fake.PinnedInstances[1];

        // Call 1's handle was replaced and disposed exactly once
        Assert.AreEqual(1, handle1.DisposeCount, "Call 1's handle must be disposed exactly once when replaced");
        if (res1.Pending is { } token1)
        {
            Assert.IsTrue(token1.IsDisposed, "Call 1's token must be disposed");
        }
        Assert.IsTrue(
            res1.State is KillState.Cancelled or KillState.NeedsForce,
            $"Call 1 result must be Cancelled or NeedsForce, was {res1.State}");

        // Call 2's token is live
        Assert.AreEqual(KillState.NeedsForce, res2.State);
        Assert.IsNotNull(res2.Pending);
        PendingKill liveToken = res2.Pending;
        Assert.IsFalse(liveToken.IsDisposed, "Call 2's token must be live");
        Assert.AreEqual(0, handle2.DisposeCount, "Call 2's handle must remain open");

        // Declining call 2's token disposes handle 2 once
        flow.Decline(liveToken);
        Assert.IsTrue(liveToken.IsDisposed);
        Assert.AreEqual(1, handle2.DisposeCount, "Call 2's handle must be disposed once on decline");
    }

    [TestMethod]
    public void CancelAll_WhileForceAboutToRun_WhenForceAcquiresGateFirst_SigkillHappenedBeforeClose_AndNeverSignalsAfterward()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "app";
        fake.ExitAfterMs[200] = 10000;

        using var cancelRequested = new ManualResetEventSlim(false);
        KillFlow? flow = null;
        Task? cancelTask = null;

        // Inside the Force signal (which runs under lock (_gate) in Force):
        fake.OnSignal = (pid, force) =>
        {
            if (force)
            {
                // Force holds lock (_gate). Start CancelAll on a task.
                cancelTask = Task.Run(() => flow!.CancelAll());

                // Wait (5 s timeout) for onCancelAllRequested hook before returning.
                // This proves the overlap, because CancelAll is blocked on the gate while Force holds it.
                bool hookFired = cancelRequested.Wait(TimeSpan.FromSeconds(5));
                Assert.IsTrue(hookFired, "CancelAll hook was not called within 5 s");
            }
            return null;
        };

        flow = new KillFlow(
            fake,
            fake.Clock.Now,
            fake.Step,
            onCancelAllRequested: () => cancelRequested.Set());

        var target = new ProcessTarget(200, 1000, "app");

        KillResult termResult = flow.Terminate(target);
        Assert.AreEqual(KillState.NeedsForce, termResult.State);
        Assert.IsNotNull(termResult.Pending);
        PendingKill token = termResult.Pending;

        // Run Force
        var forceTask = Task.Run(() => flow.Force(token));

        // Wait for both to finish (5 s timeout)
        bool forceCompleted = forceTask.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(forceCompleted, "Force task did not finish in time");
        Assert.IsNotNull(cancelTask, "CancelAll task must have been started");
        bool cancelCompleted = cancelTask.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(cancelCompleted, "CancelAll task did not finish in time");

        KillResult forceResult = forceTask.Result;
        Assert.AreEqual(KillState.Gone, forceResult.State);

        // SIGKILL happened before the close
        Assert.AreEqual(1, fake.SignalsSent.Count(s => s.Force), "SIGKILL must have happened before close");

        // After CancelAll returns, no signal is ever sent
        KillResult secondForce = flow.Force(token);
        Assert.AreEqual(KillState.Cancelled, secondForce.State);

        KillResult termAfterClose = flow.Terminate(target);
        Assert.AreEqual(KillState.Cancelled, termAfterClose.State);

        // Total force signals remains exactly 1
        Assert.AreEqual(1, fake.SignalsSent.Count(s => s.Force), "No signal must ever be sent after CancelAll");
        Assert.AreEqual(1, fake.PinCalls.Count, "Pin must not be called after CancelAll");
    }

    [TestMethod]
    public void CancelAll_RequestedWhileTerminateInsidePin_CancelAllWaits_SigtermSentBeforeClose_TokenDisposed_NoSignalAfterClose()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "app";
        fake.ExitAfterMs[200] = 10000;

        using var cancelRequested = new ManualResetEventSlim(false);
        KillFlow? flow = null;
        Task? cancelTask = null;
        FakeTargets.PinnedProcess? pinnedProcess = null;

        fake.OnPin = (pid) =>
        {
            // Inside Pin: Terminate holds lock (_gate).
            // Start CancelAll on a task.
            cancelTask = Task.Run(() => flow!.CancelAll());

            // Block the fake Pin until the hook fires.
            bool hookFired = cancelRequested.Wait(TimeSpan.FromSeconds(5));
            Assert.IsTrue(hookFired, "CancelAll hook was not called within 5 s");

            // Create and return the pinned handle. Terminate still holds lock (_gate).
            pinnedProcess = new FakeTargets.PinnedProcess(fake, pid);
            fake.PinnedInstances.Add(pinnedProcess);
            return PinnedResult.Pinned(pinnedProcess);
        };

        fake.OnSignal = (pid, force) =>
        {
            if (!force)
            {
                // Terminate is sending SIGTERM while still holding lock (_gate).
                // Expect CancelAll to wait: CancelAll cannot acquire _gate yet.
                Assert.IsNotNull(cancelTask, "CancelAll task must have been started");
                Assert.IsFalse(cancelTask.IsCompleted, "CancelAll must wait on the gate while Terminate holds it");
            }
            return null;
        };

        ExitWait waitForExit = (read, deadline) =>
        {
            // Terminate has sent SIGTERM, published the token, and left the lock to wait for exit.
            // CancelAll can now acquire lock (_gate), mark the flow closed, and dispose the token.
            // Wait for CancelAll to finish (5 s timeout).
            bool cancelFinished = cancelTask!.Wait(TimeSpan.FromSeconds(5));
            Assert.IsTrue(cancelFinished, "CancelAll did not complete within 5 s");
            return false;
        };

        flow = new KillFlow(
            fake,
            fake.Clock.Now,
            fake.Step,
            waitForExit: waitForExit,
            onCancelAllRequested: () => cancelRequested.Set());

        var target = new ProcessTarget(200, 1000, "app");

        // Run Terminate on a background task
        var terminateTask = Task.Run(() => flow.Terminate(target));

        bool terminateCompleted = terminateTask.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(terminateCompleted, "Terminate task did not finish in time");

        KillResult termResult = terminateTask.Result;
        // Token was disposed by CancelAll while in exit wait, so result is Cancelled
        Assert.AreEqual(KillState.Cancelled, termResult.State);
        Assert.IsNull(termResult.Pending);

        // SIGTERM was sent before the close (the user's confirmed action)
        Assert.AreEqual(1, fake.SignalsSent.Count);
        Assert.AreEqual(200, fake.SignalsSent[0].Pid);
        Assert.IsFalse(fake.SignalsSent[0].Force, "SIGTERM must have been sent before close");

        // Pinned handle was disposed once by CancelAll
        Assert.IsNotNull(pinnedProcess);
        Assert.AreEqual(1, pinnedProcess.DisposeCount, "Pinned handle must be disposed once by CancelAll");

        // No signal after CancelAll returns
        KillResult forceAfterClose = flow.Force(termResult.Pending);
        Assert.AreEqual(KillState.Cancelled, forceAfterClose.State);

        KillResult termAfterClose = flow.Terminate(target);
        Assert.AreEqual(KillState.Cancelled, termAfterClose.State);

        Assert.AreEqual(1, fake.SignalsSent.Count, "No signal must be sent after CancelAll");
        Assert.AreEqual(1, fake.PinCalls.Count, "Pin must not be called after CancelAll");
    }

    [TestMethod]
    public void Terminate_AfterCancelAll_PinIsNeverCalled()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "app";

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);
        var target = new ProcessTarget(200, 1000, "app");

        flow.CancelAll();

        KillResult result = flow.Terminate(target);

        Assert.AreEqual(KillState.Cancelled, result.State);
        Assert.AreEqual(0, fake.PinCalls.Count, "Pin must never be called after CancelAll");
        Assert.AreEqual(0, fake.SignalsSent.Count, "No signal must be sent after CancelAll");
    }

    [TestMethod]
    public void CancelAll_WhileForceAboutToRun_WhenCancelAllAcquiresGateFirst_NoSignalSent_AndNeverSignalsAfterward()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "app";
        fake.ExitAfterMs[200] = 10000;

        using var forceReady = new ManualResetEventSlim(false);
        using var cancelProceed = new ManualResetEventSlim(false);

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step);
        var target = new ProcessTarget(200, 1000, "app");

        KillResult termResult = flow.Terminate(target);
        Assert.AreEqual(KillState.NeedsForce, termResult.State);
        Assert.IsNotNull(termResult.Pending);
        PendingKill token = termResult.Pending;

        // Force is queued on a task, ready to call flow.Force
        var forceTask = Task.Run(() =>
        {
            forceReady.Set();
            bool proceed = cancelProceed.Wait(TimeSpan.FromSeconds(5));
            Assert.IsTrue(proceed, "Force wait timed out");
            return flow.Force(token);
        });

        bool ready = forceReady.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(ready, "Force task was not ready in time");

        // CancelAll runs while Force is about to run
        flow.CancelAll();

        // Release Force task to proceed to lock (_gate)
        cancelProceed.Set();

        bool completed = forceTask.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(completed, "Force task did not finish in time");

        KillResult forceResult = forceTask.Result;

        // Result is Cancelled because CancelAll closed the flow and disposed the token
        Assert.AreEqual(KillState.Cancelled, forceResult.State);

        // No force signal was sent
        Assert.AreEqual(0, fake.SignalsSent.Count(s => s.Force), "No SIGKILL should be sent when CancelAll wins");

        // After CancelAll returns, no signal is ever sent
        KillResult secondForce = flow.Force(token);
        Assert.AreEqual(KillState.Cancelled, secondForce.State);

        KillResult termAfterClose = flow.Terminate(target);
        Assert.AreEqual(KillState.Cancelled, termAfterClose.State);

        Assert.AreEqual(0, fake.SignalsSent.Count(s => s.Force), "No signal must ever be sent after CancelAll");
    }

    [TestMethod]
    public void CancelAll_DuringTerminateExitWait_ReturnsCancelled_AndHandleDisposedOnce()
    {
        var fake = new FakeTargets();
        fake.Running[200] = "app";
        fake.ExitAfterMs[200] = 10000;

        using var waitEntered = new ManualResetEventSlim(false);
        using var waitRelease = new ManualResetEventSlim(false);

        ExitWait waitForExit = (read, deadline) =>
        {
            waitEntered.Set();
            bool released = waitRelease.Wait(TimeSpan.FromSeconds(5));
            Assert.IsTrue(released, "Exit wait was not released in time");
            return false;
        };

        var flow = new KillFlow(fake, fake.Clock.Now, fake.Step, waitForExit: waitForExit);
        var target = new ProcessTarget(200, 1000, "app");

        var terminateTask = Task.Run(() => flow.Terminate(target));

        bool entered = waitEntered.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(entered, "Terminate did not enter exit wait in time");

        Assert.AreEqual(1, fake.PinnedInstances.Count);
        FakeTargets.PinnedProcess handle = fake.PinnedInstances[0];
        Assert.AreEqual(0, handle.DisposeCount, "Handle must remain open while waiting");
        Assert.AreEqual(1, fake.SignalsSent.Count, "SIGTERM must have been sent");
        Assert.IsFalse(fake.SignalsSent[0].Force);

        // CancelAll during Terminate's wait
        flow.CancelAll();

        // Handle is disposed once by CancelAll
        Assert.AreEqual(1, handle.DisposeCount, "Handle must be disposed by CancelAll");

        // Release the wait
        waitRelease.Set();

        bool finished = terminateTask.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(finished, "Terminate task did not finish in time");

        KillResult result = terminateTask.Result;
        Assert.AreEqual(KillState.Cancelled, result.State);
        Assert.IsNull(result.Error);
        Assert.IsNull(result.Pending);

        // Handle is disposed exactly once
        Assert.AreEqual(1, handle.DisposeCount, "Handle must be disposed exactly once");
        // No SIGKILL was sent
        Assert.AreEqual(1, fake.SignalsSent.Count);
        Assert.IsFalse(fake.SignalsSent[0].Force);
    }

    [TestMethod]
    public void ShellModel_ExposesOneKillFlowInstance_FromConstruction()
    {
        var fake = new FakeTargets();
        var config = new AppConfig();
        var lang = new Lang("en");

        var model = new ShellModel(config, lang, killTargets: fake);

        // Exposes one KillFlow instance from construction (not null, no lazy initialization)
        KillFlow initialFlow = model.Kills;
        Assert.IsNotNull(initialFlow, "KillFlow must be created eagerly in constructor");

        // Switching views must retain the exact same KillFlow instance
        model.SwitchTo(ViewMode.Full);
        Assert.AreSame(initialFlow, model.Kills, "Switching to Full must retain the same KillFlow instance");

        model.CancelKills();
        Assert.AreSame(initialFlow, model.Kills, "CancelKills must retain the same KillFlow instance");

        // Reopening Full view calls Reopen() on the SAME instance
        model.SwitchTo(ViewMode.Widget);
        model.SwitchTo(ViewMode.Full);
        Assert.AreSame(initialFlow, model.Kills, "Reopening Full must retain the same KillFlow instance");

        model.SetChoice(SettingKeys.Language, "th");
        Assert.AreSame(initialFlow, model.Kills, "Changing language must retain the same KillFlow instance");

        model.Rebuild();
        Assert.AreSame(initialFlow, model.Kills, "Rebuild must retain the same KillFlow instance");
    }
}