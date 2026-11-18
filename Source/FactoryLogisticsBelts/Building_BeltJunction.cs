using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace FactoryLogistics.Belts
{
    /// <summary>
    /// Splits one supply trunk between several machines. The trunk belt points at the junction
    /// and drops its items onto it; belts leading away from the junction's other sides each run
    /// to a Feeder. Loaders on the trunk send what all those Feeders' machines still lack, and the
    /// junction passes each item to the branch that wants it - highest Feeder priority first, then
    /// the branch served longest ago. An item no branch wants any more is set down beside it, so
    /// the trunk never jams; one a branch wants but has no room for yet waits on the junction.
    ///
    /// Like the arm it is not an edifice, which is what makes a VFE belt drop items onto it.
    /// </summary>
    public class Building_BeltJunction : Building
    {
        private const int WorkInterval = 20;

        private CompPowerTrader power;
        private readonly Dictionary<int, int> lastServed = new Dictionary<int, int>();
        public string status;

        public bool PowerOn
        {
            get { return power == null || power.PowerOn; }
        }

        public int ServedAgo(Building_LogisticsArm feeder)
        {
            int tick;
            if (!lastServed.TryGetValue(feeder.thingIDNumber, out tick))
                return 1000000;
            return Mathf.Min(1000000, Find.TickManager.TicksGame - tick);
        }

        public void NoteServed(Building_LogisticsArm feeder)
        {
            lastServed[feeder.thingIDNumber] = Find.TickManager.TicksGame;
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
            if (manager != null)
                manager.WorkJunction(this);
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());
            LogisticsManager manager = Spawned ? LogisticsManager.For(Map) : null;
            string summary = manager != null ? manager.DescribeFor(this) : null;
            if (!summary.NullOrEmpty())
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.Append(summary);
            }
            if (!status.NullOrEmpty())
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.Append(status);
            }
            return sb.ToString().TrimEndNewlines();
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            LogisticsManager manager = Spawned ? LogisticsManager.For(Map) : null;
            if (manager == null)
                return;
            foreach (Thing machine in manager.BranchMachines(this))
                GenDraw.DrawLineBetween(DrawPos, machine.DrawPos, SimpleColor.Orange);
        }
    }
}
