# Handoff — tests for R-030 phase C Part 2 (Processes grid)

From: Coding Owner · To: Test Author (agy). No shell, no subagents; file
read/write tools only. Behaviour: `Docs/HANDOFF-R030-phaseC.md` Part 2. Read
first: `SysMonitor.Linux/Presentation/ShellModel.cs` (SetProcsSort,
CanEndProcess), `ProcessGrid.cs`, `StringData.cs`, `Lang.cs`, the protection
rules (`ProcessProtection`), and existing `PhaseC1Tests.cs` for helpers. Check
every member name against the real code. New file
`SysMonitor.Linux.Tests/PhaseC2Tests.cs`. Items 1–4 (skip 5):

1. `ShellModel.SetProcsSort`: new numeric column starts descending; clicking the same column reverses; new Name starts ascending; values saved to `procs_sort`/`procs_sort_desc` and reloaded from `AppConfig`.
2. `ShellModel.CanEndProcess`: matrix over own UID, other UID, PID 1/2, own PID, kernel thread, unreadable entry, null UID, and every `ProcessProtection.SessionNames` entry; assert it matches `ProcessProtection.IsProtected` for the same fields.
3. `StringData.ProcessesPageKeys`: Thai + English present for every key, `procs_count` formats in both languages, and no duplicate keys across all eight tables (extend the existing table list in `PhaseC1Tests`).
4. `ProcessGrid` process filter/sort: filter by command, user, PID and name (case-insensitive); sort by User and PID ascending/descending; confirm null CPU/GPU still last in both directions.

Report files added, count, mismatches.
