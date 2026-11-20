using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class BatteryReaderTests
{
    [TestMethod]
    public void Read_SysfsEnergyAndPower_ConvertsMicroUnitsToWholeUnits()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/model_name", "AP18C4K\n");
        fake.WriteFile("sys/class/power_supply/BAT0/manufacturer", "SMP\n");
        fake.WriteFile("sys/class/power_supply/BAT0/technology", "Li-ion\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "45000000\n"); // 45.0 Wh
        fake.WriteFile("sys/class/power_supply/BAT0/energy_full", "50000000\n"); // 50.0 Wh
        fake.WriteFile("sys/class/power_supply/BAT0/energy_full_design", "60000000\n"); // 60.0 Wh
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "12500000\n"); // 12.5 W
        fake.WriteFile("sys/class/power_supply/BAT0/voltage_now", "12000000\n"); // 12.0 V
        fake.WriteFile("sys/class/power_supply/BAT0/capacity", "90\n");
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Discharging\n");

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        Battery bat = reading.Batteries[0];

        Assert.AreEqual("BAT0", bat.Id);
        Assert.AreEqual("AP18C4K", bat.Model);
        Assert.AreEqual("SMP", bat.Manufacturer);
        Assert.AreEqual("Li-ion", bat.Technology);
        Assert.AreEqual(90, bat.Percent);
        Assert.AreEqual(BatteryState.Discharging, bat.State);
        Assert.AreEqual(45.0, bat.EnergyNowWh!.Value, 0.001);
        Assert.AreEqual(50.0, bat.EnergyFullWh!.Value, 0.001);
        Assert.AreEqual(60.0, bat.EnergyDesignWh!.Value, 0.001);
        Assert.AreEqual(12.5, bat.PowerW!.Value, 0.001);
        Assert.AreEqual(12.0, bat.VoltageV!.Value, 0.001);
        Assert.AreEqual(50.0 / 60.0 * 100, bat.HealthPercent!.Value, 0.001);
    }

    [TestMethod]
    public void Read_SysfsChargeAndCurrent_ConvertsUsingVoltageNow()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        // No energy_* or power_now files
        fake.WriteFile("sys/class/power_supply/BAT0/charge_now", "4000000\n"); // 4.0 Ah
        fake.WriteFile("sys/class/power_supply/BAT0/charge_full", "5000000\n"); // 5.0 Ah
        fake.WriteFile("sys/class/power_supply/BAT0/charge_full_design", "6000000\n"); // 6.0 Ah
        fake.WriteFile("sys/class/power_supply/BAT0/current_now", "1500000\n"); // 1.5 A
        fake.WriteFile("sys/class/power_supply/BAT0/voltage_now", "12000000\n"); // 12.0 V
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Charging\n");

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        Battery bat = reading.Batteries[0];

        // 4.0 Ah * 12.0 V = 48.0 Wh
        Assert.AreEqual(48.0, bat.EnergyNowWh!.Value, 0.001);
        // 5.0 Ah * 12.0 V = 60.0 Wh
        Assert.AreEqual(60.0, bat.EnergyFullWh!.Value, 0.001);
        // 6.0 Ah * 12.0 V = 72.0 Wh
        Assert.AreEqual(72.0, bat.EnergyDesignWh!.Value, 0.001);
        // 1.5 A * 12.0 V = 18.0 W
        Assert.AreEqual(18.0, bat.PowerW!.Value, 0.001);
        Assert.AreEqual(12.0, bat.VoltageV!.Value, 0.001);
    }

    [TestMethod]
    public void Read_SysfsChargeAndCurrent_FallsBackToVoltageMinDesign()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/charge_now", "4000000\n"); // 4.0 Ah
        fake.WriteFile("sys/class/power_supply/BAT0/current_now", "1000000\n"); // 1.0 A
        // voltage_now is absent; voltage_min_design is present
        fake.WriteFile("sys/class/power_supply/BAT0/voltage_min_design", "11100000\n"); // 11.1 V
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Discharging\n");

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        Battery bat = reading.Batteries[0];

        // 4.0 Ah * 11.1 V = 44.4 Wh
        Assert.AreEqual(44.4, bat.EnergyNowWh!.Value, 0.001);
        // 1.0 A * 11.1 V = 11.1 W
        Assert.AreEqual(11.1, bat.PowerW!.Value, 0.001);
        Assert.AreEqual(11.1, bat.VoltageV!.Value, 0.001);
    }

    [TestMethod]
    public void Read_SysfsChargeAndCurrent_WithoutVoltage_LeavesEnergyAndPowerNull()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/charge_now", "4000000\n");
        fake.WriteFile("sys/class/power_supply/BAT0/charge_full", "5000000\n");
        fake.WriteFile("sys/class/power_supply/BAT0/current_now", "1000000\n");
        // No voltage_now and no voltage_min_design

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        Battery bat = reading.Batteries[0];

        Assert.IsNull(bat.EnergyNowWh);
        Assert.IsNull(bat.EnergyFullWh);
        Assert.IsNull(bat.EnergyDesignWh);
        Assert.IsNull(bat.PowerW);
        Assert.IsNull(bat.VoltageV);
    }

    [TestMethod]
    public void Read_CapacityMissing_CalculatesPercentFromEnergy()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        // No capacity file
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "30000000\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_full", "60000000\n");

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        // 30 / 60 = 50 %
        Assert.AreEqual(50, reading.Batteries[0].Percent);
    }

    [TestMethod]
    public void Read_CapacityMissing_CalculatesPercentFromCharge()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        // No capacity, no energy_* files
        fake.WriteFile("sys/class/power_supply/BAT0/charge_now", "2500000\n");
        fake.WriteFile("sys/class/power_supply/BAT0/charge_full", "10000000\n");

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        // 2.5 / 10 = 25 %
        Assert.AreEqual(25, reading.Batteries[0].Percent);
    }

    [TestMethod]
    public void Read_CapacityMissing_NoEnergyOrCharge_LeavesPercentNull()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        Assert.IsNull(reading.Batteries[0].Percent);
    }

    [TestMethod]
    public void Read_HealthPercent_CalculatesFullDividedByDesign()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_full", "40000000\n"); // 40.0 Wh
        fake.WriteFile("sys/class/power_supply/BAT0/energy_full_design", "50000000\n"); // 50.0 Wh

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        // 40.0 / 50.0 * 100 = 80 %
        Assert.AreEqual(80.0, reading.Batteries[0].HealthPercent!.Value, 0.001);
    }

    [TestMethod]
    public void Read_CycleCount_ZeroWithoutHistory_ReturnsNull()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/cycle_count", "0\n");
        // No energy_now and no charge_now

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        Assert.IsNull(reading.Batteries[0].CycleCount);
    }

    [TestMethod]
    public void Read_CycleCount_ZeroWithReadings_KeptAsZero()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/cycle_count", "0\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "40000000\n");

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        Assert.AreEqual(0, reading.Batteries[0].CycleCount);
    }

    [TestMethod]
    public void Read_CycleCount_PositiveNumber_KeptAsReported()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/cycle_count", "42\n");

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        Assert.AreEqual(42, reading.Batteries[0].CycleCount);
    }

    [TestMethod]
    public void Read_StatusMapping_MapsAllKnownStatesAndUnknown()
    {
        (string? Status, BatteryState Expected)[] cases =
        {
            ("Charging", BatteryState.Charging),
            ("charging", BatteryState.Charging),
            ("Discharging", BatteryState.Discharging),
            ("discharging", BatteryState.Discharging),
            ("Full", BatteryState.Full),
            ("full", BatteryState.Full),
            ("Not charging", BatteryState.NotCharging),
            ("not charging", BatteryState.NotCharging),
            ("Unknown", BatteryState.Unknown),
            ("SomeCustomStatus", BatteryState.Unknown),
            ("", BatteryState.Unknown),
            (null, BatteryState.Unknown),
        };

        foreach ((string? status, BatteryState expected) in cases)
        {
            using var fake = new FakeRoot();
            fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
            fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
            if (status is not null)
            {
                fake.WriteFile("sys/class/power_supply/BAT0/status", status + "\n");
            }

            var reader = new BatteryReader(fake.Root);
            PowerSupplyReading reading = reader.Read();

            Assert.AreEqual(1, reading.Batteries.Count);
            Assert.AreEqual(expected, reading.Batteries[0].State, $"Failed for status '{status}'");
        }
    }

    [TestMethod]
    public void Read_ScopeDevice_ReportsAsSysfsPeripheralNotSystemBattery()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/hid-mouse-battery/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/hid-mouse-battery/scope", "Device\n");
        fake.WriteFile("sys/class/power_supply/hid-mouse-battery/present", "1\n");
        fake.WriteFile("sys/class/power_supply/hid-mouse-battery/model_name", "Wireless Mouse\n");
        fake.WriteFile("sys/class/power_supply/hid-mouse-battery/capacity", "85\n");
        fake.WriteFile("sys/class/power_supply/hid-mouse-battery/status", "Discharging\n");

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(0, reading.Batteries.Count, "Device scope must not be in Batteries.");
        Assert.AreEqual(1, reading.Devices.Count, "Device scope must be in Devices.");

        SysfsPeripheral dev = reading.Devices[0];
        Assert.AreEqual("hid-mouse-battery", dev.Name);
        Assert.AreEqual(PeripheralKey.ForSysfs("hid-mouse-battery"), dev.Device.Id);
        Assert.AreEqual(PeripheralKind.Other, dev.Device.Kind);
        Assert.AreEqual("Wireless Mouse", dev.Device.Model);
        Assert.AreEqual(85, dev.Device.Percent);
        Assert.AreEqual(BatteryState.Discharging, dev.Device.State);
    }

    [TestMethod]
    public void Read_ScopeSystemOrMissing_ReportsAsSystemBattery()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/scope", "System\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");

        fake.WriteFile("sys/class/power_supply/BAT1/type", "Battery\n");
        // BAT1 has no scope file -> defaults to system
        fake.WriteFile("sys/class/power_supply/BAT1/present", "1\n");

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(2, reading.Batteries.Count);
        Assert.AreEqual(0, reading.Devices.Count);
        Assert.AreEqual("BAT0", reading.Batteries[0].Id);
        Assert.AreEqual("BAT1", reading.Batteries[1].Id);
    }

    [TestMethod]
    public void Read_ScopeUnrecognized_Ignored()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/scope", "UnknownScope\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(0, reading.Batteries.Count);
        Assert.AreEqual(0, reading.Devices.Count);
    }

    [TestMethod]
    public void Read_PresentZero_Ignored()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "0\n");

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(0, reading.Batteries.Count);
    }

    [TestMethod]
    public void Read_PowerSources_MapsMainsAndUsbTypesWithOnlineStatus()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/AC0/type", "Mains\n");
        fake.WriteFile("sys/class/power_supply/AC0/online", "1\n");

        fake.WriteFile("sys/class/power_supply/AC1/type", "Mains\n");
        fake.WriteFile("sys/class/power_supply/AC1/online", "0\n");

        fake.WriteFile("sys/class/power_supply/USB0/type", "USB\n");
        fake.WriteFile("sys/class/power_supply/USB0/online", "1\n");

        fake.WriteFile("sys/class/power_supply/USB_C/type", "USB_C\n");
        // No online file -> defaults to false

        fake.WriteFile("sys/class/power_supply/USB_PD/type", "USB_PD\n");
        fake.WriteFile("sys/class/power_supply/USB_PD/online", "1\n");

        fake.WriteFile("sys/class/power_supply/OTHER/type", "Solar\n");

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(5, reading.PowerSources.Count);

        PowerSource ac0 = reading.PowerSources.First(s => s.Id == "AC0");
        Assert.AreEqual(PowerSourceKind.Mains, ac0.Kind);
        Assert.IsTrue(ac0.Online);

        PowerSource ac1 = reading.PowerSources.First(s => s.Id == "AC1");
        Assert.AreEqual(PowerSourceKind.Mains, ac1.Kind);
        Assert.IsFalse(ac1.Online);

        PowerSource usb0 = reading.PowerSources.First(s => s.Id == "USB0");
        Assert.AreEqual(PowerSourceKind.Usb, usb0.Kind);
        Assert.IsTrue(usb0.Online);

        PowerSource usbC = reading.PowerSources.First(s => s.Id == "USB_C");
        Assert.AreEqual(PowerSourceKind.Usb, usbC.Kind);
        Assert.IsFalse(usbC.Online);

        PowerSource usbPd = reading.PowerSources.First(s => s.Id == "USB_PD");
        Assert.AreEqual(PowerSourceKind.Usb, usbPd.Kind);
        Assert.IsTrue(usbPd.Online);
    }

    [TestMethod]
    public void Read_StaleSmoothing_RemovedWhenBatteryDisappears()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Discharging\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "30000000\n");
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "10000000\n"); // 10 W

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading1 = reader.Read();
        Assert.AreEqual(1, reading1.Batteries.Count);

        // Delete BAT0 directory so it disappears
        Directory.Delete(Path.Combine(fake.Root, "sys/class/power_supply/BAT0"), recursive: true);

        PowerSupplyReading reading2 = reader.Read();
        Assert.AreEqual(0, reading2.Batteries.Count);

        // Recreate BAT0 with power 20 W
        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Discharging\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "20000000\n"); // 20 Wh
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "20000000\n"); // 20 W

        PowerSupplyReading reading3 = reader.Read();
        Assert.AreEqual(1, reading3.Batteries.Count);
        // Because smoothing was purged, BAT0 seeds afresh with 20 W:
        // TimeToEmpty = 20 Wh / 20 W = 1.0 hour (if not purged, it would blend 0.3*20 + 0.7*10 = 13 W)
        Assert.AreEqual(TimeSpan.FromHours(1.0), reading3.Batteries[0].TimeToEmpty);
    }

    [TestMethod]
    public void Read_EmaSmoothing_WeightsNewestReadingWithAlpha03()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Discharging\n");

        var reader = new BatteryReader(fake.Root);

        // Sample 1: Power = 10.0 W. Average seeds at 10.0 W.
        // Energy = 10.0 Wh -> TimeToEmpty = 10.0 / 10.0 = 1.0 h.
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "10000000\n");
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "10000000\n");
        PowerSupplyReading r1 = reader.Read();
        Assert.AreEqual(TimeSpan.FromHours(1.0), r1.Batteries[0].TimeToEmpty);

        // Sample 2: Power = 20.0 W.
        // Average = 0.3 * 20.0 + 0.7 * 10.0 = 6.0 + 7.0 = 13.0 W.
        // Set Energy = 13.0 Wh -> TimeToEmpty = 13.0 / 13.0 = 1.0 h.
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "13000000\n");
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "20000000\n");
        PowerSupplyReading r2 = reader.Read();
        Assert.AreEqual(TimeSpan.FromHours(1.0), r2.Batteries[0].TimeToEmpty);

        // Sample 3: Power = 30.0 W.
        // Average = 0.3 * 30.0 + 0.7 * 13.0 = 9.0 + 9.1 = 18.1 W.
        // Set Energy = 18.1 Wh -> TimeToEmpty = 18.1 / 18.1 = 1.0 h.
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "18100000\n");
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "30000000\n");
        PowerSupplyReading r3 = reader.Read();
        Assert.AreEqual(TimeSpan.FromHours(1.0), r3.Batteries[0].TimeToEmpty);
    }

    [TestMethod]
    public void Read_EmaSmoothing_ResetsWhenStateChanges()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Discharging\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "10000000\n");
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "10000000\n"); // 10 W

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading r1 = reader.Read();
        Assert.AreEqual(BatteryState.Discharging, r1.Batteries[0].State);

        // State changes to Charging with Power = 20 W
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Charging\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "10000000\n"); // 10 Wh
        fake.WriteFile("sys/class/power_supply/BAT0/energy_full", "50000000\n"); // 50 Wh
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "20000000\n"); // 20 W

        PowerSupplyReading r2 = reader.Read();
        Assert.AreEqual(BatteryState.Charging, r2.Batteries[0].State);
        // Average reset to 20 W rather than blending with previous 10 W:
        // TimeToFull = (50 - 10) / 20 = 40 / 20 = 2.0 h.
        Assert.AreEqual(TimeSpan.FromHours(2.0), r2.Batteries[0].TimeToFull);
    }

    [TestMethod]
    public void Read_EmaSmoothing_NullPowerClearsAverage()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Discharging\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "10000000\n");
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "10000000\n");

        var reader = new BatteryReader(fake.Root);
        reader.Read(); // seeded with 10 W

        // Power becomes missing
        File.Delete(Path.Combine(fake.Root, "sys/class/power_supply/BAT0/power_now"));
        PowerSupplyReading r2 = reader.Read();
        Assert.IsNull(r2.Batteries[0].PowerW);
        Assert.IsNull(r2.Batteries[0].TimeToEmpty);

        // Power returns with 30 W -> seeds anew
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "30000000\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "30000000\n");
        PowerSupplyReading r3 = reader.Read();
        // TimeToEmpty = 30 / 30 = 1.0 h (seeds at 30 W, not blended)
        Assert.AreEqual(TimeSpan.FromHours(1.0), r3.Batteries[0].TimeToEmpty);
    }

    [TestMethod]
    public void Read_TimeToEmpty_CalculatesDischargingTime()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Discharging\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "30000000\n"); // 30 Wh
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "10000000\n"); // 10 W

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        Battery bat = reading.Batteries[0];
        // 30 Wh / 10 W = 3.0 h
        Assert.AreEqual(TimeSpan.FromHours(3.0), bat.TimeToEmpty);
        Assert.IsNull(bat.TimeToFull);
    }

    [TestMethod]
    public void Read_TimeToFull_CalculatesChargingTime()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Charging\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "20000000\n"); // 20 Wh
        fake.WriteFile("sys/class/power_supply/BAT0/energy_full", "50000000\n"); // 50 Wh
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "15000000\n"); // 15 W

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        Battery bat = reading.Batteries[0];
        // (50 - 20) Wh / 15 W = 30 / 15 = 2.0 h
        Assert.AreEqual(TimeSpan.FromHours(2.0), bat.TimeToFull);
        Assert.IsNull(bat.TimeToEmpty);
    }

    [TestMethod]
    public void Read_TimeToEmptyAndFull_NullWhenPowerBelowMinimumThreshold()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Discharging\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "30000000\n");
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "400000\n"); // 0.4 W (< 0.5 W)

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        Assert.IsNull(reading.Batteries[0].TimeToEmpty);
        Assert.IsNull(reading.Batteries[0].TimeToFull);
    }

    [TestMethod]
    public void Read_TimeToEmptyAndFull_NullWhenTimeExceeds48Hours()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Discharging\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "50000000\n"); // 50 Wh
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "1000000\n"); // 1.0 W -> 50 h > 48 h

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        Assert.IsNull(reading.Batteries[0].TimeToEmpty);

        // Charging exceeding 48 hours
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Charging\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "0\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_full", "50000000\n"); // (50 - 0) / 1 = 50 h
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "1000000\n");

        PowerSupplyReading readingCharging = reader.Read();
        Assert.IsNull(readingCharging.Batteries[0].TimeToFull);
    }

    [TestMethod]
    public void Read_TimeToFull_NullWhenEnergyFullLessThanOrEqualToEnergyNow()
    {
        using var fake = new FakeRoot();

        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Charging\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_now", "50000000\n");
        fake.WriteFile("sys/class/power_supply/BAT0/energy_full", "50000000\n"); // full == now
        fake.WriteFile("sys/class/power_supply/BAT0/power_now", "10000000\n");

        var reader = new BatteryReader(fake.Root);
        PowerSupplyReading reading = reader.Read();

        Assert.AreEqual(1, reading.Batteries.Count);
        Assert.IsNull(reading.Batteries[0].TimeToFull);

        // full < now
        fake.WriteFile("sys/class/power_supply/BAT0/energy_full", "45000000\n");
        PowerSupplyReading readingLess = reader.Read();
        Assert.IsNull(readingLess.Batteries[0].TimeToFull);
    }
}
