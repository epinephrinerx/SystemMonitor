# Handoff — R-027 phase 2a: Avalonia app shell, Widget and Overall views

From: Coding Owner (Claude Code) · To: Writing Owner 2 (opencode, `opencode-go/deepseek-v4.1-flash`)
Branch: `linux-native` · Date: 2026-11-19
Requirement: `Requirements.md` R-027 · Reference: **`Docs/WPF-UI-SPEC-4.3.0.md`** (the Windows 4.3.0 behaviour, taken from the code on `main`)

## Objective

The Linux app must look and behave like Windows 4.3.0. This phase builds the
app shell and two of the three views: **Widget** and **Overall** (with its
sidebar), plus the Settings / Manual / About windows, config, theme and
Thai/English. The **Full** view is phase 2b: in this phase, switching to Full
shows a placeholder panel with the `full_data` title and the Full header
buttons, so the switching rules can already be exercised.

Follow the spec section by section. Where this handoff and the spec differ,
this handoff wins (Linux decisions below). When the spec says "Unclear", pick
the simplest behaviour and list it in your report.

## Projects

- `SysMonitor.Linux` (exists, no packages): add a `Presentation/` folder for
  everything testable without a window. That covers the view model state,
  view switching, widget rotation pages, meter rows and formatting, load and
  temperature colours, disk alerts with hysteresis, the palette tokens, the
  i18n string table (all 199 keys from spec §12, both languages, verbatim),
  config load/save, and window geometry maths (snap, keep on screen, size
  limits, aspect ratio).
- `SysMonitor.Linux.App` (new): `net10.0` Exe, `AssemblyName sysmonitor`,
  Avalonia **12.1.5** (`Avalonia`, `Avalonia.Desktop`,
  `Avalonia.Themes.Fluent` — already in the NuGet cache).
  - This is the **only** project allowed packages; R-027 decision 1 allows it.
  - The project holds views and wiring only. Delete the ignored `bin/obj`
    leftovers in `SysMonitor.Linux.App/` first.
- Do not create test projects; the Test Author writes the tests after you.

## Linux decisions (override the spec)

| Spec item | Linux |
|---|---|
| Config `%APPDATA%\SystemMonitor\config.json` | `$XDG_CONFIG_HOME/SystemMonitor/config.json` (default `~/.config/SystemMonitor/`). Same keys and defaults as spec §7, minus Windows-only keys. Write atomically (temp file + rename). A corrupt file → defaults, keep the bad file as `config.json.bad`. |
| Fonts Segoe UI / Leelawadee | `"Noto Sans, Noto Sans Thai, Ubuntu, sans-serif"` (installed here) |
| Segoe MDL2 glyphs | `PathIcon`s with small inline geometry for each codepoint in the spec's table (close, pause, play, chevrons, full screen, widget, home, settings, manual, about, kill, delete, lock, restart, stop). Simple line icons are fine. |
| Single instance (named mutex) | an exclusive lock on `$XDG_RUNTIME_DIR/sysmonitor.lock` (fallback: the config dir), waiting up to 5 s; `--allow-multiple` skips it |
| Disk drive letters | mount points (`Disk.Letter` holds `/`, `/boot/efi`) |
| Disk tools menu (§4b) | "Open" → `xdg-open <mount>`; "Disk usage" → `baobab <mount>` only if `baobab` is on `PATH`, else hidden; "Disks" → `gnome-disks` only if on `PATH`, else hidden |
| Double-click a drive row | `xdg-open <mount>` |
| Settings window: SMART / admin section | drop it (no elevation, R-027 out-of-scope) |
| Autostart, updater, tray, restart | phase 3 — show the controls but wire them to no-op stubs behind one interface each (`IAutostart`, `IUpdater`, `ITray`, `IRestarter`) |
| `close_action = tray` | until phase 3, behaves like `exit` |
| Command line | `--expanded`, `--full`, `--allow-multiple`, `--heartbeat <s>`; drop the Windows-only switches |
| Fonts/sizes in px | use the spec values as Avalonia DIPs |
| Windows-only items in §11 | skip |

Window: borderless (`SystemDecorations="None"`), `TransparencyLevelHint`
Transparent, `Background="Transparent"`, `ShowInTaskbar` false, `Topmost`
from config. Opacity is per-pixel on the panel brush only (spec §1). Drag
and resize are custom (spec §1). Use `BeginMoveDrag` / `BeginResizeDrag` only
if the spec's 4 px threshold and edge snapping still work; otherwise move the
window yourself.

Data: run `LinuxSampler` (phase 1b) on a background thread. Use the
interval from the `speed` setting (eco / balanced / fast values in spec §7).
Publish the latest `Snapshot` through a volatile field. Use a 400 ms
`DispatcherTimer` to redraw only on a new snapshot or a due rotation
(spec §0). `includeProcesses` is false in this phase. History (72 points)
is pushed on every fresh snapshot.

## Acceptance criteria

1. `~/.dotnet/dotnet build SysMonitor.Linux.App` — 0 errors, 0 warnings.
2. Behaviour from spec §0–§4, §6, §7, §10, §12 is implemented for Widget and
   Overall. That covers switching (double-click / F11 / Esc / buttons /
   context menu), widget rotation (5 s, pause, prev/next, pages per spec),
   the Overall sidebar with every setting, the content cards, meter rows,
   temperature badge and disk alerts. Also: theme light/dark,
   Thai/English live switch, opacity, font scale, always-on-top, snap, one
   shared position, per-view size, and position/size remembered across
   restarts.
3. Every number the views show comes from the `Snapshot`. Show `N/A`
   (i18n) for null. No invented values.
4. `--probe-ui` switch: build the view models from two real samples. Print
   the widget pages and the Overall rows as text to stdout, then exit
   without opening a window. Paste the output in your report.
5. You may launch the app **once** to check that it starts. Close it
   yourself within 30 s (`timeout 30`). Do not leave a window open. Report
   any startup exception.
6. `Docs/ARCHITECTURE.md` §5: add the `SysMonitor.Linux.App` row with
   the Avalonia exception (MIT licence, why, UI project only).

## Scope

`SysMonitor.Linux/Presentation/**` (new), `SysMonitor.Linux.App/**` (new),
small additive changes to `SysMonitor.Linux` sampler API if the UI needs them
(list them), `Docs/ARCHITECTURE.md` §5.

## Constraints

- No packages outside `SysMonitor.Linux.App`. No root. No load tests.
- Do not touch `Requirements.md`, `CLAUDE.md`, `AGENTS.md`, `Docs/AI_WORKFLOW.md`, `Docs/WPF-UI-SPEC-4.3.0.md`.
- Do not commit, push or create branches.
- `export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH`

## Report back

```
## Summary
## Changed        (one line per file)
## Verification   (build; --probe-ui output; launch result)
## Spec deviations and "Unclear" choices
## Open questions / risks
```
