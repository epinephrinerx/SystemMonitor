I've finished reading both diffs and the code around them, and I've run the tests. Here is the report.

# Review: R-009 (dc0df05) and R-010 (4ee5587)

## Tests (actually run)
`dotnet test SysMonitor.Tests` → **Passed: 234, Failed: 0, Skipped: 0** (exit 0)

---

## Must fix

### 1. R-009: the Overall window grows (or shrinks) a bit more every time you open Overall
- **Location:** `SysMonitor.Wpf/MainWindow.xaml.cs:548-561` (`FitOverallToContent`), together with `:204`/`:263-274` (`ApplyOverallScale`)
- **Cause:** `SetMode(Overall)` calls `ApplyPanelSize()`, which sets the scale `s` from `OverallW/H`. After that, `FitOverallToContent` measures with `OverallView.Measure(∞)`. Because a `LayoutTransform` is now on the element, `DesiredSize` comes back multiplied by `s`, i.e. natural size × `s`. That value is written into `_config.OverallW/H`. `ApplyOverallScale` is not called again after `Width`/`Height` are set, so the old scale stays.
  - The next time Overall opens, `s' = min(natural·s/630, natural·s/480)`. That compounds each time. It only holds steady if `min(nW/630, nH/480) == 1` exactly.
  - The fitted size is saved to the config (`Save` runs at drag end, in `HideToTray` and in `OnClosing`), so the drift carries over between sessions.
- **Impact:** Everyone who has never dragged the Overall window's edge (`OverallSized=false`, the default) is affected. If the content's natural size is bigger than 630×480, the window and text grow each time until they hit the work area. If it's smaller, they shrink down to 470×300 (s≈0.625). This also means the scale depends on the data after all (number of cores, drives and adapters decides the natural size), which contradicts the aim that it depend only on the panel.
- **Reproduce:** Delete or reset the config (or use "Reset window size"). Toggle Widget ↔ Overall 4–5 times with the context menu or Esc. Watch the window size and the text size change each round. Check `OverallW/H` in the config file after each round.
- **Fix direction (needs a design decision):** Measure with the transform at identity (or divide `DesiredSize` by `s`), then call `ApplyOverallScale()` after setting the new `Width`/`Height`. Another option is to reconsider whether auto-fit and window-based scaling should coexist at all. For example, the fit could pick a size that keeps `s=1`.
- **Missing test:** The fit logic sits in `MainWindow`, which the tests don't cover. Pulling the fit calculation out into a pure `WindowGeometry` function would let a test pin down that the fit doesn't compound with the scale.

---

## Noted

**R-009**
- **Sidebar 196 → 196·s, corner buttons 26 → 26·s:** This is intended under option ก. The layout gets `available/s`, so the `*` column and the inner `ScrollViewer`s (sidebar and content) scroll correctly in scaled units. At the minimum, 470×300 gives s = min(0.746, 0.625) = **0.625**: corner buttons are about 16 px, and body text is about 0.625× combined with the font slider. **The user should check readability and click targets at the minimum size on screen.** This is an acceptance item listed in Requirements.
- **Full view:** Not affected. `ApplyOverallScale` returns early outside Overall. `OverallView` is Collapsed, so the leftover transform is neither measured nor drawn, and it gets recomputed through `ApplyPanelSize` on re-entry. Harmless.
- **Code paths:** Startup, `SetMode`, `Resize` (`:816`) and `ResetSize` (via `ApplyPanelSize`) are all covered. The only gap is `FitOverallToContent`, as in finding 1.
- **Scale rule:** `min` of the two axes. A wide, short window scales by height and the right-hand column stretches to fill. That's reasonable.
- **Duplicated numbers:** `ResetSize` (`:573-574`) still hardcodes 630/480 instead of using `WindowGeometry.OverallReference`. The test pins the reference to `AppConfig`, but not to `ResetSize`.
- Popups and ContextMenus are outside the transform, so they don't scale. That's expected.

**R-010**
- **`Application.Current` at construction time:** It can't be null. `MainWindow` is created in `App.OnStartup` (`App.xaml.cs:34`), and the tests don't construct `MainWindow`.
- **Safety at shutdown:** `SessionEnding` is raised on the UI thread. `Save()` writes a temp file and then does `File.Move` (atomic), wrapped in try/catch (`AppConfig.cs:264-278`), so it's safe.
- **Double save:** When `SessionEnding` fires and `OnClosing` runs afterwards, the same values get written twice. That's idempotent and harmless.
- **Does the fix close the stated gap?** Only partly.
  - The "shutdown while in the tray" case already had its position saved by `HideToTray()` (`:1431`), so the new handler adds nothing there.
  - It only helps when the session ends while the window is visible and the position changed without a save. Examples: `KeepOnScreen` after a mode switch (`:479`, `:562`), or coming back from Full (`:461`).
  - A kill or crash is still not covered. So the agreed scope, "save continuously as the user moves/resizes", isn't fully met, although in practice drag end already saves.
- **More likely cause of the user's symptom (pre-existing, not from this commit):**
  - Whatever position is current at close gets saved. If you close while in Overall, Overall's top-left is saved, and `KeepOnScreen` has often pushed it up and to the left because the window is larger. If you close while in Full, the Full window's position is saved, not `_beforeFull`.
  - The next start always opens in Widget, so the widget appears where the larger window was, not where the user put it.
  - Switching Widget → Overall → Widget within one session also moves the widget permanently, because there's no widget-position restore.
  - Repro: put the widget in the bottom-right corner, open Overall, close from Overall, reopen. The widget is no longer in its original spot.
  - I recommend asking the user whether this matches what they saw before closing R-010.

---

VERDICT: FAIL. R-009 has a regression where `FitOverallToContent` measures a size that already includes the scale, so the Overall window and its text drift with every entry and the drift is saved to the config. R-010 is safe, but it doesn't address the more likely cause.
