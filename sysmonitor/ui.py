"""The widget itself: a frameless, translucent, always-on-top Tk window.

Everything is drawn onto a canvas rather than built from Tk widgets, which
keeps the window count at one and the redraw cost at a couple of milliseconds.

Drawing goes through Painter, which reuses canvas items instead of recreating
them each frame -- see painter.py for why that is not optional here.
"""

import math
import time
import tkinter as tk

from . import config, diag, sensors, theme, win32
from .i18n import Lang
from .painter import Painter
from .theme import TRANSPARENT_KEY

MARGIN = 12
RADIUS = 14
SIDEBAR_W = 196
SNAP_MARGIN = 25
GRIP = 16              # bottom-right corner hit zone
EDGE = 6               # right / bottom edge hit zone
ROTATE_EVERY = 5.0
PULSE_EVERY = 600      # ms between hot-badge blinks
TICK = 400             # ms UI poll; a redraw only happens if something changed


def fmt_speed(value):
    return ("%.0f" if value >= 100 else "%.1f") % value


class Widget:
    def __init__(self, cfg, sampler):
        self.cfg = cfg
        self.sampler = sampler
        self.lang = Lang(cfg["lang"])
        self.pal = theme.palette(cfg["theme"])

        self.root = tk.Tk()
        diag.install_tk_handler(self.root)
        self.root.withdraw()
        self.root.title("SysMonitor")

        dpi = self.root.winfo_fpixels("1i")
        self.scale = dpi / 96.0
        self.root.tk.call("tk", "scaling", dpi / 72.0)

        self.root.overrideredirect(True)
        self.root.attributes("-topmost", bool(cfg["always_on_top"]))
        self.root.attributes("-alpha", float(cfg["opacity"]))
        self.root.attributes("-transparentcolor", TRANSPARENT_KEY)
        self.root.configure(bg=TRANSPARENT_KEY)

        self.canvas = tk.Canvas(self.root, bg=TRANSPARENT_KEY, highlightthickness=0,
                                bd=0, takefocus=0)
        self.canvas.pack(fill="both", expand=True)
        self.content = tk.Canvas(self.root, bg=self.pal["panel"],
                                 highlightthickness=0, bd=0, takefocus=0)

        self.p = Painter(self.canvas)          # panel, sidebar, mini view
        self.pc = Painter(self.content)        # scrolling detail area

        self.expanded = False
        self.views = []
        self.view_index = 0
        self.last_rotate = 0.0
        self.pulse_on = False
        self.last_snapshot_id = None
        self.hot_items = []                    # (painter, shape_key, text_key)
        self.scroll_y = 0.0
        self.content_height = 1

        self._drag = None
        self._resize = None
        self._slider_ui = None
        self._slider_drag = False
        self._cursor = ""
        self._menu = None
        self._bound = set()                    # (canvas id, tag, sequence)

        self._bind()
        self._apply_geometry(initial=True)
        self.rebuild_views()
        self.render()
        self.root.deiconify()
        self.root.after(TICK, self._tick)
        self.root.after(PULSE_EVERY, self._pulse)

    # ---------------------------------------------------------------- basics
    def f(self, size, bold=False):
        return ("Segoe UI", size, "bold") if bold else ("Segoe UI", size)

    def s(self, value):
        return int(round(value * self.scale))

    def _bind_tag(self, cv, tag, sequence, handler):
        """Bind a control once.

        Tags are derived from stable keys and the items behind them are reused,
        so re-binding on every pass would only churn Tcl commands.
        """
        ident = (id(cv), tag, sequence)
        if ident in self._bound:
            return
        cv.tag_bind(tag, sequence, handler)
        self._bound.add(ident)

    # ------------------------------------------------------------- geometry
    def _panel_size(self):
        if self.expanded:
            return self.cfg["exp_w"], self.cfg["exp_h"]
        return self.cfg["mini_w"], self.cfg["mini_h"]

    def _set_panel_size(self, width, height):
        low, high = ((config.MIN_EXP, (4000, 3000)) if self.expanded
                     else (config.MIN_MINI, config.MAX_MINI))
        width = max(low[0], min(int(width), high[0]))
        height = max(low[1], min(int(height), high[1]))
        if self.expanded:
            self.cfg["exp_w"], self.cfg["exp_h"] = width, height
        else:
            self.cfg["mini_w"], self.cfg["mini_h"] = width, height

    @property
    def pw(self):
        return self._panel_size()[0]

    @property
    def ph(self):
        return self._panel_size()[1]

    def _apply_geometry(self, initial=False):
        pw, ph = self._panel_size()
        width = self.s(pw + MARGIN * 2)
        height = self.s(ph + MARGIN * 2)
        if initial:
            x, y = self.cfg["pos_x"], self.cfg["pos_y"]
            if x is None or y is None:
                x = self.root.winfo_screenwidth() - width - self.s(20)
                y = self.s(40)
        else:
            # Keep the top-left anchored when growing or shrinking.
            x, y = self.root.winfo_x(), self.root.winfo_y()
        x, y = self._clamp_to_screen(int(x), int(y), width, height)
        self.root.geometry("%dx%d+%d+%d" % (width, height, x, y))

    def _clamp_to_screen(self, x, y, width, height):
        area = win32.work_area(x + width // 2, y + height // 2)
        if area is None:
            return x, y
        left, top, right, bottom = area
        x = max(left, min(x, right - width))
        y = max(top, min(y, bottom - height))
        return x, y

    # --------------------------------------------------------------- events
    def _bind(self):
        for widget in (self.canvas, self.content):
            widget.bind("<Button-1>", self._on_press)
            widget.bind("<B1-Motion>", self._on_drag)
            widget.bind("<ButtonRelease-1>", self._on_release)
            widget.bind("<Double-Button-1>", self._on_double)
            widget.bind("<Button-3>", self._on_context)
            widget.bind("<Motion>", self._on_motion)
        self.content.bind("<MouseWheel>", self._on_wheel)
        self.root.bind("<Escape>", lambda e: self.collapse())

    def _resize_mode(self, event):
        """Which edge, if any, the pointer is over -- in root window coords."""
        m = self.s(MARGIN)
        px = m + self.s(self.pw)
        py = m + self.s(self.ph)
        x = event.x_root - self.root.winfo_x()
        y = event.y_root - self.root.winfo_y()
        # Zones must stay *inside* the panel: anything past px/py, and the
        # corner outside the rounded arc, is transparent and clicks through.
        grip, edge = self.s(GRIP) + self.s(4), self.s(EDGE)
        corner = self.s(RADIUS) + self.s(6)
        if (px - grip <= x <= px - self.s(2)
                and py - grip <= y <= py - self.s(2)):
            return "se"
        if px - edge <= x <= px - 1 and m + corner <= y <= py - grip:
            return "e"
        if py - edge <= y <= py - 1 and m + corner <= x <= px - grip:
            return "s"
        return None

    def _on_motion(self, event):
        if self._drag or self._resize:
            return
        cursor = {"se": "sizing", "e": "sb_h_double_arrow",
                  "s": "sb_v_double_arrow"}.get(self._resize_mode(event), "")
        if cursor != self._cursor:
            self._cursor = cursor
            try:
                self.root.configure(cursor=cursor)
            except tk.TclError:
                pass

    def _on_press(self, event):
        mode = self._resize_mode(event)
        if mode:
            self._resize = (mode, event.x_root, event.y_root, self.pw, self.ph)
            return "break"
        self._drag = (event.x_root, event.y_root,
                      self.root.winfo_x(), self.root.winfo_y(), False)

    def _on_drag(self, event):
        if self._slider_drag:
            self._slider_apply(event.x)
            return "break"
        if self._resize is not None:
            mode, sx, sy, w0, h0 = self._resize
            dx = (event.x_root - sx) / self.scale
            dy = (event.y_root - sy) / self.scale
            self._set_panel_size(w0 + dx if "e" in mode else w0,
                                 h0 + dy if "s" in mode else h0)
            self._apply_geometry()
            self.render()
            return "break"
        if self._drag is None:
            return
        sx, sy, wx, wy, moved = self._drag
        dx, dy = event.x_root - sx, event.y_root - sy
        if not moved and abs(dx) < 3 and abs(dy) < 3:
            return
        self._drag = (sx, sy, wx, wy, True)
        self.root.geometry("+%d+%d" % (wx + dx, wy + dy))

    def _on_release(self, event):
        if self._slider_drag:
            self._slider_drag = False
            self.cfg.save()      # one write per drag, not one per pixel
            return "break"
        if self._resize is not None:
            self._resize = None
            self.cfg.save()
            return "break"
        if self._drag is None:
            return
        moved = self._drag[4]
        self._drag = None
        if not moved:
            return
        if self.cfg["snap"]:
            self._snap()
        self.cfg["pos_x"] = self.root.winfo_x()
        self.cfg["pos_y"] = self.root.winfo_y()
        self.cfg.save()

    def _snap(self):
        x, y = self.root.winfo_x(), self.root.winfo_y()
        w, h = self.root.winfo_width(), self.root.winfo_height()
        area = win32.work_area(x + w // 2, y + h // 2)
        if area is None:
            return
        left, top, right, bottom = area
        margin = self.s(SNAP_MARGIN)
        if abs(x - left) < margin:
            x = left
        elif abs((x + w) - right) < margin:
            x = right - w
        if abs(y - top) < margin:
            y = top
        elif abs((y + h) - bottom) < margin:
            y = bottom - h
        self.root.geometry("+%d+%d" % (x, y))

    def _on_double(self, event):
        if self.expanded:
            self.collapse()
        else:
            self.expand()
        return "break"

    def _on_wheel(self, event):
        viewport = self.content.winfo_height()
        if self.content_height <= viewport:
            return
        self.scroll_y -= event.delta / 120.0 * self.s(40)
        self.scroll_y = max(0.0, min(self.scroll_y, self.content_height - viewport))
        self.render_content()

    def _on_context(self, event):
        if self._menu is not None:
            self._menu.destroy()
        menu = tk.Menu(self.root, tearoff=0)
        t = self.lang
        menu.add_command(label=t("collapse") if self.expanded else t("expand_hint"),
                         command=self.collapse if self.expanded else self.expand)
        menu.add_command(label=t("reset_size"), command=self.reset_size)
        menu.add_separator()
        menu.add_command(label=t("close"), command=self.quit)
        self._menu = menu
        menu.tk_popup(event.x_root, event.y_root)
        return "break"

    # ----------------------------------------------------------- mode switch
    def expand(self):
        if self.expanded:
            return
        self.expanded = True
        self.scroll_y = 0.0
        self._apply_geometry()
        self.render()

    def collapse(self):
        if not self.expanded:
            return
        self.expanded = False
        self.content.place_forget()
        self._apply_geometry()
        self.rebuild_views()
        self.render()

    def reset_size(self):
        for key, value in config.DEFAULT_SIZES.items():
            self.cfg[key] = value
        self._apply_geometry()
        self.cfg.save()
        self.render()

    def quit(self):
        self.cfg["pos_x"] = self.root.winfo_x()
        self.cfg["pos_y"] = self.root.winfo_y()
        self.cfg.save()
        self.sampler.stop()
        try:
            self.root.destroy()
        except tk.TclError:
            pass

    # ------------------------------------------------------------ view list
    def rebuild_views(self):
        snap = self.sampler.read()
        views = []
        cores = len(snap.cores) or 1
        if self.cfg["show_cpu"]:
            if self.cfg["cpu_mode"] == "separated":
                for start in range(0, cores, 4):
                    views.append(("cpu", start, min(start + 4, cores)))
            else:
                views.append(("cpu", None, None))
        if self.cfg["show_ram"]:
            views.append(("ram", None, None))
        if self.cfg["show_disk"]:
            if self.cfg["disk_mode"] == "separated":
                for index in range(len(snap.disks)):
                    views.append(("disk", index, None))
            else:
                views.append(("disk", None, None))
        if not views:
            views.append(("empty", None, None))
        self.views = views
        self.view_index = min(self.view_index, len(views) - 1)

    def _layout_stale(self, snap):
        if self.cfg["show_disk"] and self.cfg["disk_mode"] == "separated":
            if sum(1 for v in self.views if v[0] == "disk") != len(snap.disks):
                return True
        if self.cfg["show_cpu"] and self.cfg["cpu_mode"] == "separated":
            chunks = sum(1 for v in self.views if v[0] == "cpu")
            if chunks != max(1, (len(snap.cores) + 3) // 4):
                return True
        return False

    # ---------------------------------------------------------------- timers
    def _tick(self):
        try:
            # Receive completed samples without waiting for hardware I/O.
            self.sampler.poll()
            snap = self.sampler.read()
            changed = id(snap) != self.last_snapshot_id
            if changed:
                self.last_snapshot_id = id(snap)
                if self._layout_stale(snap):
                    self.rebuild_views()
            now = time.monotonic()
            rotate = (not self.expanded and len(self.views) > 1
                      and now - self.last_rotate >= ROTATE_EVERY)
            if rotate:
                self.last_rotate = now
                self.view_index = (self.view_index + 1) % len(self.views)
            if changed or rotate:
                self.refresh()
        except tk.TclError:
            diag.report_exception("UI tick")
        except Exception:
            diag.report_exception("UI tick")
        try:
            self.root.after(TICK, self._tick)
        except tk.TclError:
            return

    def _pulse(self):
        try:
            if self.hot_items:
                self.pulse_on = not self.pulse_on
                fill = theme.TEMP_HOT if self.pulse_on else self.pal["badge_hot"]
                text = self.pal["panel"] if self.pulse_on else theme.TEMP_HOT
                for painter, shape_key, text_key in self.hot_items:
                    painter.set(shape_key, fill=fill)
                    painter.set(text_key, fill=text)
        except tk.TclError:
            return
        self.root.after(PULSE_EVERY, self._pulse)

    # --------------------------------------------------------- draw helpers
    def _round_points(self, x1, y1, x2, y2, r):
        r = max(0.0, min(r, (x2 - x1) / 2.0, (y2 - y1) / 2.0))
        k = 0.5523 * r
        return [
            x1 + r, y1, x2 - r, y1, x2 - k, y1, x2, y1 + k, x2, y1 + r,
            x2, y2 - r, x2, y2 - k, x2 - k, y2, x2 - r, y2,
            x1 + r, y2, x1 + k, y2, x1, y2 - k, x1, y2 - r,
            x1, y1 + r, x1, y1 + k, x1 + k, y1,
        ]

    def round_rect(self, p, key, x1, y1, x2, y2, r, **kwargs):
        kwargs.setdefault("smooth", True)
        kwargs.setdefault("splinesteps", 8)
        return p.poly(key, self._round_points(x1, y1, x2, y2, r), **kwargs)

    def bar(self, p, key, x, y, w, h, value, color, empty=None):
        self.round_rect(p, key + "/bg", x, y, x + w, y + h, h / 2.0,
                        fill=empty or self.pal["bar_empty"], outline="")
        filled = max(0, min(100, value)) / 100.0 * w
        self.round_rect(p, key + "/fill", x, y, x + max(filled, h), y + h, h / 2.0,
                        fill=color, outline="")

    def _temp_icon_points(self, x, y, hot):
        """Flame or thermometer as a single polygon.

        Same item kind either way, so crossing the alert threshold restyles the
        icon instead of replacing it.
        """
        if hot:
            h = self.s(5)
            return [x, y + h, x + h * 0.85, y + h, x + h * 0.42, y - h]
        # Thermometer: narrow stem over a round bulb.
        w = self.s(1.1)
        stem = self.s(5.5)
        r = self.s(2.1)
        cx = x + w
        top = y - stem
        pts = [cx - w, top, cx + w, top, cx + w, y]
        for i in range(9):                       # bulb, right side around left
            a = math.radians(-60 + i * (300.0 / 8))
            pts += [cx + r * math.sin(a), y + r * 0.9 - r * math.cos(a)]
        pts += [cx - w, y]
        return pts

    def badge(self, p, key, x, y, temp, estimated, anchor="w"):
        """Temperature pill.  Hot ones register for the pulse animation."""
        fg, bg = theme.temp_colors(temp, self.pal)
        text = sensors.temp_text(temp, estimated)
        pad = self.s(5)
        icon = self.s(9)
        width = self.s(6.4) * len(text) + pad * 2 + icon
        height = self.s(17)
        if anchor == "e":
            x -= width
        hot = sensors.temp_is_hot(temp)
        self.round_rect(p, key + "/bg", x, y, x + width, y + height, height / 2.0,
                        fill=bg, outline="")
        # A thermometer marks every badge, so "n/a" reads as "no temperature
        # sensor" rather than as an unlabelled mystery value.
        p.poly(key + "/icon",
               self._temp_icon_points(x + pad, y + height / 2, hot),
               fill=fg, outline="", smooth=False)
        p.text(key + "/txt", x + (width + icon) / 2.0, y + height / 2.0,
               text=text, fill=fg, font=self.f(7, True))
        if hot:
            self.hot_items.append((p, key + "/bg", key + "/txt"))
        return width

    def icon_chip(self, p, key, x, y, size, color):
        self.round_rect(p, key + "/o", x, y, x + size, y + size, self.s(3),
                        fill="", outline=color, width=self.s(1.6))
        inset = size * 0.3
        self.round_rect(p, key + "/i", x + inset, y + inset,
                        x + size - inset, y + size - inset, self.s(1),
                        fill=color, outline="")

    def icon_ram(self, p, key, x, y, size, color):
        self.round_rect(p, key + "/o", x, y + size * 0.2, x + size,
                        y + size * 0.8, self.s(2), fill="", outline=color,
                        width=self.s(1.6))
        for i in range(3):
            px = x + size * (0.28 + i * 0.22)
            p.line(key + "/p%d" % i, [px, y + size * 0.35, px, y + size * 0.65],
                   fill=color, width=self.s(1.4))

    def icon_disk(self, p, key, x, y, size, color):
        p.oval(key + "/o", x, y, x + size, y + size, outline=color,
               width=self.s(1.6), fill="")
        p.oval(key + "/i", x + size * 0.38, y + size * 0.38,
               x + size * 0.62, y + size * 0.62, fill=color, outline="")

    # -------------------------------------------------------------- controls
    def checkbox(self, p, key, x, y, width, label, checked, callback):
        tag = "chk_" + key
        box = self.s(15)
        p.rect(key + "/hit", x, y, x + width, y + self.s(20),
               fill=self.pal["sidebar"], outline="", tags=(tag,))
        accent = theme.ACCENT_CPU if checked else self.pal["border"]
        self.round_rect(p, key + "/box", x, y + self.s(2), x + box,
                        y + self.s(2) + box, self.s(4),
                        fill=accent if checked else "", outline=accent,
                        width=self.s(1.4), tags=(tag,))
        p.line(key + "/tick",
               [x + box * 0.25, y + self.s(2) + box * 0.52,
                x + box * 0.44, y + self.s(2) + box * 0.72,
                x + box * 0.76, y + self.s(2) + box * 0.28],
               fill="#ffffff", width=self.s(1.8), tags=(tag,),
               state="normal" if checked else "hidden")
        p.text(key + "/label", x + box + self.s(8), y + self.s(10), text=label,
               anchor="w", fill=self.pal["text"], font=self.f(8), tags=(tag,))
        self._bind_tag(p.cv, tag, "<Button-1>",
                       lambda e, cb=callback: self._control(cb))
        return self.s(22)

    def segmented(self, p, key, x, y, width, options, current, callback):
        height = self.s(22)
        self.round_rect(p, key + "/plate", x, y, x + width, y + height, self.s(6),
                        fill=self.pal["chip"], outline="")
        seg = width / float(len(options))
        inset = self.s(2)
        last = len(options) - 1
        for index, (value, label) in enumerate(options):
            tag = "seg_%s_%s" % (key, value)
            sub = "%s/%s" % (key, value)
            x1 = x + index * seg
            # Hit area stays inside the rounded plate so its corners survive.
            p.rect(sub + "/hit", x1 + (inset if index == 0 else 0), y + inset,
                   x1 + seg - (inset if index == last else 0), y + height - inset,
                   fill=self.pal["chip"], outline="", tags=(tag,))
            selected = value == current
            self.round_rect(p, sub + "/sel", x1 + inset, y + inset,
                            x1 + seg - inset, y + height - inset, self.s(5),
                            fill=theme.ACCENT_CPU, outline="", tags=(tag,),
                            state="normal" if selected else "hidden")
            p.text(sub + "/txt", x1 + seg / 2.0, y + height / 2.0, text=label,
                   fill="#ffffff" if selected else self.pal["muted"],
                   font=self.f(7, selected), tags=(tag,))
            self._bind_tag(p.cv, tag, "<Button-1>",
                           lambda e, v=value, cb=callback: self._control(cb, v))
        return height + self.s(6)

    def slider(self, p, key, x, y, width, value, callback):
        tag = "sld_" + key
        track = self.s(5)
        cy = y + self.s(9)
        self.round_rect(p, key + "/hit", x - self.s(6), y - self.s(1),
                        x + width + self.s(6), y + self.s(19), self.s(6),
                        fill=self.pal["chip"], outline="", tags=(tag,))
        self.round_rect(p, key + "/track", x, cy - track / 2.0, x + width,
                        cy + track / 2.0, track / 2.0,
                        fill=self.pal["bar_empty"], outline="", tags=(tag,))
        pos = x + (value - 0.2) / 0.8 * width
        self.round_rect(p, key + "/fill", x, cy - track / 2.0,
                        max(pos, x + track), cy + track / 2.0, track / 2.0,
                        fill=theme.ACCENT_CPU, outline="", tags=(tag,))
        knob = self.s(7)
        p.oval(key + "/knob", pos - knob, cy - knob, pos + knob, cy + knob,
               fill="#ffffff", outline=theme.ACCENT_CPU, width=self.s(2),
               tags=(tag,))
        self._slider_ui = {"p": p, "key": key, "x": x, "width": float(width),
                           "cy": cy, "track": track, "knob": knob,
                           "callback": callback}
        # Only the press is an item binding.  Motion is tracked on the widget
        # (see _on_drag) so the drag survives leaving the thin track.
        self._bind_tag(p.cv, tag, "<Button-1>", self._slider_press)
        return self.s(24)

    def _slider_press(self, event):
        if self._slider_ui is None:
            return None
        self._slider_drag = True
        self._drag = None
        self._resize = None
        self._slider_apply(event.x)
        return "break"

    def _slider_apply(self, canvas_x):
        ui = self._slider_ui
        if ui is None:
            return
        ratio = max(0.0, min(1.0, (canvas_x - ui["x"]) / ui["width"]))
        ui["callback"](round(0.2 + ratio * 0.8, 2))

    def _slider_redraw(self, value):
        ui = self._slider_ui
        if ui is None:
            return
        p, key, x, width = ui["p"], ui["key"], ui["x"], ui["width"]
        cy, track, knob = ui["cy"], ui["track"], ui["knob"]
        pos = x + (value - 0.2) / 0.8 * width
        self.round_rect(p, key + "/fill", x, cy - track / 2.0,
                        max(pos, x + track), cy + track / 2.0, track / 2.0,
                        fill=theme.ACCENT_CPU, outline="")
        p.oval(key + "/knob", pos - knob, cy - knob, pos + knob, cy + knob,
               fill="#ffffff", outline=theme.ACCENT_CPU, width=self.s(2))
        p.set("sb/opacity_val", text="%d%%" % round(value * 100))

    def icon_button(self, p, key, x, y, size, kind, callback):
        tag = "btn_" + key
        hot = theme.TEMP_HOT if kind == "close" else self.pal["muted"]
        self.round_rect(p, key + "/bg", x, y, x + size, y + size, self.s(6),
                        fill=self.pal["control"], outline="", tags=(tag,))
        pad = size * 0.32
        if kind == "close":
            a = [x + pad, y + pad, x + size - pad, y + size - pad]
            b = [x + size - pad, y + pad, x + pad, y + size - pad]
        else:
            a = [x + pad, y + size / 2.0, x + size - pad, y + size / 2.0]
            b = [x + pad, y + size * 0.66, x + size - pad, y + size * 0.66]
        p.line(key + "/a", a, fill=hot, width=self.s(1.8), tags=(tag,))
        p.line(key + "/b", b, fill=hot, width=self.s(1.8), tags=(tag,))
        self._bind_tag(p.cv, tag, "<Button-1>",
                       lambda e, cb=callback: self._control(cb))

    def _control(self, callback, *args):
        """Run a control handler and swallow the event so it never starts a drag."""
        self._drag = None
        callback(*args)
        return "break"

    # ------------------------------------------------------------- rendering
    def refresh(self):
        """Data-only redraw, used by the tick loop."""
        if self.expanded:
            self.render_content()
        else:
            self.render()

    def render(self):
        self.hot_items = [e for e in self.hot_items if e[0] is not self.p]
        self._slider_ui = None
        self.p.begin()
        if self.expanded:
            self.render_expanded()
        else:
            self.content.place_forget()
            self.render_mini()
        self.p.end()

    def _panel(self, p, width, height):
        m = self.s(MARGIN)
        self.round_rect(p, "panel", m, m, m + self.s(width), m + self.s(height),
                        self.s(RADIUS), fill=self.pal["panel"],
                        outline=self.pal["border"], width=self.s(1))
        self._grip(p, m + self.s(width), m + self.s(height))

    def _grip(self, p, px, py):
        """Three diagonal ticks in the bottom-right corner: the resize handle.

        Inset far enough to sit inside the rounded corner -- ticks drawn past
        the arc land on transparent pixels and would be both invisible and
        unclickable.
        """
        base = self.s(7)
        for i, step in enumerate((7, 11, 15)):
            offset = self.s(step)
            p.line("grip%d" % i, [px - offset, py - base, px - base, py - offset],
                   fill=self.pal["label" if i == 1 else "border"],
                   width=self.s(1.4), capstyle="round")

    # ----------------------------------------------------------------- mini
    def render_mini(self):
        p = self.p
        self._panel(p, self.pw, self.ph)
        snap = self.sampler.read()
        m = self.s(MARGIN)
        pad = self.s(12)
        inner = self.s(self.pw) - pad * 2
        cx = m + self.s(self.pw) / 2.0
        cy = m + self.s(self.ph) / 2.0

        if not snap.ready:
            p.text("mini/msg", cx, cy, text=self.lang("connecting"),
                   fill=self.pal["label"], font=self.f(8))
            return

        kind, a, b = self.views[self.view_index]
        if kind == "empty":
            p.text("mini/msg", cx, cy, text=self.lang("no_data"),
                   fill=self.pal["label"], font=self.f(8))
            return

        # The view is a fixed-height block; centre it vertically so a resized
        # mini widget does not leave everything pinned to the top edge.
        if kind == "cpu" and a is not None:
            rows = (min(b, len(snap.cores)) - a + 1) // 2
            block = self.s(26) + max(1, rows) * self.s(20)
        else:
            block = self.s(62)
        x0 = m + pad
        y0 = m + max(pad, (self.s(self.ph) - block) / 2.0)

        if kind == "cpu":
            if a is None:
                label = "%s (%s)" % (self.lang("cpu"), self.lang("total"))
            else:
                label = "%s  C%d-%d" % (self.lang("cpu"), a, b - 1)
            p.text("mini/cpu/label", x0, y0 + self.s(6), text=label.upper(),
                   anchor="w", fill=self.pal["label"], font=self.f(7, True))
            p.text("mini/cpu/total", x0 + inner, y0 + self.s(6),
                   text="%d%%" % snap.cpu_total, anchor="e",
                   fill=theme.ACCENT_CPU, font=self.f(9, True))
            self.badge(p, "mini/cpu/temp", x0 + inner - self.s(46), y0 - self.s(2),
                       snap.cpu_temp, snap.cpu_temp_estimated, anchor="e")
            if a is None:
                self.bar(p, "mini/cpu/bar", x0, y0 + self.s(34), inner, self.s(10),
                         snap.cpu_total, theme.load_color(snap.cpu_total))
            else:
                col_w = (inner - self.s(10)) / 2.0
                for index in range(a, b):
                    if index >= len(snap.cores):
                        break
                    slot = index - a
                    bx = x0 + (slot % 2) * (col_w + self.s(10))
                    by = y0 + self.s(26) + (slot // 2) * self.s(20)
                    p.text("mini/core%d/lbl" % slot, bx, by + self.s(5),
                           text="C%d" % index, anchor="w",
                           fill=self.pal["muted"], font=self.f(7))
                    usage = snap.cores[index].usage
                    self.bar(p, "mini/core%d/bar" % slot, bx + self.s(22), by,
                             col_w - self.s(24), self.s(9), usage,
                             theme.load_color(usage))

        elif kind == "ram":
            self._mini_gauge(p, x0, y0, inner, self.lang("memory"),
                             snap.ram.usage, theme.ACCENT_RAM,
                             "%.1f / %.1f GB" % (snap.ram.used_gb, snap.ram.total_gb),
                             snap.ram.temp, snap.ram.estimated, "ram")

        elif kind == "disk":
            if a is None:
                usage, temp, read, write = self._disk_totals(snap)
                title = "%s (%s)" % (self.lang("disk"), self.lang("all_drives"))
            elif a < len(snap.disks):
                disk = snap.disks[a]
                usage, temp = disk.usage, disk.temp
                read, write = disk.read_mb, disk.write_mb
                title = "%s %s" % (self.lang("drive"), disk.title)
            else:
                return
            desc = "%s %s | %s %s MB/s" % (self.lang("read"), fmt_speed(read),
                                           self.lang("write"), fmt_speed(write))
            self._mini_gauge(p, x0, y0, inner, title, usage, theme.ACCENT_DISK,
                             desc, temp, False, "disk")

    def _mini_gauge(self, p, x, y, width, title, usage, color, desc,
                    temp, estimated, icon):
        size = self.s(34)
        self.round_rect(p, "mini/g/box", x, y + self.s(14), x + size,
                        y + self.s(14) + size, self.s(8),
                        fill=self.pal["control"], outline="")
        pad = size * 0.25
        draw = {"ram": self.icon_ram, "disk": self.icon_disk}.get(icon,
                                                                 self.icon_chip)
        draw(p, "mini/g/icon", x + pad, y + self.s(14) + pad, size - pad * 2, color)

        tx = x + size + self.s(12)
        right = x + width
        p.text("mini/g/title", tx, y + self.s(8), text=title.upper(), anchor="w",
               fill=self.pal["label"], font=self.f(7, True))
        self.badge(p, "mini/g/temp", right, y + self.s(1), temp, estimated,
                   anchor="e")
        p.text("mini/g/pct", tx, y + self.s(32), text="%d%%" % usage, anchor="w",
               fill=self.pal["text"], font=self.f(14, True))
        p.text("mini/g/desc", right, y + self.s(36), text=desc, anchor="e",
               fill=self.pal["muted"], font=self.f(7))
        self.bar(p, "mini/g/bar", tx, y + self.s(50), right - tx, self.s(9),
                 usage, color)

    def _disk_totals(self, snap):
        if not snap.disks:
            return 0, None, 0.0, 0.0
        usage = int(round(sum(d.usage for d in snap.disks) / len(snap.disks)))
        temps = [d.temp for d in snap.disks if d.temp is not None]
        temp = int(round(sum(temps) / len(temps))) if temps else None
        return (usage, temp,
                sum(d.read_mb for d in snap.disks),
                sum(d.write_mb for d in snap.disks))

    # ------------------------------------------------------------- expanded
    def render_expanded(self):
        p = self.p
        self._panel(p, self.pw, self.ph)
        m = self.s(MARGIN)

        # Sidebar plate, square on the right so it butts against the content.
        self.round_rect(p, "sb/plate", m, m, m + self.s(SIDEBAR_W),
                        m + self.s(self.ph), self.s(RADIUS),
                        fill=self.pal["sidebar"], outline="")
        p.rect("sb/edge", m + self.s(SIDEBAR_W) - self.s(RADIUS), m,
               m + self.s(SIDEBAR_W), m + self.s(self.ph),
               fill=self.pal["sidebar"], outline="")
        p.line("sb/divider", [m + self.s(SIDEBAR_W), m + self.s(6),
                              m + self.s(SIDEBAR_W), m + self.s(self.ph) - self.s(6)],
               fill=self.pal["border"])

        self._render_sidebar(p, m + self.s(16), m + self.s(16),
                             self.s(SIDEBAR_W - 32))

        size = self.s(26)
        top = m + self.s(14)
        right = m + self.s(self.pw) - self.s(14)
        self.icon_button(p, "btn/close", right - size, top, size, "close",
                         self.quit)
        self.icon_button(p, "btn/collapse", right - size * 2 - self.s(8), top,
                         size, "collapse", self.collapse)

        cx = m + self.s(SIDEBAR_W + 16)
        cy = m + self.s(50)
        cw = self.s(self.pw - SIDEBAR_W - 30)
        ch = self.s(self.ph - 50 - 14)
        self.content.configure(bg=self.pal["panel"])
        self.content.place(x=cx, y=cy, width=cw, height=ch)
        self.render_content()

    def _render_sidebar(self, p, x, y, width):
        t = self.lang
        p.text("sb/title", x, y + self.s(6), text=t("title"), anchor="w",
               fill=self.pal["text"], font=self.f(12, True))
        p.text("sb/ver", x + width, y + self.s(8), text="v2.0", anchor="e",
               fill=self.pal["label"], font=self.f(7))
        y += self.s(30)

        def section(key, label, y):
            p.text("sb/sec/" + key, x, y, text=label.upper(), anchor="w",
                   fill=self.pal["label"], font=self.f(7, True))
            return y + self.s(14)

        y = section("win", t("window_settings"), y)
        y += self.checkbox(p, "sb/top", x, y, width, t("always_on_top"),
                           self.cfg["always_on_top"], self._toggle_top)
        y += self.checkbox(p, "sb/snap", x, y, width, t("snap"), self.cfg["snap"],
                           self._toggle_snap)
        y += self.checkbox(p, "sb/light", x, y, width, t("light_mode"),
                           self.cfg["theme"] == "light", self._toggle_theme)

        y += self.s(10)
        y = section("disp", t("display_settings"), y)
        y += self.checkbox(p, "sb/cpu", x, y, width, t("show_cpu"),
                           self.cfg["show_cpu"],
                           lambda: self._toggle_show("show_cpu"))
        y += self.segmented(p, "sb/cpumode", x, y, width,
                            [("separated", t("per_core")), ("total", t("total"))],
                            self.cfg["cpu_mode"],
                            lambda v: self._set_mode("cpu_mode", v))
        y += self.checkbox(p, "sb/ram", x, y, width, t("show_ram"),
                           self.cfg["show_ram"],
                           lambda: self._toggle_show("show_ram"))
        y += self.checkbox(p, "sb/disk", x, y, width, t("show_disk"),
                           self.cfg["show_disk"],
                           lambda: self._toggle_show("show_disk"))
        y += self.segmented(p, "sb/diskmode", x, y, width,
                            [("separated", t("per_drive")), ("total", t("total"))],
                            self.cfg["disk_mode"],
                            lambda v: self._set_mode("disk_mode", v))

        y += self.s(10)
        y = section("speed", t("speed"), y)
        y += self.segmented(p, "sb/speed", x, y, width,
                            [("eco", t("eco")), ("balanced", t("balanced")),
                             ("fast", t("fast"))],
                            self.cfg["speed"], self._set_speed)

        y = section("lang", t("language"), y + self.s(6))
        y += self.segmented(p, "sb/lang", x, y, width,
                            [("th", "ไทย"), ("en", "English")],
                            self.cfg["lang"], self._set_lang)

        bottom = self.s(MARGIN + self.ph - 62)
        p.text("sb/opacity", x, bottom, text=t("opacity"), anchor="w",
               fill=self.pal["label"], font=self.f(7, True))
        p.text("sb/opacity_val", x + width, bottom,
               text="%d%%" % round(self.cfg["opacity"] * 100), anchor="e",
               fill=self.pal["muted"], font=self.f(7))
        self.slider(p, "sb/slider", x, bottom + self.s(12), width,
                    self.cfg["opacity"], self._set_opacity)

    def render_content(self):
        p = self.pc
        # Hot badges on this canvas are rebuilt below; drop the old entries so
        # the pulse timer never holds keys that this pass will not redraw.
        self.hot_items = [e for e in self.hot_items if e[0] is not p]
        p.begin()
        snap = self.sampler.read()
        t = self.lang
        width = self.content.winfo_width() or self.s(self.pw - SIDEBAR_W - 30)
        inner = width - self.s(10)
        y = self.s(4) - self.scroll_y
        x = 0

        if not snap.ready:
            p.text("connecting", width / 2.0, self.s(60),
                   text=t("connecting"), fill=self.pal["label"], font=self.f(9))
            self.content_height = 1
            p.end()
            return

        if not (self.cfg["show_cpu"] or self.cfg["show_ram"] or self.cfg["show_disk"]):
            p.text("empty", width / 2.0, self.s(60), text=t("no_data"),
                   fill=self.pal["label"], font=self.f(9))
            self.content_height = 1
            p.end()
            return

        if self.cfg["show_cpu"]:
            y = self._section_header(p, "cpu", x, y, inner,
                                     "%s  (%d %s)" % (t("cpu"), len(snap.cores),
                                                      t("cores")),
                                     theme.ACCENT_CPU, self.icon_chip,
                                     snap.cpu_temp, snap.cpu_temp_estimated,
                                     "%d%%" % snap.cpu_total)
            self.round_rect(p, "cpu/box", x, y, x + inner, y + self.s(30),
                            self.s(6), fill=self.pal["control"], outline="")
            self.bar(p, "cpu/bar", x + self.s(10), y + self.s(10),
                     inner - self.s(20), self.s(10), snap.cpu_total,
                     theme.load_color(snap.cpu_total))
            y += self.s(40)
            if self.cfg["cpu_mode"] == "separated":
                col_w = (inner - self.s(10)) / 2.0
                for index, core in enumerate(snap.cores):
                    key = "core%d" % index
                    cx = x + (index % 2) * (col_w + self.s(10))
                    cy = y + (index // 2) * self.s(34)
                    self.round_rect(p, key + "/box", cx, cy, cx + col_w,
                                    cy + self.s(28), self.s(6),
                                    fill=self.pal["control"], outline="")
                    p.text(key + "/lbl", cx + self.s(9), cy + self.s(14),
                           text="Core %d" % index, anchor="w",
                           fill=self.pal["muted"], font=self.f(7))
                    # Right block holds the badge plus the % readout; the bar
                    # takes what is left so the two never collide.
                    bw = max(self.s(20), col_w - self.s(44) - self.s(88))
                    self.bar(p, key + "/bar", cx + self.s(44), cy + self.s(10),
                             bw, self.s(8), core.usage,
                             theme.load_color(core.usage))
                    self.badge(p, key + "/temp", cx + col_w - self.s(36),
                               cy + self.s(6), core.temp, core.estimated,
                               anchor="e")
                    p.text(key + "/pct", cx + col_w - self.s(8), cy + self.s(14),
                           text="%d%%" % core.usage, anchor="e",
                           fill=theme.load_color(core.usage), font=self.f(7, True))
                y += ((len(snap.cores) + 1) // 2) * self.s(34)
            y += self.s(8)

        if self.cfg["show_ram"]:
            y = self._section_header(p, "ram", x, y, inner, t("memory"),
                                     theme.ACCENT_RAM, self.icon_ram,
                                     snap.ram.temp, snap.ram.estimated, "")
            self.round_rect(p, "ram/box", x, y, x + inner, y + self.s(54),
                            self.s(6), fill=self.pal["control"], outline="")
            p.text("ram/pct", x + self.s(12), y + self.s(17),
                   text="%d%%" % snap.ram.usage, anchor="w",
                   fill=theme.ACCENT_RAM, font=self.f(14, True))
            p.text("ram/desc", x + inner - self.s(12), y + self.s(18),
                   text="%.1f GB / %.1f GB %s" % (snap.ram.used_gb,
                                                  snap.ram.total_gb, t("in_use")),
                   anchor="e", fill=self.pal["muted"], font=self.f(7))
            self.bar(p, "ram/bar", x + self.s(12), y + self.s(34),
                     inner - self.s(24), self.s(10), snap.ram.usage,
                     theme.ACCENT_RAM)
            y += self.s(66)

        if self.cfg["show_disk"]:
            header_y = y
            y = self._section_header(p, "disk", x, y, inner, t("disk"),
                                     theme.ACCENT_DISK, self.icon_disk, None,
                                     False, "", show_temp=False)
            # Spell out what the n/a badges on the cards below mean, right
            # where they are -- the full legend sits further down the scroll.
            if any(d.temp is None for d in snap.disks):
                p.text("disk/note", x + inner, header_y + self.s(9),
                       text=t("no_sensor"), anchor="e", fill=self.pal["label"],
                       font=self.f(7))
                box = p.bbox("disk/note")
                left = box[0] if box else x + inner - self.s(150)
                p.poly("disk/note_icon",
                       self._temp_icon_points(left - self.s(11),
                                              header_y + self.s(11), False),
                       fill=self.pal["label"], outline="", smooth=False)

            if self.cfg["disk_mode"] == "total":
                usage, temp, read, write = self._disk_totals(snap)
                self.round_rect(p, "disk/box", x, y, x + inner, y + self.s(54),
                                self.s(6), fill=self.pal["control"], outline="")
                p.text("disk/pct", x + self.s(12), y + self.s(17),
                       text="%d%%" % usage, anchor="w", fill=theme.ACCENT_DISK,
                       font=self.f(14, True))
                p.text("disk/rw", x + inner - self.s(12), y + self.s(18),
                       text="%s %s | %s %s MB/s" % (t("read"), fmt_speed(read),
                                                    t("write"), fmt_speed(write)),
                       anchor="e", fill=self.pal["muted"], font=self.f(7))
                self.bar(p, "disk/bar", x + self.s(12), y + self.s(34),
                         inner - self.s(24), self.s(10), usage, theme.ACCENT_DISK)
                y += self.s(66)
            else:
                col_w = (inner - self.s(10)) / 2.0
                for index, disk in enumerate(snap.disks):
                    cx = x + (index % 2) * (col_w + self.s(10))
                    cy = y + (index // 2) * self.s(84)
                    self._disk_card(p, "d%d" % index, cx, cy, col_w, disk)
                y += ((len(snap.disks) + 1) // 2) * self.s(84) + self.s(4)

        # Legend, drawn with the same glyph the badges use so the mapping is
        # unambiguous rather than relying on an emoji font.
        p.poly("legend/icon",
               self._temp_icon_points(x, y + self.s(11), False),
               fill=self.pal["label"], outline="", smooth=False)
        p.text("legend", x + self.s(12), y + self.s(6), text=t("temp_legend"),
               anchor="w", fill=self.pal["label"], font=self.f(7))
        y += self.s(22)

        self.content_height = y + self.scroll_y
        viewport = self.content.winfo_height() or self.s(self.ph)
        if self.content_height > viewport:
            self._scroll_indicator(p, width, viewport)
        p.end()

    def _disk_card(self, p, key, x, y, width, disk):
        t = self.lang
        self.round_rect(p, key + "/box", x, y, x + width, y + self.s(78),
                        self.s(6), fill=self.pal["control"], outline="")
        p.text(key + "/title", x + self.s(10), y + self.s(15), text=disk.title,
               anchor="w", fill=self.pal["text"], font=self.f(8, True))
        p.text(key + "/pct", x + width - self.s(10), y + self.s(15),
               text="%d%%" % disk.usage, anchor="e", fill=theme.ACCENT_DISK,
               font=self.f(10, True))

        tx = x + self.s(10)
        for i, tag in enumerate((disk.media, disk.bus)):
            tw = self.s(7) * len(tag) + self.s(10)
            self.round_rect(p, "%s/tag%d/bg" % (key, i), tx, y + self.s(26),
                            tx + tw, y + self.s(41), self.s(3),
                            fill=self.pal["tag"], outline="")
            p.text("%s/tag%d/txt" % (key, i), tx + tw / 2.0, y + self.s(33.5),
                   text=tag, fill=self.pal["muted"], font=self.f(7))
            tx += tw + self.s(5)
        self.badge(p, key + "/temp", tx, y + self.s(26), disk.temp, disk.estimated)

        p.text(key + "/rw", x + self.s(10), y + self.s(52),
               text="%s %s | %s %s MB/s" % (t("read"), fmt_speed(disk.read_mb),
                                            t("write"), fmt_speed(disk.write_mb)),
               anchor="w", fill=self.pal["muted"], font=self.f(7))
        p.text(key + "/size", x + width - self.s(10), y + self.s(52),
               text="%.0f/%.0f GB" % (disk.used_gb, disk.total_gb), anchor="e",
               fill=self.pal["label"], font=self.f(7))
        self.bar(p, key + "/bar", x + self.s(10), y + self.s(62),
                 width - self.s(20), self.s(8), disk.usage, theme.ACCENT_DISK)

    def _section_header(self, p, key, x, y, width, title, color, icon,
                        temp, estimated, right_text, show_temp=True):
        size = self.s(14)
        icon(p, key + "/hicon", x, y + self.s(2), size, color)
        p.text(key + "/htitle", x + size + self.s(8), y + self.s(9), text=title,
               anchor="w", fill=self.pal["text"], font=self.f(9, True))
        rx = x + width
        if right_text:
            p.text(key + "/hright", rx, y + self.s(9), text=right_text,
                   anchor="e", fill=color, font=self.f(11, True))
            rx -= self.s(52)
        if show_temp:
            self.badge(p, key + "/htemp", rx, y, temp, estimated, anchor="e")
        p.line(key + "/hrule", [x, y + self.s(24), x + width, y + self.s(24)],
               fill=self.pal["border"])
        return y + self.s(32)

    def _scroll_indicator(self, p, width, viewport):
        track_x = width - self.s(4)
        p.line("scroll/track", [track_x, 0, track_x, viewport],
               fill=self.pal["control"], width=self.s(3))
        span = max(0.1, viewport / float(self.content_height))
        top = (self.scroll_y / float(self.content_height)) * viewport
        p.line("scroll/thumb", [track_x, top, track_x, top + viewport * span],
               fill=self.pal["border"], width=self.s(3))

    # -------------------------------------------------------------- handlers
    def _toggle_top(self):
        self.cfg["always_on_top"] = not self.cfg["always_on_top"]
        self.root.attributes("-topmost", bool(self.cfg["always_on_top"]))
        self._after_change()

    def _toggle_snap(self):
        self.cfg["snap"] = not self.cfg["snap"]
        self._after_change()

    def _toggle_theme(self):
        self.cfg["theme"] = "dark" if self.cfg["theme"] == "light" else "light"
        self.pal = theme.palette(self.cfg["theme"])
        self._after_change()

    def _toggle_show(self, key):
        self.cfg[key] = not self.cfg[key]
        self.rebuild_views()
        self._after_change()

    def _set_mode(self, key, value):
        self.cfg[key] = value
        self.rebuild_views()
        self._after_change()

    def _set_speed(self, value):
        self.cfg["speed"] = value
        self.sampler.nudge()
        self._after_change()

    def _set_lang(self, value):
        self.cfg["lang"] = value
        self.lang.set(value)
        self._after_change()

    def _set_opacity(self, value):
        self.cfg["opacity"] = value
        self.root.attributes("-alpha", value)
        self._slider_redraw(value)
        if not self._slider_drag:
            self.cfg.save()

    def _after_change(self):
        self.cfg.save()
        self.render()

    def run(self):
        self.root.mainloop()
