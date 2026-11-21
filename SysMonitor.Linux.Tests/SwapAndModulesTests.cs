using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class SwapAndModulesTests
{
    // --- Swap tests ---

    [TestMethod]
    public void MemInfo_Parse_ComputesSwapTotalUsedAndUsage()
    {
        string meminfo =
            "MemTotal:        16384000 kB\n" +
            "MemFree:          8192000 kB\n" +
            "MemAvailable:    12288000 kB\n" +
            "SwapTotal:        8388608 kB\n" +
            "SwapFree:         4194304 kB\n";

        Ram ram = MemInfo.Parse(meminfo);

        Assert.AreEqual(8388608L * 1024, ram.SwapTotalBytes);
        Assert.AreEqual((8388608L - 4194304L) * 1024, ram.SwapUsedBytes);
        Assert.AreEqual(50, ram.SwapUsage);
    }

    [TestMethod]
    public void MemInfo_Parse_WhenSwapTotalZero_ReturnsZeroUsage()
    {
        string meminfo =
            "MemTotal:        16384000 kB\n" +
            "MemFree:          8192000 kB\n" +
            "SwapTotal:              0 kB\n" +
            "SwapFree:               0 kB\n";

        Ram ram = MemInfo.Parse(meminfo);

        Assert.AreEqual(0L, ram.SwapTotalBytes);
        Assert.AreEqual(0L, ram.SwapUsedBytes);
        Assert.AreEqual(0, ram.SwapUsage);
    }

    [TestMethod]
    public void SwapsReader_Parse_HandlesHeaderFilesPartitionsAndOctalSpaces()
    {
        string swaps =
            "Filename\t\t\t\tType\t\tSize\t\tUsed\t\tPriority\n" +
            "/swapfile                               file\t\t8388604\t\t1048576\t\t-2\n" +
            "/dev/nvme0n1p3                          partition\t4194300\t\t0\t\t-1\n" +
            "/swap\\040drive/file                     file\t\t2097148\t\t524288\t\t-3\n";

        IReadOnlyList<SwapDevice> devices = SwapsReader.Parse(swaps);

        Assert.AreEqual(3, devices.Count);

        // 1. /swapfile
        Assert.AreEqual("/swapfile", devices[0].Name);
        Assert.AreEqual("file", devices[0].Type);
        Assert.AreEqual(8388604L * 1024, devices[0].SizeBytes);
        Assert.AreEqual(1048576L * 1024, devices[0].UsedBytes);
        Assert.AreEqual(-2, devices[0].Priority);

        // 2. /dev/nvme0n1p3 partition
        Assert.AreEqual("/dev/nvme0n1p3", devices[1].Name);
        Assert.AreEqual("partition", devices[1].Type);
        Assert.AreEqual(4194300L * 1024, devices[1].SizeBytes);
        Assert.AreEqual(0L, devices[1].UsedBytes);
        Assert.AreEqual(-1, devices[1].Priority);

        // 3. /swap\040drive/file -> octal \040 decoded to space
        Assert.AreEqual("/swap drive/file", devices[2].Name);
        Assert.AreEqual("file", devices[2].Type);
        Assert.AreEqual(2097148L * 1024, devices[2].SizeBytes);
        Assert.AreEqual(524288L * 1024, devices[2].UsedBytes);
        Assert.AreEqual(-3, devices[2].Priority);
    }

    // --- Memory modules tests ---

    private const string HandoffFixture =
        "E:MEMORY_ARRAY_NUM_DEVICES=2\n" +
        "E:MEMORY_ARRAY_MAX_CAPACITY=34359738368\n" +
        "E:MEMORY_DEVICE_0_LOCATOR=DIMM 0\n" +
        "E:MEMORY_DEVICE_0_SIZE=4294967296\n" +
        "E:MEMORY_DEVICE_0_TYPE=LPDDR4\n" +
        "E:MEMORY_DEVICE_0_TYPE_DETAIL=Synchronous\n" +
        "E:MEMORY_DEVICE_0_FORM_FACTOR=Row Of Chips\n" +
        "E:MEMORY_DEVICE_0_SPEED_MTS=4267\n" +
        "E:MEMORY_DEVICE_0_CONFIGURED_SPEED_MTS=2667\n" +
        "E:MEMORY_DEVICE_0_MANUFACTURER=Micron\n" +
        "E:MEMORY_DEVICE_0_SERIAL_NUMBER=SECRET123\n" +
        "E:MEMORY_DEVICE_0_ASSET_TAG=TAG999\n" +
        "E:MEMORY_DEVICE_0_PART_NUMBER=PARTXYZ\n" +
        "E:MEMORY_DEVICE_0_RANK=2\n" +
        "E:MEMORY_DEVICE_0_CONFIGURED_VOLTAGE=600\n" +
        "E:MEMORY_DEVICE_1_LOCATOR=DIMM 1\n" +
        "E:MEMORY_DEVICE_1_SIZE=4294967296\n" +
        "E:MEMORY_DEVICE_1_TYPE=LPDDR4\n" +
        "E:MEMORY_DEVICE_1_TYPE_DETAIL=Synchronous\n" +
        "E:MEMORY_DEVICE_1_FORM_FACTOR=Row Of Chips\n" +
        "E:MEMORY_DEVICE_1_SPEED_MTS=4267\n" +
        "E:MEMORY_DEVICE_1_CONFIGURED_SPEED_MTS=2667\n" +
        "E:MEMORY_DEVICE_1_MANUFACTURER=Micron\n" +
        "E:MEMORY_DEVICE_1_SERIAL_NUMBER=SECRET456\n" +
        "E:MEMORY_DEVICE_1_ASSET_TAG=TAG888\n" +
        "E:MEMORY_DEVICE_1_PART_NUMBER=PARTABC\n" +
        "E:MEMORY_DEVICE_1_RANK=2\n" +
        "E:MEMORY_DEVICE_1_CONFIGURED_VOLTAGE=600\n";

    [TestMethod]
    public void MemoryModules_Parse_RealKeysFixture_ExtractsAllProperties()
    {
        MemoryLayout layout = MemoryModules.Parse(HandoffFixture);

        Assert.AreEqual(2, layout.SlotsUsed);
        Assert.AreEqual(2, layout.SlotsTotal);
        Assert.AreEqual(34359738368L, layout.MaxCapacityBytes);
        Assert.AreEqual(2, layout.Modules.Count);

        MemoryModule mod0 = layout.Modules[0];
        Assert.AreEqual("DIMM 0", mod0.Slot);
        Assert.AreEqual(4294967296L, mod0.SizeBytes);
        Assert.AreEqual("LPDDR4", mod0.Type);
        Assert.AreEqual("Synchronous", mod0.TypeDetail);
        Assert.AreEqual("Row Of Chips", mod0.FormFactor);
        Assert.AreEqual(4267, mod0.SpeedMts);
        Assert.AreEqual(2667, mod0.ConfiguredSpeedMts);
        Assert.AreEqual("Micron", mod0.Manufacturer);
        Assert.AreEqual(2, mod0.Rank);
        Assert.AreEqual(0.6, mod0.ConfiguredVoltage!.Value, 0.001);

        MemoryModule mod1 = layout.Modules[1];
        Assert.AreEqual("DIMM 1", mod1.Slot);
        Assert.AreEqual(4294967296L, mod1.SizeBytes);
        Assert.AreEqual(0.6, mod1.ConfiguredVoltage!.Value, 0.001);
    }

    [TestMethod]
    public void MemoryModules_Parse_SlotWithSizeZero_IsNotListed()
    {
        string text =
            "E:MEMORY_ARRAY_NUM_DEVICES=2\n" +
            "E:MEMORY_DEVICE_0_LOCATOR=DIMM 0\n" +
            "E:MEMORY_DEVICE_0_SIZE=4294967296\n" +
            "E:MEMORY_DEVICE_1_LOCATOR=DIMM 1\n" +
            "E:MEMORY_DEVICE_1_SIZE=0\n";

        MemoryLayout layout = MemoryModules.Parse(text);

        Assert.AreEqual(1, layout.SlotsUsed);
        Assert.AreEqual(2, layout.SlotsTotal);
        Assert.AreEqual(1, layout.Modules.Count);
        Assert.AreEqual("DIMM 0", layout.Modules[0].Slot);
    }

    [TestMethod]
    public void MemoryModules_Parse_UnknownAndNotSpecified_AreEmptyOrNull()
    {
        string text =
            "E:MEMORY_ARRAY_NUM_DEVICES=1\n" +
            "E:MEMORY_DEVICE_0_LOCATOR=DIMM 0\n" +
            "E:MEMORY_DEVICE_0_SIZE=4294967296\n" +
            "E:MEMORY_DEVICE_0_TYPE=Unknown\n" +
            "E:MEMORY_DEVICE_0_MANUFACTURER=Not Specified\n" +
            "E:MEMORY_DEVICE_0_FORM_FACTOR=Unknown\n";

        MemoryLayout layout = MemoryModules.Parse(text);

        Assert.AreEqual(1, layout.Modules.Count);
        MemoryModule mod = layout.Modules[0];
        Assert.AreEqual(string.Empty, mod.Type);
        Assert.AreEqual(string.Empty, mod.Manufacturer);
        Assert.AreEqual(string.Empty, mod.FormFactor);
    }

    [TestMethod]
    public void MemoryModules_Parse_MissingOrEmptyFile_ReturnsNullsAndEmptyList()
    {
        MemoryLayout layoutNull = MemoryModules.Parse(null);
        Assert.AreEqual(0, layoutNull.Modules.Count);
        Assert.IsNull(layoutNull.SlotsUsed);
        Assert.IsNull(layoutNull.SlotsTotal);
        Assert.IsNull(layoutNull.MaxCapacityBytes);

        MemoryLayout layoutEmpty = MemoryModules.Parse("");
        Assert.AreEqual(0, layoutEmpty.Modules.Count);
        Assert.IsNull(layoutEmpty.SlotsUsed);
        Assert.IsNull(layoutEmpty.SlotsTotal);
        Assert.IsNull(layoutEmpty.MaxCapacityBytes);
    }

    [TestMethod]
    public void MemoryModules_Parse_SerialAssetTagAndPartNumber_NeverAppearInOutput()
    {
        MemoryLayout layout = MemoryModules.Parse(HandoffFixture);

        // Serialize the entire model to JSON and ToString to verify no secrets leak
        string json = JsonSerializer.Serialize(layout);
        string repr = layout.ToString() ?? string.Empty;

        Assert.IsFalse(json.Contains("SECRET123"), "Serial number must not appear in serialized output");
        Assert.IsFalse(json.Contains("TAG999"), "Asset tag must not appear in serialized output");
        Assert.IsFalse(json.Contains("PARTXYZ"), "Part number must not appear in serialized output");

        Assert.IsFalse(repr.Contains("SECRET123"), "Serial number must not appear in ToString");
        Assert.IsFalse(repr.Contains("TAG999"), "Asset tag must not appear in ToString");
        Assert.IsFalse(repr.Contains("PARTXYZ"), "Part number must not appear in ToString");
    }

    [TestMethod]
    public void MemoryModules_Parse_ConfiguredVoltageOutsidePlausibleRange_ReturnsNull()
    {
        // Plausible range is 500 mV to 5000 mV (0.5 V to 5.0 V)
        // 600 mV -> 0.6 V
        // 1 mV -> null (hardware placeholder)
        // 6000 mV -> null
        string text =
            "E:MEMORY_ARRAY_NUM_DEVICES=3\n" +
            "E:MEMORY_DEVICE_0_LOCATOR=DIMM 0\n" +
            "E:MEMORY_DEVICE_0_SIZE=4294967296\n" +
            "E:MEMORY_DEVICE_0_CONFIGURED_VOLTAGE=600\n" +
            "E:MEMORY_DEVICE_1_LOCATOR=DIMM 1\n" +
            "E:MEMORY_DEVICE_1_SIZE=4294967296\n" +
            "E:MEMORY_DEVICE_1_CONFIGURED_VOLTAGE=1\n" +
            "E:MEMORY_DEVICE_2_LOCATOR=DIMM 2\n" +
            "E:MEMORY_DEVICE_2_SIZE=4294967296\n" +
            "E:MEMORY_DEVICE_2_CONFIGURED_VOLTAGE=6000\n";

        MemoryLayout layout = MemoryModules.Parse(text);

        Assert.AreEqual(3, layout.Modules.Count);
        Assert.AreEqual(0.6, layout.Modules[0].ConfiguredVoltage!.Value, 0.001);
        Assert.IsNull(layout.Modules[1].ConfiguredVoltage, "Voltage of 1 mV is out of 500-5000 mV range and must be null");
        Assert.IsNull(layout.Modules[2].ConfiguredVoltage, "Voltage of 6000 mV is out of 500-5000 mV range and must be null");
    }
}
