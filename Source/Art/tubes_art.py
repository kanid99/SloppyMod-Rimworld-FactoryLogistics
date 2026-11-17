#!/usr/bin/env python3
"""Draws every sprite in the mod: the linked tube atlas, the buildings, the capsules, the
command icons and the store-page art.

    python3 Source/Art/tubes_art.py      # from the repo root

The atlas is the part that can be quietly wrong. A linked graphic is sixteen tiles in a 4x4
grid, one per combination of neighbours, and RimWorld picks a tile by bitmask:

    up (north) 1, right (east) 2, down (south) 4, left (west) 8

MaterialAtlasPool takes tile i from column i % 4 and row i // 4 counted from the BOTTOM of the
texture, and samples only the middle three quarters of each tile. So after drawing, this
samples each tile at the four edge midpoints of its sampled area and fails unless an arm is
present exactly where that tile's bitmask says there is a neighbour.

Depth is tone, not outline: the near-black appears only on outer silhouettes, and everything
inside is a step of grey, lit from above.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from tubes_draw import (  # noqa: E402
    AMBER, AMBER_DARK, BRASS, CASE, CASE_DARK, CASE_LIT, GLASS, GLASS_DARK, GLASS_LIT, HOLE,
    OUTLINE, PLATE, RED, TEAL, TEAL_DARK, WHITE, WOOD, WOOD_DARK, WOOD_LIT, disc, ellipse,
    finish, line, new_canvas, polygon, rect,
)

BUILDING = os.path.join("Textures", "Things", "Building", "FactoryLogistics")
ITEM = os.path.join("Textures", "Things", "Item", "FactoryLogistics")
COMMANDS = os.path.join("Textures", "UI", "Commands", "FactoryLogistics")
OVERLAYS = os.path.join("Textures", "UI", "Overlays", "FactoryLogistics")

CELL = 128                      # pixels per map cell in the building sprites

TILE = 128                      # one atlas tile; the middle 96px is one map cell
ATLAS = TILE * 4
UP, RIGHT, DOWN, LEFT = 1, 2, 4, 8
HALF_WIDTH = 13                 # half the tube's width, in atlas pixels
ALPHA_THRESHOLD = 40


# ------------------------------------------------------------------ the tube

def tile_origin(index):
    """Top-left of tile `index` in image coordinates (y down)."""
    return (index % 4) * TILE, (3 - index // 4) * TILE


def tube_run(draw, box, horizontal):
    """One straight run of glass tube: dark rim, pale body, a highlight along one side."""
    x0, y0, x1, y1 = box
    rect(draw, box, fill=OUTLINE)
    if horizontal:
        rect(draw, (x0, y0 + 3, x1, y1 - 3), fill=GLASS_DARK)
        rect(draw, (x0, y0 + 5, x1, y1 - 7), fill=GLASS)
        rect(draw, (x0, y0 + 7, x1, y0 + 11), fill=GLASS_LIT)
    else:
        rect(draw, (x0 + 3, y0, x1 - 3, y1), fill=GLASS_DARK)
        rect(draw, (x0 + 5, y0, x1 - 7, y1), fill=GLASS)
        rect(draw, (x0 + 7, y0, x0 + 11, y1), fill=GLASS_LIT)


def collar(draw, cx, cy):
    """The steel collar every tile has, so a bend or a tee reads as a fitting."""
    size = HALF_WIDTH + 5
    rect(draw, (cx - size, cy - size, cx + size, cy + size), fill=OUTLINE, radius=6)
    rect(draw, (cx - size + 3, cy - size + 3, cx + size - 3, cy + size - 3), fill=CASE, radius=5)
    rect(draw, (cx - size + 5, cy - size + 5, cx + size - 5, cy - 1), fill=CASE_LIT, radius=4)
    disc(draw, cx, cy + 3, 4, fill=BRASS)


def draw_tile(draw, index):
    x, y = tile_origin(index)
    cx, cy = x + TILE / 2, y + TILE / 2
    w = HALF_WIDTH
    # Arms run to the very edge of the tile, through the bleed, so neighbouring cells meet.
    if index & UP:
        tube_run(draw, (cx - w, y, cx + w, cy), horizontal=False)
    if index & DOWN:
        tube_run(draw, (cx - w, cy, cx + w, y + TILE), horizontal=False)
    if index & LEFT:
        tube_run(draw, (x, cy - w, cx, cy + w), horizontal=True)
    if index & RIGHT:
        tube_run(draw, (cx, cy - w, x + TILE, cy + w), horizontal=True)
    collar(draw, cx, cy)


def atlas():
    image, draw = new_canvas(ATLAS, ATLAS)
    for index in range(16):
        draw_tile(draw, index)
    return finish(image, ATLAS, ATLAS)


def verify_atlas(image):
    pad = TILE // 8
    for index in range(16):
        x, y = tile_origin(index)
        mid = TILE // 2
        probes = {
            UP: (x + mid, y + pad + 2),
            DOWN: (x + mid, y + TILE - pad - 3),
            LEFT: (x + pad + 2, y + mid),
            RIGHT: (x + TILE - pad - 3, y + mid),
        }
        for bit, point in probes.items():
            present = image.getpixel(point)[3] > ALPHA_THRESHOLD
            wanted = bool(index & bit)
            if present != wanted:
                raise SystemExit("atlas tile %d: arm for bit %d is %s, should be %s"
                                 % (index, bit, present, wanted))


def tube_menu_icon():
    size = TILE
    image, draw = new_canvas(size, size)
    tube_run(draw, (0, size / 2 - HALF_WIDTH, size, size / 2 + HALF_WIDTH), horizontal=True)
    collar(draw, size / 2, size / 2)
    return finish(image, size, size)


# ------------------------------------------------------------------ shared parts

def case(draw, box, radius=12):
    """A machine's body: silhouette, dark slab, mid slab stopped short of the bottom, lit top."""
    x0, y0, x1, y1 = box
    rect(draw, box, fill=OUTLINE, radius=radius)
    rect(draw, (x0 + 3, y0 + 3, x1 - 3, y1 - 3), fill=CASE_DARK, radius=radius - 2)
    rect(draw, (x0 + 3, y0 + 3, x1 - 3, y1 - 10), fill=CASE, radius=radius - 2)
    rect(draw, (x0 + 7, y0 + 6, x1 - 7, y0 + 13), fill=CASE_LIT, radius=3)


