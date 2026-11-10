using RimWorld;
using UnityEngine;
using Verse;

namespace SloppyTubes
{
    [DefOf]
    public static class TubeDefOf
    {
        // A packed capsule on its way out: made by the pack bill, accepted by sending stations.
        public static ThingDef SloppyTubes_Capsule;

        // The same capsule after it has come out of a receiving station. A separate def rather
        // than a flag so vanilla filters can tell the two apart: the unpack bill takes only
        // these, and sending stations' storage takes only the other.
        public static ThingDef SloppyTubes_CapsuleDelivered;

        public static ThingDef SloppyTubes_CapsuleShell;

        static TubeDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(TubeDefOf));
        }
    }

    [StaticConstructorOnStartup]
    public static class TubeTex
    {
        public static readonly Texture2D SetDestination = ContentFinder<Texture2D>.Get("UI/Commands/SloppyTubes/SetDestination");
        public static readonly Texture2D AssignSender = ContentFinder<Texture2D>.Get("UI/Commands/SloppyTubes/AssignSender");
        public static readonly Texture2D MarkForUnpacking = ContentFinder<Texture2D>.Get("UI/Commands/SloppyTubes/MarkForUnpacking");
        public static readonly Texture2D Rename = ContentFinder<Texture2D>.Get("UI/Buttons/Rename");
    }

    /// <summary>Per-station numbers, set in XML on the station's def.</summary>
    public class TubeStationExtension : DefModExtension
    {
        // Compressed air a sending station takes from the network's tanks for each capsule.
        public float launchAirCost = 30f;

        // Travel time, per map cell between the two stations.
        public float transitTicksPerCell = 2f;

        public int minTransitTicks = 30;

        public int maxTransitTicks = 900;
    }
}
