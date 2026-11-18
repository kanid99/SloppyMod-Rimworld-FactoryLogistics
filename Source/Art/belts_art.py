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


def lift():
    """The conveyor lift facing north: a square shaft with a platform, up and down marks on
    the sides, and an output lip on the front edge (top)."""
    image, draw = new_canvas(CELL, CELL)
    c = CELL / 2
    rect(draw, (12, 12, CELL - 12, CELL - 12), fill=OUTLINE, radius=10)
    rect(draw, (16, 16, CELL - 16, CELL - 16), fill=CASE_DARK, radius=8)
    rect(draw, (16, 16, CELL - 16, CELL - 24), fill=CASE, radius=8)
    # The shaft opening and its platform.
    rect(draw, (32, 32, CELL - 32, CELL - 32), fill=OUTLINE, radius=4)
    rect(draw, (35, 35, CELL - 35, CELL - 35), fill=HOLE, radius=3)
    rect(draw, (40, 44, CELL - 40, CELL - 40), fill=PLATE, radius=3)
    for y in range(50, CELL - 44, 9):
        line(draw, [(44, y), (CELL - 44, y)], CASE_DARK, 2)
    # Up on the left, down on the right.
    polygon(draw, [(22, 56), (28, 46), (34, 56)], fill=TEAL)
    polygon(draw, [(22, 66), (28, 56), (34, 66)], fill=TEAL_DARK)
    polygon(draw, [(CELL - 34, 62), (CELL - 28, 72), (CELL - 22, 62)], fill=AMBER)
    polygon(draw, [(CELL - 34, 72), (CELL - 28, 82), (CELL - 22, 72)], fill=AMBER_DARK)
    return finish(image, CELL, CELL)


def lift_lip():
    """The output lip on the front edge (top), on its own layer so it alone turns with the
    lift: the up and down marks stay upright whichever way the lift faces."""
    image, draw = new_canvas(CELL, CELL)
    rect(draw, (40, 6, CELL - 40, 24), fill=OUTLINE, radius=4)
    rect(draw, (43, 9, CELL - 43, 21), fill=CASE_LIT, radius=3)
    chevron_up(draw, CELL / 2, 15, 7, OUTLINE)
    return finish(image, CELL, CELL)


def save_lift(name):
    os.makedirs(BUILDING, exist_ok=True)
    body, lip = lift(), lift_lip()
    for suffix, angle in (("north", 0), ("east", -90), ("south", 180)):
        image = body.copy()
        image.alpha_composite(lip.rotate(angle, resample=Image.BICUBIC))
        path = os.path.join(BUILDING, "%s_%s.png" % (name, suffix))
        image.save(path)
        print("wrote", path)
        if suffix == "north":
            save_icon(image, name)


def icon_link():
    image, draw = new_canvas(64, 64)
    for x0, color, dark in ((8, TEAL, TEAL_DARK), (30, AMBER, AMBER_DARK)):
        rect(draw, (x0, 18, x0 + 26, 46), fill=OUTLINE, radius=12)
        rect(draw, (x0 + 4, 22, x0 + 22, 42), fill=color, radius=9)
        rect(draw, (x0 + 9, 27, x0 + 17, 37), fill=OUTLINE, radius=4)
    return finish(image, 64, 64)


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
    save_lift("ConveyorLift")
    save_icon(icon_link(), "LiftLink")


if __name__ == "__main__":
    main()
