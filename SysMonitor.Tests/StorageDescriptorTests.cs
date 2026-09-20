using SysMonitor.Native;

namespace SysMonitor.Tests;

/// <summary>
/// The byte layouts. Every offset here was wrong at some point; the
/// temperature one shipped reading a reserved byte and reported 0 for a
/// drive at 55 C.
/// </summary>
[TestClass]
public class StorageDescriptorTests
{
    /// <summary>
    /// STORAGE_TEMPERATURE_DATA_DESCRIPTOR with one reading at 55 C:
    /// Version 0, Size 4, InfoCount 12, Reserved1 16..24, info from 24.
    /// </summary>
    private static byte[] TemperatureDescriptor(short celsius, int infoCount = 1,
                                                uint? declaredSize = null)
    {
        int size = 24 + infoCount * 16;
        var raw = new byte[size];
        BitConverter.GetBytes(1u).CopyTo(raw, 0);                       // Version
        BitConverter.GetBytes(declaredSize ?? (uint)size).CopyTo(raw, 4);
        BitConverter.GetBytes((ushort)infoCount).CopyTo(raw, 12);
        // STORAGE_TEMPERATURE_INFO: Index 0, Temperature +2.
        BitConverter.GetBytes(celsius).CopyTo(raw, 26);
        return raw;
    }

    [TestMethod]
    public void Temperature_reads_the_first_sensor()
    {
        Assert.AreEqual(55, StorageDescriptors.Temperature(TemperatureDescriptor(55)));
    }

    [TestMethod]
    public void Temperature_does_not_read_the_reserved_byte()
    {
        // Byte 18 is inside Reserved1. A parser looking there sees zero even
        // though the descriptor plainly carries 55.
        byte[] raw = TemperatureDescriptor(55);
        Assert.AreEqual(0, BitConverter.ToInt16(raw, 18));
        Assert.AreEqual(55, StorageDescriptors.Temperature(raw));
    }

    [TestMethod]
    public void Temperature_rejects_a_short_buffer()
    {
        Assert.IsNull(StorageDescriptors.Temperature(new byte[39]));
    }

    [TestMethod]
    public void Temperature_rejects_a_count_the_buffer_cannot_hold()
    {
        // Four readings claimed, one buffer's worth of bytes returned.
        byte[] raw = TemperatureDescriptor(55);
        BitConverter.GetBytes((ushort)4).CopyTo(raw, 12);
        Assert.IsNull(StorageDescriptors.Temperature(raw));
    }

    [TestMethod]
    public void Temperature_rejects_a_count_the_declared_size_cannot_hold()
    {
        // The buffer is long enough but the device says the data is not.
        byte[] raw = TemperatureDescriptor(55, infoCount: 2, declaredSize: 30);
        Assert.IsNull(StorageDescriptors.Temperature(raw));
    }

    [TestMethod]
    public void Temperature_rejects_impossible_readings()
    {
        Assert.IsNull(StorageDescriptors.Temperature(TemperatureDescriptor(-60)));
        Assert.IsNull(StorageDescriptors.Temperature(TemperatureDescriptor(200)));
    }

    [TestMethod]
    public void Media_type_comes_from_the_seek_penalty_flag()
    {
        var ssd = new byte[9];
        var hdd = new byte[9];
        hdd[8] = 1;
        Assert.AreEqual("SSD", StorageDescriptors.MediaType(ssd));
        Assert.AreEqual("HDD", StorageDescriptors.MediaType(hdd));
        Assert.IsNull(StorageDescriptors.MediaType(new byte[8]));
    }

    [TestMethod]
    public void Bus_type_is_read_at_offset_24()
    {
        var raw = new byte[25];
        raw[24] = 17;                                   // BusTypeNvme
        Assert.AreEqual("NVMe", StorageDescriptors.BusType(raw));

        raw[24] = 11;                                   // BusTypeSata
        Assert.AreEqual("SATA", StorageDescriptors.BusType(raw));

        raw[24] = 200;                                  // outside the table
        Assert.AreEqual("Unknown", StorageDescriptors.BusType(raw));
    }

    [TestMethod]
    public void Device_number_rejects_a_volume_without_one()
    {
        var raw = new byte[8];
        BitConverter.GetBytes(3).CopyTo(raw, 4);
        Assert.AreEqual(3, StorageDescriptors.DeviceNumber(raw));

        BitConverter.GetBytes(-1).CopyTo(raw, 4);
        Assert.IsNull(StorageDescriptors.DeviceNumber(raw));
    }

    [TestMethod]
    public void Io_counters_read_both_longs()
    {
        var raw = new byte[16];
        BitConverter.GetBytes(1234L).CopyTo(raw, 0);
        BitConverter.GetBytes(5678L).CopyTo(raw, 8);
        Assert.AreEqual((1234L, 5678L), StorageDescriptors.IoCounters(raw));
        Assert.IsNull(StorageDescriptors.IoCounters(new byte[15]));
    }
}
