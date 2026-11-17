using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace FactoryLogistics
{
    /// <summary>
    /// A capsule and whatever was packed into it. It is an ordinary item that happens to hold
    /// other items, the way a minified building holds a building, so it can be hauled, stored,
    /// forbidden, dropped and stolen like anything else.
    ///
    /// The same class backs both capsule defs (see TubeDefOf). Going from one to the other is
    /// ConvertTo, which makes a new thing and moves the contents across, because a thing's def
    /// cannot change.
    /// </summary>
    public class TubeCapsule : ThingWithComps, IThingHolder
    {
        private ThingOwner<Thing> innerContainer;

        // The sending station this capsule is meant for. Stamped by the pack bill from its
        // "take to" setting, or set by hand from the capsule's gizmo. Only that station's
        // storage accepts the capsule, which is what keeps two nearby senders from taking
        // each other's capsules.
        private Building_TubeSender assignedSender;

        // Set by the sending station at launch; the receiving station releases the capsule
        // once the game reaches this tick.
        public int arriveTick;

        public TubeCapsule()
        {
            innerContainer = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

        public bool Delivered
        {
            get { return def == TubeDefOf.FL_CapsuleDelivered; }
        }

        public ThingOwner Contents
        {
            get { return innerContainer; }
        }

        public Building_TubeSender AssignedSender
        {
            get
            {
                // A station that was destroyed or uninstalled leaves its capsules unassigned.
                if (assignedSender != null && (assignedSender.Destroyed || !assignedSender.Spawned))
                    assignedSender = null;
                return assignedSender;
            }
            set
            {
                assignedSender = value;
                // Which storage is right for this capsule has just changed, so have the
                // haulers look at it again now and not on their next slow pass.
                if (Spawned)
                    Map.listerHaulables.RecalcAllInCell(Position);
            }
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public override void PostMake()
        {
            base.PostMake();
            if (def == TubeDefOf.FL_Capsule)
                PackContext.NoteMade(this);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_References.Look(ref assignedSender, "assignedSender");
            Scribe_Values.Look(ref arriveTick, "arriveTick", 0);
        }

        // The contents keep ticking, so food packed into a capsule still rots. A capsule is a
        // way to move things, not a freezer.
        protected override void Tick()
        {
            base.Tick();
            innerContainer.DoTick();
        }

        /// <summary>How many units inside pass the filter. Used by the pack bill's counter.</summary>
        public int CountMatching(ThingFilter filter)
        {
            int count = 0;
            for (int i = 0; i < innerContainer.Count; i++)
            {
                Thing thing = innerContainer[i];
                if (filter == null || filter.Allows(thing))
                    count += thing.stackCount;
            }
            return count;
        }

        public string ContentsLabel
        {
            get
            {
                if (innerContainer.Count == 0)
                    return "FactoryLogistics.Empty".Translate();
                string first = innerContainer[0].Label;
                if (innerContainer.Count == 1)
                    return first;
                return "FactoryLogistics.ContentsAndMore".Translate(first, innerContainer.Count - 1);
            }
        }

        public override string LabelNoCount
        {
            get
            {
                string label = base.LabelNoCount;
                if (innerContainer.Count > 0)
                    label += " (" + ContentsLabel + ")";
                Building_TubeSender sender = AssignedSender;
                if (!Delivered && sender != null)
                    label += " -> " + sender.StationName;
                return label;
            }
        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            if (!text.NullOrEmpty())
                text += "\n";
            text += "FactoryLogistics.CapsuleContents".Translate(ContentsLabel);
            if (!Delivered)
            {
                Building_TubeSender sender = AssignedSender;
                text += "\n" + (sender != null
                    ? "FactoryLogistics.CapsuleAssigned".Translate(sender.RouteLabel)
                    : "FactoryLogistics.CapsuleUnassigned".Translate());
            }
            return text;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;

            if (!Spawned)
                yield break;

            Command_Action assign = new Command_Action();
            assign.icon = TubeTex.AssignSender;
            assign.defaultLabel = Delivered ? "FactoryLogistics.SendOnward".Translate() : "FactoryLogistics.AssignSender".Translate();
            assign.defaultDesc = Delivered ? "FactoryLogistics.SendOnwardDesc".Translate() : "FactoryLogistics.AssignSenderDesc".Translate();
            assign.action = delegate { OpenAssignMenu(); };
            yield return assign;

            if (!Delivered)
            {
                Command_Action unpack = new Command_Action();
                unpack.icon = TubeTex.MarkForUnpacking;
                unpack.defaultLabel = "FactoryLogistics.MarkForUnpacking".Translate();
                unpack.defaultDesc = "FactoryLogistics.MarkForUnpackingDesc".Translate();
                unpack.action = delegate
                {
                    foreach (TubeCapsule capsule in SelectedLike(this))
                        capsule.ConvertTo(TubeDefOf.FL_CapsuleDelivered, null);
                };
                yield return unpack;
            }
        }

        // The menu applies to every selected capsule in the same state as this one, so a
        // stockpile's worth can be pointed at a station in one go.
        private static List<TubeCapsule> SelectedLike(TubeCapsule clicked)
        {
            List<TubeCapsule> result = new List<TubeCapsule>();
            foreach (object selected in Find.Selector.SelectedObjectsListForReading)
            {
                TubeCapsule capsule = selected as TubeCapsule;
                if (capsule != null && capsule.Spawned && capsule.Delivered == clicked.Delivered)
                    result.Add(capsule);
            }
            if (!result.Contains(clicked))
                result.Add(clicked);
            return result;
        }

        private void OpenAssignMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            TubeCapsule clicked = this;

            if (!Delivered)
            {
                options.Add(new FloatMenuOption("FactoryLogistics.AssignNone".Translate(), delegate
                {
                    foreach (TubeCapsule capsule in SelectedLike(clicked))
                        capsule.AssignedSender = null;
                }));
            }

            foreach (Building_TubeSender sender in TubeUtility.Senders(Map).OrderBy(s => s.StationName))
            {
                Building_TubeSender target = sender;
                string label = target.RouteLabel;
                string blocked = target.BlockReasonKey(false);
                if (blocked != null)
                    label += " (" + blocked.Translate() + ")";
                options.Add(new FloatMenuOption(label, delegate
                {
                    foreach (TubeCapsule capsule in SelectedLike(clicked))
                        capsule.ConvertTo(TubeDefOf.FL_Capsule, target);
                }));
            }

            if (options.Count == 0 || (options.Count == 1 && !Delivered))
                options.Add(new FloatMenuOption("FactoryLogistics.NoSenders".Translate(), null));

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Turns this capsule into one of the other def (or just restamps it, if it already is
        /// that def) and returns the capsule that now exists. Contents, damage, position,
        /// forbidden state and selection carry over.
        /// </summary>
        public TubeCapsule ConvertTo(ThingDef newDef, Building_TubeSender sender)
        {
            if (def == newDef)
            {
                AssignedSender = newDef == TubeDefOf.FL_Capsule ? sender : null;
                return this;
            }

            TubeCapsule fresh = (TubeCapsule)ThingMaker.MakeThing(newDef);
            fresh.HitPoints = Mathf.Clamp(HitPoints, 1, fresh.MaxHitPoints);
            innerContainer.TryTransferAllToContainer(fresh.innerContainer);
            fresh.assignedSender = newDef == TubeDefOf.FL_Capsule ? sender : null;
            fresh.arriveTick = arriveTick;

            bool wasSpawned = Spawned;
            Map map = wasSpawned ? Map : null;
            IntVec3 position = wasSpawned ? Position : IntVec3.Invalid;
            bool forbidden = wasSpawned && this.IsForbidden(Faction.OfPlayer);
            bool selected = wasSpawned && Find.Selector.IsSelected(this);

            // Empty by now, so this cannot spill or destroy anything.
            Destroy(DestroyMode.Vanish);

            if (wasSpawned)
            {
                if (!GenPlace.TryPlaceThing(fresh, position, map, ThingPlaceMode.Direct))
                    GenPlace.TryPlaceThing(fresh, position, map, ThingPlaceMode.Near);
                if (fresh.Spawned)
                {
                    fresh.SetForbidden(forbidden, false);
                    if (selected)
                        Find.Selector.Select(fresh, false, false);
                }
            }
            return fresh;
        }

        // A capsule that burns, deteriorates away or is shot apart spills what was in it.
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (innerContainer != null && innerContainer.Count > 0)
            {
                Map map = MapHeld;
                if (mode != DestroyMode.Vanish && map != null)
                    innerContainer.TryDropAll(PositionHeld, map, ThingPlaceMode.Near);
                else
                    innerContainer.ClearAndDestroyContents();
            }
            base.Destroy(mode);
        }
    }
}
