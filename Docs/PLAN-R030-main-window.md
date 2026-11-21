# Plan — R-030: one main window with a Resources-style sidebar

Branch `linux-native` · 2026-11-20 · Coding Owner (Claude Code)
Windows follow-up: R-031 on `main`; Mac: R-032 on `Mac`. Target version **4.4.0** on all three.

## 1. What the user asked for

The new GUI takes its sidebar from the Resources app (`net.nokyan.Resources`).

- The sidebar shows a small graph for every module.
- **Overall** shows the Overall page in the right frame, as today.
- **Settings** expands in place and lists the existing settings.
- **Apps** lists every running app with Memory/CPU/GPU use:
  - close and restart buttons;
  - its sessions can be opened;
  - shown as a grid that sorts by name or by any resource.
- **Processes** lists every process with PID, User and Memory/CPU/GPU use:
  - close and restart buttons;
  - shown as a grid that sorts by name or by any resource.
- Each module's graphs appear with its device name.

The user's choices (2026-11-20):

| Question | Choice |
|---|---|
| Which views it replaces | **Overall + Full** become one main window. The Widget stays a separate small window, and double-click switches Widget ↔ main window. |
| Restart | Only where it is safe. **Apps:** close every instance, then relaunch from the app's `.desktop` entry. **Processes:** restart only a process that is a systemd user service (`systemctl --user restart`). Any other process has a close button only. |
| Sessions | The app's instances, and the processes under each instance, each with its CPU/RAM/GPU use. One instance can be closed at a time. |
| Windows edition | Send the requirement to `main` now (R-031). |

## 2. Layout

```
┌──────────────────────┬──────────────────────────────────────────────┐
│ ▣ Overall            │                                              │
│ ▦ Apps               │   right frame: the page for the selected     │
│ ≡ Processes          │   sidebar item                               │
│ ⚙ Settings      ▾    │                                              │
│   (expands: today's  │   • Overall   → today's Overall content      │
│    sidebar settings) │   • Apps      → app grid                     │
│ ──────────────────── │   • Processes → process grid                 │
│ CPU            14 %  │   • a module  → today's Full tab content     │
│ Intel i5-1035G1 ▁▂▃▅ │     (facts, big and small charts)            │
│ Memory    4.0/7.4 GB │                                              │
│ ▁▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂ │                                              │
│ GPU · Intel Iris  0% │                                              │
│ GPU · MX250       0% │                                              │
│ Disk · WDS250G…  R/W │                                              │
│ Disk · Backup+ … R/W │                                              │
│ Wi-Fi · wlp0s20f3    │                                              │
│ Ethernet · enx…      │                                              │
│ Battery   100 % ⚡    │                                              │
└──────────────────────┴──────────────────────────────────────────────┘
```

- **Sidebar.** About 230 px wide; the user can resize it between 200 and 320 px,
  and the width is remembered. It scrolls on its own.
  - The nav items come first. **Settings** is an expander, collapsed by
    default, and its state is remembered. Inside it are today's Overall
    sidebar rows (checks, radios, dials, buttons, restart, manual, about),
    unchanged.
  - Each module item shows: title (module · short device name), current
    value on the right, the full device name in small type, and a
    72-point sparkline in the module's accent colour (the same
    `ChartMath` drawing, but with no grid and no frame).
  - There is one item per CPU, per memory, per GPU, per physical disk,
    per network adapter and per battery. The battery comes from R-028
    and hides itself when there is none. The `show_*` settings hide
    their modules.
  - The selected item is highlighted the way the Full tabs are today.
    The selection is remembered (`main_page`).
- **Right frame.** The page for the selection.
  - A module page is today's Full detail pane (heading and hardware,
    facts card, big and small charts).
  - The memory page **drops** its process list, which moves to Processes.
  - **Services** (from phase 2b) moves into the Processes page as a
    second tab. This is my placement: the user did not say where
    services go.
