# Handoff — R-027 release prep: .deb package and Linux-only release lookup

From: Coding Owner (Claude Code) · To: Writing Owner 2 (opencode, `opencode-go/deepseek-v4.1-flash`)
Branch: `linux-native` · Date: 2026-11-20

## Context (user instruction, 2026-11-20)

> ส่วนงานเดิม ถ้าเสร็จแล้วให้ commit, build.deb, push บน branch linux-native ได้เลยครับ, release ใน tag linux

The repository's GitHub releases are shared with Windows. `v4.3.0`
(Windows) is **Latest**. The Windows updater reads `/releases/latest`
and looks for `SystemMonitor-Setup-*.exe`. The Linux release will
therefore be published with `--latest=false` under the existing Linux tag
convention `SystemMonitorLinux-<ver>` (the earlier one is
`SystemMonitorLinux-3.2.0`). That means the Linux updater **must not**
read `/releases/latest`.

## 1. Linux updater looks up Linux releases only

- Endpoint: `GET https://api.github.com/repos/epinephrinerx/SystemMonitor/releases?per_page=30`.
  Keep the same headers and timeout. Keep the manual-redirect host
  allow-list.
- Consider only releases where all of these hold:
  - `tag_name` starts with `SystemMonitorLinux-`;
  - `draft` is false;
  - `prerelease` is false.
- The version is `tag_name` minus that prefix, parsed as
  Major.Minor.Build. Pick the highest one. Ignore everything else,
  including Windows `v*` tags and the Mac tags.
- The asset rules are unchanged: `SystemMonitor-<ver>-linux-<arch>.tar.gz`
  plus `.sha256`. For a `.deb` install, see §3.
- No Linux release found means `up_to_date`, **not** `check_failed`,
  because no Linux release exists yet. A non-200 status, a parse error or
  a network failure still means `check_failed`.
- `--probe-update` prints which tag it picked and why.

## 2. `.deb` package (`build-linux.sh --deb`)

- The `--deb` flag makes the build also produce
  `dist/systemmonitor_<ver>_<debarch>.deb`, where `debarch` is `amd64`
  or `arm64`, plus a `.sha256` file in the same `sha256sum` format.
  It reuses the same self-contained publish output.
- Layout:
  - `/opt/systemmonitor/` holds the publish output; the binary is
    `/opt/systemmonitor/sysmonitor`;
  - `/usr/bin/sysmonitor` is a symlink to it (fine in a `.deb`);
  - `/usr/share/applications/systemmonitor.desktop` with
    `Exec=/opt/systemmonitor/sysmonitor` (no field codes),
    `Icon=systemmonitor`, `Categories=System;Monitor;`;
  - `/usr/share/icons/hicolor/256x256/apps/systemmonitor.png`;
  - `/usr/share/doc/systemmonitor/copyright` in DEP-5 format. Cover the
    program (same licence as the repo; check `LICENSE` and say what you
    found) and the bundled IBM Plex Sans Thai fonts (OFL-1.1).
- `DEBIAN/control`:
  - `Package: systemmonitor`, `Version: <ver>`, `Architecture`,
    `Section: utils`, `Priority: optional`, `Installed-Size`;
  - `Maintainer` taken from `git config user.name`, with a
    `noreply` address: use the GitHub-noreply form
    `epinephrinerx@users.noreply.github.com`. **Never** put a personal
    email address in the package;
  - `Description`;
  - `Depends`: work it out with `dpkg-shlibdeps` on the native `.so`
    files plus the .NET runtime needs (`libc6`, `libgcc-s1`,
    `libstdc++6`, `zlib1g`, ICU unless `InvariantGlobalization` is set,
    OpenSSL for HTTPS, `libfontconfig1`, and `libx11-6`, `libice6`,
    `libsm6` for Avalonia X11). Report the final line and how you got
    it. On this machine (Ubuntu "resolute") some libraries have `t64`
    names; use alternatives (`libssl3t64 | libssl3`) where the names
    differ between releases.
- `DEBIAN/postinst` and `postrm`: refresh the desktop database and icon
  cache only if those tools exist. Nothing else: no user files, no
  services.
