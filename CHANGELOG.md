# Change log

## Unreleased — one copy at a time

**Only one System Monitor runs per Windows session.** A second launch (a
doubled start at logon, a double click) now waits a few seconds for the
first to finish -- Restart hands over this way -- and then exits instead of
opening a second window. The running copy is never killed.

**The "Start with Windows" box and the installer now agree.** The sidebar
checkbox still wrote the pre-4.0 `SysMonitor.NET` Run value while the
installer writes `SystemMonitor`, so ticking it added a second entry and
Windows started the app twice. Both use `SystemMonitor` now, and the stale
`SysMonitor.NET` value is removed whenever the box is set.

## v4.0.0 — 2026-09-28 — the software is called System Monitor

The product's name is **System Monitor**, and from this release every
user-visible place agrees with that. The installer lays down
`SystemMonitor.exe` into `%LOCALAPPDATA%\Programs\SystemMonitor`, the Run
value and the uninstall key become `SystemMonitor`, the shortcuts are named
"System Monitor", and so are the window titles and the setup wizard. The
distributed files are `SystemMonitor-4.0.0.exe` and
`SystemMonitor-Setup-4.0.0.exe`; the updater looks for the new installer
name from this release on.

**Old builds are found and offered removal.** Until now the setup only
looked for the retired Python build. It now also recognises the pre-4.0
C# build (`SysMonitor.NET` in Add/Remove Programs) and offers to uninstall
each of them, the same way: through their own uninstallers, with the
settings backed up first.

**Settings move with the name.** The config folder becomes
`%APPDATA%\SystemMonitor`. On first run the pre-4.0 `config.wpf.json` is
copied across, and an existing new config is never overwritten -- an
upgrade still opens with the window the size the user left it.

**The widget's contents follow its size.** The widget keeps its shape as
it is resized, and its text, values and bars scale with it: the default
widget draws exactly as before, and someone whose saved widget is not the
default size -- smaller or larger -- will see the text change accordingly
after upgrading. Drag it or use the font-size slider if that is not
wanted. Reset Size returns to the default 270×104.

**The Python build is out of the repository.** Its source, scripts, tests
and version stamp go; the requirement always said to keep only the C#
build. The installer's legacy-detection code stays, because installed
copies still need to be removable. 198 tests.

## v3.3.0 — 2026-09-22 — resizing stays resizing

**A resize is a resize.** Dragging the window below the size its tabs need
used to step back to the overall view; now it stops at the view's own
minimum, and only a button changes the displayed view.

**A restart command.** Under Settings and in the context menu. It saves the
window position first, so the new copy opens where the old one stood.

**The old build gets an exit.** Every install detects the retired Python
build and offers to remove it through its own uninstaller. (Manual removal
deletes all of `%APPDATA%\SysMonitor`, which both builds share -- the
installer backs up this build's settings around it, but manual removal does
not. From 4.0 the settings folder is this build's own, and the problem is
gone.)

## v3.2.1 — 2026-09-21 — the missing drives, and the way back from the tray

**Network drives are found.** Mapped drives (L:, N:, P:) were filtered out
of the enumeration; they now appear under their own "Network drives"
heading, with a new "Show network drives" setting. Shares are probed with
longer timeouts, because an offline host can block SMB.

**The tray icon is reachable.** The close-to-tray setting stayed "exit" on
machines with older settings files, so the icon logic never ran; and
Windows 11 hid the icon behind the overflow arrow. The app now promotes its
icon out of the overflow once and remembers; drag it back and it stays.

**An updates page.** Settings → Updates shows the current version and
checks GitHub; one button downloads and launches the installer. Downloads
are limited to GitHub hosts over HTTPS.

**The installer reports its real version.** `Installer.Version` was a
constant that went stale at 3.1.0 while the app moved on, so 3.2.0
registered itself as 3.1.0. It is read from the assembly now. Also: the
installer matches the app's light theme, and the settings list scrolls.

## v3.2.0 — 2026-09-21 — the three views get their names, the full view gets real hardware

The views are named **widget / overall / full** in code, settings keys and
conversation; the old key names carry over on upgrade.

The full view's left rail follows Task Manager's shape -- device name, type,
reading -- so the facts panel only says what the rail does not: CPU model,
cores/threads, virtualization, L2/L3; RAM modules by slot and slot count;
disks grouped by physical device with type, size, bus and status, drives
without hardware under "Virtual drives". **A GPU section arrives**: overall
load, per-engine, adapter, driver, graphics memory. Per-core graphs become
square tiles that reflow with the window.

Temperatures get honest: the per-core badges go (one sensor, so the values
were the package repeated), the package temperature shows once on the CPU
heading and rail, and no GPU temperature is claimed -- Windows needs an
undocumented driver call, which is why Task Manager says N/A too.

Fresh installs start not-always-on-top, English, full opacity, closing to
tray, every section shown, with "Start with Windows" on. Fixed: the full
window is draggable from anywhere again; disks were missing entirely
because `MSFT_Partition.DriveLetter` returns a signed Int16; and
virtualization read "disabled" under a hypervisor.

## v3.1.0 — 2026-09-21 — the full view becomes a tabbed one

Six requirements, all in `requirements.md`.

