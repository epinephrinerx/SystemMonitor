# Handoff — R-027 phase 1b: Linux sensor layer + probe

From: Coding Owner (Claude Code) · To: Writing Owner 2 (opencode, `opencode-go/deepseek-v4.1-flash`)
Branch: `linux-native` at `a0fc668` · Date: 2026-11-19
Requirement: `Requirements.md` R-027 (Linux-only rebuild of 4.3.0)

## Objective

Build `SysMonitor.Linux`, a package-free library that reads `/proc`, `/sys`
and two user-level commands and fills the shared `SysMonitor.Core` model
(`SysMonitor.Model.Snapshot`) once per tick. Plus `SysMonitor.Linux.Probe`,
a console program that prints what the sampler sees, so the data layer can
be checked without a window. No UI in this phase.

The old Linux build (`git show linux-legacy:SysMonitor.Linux/...`) has
working parsers for `/proc/stat`, `/proc/meminfo`, `/proc/diskstats`,
`/proc/self/mountinfo`, `/proc/net/dev`, hwmon and block devices. Reuse them
where they fit — but they target an older model, and they have known bugs
(see "Known defects to fix").

## Target machine (this machine — verify on it)

Ubuntu, Wayland, i5-1035G1 (4 cores / 8 threads), 7.4 GB RAM, one NVMe SSD
(`nvme0n1`, mounts `/` and `/boot/efi`), Wi-Fi `wlp0s20f3`, two GPUs:
Intel Iris Plus (`i915`, `/sys/class/drm/card1`, `boot_vga=1`) and NVIDIA
MX250 (`nvidia`, card2, `nvidia-smi` present). hwmon: `coretemp`, `nvme`,
`acpitz`, `BAT0`, `iwlwifi_1`, ... . No root.

## Projects

1. `SysMonitor.Linux/SysMonitor.Linux.csproj` — `net10.0` (only the .NET 10
   runtime is installed), Nullable + ImplicitUsings on, `LangVersion latest`,
   `RootNamespace SysMonitor.Linux`, **no PackageReference**, one
   ProjectReference to `..\SysMonitor.Core\SysMonitor.Core.csproj`.
2. `SysMonitor.Linux.Probe/SysMonitor.Linux.Probe.csproj` — `net10.0` Exe,
   references `SysMonitor.Linux`. No packages.
3. Retarget `SysMonitor.Core` to `net10.0` (one line) so every project uses
   the installed runtime.

## Design rules

- Every parser is a pure function over text, or over a **root directory**
  (`string root`, default `"/"`), so tests can feed a fake tree from a temp
  folder. No reader may hard-code `/proc` or `/sys` without the root prefix.
- External commands go through one interface
  `ICommandRunner { string? Run(string file, string args, int timeoutMs); }`
  (returns stdout, or null on missing binary / non-zero exit / timeout) with a
  real implementation using `System.Diagnostics.Process`. Tests pass a fake.
- Time goes through a `Func<long>` millisecond clock (default
  `Environment.TickCount64`) so rates are testable.
- A value the user cannot read without root is **null / 0 / empty**, never a
  guess. Use the model's existing `Estimated` flags when a value is derived.
- One failing source (missing file, permission denied, parse error) must not
  break the snapshot: catch per source, leave its fields empty.
- No `Thread.Sleep` inside the library. No load generation in tests.

## Model changes allowed in `SysMonitor.Core` (Linux-only branch)

- `Gpu.Temp`: change from `=> null` to `{ get; init; }` (`int?`).
- `Snapshot`: add `IReadOnlyList<Gpu> Gpus` (sealed like the other lists).
  `Snapshot.Gpu` stays and is the primary GPU (the one with `boot_vga=1`,
  else the first).
- `Disk.Title`: Linux has mount points, not letters. `Letter` holds the mount
  point (`/`, `/boot/efi`); make `Title` = `Letter` plus ` (Label)` when a
  label exists (no colon).
- Rewrite the Windows wording in the doc comments of the types you touch.
  Nothing else in Core changes.

## What the sampler must fill

`LinuxSampler` (constructor: root, `ICommandRunner`, clock) with
`Snapshot Sample(bool includeProcesses)`. The first call returns
`Ready = false` (rates need two readings); later calls `Ready = true`.

