# System Monitor 4.3.0 (WPF) — UI/behaviour spec for an Avalonia/Linux rebuild

Source: branch `main`, `SysMonitor.Wpf/*` (read with `git show main:<path>`). All values are taken from the code.
Where the WPF README contradicts the code, the code wins (noted inline). "Unclear" means the code did not settle it.
Units: px = WPF device-independent pixels (1/96 in). Fonts are pt-like WPF units (same as px).

---

## 0. Global structure

- One main window (`MainWindow`) with three views stacked in one panel; only one is visible: **Widget**, **Overall**, **Full** (enum `Mode`). The old names were mini / expanded / full screen.
- Secondary windows (`PanelWindow`): **Settings**, **Manual**, **About**. Each has normal OS chrome.
- Sampler (background) publishes an immutable `Snapshot` on each metrics interval. A UI timer polls it every **400 ms**. It redraws only when the snapshot reference changed or a widget rotation is due.
- Graph history is pushed on **every fresh snapshot, whatever view is showing**, so the Full view always opens with history.
- Heartbeat timer: every **10 min** (`--heartbeat <seconds>` overrides it). It writes a diagnostics line and trims the working set. Windows-only part: skip the trim.

### Command-line switches
| switch | effect |
|---|---|
| `--expanded` | start in Overall |
| `--full` | start in Full (wins over `--expanded`) |
| `--allow-multiple` | skip the single-instance guard |
| `--heartbeat <s>` | heartbeat interval in seconds |
| `--elevated-op ...` | Windows-only helper mode (UAC). Skip |
| extension approval switch | Windows-only elevated helper. Skip |

Startup view = `--full` / `--expanded`, otherwise `config.last_view` ("widget" | "overall" | "full"; anything else = widget). A non-widget view is applied on `Loaded`.

---

## 1. Window chrome (main window)