**It is a window, not an OS full-screen mode.** It fills the work area the
first time and is an ordinary resizable window after that, so the taskbar stays
reachable, the corners stay rounded and the size is the user's to keep. Dragged
below 640x480 it steps back to the expanded view rather than showing a tab
strip with nowhere to put a graph.

**A tab per device.** CPU, Memory, then one tab per *logical* disk, then the
adapters -- the order of the middle view. Each tab carries its headline figure
on the button, so the devices that are not open still report. Within a tab the
graphs run down the page, each in its own framed panel.

**A drive tab says what the drive actually is**: which physical disk it sits
on and which partition, how big that partition is against the whole disk, the
filesystem, and the disk's model, serial, firmware, bus, media type and
partition count. Read once and cached from the storage WMI namespace -- the
live figures stay on the direct syscalls that keep the widget cheap.

**A "Full Data" button** in the sidebar above the window settings. F11, a
double-click and the context menu are all invisible to someone who has not been
told about them.

**Light is the default theme**, and the background with it.

**The widget steps.** Arrows on either side of the mini view move to the next
reading instead of waiting out the five-second rotation, and using them
restarts the timer so the choice is not swept away a moment later.

Also: `dist-wpf` is no longer emptied before a build. Every version that has
been built stays on the machine, and the version in the file name keeps them
apart.

128 tests, up from 124. `FullViewGroupingTests` became `FullViewTabTests`,
because the thing it tested no longer exists.

## v3.0.4 — 2026-09-20 — versioned artifacts

The distributed files now carry their version: `SysMonitor-3.0.4.exe` and
`SysMonitor-Setup-3.0.4.exe`. Downloading two builds no longer leaves two files
with the same name and no way to tell them apart.

**The installed file names do not change.** What the installer lays down stays
`SysMonitor.exe` and `uninstall.exe`, because an uninstaller written by an
older version looks for exactly those names, and a rename would strand it with
nothing to remove. Verified by installing from the versioned setup, uninstalling,
and checking the directory came away clean.

The version is read out of the project at build time with
`msbuild -getProperty:Version`, so the name on the file and the version inside
it come from one place and cannot drift apart.

No code changes. 124 tests.

## v3.0.3 — 2026-09-20 — third review pass

Two findings, both in the pen cache added two rounds ago. The review is
converging: ten, then seven, now two, and nothing outside the code the previous
round touched.

- **Only one of the two colour caches was locked.** `Chart.Pens` was guarded;
  `Palette.BrushCache` was not, and it is reached from the chart's render path,
  from `FillFor` and from the view model. Locking half of a problem is not
  locking it. Both are guarded now.
- **Both caches could grow for the life of the process.** The palette is a
  couple of dozen colours, but the chart's colours come from public properties,
  so an animated or continually recoloured brush would have added an entry per
  frame and never given one back. Both are capped, and emptied and refilled if
  the cap is ever reached.

Everything else the review looked at came back clean: `Seal` no longer lets a
collection escape and its copying costs a small reference array every couple of
seconds; leaving full screen restores the position, applies the view size, then
clamps; rejecting a zero declared size follows the structure contract and
should affect no conforming driver; and the anchor list fails closed.

The non-atomic junction check was accepted as a defensible trade for a
per-user installer, with the limitation stated plainly rather than papered
over. It stays as it is.

124 tests, up from 120.

## v3.0.2 — 2026-09-20 — second review pass

The same reviewer went over the 3.0.1 fixes and found seven more things. Four
of them were faults the fixes themselves had introduced, which is the useful
half of asking twice.

### Introduced by the previous round

- **The pen cache only grew.** Non-solid brushes were cloned on the way in and
  the cache was keyed by the clone, so every repaint added an entry that could
  never be hit again. It is keyed by colour now, only solid brushes are cached
  at all, and the dictionary is locked: nothing stops a second dispatcher
  rendering a `Chart` of its own, and an unsynchronised dictionary does not
  merely give wrong answers then, it corrupts.
- **`Seal` wrapped rather than copied.** `AsReadOnly` is a live view of the
  original, so a producer still holding the input list could change what the UI
  was already reading. It copies now. A test had asserted the wrapping
  behaviour as though it were correct; that test now asserts the opposite.
- **Clamping at load broke full screen.** The new position check runs against
  the work area, and full screen is deliberately the whole monitor, so a
  taskbar on the top or left edge would have shoved it off the other side.
  Full screen is exempt.
- **Freezing a clone throws for brushes that cannot be frozen.** A
  `VisualBrush` reports `CanFreeze == false`, and the fix for gradients froze
  unconditionally.

### Remaining from the first round

- **A descriptor declaring size zero was still read.** It was waved through as
  "the device did not say", which is reading past a structure that states it
  holds nothing. The tests had used zero-filled headers throughout, so they
  never reached the check they were meant to cover.
- **The link walk could reject a legitimate folder.** It climbed to the drive
  root, so a profile redirected with a junction -- ordinary on a managed
  machine -- would have made the uninstaller refuse to remove anything. It now
  stops at the known folder the directory is rooted in.
- **The junction check is a check, not a guarantee**, and the README now says
  so instead of implying otherwise. The state is read and the delete happens
  afterwards; closing that race means opening every path component by handle
  rather than by name.

### Also

The "framed on all four sides" assertion counted red pixels, which one
horizontal edge of a 120-pixel-wide box satisfies on its own. Each side is
asked for by name.

120 tests, up from 116.

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
