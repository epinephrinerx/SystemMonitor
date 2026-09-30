## Round 2 review of phase 3 / R-006 (`9bbb835..dc6f418`)

F1 and F2 are both fixed, going by the XAML geometry at the default font size. The fix didn't introduce any logic regressions. I calculated all of this from the XAML and didn't open the app.

### Test results (actually run)
`dotnet test SysMonitor.Tests` → **Passed! Failed: 0, Passed: 209, Skipped: 0, Total: 209** (1 s)

### F1: pause button covering the value — ✅ fixed
- **Where:** `MainWindow.xaml:262-268`, `Margin="0,-12,18,0"`. Coordinates below are relative to `WidgetView`, whose inner width is W.
- **Button position:** x ∈ [W−36, W−18], y ∈ [−12, 6]. The top edge sits on the Border's inner edge because `WidgetView` has `Margin="12"`, so the button doesn't hang outside the panel.
- **Glyph position:** at `FontTiny` = 9 the glyph is centred at y = −3, and its em box spans about [−7.5, 1.5].
- **Value text:** the 14pt bold ink starts about 5px below the top of the title row. Even when the content fills the whole height (row at y = 0), there's still 3px or more of clearance.
- **At FontScale 1.6:** the glyph is 14.4, ink about [−8, 2]. The value is 22.4, ink starts about y ≈ 8. Still clear.
- **Still clickable:** in window coordinates the button is at y = 13–31, 43–61px from the right edge.
  - Its top 5px falls inside `EdgeBand` (18). That's fine because `OnPreviewMouseLeftButtonDown` checks `OverControl` before `HitTest` (`MainWindow.xaml.cs:526`), and the cursor goes back to Arrow (`:615`).
  - It isn't inside `CornerBand` (28).

### F2: badge covering the title — ✅ fixed
- **Where:** `MainWindow.xaml:308-321` and `MainWindow.xaml.cs:366-367`.
- **Badge position:** it's now in the title row's Grid at x = 0 of the StackPanel. In the badge's text box, "⏸ Paused" at `FontLabel` 10 SemiBold estimates to about 45–50px. With the 8px margin that ends around 58px, which is less than 72. The Thai text "⏸ ค้าง" is shorter.
- **Title still trims:** it keeps `TextTrimming` and `HorizontalAlignment="Left"`. At the default width (270) it gets 212 − 72 − 96 = 44px while paused.
- **N2 from round 1 is gone too:** the badge now starts at x = 16 of `WidgetView`, past the Prev chip (−6..14).

### Regression checks
| Case | Result | Evidence |
|---|---|---|
| Switching language | ✅ | `:992` → `BuildSidebar` → `ApplyRotationPauseState` (`:838`) recomputes the margin from `_rotationPaused` every time, so no stale margin |
| `RebuildViews` | ✅ | It only touches the ViewModel. The badge and title are named elements set in code, not generated from a template |
| `SetMode` → Widget | ✅ | `:371-423` doesn't reset `_rotationPaused` or the margin, and `WidgetView` is the same instance |
| Initial state | ✅ | Called after `InitializeComponent` (`:93`). The margin it sets is `0,0,96,0`, the same as the XAML |

### Noted (not blocking)
- **N6. The value 72 is hardcoded and doesn't scale with FontScale (0.8–1.6).**
  - At 1.6 (`FontLabel` = 16) I estimate "⏸ Paused" at about 70–75px. The 8px gap would disappear and the badge might touch the title.
  - This is an estimate; I couldn't measure it because running PowerShell to measure the text wasn't approved.
  - Worth checking on screen. A lasting fix is to put the badge and title in `Auto`/`*` columns of the Grid, or in a DockPanel.
- **N7. At narrow widths the title disappears entirely while paused.**
  - At the minimum panel width of 200 the row has 142px, and 142 − 72 − 96 < 0, so the title gets 0 width.
  - At about 226 or narrower you only see "⏸ Paused" and can't tell which page is paused.
  - It doesn't overlap anything, but it's a UX loss.
- **N8. The badge now lives in the StackPanel that collapses when `ShowMessage` = true.** While a message is showing, the badge won't appear even if rotation is paused. The button's icon still shows the state.
- **N9. The pause button (top −12) sits 8px higher than the close button (top −4).** This is only visual.
- N1, N4 and N5 from round 1 still stand.

I didn't change any files in this review. The commands I ran were `git diff 9bbb835..dc6f418`, `dotnet test SysMonitor.Tests`, and reads of the relevant source.

VERDICT: PASS — F1 and F2 are fixed by the XAML geometry at the default font, with no logic regressions and 209/209 tests passing. You still need to check on screen at FontScale 1.6 (N6) and at the minimum width of 200 (N7).
