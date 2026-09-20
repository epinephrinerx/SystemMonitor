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
