using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace FactoryLogistics
{
    /// <summary>
    /// The way out of the tubes. Any number of sending stations can point at one. Capsules in
    /// flight are held inside it until they are due, then set down in its storage cells, where
    /// an unpack bill's ordinary ingredient search finds them. How many it holds is its size
    /// times the def's maxItemsInCell, and a full receiver makes its senders wait.
    /// </summary>
    public class Building_TubeReceiver : Building_TubeStation, IThingHolder
    {
        private const int ArrivalCheckInterval = 10;

        private ThingOwner<Thing> transit;

        public Building_TubeReceiver()
        {
            transit = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

        public virtual int Capacity
        {
            get { return AllSlotCellsList().Count * Mathf.Max(1, def.building.maxItemsInCell); }
        }

        public int InTransitCount
        {
            get { return transit.Count; }
        }

        /// <summary>Room for more capsules, counting the ones already on their way.</summary>
        public virtual int FreeSlots
        {
            get { return Spawned ? Capacity - HeldItemCount() - transit.Count : 0; }
        }

        /// <summary>
        /// Whether a sender may fire this capsule here now (null: any capsule). A receiver whose
        /// room depends on what is sent - a vehicle dock counting weight - overrides it.
        /// </summary>
        public virtual bool HasRoomFor(TubeCapsule capsule)
        {
            return FreeSlots > 0;
        }

        /// <summary>The capsules on their way here.</summary>
        protected ThingOwner<Thing> Transit
        {
            get { return transit; }
        }

        // Only what is already inside counts as correctly stored here. Haulers never carry a
        // capsule INTO a receiver, which would use up slots the senders are counting on; and
        // because what is inside is "in valid storage", they do not carry it off either unless
        // the player sets up something with a higher priority.
        public override bool AcceptsThing(Thing thing)
        {
            TubeCapsule capsule = thing as TubeCapsule;
            return capsule != null && capsule.Delivered && HoldsInItsCells(thing);
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return transit;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref transit, "transit", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && transit == null)
                transit = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

        public void TakeInTransit(TubeCapsule capsule)
        {
            if (!transit.TryAdd(capsule, false))
                GenPlace.TryPlaceThing(capsule, Position, Map, ThingPlaceMode.Near);
        }

        public int CountMatchingInTransit(ThingFilter filter)
        {
            int count = 0;
            for (int i = 0; i < transit.Count; i++)
            {
                TubeCapsule capsule = transit[i] as TubeCapsule;
                if (capsule != null)
                    count += capsule.CountMatching(filter);
            }
            return count;
        }

        protected override void Tick()
        {
            base.Tick();
            // Capsules in flight keep ticking too, so their contents age on the way.
            transit.DoTick();

            if (!Spawned || transit.Count == 0 || !this.IsHashIntervalTick(ArrivalCheckInterval))
                return;

            int now = Find.TickManager.TicksGame;
            for (int i = transit.Count - 1; i >= 0; i--)
            {
                TubeCapsule capsule = transit[i] as TubeCapsule;
                if (capsule == null || now >= capsule.arriveTick)
                    SetDown(transit[i]);
            }
        }

        /// <summary>A capsule has arrived: put it in the station's cells.</summary>
        protected virtual void SetDown(Thing thing)
        {
            Thing placed;
            int perCell = Mathf.Max(1, def.building.maxItemsInCell);
            List<IntVec3> cells = AllSlotCellsList();
            for (int i = 0; i < cells.Count; i++)
            {
                // Direct placement does not check how full a cell is for things that do not
                // stack, so the check is made here.
                if (ItemCountAt(cells[i]) >= perCell)
                    continue;
                if (transit.TryDrop(thing, cells[i], Map, ThingPlaceMode.Direct, out placed))
                {
                    FleckMaker.ThrowAirPuffUp(cells[i].ToVector3Shifted(), Map);
                    return;
                }
            }
            // Something else is taking up the slots (a hand-placed item). Put it beside the
            // station so nothing is ever stuck inside.
            transit.TryDrop(thing, Position, Map, ThingPlaceMode.Near, out placed);
        }

        // Uninstalled, deconstructed or destroyed with capsules in flight: they come out here.
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map map = Map;
            IntVec3 position = Position;
            base.DeSpawn(mode);
            if (transit.Count > 0 && map != null)
                transit.TryDropAll(position, map, ThingPlaceMode.Near);
        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            if (!text.NullOrEmpty())
                text += "\n";
            if (Spawned)
            {
                text += "FactoryLogistics.ReceiverHolding".Translate(HeldItemCount(), Capacity);
                if (transit.Count > 0)
                    text += "\n" + "FactoryLogistics.ReceiverInTransit".Translate(transit.Count);
                if (!AirOn)
                    text += "\n" + "FactoryLogistics.ReceiverNoAir".Translate();
            }
            return text;
        }
    }
}
