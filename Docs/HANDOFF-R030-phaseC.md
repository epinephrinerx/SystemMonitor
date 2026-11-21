# Handoff — R-030 phase C: Apps and Processes pages

From: Coding Owner · To: Writing Owner 2. Branch `linux-native`, on top of the
B2 hands-on fixes. Spec: `Docs/PLAN-R030-main-window.md` §3 (Apps), §4
(Processes), §5 (measurements), §6 row C. User (2026-11-21): "อย่าลืมเพิ่มหน้า apps นะครับ".

The data and action layer already exists and is tested (phase A1) — reuse it,
do not rewrite it:
- `SysMonitor.Linux/Sensors/ProcessStats.cs` (`Read(stampMs, serviceByPid)` →
  `ProcessRow`s with CPU/RSS/user/GPU), `ProcessGpu.cs`, `AppResolver.cs`
  (`Read(rows)` → `AppEntry` → `AppInstance` → `ProcessRow`).
  See `SysMonitor.Linux.Probe/Program.cs` `PrintApps` for how they are driven.
- `SysMonitor.Linux/Presentation/ProcessGrid.cs` (Sort/Filter/Key/ParseColumn),
  `AppActions.cs` (CloseInstance, CloseApp, RestartApp, RestartService; results
  `ActionResult` with `NeedsForce`), `KillFlow` via `ShellModel`.
- Config already has `apps_sort`, `apps_sort_desc`, `procs_sort`, `procs_sort_desc`.
- Existing confirm UI: `ProcessesView.Confirm` / `ConfirmWindow`, force prompt
  pattern at `ProcessesView.cs` ~464–490. Reuse these.

Work in two parts. **Do Part 1 only when asked for Part 1, Part 2 only when
asked for Part 2.**

## Part 1 — sampling + Apps page

1. **Sampling.** Add `IReadOnlyList<ProcessRow> ProcessRows` and
   `IReadOnlyList<AppEntry> Apps` to `Snapshot` (init, default empty).
   `LinuxSampler` owns one `ProcessStats` and one `AppResolver` and fills them
   only when asked (new flag), every 2 s (`MemoryCadence`-style constant
   `GridMs = 2000`); between reads keep the previous lists so the grid does not
   blank. Pass the service-unit-by-PID map from the existing service sample when
   one is available. `ShellWindow` asks for them only while the Apps or
   Processes page is visible (same conditions as `_memoryVisible`: not hidden
   to tray, visible, not minimised), and forces one read when a page is entered.
2. **Apps page** — new `SysMonitor.Linux.App/AppsView.cs`, replacing
   `MainView.RenderApps` placeholder (drop the `apps_placeholder` string use).
   - Top: heading "Apps" + count, a search box (filters by name via
     `ProcessGrid.Filter`).
   - Header row: Name · Instances · CPU · Memory · GPU · GPU memory. Click
     sorts (`ProcessGrid.Sort`), click again reverses; arrow ▲/▼ drawn with the
     existing icon set (not text glyphs missing from IBM Plex); saved to
     `apps_sort`/`apps_sort_desc`.
   - Rows: ▸ expander · icon (from the `.desktop` icon if the existing icon
     loader supports it, else a generic app icon) · name (+ "starts at login"
     marker for autostart services) · numbers right-aligned; null CPU/GPU → "—".
   - Expanded: each instance (label + its totals + **Close instance**), and
     under it its processes (name · PID · CPU · memory).
   - Actions per app: **Close** and **Restart** (Restart disabled with tooltip
     when the app has no launchable Exec). Every action asks to confirm first;
     a `NeedsForce` result shows the existing force prompt; errors go to a
     status line. Actions run off the UI thread (`Task.Run`), restart can take
     up to 5 s — show "Restarting…" in the status line meanwhile and disable
     that row's buttons.
   - Keyed in-place updates by app id / instance id / PID: expanded state kept,
     buttons do not move under the pointer, no full rebuild per tick.
3. Strings Thai + English for every new label (B1 language table).
4. `--probe-ui`: print the Apps page header and the top 5 apps (name,
   instances, CPU, memory, GPU) — no command lines.

## Part 2 — Processes grid

1. Replace the grouped process list on the Processes tab of
   `ProcessesView.cs` with a grid fed by `Snapshot.ProcessRows`: Name · PID ·
   User · CPU · Memory · GPU · GPU memory · Command (ellipsis, tooltip = full
   command). Sort/search like Apps, saved to `procs_sort`/`procs_sort_desc`.
   All users' processes are shown.
2. Actions column: **Close** (existing protection rules: another user's
   process, session-critical, kernel thread, our own PID → lock icon with a
   tooltip instead); **Restart** only when `ServiceUnit` is a systemd user unit
   whose MainPID is this PID (`AppActions.RestartService`), after confirm.
3. ~300 rows: keyed row reuse, and virtualise (Avalonia's built-in
   `VirtualizingStackPanel`/`ItemsControl` — **no new package**) or otherwise
   keep a tick under ~10 ms; state which you chose.
4. The **Services** tab stays as it is.
5. Remove code made dead by the new grid (old group rows) only inside
   `ProcessesView.cs`; leave `MemoryTab`/`MemoryBoard` if tests use them.
6. `--probe-ui`: Processes grid header + top 5 rows (name, PID, user, CPU,
   memory) — no command lines.

## Rules

