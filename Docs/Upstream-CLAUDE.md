# SysMonitor — working notes for Claude

A native Windows desktop widget: CPU, memory, disks, network, temperatures.

**The C# / WPF build is the product.** It lives in `SysMonitor.Wpf`, with its
installer in `SysMonitor.Setup` and its tests in `SysMonitor.Tests`. Read
`SysMonitor.Wpf/README.md` first — it carries the design decisions and the
measurements.

The Python / Tk build (`sysmonitor/`, `tools/`, `build.cmd`) is **retired**. It
still runs and is still installed, but it is not developed any further. See
"The retired Python build" at the end for the parts of it that are still worth
knowing.

## Rules for working on this machine

**Never run load-generation tests** — no CPU spinners, memory ballooning, or
disk churn. I did this once and the user's CPU overheated; they killed the task
and asked that diagnosis not "torture the machine". Diagnose passively: read
the log, instrument a single run, search upstream bug trackers. If a test would
raise system load, say what it will do and get agreement first.

**Do not take over the screen.** The widget's full-screen view covers
everything the user is doing. I opened it three times in one session to take
screenshots; the user clicked on it twice, once into a crash. Verify rendering
offscreen instead — `ChartRenderTests` draws into a `RenderTargetBitmap` and
reads the pixels back, which needs no window at all. If a real full-screen
check is genuinely needed, ask first.

**Commit messages carry no tool attribution.** No `Co-Authored-By`, no session
links, no "written by <tool>" notes — for any tool, not just this one. The user
is the author.

**No NuGet packages in the shipped apps.** Everything goes through the base
class library and `ctypes`-style P/Invoke in `Native/Win32.cs`. The test project
is the exception: MSTest is a test dependency and ships nothing.

## The three views, and what to call them

The user named these, and the names are the same in Thai, in English and in
the code, so a sentence about one of them points at a symbol without
translation:

| | in code | in config |
|---|---|---|
| widget | `View.Widget` | `widget_w` / `widget_h` |
| overall | `View.Overall` | `overall_w` / `overall_h` |
| full | `View.Full` | `full_w` / `full_h` |

"Overall" says what the middle view does -- every device group summarised on
one page -- where the old name, "expanded", only said it was bigger than
something else. Note that `CpuMode`/`DiskMode` also have a `"total"` setting,
meaning "not split per core or per drive"; that is a different axis from the
view, and neither name should be used for the other.

## What the app does that is easy to get wrong

**Temperature colours and usage colours are separate.** `WarmAt`/`HotAt` (65/80)
belong to the thermometer badge; `LoadWarmAt`/`LoadHotAt` (70/90) belong to the
usage bars. The user asked for the traffic light on the meters and explicitly
said not to touch anything about temperature colour. A test asserts both sets.

**The CPU temperature source matters.** `MSAcpi_ThermalZoneTemperature` in
`root\WMI` returns *access denied* to a normal user — that is why the Python
build's setting never produced a reading. Use
`Win32_PerfFormattedData_Counters_ThermalZoneInformation` in `root\cimv2`,
which needs no elevation. It is a thermal zone, not the CPU die: the die sensor
is behind an MSR and needs a kernel driver.

**Windows reports no per-module memory usage.** The controller interleaves
across channels, so the quantity does not exist. `MemoryModules` shows what is
installed — slot, size, speed, type — and the section says so rather than
drawing a bar nobody measured.

**Windows 11 hides every tray icon it has not seen before.** A new icon goes
into the overflow flyout and stays there until the user drags it out, which for
an icon whose only job is being the way back to a window that just vanished is
the one place it must not be. The shell records the choice under
`HKCU\Control Panel\NotifyIconSettings`, keyed by a hash it computes, so
`TrayPromotion` finds our entry by the `ExecutablePath` value it carries and
sets `IsPromoted`. The entry exists only after the icon has been registered
once, so this runs after `NotifyIcon.Visible`, and `AppConfig.TrayPromoted`
stops it happening twice -- a user who drags the icon back in means it.

**Network drives were filtered out at the source.** `Win32.LogicalDrives` takes
an `includeNetwork` flag defaulting to false and the sampler was not passing
one, so `DRIVE_REMOTE` letters never reached the view at all. They are probed on
a longer leash than local volumes: a share whose host is asleep blocks until SMB
gives up.

**`PDH_FMT_COUNTERVALUE_ITEM` is 24 bytes, not 16.** The item is a name
pointer followed by a whole `PDH_FMT_COUNTERVALUE`, which is a `CStatus` word
*and* the union. Leaving the status field out of the interop struct makes the
stride wrong, so every item after the first reads its name pointer out of the
middle of a double -- an access violation, which .NET cannot catch, so it takes
the process down rather than throwing. `Pdh.CounterItem` carries the field.

**The GPU counter is cheap if the query stays open.** `\GPU Engine(*)` has
about 1200 instances on this machine, and the reputation it has for being slow
comes from reopening the query, which re-expands that instance list every time.
`GpuSensor` holds one query open for the life of the process and a sample
measures 0.5 ms, so it sits on the normal metrics cadence. `GpuSensorTests`
asserts the budget. Note also that a rate counter has no value until the
*second* collection.

