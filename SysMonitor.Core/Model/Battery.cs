namespace SysMonitor.Model;

/// <summary>What a battery is doing right now.</summary>
public enum BatteryState
{
    /// <summary>The driver said nothing recognisable, or said nothing at all.</summary>
    Unknown,
    Charging,
    Discharging,
    Full,

    /// <summary>Plugged in but deliberately not charging (a charge threshold, or a full cell).</summary>
    NotCharging,
}

/// <summary>How a power supply feeds the machine.</summary>
public enum PowerSourceKind
{
    Mains,
    Usb,
    Other,
}

/// <summary>What kind of peripheral a battery belongs to.</summary>
public enum PeripheralKind
{
    Mouse,
    Keyboard,
    Headset,
    Gamepad,
    Phone,
    Tablet,
    Other,
}

/// <summary>
/// One system battery: what it is, what it holds and what it is doing.
///
/// Every field a driver may omit is nullable, and a missing field stays null.
/// Nothing here is a fallback constant: the difference between "0 %" and "the
/// driver did not say" is the whole point, so a consumer prints "unknown"
/// rather than a number nobody measured.
///
/// The cycle count is the one exception where 0 is not trusted: a driver that
/// publishes <c>cycle_count</c> as 0 while reporting no other usage history is
/// indistinguishable from a driver that does not track cycles at all, so the
/// reader reports null instead of inventing a fresh battery (see
/// <c>BatteryReader</c>). A real 0 with history intact is kept as 0.
/// </summary>
public sealed record Battery
{
    /// <summary>The kernel's stable name for the battery, for example <c>BAT0</c>.</summary>
    public required string Id { get; init; }

    public string? Model { get; init; }
    public string? Manufacturer { get; init; }
    public string? Technology { get; init; }

    /// <summary>Charge in whole percent, 0-100, or null when the driver reports no capacity.</summary>
    public int? Percent { get; init; }

    public BatteryState State { get; init; }

    /// <summary>Power flowing in or out, in watts, as a magnitude. Never negative.</summary>
    public double? PowerW { get; init; }

    public double? EnergyNowWh { get; init; }
    public double? EnergyFullWh { get; init; }

    /// <summary>The capacity the battery left the factory with.</summary>
    public double? EnergyDesignWh { get; init; }

    /// <summary>
    /// Wear, as <c>EnergyFull / EnergyDesign × 100</c>. Null unless both figures
    /// are known and the design capacity is above zero.
    /// </summary>
    public double? HealthPercent =>
        EnergyFullWh is double full && EnergyDesignWh is double design && design > 0 && full >= 0
            ? full / design * 100
            : null;

    /// <summary>Charge cycles, or null when the driver does not really track them.</summary>
    public int? CycleCount { get; init; }

    /// <summary>The battery voltage, or null when no voltage file is readable.</summary>
    public double? VoltageV { get; init; }

    /// <summary>Estimated time until empty. Only while discharging on a real load.</summary>
    public TimeSpan? TimeToEmpty { get; init; }

    /// <summary>Estimated time until full. Only while charging on a real load.</summary>
    public TimeSpan? TimeToFull { get; init; }
}

/// <summary>An adapter that can feed the machine: mains, or USB-C power delivery.</summary>
public sealed record PowerSource(string Id, PowerSourceKind Kind, bool Online);

/// <summary>
/// A battery in a peripheral: a wireless mouse, keyboard, headset and so on.
///
/// <see cref="Id"/> is an opaque key derived from a hash, never the device's
/// address or serial number: the model is carried into the UI, the log and the
/// probe output, and a MAC address has no business in any of them.
/// </summary>
public sealed record PeripheralBattery
{
    /// <summary>A stable, opaque key for this device. Not a MAC address and not a serial.</summary>
    public required string Id { get; init; }

    public PeripheralKind Kind { get; init; } = PeripheralKind.Other;

    public string? Model { get; init; }

    /// <summary>Charge in whole percent, 0-100, or null when the source reports none.</summary>
    public int? Percent { get; init; }

    public BatteryState State { get; init; }
}
