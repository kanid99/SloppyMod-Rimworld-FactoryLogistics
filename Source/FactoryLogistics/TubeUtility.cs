using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace FactoryLogistics
{
    /// <summary>
    /// On the packed capsule's def: how much one capsule may carry. A patch can change it.
    /// </summary>
    public class CapsuleExtension : DefModExtension
    {
        public float massLimit = 40f;

        // Launch air: the station's base cost times this at empty, rising in a straight line to
        // fullFactor at the weight limit (and on past it for one oversize item).
        public float emptyFactor = 0.5f;
        public float fullFactor = 1.5f;
        public float maxFactor = 2.5f;
    }

    public static class TubeUtility
    {
        private static readonly CapsuleExtension DefaultCapsule = new CapsuleExtension();

        public static CapsuleExtension Capsule
        {
            get
            {
                CapsuleExtension ext = TubeDefOf.FL_Capsule != null ? TubeDefOf.FL_Capsule.GetModExtension<CapsuleExtension>() : null;
                return ext ?? DefaultCapsule;
            }
        }

        /// <summary>The most one capsule may carry, in kg.</summary>
        public static float CapsuleMassLimit
        {
            get { return Capsule.massLimit; }
        }

        /// <summary>How much a capsule of this weight costs to launch, relative to the base cost.</summary>
        public static float LaunchCostFactor(float mass)
        {
            CapsuleExtension ext = Capsule;
            if (ext.massLimit <= 0f)
                return 1f;
            float factor = ext.emptyFactor + (ext.fullFactor - ext.emptyFactor) * (mass / ext.massLimit);
            return Mathf.Clamp(factor, ext.emptyFactor, ext.maxFactor);
        }
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
