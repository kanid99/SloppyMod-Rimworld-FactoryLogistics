using System.Collections.Generic;
using Verse;
using VanillaFurnitureExpandedFactory;

namespace FactoryLogistics.Belts
{
    /// <summary>Adds VFE belts in the ring around an Ancient Mining chunk machine as a chunk source.</summary>
    [StaticConstructorOnStartup]
    public static class StoneCutterBelts
    {
        static StoneCutterBelts()
        {
            StoneCutterCompat.ChunkSources.Add(ChunkFromBelts);
        }

        private static Thing ChunkFromBelts(Map map, List<IntVec3> ring)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                Building_Conveyor belt = LogisticsManager.ConveyorAt(ring[i], map);
                if (belt == null || belt.innerContainer == null)
                    continue;
                List<Thing> held = belt.innerContainer.InnerListForReading;
                for (int k = 0; k < held.Count; k++)
                {
                    Thing item = held[k];
                    if (!StoneCutterCompat.IsChunk(item))
                        continue;
                    Thing chunk = item.stackCount > 1 ? item.SplitOff(1) : item;
                    if (chunk == item)
                        belt.innerContainer.Remove(item);
                    if (belt.innerContainer.Count == 0)
                    {
                        belt.state = Building_Conveyor.ConveyorState.Empty;
                        belt.itemProgress = 0f;
                    }
                    return chunk;
                }
            }
            return null;
        }
    }
}
