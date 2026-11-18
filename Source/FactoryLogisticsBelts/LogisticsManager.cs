using System.Collections.Generic;
using System.Reflection;
using PipeSystem;
using RimWorld;
using Verse;
using VanillaFurnitureExpandedFactory;

namespace FactoryLogistics.Belts
{
    // One belt network: a Feeder at its end, every conveyor that leads to it, the Loaders that put
    // onto those conveyors, and the VFE Factory machine the Feeder serves.
    public class LogisticsNetwork
    {
        public Building_LogisticsArm feeder;
        public CompAdvancedResourceProcessor machine;
        public readonly HashSet<Building_Conveyor> belts = new HashSet<Building_Conveyor>();
        public readonly List<Building_LogisticsArm> loaders = new List<Building_LogisticsArm>();

        // Set when an earlier Feeder already owns these belts: one Feeder per network.
        public Building_LogisticsArm pausedBy;
    }

    // What a machine still needs of one ingredient: a specific item, or anything in a category
    // ("any stone chunk").
    public struct Need
    {
        public ThingDef thing;
        public ThingCategoryDef category;
        public List<ThingDef> disallowed;
        public int count;

        public bool Matches(ThingDef def)
        {
            if (def == null)
                return false;
            if (disallowed != null && disallowed.Contains(def))
                return false;
            if (thing != null)
                return def == thing;
            return category != null && def.IsWithinCategory(category);
        }

        public string Label
        {
            get
            {
                if (thing != null)
                    return thing.label;
                return category != null ? category.label : "?";
            }
        }
    }

    // Runs the belt networks on one map. Nothing here is saved: every network is worked out again
    // from what is built, so a save never carries stale routing.
    //
    // VFE gives no notice when a conveyor is built or removed, so networks are rebuilt every
    // couple of seconds as well as whenever an arm changes - cheap, since the walk only follows
    // belts that actually lead to a Feeder.
    public class LogisticsManager : MapComponent
    {
        private const int RebuildInterval = 120;
        private const float MachineSearchRadius = 8f;

        private readonly List<LogisticsNetwork> networks = new List<LogisticsNetwork>();
        private readonly Dictionary<Building_LogisticsArm, LogisticsNetwork> byArm =
            new Dictionary<Building_LogisticsArm, LogisticsNetwork>();
        private bool dirty = true;
        private int builtAt = -99999;

        // VFE keeps the link between an underground entrance and its exit in a protected field.
        private static readonly FieldInfo LinkedField =
            typeof(Building_UndergroundConveyorBase).GetField("linkedBuilding", BindingFlags.Instance | BindingFlags.NonPublic);

        public LogisticsManager(Map map) : base(map)
        {
        }

        public static LogisticsManager For(Map map)
        {
            return map != null ? map.GetComponent<LogisticsManager>() : null;
        }

        public void MarkDirty()
        {
            dirty = true;
        }

        public static Building_Conveyor ConveyorAt(IntVec3 cell, Map map)
        {
            if (map == null || !cell.InBounds(map))
                return null;

            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Building_Conveyor conveyor = things[i] as Building_Conveyor;
                if (conveyor != null)
                    return conveyor;
            }

            return null;
        }

        public static IEnumerable<Building_LogisticsArm> AllArms(Map map)
        {
            List<Thing> things = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            for (int i = 0; i < things.Count; i++)
            {
                Building_LogisticsArm arm = things[i] as Building_LogisticsArm;
                if (arm != null)
                    yield return arm;
            }
        }

        // ------------------------------------------------------------------ networks

        private void EnsureBuilt()
        {
            int now = Find.TickManager.TicksGame;
            if (!dirty && now - builtAt < RebuildInterval)
                return;

            dirty = false;
            builtAt = now;
            networks.Clear();
            byArm.Clear();

            List<Building_LogisticsArm> feeders = new List<Building_LogisticsArm>();
            List<Building_LogisticsArm> loaders = new List<Building_LogisticsArm>();
            foreach (Building_LogisticsArm arm in AllArms(map))
            {
                if (arm.Mode == ArmMode.Feeder)
                    feeders.Add(arm);
                else
                    loaders.Add(arm);
            }

            // Oldest Feeder first, so the one that keeps a contested belt is always the same one.
            feeders.SortBy(a => a.thingIDNumber);

            Dictionary<Building_Conveyor, LogisticsNetwork> claimed = new Dictionary<Building_Conveyor, LogisticsNetwork>();
            foreach (Building_LogisticsArm feeder in feeders)
            {
                LogisticsNetwork network = new LogisticsNetwork();
                network.feeder = feeder;
                CollectBelts(feeder, network.belts);

                LogisticsNetwork owner = null;
                foreach (Building_Conveyor belt in network.belts)
                {
                    LogisticsNetwork other;
                    if (claimed.TryGetValue(belt, out other))
                    {
                        owner = other;
                        break;
                    }
                }

                if (owner != null)
                {
                    network.pausedBy = owner.feeder;
                    network.belts.Clear();
                }
                else
                {
                    foreach (Building_Conveyor belt in network.belts)
                        claimed[belt] = network;
                }

                network.machine = FindMachine(feeder);
                networks.Add(network);
                byArm[feeder] = network;
            }

            foreach (Building_LogisticsArm loader in loaders)
            {
                Building_Conveyor belt = loader.FrontConveyor;
                LogisticsNetwork network;
                if (belt != null && claimed.TryGetValue(belt, out network))
                {
                    network.loaders.Add(loader);
                    byArm[loader] = network;
                }
            }
        }

