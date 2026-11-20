# Handoff — R-027 phase 4: packaging, install/uninstall, docs

From: Coding Owner (Claude Code) · To: Writing Owner 2 (opencode, `opencode-go/deepseek-v4.1-flash`)
Branch: `linux-native` at `7051275` · Date: 2026-11-20
Requirement: `Requirements.md` R-027 ("แพ็กเกจแบบติดตั้งต่อผู้ใช้ ไม่ต้องใช้ root")

## Objective

Ship the Linux app as a per-user tar.gz that needs no root, with
install and uninstall scripts that honour the phase 3 updater contract.
Then bring the docs in line with the Linux-only branch.

## 1. Build script `build-linux.sh` (repo root, POSIX `sh`, `set -eu`)

- Usage: `./build-linux.sh [--arch x64|arm64] [--framework-dependent]`.
  The default is `x64`, self-contained.
- Version: read `<Version>` from `SysMonitor.Linux.App/SysMonitor.Linux.App.csproj`.
- Run `dotnet publish SysMonitor.Linux.App -c Release -r linux-<arch>`
  with `--self-contained true`, or `false` with `--framework-dependent`.
  Use no single-file, and `-p:DebugType=none`. Publish into
  `dist/stage/`.
- Archive layout. The archive is **flat**: `install.sh` sits at the
  root, as the phase 3 updater requires.

  ```
  install.sh
  uninstall.sh
  systemmonitor.desktop        (template; install.sh fills in Exec)
  icons/systemmonitor.png      (256×256)
  app/…                        (publish output, `app/sysmonitor` is the binary)
  VERSION                      (e.g. 4.3.0)
  ```
- **No links of any kind in the archive.** The updater rejects them.
  After staging, fail the build if `find dist/stage -type l` prints
  anything. Use `tar --format=pax --owner=0 --group=0 --numeric-owner`
  with a sorted file list (`--sort=name`) for reproducible output.
- Outputs:
  - `dist/SystemMonitor-<ver>-linux-<arch>.tar.gz`;
  - `dist/SystemMonitor-<ver>-linux-<arch>.tar.gz.sha256`, holding
    `<hex>  <file name>`, which is what `sha256sum` prints.
- Icon: the repo has only `SysMonitor.ico`. Add a committed
  `Assets/systemmonitor.png` at 256×256, extracted from the `.ico`. Use
  a one-off method of your choice: a tiny .NET snippet that writes the
  largest ICO frame, or a tool if one is installed. Report the method.
  The build only copies the PNG.
- `dist/` goes in `.gitignore`.

## 2. `install.sh` (POSIX `sh`, `set -eu`)

- Refuse to run as root (`id -u` = 0): print a message and exit 1.
- Locations, honouring XDG:
  - app: `${XDG_DATA_HOME:-$HOME/.local/share}/SystemMonitor/app/`;
  - launcher: `$HOME/.local/bin/sysmonitor`, a symlink to the app
    binary. The installer creates it; it is not in the archive;
  - menu entry: `${XDG_DATA_HOME:-…}/applications/systemmonitor.desktop`,
    with `Exec="<app>/sysmonitor"` escaped by the same two-stage rule as
    phase 3 (`Docs/HANDOFF-R027-phase3.md` Amendment 2 item 3). Refuse
    an install path containing a newline;
  - icon: `${XDG_DATA_HOME:-…}/icons/hicolor/256x256/apps/systemmonitor.png`.
- Install atomically:
  1. copy `app/` to `SystemMonitor/app.new`;
  2. `mv app app.old` if present;
  3. `mv app.new app`;
  4. `rm -rf app.old`.

  If step 3 fails, move `app.old` back. Set `chmod 755` on
  `app/sysmonitor`.
- Run `update-desktop-database` and `gtk-update-icon-cache` only if
  installed, and ignore their failures.
- `--update --wait-pid N`:
  - N must be numeric;
  - poll `kill -0 N` every 0.2 s for up to 30 s, then install;
  - after installing, start the new app detached:
    `setsid "<app>/sysmonitor" >/dev/null 2>&1 &`, or
    `nohup … &` when `setsid` is missing;
  - if the PID does not exit, exit 1 and change nothing.
- Without `--update`: install, then print where everything went and
  how to start it. Do not start the app.
