using System.Collections.Generic;
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
        private const int WorkInterval = 30;

        // Status for the inspect pane, set by the last work step.
        public string status;

        private CompPowerTrader power;
        private Graphic feederGraphic;

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
            if (!carried.TryAdd(thing, true))
            {
                GenPlace.TryPlaceThing(thing, Position, Map, ThingPlaceMode.Near);
                return;
            }
            swing = Swing.Out;
            swingStart = Find.TickManager.TicksGame;
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
            get { return Position - Rotation.FacingCell; }
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

                if (feederGraphic == null)
                {
                    GraphicData data = def.graphicData;
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

            if (!Spawned || !this.IsHashIntervalTick(WorkInterval))
                return;

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
            else
                manager.WorkFeeder(this);
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
                if (returning)
                    return swing == Swing.Reach ? 180f * eased : 180f + 180f * eased;
                return swing == Swing.Out ? 180f * eased : 180f + 180f * eased;
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);

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
                : (closed ? ArmTex.FeederClosed : ArmTex.FeederOpen);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pivot, turn, Vector3.one), armMat, 0);
        }

        // What a Loader draws from: the whole storage its gripper cell belongs to - a stockpile
        // zone or a storage building - or, if that cell is not storage, whatever lies on it.
        public IEnumerable<Thing> SourceThings()
        {
            Map map = Map;
            if (map == null || !SourceCell.InBounds(map))
                yield break;

            SlotGroup group = SourceCell.GetSlotGroup(map);
            if (group != null)
            {
                foreach (Thing thing in group.HeldThings)
                    yield return thing;

                yield break;
            }

            List<Thing> things = SourceCell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (things[i].def.category == ThingCategory.Item)
                    yield return things[i];
            }
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
            toggle.icon = ContentFinder<Texture2D>.Get(
                mode == ArmMode.Loader ? "UI/Commands/FactoryLogistics/ArmLoader" : "UI/Commands/FactoryLogistics/ArmFeeder");
            toggle.action = delegate
            {
                SetMode(mode == ArmMode.Loader ? ArmMode.Feeder : ArmMode.Loader);
            };
            yield return toggle;

            // Turn it round where it stands: takes from the other side, puts down on this one.
            // Turn it where it stands, a quarter turn either way, like Q and E when placing.
            Command_Action clockwise = new Command_Action();
            clockwise.defaultLabel = "FactoryLogistics.ArmRotateClockwise".Translate();
            clockwise.defaultDesc = "FactoryLogistics.ArmRotateDesc".Translate();
            clockwise.icon = ContentFinder<Texture2D>.Get("UI/Widgets/RotRight");
            clockwise.action = delegate { Turn(Rotation.Rotated(RotationDirection.Clockwise)); };
            yield return clockwise;

            Command_Action counter = new Command_Action();
            counter.defaultLabel = "FactoryLogistics.ArmRotateCounter".Translate();
            counter.defaultDesc = "FactoryLogistics.ArmRotateDesc".Translate();
            counter.icon = ContentFinder<Texture2D>.Get("UI/Widgets/RotLeft");
            counter.action = delegate { Turn(Rotation.Rotated(RotationDirection.Counterclockwise)); };
            yield return counter;

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