        // Every conveyor that leads to the Feeder: start at the belt whose end points at it and
        // walk back up every belt that feeds into one already found. Merges are followed, and so
        // are underground conveyors: an exit leads back to the entrance it is linked to.
        private void CollectBelts(Building_LogisticsArm feeder, HashSet<Building_Conveyor> belts)
        {
            Building_Conveyor last = feeder.BackConveyor;
            if (last == null || last.ForwardCell != feeder.Position)
                return;

            Queue<Building_Conveyor> open = new Queue<Building_Conveyor>();
            belts.Add(last);
            open.Enqueue(last);

            while (open.Count > 0)
            {
                Building_Conveyor belt = open.Dequeue();

                Building_UndergroundConveyorExit exit = belt as Building_UndergroundConveyorExit;
                if (exit != null && LinkedField != null)
                {
                    Building_Conveyor entrance = LinkedField.GetValue(exit) as Building_Conveyor;
                    if (entrance != null && entrance.Spawned && belts.Add(entrance))
                        open.Enqueue(entrance);
                }

                for (int i = 0; i < 4; i++)
                {
                    Building_Conveyor upstream = ConveyorAt(belt.Position + GenAdj.CardinalDirections[i], map);
                    if (upstream == null || belts.Contains(upstream))
                        continue;

                    // An entrance's forward cell is not where its items come out.
                    if (upstream is Building_UndergroundConveyorEntrance)
                        continue;

                    if (upstream.ForwardCell == belt.Position)
                    {
                        belts.Add(upstream);
                        open.Enqueue(upstream);
                    }
                }
            }
        }

        // The VFE Factory machine with an input slot on the cell in front of the Feeder - the same
        // slot a factory hopper (or a conveyor, which VFE also counts as one) sits on.
        private CompAdvancedResourceProcessor FindMachine(Building_LogisticsArm feeder)
        {
            IntVec3 slot = feeder.FrontCell;
            HashSet<Thing> seen = new HashSet<Thing>();

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(slot, MachineSearchRadius, true))
            {
                if (!cell.InBounds(map))
                    continue;

                Building building = cell.GetEdifice(map);
                if (building == null || !seen.Add(building))
                    continue;

                CompAdvancedResourceProcessor processor = building.TryGetComp<CompAdvancedResourceProcessor>();
                if (processor == null || processor.Props == null || processor.Props.processes == null)
                    continue;

                foreach (ProcessDef process in processor.Props.processes)
                {
                    if (process == null || process.autoInputSlots == null)
                        continue;

                    foreach (IntVec3 offset in process.autoInputSlots)
                    {
                        if (building.Position + offset.RotatedBy(building.Rotation) == slot)
                            return processor;
                    }
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ demand

        // What the machine's current process still lacks. Pipe-net ingredients (fuel, chemfuel
        // through pipes) are not belt cargo and are left out.
        public static List<Need> NeedsOf(CompAdvancedResourceProcessor machine)
        {
            List<Need> needs = new List<Need>();
            Process process = machine != null ? machine.Process : null;
            List<ThingAndResourceOwner> owners = process != null ? process.IngredientsOwners : null;
            if (owners == null)
                return needs;

            for (int i = 0; i < owners.Count; i++)
            {
                ThingAndResourceOwner owner = owners[i];
                if (owner == null || owner.PipeNetDef != null || owner.Required <= 0)
                    continue;
                if (owner.ThingDef == null && owner.ThingCategoryDef == null)
                    continue;

                Need need = new Need();
                need.thing = owner.ThingDef;
                need.category = owner.ThingCategoryDef;
                need.count = owner.Required;
                need.disallowed = DisallowedFor(process, owner);
                needs.Add(need);
            }

            return needs;
        }

        // The process def's own exclusions for a category ingredient ("any raw food but hay").
        private static List<ThingDef> DisallowedFor(Process process, ThingAndResourceOwner owner)
        {
            if (owner.ThingCategoryDef == null || process.Def == null || process.Def.ingredients == null)
                return null;

            foreach (ProcessDef.Ingredient ingredient in process.Def.ingredients)
            {
                if (ingredient.thingCategory == owner.ThingCategoryDef)
                    return ingredient.disallowedThingDefs;
            }

            return null;
        }

        private int CountOn(IntVec3 cell, Need need)
        {
            if (!cell.InBounds(map))
                return 0;

            int count = 0;
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing.def.category == ThingCategory.Item && need.Matches(thing.def))
                    count += thing.stackCount;
            }

            Building_Conveyor conveyor = ConveyorAt(cell, map);
            if (conveyor != null)
                count += CountIn(conveyor, need);

            return count;
        }

