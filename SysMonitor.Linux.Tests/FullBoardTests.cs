using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class FullBoardTests
{
    private static Snapshot CreateSnapshot(
        bool ready = true,
        int cpuTotal = 30,
        int? cpuTemp = 55,
        int coreCount = 4,
        bool includeUsb = true,
        bool includeVirtual = false,
        bool includeNetworkDrive = false,
        int gpuCount = 2,
        int adapterCount = 2)
    {
        var cores = new List<Core>(coreCount);
        for (int i = 0; i < coreCount; i++)
        {
            cores.Add(new Core { Usage = 20 + i * 5, Temp = 48 + i });
        }

        var disks = new List<Disk>();

        // NVMe disk (internal): root disk holds "/" and "/boot/efi"
        var nvmePhysical = new PhysicalDisk
        {
            Number = 0,
            Name = "nvme0n1",
            Model = "Samsung 970 EVO 500GB",
            Serial = "S4EVNF0M123456",
            Bus = "NVMe",
            Media = "SSD",
            Bytes = 500L * 1024 * 1024 * 1024,
            PartitionCount = 2,
            AllocatedBytes = 500L * 1024 * 1024 * 1024,
            Online = true,
        };

        disks.Add(new Disk
        {
            Letter = "/",
            Usage = 60,
            UsedGb = 120.0,
            TotalGb = 200.0,
            ReadMb = 15.0,
            WriteMb = 5.0,
            Temp = 42,
            Detail = new DriveDetail
            {
                Letter = "/",
                FileSystem = "ext4",
                Disk = nvmePhysical,
                PartitionNumber = 2,
            },
        });

        disks.Add(new Disk
        {
            Letter = "/boot/efi",
            Usage = 25,
            UsedGb = 0.25,
            TotalGb = 1.0,
            ReadMb = 0.5,
            WriteMb = 0.1,
            Temp = 42,
            Detail = new DriveDetail
            {
                Letter = "/boot/efi",
                FileSystem = "vfat",
                Disk = nvmePhysical,
                PartitionNumber = 1,
            },
        });

        // Second internal disk: sda
        var sdaPhysical = new PhysicalDisk
        {
            Number = 1,
            Name = "sda",
            Model = "WDC WD10EZEX-00WN4A0",
            Bus = "SATA",
            Media = "HDD",
            Bytes = 1000L * 1024 * 1024 * 1024,
            Online = true,
        };

        disks.Add(new Disk
        {
            Letter = "/data",
            Usage = 50,
            UsedGb = 500.0,
            TotalGb = 1000.0,
            ReadMb = 2.0,
            WriteMb = 1.0,
            Temp = 38,
            Detail = new DriveDetail
            {
                Letter = "/data",
                FileSystem = "ext4",
                Disk = sdaPhysical,
                PartitionNumber = 1,
            },
        });

        // Removable USB disk: sdb
        if (includeUsb)
        {
            var usbPhysical = new PhysicalDisk
            {
                Number = 2,
                Name = "sdb",
                Model = "SanDisk Ultra USB 3.0",
                Bus = "USB",
                Media = "Removable",
                Bytes = 64L * 1024 * 1024 * 1024,
                Online = true,
            };

            disks.Add(new Disk
            {
                Letter = "/media/alice/USB",
                Label = "USB",
                Media = "Removable",
                Bus = "USB",
                Usage = 40,
                UsedGb = 25.6,
                TotalGb = 64.0,
                ReadMb = 8.0,
                WriteMb = 4.0,
                Temp = 35,
                Detail = new DriveDetail
                {
                    Letter = "/media/alice/USB",
                    FileSystem = "vfat",
                    Disk = usbPhysical,
                    PartitionNumber = 1,
                },
            });
        }

        if (includeVirtual)
        {
            disks.Add(new Disk
            {
                Letter = "/mnt/virtual",
                Media = "Virtual",
                Usage = 10,
                UsedGb = 1.0,
                TotalGb = 10.0,
                Detail = null, // No physical disk behind virtual drive
            });
        }

        if (includeNetworkDrive)
        {
            disks.Add(new Disk
            {
                Letter = "/mnt/nfs",
                Media = "Network",
                IsNetwork = true,
                Usage = 70,
                UsedGb = 700.0,
                TotalGb = 1000.0,
                Detail = null,
            });
        }

        var gpus = new List<Gpu>();
        if (gpuCount >= 1)
        {
            gpus.Add(new Gpu
            {
                Present = true,
                Name = "Intel Iris Plus Graphics G1 (Ice Lake)",
                PciSlot = "0000:00:02.0",
                Driver = "i915",
                Usage = 25,
                DedicatedGb = 0,
                DedicatedTotalGb = 0,
                SharedGb = 1.5,
                Temp = null,
                Engines = new[]
                {
                    new GpuEngine { Name = "3D", Usage = 25 },
                    new GpuEngine { Name = "Copy", Usage = 5 },
                },
            });
        }
        if (gpuCount >= 2)
        {
            gpus.Add(new Gpu
            {
                Present = true,
                Name = "NVIDIA GeForce MX250",
                PciSlot = "0000:01:00.0",
                Driver = "nvidia",
                Usage = 65,
                DedicatedGb = 1.2,
                DedicatedTotalGb = 2.0,
                SharedGb = 0,
                Temp = 60,
                Engines = Array.Empty<GpuEngine>(), // NVIDIA has no engines
            });
        }

        var adapters = new List<Adapter>();
        if (adapterCount >= 1)
        {
            adapters.Add(new Adapter
            {
                Id = "wlp0s20f3",
                Name = "Wi-Fi",
                Description = "Intel Wi-Fi 6 AX201",
                Wireless = true,
                SpeedMbps = 433,
                DownMb = 6.5,
                UpMb = 1.2,
            });
        }
        if (adapterCount >= 2)
        {
            adapters.Add(new Adapter
            {
                Id = "eth0",
                Name = "Ethernet",
                Description = "Realtek RTL8111",
                Wireless = false,
                SpeedMbps = 1000,
                DownMb = 25.0,
                UpMb = 15.0,
            });
        }

        return new Snapshot
        {
            Ready = ready,
            CpuTotal = cpuTotal,
            CpuTemp = cpuTemp,
            Cores = cores,
            CpuInfo = new CpuInfo
            {
                Name = "Intel(R) Core(TM) i5-1035G1",
                Vendor = "GenuineIntel",
                Cores = 4,
                Logical = 8,
                BaseMhz = 1200,
                L2Kb = 2048,
                L3Kb = 6144,
                Virtualization = true,
            },
            Ram = new Ram
            {
                Usage = 45,
                UsedGb = 3.6,
                TotalGb = 8.0,
            },
            Disks = disks,
            Gpus = gpus,
            Gpu = gpus.Count > 0 ? gpus[0] : new Gpu(),
            Adapters = adapters,
        };
    }

    [TestMethod]
    public void Build_TabsOrderAndKeys_RootDiskFirst_InternalThenRemovable()
    {
        Snapshot snapshot = CreateSnapshot();
        var config = new AppConfig();
        var lang = new Lang("en");
        var palette = Palette.Dark;
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel model = FullBoard.Build(
            snapshot, config, lang, palette, history, selection, rememberedTab: "");

        // Expected tabs order (R-030: CPU, RAM, GPUs, Disks, Networks):
        // 0: cpu
        // 1: ram
        // 2: gpu0         (Intel)
        // 3: gpu1         (NVIDIA)
        // 4: disk:nvme0n1 (holds "/" -> root disk first!)
        // 5: disk:sda     (internal disk, sorted by kernel name)
        // 6: disk:sdb     (removable USB disk)
        // 7: netwlp0s20f3 (Wi-Fi)
        // 8: neteth0      (Ethernet)
        Assert.AreEqual(9, model.Tabs.Count);

        Assert.AreEqual("cpu", model.Tabs[0].Key);
        Assert.AreEqual("ram", model.Tabs[1].Key);

        // GPUs: one tab per GPU (before Disks)
        Assert.AreEqual("gpu:0000:00:02.0", model.Tabs[2].Key);
        Assert.AreEqual("Intel Iris Plus Graphics G1 (Ice Lake)", model.Tabs[2].Title);
        Assert.AreEqual("gpu:0000:01:00.0", model.Tabs[3].Key);
        Assert.AreEqual("NVIDIA GeForce MX250", model.Tabs[3].Title);

        // Disks: root disk nvme0n1 first
        Assert.AreEqual("disk:nvme0n1", model.Tabs[4].Key);
        Assert.AreEqual("Disk 0", model.Tabs[4].Title);
        Assert.IsTrue(model.Tabs[4].IsDisk);
        CollectionAssert.AreEqual(new[] { "/", "/boot/efi" }, (System.Collections.ICollection)model.Tabs[4].Mounts);

        // Internal disk sda second
        Assert.AreEqual("disk:sda", model.Tabs[5].Key);
        Assert.AreEqual("Disk 1", model.Tabs[5].Title);
        Assert.IsTrue(model.Tabs[5].IsDisk);
        CollectionAssert.AreEqual(new[] { "/data" }, (System.Collections.ICollection)model.Tabs[5].Mounts);

        // Removable disk sdb third
        Assert.AreEqual("disk:sdb", model.Tabs[6].Key);
        Assert.AreEqual("Disk 2", model.Tabs[6].Title);
        Assert.IsTrue(model.Tabs[6].IsDisk);

        // Network: one tab per adapter
        Assert.AreEqual("netwlp0s20f3", model.Tabs[7].Key);
        Assert.AreEqual("Wi-Fi", model.Tabs[7].Title);
        Assert.AreEqual("neteth0", model.Tabs[8].Key);
        Assert.AreEqual("Ethernet", model.Tabs[8].Title);
    }

    [TestMethod]
    public void Build_VirtualAndNetworkDisks_ProduceDedicatedGroupTabs()
    {
        Snapshot snapshot = CreateSnapshot(includeVirtual: true, includeNetworkDrive: true);
        var config = new AppConfig();
        var lang = new Lang("en");
        var palette = Palette.Dark;
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel model = FullBoard.Build(
            snapshot, config, lang, palette, history, selection, rememberedTab: "");

        FullTab? virtualTab = model.Tabs.FirstOrDefault(t => t.Key == "diskvirtual");
        Assert.IsNotNull(virtualTab);
        Assert.AreEqual("Virtual drives", virtualTab.Title);

        FullTab? networkTab = model.Tabs.FirstOrDefault(t => t.Key == "disknetwork");
        Assert.IsNotNull(networkTab);
        Assert.AreEqual("Network drives", networkTab.Title);
    }

    [TestMethod]
    public void Build_CpuTab_HasFactsBigCardsAndSmallPerCoreCards()
    {
        Snapshot snapshot = CreateSnapshot(coreCount: 4);
        var config = new AppConfig { CpuMode = "separated", CpuTemperature = true };
        var lang = new Lang("en");
        var palette = Palette.Dark;
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel model = FullBoard.Build(
            snapshot, config, lang, palette, history, selection, rememberedTab: "cpu");

        FullTab cpuTab = model.Tabs[0];
        Assert.AreEqual("cpu", cpuTab.Key);
        Assert.AreEqual("Intel(R) Core(TM) i5-1035G1", cpuTab.Hardware);

        // Facts: Virtualization, L2, L3
        Assert.IsTrue(cpuTab.Facts.Any(f => f.Title == "Virtualization" && f.Value == "Enabled"));
        Assert.IsTrue(cpuTab.Facts.Any(f => f.Title == "L2" && f.Value == "2.0 MB"));
        Assert.IsTrue(cpuTab.Facts.Any(f => f.Title == "L3" && f.Value == "6.0 MB"));

        // BigCard: cpu
        Assert.AreEqual(1, cpuTab.BigCards.Count);
        Assert.AreEqual("cpu", cpuTab.BigCards[0].Key);
        Assert.AreEqual("30%", cpuTab.BigCards[0].Value);
        Assert.AreEqual(100.0, cpuTab.BigCards[0].Maximum);

        // SmallCards: 4 per-core cards with temperature included
        Assert.AreEqual(4, cpuTab.SmallCards.Count);
        Assert.AreEqual("core0", cpuTab.SmallCards[0].Key);
        StringAssert.Contains(cpuTab.SmallCards[0].Value, "20%");
        StringAssert.Contains(cpuTab.SmallCards[0].Value, "48°C");
    }

    [TestMethod]
    public void Build_GpuTabs_FactsEnginesAndDedicatedMemory()
    {
        Snapshot snapshot = CreateSnapshot(gpuCount: 2);
        var config = new AppConfig();
        var lang = new Lang("en");
        var palette = Palette.Dark;
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel model = FullBoard.Build(
            snapshot, config, lang, palette, history, selection, rememberedTab: "");

        // Intel GPU (gpu0): 2 engines, no temp, shared memory
        FullTab intelTab = model.Tabs.First(t => t.Key == "gpu:0000:00:02.0");
        Assert.IsTrue(intelTab.Facts.Any(f => f.Title == "Driver" && f.Value == "i915"));
        Assert.IsFalse(intelTab.Facts.Any(f => f.Title == "Temp"));
        Assert.AreEqual(2, intelTab.SmallCards.Count);
        Assert.AreEqual("3D", intelTab.SmallCards[0].Title);
        Assert.AreEqual("25%", intelTab.SmallCards[0].Value);

        // NVIDIA GPU (gpu1): Dedicated 1.2 / 2.0 GB, Temp 60 C, no engines -> SmallCards empty
        FullTab nvidiaTab = model.Tabs.First(t => t.Key == "gpu:0000:01:00.0");
        Assert.IsTrue(nvidiaTab.Facts.Any(f => f.Title == "Driver" && f.Value == "nvidia"));
        Assert.IsTrue(nvidiaTab.Facts.Any(f => f.Title == "Dedicated" && f.Value == "1.2 / 2.0 GB"));
        Assert.IsTrue(nvidiaTab.Facts.Any(f => f.Title == "Temp" && f.Value == "60°C"));
        Assert.AreEqual(0, nvidiaTab.SmallCards.Count, "NVIDIA with no engines should have no small cards");
    }

    [TestMethod]
    public void Build_RememberedTab_RestoresDirectlyWhenPresent()
    {
        Snapshot snapshot = CreateSnapshot();
        var config = new AppConfig();
        var lang = new Lang("en");
        var palette = Palette.Dark;
        var history = new HistoryBoard();
        var selection = new FullSelection();

        // Remember "disk:sda"
        FullModel model = FullBoard.Build(
            snapshot, config, lang, palette, history, selection, rememberedTab: "disk:sda");

        Assert.AreEqual("disk:sda", model.SelectedKey);
        Assert.AreEqual("disk:sda", selection.Current);
        Assert.IsFalse(selection.UsedFallback);
    }

    [TestMethod]
    public void Build_RememberedTab_WhenMissing_FallsBackToFirstTab_AndRestoresWhenReplugged()
    {
        // Sample without USB disk: available disks are nvme0n1 and sda
        Snapshot withoutUsb = CreateSnapshot(includeUsb: false);
        var config = new AppConfig();
        var lang = new Lang("en");
        var palette = Palette.Dark;
        var history = new HistoryBoard();
        var selection = new FullSelection();

        // Config remembers USB disk: "disk:sdb"
        FullModel modelFallback = FullBoard.Build(
            withoutUsb, config, lang, palette, history, selection, rememberedTab: "disk:sdb");

        // "disk:sdb" does not exist -> falls back to first tab ("cpu")
        Assert.AreEqual("cpu", modelFallback.SelectedKey);
        Assert.AreEqual("cpu", selection.Current);
        Assert.IsTrue(selection.UsedFallback);

        // Now USB disk is plugged back in!
        Snapshot withUsb = CreateSnapshot(includeUsb: true);
        FullModel modelRestored = FullBoard.Build(
            withUsb, config, lang, palette, history, selection, rememberedTab: "disk:sdb");

        // Reconcile notices fallback was used and remembered tab "disk:sdb" reappeared -> restores it!
        Assert.AreEqual("disk:sdb", modelRestored.SelectedKey);
        Assert.AreEqual("disk:sdb", selection.Current);
        Assert.IsFalse(selection.UsedFallback);
    }

    [TestMethod]
    public void FullSelection_UserPickWinsPermanently_UntilTabDisappears()
    {
        var selection = new FullSelection();
        var tabList = new[]
        {
            new FullTab { Key = "cpu", Title = "CPU" },
            new FullTab { Key = "ram", Title = "RAM" },
            new FullTab { Key = "gpu:0000:00:02.0", Title = "GPU" },
        };

        // User explicitly picked "gpu:0000:00:02.0"
        selection.Select("gpu:0000:00:02.0");
        Assert.AreEqual("gpu:0000:00:02.0", selection.Current);
        Assert.IsFalse(selection.UsedFallback);

        // Even if config remembers "ram", the user choice wins
        string? active = selection.Reconcile(tabList, remembered: "ram");
        Assert.AreEqual("gpu:0000:00:02.0", active);
        Assert.IsFalse(selection.UsedFallback);

        // If "gpu0" disappears from tabList, it falls back
        var withoutGpu = new[]
        {
            new FullTab { Key = "cpu", Title = "CPU" },
            new FullTab { Key = "ram", Title = "RAM" },
        };
        string? fallback = selection.Reconcile(withoutGpu, remembered: "ram");
        Assert.AreEqual("ram", fallback); // remembered tab was present and picked
    }

    [TestMethod]
    public void FullSelection_ExtensionFallback_FindsAnyTabWithSamePrefix()
    {
        var selection = new FullSelection();
        var tabs = new[]
        {
            new FullTab { Key = "cpu", Title = "CPU" },
            new FullTab { Key = "ext:smart:overview", Title = "SMART Overview" },
        };

        // Remembered subtab "ext:smart:disk0" does not exist, but "ext:smart:overview" matches "ext:smart:" prefix!
        string? picked = selection.Reconcile(tabs, remembered: "ext:smart:disk0");
        Assert.AreEqual("ext:smart:overview", picked);
        Assert.IsTrue(selection.UsedFallback);

        Assert.AreEqual("ext:smart:", FullSelection.ExtensionPrefix("ext:smart:disk0"));
        Assert.AreEqual(string.Empty, FullSelection.ExtensionPrefix("disk:nvme0n1"));
    }
}