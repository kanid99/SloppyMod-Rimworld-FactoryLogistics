using System.Collections.Generic;
using System.Linq;
using PipeSystem;
using RimWorld;
using UnityEngine;
using Verse;

namespace FactoryLogistics
{
    /// <summary>
    /// The way into the tubes. It is a one-cell storage building that takes only capsules
    /// addressed to it, so a pack bill can be told to "take to" it like a shelf and haulers
    /// bring it strays. Whatever capsule is sitting in it is launched to the one receiving
    /// station it has been pointed at, as soon as the route is open and the tanks hold
    /// enough air.
    ///
    /// It is IRenameable because that is what puts a storage building in a bill's "take to"
    /// list; the members themselves are on the base class.
    /// </summary>
    public class Building_TubeSender : Building_TubeStation, IRenameable
    {
        private const int LaunchCheckInterval = 30;

        private Building_TubeReceiver destination;

        public Building_TubeReceiver Destination
        {
            get
            {
                if (destination != null && (destination.Destroyed || !destination.Spawned))
                    destination = null;
                return destination;
            }
            set { destination = value; }
        }

        /// <summary>"Kitchen line -> Kitchen", for lists that have to say where a sender goes.</summary>
        public string RouteLabel
        {
            get
            {
                Building_TubeReceiver target = Destination;
                return StationName + " -> " + (target != null ? target.StationName : "FactoryLogistics.NoDestinationShort".Translate().ToString());
            }
        }

        public override bool AcceptsThing(Thing thing)
        {
            TubeCapsule capsule = thing as TubeCapsule;
            return capsule != null && !capsule.Delivered && capsule.AssignedSender == this;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref destination, "destination");
        }

        protected override void Tick()
        {
            base.Tick();
            if (Spawned && this.IsHashIntervalTick(LaunchCheckInterval))
                TryLaunch();
        }

        /// <summary>
        /// Why this station cannot send right now, as a translation key, or null if it can.
        /// With forLaunch false the two conditions that clear by themselves in normal running
        /// (receiver full, tanks refilling) are not reported.
        /// </summary>
        public string BlockReasonKey(bool forLaunch)
        {
            Building_TubeReceiver target = Destination;
            if (target == null)
                return "FactoryLogistics.BlockNoDestination";
            if (!AirOn)
                return "FactoryLogistics.BlockNoAir";
            PipeNet net = Net;
            if (net == null || net != target.Net)
                return "FactoryLogistics.BlockNotLinked";
            if (!target.AirOn)
                return "FactoryLogistics.BlockDestinationNoAir";
            if (!forLaunch)
                return null;
            if (target.FreeSlots <= 0)
                return "FactoryLogistics.BlockDestinationFull";
            // Tanks only. The network also keeps a small overflow buffer of its own, but air
            // taken from there would never leave the tanks short, and tanks that are not short
            // are what lets the compressor idle - the burst would cost nothing.
            if (net.CurrentStored() < Ext.launchAirCost)
                return "FactoryLogistics.BlockLowPressure";
            return null;
        }

        public TubeCapsule WaitingCapsule()
        {
            return Spawned ? FirstHeldCapsule(false) : null;
        }

        private void TryLaunch()
        {
            TubeCapsule capsule = WaitingCapsule();
            if (capsule == null || BlockReasonKey(true) != null)
                return;

            Building_TubeReceiver target = Destination;

            // The burst: a lump of air out of the network's tanks. The compressor then runs at
            // full power until they are full again, which is where the electricity goes.
            float drawn;
            Net.DrawAmongStorage(Ext.launchAirCost, out drawn, null, false);

            float distance = Position.DistanceTo(target.Position);
            int travel = Mathf.Clamp(Mathf.RoundToInt(distance * Ext.transitTicksPerCell),
                Ext.minTransitTicks, Ext.maxTransitTicks);

            Vector3 puffAt = DrawPos;
            capsule.DeSpawn();
            TubeCapsule delivered = capsule.ConvertTo(TubeDefOf.FL_CapsuleDelivered, null);
            delivered.arriveTick = Find.TickManager.TicksGame + travel;
            target.TakeInTransit(delivered);

            FleckMaker.ThrowAirPuffUp(puffAt, Map);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;

            if (Faction != Faction.OfPlayer)
                yield break;

            Command_Action pick = new Command_Action();
            pick.icon = TubeTex.SetDestination;
            pick.defaultLabel = "FactoryLogistics.SetDestination".Translate();
            pick.defaultDesc = "FactoryLogistics.SetDestinationDesc".Translate();
            pick.action = delegate { OpenDestinationMenu(); };
            yield return pick;
        }

        private void OpenDestinationMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("FactoryLogistics.DestinationNone".Translate(), delegate { Destination = null; }));

            PipeNet net = Net;
            foreach (Building_TubeReceiver receiver in TubeUtility.Receivers(Map).OrderBy(r => r.StationName))
            {
                Building_TubeReceiver target = receiver;
                bool linked = net != null && net == target.Net;
                // A receiver that is not on this network can still be chosen: the player may
                // be about to lay the tube. The station just waits until the link exists.
                string label = linked
                    ? target.StationName
                    : target.StationName + " (" + "FactoryLogistics.NotLinkedShort".Translate().ToString() + ")";
                options.Add(new FloatMenuOption(label, delegate { Destination = target; }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            if (!text.NullOrEmpty())
                text += "\n";

            Building_TubeReceiver target = Destination;
            text += "FactoryLogistics.SenderDestination".Translate(target != null ? target.StationName : "FactoryLogistics.NoDestinationShort".Translate().ToString());

            if (Spawned)
            {
                bool waiting = WaitingCapsule() != null;
                string blocked = BlockReasonKey(waiting);
                text += "\n" + (blocked != null
                    ? "FactoryLogistics.SenderBlocked".Translate(blocked.Translate())
                    : (waiting ? "FactoryLogistics.SenderLaunching".Translate() : "FactoryLogistics.SenderReady".Translate()));
                text += "\n" + "FactoryLogistics.LaunchCost".Translate(Ext.launchAirCost.ToString("F0"));
            }
            return text;
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            Building_TubeReceiver target = Destination;
            if (target != null && target.Map == Map)
                GenDraw.DrawLineBetween(this.TrueCenter(), target.TrueCenter());
        }
    }
}
