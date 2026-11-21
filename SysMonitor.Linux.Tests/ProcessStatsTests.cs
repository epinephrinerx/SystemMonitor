using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class ProcessStatsTests
{
    private static string MakeStat(int pid, string comm, long utime, long stime, ulong starttime) =>
        $"{pid} ({comm}) S 1 1 1 0 0 0 0 0 0 0 {utime} {stime} 0 0 0 0 1 0 {starttime}";

    private static string MakeStatus(string name, int uid = 1000, long rssKb = 1024, int ppid = 1) =>
        $"Name:\t{name}\n" +
        $"PPid:\t{ppid}\n" +
        $"Uid:\t{uid}\t{uid}\t{uid}\t{uid}\n" +
        $"VmRSS:\t{rssKb} kB\n";

    [TestMethod]
    public void Read_CpuDelta_ComputesExactPercentForFourLogicalCpusAndClockTicks100()
    {
        using var fake = new FakeRoot();

        // 4 logical CPUs, 100 clock ticks (USER_HZ)
        // Interval: 1000 ms to 2000 ms (1.0 second elapsed)
        // Capacity = 1.0 s * 100 ticks/s * 4 cpus = 400 ticks capacity
        // Process utime+stime goes from 20 to 100 (delta = 80 ticks)
        // CpuPercent = 80 / 400 * 100 = 20.0 %
        fake.WriteFile("proc/100/status", MakeStatus("worker"));
        fake.WriteFile("proc/100/stat", MakeStat(100, "worker", utime: 10, stime: 10, starttime: 500));
        fake.WriteFile("proc/100/cmdline", "worker\0");

        var stats = new ProcessStats(fake.Root, logicalCpus: 4, clockTicks: 100);

        // First read: first sight of process -> CpuPercent is null
        IReadOnlyList<ProcessRow> read1 = stats.Read(stampMs: 1000);
        Assert.AreEqual(1, read1.Count);
        Assert.AreEqual(100, read1[0].Pid);
        Assert.IsNull(read1[0].CpuPercent, "First read must yield null CPU percent");

        // Second read: 1 second later with 80 ticks delta
        fake.WriteFile("proc/100/stat", MakeStat(100, "worker", utime: 50, stime: 50, starttime: 500));
        IReadOnlyList<ProcessRow> read2 = stats.Read(stampMs: 2000);

        Assert.AreEqual(1, read2.Count);
        Assert.AreEqual(100, read2[0].Pid);
        Assert.IsNotNull(read2[0].CpuPercent);
        Assert.AreEqual(20.0, read2[0].CpuPercent ?? double.NaN, 0.001);
    }

    [TestMethod]
    public void Read_FirstRead_ReturnsNullCpuPercent()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/101/status", MakeStatus("app"));
        fake.WriteFile("proc/101/stat", MakeStat(101, "app", utime: 30, stime: 15, starttime: 1200));

        var stats = new ProcessStats(fake.Root, logicalCpus: 2, clockTicks: 100);
        IReadOnlyList<ProcessRow> rows = stats.Read(stampMs: 1000);

        Assert.AreEqual(1, rows.Count);
        Assert.IsNull(rows[0].CpuPercent);
    }

    [TestMethod]
    public void Read_RecycledPid_ReturnsNullNotDelta()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/200/status", MakeStatus("first-app"));
        fake.WriteFile("proc/200/stat", MakeStat(200, "first-app", utime: 100, stime: 50, starttime: 1000));

        var stats = new ProcessStats(fake.Root, logicalCpus: 4, clockTicks: 100);
        stats.Read(stampMs: 1000);

        // PID 200 is recycled by a new process with a new start time
        fake.WriteFile("proc/200/status", MakeStatus("second-app"));
        fake.WriteFile("proc/200/stat", MakeStat(200, "second-app", utime: 10, stime: 5, starttime: 2000));

        IReadOnlyList<ProcessRow> rows = stats.Read(stampMs: 2000);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(200, rows[0].Pid);
        Assert.AreEqual((ulong)2000, rows[0].StartTime);
        Assert.IsNull(rows[0].CpuPercent, "Recycled PID with new StartTime must yield null CPU percent");
    }

    [TestMethod]
    public void Read_VanishedProcess_LeavesCache()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/300/status", MakeStatus("transient"));
        fake.WriteFile("proc/300/stat", MakeStat(300, "transient", utime: 10, stime: 10, starttime: 1000));

        var stats = new ProcessStats(fake.Root, logicalCpus: 4, clockTicks: 100);
        stats.Read(stampMs: 1000);

        // Process vanishes in read 2
        Directory.Delete(Path.Combine(fake.Root, "proc", "300"), recursive: true);
        IReadOnlyList<ProcessRow> read2 = stats.Read(stampMs: 2000);
        Assert.AreEqual(0, read2.Count);

        // Process reappears in read 3 with same PID and StartTime
        fake.WriteFile("proc/300/status", MakeStatus("transient"));
        fake.WriteFile("proc/300/stat", MakeStat(300, "transient", utime: 50, stime: 50, starttime: 1000));
        IReadOnlyList<ProcessRow> read3 = stats.Read(stampMs: 3000);

        Assert.AreEqual(1, read3.Count);
        Assert.IsNull(read3[0].CpuPercent, "Evicted process seen again must yield null CPU percent (fresh sight)");
    }

    [TestMethod]
    public void Read_PasswdLookup_MapsUidToNameAndFallsBackToNumber()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("etc/passwd",
            "root:x:0:0:root:/root:/bin/bash\n" +
            "alice:x:1000:1000:Alice:/home/alice:/bin/bash\n" +
            "bob:x:1001:1001:Bob:/home/bob:/bin/bash\n");

        fake.WriteFile("proc/10/status", MakeStatus("proc-alice", uid: 1000));
        fake.WriteFile("proc/10/stat", MakeStat(10, "proc-alice", 0, 0, 100));

        fake.WriteFile("proc/20/status", MakeStatus("proc-unknown", uid: 9999));
        fake.WriteFile("proc/20/stat", MakeStat(20, "proc-unknown", 0, 0, 100));

        var stats = new ProcessStats(fake.Root, logicalCpus: 1, clockTicks: 100);
        IReadOnlyList<ProcessRow> rows = stats.Read(stampMs: 1000);

        Assert.AreEqual(2, rows.Count);
        ProcessRow rowAlice = rows.First(r => r.Pid == 10);
        ProcessRow rowUnknown = rows.First(r => r.Pid == 20);

        Assert.AreEqual("alice", rowAlice.User);
        Assert.AreEqual("9999", rowUnknown.User);
    }

    [TestMethod]
    public void Read_CommandLine_ReplacesNulsWithSpacesAndTruncatesAt4096Chars()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/400/status", MakeStatus("cmd-test"));
        fake.WriteFile("proc/400/stat", MakeStat(400, "cmd-test", 0, 0, 100));
        fake.WriteFile("proc/400/cmdline", "python3\0script.py\0--verbose\0");

        string longArg = new string('x', 5000);
        fake.WriteFile("proc/401/status", MakeStatus("long-test"));
        fake.WriteFile("proc/401/stat", MakeStat(401, "long-test", 0, 0, 100));
        fake.WriteFile("proc/401/cmdline", $"app\0{longArg}\0");

        var stats = new ProcessStats(fake.Root, logicalCpus: 1, clockTicks: 100);
        IReadOnlyList<ProcessRow> rows = stats.Read(stampMs: 1000);

        ProcessRow normal = rows.First(r => r.Pid == 400);
        Assert.AreEqual("python3 script.py --verbose", normal.Command);

        ProcessRow truncated = rows.First(r => r.Pid == 401);
        Assert.AreEqual(4096, truncated.Command.Length);
    }

    [TestMethod]
    public void Read_CgroupPath_ParsesZeroColonColonUnifiedLine()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/500/status", MakeStatus("cgroup-test"));
        fake.WriteFile("proc/500/stat", MakeStat(500, "cgroup-test", 0, 0, 100));
        fake.WriteFile("proc/500/cgroup",
            "1:name=systemd:/user.slice\n" +
            "0::/user.slice/user-1000.slice/app.slice/app-konsole.scope\n");

        var stats = new ProcessStats(fake.Root, logicalCpus: 1, clockTicks: 100);
        IReadOnlyList<ProcessRow> rows = stats.Read(stampMs: 1000);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("/user.slice/user-1000.slice/app.slice/app-konsole.scope", rows[0].CgroupPath);
    }

    [TestMethod]
    public void Read_CommContainingParenthesesAndSpaces_DoesNotBreakFieldParsing()
    {
        using var fake = new FakeRoot();
        // comm contains ") (" and spaces: name is "weird ) ( comm"
        string statContent = "600 (weird ) ( comm) S 1 1 1 0 0 0 0 0 0 0 25 35 0 0 0 0 1 0 8888";
        fake.WriteFile("proc/600/status", MakeStatus("weird ) ( comm"));
        fake.WriteFile("proc/600/stat", statContent);

        var stats = new ProcessStats(fake.Root, logicalCpus: 1, clockTicks: 100);
        IReadOnlyList<ProcessRow> rows = stats.Read(stampMs: 1000);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual((ulong)8888, rows[0].StartTime);
    }

    // --- Per-process GPU tests ---

    [TestMethod]
    public void ProcessGpu_ReadsMemoryRegions_AndDeduplicatesFdsOfSameClient()
    {
        using var fake = new FakeRoot();
        fake.CreateDirectory("proc/700/fd");
        fake.CreateDirectory("proc/700/fdinfo");

        // Two fds pointing to /dev/dri/renderD128 for the SAME DRM client (client-id 42, pdev 0000:00:02.0)
        fake.CreateSymlink("proc/700/fd/3", "/dev/dri/renderD128");
        fake.CreateSymlink("proc/700/fd/4", "/dev/dri/renderD128");

        string fdinfoContent =
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t42\n" +
            "drm-engine-render:\t1000000000 ns\n" +
            "drm-memory-vram:\t1024 KiB\n" +
            "drm-total-system:\t2 MiB\n";

        fake.WriteFile("proc/700/fdinfo/3", fdinfoContent);
        fake.WriteFile("proc/700/fdinfo/4", fdinfoContent);

        var gpu = new ProcessGpu(fake.Root);
        var processes = new[] { new ProcessIdentity(700, 1000) };

        IReadOnlyDictionary<int, GpuUsage> usage = gpu.Read(stamp: 0, processes);

        Assert.IsTrue(usage.ContainsKey(700));
        // First sight: Percent is null
        Assert.IsNull(usage[700].Percent);
        // Memory: 1024 KiB (1048576) + 2 MiB (2097152) = 3145728 bytes (counted once, not doubled!)
        Assert.AreEqual(3145728L, usage[700].MemoryBytes);
    }

    [TestMethod]
    public void ProcessGpu_BusiestEngineDelta_ComputesPercent()
    {
        using var fake = new FakeRoot();
        fake.CreateDirectory("proc/710/fd");
        fake.CreateDirectory("proc/710/fdinfo");
        fake.CreateSymlink("proc/710/fd/3", "/dev/dri/renderD128");

        // First sample at stamp 0: render=1s, copy=0.5s
        fake.WriteFile("proc/710/fdinfo/3",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t42\n" +
            "drm-engine-render:\t1000000000 ns\n" +
            "drm-engine-copy:\t500000000 ns\n");

        var gpu = new ProcessGpu(fake.Root);
        var processes = new[] { new ProcessIdentity(710, 1000) };
        gpu.Read(stamp: 0, processes);

        // Second sample at stamp 5000 (elapsed 5 s = 5,000,000,000 ns >= 4000 ms IntervalMs)
        // render delta = 500,000,000 ns (busiest)
        // copy delta = 200,000,000 ns
        // GPU percent = 500,000,000 / 5,000,000,000 * 100 = 10.0 %
        fake.WriteFile("proc/710/fdinfo/3",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t42\n" +
            "drm-engine-render:\t1500000000 ns\n" +
            "drm-engine-copy:\t700000000 ns\n");

        IReadOnlyDictionary<int, GpuUsage> second = gpu.Read(stamp: 5000, processes);

        Assert.IsTrue(second.ContainsKey(710));
        Assert.IsNotNull(second[710].Percent);
        Assert.AreEqual(10.0, second[710].Percent ?? double.NaN, 0.001);
    }

    [TestMethod]
    public void ProcessGpu_WithoutMemoryLines_ReturnsNullMemory()
    {
        using var fake = new FakeRoot();
        fake.CreateDirectory("proc/720/fd");
        fake.CreateDirectory("proc/720/fdinfo");
        fake.CreateSymlink("proc/720/fd/3", "/dev/dri/renderD128");

        fake.WriteFile("proc/720/fdinfo/3",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t42\n" +
            "drm-engine-render:\t1000000000 ns\n");

        var gpu = new ProcessGpu(fake.Root);
        var processes = new[] { new ProcessIdentity(720, 1000) };
        IReadOnlyDictionary<int, GpuUsage> usage = gpu.Read(stamp: 0, processes);

        Assert.IsTrue(usage.ContainsKey(720));
        Assert.IsNull(usage[720].MemoryBytes);
    }

    [TestMethod]
    public void ProcessGpu_NvidiaFdinfoWithoutEngines_ReturnsNulls()
    {
        using var fake = new FakeRoot();
        fake.CreateDirectory("proc/730/fd");
        fake.CreateDirectory("proc/730/fdinfo");
        fake.CreateSymlink("proc/730/fd/3", "/dev/dri/card0");

        // NVIDIA proprietary style: drm-pdev and drm-client-id, but no drm-engine-* or drm-memory-*
        fake.WriteFile("proc/730/fdinfo/3",
            "drm-pdev:\t0000:01:00.0\n" +
            "drm-client-id:\t15\n");

        var gpu = new ProcessGpu(fake.Root);
        var processes = new[] { new ProcessIdentity(730, 1000) };
        gpu.Read(stamp: 0, processes);

        IReadOnlyDictionary<int, GpuUsage> usage = gpu.Read(stamp: 5000, processes);

        Assert.IsTrue(usage.ContainsKey(730));
        Assert.IsNull(usage[730].Percent);
        Assert.IsNull(usage[730].MemoryBytes);
    }

    [TestMethod]
    public void ProcessGpu_ProcessWithoutDevDriFds_IsNotScanned()
    {
        using var fake = new FakeRoot();
        fake.CreateDirectory("proc/740/fd");
        fake.CreateDirectory("proc/740/fdinfo");

        // fd 3 points to /tmp/somefile (not /dev/dri/*)
        fake.CreateSymlink("proc/740/fd/3", "/tmp/somefile");
        // fdinfo 3 is completely malformed; if read or parsed it would be abnormal
        fake.WriteFile("proc/740/fdinfo/3", "MALFORMED GARBAGE THAT CRASHES PARSER");

        var gpu = new ProcessGpu(fake.Root);
        var processes = new[] { new ProcessIdentity(740, 1000) };

        // Should not throw, and returns nulls
        IReadOnlyDictionary<int, GpuUsage> usage = gpu.Read(stamp: 0, processes);

        Assert.IsTrue(usage.ContainsKey(740));
        Assert.IsNull(usage[740].Percent);
        Assert.IsNull(usage[740].MemoryBytes);
    }

    [TestMethod]
    public void ProcessGpu_NullStartTime_ProducesNoGpuValues()
    {
        using var fake = new FakeRoot();
        fake.CreateDirectory("proc/750/fd");
        fake.CreateDirectory("proc/750/fdinfo");
        fake.CreateSymlink("proc/750/fd/3", "/dev/dri/renderD128");

        fake.WriteFile("proc/750/fdinfo/3",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t42\n" +
            "drm-engine-render:\t1000000000 ns\n" +
            "drm-memory-vram:\t1024 KiB\n");

        var gpu = new ProcessGpu(fake.Root);
        // Process with null start time has no identity (Amendment 2 item 2)
        var processes = new[] { new ProcessIdentity(750, null) };

        IReadOnlyDictionary<int, GpuUsage> first = gpu.Read(stamp: 0, processes);
        Assert.IsFalse(first.ContainsKey(750), "Process with null start time must produce no GPU entry");

        IReadOnlyDictionary<int, GpuUsage> second = gpu.Read(stamp: 5000, processes);
        Assert.IsFalse(second.ContainsKey(750), "Process with null start time must never be cached or projected");
    }

    [TestMethod]
    public void ProcessStats_ProcessWithNullStartTime_LeavesGpuValuesNull()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/755/status", MakeStatus("null-start"));
        // No stat file -> StartTime is null
        fake.CreateDirectory("proc/755/fd");
        fake.CreateDirectory("proc/755/fdinfo");
        fake.CreateSymlink("proc/755/fd/3", "/dev/dri/renderD128");
        fake.WriteFile("proc/755/fdinfo/3",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t42\n" +
            "drm-engine-render:\t1000000000 ns\n" +
            "drm-memory-vram:\t1024 KiB\n");

        var stats = new ProcessStats(fake.Root, logicalCpus: 1, clockTicks: 100);
        IReadOnlyList<ProcessRow> rows = stats.Read(stampMs: 1000);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(755, rows[0].Pid);
        Assert.IsNull(rows[0].StartTime);
        Assert.IsNull(rows[0].GpuPercent);
        Assert.IsNull(rows[0].GpuMemoryBytes);
    }

    [TestMethod]
    public void ProcessGpu_EmptyEngineClient_GpuPercentStaysNullWhileMemoryIsReported()
    {
        using var fake = new FakeRoot();
        fake.CreateDirectory("proc/760/fd");
        fake.CreateDirectory("proc/760/fdinfo");
        fake.CreateSymlink("proc/760/fd/3", "/dev/dri/renderD128");

        // Client has memory lines but NO engine counters (Amendment 2 item 3)
        fake.WriteFile("proc/760/fdinfo/3",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t42\n" +
            "drm-memory-vram:\t2048 KiB\n");

        var gpu = new ProcessGpu(fake.Root);
        var processes = new[] { new ProcessIdentity(760, 1000) };

        IReadOnlyDictionary<int, GpuUsage> first = gpu.Read(stamp: 0, processes);
        Assert.IsTrue(first.ContainsKey(760));
        Assert.IsNull(first[760].Percent);
        Assert.AreEqual(2048L * 1024, first[760].MemoryBytes);

        // Second sample at 5000 ms (>= 4000 ms IntervalMs)
        IReadOnlyDictionary<int, GpuUsage> second = gpu.Read(stamp: 5000, processes);
        Assert.IsTrue(second.ContainsKey(760));
        Assert.IsNull(second[760].Percent, "Client with empty engines must report null GPU percent, not 0%");
        Assert.AreEqual(2048L * 1024, second[760].MemoryBytes);
    }
}