| Field | Source |
|---|---|
| `CpuTotal`, `Cores[i].Usage` | `/proc/stat` deltas (`cpu`, `cpuN`) |
| `CpuInfo` | `/proc/cpuinfo` (model name, vendor_id, physical id, core id, `vmx`/`svm` flag → `Virtualization`); `BaseMhz` from `/sys/devices/system/cpu/cpu0/cpufreq/base_frequency` (kHz), else `cpuinfo_max_freq`, else 0; `L2Kb`/`L3Kb` = sum of the **distinct** caches in `/sys/devices/system/cpu/cpu*/cache/index*` (dedupe by `level`+`shared_cpu_list`; type Unified/Data for L2) |
| `CpuTemp` | hwmon `coretemp` label `Package id 0`; AMD `k10temp`/`zenpower` `Tctl`/`Tdie`; else `thermal_zone*` type `x86_pkg_temp`, else `acpitz` with `CpuTempEstimated = true` |
| `Cores[i].Temp` | `coretemp` `Core N` labels mapped to logical CPUs through `/sys/devices/system/cpu/cpuX/topology/core_id` (both hyper-threads of a core get that core's temperature) |
| `Ram` | `/proc/meminfo` `MemTotal`, `MemAvailable` (used = total − available) |
| `Modules`, `MemorySlots` | DMI type 17 needs root → empty / 0 |
| `Disks` | one per real mounted filesystem from `mountinfo` (skip `tmpfs`, `squashfs`, `overlay`, `proc`, `sysfs`, snaps, `/run`, etc.; keep `ext4`, `btrfs`, `xfs`, `vfat`, `exfat`, `ntfs*`, `f2fs`, network `nfs`/`cifs` with `IsNetwork`). Space via `statvfs` (`DriveInfo` is fine). **I/O from the partition's own `/proc/diskstats` line** (sectors × 512) via `IoRate`. `Media`: `SSD` when `queue/rotational` = 0 or NVMe, `HDD` when 1, `Removable` when `removable` = 1. `Bus`: `NVMe` / `SATA` / `USB` / `Virtual` / `Unknown` from the sysfs device path. `Temp`: hwmon under the disk's device (`nvme` → `temp1_input`; `drivetemp` for SATA when loaded), `TempSource.Storage`. `Detail` (`DriveDetail` + `PhysicalDisk`): model, serial, firmware (`firmware_rev`), size, partition number / start / size, partition count, file system and label (`/dev/disk/by-label` symlinks, decode `\x20`), `IsBoot` for `/`. |
| `Adapters` | `/proc/net/dev`, skipping `lo` and interfaces under `/sys/devices/virtual/net`; `Wireless` = `/sys/class/net/X/wireless` exists; wired `SpeedMbps` from `/sys/class/net/X/speed` (ignore errors / `-1`); Wi-Fi `SpeedMbps` = `tx bitrate` from `iw dev X link` (re-read at most every 5 s); `Description` = driver name from `device/uevent` plus the vendor/device name from `/usr/share/misc/pci.ids` or `/usr/share/hwdata/pci.ids` when present; `TotalReceived/Sent` = counters; rates via deltas |
| `Gpus`, `Gpu` | each `/sys/class/drm/card[0-9]*` (skip connectors like `card1-eDP-1`); `Name` from pci.ids by vendor/device id; `Driver` from the `driver` link. **Usage**: `amdgpu` → `device/gpu_busy_percent`; `i915`/`xe` → DRM fdinfo: sum `drm-engine-<name>` ns over `/proc/*/fdinfo/*` entries whose `drm-pdev` matches the card, counting each `drm-client-id` once, delta / elapsed → `Engines` per engine name, `Usage` = busiest engine; `nvidia` → **only if `device/power/runtime_status` is `active`**, run `nvidia-smi --query-gpu=utilization.gpu,memory.total,memory.used,temperature.gpu --format=csv,noheader,nounits` at most every 2 s (`Temp`, `DedicatedGb`); when suspended report `Present = true`, usage 0, and do **not** call nvidia-smi (it would wake the GPU). `amdgpu` `mem_info_vram_total` → `DedicatedGb`; `hwmon` under the card → `Temp`. |
| `Processes` (only when `includeProcesses`) | `/proc/<pid>/status` `Name` (prefer the basename of `/proc/<pid>/exe` link target when readable, because `Name` is cut at 15 chars), `VmRSS` → `WorkingSet` (bytes), `RssAnon` → `PrivateBytes`; unreadable → `Readable = false`; `MemoryStamp` = clock at read |
| `Services` | empty in this phase (phase 3) |

## Probe

`SysMonitor.Linux.Probe` takes two samples 1 s apart and prints a readable
text report of every field above (one block per section, `N/A` for
null/empty). Optional `--json` prints the snapshot as JSON instead. Exit 0.

## Known defects to fix (from `linux-legacy`)

- Two partitions of one disk showed identical I/O (the disk's counters were
  used for each partition). Use each partition's own line.
- Wi-Fi speed read 0 (`/sys/class/net/wlp0s20f3/speed` gives EINVAL).
- No per-core temperature, no NVMe temperature, no GPU, no processes.

## Acceptance criteria

1. `~/.dotnet/dotnet build SysMonitor.Linux.Probe` — 0 errors, 0 warnings.
2. `~/.dotnet/dotnet run --project SysMonitor.Linux.Probe` on this machine
   prints: CPU name `Intel(R) Core(TM) i5-1035G1`, 4 cores / 8 logical, a
   package temperature and 8 per-core temperatures; RAM ≈ 7.4 GB; disks `/`
   and `/boot/efi` as NVMe SSD with a disk temperature and **different**
   I/O lines; `wlp0s20f3` as wireless with a non-zero speed; **two** GPUs
   (Intel and NVIDIA) with names. Paste the full output in the report.
3. Every parser is reachable with a fake root (the Test Author will write the
   tests in `SysMonitor.Linux.Tests` after you; keep methods `internal` or
   `public` with `InternalsVisibleTo("SysMonitor.Linux.Tests")`).
4. `Docs/ARCHITECTURE.md` §5 table: add rows for `SysMonitor.Linux` and
   `SysMonitor.Linux.Probe`.

## Scope

New: `SysMonitor.Linux/**`, `SysMonitor.Linux.Probe/**`. Edit:
`SysMonitor.Core` (only the model changes above and the target framework),
`Docs/ARCHITECTURE.md` §5. The folders `SysMonitor.Linux/` and
`SysMonitor.Linux.App/` already hold ignored `bin/obj` leftovers; delete
those leftovers first.

## Constraints

- No packages, no UI, no root, no `sudo`/`pkexec`.
- Do not touch `Requirements.md`, `CLAUDE.md`, `AGENTS.md`, `Docs/AI_WORKFLOW.md`.
- Do not commit, push or create branches. Do not open windows. No load tests.
- `export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH`

## Report back

```
## Summary
## Changed        (one line per file)
## Verification   (build result; full probe output)
## Gaps           (every field left empty on this machine, and why)
## Open questions / risks
```
