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

        // VFE's belt only pulls from a hopper beside it while it is Empty; one left Waiting with
        // nothing on it never ticks again. Earlier builds of this mod left belts like that after
        // lifting an item off them, so any belt found in that state is put back to Empty.
        public override void CompTick()
        {
            base.CompTick();
            if (!parent.IsHashIntervalTick(250))
                return;
            Building_Conveyor belt = Belt;
            if (belt != null && belt.Spawned && belt.innerContainer != null && belt.innerContainer.Count == 0
                && belt.state == Building_Conveyor.ConveyorState.Waiting)
            {
                belt.state = Building_Conveyor.ConveyorState.Empty;
                belt.itemProgress = 0f;
            }
        }

        // Whether VFE's pull from a factory hopper beside this belt is allowed (see HopperPullGuard).
        public bool pullFromHoppers = true;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref pullFromHoppers, "pullFromHoppers", true);
        }

        private bool HopperBeside()
        {
            if (!parent.Spawned)
                return false;
            for (int i = 0; i < 4; i++)
            {
                Building b = (parent.Position + GenAdj.CardinalDirections[i]).GetEdifice(parent.Map);
                if (b is Building_FactoryHopper || (b != null && b.def.GetModExtension<PipeSystem.FactoryHopperExtension>()?.isfactoryHopper == true))
                    return true;
            }
            return false;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Building_Conveyor belt = Belt;
            if (belt == null || belt.Faction != Faction.OfPlayer)
                yield break;

            if (!pullFromHoppers || HopperBeside())
            {
                Command_Toggle pull = new Command_Toggle();
                pull.defaultLabel = "FactoryLogistics.BeltPullHoppers".Translate();
                pull.defaultDesc = "FactoryLogistics.BeltPullHoppersDesc".Translate();
                pull.icon = ContentFinder<Texture2D>.Get("Things/Building/FactoryHopper/FactoryHopper_Unset_south", false) ?? BaseContent.BadTex;
                pull.isActive = delegate { return pullFromHoppers; };
                pull.toggleAction = delegate { pullFromHoppers = !pullFromHoppers; };
                yield return pull;
            }

            if (belt.innerContainer == null || belt.innerContainer.Count == 0)
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

            belt.state = Building_Conveyor.ConveyorState.Empty;
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
