# Handoff — R-027 phase 2b: Full view, charts, processes and services

From: Coding Owner (Claude Code) · To: Writing Owner 2 (opencode, `opencode-go/deepseek-v4.1-flash`)
Branch: `linux-native` at `3936759` · Date: 2026-11-20
Requirement: `Requirements.md` R-027 · Reference: `Docs/WPF-UI-SPEC-4.3.0.md` **§5, §5a, §5b, §5c**

## Objective

Replace the Full placeholder with the real Full view of Windows 4.3.0:
- the tab rail;
- the detail pane with facts, big and small chart cards;
- the 72-point charts drawn exactly as spec §5b says;
- on the RAM tab, the process list and the service list (spec §5c).

Same split as phase 2a. Testable logic (tab building and reconciliation,
chart geometry maths, process grouping, filters and caps, protection rules,
service parsing and sorting, kill flow state) goes in
`SysMonitor.Linux/Presentation/` (and `SysMonitor.Linux/Sensors/` for
readers). Drawing and wiring go in `SysMonitor.Linux.App/`.

## Linux decisions (override the spec)

| Spec item | Linux |
|---|---|
| Tabs `disk{N}` | One tab per **physical disk** (`DriveDetail.Disk`), ordered by disk name. The title is "Disk N" (N = order, from 0) and the hardware line is the model. Mounts with no physical disk go to `diskvirtual`; mounts with `IsNetwork` go to `disknetwork`. Drive cards are keyed by mount point. |
| Tab `gpu` | **One tab per GPU** in `Snapshot.Gpus`, keys `gpu0`, `gpu1`, … Facts: Driver, `dedicated` "used / total GB" when the total is known, temperature when known. Small cards per engine. NVIDIA has no engines: no small cards. |
| CPU facts | Also show the base clock and the per-core temperatures in the small cards' value ("12% · 51°C") when known. |
| RAM hardware line / module facts | Modules are unknown without root: empty hardware line and no module facts. Do not invent. |
| Process sampling | `LinuxSampler.Sample(includeProcesses: true)` only while the Full view shows the RAM tab, at most every **3 s**. Keep the per-group 72-point history only while that is on. |
| Process kill | Send `SIGTERM` (P/Invoke `kill` from libc, no package). Before sending, re-check that `/proc/<pid>` still has the same name. Wait up to 3 s. If it is still alive, ask `mem_force_kill` and, on Yes, send `SIGKILL`. `EPERM` → `mem_failed` with the error text. No pkexec or sudo. |
| Protected processes | PID 1, PID 2 and kernel threads (empty `/proc/<pid>/cmdline` with parent PID 2, or PID 2 itself), our own PID, processes of another user (UID ≠ ours, from `/proc/<pid>/status` `Uid:`), unreadable entries, and these session-critical names: `systemd`, `init`, `kwin_wayland`, `kwin_x11`, `plasmashell`, `ksmserver`, `kded6`, `Xwayland`, `Xorg`, `gnome-shell`, `gnome-session-binary`, `mutter`, `dbus-daemon`, `dbus-broker`, `pipewire`, `wireplumber`, `pulseaudio`, `sddm`, `gdm`, `polkitd`, `systemd-logind`. |
| Services | systemd. System units: `systemctl list-units --type=service --all --output=json --no-pager`. User units: the same with `--user`. Main PID and unit-file state come from one `systemctl show <units…> -p Id,MainPID,UnitFileState,ActiveState,SubState,Description` call per scope (batch the units; no call per unit). Refresh at most every **10 s**, only while the RAM tab is shown. |
| Service actions | **User units only** (`systemctl --user start/stop/restart/enable/disable`), each after its confirm dialog (spec §5c). System units are read-only: no action buttons. A tooltip `mem_system_readonly` says why: changing them needs administrator rights, which this app never asks for. Startup menu for user units: `mem_auto` = enabled, `mem_manual` = disabled-but-startable, `mem_disabled` = masked is **not** offered. Show only Automatic / Manual. |
| New strings | `mem_force_kill`, `mem_system_readonly`, `mem_user_services`, `mem_system_services` (Thai + English). The service list shows two sub-headings: user services first. |
| Commands | Every `systemctl` call goes through `ICommandRunner` with an argument list. Unit names come from systemd, but pass them as arguments, **never through a shell**. Kill uses the syscall, not `kill(1)`. |

Charts:
- Implement spec §5b exactly: the 72-point ring, the 6×4 grid, the plot fill, the solid mixed fill, the 1.2 px angular stroke, the frame drawn last, and the scale rules.
- Draw in a custom Avalonia control with `Render(DrawingContext)`. Do not use a chart package.
- History is pushed on every fresh snapshot whatever the view (already in phase 2a).

Tab rail, header, the nav pair, tab selection and reconciliation follow
spec §5/§5a. `full_tab` is remembered in the config. Disk tab double-click
and right-click use the phase 2a disk tools.

## Acceptance criteria

1. `~/.dotnet/dotnet build SysMonitor.Linux.App`: 0 errors, 0 warnings.
   `dotnet test SysMonitor.Linux.Tests`: still passes. Do not edit that
   project; the Test Author adds the tests afterwards.
