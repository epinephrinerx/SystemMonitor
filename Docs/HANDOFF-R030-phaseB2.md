# Handoff — R-030 phase B2: the main window (Avalonia)

From: Coding Owner (Claude Code) · To: Writing Owner 2 (opencode, `opencode-go/deepseek-v4.1-flash`)
Branch: `linux-native` at `ca5af1f` (clean tree). Date: 2026-11-21.
Requirement **R-030**; plan `Docs/PLAN-R030-main-window.md` §2 (layout ASCII),
§2b (page structure). Models from phase B1 are done: `MainState`, `PageKeys`,
`SidebarBoard`, `FullBoard` module pages (`Label`, `Options`, `BigCards` with
`Peak`, `Sensors`, `Properties`), `ShellModel.Main`/`MainSidebar`/
`SelectMainPage`/`SetSettingsOpen`/`SetSidebarWidth`/`SetOption`/`ToggleFullScreen`.

**Scope:** `SysMonitor.Linux.App/` views, plus small `ShellModel`/`ViewState`
cleanups. Do not change readers. Do not edit `SysMonitor.Linux.Tests/` (report
any test that must change). **Never launch the GUI.** The only allowed run is
`--probe-ui` (headless). The Coding Owner will ask the user before launching.

## 1. `MainView` (new `SysMonitor.Linux.App/MainView.cs`) replaces Overall + Full

Two columns: **sidebar** | **right frame**, with a vertical splitter between
them (drag → `SetSidebarWidth`, clamped 200–320, saved on release).

**Header** (top of the right frame or full width — follow the existing Full
header style): title, then buttons **Widget** (→ Widget view), **Full screen**
(→ `ToggleFullScreen`, F11) and **Close** (existing close action). Esc leaves
full screen (B1 `ViewState`).

**Sidebar** (own `ScrollViewer`):
1. Nav items: **Overall**, **Apps**, **Processes** — one row each, highlighted
   when selected (same highlight as the old Full tabs).
2. **Settings** expander (collapsed by default, state from/to
   `SetSettingsOpen`): renders today's Overall sidebar rows from
   `ShellModel.Sidebar` with the existing `RowRenderer`, **minus** the
   per-module option checks that now live on the module pages (`cpu_mode`,
   `cpu_temperature`, `disk_mode`, `include_network`, `network_mode`). The
   `show_*` checks stay, under a "Modules" heading. Remove those rows in
   `ShellModel.BuildSidebar` rather than filtering in the view.
3. A separator, then one **module item** per `ShellModel.MainSidebar` entry:
   title (left) and value (right) on the first line, the device name in small
   label colour on the second, then a sparkline (about 28 px high) of
   `Primary` in the item's accent, with `Secondary` (swap / upload) drawn
   lighter on the same plot. Use `ChartMath`/the existing `ChartControl`
   drawing with **no grid and no frame** (add a flag to `ChartControl` if
   needed). Click → `SelectMainPage(key)`. Selected item highlighted.

**Right frame** — content for `ShellModel.Main` selection:
- `overall` → the existing `OverallView` **cards only** (its own sidebar is no
  longer shown; keep `OverallView` but drop/hide its sidebar column).
- module key → `ModulePageView` (new, §2).
- `processes` → for now, **move** the existing memory-tab process list and the
  Services list (all of `FullView`'s `RenderMemory`, `GroupRow`, `ServiceRow`,
  kill/force/confirm flow, search box) into `ProcessesView` (new file) as two
  tabs "Processes" and "Services", behaviour unchanged. Phase C replaces the
  process list with the new grid; do not redesign it now.
- `apps` → a simple page with the heading and one line "Apps list arrives in
  the next update." (localised; new string keys). Phase C fills it.

## 2. `ModulePageView`

Rendered from the selected `FullTab`, top to bottom, scrolling as one page:
1. **Header:** `Hardware` (device name) large, `Label` small under it.
2. **Options bar:** one compact checkbox per `ModuleOption` (`LabelKey`
   localised, `Value` checked); toggle → `ShellModel.SetOption(ConfigKey, v)`.
   Hidden when there are no options.
3. **Usage:** section title, then the big cards (reuse `FullView.BigCard`,
   moved into a shared helper). Each card's value line shows
   `Value · Peak` (Peak already "Highest: …"). Small cards (per core, per
   engine, per volume) wrap below as today.
