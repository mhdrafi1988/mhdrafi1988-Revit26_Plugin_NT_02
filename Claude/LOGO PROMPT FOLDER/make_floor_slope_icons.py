"""Draws the Floor Slope ribbon icons (plugin 6.31.1).

    python3 "Claude/LOGO PROMPT FOLDER/make_floor_slope_icons.py" Resources/Icons/FloorTools

Writes FloorSlopeByDrain_16/32.png and FloorSlopeByPoint_16/32.png in the house style of
Resources/Icons/FloorTools: a full orange square (#E8752C, the Floor Tools colour) with white
line art. Each icon is drawn at 16x its size and downsampled, so edges are anti-aliased.

Both icons use the drafting symbol for floor falls: the slab outline in plan, with fall lines from
its corners into the drain. By Drain: four fall arrows into one central drain (ring and dot). By
Point: two picked points (ring and dot), each taking the falls from its half, with the ridge line
between them. The 16 px versions are drawn on their own grid with plain fall lines, a solid drain
dot and no ridge line, so they stay legible. Outlines sit on whole pixels so they stay sharp.
"""

import math
import os
import sys

from PIL import Image, ImageDraw

ORANGE = (232, 117, 44, 255)       # Floor Tools background (FloorRoofFromRooms_32.png)
WHITE = (255, 255, 255, 255)
SS = 16                             # supersampling factor


class Pen:
    """Draws on a supersampled canvas in icon-pixel coordinates."""

    def __init__(self, size):
        self.size = size
        self.img = Image.new("RGBA", (size * SS, size * SS), ORANGE)
        self.d = ImageDraw.Draw(self.img)

    def _p(self, x, y):
        return (x * SS, y * SS)

    def line(self, pts, w, fill=WHITE):
        self.d.line([self._p(*p) for p in pts], fill=fill, width=round(w * SS), joint="curve")
        for p in (pts[0], pts[-1]):                      # round caps
            self.dot(p, w / 2, fill)

    def rect(self, x0, y0, x1, y1, w, fill=WHITE):
        self.d.rectangle([self._p(x0, y0), self._p(x1, y1)], outline=fill, width=round(w * SS))

    def dot(self, c, r, fill=WHITE):
        x, y = c
        self.d.ellipse([self._p(x - r, y - r), self._p(x + r, y + r)], fill=fill)

    def ring(self, c, r, w, fill=WHITE):
        x, y = c
        self.d.ellipse([self._p(x - r, y - r), self._p(x + r, y + r)], outline=fill, width=round(w * SS))

    def arrow(self, a, b, w, head):
        """Shaft from a to b, with a filled head at b."""
        ang = math.atan2(b[1] - a[1], b[0] - a[0])
        back = (b[0] - head * 0.8 * math.cos(ang), b[1] - head * 0.8 * math.sin(ang))
        self.line([a, back], w)
        left = (b[0] - head * math.cos(ang - 0.6), b[1] - head * math.sin(ang - 0.6))
        right = (b[0] - head * math.cos(ang + 0.6), b[1] - head * math.sin(ang + 0.6))
        self.d.polygon([self._p(*b), self._p(*left), self._p(*right)], fill=WHITE)

    def save(self, path):
        self.img.resize((self.size, self.size), Image.LANCZOS).save(path)


def toward(a, b, stop):
    """Point on the segment a -> b, `stop` short of b."""
    dx, dy = b[0] - a[0], b[1] - a[1]
    n = math.hypot(dx, dy)
    return (b[0] - dx / n * stop, b[1] - dy / n * stop)


def by_drain(s):
    """Floor falls in plan: slab outline, fall lines from each corner into a central drain."""
    p = Pen(s)
    c = (s / 2, s / 2)
    if s == 32:
        p.rect(3, 3, 29, 29, 2)                          # outline on whole pixels 3-4 / 27-28
        p.ring(c, 4.4, 1.7)
        p.dot(c, 1.5)
        for corner in ((6.5, 6.5), (25.5, 6.5), (6.5, 25.5), (25.5, 25.5)):
            p.arrow(corner, toward(corner, c, 5.3), 1.6, 4.0)
    else:
        p.rect(1, 1, 15, 15, 1)                          # outline on pixels 1 / 14
        p.dot(c, 2.2)
        for corner in ((3, 3), (13, 3), (3, 13), (13, 13)):
            p.line([corner, toward(corner, c, 3.3)], 1.1)
    return p


def by_point(s):
    """Floor falls in plan to two picked points, with the ridge line between them (32 px)."""
    p = Pen(s)
    if s == 32:
        p.rect(3, 3, 29, 29, 2)
        p.line([(16, 6.5), (16, 25.5)], 1.0)             # ridge between the two falls
        a, b = (10, 16), (22, 16)
        for c in (a, b):
            p.ring(c, 3.0, 1.4)
            p.dot(c, 1.3)
        for corner, c in (((6.5, 6.5), a), ((6.5, 25.5), a), ((25.5, 6.5), b), ((25.5, 25.5), b)):
            p.arrow(corner, toward(corner, c, 4.0), 1.6, 3.8)
    else:
        p.rect(1, 1, 15, 15, 1)
        a, b = (5, 8), (11, 8)
        for c in (a, b):
            p.dot(c, 1.7)
        for corner, c in (((3, 3), a), ((3, 13), a), ((13, 3), b), ((13, 13), b)):
            p.line([corner, toward(corner, c, 2.5)], 1.1)
    return p


def main(out_dir):
    os.makedirs(out_dir, exist_ok=True)
    for name, fn in (("FloorSlopeByDrain", by_drain), ("FloorSlopeByPoint", by_point)):
        for s in (16, 32):
            path = os.path.join(out_dir, f"{name}_{s}.png")
            fn(s).save(path)
            print("wrote", path)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