- Never touch `~/.config/SystemMonitor/` (settings) or
  `~/.config/autostart/`. The autostart entry points at
  `<app>/sysmonitor`, a path that stays the same across updates.

## 3. `uninstall.sh`

- Refuse to run as root.
- Remove:
  - the app dir;
  - the launcher, but **only if** it is a symlink into our app dir;
  - the menu entry;
  - the icon;
  - our autostart file, but **only if** its `Exec` points into our app
    dir.
- Keep the settings unless `--purge` is given, which also removes
  `~/.config/SystemMonitor` and `~/.local/state/SystemMonitor`.
- Never use `rm -rf` on a path built from an empty or unset variable.
  Guard every path:
  `case "$p" in "$HOME"/*) ;; *) exit 1;; esac`.

## 4. Docs (English, matching the existing style)

- `README.md`: rewrite for the Linux edition. Cover:
  - what it is and the three views;
  - what it reads on Linux (sources: `/proc`, `/sys`, hwmon, DRM
    fdinfo, `nvidia-smi`, systemd);
  - known gaps on Linux: memory modules/slots need root; no Intel GPU
    temperature; USB enclosure temperatures; r8152 link speed; no
    extensions;
  - requirements: x64/arm64 and glibc. Self-contained needs no .NET;
    the framework-dependent build needs the .NET 10 runtime;
  - build, install, uninstall and update;
  - settings location and the log path;
  - a **Windows 4.3.0 vs Linux comparison table** (feature → Windows →
    Linux).

  Keep the top banner saying the Windows app is on `main`.
- `Docs/ARCHITECTURE.md`: update the project list (`SysMonitor.Core`,
  `SysMonitor.Linux`, `SysMonitor.Linux.App`, `SysMonitor.Linux.Probe`,
  `SysMonitor.Linux.Tests`), the dependency rules (Avalonia only in
  `SysMonitor.Linux.App`; why it is the one package exception on this
  branch), and the data flow (sampler → snapshot → presentation models →
  views). Remove or mark the Windows-only sections as living on `main`.
- `Docs/TESTING.md`: the Linux commands (build, test, `--probe-ui`,
  `--probe-update`, `SysMonitor.Linux.Probe`, `build-linux.sh`, the
  install-script tests). Keep the policy text that still applies.
- `CHANGELOG.md`: add at the top
  `## Linux 4.3.0 — 2026-11-20 — Native Linux edition (R-027)`,
  summarising phases 1b–4. Leave the Windows entries untouched.
- Do not edit `Requirements.md`. The Coding Owner updates R-027.

## Acceptance criteria

1. Run `./build-linux.sh` once. The self-contained build needs the
   `linux-x64` runtime pack from nuget.org, which is not in the local
   cache; that one restore is allowed. Paste:
   - the archive file list (`tar -tzf … | head -40` and the entry
     count);
   - `find dist/stage -type l | wc -l` = 0;
   - the archive size;
   - `sha256sum -c` passing.
2. Test the install, update and uninstall scripts **only against a
   sandbox**. Use `HOME=$SANDBOX XDG_DATA_HOME=$SANDBOX/.local/share
   XDG_CONFIG_HOME=$SANDBOX/.config`, with `SANDBOX` under
   `/tmp/claude-1000/…/scratchpad/` or a `mktemp -d`. Show:
   - after install, the installed tree;
   - the `.desktop` file text;
   - after uninstall, that settings remain;
   - with `--purge`, that they are gone;
   - `--update --wait-pid` with a dead PID (e.g. a finished
     `sh -c true`). Here, replace the final launch by setting
     `SYSMONITOR_NO_LAUNCH=1`, an env var the script honours **only** to
     skip the launch;
   - a refusal with a non-numeric PID.
3. `shellcheck` on the three scripts if it is installed; say so if it
   is not.
4. App build 0 warnings. `dotnet test SysMonitor.Linux.Tests` passes.
5. **Never** run the scripts against the real `$HOME`. Never launch the
   app. No screenshots. No push, no GitHub release.

## Scope and constraints

- Allowed files: `build-linux.sh`, `packaging/linux/{install.sh,uninstall.sh,systemmonitor.desktop}`
  (the build copies them to the archive root), `Assets/systemmonitor.png`,
  `.gitignore`, `README.md`, `Docs/ARCHITECTURE.md`, `Docs/TESTING.md`,
  `CHANGELOG.md`.
