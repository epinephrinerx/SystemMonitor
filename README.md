# SysMonitor 2.0 — native Windows widget (Python)

> **Retired.** This Python / Tk build is no longer developed. The product is
> the C# / WPF build in `SysMonitor.Wpf` — see `SysMonitor.Wpf/README.md`.
> What follows is kept because it documents the sensor behaviour the C# port
> was written against, and because the build still runs.

A rewrite of the two earlier Electron prototypes (`Claude/1.0.5` and
`Gemini/1.0.4`) as a **native Windows app**, using nothing but
the Python standard library: `tkinter` for the window, `ctypes` for direct
Win32 syscalls.

No Node, no Chromium, no `npm install`, no third-party packages — not even
`psutil`.

The current source uses one Tk UI process and one isolated sensor worker. Slow
hardware calls cannot block the window, and no sensor thread runs inside Tk.
PyInstaller onefile bootloaders can add OS processes beyond these two Python
processes. See [CHANGELOG.md](CHANGELOG.md) for the 2026-09-14 review fixes.

---

## Why one app and not two

The two source projects share one lineage. `Claude/1.0.5` is `Gemini/1.0.4`
plus the drive-info additions (type / interface / R-W speed); its own README
says so. Building two near-identical ports would have duplicated everything,
so this is a single app carrying the **union** of both feature sets.

---

## Historical measurements (before the 2026-09-14 review fixes)

The table below describes the earlier single-process implementation, **not the
current source**. CPU, total process count, combined memory, and packaged sizes
have not been remeasured after introducing process isolation. The heartbeat RSS
reports the UI process only, not the worker or onefile bootloaders.

Both versions were run on this machine and sampled over the same 30-second
window, idle desktop:

| | Electron v1.0.5 | **Earlier native build** | |
|---|---|---|---|
| Processes | 4 | **1** | |
| CPU (one core) | 14.66 % | **0.57 %** | ~26× less |
| CPU (of 16 cores) | 0.92 % | **0.04 %** | |
| Working set | 318.9 MB | **32.3 MB** | ~10× less |
| Working set (packaged .exe) | 318.9 MB | **44.8 MB** | ~7× less |
| Private bytes | 155.4 MB | **15.2 MB** | ~10× less |
| Install size | ~250 MB `node_modules` | **~60 KB of source** | |
| Dependencies | electron + systeminformation | **none** | |

Worth noting: the old README claimed "1–3% CPU". Measured, it is 14.66 % of a
core. The claim was never verified against the running app.

---

## Feature checklist (from the 1.0.5 requirements)

| # | Requirement | Status |
|---|---|---|
| 1 | Light / Dark theme, switchable | done |
| 2 | Auto-detect logical cores | done — via `NtQuerySystemInformation`, 16 detected here |
| 3 | Small draggable widget, snap to screen edge, double-click to expand | done — 270×90 mini, 630×480 expanded (both resizable), 25 px snap |
| 4 | Temperatures for CPU / RAM / disks, each toggleable | done, with real sensors where Windows exposes them (see below) |
| 5 | Temperature alert: green <65, yellow 65–79, red + blink ≥80 | done, with a flame marker |
| 6 | Drive detail: type, interface, R/W speed, usage, temperature | done — **real** SSD/HDD + NVMe/SATA/USB detection and real MB/s |
| — | Thai / English UI (1.0.5 i18n) | done |
| — | Eco / Balanced / Fast refresh rates (1.0.5 performance-config) | done |
| — | Opacity slider, always-on-top, settings persistence | done |
| — | **Resizable windows** (both mini and expanded), size remembered | done |
| — | Packaged .exe + per-user installer with uninstaller | done |

---

## About the temperature readings — read this

The Electron builds **fabricated** their temperatures. In `main.js`:

```js
const mockTemp = 40 + Math.floor(usage * 0.4);   // CPU
temp: 35                                         // RAM, constant
temp: 40 // Mock temperature                     // every disk, constant
```

Those numbers were displayed as if they came from sensors. This build does not
do that. It reads what Windows actually exposes and is explicit about the rest:

- **Disk** — real, via `IOCTL_STORAGE_QUERY_PROPERTY`
  (`StorageDeviceTemperatureProperty`). Works on many NVMe/SATA SSDs on
  Windows 10 1803+. On *this* machine the storage driver returns
  `ERROR_INVALID_FUNCTION` unelevated, so it shows `n/a`.
