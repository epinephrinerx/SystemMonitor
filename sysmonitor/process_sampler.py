"""Keep synchronous hardware probes out of the Tk process, without UI threads."""

import multiprocessing
import time

from . import diag, win32
from .config import INTERVALS
from .sensors import Sampler, Snapshot

PROBE_TIMEOUT = 20.0
RESTART_BACKOFF = 300.0


class _WorkerConfig(dict):
    @property
    def intervals(self):
        return INTERVALS.get(self.get("speed"), INTERVALS["balanced"])


def _worker(connection, values):
    """One request, one response; never import the UI or create a Tk root."""
    cfg = _WorkerConfig(values)
    diag.set_enabled(bool(cfg.get("diagnostics", True)))
    sampler = Sampler(cfg, threaded=False)
    win32.quiet_error_dialogs()
    try:
        sampler._prev_cpu = win32.cpu_times()
        while True:
            try:
                sampler._collect()
                snapshot = sampler.read()
            except Exception:
                diag.report_exception("Sampler process")
                snapshot = Snapshot()
            connection.send(snapshot)
            cfg.update(connection.recv())
    except (EOFError, BrokenPipeError, OSError):
        pass  # the UI closed its end
    finally:
        connection.close()


class ProcessSampler:
    """Poll a single helper; no blocking join, hardware call, or queue thread.

    Requests are sent only after a response, so repeated setting changes cannot
    fill the pipe while a device is stuck. A timed-out helper is terminated and
    retried after five minutes, keeping both UI latency and restart cost bounded.
    """

    def __init__(self, config):
        self.config = config
        self.snapshot = Snapshot()
        self._context = multiprocessing.get_context("spawn")
        self._process = None
        self._connection = None
        self._retired = []
        self._awaiting = False
        self._requested_at = 0.0
        self._next_sample = 0.0
        self._next_start = 0.0
        self._stopped = False
        self._reported_ready = False

    def start(self):
        if self._stopped or self._process is not None:
            return
        parent, child = self._context.Pipe()
        process = self._context.Process(
            target=_worker, args=(child, self.config.as_dict()),
            name="SysMonitor sensors", daemon=True)
        try:
            process.start()
        except Exception:
            parent.close()
            child.close()
            process.close()
            diag.report_exception("Sampler process start")
            self._next_start = time.monotonic() + RESTART_BACKOFF
            return
        child.close()
        self._process, self._connection = process, parent
        self._awaiting = True
        self._reported_ready = False
        self._requested_at = time.monotonic()

    def _retire(self):
        if self._connection is not None:
            self._connection.close()
            self._connection = None
        if self._process is not None:
            if self._process.is_alive():
                self._process.terminate()
            self._retired.append(self._process)
            self._process = None
        self._awaiting = False

    def _reap(self):
        for process in self._retired[:]:
            if not process.is_alive():
                process.join(timeout=0)
                process.close()
                self._retired.remove(process)

    def stop(self):
        self._stopped = True
        self._retire()
        self._reap()

    def nudge(self):
        self._next_sample = 0.0

    def read(self):
        return self.snapshot

    def poll(self):
        self._reap()
        if self._stopped:
            return False
        now = time.monotonic()
        if self._process is None:
            if not self._retired and now >= self._next_start:
                self.start()
            return False
        try:
            if self._awaiting and self._connection.poll():
                self.snapshot = self._connection.recv()
                if self.snapshot.ready and not self._reported_ready:
                    if self.config.get("diagnostics", True):
                        diag.write("sampler ready cores=%d disks=%d worker_pid=%s"
                                   % (len(self.snapshot.cores), len(self.snapshot.disks),
                                      self._process.pid))
                    self._reported_ready = True
                self._awaiting = False
                self._next_sample = now + self.config.intervals[0]
                return True
            if not self._process.is_alive():
                raise RuntimeError("sampling process exited")
            if self._awaiting and now - self._requested_at >= PROBE_TIMEOUT:
                raise TimeoutError("hardware sample exceeded %.0fs" % PROBE_TIMEOUT)
            if not self._awaiting and now >= self._next_sample:
                self._connection.send(self.config.as_dict())
                self._awaiting = True
                self._requested_at = now
        except Exception:
            diag.report_exception("Sampler IPC")
            self._retire()
            self._next_start = now + RESTART_BACKOFF
            self.snapshot = Snapshot()  # do not present old readings as current
            return True
        return False
