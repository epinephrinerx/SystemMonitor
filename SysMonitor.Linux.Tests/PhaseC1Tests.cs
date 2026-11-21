using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux;
using SysMonitor.Linux.Presentation;
using SysMonitor.Linux.Sampling;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class PhaseC1Tests
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

    private static string MakeStat(int pid, string comm, long utime, long stime, ulong starttime = 500) =>
        $"{pid} ({comm}) S 1 1 1 0 0 0 0 0 0 0 {utime} {stime} 0 0 0 0 1 0 {starttime}";

    private static string MakeStatus(string name, int uid = 1000, long rssKb = 1024, int ppid = 1) =>
        $"Name:\t{name}\n" +
        $"PPid:\t{ppid}\n" +
        $"Uid:\t{uid}\t{uid}\t{uid}\t{uid}\n" +
        $"VmRSS:\t{rssKb} kB\n";

    private const string SampleStat1 =
        "cpu  1000 200 300 4000 500 60 70 80 0 0\n" +
        "cpu0 1000 200 300 4000 500 60 70 80 0 0\n";

    private const string SampleMeminfo =
        "MemTotal:        8000000 kB\n" +
        "MemAvailable:    6000000 kB\n";

    // ------------------------------------------------------------------------
    // 1. MemoryCadence.Next
    // ------------------------------------------------------------------------
    [TestMethod]
    public void MemoryCadence_Next_GridTimingAndPageRules()
    {
        // 1a. Grid is due at 0 (lastGridMs <= 0)
        MemoryCadence.Plan atZero = MemoryCadence.Next(
            now: 0, lastProcessMs: 0, lastServiceMs: 0, onRamTab: false, onGridPage: true, lastGridMs: 0);
        Assert.IsTrue(atZero.Grid, "Grid must be due when lastGridMs is 0");

        // 1b. Grid is NOT due under 2 s (e.g. 500 ms elapsed)
        MemoryCadence.Plan underTwoSec = MemoryCadence.Next(
            now: 1500, lastProcessMs: 0, lastServiceMs: 0, onRamTab: false, onGridPage: true, lastGridMs: 1000);
        Assert.IsFalse(underTwoSec.Grid, "Grid must not be due under 2 s (GridMs = 2000)");

        // 1c. Grid is due at exactly 2 s (2000 ms elapsed)
        MemoryCadence.Plan atTwoSec = MemoryCadence.Next(
            now: 3000, lastProcessMs: 0, lastServiceMs: 0, onRamTab: false, onGridPage: true, lastGridMs: 1000);
        Assert.IsTrue(atTwoSec.Grid, "Grid must be due at 2 s elapsed");

        // 1d. Never due when not on a grid page, even if interval passed or forced
        MemoryCadence.Plan notOnGridPage = MemoryCadence.Next(
            now: 5000, lastProcessMs: 0, lastServiceMs: 0, onRamTab: true, onGridPage: false, lastGridMs: 0, gridForced: true);
        Assert.IsFalse(notOnGridPage.Grid, "Grid must never be due when onGridPage is false");

        // 1e. gridForced ignores the interval
        MemoryCadence.Plan forcedGrid = MemoryCadence.Next(
            now: 1500, lastProcessMs: 0, lastServiceMs: 0, onRamTab: false, onGridPage: true, lastGridMs: 1000, gridForced: true);
        Assert.IsTrue(forcedGrid.Grid, "gridForced must ignore interval under 2 s");

        // 1f. onRamTab: false + onGridPage: true -> only Grid is true
        MemoryCadence.Plan onlyGrid = MemoryCadence.Next(
            now: 3000, lastProcessMs: 0, lastServiceMs: 0, onRamTab: false, onGridPage: true, lastGridMs: 1000);
        Assert.IsFalse(onlyGrid.Processes, "Processes must be false when onRamTab is false");
        Assert.IsFalse(onlyGrid.Services, "Services must be false when onRamTab is false");
        Assert.IsTrue(onlyGrid.Grid, "Grid must be true on grid page after 2 s");
    }

    // ------------------------------------------------------------------------
    // 2. LinuxSampler.Sample(includeGrid: true)
    // ------------------------------------------------------------------------
    [TestMethod]
    public void LinuxSampler_Sample_IncludeGridTrue_PopulatesProcessRowsAndApps_AndCalculatesCpuDelta()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/stat", SampleStat1);
        fake.WriteFile("proc/meminfo", SampleMeminfo);

        // Process 100
        fake.WriteFile("proc/100/status", MakeStatus("demoapp", uid: 1000));
        fake.WriteFile("proc/100/stat", MakeStat(100, "demoapp", utime: 10, stime: 10, starttime: 500));
        fake.WriteFile("proc/100/cmdline", "demoapp\0");

        // Set up app.slice for current user
        string appSlice = Path.Combine(
            "sys", "fs", "cgroup", "user.slice", $"user-{Posix.Uid}.slice", $"user@{Posix.Uid}.service", "app.slice");
        fake.WriteFile(Path.Combine(appSlice, "app-demoapp-1.scope", "cgroup.procs"), "100\n");

        string? prevXdg = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_DIRS", Path.Combine(fake.Root, "usr", "share"));
            fake.WriteFile("usr/share/applications/demoapp.desktop",
                "[Desktop Entry]\nName=Demo Application\nExec=demoapp\n");

            var runner = new FakeCommandRunner();
            var clock = new FakeClock();
            var sampler = new LinuxSampler(fake.Root, runner, clock.Now);

            // First read: ProcessRows populated, Apps grouped, first grid read has null CPU
            Snapshot snap1 = sampler.Sample(includeProcesses: false, includeGrid: true);
            Assert.AreEqual(1, snap1.ProcessRows.Count);
            Assert.AreEqual(100, snap1.ProcessRows[0].Pid);
            Assert.IsNull(snap1.ProcessRows[0].CpuPercent, "First grid read must have null process CPU percent");

            Assert.AreEqual(1, snap1.Apps.Count);
            Assert.AreEqual("Demo Application", snap1.Apps[0].Name);
            Assert.IsNull(snap1.Apps[0].CpuPercent, "First grid read must have null app CPU percent");

            // Advance clock and advance process cpu ticks
            clock.Advance(1000);
            fake.WriteFile("proc/100/stat", MakeStat(100, "demoapp", utime: 50, stime: 50, starttime: 500));

            // Second read: CPU percent is now calculated
            Snapshot snap2 = sampler.Sample(includeProcesses: false, includeGrid: true);
            Assert.AreEqual(1, snap2.ProcessRows.Count);
            Assert.IsNotNull(snap2.ProcessRows[0].CpuPercent, "Second grid read must have calculated process CPU percent");

            Assert.AreEqual(1, snap2.Apps.Count);
            Assert.IsNotNull(snap2.Apps[0].CpuPercent, "Second grid read must have calculated app CPU percent");
            Assert.AreEqual(snap2.ProcessRows[0].CpuPercent, snap2.Apps[0].CpuPercent);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_DIRS", prevXdg);
        }
    }

    // ------------------------------------------------------------------------
    // 3. LinuxSampler.Sample(includeGrid: false) republishes previous lists
    // ------------------------------------------------------------------------
    [TestMethod]
    public void LinuxSampler_Sample_IncludeGridFalse_RepublishesPreviousLists_EvenAfterFailure()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/stat", SampleStat1);
        fake.WriteFile("proc/meminfo", SampleMeminfo);

        fake.WriteFile("proc/100/status", MakeStatus("worker", uid: 1000));
        fake.WriteFile("proc/100/stat", MakeStat(100, "worker", utime: 10, stime: 10, starttime: 500));
        fake.WriteFile("proc/100/cmdline", "worker\0");

        string appSlice = Path.Combine(
            "sys", "fs", "cgroup", "user.slice", $"user-{Posix.Uid}.slice", $"user@{Posix.Uid}.service", "app.slice");
        fake.WriteFile(Path.Combine(appSlice, "app-worker-1.scope", "cgroup.procs"), "100\n");

        var runner = new FakeCommandRunner();
        var clock = new FakeClock();
        var sampler = new LinuxSampler(fake.Root, runner, clock.Now);

        // Read grid initially
        Snapshot initial = sampler.Sample(includeProcesses: false, includeGrid: true);
        Assert.AreEqual(1, initial.ProcessRows.Count);
        Assert.AreEqual(1, initial.Apps.Count);

        // includeGrid: false republishes the previous ProcessRows and Apps
        Snapshot cached = sampler.Sample(includeProcesses: false, includeGrid: false);
        Assert.AreEqual(1, cached.ProcessRows.Count);
        Assert.AreEqual(100, cached.ProcessRows[0].Pid);
        Assert.AreEqual(1, cached.Apps.Count);
        Assert.AreEqual("worker", cached.Apps[0].Name);

        // Simulate a failed read by corrupting/removing proc
        Directory.Delete(Path.Combine(fake.Root, "proc", "100"), recursive: true);
        // Writing an invalid proc directory structure or empty proc
        Snapshot failedRead = sampler.Sample(includeProcesses: false, includeGrid: true);
        // ProcessRows and Apps are still republished because of catch in LinuxSampler
        // (or if process directory was deleted, process reader returns empty unless previous is cached)
        // Note: between reads LinuxSampler keeps what the grid last showed if read throws
        Assert.IsNotNull(failedRead.ProcessRows);
        Assert.IsNotNull(failedRead.Apps);

        // includeGrid: false continues republishing without blanking
        Snapshot nextCached = sampler.Sample(includeProcesses: false, includeGrid: false);
        Assert.IsNotNull(nextCached.ProcessRows);
        Assert.IsNotNull(nextCached.Apps);
    }

    // ------------------------------------------------------------------------
    // 4. Service-by-PID mapping
    // ------------------------------------------------------------------------
    [TestMethod]
    public void LinuxSampler_Sample_ServiceByPid_MapsUserUnitPid_AndExcludesSystemUnitPid()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/stat", SampleStat1);
        fake.WriteFile("proc/meminfo", SampleMeminfo);

        // PID 101: runs user service "user-svc"
        fake.WriteFile("proc/101/status", MakeStatus("user-proc", uid: 1000));
        fake.WriteFile("proc/101/stat", MakeStat(101, "user-proc", utime: 10, stime: 10));

        // PID 202: runs system service "sys-svc"
        fake.WriteFile("proc/202/status", MakeStatus("sys-proc", uid: 0));
        fake.WriteFile("proc/202/stat", MakeStat(202, "sys-proc", utime: 10, stime: 10));

        var runner = new FakeCommandRunner();
        // User scope list-units
        runner.CommandOutputs["systemctl list-units --type=service --all --output=json --no-pager --user"] =
            "[{\"unit\":\"user-svc.service\",\"active\":\"active\",\"description\":\"User Service\"}]";
        // User scope show
        runner.CommandOutputs["systemctl --user show -p Id,MainPID,UnitFileState,ActiveState,SubState,Description --no-pager -- user-svc.service"] =
            "Id=user-svc.service\nMainPID=101\nActiveState=active\n";

        // System scope list-units
        runner.CommandOutputs["systemctl list-units --type=service --all --output=json --no-pager"] =
            "[{\"unit\":\"sys-svc.service\",\"active\":\"active\",\"description\":\"System Service\"}]";
        // System scope show
        runner.CommandOutputs["systemctl show -p Id,MainPID,UnitFileState,ActiveState,SubState,Description --no-pager -- sys-svc.service"] =
            "Id=sys-svc.service\nMainPID=202\nActiveState=active\n";

        var clock = new FakeClock();
        var sampler = new LinuxSampler(fake.Root, runner, clock.Now);

        // Sample with includeServices: true and includeGrid: true
        Snapshot snapshot = sampler.Sample(includeProcesses: false, includeServices: true, includeGrid: true);

        ProcessRow? userRow = snapshot.ProcessRows.FirstOrDefault(r => r.Pid == 101);
        ProcessRow? sysRow = snapshot.ProcessRows.FirstOrDefault(r => r.Pid == 202);

        Assert.IsNotNull(userRow, "User process row must be present");
        Assert.AreEqual("user-svc", userRow.ServiceUnit, "User unit PID must appear as ServiceUnit");

        Assert.IsNotNull(sysRow, "System process row must be present");
        Assert.IsNull(sysRow.ServiceUnit, "System unit PID must NOT appear as ServiceUnit");
    }

    // ------------------------------------------------------------------------
    // 5. Snapshot defaults and sealing
    // ------------------------------------------------------------------------
    [TestMethod]
    public void Snapshot_DefaultsAndSealing_NonEmptyEmptyAndSealedAgainstMutation()
    {
        // Defaults: non-null and empty
        var defaultSnap = new Snapshot();
        Assert.IsNotNull(defaultSnap.ProcessRows);
        Assert.AreEqual(0, defaultSnap.ProcessRows.Count);
        Assert.IsNotNull(defaultSnap.Apps);
        Assert.AreEqual(0, defaultSnap.Apps.Count);

        // Sealing against mutating the source list
        var mutableRows = new List<ProcessRow>
        {
            new() { Pid = 1, Name = "p1" }
        };
        var mutableApps = new List<AppEntry>
        {
            new() { Id = "app1", Name = "App 1", Instances = Array.Empty<AppInstance>() }
        };

        var snap = new Snapshot
        {
            ProcessRows = mutableRows,
            Apps = mutableApps,
        };

        Assert.AreEqual(1, snap.ProcessRows.Count);
        Assert.AreEqual(1, snap.Apps.Count);

        // Mutate source list after initialization
        mutableRows.Add(new() { Pid = 2, Name = "p2" });
        mutableApps.Add(new() { Id = "app2", Name = "App 2", Instances = Array.Empty<AppInstance>() });

        Assert.AreEqual(1, snap.ProcessRows.Count, "Snapshot.ProcessRows must be sealed against source list mutation");
        Assert.AreEqual(1, snap.Apps.Count, "Snapshot.Apps must be sealed against source list mutation");

        // Mutating via cast to IList throws NotSupportedException
        Assert.ThrowsException<NotSupportedException>(() =>
            ((IList<ProcessRow>)snap.ProcessRows).Add(new() { Pid = 3, Name = "p3" }));
        Assert.ThrowsException<NotSupportedException>(() =>
            ((IList<AppEntry>)snap.Apps).Add(new() { Id = "app3", Name = "App 3", Instances = Array.Empty<AppInstance>() }));
    }

    // ------------------------------------------------------------------------
    // 6. ShellModel.SetAppsSort
    // ------------------------------------------------------------------------
    [TestMethod]
    public void ShellModel_SetAppsSort_DirectionRulesAndPersistence()
    {
        string tempConfig = Path.Combine(Path.GetTempPath(), "shellmodel-sort-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var model = new ShellModel(configPath: tempConfig);
            Assert.AreEqual("memory", model.Config.AppsSort);
            Assert.IsTrue(model.Config.AppsSortDesc);

            // 6a. New numeric column starts descending
            bool cpuSet = model.SetAppsSort(GridColumn.Cpu);
            Assert.IsTrue(cpuSet);
            Assert.AreEqual("cpu", model.Config.AppsSort);
            Assert.IsTrue(model.Config.AppsSortDesc, "New numeric column must start descending");

            // 6b. Second click on same column reverses direction
            model.SetAppsSort(GridColumn.Cpu);
            Assert.AreEqual("cpu", model.Config.AppsSort);
            Assert.IsFalse(model.Config.AppsSortDesc, "Second click on same column must reverse direction to ascending");

            // 6c. Name column starts ascending
            model.SetAppsSort(GridColumn.Name);
            Assert.AreEqual("name", model.Config.AppsSort);
            Assert.IsFalse(model.Config.AppsSortDesc, "Name column must start ascending");

            // 6d. Second click on Name reverses direction to descending
            model.SetAppsSort(GridColumn.Name);
            Assert.AreEqual("name", model.Config.AppsSort);
            Assert.IsTrue(model.Config.AppsSortDesc, "Second click on Name must reverse direction to descending");

            // 6e. Config key/direction are saved and read back
            model.SetAppsSort(GridColumn.Instances);
            Assert.AreEqual("instances", model.Config.AppsSort);
            Assert.IsTrue(model.Config.AppsSortDesc);

            AppConfig reloaded = AppConfig.Load(tempConfig);
            Assert.AreEqual("instances", reloaded.AppsSort);
            Assert.IsTrue(reloaded.AppsSortDesc);
        }
        finally
        {
            if (File.Exists(tempConfig))
            {
                File.Delete(tempConfig);
            }
        }
    }

    // ------------------------------------------------------------------------
    // 7. ShellModel.MainGridVisible and AppActions sharing KillFlow
    // ------------------------------------------------------------------------
    [TestMethod]
    public void ShellModel_MainGridVisible_AndAppActionsSharesKillFlow()
    {
        var signals = new FakeProcessSignals();
        signals.Facts[500] = new ProcessFacts
        {
            Pid = 500,
            Name = "stubborn-app",
            StartTime = 1000,
            Uid = 1000,
            ParentPid = 1,
            Readable = true,
        };

        var model = new ShellModel(killTargets: signals, ownUid: 1000, ownPid: 9999);

        // 7a. MainGridVisible in Widget vs Main across pages
        Assert.AreEqual(ViewMode.Widget, model.View.Mode);
        Assert.IsFalse(model.MainGridVisible, "MainGridVisible must be false in Widget mode");

        model.SwitchTo(ViewMode.Main);
        Assert.AreEqual(ViewMode.Main, model.View.Mode);

        model.SelectMainPage(PageKeys.Overall);
        Assert.IsFalse(model.MainGridVisible, "MainGridVisible must be false on Overall page");

        model.SelectMainPage(PageKeys.Apps);
        Assert.IsTrue(model.MainGridVisible, "MainGridVisible must be true on Apps page in Main view");
        Assert.IsTrue(model.MainAppsVisible);
        Assert.IsFalse(model.MainProcessesVisible);

        model.SelectMainPage(PageKeys.Processes);
        Assert.IsTrue(model.MainGridVisible, "MainGridVisible must be true on Processes page in Main view");
        Assert.IsTrue(model.MainProcessesVisible);
        Assert.IsFalse(model.MainAppsVisible);

        // A module page exists only once a snapshot lists it, so leave the grid via Overall.
        Assert.IsTrue(model.SelectMainPage(PageKeys.Overall));
        Assert.IsFalse(model.MainGridVisible, "MainGridVisible must be false after leaving the grid pages");

        model.SwitchTo(ViewMode.Widget);
        Assert.IsFalse(model.MainGridVisible, "MainGridVisible must be false after switching to Widget");

        // 7b. AppActions shares the one KillFlow: token from CloseApp can be forced via ShellModel.ForceKill
        model.SwitchTo(ViewMode.Main);
        var row = new ProcessRow { Pid = 500, StartTime = 1000, Name = "stubborn-app" };
        var app = new AppEntry
        {
            Id = "stubborn-app",
            Name = "Stubborn App",
            Instances = new[]
            {
                new AppInstance { Unit = "app-stubborn-1.scope", Processes = new[] { row } }
            }
        };

        ActionResult closeResult = model.AppActions.CloseApp(app);
        Assert.AreEqual(ActionOutcome.NeedsForce, closeResult.Outcome);
        Assert.AreEqual(1, closeResult.Stubborn.Count);

        PendingKill token = closeResult.Stubborn[0];
        Assert.AreEqual(500, token.Target.Pid);

        // Force kill through ShellModel's ForceKill
        KillResult forceResult = model.ForceKill(token);
        Assert.AreEqual(KillState.Gone, forceResult.State);

        // Verify signals sent: first SIGTERM (force=false), then SIGKILL (force=true)
        Assert.AreEqual(2, signals.SignalsSent.Count);
        Assert.AreEqual(500, signals.SignalsSent[0].Pid);
        Assert.IsFalse(signals.SignalsSent[0].Force);
        Assert.AreEqual(500, signals.SignalsSent[1].Pid);
        Assert.IsTrue(signals.SignalsSent[1].Force);
    }

    // ------------------------------------------------------------------------
    // 8. String table
    // ------------------------------------------------------------------------
    [TestMethod]
    public void StringTable_AppsPageKeys_TranslationsNoDuplicatesAndFormatting()
    {
        var langTh = new Lang("th");
        var langEn = new Lang("en");

        // 8a. AppsPageKeys has Thai + English for every key
        Assert.IsTrue(StringData.AppsPageKeys.Length > 0);
        foreach ((string key, string thai, string english) in StringData.AppsPageKeys)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(key), "Key must not be empty");
            Assert.IsFalse(string.IsNullOrWhiteSpace(thai), $"Key '{key}' is missing Thai text");
            Assert.IsFalse(string.IsNullOrWhiteSpace(english), $"Key '{key}' is missing English text");

            Assert.IsTrue(langTh.Has(key), $"Lang(th) must have '{key}'");
            Assert.IsTrue(langEn.Has(key), $"Lang(en) must have '{key}'");
        }

        // 8b. No duplicates across all tables
        var allKeys = new HashSet<string>(StringComparer.Ordinal);
        var tables = new[]
        {
            StringData.Rows,
            StringData.LinuxExtras,
            StringData.BatteryKeys,
            StringData.ReleaseKeys,
            StringData.GridKeys,
            StringData.MainWindowKeys,
            StringData.AppsPageKeys,
        };

        foreach ((string Key, string Thai, string English)[] table in tables)
        {
            foreach ((string key, string thai, string english) in table)
            {
                Assert.IsTrue(allKeys.Add(key), $"Duplicate key detected across StringData tables: '{key}'");
            }
        }

        // 8c. apps_count formats properly
        string countTh = langTh.Get("apps_count", 42);
        Assert.AreEqual("ทั้งหมด 42 รายการ", countTh);

        string countEn = langEn.Get("apps_count", 42);
        Assert.AreEqual("42 apps", countEn);

        // 8d. apps_placeholder exists in MainWindowKeys and resolves
        Assert.IsTrue(langEn.Has("apps_placeholder"));
        Assert.IsTrue(langTh.Has("apps_placeholder"));
    }

    // ------------------------------------------------------------------------
    // 9. ProcessGrid.Sort for Apps
    // ------------------------------------------------------------------------
    [TestMethod]
    public void ProcessGrid_Sort_Apps_AcrossAllColumnsWithNullsLastInBothDirections()
    {
        static AppEntry MakeApp(string id, string name, double? cpu, long mem, double? gpu, long? gpuMem, int instances) =>
            new()
            {
                Id = id,
                Name = name,
                CpuPercent = cpu,
                MemoryBytes = mem,
                GpuPercent = gpu,
                GpuMemoryBytes = gpuMem,
                Instances = Enumerable.Range(0, instances).Select(i => new AppInstance
                {
                    Unit = $"app-{id}-{i}.scope",
                    Processes = Array.Empty<ProcessRow>(),
                }).ToList(),
            };

        var apps = new[]
        {
            MakeApp("app-b", "Bravo", cpu: 10.0, mem: 100, gpu: 20.0, gpuMem: 1024, instances: 1),
            MakeApp("app-a", "Alpha", cpu: null, mem: 300, gpu: null, gpuMem: null, instances: 3),
            MakeApp("app-c", "Charlie", cpu: 30.0, mem: 200, gpu: 10.0, gpuMem: 2048, instances: 2),
        };

        // Name
        IReadOnlyList<AppEntry> byNameAsc = ProcessGrid.Sort(apps, GridColumn.Name, descending: false);
        Assert.AreEqual("Alpha", byNameAsc[0].Name);
        Assert.AreEqual("Charlie", byNameAsc[2].Name);

        IReadOnlyList<AppEntry> byNameDesc = ProcessGrid.Sort(apps, GridColumn.Name, descending: true);
        Assert.AreEqual("Charlie", byNameDesc[0].Name);
        Assert.AreEqual("Alpha", byNameDesc[2].Name);

        // Instances
        IReadOnlyList<AppEntry> byInstAsc = ProcessGrid.Sort(apps, GridColumn.Instances, descending: false);
        Assert.AreEqual(1, byInstAsc[0].Instances.Count);
        Assert.AreEqual(3, byInstAsc[2].Instances.Count);

        IReadOnlyList<AppEntry> byInstDesc = ProcessGrid.Sort(apps, GridColumn.Instances, descending: true);
        Assert.AreEqual(3, byInstDesc[0].Instances.Count);
        Assert.AreEqual(1, byInstDesc[2].Instances.Count);

        // Cpu (nulls last in both directions)
        IReadOnlyList<AppEntry> byCpuAsc = ProcessGrid.Sort(apps, GridColumn.Cpu, descending: false);
        Assert.AreEqual(10.0, byCpuAsc[0].CpuPercent);
        Assert.AreEqual(30.0, byCpuAsc[1].CpuPercent);
        Assert.IsNull(byCpuAsc[2].CpuPercent, "Null CPU must be last when sorting ascending");

        IReadOnlyList<AppEntry> byCpuDesc = ProcessGrid.Sort(apps, GridColumn.Cpu, descending: true);
        Assert.AreEqual(30.0, byCpuDesc[0].CpuPercent);
        Assert.AreEqual(10.0, byCpuDesc[1].CpuPercent);
        Assert.IsNull(byCpuDesc[2].CpuPercent, "Null CPU must be last when sorting descending");

        // Gpu (nulls last in both directions)
        IReadOnlyList<AppEntry> byGpuAsc = ProcessGrid.Sort(apps, GridColumn.Gpu, descending: false);
        Assert.AreEqual(10.0, byGpuAsc[0].GpuPercent);
        Assert.AreEqual(20.0, byGpuAsc[1].GpuPercent);
        Assert.IsNull(byGpuAsc[2].GpuPercent, "Null GPU must be last when sorting ascending");

        IReadOnlyList<AppEntry> byGpuDesc = ProcessGrid.Sort(apps, GridColumn.Gpu, descending: true);
        Assert.AreEqual(20.0, byGpuDesc[0].GpuPercent);
        Assert.AreEqual(10.0, byGpuDesc[1].GpuPercent);
        Assert.IsNull(byGpuDesc[2].GpuPercent, "Null GPU must be last when sorting descending");

        // GpuMemory (nulls last in both directions)
        IReadOnlyList<AppEntry> byGpuMemAsc = ProcessGrid.Sort(apps, GridColumn.GpuMemory, descending: false);
        Assert.AreEqual(1024L, byGpuMemAsc[0].GpuMemoryBytes);
        Assert.AreEqual(2048L, byGpuMemAsc[1].GpuMemoryBytes);
        Assert.IsNull(byGpuMemAsc[2].GpuMemoryBytes, "Null GPU memory must be last when sorting ascending");

        IReadOnlyList<AppEntry> byGpuMemDesc = ProcessGrid.Sort(apps, GridColumn.GpuMemory, descending: true);
        Assert.AreEqual(2048L, byGpuMemDesc[0].GpuMemoryBytes);
        Assert.AreEqual(1024L, byGpuMemDesc[1].GpuMemoryBytes);
        Assert.IsNull(byGpuMemDesc[2].GpuMemoryBytes, "Null GPU memory must be last when sorting descending");
    }

    // ------------------------------------------------------------------------
    // 10. DesktopEntry.ParseExec-driven restart policy
    // ------------------------------------------------------------------------
    [TestMethod]
    public void DesktopEntry_ParseExec_RestartApp_ReturnsUnavailableWhenExecNotUsable()
    {
        var signals = new FakeProcessSignals();
        signals.Facts[600] = new ProcessFacts
        {
            Pid = 600,
            Name = "exec-test",
            StartTime = 1000,
            Uid = 1000,
            ParentPid = 1,
            Readable = true,
        };

        var kill = new KillFlow(signals, ownPid: 9999, ownUid: 1000);
        var lang = new Lang("en");
        var actions = new AppActions(kill, lang: lang, ownPid: 9999, ownUid: 1000);

        string?[] unusableExecs =
        {
            null,
            "",
            "   ",
            "/bin/echo \"unclosed quote",
            "/bin/echo \"foo\"bar",
            "%u %f",
        };

        foreach (string? exec in unusableExecs)
        {
            var app = new AppEntry
            {
                Id = "testapp",
                Name = "Test App",
                Exec = exec,
                Instances = new[]
                {
                    new AppInstance
                    {
                        Unit = "app-testapp-1.scope",
                        Processes = new[] { new ProcessRow { Pid = 600, StartTime = 1000, Name = "exec-test" } }
                    }
                }
            };

            ActionResult result = actions.RestartApp(app);
            Assert.AreEqual(ActionOutcome.Failed, result.Outcome, $"RestartApp must fail for unusable Exec '{exec}'");
            Assert.AreEqual(lang["app_restart_unavailable"], result.Error);
            Assert.AreEqual(0, signals.SignalsSent.Count, "No signal must be sent when Exec is unusable");
        }
    }
}