- **CPU** — no per-core sensor is readable without a kernel driver. Optionally
  reads the ACPI thermal zone (`wmi_cpu_temp`, off by default: it needs admin
  and spawns a PowerShell process, which is exactly the cost this design
  avoids). Otherwise it shows the same load-based model the old app used, but
  prefixed with `~` so you can see it is an estimate.
- **RAM** — consumer DIMMs have no sensor Windows can read. Always `n/a`.
  The old app's constant 35 °C was meaningless.

Every temperature is drawn with a small **thermometer glyph** so the value is
never an unlabelled number: `n/a` reads as *no temperature sensor*, not as
some unknown quantity. The Disk Storage header repeats this in words when any
drive lacks a sensor, and a full legend sits at the bottom of the panel.
`~` means modelled, not measured. Set `"temp_estimate": false` in the config to hide estimates
entirely.

Genuine per-core CPU temperatures require a signed kernel driver (the approach
LibreHardwareMonitor/HWiNFO take) — out of scope for a stdlib widget.

---

## Running it

Requires Python 3.9+ with tkinter (standard on python.org installers).
Tested on Python 3.14.7 / Tk 9.0, Windows 11.

```
run.cmd            start it (no console window)
debug.cmd          same, but keeps the console so tracebacks are visible
```

Or directly:

```
pythonw SysMonitor.pyw
python  SysMonitor.pyw --reset     restore default settings
```

---

## Using it

**Mini widget (270×90)**

- Drag anywhere on it to move; release near a screen edge to snap flush.
- Cycles CPU → Memory → Disk every 5 seconds.
- **Double-click** to expand.
- Right-click for a small menu (expand / close).

**Expanded panel (630×480)**

- Left sidebar: window options, what to show, refresh rate, language, opacity.
- CPU can be shown per-core or combined; disks per-drive or combined.
- Mouse wheel scrolls the content when it overflows (e.g. 16+ cores).
- Double-click, the collapse button, or `Esc` returns to the mini widget.

**Resizing** (both the mini widget and the expanded panel)

- Drag the **grip in the bottom-right corner**, or the right / bottom edge.
  The cursor changes when you are over a resize zone.
- The mini widget and the expanded panel keep *separate* sizes, and both are
  remembered between runs.
- Minimums: 200x64 (mini), 470x300 (expanded). Right-click -> **Reset window
  size** puts both back to the defaults.
- Aim at the grip glyph rather than the extreme corner: the widget has real
  rounded corners, and the pixels outside the curve are transparent, so clicks
  there pass through to the window behind.

Settings and window position are saved to
`%APPDATA%\SysMonitor\config.json` and restored on next launch.

---

## How it stays cheap

- **One window, isolated sampling.** The UI uses Tk canvases without a browser
  engine. A persistent helper process performs the synchronous device reads.
- **Direct syscalls, no shell-outs.** The originals' `systeminformation`
  dependency ran PowerShell in the background to get disk data. Here the same
  facts come from `DeviceIoControl` — tens of microseconds, no process spawn.
- **Tiered sampling.** Cheap counters (CPU, RAM, disk I/O) refresh on the tick;
  free space every 15 s; temperatures every 30 s; drive identity every 60 s
  (it is static). Eco mode stretches all of these.
- **Repaint only on change.** The UI polls every 400 ms but redraws only when
  a new sample landed, the mini view rotated, or a hot badge blinked.
- **Canvas items are reused across layouts.** Each key/primitive pair is drawn
  once, then moved/restyled. Items absent from a frame are hidden and restored
  when needed, including automatic CPU/Memory/Disk rotation. Draw order and
  explicitly hidden controls are preserved.
- **Sampling runs in a separate process.** The Tk event loop polls completed
  snapshots; it never waits for device I/O or WMI. A sample exceeding 20 seconds
  causes the helper to be terminated, readings to be marked unavailable, and a
  restart to be attempted after five minutes. Probes which return after 250 ms
  are backed off for five minutes inside the helper. That backoff alone is not
  a timeout. `sampler_thread: true` retains the legacy thread mode for diagnosis.

---

## Layout

