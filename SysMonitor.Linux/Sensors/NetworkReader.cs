using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// Adapters from /proc/net/dev.  A virtual interface (bridge, veth, tun) is not
/// hardware and is left out; Wi-Fi has no /sys speed file, so its SSID, link
/// rate, signal and frequency come from <c>iw dev X link</c>, re-read at most
/// every ten seconds.
/// </summary>
public sealed class NetworkReader
{
    private const int WifiInfoIntervalMs = 10_000;
    private const int WifiSpeedIntervalMs = 5_000;

    private readonly string _root;
    private readonly ICommandRunner _runner;
    private readonly PciDatabase _pci;
    private readonly Diagnostics? _diagnostics;
    private readonly Dictionary<string, (double Speed, long Stamp)> _wifi = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (WifiLink Link, long Stamp)> _wifiInfo = new(StringComparer.Ordinal);
    private Dictionary<string, NetCounters>? _previous;
    private long _previousStamp;
    private bool _iwFailureLogged;

    public NetworkReader(
        string root = "/",
        ICommandRunner? runner = null,
        PciDatabase? pci = null,
        Diagnostics? diagnostics = null)
    {
        _root = string.IsNullOrEmpty(root) ? "/" : root;
        _runner = runner ?? new ProcessCommandRunner();
        _pci = pci ?? PciDatabase.Load(_root);
        _diagnostics = diagnostics;
    }

    public IReadOnlyList<Adapter> Read(long stamp)
    {
        string? text = SysFs.ReadText(_root, "proc", "net", "dev");
        if (text is null)
        {
            _previous = null;
            return Array.Empty<Adapter>();
        }

        var adapters = new List<Adapter>();
        var current = new Dictionary<string, NetCounters>(StringComparer.Ordinal);

        foreach (NetCounters counters in NetDev.Parse(text))
        {
            current.TryAdd(counters.Name, counters);

            if (!IsPhysical(counters.Name))
            {
                continue;
            }

            bool wireless = Directory.Exists(SysFs.Path(_root, "sys", "class", "net", counters.Name, "wireless"));
            WifiLink wifi = wireless ? Wifi(counters.Name, stamp) : WifiLink.None;
            double speed = wireless ? wifi.TxBitrateMbps ?? 0 : WiredSpeed(counters.Name);

            int? signal = wireless
                ? wifi.SignalDbm ?? WirelessLevel(counters.Name)
                : null;

            (double down, double up) = _previous is not null
                && _previous.TryGetValue(counters.Name, out NetCounters previous)
                ? NetDev.Rate(previous, counters, _previousStamp, stamp)
                : (0, 0);

            (IReadOnlyList<string> ipv4, IReadOnlyList<string> ipv6) = Addresses(counters.Name);

            adapters.Add(new Adapter
            {
                Id = counters.Name,
                Name = counters.Name,
                Description = Describe(counters.Name),
                Wireless = wireless,
                Interface = counters.Name,
                Kind = Kind(counters.Name, wireless),
                Driver = DriverName(counters.Name),
                Ipv4 = ipv4,
                Ipv6 = ipv6,
                LinkSpeedMbps = !wireless && speed > 0 ? speed : null,
                Ssid = wifi.Ssid,
                SignalDbm = signal,
                FrequencyMhz = wifi.FrequencyMhz,
                TxBitrateMbps = wifi.TxBitrateMbps,
                SpeedMbps = speed,
                DownMb = down,
                UpMb = up,
                TotalReceived = counters.Received,
                TotalSent = counters.Sent,
            });
        }

        _previous = current;
        _previousStamp = stamp;
        return adapters;
    }

    /// <summary>
    /// /sys/devices/virtual/net holds interfaces the kernel made, not hardware.
    /// An unresolvable link is kept, which also lets a fake tree work.
    /// </summary>
    public bool IsPhysical(string name)
    {
        string resolved = SysFs.ResolvedPath(SysFs.Path(_root, "sys", "class", "net", name));
        return !resolved.Contains("/devices/virtual/", StringComparison.Ordinal);
    }

    public double WiredSpeed(string name)
    {
        int? speed = SysFs.ReadInt(_root, "sys", "class", "net", name, "speed");
        return speed is int value && value > 0 ? value : 0;
    }

