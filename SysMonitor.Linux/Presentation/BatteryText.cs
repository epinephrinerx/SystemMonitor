using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The battery wording the Overall card and the Widget page share (R-030 fix 1).
///
/// The two views say the same thing about the same battery, so the readout, the
/// one detail line and the power badge live here rather than in each builder.
/// The strings come from the R-028 keys; a field the driver did not report is
/// left out instead of being printed as a zero nobody measured.
/// </summary>
internal static class BatteryText
{
    /// <summary>"94%" or the localised n/a when the driver reports no charge.</summary>
    internal static string Charge(Battery battery, Lang lang) =>
        battery.Percent is int percent ? Format.Percent(percent) : lang["na"];

    /// <summary>
    /// "Charging · Power 12.3 W · Time left 2h 10m · Battery health 94%".
    /// State first, then every figure that was actually read; time to empty and
    /// time to full never both apply, so at most one of them appears.
    /// </summary>
    internal static string Detail(Battery battery, Snapshot snapshot, Lang lang)
    {
        string? time = battery.TimeToEmpty is { } empty
            ? $"{lang["battery_time_left"]} {Format.Duration(empty)}"
            : battery.TimeToFull is { } full
                ? $"{lang["battery_time_to_full"]} {Format.Duration(full)}"
                : null;

        return Format.Join(
            FullBoard.BatteryStateText(battery.State, lang),
            battery.PowerW is double watts
                ? $"{lang["battery_power"]} {Format.Watts(watts)}"
                : null,
            time,
            battery.HealthPercent is double health
                ? $"{lang["battery_health"]} {Format.Decimal(health, 0)}%"
                : null);
    }

    /// <summary>
    /// The power badge while the cell charges or a power source feeds the
    /// machine, and nothing otherwise. It is a power-kind badge, so the view
    /// draws the lightning icon without the thermometer the temperature badge
    /// uses, in the battery accent.
    /// </summary>
    internal static TempBadge? AcBadge(Battery battery, Snapshot snapshot, Palette palette) =>
        battery.State == BatteryState.Charging || snapshot.OnAcPower
            ? new TempBadge(
                Accents.Battery,
                palette.BadgeOk,
                string.Empty,
                false,
                false,
                BadgeKind.Power)
            : null;

    /// <summary>"Mouse", "Keyboard", …: what kind of device a peripheral is.</summary>
    internal static string PeripheralName(PeripheralKind kind, Lang lang) => kind switch
    {
        PeripheralKind.Mouse => lang["periph_mouse"],
        PeripheralKind.Keyboard => lang["periph_keyboard"],
        PeripheralKind.Headset => lang["periph_headset"],
        PeripheralKind.Gamepad => lang["periph_gamepad"],
        PeripheralKind.Phone => lang["periph_phone"],
        PeripheralKind.Tablet => lang["periph_tablet"],
        _ => lang["periph_other"],
    };
}
