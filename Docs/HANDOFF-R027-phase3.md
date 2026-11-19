# Handoff — R-027 phase 3: autostart, tray, close/quit, restart, updater

From: Coding Owner (Claude Code) · To: Writing Owner 2 (opencode, `opencode-go/deepseek-v4.1-flash`)
Branch: `linux-native` at `f8f10a4` · Date: 2026-11-20
Requirement: `Requirements.md` R-027 · Reference: `Docs/WPF-UI-SPEC-4.3.0.md` **§8, §9**
(Processes and services were done in phase 2b.)

## Objective

Replace the no-op `IAutostart`, `ITray`, `IRestarter` and `IUpdater` in
`SysMonitor.Linux/Presentation/Integrations.cs` with real Linux
implementations. Wire them into `SysMonitor.Linux.App` exactly as spec §8
and §9 describe: Settings checkbox, close action, tray icon and menu,
Restart, the sidebar update panel, the start-up check, and saving on
session end.

As before, put the logic and its pure helpers in `SysMonitor.Linux`,
testable through fakes (file system root, `ICommandRunner`, an
`HttpMessageHandler`, a clock). Avalonia code goes only in the App.

## Linux decisions

| Item | Linux |
|---|---|
| Autostart | `$XDG_CONFIG_HOME/autostart/systemmonitor.desktop` (default `~/.config/autostart`). Content: `[Desktop Entry]`, `Type=Application`, `Name=System Monitor`, `Exec=<quoted exe>`, `Icon=systemmonitor`, `X-GNOME-Autostart-enabled=true`, `Terminal=false`. Exe = `Environment.ProcessPath`. Quote it per the Desktop Entry spec (wrap in `"…"`; escape `"`, `` ` ``, `$`, `\`). `IsEnabled` is true only if the file exists, is not `Hidden=true`, and its `Exec` unquotes to exactly the current exe (spec §9). Disabling deletes **only** that file. Write atomically (temp file + rename). |
| Tray | Avalonia's built-in `TrayIcon` (StatusNotifierItem on KDE), no package. It exists only while the window is hidden. Menu (`NativeMenu`): `tray_show`, separator, `tray_exit`. A click restores. Tooltip text per spec §9, built by a pure function in `SysMonitor.Linux` (`TrayText.Build(snapshot, lang)`). Drop the 63-char Windows limit. Refresh it on each fresh snapshot while hidden, because the sampler keeps running. If the tray cannot be created (no SNI host), close falls back to **minimise** and logs it to Diagnostics. Never hide the window with no way back. |
| Close / Quit | Spec §9. Quit: stop the timers, save placement and config, stop the sampler, dispose the tray. |
| Session end | Save placement in the desktop lifetime's `ShutdownRequested`. |
| Restart | Save placement and config, start `Environment.ProcessPath` with the same arguments (`ProcessStartInfo.ArgumentList`, no shell), then shut down. The single-instance lock in `Platform` must **wait up to 5 s** for the old copy to release it (add the wait if it is missing). `--allow-multiple` bypasses it. |
| Update check | Spec §8 endpoint, headers, timeout, version parsing, retry once after 60 s, silent pending. Asset: `SystemMonitor-<ver>-linux-x64.tar.gz` plus its checksum asset `SystemMonitor-<ver>-linux-x64.tar.gz.sha256`. The checksum file holds the hex digest, optionally followed by a file name. Hosts are on the spec §8 allow-list, https only. The arch suffix comes from `RuntimeInformation.OSArchitecture` (`x64`, `arm64`). Missing asset or missing checksum → `up_to_date` is **wrong**; report it as `check_failed`, and log the reason. |
| Download + install | Download to `$XDG_CACHE_HOME/SystemMonitor/update/` (default `~/.cache/…`). Report progress. Delete a partial file on failure. Verify the SHA-256; a mismatch means `download_failed` and the file is deleted. Extract with `System.Formats.Tar` + `GZipStream` into `…/update/<ver>/`. Reject any entry that is absolute, contains `..`, or is a link pointing outside, or is a device or FIFO. The archive must contain `install.sh` at its root (phase 4 writes it). Run `/bin/sh <dir>/install.sh --update --wait-pid <our pid>` through `ProcessStartInfo.ArgumentList`, detached. Then save config and exit. If anything fails → `download_failed`, the pending update is cleared, and the app stays up. |
| `install.sh` contract (phase 4 implements) | `--update --wait-pid N`: wait up to 30 s for PID N to exit, install the extracted files into the per-user install location, then start the new `sysmonitor`. |

New strings, if needed, go in `StringData.LinuxExtras` (Thai + English).

## Acceptance criteria

1. App build 0 errors and 0 warnings. `dotnet test SysMonitor.Linux.Tests`
   still passes. Do not edit the tests, except to fix compilation
   broken by an API change.
2. `--probe-ui` additionally prints:
   - autostart `IsEnabled` and the `.desktop` text it **would** write
     (no write);
   - the tray tooltip for the current snapshot;
   - the update asset names it would look for.
3. **New `--probe-update` switch.** It makes one real GitHub check: GET
   only, no download. It prints the parsed result. Run it once and paste
   the output. No Linux release exists yet, so `check_failed` with "no
   linux asset" is the expected result.
4. **Never** write the real `~/.config/autostart`. Never download or run
   an installer. Never restart or launch the app. No screenshots. Use
   only fake roots and fake HTTP in code paths you exercise.

## Scope and constraints

- Scope: `SysMonitor.Linux/Presentation/**`,
  `SysMonitor.Linux/*.cs`, and `SysMonitor.Linux.App/**`.
- No new packages. No root, `pkexec` or `sudo`.
- Do not touch `Requirements.md`, `CLAUDE.md`, `AGENTS.md`, `Docs/AI_WORKFLOW.md`
  or `Docs/WPF-UI-SPEC-4.3.0.md`.
- Do not commit, push or create branches.
- Set up the toolchain with `export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH`.

## Report back

```
## Summary
## Changed        (one line per file)
## Verification   (build; tests; --probe-ui new lines; --probe-update output)
## Spec deviations and "Unclear" choices
## Open questions / risks
```

## Amendment 1 (Coding Owner, after the first report)

Accepted: the other deviations as reported.

1. **`%` in `Exec`.** The Desktop Entry spec reserves `%` for field
   codes, so write it as `%%`. Unquoting reverses it, so `IsEnabled`
   still compares against the real exe path. The handoff missed this.
2. **No-tray fallback must leave a way back.** With no SNI host, closing
   must not strand a borderless, taskbar-less window. On fallback, set
   `ShowInTaskbar = true` before minimising, then restore the configured
   value when the window is shown again. Log the fallback once per run.

## Amendment 2 (Coding Owner, after Reviewer2 round 1)

All six findings accepted.

1. **Redirects.** Set `AllowAutoRedirect = false`. Follow 301/302/303/307/308
   by hand, at most 5 hops. Every hop's absolute URL must be https and on
   the §8 host allow-list, otherwise `check_failed` / `download_failed`.
   This applies to the API call, the archive and the checksum.
2. **No links in update archives.** Reject the whole archive if it
   contains any symbolic link, hard link, device, FIFO or other non-regular,
   non-directory entry (phase 4 ships none). Then, before launching,
   `install.sh` must be a regular file: `FileInfo.LinkTarget == null`, and
   its full path must lie under the extraction directory after
   `Path.GetFullPath`. Extract into a freshly created, empty directory:
   delete any previous `…/update/<ver>/` first.
3. **Exec escaping, two stages.** First apply Exec quoting: inside
   `"…"`, prefix `"`, `` ` ``, `$` and `\` with `\`, and turn `%` into
   `%%`. Then escape the string value: double every `\`. So `$` is
   written as `\\$` and `\` as `\\\\`. `Unquote` reverses both stages
   exactly. Characters that cannot be represented (newline, CR, tab,
   control characters) mean autostart cannot be enabled: `Set(true)`
   returns or reports failure, and logs it.
4. **Exact match.** `IsEnabled` requires the Exec value to be exactly one
   quoted (or unquoted, no-space) token equal to the exe. Trailing
   whitespace is allowed; any extra argument means not enabled.
5. **Tray host check.** Before hiding, check that a StatusNotifier host
   exists: run `busctl --user --no-pager status org.kde.StatusNotifierWatcher`
   through `ICommandRunner`. Exit 0 means available. A missing `busctl`,
   a timeout or a non-zero exit means unavailable, so use the fallback
   (taskbar + minimise). Put this in `SysMonitor.Linux`
   (`TrayHost.IsAvailable(runner)`) so it can be tested.
6. **Testable seams.** Expose the code that tests need:
   - `Restart.BuildStartInfo(string processPath, IReadOnlyList<string> args)`,
     used by the real restarter;
   - an `UpdateAutoCheck` class in `SysMonitor.Linux`, holding the
     start-up check, the 60 s retry and the "setting still on" check,
     with an injectable delay function and an `IUpdater`.
     `ShellWindow` calls it and keeps no copy of the logic.
