using System.Collections.Generic;
using System.Reflection;
using PipeSystem;
using RimWorld;
using Verse;
using VanillaFurnitureExpandedFactory;

namespace FactoryLogistics.Belts
{
    // A run of belts that ends somewhere: at a Feeder (a branch) or at a junction (a trunk).
    // Holds every conveyor that leads there, the lifts it passes through, and the Loaders that
    // put onto it.
    public class BeltRun
    {
        public readonly HashSet<Building_Conveyor> belts = new HashSet<Building_Conveyor>();
        public readonly HashSet<Building_ConveyorLift> lifts = new HashSet<Building_ConveyorLift>();
        public readonly List<Building_LogisticsArm> loaders = new List<Building_LogisticsArm>();
    }

    // One Feeder's network: its machine and the belts that lead to it. If the head of those belts
    // is a junction's output, the Feeder is one of that junction's branches.
    public class LogisticsNetwork : BeltRun
    {
        public Building_LogisticsArm feeder;
        public CompAdvancedResourceProcessor machine;
        public Building_BeltJunction junction;

        // Set when an earlier Feeder already owns these belts: one Feeder per run of belts.
        public Building_LogisticsArm pausedBy;
    }

    // A junction, the trunk of belts that leads into it, and the Feeder branches it splits to.
    public class JunctionGroup : BeltRun
    {
        public Building_BeltJunction junction;
        public readonly List<LogisticsNetwork> branches = new List<LogisticsNetwork>();
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
    // couple of seconds as well as whenever an arm, lift or junction changes - cheap, since the
    // walk only follows belts that actually lead to a Feeder or a junction.
    public class LogisticsManager : MapComponent
    {
        private const int RebuildInterval = 120;
        private const float MachineSearchRadius = 8f;

        private readonly List<LogisticsNetwork> networks = new List<LogisticsNetwork>();
        private readonly List<JunctionGroup> groups = new List<JunctionGroup>();
        private readonly Dictionary<Building_LogisticsArm, BeltRun> byArm = new Dictionary<Building_LogisticsArm, BeltRun>();
        private readonly Dictionary<Building_BeltJunction, JunctionGroup> byJunction = new Dictionary<Building_BeltJunction, JunctionGroup>();
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

        private static List<T> AllOf<T>(Map map) where T : Building
        {
            List<T> result = new List<T>();
            List<Thing> things = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            for (int i = 0; i < things.Count; i++)
            {
                T thing = things[i] as T;
                if (thing != null)
                    result.Add(thing);
            }
            return result;
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
            groups.Clear();
            byArm.Clear();
            byJunction.Clear();

            List<Building_LogisticsArm> feeders = new List<Building_LogisticsArm>();
            List<Building_LogisticsArm> loaders = new List<Building_LogisticsArm>();
            foreach (Building_LogisticsArm arm in AllOf<Building_LogisticsArm>(map))
            {
                if (arm.Mode == ArmMode.Feeder)
                    feeders.Add(arm);
                else
                    loaders.Add(arm);
            }
            List<Building_ConveyorLift> allLifts = AllOf<Building_ConveyorLift>(map);
            List<Building_BeltJunction> junctions = AllOf<Building_BeltJunction>(map);

            // Oldest first, so whatever keeps a contested belt is always the same one.
            feeders.SortBy(a => a.thingIDNumber);
            junctions.SortBy(j => j.thingIDNumber);

            Dictionary<Building_Conveyor, BeltRun> claimed = new Dictionary<Building_Conveyor, BeltRun>();

            foreach (Building_LogisticsArm feeder in feeders)
            {
                LogisticsNetwork network = new LogisticsNetwork();
                network.feeder = feeder;
                CollectBelts(feeder.Position, network, allLifts);

                LogisticsNetwork owner = null;
                foreach (Building_Conveyor belt in network.belts)
                {
                    BeltRun other;
                    if (claimed.TryGetValue(belt, out other))
                    {
                        owner = other as LogisticsNetwork;
                        break;
                    }
                }

                if (owner != null)
                {
                    network.pausedBy = owner.feeder;
                    network.belts.Clear();
                    network.lifts.Clear();
                }
                else
                {
                    foreach (Building_Conveyor belt in network.belts)
                        claimed[belt] = network;
                    network.junction = JunctionFeeding(network, junctions);
                }

                network.machine = FindMachine(feeder);
                networks.Add(network);
                byArm[feeder] = network;
            }

            foreach (Building_BeltJunction junction in junctions)
            {
                JunctionGroup group = new JunctionGroup();
                group.junction = junction;
                CollectBelts(junction.Position, group, allLifts);
                List<Building_Conveyor> taken = new List<Building_Conveyor>();
                foreach (Building_Conveyor belt in group.belts)
                {
                    if (claimed.ContainsKey(belt))
                        taken.Add(belt);
                }
                foreach (Building_Conveyor belt in taken)
                    group.belts.Remove(belt);
                foreach (Building_Conveyor belt in group.belts)
                    claimed[belt] = group;

                foreach (LogisticsNetwork network in networks)
                {
                    if (network.junction == junction)
                        group.branches.Add(network);
                }
                groups.Add(group);
                byJunction[junction] = group;
            }

            foreach (Building_LogisticsArm loader in loaders)
            {
                Building_Conveyor belt = loader.FrontConveyor;
                BeltRun run;
                if (belt != null && claimed.TryGetValue(belt, out run))
                {
                    run.loaders.Add(loader);
                    byArm[loader] = run;
                }
            }
        }

