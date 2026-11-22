using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using VanillaFurnitureExpandedFactory;

namespace FactoryLogistics.Belts
{
    public enum ArmMode : byte
    {
        Loader,     // storage at its gripper -> belt behind it
        Feeder,     // end of a belt at its gripper -> machine input behind it
        Mover,      // anything at its gripper (its filter allows) -> behind it, no questions asked
    }

    // One building, two jobs, chosen with a gizmo. Its gripper reaches into the cell it takes
    // from (the cell it faces) and it puts down on the cell behind it, so its rotation is its
    // direction. (Build 0.9.9 and earlier had it the other way round; players point the gripper
    // at what it grabs, so that is how it works now.)
    //
    // It is deliberately not an edifice and not a storage building, which is what lets it sit on
    // both ends of a VFE Factory belt with no patch to VFE:
    //   - a Loader hands its item straight to the conveyor in front of it;
    //   - a conveyor whose forward cell is walkable and holds no edifice drops its item onto that
    //     cell, so a belt pointing at a Feeder drops each item onto the Feeder, one at a time, and
    //     waits while the Feeder still holds one - the belt's own back-pressure.
    // What a Loader sends, and what a Feeder does with what arrives, is decided by the map's
    // LogisticsManager from what the Feeder's machine is asking for.
    [StaticConstructorOnStartup]
    public static class ArmTex
    {
        // The swinging arm, drawn every frame over the floor-mounted base: open while it reaches,
        // closed while it carries. Pivot at the middle of the texture, pointing up (north).
        public static readonly Material LoaderOpen = MaterialPool.MatFrom("Things/Building/FactoryLogistics/ArmLoader_Arm", ShaderDatabase.Cutout);
        public static readonly Material LoaderClosed = MaterialPool.MatFrom("Things/Building/FactoryLogistics/ArmLoader_ArmClosed", ShaderDatabase.Cutout);
        public static readonly Material FeederOpen = MaterialPool.MatFrom("Things/Building/FactoryLogistics/ArmFeeder_Arm", ShaderDatabase.Cutout);
        public static readonly Material FeederClosed = MaterialPool.MatFrom("Things/Building/FactoryLogistics/ArmFeeder_ArmClosed", ShaderDatabase.Cutout);
        public static readonly Material MoverOpen = MaterialPool.MatFrom("Things/Building/FactoryLogistics/ArmMover_Arm", ShaderDatabase.Cutout);
        public static readonly Material MoverClosed = MaterialPool.MatFrom("Things/Building/FactoryLogistics/ArmMover_ArmClosed", ShaderDatabase.Cutout);

        // The output tray, drawn on whichever side the arm puts down on (it can be any side but
        // the gripper's). Drawn with the tray on the south edge.
        public static readonly Material LoaderTray = MaterialPool.MatFrom("Things/Building/FactoryLogistics/ArmLoader_Tray", ShaderDatabase.Cutout);
        public static readonly Material FeederTray = MaterialPool.MatFrom("Things/Building/FactoryLogistics/ArmFeeder_Tray", ShaderDatabase.Cutout);
        public static readonly Material MoverTray = MaterialPool.MatFrom("Things/Building/FactoryLogistics/ArmMover_Tray", ShaderDatabase.Cutout);
    }

    public class Building_LogisticsArm : Building, IThingHolder
    {
        // The swing: reach into the source cell, close, turn half round to the output cell
        // carrying the item, set it down, turn back. The item is really in the arm meanwhile (and
        // counted as on its way), so what you see is what is happening.
        // A return (a Loader putting a leftover back on its shelf) runs the other way: Reach turns
        // empty-handed to the belt behind it, then Out carries the item round to the shelf.
        private enum Swing : byte { Idle, Out, Back, Reach }

        private const int HalfSwingTicks = 25;
        private ThingOwner<Thing> carried;
        private Swing swing = Swing.Idle;
        private int swingStart;
        private bool returning;
        private bool returnLeftovers = true;
        private ArmMode mode = ArmMode.Loader;
        private int priority = 2;
        // How often it looks for work. An arm fed by a belt looks every half second, which in play
        // catches everything going past; one fed by storage, whose stock changes slowly, looks
        // less often - and less often still once it has found nothing a few times running, until
        // it moves something again.
        private const int BeltInterval = 30;
        private const int StorageInterval = 90;
        private const int IdleInterval = 250;
        private const int IdleChecksBeforeSlowing = 4;
        private const int FeedCheckInterval = 250;

