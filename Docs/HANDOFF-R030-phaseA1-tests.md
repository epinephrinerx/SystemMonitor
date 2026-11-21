# Handoff — R-030 phase A1 tests

From: Coding Owner · To: Test Author (agy, Gemini 3.8 Flash)

Add MSTest tests to `SysMonitor.Linux.Tests` for phase A1. The behaviour is
defined in `Docs/HANDOFF-R030-phaseA1.md`. First read the implementation for the
exact names: `SysMonitor.Core/Model/Processes.cs`, `Snapshot.cs`,
`SysMonitor.Linux/Sensors/ProcessStats.cs`, `AppResolver.cs`, `MemoryModules.cs`,
`MemInfo.cs`, the swaps reader, `DrmFdinfo.cs` and any new per-process GPU file,
`SysMonitor.Linux/Presentation/ProcessGrid.cs`, `AppActions.cs`,
`Autostart.cs` (`DesktopEntry.ParseExec`), `AppConfig.cs`, `StringData.cs`.
Use the existing `FakeRoot` helper (`TestHelper.cs`) and `FakeCommandRunner`.
Use `Assert.AreEqual` (MSTest). New files only, one per area, e.g.
`ProcessStatsTests.cs`, `AppResolverTests.cs`, `ProcessGridTests.cs`,
`AppActionsTests.cs`, `DesktopExecTests.cs`, `SwapAndModulesTests.cs`.
Do not edit production code. If a behaviour in the handoff is not what the code
does, write the test to the handoff and list it under "Mismatches" in your
report.

Cover:

- **ProcessStats:** two reads of a fake `/proc` with known utime/stime and a
  fake clock → exact CPU % for 4 logical CPUs and clockTicks 100; first read →
  null; recycled PID (same pid, new starttime) → null, not a delta; a vanished
  process leaves the cache; `/etc/passwd` lookup and the unknown-UID fallback;
  cmdline NULs → spaces and the 4096 cap; the `0::` cgroup line; a `comm`
  containing `) (` and spaces does not break field parsing.
- **Per-process GPU:** fdinfo with `drm-engine-*`, `drm-memory-vram: 1024 KiB`,
  `drm-total-system: 2 MiB`; two fds of the same client counted once; busiest
  engine delta → GPU %; no memory lines → null; NVIDIA-style fdinfo without
  engines → nulls; a process without `/dev/dri` fds is not scanned (no fdinfo
  read: assert via a fake root where its fdinfo is malformed and nothing throws
  / it stays null).
- **AppResolver:** fake cgroup tree with the exact names from the handoff §3
  (konsole scope with nested `tab(…).scope`, `dolphin@…` and `org.kde.dolphin@…`
  both resolving to `org.kde.dolphin.desktop` → one app with 2 instances,
  `google\x2dchrome@…` → `google-chrome.desktop`, `dropbox@autostart` →
  Autostart true); `Name[th]` vs `Name`; unresolved ID → name = ID, no Exec;
  ambiguous suffix match (two files ending `.foo.desktop`) → unresolved;
  instance with empty `cgroup.procs` dropped; sums of CPU/memory, CPU null only
  when all members null.
- **DesktopEntry.ParseExec:** `firefox %u` → [firefox]; `"/opt/My App/run" --x`
  → 2 args; escaped `\"` inside quotes; `%%` → `%`; `%F %i %c %k` removed;
  unbalanced quote → null; empty → null; string-value `\\` unescape.
- **ProcessGrid:** sort by each column both directions; nulls last in both
  directions; tie → name then PID; filter by name/command/user/PID text,
  case-insensitive; empty query → all.
- **AppConfig:** `apps_sort`, `apps_sort_desc`, `procs_sort`, `procs_sort_desc`
  defaults, round-trip, unknown value → default.
- **AppActions:** with a fake `IProcessSignals` and fake launcher: close app
  terminates every member through KillFlow; a protected member is not signalled;
  restart with null Exec → failed, no signal, no launch; restart where members
  exit → launcher called once with the parsed argv; members still alive after
  5 s (fake clock) → no launch; RestartService only when `ServiceUnit` set and
  passes `restart` + unit to ServiceControl.
- **Swap:** `/proc/meminfo` with SwapTotal/SwapFree → used/total/%; SwapTotal 0
  → usage 0; `/proc/swaps` with a header, a file, a partition and a name with
  `\040`.
