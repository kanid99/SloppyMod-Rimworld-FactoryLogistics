using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using VanillaFurnitureExpandedFactory;

namespace FactoryLogistics.Belts
{
    /// <summary>
    /// Carries items between two lifts: a belt drops items onto one, and its partner puts them
    /// onto the conveyor in front of it. Like the arm it is not an edifice, so a VFE belt that
    /// points at it drops its item onto it and waits while it is still holding one.
    ///
    /// Partners: with "As above, So below", a lift pairs by itself with the lift standing on the
    /// same spot one level up (or, failing that, one level down) - a conveyor elevator. A lift
    /// can also be linked by hand to any other lift on the map, which overrides the automatic
    /// pairing and works without that mod as a long-distance belt link.
    ///
    /// The logistics network follows belts through lifts, so a Loader on one level can serve a
    /// Feeder on another.
    /// </summary>
    public class Building_ConveyorLift : Building
    {
        private const int CycleTicks = 60;
        private const int CheckInterval = 10;

        private Building_ConveyorLift manualLink;
        private int nextCycle;
        private CompPowerTrader power;
        public string status;

        public IntVec3 FrontCell
        {
            get { return Position + Rotation.FacingCell; }
        }

        public bool PowerOn
        {
            get { return power == null || power.PowerOn; }
        }

        public Building_ConveyorLift ManualLink
        {
            get
            {
                if (manualLink != null && (manualLink.Destroyed || !manualLink.Spawned || manualLink.Map != Map))
                    manualLink = null;
                return manualLink;
            }
        }

        /// <summary>Where this lift sends what is dropped on it: the hand-made link, else the lift on the same spot a level up, else a level down.</summary>
        public Building_ConveyorLift Partner
        {
            get
            {
                if (!Spawned)
                    return null;
                Building_ConveyorLift linked = ManualLink;
                if (linked != null)
                    return linked;
                if (!LevelUtility.Banded(Map))
                    return null;
                int band = LevelUtility.BandOf(Position, Map);
                Building_ConveyorLift above = LiftAt(LevelUtility.Translate(Position, band + 1, Map));
                if (above != null)
                    return above;
                return LiftAt(LevelUtility.Translate(Position, band - 1, Map));
            }
        }

        private Building_ConveyorLift LiftAt(IntVec3 cell)
        {
            if (!cell.IsValid || !cell.InBounds(Map))
                return null;
            List<Thing> things = cell.GetThingList(Map);
            for (int i = 0; i < things.Count; i++)
            {
                Building_ConveyorLift lift = things[i] as Building_ConveyorLift;
                if (lift != null && lift != this)
                    return lift;
            }
            return null;
        }

        public Building_Conveyor FrontConveyor
        {
            get { return Spawned ? LogisticsManager.ConveyorAt(FrontCell, Map) : null; }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            power = GetComp<CompPowerTrader>();
            LogisticsManager manager = LogisticsManager.For(map);
            if (manager != null)
                manager.MarkDirty();
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map map = Map;
            base.DeSpawn(mode);
            LogisticsManager manager = LogisticsManager.For(map);
            if (manager != null)
                manager.MarkDirty();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref manualLink, "manualLink");
            Scribe_Values.Look(ref nextCycle, "nextCycle", 0);
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned || !this.IsHashIntervalTick(CheckInterval))
                return;

            Thing item = FirstItemHere();
            if (item == null)
                return;

            if (!PowerOn)
            {
                status = "FactoryLogistics.ArmNoPower".Translate();
                return;
            }

            Building_ConveyorLift partner = Partner;
            if (partner == null)
            {
                status = "FactoryLogistics.LiftNoPartner".Translate();
                return;
            }

            if (Find.TickManager.TicksGame < nextCycle)
                return;

            Building_Conveyor belt = partner.FrontConveyor;
            if (belt == null)
            {
                status = "FactoryLogistics.LiftPartnerNoBelt".Translate();
                return;
            }
            if (!belt.HasSpace() || !belt.CanAcceptItem(item))
            {
                status = "FactoryLogistics.LiftWaiting".Translate();
                return;
            }

            item.DeSpawn();
            if (!belt.TryAdd(item))
            {
                GenSpawn.Spawn(item, Position, Map);
                return;
            }

            nextCycle = Find.TickManager.TicksGame + CycleTicks;
            status = "FactoryLogistics.LiftCarried".Translate(item.LabelCap);
            FleckMaker.ThrowAirPuffUp(partner.DrawPos, Map);
        }

        private Thing FirstItemHere()
        {
            List<Thing> things = Position.GetThingList(Map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def.category == ThingCategory.Item)
                    return things[i];
            }
            return null;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;

            if (Faction != Faction.OfPlayer)
                yield break;

            Command_Action link = new Command_Action();
            link.defaultLabel = "FactoryLogistics.LiftLink".Translate();
            link.defaultDesc = "FactoryLogistics.LiftLinkDesc".Translate();
            link.icon = ContentFinder<Texture2D>.Get("UI/Commands/FactoryLogistics/LiftLink");
            link.action = delegate
            {
                TargetingParameters parms = new TargetingParameters();
                parms.canTargetPawns = false;
                parms.canTargetBuildings = true;
                parms.validator = delegate (TargetInfo t) { return t.Thing is Building_ConveyorLift && t.Thing != this; };
                Find.Targeter.BeginTargeting(parms, delegate (LocalTargetInfo target)
                {
                    manualLink = target.Thing as Building_ConveyorLift;
                    LogisticsManager manager = LogisticsManager.For(Map);
                    if (manager != null)
                        manager.MarkDirty();
                });
            };
            yield return link;

            if (manualLink != null)
            {
                Command_Action unlink = new Command_Action();
                unlink.defaultLabel = "FactoryLogistics.LiftUnlink".Translate();
                unlink.defaultDesc = "FactoryLogistics.LiftUnlinkDesc".Translate();
                unlink.icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");
                unlink.action = delegate
                {
                    manualLink = null;
                    LogisticsManager manager = LogisticsManager.For(Map);
                    if (manager != null)
                        manager.MarkDirty();
                };
                yield return unlink;
            }
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());
            if (Spawned)
            {
                Building_ConveyorLift partner = Partner;
                if (sb.Length > 0)
                    sb.AppendLine();
                if (partner == null)
                    sb.Append("FactoryLogistics.LiftNoPartner".Translate());
                else
                {
                    string where = ManualLink != null ? "FactoryLogistics.LiftLinkedByHand".Translate().ToString()
                        : (LevelUtility.BandOf(partner.Position, Map) > LevelUtility.BandOf(Position, Map)
                            ? "FactoryLogistics.LiftAbove".Translate().ToString()
                            : "FactoryLogistics.LiftBelow".Translate().ToString());
                    sb.Append("FactoryLogistics.LiftPartner".Translate(where));
                }
                if (!status.NullOrEmpty())
                    sb.AppendLine().Append(status);
            }
            return sb.ToString().TrimEndNewlines();
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            GenDraw.DrawFieldEdges(new List<IntVec3> { FrontCell }, new Color(1f, 0.6f, 0.2f));
            Building_ConveyorLift partner = Partner;
            if (partner != null)
            {
                GenDraw.DrawLineBetween(DrawPos, partner.DrawPos, SimpleColor.Cyan);
                GenDraw.DrawFieldEdges(new List<IntVec3> { partner.FrontCell }, new Color(1f, 0.6f, 0.2f));
            }
        }
    }
}
