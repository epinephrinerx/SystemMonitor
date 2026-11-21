using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class GpuPropertiesTests
{
    [TestMethod]
    public void LinkText_FormatsPcieGenerationAndWidth()
    {
        Assert.AreEqual("PCIe 1.0 ×4", GpuReader.LinkText("2.5 GT/s PCIe", "4"));
        Assert.AreEqual("PCIe 2.0 ×8", GpuReader.LinkText("5.0 GT/s", "8"));
        Assert.AreEqual("PCIe 3.0 ×4", GpuReader.LinkText("8.0 GT/s PCIe", "4"));
        Assert.AreEqual("PCIe 4.0 ×16", GpuReader.LinkText("16.0 GT/s PCIe", "16"));
        Assert.AreEqual("PCIe 5.0 ×16", GpuReader.LinkText("32.0 GT/s", "16"));
        Assert.AreEqual("PCIe 6.0 ×16", GpuReader.LinkText("64.0 GT/s", "16"));
    }

    [TestMethod]
    public void LinkText_WhenUnknownOrZeroOr255OrMissing_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, GpuReader.LinkText("Unknown", "16"));
        Assert.AreEqual(string.Empty, GpuReader.LinkText("8.0 GT/s PCIe", "0"));
        Assert.AreEqual(string.Empty, GpuReader.LinkText("8.0 GT/s PCIe", "255"));
        Assert.AreEqual(string.Empty, GpuReader.LinkText(null, "4"));
        Assert.AreEqual(string.Empty, GpuReader.LinkText("8.0 GT/s", null));
        Assert.AreEqual(string.Empty, GpuReader.LinkText(string.Empty, string.Empty));
        Assert.AreEqual(string.Empty, GpuReader.LinkText("10.0 GT/s", "4")); // unmapped rate
    }

    [TestMethod]
    public void Generation_FormatsValidAndRejectsInvalid()
    {
        Assert.AreEqual("PCIe 3.0 ×4", GpuReader.Generation(3, 4));
        Assert.AreEqual("PCIe 4.0 ×16", GpuReader.Generation(4, 16));

        Assert.AreEqual(string.Empty, GpuReader.Generation(0, 4));
        Assert.AreEqual(string.Empty, GpuReader.Generation(-1, 4));
        Assert.AreEqual(string.Empty, GpuReader.Generation(10, 4));
        Assert.AreEqual(string.Empty, GpuReader.Generation(3, 0));
        Assert.AreEqual(string.Empty, GpuReader.Generation(3, 255));
        Assert.AreEqual(string.Empty, GpuReader.Generation(null, 4));
        Assert.AreEqual(string.Empty, GpuReader.Generation(3, null));
    }

    [TestMethod]
    public void PciSlot_KeepsFourDigitDomain()
    {
        Assert.AreEqual("0000:2b:00.0", GpuReader.PciSlot("0000:2b:00.0"));
        Assert.AreEqual("0000:2b:00.0", GpuReader.PciSlot("00000000:2b:00.0"));
        Assert.AreEqual("0000:2b:00.0", GpuReader.PciSlot("0:2b:00.0"));
        Assert.AreEqual("0001:03:00.0", GpuReader.PciSlot("1:03:00.0"));
        Assert.AreEqual(string.Empty, GpuReader.PciSlot(string.Empty));
    }

    [TestMethod]
    public void Amd_DpmClock_ParsesStarredLine_OrNullWhenNoneStarred()
    {
        string tableWithStar =
            "0: 300Mhz\n" +
            "1: 600Mhz\n" +
            "2: 1750Mhz *\n";
        Assert.AreEqual(1750, GpuReader.DpmClock(tableWithStar));

        string tableWithMiddleStar =
            "0: 300Mhz\n" +
            "1: 617Mhz *\n" +
            "2: 1200Mhz\n";
        Assert.AreEqual(617, GpuReader.DpmClock(tableWithMiddleStar));

        string tableNoneStarred =
            "0: 300Mhz\n" +
            "1: 600Mhz\n" +
            "2: 1200Mhz\n";
        Assert.IsNull(GpuReader.DpmClock(tableNoneStarred));

        Assert.IsNull(GpuReader.DpmClock(null));
        Assert.IsNull(GpuReader.DpmClock(string.Empty));
    }

    [TestMethod]
    public void Amd_HwmonPower_ConvertsMicroWattsToWatts()
    {
        using var fake = new FakeRoot();
        string devPath = fake.CreateDirectory("sys/class/drm/card0/device");
        fake.WriteFile("sys/class/drm/card0/device/hwmon0/power1_average", "25000000\n");
        fake.WriteFile("sys/class/drm/card0/device/hwmon0/power1_cap", "35000000\n");

        (double? drawW, double? capW) = GpuReader.HwmonPower(devPath);

        Assert.IsNotNull(drawW);
        Assert.AreEqual(25.0, drawW.Value, 0.001);
        Assert.IsNotNull(capW);
        Assert.AreEqual(35.0, capW.Value, 0.001);
    }

    [TestMethod]
    public void Amd_Read_PopulatesClocksPowerLinkAndManufacturer()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/drm/card0/device/uevent",
            "DRIVER=amdgpu\nPCI_ID=1002:73df\nPCI_SLOT_NAME=0000:03:00.0\n");
        fake.WriteFile("sys/class/drm/card0/device/pp_dpm_sclk", "0: 500Mhz\n1: 1800Mhz *\n");
        fake.WriteFile("sys/class/drm/card0/device/pp_dpm_mclk", "0: 300Mhz\n1: 1000Mhz *\n");
        fake.WriteFile("sys/class/drm/card0/device/current_link_speed", "8.0 GT/s PCIe\n");
        fake.WriteFile("sys/class/drm/card0/device/current_link_width", "16\n");
        fake.WriteFile("sys/class/drm/card0/device/max_link_speed", "16.0 GT/s PCIe\n");
        fake.WriteFile("sys/class/drm/card0/device/max_link_width", "16\n");
        fake.WriteFile("sys/class/drm/card0/device/hwmon0/power1_average", "45000000\n");
        fake.WriteFile("sys/class/drm/card0/device/hwmon0/power1_cap", "65000000\n");

        var runner = new FakeCommandRunner();
        var reader = new GpuReader(fake.Root, runner);

        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(1, gpus.Count);
        Gpu gpu = gpus[0];

        Assert.AreEqual("Advanced Micro Devices, Inc. [AMD/ATI]", gpu.Manufacturer);
        Assert.AreEqual("0000:03:00.0", gpu.PciSlot);
        Assert.AreEqual(1800, gpu.CoreClockMhz);
        Assert.AreEqual(1000, gpu.MemoryClockMhz);
        Assert.AreEqual(45.0, gpu.PowerW.GetValueOrDefault(), 0.01);
        Assert.AreEqual(65.0, gpu.PowerCapW.GetValueOrDefault(), 0.01);
        Assert.AreEqual("PCIe 3.0 ×16", gpu.LinkCurrent);
        Assert.AreEqual("PCIe 4.0 ×16", gpu.LinkMax);
    }

    [TestMethod]
    public void Intel_Read_PopulatesGtActFreqMhz_AndMemoryClockPowerNull()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/drm/card0/device/uevent",
            "DRIVER=i915\nPCI_ID=8086:8a56\nPCI_SLOT_NAME=0000:00:02.0\n");
        fake.WriteFile("sys/class/drm/card0/gt_act_freq_mhz", "1100\n");
        fake.WriteFile("sys/class/drm/card0/device/current_link_speed", "8.0 GT/s PCIe\n");
        fake.WriteFile("sys/class/drm/card0/device/current_link_width", "4\n");

        var runner = new FakeCommandRunner();
        var reader = new GpuReader(fake.Root, runner);

        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(1, gpus.Count);
        Gpu gpu = gpus[0];

        Assert.AreEqual("Intel Corporation", gpu.Manufacturer);
        Assert.AreEqual("0000:00:02.0", gpu.PciSlot);
        Assert.AreEqual(1100, gpu.CoreClockMhz);
        Assert.IsNull(gpu.MemoryClockMhz);
        Assert.IsNull(gpu.PowerW);
        Assert.IsNull(gpu.PowerCapW);
        Assert.AreEqual("PCIe 3.0 ×4", gpu.LinkCurrent);
    }

    [TestMethod]
    public void Nvidia_Read_ParsesExtendedColumns()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/drm/card0/device/uevent",
            "DRIVER=nvidia\nPCI_ID=10de:1d13\nPCI_SLOT_NAME=0000:01:00.0\n");
        fake.WriteFile("sys/class/drm/card0/device/power/runtime_status", "active\n");

        var runner = new FakeCommandRunner();
        // Extended 15-column output:
        // pci.bus_id, utilization.gpu, memory.total, memory.used, temperature.gpu,
        // clocks.gr, clocks.mem, power.draw, power.limit, utilization.encoder, utilization.decoder,
        // pcie.link.gen.current, pcie.link.gen.max, pcie.link.width.current, pcie.link.width.max
        runner.CommandOutputs["nvidia-smi"] =
            "00000000:01:00.0, 45, 2048, 512, 58, 1500, 3000, 65.5, 75.0, 12, 24, 3, 3, 4, 16\n";

        var reader = new GpuReader(fake.Root, runner);
        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(1, gpus.Count);
        Gpu gpu = gpus[0];

        Assert.AreEqual("NVIDIA Corporation", gpu.Manufacturer);
        Assert.AreEqual("0000:01:00.0", gpu.PciSlot);
        Assert.AreEqual(45, gpu.Usage);
        Assert.AreEqual(1500, gpu.CoreClockMhz);
        Assert.AreEqual(3000, gpu.MemoryClockMhz);
        Assert.AreEqual(65.5, gpu.PowerW.GetValueOrDefault(), 0.01);
        Assert.AreEqual(75.0, gpu.PowerCapW.GetValueOrDefault(), 0.01);
        Assert.AreEqual(12, gpu.EncoderUsage);
        Assert.AreEqual(24, gpu.DecoderUsage);
        Assert.AreEqual("PCIe 3.0 ×4", gpu.LinkCurrent);
        Assert.AreEqual("PCIe 3.0 ×16", gpu.LinkMax);
    }

    [TestMethod]
    public void Nvidia_Read_WhenColumnsAreNotApplicableOrNotSupported_SetsPropertiesNull()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/drm/card0/device/uevent",
            "DRIVER=nvidia\nPCI_ID=10de:1d13\nPCI_SLOT_NAME=0000:01:00.0\n");
        fake.WriteFile("sys/class/drm/card0/device/power/runtime_status", "active\n");

        var runner = new FakeCommandRunner();
        // Values with [N/A] and [Not Supported]
        runner.CommandOutputs["nvidia-smi"] =
            "00000000:01:00.0, 45, 2048, 512, 58, [N/A], [Not Supported], [N/A], [Not Supported], [N/A], [Not Supported], [N/A], 3, [N/A], 16\n";

        var reader = new GpuReader(fake.Root, runner);
        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(1, gpus.Count);
        Gpu gpu = gpus[0];

        Assert.IsNull(gpu.CoreClockMhz);
        Assert.IsNull(gpu.MemoryClockMhz);
        Assert.IsNull(gpu.PowerW);
        Assert.IsNull(gpu.PowerCapW);
        Assert.IsNull(gpu.EncoderUsage);
        Assert.IsNull(gpu.DecoderUsage);
        Assert.AreEqual(string.Empty, gpu.LinkCurrent);
        Assert.AreEqual("PCIe 3.0 ×16", gpu.LinkMax);
    }

    [TestMethod]
    public void Nvidia_Read_MultipleActiveCards_QueriesNvidiaSmiOnlyOncePerTick()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/drm/card0/device/uevent",
            "DRIVER=nvidia\nPCI_ID=10de:1d13\nPCI_SLOT_NAME=0000:01:00.0\n");
        fake.WriteFile("sys/class/drm/card0/device/power/runtime_status", "active\n");

        fake.WriteFile("sys/class/drm/card1/device/uevent",
            "DRIVER=nvidia\nPCI_ID=10de:1f95\nPCI_SLOT_NAME=0000:02:00.0\n");
        fake.WriteFile("sys/class/drm/card1/device/power/runtime_status", "active\n");

        var runner = new FakeCommandRunner();
        runner.CommandOutputs["nvidia-smi"] =
            "00000000:01:00.0, 40, 2048, 512, 55, 1200, 2000, 50.0, 60.0, 10, 20, 3, 3, 4, 8\n" +
            "00000000:02:00.0, 70, 4096, 2048, 70, 1600, 3200, 80.0, 100.0, 15, 30, 4, 4, 16, 16\n";

        var reader = new GpuReader(fake.Root, runner);
        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(2, gpus.Count);
        Assert.AreEqual(1, runner.Executions.Count);

        Assert.AreEqual(1200, gpus[0].CoreClockMhz);
        Assert.AreEqual(1600, gpus[1].CoreClockMhz);
    }

    [TestMethod]
    public void Nvidia_Read_AsleepCard_IsNotQueried()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/drm/card0/device/uevent",
            "DRIVER=nvidia\nPCI_ID=10de:1d13\nPCI_SLOT_NAME=0000:01:00.0\n");
        fake.WriteFile("sys/class/drm/card0/device/power/runtime_status", "suspended\n");

        var runner = new FakeCommandRunner();
        var reader = new GpuReader(fake.Root, runner);

        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(1, gpus.Count);
        Assert.AreEqual(0, runner.Executions.Count);
        Assert.IsNull(gpus[0].CoreClockMhz);
        Assert.IsNull(gpus[0].PowerW);
    }
}
