using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SysMonitor.Linux.Sampling;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
[DoNotParallelize] // Root-guard tests temporarily change CurrentDirectory.
public class SamplerTests
{
    private const long GiB = 1073741824L;

    private sealed class Fixture
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        public string Proc => Path.Combine(Root, "proc");
        public string Sys => Path.Combine(Root, "sys");

        public void ProcFile(string relative, string text) => Put(Proc, relative, text);
        public void SysFile(string relative, string text) => Put(Sys, relative, text);
        public void SysDirectory(string relative) => Directory.CreateDirectory(Path.Combine(Sys, relative));
        public LinuxSampler Sampler(Func<string, (long TotalBytes, long FreeBytes)>? usage = null)
            => new(Proc, Sys, usage ?? (_ => (0L, 0L)));

        public void Mounts(params (string Device, string Point, string Fs)[] mounts)
        {
            var lines = new string[mounts.Length];
            for (int i = 0; i < mounts.Length; i++)
            {
                var m = mounts[i];
                lines[i] = $"{i + 30} 1 8:{i + 1} / {m.Point} rw - {m.Fs} {m.Device} rw";
            }
            ProcFile("self/mountinfo", string.Join("\n", lines));
        }

        private static void Put(string root, string relative, string text)
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }

    private static void WithFixture(Action<Fixture> action)
    {
        var f = new Fixture();
        try
        {
            Directory.CreateDirectory(f.Proc);
            Directory.CreateDirectory(f.Sys);
            action(f);
        }
        finally
        {
            if (Directory.Exists(f.Root)) Directory.Delete(f.Root, true);
        }
    }

    private static string Network(string name, long received, long sent)
        => FormattableString.Invariant($"{name}: {received} 0 0 0 0 0 0 0 {sent} 0 0 0 0 0 0 0\n");

    private static string DiskLine(string name, long read, long written)
        => FormattableString.Invariant($"8 0 {name} 0 0 {read} 0 0 0 {written}\n");

    private static void CounterFrame(Fixture f, int frame)
    {
        f.ProcFile("stat", FormattableString.Invariant($"cpu {100L * frame} 0 0 {100L * (frame + 1)}\ncpu0 {100L * frame} 0 0 {100L * (frame + 1)}\n"));
        f.ProcFile("diskstats", DiskLine("sda", 4096L * frame, 8192L * frame));
        f.ProcFile("net/dev", Network("eth0", 1048576L * frame, 2097152L * frame));
    }

    private static void AssertCounterRates(Snapshot s, int cpu, double read, double write, double down, double up)
    {
        // oracle: callers supply hand-calculated CPU percentages and MiB/s from their specified frame deltas.
        Assert.IsTrue(s.Ready);
        Assert.AreEqual(1, s.Cores.Count);
        Assert.AreEqual(cpu, s.CpuTotal);
        Assert.AreEqual(cpu, s.Cores[0].Usage);
        Assert.AreEqual(1, s.Disks.Count);
        Assert.AreEqual(read, s.Disks[0].ReadMb, 0.0);
        Assert.AreEqual(write, s.Disks[0].WriteMb, 0.0);
        Assert.AreEqual(1, s.Adapters.Count);
        Assert.AreEqual(down, s.Adapters[0].DownMb, 0.0);
        Assert.AreEqual(up, s.Adapters[0].UpMb, 0.0);
    }

    private static void AssertEmpty(Snapshot s, bool ready)
    {
        // oracle: clarification's no-data Snapshot has empty lists, zero RAM/CPU and no temperature; Ready follows call count.
        Assert.AreEqual(ready, s.Ready);
        Assert.AreEqual(0, s.Cores.Count);
        Assert.AreEqual(0, s.Disks.Count);
        Assert.AreEqual(0, s.Adapters.Count);
        Assert.AreEqual(0, s.Modules.Count);
        Assert.AreEqual(0, s.CpuTotal);
        Assert.IsNull(s.CpuTemp);
        Assert.IsFalse(s.CpuTempEstimated);
        Assert.IsNotNull(s.Ram);
        Assert.AreEqual(0, s.Ram.Usage);
        Assert.AreEqual(0.0, s.Ram.UsedGb, 0.0);
        Assert.AreEqual(0.0, s.Ram.TotalGb, 0.0);
    }

    [TestMethod]
    public void MissingAllFiles_FirstSnapshotIsEmptyAndNotReady()
    {
        WithFixture(f =>
        {
            // oracle: no files contribute data; first call is not Ready.
            AssertEmpty(f.Sampler().Sample(1000), false);
        });
    }

    [TestMethod]
    public void MissingAllFiles_SecondSnapshotIsReady()
    {
        WithFixture(f =>
        {
            var sampler = f.Sampler();
            sampler.Sample(1000);
            // oracle: Ready is true from the second call even with no data.
            AssertEmpty(sampler.Sample(2000), true);
        });
    }

    [TestMethod]
    public void FirstSample_CounterUsagesAndRatesAreZeroButCapacityIsFilled()
    {
        WithFixture(f =>
        {
            f.ProcFile("stat", "cpu 100 0 0 100\ncpu0 100 0 0 100\n");
            f.ProcFile("net/dev", Network("eth0", 1048576, 2097152));
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            f.ProcFile("diskstats", DiskLine("sda", 2048, 4096));
            var s = f.Sampler(_ => (8 * GiB, 2 * GiB)).Sample(1000);
            // oracle: first call lacks prior counters, so CPU usages and disk/network rates are zero.
            Assert.IsFalse(s.Ready);
            Assert.AreEqual(1, s.Cores.Count);
            Assert.AreEqual(0, s.Cores[0].Usage);
            Assert.AreEqual(0, s.CpuTotal);
            Assert.AreEqual(1, s.Disks.Count);
            Assert.AreEqual(0.0, s.Disks[0].ReadMb, 0.0);
            Assert.AreEqual(0.0, s.Disks[0].WriteMb, 0.0);
            Assert.AreEqual(1, s.Adapters.Count);
            Assert.AreEqual(0.0, s.Adapters[0].DownMb, 0.0);
            Assert.AreEqual(0.0, s.Adapters[0].UpMb, 0.0);
            // oracle: capacity needs no baseline: (8-2)/8*100=75%; 6 GiB used of 8 GiB.
            Assert.AreEqual(75, s.Disks[0].Usage);
            Assert.AreEqual(6.0, s.Disks[0].UsedGb, 0.0);
            Assert.AreEqual(8.0, s.Disks[0].TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void SecondSample_ComputesCpuDiskAndNetworkRates()
    {
        WithFixture(f =>
        {
            f.ProcFile("stat", "cpu 0 0 0 100\ncpu0 0 0 0 100\n");
            f.ProcFile("net/dev", Network("eth0", 1048576, 2097152));
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            f.ProcFile("diskstats", DiskLine("sda", 2048, 4096));
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("stat", "cpu 100 0 0 200\ncpu0 100 0 0 200\n");
            f.ProcFile("net/dev", Network("eth0", 5242880, 10485760));
            f.ProcFile("diskstats", DiskLine("sda", 10240, 20480));
            var s = sampler.Sample(3000);
            // oracle: Ready on second call; busy/total deltas 100/200=>50%; disk/network deltas 4/8 MiB over 2s=>2/4.
            Assert.IsTrue(s.Ready);
            Assert.AreEqual(50, s.CpuTotal);
            Assert.AreEqual(50, s.Cores[0].Usage);
            Assert.AreEqual(2.0, s.Disks[0].ReadMb, 0.0);
            Assert.AreEqual(4.0, s.Disks[0].WriteMb, 0.0);
            Assert.AreEqual(2.0, s.Adapters[0].DownMb, 0.0);
            Assert.AreEqual(4.0, s.Adapters[0].UpMb, 0.0);
        });
    }

    [TestMethod]
    public void UnchangedFiles_SecondSampleHasZeroRates()
    {
        WithFixture(f =>
        {
            f.ProcFile("stat", "cpu 100 0 0 100\ncpu0 100 0 0 100\n");
            f.ProcFile("net/dev", Network("eth0", 1048576, 2097152));
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            f.ProcFile("diskstats", DiskLine("sda", 2048, 4096));
            var sampler = f.Sampler();
            sampler.Sample(1000);
            var s = sampler.Sample(2000);
            // oracle: unchanged counters have zero deltas; second call is Ready.
            Assert.IsTrue(s.Ready);
            Assert.AreEqual(0, s.CpuTotal);
            Assert.AreEqual(0, s.Cores[0].Usage);
            Assert.AreEqual(0.0, s.Disks[0].ReadMb, 0.0);
            Assert.AreEqual(0.0, s.Disks[0].WriteMb, 0.0);
            Assert.AreEqual(0.0, s.Adapters[0].DownMb, 0.0);
            Assert.AreEqual(0.0, s.Adapters[0].UpMb, 0.0);
        });
    }

    [TestMethod]
    public void CpuCoreBaselines_AreMatchedByPositionNotCpuNumber()
    {
        WithFixture(f =>
        {
            f.ProcFile("stat", "cpu 100 0 0 100\ncpu7 0 0 0 100\ncpu2 100 0 0 0\n");
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("stat", "cpu 200 0 0 200\ncpu2 100 0 0 100\ncpu7 100 0 0 100\n");
            var s = sampler.Sample(2000);
            // oracle: positional busy/total deltas are 100/100=>100%, then 0/100=>0%; matching names would reverse these.
            Assert.AreEqual(2, s.Cores.Count);
            Assert.AreEqual(100, s.Cores[0].Usage);
            Assert.AreEqual(0, s.Cores[1].Usage);
            Assert.AreEqual(50, s.CpuTotal);
            Assert.IsNull(s.Cores[0].Temp);
            Assert.IsNull(s.Cores[1].Temp);
            Assert.IsFalse(s.CpuTempEstimated);
        });
    }

    [TestMethod]
    public void CpuAddedCore_HasNoPreviousCounter()
    {
        WithFixture(f =>
        {
            f.ProcFile("stat", "cpu 0 0 0 100\ncpu0 0 0 0 100\n");
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("stat", "cpu 200 0 0 100\ncpu0 100 0 0 100\ncpu1 100 0 0 0\n");
            var s = sampler.Sample(2000);
            // oracle: existing position changes busy/total by 100/100; new position lacks a prior counter.
            Assert.AreEqual(2, s.Cores.Count);
            Assert.AreEqual(100, s.Cores[0].Usage);
            Assert.AreEqual(0, s.Cores[1].Usage);
        });
    }

    [TestMethod]
    public void CpuRemovedCore_DoesNotLeaveStaleCoreInSnapshot()
    {
        WithFixture(f =>
        {
            f.ProcFile("stat", "cpu 0 0 0 200\ncpu0 0 0 0 100\ncpu1 0 0 0 100\n");
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("stat", "cpu 100 0 0 200\ncpu9 100 0 0 100\n");
            var s = sampler.Sample(2000);
            // oracle: current file has one core; its positional busy/total deltas are 100/100=>100%.
            Assert.AreEqual(1, s.Cores.Count);
            Assert.AreEqual(100, s.Cores[0].Usage);
        });
    }

    [TestMethod]
    public void CpuTabSeparatedCrLfInput_IsAccepted()
    {
        WithFixture(f =>
        {
            f.ProcFile("stat", "cpu\t0\t0\t0\t100\r\ncpu0\t0\t0\t0\t100\r\n");
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("stat", "cpu\t100\t0\t0\t100\r\ncpu0\t100\t0\t0\t100\r\n");
            var s = sampler.Sample(2000);
            // oracle: tabs/CRLF are accepted; busy and total both increase by 100=>100%.
            Assert.AreEqual(100, s.CpuTotal);
            Assert.AreEqual(1, s.Cores.Count);
            Assert.AreEqual(100, s.Cores[0].Usage);
        });
    }

    [TestMethod]
    public void Ram_IsParsedOnFirstCallWithoutBaseline()
    {
        WithFixture(f =>
        {
            const string memory = "MemTotal: 8388608 kB\nMemFree: 1048576 kB\nMemAvailable: 2097152 kB\n";
            f.ProcFile("meminfo", memory);
            var expected = MemInfo.Parse(memory);
            var s = f.Sampler().Sample(1000);
            // oracle: phase 4 assigns MemInfo.Parse(meminfo) exactly, including on the first call.
            Assert.IsFalse(s.Ready);
            Assert.AreEqual(expected.Usage, s.Ram.Usage);
            Assert.AreEqual(expected.UsedGb, s.Ram.UsedGb, 0.0);
            Assert.AreEqual(expected.TotalGb, s.Ram.TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void Ram_UsesCurrentFileOnEveryCall()
    {
        WithFixture(f =>
        {
            f.ProcFile("meminfo", "MemTotal: 8388608 kB\nMemAvailable: 6291456 kB\n");
            var sampler = f.Sampler();
            sampler.Sample(1000);
            const string memory = "MemTotal: 8388608 kB\nMemAvailable: 2097152 kB\n";
            f.ProcFile("meminfo", memory);
            var expected = MemInfo.Parse(memory);
            var s = sampler.Sample(2000);
            // oracle: RAM is MemInfo.Parse of the current file, never a previous snapshot or a rate.
            Assert.AreEqual(expected.Usage, s.Ram.Usage);
            Assert.AreEqual(expected.UsedGb, s.Ram.UsedGb, 0.0);
            Assert.AreEqual(expected.TotalGb, s.Ram.TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void MalformedFiles_ContributeNoDataAndNeverThrow()
    {
        WithFixture(f =>
        {
            f.ProcFile("stat", "invalid");
            f.ProcFile("meminfo", "invalid");
            f.ProcFile("net/dev", "invalid");
            f.ProcFile("self/mountinfo", "invalid");
            f.ProcFile("diskstats", "invalid");
            f.SysFile("class/hwmon/hwmon0/name", "coretemp");
            f.SysFile("class/hwmon/hwmon0/temp1_input", "invalid");
            var sampler = f.Sampler();
            // oracle: malformed input contributes nothing; Ready follows call count.
            AssertEmpty(sampler.Sample(1000), false);
            AssertEmpty(sampler.Sample(2000), true);
        });
    }

    [TestMethod]
    public void DiskNumber_IsParentFirstAppearanceOrdinalNotPartitionNumber()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sdb7", "/first", "ext4"), ("/dev/sda2", "/second", "xfs"), ("/dev/sdb1", "/third", "ext4"));
            var d = f.Sampler().Sample(1000).Disks;
            // oracle: first parent sdb gets 0, sda gets 1, sdb1 shares 0; partition numbers are separately 7,2,1.
            Assert.AreEqual(3, d.Count);
            Assert.AreEqual((int?)0, d[0].Detail!.DiskNumber);
            Assert.AreEqual((int?)1, d[1].Detail!.DiskNumber);
            Assert.AreEqual((int?)0, d[2].Detail!.DiskNumber);
            Assert.AreEqual(0, d[0].Detail!.Disk!.Number);
            Assert.AreEqual(1, d[1].Detail!.Disk!.Number);
            Assert.AreEqual(0, d[2].Detail!.Disk!.Number);
            Assert.AreEqual(7, d[0].Detail!.PartitionNumber);
            Assert.AreEqual(2, d[1].Detail!.PartitionNumber);
            Assert.AreEqual(1, d[2].Detail!.PartitionNumber);
        });
    }

    [TestMethod]
    public void DisksOnSameParent_AreKeptAsSeparateMounts()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/one", "ext4"), ("/dev/sda2", "/two", "xfs"));
            var d = f.Sampler().Sample(1000).Disks;
            // oracle: one Disk per MountEntry, without further de-duplication by parent.
            Assert.AreEqual(2, d.Count);
            Assert.AreEqual("/one", d[0].Letter);
            Assert.AreEqual("/two", d[1].Letter);
            Assert.AreEqual("ext4", d[0].Detail!.FileSystem);
            Assert.AreEqual("xfs", d[1].Detail!.FileSystem);
        });
    }

    [TestMethod]
    public void DiskDuplicateDevice_UsesMountInfoDeduplication()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/one", "ext4"), ("/dev/sda1", "/two", "ext4"));
            var d = f.Sampler().Sample(1000).Disks;
            // oracle: MountInfo.Parse already returns only one MountEntry per device.
            Assert.AreEqual(1, d.Count);
        });
    }

    [TestMethod]
    public void DiskLabels_RootTrailingSlashesAndNestedMounts()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/", "ext4"), ("/dev/sda2", "/var/data///", "xfs"), ("/dev/sda3", "/mnt/archive", "ext4"));
            var d = f.Sampler().Sample(1000).Disks;
            // oracle: remove trailing slashes for Label only; root remains / and final segments are data/archive.
            Assert.AreEqual(3, d.Count);
            Assert.AreEqual("/", d[0].Label);
            Assert.AreEqual("/var/data///", d[1].Letter);
            Assert.AreEqual("data", d[1].Label);
            Assert.AreEqual("archive", d[2].Label);
            for (int i = 0; i < d.Count; i++)
            {
                // oracle: DriveDetail copies Letter and Label; disk temperature is null.
                Assert.AreEqual(d[i].Letter, d[i].Detail!.Letter);
                Assert.AreEqual(d[i].Label, d[i].Detail!.Label);
                Assert.IsNull(d[i].Temp);
            }
        });
    }

    [TestMethod]
    public void UnmappedDevice_StillHasCapacityAndNoPhysicalDiskOrRates()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/mapper/volume", "/mapped", "ext4"));
            f.ProcFile("diskstats", DiskLine("volume", 0, 0));
            var sampler = f.Sampler(_ => (4 * GiB, GiB));
            sampler.Sample(1000);
            f.ProcFile("diskstats", DiskLine("volume", 2048, 4096));
            var d = sampler.Sample(2000).Disks[0];
            // oracle: empty ParentDisk means Disk/Unknown, null physical disk and zero rates; capacity is (4-1)/4=75%.
            Assert.AreEqual("/mapped", d.Letter);
            Assert.AreEqual("Disk", d.Media);
            Assert.AreEqual("Unknown", d.Bus);
            Assert.IsNotNull(d.Detail);
            Assert.IsNull(d.Detail.Disk);
            Assert.AreEqual(0.0, d.ReadMb, 0.0);
            Assert.AreEqual(0.0, d.WriteMb, 0.0);
            Assert.AreEqual(75, d.Usage);
            Assert.AreEqual(3.0, d.UsedGb, 0.0);
            Assert.AreEqual(4.0, d.TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void DiskMediaAndBus_AreReadFromParentSysBlockFixtures()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/ssd", "ext4"), ("/dev/sdb1", "/hdd", "ext4"), ("/dev/nvme0n1p2", "/nvme", "ext4"), ("/dev/mmcblk0p3", "/mmc", "ext4"));
            f.SysFile("block/sda/queue/rotational", "0\n");
            f.SysFile("block/sdb/queue/rotational", "1\n");
            f.SysFile("block/nvme0n1/queue/rotational", "0\n");
            f.SysFile("block/mmcblk0/queue/rotational", "0\n");
            var d = f.Sampler().Sample(1000).Disks;
            // oracle: four MountEntries produce four Disks; media/bus are normalized from BlockDevice.Read for each parent.
            Assert.AreEqual(4, d.Count);
            string[] parents = ["sda", "sdb", "nvme0n1", "mmcblk0"];
            for (int i = 0; i < parents.Length; i++)
            {
                var expected = BlockDevice.Read(f.Sys, parents[i], i);
                // oracle: sampler preserves only SSD/HDD and NVMe/MMC from BlockDevice.Read, otherwise using Disk/Unknown.
                Assert.AreEqual(expected.Media is "SSD" or "HDD" ? expected.Media : "Disk", d[i].Media);
                Assert.AreEqual(expected.Bus is "NVMe" or "MMC" ? expected.Bus : "Unknown", d[i].Bus);
            }
            // oracle: PartitionNumber is separate from physical-disk ordinal: nvme0n1p2 is 2 and mmcblk0p3 is 3.
            Assert.AreEqual(2, d[2].Detail!.PartitionNumber);
            Assert.AreEqual(3, d[3].Detail!.PartitionNumber);
        });
    }

    [TestMethod]
    public void DiskRates_UseWholeParentCountersNotPartitionCounters()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            f.ProcFile("diskstats", DiskLine("sda", 0, 0) + DiskLine("sda1", 0, 0));
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("diskstats", DiskLine("sda", 4096, 8192) + DiskLine("sda1", 2048, 2048));
            var d = sampler.Sample(2000).Disks[0];
            // oracle: whole-parent sectors 4096/8192=>2/4 MiB in 1s; partition counters would give 1/1.
            Assert.AreEqual(2.0, d.ReadMb, 0.0);
            Assert.AreEqual(4.0, d.WriteMb, 0.0);
        });
    }

    [TestMethod]
    public void DiskDuplicateCounters_FirstEntryWinsOnBothCalls()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            f.ProcFile("diskstats", DiskLine("sda", 2048, 4096) + DiskLine("sda", 20480, 20480));
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("diskstats", DiskLine("sda", 6144, 12288) + DiskLine("sda", 40960, 61440));
            var d = sampler.Sample(2000).Disks[0];
            // oracle: first entries change 4096/8192 sectors=>2/4 MiB; last entries would give 10/20.
            Assert.AreEqual(2.0, d.ReadMb, 0.0);
            Assert.AreEqual(4.0, d.WriteMb, 0.0);
        });
    }

    [TestMethod]
    public void DiskNewParentCounter_HasZeroInitialRate()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            f.ProcFile("diskstats", DiskLine("sdb", 0, 0));
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("diskstats", DiskLine("sda", 2048, 4096));
            var d = sampler.Sample(2000).Disks[0];
            // oracle: current sda has no matching previous counter, so both rates are zero.
            Assert.AreEqual(0.0, d.ReadMb, 0.0);
            Assert.AreEqual(0.0, d.WriteMb, 0.0);
        });
    }

    [TestMethod]
    public void FsUsage_ReceivesExactMountPoints()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/one/", "ext4"), ("/dev/sda2", "/two", "ext4"));
            var seen = new System.Collections.Generic.List<string>();
            var s = f.Sampler(point =>
            {
                seen.Add(point);
                return point == "/one/" ? (4 * GiB, GiB) : (8 * GiB, 4 * GiB);
            }).Sample(1000);
            // oracle: exact mount points select capacities 4/8 GiB and usages 75/50%; labels must not replace fsUsage arguments.
            Assert.AreEqual(2, seen.Count);
            Assert.IsTrue(seen.Contains("/one/"));
            Assert.IsTrue(seen.Contains("/two"));
            Assert.AreEqual(4.0, s.Disks[0].TotalGb, 0.0);
            Assert.AreEqual(75, s.Disks[0].Usage);
            Assert.AreEqual(8.0, s.Disks[1].TotalGb, 0.0);
            Assert.AreEqual(50, s.Disks[1].Usage);
        });
    }

    [TestMethod]
    public void FsUsage_ThrowingKeepsDiskAndOtherMounts()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/bad", "ext4"), ("/dev/sda2", "/good", "ext4"));
            var s = f.Sampler(point => point == "/bad" ? throw new IOException("fixture failure") : (2 * GiB, GiB)).Sample(1000);
            // oracle: throwing fsUsage zeroes only that disk's capacity, keeps it, and continues to the good mount.
            Assert.AreEqual(2, s.Disks.Count);
            Assert.AreEqual(0, s.Disks[0].Usage);
            Assert.AreEqual(0.0, s.Disks[0].UsedGb, 0.0);
            Assert.AreEqual(0.0, s.Disks[0].TotalGb, 0.0);
            Assert.AreEqual(50, s.Disks[1].Usage);
            Assert.AreEqual(1.0, s.Disks[1].UsedGb, 0.0);
            Assert.AreEqual(2.0, s.Disks[1].TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void FsUsage_NegativeFree()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            var disk = f.Sampler(_ => (2 * GiB, -1)).Sample(1000).Disks[0];
            // oracle: free=-1 clamps to zero; all 2 GiB are used; GiB divides bytes by 1073741824.
            Assert.AreEqual(100, disk.Usage);
            Assert.AreEqual(2.0, disk.UsedGb, 0.0);
            Assert.AreEqual(2.0, disk.TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void FsUsage_FreeAboveTotal()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            var disk = f.Sampler(_ => (2 * GiB, 3 * GiB)).Sample(1000).Disks[0];
            // oracle: free above total clamps to total; no bytes are used; GiB divides bytes by 1073741824.
            Assert.AreEqual(0, disk.Usage);
            Assert.AreEqual(0.0, disk.UsedGb, 0.0);
            Assert.AreEqual(2.0, disk.TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void FsUsage_ZeroTotal()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            var disk = f.Sampler(_ => (0, -1)).Sample(1000).Disks[0];
            // oracle: total <= 0 forces all capacity values to zero; GiB divides bytes by 1073741824.
            Assert.AreEqual(0, disk.Usage);
            Assert.AreEqual(0.0, disk.UsedGb, 0.0);
            Assert.AreEqual(0.0, disk.TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void FsUsage_NegativeTotal()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            var disk = f.Sampler(_ => (-1, -2)).Sample(1000).Disks[0];
            // oracle: total <= 0 forces all capacity values to zero; GiB divides bytes by 1073741824.
            Assert.AreEqual(0, disk.Usage);
            Assert.AreEqual(0.0, disk.UsedGb, 0.0);
            Assert.AreEqual(0.0, disk.TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void FsUsage_AllFree()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            var disk = f.Sampler(_ => (2 * GiB, 2 * GiB)).Sample(1000).Disks[0];
            // oracle: free equals total; 0% used; GiB divides bytes by 1073741824.
            Assert.AreEqual(0, disk.Usage);
            Assert.AreEqual(0.0, disk.UsedGb, 0.0);
            Assert.AreEqual(2.0, disk.TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void FsUsage_NoFree()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            var disk = f.Sampler(_ => (2 * GiB, 0)).Sample(1000).Disks[0];
            // oracle: free zero means 100% of 2 GiB used; GiB divides bytes by 1073741824.
            Assert.AreEqual(100, disk.Usage);
            Assert.AreEqual(2.0, disk.UsedGb, 0.0);
            Assert.AreEqual(2.0, disk.TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void FsUsage_HalfPercentAwayFromZero()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            var disk = f.Sampler(_ => (200, 199)).Sample(1000).Disks[0];
            // oracle: used=1 of 200 bytes =>0.5%, rounded away from zero =>1; GiB divides bytes by 1073741824.
            Assert.AreEqual(1, disk.Usage);
            Assert.AreEqual(1.0 / 1073741824.0, disk.UsedGb, 0.0);
            Assert.AreEqual(200.0 / 1073741824.0, disk.TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void FsUsage_BelowHalfPercent()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            var disk = f.Sampler(_ => (201, 200)).Sample(1000).Disks[0];
            // oracle: used=1 of 201 =>100/201 <0.5%, rounds to 0; GiB divides bytes by 1073741824.
            Assert.AreEqual(0, disk.Usage);
            Assert.AreEqual(1.0 / 1073741824.0, disk.UsedGb, 0.0);
            Assert.AreEqual(201.0 / 1073741824.0, disk.TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void FsUsage_AboveHalfPercent()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            var disk = f.Sampler(_ => (199, 198)).Sample(1000).Disks[0];
            // oracle: used=1 of 199 =>100/199 >0.5%, rounds to 1; GiB divides bytes by 1073741824.
            Assert.AreEqual(1, disk.Usage);
            Assert.AreEqual(1.0 / 1073741824.0, disk.UsedGb, 0.0);
            Assert.AreEqual(199.0 / 1073741824.0, disk.TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void Network_IdentityTotalsWirelessAndSpeedAreFilledOnFirstCall()
    {
        WithFixture(f =>
        {
            f.ProcFile("net/dev", Network("wlan0", 1048576, 2097152));
            f.SysFile("class/net/wlan0/speed", " \t1000\r\n");
            f.SysDirectory("class/net/wlan0/wireless");
            var a = f.Sampler().Sample(1000).Adapters[0];
            // oracle: identity/counters/speed/wireless need no baseline; trimmed positive integer speed is 1000.
            Assert.AreEqual("wlan0", a.Id);
            Assert.AreEqual("wlan0", a.Name);
            Assert.AreEqual(1048576L, a.TotalReceived);
            Assert.AreEqual(2097152L, a.TotalSent);
            Assert.IsTrue(a.Wireless);
            Assert.AreEqual(1000.0, a.SpeedMbps, 0.0);
            Assert.AreEqual(0.0, a.DownMb, 0.0);
            Assert.AreEqual(0.0, a.UpMb, 0.0);
        });
    }

    [TestMethod]
    public void Network_WiredAndLoopbackHandling()
    {
        WithFixture(f =>
        {
            f.ProcFile("net/dev", Network("lo", 1048576, 1048576) + Network("eth0", 2097152, 3145728));
            var adapters = f.Sampler().Sample(1000).Adapters;
            // oracle: NetDev skips loopback; no wireless directory or speed file means false/zero.
            Assert.AreEqual(1, adapters.Count);
            Assert.AreEqual("eth0", adapters[0].Id);
            Assert.IsFalse(adapters[0].Wireless);
            Assert.AreEqual(0.0, adapters[0].SpeedMbps, 0.0);
        });
    }

    [TestMethod]
    public void Network_NewInterfaceHasZeroRatesAndExistingKeepsNamedBaseline()
    {
        WithFixture(f =>
        {
            f.ProcFile("net/dev", Network("eth0", 0, 0));
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("net/dev", Network("new0", 1048576, 2097152) + Network("eth0", 2097152, 4194304));
            var adapters = sampler.Sample(2000).Adapters;
            var added = adapters.Single(a => a.Id == "new0");
            var existing = adapters.Single(a => a.Id == "eth0");
            // oracle: new0 has no baseline; eth0 retains its named baseline despite moving within the input, giving 2/4 MiB/s.
            Assert.AreEqual(2, adapters.Count);
            Assert.AreEqual("new0", added.Name);
            Assert.AreEqual(0.0, added.DownMb, 0.0);
            Assert.AreEqual(0.0, added.UpMb, 0.0);
            Assert.AreEqual("eth0", existing.Name);
            Assert.AreEqual(2.0, existing.DownMb, 0.0);
            Assert.AreEqual(4.0, existing.UpMb, 0.0);
        });
    }

    [TestMethod]
    public void Network_TabSeparatedCrLfCountersAreAccepted()
    {
        WithFixture(f =>
        {
            f.ProcFile("net/dev", "eth0:\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\r\n");
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("net/dev", "eth0:\t1048576\t0\t0\t0\t0\t0\t0\t0\t2097152\t0\t0\t0\t0\t0\t0\t0\r\n");
            var a = sampler.Sample(2000).Adapters[0];
            // oracle: accepted tabs/CRLF encode deltas 1/2 MiB over 1 second.
            Assert.AreEqual(1.0, a.DownMb, 0.0);
            Assert.AreEqual(2.0, a.UpMb, 0.0);
        });
    }

    [TestMethod]
    public void Network_CounterResetClampsDirectionsIndependently()
    {
        WithFixture(f =>
        {
            f.ProcFile("net/dev", Network("eth0", 2097152, 1048576));
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("net/dev", Network("eth0", 0, 3145728));
            var a = sampler.Sample(2000).Adapters[0];
            // oracle: received counter reset gives zero down; sent delta 2 MiB in 1s gives 2 up.
            Assert.AreEqual(0.0, a.DownMb, 0.0);
            Assert.AreEqual(2.0, a.UpMb, 0.0);
        });
    }

    [TestMethod]
    public void Network_SpeedZero()
    {
        WithFixture(f =>
        {
            f.ProcFile("net/dev", Network("eth0", 0, 0));
            f.SysFile("class/net/eth0/speed", "0");
            var a = f.Sampler().Sample(1000).Adapters[0];
            // oracle: nonpositive speed gives zero.
            Assert.AreEqual(0.0, a.SpeedMbps, 0.0);
        });
    }

    [TestMethod]
    public void Network_SpeedNegative()
    {
        WithFixture(f =>
        {
            f.ProcFile("net/dev", Network("eth0", 0, 0));
            f.SysFile("class/net/eth0/speed", "-1");
            var a = f.Sampler().Sample(1000).Adapters[0];
            // oracle: nonpositive speed gives zero.
            Assert.AreEqual(0.0, a.SpeedMbps, 0.0);
        });
    }

    [TestMethod]
    public void Network_SpeedMalformed()
    {
        WithFixture(f =>
        {
            f.ProcFile("net/dev", Network("eth0", 0, 0));
            f.SysFile("class/net/eth0/speed", "fast");
            var a = f.Sampler().Sample(1000).Adapters[0];
            // oracle: unparseable speed gives zero.
            Assert.AreEqual(0.0, a.SpeedMbps, 0.0);
        });
    }

    [TestMethod]
    public void Network_SpeedAboveIntMax()
    {
        WithFixture(f =>
        {
            f.ProcFile("net/dev", Network("eth0", 0, 0));
            f.SysFile("class/net/eth0/speed", "2147483648");
            var a = f.Sampler().Sample(1000).Adapters[0];
            // oracle: int.MaxValue+1 cannot parse as int.
            Assert.AreEqual(0.0, a.SpeedMbps, 0.0);
        });
    }

    [TestMethod]
    public void Network_SpeedAtIntMax()
    {
        WithFixture(f =>
        {
            f.ProcFile("net/dev", Network("eth0", 0, 0));
            f.SysFile("class/net/eth0/speed", "2147483647");
            var a = f.Sampler().Sample(1000).Adapters[0];
            // oracle: int.MaxValue is a valid positive speed.
            Assert.AreEqual(2147483647.0, a.SpeedMbps, 0.0);
        });
    }

    [TestMethod]
    public void Network_SpeedParsingUsesInvariantCulture()
    {
        WithFixture(f =>
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                culture.NumberFormat.PositiveSign = "p";
                CultureInfo.CurrentCulture = culture;
                f.ProcFile("net/dev", Network("eth0", 0, 0));
                f.SysFile("class/net/eth0/speed", " +1000 ");
                var a = f.Sampler().Sample(1000).Adapters[0];
                // oracle: invariant trimmed int.TryParse accepts +1000 even when current culture requires positive sign p.
                Assert.AreEqual(1000.0, a.SpeedMbps, 0.0);
            }
            finally { CultureInfo.CurrentCulture = original; }
        });
    }

    [TestMethod]
    public void Network_HostileDot_DoesNotReadPlantedSysFiles()
    {
        WithFixture(f =>
        {
            const string name = ".";
            f.SysDirectory("class/net");
            f.ProcFile("net/dev", Network(name, 1048576, 2097152));
            f.SysFile("class/net/./speed", "777");
            f.SysDirectory("class/net/./wireless");
            var adapters = f.Sampler().Sample(1000).Adapters;
            // oracle: hostile names keep identity/counters but cannot read the planted speed 777 or wireless directory.
            Assert.AreEqual(1, adapters.Count);
            Assert.AreEqual(name, adapters[0].Id);
            Assert.AreEqual(name, adapters[0].Name);
            Assert.AreEqual(1048576L, adapters[0].TotalReceived);
            Assert.AreEqual(2097152L, adapters[0].TotalSent);
            Assert.AreEqual(0.0, adapters[0].SpeedMbps, 0.0);
            Assert.IsFalse(adapters[0].Wireless);
        });
    }

    [TestMethod]
    public void Network_HostileDotDot_DoesNotReadPlantedSysFiles()
    {
        WithFixture(f =>
        {
            const string name = "..";
            f.SysDirectory("class/net");
            f.ProcFile("net/dev", Network(name, 1048576, 2097152));
            f.SysFile("class/net/../speed", "777");
            f.SysDirectory("class/net/../wireless");
            var adapters = f.Sampler().Sample(1000).Adapters;
            // oracle: hostile names keep identity/counters but cannot read the planted speed 777 or wireless directory.
            Assert.AreEqual(1, adapters.Count);
            Assert.AreEqual(name, adapters[0].Id);
            Assert.AreEqual(name, adapters[0].Name);
            Assert.AreEqual(1048576L, adapters[0].TotalReceived);
            Assert.AreEqual(2097152L, adapters[0].TotalSent);
            Assert.AreEqual(0.0, adapters[0].SpeedMbps, 0.0);
            Assert.IsFalse(adapters[0].Wireless);
        });
    }

    [TestMethod]
    public void Network_HostileSlash_DoesNotReadPlantedSysFiles()
    {
        WithFixture(f =>
        {
            const string name = "nested/evil";
            f.SysDirectory("class/net");
            f.ProcFile("net/dev", Network(name, 1048576, 2097152));
            f.SysFile("class/net/nested/evil/speed", "777");
            f.SysDirectory("class/net/nested/evil/wireless");
            var adapters = f.Sampler().Sample(1000).Adapters;
            // oracle: hostile names keep identity/counters but cannot read the planted speed 777 or wireless directory.
            Assert.AreEqual(1, adapters.Count);
            Assert.AreEqual(name, adapters[0].Id);
            Assert.AreEqual(name, adapters[0].Name);
            Assert.AreEqual(1048576L, adapters[0].TotalReceived);
            Assert.AreEqual(2097152L, adapters[0].TotalSent);
            Assert.AreEqual(0.0, adapters[0].SpeedMbps, 0.0);
            Assert.IsFalse(adapters[0].Wireless);
        });
    }

    [TestMethod]
    public void Network_HostileTraversal_DoesNotReadPlantedSysFiles()
    {
        WithFixture(f =>
        {
            const string name = "../escape";
            f.SysDirectory("class/net");
            f.ProcFile("net/dev", Network(name, 1048576, 2097152));
            f.SysFile("class/net/../escape/speed", "777");
            f.SysDirectory("class/net/../escape/wireless");
            var adapters = f.Sampler().Sample(1000).Adapters;
            // oracle: hostile names keep identity/counters but cannot read the planted speed 777 or wireless directory.
            Assert.AreEqual(1, adapters.Count);
            Assert.AreEqual(name, adapters[0].Id);
            Assert.AreEqual(name, adapters[0].Name);
            Assert.AreEqual(1048576L, adapters[0].TotalReceived);
            Assert.AreEqual(2097152L, adapters[0].TotalSent);
            Assert.AreEqual(0.0, adapters[0].SpeedMbps, 0.0);
            Assert.IsFalse(adapters[0].Wireless);
        });
    }

    [TestMethod]
    public void Network_HostileBackslash_DoesNotReadPlantedSysFiles()
    {
        WithFixture(f =>
        {
            const string name = "bad\\name";
            f.SysDirectory("class/net");
            f.ProcFile("net/dev", Network(name, 1048576, 2097152));
            f.SysFile("class/net/bad\\name/speed", "777");
            f.SysDirectory("class/net/bad\\name/wireless");
            var adapters = f.Sampler().Sample(1000).Adapters;
            // oracle: hostile names keep identity/counters but cannot read the planted speed 777 or wireless directory.
            Assert.AreEqual(1, adapters.Count);
            Assert.AreEqual(name, adapters[0].Id);
            Assert.AreEqual(name, adapters[0].Name);
            Assert.AreEqual(1048576L, adapters[0].TotalReceived);
            Assert.AreEqual(2097152L, adapters[0].TotalSent);
            Assert.AreEqual(0.0, adapters[0].SpeedMbps, 0.0);
            Assert.IsFalse(adapters[0].Wireless);
        });
    }

    [TestMethod]
    public void Network_NulName_PreservesIdentityWithoutThrowing()
    {
        WithFixture(f =>
        {
            const string name = "bad\0name";
            f.ProcFile("net/dev", Network(name, 0, 0));
            var adapters = f.Sampler().Sample(1000).Adapters;
            // oracle: NUL-containing names retain Id/Name and have false Wireless/zero SpeedMbps.
            Assert.AreEqual(1, adapters.Count);
            Assert.AreEqual(name, adapters[0].Id);
            Assert.AreEqual(name, adapters[0].Name);
            Assert.IsFalse(adapters[0].Wireless);
            Assert.AreEqual(0.0, adapters[0].SpeedMbps, 0.0);
        });
    }

    [TestMethod]
    public void Hwmon_FirstCall_ParsesWhitespacePaddedMilliDegreesAndDelegatesSelection()
    {
        WithFixture(f =>
        {
            f.SysFile("class/hwmon/hwmon0/name", " \tcoretemp\r\n");
            f.SysFile("class/hwmon/hwmon0/temp1_input", " \t42000\r\n");
            var expected = HwmonThermal.PickCpu(new[] { ("coretemp", 42000) });
            var s = f.Sampler().Sample(1000);
            // oracle: first-call temperature delegates parsed 42000 milli-degrees to PickCpu; PickCpu also trims, so this does not prove sampler name trimming.
            Assert.AreEqual(expected, s.CpuTemp);
            Assert.IsFalse(s.CpuTempEstimated);
            Assert.IsFalse(s.Ready);
        });
    }

    [TestMethod]
    public void Hwmon_MultipleZones_MatchesPickCpuWithOrdinalInput()
    {
        WithFixture(f =>
        {
            // Reverse creation order also distinguishes numeric sorting from ordinal sorting.
            f.SysFile("class/hwmon/hwmon2/name", "coretemp");
            f.SysFile("class/hwmon/hwmon2/temp1_input", "62000");
            f.SysFile("class/hwmon/hwmon10/name", "coretemp");
            f.SysFile("class/hwmon/hwmon10/temp1_input", "41000");
            f.SysFile("class/hwmon/hwmon1/name", "k10temp");
            f.SysFile("class/hwmon/hwmon1/temp1_input", "53000");
            var expected = HwmonThermal.PickCpu(new[] { ("k10temp", 53000), ("coretemp", 41000), ("coretemp", 62000) });
            var s = f.Sampler().Sample(1000);
            // oracle: ordinal directory order hwmon1,hwmon10,hwmon2 is the specified PickCpu input; each zone has a different value.
            Assert.AreEqual(expected, s.CpuTemp);
        });
    }

    [TestMethod]
    public void Hwmon_MalformedOverflowAndMissingFiles_AreSkipped()
    {
        WithFixture(f =>
        {
            f.SysFile("class/hwmon/hwmon0/name", "coretemp");
            f.SysFile("class/hwmon/hwmon0/temp1_input", "not-an-int");
            f.SysFile("class/hwmon/hwmon1/name", "coretemp");
            f.SysFile("class/hwmon/hwmon1/temp1_input", "2147483648");
            f.SysFile("class/hwmon/hwmon2/name", "coretemp");
            f.SysFile("class/hwmon/hwmon3/temp1_input", "42000");
            f.SysFile("class/hwmon/hwmon4/name", "coretemp");
            f.SysFile("class/hwmon/hwmon4/temp1_input", "45000");
            var expected = HwmonThermal.PickCpu(new[] { ("coretemp", 45000) });
            var s = f.Sampler().Sample(1000);
            // oracle: only hwmon4 has both readable files and an int-parsable temperature.
            Assert.AreEqual(expected, s.CpuTemp);
        });
    }

    [TestMethod]
    public void Hwmon_NonMatchingDirectory_IsIgnoredDespiteReadableFiles()
    {
        WithFixture(f =>
        {
            f.SysFile("class/hwmon/other0/name", "coretemp");
            f.SysFile("class/hwmon/other0/temp1_input", "99000");
            var s = f.Sampler().Sample(1000);
            // oracle: only hwmon* directories are visited; other0 cannot contribute its planted temperature.
            Assert.IsNull(s.CpuTemp);
        });
    }

    [TestMethod]
    public void Cpu_VanishingFileDropsBaselineBeforeReappearance()
    {
        WithFixture(f =>
        {
            f.ProcFile("stat", "cpu 0 0 0 100\ncpu0 0 0 0 100\n");
            var sampler = f.Sampler();
            sampler.Sample(1000);
            File.Delete(Path.Combine(f.Proc, "stat"));
            var missing = sampler.Sample(2000);
            // oracle: absent stat contributes no cores and zero aggregate usage.
            Assert.AreEqual(0, missing.Cores.Count);
            Assert.AreEqual(0, missing.CpuTotal);
            f.ProcFile("stat", "cpu 100 0 0 100\ncpu0 100 0 0 100\n");
            var restored = sampler.Sample(3000);
            // oracle: missing stat dropped its baseline, so restored CPU usage is zero and Ready stays true.
            Assert.IsTrue(restored.Ready);
            Assert.AreEqual(1, restored.Cores.Count);
            Assert.AreEqual(0, restored.Cores[0].Usage);
            Assert.AreEqual(0, restored.CpuTotal);
        });
    }

    [TestMethod]
    public void Disk_VanishingFileDropsBaselineBeforeReappearance()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            f.ProcFile("diskstats", DiskLine("sda", 0, 0));
            var sampler = f.Sampler();
            sampler.Sample(1000);
            File.Delete(Path.Combine(f.Proc, "diskstats"));
            var missing = sampler.Sample(2000);
            // oracle: missing diskstats leaves the mounted disk present with zero rates.
            Assert.AreEqual(1, missing.Disks.Count);
            Assert.AreEqual(0.0, missing.Disks[0].ReadMb, 0.0);
            Assert.AreEqual(0.0, missing.Disks[0].WriteMb, 0.0);
            f.ProcFile("diskstats", DiskLine("sda", 2048, 4096));
            var restored = sampler.Sample(3000);
            // oracle: missing diskstats dropped prior counters; reappearing parent has no baseline.
            Assert.IsTrue(restored.Ready);
            Assert.AreEqual(0.0, restored.Disks[0].ReadMb, 0.0);
            Assert.AreEqual(0.0, restored.Disks[0].WriteMb, 0.0);
        });
    }

    [TestMethod]
    public void Network_VanishingFileDropsBaselineBeforeReappearance()
    {
        WithFixture(f =>
        {
            f.ProcFile("net/dev", Network("eth0", 0, 0));
            var sampler = f.Sampler();
            sampler.Sample(1000);
            File.Delete(Path.Combine(f.Proc, "net/dev"));
            var missing = sampler.Sample(2000);
            // oracle: absent net/dev contributes no adapters.
            Assert.AreEqual(0, missing.Adapters.Count);
            f.ProcFile("net/dev", Network("eth0", 1048576, 2097152));
            var restored = sampler.Sample(3000);
            // oracle: missing net/dev dropped the baseline; restored interface starts at zero rates.
            Assert.IsTrue(restored.Ready);
            Assert.AreEqual(1, restored.Adapters.Count);
            Assert.AreEqual(0.0, restored.Adapters[0].DownMb, 0.0);
            Assert.AreEqual(0.0, restored.Adapters[0].UpMb, 0.0);
        });
    }

    [TestMethod]
    public void Network_InterfaceAbsentForOneCall_DropsItsOwnBaseline()
    {
        WithFixture(f =>
        {
            f.ProcFile("net/dev", Network("eth0", 0, 0) + Network("eth1", 0, 0));
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("net/dev", Network("eth1", 1048576, 2097152));
            sampler.Sample(2000);
            f.ProcFile("net/dev", Network("eth0", 2097152, 4194304) + Network("eth1", 2097152, 4194304));
            var adapters = sampler.Sample(3000).Adapters;
            var restored = adapters.Single(a => a.Id == "eth0");
            var continuous = adapters.Single(a => a.Id == "eth1");
            // oracle: absent eth0 loses its baseline; continuously present eth1 deltas are 1/2 MiB over 1s.
            Assert.AreEqual(0.0, restored.DownMb, 0.0);
            Assert.AreEqual(0.0, restored.UpMb, 0.0);
            Assert.AreEqual(1.0, continuous.DownMb, 0.0);
            Assert.AreEqual(2.0, continuous.UpMb, 0.0);
        });
    }

    [TestMethod]
    public void Disk_CounterAbsentForOneCall_DropsItsOwnBaseline()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            f.ProcFile("diskstats", DiskLine("sda", 0, 0));
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("diskstats", DiskLine("sdb", 2048, 4096));
            sampler.Sample(2000);
            f.ProcFile("diskstats", DiskLine("sda", 4096, 8192));
            var d = sampler.Sample(3000).Disks[0];
            // oracle: sda absent from the preceding parsed list has no retained baseline.
            Assert.AreEqual(0.0, d.ReadMb, 0.0);
            Assert.AreEqual(0.0, d.WriteMb, 0.0);
        });
    }

    [TestMethod]
    public void Ram_VanishingFileBecomesEmptyWithoutKeepingOldValues()
    {
        WithFixture(f =>
        {
            f.ProcFile("meminfo", "MemTotal: 8388608 kB\nMemAvailable: 2097152 kB\n");
            var sampler = f.Sampler();
            sampler.Sample(1000);
            File.Delete(Path.Combine(f.Proc, "meminfo"));
            var s = sampler.Sample(2000);
            // oracle: missing meminfo contributes new empty RAM instead of stale values.
            Assert.AreEqual(0, s.Ram.Usage);
            Assert.AreEqual(0.0, s.Ram.UsedGb, 0.0);
            Assert.AreEqual(0.0, s.Ram.TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void Hwmon_TemperatureFileVanishesBetweenCalls_IsSkipped()
    {
        WithFixture(f =>
        {
            f.SysFile("class/hwmon/hwmon0/name", "coretemp");
            f.SysFile("class/hwmon/hwmon0/temp1_input", "42000");
            var sampler = f.Sampler();
            sampler.Sample(1000);
            File.Delete(Path.Combine(f.Sys, "class/hwmon/hwmon0/temp1_input"));
            var s = sampler.Sample(2000);
            // oracle: a zone missing temp1_input is skipped and no previous CpuTemp is retained.
            Assert.IsNull(s.CpuTemp);
            Assert.IsFalse(s.CpuTempEstimated);
        });
    }

    [TestMethod]
    public void Time_StandingStill_ZeroesDiskAndNetworkRates()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            f.ProcFile("diskstats", DiskLine("sda", 0, 0));
            f.ProcFile("net/dev", Network("eth0", 0, 0));
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("diskstats", DiskLine("sda", 2048, 4096));
            f.ProcFile("net/dev", Network("eth0", 1048576, 2097152));
            var s = sampler.Sample(1000);
            // oracle: zero elapsed seconds forces zero disk/network rates despite positive deltas; Ready remains true.
            Assert.IsTrue(s.Ready);
            Assert.AreEqual(0.0, s.Disks[0].ReadMb, 0.0);
            Assert.AreEqual(0.0, s.Disks[0].WriteMb, 0.0);
            Assert.AreEqual(0.0, s.Adapters[0].DownMb, 0.0);
            Assert.AreEqual(0.0, s.Adapters[0].UpMb, 0.0);
        });
    }

    [TestMethod]
    public void Time_Backwards_ZeroesDiskAndNetworkRates()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            f.ProcFile("diskstats", DiskLine("sda", 0, 0));
            f.ProcFile("net/dev", Network("eth0", 0, 0));
            var sampler = f.Sampler();
            sampler.Sample(2000);
            f.ProcFile("diskstats", DiskLine("sda", 2048, 4096));
            f.ProcFile("net/dev", Network("eth0", 1048576, 2097152));
            var s = sampler.Sample(1000);
            // oracle: negative elapsed seconds forces zero disk/network rates despite positive deltas.
            Assert.IsTrue(s.Ready);
            Assert.AreEqual(0.0, s.Disks[0].ReadMb, 0.0);
            Assert.AreEqual(0.0, s.Disks[0].WriteMb, 0.0);
            Assert.AreEqual(0.0, s.Adapters[0].DownMb, 0.0);
            Assert.AreEqual(0.0, s.Adapters[0].UpMb, 0.0);
        });
    }

    [TestMethod]
    public void Time_SubsecondInterval_IsComputedAsDouble()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            f.ProcFile("diskstats", DiskLine("sda", 0, 0));
            f.ProcFile("net/dev", Network("eth0", 0, 0));
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("diskstats", DiskLine("sda", 1024, 2048));
            f.ProcFile("net/dev", Network("eth0", 524288, 1048576));
            var s = sampler.Sample(1500);
            // oracle: seconds=500/1000.0=0.5; deltas 0.5/1 MiB give 1/2 MiB/s.
            Assert.AreEqual(1.0, s.Disks[0].ReadMb, 0.0);
            Assert.AreEqual(2.0, s.Disks[0].WriteMb, 0.0);
            Assert.AreEqual(1.0, s.Adapters[0].DownMb, 0.0);
            Assert.AreEqual(2.0, s.Adapters[0].UpMb, 0.0);
        });
    }

    [TestMethod]
    public void Time_LongExtremes_DoNotOverflowBeforeSubtraction()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            f.ProcFile("diskstats", DiskLine("sda", 0, 0));
            f.ProcFile("net/dev", Network("eth0", 0, 0));
            var sampler = f.Sampler();
            sampler.Sample(long.MinValue);
            f.ProcFile("diskstats", DiskLine("sda", 2048, 4096));
            f.ProcFile("net/dev", Network("eth0", 1048576, 2097152));
            var s = sampler.Sample(long.MaxValue);
            const double seconds = 18446744073709551616.0 / 1000.0;
            // oracle: double(max)-double(min)=2^64 ms; deltas are 1/2 MiB, so rates divide by (2^64/1000) seconds.
            Assert.AreEqual(1048576.0 / seconds / 1048576.0, s.Disks[0].ReadMb, 0.0);
            Assert.AreEqual(2097152.0 / seconds / 1048576.0, s.Disks[0].WriteMb, 0.0);
            Assert.AreEqual(1048576.0 / seconds / 1048576.0, s.Adapters[0].DownMb, 0.0);
            Assert.AreEqual(2097152.0 / seconds / 1048576.0, s.Adapters[0].UpMb, 0.0);
        });
    }

    [TestMethod]
    public void Time_NonAdvancingCallStillReplacesCounterBaseline()
    {
        WithFixture(f =>
        {
            f.ProcFile("net/dev", Network("eth0", 0, 0));
            var sampler = f.Sampler();
            sampler.Sample(1000);
            f.ProcFile("net/dev", Network("eth0", 1048576, 2097152));
            sampler.Sample(1000);
            f.ProcFile("net/dev", Network("eth0", 3145728, 6291456));
            var a = sampler.Sample(2000).Adapters[0];
            // oracle: previous call supplies the baseline even at equal time; new deltas 2/4 MiB over 1s give 2/4.
            Assert.AreEqual(2.0, a.DownMb, 0.0);
            Assert.AreEqual(4.0, a.UpMb, 0.0);
        });
    }

    [TestMethod]
    public void Modules_AreEmptyWithOtherDataPresent()
    {
        WithFixture(f =>
        {
            f.ProcFile("stat", "cpu 100 0 0 100\ncpu0 100 0 0 100\n");
            f.ProcFile("meminfo", "MemTotal: 8388608 kB\nMemAvailable: 2097152 kB\n");
            var sampler = f.Sampler();
            var first = sampler.Sample(1000);
            var second = sampler.Sample(2000);
            // oracle: modules are always empty in phase 4, independently of available data and readiness.
            Assert.AreEqual(0, first.Modules.Count);
            Assert.AreEqual(0, second.Modules.Count);
        });
    }

    [TestMethod]
    public void Roots_Null_DoNotReadPlantedRelativeFiles()
    {
        WithFixture(f => CheckInvalidRoots(f, null!));
    }

    [TestMethod]
    public void Roots_Empty_DoNotReadPlantedRelativeFiles()
    {
        WithFixture(f => CheckInvalidRoots(f, ""));
    }

    private static void CheckInvalidRoots(Fixture f, string invalidRoot)
    {
        File.WriteAllText(Path.Combine(f.Root, "stat"), "cpu 100 0 0 100\ncpu0 100 0 0 100\n");
        File.WriteAllText(Path.Combine(f.Root, "meminfo"), "MemTotal: 8388608 kB\nMemAvailable: 2097152 kB\n");
        Directory.CreateDirectory(Path.Combine(f.Root, "class/hwmon/hwmon0"));
        File.WriteAllText(Path.Combine(f.Root, "class/hwmon/hwmon0/name"), "coretemp");
        File.WriteAllText(Path.Combine(f.Root, "class/hwmon/hwmon0/temp1_input"), "99000");
        string original = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = f.Root;
            var sampler = new LinuxSampler(invalidRoot, invalidRoot, _ => (0L, 0L));
            // oracle: null/empty roots act as nonexistent directories, despite readable relative stat/meminfo/hwmon files.
            AssertEmpty(sampler.Sample(1000), false);
            AssertEmpty(sampler.Sample(2000), true);
        }
        finally { Environment.CurrentDirectory = original; }
    }

    [TestMethod]
    public void Roots_NonexistentPaths_AreHandledWithoutThrowing()
    {
        WithFixture(f =>
        {
            var sampler = new LinuxSampler(Path.Combine(f.Root, "absent-proc"), Path.Combine(f.Root, "absent-sys"), _ => (0L, 0L));
            // oracle: nonexistent directories contribute no data and state still advances.
            AssertEmpty(sampler.Sample(1000), false);
            AssertEmpty(sampler.Sample(2000), true);
        });
    }

    [TestMethod]
    public void UnreadableFileShape_DirectoriesInPlaceOfFilesContributeNothing()
    {
        WithFixture(f =>
        {
            Directory.CreateDirectory(Path.Combine(f.Proc, "stat"));
            Directory.CreateDirectory(Path.Combine(f.Proc, "meminfo"));
            Directory.CreateDirectory(Path.Combine(f.Proc, "net/dev"));
            Directory.CreateDirectory(Path.Combine(f.Proc, "self/mountinfo"));
            Directory.CreateDirectory(Path.Combine(f.Proc, "diskstats"));
            // oracle: these paths cannot be read as files; each failed read contributes nothing and never throws.
            AssertEmpty(f.Sampler().Sample(1000), false);
        });
    }

    [TestMethod]
    public void Roots_NullSys_DoesNotSuppressValidProcData()
    {
        WithFixture(f =>
        {
            f.ProcFile("stat", "cpu 100 0 0 100\ncpu0 100 0 0 100\n");
            f.ProcFile("net/dev", Network("eth0", 1048576, 2097152));
            var s = new LinuxSampler(f.Proc, null!, _ => (0L, 0L)).Sample(1000);
            // oracle: nonexistent sys contributes no metadata, while valid proc still provides one core and one adapter.
            Assert.AreEqual(1, s.Cores.Count);
            Assert.AreEqual(1, s.Adapters.Count);
            Assert.AreEqual("eth0", s.Adapters[0].Id);
            Assert.AreEqual(1048576L, s.Adapters[0].TotalReceived);
            Assert.AreEqual(0.0, s.Adapters[0].SpeedMbps, 0.0);
            Assert.IsFalse(s.Adapters[0].Wireless);
            Assert.IsNull(s.CpuTemp);
        });
    }

    [TestMethod]
    public void Roots_EmptyProc_DoesNotSuppressValidSysData()
    {
        WithFixture(f =>
        {
            f.SysFile("class/hwmon/hwmon0/name", "coretemp");
            f.SysFile("class/hwmon/hwmon0/temp1_input", "42000");
            var expected = HwmonThermal.PickCpu(new[] { ("coretemp", 42000) });
            var s = new LinuxSampler("", f.Sys, _ => (0L, 0L)).Sample(1000);
            // oracle: empty proc contributes no cores; independent sys still supplies PickCpu's specified input.
            Assert.AreEqual(0, s.Cores.Count);
            Assert.AreEqual(expected, s.CpuTemp);
        });
    }

    [TestMethod]
    public void Roots_NulContainingPaths_ConstructorAndSampleNeverThrow()
    {
        WithFixture(f =>
        {
            var sampler = new LinuxSampler(Path.Combine(f.Root, "bad\0proc"), Path.Combine(f.Root, "bad\0sys"), _ => (0L, 0L));
            // oracle: constructor and Sample never throw; invalid paths cannot supply any data.
            AssertEmpty(sampler.Sample(1000), false);
            AssertEmpty(sampler.Sample(2000), true);
        });
    }

    [TestMethod]
    public void Sample_ConcurrentFirstCalls_SerializeCallbacksAndHaveExactlyOneNotReady()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            CounterFrame(f, 1);
            int active = 0;
            int overlapping = 0;
            int calls = 0;
            var sampler = f.Sampler(_ =>
            {
                if (Interlocked.Increment(ref active) > 1) Interlocked.Exchange(ref overlapping, 1);
                try
                {
                    Interlocked.Increment(ref calls);
                    Thread.SpinWait(100000);
                    return (4 * GiB, GiB);
                }
                finally { Interlocked.Decrement(ref active); }
            });
            var results = new Snapshot[32];
            // LongRunning tasks give four dedicated callers, independent of thread-pool availability.
            using var start = new Barrier(4);
            var tasks = new Task[4];
            for (int worker = 0; worker < tasks.Length; worker++)
            {
                int slot = worker;
                tasks[slot] = Task.Factory.StartNew(() =>
                {
                    start.SignalAndWait();
                    for (int call = 0; call < 8; call++)
                        results[slot * 8 + call] = sampler.Sample(1000);
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            Task.WaitAll(tasks);
            // oracle: round-3 (a) serializes all 32 calls; one mount means 32 non-overlapping callbacks and exactly one initial Ready=false.
            Assert.AreEqual(32, calls);
            Assert.AreEqual(0, overlapping);
            Assert.AreEqual(0, active);
            Assert.AreEqual(1, results.Count(s => !s.Ready));
            Assert.AreEqual(31, results.Count(s => s.Ready));
            foreach (var s in results)
            {
                // oracle: identical counters/timestamps give zero usages/rates in every call; capacity is (4-1)/4=75%, or 3 of 4 GiB.
                Assert.AreEqual(1, s.Cores.Count);
                Assert.AreEqual(0, s.CpuTotal);
                Assert.AreEqual(0, s.Cores[0].Usage);
                Assert.AreEqual(1, s.Disks.Count);
                Assert.AreEqual(0.0, s.Disks[0].ReadMb, 0.0);
                Assert.AreEqual(0.0, s.Disks[0].WriteMb, 0.0);
                Assert.AreEqual(75, s.Disks[0].Usage);
                Assert.AreEqual(3.0, s.Disks[0].UsedGb, 0.0);
                Assert.AreEqual(4.0, s.Disks[0].TotalGb, 0.0);
                Assert.AreEqual(1, s.Adapters.Count);
                Assert.AreEqual(0.0, s.Adapters[0].DownMb, 0.0);
                Assert.AreEqual(0.0, s.Adapters[0].UpMb, 0.0);
            }
        });
    }

    [TestMethod]
    public void Sample_ConcurrentCalls_KeepWholeSnapshotsAndMatchSequentialBaselineAfterThreeCalls()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            CounterFrame(f, 0);
            var concurrent = f.Sampler(_ =>
            {
                Thread.SpinWait(100000);
                return (4 * GiB, GiB);
            });
            var sequential = f.Sampler(_ => (4 * GiB, GiB));
            concurrent.Sample(1000);
            sequential.Sample(1000);
            CounterFrame(f, 1);
            var results = new Snapshot[32];
            // Files remain unchanged throughout the parallel region: only Sample state is under contention.
            Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 },
                i => results[i] = concurrent.Sample(2000));
            for (int i = 0; i < results.Length; i++) sequential.Sample(2000);
            // oracle: serialization consumes the old frame once: one CPU delta is 100/200=50%; the other 31 have zero deltas.
            Assert.AreEqual(1, results.Count(s => s.CpuTotal == 50));
            Assert.AreEqual(31, results.Count(s => s.CpuTotal == 0));
            foreach (var s in results)
            {
                if (s.CpuTotal == 50)
                {
                    // oracle: frame 0->1 over 1s gives disk 4096/8192 sectors=2/4 MiB/s and network 1/2 MiB/s in the SAME snapshot.
                    AssertCounterRates(s, 50, 2, 4, 1, 2);
                }
                else
                {
                    // oracle: all later overlapping calls see frame 1 at the same timestamp, so CPU and every rate are zero together.
                    AssertCounterRates(s, 0, 0, 0, 0, 0);
                }
            }
            for (int frame = 2; frame <= 4; frame++)
            {
                CounterFrame(f, frame);
                var actual = concurrent.Sample((frame + 1) * 1000L);
                var control = sequential.Sample((frame + 1) * 1000L);
                // oracle: each of N=3 sequential calls advances one frame/1s: 50% CPU, disk 2/4 and network 1/2; both runs must match these exact values.
                AssertCounterRates(actual, 50, 2, 4, 1, 2);
                AssertCounterRates(control, 50, 2, 4, 1, 2);
            }
        });
    }

    [TestMethod]
    public void FsUsage_ThrowsOnMiddleCall_OtherSectionsAndNextBaselinesSurvive()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            const string ramText = "MemTotal: 8388608 kB\nMemAvailable: 2097152 kB\n";
            f.ProcFile("meminfo", ramText);
            f.SysFile("class/hwmon/hwmon0/name", "coretemp");
            f.SysFile("class/hwmon/hwmon0/temp1_input", "42000");
            var expectedRam = MemInfo.Parse(ramText);
            var expectedTemp = HwmonThermal.PickCpu(new[] { ("coretemp", 42000) });
            bool fail = false;
            var sampler = f.Sampler(_ => fail ? throw new IOException("fixture capacity failure") : (4 * GiB, GiB));
            CounterFrame(f, 0);
            sampler.Sample(1000);
            fail = true;
            CounterFrame(f, 1);
            var failed = sampler.Sample(2000);
            // oracle: fsUsage failure keeps its Disk with zero capacity; frame 0->1 still gives 50% CPU, disk 2/4, network 1/2.
            AssertCounterRates(failed, 50, 2, 4, 1, 2);
            // oracle: the specific fsUsage exception rule sets all three capacity values to zero while keeping the disk.
            Assert.AreEqual(0, failed.Disks[0].Usage);
            Assert.AreEqual(0.0, failed.Disks[0].UsedGb, 0.0);
            Assert.AreEqual(0.0, failed.Disks[0].TotalGb, 0.0);
            // oracle: round-3 (b) isolates the callback failure; RAM and temperature still delegate the readable current files.
            Assert.AreEqual(expectedRam.Usage, failed.Ram.Usage);
            Assert.AreEqual(expectedRam.UsedGb, failed.Ram.UsedGb, 0.0);
            Assert.AreEqual(expectedRam.TotalGb, failed.Ram.TotalGb, 0.0);
            Assert.AreEqual(expectedTemp, failed.CpuTemp);
            Assert.IsFalse(failed.CpuTempEstimated);
            fail = false;
            CounterFrame(f, 2);
            var recovered = sampler.Sample(3000);
            // oracle: the middle call updated time and counters; frame 1->2 over 1s gives the same rates, not doubled stale-baseline rates.
            AssertCounterRates(recovered, 50, 2, 4, 1, 2);
            // oracle: recovered fsUsage supplies 4 GiB total and 1 free, giving 3 used and 3/4*100=75%.
            Assert.AreEqual(75, recovered.Disks[0].Usage);
            Assert.AreEqual(3.0, recovered.Disks[0].UsedGb, 0.0);
            Assert.AreEqual(4.0, recovered.Disks[0].TotalGb, 0.0);
        });
    }

    [TestMethod]
    public void Network_DuplicateNames_KeepEveryEntryUseFirstBaselineAndPreserveOtherSections()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            const string ramText = "MemTotal: 8388608 kB\nMemAvailable: 2097152 kB\n";
            f.ProcFile("meminfo", ramText);
            f.SysFile("class/hwmon/hwmon0/name", "coretemp");
            f.SysFile("class/hwmon/hwmon0/temp1_input", "42000");
            var expectedRam = MemInfo.Parse(ramText);
            var expectedTemp = HwmonThermal.PickCpu(new[] { ("coretemp", 42000) });
            CounterFrame(f, 0);
            f.ProcFile("net/dev", Network("eth0", 1048576, 2097152) + Network("eth0", 9437184, 12582912));
            var sampler = f.Sampler(_ => (4 * GiB, GiB));
            var first = sampler.Sample(1000);
            // oracle: round-3 (c) keeps both duplicate entries in order; first call has no baseline and is not Ready.
            Assert.IsFalse(first.Ready);
            Assert.AreEqual(2, first.Adapters.Count);
            Assert.AreEqual(1048576L, first.Adapters[0].TotalReceived);
            Assert.AreEqual(9437184L, first.Adapters[1].TotalReceived);
            foreach (var a in first.Adapters)
            {
                // oracle: both duplicate names preserve identity and have zero first-call rates.
                Assert.AreEqual("eth0", a.Id);
                Assert.AreEqual("eth0", a.Name);
                Assert.AreEqual(0.0, a.DownMb, 0.0);
                Assert.AreEqual(0.0, a.UpMb, 0.0);
            }
            CounterFrame(f, 1);
            f.ProcFile("net/dev", Network("eth0", 3145728, 6291456) + Network("eth0", 7340032, 10485760));
            var second = sampler.Sample(2000);
            // oracle: both current entries use FIRST previous (1,2) MiB: (3,6)-(1,2)=(2,4), (7,10)-(1,2)=(6,8), over 1s.
            Assert.IsTrue(second.Ready);
            Assert.AreEqual(2, second.Adapters.Count);
            Assert.AreEqual(2.0, second.Adapters[0].DownMb, 0.0);
            Assert.AreEqual(4.0, second.Adapters[0].UpMb, 0.0);
            Assert.AreEqual(6.0, second.Adapters[1].DownMb, 0.0);
            Assert.AreEqual(8.0, second.Adapters[1].UpMb, 0.0);
            Assert.AreEqual(3145728L, second.Adapters[0].TotalReceived);
            Assert.AreEqual(6291456L, second.Adapters[0].TotalSent);
            Assert.AreEqual(7340032L, second.Adapters[1].TotalReceived);
            Assert.AreEqual(10485760L, second.Adapters[1].TotalSent);
            // oracle: duplicates cannot wipe other sections: frame 0->1 yields CPU 50%, disk 2/4 and capacity 75%; RAM/temp still delegate.
            Assert.AreEqual(50, second.CpuTotal);
            Assert.AreEqual(1, second.Cores.Count);
            Assert.AreEqual(50, second.Cores[0].Usage);
            Assert.AreEqual(1, second.Disks.Count);
            Assert.AreEqual(2.0, second.Disks[0].ReadMb, 0.0);
            Assert.AreEqual(4.0, second.Disks[0].WriteMb, 0.0);
            Assert.AreEqual(75, second.Disks[0].Usage);
            Assert.AreEqual(expectedRam.Usage, second.Ram.Usage);
            Assert.AreEqual(expectedRam.UsedGb, second.Ram.UsedGb, 0.0);
            Assert.AreEqual(expectedRam.TotalGb, second.Ram.TotalGb, 0.0);
            Assert.AreEqual(expectedTemp, second.CpuTemp);
            CounterFrame(f, 2);
            f.ProcFile("net/dev", Network("eth0", 8388608, 11534336));
            var third = sampler.Sample(3000);
            // oracle: next baseline is FIRST second-call entry (3,6), so (8,11)-(3,6)=(5,5) MiB/1s; last-entry baseline would give (1,1).
            AssertCounterRates(third, 50, 2, 4, 5, 5);
        });
    }

    [TestMethod]
    public void Network_NullSysRoot_DoesNotReadPlantedRelativeWirelessOrSpeed()
    {
        // oracle: round-3 (d) treats null sysRoot as nonexistent even with planted readable relative metadata.
        WithFixture(f => CheckInvalidSysRootNetwork(f, null!));
    }

    [TestMethod]
    public void Network_EmptySysRoot_DoesNotReadPlantedRelativeWirelessOrSpeed()
    {
        // oracle: round-3 (d) treats empty sysRoot as nonexistent even with planted readable relative metadata.
        WithFixture(f => CheckInvalidSysRootNetwork(f, ""));
    }

    private static void CheckInvalidSysRootNetwork(Fixture f, string invalidSysRoot)
    {
        f.ProcFile("net/dev", Network("eth0", 1048576, 2097152));
        Directory.CreateDirectory(Path.Combine(f.Root, "class/net/eth0/wireless"));
        File.WriteAllText(Path.Combine(f.Root, "class/net/eth0/speed"), "777");
        string original = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = f.Root;
            var sampler = new LinuxSampler(f.Proc, invalidSysRoot, _ => (0L, 0L));
            for (int call = 0; call < 2; call++)
            {
                var s = sampler.Sample(1000L * (call + 1));
                // oracle: round-3 (d) prohibits relative sys access even with readable class/net/eth0/wireless and speed=777 planted in CWD.
                Assert.AreEqual(call != 0, s.Ready);
                Assert.AreEqual(1, s.Adapters.Count);
                Assert.AreEqual("eth0", s.Adapters[0].Id);
                Assert.AreEqual("eth0", s.Adapters[0].Name);
                Assert.IsFalse(s.Adapters[0].Wireless);
                Assert.AreEqual(0.0, s.Adapters[0].SpeedMbps, 0.0);
                Assert.AreEqual(1048576L, s.Adapters[0].TotalReceived);
                Assert.AreEqual(2097152L, s.Adapters[0].TotalSent);
            }
        }
        finally { Environment.CurrentDirectory = original; }
    }

    [TestMethod]
    public void Disks_MappedAndUnmapped_AreNeverEstimatedAndHaveNoTemperature()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/mapped", "ext4"), ("/dev/mapper/volume", "/unmapped", "xfs"));
            f.SysFile("class/hwmon/hwmon0/name", "drivetemp");
            f.SysFile("class/hwmon/hwmon0/temp1_input", "99000");
            var sampler = f.Sampler(_ => (4 * GiB, GiB));
            for (int call = 1; call <= 2; call++)
            {
                var s = sampler.Sample(call * 1000L);
                // oracle: round-4 (iii) sets Estimated=false and Temp=null for EVERY produced disk, on first and later calls.
                Assert.AreEqual(2, s.Disks.Count);
                foreach (var disk in s.Disks)
                {
                    // oracle: disk temperature is unfilled even when a drive-temperature zone is present; neither disk is estimated.
                    Assert.IsFalse(disk.Estimated);
                    Assert.IsNull(disk.Temp);
                }
                // oracle: round-4 (ii) gives an unmapped device neither a parent ordinal nor a PhysicalDisk.
                Assert.IsNotNull(s.Disks[1].Detail);
                Assert.IsNull(s.Disks[1].Detail!.DiskNumber);
                Assert.IsNull(s.Disks[1].Detail!.Disk);
            }
        });
    }

    [TestMethod]
    public void Disks_RootAppearingLater_DoesNotReorderMountsOrParentNumbers()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sdb2", "/data", "xfs"), ("/dev/sda1", "/", "ext4"));
            var disks = f.Sampler().Sample(1000).Disks;
            // oracle: round-3 (h) leaves root-first sorting to phase 5; mount order is /data then / and parent ordinals are 0 then 1.
            Assert.AreEqual(2, disks.Count);
            Assert.AreEqual("/data", disks[0].Letter);
            Assert.AreEqual("/", disks[1].Letter);
            Assert.AreEqual((int?)0, disks[0].Detail!.DiskNumber);
            Assert.AreEqual((int?)1, disks[1].Detail!.DiskNumber);
        });
    }

    private static void AssertExcludedHwmonName(string name)
    {
        WithFixture(f =>
        {
            f.SysFile("class/hwmon/hwmon0/name", name);
            f.SysFile("class/hwmon/hwmon0/temp1_input", "99000");
            var sampler = f.Sampler();
            var first = sampler.Sample(1000);
            var second = sampler.Sample(2000);
            // oracle: round-3 (f) excludes this sole zone before PickCpu; no eligible zones means null CpuTemp and no estimate on either call.
            Assert.IsNull(first.CpuTemp);
            Assert.IsNull(second.CpuTemp);
            Assert.IsFalse(first.CpuTempEstimated);
            Assert.IsFalse(second.CpuTempEstimated);
        });
    }

    [TestMethod]
    public void Hwmon_NvmeZoneAlone_IsExcluded()
    {
        // oracle: round-3 (f) excludes names starting with nvme.
        AssertExcludedHwmonName("nvme");
    }

    [TestMethod]
    public void Hwmon_DrivetempZoneAlone_IsExcluded()
    {
        // oracle: round-3 (f) excludes names starting with drivetemp.
        AssertExcludedHwmonName("drivetemp");
    }

    [TestMethod]
    public void Hwmon_AmdgpuZoneAlone_IsExcluded()
    {
        // oracle: round-3 (f) excludes names starting with amdgpu.
        AssertExcludedHwmonName("amdgpu");
    }

    [TestMethod]
    public void Hwmon_NouveauZoneAlone_IsExcluded()
    {
        // oracle: round-3 (f) excludes names starting with nouveau.
        AssertExcludedHwmonName("nouveau");
    }

    [TestMethod]
    public void Hwmon_RadeonZoneAlone_IsExcluded()
    {
        // oracle: round-3 (f) excludes names starting with radeon.
        AssertExcludedHwmonName("radeon");
    }

    [TestMethod]
    public void Hwmon_IwlwifiSuffixedZoneAlone_IsExcluded()
    {
        // oracle: round-3 (f) uses prefix matching, so iwlwifi_1 is excluded.
        AssertExcludedHwmonName("iwlwifi_1");
    }

    [TestMethod]
    public void Hwmon_Ath10kSuffixedZoneAlone_IsExcluded()
    {
        // oracle: round-3 (f) excludes the ath1 prefix, including ath10k_hwmon.
        AssertExcludedHwmonName("ath10k_hwmon");
    }

    [TestMethod]
    public void Hwmon_BatteryUppercaseZoneAlone_IsExcluded()
    {
        // oracle: round-3 (f) matches the bat prefix case-insensitively, including BAT0.
        AssertExcludedHwmonName("BAT0");
    }

    [TestMethod]
    public void Hwmon_UcsiSuffixedZoneAlone_IsExcluded()
    {
        // oracle: round-3 (f) excludes the ucsi prefix, including ucsi_source_psy.
        AssertExcludedHwmonName("ucsi_source_psy");
    }

    [TestMethod]
    public void Hwmon_UppercaseNvmeZoneAlone_IsExcluded()
    {
        // oracle: round-3 (f) uses ordinal case-insensitive matching, so NVME is excluded like nvme.
        AssertExcludedHwmonName("NVME");
    }

    [TestMethod]
    public void Hwmon_LeadingAndTrailingWhitespaceAroundExcludedName_IsRemovedBeforeFiltering()
    {
        // oracle: round-3 (f) filters the TRIMMED name; leading whitespace prevents an untrimmed StartsWith(nvme) match.
        AssertExcludedHwmonName(" \tnvme\n");
    }

    [TestMethod]
    public void Hwmon_CpuZoneBesideHotterNvme_UsesOnlyCpuZone()
    {
        WithFixture(f =>
        {
            f.SysFile("class/hwmon/hwmon0/name", "nvme");
            f.SysFile("class/hwmon/hwmon0/temp1_input", "99000");
            f.SysFile("class/hwmon/hwmon1/name", "coretemp");
            f.SysFile("class/hwmon/hwmon1/temp1_input", "42000");
            var expected = HwmonThermal.PickCpu(new[] { ("coretemp", 42000) });
            var s = f.Sampler().Sample(1000);
            // oracle: round-3 (f) removes the hotter 99000 nvme zone; PickCpu receives only coretemp at 42000.
            Assert.AreEqual(expected, s.CpuTemp);
            Assert.IsNotNull(s.CpuTemp);
            Assert.IsFalse(s.CpuTempEstimated);
        });
    }

    [TestMethod]
    public void Hwmon_TrimmedCpuName_RemainsEligibleBesideTrimmedExcludedZone()
    {
        WithFixture(f =>
        {
            f.SysFile("class/hwmon/hwmon0/name", " \tnvme\n");
            f.SysFile("class/hwmon/hwmon0/temp1_input", "99000");
            f.SysFile("class/hwmon/hwmon1/name", " coretemp\n");
            f.SysFile("class/hwmon/hwmon1/temp1_input", "42000");
            var expected = HwmonThermal.PickCpu(new[] { ("coretemp", 42000) });
            var s = f.Sampler().Sample(1000);
            // oracle: trimming excludes nvme but keeps coretemp eligible; selection delegates only (coretemp,42000), never the excluded 99000 input.
            Assert.AreEqual(expected, s.CpuTemp);
            Assert.IsNotNull(s.CpuTemp);
            Assert.IsFalse(s.CpuTempEstimated);
        });
    }

    [TestMethod]
    public void Hwmon_LeadingPlusMilliDegrees_IsAcceptedInvariantly()
    {
        WithFixture(f =>
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                culture.NumberFormat.PositiveSign = "p";
                CultureInfo.CurrentCulture = culture;
                f.SysFile("class/hwmon/hwmon0/name", "coretemp");
                f.SysFile("class/hwmon/hwmon0/temp1_input", " \t+42000\r\n");
                var expected = HwmonThermal.PickCpu(new[] { ("coretemp", 42000) });
                var s = f.Sampler().Sample(1000);
                // oracle: round-4 (iv) accepts +42000 after Trim with invariant parsing even when the current culture's positive sign is p.
                Assert.AreEqual(expected, s.CpuTemp);
                Assert.IsNotNull(s.CpuTemp);
                Assert.IsFalse(s.CpuTempEstimated);
            }
            finally { CultureInfo.CurrentCulture = original; }
        });
    }

    [TestMethod]
    public void Hwmon_AllExcludedPrefixesTogether_HaveNoCpuTemperature()
    {
        WithFixture(f =>
        {
            string[] names = ["nvme", "drivetemp", "amdgpu", "nouveau", "radeon", "iwlwifi_1", "ath10k_hwmon", "BAT0", "ucsi_source_psy"];
            for (int i = 0; i < names.Length; i++)
            {
                f.SysFile($"class/hwmon/hwmon{i}/name", names[i]);
                f.SysFile($"class/hwmon/hwmon{i}/temp1_input", "99000");
            }
            var s = f.Sampler().Sample(1000);
            // oracle: round-3 (f) removes ALL nine non-CPU zones, leaving no PickCpu inputs and therefore null, never an estimate.
            Assert.IsNull(s.CpuTemp);
            Assert.IsFalse(s.CpuTempEstimated);
        });
    }

    [TestMethod]
    public void Cpu_UnreadableMiddleCall_DropsOnlyCpuBaselineAndKeepsOtherRates()
    {
        WithFixture(f =>
        {
            f.Mounts(("/dev/sda1", "/data", "ext4"));
            const string ramText = "MemTotal: 8388608 kB\nMemAvailable: 2097152 kB\n";
            f.ProcFile("meminfo", ramText);
            f.SysFile("class/hwmon/hwmon0/name", "coretemp");
            f.SysFile("class/hwmon/hwmon0/temp1_input", "42000");
            var expectedRam = MemInfo.Parse(ramText);
            var expectedTemp = HwmonThermal.PickCpu(new[] { ("coretemp", 42000) });
            CounterFrame(f, 0);
            var sampler = f.Sampler(_ => (4 * GiB, GiB));
            sampler.Sample(1000);
            CounterFrame(f, 1);
            string statPath = Path.Combine(f.Proc, "stat");
            File.Delete(statPath);
            Directory.CreateDirectory(statPath);
            var failed = sampler.Sample(2000);
            // oracle: a directory cannot be read as stat; round-3 (b) zeroes only CPU, leaving current disk/network deltas and RAM/temperature intact.
            Assert.IsTrue(failed.Ready);
            Assert.AreEqual(0, failed.CpuTotal);
            Assert.AreEqual(0, failed.Cores.Count);
            Assert.AreEqual(1, failed.Disks.Count);
            Assert.AreEqual(2.0, failed.Disks[0].ReadMb, 0.0);
            Assert.AreEqual(4.0, failed.Disks[0].WriteMb, 0.0);
            Assert.AreEqual(75, failed.Disks[0].Usage);
            Assert.AreEqual(1, failed.Adapters.Count);
            Assert.AreEqual(1.0, failed.Adapters[0].DownMb, 0.0);
            Assert.AreEqual(2.0, failed.Adapters[0].UpMb, 0.0);
            Assert.AreEqual(expectedRam.Usage, failed.Ram.Usage);
            Assert.AreEqual(expectedRam.UsedGb, failed.Ram.UsedGb, 0.0);
            Assert.AreEqual(expectedRam.TotalGb, failed.Ram.TotalGb, 0.0);
            Assert.AreEqual(expectedTemp, failed.CpuTemp);
            Directory.Delete(statPath);
            CounterFrame(f, 2);
            // oracle: only CPU lost its baseline, so restored CPU is zero while frame 1->2 over 1s keeps disk 2/4 and network 1/2.
            AssertCounterRates(sampler.Sample(3000), 0, 2, 4, 1, 2);
            CounterFrame(f, 3);
            // oracle: restored CPU now has a previous frame, giving 100/200=50%; disk/network continue using each immediately previous call.
            AssertCounterRates(sampler.Sample(4000), 50, 2, 4, 1, 2);
        });
    }
}
