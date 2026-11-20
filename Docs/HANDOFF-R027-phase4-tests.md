# Handoff — R-027 phase 4 tests

From: Coding Owner · To: Test Author (agy, Gemini 3.8 Flash)

Add `SysMonitor.Linux.Tests/InstallScriptTests.cs` (MSTest). It runs
`packaging/linux/install.sh` and `uninstall.sh` through `/bin/sh`.

**Sandbox.** Every test runs in a fresh temp directory. Build a fake
extracted archive there:
- `app/sysmonitor`: a tiny `#!/bin/sh\nexit 0` file;
- `icons/systemmonitor.png`: any bytes;
- `systemmonitor.desktop`;
- `VERSION`;
- the two scripts, copied from the repo. Find the repo root by walking
  up from `AppContext.BaseDirectory` to the directory that contains
  `packaging/`.

Run the scripts with `ProcessStartInfo.ArgumentList` and an environment
holding `HOME`, `XDG_DATA_HOME`, `XDG_CONFIG_HOME` and
`XDG_STATE_HOME`, **all set to the sandbox**, plus
`SYSMONITOR_NO_LAUNCH=1`. Clear any inherited `XDG_*`. Never use the
real `HOME`. Never run the real app.

Behaviour: `Docs/HANDOFF-R027-phase4.md`, including Amendment 1. The
Writing Owner is applying the Amendment in parallel.

Cover:
- **Install:**
  - the tree: app, launcher symlink, menu entry, icon, and the
    uninstaller copy;
  - the `.desktop` `Exec` line, two-stage escaped, for a sandbox path
    containing a space and `$`;
  - the binary is executable;
  - re-installing replaces the app and leaves no `app.new` or `app.old`.
- **Refusals:**
  - an `XDG_DATA_HOME` outside `HOME`;
  - a relative `XDG_DATA_HOME`;
  - a non-numeric `--wait-pid`.
- **`--update --wait-pid <dead pid>`** installs. Get a dead PID from a
  process you started and awaited.
- **Uninstall:**
  - it keeps the settings;
  - `--purge` removes them;
  - it removes only our autostart file, not another `.desktop` file;
  - it leaves a launcher that is not our symlink in place;
  - run from the installed copy, it removes itself and the
    `SystemMonitor/` directory.

Every process wait has a 30 s timeout. Files only, no shell, only in
`SysMonitor.Linux.Tests/`. Do not commit.

## Round 2 (after Reviewer2 round 1)

Add tests to `InstallScriptTests.cs` (sandbox only):
- `XDG_DATA_HOME=$HOME/../outside` → refused, nothing is written
  outside. The same for the uninstaller.
- `XDG_DATA_HOME` under `HOME` through a symlinked parent that points
  outside `HOME` → refused by both scripts. Create the symlink inside
  the sandbox, pointing at a sibling sandbox directory.
- A pre-existing `app.new` that is a symlink → install refuses, and the
  symlink target is untouched.
- Recovery: state `app/` missing and `app.old/` present → `install.sh`
  ends with a working `app/`, and with no `app.old` or `app.new`.
- Uninstall never removes the target of a launcher symlink.

## Round 3 (after Reviewer2 round 2)

For each staged destination (`SystemMonitor/uninstall.sh`, the menu
`.desktop`, the icon), pre-plant two things in the sandbox:
- a symlink at the old fixed staging name (e.g.
  `.uninstall.sh.new`) pointing at a sentinel file **outside** the fake
  `HOME`, but still inside the sandbox;
- a symlink at the final name pointing outside.

Assert:
- the sentinel's content is unchanged;
- for the final-name symlink, install refuses (non-zero exit);
- no `.<name>.*` temp files are left behind.

## Round 4 (after Reviewer2 round 3)

Use the Amendment 4 hooks. Start from an existing install, then reinstall:
- With `SYSMONITOR_TEST_FAIL_SWAP=1`:
  - exit is non-zero;
  - `app/` is the **previous** version (put a marker file in it
    beforehand and check it is there);
  - no `app.old` is left;
  - the output says the previous version was restored.
- With `SYSMONITOR_TEST_FAIL_SWAP=1` and `SYSMONITOR_TEST_FAIL_RESTORE=1`:
  - exit is non-zero;
  - `app.old` still holds the previous version (check the marker);
  - the output says the restore failed and names `app.old`;
  - the output never says "restored".
