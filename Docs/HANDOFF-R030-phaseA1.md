# Handoff — R-030 phase A1: per-process stats, apps, actions, swap, RAM properties

From: Coding Owner (Claude Code) · To: Writing Owner 2 (opencode, `opencode-go/deepseek-v4.1-flash`)
Branch: `linux-native` at `9573e57` (clean tree). Date: 2026-11-21.
Requirement: `Requirements.md` **R-030**; plan: `Docs/PLAN-R030-main-window.md` §2 (Memory), §3, §4, §5.

**Scope: data and actions only.** No UI. Do **not** touch `SysMonitor.Linux.App/`
and do not edit any file under `SysMonitor.Linux.Tests/` (the Test Author does
that). Phase A2 (§2b module property readers: CPU max freq, GPU clocks, disk
activity, network IPs…) is a separate handoff — do not start it.

## 1. Per-process CPU and memory — `SysMonitor.Linux/Sensors/ProcessStats.cs`

- `sealed class ProcessStats` with an instance cache, so CPU % is a delta between
  two calls. Constructor takes `string root = "/"`, `int logicalCpus`, and
  `long clockTicks = 100` (USER_HZ; keep it a parameter for tests).
- `IReadOnlyList<ProcessRow> Read(long stampMs)`:
  - Enumerates `/proc/<pid>` with the existing helpers in `ProcessReader`
    (`StatusName`, `ExeName`, `ParseStartTime`, `UidField`, `IsKernelThread`).
    Reuse them; do not duplicate parsing.
  - CPU: `utime` (field 14) + `stime` (field 15) of `/proc/<pid>/stat`, parsed
    after the last `)` like `ParseStartTime`. `CpuPercent = Δticks / (Δt_s ×
    clockTicks × logicalCpus) × 100`, clamped 0–100, rounded to 1 decimal.
    First sight of a (pid, starttime) → `CpuPercent = null` (unknown, not 0).
    The cache key is **(pid, starttime)** so a recycled PID never inherits a delta.
    Drop cache entries for processes that are gone.
  - Memory: `VmRSS` (bytes). Missing (kernel thread / another user) → 0 with
    `Readable` as `ProcessReader` sets it.
  - Command line: `/proc/<pid>/cmdline`, NULs → spaces, trimmed, max 4096 chars.
  - User: UID → name from `<root>/etc/passwd`, parsed once and cached; unknown
    UID → the number as text.
  - `CgroupPath`: the `0::` line of `/proc/<pid>/cgroup` (unified hierarchy),
    or empty.
- `sealed record ProcessRow` in `SysMonitor.Core/Model/Processes.cs`
  (platform-neutral, Windows R-031 will reuse it): `Pid`, `Name`, `StartTime`,
  `Uid?`, `User`, `ParentPid`, `KernelThread`, `Readable`, `CpuPercent?`,
  `MemoryBytes`, `GpuPercent?`, `GpuMemoryBytes?`, `Command`, `CgroupPath`,
  `ServiceUnit?` (set in §4).

## 2. Per-process GPU — extend `DrmFdinfo` / new `ProcessGpu.cs`

- Scan only processes that have a `/proc/<pid>/fd/*` link to `/dev/dri/*`;
  read their `fdinfo` with the existing `DrmFdinfo.Parse`.
- Add parsing of `drm-memory-<region>:` (and `drm-total-<region>:` /
  `drm-resident-<region>:` from newer kernels) to `DrmClient` as a byte total;
  values carry units `KiB`/`MiB`/none. Keep the existing engine parsing as is.
- De-duplicate clients by (`drm-pdev`, `drm-client-id`) per process, exactly like
  `GpuReader` does (one client may be open on many fds).
- `GpuPercent` = Σ Δengine-ns of the busiest engine of each client ÷ Δt-ns × 100,
  clamped 0–100; first sight → null. `GpuMemoryBytes` = sum of the regions, or
  null if no `drm-memory`/`drm-total` lines.
- Cadence: every **4 s** (cache between; `ProcessStats.Read` merges the last GPU
  values). NVIDIA proprietary fdinfo with no engines → nulls (UI shows "—").

## 3. Apps — `SysMonitor.Linux/Sensors/AppResolver.cs`

