using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// Adapters from /proc/net/dev.  A virtual interface (bridge, veth, tun) is not
/// hardware and is left out; Wi-Fi has no /sys speed file, so its link rate
/// comes from <c>iw dev X link</c>, re-read at most every five seconds.
/// </summary>
public sealed class NetworkReader
{
    private readonly string _root;
    private readonly ICommandRunner _runner;
    private readonly PciDatabase _pci;
    private readonly Dictionary<string, (double Speed, long Stamp)> _wifi = new(StringComparer.Ordinal);
    private Dictionary<string, NetCounters>? _previous;
    private long _previousStamp;

    public NetworkReader(
        string root = "/",
        ICommandRunner? runner = null,
        PciDatabase? pci = null)
    {
        _root = string.IsNullOrEmpty(root) ? "/" : root;
        _runner = runner ?? new ProcessCommandRunner();
        _pci = pci ?? PciDatabase.Load(_root);
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
            double speed = wireless ? WifiSpeed(counters.Name, stamp) : WiredSpeed(counters.Name);

            (double down, double up) = _previous is not null
                && _previous.TryGetValue(counters.Name, out NetCounters previous)
                ? NetDev.Rate(previous, counters, _previousStamp, stamp)
                : (0, 0);

            adapters.Add(new Adapter
            {
                Id = counters.Name,
                Name = counters.Name,
                Description = Describe(counters.Name),
                Wireless = wireless,
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

    public double WifiSpeed(string name, long stamp)
    {
        if (_wifi.TryGetValue(name, out (double Speed, long Stamp) cached)
            && stamp >= cached.Stamp
            && stamp - cached.Stamp < 5000)
        {
            return cached.Speed;
        }

        string? output = _runner.Run("iw", $"dev {name} link", 2000);
        double speed = ParseIwSpeed(output);
        _wifi[name] = (speed, stamp);
        return speed;
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
