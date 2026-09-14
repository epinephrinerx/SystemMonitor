"""Hardware sampling.

Normally called inside the isolated sampling process (see process_sampler).
Cheap counters (CPU, RAM, disk I/O) are read every tick; expensive or
slow-moving ones (free space, temperatures, drive identity) are read on their
own longer cadences, which is where most of the CPU saving comes from.
"""

import threading
import time

from . import diag, theme, win32

GB = 1024 ** 3


class Core:
    __slots__ = ("usage", "temp", "estimated")

    def __init__(self, usage, temp, estimated):
        self.usage = usage
        self.temp = temp
        self.estimated = estimated


class Ram:
    __slots__ = ("usage", "used_gb", "total_gb", "temp", "estimated")

    def __init__(self, usage=0, used_gb=0.0, total_gb=0.0):
        self.usage = usage
        self.used_gb = used_gb
        self.total_gb = total_gb
        self.temp = None        # no consumer board exposes a DIMM sensor
        self.estimated = False


class Disk:
    __slots__ = ("letter", "label", "media", "bus", "usage", "used_gb",
                 "total_gb", "read_mb", "write_mb", "temp", "estimated")

    def __init__(self, letter):
        self.letter = letter
        self.label = ""
        self.media = "Disk"
        self.bus = "Unknown"
        self.usage = 0
        self.used_gb = 0.0
        self.total_gb = 0.0
        self.read_mb = 0.0
        self.write_mb = 0.0
        self.temp = None
        self.estimated = False

    @property
    def title(self):
        return self.letter + ":" + ((" (" + self.label + ")") if self.label else "")


class Snapshot:
    __slots__ = ("cores", "ram", "disks", "cpu_total", "cpu_temp",
                 "cpu_temp_estimated", "ready")

    def __init__(self):
        self.cores = []
        self.ram = Ram()
        self.disks = []
        self.cpu_total = 0
        self.cpu_temp = None
        self.cpu_temp_estimated = False
        self.ready = False


def _estimate_cpu_temp(usage):
    """The model the Electron builds used, kept so the alert thresholds still
    have something to act on -- but flagged as an estimate in the UI."""
    return int(40 + usage * 0.4)


# A single probe slower than this marks that device as sluggish and backs it
# off after it returns. This is backoff, not a timeout; process isolation
# protects the UI from a probe which never returns.
SLOW_PROBE = 0.25         # seconds
SLOW_BACKOFF = 300.0      # seconds to skip a sluggish device