Source: `<root>/sys/fs/cgroup/user.slice/user-<uid>.slice/user@<uid>.service/app.slice`
for the **current** UID only. Real examples on this machine:

```
app-org.kde.konsole-153839.scope            (scope: id org.kde.konsole, instance 153839)
app-org.kde.konsole-153839.scope/tab(153854).scope   (nested: belongs to the parent instance)
app-dolphin@132bf9f4….service               (service: id dolphin, instance 132bf9f4…)
app-org.kde.dolphin@e9469414….service
app-google\x2dchrome@6d1540ed….service      (systemd escaping: \x2d → '-')
app-com.google.Chrome-449813.scope
app-dropbox@autostart.service               (autostart marker)
```

- Parse each top-level `app-*.scope` / `app-*.service` name: strip `app-`, the
  suffix (`.scope`/`.service`), then the instance (`@<x>` for services, the
  trailing `-<digits>` for scopes). Unescape `\xNN`. Also strip an optional
  launcher prefix `<launcher>-` only if it is one of `gnome`, `kde`, `flatpak`
  **and** the remainder resolves to a desktop file (systemd allows
  `app-<launcher>-<id>-<n>.scope`).
- Processes of an instance: every PID in `cgroup.procs` of the unit directory and
  all sub-directories, recursively.
- Desktop ID → `.desktop` file, searched in `$XDG_DATA_HOME/applications`
  (default `~/.local/share/applications`) then each `$XDG_DATA_DIRS` entry
  (default `/usr/local/share:/usr/share`) plus
  `/var/lib/flatpak/exports/share` and `~/.local/share/flatpak/exports/share`
  `/applications`. Try `<id>.desktop`; if not found, try a file whose name ends
  with `.<id>.desktop` (so `dolphin` finds `org.kde.dolphin.desktop`) — exactly
  one match, else unresolved. Make the search dirs constructor parameters so
  tests use a fake root.
- Read from the `[Desktop Entry]` group only: `Name` (prefer `Name[<lang>]` when
  the app language is `th`, else plain `Name`), `Icon`, `Exec`, `NoDisplay`,
  `Hidden`. Unresolved → name = the desktop ID, no icon, no Exec.
- Group instances by **resolved .desktop path** (so `dolphin@…` and
  `org.kde.dolphin@…` that resolve to the same file become one app); unresolved
  ones group by ID.
- Models (`SysMonitor.Core/Model/Processes.cs`):
  - `AppInstance(string Unit, IReadOnlyList<ProcessRow> Processes)` with summed
    CPU/memory/GPU (CPU/GPU null only if **every** member is null).
  - `AppEntry(string Id, string Name, string? Icon, string? DesktopFile,
    string? Exec, bool Autostart, IReadOnlyList<AppInstance> Instances)` with the
    same sums. `Autostart` = any instance named `@autostart`.
- An instance with no live processes is dropped; an app with no instances is
  dropped. Processes not under `app.slice` are not apps.

## 4. Services link

- For the Processes page, mark a row's `ServiceUnit` when its PID is the
  `MainPID` of a **user** unit. Reuse `ServiceReader` (it already batches
  `systemctl --user show … MainPID`). Do not add a new systemctl call per row.
  Cadence: the existing services cadence, not every 2 s.

## 5. Grid models — `SysMonitor.Linux/Presentation/ProcessGrid.cs`

Pure functions, no Avalonia:
- `enum GridColumn { Name, Pid, User, Cpu, Memory, Gpu, GpuMemory, Instances }`.
- `Sort<T>(…, GridColumn, bool descending)` for `ProcessRow` and `AppEntry`.
  Null CPU/GPU sorts as lowest in both directions' "unknown last" rule: nulls
  always go last. Ties break by name (ordinal ignore case), then PID.
- `Filter(rows, string query)`: case-insensitive substring of name, command, user
  or PID text. Empty query → all.
- Config keys (add to `AppConfig`, with defaults and round-trip like the existing
  keys): `apps_sort` (`memory`), `apps_sort_desc` (`true`), `procs_sort`
  (`memory`), `procs_sort_desc` (`true`). Unknown values fall back to the default.

## 6. Actions — `SysMonitor.Linux/Presentation/AppActions.cs`

