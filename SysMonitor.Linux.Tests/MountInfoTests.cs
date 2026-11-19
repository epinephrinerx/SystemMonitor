using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class MountInfoTests
{
    private const string SampleMountinfo =
        // root on ext4
        "28 1 8:2 / / rw,relatime shared:1 - ext4 /dev/nvme0n1p2 rw,errors=remount-ro\n" +
        // boot on vfat
        "29 28 8:1 / /boot/efi rw,relatime shared:2 - vfat /dev/nvme0n1p1 rw\n" +
        // bind mount of root to a deeper path (same device /dev/nvme0n1p2, same root /)
        "30 28 8:2 / /var/log rw,relatime shared:1 - ext4 /dev/nvme0n1p2 rw\n" +
        // snap loop device (should be skipped)
        "50 28 7:0 / /snap/core/1234 ro,nodev shared:3 - squashfs /dev/loop0 ro\n" +
        // pseudo filesystem tmpfs (should be skipped)
        "51 28 0:25 / /run rw,nosuid,nodev shared:4 - tmpfs tmpfs rw,size=800m\n" +
        // pseudo filesystem proc (should be skipped)
        "52 28 0:26 / /proc rw,nosuid,nodev shared:5 - proc proc rw\n" +
        // removable USB flash drive mounted under /run/media/alice/USB\040DRIVE (octal space \040)
        "60 28 8:17 / /run/media/alice/USB\\040DRIVE rw,nosuid,nodev shared:6 - vfat /dev/sdb1 rw\n" +
        // removable drive under /media/bob/EXT_DISK
        "61 28 8:33 / /media/bob/EXT_DISK rw,nosuid,nodev shared:7 - ext4 /dev/sdc1 rw\n" +
        // normal mount under /run that is NOT removable (should be skipped)
        "62 28 8:49 / /run/user/1000/doc rw,nosuid shared:8 - ext4 /dev/sdd1 rw\n" +
        // network mount (nfs)
        "70 28 0:50 / /mnt/share rw,relatime shared:9 - nfs nas.local:/export rw\n";

    [TestMethod]
    public void Parse_DecodesOctalEscapesCorrectly()
    {
        IReadOnlyList<MountEntry> entries = MountInfo.Parse(SampleMountinfo);

        MountEntry usb = entries.First(e => e.Device == "/dev/sdb1");
        Assert.AreEqual("/run/media/alice/USB DRIVE", usb.MountPoint);
        Assert.AreEqual("vfat", usb.FileSystem);
    }

    [TestMethod]
    public void Real_FiltersOutPseudoAndSnapFilesystems()
    {
        IReadOnlyList<MountEntry> real = MountInfo.Real(SampleMountinfo);

        // squashfs /dev/loop0, tmpfs, proc must be skipped
        Assert.IsFalse(real.Any(m => m.FileSystem == "squashfs"));
        Assert.IsFalse(real.Any(m => m.FileSystem == "tmpfs"));
        Assert.IsFalse(real.Any(m => m.FileSystem == "proc"));
        Assert.IsFalse(real.Any(m => m.Device.StartsWith("/dev/loop")));
    }

    [TestMethod]
    public void Real_KeepsRemovableMountsUnderRunMediaAndMedia()
    {
        IReadOnlyList<MountEntry> real = MountInfo.Real(SampleMountinfo);

        Assert.IsTrue(real.Any(m => m.MountPoint == "/run/media/alice/USB DRIVE" && m.Device == "/dev/sdb1"));
        Assert.IsTrue(real.Any(m => m.MountPoint == "/media/bob/EXT_DISK" && m.Device == "/dev/sdc1"));

        // Non-removable under /run is skipped even if backed by /dev/sdd1
        Assert.IsFalse(real.Any(m => m.Device == "/dev/sdd1"));
    }

    [TestMethod]
    public void Real_DeduplicatesByDevice_KeepingShortestMountPoint()
    {
        IReadOnlyList<MountEntry> real = MountInfo.Real(SampleMountinfo);

        // /dev/nvme0n1p2 appeared twice: "/" and "/var/log".
        // Shortest mount point "/" must win.
        List<MountEntry> rootEntries = real.Where(m => m.Device == "/dev/nvme0n1p2").ToList();
        Assert.AreEqual(1, rootEntries.Count);
        Assert.AreEqual("/", rootEntries[0].MountPoint);
    }

    [TestMethod]
    public void Real_KeepsNetworkFilesystems()
    {
        IReadOnlyList<MountEntry> real = MountInfo.Real(SampleMountinfo);

        Assert.IsTrue(real.Any(m => m.MountPoint == "/mnt/share" && m.FileSystem == "nfs"));
    }

    [TestMethod]
    public void Real_WithBtrfsSubvolumes_DifferentRootsAreSeparateDisks_SameRootBindMountDeduplicated()
    {
        string btrfsMountinfo =
            // subvolume @ mounted on /
            "30 1 0:28 /@ / rw,relatime shared:1 - btrfs /dev/nvme0n1p2 rw,subvol=@\n" +
            // subvolume @home mounted on /home (same device, different root)
            "31 30 0:28 /@home /home rw,relatime shared:2 - btrfs /dev/nvme0n1p2 rw,subvol=@home\n" +
            // bind mount of @ to /mnt/bind (same device, same root /@ -> duplicate, shortest mountpoint / wins)
            "32 30 0:28 /@ /mnt/bind rw,relatime shared:1 - btrfs /dev/nvme0n1p2 rw,subvol=@\n";

        IReadOnlyList<MountEntry> real = MountInfo.Real(btrfsMountinfo);

        // Different roots (@ and @home) -> two separate entries
        // Identical (source, root) pair -> bind mount deduped, keeping shortest "/"
        Assert.AreEqual(2, real.Count);

        MountEntry rootMount = real.First(m => m.MountPoint == "/");
        Assert.AreEqual("/dev/nvme0n1p2", rootMount.Device);
        Assert.AreEqual("/@", rootMount.Root);
        Assert.AreEqual("btrfs", rootMount.FileSystem);

        MountEntry homeMount = real.First(m => m.MountPoint == "/home");
        Assert.AreEqual("/dev/nvme0n1p2", homeMount.Device);
        Assert.AreEqual("/@home", homeMount.Root);
        Assert.AreEqual("btrfs", homeMount.FileSystem);

        Assert.IsFalse(real.Any(m => m.MountPoint == "/mnt/bind"));
    }
}
