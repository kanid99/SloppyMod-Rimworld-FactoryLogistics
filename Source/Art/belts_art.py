"""Sprites for the belts module (Mods/Belts): the conveyor arm in its two modes.

Run from the repository root:  python3 Source/Art/belts_art.py   (Pillow only)

Drawn facing north and rotated for the other facings, since everything is seen from straight
above. Same palette and primitives as the tubes (tubes_draw.py): teal is the Loader (goods going
onto the belt), amber the Feeder (goods coming off it into a machine).
"""

import math
import os

from PIL import Image, ImageDraw

from tubes_draw import (AMBER, AMBER_DARK, CASE, CASE_DARK, CASE_LIT, HOLE, OUTLINE, PLATE,
                        TEAL, TEAL_DARK, disc, finish, line, new_canvas, polygon, rect)

CELL = 128
WHITE_ARROW = (236, 238, 240)
# The Mover's accent: neither "going in" teal nor "coming out" amber, since it does not care.
LIME = (150, 196, 78)
LIME_DARK = (78, 118, 40)
MODULE = os.path.join("Mods", "Belts")
BUILDING = os.path.join(MODULE, "Textures", "Things", "Building", "FactoryLogistics")
COMMANDS = os.path.join(MODULE, "Textures", "UI", "Commands", "FactoryLogistics")


def chevron_up(draw, cx, cy, size, fill):
    polygon(draw, [(cx - size, cy + size * 0.5), (cx, cy - size * 0.6), (cx + size, cy + size * 0.5),
                   (cx + size, cy + size * 0.95), (cx, cy - size * 0.15), (cx - size, cy + size * 0.95)],
            fill=fill)


def chevron_down(draw, cx, cy, size, fill):
    polygon(draw, [(cx - size, cy - size * 0.5), (cx, cy + size * 0.6), (cx + size, cy - size * 0.5),
                   (cx + size, cy - size * 0.95), (cx, cy + size * 0.15), (cx - size, cy - size * 0.95)],
            fill=fill)


def arm_base(accent, accent_dark):
    """The arm's floor-mounted base, facing north: plate, bolts, the output tray on the back
    edge (bottom) with a chevron pointing out of it, and the turret in the middle. The arm itself
    is a separate sprite drawn over it each frame, so it can swing."""
    image, draw = new_canvas(CELL, CELL)
    c = CELL / 2

    rect(draw, (14, 14, CELL - 14, CELL - 14), fill=OUTLINE, radius=14)
    rect(draw, (17, 17, CELL - 17, CELL - 17), fill=PLATE, radius=12)
    for x, y in ((26, 26), (CELL - 26, 26), (26, CELL - 26), (CELL - 26, CELL - 26)):
        disc(draw, x, y, 4, fill=OUTLINE)
        disc(draw, x, y - 0.5, 2.5, fill=CASE_LIT)

    # The output tray is not part of the base: it is drawn on whichever side the arm puts down
    # on (arm_tray), since that can be any side but the gripper's.
    # Intake mark on the front edge, where the gripper reaches in.
    rect(draw, (44, 8, CELL - 44, 16), fill=accent_dark, radius=3)

    # Turret ring at the pivot.
    disc(draw, c, c, 24, fill=OUTLINE)
    disc(draw, c, c, 21, fill=CASE_DARK)
    disc(draw, c, c - 2, 19, fill=CASE)
    return finish(image, CELL, CELL)


def arm_tray(accent):
    """The output tray on the south edge, with a chevron pointing out of it. Drawn turned to
    whichever side the arm puts down on."""
    image, draw = new_canvas(CELL, CELL)
    c = CELL / 2
    rect(draw, (36, CELL - 30, CELL - 36, CELL - 8), fill=OUTLINE, radius=5)
    rect(draw, (39, CELL - 27, CELL - 39, CELL - 11), fill=HOLE, radius=4)
    chevron_down(draw, c, CELL - 19, 8, accent)
    return finish(image, CELL, CELL)


