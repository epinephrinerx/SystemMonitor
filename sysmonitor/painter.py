"""Keyed, retained drawing over a Tk canvas.

Tk 9.0 leaks memory for every canvas item that is created and then destroyed.
Measured on this machine: 4.5 MB per 60,000 items, perfectly linear with no
plateau, for every item type, and identically when the calls bypass Tkinter and
go straight to Tcl -- so it is Tk's own C layer, not the Python binding.

A monitor widget repaints itself every couple of seconds forever.  Drawing it
the obvious way (``delete("all")`` then re-create everything) cost ~380 MB/day
with the panel expanded and ~30 MB/day as the mini widget in earlier tests.
An allocator panic was also reported, but its cause remains unproven.

So nothing is thrown away.  Every draw call carries a stable key; the first
pass creates the item, later passes move it with ``coords`` and restyle it with
``itemconfigure``.  The same 60,000 operations done that way leaked 0.01 MB.

Usage:

    p.begin()
    p.text("cpu/label", x, y, text="CPU", fill=colour, font=font)
    ...
    p.end()          # hides anything not drawn this pass

Keys must be stable across passes for the same logical element, and unique
within a pass.
"""

_UNSET = object()


class Painter:
    def __init__(self, canvas):
        self.cv = canvas
        self._items = {}      # (key, kind) -> [kind, item_id, coords, options]
        self._seen = set()
        self._active = {}     # logical key -> (key, kind), for this frame
        self._previous_item = None

    # ------------------------------------------------------------ pass frame
    def begin(self):
        self._seen = set()
        self._active = {}
        self._previous_item = None

    def end(self):
        """Hide absent items; rotation and mode switches must not churn Tk items."""
        for key in [k for k in self._items if k not in self._seen]:
            rec = self._items[key]
            if rec[3].get("state") != "hidden":
                self.cv.itemconfigure(rec[1], state="hidden")
                rec[3]["state"] = "hidden"

    def clear(self):
        self.begin()
        self.end()

    def has(self, key):
        return key in self._active

    def item(self, key):
        rec = self._items.get(self._active.get(key))
        return rec[1] if rec else None

    # ---------------------------------------------------------------- shapes
    def _put(self, key, kind, coords, opts):
        # Retain one item for each key/kind pair: some icons change primitive
        # when the mini view changes. Restore draw order when reusing them.
        ident = (key, kind)
        first_draw = ident not in self._seen
        self._seen.add(ident)
        self._active[key] = ident
        opts = dict(opts)
        opts.setdefault("state", "normal")
        coords = [float(c) for c in coords]
        rec = self._items.get(ident)
        if rec is None:
            item = getattr(self.cv, "create_" + kind)(*coords, **opts)
            rec = [kind, item, coords, dict(opts)]
            self._items[ident] = rec
        item = rec[1]
        if rec[2] != coords:
            self.cv.coords(item, *coords)
            rec[2] = coords
        changed = {k: v for k, v in opts.items() if rec[3].get(k, _UNSET) != v}
        if changed:
            self.cv.itemconfigure(item, **changed)
            rec[3].update(changed)
        if first_draw:
            if self._previous_item is None:
                self.cv.tag_lower(item)
            else:
                self.cv.tag_raise(item, self._previous_item)
            self._previous_item = item
        return item

    def text(self, key, x, y, **opts):
        return self._put(key, "text", (x, y), opts)

    def rect(self, key, x1, y1, x2, y2, **opts):
        return self._put(key, "rectangle", (x1, y1, x2, y2), opts)

    def oval(self, key, x1, y1, x2, y2, **opts):
        return self._put(key, "oval", (x1, y1, x2, y2), opts)

    def line(self, key, coords, **opts):
        return self._put(key, "line", coords, opts)

    def poly(self, key, coords, **opts):
        return self._put(key, "polygon", coords, opts)

    def set(self, key, **opts):
        """Restyle an existing item, keeping the cache in step.

        The pulse animation goes through here; writing to the canvas directly
        would leave the cached options stale and the next pass would think the
        colour was already correct.
        """
        rec = self._items.get(self._active.get(key))
        if rec is None:
            return
        changed = {k: v for k, v in opts.items() if rec[3].get(k, _UNSET) != v}
        if changed:
            try:
                self.cv.itemconfigure(rec[1], **changed)
            except Exception:
                return
            rec[3].update(changed)

    def bbox(self, key):
        rec = self._items.get(self._active.get(key))
        return self.cv.bbox(rec[1]) if rec else None
