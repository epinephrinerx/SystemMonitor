"""Generate SysMonitor.ico (build-time only; needs Pillow).

Draws a dark rounded tile with three ascending bars in the app's accent
colours, at every size Windows asks for.
"""

import os
import sys

from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "SysMonitor.ico")
SIZES = [256, 128, 64, 48, 32, 24, 16]

PANEL = (15, 23, 42, 255)
EDGE = (51, 65, 85, 255)
BARS = [((59, 130, 246, 255), 0.34), ((168, 85, 247, 255), 0.56), ((16, 185, 129, 255), 0.80)]


def render(size):
    # Supersample, then downscale: gives clean edges at 16px.
    scale = 8 if size <= 64 else 2
    s = size * scale
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    pad = s * 0.04
    d.rounded_rectangle([pad, pad, s - pad, s - pad], radius=s * 0.22,
                        fill=PANEL, outline=EDGE, width=max(1, int(s * 0.02)))

    bar_w = s * 0.14
    gap = s * 0.10
    total = len(BARS) * bar_w + (len(BARS) - 1) * gap
    x = (s - total) / 2
    base = s * 0.78
    for color, height in BARS:
        top = base - (base - s * 0.20) * height
        d.rounded_rectangle([x, top, x + bar_w, base], radius=bar_w / 2, fill=color)
        x += bar_w + gap

    return img.resize((size, size), Image.LANCZOS)


def main():
    frames = [render(n) for n in SIZES]
    out = os.path.normpath(OUT)
    frames[0].save(out, format="ICO",
                   sizes=[(n, n) for n in SIZES], append_images=frames[1:])
    print("wrote %s (%d bytes)" % (out, os.path.getsize(out)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
