# Handoff — R-030 phase B1: main-window presentation models (no Avalonia)

From: Coding Owner (Claude Code) · To: Writing Owner 2 (opencode, `opencode-go/deepseek-v4.1-flash`)
Branch: `linux-native` at `1ab4644` (clean tree). Date: 2026-11-21.
Requirement **R-030**; plan `Docs/PLAN-R030-main-window.md` §2, §2b, §6 (phase B).

**Scope:** `SysMonitor.Linux/Presentation/` (+ Core model only if needed) and
`SysMonitor.Linux.App/Probe.cs` (the `--probe-ui` dump). The Avalonia window
itself is phase B2 — do **not** change `ShellWindow.cs`, `FullView.cs`,
`OverallView.cs` or other views beyond what is needed to **keep the app
compiling** (e.g. a renamed enum value). Do not edit `SysMonitor.Linux.Tests/`.
Existing tests that break because of an intended change (e.g. `ViewState`
modes) are expected; list them in your report for the Test Author.

## 1. View modes and switching — `ViewState.cs`

- `ViewMode { Widget, Main }`. `Parse`: `"main"`, `"overall"`, `"full"` →
  `Main`; `"widget"` → `Widget`; else fallback. `ToKey`: `main` / `widget`.
- `MainFullScreen` is a separate bool on `ViewState` (F11 toggles it while in
  Main; F11 from Widget → Main + full screen; Esc leaves full screen, and from
  Main without full screen → Widget; Esc in Widget is a no-op).
- Double-click: Widget ↔ Main. Context menu: "expand"/"collapse" Widget ↔ Main,
  "full screen" toggles Main full screen. Header buttons: Widget, full screen.
- Remove the Overall/Full-specific transitions (`OverallHeaderFull`,
  `FullRail*`, `Sidebar*` …) and give the views compiling stand-ins only as far
  as needed; B2 replaces the views.

## 2. Main-page state — new `MainState.cs`

- Page keys: `overall`, `apps`, `processes`, and the module keys already used by
  `FullBoard` tabs (`cpu`, `ram`, `disk:<name>`, `gpu:…`, `net:…`, and the
  battery key below). `MainState.Select(key)`; `Reconcile(pages, remembered)`
  like `FullSelection.Reconcile`: a vanished module falls back to `overall`.
- `SettingsOpen` (bool) and `SidebarWidth` (int, clamped 200–320, default 230).
- Config keys in `AppConfig` (default, round-trip, unknown → default):
  `main_page` (`overall`), `settings_open` (`false`), `sidebar_width` (`230`).
- **Migration** (on load, when `main_page` is absent): `last_view` `overall`
  or `full` → `last_view = main`; `full_tab` (if non-empty) → `main_page`. The
  main window geometry key reuses the old **Full** geometry (find the existing
  geometry keys in `AppConfig`/`WindowGeometry`; add `main_*` keys, seeded
  from the Full ones once). Keep reading the old keys; stop writing
  `full_tab`.

## 3. Module pages — extend `FullBoard` / `FullModel`

Keep `FullBoard.Build` as the single builder (rename only if trivial). Each
`FullTab` becomes a module page with the §2b structure:

- `string Label` — e.g. `GPU 1`, `Disk 2`, `CPU`, `Memory`, `Wi-Fi`.
- `IReadOnlyList<ModuleOption> Options` — `ModuleOption(string ConfigKey,
  string LabelKey, bool Value)`:
  - CPU: `cpu_mode` (checked when per-core), `cpu_temperature`, `temp_estimate`;
  - Disk: `disk_mode`, `include_removable`, `include_network`;
  - Network: `network_mode`, `include_wireless`;
  - others: none. Add `ShellModel.SetOption(configKey, bool)` that changes the
    config exactly as the old sidebar check did (same save + refresh path), so
    every view updates at once. Reuse existing string keys for labels where they
    exist; new ones in a new `StringData` table (Thai + English), e.g.
    `opt_logical_cpus` = "Show usage of logical CPUs".
- `BigCards` = the **Usage** charts; each `FullCard` gains `string Peak`
  (`Highest: <value>` formatted like `Value`). Charts per §2b:
  - CPU: total; per-core small cards with `Mhz` in their detail;
  - Memory: Memory and **Swap** (`used / total · %`; no swap → no chart and a
    `Properties` row "Swap: none");
  - GPU: usage, video encoder/decoder (when not null), video memory
    `used / total · %`;
  - Disk: activity %, read speed, write speed (separate), per-volume cards stay
    as small cards;
  - Network: download and upload (separate);
  - Battery: charge %, power W.
- `IReadOnlyList<FullCard> Sensors` — temperature charts with peak: CPU temp,
  GPU temp, disk temp (when the existing data has one). Empty when no sensor.
- `IReadOnlyList<FullFact> Properties` — the §2b fields from phase A2 and the
  existing facts, **every** value null/empty → `N/A` (localised key `na`).
  Memory: the RAM properties card from `Snapshot.MemoryLayout` (slots n of m,
  max capacity, per module: slot, size, type + detail, form factor, speed
  `2667 MT/s (rated 4267)`, maker, rank, voltage) and the swap devices; no DMI
  data → one row "Hardware details not available". Network: MAC row is not
  shown at all.
- **Memory page drops its process list** (it moves to Processes in phase C):
  stop passing `MemoryTabModel` into the RAM tab, keep the type for now.
