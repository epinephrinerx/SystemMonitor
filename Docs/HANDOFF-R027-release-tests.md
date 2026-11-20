# Handoff — R-027 release: tests for Linux-only release lookup and .deb update path

From: Coding Owner · To: Test Author (agy, gemini-3.8-flash) · 2026-11-20
Code: `SysMonitor.Linux/Presentation/Updater.cs`, `Integrations.cs`, `ShellModel.cs`
(see `Docs/HANDOFF-R027-release.md` for the contract).

1. `SysMonitor.Linux.Tests/UpdaterTests.cs`: 7 tests still stub
   `/releases/latest` with one release object. Move them to the new
   contract: `GET /releases?per_page=30` returning a JSON **array**, with
   selection via `UpdateReleaseParser.Select`. Keep what each test was
   checking (asset names, sha256, redirects, allow-list, failures).
   After that, the single-release `Parse` helper must have no callers
   in the tests.
2. New tests for release selection:
   - Windows `v*` and `SystemMonitorMac-*` releases are ignored, and so are
     draft and prerelease releases.
   - The highest version wins, compared numerically: 4.10.0 > 4.9.0.
   - No Linux release → `up_to_date`.
   - A non-200 response or malformed JSON → `check_failed`.
   - The newest Linux release without this architecture's asset →
     `check_failed`.
3. New tests for the .deb path:
   - The asset name is `systemmonitor_<ver>_<debarch>.deb`, with x64 →
     amd64 and arm64 → arm64.
   - The install is detected as .deb only when the path is under
     `/opt/systemmonitor/` **and** dpkg-query reports `install ok installed`
     through a fake `ICommandRunner`.
   - The download directory is `XDG_DOWNLOAD_DIR` when set, and falls back
     otherwise.
   - A SHA mismatch → the file is removed and the update fails.
   - The opener is called with argv `xdg-open <file>` and never with
     sudo, pkexec, apt or dpkg.
4. Use MSTest only (`Assert.AreEqual`, not `Assert.Equal`). No network,
   no real processes. Do not commit.

## Coding Owner note

3 new tests used guessed English texts ("Package installer opened",
"Current version is up to date", "Update check failed"). I replaced them
with the real StringData values (off-role fix). The suite then passed
452/452, twice.

## Round 2 (after Amendment 1 in HANDOFF-R027-release.md)

Add tests for these cases:

- `SystemMonitorLinux-v4.4.0` and `SystemMonitorLinux-4.4.0-rc1` are
  ignored, and the valid `4.3.1` is chosen.
- An existing Downloads file with a matching SHA is reused. With a
  different SHA, the new file is saved as `… (1).deb` and the original is
  untouched.
- `xdg-open` exit code 0 → opened. Exit code 3 → failed, the file is
  kept, and the status shows its path.
- A failed download deletes only the file it created.
