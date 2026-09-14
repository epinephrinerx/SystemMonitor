# SysMonitor 2.0 — working notes for Claude

Native Windows desktop widget. Python 3.14 stdlib only — tkinter (Tk 9.0) for the UI,
ctypes for Win32. Replaces two Electron prototypes in `..\Claude\1.0.5` and `..\Gemini\1.0.4`.

`README.md` is the full documentation. This file is the short list of things that are
easy to get wrong.

## Rules for working on this machine

**Never run load-generation tests** — no CPU spinners, memory ballooning, or disk churn.
I did this once while hunting the fatal error and the user's CPU overheated; they killed
the task and asked that diagnosis not "torture the machine". Diagnose passively instead:
read the diagnostics log, instrument a single run, search upstream bug trackers. If a test
would raise system load, say what it will do and get agreement first.

**Stdlib only.** No psutil, no PyQt/PySide, no pip installs. Everything goes through
`ctypes` in `sysmonitor/win32.py`. The old 45 MB / 0.57% CPU figures predate the
isolated sampling process; do not describe them as measurements of current source.

## Known issue: `alloc: invalid block` fatal error

The reported panic comes from Tcl's allocator. Its cause in this app remains
unproven. CPython #66999 / bpo-22810 describes a similar message after a file
dialog; that report does not establish that our crash was caused by a second
thread or rule out an application bug.

Earlier mitigation shipped 2026-09-14: sampling moved to the Tk event loop;
those installed copies log `sampler=event-loop`.

Current source, after review: `ProcessSampler` uses one isolated sensor process
and polls snapshots from `ui.py:_tick`. The worker never creates Tk. Confirm with
`sampler=process`. A stuck sample is terminated after 20 seconds, with a five-minute
restart backoff; no blocking join runs on the UI event loop. The default
`sampler_thread: false` now selects this process mode; true selects the legacy
thread. Keep `multiprocessing.freeze_support()` before the app import in the
guarded entry point. Onefile bootloaders may add processes; remeasure total
resource usage before publishing performance claims.

**Long-run stability is unproven.** Earlier notes recorded no reproduction after
10:48 on 2026-09-14 (0 in ~56 runs). Prior checks found clean buffer canaries and
no off-thread Tcl calls (0 of 4,725); these observations do not conclusively rule
out all buffer, threading, UI, or allocator defects. Read the log before diagnosis.

If it recurs, read the log first. Unapproved fallbacks previously offered:
`RegisterApplicationRestart()`, or a Qt port (the sensor layer — `win32.py`, `sensors.py`,
`config.py`, `i18n.py` — carries over unchanged; only the UI is rewritten).

## Traps that have already cost time

**The installed copy is separate from `dist\`.** Verifying `dist\SysMonitor.exe` proves
nothing about what the user is running. The installed binary lives in
`%LOCALAPPDATA%\Programs\SysMonitor`. After a rebuild, install it:
`dist\SysMonitor-Setup.exe /S`. `build.cmd` warns when the installed copy is stale.

**Tk 9.0 leaks ~80 bytes per canvas item create+delete.** Never `canvas.delete("all")` on
a redraw path. All drawing goes through `sysmonitor/painter.py`, which retains items by key
and only updates coords/options that changed. Retain one item per key/primitive
pair, hide unused ones, and restore draw order on reuse. The review fixed deletion
on automatic mini-view rotation too. Earlier 378.7 -> 0.2 MB/day measurements
are historical; current long-run memory usage has not been remeasured.

**Temperature descriptor layout.** `TemperatureInfo` starts at byte 24, and the
first signed temperature is at byte 26. Validate descriptor length, declared
size and count before reading; reserved bytes at 18 are not a temperature.

**Disk I/O timing.** Keep counters and successful-read timestamps together per
drive. Skipped probes must not advance that drive's baseline. Remove stale caches
when a drive disappears.

**Uninstall ownership.** Never walk/delete an install directory recursively.
Use `owned_paths()` and the explicit program/settings filename lists. Only an
installed frozen `uninstall.exe` may schedule its own removal; use literal paths,
never interpolate them into cmd.exe. Preserve unrecognized files/subdirectories.

**Diagnostics.** Tk uses `report_callback_exception`, not `sys.excepthook`.
`diag.install_tk_handler()` bridges it; caught sampler/probe/IPC errors go through
rate-limited `diag.report_exception()`. Keep UI timers running after recoverable errors.

**`subprocess` in a `--noconsole` build has invalid std handles** → `WinError 6`. Pass
explicit `stdin/stdout/stderr=DEVNULL`; `tools/installer.py:run_quiet()` does this.

**Git Bash rewrites a bare `/S` argument into a Windows path.** Run the installer from
PowerShell, or the wizard opens on the user's screen instead of installing silently.

## Where things are

| | |
|---|---|
| Diagnostics log | `%APPDATA%\SysMonitor\sysmonitor.log` (startup banner, 10-min heartbeat, excepthooks, capped 256 KB) |
| Config | `%APPDATA%\SysMonitor\config.json` — back it up before tests that touch settings, and restore window position afterward |
| Install dir | `%LOCALAPPDATA%\Programs\SysMonitor`, `HKCU` Run key for autostart |
| Build | `build.cmd` → `dist\SysMonitor.exe` + `dist\SysMonitor-Setup.exe` |

## Communication

The user writes in Thai; reply in Thai. Code, comments, and docs stay in English.

## Review validation — 2026-09-14

`python -B -m unittest discover -s tests -v`: 20 tests passed, including actual
process spawn/IPC using simulated sensors. No hardware stress, UI windows,
registry writes, user config changes, install or uninstall was performed.
Source has been updated; dist and the installed copy have not been rebuilt or
replaced. Frozen startup, live UI layout and long-run stability remain unverified.
See `CHANGELOG.md` for the review record.

## Follow-up testing/build request — 2026-09-14

The user requested tests, commit/push and an EXE build, explicitly excluding the
installer. Use `build.cmd --app-only`; it reuses the icon and leaves setup alone.
Do not run the installer as part of this request. All 21 tests passed with
`SYSMONITOR_TK_TEST=1`, including actual Tk rendering with synthetic data and
mocked settings saves. Native Computer Use could not target the frameless widget;
the Tk integration check exercises its rendering API instead. First successful
worker results now emit `sampler ready cores=... disks=... worker_pid=...`.
This directory initially had no Git repository or remote. A local `main` repo
was initialized. The user specified `https://github.com/epinephrinerx/SystemMonitor`
as the `origin` remote; its initial remote-ref check returned no existing refs.

Standalone build completed from source commit `7b8d91d` (pushed to origin/main):
`dist/SysMonitor.exe` is 13,648,217 bytes. Its isolated startup test logged
`frozen=True`, `sampler=process`, then `sampler ready cores=16 disks=7` with no
exception during the brief check. The installer hash stayed unchanged and the
installed copy was not replaced. See CHANGELOG.md for hashes and test limits.
