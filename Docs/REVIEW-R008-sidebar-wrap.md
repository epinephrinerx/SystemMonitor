**Review of R-008 (e203394): nothing needs fixing. Tests pass 233/233.**

**Scope:** the range `fa60303..e203394` also contains the R-006 commits (3403d6f and the docs commits), so the `MainWindow.xaml` hunk (`Margin="0,-12,-4,0"`) belongs to R-006, not R-008. For R-008 I reviewed only `SysMonitor.Wpf/MainWindow.xaml.cs` in e203394.

## Tests
`dotnet test SysMonitor.Tests`: Passed! Failed: 0, Passed: 233, Skipped: 0, Total: 233 (exit code 0).

## Regression checks
- **Wrapping works in practice:** the sidebar column is fixed at `Width="196"` (`MainWindow.xaml:366`). The `ScrollViewer` has `HorizontalScrollBarVisibility="Disabled"` (`:376`), so `Sidebar` gets a finite width of about 164 px after its margin. That is what lets a TextBlock with Wrap actually wrap.
- **CheckBox with a TextBlock as Content:** `App.xaml` has no implicit `TargetType="TextBlock"` style, only keyed styles (Label/Body/Value). Foreground, FontFamily and FontSize are inherited properties, and the new TextBlock sets none of them itself, so it inherits MutedBrush, Segoe UI/Leelawadee and size 11 from the CheckBox (`MainWindow.xaml.cs:1286-1294`). Nothing is overridden.
- **Update and restart buttons:** the `SidebarButton` template (`App.xaml:116`) uses a ContentPresenter with `HorizontalAlignment="Left"`. It still measures with the column width, so the text wraps.
- **RunUpdateStep:** both places that used to set `button.Content` now change `buttonLabel.Text` (`:1150`, `:1164`). No `button.Content` is left, and the extra parameter is passed correctly at `:1115`.
- **WrapPanel** (`:1308`): each RadioButton keeps its right margin of 10, so the rows lay out correctly. It starts a new line when "ออกจากโปรแกรม" + "ย่อลง Tray" (about 176 px) is wider than 164 px.
- **No sidebar resizing:** no GridSplitter or resize handle was added, and the column is still fixed at 196, so the user's "no resizing" instruction holds.

## Must fix
None.

## Notes (not blocking)
1. **The "full data" button's text can't wrap even though Wrap is set.** At `MainWindow.xaml.cs:907-927` the label sits inside a horizontal `StackPanel`, which measures its children with infinite width, so `TextWrapping.Wrap` at `:926` has no effect. It doesn't overflow today because "ดูข้อمูลเต็ม" is short. It would overflow if the text got longer. To reproduce, temporarily change `full_data` in `I18n.cs:71` to a long string: the text runs past the right edge. A fix would be a Grid with an Auto column plus a star column, or a DockPanel.
2. **Dial captions don't wrap and share a cell with the value.** At `:1218-1229`, `caption` and `readout` sit in the same cell of one Grid (Left and Right). A long caption would run under the value instead of wrapping. "ความโปร่งใส" and "ขนาดตัวอักษร" fit even at 1.6× font scale, so this is latent only.
3. **A single option label can't wrap.** RadioButton `Content = text` is still a plain string (`:1313`). WrapPanel only breaks between items, so one option longer than about 146 px would still overflow. The current labels are all short enough.
4. **Pre-existing, not a regression:** switching language rebuilds the sidebar (`:1054`). The update button then goes back to "ตรวจหาเวอร์ชันใหม่" even when `_pendingUpdate` is set, but clicking it downloads right away. To reproduce: check for updates and find a new version, switch language, and look at the button. This existed before e203394.
5. **No new tests:** no test covers the wrapping. That's acceptable for a visual change, which is for the user to check on screen.

VERDICT: PASS — every sidebar text source that could overflow at current Thai lengths now wraps or starts a new line, there are no regressions in Foreground/Font or RunUpdateStep, no resizing was added, and tests pass 233/233. Notes 1–3 are only risks if the text gets longer later.
