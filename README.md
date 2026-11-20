> Branch `linux-native`: the native Linux edition (R-027). The Windows 4.3.0 app is on `main`.

# System Monitor

A native Linux desktop widget that shows what the machine is doing — CPU, RAM,
disks, GPU and network — built with C# / .NET 10 and Avalonia. It installs into
your own home directory, needs no root and no package manager, and asks no
administrator rights for anything it shows.

## The three views

**Widget** — a small always-available tile that rotates one reading at a time
(five seconds each, or step with the arrows). **Overall** — every device listed
with its graphs and a settings sidebar. **Full** — a large window with a tab per
device and a framed graph per reading; the tab buttons carry each device's
headline figure. Double-click cycles the views, F11 opens the full one, Escape
steps back.

Themes (light by default), Thai/English, opacity, font scale and window geometry
persist between runs. Nothing is sent anywhere; the only network call is the
update check, and it talks only to GitHub.

## What it reads

Everything comes from files the kernel and the drivers already publish for the
logged-in user, so nothing runs as root:

- CPU: total and per-core load from `/proc/stat`; model, cores and threads from
  `/proc/cpuinfo`; cache sizes and base clock from
  `/sys/devices/system/cpu/cpu*/cache` and `cpufreq`
- CPU temperature: `/sys/class/hwmon` (`coretemp`, `k10temp`, `zenpower`), with
  `/sys/class/thermal/thermal_zone*` as the fallback and a clearly marked
  estimate when no sensor reports one
