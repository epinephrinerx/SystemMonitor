using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class WidgetBoardTests
{
    private static Snapshot CreateSnapshot(
        bool ready = true,
        int cpuTotal = 25,
        int? cpuTemp = 50,
        int coreCount = 8,
        int diskCount = 2,
        int adapterCount = 2,
        bool gpuPresent = true,
        int firstDiskUsage = 60)
    {
        var cores = new List<Core>(coreCount);
        for (int i = 0; i < coreCount; i++)
        {
            cores.Add(new Core
            {
                Usage = (i + 1) * 10,
                Temp = 45 + i,
            });
        }

        var disks = new List<Disk>(diskCount);
        if (diskCount >= 1)
        {
            disks.Add(new Disk
            {
                Letter = "/",
                Usage = firstDiskUsage,
                UsedGb = firstDiskUsage,
                TotalGb = 100,
                ReadMb = 12.5,
                WriteMb = 4.2,
                Temp = 42,
            });
        }
        if (diskCount >= 2)
        {
            disks.Add(new Disk
            {
                Letter = "/boot/efi",
                Usage = 30,
                UsedGb = 0.3,
                TotalGb = 1.0,
                ReadMb = 1.0,
                WriteMb = 0.5,
                Temp = 40,
            });
        }

        var adapters = new List<Adapter>(adapterCount);
        if (adapterCount >= 1)
        {
            adapters.Add(new Adapter
            {
                Id = "wlp0s20f3",
                Name = "Wi-Fi",
                DownMb = 5.2,
                UpMb = 1.1,
                SpeedMbps = 336,
                Wireless = true,
            });
        }
        if (adapterCount >= 2)
        {
            adapters.Add(new Adapter
            {
                Id = "eth0",
                Name = "Ethernet",
                DownMb = 20.0,
                UpMb = 10.0,
                SpeedMbps = 600,
                Wireless = false,
            });
        }

        var gpus = new List<Gpu>();
        if (gpuPresent)
        {
            gpus.Add(new Gpu
            {
                Present = true,
                Name = "Intel Iris Plus",
                Usage = 35,
                DedicatedGb = 0,
                DedicatedTotalGb = 0,
                SharedGb = 1.5,
                Temp = null,
                Engines = new[]
                {
                    new GpuEngine { Name = "Render", Usage = 35 },
                },
            });
            gpus.Add(new Gpu
            {
                Present = true,
                Name = "NVIDIA GeForce MX250",
                Usage = 50,
                DedicatedGb = 1.2,
                DedicatedTotalGb = 2.0,
                SharedGb = 0,
                Temp = 58,
                Engines = new[]
                {
                    new GpuEngine { Name = "3D", Usage = 50 },
                },
            });
        }

        return new Snapshot
        {
            Ready = ready,
            CpuTotal = cpuTotal,
            CpuTemp = cpuTemp,
            CpuTempEstimated = false,
            Cores = cores,
            Ram = new Ram
            {
                Usage = 45,
                UsedGb = 3.6,
                TotalGb = 8.0,
            },
            Disks = disks,
            Adapters = adapters,
            Gpus = gpus,
            Gpu = gpus.Count > 0 ? gpus[0] : new Gpu { Present = false },
        };
    }

    [TestMethod]
    public void Build_NullOrUnreadySnapshot_ReturnsConnectingPage()
    {
        var config = new AppConfig();
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        // Null snapshot
        var pages1 = WidgetBoard.Build(null, config, lang, palette, alerts);
        Assert.AreEqual(1, pages1.Count);
        Assert.AreEqual("connecting", pages1[0].Key);
        Assert.AreEqual(WidgetPageKind.Empty, pages1[0].Kind);
        Assert.AreEqual("Reading hardware...", pages1[0].Message);

        // Unready snapshot
        Snapshot unready = CreateSnapshot(ready: false);
        var pages2 = WidgetBoard.Build(unready, config, lang, palette, alerts);
        Assert.AreEqual(1, pages2.Count);
        Assert.AreEqual("connecting", pages2[0].Key);
    }

    [TestMethod]
    public void Build_AllModulesDisabled_ReturnsNoDataPage()
    {
        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = false,
            ShowGpu = false,
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        Snapshot snapshot = CreateSnapshot(ready: true);
        var pages = WidgetBoard.Build(snapshot, config, lang, palette, alerts);

        Assert.AreEqual(1, pages.Count);
        Assert.AreEqual("empty", pages[0].Key);
        Assert.AreEqual(WidgetPageKind.Empty, pages[0].Kind);
        Assert.AreEqual("Select something to display", pages[0].Message);
    }

    [TestMethod]
    public void Build_CpuPages_Separated_GroupsInFours()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = false,
            CpuMode = "separated",
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        // 8 cores -> 2 pages of 4 cores
        Snapshot snapshot8 = CreateSnapshot(coreCount: 8);
        var pages8 = WidgetBoard.Build(snapshot8, config, lang, palette, alerts);

        Assert.AreEqual(2, pages8.Count);

        Assert.AreEqual("cpu:0-3", pages8[0].Key);
        Assert.AreEqual(WidgetPageKind.CpuGroup, pages8[0].Kind);
        Assert.AreEqual("CPU  C0-3", pages8[0].Title);
        Assert.AreEqual("25%", pages8[0].Value);
        Assert.AreEqual(2, pages8[0].Columns);
        Assert.AreEqual(4, pages8[0].Rows.Count);
        Assert.AreEqual("C0", pages8[0].Rows[0].Title);
        Assert.AreEqual("C3", pages8[0].Rows[3].Title);

        Assert.AreEqual("cpu:4-7", pages8[1].Key);
        Assert.AreEqual(WidgetPageKind.CpuGroup, pages8[1].Kind);
        Assert.AreEqual("CPU  C4-7", pages8[1].Title);
        Assert.AreEqual(2, pages8[1].Columns);
        Assert.AreEqual(4, pages8[1].Rows.Count);

        // 6 cores -> page 1 with 4 cores (2 cols), page 2 with 2 cores (1 col)
        Snapshot snapshot6 = CreateSnapshot(coreCount: 6);
        var pages6 = WidgetBoard.Build(snapshot6, config, lang, palette, alerts);
        Assert.AreEqual(2, pages6.Count);
        Assert.AreEqual("cpu:0-3", pages6[0].Key);
        Assert.AreEqual(2, pages6[0].Columns);
        Assert.AreEqual("cpu:4-5", pages6[1].Key);
        Assert.AreEqual(1, pages6[1].Columns);
        Assert.AreEqual(2, pages6[1].Rows.Count);
    }

    [TestMethod]
    public void Build_CpuPages_Total_ProducesSinglePage()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = false,
            CpuMode = "total",
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        Snapshot snapshot = CreateSnapshot(coreCount: 4, cpuTotal: 42);
        var pages = WidgetBoard.Build(snapshot, config, lang, palette, alerts);

        Assert.AreEqual(1, pages.Count);
        Assert.AreEqual("cpu", pages[0].Key);
        Assert.AreEqual(WidgetPageKind.CpuTotal, pages[0].Kind);
        Assert.AreEqual("CPU (Combined)", pages[0].Title);
        Assert.AreEqual("42%", pages[0].Value);
        Assert.AreEqual("4 cores", pages[0].Detail);
        Assert.AreEqual(1, pages[0].Rows.Count);
        Assert.AreEqual(42, pages[0].Rows[0].Percent);
    }

    [TestMethod]
    public void Build_RamPage_ShowsUsageAndMemoryInUse()
    {
        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = true,
            ShowDisk = false,
            ShowNetwork = false,
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        Snapshot snapshot = CreateSnapshot();
        var pages = WidgetBoard.Build(snapshot, config, lang, palette, alerts);

        Assert.AreEqual(1, pages.Count);
        Assert.AreEqual("ram", pages[0].Key);
        Assert.AreEqual(WidgetPageKind.Ram, pages[0].Kind);
        Assert.AreEqual("RAM", pages[0].Title);
        Assert.AreEqual("45%", pages[0].Value);
        Assert.AreEqual("3.6/8.0 GB in use", pages[0].Detail);
        Assert.AreEqual("n/a", pages[0].Badge?.Text);
    }

    [TestMethod]
    public void Build_DiskPages_Separated_ProducesOnePagePerDrive()
    {
        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = true,
            ShowNetwork = false,
            DiskMode = "separated",
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        Snapshot snapshot = CreateSnapshot(diskCount: 2);
        var pages = WidgetBoard.Build(snapshot, config, lang, palette, alerts);

        Assert.AreEqual(2, pages.Count);

        Assert.AreEqual("disk:/", pages[0].Key);
        Assert.AreEqual(WidgetPageKind.DiskOne, pages[0].Kind);
        Assert.AreEqual("Drive /", pages[0].Title);
        Assert.AreEqual("60%", pages[0].Value);
        Assert.AreEqual("R 12.5 | W 4.2 MB/s", pages[0].Detail);
        Assert.AreEqual("42°C", pages[0].Badge?.Text);
        Assert.AreEqual("/", pages[0].DriveKey);

        Assert.AreEqual("disk:/boot/efi", pages[1].Key);
        Assert.AreEqual(WidgetPageKind.DiskOne, pages[1].Kind);
        Assert.AreEqual("Drive /boot/efi", pages[1].Title);
        Assert.AreEqual("30%", pages[1].Value);
    }

    [TestMethod]
    public void Build_DiskPages_Total_ProducesSingleCombinedPage()
    {
        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = true,
            ShowNetwork = false,
            DiskMode = "total",
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        Snapshot snapshot = CreateSnapshot(diskCount: 2);
        var pages = WidgetBoard.Build(snapshot, config, lang, palette, alerts);

        Assert.AreEqual(1, pages.Count);
        Assert.AreEqual("disk", pages[0].Key);
        Assert.AreEqual(WidgetPageKind.DiskTotal, pages[0].Kind);
        Assert.AreEqual("Disk Storage (All drives)", pages[0].Title);
        // Total: 60.3 / 101.0 GB = 60%
        Assert.AreEqual("60%", pages[0].Value);
        Assert.AreEqual("R 13.5 | W 4.7 MB/s", pages[0].Detail);
        Assert.AreEqual("42°C", pages[0].Badge?.Text); // Max of 42 and 40
    }

    [TestMethod]
    public void Build_NetworkPages_SeparatedAndTotal()
    {
        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = true,
            NetworkMode = "separated",
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        Snapshot snapshot = CreateSnapshot(adapterCount: 2);
        var pagesSeparated = WidgetBoard.Build(snapshot, config, lang, palette, alerts);

        Assert.AreEqual(2, pagesSeparated.Count);
        Assert.AreEqual("net:wlp0s20f3", pagesSeparated[0].Key);
        Assert.AreEqual("Wi-Fi", pagesSeparated[0].Title);
        Assert.AreEqual("15%", pagesSeparated[0].Value);
        Assert.AreEqual("↓ 5.2 | ↑ 1.1 MB/s", pagesSeparated[0].Detail);

        Assert.AreEqual("net:eth0", pagesSeparated[1].Key);
        Assert.AreEqual("Ethernet", pagesSeparated[1].Title);
        Assert.AreEqual("40%", pagesSeparated[1].Value);

        // Total mode
        config.NetworkMode = "total";
        var pagesTotal = WidgetBoard.Build(snapshot, config, lang, palette, alerts);
        Assert.AreEqual(1, pagesTotal.Count);
        Assert.AreEqual("net", pagesTotal[0].Key);
        Assert.AreEqual("Network (All adapters)", pagesTotal[0].Title);
        // Spec §5b speed format: >= 100 -> F0, otherwise F1.
        Assert.AreEqual("↓ 25.2 | ↑ 11.1 MB/s", pagesTotal[0].Detail);
    }

    [TestMethod]
    public void Build_NoGpuPage_Ever_EvenWhenGpuIsPresent()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowRam = true,
            ShowDisk = true,
            ShowNetwork = true,
            ShowGpu = true, // GPU enabled in config!
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        // 2 GPUs in snapshot
        Snapshot snapshot = CreateSnapshot(gpuPresent: true);
        Assert.IsTrue(snapshot.Gpus.Count > 0);
        Assert.IsTrue(snapshot.Gpu.Present);

        var pages = WidgetBoard.Build(snapshot, config, lang, palette, alerts);

        // Spec §3: "There is no GPU page in the widget."
        foreach (WidgetPage page in pages)
        {
            Assert.IsFalse(page.Key.StartsWith("gpu", StringComparison.OrdinalIgnoreCase), $"Unexpected GPU page key: {page.Key}");
            Assert.IsFalse(page.Title.Contains("GPU", StringComparison.OrdinalIgnoreCase), $"Unexpected GPU in title: {page.Title}");
        }
    }

    [TestMethod]
    public void Build_DiskAlert_ReplacesDetailWithAlertText()
    {
        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = true,
            ShowNetwork = false,
            DiskMode = "separated",
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        Snapshot snapshot = CreateSnapshot(diskCount: 1, firstDiskUsage: 75);
        alerts.Update(snapshot.Disks);

        var pages = WidgetBoard.Build(snapshot, config, lang, palette, alerts);

        Assert.AreEqual(1, pages.Count);
        WidgetPage page = pages[0];
        Assert.IsNotNull(page.Alert);
        Assert.AreEqual(DiskAlertLevel.Warm, page.Alert.Level);
        Assert.AreEqual("⚠ Drive / is running low (75% used) · click to free space", page.Detail);
    }

    [TestMethod]
    public void Build_CpuPages_CpuTemperatureDisabled_HidesCpuTemperatureInWidget()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = false,
            CpuMode = "separated",
            CpuTemperature = false, // disabled!
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        Snapshot snapshot = CreateSnapshot(coreCount: 8, cpuTemp: 60);

        // Separated mode: groups of 4 cores
        var pagesSeparated = WidgetBoard.Build(snapshot, config, lang, palette, alerts);
        Assert.AreEqual(2, pagesSeparated.Count);
        foreach (WidgetPage page in pagesSeparated)
        {
            Assert.IsNull(page.Badge, "CPU page should have no temperature badge when CpuTemperature is false");
        }

        // Total mode: 1 page
        config.CpuMode = "total";
        var pagesTotal = WidgetBoard.Build(snapshot, config, lang, palette, alerts);
        Assert.AreEqual(1, pagesTotal.Count);
        Assert.IsNull(pagesTotal[0].Badge, "CPU total page should have no temperature badge when CpuTemperature is false");

        // When CpuTemperature is enabled, badge is present
        config.CpuTemperature = true;
        var pagesWithTemp = WidgetBoard.Build(snapshot, config, lang, palette, alerts);
        Assert.IsNotNull(pagesWithTemp[0].Badge, "CPU page should have badge when CpuTemperature is true");
        Assert.AreEqual("60°C", pagesWithTemp[0].Badge?.Text);
    }

    [TestMethod]
    public void Build_DiskPages_IncludeNetworkDisabled_DropsNetworkDrivesFromPagesAndTotals()
    {
        var disks = new List<Disk>
        {
            new()
            {
                Letter = "/",
                Usage = 40,
                UsedGb = 40.0,
                TotalGb = 100.0,
                ReadMb = 10.0,
                WriteMb = 5.0,
                Temp = 38,
                IsNetwork = false,
            },
            new()
            {
                Letter = "/mnt/share",
                Usage = 80,
                UsedGb = 80.0,
                TotalGb = 100.0,
                ReadMb = 20.0,
                WriteMb = 15.0,
                Temp = null,
                IsNetwork = true,
            },
        };

        var snapshot = new Snapshot
        {
            Ready = true,
            Disks = disks,
        };

        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = true,
            ShowNetwork = false,
            DiskMode = "separated",
            IncludeNetwork = false, // Network drives dropped!
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        // Separated mode with IncludeNetwork = false: only local drive "/" has a page
        var pagesSep = WidgetBoard.Build(snapshot, config, lang, palette, alerts);
        Assert.AreEqual(1, pagesSep.Count);
        Assert.AreEqual("disk:/", pagesSep[0].Key);
        Assert.AreEqual("/", pagesSep[0].DriveKey);
        Assert.AreEqual("40%", pagesSep[0].Value);

        // Total mode with IncludeNetwork = false: total calculates only from local drive (40/100 = 40%)
        config.DiskMode = "total";
        var pagesTot = WidgetBoard.Build(snapshot, config, lang, palette, alerts);
        Assert.AreEqual(1, pagesTot.Count);
        Assert.AreEqual("disk", pagesTot[0].Key);
        Assert.AreEqual("40%", pagesTot[0].Value);
        Assert.AreEqual("R 10.0 | W 5.0 MB/s", pagesTot[0].Detail);

        // When IncludeNetwork = true: both drives are included
        config.DiskMode = "separated";
        config.IncludeNetwork = true;
        var pagesSepWithNet = WidgetBoard.Build(snapshot, config, lang, palette, alerts);
        Assert.AreEqual(2, pagesSepWithNet.Count);
        Assert.AreEqual("disk:/", pagesSepWithNet[0].Key);
        Assert.AreEqual("disk:/mnt/share", pagesSepWithNet[1].Key);

        config.DiskMode = "total";
        var pagesTotWithNet = WidgetBoard.Build(snapshot, config, lang, palette, alerts);
        Assert.AreEqual(1, pagesTotWithNet.Count);
        // Combined total: (40 + 80) / (100 + 100) = 120 / 200 = 60%
        Assert.AreEqual("60%", pagesTotWithNet[0].Value);
        Assert.AreEqual("R 30.0 | W 20.0 MB/s", pagesTotWithNet[0].Detail);
    }
}
