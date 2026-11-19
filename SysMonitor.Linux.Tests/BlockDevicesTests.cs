using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class BlockDevicesTests
{
    [TestMethod]
    public void NameForDevice_And_ParentDisk_ExtractExpectedTokens()
    {
        Assert.AreEqual("nvme0n1p2", BlockDevices.NameForDevice("/dev/nvme0n1p2"));
        Assert.AreEqual("nvme0n1", BlockDevices.ParentDisk("nvme0n1p2"));

        Assert.AreEqual("sda1", BlockDevices.NameForDevice("/dev/sda1"));
        Assert.AreEqual("sda", BlockDevices.ParentDisk("sda1"));

        Assert.AreEqual("vda3", BlockDevices.NameForDevice("/dev/vda3"));
        Assert.AreEqual("vda", BlockDevices.ParentDisk("vda3"));

        Assert.AreEqual("mmcblk0p1", BlockDevices.NameForDevice("/dev/mmcblk0p1"));
        Assert.AreEqual("mmcblk0", BlockDevices.ParentDisk("mmcblk0p1"));
    }

    [TestMethod]
    public void Media_DetectsSSD_HDD_And_Removable()
    {
        using var fake = new FakeRoot();

        // SSD: rotational = 0
        fake.WriteFile("sys/class/block/nvme0n1/queue/rotational", "0\n");
        fake.WriteFile("sys/class/block/nvme0n1/removable", "0\n");
        Assert.AreEqual("SSD", BlockDevices.Media(fake.Root, "nvme0n1"));

        // HDD: rotational = 1
        fake.WriteFile("sys/class/block/sda/queue/rotational", "1\n");
        fake.WriteFile("sys/class/block/sda/removable", "0\n");
        Assert.AreEqual("HDD", BlockDevices.Media(fake.Root, "sda"));

        // Removable drive
        fake.WriteFile("sys/class/block/sdb/queue/rotational", "0\n");
        fake.WriteFile("sys/class/block/sdb/removable", "1\n");
        Assert.AreEqual("Removable", BlockDevices.Media(fake.Root, "sdb"));
    }

    [TestMethod]
    public void Bus_WithoutDevicePath_DetectsNVMe_Virtual_And_UnknownForSda()
    {
        using var fake = new FakeRoot();

        // NVMe disk
        Assert.AreEqual("NVMe", BlockDevices.Bus(fake.Root, "nvme0n1"));

        // sda with no device path -> Unknown (no guessing from name)
        Assert.AreEqual("Unknown", BlockDevices.Bus(fake.Root, "sda"));

        // VirtIO
        Assert.AreEqual("Virtual", BlockDevices.Bus(fake.Root, "vda"));
    }

    [TestMethod]
    public void Bus_WithDevicePath_DetectsUSB_And_SATA()
    {
        using var fake = new FakeRoot();

        // USB drive via symlink
        string usbTarget = fake.CreateDirectory("sys/devices/pci0000:00/0000:00:14.0/usb1/1-1/1-1:1.0/host0/target0:0:0/0:0:0:0/block/sda");
        fake.CreateDirectorySymlink("sys/class/block/sda", usbTarget);
        Assert.AreEqual("USB", BlockDevices.Bus(fake.Root, "sda"));

        // SATA drive via symlink
        string sataTarget = fake.CreateDirectory("sys/devices/pci0000:00/0000:00:17.0/ata1/host1/target1:0:0/1:0:0:0/block/sdb");
        fake.CreateDirectorySymlink("sys/class/block/sdb", sataTarget);
        Assert.AreEqual("SATA", BlockDevices.Bus(fake.Root, "sdb"));
    }

    [TestMethod]
    public void ResolveDevice_ResolvesMapperNameToDmDevice()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/block/dm-0/dm/name", "rootvg-rootlv\n");

        Assert.AreEqual("dm-0", BlockDevices.ResolveDevice(fake.Root, "/dev/mapper/rootvg-rootlv"));
        Assert.AreEqual("sda1", BlockDevices.ResolveDevice(fake.Root, "/dev/sda1"));
    }

    [TestMethod]
    public void PhysicalDiskName_WalksSlavesRecursively_AndPicksFirstInSortOrder()
    {
        using var fake = new FakeRoot();

        // As on real sysfs, each slaves/<name> entry is a symlink to that
        // device's own directory, which has its own slaves/.
        // Real devices sda1 and sdb1 have their own sysfs directories:
        string sdb1 = fake.CreateDirectory("sys/class/block/sdb1");
        string sda1 = fake.CreateDirectory("sys/class/block/sda1");

        // dm-0 has two slaves: sdb1 and sda1 (each a symlink to the device directory)
        string dm0 = fake.CreateDirectory("sys/devices/virtual/block/dm-0");
        fake.CreateDirectory("sys/devices/virtual/block/dm-0/slaves");
        fake.CreateDirectorySymlink("sys/devices/virtual/block/dm-0/slaves/sdb1", sdb1);
        fake.CreateDirectorySymlink("sys/devices/virtual/block/dm-0/slaves/sda1", sda1);
        fake.CreateDirectorySymlink("sys/block/dm-0", dm0);

        // dm-1 is stacked on dm-0
        string dm1 = fake.CreateDirectory("sys/devices/virtual/block/dm-1");
        fake.CreateDirectory("sys/devices/virtual/block/dm-1/slaves");
        fake.CreateDirectorySymlink("sys/devices/virtual/block/dm-1/slaves/dm-0", dm0);
        fake.CreateDirectorySymlink("sys/block/dm-1", dm1);

        // Recursive walk finds non-virtual disks sda1 and sdb1, picks sda1 by sort order,
        // and resolves to parent physical disk "sda"
        Assert.AreEqual("sda", BlockDevices.PhysicalDiskName(fake.Root, "dm-1"));
        Assert.AreEqual("sda", BlockDevices.PhysicalDiskName(fake.Root, "dm-0"));
    }

    [TestMethod]
    public void PhysicalDiskName_WhenOnlyVirtualLeaves_ReturnsEmpty()
    {
        using var fake = new FakeRoot();

        // dm-1 is a virtual device with empty slaves/
        string dm1 = fake.CreateDirectory("sys/devices/virtual/block/dm-1");
        fake.CreateDirectory("sys/devices/virtual/block/dm-1/slaves");
        fake.CreateDirectorySymlink("sys/block/dm-1", dm1);

        // dm-0 has slave dm-1 (a symlink to dm-1's directory)
        string dm0 = fake.CreateDirectory("sys/devices/virtual/block/dm-0");
        fake.CreateDirectory("sys/devices/virtual/block/dm-0/slaves");
        fake.CreateDirectorySymlink("sys/devices/virtual/block/dm-0/slaves/dm-1", dm1);
        fake.CreateDirectorySymlink("sys/block/dm-0", dm0);

        // A stack whose only leaves are virtual (dm-1) has no physical disk at all
        Assert.AreEqual(string.Empty, BlockDevices.PhysicalDiskName(fake.Root, "dm-0"));
        Assert.AreEqual(string.Empty, BlockDevices.PhysicalDiskName(fake.Root, "dm-1"));
    }

    [TestMethod]
    public void ReadPhysical_PopulatesModelSerialAndPartitions()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/block/nvme0n1/device/model", "SAMSUNG MZVL2512HCJQ-00B00\n");
        fake.WriteFile("sys/class/block/nvme0n1/device/serial", "S5R2NX0R123456\n");
        fake.WriteFile("sys/class/block/nvme0n1/device/firmware_rev", "HPS8N4Q\n");
        fake.WriteFile("sys/class/block/nvme0n1/size", "1000215216\n"); // ~512 GB in 512b sectors
        fake.WriteFile("sys/class/block/nvme0n1/queue/rotational", "0\n");

        // Partitions under nvme0n1
        fake.WriteFile("sys/class/block/nvme0n1/nvme0n1p1/partition", "1\n");
        fake.WriteFile("sys/class/block/nvme0n1/nvme0n1p1/size", "1048576\n"); // 512 MB
        fake.WriteFile("sys/class/block/nvme0n1/nvme0n1p2/partition", "2\n");
        fake.WriteFile("sys/class/block/nvme0n1/nvme0n1p2/size", "999166640\n");

        // Real sysfs exposes each partition also as /sys/class/block/<partition>/
        fake.WriteFile("sys/class/block/nvme0n1p1/partition", "1\n");
        fake.WriteFile("sys/class/block/nvme0n1p1/size", "1048576\n");
        fake.WriteFile("sys/class/block/nvme0n1p2/partition", "2\n");
        fake.WriteFile("sys/class/block/nvme0n1p2/size", "999166640\n");

        PhysicalDisk disk = BlockDevices.ReadPhysical(fake.Root, "nvme0n1", number: 0);

        Assert.AreEqual(0, disk.Number);
        Assert.AreEqual("SAMSUNG MZVL2512HCJQ-00B00", disk.Model);
        Assert.AreEqual("S5R2NX0R123456", disk.Serial);
        Assert.AreEqual("HPS8N4Q", disk.Firmware);
        Assert.AreEqual("SSD", disk.Media);
        Assert.AreEqual("NVMe", disk.Bus);
        Assert.AreEqual(2, disk.PartitionCount);
        Assert.AreEqual(1000215216L * 512, disk.Bytes);
        Assert.AreEqual((1048576L + 999166640L) * 512, disk.AllocatedBytes);
    }
}
