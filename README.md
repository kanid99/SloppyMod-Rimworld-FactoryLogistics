# SloppyMods Pneumatic Tubes

A RimWorld mod (1.6 and 1.7): a pneumatic tube network for moving goods around a
colony quickly. Requires Vanilla Expanded Framework.

## What the player builds

| Thing | What it does |
| --- | --- |
| Pneumatic tube | Links stations. Carries compressed air; can run under walls. |
| Air compressor | Pressurises the tubes. 40 W while the tanks are full, 300 W while refilling them. Breaks down and can be switched off like any powered machine. |
| Air tank | Stores 150 L. Each capsule sent takes 30 L from the tanks, so tanks set how many can go in a row. |
| Tube sender | The way in. Pointed at one receiver. Takes only capsules addressed to it. |
| Tube receiver / receiver rack | The way out. Holds 2 / 8 capsules. Any number of senders can point at one. |
| Packing bench | Pack bills (full, half, tenth of a stack) and an unpack bill. No power or air needed. |
| Capsule shell | Made five at a time from 20 steel at a machining table or smithy. Used by packing, returned by unpacking. |

## The loop

1. A pack bill at a packing bench gathers a stack of whatever its filter allows,
   within its ingredient radius, plus one shell, and produces a **packed capsule**
   with the goods inside.
2. The bill is set to "take to" a tube sender, which shows up in the bill's
   storage list like a named shelf. The capsule is addressed to that sender and
   the packer carries it there as the last step of the bill.
3. The sender fires any capsule sitting in it at its receiver, if both are on the
   same tubes, both have air, the receiver has room and the tanks hold 30 L.
4. After a short flight (2 ticks per cell) it appears in the receiver as a
   **delivered capsule**.
5. An unpack bill at a bench near the receiver opens it: the goods are set down
   at the bench and the shell is kept.

"Do until you have X" on a pack bill counts the goods its filter allows in the
receiver's room (or within 9 cells if it is not in a room), in delivered capsules
there, in flight, and packed for that sender but not yet sent.

A capsule can also be handled by hand: select it to address it to a sender, send
a delivered one onward unopened, or mark a packed one for unpacking where it is.

## How it leans on vanilla

There are no custom jobs, work givers or Harmony patches. Each piece is a vanilla
mechanism with one class changing its meaning:

- **Packing is a bill.** `RecipeWorker_PackCapsule` parks the ingredients vanilla
  would destroy and puts them in the product; `IngredientValueGetter_StackFraction`
  makes the ingredient count mean "a fraction of a full stack";
  `RecipeWorkerCounter_PackCapsule` makes target counts look at the destination.
- **Stations are storage buildings.** That is what lets a bill deliver to a sender
  and an unpack bill find capsules in a receiver. `Building_TubeStation` narrows
  what each one's storage accepts down to individual capsules.
- **Packed and delivered capsules are two defs** with one class, so ordinary
  filters can tell them apart.
- **The tubes are a VEF PipeSystem net** whose resource is compressed air. The
  framework handles laying, linking, the overlay, the compressor's idle mode and
  the tanks; the mod only asks it whether two stations share a net and takes air
  out of the tanks at launch.

## Not in this build

- A buried (under-floor) tube.
- Refrigerated or auto-unpacking receivers, send-only/receive-only variants.
- Capsules bigger than one stack, and mixing different goods in one capsule.
- Sounds.

## Building

See CLAUDE.md.

## Licence

CC0.