4. **Sensors:** section title and the `Sensors` cards; whole section hidden when
   empty.
5. **Properties:** section title, then a two-column list (label left in label
   colour, value right) of `Properties`; `N/A` values in label colour.

Disk pages keep the existing double-click / right-click drive tools on the page
header (see `FullView.OpenDiskTab` / `ShowDriveMenu`).

## 3. Window, modes and cleanup

- `ShellWindow`: Widget and Main only. Main uses `main_*` geometry and the B1
  `WindowGeometry` limits; full screen uses `WindowState.FullScreen` and
  restores the previous state. Double-click on empty space: Widget ↔ Main (as
  B1 `ViewState`). Tray/context menu items follow the B1 transitions.
- **Delete** `FullView.cs` once its pieces have moved, and the legacy
  `ViewMode.Overall/Full` values and stand-in methods from `ViewState` (B1
  deviation). Fix the callers; report tests that referenced them.
- Theme, font scale, opacity and light mode apply to the new views the same way
  they apply today (`UpdateBrushes`, `ScaleFonts`).
- Keep refresh cheap: rebuild controls only on structure change (page set or
  selection changed); on data ticks update texts and charts in place, keyed by
  card/item key (as `FullView.SyncTabs` does).

## 4. `--probe-ui`

Add a "main view" section that builds `MainView` headless as far as the
existing probe does for the old views (it already instantiates views to compare
fonts — follow that pattern) and prints: sidebar width, settings open, the nav
items, the module items in order, and for the selected page the section titles
present. No window is shown.

## Acceptance

1. `dotnet build SysMonitor.Linux.App -c Release` — 0 warnings, 0 errors.
2. `dotnet test` — passes, except tests that referenced the deleted legacy
   modes/methods; list each.
3. `--probe-ui` prints the main view section with 9 module items on this
   machine.
4. GUI never launched by you.

## Report back

Summary, files changed, commands run with results, tests to change, deviations,
risks, and a short **manual test script** (what the Coding Owner should click
and expect) for the hands-on check. No commit.

## Amendment 1 (Coding Owner, 2026-11-21)

1. **Group drive pages reachable.** `SidebarBoard` adds one item for each
   grouped drive page `FullBoard` builds (`diskvirtual`, `disknetwork`), placed
   after the physical disks, titled like the old rail (`Disk · Virtual drives`,
   `Disk · Network drives`), Primary = the group's existing throughput history
   if any (else no sparkline). Hidden when the page is not built.
2. Accepted deviations: probe prints model-derived structure; old "Full Data"
   / "Open widget" rows removed; `show_battery` added under Modules; Overall no
   longer scales with window size; full screen drops the shadow pad.
3. Write scratch files only under `$HOME/.cache/sysmon-wo2/` (not `/tmp`,
   which is a small tmpfs), and delete them when done.

## Amendment 2 (Coding Owner, 2026-11-21) — Reviewer2 round 1

1. **Disk header right-click** must show the drive actions in the menu that
   actually opens (`ModulePageView.cs` ~219): build the items into the attached
   `ContextMenu` (or open one menu on `ContextRequested`), not a second menu
   anchored to the first. Same items as the old `FullView.ShowDriveMenu`.
2. **Full-screen placement.** Remember the normal-window rect **before**
   entering full screen; on exit restore exactly that rect and do not call
   `SavePlacement()` with the full-screen rect. F11 from Widget must go through
   the same Widget→Main placement transition as a double-click (remember the
   Widget origin) before entering full screen.
3. **Properties refresh on data ticks.** `ModulePageView` updates Properties in
   place on each data tick (keyed by property title), not only in `Rebuild()`.
4. **No process sampling while hidden.** `_memoryVisible` (process/service
   sampling) is true only when the window is shown, in Main, on `processes`.
   `HideToTray`/minimise → false; restore → recomputed.

## Amendment 3 (Coding Owner, 2026-11-21) — Reviewer2 round 2

1. **Tray restore keeps full screen.** `RestoreFromTray` restores
   `WindowState.FullScreen` when Main was full screen (`MainFullScreen` /
   `_fullScreenApplied`), else Normal — model and window must agree after
   restore; F11 afterwards toggles correctly.
2. **Properties keyed by position, not title.** Key Properties rows by index
   (or title + occurrence number) so duplicate titles (two "Mouse" rows) update
   in place; rebuild only when the list of titles in order changes.