        private int nextWork;
        private int idleChecks;
        private bool beltFed;
        private int beltFedCheckedAt = -99999;

        // Status for the inspect pane, set by the last work step.
        public string status;

        private CompPowerTrader power;
        private Graphic feederGraphic;
        private Graphic moverGraphic;

        // A Mover's filter: what it will move. Allows everything until the player narrows it.
        private ThingFilter moveFilter;

        public ThingFilter MoveFilter
        {
            get
            {
                if (moveFilter == null)
                {
                    moveFilter = new ThingFilter();
                    moveFilter.SetAllow(ThingCategoryDefOf.Root, true);
                }
                return moveFilter;
            }
        }

        public Building_LogisticsArm()
        {
            carried = new ThingOwner<Thing>(this, true, LookMode.Deep);
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return carried;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        /// <summary>Busy with a swing; it takes nothing new until it is back.</summary>
        public bool Busy
        {
            get { return swing != Swing.Idle || carried.Count > 0; }
        }

        /// <summary>The item in its gripper, if any.</summary>
        public Thing Carried
        {
            get { return carried.Count > 0 ? carried[0] : null; }
        }

        /// <summary>Whether a Loader puts leftovers no machine needs back into its storage.</summary>
        public bool ReturnLeftovers
        {
            get { return returnLeftovers; }
        }

        /// <summary>True while carrying an item back to its storage rather than out to the belt.</summary>
        public bool Returning
        {
            get { return returning; }
        }

        /// <summary>Lifts a leftover off the belt behind it and swings it back to its storage.</summary>
        public void PickReturn(Thing thing)
        {
            Pick(thing);
            if (carried.Count == 0)
                return;
            returning = true;
            swing = Swing.Reach;
        }

        /// <summary>Takes a despawned item into the gripper and starts the swing to the output side.</summary>
        public void Pick(Thing thing)
        {
            returning = false;
            if (thing.Spawned)
                thing.DeSpawn();
            else if (thing.holdingOwner != null)
                thing.holdingOwner.Remove(thing);      // out of a storage building that holds it inside
            if (!carried.TryAdd(thing, true))
            {
                GenPlace.TryPlaceThing(thing, Position, Map, ThingPlaceMode.Near);
                return;
            }
            swing = Swing.Out;
            swingStart = Find.TickManager.TicksGame;
        }

        /// <summary>Puts what the gripper holds on the floor beside the arm, off the line, and ends the swing.</summary>
        public void DropCarried()
        {
            if (!Spawned || carried.Count == 0)
                return;
            Map map = Map;
            IntVec3 self = Position, src = SourceCell, outp = OutputCell;
            foreach (Thing thing in carried.ToList())
            {
                carried.Remove(thing);
                bool placed = GenPlace.TryPlaceThing(thing, self, map, ThingPlaceMode.Near, null, delegate (IntVec3 c)
                {
                    if (c == self || c == src || c == outp || !c.Standable(map))
                        return false;
                    List<Thing> here = c.GetThingList(map);
                    for (int i = 0; i < here.Count; i++)
                    {
                        if (here[i] is Building_Conveyor || here[i] is Building_LogisticsArm || here[i] is Building_ConveyorLift || here[i] is Building_BeltJunction)
                            return false;
                    }
                    return true;
                });
                if (!placed && !thing.Destroyed && !thing.Spawned)
                    GenPlace.TryPlaceThing(thing, self, map, ThingPlaceMode.Near);
            }
            returning = false;
            swing = Swing.Back;
            swingStart = Find.TickManager.TicksGame;
            status = null;
        }

        public ArmMode Mode
        {
            get { return mode; }
        }

        /// <summary>A Feeder's claim on a junction's trunk: 1 low, 2 normal, 3 high.</summary>
        public int Priority
        {
            get { return priority; }
        }

        /// <summary>Where it takes from: the cell its gripper faces.</summary>
        public IntVec3 SourceCell
        {
            get { return Position + Rotation.FacingCell; }
        }

        /// <summary>Where it puts down: the cell behind it.</summary>
        public IntVec3 OutputCell
        {
            get { return Position + OutputRot.FacingCell; }
        }

        // The side it puts down on. -1 (not set yet) is straight across from the gripper, which is
        // also every arm built before the destination could be turned on its own.
        private int outputRot = -1;

        public Rot4 OutputRot
        {
            get
            {
                if (outputRot < 0 || outputRot > 3 || outputRot == Rotation.AsInt)
                    return Rotation.Opposite;
                return new Rot4(outputRot);
            }
        }

        /// <summary>Turns the gripper (the source side) a quarter turn clockwise, past the destination side.</summary>
        public void RotateSource()
        {
            Rot4 output = OutputRot;
            outputRot = output.AsInt;
            Rot4 next = Rotation.Rotated(RotationDirection.Clockwise);
            if (next == output)
                next = next.Rotated(RotationDirection.Clockwise);
            Turn(next);
        }

        /// <summary>Turns the destination side a quarter turn clockwise, past the gripper.</summary>
        public void RotateDestination()
        {
            Rot4 next = OutputRot.Rotated(RotationDirection.Clockwise);
            if (next == Rotation)
                next = next.Rotated(RotationDirection.Clockwise);
            outputRot = next.AsInt;
            status = null;
            Wake();
            if (Spawned)
            {
                LogisticsManager manager = LogisticsManager.For(Map);
                if (manager != null)
                    manager.MarkDirty();
            }
        }

        public bool PowerOn
        {
            get { return power == null || power.PowerOn; }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            power = GetComp<CompPowerTrader>();
            LogisticsManager manager = LogisticsManager.For(map);
            if (manager != null)
                manager.MarkDirty();
        }

        public override void DeSpawn(DestroyMode destroyMode = DestroyMode.Vanish)
        {
            Map map = Map;
            if (carried.Count > 0 && map != null)
                carried.TryDropAll(Position, map, ThingPlaceMode.Near);
            swing = Swing.Idle;
            base.DeSpawn(destroyMode);
            LogisticsManager manager = LogisticsManager.For(map);
            if (manager != null)
                manager.MarkDirty();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref mode, "mode", ArmMode.Loader);
            Scribe_Values.Look(ref priority, "priority", 2);
            Scribe_Deep.Look(ref carried, "carried", this);
            Scribe_Values.Look(ref swing, "swing", Swing.Idle);
            Scribe_Values.Look(ref swingStart, "swingStart", 0);
            Scribe_Values.Look(ref returning, "returning", false);
            Scribe_Values.Look(ref returnLeftovers, "returnLeftovers", true);
            Scribe_Deep.Look(ref moveFilter, "moveFilter");
            Scribe_Values.Look(ref outputRot, "outputRot", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && carried == null)
                carried = new ThingOwner<Thing>(this, true, LookMode.Deep);
        }