- RAM: usage from `/proc/meminfo`
- Disks: space and mounts from `/proc/self/mountinfo`, real per-disk I/O from
  `/proc/diskstats` (partitions are never charged their parent's counters),
  model, bus and media type from `/sys/block`, and temperature from the disk's
  own hwmon node (`nvme`, `drivetemp`)
- GPU: adapters, driver, VRAM and the card's hwmon temperature from
  `/sys/class/drm`; per-engine load from the kernel's DRM counters in
  `/proc/<pid>/fdinfo`; NVIDIA also through `nvidia-smi`, which is asked only
  while the card is awake
- Network: per-adapter throughput from `/proc/net/dev`; Wi-Fi link quality,
  SSID and bit rate from `iw dev <if> link`
- Processes: `/proc/<pid>/stat`, `status`, `cmdline` and `fd`
- Services: `systemctl --user` units

Processes can be killed after a confirmation that re-reads the process first,
and user units can be started, stopped, restarted, enabled or disabled. System
units are read-only, because the app never asks for administrator rights.

## Known gaps on Linux

- Memory modules and slots need `dmidecode` as root, so they are not listed.
- There is no Intel GPU temperature: the driver does not expose one to
  unprivileged readers.
- Temperatures behind SMART (USB enclosures, some external NVMe) need root and
  are shown as `n/a`.
- The Realtek `r8152` USB Ethernet adapter reports no link speed, so that row
  stays empty.
- The Windows extension system was not ported; there are no extensions here.
- Closing to the tray needs a StatusNotifier host (KDE, and most GNOME setups
  with the AppIndicator extension). Without one the window minimises instead,
  so it cannot be lost.

## Requirements

- x64 or arm64, with glibc (any current distribution; the release was built on
  Ubuntu 26.04, glibc 2.43).
- A desktop session. X11 and Wayland both work through Avalonia.
- The self-contained archive needs no .NET installed. The framework-dependent
  archive needs the .NET 10 runtime.

## Build

```
./build-linux.sh                        # x64, self-contained
./build-linux.sh --arch arm64
./build-linux.sh --framework-dependent
```

The script reads `<Version>` from `SysMonitor.Linux.App/SysMonitor.Linux.App.csproj`,
publishes it for `linux-<arch>`, stages the scripts, the icon and `VERSION`
around the program, and writes `dist/SystemMonitor-<version>-linux-<arch>.tar.gz`
plus a `.sha256` file that `sha256sum -c` accepts. The archive is flat
(`install.sh` at the root, as the updater requires) and holds no links of any
kind.

For ordinary development:

```
export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH
dotnet build SysMonitor.Linux.App
dotnet test  SysMonitor.Linux.Tests
```

## Install

Unpack the archive into a directory you create and run its installer as
yourself, never with `sudo`. The archive is flat, so the scripts sit at its
root:

```
mkdir SystemMonitor && tar -xzf SystemMonitor-<ver>-linux-x64.tar.gz -C SystemMonitor && cd SystemMonitor && ./install.sh
```

It writes:

| What | Where |
|---|---|
| program | `~/.local/share/SystemMonitor/app/` (`$XDG_DATA_HOME`) |
| uninstaller | `~/.local/share/SystemMonitor/uninstall.sh` |
| command | `~/.local/bin/sysmonitor`, a symlink to the program |
| menu entry | `~/.local/share/applications/systemmonitor.desktop` |
| icon | `~/.local/share/icons/hicolor/256x256/apps/systemmonitor.png` |

The new tree is copied beside the old one, the old one is moved aside, and the
new one takes its place. The swap is two renames; if it is interrupted, running
`install.sh` again (or the next update) restores the previous version. Start it
from your application menu or with `sysmonitor`. The installer does not start it
unless `--update` is given, and never touches your settings. It also leaves a
copy of the uninstaller beside the program, outside `app/`, so the program can
be removed after the unpacked folder is gone.

Everything the two scripts write or remove is physically under your home
directory. An `XDG_DATA_HOME` or `XDG_CONFIG_HOME` that points outside it is
refused, as is one with a `..` or `.` component, a newline, or a symlink that
leads outside it. Each rename and removal re-checks the path first, so an
install can always be undone by the same rules. `uninstall.sh` also refuses to
remove a program directory that has become a symlink.

## Update

The setting "Check for updates at start" is on by default, and the Updates
section also has a "Check for updates" button. When a newer release exists the
button becomes "Download and install"; the program downloads the archive,
verifies its SHA-256, unpacks it, and runs its
`install.sh --update --wait-pid <pid>`. That waits up to 30 seconds for the
running copy to exit, restores the previous version if an earlier update was
interrupted, installs into the same place, and starts the new copy. The menu
entry, the command and the autostart entry keep pointing at the same path, so
nothing else needs to change.

The check itself is one HTTPS `GET` to the GitHub Releases API; hosts outside
the allow-list are refused, as are redirects that leave it.

## Uninstall

The installer leaves a copy of the uninstaller beside the program, so the
unpacked archive is not needed:

```
~/.local/share/SystemMonitor/uninstall.sh            # keep settings
~/.local/share/SystemMonitor/uninstall.sh --purge    # remove settings and the log too
```

The copy in the unpacked archive works the same way (`./uninstall.sh` from the
folder you unpacked), and removes the installed copy as well.

`uninstall.sh` needs nothing from the program directory, so it also works if the
program itself is broken. It removes the program, the command (only if the
symlink still points into the program directory), the menu entry, the icon, the
autostart entry (only if its `Exec` still points into the program directory),
and itself, along with `~/.local/share/SystemMonitor/` once that is empty. Your
settings stay unless you pass `--purge`.

You can delete the unpacked folder as soon as the install succeeds.

## Settings, log and autostart

| File | Path |
|---|---|
| settings | `~/.config/SystemMonitor/config.json` (`$XDG_CONFIG_HOME`) |
| log | `~/.local/state/SystemMonitor/sysmonitor.log` (`$XDG_STATE_HOME`) |
| autostart | `~/.config/autostart/systemmonitor.desktop` |
| update downloads | `~/.cache/SystemMonitor/update/` (`$XDG_CACHE_HOME`) |

The log is a short, one-line-per-event text file, trimmed when it grows. Set
`"diagnostics": false` in `config.json` to switch it off. A damaged `config.json`
is set aside as `config.json.bad` and the defaults are used, so a bad settings
file never stops the program.

## Windows 4.3.0 and Linux

| Feature | Windows 4.3.0 (`main`) | Linux (`linux-native`) |
|---|---|---|
| Install | per-user setup, optional machine-wide with one UAC prompt | per-user tar.gz and shell scripts, no root, no package manager |
| Toolkit / runtime | WPF on .NET 8 Desktop | Avalonia 12.1.5 on .NET 10, self-contained or framework-dependent |
| Widget, Overall and Full views | yes | yes, same layout and keyboard behaviour |
| Themes, Thai/English, opacity, font scale, window memory | yes | yes |
| CPU load, per core | performance counters | `/proc/stat` |
| CPU model, cores, threads, cache, base clock | WMI and cpuid | `/proc/cpuinfo` and sysfs |
| CPU temperature | ACPI thermal zone | hwmon, thermal zone, marked estimate |
| RAM usage | yes | yes |
| RAM modules and slots | yes | no (needs root) |
| Disk space, per-disk I/O, bus and media type | yes | yes |
| Disk temperature | storage stack, plus the SMART extension | hwmon only; SMART-backed drives show `n/a` |
| GPU load, engines, memory | vendor APIs | DRM fdinfo, sysfs, `nvidia-smi` |
| GPU temperature | vendor APIs | hwmon for AMD, `nvidia-smi` for NVIDIA; Intel exposes none |
| Network per adapter, LAN and Wi-Fi | yes | yes (`/proc/net/dev`, `iw`) |
| Processes | yes | yes |
| Services | Windows services; start/stop | systemd user units; system units read-only |
| Extensions and SMART | yes | no |
| Tray | native | StatusNotifier, else minimise |
| Autostart | registry Run key or Scheduled Task | XDG autostart |
| Restart | yes, optionally as administrator | yes, without elevation |
| Single instance | mutex | lock file under `$XDG_RUNTIME_DIR` |
| Update | setup `.exe` from GitHub | tar.gz from GitHub, installed by `install.sh` |
| Elevation | optional, per feature | never |
| Machine-wide install | yes | no |

## Repository layout

| Path | What it is |
|---|---|
| `SysMonitor.Core` | shared models (`Snapshot`, `Ram`, `Disk`, `IoRate`), no OS calls |
| `SysMonitor.Linux` | the Linux sensor readers and all presentation logic |
| `SysMonitor.Linux.App` | the Avalonia application: windows, controls, event wiring |
| `SysMonitor.Linux.Probe` | console probe that prints one snapshot as text or JSON |
| `SysMonitor.Linux.Tests` | the test suite (MSTest, 379 tests) |
| `packaging/linux` | `install.sh`, `uninstall.sh` and the menu-entry template |
| `Assets` | the 256×256 application icon |
| `build-linux.sh` | builds the release archive |
| `Docs` | workflow, architecture and testing documents, ADRs, handoffs |
| `CHANGELOG.md` | what each release changed |

## History

The project began as two Electron prototypes, became a Python/Tk widget
(2.x, standard library only), and was ported to C#/WPF from 3.0. The Windows
4.3.0 line lives on `main`; this branch rebuilds the program for Linux from that
release, keeping the three views and the data it shows. `CHANGELOG.md` keeps the
release history.