    /// <summary>
    /// The link rate from the Wi-Fi driver, at most every five seconds.  The
    /// adapter's own properties come from <see cref="Wifi"/>; this stays for
    /// callers that only want the rate.
    /// </summary>
    public double WifiSpeed(string name, long stamp)
    {
        if (_wifi.TryGetValue(name, out (double Speed, long Stamp) cached)
            && stamp >= cached.Stamp
            && stamp - cached.Stamp < WifiSpeedIntervalMs)
        {
            return cached.Speed;
        }

        string? output = _runner.Run("iw", $"dev {name} link", 2000);
        double speed = ParseIwSpeed(output);
        _wifi[name] = (speed, stamp);
        return speed;
    }

    /// <summary>
    /// One Wi-Fi network's details, from a single <c>iw</c> call shared by
    /// every caller in the ten-second window.  A machine without <c>iw</c> gets
    /// nothing here and one line in the log, not one per sample.
    /// </summary>
    public WifiLink Wifi(string name, long stamp)
    {
        if (_wifiInfo.TryGetValue(name, out (WifiLink Link, long Stamp) cached)
            && stamp >= cached.Stamp
            && stamp - cached.Stamp < WifiInfoIntervalMs)
        {
            return cached.Link;
        }

        string? output = _runner.Run("iw", $"dev {name} link", 2000);
        if (output is null && !_iwFailureLogged)
        {
            _iwFailureLogged = true;
            _diagnostics?.Line(
                "iw not available: Wi-Fi SSID, frequency and link rate are not shown");
        }

        WifiLink link = ParseIwLink(output);
        _wifiInfo[name] = (link, stamp);
        return link;
    }

