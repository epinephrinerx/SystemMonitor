using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public class MetricTests
{
    [TestMethod]
    public void NullSnapshot_ReturnsEmpty()
    {
        var items = MetricBuilder.Build(null!);
        // oracle: Null snapshot returns an empty metric list.
        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public void EmptySnapshot_BaseOrder()
    {
        var items = MetricBuilder.Build(new Snapshot());
        // oracle: CPU and RAM always precede all optional metrics.
        CollectionAssert.AreEqual(new[] { "cpu", "ram" }, items.Select(x => x.Key).ToArray());
    }

    [TestMethod]
    public void Cpu_AllFields()
    {
        var item = MetricBuilder.Build(new Snapshot { CpuTotal = 37, CpuTemp = 65, Cores = new[] { new Core(), new Core() } })[0];
        // oracle: CPU fields use aggregate 37%, two cores, temperature, and the blue accent below load 70.
        Assert.AreEqual(MetricKind.Cpu, item.Kind);
        Assert.AreEqual("cpu", item.Key);
        Assert.AreEqual("CPU", item.Title);
        Assert.AreEqual("2 cores", item.Subtitle);
        Assert.AreEqual(37, item.Usage);
        Assert.AreEqual("37% 65 C", item.ValueText);
        Assert.AreEqual("#3b82f6", item.AccentHex);
        Assert.AreEqual("#3b82f6", item.ColorHex);
    }

    [TestMethod]
    public void Cpu_ZeroCores()
    {
        var item = MetricBuilder.Build(new Snapshot { Cores = Array.Empty<Core>() })[0];
        // oracle: Core count is plural except exactly one; null list is empty.
        Assert.AreEqual("0 cores", item.Subtitle);
    }

    [TestMethod]
    public void Cpu_OneCores()
    {
        var item = MetricBuilder.Build(new Snapshot { Cores = new[] { new Core() } })[0];
        // oracle: Core count is plural except exactly one; null list is empty.
        Assert.AreEqual("1 core", item.Subtitle);
    }

    [TestMethod]
    public void Cpu_NullCores()
    {
        var item = MetricBuilder.Build(new Snapshot { Cores = null! })[0];
        // oracle: Core count is plural except exactly one; null list is empty.
        Assert.AreEqual("0 cores", item.Subtitle);
    }

    [TestMethod]
    public void Cpu_MissingTemperature()
    {
        var item = MetricBuilder.Build(new Snapshot { CpuTotal = 12 })[0];
        // oracle: Absent temperature adds no suffix or space.
        Assert.AreEqual("12%", item.ValueText);
    }

    [TestMethod]
    public void Cpu_EstimatedTemperatureHasNoMetricSuffix()
    {
        var item = MetricBuilder.Build(new Snapshot { CpuTotal = 10, CpuTemp = 50, CpuTempEstimated = true })[0];
        // oracle: Metric CPU text has no estimated suffix; FullTabs has that suffix.
        Assert.AreEqual("10% 50 C", item.ValueText);
    }

    [TestMethod]
    public void Cpu_UsageNegative()
    {
        var item = MetricBuilder.Build(new Snapshot { CpuTotal = int.MinValue })[0];
        // oracle: CPU clamps to 0..100; load 70 is yellow and load 90 is red.
        Assert.AreEqual(0, item.Usage);
        Assert.AreEqual("0%", item.ValueText);
        Assert.AreEqual("#3b82f6", item.ColorHex);
    }

    [TestMethod]
    public void Cpu_UsageAboveHundred()
    {
        var item = MetricBuilder.Build(new Snapshot { CpuTotal = int.MaxValue })[0];
        // oracle: CPU clamps to 0..100; load 70 is yellow and load 90 is red.
        Assert.AreEqual(100, item.Usage);
        Assert.AreEqual("100%", item.ValueText);
        Assert.AreEqual("#ef4444", item.ColorHex);
    }

    [TestMethod]
    public void Cpu_UsageBeforeWarm()
    {
        var item = MetricBuilder.Build(new Snapshot { CpuTotal = 69 })[0];
        // oracle: CPU clamps to 0..100; load 70 is yellow and load 90 is red.
        Assert.AreEqual(69, item.Usage);
        Assert.AreEqual("69%", item.ValueText);
        Assert.AreEqual("#3b82f6", item.ColorHex);
    }

    [TestMethod]
    public void Cpu_UsageAtWarm()
    {
        var item = MetricBuilder.Build(new Snapshot { CpuTotal = 70 })[0];
        // oracle: CPU clamps to 0..100; load 70 is yellow and load 90 is red.
        Assert.AreEqual(70, item.Usage);
        Assert.AreEqual("70%", item.ValueText);
        Assert.AreEqual("#eab308", item.ColorHex);
    }

    [TestMethod]
    public void Cpu_UsageBeforeHot()
    {
        var item = MetricBuilder.Build(new Snapshot { CpuTotal = 89 })[0];
        // oracle: CPU clamps to 0..100; load 70 is yellow and load 90 is red.
        Assert.AreEqual(89, item.Usage);
        Assert.AreEqual("89%", item.ValueText);
        Assert.AreEqual("#eab308", item.ColorHex);
    }

    [TestMethod]
    public void Cpu_UsageAtHot()
    {
        var item = MetricBuilder.Build(new Snapshot { CpuTotal = 90 })[0];
        // oracle: CPU clamps to 0..100; load 70 is yellow and load 90 is red.
        Assert.AreEqual(90, item.Usage);
        Assert.AreEqual("90%", item.ValueText);
        Assert.AreEqual("#ef4444", item.ColorHex);
    }

    [TestMethod]
    public void Ram_AllFields()
    {
        var item = MetricBuilder.Build(new Snapshot { Ram = new Ram { Usage = 50, UsedGb = 8, TotalGb = 16 } })[1];
        // oracle: RAM uses its usage, used/total sizes and purple accent below 70.
        Assert.AreEqual(MetricKind.Ram, item.Kind);
        Assert.AreEqual("ram", item.Key);
        Assert.AreEqual("Memory", item.Title);
        Assert.AreEqual("8.0 GB / 16.0 GB", item.Subtitle);
        Assert.AreEqual(50, item.Usage);
        Assert.AreEqual("50%", item.ValueText);
        Assert.AreEqual("#a855f7", item.AccentHex);
        Assert.AreEqual("#a855f7", item.ColorHex);
    }

    [TestMethod]
    public void Ram_NullBecomesZero()
    {
        var item = MetricBuilder.Build(new Snapshot { Ram = null! })[1];
        // oracle: Null RAM is zero/absent.
        Assert.AreEqual(0, item.Usage);
        Assert.AreEqual("0%", item.ValueText);
        Assert.AreEqual("0 MB / 0 MB", item.Subtitle);
    }

    [TestMethod]
    public void Ram_InvalidSizes()
    {
        var item = MetricBuilder.Build(new Snapshot { Ram = new Ram { UsedGb = double.NaN, TotalGb = -1 } })[1];
        // oracle: Invalid RAM sizes use Fmt.Size fallback independently.
        Assert.AreEqual("N/A / N/A", item.Subtitle);
    }

    [TestMethod]
    public void Ram_UsageNegative()
    {
        var item = MetricBuilder.Build(new Snapshot { Ram = new Ram { Usage = -1 } })[1];
        // oracle: RAM usage clamps to 0..100; colours switch at 70 and 90.
        Assert.AreEqual(0, item.Usage);
        Assert.AreEqual("0%", item.ValueText);
        Assert.AreEqual("#a855f7", item.ColorHex);
    }

    [TestMethod]
    public void Ram_UsageAboveHundred()
    {
        var item = MetricBuilder.Build(new Snapshot { Ram = new Ram { Usage = 101 } })[1];
        // oracle: RAM usage clamps to 0..100; colours switch at 70 and 90.
        Assert.AreEqual(100, item.Usage);
        Assert.AreEqual("100%", item.ValueText);
        Assert.AreEqual("#ef4444", item.ColorHex);
    }

    [TestMethod]
    public void Ram_UsageAtWarm()
    {
        var item = MetricBuilder.Build(new Snapshot { Ram = new Ram { Usage = 70 } })[1];
        // oracle: RAM usage clamps to 0..100; colours switch at 70 and 90.
        Assert.AreEqual(70, item.Usage);
        Assert.AreEqual("70%", item.ValueText);
        Assert.AreEqual("#eab308", item.ColorHex);
    }

    [TestMethod]
    public void Ram_UsageAtHot()
    {
        var item = MetricBuilder.Build(new Snapshot { Ram = new Ram { Usage = 90 } })[1];
        // oracle: RAM usage clamps to 0..100; colours switch at 70 and 90.
        Assert.AreEqual(90, item.Usage);
        Assert.AreEqual("90%", item.ValueText);
        Assert.AreEqual("#ef4444", item.ColorHex);
    }

    [TestMethod]
    public void Disk_AllFields()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Label = "Data", Usage = 40, UsedGb = 40 } } });
        var item = items[2];
        // oracle: Disk uses its mount, label, clamped usage, sizes and green accent below 70.
        Assert.AreEqual(MetricKind.Disk, item.Kind);
        Assert.AreEqual("disk:/data", item.Key);
        Assert.AreEqual("Data", item.Title);
        Assert.AreEqual("/data - 40.0 GB / 100.0 GB", item.Subtitle);
        Assert.AreEqual(40, item.Usage);
        Assert.AreEqual("40%", item.ValueText);
        Assert.AreEqual("#10b981", item.ColorHex);
        Assert.AreEqual("#10b981", item.AccentHex);
    }

    [TestMethod]
    public void Disk_FilterZero()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 0 } } });
        // oracle: Disk is dropped when !(TotalGb > 0).
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public void Disk_FilterNegative()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = -1 } } });
        // oracle: Disk is dropped when !(TotalGb > 0).
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public void Disk_FilterNaN()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = double.NaN } } });
        // oracle: Disk is dropped when !(TotalGb > 0).
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public void Disk_FilterNegativeInfinity()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = double.NegativeInfinity } } });
        // oracle: Disk is dropped when !(TotalGb > 0).
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public void Disk_PositiveInfinityRetained()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = double.PositiveInfinity } } });
        var item = items[2];
        // oracle: Positive infinity is > 0, so retained; Fmt.Size(infinity) is N/A.
        Assert.AreEqual("disk:/data", item.Key);
        Assert.AreEqual("/data - 0 MB / N/A", item.Subtitle);
    }

    [TestMethod]
    public void Disk_SmallPositiveRetained()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = double.Epsilon } } });
        var item = items[2];
        // oracle: Any strictly positive total passes the filter; tiny size rounds to zero MB.
        Assert.AreEqual("/data - 0 MB / 0 MB", item.Subtitle);
    }

    [TestMethod]
    public void Disk_RootFirstThenOrdinal()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/z", TotalGb = 100 }, new Disk { Letter = "/a", TotalGb = 100 }, new Disk { Letter = "/", TotalGb = 100 }, new Disk { Letter = "/B", TotalGb = 100 } } });
        // oracle: Root precedes others, then ordinal ordering places B before a before z.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "disk:/", "disk:/B", "disk:/a", "disk:/z" }, items.Select(x => x.Key).ToArray());
    }

    [TestMethod]
    public void Disk_LabelEmpty()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Label = "" } } });
        var item = items[2];
        // oracle: Empty or whitespace-only label falls back to Letter; absent string is empty.
        Assert.AreEqual("/data", item.Title);
    }

    [TestMethod]
    public void Disk_LabelWhitespace()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Label = " \t " } } });
        var item = items[2];
        // oracle: Empty or whitespace-only label falls back to Letter; absent string is empty.
        Assert.AreEqual("/data", item.Title);
    }

    [TestMethod]
    public void Disk_LabelNull()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Label = null! } } });
        var item = items[2];
        // oracle: Empty or whitespace-only label falls back to Letter; absent string is empty.
        Assert.AreEqual("/data", item.Title);
    }

    [TestMethod]
    public void Disk_UsageNegative()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = -9 } } });
        var item = items[2];
        // oracle: Disk usage clamps to 0..100; load thresholds are 70 and 90.
        Assert.AreEqual(0, item.Usage);
        Assert.AreEqual("0%", item.ValueText);
        Assert.AreEqual("#10b981", item.ColorHex);
    }

    [TestMethod]
    public void Disk_UsageAboveHundred()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = 190 } } });
        var item = items[2];
        // oracle: Disk usage clamps to 0..100; load thresholds are 70 and 90.
        Assert.AreEqual(100, item.Usage);
        Assert.AreEqual("100%", item.ValueText);
        Assert.AreEqual("#ef4444", item.ColorHex);
    }

    [TestMethod]
    public void Disk_UsageAtWarm()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = 70 } } });
        var item = items[2];
        // oracle: Disk usage clamps to 0..100; load thresholds are 70 and 90.
        Assert.AreEqual(70, item.Usage);
        Assert.AreEqual("70%", item.ValueText);
        Assert.AreEqual("#eab308", item.ColorHex);
    }

    [TestMethod]
    public void Disk_UsageAtHot()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Usage = 90 } } });
        var item = items[2];
        // oracle: Disk usage clamps to 0..100; load thresholds are 70 and 90.
        Assert.AreEqual(90, item.Usage);
        Assert.AreEqual("90%", item.ValueText);
        Assert.AreEqual("#ef4444", item.ColorHex);
    }

    [TestMethod]
    public void Disk_NullList()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = null! });
        // oracle: Null disk list is empty.
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public void Disk_DuplicateKeysStable()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100, Label = "First" }, new Disk { Letter = "/data", TotalGb = 100, Label = "Second" } } });
        // oracle: All duplicate disks survive and retain their relative order.
        CollectionAssert.AreEqual(new[] { "First", "Second" }, items.Skip(2).Select(x => x.Title).ToArray());
        CollectionAssert.AreEqual(new[] { "disk:/data", "disk:/data" }, items.Skip(2).Select(x => x.Key).ToArray());
    }

    [TestMethod]
    public void Disk_SanitisesLetterAndLabel()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "\t /da\nta\u007f ", TotalGb = 100, Label = " \u0000Data\tSet\r " } } });
        var item = items[2];
        // oracle: Every control becomes one space and the result is trimmed in keys, titles and subtitles.
        Assert.AreEqual("disk:/da ta", item.Key);
        Assert.AreEqual("Data Set", item.Title);
        Assert.AreEqual("/da ta - 0 MB / 100.0 GB", item.Subtitle);
    }

    [TestMethod]
    public void Disk_SanitisedRootGetsPriority()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/a", TotalGb = 100 }, new Disk { Letter = "\n/\r", TotalGb = 100 } } });
        // oracle: Ordering uses the consistently sanitised root mount.
        Assert.AreEqual("disk:/", items[2].Key);
    }

    [TestMethod]
    public void Disk_UnicodeIsPreserved()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/\u00e9", TotalGb = 100, Label = "\u65e5\u672c" } } });
        var item = items[2];
        // oracle: Non-ASCII letters are preserved in system strings.
        Assert.AreEqual("disk:/\u00e9", item.Key);
        Assert.AreEqual("\u65e5\u672c", item.Title);
    }

    [TestMethod]
    public void Disk_CaseDistinctKeys()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/a", TotalGb = 100 }, new Disk { Letter = "/A", TotalGb = 100 } } });
        // oracle: Keys and disk ordering use ordinal case-sensitive comparisons.
        CollectionAssert.AreEqual(new[] { "disk:/A", "disk:/a" }, items.Skip(2).Select(x => x.Key).ToArray());
    }

    [TestMethod]
    public void Network_AllFields()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 1, UpMb = 0.5 } } });
        var item = items[2];
        // oracle: (1 + 0.5) * 8.388608 = 12.582912 Mbit/s; at 100 Mbps this rounds to 13%; direction text has two spaces.
        Assert.AreEqual(MetricKind.Network, item.Kind);
        Assert.AreEqual("net:eth0", item.Key);
        Assert.AreEqual("eth0", item.Title);
        Assert.AreEqual("LAN 100 Mbps", item.Subtitle);
        Assert.AreEqual(13, item.Usage);
        Assert.AreEqual("Down 1.0 MB/s  Up 512 KB/s", item.ValueText);
        Assert.AreEqual("#06b6d4", item.AccentHex);
        Assert.AreEqual("#06b6d4", item.ColorHex);
    }

    [TestMethod]
    public void Network_Wireless()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 2500, Wireless = true } } });
        var item = items[2];
        // oracle: Wireless subtitle uses Wi-Fi and formatted link speed.
        Assert.AreEqual("Wi-Fi 2.5 Gbps", item.Subtitle);
    }

    [TestMethod]
    public void Network_ExcludesPrefix_docker()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "DOCKER123", SpeedMbps = 100, TotalReceived = 99, DownMb = 1 } } });
        // oracle: Virtual-link prefix docker is ordinal case-insensitive and excludes even links with traffic.
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public void Network_ExcludesPrefix_veth()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "VETH123", SpeedMbps = 100, TotalReceived = 99, DownMb = 1 } } });
        // oracle: Virtual-link prefix veth is ordinal case-insensitive and excludes even links with traffic.
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public void Network_ExcludesPrefix_brDash()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "BR-123", SpeedMbps = 100, TotalReceived = 99, DownMb = 1 } } });
        // oracle: Virtual-link prefix br- is ordinal case-insensitive and excludes even links with traffic.
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public void Network_ExcludesPrefix_virbr()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "VIRBR123", SpeedMbps = 100, TotalReceived = 99, DownMb = 1 } } });
        // oracle: Virtual-link prefix virbr is ordinal case-insensitive and excludes even links with traffic.
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public void Network_ExcludesPrefix_lo()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "LO123", SpeedMbps = 100, TotalReceived = 99, DownMb = 1 } } });
        // oracle: Virtual-link prefix lo is ordinal case-insensitive and excludes even links with traffic.
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public void Network_DoesNotFilterEmbeddedPrefix()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "ethdocker0", SpeedMbps = 100 } } });
        var item = items[2];
        // oracle: Virtual-link filtering tests the start, not a substring.
        Assert.AreEqual("net:ethdocker0", item.Key);
    }

    [TestMethod]
    public void Network_ZeroSpeedAndCountersExcluded()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 0 } } });
        // oracle: Speed <= 0 with both counters zero is excluded.
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public void Network_NegativeSpeedAndCountersExcluded()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = -1 } } });
        // oracle: Negative speed and zero counters satisfy idle exclusion.
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public void Network_ZeroSpeedWithReceivedRetained()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 0, TotalReceived = 1 } } });
        var item = items[2];
        // oracle: A nonzero counter retains a zero-speed physical link; utilisation is zero.
        Assert.AreEqual(0, item.Usage);
        Assert.AreEqual("LAN N/A", item.Subtitle);
    }

    [TestMethod]
    public void Network_ZeroSpeedWithSentRetained()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 0, TotalSent = 1 } } });
        var item = items[2];
        // oracle: A nonzero counter retains a zero-speed physical link; utilisation is zero.
        Assert.AreEqual(0, item.Usage);
        Assert.AreEqual("LAN N/A", item.Subtitle);
    }

    [TestMethod]
    public void Network_NanSpeedRetained()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = double.NaN } } });
        var item = items[2];
        // oracle: NaN is neither <= 0 nor > 0: link retained, utilisation zero, speed N/A.
        Assert.AreEqual(0, item.Usage);
        Assert.AreEqual("LAN N/A", item.Subtitle);
    }

    [TestMethod]
    public void Network_NullList()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = null! });
        // oracle: Null adapter list is empty.
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public void Network_SnapshotOrderAfterDisks()
    {
        var items = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/data", TotalGb = 100 } }, Adapters = new[] { new Adapter { Id = "z0", SpeedMbps = 100 }, new Adapter { Id = "a0", SpeedMbps = 100 } } });
        // oracle: All adapters follow disks and retain snapshot order.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "disk:/data", "net:z0", "net:a0" }, items.Select(x => x.Key).ToArray());
    }

    [TestMethod]
    public void Network_DuplicatesAllReturned()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 1 }, new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 2 } } });
        // oracle: Duplicate network keys all survive in original order.
        CollectionAssert.AreEqual(new[] { "net:eth0", "net:eth0" }, items.Skip(2).Select(x => x.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "Down 1.0 MB/s  Up 0 KB/s", "Down 2.0 MB/s  Up 0 KB/s" }, items.Skip(2).Select(x => x.ValueText).ToArray());
    }

    [TestMethod]
    public void Network_SanitisedIdUsedEverywhere()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "\t eth\u00000\r ", SpeedMbps = 100 } } });
        var item = items[2];
        // oracle: Control characters become spaces and Trim applies consistently to Id.
        Assert.AreEqual("net:eth 0", item.Key);
        Assert.AreEqual("eth 0", item.Title);
    }

    [TestMethod]
    public void Network_SanitisedPrefixIsFiltered()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "\tdocker0\r", SpeedMbps = 100 } } });
        // oracle: The consistently sanitised Id starts with the excluded docker prefix.
        Assert.AreEqual(2, items.Count);
    }

    [TestMethod]
    public void Network_UnicodeIdPreserved()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth\u00e9", SpeedMbps = 100 } } });
        var item = items[2];
        // oracle: Unicode letters are preserved in Id and title.
        Assert.AreEqual("net:eth\u00e9", item.Key);
        Assert.AreEqual("eth\u00e9", item.Title);
    }

    [TestMethod]
    public void Network_UtilisationBelowHalf()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 62499 / 1048576.0, UpMb = 0 } } });
        var item = items[2];
        // oracle: 62499 bytes/s * 8 / 1000000 = 0.499992 Mbit/s; at 100 Mbps rounds to 0%.
        Assert.AreEqual(0, item.Usage);
        Assert.AreEqual("#06b6d4", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationAtHalf()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 62500 / 1048576.0, UpMb = 0 } } });
        var item = items[2];
        // oracle: 62500 bytes/s = 0.5 Mbit/s exactly; at 100 Mbps 0.5% rounds away to 1%.
        Assert.AreEqual(1, item.Usage);
        Assert.AreEqual("#06b6d4", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationAboveHalf()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 62501 / 1048576.0, UpMb = 0 } } });
        var item = items[2];
        // oracle: 62501 bytes/s = 0.500008 Mbit/s; at 100 Mbps rounds to 1%.
        Assert.AreEqual(1, item.Usage);
        Assert.AreEqual("#06b6d4", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationSumDirections()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 2, UpMb = 3 } } });
        var item = items[2];
        // oracle: (2 + 3) * 8.388608 = 41.94304 Mbit/s; at 100 Mbps rounds to 42%.
        Assert.AreEqual(42, item.Usage);
        Assert.AreEqual("#06b6d4", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationClampsHigh()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 100, UpMb = 100 } } });
        var item = items[2];
        // oracle: Negative inputs become zero; roundAway((down + up) * 8.388608 / speed * 100), clamp 0..100; NaN -> 0, +Infinity -> 100; colour thresholds 70/90.
        Assert.AreEqual(100, item.Usage);
        Assert.AreEqual("#ef4444", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationNegativeDownZeroed()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = -1, UpMb = 1 } } });
        var item = items[2];
        // oracle: Negative inputs become zero; roundAway((down + up) * 8.388608 / speed * 100), clamp 0..100; NaN -> 0, +Infinity -> 100; colour thresholds 70/90.
        Assert.AreEqual(8, item.Usage);
        Assert.AreEqual("#06b6d4", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationNegativeUpZeroed()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 1, UpMb = -1 } } });
        var item = items[2];
        // oracle: Negative inputs become zero; roundAway((down + up) * 8.388608 / speed * 100), clamp 0..100; NaN -> 0, +Infinity -> 100; colour thresholds 70/90.
        Assert.AreEqual(8, item.Usage);
        Assert.AreEqual("#06b6d4", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationBothNegative()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = -1, UpMb = -2 } } });
        var item = items[2];
        // oracle: Negative inputs become zero; roundAway((down + up) * 8.388608 / speed * 100), clamp 0..100; NaN -> 0, +Infinity -> 100; colour thresholds 70/90.
        Assert.AreEqual(0, item.Usage);
        Assert.AreEqual("#06b6d4", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationNanRate()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = double.NaN, UpMb = 1 } } });
        var item = items[2];
        // oracle: Negative inputs become zero; roundAway((down + up) * 8.388608 / speed * 100), clamp 0..100; NaN -> 0, +Infinity -> 100; colour thresholds 70/90.
        Assert.AreEqual(0, item.Usage);
        Assert.AreEqual("#06b6d4", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationInfiniteRate()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = double.PositiveInfinity, UpMb = 0 } } });
        var item = items[2];
        // oracle: Negative inputs become zero; roundAway((down + up) * 8.388608 / speed * 100), clamp 0..100; NaN -> 0, +Infinity -> 100; colour thresholds 70/90.
        Assert.AreEqual(100, item.Usage);
        Assert.AreEqual("#ef4444", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationOverflowingSum()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = double.MaxValue, UpMb = double.MaxValue } } });
        var item = items[2];
        // oracle: Negative inputs become zero; roundAway((down + up) * 8.388608 / speed * 100), clamp 0..100; NaN -> 0, +Infinity -> 100; colour thresholds 70/90.
        Assert.AreEqual(100, item.Usage);
        Assert.AreEqual("#ef4444", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationInfiniteSpeedFiniteRate()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = double.PositiveInfinity, DownMb = 1, UpMb = 0 } } });
        var item = items[2];
        // oracle: Negative inputs become zero; roundAway((down + up) * 8.388608 / speed * 100), clamp 0..100; NaN -> 0, +Infinity -> 100; colour thresholds 70/90.
        Assert.AreEqual(0, item.Usage);
        Assert.AreEqual("#06b6d4", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationWarmRounding()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 8687500 / 1048576.0, UpMb = 0 } } });
        var item = items[2];
        // oracle: 8687500 bytes/s = 69.5 Mbit/s; at 100 Mbps rounds away to 70%, yellow.
        Assert.AreEqual(70, item.Usage);
        Assert.AreEqual("#eab308", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationBeforeWarm()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 8687499 / 1048576.0, UpMb = 0 } } });
        var item = items[2];
        // oracle: 8687499 bytes/s = 69.499992 Mbit/s; at 100 Mbps rounds to 69%, accent.
        Assert.AreEqual(69, item.Usage);
        Assert.AreEqual("#06b6d4", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationHotRounding()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 11187500 / 1048576.0, UpMb = 0 } } });
        var item = items[2];
        // oracle: 11187500 bytes/s = 89.5 Mbit/s; at 100 Mbps rounds away to 90%, red.
        Assert.AreEqual(90, item.Usage);
        Assert.AreEqual("#ef4444", item.ColorHex);
    }

    [TestMethod]
    public void Network_UtilisationBeforeHot()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 11187499 / 1048576.0, UpMb = 0 } } });
        var item = items[2];
        // oracle: 11187499 bytes/s = 89.499992 Mbit/s; at 100 Mbps rounds to 89%, yellow.
        Assert.AreEqual(89, item.Usage);
        Assert.AreEqual("#eab308", item.ColorHex);
    }

    [TestMethod]
    public void Network_InvalidRatesText()
    {
        var items = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = double.NaN, UpMb = -1 } } });
        var item = items[2];
        // oracle: Each invalid rate is formatted as 0 KB/s.
        Assert.AreEqual("Down 0 KB/s  Up 0 KB/s", item.ValueText);
    }

    [TestMethod]
    public void ReadyFalseStillBuildsMetrics()
    {
        var items = MetricBuilder.Build(new Snapshot { Ready = false, CpuTotal = 25, Ram = new Ram { Usage = 30 } });
        // oracle: MetricBuilder has no Ready gate; aggregate values are presented.
        Assert.AreEqual(25, items[0].Usage);
        Assert.AreEqual(30, items[1].Usage);
    }

    [TestMethod]
    public void Disk_ConsecutiveControlsRemainSeparateSpaces()
    {
        var item = MetricBuilder.Build(new Snapshot { Disks = new[] { new Disk { Letter = "/A\u0000\tB", Label = "X\u0085\u009fY", TotalGb = 1 } } })[2];
        // oracle: Each control character contributes one space, including C1 controls; consecutive replacements are not collapsed.
        Assert.AreEqual("disk:/A  B", item.Key);
        Assert.AreEqual("X  Y", item.Title);
        Assert.AreEqual("/A  B - 0 MB / 1.0 GB", item.Subtitle);
    }

    [TestMethod]
    public void Network_NegativeSpeedWithCounterRetained()
    {
        var item = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = -1, TotalSent = 1, DownMb = 2 } } })[2];
        // oracle: Nonzero sent counter retains the link despite negative speed; speed <= 0 yields zero utilisation and N/A speed.
        Assert.AreEqual(0, item.Usage);
        Assert.AreEqual("LAN N/A", item.Subtitle);
        Assert.AreEqual("Down 2.0 MB/s  Up 0 KB/s", item.ValueText);
    }

    [TestMethod]
    public void Network_InfiniteSumDividedByInfiniteSpeedIsZero()
    {
        var item = MetricBuilder.Build(new Snapshot { Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = double.PositiveInfinity, DownMb = double.PositiveInfinity } } })[2];
        // oracle: Infinity / Infinity is NaN, so utilisation is zero; colour remains the network accent.
        Assert.AreEqual(0, item.Usage);
        Assert.AreEqual("#06b6d4", item.ColorHex);
    }


    [TestMethod]
    public void Network_UtilisationAtOneHundredMegabitsUsesMibConversion()
    {
        var item = MetricBuilder.Build(new Snapshot
        {
            Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 11.920928955078125 } }
        })[2];
        // oracle: 11.920928955078125 MiB/s = 12500000 bytes/s = 100 Mbit/s;
        // round(100 / 100 * 100) = 100%, red. Using factor 8 would incorrectly give 95%.
        Assert.AreEqual(100, item.Usage);
        Assert.AreEqual("#ef4444", item.ColorHex);
    }

    [TestMethod]
    public void Build_NullElementsInEveryListAreSkipped()
    {
        var items = MetricBuilder.Build(new Snapshot
        {
            CpuTotal = 23,
            Cores = new Core[] { null!, new Core { Usage = 17 }, null! },
            Disks = new Disk[] { null!, new Disk { Letter = "/data", TotalGb = 1 }, null! },
            Adapters = new Adapter[] { null!, new Adapter { Id = "eth0", SpeedMbps = 100 }, null! },
            Modules = new Module[] { null!, new Module { Slot = "A" }, null! }
        });
        // oracle: Null elements create no optional metrics and do not prevent valid entries or CPU/RAM output.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "disk:/data", "net:eth0" }, items.Select(x => x.Key).ToArray());
        Assert.AreEqual("23%", items[0].ValueText);
    }

    [TestMethod]
    public void Clean_PublicOutputPreservesTextWithoutControlsOrEdgeWhitespace()
    {
        var items = MetricBuilder.Build(new Snapshot
        {
            Disks = new[] { new Disk { Letter = "/Data  Volume", Label = "Data  Volume \u00e9", TotalGb = 1 } },
            Adapters = new[] { new Adapter { Id = "eth  0", SpeedMbps = 100 } }
        });
        // oracle: Cleaning leaves text with no controls or edge whitespace unchanged, including internal spaces and Unicode.
        Assert.AreEqual("disk:/Data  Volume", items[2].Key);
        Assert.AreEqual("Data  Volume \u00e9", items[2].Title);
        Assert.AreEqual("/Data  Volume - 0 MB / 1.0 GB", items[2].Subtitle);
        Assert.AreEqual("net:eth  0", items[3].Key);
        Assert.AreEqual("eth  0", items[3].Title);
    }

    [TestMethod]
    public void Clean_PublicOutputTrimsWhitespaceWithoutControlCharacters()
    {
        var items = MetricBuilder.Build(new Snapshot
        {
            Disks = new[] { new Disk { Letter = "  /data\u2003", Label = "\u2003 Data  Volume  ", TotalGb = 1 } },
            Adapters = new[] { new Adapter { Id = "\u2003eth0  ", SpeedMbps = 100 } }
        });
        // oracle: Trim removes leading/trailing spaces and U+2003, which is whitespace but not a control; internal spaces survive.
        Assert.AreEqual("disk:/data", items[2].Key);
        Assert.AreEqual("Data  Volume", items[2].Title);
        Assert.AreEqual("/data - 0 MB / 1.0 GB", items[2].Subtitle);
        Assert.AreEqual("net:eth0", items[3].Key);
        Assert.AreEqual("eth0", items[3].Title);
    }


    [TestMethod]
    public void Build_ListsContainingOnlyNullElementsProduceOnlyBaseMetrics()
    {
        var items = MetricBuilder.Build(new Snapshot
        {
            Cores = new Core[] { null! }, Disks = new Disk[] { null! },
            Adapters = new Adapter[] { null! }, Modules = new Module[] { null! }
        });
        // oracle: Skipping every null element leaves CPU and RAM and creates no disk or network metrics.
        CollectionAssert.AreEqual(new[] { "cpu", "ram" }, items.Select(x => x.Key).ToArray());
        Assert.AreEqual("0%", items[0].ValueText);
        Assert.AreEqual("0%", items[1].ValueText);
    }

}
