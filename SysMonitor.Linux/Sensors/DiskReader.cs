using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// Builds one <see cref="Disk"/> per mounted filesystem.  Space comes from
/// statvfs through <see cref="DriveInfo"/>; I/O comes from each partition's own
/// /proc/diskstats line, never its parent disk's, so two partitions on one
/// NVMe do not report the same numbers.
/// </summary>
public sealed class DiskReader
{
    private readonly string _root;
    private readonly Func<string, (long Total, long Free)> _space;
    private Dictionary<string, (DiskCounters Counters, long Stamp)> _previous = new();

    public DiskReader(
        string root = "/",
        Func<string, (long Total, long Free)>? space = null)
    {
        _root = root;
        _space = space ?? SpaceFromStatVfs;
    }

    public IReadOnlyList<Disk> Read(IReadOnlyList<MountEntry> mounts, long stamp)
    {
        IReadOnlyDictionary<string, DiskCounters> counters = ReadCounters();
        Dictionary<string, string> labels = ReadLabels();

        var disks = new List<Disk>();
        var physical = new Dictionary<string, PhysicalDisk>(StringComparer.Ordinal);
        int nextDiskNumber = 0;

        foreach (MountEntry mount in mounts)
        {
            // /dev/mapper/<name> is a dm-N device; diskstats and sysfs both
            // know it by the kernel name, not the mapper name.
            string device = mount.Device.StartsWith("/dev/", StringComparison.Ordinal)
                ? BlockDevices.ResolveDevice(_root, mount.Device)
                : BlockDevices.NameForDevice(mount.Device);
            string parent = BlockDevices.PhysicalDiskName(_root, device);

            PhysicalDisk? disk = null;
            if (parent.Length > 0)
            {
                if (!physical.TryGetValue(parent, out disk))
                {
                    disk = BlockDevices.ReadPhysical(_root, parent, nextDiskNumber++);
                    physical[parent] = disk;
                }
            }

            labels.TryGetValue(device, out string? label);
            label ??= string.Empty;

            (long total, long free) = Space(mount.MountPoint);
            free = Math.Clamp(free, 0, total);
            long used = total - free;

            (double readMb, double writeMb) = rates(device);
            int? temp = parent.Length > 0 ? BlockDevices.Temperature(_root, parent) : null;

            long partitionBytes = device.Length > 0 ? BlockDevices.Bytes(_root, device) : 0;
            if (partitionBytes == 0 && disk is not null)
            {
                partitionBytes = disk.Bytes;
            }

            string media = disk?.Media is { Length: > 0 } known ? known : "Disk";
            if (disk is not null
                && MountInfo.IsRemovableMount(mount.MountPoint)
                && (BlockDevices.IsRemovable(_root, parent) || disk.Bus == "USB"))
            {
                media = "Removable";
            }

            disks.Add(new Disk
            {
                Letter = mount.MountPoint,
                Label = label,
                Media = media,
                Bus = disk?.Bus is { Length: > 0 } bus ? bus : "Unknown",
                IsNetwork = MountInfo.IsNetworkFilesystem(mount.FileSystem),
                Usage = total > 0 ? (int)Math.Clamp(
                    Math.Round(used * 100.0 / total, MidpointRounding.AwayFromZero), 0, 100) : 0,
                UsedGb = used / 1073741824.0,
                TotalGb = total / 1073741824.0,
                ReadMb = readMb,
                WriteMb = writeMb,
                Temp = temp,
                TempSource = temp is null ? TempSource.None : TempSource.Storage,
                Detail = new DriveDetail
                {
                    Letter = mount.MountPoint,
                    DiskNumber = disk?.Number,
                    PartitionNumber = device.Length > 0
                        ? BlockDevices.PartitionNumber(_root, device)
                        : 0,
                    PartitionBytes = partitionBytes,
                    PartitionOffset = device.Length > 0
                        ? BlockDevices.PartitionStartBytes(_root, device)
                        : 0,
                    IsBoot = mount.MountPoint == "/",
                    FileSystem = mount.FileSystem,
                    Label = label,
                    Disk = disk,
                },
            });

            (double ReadMb, double WriteMb) rates(string name)
            {
                if (name.Length == 0 || !counters.TryGetValue(name, out DiskCounters current))
                {
                    return (0, 0);
                }

                if (_previous.TryGetValue(name, out (DiskCounters Counters, long Stamp) previous))
                {
                    return DiskStats.Rate(previous.Counters, current, previous.Stamp, stamp);
                }

                return (0, 0);
            }
        }

        var next = new Dictionary<string, (DiskCounters, long)>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, DiskCounters> pair in counters)
        {
            next[pair.Key] = (pair.Value, stamp);
        }
        _previous = next;

        return disks;
    }

    private IReadOnlyDictionary<string, DiskCounters> ReadCounters()
    {
        string? text = SysFs.ReadText(_root, "proc", "diskstats");
        var counters = new Dictionary<string, DiskCounters>(StringComparer.Ordinal);
        if (text is null)
        {
            return counters;
        }

        foreach (DiskCounters counter in DiskStats.Parse(text))
        {
            counters.TryAdd(counter.Name, counter);
        }
        return counters;
    }

    /// <summary>
    /// /dev/disk/by-label is a directory of symlinks; the link target names the
    /// partition.  udev escapes spaces as \x20.
    /// </summary>
    private Dictionary<string, string> ReadLabels()
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        string dir = SysFs.Path(_root, "dev", "disk", "by-label");

        foreach (string link in SysFs.Files(dir))
        {
            string? target = SysFs.LinkTarget(link);
            if (target is null)
            {
                continue;
            }

            string device = BlockDevices.NameForDevice(target);
            if (device.Length > 0)
            {
                labels.TryAdd(device, DecodeUdev(Path.GetFileName(link)));
            }
        }

        return labels;
    }

    /// <summary>udev encodes a space as \x20 and a backslash as \x5c.</summary>
    public static string DecodeUdev(string text)
    {
        if (text.IndexOf("\\x", StringComparison.Ordinal) < 0)
        {
            return text;
        }

        var result = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 3 < text.Length && text[i + 1] == 'x'
                && int.TryParse(text.AsSpan(i + 2, 2), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out int code))
            {
                result.Append((char)code);
                i += 3;
                continue;
            }
            result.Append(text[i]);
        }
        return result.ToString();
    }

    private (long Total, long Free) Space(string mountPoint)
    {
        try
        {
            return _space(mountPoint);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    private static (long Total, long Free) SpaceFromStatVfs(string mountPoint)
    {
        try
        {
            var info = new DriveInfo(mountPoint);
            return (info.TotalSize, info.AvailableFreeSpace);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }
}
