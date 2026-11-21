# Handoff — R-030 B2 hands-on fix 5: Restart / Manual / About as nav rows

From: Coding Owner · To: Writing Owner 2.
User (2026-11-21): "ผมอยากให้ดึง restart manual และ about ออกมาอยู่ใต้ Processes ก่อน setting ครับ"

Sidebar nav order becomes:

```
Overall
Apps
Processes
Restart
Manual
About
Settings ▸   (expander)
```

1. Move the **Restart**, **Manual** and **About** rows out of the Settings
   expander (remove them from `ShellModel.BuildSidebar`) into the nav list in
   `MainView`, styled like the other nav rows (icon + label), each running the
   same action as today (Restart keeps its confirm/behaviour; Manual and About
   open the same panels). They are actions, not pages: they never take the
   selected highlight and do not change `main_page`.
2. Settings expander keeps the rest unchanged (window, opacity, font size,
   Modules, "More settings…", Updates).
3. Do not touch files being changed by fix 4 beyond what this needs; apply on
   top of the current working tree.

No test edits; never launch the GUI; scratch only under `$HOME/.cache/sysmon-wo2/`.
Build + `dotnet test`; report files changed, commands, results, tests that must
change or be added. No commit.
