# Handoff — tests for R-030 B2 hands-on fixes 1–3

From: Coding Owner · To: Test Author (agy). No shell, no subagents; file
read/write tools only. Behaviour: `Docs/HANDOFF-R030-B2-fix1.md`, `fix2.md`,
`fix3.md`. Read first: `SysMonitor.Linux/Presentation/OverallBoard.cs`,
`WidgetBoard.cs`, `Models.cs`, `BatteryText.cs` (or wherever the battery text
helpers are), `Palette.cs`, and the existing `OverallBoardTests.cs`,
`WidgetBoardTests.cs`. Check every member against the real code.

Add new test files (e.g. `BatteryOverallWidgetTests.cs`):
1. Overall: Battery section after network, key `battery`, only when
   `ShowBattery` and batteries exist; one row per battery then one per
   peripheral; `ValueText` `NN%`, null percent → `N/A` and bar 0; detail omits
   null fields; charging/on-AC → a **power** badge (fix 3 badge kind), no badge
   when discharging off AC; temperature badges elsewhere keep the temperature kind.
2. Widget: one Battery page per battery after the network pages, key
   `battery:<Id>`; none when `ShowBattery = false` or no batteries.
3. Battery text helpers: time-to-empty vs time-to-full precedence, null
   omission, AC badge truth table.
(Fix 2 is view layout only; no unit test.)
Report files added, count, mismatches.

## Round 2 (Coding Owner ran dotnet test: 1 failed)

`BatteryOverallWidgetTests.Widget_BatteryPages_PositionedAfterNetworkPages`
hard-codes page keys (`cpu`, `ram`, `disk`, `net`) but the real keys differ
(first page is `cpu:0-1`). Rewrite it to assert only what the handoff says: the
battery page(s) come after the last network page (find by `Kind`), key
`battery:<Id>`, Kind Battery. Do not hard-code other pages' keys.

## Round 3 — fixes 4 and 5 (`fix4.md`, `fix5.md`)

New file `SysMonitor.Linux.Tests/B2Fix45Tests.cs`. Read first
`SysMonitor.Linux/Presentation/FullBoard.cs`, `SidebarBoard.cs`, `ShellModel.cs`,
`Models.cs`, the language table, and the existing `ShellModelTests.cs`,
`FullBoardTests.cs`, `SidebarBoardTests.cs`.
1. CPU page small core cards: `Title == "C0"`…; value contains `NN%`, `NN°C`
   and `1.2 GHz` when `Core.Mhz = 1200`; no `GHz` when Mhz is null.
2. Battery page summary/value and sidebar battery value contain no `⚡`; badge
   is `BadgeKind.Power` while charging / on AC, null when discharging off AC.
3. Settings expander: the row that opens the Settings window is labelled
   `More settings…` (en) / `ตั้งค่าเพิ่มเติม…` (th); key `settings_more` exists
   in both languages.
4. Fix 5: `ShellModel` sidebar/expander rows contain no Restart, Manual or
   About rows any more, while it still has the window heading, the six
   `show_*` checks, auto-check-updates, OpenSettings and the Updates action.
