# Handoff — R-030 phase B2 tests

From: Coding Owner · To: Test Author (agy). No shell, no subagents; file
read/write tools only. Behaviour: `Docs/HANDOFF-R030-phaseB2.md` + Amendment 1.

1. Fix the tests that no longer compile because the legacy modes and stand-in
   methods were deleted (read `SysMonitor.Linux/Presentation/ViewState.cs`,
   `WindowGeometry.cs`, `ShellModel.cs` first):
   - `ViewStateTests.ViewModeExtensions_ToKey_And_Parse` (lines ~14–15):
     `overall`/`full` strings still parse to `Main`; no enum values to test.
   - `ViewStateTests.HeaderAndSidebar_DirectNavigationMethods` (~115–138):
     the stand-ins are gone — replace with the real paths (`SwitchTo`,
     `DoubleClick`, `F11`, `Escape`, context-menu methods).
   - `WindowGeometryTests.LimitsAndReferenceSizes_MatchSpec` (~17–30) and
     `Placement_RemembersPositionBeforeFull_UnlessMovedInFull` (~176–194):
     use `ViewMode.Main`.
   - `KillFlowTests.ShellModel_ExposesOneKillFlowInstance_FromConstruction`
     (~1442, 1450): `SwitchTo(ViewMode.Main)`.
2. New tests:
   - `ShellModel.Sidebar` (BuildSidebar) contains a "Modules" heading with the
     six `show_*` checks including `show_battery`, and **no** `cpu_mode`,
     `cpu_temperature`, `disk_mode`, `include_network`, `network_mode`,
     "Full Data" or "Open widget" rows. `SetBool(show_battery, false)` hides the
     battery page and its sidebar item.
   - `SidebarBoard` (Amendment 1): with virtual and network drive groups present,
     items `diskvirtual` and `disknetwork` appear after the physical disks; absent
     when the pages are not built.
   - `ShellModel.MainProcessesVisible` is true only when Main is shown and the
     selected page is `processes`.
Report files changed, count, mismatches.
