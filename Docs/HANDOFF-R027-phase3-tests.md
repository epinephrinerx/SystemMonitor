# Handoff — R-027 phase 3 tests

From: Coding Owner · To: Test Author (agy, Gemini 3.8 Flash)

Add tests to `SysMonitor.Linux.Tests` for phase 3. The behaviour is set by
`Docs/HANDOFF-R027-phase3.md` (including its Amendment) and
`Docs/WPF-UI-SPEC-4.3.0.md` §8–§9. The Writing Owner is applying the
Amendment in parallel. Read `SysMonitor.Linux/Presentation/Autostart.cs`,
`Restart.cs`, `TrayText.cs`, `Updater.cs`, `Integrations.cs` and
`StartupOptions.cs` for the exact names before you write anything.

Cover:
- **Autostart** under a fake config root:
  - the written file's content;
  - Exec quoting and escaping of `"`, `` ` ``, `$`, `\` and `%` (as `%%`);
  - round-trip unquote;
  - `IsEnabled` only when Exec equals the current exe;
  - `Hidden=true` means disabled;
  - disable deletes only our file and leaves the other `.desktop` files;
  - honours `XDG_CONFIG_HOME`.
- **TrayText:**
  - "SysMonitor" before the first data;
  - CPU/RAM percentages;
  - the temperature suffix with the `~` modelled marker.
- **Restart:** the start info uses `ArgumentList` with the original args
  unchanged, and the exe is `ProcessPath`. Never start a real process.
  Use the abstraction or inspect the built `ProcessStartInfo`.
- **Updater** with a fake `HttpMessageHandler`:
  - check: `v`-prefix trimming; newer, older and same version;
    User-Agent and Accept headers; asset and checksum name per arch;
    missing asset, missing checksum or a non-allow-listed host means
    `check_failed`; non-https is rejected; retry once after the
    (fake-clock) 60 s when the setting is still on;
  - download: progress is reported; a checksum mismatch means
    `download_failed` and the file is deleted; a partial file is deleted
    on failure;
  - extraction: build `.tar.gz` fixtures in memory with
    `System.Formats.Tar`. Reject an absolute path, a `..` entry, a
    symlink pointing outside, and a device or FIFO entry. A missing
    `install.sh` means failure;
  - the install launch argv is
    `/bin/sh <dir>/install.sh --update --wait-pid <pid>`, through a fake
    process starter. Never run anything.

Use temp directories only, and never the real `~/.config` or `~/.cache`.
No network. Files only, no shell, only in `SysMonitor.Linux.Tests/`. Do
not commit.

## Round 2 (after Reviewer2 round 1)

Use the real production seams from Amendment 2 in `Docs/HANDOFF-R027-phase3.md`:
- **Redirects:** use a fake handler. Assert:
  - a 302 from an allowed host to a non-allowed host fails;
  - a 302 to `http://` fails;
  - 6 hops fail;
  - an allowed → allowed redirect succeeds.
  Apply these to the check, the archive and the checksum.
- **Archive:** add one test per entry kind: symlink, hardlink, char/block
  device and FIFO. Each rejects the archive. Include the
  `a -> .` + `a/install.sh -> ../outside.sh` case. A previous extraction
  directory is cleared first.
- **Exec:** round-trip paths containing `$`, `` ` ``, `"`, `\`, `%` and a
  space. Assert the exact file text for `$` (`\\$`) and `\` (`\\\\`).
  Make the `%` test strict. A path containing a newline cannot be
  enabled.
- **IsEnabled:** `Exec="<exe>" --probe-update` means not enabled; the
  quoted exe with trailing spaces means enabled.
- **TrayHost.IsAvailable:** a fake runner returning exit 0 means
  available. A non-zero exit, a thrown exception or a timeout means not
  available. Assert the argv.
- **Rewrite:** `RestartTests` must use `Restart.BuildStartInfo`, and the
  retry tests must use `UpdateAutoCheck` with a fake delay. Delete the
  copied logic.

Use fakes and temp directories only. Files only, no shell, only in
`SysMonitor.Linux.Tests/`.

Coding Owner off-role test fixes, round 2:
- `AutostartTests.cs:72`: a verbatim string used `\"`. Changed it to a
  regular string.
- `UpdaterTests.cs:1329`: the fake handler matched the redirect target
  (`…/download/file.sha256`) as the redirect source, which looped past
  5 hops. The branch now excludes the target.
