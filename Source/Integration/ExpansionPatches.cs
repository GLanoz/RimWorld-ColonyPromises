using HarmonyLib;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public sealed partial class GameComponent_Promises
    {
        public void NotifyStyleChanged(Pawn pawn)
        {
            if (!ModsConfig.IdeologyActive || Scribe.mode != LoadSaveMode.Inactive || pawn == null) return;
            var record = PeekState(pawn).Active;
            int now = Find.TickManager.TicksGame;
            if (CreditStyle(record, pawn, now)) Check(StateFor(pawn), now);
        }
        public static bool CreditStyle(PromiseRecord record, Pawn pawn, int now)
        {
            if (!ModsConfig.IdeologyActive || pawn == null || record?.Kind != PromiseKind.Style || !record.FrozenConsequences || record.StyleChangedTick >= 0
                || now < record.CreatedTick || now > record.DeadlineTick) return false;
            var original = record.StyleBeard ? (StyleItemDef)record.OriginalBeard : record.OriginalHair;
            if (ExpansionRequests.CurrentStyle(pawn, record.StyleBeard) == original
                || ExpansionRequests.CurrentStyle(pawn, record.StyleBeard) != ExpansionRequests.TargetStyle(record)) return false;
            record.StyleChangedTick = now; record.ProgressTicks = record.RequiredTicks;
            return true;
        }
    }
    [HarmonyPatch(typeof(Pawn_Ownership), nameof(Pawn_Ownership.UnclaimThrone))]
    internal static class ThroneUnclaimedPatch
    {
        private static void Prefix(Pawn_Ownership __instance)
        { if (ModsConfig.RoyaltyActive) GameComponent_Promises.Current?.InvalidateThing(__instance.AssignedThrone); }
    }
    [HarmonyPatch(typeof(Pawn_Ownership), nameof(Pawn_Ownership.ClaimThrone))]
    internal static class ThroneClaimedPatch
    {
        private static void Postfix(Building_Throne __0, bool __result)
        { if (ModsConfig.RoyaltyActive && __result) GameComponent_Promises.Current?.InvalidateThing(__0); }
    }
    [HarmonyPatch(typeof(Pawn_StyleTracker), nameof(Pawn_StyleTracker.Notify_StyleItemChanged))]
    internal static class AppearanceChangedPatch
    {
        private static void Postfix(Pawn ___pawn) => GameComponent_Promises.Current?.NotifyStyleChanged(___pawn);
    }
    [HarmonyPatch(typeof(CompGlower), nameof(CompGlower.UpdateLit))]
    internal static class ThroneLightingPatch
    {
        private static void Postfix(CompGlower __instance)
        {
            if (ModsConfig.RoyaltyActive && __instance.parent?.Spawned == true)
                GameComponent_Promises.Current?.ThroneLightingChanged(__instance.parent.GetRoom());
        }
    }
}
