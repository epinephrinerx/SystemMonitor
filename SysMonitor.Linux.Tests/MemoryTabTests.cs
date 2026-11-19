using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class MemoryTabTests
{
    private const int OwnPid = 5000;
    private const int OwnUid = 1000;

    // -------------------------------------------------------- Process Grouping

    [TestMethod]
    public void MemoryBoard_Group_GroupsCaseInsensitively_AndSumsWorkingSet()
    {
        var processes = new[]
        {
            new ProcessEntry { Pid = 101, Name = "chrome", WorkingSet = 200 * 1024 * 1024, PrivateBytes = 150 * 1024 * 1024 },
            new ProcessEntry { Pid = 102, Name = "Chrome", WorkingSet = 300 * 1024 * 1024, PrivateBytes = 250 * 1024 * 1024 },
            new ProcessEntry { Pid = 103, Name = "CHROME", WorkingSet = 100 * 1024 * 1024, PrivateBytes = 80 * 1024 * 1024 },
            new ProcessEntry { Pid = 201, Name = "code", WorkingSet = 400 * 1024 * 1024, PrivateBytes = 300 * 1024 * 1024 },
        };

        IReadOnlyList<ProcessGroup> groups = MemoryBoard.Group(processes);

        Assert.AreEqual(2, groups.Count);

        // Group order: highest working set first
        // chrome group total: 200 + 300 + 100 = 600 MB
        // code group total: 400 MB
        ProcessGroup chromeGroup = groups[0];
        Assert.AreEqual("chrome", chromeGroup.Name, ignoreCase: true);
        Assert.AreEqual(600L * 1024 * 1024, chromeGroup.WorkingSet);
        Assert.AreEqual(3, chromeGroup.Count);

        // Within group, members sorted by working set descending, then PID
        Assert.AreEqual(102, chromeGroup.Members[0].Pid); // 300 MB
        Assert.AreEqual(101, chromeGroup.Members[1].Pid); // 200 MB
        Assert.AreEqual(103, chromeGroup.Members[2].Pid); // 100 MB

        ProcessGroup codeGroup = groups[1];
        Assert.AreEqual("code", codeGroup.Name);
        Assert.AreEqual(400L * 1024 * 1024, codeGroup.WorkingSet);
        Assert.AreEqual(1, codeGroup.Count);
    }

    [TestMethod]
    public void MemoryBoard_Size_FormatsShortReadableUnits()
    {
        Assert.AreEqual("1.5 GB", MemoryBoard.Size((long)(1.5 * 1024 * 1024 * 1024)));
        Assert.AreEqual("340 MB", MemoryBoard.Size(340L * 1024 * 1024));
        Assert.AreEqual("12.5 MB", MemoryBoard.Size((long)(12.5 * 1024 * 1024)));
        Assert.AreEqual("0.5 MB", MemoryBoard.Size((long)(0.5 * 1024 * 1024)));
    }

    // -------------------------------------------------------- Protection Rules

    [TestMethod]
    public void ProcessProtection_Pid1AndPid2_AlwaysProtected()
    {
        var pid1 = new ProcessEntry { Pid = 1, Name = "systemd", Uid = OwnUid };
        var pid2 = new ProcessEntry { Pid = 2, Name = "kthreadd", Uid = OwnUid };

        Assert.IsTrue(ProcessProtection.IsProtected(pid1, ownPid: OwnPid, ownUid: OwnUid));
        Assert.IsTrue(ProcessProtection.IsProtected(pid2, ownPid: OwnPid, ownUid: OwnUid));
    }

    [TestMethod]
    public void ProcessProtection_OwnPid_Protected()
    {
        var ownProc = new ProcessEntry { Pid = OwnPid, Name = "sysmonitor", Uid = OwnUid };
        Assert.IsTrue(ProcessProtection.IsProtected(ownProc, ownPid: OwnPid, ownUid: OwnUid));
    }

    [TestMethod]
    public void ProcessProtection_OtherUserUid_Protected()
    {
        // Another user's process (e.g. UID 0 or 1001 != OwnUid 1000)
        var otherUser = new ProcessEntry { Pid = 2345, Name = "daemon", Uid = 0 };
        Assert.IsTrue(ProcessProtection.IsProtected(otherUser, ownPid: OwnPid, ownUid: OwnUid));

        var thirdUser = new ProcessEntry { Pid = 3456, Name = "otherapp", Uid = 1001 };
        Assert.IsTrue(ProcessProtection.IsProtected(thirdUser, ownPid: OwnPid, ownUid: OwnUid));
    }

    [TestMethod]
    public void ProcessProtection_KernelThread_Protected()
    {
        // Flagged as kernel thread
        var kthread = new ProcessEntry { Pid = 35, Name = "kworker/0:1", KernelThread = true, Uid = OwnUid };
        Assert.IsTrue(ProcessProtection.IsProtected(kthread, ownPid: OwnPid, ownUid: OwnUid));
    }

    [TestMethod]
    public void ProcessProtection_UnreadableEntry_Protected()
    {
        var unreadable = new ProcessEntry { Pid = 9999, Name = "secret", Readable = false, Uid = OwnUid };
        Assert.IsTrue(ProcessProtection.IsProtected(unreadable, ownPid: OwnPid, ownUid: OwnUid));
    }

    [TestMethod]
    public void ProcessProtection_UidNull_IsAlwaysProtected()
    {
        // ProcessEntry with unknown owner (Uid == null)
        var unknownEntry = new ProcessEntry { Pid = 4321, Name = "mystery", Uid = null, Readable = true };
        Assert.IsTrue(ProcessProtection.IsProtected(unknownEntry, ownPid: OwnPid, ownUid: OwnUid));

        // ProcessFacts with unknown owner (Uid == null)
        var unknownFacts = new ProcessFacts { Pid = 4321, Name = "mystery", Uid = null, Readable = true };
        Assert.IsTrue(ProcessProtection.IsProtected(unknownFacts, ownPid: OwnPid, ownUid: OwnUid));
    }

    [TestMethod]
    public void ProcessProtection_SessionCriticalNames_Protected()
    {
        string[] criticalNames =
        {
            "systemd", "init", "kwin_wayland", "kwin_x11", "plasmashell",
            "ksmserver", "kded6", "Xwayland", "Xorg", "gnome-shell",
            "gnome-session-binary", "mutter", "dbus-daemon", "dbus-broker",
            "pipewire", "wireplumber", "pulseaudio", "sddm", "gdm",
            "polkitd", "systemd-logind",
        };

        foreach (string name in criticalNames)
        {
            var proc = new ProcessEntry { Pid = 1200, Name = name, Uid = OwnUid, Readable = true };
            Assert.IsTrue(ProcessProtection.IsProtected(proc, ownPid: OwnPid, ownUid: OwnUid),
                $"Session process '{name}' must be protected");
        }
    }

    [TestMethod]
    public void ProcessProtection_NormalUserProcess_CanBeKilled()
    {
        var normal = new ProcessEntry
        {
            Pid = 8888,
            Name = "my-calculator",
            Uid = OwnUid,
            Readable = true,
            KernelThread = false,
        };

        Assert.IsFalse(ProcessProtection.IsProtected(normal, ownPid: OwnPid, ownUid: OwnUid));
    }

    // ---------------------------------------------------- Process Table & Caps

    [TestMethod]
    public void MemoryTabBoard_ProcessCapsAndNotes_40Normally_120WhenSearching()
    {
        var lang = new Lang("en");
        var procs = new List<ProcessEntry>();

        // Generate 150 distinct process groups
        for (int i = 1; i <= 150; i++)
        {
            procs.Add(new ProcessEntry
            {
                Pid = 1000 + i,
                Name = $"proc-{i:D3}",
                WorkingSet = (150 - i + 1) * 1024 * 1024,
                Uid = OwnUid,
            });
        }

        var snapshot = new Snapshot { Processes = procs };

        // 1. Without search: capped at 40 groups, note shows "Showing 40 of 150 · search to see the rest"
        MemoryTabModel modelNormal = MemoryTabBoard.Build(
            snapshot, lang, query: "", ownPid: OwnPid, ownUid: OwnUid, graphedGroup: null);

        Assert.AreEqual(MemoryTabBoard.GroupCap, modelNormal.Groups.Count);
        Assert.AreEqual(40, modelNormal.Groups.Count);
        Assert.AreEqual("Showing 40 of 150 · search to see the rest", modelNormal.GroupNote);

        // 2. With search matching all 150: capped at 120 groups
        MemoryTabModel modelSearching = MemoryTabBoard.Build(
            snapshot, lang, query: "proc", ownPid: OwnPid, ownUid: OwnUid, graphedGroup: null);

        Assert.AreEqual(MemoryTabBoard.GroupCapSearching, modelSearching.Groups.Count);
        Assert.AreEqual(120, modelSearching.Groups.Count);
        Assert.AreEqual("Showing 120 of 150 · search to see the rest", modelSearching.GroupNote);

        // 3. With targeted search matching only 5: all 5 shown, note is empty
        MemoryTabModel modelFew = MemoryTabBoard.Build(
            snapshot, lang, query: "proc-00", ownPid: OwnPid, ownUid: OwnUid, graphedGroup: null);

        Assert.AreEqual(9, modelFew.Groups.Count); // proc-001 through proc-009
        Assert.AreEqual(string.Empty, modelFew.GroupNote, "Note should be empty when all matching groups are shown");
    }

    [TestMethod]
    public void MemoryTabBoard_ProcessGroup_CanKillReflectsMemberProtection()
    {
        var lang = new Lang("en");
        var processes = new[]
        {
            // Group "daemon": one PID is protected (root UID 0), one PID is killable (OwnUid)
            new ProcessEntry { Pid = 2001, Name = "daemon", Uid = 0, WorkingSet = 50 * 1024 * 1024 },
            new ProcessEntry { Pid = 2002, Name = "daemon", Uid = OwnUid, WorkingSet = 40 * 1024 * 1024 },

            // Group "kwin_wayland": all members session-critical
            new ProcessEntry { Pid = 3001, Name = "kwin_wayland", Uid = OwnUid, WorkingSet = 100 * 1024 * 1024 },
        };

        var snapshot = new Snapshot { Processes = processes };
        MemoryTabModel model = MemoryTabBoard.Build(snapshot, lang, query: null, ownPid: OwnPid, ownUid: OwnUid, graphedGroup: null);

        MemProcessGroup? daemonGroup = model.Group("daemon");
        Assert.IsNotNull(daemonGroup);
        Assert.IsTrue(daemonGroup.CanKill, "Group with at least one killable member can be killed");
        Assert.AreEqual(1, daemonGroup.KillableCount);
        Assert.AreEqual(2, daemonGroup.Count);

        MemProcessGroup? kwinGroup = model.Group("kwin_wayland");
        Assert.IsNotNull(kwinGroup);
        Assert.IsFalse(kwinGroup.CanKill, "Group where all members are protected cannot be killed");
        Assert.AreEqual(0, kwinGroup.KillableCount);
    }

    // ---------------------------------------------------- Services & Caps

    [TestMethod]
    public void MemoryBoard_Attach_CalculatesSharedWithAndWorkingSetFromHostProcess()
    {
        var processes = new[]
        {
            new ProcessEntry { Pid = 1234, Name = "host", WorkingSet = 30 * 1024 * 1024 },
        };

        var services = new[]
        {
            new ServiceEntry { Name = "serviceA", Pid = 1234, Running = true },
            new ServiceEntry { Name = "serviceB", Pid = 1234, Running = true },
            new ServiceEntry { Name = "serviceC", Pid = 5678, Running = false }, // stopped / no host in processes
        };

        IReadOnlyList<ServiceEntry> attached = MemoryBoard.Attach(services, processes);

        Assert.AreEqual(3, attached.Count);

        ServiceEntry sA = attached.First(s => s.Name == "serviceA");
        Assert.AreEqual(30L * 1024 * 1024, sA.WorkingSet);
        Assert.AreEqual(2, sA.SharedWith); // shared with serviceB

        ServiceEntry sB = attached.First(s => s.Name == "serviceB");
        Assert.AreEqual(30L * 1024 * 1024, sB.WorkingSet);
        Assert.AreEqual(2, sB.SharedWith);

        ServiceEntry sC = attached.First(s => s.Name == "serviceC");
        Assert.AreEqual(0L, sC.WorkingSet);
        Assert.AreEqual(1, sC.SharedWith);
    }

    [TestMethod]
    public void MemoryTabBoard_Services_SeparatesUserAndSystemUnits_AndCapsAt60And120()
    {
        var lang = new Lang("en");
        var services = new List<ServiceEntry>();

        // 50 user units and 50 system units = 100 total
        for (int i = 1; i <= 50; i++)
        {
            services.Add(new ServiceEntry
            {
                Name = $"user-svc-{i:D2}",
                Running = true,
                UserUnit = true,
                Start = ServiceStart.Automatic,
            });
            services.Add(new ServiceEntry
            {
                Name = $"sys-svc-{i:D2}",
                Running = true,
                UserUnit = false,
                Start = ServiceStart.Automatic,
            });
        }

        var snapshot = new Snapshot { Services = services };

        // 1. Without search: capped at 60 total
        MemoryTabModel model = MemoryTabBoard.Build(snapshot, lang, query: null, ownPid: OwnPid, ownUid: OwnUid, graphedGroup: null);
        Assert.AreEqual(60, model.TotalServices);
        Assert.AreEqual("Showing 60 of 100 services · search to see the rest", model.ServiceNote);

        // 2. With search matching 100: capped at 120 (so all 100 shown)
        MemoryTabModel modelSearch = MemoryTabBoard.Build(snapshot, lang, query: "svc", ownPid: OwnPid, ownUid: OwnUid, graphedGroup: null);
        Assert.AreEqual(100, modelSearch.TotalServices);
        Assert.AreEqual(string.Empty, modelSearch.ServiceNote);

        // User units are in UserServices, system units are in SystemServices
        Assert.IsTrue(model.UserServices.All(s => s.UserUnit));
        Assert.IsTrue(model.SystemServices.All(s => !s.UserUnit));
    }

    [TestMethod]
    public void MemService_StatesAndStartupTexts()
    {
        var lang = new Lang("en");

        var running = new MemService
        {
            Entry = new ServiceEntry { Name = "test", Running = true, State = "active", Start = ServiceStart.Automatic, UserUnit = true },
        };
        Assert.IsTrue(running.CanStop);
        Assert.IsFalse(running.CanStart);
        Assert.AreEqual("Running", running.StateText(lang));
        Assert.AreEqual("Automatic", running.StartupText(lang));

        var stopped = new MemService
        {
            Entry = new ServiceEntry { Name = "test", Running = false, State = "inactive", Start = ServiceStart.Manual, UserUnit = true },
        };
        Assert.IsFalse(stopped.CanStop);
        Assert.IsTrue(stopped.CanStart);
        Assert.AreEqual("Stopped", stopped.StateText(lang));
        Assert.AreEqual("Manual", stopped.StartupText(lang));

        var masked = new MemService
        {
            Entry = new ServiceEntry { Name = "test", Running = false, State = "dead", Start = ServiceStart.Disabled, UserUnit = true },
        };
        Assert.IsFalse(masked.CanStart, "Disabled/masked unit cannot be started");
        Assert.AreEqual("Disabled", masked.StartupText(lang));
    }

    [TestMethod]
    public void ServiceControl_BuildsExactUserUnitArguments_AndRejectsInvalidActions()
    {
        var executed = new List<IReadOnlyList<string>>();
        var control = new ServiceControl(args =>
        {
            executed.Add(args);
            return null; // success
        });

        // 1. Start action
        ServiceResult startRes = control.Apply(ServiceControl.Start, "pipewire");
        Assert.IsTrue(startRes.Ok);
        Assert.IsNull(startRes.Error);
        CollectionAssert.AreEqual(new[] { "--user", "start", "--", "pipewire.service" }, (System.Collections.ICollection)executed[0]);

        // 2. Stop action with existing .service suffix
        control.Apply(ServiceControl.Stop, "wireplumber.service");
        CollectionAssert.AreEqual(new[] { "--user", "stop", "--", "wireplumber.service" }, (System.Collections.ICollection)executed[1]);

        // 3. Restart action
        control.Apply(ServiceControl.Restart, "dbus");
        CollectionAssert.AreEqual(new[] { "--user", "restart", "--", "dbus.service" }, (System.Collections.ICollection)executed[2]);

        // 4. SetStartup: Automatic -> enable
        control.SetStartup(automatic: true, "test-unit");
        CollectionAssert.AreEqual(new[] { "--user", "enable", "--", "test-unit.service" }, (System.Collections.ICollection)executed[3]);

        // 5. SetStartup: Manual -> disable
        control.SetStartup(automatic: false, "test-unit");
        CollectionAssert.AreEqual(new[] { "--user", "disable", "--", "test-unit.service" }, (System.Collections.ICollection)executed[4]);

        // 6. Unknown action throws ArgumentOutOfRangeException
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => control.Apply("destroy", "unit"));
    }

    [TestMethod]
    public void ServiceControl_Arguments_UnitNameStartingWithDash_StaysAfterOptionTerminator()
    {
        IReadOnlyList<string> args = ServiceControl.Arguments(ServiceControl.Start, "-dash-unit");

        // The terminator '--' prevents '-dash-unit.service' from being interpreted as an option
        Assert.AreEqual(4, args.Count);
        Assert.AreEqual("--user", args[0]);
        Assert.AreEqual("start", args[1]);
        Assert.AreEqual("--", args[2]);
        Assert.AreEqual("-dash-unit.service", args[3]);
    }

    [TestMethod]
    public void ServiceControl_IsValidUnitName_And_UnitFile_Rejection()
    {
        // Valid unit names
        Assert.IsTrue(ServiceControl.IsValidUnitName("pipewire"));
        Assert.IsTrue(ServiceControl.IsValidUnitName("pipewire.service"));
        Assert.IsTrue(ServiceControl.IsValidUnitName("-dashed"));

        // Invalid unit names: empty, null, contains '/' or NUL
        Assert.IsFalse(ServiceControl.IsValidUnitName(""));
        Assert.IsFalse(ServiceControl.IsValidUnitName(null));
        Assert.IsFalse(ServiceControl.IsValidUnitName("path/with/slash"));
        Assert.IsFalse(ServiceControl.IsValidUnitName("/etc/systemd/system/malicious.service"));
        Assert.IsFalse(ServiceControl.IsValidUnitName("null\0byte"));

        // UnitFile appends .service when missing
        Assert.AreEqual("cups.service", ServiceControl.UnitFile("cups"));
        Assert.AreEqual("cups.service", ServiceControl.UnitFile("cups.service"));
        Assert.AreEqual("-custom.service", ServiceControl.UnitFile("-custom"));

        // UnitFile throws ArgumentException for invalid names
        Assert.ThrowsException<ArgumentException>(() => ServiceControl.UnitFile(""));
        Assert.ThrowsException<ArgumentException>(() => ServiceControl.UnitFile("invalid/name"));
        Assert.ThrowsException<ArgumentException>(() => ServiceControl.UnitFile("null\0name"));
    }

    // ---------------------------------------------------------- Memory Cadence

    [TestMethod]
    public void MemoryCadence_Next_Follows3sAnd10sIntervals_OnlyOnRamTab()
    {
        // 1. Not on RAM tab -> reads nothing
        MemoryCadence.Plan notOnTab = MemoryCadence.Next(
            now: 50000, lastProcessMs: 0, lastServiceMs: 0, onRamTab: false);
        Assert.IsFalse(notOnTab.Processes);
        Assert.IsFalse(notOnTab.Services);

        // 2. On RAM tab, initial read (lastMs = 0) -> both due
        MemoryCadence.Plan initial = MemoryCadence.Next(
            now: 50000, lastProcessMs: 0, lastServiceMs: 0, onRamTab: true);
        Assert.IsTrue(initial.Processes);
        Assert.IsTrue(initial.Services);

        // 3. 2 seconds elapsed: neither is due (process needs 3s, service needs 10s)
        MemoryCadence.Plan at2s = MemoryCadence.Next(
            now: 52000, lastProcessMs: 50000, lastServiceMs: 50000, onRamTab: true);
        Assert.IsFalse(at2s.Processes);
        Assert.IsFalse(at2s.Services);

        // 4. 3.5 seconds elapsed: processes due (>= 3000ms), services NOT due (< 10000ms)
        MemoryCadence.Plan at35s = MemoryCadence.Next(
            now: 53500, lastProcessMs: 50000, lastServiceMs: 50000, onRamTab: true);
        Assert.IsTrue(at35s.Processes);
        Assert.IsFalse(at35s.Services);

        // 5. 10.5 seconds elapsed: both due
        MemoryCadence.Plan at105s = MemoryCadence.Next(
            now: 60500, lastProcessMs: 50000, lastServiceMs: 50000, onRamTab: true);
        Assert.IsTrue(at105s.Processes);
        Assert.IsTrue(at105s.Services);

        // 6. Forced read: ignores intervals once
        MemoryCadence.Plan forced = MemoryCadence.Next(
            now: 50500, lastProcessMs: 50000, lastServiceMs: 50000, onRamTab: true, forced: true);
        Assert.IsTrue(forced.Processes);
        Assert.IsTrue(forced.Services);

        // 7. Clock moved backwards (e.g. NTP sync: now < lastMs) -> due immediately
        Assert.IsTrue(MemoryCadence.Due(now: 40000, lastMs: 50000, intervalMs: 3000));
    }
}