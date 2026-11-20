using System.Globalization;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// Peripheral batteries from <c>upower --dump</c>: mice, keyboards, headsets and
/// anything else whose charge UPower tracks over Bluetooth or USB.
///
/// The command is not free (it talks to a daemon), so it runs at most once every
/// thirty seconds and the parsed list is reused in between. A missing daemon, a
/// timeout or a non-zero exit is one empty list plus one line in Diagnostics --
/// never a retry on the same tick, and never a failure that stops the sample.
///
/// Privacy: UPower identifies a device by an object path that carries the
/// Bluetooth address, and by a serial number. Neither is copied into the model
/// or the log; the id handed out is a hash (<see cref="PeripheralKey"/>). The
/// native path is used in this class only, to recognise a device the sysfs
/// reader already reported.
/// </summary>
public sealed class UPowerReader
{
    public const string Command = "upower";

    /// <summary>Long enough for a busy daemon, short enough not to stall a sample.</summary>
    public const int TimeoutMs = 3000;

    /// <summary>The daemon's device list changes slowly; once per half minute is plenty.</summary>
    public const long IntervalMs = 30_000;

    private static readonly string[] DumpArguments = { "--dump" };

    private readonly ICommandRunner _commands;
    private readonly Diagnostics? _diagnostics;

    private List<UpowerDevice> _devices = new();
    private long _lastRun;
    private bool _ran;
    private bool _failureLogged;

