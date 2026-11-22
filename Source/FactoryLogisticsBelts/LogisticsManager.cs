using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PipeSystem;
using RimWorld;
using UnityEngine;
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
        public Consumer consumer;
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

    // What a Feeder feeds, behind it. Three kinds, tried in this order:
    //   - a VFE Factory machine with an input slot on that cell: it asks for exactly what its
    //     current process still lacks;
    //   - any building that offers the small logistics hook below (the SloppyMods repair centre
    //     does): it says which items it would take and how many it wants queued;
    //   - storage on that cell (a hopper, a shelf, a stockpile): it takes whatever its storage
    //     settings allow, while it has room.
    //
    // The hook is found by name, so no mod has to reference this one:
    //   IntVec3 LogisticsInputCell { get; }   the cell items are delivered to
    //   bool LogisticsAccepts(Thing thing)    would it take this item now
    //   int LogisticsCapacity { get; }        how many items it wants waiting on that cell
    public class Consumer
    {
        public Thing thing;
        public CompAdvancedResourceProcessor vfe;
        public SlotGroup storage;
        public MethodInfo acceptsMethod;
        public PropertyInfo capacityProp;
        public MethodInfo refusalMethod;

        // A material spot (optional hook: IEnumerable<IntVec3> LogisticsResourceCells and
        // List<ThingDefCountClass> LogisticsMaterialNeeds(IntVec3 cell)): the consumer names the
        // exact materials and amounts still missing, and Loaders send exactly that much.
        public MethodInfo materialsMethod;
        public IntVec3 cell;

        // Why it will not take this item, if it can say (optional hook: string LogisticsRefusal(Thing)).
        public string Refusal(Thing item)
        {
            if (refusalMethod != null)
                return refusalMethod.Invoke(thing, new object[] { item }) as string;
            if (storage != null && !storage.parent.Accepts(item))
                return "FactoryLogistics.RefusedByStorage".Translate();
            return null;
        }

        public bool Accepts(Thing item)
        {
            if (acceptsMethod != null)
            {
                object result = acceptsMethod.Invoke(thing, new object[] { item });
                return result is bool && (bool)result;
            }
            if (storage != null)
                return storage.parent.Accepts(item);
            return false;
        }

        public int Capacity
        {
            get
            {
                if (capacityProp != null)
                {
                    object value = capacityProp.GetValue(thing, null);
                    return value is int ? (int)value : 1;
                }
                if (storage != null)
                {
                    int perCell = 1;
                    Building_Storage building = storage.parent as Building_Storage;
                    if (building != null)
                        perCell = UnityEngine.Mathf.Max(1, building.def.building.maxItemsInCell);
                    return storage.CellsList.Count * perCell;
                }
                return 1;
            }
        }
    }

    // What a machine still needs of one ingredient: a specific item, or anything in a category
    // ("any stone chunk").
    public struct Need
    {
        public ThingDef thing;
        public ThingCategoryDef category;
        public List<ThingDef> disallowed;
        public int count;

        // For consumers that judge each item (the repair centre: "gear my filters would take"),
        // a test on the thing itself; counts are then in stacks, not units.
        public System.Predicate<Thing> accepts;
        public bool stacks;
        public string label;
        // What asks for it, for the player: the machine's bill, or its storage settings.
        public string source;

        public bool MatchesThing(Thing t)
        {
            if (t == null)
                return false;
            if (accepts != null)
                return accepts(t);
            return Matches(t.def);
        }

        public int Units(Thing t)
        {
            return stacks ? 1 : t.stackCount;
        }

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
                if (label != null)
                    return label;
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

        // Every run a Loader's belt belongs to. A main line that passes several Feeders belongs to
        // all of their runs, and its Loaders serve all of those machines.
        private readonly Dictionary<Building_LogisticsArm, List<BeltRun>> loaderRuns = new Dictionary<Building_LogisticsArm, List<BeltRun>>();
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
            loaderRuns.Clear();

            List<Building_LogisticsArm> feeders = new List<Building_LogisticsArm>();
            List<Building_LogisticsArm> loaders = new List<Building_LogisticsArm>();
            foreach (Building_LogisticsArm arm in AllOf<Building_LogisticsArm>(map))
            {
                if (arm.Mode == ArmMode.Feeder)
                    feeders.Add(arm);
                else if (arm.Mode == ArmMode.Loader)
                    loaders.Add(arm);
            }
            List<Building_ConveyorLift> allLifts = AllOf<Building_ConveyorLift>(map);
            List<Building_BeltJunction> junctions = AllOf<Building_BeltJunction>(map);

            // Oldest first, so whatever keeps a contested belt is always the same one.
            feeders.SortBy(a => a.thingIDNumber);
            junctions.SortBy(j => j.thingIDNumber);

            Dictionary<Building_Conveyor, List<BeltRun>> claimed = new Dictionary<Building_Conveyor, List<BeltRun>>();

            // Feeders may share belts: several machines along one main line each take what they
            // want off it as it passes.
            foreach (Building_LogisticsArm feeder in feeders)
            {
                LogisticsNetwork network = new LogisticsNetwork();
                network.feeder = feeder;
                CollectBelts(feeder.Position, network, allLifts, feeder.SourceConveyor);
                foreach (Building_Conveyor belt in network.belts)
                    Claim(claimed, belt, network);
                network.junction = JunctionFeeding(network, junctions);
                network.consumer = FindConsumer(feeder);
                networks.Add(network);
                byArm[feeder] = network;
            }

            foreach (Building_BeltJunction junction in junctions)
            {
                JunctionGroup group = new JunctionGroup();
                group.junction = junction;
                CollectBelts(junction.Position, group, allLifts, null);
                List<Building_Conveyor> taken = new List<Building_Conveyor>();
                foreach (Building_Conveyor belt in group.belts)
                {
                    if (claimed.ContainsKey(belt))
                        taken.Add(belt);
                }
                foreach (Building_Conveyor belt in taken)
                    group.belts.Remove(belt);
                foreach (Building_Conveyor belt in group.belts)
                    Claim(claimed, belt, group);

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
                Building_Conveyor belt = loader.OutputConveyor;
                List<BeltRun> runs;
                if (belt != null && claimed.TryGetValue(belt, out runs) && runs.Count > 0)
                {
                    foreach (BeltRun run in runs)
                        run.loaders.Add(loader);
                    loaderRuns[loader] = runs;
                    byArm[loader] = runs[0];
                }
            }
        }

        private static void Claim(Dictionary<Building_Conveyor, List<BeltRun>> claimed, Building_Conveyor belt, BeltRun run)
        {
            List<BeltRun> runs;
            if (!claimed.TryGetValue(belt, out runs))
            {
                runs = new List<BeltRun>();
                claimed[belt] = runs;
            }
            runs.Add(run);
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
        // A Feeder also has a seed: the belt at its gripper, which may run past it rather than end
        // on it - the Feeder takes what it wants off that belt as it goes by.
        private void CollectBelts(IntVec3 target, BeltRun run, List<Building_ConveyorLift> allLifts, Building_Conveyor seed)
        {
            HashSet<Building_Conveyor> belts = run.belts;
            Queue<Building_Conveyor> open = new Queue<Building_Conveyor>();
            if (seed != null && belts.Add(seed))
                open.Enqueue(seed);
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

        private static readonly Dictionary<System.Type, MethodInfo[]> HookCache = new Dictionary<System.Type, MethodInfo[]>();

        // Whatever the Feeder feeds, behind it: see Consumer.
        private Consumer FindConsumer(Building_LogisticsArm feeder)
        {
            IntVec3 slot = feeder.OutputCell;
            if (!slot.InBounds(map))
                return null;
            HashSet<Thing> seen = new HashSet<Thing>();
            Consumer hooked = null;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(slot, MachineSearchRadius, true))
            {
                if (!cell.InBounds(map))
                    continue;

                Building building = cell.GetEdifice(map);
                if (building == null || !seen.Add(building))
                    continue;

                CompAdvancedResourceProcessor processor = building.TryGetComp<CompAdvancedResourceProcessor>();
                if (processor != null && processor.Props != null && processor.Props.processes != null)
                {
                    foreach (ProcessDef process in processor.Props.processes)
                    {
                        if (process == null || process.autoInputSlots == null)
                            continue;

                        foreach (IntVec3 offset in process.autoInputSlots)
                        {
                            if (building.Position + offset.RotatedBy(building.Rotation) == slot)
                            {
                                Consumer vfe = new Consumer();
                                vfe.thing = building;
                                vfe.vfe = processor;
                                return vfe;
                            }
                        }
                    }
                }

                if (hooked == null)
                    hooked = Hooked(building, slot);
            }

            if (hooked != null)
                return hooked;

            SlotGroup group = slot.GetSlotGroup(map);
            if (group != null)
            {
                Consumer storage = new Consumer();
                storage.storage = group;
                Thing parent = group.parent as Thing;
                storage.thing = parent ?? (Thing)feeder;
                return storage;
            }
            return null;
        }

        private static Consumer Hooked(Building building, IntVec3 slot)
        {
            System.Type type = building.GetType();
            MethodInfo[] hook;
            if (!HookCache.TryGetValue(type, out hook))
            {
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
                PropertyInfo cellProp = type.GetProperty("LogisticsInputCell", flags);
                MethodInfo accepts = type.GetMethod("LogisticsAccepts", flags, null, new[] { typeof(Thing) }, null);
                PropertyInfo capacity = type.GetProperty("LogisticsCapacity", flags);
                PropertyInfo resourceCells = type.GetProperty("LogisticsResourceCells", flags);
                MethodInfo materials = type.GetMethod("LogisticsMaterialNeeds", flags, null, new[] { typeof(IntVec3) }, null);
                bool item = cellProp != null && accepts != null && cellProp.PropertyType == typeof(IntVec3);
                bool mats = resourceCells != null && materials != null
                    && typeof(IEnumerable<IntVec3>).IsAssignableFrom(resourceCells.PropertyType)
                    && typeof(List<ThingDefCountClass>).IsAssignableFrom(materials.ReturnType);
                hook = item || mats
                    ? new[]
                    {
                        item ? cellProp.GetGetMethod() : null,
                        item ? accepts : null,
                        item && capacity != null ? capacity.GetGetMethod() : null,
                        mats ? resourceCells.GetGetMethod() : null,
                        mats ? materials : null,
                    }
                    : null;
                HookCache[type] = hook;
            }
            if (hook == null)
                return null;

            if (hook[0] != null && (IntVec3)hook[0].Invoke(building, null) == slot)
            {
                Consumer consumer = new Consumer();
                consumer.thing = building;
                consumer.cell = slot;
                consumer.acceptsMethod = hook[1];
                consumer.capacityProp = hook[2] != null ? type.GetProperty("LogisticsCapacity", BindingFlags.Instance | BindingFlags.Public) : null;
                consumer.refusalMethod = type.GetMethod("LogisticsRefusal", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Thing) }, null);
                return consumer;
            }

            if (hook[3] != null)
            {
                IEnumerable<IntVec3> cells = hook[3].Invoke(building, null) as IEnumerable<IntVec3>;
                if (cells != null)
                {
                    foreach (IntVec3 c in cells)
                    {
                        if (c != slot)
                            continue;
                        Consumer consumer = new Consumer();
                        consumer.thing = building;
                        consumer.cell = slot;
                        consumer.materialsMethod = hook[4];
                        return consumer;
                    }
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ demand

        // What a consumer still wants, before counting what is on its way.
        public static List<Need> NeedsOf(Consumer consumer)
        {
            if (consumer == null)
                return new List<Need>();
            if (consumer.vfe != null)
                return NeedsOf(consumer.vfe);

            if (consumer.materialsMethod != null)
            {
                List<Need> wanted = new List<Need>();
                List<ThingDefCountClass> list = consumer.materialsMethod.Invoke(consumer.thing, new object[] { consumer.cell }) as List<ThingDefCountClass>;
                if (list != null)
                {
                    foreach (ThingDefCountClass entry in list)
                    {
                        if (entry == null || entry.thingDef == null || entry.count <= 0)
                            continue;
                        Need material = new Need();
                        material.thing = entry.thingDef;
                        material.count = entry.count;
                        material.source = "FactoryLogistics.NeedSourceMaterials".Translate().ToString();
                        wanted.Add(material);
                    }
                }
                return wanted;
            }

            Need need = new Need();
            need.accepts = consumer.Accepts;
            need.stacks = true;
            need.count = consumer.Capacity;
            need.label = "FactoryLogistics.NeedAccepted".Translate(consumer.thing.LabelShort);
            need.source = "FactoryLogistics.NeedSourceStorage".Translate().ToString();
            List<Need> needs = new List<Need>();
            if (need.count > 0)
                needs.Add(need);
            return needs;
        }

        // What the machine's current process still lacks. Pipe-net ingredients (fuel, chemfuel
        // through pipes) are not belt cargo and are left out.
        public static List<Need> NeedsOf(CompAdvancedResourceProcessor machine)
        {
            List<Need> needs = new List<Need>();
            Process process = machine != null ? machine.Process : null;
            if (process == null)
                return IdleForeverNeeds(machine);
            List<ThingAndResourceOwner> owners = process.IngredientsOwners;
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
                need.source = process.Def != null ? "FactoryLogistics.NeedSourceBill".Translate(process.Def.LabelCap).ToString() : null;
                needs.Add(need);
            }

            return needs;
        }

        private static FieldInfo suspendedField;

        private static bool IsSuspended(Process process)
        {
            if (suspendedField == null)
                suspendedField = typeof(Process).GetField("suspended", BindingFlags.Instance | BindingFlags.NonPublic);
            if (suspendedField == null)
                return false;
            return (bool)suspendedField.GetValue(process);
        }

        // VFE runs a "Forever" hopper bill only once its ingredient is already sitting in the input
        // hopper; until then the machine reports no current process, so it looks like it needs
        // nothing and nobody brings the ingredient: a deadlock (e.g. the masonry saw waiting for a
        // chunk). For such an idle machine, ask for one batch of the first Forever bill's ingredients.
        private static List<Need> IdleForeverNeeds(CompAdvancedResourceProcessor machine)
        {
            List<Need> needs = new List<Need>();
            ProcessStack stack = machine != null ? machine.ProcessStack : null;
            List<Process> processes = stack != null ? stack.Processes : null;
            if (processes == null)
                return needs;

            foreach (Process candidate in processes)
            {
                if (candidate == null || candidate.Def == null || candidate.repeatMode != BillRepeatModeDefOf.Forever || IsSuspended(candidate))
                    continue;
                ProcessDef def = candidate.Def;
                if (!def.onlyGrabAndOutputToFactoryHoppers || def.autoInputSlots.NullOrEmpty() || def.ingredients.NullOrEmpty())
                    continue;

                foreach (ProcessDef.Ingredient ingredient in def.ingredients)
                {
                    if (ingredient == null || ingredient.pipeNet != null)
                        continue;
                    if (ingredient.thing == null && ingredient.thingCategory == null)
                        continue;
                    Need need = new Need();
                    need.thing = ingredient.thing;
                    need.category = ingredient.thingCategory;
                    need.count = Mathf.Max(1, Mathf.CeilToInt(ingredient.countNeeded));
                    need.disallowed = ingredient.thingCategory != null ? ingredient.disallowedThingDefs : null;
                    need.source = "FactoryLogistics.NeedSourceForever".Translate(def.LabelCap).ToString();
                    needs.Add(need);
                }
                if (needs.Count > 0)
                    return needs;
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
                if (thing.def.category == ThingCategory.Item && need.MatchesThing(thing))
                    count += need.Units(thing);
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
                if (need.MatchesThing(thing))
                    count += need.Units(thing);
            }

            return count;
        }

        // On a run's belts, lying on its belt cells waiting to be picked up, or waiting on its lifts.
        private int CountOnRun(BeltRun run, Need need)
        {
            int count = 0;
            foreach (Building_LogisticsArm loader in run.loaders)
            {
                Thing carried = loader.Carried;
                if (carried != null && need.MatchesThing(carried))
                    count += need.Units(carried);
            }
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
            int count = CountOnRun(network, need) + CountOn(network.feeder.Position, need) + CountOn(network.feeder.OutputCell, need);
            Thing carried = network.feeder.Carried;
            if (carried != null && need.MatchesThing(carried))
                count += need.Units(carried);
            return count;
        }

        // What a Feeder's machine still lacks once everything already on its way is counted.
        private List<Need> Outstanding(LogisticsNetwork network)
        {
            List<Need> result = new List<Need>();
            if (network.consumer == null || network.pausedBy != null)
                return result;
            foreach (Need need in NeedsOf(network.consumer))
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
            foreach (Building_LogisticsArm loader in group.loaders)
            {
                if (loader.Carried != null)
                    trunkItems.Add(loader.Carried);
            }

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
                    if (have <= 0 || !need.MatchesThing(thing))
                        continue;
                    if (need.stacks)
                        have = 1;
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
            loader.returnNote = null;

            // A belt that ends at the Loader drops its items onto the Loader's own cell. Those are
            // passed straight on to the belt behind it, whatever they are: the line they came on
            // has nowhere else to go (a side line's tail returning its excess to a main line).
            Building_Conveyor passOn = loader.OutputConveyor;
            if (passOn != null)
            {
                List<Thing> dropped = new List<Thing>();
                AddLoose(loader.Position, dropped);
                if (dropped.Count > 0)
                {
                    Thing first = dropped[0];
                    if (passOn.HasSpace() && passOn.state != Building_Conveyor.ConveyorState.Moving && passOn.CanAcceptItem(first))
                    {
                        string label = first.LabelCap;
                        loader.Pick(first);
                        loader.status = "FactoryLogistics.LoaderPassedOn".Translate(label);
                    }
                    else
                    {
                        loader.status = "FactoryLogistics.LoaderBeltBusy".Translate();
                    }
                    return;
                }
            }

            List<BeltRun> runs;
            if (!loaderRuns.TryGetValue(loader, out runs))
            {
                loader.status = loader.OutputConveyor == null
                    ? "FactoryLogistics.LoaderNoBelt".Translate().ToString()
                    : "FactoryLogistics.LoaderNoFeeder".Translate().ToString();
                return;
            }

            Building_Conveyor belt = loader.OutputConveyor;
            if (belt == null)
                return;

            // A leftover passing behind it that no machine on the network needs any more goes
            // back into its storage, if the storage takes it.
            if (loader.ReturnLeftovers && TryReturnLeftover(loader, belt, runs))
                return;

            // One stack at a time onto an empty belt cell.
            if (!belt.HasSpace() || belt.state == Building_Conveyor.ConveyorState.Moving)
            {
                loader.status = "FactoryLogistics.LoaderBeltBusy".Translate();
                return;
            }

            // What every machine this belt reaches still lacks, highest Feeder priority first.
            List<Need> outstanding = new List<Need>();
            List<string> targets = new List<string>();
            LogisticsNetwork network = null;
            List<BeltRun> ordered = new List<BeltRun>(runs);
            ordered.SortByDescending(r => r is LogisticsNetwork ? ((LogisticsNetwork)r).feeder.Priority : 2);
            foreach (BeltRun run in ordered)
            {
                LogisticsNetwork n = run as LogisticsNetwork;
                JunctionGroup g = run as JunctionGroup;
                List<Need> part = n != null ? Outstanding(n) : Outstanding(g);
                foreach (Need need in part)
                {
                    outstanding.Add(need);
                    targets.Add(n != null && n.consumer != null ? n.consumer.thing.LabelShort : (g != null ? g.junction.LabelShort : "?"));
                }
                if (network == null && n != null && n.consumer != null)
                    network = n;
            }

            if (network == null && !(runs[0] is JunctionGroup))
            {
                loader.status = "FactoryLogistics.NoMachine".Translate();
                return;
            }

            if (outstanding.Count == 0)
            {
                loader.status = "FactoryLogistics.MachineCovered".Translate();
                return;
            }

            string missing = null;
            foreach (Need need in outstanding)
            {
                Thing source = null;
                Building_Conveyor sourceBelt = loader.SourceConveyor;
                foreach (Thing thing in loader.SourceThings())
                {
                    if (!need.MatchesThing(thing))
                        continue;
                    // Off a passing belt: only what its own network needs (that is what "need"
                    // is), and anything else rides on.
                    if (loader.CanTakeFromSource(thing))
                    {
                        source = thing;
                        break;
                    }
                }

                if (source == null)
                {
                    // Nothing more of it here yet: what has been sent stays sent, and this Loader
                    // keeps looking each time it is free, so the rest goes as it turns up.
                    if (missing == null)
                        missing = need.count + " " + need.Label;
                    continue;
                }

                int take = need.stacks ? source.stackCount : UnityEngine.Mathf.Min(need.count, source.stackCount);
                Thing sent;
                if (sourceBelt != null && source.holdingOwner == sourceBelt.innerContainer)
                {
                    if (take >= source.stackCount)
                    {
                        sourceBelt.innerContainer.Remove(source);
                        sent = source;
                    }
                    else
                    {
                        sent = source.SplitOff(take);
                    }
                    if (sourceBelt.innerContainer.Count == 0)
                    {
                        sourceBelt.state = Building_Conveyor.ConveyorState.Empty;
                        sourceBelt.itemProgress = 0f;
                    }
                }
                else
                {
                    sent = source.SplitOff(take);
                }
                loader.Pick(sent);

                loader.status = "FactoryLogistics.LoaderSent".Translate(sent.LabelCap, targets[outstanding.IndexOf(need)]);
                return;
            }

            loader.status = ExplainNoStock(loader, runs, missing);
        }

        // Why a Loader found nothing to send: an empty source, or what the machine says about the
        // first thing in it, so the player can see which setting to change.
        private string ExplainNoStock(Building_LogisticsArm loader, List<BeltRun> runs, string missing)
        {
            if (loader.SourceConveyor != null)
                return "FactoryLogistics.LoaderWatchingBelt".Translate(missing ?? "?");

            List<Thing> stock = loader.SourceThings().ToList();
            if (stock.Count == 0)
                return "FactoryLogistics.LoaderSourceEmpty".Translate();

            // Machines on its belt that judge each item (the repair centre) can say why they will
            // not take something: list a few different items with their reasons, so the one the
            // player is wondering about is likely among them.
            List<Consumer> judges = new List<Consumer>();
            if (runs != null)
            {
                foreach (BeltRun run in runs)
                {
                    LogisticsNetwork n = run as LogisticsNetwork;
                    if (n != null && n.consumer != null && n.consumer.vfe == null && !judges.Contains(n.consumer))
                        judges.Add(n.consumer);
                }
            }

            string line = "FactoryLogistics.LoaderNoStock".Translate(missing ?? "?");
            if (judges.Count == 0)
                return line;

            List<string> reasons = new List<string>();
            HashSet<ThingDef> seen = new HashSet<ThingDef>();
            foreach (Thing thing in stock)
            {
                if (reasons.Count >= 6 || !seen.Add(thing.def))
                    continue;
                string why;
                if (!loader.CanTakeFromSource(thing))
                    why = "FactoryLogistics.ForbiddenItem".Translate();
                else
                {
                    why = null;
                    foreach (Consumer judge in judges)
                    {
                        string r = judge.Refusal(thing);
                        if (r.NullOrEmpty() && judge.Accepts(thing))
                        {
                            why = "FactoryLogistics.WouldTake".Translate(judge.thing.LabelShort);
                            break;
                        }
                        if (why == null && !r.NullOrEmpty())
                            why = judge.thing.LabelShort + ": " + r;
                    }
                }
                if (!why.NullOrEmpty())
                    reasons.Add(thing.LabelShort + " - " + why);
            }
            return reasons.Count == 0 ? line : line + "\n" + string.Join("\n", reasons.ToArray());
        }

        // Lifts the first surplus item on the belt behind the Loader and starts swinging it back to
        // a free cell of the storage at its gripper.
        private bool TryReturnLeftover(Building_LogisticsArm loader, Building_Conveyor belt, List<BeltRun> runs)
        {
            if (belt.innerContainer == null || belt.innerContainer.Count == 0)
                return false;
            // Back to where it came from: the storage at its gripper (the whole row of shelves),
            // or the belt at its gripper (a Loader feeding a side line from a main line puts the
            // side line's surplus back onto the main line).
            Building_Conveyor sourceBelt = loader.SourceConveyor;
            // Never back onto a belt that ends at this Loader: it would only come straight back.
            if (sourceBelt != null && sourceBelt.ForwardCell == loader.Position)
                return false;
            List<SlotGroup> storage = sourceBelt == null ? loader.SourceStorage().ToList() : new List<SlotGroup>();
            if (storage.Count == 0 && sourceBelt == null)
                return false;

            string note = null;
            foreach (Thing thing in belt.innerContainer.InnerListForReading.ToArray())
            {
                string wantedBy;
                if (!IsSurplus(thing, runs, out wantedBy))
                {
                    note = note ?? "FactoryLogistics.LoaderKeepWanted".Translate(thing.LabelShort, wantedBy).ToString();
                    continue;
                }
                if (sourceBelt != null)
                {
                    if (!sourceBelt.HasSpace() || !sourceBelt.CanAcceptItem(thing))
                    {
                        note = note ?? "FactoryLogistics.LoaderKeepBeltFull".Translate(thing.LabelShort).ToString();
                        continue;
                    }
                }
                else if (!storage.Any(g => g.parent.Accepts(thing)))
                {
                    note = note ?? "FactoryLogistics.LoaderKeepNotAccepted".Translate(thing.LabelShort).ToString();
                    continue;
                }
                else if (!ReturnCellFor(loader, thing).IsValid)
                {
                    note = note ?? "FactoryLogistics.LoaderKeepFull".Translate(thing.LabelShort).ToString();
                    continue;
                }

                string label = thing.LabelCap;
                belt.innerContainer.Remove(thing);
                if (belt.innerContainer.Count == 0)
                {
                    belt.state = Building_Conveyor.ConveyorState.Empty;
                    belt.itemProgress = 0f;
                }
                loader.PickReturn(thing);
                loader.status = "FactoryLogistics.LoaderReturned".Translate(label);
                return true;
            }
            loader.returnNote = note;
            return false;
        }

        // A free cell anywhere in the storage at the arm's gripper that takes this item, nearest
        // the gripper first, or Invalid.
        private IntVec3 ReturnCellFor(Building_LogisticsArm arm, Thing thing)
        {
            IntVec3 best = IntVec3.Invalid;
            int bestDist = int.MaxValue;
            foreach (SlotGroup group in arm.SourceStorage())
            {
                if (!group.parent.Accepts(thing))
                    continue;
                IntVec3 cell = StoreCellFor(group, thing);
                if (!cell.IsValid)
                    continue;
                int d = cell.DistanceToSquared(arm.SourceCell);
                if (d < bestDist)
                {
                    best = cell;
                    bestDist = d;
                }
            }
            return best;
        }

        // A cell of the storage that can take this item now, or Invalid.
        private IntVec3 StoreCellFor(SlotGroup group, Thing thing)
        {
            List<IntVec3> cells = group.CellsList;
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].IsValidStorageFor(map, thing))
                    return cells[i];
            }
            return IntVec3.Invalid;
        }

        // No machine on these runs needs this item: nothing it matches still lacks it once
        // everything already on its way - this item included - is counted.
        private bool IsSurplus(Thing thing, List<BeltRun> runs)
        {
            string wantedBy;
            return IsSurplus(thing, runs, out wantedBy);
        }

        private bool IsSurplus(Thing thing, List<BeltRun> runs, out string wantedBy)
        {
            wantedBy = null;
            foreach (BeltRun run in runs)
            {
                List<LogisticsNetwork> networks = new List<LogisticsNetwork>();
                LogisticsNetwork single = run as LogisticsNetwork;
                if (single != null)
                    networks.Add(single);
                JunctionGroup group = run as JunctionGroup;
                if (group != null)
                    networks.AddRange(group.branches);

                foreach (LogisticsNetwork network in networks)
                {
                    if (network.consumer == null)
                        continue;
                    foreach (Need need in NeedsOf(network.consumer))
                    {
                        if (!need.MatchesThing(thing))
                            continue;
                        int inFlight = CountInFlight(network, need);
                        int excess = inFlight - need.count;
                        if (excess < need.Units(thing))
                        {
                            Thing machine = network.consumer.thing;
                            string who = machine != null ? machine.LabelShort : network.feeder.LabelShort;
                            int missing = Mathf.Max(1, need.count - inFlight + need.Units(thing));
                            wantedBy = need.stacks
                                ? "FactoryLogistics.WantedByStorage".Translate(who, missing).ToString()
                                : "FactoryLogistics.WantedByDetail".Translate(who, need.source ?? "?", need.Label, missing, need.count).ToString();
                            return false;
                        }
                    }
                }
            }
            return true;
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

            bool hasMachine = network != null && network.consumer != null;
            if (arrived.Count == 0)
            {
                if (!hasMachine)
                    feeder.status = "FactoryLogistics.NoMachine".Translate().ToString();
                else if (!TryGrabFromBelt(feeder, network))
                    feeder.status = null;
                return;
            }

            List<Need> needs = hasMachine ? NeedsOf(network.consumer) : new List<Need>();
            foreach (Thing thing in arrived)
            {
                // Still wanted once what is already in the input slot is counted? Into the slot.
                bool wanted = false;
                foreach (Need need in needs)
                {
                    if (need.MatchesThing(thing) && need.count - CountOn(feeder.OutputCell, need) > 0)
                    {
                        wanted = true;
                        break;
                    }
                }

                if (wanted)
                {
                    // Only pick it up if it has somewhere to go; taking it and putting it back
                    // whenever the machine's spot was occupied made it flicker back and forth.
                    if (CanDeliver(feeder, thing))
                    {
                        string picked = thing.LabelCap;
                        feeder.Pick(thing);
                        feeder.status = "FactoryLogistics.FeederDelivered".Translate(picked);
                        return;     // one item per swing
                    }
                    feeder.status = "FactoryLogistics.FeederSlotFull".Translate();   // the belt behind waits too
                    continue;
                }

                // Nothing on this network wants it any more - the process finished or changed, or
                // it reached the belt some other way. Off the belt it goes, so it never jams.
                string label = thing.LabelCap;
                if (DropStray(feeder.Position, feeder.OutputCell, thing))
                    feeder.status = "FactoryLogistics.FeederDropped".Translate(label);
            }
        }

        // The belt at the Feeder's gripper, passing by: lift off it the first item the machine still
        // wants and has room for. Anything else rides on.
        private bool TryGrabFromBelt(Building_LogisticsArm feeder, LogisticsNetwork network)
        {
            Building_Conveyor belt = feeder.SourceConveyor;
            if (belt == null || belt.innerContainer == null || belt.innerContainer.Count == 0)
                return false;

            List<Need> needs = NeedsOf(network.consumer);
            foreach (Thing thing in belt.innerContainer.InnerListForReading.ToArray())
            {
                bool wanted = false;
                foreach (Need need in needs)
                {
                    if (need.MatchesThing(thing) && need.count - CountOn(feeder.OutputCell, need) > 0)
                    {
                        wanted = true;
                        break;
                    }
                }
                if (!wanted)
                    continue;
                if (!CanDeliver(feeder, thing))
                {
                    feeder.status = "FactoryLogistics.FeederSlotFull".Translate();
                    return true;
                }

                string label = thing.LabelCap;
                belt.innerContainer.Remove(thing);
                if (belt.innerContainer.Count == 0)
                {
                    belt.state = Building_Conveyor.ConveyorState.Empty;
                    belt.itemProgress = 0f;
                }
                feeder.Pick(thing);
                feeder.status = "FactoryLogistics.FeederDelivered".Translate(label);
                return true;
            }
            return false;
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
                        if (need.MatchesThing(thing))
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
                            branch.consumer != null ? branch.consumer.thing.LabelShort : branch.feeder.LabelShort);
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

        // Whether the machine input behind the Feeder (its conveyor, hopper, spot or storage) has
        // room for this item right now.
        private bool CanDeliver(Building_LogisticsArm feeder, Thing thing)
        {
            Building_Conveyor slotBelt = feeder.OutputConveyor;
            if (slotBelt != null)
                return slotBelt.HasSpace() && slotBelt.CanAcceptItem(thing);
            // A Mover putting into storage keeps to the storage's settings; a Feeder serves a
            // machine that has already said it wants the item.
            if (feeder.Mode == ArmMode.Mover)
            {
                SlotGroup group = feeder.OutputCell.InBounds(map) ? feeder.OutputCell.GetSlotGroup(map) : null;
                if (group != null && !group.parent.Accepts(thing))
                    return false;
            }
            return HasRoomOnCell(feeder.OutputCell, thing);
        }

        // A Mover: anything at its gripper that its filter allows goes to the other side - what a
        // belt drops on its own cell first, then what the gripper reaches (a passing belt, storage,
        // or whatever lies there) - as long as there is room.
        public void WorkMover(Building_LogisticsArm mover)
        {
            List<Thing> candidates = new List<Thing>();
            AddLoose(mover.Position, candidates);
            Building_Conveyor sourceBelt = mover.SourceConveyor;
            foreach (Thing thing in mover.SourceThings())
                candidates.Add(thing);

            ThingFilter filter = mover.MoveFilter;
            bool anyAllowed = false;
            foreach (Thing thing in candidates)
            {
                if (!filter.Allows(thing))
                    continue;
                bool onBelt = sourceBelt != null && thing.holdingOwner == sourceBelt.innerContainer;
                bool onOwnCell = thing.Spawned && thing.Position == mover.Position;
                if (!onBelt && !onOwnCell && !mover.CanTakeFromSource(thing))
                    continue;
                anyAllowed = true;
                if (!CanDeliver(mover, thing))
                    continue;

                string label = thing.LabelCap;
                if (onBelt)
                {
                    sourceBelt.innerContainer.Remove(thing);
                    if (sourceBelt.innerContainer.Count == 0)
                    {
                        sourceBelt.state = Building_Conveyor.ConveyorState.Empty;
                        sourceBelt.itemProgress = 0f;
                    }
                }
                mover.Pick(thing);
                mover.status = "FactoryLogistics.MoverMoved".Translate(label);
                return;
            }

            mover.status = anyAllowed
                ? "FactoryLogistics.MoverNoRoom".Translate().ToString()
                : "FactoryLogistics.MoverNothing".Translate().ToString();
        }

        // Puts a carried (unspawned) item into the machine input behind the Feeder.
        private bool Deliver(Building_LogisticsArm feeder, Thing thing)
        {
            if (!CanDeliver(feeder, thing))
                return false;
            Building_Conveyor slotBelt = feeder.OutputConveyor;
            if (slotBelt != null)
                return slotBelt.TryAdd(thing);

            // A part-full stack on the spot takes what fits; the remainder stays in the gripper
            // (Release reports false, so the arm holds it and tries again).
            int room = RoomInStackFor(feeder.OutputCell, thing);
            if (room > 0 && room < thing.stackCount)
            {
                Thing part = thing.SplitOff(room);
                if (!GenPlace.TryPlaceThing(part, feeder.OutputCell, map, ThingPlaceMode.Direct))
                    thing.TryAbsorbStack(part, false);
                return false;
            }
            return GenPlace.TryPlaceThing(thing, feeder.OutputCell, map, ThingPlaceMode.Direct);
        }

        /// <summary>
        /// The end of an arm's swing: set the carried item down on the output side - onto the belt
        /// for a Loader, into the machine input for a Feeder. False if there is no room yet.
        /// </summary>
        public bool Release(Building_LogisticsArm arm, Thing thing)
        {
            if (arm.Returning)
            {
                Building_Conveyor back = arm.SourceConveyor;
                if (back != null)
                    return back.HasSpace() && back.CanAcceptItem(thing) && back.TryAdd(thing);
                IntVec3 cell = ReturnCellFor(arm, thing);
                return cell.IsValid && GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Direct);
            }
            if (arm.Mode == ArmMode.Feeder || arm.Mode == ArmMode.Mover)
                return Deliver(arm, thing);
            Building_Conveyor belt = arm.OutputConveyor;
            if (belt == null || !belt.HasSpace() || !belt.CanAcceptItem(thing))
                return false;
            return belt.TryAdd(thing);
        }

        // Room left in a stack of the same item already on the cell (0 if there is none).
        private int RoomInStackFor(IntVec3 cell, Thing thing)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing other = things[i];
                if (other.def.category == ThingCategory.Item && other.CanStackWith(thing))
                    return Mathf.Max(0, other.def.stackLimit - other.stackCount);
            }
            return 0;
        }

        // Whether this item can be set down on the cell as it is now: it tops up a stack already
        // there, or the cell holds fewer items than it allows (one on bare floor; more in storage).
        private bool HasRoomOnCell(IntVec3 cell, Thing thing)
        {
            if (!cell.InBounds(map) || !cell.Walkable(map))
                return false;
            int items = 0;
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing other = things[i];
                if (other.def.category != ThingCategory.Item)
                    continue;
                items++;
                // Topping up a part-full stack counts, even if only some of it fits: the rest is
                // held in the arm until there is room.
                if (other.CanStackWith(thing) && other.stackCount < other.def.stackLimit)
                    return true;
            }
            return items < cell.GetMaxItemsAllowedInCell(map);
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
                return network.consumer != null ? network.consumer.thing : null;
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
                result.Add(branch.consumer != null ? branch.consumer.thing : branch.feeder);
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

            List<BeltRun> shared;
            if (arm.Mode == ArmMode.Loader && loaderRuns.TryGetValue(arm, out shared) && shared.Count > 1)
            {
                List<Need> all = new List<Need>();
                foreach (BeltRun r in shared)
                    all.AddRange(r is LogisticsNetwork ? Outstanding((LogisticsNetwork)r) : Outstanding((JunctionGroup)r));
                string head = "FactoryLogistics.ServingMany".Translate(shared.Count);
                return all.Count == 0 ? head : head + "\n" + NeedsLine(all);
            }

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

            if (network.consumer == null)
                return "FactoryLogistics.NoMachine".Translate();

            List<Need> needs = NeedsOf(network.consumer);
            string machine = network.consumer.thing.LabelShort;
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
