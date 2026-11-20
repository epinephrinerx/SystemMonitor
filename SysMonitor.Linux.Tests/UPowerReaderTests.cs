using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class UPowerReaderTests
{
    private sealed class TestCommandRunner : ICommandRunner
    {
        public Func<string, IReadOnlyList<string>, int, CommandResult?>? ResultHandler { get; set; }
        public List<(string File, IReadOnlyList<string> Args, int TimeoutMs)> Executions { get; } = new();

        public string? Run(string file, string args, int timeoutMs) => null;

        public CommandResult? RunResult(string file, IReadOnlyList<string> args, int timeoutMs)
        {
            Executions.Add((file, args, timeoutMs));
            if (ResultHandler is not null)
            {
                return ResultHandler(file, args, timeoutMs);
            }
            return null;
        }
    }

    private const string UpowerDumpFixture = """
    Device: /org/freedesktop/UPower/devices/DisplayDevice
      power supply:         yes
      percentage:           50%

    Device: /org/freedesktop/UPower/devices/line_power_AC
      native-path:          AC
      power supply:         yes
      line-power
        online:             yes

    Device: /org/freedesktop/UPower/devices/battery_BAT0
      native-path:          BAT0
      power supply:         yes
      vendor:               SMP
      model:                AP18C4K
      battery
        present:             yes
        state:               charging
        percentage:          80%

    Device: /org/freedesktop/UPower/devices/mouse_dev_AA_BB_CC_DD_EE_11
      native-path:          hid-AA:BB:CC:DD:EE:11-battery
      model:                Logitech MX Master 3
      serial:               SECRET_SERIAL_MOUSE_123
      mouse
        present:             yes
        state:               discharging
        percentage:          75%

    Device: /org/freedesktop/UPower/devices/keyboard_dev_AA_BB_CC_DD_EE_22
      native-path:          hid-AA:BB:CC:DD:EE:22-battery
      model:                Keychron K2
      serial:               SECRET_SERIAL_KEYBOARD_456
      keyboard
        present:             yes
        state:               charging
        percentage:          90%

    Device: /org/freedesktop/UPower/devices/mouse_dev_AA_BB_CC_DD_EE_33
      native-path:          hid-AA:BB:CC:DD:EE:33-battery
      model:                Old Wireless Mouse
      serial:               SECRET_SERIAL_OLD_789
      mouse
        present:             no
        state:               discharging
        percentage:          15%

    Device: /org/freedesktop/UPower/devices/headset_dev_AA_BB_CC_DD_EE_44
      native-path:          hid-AA:BB:CC:DD:EE:44-battery
      model:                Sony WH-1000XM4
      serial:               SECRET_SERIAL_HEADSET_000
      headset
        present:             yes
        state:               fully-charged
        percentage:          100%
    """;

    [TestMethod]
    public void Read_FullDumpFixture_FiltersNonPeripheralsAndExtractsDevices()
    {
        var runner = new TestCommandRunner
        {
            ResultHandler = (_, _, _) => new CommandResult(0, UpowerDumpFixture, string.Empty)
        };

        var reader = new UPowerReader(runner);

        // BAT0 is reported as a system battery from sysfs
        var sysfsBatteries = new[]
        {
            new Battery { Id = "BAT0", State = BatteryState.Charging }
        };
        var sysfs = new PowerSupplyReading(sysfsBatteries, Array.Empty<PowerSource>(), Array.Empty<SysfsPeripheral>());

        IReadOnlyList<PeripheralBattery> peripherals = reader.Read(stamp: 1000, sysfs: sysfs);

        // DisplayDevice, line_power_AC, battery_BAT0, and present: no device must all be excluded.
        // Only Mouse, Keyboard, Headset must remain.
        Assert.AreEqual(3, peripherals.Count);

        PeripheralBattery mouse = peripherals.First(p => p.Kind == PeripheralKind.Mouse);
        Assert.AreEqual("Logitech MX Master 3", mouse.Model);
        Assert.AreEqual(75, mouse.Percent);
        Assert.AreEqual(BatteryState.Discharging, mouse.State);

        PeripheralBattery keyboard = peripherals.First(p => p.Kind == PeripheralKind.Keyboard);
        Assert.AreEqual("Keychron K2", keyboard.Model);
        Assert.AreEqual(90, keyboard.Percent);
        Assert.AreEqual(BatteryState.Charging, keyboard.State);

        PeripheralBattery headset = peripherals.First(p => p.Kind == PeripheralKind.Headset);
        Assert.AreEqual("Sony WH-1000XM4", headset.Model);
        Assert.AreEqual(100, headset.Percent);
        Assert.AreEqual(BatteryState.Full, headset.State);
    }

    [TestMethod]
    public void Read_Privacy_NeverExposesMacAddressOrSerialInAnyField()
    {
        var runner = new TestCommandRunner
        {
            ResultHandler = (_, _, _) => new CommandResult(0, UpowerDumpFixture, string.Empty)
        };

        var reader = new UPowerReader(runner);
        IReadOnlyList<PeripheralBattery> peripherals = reader.Read(stamp: 1000);

        Assert.IsTrue(peripherals.Count > 0);

        string[] forbiddenSubstrings =
        {
            "AA:BB:CC:DD:EE",
            "AA_BB_CC_DD_EE",
            "SECRET_SERIAL",
            "MOUSE_123",
            "KEYBOARD_456",
            "HEADSET_000"
        };

        foreach (PeripheralBattery p in peripherals)
        {
            foreach (string forbidden in forbiddenSubstrings)
            {
                Assert.IsFalse(
                    p.Id.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"Id '{p.Id}' contained sensitive fragment '{forbidden}'");

                if (p.Model is not null)
                {
                    Assert.IsFalse(
                        p.Model.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                        $"Model '{p.Model}' contained sensitive fragment '{forbidden}'");
                }
            }
        }
    }

    [TestMethod]
    public void Read_Stability_GeneratesStableIdAcrossParsesWithoutMac()
    {
        var runner1 = new TestCommandRunner
        {
            ResultHandler = (_, _, _) => new CommandResult(0, UpowerDumpFixture, string.Empty)
        };
        var reader1 = new UPowerReader(runner1);
        IReadOnlyList<PeripheralBattery> list1 = reader1.Read(stamp: 1000);

        var runner2 = new TestCommandRunner
        {
            ResultHandler = (_, _, _) => new CommandResult(0, UpowerDumpFixture, string.Empty)
        };
        var reader2 = new UPowerReader(runner2);
        IReadOnlyList<PeripheralBattery> list2 = reader2.Read(stamp: 5000);

        Assert.AreEqual(list1.Count, list2.Count);

        string[] fixtureMacs =
        {
            "AA:BB:CC:DD:EE:11", "AABBCCDDEE11",
            "AA:BB:CC:DD:EE:22", "AABBCCDDEE22",
            "AA:BB:CC:DD:EE:33", "AABBCCDDEE33",
            "AA:BB:CC:DD:EE:44", "AABBCCDDEE44"
        };

        for (int i = 0; i < list1.Count; i++)
        {
            Assert.AreEqual(list1[i].Id, list2[i].Id, "Id must be stable across separate parses of the same device path.");
            Assert.AreEqual(12, list1[i].Id.Length, "Id must be a 12-char hex hash.");
            foreach (string mac in fixtureMacs)
            {
                Assert.IsFalse(
                    list1[i].Id.Contains(mac, StringComparison.OrdinalIgnoreCase),
                    $"Id '{list1[i].Id}' must not contain fixture MAC '{mac}'");
            }
        }
    }

    [TestMethod]
    public void Read_Cadence_CachesResultsWithin30Seconds()
    {
        var runner = new TestCommandRunner
        {
            ResultHandler = (_, _, _) => new CommandResult(0, UpowerDumpFixture, string.Empty)
        };

        var reader = new UPowerReader(runner);

        // First call at stamp 0: runs command
        IReadOnlyList<PeripheralBattery> first = reader.Read(stamp: 0);
        Assert.AreEqual(1, runner.Executions.Count);
        Assert.AreEqual(3, first.Count);

        // Second call at stamp 10_000 (10 s later, inside 30 s cache window): does NOT run command
        IReadOnlyList<PeripheralBattery> second = reader.Read(stamp: 10_000);
        Assert.AreEqual(1, runner.Executions.Count, "Command runner must not be called within 30 s.");
        Assert.AreEqual(3, second.Count);

        // Third call at stamp 29_999: still inside cache window
        IReadOnlyList<PeripheralBattery> third = reader.Read(stamp: 29_999);
        Assert.AreEqual(1, runner.Executions.Count);
    }

    [TestMethod]
    public void Read_Cadence_RefreshesAfter30Seconds()
    {
        var runner = new TestCommandRunner
        {
            ResultHandler = (_, _, _) => new CommandResult(0, UpowerDumpFixture, string.Empty)
        };

        var reader = new UPowerReader(runner);

        // Initial run at stamp 1000
        reader.Read(stamp: 1000);
        Assert.AreEqual(1, runner.Executions.Count);

        // At stamp 31_000 (30 s later): cache expired, runs command again
        reader.Read(stamp: 31_000);
        Assert.AreEqual(2, runner.Executions.Count, "Command runner must be called once the 30 s window has passed.");
    }

    [TestMethod]
    public void Read_Cadence_RunsAgainWhenClockGoesBackwards()
    {
        var runner = new TestCommandRunner
        {
            ResultHandler = (_, _, _) => new CommandResult(0, UpowerDumpFixture, string.Empty)
        };

        var reader = new UPowerReader(runner);

        reader.Read(stamp: 50_000);
        Assert.AreEqual(1, runner.Executions.Count);

        // Clock jump backwards
        reader.Read(stamp: 10_000);
        Assert.AreEqual(2, runner.Executions.Count, "Command runner must run again when clock steps backwards.");
    }

    [TestMethod]
    public void Read_Failure_MissingOrTimeout_ReturnsEmptyListAndLogsOnce()
    {
        string logDir = Path.Combine(Path.GetTempPath(), "upower-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDir);
        string logFile = Path.Combine(logDir, "sysmonitor.log");

        try
        {
            var diagnostics = new Diagnostics(logFile, enabled: true);
            var runner = new TestCommandRunner
            {
                ResultHandler = (_, _, _) => null // Simulates missing tool or timeout
            };

            var reader = new UPowerReader(runner, diagnostics);

            // Call 1: failure at stamp 0
            IReadOnlyList<PeripheralBattery> devices1 = reader.Read(stamp: 0);
            Assert.AreEqual(0, devices1.Count);

            string[] lines = File.ReadAllLines(logFile);
            Assert.AreEqual(1, lines.Length);
            Assert.IsTrue(lines[0].Contains("upower: not available or timed out; device batteries are hidden until the next check"));

            // Call 2: repeated failure at stamp 35_000 (after cache window)
            IReadOnlyList<PeripheralBattery> devices2 = reader.Read(stamp: 35_000);
            Assert.AreEqual(0, devices2.Count);

            // Must NOT log duplicate line across repeated failure episodes
            string[] lines2 = File.ReadAllLines(logFile);
            Assert.AreEqual(1, lines2.Length, "Diagnostics line must not repeat across repeated failures.");
        }
        finally
        {
            if (Directory.Exists(logDir))
            {
                Directory.Delete(logDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Read_Failure_NonZeroExit_ReturnsEmptyListAndLogsExitCode()
    {
        string logDir = Path.Combine(Path.GetTempPath(), "upower-exit-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDir);
        string logFile = Path.Combine(logDir, "sysmonitor.log");

        try
        {
            var diagnostics = new Diagnostics(logFile, enabled: true);
            var runner = new TestCommandRunner
            {
                ResultHandler = (_, _, _) => new CommandResult(1, string.Empty, "error: daemon not responding")
            };

            var reader = new UPowerReader(runner, diagnostics);

            IReadOnlyList<PeripheralBattery> devices = reader.Read(stamp: 0);
            Assert.AreEqual(0, devices.Count);

            string[] lines = File.ReadAllLines(logFile);
            Assert.AreEqual(1, lines.Length);
            Assert.IsTrue(lines[0].Contains("upower: exited 1; device batteries are hidden until the next check"));
        }
        finally
        {
            if (Directory.Exists(logDir))
            {
                Directory.Delete(logDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Read_Failure_RecoveryResetsFailureLogged()
    {
        string logDir = Path.Combine(Path.GetTempPath(), "upower-rec-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDir);
        string logFile = Path.Combine(logDir, "sysmonitor.log");

        try
        {
            var diagnostics = new Diagnostics(logFile, enabled: true);
            bool fail = true;
            var runner = new TestCommandRunner
            {
                ResultHandler = (_, _, _) => fail
                    ? null
                    : new CommandResult(0, UpowerDumpFixture, string.Empty)
            };

            var reader = new UPowerReader(runner, diagnostics);

            // 1. Initial failure
            reader.Read(stamp: 0);
            Assert.AreEqual(1, File.ReadAllLines(logFile).Length);

            // 2. Success at next interval -> resets failureLogged flag
            fail = false;
            IReadOnlyList<PeripheralBattery> okDevices = reader.Read(stamp: 35_000);
            Assert.AreEqual(3, okDevices.Count);

            // 3. Failure again at next interval -> must log anew
            fail = true;
            reader.Read(stamp: 70_000);
            Assert.AreEqual(2, File.ReadAllLines(logFile).Length);
        }
        finally
        {
            if (Directory.Exists(logDir))
            {
                Directory.Delete(logDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Read_Deduplication_SysfsDeviceMatchingUpowerNativePath_IsDropped()
    {
        var runner = new TestCommandRunner
        {
            ResultHandler = (_, _, _) => new CommandResult(0, UpowerDumpFixture, string.Empty)
        };

        var reader = new UPowerReader(runner);

        // Sysfs reports a peripheral with the exact same name as UPower's mouse native path
        var duplicateSysfsPeripheral = new SysfsPeripheral(
            "hid-AA:BB:CC:DD:EE:11-battery",
            new PeripheralBattery
            {
                Id = PeripheralKey.ForSysfs("hid-AA:BB:CC:DD:EE:11-battery"),
                Kind = PeripheralKind.Other,
                Model = "Sysfs Generic Mouse",
                Percent = 50,
                State = BatteryState.Discharging
            });

        var sysfs = new PowerSupplyReading(
            Array.Empty<Battery>(),
            Array.Empty<PowerSource>(),
            new[] { duplicateSysfsPeripheral });

        IReadOnlyList<PeripheralBattery> peripherals = reader.Read(stamp: 1000, sysfs: sysfs);

        // Total must be 3 (not 4). UPower mouse wins!
        Assert.AreEqual(3, peripherals.Count);
        Assert.IsFalse(peripherals.Any(p => p.Model == "Sysfs Generic Mouse"));
        Assert.IsTrue(peripherals.Any(p => p.Model == "Logitech MX Master 3"));
    }

    [TestMethod]
    public void Read_Deduplication_SysfsDeviceNotInUpower_IsPreserved()
    {
        var runner = new TestCommandRunner
        {
            ResultHandler = (_, _, _) => new CommandResult(0, UpowerDumpFixture, string.Empty)
        };

        var reader = new UPowerReader(runner);

        // Sysfs reports a peripheral that UPower does not know
        var standalonePeripheral = new SysfsPeripheral(
            "hid-standalone-device-battery",
            new PeripheralBattery
            {
                Id = PeripheralKey.ForSysfs("hid-standalone-device-battery"),
                Kind = PeripheralKind.Other,
                Model = "Standalone Device",
                Percent = 65,
                State = BatteryState.Discharging
            });

        var sysfs = new PowerSupplyReading(
            Array.Empty<Battery>(),
            Array.Empty<PowerSource>(),
            new[] { standalonePeripheral });

        IReadOnlyList<PeripheralBattery> peripherals = reader.Read(stamp: 1000, sysfs: sysfs);

        // Total is 3 (UPower) + 1 (Sysfs standalone) = 4
        Assert.AreEqual(4, peripherals.Count);
        Assert.IsTrue(peripherals.Any(p => p.Model == "Standalone Device" && p.Percent == 65));
    }

    [TestMethod]
    public void Read_DeviceClasses_MapsAllSupportedPeripheralKinds()
    {
        string dump = """
        Device: /org/freedesktop/UPower/devices/gamepad_dev
          model: Xbox Controller
          gaming-input
            present: yes
            percentage: 60%
            state: discharging

        Device: /org/freedesktop/UPower/devices/phone_dev
          model: Pixel Phone
          phone
            present: yes
            percentage: 80%
            state: charging

        Device: /org/freedesktop/UPower/devices/tablet_dev
          model: Graphic Tablet
          tablet
            present: yes
            percentage: 95%
            state: fully-charged

        Device: /org/freedesktop/UPower/devices/headphones_dev
          model: Studio Headphones
          headphones
            present: yes
            percentage: 40%
            state: discharging

        Device: /org/freedesktop/UPower/devices/other_dev
          model: Unknown Device
          custom-gadget
            present: yes
            percentage: 30%
            state: unknown
        """;

        var runner = new TestCommandRunner
        {
            ResultHandler = (_, _, _) => new CommandResult(0, dump, string.Empty)
        };

        var reader = new UPowerReader(runner);
        IReadOnlyList<PeripheralBattery> peripherals = reader.Read(stamp: 1000);

        Assert.AreEqual(5, peripherals.Count);
        Assert.AreEqual(PeripheralKind.Gamepad, peripherals.First(p => p.Model == "Xbox Controller").Kind);
        Assert.AreEqual(PeripheralKind.Phone, peripherals.First(p => p.Model == "Pixel Phone").Kind);
        Assert.AreEqual(PeripheralKind.Tablet, peripherals.First(p => p.Model == "Graphic Tablet").Kind);
        Assert.AreEqual(PeripheralKind.Headset, peripherals.First(p => p.Model == "Studio Headphones").Kind);
        Assert.AreEqual(PeripheralKind.Other, peripherals.First(p => p.Model == "Unknown Device").Kind);
    }

    [TestMethod]
    public void Read_BatteryClassDevice_WhenNotSysfsBattery_ReportedAsOther_WhenMatchesSysfs_Skipped()
    {
        string dump = """
        Device: /org/freedesktop/UPower/devices/battery_BAT0
          native-path: BAT0
          model: Laptop Internal Battery
          battery
            present: yes
            state: discharging
            percentage: 85%

        Device: /org/freedesktop/UPower/devices/battery_dev_wacom
          native-path: wacom_battery_0
          model: Wacom Pen Tablet Battery
          battery
            present: yes
            state: discharging
            percentage: 60%
        """;

        var runner = new TestCommandRunner
        {
            ResultHandler = (_, _, _) => new CommandResult(0, dump, string.Empty)
        };

        var reader = new UPowerReader(runner);

        var sysfsBatteries = new[]
        {
            new Battery { Id = "BAT0", State = BatteryState.Discharging }
        };
        var sysfs = new PowerSupplyReading(sysfsBatteries, Array.Empty<PowerSource>(), Array.Empty<SysfsPeripheral>());

        IReadOnlyList<PeripheralBattery> peripherals = reader.Read(stamp: 1000, sysfs: sysfs);

        // BAT0 matches sysfs battery -> skipped.
        // battery_dev_wacom does not match any sysfs battery -> reported as PeripheralKind.Other.
        Assert.AreEqual(1, peripherals.Count);
        PeripheralBattery pen = peripherals[0];
        Assert.AreEqual(PeripheralKind.Other, pen.Kind);
        Assert.AreEqual("Wacom Pen Tablet Battery", pen.Model);
        Assert.AreEqual(60, pen.Percent);
        Assert.AreEqual(BatteryState.Discharging, pen.State);
    }

    [TestMethod]
    public void Read_BatteryClassDevice_WithPowerSupplyYes_WhenNotInSysfs_Skipped()
    {
        string dump = """
        Device: /org/freedesktop/UPower/devices/battery_BAT1
          native-path: BAT1
          power supply: yes
          model: Primary Battery Pack
          battery
            present: yes
            state: discharging
            percentage: 95%

        Device: /org/freedesktop/UPower/devices/mouse_dev
          native-path: hid-mouse-battery
          model: Wireless Mouse
          mouse
            present: yes
            state: discharging
            percentage: 80%
        """;

        var runner = new TestCommandRunner
        {
            ResultHandler = (_, _, _) => new CommandResult(0, dump, string.Empty)
        };

        var reader = new UPowerReader(runner);

        // BAT1 is not in sysfs batteries
        var sysfs = new PowerSupplyReading(
            Array.Empty<Battery>(),
            Array.Empty<PowerSource>(),
            Array.Empty<SysfsPeripheral>());

        IReadOnlyList<PeripheralBattery> peripherals = reader.Read(stamp: 1000, sysfs: sysfs);

        // A battery-class device with power supply: yes is a system power pack
        // and must be skipped even if it is not tracked in sysfs.
        Assert.AreEqual(1, peripherals.Count);
        Assert.AreEqual("Wireless Mouse", peripherals[0].Model);
        Assert.AreEqual(PeripheralKind.Mouse, peripherals[0].Kind);
        Assert.IsFalse(peripherals.Any(p => p.Model == "Primary Battery Pack"));
    }

    [TestMethod]
    public void Read_PresentNoInUpower_DoesNotClaimNativePath_LiveSysfsPeripheralAppearsOnce()
    {
        const string nativePath = "hid-dev-AA:BB:CC:DD:EE:33-battery";
        string dump = $"""
        Device: /org/freedesktop/UPower/devices/mouse_dev_absent
          native-path: {nativePath}
          model: Absent Mouse
          mouse
            present: no
            state: discharging
            percentage: 20%
        """;

        var runner = new TestCommandRunner
        {
            ResultHandler = (_, _, _) => new CommandResult(0, dump, string.Empty)
        };

        var reader = new UPowerReader(runner);

        // A live sysfs device-scope peripheral with the same native path
        var sysfsPeripheral = new SysfsPeripheral(
            nativePath,
            new PeripheralBattery
            {
                Id = PeripheralKey.ForSysfs(nativePath),
                Kind = PeripheralKind.Other,
                Model = "Live Sysfs Mouse",
                Percent = 75,
                State = BatteryState.Discharging
            });

        var sysfs = new PowerSupplyReading(
            Array.Empty<Battery>(),
            Array.Empty<PowerSource>(),
            new[] { sysfsPeripheral });

        IReadOnlyList<PeripheralBattery> peripherals = reader.Read(stamp: 1000, sysfs: sysfs);

        // Absent UPower device is dropped before claiming the native path,
        // so the live sysfs peripheral appears exactly once.
        Assert.AreEqual(1, peripherals.Count);
        PeripheralBattery result = peripherals[0];
        Assert.AreEqual("Live Sysfs Mouse", result.Model);
        Assert.AreEqual(75, result.Percent);
        Assert.AreEqual(BatteryState.Discharging, result.State);
    }
}
