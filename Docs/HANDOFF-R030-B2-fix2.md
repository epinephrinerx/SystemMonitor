# Handoff — R-030 B2 hands-on fix 2: Widget controls outside the panel

From: Coding Owner · To: Writing Owner 2. User report (2026-11-21):
"ใน widget เครื่องหมายต่างๆ ยังลอยออกนอกหน้าต่างครับ". Screenshot shows the
pause (II) and close (X) icons above the widget's top-right edge, in the
shadow area, and the ◀ ▶ step buttons half outside the left/right edges.

Causes:
- `ShellWindow.cs` ~173: `_widgetCorner` is a child of `root` aligned
  top-right of the whole window, while `_panel` is inset by
  `Margin = WindowGeometry.ShadowPad`. → the corner sits in the shadow pad.
- `WidgetView.cs` ~45/53: `prev.Margin = (-6,0,0,0)`, `next.Margin = (0,0,-6,0)`.

Fix:
1. Place the widget corner controls **inside** the panel: give `_widgetCorner`
   the same inset as `_panel` plus a small inner gap (e.g. `ShadowPad + 6`
   top/right), or move it into the panel's content grid. It must follow the
   panel margin when the margin changes (full screen sets it to 0).
2. Step buttons fully inside the panel: non-negative margins (e.g. 2 px), and
   make sure the widget content keeps enough horizontal padding so the arrows
   do not cover the bars/text (adjust the content margin if needed).
3. Check the font-scale path (`ScaleText`) does not push them out again at the
   largest font size; keep the corner icons' hit areas unchanged.

No test edits; never launch the GUI; scratch only under `$HOME/.cache/sysmon-wo2/`.
Build + `dotnet test`; report files changed, commands, results. No commit.
