using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class DiskStatsTests
{
    private const string SampleDiskstats =
        " 259       0 nvme0n1 1000 50 16000 200 2000 100 32000 400 0 500 600 0 0 0 0 0 0\n" +
        " 259       1 nvme0n1p1 100 5 1600 20 200 10 3200 40 0 50 60 0 0 0 0 0 0\n" +
        " 259       2 nvme0n1p2 900 45 14400 180 1800 90 28800 360 0 450 540 0 0 0 0 0 0\n";

    [TestMethod]
    public void Parse_ReadsBothDisksAndPartitionsSeparatelyWithSectorToByteConversion()
    {
        IReadOnlyList<DiskCounters> counters = DiskStats.Parse(SampleDiskstats);

        Assert.AreEqual(3, counters.Count);

        // nvme0n1: 16000 sectors read * 512 = 8,192,000 bytes; 32000 sectors written * 512 = 16,384,000 bytes
        DiskCounters wholeDisk = counters[0];
        Assert.AreEqual("nvme0n1", wholeDisk.Name);
        Assert.AreEqual(16000L * 512, wholeDisk.BytesRead);
        Assert.AreEqual(32000L * 512, wholeDisk.BytesWritten);

        // nvme0n1p1: 1600 sectors read * 512 = 819,200 bytes; 3200 * 512 = 1,638,400 bytes
        DiskCounters p1 = counters[1];
        Assert.AreEqual("nvme0n1p1", p1.Name);
        Assert.AreEqual(1600L * 512, p1.BytesRead);
        Assert.AreEqual(3200L * 512, p1.BytesWritten);

        // nvme0n1p2: 14400 sectors read * 512 = 7,372,800 bytes; 28800 * 512 = 14,745,600 bytes
        DiskCounters p2 = counters[2];
        Assert.AreEqual("nvme0n1p2", p2.Name);
        Assert.AreEqual(14400L * 512, p2.BytesRead);
        Assert.AreEqual(28800L * 512, p2.BytesWritten);
    }

    [TestMethod]
    public void Rate_CalculatesMegabytesPerSecondCorrectly()
    {
        var prev = new DiskCounters("nvme0n1p2", BytesRead: 10 * 1024 * 1024, BytesWritten: 20 * 1024 * 1024);
        var curr = new DiskCounters("nvme0n1p2", BytesRead: 20 * 1024 * 1024, BytesWritten: 40 * 1024 * 1024);

        // 10 MB read and 20 MB written over 2 seconds (2000 ms)
        // Rate = 5 MB/s read, 10 MB/s write
        (double readMb, double writeMb) = DiskStats.Rate(prev, curr, previousStamp: 1000, stamp: 3000);

        Assert.AreEqual(5.0, readMb, 0.001);
        Assert.AreEqual(10.0, writeMb, 0.001);
    }
}
