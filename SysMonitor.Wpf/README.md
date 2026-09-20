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

`IsSafeTarget` also refuses a path that reaches its target through a junction
or symbolic link, because a lexically perfect path inside a directory we appear
to own resolves somewhere else entirely when that directory is a link. The walk
stops at the known folder the directory is rooted in: corporate profiles are
routinely redirected with junctions, and refusing to uninstall on such a
machine would be a worse failure than the one this guards against.

**That is a check, not a guarantee.** It reads the state of the path and the
delete happens afterwards, so a link put in place in between would still be
followed. Closing the race properly means opening every path component by
handle and never by name, which is a great deal of Win32 for a per-user
installer -- and an attacker who can win it can already run code as the user
whose files are at stake. Said plainly here rather than left implied.

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

## Controls

- **Sliders read out their value.** Opacity and font size both show a
  percentage beside the caption; a bare track leaves you guessing whether you
  are at 60% or 65%.
- **Font size** rewrites the type ramp, which lives in application resources
  and is bound through `DynamicResource`, so everything that draws text
  follows. Bars keep their height; rows grow taller as the text in them does.
- **A full-screen button** sits beside collapse and close, because
  double-click, F11 and the context menu are all invisible to someone who has
  not been told about them.
- **The scrollbar** has no arrow buttons and no track chrome: a thin rounded
  thumb that thickens under the pointer.

### Resizing

**Every edge and every corner**, not one grip. `WindowGeometry.HitTest` reads
the pointer against the window rectangle and returns which sides it is on;
corners are tested first so the overlap resizes both ways rather than whichever
side happened to be checked first. The cursor follows -- SizeWE, SizeNS,
SizeNWSE, SizeNESW.

**Every frame is computed from the rectangle the drag started on**, never from
the last frame. Dragging a left or top edge moves the window as well as sizing
it, and an incremental version drifts -- worse once the size hits its limit and
the pointer keeps going, at which point the far edge slides away. Pinning to
the origin means the opposite side stays exactly where it was, clamped or not,
and two tests say so. Position and size are then saved together on release,
because half of one is a window that jumps on next start.

Three things had to be got out of the way for it to work at all:

- `ScrollViewer` marks `MouseLeftButtonDown` handled to take focus, so the
  window's bubbling handler never saw a click over the expanded content. The
  resize claims the click in `OnPreviewMouseLeftButtonDown` instead, before any
  child can take it.
- A transparent region in a WPF window is not hit-testable, so the shadow
  margin -- exactly where the pointer goes when someone aims at the edge of
  what they can see -- passed clicks straight through. The outer grid now has
  an explicit `Transparent` background, which does take input. The cost is that
  the twelve-pixel margin no longer clicks through to the desktop.
- The title-bar buttons and the widget's close button sit in corners, which are
  now resize zones, so claiming the click there would have made them dead.
  `OverControl` walks up from whatever was hit and leaves the click alone if it
  belongs to a button, slider, scrollbar or thumb.

And the expanded view's size cap went from 2000x1400 -- smaller than the
monitor this runs on -- to 4000x3000.

`WindowGeometry` holds the arithmetic apart from the event plumbing so it can
be checked without a window. `WindowGeometryTests` pins every edge and corner
of the hit test, that each edge pins its opposite, that clamping does not move
the far side, both size limits, and that what gets saved is the panel size
rather than the window's.

## The close button

The widget carries its own, top right, at 35% opacity until the pointer is on
the window. Faint rather than hidden: a control nobody can see is a control
nobody finds, and the mini view is too small for a hover-only button to be
anything but a guess.

It does whichever of two things the user picked, set in the sidebar:

- **Exits** (default) -- the widget quits.
- **Hides to tray** -- the window goes away and a notification-area icon takes
  its place. Left-click or double-click brings it back; the icon's menu offers
  Show window and Exit.

The icon exists only while the window is hidden. A widget that sits on the
desktop all day does not also need a permanent icon in the tray. Its tooltip
carries CPU, memory and temperature, because while the window is away that is
the only thing still reporting -- trimmed to 63 characters, since Windows does
not truncate an over-long tooltip, it drops it and leaves the icon with none.

Hiding stops the UI timer but not the sampler: the sampler is the cheap half,
and its history keeps filling so the graphs are not blank on return.

`NotifyIcon` comes from Windows Forms, which is part of the desktop framework
rather than a package, and is a hundred and fifty lines of message-window
plumbing we would otherwise own. Turning it on adds `System.Drawing` and
`System.Windows.Forms` to the implicit usings, where `Brush`, `Color`, `Point`,
`Size`, `CheckBox` and half a dozen event args collide with their WPF
namesakes in every file -- so the csproj removes both from the implicit set and
`TrayIcon.cs` asks for them itself.

## The three views

**Mini** is unchanged: one metric at a time, rotating every five seconds.

**Expanded** now lays each group out as two columns of cards rather than one
long list. Sixteen cores in a single column was most of a screen; side by side
it is four rows.

**Full screen** takes the whole monitor, taskbar included, and gives every
device its own graph **under a heading per device kind** -- CPU, Memory, Disk
Storage, Network -- rather than one flat wrap of cards. The CPU package gets a
full-size graph and the logical processors a grid of quarter-size ones, the way
Task Manager lays them out. Each drive gets two graphs: how full it is, and
what it is doing. History is pushed on every snapshot whatever view is on
screen, so opening the full view shows the last few minutes rather than an
empty box. Seventy-two points is three minutes at the balanced cadence.

The graphs are one `FrameworkElement` drawing in `OnRender`, not a chart built
from elements -- seventy-two points as seventy-two visuals would cost more than
everything else in the window together. One `StreamGeometry` per repaint, and a
scratch buffer reused between repaints, so drawing allocates nothing per frame.

### Drawn like Task Manager

The first version looked weak beside it, and comparing the two said why:

- A **framed plot box**, not a floating line. The frame is drawn last so the
  fill cannot paint over it.
- A **square grid**, six by four, vertical lines as well as horizontal. Grid
  and frame are drawn on half-pixel offsets so a one-pixel line lands on a
  pixel instead of straddling two and rendering as two grey ones.
- A **solid tint** under the line instead of a faint wash. The tint is the line
  colour mixed toward the panel rather than toward transparency, so grid lines
  stay hidden under the fill exactly as they do in Task Manager. At a glance
  the filled area is what tells you how busy something is.
- An **angular stroke**: `isSmoothJoin: false`, because a spike should look
  like a spike.
- **Axis labels above the plot**: what the axis counts on the left, where its
  top is on the right. A percentage says `100%`; a throughput graph has no
  ceiling, so it scales to its own peak and says what that peak is.

`ChartAppearanceTests` renders the graph at card size in both themes and saves
`%TEMP%\sysmonitor-chart-dark.png` and `-light.png`, so the look can be judged
against Task Manager without opening a window over anybody's desktop.

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

What is fitted, summarised onto the memory row: `22.1 / 31.6 GB in use  ·
2 x 16 GB DDR4 2667 MHz`. From `Win32_PhysicalMemory`, queried once and cached
because a DIMM does not appear while the widget runs.

It began as a section of its own, one row per module. That was a mistake twice
over. **There is no per-module usage, and no API exposes one** -- the memory
controller interleaves across channels, so "how much of DIMM 2 is in use" is
not a quantity the hardware tracks -- so every row was a fact with no bar. And
the section sat last, below sixteen cores, four drives and two adapters, where
nobody scrolled to find it. The facts are worth having; a section was not the
place for them.

Mixed sizes are listed rather than averaged (`16 GB + 8 GB`), and a type or
speed the modules disagree on is left out rather than guessed at.
