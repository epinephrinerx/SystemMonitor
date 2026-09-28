"""Small regression cases: mock devices, canvas, processes and installer effects.

Run: python -B -m unittest discover -s tests -v
No Tk windows, hardware sampling, registry writes or actual uninstall occur.
"""

import base64
import contextlib
import os
import struct
import sys
import time
import unittest
from types import SimpleNamespace
from unittest.mock import MagicMock, patch

from sysmonitor import config, diag, process_sampler, sensors, theme, win32
from sysmonitor.i18n import Lang
from sysmonitor.painter import Painter
from sysmonitor.ui import Widget, TICK
from tools import installer


def make_config():
    cfg = config.Config.__new__(config.Config)
    cfg._data = dict(config.DEFAULTS, diagnostics=False)
    return cfg


def fake_canvas():
    cv = MagicMock()
    serial = iter(range(1, 10000))
    for kind in ("text", "polygon", "oval", "rectangle", "line"):
        getattr(cv, "create_" + kind).side_effect = lambda *a, **k: next(serial)
    return cv


class TemperatureTests(unittest.TestCase):
    def read(self, raw):
        with patch.object(win32, "_open_device", return_value=123), \
                patch.object(win32, "_storage_query", return_value=raw), \
                patch.object(win32.kernel32, "CloseHandle") as close:
            result = win32.drive_temperature(0)
            self.assertTrue(close.called)
            return result

    def descriptor(self, temperature):
        raw = bytearray(40)
        struct.pack_into("<IIhhH", raw, 0, 40, 40, 90, 80, 1)
        struct.pack_into("<HhhhBBBBI", raw, 24, 0, temperature, 80, 0, 0, 0, 0, 0, 0)
        return raw

    def test_real_temperature_offset(self):
        self.assertEqual(self.read(self.descriptor(55)), 55)

    def test_invalid_and_incomplete_descriptors(self):
        for raw in (None, b"", self.descriptor(55)[:28], self.descriptor(-32768)):
            with self.subTest(raw=raw):
                self.assertIsNone(self.read(raw))
        raw = self.descriptor(55)
        struct.pack_into("<H", raw, 12, 2)
        self.assertIsNone(self.read(raw))
        struct.pack_into("<H", raw, 12, 0)
        self.assertIsNone(self.read(raw))


class PainterTests(unittest.TestCase):
    def test_hide_restore_and_explicit_hidden(self):
        cv = fake_canvas()
        p = Painter(cv)
        p.begin()
        item = p.text("label", 0, 0, text="value")
        p.end()
        p.clear()
        cv.itemconfigure.assert_called_with(item, state="hidden")
        p.begin()
        self.assertEqual(p.text("label", 0, 0, text="value"), item)
        p.end()
        cv.itemconfigure.assert_called_with(item, state="normal")
        p.begin()
        p.text("label", 0, 0, text="value", state="hidden")
        p.end()
        cv.itemconfigure.assert_called_with(item, state="hidden")
        cv.delete.assert_not_called()
        self.assertEqual(cv.create_text.call_count, 1)

    def test_kind_switch_retains_both_and_restores_stack(self):
        cv = fake_canvas()
        p = Painter(cv)
        p.begin()
        old = p.rect("icon", 0, 0, 10, 10)
        foreground = p.text("foreground", 0, 0, text="text")
        p.end()
        p.begin()
        p.oval("icon", 0, 0, 10, 10)
        p.end()
        p.begin()
        self.assertEqual(p.rect("icon", 0, 0, 10, 10), old)
        self.assertEqual(p.text("foreground", 0, 0, text="text"), foreground)
        p.end()
        cv.tag_raise.assert_called_with(foreground, old)
        cv.delete.assert_not_called()

    def test_actual_mini_rotation_never_deletes_or_recreates(self):
        w = Widget.__new__(Widget)
        w.cfg, w.scale, w.expanded = make_config(), 1.0, False
        w.pal, w.lang, w.hot_items = theme.DARK, Lang("en"), []
        cv = fake_canvas()
        w.p = Painter(cv)
        snap = sensors.Snapshot()
        snap.ready = True
        snap.cores = [sensors.Core(10, 44, True) for _ in range(4)]
        snap.cpu_temp, snap.cpu_temp_estimated = 44, True
        snap.disks = [sensors.Disk("X")]
        w.sampler = SimpleNamespace(read=lambda: snap)
        w.view_index = 0
        for cycle in range(2):
            for view in (("cpu", 0, 4), ("ram", None, None), ("disk", 0, None)):
                w.views = [view]
                w.p.begin()
                w.render_mini()
                w.p.end()
            count = sum(getattr(cv, "create_" + k).call_count for k in
                        ("text", "polygon", "oval", "rectangle", "line"))
            if cycle == 0:
                initial = count
            else:
                self.assertEqual(count, initial)
        cv.delete.assert_not_called()


