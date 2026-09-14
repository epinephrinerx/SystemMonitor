"""Entry point: wire config, sampler and widget together, then run."""

import sys

from . import diag, win32
from .config import Config
from .sensors import Sampler
from .process_sampler import ProcessSampler
from .ui import Widget


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv

    # Must happen before Tk creates any window, or layout lands at 96 DPI.
    win32.set_dpi_aware()
    win32.quiet_error_dialogs()

    config = Config()
    if "--reset" in argv:
        from .config import DEFAULTS
        for key, value in DEFAULTS.items():
            config[key] = value
        config.save()

    from . import __version__
    diag.start(__version__, enabled=bool(config.get("diagnostics", True)))
    diag.write("sampler=%s"
               % ("thread" if config.get("sampler_thread", False)
                  else "process"))

    sampler = (Sampler(config, threaded=True) if config.get("sampler_thread", False)
               else ProcessSampler(config))
    sampler.start()
    try:
        widget = Widget(config, sampler)
        _install_heartbeat(widget)
        win32.trim_working_set()
        try:
            widget.run()
        except KeyboardInterrupt:
            widget.quit()
    finally:
        sampler.stop()
    return 0


def _install_heartbeat(widget, every_ms=600000):
    """One line every ten minutes, so a sudden death leaves a memory trail."""
    import tkinter as tk

    def beat():
        try:
            diag.heartbeat("expanded" if widget.expanded else "mini",
                           "cores=%d disks=%d"
                           % (len(widget.sampler.read().cores),
                              len(widget.sampler.read().disks)))
            widget.root.after(every_ms, beat)
        except tk.TclError:
            return

    widget.root.after(every_ms, beat)


if __name__ == "__main__":
    sys.exit(main())
