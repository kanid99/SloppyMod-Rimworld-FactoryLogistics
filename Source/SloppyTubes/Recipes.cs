using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace SloppyTubes
{
    /// <summary>
    /// Carries one pack job's pieces between the three vanilla hooks that see them.
    ///
    /// When a bill finishes, vanilla makes the product, then hands each ingredient to
    /// RecipeWorker.ConsumeIngredient, then calls Notify_IterationCompleted - and none of the
    /// three is given both the product and the ingredients. So the capsule notes itself here
    /// when it is made, the worker parks the ingredients here in place of destroying them, and
    /// Finish puts them together. Finish does not depend on which of the first two happened
    /// first, only on being called after both.
    ///
    /// All of it happens inside one toil on the main thread, in one tick.
    /// </summary>
    public static class PackContext
    {
        private static TubeCapsule lastCapsule;
        private static int lastTick = -1;
        private static readonly List<Thing> pending = new List<Thing>();

        public static void NoteMade(TubeCapsule capsule)
        {
            if (Current.ProgramState != ProgramState.Playing)
                return;
            lastCapsule = capsule;
            lastTick = Find.TickManager.TicksGame;
        }

        public static void Park(Thing ingredient)
        {
            if (ingredient.Spawned)
                ingredient.DeSpawn();
            else if (ingredient.holdingOwner != null)
                ingredient.holdingOwner.Remove(ingredient);
            pending.Add(ingredient);
        }

        public static void Finish(Pawn billDoer)
        {
            TubeCapsule capsule = lastCapsule;
            lastCapsule = null;

            bool usable = capsule != null && !capsule.Destroyed && !capsule.Spawned
                && lastTick == Find.TickManager.TicksGame;

            for (int i = 0; i < pending.Count; i++)
            {
                Thing thing = pending[i];
                if (thing == null || thing.Destroyed)
                    continue;
                // Never lose the player's items: anything that cannot go in the capsule is
                // put back on the ground next to the packer.
                if (!usable || !capsule.Contents.TryAdd(thing, true))
                    GenPlace.TryPlaceThing(thing, billDoer.Position, billDoer.Map, ThingPlaceMode.Near);
            }
            pending.Clear();

            if (!usable)
            {
                Log.ErrorOnce("[SloppyMods Pneumatic Tubes] A pack bill finished without a capsule to fill; "
                    + "the items were put back on the ground. Please report this.", 77316041);
                return;
            }

            // Address the capsule to the station the bill takes its product to. This has to
            // happen here, before vanilla looks for a storage cell, because the station only
            // accepts capsules addressed to it.
            capsule.AssignedSender = TubeUtility.SenderForBill(billDoer.CurJob != null ? billDoer.CurJob.bill : null);
        }
    }

    public class RecipeWorker_PackCapsule : RecipeWorker
    {
        public override void ConsumeIngredient(Thing ingredient, RecipeDef recipe, Map map)
        {
            // The empty shell is used up; it comes back when the capsule is unpacked.
            if (ingredient.def == TubeDefOf.SloppyTubes_CapsuleShell)
            {
                base.ConsumeIngredient(ingredient, recipe, map);
                return;
            }
            PackContext.Park(ingredient);
        }

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            PackContext.Finish(billDoer);
        }
    }

    public class RecipeWorker_UnpackCapsule : RecipeWorker
    {
        public override void ConsumeIngredient(Thing ingredient, RecipeDef recipe, Map map)
        {
            TubeCapsule capsule = ingredient as TubeCapsule;
            if (capsule != null && capsule.Contents.Count > 0)
            {
                IntVec3 where = capsule.PositionHeld;
                if (!where.IsValid || !where.InBounds(map))
                    where = map.Center;
                capsule.Contents.TryDropAll(where, map, ThingPlaceMode.Near);
            }
            // The capsule itself goes; the recipe's product is the empty shell.
            base.ConsumeIngredient(ingredient, recipe, map);
        }
    }

    /// <summary>
    /// Measures the cargo as a fraction of a full stack, so one recipe can say "a full stack of
    /// whatever this is": 75 steel, 10 meals, one rifle. The shell is counted in whole shells.
    /// </summary>
    public class IngredientValueGetter_StackFraction : IngredientValueGetter
    {
        public override float ValuePerUnitOf(ThingDef t)
        {
            if (t == TubeDefOf.SloppyTubes_CapsuleShell)
                return 1f;
            return 1f / Mathf.Max(1, t.stackLimit);
        }

        public override string BillRequirementsDescription(RecipeDef r, IngredientCount ing)
        {
            if (ing.IsFixedIngredient)
                return "SloppyTubes.BillNeedsShell".Translate();
            return "SloppyTubes.BillNeedsCargo".Translate(Mathf.Round(ing.GetBaseCount() * 100f).ToString("F0"));
        }
    }

    /// <summary>
    /// Makes "do until you have X" mean something for a pack bill. Vanilla would count capsules
    /// on the map, which says nothing; this counts the cargo the bill's filter allows that is
    /// already at the far end or on its way there:
    ///
    ///   - in the room the receiving station stands in (or near it, if it is not in a room),
    ///   - inside delivered capsules waiting there,
    ///   - in transit to that station,
    ///   - packed and addressed to the bill's sending station but not sent yet.
    ///
    /// A bill that is not set to take its capsules to a sending station has no far end, so it
    /// counts nothing and "until you have X" behaves like "forever".
    /// </summary>
    public class RecipeWorkerCounter_PackCapsule : RecipeWorkerCounter
    {
        private const int CacheTicks = 120;
        private const float NoRoomRadius = 8.9f;
        private const int LargestRoomCounted = 1200;

        private class Cached
        {
            public int tick = -99999;
            public int count;
        }

        private static readonly ConditionalWeakTable<Bill_Production, Cached> cache =
            new ConditionalWeakTable<Bill_Production, Cached>();

        public override bool CanCountProducts(Bill_Production bill)
        {
            return true;
        }

        public override string ProductsDescription(Bill_Production bill)
        {
            return "SloppyTubes.CountDescription".Translate();
        }

        public override int CountProducts(Bill_Production bill)
        {
            // Bills are checked every time a pawn looks for work, so the answer is kept for a
            // couple of seconds.
            Cached cached = cache.GetOrCreateValue(bill);
            int now = Find.TickManager.TicksGame;
            if (now - cached.tick < CacheTicks && now >= cached.tick)
                return cached.count;
            cached.tick = now;
            cached.count = Count(bill);
            return cached.count;
        }

        private static int Count(Bill_Production bill)
        {
            Building_TubeSender sender = TubeUtility.SenderForBill(bill);
            if (sender == null || !sender.Spawned)
                return 0;

            Map map = sender.Map;
            ThingFilter filter = bill.ingredientFilter;
            int count = 0;

            List<Thing> outbound = map.listerThings.ThingsOfDef(TubeDefOf.SloppyTubes_Capsule);
            for (int i = 0; i < outbound.Count; i++)
            {
                TubeCapsule capsule = outbound[i] as TubeCapsule;
                if (capsule != null && capsule.AssignedSender == sender)
                    count += capsule.CountMatching(filter);
            }

            Building_TubeReceiver receiver = sender.Destination;
            if (receiver == null || !receiver.Spawned || receiver.Map != map)
                return count;

            count += receiver.CountMatchingInTransit(filter);

            Room room = receiver.GetRoom();
            bool useRoom = room != null && !room.TouchesMapEdge && room.CellCount <= LargestRoomCounted;
            if (useRoom)
            {
                List<Thing> things = room.ContainedAndAdjacentThings;
                for (int i = 0; i < things.Count; i++)
                    count += CountThing(things[i], filter);
            }
            else
            {
                foreach (Thing thing in GenRadial.RadialDistinctThingsAround(receiver.Position, map, NoRoomRadius, true))
                    count += CountThing(thing, filter);
            }
            return count;
        }

        private static int CountThing(Thing thing, ThingFilter filter)
        {
            if (thing.def.category != ThingCategory.Item)
                return 0;
            TubeCapsule capsule = thing as TubeCapsule;
            if (capsule != null)
                return capsule.Delivered ? capsule.CountMatching(filter) : 0;
            return filter.Allows(thing) ? thing.stackCount : 0;
        }
    }
}
