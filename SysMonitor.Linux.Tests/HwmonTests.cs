using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class HwmonTests
{
    [TestMethod]
    public void Enumerate_ReadsAllInputsAndLabels()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/hwmon/hwmon1/name", "nvme\n");
        fake.WriteFile("sys/class/hwmon/hwmon1/temp1_input", "42000\n");
        fake.WriteFile("sys/class/hwmon/hwmon1/temp1_label", "Composite\n");
        fake.WriteFile("sys/class/hwmon/hwmon1/temp2_input", "45000\n");
        fake.WriteFile("sys/class/hwmon/hwmon1/temp2_label", "Sensor 1\n");

        IReadOnlyList<HwmonSensor> sensors = Hwmon.Enumerate(fake.Root);

        Assert.AreEqual(1, sensors.Count);
        Assert.AreEqual("nvme", sensors[0].Name);
        Assert.AreEqual(2, sensors[0].Temps.Count);
        Assert.AreEqual("Composite", sensors[0].Temps[0].Label);
        Assert.AreEqual(42, sensors[0].Temps[0].Celsius);
        Assert.AreEqual("Sensor 1", sensors[0].Temps[1].Label);
        Assert.AreEqual(45, sensors[0].Temps[1].Celsius);
    }

    [TestMethod]
    public void Pick_RespectsLabelPreference()
    {
        var sensor = new HwmonSensor(
            Name: "k10temp",
            Path: "/fake/path",
            Temps: new List<HwmonTemp>
            {
                new("Tedge", 40),
                new("Tctl", 65),
                new("Tdie", 60),
            });

        int? picked = Hwmon.Pick(sensor, "Tctl", "Tdie");
        Assert.AreEqual(65, picked);

        int? pickedTdie = Hwmon.Pick(sensor, "Tdie");
        Assert.AreEqual(60, pickedTdie);

        int? fallback = Hwmon.Pick(sensor, "NonExistentLabel");
        Assert.AreEqual(40, fallback); // First non-null temp
    }

    [TestMethod]
    public void DeviceTemperature_FindsDirectHwmonOrNestedHwmon()
    {
        using var fake = new FakeRoot();

        // Direct hwmon under device: device/hwmon0/temp1_input (PCI / NVMe layout)
        string nvmeDevice = fake.CreateDirectory("sys/devices/pci0000:00/0000:00:0e.0/nvme/nvme0/device");
        fake.WriteFile("sys/devices/pci0000:00/0000:00:0e.0/nvme/nvme0/device/hwmon0/temp1_input", "39000\n");

        int? nvmeTemp = Hwmon.DeviceTemperature(nvmeDevice);
        Assert.AreEqual(39, nvmeTemp);

        // Nested hwmon under device: device/hwmon/hwmon1/temp1_input (SCSI / drivetemp layout)
        string scsiDevice = fake.CreateDirectory("sys/devices/pci0000:00/ata1/host0/target0:0:0/0:0:0:0/device");
        fake.WriteFile("sys/devices/pci0000:00/ata1/host0/target0:0:0/0:0:0:0/device/hwmon/hwmon1/temp1_input", "34000\n");

        int? scsiTemp = Hwmon.DeviceTemperature(scsiDevice);
        Assert.AreEqual(34, scsiTemp);
    }

    [TestMethod]
    public void CoreTemps_WithMultipleSockets_MapsTemperaturesBySocketAndCoreId()
    {
        using var fake = new FakeRoot();

        // Socket 0 coretemp chip
        fake.WriteFile("sys/class/hwmon/hwmon0/name", "coretemp\n");
        fake.WriteFile("sys/class/hwmon/hwmon0/temp1_label", "Package id 0\n");
        fake.WriteFile("sys/class/hwmon/hwmon0/temp1_input", "50000\n");
        fake.WriteFile("sys/class/hwmon/hwmon0/temp2_label", "Core 0\n");
        fake.WriteFile("sys/class/hwmon/hwmon0/temp2_input", "45000\n");
        fake.WriteFile("sys/class/hwmon/hwmon0/temp3_label", "Core 1\n");
        fake.WriteFile("sys/class/hwmon/hwmon0/temp3_input", "47000\n");

        // Socket 1 coretemp chip
        fake.WriteFile("sys/class/hwmon/hwmon1/name", "coretemp\n");
        fake.WriteFile("sys/class/hwmon/hwmon1/temp1_label", "Package id 1\n");
        fake.WriteFile("sys/class/hwmon/hwmon1/temp1_input", "65000\n");
        fake.WriteFile("sys/class/hwmon/hwmon1/temp2_label", "Core 0\n");
        fake.WriteFile("sys/class/hwmon/hwmon1/temp2_input", "60000\n");
        fake.WriteFile("sys/class/hwmon/hwmon1/temp3_label", "Core 1\n");
        fake.WriteFile("sys/class/hwmon/hwmon1/temp3_input", "62000\n");

        // CPU topology: 4 CPUs across 2 sockets
        // cpu0: socket 0, core 0
        fake.WriteFile("sys/devices/system/cpu/cpu0/topology/physical_package_id", "0\n");
        fake.WriteFile("sys/devices/system/cpu/cpu0/topology/core_id", "0\n");

        // cpu1: socket 0, core 1
        fake.WriteFile("sys/devices/system/cpu/cpu1/topology/physical_package_id", "0\n");
        fake.WriteFile("sys/devices/system/cpu/cpu1/topology/core_id", "1\n");

        // cpu2: socket 1, core 0
        fake.WriteFile("sys/devices/system/cpu/cpu2/topology/physical_package_id", "1\n");
        fake.WriteFile("sys/devices/system/cpu/cpu2/topology/core_id", "0\n");

        // cpu3: socket 1, core 1
        fake.WriteFile("sys/devices/system/cpu/cpu3/topology/physical_package_id", "1\n");
        fake.WriteFile("sys/devices/system/cpu/cpu3/topology/core_id", "1\n");

        CpuThermalReading reading = ThermalReader.Read(fake.Root, logicalCount: 4);

        Assert.AreEqual(50, reading.CpuTemp);
        Assert.AreEqual(4, reading.CoreTemps.Count);

        // Core 0 on Socket 0 gets 45 C, while Core 0 on Socket 1 gets 60 C
        Assert.AreEqual(45, reading.CoreTemps[0]);
        Assert.AreEqual(47, reading.CoreTemps[1]);
        Assert.AreEqual(60, reading.CoreTemps[2]);
        Assert.AreEqual(62, reading.CoreTemps[3]);
        Assert.AreNotEqual(reading.CoreTemps[0], reading.CoreTemps[2]);
    }

    [TestMethod]
    public void PackageOf_IdentifiesPackageFromDeviceNameWhenLabelMissing()
    {
        using var fake = new FakeRoot();

        // coretemp platform device: coretemp.1
        string deviceHwmon = fake.CreateDirectory("sys/devices/platform/coretemp.1/hwmon/hwmon0");
        fake.WriteFile("sys/devices/platform/coretemp.1/hwmon/hwmon0/name", "coretemp\n");
        fake.WriteFile("sys/devices/platform/coretemp.1/hwmon/hwmon0/temp1_input", "55000\n");
        fake.CreateDirectorySymlink("sys/class/hwmon/hwmon0", deviceHwmon);

        IReadOnlyList<HwmonSensor> sensors = Hwmon.Enumerate(fake.Root);
        Assert.AreEqual(1, sensors.Count);
        Assert.AreEqual(1, ThermalReader.PackageOf(sensors[0]));
    }
}
