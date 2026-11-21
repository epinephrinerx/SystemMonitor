using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class HistoryBoardTests
{
    [TestMethod]
    public void GpuHistory_TwoSnapshotsWithGpusAAndB_ThenSnapshotWithOnlyB_RetainsOnlyBValuesAndPrunesA()
    {
        var history = new HistoryBoard();

        // GPU A: PCI slot 0000:00:02.0
        var gpuA1 = new Gpu
        {
            Present = true,
            Name = "Intel UHD Graphics",
            PciSlot = "0000:00:02.0",
            Usage = 20,
            EncoderUsage = 10,
            DecoderUsage = 15,
            DedicatedGb = 1.0,
            DedicatedTotalGb = 4.0,
            Temp = 45,
            Engines = new[]
            {
                new GpuEngine { Name = "3D", Usage = 25 },
                new GpuEngine { Name = "Video", Usage = 15 },
            },
        };

        // GPU B: PCI slot 0000:01:00.0
        var gpuB1 = new Gpu
        {
            Present = true,
            Name = "NVIDIA GeForce RTX",
            PciSlot = "0000:01:00.0",
            Usage = 50,
            EncoderUsage = 30,
            DecoderUsage = 35,
            DedicatedGb = 2.0,
            DedicatedTotalGb = 8.0,
            Temp = 65,
            Engines = new[]
            {
                new GpuEngine { Name = "3D", Usage = 55 },
            },
        };

        var initialGpus = new[] { gpuA1, gpuB1 };
        string keyA = PageKeys.Gpu(initialGpus, 0);
        string keyB = PageKeys.Gpu(initialGpus, 1);

        Assert.AreEqual("gpu:0000:00:02.0", keyA);
        Assert.AreEqual("gpu:0000:01:00.0", keyB);
        Assert.IsTrue(PageKeys.IsGpuKey(keyA));
        Assert.IsTrue(PageKeys.IsGpuKey(keyB));
        Assert.IsTrue(PageKeys.IsPageKey(keyA));
        Assert.IsTrue(PageKeys.IsPageKey(keyB));

        // Snapshot 1: both A and B
        var snapshot1 = new Snapshot
        {
            Ready = true,
            Gpus = initialGpus,
            Gpu = gpuA1,
        };
        history.Push(snapshot1);

        // Snapshot 2: both A and B with updated readings
        var gpuA2 = new Gpu
        {
            Present = true,
            Name = "Intel UHD Graphics",
            PciSlot = "0000:00:02.0",
            Usage = 22,
            EncoderUsage = 12,
            DecoderUsage = 18,
            DedicatedGb = 1.2,
            DedicatedTotalGb = 4.0,
            Temp = 48,
            Engines = new[]
            {
                new GpuEngine { Name = "3D", Usage = 28 },
                new GpuEngine { Name = "Video", Usage = 16 },
            },
        };
        var gpuB2 = new Gpu
        {
            Present = true,
            Name = "NVIDIA GeForce RTX",
            PciSlot = "0000:01:00.0",
            Usage = 54,
            EncoderUsage = 32,
            DecoderUsage = 38,
            DedicatedGb = 2.4,
            DedicatedTotalGb = 8.0,
            Temp = 68,
            Engines = new[]
            {
                new GpuEngine { Name = "3D", Usage = 58 },
            },
        };
        var snapshot2 = new Snapshot
        {
            Ready = true,
            Gpus = new[] { gpuA2, gpuB2 },
            Gpu = gpuA2,
        };
        history.Push(snapshot2);

        // Both have 2 samples
        Assert.AreEqual(2, history.GpuUsage(keyA).Count);
        Assert.AreEqual(2, history.GpuUsage(keyB).Count);
        Assert.AreEqual(22.0, history.GpuUsage(keyA).Latest);
        Assert.AreEqual(54.0, history.GpuUsage(keyB).Latest);

        // Snapshot 3: ONLY GPU B! GPU A has vanished.
        // Notice GPU B is now at index 0 of snapshot3.Gpus.
        var gpuB3 = new Gpu
        {
            Present = true,
            Name = "NVIDIA GeForce RTX",
            PciSlot = "0000:01:00.0",
            Usage = 60,
            EncoderUsage = 40,
            DecoderUsage = 45,
            DedicatedGb = 3.0,
            DedicatedTotalGb = 8.0,
            Temp = 72,
            Engines = new[]
            {
                new GpuEngine { Name = "3D", Usage = 65 },
            },
        };
        var snapshot3 = new Snapshot
        {
            Ready = true,
            Gpus = new[] { gpuB3 },
            Gpu = gpuB3,
        };

        // GPU B's page key at index 0 remains unchanged!
        string keyBAtZero = PageKeys.Gpu(snapshot3.Gpus, 0);
        Assert.AreEqual(keyB, keyBAtZero);
        Assert.AreEqual("gpu:0000:01:00.0", keyBAtZero);

        history.Push(snapshot3);

        // B's history holds only B's values (all 3 samples: 50, 54, 60)
        Assert.AreEqual(3, history.GpuUsage(keyB).Count);
        var bUsage = new double[3];
        history.GpuUsage(keyB).CopyTo(bUsage);
        CollectionAssert.AreEqual(new[] { 50.0, 54.0, 60.0 }, bUsage);

        Assert.AreEqual(3, history.GpuEncoder(keyB).Count);
        var bEncoder = new double[3];
        history.GpuEncoder(keyB).CopyTo(bEncoder);
        CollectionAssert.AreEqual(new[] { 30.0, 32.0, 40.0 }, bEncoder);

        Assert.AreEqual(3, history.GpuDecoder(keyB).Count);
        var bDecoder = new double[3];
        history.GpuDecoder(keyB).CopyTo(bDecoder);
        CollectionAssert.AreEqual(new[] { 35.0, 38.0, 45.0 }, bDecoder);

        Assert.AreEqual(3, history.GpuMemory(keyB).Count);
        var bMemory = new double[3];
        history.GpuMemory(keyB).CopyTo(bMemory);
        CollectionAssert.AreEqual(new[] { 25.0, 30.0, 37.5 }, bMemory);

        Assert.AreEqual(3, history.GpuTemp(keyB).Count);
        var bTemp = new double[3];
        history.GpuTemp(keyB).CopyTo(bTemp);
        CollectionAssert.AreEqual(new[] { 65.0, 68.0, 72.0 }, bTemp);

        Assert.AreEqual(3, history.Engine(keyB, "3D").Count);
        var bEngine3D = new double[3];
        history.Engine(keyB, "3D").CopyTo(bEngine3D);
        CollectionAssert.AreEqual(new[] { 55.0, 58.0, 65.0 }, bEngine3D);

        // A's series is gone! (Pruned from HistoryBoard; lazily queried history is empty)
        Assert.AreEqual(0, history.GpuUsage(keyA).Count);
        Assert.AreEqual(0, history.GpuEncoder(keyA).Count);
        Assert.AreEqual(0, history.GpuDecoder(keyA).Count);
        Assert.AreEqual(0, history.GpuMemory(keyA).Count);
        Assert.AreEqual(0, history.GpuTemp(keyA).Count);
        Assert.AreEqual(0, history.Engine(keyA, "3D").Count);
        Assert.AreEqual(0, history.Engine(keyA, "Video").Count);
    }

    [TestMethod]
    public void PageKeys_Gpu_WithoutPciSlot_FallsBackToDeviceIdOrNameOrdinal()
    {
        // GPU with no PciSlot but a DeviceId -> gpu:dev:<id>
        var devIdGpu = new Gpu
        {
            Present = true,
            Name = "Mali-G52",
            PciSlot = string.Empty,
            DeviceId = "fde60000.gpu",
        };
        string devKey = PageKeys.Gpu(new[] { devIdGpu }, 0);
        Assert.AreEqual("gpu:dev:fde60000.gpu", devKey);
        Assert.IsTrue(PageKeys.IsGpuKey(devKey));
        Assert.IsTrue(PageKeys.IsPageKey(devKey));

        // Neither PciSlot nor DeviceId -> gpu:name:<Name>#0, a second same-name GPU #1
        var nameGpu0 = new Gpu
        {
            Present = true,
            Name = "Platform GPU",
            PciSlot = string.Empty,
            DeviceId = string.Empty,
        };
        var nameGpu1 = new Gpu
        {
            Present = true,
            Name = "Platform GPU",
            PciSlot = string.Empty,
            DeviceId = string.Empty,
        };
        var otherNameGpu = new Gpu
        {
            Present = true,
            Name = "Another GPU",
            PciSlot = string.Empty,
            DeviceId = string.Empty,
        };

        var gpus = new[] { nameGpu0, nameGpu1, otherNameGpu };
        string key0 = PageKeys.Gpu(gpus, 0);
        string key1 = PageKeys.Gpu(gpus, 1);
        string keyOther = PageKeys.Gpu(gpus, 2);

        Assert.AreEqual("gpu:name:Platform GPU#0", key0);
        Assert.AreEqual("gpu:name:Platform GPU#1", key1);
        Assert.AreEqual("gpu:name:Another GPU#0", keyOther);

        Assert.IsTrue(PageKeys.IsGpuKey(key0));
        Assert.IsTrue(PageKeys.IsGpuKey(key1));
        Assert.IsTrue(PageKeys.IsGpuKey(keyOther));
        Assert.IsTrue(PageKeys.IsPageKey(key0));
        Assert.IsTrue(PageKeys.IsPageKey(key1));
        Assert.IsTrue(PageKeys.IsPageKey(keyOther));
    }

    [TestMethod]
    public void GpuHistory_SnapshotsPciAThenAddresslessB_ThenSnapshotWithOnlyB_RetainsBKeyAndHistory()
    {
        var history = new HistoryBoard();

        // GPU A: PCI device (PciSlot = 0000:00:02.0)
        var gpuA1 = new Gpu
        {
            Present = true,
            Name = "Intel UHD Graphics",
            PciSlot = "0000:00:02.0",
            Usage = 20,
        };

        // GPU B: Addressless device (no PciSlot, but has DeviceId)
        var gpuB1 = new Gpu
        {
            Present = true,
            Name = "Platform Mali",
            PciSlot = string.Empty,
            DeviceId = "fde60000.gpu",
            Usage = 40,
            EncoderUsage = 15,
            DecoderUsage = 20,
            DedicatedGb = 1.0,
            DedicatedTotalGb = 4.0,
            Temp = 50,
            Engines = new[]
            {
                new GpuEngine { Name = "3D", Usage = 35 },
            },
        };

        var initialGpus = new[] { gpuA1, gpuB1 };
        string keyA = PageKeys.Gpu(initialGpus, 0);
        string keyB = PageKeys.Gpu(initialGpus, 1);

        Assert.AreEqual("gpu:0000:00:02.0", keyA);
        Assert.AreEqual("gpu:dev:fde60000.gpu", keyB);

        // Snapshot 1: [PCI A, addressless B]
        var snapshot1 = new Snapshot
        {
            Ready = true,
            Gpus = initialGpus,
            Gpu = gpuA1,
        };
        history.Push(snapshot1);

        Assert.AreEqual(1, history.GpuUsage(keyA).Count);
        Assert.AreEqual(1, history.GpuUsage(keyB).Count);
        Assert.AreEqual(20.0, history.GpuUsage(keyA).Latest);
        Assert.AreEqual(40.0, history.GpuUsage(keyB).Latest);

        // Snapshot 2: [PCI A, addressless B] with updated values
        var gpuA2 = new Gpu
        {
            Present = true,
            Name = "Intel UHD Graphics",
            PciSlot = "0000:00:02.0",
            Usage = 25,
        };
        var gpuB2 = new Gpu
        {
            Present = true,
            Name = "Platform Mali",
            PciSlot = string.Empty,
            DeviceId = "fde60000.gpu",
            Usage = 55,
            EncoderUsage = 20,
            DecoderUsage = 25,
            DedicatedGb = 1.5,
            DedicatedTotalGb = 4.0,
            Temp = 55,
            Engines = new[]
            {
                new GpuEngine { Name = "3D", Usage = 45 },
            },
        };
        var snapshot2 = new Snapshot
        {
            Ready = true,
            Gpus = new[] { gpuA2, gpuB2 },
            Gpu = gpuA2,
        };
        history.Push(snapshot2);

        Assert.AreEqual(2, history.GpuUsage(keyB).Count);
        Assert.AreEqual(55.0, history.GpuUsage(keyB).Latest);

        // Snapshot 3: only [B]! GPU A is gone. B moves from index 1 to index 0.
        var gpuB3 = new Gpu
        {
            Present = true,
            Name = "Platform Mali",
            PciSlot = string.Empty,
            DeviceId = "fde60000.gpu",
            Usage = 70,
            EncoderUsage = 30,
            DecoderUsage = 35,
            DedicatedGb = 2.0,
            DedicatedTotalGb = 4.0,
            Temp = 60,
            Engines = new[]
            {
                new GpuEngine { Name = "3D", Usage = 65 },
            },
        };
        var snapshot3 = new Snapshot
        {
            Ready = true,
            Gpus = new[] { gpuB3 },
            Gpu = gpuB3,
        };

        // B's key at index 0 is unchanged!
        string keyBAtZero = PageKeys.Gpu(snapshot3.Gpus, 0);
        Assert.AreEqual(keyB, keyBAtZero);
        Assert.AreEqual("gpu:dev:fde60000.gpu", keyBAtZero);

        history.Push(snapshot3);

        // B's key and history unchanged (retains 40, 55, 70)
        Assert.AreEqual(3, history.GpuUsage(keyB).Count);
        var bUsage = new double[3];
        history.GpuUsage(keyB).CopyTo(bUsage);
        CollectionAssert.AreEqual(new[] { 40.0, 55.0, 70.0 }, bUsage);

        Assert.AreEqual(3, history.GpuEncoder(keyB).Count);
        Assert.AreEqual(3, history.GpuDecoder(keyB).Count);
        Assert.AreEqual(3, history.GpuMemory(keyB).Count);
        Assert.AreEqual(3, history.GpuTemp(keyB).Count);
        Assert.AreEqual(3, history.Engine(keyB, "3D").Count);

        // A's series is pruned/gone
        Assert.AreEqual(0, history.GpuUsage(keyA).Count);
    }

    [TestMethod]
    public void GpuHistory_SnapshotsPciAThenNamedAddresslessB_ThenSnapshotWithOnlyB_RetainsBKeyAndHistory()
    {
        var history = new HistoryBoard();

        var gpuA1 = new Gpu
        {
            Present = true,
            Name = "Intel UHD Graphics",
            PciSlot = "0000:00:02.0",
            Usage = 10,
        };
        var gpuB1 = new Gpu
        {
            Present = true,
            Name = "Rockchip VPU",
            PciSlot = string.Empty,
            DeviceId = string.Empty,
            Usage = 30,
        };

        var gpus1 = new[] { gpuA1, gpuB1 };
        string keyA = PageKeys.Gpu(gpus1, 0);
        string keyB = PageKeys.Gpu(gpus1, 1);

        Assert.AreEqual("gpu:0000:00:02.0", keyA);
        Assert.AreEqual("gpu:name:Rockchip VPU#0", keyB);

        history.Push(new Snapshot { Ready = true, Gpus = gpus1, Gpu = gpuA1 });

        // Snapshot 2: both [A, B]
        var gpuA2 = new Gpu { Present = true, Name = "Intel UHD Graphics", PciSlot = "0000:00:02.0", Usage = 15 };
        var gpuB2 = new Gpu { Present = true, Name = "Rockchip VPU", PciSlot = string.Empty, DeviceId = string.Empty, Usage = 35 };
        history.Push(new Snapshot { Ready = true, Gpus = new[] { gpuA2, gpuB2 }, Gpu = gpuA2 });

        // Snapshot 3: only [B]
        var gpuB3 = new Gpu { Present = true, Name = "Rockchip VPU", PciSlot = string.Empty, DeviceId = string.Empty, Usage = 50 };
        var gpus3 = new[] { gpuB3 };
        string keyBAtZero = PageKeys.Gpu(gpus3, 0);

        Assert.AreEqual(keyB, keyBAtZero);
        Assert.AreEqual("gpu:name:Rockchip VPU#0", keyBAtZero);

        history.Push(new Snapshot { Ready = true, Gpus = gpus3, Gpu = gpuB3 });

        Assert.AreEqual(3, history.GpuUsage(keyB).Count);
        var bUsage = new double[3];
        history.GpuUsage(keyB).CopyTo(bUsage);
        CollectionAssert.AreEqual(new[] { 30.0, 35.0, 50.0 }, bUsage);

        Assert.AreEqual(0, history.GpuUsage(keyA).Count);
    }

    [TestMethod]
    public void PageKeys_IsGpuKey_And_IsPageKey_Rules()
    {
        Assert.IsTrue(PageKeys.IsGpuKey("gpu0"));
        Assert.IsTrue(PageKeys.IsGpuKey("gpu12"));
        Assert.IsTrue(PageKeys.IsGpuKey("gpu:0000:00:02.0"));
        Assert.IsTrue(PageKeys.IsGpuKey("gpu:dev:fde60000.gpu"));
        Assert.IsTrue(PageKeys.IsGpuKey("gpu:name:Mali-G52#0"));
        Assert.IsFalse(PageKeys.IsGpuKey("gpu"));
        Assert.IsFalse(PageKeys.IsGpuKey("gpu:"));
        Assert.IsFalse(PageKeys.IsGpuKey("gpux"));
        Assert.IsFalse(PageKeys.IsGpuKey(null));

        Assert.IsTrue(PageKeys.IsPageKey(PageKeys.DiskVirtual));
        Assert.IsTrue(PageKeys.IsPageKey(PageKeys.DiskNetwork));
        Assert.IsTrue(PageKeys.IsPageKey("gpu:0000:01:00.0"));
        Assert.IsTrue(PageKeys.IsPageKey("gpu:dev:fde60000.gpu"));
        Assert.IsTrue(PageKeys.IsPageKey("gpu:name:Mali-G52#0"));
        Assert.IsTrue(PageKeys.IsPageKey("gpu0"));
        Assert.IsTrue(PageKeys.IsPageKey("disk:nvme0n1"));
        Assert.IsTrue(PageKeys.IsPageKey("neteth0"));
        Assert.IsTrue(PageKeys.IsPageKey("battery:BAT0"));
        Assert.IsFalse(PageKeys.IsPageKey(""));
        Assert.IsFalse(PageKeys.IsPageKey(null));
        Assert.IsFalse(PageKeys.IsPageKey("invalid_page"));
    }
}
