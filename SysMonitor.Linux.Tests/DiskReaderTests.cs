using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class DiskReaderTests
{
    [TestMethod]
    public void Read_TwoPartitionsOfOneDisk_ReceiveIndependentIoRates()
    {
        using var fake = new FakeRoot();

        // nvme0n1 has two partitions: nvme0n1p1 mounted on /boot/efi, nvme0n1p2 on /
        var mounts = new List<MountEntry>
        {
            new(MountPoint: "/", Device: "/dev/nvme0n1p2", FileSystem: "ext4"),
            new(MountPoint: "/boot/efi", Device: "/dev/nvme0n1p1", FileSystem: "vfat"),
        };

        // Write sysfs block device metadata
        fake.WriteFile("sys/class/block/nvme0n1/queue/rotational", "0\n");
        fake.WriteFile("sys/class/block/nvme0n1/removable", "0\n");
        fake.WriteFile("sys/class/block/nvme0n1/device/model", "Test SSD\n");
        fake.WriteFile("sys/class/block/nvme0n1/device/hwmon0/temp1_input", "41000\n");

        fake.WriteFile("sys/class/block/nvme0n1p1/partition", "1\n");
        fake.WriteFile("sys/class/block/nvme0n1p1/size", "1048576\n"); // 512 MB
        fake.WriteFile("sys/class/block/nvme0n1p2/partition", "2\n");
        fake.WriteFile("sys/class/block/nvme0n1p2/size", "100000000\n");

        // First sample diskstats:
        // nvme0n1p1: 1000 sectors read (512,000 bytes), 0 sectors written
        // nvme0n1p2: 20000 sectors read (10,240,000 bytes), 40000 sectors written (20,480,000 bytes)
        fake.WriteFile("proc/diskstats",
            " 259 0 nvme0n1 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\n" +
            " 259 1 nvme0n1p1 10 0 1000 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\n" +
            " 259 2 nvme0n1p2 100 0 20000 0 200 0 40000 0 0 0 0 0 0 0 0 0 0 0\n");

        var reader = new DiskReader(fake.Root, space: mount => (100L * 1024 * 1024 * 1024, 50L * 1024 * 1024 * 1024));

        // Sample 1 at t = 1000 ms: no rates yet
        IReadOnlyList<Disk> sample1 = reader.Read(mounts, stamp: 1000);
        Assert.AreEqual(2, sample1.Count);
        Assert.AreEqual(0.0, sample1[0].ReadMb);
        Assert.AreEqual(0.0, sample1[0].WriteMb);
        Assert.AreEqual(0.0, sample1[1].ReadMb);
        Assert.AreEqual(0.0, sample1[1].WriteMb);

        // Second sample diskstats at t = 2000 ms (1 second later):
        // nvme0n1p1: read sectors increased by 2048 (+1,048,576 bytes = 1 MB), write = 0 -> 1 MB/s read, 0 MB/s write
        // nvme0n1p2: read sectors increased by 20480 (+10,485,760 bytes = 10 MB), write sectors increased by 40960 (+20,971,520 bytes = 20 MB) -> 10 MB/s read, 20 MB/s write
        fake.WriteFile("proc/diskstats",
            " 259 0 nvme0n1 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\n" +
            " 259 1 nvme0n1p1 20 0 3048 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\n" +
            " 259 2 nvme0n1p2 200 0 40480 0 400 0 80960 0 0 0 0 0 0 0 0 0 0 0\n");

        IReadOnlyList<Disk> sample2 = reader.Read(mounts, stamp: 2000);

        Disk rootDisk = sample2.First(d => d.Letter == "/");
        Disk bootDisk = sample2.First(d => d.Letter == "/boot/efi");

        // Verify root disk got nvme0n1p2 rates: 10 MB/s read, 20 MB/s write
        Assert.AreEqual(10.0, rootDisk.ReadMb, 0.05);
        Assert.AreEqual(20.0, rootDisk.WriteMb, 0.05);

        // Verify boot disk got nvme0n1p1 rates: 1 MB/s read, 0 MB/s write (NOT the same as root disk!)
        Assert.AreEqual(1.0, bootDisk.ReadMb, 0.05);
        Assert.AreEqual(0.0, bootDisk.WriteMb, 0.05);

        // Both inherit the physical disk's temperature (41 C)
        Assert.AreEqual(41, rootDisk.Temp);
        Assert.AreEqual(41, bootDisk.Temp);
    }

    [TestMethod]
    public void Read_RemovableDrive_GetsRemovableMediaAndLabel()
    {
        using var fake = new FakeRoot();

        var mounts = new List<MountEntry>
        {
            new(MountPoint: "/run/media/alice/MY USB", Device: "/dev/sdb1", FileSystem: "vfat"),
        };

        fake.WriteFile("sys/class/block/sdb/removable", "1\n");
        fake.WriteFile("sys/class/block/sdb/queue/rotational", "0\n");
        fake.WriteFile("sys/class/block/sdb/device/model", "Flash Drive\n");

        fake.WriteFile("sys/class/block/sdb1/partition", "1\n");
        fake.WriteFile("sys/class/block/sdb1/size", "15000000\n");

        // Create label symlink in dev/disk/by-label/MY\x20USB -> ../../sdb1
        fake.CreateSymlink("dev/disk/by-label/MY\\x20USB", "../../sdb1");

        var reader = new DiskReader(fake.Root, space: _ => (8L * 1024 * 1024 * 1024, 4L * 1024 * 1024 * 1024));
        IReadOnlyList<Disk> disks = reader.Read(mounts, stamp: 1000);

        Assert.AreEqual(1, disks.Count);
        Disk usb = disks[0];
        Assert.AreEqual("Removable", usb.Media);
        Assert.AreEqual("MY USB", usb.Label);
        Assert.AreEqual("/run/media/alice/MY USB (MY USB)", usb.Title);
    }

    [TestMethod]
    public void DecodeUdev_DecodesHexEscapedCharacters()
    {
        Assert.AreEqual("MY USB DRIVE", DiskReader.DecodeUdev("MY\\x20USB\\x20DRIVE"));
        Assert.AreEqual("NoEscapeHere", DiskReader.DecodeUdev("NoEscapeHere"));
    }

    [TestMethod]
    public void Read_WithDeviceMapper_ResolvesDmDeviceAndUsesDmDiskStats_AndPhysicalProperties()
    {
        using var fake = new FakeRoot();

        var mounts = new List<MountEntry>
        {
            new(MountPoint: "/", Device: "/dev/mapper/vg-root", FileSystem: "ext4"),
        };

        // dm-0 represents vg-root and has slave partition nvme0n1p2 (symlink to its device directory)
        fake.WriteFile("sys/block/dm-0/dm/name", "vg-root\n");
        string nvmePart = fake.CreateDirectory("sys/class/block/nvme0n1p2");
        fake.CreateDirectory("sys/block/dm-0/slaves");
        fake.CreateDirectorySymlink("sys/block/dm-0/slaves/nvme0n1p2", nvmePart);

        // Physical parent disk metadata
        fake.WriteFile("sys/class/block/nvme0n1/device/model", "NVMe Slave Disk\n");
        fake.WriteFile("sys/class/block/nvme0n1/device/serial", "SERIAL123\n");
        fake.WriteFile("sys/class/block/nvme0n1/queue/rotational", "0\n");
        fake.WriteFile("sys/class/block/nvme0n1/size", "100000000\n");

        var reader = new DiskReader(fake.Root, space: _ => (100L * 1024 * 1024 * 1024, 60L * 1024 * 1024 * 1024));

        // Sample 1: baseline diskstats for dm-0
        fake.WriteFile("proc/diskstats",
            " 254       0 dm-0 100 0 2000 50 200 0 4000 100 0 50 80 0 0 0 0 0 0\n");
        IReadOnlyList<Disk> sample1 = reader.Read(mounts, stamp: 1000);
        Assert.AreEqual(1, sample1.Count);
        Assert.AreEqual(0, sample1[0].ReadMb);
        Assert.AreEqual(0, sample1[0].WriteMb);

        // Sample 2: 1 second later (stamp: 2000 ms), dm-0 counters advanced
        fake.WriteFile("proc/diskstats",
            " 254       0 dm-0 200 0 4000 70 400 0 8000 150 0 80 120 0 0 0 0 0 0\n");
        IReadOnlyList<Disk> sample2 = reader.Read(mounts, stamp: 2000);
        Assert.AreEqual(1, sample2.Count);
        Disk rootDisk = sample2[0];

        // Delta of 2000 sectors read (1,024,000 bytes) and 4000 sectors written (2,048,000 bytes) over 1s
        Assert.IsTrue(rootDisk.ReadMb > 0.9 && rootDisk.ReadMb < 1.1);
        Assert.IsTrue(rootDisk.WriteMb > 1.9 && rootDisk.WriteMb < 2.1);

        // Physical disk properties resolved from slave walk (nvme0n1p2 -> nvme0n1)
        DriveDetail? detail = rootDisk.Detail;
        Assert.IsNotNull(detail);
        PhysicalDisk? physDisk = detail.Disk;
        Assert.IsNotNull(physDisk);
        Assert.AreEqual("NVMe Slave Disk", physDisk.Model);
        Assert.AreEqual("SERIAL123", physDisk.Serial);
    }
}
