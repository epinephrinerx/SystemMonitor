using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class DiskPropertiesTests
{
    [TestMethod]
    public void Kind_IdentifiesNvmeHddSsdAndUnreadable()
    {
        using var fake = new FakeRoot();

        // nvme0n1 is NVMe by name regardless of rotational flag
        Assert.AreEqual("NVMe", BlockDevices.Kind(fake.Root, "nvme0n1"));

        // sda with rotational = 1 is HDD
        fake.WriteFile("sys/class/block/sda/queue/rotational", "1\n");
        Assert.AreEqual("HDD", BlockDevices.Kind(fake.Root, "sda"));

        // sdb with rotational = 0 is SSD
        fake.WriteFile("sys/class/block/sdb/queue/rotational", "0\n");
        Assert.AreEqual("SSD", BlockDevices.Kind(fake.Root, "sdb"));

        // sdc with missing rotational file is empty string
        Assert.AreEqual(string.Empty, BlockDevices.Kind(fake.Root, "sdc"));

        // Unsafe name is empty string
        Assert.AreEqual(string.Empty, BlockDevices.Kind(fake.Root, "../sda"));
    }

    [TestMethod]
    public void RemovableFlag_And_Writable_ParseSysfsFlags()
    {
        using var fake = new FakeRoot();

        // Removable: 1 -> true, 0 -> false, missing -> null
        fake.WriteFile("sys/class/block/sda/removable", "1\n");
        fake.WriteFile("sys/class/block/sdb/removable", "0\n");
        Assert.AreEqual(true, BlockDevices.RemovableFlag(fake.Root, "sda"));
        Assert.AreEqual(false, BlockDevices.RemovableFlag(fake.Root, "sdb"));
        Assert.IsNull(BlockDevices.RemovableFlag(fake.Root, "sdc"));

        // Writable: 0 -> true (not ro), 1 -> false (ro), missing -> null
        fake.WriteFile("sys/class/block/sda/ro", "0\n");
        fake.WriteFile("sys/class/block/sdb/ro", "1\n");
        Assert.AreEqual(true, BlockDevices.Writable(fake.Root, "sda"));
        Assert.AreEqual(false, BlockDevices.Writable(fake.Root, "sdb"));
        Assert.IsNull(BlockDevices.Writable(fake.Root, "sdc"));
    }

    [TestMethod]
    public void TrimVersion_TrimsTrailingZerosAfterDecimal()
    {
        Assert.AreEqual("2.1", BlockDevices.TrimVersion("2.10"));
        Assert.AreEqual("2", BlockDevices.TrimVersion("2.00"));
        Assert.AreEqual("3", BlockDevices.TrimVersion("3.00"));
        Assert.AreEqual("3.1", BlockDevices.TrimVersion("3.10"));
        Assert.AreEqual("1.1", BlockDevices.TrimVersion("1.10"));
        Assert.AreEqual("2", BlockDevices.TrimVersion("2.0"));
        Assert.AreEqual("3", BlockDevices.TrimVersion("3"));
        Assert.AreEqual("2.1", BlockDevices.TrimVersion(" 2.10 "));
    }

    [TestMethod]
    public void Link_NvmeDrive_ReportsPcieLink()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/block/nvme0n1/device/current_link_speed", "8.0 GT/s PCIe\n");
        fake.WriteFile("sys/class/block/nvme0n1/device/current_link_width", "4\n");

        Assert.AreEqual("PCIe 3.0 ×4", BlockDevices.Link(fake.Root, "nvme0n1"));
    }

    [TestMethod]
    public void Link_UsbDrive_WalksAncestryAndReportsUsbLink()
    {
        using var fake = new FakeRoot();

        // Ancestor directory with version and speed
        fake.CreateDirectory("sys/devices/pci0000:00/0000:00:14.0/usb1/1-1");
        fake.WriteFile("sys/devices/pci0000:00/0000:00:14.0/usb1/1-1/version", " 2.10\n");
        fake.WriteFile("sys/devices/pci0000:00/0000:00:14.0/usb1/1-1/speed", "480\n");

        // Resolved path of sda
        string sdaTarget = fake.CreateDirectory(
            "sys/devices/pci0000:00/0000:00:14.0/usb1/1-1/1-1:1.0/host0/target0:0:0/0:0:0:0/block/sda");
        fake.CreateDirectorySymlink("sys/class/block/sda", sdaTarget);

        Assert.AreEqual("USB 2.1 (480 Mb/s)", BlockDevices.Link(fake.Root, "sda"));

        // Test with USB 3.0 (version 3.00, speed 5000)
        fake.CreateDirectory("sys/devices/pci0000:00/0000:00:14.0/usb2/2-1");
        fake.WriteFile("sys/devices/pci0000:00/0000:00:14.0/usb2/2-1/version", "3.00\n");
        fake.WriteFile("sys/devices/pci0000:00/0000:00:14.0/usb2/2-1/speed", "5000\n");

        string sdbTarget = fake.CreateDirectory(
            "sys/devices/pci0000:00/0000:00:14.0/usb2/2-1/2-1:1.0/host1/target1:0:0/1:0:0:0/block/sdb");
        fake.CreateDirectorySymlink("sys/class/block/sdb", sdbTarget);

        Assert.AreEqual("USB 3 (5000 Mb/s)", BlockDevices.Link(fake.Root, "sdb"));
    }

    [TestMethod]
    public void DiskReader_ActivityPercent_FirstSampleNull_CalculatedOnSecondSample_AndClamped()
    {
        using var fake = new FakeRoot();

        var mounts = new List<MountEntry>
        {
            new(MountPoint: "/", Device: "/dev/nvme0n1p1", FileSystem: "ext4"),
        };

        fake.WriteFile("sys/class/block/nvme0n1/queue/rotational", "0\n");
        fake.WriteFile("sys/class/block/nvme0n1/removable", "0\n");
        fake.WriteFile("sys/class/block/nvme0n1/ro", "0\n");
        fake.WriteFile("sys/class/block/nvme0n1p1/partition", "1\n");
        fake.WriteFile("sys/class/block/nvme0n1p1/size", "1000000\n");

        // Sample 1: io_ticks = 100 ms at t = 1000 ms
        fake.WriteFile("proc/diskstats",
            " 259 0 nvme0n1 100 0 2000 50 200 0 4000 100 0 100 200 0 0 0 0 0 0\n" +
            " 259 1 nvme0n1p1 100 0 2000 50 200 0 4000 100 0 100 200 0 0 0 0 0 0\n");

        var reader = new DiskReader(fake.Root, space: _ => (100L * 1024 * 1024 * 1024, 50L * 1024 * 1024 * 1024));

        IReadOnlyList<Disk> sample1 = reader.Read(mounts, stamp: 1000);
        Assert.AreEqual(1, sample1.Count);
        PhysicalDisk? physDisk1 = sample1[0].Detail?.Disk;
        Assert.IsNotNull(physDisk1);
        Assert.IsNull(physDisk1.ActivityPercent); // First sample has no baseline

        // Sample 2 at t = 2000 ms (Δt = 1000 ms): io_ticks advances by 250 ms (100 -> 350)
        fake.WriteFile("proc/diskstats",
            " 259 0 nvme0n1 200 0 4000 75 400 0 8000 150 0 350 400 0 0 0 0 0 0\n" +
            " 259 1 nvme0n1p1 200 0 4000 75 400 0 8000 150 0 350 400 0 0 0 0 0 0\n");

        IReadOnlyList<Disk> sample2 = reader.Read(mounts, stamp: 2000);
        PhysicalDisk? physDisk2 = sample2[0].Detail?.Disk;
        Assert.IsNotNull(physDisk2);
        Assert.IsNotNull(physDisk2.ActivityPercent);
        Assert.AreEqual(25.0, physDisk2.ActivityPercent.Value, 0.01);

        // Sample 3 at t = 3000 ms (Δt = 1000 ms): io_ticks advances by 1500 ms (350 -> 1850), clamped at 100
        fake.WriteFile("proc/diskstats",
            " 259 0 nvme0n1 300 0 6000 100 600 0 12000 200 0 1850 600 0 0 0 0 0 0\n" +
            " 259 1 nvme0n1p1 300 0 6000 100 600 0 12000 200 0 1850 600 0 0 0 0 0 0\n");

        IReadOnlyList<Disk> sample3 = reader.Read(mounts, stamp: 3000);
        PhysicalDisk? physDisk3 = sample3[0].Detail?.Disk;
        Assert.IsNotNull(physDisk3);
        Assert.IsNotNull(physDisk3.ActivityPercent);
        Assert.AreEqual(100.0, physDisk3.ActivityPercent.Value, 0.01);
    }

    [TestMethod]
    public void DiskReader_ReadWriteRatesAndLifetimeTotals_CalculatedFromSectors()
    {
        using var fake = new FakeRoot();

        var mounts = new List<MountEntry>
        {
            new(MountPoint: "/", Device: "/dev/sda1", FileSystem: "ext4"),
        };

        fake.WriteFile("sys/class/block/sda/queue/rotational", "1\n");
        fake.WriteFile("sys/class/block/sda/removable", "0\n");
        fake.WriteFile("sys/class/block/sda/ro", "0\n");
        fake.WriteFile("sys/class/block/sda1/partition", "1\n");
        fake.WriteFile("sys/class/block/sda1/size", "1000000\n");

        // Sample 1 at t = 1000 ms:
        // sectorsRead = 2000 -> 2000 * 512 = 1,024,000 bytes
        // sectorsWritten = 4000 -> 4000 * 512 = 2,048,000 bytes
        fake.WriteFile("proc/diskstats",
            " 8 0 sda 10 0 2000 50 20 0 4000 100 0 50 150 0 0 0 0 0 0\n" +
            " 8 1 sda1 10 0 2000 50 20 0 4000 100 0 50 150 0 0 0 0 0 0\n");

        var reader = new DiskReader(fake.Root, space: _ => (100L * 1024 * 1024 * 1024, 50L * 1024 * 1024 * 1024));

        IReadOnlyList<Disk> sample1 = reader.Read(mounts, stamp: 1000);
        PhysicalDisk? disk1 = sample1[0].Detail?.Disk;
        Assert.IsNotNull(disk1);
        Assert.AreEqual(1024000L, disk1.TotalReadBytes);
        Assert.AreEqual(2048000L, disk1.TotalWrittenBytes);
        Assert.AreEqual(0.0, disk1.ReadBytesPerSec);
        Assert.AreEqual(0.0, disk1.WriteBytesPerSec);

        // Sample 2 at t = 2000 ms (1s elapsed):
        // sectorsRead increases by 2000 (+1,024,000 bytes) -> 1,024,000 B/s
        // sectorsWritten increases by 4000 (+2,048,000 bytes) -> 2,048,000 B/s
        fake.WriteFile("proc/diskstats",
            " 8 0 sda 20 0 4000 75 40 0 8000 200 0 100 300 0 0 0 0 0 0\n" +
            " 8 1 sda1 20 0 4000 75 40 0 8000 200 0 100 300 0 0 0 0 0 0\n");

        IReadOnlyList<Disk> sample2 = reader.Read(mounts, stamp: 2000);
        PhysicalDisk? disk2 = sample2[0].Detail?.Disk;
        Assert.IsNotNull(disk2);
        Assert.AreEqual(2048000L, disk2.TotalReadBytes);
        Assert.AreEqual(4096000L, disk2.TotalWrittenBytes);
        Assert.AreEqual(1024000.0, disk2.ReadBytesPerSec, 1.0);
        Assert.AreEqual(2048000.0, disk2.WriteBytesPerSec, 1.0);
    }

    [TestMethod]
    public void DiskReader_PopulatesPhysicalDiskProperties()
    {
        using var fake = new FakeRoot();

        var mounts = new List<MountEntry>
        {
            new(MountPoint: "/", Device: "/dev/nvme0n1p2", FileSystem: "ext4"),
        };

        fake.WriteFile("sys/class/block/nvme0n1/queue/rotational", "0\n");
        fake.WriteFile("sys/class/block/nvme0n1/removable", "0\n");
        fake.WriteFile("sys/class/block/nvme0n1/ro", "0\n");
        fake.WriteFile("sys/class/block/nvme0n1/device/current_link_speed", "8.0 GT/s PCIe\n");
        fake.WriteFile("sys/class/block/nvme0n1/device/current_link_width", "4\n");
        fake.WriteFile("sys/class/block/nvme0n1p2/partition", "2\n");
        fake.WriteFile("sys/class/block/nvme0n1p2/size", "1000000\n");

        fake.WriteFile("proc/diskstats",
            " 259 0 nvme0n1 10 0 1000 50 20 0 2000 100 0 50 150 0 0 0 0 0 0\n" +
            " 259 2 nvme0n1p2 10 0 1000 50 20 0 2000 100 0 50 150 0 0 0 0 0 0\n");

        var reader = new DiskReader(fake.Root, space: _ => (100L * 1024 * 1024 * 1024, 50L * 1024 * 1024 * 1024));

        IReadOnlyList<Disk> disks = reader.Read(mounts, stamp: 1000);
        Assert.AreEqual(1, disks.Count);

        PhysicalDisk? phys = disks[0].Detail?.Disk;
        Assert.IsNotNull(phys);
        Assert.AreEqual("nvme0n1", phys.DeviceName);
        Assert.AreEqual("NVMe", phys.Kind);
        Assert.AreEqual(false, phys.Removable);
        Assert.AreEqual(true, phys.Writable);
        Assert.AreEqual("PCIe 3.0 ×4", phys.Link);
    }

    [TestMethod]
    public void DiskStats_Parse_ElevenFields_SetsIoMsNull()
    {
        const string stats11 = " 8 0 sda 10 0 2000 50 20 0 4000 100\n";
        IReadOnlyList<DiskCounters> parsed = DiskStats.Parse(stats11);

        Assert.AreEqual(1, parsed.Count);
        Assert.AreEqual("sda", parsed[0].Name);
        Assert.AreEqual(2000L * 512, parsed[0].BytesRead);
        Assert.AreEqual(4000L * 512, parsed[0].BytesWritten);
        Assert.IsNull(parsed[0].IoMs);
    }

    [TestMethod]
    public void DiskStats_Parse_ThirteenOrMoreFields_ParsesIoMs()
    {
        const string stats13 = " 8 0 sda 10 0 2000 50 20 0 4000 100 0 250 500\n";
        IReadOnlyList<DiskCounters> parsed = DiskStats.Parse(stats13);

        Assert.AreEqual(1, parsed.Count);
        Assert.AreEqual("sda", parsed[0].Name);
        Assert.AreEqual(250L, parsed[0].IoMs);

        // Negative ticks -> null
        const string statsNegative = " 8 0 sda 10 0 2000 50 20 0 4000 100 0 -1 500\n";
        IReadOnlyList<DiskCounters> parsedNeg = DiskStats.Parse(statsNegative);
        Assert.IsNull(parsedNeg[0].IoMs);
    }

    [TestMethod]
    public void DiskReader_ActivityPercent_ElevenFieldsWithoutIoTicks_RemainsNullAfterTwoSamples()
    {
        using var fake = new FakeRoot();

        var mounts = new List<MountEntry>
        {
            new(MountPoint: "/", Device: "/dev/sda1", FileSystem: "ext4"),
        };

        fake.WriteFile("sys/class/block/sda/queue/rotational", "1\n");
        fake.WriteFile("sys/class/block/sda/removable", "0\n");
        fake.WriteFile("sys/class/block/sda/ro", "0\n");
        fake.WriteFile("sys/class/block/sda1/partition", "1\n");
        fake.WriteFile("sys/class/block/sda1/size", "1000000\n");

        // Sample 1 at t = 1000 ms (only 11 fields):
        fake.WriteFile("proc/diskstats",
            " 8 0 sda 10 0 2000 50 20 0 4000 100\n" +
            " 8 1 sda1 10 0 2000 50 20 0 4000 100\n");

        var reader = new DiskReader(fake.Root, space: _ => (100L * 1024 * 1024 * 1024, 50L * 1024 * 1024 * 1024));

        IReadOnlyList<Disk> sample1 = reader.Read(mounts, stamp: 1000);
        PhysicalDisk? physDisk1 = sample1[0].Detail?.Disk;
        Assert.IsNotNull(physDisk1);
        Assert.IsNull(physDisk1.ActivityPercent);

        // Sample 2 at t = 2000 ms (still only 11 fields):
        fake.WriteFile("proc/diskstats",
            " 8 0 sda 20 0 4000 75 40 0 8000 200\n" +
            " 8 1 sda1 20 0 4000 75 40 0 8000 200\n");

        IReadOnlyList<Disk> sample2 = reader.Read(mounts, stamp: 2000);
        PhysicalDisk? physDisk2 = sample2[0].Detail?.Disk;
        Assert.IsNotNull(physDisk2);
        Assert.IsNull(physDisk2.ActivityPercent);
    }

    [TestMethod]
    public void DiskReader_ActivityPercent_CounterGoesBackwards_SetsActivityPercentNull()
    {
        using var fake = new FakeRoot();

        var mounts = new List<MountEntry>
        {
            new(MountPoint: "/", Device: "/dev/sda1", FileSystem: "ext4"),
        };

        fake.WriteFile("sys/class/block/sda/queue/rotational", "1\n");
        fake.WriteFile("sys/class/block/sda/removable", "0\n");
        fake.WriteFile("sys/class/block/sda/ro", "0\n");
        fake.WriteFile("sys/class/block/sda1/partition", "1\n");
        fake.WriteFile("sys/class/block/sda1/size", "1000000\n");

        // Sample 1 at t = 1000 ms: io_ticks = 500
        fake.WriteFile("proc/diskstats",
            " 8 0 sda 10 0 2000 50 20 0 4000 100 0 500 150 0 0 0 0 0 0\n" +
            " 8 1 sda1 10 0 2000 50 20 0 4000 100 0 500 150 0 0 0 0 0 0\n");

        var reader = new DiskReader(fake.Root, space: _ => (100L * 1024 * 1024 * 1024, 50L * 1024 * 1024 * 1024));

        IReadOnlyList<Disk> sample1 = reader.Read(mounts, stamp: 1000);
        PhysicalDisk? physDisk1 = sample1[0].Detail?.Disk;
        Assert.IsNotNull(physDisk1);
        Assert.IsNull(physDisk1.ActivityPercent);

        // Sample 2 at t = 2000 ms: io_ticks = 200 (device reset, counter went backwards)
        fake.WriteFile("proc/diskstats",
            " 8 0 sda 20 0 4000 75 40 0 8000 200 0 200 300 0 0 0 0 0 0\n" +
            " 8 1 sda1 20 0 4000 75 40 0 8000 200 0 200 300 0 0 0 0 0 0\n");

        IReadOnlyList<Disk> sample2 = reader.Read(mounts, stamp: 2000);
        PhysicalDisk? physDisk2 = sample2[0].Detail?.Disk;
        Assert.IsNotNull(physDisk2);
        Assert.IsNull(physDisk2.ActivityPercent);

        // Sample 3 at t = 3000 ms: io_ticks = 300 (+100 ms in 1000 ms elapsed -> 10%)
        fake.WriteFile("proc/diskstats",
            " 8 0 sda 30 0 6000 100 60 0 12000 300 0 300 450 0 0 0 0 0 0\n" +
            " 8 1 sda1 30 0 6000 100 60 0 12000 300 0 300 450 0 0 0 0 0 0\n");

        IReadOnlyList<Disk> sample3 = reader.Read(mounts, stamp: 3000);
        PhysicalDisk? physDisk3 = sample3[0].Detail?.Disk;
        Assert.IsNotNull(physDisk3);
        Assert.IsNotNull(physDisk3.ActivityPercent);
        Assert.AreEqual(10.0, physDisk3.ActivityPercent.Value, 0.01);
    }
}
