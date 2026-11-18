# SloppyMods Factory Logistics

A RimWorld mod (1.6 and 1.7) for moving goods around a colony without hauling them
by hand. Requires Vanilla Expanded Framework; Vanilla Furniture Expanded - Factory
is optional.

It is one mod in modules:

| Module | Needs | What it adds | State |
| --- | --- | --- | --- |
| Core: pneumatic tubes | Vanilla Expanded Framework | Point-to-point capsule delivery across the base (below) | Built, not yet load-tested |
| Vehicle dock | Vehicle Framework | A pad that loads parked vehicles from the tubes and unloads them into the tubes | Built (`Mods/Vehicles`), not yet load-tested |
| Belts | Vanilla Furniture Expanded - Factory | Conveyor arms that load belts from storage and feed machines on demand | Built (`Mods/Belts`), not yet load-tested |

## Roadmap

1. Merge the pneumatic tubes in as the core (0.9.4).
2. **Belts module** (0.9.5). One rotatable *conveyor arm* building with two modes:
   - *Loader*: takes from the storage behind it and puts onto the belt in front -
     only what the network's machine has asked for.
   - *Feeder*: sits at the end of a belt and puts what arrives into the VFE
     machine's input slot in front of it (or the hopper on it). One per network.
     It reads the machine's current process for what it still needs; anything
     that arrives with no taker is dropped beside it, so a belt never jams.
   A Loader hands its item to the conveyor in front through VFE's public
   `Building_Conveyor.TryAdd`; a VFE belt drops items onto a walkable,
   non-edifice cell at its end, which is where a Feeder stands. So the arm
   needs no patch to VFE. Networks follow merges and underground conveyors
   (exit to linked entrance) and are rebuilt every two seconds, as VFE gives no
   notice when a belt is built or removed. Source: `Source/FactoryLogisticsBelts`,
   assembly `Mods/Belts/Assemblies/FactoryLogisticsBelts.dll`, art
   `Source/Art/belts_art.py`.
3. **Shared request layer:** feeders post their machine's needs; belts and tubes
   both fill them.
4. **Bridges:** an auto-unpacking tube receiver that loaders can draw from, and
   pack bills driven by machine demand. Later: a junction that routes one supply
   trunk to several feeders, with priorities and per-machine in-transit caps.

# Pneumatic tubes

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
