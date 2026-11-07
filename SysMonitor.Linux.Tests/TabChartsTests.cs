#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class TabChartsTests
{
    [TestMethod]
    public void For_Null_Empty()
    {
        var actual = TabCharts.For(null, new[] { "core:0", "disk:/" });
        // oracle: section 4 unknown/null/blank/case-mismatched/untrimmed tab keys produce no charts.
        CollectionAssert.AreEqual(new ChartSpec[] {  }, actual.ToArray());
    }

    [TestMethod]
    public void For_Empty_Empty()
    {
        var actual = TabCharts.For("", new[] { "core:0", "disk:/" });
        // oracle: section 4 unknown/null/blank/case-mismatched/untrimmed tab keys produce no charts.
        CollectionAssert.AreEqual(new ChartSpec[] {  }, actual.ToArray());
    }

    [TestMethod]
    public void For_Whitespace_Empty()
    {
        var actual = TabCharts.For(" \t", new[] { "core:0", "disk:/" });
        // oracle: section 4 unknown/null/blank/case-mismatched/untrimmed tab keys produce no charts.
        CollectionAssert.AreEqual(new ChartSpec[] {  }, actual.ToArray());
    }

    [TestMethod]
    public void For_Unknown_Empty()
    {
        var actual = TabCharts.For("network", new[] { "core:0", "disk:/" });
        // oracle: section 4 unknown/null/blank/case-mismatched/untrimmed tab keys produce no charts.
        CollectionAssert.AreEqual(new ChartSpec[] {  }, actual.ToArray());
    }

    [TestMethod]
    public void For_CpuUppercase_Empty()
    {
        var actual = TabCharts.For("CPU", new[] { "core:0", "disk:/" });
        // oracle: section 4 unknown/null/blank/case-mismatched/untrimmed tab keys produce no charts.
        CollectionAssert.AreEqual(new ChartSpec[] {  }, actual.ToArray());
    }

    [TestMethod]
    public void For_MemoryUppercase_Empty()
    {
        var actual = TabCharts.For("Memory", new[] { "core:0", "disk:/" });
        // oracle: section 4 unknown/null/blank/case-mismatched/untrimmed tab keys produce no charts.
        CollectionAssert.AreEqual(new ChartSpec[] {  }, actual.ToArray());
    }

    [TestMethod]
    public void For_DiskUppercase_Empty()
    {
        var actual = TabCharts.For("Disk", new[] { "core:0", "disk:/" });
        // oracle: section 4 unknown/null/blank/case-mismatched/untrimmed tab keys produce no charts.
        CollectionAssert.AreEqual(new ChartSpec[] {  }, actual.ToArray());
    }

    [TestMethod]
    public void For_LeadingSpace_Empty()
    {
        var actual = TabCharts.For(" cpu", new[] { "core:0", "disk:/" });
        // oracle: section 4 unknown/null/blank/case-mismatched/untrimmed tab keys produce no charts.
        CollectionAssert.AreEqual(new ChartSpec[] {  }, actual.ToArray());
    }

    [TestMethod]
    public void For_TrailingSpace_Empty()
    {
        var actual = TabCharts.For("memory ", new[] { "core:0", "disk:/" });
        // oracle: section 4 unknown/null/blank/case-mismatched/untrimmed tab keys produce no charts.
        CollectionAssert.AreEqual(new ChartSpec[] {  }, actual.ToArray());
    }

    [TestMethod]
    public void For_DiskPrefixUppercase_Empty()
    {
        var actual = TabCharts.For("DISK:/", new[] { "core:0", "disk:/" });
        // oracle: section 4 unknown/null/blank/case-mismatched/untrimmed tab keys produce no charts.
        CollectionAssert.AreEqual(new ChartSpec[] {  }, actual.ToArray());
    }

    [TestMethod]
    public void Cpu_NullHistoryHasTotal()
    {
        var actual = TabCharts.For("cpu", null);
        // oracle: section 4 null history is an empty key sequence, so CPU still has its unconditional total chart.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("CPU", "cpu", "#3b82f6", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void Cpu_EmptyHistoryHasTotal()
    {
        var actual = TabCharts.For("cpu", Array.Empty<string>());
        // oracle: section 4 cpu starts with the total chart.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("CPU", "cpu", "#3b82f6", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void Cpu_PreservesCoreOrder()
    {
        var actual = TabCharts.For("cpu", new[] { "core:9", "core:2", "core:0" });
        // oracle: section 4 core keys use enumeration order rather than sorting.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("CPU", "cpu", "#3b82f6", 100), new ChartSpec("Core 9", "core:9", "#3b82f6", 100), new ChartSpec("Core 2", "core:2", "#3b82f6", 100), new ChartSpec("Core 0", "core:0", "#3b82f6", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void Cpu_OnlyOrdinalCorePrefix()
    {
        var actual = TabCharts.For("cpu", new[] { "cpu", "Core:1", " core:2", "core", "core:3", "disk:/" });
        // oracle: section 4 only ordinal core: matches, unrelated history is ignored.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("CPU", "cpu", "#3b82f6", 100), new ChartSpec("Core 3", "core:3", "#3b82f6", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void Cpu_EmptySuffix()
    {
        var actual = TabCharts.For("cpu", new[] { "core:" });
        // oracle: section 4 core suffix is concatenated verbatim even when empty.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("CPU", "cpu", "#3b82f6", 100), new ChartSpec("Core ", "core:", "#3b82f6", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void Cpu_ArbitrarySuffix()
    {
        var actual = TabCharts.For("cpu", new[] { "core:a:b" });
        // oracle: section 4 core suffix has no numeric or colon validation.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("CPU", "cpu", "#3b82f6", 100), new ChartSpec("Core a:b", "core:a:b", "#3b82f6", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void Cpu_SkipsNullElements()
    {
        var actual = TabCharts.For("cpu", new string[] { null!, "core:1", null! });
        // oracle: section 4 null history elements are skipped.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("CPU", "cpu", "#3b82f6", 100), new ChartSpec("Core 1", "core:1", "#3b82f6", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void Cpu_PreservesDuplicates()
    {
        var actual = TabCharts.For("cpu", new[] { "core:1", "core:1" });
        // oracle: section 4 each matching history key contributes a chart, including duplicates.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("CPU", "cpu", "#3b82f6", 100), new ChartSpec("Core 1", "core:1", "#3b82f6", 100), new ChartSpec("Core 1", "core:1", "#3b82f6", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void Memory_NullHistory()
    {
        var actual = TabCharts.For("memory", null);
        // oracle: section 4 memory always uses ram, purple, fixed max 100.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("Memory", "ram", "#a855f7", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void Memory_IgnoresUnrelatedAndNullKeys()
    {
        var actual = TabCharts.For("memory", new string[] { "cpu", null!, "disk:/", "ram" });
        // oracle: section 4 memory chart does not depend on history enumeration.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("Memory", "ram", "#a855f7", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void DiskOverview_NullHistory()
    {
        var actual = TabCharts.For("disk", null);
        // oracle: section 4 disk overview has no charts without history keys.
        CollectionAssert.AreEqual(new ChartSpec[] {  }, actual.ToArray());
    }

    [TestMethod]
    public void DiskOverview_EmptyHistory()
    {
        var actual = TabCharts.For("disk", Array.Empty<string>());
        // oracle: section 4 empty history yields empty disk overview.
        CollectionAssert.AreEqual(new ChartSpec[] {  }, actual.ToArray());
    }

    [TestMethod]
    public void DiskOverview_MountLettersAndOrder()
    {
        var actual = TabCharts.For("disk", new[] { "disk:/z", "disk:/", "disk:/home", "disk:C" });
        // oracle: section 4 mount suffixes become titles and order is preserved.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("/z", "disk:/z", "#10b981", 100), new ChartSpec("/", "disk:/", "#10b981", 100), new ChartSpec("/home", "disk:/home", "#10b981", 100), new ChartSpec("C", "disk:C", "#10b981", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void DiskOverview_EmptySuffix()
    {
        var actual = TabCharts.For("disk", new[] { "disk:" });
        // oracle: section 4 disk: with no suffix is included with empty title.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("", "disk:", "#10b981", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void DiskOverview_ReadWriteAndExtraColonExcluded()
    {
        var actual = TabCharts.For("disk", new[] { "disk:/:read", "disk:/:write", "disk:a:b", "disk:/ok", "disk::" });
        // oracle: section 4 any colon after disk: excludes a history key from overview.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("/ok", "disk:/ok", "#10b981", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void DiskOverview_CaseAndWhitespaceAreOrdinal()
    {
        var actual = TabCharts.For("disk", new[] { "Disk:/", " disk:/", "disk", "disk:/data " });
        // oracle: section 4 prefix is ordinal and matching suffix is not trimmed.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("/data ", "disk:/data ", "#10b981", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void DiskOverview_SkipsNullElements()
    {
        var actual = TabCharts.For("disk", new string[] { null!, "disk:/", null! });
        // oracle: section 4 null history elements are skipped.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("/", "disk:/", "#10b981", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void DiskOverview_PreservesDuplicateKeys()
    {
        var actual = TabCharts.For("disk", new[] { "disk:/", "disk:/" });
        // oracle: section 4 each matching key is returned without deduplication.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("/", "disk:/", "#10b981", 100), new ChartSpec("/", "disk:/", "#10b981", 100) }, actual.ToArray());
    }

    [TestMethod]
    public void PerDisk_Root()
    {
        var actual = TabCharts.For("disk:/", null);
        // oracle: section 4 any ordinal disk: tab produces Usage/Read/Write with fixed Usage max and auto throughput maxima.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("Usage", "disk:/", "#10b981", 100), new ChartSpec("Read", "disk:/:read", "#3b82f6", null), new ChartSpec("Write", "disk:/:write", "#f59e0b", null) }, actual.ToArray());
    }

    [TestMethod]
    public void PerDisk_EmptySuffix()
    {
        var actual = TabCharts.For("disk:", null);
        // oracle: section 4 any ordinal disk: tab produces Usage/Read/Write with fixed Usage max and auto throughput maxima.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("Usage", "disk:", "#10b981", 100), new ChartSpec("Read", "disk::read", "#3b82f6", null), new ChartSpec("Write", "disk::write", "#f59e0b", null) }, actual.ToArray());
    }

    [TestMethod]
    public void PerDisk_MountWithSpaces()
    {
        var actual = TabCharts.For("disk:/media/My Data", null);
        // oracle: section 4 any ordinal disk: tab produces Usage/Read/Write with fixed Usage max and auto throughput maxima.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("Usage", "disk:/media/My Data", "#10b981", 100), new ChartSpec("Read", "disk:/media/My Data:read", "#3b82f6", null), new ChartSpec("Write", "disk:/media/My Data:write", "#f59e0b", null) }, actual.ToArray());
    }

    [TestMethod]
    public void PerDisk_DriveLetter()
    {
        var actual = TabCharts.For("disk:C", null);
        // oracle: section 4 any ordinal disk: tab produces Usage/Read/Write with fixed Usage max and auto throughput maxima.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("Usage", "disk:C", "#10b981", 100), new ChartSpec("Read", "disk:C:read", "#3b82f6", null), new ChartSpec("Write", "disk:C:write", "#f59e0b", null) }, actual.ToArray());
    }

    [TestMethod]
    public void PerDisk_ColonSuffix()
    {
        var actual = TabCharts.For("disk:C:", null);
        // oracle: section 4 any ordinal disk: tab produces Usage/Read/Write with fixed Usage max and auto throughput maxima.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("Usage", "disk:C:", "#10b981", 100), new ChartSpec("Read", "disk:C::read", "#3b82f6", null), new ChartSpec("Write", "disk:C::write", "#f59e0b", null) }, actual.ToArray());
    }

    [TestMethod]
    public void PerDisk_MixedCaseSuffix()
    {
        var actual = TabCharts.For("disk:/DATA", null);
        // oracle: section 4 any ordinal disk: tab produces Usage/Read/Write with fixed Usage max and auto throughput maxima.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("Usage", "disk:/DATA", "#10b981", 100), new ChartSpec("Read", "disk:/DATA:read", "#3b82f6", null), new ChartSpec("Write", "disk:/DATA:write", "#f59e0b", null) }, actual.ToArray());
    }

    [TestMethod]
    public void PerDisk_IgnoresHistoryContents()
    {
        var actual = TabCharts.For("disk:/data", new string[] { null!, "disk:/other", "core:0" });
        // oracle: section 4 per-disk chart keys come from the selected tab regardless of history existence.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("Usage", "disk:/data", "#10b981", 100), new ChartSpec("Read", "disk:/data:read", "#3b82f6", null), new ChartSpec("Write", "disk:/data:write", "#f59e0b", null) }, actual.ToArray());
    }

    [TestMethod]
    public void ChartSpec_PositionalPropertiesAndRecordEquality()
    {
        var a = new ChartSpec("Read", "disk:/:read", "#3b82f6", null);
        var b = new ChartSpec("Read", "disk:/:read", "#3b82f6", null);
        // oracle: section 4 ChartSpec is a positional record with Title, Key, ColorHex and nullable FixedMax.
        Assert.AreEqual("Read", a.Title);
        Assert.AreEqual("disk:/:read", a.Key);
        Assert.AreEqual("#3b82f6", a.ColorHex);
        Assert.IsNull(a.FixedMax);
        Assert.AreEqual(a, b);
        Assert.AreNotEqual(a, new ChartSpec("Read", "disk:/:read", "#3b82f6", 100));
    }

    [TestMethod]
    public void DiskOverview_CaseDistinctMountsStayDistinct()
    {
        var actual = TabCharts.For("disk", new[] { "disk:/data", "disk:/DATA", "disk:/data:read", "disk:/DATA:write" });
        // oracle: section 4 preserves each case-sensitive mount key and its order, while excluding the throughput series.
        CollectionAssert.AreEqual(new[]
        {
            new ChartSpec("/data", "disk:/data", "#10b981", 100),
            new ChartSpec("/DATA", "disk:/DATA", "#10b981", 100)
        }, actual.ToArray());
    }

}
