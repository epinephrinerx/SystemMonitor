# Handoff — R-030 phase B1 tests

From: Coding Owner · To: Test Author (agy, Gemini 3.8 Flash)
You have NO shell and must not start subagents. Use only file read/write tools.
Behaviour: `Docs/HANDOFF-R030-phaseB1.md` including Amendment 1 and the WO2
deviations listed there. Read first:
`SysMonitor.Linux/Presentation/ViewState.cs`, `MainState.cs`, `SidebarBoard.cs`,
`FullBoard.cs`, `FullModel.cs`, `DiskAlerts.cs` (HistoryBoard), `AppConfig.cs`,
`WindowGeometry.cs`, `StartupOptions.cs`, `ShellModel.cs`, `StringData.cs`.

## 1. Update the 11 tests broken by the intended change

`AppConfigTests.SaveAndLoad_RoundTrip_InCustomPath`,
`FullBoardTests.Build_TabsOrderAndKeys_RootDiskFirst_InternalThenRemovable`,
`RestartTests.StartupOptions_StartView_FullWinsOverExpanded`,
`ViewStateTests` (`ViewModeExtensions_ToKey_And_Parse`,
`DoubleClick_FollowsSpecSwitchingCycle`, `F11_StepsToFull_AndBackToOverall`,
`Escape_StepsBackOneLevel_AndIsNoOpInWidget`,
`ContextMenu_ExpandAndFullScreen_FollowSpec`,
`HeaderAndSidebar_DirectNavigationMethods`,
`ViewState_Properties_ReflectCurrentMode`),
`WindowGeometryTests.LimitsAndReferenceSizes_MatchSpec`.
Rewrite each to the new behaviour (rename the method when its name no longer
fits). Do not delete a test without replacing what it covered.

## 2. New tests (new files, e.g. `MainStateTests.cs`, `SidebarBoardTests.cs`, `ModulePageTests.cs`, `ConfigMigrationTests.cs`)

- **ViewState:** Widget ↔ Main by double-click; F11 from Widget → Main + full
  screen; F11 in Main toggles full screen; Esc: full screen off first, then
  Main → Widget; Esc in Widget no-op.
- **MainState:** select/reconcile (vanished module → `overall`; `apps`,
  `processes` always valid); sidebar width clamp 200/320; settings open flag.
- **Config migration:** `last_view` `overall`/`full` → `main`; `full_tab`
  `disk:nvme0n1` → `main_page` when `main_page` absent; existing `main_page`
  wins; Full geometry seeds `main_*` once; `full_tab` not written on save;
  unknown/garbage `main_page` → `overall`; `sidebar_width` 999 → 320.
- **PeakTracker:** reset on page select; highest since reset; first build after
  reset uses the history max.
- **Module pages (FullBoard):** CPU options exactly `cpu_mode`,
  `cpu_temperature`; Disk `disk_mode`, `include_network`; Network
  `network_mode`; Memory/GPU/Battery none (Amendment 1). Memory page: swap chart
  present with swap, absent without; RAM properties from a `MemoryLayout`
  (slots "2 of 2", speed text with rated, no serial anywhere), empty layout →
  "Hardware details not available"; every null property → `N/A`; Network page
  has no MAC row; battery page appears per battery and not when none or
  `ShowBattery` false; process list is no longer on the memory page.
- **SidebarBoard:** order CPU, Memory, GPUs, Disks, Networks, Batteries;
  Memory Secondary = swap history; Network Primary/Secondary down/up; titles
  like `GPU · <short>`; hidden modules (`ShowGpu=false`) not present.
- **SetOption:** `ShellModel.SetOption("cpu_mode", …)` changes the config the
  same way the old sidebar check did (assert on the config value and that a
  save is requested if the model exposes that).
- **Power-source labels** localised (Amendment 1 item 3), Thai and English.

Report: files changed/added, test count, mismatches.

## Round 2 — compile errors (Coding Owner ran dotnet test)

Your tests used members that do not exist. Read the real types before writing:
`SysMonitor.Core/Model/Battery.cs` (`PowerSource` is a positional record
`(string Id, PowerSourceKind Kind, bool Online)`; kinds are `Mains, Usb, Other`;
`Battery.HealthPercent` is computed from the energy fields — set
`EnergyFullWh`/`EnergyDesignWh` instead; `Snapshot.OnAcPower` is derived from
`PowerSources` — set those instead), `SysMonitor.Core/Model/MemoryDetail.cs`
(`MemoryModule` has **no** serial field by design — to prove "no serial
anywhere", put the serial text into the udev fixture and parse it with
`MemoryModules`, or simply assert no property value equals it),
`SysMonitor.Core/Model/Snapshot.cs` (`Adapter` has **no** `Mac` — assert the
page has no MAC row instead).

```
ModulePageTests.cs(107): 'Adapter' does not contain 'Mac'
ModulePageTests.cs(116-118): PowerSource needs (Id, Kind, Online)
ModulePageTests.cs(27): Battery.HealthPercent is read only
ModulePageTests.cs(334-335): PowerSourceKind has no 'Battery'
ModulePageTests.cs(56): MemoryModule has no 'Serial'
SidebarBoardTests.cs(104): Snapshot.OnAcPower is read only
```

## Round 3 (after Amendment 2)

- Migration: `full_tab` `diskvirtual` and `disknetwork` (no `main_page`) →
  `main_page` keeps them; a save/load round-trip keeps them.
- Reset window size: after `MainW/MainH` are set, the reset action clears
  them (and `FullW/FullH`).
- GPU history: two snapshots with GPUs A and B, then a snapshot with only B →
  B's history holds only B's values and B's page key is unchanged; A's series
  is gone.

Member names (from WO2): `PageKeys.IsPageKey(string?)`, `PageKeys.Gpu(Gpu, int)`,
`PageKeys.IsGpuKey`, `PageKeys.DiskVirtual`, `PageKeys.DiskNetwork`,
`HistoryBoard.GpuUsage(string key)` / `GpuEncoder` / `GpuDecoder` / `GpuMemory`
/ `GpuTemp` / `Engine(string gpuKey, string name)`, `ShellModel.ResetSize()`.
GPU page keys are `gpu:<pci address>`.

## Round 4 (after Amendment 3)

- Replace `PageKeys_Gpu_WithoutPciSlot_FallsBackToPositionalKey` and the
  matching HistoryBoard fallback test: a GPU with no PciSlot but a DeviceId →
  `gpu:dev:<id>`; neither → `gpu:name:<Name>#0`, a second same-name GPU `#1`;
  snapshots `[PCI A, addressless B]` then `[B]` → B's key and history unchanged.
- `GpuReader`: `DeviceId` from the resolved `device` link basename (fake root
  with a symlink).
