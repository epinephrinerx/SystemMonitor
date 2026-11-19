using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class MemInfoTests
{
    private const string SampleMemInfoWithAvailable =
        "MemTotal:        8000000 kB\n" +
        "MemFree:         2000000 kB\n" +
        "MemAvailable:    6000000 kB\n" +
        "Buffers:          200000 kB\n" +
        "Cached:          1800000 kB\n";

    private const string SampleMemInfoLegacyWithoutAvailable =
        "MemTotal:        8000000 kB\n" +
        "MemFree:         2000000 kB\n" +
        "Buffers:          500000 kB\n" +
        "Cached:          1500000 kB\n";

    [TestMethod]
    public void Parse_WithMemAvailable_UsesTotalMinusAvailable()
    {
        Ram ram = MemInfo.Parse(SampleMemInfoWithAvailable);

        // total = 8000000 kB = 8000000 / 1024 / 1024 GB = 7.62939...
        // used = 8000000 - 6000000 = 2000000 kB
        // usage = 2000000 / 8000000 * 100 = 25%
        Assert.AreEqual(25, ram.Usage);
        Assert.AreEqual(8000000.0 / 1024.0 / 1024.0, ram.TotalGb, 0.001);
        Assert.AreEqual(2000000.0 / 1024.0 / 1024.0, ram.UsedGb, 0.001);
    }

    [TestMethod]
    public void Parse_WithoutMemAvailable_FallsBackToFreeBuffersCached()
    {
        Ram ram = MemInfo.Parse(SampleMemInfoLegacyWithoutAvailable);

        // available = free(2000000) + buffers(500000) + cached(1500000) = 4000000 kB
        // used = 8000000 - 4000000 = 4000000 kB
        // usage = 50%
        Assert.AreEqual(50, ram.Usage);
        Assert.AreEqual(4000000.0 / 1024.0 / 1024.0, ram.UsedGb, 0.001);
    }

    [TestMethod]
    public void Parse_EmptyOrInvalid_ReturnsEmptyRam()
    {
        Ram empty = MemInfo.Parse("");
        Assert.AreEqual(0, empty.Usage);
        Assert.AreEqual(0.0, empty.TotalGb);
        Assert.AreEqual(0.0, empty.UsedGb);

        Ram invalid = MemInfo.Parse("Foo: Bar kB\nBaz: 123");
        Assert.AreEqual(0, invalid.Usage);
        Assert.AreEqual(0.0, invalid.TotalGb);
    }
}
