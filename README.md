# System Monitor

A native Windows desktop widget that shows what the machine is doing — CPU,
RAM, disks, GPU and network — built with C# / .NET 8 / WPF and no third-party
packages at all. Per-user install, no admin rights, under a megabyte of
executable.

## The three views

**Widget** — a small always-available tile that rotates one reading at a time
(five seconds each, or step with the arrows). **Overall** — every device
listed with its graphs and a settings sidebar. **Full** — a large window with
a tab per device and a framed graph per reading; the tab buttons carry each
device's headline figure. Double-click cycles the views, F11 opens the full
one, Escape steps back.

## What it reads

- CPU: total and per-core load from the perf counters, package temperature
  from the ACPI thermal zone, model / cores / threads / virtualization /
  cache from WMI and cpuid
- RAM: usage, installed modules by slot, slot count
- Disks: space, I/O rates, temperature where a sensor reports one; physical
  grouping, filesystem, bus and media type; network and removable drives
- GPU: load and per-engine breakdown, adapter and driver, memory
- Network: per-adapter upload/download throughput, LAN and Wi-Fi

The sampler runs in the background; slow hardware calls never block the UI
thread. Config, themes (light default), Thai/English, opacity and window
geometry persist in `%APPDATA%\SystemMonitor\config.wpf.json`. Nothing is
sent anywhere; the one network call in the app is the updates check you ask
for, and it talks only to GitHub.

## Install

Run `SystemMonitor-Setup-<version>.exe` (add `/S` for a silent install).
Requires the .NET 8 Desktop Runtime. Installs to
`%LOCALAPPDATA%\Programs\SystemMonitor` with an uninstaller registered in
Add/Remove Programs.

Coming from an older copy? The installer offers to remove the retired
Python build ("SysMonitor") and the pre-4.0 C# build ("SysMonitor.NET")
through their own uninstallers, and your settings carry over from
`%APPDATA%\SysMonitor` automatically.

## Build

```
build-wpf.cmd
```

Runs the tests, publishes the app and the installer into `dist-wpf\`, and
names each artifact with the version read back out of the project. Ordinary
development:

```
dotnet build SysMonitor.Wpf
dotnet test  SysMonitor.Tests
```

## Repository layout

| Path | What it is |
|---|---|
| `SysMonitor.Wpf` | the application: UI, sensors, config, updater |
| `SysMonitor.Setup` | the per-user installer/uninstaller wizard |
| `SysMonitor.Tests` | the test suite (MSTest, ~200 tests) |
| `Docs/` | this workspace's workflow documents and upstream history |
| `CHANGELOG.md` | what each release changed |

## History

- **1.x — browser extension, then Electron.** The first System Monitor was
  written in HTML and JavaScript as a Firefox/Chrome extension. It was then
  wrapped in Electron so it could run on its own, outside the browser. Two
  prototypes came out of that line, `Gemini/1.0.4` and `Claude/1.0.5`; the
  second is the first plus drive details (type, interface, read/write
  speed). Their source is not in this repository.
- **2.x — Python/Tk.** A rewrite as a native Windows widget with only the
  Python standard library (`tkinter`, `ctypes`), carrying the union of both
  prototypes' features (commits `7b8d91d`, 2.0.0, and `41c246a`, 2.0.1).
- **3.x–4.x — C#/WPF.** Ported to C#/WPF on 2026-09-20 (3.0.0).
  `SysMonitor.Wpf/README.md` keeps the port's engineering notes. The Python
  source left the repository in 4.0, as the original requirement always said
  it would.
- **Other editions.** The Mac Edition (1.0.0) lives on branch `Mac`; the
  native Linux edition is rebuilt from 4.3.0 on branch `linux-native`.

`CHANGELOG.md` keeps the release history.
