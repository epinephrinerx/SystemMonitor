using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class ProcessReaderTests
{
    [TestMethod]
    public void StatusName_WhenExactly15CharsAndExeStartsWithIt_RecoversFullName()
    {
        // 15 characters truncated name from /proc/<pid>/status
        string truncated15 = "gnome-system-mo"; // exactly 15 chars
        string exeName = "gnome-system-monitor"; // full name

        string recovered = ProcessReader.StatusName(truncated15, exeName);
        Assert.AreEqual("gnome-system-monitor", recovered);
    }

    [TestMethod]
    public void StatusName_WhenUnder15Chars_KeepsStatusNameEvenIfExeDiffers()
    {
        // Short name (e.g. process changed its comm or python script)
        string shortName = "myscript";
        string exeName = "python3.11";

        string name = ProcessReader.StatusName(shortName, exeName);
        Assert.AreEqual("myscript", name);
    }

    [TestMethod]
    public void StatusName_When15CharsButExeDoesNotMatch_KeepsStatusName()
    {
        string status15 = "123456789012345";
        string exeName = "different-program";

        string name = ProcessReader.StatusName(status15, exeName);
        Assert.AreEqual("123456789012345", name);
    }

    [TestMethod]
    public void Read_ReadsMemoryInBytesAndHandlesUnreadableProcesses()
    {
        using var fake = new FakeRoot();

        // PID 1234: readable process
        fake.WriteFile("proc/1234/status",
            "Name:\tsystemd\n" +
            "VmRSS:\t   16384 kB\n" + // 16 MB = 16,777,216 bytes
            "RssAnon:\t    8192 kB\n"); // 8 MB = 8,388,608 bytes

        // PID 5678: unreadable process (e.g. another user's daemon, no status file)
        fake.CreateDirectory("proc/5678");

        IReadOnlyList<ProcessEntry> processes = ProcessReader.Read(fake.Root);

        Assert.AreEqual(2, processes.Count);

        ProcessEntry p1 = processes[0];
        Assert.AreEqual(1234, p1.Pid);
        Assert.AreEqual("systemd", p1.Name);
        Assert.AreEqual(16384L * 1024, p1.WorkingSet);
        Assert.AreEqual(8192L * 1024, p1.PrivateBytes);
        Assert.IsTrue(p1.Readable);

        ProcessEntry p2 = processes[1];
        Assert.AreEqual(5678, p2.Pid);
        Assert.IsFalse(p2.Readable);
        Assert.AreEqual(0L, p2.WorkingSet);
        Assert.AreEqual(0L, p2.PrivateBytes);
    }

    [TestMethod]
    public void Read_ParsesUidAndParentPid_FromFakeProc()
    {
        using var fake = new FakeRoot();

        // Process 100: Uid 1000 1000 1000 1000, PPid 1
        fake.WriteFile("proc/100/status",
            "Name:\tmy-app\n" +
            "PPid:\t1\n" +
            "Uid:\t1000\t1000\t1000\t1000\n" +
            "VmRSS:\t4096 kB\n");
        fake.WriteFile("proc/100/cmdline", "/usr/bin/my-app\0--arg\0");

        // Process 101: Root process, Uid 0, PPid 1
        fake.WriteFile("proc/101/status",
            "Name:\troot-daemon\n" +
            "PPid:\t1\n" +
            "Uid:\t0\t0\t0\t0\n" +
            "VmRSS:\t2048 kB\n");
        fake.WriteFile("proc/101/cmdline", "/usr/sbin/root-daemon\0");

        IReadOnlyList<ProcessEntry> processes = ProcessReader.Read(fake.Root);

        Assert.AreEqual(2, processes.Count);

        ProcessEntry p100 = processes.First(p => p.Pid == 100);
        Assert.AreEqual("my-app", p100.Name);
        Assert.AreEqual(1000, p100.Uid);
        Assert.AreEqual(1, p100.ParentPid);
        Assert.IsFalse(p100.KernelThread);

        ProcessEntry p101 = processes.First(p => p.Pid == 101);
        Assert.AreEqual("root-daemon", p101.Name);
        Assert.AreEqual(0, p101.Uid);
        Assert.AreEqual(1, p101.ParentPid);
        Assert.IsFalse(p101.KernelThread);
    }

    [TestMethod]
    public void Read_KernelThreadDetection_DistinguishesKernelThreadsFromUserProcesses()
    {
        using var fake = new FakeRoot();

        // PID 2: kthreadd itself, no cmdline (empty)
        fake.WriteFile("proc/2/status",
            "Name:\tkthreadd\n" +
            "PPid:\t0\n" +
            "Uid:\t0\t0\t0\t0\n");
        fake.WriteFile("proc/2/cmdline", "");

        // PID 15: kworker, child of PPid 2, empty cmdline -> KernelThread true
        fake.WriteFile("proc/15/status",
            "Name:\tkworker/0:1\n" +
            "PPid:\t2\n" +
            "Uid:\t0\t0\t0\t0\n");
        fake.WriteFile("proc/15/cmdline", "");

        // PID 50: child of PPid 2, but has non-empty cmdline (rare/hypothetical) -> NOT kernel thread
        fake.WriteFile("proc/50/status",
            "Name:\tcustom-proc\n" +
            "PPid:\t2\n" +
            "Uid:\t1000\t1000\t1000\t1000\n");
        fake.WriteFile("proc/50/cmdline", "custom-proc\0");

        // PID 100: regular user process with PPid != 2
        fake.WriteFile("proc/100/status",
            "Name:\tfirefox\n" +
            "PPid:\t1200\n" +
            "Uid:\t1000\t1000\t1000\t1000\n");
        fake.WriteFile("proc/100/cmdline", "firefox\0");

        IReadOnlyList<ProcessEntry> processes = ProcessReader.Read(fake.Root);

        ProcessEntry kthreadd = processes.First(p => p.Pid == 2);
        Assert.IsTrue(kthreadd.KernelThread, "PID 2 with empty cmdline must be flagged as KernelThread");

        ProcessEntry kworker = processes.First(p => p.Pid == 15);
        Assert.IsTrue(kworker.KernelThread, "Child of PID 2 with empty cmdline must be flagged as KernelThread");

        ProcessEntry custom = processes.First(p => p.Pid == 50);
        Assert.IsFalse(custom.KernelThread, "Process with non-empty cmdline is not a kernel thread");

        ProcessEntry firefox = processes.First(p => p.Pid == 100);
        Assert.IsFalse(firefox.KernelThread, "Regular user process is not a kernel thread");
    }

    [TestMethod]
    public void IsKernelThread_StaticMethod_FollowsSpecification()
    {
        // PID 2 with no cmdline -> true
        Assert.IsTrue(ProcessReader.IsKernelThread(pid: 2, parentPid: 0, hasCommandLine: false));

        // Child of PID 2 with no cmdline -> true
        Assert.IsTrue(ProcessReader.IsKernelThread(pid: 99, parentPid: 2, hasCommandLine: false));

        // Has command line -> false (even if PID 2 or parent 2)
        Assert.IsFalse(ProcessReader.IsKernelThread(pid: 2, parentPid: 0, hasCommandLine: true));
        Assert.IsFalse(ProcessReader.IsKernelThread(pid: 99, parentPid: 2, hasCommandLine: true));

        // Neither PID 2 nor parent 2 -> false
        Assert.IsFalse(ProcessReader.IsKernelThread(pid: 100, parentPid: 1, hasCommandLine: false));
        Assert.IsFalse(ProcessReader.IsKernelThread(pid: 100, parentPid: 1, hasCommandLine: true));
    }

    [TestMethod]
    public void UidField_ParsesRealUid_AndHandlesMissingOrEmpty()
    {
        // Standard multi-field Uid line (real, effective, saved, fs)
        string status = "Name:\ttest\nUid:\t1001\t1001\t1001\t1001\n";
        Assert.AreEqual(1001, ProcessReader.UidField(status));

        // Single field
        string statusSingle = "Name:\ttest\nUid:\t500\n";
        Assert.AreEqual(500, ProcessReader.UidField(statusSingle));

        // Missing Uid
        string statusNoUid = "Name:\ttest\nPPid:\t1\n";
        Assert.IsNull(ProcessReader.UidField(statusNoUid));

        // Malformed Uid
        string statusMalformed = "Name:\ttest\nUid:\tabc\n";
        Assert.IsNull(ProcessReader.UidField(statusMalformed));
    }

    [TestMethod]
    public void NameOf_ReturnsCurrentProcessNameOrNull()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("proc/500/status", "Name:\tbash\n");
        Assert.AreEqual("bash", ProcessReader.NameOf(fake.Root, 500));

        // Non-existent PID returns null
        Assert.IsNull(ProcessReader.NameOf(fake.Root, 9999));
    }

    [TestMethod]
    public void ParseStartTime_WithCommHoldingSpacesAndParentheses_ParsesStartTimeCorrectly()
    {
        // /proc/<pid>/stat format:
        // pid (comm) state ppid pgrp session tty_nr tpgid flags minflt cminflt majflt cmajflt utime stime cutime cstime priority nice num_threads itrealvalue starttime ...
        // fields after the last ')' start with field 3 (state), so field 22 (starttime) is index 19 (0-based) after ')'
        // Here comm contains spaces, nested parentheses and trailing parentheses: (web browser (worker))
        string stat = "1234 (web browser (worker)) S 1 1234 1234 0 -1 4194304 100 0 0 0 10 20 0 0 20 0 1 0 789123 1234567 100";

        ulong? startTime = ProcessReader.ParseStartTime(stat);
        Assert.AreEqual(789123UL, startTime);

        // Simple comm without extra parentheses
        string simpleStat = "500 (bash) R 1 500 500 0 -1 4194304 10 0 0 0 5 10 0 0 20 0 1 0 45678 999999 50";
        Assert.AreEqual(45678UL, ProcessReader.ParseStartTime(simpleStat));

        // Malformed or missing stat lines have no start time at all, so an
        // unknown value can never match a real one (Amendment 3).
        Assert.IsNull(ProcessReader.ParseStartTime(""));
        Assert.IsNull(ProcessReader.ParseStartTime("not a stat line"));
        Assert.IsNull(ProcessReader.ParseStartTime("1234 missing_closing_paren"));
        Assert.IsNull(ProcessReader.ParseStartTime("1234 (comm) S 1 2 3")); // fewer than 22 fields

        // Zero or negative ticks are rejected (Amendment 3: StartTime 0 is not an identity)
        string statZero = "500 (bash) R 1 500 500 0 -1 4194304 10 0 0 0 5 10 0 0 20 0 1 0 0 999999 50";
        Assert.IsNull(ProcessReader.ParseStartTime(statZero));
    }

    [TestMethod]
    public void Read_WithStatFile_PopulatesStartTime()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("proc/400/status", "Name:\tmy (app)\nPPid:\t1\nUid:\t1000\t1000\t1000\t1000\nVmRSS:\t1024 kB\n");
        // comm has spaces and parentheses; field 22 (starttime) = 98765
        fake.WriteFile("proc/400/stat", "400 (my (app)) S 1 400 400 0 -1 4194304 0 0 0 0 0 0 0 0 20 0 1 0 98765 0 0\n");
        fake.WriteFile("proc/400/cmdline", "my-app\0");

        IReadOnlyList<ProcessEntry> processes = ProcessReader.Read(fake.Root);
        Assert.AreEqual(1, processes.Count);
        Assert.AreEqual(400, processes[0].Pid);
        Assert.AreEqual(98765UL, processes[0].StartTime);
    }

    [TestMethod]
    public void FactsOf_PopulatesStartTimeAndAllActionTimeFacts()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("proc/450/status", "Name:\tworker\nPPid:\t1\nUid:\t1000\t1000\t1000\t1000\n");
        fake.WriteFile("proc/450/stat", "450 (worker) S 1 450 450 0 -1 4194304 0 0 0 0 0 0 0 0 20 0 1 0 54321 0 0\n");
        fake.WriteFile("proc/450/cmdline", "worker\0");

        ProcessFacts? facts = ProcessReader.FactsOf(fake.Root, 450);
        Assert.IsNotNull(facts);
        Assert.AreEqual(450, facts.Pid);
        Assert.AreEqual("worker", facts.Name);
        Assert.AreEqual(54321UL, facts.StartTime);
        Assert.AreEqual(1000, facts.Uid);
        Assert.AreEqual(1, facts.ParentPid);
        Assert.IsFalse(facts.KernelThread);
        Assert.IsTrue(facts.Readable);

        // Missing process returns null
        Assert.IsNull(ProcessReader.FactsOf(fake.Root, 9999));
    }
}