def port(draw, cx, cy, r, accent, accent_dark):
    """The mouth of a tube, seen from above: an accent ring round a dark hole."""
    disc(draw, cx, cy, r + 4, fill=OUTLINE)
    disc(draw, cx, cy, r + 1, fill=accent_dark)
    disc(draw, cx, cy - 1, r, fill=accent)
    disc(draw, cx, cy, r - 6, fill=OUTLINE)
    disc(draw, cx, cy + 1, r - 8, fill=HOLE)


def chevron(draw, cx, cy, size, up, fill):
    """A flat arrowhead. Up means into the tubes, down means out of them."""
    d = -1 if up else 1
    polygon(draw, [(cx - size, cy - d * size * 0.5), (cx, cy + d * size * 0.6),
                   (cx + size, cy - d * size * 0.5), (cx + size, cy - d * size * 0.95),
                   (cx, cy + d * size * 0.15), (cx - size, cy - d * size * 0.95)], fill=fill)


def capsule_shape(draw, cx, cy, half_len, half_w, band, band_dark, open_end=False):
    """A capsule lying on its side: glass barrel, steel end caps, one coloured band."""
    rect(draw, (cx - half_len, cy - half_w, cx + half_len, cy + half_w), fill=OUTLINE,
         radius=half_w)
    rect(draw, (cx - half_len + 3, cy - half_w + 3, cx + half_len - 3, cy + half_w - 3),
         fill=GLASS_DARK, radius=half_w - 3)
    rect(draw, (cx - half_len + 3, cy - half_w + 3, cx + half_len - 3, cy + half_w - 8),
         fill=GLASS, radius=half_w - 3)
    rect(draw, (cx - half_len + 12, cy - half_w + 6, cx + half_len - 12, cy - half_w + 10),
         fill=GLASS_LIT, radius=2)
    cap = half_len * 0.32
    for sign in (-1, 1):
        x_outer = cx + sign * (half_len - 3)
        x_inner = cx + sign * (half_len - 3 - cap)
        x0, x1 = min(x_outer, x_inner), max(x_outer, x_inner)
        rect(draw, (x0, cy - half_w + 3, x1, cy + half_w - 3), fill=CASE_DARK,
             radius=half_w - 3)
        rect(draw, (x0, cy - half_w + 3, x1, cy + half_w - 9), fill=CASE_LIT,
             radius=half_w - 3)
        # Square off the cap's inner edge so it reads as a collar round the glass.
        inner0, inner1 = (x1 - 6, x1) if sign < 0 else (x0, x0 + 6)
        rect(draw, (inner0, cy - half_w + 3, inner1, cy + half_w - 3), fill=CASE)
    if band is not None:
        rect(draw, (cx - 7, cy - half_w + 3, cx + 7, cy + half_w - 3), fill=band_dark)
        rect(draw, (cx - 7, cy - half_w + 3, cx + 7, cy + half_w - 9), fill=band)
    if open_end:
        ellipse(draw, (cx + half_len - 15, cy - half_w + 6, cx + half_len - 5, cy + half_w - 6),
                fill=HOLE)


