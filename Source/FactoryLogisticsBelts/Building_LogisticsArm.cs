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
    public class Building_LogisticsArm : Building
    {
        private ArmMode mode = ArmMode.Loader;
        private int priority = 2;
        private const int WorkInterval = 30;

        // Status for the inspect pane, set by the last work step.
        public string status;

        private CompPowerTrader power;
        private Graphic feederGraphic;

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

        // ------------------------------------------------------------------ work

        protected override void Tick()
        {
            base.Tick();

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
