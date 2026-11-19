using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class GpuReaderTests
{
    [TestMethod]
    public void Read_WithNvidiaSuspended_DoesNotRunNvidiaSmi()
    {
        using var fake = new FakeRoot();

        // Setup card0 as nvidia with runtime_status = suspended
        fake.WriteFile("sys/class/drm/card0/device/uevent", "DRIVER=nvidia\nPCI_ID=10de:1d13\nPCI_SLOT_NAME=0000:01:00.0\n");
        fake.WriteFile("sys/class/drm/card0/device/power/runtime_status", "suspended\n");

        var runner = new FakeCommandRunner();
        var pci = PciDatabase.Parse("10de  NVIDIA Corporation\n\t1d13  GP108BM [GeForce MX250]\n");
        var reader = new GpuReader(fake.Root, runner, pci);

        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(1, gpus.Count);
        Gpu gpu = gpus[0];
        Assert.IsTrue(gpu.Present);
        // Phase 2b Amendment item 2: short vendor + bracketed product name.
        Assert.AreEqual("NVIDIA GeForce MX250", gpu.Name);
        Assert.AreEqual("nvidia", gpu.Driver);
        Assert.AreEqual(0, gpu.Usage);
        Assert.AreEqual(0.0, gpu.DedicatedGb);

        // Crucial test: nvidia-smi MUST NOT have been run!
        Assert.AreEqual(0, runner.Executions.Count);
    }

    [TestMethod]
    public void Read_WithNvidiaActive_ParsesNvidiaSmiOutput()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/drm/card0/device/uevent", "DRIVER=nvidia\nPCI_ID=10de:1d13\nPCI_SLOT_NAME=0000:01:00.0\n");
        fake.WriteFile("sys/class/drm/card0/device/power/runtime_status", "active\n");

        var runner = new FakeCommandRunner();
        // nvidia-smi returns: pci.bus_id, utilization.gpu, memory.total, memory.used, temperature.gpu
        // 45 % utilization, 2048 MB total, 512 MB used, 58 C
        runner.CommandOutputs["nvidia-smi"] = "0000:01:00.0, 45, 2048, 512, 58\n";

        var pci = PciDatabase.Parse("10de  NVIDIA Corporation\n\t1d13  GP108BM [GeForce MX250]\n");
        var reader = new GpuReader(fake.Root, runner, pci);

        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(1, gpus.Count);
        Gpu gpu = gpus[0];
        Assert.IsTrue(gpu.Present);
        Assert.AreEqual(45, gpu.Usage);
        Assert.AreEqual(0.5, gpu.DedicatedGb, 0.01); // 512 MB = 0.5 GB
        Assert.AreEqual(2.0, gpu.DedicatedTotalGb, 0.01); // 2048 MB = 2.0 GB
        Assert.AreEqual(58, gpu.Temp);

        Assert.AreEqual(1, runner.Executions.Count);
    }

    [TestMethod]
    public void Read_WithNvidiaUnsupported_RunsNvidiaSmi()
    {
        using var fake = new FakeRoot();

        // runtime_status = unsupported means no runtime PM; card is always on
        fake.WriteFile("sys/class/drm/card0/device/uevent", "DRIVER=nvidia\nPCI_ID=10de:1d13\nPCI_SLOT_NAME=0000:01:00.0\n");
        fake.WriteFile("sys/class/drm/card0/device/power/runtime_status", "unsupported\n");

        var runner = new FakeCommandRunner();
        runner.CommandOutputs["nvidia-smi"] = "0000:01:00.0, 50, 4096, 1024, 62\n";

        var reader = new GpuReader(fake.Root, runner);
        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(1, gpus.Count);
        Assert.AreEqual(1, runner.Executions.Count);
        Assert.AreEqual(50, gpus[0].Usage);
        Assert.AreEqual(62, gpus[0].Temp);
    }

    [TestMethod]
    public void Read_WithNvidiaRuntimeStatusMissing_DoesNotRunNvidiaSmi()
    {
        using var fake = new FakeRoot();

        // No power/runtime_status file created
        fake.WriteFile("sys/class/drm/card0/device/uevent", "DRIVER=nvidia\nPCI_ID=10de:1d13\nPCI_SLOT_NAME=0000:01:00.0\n");

        var runner = new FakeCommandRunner();
        var reader = new GpuReader(fake.Root, runner);

        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(1, gpus.Count);
        Assert.IsTrue(gpus[0].Present);
        Assert.AreEqual(0, gpus[0].Usage);
        Assert.IsNull(gpus[0].Temp);
        Assert.AreEqual(0, runner.Executions.Count);
    }

    [TestMethod]
    public void Read_WithTwoNvidiaCards_RunsNvidiaSmiOnce_AndMatchesByPciBusId()
    {
        using var fake = new FakeRoot();

        // Card 0: 0000:01:00.0
        fake.WriteFile("sys/class/drm/card0/device/uevent", "DRIVER=nvidia\nPCI_ID=10de:1d13\nPCI_SLOT_NAME=0000:01:00.0\n");
        fake.WriteFile("sys/class/drm/card0/device/power/runtime_status", "active\n");

        // Card 1: 0000:02:00.0
        fake.WriteFile("sys/class/drm/card1/device/uevent", "DRIVER=nvidia\nPCI_ID=10de:1f95\nPCI_SLOT_NAME=0000:02:00.0\n");
        fake.WriteFile("sys/class/drm/card1/device/power/runtime_status", "active\n");

        var runner = new FakeCommandRunner();
        // nvidia-smi returns 8-digit domain width (00000000:...) and uppercase/lowercase variants
        runner.CommandOutputs["nvidia-smi"] =
            "00000000:01:00.0, 35, 4096, 1024, 55\n" +
            "00000000:02:00.0, 80, 8192, 6144, 75\n";

        var reader = new GpuReader(fake.Root, runner);
        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(2, gpus.Count);

        // One call per refresh must serve both cards with their IDs in --id=
        Assert.AreEqual(1, runner.Executions.Count);
        Assert.IsTrue(runner.Executions[0].Args.Contains("00000000:01:00.0") && runner.Executions[0].Args.Contains("00000000:02:00.0"));

        // card0 matched to 01:00.0
        Gpu card0 = gpus[0];
        Assert.AreEqual(35, card0.Usage);
        Assert.AreEqual(1.0, card0.DedicatedGb, 0.01);
        Assert.AreEqual(4.0, card0.DedicatedTotalGb, 0.01);
        Assert.AreEqual(55, card0.Temp);

        // card1 matched to 02:00.0
        Gpu card1 = gpus[1];
        Assert.AreEqual(80, card1.Usage);
        Assert.AreEqual(6.0, card1.DedicatedGb, 0.01);
        Assert.AreEqual(8.0, card1.DedicatedTotalGb, 0.01);
        Assert.AreEqual(75, card1.Temp);
    }

    [TestMethod]
    public void Read_WithNvidiaCard_WhenPciBusIdDoesNotMatch_ReportsNoFigures()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/drm/card0/device/uevent", "DRIVER=nvidia\nPCI_ID=10de:1d13\nPCI_SLOT_NAME=0000:03:00.0\n");
        fake.WriteFile("sys/class/drm/card0/device/power/runtime_status", "active\n");

        var runner = new FakeCommandRunner();
        // nvidia-smi returns data for a different bus id (0000:99:00.0)
        runner.CommandOutputs["nvidia-smi"] = "00000000:99:00.0, 50, 4096, 2048, 60\n";

        var reader = new GpuReader(fake.Root, runner);
        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(1, gpus.Count);
        Gpu card0 = gpus[0];
        Assert.IsTrue(card0.Present);
        Assert.AreEqual(0, card0.Usage);
        Assert.IsNull(card0.Temp);
        Assert.AreEqual(0.0, card0.DedicatedGb);
    }

    [TestMethod]
    public void Read_WithOneActiveAndOneSuspendedNvidiaCard_CallsRunnerOnceWithOnlyActiveCardId()
    {
        using var fake = new FakeRoot();

        // Card 0: active
        fake.WriteFile("sys/class/drm/card0/device/uevent", "DRIVER=nvidia\nPCI_ID=10de:1d13\nPCI_SLOT_NAME=0000:01:00.0\n");
        fake.WriteFile("sys/class/drm/card0/device/power/runtime_status", "active\n");

        // Card 1: suspended
        fake.WriteFile("sys/class/drm/card1/device/uevent", "DRIVER=nvidia\nPCI_ID=10de:1f95\nPCI_SLOT_NAME=0000:02:00.0\n");
        fake.WriteFile("sys/class/drm/card1/device/power/runtime_status", "suspended\n");

        var runner = new FakeCommandRunner();
        // nvidia-smi output for only the active card
        runner.CommandOutputs["nvidia-smi"] = "00000000:01:00.0, 50, 4096, 2048, 65\n";

        var reader = new GpuReader(fake.Root, runner);
        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(2, gpus.Count);

        // Runner is called exactly once
        Assert.AreEqual(1, runner.Executions.Count);
        string args = runner.Executions[0].Args;

        // --id= names only the active card, not the suspended card
        Assert.IsTrue(args.Contains("--id=00000000:01:00.0") || args.Contains("--id=0000:01:00.0"));
        Assert.IsFalse(args.Contains("02:00.0"));

        // Active card (card0) gets parsed figures
        Gpu activeGpu = gpus[0];
        Assert.AreEqual(50, activeGpu.Usage);
        Assert.AreEqual(2.0, activeGpu.DedicatedGb, 0.01);
        Assert.AreEqual(4.0, activeGpu.DedicatedTotalGb, 0.01);
        Assert.AreEqual(65, activeGpu.Temp);

        // Suspended card (card1) gets no figures
        Gpu suspendedGpu = gpus[1];
        Assert.AreEqual(0, suspendedGpu.Usage);
        Assert.AreEqual(0.0, suspendedGpu.DedicatedGb);
        Assert.AreEqual(0.0, suspendedGpu.DedicatedTotalGb);
        Assert.IsNull(suspendedGpu.Temp);
    }

    [TestMethod]
    public void Read_WithNvidiaCard_MatchesPciWithHexLettersAndDifferentCaseAndDomainWidth()
    {
        using var fake = new FakeRoot();

        // sysfs uses 4-digit domain and lowercase hex letters: 0000:0a:00.0
        fake.WriteFile("sys/class/drm/card0/device/uevent", "DRIVER=nvidia\nPCI_ID=10de:1d13\nPCI_SLOT_NAME=0000:0a:00.0\n");
        fake.WriteFile("sys/class/drm/card0/device/power/runtime_status", "active\n");

        var runner = new FakeCommandRunner();
        // nvidia-smi returns 8-digit domain and uppercase hex letters: 00000000:0A:00.0
        runner.CommandOutputs["nvidia-smi"] = "00000000:0A:00.0, 75, 4096, 2048, 70\n";

        var reader = new GpuReader(fake.Root, runner);
        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(1, gpus.Count);
        Gpu gpu = gpus[0];
        Assert.IsTrue(gpu.Present);
        Assert.AreEqual(75, gpu.Usage);
        Assert.AreEqual(2.0, gpu.DedicatedGb, 0.01);
        Assert.AreEqual(4.0, gpu.DedicatedTotalGb, 0.01);
        Assert.AreEqual(70, gpu.Temp);

        // Direct normalization checks for hex letters, case, and domain width
        Assert.AreEqual(GpuReader.NormalisePci("0000:0a:00.0"), GpuReader.NormalisePci("00000000:0A:00.0"));
        Assert.AreEqual(GpuReader.NormalisePci("0000:00:1f.3"), GpuReader.NormalisePci("00000000:00:1F.3"));
    }

    [TestMethod]
    public void Read_WithI915Fdinfo_DeduplicatesByClientIdAndCalculatesEngineDeltas()
    {
        using var fake = new FakeRoot();

        // Card1 is Intel i915
        fake.WriteFile("sys/class/drm/card1/device/uevent", "DRIVER=i915\nPCI_ID=8086:8a56\nPCI_SLOT_NAME=0000:00:02.0\n");
        fake.WriteFile("sys/class/drm/card1/device/boot_vga", "1\n");

        // Two processes opening card1:
        // pid 100 has TWO fds for the SAME client id 42 (must be deduplicated!)
        fake.WriteFile("proc/100/fdinfo/3",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t42\n" +
            "drm-engine-render:\t1000000000 ns\n" +
            "drm-engine-copy:\t200000000 ns\n");
        fake.WriteFile("proc/100/fdinfo/4",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t42\n" +
            "drm-engine-render:\t1000000000 ns\n" +
            "drm-engine-copy:\t200000000 ns\n");

        // pid 200 has client id 99
        fake.WriteFile("proc/200/fdinfo/5",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t99\n" +
            "drm-engine-render:\t500000000 ns\n" +
            "drm-engine-copy:\t100000000 ns\n");

        var runner = new FakeCommandRunner();
        var reader = new GpuReader(fake.Root, runner);

        // First sample at t = 1000 ms: baseline reading, usage = 0
        IReadOnlyList<Gpu> sample1 = reader.Read(stamp: 1000);
        Assert.AreEqual(1, sample1.Count);
        Assert.AreEqual(0, sample1[0].Usage);

        // Advance 2000 ms (2 seconds later) at t = 3000 ms
        // Over 2 seconds, render engine increased by 1,000,000,000 ns across client 42 and 99
        // 1,000,000,000 ns / (2000 ms * 1,000,000 ns/ms) = 1,000,000,000 / 2,000,000,000 = 50%
        fake.WriteFile("proc/100/fdinfo/3",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t42\n" +
            "drm-engine-render:\t1600000000 ns\n" + // +600M ns
            "drm-engine-copy:\t200000000 ns\n");
        fake.WriteFile("proc/100/fdinfo/4",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t42\n" +
            "drm-engine-render:\t1600000000 ns\n" +
            "drm-engine-copy:\t200000000 ns\n");

        fake.WriteFile("proc/200/fdinfo/5",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t99\n" +
            "drm-engine-render:\t900000000 ns\n" + // +400M ns -> total delta render = 1000M ns
            "drm-engine-copy:\t100000000 ns\n");

        IReadOnlyList<Gpu> sample2 = reader.Read(stamp: 3000);
        Gpu intelGpu = sample2[0];
        Assert.AreEqual(50, intelGpu.Usage);

        GpuEngine renderEngine = intelGpu.Engines.First(e => e.Name == "3D"); // render maps to "3D"
        Assert.AreEqual(50, renderEngine.Usage);
    }

    [TestMethod]
    public void Read_WithMultipleCards_PutsBootVgaPrimaryFirst()
    {
        using var fake = new FakeRoot();

        // card0 is nvidia, boot_vga = 0
        fake.WriteFile("sys/class/drm/card0/device/uevent", "DRIVER=nvidia\nPCI_ID=10de:1d13\n");
        fake.WriteFile("sys/class/drm/card0/device/boot_vga", "0\n");
        fake.WriteFile("sys/class/drm/card0/device/power/runtime_status", "suspended\n");

        // card1 is intel i915, boot_vga = 1
        fake.WriteFile("sys/class/drm/card1/device/uevent", "DRIVER=i915\nPCI_ID=8086:8a56\n");
        fake.WriteFile("sys/class/drm/card1/device/boot_vga", "1\n");

        var runner = new FakeCommandRunner();
        var reader = new GpuReader(fake.Root, runner);

        IReadOnlyList<Gpu> gpus = reader.Read(stamp: 1000);

        Assert.AreEqual(2, gpus.Count);
        // Primary (boot_vga=1, i915) must be first!
        Assert.AreEqual("i915", gpus[0].Driver);
        Assert.AreEqual("nvidia", gpus[1].Driver);
    }

    [TestMethod]
    public void Read_WithI915Fdinfo_IntermediateCallInsideCacheWindow_ComputesUsageOverElapsedBetweenScans()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/drm/card0/device/uevent", "DRIVER=i915\nPCI_ID=8086:8a56\nPCI_SLOT_NAME=0000:00:02.0\n");
        fake.WriteFile("sys/class/drm/card0/device/boot_vga", "1\n");

        fake.WriteFile("proc/100/fdinfo/3",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t42\n" +
            "drm-engine-render:\t1000000000 ns\n");

        var runner = new FakeCommandRunner();
        var reader = new GpuReader(fake.Root, runner);

        // Call 1 at t = 0 s (stamp: 0 ms): baseline reading, usage = 0
        IReadOnlyList<Gpu> sample1 = reader.Read(stamp: 0);
        Assert.AreEqual(1, sample1.Count);
        Assert.AreEqual(0, sample1[0].Usage);

        // Counters grow, but Call 2 at t = 1 s (stamp: 1000 ms) falls inside the 2 s cache window
        fake.WriteFile("proc/100/fdinfo/3",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t42\n" +
            "drm-engine-render:\t1400000000 ns\n");

        IReadOnlyList<Gpu> sample2 = reader.Read(stamp: 1000);
        Assert.AreEqual(1, sample2.Count);
        Assert.AreEqual(0, sample2[0].Usage);

        // Counters continue growing by t = 2.5 s (stamp: 2500 ms)
        // Delta render = 1,000,000,000 ns over 2500 ms elapsed between scans (t=0 to t=2.5) -> 40% usage
        fake.WriteFile("proc/100/fdinfo/3",
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-client-id:\t42\n" +
            "drm-engine-render:\t2000000000 ns\n");

        // Call 3 at t = 2.5 s: cache expired (2500 ms - 0 ms >= 2000 ms).
        // Usage must be computed over the 2.5 s between the two scans actually taken, not 0 and not 1.5 s.
        IReadOnlyList<Gpu> sample3 = reader.Read(stamp: 2500);
        Assert.AreEqual(1, sample3.Count);
        Gpu gpu = sample3[0];

        GpuEngine renderEngine = gpu.Engines.First(e => e.Name == "3D");
        Assert.IsTrue(renderEngine.Usage > 0);
        Assert.AreEqual(40, renderEngine.Usage);
        Assert.AreEqual(40, gpu.Usage);
    }
}