All go through the existing `KillFlow` (identity + `ProcessProtection` re-check);
never call `kill` directly.
- `CloseInstance(AppInstance)` / `CloseApp(AppEntry)` → `KillFlow.TerminateAll`
  over the members as `ProcessTarget(pid, starttime, name)`; return the stubborn
  tokens for the force prompt, unchanged from today.
- `RestartApp(AppEntry)`:
  1. disabled (return a failed result) when `Exec` is null or cannot be parsed;
  2. close all instances as above; wait up to **5 s** (poll 100 ms, injectable
     clock/sleep like `KillFlow`) for every member to be gone; if some remain,
     return `NeedsForce`-style result and do **not** launch;
  3. launch once: parse `Exec` into argv with a **new** multi-argument parser
     `DesktopEntry.ParseExec(string)` (Desktop Entry spec: quoting rules,
     string-value unescape, then remove field codes `%f %F %u %U %i %c %k`,
     `%%` → `%`; reject `%d %D %n %N %v %m` silently by removing them). Return
     null for unbalanced quotes or an empty argv.
     Start with `ProcessStartInfo` + `ArgumentList`, `UseShellExecute = false`,
     no shell, detached (do not wait), behind an injectable `Func<IReadOnlyList<string>, string?>`
     launcher so tests never start a process.
- `RestartService(ProcessRow)` → only when `ServiceUnit` is set: reuse
  `ServiceControl` with `restart` (`systemctl --user restart -- <unit>`).
- New strings (Thai + English) in `StringData`: `app_close`, `app_restart`,
  `app_close_confirm`, `app_restart_confirm`, `app_restart_unavailable`,
  `app_restart_still_running`, `proc_restart_service`, `col_name`, `col_pid`,
  `col_user`, `col_cpu`, `col_memory`, `col_gpu`, `col_gpu_memory`,
  `col_instances`, `col_command`, `apps_autostart`. Keep the existing key style.

## 7. Swap — extend `MemInfo` and add `SwapsReader`

- `Ram` (Core) gains `SwapTotalBytes`, `SwapUsedBytes` (`SwapTotal − SwapFree`,
  ≥ 0) and `SwapUsage` (int %, 0 when total is 0). Parse in `MemInfo.Parse`.
- `SwapsReader.Parse(string text)` for `/proc/swaps` → list of
  `SwapDevice(Name, Type, SizeBytes, UsedBytes, Priority)` (sizes are KiB).
  Header line skipped; `\040` in names unescaped to space.
- Put the swap devices on the snapshot (`Snapshot.SwapDevices`, empty by default),
  read every sample (one small file).

## 8. RAM properties — `SysMonitor.Linux/Sensors/MemoryModules.cs`

- Parse `<root>/run/udev/data/+dmi:id`; lines look like
  `E:MEMORY_DEVICE_0_SIZE=4294967296`. Only `E:` lines.
- Core model `MemoryModule(Slot, SizeBytes?, Type, TypeDetail, FormFactor,
  ConfiguredSpeedMts?, SpeedMts?, Manufacturer, Rank?, ConfiguredVoltage?)` and
  `MemoryLayout(int? SlotsUsed, int? SlotsTotal, long? MaxCapacityBytes,
  IReadOnlyList<MemoryModule> Modules)`.
  - Keys: `LOCATOR`, `SIZE`, `TYPE`, `TYPE_DETAIL`, `FORM_FACTOR`,
    `CONFIGURED_SPEED_MTS`, `SPEED_MTS`, `MANUFACTURER`, `RANK`,
    `CONFIGURED_VOLTAGE` (mV → store as volts double), plus
    `MEMORY_ARRAY_NUM_DEVICES`, `MEMORY_ARRAY_MAX_CAPACITY`.
  - `SlotsUsed` = devices with size > 0; `SlotsTotal` = `NUM_DEVICES`, else the
    device count. Devices with size 0 / "No Module Installed" are not listed.
  - "Unknown", "Not Specified", empty → null/empty. **Never** read or store
    `SERIAL_NUMBER`, `ASSET_TAG`, `PART_NUMBER` (skip them in the parser so
    they cannot leak).
  - Missing file / no keys → `MemoryLayout` with nulls and an empty list.