class SensorTests(unittest.TestCase):
    def test_io_interval_is_per_successful_read(self):
        sampler = sensors.Sampler(make_config())
        sampler._drives = ["X"]
        mb = 1024 * 1024
        with patch.object(sampler, "_guard", side_effect=[
                (True, (100 * mb, 0)), (False, None), (True, (400 * mb, 0))]), \
                patch.object(sensors.time, "monotonic", side_effect=[100.0, 400.0]):
            sampler._collect_disks(sensors.Snapshot(), 100.0)
            sampler._collect_disks(sensors.Snapshot(), 399.0)
            snap = sensors.Snapshot()
            sampler._collect_disks(snap, 400.0)
        self.assertEqual(snap.disks[0].read_mb, 1.0)

    def test_removed_drive_loses_all_cached_measurements(self):
        sampler = sensors.Sampler(make_config())
        for cache in (sampler._hw_cache, sampler._prev_io, sampler._space, sampler._temps):
            cache["X"] = "old"
        with patch.object(sampler, "_guard", return_value=(True, [])):
            sampler._refresh_drive_list()
        self.assertFalse(sampler._prev_io)
        self.assertFalse(sampler._space)
        self.assertFalse(sampler._temps)

    def test_probe_failure_is_logged_and_backed_off(self):
        sampler = sensors.Sampler(make_config())
        probe = MagicMock(side_effect=ValueError("bad counter"))
        with patch.object(sensors.time, "monotonic", return_value=100), \
                patch.object(diag, "report_exception") as report:
            self.assertEqual(sampler._guard("test", probe), (False, None))
            self.assertEqual(sampler._guard("test", probe), (False, None))
        self.assertEqual(probe.call_count, 1)
        report.assert_called_once_with("Probe test")


class ProcessTests(unittest.TestCase):
    def setUp(self):
        self.sampler = process_sampler.ProcessSampler(make_config())
        self.ctx = MagicMock()
        self.parent, self.child = MagicMock(), MagicMock()
        self.ctx.Pipe.return_value = self.parent, self.child
        self.proc = self.ctx.Process.return_value
        self.proc.is_alive.return_value = True
        self.parent.poll.return_value = False
        self.sampler._context = self.ctx
        self.clock = patch.object(process_sampler.time, "monotonic", return_value=100)
        self.now = self.clock.start()
        self.addCleanup(self.clock.stop)
        self.sampler.start()

    def test_waiting_poll_never_waits_or_fills_pipe(self):
        for _ in range(3):
            self.sampler.nudge()
            self.assertFalse(self.sampler.poll())
        self.parent.recv.assert_not_called()
        self.parent.send.assert_not_called()
        self.proc.join.assert_not_called()

    def test_response_and_latest_config(self):
        snap = sensors.Snapshot()
        snap.ready = True
        self.parent.poll.return_value = True
        self.parent.recv.return_value = snap
        self.assertTrue(self.sampler.poll())
        self.assertIs(self.sampler.read(), snap)
        self.sampler.config["speed"] = "eco"
        self.sampler.nudge()
        self.sampler.poll()
        self.assertEqual(self.parent.send.call_args.args[0]["speed"], "eco")
        self.assertTrue(self.sampler._awaiting)

    def test_timeout_terminates_then_restarts_after_backoff(self):
        self.now.return_value = 121
        with patch.object(diag, "report_exception"):
            self.assertTrue(self.sampler.poll())
        self.proc.terminate.assert_called_once()
        self.proc.join.assert_not_called()
        self.assertFalse(self.sampler.read().ready)
        self.proc.is_alive.return_value = False
        self.now.return_value = 122
        self.sampler.poll()
        self.proc.join.assert_called_once_with(timeout=0)
        self.assertEqual(self.ctx.Process.call_count, 1)
        self.now.return_value = 422
        self.sampler.poll()
        self.assertEqual(self.ctx.Process.call_count, 2)

    def test_ipc_failure_and_stop_are_nonblocking(self):
        self.parent.poll.return_value = True
        self.parent.recv.side_effect = EOFError
        with patch.object(diag, "report_exception"):
            self.assertTrue(self.sampler.poll())
        self.sampler.stop()
        self.assertFalse(self.sampler.poll())
        self.proc.terminate.assert_called_once()
        self.proc.join.assert_not_called()

    def test_worker_samples_without_tk_and_accepts_settings(self):
        connection = MagicMock()
        connection.recv.side_effect = [{"speed": "eco"}, EOFError]
        with patch.object(process_sampler, "Sampler") as factory, \
                patch.object(win32, "quiet_error_dialogs"), \
                patch.object(win32, "cpu_times", return_value=[]), \
                patch.object(diag, "set_enabled"):
            process_sampler._worker(connection, dict(config.DEFAULTS))
        self.assertEqual(factory.return_value._collect.call_count, 2)
        self.assertEqual(factory.call_args.args[0].intervals[0], 5.0)
        self.assertEqual(connection.send.call_count, 2)
        connection.close.assert_called_once()