        // The junction whose output the head of this branch takes from: a branch belt beside a
        // junction that does not point into it.
        private static Building_BeltJunction JunctionFeeding(LogisticsNetwork network, List<Building_BeltJunction> junctions)
        {
            foreach (Building_BeltJunction junction in junctions)
            {
                for (int i = 0; i < 4; i++)
                {
                    Building_Conveyor belt = ConveyorAt(junction.Position + GenAdj.CardinalDirections[i], junction.Map);
                    if (belt != null && belt.ForwardCell != junction.Position && network.belts.Contains(belt))
                        return junction;
                }
            }
            return null;
        }

        // Every conveyor that leads to the target cell (a Feeder or a junction): start at the belts
        // whose end points at it and walk back up every belt that feeds into one already found.
        // Merges are followed, and so are underground conveyors (an exit leads back to the
        // entrance it is linked to) and lifts (a lift putting onto a belt leads back to every lift
        // sending to it, and the belts that drop onto those).
        private void CollectBelts(IntVec3 target, BeltRun run, List<Building_ConveyorLift> allLifts)
        {
            HashSet<Building_Conveyor> belts = run.belts;
            Queue<Building_Conveyor> open = new Queue<Building_Conveyor>();
            for (int i = 0; i < 4; i++)
            {
                Building_Conveyor last = ConveyorAt(target + GenAdj.CardinalDirections[i], map);
                if (last != null && last.ForwardCell == target && !(last is Building_UndergroundConveyorEntrance) && belts.Add(last))
                    open.Enqueue(last);
            }

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
                    if (upstream != null && !belts.Contains(upstream)
                        && !(upstream is Building_UndergroundConveyorEntrance)   // its forward cell is not where items come out
                        && upstream.ForwardCell == belt.Position)
                    {
                        belts.Add(upstream);
                        open.Enqueue(upstream);
                    }
                }