        // ------------------------------------------------------------------ graphics
        //
        // The two modes look different, so a glance tells them apart. The def's texPath names the
        // Loader's art; the Feeder's is the same path with "Loader" swapped for "Feeder".
        public override Graphic Graphic
        {
            get
            {
                if (mode == ArmMode.Loader)
                    return base.Graphic;

                GraphicData data = def.graphicData;
                if (mode == ArmMode.Mover)
                {
                    if (moverGraphic == null)
                        moverGraphic = GraphicDatabase.Get<Graphic_Multi>(
                            data.texPath.Replace("Loader", "Mover"), data.shaderType.Shader, data.drawSize, DrawColor);
                    return moverGraphic;
                }

                if (feederGraphic == null)
                {
                    feederGraphic = GraphicDatabase.Get<Graphic_Multi>(
                        data.texPath.Replace("Loader", "Feeder"), data.shaderType.Shader, data.drawSize, DrawColor);
                }

                return feederGraphic;
            }
        }

        public void SetMode(ArmMode next)
        {
            if (next == mode)
                return;

            mode = next;
            status = null;
            Wake();
            returning = false;      // a half-done return would otherwise try to go the old way
            if (Spawned)
            {
                DirtyMapMesh(Map);
                LogisticsManager manager = LogisticsManager.For(Map);
                if (manager != null)
                    manager.MarkDirty();
            }
        }

        public void Turn(Rot4 facing)
        {
            if (!Spawned || facing == Rotation)
                return;
            Map map = Map;
            Rotation = facing;
            status = null;
            Wake();
            DirtyMapMesh(map);
            LogisticsManager manager = LogisticsManager.For(map);
            if (manager != null)
                manager.MarkDirty();
        }

        // ------------------------------------------------------------------ work

