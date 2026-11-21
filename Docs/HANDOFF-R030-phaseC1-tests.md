# Handoff — tests for R-030 phase C Part 1 (sampling + Apps page)

From: Coding Owner · To: Test Author (agy). No shell, no subagents; file
read/write tools only. Behaviour: `Docs/HANDOFF-R030-phaseC.md` Part 1.
Read first: `SysMonitor.Linux/Presentation/MemoryCadence.cs`,
`SysMonitor.Linux/Sampling/LinuxSampler.cs`, `SysMonitor.Core/Model/Snapshot.cs`,
`SysMonitor.Linux/Presentation/ShellModel.cs` (search SetAppsSort,
MainGridVisible, AppActions), `StringData.cs`, `Lang.cs`, `ProcessGrid.cs`,
and existing tests that use fake roots (e.g. `ProcessStatsTests.cs`,
`AppResolverTests.cs`, `ShellModelTests.cs`) to reuse their fixture helpers.
Check every member name against the real code. New file
`SysMonitor.Linux.Tests/PhaseC1Tests.cs`. Skip any item that would need
Avalonia (the test project does not reference the App project).

Items proposed by Writing Owner 2:
## Tests the Test Author should add

1. `MemoryCadence.Next`: grid due at 0, not due under 2 s, due at 2 s; never due when not on a grid page; `gridForced` ignores the interval; `onRamTab: false` + `onGridPage: true` → only `Grid` is true.
2. `LinuxSampler.Sample(includeGrid: true)` on a fixture `/proc` + fake `app.slice`/desktop dir: `ProcessRows` populated, apps grouped; first grid read has null CPU, second read after the fake clock advances has a value.
3. `LinuxSampler.Sample(includeGrid: false)` republishes the previous `ProcessRows`/`Apps` (same content, not empty), including after a sample that failed to read the grid.
4. Service-by-PID mapping: a user unit’s PID appears as `ProcessRow.ServiceUnit`; a system unit’s PID does not.
5. `Snapshot` defaults: `ProcessRows`/`Apps` are non-null empty, and their lists are sealed against later mutation.
6. `ShellModel.SetAppsSort`: new numeric column starts descending, `Name` starts ascending, second click reverses, config key/direction are saved and read back.
7. `ShellModel.MainGridVisible` for Overall/Apps/Processes in Main vs Widget, and `AppActions` sharing the one `KillFlow` (a token from `CloseApp` can be forced through `ShellModel.ForceKill`).
8. String table: `AppsPageKeys` has Thai + English for every key, no duplicates across all tables, `apps_count` formats, and the removed `apps_placeholder` use has no replacement key requirement.
9. `ProcessGrid.Sort` for Apps across `Name`, `Instances`, `Cpu`, `Gpu`, `GpuMemory` with null-last in both directions (covered partially today; extend for the new columns).
10. `DesktopEntry.ParseExec`-driven restart policy at model level: App with no usable `Exec` → `AppActions.RestartApp` returns `app_restart_unavailable` (existing coverage, re-check after the Apps page wiring).


Report files added, test count, mismatches.

## Round 2 — Amendment 1 item 1 (`AppActions.CompleteRestart`)

Add to `PhaseC1Tests.cs` (or a new `PhaseC1RestartTests.cs`). Read `SysMonitor.Linux/Presentation/AppActions.cs` and the existing `AppActionsTests.cs` fakes first.
For item 1 (all model-level, using the existing fake `launcher`/`clock`/`sleep`/`isAlive` seams in `AppActions`):

1. `CompleteRestart` launches exactly once when every member is gone — one recorded argv, `ActionOutcome.Done`.
2. `CompleteRestart` does **not** launch while a member reports alive at the deadline — `Failed` with `app_restart_still_running`, recorder empty, and the fake clock advanced by at least `RestartWaitMs` (proves the 5 s wait).
3. `CompleteRestart` waits for a member that disappears mid-wait (`isAlive` true then false) and then launches once — proves it waits for exit rather than failing at once.
4. `CompleteRestart` refuses an unusable `Exec` (null, empty, whitespace, no argv) with `app_restart_unavailable` and never calls the launcher or the sleep.
5. `CompleteRestart` with an app that has no instances is refused (`app_restart_unavailable`) — the "no member to verify, do not launch" guard.
6. Launcher error is returned as `Failed(<error>)` and is not retried; a second call to `CompleteRestart` is not guarded at model level, so record the documented expectation that the view's busy flag is what prevents it.
7. Sequence at model level: `RestartApp` → `NeedsForce` token → `ForceKill` → `CompleteRestart` records exactly one launch; the declined branch has no launch. The decline UI itself lives in `AppsView` and is not reachable from the test project (it has no reference to `SysMonitor.Linux.App`), so say so in the test report.


