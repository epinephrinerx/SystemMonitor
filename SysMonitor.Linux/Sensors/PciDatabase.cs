using System.Globalization;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// The PCI ID list from <c>pci.ids</c>.  It is a plain text file shipped by
/// pciutils/hwdata; reading it needs no package and no daemon.
/// </summary>
public sealed class PciDatabase
{
    private readonly Dictionary<int, string> _vendors;
    private readonly Dictionary<(int Vendor, int Device), string> _devices;

    private PciDatabase(
        Dictionary<int, string> vendors,
        Dictionary<(int Vendor, int Device), string> devices)
    {
        _vendors = vendors;
        _devices = devices;
    }

    public static PciDatabase Empty { get; } = new(new(), new());

    public static PciDatabase Load(string root = "/")
    {
        foreach (string path in new[] { "usr/share/misc/pci.ids", "usr/share/hwdata/pci.ids" })
        {
            string? text = SysFs.ReadText(root, path.Split('/'));
            if (text is not null)
            {
                return Parse(text);
            }
        }
        return Empty;
    }

    public static PciDatabase Parse(string text)
    {
        var vendors = new Dictionary<int, string>();
        var devices = new Dictionary<(int, int), string>();
        int vendor = -1;

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            int tabs = 0;
            while (tabs < line.Length && line[tabs] == '\t')
            {
                tabs++;
            }

            if (tabs == 0)
            {
                // "10de  NVIDIA Corporation" -- class and subclass sections
                // start with a non-hex letter and are skipped here.  A new
                // section also ends the vendor whose devices we were reading.
                string[] fields = SysFs.Fields(line);
                if (fields.Length >= 2 && TryHex(fields[0], out int id))
                {
                    vendor = id;
                    vendors[id] = string.Join(' ', fields[1..]);
                }
                else
                {
                    vendor = -1;
                }
                continue;
            }

            // A subsystem is indented twice; only devices (one tab) name the
            // card itself.
            if (tabs != 1 || vendor < 0)
            {
                continue;
            }

            string body = line[1..];
            string[] parts = SysFs.Fields(body);
            if (parts.Length >= 2 && TryHex(parts[0], out int deviceId))
            {
                devices[(vendor, deviceId)] = string.Join(' ', parts[1..]);
            }
        }

        return new PciDatabase(vendors, devices);
    }

    public string VendorName(int vendor) =>
        _vendors.TryGetValue(vendor, out string? name) ? name : string.Empty;

    public string DeviceName(int vendor, int device) =>
        _devices.TryGetValue((vendor, device), out string? name) ? name : string.Empty;

    /// <summary>"NVIDIA Corporation GP108BM [GeForce MX250]": vendor and device.</summary>
    public string Describe(int vendor, int device)
    {
        string vendorName = VendorName(vendor);
        string deviceName = DeviceName(vendor, device);

        if (vendorName.Length > 0 && deviceName.Length > 0)
        {
            return vendorName + " " + deviceName;
        }
        return vendorName.Length > 0 ? vendorName : deviceName;
    }

    private static bool TryHex(string text, out int value)
    {
        value = 0;
        return text.Length == 4
            && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