- **Memory modules:** fixture using the real keys below; slots 2 of 2, LPDDR4,
  Micron, 4 GiB × 2, 2667/4267 MT/s, voltage 0.6 V from 600 mV; a slot with
  size 0 not listed; "Unknown"/"Not Specified" → null; missing file → nulls and
  empty list; **the serial, asset tag and part number values never appear** in
  any property of the result (search the ToString/serialised output for the
  fixture's serial text).

```
E:MEMORY_ARRAY_NUM_DEVICES=2
E:MEMORY_ARRAY_MAX_CAPACITY=34359738368
E:MEMORY_DEVICE_0_LOCATOR=DIMM 0
E:MEMORY_DEVICE_0_SIZE=4294967296
E:MEMORY_DEVICE_0_TYPE=LPDDR4
E:MEMORY_DEVICE_0_TYPE_DETAIL=Synchronous
E:MEMORY_DEVICE_0_FORM_FACTOR=Row Of Chips
E:MEMORY_DEVICE_0_SPEED_MTS=4267
E:MEMORY_DEVICE_0_CONFIGURED_SPEED_MTS=2667
E:MEMORY_DEVICE_0_MANUFACTURER=Micron
E:MEMORY_DEVICE_0_SERIAL_NUMBER=SECRET123
E:MEMORY_DEVICE_0_ASSET_TAG=TAG999
E:MEMORY_DEVICE_0_PART_NUMBER=PARTXYZ
E:MEMORY_DEVICE_0_RANK=2
E:MEMORY_DEVICE_0_CONFIGURED_VOLTAGE=600
```

Report: files added, number of tests, mismatches with the handoff. You cannot run
shell; the Coding Owner runs `dotnet test`.

## Note from Coding Owner (after WO2)

- Configured voltage outside 500–5000 mV → `null` (Amendment 1). 600 mV in
  the fixture stays 0.6 V; add cases for `1` and `6000` → null.
- Apps whose resolved `.desktop` files differ but have identical `Name` and
  `Exec` are merged into one app (accepted deviation); two files with different
  Exec stay separate.
- `ParseExec` lives in `SysMonitor.Linux.Presentation.DesktopEntry`.

## Round 2 (after Amendment 2)

Add tests for Amendment 2 items 1–6: RestartService refuses when start time
or name differs, when the fresh MainPID differs, and runs only when all match;
null start time → no GPU values; empty-engine client → GPU % null; UTF-8
`\xc3\xa9` unescape → `café`, invalid UTF-8 → no throw; `"foo"bar` and
`foo"bar"` → ParseExec null. Update any of your earlier tests that assumed the
old behaviour.

### Round 2 — failures found by the Coding Owner (dotnet test: 8 failed / 510)

1. `AppResolverTests` (6 tests): `FakeRoot.WriteFile` takes a path **relative
   to the fake root**, but the tests pass `Path.Combine(appSlice, …)` where
   `appSlice` already contains `fake.Root`, so the files land under
   `<root>/<root>/…` and the resolver finds nothing. Use a relative app-slice
   path (e.g. `sys/fs/cgroup/user.slice/user-1000.slice/user@1000.service/app.slice/…`).
2. `DesktopExecTests.ParseExec_DoublePercent_BecomesSinglePercent`:
   `myapp %% --ratio 50%%` is 4 arguments (`myapp`, `%`, `--ratio`, `50%`);
   the test expects 3. Fix the expectation.
3. `AppActionsTests.RestartService_WhenServiceUnitSet_CallsServiceControlRestart`:
   update to Amendment 2 item 1 (fake `/proc/<pid>/status` + `stat` with a
   matching start time and name, a fake `systemctl --user show -p MainPID`
   output returning the same PID, `ownUid`), and add the refusal cases.
Then add the Amendment 2 tests listed above.

### Round 3 (after Amendment 3)

- `RestartService` on a session-critical name (`plasmashell`) owned by our own
  UID, with a matching MainPID → failed, `systemctl restart` never called.
  Adjust any Round 2 test that relied on the own-UID exception.
- `app-cafe@bad\xff.service` with `cafe.desktop` present → unresolved (no Exec,
  DesktopFile null), no throw.
