using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SloppyTubes
{
    public static class TubeUtility
    {
        public static IEnumerable<Building_TubeSender> Senders(Map map)
        {
            if (map == null)
                yield break;
            foreach (Building_TubeSender sender in map.listerBuildings.AllBuildingsColonistOfClass<Building_TubeSender>())
                yield return sender;
        }

        public static IEnumerable<Building_TubeReceiver> Receivers(Map map)
        {
            if (map == null)
                yield break;
            foreach (Building_TubeReceiver receiver in map.listerBuildings.AllBuildingsColonistOfClass<Building_TubeReceiver>())
                yield return receiver;
        }

        /// <summary>
        /// The sending station a bill is set to take its product to, or null if its storage
        /// setting is anything else (drop on floor, best stockpile, an ordinary shelf).
        /// </summary>
        public static Building_TubeSender SenderForBill(Bill bill)
        {
            if (bill == null || bill.GetStoreMode() != BillStoreModeDefOf.SpecificStockpile)
                return null;
            SlotGroup group = bill.GetSlotGroup() as SlotGroup;
            return group != null ? group.parent as Building_TubeSender : null;
        }
    }
}
