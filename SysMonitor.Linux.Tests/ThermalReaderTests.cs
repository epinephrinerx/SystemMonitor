using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class ThermalReaderTests
{
    [TestMethod]
    public void Read_WithCoretemp_MapsPackageTempAndPerCoreTemperaturesViaCoreId()
    {
        using var fake = new FakeRoot();

        // hwmon0 is coretemp
        fake.WriteFile("sys/class/hwmon/hwmon0/name", "coretemp\n");
        // Package temp: 52000 mC = 52 C
        fake.WriteFile("sys/class/hwmon/hwmon0/temp1_input", "52000\n");
        fake.WriteFile("sys/class/hwmon/hwmon0/temp1_label", "Package id 0\n");

        // Physical Core 0 temp: 48000 mC = 48 C
        fake.WriteFile("sys/class/hwmon/hwmon0/temp2_input", "48000\n");
        fake.WriteFile("sys/class/hwmon/hwmon0/temp2_label", "Core 0\n");

        // Physical Core 1 temp: 50000 mC = 50 C
        fake.WriteFile("sys/class/hwmon/hwmon0/temp3_input", "50000\n");
        fake.WriteFile("sys/class/hwmon/hwmon0/temp3_label", "Core 1\n");

        // Setup 4 logical CPUs:
        // cpu0 -> core_id 0
        // cpu1 -> core_id 1
        // cpu2 -> core_id 0 (HT sibling of cpu0)
        // cpu3 -> core_id 1 (HT sibling of cpu1)
        fake.WriteFile("sys/devices/system/cpu/cpu0/topology/core_id", "0\n");
        fake.WriteFile("sys/devices/system/cpu/cpu1/topology/core_id", "1\n");
        fake.WriteFile("sys/devices/system/cpu/cpu2/topology/core_id", "0\n");
        fake.WriteFile("sys/devices/system/cpu/cpu3/topology/core_id", "1\n");

        CpuThermalReading reading = ThermalReader.Read(fake.Root, logicalCount: 4);

        Assert.AreEqual(52, reading.CpuTemp);
        Assert.IsFalse(reading.Estimated);
        Assert.AreEqual(4, reading.CoreTemps.Count);
        Assert.AreEqual(48, reading.CoreTemps[0]); // Core 0
        Assert.AreEqual(50, reading.CoreTemps[1]); // Core 1
        Assert.AreEqual(48, reading.CoreTemps[2]); // Core 0 (HT sibling)
        Assert.AreEqual(50, reading.CoreTemps[3]); // Core 1 (HT sibling)
    }

    [TestMethod]
    public void Read_WithAmdK10Temp_PicksTctlAndLeavesCoreTempsEmpty()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/hwmon/hwmon0/name", "k10temp\n");
        fake.WriteFile("sys/class/hwmon/hwmon0/temp1_input", "61500\n");
        fake.WriteFile("sys/class/hwmon/hwmon0/temp1_label", "Tctl\n");

        CpuThermalReading reading = ThermalReader.Read(fake.Root, logicalCount: 8);

        Assert.AreEqual(62, reading.CpuTemp);
        Assert.IsFalse(reading.Estimated);
        Assert.AreEqual(8, reading.CoreTemps.Count);
        Assert.IsTrue(reading.CoreTemps.All(t => t == null));
    }

    [TestMethod]
    public void Read_WithThermalZoneX86PkgTemp_SetsCpuTemp()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/thermal/thermal_zone0/type", "x86_pkg_temp\n");
        fake.WriteFile("sys/class/thermal/thermal_zone0/temp", "55000\n");

        CpuThermalReading reading = ThermalReader.Read(fake.Root, logicalCount: 4);

        Assert.AreEqual(55, reading.CpuTemp);
        Assert.IsFalse(reading.Estimated);
    }

    [TestMethod]
    public void Read_WithAcpitzFallback_SetsEstimatedTrue()
    {
        using var fake = new FakeRoot();

        // No coretemp, no k10temp, no x86_pkg_temp
        fake.WriteFile("sys/class/hwmon/hwmon0/name", "acpitz\n");
        fake.WriteFile("sys/class/hwmon/hwmon0/temp1_input", "45000\n");

        CpuThermalReading reading = ThermalReader.Read(fake.Root, logicalCount: 4);

        Assert.AreEqual(45, reading.CpuTemp);
        Assert.IsTrue(reading.Estimated);
    }

    [TestMethod]
    public void Read_WithNoSensors_ReturnsNullAndEstimatedFalse()
    {
        using var fake = new FakeRoot();

        CpuThermalReading reading = ThermalReader.Read(fake.Root, logicalCount: 2);

        Assert.IsNull(reading.CpuTemp);
        Assert.IsFalse(reading.Estimated);
        Assert.AreEqual(2, reading.CoreTemps.Count);
    }
}
