using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public sealed class ColonyPromisesMod : Mod
    {
        public ColonyPromisesMod(ModContentPack content) : base(content)
        {
            new Harmony("colonypromises.core").PatchAll();
        }
    }

    [HarmonyPatch(typeof(JoyUtility), nameof(JoyUtility.JoyTickCheckEnd))]
    internal static class RecreationActivityPatch
    {
        // Prefix retains the activity state before the utility can end the job.
        private static void Prefix(Pawn pawn, int delta) => GameComponent_Promises.Current?.CreditLeisure(pawn, delta);
    }

    [HarmonyPatch(typeof(Pawn_Ownership), nameof(Pawn_Ownership.UnclaimBed))]
    internal static class BedUnclaimedPatch
    {
        private static void Prefix(Pawn_Ownership __instance) => GameComponent_Promises.Current?.InvalidateThing(__instance.OwnedBed);
    }

    [HarmonyPatch(typeof(Pawn_Ownership), nameof(Pawn_Ownership.ClaimBedIfNonMedical))]
    internal static class BedClaimedPatch
    {
        private static void Postfix(Building_Bed __0, bool __result)
        {
            if (__result) GameComponent_Promises.Current?.InvalidateThing(__0);
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentRemoved))]
    internal static class EquipmentRemovedPatch
    {
        private static void Prefix(ThingWithComps eq) => GameComponent_Promises.Current?.InvalidateThing(eq);
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelRemoved))]
    internal static class ApparelRemovedPatch
    {
        private static void Prefix(Apparel apparel) => GameComponent_Promises.Current?.InvalidateThing(apparel);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    internal static class PawnLeavesMapPatch
    {
        private static void Prefix(Pawn __instance) => GameComponent_Promises.Current?.InvalidatePawn(__instance);
    }

    [HarmonyPatch(typeof(CompQuality), nameof(CompQuality.SetQuality))]
    internal static class EquipmentQualityChangedPatch
    {
        private static void Prefix(CompQuality __instance) => GameComponent_Promises.Current?.InvalidateThing(__instance.parent);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class PawnDiedPatch
    {
        private static void Postfix(Pawn __instance) => GameComponent_Promises.Current?.CheckLifecycle(__instance);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    internal static class PawnFactionChangedPatch
    {
        private static void Postfix(Pawn __instance) => GameComponent_Promises.Current?.CheckLifecycle(__instance);
    }

    [HarmonyPatch]
    internal static class BedroomChangedPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Room), "Notify_ContainedThingSpawnedOrDespawned");
            yield return AccessTools.Method(typeof(Room), "Notify_TerrainChanged");
            yield return AccessTools.Method(typeof(Room), "Notify_BedTypeChanged");
            yield return AccessTools.Method(typeof(Room), "Notify_RoofChanged");
            yield return AccessTools.Method(typeof(Room), "Notify_RoomShapeChanged");
        }
        private static void Postfix(Room __instance, MethodBase __originalMethod) =>
            GameComponent_Promises.Current?.RoomChanged(__instance, __originalMethod.Name == "Notify_RoomShapeChanged");
    }
}
