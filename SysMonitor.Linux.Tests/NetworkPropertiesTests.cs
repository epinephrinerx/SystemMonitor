using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sampling;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class NetworkPropertiesTests
{
    private const string SampleConnectedIwLink =
        "Connected to 00:11:22:33:44:55 (on wlp0s20f3)\n" +
        "\tSSID: TestNetwork5G\n" +
        "\tfreq: 5220\n" +
        "\tRX: 12345 bytes (100 packets)\n" +
        "\tTX: 67890 bytes (200 packets)\n" +
        "\tsignal: -65 dBm\n" +
        "\trx bitrate: 780.0 MBit/s VHT-MCS 8 80MHz\n" +
        "\ttx bitrate: 576.4 MBit/s VHT-MCS 7 80MHz\n";

    [TestMethod]
    public void ParseIwLink_ParsesSsidFreqSignalAndTxBitrate()
    {
        WifiLink link = NetworkReader.ParseIwLink(SampleConnectedIwLink);

        Assert.AreEqual("TestNetwork5G", link.Ssid);
        Assert.AreEqual(5220, link.FrequencyMhz);
        Assert.AreEqual(-65, link.SignalDbm);
        Assert.IsNotNull(link.TxBitrateMbps);
        Assert.AreEqual(576.4, link.TxBitrateMbps.Value, 0.01);
    }

    [TestMethod]
    public void ParseIwLink_WhenNotConnectedOrEmptyOrNull_ReturnsNullsAndEmptySsid()
    {
        WifiLink disconnected = NetworkReader.ParseIwLink("Not connected.\n");
        Assert.AreEqual(string.Empty, disconnected.Ssid);
        Assert.IsNull(disconnected.FrequencyMhz);
        Assert.IsNull(disconnected.SignalDbm);
        Assert.IsNull(disconnected.TxBitrateMbps);

        WifiLink empty = NetworkReader.ParseIwLink(string.Empty);
        Assert.AreEqual(string.Empty, empty.Ssid);
        Assert.IsNull(empty.FrequencyMhz);
        Assert.IsNull(empty.SignalDbm);
        Assert.IsNull(empty.TxBitrateMbps);

        WifiLink nullOutput = NetworkReader.ParseIwLink(null);
        Assert.AreEqual(string.Empty, nullOutput.Ssid);
        Assert.IsNull(nullOutput.FrequencyMhz);
        Assert.IsNull(nullOutput.SignalDbm);
        Assert.IsNull(nullOutput.TxBitrateMbps);
    }

    [TestMethod]
    public void Wifi_CachesFor10SecondsBeforeCallingIwAgain()
    {
        using var fake = new FakeRoot();
        var runner = new FakeCommandRunner();
        runner.CommandOutputs["iw dev wlp0s20f3 link"] = SampleConnectedIwLink;

        var reader = new NetworkReader(fake.Root, runner);

        // Call 1 at stamp = 1000 ms: runs iw
        WifiLink link1 = reader.Wifi("wlp0s20f3", stamp: 1000);
        Assert.AreEqual("TestNetwork5G", link1.Ssid);
        Assert.AreEqual(1, runner.Executions.Count);

        // Call 2 at stamp = 7000 ms (6s elapsed < 10s): uses cache
        WifiLink link2 = reader.Wifi("wlp0s20f3", stamp: 7000);
        Assert.AreEqual("TestNetwork5G", link2.Ssid);
        Assert.AreEqual(1, runner.Executions.Count);

        // Call 3 at stamp = 11001 ms (10001 ms elapsed >= 10s): runs iw again
        runner.CommandOutputs["iw dev wlp0s20f3 link"] =
            "Connected to 00:11:22:33:44:55 (on wlp0s20f3)\n" +
            "\tSSID: OtherNetwork\n" +
            "\tfreq: 2412\n" +
            "\tsignal: -40 dBm\n" +
            "\ttx bitrate: 144.0 MBit/s\n";

        WifiLink link3 = reader.Wifi("wlp0s20f3", stamp: 11001);
        Assert.AreEqual("OtherNetwork", link3.Ssid);
        Assert.AreEqual(2, runner.Executions.Count);
    }

    [TestMethod]
    public void WirelessLevel_ProcNetWirelessFallback_ParsesLevelColumn()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/net/wireless",
            "Inter-| sta-|   Quality        |   Discarded packets               | Missed | WE\n" +
            " face | tus | link level noise |  nwid  crypt   frag  retry   misc | beacon | 22\n" +
            "wlp0s20f3: 0000   45.  -68.  -256        0      0      0      0      0        0\n");

        var runner = new FakeCommandRunner();
        var reader = new NetworkReader(fake.Root, runner);

        Assert.AreEqual(-68, reader.WirelessLevel("wlp0s20f3"));
        Assert.IsNull(reader.WirelessLevel("eth0")); // interface not found in wireless file
    }

    [TestMethod]
    public void Read_WhenIwFails_FallsBackToProcNetWirelessForSignal()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/net/dev",
            "Inter-|   Receive                                                |  Transmit\n" +
            " face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed\n" +
            "wlp0s20f3: 1000 0 0 0 0 0 0 0 1000 0 0 0 0 0 0 0\n");

        fake.CreateDirectory("sys/class/net/wlp0s20f3/wireless");
        fake.WriteFile("sys/class/net/wlp0s20f3/device/uevent", "DRIVER=iwlwifi\n");
        fake.WriteFile("proc/net/wireless",
            "Inter-| sta-|   Quality        |   Discarded packets               | Missed | WE\n" +
            " face | tus | link level noise |  nwid  crypt   frag  retry   misc | beacon | 22\n" +
            "wlp0s20f3: 0000   50.  -72.  -256        0      0      0      0      0        0\n");

        // Command runner returns null (iw command fails / not available)
        var runner = new FakeCommandRunner();

        var reader = new NetworkReader(fake.Root, runner);
        IReadOnlyList<Adapter> adapters = reader.Read(stamp: 1000);

        Assert.AreEqual(1, adapters.Count);
        Adapter wifi = adapters[0];
        Assert.AreEqual(string.Empty, wifi.Ssid);
        Assert.IsNull(wifi.FrequencyMhz);
        Assert.IsNull(wifi.TxBitrateMbps);
        Assert.AreEqual(-72, wifi.SignalDbm); // Successfully fell back to /proc/net/wireless!
    }

    [TestMethod]
    public void Read_WiredSpeed_NegativeOneOrMissing_SetsLinkSpeedMbpsNull()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/net/dev",
            "Inter-|   Receive                                                |  Transmit\n" +
            " face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed\n" +
            "  eth0: 1000 0 0 0 0 0 0 0 1000 0 0 0 0 0 0 0\n" +
            "  eth1: 1000 0 0 0 0 0 0 0 1000 0 0 0 0 0 0 0\n" +
            "  eth2: 1000 0 0 0 0 0 0 0 1000 0 0 0 0 0 0 0\n");

        // eth0 has speed = -1 (disconnected)
        fake.WriteFile("sys/class/net/eth0/speed", "-1\n");
        fake.WriteFile("sys/class/net/eth0/type", "1\n");

        // eth1 has valid speed = 1000
        fake.WriteFile("sys/class/net/eth1/speed", "1000\n");
        fake.WriteFile("sys/class/net/eth1/type", "1\n");

        // eth2 has missing speed file
        fake.WriteFile("sys/class/net/eth2/type", "1\n");

        var reader = new NetworkReader(fake.Root);
        IReadOnlyList<Adapter> adapters = reader.Read(stamp: 1000);

        Adapter eth0 = adapters.First(a => a.Name == "eth0");
        Assert.IsNull(eth0.LinkSpeedMbps);

        Adapter eth1 = adapters.First(a => a.Name == "eth1");
        Assert.AreEqual(1000.0, eth1.LinkSpeedMbps);

        Adapter eth2 = adapters.First(a => a.Name == "eth2");
        Assert.IsNull(eth2.LinkSpeedMbps);
    }

    [TestMethod]
    public void DriverName_ResolvesFromSymlinkOrUevent()
    {
        using var fake = new FakeRoot();

        // eth0: driver symlink exists
        string driverTarget = fake.CreateDirectory("sys/bus/pci/drivers/r8169");
        fake.CreateDirectorySymlink("sys/class/net/eth0/device/driver", driverTarget);

        // eth1: no driver symlink, uevent has DRIVER=iwlwifi
        fake.WriteFile("sys/class/net/eth1/device/uevent", "DRIVER=iwlwifi\nPCI_ID=8086:02f0\n");

        // eth2: neither exists
        fake.CreateDirectory("sys/class/net/eth2/device");

        var reader = new NetworkReader(fake.Root);

        Assert.AreEqual("r8169", reader.DriverName("eth0"));
        Assert.AreEqual("iwlwifi", reader.DriverName("eth1"));
        Assert.AreEqual(string.Empty, reader.DriverName("eth2"));
    }

    [TestMethod]
    public void Kind_IdentifiesWiFiEthernetAndOther()
    {
        using var fake = new FakeRoot();

        // wlan0 has wireless directory -> Wi-Fi
        fake.CreateDirectory("sys/class/net/wlan0/wireless");

        // eth0 has type 1, non-wireless -> Ethernet
        fake.WriteFile("sys/class/net/eth0/type", "1\n");

        // lo has type 772 -> empty string
        fake.WriteFile("sys/class/net/lo/type", "772\n");

        var reader = new NetworkReader(fake.Root);

        Assert.AreEqual("Wi-Fi", reader.Kind("wlan0", wireless: true));
        Assert.AreEqual("Ethernet", reader.Kind("eth0", wireless: false));
        Assert.AreEqual(string.Empty, reader.Kind("lo", wireless: false));
    }

    [TestMethod]
    public void Privacy_MacAddressInSysfsOrIwOutput_NeverAppearsInModelProperties()
    {
        using var fake = new FakeRoot();

        const string targetMac = "aa:bb:cc:dd:ee:ff";
        const string apMac = "11:22:33:44:55:66";

        fake.WriteFile("proc/net/dev",
            "Inter-|   Receive                                                |  Transmit\n" +
            " face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed\n" +
            "  eth0: 1000 0 0 0 0 0 0 0 1000 0 0 0 0 0 0 0\n" +
            "wlp0s20f3: 1000 0 0 0 0 0 0 0 1000 0 0 0 0 0 0 0\n");

        fake.WriteFile("sys/class/net/eth0/address", $"{targetMac}\n");
        fake.WriteFile("sys/class/net/eth0/type", "1\n");

        fake.CreateDirectory("sys/class/net/wlp0s20f3/wireless");
        fake.WriteFile("sys/class/net/wlp0s20f3/address", "99:88:77:66:55:44\n");

        var runner = new FakeCommandRunner();
        runner.CommandOutputs["iw dev wlp0s20f3 link"] =
            $"Connected to {apMac} (on wlp0s20f3)\n" +
            "\tSSID: SecretWifi\n" +
            "\tfreq: 5220\n" +
            "\tsignal: -60 dBm\n" +
            "\ttx bitrate: 300.0 MBit/s\n";

        var reader = new NetworkReader(fake.Root, runner);
        IReadOnlyList<Adapter> adapters = reader.Read(stamp: 1000);

        var macRegex = new Regex(@"^[0-9a-fA-F]{2}(:[0-9a-fA-F]{2}){5}$", RegexOptions.Compiled);

        foreach (Adapter adapter in adapters)
        {
            // Verify all string properties of Adapter do not contain MAC addresses
            PropertyInfo[] stringProps = typeof(Adapter).GetProperties()
                .Where(p => p.PropertyType == typeof(string))
                .ToArray();

            foreach (PropertyInfo prop in stringProps)
            {
                string? value = (string?)prop.GetValue(adapter);
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                Assert.IsFalse(value.Contains(targetMac, StringComparison.OrdinalIgnoreCase),
                    $"Property {prop.Name} contained MAC {targetMac}");
                Assert.IsFalse(value.Contains(apMac, StringComparison.OrdinalIgnoreCase),
                    $"Property {prop.Name} contained AP MAC {apMac}");
                Assert.IsFalse(macRegex.IsMatch(value),
                    $"Property {prop.Name} was a MAC address: {value}");
            }
        }
    }
}
