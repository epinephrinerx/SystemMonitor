using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class OverallBoardTests
{
    private static Snapshot CreateSnapshot(
        bool ready = true,
        int? cpuTemp = 52,
        int? diskTemp = 42,
        int? nvidiaTemp = 60)
    {
        var cores = new List<Core>
        {
            new() { Usage = 20 },
            new() { Usage = 30 },
            new() { Usage = 40 },
            new() { Usage = 50 },
        };

        var disks = new List<Disk>
        {
            new()
            {
                Letter = "/",
                Usage = 65,
                UsedGb = 65.0,
                TotalGb = 100.0,
                ReadMb = 10.0,
                WriteMb = 5.0,
                Media = "SSD",
                Bus = "NVMe",
                Temp = diskTemp,
            },
        };

        var adapters = new List<Adapter>
        {
            new()
            {
                Id = "wlp0s20f3",
                Name = "Wi-Fi",
                DownMb = 4.0,
                UpMb = 1.0,
                SpeedMbps = 200,
                Wireless = true,
            },
        };

        var gpus = new List<Gpu>
        {
            new()
            {
                Present = true,
                Name = "Intel Iris Plus",
                Usage = 25,
                DedicatedGb = 0,
                DedicatedTotalGb = 0,
                SharedGb = 1.2,
                Temp = null, // Intel: no temp
                Engines = new[]
                {
                    new GpuEngine { Name = "Render", Usage = 25 },
                    new GpuEngine { Name = "Blitter", Usage = 0 }, // 0 usage should be omitted
                },
            },
            new()
            {
                Present = true,
                Name = "NVIDIA GeForce MX250",
                Usage = 70,
                DedicatedGb = 1.5,
                DedicatedTotalGb = 2.0,
                SharedGb = 0,
                Temp = nvidiaTemp,
                Engines = new[]
                {
                    new GpuEngine { Name = "3D", Usage = 70 },
                },
            },
        };

        return new Snapshot
        {
            Ready = ready,
            CpuTotal = 35,
            CpuTemp = cpuTemp,
            Cores = cores,
            Ram = new Ram
            {
                Usage = 50,
                UsedGb = 4.0,
                TotalGb = 8.0,
            },
            Disks = disks,
            Adapters = adapters,
            Gpus = gpus,
            Gpu = gpus[0],
        };
    }

    [TestMethod]
    public void Build_SectionOrder_CpuRamDiskGpuNetwork()
    {
        var config = new AppConfig();
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        Snapshot snapshot = CreateSnapshot();
        OverallModel model = OverallBoard.Build(snapshot, config, lang, palette, alerts);

        // Sections: cpu, ram, disk, gpu (intel), gpu (nvidia), network
        Assert.AreEqual(6, model.Sections.Count);

        Assert.AreEqual("cpu", model.Sections[0].Key);
        Assert.AreEqual("ram", model.Sections[1].Key);
        Assert.AreEqual("disk", model.Sections[2].Key);
        Assert.AreEqual("gpu", model.Sections[3].Key);
        Assert.AreEqual("Intel Iris Plus", model.Sections[3].Title);
        Assert.AreEqual("gpu", model.Sections[4].Key);
        Assert.AreEqual("NVIDIA GeForce MX250", model.Sections[4].Title);
        Assert.AreEqual("network", model.Sections[5].Key);

        StringAssert.StartsWith(model.Legend, "= ");
    }

    [TestMethod]
    public void Build_ModuleToggles_OmitDisabledSections_AndKeepOrder()
    {
        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = true,
            ShowDisk = false,
            ShowGpu = true,
            ShowNetwork = false,
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        Snapshot snapshot = CreateSnapshot();
        OverallModel model = OverallBoard.Build(snapshot, config, lang, palette, alerts);

        // Only RAM and 2 GPUs
        Assert.AreEqual(3, model.Sections.Count);
        Assert.AreEqual("ram", model.Sections[0].Key);
        Assert.AreEqual("gpu", model.Sections[1].Key);
        Assert.AreEqual("gpu", model.Sections[2].Key);
    }

    [TestMethod]
    public void Build_CpuSection_SeparatedVsTotal()
    {
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();
        Snapshot snapshot = CreateSnapshot(cpuTemp: 55);

        // Separated: 2 columns, rows C0..C3, note is "55°C", no badges on rows
        var configSep = new AppConfig { ShowCpu = true, ShowRam = false, ShowDisk = false, ShowGpu = false, ShowNetwork = false, CpuMode = "separated" };
        OverallModel modelSep = OverallBoard.Build(snapshot, configSep, lang, palette, alerts);
        SectionModel cpuSep = modelSep.Sections[0];
        Assert.AreEqual(2, cpuSep.Columns);
        Assert.AreEqual("55°C", cpuSep.Note);
        Assert.AreEqual(4, cpuSep.Rows.Count);
        Assert.AreEqual("C0", cpuSep.Rows[0].Title);
        Assert.IsNull(cpuSep.Rows[0].Badge);

        // Total: 1 column, 1 row, badge carries CPU temp
        var configTot = new AppConfig { ShowCpu = true, ShowRam = false, ShowDisk = false, ShowGpu = false, ShowNetwork = false, CpuMode = "total" };
        OverallModel modelTot = OverallBoard.Build(snapshot, configTot, lang, palette, alerts);
        SectionModel cpuTot = modelTot.Sections[0];
        Assert.AreEqual(1, cpuTot.Columns);
        Assert.AreEqual(1, cpuTot.Rows.Count);
        Assert.AreEqual("CPU", cpuTot.Rows[0].Title);
        Assert.AreEqual("4 cores", cpuTot.Rows[0].Detail);
        Assert.IsNotNull(cpuTot.Rows[0].Badge);
        Assert.AreEqual("55°C", cpuTot.Rows[0].Badge?.Text);
    }

    [TestMethod]
    public void Build_OneGpuSectionPerGpu_WithEnginesAndMemoryFormatting()
    {
        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = false,
            ShowGpu = true,
            ShowNetwork = false,
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        Snapshot snapshot = CreateSnapshot(nvidiaTemp: 62);
        OverallModel model = OverallBoard.Build(snapshot, config, lang, palette, alerts);

        Assert.AreEqual(2, model.Sections.Count);

        // Intel section
        SectionModel intel = model.Sections[0];
        Assert.AreEqual("Intel Iris Plus", intel.Title);
        Assert.AreEqual(string.Empty, intel.Note, "Intel GPU without temp should have empty note");
        // Row 0 is main usage row, plus only engines with usage > 0 (Render=25, Blitter=0 is excluded)
        Assert.AreEqual(2, intel.Rows.Count);
        Assert.AreEqual("GPU", intel.Rows[0].Title);
        Assert.AreEqual(25, intel.Rows[0].Percent);
        Assert.IsNull(intel.Rows[0].Badge, "Intel row has no badge when temp is null");
        StringAssert.Contains(intel.Rows[0].Detail, "1.2 GB Shared");
        Assert.AreEqual("Render", intel.Rows[1].Title);
        Assert.AreEqual(25, intel.Rows[1].Percent);

        // NVIDIA section
        SectionModel nvidia = model.Sections[1];
        Assert.AreEqual("NVIDIA GeForce MX250", nvidia.Title);
        Assert.AreEqual("62°C", nvidia.Note);
        Assert.AreEqual(2, nvidia.Rows.Count);
        Assert.AreEqual("GPU", nvidia.Rows[0].Title);
        Assert.AreEqual(70, nvidia.Rows[0].Percent);
        Assert.IsNotNull(nvidia.Rows[0].Badge);
        Assert.AreEqual("62°C", nvidia.Rows[0].Badge?.Text);
        // Memory format when DedicatedTotalGb > 0: "x.x / y.y GB Dedicated"
        StringAssert.Contains(nvidia.Rows[0].Detail, "1.5 / 2.0 GB Dedicated");
        Assert.AreEqual("3D", nvidia.Rows[1].Title);
        Assert.AreEqual(70, nvidia.Rows[1].Percent);
    }

    [TestMethod]
    public void Build_NoInventedValues_NullTempsYieldEmptyOrNa()
    {
        var config = new AppConfig();
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        // Null temperatures everywhere
        Snapshot snapshot = CreateSnapshot(cpuTemp: null, diskTemp: null, nvidiaTemp: null);
        OverallModel model = OverallBoard.Build(snapshot, config, lang, palette, alerts);

        SectionModel cpu = model.Sections[0];
        Assert.AreEqual(string.Empty, cpu.Note, "Null CPU temp should not invent 0°C");

        SectionModel disk = model.Sections[2];
        Assert.AreEqual("n/a", disk.Rows[0].Badge?.Text, "Null disk temp should be n/a badge, not 0°C");

        SectionModel nvidia = model.Sections[4];
        Assert.AreEqual(string.Empty, nvidia.Note, "Null GPU temp should have empty note");
        Assert.IsNull(nvidia.Rows[0].Badge);
    }

    [TestMethod]
    public void Build_CpuSection_CpuTemperatureDisabled_HidesCpuTemperatureInOverall()
    {
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();
        Snapshot snapshot = CreateSnapshot(cpuTemp: 55);

        // Separated mode with CpuTemperature = false: Note is empty, no row badges
        var configSep = new AppConfig
        {
            ShowCpu = true,
            ShowRam = false,
            ShowDisk = false,
            ShowGpu = false,
            ShowNetwork = false,
            CpuMode = "separated",
            CpuTemperature = false,
        };
        OverallModel modelSep = OverallBoard.Build(snapshot, configSep, lang, palette, alerts);
        SectionModel cpuSep = modelSep.Sections[0];
        Assert.AreEqual(string.Empty, cpuSep.Note, "CPU note should be empty when CpuTemperature is false");
        foreach (MeterRow row in cpuSep.Rows)
        {
            Assert.IsNull(row.Badge, "Core row should have no badge when CpuTemperature is false");
        }

        // Total mode with CpuTemperature = false: Note is empty, row Badge is null (not n/a)
        var configTot = new AppConfig
        {
            ShowCpu = true,
            ShowRam = false,
            ShowDisk = false,
            ShowGpu = false,
            ShowNetwork = false,
            CpuMode = "total",
            CpuTemperature = false,
        };
        OverallModel modelTot = OverallBoard.Build(snapshot, configTot, lang, palette, alerts);
        SectionModel cpuTot = modelTot.Sections[0];
        Assert.AreEqual(string.Empty, cpuTot.Note, "CPU note should be empty when CpuTemperature is false");
        Assert.IsNull(cpuTot.Rows[0].Badge, "CPU total row badge should be null when CpuTemperature is false");

        // Contrast: when CpuTemperature is true, Note is 55°C and Badge is present
        configTot.CpuTemperature = true;
        OverallModel modelWithTemp = OverallBoard.Build(snapshot, configTot, lang, palette, alerts);
        Assert.AreEqual("55°C", modelWithTemp.Sections[0].Note);
        Assert.IsNotNull(modelWithTemp.Sections[0].Rows[0].Badge);
        Assert.AreEqual("55°C", modelWithTemp.Sections[0].Rows[0].Badge?.Text);
    }

    [TestMethod]
    public void Build_DiskSection_IncludeNetworkDisabled_DropsNetworkDrivesFromRowsAndTotals()
    {
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

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
                Media = "SSD",
                Bus = "NVMe",
                Temp = 42,
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
                Media = "Network",
                Bus = "SMB",
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
            ShowGpu = false,
            ShowNetwork = false,
            DiskMode = "separated",
            IncludeNetwork = false, // Network drive dropped!
        };

        // Separated mode: only local drive row appears
        OverallModel modelSep = OverallBoard.Build(snapshot, config, lang, palette, alerts);
        SectionModel diskSep = modelSep.Sections[0];
        Assert.AreEqual(1, diskSep.Rows.Count);
        Assert.AreEqual("/", diskSep.Rows[0].DriveKey);
        Assert.AreEqual(40, diskSep.Rows[0].Percent);

        // Total mode: total calculates only from local drive (40%)
        config.DiskMode = "total";
        OverallModel modelTot = OverallBoard.Build(snapshot, config, lang, palette, alerts);
        SectionModel diskTot = modelTot.Sections[0];
        Assert.AreEqual(1, diskTot.Rows.Count);
        Assert.AreEqual(40, diskTot.Rows[0].Percent);
        Assert.IsNull(diskTot.Rows[0].ValueText, "With real capacity, ValueText is null to show percentage");

        // When IncludeNetwork is true: network drive is included in rows and total
        config.DiskMode = "separated";
        config.IncludeNetwork = true;
        OverallModel modelSepWithNet = OverallBoard.Build(snapshot, config, lang, palette, alerts);
        Assert.AreEqual(2, modelSepWithNet.Sections[0].Rows.Count);
        Assert.AreEqual("/", modelSepWithNet.Sections[0].Rows[0].DriveKey);
        Assert.AreEqual("/mnt/share", modelSepWithNet.Sections[0].Rows[1].DriveKey);

        config.DiskMode = "total";
        OverallModel modelTotWithNet = OverallBoard.Build(snapshot, config, lang, palette, alerts);
        // (40 + 80) / 200 = 60%
        Assert.AreEqual(60, modelTotWithNet.Sections[0].Rows[0].Percent);
    }

    [TestMethod]
    public void Build_DiskSection_NoDrivesOrZeroCapacity_ShowsNaWithoutInventingZeroPercent()
    {
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        // Case 1: Snapshot with no drives at all
        var snapshotNoDrives = new Snapshot
        {
            Ready = true,
            Disks = Array.Empty<Disk>(),
        };

        var configTot = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = true,
            ShowGpu = false,
            ShowNetwork = false,
            DiskMode = "total",
        };

        OverallModel modelNoDrives = OverallBoard.Build(snapshotNoDrives, configTot, lang, palette, alerts);
        Assert.AreEqual(1, modelNoDrives.Sections.Count);
        SectionModel diskSection = modelNoDrives.Sections[0];
        Assert.AreEqual(1, diskSection.Rows.Count);
        MeterRow row = diskSection.Rows[0];
        Assert.AreEqual("All drives", row.Title);
        Assert.AreEqual("n/a", row.ValueText, "Zero drives should show n/a instead of inventing 0%");
        Assert.AreEqual(string.Empty, row.Detail);

        // Case 2: Drive present but zero capacity (TotalGb = 0)
        var snapshotZeroCapacity = new Snapshot
        {
            Ready = true,
            Disks = new[]
            {
                new Disk
                {
                    Letter = "/empty",
                    Usage = 0,
                    UsedGb = 0,
                    TotalGb = 0,
                },
            },
        };

        OverallModel modelZeroCapacity = OverallBoard.Build(snapshotZeroCapacity, configTot, lang, palette, alerts);
        MeterRow rowZeroCap = modelZeroCapacity.Sections[0].Rows[0];
        Assert.AreEqual("n/a", rowZeroCap.ValueText, "Zero capacity should show n/a instead of inventing 0%");
    }
}
