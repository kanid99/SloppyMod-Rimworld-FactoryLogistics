using System.Collections.Generic;
using RimWorld;
using Verse;

namespace FactoryLogistics
{
    /// <summary>
    /// A sending station that has a capsule to send and cannot, for a reason that will not
    /// clear on its own: no destination chosen, tube cut, no air. A full receiver or tanks
    /// that are still refilling are ordinary waiting and are left out.
    /// </summary>
    public class Alert_TubeSenderBlocked : Alert
    {
        private readonly List<Thing> culprits = new List<Thing>();

        public Alert_TubeSenderBlocked()
        {
            defaultLabel = "FactoryLogistics.AlertBlocked".Translate();
            defaultExplanation = "FactoryLogistics.AlertBlockedDesc".Translate();
            defaultPriority = AlertPriority.Medium;
        }

        public override AlertReport GetReport()
        {
            culprits.Clear();
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                if (!maps[i].IsPlayerHome)
                    continue;
                foreach (Building_TubeSender sender in TubeUtility.Senders(maps[i]))
                {
                    if (sender.WaitingCapsule() != null && sender.BlockReasonKey(false) != null)
                        culprits.Add(sender);
                }
            }
            return AlertReport.CulpritsAre(culprits);
        }
    }
}
