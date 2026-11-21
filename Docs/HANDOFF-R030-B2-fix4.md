# Handoff — R-030 B2 hands-on fix 4

From: Coding Owner · To: Writing Owner 2. From the hands-on screenshots and
Reviewer2 on fixes 1–3.

1. **Widget title reserve on resize** (Reviewer2): `WidgetView.cs` ~231 computes
   the title's right reserve with `_factor`, but `SetScale` (~173) changes the
   factor without rebuilding the title row, so at 200×64 the title overlaps the
   pause control. Recompute on scale change, or keep the reserve in panel
   coordinates.
2. **Per-core card titles truncated** ("Co… 22% · 85°C"): the small cards on
   the CPU page must show the full core label. Use the short label `C0`…`C7`
   (like Overall) in the small-card title and put the clock (`1.2 GHz` from
   `Core.Mhz`, omitted when null) on the value line; make small cards wide
   enough that `C7 · 100% · 105°C` fits at 100 % font size (or let the title
   wrap to two lines instead of ellipsis).
3. **Duplicate "Settings"**: inside the Settings expander the row that opens
   the Settings window is also labelled "Settings". Rename that row to
   "More settings…" / "ตั้งค่าเพิ่มเติม…" (new string key).
4. **Text ⚡ glyph**: `FullBoard`/`SidebarBoard` append a `⚡` text glyph to
   battery values ("100% ⚡"); IBM Plex Sans Thai has no such glyph. Drop the
   glyph from the text and, in the sidebar item and module page header, draw
   the same power icon used by the fix-3 power badge when on AC/charging.
5. **Nav highlight**: verify only the selected nav row (Overall/Apps/Processes)
   uses the selected style; a hovered row uses the hover style and returns to
   normal when the pointer leaves (screenshot showed Overall tinted while Apps
   was selected — confirm whether this is hover; fix if a stale state).

No test edits; never launch the GUI; scratch only under `$HOME/.cache/sysmon-wo2/`.
Build + `dotnet test`; report files changed, commands, results, tests to add.
No commit.