2. `--probe-ui` also prints the Full tabs. For each tab it prints key,
   title, rail detail, summary and hardware, the facts, and every card's
   title/value/detail/unit/ceiling. It also prints the top 10 process
   groups with their protection flag, and the service counts per scope
   with the first 5 of each. On this machine the expected tabs are cpu,
   ram, one disk tab for the NVMe (`/` and `/boot/efi`), one for the USB
   disk, gpu0 (Intel), gpu1 (NVIDIA), and two network tabs. Paste the
   output.
3. No launching the app and no screenshots. The Coding Owner asks the
   user before showing a window.
4. Never kill a process or start or stop a service while testing. Only
   the probe's read-only listing is allowed.

## Scope

`SysMonitor.Linux/Presentation/**`, `SysMonitor.Linux/Sensors/**` (the
service reader; process UID, PPID and cmdline-empty fields in the process
reader), `SysMonitor.Core` (only add `ProcessEntry.Uid`, `ParentPid`,
`KernelThread` if needed), `SysMonitor.Linux.App/**`, and
`StringData` for the new keys.

## Constraints

- No new packages. No root. No load tests.
- Do not touch `Requirements.md`, `CLAUDE.md`, `AGENTS.md`, `Docs/AI_WORKFLOW.md`, `Docs/WPF-UI-SPEC-4.3.0.md`, `SysMonitor.Linux.Tests/`.
- Do not commit, push or create branches.
- `export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH`

## Report back

```
## Summary
## Changed        (one line per file)
## Verification   (build; tests; --probe-ui Full output)
## Spec deviations and "Unclear" choices
## Open questions / risks
```

## Amendment (Coding Owner, after the first report)

Accepted as reported: deviations 2 (`ServiceEntry.UserUnit`), 3 (process
cadence), 4 (GPU tab title = GPU name), 6 (empty virtual/network tabs
omitted), 7, 8, 12, 13, 14, 15, 16, and 1 (`LinuxExtras` table — the
Test Author will update the count test).

Changes to apply:

1. **Disk tab order and keys.** Order: the disk that holds `/` first,
   then other internal disks, then removable/USB, each group by kernel
   name. Keys are stable: `disk:<kernel name>` (e.g. `disk:nvme0n1`), so a
   remembered `full_tab` keeps pointing at the same disk after a replug.
   Titles stay "Disk N" by position.
2. **Short GPU names.** `GpuReader` builds `Gpu.Name` as vendor short
   name (`Intel`, `NVIDIA`, `AMD`, else the pci.ids vendor) + the text in
   the last `[...]` of the pci.ids device name when present, else the
   device name. Here: "NVIDIA GeForce MX250", "Intel Iris Plus Graphics
   G1 (Ice Lake)". Everything that shows a GPU name follows.
3. **No hard-coded UI text.** Add keys to `LinuxExtras` (Thai + English):
   `yes`, `no`, `mem_pid_changed` ({0} pid, {1} new name),
   `mem_systemctl_failed` ({0} stderr text). Use them in the confirm
   dialog, the kill flow and service failures.
4. **Service failure text.** Extend `ICommandRunner` with a method that
   returns exit code, stdout and stderr (keep `Run` as is for existing
   callers); service actions show the first stderr line in
   `mem_systemctl_failed`.
5. **Service rows are synced in place** by unit name (like process
   groups), so an open startup menu or a hovered button survives a
   redraw.

## Amendment 2 (Coding Owner, after Reviewer2 round 1)

All four findings accepted.

1. **Process identity (PID reuse).** `ProcessEntry` gets `StartTime`
   (field 22 of `/proc/<pid>/stat`, clock ticks since boot; parse after
   the last `)` because `comm` may contain spaces and parentheses). A kill
   target is the triple (PID, StartTime, Name) captured from the row.
   At action time:
   - `pidfd_open(pid, 0)` (syscall 434 via libc `syscall`; no package);
   - then re-read `/proc/<pid>/stat` + `status`; abort with
     `mem_pid_changed` unless StartTime **and** Name still match;
   - then signal through `pidfd_send_signal(pidfd, sig, NULL, 0)`
     (syscall 424) and close the fd. A pidfd pins the process, so the
     check and the signal hit the same process.
   - If `pidfd_open` returns `ENOSYS`, fall back to: re-check, then
     `kill()`, and say so in a code comment. The same applies to the
     SIGKILL after the 3 s wait: re-verify through the same pidfd.
2. **Protection is re-evaluated at action time** from the fresh read in
   step 1 (UID, kernel thread, names, own PID, PID 1/2), not from the
   row. A protected result aborts with `mem_protected`.
3. **Unknown owner is protected.** `Uid == null` → protected (the same
   as unreadable).
4. **Option terminator.** Every `systemctl` argv puts `--` before the
   unit names (`systemctl --user stop -- <unit>`,
   `systemctl show -p … -- <units…>`). Also reject a unit name that is
   empty or contains `/` or a NUL.