def arm_overlay(accent, accent_dark, closed):
    """The swinging arm, pivot at the middle of the sprite, pointing north: a thick upper arm,
    an elbow, a forearm and the gripper at the cell's front edge - open, or closed on an item."""
    image, draw = new_canvas(CELL, CELL)
    c = CELL / 2

    line(draw, [(c, c), (c, c - 26)], OUTLINE, 18)
    line(draw, [(c, c), (c, c - 26)], accent_dark, 12)
    line(draw, [(c - 2, c - 2), (c - 2, c - 24)], accent, 6)
    disc(draw, c, c - 26, 10, fill=OUTLINE)
    disc(draw, c, c - 26, 7, fill=CASE_LIT)
    line(draw, [(c, c - 26), (c, 20)], OUTLINE, 12)
    line(draw, [(c, c - 26), (c, 20)], CASE, 7)

    for sign in (-1, 1):
        if closed:
            pts_out = [(c + sign * 4, 24), (c + sign * 13, 18), (c + sign * 10, 4),
                       (c + sign * 5, 4), (c + sign * 7, 15), (c + sign * 2, 18)]
            pts_in = [(c + sign * 5, 21), (c + sign * 11, 17), (c + sign * 9, 7),
                      (c + sign * 7, 7), (c + sign * 8, 15), (c + sign * 3, 18)]
        else:
            pts_out = [(c + sign * 4, 24), (c + sign * 17, 16), (c + sign * 19, 4),
                       (c + sign * 13, 4), (c + sign * 11, 13), (c + sign * 3, 17)]
            pts_in = [(c + sign * 5, 21), (c + sign * 15, 15), (c + sign * 16, 7),
                      (c + sign * 14, 7), (c + sign * 12, 14), (c + sign * 4, 18)]
        polygon(draw, pts_out, fill=OUTLINE)
        polygon(draw, pts_in, fill=accent)
    disc(draw, c, 20, 6, fill=OUTLINE)
    disc(draw, c, 20, 3.5, fill=accent_dark)

    # Pivot cap over the arm root.
    disc(draw, c, c, 9, fill=OUTLINE)
    disc(draw, c, c - 1, 6, fill=accent)
    return finish(image, CELL, CELL)


def arm_icon(accent, accent_dark):
    """Base and open arm together, for the build menu and gizmo icon."""
    image = arm_base(accent, accent_dark)
    image.alpha_composite(arm_tray(accent))
    image.alpha_composite(arm_overlay(accent, accent_dark, closed=False))
    return image


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


def junction():
    """The belt junction, seen from above: a round diverter turntable on a square plate, with
    an amber chevron pointing out of each side."""
    image, draw = new_canvas(CELL, CELL)
    c = CELL / 2
    rect(draw, (12, 12, CELL - 12, CELL - 12), fill=OUTLINE, radius=12)
    rect(draw, (16, 16, CELL - 16, CELL - 16), fill=PLATE, radius=10)
    for angle in (0, 90, 180, 270):
        layer, ldraw = new_canvas(CELL, CELL)
        chevron_up(ldraw, c, 26, 9, AMBER)
        image.alpha_composite(layer.rotate(angle, resample=Image.BICUBIC))
        draw = ImageDraw.Draw(image)
    disc(draw, c, c, 30, fill=OUTLINE)
    disc(draw, c, c, 26, fill=CASE_DARK)
    disc(draw, c, c - 2, 23, fill=CASE)
    # The diverter blade.
    line(draw, [(c - 18, c + 12), (c + 18, c - 12)], OUTLINE, 10)
    line(draw, [(c - 16, c + 11), (c + 16, c - 11)], TEAL, 5)
    disc(draw, c, c, 7, fill=OUTLINE)
    disc(draw, c, c - 0.5, 4.5, fill=CASE_LIT)
    return finish(image, CELL, CELL)


