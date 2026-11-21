using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace FactoryLogistics
{
    /// <summary>
    /// Feeds Ancient Mining Industry's chunk machines (the ore dresser and anything else built on
    /// its CompAutoStoneCutter) from this mod's logistics. Found by name: no dependency either way.
    ///
    /// Those machines take a stone chunk lying on the ground in the ring of cells around them.
    /// Chunks travelling in a tube capsule or on a VFE belt are held inside it, so the machine
    /// cannot see them. When a machine is empty, switched on and has no chunk on the ground around
    /// it, this takes one from a chunk source in that ring and puts it straight into the machine:
    /// a delivered tube capsule in a receiver there, or (with the belts module) a belt there.
    /// </summary>
    public static class StoneCutterCompat
    {
        /// <summary>
        /// Extra places to take a chunk from: given the map and the ring cells, return a chunk
        /// already removed from wherever it was (unspawned, not held), or null. The belts module
        /// adds the belt source.
        /// </summary>
        public static readonly List<Func<Map, List<IntVec3>, Thing>> ChunkSources = new List<Func<Map, List<IntVec3>, Thing>>();

        private static bool resolved;
        private static Type compType;
        private static PropertyInfo shouldBeOn;

        public static Type CompType
        {
            get
            {
                if (!resolved)
                {
                    resolved = true;
                    compType = GenTypes.GetTypeInAnyAssembly("AncientMining.CompAutoStoneCutter");
                    if (compType != null)
                        shouldBeOn = compType.GetProperty("shouldBeOn", BindingFlags.Instance | BindingFlags.Public);
                }
                return compType;
            }
        }

        /// <summary>A stone chunk, as that mod counts them: in Stone chunks or one of its sub-categories.</summary>
        public static bool IsChunk(Thing thing)
        {
            List<ThingCategoryDef> cats = thing != null ? thing.def.thingCategories : null;
            if (cats == null)
                return false;
            ThingCategoryDef chunks = ThingCategoryDefOf.StoneChunks;
            for (int i = 0; i < cats.Count; i++)
            {
                if (cats[i] == chunks || chunks.childCategories.Contains(cats[i]))
                    return true;
            }
            return false;
        }

        public static List<IntVec3> RingCells(Thing machine)
        {
            return GenAdjFast.AdjacentCells8Way(machine.Position, machine.Rotation, machine.RotatedSize);
        }

        /// <summary>Empty, switched on, and nothing for it on the ground around it.</summary>
        public static bool Hungry(ThingComp comp, List<IntVec3> ring)
        {
            IThingHolder holder = comp as IThingHolder;
            ThingOwner held = holder != null ? holder.GetDirectlyHeldThings() : null;
            if (held == null || held.Count > 0)
                return false;
            if (shouldBeOn != null)
            {
                object on = shouldBeOn.GetValue(comp, null);
                if (on is bool && !(bool)on)
                    return false;
            }
            Map map = comp.parent.Map;
            for (int i = 0; i < ring.Count; i++)
            {
                if (!ring[i].InBounds(map))
                    continue;
                List<Thing> things = ring[i].GetThingList(map);
                for (int j = 0; j < things.Count; j++)
                {
                    if (things[j].def.category == ThingCategory.Item && IsChunk(things[j]))
                        return false;
                }
            }
            return true;
        }

        /// <summary>A chunk from a delivered capsule sitting in the ring; an emptied capsule becomes a shell.</summary>
        public static Thing ChunkFromTubes(Map map, List<IntVec3> ring)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                if (!ring[i].InBounds(map))
                    continue;
                List<Thing> things = ring[i].GetThingList(map);
                for (int j = 0; j < things.Count; j++)
                {
                    TubeCapsule capsule = things[j] as TubeCapsule;
                    if (capsule == null || !capsule.Delivered)
                        continue;
                    ThingOwner contents = capsule.Contents;
                    for (int k = 0; k < contents.Count; k++)
                    {
                        Thing item = contents[k];
                        if (!IsChunk(item))
                            continue;
                        Thing chunk = item.stackCount > 1 ? item.SplitOff(1) : item;
                        if (chunk == item)
                            contents.Remove(item);
                        if (contents.Count == 0)
                            Shell(capsule);
                        return chunk;
                    }
                }
            }
            return null;
        }

        private static void Shell(TubeCapsule capsule)
        {
            Map map = capsule.Map;
            IntVec3 at = capsule.Position;
            capsule.Destroy(DestroyMode.Vanish);
            GenPlace.TryPlaceThing(ThingMaker.MakeThing(TubeDefOf.FL_CapsuleShell), at, map, ThingPlaceMode.Near);
        }
    }

    /// <summary>Runs StoneCutterCompat on each map, once a second.</summary>
    public class StoneCutterIntake : MapComponent
    {
        private const int Interval = 60;
        private const int RescanInterval = 600;

        private readonly List<ThingComp> cutters = new List<ThingComp>();
        private int scannedAt = -99999;

        public StoneCutterIntake(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            Type type = StoneCutterCompat.CompType;
            if (type == null)
                return;
            int now = Find.TickManager.TicksGame;
            if (now % Interval != 0)
                return;

            if (now - scannedAt >= RescanInterval || now < scannedAt)
            {
                scannedAt = now;
                cutters.Clear();
                List<Building> buildings = map.listerBuildings.allBuildingsColonist;
                for (int i = 0; i < buildings.Count; i++)
                {
                    ThingWithComps owner = buildings[i];
                    List<ThingComp> comps = owner.AllComps;
                    for (int j = 0; j < comps.Count; j++)
                    {
                        if (type.IsInstanceOfType(comps[j]))
                            cutters.Add(comps[j]);
                    }
                }
            }

            for (int i = 0; i < cutters.Count; i++)
            {
                ThingComp comp = cutters[i];
                if (comp.parent == null || !comp.parent.Spawned || comp.parent.Map != map)
                    continue;
                List<IntVec3> ring = StoneCutterCompat.RingCells(comp.parent);
                if (!StoneCutterCompat.Hungry(comp, ring))
                    continue;

                Thing chunk = StoneCutterCompat.ChunkFromTubes(map, ring);
                for (int s = 0; chunk == null && s < StoneCutterCompat.ChunkSources.Count; s++)
                    chunk = StoneCutterCompat.ChunkSources[s](map, ring);
                if (chunk != null && !((IThingHolder)comp).GetDirectlyHeldThings().TryAdd(chunk, true))
                    GenPlace.TryPlaceThing(chunk, comp.parent.Position, map, ThingPlaceMode.Near);
            }
        }
    }
}
