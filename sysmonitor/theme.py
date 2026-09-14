"""Colour palettes.

The Electron build layered translucent CSS over the desktop.  A Tk window can
only be uniformly translucent, so the rgba() panel colours are pre-flattened
here and the whole-window alpha (the opacity slider) supplies the see-through.
"""

# Pixels painted in this colour become fully transparent *and* click-through.
TRANSPARENT_KEY = "#010203"

# The panel is translucent, so surfaces stacked on it are stepped *lighter*
# (dark theme) rather than darker -- an inset chip loses all contrast once the
# desktop shows through 8% of the way.
DARK = {
    "panel": "#0f172a",
    "sidebar": "#0b1220",
    "control": "#1a2438",   # cards and boxes sitting on the panel
    "chip": "#1e293b",      # controls sitting on the darker sidebar
    "bar_empty": "#070c17",
    "text": "#ffffff",
    "muted": "#cbd5e1",
    "label": "#94a3b8",
    "border": "#2b374b",
    "tag": "#243044",
    "badge_ok": "#16351f",
    "badge_warm": "#3a3212",
    "badge_hot": "#3d1717",
}

LIGHT = {
    "panel": "#ffffff",
    "sidebar": "#f3f5f9",
    "control": "#eef1f6",
    "chip": "#e6eaf1",
    "bar_empty": "#dfe4ec",
    "text": "#1e293b",
    "muted": "#64748b",
    "label": "#475569",
    "border": "#d5dae1",
    "tag": "#e8ecf1",
    "badge_ok": "#dcfce7",
    "badge_warm": "#fef3c7",
    "badge_hot": "#fee2e2",
}

ACCENT_CPU = "#3b82f6"
ACCENT_RAM = "#a855f7"
ACCENT_DISK = "#10b981"

TEMP_OK = "#22c55e"
TEMP_WARM = "#eab308"
TEMP_HOT = "#ef4444"

WARM_AT = 65
HOT_AT = 80


def palette(name):
    return LIGHT if name == "light" else DARK


def load_color(value):
    """Bar colour by load, matching the original getColor()."""
    if value > 80:
        return "#ef4444"
    if value > 50:
        return "#eab308"
    return ACCENT_CPU


def temp_colors(temp, pal):
    """(foreground, background) for a temperature badge."""
    if temp is None:
        return pal["label"], pal["tag"]
    if temp >= HOT_AT:
        return TEMP_HOT, pal["badge_hot"]
    if temp >= WARM_AT:
        return TEMP_WARM, pal["badge_warm"]
    return TEMP_OK, pal["badge_ok"]
