using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class ModulePageTests
{
    private static Snapshot CreateSnapshot(
        bool hasSwap = true,
        bool hasMemoryLayout = true,
        int batteryCount = 1)
    {
        var batteries = new List<Battery>();
        for (int i = 0; i < batteryCount; i++)
        {
            batteries.Add(new Battery
            {
                Id = "BAT" + i,
                Manufacturer = "SMP",
                Model = "SimulatedBatt",
                Technology = "Li-ion",
                Percent = 88,
                PowerW = 12.5,
                State = BatteryState.Discharging,
                EnergyNowWh = 45.0,
                EnergyFullWh = 51.0,
                EnergyDesignWh = 54.0,
                CycleCount = 120,
                VoltageV = 11.4,
            });
        }

        MemoryLayout layout = hasMemoryLayout
            ? new MemoryLayout
            {
                SlotsTotal = 2,
                SlotsUsed = 2,
                MaxCapacityBytes = 32L * 1024 * 1024 * 1024,
                Modules = new[]
                {
                    new MemoryModule
                    {
                        Slot = "ChannelA-DIMM0",
                        SizeBytes = 8L * 1024 * 1024 * 1024,
                        Type = "DDR4",
                        TypeDetail = "Synchronous",
                        FormFactor = "SODIMM",
                        ConfiguredSpeedMts = 2667,
                        SpeedMts = 4267,
                        Manufacturer = "Samsung",
                        Rank = 1,
                        ConfiguredVoltage = 1.2,
                    },
                },
            }
            : new MemoryLayout();

        return new Snapshot
        {
            Ready = true,
            CpuTotal = 15,
            CpuInfo = new CpuInfo
            {
                Name = "Core i7",
                Cores = 4,
                Logical = 8,
            },
            Ram = new Ram
            {
                Usage = 40,
                UsedGb = 3.2,
                TotalGb = 8.0,
                SwapUsedBytes = hasSwap ? 512L * 1024 * 1024 : 0,
                SwapTotalBytes = hasSwap ? 2048L * 1024 * 1024 : 0,
                SwapUsage = hasSwap ? 25 : 0,
            },
            MemoryLayout = layout,
            Disks = new[]
            {
                new Disk
                {
                    Letter = "/",
                    Usage = 60,
                    Detail = new DriveDetail
                    {
                        Letter = "/",
                        Disk = new PhysicalDisk { Name = "nvme0n1", Model = "DiskModel", Online = true },
                    },
                },
            },
            Gpus = new[]
            {
                new Gpu { Present = true, Name = "Simulated GPU", PciSlot = "0000:00:02.0", Usage = 10 },
            },
            Gpu = new Gpu { Present = true, Name = "Simulated GPU", PciSlot = "0000:00:02.0", Usage = 10 },
            Adapters = new[]
            {
                new Adapter
                {
                    Id = "eth0",
                    Name = "Ethernet",
                    Description = "Intel NIC",
                    SpeedMbps = 1000,
                    DownMb = 1.0,
                    UpMb = 0.5,
                },
            },
            Batteries = batteries,
            PowerSources = new[]
            {
                new PowerSource("ACAD", PowerSourceKind.Mains, true),
                new PowerSource("USB-PD", PowerSourceKind.Usb, false),
                new PowerSource("AUX", PowerSourceKind.Other, false),
            },
        };
    }

    [TestMethod]
    public void ModuleOptions_CpuPage_HasOnlyCpuModeAndCpuTemperature()
    {
        Snapshot snapshot = CreateSnapshot();
        var config = new AppConfig();
        var lang = new Lang("en");
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel modules = FullBoard.Build(snapshot, config, lang, Palette.Dark, history, selection, "");
        FullTab cpuTab = modules.Tabs.First(t => t.Key == PageKeys.Cpu);

        Assert.AreEqual(2, cpuTab.Options.Count);
        CollectionAssert.AreEqual(
            new[] { "cpu_mode", "cpu_temperature" },
            cpuTab.Options.Select(o => o.ConfigKey).ToArray());
    }

    [TestMethod]
    public void ModuleOptions_DiskPage_HasOnlyDiskModeAndIncludeNetwork()
    {
        Snapshot snapshot = CreateSnapshot();
        var config = new AppConfig();
        var lang = new Lang("en");
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel modules = FullBoard.Build(snapshot, config, lang, Palette.Dark, history, selection, "");
        FullTab diskTab = modules.Tabs.First(t => t.Key == "disk:nvme0n1");

        Assert.AreEqual(2, diskTab.Options.Count);
        CollectionAssert.AreEqual(
            new[] { "disk_mode", "include_network" },
            diskTab.Options.Select(o => o.ConfigKey).ToArray());
    }

    [TestMethod]
    public void ModuleOptions_NetworkPage_HasOnlyNetworkMode()
    {
        Snapshot snapshot = CreateSnapshot();
        var config = new AppConfig();
        var lang = new Lang("en");
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel modules = FullBoard.Build(snapshot, config, lang, Palette.Dark, history, selection, "");
        FullTab netTab = modules.Tabs.First(t => t.Key == "neteth0");

        Assert.AreEqual(1, netTab.Options.Count);
        Assert.AreEqual("network_mode", netTab.Options[0].ConfigKey);
    }

    [TestMethod]
    public void ModuleOptions_MemoryGpuBatteryPages_HaveNoOptions()
    {
        Snapshot snapshot = CreateSnapshot();
        var config = new AppConfig();
        var lang = new Lang("en");
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel modules = FullBoard.Build(snapshot, config, lang, Palette.Dark, history, selection, "");

        FullTab ramTab = modules.Tabs.First(t => t.Key == PageKeys.Ram);
        Assert.AreEqual(0, ramTab.Options.Count);

        FullTab gpuTab = modules.Tabs.First(t => t.Key == "gpu:0000:00:02.0");
        Assert.AreEqual(0, gpuTab.Options.Count);

        FullTab batTab = modules.Tabs.First(t => t.Key == "battery:BAT0");
        Assert.AreEqual(0, batTab.Options.Count);
    }

    [TestMethod]
    public void MemoryPage_SwapChart_PresentWithSwap_AbsentWithout()
    {
        var config = new AppConfig();
        var lang = new Lang("en");
        var history = new HistoryBoard();
        var selection = new FullSelection();

        // With swap
        Snapshot withSwap = CreateSnapshot(hasSwap: true);
        FullModel modulesWithSwap = FullBoard.Build(withSwap, config, lang, Palette.Dark, history, selection, "");
        FullTab ramWithSwap = modulesWithSwap.Tabs.First(t => t.Key == PageKeys.Ram);

        Assert.AreEqual(2, ramWithSwap.BigCards.Count);
        Assert.AreEqual("memory", ramWithSwap.BigCards[0].Key);
        Assert.AreEqual("swap", ramWithSwap.BigCards[1].Key);

        // Without swap
        Snapshot withoutSwap = CreateSnapshot(hasSwap: false);
        FullModel modulesWithoutSwap = FullBoard.Build(withoutSwap, config, lang, Palette.Dark, history, selection, "");
        FullTab ramWithoutSwap = modulesWithoutSwap.Tabs.First(t => t.Key == PageKeys.Ram);

        Assert.AreEqual(1, ramWithoutSwap.BigCards.Count);
        Assert.AreEqual("memory", ramWithoutSwap.BigCards[0].Key);
        Assert.IsTrue(ramWithoutSwap.Properties.Any(p => p.Title == "Swap" && p.Value == "none"));
    }

    [TestMethod]
    public void MemoryPage_RamProperties_FromMemoryLayout()
    {
        var config = new AppConfig();
        var lang = new Lang("en");
        var history = new HistoryBoard();
        var selection = new FullSelection();

        // Valid MemoryLayout
        Snapshot snapshot = CreateSnapshot(hasMemoryLayout: true);
        FullModel modules = FullBoard.Build(snapshot, config, lang, Palette.Dark, history, selection, "");
        FullTab ramTab = modules.Tabs.First(t => t.Key == PageKeys.Ram);

        FullFact? slotsFact = ramTab.Properties.FirstOrDefault(p => p.Title == "Slots used");
        Assert.IsNotNull(slotsFact);
        StringAssert.Contains(slotsFact.Value, "2 of 2");

        FullFact? dimmFact = ramTab.Properties.FirstOrDefault(p => p.Title == "ChannelA-DIMM0");
        Assert.IsNotNull(dimmFact);
        StringAssert.Contains(dimmFact.Value, "2667 MT/s (rated 4267)");
        Assert.IsFalse(ramTab.Properties.Any(p => p.Title.Contains("Serial", StringComparison.OrdinalIgnoreCase)), "Serial number must not be exposed");

        // Empty layout -> "Hardware details not available"
        Snapshot emptySnapshot = CreateSnapshot(hasMemoryLayout: false);
        FullModel modulesEmpty = FullBoard.Build(emptySnapshot, config, lang, Palette.Dark, history, selection, "");
        FullTab ramEmptyTab = modulesEmpty.Tabs.First(t => t.Key == PageKeys.Ram);

        FullFact? hwFact = ramEmptyTab.Properties.FirstOrDefault(p => p.Title == "RAM properties");
        Assert.IsNotNull(hwFact);
        Assert.AreEqual("Hardware details not available", hwFact.Value);
    }

    [TestMethod]
    public void Properties_NullOrEmptyValue_FormatsAsNA()
    {
        var snapshot = new Snapshot
        {
            Ready = true,
            CpuTotal = 10,
            CpuInfo = new CpuInfo
            {
                Name = "Test CPU",
                MaxMhz = 0, // unreadable max frequency -> N/A
            },
            Ram = new Ram { Usage = 20, TotalGb = 8 },
        };
        var config = new AppConfig();
        var lang = new Lang("en");
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel modules = FullBoard.Build(snapshot, config, lang, Palette.Dark, history, selection, "");
        FullTab cpuTab = modules.Tabs.First(t => t.Key == PageKeys.Cpu);

        FullFact? maxFreqFact = cpuTab.Properties.FirstOrDefault(p => p.Title == "Max frequency");
        Assert.IsNotNull(maxFreqFact);
        Assert.AreEqual("N/A", maxFreqFact.Value);
    }

    [TestMethod]
    public void NetworkPage_HasNoMacRow()
    {
        Snapshot snapshot = CreateSnapshot();
        var config = new AppConfig();
        var lang = new Lang("en");
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel modules = FullBoard.Build(snapshot, config, lang, Palette.Dark, history, selection, "");
        FullTab netTab = modules.Tabs.First(t => t.Key == "neteth0");

        Assert.IsFalse(netTab.Properties.Any(p => p.Title.Contains("MAC", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BatteryPage_AppearsPerBattery_AndNotWhenNoneOrShowBatteryFalse()
    {
        var config = new AppConfig();
        var lang = new Lang("en");
        var history = new HistoryBoard();
        var selection = new FullSelection();

        // 2 batteries -> 2 tabs
        Snapshot snapshotTwo = CreateSnapshot(batteryCount: 2);
        FullModel modulesTwo = FullBoard.Build(snapshotTwo, config, lang, Palette.Dark, history, selection, "");
        Assert.AreEqual(2, modulesTwo.Tabs.Count(t => t.Key.StartsWith("battery:")));

        // 0 batteries -> 0 tabs
        Snapshot snapshotZero = CreateSnapshot(batteryCount: 0);
        FullModel modulesZero = FullBoard.Build(snapshotZero, config, lang, Palette.Dark, history, selection, "");
        Assert.AreEqual(0, modulesZero.Tabs.Count(t => t.Key.StartsWith("battery:")));

        // ShowBattery = false -> 0 tabs
        var configNoBattery = new AppConfig { ShowBattery = false };
        FullModel modulesDisabled = FullBoard.Build(snapshotTwo, configNoBattery, lang, Palette.Dark, history, selection, "");
        Assert.AreEqual(0, modulesDisabled.Tabs.Count(t => t.Key.StartsWith("battery:")));
    }

    [TestMethod]
    public void PowerSourceLabels_Localised_ThaiAndEnglish()
    {
        var en = new Lang("en");
        var th = new Lang("th");

        Assert.AreEqual("AC adapter", FullBoard.PowerSourceText(PowerSourceKind.Mains, en));
        Assert.AreEqual("อะแดปเตอร์", FullBoard.PowerSourceText(PowerSourceKind.Mains, th));

        Assert.AreEqual("USB", FullBoard.PowerSourceText(PowerSourceKind.Usb, en));
        Assert.AreEqual("USB", FullBoard.PowerSourceText(PowerSourceKind.Usb, th));

        Assert.AreEqual("Other", FullBoard.PowerSourceText(PowerSourceKind.Other, en));
        Assert.AreEqual("อื่น ๆ", FullBoard.PowerSourceText(PowerSourceKind.Other, th));
    }

    [TestMethod]
    public void ShellModel_SetOption_ChangesConfig_AndRebuilds()
    {
        var shell = new ShellModel();

        // cpu_mode
        Assert.IsTrue(shell.SetOption("cpu_mode", false));
        Assert.AreEqual("total", shell.Config.CpuMode);
        Assert.IsTrue(shell.SetOption("cpu_mode", true));
        Assert.AreEqual("separated", shell.Config.CpuMode);

        // cpu_temperature
        Assert.IsTrue(shell.SetOption("cpu_temperature", false));
        Assert.IsFalse(shell.Config.CpuTemperature);

        // disk_mode
        Assert.IsTrue(shell.SetOption("disk_mode", false));
        Assert.AreEqual("total", shell.Config.DiskMode);

        // include_network
        Assert.IsTrue(shell.SetOption("include_network", false));
        Assert.IsFalse(shell.Config.IncludeNetwork);

        // network_mode
        Assert.IsTrue(shell.SetOption("network_mode", false));
        Assert.AreEqual("total", shell.Config.NetworkMode);

        // Invalid option returns false
        Assert.IsFalse(shell.SetOption("non_existent_key", true));
    }
}