```
Native/
  SysMonitor.pyw        entry point (pythonw = no console)
  run.cmd / debug.cmd   launchers
  sysmonitor/
    painter.py          keyed retained drawing (reuses canvas items)
    win32.py            ctypes bindings: NtQuerySystemInformation,
                        GlobalMemoryStatusEx, IOCTL_DISK_PERFORMANCE,
                        IOCTL_STORAGE_QUERY_PROPERTY, monitor work area
    sensors.py          synchronous probes and optional legacy thread -> Snapshot
    process_sampler.py  isolated worker, IPC polling, timeout/restart
    ui.py               the canvas-drawn widget (mini + expanded)
    theme.py            dark/light palettes, temperature thresholds
    i18n.py             Thai / English strings
    config.py           %APPDATA% settings
    app.py              wiring
    diag.py             heartbeat and rate-limited exception logging
  tests/                regression tests with synthetic devices and mock canvas
  build.cmd             builds both .exe files into dist
  version_info.txt      Windows file-version resource
  SysMonitor.ico        app icon (regenerated by tools/make_icon.py)
  tools/
    installer.py        the setup wizard / uninstaller
    make_icon.py        icon generator (build-time only, needs Pillow)
  dist/                 build output: SysMonitor.exe, SysMonitor-Setup.exe
```

---

## Building the .exe and the installer

```
build.cmd
```

To build only the portable application, keeping the existing icon and leaving
the installer untouched:

```powershell
.\build.cmd --app-only
```

