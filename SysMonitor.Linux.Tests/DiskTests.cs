using System;
using System.Globalization;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public class DiskTests
{
    private static string Mount(string point, string device, string fs = "ext4") =>
        "36 35 8:1 / " + point + " rw,noatime - " + fs + " " + device + " rw";

    private static void WithRoot(Action<string> body)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(root);
            body(root);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void Put(string root, string disk, string relative, string content)
    {
        string path = Path.Combine(root, "block", disk, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void CheckDefaultDisk(PhysicalDisk disk, int number)
    {
        // oracle: invalid/missing disk returns Number as supplied, empty strings, numeric zeros, and default Healthy=true.
        Assert.AreEqual(number, disk.Number);
        Assert.AreEqual(0L, disk.Bytes);
        Assert.AreEqual("", disk.Model);
        Assert.AreEqual("", disk.Serial);
        Assert.AreEqual("", disk.Firmware);
        Assert.AreEqual("", disk.Bus);
        Assert.AreEqual("", disk.Interface);
        Assert.AreEqual("", disk.Media);
        Assert.AreEqual("", disk.PartitionStyle);
        Assert.AreEqual(0, disk.PartitionCount);
        Assert.AreEqual(0, disk.Rpm);
        Assert.IsTrue(disk.Healthy);
    }

    // For path guards, the exact unguarded target holds data that changes observable results.
    private static void CheckPlantedGuard(string invalidDisk, Func<string, string> targetPath)
    {
        WithRoot(root =>
        {
            string sysRoot = Path.Combine(root, "sys");
            Directory.CreateDirectory(Path.Combine(sysRoot, "block"));
            string target = targetPath(sysRoot);
            Directory.CreateDirectory(Path.Combine(target, "device"));
            File.WriteAllText(Path.Combine(target, "size"), "7");
            File.WriteAllText(Path.Combine(target, "device", "model"), "GUARD_CANARY");
            string unguarded = Path.Combine(sysRoot, "block", invalidDisk);
            // oracle: unguarded <sysRoot>/block/<disk>/size and device/model really resolve to the planted files.
            Assert.AreEqual("7", File.ReadAllText(Path.Combine(unguarded, "size")));
            Assert.AreEqual("GUARD_CANARY", File.ReadAllText(Path.Combine(unguarded, "device", "model")));
            CheckDefaultDisk(BlockDevice.Read(sysRoot, invalidDisk, 17), 17);
        });
    }

    [TestMethod]
    public void Mounts_SeparatorHandlesOptionalFieldsAndRootIsNotMountPoint()
    {
        var rows = MountInfo.Parse("36 35 98:0 /root /mnt/point rw,noatime master:1 shared:2 - ext3 /dev/root rw,errors=continue");
        // oracle: field 5 is mountpoint; fstype/source follow '-', independent of optional-field count.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new MountEntry("/mnt/point", "/dev/root", "ext3"), rows[0]);
    }

    [TestMethod]
    public void Mounts_NoOptionalFieldsStillParses()
    {
        var rows = MountInfo.Parse(Mount("/", "/dev/sda1", "xfs"));
        // oracle: a valid record needs no optional fields; root mountpoint '/' is retained.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new MountEntry("/", "/dev/sda1", "xfs"), rows[0]);
    }

    [TestMethod]
    public void Mounts_NonDeviceSourcesAreExcluded()
    {
        var rows = MountInfo.Parse(Mount("/run", "tmpfs", "tmpfs") + "\n" + Mount("/net", "server:/share", "nfs") + "\n" + Mount("/fake", "/device/sda1"));
        // oracle: none of tmpfs, server:/share, /device/sda1 starts with the exact '/dev/' prefix.
        Assert.AreEqual(0, rows.Count);
    }

    [TestMethod]
    public void Mounts_LoopPrefixIsExcludedEvenForLongerNames()
    {
        var rows = MountInfo.Parse(Mount("/a", "/dev/loop0") + "\n" + Mount("/b", "/dev/loop12p1") + "\n" + Mount("/c", "/dev/loopish"));
        // oracle: every source beginning '/dev/loop' is excluded, not just a specific loop number.
        Assert.AreEqual(0, rows.Count);
    }

    [TestMethod]
    public void Mounts_SquashfsIsExcludedForNonLoopDevice()
    {
        // oracle: squashfs is excluded even when its source is an otherwise eligible /dev/sda1.
        Assert.AreEqual(0, MountInfo.Parse(Mount("/snap", "/dev/sda1", "squashfs")).Count);
    }

    [TestMethod]
    public void Mounts_MapperDeviceIsRetainedAtMountParsingStage()
    {
        var rows = MountInfo.Parse(Mount("/data", "/dev/mapper/vg-root"));
        // oracle: /dev/mapper/vg-root starts /dev/, is not loop, and uses ext4; parent resolution is a separate API.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new MountEntry("/data", "/dev/mapper/vg-root", "ext4"), rows[0]);
    }

    [TestMethod]
    public void Mounts_DecodeSpaceTabNewlineAndBackslashEscapes()
    {
        var rows = MountInfo.Parse(Mount(@"/a\040b\011c\012d\134e", "/dev/sda1"));
        // oracle: \040 = space, \011 = tab, \012 = newline, \134 = backslash, in the mountpoint only.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("/a b\tc\nd\\e", rows[0].MountPoint);
    }

    [TestMethod]
    public void Mounts_DecodeGeneralThreeDigitOctal()
    {
        var rows = MountInfo.Parse(Mount(@"/\141\142", "/dev/sda1"));
        // oracle: octal 141 = decimal 97 ('a'); octal 142 = decimal 98 ('b').
        Assert.AreEqual("/ab", rows[0].MountPoint);
    }

    [TestMethod]
    public void Mounts_LaterShorterDuplicateReplacesEntryWithoutReorderingDevices()
    {
        var rows = MountInfo.Parse(Mount("/very/long/path", "/dev/sda1") + "\n" + Mount("/middle", "/dev/sdb1") + "\n" + Mount("/x", "/dev/sda1", "btrfs"));
        // oracle: later /x is shorter for sda1; device first-appearance order remains sda1,sdb1; replacement carries btrfs.
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(new MountEntry("/x", "/dev/sda1", "btrfs"), rows[0]);
        Assert.AreEqual(new MountEntry("/middle", "/dev/sdb1", "ext4"), rows[1]);
    }

    [TestMethod]
    public void Mounts_EqualLengthDuplicateKeepsFirstEntry()
    {
        var rows = MountInfo.Parse(Mount("/aa", "/dev/sda1", "ext4") + "\n" + Mount("/bb", "/dev/sda1", "xfs"));
        // oracle: /aa and /bb both have length 3; the first complete entry wins the tie.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new MountEntry("/aa", "/dev/sda1", "ext4"), rows[0]);
    }

    [TestMethod]
    public void Mounts_DuplicateLengthIsMeasuredAfterOctalDecoding()
    {
        var rows = MountInfo.Parse(Mount("/wxyz", "/dev/sda1") + "\n" + Mount(@"/a\040b", "/dev/sda1"));
        // oracle: later decoded '/a b' length 4 beats '/wxyz' length 5, though its encoded length is 7.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("/a b", rows[0].MountPoint);
    }

    [TestMethod]
    public void Mounts_DifferentDevicesSharingMountPointRemainDistinct()
    {
        var rows = MountInfo.Parse(Mount("/data", "/dev/sda1") + "\n" + Mount("/data", "/dev/sdb1"));
        // oracle: deduplication key is Device, not MountPoint; both distinct devices survive.
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("/dev/sda1", rows[0].Device);
        Assert.AreEqual("/dev/sdb1", rows[1].Device);
    }

    [TestMethod]
    public void Mounts_MalformedRecordsDoNotHideLaterValidRecord()
    {
        var rows = MountInfo.Parse("nonsense\n36 35 8:1 / /bad rw ext4 /dev/sda1 rw\n36 35 8:1 / /bad rw -\n36 35 8:1 / /bad rw - ext4\n" + Mount("/ok", "/dev/sdc1"));
        // oracle: truncated or separator-free records are skipped; the only complete valid record is /dev/sdc1.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new MountEntry("/ok", "/dev/sdc1", "ext4"), rows[0]);
    }

    [TestMethod]
    public void Mounts_EmptyInputReturnsEmpty()
    {
        // oracle: empty input has no mount records.
        Assert.AreEqual(0, MountInfo.Parse("").Count);
    }

    [TestMethod]
    public void Mounts_NullInputReturnsEmpty()
    {
        // oracle: null malformed input never throws and has no mount records.
        Assert.AreEqual(0, MountInfo.Parse(null!).Count);
    }

    [TestMethod]
    public void Parent_ScsiPartitionAcceptsDevPrefixAndBareName()
    {
        // oracle: sda1 is partition 1 of sda, with or without /dev/.
        Assert.AreEqual("sda", BlockDevice.ParentDisk("/dev/sda1"));
        Assert.AreEqual("sda", BlockDevice.ParentDisk("sda1"));
        Assert.AreEqual(1, BlockDevice.PartitionNumber("/dev/sda1"));
        Assert.AreEqual(1, BlockDevice.PartitionNumber("sda1"));
    }

    [TestMethod]
    public void Parent_ScsiWholeDiskHasNoPartition()
    {
        // oracle: whole disk sda maps to itself and partition number zero.
        Assert.AreEqual("sda", BlockDevice.ParentDisk("sda"));
        Assert.AreEqual("sda", BlockDevice.ParentDisk("/dev/sda"));
        Assert.AreEqual(0, BlockDevice.PartitionNumber("sda"));
        Assert.AreEqual(0, BlockDevice.PartitionNumber("/dev/sda"));
    }

    [TestMethod]
    public void Parent_NvmePartitionPreservesNamespaceDigits()
    {
        // oracle: nvme0n1p2 is partition 2 of nvme0n1; namespace digits belong to the disk.
        Assert.AreEqual("nvme0n1", BlockDevice.ParentDisk("/dev/nvme0n1p2"));
        Assert.AreEqual("nvme0n1", BlockDevice.ParentDisk("nvme0n1p2"));
        Assert.AreEqual(2, BlockDevice.PartitionNumber("/dev/nvme0n1p2"));
        Assert.AreEqual(2, BlockDevice.PartitionNumber("nvme0n1p2"));
    }

    [TestMethod]
    public void Parent_NvmeWholeDiskIsNotPartitionOne()
    {
        // oracle: nvme0n1 has no p<partition> suffix, so it is a whole disk.
        Assert.AreEqual("nvme0n1", BlockDevice.ParentDisk("/dev/nvme0n1"));
        Assert.AreEqual("nvme0n1", BlockDevice.ParentDisk("nvme0n1"));
        Assert.AreEqual(0, BlockDevice.PartitionNumber("/dev/nvme0n1"));
        Assert.AreEqual(0, BlockDevice.PartitionNumber("nvme0n1"));
    }

    [TestMethod]
    public void Parent_MmcPartitionPreservesDiskDigits()
    {
        // oracle: mmcblk0p1 is partition 1 of mmcblk0, with or without /dev/.
        Assert.AreEqual("mmcblk0", BlockDevice.ParentDisk("/dev/mmcblk0p1"));
        Assert.AreEqual("mmcblk0", BlockDevice.ParentDisk("mmcblk0p1"));
        Assert.AreEqual(1, BlockDevice.PartitionNumber("/dev/mmcblk0p1"));
        Assert.AreEqual(1, BlockDevice.PartitionNumber("mmcblk0p1"));
    }

    [TestMethod]
    public void Parent_MmcWholeDiskHasNoPartition()
    {
        // oracle: mmcblk0 has no p<partition> suffix; its trailing 0 is part of its disk name.
        Assert.AreEqual("mmcblk0", BlockDevice.ParentDisk("/dev/mmcblk0"));
        Assert.AreEqual("mmcblk0", BlockDevice.ParentDisk("mmcblk0"));
        Assert.AreEqual(0, BlockDevice.PartitionNumber("/dev/mmcblk0"));
        Assert.AreEqual(0, BlockDevice.PartitionNumber("mmcblk0"));
    }

    [TestMethod]
    public void Parent_MapperAnywhereIsUnknown()
    {
        // oracle: anything containing mapper/ is unknown, including the middle of a string and a bare mapper path.
        Assert.AreEqual("", BlockDevice.ParentDisk("/dev/mapper/vg-root"));
        Assert.AreEqual(0, BlockDevice.PartitionNumber("/dev/mapper/vg-root"));
        Assert.AreEqual("", BlockDevice.ParentDisk("mapper/sda1"));
        Assert.AreEqual(0, BlockDevice.PartitionNumber("mapper/sda1"));
        Assert.AreEqual("", BlockDevice.ParentDisk("prefixmapper/sda1"));
        Assert.AreEqual(0, BlockDevice.PartitionNumber("prefixmapper/sda1"));
    }

    [TestMethod]
    public void Parent_OverflowingPartitionKeepsParentButReturnsZeroNumber()
    {
        // oracle: 99999999999 exceeds int.MaxValue; parent stripping still returns sda.
        Assert.AreEqual("sda", BlockDevice.ParentDisk("sda99999999999"));
        Assert.AreEqual(0, BlockDevice.PartitionNumber("sda99999999999"));
    }

    [TestMethod]
    public void Parent_IntMaximumPartitionIsAccepted()
    {
        // oracle: 2147483647 = int.MaxValue, so this partition suffix fits exactly.
        Assert.AreEqual("sda", BlockDevice.ParentDisk("sda2147483647"));
        Assert.AreEqual(2147483647, BlockDevice.PartitionNumber("sda2147483647"));
    }

    [TestMethod]
    public void Parent_PartitionJustAboveIntMaximumReturnsZero()
    {
        // oracle: 2147483648 = int.MaxValue+1; parent remains sda, partition number is zero.
        Assert.AreEqual("sda", BlockDevice.ParentDisk("sda2147483648"));
        Assert.AreEqual(0, BlockDevice.PartitionNumber("sda2147483648"));
    }

    [TestMethod]
    public void Parent_UnicodeDigitIsNotAnAsciiPartitionSuffix()
    {
        // oracle: explicit /dev/sda + U+0663 clarification requires unknown parent and zero partition.
        Assert.AreEqual("", BlockDevice.ParentDisk("/dev/sda\u0663"));
        Assert.AreEqual(0, BlockDevice.PartitionNumber("/dev/sda\u0663"));
    }

    [TestMethod]
    public void Parent_EmptyDeviceIsUnknown()
    {
        // oracle: empty device maps to empty parent and partition zero.
        Assert.AreEqual("", BlockDevice.ParentDisk(""));
        Assert.AreEqual(0, BlockDevice.PartitionNumber(""));
    }

    [TestMethod]
    public void Parent_NullDeviceIsUnknown()
    {
        // oracle: null device maps to empty parent and partition zero without throwing.
        Assert.AreEqual("", BlockDevice.ParentDisk(null!));
        Assert.AreEqual(0, BlockDevice.PartitionNumber(null!));
    }

    [TestMethod]
    public void Parent_MalformedDeviceIsUnknown()
    {
        // oracle: '???' is not a disk or partition name; malformed/unknown input gives empty parent and zero partition.
        Assert.AreEqual("", BlockDevice.ParentDisk("???"));
        Assert.AreEqual(0, BlockDevice.PartitionNumber("???"));
    }

    [TestMethod]
    public void Read_ConvertsSectorsAndTrimsAllIdentityFields()
    {
        WithRoot(root =>
        {
            Put(root, "sdz", "size", "  4096\n");
            Put(root, "sdz", Path.Combine("device", "model"), "  Model Z \n");
            Put(root, "sdz", Path.Combine("device", "serial"), "\t SN-42 \n");
            Put(root, "sdz", Path.Combine("device", "firmware_rev"), "  FW-7\n");
            var disk = BlockDevice.Read(root, "sdz", 9);
            // oracle: 4096*512 = 2097152 bytes; identity strings are trimmed; Number is supplied 9.
            Assert.AreEqual(2097152L, disk.Bytes);
            Assert.AreEqual("Model Z", disk.Model);
            Assert.AreEqual("SN-42", disk.Serial);
            Assert.AreEqual("FW-7", disk.Firmware);
            Assert.AreEqual(9, disk.Number);
        });
    }

    [TestMethod]
    public void Read_FirmwareRevTakesPrecedenceOverRev()
    {
        WithRoot(root =>
        {
            Put(root, "sda", Path.Combine("device", "firmware_rev"), " PRIMARY \n");
            Put(root, "sda", Path.Combine("device", "rev"), " FALLBACK \n");
            // oracle: existing firmware_rev is preferred to device/rev and is trimmed.
            Assert.AreEqual("PRIMARY", BlockDevice.Read(root, "sda", 0).Firmware);
        });
    }

    [TestMethod]
    public void Read_AbsentFirmwareRevFallsBackToTrimmedRev()
    {
        WithRoot(root =>
        {
            Put(root, "sda", Path.Combine("device", "rev"), "  R2 \n");
            // oracle: firmware_rev is absent; fallback device/rev supplies trimmed R2.
            Assert.AreEqual("R2", BlockDevice.Read(root, "sda", 0).Firmware);
        });
    }

    [TestMethod]
    public void Read_ExistingEmptyFirmwareRevDoesNotUseFallback()
    {
        WithRoot(root =>
        {
            Put(root, "sda", Path.Combine("device", "firmware_rev"), " \n");
            Put(root, "sda", Path.Combine("device", "rev"), "FALLBACK");
            // oracle: fallback is specified only when firmware_rev is absent; a present blank file trims to empty.
            Assert.AreEqual("", BlockDevice.Read(root, "sda", 0).Firmware);
        });
    }

    [TestMethod]
    public void Read_RotationalZeroMeansSsd()
    {
        WithRoot(root =>
        {
            Put(root, "sda", Path.Combine("queue", "rotational"), "0\n");
            // oracle: trimmed rotational 0 maps to SSD.
            Assert.AreEqual("SSD", BlockDevice.Read(root, "sda", 0).Media);
        });
    }

    [TestMethod]
    public void Read_RotationalOneMeansHdd()
    {
        WithRoot(root =>
        {
            Put(root, "sda", Path.Combine("queue", "rotational"), "1\n");
            // oracle: trimmed rotational 1 maps to HDD.
            Assert.AreEqual("HDD", BlockDevice.Read(root, "sda", 0).Media);
        });
    }

    [TestMethod]
    public void Read_OtherRotationalValuesMeanUnknownMedia()
    {
        WithRoot(root =>
        {
            foreach (string value in new[] { "2", "-1", "", "abc" })
            {
                Put(root, "sda", Path.Combine("queue", "rotational"), value);
                // oracle: only 0 and 1 map to media; 2, -1, empty, and abc all map to empty string.
                Assert.AreEqual("", BlockDevice.Read(root, "sda", 0).Media);
            }
        });
    }

    [TestMethod]
    public void Read_NvmePrefixSuppliesBus()
    {
        WithRoot(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "block", "nvme0n1"));
            // oracle: disk name starting nvme maps Bus to NVMe independently of missing metadata.
            Assert.AreEqual("NVMe", BlockDevice.Read(root, "nvme0n1", 0).Bus);
        });
    }

    [TestMethod]
    public void Read_MmcPrefixSuppliesBus()
    {
        WithRoot(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "block", "mmcblk0"));
            // oracle: disk name starting mmcblk maps Bus to MMC.
            Assert.AreEqual("MMC", BlockDevice.Read(root, "mmcblk0", 0).Bus);
        });
    }

    [TestMethod]
    public void Read_OtherNamesHaveEmptyBusEvenWithNvmeModel()
    {
        WithRoot(root =>
        {
            Put(root, "sda", Path.Combine("device", "model"), "NVMe drive");
            // oracle: Bus derives from disk-name prefix; sda matches neither nvme nor mmcblk.
            Assert.AreEqual("", BlockDevice.Read(root, "sda", 0).Bus);
        });
    }

    [TestMethod]
    public void Read_PartitionsRequirePrefixAndMarkerFile()
    {
        WithRoot(root =>
        {
            Put(root, "sda", Path.Combine("sda1", "partition"), "1");
            Put(root, "sda", Path.Combine("sdax", "partition"), "99");
            Put(root, "sda", Path.Combine("sdb1", "partition"), "2");
            Directory.CreateDirectory(Path.Combine(root, "block", "sda", "sda2"));
            // oracle: sda1 and sdax both start sda and have marker files; sdb1 lacks prefix; sda2 lacks marker => 2.
            Assert.AreEqual(2, BlockDevice.Read(root, "sda", 0).PartitionCount);
        });
    }

    [TestMethod]
    public void Read_PartitionMarkerDirectoryIsNotAFile()
    {
        WithRoot(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "block", "sda", "sda1", "partition"));
            // oracle: partition must be a file; a directory named partition contributes zero.
            Assert.AreEqual(0, BlockDevice.Read(root, "sda", 0).PartitionCount);
        });
    }

    [TestMethod]
    public void Read_PartitionsAreImmediateSubdirectoriesOnly()
    {
        WithRoot(root =>
        {
            Put(root, "sda", Path.Combine("holder", "sda1", "partition"), "1");
            // oracle: holder is the only immediate subdirectory and lacks the sda prefix/marker; nested sda1 is not counted.
            Assert.AreEqual(0, BlockDevice.Read(root, "sda", 0).PartitionCount);
        });
    }

    [TestMethod]
    public void Read_MissingFilesGiveDefaultsWithoutLosingNumber()
    {
        WithRoot(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "block", "sda"));
            CheckDefaultDisk(BlockDevice.Read(root, "sda", 23), 23);
        });
    }

    [TestMethod]
    public void Read_MissingRootReturnsDefaults()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try { CheckDefaultDisk(BlockDevice.Read(root, "sda", 4), 4); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Read_NullSysRootReturnsDefaultsWithoutThrowing()
    {
        CheckDefaultDisk(BlockDevice.Read(null!, "sda", 5), 5);
    }

    [TestMethod]
    public void Read_DirectoryInsteadOfSizeFileGivesZeroButKeepsOtherData()
    {
        WithRoot(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "block", "sdz", "size"));
            Put(root, "sdz", Path.Combine("device", "model"), "Still readable");
            var disk = BlockDevice.Read(root, "sdz", 0);
            // oracle: unreadable size yields Bytes 0 without suppressing independently readable model.
            Assert.AreEqual(0L, disk.Bytes);
            Assert.AreEqual("Still readable", disk.Model);
        });
    }

    [TestMethod]
    public void Read_DirectoriesInsteadOfIdentityAndRotationalFilesGiveEmptyStrings()
    {
        WithRoot(root =>
        {
            foreach (string name in new[] { "model", "serial", "firmware_rev", "rev" })
                Directory.CreateDirectory(Path.Combine(root, "block", "sdz", "device", name));
            Directory.CreateDirectory(Path.Combine(root, "block", "sdz", "queue", "rotational"));
            CheckDefaultDisk(BlockDevice.Read(root, "sdz", 6), 6);
        });
    }

    [TestMethod]
    public void Read_ZeroNegativeEmptyAndMalformedSectorsGiveZeroBytes()
    {
        WithRoot(root =>
        {
            foreach (string value in new[] { "0", "-1", "", "abc", "1.5", "9223372036854775808" })
            {
                Put(root, "sda", "size", value);
                // oracle: sector count must be a positive long integer; all listed values fail that rule.
                Assert.AreEqual(0L, BlockDevice.Read(root, "sda", 0).Bytes);
            }
        });
    }

    [TestMethod]
    public void Read_OneSectorIsSmallestPositiveSize()
    {
        WithRoot(root =>
        {
            Put(root, "sda", "size", "1");
            // oracle: smallest positive integer sector count gives 1*512 = 512 bytes.
            Assert.AreEqual(512L, BlockDevice.Read(root, "sda", 0).Bytes);
        });
    }

    [TestMethod]
    public void Read_SectorParsingUsesInvariantCulture()
    {
        WithRoot(root =>
        {
            Put(root, "sda", "size", "+2");
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                culture.NumberFormat.PositiveSign = "p";
                CultureInfo.CurrentCulture = culture;
                // oracle: invariant long parsing accepts +2 despite the current culture's different positive sign; 2*512 = 1024.
                Assert.AreEqual(1024L, BlockDevice.Read(root, "sda", 0).Bytes);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        });
    }

    [TestMethod]
    public void Read_LargestSafeSectorCountDoesNotOverflow()
    {
        WithRoot(root =>
        {
            Put(root, "sda", "size", "18014398509481983");
            // oracle: floor(long.MaxValue/512) = 18014398509481983; product = 9223372036854775296.
            Assert.AreEqual(9223372036854775296L, BlockDevice.Read(root, "sda", 0).Bytes);
        });
    }

    [TestMethod]
    public void Read_FirstOverflowingSectorCountGivesZero()
    {
        WithRoot(root =>
        {
            Put(root, "sda", "size", "18014398509481984");
            // oracle: 18014398509481984*512 = 9223372036854775808 = long.MaxValue+1 => Bytes 0.
            Assert.AreEqual(0L, BlockDevice.Read(root, "sda", 0).Bytes);
        });
    }

    [TestMethod]
    public void Read_HealthyStaysTrueAndUnspecifiedMetadataStaysDefault()
    {
        WithRoot(root =>
        {
            Put(root, "sda", "size", "1");
            Put(root, "sda", Path.Combine("device", "model"), "Model");
            var disk = BlockDevice.Read(root, "sda", -3);
            // oracle: Healthy is left true; Interface/PartitionStyle/Rpm have no supplied mapping; Number is exactly -3.
            Assert.IsTrue(disk.Healthy);
            Assert.AreEqual("", disk.Interface);
            Assert.AreEqual("", disk.PartitionStyle);
            Assert.AreEqual(0, disk.Rpm);
            Assert.AreEqual(-3, disk.Number);
        });
    }

    [TestMethod]
    public void Read_EmptyDiskCannotReadFilesPlantedInBlockDirectory()
    {
        CheckPlantedGuard("", root => Path.Combine(root, "block"));
    }

    [TestMethod]
    public void Read_DotDiskCannotReadFilesPlantedInBlockDirectory()
    {
        CheckPlantedGuard(".", root => Path.Combine(root, "block"));
    }

    [TestMethod]
    public void Read_DotDotDiskCannotReadFilesPlantedInSysRoot()
    {
        CheckPlantedGuard("..", root => root);
    }

    [TestMethod]
    public void Read_TraversalCannotReadPlantedSiblingOutsideSysRoot()
    {
        CheckPlantedGuard("../../escape", root => Path.Combine(Path.GetDirectoryName(root)!, "escape"));
    }

    [TestMethod]
    public void Read_ForwardSlashCannotReadPlantedNestedDisk()
    {
        CheckPlantedGuard("nested/sda", root => Path.Combine(root, "block", "nested", "sda"));
    }

    [TestMethod]
    public void Read_BackslashCannotReadPlantedLiteralOrNestedDisk()
    {
        CheckPlantedGuard("nested\\sda", root => Path.Combine(root, "block", "nested\\sda"));
    }

    [TestMethod]
    public void Read_AbsoluteDiskCannotReplaceBlockRoot()
    {
        WithRoot(root =>
        {
            string disk = Path.Combine(root, "outside");
            string sysRoot = Path.Combine(root, "sys");
            Directory.CreateDirectory(Path.Combine(sysRoot, "block"));
            Directory.CreateDirectory(Path.Combine(disk, "device"));
            File.WriteAllText(Path.Combine(disk, "size"), "9");
            File.WriteAllText(Path.Combine(disk, "device", "model"), "ABSOLUTE_CANARY");
            string unguarded = Path.Combine(sysRoot, "block", disk);
            // oracle: a rooted disk replaces prior Path.Combine components; the unguarded size really contains 9.
            Assert.AreEqual("9", File.ReadAllText(Path.Combine(unguarded, "size")));
            Assert.AreEqual("ABSOLUTE_CANARY", File.ReadAllText(Path.Combine(unguarded, "device", "model")));
            CheckDefaultDisk(BlockDevice.Read(sysRoot, disk, 8), 8);
        });
    }

    [TestMethod]
    public void Read_NullDiskReturnsOnlyNumberWithoutThrowing()
    {
        WithRoot(root => CheckDefaultDisk(BlockDevice.Read(root, null!, 18), 18));
    }

    [TestMethod]
    public void Read_NulContainingDiskReturnsOnlyNumberWithoutThrowing()
    {
        WithRoot(root => CheckDefaultDisk(BlockDevice.Read(root, "sd\0a", 19), 19));
    }

    [TestMethod]
    public void Mounts_TabsAndMixedWhitespaceSeparateAllFields()
    {
        var rows = MountInfo.Parse("36\t35 \t8:1\t/\t/mixed\t rw,noatime\tshared:2 \t-\text4\t/dev/sda1\trw");
        // oracle: round-4(a) splits any run of spaces/tabs; field 5=/mixed, filesystem/source follow the separator.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new MountEntry("/mixed", "/dev/sda1", "ext4"), rows[0]);
    }

    [TestMethod]
    public void Mounts_CrlfAndFinalCarriageReturnAreIgnored()
    {
        var rows = MountInfo.Parse(Mount("/first", "/dev/sda1") + "\r\n" + Mount("/last", "/dev/sdb1", "xfs") + "\r");
        // oracle: trailing CR is ignored on every line; both complete device records survive in appearance order.
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(new MountEntry("/first", "/dev/sda1", "ext4"), rows[0]);
        Assert.AreEqual(new MountEntry("/last", "/dev/sdb1", "xfs"), rows[1]);
    }

    [TestMethod]
    public void Mounts_WhitespaceOnlyLinesAreSkippedWithoutHidingRecords()
    {
        const string whitespace = " \t \r\n\t\n   \r";
        // oracle: round-4(f) skips whitespace-only mountinfo lines, giving no entries.
        Assert.AreEqual(0, MountInfo.Parse(whitespace).Count);
        var rows = MountInfo.Parse(whitespace + "\n" + Mount("/ok", "/dev/sdz1") + "\n" + whitespace);
        // oracle: skipped whitespace leaves the single complete /dev/sdz1 record intact.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new MountEntry("/ok", "/dev/sdz1", "ext4"), rows[0]);
    }

    [TestMethod]
    public void Mounts_NonnumericNegativeAndFractionalIdsAreMalformed()
    {
        foreach (string id in new[] { "x", "-1", "1.5" })
        {
            var rows = MountInfo.Parse(id + " 35 8:1 / /fake rw - ext4 /dev/sda1 rw\n" + Mount("/ok", "/dev/sdb1"));
            // oracle: round-4(c) requires a non-negative integer id; invalid id alone rejects sda1 while valid sdb1 survives.
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(new MountEntry("/ok", "/dev/sdb1", "ext4"), rows[0]);
        }
    }

    [TestMethod]
    public void Mounts_NonnumericNegativeAndFractionalParentsAreMalformed()
    {
        foreach (string parent in new[] { "y", "-1", "1.5" })
        {
            var rows = MountInfo.Parse("36 " + parent + " 8:1 / /fake rw - ext4 /dev/sda1 rw\n" + Mount("/ok", "/dev/sdb1"));
            // oracle: round-4(c) requires a non-negative integer parent; invalid parent alone rejects sda1, leaving sdb1.
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(new MountEntry("/ok", "/dev/sdb1", "ext4"), rows[0]);
        }
    }

    [TestMethod]
    public void Mounts_NonnumericNegativeAndFractionalMajorsAreMalformed()
    {
        foreach (string major in new[] { "z", "-1", "1.5" })
        {
            var rows = MountInfo.Parse("36 35 " + major + ":1 / /fake rw - ext4 /dev/sda1 rw");
            // oracle: round-4(c) requires the major part alone to be a non-negative integer; all these lines are skipped.
            Assert.AreEqual(0, rows.Count);
        }
    }

    [TestMethod]
    public void Mounts_NonnumericNegativeAndFractionalMinorsAreMalformed()
    {
        foreach (string minor in new[] { "z", "-1", "1.5" })
        {
            var rows = MountInfo.Parse("36 35 8:" + minor + " / /fake rw - ext4 /dev/sda1 rw");
            // oracle: round-4(c) requires the minor part alone to be a non-negative integer; all these lines are skipped.
            Assert.AreEqual(0, rows.Count);
        }
    }

    [TestMethod]
    public void Mounts_DeviceNumberRequiresExactlyTwoNonemptyColonSeparatedParts()
    {
        foreach (string deviceNumber in new[] { "8", "8:", ":1", "8:1:2", "8::1" })
        {
            var rows = MountInfo.Parse("36 35 " + deviceNumber + " / /fake rw - ext4 /dev/sda1 rw");
            // oracle: round-4(c) requires <major>:<minor>; missing parts or extra colon-delimited parts make the line malformed.
            Assert.AreEqual(0, rows.Count);
        }
    }

    [TestMethod]
    public void Mounts_ZeroIdParentMajorAndMinorAreNonnegativeBoundaryValues()
    {
        var rows = MountInfo.Parse("0 0 0:0 / /zero rw - ext4 /dev/sda1 rw");
        // oracle: round-4(c) permits non-negative integers including zero in id, parent, major, and minor.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new MountEntry("/zero", "/dev/sda1", "ext4"), rows[0]);
    }

    [TestMethod]
    public void Mounts_NonoctalDigitPreventsEscapeDecoding()
    {
        var rows = MountInfo.Parse(Mount(@"/a\08b", "/dev/sda1"));
        // oracle: round-4(g) requires three octal digits; 8 and b are not octal, so /a\08b stays literal.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(@"/a\08b", rows[0].MountPoint);
    }

    [TestMethod]
    public void Mounts_TruncatedTwoDigitEscapeRemainsLiteral()
    {
        var rows = MountInfo.Parse(Mount(@"/a\04", "/dev/sda1"));
        // oracle: round-4(g) requires exactly three octal digits; the final two-digit \04 stays literal.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(@"/a\04", rows[0].MountPoint);
    }

    [TestMethod]
    public void Mounts_DecodedBackslashIsNotDecodedAgain()
    {
        var rows = MountInfo.Parse(Mount(@"/a\134040", "/dev/sda1"));
        // oracle: round-4(g) decodes \134 once to backslash; following 040 stays literal, yielding /a\040 rather than /a plus space.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(@"/a\040", rows[0].MountPoint);
    }

    [TestMethod]
    [DoNotParallelize]
    public void Read_EmptySysRootCannotReadPlantedRelativeBlockDirectory()
    {
        WithRoot(root =>
        {
            Put(root, "nvme0n1", "size", "11");
            Put(root, "nvme0n1", Path.Combine("device", "model"), "EMPTY_ROOT_CANARY");
            string previousDirectory = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(root);
                string unguarded = Path.Combine("", "block", "nvme0n1");
                // oracle: without the empty-root guard the relative path really reaches planted size=11 and model=EMPTY_ROOT_CANARY.
                Assert.AreEqual("11", File.ReadAllText(Path.Combine(unguarded, "size")));
                Assert.AreEqual("EMPTY_ROOT_CANARY", File.ReadAllText(Path.Combine(unguarded, "device", "model")));
                // oracle: round-4(e) makes empty sysRoot behave like null: only Number=27, all other fields default (even NVMe Bus).
                CheckDefaultDisk(BlockDevice.Read("", "nvme0n1", 27), 27);
            }
            finally { Directory.SetCurrentDirectory(previousDirectory); }
        });
    }
}