        private static int CountIn(Building_Conveyor belt, Need need)
        {
            int count = 0;
            if (belt.innerContainer == null)
                return 0;

            foreach (Thing thing in belt.innerContainer)
            {
                if (need.Matches(thing.def))
                    count += thing.stackCount;
            }

            return count;
        }

        // Already on its way or already delivered: on the belts, lying on a belt cell waiting to be
        // picked up, on the Feeder, or in the machine's input slot. A Loader only ever sends what
        // is still missing after all of that, so the network never over-sends.
        private int CountInFlight(LogisticsNetwork network, Need need)
        {
            int count = CountOn(network.feeder.Position, need) + CountOn(network.feeder.FrontCell, need);
            foreach (Building_Conveyor belt in network.belts)
            {
                count += CountIn(belt, need);
                List<Thing> things = belt.Position.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i].def.category == ThingCategory.Item && need.Matches(things[i].def))
                        count += things[i].stackCount;
                }
            }

            return count;
        }

        // ------------------------------------------------------------------ work

        public void WorkLoader(Building_LogisticsArm loader)
        {
            EnsureBuilt();

            LogisticsNetwork network;
            if (!byArm.TryGetValue(loader, out network))
            {
                loader.status = loader.FrontConveyor == null
                    ? "FactoryLogistics.LoaderNoBelt".Translate().ToString()
                    : "FactoryLogistics.LoaderNoFeeder".Translate().ToString();
                return;
            }

            if (network.machine == null)
            {
                loader.status = "FactoryLogistics.NoMachine".Translate();
                return;
            }

            Building_Conveyor belt = loader.FrontConveyor;
            if (belt == null)
                return;

            // One stack at a time onto an empty belt cell.
            if (!belt.HasSpace() || belt.state == Building_Conveyor.ConveyorState.Moving)
            {
                loader.status = "FactoryLogistics.LoaderBeltBusy".Translate();
                return;
            }

            List<Need> needs = NeedsOf(network.machine);
            if (needs.Count == 0)
            {
                loader.status = "FactoryLogistics.MachineSatisfied".Translate();
                return;
            }

            string missing = null;
            foreach (Need need in needs)
            {
                int outstanding = need.count - CountInFlight(network, need);
                if (outstanding <= 0)
                    continue;

                Thing source = null;
                foreach (Thing thing in loader.SourceThings())
                {
                    if (thing.Spawned && need.Matches(thing.def) && !thing.IsForbidden(Faction.OfPlayer)
                        && !map.reservationManager.IsReservedByAnyoneOf(thing, Faction.OfPlayer))
                    {
                        source = thing;
                        break;
                    }
                }

                if (source == null)
                {
                    if (missing == null)
                        missing = need.Label;
                    continue;
                }

                int take = UnityEngine.Mathf.Min(outstanding, source.stackCount);
                IntVec3 from = source.Position;
                Thing sent = source.SplitOff(take);
                if (sent.Spawned)
                    sent.DeSpawn();
                if (!belt.TryAdd(sent))
                {
                    // Put it back where it came from rather than lose it.
                    GenPlace.TryPlaceThing(sent, from, map, ThingPlaceMode.Near);
                    loader.status = "FactoryLogistics.LoaderBeltBusy".Translate();
                    return;
                }

                loader.status = "FactoryLogistics.LoaderSent".Translate(sent.LabelCap, network.machine.parent.LabelShort);
                return;
            }

            loader.status = missing != null
                ? "FactoryLogistics.LoaderNoStock".Translate(missing).ToString()
                : "FactoryLogistics.MachineCovered".Translate().ToString();
        }

        public void WorkFeeder(Building_LogisticsArm feeder)
        {
            EnsureBuilt();

            LogisticsNetwork network;
            byArm.TryGetValue(feeder, out network);
            if (network != null && network.pausedBy != null)
            {
                feeder.status = "FactoryLogistics.FeederPaused".Translate(network.pausedBy.LabelShort);
                return;
            }

            List<Thing> arrived = new List<Thing>();
            List<Thing> here = feeder.Position.GetThingList(map);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i].def.category == ThingCategory.Item)
                    arrived.Add(here[i]);
            }

            bool hasMachine = network != null && network.machine != null;
            if (arrived.Count == 0)
            {
                feeder.status = hasMachine ? null : "FactoryLogistics.NoMachine".Translate().ToString();
                return;
            }

            List<Need> needs = hasMachine ? NeedsOf(network.machine) : new List<Need>();
            foreach (Thing thing in arrived)
            {
                // Still wanted once what is already in the input slot is counted? Into the slot.
                bool wanted = false;
                foreach (Need need in needs)
                {
                    if (need.Matches(thing.def) && need.count - CountOn(feeder.FrontCell, need) > 0)
                    {
                        wanted = true;
                        break;
                    }
                }

                if (wanted)
                {
                    if (TryDeliver(feeder, thing))
                        feeder.status = "FactoryLogistics.FeederDelivered".Translate(thing.LabelCap);
                    else
                        feeder.status = "FactoryLogistics.FeederSlotFull".Translate();   // the belt behind waits too
                    continue;
                }

                // Nothing on this network wants it any more - the process finished or changed, or
                // it reached the belt some other way. Off the belt it goes, so it never jams.
                DropStray(feeder, thing);
            }
        }

        // Into the conveyor or factory hopper on the machine's input slot.
        private bool TryDeliver(Building_LogisticsArm feeder, Thing thing)
        {
            Building_Conveyor slotBelt = feeder.FrontConveyor;
            if (slotBelt != null)
            {
                if (!slotBelt.CanAcceptItem(thing) || !slotBelt.HasSpace())
                    return false;
                thing.DeSpawn();
                if (slotBelt.TryAdd(thing))
                    return true;
                GenSpawn.Spawn(thing, feeder.Position, map);
                return false;
            }

            IntVec3 at = thing.Position;
            thing.DeSpawn();
            if (GenPlace.TryPlaceThing(thing, feeder.FrontCell, map, ThingPlaceMode.Direct))
                return true;

            if (!thing.Destroyed && !thing.Spawned)
                GenSpawn.Spawn(thing, at, map);
            return false;
        }

        private void DropStray(Building_LogisticsArm feeder, Thing thing)
        {
            IntVec3 at = thing.Position;
            IntVec3 front = feeder.FrontCell;
            IntVec3 self = feeder.Position;
            Map m = map;
            thing.DeSpawn();
            bool placed = GenPlace.TryPlaceThing(thing, self, m, ThingPlaceMode.Near, null,
                delegate (IntVec3 c) { return c != self && c != front && ConveyorAt(c, m) == null && c.Standable(m); });
            if (!placed)
            {
                if (!thing.Destroyed && !thing.Spawned)
                    GenSpawn.Spawn(thing, at, m);
                return;
            }

            feeder.status = "FactoryLogistics.FeederDropped".Translate(thing.LabelCap);
        }

        // ------------------------------------------------------------------ UI

        public Thing MachineFor(Building_LogisticsArm arm)
        {
            EnsureBuilt();
            LogisticsNetwork network;
            if (byArm.TryGetValue(arm, out network) && network.machine != null)
                return network.machine.parent;
            return null;
        }

        public string DescribeFor(Building_LogisticsArm arm)
        {
            EnsureBuilt();

            LogisticsNetwork network;
            if (!byArm.TryGetValue(arm, out network))
                return arm.Mode == ArmMode.Loader ? "FactoryLogistics.LoaderNoFeeder".Translate().ToString() : null;

            if (network.pausedBy != null)
                return "FactoryLogistics.FeederPaused".Translate(network.pausedBy.LabelShort);

            if (network.machine == null)
                return "FactoryLogistics.NoMachine".Translate();

            List<Need> needs = NeedsOf(network.machine);
            string machine = network.machine.parent.LabelShort;
            string line = arm.Mode == ArmMode.Feeder
                ? "FactoryLogistics.Feeding".Translate(machine, network.loaders.Count).ToString()
                : "FactoryLogistics.Serving".Translate(machine).ToString();

            if (needs.Count == 0)
                return line + "\n" + "FactoryLogistics.MachineSatisfied".Translate();

            List<string> parts = new List<string>();
            foreach (Need need in needs)
                parts.Add(need.count + " " + need.Label);
            return line + "\n" + "FactoryLogistics.CallingFor".Translate(string.Join(", ", parts.ToArray()));
        }
    }
}
