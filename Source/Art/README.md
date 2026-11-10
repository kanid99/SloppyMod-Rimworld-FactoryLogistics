# Pneumatic tube artwork

Every sprite is drawn from primitives by `tubes_art.py`, with the shared helpers and
palette in `tubes_draw.py`. Nothing is generated and there are no source files to keep:
the art rebuilds from a clean checkout with only Pillow installed.

    pip install pillow
    python3 Source/Art/tubes_art.py      # from the repo root

It writes the building, item and interface textures and the About images, and fails if
the tube atlas does not have an arm exactly where each tile's link bitmask says a
neighbour is.

The rules are the ones the other SloppyMods art follows: near-black only on the outer
silhouette, depth as flat steps of tone lit from above, and detail only as a row of
identical marks. There are two accents and they mean something: teal is the way in
(senders, packed capsules), amber is the way out (receivers, delivered capsules).

Storage buildings draw their contents on top of themselves, so the middle of each
station sprite is kept plain.
