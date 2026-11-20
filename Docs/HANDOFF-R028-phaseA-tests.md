# Handoff — R-028 phase A tests

From: Coding Owner · To: Test Author (agy, Gemini 3.8 Flash)

Add MSTest tests to `SysMonitor.Linux.Tests` for phase A. The behaviour is
defined in `Docs/HANDOFF-R028-phaseA.md`. First read these files for the
exact names: `SysMonitor.Core/Model/Battery.cs`,
`SysMonitor.Linux/Sensors/BatteryReader.cs`, `UPowerReader.cs`,
`PeripheralKey.cs`, `SysMonitor.Linux/Sampling/LinuxSampler.cs`,
`SysMonitor.Linux/Presentation/StringData.cs`, `Lang.cs` and `AppConfig.cs`.
Use the existing fake-root helpers (see `BlockDevicesTests` or
`GpuReaderTests`) and `FakeCommandRunner`.

Cover the following.

- **sysfs batteries:**
  - energy_* / power_now in µWh/µW → Wh/W;
  - charge_* / current_now in µAh/µA converted with voltage_now, falling
    back to voltage_min_design;
  - no voltage → energy and power are null;
  - capacity missing → percent computed;
  - health = full / design;
  - `cycle_count` 0 with no energy/charge → null; 0 with readings →
    kept as 0;
  - status mapping, including "Not charging" and an unknown string.
- **Scope:** `SCOPE=Device` → peripheral, not a system battery.
  Mains/USB sources and their `online` value.
- **EMA and time:**
  - α = 0.3 over several samples;
  - reset when the state changes;
  - TimeToEmpty / TimeToFull formulas;
  - < 0.5 W → null; > 48 h → null.
- **UPower parsing** of a `upower --dump` fixture with these blocks:
  DisplayDevice, line_power, battery_BAT0 (matching a sysfs battery),
  a mouse, a keyboard, a `present: no` device and a headset.
  - Expect only the peripherals, with the right kinds, percents and
    states.
  - The MAC address and serial never appear in any field.
  - The `Id` is stable across two parses and does not contain the MAC.
- **UPower cadence and failures:**
  - a second read within 30 s does not run the command again (fake
    clock);
  - after 30 s it runs again;
  - command missing, non-zero exit or timeout → empty list and a single
    Diagnostics line across repeated failures.
- **De-duplication:** a sysfs device-scope peripheral that is also in
  UPower appears once.
- **Snapshot:** `LinuxSampler` with a fake root fills Batteries,
  PowerSources, Peripherals and OnAcPower.
- **Config and strings:**
  - `show_battery` defaults to true and round-trips;
  - every new battery string key exists in Thai and English through
    `Lang`.

Use fakes only: no real `/sys` and no real `upower`. Files only, no
shell, only in `SysMonitor.Linux.Tests/`. Do not commit.

## Round 2 (after Reviewer2 round 1)

- A UPower fixture with a `battery`-class device whose native-path is
  **not** a sysfs battery → it is reported as a peripheral of kind Other.
  A `battery`-class device that matches BAT0 → skipped.
- UPower `present: no` plus a live sysfs device-scope peripheral on the
  same native-path → the sysfs peripheral appears exactly once.
- If the probe JSON writer can be called from tests, assert it has no
  `Id`. Otherwise skip this item and say so.

## Round 3

- `Read_Stability_GeneratesStableIdAcrossParsesWithoutMac` asserts the Id
  does not contain "AA". A 12-hex hash can contain "aa" by chance, so the
  test is flaky. Assert instead that it does not contain any fixture MAC
  (`AA:BB:CC:DD:EE:11`, `…:22`, `…:33`, `…:44`), with or without the colons
  (`AABBCCDDEE11`), ignoring case.
- Add: a `battery`-class device with `power supply: yes` whose native-path
  is not in sysfs → skipped.
