namespace SysMonitor.Linux.Sensors;

/// <summary>
/// One mounted filesystem as mountinfo describes it.  <see cref="Root"/> is the
/// directory inside the source that the mount exposes; a btrfs subvolume has a
/// root other than <c>/</c>, which is what tells two mounts of one device apart.
/// </summary>
public readonly record struct MountEntry(
    string MountPoint,
    string Device,
    string FileSystem,
    string Root = "/");

/// <summary>
/// /proc/self/mountinfo.  The file lists every mount the process can see,
/// including pseudo filesystems and snap loops, so the sampler filters down to
/// the filesystems a person would call a disk.
/// </summary>
public static class MountInfo
{
    private static readonly HashSet<string> RealFilesystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "ext2", "ext3", "ext4", "btrfs", "xfs", "f2fs", "jfs", "reiserfs",
        "vfat", "msdos", "exfat", "ntfs", "ntfs3", "fuseblk", "iso9660", "udf",
        "nfs", "nfs4", "cifs", "smb3",
    };

    private static readonly HashSet<string> NetworkFilesystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "nfs", "nfs4", "cifs", "smb3",
    };

    private static readonly string[] SkippedPrefixes =
    {
        "/snap", "/var/snap", "/run",
    };

    private static readonly string[] RemovableRoots =
    {
        "/run/media", "/media",
    };

    public static bool IsRealFilesystem(string fileSystem) =>
        RealFilesystems.Contains(fileSystem);

    public static bool IsNetworkFilesystem(string fileSystem) =>
        NetworkFilesystems.Contains(fileSystem);

    /// <summary>
    /// True for a normal user's removable-media mount: a real filesystem below
    /// <c>/run/media/&lt;user&gt;/</c> or <c>/media/&lt;user&gt;/</c>.  The rest
    /// of <c>/run</c> stays out of the disk list.
    /// </summary>
    public static bool IsRemovableMount(string mountPoint)
    {
        foreach (string root in RemovableRoots)
        {
            string prefix = root + "/";
            if (!mountPoint.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string rest = mountPoint[prefix.Length..];
            int slash = rest.IndexOf('/');
            if (slash > 0 && slash < rest.Length - 1)
            {
                return true;
            }
        }
        return false;
    }

    public static IReadOnlyList<MountEntry> Parse(string text)
    {
        var entries = new List<MountEntry>();

        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseLine(line, out MountEntry entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>
    /// The mounts worth showing: real filesystems, not pseudo or snap mounts,
    /// and one entry per (source, subvolume root) pair.  A btrfs subvolume is
    /// the same source with a different root and stays a separate entry; two
    /// mounts of an identical pair are a bind mount, where the shortest mount
    /// point wins.
    /// </summary>
    public static IReadOnlyList<MountEntry> Real(IEnumerable<MountEntry> entries)
    {
        var bySource = new Dictionary<(string Device, string Root), MountEntry>();
        var order = new List<(string Device, string Root)>();

        foreach (MountEntry entry in entries)
        {
            if (!IsRealFilesystem(entry.FileSystem))
            {
                continue;
            }

            if (IsSkippedMountPoint(entry.MountPoint)
                && !IsRemovableMount(entry.MountPoint))
            {
                continue;
            }

            bool network = IsNetworkFilesystem(entry.FileSystem);
            if (!network
                && (!entry.Device.StartsWith("/dev/", StringComparison.Ordinal)
                    || entry.Device.StartsWith("/dev/loop", StringComparison.Ordinal)))
            {
                continue;
            }

            var key = (entry.Device, entry.Root);
            if (!bySource.TryGetValue(key, out MountEntry existing))
            {
                bySource[key] = entry;
                order.Add(key);
            }
            else if (entry.MountPoint.Length < existing.MountPoint.Length)
            {
                bySource[key] = entry;
            }
        }

        return order.Select(key => bySource[key]).ToList();
    }

    public static IReadOnlyList<MountEntry> Real(string text) => Real(Parse(text));

    private static bool IsSkippedMountPoint(string mountPoint)
    {
        foreach (string prefix in SkippedPrefixes)
        {
            if (mountPoint == prefix || mountPoint.StartsWith(prefix + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryParseLine(string line, out MountEntry entry)
    {
        entry = default;
        string[] fields = SysFs.Fields(line).Select(SysFs.DecodeOctal).ToArray();
        if (fields.Length < 7)
        {
            return false;
        }

        // Optional fields (shared:X, master:X, ...) sit between the per-mount
        // options and the "-" separator.
        int separator = Array.IndexOf(fields, "-");
        if (separator < 6 || separator + 2 >= fields.Length)
        {
            return false;
        }

        string fileSystem = fields[separator + 1];
        string device = fields[separator + 2];
        string mountPoint = fields[4];
        string sourceRoot = fields[3];

        if (mountPoint.Length == 0 || device.Length == 0 || fileSystem.Length == 0)
        {
            return false;
        }

        entry = new MountEntry(mountPoint, device, fileSystem,
                               sourceRoot.Length > 0 ? sourceRoot : "/");
        return true;
    }
}
