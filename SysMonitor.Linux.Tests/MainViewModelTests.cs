#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Specialized;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class MainViewModelTests
{
    // All calls in each test run sequentially on one thread, per the UI-thread contract.
    private static Snapshot Sample(int cpu = 10, int ram = 20, int disk = 30,
        bool includeDisk = true, bool includeNetwork = true, string letter = "/", string label = "Root") => new Snapshot
    {
        Ready = true,
        CpuTotal = cpu,
        CpuTemp = 60,
        Cores = new[] { new Core { Usage = cpu, Temp = 50 } },
        Ram = new Ram { Usage = ram, UsedGb = 2, TotalGb = 8 },
        Disks = includeDisk
            ? new[] { new Disk { Letter = letter, Label = label, TotalGb = 100, UsedGb = 30,
                Usage = disk, ReadMb = 1, WriteMb = 2 } }
            : Array.Empty<Disk>(),
        Adapters = includeNetwork
            ? new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 1, UpMb = 0.5,
                TotalReceived = 1000, TotalSent = 2000 } }
            : Array.Empty<Adapter>(),
        Modules = Array.Empty<Module>()
    };

    private static List<string> Watch(MainViewModel vm)
    {
        var names = new List<string>();
        vm.PropertyChanged += (_, e) => names.Add(e.PropertyName ?? "<null>");
        return names;
    }

    private static void Events(List<string> actual, params string[] expected)
    {
        // oracle: section 3 specifies an unordered event-name set with each name raised at most once.
        Assert.AreEqual(actual.Count, actual.Distinct(StringComparer.Ordinal).Count(), "Duplicate event names");
        CollectionAssert.AreEquivalent(expected, actual.ToArray());
    }

    private static List<NotifyCollectionChangedEventArgs> Changes(INotifyCollectionChanged collection)
    {
        var result = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => result.Add(e);
        return result;
    }

    private static void Actions(List<NotifyCollectionChangedEventArgs> actual, params NotifyCollectionChangedAction[] expected)
    {
        // oracle: section 3 collection changes are ordered; equal keys replace only unequal elements, otherwise Reset then Adds.
        CollectionAssert.AreEqual(expected, actual.Select(e => e.Action).ToArray());
    }

    [TestMethod]
    public void Constructor_NullSettingsDefaults()
    {
        var vm = new MainViewModel(null);
        // oracle: section 3 null settings uses defaults; constructor fixes empty FullTab to cpu.
        Assert.IsNotNull(vm.Settings);
        Assert.AreEqual("light", vm.Settings.Theme);
        Assert.AreEqual("cpu", vm.Settings.FullTab);
        Assert.AreEqual(ViewMode.Mini, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Mini, vm.EffectiveMode);
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
    }

    [TestMethod]
    public void Constructor_SanitizesSuppliedInstance()
    {
        var settings = new AppSettings { Theme = " DARK ", Mode = " EXPANDED ", MiniW = 50, Opacity = double.NaN };
        var vm = new MainViewModel(settings);
        // oracle: section 3 keeps the same settings instance and calls Sanitize.
        Assert.AreSame(settings, vm.Settings);
        Assert.AreEqual("dark", settings.Theme);
        Assert.AreEqual("expanded", settings.Mode);
        Assert.AreEqual(100.0, settings.MiniW);
        Assert.AreEqual(0.92, settings.Opacity);
    }

    [TestMethod]
    public void Constructor_Size_MiniCustom()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "mini", MiniW = 333, MiniH = 222 });
        // oracle: section 3 initial client size comes from requested mode; Full needs both dimensions and clamps to 640x480.
        Assert.AreEqual(333.0, vm.WindowWidth);
        Assert.AreEqual(222.0, vm.WindowHeight);
        Assert.AreEqual(ViewMode.Mini, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Mini, vm.EffectiveMode);
    }

    [TestMethod]
    public void Constructor_Size_ExpandedCustom()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "expanded", ExpW = 800, ExpH = 600 });
        // oracle: section 3 initial client size comes from requested mode; Full needs both dimensions and clamps to 640x480.
        Assert.AreEqual(800.0, vm.WindowWidth);
        Assert.AreEqual(600.0, vm.WindowHeight);
        Assert.AreEqual(ViewMode.Expanded, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Expanded, vm.EffectiveMode);
    }

    [TestMethod]
    public void Constructor_Size_FullUnset()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full" });
        // oracle: section 3 initial client size comes from requested mode; Full needs both dimensions and clamps to 640x480.
        Assert.AreEqual(1024.0, vm.WindowWidth);
        Assert.AreEqual(768.0, vm.WindowHeight);
        Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Full, vm.EffectiveMode);
    }

    [TestMethod]
    public void Constructor_Size_FullWidthOnly()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full", FullW = 1500 });
        // oracle: section 3 initial client size comes from requested mode; Full needs both dimensions and clamps to 640x480.
        Assert.AreEqual(1024.0, vm.WindowWidth);
        Assert.AreEqual(768.0, vm.WindowHeight);
        Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Full, vm.EffectiveMode);
    }

    [TestMethod]
    public void Constructor_Size_FullHeightOnly()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full", FullH = 900 });
        // oracle: section 3 initial client size comes from requested mode; Full needs both dimensions and clamps to 640x480.
        Assert.AreEqual(1024.0, vm.WindowWidth);
        Assert.AreEqual(768.0, vm.WindowHeight);
        Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Full, vm.EffectiveMode);
    }

    [TestMethod]
    public void Constructor_Size_FullCustom()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full", FullW = 1400, FullH = 900 });
        // oracle: section 3 initial client size comes from requested mode; Full needs both dimensions and clamps to 640x480.
        Assert.AreEqual(1400.0, vm.WindowWidth);
        Assert.AreEqual(900.0, vm.WindowHeight);
        Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Full, vm.EffectiveMode);
    }

    [TestMethod]
    public void Constructor_Size_FullMinimum()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full", FullW = 640, FullH = 480 });
        // oracle: section 3 initial client size comes from requested mode; Full needs both dimensions and clamps to 640x480.
        Assert.AreEqual(640.0, vm.WindowWidth);
        Assert.AreEqual(480.0, vm.WindowHeight);
        Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Full, vm.EffectiveMode);
    }

    [TestMethod]
    public void Constructor_Size_FullClamped()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full", FullW = 200, FullH = 100 });
        // oracle: section 3 initial client size comes from requested mode; Full needs both dimensions and clamps to 640x480.
        Assert.AreEqual(640.0, vm.WindowWidth);
        Assert.AreEqual(480.0, vm.WindowHeight);
        Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Full, vm.EffectiveMode);
    }

    [TestMethod]
    public void Constructor_InitialItemsSnapshotAndHistory()
    {
        var vm = new MainViewModel(null);
        // oracle: section 3 starts with no snapshot/items/current and empty 120-point history.
        Assert.IsNull(vm.Snapshot);
        Assert.IsNull(vm.Current);
        Assert.AreEqual(0, vm.Items.Count);
        Assert.AreEqual(0, vm.HistoryVersion);
        Assert.AreEqual(0, vm.History.Keys.Count);
    }

    [TestMethod]
    public void Constructor_ThreeNoDataTabs()
    {
        var vm = new MainViewModel(null);
        // oracle: sections 3 and 5a/5 FullTabs(null) gives cpu/memory/disk with one No data line each.
        CollectionAssert.AreEqual(new[] { "cpu", "memory", "disk" }, vm.Tabs.Select(t => t.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "CPU", "Memory", "Disk" }, vm.Tabs.Select(t => t.Title).ToArray());
        foreach (var tab in vm.Tabs)
        {
            // oracle: 5a/5 every initial tab has a single No data line.
            CollectionAssert.AreEqual(new[] { "No data" }, tab.Lines.ToArray());
        }
    }

    [TestMethod]
    public void Constructor_TabFixup_Empty()
    {
        var settings = new AppSettings { FullTab = "" };
        var vm = new MainViewModel(settings);
        // oracle: section 3 constructor selects first tab for unknown keys except ordinal disk: prefix is preserved.
        Assert.AreEqual("cpu", vm.SelectedTabKey);
        Assert.AreEqual("cpu", settings.FullTab);
        Assert.AreSame(vm.Tabs.First(t => t.Key == vm.SelectedTabKey), vm.SelectedTab);
    }

    [TestMethod]
    public void Constructor_TabFixup_Unknown()
    {
        var settings = new AppSettings { FullTab = "missing" };
        var vm = new MainViewModel(settings);
        // oracle: section 3 constructor selects first tab for unknown keys except ordinal disk: prefix is preserved.
        Assert.AreEqual("cpu", vm.SelectedTabKey);
        Assert.AreEqual("cpu", settings.FullTab);
        Assert.AreSame(vm.Tabs.First(t => t.Key == vm.SelectedTabKey), vm.SelectedTab);
    }

    [TestMethod]
    public void Constructor_TabFixup_Known()
    {
        var settings = new AppSettings { FullTab = "memory" };
        var vm = new MainViewModel(settings);
        // oracle: section 3 constructor selects first tab for unknown keys except ordinal disk: prefix is preserved.
        Assert.AreEqual("memory", vm.SelectedTabKey);
        Assert.AreEqual("memory", settings.FullTab);
        Assert.AreSame(vm.Tabs.First(t => t.Key == vm.SelectedTabKey), vm.SelectedTab);
    }

    [TestMethod]
    public void Constructor_TabFixup_PersistedDisk()
    {
        var settings = new AppSettings { FullTab = "disk:/data" };
        var vm = new MainViewModel(settings);
        // oracle: section 3 constructor selects first tab for unknown keys except ordinal disk: prefix is preserved.
        Assert.AreEqual("disk:/data", vm.SelectedTabKey);
        Assert.AreEqual("disk:/data", settings.FullTab);
        Assert.IsNull(vm.SelectedTab);
    }

    [TestMethod]
    public void Constructor_TabFixup_EmptyDiskSuffix()
    {
        var settings = new AppSettings { FullTab = "disk:" };
        var vm = new MainViewModel(settings);
        // oracle: section 3 constructor selects first tab for unknown keys except ordinal disk: prefix is preserved.
        Assert.AreEqual("disk:", vm.SelectedTabKey);
        Assert.AreEqual("disk:", settings.FullTab);
        Assert.IsNull(vm.SelectedTab);
    }

    [TestMethod]
    public void Constructor_TabFixup_CaseSensitiveDisk()
    {
        var settings = new AppSettings { FullTab = "DISK:/data" };
        var vm = new MainViewModel(settings);
        // oracle: section 3 constructor selects first tab for unknown keys except ordinal disk: prefix is preserved.
        Assert.AreEqual("cpu", vm.SelectedTabKey);
        Assert.AreEqual("cpu", settings.FullTab);
        Assert.AreSame(vm.Tabs.First(t => t.Key == vm.SelectedTabKey), vm.SelectedTab);
    }

    [TestMethod]
    public void SetWindowSize_Ignores_ZeroWidth()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(0, 555);
        // oracle: section 3 rejects the entire call unless both dimensions are finite and positive.
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
        Events(events);
    }

    [TestMethod]
    public void SetWindowSize_Ignores_NegativeWidth()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(-1, 555);
        // oracle: section 3 rejects the entire call unless both dimensions are finite and positive.
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
        Events(events);
    }

    [TestMethod]
    public void SetWindowSize_Ignores_NaNWidth()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(double.NaN, 555);
        // oracle: section 3 rejects the entire call unless both dimensions are finite and positive.
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
        Events(events);
    }

    [TestMethod]
    public void SetWindowSize_Ignores_PositiveInfinityWidth()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(double.PositiveInfinity, 555);
        // oracle: section 3 rejects the entire call unless both dimensions are finite and positive.
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
        Events(events);
    }

    [TestMethod]
    public void SetWindowSize_Ignores_NegativeInfinityWidth()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(double.NegativeInfinity, 555);
        // oracle: section 3 rejects the entire call unless both dimensions are finite and positive.
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
        Events(events);
    }

    [TestMethod]
    public void SetWindowSize_Ignores_ZeroHeight()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(555, 0);
        // oracle: section 3 rejects the entire call unless both dimensions are finite and positive.
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
        Events(events);
    }

    [TestMethod]
    public void SetWindowSize_Ignores_NegativeHeight()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(555, -1);
        // oracle: section 3 rejects the entire call unless both dimensions are finite and positive.
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
        Events(events);
    }

    [TestMethod]
    public void SetWindowSize_Ignores_NaNHeight()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(555, double.NaN);
        // oracle: section 3 rejects the entire call unless both dimensions are finite and positive.
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
        Events(events);
    }

    [TestMethod]
    public void SetWindowSize_Ignores_PositiveInfinityHeight()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(555, double.PositiveInfinity);
        // oracle: section 3 rejects the entire call unless both dimensions are finite and positive.
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
        Events(events);
    }

    [TestMethod]
    public void SetWindowSize_Ignores_NegativeInfinityHeight()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(555, double.NegativeInfinity);
        // oracle: section 3 rejects the entire call unless both dimensions are finite and positive.
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
        Events(events);
    }

    [TestMethod]
    public void SetWindowSize_Unchanged()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(270, 104);
        // oracle: section 3 stores positive finite sizes without clamping and notifies only changed values.
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
        Events(events);
    }

    [TestMethod]
    public void SetWindowSize_WidthOnly()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(300, 104);
        // oracle: section 3 stores positive finite sizes without clamping and notifies only changed values.
        Assert.AreEqual(300.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
        Events(events, "WindowWidth");
    }

    [TestMethod]
    public void SetWindowSize_HeightOnly()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(270, 120);
        // oracle: section 3 stores positive finite sizes without clamping and notifies only changed values.
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(120.0, vm.WindowHeight);
        Events(events, "WindowHeight");
    }

    [TestMethod]
    public void SetWindowSize_Both()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(300, 120);
        // oracle: section 3 stores positive finite sizes without clamping and notifies only changed values.
        Assert.AreEqual(300.0, vm.WindowWidth);
        Assert.AreEqual(120.0, vm.WindowHeight);
        Events(events, "WindowWidth", "WindowHeight");
    }

    [TestMethod]
    public void SetWindowSize_TinyPositive()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SetWindowSize(0.001, 0.002);
        // oracle: section 3 stores positive finite sizes without clamping and notifies only changed values.
        Assert.AreEqual(0.001, vm.WindowWidth);
        Assert.AreEqual(0.002, vm.WindowHeight);
        Events(events, "WindowWidth", "WindowHeight");
    }

    [TestMethod]
    public void SetWindowSize_Full_AtThreshold()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full" });
        var events = Watch(vm);
        vm.SetWindowSize(640, 480);
        // oracle: section 3 and 5a/1 Full requires width >=640 AND height >=480; RequestedMode stays Full.
        Assert.AreEqual(ViewMode.Full, vm.EffectiveMode);
        Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
        Assert.AreEqual("full", vm.Settings.Mode);
        Events(events, "WindowWidth", "WindowHeight");
    }

    [TestMethod]
    public void SetWindowSize_Full_WidthJustBelow()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full" });
        var events = Watch(vm);
        vm.SetWindowSize(639.999, 480);
        // oracle: section 3 and 5a/1 Full requires width >=640 AND height >=480; RequestedMode stays Full.
        Assert.AreEqual(ViewMode.Expanded, vm.EffectiveMode);
        Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
        Assert.AreEqual("full", vm.Settings.Mode);
        Events(events, "WindowWidth", "WindowHeight", "EffectiveMode");
    }

    [TestMethod]
    public void SetWindowSize_Full_HeightJustBelow()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full" });
        var events = Watch(vm);
        vm.SetWindowSize(640, 479.999);
        // oracle: section 3 and 5a/1 Full requires width >=640 AND height >=480; RequestedMode stays Full.
        Assert.AreEqual(ViewMode.Expanded, vm.EffectiveMode);
        Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
        Assert.AreEqual("full", vm.Settings.Mode);
        Events(events, "WindowWidth", "WindowHeight", "EffectiveMode");
    }

    [TestMethod]
    public void SetWindowSize_Full_BothBelow()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full" });
        var events = Watch(vm);
        vm.SetWindowSize(600, 400);
        // oracle: section 3 and 5a/1 Full requires width >=640 AND height >=480; RequestedMode stays Full.
        Assert.AreEqual(ViewMode.Expanded, vm.EffectiveMode);
        Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
        Assert.AreEqual("full", vm.Settings.Mode);
        Events(events, "WindowWidth", "WindowHeight", "EffectiveMode");
    }

    [TestMethod]
    public void SetWindowSize_ExitFallback()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full" });
        vm.SetWindowSize(600, 400);
        var events = Watch(vm);
        vm.SetWindowSize(640, 480);
        // oracle: section 3 restoring both thresholds makes Full effective.
        Assert.AreEqual(ViewMode.Full, vm.EffectiveMode);
        Events(events, "WindowWidth", "WindowHeight", "EffectiveMode");
    }

    [TestMethod]
    public void SetWindowSize_DoesNotPersistModeDimensions()
    {
        var vm = new MainViewModel(null);
        vm.SetWindowSize(320, 180);
        // oracle: section 3 SetWindowSize changes VM client dimensions; persistence of client dimensions belongs to Layer 2.
        Assert.AreEqual(270.0, vm.Settings.MiniW);
        Assert.AreEqual(104.0, vm.Settings.MiniH);
    }

    [TestMethod]
    public void SetMode_AppliesMiniSettingsSize()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "expanded", MiniW = 333, MiniH = 222, ExpW = 800, ExpH = 600, FullW = 1400, FullH = 900 });
        vm.SetWindowSize(350, 240);
        var events = Watch(vm);
        vm.SetMode(ViewMode.Mini);
        // oracle: section 3 changing requested mode reapplies the new mode settings size and persists its string.
        Assert.AreEqual(ViewMode.Mini, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Mini, vm.EffectiveMode);
        Assert.AreEqual("mini", vm.Settings.Mode);
        Assert.AreEqual(333.0, vm.WindowWidth);
        Assert.AreEqual(222.0, vm.WindowHeight);
        Events(events, "RequestedMode", "EffectiveMode", "WindowWidth", "WindowHeight");
    }

    [TestMethod]
    public void SetMode_AppliesExpandedSettingsSize()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "mini", MiniW = 333, MiniH = 222, ExpW = 800, ExpH = 600, FullW = 1400, FullH = 900 });
        vm.SetWindowSize(350, 240);
        var events = Watch(vm);
        vm.SetMode(ViewMode.Expanded);
        // oracle: section 3 changing requested mode reapplies the new mode settings size and persists its string.
        Assert.AreEqual(ViewMode.Expanded, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Expanded, vm.EffectiveMode);
        Assert.AreEqual("expanded", vm.Settings.Mode);
        Assert.AreEqual(800.0, vm.WindowWidth);
        Assert.AreEqual(600.0, vm.WindowHeight);
        Events(events, "RequestedMode", "EffectiveMode", "WindowWidth", "WindowHeight");
    }

    [TestMethod]
    public void SetMode_AppliesFullSettingsSize()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "mini", MiniW = 333, MiniH = 222, ExpW = 800, ExpH = 600, FullW = 1400, FullH = 900 });
        vm.SetWindowSize(350, 240);
        var events = Watch(vm);
        vm.SetMode(ViewMode.Full);
        // oracle: section 3 changing requested mode reapplies the new mode settings size and persists its string.
        Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Full, vm.EffectiveMode);
        Assert.AreEqual("full", vm.Settings.Mode);
        Assert.AreEqual(1400.0, vm.WindowWidth);
        Assert.AreEqual(900.0, vm.WindowHeight);
        Events(events, "RequestedMode", "EffectiveMode", "WindowWidth", "WindowHeight");
    }

    [TestMethod]
    public void SetMode_SameModeDoesNotReapplySize()
    {
        var vm = new MainViewModel(null);
        vm.SetWindowSize(350, 180);
        var events = Watch(vm);
        vm.SetMode(ViewMode.Mini);
        // oracle: section 3 equal RequestedMode is a no-op while effective mode matches.
        Assert.AreEqual(350.0, vm.WindowWidth);
        Assert.AreEqual(180.0, vm.WindowHeight);
        Events(events);
    }

    [TestMethod]
    public void SetMode_SameRequestedAndEffectiveModeRaisesNoEvents()
    {
        foreach (var mode in new[] { ViewMode.Mini, ViewMode.Expanded, ViewMode.Full })
        {
            var vm = new MainViewModel(new AppSettings { Mode = mode.ToString().ToLowerInvariant() });
            // oracle: section 3 initial requested and effective modes match for Mini, Expanded and default-size Full.
            Assert.AreEqual(mode, vm.RequestedMode);
            Assert.AreEqual(mode, vm.EffectiveMode);
            var events = Watch(vm);
            vm.SetMode(mode);
            // oracle: ROUND-3 RULING SetMode(m) raises nothing when m == RequestedMode and EffectiveMode == m.
            Events(events);
        }
    }

    [TestMethod]
    public void SetMode_OnlyChangedNamesWhenSizesEqual()
    {
        var vm = new MainViewModel(new AppSettings { MiniW = 630, MiniH = 480 });
        var events = Watch(vm);
        vm.SetMode(ViewMode.Expanded);
        // oracle: section 3 mode changes notify only values that changed; equal dimensions stay silent.
        Events(events, "RequestedMode", "EffectiveMode");
        Assert.AreEqual("expanded", vm.Settings.Mode);
    }

    [TestMethod]
    public void SetMode_FallbackToExpandedDoesNotNotifyEffectiveMode()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full", ExpW = 600, ExpH = 400 });
        vm.SetWindowSize(600, 400);
        var events = Watch(vm);
        vm.SetMode(ViewMode.Expanded);
        // oracle: section 3 effective Expanded already matches new mode and dimensions, so only RequestedMode changes.
        Assert.AreEqual(ViewMode.Expanded, vm.RequestedMode);
        Assert.AreEqual("expanded", vm.Settings.Mode);
        Events(events, "RequestedMode");
    }

    [TestMethod]
    public void SetMode_FullReentry_Unset()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full" });
        vm.SetWindowSize(600, 400);
        var events = Watch(vm);
        vm.SetMode(ViewMode.Full);
        // oracle: ROUND-3 RULING Full re-entry restores size and always raises RequestedMode; effective mode and both dimensions change.
        Assert.AreEqual(ViewMode.Full, vm.EffectiveMode);
        Assert.AreEqual(1024.0, vm.WindowWidth);
        Assert.AreEqual(768.0, vm.WindowHeight);
        Events(events, "RequestedMode", "EffectiveMode", "WindowWidth", "WindowHeight");
    }

    [TestMethod]
    public void SetMode_FullReentry_Clamped()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full", FullW = 200, FullH = 100 });
        vm.SetWindowSize(600, 400);
        var events = Watch(vm);
        vm.SetMode(ViewMode.Full);
        // oracle: ROUND-3 RULING Full re-entry restores size and always raises RequestedMode; effective mode and both dimensions change.
        Assert.AreEqual(ViewMode.Full, vm.EffectiveMode);
        Assert.AreEqual(640.0, vm.WindowWidth);
        Assert.AreEqual(480.0, vm.WindowHeight);
        Events(events, "RequestedMode", "EffectiveMode", "WindowWidth", "WindowHeight");
    }

    [TestMethod]
    public void SetMode_FullReentry_Custom()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full", FullW = 1400, FullH = 900 });
        vm.SetWindowSize(600, 400);
        var events = Watch(vm);
        vm.SetMode(ViewMode.Full);
        // oracle: ROUND-3 RULING Full re-entry restores size and always raises RequestedMode; effective mode and both dimensions change.
        Assert.AreEqual(ViewMode.Full, vm.EffectiveMode);
        Assert.AreEqual(1400.0, vm.WindowWidth);
        Assert.AreEqual(900.0, vm.WindowHeight);
        Events(events, "RequestedMode", "EffectiveMode", "WindowWidth", "WindowHeight");
    }

    [TestMethod]
    public void CycleMode_MiniToExpanded()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "mini" });
        var events = Watch(vm);
        vm.CycleMode();
        // oracle: section 3 CycleMode uses Next(EffectiveMode).
        Assert.AreEqual(ViewMode.Expanded, vm.RequestedMode);
        Assert.AreEqual("expanded", vm.Settings.Mode);
        Assert.AreEqual(630.0, vm.WindowWidth);
        Assert.AreEqual(480.0, vm.WindowHeight);
        Events(events, "RequestedMode", "EffectiveMode", "WindowWidth", "WindowHeight");
    }

    [TestMethod]
    public void CycleMode_ExpandedToFull()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "expanded" });
        var events = Watch(vm);
        vm.CycleMode();
        // oracle: section 3 CycleMode uses Next(EffectiveMode).
        Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
        Assert.AreEqual("full", vm.Settings.Mode);
        Assert.AreEqual(1024.0, vm.WindowWidth);
        Assert.AreEqual(768.0, vm.WindowHeight);
        Events(events, "RequestedMode", "EffectiveMode", "WindowWidth", "WindowHeight");
    }

    [TestMethod]
    public void CycleMode_FullToMini()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full" });
        var events = Watch(vm);
        vm.CycleMode();
        // oracle: section 3 CycleMode uses Next(EffectiveMode).
        Assert.AreEqual(ViewMode.Mini, vm.RequestedMode);
        Assert.AreEqual("mini", vm.Settings.Mode);
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
        Events(events, "RequestedMode", "EffectiveMode", "WindowWidth", "WindowHeight");
    }

    [TestMethod]
    public void CycleMode_FallbackExpandedReentersFull()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full" });
        vm.SetWindowSize(600, 400);
        var events = Watch(vm);
        vm.CycleMode();
        // oracle: section 3 Cycle from fallback Expanded calls SetMode(Full); ROUND-3 RULING re-entry always raises RequestedMode, plus changed effective mode and dimensions.
        Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Full, vm.EffectiveMode);
        Assert.AreEqual(1024.0, vm.WindowWidth);
        Assert.AreEqual(768.0, vm.WindowHeight);
        Events(events, "RequestedMode", "EffectiveMode", "WindowWidth", "WindowHeight");
    }

    [TestMethod]
    public void Apply_NullBeforeFirstIsCompleteNoOp()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        var items = Changes(vm.Items);
        var tabs = Changes(vm.Tabs);
        vm.Apply(null, 0);
        // oracle: section 3 null Apply does nothing, including no collection or property events.
        Assert.IsNull(vm.Snapshot);
        Assert.IsNull(vm.Current);
        Assert.AreEqual(0, vm.HistoryVersion);
        Assert.AreEqual(0, vm.History.Keys.Count);
        Assert.AreEqual(0, vm.Items.Count);
        Assert.AreEqual(3, vm.Tabs.Count);
        Events(events);
        Actions(items);
        Actions(tabs);
    }

    [TestMethod]
    public void Apply_NullAfterSnapshotPreservesAllState()
    {
        var vm = new MainViewModel(null);
        var sample = Sample();
        vm.Apply(sample, 0);
        var current = vm.Current;
        var events = Watch(vm);
        var items = Changes(vm.Items);
        var tabs = Changes(vm.Tabs);
        vm.Apply(null, 100000);
        // oracle: section 3 null Apply changes no snapshot, history, rotation, selection or collections.
        Assert.AreSame(sample, vm.Snapshot);
        Assert.AreSame(current, vm.Current);
        Assert.AreEqual(1, vm.HistoryVersion);
        Assert.AreEqual(1, vm.History.Get("cpu")!.Count);
        Assert.AreEqual("cpu", vm.SelectedTabKey);
        Events(events);
        Actions(items);
        Actions(tabs);
    }

    [TestMethod]
    public void Apply_FirstHasExactEventSet()
    {
        var vm = new MainViewModel(null);
        var sample = Sample();
        var events = Watch(vm);
        vm.Apply(sample, 0);
        // oracle: section 3 first Apply always notifies four names plus Current, with no Items/Tabs property event.
        Assert.AreSame(sample, vm.Snapshot);
        Assert.AreEqual(1, vm.HistoryVersion);
        Assert.AreEqual("cpu", vm.Current!.Key);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts", "Current");
    }

    [TestMethod]
    public void Apply_UnchangedHasOnlyMandatoryEvents()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.Apply(Sample(), 100);
        // oracle: section 3 equal current record needs no Current event; each non-null Apply has mandatory four events.
        Assert.AreEqual(2, vm.HistoryVersion);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts");
    }

    [TestMethod]
    public void Apply_CurrentRecordChangedSameKeyNotifies()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.Apply(Sample(cpu: 11), 100);
        // oracle: section 3 Current compares record value as well as key.
        Assert.AreEqual("cpu", vm.Current!.Key);
        Assert.AreEqual(11, vm.Current.Usage);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts", "Current");
    }

    [TestMethod]
    public void Apply_NonCurrentItemChangeDoesNotNotifyCurrent()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.Apply(Sample(ram: 21), 100);
        // oracle: section 3 Current stays equal when only RAM changes while CPU selected.
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts");
    }

    [TestMethod]
    public void Apply_InvalidSelectionFixupNotifiesOnce()
    {
        var vm = new MainViewModel(null);
        vm.SelectedTabKey = "unknown";
        var events = Watch(vm);
        vm.Apply(Sample(), 0);
        // oracle: H06 Apply fix-up emits SelectedTabKey once and SelectedTab/SelectedCharts once each.
        Assert.AreEqual("cpu", vm.SelectedTabKey);
        Assert.AreEqual("cpu", vm.Settings.FullTab);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts", "SelectedTabKey", "Current");
    }

    [TestMethod]
    public void Apply_ValidSelectionSurvivesNewSnapshot()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.SelectedTabKey = "memory";
        var events = Watch(vm);
        vm.Apply(Sample(ram: 25), 100);
        // oracle: section 3 valid selected key is retained when tab content is replaced.
        Assert.AreEqual("memory", vm.SelectedTabKey);
        Assert.AreEqual("memory", vm.Settings.FullTab);
        Assert.AreSame(vm.Tabs[1], vm.SelectedTab);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts");
    }

    [TestMethod]
    public void Apply_PersistedDiskTabBecomesAvailable()
    {
        var vm = new MainViewModel(new AppSettings { FullTab = "disk:/" });
        var events = Watch(vm);
        vm.Apply(Sample(), 0);
        // oracle: section 3 a persisted disk tab survives constructor and first Apply when present.
        Assert.AreEqual("disk:/", vm.SelectedTabKey);
        Assert.AreSame(vm.Tabs[3], vm.SelectedTab);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts", "Current");
    }

    [TestMethod]
    public void Apply_MissingPersistedDiskTabFallsBack()
    {
        var vm = new MainViewModel(new AppSettings { FullTab = "disk:/missing" });
        var events = Watch(vm);
        vm.Apply(Sample(), 0);
        // oracle: section 3 first Apply fixes missing persisted disk key to first tab.
        Assert.AreEqual("cpu", vm.SelectedTabKey);
        Assert.AreEqual("cpu", vm.Settings.FullTab);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts", "SelectedTabKey", "Current");
    }

    [TestMethod]
    public void Apply_RemovedSelectedDiskFallsBack()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.SelectedTabKey = "disk:/";
        var events = Watch(vm);
        vm.Apply(Sample(includeDisk: false), 100);
        // oracle: section 3 disappearing selected tab falls back to cpu with one notification of each name.
        Assert.AreEqual("cpu", vm.SelectedTabKey);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts", "SelectedTabKey");
    }

    [TestMethod]
    public void Apply_HistoryValuesOrderAndVersion()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.Apply(Sample(cpu: 15), 1);
        // oracle: section 3 History.Record precedes notification; clarification 04(d) specifies creation order and MiB rate values.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "core:0", "disk:/", "disk:/:read", "disk:/:write", "net:eth0:down", "net:eth0:up" }, vm.History.Keys.ToArray());
        Assert.AreEqual(2, vm.HistoryVersion);
        Assert.AreEqual(2, vm.History.Get("cpu")!.Count);
        Assert.AreEqual(15.0, vm.History.Get("cpu")!.Latest);
        Assert.AreEqual(20.0, vm.History.Get("ram")!.Latest);
        Assert.AreEqual(1.0, vm.History.Get("disk:/:read")!.Latest);
        Assert.AreEqual(0.5, vm.History.Get("net:eth0:up")!.Latest);
    }

    [TestMethod]
    public void Apply_HistoryCapacityIs120()
    {
        var vm = new MainViewModel(null);
        for (var i = 0; i < 121; i++) vm.Apply(Sample(cpu: i % 100), i);
        // oracle: section 3 History is HistoryStore(120): 121 samples retain 120; latest CPU is 120 mod 100 = 20.
        Assert.AreEqual(120, vm.History.Get("cpu")!.Count);
        Assert.AreEqual(20.0, vm.History.Get("cpu")!.Latest);
        Assert.AreEqual(121, vm.HistoryVersion);
    }

    [TestMethod]
    public void Apply_NotReadySnapshotStillApplied()
    {
        var vm = new MainViewModel(null);
        var sample = new Snapshot { Ready = false, CpuTotal = 7, Ram = new Ram(), Cores = Array.Empty<Core>(), Disks = Array.Empty<Disk>(), Adapters = Array.Empty<Adapter>(), Modules = Array.Empty<Module>() };
        var events = Watch(vm);
        vm.Apply(sample, 0);
        // oracle: section 3 every non-null snapshot is applied regardless of Ready.
        Assert.AreSame(sample, vm.Snapshot);
        Assert.AreEqual(2, vm.Items.Count);
        Assert.AreEqual(7.0, vm.History.Get("cpu")!.Latest);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts", "Current");
    }

    [TestMethod]
    public void Apply_ItemsHaveHandCalculatedMetricValues()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        // oracle: section 3 uses MetricBuilder; 04(b) network round(1.5*8.388608/100*100)=13; all usages below 70 use accent.
        CollectionAssert.AreEqual(new[]
        {
            new MetricItem(MetricKind.Cpu, "cpu", "CPU", "1 core", 10, "10% 60 C", "#3b82f6", "#3b82f6"),
            new MetricItem(MetricKind.Ram, "ram", "Memory", "2.0 GB / 8.0 GB", 20, "20%", "#a855f7", "#a855f7"),
            new MetricItem(MetricKind.Disk, "disk:/", "Root", "/ - 30.0 GB / 100.0 GB", 30, "30%", "#10b981", "#10b981"),
            new MetricItem(MetricKind.Network, "net:eth0", "eth0", "LAN 100 Mbps", 13, "Down 1.0 MB/s  Up 512 KB/s", "#06b6d4", "#06b6d4")
        }, vm.Items.ToArray());
    }

    [TestMethod]
    public void Apply_Filter_ShowCpu()
    {
        var vm = new MainViewModel(new AppSettings { ShowCpu = false });
        vm.Apply(Sample(), 0);
        // oracle: section 3 ShowCpu hides only its MetricKind; Tabs and History still use full snapshot.
        CollectionAssert.AreEqual(new[] { "ram", "disk:/", "net:eth0" }, vm.Items.Select(i => i.Key).ToArray());
        Assert.AreEqual(4, vm.Tabs.Count);
        Assert.AreEqual(8, vm.History.Keys.Count);
    }

    [TestMethod]
    public void Apply_Filter_ShowRam()
    {
        var vm = new MainViewModel(new AppSettings { ShowRam = false });
        vm.Apply(Sample(), 0);
        // oracle: section 3 ShowRam hides only its MetricKind; Tabs and History still use full snapshot.
        CollectionAssert.AreEqual(new[] { "cpu", "disk:/", "net:eth0" }, vm.Items.Select(i => i.Key).ToArray());
        Assert.AreEqual(4, vm.Tabs.Count);
        Assert.AreEqual(8, vm.History.Keys.Count);
    }

    [TestMethod]
    public void Apply_Filter_ShowDisk()
    {
        var vm = new MainViewModel(new AppSettings { ShowDisk = false });
        vm.Apply(Sample(), 0);
        // oracle: section 3 ShowDisk hides only its MetricKind; Tabs and History still use full snapshot.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "net:eth0" }, vm.Items.Select(i => i.Key).ToArray());
        Assert.AreEqual(4, vm.Tabs.Count);
        Assert.AreEqual(8, vm.History.Keys.Count);
    }

    [TestMethod]
    public void Apply_Filter_ShowNetwork()
    {
        var vm = new MainViewModel(new AppSettings { ShowNetwork = false });
        vm.Apply(Sample(), 0);
        // oracle: section 3 ShowNetwork hides only its MetricKind; Tabs and History still use full snapshot.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "disk:/" }, vm.Items.Select(i => i.Key).ToArray());
        Assert.AreEqual(4, vm.Tabs.Count);
        Assert.AreEqual(8, vm.History.Keys.Count);
    }

    [TestMethod]
    public void Apply_AllFiltersOffLeavesCurrentNull()
    {
        var vm = new MainViewModel(new AppSettings { ShowCpu = false, ShowRam = false, ShowDisk = false, ShowNetwork = false });
        var events = Watch(vm);
        vm.Apply(Sample(), 0);
        // oracle: section 3 all metric kinds filtered leaves Items empty and Current null; no Current event when null remains null.
        Assert.AreEqual(0, vm.Items.Count);
        Assert.IsNull(vm.Current);
        Assert.AreEqual(4, vm.Tabs.Count);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts");
    }

    [TestMethod]
    public void Apply_FiltersTakeEffectOnlyOnNextApply()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.Settings.ShowCpu = false;
        var events = Watch(vm);
        // oracle: section 3 filtering is evaluated on Apply only.
        Assert.AreEqual(4, vm.Items.Count);
        Assert.AreEqual("cpu", vm.Current!.Key);
        vm.Apply(Sample(), 100);
        // oracle: section 3 removing selected CPU makes rotation select RAM at clamped index zero.
        CollectionAssert.AreEqual(new[] { "ram", "disk:/", "net:eth0" }, vm.Items.Select(i => i.Key).ToArray());
        Assert.AreEqual("ram", vm.Current!.Key);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts", "Current");
    }

    [TestMethod]
    public void Apply_CollectionInstancesNeverReplaced()
    {
        var vm = new MainViewModel(null);
        var items = vm.Items;
        var tabs = vm.Tabs;
        var history = vm.History;
        vm.Apply(Sample(), 0);
        vm.Apply(Sample(includeDisk: false), 10);
        // oracle: section 3 Items, Tabs and History are stable instances across Apply.
        Assert.AreSame(items, vm.Items);
        Assert.AreSame(tabs, vm.Tabs);
        Assert.AreSame(history, vm.History);
    }

    [TestMethod]
    public void Apply_UnchangedCollectionsEmitNoEvents()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var items = Changes(vm.Items);
        var tabs = Changes(vm.Tabs);
        vm.Apply(Sample(), 10);
        // oracle: section 3 equal metric records and tab Lines sequences (separate arrays) require zero collection events.
        Actions(items);
        Actions(tabs);
    }

    [TestMethod]
    public void Apply_SameKeysOnlyChangedCpuIsReplaced()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var items = Changes(vm.Items);
        var tabs = Changes(vm.Tabs);
        vm.Apply(Sample(cpu: 11), 10);
        // oracle: section 3 unchanged keys and one changed CPU item/tab yield one Replace each at index zero.
        Actions(items, NotifyCollectionChangedAction.Replace);
        Actions(tabs, NotifyCollectionChangedAction.Replace);
        Assert.AreEqual(0, items[0].NewStartingIndex);
        Assert.AreEqual(0, tabs[0].NewStartingIndex);
        Assert.AreEqual(11, ((MetricItem)items[0].NewItems![0]!).Usage);
        Assert.AreEqual(10, ((MetricItem)items[0].OldItems![0]!).Usage);
    }

    [TestMethod]
    public void Apply_SameKeysMultipleChangedElementsAreReplaced()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var items = Changes(vm.Items);
        var tabs = Changes(vm.Tabs);
        vm.Apply(Sample(cpu: 11, ram: 21, disk: 31), 10);
        // oracle: section 3 CPU/RAM/disk metrics replace indices 0,1,2; tabs at 0,1,2 change, while per-disk UsedGb/TotalGb and its Lines stay unchanged.
        Actions(items, NotifyCollectionChangedAction.Replace, NotifyCollectionChangedAction.Replace, NotifyCollectionChangedAction.Replace);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, items.Select(e => e.NewStartingIndex).ToArray());
        Actions(tabs, NotifyCollectionChangedAction.Replace, NotifyCollectionChangedAction.Replace, NotifyCollectionChangedAction.Replace);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, tabs.Select(e => e.NewStartingIndex).ToArray());
    }

    [TestMethod]
    public void Apply_FirstCollectionsResetThenAdd()
    {
        var vm = new MainViewModel(null);
        var items = Changes(vm.Items);
        var tabs = Changes(vm.Tabs);
        vm.Apply(Sample(), 0);
        // oracle: section 3 key lists change from empty to four metrics and from three to four tabs: Reset then four Adds.
        Actions(items, NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add);
        Actions(tabs, NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, items.Skip(1).Select(e => e.NewStartingIndex).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, tabs.Skip(1).Select(e => e.NewStartingIndex).ToArray());
    }

    [TestMethod]
    public void Apply_RemovedKeysResetThenAdd()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var items = Changes(vm.Items);
        var tabs = Changes(vm.Tabs);
        vm.Apply(Sample(includeDisk: false), 10);
        // oracle: section 3 removing a disk changes key sequences: Reset then three Adds in both collections.
        Actions(items, NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add);
        Actions(tabs, NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add);
    }

    [TestMethod]
    public void Apply_SameCountDifferentKeysReset()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var items = Changes(vm.Items);
        var tabs = Changes(vm.Tabs);
        vm.Apply(Sample(letter: "/data"), 10);
        // oracle: section 3 same counts do not suffice; differing keys require Reset and four Adds.
        Actions(items, NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add);
        Actions(tabs, NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add);
    }

    [TestMethod]
    public void Apply_TabTitleChangeReplacesOnlyDiskTab()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var items = Changes(vm.Items);
        var tabs = Changes(vm.Tabs);
        vm.Apply(Sample(label: "Renamed"), 10);
        // oracle: section 3 TabModel equality includes Title even when Key and Lines match.
        Actions(items, NotifyCollectionChangedAction.Replace);
        Actions(tabs, NotifyCollectionChangedAction.Replace);
        Assert.AreEqual(2, items[0].NewStartingIndex);
        Assert.AreEqual(3, tabs[0].NewStartingIndex);
        Assert.AreEqual("Renamed", vm.Tabs[3].Title);
    }

    [TestMethod]
    public void Apply_ZeroMetricsFromNonemptyOnlyResets()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        // oracle: the initial unfiltered snapshot produces four metrics with CPU selected before all kinds are hidden.
        Assert.AreEqual(4, vm.Items.Count);
        Assert.AreEqual("cpu", vm.Current!.Key);
        vm.Settings.ShowCpu = vm.Settings.ShowRam = vm.Settings.ShowDisk = vm.Settings.ShowNetwork = false;
        var items = Changes(vm.Items);
        var events = Watch(vm);
        vm.Apply(Sample(), 10);
        // oracle: section 3 changing metric keys to empty emits one Reset and Current changes to null.
        Actions(items, NotifyCollectionChangedAction.Reset);
        Assert.AreEqual(0, vm.Items.Count);
        Assert.IsNull(vm.Current);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts", "Current");
    }

    [TestMethod]
    public void Next_BeforeApplyNoEvents()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.Next(10000);
        // oracle: section 3 empty rotation stays null and does not notify Current.
        Assert.IsNull(vm.Current);
        Events(events);
    }

    [TestMethod]
    public void Previous_BeforeApplyNoEvents()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.Previous(10000);
        // oracle: section 3 empty rotation stays null and does not notify Current.
        Assert.IsNull(vm.Current);
        Events(events);
    }

    [TestMethod]
    public void Tick_BeforeApplyNoEvents()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.Tick(10000);
        // oracle: section 3 empty rotation stays null and does not notify Current.
        Assert.IsNull(vm.Current);
        Events(events);
    }

    [TestMethod]
    public void Rotation_NextAdvances()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.Next(100);
        // oracle: section 3 and 5a/4 buttons move one item with wrap and raise only Current.
        Assert.AreEqual("ram", vm.Current!.Key);
        Events(events, "Current");
    }

    [TestMethod]
    public void Rotation_PreviousWraps()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.Previous(100);
        // oracle: section 3 and 5a/4 buttons move one item with wrap and raise only Current.
        Assert.AreEqual("net:eth0", vm.Current!.Key);
        Events(events, "Current");
    }

    [TestMethod]
    public void Rotation_NextWraps()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.Previous(100);
        var events = Watch(vm);
        vm.Next(200);
        // oracle: section 3 and 5a/4 buttons move one item with wrap and raise only Current.
        Assert.AreEqual("cpu", vm.Current!.Key);
        Events(events, "Current");
    }

    [TestMethod]
    public void Tick_BeforeInterval()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.Tick(4999);
        // oracle: section 3 delegates to 5a/4: 5000 ms due, advance exactly one, backwards never advances.
        Assert.AreEqual("cpu", vm.Current!.Key);
        Events(events);
    }

    [TestMethod]
    public void Tick_AtInterval()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.Tick(5000);
        // oracle: section 3 delegates to 5a/4: 5000 ms due, advance exactly one, backwards never advances.
        Assert.AreEqual("ram", vm.Current!.Key);
        Events(events, "Current");
    }

    [TestMethod]
    public void Tick_LongDelayOneStep()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.Tick(50000);
        // oracle: section 3 delegates to 5a/4: 5000 ms due, advance exactly one, backwards never advances.
        Assert.AreEqual("ram", vm.Current!.Key);
        Events(events, "Current");
    }

    [TestMethod]
    public void Tick_Backwards()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.Tick(-1);
        // oracle: section 3 delegates to 5a/4: 5000 ms due, advance exactly one, backwards never advances.
        Assert.AreEqual("cpu", vm.Current!.Key);
        Events(events);
    }

    [TestMethod]
    public void Next_OneItemDoesNotNotify()
    {
        var vm = new MainViewModel(new AppSettings { ShowRam = false, ShowDisk = false, ShowNetwork = false });
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.Next(10000);
        // oracle: section 3 single-item rotation keeps Current equal with no event.
        Assert.AreEqual("cpu", vm.Current!.Key);
        Events(events);
    }

    [TestMethod]
    public void Previous_OneItemDoesNotNotify()
    {
        var vm = new MainViewModel(new AppSettings { ShowRam = false, ShowDisk = false, ShowNetwork = false });
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.Previous(10000);
        // oracle: section 3 single-item rotation keeps Current equal with no event.
        Assert.AreEqual("cpu", vm.Current!.Key);
        Events(events);
    }

    [TestMethod]
    public void Tick_OneItemDoesNotNotify()
    {
        var vm = new MainViewModel(new AppSettings { ShowRam = false, ShowDisk = false, ShowNetwork = false });
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.Tick(10000);
        // oracle: section 3 single-item rotation keeps Current equal with no event.
        Assert.AreEqual("cpu", vm.Current!.Key);
        Events(events);
    }

    [TestMethod]
    public void Rotation_ManualMoveRestartsTimer()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.Next(4000);
        var events = Watch(vm);
        vm.Tick(5000);
        // oracle: 5a/4 Next restarts timer: only 1000 ms elapsed, so RAM remains current.
        Assert.AreEqual("ram", vm.Current!.Key);
        Events(events);
    }

    [TestMethod]
    public void Rotation_ApplyPreservesTimerAndSelectedKey()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.Next(100);
        vm.Apply(Sample(), 3000);
        var events = Watch(vm);
        vm.Tick(5100);
        // oracle: 5a/4 Apply retains RAM key and timer; 5100-100=5000 advances to disk.
        Assert.AreEqual("disk:/", vm.Current!.Key);
        Events(events, "Current");
    }

    [TestMethod]
    public void SelectedTabKey_Null()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SelectedTabKey = null!;
        // oracle: section 3 programmatic key setter is ungated, accepts verbatim strings, null becomes empty.
        Assert.AreEqual("", vm.SelectedTabKey);
        Assert.AreEqual("", vm.Settings.FullTab);
        Events(events, "SelectedTabKey", "SelectedTab", "SelectedCharts");
        Assert.IsNull(vm.SelectedTab);
    }

    [TestMethod]
    public void SelectedTabKey_Unknown()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SelectedTabKey = "unknown";
        // oracle: section 3 programmatic key setter is ungated, accepts verbatim strings, null becomes empty.
        Assert.AreEqual("unknown", vm.SelectedTabKey);
        Assert.AreEqual("unknown", vm.Settings.FullTab);
        Events(events, "SelectedTabKey", "SelectedTab", "SelectedCharts");
        Assert.IsNull(vm.SelectedTab);
    }

    [TestMethod]
    public void SelectedTabKey_Verbatim()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SelectedTabKey = " MEMORY ";
        // oracle: section 3 programmatic key setter is ungated, accepts verbatim strings, null becomes empty.
        Assert.AreEqual(" MEMORY ", vm.SelectedTabKey);
        Assert.AreEqual(" MEMORY ", vm.Settings.FullTab);
        Events(events, "SelectedTabKey", "SelectedTab", "SelectedCharts");
        Assert.IsNull(vm.SelectedTab);
    }

    [TestMethod]
    public void SelectedTabKey_CaseSensitive()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SelectedTabKey = "CPU";
        // oracle: section 3 programmatic key setter is ungated, accepts verbatim strings, null becomes empty.
        Assert.AreEqual("CPU", vm.SelectedTabKey);
        Assert.AreEqual("CPU", vm.Settings.FullTab);
        Events(events, "SelectedTabKey", "SelectedTab", "SelectedCharts");
        Assert.IsNull(vm.SelectedTab);
    }

    [TestMethod]
    public void SelectedTabKey_Known()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SelectedTabKey = "memory";
        // oracle: section 3 programmatic key setter is ungated, accepts verbatim strings, null becomes empty.
        Assert.AreEqual("memory", vm.SelectedTabKey);
        Assert.AreEqual("memory", vm.Settings.FullTab);
        Events(events, "SelectedTabKey", "SelectedTab", "SelectedCharts");
        Assert.AreSame(vm.Tabs[1], vm.SelectedTab);
    }

    [TestMethod]
    public void SelectedTabKey_UnchangedNoEvents()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SelectedTabKey = "cpu";
        // oracle: section 3 ordinal-equal selected keys do nothing.
        Assert.AreEqual("cpu", vm.Settings.FullTab);
        Events(events);
    }

    [TestMethod]
    public void SelectedTabKey_NullWhenAlreadyEmptyNoEvents()
    {
        var vm = new MainViewModel(null);
        vm.SelectedTabKey = "";
        var events = Watch(vm);
        vm.SelectedTabKey = null!;
        // oracle: section 3 null maps to empty and ordinal equality suppresses notifications.
        Assert.AreEqual("", vm.SelectedTabKey);
        Events(events);
    }

    [TestMethod]
    public void SelectedTab_BeforeFirstApplyIgnored()
    {
        var vm = new MainViewModel(new AppSettings { FullTab = "memory" });
        var events = Watch(vm);
        vm.SelectedTab = vm.Tabs[0];
        // oracle: F01 section 3 TabControl writes are ignored until first non-null Apply.
        Assert.AreEqual("memory", vm.SelectedTabKey);
        Assert.AreEqual("memory", vm.Settings.FullTab);
        Events(events);
    }

    [TestMethod]
    public void SelectedTab_NullApplyDoesNotOpenGate()
    {
        var vm = new MainViewModel(new AppSettings { FullTab = "memory" });
        vm.Apply(null, 0);
        var events = Watch(vm);
        vm.SelectedTab = vm.Tabs[0];
        // oracle: section 3 only first non-null Apply permits TabControl selection writes.
        Assert.AreEqual("memory", vm.SelectedTabKey);
        Events(events);
    }

    [TestMethod]
    public void SelectedTab_NullAfterApplyIgnored()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.SelectedTabKey = "memory";
        var events = Watch(vm);
        vm.SelectedTab = null;
        // oracle: section 3 null TabControl writes are always ignored.
        Assert.AreEqual("memory", vm.SelectedTabKey);
        Events(events);
    }

    [TestMethod]
    public void SelectedTab_AfterApplyWritesKeyAndSettings()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.SelectedTab = vm.Tabs[1];
        // oracle: section 3 after first non-null Apply SelectedTab setter forwards value.Key.
        Assert.AreEqual("memory", vm.SelectedTabKey);
        Assert.AreEqual("memory", vm.Settings.FullTab);
        Assert.AreSame(vm.Tabs[1], vm.SelectedTab);
        Events(events, "SelectedTabKey", "SelectedTab", "SelectedCharts");
    }

    [TestMethod]
    public void SelectedTab_UnknownExternalValueAcceptedAfterApply()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.SelectedTab = new TabModel("external", "External", Array.Empty<string>());
        // oracle: section 3 SelectedTab forwards any value.Key; getter returns null when no matching tab.
        Assert.AreEqual("external", vm.SelectedTabKey);
        Assert.AreEqual("external", vm.Settings.FullTab);
        Assert.IsNull(vm.SelectedTab);
        Events(events, "SelectedTabKey", "SelectedTab", "SelectedCharts");
    }

    [TestMethod]
    public void SelectedTab_SameKeyDifferentInstanceNoEvents()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var events = Watch(vm);
        vm.SelectedTab = new TabModel("cpu", "Different", new[] { "Different" });
        // oracle: section 3 SelectedTab sets only Key; equal key emits nothing and getter returns collection tab.
        Assert.AreSame(vm.Tabs[0], vm.SelectedTab);
        Events(events);
    }

    [TestMethod]
    public void SelectedTab_WritesDuringReplaceIgnored()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.SelectedTabKey = "memory";
        var callbacks = 0;
        vm.Tabs.CollectionChanged += (_, _) =>
        {
            callbacks++;
            vm.SelectedTab = new TabModel("cpu", "CPU", Array.Empty<string>());
        };
        var events = Watch(vm);
        vm.Apply(Sample(ram: 21), 10);
        // oracle: F01 section 3 EVERY SelectedTab write while updating Tabs is ignored, for Replace, Reset and Add.
        Assert.IsTrue(callbacks > 0);
        Assert.AreEqual("memory", vm.SelectedTabKey);
        Assert.AreEqual("memory", vm.Settings.FullTab);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts");
    }

    [TestMethod]
    public void SelectedTab_WritesDuringResetAndAddsIgnored()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.SelectedTabKey = "memory";
        var callbacks = 0;
        vm.Tabs.CollectionChanged += (_, _) =>
        {
            callbacks++;
            vm.SelectedTab = new TabModel("cpu", "CPU", Array.Empty<string>());
        };
        var events = Watch(vm);
        vm.Apply(Sample(includeDisk: false), 10);
        // oracle: F01 section 3 EVERY SelectedTab write while updating Tabs is ignored, for Replace, Reset and Add.
        Assert.IsTrue(callbacks > 0);
        Assert.AreEqual("memory", vm.SelectedTabKey);
        Assert.AreEqual("memory", vm.Settings.FullTab);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts");
    }

    [TestMethod]
    public void SelectedTab_FirstApplyCallbacksCannotClobberPersistedDisk()
    {
        var vm = new MainViewModel(new AppSettings { FullTab = "disk:/" });
        vm.Tabs.CollectionChanged += (_, _) => vm.SelectedTab = new TabModel("cpu", "CPU", Array.Empty<string>());
        var events = Watch(vm);
        vm.Apply(Sample(), 0);
        // oracle: F01 first Apply collection callbacks must not overwrite persisted disk selection.
        Assert.AreEqual("disk:/", vm.SelectedTabKey);
        Assert.AreEqual("disk:/", vm.Settings.FullTab);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts", "Current");
    }

    [TestMethod]
    public void SelectedCharts_Cpu()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.SelectedTabKey = "cpu";
        // oracle: sections 3-4 SelectedCharts reflects selected tab and History.Keys, with hand-enumerated charts.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("CPU", "cpu", "#3b82f6", 100), new ChartSpec("Core 0", "core:0", "#3b82f6", 100) }, vm.SelectedCharts.ToArray());
    }

    [TestMethod]
    public void SelectedCharts_Memory()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.SelectedTabKey = "memory";
        // oracle: sections 3-4 SelectedCharts reflects selected tab and History.Keys, with hand-enumerated charts.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("Memory", "ram", "#a855f7", 100) }, vm.SelectedCharts.ToArray());
    }

    [TestMethod]
    public void SelectedCharts_Overview()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.SelectedTabKey = "disk";
        // oracle: sections 3-4 SelectedCharts reflects selected tab and History.Keys, with hand-enumerated charts.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("/", "disk:/", "#10b981", 100) }, vm.SelectedCharts.ToArray());
    }

    [TestMethod]
    public void SelectedCharts_PerDisk()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.SelectedTabKey = "disk:/";
        // oracle: sections 3-4 SelectedCharts reflects selected tab and History.Keys, with hand-enumerated charts.
        CollectionAssert.AreEqual(new ChartSpec[] { new ChartSpec("Usage", "disk:/", "#10b981", 100), new ChartSpec("Read", "disk:/:read", "#3b82f6", null), new ChartSpec("Write", "disk:/:write", "#f59e0b", null) }, vm.SelectedCharts.ToArray());
    }

    [TestMethod]
    public void SelectedCharts_Unknown()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.SelectedTabKey = "unknown";
        // oracle: sections 3-4 SelectedCharts reflects selected tab and History.Keys, with hand-enumerated charts.
        CollectionAssert.AreEqual(new ChartSpec[] {  }, vm.SelectedCharts.ToArray());
    }

    [TestMethod]
    public void SelectedCharts_BeforeApplyCpuHasOnlyTotal()
    {
        var vm = new MainViewModel(null);
        // oracle: sections 3-4 initial history has no core keys but cpu always has total chart.
        CollectionAssert.AreEqual(new[] { new ChartSpec("CPU", "cpu", "#3b82f6", 100) }, vm.SelectedCharts.ToArray());
    }

    [TestMethod]
    public void Sidebar_DefaultLightThemeAndColors()
    {
        var vm = new MainViewModel(null);
        // oracle: section 3 and phase-5b introduction light palette Panel white, Sidebar f3f5f9, BarEmpty dfe4ec.
        Assert.IsFalse(vm.IsDark);
        Assert.AreEqual("light", vm.ThemeName);
        Assert.AreEqual("#ffffff", vm.Colors.Panel);
        Assert.AreEqual("#f3f5f9", vm.Colors.Sidebar);
        Assert.AreEqual("#dfe4ec", vm.Colors.BarEmpty);
        Assert.IsTrue(vm.AlwaysOnTop);
        Assert.AreEqual(0.92, vm.Opacity);
        Assert.AreEqual("balanced", vm.Speed);
        Assert.AreEqual(1, vm.SpeedIndex);
    }

    [TestMethod]
    public void IsDark_EnableDark()
    {
        var vm = new MainViewModel(new AppSettings { Theme = "light" });
        var events = Watch(vm);
        vm.IsDark = true;
        // oracle: section 3 theme changes settings and notifies exactly IsDark, ThemeName, Colors.
        Assert.AreEqual(true, vm.IsDark);
        Assert.AreEqual("dark", vm.ThemeName);
        Assert.AreEqual("dark", vm.Settings.Theme);
        Events(events, "IsDark", "ThemeName", "Colors");
    }

    [TestMethod]
    public void IsDark_DisableDark()
    {
        var vm = new MainViewModel(new AppSettings { Theme = "dark" });
        var events = Watch(vm);
        vm.IsDark = false;
        // oracle: section 3 theme changes settings and notifies exactly IsDark, ThemeName, Colors.
        Assert.AreEqual(false, vm.IsDark);
        Assert.AreEqual("light", vm.ThemeName);
        Assert.AreEqual("light", vm.Settings.Theme);
        Events(events, "IsDark", "ThemeName", "Colors");
    }

    [TestMethod]
    public void IsDark_EqualNoEvents()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.IsDark = false;
        // oracle: section 3 sidebar setters notify only changed values.
        Events(events);
    }

    [TestMethod]
    public void ToggleTheme_FlipsBothDirections()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.ToggleTheme();
        // oracle: section 3 ToggleTheme flips IsDark and emits the three theme names.
        Assert.IsTrue(vm.IsDark);
        Assert.AreEqual("dark", vm.Settings.Theme);
        Events(events, "IsDark", "ThemeName", "Colors");
        events.Clear();
        vm.ToggleTheme();
        // oracle: section 3 second ToggleTheme restores light with same exact event set.
        Assert.IsFalse(vm.IsDark);
        Assert.AreEqual("#ffffff", vm.Colors.Panel);
        Events(events, "IsDark", "ThemeName", "Colors");
    }

    [TestMethod]
    public void AlwaysOnTop_Disable()
    {
        var vm = new MainViewModel(new AppSettings { AlwaysOnTop = true });
        var events = Watch(vm);
        vm.AlwaysOnTop = false;
        // oracle: section 3 AlwaysOnTop writes settings and emits its own name only on change.
        Assert.AreEqual(false, vm.AlwaysOnTop);
        Assert.AreEqual(false, vm.Settings.AlwaysOnTop);
        Events(events, "AlwaysOnTop");
    }

    [TestMethod]
    public void AlwaysOnTop_Enable()
    {
        var vm = new MainViewModel(new AppSettings { AlwaysOnTop = false });
        var events = Watch(vm);
        vm.AlwaysOnTop = true;
        // oracle: section 3 AlwaysOnTop writes settings and emits its own name only on change.
        Assert.AreEqual(true, vm.AlwaysOnTop);
        Assert.AreEqual(true, vm.Settings.AlwaysOnTop);
        Events(events, "AlwaysOnTop");
    }

    [TestMethod]
    public void AlwaysOnTop_Equal()
    {
        var vm = new MainViewModel(new AppSettings { AlwaysOnTop = true });
        var events = Watch(vm);
        vm.AlwaysOnTop = true;
        // oracle: section 3 AlwaysOnTop writes settings and emits its own name only on change.
        Assert.AreEqual(true, vm.AlwaysOnTop);
        Assert.AreEqual(true, vm.Settings.AlwaysOnTop);
        Events(events);
    }

    [TestMethod]
    public void Opacity_Inside()
    {
        var vm = new MainViewModel(new AppSettings { Opacity = .92 });
        var events = Watch(vm);
        vm.Opacity = 0.7;
        // oracle: section 3 Opacity setter uses section 1 NaN-default and clamp rules.
        Assert.AreEqual(0.7, vm.Opacity);
        Assert.AreEqual(0.7, vm.Settings.Opacity);
        Events(events, "Opacity");
    }

    [TestMethod]
    public void Opacity_Below()
    {
        var vm = new MainViewModel(new AppSettings { Opacity = .92 });
        var events = Watch(vm);
        vm.Opacity = 0.29;
        // oracle: section 3 Opacity setter uses section 1 NaN-default and clamp rules.
        Assert.AreEqual(0.3, vm.Opacity);
        Assert.AreEqual(0.3, vm.Settings.Opacity);
        Events(events, "Opacity");
    }

    [TestMethod]
    public void Opacity_Above()
    {
        var vm = new MainViewModel(new AppSettings { Opacity = .92 });
        var events = Watch(vm);
        vm.Opacity = 1.01;
        // oracle: section 3 Opacity setter uses section 1 NaN-default and clamp rules.
        Assert.AreEqual(1.0, vm.Opacity);
        Assert.AreEqual(1.0, vm.Settings.Opacity);
        Events(events, "Opacity");
    }

    [TestMethod]
    public void Opacity_NaN()
    {
        var vm = new MainViewModel(new AppSettings { Opacity = .7 });
        var events = Watch(vm);
        vm.Opacity = double.NaN;
        // oracle: section 3 Opacity setter uses section 1 NaN-default and clamp rules.
        Assert.AreEqual(0.92, vm.Opacity);
        Assert.AreEqual(0.92, vm.Settings.Opacity);
        Events(events, "Opacity");
    }

    [TestMethod]
    public void Opacity_PositiveInfinity()
    {
        var vm = new MainViewModel(new AppSettings { Opacity = .92 });
        var events = Watch(vm);
        vm.Opacity = double.PositiveInfinity;
        // oracle: section 3 Opacity setter uses section 1 NaN-default and clamp rules.
        Assert.AreEqual(1.0, vm.Opacity);
        Assert.AreEqual(1.0, vm.Settings.Opacity);
        Events(events, "Opacity");
    }

    [TestMethod]
    public void Opacity_NegativeInfinity()
    {
        var vm = new MainViewModel(new AppSettings { Opacity = .92 });
        var events = Watch(vm);
        vm.Opacity = double.NegativeInfinity;
        // oracle: section 3 Opacity setter uses section 1 NaN-default and clamp rules.
        Assert.AreEqual(0.3, vm.Opacity);
        Assert.AreEqual(0.3, vm.Settings.Opacity);
        Events(events, "Opacity");
    }

    [TestMethod]
    public void Opacity_NormalizedEqualNoEvents()
    {
        var vm = new MainViewModel(new AppSettings { Opacity = 1 });
        var events = Watch(vm);
        vm.Opacity = double.PositiveInfinity;
        // oracle: section 3 only sanitised effective value changes notify; infinity clamps to current 1.
        Assert.AreEqual(1.0, vm.Opacity);
        Events(events);
    }

    [TestMethod]
    public void Opacity_NaNAtDefaultNoEvents()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.Opacity = double.NaN;
        // oracle: section 3 NaN sanitises to current default 0.92, hence no event.
        Assert.AreEqual(0.92, vm.Settings.Opacity);
        Events(events);
    }

    [TestMethod]
    public void Speed_Eco()
    {
        var vm = new MainViewModel(new AppSettings { Speed = "balanced" });
        var events = Watch(vm);
        vm.Speed = "eco";
        // oracle: section 3 sanitises Speed; M05 either setter notifies Speed and SpeedIndex exactly once.
        Assert.AreEqual("eco", vm.Speed);
        Assert.AreEqual("eco", vm.Settings.Speed);
        Assert.AreEqual(0, vm.SpeedIndex);
        Events(events, "Speed", "SpeedIndex");
    }

    [TestMethod]
    public void Speed_FastNormalized()
    {
        var vm = new MainViewModel(new AppSettings { Speed = "balanced" });
        var events = Watch(vm);
        vm.Speed = " FAST ";
        // oracle: section 3 sanitises Speed; M05 either setter notifies Speed and SpeedIndex exactly once.
        Assert.AreEqual("fast", vm.Speed);
        Assert.AreEqual("fast", vm.Settings.Speed);
        Assert.AreEqual(2, vm.SpeedIndex);
        Events(events, "Speed", "SpeedIndex");
    }

    [TestMethod]
    public void Speed_NullDefaults()
    {
        var vm = new MainViewModel(new AppSettings { Speed = "eco" });
        var events = Watch(vm);
        vm.Speed = null!;
        // oracle: section 3 sanitises Speed; M05 either setter notifies Speed and SpeedIndex exactly once.
        Assert.AreEqual("balanced", vm.Speed);
        Assert.AreEqual("balanced", vm.Settings.Speed);
        Assert.AreEqual(1, vm.SpeedIndex);
        Events(events, "Speed", "SpeedIndex");
    }

    [TestMethod]
    public void Speed_UnknownDefaults()
    {
        var vm = new MainViewModel(new AppSettings { Speed = "eco" });
        var events = Watch(vm);
        vm.Speed = "unknown";
        // oracle: section 3 sanitises Speed; M05 either setter notifies Speed and SpeedIndex exactly once.
        Assert.AreEqual("balanced", vm.Speed);
        Assert.AreEqual("balanced", vm.Settings.Speed);
        Assert.AreEqual(1, vm.SpeedIndex);
        Events(events, "Speed", "SpeedIndex");
    }

    [TestMethod]
    public void Speed_Balanced()
    {
        var vm = new MainViewModel(new AppSettings { Speed = "eco" });
        var events = Watch(vm);
        vm.Speed = "balanced";
        // oracle: section 3 sanitises Speed; M05 either setter notifies Speed and SpeedIndex exactly once.
        Assert.AreEqual("balanced", vm.Speed);
        Assert.AreEqual("balanced", vm.Settings.Speed);
        Assert.AreEqual(1, vm.SpeedIndex);
        Events(events, "Speed", "SpeedIndex");
    }

    [TestMethod]
    public void Speed_NormalizedEqualNoEvents()
    {
        var vm = new MainViewModel(new AppSettings { Speed = "fast" });
        var events = Watch(vm);
        vm.Speed = " FAST ";
        // oracle: section 3 unchanged effective speed emits no events.
        Assert.AreEqual("fast", vm.Settings.Speed);
        Events(events);
    }

    [TestMethod]
    public void SpeedIndex_0()
    {
        var vm = new MainViewModel(new AppSettings { Speed = "fast" });
        var events = Watch(vm);
        vm.SpeedIndex = 0;
        // oracle: section 3 index 0/1/2 maps eco/balanced/fast; M05 both names fire once.
        Assert.AreEqual(0, vm.SpeedIndex);
        Assert.AreEqual("eco", vm.Speed);
        Assert.AreEqual("eco", vm.Settings.Speed);
        Events(events, "Speed", "SpeedIndex");
    }

    [TestMethod]
    public void SpeedIndex_1()
    {
        var vm = new MainViewModel(new AppSettings { Speed = "fast" });
        var events = Watch(vm);
        vm.SpeedIndex = 1;
        // oracle: section 3 index 0/1/2 maps eco/balanced/fast; M05 both names fire once.
        Assert.AreEqual(1, vm.SpeedIndex);
        Assert.AreEqual("balanced", vm.Speed);
        Assert.AreEqual("balanced", vm.Settings.Speed);
        Events(events, "Speed", "SpeedIndex");
    }

    [TestMethod]
    public void SpeedIndex_2()
    {
        var vm = new MainViewModel(new AppSettings { Speed = "eco" });
        var events = Watch(vm);
        vm.SpeedIndex = 2;
        // oracle: section 3 index 0/1/2 maps eco/balanced/fast; M05 both names fire once.
        Assert.AreEqual(2, vm.SpeedIndex);
        Assert.AreEqual("fast", vm.Speed);
        Assert.AreEqual("fast", vm.Settings.Speed);
        Events(events, "Speed", "SpeedIndex");
    }

    [TestMethod]
    public void SpeedIndex_IgnoresNegative()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SpeedIndex = -1;
        // oracle: section 3 out-of-range SpeedIndex is ignored.
        Assert.AreEqual(1, vm.SpeedIndex);
        Assert.AreEqual("balanced", vm.Settings.Speed);
        Events(events);
    }

    [TestMethod]
    public void SpeedIndex_IgnoresAboveMaximum()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SpeedIndex = 3;
        // oracle: section 3 out-of-range SpeedIndex is ignored.
        Assert.AreEqual(1, vm.SpeedIndex);
        Assert.AreEqual("balanced", vm.Settings.Speed);
        Events(events);
    }

    [TestMethod]
    public void SpeedIndex_IgnoresMinimumInteger()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SpeedIndex = int.MinValue;
        // oracle: section 3 out-of-range SpeedIndex is ignored.
        Assert.AreEqual(1, vm.SpeedIndex);
        Assert.AreEqual("balanced", vm.Settings.Speed);
        Events(events);
    }

    [TestMethod]
    public void SpeedIndex_IgnoresMaximumInteger()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SpeedIndex = int.MaxValue;
        // oracle: section 3 out-of-range SpeedIndex is ignored.
        Assert.AreEqual(1, vm.SpeedIndex);
        Assert.AreEqual("balanced", vm.Settings.Speed);
        Events(events);
    }

    [TestMethod]
    public void SpeedIndex_EqualNoEvents()
    {
        var vm = new MainViewModel(null);
        var events = Watch(vm);
        vm.SpeedIndex = 1;
        // oracle: section 3 unchanged speed index emits no events.
        Events(events);
    }

    [TestMethod]
    public void Apply_DuplicateMetricAndTabKeysPreservePositions()
    {
        var vm = new MainViewModel(null);
        Snapshot Make(int secondUsage) => new Snapshot
        {
            Cores = Array.Empty<Core>(), Ram = new Ram(), Adapters = Array.Empty<Adapter>(), Modules = Array.Empty<Module>(),
            Disks = new[]
            {
                new Disk { Letter = "/", Label = "First", TotalGb = 10, Usage = 10 },
                new Disk { Letter = "/", Label = "Second", TotalGb = 10, Usage = secondUsage }
            }
        };
        vm.Apply(Make(20), 0);
        var items = Changes(vm.Items);
        var tabs = Changes(vm.Tabs);
        vm.Apply(Make(21), 10);
        // oracle: section 3 positional comparison allows duplicate keys; second disk metric alone changes, and overview Lines change.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "disk:/", "disk:/" }, vm.Items.Select(i => i.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "cpu", "memory", "disk", "disk:/", "disk:/" }, vm.Tabs.Select(t => t.Key).ToArray());
        Actions(items, NotifyCollectionChangedAction.Replace);
        Assert.AreEqual(3, items[0].NewStartingIndex);
        Actions(tabs, NotifyCollectionChangedAction.Replace);
        Assert.AreEqual(2, tabs[0].NewStartingIndex);
    }

    [TestMethod]
    public void SelectedTab_DuplicateKeysReturnsFirst()
    {
        var vm = new MainViewModel(null);
        vm.Apply(new Snapshot
        {
            Cores = Array.Empty<Core>(), Ram = new Ram(), Adapters = Array.Empty<Adapter>(), Modules = Array.Empty<Module>(),
            Disks = new[]
            {
                new Disk { Letter = "/", Label = "First", TotalGb = 10 },
                new Disk { Letter = "/", Label = "Second", TotalGb = 10 }
            }
        }, 0);
        vm.SelectedTab = vm.Tabs[4];
        // oracle: H03 duplicate tab keys select only the first matching tab.
        Assert.AreEqual("disk:/", vm.SelectedTabKey);
        Assert.AreSame(vm.Tabs[3], vm.SelectedTab);
        Assert.AreEqual("First", vm.SelectedTab!.Title);
    }

    [TestMethod]
    public void Apply_ReorderedKeysResetItemsButLeaveTabsUnchanged()
    {
        var vm = new MainViewModel(null);
        var a = new Adapter { Id = "eth0", SpeedMbps = 100 };
        var b = new Adapter { Id = "eth1", SpeedMbps = 100 };
        Snapshot Make(Adapter first, Adapter second) => new Snapshot
        {
            Cores = Array.Empty<Core>(), Ram = new Ram(), Disks = Array.Empty<Disk>(),
            Adapters = new[] { first, second }, Modules = Array.Empty<Module>()
        };
        vm.Apply(Make(a, b), 0);
        var items = Changes(vm.Items);
        var tabs = Changes(vm.Tabs);
        vm.Apply(Make(b, a), 10);
        // oracle: section 3 same key set in a different order resets Items; network order does not affect Tabs.
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "net:eth1", "net:eth0" }, vm.Items.Select(i => i.Key).ToArray());
        Actions(items, NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add);
        Actions(tabs);
    }

    [TestMethod]
    public void Apply_CurrentKeySurvivesMovingIndexWithoutCurrentEvent()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        vm.Next(10);
        vm.Settings.ShowCpu = false;
        var events = Watch(vm);
        vm.Apply(Sample(), 20);
        // oracle: section 3 Current compares item, not index; RAM remains equal after moving from index 1 to 0.
        Assert.AreEqual("ram", vm.Current!.Key);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts");
    }

    [TestMethod]
    public void Apply_HistoryAlreadyUpdatedAtNotifications()
    {
        var vm = new MainViewModel(null);
        var seen = new List<double>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "HistoryVersion") seen.Add(vm.History.Get("cpu")!.Latest);
        };
        vm.Apply(Sample(cpu: 42), 0);
        // oracle: section 3 History.Record occurs before Apply property notifications.
        CollectionAssert.AreEqual(new[] { 42.0 }, seen);
    }

    [TestMethod]
    public void SetMode_FullSameEffectiveModeDoesNotReapply()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full" });
        vm.SetWindowSize(900, 700);
        var events = Watch(vm);
        vm.SetMode(ViewMode.Full);
        // oracle: section 3 equal requested Full is a no-op when Full is still effective, preserving user size.
        Assert.AreEqual(900.0, vm.WindowWidth);
        Assert.AreEqual(700.0, vm.WindowHeight);
        Events(events);
    }

    [TestMethod]
    public void SetMode_FullReentryOnlyWidthChanged()
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full", FullW = 640, FullH = 480 });
        vm.SetWindowSize(600, 480);
        var events = Watch(vm);
        vm.SetMode(ViewMode.Full);
        // oracle: ROUND-3 RULING re-entry always raises RequestedMode, plus changed width and effective mode; unchanged height stays silent.
        Assert.AreEqual(640.0, vm.WindowWidth);
        Assert.AreEqual(480.0, vm.WindowHeight);
        Events(events, "RequestedMode", "WindowWidth", "EffectiveMode");
    }

    [TestMethod]
    public void SetMode_OnlyWidthChanged()
    {
        var vm = new MainViewModel(new AppSettings { MiniH = 480 });
        var events = Watch(vm);
        vm.SetMode(ViewMode.Expanded);
        // oracle: section 3 equal dimensions are silent even when requested mode changes.
        Assert.AreEqual(630.0, vm.WindowWidth);
        Assert.AreEqual(480.0, vm.WindowHeight);
        Events(events, "RequestedMode", "EffectiveMode", "WindowWidth");
    }

    [TestMethod]
    public void SetMode_OnlyHeightChanged()
    {
        var vm = new MainViewModel(new AppSettings { MiniW = 630 });
        var events = Watch(vm);
        vm.SetMode(ViewMode.Expanded);
        // oracle: section 3 equal dimensions are silent even when requested mode changes.
        Assert.AreEqual(630.0, vm.WindowWidth);
        Assert.AreEqual(480.0, vm.WindowHeight);
        Events(events, "RequestedMode", "EffectiveMode", "WindowHeight");
    }

    [TestMethod]
    public void SelectedTabKey_DoesNotSanitizeUntilSave()
    {
        var vm = new MainViewModel(null);
        var key = new string('x', 201) + "\t";
        var events = Watch(vm);
        vm.SelectedTabKey = key;
        // oracle: section 3 key setter accepts verbatim strings; FullTab sanitisation happens on Sanitize/Save, not selection.
        Assert.AreEqual(key, vm.SelectedTabKey);
        Assert.AreEqual(key, vm.Settings.FullTab);
        Assert.IsNull(vm.SelectedTab);
        Events(events, "SelectedTabKey", "SelectedTab", "SelectedCharts");
    }

    [TestMethod]
    public void Apply_NestedNullMembersUseEmptyDefaults()
    {
        var vm = new MainViewModel(null);
        vm.Apply(new Snapshot { CpuTotal = 5, Ram = null!, Cores = null!, Disks = null!, Adapters = null!, Modules = null! }, 0);
        // oracle: 04 nested null snapshot members count as empty/zero/absent; CPU and RAM still exist.
        CollectionAssert.AreEqual(new[] { "cpu", "ram" }, vm.Items.Select(i => i.Key).ToArray());
        Assert.AreEqual(5, vm.Items[0].Usage);
        Assert.AreEqual(0, vm.Items[1].Usage);
        Assert.AreEqual(3, vm.Tabs.Count);
        CollectionAssert.AreEqual(new[] { "cpu", "ram" }, vm.History.Keys.ToArray());
    }

    [TestMethod]
    public void Next_DuplicateEqualItemsDoNotNotifyCurrent()
    {
        var vm = new MainViewModel(null);
        vm.Apply(new Snapshot
        {
            Cores = Array.Empty<Core>(), Ram = new Ram(), Adapters = Array.Empty<Adapter>(), Modules = Array.Empty<Module>(),
            Disks = new[]
            {
                new Disk { Letter = "/", Label = "Root", TotalGb = 10 },
                new Disk { Letter = "/", Label = "Root", TotalGb = 10 }
            }
        }, 0);
        vm.Next(1);
        vm.Next(2);
        var previous = vm.Current;
        var events = Watch(vm);
        vm.Next(3);
        // oracle: section 3 Current compares key and record value, so moving between equal duplicate records emits nothing.
        Assert.AreEqual("disk:/", vm.Current!.Key);
        Assert.AreEqual(previous, vm.Current);
        Events(events);
    }

    [TestMethod]
    public void SelectedCharts_ReadReflectsNewHistoryWithoutChangingKey()
    {
        var vm = new MainViewModel(null);
        var before = vm.SelectedCharts.ToArray();
        vm.Apply(Sample(), 0);
        // oracle: section 3 SelectedCharts is computed on read; adding core history changes chart list while cpu remains selected.
        CollectionAssert.AreEqual(new[] { new ChartSpec("CPU", "cpu", "#3b82f6", 100) }, before);
        CollectionAssert.AreEqual(new[] { new ChartSpec("CPU", "cpu", "#3b82f6", 100), new ChartSpec("Core 0", "core:0", "#3b82f6", 100) }, vm.SelectedCharts.ToArray());
        Assert.AreEqual("cpu", vm.SelectedTabKey);
    }

    [TestMethod]
    public void Apply_CaseOnlyKeyChangeResetsThenAdds()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(letter: "/data"), 0);
        var items = Changes(vm.Items);
        vm.Apply(Sample(letter: "/DATA"), 10);
        // oracle: Inspector-B (3) keys compare ordinally; disk:/data -> disk:/DATA requires Reset then four Adds, with no Replace.
        Actions(items, NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Add,
            NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, items.Skip(1).Select(e => e.NewStartingIndex).ToArray());
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "disk:/DATA", "net:eth0" }, vm.Items.Select(i => i.Key).ToArray());
    }

    [TestMethod]
    public void Apply_OnlyCpuValueTextChangedReplacesExactlyOneItem()
    {
        Snapshot WithTemperature(int temperature) => new Snapshot
        {
            CpuTotal = 10, CpuTemp = temperature, Cores = Array.Empty<Core>(),
            Ram = new Ram(), Disks = Array.Empty<Disk>(), Adapters = Array.Empty<Adapter>(),
            Modules = Array.Empty<Module>()
        };
        var vm = new MainViewModel(null);
        vm.Apply(WithTemperature(60), 0);
        var items = Changes(vm.Items);
        var events = Watch(vm);
        vm.Apply(WithTemperature(61), 10);
        // oracle: Inspector-B (3) full record equality includes ValueText; only CPU temperature changes, so only index 0 is replaced.
        Actions(items, NotifyCollectionChangedAction.Replace);
        Assert.AreEqual(0, items[0].OldStartingIndex);
        Assert.AreEqual(0, items[0].NewStartingIndex);
        Assert.AreEqual(new MetricItem(MetricKind.Cpu, "cpu", "CPU", "0 cores", 10,
            "10% 60 C", "#3b82f6", "#3b82f6"), items[0].OldItems![0]);
        Assert.AreEqual(new MetricItem(MetricKind.Cpu, "cpu", "CPU", "0 cores", 10,
            "10% 61 C", "#3b82f6", "#3b82f6"), items[0].NewItems![0]);
        Assert.AreEqual("10% 61 C", vm.Current!.ValueText);
        Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts", "Current");
    }

    [TestMethod]
    public void Apply_OnlyColorHexDiffersReplacesExactlyOneItem()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var expected = new MetricItem(MetricKind.Cpu, "cpu", "CPU", "1 core", 10,
            "10% 60 C", "#3b82f6", "#3b82f6");
        vm.Items[0] = expected with { ColorHex = "#ef4444" };
        var items = Changes(vm.Items);
        vm.Apply(Sample(), 10);
        // oracle: Inspector-B (3) full record equality includes ColorHex; Apply corrects this sole differing field through one Replace.
        Actions(items, NotifyCollectionChangedAction.Replace);
        Assert.AreEqual(0, items[0].NewStartingIndex);
        Assert.AreEqual(expected with { ColorHex = "#ef4444" }, items[0].OldItems![0]);
        Assert.AreEqual(expected, items[0].NewItems![0]);
        Assert.AreEqual(expected, vm.Items[0]);
    }

    [TestMethod]
    public void SpeedIndex_OutOfRangePreservesEcoAndFast()
    {
        foreach (var speed in new[] { "eco", "fast" })
        foreach (var invalidIndex in new[] { -1, 3 })
        {
            var vm = new MainViewModel(new AppSettings { Speed = speed });
            var events = Watch(vm);
            vm.SpeedIndex = invalidIndex;
            // oracle: section 3 ignores -1 and 3 completely for every speed, including eco (0) and fast (2).
            Assert.AreEqual(speed, vm.Speed);
            Assert.AreEqual(speed, vm.Settings.Speed);
            Assert.AreEqual(speed == "eco" ? 0 : 2, vm.SpeedIndex);
            Events(events);
        }
    }

    [TestMethod]
    public void Apply_HistoryVersionSaturatesAndStillNotifies()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        typeof(MainViewModel).GetProperty("HistoryVersion")!.GetSetMethod(true)!
            .Invoke(vm, new object[] { int.MaxValue });
        var events = Watch(vm);
        foreach (var now in new long[] { 10, 20 })
        {
            vm.Apply(Sample(), now);
            // oracle: Inspector-B (7) saturates HistoryVersion at int.MaxValue; section 3 ALWAYS notifies it once per non-null Apply.
            Assert.AreEqual(int.MaxValue, vm.HistoryVersion);
            Events(events, "Snapshot", "HistoryVersion", "SelectedTab", "SelectedCharts");
            events.Clear();
        }
        vm.Apply(null, 30);
        // oracle: section 3 null Apply is still a complete no-op at saturation.
        Assert.AreEqual(int.MaxValue, vm.HistoryVersion);
        Events(events);
    }

    [TestMethod]
    public void SelectedTabKey_ProgrammaticWriteDuringReplaceTakesEffect()
    {
        var vm = new MainViewModel(null);
        vm.Apply(Sample(), 0);
        var callbacks = 0;
        vm.Tabs.CollectionChanged += (_, e) =>
        {
            if (e.Action != NotifyCollectionChangedAction.Replace) return;
            callbacks++;
            vm.SelectedTabKey = "memory";
            // oracle: Inspector-B (8) the programmatic key setter takes effect immediately even while Apply updates Tabs.
            Assert.AreEqual("memory", vm.SelectedTabKey);
            Assert.AreEqual("memory", vm.Settings.FullTab);
        };
        vm.Apply(Sample(cpu: 11), 10);
        // oracle: the single CPU tab replacement invokes the callback once and fix-up retains its existing memory key.
        Assert.AreEqual(1, callbacks);
        Assert.AreEqual("memory", vm.SelectedTabKey);
        Assert.AreEqual("memory", vm.Settings.FullTab);
        Assert.AreSame(vm.Tabs[1], vm.SelectedTab);
    }

    [TestMethod]
    public void SelectedTabKey_ProgrammaticWriteDuringResetAndAddsTakesEffect()
    {
        var vm = new MainViewModel(null);
        var callbacks = 0;
        vm.Tabs.CollectionChanged += (_, _) =>
        {
            callbacks++;
            vm.SelectedTabKey = "disk:/";
            // oracle: Inspector-B (8) SelectedTabKey is ungated during Reset and every Add, including the first Apply.
            Assert.AreEqual("disk:/", vm.SelectedTabKey);
            Assert.AreEqual("disk:/", vm.Settings.FullTab);
        };
        vm.Apply(Sample(), 0);
        // oracle: first Apply resets Tabs and adds four tabs; the requested disk key exists at final fix-up and must survive.
        Assert.AreEqual(5, callbacks);
        Assert.AreEqual("disk:/", vm.SelectedTabKey);
        Assert.AreEqual("disk:/", vm.Settings.FullTab);
        Assert.AreSame(vm.Tabs[3], vm.SelectedTab);
    }

    [TestMethod]
    public void SetMode_NoOpDoesNotRewriteDirectlyEditedSettingsMode()
    {
        var settings = new AppSettings();
        var vm = new MainViewModel(settings);
        settings.Mode = "full";
        var events = Watch(vm);
        vm.SetMode(ViewMode.Mini);
        // oracle: Inspector-B (2) equal requested/effective Mini does NOTHING, including leaving directly edited settings.Mode untouched.
        Assert.AreEqual("full", settings.Mode);
        Assert.AreEqual(ViewMode.Mini, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Mini, vm.EffectiveMode);
        Assert.AreEqual(270.0, vm.WindowWidth);
        Assert.AreEqual(104.0, vm.WindowHeight);
        Events(events);

        vm.SetMode(ViewMode.Expanded);
        // oracle: section 3 a real mode change still persists its mode string and raises the four changed properties.
        Assert.AreEqual("expanded", settings.Mode);
        Assert.AreEqual(ViewMode.Expanded, vm.RequestedMode);
        Assert.AreEqual(ViewMode.Expanded, vm.EffectiveMode);
        Assert.AreEqual(630.0, vm.WindowWidth);
        Assert.AreEqual(480.0, vm.WindowHeight);
        Events(events, "RequestedMode", "EffectiveMode", "WindowWidth", "WindowHeight");
    }

}
