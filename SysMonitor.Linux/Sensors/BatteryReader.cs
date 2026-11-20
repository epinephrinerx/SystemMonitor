using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// A battery the kernel reports for a peripheral (<c>SCOPE=Device</c>), before
/// UPower has had its say. The kernel name is kept beside the model so
/// <see cref="UPowerReader"/> can drop the entry when UPower also reports it.
/// </summary>
public sealed record SysfsPeripheral(string Name, PeripheralBattery Device);

/// <summary>One pass over <c>/sys/class/power_supply</c>.</summary>
public sealed record PowerSupplyReading(
    IReadOnlyList<Battery> Batteries,
    IReadOnlyList<PowerSource> PowerSources,
    IReadOnlyList<SysfsPeripheral> Devices)
{
    public static PowerSupplyReading Empty { get; } = new(
        Array.Empty<Battery>(),
        Array.Empty<PowerSource>(),
        Array.Empty<SysfsPeripheral>());
}

/// <summary>
/// The machine's batteries and power sources from <c>/sys/class/power_supply</c>.
///
/// The kernel publishes several shapes for the same thing: energy in µWh or
/// charge in µAh, power in µW or current in µA, and a voltage that may be the
/// present one or the minimum design one. Every reading is converted to Wh, W
/// and V here so nothing downstream has to know which shape this machine
/// happened to use. When a conversion needs a voltage the driver does not
/// publish, the energy and power stay null rather than being guessed.
///
/// Time remaining is smoothed: power readings on a laptop move by watts between
/// ticks, and an unsmoothed division turns that into a time that jumps by
/// hours. The reader keeps one exponential moving average per battery
/// (α = 0.3), restarted whenever the battery changes state.
/// </summary>
public sealed class BatteryReader
{
    /// <summary>Weight of the newest reading in the moving average.</summary>
    public const double SmoothingAlpha = 0.3;

    /// <summary>Below this the load is noise and no time is estimated.</summary>
    public const double MinimumTimePowerW = 0.5;

    /// <summary>A time beyond this is not a reading, it is a stalled battery.</summary>
    public static readonly TimeSpan MaximumTime = TimeSpan.FromHours(48);

    private readonly string _root;

    /// <summary>Per battery: the state the average belongs to and the average itself.</summary>
    private readonly Dictionary<string, (BatteryState State, double? Average)> _smoothing =
        new(StringComparer.Ordinal);

    public BatteryReader(string root = "/")
    {
        _root = string.IsNullOrEmpty(root) ? "/" : root;
    }

    public string Root => _root;

    public PowerSupplyReading Read()
    {
        var batteries = new List<Battery>();
        var sources = new List<PowerSource>();
        var devices = new List<SysfsPeripheral>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string directory in SysFs.Directories(_root, "sys", "class", "power_supply"))
        {
            string name = Path.GetFileName(directory);
            if (!SysFs.IsSafeName(name))
            {
                continue;
            }

            string? type = SysFs.ReadTrimmed(directory, "type");
            if (type is null)
            {
                continue;
            }

            if (string.Equals(type, "Battery", StringComparison.OrdinalIgnoreCase))
            {
                ReadBattery(directory, name, batteries, devices, seen);
                continue;
            }

            PowerSourceKind? kind = SourceKind(type);
            if (kind is PowerSourceKind powerKind)
            {
                sources.Add(new PowerSource(name, powerKind, Online(directory)));
            }
        }

        // A battery that has left the machine must not keep smoothing state.
        foreach (string stale in _smoothing.Keys.Where(key => !seen.Contains(key)).ToList())
        {
            _smoothing.Remove(stale);
        }