    /// <summary>The "tx bitrate: 866.7 MBit/s" line, in Mbps.  0 when absent.</summary>
    public static double ParseIwSpeed(string? output)
    {
        if (output is null)
        {
            return 0;
        }

        const string marker = "tx bitrate:";
        foreach (string line in output.Split('\n'))
        {
            int at = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                continue;
            }

            string rest = line[(at + marker.Length)..].Trim();
            string[] fields = SysFs.Fields(rest);
            if (fields.Length > 0
                && double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double speed)
                && speed > 0)
            {
                return speed;
            }
        }

        return 0;
    }

    /// <summary>
    /// The four lines worth keeping from <c>iw dev X link</c>.  A "Connected
    /// to" line carries the access point's MAC address and is discarded with
    /// the rest: no address is read into the model.
    /// </summary>
    public static WifiLink ParseIwLink(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return WifiLink.None;
        }

        string ssid = string.Empty;
        int? signal = null;
        int? frequency = null;
        double? bitrate = null;

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.Trim();

            if (line.StartsWith("SSID:", StringComparison.Ordinal))
            {
                ssid = line["SSID:".Length..].Trim();
            }
            else if (line.StartsWith("freq:", StringComparison.Ordinal))
            {
                frequency = Whole(First(line["freq:".Length..]));
            }
            else if (line.StartsWith("signal:", StringComparison.Ordinal))
            {
                signal = Whole(First(line["signal:".Length..]));
            }
            else if (line.StartsWith("tx bitrate:", StringComparison.OrdinalIgnoreCase))
            {
                bitrate = First(line["tx bitrate:".Length..]);
            }
        }

        return new WifiLink(ssid, signal, frequency, bitrate);
    }

    /// <summary>
    /// The level column of <c>/proc/net/wireless</c>, the fallback when iw is
    /// not installed.  The -256 sentinel means the driver reported no reading.
    /// </summary>
    public int? WirelessLevel(string name)
    {
        string? text = SysFs.ReadText(_root, "proc", "net", "wireless");
        if (text is null)
        {
            return null;
        }

        foreach (string raw in text.Split('\n'))
        {
            int colon = raw.IndexOf(':');
            if (colon <= 0
                || !string.Equals(raw[..colon].Trim(), name, StringComparison.Ordinal))
            {
                continue;
            }

            // status link level noise: the level is the third figure.
            string[] fields = SysFs.Fields(raw[(colon + 1)..]);
            if (fields.Length < 3)
            {
                return null;
            }

            string level = fields[2].TrimEnd('.');
            if (double.TryParse(level, NumberStyles.Float, CultureInfo.InvariantCulture, out double dbm)
                && dbm < 0 && dbm > -200)
            {
                return (int)Math.Round(dbm);
            }
            return null;
        }

        return null;
    }

    /// <summary>
    /// "iwlwifi · Intel Corporation Wi-Fi 6 AX201": the driver from the
    /// interface's uevent, then the PCI name when the card is on PCI.
    /// </summary>
    public string Describe(string name)
    {
        string device = SysFs.Path(_root, "sys", "class", "net", name, "device");
        string? uevent = SysFs.ReadText(device, "uevent");
        if (uevent is null)
        {
            return string.Empty;
        }

        string driver = string.Empty;
        string pciName = string.Empty;

        foreach (string line in uevent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            string key = line[..equals];
            string value = line[(equals + 1)..].Trim();

            if (key == "DRIVER")
            {
                driver = value;
            }
            else if (key == "PCI_ID")
            {
                pciName = PciName(value);
            }
        }

        if (driver.Length > 0 && pciName.Length > 0)
        {
            return driver + " · " + pciName;
        }
        return driver.Length > 0 ? driver : pciName;
    }

    /// <summary>The kernel module's name: the basename of <c>device/driver</c>.</summary>
    public string DriverName(string name)
    {
        string linked = SysFs.LinkTargetName(
            SysFs.Path(_root, "sys", "class", "net", name, "device", "driver"));
        if (linked.Length > 0)
        {
            return linked;
        }

        string? uevent = SysFs.ReadText(_root, "sys", "class", "net", name, "device", "uevent");
        if (uevent is null)
        {
            return string.Empty;
        }

        foreach (string line in uevent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("DRIVER=", StringComparison.Ordinal))
            {
                return line["DRIVER=".Length..].Trim();
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// "Wi-Fi" when the kernel put the interface under <c>wireless</c>,
    /// "Ethernet" for a non-wireless interface of ARPHRD type 1, else empty.
    /// </summary>
    public string Kind(string name, bool wireless)
    {
        if (wireless)
        {
            return "Wi-Fi";
        }

        return SysFs.ReadInt(_root, "sys", "class", "net", name, "type") == 1
            ? "Ethernet"
            : string.Empty;
    }

    /// <summary>
    /// The interface's unicast addresses with their prefixes, straight from
    /// .NET rather than by running <c>ip</c>.  Names are matched exactly; the
    /// IPv6 zone is dropped because it is the interface name again.
    /// </summary>
    public static (IReadOnlyList<string> V4, IReadOnlyList<string> V6) Addresses(string name)
    {
        var v4 = new List<string>();
        var v6 = new List<string>();

        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!string.Equals(adapter.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
                {
                    string text = address.Address.ToString();
                    int zone = text.IndexOf('%');
                    if (zone >= 0)
                    {
                        text = text[..zone];
                    }

                    string entry = $"{text}/{address.PrefixLength.ToString(CultureInfo.InvariantCulture)}";
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        v4.Add(entry);
                    }
                    else if (address.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        v6.Add(entry);
                    }
                }
            }
        }
        catch (Exception)
        {
            // NetworkInformation can fail on a sandbox; the addresses stay empty.
        }

        return (v4, v6);
    }

    /// <summary>The first whitespace-separated field, as a number.</summary>
    private static double? First(string text)
    {
        string[] fields = SysFs.Fields(text);
        if (fields.Length == 0)
        {
            return null;
        }

        return double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }

    private static int? Whole(double? value) =>
        value is double number ? (int)Math.Round(number) : null;

    private string PciName(string id)
    {
        string[] parts = id.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int vendor)
            || !int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int device))
        {
            return string.Empty;
        }
        return _pci.Describe(vendor, device);
    }
}

/// <summary>
/// What <c>iw dev X link</c> says about the current network.  The SSID is
/// model-only and never printed; the access point's address is not read at all.
/// </summary>
public sealed record WifiLink(
    string Ssid,
    int? SignalDbm,
    int? FrequencyMhz,
    double? TxBitrateMbps)
{
    public static WifiLink None { get; } = new(string.Empty, null, null, null);
}
