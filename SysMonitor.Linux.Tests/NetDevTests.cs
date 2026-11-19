using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class NetDevTests
{
    private const string SampleNetDev =
        "Inter-|   Receive                                                |  Transmit\n" +
        " face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed\n" +
        "    lo: 1234567       0    0    0    0     0          0         0  1234567       0    0    0    0     0       0          0\n" +
        "wlp0s20f3: 50000000    1000    0    0    0     0          0         0 10000000     500    0    0    0     0       0          0\n" +
        "  eth0: 20000000     400    0    0    0     0          0         0  5000000     200    0    0    0     0       0          0\n";

    [TestMethod]
    public void Parse_ReadsInterfacesAndSkipsLoopback()
    {
        IReadOnlyList<NetCounters> counters = NetDev.Parse(SampleNetDev);

        Assert.AreEqual(2, counters.Count);
        Assert.IsFalse(counters.Any(c => c.Name == "lo"));

        NetCounters wifi = counters.First(c => c.Name == "wlp0s20f3");
        Assert.AreEqual(50000000L, wifi.Received);
        Assert.AreEqual(10000000L, wifi.Sent);

        NetCounters eth = counters.First(c => c.Name == "eth0");
        Assert.AreEqual(20000000L, eth.Received);
        Assert.AreEqual(5000000L, eth.Sent);
    }

    [TestMethod]
    public void Rate_CalculatesMegabytesPerSecondCorrectly()
    {
        var prev = new NetCounters("eth0", Received: 10 * 1024 * 1024, Sent: 5 * 1024 * 1024);
        var curr = new NetCounters("eth0", Received: 20 * 1024 * 1024, Sent: 7 * 1024 * 1024);

        // 10 MB down, 2 MB up over 2 seconds (2000 ms)
        (double downMb, double upMb) = NetDev.Rate(prev, curr, previousStamp: 1000, stamp: 3000);

        Assert.AreEqual(5.0, downMb, 0.001);
        Assert.AreEqual(1.0, upMb, 0.001);
    }
}
