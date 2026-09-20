# SysMonitor 3.0 — the WPF port

C# / .NET 8 / WPF rewrite of the widget, on the `wpf-port` branch. The goal is
an executable with no Tcl/Tk in the process at all, keeping the 1.0.5 feature
set. The Python build stays on `main` and is not touched.

## Status

Working: frameless translucent always-on-top window, drag, snap to edge,
resize grip, double-click expand/collapse, context menu, mini view with the
five-second rotation, expanded view with the settings sidebar, Thai/English,
dark/light, opacity, the full sensor layer (per-core CPU, RAM, per-drive space,
I/O rates, drive temperature, SSD/HDD and bus detection), diagnostics log.

Not done yet: installer and uninstaller, autostart registration, an icon on the
published exe, WMI CPU temperature (the opt-in ACPI thermal zone), and tests.

## Measured, on this machine

| | Python + Tk (2.0.1) | C# + WPF (3.0.0) |
|---|---|---|
| exe | 13.6 MB (onefile) | **0.29 MB** (framework-dependent) |
| RSS, mini idle | 25–31 MB | 63 MB |
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

Brushes are cached and frozen rather than reallocated per row per tick, and
`Palette.LoadColor` no longer parses a colour string on every call.

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

## Building

```
dotnet build
dotnet run -- --expanded          # opens straight into the full view
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

`--expanded` exists so the expanded layout can be checked without driving a
double-click into the user's desktop.

## Gotchas already paid for

- **`InvariantGlobalization` must stay off.** WPF data binding asks for the
  specific culture behind `en-US`; without ICU data the first `Show()` throws
  `Cannot find non-neutral culture related to 'en-us'`.
- **XAML comments cannot contain `--`.** Section-divider comments of dashes are
  an XML parse error, not a warning.
