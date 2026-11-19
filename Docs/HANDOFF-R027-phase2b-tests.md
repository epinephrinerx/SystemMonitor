# Handoff — R-027 phase 2b tests

From: Coding Owner · To: Test Author (agy, Gemini 3.8 Flash)

Add tests to `SysMonitor.Linux.Tests` for the phase 2b logic. Behaviour:
`Docs/HANDOFF-R027-phase2b.md` (incl. its Amendment) and
`Docs/WPF-UI-SPEC-4.3.0.md` §5–§5c. The Writing Owner is applying the
Amendment in parallel; test the intended behaviour and read the current
code for type names (`FullBoard`, `FullModel`, `ChartMath`, `MemoryTab`,
`KillFlow`, `ServiceControl`, `MemoryCadence`, `ServiceReader`,
`ProcessReader`, `ProcessSignals`, `Posix`, `StringData`, `Lang`).

Cover at least:
- **Full tabs:** order and keys, including `disk:<kernel name>` and the
  root disk first; one tab per GPU; network tabs; `full_tab` restore and
  the fallback to the first tab.
- **Chart maths (§5b):** 72-point ring; scale for max 100 vs auto ×1.25;
  x step and newest point at the right; a partial series not stretched;
  the fill mix 0.62 / 0.72; the ceiling text.
- **Process groups:** case-insensitive grouping, sorting, the 40/120 caps
  and the note, search.
- **Protection rules:** PID 1/2, kernel thread, own PID, other UID, the
  session-critical names, unreadable entries.
- **Kill flow** with a fake signal sender: SIGTERM first; the name
  re-check aborts on a changed name with `mem_pid_changed`; force-kill
  prompt after 3 s (fake clock); EPERM → `mem_failed`.
- **ServiceReader:** parses the `--output=json` list and batched
  `systemctl show` text from fixtures, for system and `--user` scopes.
- **Service sorting and caps** (60/120); system units have no actions;
  the user-unit startup menu offers only Automatic/Manual.
- **Strings:** the new `LinuxExtras` keys exist in both languages. Update
  the 197-row count test so it accounts for `LinuxExtras` instead of
  failing.
- **ProcessReader:** Uid, ParentPid and kernel-thread detection from a
  fake `/proc`.

Never send a real signal or run a real `systemctl`. Use fakes only. Files
only, no shell, only in `SysMonitor.Linux.Tests/`. Do not commit.

## Round 2 (after Reviewer2)

Tests for Amendment 2 in `Docs/HANDOFF-R027-phase2b.md` (fakes only,
never a real signal):
- `/proc/<pid>/stat` StartTime parsing with a `comm` holding spaces and
  `)`;
- PID reused by a same-name process with a different StartTime → abort,
  no signal;
- a stale killable row whose PID now belongs to a protected process
  (other UID or a session-critical name) → abort with `mem_protected`;
- `Uid == null` → protected;
- every `systemctl` argv has `--` before the unit names;
- a unit name starting with `-` stays after `--`;
- an empty unit name, or one with `/`, is rejected.

Read the code for the signal-sender abstraction names after the Writing
Owner's change. Files only, no shell, only in `SysMonitor.Linux.Tests/`.

## Round 3 (after Reviewer2 round 2)

- StartTime null or 0 on the row or at action time → no signal,
  `mem_pid_changed`.
- Use non-zero StartTimes in the happy-path kill tests.
- Terminate → NeedsForce → Force: `Pin` called once, the same handle
  signals SIGTERM then SIGKILL, `Dispose` called exactly once.
- Terminate → NeedsForce → user declines: `Dispose` called once, no
  SIGKILL.
- Terminate → process exits within 3 s: `Dispose` called once.
Fakes only. Files only, no shell, only in `SysMonitor.Linux.Tests/`.

## Round 4 (after Reviewer2 round 3)

- `CancelAll` then `Force(token)`: no `Pin`, no signal, result `Cancelled`.
- Two `Terminate` calls for one PID: the first token is disposed when the
  second is created. `Force(firstToken)` does nothing. `Force(secondToken)`
  signals through the second handle only.
- `Dispose` called twice → the handle closes once.
- Two threads calling `Dispose` on the same token concurrently → the
  handle closes once (use `Parallel.For` or two `Task`s; no sleep).
Fakes only. Files only, no shell, only in `SysMonitor.Linux.Tests/`.

## Round 5 (after Reviewer2 round 4)

Deterministic interleaving tests with a fake signal sender. Inject hooks,
for example a fake `Pin`/`WaitForExit` that blocks on a
`ManualResetEventSlim`, to force each order. No sleeps.
- Two `Terminate` calls for one PID, interleaved so the second registers
  first. Afterwards exactly one token is live, and every other handle is
  disposed exactly once.
- `CancelAll` while `Force` is about to run: either the SIGKILL happened
  before the close, or no signal is sent. After `CancelAll` returns, no
  signal is ever sent.
- `CancelAll` during Terminate's 3 s wait: the result is `Cancelled`, and
  the handle is disposed once.
- `ShellModel` exposes one `KillFlow` instance from construction.
Files only, no shell, only in `SysMonitor.Linux.Tests/`.

## Round 6 (after Reviewer2 round 5)

- Rewrite `CancelAll_WhileForceAboutToRun_WhenForceAcquiresGateFirst_…`.
  Inside the fake SIGKILL callback, start `CancelAll` on a task, then
  wait (5 s timeout) for the `onCancelAllRequested` hook before
  returning. This proves the overlap, because `CancelAll` is blocked on
  the gate while `Force` holds it. Assert one SIGKILL, and that later
  calls send nothing.
- New test: `CancelAll` requested while `Terminate` is inside `Pin`. Block
  the fake `Pin` until the hook fires. Expect `CancelAll` to wait, SIGTERM
  to have been sent before the close (the user's confirmed action), the
  token to be disposed by `CancelAll`, and no signal after `CancelAll`
  returns.
- `Terminate` after `CancelAll`: `Pin` is never called.
Fakes only. Files only, no shell, only in `SysMonitor.Linux.Tests/`.

## Round 6b

Amendment 6 makes `Terminate_TwoCallsForSamePid_InterleavedSoSecondRegistersFirst_…`
impossible by design: step 1 holds the gate, so call 2 cannot register
first. Replace that test with
`Terminate_TwoCallsForSamePid_SecondWaitsForGate_ExactlyOneTokenLive_EarlierDisposedOnce`:
- Hold call 1 inside its SIGTERM callback.
- Start call 2 and give it a fixed window: a `ManualResetEventSlim` set by
  call 2's `OnPin`, waited with a 300 ms timeout. Assert that call 2 did
  **not** pin while call 1 held the gate.
- Release call 1. Both calls complete within 5 s.
- Afterwards there are 2 pins, call 1's token is disposed exactly once
  (replaced), and call 2's token is live, or is `Cancelled`/`NeedsForce`
  per the results.

## Round 6c (after Reviewer2 round 6)

In `Terminate_TwoCallsForSamePid_SecondWaitsForGate_…`, replace the
300 ms window with a barrier: wait (5 s timeout) for
`onTerminateRequested` from call 2, while call 1 is still held inside its
SIGTERM callback. Then assert that call 2 has not pinned, release call 1,
and keep the existing end-state asserts. A short negative wait after the
barrier, to show call 2 is blocked rather than not yet run, is fine.
