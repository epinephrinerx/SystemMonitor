"""Opt-in Tk rendering check; synthetic data and no persistent settings changes.

Set SYSMONITOR_TK_TEST=1 to run. Exercises the application's own rendering API,
without sending input to desktop windows or polling real hardware.
"""

import os
import unittest
from types import SimpleNamespace
from unittest.mock import patch

from sysmonitor import config, sensors
from sysmonitor.ui import Widget


@unittest.skipUnless(os.environ.get("SYSMONITOR_TK_TEST") == "1", "opt-in Tk integration")
class TkIntegrationTests(unittest.TestCase):
    def test_layout_reuse_and_controls_in_real_tk(self):
        cfg = config.Config.__new__(config.Config)
        cfg._data = dict(config.DEFAULTS, pos_x=100, pos_y=80)
        snap = sensors.Snapshot()
        snap.ready = True
        snap.cores = [sensors.Core(i * 6, 44, True) for i in range(16)]
        snap.ram = sensors.Ram(25, 4, 16)
        snap.disks = [sensors.Disk(letter) for letter in "CDE"]
        sampler = SimpleNamespace(read=lambda: snap, poll=lambda: False,
                                  nudge=lambda: None, stop=lambda: None)
        widget = None
        with patch.object(cfg, "save") as save:
            try:
                widget = Widget(cfg, sampler)
                widget.root.withdraw()
                widget.root.update_idletasks()
                # Warm every mini view and both expanded display modes/themes.
                def layouts():
                    widget.collapse()
                    for i in range(len(widget.views)):
                        widget.view_index = i
                        widget.render()
                    widget.expand()
                    widget.root.update_idletasks()
                    for lang in ("th", "en"):
                        widget._set_lang(lang)
                        for mode in ("total", "separated"):
                            widget._set_mode("cpu_mode", mode)
                            widget._set_mode("disk_mode", mode)
                            widget._toggle_theme()
                            widget.render_content()
                    widget._set_opacity(0.8)
                    widget._set_panel_size(700, 520)
                    widget._apply_geometry()
                    widget.root.update_idletasks()
                    widget.render()
                layouts()
                before = (set(widget.canvas.find_all()), set(widget.content.find_all()),
                          len(widget._bound), len(widget.canvas._tclCommands or []))
                layouts()
                after = (set(widget.canvas.find_all()), set(widget.content.find_all()),
                         len(widget._bound), len(widget.canvas._tclCommands or []))
                self.assertEqual(before, after)
                self.assertGreater(len(before[0]), 0)
                self.assertGreater(len(before[1]), 0)
                self.assertTrue(save.called)
            finally:
                if widget is not None:
                    widget.quit()
