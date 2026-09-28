"""Settings persisted to %APPDATA%\\SysMonitor\\config.json."""

import json
import os

DEFAULTS = {
    "lang": "th",
    "theme": "dark",           # dark | light
    "always_on_top": True,
    "snap": True,
    "opacity": 0.92,
    "show_cpu": True,
    "show_ram": True,
    "show_disk": True,
    "cpu_mode": "separated",   # separated | total
    "disk_mode": "separated",  # separated | total
    "speed": "balanced",       # eco | balanced | fast
    "temp_estimate": True,     # show '~' modelled temps when no sensor exists
    "wmi_cpu_temp": False,     # try the ACPI thermal zone (needs admin, costs a bit)
    "include_removable": True,
    "diagnostics": True,   # one log line every 10 min; see diag.py
    "sampler_thread": False,  # False: isolated helper process; True: legacy thread
    "pos_x": None,
    "pos_y": None,
    # Panel sizes in logical px (DPI scaling is applied on top of these).
    "mini_w": 270,
    "mini_h": 90,
    "exp_w": 630,
    "exp_h": 480,
}

# Resize limits, logical px.
MIN_MINI = (200, 64)
MAX_MINI = (900, 400)
MIN_EXP = (470, 300)
DEFAULT_SIZES = {"mini_w": 270, "mini_h": 90, "exp_w": 630, "exp_h": 480}

# Sampling cadence per speed mode: (metrics, disk usage, temperatures) seconds.
INTERVALS = {
    "eco": (5.0, 30.0, 60.0),
    "balanced": (2.5, 15.0, 30.0),
    "fast": (1.0, 10.0, 30.0),
}


def config_path():
    base = os.environ.get("APPDATA") or os.path.expanduser("~")
    return os.path.join(base, "SysMonitor", "config.json")


class Config:
    def __init__(self):
        self._data = dict(DEFAULTS)
        self.load()

    def load(self):
        try:
            with open(config_path(), "r", encoding="utf-8") as fh:
                stored = json.load(fh)
            if isinstance(stored, dict):
                for key, value in stored.items():
                    if key in DEFAULTS:
                        self._data[key] = value
        except (OSError, ValueError):
            pass

    def save(self):
        path = config_path()
        try:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            tmp = path + ".tmp"
            with open(tmp, "w", encoding="utf-8") as fh:
                json.dump(self._data, fh, indent=2)
            os.replace(tmp, path)
        except OSError:
            pass

    def __getitem__(self, key):
        return self._data[key]

    def __setitem__(self, key, value):
        self._data[key] = value

    def get(self, key, default=None):
        return self._data.get(key, default)

    def as_dict(self):
        return dict(self._data)

    @property
    def intervals(self):
        return INTERVALS.get(self._data["speed"], INTERVALS["balanced"])
