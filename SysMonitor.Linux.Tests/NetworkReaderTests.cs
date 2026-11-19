using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class NetworkReaderTests
{
    private const string SampleNetDev =
        "Inter-|   Receive                                                |  Transmit\n" +
        " face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed\n" +
        "wlp0s20f3: 1000000 0 0 0 0 0 0 0 1000000 0 0 0 0 0 0 0\n" +
        "  eth0: 1000000 0 0 0 0 0 0 0 1000000 0 0 0 0 0 0 0\n" +
        "docker0: 1000000 0 0 0 0 0 0 0 1000000 0 0 0 0 0 0 0\n";

    [TestMethod]
    public void ParseIwSpeed_ParsesTxBitrateCorrectly()
    {
        string iwOutput =
            "Connected to 00:11:22:33:44:55 (on wlp0s20f3)\n" +
            "\tSSID: MyWifiNetwork\n" +
            "\tfreq: 5180\n" +
            "\trx bitrate: 780.0 MBit/s VHT-MCS 8 80MHz short GI VHT-NSS 2\n" +
            "\ttx bitrate: 866.7 MBit/s VHT-MCS 9 80MHz short GI VHT-NSS 2\n" +
            "\tsignal: -52 dBm\n";

        double speed = NetworkReader.ParseIwSpeed(iwOutput);
        Assert.AreEqual(866.7, speed, 0.01);

        Assert.AreEqual(0.0, NetworkReader.ParseIwSpeed(null));
        Assert.AreEqual(0.0, NetworkReader.ParseIwSpeed("Not connected."));
    }

    [TestMethod]
    public void WiredSpeed_WhenNegativeOneOrMissing_ReturnsZero()
    {
        using var fake = new FakeRoot();
        var runner = new FakeCommandRunner();
        var reader = new NetworkReader(fake.Root, runner);

        // speed = -1 (disconnected link or unsupported)
        fake.WriteFile("sys/class/net/eth0/speed", "-1\n");
        Assert.AreEqual(0.0, reader.WiredSpeed("eth0"));

        // valid speed = 1000 Mbps
        fake.WriteFile("sys/class/net/eth1/speed", "1000\n");
        Assert.AreEqual(1000.0, reader.WiredSpeed("eth1"));

        // missing speed file
        Assert.AreEqual(0.0, reader.WiredSpeed("eth2"));
    }

    [TestMethod]
    public void WifiSpeed_CachesFor5SecondsBeforeCallingIwAgain()
    {
        using var fake = new FakeRoot();
        var runner = new FakeCommandRunner();
        runner.CommandOutputs["iw dev wlp0s20f3 link"] = "tx bitrate: 866.7 MBit/s\n";

        var reader = new NetworkReader(fake.Root, runner);

        // First call at t = 1000 ms
        double speed1 = reader.WifiSpeed("wlp0s20f3", stamp: 1000);
        Assert.AreEqual(866.7, speed1, 0.01);
        Assert.AreEqual(1, runner.Executions.Count);

        // Call at t = 4000 ms (< 5000 ms elapsed): should return cached value and NOT run command
        double speed2 = reader.WifiSpeed("wlp0s20f3", stamp: 4000);
        Assert.AreEqual(866.7, speed2, 0.01);
        Assert.AreEqual(1, runner.Executions.Count);

        // Call at t = 6500 ms (5500 ms elapsed >= 5000 ms): should run command again
        runner.CommandOutputs["iw dev wlp0s20f3 link"] = "tx bitrate: 433.3 MBit/s\n";
        double speed3 = reader.WifiSpeed("wlp0s20f3", stamp: 6500);
        Assert.AreEqual(433.3, speed3, 0.01);
        Assert.AreEqual(2, runner.Executions.Count);
    }

    [TestMethod]
    public void Read_SkipsVirtualInterfacesAndPopulatesPhysical()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/net/dev", SampleNetDev);

        // wlp0s20f3 is wireless
        fake.CreateDirectory("sys/class/net/wlp0s20f3/wireless");
        fake.WriteFile("sys/class/net/wlp0s20f3/device/uevent", "DRIVER=iwlwifi\nPCI_ID=8086:02f0\n");

        // eth0 is physical wired
        fake.WriteFile("sys/class/net/eth0/speed", "1000\n");
        fake.WriteFile("sys/class/net/eth0/device/uevent", "DRIVER=r8169\n");

        // docker0 is virtual: symlink to /sys/devices/virtual/net/docker0
        string virtualTarget = fake.CreateDirectory("sys/devices/virtual/net/docker0");
        fake.CreateDirectorySymlink("sys/class/net/docker0", virtualTarget);

        var runner = new FakeCommandRunner();
        runner.CommandOutputs["iw dev wlp0s20f3 link"] = "tx bitrate: 866.7 MBit/s\n";

        var reader = new NetworkReader(fake.Root, runner);
        IReadOnlyList<Adapter> adapters = reader.Read(stamp: 1000);

        Assert.AreEqual(2, adapters.Count);
        Assert.IsFalse(adapters.Any(a => a.Name == "docker0")); // docker0 was filtered out

        Adapter wifi = adapters.First(a => a.Name == "wlp0s20f3");
        Assert.IsTrue(wifi.Wireless);
        Assert.AreEqual(866.7, wifi.SpeedMbps, 0.01);

        Adapter eth = adapters.First(a => a.Name == "eth0");
        Assert.IsFalse(eth.Wireless);
        Assert.AreEqual(1000.0, eth.SpeedMbps, 0.01);
    }
}
