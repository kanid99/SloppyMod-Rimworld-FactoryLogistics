using System.Collections.Generic;
using System.Text;
using PipeSystem;
using RimWorld;
using UnityEngine;
using Verse;
using Vehicles;

namespace FactoryLogistics.VehicleDock
{
    public enum DockMode : byte
    {
        Load,       // capsules sent here go into the parked vehicle's cargo
        Unload,     // the parked vehicle's cargo goes out in capsules to a chosen receiver
    }

    /// <summary>
    /// A flat pad on the tube network that a Vehicle Framework vehicle parks over.
    ///
    /// Loading, it is a tube receiver whose room is the parked vehicle's: its free cargo weight,
    /// less what is already in flight to it, counted in capsules. With nothing parked it has no
    /// room, so senders wait - except for capsules already on their way, which wait on the pad
    /// and go into the next vehicle that parks.
    ///
    /// Unloading, it sends the vehicle's cargo out one capsule at a time to the receiver it is
    /// pointed at, using an empty capsule shell from its own storage for each.
    ///
    /// It works only on the player's vehicles that have stopped, and never touches passengers.
    /// </summary>
    public class Building_VehicleDock : Building_TubeReceiver
    {
        private const int WorkInterval = 60;
        private const int ShellsWanted = 10;

        private DockMode mode = DockMode.Load;
        private Building_TubeReceiver destination;
        private ThingFilter unloadFilter;

        private VehiclePawn parked;
        private int parkedCheckedAt = -99999;
        public string status;

        public DockMode Mode
        {
            get { return mode; }
        }

        public ThingFilter UnloadFilter
        {
            get
            {
                if (unloadFilter == null)
                {
                    unloadFilter = new ThingFilter();
                    unloadFilter.SetAllow(ThingCategoryDefOf.Root, true);
                }
                return unloadFilter;
            }
        }

        public Building_TubeReceiver Destination
        {
            get
            {
                if (destination != null && (destination.Destroyed || !destination.Spawned || destination == this))
                    destination = null;
                return destination;
            }
            set { destination = value; }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref mode, "dockMode", DockMode.Load);
            Scribe_References.Look(ref destination, "dockDestination");
            Scribe_Deep.Look(ref unloadFilter, "unloadFilter");
        }

        // ------------------------------------------------------------------ the vehicle

        /// <summary>The player's vehicle standing over the pad and not moving, or null.</summary>
        public VehiclePawn Parked
        {
            get
            {
                int now = Find.TickManager.TicksGame;
                if (now - parkedCheckedAt >= 30 || now < parkedCheckedAt)
                {
                    parkedCheckedAt = now;
                    parked = FindParked();
                }
                return parked;
            }
        }