- Read once at sampler start and put on the snapshot (`Snapshot.MemoryLayout`).

## 9. Probe output

- `SysMonitor.Linux.Probe`: print `swap` (total/used/% and each device) and
  `ram modules` (slots, max, each module) in text and `--json`.
- New flag `--apps`: two samples 1 s apart, then the top 15 apps by memory with
  instance count, CPU %, memory, GPU %, and the top 15 processes with PID, user,
  CPU %, memory, GPU %. No command lines in the output (privacy), no serials.
- `SysMonitor.Linux.App/Probe.cs` is **not** in scope.

## Acceptance

1. `dotnet build SysMonitor.Linux.App -c Release` — 0 warnings, 0 errors.
2. `dotnet test SysMonitor.Linux.Tests` — the existing 459 still pass.
3. `dotnet run --project SysMonitor.Linux.Probe -- --apps` on this machine lists
   Konsole, VS Code, Dolphin, Chrome (merged across their units), and shows swap
   `/swapfile` ~8 GB and RAM modules 2 of 2, LPDDR4, Micron, 4 GB × 2, 2667 MT/s.
4. No serial/asset/part number anywhere; no process is signalled or started by
   the probe.

## Report back

Summary, files changed, commands run with results, deviations, risks. No commit.

## Amendment 1 (Coding Owner, 2026-11-21)

1. **Implausible voltage.** This machine's SMBIOS reports
   `CONFIGURED_VOLTAGE=1` (and MIN/MAX `2`), which the probe shows as
   "0.00 V". A configured voltage outside **500–5000 mV** is not a real reading:
   store `null` (the UI shows N/A). Do not guess a value.
2. Accepted deviations: Chrome grouping by `(Name, Exec)` as well as path;
   `ParseExec` in the existing `DesktopEntry`; `drm-resident-*` kept separate;
   service map passed in by the caller; fifth `StringData` table.

## Amendment 2 (Coding Owner, 2026-11-21) — Reviewer2 round 1

1. **Service restart identity.** `RestartService` must re-check at action
   time: read fresh `ProcessFacts` for the row's PID; require the same start
   time (non-null) and name as the row, `ProcessProtection` must not block it
   (except: a user unit's MainPID owned by our UID is allowed), and a fresh
   `systemctl --user show -p MainPID -- <unit>` must still return that PID.
   Any mismatch → failed result, no `systemctl restart`.
2. **GPU cache identity.** In `ProcessGpu` never cache or project values for a
   process whose start time is null; such a process gets null GPU values.
3. **No engines → unknown.** A client whose engine dictionaries are empty in
   either scan does not count as comparable; if no client of the process is
   comparable, `GpuPercent` is null (memory may still be reported).
4. **Probe `--apps`.** Take the two samples ≥ 4.5 s apart (or force a GPU scan
   on both reads) so GPU % can appear. Print a line saying the wait.
5. **Unicode unescape.** `\xNN` sequences are bytes: collect them and decode
   as UTF-8 (`app-caf\xc3\xa9-123.scope` → `café`). Invalid UTF-8 → leave the
   unit unresolved (use the raw name), never throw.
6. **Exec quoting.** Per the Desktop Entry spec a quote may only start at the
   beginning of an argument and must be followed by whitespace or the end;
   `/bin/echo "foo"bar` and `/bin/echo foo"bar"` → null (Exec rejected →
   restart disabled).
7. Not in scope: the pre-existing disk-serial suffix in the probe (existing,
   deliberate masking).

## Amendment 3 (Coding Owner, 2026-11-21) — Reviewer2 round 2

1. **Remove the own-UID exception** I wrote in Amendment 2 item 1. It let
   `RestartService` bypass every `ProcessProtection` rule (e.g. restart
   `plasmashell`). `RestartService` must apply `ProcessProtection.IsProtected`
   in full, with no exception: protected → failed, no `systemctl`.
2. **Invalid UTF-8 anywhere in the unit name** (ID **or** instance) → the whole
   unit is unresolved: no desktop lookup, name = raw unit ID text, no Exec,
   restart disabled. `app-cafe@bad\xff.service` with `cafe.desktop` present must
   not resolve to `cafe`.
