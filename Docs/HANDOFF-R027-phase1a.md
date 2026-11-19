# Handoff — R-027 phase 1a: shared model library

From: Coding Owner (Claude Code) · To: Writing Owner 2 (opencode, `opencode-go/deepseek-v4.1-flash`)
Branch: `linux-v4` (from `main` 41c29c3, v4.3.0) · Date: 2026-11-19
Requirement: `Requirements.md` R-027 (Linux rebuild on the 4.3.0 line)

## Objective

Move the platform-free snapshot model out of `SysMonitor.Wpf` into a new
library `SysMonitor.Core`, so the coming Linux build can share it. This is a
**pure move**: the Windows build's behaviour, public shape and tests must not
change.

## Acceptance criteria

1. New project `SysMonitor.Core/SysMonitor.Core.csproj`: `net8.0` (not
   `-windows`), `Nullable` and `ImplicitUsings` enabled, `LangVersion latest`,
   **no `PackageReference`**, `RootNamespace` `SysMonitor`.
2. These files are moved with `git mv` (history kept), content unchanged
   except where a step below says otherwise:
   - `SysMonitor.Wpf/Model/History.cs` → `SysMonitor.Core/Model/History.cs`
   - `SysMonitor.Wpf/Model/Memory.cs` → `SysMonitor.Core/Model/Memory.cs`
   - `SysMonitor.Wpf/Model/Snapshot.cs` → `SysMonitor.Core/Model/Snapshot.cs`
   - `SysMonitor.Wpf/Model/TempInfo.cs` → `SysMonitor.Core/Model/TempInfo.cs`
   - `SysMonitor.Wpf/Sensors/IoRate.cs` → `SysMonitor.Core/Sensors/IoRate.cs`
3. Namespaces stay exactly as they are (`SysMonitor.Model`,
   `SysMonitor.Sensors`).
4. `IoRate` (and any other moved type that is `internal`) stays `internal`.
   `SysMonitor.Core/AssemblyInfo.cs` grants
   `[assembly: InternalsVisibleTo("SystemMonitor")]` (the WPF assembly name)
   and `[assembly: InternalsVisibleTo("SysMonitor.Tests")]`. Add
   `"SysMonitor.Linux"` and `"SysMonitor.Linux.Tests"` too — the next phase
   uses them.
5. `SysMonitor.Wpf.csproj` gets a `ProjectReference` to
   `..\SysMonitor.Core\SysMonitor.Core.csproj`. Nothing else in that file
   changes.
6. If any moved file uses a WPF or Windows-only API, **stop and report** —
   do not edit it to make it compile. (The Coding Owner checked: none should.)
7. `SysMonitor.Setup`, `SysMonitor.Ext.*`, `SysMonitor.Extensibility`,
   `SysMonitor.PackTool` and the installer payload: if any of them compiles
   one of the moved files by path (`<Compile Include="..\SysMonitor.Wpf\Model\...">`)
   or packs `SystemMonitor.dll` alone, update the path / add
   `SysMonitor.Core.dll` to what is packed, and list it in your report.
   `grep -rn 'Model\\\|IoRate\|SystemMonitor.dll' --include=*.csproj --include=*.cs --include=*.cmd --include=*.json .`
   finds the places.
8. Builds on Linux (this machine has no Windows):
   - `~/.dotnet/dotnet build SysMonitor.Core` — 0 errors, 0 warnings
   - `~/.dotnet/dotnet build SysMonitor.Wpf -p:EnableWindowsTargeting=true` — 0 errors, no new warnings
   - `~/.dotnet/dotnet build SysMonitor.Tests -p:EnableWindowsTargeting=true` — 0 errors
9. `Docs/ARCHITECTURE.md` §5 (dependency rules) gets one row for
   `SysMonitor.Core`: depends on nothing; no WPF, no Windows API, no package.

## Scope (files you may touch)

- new: `SysMonitor.Core/**`
- moved: the five files above
- `SysMonitor.Wpf/SysMonitor.Wpf.csproj` (one `ProjectReference`)
- only if criterion 7 finds something: the file it points at
- `Docs/ARCHITECTURE.md` §5 (one row)

## Constraints and non-goals

- No behaviour change, no renames, no reformatting, no "while I'm here"
  cleanup. A reviewer will diff the moved files with `git diff -M` and expect
  100 % similarity.
- No new packages anywhere.
- Do not touch `Requirements.md`, `CLAUDE.md`, `AGENTS.md`.
- No Linux code yet — that is phase 1b.
- Do not commit, push, or create branches. Leave the change in the working
  tree; the Coding Owner reviews and commits.
- Do not run load tests or open any window.

## Environment

- .NET 10.0.404 SDK at `~/.dotnet` (`export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH`).
  It can build `net8.0`; restore may download the .NET 8 targeting packs once.
- Windows tests (`dotnet test SysMonitor.Tests`) cannot run here. Say so in
  the report; do not claim they pass.

## Report back (in this shape)

```
## Summary
## Changed        (every file, one line each)
## Verification   (each command from criterion 8, PASS/FAIL, warning count)
## Criterion 7    (what grep found and what you did)
## Open questions / risks
```

## Known risks

- `SysMonitor.Tests` may reference `IoRate` or model internals: the
  `InternalsVisibleTo` in criterion 4 covers that.
- The installer or the pack tool may copy a fixed list of files; a missing
  `SysMonitor.Core.dll` would install a build that crashes at start. That is
  why criterion 7 exists.

## Amendment (Coding Owner, 2026-11-19, after Reviewer2 round 1)

- **Accepted:** the sixth move `SysMonitor.Wpf/Extensions/ExtensionViewData.cs`
  → `SysMonitor.Core/Extensions/ExtensionViewData.cs`, and the one
  `ProjectReference` `SysMonitor.Core` → `SysMonitor.Extensibility`.
  `Snapshot.Extensions` is typed by `ExtensionViewData`, which wraps the
  contract type `ExtensionView`; a pure move of `Snapshot` needs both.
  `SysMonitor.Extensibility` is `net8.0` with no WPF and no package, so
  Core stays platform-free. Criterion 9 now reads: Core depends on
  `SysMonitor.Extensibility` only; no WPF, no Windows API, no package.
- **Fix requested (round 2):** `Docs/ARCHITECTURE.md` §5, the
  `SysMonitor.Tests` row, says it references every project; it reaches
  `SysMonitor.Core` only through `SysMonitor.Wpf`. Correct that row's text.
  No csproj change.