- `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`, `ResizeMode=NoResize` (resizing is done by the app's own code), `ShowInTaskbar=False`.
- `Topmost = config.always_on_top` (default **false**).
- **Shadow pad**: the window is **12 px** larger than the panel on every side (`ShadowPad=12`). Window size = panel size + 24. That margin has a `Transparent` background so it takes input and does not click through. No drop shadow is drawn (it was removed for CPU cost).
- **Panel** (`Border x:Name=Panel`): margin 12, `CornerRadius` **14** (**0** in Full view), background = `PanelBrush` (panel colour with opacity alpha), border = `EdgeBrush` (palette Border), thickness 1.
- **Opacity** affects only the panel and sidebar backgrounds: `alpha = clamp(opacity*255, 40, 255)`. Text and controls stay opaque (per-pixel alpha, not whole-window opacity).
- **Resize grip glyph**: a Path `M0,14 L14,0 M5,14 L14,5 M10,14 L14,10`, stroke = Label colour, thickness 1.4, 14×14, bottom-right, margin 0,0,15,15, opacity 0.65, not hit-testable. Hidden in Full view.
- Fonts: `"Segoe UI, Leelawadee UI, Tahoma"` (Leelawadee for Thai). Icon glyphs: `Segoe MDL2 Assets` (codepoints listed below). On Linux use any icon font or SVG equivalents.

### Type ramp (resources; multiplied by `font_scale`, clamped 0.8–1.6, rounded to 0.1)
| key | base size |
|---|---|
| FontTiny | 9 |
| FontSmall | 10 |
| FontLabel | 10 |
| FontBody | 11 |
| FontValue | 14 |
| FontHeading | 15 |
| FontNav | 12 |
| FontNavIcon | 13 |

Text styles: **Label** = FontLabel, SemiBold. **Body** = FontBody. **Value** = FontValue, Bold. **BadgeText** = 10 (fixed), SemiBold.

### Resize (all views except Full; Full can also be resized, see below)
- Hit test on the whole window rect (shadow pad included). Corners are tested first, within `CornerBand = 12+16 = 28` px of both edges. Edges are within `EdgeBand = 12+6 = 18` px.
- Cursors: SizeWE (L/R), SizeNS (T/B), SizeNWSE (TL/BR), SizeNESW (TR/BL). Over a control (button, slider, scrollbar, thumb, or any ancestor that is one) the cursor is an arrow and no resize/drag starts.
- Every frame is computed from the rect at drag start, never from the previous frame. The opposite edge stays pinned, even when the size clamps.
- The resize starts in the *preview* mouse-down (before child controls) and captures the mouse.
- **Widget resize keeps the aspect ratio** (`ResizeWidget`):
  - A side drag drives the scale. A top/bottom drag widens around the centre.
  - A corner drag uses the axis that moved relatively further, bounded by the room left in the work area.
  - The scale is clamped so both dimensions stay within min/max. If the start is already outside the limits, the scale nearest to 1 is kept.
  - A horizontal drag grows downward until the panel bottom reaches the work-area bottom, then grows upward.
  - A vertical (centre-widening) drag is clamped so both side edges stay on screen.
  - Work area = union of the start monitor's work area and the work area of the monitor under the pointer.
- Overall and Full resize freely within their limits.
- Size limits (panel px, not window px):

| view | min | max |
|---|---|---|
| widget | 200×64 | 900×400 |
| overall | 470×300 | 4000×3000 |
| full | 640×480 | 4000×3000 |

- On mouse-up the position **and** size are saved (`SavePlacement` + config).
- A resize of Overall sets `overall_sized=true`.

### Drag-move
- Press anywhere that is not an edge and not a control → pending drag. It becomes a drag after the pointer moves **≥4 px** on either axis. Below that it stays a click and reaches the control underneath.
- Pixel deltas are converted with the DPI captured at drag start.
- **Snap** (`config.snap`, default true): while dragging, snap to the work-area edges of the monitor under the window centre, within **25 px** (`SnapMargin`), on each of the four edges.
- Mouse-up → save the position.

### Position memory
- **One** remembered position for all views: `pos_x` / `pos_y` (window top-left, window coordinates including the pad). Saved on mode switch, drag end, resize end, hide-to-tray, close, restart, and OS session end.
- The per-view keys `widget_pos_x/y`, `overall_pos_x/y`, `full_pos_x/y` still exist in config but are **no longer read or written** (R-024).
- First run (no pos): bottom-right of the primary work area, 24 px in: `Left = area.Right - Width - 24`, `Top = area.Bottom - Height - 24`.
- On `Loaded` and after every mode switch: `KeepOnScreen`. The window is moved the minimum needed to sit inside the work area of the monitor at its top-left. If it is larger than the area, it is put at the area's top-left.
- Mode switch: the window keeps its top-left. **Exception:** leaving Full returns to the position before Full was entered, but only if the window has not been moved while in Full.

### Per-view size
- Widget: `widget_w`×`widget_h` (default **270×104**).
- Overall: `overall_w`×`overall_h` (default **630×480**).
  - While `overall_sized=false`, entering Overall **auto-fits**: the view is measured unconstrained, the layout scale is divided out, the result is rounded up (ceil) and clamped to [min, work area − 24]. The window is then kept on screen.
  - After a manual resize (`overall_sized=true`) the stored size wins.
- Full: `full_w`×`full_h`. 0 means unset → opens at minimum **640×480**. It is a normal window size, **not** OS fullscreen. (The README says "takes the whole monitor"; that is outdated.)
- **Content scaling**: the Widget content and the whole Overall view are scaled by `min(panelW/refW, panelH/refH)`.
  - Reference sizes: widget **270×104**, overall **630×480**.
  - In the Widget, only `WidgetContentHost` is scaled. The corner/step buttons stay fixed size.
  - The paused badge counter-scales (1/scale). The side margins are written as 16/scale so they stay 16 screen px.
  - In Overall, the whole view (sidebar + buttons + content) is scaled.
  - Font scale multiplies independently on top.
- **Reset window size** (context menu): `overall_sized=false`; `full_w=full_h=0`; widget → 270×104; overall → 630×480. Then apply, keep on screen, and save.

---

## 2. View switching

| input | Widget | Overall | Full |
|---|---|---|---|
| double-click (left, anywhere not handled by a child) | → Overall | → Full | → Widget |
| F11 | → Full | → Full | → Overall |
| Esc | → Widget (no-op) | → Widget | → Overall |
| context menu "expand / collapse" | → Overall | → Widget | → Overall |
| context menu "full screen / exit full screen" | → Full | → Full | → Overall |
| Overall header buttons | — | Full (E740), Widget (E745), Close (E8BB) | — |
| Overall sidebar nav buttons | — | "Full data" (E9D9) → Full; "Open widget" (E745) → Widget | — |
| Full header buttons | — | — | Overall (E80F), Widget (E745), Close (E8BB) |
| Full rail nav buttons (above tabs) | — | — | "Overall" (E80F), "Open widget" (E745) |
| Widget close button | Close (see close action) | | |

Exceptions: a double-click on a drive row (Overall) or a disk tab (Full) opens the file manager and does **not** switch view. A double-click with `ClickCount>1` does not start a resize.

On every switch, in this order:
1. Save the placement.
2. Compute the origin (see above). When entering Full, remember the rect before Full.
3. Set `last_view` and save the config.
4. Toggle visibility. Corner radius is 0 in Full, 14 otherwise. The grip is hidden in Full.
5. Apply the size, then keep on screen.
6. Per view:
   - Overall: update + auto-fit.
   - Full: set the title to `full_data`, push history, select `full_tab` if set.
   - Widget: rebuild the rotation list + update.

Header icon tooltips:
- Full button: `full_data`.
- Widget button: `open_widget`.
- Full→Overall button: `go_overall`.
- Full→Widget button: `open_widget`.

**Main context menu** (right-click anywhere without its own menu). The items are rebuilt with the current language each time the menu opens:
1. `collapse` (in Overall) / `expand_hint` (otherwise)
2. `exit_fullscreen` (in Full) / `fullscreen`
3. `reset_size`
4. `restart`
5. separator
6. `close`

Icon button style (`IconButton`):
- Foreground `#94a3b8`, transparent background, CornerRadius 6.
- Hover background `#33808080`, pressed `#55808080`, hand cursor.
- Header buttons are 26×26, glyph size FontBody, 8 px gap.

---

## 3. Widget view

Layout inside the panel (panel margin 12):
- **Close button** (E8BB): 18×18, top-right, margin `0,-12,-4,0`, glyph FontTiny. Style `HoverIconButton`: opacity **0.35**, 1.0 while the pointer is over the window.
- **Pause button**: E769 (pause) while running, E768 (play) while paused. 18×18, top-right, margin `0,-12,18,0`, same hover style. Tooltip `pause_rotation` / `resume_rotation`.
- **Prev** (E76B) and **Next** (E76C) step buttons:
  - 20×20, glyph FontSmall, vertically centred, at the left and right edges, margin −6 (they overlap the panel gutter).
  - Style `WidgetStepButton`: always fully visible, foreground = Muted, background = Chip colour, CornerRadius 6.
- **Content host** (scaled), side margin 16 (screen px):
  - **Title row**:
    - Left: the paused badge (Label style, Label colour, text `paused`, hidden unless paused). When the badge is shown, the title's left margin becomes 72/scale.
    - Title: Label style, Label colour, UPPERCASED, ellipsis, right margin 96.
    - Right: the temperature badge, then the value (Value style, 14 bold, Text colour).
  - **Rows**: a UniformGrid with `WidgetColumns` columns of `MeterRow`s.
  - **Detail line**: FontSmall, ellipsis, top margin 4. Colour is Muted, or amber/red when it carries a disk alert. Clicking it while it carries an alert opens disk cleanup and dismisses the alert.
  - **Message** (centred, Label colour) replaces the content:
    - `connecting` before the first snapshot.
    - `no_data` when nothing is enabled.

### Rotation
- Interval **5 s** (`RotateEvery`), checked on the 400 ms tick.
- Rotates only if: not paused, in Widget mode, and the page count > 1.
- Prev/Next step ±1 with wrap-around and reset the 5 s timer. Stepping while paused changes the page but keeps the pause.
- Resuming resets the timer.
- The pause state is not persisted (in-memory only).

**Page list** (`RebuildViews`), in this order:
1. CPU (if `show_cpu`):
   - `cpu_mode=separated`: one page per **4 logical cores** (C0–3, C4–7, …).
   - `total`: one page.
2. RAM (if `show_ram`): one page.
3. Disk (if `show_disk`):
   - `disk_mode=separated`: one page per drive.
   - `total`: one page.
4. Network (if `show_network`):
   - `network_mode=separated`: one page per adapter.
   - `total`: one page if there are any adapters.
5. Extension pages.
6. If the list is empty: one "empty" page (shows `no_data`).

**There is no GPU page in the widget.** `ViewIndex` is clamped when the list shrinks. The list is rebuilt on settings changes and when entering the Widget.

**Page contents** (title is uppercased; the value is "N%" unless stated):

| page | title | value | detail line | rows | temp badge |
|---|---|---|---|---|---|
| CPU total | `cpu (total)` | CPU total % | "`N` `cores`" | 1 compact bar (load colour, CPU accent) | CPU package temp |
| CPU group | `cpu  C{from}-{to-1}` | CPU total % | (none) | one compact bar per core, labelled "C{i}"; 2 columns if >2 cores, else 1 | CPU package temp |
| RAM | `memory` | RAM % | "`used`/`total` GB `in_use`  ·  `top process group`" (F1) | 1 bar, RAM accent | RAM temp (always n/a) |
| Disk total | `disk (all_drives)` | used/total over all drives, rounded % | "`read` x \| `write` y MB/s" | 1 bar, disk accent | max temp over drives |
| Disk one | `drive` + " " + `Letter: (Label)` | drive % | speed text, or the alert text (amber/red, clickable) | 1 bar, disk accent | drive temp |
| Net total | `network (all_adapters)` | link-usage % | "↓ x \| ↑ y MB/s" | 1 bar, net accent | none |
| Net one | adapter name | adapter usage % | ↓/↑ text | 1 bar, net accent | none |

- Compact rows show only the label and the bar: no value text, no badge, no alert.
- Top process group string: `name ×count · size` (×count only when count>1). It is the group with the most working set.

---

## 4. Overall view

Grid with two columns: **sidebar 196 px** + content `*`.

### Sidebar
- Background: Sidebar colour (with opacity alpha), CornerRadius `14,0,0,14`.
- Vertical ScrollViewer with a StackPanel, margin 16.

Controls, in order:

1. Nav button **E9D9** `full_data` → Full.
2. Nav button **E745** `open_widget` → Widget.
3. Heading `window_settings`, then:
   - Check `always_on_top` (bool, default false) → sets Topmost immediately. Mirrored with the Settings window.
   - Check `light_mode` (theme=="light"; default theme **light**) → re-themes live. Mirrored.
   - Dial `opacity`: 0.35–1.0, default 1.0. Readout "NN%". Live while dragging; saved on mouse-up. Mirrored.
   - Dial `font_size`: 0.8–1.6, default 1.0. Readout "NN%". Rewrites the type ramp live. Mirrored.
4. Heading `display_settings`, then:
   - Check `show_cpu` (default true)
     - indented: Check `per_core` (cpu_mode=="separated"; default separated)
     - indented: Check `cpu_temp` (`cpu_temperature`, default true) → nudges the sampler.
   - Check `show_ram` (true)
   - Check `show_disk` (true)
     - indented: Check `per_drive` (disk_mode separated)
     - indented: Check `show_network_drives` (`include_network`, default true) → refreshes the drive list.
     - indented: Check `show_smart` = the SMART extension enable toggle, shown only if the extension exists. Label "`show_smart` (status)".
   - Check `show_gpu` (true) → nudges the sampler.
   - Check `show_network` (true)
     - indented: Check `per_adapter` (network_mode separated)
5. Heading `extensions` + one check per other extension (only if any exist). Each has a status suffix and a tooltip (version, detail). There is an "approve" button for unapproved ones (Windows-only approval flow: skip or redesign).
6. Nav button **E713** `settings` (top margin 12) → opens the Settings window.
7. Heading `updates`, then:
   - Check `auto_check_updates` (default true).
   - Update panel (see §8).
8. Restart button (SidebarButton, top margin 10): label `restart_admin` if not elevated, `restart` if elevated. Admin restart is Windows-only; on Linux use a plain `restart`.
9. Nav button **E897** `manual` → Manual window.
10. Nav button **E946** `about` → About window.

Sidebar control details:
- **Heading**: Label style, Label colour, UPPERCASE, margin 0,10,0,6, wraps.
- **Check**: Muted foreground, FontBody, margin 0,3,0,3, label wraps. Indented = left margin 20.
- **Dial**:
  - Header grid (margin 0,10,0,4): caption UPPERCASE in Label colour on the left; readout in Muted on the right.
  - Slider (margin 0,0,0,6).
  - Slider style: 18 px tall; 4 px track `#44808080` radius 2; filled part `#3b82f6`; thumb 14×14 ellipse, fill `#ffffff`, stroke `#3b82f6` 2 px; move-to-point on click.
- **SidebarButton**:
  - Full width, left aligned, foreground `#3b82f6`, CornerRadius 7.
  - Border 1 px `#553b82f6`, padding 10,7,10,7, margin-bottom 4.
  - Hover background `#223b82f6`.
- **Nav button**: glyph (FontNavIcon, right margin 8) + label (FontNav, wraps) in an Auto/* grid.
- Thai text is passed through `ThaiWrap.Soften`, which inserts U+200B before Thai leading vowels (เ แ โ ใ ไ) that follow a Thai non-leading-vowel character, so long Thai labels can wrap.

Every change: write the config → (sometimes) nudge the sampler → rebuild the pages and refresh the current view → save.

**Mirroring**: always_on_top, light_mode, opacity and font_size appear in both the sidebar and Settings. Changing one updates the other without running the handler twice.

### Content column
- Margin 16,14,14,12. Three rows:
  1. Header buttons, right aligned: Full **E740**, Widget **E745**, Close **E8BB**.
  2. Scrolling list of **section cards**.
  3. Temperature legend: FontSmall, Label colour, wraps, top margin 6, text `temp_legend`.
- Section card:
  - Background Control colour, CornerRadius 10, padding 12,10,12,10, margin-bottom 10.
  - Header: title (Label style, Label colour, wraps) on the left; note (FontSmall, Label colour) on the right.
  - Rows in a UniformGrid with `Columns`: **2 for CPU when per-core, otherwise 1**.

Section order: cpu, ram, disk, gpu (only if `show_gpu` and a GPU is present), net (only if `show_network` and there are adapters), then extension sections inserted after their `place_after` anchor or appended.

| section | note | rows |
|---|---|---|
| CPU (`cpu`) | package temp text, e.g. "~52°C" or "52°C" | per-core: "C{i}" bar, load colour, **no badge**. Total: label `cpu`, detail "N `cores`", CPU temp badge |
| RAM (`memory`) | `mem_top` formatted with the top line | label `memory`, detail "`used` / `total` GB  `in_use`  ·  `module summary`", e.g. "2 x 16 GB DDR4 2667 MHz". Badge n/a |
| Disk (`disk`) | — | per-drive: title "C: (Label)", detail "`media` · `bus` · `used`/`total` GB (F0) · `read` x \| `write` y MB/s", temp badge, alert banner. Total: `all_drives` row with speed text, max temp, no alert/letter |
| GPU (`gpu`) | GPU name | row 1: `gpu` usage, detail "x GB `dedicated`  ·  y GB `shared`" (F1, only parts >0.01 GB), no badge. Then one row per engine with usage >0 (name, usage), no badge |
| Net (`network`) | — | per-adapter: name, usage = (down+up)·8/linkMbps·100 clamped 0–100 (0 when there is no link), detail "`wireless`\|`wired` · `N Mbps`\|`no_link` · ↓ x \| ↑ y MB/s". Total: `all_adapters` |

**MeterRow** (template):
- Grid margin 0,2,6,2. Columns: Auto / * / Auto / Auto.
- Title: Body, Muted colour, ellipsis, max width 150, right margin 8.
- Bar: 10 px tall. Empty part = BarEmpty colour, CornerRadius 5. Fill = accent/load colour, radius 5, star widths percent / (100−percent).
- Value: Body, Text colour, min width 34, right aligned, left margin 8. Hidden when compact.
- Temp badge: left margin 8. Hidden when compact or when `ShowTemp=false`.
- Row 2: detail. FontSmall, Label colour, ellipsis, margin 0,1,0,3. Hidden when empty.
- Row 3: **alert banner**.
  - Background = alert colour with alpha 0x30. Left border 3 px in the alert colour (amber `#eab308` / red `#ef4444`). CornerRadius 4, padding 8,3,6,4, margin-bottom 4, hand cursor.
  - Text FontSmall, Text colour, wraps.
  - Dismiss glyph E711 (FontTiny, Muted) on the right.
  - Click the banner → disk cleanup + dismiss. Click the ✕ → dismiss only.
- A drive row reacts to:
  - Double-click → open the drive root in the file manager.
  - Right-click → drive menu (§4b).

**Temperature badge**:
- Border CornerRadius 6, padding 5,1,6,2.
- Thermometer path `M 3.1,0.6 A 1.4,1.4 0 0 1 5.9,0.6 L 5.9,6.2 A 2.6,2.6 0 1 1 3.1,6.2 Z`, 9×11, right margin 4, fill = fore colour.
- Text BadgeText = fore colour: "n/a", or "52°C", or "~52°C" when estimated.
- When hot (≥80): the badge opacity pulses 1.0→0.35, 0.6 s, auto-reverse, forever.

### 4b. Disk tools (Windows-specific; map to Linux equivalents)
- Drive menu with one drive:
  - "`disk_open_explorer`" ({0}=`C:`) → open the root.
  - "`disk_cleanup`" → `cleanmgr /d X`. Enabled only for fixed drives.
  - "`disk_manager`" → `diskmgmt.msc`.
  - separator, then the main window menu items.
- With several drives: one submenu per letter (`C:`) holding open + cleanup, then disk management, separator, window items.
- Letter validated as a single ASCII letter.
- Linux mapping is a design decision. Suggestion only: `xdg-open <mountpoint>`; cleanup → (none / baobab); management → gnome-disks. Unclear: not in the source.

**Disk alerts** (`DiskAlertTracker`):
- Only local fixed drives (not network, not removable).
- Levels: Warm ≥70 %, Hot ≥90 %. Hysteresis 2 points: Hot holds down to ≥88, Warm holds down to ≥68.
- A warning becomes pending when a drive is first seen at Warm/Hot (so on every app start) or when its level rises.
- Dismissing clears the pending warning until the level rises again. Falling to Ok clears it. A drive that disappears is forgotten.
- Text: `disk_warn` / `disk_hot` with {0}=`C:`, {1}=usage %.
- Shown as:
  - the Overall drive-row banner;
  - the Widget single-drive page detail line (amber/red text, click → cleanup + dismiss).

---

## 5. Full view

- Margin 14,12,14,12. Grid: row 0 Auto (header), row 1 * (body). Columns: Auto (rail) and * (detail).
- **Header**:
  - Title `full_data`: Value style at FontHeading, Text colour.
  - Right side: Overall **E80F**, Widget **E745**, Close **E8BB** (26×26).
- **Rail** (column 0, right margin 12):
  - Nav pair (`go_overall` E80F, `open_widget` E745) pinned on top, margin-bottom 8. Its width follows the tab list's width.
  - Vertical scrolling tab list, min width **128**.
- **Tab button** (`DeviceTabButton`):
  - Margin-bottom 3, CornerRadius 7, left border 3 px, padding 9,6,10,6.
  - Foreground `#64748b`. Hover background `#22808080`.
  - **Selected**: background `#3b82f6`, border `#3b82f6`, foreground `#ffffff`.
  - Content, stacked:
    - Title: Label style, ellipsis.
    - Detail: FontSmall, opacity 0.7, hidden when empty.
    - Summary (the headline figure): FontSmall, opacity 0.85.
- Click a tab → select it, then save `full_tab` = tab key.
- Disk tabs only:
  - Double-click → open the drive in the file manager (one letter), or show the drive menu (several letters).
  - Right-click → drive menu.
- **Detail pane** (column 1, scrolling, DataContext = selected tab), top to bottom:
  1. Heading row: tab Title (Value style at FontHeading, Text colour) on the left. `Hardware` on the right (Body, Label colour, max width 360, ellipsis, bottom aligned).
  2. Facts card, shown only if there are facts:
     - Control colour, CornerRadius 10, padding 12,10,12,4, margin-bottom 10.
     - WrapPanel of facts. Each fact: Name (Label style, Label colour) over Value (Body, Text colour, wraps); max width 280, margin 0,0,24,8.
  3. Big chart cards, stacked.
  4. Small (core/engine) cards in a WrapPanel.
  5. RAM tab only: the process list and services (§5c).

**Big chart card**:
- Control colour, CornerRadius 10, padding 12,10,12,11, margin-bottom 10, min height **178**.
- Row 1: Title (Label style, Text colour, ellipsis, right margin 66) on the left; Value (Body SemiBold, Text colour) on the right.
- Row 2: Detail (FontSmall, Label colour, ellipsis).
- Row 3 (margin 0,6,0,2): `Unit` on the left and `Ceiling` on the right (FontTiny, Label colour).
- Row 4: Chart, min height 40.

**Small card**:
- **89×89** (=178/2). Control colour, CornerRadius 8, padding 7,5,7,6, margin 0,0,8,8.
- Header: Title on the left (FontSmall, Label colour); Value on the right (FontSmall SemiBold, Text colour), margin-bottom 4.
- Chart fills the rest.

### 5a. Tabs (key → content), in this order
Tab selection:
- When there is no selection, use `full_tab` if it exists.
  - For an `ext:<id>:*` key, fall back to any tab of the same extension.
  - Otherwise use the first tab.
- If a fallback was used and the remembered tab appears later, switch to it.
- Tabs are reconciled in place, so a tab keeps its identity and its history.

| key | title | rail detail | summary (headline) | hardware | cards | facts |
|---|---|---|---|---|---|---|
| `cpu` (if show_cpu) | `cpu` | "{cores}C/{logical}T" (or "{cores}C" when equal) + "  ·  x.xx GHz" (base clock) | "N%" + "  ·  temp" if any | CPU name | big: `cpu`, value N%, detail "N `cores`  ·  temp", accent CPU, max 100, unit `utilisation`, ceiling "100%". Small (only when per-core): "Core i", N% | `virtualisation` = `enabled`/`disabled` (if known); "L2", "L3" (≥1024 KB → "x.# MB", else "N KB") |
| `ram` (if show_ram) | `memory` | "x.x GB / y.y GB" | "N%" | "{filled} `of_slots` {total} `slots`" (empty if no modules) | big: `memory`, N%, "used / total GB `in_use`", RAM accent, max 100. Plus, if a process group is picked: big card "proc:<name>": title = name, value = size, detail = `mem_count`, RAM accent, max 0 (auto), unit "MB", seeded with that group's history, ceiling unit " MB" | one per module: Name = slot (or bank), Value = "16 GB · DDR4 · 2667 MHz · Manufacturer" |
| `disk{N}`, one per physical disk (drives grouped by disk number, ascending) | "Disk N" | "media (bus)  ·  x.x GB  ·  `online`/`offline`  ·  x.x GB `unallocated`" (if >0.05) | disk model | disk model | one big card per drive letter (sorted), key `diskio{L}`: title "C: (Label)", value "{read+write} MB/s", detail "FS  ·  used / total GB  (N%)  ·  `read` x \| `write` y MB/s  ·  temp", disk accent, max 0 (auto), unit "MB/s" | none |
| `diskvirtual` | `virtual_drives` | "N `drives`" | "C:  ·  D:" | — | same per-drive cards | — |
| `disknetwork` | `network_drives` | "N `drives`" | letters | — | same | — |
| `gpu` (if show_gpu && present) | `gpu` | GPU memory text | "N%" | GPU name | big: `gpu`, detail = "Engine N%  ·  ..." for each engine, GPU accent (amber), max 100. Small per engine | "Driver", `dedicated` "x.xx GB", `shared` "x.xx GB" (only when >0.01) |
| `net{id}`, one per adapter (if show_network) | adapter name | `wireless`/`wired` | "{down+up} MB/s" | adapter description | big: name, "{down+up} MB/s", detail "↓ x \| ↑ y MB/s", net accent, max 0 (auto), unit "MB/s" | `network` = wired/wireless; "Link" = "N Mbps" or `no_link`; "Adapter" = description |
| `ext:<id>:<sub>` | extension-defined (e.g. SMART) | | | | | |

Notes:
- Speed format: ≥100 → "F0", otherwise "F1".
- The "Disk N" / "Driver" / "Link" / "Adapter" / "L2" / "L3" labels are hard-coded English, not translated.
- **Ceiling** of an auto-scaled chart = `Speed(series.max × 1.25)` + " MB/s" (or " MB"). For max 100 the ceiling is "100%".
- Only *local* drives that have a disk number go under `disk{N}`. Network drives go to `disknetwork`. Drives without a disk number (cloud, subst) go to `diskvirtual`.

### 5b. Chart rendering (`Controls/Chart.cs`)
- History ring of **72 points** (`ChartCard.Points`): 3 min at 2.5 s, 72 s at 1 s, 6 min at 5 s. A new card starts empty, or seeded from a group history.
- Plot rect = `(0.5, 0.5, floor(w)-1, floor(h)-1)`, so 1 px lines land on a pixel.
- Drawing order:
  1. Fill the plot with PlotBrush.
  2. Grid: **6 columns × 4 rows**. Inner lines only, 1 px, Grid colour, x/y rounded + 0.5.
  3. The series (if count ≥2).
  4. Frame: 1 px, colour = palette Border, drawn **last**.
- Scale:
  - If `Maximum>0`, use it (100 for percentages).
  - Otherwise `max(series.max × 1.25, 0.001)`.
  - Values are clamped to 0–1 of the scale.
- X axis:
  - step = plotWidth / (72−1).
  - The newest point sits at the right edge. A partially filled series starts part-way across; it is not stretched.
- Geometry:
  - A closed polygon from (left, bottom) through the points to (right, bottom).
  - The stroke is **1.2 px** in the accent colour with angular joins (no smoothing).
  - The fill is a **solid** colour: accent mixed toward the panel colour.
    - Dark plot (R+G+B < 384): mix toward `#0f172a` by **0.62**.
    - Light plot: mix toward `#ffffff` by **0.72**.
- An empty chart still draws the plot, grid and frame. A chart with one point draws no line.

### 5c. Memory tab: processes and services (RAM tab only, under the cards)
Refresh cadence (sampler):
- Process table every **3 s**.
- Service table every **10 s**, re-attached to each process read.
- After a kill or service operation the next read is forced immediately (`RefreshMemory`).

- Heading `mem_processes` (FontHeading, SemiBold, Text colour), margin 2,6,2,8.
- **Search box**:
  - Padding 6,4, Control colour background, Text colour foreground and caret, no border. Tooltip `mem_search`.
  - Placeholder `mem_search` (FontSmall, Label colour) shown while empty.
  - Filters live, on every keystroke.
  - Case-insensitive substring match: on group name for processes; on service name **or** display name for services.
- Status line (FontSmall, Label colour, wraps): the result of the last operation. Empty on success, `mem_cancelled`, or `mem_failed` {0}=message.
- **Process groups**:
  - Grouped by process name (case-insensitive). Group size = sum of working sets.
  - Groups sorted by size desc, then name. Members sorted by working set desc, then PID.
  - Cap **40** groups (**120** while searching). When capped, a `mem_group_note` line shows ({0} shown, {1} total).
  - Group row:
    - Border CornerRadius 6, padding 2,2,4,2. Background `#223b82f6` when this is the graphed group.
    - Columns 28 / * / Auto / 30:
      - expand button: glyph E76C collapsed, E70D expanded; style MemGlyph 26×26, glyph FontNavIcon;
      - name button: name (Text colour) + count text `mem_count` when >1 (FontSmall, Label colour, left margin 8). Tooltip `mem_graph`. Click toggles the graph card for this group on the RAM tab (click again to hide);
      - size: SemiBold, Text colour, left margin 8;
      - kill-group button: glyph E74D, colour `#ef4444`, tooltip `mem_kill_group`. Shown if any member can be killed.
  - Expanded members:
    - Margin 30,1,0,1. Columns * / Auto / Auto / 30.
    - "PID n" (FontSmall, Label colour); `mem_private` {0}=private bytes (right margin 14); size (FontSmall, Text colour), or `mem_no_access` when unreadable.
    - Kill button E711 `#ef4444` (tooltip `mem_kill`), or a lock glyph E72E (tooltip `mem_protected`) when protected.
  - Rows are updated in place (keyed sync), so an expanded group stays expanded and the button under the pointer stays the same.
  - Size format: ≥1024 MB → "x.x GB"; ≥100 MB → "N MB"; otherwise "x.x MB".
  - Per-group history (72 points, MB) is kept for every running group, so a newly picked graph has a past. The selection clears when the group disappears.
- **Protected (cannot kill)**:
  - PID ≤4, or our own PID, or names: system, idle, registry, smss, csrss, wininit, winlogon, services, lsass, lsm, memory compression, secure system, system interrupts, dwm.
  - Unreadable processes.
  - Linux needs its own list, e.g. PID 1 / kernel threads / own PID. That is a design decision, not in the source.
- **Kill flow**:
  - Confirm dialog, Yes/No, default No, warning icon, title `title`. Text `mem_confirm_kill` ({0} name, {1} pid).
  - Group kill: `mem_confirm_kill_group` ({0} name, {1} count of killable members).
  - If services run in the target process(es), append "\n\n" + `mem_confirm_host` ({0} count).
  - The kill re-checks that the PID still has the same name, then waits up to 3 s for exit.
  - On access denied: Windows starts an elevated helper copy (UAC) and waits up to 60 s. Windows-only: on Linux use pkexec or report failure (design decision).
- **Services** (Windows services → systemd units on Linux, a design decision):
  - Heading `mem_services` (margin 2,16,2,8).
  - Sort: running first, then host memory desc, then display name. Cap **60** (120 while searching), `mem_service_note` when capped.
  - Row: Border radius 6, padding 6,4, bottom border 1 px `#22808080`.
    - Left column:
      - Display name (Text colour, ellipsis).
      - Line 2 (FontSmall, Label colour): "name · state" + " · PID n · size" when running. State is `mem_running` / `mem_stopped` / `mem_pending` (anything else).
      - `mem_shared` {0}=number of services sharing the host process, when >1.
    - Startup button: "`mem_auto`|`mem_manual`|`mem_disabled`|`mem_other` ▾", tooltip `mem_startup`.
      - Opens a menu with Automatic / Manual / Disabled. The current one is checked.
      - Picking a different one asks `mem_confirm_startup` ({0} display, {1} choice).
    - Action buttons (26×26 glyphs):
      - Start E768 `#22c55e`: shown when not running and not disabled. Confirm `mem_confirm_start`.
      - Restart E72C: shown when running. Confirm `mem_confirm_restart`.
      - Stop E71A `#ef4444`: shown when running. Confirm `mem_confirm_stop`.

---

## 6. Palette / themes (`Palette.cs`)

| token | dark | light |
|---|---|---|
| Panel | `#0f172a` | `#ffffff` |
| Sidebar | `#0b1220` | `#f3f5f9` |
| Control (cards) | `#1a2438` | `#eef1f6` |
| Chip | `#1e293b` | `#e6eaf1` |
| BarEmpty | `#070c17` | `#dfe4ec` |
| Text | `#ffffff` | `#1e293b` |
| Muted | `#cbd5e1` | `#64748b` |
| Label | `#94a3b8` | `#475569` |
| Border (edge/frame) | `#2b374b` | `#d5dae1` |
| Tag (badge bg, no reading) | `#243044` | `#e8ecf1` |
| BadgeOk bg | `#16351f` | `#dcfce7` |
| BadgeWarm bg | `#3a3212` | `#fef3c7` |
| BadgeHot bg | `#3d1717` | `#fee2e2` |
| Plot (chart bg) | `#0b1220` | `#ffffff` |
| Grid (chart grid) | `#22304a` | `#e2e6ec` |

- Panel and Sidebar get the opacity alpha. `WindowBrush` (secondary windows' background) = Panel, opaque.
- The theme is chosen by `theme` == "light" → Light; anything else → Dark.

Accents (theme-independent):

| accent | colour |
|---|---|
| CPU | `#3b82f6` |
| RAM | `#a855f7` |
| Disk | `#10b981` |
| Net | `#06b6d4` |
| GPU | `#f59e0b` |

**Load colour** (all usage bars):
- value ≥ **90** → `#ef4444` (LoadHot)
- ≥ **70** → `#eab308` (LoadWarm)
- otherwise the meter's accent (default the CPU accent).

GPU rows use the CPU accent below 70. Charts always use the plain accent; there is no load colouring on charts.

**Temperature badge**:
- null → fore = Label, back = Tag, text "n/a".
- ≥ **80** → fore `#ef4444`, back BadgeHot, pulsing.
- ≥ **65** → fore `#eab308`, back BadgeWarm.
- otherwise → fore `#22c55e`, back BadgeOk.

Temperature text: `n/a` | `[~]N°C` (~ = estimated).

- CPU estimate when no sensor and `temp_estimate`=true: `40 + usage×0.4` (integer). It is applied per core and to the package (using total load).
- RAM and GPU temperatures are always null (n/a badge for RAM; the GPU has no badge).

Fixed (theme-independent) colours used in styles:
- `#94a3b8` icon buttons
- `#3b82f6` selection, sidebar buttons and slider
- `#64748b` tab text
- `#808080` with alpha `33` / `55` / `22` / `44` / `99` for hovers and the scrollbar
- `#ef4444` / `#22c55e` for kill/stop/start glyphs

Scrollbar:
- 10 px wide, no arrows, no visible track.
- Thumb `#55808080` (CornerRadius 4, side margin 2). `#99808080` on hover.
- Clicking the track pages.

---

## 7. Config file

- Windows: `%APPDATA%\SystemMonitor\config.wpf.json`.
  - One-time copy from the legacy `%APPDATA%\SysMonitor\config.wpf.json` if the new file is absent. (The README's `SysMonitor` path is outdated.)
- JSON, indented, **snake_case_lower** property names (`System.Text.Json` `SnakeCaseLower`).
- Atomic save: write `<path>.tmp`, then move over the file. Save errors are ignored.
- A missing or corrupt file → defaults, never a startup failure.

| key | type | default | meaning |
|---|---|---|---|
| `lang` | string | `"en"` | `th` \| `en` |
| `theme` | string | `"light"` | `light` \| `dark` |
| `always_on_top` | bool | false | Topmost |
| `snap` | bool | true | snap to screen edges within 25 px while dragging |
| `auto_check_updates` | bool | true | check GitHub at start |
| `opacity` | double | 1.0 | 0.35–1.0 (alpha floor 40/255) |
| `font_scale` | double | 1.0 | 0.8–1.6 |
| `show_cpu` / `show_ram` / `show_disk` / `show_network` / `show_gpu` | bool | true | module visibility |
| `show_smart` | bool? | — | legacy; migrated to `extensions.smart.enabled` and never written again |
| `include_wireless` | bool | true | include Wi-Fi adapters (no UI control) |
| `network_mode` | string | `"separated"` | `separated` \| `total` |
| `cpu_mode` | string | `"separated"` | `separated` \| `total` |
| `disk_mode` | string | `"separated"` | `separated` \| `total` |
| `speed` | string | `"balanced"` | `eco` \| `balanced` \| `fast` |
| `temp_estimate` | bool | true | show the "~" modelled CPU temperature when there is no sensor (no UI control) |
| `cpu_temperature` | bool | true | read the real CPU temperature |
| `include_removable` | bool | true | include removable drives (no UI control) |
| `include_network` | bool | true | include network drives |
| `diagnostics` | bool | true | write the log file (no UI control) |
| `close_action` | string | `"tray"` | `tray` \| `exit`. (The README says exit is the default; the code says **tray**.) |
| `tray_promoted` | bool | false | Windows-only, skip |
| `pos_x`, `pos_y` | double? | null | window top-left (shared by all views) |
| `widget_pos_x/y`, `overall_pos_x/y`, `full_pos_x/y` | double? | null | legacy, unused |
| `widget_w`, `widget_h` | double | 270, 104 | widget panel size |
| `overall_w`, `overall_h` | double | 630, 480 | overall panel size |
| `overall_sized` | bool | false | user resized Overall (disables auto-fit) |
| `full_w`, `full_h` | double | 0, 0 | 0 = use the minimum 640×480 |
| `last_view` | string | `"widget"` | `widget` \| `overall` \| `full` |
| `full_tab` | string | `""` | last Full tab key. Legacy `smart` → `ext:smart:status`; `smartN` → `ext:smart:diskN` |
| `extensions` | map id → `{ "enabled": bool }` | `{}` | case-insensitive keys |

Legacy key migration (read only when the new key is absent): `mini_w/h` → `widget_w/h`; `exp_w/h` → `overall_w/h`; `exp_sized` → `overall_sized`.

Sampling cadence by `speed` (metrics / disk space / temperatures):

| speed | metrics | disk space | temperatures |
|---|---|---|---|
| eco | 5 s | 30 s | 60 s |
| balanced | 2.5 s | 15 s | 30 s |
| fast | 1 s | 10 s | 30 s |

Fixed sampler intervals:
- Drive list: 60 s.
- Processes: 3 s.
- Services: 10 s.
- A probe slower than its budget (250 ms default; 2–3 s for some) or throwing an error is backed off for 5 min. The last good value is kept meanwhile.

---

## 8. Updater

- Endpoint: `GET https://api.github.com/repos/epinephrinerx/SystemMonitor/releases/latest`.
  - User-Agent `SystemMonitor/<current>`, Accept `application/vnd.github+json`, timeout 30 s.
- Version: `tag_name` with a leading v/V trimmed, parsed as Major.Minor.Build. Current version = assembly version (3 parts).
- Asset: the first asset whose name starts with `SystemMonitor-Setup-` and ends with `.exe`, with an https `browser_download_url` on a host from: github.com, objects.githubusercontent.com, release-assets.githubusercontent.com.
  - On Linux, choose a new asset pattern (design decision).
- **At start** (on Loaded, if `auto_check_updates`):
  - Check once. On failure wait 60 s and retry once (skipped if the setting was turned off meanwhile).
  - If a newer version is found, keep it as pending and rebuild the sidebar.
  - Silent: no dialog, nothing downloaded automatically.
- **Sidebar update panel**:
  - Text "`current_version` X.Y.Z" (Muted, FontBody).
  - One button (SidebarButton):
    - Label `check_updates`, or `download_install` when an update is pending.
    - Disabled while working.
  - Status line (hidden until used):
    - Check: `checking` → `check_failed` | `up_to_date` | "`update_found` X.Y.Z" (label switches to `download_install`).
    - Download: "`downloading` NN%" progress, then `installing`. The installer is launched, the config saved, and the app exits.
    - Download failure → `download_failed`; the pending update is cleared and the label goes back to `check_updates`.
    - Exception → `check_failed`.
  - A pending update survives a sidebar rebuild (language change).
- The download goes to the temp dir as `SystemMonitor-Setup-<ver>.exe`. A partial file is deleted. Elevated staging under ProgramData is Windows-only: skip.

---

## 9. Startup, single instance, tray, close, restart

- **Single instance**:
  - Windows named mutex `Local\SystemMonitor.SingleInstance`, wait up to **5 s** (covers a restart handover).
  - If it is not acquired, the new copy exits silently. There is no signal to the running copy (it is not activated).
  - `--allow-multiple` bypasses the guard.
  - Linux: a lock file or abstract socket, same semantics.
- **Autostart** (only in the Settings window): Check `autostart`.
  - Windows: HKCU `...\CurrentVersion\Run` value `SystemMonitor` = `"<exe>"`. The legacy value `SysMonitor.NET` is deleted on every set.
  - Shown checked only if the value equals exactly the current exe path.
  - Machine-wide install (Scheduled Task, admin-only) is Windows-only: skip.
  - Linux: `~/.config/autostart/*.desktop`.
- **Close button / Close menu item**:
  - `close_action=="tray"`: hide to tray.
  - `exit`: quit.
  - The tray "Exit" item always quits.
- **Hide to tray**:
  - Save the placement, stop the UI timer (the sampler keeps running so history keeps filling), show the tray icon, hide the window.
  - The tray icon exists **only while the window is hidden**.
  - Icon = the app's own icon (fallback: a generic icon).
  - Tooltip:
    - "SysMonitor\nCPU N%  ·  RAM N%" + "  ·  [~]N°C" when there is a CPU temperature.
    - "SysMonitor" before the first data.
    - Trimmed to 63 chars (62 + "…"); this is a Windows limit.
    - Refreshed on each fresh snapshot while hidden. Note: the UI timer is stopped while hidden, so in practice the tooltip may not refresh. Unclear whether this is intended.
  - Tray menu: `tray_show`, separator, `tray_exit`.
  - Left click or double-click → restore: remove the icon, Show, Activate, restart the timer, tick immediately.
  - Win11 overflow "promotion" registry hack: Windows-only, skip.
- **Quit**: stop the timers, save the placement, stop the sampler, dispose the tray. Extensions get up to 2 s to dispose.
- **Restart** (context menu / sidebar):
  - Save the placement and config, start a new copy of the exe, then shut down immediately. The new copy waits up to 5 s on the instance lock.
  - Admin variant via UAC: Windows-only. If the user declines UAC, a `mem_cancelled` message box is shown and the app stays up.
- **Session end** (logoff/shutdown) → save the placement.
- Diagnostics log: `%APPDATA%\SysMonitor\sysmonitor-wpf.log` per the README. The exact path is in Diag.cs (not verified). Timestamps use the invariant culture (no Buddhist-era years).

---

## 10. Secondary windows (`PanelWindow`)

- Normal chrome, owner = main window, `ShowInTaskbar=False`, centred on the owner, min size **280×240**.
- Background = theme Panel colour (opaque). The content scrolls vertically, padding 18.
- Esc closes it.
- One instance per page; opening it again restores it from minimised and activates it.
- The content is rebuilt (keeping the scroll offset) on language/sidebar rebuild.
- Theme change: the background brush is shared, so it follows. Unclear: whether text colours in already-open windows update.

| window | size | resizable | content |
|---|---|---|---|
| Settings (`settings`) | 360×600 | yes | Heading `window_settings`, then `always_on_top`, `snap`, `autostart`, `light_mode`, opacity dial, font-size dial. Then heading `close_action` + radios (`close_to_exit` / `close_to_tray`). Heading `speed` + radios `eco`/`balanced`/`fast` (nudges the sampler). Heading `language` + radios "ไทย" / "English". Heading `disk_smart` + paragraph(s) (`admin_active` if elevated, otherwise `disk_smart_hint` + (`restart_admin` button if the SMART extension is loaded, else `admin_missing_core`)) |
| Manual (`manual`) | 460×560 | yes | For i = 1, 2, … while key `manual_{i}_h` exists: Heading `manual_{i}_h` + Paragraph `manual_{i}_t` |
| About (`about`) | 380×440 | no | "System Monitor" (Value style at FontHeading, Text colour); "`about_version` X.Y.Z"; `about_desc`; heading `about_developer` + "Apichart Chantanis"; heading `about_copyright` + "© 2026 Apichart Chantanis"; heading `about_terms` + `about_terms_text`; heading `about_credit` + "`about_credit_text` epinephrinerx/SystemMonitor"; nav button `about_source` → opens https://github.com/epinephrinerx/SystemMonitor |

- Radios: a WrapPanel. Muted foreground, FontBody, right margin 10.
- **Language switch** (radio):
  - `lang` = value; the view model language is set.
  - Rebuild the sidebar (which also rebuilds the open secondary windows), rebuild the context menu, refresh.
  - The Full view title is set only on entering Full. Unclear: whether it updates while already in Full.
- Paragraph: FontBody, wraps, Muted (or Text when `muted:false`), margin-bottom 4.

---

## 11. Windows-only: skip (no Linux meaning, or needs a redesign)

- **UAC elevation helpers** (`--elevated-op`, `AdminRestart`, `restart_admin`, the SMART "restart as administrator"): Windows UAC. Use pkexec/polkit or drop.
- **Extension approval elevated flow / ACL policy / Authenticode first-party allowlist**: Windows ACL/signing model.
- **Machine-wide install + Scheduled Task autostart** (`InstallMode`, `MachineWideStartup`): Windows installer model.
- **Run registry autostart**: replace with an XDG autostart `.desktop` file.
- **TrayPromotion** (`NotifyIconSettings\IsPromoted`): Win11 overflow flyout only.
- **63-char tray tooltip limit**: a Windows NotifyIcon limit.
- **`SetProcessWorkingSetSize` trim on heartbeat**: Windows memory manager.
- **Disk Cleanup (`cleanmgr /d X`) and Disk Management (`diskmgmt.msc`)**: Windows tools. Map or drop; drive letters become mount points.
- **Windows Services start/stop/startup type**: replace with systemd units or drop.
- **Critical-process kill list**: Windows process names.
- **ACPI thermal-zone WMI CPU temperature, PDH, IOCTL storage descriptors**: sensor layer only, not UI.
- **Installer/uninstaller (`SysMonitor.Setup`)** and updater asset `SystemMonitor-Setup-*.exe`: Windows packaging.
- **Segoe MDL2 Assets glyphs / Segoe UI / Leelawadee UI**: Windows fonts. Substitute an icon set and a Thai-capable font (e.g. Noto Sans Thai).
- **ProgramData admin update staging**: an elevation artefact.

Icon codepoints used (Segoe MDL2):

| codepoint | meaning |
|---|---|
| E8BB | close |
| E769 | pause |
| E768 | play / start |
| E76B | chevron left |
| E76C | chevron right / collapsed |
| E70D | chevron down / expanded |
| E740 | full screen |
| E745 | widget |
| E80F | home / overall |
| E9D9 | full data (sidebar) |
| E713 | settings |
| E897 | manual |
| E946 | about |
| E711 | ✕ (kill / dismiss) |
| E74D | delete (kill group) |
| E72E | lock (protected) |
| E72C | restart |
| E71A | stop |

The SMART "restart as admin" and About "source" NavButtons use a glyph that did not survive extraction (a private-use char). Unclear which one.

---

## 12. I18n

- `Lang` class: two dictionaries, `th` and `en`, with identical key sets.
- Lookup: current language → fallback `en` → the key itself.
- An unknown code passed to `Set` is ignored (the previous language is kept). The `Lang` constructor defaults to `th`, but the config default is `en`.
- Switching language is live: the sidebar, context menu, secondary windows and view text are rebuilt. The widget pause tooltip/badge is re-applied.
- Placeholders use `string.Format` `{0}`, `{1}` …
- Hard-coded, untranslated UI strings: "Disk N", "Core i", "C{i}", "Driver", "Link", "Adapter", "L2", "L3", "PID n", "ไทย", "English", "SysMonitor" (tray), "n/a".

### String table (all keys, extracted verbatim; `\n` = newline)

| key | th | en |
|---|---|---|
| `title` | System Monitor | System Monitor |
| `connecting` | กำลังอ่านข้อมูลฮาร์ดแวร์... | Reading hardware... |
| `no_data` | กรุณาเลือกข้อมูลที่จะแสดงผล | Select something to display |
| `window_settings` | การตั้งค่าหน้าต่าง | Window |
| `always_on_top` | หน้าสุดเสมอ | Always on top |
| `snap` | ดูดขอบจอ | Snap to edge |
| `autostart` | เปิดพร้อม Windows | Start with Windows |
| `light_mode` | โหมดสว่าง | Light mode |
| `display_settings` | ข้อมูลที่แสดงผล | Display |
| `show_cpu` | แสดง CPU | Show CPU |
| `show_ram` | แสดง RAM | Show RAM |
| `show_disk` | แสดง Disk | Show Disk |
| `per_core` | แยกราย Core | Per core |
| `total` | รวมทั้งหมด | Combined |
| `per_drive` | แยกราย Drive | Per drive |
| `speed` | ความถี่ในการอ่าน | Refresh rate |
| `eco` | ประหยัด | Eco |
| `balanced` | สมดุล | Balanced |
| `fast` | เร็ว | Fast |
| `language` | ภาษา | Language |
| `opacity` | ความโปร่งใส | Opacity |
| `font_size` | ขนาดตัวอักษร | Font size |
| `cpu` | CPU | CPU |
| `cores` | คอร์ | cores |
| `memory` | RAM | RAM |
| `disk` | Disk Storage | Disk Storage |
| `in_use` | ใช้งานอยู่ | in use |
| `read` | อ่าน | R |
| `write` | เขียน | W |
| `all_drives` | ทุกไดรฟ์ | All drives |
| `temp_legend` | = อุณหภูมิ  •  n/a = ไม่มีเซ็นเซอร์วัดอุณหภูมิ  •  ~ = ค่าประมาณจากโหลด ไม่ใช่ค่าที่วัดจริง | = temperature  •  n/a = no temperature sensor  •  ~ = modelled from load, not measured |
| `temp_label` | อุณหภูมิ | Temp |
| `cpu_temp` | อ่านอุณหภูมิ CPU จริง | Read real CPU temperature |
| `no_sensor` | n/a = ไม่มีเซ็นเซอร์วัดอุณหภูมิ | n/a = no temperature sensor |
| `reset_size` | รีเซ็ตขนาดหน้าต่าง | Reset window size |
| `restart` | รันโปรแกรมใหม่ | Restart |
| `close` | ปิดโปรแกรม | Close |
| `close_action` | เมื่อกดปิด | Close button |
| `close_to_exit` | ออกจากโปรแกรม | Exits |
| `close_to_tray` | ย่อลง Tray | Hides to tray |
| `tray_show` | แสดงหน้าต่าง | Show window |
| `tray_exit` | ออกจากโปรแกรม | Exit |
| `collapse` | ย่อหน้าต่าง | Collapse |
| `expand_hint` | ดับเบิลคลิกเพื่อขยาย | Double-click to expand |
| `drive` | ไดรฟ์ | Drive |
| `show_network` | แสดง Network | Show Network |
| `per_adapter` | แยกราย Adapter | Per adapter |
| `network` | Network | Network |
| `download` | ดาวน์โหลด | Down |
| `upload` | อัปโหลด | Up |
| `wireless` | ไร้สาย | Wi-Fi |
| `wired` | มีสาย | Ethernet |
| `all_adapters` | ทุก Adapter | All adapters |
| `fullscreen` | เต็มจอ | Full screen |
| `exit_fullscreen` | ออกจากเต็มจอ | Leave full screen |
| `history` | ย้อนหลัง | History |
| `partition` | พาร์ทิชัน | Partition |
| `of_disk` | บนดิสก์ | of disk |
| `partition_size` | ขนาดพาร์ทิชัน | Partition size |
| `physical_disk` | ดิสก์จริง | Physical disk |
| `partitions` | จำนวนพาร์ทิชัน | Partitions |
| `full_data` | ดูข้อมูลเต็ม | Full Data |
| `go_overall` | ไปหน้ารวม | Overall view |
| `open_widget` | เปิดเป็น Widget | Open as widget |
| `pause_rotation` | หยุดวนค้างไว้ | Pause rotation |
| `resume_rotation` | วนต่อ | Resume rotation |
| `paused` | ⏸ ค้าง | ⏸ Paused |
| `utilisation` | % การใช้งาน | % utilisation |
| `over_time` | ย้อนหลัง 3 นาที | over 3 minutes |
| `no_link` | ไม่ทราบความเร็วลิงก์ | link speed unknown |
| `base_speed` | ความเร็วพื้นฐาน | Base speed |
| `of_slots` | จาก | of |
| `slots` | สล็อต | slots |
| `online` | Online | Online |
| `offline` | Offline | Offline |
| `unallocated` | ยังไม่จัดสรร | unallocated |
| `virtual_drives` | ไดรฟ์เสมือน | Virtual drives |
| `network_drives` | ไดรฟ์เครือข่าย | Network drives |
| `updates` | อัปเดต | Updates |
| `settings` | ตั้งค่า | Settings |
| `manual` | คู่มือ | Manual |
| `about` | เกี่ยวกับ | About |
| `about_version` | เวอร์ชัน | Version |
| `about_desc` | มอนิเตอร์สถานะเครื่อง (CPU, RAM, Disk, GPU, Network) แบบ Widget บน Windows | A Windows desktop widget for CPU, RAM, disk, GPU and network status. |
| `about_developer` | ผู้พัฒนา | Developer |
| `about_copyright` | ลิขสิทธิ์ | Copyright |
| `about_terms` | เงื่อนไขการใช้งาน | Terms |
| `about_terms_text` | สงวนลิขสิทธิ์ทั้งหมด | All rights reserved. |
| `about_credit` | เครดิต | Credits |
| `about_credit_text` | ต่อยอดจากโครงการ SystemMonitor ของ | Built on the SystemMonitor project by |
| `about_source` | เปิดหน้าโครงการบน GitHub | Open the project on GitHub |
| `manual_1_h` | หน้า Widget | The widget |
| `manual_1_t` | แสดงค่าทีละหมวด (CPU, RAM, Disk, GPU, Network) และเปลี่ยนหน้าเองทุก 5 วินาที กดปุ่มลูกศรซ้าย/ขวาเพื่อเลื่อนดูเอง กดปุ่มหยุด (⏸) เพื่อค้างหน้าที่ดูอยู่ ลากเพื่อย้ายหน้าต่าง ลากขอบเพื่อปรับขนาด | Shows one group at a time (CPU, RAM, Disk, GPU, Network) and turns the page every 5 seconds. Use the left and right arrows to step through yourself, and the pause button (⏸) to hold the page you are on. Drag to move the window; drag an edge to resize it. |
| `manual_2_h` | หน้ารวม (Overall) | Overall |
| `manual_2_t` | ดับเบิลคลิกที่ Widget เพื่อเปิดหน้ารวม แถบซ้ายมีปุ่มนำทางและการตั้งค่าที่ใช้บ่อย ฝั่งขวาแสดงทุกค่า ยิ่งขยายหน้าต่างตัวอักษรยิ่งโตตาม | Double-click the widget to open the overall view. The left bar holds navigation and the settings you reach for most; the right side shows every reading. The bigger the window, the bigger the text. |
| `manual_3_h` | หน้าข้อมูลเต็ม (Full) | Full view |
| `manual_3_t` | ดับเบิลคลิกอีกครั้งที่หน้ารวม หรือกด F11 เลือกอุปกรณ์ทางซ้าย ดูกราฟย้อนหลัง 3 นาทีทางขวา กด Esc หรือ F11 เพื่อย้อนกลับทีละขั้น | Double-click again in the overall view, or press F11. Pick a device on the left and read its last three minutes as a graph on the right. Esc or F11 steps back one level. |
| `manual_4_h` | ไดรฟ์และดิสก์ | Drives and disks |
| `manual_4_t` | ดับเบิลคลิกที่แถวไดรฟ์ในหน้ารวม หรือแท็บดิสก์ในหน้าข้อมูลเต็ม เพื่อเปิด File Explorer คลิกขวาเพื่อเลือก File Explorer, Disk Cleanup หรือ Disk Management เมื่อไดรฟ์ในเครื่องใช้พื้นที่ถึง 70% จะมีแถบเตือน กดที่แถบเพื่อเปิด Disk Cleanup | Double-click a drive row in the overall view, or a disk tab in the full view, to open File Explorer. Right-click for File Explorer, Disk Cleanup or Disk Management. When a local drive is 70% full a warning bar appears; click it to open Disk Cleanup. |
| `manual_5_h` | เมนูคลิกขวา | Right-click menu |
| `manual_5_t` | คลิกขวาที่หน้าต่างเพื่อขยาย/ย่อ, เต็มจอ, รีเซ็ตขนาดหน้าต่าง, รันโปรแกรมใหม่ หรือปิดโปรแกรม | Right-click the window to expand or collapse it, go full screen, reset the window size, restart the program or close it. |
| `manual_6_h` | ตั้งค่า | Settings |
| `manual_6_t` | ปุ่ม ตั้งค่า ในหน้ารวมเปิดหน้าต่างตั้งค่า: หน้าสุดเสมอ, ดูดขอบจอ, เปิดพร้อม Windows, โหมดสว่าง, ความโปร่งใส, ขนาดตัวอักษร, เมื่อกดปิด, ความถี่ในการอ่าน และภาษา | The Settings button in the overall view opens the settings window: always on top, snap to edge, start with Windows, light mode, opacity, font size, when closing, reading frequency and language. |
| `manual_7_h` | ย่อลง Tray | Minimise to the tray |
| `manual_7_t` | เลือก ย่อลง Tray ในส่วน เมื่อกดปิด แล้วการกดปิดจะซ่อนโปรแกรมไว้ที่ถาดระบบ คลิกไอคอนเพื่อเปิดกลับ | Choose Minimise to tray under When closing and the close button hides the program in the notification area. Click its icon to bring it back. |
| `manual_8_h` | อัปเดต | Updates |
| `manual_8_t` | โปรแกรมตรวจหาเวอร์ชันใหม่ทุกครั้งที่เปิด (ปิดได้ในส่วนอัปเดต) หรือกดปุ่มตรวจเอง เมื่อพบ ปุ่มจะเปลี่ยนเป็น ดาวน์โหลดและติดตั้ง | The program looks for a newer version every time it opens (you can turn that off under Updates), or press the button to check yourself. When one is found the button becomes Download and install. |
| `manual_9_h` | อุณหภูมิ | Temperatures |
| `manual_9_t` | n/a = เครื่องไม่มีเซ็นเซอร์วัดอุณหภูมิ  •  ~ = ค่าประมาณจากโหลด ไม่ใช่ค่าที่วัดจริง | n/a = this machine has no temperature sensor  •  ~ = estimated from load, not measured |
| `manual_10_h` | SMART ของดิสก์ | Disk SMART |
| `manual_10_t` | เปิดได้ที่ตัวเลือก \"แสดง SMART ของดิสก์\" ในกลุ่มข้อมูลที่แสดงผลที่แถบข้าง ต้องใช้สิทธิ์ผู้ดูแลระบบและ DiskInfoCore.dll ในโฟลเดอร์ส่วนเสริม SMART โปรแกรมจะอ่านค่าทุกประมาณ 2 นาที ซึ่งอาจปลุกฮาร์ดดิสก์ที่หลับอยู่ จึงปิดไว้เป็นค่าเริ่มต้น แถบแสดงอายุการใช้งานที่เหลือ สำหรับดิสก์ที่ไม่มีค่านี้จะแสดงแถบเต็มตามสีของสถานะ | Turn it on with \"Show disk SMART\" among the display options in the sidebar. It needs the app to run as administrator and DiskInfoCore.dll in the SMART extension folder. It reads about every 2 minutes, which can wake a sleeping hard disk, so it is off by default. The bar shows life left; a disk with no such gauge shows a full bar in the colour of its health. |
| `disk_open_explorer` | เปิด {0} ใน File Explorer | Open {0} in File Explorer |
| `disk_cleanup` | Disk Cleanup ({0}) | Disk Cleanup ({0}) |
| `disk_manager` | Disk Management | Disk Management |
| `disk_warn` | ⚠ ไดรฟ์ {0} พื้นที่เหลือน้อย (ใช้ {1}%) · คลิกเพื่อล้างข้อมูล | ⚠ Drive {0} is running low ({1}% used) · click to free space |
| `disk_hot` | ⚠ ไดรฟ์ {0} เกือบเต็ม (ใช้ {1}%) · คลิกเพื่อล้างข้อมูล | ⚠ Drive {0} is almost full ({1}% used) · click to free space |
| `mem_processes` | โปรเซสในหน่วยความจำ | Processes in memory |
| `mem_services` | เซอร์วิส | Services |
| `mem_search` | ค้นหาชื่อโปรเซสหรือเซอร์วิส | Search a process or service |
| `mem_group_note` | แสดง {0} จาก {1} รายการ · พิมพ์ค้นหาเพื่อดูที่เหลือ | Showing {0} of {1} · search to see the rest |
| `mem_service_note` | แสดง {0} จาก {1} เซอร์วิส · พิมพ์ค้นหาเพื่อดูที่เหลือ | Showing {0} of {1} services · search to see the rest |
| `mem_count` | {0} โปรเซส | {0} processes |
| `mem_private` | ส่วนตัว {0} | private {0} |
| `mem_no_access` | ไม่มีสิทธิ์อ่าน | no access |
| `mem_kill` | จบโปรเซส | End process |
| `mem_kill_group` | จบทั้งกลุ่ม | End all |
| `mem_protected` | โปรเซสของระบบ จบไม่ได้ | System process — cannot be ended |
| `mem_graph` | คลิกชื่อเพื่อดูกราฟ RAM ย้อนหลัง | Click a name to see its RAM history |
| `mem_confirm_kill` | จบโปรเซส {0} (PID {1}) ?\nข้อมูลที่ยังไม่บันทึกในโปรแกรมนั้นจะหายไป | End process {0} (PID {1})?\nUnsaved data in that program will be lost. |
| `mem_confirm_kill_group` | จบ {0} ทั้ง {1} โปรเซส ?\nข้อมูลที่ยังไม่บันทึกในโปรแกรมนั้นจะหายไป | End all {1} {0} processes?\nUnsaved data in that program will be lost. |
| `mem_confirm_host` | โปรเซสนี้เป็นที่รันของ {0} เซอร์วิส จบแล้วเซอร์วิสเหล่านั้นจะหยุดด้วย | This process hosts {0} services; ending it stops them too. |
| `mem_confirm_stop` | หยุดเซอร์วิส {0} ? | Stop service {0}? |
| `mem_confirm_start` | เริ่มเซอร์วิส {0} ? | Start service {0}? |
| `mem_confirm_restart` | รีสตาร์ตเซอร์วิส {0} ? | Restart service {0}? |
| `mem_confirm_startup` | ตั้งเซอร์วิส {0} ให้เริ่มแบบ \"{1}\" ? | Set service {0} to start \"{1}\"? |
| `mem_stop` | หยุด | Stop |
| `mem_start` | เริ่ม | Start |
| `mem_restart` | รีสตาร์ต | Restart |
| `mem_startup` | ประเภทการเริ่ม | Startup type |
| `mem_auto` | อัตโนมัติ | Automatic |
| `mem_manual` | ด้วยตนเอง | Manual |
| `mem_disabled` | ปิดใช้งาน | Disabled |
| `mem_other` | ระบบ | System |
| `mem_running` | ทำงานอยู่ | Running |
| `mem_stopped` | หยุดอยู่ | Stopped |
| `mem_pending` | กำลังเปลี่ยนสถานะ | Changing |
| `mem_shared` | ใช้โปรเซสร่วมกับ {0} เซอร์วิส (RAM เป็นของทั้งกลุ่ม) | shares its process with {0} services (the RAM belongs to the group) |
| `mem_failed` | ทำไม่สำเร็จ: {0} | Failed: {0} |
| `mem_cancelled` | ยกเลิก: ไม่ได้รับสิทธิ์ผู้ดูแลระบบ | Cancelled: administrator permission was not given |
| `mem_top` | ใช้ RAM สูงสุด: {0} | Top RAM: {0} |
| `auto_check_updates` | ตรวจหาเวอร์ชันใหม่ทุกครั้งที่เปิด | Check for updates at start |
| `check_updates` | ตรวจหาเวอร์ชันใหม่ | Check for updates |
| `checking` | กำลังตรวจสอบ... | Checking... |
| `up_to_date` | เป็นเวอร์ชันล่าสุดแล้ว | You have the newest version |
| `check_failed` | ตรวจสอบไม่สำเร็จ ลองใหม่อีกครั้ง | Could not check. Try again. |
| `update_found` | พบเวอร์ชัน | Version |
| `download_install` | ดาวน์โหลดและติดตั้ง | Download and install |
| `downloading` | กำลังดาวน์โหลด | Downloading |
| `download_failed` | ดาวน์โหลดไม่สำเร็จ | Download failed |
| `installing` | กำลังเปิดตัวติดตั้ง... | Starting the installer... |
| `current_version` | เวอร์ชันปัจจุบัน | Current version |
| `show_network_drives` | แสดงไดรฟ์เครือข่าย | Show network drives |
| `drives` | ไดรฟ์ | drives |
| `gpu` | GPU | GPU |
| `show_gpu` | แสดง GPU | Show GPU |
| `gpu_memory` | หน่วยความจำกราฟิก | Graphics memory |
| `dedicated` | เฉพาะการ์ด | Dedicated |
| `shared` | แบ่งจากระบบ | Shared |
| `sockets` | ซ็อกเก็ต | Sockets |
| `logical` | ตัวประมวลผลเชิงตรรกะ | Logical processors |
| `virtualisation` | Virtualization | Virtualization |
| `enabled` | เปิดใช้งาน | Enabled |
| `disabled` | ปิดอยู่ | Disabled |
| `type` | ชนิด | Type |
| `model` | รุ่น | Model |
| `disk_smart` | อุณหภูมิดิสก์แบบเต็ม (ผู้ดูแลระบบ) | Full disk temperature (administrator) |
| `disk_smart_hint` | ดิสก์ USB และ NVMe ภายนอกบางตัวบอกอุณหภูมิได้เฉพาะเมื่อโปรแกรมเปิดแบบผู้ดูแลระบบ โปรแกรมจะไม่ขอสิทธิ์นี้เองและทำงานแบบปกติต่อไปถ้าคุณไม่เลือก | Some USB and external NVMe drives only report a temperature when the app runs as administrator. The app never asks for this on its own and keeps running normally if you do not choose it. |
| `restart_admin` | รันใหม่แบบผู้ดูแลระบบ | Restart as administrator |
| `admin_active` | กำลังทำงานแบบผู้ดูแลระบบ: อ่านอุณหภูมิดิสก์จาก SMART ได้ | Running as administrator: disk temperature is read from SMART |
| `admin_missing_core` | ไม่พบ DiskInfoCore.dll ในโฟลเดอร์ส่วนเสริม SMART จึงอ่านจาก SMART ไม่ได้ | DiskInfoCore.dll was not found in the SMART extension folder, so SMART cannot be read |
| `extensions` | ส่วนเสริม | Extensions |
| `show_smart` | แสดง SMART ของดิสก์ | Show disk SMART |
| `ext_status_not_approved` | ยังไม่ได้รับอนุญาตให้ทำงาน | Not approved to run |
| `ext_status_not_trusted_when_elevated` | ไม่อนุญาตให้ทำงานเมื่อโปรแกรมทำงานด้วยสิทธิ์ผู้ดูแลระบบ | Not permitted to run when the application is elevated |
| `ext_status_contract_mismatch` | เวอร์ชันของอินเทอร์เฟซไม่ตรงกัน | Interface contract version mismatch |
| `ext_status_host_too_old` | โปรแกรมหลักเวอร์ชันเก่าเกินไปสำหรับส่วนเสริมนี้ | Host application is too old for this extension |
| `ext_status_hash_mismatch` | ไฟล์ส่วนเสริมถูกแก้ไขหรือความสมบูรณ์ไม่ตรงกัน | Extension files modified or hash mismatch |
| `ext_status_unsafe_path` | โฟลเดอร์ของส่วนเสริมมีความเสี่ยงด้านความปลอดภัย | Extension folder path is unsafe or insecure |
| `ext_status_file_missing` | ไม่พบไฟล์ของส่วนเสริม | Extension file not found |
| `ext_status_manifest_invalid` | ไฟล์ Manifest ของส่วนเสริมไม่ถูกต้อง | Invalid extension manifest |
| `ext_status_entry_not_found` | ไม่พบคลาสหลักของส่วนเสริม | Extension entry point not found |
| `ext_status_load_failed` | โหลดส่วนเสริมไม่สำเร็จ | Failed to load extension |
| `ext_status_duplicate_id` | รหัสส่วนเสริมซ้ำกับส่วนเสริมอื่น | Duplicate extension identifier |
| `ext_status_disabled` | ถูกปิดการใช้งาน | Disabled |
| `ext_approve_button` | อนุมัติ... | Approve... |
| `ext_elevated_hint` | เปิด SystemMonitor ในโหมดปกติ (ไม่ใช่ผู้ดูแลระบบ) เพื่ออนุมัติหรือใช้งานส่วนเสริมนี้ | Open SystemMonitor normally (not as administrator) to approve or use this extension. |
| `ext_approve_title` | อนุมัติส่วนเสริม | Approve Extension |
| `ext_approve_body` | คุณต้องการอนุมัติให้ส่วนเสริมนี้ทำงานใน System Monitor หรือไม่?\n\nรหัส: {1}\nSHA-256 ของ Manifest: {3}...\nชื่อ: {0}\nเวอร์ชัน: {2}\nไฟล์:\n{4}\n\nคำเตือน: ส่วนเสริมจะทำงานด้วยสิทธิ์เดียวกับคุณภายในโปรแกรม อย่าอนุมัติส่วนเสริมที่คุณไม่มั่นใจในที่มา | Do you want to approve this extension to run in System Monitor?\n\nID: {1}\nManifest SHA-256: {3}...\nName: {0}\nVersion: {2}\nFiles:\n{4}\n\nWarning: The extension will run with your user rights inside the app. You should only approve extensions from a source you trust. |
| `ext_approve_native_warning` | ข้อควรระวัง: ส่วนเสริมนี้มีการโหลดเนทีฟโค้ด (.dll) | Caution: This extension loads native code (.dll). |

199 keys (th=199, en=199).
