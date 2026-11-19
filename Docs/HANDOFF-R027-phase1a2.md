# Handoff — R-027 phase 1a-2: remove Windows from the Linux branch

From: Coding Owner (Claude Code) · To: Writing Owner 2 (opencode, `opencode-go/deepseek-v4.1-flash`)
Branch: `linux-native` (renamed from `linux-v4`; base `main` 41c29c3) · Date: 2026-11-19
Requirement: `Requirements.md` R-027, decision 3

## Objective

The user decided this branch is Linux-only: nothing here has to build or
run on Windows any more. Remove every Windows-only project and make
`SysMonitor.Core` depend on nothing. The Windows 4.3.0 code stays on `main`
for reference (`git show main:<path>`).

Phase 1a is already staged in the index (the move into `SysMonitor.Core`).
Build on top of it; do not unstage or redo it.

## Acceptance criteria

1. `git rm -r` these, whole: `SysMonitor.Wpf/`, `SysMonitor.Setup/`,
   `SysMonitor.Ext.Smart/`, `SysMonitor.Ext.Sample/`,
   `SysMonitor.Extensibility/`, `SysMonitor.PackTool/`, `SysMonitor.Tests/`,
   `SysMonitor.Tests.DummyExtension/`, `build-wpf.cmd`.
   Also delete their ignored `bin/`/`obj/` leftovers from disk.
2. `SysMonitor.Core`:
   - `git rm SysMonitor.Core/Extensions/ExtensionViewData.cs`.
   - `Model/Snapshot.cs`: remove the `_extensions` field and the
     `Extensions` property (lines 16-22 today) and nothing else.
   - `SysMonitor.Core.csproj`: remove the `ProjectReference` to
     `SysMonitor.Extensibility` and its comment; Core then has no
     `ProjectReference` and no `PackageReference`. Update the remaining
     comment so it no longer mentions Windows sharing.
   - `AssemblyInfo.cs`: keep only `InternalsVisibleTo("SysMonitor.Linux")`
     and `InternalsVisibleTo("SysMonitor.Linux.Tests")`; fix the comment.
   - Do not touch other Windows wording in doc comments (`Memory.cs`,
     `TempInfo.cs`, `Snapshot.cs`); phase 1b rewrites those types.
3. `Docs/ARCHITECTURE.md` §5 table: delete the rows of removed projects;
   the `SysMonitor.Core` row says: no ProjectReference, no package, no OS
   API. Remove the bullet about `SysMonitor.Wpf` `InternalsVisibleTo`.
   Add one sentence under the table: "Branch `linux-native` is Linux-only
   (R-027); the Windows projects live on `main`." Leave the rest of the
   document for phase 4.
4. `README.md`: add one line at the very top:
   `> Branch linux-native: Linux-only rebuild in progress (R-027). The Windows 4.3.0 app is on main.`
   No other README change.
5. `grep -rn "SysMonitor.Wpf\|SysMonitor.Extensibility\|Ext.Smart\|PackTool\|build-wpf" --include=*.csproj --include=*.cs --include=*.sh --include=*.cmd --include=*.json --include=.gitignore .`
   (excluding `bin/`, `obj/`) returns nothing outside `Docs/` and `CHANGELOG.md`.
6. `~/.dotnet/dotnet build SysMonitor.Core` — 0 errors, 0 warnings.

## Scope

Only the paths named above. Do not touch `Requirements.md`, `CLAUDE.md`,
`AGENTS.md`, `Docs/AI_WORKFLOW.md`, `CHANGELOG.md`, `NEXT_STEPS.md`, the
historical `Docs/REVIEW-*` / `HANDOFF-*` / `CODING-OWNER-PLAN-*` files,
`Docs/EXTENSION_CONTRACT.md`, `Docs/adr/`, `SysMonitor.ico`.

## Constraints

- No new code, no Linux code (phase 1b), no packages.
- Do not commit, push or create branches.
- Do not open windows or run load tests.

## Environment

`export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH`

## Report back

```
## Summary
## Changed        (deleted dirs as one line each; edited files one line each)
## Verification   (criterion 5 grep output, criterion 6 build: PASS/FAIL, warnings)
## Open questions / risks
```
