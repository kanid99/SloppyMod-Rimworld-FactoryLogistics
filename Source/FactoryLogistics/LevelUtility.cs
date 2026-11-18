using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace FactoryLogistics
{
    /// <summary>
    /// Levels from "As above, So below 2", read by reflection so the mod is never a dependency.
    ///
    /// That mod cuts one real map into horizontal bands, one per level. The same spot on another
    /// level is the same x and the same row within its band, one band-slot (64+ rows) away. So
    /// a straight-line distance between levels counts all those rows; anything that travels
    /// between levels measures on one level and adds a fixed cost per level crossed instead.
    ///
    /// Without that mod, or on a map it has not banded, everything here reports one level.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class LevelUtility
    {
        private static readonly Type BandMapType;
        private static readonly PropertyInfo BandedProp;
        private static readonly FieldInfo BandCountField;
        private static readonly MethodInfo BandOfMethod;
        private static readonly MethodInfo TranslateMethod;

        public static bool Available
        {
            get { return BandMapType != null && BandOfMethod != null && TranslateMethod != null; }
        }

        static LevelUtility()
        {
            BandMapType = GenTypes.GetTypeInAnyAssembly("AsAboveSoBelow.ABBandMap");
            if (BandMapType == null)
                return;
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            BandedProp = BandMapType.GetProperty("Banded", flags);
            BandCountField = BandMapType.GetField("bandCount", flags);
            BandOfMethod = BandMapType.GetMethod("BandOf", flags, null, new[] { typeof(IntVec3) }, null);
            TranslateMethod = BandMapType.GetMethod("Translate", flags, null, new[] { typeof(IntVec3), typeof(int) }, null);
            if (!Available)
                Log.Warning("[SloppyMods Factory Logistics] As above, So below is loaded but its level map "
                    + "does not look as expected; travel between levels is measured in straight lines.");
        }

        private static MapComponent BandMap(Map map)
        {
            if (!Available || map == null)
                return null;
            List<MapComponent> components = map.components;
            for (int i = 0; i < components.Count; i++)
            {
                if (BandMapType.IsInstanceOfType(components[i]))
                {
                    MapComponent comp = components[i];
                    if (BandedProp != null && !(bool)BandedProp.GetValue(comp, null))
                        return null;
                    return comp;
                }
            }
            return null;
        }

        /// <summary>True when the map is cut into levels.</summary>
        public static bool Banded(Map map)
        {
            return BandMap(map) != null;
        }

        /// <summary>The level band a cell is in: 0 on a map without levels.</summary>
        public static int BandOf(IntVec3 cell, Map map)
        {
            MapComponent comp = BandMap(map);
            if (comp == null)
                return 0;
            return (int)BandOfMethod.Invoke(comp, new object[] { cell });
        }

        public static int BandCount(Map map)
        {
            MapComponent comp = BandMap(map);
            if (comp == null || BandCountField == null)
                return 1;
            return (int)BandCountField.GetValue(comp);
        }

        /// <summary>
        /// The same spot as <paramref name="cell"/> on another band, or IntVec3.Invalid if the
        /// map has no levels or the band does not exist.
        /// </summary>
        public static IntVec3 Translate(IntVec3 cell, int toBand, Map map)
        {
            MapComponent comp = BandMap(map);
            if (comp == null || toBand < 0 || toBand >= BandCount(map))
                return IntVec3.Invalid;
            IntVec3 result = (IntVec3)TranslateMethod.Invoke(comp, new object[] { cell, toBand });
            return result.InBounds(map) ? result : IntVec3.Invalid;
        }

        /// <summary>
        /// How far something travels from a to b: straight-line on one level; between levels,
        /// the distance on one level plus <paramref name="perLevel"/> cells for each level crossed.
        /// </summary>
        public static float TravelDistance(IntVec3 a, IntVec3 b, Map map, float perLevel)
        {
            MapComponent comp = BandMap(map);
            if (comp == null)
                return a.DistanceTo(b);
            int bandA = (int)BandOfMethod.Invoke(comp, new object[] { a });
            int bandB = (int)BandOfMethod.Invoke(comp, new object[] { b });
            if (bandA == bandB)
                return a.DistanceTo(b);
            IntVec3 bOnA = (IntVec3)TranslateMethod.Invoke(comp, new object[] { b, bandA });
            return a.DistanceTo(bOnA) + Math.Abs(bandA - bandB) * perLevel;
        }
    }
}