    public UPowerReader(ICommandRunner? commands = null, Diagnostics? diagnostics = null)
    {
        _commands = commands ?? new ProcessCommandRunner();
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// The peripheral batteries to show: UPower's list first, then any sysfs
    /// <c>SCOPE=Device</c> battery UPower does not know about. UPower wins when
    /// both report the same device, because it has the device's model name and
    /// state, and because only one of them names the same hardware twice.
    /// </summary>
    /// <param name="stamp">The sampler's clock, so the 30 s cache is testable.</param>
    /// <param name="sysfs">What <see cref="BatteryReader"/> read this tick.</param>
    public IReadOnlyList<PeripheralBattery> Read(long stamp, PowerSupplyReading? sysfs = null)
    {
        Refresh(stamp);

        IReadOnlyList<Battery> batteries = sysfs?.Batteries ?? Array.Empty<Battery>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var peripherals = new List<PeripheralBattery>(_devices.Count);
        foreach (UpowerDevice device in _devices)
        {
            if (IsSystemBattery(device, batteries))
            {
                continue;
            }

            // Only a device that actually appears may claim its native path: an
            // absent one was dropped at parse time and must not hide a live
            // sysfs peripheral behind the same path.
            if (device.NativePath.Length > 0)
            {
                claimed.Add(device.NativePath);
            }
            peripherals.Add(device.ToPeripheral());
        }

        foreach (SysfsPeripheral device in sysfs?.Devices ?? Array.Empty<SysfsPeripheral>())
        {
            if (!claimed.Contains(device.Name))
            {
                peripherals.Add(device.Device);
            }
        }

        return peripherals;
    }

    /// <summary>
    /// The machine's own packs, which UPower must not report as peripherals.
    /// UPower marks them with <c>power supply: yes</c>; the native path catches a
    /// pack whose mark is missing, and either signal is enough to skip it.
    /// </summary>
    private static bool IsSystemBattery(UpowerDevice device, IReadOnlyList<Battery> batteries)
    {
        if (device.PowerSupply)
        {
            return true;
        }

        if (device.NativePath.Length == 0)
        {
            return false;
        }

        foreach (Battery battery in batteries)
        {
            if (string.Equals(battery.Id, device.NativePath, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Run the command only when the 30 s window has passed.</summary>
    private void Refresh(long stamp)
    {
        bool due = !_ran || stamp < _lastRun || stamp - _lastRun >= IntervalMs;
        if (!due)
        {
            return;
        }

        _ran = true;
        _lastRun = stamp;

        CommandResult? result = _commands.RunResult(Command, DumpArguments, TimeoutMs);
        if (result is null || !result.Value.Ok)
        {
            _devices = new List<UpowerDevice>();
            Report(result);
            return;
        }

        try
        {
            _devices = Parse(result.Value.Output);
            _failureLogged = false;
        }
        catch (Exception)
        {
            _devices = new List<UpowerDevice>();
        }
    }

    /// <summary>
    /// One line, once per failure episode. Nothing from the output is logged:
    /// UPower's text contains device addresses and serial numbers.
    /// </summary>
    private void Report(CommandResult? result)
    {
        if (_failureLogged)
        {
            return;
        }
        _failureLogged = true;

        string reason = result is null
            ? "not available or timed out"
            : $"exited {result.Value.ExitCode.ToString(CultureInfo.InvariantCulture)}";
        _diagnostics?.Line($"upower: {reason}; device batteries are hidden until the next check");
    }

    private static List<UpowerDevice> Parse(string output)
    {
        var devices = new List<UpowerDevice>();
        Block? block = null;

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            string text = line.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (text.StartsWith("Device:", StringComparison.Ordinal))
            {
                Add(devices, block);
                block = new Block { Path = text["Device:".Length..].Trim() };
                continue;
            }

            if (block is null)
            {
                continue;
            }

            if (Indent(line) > 2)
            {
                ReadClassProperty(block, text);
            }
            else if (text.Contains(':'))
            {
                ReadProperty(block, text);
            }
            else
            {
                // "battery", "line-power", "mouse": the class line, alone on its row.
                block.Class = text;
            }
        }

        Add(devices, block);
        return devices;
    }

    private static void ReadProperty(Block block, string text)
    {
        int colon = text.IndexOf(':');
        if (colon < 0)
        {
            return;
        }

        string key = text[..colon].Trim();
        string value = text[(colon + 1)..].Trim();
        switch (key)
        {
            case "native-path":
                block.NativePath = value;
                break;
            case "model":
                block.Model = value;
                break;
            case "power supply":
                block.PowerSupply = string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
                break;
        }
    }

    private static void ReadClassProperty(Block block, string text)
    {
        int colon = text.IndexOf(':');
        if (colon < 0)
        {
            return;
        }

        string key = text[..colon].Trim();
        string value = text[(colon + 1)..].Trim();
        switch (key)
        {
            case "present":
            case "is-present":
                block.Present = !string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);
                break;
            case "state":
                block.State = value;
                break;
            case "percentage":
                block.Percentage = value;
                break;
        }
    }

    private static void Add(List<UpowerDevice> devices, Block? block)
    {
        if (block is null || block.Path.Length == 0)
        {
            return;
        }

        // UPower's synthetic aggregate battery and its AC adapters are not devices
        // a person carries; the sysfs reader reports the adapters as power sources.
        if (block.Path.EndsWith("DisplayDevice", StringComparison.Ordinal)
            || IsLinePower(block))
        {
            return;
        }

        // Present before claiming: an absent entry is discarded here, before
        // de-duplication, so it never takes its native path away from a live
        // sysfs peripheral that a person is actually carrying.
        if (!block.Present)
        {
            return;
        }

        // Everything else is a peripheral, including a "battery"-class device:
        // that class is only a guess about the pack the machine runs on, and a
        // UPower battery whose native path is not a sysfs system battery is a
        // device in its own right. It becomes kind Other unless the type line
        // says more. The machine's own pack is dropped later, by its
        // "power supply: yes" mark or by native path.
        devices.Add(Device(block));
    }

    private static bool IsLinePower(Block block)
    {
        if (block.Class.Equals("line-power", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string name = block.Path.TrimEnd('/');
        int slash = name.LastIndexOf('/');
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }
        return name.StartsWith("line_power", StringComparison.Ordinal);
    }

    private static UpowerDevice Device(Block block) => new(
        PeripheralKey.ForUpower(block.Path),
        block.NativePath,
        block.PowerSupply,
        Kind(block.Class),
        block.Model.Length > 0 ? block.Model : null,
        Percent(block.Percentage),
        State(block.State));

    private static int? Percent(string text)
    {
        string value = text.TrimEnd('%', ' ');
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            && double.IsFinite(number)
                ? Math.Clamp((int)Math.Round(number, MidpointRounding.AwayFromZero), 0, 100)
                : null;
    }

    private static BatteryState State(string state)
    {
        if (string.Equals(state, "charging", StringComparison.OrdinalIgnoreCase))
        {
            return BatteryState.Charging;
        }
        if (string.Equals(state, "discharging", StringComparison.OrdinalIgnoreCase))
        {
            return BatteryState.Discharging;
        }
        if (string.Equals(state, "fully-charged", StringComparison.OrdinalIgnoreCase))
        {
            return BatteryState.Full;
        }
        return BatteryState.Unknown;
    }

    private static PeripheralKind Kind(string deviceClass)
    {
        switch (deviceClass.ToLowerInvariant())
        {
            case "mouse":
                return PeripheralKind.Mouse;
            case "keyboard":
                return PeripheralKind.Keyboard;
            case "headset":
            case "headphones":
            case "headset-headphones":
                return PeripheralKind.Headset;
            case "gaming-input":
            case "joystick":
            case "gamepad":
                return PeripheralKind.Gamepad;
            case "phone":
                return PeripheralKind.Phone;
            case "tablet":
                return PeripheralKind.Tablet;
            default:
                // "battery" is not a peripheral type: a UPower battery that is
                // not the machine's own pack is a device of unknown kind.
                return PeripheralKind.Other;
        }
    }

    private static int Indent(string line)
    {
        int spaces = 0;
        while (spaces < line.Length && line[spaces] == ' ')
        {
            spaces++;
        }
        return spaces;
    }

    /// <summary>One <c>Device:</c> block, before the filters turn it into a model.</summary>
    private sealed class Block
    {
        public string Path = string.Empty;
        public string NativePath = string.Empty;
        public string Model = string.Empty;
        public string Class = string.Empty;
        public string State = string.Empty;
        public string Percentage = string.Empty;
        public bool Present = true;
        public bool PowerSupply;
    }

    /// <summary>What a block means, with the native path kept for de-duplication only.</summary>
    private sealed record UpowerDevice(
        string Id,
        string NativePath,
        bool PowerSupply,
        PeripheralKind Kind,
        string? Model,
        int? Percent,
        BatteryState State)
    {
        public PeripheralBattery ToPeripheral() => new()
        {
            Id = Id,
            Kind = Kind,
            Model = Model,
            Percent = Percent,
            State = State,
        };
    }
}
