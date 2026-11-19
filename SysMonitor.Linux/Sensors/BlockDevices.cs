using System.Globalization;
using System.Text.RegularExpressions;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// Everything about the block layer that sysfs can answer without opening the
/// device: the physical disk behind a partition, its media and bus, its
/// temperature and its partition table.
/// </summary>
public static class BlockDevices
{
    private static readonly Regex NvmeName = new(
        @"^(?<disk>(?:nvme\d+n\d+)|(?:mmcblk\d+))(?:p(?<part>\d+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ScsiName = new(
        @"^(?<disk>(?:sd|vd|hd|xvd)[a-z]+)(?<part>\d+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The kernel's name for a mountinfo source: /dev/nvme0n1p2 to nvme0n1p2.</summary>
    public static string NameForDevice(string device)
    {
        string name = device.TrimEnd('/');
        int slash = name.LastIndexOf('/');
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    /// <summary>nvme0n1p2 to nvme0n1, sda1 to sda. Empty for LVM and friends.</summary>
    public static string ParentDisk(string name)
    {
        if (!SysFs.IsSafeName(name))
        {
            return string.Empty;
        }

        Match nvme = NvmeName.Match(name);
        if (nvme.Success)
        {
            return nvme.Groups["disk"].Value;
        }

        Match scsi = ScsiName.Match(name);
        return scsi.Success ? scsi.Groups["disk"].Value : string.Empty;
    }

    /// <summary>
    /// The kernel name of the block device behind a mount source.  A plain
    /// device keeps its name; <c>/dev/mapper/&lt;name&gt;</c> is a device-mapper
    /// node whose kernel name is <c>dm-N</c>, which is what /proc/diskstats
    /// reports, so the mapper name is looked up in <c>dm/name</c>.
    /// </summary>
    public static string ResolveDevice(string root, string source)
    {
        string name = NameForDevice(source);
        if (!SysFs.IsSafeName(name))
        {
            return string.Empty;
        }

        if (BlockDeviceExists(root, name))
        {
            return name;
        }

        foreach (string dir in DmDevices(root))
        {
            if (string.Equals(SysFs.ReadTrimmed(dir, "dm", "name"), name, StringComparison.Ordinal))
            {
                return Path.GetFileName(dir);
            }
        }

        return name;
    }

    /// <summary>
    /// The physical disk behind a block device.  A device-mapper or md device
    /// is virtual: its <c>slaves/</c> are the members that really hold the
    /// data, recursively.  When several members exist the first in sort order
    /// is used, and the rest are not mentioned.  When every leaf is virtual
    /// (loop, zram, a nested dm with no disk below) the physical disk is
    /// unknown and the name is empty.
    /// </summary>
    public static string PhysicalDiskName(string root, string name)
    {
        if (!SysFs.IsSafeName(name))
        {
            return string.Empty;
        }

        string blockPath = SysFs.Path(root, "sys", "block", name);
        if (!Directory.Exists(blockPath))
        {
            blockPath = SysFs.Path(root, "sys", "class", "block", name);
        }

        if (Directory.Exists(blockPath))
        {
            string physical = FirstPhysical(blockPath);
            if (physical.Length > 0)
            {
                return physical;
            }
        }

        return ParentDisk(name);
    }

    /// <summary>The partition number, from sysfs or, for a fake tree, from the name.</summary>
    public static int PartitionNumber(string root, string name)
    {
        int? fromSysfs = SysFs.ReadInt(root, "sys", "class", "block", name, "partition");
        if (fromSysfs is int number && number > 0)
        {
            return number;
        }

        Match nvme = NvmeName.Match(name);
        if (nvme.Success && nvme.Groups["part"].Success)
        {
            return ParseInt(nvme.Groups["part"].Value);
        }

        Match scsi = ScsiName.Match(name);
        if (scsi.Success && scsi.Groups["part"].Success)
        {
            return ParseInt(scsi.Groups["part"].Value);
        }

        return 0;
    }

    public static long Bytes(string root, string name)
    {
        long? sectors = SysFs.ReadLong(root, "sys", "class", "block", name, "size");
        return sectors is long value && value > 0 && value <= long.MaxValue / 512
            ? value * 512
            : 0;
    }

    public static long PartitionStartBytes(string root, string name)
    {
        long? sectors = SysFs.ReadLong(root, "sys", "class", "block", name, "start");
        return sectors is long value && value > 0 && value <= long.MaxValue / 512
            ? value * 512
            : 0;
    }

    /// <summary>The kernel's ejectable flag for the disk.</summary>
    public static bool IsRemovable(string root, string disk) =>
        SysFs.ReadInt(root, "sys", "class", "block", disk, "removable") == 1;

    /// <summary>SSD, HDD, Removable or Disk, from sysfs attributes only.</summary>
    public static string Media(string root, string disk)
    {
        if (IsRemovable(root, disk))
        {
            return "Removable";
        }

        int? rotational = SysFs.ReadInt(root, "sys", "class", "block", disk, "queue", "rotational");
        if (rotational == 0)
        {
            return "SSD";
        }
        if (rotational == 1)
        {
            return "HDD";
        }

        return disk.StartsWith("nvme", StringComparison.Ordinal) ? "SSD" : "Disk";
    }

    /// <summary>
    /// NVMe, SATA, USB, Virtual, SD/MMC or Unknown, from where the device
    /// hangs in sysfs.  The name is never used to guess a bus: an <c>sd</c>
    /// name says nothing about SATA versus USB, so only the resolved device
    /// path decides.  Virtual devices keep their kernel name as the marker,
    /// because sysfs places them under /virtio or /mmc_host.
    /// </summary>
    public static string Bus(string root, string disk)
    {
        if (!SysFs.IsSafeName(disk))
        {
            return "Unknown";
        }

        string path = SysFs.ResolvedPath(SysFs.Path(root, "sys", "class", "block", disk));
        if (path.Length == 0)
        {
            path = SysFs.Path(root, "sys", "class", "block", disk);
        }
        string lower = path.ToLowerInvariant();

        if (lower.Contains("/usb", StringComparison.Ordinal))
        {
            return "USB";
        }
        if (lower.Contains("/nvme", StringComparison.Ordinal)
            || disk.StartsWith("nvme", StringComparison.Ordinal))
        {
            return "NVMe";
        }
        if (lower.Contains("/virtio", StringComparison.Ordinal)
            || disk.StartsWith("vd", StringComparison.Ordinal)
            || disk.StartsWith("xvd", StringComparison.Ordinal))
        {
            return "Virtual";
        }
        if (lower.Contains("/ata", StringComparison.Ordinal)
            || lower.Contains("/sata", StringComparison.Ordinal))
        {
            return "SATA";
        }
        if (lower.Contains("/mmc", StringComparison.Ordinal)
            || disk.StartsWith("mmcblk", StringComparison.Ordinal))
        {
            return "SD/MMC";
        }

        return "Unknown";
    }

    /// <summary>
    /// The disk's own hwmon temperature.  NVMe puts the chip directly under the
    /// controller (device/hwmon3); libata's drivetemp puts it under
    /// device/hwmon/hwmonN.
    /// </summary>
    public static int? Temperature(string root, string disk)
    {
        if (!SysFs.IsSafeName(disk))
        {
            return null;
        }

        string device = SysFs.Path(root, "sys", "class", "block", disk, "device");
        return Hwmon.DeviceTemperature(device);
    }

    public static PhysicalDisk ReadPhysical(string root, string disk, int number)
    {
        if (!SysFs.IsSafeName(disk))
        {
            return new PhysicalDisk { Number = number };
        }

        string basePath = SysFs.Path(root, "sys", "class", "block", disk);
        if (!Directory.Exists(basePath))
        {
            return new PhysicalDisk { Number = number };
        }

        string device = SysFs.Path(basePath, "device");
        int partitionCount = 0;
        long allocated = 0;

        foreach (string child in SysFs.Directories(basePath))
        {
            string childName = Path.GetFileName(child);
            if (!childName.StartsWith(disk, StringComparison.Ordinal)
                || SysFs.ReadInt(child, "partition") is null)
            {
                continue;
            }

            partitionCount++;
            allocated += Bytes(root, childName);
        }

        return new PhysicalDisk
        {
            Number = number,
            Model = SysFs.ReadTrimmed(device, "model") ?? string.Empty,
            Serial = SysFs.ReadTrimmed(device, "serial") ?? string.Empty,
            Firmware = SysFs.ReadTrimmed(device, "firmware_rev")
                       ?? SysFs.ReadTrimmed(device, "rev")
                       ?? string.Empty,
            Bus = Bus(root, disk),
            Media = Media(root, disk),
            Bytes = Bytes(root, disk),
            PartitionCount = partitionCount,
            AllocatedBytes = allocated,
        };
    }

    private static int ParseInt(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0;

    private static bool BlockDeviceExists(string root, string name) =>
        Directory.Exists(SysFs.Path(root, "sys", "class", "block", name))
        || Directory.Exists(SysFs.Path(root, "sys", "block", name));

    /// <summary>Every dm-N directory, from /sys/block and /sys/class/block.</summary>
    private static IReadOnlyList<string> DmDevices(string root)
    {
        var result = new List<string>();

        foreach (string dir in SysFs.Directories(root, "sys", "block"))
        {
            if (Path.GetFileName(dir).StartsWith("dm-", StringComparison.Ordinal))
            {
                result.Add(dir);
            }
        }

        foreach (string dir in SysFs.Directories(root, "sys", "class", "block"))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith("dm-", StringComparison.Ordinal)
                && !result.Any(existing => Path.GetFileName(existing) == name))
            {
                result.Add(dir);
            }
        }

        return result;
    }

    /// <summary>
    /// A block device with no hardware of its own: device-mapper (LVM,
    /// dm-crypt), software RAID, loop and zram.  The slave walk passes through
    /// these to reach the disk below, but never names one as the physical disk.
    /// </summary>
    public static bool IsVirtual(string name) =>
        name.StartsWith("dm-", StringComparison.Ordinal)
        || name.StartsWith("md", StringComparison.Ordinal)
        || name.StartsWith("loop", StringComparison.Ordinal)
        || name.StartsWith("zram", StringComparison.Ordinal);

    /// <summary>
    /// Walk <c>slaves/</c> until a member with no slaves of its own is found:
    /// that is the real disk.  A slave that is a partition is replaced by its
    /// parent disk, so the caller never reads a partition's model.  A virtual
    /// member (dm, md, loop, zram) is walked through rather than named, so a
    /// stack whose only leaves are virtual has no physical disk at all.
    /// </summary>
    private static string FirstPhysical(string blockPath)
    {
        foreach (string slave in SysFs.Directories(blockPath, "slaves"))
        {
            string slaveName = Path.GetFileName(slave);
            if (!SysFs.IsSafeName(slaveName))
            {
                continue;
            }

            // dm-crypt over LVM over a real disk resolves to the real disk.
            string nested = FirstPhysical(slave);
            if (nested.Length > 0)
            {
                return nested;
            }

            if (IsVirtual(slaveName))
            {
                // Nothing physical under it: try the next member instead.
                continue;
            }

            string disk = ParentDisk(slaveName);
            string candidate = disk.Length > 0 ? disk : slaveName;
            if (!IsVirtual(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }
}
