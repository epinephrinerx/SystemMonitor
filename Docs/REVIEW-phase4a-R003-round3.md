F3 is closed. I worked through the review's reproduce and the other corners by hand, and the full test suite passes.

## Test results (actually run)
`dotnet test SysMonitor.Tests` gave **Passed 226, Failed 0, Skipped 0**. Round 2 had 222, so that is exactly the 4 new tests in this diff.

## F3: checked by hand against `WindowGeometry.cs:200-216`
Constants: ShadowPad = 12, MinWidget = 200×64, MaxWidget = 900×400.

**The review's reproduce (panel 270×104, first-run position):**
- The window is 294×128 with `Top = B − 152`, so the visible bottom sits at `B − 36`.
- Dragging Left|Bottom 135px left with dy = 0 gives sX = 405/270 = **1.5**.
- `roomH = B − (B−152) − 12 = 140`, so the cap is 140/104 = **1.346**.
- `roomW` is large because the right edge is pinned.
- sMin = 0.741 and sMax = 3.33, so s = 1.346.
- The panel becomes 363.5×140, and the visible bottom is `(B−152) + 164 − 12` = **exactly `B`**. Before the fix it was B + 16.
- Dragging all the way out (toward sMax 3.33) still stops at 1.346, so the 206px overflow from before is gone.

**Left|Top and Right|Top at the top edge:**
- `roomH = origin.Bottom − area.Top − 12` is the distance from the pinned visible bottom to area.Top.
- The new visible top is `origin.Bottom − 12 − panelH·s`, which is ≥ area.Top whenever s ≤ roomH/panelH. So it stays inside.
- For Right|Top, `roomW = area.Right − origin.Left − 12` gives visible right = `origin.Left + 12 + panelW·s` ≤ area.Right, which is also correct.
- The same formula caps the dragged axis at the screen edge too. That makes sense.

**The new test uses a different panel (Origin 500×250, work area bottom 448):**
- The cap is 236/226 = 1.044, giving visible bottom = 448 exactly.
- Before the fix, sX = 1.284 would have put the bottom at 502. So this test would have failed before 303cabd; it really catches F3.
- Small thing: the comment says the widget is "20px above". It is actually 10px (438 vs 448).

## No interference with normal drags, and no negative/NaN
- `s = min(s, room)` only caps from above and is continuous, so nothing jumps mid-drag.
- A start that is on screen always has room/panel ≥ 1, so a drag on a big screen isn't touched. On a 4000×3000 screen with dx = −60, the cap is ≈ 12.2 against s = 1.126. The test `A_normal_corner_drag_is_not_disturbed…` confirms this.
- If room goes negative (the pinned edge is already off screen), `Math.Clamp(s, sMin, sMax)` pulls s back to a positive sMin.
- `panelW` and `panelH` are guarded > 0 (`:160`), and `_dragWorkArea` is captured before `_resizing` is set (`MainWindow.xaml.cs:553-555`). So there is no division by zero or NaN.

**The sMin fallback test (N2):** 1800×100 gives sMin 0.64 and sMax 0.5. |1−0.64| < |1−0.5|, so the code picks 0.64 and the width is 1152, matching the assertion. The comment in the code also correctly admits that one dimension can stay outside the limits.

## Observations (not blocking)
1. **`A_vertical_drag_is_clamped_on_the_right_side_too` (`WindowGeometryTests.cs:419`) never reaches `maxLeft`.**
   - Worked out: s = 1.354, width 668.5, and the raw left of −64.3 gets clamped by `minLeft` to −12. That is the same left-side clamp as the old test.
   - The Right edge ends at 656.5, far below 1212, so the assertion passes even without any clamp at all.
   - The N1 coverage gap from round 2 is therefore still open, even though the test name says it's covered.
2. **A corner drag that starts from a position already off screen jumps on the first frame and can't grow.**
   - This is observation 1 from round 2, now also true for corners.
   - Example: the visible bottom is 50px under the taskbar. Then `roomH/panelH = 54/104 = 0.52`, which clamps to sMin 0.74.
   - A 1px drag shrinks the panel from 270×104 to 200×77 at once, and it is still 23px below.
   - This only happens after a move-drag leaves the widget hanging off screen, because moving has no clamp (`MainWindow.xaml.cs:636-643`).
3. Once the cap is hit, the dragged edge stops following the pointer. That is a UX choice that makes sense.

I couldn't run a numeric script to replay the cases (permission for python was declined), so all the numbers above are hand calculations from the code.

VERDICT: PASS. F3 is closed: the reproduce's visible bottom stops exactly at area.Bottom, and the Left|Top and Right|Top corners are bounded by the same reasoning. Normal drags on a big screen are untouched, s can't go negative or NaN, and tests pass 226/226. Two things remain as notes: the right-side clamp test doesn't actually reach `maxLeft`, and a corner drag from an off-screen start jumps. Real on-screen dragging is still yours to check.