                // A lift beside this belt that puts onto it.
                for (int i = 0; i < allLifts.Count; i++)
                {
                    Building_ConveyorLift output = allLifts[i];
                    if (!output.Spawned || output.FrontCell != belt.Position)
                        continue;
                    for (int j = 0; j < allLifts.Count; j++)
                    {
                        Building_ConveyorLift input = allLifts[j];
                        if (input == output || input.Partner != output || !run.lifts.Add(input))
                            continue;
                        for (int k = 0; k < 4; k++)
                        {
                            Building_Conveyor feeding = ConveyorAt(input.Position + GenAdj.CardinalDirections[k], map);
                            if (feeding != null && !belts.Contains(feeding) && feeding.ForwardCell == input.Position)
                            {
                                belts.Add(feeding);
                                open.Enqueue(feeding);
                            }
                        }
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

        private int CountLoose(IntVec3 cell, Need need)
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
            return count;
        }

        // Lying on the cell, or carried by a conveyor standing on it.
        private int CountOn(IntVec3 cell, Need need)
        {
            int count = CountLoose(cell, need);
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

        // On a run's belts, lying on its belt cells waiting to be picked up, or waiting on its lifts.
        private int CountOnRun(BeltRun run, Need need)
        {
            int count = 0;
            foreach (Building_Conveyor belt in run.belts)
                count += CountIn(belt, need) + CountLoose(belt.Position, need);
            foreach (Building_ConveyorLift lift in run.lifts)
                count += CountLoose(lift.Position, need);
            return count;
        }

        // Already on its way to this Feeder or already delivered: on its belts and lifts, on the
        // Feeder, or in the machine's input slot.
        private int CountInFlight(LogisticsNetwork network, Need need)
        {
            return CountOnRun(network, need) + CountOn(network.feeder.Position, need) + CountOn(network.feeder.FrontCell, need);
        }

        // What a Feeder's machine still lacks once everything already on its way is counted.
        private List<Need> Outstanding(LogisticsNetwork network)
        {
            List<Need> result = new List<Need>();
            if (network.machine == null || network.pausedBy != null)
                return result;
            foreach (Need need in NeedsOf(network.machine))
            {
                Need left = need;
                left.count = need.count - CountInFlight(network, need);
                if (left.count > 0)
                    result.Add(left);
            }
            return result;
        }

        // What a junction's trunk should carry: everything its branches still lack, less what is
        // already on the trunk or waiting on the junction. Higher-priority branches come first.
        private List<Need> Outstanding(JunctionGroup group)
        {
            List<Need> result = new List<Need>();
            List<LogisticsNetwork> branches = new List<LogisticsNetwork>(group.branches);
            branches.SortByDescending(b => b.feeder.Priority);
            foreach (LogisticsNetwork branch in branches)
                result.AddRange(Outstanding(branch));

            // Hand out what is already on the trunk to those needs in order.
            List<Thing> trunkItems = new List<Thing>();
            foreach (Building_Conveyor belt in group.belts)
            {
                if (belt.innerContainer != null)
                    trunkItems.AddRange(belt.innerContainer);
                AddLoose(belt.Position, trunkItems);
            }
            foreach (Building_ConveyorLift lift in group.lifts)
                AddLoose(lift.Position, trunkItems);
            AddLoose(group.junction.Position, trunkItems);

            Dictionary<Thing, int> left = new Dictionary<Thing, int>();
            foreach (Thing thing in trunkItems)
                left[thing] = thing.stackCount;

            for (int i = 0; i < result.Count; i++)
            {
                Need need = result[i];
                foreach (Thing thing in trunkItems)
                {
                    if (need.count <= 0)
                        break;
                    int have = left[thing];
                    if (have <= 0 || !need.Matches(thing.def))
                        continue;
                    int used = UnityEngine.Mathf.Min(have, need.count);
                    left[thing] = have - used;
                    need.count -= used;
                }
                result[i] = need;
            }
            result.RemoveAll(n => n.count <= 0);
            return result;
        }

        private void AddLoose(IntVec3 cell, List<Thing> into)
        {
            if (!cell.InBounds(map))
                return;
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def.category == ThingCategory.Item)
                    into.Add(things[i]);
            }
        }

        // ------------------------------------------------------------------ work