        protected override void Tick()
        {
            base.Tick();
            carried.DoTick();

            if (Spawned && swing != Swing.Idle)
            {
                TickSwing();
                return;
            }

            if (!Spawned)
                return;
            int now = Find.TickManager.TicksGame;
            if (now < nextWork)
                return;
            nextWork = now + NextInterval(now);

            if (!PowerOn)
            {
                status = "FactoryLogistics.ArmNoPower".Translate();
                return;
            }

            LogisticsManager manager = LogisticsManager.For(Map);
            if (manager == null)
                return;

            if (mode == ArmMode.Loader)
                manager.WorkLoader(this);
            else if (mode == ArmMode.Feeder)
                manager.WorkFeeder(this);
            else
                manager.WorkMover(this);

            // Picked something up: stay quick. Found nothing: count towards slowing down.
            if (Busy)
                idleChecks = 0;
            else if (idleChecks < IdleChecksBeforeSlowing)
                idleChecks++;
        }

        private int NextInterval(int now)
        {
            if (now - beltFedCheckedAt >= FeedCheckInterval || now < beltFedCheckedAt)
            {
                beltFedCheckedAt = now;
                beltFed = SourceConveyor != null || BeltEndsHere();
            }
            if (beltFed)
                return BeltInterval;
            return idleChecks >= IdleChecksBeforeSlowing ? IdleInterval : StorageInterval;
        }

        // A belt whose end points at this arm drops its items onto it.
        private bool BeltEndsHere()
        {
            for (int i = 0; i < 4; i++)
            {
                Building_Conveyor belt = LogisticsManager.ConveyorAt(Position + GenAdj.CardinalDirections[i], Map);
                if (belt != null && belt.ForwardCell == Position)
                    return true;
            }
            return false;
        }

        /// <summary>Look again soon: something about it changed (turned, mode switched).</summary>
        public void Wake()
        {
            nextWork = 0;
            idleChecks = 0;
            beltFedCheckedAt = -99999;
        }

        private void TickSwing()
        {
            if (!PowerOn)
                return;     // frozen mid-swing until power is back
            int now = Find.TickManager.TicksGame;
            if (swing == Swing.Reach)
            {
                if (now - swingStart >= HalfSwingTicks)
                {
                    swing = Swing.Out;
                    swingStart = now;
                }
                return;
            }
            if (swing == Swing.Out)
            {
                if (now - swingStart < HalfSwingTicks)
                    return;
                Thing thing = Carried;
                if (thing != null)
                {
                    // At the output side: set it down, or hold it there until there is room.
                    if (!this.IsHashIntervalTick(10) && now - swingStart > HalfSwingTicks)
                        return;
                    LogisticsManager manager = LogisticsManager.For(Map);
                    carried.Remove(thing);
                    if (manager == null || !manager.Release(this, thing))
                    {
                        if (!thing.Destroyed && !thing.Spawned && thing.holdingOwner == null)
                            carried.TryAdd(thing, true);
                        // A return that cannot be put back within about ten seconds is sent on to
                        // the output side instead, so the arm never waits on it forever.
                        if (returning && now - swingStart > 600)
                            returning = false;
                        status = "FactoryLogistics.ArmHolding".Translate(thing.LabelShort);
                        return;
                    }
                }
                if (returning)
                {
                    // Already facing its storage again: the swing ends here.
                    returning = false;
                    swing = Swing.Idle;
                    return;
                }
                swing = Swing.Back;
                swingStart = now;
            }
            else if (swing == Swing.Back && now - swingStart >= HalfSwingTicks)
            {
                swing = Swing.Idle;
            }
        }

        // How far round the arm is from the source side: 0 reaching into the source cell, 180 at
        // the output cell. Turns clockwise out and carries on round back, easing at both ends.
        private float SwingAngle
        {
            get
            {
                if (swing == Swing.Idle)
                    return 0f;
                float t = Mathf.Clamp01((Find.TickManager.TicksGame - swingStart) / (float)HalfSwingTicks);
                float eased = t * t * (3f - 2f * t);
                // How far round, clockwise, the destination side is from the gripper: 90, 180 or 270.
                float span = (OutputRot.AsInt - Rotation.AsInt + 4) % 4 * 90f;
                if (span <= 0f)
                    span = 180f;
                if (returning)
                    return swing == Swing.Reach ? span * eased : span + (360f - span) * eased;
                return swing == Swing.Out ? span * eased : span + (360f - span) * eased;
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);

            // The tray on the destination side, over the base and under the arm.
            Vector3 trayPos = drawLoc;
            trayPos.y = AltitudeLayer.BuildingOnTop.AltitudeFor() + 0.005f;
            Material tray = mode == ArmMode.Loader ? ArmTex.LoaderTray : mode == ArmMode.Feeder ? ArmTex.FeederTray : ArmTex.MoverTray;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(trayPos, Quaternion.AngleAxis(OutputRot.AsAngle - 180f, Vector3.up), Vector3.one), tray, 0);