# ------------------------------------------------------------------ buildings

def sender():
    image, draw = new_canvas(CELL, CELL)
    case(draw, (10, 10, CELL - 10, CELL - 8))
    port(draw, CELL / 2, 52, 27, TEAL, TEAL_DARK)
    chevron(draw, CELL / 2, 101, 13, True, TEAL)
    return finish(image, CELL, CELL)


def receiver():
    image, draw = new_canvas(CELL, CELL)
    case(draw, (10, 10, CELL - 10, CELL - 8))
    # A shallow tray across the lower half, where arrived capsules sit.
    rect(draw, (20, 62, CELL - 20, CELL - 20), fill=PLATE, radius=8)
    rect(draw, (23, 65, CELL - 23, CELL - 26), fill=HOLE, radius=6)
    port(draw, CELL / 2, 40, 17, AMBER, AMBER_DARK)
    chevron(draw, CELL / 2, 92, 11, False, AMBER)
    return finish(image, CELL, CELL)


def receiver_rack():
    width, height = CELL, CELL * 2
    image, draw = new_canvas(width, height)
    case(draw, (10, 10, width - 10, height - 8))
    port(draw, width / 2, 44, 19, AMBER, AMBER_DARK)
    chevron(draw, width / 2, 82, 11, False, AMBER)
    # Four cradles down the rack.
    top = 104
    for i in range(4):
        y = top + i * 34
        rect(draw, (20, y, width - 20, y + 26), fill=PLATE, radius=8)
        rect(draw, (23, y + 3, width - 23, y + 21), fill=HOLE, radius=6)
    return finish(image, width, height)


def compressor():
    size = CELL * 2
    image, draw = new_canvas(size, size)
    case(draw, (12, 14, size - 12, size - 10), radius=16)
    # The motor: a drum lying across the top.
    rect(draw, (30, 30, size - 30, 96), fill=OUTLINE, radius=30)
    rect(draw, (33, 33, size - 33, 93), fill=CASE_DARK, radius=28)
    rect(draw, (33, 33, size - 33, 78), fill=CASE_LIT, radius=28)
    rect(draw, (48, 40, size - 48, 47), fill=WHITE, radius=3)
    for x in range(64, size - 60, 22):
        rect(draw, (x, 54, x + 8, 88), fill=CASE)
    # The fan.
    cx, cy, r = 92, 168, 50
    disc(draw, cx, cy, r + 4, fill=OUTLINE)
    disc(draw, cx, cy, r, fill=PLATE)
    disc(draw, cx, cy, r - 6, fill=HOLE)
    for dx, dy in ((0, -1), (1, 0), (0, 1), (-1, 0)):
        polygon(draw, [(cx + dx * 6 - dy * 4, cy + dy * 6 + dx * 4),
                       (cx + dx * (r - 12) - dy * 17, cy + dy * (r - 12) + dx * 17),
                       (cx + dx * (r - 10) + dy * 6, cy + dy * (r - 10) - dx * 6)], fill=CASE)
    disc(draw, cx, cy, 10, fill=CASE_LIT)
    # The outlet and its pressure gauge.
    disc(draw, 196, 148, 24, fill=OUTLINE)
    disc(draw, 196, 148, 20, fill=WHITE)
    line(draw, [(196, 148), (208, 136)], fill=RED, width=3)
    disc(draw, 196, 148, 3, fill=OUTLINE)
    rect(draw, (176, 188, 216, 226), fill=OUTLINE, radius=8)
    rect(draw, (179, 191, 213, 223), fill=GLASS_DARK, radius=6)
    rect(draw, (179, 191, 213, 212), fill=GLASS, radius=6)
    rect(draw, (184, 195, 208, 199), fill=GLASS_LIT, radius=2)
    return finish(image, size, size)


