# Handoff — R-027 phase 1b tests

From: Coding Owner (Claude Code) · To: Test Author (agy, Gemini 3.8 Flash)
Branch: `linux-native` · Date: 2026-11-19

## Objective

Create `SysMonitor.Linux.Tests` (MSTest, `net10.0`) covering the parsers in
`SysMonitor.Linux` (read `Docs/HANDOFF-R027-phase1b.md` incl. its Amendment
for the intended behaviour). The Writing Owner is applying the Amendment in
parallel; write tests for the **intended** behaviour in the handoff, not
for whatever the code does today, and report any test that fails because
the code disagrees with the handoff.

## Rules

- Packages allowed only in this test project: `Microsoft.NET.Test.Sdk`
  17.8.0, `MSTest.TestAdapter` 3.1.1, `MSTest.TestFramework` 3.1.1 (in the
  NuGet cache). ProjectReference to `..\SysMonitor.Linux\SysMonitor.Linux.csproj`.
- Every test uses text fixtures or a fake root built in a temp directory
  (`Path.GetTempPath()` + GUID, deleted in cleanup) and a fake
  `ICommandRunner` and clock. No test reads the real `/proc` or `/sys`, runs
  a real command, sleeps, or generates load.
- Cover at least: `/proc/stat` deltas; meminfo; diskstats per partition
  (two partitions of one disk get different rates); mountinfo filtering
  (pseudo fs skipped, `/run/media/<user>/x` kept, octal escapes); cpuinfo +
  distinct L2/L3 cache dedupe; coretemp package + per-core mapping through
  `topology/core_id`; acpitz fallback sets `CpuTempEstimated`; nvme hwmon
  disk temperature; `iw` tx bitrate parsing and the 5 s cache; wired
  `speed` = -1 ignored; virtual interfaces skipped; pci.ids lookup; DRM
  fdinfo dedupe by `drm-client-id` and engine deltas; nvidia suspended →
  no command run; nvidia active → parsed figures; process name truncation
  rule; first `Sample` has `Ready = false`, second `true`; one broken
  source does not break the snapshot.
- Do not edit anything outside `SysMonitor.Linux.Tests/`. Do not commit.

## Verification

`~/.dotnet/dotnet test SysMonitor.Linux.Tests` (with
`export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH`). Report the pass/fail
counts and, for each failure, whether the test or the code is wrong.

## Round 2 (after Reviewer2)

Read "Amendment 2" in `Docs/HANDOFF-R027-phase1b.md`. Add tests for items
2–7 (dm-0 diskstats via `/dev/mapper`, slaves walk; nvidia gate for
`active`/`unsupported`/missing/`suspended`; two NVIDIA cards matched by
`pci.bus_id`; `sda` with no device path → `Unknown`, and a USB path →
`USB`; two sockets with different `Core 0` readings; btrfs `/` and `/home`
from one source with different roots → two disks, bind mount → one). Fix
the misleading USB test name in `BlockDevicesTests`. Files only, no shell.

## Round 3 (after Reviewer2 round 2)

- Make every `slaves/<name>` entry a symlink to that device's directory
  (`BlockDevicesTests` slave walk: also `sda1`/`sdb1`; `DiskReaderTests`
  mapper fixture).
- Virtual leaf: `dm-0/slaves/dm-1`, `dm-1/slaves` empty → physical disk
  name is empty.
- NVIDIA: one active + one suspended card → the runner is called once with
  `--id=` naming only the active card; the suspended card gets no figures.
- PCI matching with hex letters and different case/domain width (e.g.
  sysfs `0000:0a:00.0` vs nvidia-smi `00000000:0A:00.0`).
Files only, no shell, only in `SysMonitor.Linux.Tests/`.
