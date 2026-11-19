# Handoff — R-027 phase 2a tests (Presentation layer)

From: Coding Owner · To: Test Author (agy, Gemini 3.8 Flash)

Add tests to `SysMonitor.Linux.Tests` for `SysMonitor.Linux/Presentation/`
(no UI). Behaviour reference: `Docs/HANDOFF-R027-phase2a.md` (with its
Amendment) and `Docs/WPF-UI-SPEC-4.3.0.md`. The Writing Owner is applying
the Amendment in parallel: write tests for the intended behaviour.

Cover at least: `AppConfig` (defaults, round-trip in a temp
`XDG_CONFIG_HOME`, corrupt file → defaults + `config.json.bad`, clamping);
`StringData` (every key has Thai and English, no duplicates, the Linux
wording from the Amendment, no "Windows"/"File Explorer" in the visible
keys used on Linux, Manual ends at section 9); `ViewState` switching
table (double-click / F11 / Esc per view); rotation (5 s, pause,
prev/next, page kept by key); widget pages (CPU groups of 4, RAM, each
drive, each adapter, no GPU page); Overall sections (order, one GPU
section per GPU, n/a badges, no invented 0 %); disk alerts (70/90, 2-point
hysteresis, dismiss, rise, forget, removable/network skipped); palette
load and temperature colours; `WindowGeometry` (snap within 25 px,
keep-on-screen, size limits, widget aspect ratio). Use fake snapshots
built in code; no real `/proc`, no window, no sleep. Files only, no shell,
only in `SysMonitor.Linux.Tests/`. Do not commit.

## Round 2 (after Reviewer2)

Tests for Amendment 2 in `Docs/HANDOFF-R027-phase2a.md`: widget resize
through the window↔panel conversion (270×104 panel, drag right edge 54 →
panel 324×124.8); `cpu_temperature = false` hides CPU temperature in widget
and Overall; `include_network = false` drops an `IsNetwork` drive from
pages, rows, totals and alerts; Overall disk total with no drives → `n/a`.
Also remove the two compiler warnings in your files
(`DiskReaderTests.cs:158` CS8602, `StringDataTests.cs:169` CS0219).
Files only, no shell, only in `SysMonitor.Linux.Tests/`.
