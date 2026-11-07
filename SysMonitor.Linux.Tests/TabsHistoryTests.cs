using System;
using System.Globalization;
using System.Linq;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public class TabsHistoryTests
{
    private static void AssertSeries(History? actual, params double[] expected)
    {
        // oracle: Expected points are literal hand-derived values from the calling test, in record order.
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.Length, actual!.Count);
        var values = new double[expected.Length];
        // oracle: CopyTo copies Count points oldest-to-newest into an equally sized array and returns Count.
        Assert.AreEqual(expected.Length, actual.CopyTo(values));
        for (var i = 0; i < expected.Length; i++)
        {
            // oracle: Each copied point equals the corresponding literal expected point supplied by the test.
            Assert.AreEqual(expected[i], values[i], 0.000000001);
        }
    }

    [TestMethod]
    public void Tabs_NullSnapshotHasThreeNoDataTabs()
    {
        var tabs = FullTabs.Build(null!);
        // oracle: Null snapshot returns exactly CPU, Memory, Disk, each with the single line No data.
        CollectionAssert.AreEqual(new[] { "cpu", "memory", "disk" }, tabs.Select(x => x.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "CPU", "Memory", "Disk" }, tabs.Select(x => x.Title).ToArray());
        CollectionAssert.AreEqual(new[] { "No data" }, tabs[0].Lines.ToArray());
        CollectionAssert.AreEqual(new[] { "No data" }, tabs[1].Lines.ToArray());
        CollectionAssert.AreEqual(new[] { "No data" }, tabs[2].Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_EmptySnapshotAllBaseLines()
    {
        var tabs = FullTabs.Build(new Snapshot());
        // oracle: An empty non-null snapshot has zero base values, absent temperature/modules and an empty overview.
        CollectionAssert.AreEqual(new[] { "Total: 0%", "Temperature: N/A" }, tabs[0].Lines.ToArray());
        CollectionAssert.AreEqual(new[] { "Usage: 0%", "Used: 0 MB", "Total: 0 MB", "Modules: N/A (needs root)" }, tabs[1].Lines.ToArray());
        Assert.AreEqual(0, tabs[2].Lines.Count);
        Assert.AreEqual(3, tabs.Count);
    }

    [TestMethod]
    public void Tabs_BaseAndLogicalDiskOrder()
    {
        var tabs = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/z", TotalGb = 100, Label = "Label /z" }, new Disk { Letter = "/a", TotalGb = 100, Label = "Label /a" }, new Disk { Letter = "/", TotalGb = 100, Label = "Label /" }, new Disk { Letter = "/B", TotalGb = 100, Label = "Label /B" } } });
        // oracle: Base tabs first, followed by root-first ordinal logical disks; disk titles use labels.
        CollectionAssert.AreEqual(new[] { "cpu", "memory", "disk", "disk:/", "disk:/B", "disk:/a", "disk:/z" }, tabs.Select(x => x.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "CPU", "Memory", "Disk", "Label /", "Label /B", "Label /a", "Label /z" }, tabs.Select(x => x.Title).ToArray());
    }

    [TestMethod]
    public void Tabs_AdaptersDoNotCreateTabs()
    {
        var tabs = FullTabs.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100 } } });
        // oracle: Network is not a tab in phase 5.
        CollectionAssert.AreEqual(new[] { "cpu", "memory", "disk" }, tabs.Select(x => x.Key).ToArray());
    }

    [TestMethod]
    public void Tabs_CpuAllLines()
    {
        var tab = FullTabs.Build(new Snapshot { CpuTotal = 73, CpuTemp = 65, Cores = new[] { new Core { Usage = 10, Temp = 50 }, new Core { Usage = 100 }, new Core { Usage = 25, Temp = -5 } } })[0];
        // oracle: CPU total, temperature and zero-based cores follow source order; core temperatures have two spaces before them.
        CollectionAssert.AreEqual(new[] { "Total: 73%", "Temperature: 65 C", "Core 0: 10%  50 C", "Core 1: 100%", "Core 2: 25%  -5 C" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_CpuEstimatedAggregateTemperature()
    {
        var tab = FullTabs.Build(new Snapshot { CpuTemp = 51, CpuTempEstimated = true })[0];
        // oracle: CpuTempEstimated appends the estimated suffix.
        CollectionAssert.AreEqual(new[] { "Total: 0%", "Temperature: 51 C (estimated)" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_CpuEstimatedMissingAggregateTemperature()
    {
        var tab = FullTabs.Build(new Snapshot { CpuTempEstimated = true })[0];
        // oracle: The aggregate suffix depends on its flag even when CpuTemp is absent.
        CollectionAssert.AreEqual(new[] { "Total: 0%", "Temperature: N/A (estimated)" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_CoreEstimatedTemperature()
    {
        var tab = FullTabs.Build(new Snapshot { Cores = new[] { new Core { Usage = 10, Temp = 50, Estimated = true } } })[0];
        // oracle: The core estimated suffix follows its present temperature.
        CollectionAssert.AreEqual(new[] { "Total: 0%", "Temperature: N/A", "Core 0: 10%  50 C (estimated)" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_CoreEstimatedWithoutTemperatureHasNoSuffix()
    {
        var tab = FullTabs.Build(new Snapshot { Cores = new[] { new Core { Usage = 10, Estimated = true } } })[0];
        // oracle: No temperature means no core estimated suffix.
        CollectionAssert.AreEqual(new[] { "Total: 0%", "Temperature: N/A", "Core 0: 10%" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_CpuAndCoreUsageClamped()
    {
        var tab = FullTabs.Build(new Snapshot { CpuTotal = -1, Cores = new[] { new Core { Usage = int.MinValue }, new Core { Usage = int.MaxValue } } })[0];
        // oracle: Percent clamps total and each core to 0..100.
        CollectionAssert.AreEqual(new[] { "Total: 0%", "Temperature: N/A", "Core 0: 0%", "Core 1: 100%" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_CpuHighTotalClamped()
    {
        var tab = FullTabs.Build(new Snapshot { CpuTotal = int.MaxValue })[0];
        // oracle: Aggregate CPU above 100 clamps to 100%.
        CollectionAssert.AreEqual(new[] { "Total: 100%", "Temperature: N/A" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_NullCoreList()
    {
        var tab = FullTabs.Build(new Snapshot { Cores = null! })[0];
        // oracle: Null cores count as an empty list.
        CollectionAssert.AreEqual(new[] { "Total: 0%", "Temperature: N/A" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_MemoryAllLinesAndModuleOrder()
    {
        var tab = FullTabs.Build(new Snapshot { Ram = new Ram { Usage = 50, UsedGb = 8, TotalGb = 16 }, Modules = new[] { new Module { Slot = "B2", Gb = 8, Kind = "DDR5", Mhz = 6000 }, new Module { Slot = "A1", Gb = 8, Kind = "DDR4", Mhz = 3200 } } })[1];
        // oracle: Memory lines precede module lines; modules retain source order and use slot, size, kind and MHz.
        CollectionAssert.AreEqual(new[] { "Usage: 50%", "Used: 8.0 GB", "Total: 16.0 GB", "Slot B2: 8.0 GB DDR5 6000 MHz", "Slot A1: 8.0 GB DDR4 3200 MHz" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_MemoryNullRam()
    {
        var tab = FullTabs.Build(new Snapshot { Ram = null! })[1];
        // oracle: Null RAM becomes zero.
        CollectionAssert.AreEqual(new[] { "Usage: 0%", "Used: 0 MB", "Total: 0 MB", "Modules: N/A (needs root)" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_MemoryNullModules()
    {
        var tab = FullTabs.Build(new Snapshot { Modules = null! })[1];
        // oracle: Null modules count as empty and print the needs-root fallback.
        CollectionAssert.AreEqual(new[] { "Usage: 0%", "Used: 0 MB", "Total: 0 MB", "Modules: N/A (needs root)" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_MemoryNegativeUsageAndInvalidSizes()
    {
        var tab = FullTabs.Build(new Snapshot { Ram = new Ram { Usage = -1, UsedGb = double.NaN, TotalGb = double.PositiveInfinity } })[1];
        // oracle: Usage clamps; non-finite sizes are N/A.
        CollectionAssert.AreEqual(new[] { "Usage: 0%", "Used: N/A", "Total: N/A", "Modules: N/A (needs root)" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_MemoryHighUsageAndCarrySizes()
    {
        var tab = FullTabs.Build(new Snapshot { Ram = new Ram { Usage = 101, UsedGb = 1023.5 / 1024, TotalGb = 1023.95 } })[1];
        // oracle: Usage clamps to 100; sizes carry at 1023.5 MB and 1023.95 GB.
        CollectionAssert.AreEqual(new[] { "Usage: 100%", "Used: 1.0 GB", "Total: 1.0 TB", "Modules: N/A (needs root)" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_ModuleControlCharacters()
    {
        var tab = FullTabs.Build(new Snapshot { Modules = new[] { new Module { Slot = "\t A\n1\r", Kind = " DDR\u00005 ", Gb = 8, Mhz = 4800 } } })[1];
        // oracle: Slot and Kind controls become one space each, then Trim applies.
        CollectionAssert.AreEqual(new[] { "Usage: 0%", "Used: 0 MB", "Total: 0 MB", "Slot A 1: 8.0 GB DDR 5 4800 MHz" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_ModuleUnicodeIsPreserved()
    {
        var tab = FullTabs.Build(new Snapshot { Modules = new[] { new Module { Slot = "\u00e9", Kind = "\u65e5", Gb = 0.5, Mhz = 1 } } })[1];
        // oracle: Unicode module strings are retained; 0.5 GB = 512 MB.
        CollectionAssert.AreEqual(new[] { "Usage: 0%", "Used: 0 MB", "Total: 0 MB", "Slot \u00e9: 512 MB \u65e5 1 MHz" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_ModuleMalformedNumericFields()
    {
        var tab = FullTabs.Build(new Snapshot { Modules = new[] { new Module { Slot = "A", Kind = "DDR", Gb = -1, Mhz = -2 } } })[1];
        // oracle: Module size uses Fmt.Size; Mhz is printed as supplied in the specified template.
        CollectionAssert.AreEqual(new[] { "Usage: 0%", "Used: 0 MB", "Total: 0 MB", "Slot A: N/A DDR -2 MHz" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_OverviewAllLinesAndOrder()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/z", TotalGb = 2, Usage = 80 }, new Disk { Letter = "/", TotalGb = 10, Usage = 20 }, new Disk { Letter = "/a", TotalGb = 0.5, Usage = 50 } } })[2];
        // oracle: Overview is root-first ordinal and formats usage and total capacity.
        CollectionAssert.AreEqual(new[] { "/: 20% of 10.0 GB", "/a: 50% of 512 MB", "/z: 80% of 2.0 GB" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_FilterDiskZero()
    {
        var tabs = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 0 } } });
        // oracle: Disk filter drops !(TotalGb > 0) from overview and individual tabs.
        Assert.AreEqual(3, tabs.Count);
        Assert.AreEqual(0, tabs[2].Lines.Count);
    }

    [TestMethod]
    public void Tabs_FilterDiskNegative()
    {
        var tabs = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = -1 } } });
        // oracle: Disk filter drops !(TotalGb > 0) from overview and individual tabs.
        Assert.AreEqual(3, tabs.Count);
        Assert.AreEqual(0, tabs[2].Lines.Count);
    }

    [TestMethod]
    public void Tabs_FilterDiskNaN()
    {
        var tabs = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = double.NaN } } });
        // oracle: Disk filter drops !(TotalGb > 0) from overview and individual tabs.
        Assert.AreEqual(3, tabs.Count);
        Assert.AreEqual(0, tabs[2].Lines.Count);
    }

    [TestMethod]
    public void Tabs_FilterDiskNegativeInfinity()
    {
        var tabs = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = double.NegativeInfinity } } });
        // oracle: Disk filter drops !(TotalGb > 0) from overview and individual tabs.
        Assert.AreEqual(3, tabs.Count);
        Assert.AreEqual(0, tabs[2].Lines.Count);
    }

    [TestMethod]
    public void Tabs_PositiveInfinityDiskRetained()
    {
        var tabs = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = double.PositiveInfinity } } });
        // oracle: Positive infinity passes > 0 but formats as N/A.
        Assert.AreEqual(4, tabs.Count);
        CollectionAssert.AreEqual(new[] { "/data: 0% of N/A" }, tabs[2].Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_NullDiskList()
    {
        var tabs = FullTabs.Build(new Snapshot { Disks = null! });
        // oracle: Null disk list counts as empty.
        Assert.AreEqual(3, tabs.Count);
        Assert.AreEqual(0, tabs[2].Lines.Count);
    }

    [TestMethod]
    public void Tabs_PerDiskAllNineLines()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Label = "Data", Usage = 25, UsedGb = 25, ReadMb = 1.25, WriteMb = 0.5, Media = "SSD", Bus = "NVMe", Detail = new DriveDetail { Letter = "/data", FileSystem = "ext4", DiskNumber = 2, PartitionNumber = 3, Disk = new PhysicalDisk { Model = "Model X", Media = "HDD", Bus = "SATA" } } } } })[3];
        // oracle: All nine lines are ordered as specified; rates round away; Media/Bus come from the logical disk.
        Assert.AreEqual("disk:/data", tab.Key);
        Assert.AreEqual("Data", tab.Title);
        CollectionAssert.AreEqual(new[] { "Mount: /data", "Filesystem: ext4", "Used: 25.0 GB of 100.0 GB", "Read: 1.3 MB/s", "Write: 512 KB/s", "Physical disk: Disk 2", "Partition: 3", "Model: Model X", "Media: SSD, NVMe" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_PerDiskMissingDetailAllNineLines()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, UsedGb = 5, ReadMb = double.NaN, WriteMb = -1, Detail = null! } } })[3];
        // oracle: Missing detail yields N/A detail fields; invalid rates use zero and Unknown bus is omitted.
        Assert.AreEqual("/data", tab.Title);
        CollectionAssert.AreEqual(new[] { "Mount: /data", "Filesystem: N/A", "Used: 5.0 GB of 100.0 GB", "Read: 0 KB/s", "Write: 0 KB/s", "Physical disk: N/A", "Partition: N/A", "Model: N/A", "Media: Disk" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_PerDiskNullPhysicalDisk()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Detail = new DriveDetail { Letter = "/data", FileSystem = "xfs", DiskNumber = 0, PartitionNumber = 1, Disk = null! } } } })[3];
        // oracle: Null physical disk changes only Model; DiskNumber zero and partition one remain known.
        CollectionAssert.AreEqual(new[] { "Mount: /data", "Filesystem: xfs", "Used: 0 MB of 100.0 GB", "Read: 0 KB/s", "Write: 0 KB/s", "Physical disk: Disk 0", "Partition: 1", "Model: N/A", "Media: Disk" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_FileSystemEmpty()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Detail = new DriveDetail { Letter = "/data", FileSystem = "" } } } })[3];
        // oracle: Empty, whitespace-only or absent FileSystem is unknown.
        Assert.AreEqual("Filesystem: N/A", tab.Lines[1]);
    }

    [TestMethod]
    public void Tabs_FileSystemWhitespace()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Detail = new DriveDetail { Letter = "/data", FileSystem = " \t " } } } })[3];
        // oracle: Empty, whitespace-only or absent FileSystem is unknown.
        Assert.AreEqual("Filesystem: N/A", tab.Lines[1]);
    }

    [TestMethod]
    public void Tabs_FileSystemNull()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Detail = new DriveDetail { Letter = "/data", FileSystem = null! } } } })[3];
        // oracle: Empty, whitespace-only or absent FileSystem is unknown.
        Assert.AreEqual("Filesystem: N/A", tab.Lines[1]);
    }

    [TestMethod]
    public void Tabs_ModelEmpty()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Detail = new DriveDetail { Letter = "/data", Disk = new PhysicalDisk { Model = "" } } } } })[3];
        // oracle: Empty, whitespace-only or absent Model is unknown.
        Assert.AreEqual("Model: N/A", tab.Lines[7]);
    }

    [TestMethod]
    public void Tabs_ModelWhitespace()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Detail = new DriveDetail { Letter = "/data", Disk = new PhysicalDisk { Model = " \t " } } } } })[3];
        // oracle: Empty, whitespace-only or absent Model is unknown.
        Assert.AreEqual("Model: N/A", tab.Lines[7]);
    }

    [TestMethod]
    public void Tabs_ModelNull()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Detail = new DriveDetail { Letter = "/data", Disk = new PhysicalDisk { Model = null! } } } } })[3];
        // oracle: Empty, whitespace-only or absent Model is unknown.
        Assert.AreEqual("Model: N/A", tab.Lines[7]);
    }

    [TestMethod]
    public void Tabs_PartitionZero()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Detail = new DriveDetail { Letter = "/data", PartitionNumber = 0 } } } })[3];
        // oracle: Only strictly positive partition numbers are known.
        Assert.AreEqual("Partition: N/A", tab.Lines[6]);
    }

    [TestMethod]
    public void Tabs_PartitionNegative()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Detail = new DriveDetail { Letter = "/data", PartitionNumber = -1 } } } })[3];
        // oracle: Only strictly positive partition numbers are known.
        Assert.AreEqual("Partition: N/A", tab.Lines[6]);
    }

    [TestMethod]
    public void Tabs_PartitionMaximum()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Detail = new DriveDetail { Letter = "/data", PartitionNumber = int.MaxValue } } } })[3];
        // oracle: Every positive int partition is printed.
        Assert.AreEqual("Partition: 2147483647", tab.Lines[6]);
    }

    [TestMethod]
    public void Tabs_NullDiskNumber()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Detail = new DriveDetail { Letter = "/data", DiskNumber = null } } } })[3];
        // oracle: Missing DiskNumber prints N/A even when Detail exists.
        Assert.AreEqual("Physical disk: N/A", tab.Lines[5]);
    }

    [TestMethod]
    public void Tabs_NegativeDiskNumberIsPresent()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Detail = new DriveDetail { Letter = "/data", DiskNumber = -1 } } } })[3];
        // oracle: The specified disk-number template accepts any present int n.
        Assert.AreEqual("Physical disk: Disk -1", tab.Lines[5]);
    }

    [TestMethod]
    public void Tabs_UnknownBusOmitted()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Media = "HDD", Bus = "Unknown" } } })[3];
        // oracle: The exact Unknown bus is omitted.
        Assert.AreEqual("Media: HDD", tab.Lines[8]);
    }

    [TestMethod]
    public void Tabs_BusComparisonIsCaseSensitive()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Media = "Disk", Bus = "unknown" } } })[3];
        // oracle: A non-empty cleaned Bus is appended unless ordinally equal to Unknown; lowercase unknown is appended.
        Assert.AreEqual("Media: Disk, unknown", tab.Lines[8]);
    }

    [TestMethod]
    public void Tabs_LogicalMediaOverridesPhysicalFields()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Media = "HDD", Bus = "USB", Detail = new DriveDetail { Letter = "/data", Disk = new PhysicalDisk { Model = "M", Media = "SSD", Bus = "NVMe" } } } } })[3];
        // oracle: Logical Media and Bus are used independently of physical disk fields.
        Assert.AreEqual("Media: HDD, USB", tab.Lines[8]);
    }

    [TestMethod]
    public void Tabs_DiskControlCharactersEveryUse()
    {
        var tabs = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "\t/da\nta\r", TotalGb = 100, Label = "\nDa\u0000ta ", Detail = new DriveDetail { Letter = "/data", FileSystem = " e\u007fxt4\n", Disk = new PhysicalDisk { Model = " M\tX\r " } } } } });
        var tab = tabs[3];
        // oracle: Controls in Letter, Label, FileSystem and Model become single spaces followed by Trim in all uses.
        Assert.AreEqual("disk:/da ta", tab.Key);
        Assert.AreEqual("Da ta", tab.Title);
        CollectionAssert.AreEqual(new[] { "/da ta: 0% of 100.0 GB" }, tabs[2].Lines.ToArray());
        Assert.AreEqual("Mount: /da ta", tab.Lines[0]);
        Assert.AreEqual("Filesystem: e xt4", tab.Lines[1]);
        Assert.AreEqual("Model: M X", tab.Lines[7]);
    }

    [TestMethod]
    public void Tabs_ControlOnlyDetailsBecomeUnknown()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Label = "\n\t", Detail = new DriveDetail { Letter = "/data", FileSystem = "\u0000\r", Disk = new PhysicalDisk { Model = "\u007f\t" } } } } })[3];
        // oracle: Control-only strings become whitespace then empty: label falls back and detail values become N/A.
        Assert.AreEqual("/data", tab.Title);
        Assert.AreEqual("Filesystem: N/A", tab.Lines[1]);
        Assert.AreEqual("Model: N/A", tab.Lines[7]);
    }

    [TestMethod]
    public void Tabs_DiskUnicodeIsPreserved()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/\u00e9", TotalGb = 100, Label = "\u65e5", Detail = new DriveDetail { Letter = "/data", FileSystem = "\u00e9fs", Disk = new PhysicalDisk { Model = "\u65e5M" } } } } })[3];
        // oracle: Unicode letters in system-provided strings are preserved.
        Assert.AreEqual("disk:/\u00e9", tab.Key);
        Assert.AreEqual("\u65e5", tab.Title);
        Assert.AreEqual("Mount: /\u00e9", tab.Lines[0]);
        Assert.AreEqual("Filesystem: \u00e9fs", tab.Lines[1]);
        Assert.AreEqual("Model: \u65e5M", tab.Lines[7]);
    }

    [TestMethod]
    public void Tabs_DuplicateDisksAllReturned()
    {
        var tabs = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Label = "First", Usage = 10 }, new Disk { Letter = "/data", TotalGb = 100, Label = "Second", Usage = 20 } } });
        // oracle: All duplicate logical disks remain in stable order in overview and disk tabs.
        CollectionAssert.AreEqual(new[] { "disk:/data", "disk:/data" }, tabs.Skip(3).Select(x => x.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "First", "Second" }, tabs.Skip(3).Select(x => x.Title).ToArray());
        CollectionAssert.AreEqual(new[] { "/data: 10% of 100.0 GB", "/data: 20% of 100.0 GB" }, tabs[2].Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_SanitisedRootOrdering()
    {
        var tabs = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/a", TotalGb = 100 }, new Disk { Letter = "\t/\n", TotalGb = 100 } } });
        // oracle: The sanitised root mount is first consistently in overview and disk tabs.
        Assert.AreEqual("disk:/", tabs[3].Key);
        CollectionAssert.AreEqual(new[] { "/: 0% of 100.0 GB", "/a: 0% of 100.0 GB" }, tabs[2].Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_CultureIndependence()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var tabs = FullTabs.Build(new Snapshot { CpuTemp = 65, Ram = new Ram { UsedGb = 5.25, TotalGb = 16 }, Modules = new[] { new Module { Slot = "A", Gb = 8, Kind = "DDR5", Mhz = 6000 } }, Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Detail = new DriveDetail { Letter = "/data", DiskNumber = 1234, PartitionNumber = 2345 } } } });
            // oracle: Numeric strings use invariant decimal points and integers without grouping, even under de-DE.
            Assert.AreEqual("Temperature: 65 C", tabs[0].Lines[1]);
            Assert.AreEqual("Used: 5.3 GB", tabs[1].Lines[1]);
            Assert.AreEqual("Slot A: 8.0 GB DDR5 6000 MHz", tabs[1].Lines[3]);
            Assert.AreEqual("Physical disk: Disk 1234", tabs[3].Lines[5]);
            Assert.AreEqual("Partition: 2345", tabs[3].Lines[6]);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [TestMethod]
    public void History_NewStoreHasNoKeys()
    {
        var store = new HistoryStore();
        // oracle: A series is created only on its first recorded value.
        Assert.AreEqual(0, store.Keys.Count);
        Assert.IsNull(store.Get("cpu"));
    }

    [TestMethod]
    public void History_NullRecordDoesNothing()
    {
        var store = new HistoryStore();
        store.Record(null!);
        // oracle: Record(null) is a no-op.
        Assert.AreEqual(0, store.Keys.Count);
        Assert.IsNull(store.Get("cpu"));
    }

    [TestMethod]
    public void History_NullRecordDoesNotAgeSeries()
    {
        var store = new HistoryStore(2);
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100 } } });
        store.Record(null!);
        store.Record(null!);
        store.Record(new Snapshot());
        // oracle: Null records are no-ops; only one non-null record misses the disk, below capacity two.
        AssertSeries(store.Get("disk:/data"), 0);
    }

    [TestMethod]
    public void History_UnknownAndNullKeys()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot());
        // oracle: Get returns null for unknown keys, including null.
        Assert.IsNull(store.Get(null!));
        Assert.IsNull(store.Get("missing"));
    }

    [TestMethod]
    public void History_GetIsCaseSensitive()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot());
        // oracle: Get is ordinal and case-sensitive.
        Assert.IsNull(store.Get("CPU"));
        Assert.IsNull(store.Get("Ram"));
        AssertSeries(store.Get("cpu"), 0);
    }

    [TestMethod]
    public void History_GetDoesNotTrimKey()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot());
        // oracle: Lookups compare keys exactly without trimming.
        Assert.IsNull(store.Get(" cpu"));
        Assert.IsNull(store.Get("cpu "));
    }

    [TestMethod]
    public void History_BasePointsAndCreationOrder()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { CpuTotal = 17, Ram = new Ram { Usage = 29 } });
        // oracle: Non-null Record creates CPU then RAM with their aggregate values.
        CollectionAssert.AreEqual(new[] { "cpu", "ram" }, store.Keys.ToArray());
        AssertSeries(store.Get("cpu"), 17);
        AssertSeries(store.Get("ram"), 29);
    }

    [TestMethod]
    public void History_BaseValuesAreRawNotClamped()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { CpuTotal = -7, Ram = new Ram { Usage = 130 } });
        // oracle: History explicitly uses raw CpuTotal and Ram.Usage for base series.
        AssertSeries(store.Get("cpu"), -7);
        AssertSeries(store.Get("ram"), 130);
    }

    [TestMethod]
    public void History_NullNestedMembersBecomeZeroOrEmpty()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Ram = null!, Cores = null!, Disks = null!, Adapters = null!, Modules = null! });
        // oracle: Null nested members become zero or empty and never throw.
        CollectionAssert.AreEqual(new[] { "cpu", "ram" }, store.Keys.ToArray());
        AssertSeries(store.Get("cpu"), 0);
        AssertSeries(store.Get("ram"), 0);
    }

    [TestMethod]
    public void History_RecordsOldestToNewest()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { CpuTotal = 10 });
        store.Record(new Snapshot { CpuTotal = 20 });
        store.Record(new Snapshot { CpuTotal = 15 });
        // oracle: Points are 10,20,15 in record order: Latest = 15, Max = 20.
        AssertSeries(store.Get("cpu"), 10, 20, 15);
        Assert.AreEqual(15.0, store.Get("cpu")!.Latest, 0.000000001);
        Assert.AreEqual(20.0, store.Get("cpu")!.Max, 0.000000001);
    }

    [TestMethod]
    public void History_DiskUsageIsClamped()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = -1 } } });
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = 101 } } });
        // oracle: Disk history records MetricBuilder item.Usage, clamped to 0..100.
        AssertSeries(store.Get("disk:/data"), 0, 100);
    }

    [TestMethod]
    public void History_DiskFilterAndCreationOrder()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/z", TotalGb = 100 }, new Disk { Letter = "/", TotalGb = 100 }, new Disk { Letter = "/A", TotalGb = 100 }, new Disk { Letter = "/hidden", TotalGb = 0 } } });
        // oracle: Disk series use the metric filter and root-first ordinal creation order.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "disk:/", "disk:/:read", "disk:/:write", "disk:/A", "disk:/A:read", "disk:/A:write", "disk:/z", "disk:/z:read", "disk:/z:write" }, store.Keys.ToArray());
        Assert.IsNull(store.Get("disk:/hidden"));
    }

    [TestMethod]
    public void History_NanTotalDiskExcluded()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = double.NaN } } });
        // oracle: NaN total is filtered by !(TotalGb > 0) and creates no series.
        Assert.IsNull(store.Get("disk:/data"));
        Assert.IsNull(store.Get("disk:/data:read"));
        Assert.IsNull(store.Get("disk:/data:write"));
    }

    [TestMethod]
    public void History_AdapterDirectionsUseRawRates()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 1.25, UpMb = 0.5 } } });
        // oracle: Network histories store DownMb and UpMb separately; no utilisation series is specified.
        AssertSeries(store.Get("net:eth0:down"), 1.25);
        AssertSeries(store.Get("net:eth0:up"), 0.5);
        Assert.IsNull(store.Get("net:eth0"));
    }

    [TestMethod]
    public void History_AdapterNegativeRatesPreserved()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = -1, UpMb = -2 } } });
        // oracle: History rates are raw DownMb and UpMb; utilisation sanitisation does not change those fields.
        AssertSeries(store.Get("net:eth0:down"), -1);
        AssertSeries(store.Get("net:eth0:up"), -2);
    }

    [TestMethod]
    public void History_NetworkCreationOrder()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = "z0", SpeedMbps = 100 }, new Adapter { Id = "a0", SpeedMbps = 100 } } });
        // oracle: Network series follow snapshot order, each down before up.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "net:z0:down", "net:z0:up", "net:a0:down", "net:a0:up" }, store.Keys.ToArray());
    }

    [TestMethod]
    public void History_FilteredAdaptersHaveNoSeries()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = "DOCKER0", SpeedMbps = 100 }, new Adapter { Id = "veth0", SpeedMbps = 100 }, new Adapter { Id = "br-0", SpeedMbps = 100 }, new Adapter { Id = "virbr0", SpeedMbps = 100 }, new Adapter { Id = "lo1", SpeedMbps = 100 }, new Adapter { Id = "idle", SpeedMbps = 0 } } });
        // oracle: All virtual prefixes and idle zero-speed zero-counter links are filtered before creating history.
        CollectionAssert.AreEqual(new[] { "cpu", "ram" }, store.Keys.ToArray());
    }

    [TestMethod]
    public void History_ZeroSpeedWithTrafficIsRecorded()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 0, TotalReceived = 1, DownMb = 2 } } });
        // oracle: Nonzero received counter retains a zero-speed physical link for history.
        AssertSeries(store.Get("net:eth0:down"), 2);
        AssertSeries(store.Get("net:eth0:up"), 0);
    }

    [TestMethod]
    public void History_DuplicateDisksFirstOnly()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = 11, ReadMb = 1.25, WriteMb = 0.5 }, new Disk { Letter = "/data", TotalGb = 100, Usage = 99, ReadMb = 9, WriteMb = 8 } } });
        // oracle: The first included duplicate supplies one point in each of the usage/read/write series, giving five keys with CPU/RAM.
        AssertSeries(store.Get("disk:/data"), 11);
        AssertSeries(store.Get("disk:/data:read"), 1.25);
        AssertSeries(store.Get("disk:/data:write"), 0.5);
        Assert.AreEqual(5, store.Keys.Count);
    }

    [TestMethod]
    public void History_DuplicateAdaptersFirstOnly()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 1, UpMb = 2 }, new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 3, UpMb = 4 } } });
        // oracle: Only the first duplicate adapter supplies one point for each direction.
        AssertSeries(store.Get("net:eth0:down"), 1);
        AssertSeries(store.Get("net:eth0:up"), 2);
        Assert.AreEqual(4, store.Keys.Count);
    }

    [TestMethod]
    public void History_DuplicateDisksOnePointEachRecord()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = 10, ReadMb = 1, WriteMb = 2 }, new Disk { Letter = "/data", TotalGb = 100, Usage = 20, ReadMb = 3, WriteMb = 4 } } });
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = 30, ReadMb = 5, WriteMb = 6 }, new Disk { Letter = "/data", TotalGb = 100, Usage = 40, ReadMb = 7, WriteMb = 8 } } });
        // oracle: Two duplicate-containing records add exactly two points, using each first entry.
        AssertSeries(store.Get("disk:/data"), 10, 30);
        AssertSeries(store.Get("disk:/data:read"), 1, 5);
        AssertSeries(store.Get("disk:/data:write"), 2, 6);
    }

    [TestMethod]
    public void History_FilterBeforeDuplicateSelection()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 0, Usage = 99, ReadMb = 9, WriteMb = 8 }, new Disk { Letter = "/data", TotalGb = 100, Usage = 10, ReadMb = 1, WriteMb = 2 } } });
        // oracle: The first matching retained MetricBuilder item supplies the point; filtered items do not.
        AssertSeries(store.Get("disk:/data"), 10);
        AssertSeries(store.Get("disk:/data:read"), 1);
        AssertSeries(store.Get("disk:/data:write"), 2);
    }

    [TestMethod]
    public void History_SanitisedDiskKeyDuplicatesFirstOnly()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = " /data\t", TotalGb = 100, Usage = 10, ReadMb = 1, WriteMb = 2 }, new Disk { Letter = "/data", TotalGb = 100, Usage = 20, ReadMb = 3, WriteMb = 4 } } });
        // oracle: Sanitised identical keys form one series and the first entry wins.
        AssertSeries(store.Get("disk:/data"), 10);
        AssertSeries(store.Get("disk:/data:read"), 1);
        AssertSeries(store.Get("disk:/data:write"), 2);
        Assert.IsNull(store.Get("disk: /data\t"));
    }

    [TestMethod]
    public void History_SanitisedNetworkKeyDuplicatesFirstOnly()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = " eth\n0 ", SpeedMbps = 100, DownMb = 1, UpMb = 2 }, new Adapter { Id = "eth 0", SpeedMbps = 100, DownMb = 3, UpMb = 4 } } });
        // oracle: Network series keys use sanitised Id before first-entry duplicate resolution.
        AssertSeries(store.Get("net:eth 0:down"), 1);
        AssertSeries(store.Get("net:eth 0:up"), 2);
    }

    [TestMethod]
    public void History_CaseDistinctDiskAndNetworkKeys()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/A", TotalGb = 100, Usage = 1 }, new Disk { Letter = "/a", TotalGb = 100, Usage = 2 } }, Adapters = new[] { new Adapter { Id = "ETH0", SpeedMbps = 100, DownMb = 3 }, new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 4 } } });
        // oracle: Ordinal case-sensitive keys keep upper/lowercase names separate.
        AssertSeries(store.Get("disk:/A"), 1);
        AssertSeries(store.Get("disk:/a"), 2);
        AssertSeries(store.Get("net:ETH0:down"), 3);
        AssertSeries(store.Get("net:eth0:down"), 4);
    }

    [TestMethod]
    public void History_MissingSeriesGetsNoSyntheticPoint()
    {
        var store = new HistoryStore(3);
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = 21 } } });
        store.Record(new Snapshot { CpuTotal = 1 });
        store.Record(new Snapshot { CpuTotal = 2 });
        // oracle: Missing series receive no synthetic point; two misses remain below capacity three.
        AssertSeries(store.Get("disk:/data"), 21);
        AssertSeries(store.Get("cpu"), 0, 1, 2);
    }

    [TestMethod]
    public void History_TrimAfterExactlyCapacityMisses()
    {
        var store = new HistoryStore(3);
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = 21 } } });
        store.Record(new Snapshot());
        store.Record(new Snapshot());
        // oracle: Two consecutive misses are below capacity three.
        AssertSeries(store.Get("disk:/data"), 21);
        store.Record(new Snapshot());
        // oracle: The third consecutive Record without a point removes the disk series and its key.
        Assert.IsNull(store.Get("disk:/data"));
        CollectionAssert.AreEqual(new[] { "cpu", "ram" }, store.Keys.ToArray());
    }

    [TestMethod]
    public void History_ReceivingPointResetsMissStreak()
    {
        var store = new HistoryStore(3);
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = 10 } } });
        store.Record(new Snapshot());
        store.Record(new Snapshot());
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = 20 } } });
        store.Record(new Snapshot());
        store.Record(new Snapshot());
        // oracle: A point resets the consecutive-miss count; two misses after it do not trim capacity three.
        AssertSeries(store.Get("disk:/data"), 10, 20);
    }

    [TestMethod]
    public void History_FilteredDiskCountsAsMissing()
    {
        var store = new HistoryStore(2);
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100 } } });
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 0 } } });
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = double.NaN } } });
        // oracle: Two filtered disk records supply no points and trim at capacity two.
        Assert.IsNull(store.Get("disk:/data"));
        Assert.IsNull(store.Get("disk:/data:read"));
        Assert.IsNull(store.Get("disk:/data:write"));
    }

    [TestMethod]
    public void History_FilteredAdapterCountsAsMissing()
    {
        var store = new HistoryStore(2);
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100 } } });
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 0 } } });
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 0 } } });
        // oracle: Two idle filtered records supply no direction points; both series expire.
        Assert.IsNull(store.Get("net:eth0:down"));
        Assert.IsNull(store.Get("net:eth0:up"));
    }

    [TestMethod]
    public void History_DifferentSeriesAgeIndependently()
    {
        var store = new HistoryStore(2);
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/a", TotalGb = 100, Usage = 10 }, new Disk { Letter = "/b", TotalGb = 100, Usage = 20 } } });
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/b", TotalGb = 100, Usage = 30 } } });
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/b", TotalGb = 100, Usage = 40 } } });
        // oracle: Absent a trims after two misses while present b retains its latest two values.
        Assert.IsNull(store.Get("disk:/a"));
        AssertSeries(store.Get("disk:/b"), 30, 40);
    }

    [TestMethod]
    public void History_RecreatedSeriesAppendsToKeys()
    {
        var store = new HistoryStore(2);
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/a", TotalGb = 100, Usage = 10 } } });
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/b", TotalGb = 100, Usage = 20 } } });
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/b", TotalGb = 100, Usage = 30 } } });
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/a", TotalGb = 100, Usage = 40 }, new Disk { Letter = "/b", TotalGb = 100, Usage = 50 } } });
        // oracle: Trimmed a returns as a fresh one-point series, appended after existing b.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "disk:/b", "disk:/b:read", "disk:/b:write", "disk:/a", "disk:/a:read", "disk:/a:write" }, store.Keys.ToArray());
        AssertSeries(store.Get("disk:/a"), 40);
    }

    [TestMethod]
    public void History_KeysKeepCreationOrderAcrossReorderedSnapshot()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = "z", SpeedMbps = 100 }, new Adapter { Id = "a", SpeedMbps = 100 } } });
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = "a", SpeedMbps = 100 }, new Adapter { Id = "z", SpeedMbps = 100 } } });
        // oracle: Existing keys keep creation order even when snapshot order changes.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "net:z:down", "net:z:up", "net:a:down", "net:a:up" }, store.Keys.ToArray());
    }

    [TestMethod]
    public void History_LaterNewDiskAppendsAfterExistingNetwork()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100 } } });
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100 } }, Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100 } } });
        // oracle: A later-created disk appends after existing network keys despite current metric order.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "net:eth0:down", "net:eth0:up", "disk:/data", "disk:/data:read", "disk:/data:write" }, store.Keys.ToArray());
    }

    [TestMethod]
    public void History_RollingCapacityKeepsNewestPoints()
    {
        var store = new HistoryStore(3);
        for (var i = 1; i <= 5; i++)
            store.Record(new Snapshot { CpuTotal = i, Ram = new Ram { Usage = i + 10 } });
        // oracle: Capacity three retains the latest three of five points independently per series.
        AssertSeries(store.Get("cpu"), 3, 4, 5);
        AssertSeries(store.Get("ram"), 13, 14, 15);
    }

    [TestMethod]
    public void History_RollingMaxDropsEvictedPeak()
    {
        var store = new HistoryStore(2);
        store.Record(new Snapshot { CpuTotal = 90 });
        store.Record(new Snapshot { CpuTotal = 20 });
        store.Record(new Snapshot { CpuTotal = 10 });
        // oracle: Evicting 90 leaves 20,10; Latest = 10 and Max = 20.
        AssertSeries(store.Get("cpu"), 20, 10);
        Assert.AreEqual(10.0, store.Get("cpu")!.Latest, 0.000000001);
        Assert.AreEqual(20.0, store.Get("cpu")!.Max, 0.000000001);
    }

    [TestMethod]
    public void History_CapacityMinimumInt()
    {
        var store = new HistoryStore(int.MinValue);
        store.Record(new Snapshot { CpuTotal = 1 });
        store.Record(new Snapshot { CpuTotal = 2 });
        store.Record(new Snapshot { CpuTotal = 3 });
        // oracle: Requested capacity below two clamps to two; capacity two retains points 2 and 3.
        AssertSeries(store.Get("cpu"), 2, 3);
    }

    [TestMethod]
    public void History_CapacityNegative()
    {
        var store = new HistoryStore(-1);
        store.Record(new Snapshot { CpuTotal = 1 });
        store.Record(new Snapshot { CpuTotal = 2 });
        store.Record(new Snapshot { CpuTotal = 3 });
        // oracle: Requested capacity below two clamps to two; capacity two retains points 2 and 3.
        AssertSeries(store.Get("cpu"), 2, 3);
    }

    [TestMethod]
    public void History_CapacityZero()
    {
        var store = new HistoryStore(0);
        store.Record(new Snapshot { CpuTotal = 1 });
        store.Record(new Snapshot { CpuTotal = 2 });
        store.Record(new Snapshot { CpuTotal = 3 });
        // oracle: Requested capacity below two clamps to two; capacity two retains points 2 and 3.
        AssertSeries(store.Get("cpu"), 2, 3);
    }

    [TestMethod]
    public void History_CapacityOne()
    {
        var store = new HistoryStore(1);
        store.Record(new Snapshot { CpuTotal = 1 });
        store.Record(new Snapshot { CpuTotal = 2 });
        store.Record(new Snapshot { CpuTotal = 3 });
        // oracle: Requested capacity below two clamps to two; capacity two retains points 2 and 3.
        AssertSeries(store.Get("cpu"), 2, 3);
    }

    [TestMethod]
    public void History_CapacityExactlyTwo()
    {
        var store = new HistoryStore(2);
        store.Record(new Snapshot { CpuTotal = 1 });
        store.Record(new Snapshot { CpuTotal = 2 });
        store.Record(new Snapshot { CpuTotal = 3 });
        // oracle: Requested capacity below two clamps to two; capacity two retains points 2 and 3.
        AssertSeries(store.Get("cpu"), 2, 3);
    }

    [TestMethod]
    public void History_LowerCapacityBoundAlsoControlsTrimming()
    {
        var store = new HistoryStore(0);
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100 } } });
        store.Record(new Snapshot());
        // oracle: Requested zero capacity clamps to two, so the first miss retains the series.
        AssertSeries(store.Get("disk:/data"), 0);
        store.Record(new Snapshot());
        // oracle: At clamped capacity two, the second consecutive miss removes the series.
        Assert.IsNull(store.Get("disk:/data"));
    }

    [TestMethod]
    public void History_CapacityDefaultIs120()
    {
        var store = new HistoryStore();
        for (var i = 0; i < 121; i++)
            store.Record(new Snapshot { CpuTotal = i });
        var values = new double[120];
        var series = store.Get("cpu")!;
        // oracle: Effective capacity is 120; records 0..120 evict zero and retain 1..120.
        Assert.AreEqual(120, series.Count);
        Assert.AreEqual(120, series.CopyTo(values));
        Assert.AreEqual(1.0, values[0], 0.000000001);
        Assert.AreEqual(120.0, values[119], 0.000000001);
    }

    [TestMethod]
    public void History_CapacityExactly10000()
    {
        var store = new HistoryStore(10000);
        for (var i = 0; i < 10001; i++)
            store.Record(new Snapshot { CpuTotal = i });
        var values = new double[10000];
        var series = store.Get("cpu")!;
        // oracle: Effective capacity is 10000; records 0..10000 evict zero and retain 1..10000.
        Assert.AreEqual(10000, series.Count);
        Assert.AreEqual(10000, series.CopyTo(values));
        Assert.AreEqual(1.0, values[0], 0.000000001);
        Assert.AreEqual(10000.0, values[9999], 0.000000001);
    }

    [TestMethod]
    public void History_CapacityAbove10000()
    {
        var store = new HistoryStore(10001);
        for (var i = 0; i < 10001; i++)
            store.Record(new Snapshot { CpuTotal = i });
        var values = new double[10000];
        var series = store.Get("cpu")!;
        // oracle: Effective capacity is 10000; records 0..10000 evict zero and retain 1..10000.
        Assert.AreEqual(10000, series.Count);
        Assert.AreEqual(10000, series.CopyTo(values));
        Assert.AreEqual(1.0, values[0], 0.000000001);
        Assert.AreEqual(10000.0, values[9999], 0.000000001);
    }

    [TestMethod]
    public void History_CapacityMaximumInt()
    {
        var store = new HistoryStore(int.MaxValue);
        for (var i = 0; i < 10001; i++)
            store.Record(new Snapshot { CpuTotal = i });
        var values = new double[10000];
        var series = store.Get("cpu")!;
        // oracle: Effective capacity is 10000; records 0..10000 evict zero and retain 1..10000.
        Assert.AreEqual(10000, series.Count);
        Assert.AreEqual(10000, series.CopyTo(values));
        Assert.AreEqual(1.0, values[0], 0.000000001);
        Assert.AreEqual(10000.0, values[9999], 0.000000001);
    }

    [TestMethod]
    public void History_UpperBoundCapacityControlsTrimming()
    {
        var store = new HistoryStore(int.MaxValue);
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100 } } });
        for (var i = 0; i < 9999; i++)
            store.Record(new Snapshot());
        // oracle: 9999 consecutive misses are below effective capacity 10000.
        AssertSeries(store.Get("disk:/data"), 0);
        store.Record(new Snapshot());
        // oracle: The 10000th consecutive miss removes the series at effective capacity 10000.
        Assert.IsNull(store.Get("disk:/data"));
    }

    [TestMethod]
    public void History_DefaultCapacityControlsTrimming()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100 } } });
        for (var i = 0; i < 119; i++)
            store.Record(new Snapshot());
        // oracle: 119 consecutive misses are below effective capacity 120.
        AssertSeries(store.Get("disk:/data"), 0);
        store.Record(new Snapshot());
        // oracle: The 120th consecutive miss removes the series at effective capacity 120.
        Assert.IsNull(store.Get("disk:/data"));
    }

    [TestMethod]
    public void History_ClearRemovesAllSeriesAndKeys()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Cores = new[] { new Core { Usage = 17 } }, Disks = new[] { new Disk { Letter = "/data", TotalGb = 100 } }, Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100 } } });
        // oracle: Before Clear, Keys contains CPU, RAM, core, disk usage/read/write and network down/up in creation order.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "core:0", "disk:/data", "disk:/data:read", "disk:/data:write", "net:eth0:down", "net:eth0:up" }, store.Keys.ToArray());
        store.Clear();
        // oracle: Clear removes every series and every key across all metric kinds.
        Assert.AreEqual(0, store.Keys.Count);
        Assert.IsNull(store.Get("cpu"));
        Assert.IsNull(store.Get("ram"));
        Assert.IsNull(store.Get("core:0"));
        Assert.IsNull(store.Get("disk:/data"));
        Assert.IsNull(store.Get("disk:/data:read"));
        Assert.IsNull(store.Get("disk:/data:write"));
        Assert.IsNull(store.Get("net:eth0:down"));
        Assert.IsNull(store.Get("net:eth0:up"));
    }

    [TestMethod]
    public void History_ClearEmptyAndRepeatedIsSafe()
    {
        var store = new HistoryStore();
        store.Clear();
        store.Clear();
        // oracle: Repeated Clear on an empty store leaves it empty.
        Assert.AreEqual(0, store.Keys.Count);
        Assert.IsNull(store.Get("cpu"));
    }

    [TestMethod]
    public void History_RecordAfterClearStartsFreshOrder()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot { Cores = new Core[] { null!, null!, new Core { Usage = 12 } }, Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100 } } });
        store.Clear();
        store.Record(new Snapshot { Cores = new[] { new Core { Usage = 21 } }, Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = 30 } } });
        // oracle: After Clear, fresh records recreate keys in order with no old values or series.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "core:0", "disk:/data", "disk:/data:read", "disk:/data:write" }, store.Keys.ToArray());
        AssertSeries(store.Get("cpu"), 0);
        AssertSeries(store.Get("core:0"), 21);
        AssertSeries(store.Get("disk:/data"), 30);
        AssertSeries(store.Get("disk:/data:read"), 0);
        AssertSeries(store.Get("disk:/data:write"), 0);
        Assert.IsNull(store.Get("core:2"));
        Assert.IsNull(store.Get("net:eth0:down"));
    }

    [TestMethod]
    public void History_ClearResetsAbsenceAccounting()
    {
        var store = new HistoryStore(2);
        store.Record(new Snapshot { Cores = new[] { new Core { Usage = 17 } }, Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = 10, ReadMb = 1, WriteMb = 2 } } });
        store.Record(new Snapshot());
        store.Clear();
        store.Record(new Snapshot { Cores = new[] { new Core { Usage = 17 } }, Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = 20, ReadMb = 3, WriteMb = 4 } } });
        store.Record(new Snapshot());
        // oracle: Clear discards prior miss counters; the fresh series has one point and one miss only.
        AssertSeries(store.Get("core:0"), 17);
        AssertSeries(store.Get("disk:/data"), 20);
        AssertSeries(store.Get("disk:/data:read"), 3);
        AssertSeries(store.Get("disk:/data:write"), 4);
    }

    [TestMethod]
    public void Tabs_OverviewUsageClamping()
    {
        var tab = FullTabs.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/a", TotalGb = 1, Usage = -1 }, new Disk { Letter = "/b", TotalGb = 1, Usage = 101 } } })[2];
        // oracle: Overview Percent clamps disk usage at 0 and 100.
        CollectionAssert.AreEqual(new[] { "/a: 0% of 1.0 GB", "/b: 100% of 1.0 GB" }, tab.Lines.ToArray());
    }

    [TestMethod]
    public void Tabs_ConsecutiveControlsAreNotCollapsed()
    {
        var tabs = FullTabs.Build(new Snapshot { Modules = new[] { new Module { Slot = "A\u0000\tB", Kind = "D\u0085\u009fR", Gb = 1, Mhz = 1 } }, Disks = new[] { new Disk { Letter = "/A\u0000\tB", TotalGb = 1, Detail = new DriveDetail { Letter = "/A", FileSystem = "e\u0085\u009fx", Disk = new PhysicalDisk { Model = "M\u0000\tX" } } } } });
        // oracle: Every control maps individually to one space, including C1 controls; internal consecutive spaces survive trimming.
        Assert.AreEqual("Slot A  B: 1.0 GB D  R 1 MHz", tabs[1].Lines[3]);
        Assert.AreEqual("disk:/A  B", tabs[3].Key);
        Assert.AreEqual("Filesystem: e  x", tabs[3].Lines[1]);
        Assert.AreEqual("Model: M  X", tabs[3].Lines[7]);
    }

    [TestMethod]
    public void Tabs_EntirelyNullListsStillHaveBaseTabs()
    {
        var tabs = FullTabs.Build(new Snapshot { Cores = null!, Ram = null!, Disks = null!, Adapters = null!, Modules = null! });
        // oracle: All nested null lists are empty and null RAM is zero; the three normal base tabs remain.
        CollectionAssert.AreEqual(new[] { "cpu", "memory", "disk" }, tabs.Select(x => x.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "Total: 0%", "Temperature: N/A" }, tabs[0].Lines.ToArray());
        CollectionAssert.AreEqual(new[] { "Usage: 0%", "Used: 0 MB", "Total: 0 MB", "Modules: N/A (needs root)" }, tabs[1].Lines.ToArray());
        Assert.AreEqual(0, tabs[2].Lines.Count);
    }

    [TestMethod]
    public void History_CapacityJustBelowUpperBound()
    {
        var store = new HistoryStore(9999);
        for (var i = 0; i < 10000; i++)
            store.Record(new Snapshot { CpuTotal = i });
        var values = new double[9999];
        var history = store.Get("cpu")!;
        // oracle: Capacity 9999 is within bounds and remains 9999; records 0..9999 retain 1..9999.
        Assert.AreEqual(9999, history.Count);
        Assert.AreEqual(9999, history.CopyTo(values));
        Assert.AreEqual(1.0, values[0], 0.000000001);
        Assert.AreEqual(9999.0, values[9998], 0.000000001);
    }


    [TestMethod]
    public void Tabs_EmptyWhitespaceAndNullBusOmitSuffix()
    {
        foreach (var bus in new string[] { "", "   ", "\t\r", null! })
        {
            var tab = FullTabs.Build(new Snapshot
            {
                Disks = new[] { new Disk { Letter = "/data", TotalGb = 1, Media = "SSD", Bus = bus } }
            })[3];
            // oracle: Each Bus cleans to empty, so ruling (e) adds no comma or suffix.
            Assert.AreEqual("Media: SSD", tab.Lines[8]);
        }
    }

    [TestMethod]
    public void Tabs_MediaAndBusAreCleanedBeforeFallbackAndUnknownComparison()
    {
        var tabs = FullTabs.Build(new Snapshot
        {
            Disks = new[]
            {
                new Disk { Letter = "/a", TotalGb = 1, Media = "  S\u0000SD\t", Bus = "\tUnknown  " },
                new Disk { Letter = "/b", TotalGb = 1, Media = "\u2003\t ", Bus = "  N\u0000VMe\r" },
                new Disk { Letter = "/c", TotalGb = 1, Media = " SSD ", Bus = " unknown\t" }
            }
        });
        // oracle: Clean maps controls individually to spaces and trims before comparing Bus ordinally to Unknown.
        Assert.AreEqual("Media: S SD", tabs[3].Lines[8]);
        // oracle: Empty cleaned Media becomes N/A; a non-empty Bus other than Unknown is appended.
        Assert.AreEqual("Media: N/A, N VMe", tabs[4].Lines[8]);
        Assert.AreEqual("Media: SSD, unknown", tabs[5].Lines[8]);
    }

    [TestMethod]
    public void Tabs_EmptyAndNullMediaUseUnknownText()
    {
        foreach (var media in new string[] { "", null! })
        {
            var tab = FullTabs.Build(new Snapshot
            {
                Disks = new[] { new Disk { Letter = "/data", TotalGb = 1, Media = media, Bus = "Unknown" } }
            })[3];
            // oracle: Empty/absent Media becomes N/A and the exact Unknown Bus adds nothing.
            Assert.AreEqual("Media: N/A", tab.Lines[8]);
        }
    }

    [TestMethod]
    public void Tabs_NullElementsAreSkippedAndCoreNumbersKeepListPositions()
    {
        var tabs = FullTabs.Build(new Snapshot
        {
            Cores = new Core[] { null!, new Core { Usage = 12, Temp = 50, Estimated = true }, null!, new Core { Usage = 34 } },
            Disks = new Disk[] { null!, new Disk { Letter = "/data", TotalGb = 1 }, null! },
            Adapters = new Adapter[] { null!, new Adapter { Id = "eth0", SpeedMbps = 100 }, null! },
            Modules = new Module[] { null!, new Module { Slot = "B", Gb = 2, Kind = "DDR5", Mhz = 6000 }, null! }
        });
        // oracle: Null cores produce no lines; surviving cores retain indices 1 and 3, including temperature suffix rules.
        CollectionAssert.AreEqual(new[] { "Total: 0%", "Temperature: N/A", "Core 1: 12%  50 C (estimated)", "Core 3: 34%" }, tabs[0].Lines.ToArray());
        // oracle: Only the surviving module adds a line and only the surviving disk adds a tab/overview line; adapters add none.
        CollectionAssert.AreEqual(new[] { "Usage: 0%", "Used: 0 MB", "Total: 0 MB", "Slot B: 2.0 GB DDR5 6000 MHz" }, tabs[1].Lines.ToArray());
        CollectionAssert.AreEqual(new[] { "cpu", "memory", "disk", "disk:/data" }, tabs.Select(x => x.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "/data: 0% of 1.0 GB" }, tabs[2].Lines.ToArray());
        Assert.AreEqual(9, tabs[3].Lines.Count);
    }

    [TestMethod]
    public void Tabs_ListsContainingOnlyNullElementsBehaveAsEmpty()
    {
        var tabs = FullTabs.Build(new Snapshot
        {
            Cores = new Core[] { null! }, Disks = new Disk[] { null! },
            Adapters = new Adapter[] { null! }, Modules = new Module[] { null! }
        });
        // oracle: Skipping all null elements leaves the three base tabs, no core or disk lines, and no usable modules.
        CollectionAssert.AreEqual(new[] { "cpu", "memory", "disk" }, tabs.Select(x => x.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "Total: 0%", "Temperature: N/A" }, tabs[0].Lines.ToArray());
        CollectionAssert.AreEqual(new[] { "Usage: 0%", "Used: 0 MB", "Total: 0 MB", "Modules: N/A (needs root)" }, tabs[1].Lines.ToArray());
        Assert.AreEqual(0, tabs[2].Lines.Count);
    }

    [TestMethod]
    public void History_NullElementsPreserveCorePositionsAndSkipOtherEntries()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot
        {
            Cores = new Core[] { null!, new Core { Usage = -7 }, null!, new Core { Usage = 130 } },
            Disks = new Disk[] { null!, new Disk { Letter = "/data", TotalGb = 1, Usage = 23, ReadMb = 1.25, WriteMb = 0.5 }, null! },
            Adapters = new Adapter[] { null!, new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 2, UpMb = 3 }, null! },
            Modules = new Module[] { null!, new Module { Slot = "A" }, null! }
        });
        // oracle: Ruling (d) puts cores at original positions 1 and 3 after RAM, then disk usage/read/write, then network down/up.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "core:1", "core:3", "disk:/data", "disk:/data:read", "disk:/data:write", "net:eth0:down", "net:eth0:up" }, store.Keys.ToArray());
        // oracle: Core history values are raw core.Usage, while null entries have no series.
        AssertSeries(store.Get("core:1"), -7);
        AssertSeries(store.Get("core:3"), 130);
        Assert.IsNull(store.Get("core:0"));
        Assert.IsNull(store.Get("core:2"));
        AssertSeries(store.Get("disk:/data"), 23);
        AssertSeries(store.Get("disk:/data:read"), 1.25);
        AssertSeries(store.Get("disk:/data:write"), 0.5);
        AssertSeries(store.Get("net:eth0:down"), 2);
        AssertSeries(store.Get("net:eth0:up"), 3);
    }

    [TestMethod]
    public void History_AllFamiliesFollowSpecifiedFirstAppearanceOrder()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot
        {
            Cores = new[] { new Core { Usage = 11 }, new Core { Usage = 22 } },
            Disks = new[]
            {
                new Disk { Letter = "/z", TotalGb = 1, Usage = 30, ReadMb = 3, WriteMb = 4 },
                new Disk { Letter = " /\t", TotalGb = 1, Usage = 10, ReadMb = 1, WriteMb = 2 },
                new Disk { Letter = "/A", TotalGb = 1, Usage = 20, ReadMb = 5, WriteMb = 6 }
            },
            Adapters = new[] { new Adapter { Id = "z", SpeedMbps = 100 }, new Adapter { Id = "a", SpeedMbps = 100 } }
        });
        // oracle: CPU/RAM, positional cores, cleaned root then ordinal A/z (each usage/read/write), then adapters in snapshot order (down/up).
        CollectionAssert.AreEqual(new[]
        {
            "cpu", "ram", "core:0", "core:1",
            "disk:/", "disk:/:read", "disk:/:write",
            "disk:/A", "disk:/A:read", "disk:/A:write",
            "disk:/z", "disk:/z:read", "disk:/z:write",
            "net:z:down", "net:z:up", "net:a:down", "net:a:up"
        }, store.Keys.ToArray());
        AssertSeries(store.Get("disk:/:read"), 1);
        AssertSeries(store.Get("disk:/:write"), 2);
        AssertSeries(store.Get("disk:/A:read"), 5);
        AssertSeries(store.Get("disk:/z:write"), 4);
    }

    [TestMethod]
    public void History_FirstIncludedAdapterSuppliesBothDirectionsForDuplicateId()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot
        {
            Adapters = new[]
            {
                new Adapter { Id = "eth0", SpeedMbps = 0, DownMb = 9, UpMb = 8 },
                new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 1.25, UpMb = 0.5 },
                new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 7, UpMb = 6 }
            }
        });
        // oracle: The first eth0 has speed <= 0 and zero counters, so is excluded even with rate fields set;
        // the first INCLUDED eth0 supplies exactly one point per direction; the later included duplicate supplies none.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "net:eth0:down", "net:eth0:up" }, store.Keys.ToArray());
        AssertSeries(store.Get("net:eth0:down"), 1.25);
        AssertSeries(store.Get("net:eth0:up"), 0.5);
        store.Record(new Snapshot
        {
            Adapters = new[]
            {
                new Adapter { Id = "eth0", SpeedMbps = 0, DownMb = 99, UpMb = 88 },
                new Adapter { Id = "eth0", SpeedMbps = 0, TotalSent = 1, DownMb = 2.25, UpMb = 3.5 }
            }
        });
        // oracle: Nonzero TotalSent includes the second zero-speed adapter; each direction gets only its value in this record.
        AssertSeries(store.Get("net:eth0:down"), 1.25, 2.25);
        AssertSeries(store.Get("net:eth0:up"), 0.5, 3.5);
    }

    [TestMethod]
    public void History_FirstIncludedAdapterUsesCleanedDuplicateKey()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot
        {
            Adapters = new[]
            {
                new Adapter { Id = " eth0\t", SpeedMbps = 0, DownMb = 9, UpMb = 8 },
                new Adapter { Id = "\teth0 ", SpeedMbps = 100, DownMb = 1, UpMb = 2 },
                new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 3, UpMb = 4 }
            }
        });
        // oracle: All Ids clean to eth0; filtering occurs before taking the first entry for that cleaned key.
        AssertSeries(store.Get("net:eth0:down"), 1);
        AssertSeries(store.Get("net:eth0:up"), 2);
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "net:eth0:down", "net:eth0:up" }, store.Keys.ToArray());
    }

    [TestMethod]
    public void History_NewCoreAndDiskDirectionSeriesRollIndependently()
    {
        var store = new HistoryStore(2);
        store.Record(new Snapshot { Cores = new[] { new Core { Usage = 90 } }, Disks = new[] { new Disk { Letter = "/data", TotalGb = 1, ReadMb = 0.5, WriteMb = 1.25 } } });
        store.Record(new Snapshot { Cores = new[] { new Core { Usage = 20 } }, Disks = new[] { new Disk { Letter = "/data", TotalGb = 1, ReadMb = 2.25, WriteMb = 3.5 } } });
        store.Record(new Snapshot { Cores = new[] { new Core { Usage = 10 } }, Disks = new[] { new Disk { Letter = "/data", TotalGb = 1, ReadMb = -1, WriteMb = -2 } } });
        // oracle: Capacity two retains only the last two raw points for each new series; rate formatting/clamping does not alter history.
        AssertSeries(store.Get("core:0"), 20, 10);
        AssertSeries(store.Get("disk:/data:read"), 2.25, -1);
        AssertSeries(store.Get("disk:/data:write"), 3.5, -2);
    }

    [TestMethod]
    public void History_NewSeriesExpireAfterExactlyCapacityConsecutiveMisses()
    {
        var store = new HistoryStore(3);
        store.Record(new Snapshot
        {
            Cores = new Core[] { new Core { Usage = 11 }, null!, new Core { Usage = 22 } },
            Disks = new[] { new Disk { Letter = "/data", TotalGb = 1, ReadMb = 1.25, WriteMb = 0.5 } }
        });
        store.Record(new Snapshot());
        store.Record(new Snapshot());
        // oracle: Two misses are below capacity three; missing series get no synthetic points.
        AssertSeries(store.Get("core:0"), 11);
        AssertSeries(store.Get("core:2"), 22);
        AssertSeries(store.Get("disk:/data:read"), 1.25);
        AssertSeries(store.Get("disk:/data:write"), 0.5);
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "core:0", "core:2", "disk:/data", "disk:/data:read", "disk:/data:write" }, store.Keys.ToArray());
        store.Record(new Snapshot());
        // oracle: The third miss expires every absent core and disk series and removes their keys.
        CollectionAssert.AreEqual(new[] { "cpu", "ram" }, store.Keys.ToArray());
        Assert.IsNull(store.Get("core:0"));
        Assert.IsNull(store.Get("core:2"));
        Assert.IsNull(store.Get("disk:/data"));
        Assert.IsNull(store.Get("disk:/data:read"));
        Assert.IsNull(store.Get("disk:/data:write"));
    }

    [TestMethod]
    public void History_NewSeriesReturningPointResetsMissCount()
    {
        var store = new HistoryStore(2);
        store.Record(new Snapshot { Cores = new[] { new Core { Usage = 10 } }, Disks = new[] { new Disk { Letter = "/data", TotalGb = 1, ReadMb = 1, WriteMb = 2 } } });
        store.Record(new Snapshot());
        store.Record(new Snapshot { Cores = new[] { new Core { Usage = 20 } }, Disks = new[] { new Disk { Letter = "/data", TotalGb = 1, ReadMb = 3, WriteMb = 4 } } });
        store.Record(new Snapshot());
        // oracle: Returning values reset each miss streak; one later miss cannot expire capacity-two series.
        AssertSeries(store.Get("core:0"), 10, 20);
        AssertSeries(store.Get("disk:/data:read"), 1, 3);
        AssertSeries(store.Get("disk:/data:write"), 2, 4);
        store.Record(new Snapshot());
        // oracle: The second consecutive miss after the reset expires all absent series.
        CollectionAssert.AreEqual(new[] { "cpu", "ram" }, store.Keys.ToArray());
    }

    [TestMethod]
    public void History_NewCoreSeriesAppendByFirstAppearanceRatherThanNumericOrder()
    {
        var store = new HistoryStore();
        store.Record(new Snapshot
        {
            Cores = new Core[] { null!, new Core { Usage = 11 } },
            Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100 } }
        });
        store.Record(new Snapshot
        {
            Cores = new[] { new Core { Usage = 22 }, new Core { Usage = 33 } },
            Disks = new[] { new Disk { Letter = "/data", TotalGb = 1 } }
        });
        // oracle: core:1 and the adapter existed first; later core:0 appends before the later disk trio and never reorders older keys.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "core:1", "net:eth0:down", "net:eth0:up", "core:0", "disk:/data", "disk:/data:read", "disk:/data:write" }, store.Keys.ToArray());
        AssertSeries(store.Get("core:1"), 11, 33);
        AssertSeries(store.Get("core:0"), 22);
    }

    [TestMethod]
    public void History_ExpiredCoreRecreationAppendsAfterSurvivingSeries()
    {
        var store = new HistoryStore(2);
        store.Record(new Snapshot { Cores = new[] { new Core { Usage = 11 } } });
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100 } } });
        store.Record(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100 } } });
        // oracle: Two absent records expire core:0 while adapter directions survive.
        Assert.IsNull(store.Get("core:0"));
        store.Record(new Snapshot { Cores = new[] { new Core { Usage = 44 } }, Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100 } } });
        // oracle: Recreated core:0 is a fresh one-point series appended after surviving keys.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "net:eth0:down", "net:eth0:up", "core:0" }, store.Keys.ToArray());
        AssertSeries(store.Get("core:0"), 44);
    }


    [TestMethod]
    public void History_NullCoreEntriesAgeOnlyTheirOwnPosition()
    {
        var store = new HistoryStore(2);
        store.Record(new Snapshot { Cores = new[] { new Core { Usage = 10 }, new Core { Usage = 20 } } });
        store.Record(new Snapshot { Cores = new Core[] { null!, new Core { Usage = 30 } } });
        // oracle: A null core supplies no synthetic point at position zero; one miss retains its old value.
        AssertSeries(store.Get("core:0"), 10);
        AssertSeries(store.Get("core:1"), 20, 30);
        store.Record(new Snapshot { Cores = new Core[] { null!, new Core { Usage = 40 } } });
        // oracle: Position zero expires after two misses, while position one keeps receiving values at its original index.
        Assert.IsNull(store.Get("core:0"));
        AssertSeries(store.Get("core:1"), 30, 40);
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "core:1" }, store.Keys.ToArray());
    }

}