**The chip has one thermal sensor, so only one thing may wear a badge.**
A per-core temperature does not exist without a kernel driver: reading
`IA32_THERM_STATUS` (0x19C) per logical processor is a ring 0 instruction, and
the only ways to it are a blocklisted driver like WinRing0 or an
attestation-signed one of our own. What the core rows used to show was
`40 + load * 0.4` -- the bar beside them converted into degrees -- or, with the
thermal zone on, the one package reading printed sixteen times. The figure now
sits on the CPU section heading (`Section.Note`) and on the full view's rail,
and `MeterRow.ShowTemp` is what keeps a core row from claiming a reading.
Note that this differs from a drive with no sensor, which still earns an
"n/a": there the question makes sense and the answer is that nothing measured.

**`VirtualizationFirmwareEnabled` lies on a machine running Hyper-V.** Once a
hypervisor owns the virtualization extensions, Windows runs in the root
partition and both that WMI property and `IsProcessorFeaturePresent(21)` come
back false on a machine where virtualization plainly works. Task Manager reads
`Win32_ComputerSystem.HypervisorPresent` as well, and so does `CpuDetails`.

**WMI `char16` arrives as a signed `Int16`.** `MSFT_Partition.DriveLetter` is
67 for C, not `'C'` and not a `ushort`. A conversion that missed `short`
dropped every partition and left the disk panel empty.

**Freezing a `Pen` freezes its `Brush`.** The view model recolours its brushes
on every theme switch, so a chart that froze one turned the next switch into
"Cannot set a property ... because it is in a read-only state". `Chart.PenFor`
takes the colour, not the instance. Two tests guard this.

**`InvariantGlobalization` must stay off.** WPF data binding asks for the
specific culture behind `en-US`; without ICU data the first `Show()` throws.

**Log timestamps use the invariant culture.** Under a Thai locale the default
formatter writes Buddhist-era years, which made the first log impossible to
line up with anything else on the machine.

**XAML comments cannot contain `--`.** Divider comments made of dashes are an
XML parse error, not a warning.

**Uninstall never walks a directory.** The files the installer creates are
written down; only those are deleted, each checked against `IsSafeTarget`
first. The directory goes only if it ends up empty.

**The C# build installs beside the Python one, not over it.** Install directory
`SysMonitor.NET`, Run value `SysMonitor.NET`, uninstall key `SysMonitor.NET`,
shortcut "SysMonitor (.NET)". Both are called SysMonitor and both install
per-user, so sharing any of those names would overwrite a working application.

**Beside it also means both start every morning.** The user reported the screen
had "reverted to version 2.0"; it had not, it *was* version 2.0 -- the Python
build, still installed, still in the Run key, its window landing on top of the
newer one. Nothing in the C# code could have explained it, and a process list
settled it in one command. `Legacy.Find()` now looks for the old build on every
install and offers to remove it.

**The Python uninstaller deletes the whole of `%APPDATA%\SysMonitor`.** That
directory holds both builds' settings, so removing the old build takes
`config.wpf.json` and the log with it -- which it did, silently, on the user's
own machine. `Legacy.Remove` copies the directory out before calling that
uninstaller and puts back anything it destroyed. Back the file up before
letting that uninstaller anywhere near it.

**Resizing must never change which view is showing.** The full view used to
step back to the overall one when dragged below 640x480, so reaching for a
corner could replace the contents of the window under your hand. Views change
by button only; a drag runs out of room at `Limits(view).Min` instead.
`RestartAndViewTests` asserts that the old size threshold is gone.

**Measure memory before believing it.** A ten-minute run reporting 108 MB
looked like a leak; the managed heap was 4–9 MB and sawtoothing normally, so
those were pages the GC had freed and Windows had not reclaimed. The heartbeat
logs both numbers, and `TrimWorkingSet` on each heartbeat hands them back.

## Where things are

| | |
|---|---|
| Build everything | `build-wpf.cmd` → `dist-wpf\` (tests, app, installer) |
| Tests | `dotnet test SysMonitor.Tests` |
| Try a view | `dotnet run --project SysMonitor.Wpf -- --expanded` / `--full` |
| Memory question | `-- --heartbeat 60` shortens the diagnostics interval |
| Log | `%APPDATA%\SysMonitor\sysmonitor-wpf.log` |
| Config | `%APPDATA%\SysMonitor\config.wpf.json` — the user's real settings; back up before tests that write it |
| Install dir | `%LOCALAPPDATA%\Programs\SysMonitor.NET` |
| Backlog | `requirements.md`, written by the user |

`dist-wpf\SysMonitor-Setup-<version>.exe /S` installs silently. The
distributed files carry the version; the installed one stays `SysMonitor.exe`. Run it from PowerShell,
not Git Bash: Git Bash rewrites a bare `/S` into a Windows path and the wizard
opens instead.

## Communication

The user writes in Thai; reply in Thai. Code, comments, and docs stay in
English.

## The retired Python build

Kept installed and working, not developed. Two things from it are still worth
knowing because the C# port inherited the knowledge:

- **`alloc: invalid block`** was a Tcl allocator panic with no Python
  traceback. Its cause was never established. Removing Tcl/Tk from the process
  entirely is what the C# port is for.
- **The drive temperature descriptor's first reading is at byte 26**, not 18 —
  byte 18 is reserved and reads as zero. `Win32.cs` carries the corrected
  parsing and `StorageDescriptorTests` asserts the exact failure shape.