Installs PyInstaller if missing, regenerates the icon, and produces two files
in `dist\`:

| File | Size | What it is |
|---|---|---|
| `SysMonitor.exe` | 12.6 MB | Portable. Copy it anywhere and run it — nothing else needed. |
| `SysMonitor-Setup.exe` | 25.0 MB | Installer, with the application embedded inside it. |

### What the installer does

Installs **per user**, into `%LOCALAPPDATA%\Programs\SysMonitor` — so there is
no UAC prompt and no administrator rights are required.

- Copies the app and its icon
- Start Menu shortcut, and optionally a Desktop shortcut
- Optionally registers to start with Windows (`HKCU\...\CurrentVersion\Run`)
- Registers in **Settings → Apps & installed apps**, with a working uninstaller
- Writes `uninstall.exe` next to the app

Command line:

```
SysMonitor-Setup.exe              wizard
SysMonitor-Setup.exe /S           silent install, default options
SysMonitor-Setup.exe --uninstall  uninstall wizard
uninstall.exe --uninstall /S      silent uninstall
```

Uninstalling removes only the three known program files (`SysMonitor.exe`,
`SysMonitor.ico`, `uninstall.exe`), both shortcuts, the startup entry, the Apps &
features registration and (unless you tick *keep my settings*) the known config
and log files. Other files and subdirectories are preserved; directories are
removed only when empty. Paths are resolved and checked before deletion.
Only the frozen `uninstall.exe` in the registered install directory may schedule
its own deletion. Running setup elsewhere or running from Python never schedules
that executable for deletion. Source-mode installation requires both built EXEs.

The build is unsigned, so SmartScreen may show "Windows protected your PC" on
first run — *More info → Run anyway*. Silencing that permanently needs a code
signing certificate, which has to be bought.

---

## The Tk 9.0 canvas leak

Worth recording, because it dictates how the drawing code is written.

Tk 9.0 leaks memory for every canvas item that is created and then destroyed.
Measured here: **4.5 MB per 60,000 items, perfectly linear with no plateau**,
for every item type (rectangle, polygon, oval, line, text), and identically
when the calls bypass Tkinter and go straight to Tcl -- so it is Tk's own C
layer, not the Python binding.

Drawing the widget the obvious way (`delete("all")`, then re-create everything
each frame) therefore cost:

| Mode | Before | After |
|---|---|---|
| Mini widget | 29.5 MB/day | **0.0 MB/day** |
| Expanded panel | 378.7 MB/day | **0.2 MB/day** |

An earlier run also ended in a Tcl allocator panic (`alloc: invalid block`),
but the cause of that crash has not been established. The same 60,000 operations done as
`coords` + `itemconfigure` on retained items leak 0.01 MB, so `painter.py`
keeps every element under a stable key and never throws one away. A separate
leak of Tcl commands -- 18 per redraw, from re-registering control callbacks
on every frame -- was fixed at the same time by binding each control once and
keeping the sidebar out of the per-sample redraw path.

The 2026-09-14 review found that `Painter.end()` still deleted items on layout
changes, including normal mini-view rotation. This is now fixed by retaining
and hiding them. The historical memory figures above must not be interpreted
as a measurement or long-run stability guarantee for the current implementation.

---

## If you rebuild: the installed copy is separate

`build.cmd` writes to `dist\`. It does **not** touch an already-installed
copy in `%LOCALAPPDATA%\Programs\SysMonitor`. Running a stale install against
freshly fixed source is how an already-fixed memory leak went on biting for
days, so the build now compares the two and prints a warning when they differ.

To update an existing install in place, keeping settings, shortcuts and the
autostart entry:

```
dist\SysMonitor-Setup.exe /S
```

An upgrade reads your current choices first (install location, desktop
shortcut, start-with-Windows) and re-applies them rather than resetting to
defaults -- an earlier version silently deleted the autostart entry on
reinstall.

---

## Diagnostics

A Tcl allocator panic kills the process outright: no Python traceback, no exit
handler, just a message box. When that happened there was nothing on disk to
explain it.

The app now keeps a small log at `%APPDATA%\SysMonitor\sysmonitor.log`:

```
2026-09-14 10:16:10  --- start v2.0.0  python 3.14.7  tcl/tk 9.0/9.0  frozen=True  rss=44.8MB
2026-09-14 10:26:10  alive  mode=expanded rss=  45.1MB cores=16 disks=7
```

The current startup also records `sampler=process` (or `sampler=thread` for the
legacy opt-in). One line at startup, one every ten minutes, plus errors from
Tk callbacks, sampling, individual probes, and IPC. Caught errors include their
tracebacks and are limited to once per minute per context to avoid log floods.
The UI tick reschedules after a recoverable callback failure. If it dies again, the tail of that file
shows whether memory was climbing and what the widget was doing. The file is
capped at 256 KB and truncated. Set `"diagnostics": false` in `config.json`
to switch it off.

## Regression checks

```powershell
python -B -m unittest discover -s tests -v
```

The 20 checks use simulated descriptors/counters, a mock canvas, mocked registry
and deletion operations, and one real spawned worker with synthetic hardware.
They do not sample actual hardware, open a Tk window, modify user settings,
install/uninstall anything, or generate CPU/RAM/disk load. Long-run crash
diagnosis should remain passive; never run stress tests without agreement.

To include the real Tk rendering check (synthetic data, mocked config saves):

```powershell
$env:SYSMONITOR_TK_TEST = '1'
python -B -m unittest discover -s tests -v
```

All 21 tests passed on 2026-09-14. The additional check exercises mini/expanded
views, both display modes, both languages, themes, opacity, resizing and canvas
reuse in actual Tk. A `sampler ready cores=... disks=... worker_pid=...` log entry
confirms the first complete sample from each helper. Packaging validation is
recorded in CHANGELOG.md; the installed copy is always separate from `dist`.

---

## Gotcha: subprocess in a --noconsole build

A PyInstaller windowed build has no valid standard handles. Any child process
that inherits them fails with `OSError [WinError 6] The handle is invalid`.
This is silent and confusing: the same code works perfectly from a console and
dies only in the packaged app.

It broke silent install outright (the first `taskkill` in `install()` threw
before anything was copied, and PyInstaller showed only "Unhandled exception in
script"), and it would have stopped the optional WMI CPU temperature from ever
working in the packaged app.

Every child process is now launched with handles detached explicitly:

```python
subprocess.run(cmd, stdin=subprocess.DEVNULL,
               stdout=subprocess.PIPE, stderr=subprocess.PIPE,
               creationflags=CREATE_NO_WINDOW)
```

---

## Known limits

- `NtQuerySystemInformation` reports the calling processor group, so on
  machines with more than 64 logical processors only the first group's cores
  are listed.
- One drive letter per volume: several letters backed by the same physical
  disk (subst'd or partitioned) each get their own card, matching the old
  behaviour.
- Tk gives a window one uniform alpha, so the opacity slider dims the whole
  panel rather than the CSS per-layer translucency the Electron build had.
  The palette is pre-flattened to compensate.