def air_tank():
    image, draw = new_canvas(CELL, CELL)
    cx, cy, r = CELL / 2, CELL / 2 - 4, 50
    disc(draw, cx, cy + 3, r + 3, fill=OUTLINE)
    disc(draw, cx, cy + 3, r, fill=CASE_DARK)
    disc(draw, cx, cy, r - 1, fill=CASE)
    disc(draw, cx, cy - 4, r - 12, fill=CASE_LIT)
    disc(draw, cx, cy - 1, r - 22, fill=CASE)
    # Valve.
    disc(draw, cx, cy - 2, 10, fill=OUTLINE)
    disc(draw, cx, cy - 2, 7, fill=BRASS)
    # A plain plate along the bottom edge, where the framework draws the fill bar.
    rect(draw, (CELL / 2 - 46, CELL - 34, CELL / 2 + 46, CELL - 10), fill=OUTLINE, radius=6)
    rect(draw, (CELL / 2 - 43, CELL - 31, CELL / 2 + 43, CELL - 13), fill=PLATE, radius=4)
    return finish(image, CELL, CELL)


def packing_bench():
    width, height = CELL * 2, CELL
    image, draw = new_canvas(width, height)
    rect(draw, (8, 12, width - 8, height - 10), fill=OUTLINE, radius=10)
    rect(draw, (11, 15, width - 11, height - 13), fill=WOOD_DARK, radius=8)
    rect(draw, (11, 15, width - 11, height - 22), fill=WOOD, radius=8)
    rect(draw, (16, 19, width - 16, 26), fill=WOOD_LIT, radius=3)
    # A cradle holding an open shell on the left, a tray of shells on the right.
    rect(draw, (22, 40, 122, 96), fill=PLATE, radius=10)
    rect(draw, (25, 43, 119, 90), fill=HOLE, radius=8)
    capsule_shape(draw, 72, 66, 40, 15, None, None, open_end=True)
    rect(draw, (140, 36, width - 22, 100), fill=CASE_DARK, radius=8)
    rect(draw, (143, 39, width - 25, 92), fill=CASE, radius=6)
    for i in range(3):
        y = 50 + i * 16
        rect(draw, (150, y - 6, width - 32, y + 6), fill=OUTLINE, radius=6)
        rect(draw, (152, y - 4, width - 34, y + 2), fill=GLASS, radius=4)
    return finish(image, width, height)


# ------------------------------------------------------------------ items

def capsule_item(band, band_dark, open_end=False):
    size = 96
    image, draw = new_canvas(size, size)
    capsule_shape(draw, size / 2, size / 2 + 2, 40, 17, band, band_dark, open_end)
    return finish(image, size, size)


# ------------------------------------------------------------------ interface

def icon_set_destination():
    size = 128
    image, draw = new_canvas(size, size)
    port(draw, 34, 64, 20, TEAL, TEAL_DARK)
    port(draw, 94, 64, 20, AMBER, AMBER_DARK)
    line(draw, [(50, 30), (72, 30)], fill=WHITE, width=7)
    polygon(draw, [(70, 18), (88, 30), (70, 42)], fill=WHITE)
    return finish(image, size, size)


