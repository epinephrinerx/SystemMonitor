# Handoff — R-030 B2 hands-on fix 1: Battery in Overall and Widget

From: Coding Owner · To: Writing Owner 2. Branch `linux-native` at `509ba53`.
User report (2026-11-21, hands-on): "ในหน้า overall แสดงผลไม่ครบตาม sidebar".
Cause: `OverallBoard` builds CPU, RAM, Disk, GPU, Network only; R-028 also
requires a Battery row in Overall and a Battery page in the Widget rotation
(Requirements R-028 "คุณเลือก"), and neither exists.

1. `OverallBoard`: after Network, when `config.ShowBattery` and
   `snapshot.Batteries` is non-empty, add a **Battery** section: one meter row
   per battery (title `Battery` or the model, bar = percent, value `NN%`,
   badge: ⚡ when charging/on AC, detail line: state · power W · time to
   empty/full · health %, values that are null left out). Peripheral batteries
   as small rows below (kind · model · percent) when present.
2. `WidgetBoard`: add a Battery page (`WidgetPageKind` value if needed) to the
   rotation under the same conditions, styled like the other pages.
3. Colours from `Accents.Battery`; strings Thai + English in the B1 table.
4. `--probe-ui`: Overall and Widget sections list the Battery section/page.

Do not edit `SysMonitor.Linux.Tests/`; never launch the GUI (only `--probe-ui`);
scratch only under `$HOME/.cache/sysmon-wo2/`. Build + `dotnet test`; report
files changed, commands, results, tests to add. No commit.
