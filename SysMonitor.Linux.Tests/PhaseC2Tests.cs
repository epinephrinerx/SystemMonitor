using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class PhaseC2Tests
{
    // ------------------------------------------------------------------------
    // 1. ShellModel.SetProcsSort
    // ------------------------------------------------------------------------
    [TestMethod]
    public void ShellModel_SetProcsSort_DirectionRulesAndPersistence()
    {
        string tempConfig = Path.Combine(Path.GetTempPath(), "shellmodel-procs-sort-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var model = new ShellModel(configPath: tempConfig);

            // Default configuration
            Assert.AreEqual("memory", model.Config.ProcsSort);
            Assert.IsTrue(model.Config.ProcsSortDesc);

            // 1a. New numeric column starts descending
            bool cpuSet = model.SetProcsSort(GridColumn.Cpu);
            Assert.IsTrue(cpuSet);
            Assert.AreEqual("cpu", model.Config.ProcsSort);
            Assert.IsTrue(model.Config.ProcsSortDesc, "New numeric column must start descending");

            // 1b. Clicking the same column reverses direction to ascending
            model.SetProcsSort(GridColumn.Cpu);
            Assert.AreEqual("cpu", model.Config.ProcsSort);
            Assert.IsFalse(model.Config.ProcsSortDesc, "Second click on same column must reverse direction to ascending");

            // 1c. Clicking the same column a third time reverses direction back to descending
            model.SetProcsSort(GridColumn.Cpu);
            Assert.AreEqual("cpu", model.Config.ProcsSort);
            Assert.IsTrue(model.Config.ProcsSortDesc, "Third click on same column must reverse direction back to descending");

            // 1d. New Name column starts ascending
            model.SetProcsSort(GridColumn.Name);
            Assert.AreEqual("name", model.Config.ProcsSort);
            Assert.IsFalse(model.Config.ProcsSortDesc, "Name column must start ascending");

            // 1e. Second click on Name reverses direction to descending
            model.SetProcsSort(GridColumn.Name);
            Assert.AreEqual("name", model.Config.ProcsSort);
            Assert.IsTrue(model.Config.ProcsSortDesc, "Second click on Name must reverse direction to descending");

            // 1f. Another numeric column (Pid) starts descending
            model.SetProcsSort(GridColumn.Pid);
            Assert.AreEqual("pid", model.Config.ProcsSort);
            Assert.IsTrue(model.Config.ProcsSortDesc, "New numeric column (Pid) must start descending");

            // 1g. Values saved to procs_sort / procs_sort_desc and reloaded from AppConfig
            AppConfig reloaded = AppConfig.Load(tempConfig);
            Assert.AreEqual("pid", reloaded.ProcsSort);
            Assert.IsTrue(reloaded.ProcsSortDesc);

            // Reverse Pid and verify reloaded
            model.SetProcsSort(GridColumn.Pid);
            Assert.AreEqual("pid", model.Config.ProcsSort);
            Assert.IsFalse(model.Config.ProcsSortDesc);

            AppConfig reloaded2 = AppConfig.Load(tempConfig);
            Assert.AreEqual("pid", reloaded2.ProcsSort);
            Assert.IsFalse(reloaded2.ProcsSortDesc);
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
    // 2. ShellModel.CanEndProcess
    // ------------------------------------------------------------------------
    [TestMethod]
    public void ShellModel_CanEndProcess_ProtectionMatrixMatchesProcessProtection()
    {
        const int ownPid = 5000;
        const int ownUid = 1000;
        const int otherUid1 = 0;
        const int otherUid2 = 1001;

        var model = new ShellModel(ownPid: ownPid, ownUid: ownUid);

        // Matrix test cases covering:
        // - normal user process with own UID
        // - other UIDs (0, 1001)
        // - PID 1 and PID 2
        // - own PID
        // - kernel thread
        // - unreadable entry
        // - null UID
        var testCases = new (string Description, ProcessRow Row, bool ExpectedCanEnd)[]
        {
            (
                "Normal user process with own UID",
                new ProcessRow { Pid = 1234, Name = "calculator", Uid = ownUid, Readable = true, KernelThread = false },
                true
            ),
            (
                "Other UID (root, UID 0)",
                new ProcessRow { Pid = 1234, Name = "root-daemon", Uid = otherUid1, Readable = true, KernelThread = false },
                false
            ),
            (
                "Other UID (another normal user, UID 1001)",
                new ProcessRow { Pid = 1234, Name = "other-calc", Uid = otherUid2, Readable = true, KernelThread = false },
                false
            ),
            (
                "PID 1 (init/systemd)",
                new ProcessRow { Pid = 1, Name = "custom-init", Uid = ownUid, Readable = true, KernelThread = false },
                false
            ),
            (
                "PID 2 (kthreadd)",
                new ProcessRow { Pid = 2, Name = "custom-kthread", Uid = ownUid, Readable = true, KernelThread = false },
                false
            ),
            (
                "Own PID",
                new ProcessRow { Pid = ownPid, Name = "sysmonitor", Uid = ownUid, Readable = true, KernelThread = false },
                false
            ),
            (
                "Kernel thread",
                new ProcessRow { Pid = 1234, Name = "kworker", Uid = ownUid, Readable = true, KernelThread = true },
                false
            ),
            (
                "Unreadable entry",
                new ProcessRow { Pid = 1234, Name = "unreadable", Uid = ownUid, Readable = false, KernelThread = false },
                false
            ),
            (
                "Null UID (unknown owner)",
                new ProcessRow { Pid = 1234, Name = "unknown-owner", Uid = null, Readable = true, KernelThread = false },
                false
            ),
        };

        foreach (var (description, row, expectedCanEnd) in testCases)
        {
            var entry = new ProcessEntry
            {
                Pid = row.Pid,
                Name = row.Name,
                Uid = row.Uid,
                Readable = row.Readable,
                KernelThread = row.KernelThread,
            };

            bool isProtected = ProcessProtection.IsProtected(entry, ownPid: ownPid, ownUid: ownUid);
            bool canEnd = model.CanEndProcess(row);

            Assert.AreEqual(expectedCanEnd, canEnd, $"CanEndProcess returned unexpected result for: {description}");
            Assert.AreEqual(!isProtected, canEnd, $"CanEndProcess must match !ProcessProtection.IsProtected for: {description}");
        }
    }

    [TestMethod]
    public void ShellModel_CanEndProcess_SessionCriticalNamesAllProtected()
    {
        const int ownPid = 5000;
        const int ownUid = 1000;

        var model = new ShellModel(ownPid: ownPid, ownUid: ownUid);

        Assert.IsTrue(ProcessProtection.SessionNames.Count > 0, "SessionNames must not be empty");

        // Verify every entry in ProcessProtection.SessionNames
        foreach (string sessionName in ProcessProtection.SessionNames)
        {
            // Exact casing
            var rowExact = new ProcessRow
            {
                Pid = 8888,
                Name = sessionName,
                Uid = ownUid,
                Readable = true,
                KernelThread = false,
            };

            var entryExact = new ProcessEntry
            {
                Pid = rowExact.Pid,
                Name = rowExact.Name,
                Uid = rowExact.Uid,
                Readable = rowExact.Readable,
                KernelThread = rowExact.KernelThread,
            };

            Assert.IsTrue(ProcessProtection.IsProtected(entryExact, ownPid: ownPid, ownUid: ownUid),
                $"ProcessProtection.IsProtected must be true for session name '{sessionName}'");
            Assert.IsFalse(model.CanEndProcess(rowExact),
                $"CanEndProcess must be false for session name '{sessionName}'");
            Assert.AreEqual(!ProcessProtection.IsProtected(entryExact, ownPid, ownUid), model.CanEndProcess(rowExact));

            // Upper case (case-insensitivity check)
            var rowUpper = new ProcessRow
            {
                Pid = 8889,
                Name = sessionName.ToUpperInvariant(),
                Uid = ownUid,
                Readable = true,
                KernelThread = false,
            };

            var entryUpper = new ProcessEntry
            {
                Pid = rowUpper.Pid,
                Name = rowUpper.Name,
                Uid = rowUpper.Uid,
                Readable = rowUpper.Readable,
                KernelThread = rowUpper.KernelThread,
            };

            Assert.IsTrue(ProcessProtection.IsProtected(entryUpper, ownPid: ownPid, ownUid: ownUid),
                $"ProcessProtection.IsProtected must be true for uppercase session name '{sessionName.ToUpperInvariant()}'");
            Assert.IsFalse(model.CanEndProcess(rowUpper),
                $"CanEndProcess must be false for uppercase session name '{sessionName.ToUpperInvariant()}'");
            Assert.AreEqual(!ProcessProtection.IsProtected(entryUpper, ownPid, ownUid), model.CanEndProcess(rowUpper));
        }
    }

    // ------------------------------------------------------------------------
    // 3. StringData.ProcessesPageKeys
    // ------------------------------------------------------------------------
    [TestMethod]
    public void StringData_ProcessesPageKeys_TranslationsFormattingAndNoDuplicates()
    {
        var langTh = new Lang("th");
        var langEn = new Lang("en");

        // 3a. Thai + English present for every key in ProcessesPageKeys
        Assert.IsTrue(StringData.ProcessesPageKeys.Length > 0, "ProcessesPageKeys must not be empty");
        foreach ((string key, string thai, string english) in StringData.ProcessesPageKeys)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(key), "Key must not be empty");
            Assert.IsFalse(string.IsNullOrWhiteSpace(thai), $"Key '{key}' is missing Thai translation");
            Assert.IsFalse(string.IsNullOrWhiteSpace(english), $"Key '{key}' is missing English translation");

            Assert.IsTrue(langTh.Has(key), $"Lang(th) must have key '{key}'");
            Assert.IsTrue(langEn.Has(key), $"Lang(en) must have key '{key}'");
            Assert.AreNotEqual(key, langTh[key], $"Key '{key}' should not fall back to key name in Thai");
            Assert.AreNotEqual(key, langEn[key], $"Key '{key}' should not fall back to key name in English");
        }

        // 3b. procs_count formats in both languages
        string countTh = langTh.Get("procs_count", 42);
        Assert.AreEqual("ทั้งหมด 42 โปรเซส", countTh);

        string countEn = langEn.Get("procs_count", 42);
        Assert.AreEqual("42 processes", countEn);

        // Additional format checks
        Assert.AreEqual("ทั้งหมด 0 โปรเซส", langTh.Get("procs_count", 0));
        Assert.AreEqual("0 processes", langEn.Get("procs_count", 0));

        // 3c. No duplicate keys across all eight tables
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
            StringData.ProcessesPageKeys,
        };

        Assert.AreEqual(8, tables.Length, "Must have exactly eight string tables");

        foreach ((string Key, string Thai, string English)[] table in tables)
        {
            foreach ((string key, string thai, string english) in table)
            {
                Assert.IsTrue(allKeys.Add(key), $"Duplicate key detected across StringData tables: '{key}'");
            }
        }
    }

    // ------------------------------------------------------------------------
    // 4. ProcessGrid process filter/sort
    // ------------------------------------------------------------------------
    [TestMethod]
    public void ProcessGrid_Filter_ByCommandUserPidAndName_CaseInsensitive()
    {
        var rows = new[]
        {
            new ProcessRow
            {
                Pid = 101,
                Name = "TerminalApp",
                User = "alice",
                Command = "/usr/bin/bash --login",
                MemoryBytes = 1000,
            },
            new ProcessRow
            {
                Pid = 202,
                Name = "code-oss",
                User = "bob",
                Command = "code --type=renderer",
                MemoryBytes = 2000,
            },
            new ProcessRow
            {
                Pid = 303,
                Name = "systemd-journal",
                User = "root",
                Command = "/usr/lib/systemd/systemd-journald",
                MemoryBytes = 500,
            },
            new ProcessRow
            {
                Pid = 404,
                Name = "chrome",
                User = "charlie",
                Command = "/opt/google/chrome/chrome --no-sandbox",
                MemoryBytes = 3000,
            },
        };

        // 4a. Filter by command (case-insensitive substring)
        IReadOnlyList<ProcessRow> byCmd = ProcessGrid.Filter(rows, "RENDERER");
        Assert.AreEqual(1, byCmd.Count);
        Assert.AreEqual(202, byCmd[0].Pid);

        IReadOnlyList<ProcessRow> byCmd2 = ProcessGrid.Filter(rows, "--login");
        Assert.AreEqual(1, byCmd2.Count);
        Assert.AreEqual(101, byCmd2[0].Pid);

        // 4b. Filter by user (case-insensitive substring)
        IReadOnlyList<ProcessRow> byUser = ProcessGrid.Filter(rows, "ALICE");
        Assert.AreEqual(1, byUser.Count);
        Assert.AreEqual("alice", byUser[0].User);

        IReadOnlyList<ProcessRow> byUserRoot = ProcessGrid.Filter(rows, "root");
        Assert.AreEqual(1, byUserRoot.Count);
        Assert.AreEqual(303, byUserRoot[0].Pid);

        // 4c. Filter by PID (exact and substring)
        IReadOnlyList<ProcessRow> byPidExact = ProcessGrid.Filter(rows, "202");
        Assert.AreEqual(1, byPidExact.Count);
        Assert.AreEqual(202, byPidExact[0].Pid);

        IReadOnlyList<ProcessRow> byPidSub = ProcessGrid.Filter(rows, "0");
        Assert.AreEqual(4, byPidSub.Count, "All four processes have '0' in their PID (101, 202, 303, 404)");

        // 4d. Filter by name (case-insensitive substring)
        IReadOnlyList<ProcessRow> byName = ProcessGrid.Filter(rows, "terminal");
        Assert.AreEqual(1, byName.Count);
        Assert.AreEqual("TerminalApp", byName[0].Name);

        IReadOnlyList<ProcessRow> byNameUpper = ProcessGrid.Filter(rows, "CHROME");
        Assert.AreEqual(1, byNameUpper.Count);
        Assert.AreEqual("chrome", byNameUpper[0].Name);

        // 4e. Empty, whitespace and null query return all rows unchanged
        Assert.AreEqual(4, ProcessGrid.Filter(rows, null).Count);
        Assert.AreEqual(4, ProcessGrid.Filter(rows, "").Count);
        Assert.AreEqual(4, ProcessGrid.Filter(rows, "   ").Count);

        // 4f. Non-matching query returns empty list
        Assert.AreEqual(0, ProcessGrid.Filter(rows, "does-not-exist").Count);
    }

    [TestMethod]
    public void ProcessGrid_Sort_ByUserAndPid_AscendingAndDescending()
    {
        var rows = new[]
        {
            new ProcessRow { Pid = 30, Name = "delta", User = "charlie", MemoryBytes = 100 },
            new ProcessRow { Pid = 10, Name = "bravo", User = "alice", MemoryBytes = 100 },
            new ProcessRow { Pid = 40, Name = "alpha", User = "alice", MemoryBytes = 100 },
            new ProcessRow { Pid = 20, Name = "charlie", User = "bob", MemoryBytes = 100 },
        };

        // Sort by User ascending: alice (alpha, bravo), bob (charlie), charlie (delta)
        IReadOnlyList<ProcessRow> userAsc = ProcessGrid.Sort(rows, GridColumn.User, descending: false);
        Assert.AreEqual("alice", userAsc[0].User);
        Assert.AreEqual("alpha", userAsc[0].Name); // tie-break by name
        Assert.AreEqual("alice", userAsc[1].User);
        Assert.AreEqual("bravo", userAsc[1].Name);
        Assert.AreEqual("bob", userAsc[2].User);
        Assert.AreEqual("charlie", userAsc[3].User);

        // Sort by User descending: charlie, bob, alice (alpha, bravo)
        IReadOnlyList<ProcessRow> userDesc = ProcessGrid.Sort(rows, GridColumn.User, descending: true);
        Assert.AreEqual("charlie", userDesc[0].User);
        Assert.AreEqual("bob", userDesc[1].User);
        Assert.AreEqual("alice", userDesc[2].User);
        Assert.AreEqual("alpha", userDesc[2].Name); // tie-break by name
        Assert.AreEqual("alice", userDesc[3].User);
        Assert.AreEqual("bravo", userDesc[3].Name);

        // Sort by PID ascending: 10, 20, 30, 40
        IReadOnlyList<ProcessRow> pidAsc = ProcessGrid.Sort(rows, GridColumn.Pid, descending: false);
        Assert.AreEqual(10, pidAsc[0].Pid);
        Assert.AreEqual(20, pidAsc[1].Pid);
        Assert.AreEqual(30, pidAsc[2].Pid);
        Assert.AreEqual(40, pidAsc[3].Pid);

        // Sort by PID descending: 40, 30, 20, 10
        IReadOnlyList<ProcessRow> pidDesc = ProcessGrid.Sort(rows, GridColumn.Pid, descending: true);
        Assert.AreEqual(40, pidDesc[0].Pid);
        Assert.AreEqual(30, pidDesc[1].Pid);
        Assert.AreEqual(20, pidDesc[2].Pid);
        Assert.AreEqual(10, pidDesc[3].Pid);
    }

    [TestMethod]
    public void ProcessGrid_Sort_NullCpuAndGpuLastInBothDirections()
    {
        var rows = new[]
        {
            new ProcessRow { Pid = 1, Name = "proc-mid", CpuPercent = 15.0, GpuPercent = 10.0, GpuMemoryBytes = 1024L },
            new ProcessRow { Pid = 2, Name = "proc-null1", CpuPercent = null, GpuPercent = null, GpuMemoryBytes = null },
            new ProcessRow { Pid = 3, Name = "proc-high", CpuPercent = 50.0, GpuPercent = 40.0, GpuMemoryBytes = 4096L },
            new ProcessRow { Pid = 4, Name = "proc-low", CpuPercent = 5.0, GpuPercent = 20.0, GpuMemoryBytes = 512L },
            new ProcessRow { Pid = 5, Name = "proc-null2", CpuPercent = null, GpuPercent = null, GpuMemoryBytes = null },
        };

        // --- CPU Ascending (nulls last) ---
        IReadOnlyList<ProcessRow> cpuAsc = ProcessGrid.Sort(rows, GridColumn.Cpu, descending: false);
        Assert.AreEqual(5.0, cpuAsc[0].CpuPercent);
        Assert.AreEqual(15.0, cpuAsc[1].CpuPercent);
        Assert.AreEqual(50.0, cpuAsc[2].CpuPercent);
        Assert.IsNull(cpuAsc[3].CpuPercent, "Null CPU must be last when sorting ascending");
        Assert.IsNull(cpuAsc[4].CpuPercent, "Null CPU must be last when sorting ascending");

        // --- CPU Descending (nulls last) ---
        IReadOnlyList<ProcessRow> cpuDesc = ProcessGrid.Sort(rows, GridColumn.Cpu, descending: true);
        Assert.AreEqual(50.0, cpuDesc[0].CpuPercent);
        Assert.AreEqual(15.0, cpuDesc[1].CpuPercent);
        Assert.AreEqual(5.0, cpuDesc[2].CpuPercent);
        Assert.IsNull(cpuDesc[3].CpuPercent, "Null CPU must be last when sorting descending");
        Assert.IsNull(cpuDesc[4].CpuPercent, "Null CPU must be last when sorting descending");

        // --- GPU Ascending (nulls last) ---
        IReadOnlyList<ProcessRow> gpuAsc = ProcessGrid.Sort(rows, GridColumn.Gpu, descending: false);
        Assert.AreEqual(10.0, gpuAsc[0].GpuPercent);
        Assert.AreEqual(20.0, gpuAsc[1].GpuPercent);
        Assert.AreEqual(40.0, gpuAsc[2].GpuPercent);
        Assert.IsNull(gpuAsc[3].GpuPercent, "Null GPU must be last when sorting ascending");
        Assert.IsNull(gpuAsc[4].GpuPercent, "Null GPU must be last when sorting ascending");

        // --- GPU Descending (nulls last) ---
        IReadOnlyList<ProcessRow> gpuDesc = ProcessGrid.Sort(rows, GridColumn.Gpu, descending: true);
        Assert.AreEqual(40.0, gpuDesc[0].GpuPercent);
        Assert.AreEqual(20.0, gpuDesc[1].GpuPercent);
        Assert.AreEqual(10.0, gpuDesc[2].GpuPercent);
        Assert.IsNull(gpuDesc[3].GpuPercent, "Null GPU must be last when sorting descending");
        Assert.IsNull(gpuDesc[4].GpuPercent, "Null GPU must be last when sorting descending");

        // --- GPU Memory Ascending (nulls last) ---
        IReadOnlyList<ProcessRow> gpuMemAsc = ProcessGrid.Sort(rows, GridColumn.GpuMemory, descending: false);
        Assert.AreEqual(512L, gpuMemAsc[0].GpuMemoryBytes);
        Assert.AreEqual(1024L, gpuMemAsc[1].GpuMemoryBytes);
        Assert.AreEqual(4096L, gpuMemAsc[2].GpuMemoryBytes);
        Assert.IsNull(gpuMemAsc[3].GpuMemoryBytes, "Null GPU memory must be last when sorting ascending");
        Assert.IsNull(gpuMemAsc[4].GpuMemoryBytes, "Null GPU memory must be last when sorting ascending");

        // --- GPU Memory Descending (nulls last) ---
        IReadOnlyList<ProcessRow> gpuMemDesc = ProcessGrid.Sort(rows, GridColumn.GpuMemory, descending: true);
        Assert.AreEqual(4096L, gpuMemDesc[0].GpuMemoryBytes);
        Assert.AreEqual(1024L, gpuMemDesc[1].GpuMemoryBytes);
        Assert.AreEqual(512L, gpuMemDesc[2].GpuMemoryBytes);
        Assert.IsNull(gpuMemDesc[3].GpuMemoryBytes, "Null GPU memory must be last when sorting descending");
        Assert.IsNull(gpuMemDesc[4].GpuMemoryBytes, "Null GPU memory must be last when sorting descending");
    }
}