## Round 3 — Amendment 2 (fresh cgroup check, Alive identity, Cancelled force)

New file `SysMonitor.Linux.Tests/PhaseC1FreshCheckTests.cs`. Read `AppActions.cs`, `KillFlow.cs`, `ShellModel.cs` and the existing `AppActionsTests.cs`/`PhaseC1Tests.cs` fakes (FakeRoot, fake signals, recording launcher) first. Items 1–10 below; skip 11.
Item 1 — `AppActions` with `FakeRoot` + fake `IProcessSignals`, `isAlive: false`, recording launcher:

1. `RestartApp`: known instance's `cgroup.procs` has a PID → `Failed`, recorder empty (the snapshot members are gone, the cgroup is not).
2. `RestartApp`: nested child cgroup has a PID → same, proving recursion.
3. `RestartApp`: another top-level `app-<id>@other.service` (or `app-<id>-2.scope`) has a PID → `Failed`, recorder empty.
4. `RestartApp`: an escaped unit (`app-google\x2dchrome@x.service`) with a PID is detected under `app.Id = "google-chrome"`.
5. `RestartApp`: every cgroup missing or with empty `cgroup.procs` → `Done`, exactly one recorded argv.
6. `RestartApp`: a cgroup directory exists but has no readable `cgroup.procs` → `Failed`, no launch.
7. `CompleteRestart`: a process appears in the cgroup after the snapshot while the captured PIDs are gone → `Failed`, no launch (the core Amendment 2 regression).
8. `Alive` identity: fake `/proc/<pid>` has the row's PID and start time but a different `Name` → `CompleteRestart` returns `Failed(app_restart_still_running)` and does not launch; `RestartApp` returns `NeedsForce`, no launch. This fails on the previous name-comparing implementation.
9. Assert blocked fresh checks return `Stubborn.Count == 0` (no force tokens).

Item 2 — model level:

10. `KillFlow.Force` after `Decline`/`CancelAll` returns `Cancelled` with no SIGKILL; `ShellModel.ForceKill` propagates `Cancelled`. This is the model evidence for "only `Gone` continues".

## Round 4 — Amendment 3 (unlistable cgroups block launch, symlinks skipped)

Add to `SysMonitor.Linux.Tests/PhaseC1FreshCheckTests.cs`. Prefer the file-in-place-of-directory fixture; for permission-mode fixtures use `Assert.Inconclusive` when running as root. Items:
1. `RestartApp`: `app.slice` replaced by a **regular file** while the known instance cgroup is empty → `Failed(app_restart_still_running)`, launcher not called, `Stubborn.Count == 0`.
2. `RestartApp`: `app.slice` directory mode `000` → same result (guard with `Posix.Uid != 0`, since root ignores permission bits; otherwise `Assert.Inconclusive`).
3. `CompleteRestart`: same unlistable-slice fixtures → same failure, no launch.
4. A nested child cgroup with mode `111` (listable path traversable, `cgroup.procs` readable, but `EnumerateDirectories` fails) → `Failed`, no launch; proves the *child listing* failure path specifically.
5. Symlink skipped, not followed: `app.slice/app-<id>-2.scope` is a symlink to a real directory containing a live `cgroup.procs` → `Done`, exactly one launch.
6. Same for a symlinked nested child inside the known instance cgroup → `Done`, one launch.
7. Symlinked **instance path** (`app.slice/app-<id>-1.scope` → real dir) → `Failed`, no launch (documented conservative choice).
8. Regression guard: with the whole `app.slice` missing, still `Done` + one launch (already covered by `RestartApp_WhenEveryCgroupMissingOrEmpty...`, re-check after this change).
9. `SysFs.Directories` still returns an empty (non-null) list for an unreadable directory — i.e. it was not changed.
