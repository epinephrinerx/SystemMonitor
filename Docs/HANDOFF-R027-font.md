# Handoff — R-027 follow-up: one bundled UI font for Thai and Latin

From: Coding Owner (Claude Code) · To: Writing Owner 2 (opencode, `opencode-go/deepseek-v4.1-flash`)
Branch: `linux-native` at `965c0f2` · Date: 2026-11-20

## Problem (user report, first hands-on test)

> ขนาด font ยังดูแปลกๆ ครับ น่าจะเพราะใช้ font ระบบ ซึ่งไทยและละตินคนละแบบกัน

`Ui.FontStack` in `SysMonitor.Linux.App/Controls.cs:23` is
`"Noto Sans, Noto Sans Thai, Ubuntu, sans-serif"`. Latin text renders in
Noto Sans and Thai falls back to Noto Sans Thai. The two have different
x-heights and line metrics, so mixed Thai and Latin text looks uneven.
The result also depends on whichever fonts a user happens to have
installed.

## Decision

Bundle **IBM Plex Sans Thai** (SIL OFL-1.1) and use it as the primary UI
family. It contains matching Latin and Thai glyphs, plus `↓ ↑ ° · … ≈ ~`.
This is the Linux counterpart of Windows' Segoe UI + Leelawadee UI pair.

1. Copy these files from `/usr/share/fonts/truetype/ibm-plex/` into
   `SysMonitor.Linux.App/Assets/Fonts/`:
   - `IBMPlexSansThai-Regular.ttf`;
   - `IBMPlexSansThai-SemiBold.ttf`;
   - `IBMPlexSansThai-Bold.ttf`.

   Add the OFL text as `Assets/Fonts/OFL.txt`. Take it from the
   `fonts-ibm-plex` copyright file, or the standard OFL 1.1 text with
   IBM's copyright line.
2. Include them as `<AvaloniaResource>` and reference them as
   `avares://sysmonitor/Assets/Fonts#IBM Plex Sans Thai` (check the
   assembly name and the family name with `fc-query`). The stack is then
   `"<that avares family>, Noto Sans, Noto Sans Thai, sans-serif"`. The
   system fonts are fallback for glyphs Plex lacks, e.g. `▲`.
3. Check every place that sets a `FontFamily`. All text uses
   `Ui.Family`, except the icon glyph font. Also check the Avalonia
   theme default (`App.axaml`, `FontFamily` on the application or
   `FluentTheme`), so that dialogs, menus, tooltips, `TextBox` and the
   tray menu inherit it. No text may still use the bare system default.
4. **Weights.** The spec uses SemiBold and Bold. Make sure they map to
   the bundled SemiBold and Bold files and are not synthesised.
5. **Vertical metrics.** Plex Thai has taller ascenders and descenders
   for Thai marks. Check that the type ramp (spec §1: 9/10/11/14/15 px)
   still fits the fixed-height rows in Widget (rotating line, badges)
   and Overall. Fix only real clipping, by adjusting `LineHeight` or
   padding where needed, and report what changed. Do not change the
   ramp sizes.
6. `--probe-ui` prints the resolved font family name and confirms the
   three faces loaded (e.g. by glyph typeface lookup).
7. README: add one line under credits/licences naming the bundled font
   and its OFL-1.1 licence.

## Acceptance

- App build 0 errors and 0 warnings. `dotnet test SysMonitor.Linux.Tests`
  passes.
- `--probe-ui` shows the bundled family and the three loaded faces.
- `./build-linux.sh` still produces a link-free archive, and the font
  files are inside `app/`. Paste the `tar -tzf … | grep -i plex` output.
- **Do not launch the GUI.** The only headless runs allowed are
  `--probe-ui` and `--probe-update`. No screenshots. Do not touch
  `~/.local/share/SystemMonitor`, because the user is running that
  install. Do not commit.

## Scope

`SysMonitor.Linux.App/**`, `README.md`.

## Amendment 1 (user feedback after the first look, 2026-11-20)

> ระยะระหว่างบรรทัดของ แต่ละเมนูใน sidebar ห่างเกินไปครับ และช่องตัวเลือกใหญ่ไป

1. **Line height back to the old metrics.** Plex Thai's natural line box
   is 1.65 em. Noto Sans was 1.36 em and Segoe UI is about 1.33 em. Set
   an explicit line height of **1.36 × font size** on every text block:
   - in `Ui.Text`;
   - in a default `TextBlock` style in `App.axaml`, unless a size-relative
     form exists there; otherwise put it in `Ui.Text` only, and in any
     other `TextBlock` construction you find.

   Check that Thai above and below marks (e.g. "ที่", "ปั๊ม", "ฤๅ")
   are not clipped. The text must sit vertically centred in its line,
   not top-cropped. Verify headless: measure a `TextLayout` with that
   `LineHeight` and report the ink bounds against the line box for
   "ที่ปั๊ม Ag".
   Then **revert the Widget margin change** (`WidgetView._scale.Margin`
   back to `16`, as spec §1 says), because the old vertical budget
   applies again. Re-check the four-lane CPU page arithmetic from your
   report with 1.36 em.
2. **Compact CheckBox and RadioButton.** Do not rely on Fluent template
   part names. Define our own `ControlTheme`s in `App.axaml` for
   `CheckBox` and `RadioButton`, applied app-wide:
   - **box:** 14×14, CornerRadius 3, 1 px border in the palette Muted
     colour at about 60 % opacity, transparent fill;
   - **checked:** fill and border `#3b82f6`, with a white tick `Path`
     about 9 px wide and 1.6 px stroke;
   - **radio:** a 14×14 circle; when checked, a `#3b82f6` ring with a
     6 px inner dot;
   - **layout:** content left margin 6, box top-aligned with the first
     text line (centred on that line's height), `MinHeight` 0, no extra
     padding;
   - **states:** hover border `#3b82f6`; disabled opacity 0.5;
     keyboard focus a 1 px `#3b82f6` outline;
   - **behaviour:** clicking the label still toggles.

   Keep the sidebar row margins at spec §4 (Check margin 0,3,0,3;
   Indented left 20). Check that the Settings window's checkboxes, the
   disk and alert options, and every `RadioButton` row use the new
   themes.
3. Do not launch the GUI. Build, test, run `./build-linux.sh`, and
   report. Do not commit.
