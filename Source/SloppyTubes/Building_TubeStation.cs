using System.Collections.Generic;
using PipeSystem;
using RimWorld;
using Verse;

namespace SloppyTubes
{
    /// <summary>
    /// What sending and receiving stations share: both are storage buildings (so vanilla
    /// hauling and bills can use them with no custom jobs), both sit on the compressed air
    /// network, and both carry a name the player can change.
    ///
    /// The part that needs care is WHICH capsules a station's storage takes. A storage filter
    /// can only say "capsules"; a sending station has to say "capsules addressed to me". Vanilla
    /// decides whether a thing may be stored by asking the building's storage settings, which
    /// in turn ask the building's PARENT settings - normally the def's fixed filter. So each
    /// station hands out its own parent settings (the gate, below) whose filter also asks the
    /// station. Building_Storage's method is not virtual, which is why IStoreSettingsParent is
    /// listed again here: everything in vanilla reaches it through that interface. Nothing
    /// about the gate is saved, and no Harmony patch is involved.
    /// </summary>
    public abstract class Building_TubeStation : Building_Storage, IStoreSettingsParent
    {
        private string customName;

        private StorageSettings gate;

        protected CompResourceTrader airComp;

        public TubeStationExtension Ext
        {
            get { return def.GetModExtension<TubeStationExtension>() ?? DefaultExt; }
        }

        private static readonly TubeStationExtension DefaultExt = new TubeStationExtension();

        /// <summary>True while the station is getting its trickle of compressed air.</summary>
        public bool AirOn
        {
            get { return airComp != null && airComp.ResourceOn; }
        }

        /// <summary>The tube network the station is on. Two stations are linked if these match.</summary>
        public PipeNet Net
        {
            get { return airComp != null ? airComp.PipeNet : null; }
        }

        public string StationName
        {
            get { return customName.NullOrEmpty() ? def.LabelCap.ToString() : customName; }
        }

        public string RenamableLabel
        {
            get { return StationName; }
            set { customName = value; }
        }

        public string BaseLabel
        {
            get { return def.LabelCap; }
        }

        public string InspectLabel
        {
            get { return StationName; }
        }

        // The bill "take to" list and the inspect pane both show this.
        public override string LabelNoCount
        {
            get { return StationName; }
        }

        /// <summary>Whether this particular thing belongs in this station.</summary>
        public abstract bool AcceptsThing(Thing thing);

        public new StorageSettings GetParentStoreSettings()
        {
            if (gate == null)
            {
                StorageSettings fixedSettings = base.GetParentStoreSettings();
                gate = new StorageSettings();
                gate.filter = new TubeStationFilter(this);
                gate.filter.CopyAllowancesFrom(fixedSettings.filter);
            }
            return gate;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            airComp = GetComp<CompResourceTrader>();

            // Number new stations so the lists that name them are readable before the player
            // has renamed anything: "Tube sender 2", "Tube receiver 3".
            if (customName.NullOrEmpty())
            {
                int number = 1;
                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    if (building != this && building.def == def)
                        number++;
                }
                customName = def.LabelCap + " " + number;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref customName, "tubeStationName");
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;

            if (Faction != Faction.OfPlayer)
                yield break;

            Command_Action rename = new Command_Action();
            rename.icon = TubeTex.Rename;
            rename.defaultLabel = "SloppyTubes.Rename".Translate();
            rename.defaultDesc = "SloppyTubes.RenameDesc".Translate();
            rename.action = delegate { Find.WindowStack.Add(new Dialog_RenameTubeStation(new TubeStationName(this))); };
            yield return rename;
        }

        /// <summary>The first thing of the given kind lying in this station's storage cells.</summary>
        protected TubeCapsule FirstHeldCapsule(bool delivered)
        {
            List<IntVec3> cells = AllSlotCellsList();
            for (int i = 0; i < cells.Count; i++)
            {
                List<Thing> things = Map.thingGrid.ThingsListAt(cells[i]);
                for (int j = 0; j < things.Count; j++)
                {
                    TubeCapsule capsule = things[j] as TubeCapsule;
                    if (capsule != null && capsule.Delivered == delivered)
                        return capsule;
                }
            }
            return null;
        }

        protected int ItemCountAt(IntVec3 cell)
        {
            int count = 0;
            List<Thing> things = Map.thingGrid.ThingsListAt(cell);
            for (int j = 0; j < things.Count; j++)
            {
                if (things[j].def.category == ThingCategory.Item)
                    count++;
            }
            return count;
        }

        protected int HeldItemCount()
        {
            int count = 0;
            List<IntVec3> cells = AllSlotCellsList();
            for (int i = 0; i < cells.Count; i++)
                count += ItemCountAt(cells[i]);
            return count;
        }

        protected bool HoldsInItsCells(Thing thing)
        {
            if (!thing.Spawned || thing.Map != Map)
                return false;
            return AllSlotCellsList().Contains(thing.Position);
        }
    }

    /// <summary>The station's half of its storage rule; see Building_TubeStation.</summary>
    public class TubeStationFilter : ThingFilter
    {
        private readonly Building_TubeStation station;

        public TubeStationFilter()
        {
        }

        public TubeStationFilter(Building_TubeStation station)
        {
            this.station = station;
        }

        public override bool Allows(Thing t)
        {
            return base.Allows(t) && (station == null || station.AcceptsThing(t));
        }
    }

    /// <summary>
    /// What the rename dialog edits. Stations are not IRenameable themselves, except the
    /// sender: the bill "take to" list shows every IRenameable storage building on the map
    /// for every bill, and only senders belong in it.
    /// </summary>
    public class TubeStationName : IRenameable
    {
        private readonly Building_TubeStation station;

        public TubeStationName(Building_TubeStation station)
        {
            this.station = station;
        }

        public string RenamableLabel
        {
            get { return station.RenamableLabel; }
            set { station.RenamableLabel = value; }
        }

        public string BaseLabel
        {
            get { return station.BaseLabel; }
        }

        public string InspectLabel
        {
            get { return station.InspectLabel; }
        }
    }

    public class Dialog_RenameTubeStation : Dialog_Rename<TubeStationName>
    {
        public Dialog_RenameTubeStation(TubeStationName name) : base(name)
        {
        }
    }
}
