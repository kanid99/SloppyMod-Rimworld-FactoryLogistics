"""Sprites for the belts module (Mods/Belts): the conveyor arm in its two modes.

Run from the repository root:  python3 Source/Art/belts_art.py   (Pillow only)

Drawn facing north and rotated for the other facings, since everything is seen from straight
above. Same palette and primitives as the tubes (tubes_draw.py): teal is the Loader (goods going
onto the belt), amber the Feeder (goods coming off it into a machine).
"""

import math
import os

from PIL import Image

from tubes_draw import (AMBER, AMBER_DARK, CASE, CASE_DARK, CASE_LIT, HOLE, OUTLINE, PLATE,
                        TEAL, TEAL_DARK, disc, finish, line, new_canvas, polygon, rect)

CELL = 128
MODULE = os.path.join("Mods", "Belts")
BUILDING = os.path.join(MODULE, "Textures", "Things", "Building", "FactoryLogistics")
COMMANDS = os.path.join(MODULE, "Textures", "UI", "Commands", "FactoryLogistics")


def chevron_up(draw, cx, cy, size, fill):
    polygon(draw, [(cx - size, cy + size * 0.5), (cx, cy - size * 0.6), (cx + size, cy + size * 0.5),
                   (cx + size, cy + size * 0.95), (cx, cy - size * 0.15), (cx - size, cy + size * 0.95)],
            fill=fill)


def arm(accent, accent_dark):
    """The arm facing north: a tray at the back (bottom), a turret, and a jointed arm reaching to
    the front edge (top) with an open gripper."""
    image, draw = new_canvas(CELL, CELL)
    c = CELL / 2

    # Base plate, low and square, so it reads as floor-mounted.
    rect(draw, (14, 14, CELL - 14, CELL - 14), fill=OUTLINE, radius=14)
    rect(draw, (17, 17, CELL - 17, CELL - 17), fill=PLATE, radius=12)
    # Hazard-ish corner bolts.
    for x, y in ((26, 26), (CELL - 26, 26), (26, CELL - 26), (CELL - 26, CELL - 26)):
        disc(draw, x, y, 4, fill=OUTLINE)
        disc(draw, x, y - 0.5, 2.5, fill=CASE_LIT)

    # Intake tray on the back edge, where it takes from.
    rect(draw, (36, CELL - 30, CELL - 36, CELL - 8), fill=OUTLINE, radius=5)
    rect(draw, (39, CELL - 27, CELL - 39, CELL - 11), fill=HOLE, radius=4)
    chevron_up(draw, c, CELL - 19, 8, accent)

    # Turret.
    disc(draw, c, c + 14, 22, fill=OUTLINE)
    disc(draw, c, c + 14, 19, fill=CASE_DARK)
    disc(draw, c, c + 12, 17, fill=CASE)
    disc(draw, c, c + 9, 9, fill=CASE_LIT)

    # Arm: a thick upper segment and a thinner forearm, both pointing forwards.
    line(draw, [(c, c + 14), (c, c - 16)], OUTLINE, 18)
    line(draw, [(c, c + 14), (c, c - 16)], accent_dark, 12)
    line(draw, [(c - 2, c + 12), (c - 2, c - 14)], accent, 6)
    disc(draw, c, c - 16, 10, fill=OUTLINE)
    disc(draw, c, c - 16, 7, fill=CASE_LIT)
    line(draw, [(c, c - 16), (c, 22)], OUTLINE, 12)
    line(draw, [(c, c - 16), (c, 22)], CASE, 7)

    # Gripper at the front edge, open.
    for sign in (-1, 1):
        polygon(draw, [(c + sign * 4, 26), (c + sign * 17, 18), (c + sign * 19, 6),
                       (c + sign * 13, 6), (c + sign * 11, 15), (c + sign * 3, 19)], fill=OUTLINE)
        polygon(draw, [(c + sign * 5, 23), (c + sign * 15, 17), (c + sign * 16, 9),
                       (c + sign * 14, 9), (c + sign * 12, 16), (c + sign * 4, 20)], fill=accent)
    disc(draw, c, 22, 6, fill=OUTLINE)
    disc(draw, c, 22, 3.5, fill=accent_dark)

    # Turret pivot cap last, over the arm root.
    disc(draw, c, c + 14, 8, fill=OUTLINE)
    disc(draw, c, c + 13, 5.5, fill=accent)
    return finish(image, CELL, CELL)


def save_multi(image, name):
    os.makedirs(BUILDING, exist_ok=True)
    # North as drawn; east is the north sprite turned a quarter clockwise; south half a turn.
    # West is left to RimWorld, which mirrors east.
    for suffix, angle in (("north", 0), ("east", -90), ("south", 180)):
        path = os.path.join(BUILDING, "%s_%s.png" % (name, suffix))
        image.rotate(angle, resample=Image.BICUBIC).save(path)
        print("wrote", path)


def save_icon(image, name):
    os.makedirs(COMMANDS, exist_ok=True)
    path = os.path.join(COMMANDS, name + ".png")
    image.save(path)
    print("wrote", path)


def main():
    loader = arm(TEAL, TEAL_DARK)
    feeder = arm(AMBER, AMBER_DARK)
    save_multi(loader, "ArmLoader")
    save_multi(feeder, "ArmFeeder")
    save_icon(loader, "ArmLoader")
    save_icon(feeder, "ArmFeeder")


if __name__ == "__main__":
    main()