class DiagnosticTests(unittest.TestCase):
    def test_tk_traceback_logged_and_rate_limited(self):
        root = SimpleNamespace()
        diag.install_tk_handler(root)
        with patch.object(diag, "_enabled", True), patch.object(diag, "_last_errors", {}), \
                patch.object(diag, "write") as write, \
                patch.object(diag.time, "monotonic", return_value=100):
            try:
                raise RuntimeError("callback failure")
            except RuntimeError:
                args = sys.exc_info()
                root.report_callback_exception(*args)
                first_count = write.call_count
                root.report_callback_exception(*args)
            self.assertEqual(write.call_count, first_count)
            output = "\n".join(call.args[0] for call in write.call_args_list)
            self.assertIn("Tk callback", output)
            self.assertIn("Traceback", output)
            self.assertIn("callback failure", output)

    def test_disabled_diagnostics_write_nothing(self):
        with patch.object(diag, "_enabled", False), patch.object(diag, "write") as write:
            diag.report_exception("test", ValueError, ValueError("x"), None)
        write.assert_not_called()

    def test_tick_reschedules_after_failure(self):
        w = Widget.__new__(Widget)
        w.root, w.sampler = MagicMock(), MagicMock()
        w.sampler.poll.side_effect = ValueError("bad sample")
        with patch.object(diag, "report_exception") as report:
            w._tick()
        report.assert_called_once_with("UI tick")
        w.root.after.assert_called_once_with(TICK, w._tick)


class SpawnIntegrationTests(unittest.TestCase):
    def test_real_spawn_with_simulated_hardware(self):
        from worker_fixture import simulated_worker
        sampler = process_sampler.ProcessSampler(make_config())
        try:
            with patch.object(process_sampler, "_worker", simulated_worker):
                sampler.start()
            deadline = time.monotonic() + 8
            while not sampler.read().ready and time.monotonic() < deadline:
                sampler.poll()
                time.sleep(0.02)
            self.assertTrue(sampler.read().ready)
            self.assertEqual(sampler.read().ram.total_gb, 4)
            self.assertEqual(sampler.read().disks[0].label, "Synthetic")
            first = sampler.read()
            sampler.nudge()
            while sampler.read() is first and time.monotonic() < deadline:
                sampler.poll()
                time.sleep(0.02)
            self.assertIsNot(sampler.read(), first)
        finally:
            sampler.stop()
            for process in sampler._retired:
                process.join(timeout=2)
            sampler._reap()
        self.assertFalse(sampler._retired)


class InstallerTests(unittest.TestCase):
    def test_uninstall_only_selects_owned_files(self):
        directory = os.path.abspath("test-install-location")
        with contextlib.ExitStack() as stack:
            stack.enter_context(patch.object(installer, "stop_running"))
            stack.enter_context(patch.object(installer.winreg, "OpenKey", return_value=MagicMock()))
            stack.enter_context(patch.object(installer.winreg, "QueryValueEx", return_value=(directory, 1)))
            stack.enter_context(patch.object(installer.winreg, "DeleteValue"))
            stack.enter_context(patch.object(installer.winreg, "DeleteKey"))
            stack.enter_context(patch.object(installer.os.path, "exists", return_value=False))
            stack.enter_context(patch.object(installer.os.path, "isdir", return_value=True))
            stack.enter_context(patch.object(installer.os.path, "isfile", return_value=True))
            remove = stack.enter_context(patch.object(installer.os, "remove"))
            stack.enter_context(patch.object(installer.os, "rmdir", side_effect=OSError("not empty")))
            walk = stack.enter_context(patch.object(installer.os, "walk"))
            spawn = stack.enter_context(patch.object(installer.subprocess, "Popen"))
            installer.uninstall(keep_settings=True, log=lambda s: None)
        self.assertEqual([c.args[0] for c in remove.call_args_list],
                         [os.path.join(directory, n) for n in installer.PROGRAM_FILES])
        walk.assert_not_called()
        spawn.assert_not_called()  # source Python must never delete itself

    def test_reject_root_relative_and_escaped_file(self):
        for directory in ("", "relative", os.path.abspath(os.sep)):
            with self.subTest(directory=directory), self.assertRaises(ValueError):
                installer.owned_paths(directory, installer.PROGRAM_FILES)
        target = os.path.abspath("target")
        outside = os.path.abspath("outside.exe")
        with patch.object(installer.os.path, "realpath", side_effect=[target, outside]), \
                self.assertRaises(ValueError):
            installer.owned_paths(target, (installer.EXE_NAME,))

    def test_self_cleanup_is_exact_and_literal(self):
        target = os.path.abspath("test O'Brien %PATH% & folder")
        running = os.path.join(target, installer.UNINST_NAME)
        with patch.object(installer.sys, "frozen", True, create=True), \
                patch.object(installer.subprocess, "Popen") as spawn:
            self.assertFalse(installer.schedule_self_removal(target, sys.executable))
            self.assertTrue(installer.schedule_self_removal(target, running))
        args = spawn.call_args.args[0]
        script = base64.b64decode(args[-1]).decode("utf-16-le")
        self.assertIn(running.replace("'", "''"), script)
        self.assertIn("-LiteralPath", script)
        self.assertNotIn("-Recurse", script)
        self.assertNotIn("shell", spawn.call_args.kwargs)
        self.assertEqual(spawn.call_args.kwargs["creationflags"], installer.CREATE_NO_WINDOW)


if __name__ == "__main__":
    unittest.main()
