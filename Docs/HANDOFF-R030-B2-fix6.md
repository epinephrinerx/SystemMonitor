# Handoff — R-030 B2 fix 6: small cards at large font sizes

From: Coding Owner · To: Writing Owner 2. Reviewer2 (P2): `CardViews.cs:186`
puts a small card's title and value on one line in a fixed 146 px card (132 px
inner). At font size 110 % `C7` + `100% · 105°C · 1.2 GHz` no longer fits, at
160 % it needs ~200 px.

Fix: in small cards put the **title on the first line and the value on its own
second line** (value left-aligned or right-aligned, single line, ellipsis only
as a last resort), and let the card width grow with the font scale
(`146 × Ramp scale`, so the grid reflows to fewer columns) instead of a fixed
146 px. Keep the graph below. Applies to every small card that uses this
layout (cores, GPU engines, disk volumes). No model changes.

No test edits; never launch the GUI; scratch only under `$HOME/.cache/sysmon-wo2/`.
Build (0 warnings) + `dotnet test`; report files changed, commands, results. No commit.

## Amendment 1 (Reviewer1)

`CardViews.cs:216-217` sets the card `Width`/`Height` from `ramp.Scale` only in
the `SmallCardView` constructor. A live font-size change fires only
`AppearanceChanged` → `ShellWindow.ScaleFonts` (no rebuild), so cards keep the
old size and the value is ellipsised until the page is rebuilt. Repro: CPU page
at 100 %, drag the font dial to 160 %.

Fix: make small cards follow a live scale change — e.g. `SmallCardView`
recomputes `Width`/`Height` from the current scale (via the same hook
`ScaleFonts` uses, or a `Rescale(scale)` called from `ModulePageView` when
appearance changes). Do not rebuild the whole page per tick.

## Amendment 2 (Reviewer1 round 2)

`SmallCardView.Rescale` (`CardViews.cs` ~232) recomputes the card height from
the new line height, but `_title`/`_value` keep the `LineHeight` set at
construction (`Controls.cs:92`); `ScaleText` changes only `FontSize`. Built at
160 % then dialled to 100 %, the content is 107.5 px in a 103 px card and the
graph runs into the next row. Fix: in `Rescale` also set `_title.LineHeight`
and `_value.LineHeight` to the same `lineHeight`.
