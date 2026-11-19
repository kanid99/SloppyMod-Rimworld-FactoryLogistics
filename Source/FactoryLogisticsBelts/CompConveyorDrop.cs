using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using VanillaFurnitureExpandedFactory;

namespace FactoryLogistics.Belts
{
    public class CompProperties_ConveyorDrop : CompProperties
    {
        public CompProperties_ConveyorDrop()
        {
            compClass = typeof(CompConveyorDrop);
        }
    }

    /// <summary>
    /// Added to every VFE Factory conveyor by a patch: a "Drop items" button that kicks whatever
    /// the selected belt piece is carrying off it, onto the floor beside the line - never onto
    /// another belt or one of this module's buildings, where it would go straight back on.
    /// </summary>
    public class CompConveyorDrop : ThingComp
    {
        private Building_Conveyor Belt
        {
            get { return parent as Building_Conveyor; }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Building_Conveyor belt = Belt;
            if (belt == null || belt.Faction != Faction.OfPlayer || belt.innerContainer == null || belt.innerContainer.Count == 0)
                yield break;

            Command_Action drop = new Command_Action();
            drop.defaultLabel = "FactoryLogistics.BeltDrop".Translate();
            drop.defaultDesc = "FactoryLogistics.BeltDropDesc".Translate();
            drop.icon = ContentFinder<Texture2D>.Get("UI/Commands/FactoryLogistics/BeltDrop");
            drop.action = DropAll;
            yield return drop;
        }

        public void DropAll()
        {
            Building_Conveyor belt = Belt;
            if (belt == null || !belt.Spawned || belt.innerContainer == null)
                return;
            Map map = belt.Map;
            IntVec3 at = belt.Position;

            foreach (Thing thing in belt.innerContainer.InnerListForReading.ToArray())
            {
                belt.innerContainer.Remove(thing);
                bool placed = GenPlace.TryPlaceThing(thing, at, map, ThingPlaceMode.Near, null,
                    delegate (IntVec3 c) { return OffTheLine(c, map); });
                if (!placed && !thing.Destroyed && !thing.Spawned)
                    GenPlace.TryPlaceThing(thing, at, map, ThingPlaceMode.Near);
            }

            belt.state = Building_Conveyor.ConveyorState.Waiting;
            belt.itemProgress = 0f;
        }

        private static bool OffTheLine(IntVec3 c, Map map)
        {
            if (!c.Standable(map))
                return false;
            List<Thing> things = c.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t is Building_Conveyor || t is Building_LogisticsArm || t is Building_ConveyorLift || t is Building_BeltJunction)
                    return false;
            }
            return true;
        }
    }
}
