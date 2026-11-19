using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class ProcStatTests
{
    private const string SampleStat1 =
        "cpu  1000 200 300 4000 500 60 70 80 0 0\n" +
        "cpu0 500 100 150 2000 250 30 35 40 0 0\n" +
        "cpu1 500 100 150 2000 250 30 35 40 0 0\n" +
        "intr 123456\n" +
        "ctxt 789012\n";

    private const string SampleStat2 =
        "cpu  1200 220 330 4400 520 70 80 90 0 0\n" +
        "cpu0 620 110 165 2200 260 35 40 45 0 0\n" +
        "cpu1 580 110 165 2200 260 35 40 45 0 0\n";

    [TestMethod]
    public void Parse_ReadsTotalAndCoresCorrectly()
    {
        ProcStatReading reading = ProcStat.Parse(SampleStat1);

        // busy = 1000 + 200 + 300 + 60 + 70 + 80 = 1710
        // total = 1710 + 4000 (idle) + 500 (iowait) = 6210
        Assert.AreEqual(1710, reading.Total.Busy);
        Assert.AreEqual(6210, reading.Total.Total);

        Assert.AreEqual(2, reading.Cores.Count);
        // core busy = 500 + 100 + 150 + 30 + 35 + 40 = 855
        // core total = 855 + 2000 + 250 = 3105
        Assert.AreEqual(855, reading.Cores[0].Busy);
        Assert.AreEqual(3105, reading.Cores[0].Total);
        Assert.AreEqual(855, reading.Cores[1].Busy);
        Assert.AreEqual(3105, reading.Cores[1].Total);
    }

    [TestMethod]
    public void Parse_SortsCoresByIndexEvenIfUnordered()
    {
        string disordered =
            "cpu  100 20 30 400 50 6 7 8 0 0\n" +
            "cpu2 10 2 3 40 5 1 1 1 0 0\n" +
            "cpu0 30 2 3 40 5 1 1 1 0 0\n" +
            "cpu1 20 2 3 40 5 1 1 1 0 0\n";

        ProcStatReading reading = ProcStat.Parse(disordered);

        Assert.AreEqual(3, reading.Cores.Count);
        // cpu0 busy was 30+2+3+1+1+1 = 38
        Assert.AreEqual(38, reading.Cores[0].Busy);
        // cpu1 busy was 20+2+3+1+1+1 = 28
        Assert.AreEqual(28, reading.Cores[1].Busy);
        // cpu2 busy was 10+2+3+1+1+1 = 18
        Assert.AreEqual(18, reading.Cores[2].Busy);
    }

    [TestMethod]
    public void Usage_CalculatesPercentageDeltaCorrectly()
    {
        ProcStatReading reading1 = ProcStat.Parse(SampleStat1);
        ProcStatReading reading2 = ProcStat.Parse(SampleStat2);

        // Delta for total:
        // reading2 busy: 1200+220+330+70+80+90 = 1990
        // reading2 total: 1990 + 4400 + 520 = 6910
        // delta busy = 1990 - 1710 = 280
        // delta total = 6910 - 6210 = 700
        // usage = 280 / 700 = 40%
        int totalUsage = ProcStat.Usage(reading1.Total, reading2.Total);
        Assert.AreEqual(40, totalUsage);

        int core0Usage = ProcStat.Usage(reading1.Cores[0], reading2.Cores[0]);
        // reading2 core0 busy: 620+110+165+35+40+45 = 1015
        // reading2 core0 total: 1015 + 2200 + 260 = 3475
        // delta busy = 1015 - 855 = 160
        // delta total = 3475 - 3105 = 370
        // usage = 160 / 370 = 43.24% -> 43%
        Assert.AreEqual(43, core0Usage);
    }

    [TestMethod]
    public void Usage_WhenTotalDoesNotAdvanceOrGoesBackwards_ReturnsZero()
    {
        var prev = new CpuTimes(Busy: 1000, Total: 5000);
        var currentSame = new CpuTimes(Busy: 1000, Total: 5000);
        var currentBackwards = new CpuTimes(Busy: 200, Total: 1000);

        Assert.AreEqual(0, ProcStat.Usage(prev, currentSame));
        Assert.AreEqual(0, ProcStat.Usage(prev, currentBackwards));
    }
}