            float angle = Rotation.AsAngle + SwingAngle;
            Quaternion turn = Quaternion.AngleAxis(angle, Vector3.up);
            Vector3 pivot = drawLoc;
            pivot.y = AltitudeLayer.BuildingOnTop.AltitudeFor() + 0.02f;

            // While reaching for a leftover the item is already lifted off the belt, but the arm is
            // not there yet: it is shown in the gripper only once the arm has it.
            Thing thing = swing == Swing.Reach ? null : Carried;
            if (thing != null)
            {
                Vector3 tip = pivot + turn * new Vector3(0f, 0f, 0.42f);
                tip.y = pivot.y - 0.01f;
                Material itemMat = thing.Graphic.MatAt(Rot4.North, thing);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(tip, Quaternion.identity, new Vector3(0.55f, 1f, 0.55f)), itemMat, 0);
            }

            bool closed = thing != null;
            Material armMat = mode == ArmMode.Loader
                ? (closed ? ArmTex.LoaderClosed : ArmTex.LoaderOpen)
                : mode == ArmMode.Feeder
                    ? (closed ? ArmTex.FeederClosed : ArmTex.FeederOpen)
                    : (closed ? ArmTex.MoverClosed : ArmTex.MoverOpen);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pivot, turn, Vector3.one), armMat, 0);
        }

        // What a Loader draws from: a belt at its gripper (what it carries as it passes), or the
        // whole storage its gripper cell belongs to - a stockpile zone or a storage building - or,
        // if that cell is neither, whatever lies on it.
        public IEnumerable<Thing> SourceThings()
        {
            Map map = Map;
            if (map == null || !SourceCell.InBounds(map))
                yield break;

            // A belt at the gripper: what it is carrying, as it passes.
            Building_Conveyor belt = LogisticsManager.ConveyorAt(SourceCell, map);
            if (belt != null)
            {
                if (belt.innerContainer != null)
                {
                    foreach (Thing thing in belt.innerContainer.InnerListForReading.ToArray())
                        yield return thing;
                }
                yield break;
            }

            // A storage building that keeps its items inside itself (a crate, a deep-storage unit,
            // anything with a "contained items" list) rather than on its cells.
            List<Thing> here = SourceCell.GetThingList(map);
            for (int i = 0; i < here.Count; i++)
            {
                Building building = here[i] as Building;
                if (building == null || building is Building_LogisticsArm || building is Building_ConveyorLift || building is Building_BeltJunction)
                    continue;
                IThingHolder holder = building as IThingHolder;
                ThingOwner inside = holder != null ? holder.GetDirectlyHeldThings() : null;
                if (inside == null)
                    continue;
                foreach (Thing thing in inside.ToList())
                {
                    if (thing.def.category == ThingCategory.Item)
                        yield return thing;
                }
            }

            SlotGroup group = SourceCell.GetSlotGroup(map);
            if (group != null)
            {
                foreach (SlotGroup g in StorageAround(group))
                {
                    foreach (Thing thing in g.HeldThings)
                        yield return thing;
                    // The rest of the row may keep its items inside itself too (the building at
                    // the gripper was already listed above).
                    Thing parentThing = g.parent as Thing;
                    IThingHolder holder = g.parent as IThingHolder;
                    if (g != group && holder != null && parentThing != null && parentThing.Position != SourceCell
                        && !parentThing.OccupiedRect().Contains(SourceCell))
                    {
                        ThingOwner inside = holder.GetDirectlyHeldThings();
                        if (inside != null)
                        {
                            foreach (Thing thing in inside.ToList())
                            {
                                if (thing.def.category == ThingCategory.Item && !thing.Spawned)
                                    yield return thing;
                            }
                        }
                    }
                }

                yield break;
            }

            List<Thing> things = SourceCell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (things[i].def.category == ThingCategory.Item)
                    yield return things[i];
            }
        }

        // The whole storage the gripper reaches into, as a player sees it: a stockpile zone, or a
        // shelf - and with a shelf, every storage building linked to it (Link settings) and every
        // one of the same kind joined to it in a row, since a row of shelves or crates is really
        // several buildings each with its own storage.
        private const int MaxStorageBuildings = 40;

        private IEnumerable<SlotGroup> StorageAround(SlotGroup start)
        {
            yield return start;
            Building_Storage first = start.parent as Building_Storage;
            if (first == null)
                yield break;

            HashSet<Building_Storage> seen = new HashSet<Building_Storage> { first };
            Queue<Building_Storage> open = new Queue<Building_Storage>();
            open.Enqueue(first);

            IStorageGroupMember member = first as IStorageGroupMember;
            if (member != null && member.Group != null)
            {
                foreach (IStorageGroupMember other in member.Group.members)
                {
                    Building_Storage b = other as Building_Storage;
                    if (b != null && b.Spawned && b.Map == Map && seen.Add(b))
                        open.Enqueue(b);
                }
            }

            while (open.Count > 0 && seen.Count <= MaxStorageBuildings)
            {
                Building_Storage b = open.Dequeue();
                if (b != first && b.GetSlotGroup() != null)
                    yield return b.GetSlotGroup();
                foreach (IntVec3 c in GenAdj.CellsAdjacentCardinal(b))
                {
                    if (!c.InBounds(Map))
                        continue;
                    Building_Storage next = c.GetEdifice(Map) as Building_Storage;
                    if (next != null && next.def == first.def && seen.Add(next))
                        open.Enqueue(next);
                }
            }
        }

        /// <summary>Whether the arm may take this item from its source right now.</summary>
        public bool CanTakeFromSource(Thing thing)
        {
            if (thing == null || thing.Destroyed)
                return false;
            Building_Conveyor belt = SourceConveyor;
            if (belt != null && thing.holdingOwner == belt.innerContainer)
                return true;
            if (thing.IsForbidden(Faction.OfPlayer))
                return false;
            if (thing.Spawned)
                return !Map.reservationManager.IsReservedByAnyoneOf(thing, Faction.OfPlayer);
            // Held inside a storage building at the gripper.
            return thing.holdingOwner != null;
        }

        // The belt at the gripper: what a Feeder takes from as it passes (or ends here).
        public Building_Conveyor SourceConveyor
        {
            get { return Spawned ? LogisticsManager.ConveyorAt(SourceCell, Map) : null; }
        }

        // The conveyor a Loader puts onto, or a Feeder's machine input conveyor.
        public Building_Conveyor OutputConveyor
        {
            get { return Spawned ? LogisticsManager.ConveyorAt(OutputCell, Map) : null; }
        }

        // ------------------------------------------------------------------ UI

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;

            // A plain toggle rather than a menu, so it works with several arms selected.
            Command_Action toggle = new Command_Action();
            toggle.defaultLabel = ("FactoryLogistics.Mode_" + mode).Translate();
            toggle.defaultDesc = "FactoryLogistics.ModeDesc".Translate();
            toggle.icon = ContentFinder<Texture2D>.Get("UI/Commands/FactoryLogistics/Arm" + mode);
            toggle.action = delegate
            {
                SetMode(mode == ArmMode.Loader ? ArmMode.Feeder : mode == ArmMode.Feeder ? ArmMode.Mover : ArmMode.Loader);
            };
            yield return toggle;

            // Turn it round where it stands: takes from the other side, puts down on this one.
            // Whatever it is holding - waiting for room that is not coming, say - goes on the floor
            // beside it, off the line, and the arm swings home.
            if (Carried != null)
            {
                Command_Action drop = new Command_Action();
                drop.defaultLabel = "FactoryLogistics.ArmDrop".Translate();
                drop.defaultDesc = "FactoryLogistics.ArmDropDesc".Translate();
                drop.icon = ContentFinder<Texture2D>.Get("UI/Commands/FactoryLogistics/BeltDrop");
                drop.action = DropCarried;
                yield return drop;
            }

            if (mode == ArmMode.Mover)
            {
                Command_Action filter = new Command_Action();
                filter.defaultLabel = "FactoryLogistics.MoveFilter".Translate();
                filter.defaultDesc = "FactoryLogistics.MoveFilterDesc".Translate();
                filter.icon = ContentFinder<Texture2D>.Get("UI/Commands/FactoryLogistics/MoveFilter");
                filter.action = delegate { Find.WindowStack.Add(new Dialog_ArmFilter(this)); };
                yield return filter;
            }

            // Source (gripper) and destination sides turn separately, so an arm can pass things
            // straight across or round a corner.
            Command_Action rotSource = new Command_Action();
            rotSource.defaultLabel = "FactoryLogistics.ArmRotateSource".Translate();
            rotSource.defaultDesc = "FactoryLogistics.ArmRotateSourceDesc".Translate();
            rotSource.icon = ContentFinder<Texture2D>.Get("UI/Widgets/RotRight");
            rotSource.action = RotateSource;
            yield return rotSource;

            Command_Action rotDest = new Command_Action();
            rotDest.defaultLabel = "FactoryLogistics.ArmRotateDestination".Translate();
            rotDest.defaultDesc = "FactoryLogistics.ArmRotateDestinationDesc".Translate();
            rotDest.icon = ContentFinder<Texture2D>.Get("UI/Widgets/RotLeft");
            rotDest.action = RotateDestination;
            yield return rotDest;

            if (mode == ArmMode.Loader)
            {
                Command_Toggle leftovers = new Command_Toggle();
                leftovers.defaultLabel = "FactoryLogistics.ReturnLeftovers".Translate();
                leftovers.defaultDesc = "FactoryLogistics.ReturnLeftoversDesc".Translate();
                leftovers.icon = ContentFinder<Texture2D>.Get("UI/Commands/FactoryLogistics/ReturnLeftovers");
                leftovers.isActive = delegate { return returnLeftovers; };
                leftovers.toggleAction = delegate { returnLeftovers = !returnLeftovers; };
                yield return leftovers;
            }

            if (mode == ArmMode.Feeder)
            {
                Command_Action prio = new Command_Action();
                prio.defaultLabel = "FactoryLogistics.FeederPriority".Translate(("FactoryLogistics.Priority_" + priority).Translate());
                prio.defaultDesc = "FactoryLogistics.FeederPriorityDesc".Translate();
                prio.icon = ContentFinder<Texture2D>.Get("UI/Commands/FactoryLogistics/FeederPriority");
                prio.action = delegate { priority = priority >= 3 ? 1 : priority + 1; };
                yield return prio;
            }
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());

            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(("FactoryLogistics.Mode_" + mode).Translate());

            LogisticsManager manager = Spawned ? LogisticsManager.For(Map) : null;
            string network = manager != null ? manager.DescribeFor(this) : null;
            if (!network.NullOrEmpty())
                sb.AppendLine().Append(network);

            if (!status.NullOrEmpty())
                sb.AppendLine().Append(status);

            return sb.ToString().TrimEndNewlines();
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();

            // Where it takes from and where it puts, so its direction is never a guess.
            GenDraw.DrawFieldEdges(new List<IntVec3> { SourceCell }, Color.cyan);
            GenDraw.DrawFieldEdges(new List<IntVec3> { OutputCell }, new Color(1f, 0.6f, 0.2f));

            LogisticsManager manager = Spawned ? LogisticsManager.For(Map) : null;
            Thing machine = manager != null ? manager.MachineFor(this) : null;
            if (machine != null)
                GenDraw.DrawLineBetween(DrawPos, machine.DrawPos, SimpleColor.Orange);
        }
    }
}

namespace FactoryLogistics.Belts
{
    public class Dialog_ArmFilter : Window
    {
        private readonly Building_LogisticsArm arm;
        private readonly ThingFilterUI.UIState state = new ThingFilterUI.UIState();

        public override Vector2 InitialSize
        {
            get { return new Vector2(420f, 620f); }
        }

        public Dialog_ArmFilter(Building_LogisticsArm arm)
        {
            this.arm = arm;
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "FactoryLogistics.MoveFilter".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 36f, inRect.width, 44f), "FactoryLogistics.MoveFilterHint".Translate());
            Rect list = new Rect(0f, 84f, inRect.width, inRect.height - 84f - CloseButSize.y - 12f);
            ThingFilterUI.DoThingFilterConfigWindow(list, state, arm.MoveFilter, null, 1, map: arm.Map);
        }
    }
}