- **Battery page** (R-028 phase B lands here): key `battery:<Id>` per
  `Snapshot.Batteries` (hidden when none or `show_battery` false); Properties:
  state, health %, energy now/full/design Wh, cycles, technology, manufacturer,
  model, voltage, time to empty/full, power sources; peripheral batteries as
  extra rows (kind, model, percent).
- **Peaks:** a `PeakTracker` keyed by card key: `Reset()` when the page is
  selected (MainState.Select), then `Highest` = max of values seen since; for
  the first build after reset use the visible history's max. Pure and testable.

## 4. Histories — `HistoryBoard`

Add the series the new charts need, all 72 points, pushed in `Push(snapshot)`:
swap %, per-GPU usage/encoder/decoder/memory % and temperature (keyed by GPU
key), CPU temperature, per physical disk activity / read / write (keyed by
disk key), per adapter down / up, per battery charge % and power W. Reuse
existing ones where they already hold the same data.

## 5. Sidebar model — new `SidebarBoard.cs`

`SidebarBoard.Build(FullModel modules, Snapshot?, HistoryBoard, Lang)` →
`IReadOnlyList<SidebarItem>`:
`SidebarItem(string Key, string Title, string Value, string Device, Colour
Accent, History Primary, History? Secondary)`:
- Title `module · short device name` (e.g. `GPU · MX250`), Value on the right
  (CPU `14 %`, Memory `4.0/7.4 GB`, GPU `0 %`, Disk `R 3 KB/s · W 5 KB/s`,
  Network `↓ … ↑ …`, Battery `100 % ⚡` when on AC), Device = full name.
- Memory: Primary = RAM %, Secondary = swap %. Network: Primary down, Secondary up.
  Disk: Primary activity, Secondary none.
- Order: CPU, Memory, GPUs, Disks, Networks, Batteries — same as the tabs.

## 6. `--probe-ui`

`SysMonitor.Linux.App/Probe.cs`: print the sidebar items and, for every module
page, the Label, Options (with values), Usage cards (value + peak), Sensors and
Properties. Keep it headless (no window). The probe's privacy rules apply (no
MAC, SSID `(hidden)`, `Privacy.MaskInterface` for names).

Also fix the legacy RAM summary line in `SysMonitor.Linux.Probe` that prints
`Modules: N/A (0 slots)`: take it from `Snapshot.MemoryLayout`.

## Acceptance

1. `dotnet build SysMonitor.Linux.App -c Release` — 0 warnings, 0 errors; the
   app still starts into the existing views (you may not launch it; compile only).
2. `dotnet test` — all pass except tests that assert the removed Overall/Full
   transitions or old config behaviour; list each with the reason.
3. `--probe-ui` on this machine prints the sidebar (CPU, Memory, 2 GPUs, 2
   disks, 2 adapters, BAT0) and every page with N/A where unknown.
4. Never launch the GUI; only `--probe-ui` (headless) is allowed.

## Report back

Summary, files changed, commands run with results, broken tests with reasons,
deviations, risks. No commit.

## Amendment 1 (Coding Owner, 2026-11-21)

1. **No dead toggles.** `include_removable`, `include_wireless` and
   `temp_estimate` have no consumer on Linux (verified on `1ab4644`). Do not
   show them as module options. The option lists become:
   CPU: `cpu_mode`, `cpu_temperature` · Disk: `disk_mode`, `include_network` ·
   Network: `network_mode` · others: none. Keep the config keys as they are.
2. Accepted deviations: legacy enum values/stand-ins until B2; `14%` style;
   conditional GPU rows; totals as Properties; disk temperature history;
   `full_w/full_h` still written; Probe RAM line fix.
3. Localise power-source kinds (`mains` → "AC adapter"/"อะแดปเตอร์", `usb` →
   "USB", other → "Other"/"อื่น ๆ") with new keys in the B1 string table.

## Amendment 2 (Coding Owner, 2026-11-21) — Reviewer2 round 1

1. **Migration keeps every real page key.** `IsPageKey` must accept every key
   `FullBoard` can produce, including `diskvirtual` and `disknetwork` (and any
   other grouped-drive or extension key — derive the rule from `FullBoard` /
   `FullSelection.ExtensionPrefix`, do not hand-list a subset). An old
   `full_tab` that is a valid key must survive into `main_page`.
2. **Reset window size resets Main.** The reset action clears `MainW`/`MainH`
   (and keeps clearing the old Full keys), so Main returns to its default size
   now and after restart.
3. **GPU histories keyed by device, not index.** Key every per-GPU series by a
   stable GPU identity (its PCI address / the page key), like disks and
   adapters already are; drop series for GPUs that disappeared. The page key
   (`gpu…`) must also be stable when another GPU vanishes.

## Amendment 3 (Coding Owner, 2026-11-21) — Reviewer2 round 2

GPU identity without a PCI address must not be positional:
1. `Gpu` gains `string DeviceId`: `GpuReader` sets it to the basename of the
   card's resolved `device` link (e.g. `0000:00:02.0` on PCI, `fde60000.gpu`
   on a platform device). Read once per card.
2. `PageKeys.Gpu`: `gpu:<pci slot>` if PciSlot is set; else
   `gpu:dev:<DeviceId>` if set; else `gpu:name:<Name>#<n>` where `n` counts
   only earlier GPUs **with the same Name** in the list (0-based). The `index`
   parameter is no longer used for the key (remove it if no caller needs it).
   `IsGpuKey`/`IsPageKey` accept all three shapes; the legacy `gpu<N>` shape
   is still accepted for old configs (it simply reconciles to `overall`).
3. History/pruning unchanged otherwise (keyed by the page key).