- Build with `dpkg-deb --root-owner-group --build`. Permissions: 755 on
  directories and on the binary, 644 on everything else.
- Check it with `dpkg-deb --info` and `dpkg-deb --contents`. Run
  `lintian` if it is installed; it is not on this machine, so say so.

## 3. Updater behaviour for a `.deb` install

- The app counts as a `.deb` install when `Environment.ProcessPath`
  resolves under `/opt/systemmonitor/` **and** `dpkg-query -W -f='${Status}' systemmonitor`
  (through `ICommandRunner`) reports `install ok installed`.
- Then the update looks for `systemmonitor_<ver>_<debarch>.deb` plus
  `.sha256` in the chosen Linux release.
- Download with the same manual-redirect and allow-list code, and check
  the SHA-256.
- Save the file to `$XDG_DOWNLOAD_DIR`, falling back to `~/Downloads`
  (from `xdg-user-dir DOWNLOAD` if present).
- Open it with `xdg-open <file>` through `ProcessStartInfo.ArgumentList`.
  The desktop's package installer (Discover or GNOME Software) then asks
  for authentication. Show the new status string `update_deb_opened`
  (Thai + English). **Never** run `pkexec`, `sudo`, `apt` or `dpkg -i`
  ourselves.
- Per-user tar.gz installs keep the phase 3 `install.sh --update` path.

## 4. Docs

- README: a ".deb" install section with
  `sudo apt install ./systemmonitor_<ver>_amd64.deb` and how to remove
  it, how updates work for each install type, and the release tag
  convention.
- TESTING: the `--deb` build commands.
- CHANGELOG: add a line to the Linux 4.3.0 entry.

## Acceptance

1. Build 0 warnings. `dotnet test` passes. Do not edit the tests, except
   for a compile break; the Test Author covers the new logic.
2. Run `./build-linux.sh --deb` and paste `dpkg-deb --info`, the
   `--contents` listing (first 30 lines plus a count), and
   `sha256sum -c` for both artefacts.
3. Run `--probe-update` once (a real GET of `/releases`). Expected result:
   no `SystemMonitorLinux-*` release newer than 4.3.0 means up to date;
   3.2.0 is a prerelease and is ignored anyway.
4. **Never** install the `.deb` (`dpkg -i`, `apt install`). Never use
   `sudo`. Never launch the GUI. Never create a tag or release. Never
   push or commit.

## Scope

`SysMonitor.Linux/Presentation/Updater.cs` (and closely related
presentation files), `StringData`, `build-linux.sh`, `packaging/linux/**`
(add `packaging/linux/deb/`), `README.md`, `Docs/TESTING.md`,
`CHANGELOG.md`.

## Amendment 1 (Coding Owner, after Reviewer2 round 1)

All four findings are accepted.

1. **`build-linux.sh:59`.** `--deb` together with `--framework-dependent`
   must fail. Print a clear error and exit 2. The `.deb` is always
   self-contained.
2. **`Updater.cs:655`.**
   - Never overwrite or delete a file the app did not create in the
     Downloads directory.
   - If `systemmonitor_<ver>_<arch>.deb` already exists there **and**
     its SHA-256 matches, reuse it.
   - Otherwise save under a free name: `systemmonitor_<ver>_<arch> (1).deb`,
     `(2)`, …
   - On failure, delete only the file this run wrote.
3. **`Updater.cs:1057`.** Wait for `xdg-open` to exit, for up to 10 s.
   - Exit code 0 → `update_deb_opened`.
   - Non-zero exit, or `xdg-open` missing → `download_failed`, keeping the
     downloaded file (it is valid, because the SHA has been checked).
     Show its path in the status text so the user can open it by hand:
     add string `update_deb_saved` (Thai + English) with a `{0}` path
     placeholder.
   - Timed out → treat it as opened, because some handlers block.
4. **`Updater.cs:143`.** The tag suffix must be exactly
   `Major.Minor.Build`: digits only, no `v`, nothing after it. Any other
   tag is ignored.

Scope and rules are unchanged: no commit, no GUI, never install.