No test edits (`SysMonitor.Linux.Tests/`); never launch the GUI (only
`--probe-ui`); scratch only under `$HOME/.cache/sysmon-wo2/`. Build (0
warnings) + `dotnet test`. Report files changed, commands, results, design
choices, and the list of tests the Test Author should add. No commit.

## Part 1 — Amendment 1 (Reviewer2)

1. **P1 restart after force** (`AppsView.cs` ~344): when `RestartApp` returns
   `NeedsForce` and the person confirms the force, after the force kill
   succeeds wait for exit (same 5 s rule) and then launch the app once (add an
   `AppActions` method such as `CompleteRestart(AppEntry)` that only launches
   when no instance of the app is still alive — never launch beside a living
   process). Status line reports the outcome. If the person declines the force,
   nothing is launched and the status says the app is still running.
2. **P2 rows move under the pointer** (~175/231): while the pointer is over the
   rows area, keep the current row **order** frozen (values still update in
   place); re-sort when the pointer leaves, when a header is clicked, or when
   the search text changes. New apps append at the end while frozen; gone apps
   are removed.
3. **P2 expansion lost on filter** (~211): prune `_expanded` only against the
   unfiltered app list (apps that no longer exist), not the filtered list.
4. **P2 disabled states** (~661/796): Restart is really disabled when Exec is
   unusable — keep the tooltip by wrapping the button in a container that
   carries the tooltip (or set `ToolTip.ShowOnDisabled`); Close instance (and
   app Close/Restart) are disabled while any action for that app is running.
5. Cosmetic: header row columns line up with the rows when the vertical
   scrollbar is visible (reserve the scrollbar width in the header, or put the
   header inside the same layout width).

Same rules as above; no test edits; report tests to add for item 1.

## Part 1 — Amendment 2 (Reviewer2 round 2)

1. **P1 launch beside a living instance** (`AppActions.cs` ~203, ~379):
   - Before launching (in both `RestartApp` and `CompleteRestart`), check
     **fresh** state, not only the PIDs captured at click time: re-read every
     instance's cgroup (`cgroup.procs`, recursively, from the instance's
     cgroup path under the given `root`) and also re-scan for any other live
     instance of the same app id (`app-<id>-*.scope` / `app-<id>@*.service`
     under the user's `app.slice`). Launch only when all of them are empty;
     otherwise `Failed(app_restart_still_running)`.
   - `Alive`: identity is PID + start time only. A process whose name changed
     (exec, `comm` rename) with the same PID and start time is still alive.
2. **P1 cancelled force continues** (`AppsView.cs` ~479): only a force result
   that actually succeeded (`KillState` meaning the process was killed/gone)
   may continue to `CompleteRestart`. `Cancelled`, `Failed`, `NeedsForce` or
   anything else stops the restart with a status message; nothing launches.
3. **P2 busy flags** (`AppsView.cs` ~396, ~460): wrap each action's awaited
   work (confirm, Task.Run, force prompt, finish) in `try/finally` so the busy
   flag is always cleared and buttons re-enabled; a faulted task writes the
   error to the status line.

Same rules; no test edits; report tests to add for items 1–2 (model level).

## Part 1 — Amendment 3 (Reviewer2 round 3)

1. **P1 failed listing allows launch** (`AppActions.cs` ~472, ~533):
   `SysFs.Directories` returns empty on an enumeration error, so an unreadable
   `app.slice` or child cgroup counts as "no instance". In the restart fresh
   check, enumerate with a helper that distinguishes "listed, empty" from
   "could not list" (e.g. `Directory.EnumerateDirectories` in try/catch
   returning null on failure). A failed listing anywhere in the walk → treat
   as not empty → `Failed(app_restart_still_running)`. Do not change
   `SysFs.Directories` itself (used elsewhere). Skip symlinked directories in
   the walk (no following links).
2. **P2 overlapping actions** (`AppsView.cs` ~396, Restart and Close
   instance too): set the app's busy flag **before** awaiting the confirm
   dialog (and clear it in the same `finally` if the person declines), so a
   second click while a confirm is open does nothing.

Same rules; no test edits; report tests to add for item 1.

## Part 2 — Amendment 1 (Reviewer2)

1. **P2 new rows under a frozen pointer** (`ProcessesView.cs` ~493): when a
   key appears while the order is frozen, append it to `_frozenOrder` too, so
   later samples keep it in place until the freeze ends. Apply the same check
   to `AppsView` if it has the same gap.
2. **P2 unknown start time** (~572): a row whose start time is null has no
   verified identity. Give it a key that is not reused across samples for a
   different process — e.g. `pid:?:<first-seen sample number>` cleared when
   the PID disappears from a sample — and never carry busy state onto a row
   whose identity cannot be verified (actions on it already go through the
   kill flow's own identity check).
3. **Command sorting — not required.** The user asked for sorting "ตามชื่อหรือ
   resource ที่ใช้งาน" (by name or resource); Command stays unsortable.
   Coding Owner records this in the plan; no code change.

Same rules; no test edits.

## Part 2 — Amendment 2 (Reviewer2 round 2)

Unknown start time (`ProcessesView.cs` ~482): a reused PID seen in
consecutive samples can still inherit the old row and its busy flag. Simplest
safe rule: a row whose `StartTime` is null has **no actions** — Close and
Restart are disabled with a tooltip (new string, Thai + English: "Cannot
verify this process" / "ยืนยันตัวตนโปรเซสนี้ไม่ได้"), it can never become busy,
and it has no busy state to inherit. Keep the key scheme from Amendment 1.
Same rules; no test edits.