def icon_assign_sender():
    size = 128
    image, draw = new_canvas(size, size)
    capsule_shape(draw, 64, 86, 44, 18, TEAL, TEAL_DARK)
    line(draw, [(64, 54), (64, 30)], fill=WHITE, width=8)
    polygon(draw, [(48, 34), (64, 12), (80, 34)], fill=WHITE)
    return finish(image, size, size)


def icon_mark_for_unpacking():
    size = 128
    image, draw = new_canvas(size, size)
    capsule_shape(draw, 64, 44, 44, 18, AMBER, AMBER_DARK, open_end=True)
    line(draw, [(64, 74), (64, 98)], fill=WHITE, width=8)
    polygon(draw, [(48, 94), (64, 116), (80, 94)], fill=WHITE)
    return finish(image, size, size)


def overlay_no_air():
    size = 64
    image, draw = new_canvas(size, size)
    disc(draw, 32, 32, 26, fill=OUTLINE)
    disc(draw, 32, 32, 22, fill=WHITE)
    disc(draw, 32, 32, 3, fill=OUTLINE)
    line(draw, [(32, 32), (20, 40)], fill=OUTLINE, width=3)
    line(draw, [(12, 12), (52, 52)], fill=RED, width=7)
    return finish(image, size, size)


# ------------------------------------------------------------------ store page

def about_art():
    """Preview and mod icon, composed from the sprites above so they cannot drift apart."""
    from PIL import Image

    backdrop = (38, 42, 48)
    preview = Image.new("RGBA", (640, 360), backdrop + (255,))
    run = Image.new("RGBA", (640, 26), (0, 0, 0, 0))
    strip_image, strip = new_canvas(640, 26)
    tube_run(strip, (0, 0, 640, 26), horizontal=True)
    run = finish(strip_image, 640, 26)
    preview.alpha_composite(run, (0, 167))
    left = sender().resize((176, 176), Image.LANCZOS)
    right = receiver().resize((176, 176), Image.LANCZOS)
    preview.alpha_composite(left, (70, 92))
    preview.alpha_composite(right, (394, 92))
    capsule = capsule_item(TEAL, TEAL_DARK).resize((120, 120), Image.LANCZOS)
    preview.alpha_composite(capsule, (260, 120))

    icon = Image.new("RGBA", (256, 256), backdrop + (255,))
    icon.alpha_composite(capsule_item(TEAL, TEAL_DARK).resize((224, 224), Image.LANCZOS), (16, 16))
    return preview.convert("RGB"), icon.convert("RGB")


def save(image, folder, name):
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, name)
    image.save(path)
    print("wrote", path, image.size)


def main():
    tubes = atlas()
    verify_atlas(tubes)
    save(tubes, BUILDING, "Tube_Atlas.png")
    save(tube_menu_icon(), BUILDING, "Tube_MenuIcon.png")
    save(sender(), BUILDING, "Sender.png")
    save(receiver(), BUILDING, "Receiver.png")
    save(receiver_rack(), BUILDING, "ReceiverRack.png")
    save(compressor(), BUILDING, "Compressor.png")
    save(air_tank(), BUILDING, "AirTank.png")
    save(packing_bench(), BUILDING, "PackingBench.png")

    save(capsule_item(None, None, open_end=True), ITEM, "CapsuleShell.png")
    save(capsule_item(TEAL, TEAL_DARK), ITEM, "Capsule.png")
    save(capsule_item(AMBER, AMBER_DARK), ITEM, "CapsuleDelivered.png")

    save(icon_set_destination(), COMMANDS, "SetDestination.png")
    save(icon_assign_sender(), COMMANDS, "AssignSender.png")
    save(icon_mark_for_unpacking(), COMMANDS, "MarkForUnpacking.png")
    save(overlay_no_air(), OVERLAYS, "NoAir.png")

    preview, icon = about_art()
    save(preview, "About", "Preview.png")
    save(icon, "About", "ModIcon.png")


if __name__ == "__main__":
    main()
