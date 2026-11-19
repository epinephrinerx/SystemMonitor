using Microsoft.VisualStudio.TestTools.UnitTesting;
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
}