## Amendment 3 (Coding Owner, after Reviewer2 round 2)

1. **No identity, no signal.** A StartTime that is missing, unparsed or
   `0` (on the row or at action time) aborts with `mem_pid_changed`;
   make `StartTime` nullable (`ulong?`) so "unknown" cannot equal a real
   value.
2. **One pidfd from SIGTERM to SIGKILL.** `Terminate` keeps its pinned
   handle when it returns `NeedsForce`. It holds it in the pending-kill
   state, and `Force` uses that same handle: no second `Pin`. The handle is
   closed exactly once, on Force, on the user declining, on the process
   exiting, on the view closing, or when a newer kill replaces the pending
   one. The ENOSYS fallback keeps its re-check before `kill()`.

## Amendment 4 (Coding Owner, after Reviewer2 round 3)

Replace the shared pending-handle field with an owned token:

1. `Terminate` returns a result. When the result is `NeedsForce`, it
   carries a `PendingKill` object that **owns** the pinned handle and the
   target. The flow keeps no other copy of the handle.
2. `Force(PendingKill)` signals only through that token's handle. There
   is **no** path that pins again for a force (remove `FirstPinForce` or
   its equivalent). If the token is already disposed (view closed,
   replaced, declined, exited), `Force` does nothing and returns
   `Cancelled`. It never pins and never signals.
3. `PendingKill.Dispose` is idempotent and thread-safe (`Interlocked`
   flag). `KillFlow` tracks its live tokens under a lock:
   - a new `Terminate` for the same PID disposes the older token;
   - `CancelAll` (view close) disposes every token;
   - a token is removed from tracking when it is disposed.
4. The UI holds the token from the confirm dialog it opened, so an older
   dialog can only act on its own token.

## Amendment 5 (Coding Owner, after Reviewer2 round 4): exact concurrency design

One `private readonly object _gate` in `KillFlow`. A
`Dictionary<int, PendingKill> _pending` (by PID), and a `bool _closed`
flag set by `CancelAll`. All of the following state changes happen only
inside `lock (_gate)`.

- **ShellModel** creates its `KillFlow` once, in the constructor, as a
  `readonly` field. No lazy initialisation.
- **Terminate(target)**:
  1. Pin, re-check identity and protection, and send SIGTERM through the
     pinned handle. All of this happens outside the lock, on the caller's
     own handle, which nothing else can see yet.
  2. `lock`: if `_closed`, dispose the handle and return `Cancelled`.
     Otherwise create the token. If `_pending` already holds a token for
     that PID, dispose the old token. Store the new token. Doing both in
     one lock section means no interleaving can lose a token.
  3. Wait up to 3 s for exit, outside the lock. Then, under `lock`: if the
     token was disposed meanwhile, return `Cancelled`. If the process
     exited, remove and dispose the token and return `Exited`. Otherwise
     return `NeedsForce(token)`.
- **Force(token)**, entirely under `lock`:
  1. If `_closed`, or the token is disposed, or `_pending[pid]` is not
     this token: return `Cancelled`.
  2. Send SIGKILL through the token's handle. This is a fast syscall and
     is allowed under the lock.
  3. Remove and dispose the token.
- **Decline(token)**, under `lock`: remove the token if it is current,
  and dispose it.
- **CancelAll()**, under `lock`: set `_closed`, dispose every token, and
  clear the dictionary. Opening the view again creates a new `KillFlow`,
  or calls `Reopen()` to clear `_closed`.
- **PendingKill.Dispose** stays idempotent (an `Interlocked` flag).

## Amendment 5a (Coding Owner, off-role edit)

`Terminate` checks `_closed` under the gate **before** pinning, so a kill
started after `CancelAll` sends no signal. `Publish` still re-checks the
flag for a close that lands during step 1. This was found by the Round 5
test `CancelAll_WhileForceAboutToRun_WhenForceAcquiresGateFirst_…`. The
Coding Owner made the four-line fix in `KillFlow.Terminate`.

## Amendment 6 (Coding Owner, after Reviewer2 round 5)

1. **Terminate step 1 runs under `_gate`.** Pin, the `/proc` re-check
   and SIGTERM are all fast and non-blocking, like the SIGKILL in
   `Force`. Run them in the same `lock (_gate)` section as the `_closed`
   check and `Publish`: check `_closed` → pin → check → SIGTERM → store
   the token. Only the ≤3 s exit wait runs outside the lock. The same
   rule applies to the ENOSYS fallback: check `_closed`, re-check, then
   `kill()`, all under the gate. Invariant: once `CancelAll` has returned,
   no signal of any kind is sent.
2. **Test hook.** Add an optional `Action? onCancelAllRequested`
   (constructor parameter or internal property), called by `CancelAll`
   **before** it takes the gate, so a test can prove `CancelAll` started
   while `Force` or `Terminate` held the gate.

## Amendment 6c (test hook)

Add an optional `Action<int>? onTerminateRequested` (pid), set the same
way as `onCancelAllRequested`. `Terminate` calls it **before** taking
`_gate`, and after the identity pre-check. Behaviour is otherwise
unchanged.
