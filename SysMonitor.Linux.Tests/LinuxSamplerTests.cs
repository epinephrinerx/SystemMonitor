using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sampling;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class LinuxSamplerTests
{
    private const string SampleStat1 =
        "cpu  1000 200 300 4000 500 60 70 80 0 0\n" +
        "cpu0 1000 200 300 4000 500 60 70 80 0 0\n";

    private const string SampleStat2 =
        "cpu  1200 220 330 4400 520 70 80 90 0 0\n" +
        "cpu0 1200 220 330 4400 520 70 80 90 0 0\n";

    private const string SampleMeminfo =
        "MemTotal:        8000000 kB\n" +
        "MemAvailable:    6000000 kB\n";

    [TestMethod]
    public void Sample_FirstCallReportsReadyFalse_SecondReportsReadyTrue()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/stat", SampleStat1);
        fake.WriteFile("proc/meminfo", SampleMeminfo);

        var runner = new FakeCommandRunner();
        var clock = new FakeClock();
        var sampler = new LinuxSampler(fake.Root, runner, clock.Now);

        // First sample: rates need a baseline, so Ready is false
        Snapshot first = sampler.Sample(includeProcesses: false);
        Assert.IsFalse(first.Ready);
        Assert.AreEqual(0, first.CpuTotal);
        Assert.AreEqual(25, first.Ram.Usage);

        // Advance clock and update stat for delta
        clock.Advance(1000);
        fake.WriteFile("proc/stat", SampleStat2);

        // Second sample: Ready is true, CpuTotal rate is calculated
        Snapshot second = sampler.Sample(includeProcesses: false);
        Assert.IsTrue(second.Ready);
        Assert.AreEqual(40, second.CpuTotal);
    }

    [TestMethod]
    public void Sample_WhenOneSourceIsBroken_OtherSectionsStillPopulateWithoutThrowing()
    {
        using var fake = new FakeRoot();

        // /proc/stat is MISSING entirely!
        // But /proc/meminfo is present:
        fake.WriteFile("proc/meminfo", SampleMeminfo);

        var runner = new FakeCommandRunner();
        var clock = new FakeClock();
        var sampler = new LinuxSampler(fake.Root, runner, clock.Now);

        // Sampling must NOT throw
        Snapshot snapshot = sampler.Sample(includeProcesses: false);

        // CPU is empty/zero because /proc/stat was missing
        Assert.AreEqual(0, snapshot.CpuTotal);
        Assert.AreEqual(0, snapshot.Cores.Count);

        // RAM is still populated from /proc/meminfo!
        Assert.AreEqual(25, snapshot.Ram.Usage);
        Assert.AreEqual(8000000.0 / 1024.0 / 1024.0, snapshot.Ram.TotalGb, 0.001);
    }

    [TestMethod]
    public void Sample_ProcessInclusionFlagControlsProcessEnumeration()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/stat", SampleStat1);
        fake.WriteFile("proc/123/status", "Name:\tinit\nVmRSS:\t1024 kB\n");

        var runner = new FakeCommandRunner();
        var clock = new FakeClock();
        var sampler = new LinuxSampler(fake.Root, runner, clock.Now);

        Snapshot withoutProcs = sampler.Sample(includeProcesses: false);
        Assert.AreEqual(0, withoutProcs.Processes.Count);

        clock.Advance(1000);
        Snapshot withProcs = sampler.Sample(includeProcesses: true);
        Assert.AreEqual(1, withProcs.Processes.Count);
        Assert.AreEqual("init", withProcs.Processes[0].Name);
    }
}
