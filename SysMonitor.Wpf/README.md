# SysMonitor 3.0 — the WPF port

C# / .NET 8 / WPF rewrite of the widget, on the `C_Sharp` branch. The goal is
an executable with no Tcl/Tk in the process at all, keeping the 1.0.5 feature
set. The Python build stays on `main` and is not touched.

## Status

Working: frameless translucent always-on-top window, drag, snap to edge,
resize grip, double-click expand/collapse, context menu, mini view with the
five-second rotation, expanded view with the settings sidebar, Thai/English,
dark/light, opacity, the full sensor layer (per-core CPU, RAM, per-drive space,
I/O rates, drive temperature, SSD/HDD and bus detection), diagnostics log.

Also working: real CPU temperature, LAN and Wi-Fi throughput, installed memory
modules, a full-screen view with a graph per device, two-column card layout,
installer and uninstaller, autostart from the sidebar, the exe icon, and
70 tests.

Three views, cycled by double-click, or F11 for full screen and Escape to step
back: **mini** rotates one metric every five seconds, **expanded** lists
everything with the settings sidebar, **full** graphs every device.

## Measured, on this machine

| | Python + Tk (2.0.1) | C# + WPF (3.0.0) |
|---|---|---|
| exe | 13.6 MB (onefile) | **0.29 MB** (framework-dependent) |
| RSS, mini idle | 25–31 MB | 41–50 MB |
| CPU, idle | 0.57% | 0.78% |

Release build, framework-dependent, measured over 40 s of idle after a 20 s
warm-up. WPF costs about twice the memory of Tk; that is the price of the
runtime and it is not going away. The executable is small only because .NET 8
desktop is already installed — a self-contained publish would be 70–150 MB.

Two things earned most of the CPU back and are worth keeping:

- **Repaint only when the snapshot changes.** The sampler publishes every 2.5 s
  but the UI timer fires every 400 ms. A per-pixel-alpha window is composited
  in software, so repainting an unchanged frame is not free: gating on the
  snapshot reference took idle CPU from 1.77% to 1.09%.
- **No drop shadow.** A blurred `DropShadowEffect` on a layered window is a
  software blur on every repaint: 1.09% → 0.78% CPU and 74 MB → 63 MB. The Tk
  build never had one. Put it back in `MainWindow.xaml` if the look matters
  more than the cost.

Brushes are cached and frozen rather than reallocated per row per tick,
`Palette.LoadColor` no longer parses a colour string on every call, and the
cores are built once per sample instead of being built and then rebuilt to
carry a temperature.

Memory needed measuring before it could be believed. A ten-minute run
reported 108 MB and looked like a leak; logging the managed heap next to the
working set showed the heap sitting at 4-9 MB and sawtoothing normally, so
the 108 MB was pages the GC had already freed and Windows had not reclaimed.
`SetProcessWorkingSetSize` on each heartbeat hands them back, and idle RSS now
settles at 41-50 MB and trends down rather than up. The heartbeat logs both
numbers for exactly this reason: a growing heap is a leak, a growing working
set on a flat heap is not.

## CPU temperature

There is no driver-free way to read the CPU die sensor on Windows -- that
lives behind an MSR and needs a kernel driver. The ACPI thermal zone is the
closest an unprivileged process can get, and on most machines the zone the
firmware calls TZ00 tracks the package closely enough to be worth showing.

**The source matters more than the parsing.** The obvious class,
`MSAcpi_ThermalZoneTemperature` in `root\WMI`, returns *access denied* to a
normal user -- which is why the Python build's opt-in setting never once
produced a reading on this machine. The performance-counter class
`Win32_PerfFormattedData_Counters_ThermalZoneInformation` in `root\cimv2`
exposes the same zones with no elevation at all:

```
root\WMI   MSAcpi_ThermalZoneTemperature   -> Access denied
root\cimv2 ...ThermalZoneInformation       -> \_TZ.TZ00 = 325 K = 51.9 C
```

