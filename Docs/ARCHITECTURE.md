# Architecture

This document describes the structure and boundaries of **System Monitor** on
the `linux-native` branch. Update it when a service boundary, the data flow, the
storage or the dependency direction changes.

> Scope: this document covers the **Linux edition** (R-027) only. The Windows
> 4.3.0 edition — WPF host, extension host and contract, SMART extension,
> machine-wide install, ADR 0001 and ADR 0002 — lives on `main`. Nothing from it
> is built, tested or shipped on this branch.

## 1. System overview

System Monitor is a native Linux desktop widget (C#/.NET 10, Avalonia) that
shows CPU, RAM, disk, GPU and network figures. It is one process with no
privileged helper, no daemon and no service: it reads what the kernel already
publishes for the logged-in user (`/proc`, `/sys`, hwmon, DRM fdinfo) and asks a
few command-line tools for the rest (`iw`, `nvidia-smi`, `systemctl --user`).

The program is installed per user into `~/.local/share/SystemMonitor/app`, and
updated by replacing that directory in place while the metadata that points at
it (menu entry, command, autostart entry) stays unchanged.

### Goals

- The three views and the readings of the Windows 4.3.0 edition, on Linux.
- No root and no elevation at run time. Two install paths: the per-user tar.gz
  (`install.sh`, no package manager) and, since the 4.3.0 release, an optional
  `.deb` (`/opt/systemmonitor`) that the user installs through their own
  package tools. The app never runs `sudo`, `pkexec`, `apt` or `dpkg`.
- Values that need privileges are shown as `n/a`; nothing is guessed and no
  prompt appears.
- All logic that can be tested lives in `SysMonitor.Linux`, away from Avalonia.

### Non-goals

From `Requirements.md` R-027:

- No extension system on Linux (R-027 excludes it).
- No full SMART scanning; disk temperature comes from hwmon only.
- No `.rpm`, Flatpak or Snap package (the `.deb` was added for the 4.3.0 release at the user's request, 2026-11-20).
- No behaviour change to the Windows edition.

## 2. Context diagram

```text
                        ┌───────────────┐
                        │     user      │
                        └───────┬───────┘
   window / tray / menu entry    │    autostart entry (XDG)
                                ▼
┌──────────────────────────────────────────────────────────────────────┐
│ one unprivileged process: sysmonitor                                 │
│                                                                      │
│  ShellWindow ◄── Snapshot (Volatile) ── LinuxSampler (background)    │
│      │                                        │  /proc  /sys  hwmon   │
│      │                                        │  DRM fdinfo           │
│      │                                        └──► iw, nvidia-smi,    │
│      │                                             systemctl --user   │
│      ├──► ShellModel ──► Widget/Overall/Full board models ──► rows    │
│      ├──► FileAutostart ──► $XDG_CONFIG_HOME/autostart/…desktop       │
│      ├──► TrayIconHost ──► StatusNotifierWatcher (busctl)             │
│      └──► GitHubUpdater ──► api.github.com / release assets (HTTPS)   │
│                                                                      │
│  AppConfig ─ $XDG_CONFIG_HOME/SystemMonitor/config.json               │
│  Diagnostics ─ $XDG_STATE_HOME/SystemMonitor/sysmonitor.log           │
│  SingleInstanceLock ─ $XDG_RUNTIME_DIR/sysmonitor.lock                │
└──────────────────────────────────────────────────────────────────────┘
             │ on update: /bin/sh install.sh --update --wait-pid <pid>
             ▼
   $XDG_DATA_HOME/SystemMonitor/app  (replaced in place)
```

**Trust boundaries**

| Boundary | Meaning | Enforced by |
|---|---|---|
| kernel interfaces → app | `/proc` and `/sys` are read-only inputs; a missing or unreadable file empties that section and leaves the rest of the sample intact | per-source `catch` in `LinuxSampler` and the individual readers |
| external tools → app | `iw`, `nvidia-smi` and `systemctl` output is parsed, never executed as shell; each call has a timeout | `ProcessCommandRunner` (`ArgumentList`, timeout, no shell) |
| GitHub → app | HTTPS only, host allow-list, manual redirects, SHA-256 before extraction | `GitHubUpdater` |
| archive → disk | the updater refuses links, absolute paths and `..`, then requires a regular `install.sh` at the archive root | `TarExtractor`, `IsInstallerFile` |
| update script → installed tree | `install.sh` copies inside `$XDG_DATA_HOME/SystemMonitor`, never into `$HOME` directly; every path is resolved physically (`cd -P`) and re-checked immediately before each rename or removal | `install.sh` / `uninstall.sh` boundary helpers |

## 3. Repository map

```text
SystemMonitor/
├── SysMonitor.Core/            # shared models: Snapshot, Ram, Disk, IoRate (no OS calls, no packages)
├── SysMonitor.Linux/           # sensor readers + all presentation logic (no packages)
│   ├── Sensors/                # ProcStat, MemInfo, DiskReader, NetDev, GpuReader, Hwmon, …
│   ├── Sampling/               # LinuxSampler: one Snapshot per tick
│   └── Presentation/           # ShellModel, boards, config, lang, updater, autostart, tray, restart
├── SysMonitor.Linux.App/       # Avalonia application (the only project with a package)
├── SysMonitor.Linux.Probe/     # console probe: one snapshot as text or JSON
├── SysMonitor.Linux.Tests/     # MSTest suite, fake roots and fake command runners
├── packaging/linux/            # install.sh, uninstall.sh, systemmonitor.desktop template
├── Assets/systemmonitor.png    # 256×256 icon, the largest frame of SysMonitor.ico
├── build-linux.sh              # publish + stage + archive + sha256
├── Docs/                       # workflow, architecture, testing, ADRs, handoffs
└── dist/                       # build output (gitignored)
```

## 4. Components and ownership

| Component | Responsibility | Public interface |
|---|---|---|
| `SysMonitor.Core` | models shared by sampler and UI | `Snapshot`, `Ram`, `Disk`, `IoRate`, `Battery`, `PowerSource`, `PeripheralBattery` (R-028) |
| `SysMonitor.Linux` (sensors) | read `/proc`, `/sys`, hwmon, DRM fdinfo and the three external tools into one `Snapshot` | `LinuxSampler.Sample(bool processes, bool services, bool keepMemory, bool grid)`, plus one pure reader per source (`ProcStat`, `MemInfo`, `DiskStats`, `MountInfo`, `BlockDevices`, `NetDev`, `Hwmon`, `ThermalReader`, `DrmFdinfo`, `GpuReader`, `ProcessReader`, `ProcessStats`, `ProcessGpu`, `AppResolver`, `ServiceReader`, `BatteryReader` (`/sys/class/power_supply`), `UPowerReader` (`upower --dump` every 30 s, peripherals; ids are hashes, never MAC or serial)) |
| `SysMonitor.Linux` (presentation) | turn a snapshot into row models, hold settings, own the integrations | `ShellModel`, `WidgetBoard`, `OverallBoard`, `FullBoard`, `UiRows`, `AppConfig`, `Lang`, `Palette`, `ChartMath`, `Format`, `ViewState`, `WindowGeometry`, `UpdateAutoCheck`, `GitHubUpdater`, `TarExtractor`, `FileAutostart`, `DesktopEntry`, `TrayText`, `TrayHost`, `Restart`, `SingleInstanceLock`, `KillFlow`, `ServiceControl`, `Diagnostics` |
| `SysMonitor.Linux.App` | Avalonia windows, controls, event wiring, sampling loop, tray | `Program`, `App`, `ShellWindow`, `WidgetView`, `OverallView`, `FullView`, `PanelWindow`, `ChartControl`, `DialView`, `TrayIconHost`, `Probe` |
| `SysMonitor.Linux.Probe` | developer console view of one snapshot | `dotnet run --project SysMonitor.Linux.Probe -- [--json]` |
| `SysMonitor.Linux.Tests` | the suite | `dotnet test SysMonitor.Linux.Tests` |
| `packaging/linux` + `build-linux.sh` | per-user install, update and uninstall; `.deb` build | `install.sh`, `uninstall.sh`, `build-linux.sh [--deb]`, `packaging/linux/deb/` |

## 5. Dependency rules

| Project | ProjectReference | Notes |
|---|---|---|
| `SysMonitor.Core` | none | shared models only; no package, no OS API; `InternalsVisibleTo` `SysMonitor.Linux` and `SysMonitor.Linux.Tests` |
| `SysMonitor.Linux` | `SysMonitor.Core` | readers instead of OS APIs; every reader takes a root (default `/`) so tests use a fixture tree; `ICommandRunner` is the only way to run a tool; `InternalsVisibleTo` `SysMonitor.Linux.Tests` |
| `SysMonitor.Linux.Probe` | `SysMonitor.Linux` | console only, no window, no package |
| `SysMonitor.Linux.App` | `SysMonitor.Linux` | Avalonia only here; UI and event wiring, no sensor parsing |
| `SysMonitor.Linux.Tests` | `SysMonitor.Core`, `SysMonitor.Linux` | test packages only |

- **No `PackageReference` in a product project, with one exception**: `SysMonitor.Linux.App` uses `Avalonia`, `Avalonia.Desktop` and `Avalonia.Themes.Fluent` 12.1.5 (MIT). Linux has no UI stack in .NET comparable to WPF, and R-027 decision 1 allowed exactly this: the UI project of the Linux edition, and nothing else. `SysMonitor.Core`, `SysMonitor.Linux` and `SysMonitor.Linux.Probe` have no packages.
- The direction is `App → Linux → Core`; nothing points back. Avalonia types never appear in `SysMonitor.Linux`, which is why every board, updater and tray rule can be tested without a display.
- External tools are reached only through `ICommandRunner`, so a test never runs `systemctl`, `iw` or `nvidia-smi`.

## 6. Runtime data flow

### (a) Sampling → snapshot → presentation models → views

1. `App.OnFrameworkInitializationCompleted` loads `AppConfig`, creates `Diagnostics`, `ShellModel` (with `FileAutostart`, `GitHubUpdater`, `ProcessSignals`) and `ShellWindow`.
2. `ShellWindow.SampleLoop` runs on a background thread. Every `MetricsIntervalMs` (eco 5000, balanced 2500, fast 1000) it calls `LinuxSampler.Sample(...)`; the first sample is not `Ready` because a usage percentage needs two readings.
3. Processes and services are read on their own cadence (`MemoryCadence`: processes every 3 s, services every 10 s) and only while the RAM tab is visible; `keepMemory` republishes the last list between reads so the tab does not empty.
4. The Apps and Processes grids have a separate 2 s cadence (`MemoryCadence.GridMs`) and are read only while one of those pages is showing; `includeGrid` fills `Snapshot.ProcessRows` (one `ProcessStats`) and `Snapshot.Apps` (one `AppResolver`) and republishes the last lists between reads, so the grid does not blank. Entering either page forces one read.
5. The sampler publishes the new `Snapshot` with `Volatile.Write`. While the window is hidden to the tray, it also posts the sample to the UI thread so history and the tray tooltip keep moving.
6. A 400 ms `DispatcherTimer` reads the snapshot and calls `ShellModel.SetSnapshot`. The shell then builds rows through `WidgetBoard`, `OverallBoard` and `FullBoard`, which are pure functions of the snapshot, the configuration and the language. History (`ChartMath`) and dial geometry are computed the same way.
7. Views render those row models; they do not read hardware and they do not format numbers themselves.

Failure behaviour (verified in code):

- one reader throws → that section is empty for the tick, the rest of the snapshot is still published; the sampler catches anything that escapes as `sample failed` in the log
- a command-line tool is missing, times out or returns non-zero → that value is `null`; the row shows `n/a` and the app keeps running
- an unreadable `/proc/<pid>` entry is skipped, not fatal

### (b) Autostart, tray, close and restart

1. The autostart switch writes `$XDG_CONFIG_HOME/autostart/systemmonitor.desktop` atomically (temp file + rename). `Exec` is escaped in two stages: Exec quoting (`"`, `` ` ``, `$`, `\` escaped; `%` doubled) and string-value escaping (every `\` doubled). `IsEnabled` requires file present, not `Hidden=true`, and exactly one argument equal to the running executable after both stages are reversed.
2. Closing to the tray first asks `TrayHost.IsAvailable` (`busctl --user status org.kde.StatusNotifierWatcher`, exit 0). With a host the window hides and a `TrayIcon` with Show/Exit stays behind; without one the window minimises and `ShowInTaskbar` is switched on, so it can never be lost. The fallback is logged once per run.
3. Quit stops the timers and the sampler, saves placement and configuration, and disposes the tray.
4. Restart saves placement and configuration, starts `Environment.ProcessPath` with the same arguments through `ProcessStartInfo.ArgumentList`, then shuts down. `SingleInstanceLock.Acquire` waits up to 5 s so the new copy can take the lock; `--allow-multiple` bypasses the lock.
5. `ShutdownRequested` saves the placement when the session ends.

### (c) Update

1. The start-up check (`UpdateAutoCheck`) runs only while the setting is on, retries once after 60 s, and stays silent until the side panel is opened.
2. `GitHubUpdater.CheckAsync` makes one HTTPS `GET` to `/releases?per_page=30` (not `/releases/latest`, which belongs to the Windows edition) with the allow-listed hosts; `UpdateReleaseParser.Select` keeps only non-draft, non-prerelease `SystemMonitorLinux-<ver>` tags and takes the highest version (none → `up_to_date`). It follows at most five redirects by hand (each hop must stay https and allow-listed), and looks for `SystemMonitor-<ver>-linux-<arch>.tar.gz` plus its `.sha256` asset — or, for a `.deb` install (binary under `/opt/systemmonitor/` and `dpkg-query` reports `install ok installed`), `systemmonitor_<ver>_<debarch>.deb` plus `.sha256`. A missing asset or missing checksum is `check_failed`, never `up_to_date`.
3. `DownloadAsync` downloads into `$XDG_CACHE_HOME/SystemMonitor/update`, reports progress, deletes a partial file on failure, verifies the SHA-256, and clears a pending update on any error.
4. `TarExtractor` refuses absolute paths, `..`, links, devices and FIFOs, and extracts into a freshly emptied `…/update/<ver>/`. `install.sh` must be a regular file inside that directory.
5. `.deb` install: the verified package is saved to the XDG download directory and opened with `xdg-open`, so the desktop's package installer asks for authentication; the app does nothing privileged itself. Steps 4 and 6 apply to the tar.gz install only.
6. The updater then starts `/bin/sh install.sh --update --wait-pid <own pid>` detached through `ArgumentList`, saves the configuration and exits. `install.sh` waits up to 30 s for the pid, restores the previous version if an interrupted run left `app.old` behind, installs with two renames, and starts the new copy; a pid that does not exit leaves everything untouched and exits 1.

## 7. Data and storage

| Entity/file | Real path | Owner | Notes |
|---|---|---|---|
| `config.json` | `$XDG_CONFIG_HOME/SystemMonitor/config.json`, default `~/.config/SystemMonitor/config.json` (`AppConfig.ConfigDirectory`) | `AppConfig` | JSON, snake_case; written through `path + ".tmp"` and rename; missing or corrupt = defaults, corrupt file kept as `config.json.bad` |
| `sysmonitor.log` | `$XDG_STATE_HOME/SystemMonitor/sysmonitor.log`, default `~/.local/state/SystemMonitor/` (`Diagnostics.StateDirectory`) | `Diagnostics` | one line per event, trimmed when large, best effort, switched off by `diagnostics` |
| `systemmonitor.desktop` (autostart) | `$XDG_CONFIG_HOME/autostart/` | `FileAutostart` | temp file + rename; disabling deletes only this file |
| `sysmonitor.lock` | `$XDG_RUNTIME_DIR/sysmonitor.lock`, falling back to the config directory | `SingleInstanceLock` | held open with `FileShare.None`; a restart waits up to 5 s for the old copy |
| update cache | `$XDG_CACHE_HOME/SystemMonitor/update/` | `GitHubUpdater` | archive, checksum, and `…/<ver>/` extraction; deleted on failure |
| installed program | `$XDG_DATA_HOME/SystemMonitor/app/`, default `~/.local/share/SystemMonitor/app/` | `install.sh` | replaced by two renames: `app` → `app.old`, `app.new` → `app`, then remove `app.old`; an interrupted run is repaired by the next one |
| installed uninstaller | `$XDG_DATA_HOME/SystemMonitor/uninstall.sh` | `install.sh` | copied outside `app/` (mode 755) so the swap never touches it; removed by itself, and the then-empty directory with it |
| command | `~/.local/bin/sysmonitor` (symlink) | `install.sh` | created by the installer, not in the archive; removed only while it still points into `app/` |
| menu entry | `$XDG_DATA_HOME/applications/systemmonitor.desktop` | `install.sh` | `Exec` shares the two-stage escaping rule |
| icon | `$XDG_DATA_HOME/icons/hicolor/256x256/apps/systemmonitor.png` | `install.sh` | theme name `systemmonitor` |

- Source of truth: one JSON file per user plus the XDG files above. No database,
  no schema migration, no backup.
- Transaction boundary: configuration and autostart use temp + rename. For the
  program directory, the swap is two renames; if it is interrupted, running
  `install.sh` again (or the next update) restores the previous version
  (`app` → `app.old` → `app`, then remove `app.old`).
- Settings and the autostart entry live outside the install directory, so an
  update never touches them. The installed uninstaller does too: it sits beside
  `app/`, not inside it.
- Both scripts keep every path physically under `$HOME`. `install.sh` refuses a
  relocated `XDG_DATA_HOME` or `XDG_CONFIG_HOME` and `uninstall.sh` refuses to
  remove a path outside the home directory, so what is installed is always
  removable by the same rules.

## 8. Interfaces

### Command execution

- `ICommandRunner.Run(string file, string arguments, int timeoutMs)` is the only
  way the program reaches an external tool; `ProcessCommandRunner` uses
  `ArgumentList`, no shell, and a timeout. Tests inject a fake.

### Update contract with the release archive

- Asset names: `SystemMonitor-<version>-linux-<arch>.tar.gz` and
  `SystemMonitor-<version>-linux-<arch>.tar.gz.sha256` (`arch` from
  `RuntimeInformation.OSArchitecture`: `x64`, `arm64`).
- The checksum file holds the hex digest, optionally followed by a file name, as
  `sha256sum` writes it.
- The archive is flat, holds `install.sh` at the root, contains no links, and is
  extracted by `System.Formats.Tar` + `GZipStream`.
- `install.sh --update --wait-pid N` waits up to 30 s for `N`, installs, then
  starts the new `sysmonitor` detached (`setsid`, else `nohup`).
  `SYSMONITOR_NO_LAUNCH=1` suppresses only that launch.
- The archive also carries `uninstall.sh`. `install.sh` copies it to
  `$XDG_DATA_HOME/SystemMonitor/uninstall.sh`, outside the `app/` tree that an
  update replaces, and `uninstall.sh` finds the install from the XDG paths
  rather than from its own location.

### External dependencies

| Dependency | Purpose | Timeout / retry | Failure impact |
|---|---|---|---|
| `/proc`, `/sys`, `/sys/class/hwmon`, DRM fdinfo | every reading | none; one read per tick | section empty, rest of the snapshot intact |
| `iw dev <if> link` | Wi-Fi SSID, bit rate, quality | 2 s per call | Wi-Fi rows show `n/a` |
| `nvidia-smi` | NVIDIA load, memory, temperature | 3 s, asked only while the card is awake | NVIDIA rows fall back to DRM counters |
| `systemctl --user` | service list and user-unit actions | read timeout in `ServiceReader` | services list empty, actions report the error |
| `busctl --user status org.kde.StatusNotifierWatcher` | tray availability | through `ICommandRunner` | close minimises instead of hiding |
| `api.github.com` and release asset hosts | update check and download | 30 s `HttpClient` timeout, one 60 s retry, five manual redirects | `check_failed` / `download_failed`, never `up_to_date` |

## 9. Cross-cutting concerns

- **Configuration** — one `config.json` loaded at start and passed as one object
  to the shell, the sampler and the integrations; the caller saves, not the
  reader; unknown keys are ignored and missing keys take defaults.
- **Localisation** — `Lang` holds Thai and English; an unknown key falls back to
  English and then to the key itself. Both languages are tested.
- **Logging** — one line per event, no stack traces beyond the message, capped
  and trimmed; diagnostics can be switched off, and a startup failure is written
  even then.
- **Theme** — light by default; `Palette` is a pure function so colours are
  tested without a window.
- **Single instance** — a lock file; a second launch exits silently.
- **No elevation, no secrets, no telemetry** — the only network call is the
  update check and download the user asks for.

## 10. Quality attributes

| Attribute | Target | How verified |
|---|---|---|
| Test suite | green before packaging | `dotnet test SysMonitor.Linux.Tests`: 379 passed, 0 failed (2026-11-20) |
| Build | no warnings | `dotnet build SysMonitor.Linux.App -c Release`: 0 warnings, 0 errors |
| No root anywhere | install, update, uninstall and every reading | `install.sh`/`uninstall.sh` refuse `id -u` 0; readers use only user-readable paths |
| Values are never invented | unreadable source → `n/a` | tests per reader with fixture trees; no fallback constants |
| Update archive integrity | SHA-256 checked, links refused, `install.sh` at root | `UpdaterTests`, `TarExtractor` tests |
| Interrupted install is repaired | the next run puts the previous version back | `install.sh` recovery step, sandbox transcript in `Docs/HANDOFF-R027-phase4.md` |
| Latency / throughput | not specified | — |

## 11. Architecture decisions

| Decision | Status |
|---|---|
| Linux edition built from the 4.3.0 line as a separate branch, Windows code left on `main` | Accepted (R-027 decision 3) |
| Avalonia 12.1.5 (MIT) as the one package exception, in `SysMonitor.Linux.App` only | Accepted (R-027 decision 1) |
| Sensors and presentation logic in `SysMonitor.Linux`, testable through fake roots and `ICommandRunner` | Accepted (R-027) |
| Per-user tar.gz with shell scripts instead of a distribution package | Accepted (R-027); `.deb` added alongside for 4.3.0 |
| Linux releases tagged `SystemMonitorLinux-<ver>`, published as not-Latest; the Linux updater lists releases instead of reading `/releases/latest` | Accepted (2026-11-20) |
| Archive layout and `install.sh --update --wait-pid` contract | Accepted (R-027 phase 3 handoff) |

ADR 0001 (extension architecture) and ADR 0002 (machine-wide install) describe
the Windows edition and remain on `main`; neither applies to this branch.

## 12. Known constraints and technical debt

1. **Install and uninstall share one physical boundary: `$HOME`.** Both scripts
   reject a `HOME` or `XDG_*` value with a `.` or `..` component or a newline,
   resolve each target through its nearest existing ancestor with `cd -P`, and
   require the result under the physical home directory. A symlinked
   `XDG_DATA_HOME`, `XDG_CONFIG_HOME` or `~/.local/bin` that leads outside the
   home directory is refused, not followed. The same check runs again
   immediately before every `rm -rf` and every `mv`, which also refuses a target
   that has become a symlink; `app.new` and `app.old` must be real directories.
   Nothing can therefore be installed that the uninstaller would decline to
   remove. `XDG_STATE_HOME` is checked only with `--purge`, in a preflight
   before the first removal: an escaping value exits 1 with the install and the
   settings still in place. The launcher is the one symlink the scripts create
   and remove; it goes with `rm -f`, never with `-r`.
2. **The installed uninstaller is a copy, fixed at install time.** `install.sh`
   writes `$XDG_DATA_HOME/SystemMonitor/uninstall.sh`, so a user who deletes the
   unpacked folder can still uninstall; the archive's own copy works as well and
   removes the installed one. An install made before this change, or one whose
   copy was deleted, still needs the archive.
3. **The `SystemMonitor/` data directory is removed only when empty.** The
   uninstaller removes itself and then the directory; a leftover `app.old`, or
   any other file beside it, keeps the directory in place.
4. **The tray needs a StatusNotifier host.** Without one, close minimises; the
   window cannot be hidden with no way back, but the behaviour differs from KDE.
5. **No Intel GPU temperature**, no memory modules, no SMART-backed disk
   temperature, no `r8152` link speed — all need root or are not exposed at all.
6. **The app never elevates**, so system units are read-only and processes owned
   by other users cannot be signalled.
7. **The probe output is developer-facing** and not a supported user interface.
8. **The update swap is two renames, not one atomic step.** A run killed between
   them leaves `app/` missing with the previous version in `app.old/`, and the
   program cannot start until the next `install.sh` run (or the next update)
   puts it back. The repair is automatic, but it is not instantaneous.

Not done and not to be assumed finished: a real update run from an older release
to a newer one (no Linux release exists yet), tray behaviour on GNOME without an
AppIndicator extension, and an install on a machine without `update-desktop-database`
or `gtk-update-icon-cache`.
