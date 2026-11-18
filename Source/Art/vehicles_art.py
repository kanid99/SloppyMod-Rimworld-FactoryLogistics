"""Sprites for the vehicle dock module (Mods/Vehicles).

Run from the repository root:  python3 Source/Art/vehicles_art.py   (Pillow only)
"""

import os

from tubes_draw import (AMBER, AMBER_DARK, CASE, CASE_DARK, CASE_LIT, GLASS, HOLE, OUTLINE, PLATE,
                        TEAL, TEAL_DARK, WHITE, disc, finish, line, new_canvas, polygon, rect)

MODULE = os.path.join("Mods", "Vehicles")
BUILDING = os.path.join(MODULE, "Textures", "Things", "Building", "FactoryLogistics")
COMMANDS = os.path.join(MODULE, "Textures", "UI", "Commands", "FactoryLogistics")
HAZARD = (214, 176, 52)


def dock():
    size = 384
    image, draw = new_canvas(size, size)
    # Slab with a hazard-striped border.
    rect(draw, (6, 6, size - 6, size - 6), fill=OUTLINE, radius=18)
    # Stripes are drawn on their own layer and cut to the border's rounded shape.
    stripes, sdraw = new_canvas(size, size)
    rect(sdraw, (0, 0, size, size), fill=HAZARD)
    step = 26
    for k in range(-size, size * 2, step * 2):
        polygon(sdraw, [(k, 0), (k + step, 0), (k + step - size, size), (k - size, size)], fill=OUTLINE)
    mask, mdraw = new_canvas(size, size)
    rect(mdraw, (10, 10, size - 10, size - 10), fill=(255, 255, 255, 255), radius=15)
    image.paste(stripes, (0, 0), mask)
    rect(draw, (32, 32, size - 32, size - 32), fill=OUTLINE, radius=10)
    rect(draw, (36, 36, size - 36, size - 36), fill=PLATE, radius=8)
    # Tread plate dimples.
    for y in range(56, size - 40, 24):
        for x in range(56 + (12 if (y // 24) % 2 else 0), size - 40, 24):
            line(draw, [(x - 4, y - 4), (x + 4, y + 4)], CASE_DARK, 3)
    # Guide lines for the wheels.
    for x in (92, size - 92):
        rect(draw, (x - 6, 48, x + 6, size - 48), fill=CASE_DARK, radius=3)
        rect(draw, (x - 3, 50, x + 3, size - 50), fill=CASE, radius=2)
    # Central hatch: the tube mouth that capsules come and go through.
    c = size / 2
    disc(draw, c, c, 54, fill=OUTLINE)
    disc(draw, c, c, 48, fill=CASE_DARK)
    disc(draw, c, c - 2, 44, fill=CASE)
    disc(draw, c, c, 34, fill=OUTLINE)
    disc(draw, c, c, 30, fill=TEAL_DARK)
    disc(draw, c, c - 2, 27, fill=TEAL)
    disc(draw, c, c, 18, fill=OUTLINE)
    disc(draw, c, c + 1, 14, fill=HOLE)
    for dx, dy in ((0, -1), (1, 0), (0, 1), (-1, 0)):
        disc(draw, c + dx * 41, c + dy * 41, 4, fill=OUTLINE)
        disc(draw, c + dx * 41, c + dy * 41 - 0.5, 2.5, fill=CASE_LIT)
    return finish(image, size, size)


def crate(draw, cx, cy, w, h):
    rect(draw, (cx - w, cy - h, cx + w, cy + h), fill=OUTLINE, radius=4)
    rect(draw, (cx - w + 3, cy - h + 3, cx + w - 3, cy + h - 3), fill=CASE, radius=3)
    rect(draw, (cx - w + 3, cy - h + 3, cx + w - 3, cy - h + 10), fill=CASE_LIT, radius=3)


def arrow(draw, x0, y, x1, color, dark):
    d = 1 if x1 > x0 else -1
    line(draw, [(x0, y), (x1 - d * 10, y)], OUTLINE, 14)
    line(draw, [(x0, y), (x1 - d * 10, y)], color, 8)
    polygon(draw, [(x1 + d * 4, y), (x1 - d * 16, y - 16), (x1 - d * 16, y + 16)], fill=OUTLINE)
    polygon(draw, [(x1 - d * 1, y), (x1 - d * 13, y - 10), (x1 - d * 13, y + 10)], fill=color)


def icon_load():
    image, draw = new_canvas(64, 64)
    crate(draw, 44, 34, 16, 18)
    arrow(draw, 4, 34, 34, TEAL, TEAL_DARK)
    return finish(image, 64, 64)


def icon_unload():
    image, draw = new_canvas(64, 64)
    crate(draw, 20, 34, 16, 18)
    arrow(draw, 30, 34, 60, AMBER, AMBER_DARK)
    return finish(image, 64, 64)


def icon_filter():
    image, draw = new_canvas(64, 64)
    polygon(draw, [(6, 8), (58, 8), (38, 34), (38, 56), (26, 50), (26, 34)], fill=OUTLINE)
    polygon(draw, [(12, 12), (52, 12), (34, 33), (34, 50), (30, 48), (30, 33)], fill=AMBER)
    line(draw, [(14, 14), (50, 14)], WHITE, 2)
    return finish(image, 64, 64)


def save(image, folder, name):
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, name)
    image.save(path)
    print("wrote", path, image.size)


def main():
    save(dock(), BUILDING, "VehicleDock.png")
    save(icon_load(), COMMANDS, "DockLoad.png")
    save(icon_unload(), COMMANDS, "DockUnload.png")
    save(icon_filter(), COMMANDS, "DockFilter.png")


if __name__ == "__main__":
    main()
