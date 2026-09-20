# Change log

## v3.0.1 — 2026-09-20 — review fixes

An outside review of the C# code found ten things. All ten are addressed here.
None of them broke the widget in normal use; several were claims the code made
about itself that it did not keep.

### Safety

- **The uninstaller could delete through a junction.** `IsSafeTarget` compared
  path strings and stopped there, so `InstallDir\SysMonitor.exe` passed the
  check even when `InstallDir` was a link pointing somewhere else entirely. It
  now walks the parent chain for reparse points and refuses any path that
  passes through one, and the installer refuses to write into such a directory
  in the first place. `InstallerJunctionTests` creates a real junction and
  checks — the previous tests only ever asserted on path *selection*, which is
  not the same claim.
- **The uninstall handover could be given a different executable.** It copied
  itself to a predictable temp path, closed the file, then launched it by
  name; anything that replaced the file in between would have been run
  instead. `ArgumentList` protects the command line, not the image. The copy
  now goes into a freshly created random directory through a handle that
  denies writing and deleting, held open until the child has started.

### Correctness

- **A restored window position was never checked against the screens that
  exist now.** Unplug the monitor it was saved on and the widget opened
  somewhere unreachable. It is clamped once the window has a handle.
- **Resizing drifted across monitors of different scaling.** The drag origin
  was captured in physical pixels but each frame divided by whatever the DPI
  was at that moment. The scale is now captured with the origin and used for
  the whole gesture.
- **Two descriptor parsers trusted the returned buffer length alone.** The
  seek-penalty and adapter parsers now check the size the device declares as
  well, which the temperature parser did from the start.
- **WMI wrappers leaked on error paths**, and the COM enumerator was never
  released at all. Each is released in its own `finally`, and a field a given
  instance does not carry is now a missing value rather than the end of the
  query.
- **`Chart.PenFor` only protected solid brushes.** A gradient went straight
  into a frozen pen, taking the caller's brush with it — the same bug it was
  written to prevent. Anything not solid is cloned first.

### Claims the code did not keep

- **`Snapshot` handed out `List<T>` behind `IReadOnlyList<T>`**, which casts
  straight back. A snapshot crosses a thread boundary and is meant to be
  read-only once published, so it now is.
- **`Chart` said it allocated "almost nothing per frame"** while building
  three pens on every repaint. Pens are cached by brush and width.
- **Three tests asserted less than their names said.** The appearance test
  checked only that a PNG existed and was over a kilobyte; it now checks the
  frame and grid are actually drawn. `The_size_saved_is_the_panel_not_the_window`
  never touched saving and is renamed for what it does, with a separate test
  for the round trip.

116 tests, up from 104.

## v3.0.0 — 2026-09-20 — the C# / WPF build

The widget is now C# on .NET 8 with a WPF front end. No Tcl/Tk in the process
at all, which is what the port was for. The Python build is retired; it still
runs and stays installed, and both can be installed at once because this one
owns `SysMonitor.NET` everywhere the other owns `SysMonitor`.

### Views

- **Mini** rotates one metric every five seconds, with its own close button.
- **Expanded** lists everything beside a settings sidebar. Per-core rows sit
  two to a line; drives, adapters and modules run down the page, because they
  each carry a line of detail that does not survive half width.
- **Full screen** graphs every device under a heading per kind, drawn the way
  Task Manager draws one: framed plot, square grid, solid tint under an angular
  line, axis labels above.

Double-click cycles them, F11 toggles full screen, Escape steps back, and
there are buttons for all of it.

### What it reads

Per-core CPU, memory, per-drive space and throughput, drive temperature,
SSD/HDD and bus detection, LAN and Wi-Fi throughput, installed memory modules,
and a **real CPU temperature** from the ACPI thermal zone — through
`Win32_PerfFormattedData_Counters_ThermalZoneInformation`, which needs no
elevation, unlike the `root\WMI` class the Python build asked for and was
denied every time.

### Interface

Traffic light on every usage bar: its own colour to 70%, amber to 90%, then
red. The temperature thresholds are separate and unchanged at 65/80.

Resizing works from every edge and corner, computed from the rectangle the drag
started on so the opposite side never creeps. Opacity and font size are
sliders that say what they are set to. The close button either exits or hides
to a tray icon, whichever the user picked.

### Measured on this machine

| | Python + Tk | C# + WPF |
|---|---|---|
| exe | 13.6 MB | 0.35 MB (framework-dependent) |
| RSS, mini idle | 25–31 MB | 45–65 MB |
| CPU, mini idle | 0.57% | 1.6% |
| CPU, expanded idle | — | 1.5% |

Release build, measured over 40 s after a 20 s warm-up. WPF costs more memory
than Tk; that is the runtime and it is not going away.

**Full screen is not measured.** A 2560x1440 window with per-pixel alpha is
composited in software, and a reading of about 30% of one core was seen while
the window was open — though the process was being used at the time, so the
number is not clean. Worth measuring properly before leaning on that view.

### Tests

104, no hardware and no installing. The graph is checked by rendering it into
a `RenderTargetBitmap` and counting pixels; the descriptor tests assert the
exact shape of a bug that shipped once, where the drive temperature was read
from a reserved byte and a drive at 55 C reported 0.

## 2026-09-14 — Source review fixes

The review identified six defects. This revision addresses them without adding
third-party dependencies. Runtime performance and crash stability have not been
remeasured; the installed executable remains the earlier build.

