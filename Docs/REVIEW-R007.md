I did not change any files. Two things need fixing, so the verdict is FAIL. Tests pass: `dotnet test SysMonitor.Tests` gave **Passed 233 / Failed 0 / Skipped 0**.

## Risk points ก–ฉ from the plan

| Point | Result |
|---|---|
| ก ScrollViewer in the `*` row | ✅ `MainWindow.xaml:500,510`. The ScrollViewer sits in the `*` row of the new Grid, and that Grid is in the `*` row of FullView (`:466`). Height is limited, so the tab list can still scroll. |
| ข Width binding / layout loop | ✅ No layout loop. ❌ **The text does not actually wrap** (see 1 below). |
| ค ButtonBase | ✅ `NavButton` returns a real `Button`, so `OverControl` (`.xaml.cs:720`) sees it as a control. |
| ง Double-click | ✅ The Button marks the MouseLeftButtonDown as handled, so the window's `OnMouseLeftButtonDown` (`:615`) is not called for the click on the button. See also 5 below. |
| จ Clear before adding | ✅ `.xaml.cs:911` calls `FullNav.Children.Clear()` before adding. Click handlers are lambdas on the new buttons each time. |
| ฉ Top-right icons | ✅ The diff does not touch `xaml:478-494`, `.xaml.cs:98-107` or `UpdateHeaderTooltips` (`:880-886`, still sets all 4 tooltips). |
| Margin / MinWidth | ✅ `Margin="0,0,12,0"` moved to the wrapping Grid (`xaml:497`). The column is `Auto`, so the spacing is the same as before. `MinWidth="128"` is still on `TabsList` (`:513`). |

## ต้องแก้

**1. The label cannot wrap: the TextBlock sits inside a horizontal StackPanel** (`MainWindow.xaml.cs` NavButton, around `:1271-1292`).
- **Cause:** A StackPanel with `Orientation=Horizontal` measures its children with unlimited width. The label's `TextWrapping = Wrap` never takes effect, whatever width `FullNav` has.
- **Impact:** The XAML comment (`xaml:505-507`) and the commit message say "long labels wrap instead of widening the rail". That is not true. If a label is wider than the space left, WPF clips it in the middle of a character instead of wrapping.
- **Space estimate:** With the rail at its 128 minimum, subtract padding and border (22) and icon plus margin (21). That leaves about 85 px for the text. "เปิดเป็น Widget" and "Open as widget" at 12 px are about 80 px, which is very close. This only happens on machines whose device names are all short, so the rail stays at 128.
- **Reproduce:** Temporarily set `lang["open_widget"]` to a long string, open Full on a machine with short tab names (rail = 128). The text is cut off and does not go to a second line.
- **Fix:** Change the content to a `DockPanel` (icon `Dock=Left`) or a `Grid` with an `Auto`/`*` column pair, so the label gets a limited width.

**2. The "ดูข้อมูลเต็ม" icon changed without being asked for** (`.xaml.cs:904`).
- **Evidence:** In 3403d6f the glyph was embedded as the bytes `EE A7 99`, which is **U+E9D9** (checked with `git show 3403d6f:… | od -c`). The new code uses `"\uE740"` (the FullScreen icon, same as the top-right button).
- **Impact:** The existing button's icon changes. This contradicts plan step 1 ("หน้าตาเหมือนเดิมทุกอย่าง") and the commit message, which says it was "extracted from the full-data button".
- **Fix:** Use `"\uE9D9"`. If E740 is meant to match the top-right icon, the user needs to confirm that first and it should be recorded in the Requirement.

## สังเกตไว้

3. **The Full rail width only ever grows** (`xaml:509`). The column is `Auto` and takes the larger of `FullNav.Width` and the tab list. The ItemsControl stretches to the column, so `ActualWidth` never shrinks.
   - If the tabs get narrower later (tab labels change with language, or a device is unplugged), the rail keeps its old width.
   - There is no loop, but it is sticky.
   - To check: open Full in the language with the longer labels, then switch language. The rail should not shrink.
4. **The first render has `FullNav.Width = 0`**, until `TabsList` has an `ActualWidth`. The next layout pass corrects it, so the most you would see is a single flicker frame.
5. **Double-click across a mode change.** The first click switches mode on mouse-up. The second click (`ClickCount=2`) hit-tests the new layout. For example, double-clicking "เปิดเป็น Widget" in Overall: if the cursor lands on the widget background, it goes on to Overall. The top-right icon buttons already behave this way, so it is not new. It needs checking on screen.
6. **The range 3403d6f..88d02c8 also includes e203394 (R-008 wrap fix)**, which was reviewed separately. This review covers the R-007 part only (88d02c8).
7. `Docs/CODING-OWNER-PLAN-R007.md:71` still says the plan file is empty. That is out of date now that the plan has been committed.
8. Plan step 4 (update the doc comment on `BuildSidebar`/`UpdateHeaderTooltips` to mention the Full rail buttons) was not done (`:875-893`). Minor.
9. Plan point ช (light/dark theme): the `#3b82f6` colour on the Full rail background needs checking by eye in both themes.

**Left for you to check on screen:** point ก at 640×480 with many tabs; point ค, clicking a button near the left edge (should switch view, not start a resize, with an arrow cursor); point ช; and points 3 and 5 above.

VERDICT: FAIL — the NavButton label cannot wrap because it sits in a horizontal StackPanel, and the "ดูข้อมูลเต็ม" icon changed from U+E9D9 to U+E740 without being asked for.