class Sampler:
    """Reads the machine's counters.

    The app uses ProcessSampler by default. This class owns synchronous probe
    logic and the optional legacy worker-thread mode. The cause of the earlier
    Tcl allocator crash is unproven; the isolated process never creates Tk.
    """

    def __init__(self, config, threaded=None):
        self.config = config
        self.threaded = (bool(config.get("sampler_thread", False))
                         if threaded is None else threaded)
        self.snapshot = Snapshot()
        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._wake = threading.Event()
        self._thread = None

        self._prev_cpu = None
        self._prev_io = {}
        self._hw_cache = {}       # drive letter -> (media, bus, physical no)
        self._next_space = 0.0
        self._next_temp = 0.0
        self._next_drives = 0.0
        self._next_sample = 0.0
        self._drives = []
        self._space = {}
        self._temps = {}
        self._slow_until = {}     # probe key -> monotonic time to retry after
        self._cpu_temp_real = None
        self._wmi_broken = False

    # ------------------------------------------------------------------ life
    def start(self):
        win32.quiet_error_dialogs()
        self._prev_cpu = win32.cpu_times()
        if self.threaded:
            self._thread = threading.Thread(target=self._run, name="sensors",
                                            daemon=True)
            self._thread.start()
            return
        # Populate RAM and drives straight away so the widget has something to
        # show before the first tick.  CPU needs two samples, so it reads 0%
        # until the next one -- same as the threaded path did.
        try:
            self._collect()
        except Exception:
            diag.report_exception("Sampler start")

    def stop(self):
        self._stop.set()
        self._wake.set()

    def nudge(self):
        """Apply a changed refresh rate without waiting out the current sleep."""
        self._next_sample = 0.0
        self._wake.set()

    def read(self):
        with self._lock:
            return self.snapshot

    def poll(self):
        """Advance sampling from the event loop.  Returns True if new data.

        Cheap to call often: it does nothing until the configured interval has
        elapsed.
        """
        if self.threaded or self._stop.is_set():
            return False
        now = time.monotonic()
        if now < self._next_sample:
            return False
        self._next_sample = now + self.config.intervals[0]
        try:
            self._collect()
            return True
        except Exception:
            diag.report_exception("Sampler poll")
            return False      # a widget must never die because a counter misbehaved

    # ------------------------------------------------------------ slow guard
    def _guard(self, key, fn, *args):
        """Run a probe, and shelve the device for a while if it drags.

        Returns (ok, result).  A cloud or network mount that stops answering
        gets skipped instead of blocking every future sample.
        """
        now = time.monotonic()
        if now < self._slow_until.get(key, 0.0):
            return False, None
        started = time.perf_counter()
        try:
            result = fn(*args)
        except Exception:
            diag.report_exception("Probe " + key)
            self._slow_until[key] = time.monotonic() + SLOW_BACKOFF
            return False, None
        if time.perf_counter() - started > SLOW_PROBE:
            self._slow_until[key] = time.monotonic() + SLOW_BACKOFF
            diag.write("Slow probe %s; backing off for %.0fs" % (key, SLOW_BACKOFF))
        return True, result

    # ----------------------------------------------------------------- loop
    def _run(self):
        win32.quiet_error_dialogs()
        self._prev_cpu = win32.cpu_times()
        while not self._stop.is_set():
            try:
                self._collect()
            except Exception:
                diag.report_exception("Sampler thread")
            interval = self.config.intervals[0]
            self._wake.wait(interval)
            self._wake.clear()

    def _collect(self):
        now = time.monotonic()
        snap = Snapshot()

        self._collect_cpu(snap)
        self._collect_ram(snap)

        if now >= self._next_drives:
            self._next_drives = now + 60.0
            self._refresh_drive_list()
        if now >= self._next_space:
            self._next_space = now + self.config.intervals[1]
            self._refresh_space()
        if now >= self._next_temp:
            self._next_temp = now + self.config.intervals[2]
            self._refresh_temperatures()

        self._collect_disks(snap, now)
        self._apply_cpu_temp(snap)

        snap.ready = True
        with self._lock:
            self.snapshot = snap

    # ------------------------------------------------------------------ cpu
    def _collect_cpu(self, snap):
        times = win32.cpu_times()
        if times is None:
            return
        if self._prev_cpu is None or len(self._prev_cpu) != len(times):
            self._prev_cpu = times
            return
        cores = []
        for prev, curr in zip(self._prev_cpu, times):
            idle_d = curr[0] - prev[0]
            # KernelTime already includes IdleTime, so busy = total - idle.
            total_d = (curr[1] - prev[1]) + (curr[2] - prev[2])
            if total_d <= 0:
                usage = 0
            else:
                usage = int(round(100.0 * (total_d - idle_d) / total_d))
            usage = max(0, min(100, usage))
            cores.append(Core(usage, None, False))
        self._prev_cpu = times
        snap.cores = cores
        if cores:
            snap.cpu_total = int(round(sum(c.usage for c in cores) / len(cores)))

    def _apply_cpu_temp(self, snap):
        if self._cpu_temp_real is not None:
            snap.cpu_temp = self._cpu_temp_real
            snap.cpu_temp_estimated = False
            for core in snap.cores:
                core.temp = self._cpu_temp_real
                core.estimated = False
        elif self.config["temp_estimate"]:
            snap.cpu_temp = _estimate_cpu_temp(snap.cpu_total)
            snap.cpu_temp_estimated = True
            for core in snap.cores:
                core.temp = _estimate_cpu_temp(core.usage)
                core.estimated = True

    # ------------------------------------------------------------------ ram
    def _collect_ram(self, snap):
        mem = win32.memory_status()
        if mem is None:
            return
        used, total = mem
        snap.ram = Ram(int(round(100.0 * used / total)), used / GB, total / GB)

    # ---------------------------------------------------------------- disks
    def _refresh_drive_list(self):
        ok, letters = self._guard("enum", win32.logical_drives,
                                  self.config["include_removable"])
        if not ok or letters is None:
            return
        self._drives = letters
        for letter in letters:
            if letter in self._hw_cache:
                continue
            ok, number = self._guard("num:" + letter,
                                     win32.physical_drive_number, letter)
            if not ok:
                continue
            hardware = None
            if number is not None:
                _, hardware = self._guard("hw:%s" % number,
                                          win32.drive_hardware, number)
            media, bus = hardware if hardware else ("Disk", "Unknown")
            _, label = self._guard("label:" + letter, win32.volume_label, letter)
            self._hw_cache[letter] = (media, bus, number, label or "")
        for cache in (self._hw_cache, self._prev_io, self._space, self._temps):
            for stale in [k for k in cache if k not in letters]:
                del cache[stale]

    def _refresh_space(self):
        for letter in self._drives:
            ok, space = self._guard("space:" + letter, win32.disk_space, letter)
            if ok and space is not None:
                self._space[letter] = space

    def _refresh_temperatures(self):
        seen = {}
        for letter in self._drives:
            number = self._hw_cache.get(letter, (None, None, None, ""))[2]
            if number is None:
                continue
            if number not in seen:
                ok, value = self._guard("temp:%s" % number,
                                        win32.drive_temperature, number)
                seen[number] = value if ok else None
            self._temps[letter] = seen[number]
        if self.config["wmi_cpu_temp"] and not self._wmi_broken:
            self._cpu_temp_real = self._read_wmi_cpu_temp()
        else:
            self._cpu_temp_real = None

    def _collect_disks(self, snap, now):
        disks = []
        for letter in self._drives:
            media, bus, _number, label = self._hw_cache.get(
                letter, ("Disk", "Unknown", None, "")
            )
            disk = Disk(letter)
            disk.label = label
            disk.media = media
            disk.bus = bus

            space = self._space.get(letter)
            if space:
                used, total = space
                disk.used_gb = used / GB
                disk.total_gb = total / GB
                disk.usage = int(round(100.0 * used / total))

            # Runs every tick on every drive, so it is guarded too.
            _, counters = self._guard("io:" + letter,
                                      win32.volume_io_counters, letter)
            if counters is not None:
                prev = self._prev_io.get(letter)
                sampled_at = time.monotonic()
                self._prev_io[letter] = (counters, sampled_at)
                elapsed = sampled_at - prev[1] if prev is not None else 0
                if prev is not None and elapsed > 0:
                    read_d = max(0, counters[0] - prev[0][0])
                    write_d = max(0, counters[1] - prev[0][1])
                    disk.read_mb = read_d / elapsed / (1024 * 1024)
                    disk.write_mb = write_d / elapsed / (1024 * 1024)

            disk.temp = self._temps.get(letter)
            disks.append(disk)
        snap.disks = disks

    # ----------------------------------------------------------- optional TZ
    def _read_wmi_cpu_temp(self):
        """ACPI thermal zone via PowerShell.  Opt-in: it spawns a process and
        usually needs admin, which is exactly what the low-CPU design avoids."""
        import subprocess
        script = (
            "(Get-CimInstance -Namespace root/WMI "
            "-ClassName MSAcpi_ThermalZoneTemperature -ErrorAction Stop"
            " | Select-Object -First 1).CurrentTemperature"
        )
        try:
            out = subprocess.run(
                ["powershell", "-NoProfile", "-NonInteractive", "-Command", script],
                # stdin must be detached explicitly: a --noconsole build has no
                # valid standard handles, and inheriting them makes subprocess
                # fail with 'WinError 6: The handle is invalid'.
                stdin=subprocess.DEVNULL,
                stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                text=True, timeout=8,
                creationflags=0x08000000,  # CREATE_NO_WINDOW
            )
            value = int(out.stdout.strip())
            celsius = int(round(value / 10.0 - 273.15))
            if -20 < celsius < 130:
                return celsius
        except Exception:
            diag.report_exception("WMI temperature")
        self._wmi_broken = True
        return None


def temp_text(temp, estimated):
    if temp is None:
        return "n/a"
    return ("~%d" % temp if estimated else "%d" % temp) + "°C"


def temp_is_hot(temp):
    return temp is not None and temp >= theme.HOT_AT