- Do not touch the C# projects, except the csproj `<Version>` if it is
  missing, or the tests.
- Do not commit, push or create branches.

## Report back

```
## Summary
## Changed
## Verification   (build output, archive list, sandbox transcript)
## Deviations / Unclear
## Open questions / risks
```

## Amendment 1 (Coding Owner, after the first report)

Accepted: deviations 1–5 and 7–9.

1. **Keep an uninstaller after install.** `install.sh` copies
   `uninstall.sh` to `${XDG_DATA_HOME:-…}/SystemMonitor/uninstall.sh`,
   outside `app/`, so the atomic swap does not touch it, with mode 755.
   The script must work when run from that location: it locates the
   install by XDG, not by its own path. When run from there, it also
   removes itself and the then-empty `SystemMonitor/` directory. README
   documents the command
   `~/.local/share/SystemMonitor/uninstall.sh`.
2. **Same boundary for install and uninstall.** `install.sh` refuses
   (exit 1, with a message) an `XDG_DATA_HOME` or `XDG_CONFIG_HOME` that is
   not under `$HOME`, so nothing can be installed that the uninstaller
   would refuse to remove. Update `ARCHITECTURE.md` §12 to match.

## Amendment 2 (Coding Owner, after Reviewer2 round 1)

All three findings are accepted.

1. **Physical path boundary.** Both scripts share the same helper
   (duplicate it in both, because each must stand alone):
   - Reject any `XDG_*` path or `HOME` containing a `..` or `.`
     component, or a newline.
   - `home_real=$(cd -P -- "$HOME" && pwd -P)`.
   - For each target directory, resolve its nearest existing ancestor
     with `cd -P … && pwd -P`, append the remaining components, and
     require the result under `"$home_real"/`.
   - Run the check again **immediately before** every `rm -rf` and
     every `mv`, on the parent, and refuse if the target itself is a
     symlink, except the launcher symlink, which is removed with
     `rm -f`, never `-r`.
   - `app.new` / `app.old`: if either exists as a symlink or a non-directory,
     refuse.
2. **Interrupted update is repaired, not claimed atomic.**
   - `install.sh` runs a recovery step first. If `app/` is missing and
     `app.old/` exists, move `app.old` back to `app`. Then remove any
     stale `app.new`.
   - After that, swap as before.
   - Change README and ARCHITECTURE to say exactly this: "the swap is two
     renames; if it is interrupted, running `install.sh` again (or the
     next update) restores the previous version". Remove any claim of
     atomicity.
3. **README install commands.** Use
   `mkdir SystemMonitor && tar -xzf SystemMonitor-<ver>-linux-x64.tar.gz -C SystemMonitor && cd SystemMonitor && ./install.sh`.
   Check every other command in README and TESTING against the real
   archive layout and script flags.

## Amendment 3 (Coding Owner, after Reviewer2 round 2)

1. **No fixed staging names.** Every staged file (the uninstaller copy,
   the menu entry, the icon, and any other temp file) is created with
   `mktemp "<dest dir>/.<name>.XXXXXX"`. `mktemp` creates a new file with
   O_EXCL, so it can never follow a planted link. Write into it, `chmod`
   it, run the boundary check on the destination, then `mv -f` it onto the
   final name. If the final name exists and is a symlink or a directory,
   refuse. Remove the temp file on every failure path (`trap`).
2. **Rollback `mv`.** Run the same parent and symlink check before it.
   Check its exit status. On failure, print that the restore failed and
   where `app.old` is, and exit non-zero. Never print "restored" unless
   the restore succeeded.

## Amendment 4 (Coding Owner, after Reviewer2 round 3): test hooks

Add two test-only environment hooks to `install.sh`, documented in a
comment next to `SYSMONITOR_NO_LAUNCH`:
- `SYSMONITOR_TEST_FAIL_SWAP=1` makes the forward `mv app.new app` fail.
  Treat it as if `mv` returned non-zero, so the real rollback runs.
- `SYSMONITOR_TEST_FAIL_RESTORE=1` makes the rollback `mv` fail in the
  same way.

Each hook replaces only that one `mv`'s result; the rest of the path is
real. Nothing else reads them.