        return new PowerSupplyReading(batteries, sources, devices);
    }

    private void ReadBattery(
        string directory,
        string name,
        List<Battery> batteries,
        List<SysfsPeripheral> devices,
        HashSet<string> seen)
    {
        // A bay the firmware reports but with no pack in it is not a battery.
        if (SysFs.ReadInt(directory, "present") == 0)
        {
            return;
        }

        string? scope = SysFs.ReadTrimmed(directory, "scope");
        bool system = scope is null
            || scope.Length == 0
            || string.Equals(scope, "System", StringComparison.OrdinalIgnoreCase);
        if (!system && !string.Equals(scope, "Device", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        long? chargeNowRaw = SysFs.ReadLong(directory, "charge_now");
        long? chargeFullRaw = SysFs.ReadLong(directory, "charge_full");

        double? voltage = Volts(SysFs.ReadLong(directory, "voltage_now"))
            ?? Volts(SysFs.ReadLong(directory, "voltage_min_design"));

        double? energyNow = Micro(SysFs.ReadLong(directory, "energy_now"))
            ?? ChargeEnergy(chargeNowRaw, voltage);
        double? energyFull = Micro(SysFs.ReadLong(directory, "energy_full"))
            ?? ChargeEnergy(chargeFullRaw, voltage);
        double? energyDesign = Micro(SysFs.ReadLong(directory, "energy_full_design"))
            ?? ChargeEnergy(SysFs.ReadLong(directory, "charge_full_design"), voltage);

        long? powerNowRaw = Magnitude(SysFs.ReadLong(directory, "power_now"));
        double? power = Micro(powerNowRaw);
        if (power is null)
        {
            power = ChargeEnergy(Magnitude(SysFs.ReadLong(directory, "current_now")), voltage);
        }

        BatteryState state = State(SysFs.ReadTrimmed(directory, "status"));
        int? percent = Percent(directory, energyNow, energyFull);

        int? cycles = SysFs.ReadInt(directory, "cycle_count");

        // Zero cycles is what a driver that does not count reports, and it is
        // also what a brand-new pack reports. With no other usage history there
        // is no way to tell the two apart, so the count stays unknown.
        if (cycles == 0 && energyNow is null && chargeNowRaw is null)
        {
            cycles = null;
        }

        double? average = Smooth(name, state, power);

        seen.Add(name);
        var battery = new Battery
        {
            Id = name,
            Model = Text(directory, "model_name"),
            Manufacturer = Text(directory, "manufacturer"),
            Technology = Text(directory, "technology"),
            Percent = percent,
            State = state,
            PowerW = power,
            EnergyNowWh = energyNow,
            EnergyFullWh = energyFull,
            EnergyDesignWh = energyDesign,
            CycleCount = cycles,
            VoltageV = voltage,
            TimeToEmpty = TimeToEmpty(state, energyNow, average),
            TimeToFull = TimeToFull(state, energyNow, energyFull, average),
        };

        if (system)
        {
            batteries.Add(battery);
        }
        else
        {
            devices.Add(new SysfsPeripheral(name, new PeripheralBattery
            {
                Id = PeripheralKey.ForSysfs(name),
                Kind = PeripheralKind.Other,
                Model = battery.Model,
                Percent = percent,
                State = state,
            }));
        }
    }

    /// <summary>
    /// One exponential moving average per battery. The first reading in a state
    /// seeds the average; every later one is blended in. A null power reading
    /// clears the average, because there is nothing to blend.
    /// </summary>
    private double? Smooth(string id, BatteryState state, double? power)
    {
        if (power is not double watts)
        {
            _smoothing[id] = (state, null);
            return null;
        }

        double average = _smoothing.TryGetValue(id, out (BatteryState State, double? Average) previous)
            && previous.State == state
            && previous.Average is double value
                ? SmoothingAlpha * watts + (1 - SmoothingAlpha) * value
                : watts;

        _smoothing[id] = (state, average);
        return average;
    }

    private static TimeSpan? TimeToEmpty(BatteryState state, double? energyNow, double? average)
    {
        if (state != BatteryState.Discharging
            || energyNow is not double energy
            || average is not double power
            || power < MinimumTimePowerW)
        {
            return null;
        }

        return Time(energy / power);
    }

    private static TimeSpan? TimeToFull(
        BatteryState state,
        double? energyNow,
        double? energyFull,
        double? average)
    {
        if (state != BatteryState.Charging
            || energyNow is not double energy
            || energyFull is not double full
            || full <= energy
            || average is not double power
            || power < MinimumTimePowerW)
        {
            return null;
        }

        return Time((full - energy) / power);
    }

    /// <summary>Hours to a span, or null for a value that is no estimate at all.</summary>
    private static TimeSpan? Time(double hours)
    {
        if (!double.IsFinite(hours) || hours <= 0 || hours > MaximumTime.TotalHours)
        {
            return null;
        }
        return TimeSpan.FromHours(hours);
    }

    private static int? Percent(string directory, double? energyNow, double? energyFull)
    {
        int? capacity = SysFs.ReadInt(directory, "capacity");
        if (capacity is int reported)
        {
            return Math.Clamp(reported, 0, 100);
        }

        double? fraction = Fraction(energyNow, energyFull)
            ?? Fraction(Micro(SysFs.ReadLong(directory, "charge_now")), Micro(SysFs.ReadLong(directory, "charge_full")));
        return fraction is double value
            ? Math.Clamp((int)Math.Round(value * 100, MidpointRounding.AwayFromZero), 0, 100)
            : null;
    }

    private static double? Fraction(double? part, double? whole) =>
        part is double value && whole is double total && total > 0 ? value / total : null;

    private static BatteryState State(string? status)
    {
        if (string.IsNullOrEmpty(status))
        {
            return BatteryState.Unknown;
        }

        if (string.Equals(status, "Charging", StringComparison.OrdinalIgnoreCase))
        {
            return BatteryState.Charging;
        }
        if (string.Equals(status, "Discharging", StringComparison.OrdinalIgnoreCase))
        {
            return BatteryState.Discharging;
        }
        if (string.Equals(status, "Full", StringComparison.OrdinalIgnoreCase))
        {
            return BatteryState.Full;
        }
        if (string.Equals(status, "Not charging", StringComparison.OrdinalIgnoreCase))
        {
            return BatteryState.NotCharging;
        }
        return BatteryState.Unknown;
    }

    private static PowerSourceKind? SourceKind(string type)
    {
        if (string.Equals(type, "Mains", StringComparison.OrdinalIgnoreCase))
        {
            return PowerSourceKind.Mains;
        }
        if (string.Equals(type, "USB", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "USB_C", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "USB_PD", StringComparison.OrdinalIgnoreCase))
        {
            return PowerSourceKind.Usb;
        }
        return null;
    }

    /// <summary>
    /// A source with no readable <c>online</c> counts as offline: that is what
    /// the kernel reports for an adapter that is not supplying power.
    /// </summary>
    private bool Online(string directory) => SysFs.ReadInt(directory, "online") == 1;

    private static string? Text(string directory, string file) =>
        SysFs.ReadTrimmed(directory, file) is { Length: > 0 } value ? value : null;

    /// <summary>Micro-units to whole units. Zero is a real reading here, negative is not.</summary>
    private static double? Micro(long? micro)
    {
        return micro is long value && value >= 0 ? value / 1_000_000.0 : null;
    }

    /// <summary>Microvolts to volts. Zero volts means "not published", not 0 V.</summary>
    private static double? Volts(long? microvolts)
    {
        return microvolts is long value && value > 0 ? value / 1_000_000.0 : null;
    }

    /// <summary>
    /// µAh at a voltage, in Wh; µA at a voltage, in W. Same factor either way,
    /// and null without a voltage rather than a figure with a guessed one.
    /// </summary>
    private static double? ChargeEnergy(long? micro, double? volts)
    {
        return micro is long value && value >= 0 && volts is double voltage
            ? value / 1_000_000.0 * voltage
            : null;
    }

    /// <summary>A current or power reading is a magnitude; the sign says direction only.</summary>
    private static long? Magnitude(long? value) => value is long number ? Math.Abs(number) : null;
}