        private VehiclePawn FindParked()
        {
            if (!Spawned)
                return null;
            IReadOnlyList<Pawn> pawns = Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                VehiclePawn vehicle = pawns[i] as VehiclePawn;
                if (vehicle == null || vehicle.Faction != Faction.OfPlayer || vehicle.Dead)
                    continue;
                if (!vehicle.OccupiedRect().Overlaps(this.OccupiedRect()))
                    continue;
                if (vehicle.vehiclePather != null && vehicle.vehiclePather.Moving)
                    continue;
                return vehicle;
            }
            return null;
        }

        private static float FreeCargo(VehiclePawn vehicle)
        {
            float capacity = vehicle.GetStatValue(VehicleStatDefOf.CargoCapacity);
            return capacity - MassUtility.InventoryMass(vehicle);
        }

        private float TransitMass()
        {
            float mass = 0f;
            ThingOwner<Thing> transit = Transit;
            for (int i = 0; i < transit.Count; i++)
            {
                TubeCapsule capsule = transit[i] as TubeCapsule;
                if (capsule != null)
                    mass += capsule.ContentsMass;
            }
            return mass;
        }

        /// <summary>Cargo weight the parked vehicle can still take once everything in flight lands.</summary>
        private float RoomLeft()
        {
            VehiclePawn vehicle = Parked;
            if (vehicle == null || mode != DockMode.Load)
                return 0f;
            return Mathf.Max(0f, FreeCargo(vehicle) - TransitMass());
        }

        // ------------------------------------------------------------------ receiving

        // The capsules the pad can still take, as the player sees it: weight left over the
        // capsule weight limit. Senders ask HasRoomFor, which counts real weight.
        public override int FreeSlots
        {
            get
            {
                float limit = TubeUtility.CapsuleMassLimit;
                if (limit <= 0f)
                    return RoomLeft() > 0f ? 1 : 0;
                return Mathf.FloorToInt(RoomLeft() / limit);
            }
        }

        public override int Capacity
        {
            get { return FreeSlots + InTransitCount; }
        }

        public override bool HasRoomFor(TubeCapsule capsule)
        {
            float needed = capsule != null ? capsule.ContentsMass : 0.01f;
            return RoomLeft() >= Mathf.Max(0.01f, needed);
        }

        protected override void SetDown(Thing thing)
        {
            TubeCapsule capsule = thing as TubeCapsule;
            VehiclePawn vehicle = Parked;
            if (capsule != null && vehicle != null && mode == DockMode.Load
                && FreeCargo(vehicle) >= capsule.ContentsMass)
            {
                Transit.Remove(capsule);
                EmptyInto(capsule, vehicle);
                return;
            }
            base.SetDown(thing);
        }

        // Contents into the vehicle's cargo, the empty shell onto the pad for haulers.
        private void EmptyInto(TubeCapsule capsule, VehiclePawn vehicle)
        {
            ThingOwner contents = capsule.Contents;
            while (contents.Count > 0)
            {
                Thing item = contents[0];
                contents.Remove(item);
                int count = item.stackCount;
                int moved = vehicle.AddOrTransfer(item, count);
                if (moved < count && !item.Destroyed && item.holdingOwner == null && !item.Spawned)
                    GenPlace.TryPlaceThing(item, Position, Map, ThingPlaceMode.Near);
            }

            if (capsule.Spawned)
                capsule.DeSpawn();
            capsule.Destroy(DestroyMode.Vanish);
            Thing shell = ThingMaker.MakeThing(TubeDefOf.FL_CapsuleShell);
            GenPlace.TryPlaceThing(shell, Position, Map, ThingPlaceMode.Near);
            FleckMaker.ThrowAirPuffUp(DrawPos, Map);
            status = "FactoryLogistics.DockLoaded".Translate(vehicle.LabelShort);
        }

        // Delivered capsules sitting on the pad, and empty shells while unloading.
        public override bool AcceptsThing(Thing thing)
        {
            if (thing.def == TubeDefOf.FL_CapsuleShell)
                return mode == DockMode.Unload && (HoldsInItsCells(thing) || ShellCount() < ShellsWanted);
            return base.AcceptsThing(thing);
        }

        private int ShellCount()
        {
            int count = 0;
            List<IntVec3> cells = AllSlotCellsList();
            for (int i = 0; i < cells.Count; i++)
            {
                List<Thing> things = Map.thingGrid.ThingsListAt(cells[i]);
                for (int j = 0; j < things.Count; j++)
                {
                    if (things[j].def == TubeDefOf.FL_CapsuleShell)
                        count += things[j].stackCount;
                }
            }
            return count;
        }

        private Thing FirstShell()
        {
            List<IntVec3> cells = AllSlotCellsList();
            for (int i = 0; i < cells.Count; i++)
            {
                List<Thing> things = Map.thingGrid.ThingsListAt(cells[i]);
                for (int j = 0; j < things.Count; j++)
                {
                    if (things[j].def == TubeDefOf.FL_CapsuleShell)
                        return things[j];
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ work

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned || !this.IsHashIntervalTick(WorkInterval))
                return;

            VehiclePawn vehicle = Parked;
            if (vehicle == null)
            {
                status = "FactoryLogistics.DockEmpty".Translate();
                return;
            }

            if (mode == DockMode.Load)
                LoadWaiting(vehicle);
            else
                UnloadOne(vehicle);
        }

        // Capsules that arrived while nothing was parked go into this vehicle now.
        private void LoadWaiting(VehiclePawn vehicle)
        {
            TubeCapsule waiting = FirstHeldCapsule(true);
            if (waiting == null)
            {
                status = "FactoryLogistics.DockReady".Translate(vehicle.LabelShort, FreeCargo(vehicle).ToString("0.#"));
                return;
            }
            if (FreeCargo(vehicle) < waiting.ContentsMass)
            {
                status = "FactoryLogistics.DockVehicleFull".Translate(vehicle.LabelShort);
                return;
            }
            EmptyInto(waiting, vehicle);
        }

        private void UnloadOne(VehiclePawn vehicle)
        {
            // Being packed for a caravan: leave its cargo alone.
            if (vehicle.cargoToLoad != null && vehicle.cargoToLoad.Count > 0)
            {
                status = "FactoryLogistics.DockVehiclePacking".Translate(vehicle.LabelShort);
                return;
            }

            Building_TubeReceiver target = Destination;
            if (target == null)
            {
                status = Block("FactoryLogistics.BlockNoDestination");
                return;
            }
            PipeNet net = Net;
            if (!AirOn || net == null)
            {
                status = Block("FactoryLogistics.BlockNoAir");
                return;
            }
            if (net != target.Net)
            {
                status = Block("FactoryLogistics.BlockNotLinked");
                return;
            }
            if (!target.AirOn)
            {
                status = Block("FactoryLogistics.BlockDestinationNoAir");
                return;
            }

            Thing cargo = NextCargo(vehicle);
            if (cargo == null)
            {
                status = "FactoryLogistics.DockNothingToUnload".Translate(vehicle.LabelShort);
                return;
            }

            Thing shell = FirstShell();
            if (shell == null)
            {
                status = "FactoryLogistics.DockNeedsShells".Translate();
                return;
            }

            int count = Mathf.Min(cargo.stackCount, TubeUtility.CapsuleLoadOf(cargo));
            float mass = cargo.GetStatValue(StatDefOf.Mass) * count;
            float cost = Ext.launchAirCost * TubeUtility.LaunchCostFactor(mass);
            if (!target.HasRoomFor(null))
            {
                status = Block("FactoryLogistics.BlockDestinationFull");
                return;
            }
            if (net.CurrentStored() < cost)
            {
                status = Block("FactoryLogistics.BlockLowPressure");
                return;
            }

            Thing taken = vehicle.TakeFromInventory(cargo, count);
            if (taken == null)
                return;
            TubeCapsule capsule = (TubeCapsule)ThingMaker.MakeThing(TubeDefOf.FL_CapsuleDelivered);
            if (!capsule.Contents.TryAdd(taken, true))
            {
                vehicle.AddOrTransfer(taken, taken.stackCount);
                capsule.Destroy(DestroyMode.Vanish);
                return;
            }

            shell.SplitOff(1).Destroy(DestroyMode.Vanish);
            TubeUtility.Fire(this, capsule, target, cost);
            status = "FactoryLogistics.DockSent".Translate(taken.LabelCap, target.StationName);
        }

        // The tube block reasons are sentence fragments ("no air pressure").
        private static string Block(string key)
        {
            return key.Translate().CapitalizeFirst() + ".";
        }

        private Thing NextCargo(VehiclePawn vehicle)
        {
            ThingOwner inventory = vehicle.inventory.innerContainer;
            ThingFilter filter = UnloadFilter;
            for (int i = 0; i < inventory.Count; i++)
            {
                Thing thing = inventory[i];
                if (thing is Pawn || thing is TubeCapsule)
                    continue;
                if (filter.Allows(thing))
                    return thing;
            }
            return null;
        }

        // ------------------------------------------------------------------ UI

        private void SetMode(DockMode next)
        {
            mode = next;
            status = null;
            if (Spawned)
                Map.listerHaulables.RecalcAllInCells(AllSlotCellsList());
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;

            if (Faction != Faction.OfPlayer)
                yield break;

            Command_Action toggle = new Command_Action();
            toggle.defaultLabel = ("FactoryLogistics.DockMode_" + mode).Translate();
            toggle.defaultDesc = "FactoryLogistics.DockModeDesc".Translate();
            toggle.icon = ContentFinder<Texture2D>.Get(mode == DockMode.Load
                ? "UI/Commands/FactoryLogistics/DockLoad" : "UI/Commands/FactoryLogistics/DockUnload");
            toggle.action = delegate { SetMode(mode == DockMode.Load ? DockMode.Unload : DockMode.Load); };
            yield return toggle;

            if (mode != DockMode.Unload)
                yield break;

            Command_Action pick = new Command_Action();
            pick.icon = ContentFinder<Texture2D>.Get("UI/Commands/FactoryLogistics/SetDestination");
            pick.defaultLabel = "FactoryLogistics.SetDestination".Translate();
            pick.defaultDesc = "FactoryLogistics.DockSetDestinationDesc".Translate();
            pick.action = delegate { OpenDestinationMenu(); };
            yield return pick;

            Command_Action filter = new Command_Action();
            filter.icon = ContentFinder<Texture2D>.Get("UI/Commands/FactoryLogistics/DockFilter");
            filter.defaultLabel = "FactoryLogistics.DockFilter".Translate();
            filter.defaultDesc = "FactoryLogistics.DockFilterDesc".Translate();
            filter.action = delegate { Find.WindowStack.Add(new Dialog_DockUnloadFilter(this)); };
            yield return filter;
        }

        private void OpenDestinationMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("FactoryLogistics.DestinationNone".Translate(), delegate { Destination = null; }));
            PipeNet net = Net;
            List<Building_TubeReceiver> receivers = new List<Building_TubeReceiver>(TubeUtility.Receivers(Map));
            receivers.SortBy(r => r.StationName);
            foreach (Building_TubeReceiver receiver in receivers)
            {
                if (receiver == this)
                    continue;
                Building_TubeReceiver target = receiver;
                bool linked = net != null && net == target.Net;
                string label = linked
                    ? target.StationName
                    : target.StationName + " (" + "FactoryLogistics.NotLinkedShort".Translate().ToString() + ")";
                options.Add(new FloatMenuOption(label, delegate { Destination = target; }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder();
            string baseText = base.GetInspectString();
            if (!baseText.NullOrEmpty())
                sb.Append(baseText);

            if (Spawned)
            {
                sb.AppendLine().Append(("FactoryLogistics.DockMode_" + mode).Translate());
                VehiclePawn vehicle = Parked;
                if (vehicle != null)
                {
                    sb.AppendLine().Append("FactoryLogistics.DockParked".Translate(vehicle.LabelShort,
                        FreeCargo(vehicle).ToString("0.#"),
                        vehicle.GetStatValue(VehicleStatDefOf.CargoCapacity).ToString("0.#")));
                    if (mode == DockMode.Load)
                        sb.AppendLine().Append("FactoryLogistics.DockRoom".Translate(FreeSlots, TransitMass().ToString("0.#")));
                }
                if (mode == DockMode.Unload)
                {
                    Building_TubeReceiver target = Destination;
                    sb.AppendLine().Append("FactoryLogistics.SenderDestination".Translate(
                        target != null ? target.StationName : "FactoryLogistics.NoDestinationShort".Translate().ToString()));
                    sb.AppendLine().Append("FactoryLogistics.DockShells".Translate(ShellCount()));
                }
                if (!status.NullOrEmpty())
                    sb.AppendLine().Append(status);
            }
            return sb.ToString().TrimStart('\n', '\r').TrimEndNewlines();
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            Building_TubeReceiver target = mode == DockMode.Unload ? Destination : null;
            if (target != null && target.Map == Map)
                GenDraw.DrawLineBetween(this.TrueCenter(), target.TrueCenter());
        }
    }

    public class Dialog_DockUnloadFilter : Window
    {
        private readonly Building_VehicleDock dock;
        private readonly ThingFilterUI.UIState state = new ThingFilterUI.UIState();

        public override Vector2 InitialSize
        {
            get { return new Vector2(420f, 620f); }
        }

        public Dialog_DockUnloadFilter(Building_VehicleDock dock)
        {
            this.dock = dock;
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "FactoryLogistics.DockFilter".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 36f, inRect.width, 44f), "FactoryLogistics.DockFilterHint".Translate());
            Rect list = new Rect(0f, 84f, inRect.width, inRect.height - 84f - CloseButSize.y - 12f);
            ThingFilterUI.DoThingFilterConfigWindow(list, state, dock.UnloadFilter, null, 1, map: dock.Map);
        }
    }
}
