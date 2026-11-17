"""Shared drawing primitives for the pneumatic tube sprites.

Same approach as the other SloppyMods art: everything is drawn from primitives at SS times
the final size and downsampled with LANCZOS, because PIL's ellipse and polygon are
hard-aliased and a circle drawn directly has a visible staircase at RimWorld's default zoom.
"""

from PIL import Image, ImageDraw

SS = 4

# Palette.
#
# The machinery is the same cool grey as the Riimba station's, so the two mods sit together.
# The tubes themselves are a pale blue-grey glass, which is the one thing that says "air";
# the two accents separate the two ends of a route, and nothing else is saturated:
# teal means "going in" (senders, packed capsules), amber means "come out" (receivers,
# delivered capsules).
OUTLINE = (18, 20, 24)
CASE_DARK = (62, 66, 74)
CASE = (92, 97, 106)
CASE_LIT = (130, 136, 146)
PLATE = (46, 49, 55)
HOLE = (24, 26, 31)
GLASS_DARK = (96, 122, 138)
GLASS = (142, 176, 192)
GLASS_LIT = (202, 226, 236)
TEAL = (60, 168, 178)
TEAL_DARK = (30, 96, 106)
AMBER = (222, 160, 62)
AMBER_DARK = (142, 96, 30)
BRASS = (188, 170, 96)
WOOD = (132, 104, 74)
WOOD_LIT = (160, 130, 96)
WOOD_DARK = (96, 74, 52)
RED = (206, 70, 60)
WHITE = (236, 238, 240)


def new_canvas(width, height):
    image = Image.new("RGBA", (int(width * SS), int(height * SS)), (0, 0, 0, 0))
    return image, ImageDraw.Draw(image)


def finish(image, width, height):
    return image.resize((int(width), int(height)), Image.LANCZOS)


def s(value):
    return int(round(value * SS))


def ellipse(draw, box, fill=None, outline=None, width=1):
    x0, y0, x1, y1 = box
    draw.ellipse((s(x0), s(y0), s(x1), s(y1)), fill=fill, outline=outline,
                 width=max(1, s(width)))


def disc(draw, cx, cy, r, fill=None, outline=None, width=1):
    ellipse(draw, (cx - r, cy - r, cx + r, cy + r), fill=fill, outline=outline, width=width)


def rect(draw, box, fill=None, outline=None, width=1, radius=None):
    x0, y0, x1, y1 = box
    scaled = (s(x0), s(y0), s(x1), s(y1))
    if radius:
        draw.rounded_rectangle(scaled, radius=s(radius), fill=fill, outline=outline,
                               width=max(1, s(width)))
    else:
        draw.rectangle(scaled, fill=fill, outline=outline, width=max(1, s(width)))


def polygon(draw, points, fill=None, outline=None):
    draw.polygon([(s(x), s(y)) for x, y in points], fill=fill, outline=outline)


def line(draw, points, fill, width=1):
    draw.line([(s(x), s(y)) for x, y in points], fill=fill, width=max(1, s(width)))