1. **Uninstall ownership:** replaced recursive enumeration/deletion with three
   explicit program filenames and known settings/log filenames. Resolve paths,
   reject relative/root targets and files resolving outside the target, preserve
   other files/subdirectories, and remove only empty directories. Self-removal
   is restricted to the installed frozen uninstaller, waits for the wizard to
   exit, and uses an encoded PowerShell command with literal paths. A source
   Python interpreter or an external setup executable is never deleted.
   Source-mode installs now require the real setup EXE instead of copying the
   application as an uninstaller. Shortcut paths escape apostrophes and the
   PowerShell helper checks exit status.
2. **Disk temperature:** corrected the first sensor offset from 18 to 26,
   accounting for the descriptor's reserved array, and validate complete entries
   against returned length and declared size/count. The synthetic 55 C case now
   returns 55 rather than 0. Layout references:
   [Microsoft descriptor](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddstor/ns-ntddstor-_storage_temperature_data_descriptor)
   and [sensor entry](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddstor/ns-ntddstor-_storage_temperature_info).
3. **Retained drawing:** `Painter.end()`/`clear()` hide items instead of deleting
   them. Retain separate primitives for a shared key, restore explicit visibility
   and stacking order, and reuse existing items across CPU/Memory/Disk rotation.
   Two simulated rotation cycles allocate no new items on the second cycle and
   delete none. Cache size follows the distinct keys/primitives seen, not frames.
4. **UI responsiveness:** the default `ProcessSampler` uses a persistent helper
   with synchronous probes and no Tk root. The UI polls responses, allows one
   request at a time, and sends the latest settings on the next request. A sample
   exceeding 20 seconds terminates the helper, clears stale readings, and retries
   after five minutes; process reaping never waits on the UI thread. Optional
   WMI runs in that helper. The legacy thread option remains. This intentionally
   adds a process (and potentially onefile bootloader overhead) rather than
   claiming a synchronous probe backoff can prevent blocking.
5. **I/O rates:** baseline timestamps belong to each successful drive read.
   A skipped interval no longer divides accumulated bytes by the last global
   tick: the synthetic 300 MB over 300 seconds case reports 1 MB/s, not 300.
   Remove cached I/O, space, temperature and identity for disconnected letters.
6. **Error evidence:** connect Tk callback reporting to diagnostics, log caught
   sampling/probe/IPC errors with tracebacks, rate-limit each context to one
   report per minute, and reschedule the UI tick after recoverable errors.

The prior attribution of `alloc: invalid block` solely to threading has been
removed. [CPython #66999](https://github.com/python/cpython/issues/66999) reports
a similar allocator message after a file dialog; it does not prove the cause
of this application's crash. Passive long-run observation is still required.

### Validation

`python -B -m unittest discover -s tests -v` passed **20 tests**, including one
real spawned worker with synthetic hardware, timeout/restart and IPC-failure
cases, canvas reuse/visibility/order, temperature parsing, skipped I/O probes,
exception logging, and safe uninstall target selection.

All deletion, registry, and device operations in regression tests are mocked.
No stress tests, hardware polling, UI windows, installation, config modifications
or dependency installation were performed. Build artifacts were not regenerated;
frozen worker startup, interactive layout and long-run memory/crash behavior are
not covered by these checks. The README's old benchmarks are explicitly historical.

## 2026-09-14 — Follow-up validation and standalone build support

- All 21 tests passed with `SYSMONITOR_TK_TEST=1`. The extra integration check
  renders real Tk canvases through mini/expanded views, display modes, languages,
  themes, opacity and resizing. Repeated layouts keep the same canvas item IDs
  and callback counts. User settings saves are mocked.
- A normal source launch used isolated config/logs under `build/ui-smoke` and
  logged `sampler=process` without an error. Native window automation could not
  target the frameless widget, so no manual-input/visual QA claim is made.
- Added `build.cmd --app-only`, which reuses the existing icon and builds only
  `dist/SysMonitor.exe`. The installer is explicitly excluded from this request.
- Log the first completed worker sample with core/drive counts and worker PID,
  allowing source and frozen startup to be checked without a ten-minute wait.
- Initialized local Git on `main`. The user supplied
  `https://github.com/epinephrinerx/SystemMonitor` as `origin`; the initial remote
  check found no existing refs to preserve or merge.

### Standalone artifact verification

- Source commit `7b8d91d` was pushed to `origin/main` before packaging.
- `build.cmd --app-only` succeeded with Python 3.14.7 and the existing
  PyInstaller 6.22.2. No packages were installed.
- Output: `dist/SysMonitor.exe`, 13,648,217 bytes, file version 2.0.0.
- SHA-256: `38CAE229A721642CCB6DA0EFEA19D737D843BBD4A683B3A809944A04A2A6B1CB`.
- The executable was launched with config/logs isolated under `build/exe-smoke`.
  At 20:29:05 local time it logged `frozen=True` and `sampler=process`; at
  20:29:06 it logged `sampler ready cores=16 disks=7 worker_pid=11888`.
  No exception was recorded during the brief startup check. Test processes were
  then stopped. This verifies frozen startup/IPC, not long-run crash stability.
- `dist/SysMonitor-Setup.exe` was not rebuilt. Its SHA-256 before and after was
  `3F11C304B2CB42C9677DDEE8694A4AC10D14B3D11B1D8BE6E6AF9AA6BBACF2F1`.
- The installed application and user configuration were not replaced. The
  earlier statements about unbuilt artifacts above describe the initial review;
  this subsection records the subsequent standalone build.