So this build reads the second one, it is **on by default**, and all sixteen
cores show a measured figure with no `~`. `HighPrecisionTemperature` is used
where the firmware provides it, since plain `Temperature` quantises to whole
Kelvin. Where several zones exist, one named for the CPU wins; failing that,
the hottest, because that is the one worth warning about.

The query goes through WMI's scripting COM object by late binding: not
`System.Management`, which is a package, and not a PowerShell subprocess,
which is what the Python build spawned and waited up to eight seconds for.
It runs on the slow temperature cadence with a two-second budget rather than
the 250 ms the device probes use, because a WMI round-trip is inherently
slower than an IOCTL. A machine with no zone at all gets three tries and is
then left alone: a class that is not implemented will not become implemented.

## What carried over unchanged

`Native/Win32.cs` is the ctypes layer ported call for call, including the
corrections the September review found: the drive temperature descriptor's
first reading is at byte **26**, not 18, and I/O rate baselines are per drive
so a skipped probe cannot report a huge instantaneous rate.

`Palette.cs`, `I18n.cs` and `AppConfig.cs` are direct ports of `theme.py`,
`i18n.py` and `config.py`.

## What is deliberately different

- **Transparency.** Tk could only fade a whole window, so the panel colours
  were pre-flattened and a colour key supplied the see-through — which is what
  made the resize grip click-through. WPF composites real per-pixel alpha, so
  the panel is translucent, the text stays opaque, and every pixel takes input.
- **Sampling.** The Python build had to push probes into a separate *process*
  because a second thread was the leading suspect for the Tcl allocator panic.
  .NET has no such hazard: one background task, no extra process.
- **Drawing.** No `Painter`. WPF is retained-mode, so there is no canvas-item
  leak to work around; rows are still reused in place because it is cheaper.
- **Config file.** `%APPDATA%\SysMonitor\config.wpf.json`, separate from the
  Python build's `config.json`, so both can run without clobbering each other.
- **Log file.** `%APPDATA%\SysMonitor\sysmonitor-wpf.log`. Timestamps are
  written with the invariant culture: under a Thai locale the default formatter
  writes Buddhist-era years, which made the first log unreadable against
  anything else on the machine.

## Installing

`SysMonitor.Setup` is the wizard. It is the same executable twice: run as
`SysMonitor-Setup.exe` it installs, and the copy it leaves in the install
directory as `uninstall.exe` removes. `/S` runs either silently.

Everything it owns is named apart from the Python build's -- install directory
`SysMonitor.NET`, Run value `SysMonitor.NET`, uninstall key `SysMonitor.NET`,
shortcut "SysMonitor (.NET)". Both builds are called SysMonitor and both
install per-user, so sharing any of those names would have meant this
installer overwriting a working application. A test asserts the separation.

The two do share `%APPDATA%\SysMonitor`, but only file by file:
`config.wpf.json` and `sysmonitor-wpf.log` are this build's, and the
uninstaller's owned-file list names them individually.

**Uninstall never walks a directory.** The files this installer creates are
written down in `ProgramFiles` and `SettingsFiles`; only those are deleted,
each one checked against `IsSafeTarget` first, which rejects relative paths,
drive roots, subdirectories and anything resolving outside the two directories
we own. The directory goes only if it ends up empty, so a file the user put
there keeps both the file and the folder.

An executable cannot delete itself, so `uninstall.exe` copies itself to the
temp directory, hands over via `--finish <pid>`, and the copy waits for the
original to exit before finishing the job and marking itself for removal at the
next reboot. The handover uses `ProcessStartInfo.ArgumentList` -- no `cmd.exe`,
so there is no command line for a path to be reinterpreted in.

The app is framework-dependent, so the wizard checks for the .NET 8 Desktop
Runtime and says plainly if it is missing rather than installing something that
will not start.

## Building

```
build-wpf.cmd                     # tests, publish, installer, into dist-wpf\
dotnet test SysMonitor.Tests
dotnet run --project SysMonitor.Wpf -- --expanded
```

`--expanded` exists so the expanded layout can be checked without driving a
double-click into the user's desktop.

Run `dist-wpf\SysMonitor-Setup.exe /S` from PowerShell, not Git Bash: Git Bash
rewrites a bare `/S` into a Windows path and the wizard opens instead.

