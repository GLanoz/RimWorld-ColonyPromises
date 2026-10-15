using HarmonyLib;
using Verse;

namespace ColonyPromises
{
    public sealed partial class GameComponent_Promises
    {
        public void NotifySubstanceIngested(Pawn pawn, ThingDef def, int now, PromiseRecord expected, bool effectGained)
        {
            if (Scribe.mode != LoadSaveMode.Inactive || !PromiseConditions.Eligible(pawn) || def?.IsDrug != true) return;
            var state = StateFor(pawn);
            // Persist familiarity even without a request, on caravans, and after all outcomes.
            SubstanceNeeds.Remember(state, def);
            if (SubstanceNeeds.Credit(state, expected, def, now, effectGained)) Check(state, now);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    internal static class SubstanceIngestionPatch
    {
        internal struct IngestionState
        {
            public bool Valid;
            public int Count;
            public ThingDef Def;
            public PromiseRecord Expected;
            public float BeforeEffect;
        }
        private static void Prefix(Thing __instance, Pawn __0, out IngestionState __state)
        {
            var record = __0 == null ? null : GameComponent_Promises.Current?.PeekState(__0)?.Active;
            __state = new IngestionState { Valid = Scribe.mode == LoadSaveMode.Inactive && !__instance.Destroyed
                && __instance.def.IsDrug && __instance.IngestibleNow && __instance.stackCount > 0,
                Count = __instance.stackCount, Def = __instance.def, Expected = record,
                BeforeEffect = record?.SubstanceEffect == null ? 0
                    : __0.health.hediffSet.GetFirstHediffOfDef(record.SubstanceEffect)?.Severity ?? 0 };
        }
        // The original applies ingestion outcomes before returning. Nutrition is not a dose count.
        private static void Postfix(Thing __instance, Pawn __0, IngestionState __state)
        {
            if (__state.Valid && (__instance.Destroyed || __instance.stackCount < __state.Count))
            {
                float after = __state.Expected?.SubstanceEffect == null ? 0
                    : __0.health.hediffSet.GetFirstHediffOfDef(__state.Expected.SubstanceEffect)?.Severity ?? 0;
                GameComponent_Promises.Current?.NotifySubstanceIngested(__0, __state.Def, Find.TickManager.TicksGame,
                    __state.Expected, after > __state.BeforeEffect);
            }
        }
    }
}
