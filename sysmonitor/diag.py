"""A tiny flight recorder.

A Tcl allocator panic kills the process outright -- no Python traceback, no
exit handler, just a message box.  When that happened there was nothing on
disk to explain it, so diagnosing it meant guessing.

This writes a handful of lines to %APPDATA%\\SysMonitor\\sysmonitor.log: one at
startup, one every ten minutes with memory and mode, and one for any unhandled
exception.  If the app dies again, the tail of that file shows whether memory
was climbing beforehand and what it was doing.

Cost is one short line per ten minutes.  The file is capped and truncated.
"""

import ctypes
import os
import sys
import threading
import time
import traceback

MAX_BYTES = 256 * 1024
_lock = threading.Lock()
_enabled = True
_last_errors = {}


def log_path():
    base = os.environ.get("APPDATA") or os.path.expanduser("~")
    return os.path.join(base, "SysMonitor", "sysmonitor.log")


def set_enabled(enabled):
    global _enabled
    _enabled = enabled


class _PMC(ctypes.Structure):
    _fields_ = [("cb", ctypes.c_uint32), ("PageFaultCount", ctypes.c_uint32),
                ("PeakWorkingSetSize", ctypes.c_size_t),
                ("WorkingSetSize", ctypes.c_size_t),
                ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
                ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                ("PagefileUsage", ctypes.c_size_t),
                ("PeakPagefileUsage", ctypes.c_size_t)]


def working_set_mb():
    try:
        kernel32 = ctypes.WinDLL("kernel32")
        kernel32.GetCurrentProcess.restype = ctypes.c_void_p
        psapi = ctypes.WinDLL("psapi")
        psapi.GetProcessMemoryInfo.argtypes = [ctypes.c_void_p,
                                               ctypes.POINTER(_PMC),
                                               ctypes.c_uint32]
        pmc = _PMC()
        pmc.cb = ctypes.sizeof(pmc)
        if psapi.GetProcessMemoryInfo(kernel32.GetCurrentProcess(),
                                      ctypes.byref(pmc), pmc.cb):
            return pmc.WorkingSetSize / (1024.0 * 1024.0)
    except Exception:
        pass
    return -1.0


def write(line):
    if not _enabled:
        return
    stamp = time.strftime("%Y-%m-%d %H:%M:%S")
    try:
        with _lock:
            path = log_path()
            os.makedirs(os.path.dirname(path), exist_ok=True)
            try:
                if os.path.getsize(path) > MAX_BYTES:
                    with open(path, "r", encoding="utf-8", errors="replace") as fh:
                        tail = fh.readlines()[-200:]
                    with open(path, "w", encoding="utf-8") as fh:
                        fh.writelines(tail)
            except OSError:
                pass
            with open(path, "a", encoding="utf-8") as fh:
                fh.write("%s  %s\n" % (stamp, line))
    except Exception:
        pass          # diagnostics must never take the app down


def start(version, enabled=True):
    global _enabled
    _enabled = enabled
    if not _enabled:
        return
    try:
        import _tkinter
        tcl = "%s/%s" % (_tkinter.TCL_VERSION, _tkinter.TK_VERSION)
    except Exception:
        tcl = "?"
    write("--- start v%s  python %s  tcl/tk %s  frozen=%s  rss=%.1fMB"
          % (version, sys.version.split()[0], tcl,
             bool(getattr(sys, "frozen", False)), working_set_mb()))

    previous = sys.excepthook

    def hook(kind, value, tb):
        write("UNHANDLED %s: %s" % (kind.__name__, value))
        for line in traceback.format_exception(kind, value, tb):
            write("  " + line.rstrip())
        if previous:
            previous(kind, value, tb)

    sys.excepthook = hook

    def thread_hook(args):
        write("UNHANDLED in thread %s: %s: %s"
              % (getattr(args.thread, "name", "?"),
                 args.exc_type.__name__, args.exc_value))

    try:
        threading.excepthook = thread_hook
    except Exception:
        pass


def heartbeat(mode, extra=""):
    write("alive  mode=%-8s rss=%6.1fMB %s" % (mode, working_set_mb(), extra))


def report_exception(context, kind=None, value=None, tb=None):
    """Log caught failures with a traceback, at most once/minute per context."""
    if not _enabled:
        return
    if kind is None:
        kind, value, tb = sys.exc_info()
    if kind is None:
        return
    now = time.monotonic()
    with _lock:
        if now - _last_errors.get(context, float("-inf")) < 60:
            return
        _last_errors[context] = now
    write("ERROR %s: %s: %s" % (context, kind.__name__, value))
    for line in traceback.format_exception(kind, value, tb):
        write("  " + line.rstrip())


def install_tk_handler(root):
    def report(kind, value, tb):
        report_exception("Tk callback", kind, value, tb)
    root.report_callback_exception = report