def icon_priority():
    image, draw = new_canvas(64, 64)
    for i, (h, color) in enumerate(((16, CASE_LIT), (28, AMBER), (40, AMBER))):
        x = 10 + i * 16
        rect(draw, (x, 54 - h, x + 12, 54), fill=OUTLINE, radius=2)
        rect(draw, (x + 2, 56 - h, x + 10, 52), fill=color, radius=2)
    return finish(image, 64, 64)


def icon_return_leftovers():
    """A shelf with an arrow curving back up into it: leftovers go home."""
    image, draw = new_canvas(64, 64)
    rect(draw, (8, 6, 56, 28), fill=OUTLINE, radius=3)
    rect(draw, (11, 9, 53, 25), fill=CASE, radius=2)
    line(draw, [(11, 17), (53, 17)], CASE_DARK, 2)
    line(draw, [(44, 56), (20, 56), (14, 50), (14, 40)], OUTLINE, 10)
    line(draw, [(44, 56), (20, 56), (14, 50), (14, 40)], TEAL, 5)
    polygon(draw, [(14, 30), (4, 44), (24, 44)], fill=OUTLINE)
    polygon(draw, [(14, 34), (8, 42), (20, 42)], fill=TEAL)
    return finish(image, 64, 64)


def icon_belt_drop():
    """A belt piece with a crate being kicked off its side."""
    image, draw = new_canvas(64, 64)
    rect(draw, (4, 26, 40, 54), fill=OUTLINE, radius=3)
    rect(draw, (7, 29, 37, 51), fill=CASE, radius=2)
    for x in (12, 22, 32):
        polygon(draw, [(x - 3, 33), (x + 3, 40), (x - 3, 47)], fill=CASE_DARK)
    rect(draw, (36, 8, 58, 30), fill=OUTLINE, radius=3)
    rect(draw, (39, 11, 55, 27), fill=AMBER, radius=2)
    line(draw, [(39, 19), (55, 19)], AMBER_DARK, 2)
    line(draw, [(20, 22), (32, 12)], OUTLINE, 7)
    line(draw, [(20, 22), (32, 12)], WHITE_ARROW, 3)
    return finish(image, 64, 64)


def icon_move_filter():
    """A funnel in the Mover's lime."""
    image, draw = new_canvas(64, 64)
    polygon(draw, [(6, 8), (58, 8), (38, 34), (38, 56), (26, 50), (26, 34)], fill=OUTLINE)
    polygon(draw, [(12, 12), (52, 12), (34, 33), (34, 50), (30, 48), (30, 33)], fill=LIME)
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
    for name, accent, dark in (("ArmLoader", TEAL, TEAL_DARK), ("ArmFeeder", AMBER, AMBER_DARK), ("ArmMover", LIME, LIME_DARK)):
        save_multi(arm_base(accent, dark), name)
        tray_path = os.path.join(BUILDING, name + "_Tray.png")
        arm_tray(accent).save(tray_path)
        print("wrote", tray_path)
        for suffix, closed in (("_Arm", False), ("_ArmClosed", True)):
            path = os.path.join(BUILDING, name + suffix + ".png")
            arm_overlay(accent, dark, closed).save(path)
            print("wrote", path)
        save_icon(arm_icon(accent, dark), name)
    save_lift("ConveyorLift")
    save_icon(icon_link(), "LiftLink")
    junction_image = junction()
    os.makedirs(BUILDING, exist_ok=True)
    junction_image.save(os.path.join(BUILDING, "BeltJunction.png"))
    print("wrote", os.path.join(BUILDING, "BeltJunction.png"))
    save_icon(junction_image, "BeltJunction")
    save_icon(icon_priority(), "FeederPriority")
    save_icon(icon_return_leftovers(), "ReturnLeftovers")
    save_icon(icon_belt_drop(), "BeltDrop")
    save_icon(icon_move_filter(), "MoveFilter")


if __name__ == "__main__":
    main()