- **Module options on each module page.** The user added this on
  2026-11-20, following Resources' "Show Usage of Logical CPUs":
  > ผมต้องการให้ setting เดิมที่เราเคยมี ย้ายไปอยู่ตรงนั้นด้วยครับ

  Each module page gets an options bar under its heading, a row of
  compact checkboxes. The per-module settings **move** there from the
  sidebar:

  | Module page | Options (existing config key) |
  |---|---|
  | CPU | Show usage of logical CPUs (`cpu_mode` separated/combined = today's "per core") · CPU temperature (`cpu_temperature`) · Estimate temperature when no sensor (`temp_estimate`) |
  | Disk (each) | One graph per drive (`disk_mode`) · Removable drives (`include_removable`) · Network drives (`include_network`) |
  | Network (each) | One graph per adapter (`network_mode`) · Include wireless (`include_wireless`) |
  | GPU, Memory, Battery | None for now. The bar is hidden when a module has no options. |

  - These settings are global: one `cpu_mode` for the whole app. They
    also shape the Widget and Overall, as today.
  - Changing one on a module page refreshes every view straight away.
  - The **Show/hide module** checks (`show_cpu`, `show_ram`, `show_disk`,
    `show_gpu`, `show_network`, `show_battery`) stay in **Settings**
    under a "Modules" heading, not on the module pages. Once a module is
    hidden, its page leaves the sidebar, so a toggle on that page could
    never be switched back on. (My decision; the user did not say.)
  - **Settings** then holds only the app-wide options:
    - window (always on top, snap, autostart, light mode, opacity,
      font size);
    - close action, speed and language;
    - updates;
    - modules shown;
    - restart, manual and about.
- **Memory page: Swap and RAM properties.** The user added this on
  2026-11-20 after looking at Resources' Memory page:
  > และเดิมเราไม่มี Swap memory และ properties RAM แบบละเอียด

  The page has two usage charts, Memory and Swap, each captioned
  "used / total · %". Swap is new; nothing in the code reads it today.
  - **Swap.** Read `SwapTotal` and `SwapFree` from `/proc/meminfo`, which
    covers swap files, partitions and zram together.
    - The facts card also lists each device from `/proc/swaps`: name,
      type, size, used and priority.
    - With no swap the chart is replaced by "No swap".
    - The sidebar Memory item shows both lines (RAM, plus swap underneath
      in a lighter shade).
  - **RAM properties card.** No root needed: systemd-udev already decodes
    SMBIOS type 17 into the world-readable `/run/udev/data/+dmi:id`
    (`MEMORY_DEVICE_<n>_*` and `MEMORY_ARRAY_*`). Show these fields:

    | Field | Source |
    |---|---|
    | Slots used | "n of m", from devices with a size > 0 out of `MEMORY_ARRAY_NUM_DEVICES` or the device count |
    | Max capacity | `MEMORY_ARRAY_MAX_CAPACITY` |
    | Per module: slot | `LOCATOR` |
    | Per module: size | `SIZE` |
    | Per module: type and detail | `TYPE`, `TYPE_DETAIL` |
    | Per module: form factor | `FORM_FACTOR` |
    | Per module: speed | `CONFIGURED_SPEED_MTS`, shown with the rated `SPEED_MTS` |
    | Per module: maker | `MANUFACTURER` |
    | Per module: rank | `RANK` |
    | Per module: voltage | `CONFIGURED_VOLTAGE` |

    - On this machine that gives 2 of 2, LPDDR4 Synchronous, Row Of Chips,
      2667 MT/s (rated 4267), Micron, 4 GB × 2.
    - **Never** read or show `SERIAL_NUMBER`, `ASSET_TAG` or `PART_NUMBER`,
      the same privacy rule as the battery.
    - Missing file or keys (VMs, containers, udev without DMI) → the card
      shows only what `/proc/meminfo` gives and says "Hardware details not
      available".
    - Read once at start; it doesn't change.
  - The `--probe-ui`/probe output prints the swap and properties sections.
  - This is new reading work for phase A.
- **Header.** Title, then buttons to switch to the Widget, go full screen
  (F11) and close. Esc leaves full screen.
- **Config migration.** `last_view` `overall` or `full` becomes `main`.
  `full_tab` maps to `main_page`. Window size and position are taken
  from the old Full geometry.

## 2b. Module detail pages — Resources layout on all three editions

The user's instruction (2026-11-20), after sending six screenshots of
Resources' module pages:
> ให้แสดงผลในลักษณะนี้ทั้งบน windows linux และ mac ครับ

The same page layout applies to Linux (R-030), Windows (R-031, on `main`)
and Mac (R-032, on `Mac`).

**Page structure.** Every module page has the same order:

1. **Header.** The device name, and under it the module label (e.g. "GPU 1").
2. **Options bar.** See "Module options" in §2.
3. **Usage.** A stack of charts. Each chart has a caption and a value line
   of the form `now · Highest: peak`. The peak is the highest value in the
   visible history, and resets when the page opens.
4. **Sensors.** A temperature chart with the same `now · Highest` line,
   shown only where a sensor exists.
5. **Properties.** A list of fields, one label/value pair per row.
   A value that cannot be read shows as "N/A". It is never guessed.

Today's extras stay: L2/L3, the GPU engine breakdown, per-volume I/O, and
the SMART temperature/health from R-0xx.

**Gaps against what the Full view shows today** (from the `--probe-ui`
dump on this machine). These are the fields to add, given as the Linux
source (Windows / Mac source in R-031 / R-032).

| Module | Usage charts to add | Properties to add |
|---|---|---|
| CPU | Each logical-CPU card shows its current clock (`cpufreq/scaling_cur_freq`). A temperature chart with its peak. | Max frequency (`cpuinfo_max_freq`) · logical cores · physical cores · sockets · uptime · virtualisation **type** (VT-x / AMD-V, not just Enabled) · architecture |
| Memory | Swap (§2) | RAM properties card (§2). On Linux today the module list is empty, so the card is new. |
| GPU (each) | Separate charts for usage, video encoder/decoder, and video-memory usage (`used / total · %`). A temperature chart with its peak, or "N/A". | Manufacturer · PCI slot · driver (exists) · GPU clock · video-memory clock · power usage · max power cap · link (`current / max`, e.g. "PCIe 1.0 ×4 / PCIe 3.0 ×4") |
| Disk (each physical drive) | Drive activity % (busy time, `/sys/block/X/stat` io_ticks) · read speed and write speed as **separate** charts with their peaks · total read and total written since boot. The per-volume cards stay below. | Type (NVMe / SSD / HDD) · device (`nvme0n1`) · capacity · writable · removable · link (e.g. "USB 2.0 (480 Mb/s)", "PCIe 3.0 ×4") |
| Network (each) | Download and upload as separate charts with their peaks · total received and total sent since boot | Interface · type · driver (exists) · MAC **hidden** (privacy rule) · IPv4/IPv6 · link speed (wired) or SSID, signal and frequency (Wi-Fi) |
| Battery | Charge chart `100 % (Full)` · power-usage chart with its peak | Health · design capacity · charge cycles · technology · manufacturer · model · device. This matches R-028 phase B, which moves here. |

**Platform sources** (detail in R-031 and R-032):

- **Windows:**
  - PDH and WMI (`Win32_Processor`, `Win32_PhysicalMemory`,
    `MSFT_PhysicalDisk`, `Win32_Battery` / `BatteryStaticData`);
  - the PDH GPU Engine counters;
  - NVML or `nvidia-smi` for NVIDIA clocks, power and link.
- **Mac:**
  - `sysctl`, `host_statistics`, `vm.swapusage`;
  - IOKit (`IOBlockStorageDriver` statistics, `AppleSmartBattery`,
    `IOPCIDevice`);
  - IOReport for GPU use;
  - `system_profiler SPMemoryDataType` once at start for RAM.

  Anything Apple does not expose shows as "N/A".

## 3. Apps page

**Where apps come from (Linux).** Desktop sessions start each app in its
own cgroup under `user@UID.service/app.slice`:

- `app-<desktop-id>-<n>.scope` or `app-<desktop-id>@<id>.service` is one
  instance. KDE and GNOME both do this; this machine already shows
  Konsole, VS Code, Dolphin and Resources this way.
- `<desktop-id>` is resolved to a `.desktop` file through the XDG data
  dirs, including Flatpak exports, which give the name and icon.
- An instance's processes are read from its `cgroup.procs`, recursively.
- `@autostart` services (Dropbox, KDE Connect) are listed as apps with a
  "starts at login" marker.
- Processes outside `app.slice` are not apps. They appear only on the
  Processes page.

**Grid columns.**

| Column | Notes |
|---|---|
| Expand | ▸ shows the instances, and under each instance its processes. |
| Icon | From the `.desktop` file. |
| Name | |
| Instances | |
| CPU % | As a share of the whole machine. |
| Memory | Sum of RSS. |
| GPU % | |
| GPU memory | |
| Actions | Close and Restart for the app; Close for each instance. |

- Clicking a column header sorts by it; clicking again reverses the
  order. The sort is remembered.
- A search box filters by name.
- The grid refreshes every 2 s, and only while the page is visible.
- Rows are updated in place, keyed by ID, so an expanded app stays
  expanded and the button under the pointer does not move. This is the
  same keyed sync as phase 2b.

**Actions.** Every action asks for confirmation first.

- **Close instance.** SIGTERM to every process in the instance, through
  the phase 2b pidfd `KillFlow`, with the force prompt after 3 s. Uses
  the same protection rules.
- **Close app.** The same, for every instance.
- **Restart app.** Close every instance, wait up to 5 s for them all to
  exit, then launch the app once from its `.desktop` `Exec`:
  - parsed with the phase 3 Desktop Entry parser, with field codes
    removed;
  - started with `ProcessStartInfo.ArgumentList` and no shell;
  - the restart button is disabled when the `.desktop` file is missing or
    its Exec cannot be parsed.

## 4. Processes page

Two tabs: **Processes** and **Services** (the phase 2b list, moved here).

**Columns.**

| Column | Notes |
|---|---|
| Name | |
| PID | |
| User | Name looked up from the UID. |
| CPU % | |
| Memory | RSS. |
| GPU % | |
| GPU memory | |
| Command | Tooltip with the full command line. |
| Actions | Close, and Restart only for services. |

- All users' processes are shown.
- The phase 2b protection rules apply: another user's process,
  session-critical processes and kernel threads show a lock instead of
  Close.
- Restart is shown only when the PID is the `MainPID` of a systemd
  **user** unit. It runs `systemctl --user restart -- <unit>` after a
  confirm.
- Sorting, search, 2 s refresh and keyed sync work as on the Apps page.
  Command is the only column that does not sort: the request was to sort
  "by name or by resource used" (decided by Coding Owner, 2026-11-21).

## 5. Per-process measurements (Linux)

| Value | Source | Cost and cadence |
|---|---|---|
| CPU % | Δ(`utime` + `stime`) from `/proc/<pid>/stat` ÷ (Δt × logical CPUs × clock ticks) | Every 2 s while Apps or Processes is open. |
| Memory | `VmRSS` from `/proc/<pid>/status`. PSS is more accurate but needs `smaps_rollup` per process, which is too slow every 2 s; offer it later. | Same. |
| User | UID → `/etc/passwd`, cached. | — |
| GPU %, GPU memory | DRM fdinfo per process: `drm-engine-*` busy time and `drm-memory-*` for i915 and amdgpu. Only processes that hold `/dev/dri/*` are scanned. NVIDIA proprietary fdinfo is partial, so it is shown as "—" when the driver does not report it. | Every 4 s. |
| App membership | `/proc/<pid>/cgroup` and `app.slice` cgroups. | Every 2 s. |

## 6. Phases (same roster loop each: handoff → WO2 → build/test/probe → agy tests → Reviewer2 → commit)

| Phase | Content |
|---|---|
| A | **Data and actions** (`SysMonitor.Linux`, no UI): `ProcessStats` (CPU deltas, RSS, user, GPU per process), `AppResolver` (cgroups → `.desktop` → instances → processes), sortable and searchable grid models, close/restart actions, swap and RAM-properties readers, `--probe-ui` dump of the top apps and processes. Tests. |
| B | **Shell**: main window, sidebar (nav, settings expander, module items with sparklines), right-frame host, Overall page and module pages moved from Overall/Full, Widget ↔ main switching, config migration, old Overall sidebar and Full rail removed. Battery module (R-028 phase B lands here instead of the old Full tab). |
| C | **Apps and Processes pages**: our own lightweight grid (sortable header row with keyed, virtualised rows), with no `DataGrid` package. Services tab moved in. |
| D | **Polish**: keyboard navigation, docs (README, ARCHITECTURE, TESTING), package, hands-on test with the user. |

R-028 phase A (battery readers) finishes first and is unaffected.

## 7. Risks and limits

- NVIDIA per-process GPU use depends on the driver; it may show "—".
- An app not started through the desktop session (e.g. from a plain
  shell) has no `app-*` scope. It appears under Processes but not Apps,
  unless it falls in the terminal's scope. This matches Resources.
- Restarting an app restores its window but not its state, such as open
  documents or tabs.
- 300 processes read every 2 s is cheap. The fdinfo scan is the expensive
  part, so it runs every 4 s and only on processes that hold DRM fds.
