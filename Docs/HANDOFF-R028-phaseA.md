# Handoff — R-028 phase A: battery model, readers and probe

From: Coding Owner (Claude Code) · To: Writing Owner 2 (opencode, `opencode-go/deepseek-v4.1-flash`)
Branch: `linux-native` at `965c0f2`. The working tree also holds the uncommitted
font follow-up under review: **do not touch `SysMonitor.Linux.App/`**.
Date: 2026-11-20. Requirement: `Requirements.md` **R-028** (and R-029 on `main`
will reuse the Core model, so keep it platform-neutral).

## Objective

Read the system battery, the power sources and the peripheral (Bluetooth)
batteries, put them into the snapshot, and print them in
`SysMonitor.Linux.Probe`. The views come in phase B.

## 1. Core model (`SysMonitor.Core/Model/Battery.cs`), platform-neutral

- `enum BatteryState { Unknown, Charging, Discharging, Full, NotCharging }`
- `sealed record Battery`:
  - identity: `Id` (e.g. `BAT0`), `Model`, `Manufacturer`, `Technology`;
  - state: `Percent` (0–100), `State`;
  - power: `PowerW` (≥ 0, the magnitude);
  - energy: `EnergyNowWh`, `EnergyFullWh`, `EnergyDesignWh`;
  - derived: `HealthPercent` (`EnergyFull / EnergyDesign × 100`);
  - other: `CycleCount`, `VoltageV`;
  - time: `TimeToEmpty`, `TimeToFull` (`TimeSpan?`).

  Every field that may be missing is nullable. **Never invent a
  value.** A cycle count of 0 when the driver also reports no other
  history means unknown, so use `null`. Document that rule.
- `sealed record PowerSource(string Id, PowerSourceKind Kind, bool Online)`,
  with `PowerSourceKind { Mains, Usb, Other }`.
- `sealed record PeripheralBattery`:
  - `Id`: an opaque, stable key that is **not** the MAC address; use a
    hash of the UPower object path;
  - `Kind`: Mouse, Keyboard, Headset, Gamepad, Phone, Tablet or Other;
  - `Model`, `Percent?` and `State`.
- `Snapshot` gains `Batteries`, `PowerSources`, `Peripherals` (empty
  lists by default) and `bool OnAcPower` (derived).

## 2. System battery reader (`SysMonitor.Linux/Sensors/BatteryReader.cs`)

- Read `/sys/class/power_supply/*` through the existing `SysFs` root, so
  fake roots work.
- `TYPE=Battery` with `SCOPE` absent or `System` is a system battery.
  `SCOPE=Device` is a peripheral. Report it as a `PeripheralBattery`,
  and de-duplicate it against UPower, which wins.
- `TYPE=Mains`, `USB`, `USB_C` or `USB_PD` is a `PowerSource`, using
  `online`.
- **Units.** sysfs gives µWh, µW, µV and µAh/µA.
  - If `energy_*` or `power_now` exist, use them.
  - Otherwise convert `charge_*` and `current_now` with `voltage_now`,
    falling back to `voltage_min_design`.
  - If no voltage exists, leave energy and power null rather than
    guessing.
  - `capacity` gives the percent. If it is missing, compute it from
    `energy_now / energy_full`.
- **State.** Map `status`: `Charging`, `Discharging`, `Full` and
  `Not charging` map to their states; anything else is `Unknown`.
- **Time remaining.** Smooth `PowerW` with an exponential moving average
  (α = 0.3) in the reader instance. Reset it when the state changes.
  - Discharging: `TimeToEmpty = EnergyNow / avgPower`.
  - Charging: `TimeToFull = (EnergyFull − EnergyNow) / avgPower`.
  - Only when `avgPower ≥ 0.5 W`. Cap the result at 48 h; above that,
    report null.
- Read cadence: every sample. These are a handful of tiny sysfs reads.

