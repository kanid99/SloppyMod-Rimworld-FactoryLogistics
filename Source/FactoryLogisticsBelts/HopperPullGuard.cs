using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using VanillaFurnitureExpandedFactory;

namespace FactoryLogistics.Belts
{
    /// <summary>
    /// VFE's belts pull whatever is in a factory hopper beside them (behind or to either side),
    /// so a hopper that only happens to touch a belt gets emptied onto it. It is left alone when:
    /// a conveyor arm takes from or puts into it, it is a machine's input hopper, its own "allow
    /// taking" is off, or the belt is told not to pull from hoppers (its "Pull from hoppers" button).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HopperPullGuard
    {
        static HopperPullGuard()
        {
            MethodInfo target = AccessTools.Method(typeof(Building_Conveyor), "TryPullFromDir");
            if (target == null)
            {
                Log.Warning("[Factory Logistics] VFE belt hopper pull not found; belts will pull from every hopper beside them.");
                return;
            }
            new Harmony("SloppyMods.FactoryLogistics.Belts").Patch(target,
                prefix: new HarmonyMethod(typeof(HopperPullGuard), nameof(Prefix)));
        }

        public static bool Prefix(Building_Conveyor __instance, Rot4 dir, ref bool __result)
        {
            try
            {
                CompConveyorDrop comp = __instance.GetComp<CompConveyorDrop>();
                if (comp != null && !comp.pullFromHoppers)
                {
                    __result = false;
                    return false;
                }
                Map map = __instance.Map;
                LogisticsManager manager = LogisticsManager.For(map);
                if (manager == null)
                    return true;
                Building hopper = (__instance.Position + dir.FacingCell).GetEdifice(map);
                if (hopper == null)
                    return true;
                // The hopper's own "allow taking" switch, and a machine's input hopper (VFE draws
                // it with the input arrow): what is put there is for the machine, not the belt.
                Building_FactoryHopper fh = hopper as Building_FactoryHopper;
                if ((fh != null && !fh.allowTaking) || hopper.Graphic == GraphicsCache.hopperInput || manager.ArmWorks(hopper))
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception)
            {
            }
            return true;
        }
    }
}