        public void WorkLoader(Building_LogisticsArm loader)
        {
            EnsureBuilt();

            BeltRun run;
            if (!byArm.TryGetValue(loader, out run))
            {
                loader.status = loader.FrontConveyor == null
                    ? "FactoryLogistics.LoaderNoBelt".Translate().ToString()
                    : "FactoryLogistics.LoaderNoFeeder".Translate().ToString();
                return;
            }

            LogisticsNetwork network = run as LogisticsNetwork;
            JunctionGroup group = run as JunctionGroup;
            if (network != null && network.machine == null)
            {
                loader.status = "FactoryLogistics.NoMachine".Translate();
                return;
            }
            if (group != null && group.branches.Count == 0)
            {
                loader.status = "FactoryLogistics.JunctionNoBranches".Translate();
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

            List<Need> outstanding = network != null ? Outstanding(network) : Outstanding(group);
            if (outstanding.Count == 0)
            {
                loader.status = "FactoryLogistics.MachineCovered".Translate();
                return;
            }

            string missing = null;
            foreach (Need need in outstanding)
            {
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

                int take = UnityEngine.Mathf.Min(need.count, source.stackCount);
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

                string to = network != null ? network.machine.parent.LabelShort : group.junction.LabelShort;
                loader.status = "FactoryLogistics.LoaderSent".Translate(sent.LabelCap, to);
                return;
            }

            loader.status = "FactoryLogistics.LoaderNoStock".Translate(missing ?? "?");
        }

        public void WorkFeeder(Building_LogisticsArm feeder)
        {
            EnsureBuilt();

            BeltRun run;
            byArm.TryGetValue(feeder, out run);
            LogisticsNetwork network = run as LogisticsNetwork;
            if (network != null && network.pausedBy != null)
            {
                feeder.status = "FactoryLogistics.FeederPaused".Translate(network.pausedBy.LabelShort);
                return;
            }

            List<Thing> arrived = new List<Thing>();
            AddLoose(feeder.Position, arrived);

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
                string label = thing.LabelCap;
                if (DropStray(feeder.Position, feeder.FrontCell, thing))
                    feeder.status = "FactoryLogistics.FeederDropped".Translate(label);
            }
        }

        // A junction passes each item that reaches it to the branch that wants it most, onto the
        // head belt of that branch. Items no branch wants are set down beside it.
        public void WorkJunction(Building_BeltJunction junction)
        {
            EnsureBuilt();

            JunctionGroup group;
            if (!byJunction.TryGetValue(junction, out group))
                return;

            List<Thing> arrived = new List<Thing>();
            AddLoose(junction.Position, arrived);
            if (arrived.Count == 0)
            {
                junction.status = group.branches.Count == 0 ? "FactoryLogistics.JunctionNoBranches".Translate().ToString() : null;
                return;
            }

            // Highest priority first; among equals, the branch served longest ago.
            List<LogisticsNetwork> branches = new List<LogisticsNetwork>(group.branches);
            branches.SortByDescending(b => b.feeder.Priority * 10000000 + junction.ServedAgo(b.feeder));

            foreach (Thing thing in arrived)
            {
                bool someoneWants = false;
                foreach (LogisticsNetwork branch in branches)
                {
                    bool wants = false;
                    foreach (Need need in Outstanding(branch))
                    {
                        if (need.Matches(thing.def))
                        {
                            wants = true;
                            break;
                        }
                    }
                    if (!wants)
                        continue;
                    someoneWants = true;

                    Building_Conveyor head = HeadBelt(junction, branch);
                    if (head == null || !head.HasSpace() || !head.CanAcceptItem(thing))
                        continue;   // try the next branch that wants it; if none has room, hold it

                    string label = thing.LabelCap;
                    thing.DeSpawn();
                    if (head.TryAdd(thing))
                    {
                        junction.NoteServed(branch.feeder);
                        junction.status = "FactoryLogistics.JunctionRouted".Translate(label,
                            branch.machine != null ? branch.machine.parent.LabelShort : branch.feeder.LabelShort);
                    }
                    else
                    {
                        GenSpawn.Spawn(thing, junction.Position, map);
                    }
                    break;
                }

                if (!someoneWants)
                {
                    string label = thing.LabelCap;
                    if (DropStray(junction.Position, IntVec3.Invalid, thing))
                        junction.status = "FactoryLogistics.FeederDropped".Translate(label);
                }
                else if (thing.Spawned && thing.Position == junction.Position)
                {
                    junction.status = "FactoryLogistics.JunctionWaiting".Translate(thing.LabelCap);
                }
            }
        }

