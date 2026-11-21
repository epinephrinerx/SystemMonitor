# Handoff — R-030 B2 hands-on fix 3: battery badge without a thermometer

From: Coding Owner · To: Writing Owner 2. Fix 1 reused `TempBadge` for the
⚡ charging badge, so the view draws the thermometer glyph next to ⚡.

1. Give the badge model a kind (e.g. `BadgeKind.Temperature` default,
   `BadgeKind.Power`) or a separate `PowerBadge`; the views (`RowRenderer`,
   `WidgetView`, `OverallView`, wherever badges are drawn) draw the thermometer
   only for temperature badges. A power badge shows a plug/lightning **icon from
   the existing icon set** (`IconName` — pick the closest; add one only if none
   fits) plus no text, in `Accents.Battery`. Do not rely on the ⚡ text glyph
   (missing from IBM Plex Sans Thai).
2. Keep temperature badges exactly as today.
3. `--probe-ui` reports the badge kind.

No test edits; never launch the GUI; scratch only under `$HOME/.cache/sysmon-wo2/`.
Build + `dotnet test`; report files changed, commands, results. No commit.
