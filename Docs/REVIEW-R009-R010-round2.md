# Re-review of R-009/R-010, round 2 (diff `4ee5587..2111940`)

Both round-1 findings are fixed. The drift is gone, and each view now saves and restores its own position. Tests pass. I found no blocking issues, only 3 notes.

## Tests (actually run)
`dotnet test SysMonitor.Tests`: **Passed 234, Failed 0, Skipped 0** (exit 0)

## 1. Drift in `FitOverallToContent` (`MainWindow.xaml.cs:560-581`)
- **The math:** when Overall opens, `ApplyPanelSize` sets the scale `s = ContentScale(OverallW,H)`. `Measure(∞)` then returns `DesiredSize = natural·s`, because the LayoutTransform sits on `OverallView` itself. The fit computes `s` with the same function from the same `Width/Height`, so dividing by `s` gives exactly `natural`.
- **Stable across rounds:** the new `OverallW/H` is `ceil(natural)`, clamped, and it does not depend on the previous size. Next round it measures `natural·s'`, divides by `s'` and gets `natural` again. The size is fixed after the first round. The only variation is ±1px from `Ceiling` with floating-point epsilon, and that does not compound.
- **No margin error:** `OverallView` (`MainWindow.xaml:364`) has no Margin, so nothing sits outside the transform to throw the division off.
- **Scale updated:** `ApplyOverallScale()` is called after `Width/Height` are set (`:580`), so the scale matches the new size.
- `ResetSize` now uses `OverallReference` (`:592-593`). Done.

## 2. Per-view save/restore: every path is covered
All saves go through `SavePlacement()` (`:878`), which writes the current `_mode`.

| Path | Result |
|---|---|
| End of a drag or resize (`:757`) | Saves the current view ✓ |
| `SetMode`: `SavePlacement()` (`:461`) | Runs before `_mode` changes, so it writes the view being left ✓ |
| `HideToTray` (`:1488`), `OnClosing` (`:1544`), `SessionEnding` (`:116`), `Restart` (`:1455`) | Each saves the current view. Closing from Overall or Full no longer touches `WidgetPos*` ✓ |
| Startup (`:96-98`) | `RestorePosition` (legacy or first-run), then overridden by `RestoreViewPosition(Widget)` if it has a value ✓ |

**Migrating an old config:** the new keys are missing, so they load as `null`. `RestoreViewPosition` returns false, and the placement falls back to `PosX/PosY` or the first-run default ✓.

## 3. Regressions
- **Widget↔Overall↔Full, repeated:** every exit from a view saves that view's position, and every entry restores the target view's own position. The positions stay put.
  - The only exception is the first entry to a view that has never been placed. It opens at the previous view's top-left, then `KeepOnScreen` moves it. That is expected.
- **Where `KeepOnScreen` runs:** Widget/Overall always get it. Full gets it only when a position was restored, which covers the case of a monitor being unplugged.
  - Full entered for the first time without a restored position gets no `KeepOnScreen`, same as the old behaviour.
  - `FitOverallToContent` also calls `KeepOnScreen` after the size changes ✓.
- **`_beforeFull` when `WidgetPosX` is null:**
  - Entering Full always leaves from Widget or Overall. `SavePlacement` writes that view's position first, including when opened with `--full`/`--expanded`, which goes through `SetMode` from Widget.
  - So on the way back, `RestoreViewPosition` almost always returns true and overrides `_beforeFull`, with the same value. `_beforeFull` is left only as a fallback and cannot conflict ✓.

## Notes (not blocking)
1. **Fit and scale are still not exactly consistent (R-009, needs a design decision):**
   - The fit sets the panel to `natural`, but the view is laid out in `panel/s'`, where `s' = min(nW/630, nH/480)`. The limiting axis therefore always comes out at exactly 630 or 480 scaled units.
   - Example: natural 800×500 gives s'≈1.04 and a layout of about 769×480, which is smaller than natural. The inner ScrollViewers get a little scroll on both axes. If natural is smaller than the reference, s' < 1 and you get extra empty space instead.
   - It does not drift, but it is not a tight fit. The user should check on screen whether this is acceptable. An alternative from round 1 is to pick a fit size that keeps `s=1`.
2. **Missing tests:**
   - Nothing covers round-tripping the 6 new keys, or loading an old config where they are null. `PaletteAndConfigTests.cs:114-145` covers only `PosX/PosY`.
   - The fit logic is still in `MainWindow`, so no test locks in "no compounding".
3. **Legacy config on the first launch after upgrading:** if the old `PosX` was saved from Overall or Full (the original bug), the widget will open at that spot once. After that it is correct. This cannot be avoided without migration data.

I did not change any files.

VERDICT: PASS. The drift is closed mathematically (DesiredSize/s = natural, stable every round). Per-view save/restore covers drag end, SetMode, HideToTray, OnClosing, SessionEnding, Restart and startup, and the fallback for old configs is correct. Tests pass 234/234. What remains is design: fit vs scale, plus on-screen checks by the user.