## Tests

37, no hardware and no installing. The ones that matter:

- **Descriptor layouts.** The temperature test asserts that byte 18 reads zero
  while the descriptor plainly carries 55 C -- the exact shape of the bug that
  shipped. Also short buffers, a sensor count the buffer cannot hold, and one
  the device's own declared size cannot hold.
- **I/O rates.** 300 MB accumulated over 300 s is 1 MB/s, not 300. Counter
  resets are not negative rates.
- **Uninstall targets.** Drive roots, relative paths, `..` escapes,
  subdirectories and unrelated files are all rejected; the install directory
  itself is never a delete target; `config.json` and `sysmonitor.log` are not
  ours to remove.
- **Coexistence.** The install directory, Run value, uninstall key and
  shortcut names all differ from the Python build's.
- **Thermal zones.** Kelvin and tenths-of-Kelvin conversion, readings no room
  ever sees, and zone selection: a CPU-named zone beats a hotter one, TZ00 is
  treated as the CPU, and with no recognisable name the hottest wins.
- **The graph, drawn offscreen.** `ChartRenderTests` renders a `Chart` into a
  `RenderTargetBitmap` and counts accent-coloured pixels: a series draws, a
  busy one fills more than a quiet one, one reading is not a line, an empty
  chart still draws its grid, and an unbounded series scales to its own peak.
  Checking this needed no window over anyone's desktop.
- **The history ring.** Oldest-first ordering, wrapping many times over, a
  buffer smaller than the ring, and a max that covers only what is still held.
- **Traffic light.** Every meter keeps its own colour to 70%, then amber, then
  red at 90% -- and the temperature thresholds are asserted to be untouched.
- **Pen freezing.** A pen built from a brush must not freeze the caller's
  brush, or the next theme switch throws.

`StorageDescriptors` and `IoRate` are internal, with `InternalsVisibleTo` for
the test assembly: nothing outside the sensor layer should call them, but they
are the parts most worth testing.

## Gotchas already paid for

- **`InvariantGlobalization` must stay off.** WPF data binding asks for the
  specific culture behind `en-US`; without ICU data the first `Show()` throws
  `Cannot find non-neutral culture related to 'en-us'`.
- **XAML comments cannot contain `--`.** Section-divider comments of dashes are
  an XML parse error, not a warning.

## The three views

**Mini** is unchanged: one metric at a time, rotating every five seconds.

**Expanded** now lays each group out as two columns of cards rather than one
long list. Sixteen cores in a single column was most of a screen; side by side
it is four rows.

**Full screen** takes the whole monitor, taskbar included, and gives every
device its own graph: CPU total and each core, memory, each drive's usage and
its throughput, and each adapter. History is pushed on every snapshot whatever
view is on screen, so opening the full view shows the last few minutes rather
than an empty box. Seventy-two points is three minutes at the balanced cadence.

The graphs are one `FrameworkElement` drawing in `OnRender`, not a chart built
from elements -- seventy-two points as seventy-two visuals would cost more than
everything else in the window together. One `StreamGeometry` per repaint, and a
scratch buffer reused between repaints, so drawing allocates nothing per frame.

## Network

`NetworkInterface` from the base class library, not `GetIfTable2`: the
counters, the media type and the link state all come from there, so this needs
neither a P/Invoke nor a package. Rates go through the same `IoRate` helper the
drives use, so an adapter whose probe was skipped is divided by its own elapsed
time rather than a shared tick.

The bar is combined throughput against the link rate -- a saturated uplink
matters as much as a saturated downlink. An adapter that reports no link speed
shows no percentage rather than dividing by zero.

## Memory modules

Slot, size, speed, type and maker, from `Win32_PhysicalMemory`, queried once
and cached because a DIMM does not appear while the widget runs.

**There is no per-module usage, and no API exposes one.** The memory controller
interleaves across channels, so "how much of DIMM 2 is in use" is not a
quantity the hardware tracks. The section shows what is installed, says so in
its heading, and draws no bar rather than inventing a number.