## 3. Peripheral reader (`SysMonitor.Linux/Sensors/UPowerReader.cs`)

- Run `upower --dump` through `ICommandRunner` (argv, 3 s timeout), at
  most **every 30 s**. Cache the result in between.
- Parse the device blocks. Skip `DisplayDevice`, `line_power_*` and any
  device whose `native-path` matches a system battery already read from
  sysfs.
- Keep the kind (from the type line: mouse, keyboard, …), `model`,
  `percentage`, `state`, and `present`/`is-present`. Ignore `serial` and
  `native-path` for output.
- If `upower` is missing, times out or exits non-zero, return an empty
  list, log once to Diagnostics, and retry at the next 30 s mark.
- **Privacy:** never put a MAC address or serial into the model, the log
  or the probe.

## 4. Sampler and config

- `LinuxSampler` fills the new snapshot fields. Battery reading is
  always on: it is cheap.
- `AppConfig` gains `show_battery` (default **true**), read and written
  like the other `show_*` keys. Phase B uses it.
- New strings in `StringData.LinuxExtras` (Thai + English) for phase B:
  - `battery`, `battery_health`, `battery_cycles`, `battery_power`;
  - `battery_time_left`, `battery_time_to_full`;
  - `battery_charging`, `battery_discharging`, `battery_full`,
    `battery_not_charging`;
  - `battery_on_ac`, `battery_on_battery`, `battery_devices`,
    `show_battery`, `unknown`.

  If `unknown` already exists, reuse it.

## 5. Probe

`SysMonitor.Linux.Probe` prints a Battery section:
- every field of each system battery, with "unknown" for null;
- the power sources;
- the peripherals: kind, model, percent and state, with no ids, MACs or
  serials;
- `OnAcPower`.

Three samples 1 s apart, as already done, so the EMA shows.

## Acceptance

1. `dotnet build` of `SysMonitor.Linux` and of the Probe: 0 warnings.
   `dotnet test SysMonitor.Linux.Tests` still passes. Do not edit tests;
   the Test Author adds them.
2. Probe output pasted. Expected on this machine:
   - BAT0 AP18C4K, 100 %, Full, health ≈ 69 %, 35 cycles, ≈ 12.1 V;
   - ADP1 Mains online, and the USB-C source online;
   - a LOGI M240 mouse at about 80 %, plus a keyboard.
3. Never launch the GUI app. No screenshots. Do not touch
   `SysMonitor.Linux.App/` or `~/.local/share/SystemMonitor`. Do not
   commit.

## Scope

`SysMonitor.Core/Model/**`, `SysMonitor.Linux/**` (not `.App`),
`SysMonitor.Linux.Probe/**`.

## Report back

```
## Summary
## Changed
## Verification   (build; tests; probe Battery section)
## Deviations / Unclear
## Open questions / risks
```

## Amendment 1 (Coding Owner, after Reviewer2 round 1)

All three findings are accepted.

1. **Do not drop a UPower `battery`-class device by its class.** Skip a
   UPower device only if its `native-path` matches a system battery read
   from sysfs, or if it is `DisplayDevice` or `line_power_*`. Any other
   `battery`-class device that is present becomes a peripheral of kind
   Other.
2. **Present before claiming.** A UPower entry with `present: no` (or
   `is-present: no`) is discarded **before** de-duplication. It must not
   claim the native path, so a live sysfs peripheral on that path still
   appears.
3. **No ids in any probe output.** The JSON output of
   `SysMonitor.Linux.Probe` leaves out `PeripheralBattery.Id`, just as the
   text output does.

## Amendment 2 (Coding Owner)

UPower marks the machine's own batteries with `power supply: yes`. Skip a
UPower device if **either** its native-path matches a sysfs system battery
**or** it has `power supply: yes`. Devices with `power supply: no` and a
`battery` class stay as peripherals of kind Other. Existing tests that
call the reader without sysfs names rely on this.