        private Building_Conveyor HeadBelt(Building_BeltJunction junction, LogisticsNetwork branch)
        {
            for (int i = 0; i < 4; i++)
            {
                Building_Conveyor belt = ConveyorAt(junction.Position + GenAdj.CardinalDirections[i], map);
                if (belt != null && belt.ForwardCell != junction.Position && branch.belts.Contains(belt))
                    return belt;
            }
            return null;
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

        // Off the line: onto a free floor cell nearby that is not a belt or one of this module's
        // buildings, so it never re-enters the line.
        private bool DropStray(IntVec3 self, IntVec3 avoid, Thing thing)
        {
            IntVec3 at = thing.Position;
            Map m = map;
            thing.DeSpawn();
            bool placed = GenPlace.TryPlaceThing(thing, self, m, ThingPlaceMode.Near, null,
                delegate (IntVec3 c)
                {
                    if (c == self || c == avoid || ConveyorAt(c, m) != null || !c.Standable(m))
                        return false;
                    List<Thing> things = c.GetThingList(m);
                    for (int i = 0; i < things.Count; i++)
                    {
                        if (things[i] is Building_LogisticsArm || things[i] is Building_ConveyorLift || things[i] is Building_BeltJunction)
                            return false;
                    }
                    return true;
                });
            if (!placed && !thing.Destroyed && !thing.Spawned)
                GenSpawn.Spawn(thing, at, m);
            return placed;
        }

        // ------------------------------------------------------------------ UI

        public Thing MachineFor(Building_LogisticsArm arm)
        {
            EnsureBuilt();
            BeltRun run;
            if (!byArm.TryGetValue(arm, out run))
                return null;
            LogisticsNetwork network = run as LogisticsNetwork;
            if (network != null)
                return network.machine != null ? network.machine.parent : null;
            JunctionGroup group = run as JunctionGroup;
            return group != null ? group.junction : null;
        }

        public List<Thing> BranchMachines(Building_BeltJunction junction)
        {
            EnsureBuilt();
            List<Thing> result = new List<Thing>();
            JunctionGroup group;
            if (!byJunction.TryGetValue(junction, out group))
                return result;
            foreach (LogisticsNetwork branch in group.branches)
                result.Add(branch.machine != null ? (Thing)branch.machine.parent : branch.feeder);
            return result;
        }

        private static string NeedsLine(List<Need> needs)
        {
            List<string> parts = new List<string>();
            foreach (Need need in needs)
                parts.Add(need.count + " " + need.Label);
            return "FactoryLogistics.CallingFor".Translate(string.Join(", ", parts.ToArray()));
        }

        public string DescribeFor(Building_LogisticsArm arm)
        {
            EnsureBuilt();

            BeltRun run;
            if (!byArm.TryGetValue(arm, out run))
                return arm.Mode == ArmMode.Loader ? "FactoryLogistics.LoaderNoFeeder".Translate().ToString() : null;

            JunctionGroup group = run as JunctionGroup;
            if (group != null)
            {
                string head = "FactoryLogistics.ServingJunction".Translate(group.branches.Count);
                List<Need> wanted = Outstanding(group);
                return wanted.Count == 0 ? head : head + "\n" + NeedsLine(wanted);
            }

            LogisticsNetwork network = (LogisticsNetwork)run;
            if (network.pausedBy != null)
                return "FactoryLogistics.FeederPaused".Translate(network.pausedBy.LabelShort);

            if (network.machine == null)
                return "FactoryLogistics.NoMachine".Translate();

            List<Need> needs = NeedsOf(network.machine);
            string machine = network.machine.parent.LabelShort;
            string line;
            if (arm.Mode == ArmMode.Feeder)
            {
                line = network.junction != null
                    ? "FactoryLogistics.FeedingFromJunction".Translate(machine, network.loaders.Count).ToString()
                    : "FactoryLogistics.Feeding".Translate(machine, network.loaders.Count).ToString();
                line += "\n" + "FactoryLogistics.FeederPriority".Translate(("FactoryLogistics.Priority_" + arm.Priority).Translate());
            }
            else
                line = "FactoryLogistics.Serving".Translate(machine).ToString();

            if (needs.Count == 0)
                return line + "\n" + "FactoryLogistics.MachineSatisfied".Translate();

            return line + "\n" + NeedsLine(needs);
        }

        public string DescribeFor(Building_BeltJunction junction)
        {
            EnsureBuilt();
            JunctionGroup group;
            if (!byJunction.TryGetValue(junction, out group))
                return null;
            string line = "FactoryLogistics.JunctionSummary".Translate(group.branches.Count, group.loaders.Count);
            List<Need> wanted = Outstanding(group);
            return wanted.Count == 0 ? line : line + "\n" + NeedsLine(wanted);
        }
    }
}
