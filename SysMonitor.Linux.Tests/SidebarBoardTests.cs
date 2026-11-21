using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class SidebarBoardTests
{
    private static Snapshot CreateSnapshot(
        bool showGpu = true,
        bool showBattery = true)
    {
        var disks = new List<Disk>
        {
            new()
            {
                Letter = "/",
                Usage = 50,
                Detail = new DriveDetail
                {
                    Letter = "/",
                    Disk = new PhysicalDisk { Name = "nvme0n1", Model = "Samsung 970 EVO", Online = true },
                },
            },
            new()
            {
                Letter = "/data",
                Usage = 40,
                Detail = new DriveDetail
                {
                    Letter = "/data",
                    Disk = new PhysicalDisk { Name = "sda", Model = "WDC WD10EZEX", Online = true },
                },
            },
        };

        var gpus = new List<Gpu>();
        if (showGpu)
        {
            gpus.Add(new Gpu
            {
                Present = true,
                Name = "Intel Iris Plus Graphics G1 (Ice Lake)",
                PciSlot = "0000:00:02.0",
                Usage = 15,
            });
            gpus.Add(new Gpu
            {
                Present = true,
                Name = "NVIDIA GeForce MX250",
                PciSlot = "0000:01:00.0",
                Usage = 30,
            });
        }

        var adapters = new List<Adapter>
        {
            new()
            {
                Id = "wlp0s20f3",
                Name = "Wi-Fi",
                Wireless = true,
                DownMb = 2.5,
                UpMb = 0.5,
            },
            new()
            {
                Id = "eth0",
                Name = "Ethernet",
                Wireless = false,
                DownMb = 10.0,
                UpMb = 5.0,
            },
        };

        var batteries = new List<Battery>();
        if (showBattery)
        {
            batteries.Add(new Battery
            {
                Id = "BAT0",
                Model = "Standard Battery",
                Percent = 95,
            });
        }

        return new Snapshot
        {
            Ready = true,
            CpuTotal = 25,
            Ram = new Ram
            {
                Usage = 50,
                UsedGb = 4.0,
                TotalGb = 8.0,
                SwapUsedBytes = 1024L * 1024 * 1024,
                SwapTotalBytes = 4096L * 1024 * 1024,
                SwapUsage = 25,
            },
            Disks = disks,
            Gpus = gpus,
            Gpu = gpus.Count > 0 ? gpus[0] : new Gpu(),
            Adapters = adapters,
            Batteries = batteries,
            PowerSources = new[]
            {
                new PowerSource("ACAD", PowerSourceKind.Mains, true),
            },
        };
    }

    [TestMethod]
    public void Build_SidebarItemsOrder_CpuMemoryGpusDisksNetworksBatteries()
    {
        Snapshot snapshot = CreateSnapshot();
        var config = new AppConfig();
        var lang = new Lang("en");
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel modules = FullBoard.Build(
            snapshot, config, lang, Palette.Dark, history, selection, rememberedTab: "");

        IReadOnlyList<SidebarItem> items = SidebarBoard.Build(modules, snapshot, history, lang);

        // Order: CPU, Memory, 2 GPUs, 2 Disks, 2 Adapters, 1 Battery -> 9 items
        Assert.AreEqual(9, items.Count);

        // 0: CPU
        Assert.AreEqual(PageKeys.Cpu, items[0].Key);
        Assert.AreEqual("CPU", items[0].Title);

        // 1: Memory
        Assert.AreEqual(PageKeys.Ram, items[1].Key);
        Assert.AreEqual("Memory", items[1].Title);

        // 2: GPU 0
        Assert.AreEqual("gpu:0000:00:02.0", items[2].Key);
        Assert.AreEqual("GPU · Iris Plus G1", items[2].Title);

        // 3: GPU 1
        Assert.AreEqual("gpu:0000:01:00.0", items[3].Key);
        Assert.AreEqual("GPU · MX250", items[3].Title);

        // 4: Disk 0
        Assert.AreEqual("disk:nvme0n1", items[4].Key);
        Assert.AreEqual("Disk · nvme0n1", items[4].Title);

        // 5: Disk 1
        Assert.AreEqual("disk:sda", items[5].Key);
        Assert.AreEqual("Disk · sda", items[5].Title);

        // 6: Adapter 0
        Assert.AreEqual("netwlp0s20f3", items[6].Key);
        Assert.AreEqual("Wi-Fi · wlp0s20f3", items[6].Title);

        // 7: Adapter 1
        Assert.AreEqual("neteth0", items[7].Key);
        Assert.AreEqual("Ethernet · eth0", items[7].Title);

        // 8: Battery
        Assert.AreEqual("battery:BAT0", items[8].Key);
        Assert.AreEqual("Battery · BAT0", items[8].Title);
    }

    [TestMethod]
    public void Build_MemoryItem_PrimaryIsRam_SecondaryIsSwapHistory()
    {
        Snapshot snapshot = CreateSnapshot();
        var config = new AppConfig();
        var lang = new Lang("en");
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel modules = FullBoard.Build(snapshot, config, lang, Palette.Dark, history, selection, "");
        IReadOnlyList<SidebarItem> items = SidebarBoard.Build(modules, snapshot, history, lang);

        SidebarItem memItem = items.First(i => i.Key == PageKeys.Ram);
        Assert.AreSame(history.Ram, memItem.Primary);
        Assert.AreSame(history.Swap, memItem.Secondary);
    }

    [TestMethod]
    public void Build_NetworkItem_PrimaryIsDown_SecondaryIsUp()
    {
        Snapshot snapshot = CreateSnapshot();
        var config = new AppConfig();
        var lang = new Lang("en");
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel modules = FullBoard.Build(snapshot, config, lang, Palette.Dark, history, selection, "");
        IReadOnlyList<SidebarItem> items = SidebarBoard.Build(modules, snapshot, history, lang);

        SidebarItem netItem = items.First(i => i.Key == "netwlp0s20f3");
        Assert.AreSame(history.AdapterDown("wlp0s20f3"), netItem.Primary);
        Assert.AreSame(history.AdapterUp("wlp0s20f3"), netItem.Secondary);
    }

    [TestMethod]
    public void Build_DiskItem_PrimaryIsActivity_SecondaryIsNull()
    {
        Snapshot snapshot = CreateSnapshot();
        var config = new AppConfig();
        var lang = new Lang("en");
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel modules = FullBoard.Build(snapshot, config, lang, Palette.Dark, history, selection, "");
        IReadOnlyList<SidebarItem> items = SidebarBoard.Build(modules, snapshot, history, lang);

        SidebarItem diskItem = items.First(i => i.Key == "disk:nvme0n1");
        Assert.AreSame(history.DiskActivity("disk:nvme0n1"), diskItem.Primary);
        Assert.IsNull(diskItem.Secondary);
    }

    [TestMethod]
    public void Build_NullSnapshot_ReturnsEmptyList()
    {
        var modules = new FullModel();
        var history = new HistoryBoard();
        var lang = new Lang("en");

        IReadOnlyList<SidebarItem> items = SidebarBoard.Build(modules, null, history, lang);
        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public void Build_HiddenModules_AreNotPresent()
    {
        Snapshot snapshot = CreateSnapshot(showGpu: true, showBattery: true);
        var config = new AppConfig
        {
            ShowGpu = false,
            ShowBattery = false,
        };
        var lang = new Lang("en");
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel modules = FullBoard.Build(snapshot, config, lang, Palette.Dark, history, selection, "");
        IReadOnlyList<SidebarItem> items = SidebarBoard.Build(modules, snapshot, history, lang);

        Assert.IsFalse(items.Any(i => i.Key.StartsWith("gpu")));
        Assert.IsFalse(items.Any(i => i.Key.StartsWith("battery:")));
    }

    [TestMethod]
    public void ShortName_StripsVendorNoiseAndTruncates()
    {
        Assert.AreEqual("Iris Plus G1", SidebarBoard.ShortName("Intel Iris Plus Graphics G1 (Ice Lake)", "GPU 1"));
        Assert.AreEqual("MX250", SidebarBoard.ShortName("NVIDIA GeForce MX250", "GPU 2"));
        Assert.AreEqual("Custom Device", SidebarBoard.ShortName("", "Custom Device"));
    }
}
