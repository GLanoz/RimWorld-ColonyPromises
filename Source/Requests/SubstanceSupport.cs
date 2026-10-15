using System;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    // Definition-only recognition, independent of pawn state, policy and supply.
    public static class SubstanceSupport
    {
        private static CompProperties_Drug Properties(ThingDef def) => SubstanceNeeds.Properties(def);
        // Auxiliary components do not determine whether the ingestion effect is verifiable.
        public static bool Standard(ThingDef def) => def?.IsDrug == true && def.IsIngestible
            && def.PlayerAcquirable && def.thingClass != null && typeof(ThingWithComps).IsAssignableFrom(def.thingClass)
            && Properties(def)?.compClass != null && typeof(CompDrug).IsAssignableFrom(Properties(def).compClass)
            && def.ingestible.outcomeDoers?.Count > 0;

        public static string SupportReason(ThingDef def, bool experiment)
        {
            if (!Standard(def)) return "CP_SubstanceStructure";
            if (!experiment) return null;
            if (def.ingestible.drugCategory != DrugCategory.Social && def.ingestible.drugCategory != DrugCategory.Hard)
                return "CP_SubstanceCategory";
            // Recognize the effect provider, allowing standard auxiliary need/psyfocus offsets.
            foreach (var outcome in def.ingestible.outcomeDoers)
                if (!SupportedOutcome(outcome))
                    return "CP_SubstanceOutcome";
            var highs = def.ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_GiveHediff>()
                .Where(o => o.severity > 0 && o.chance >= 1 && TemporaryHigh(o.hediffDef))
                .Select(o => o.hediffDef).Distinct().ToList();
            if (highs.Count != 1) return "CP_SubstanceEffect";
            if (def.ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_GiveHediff>().Any(o =>
                o.hediffDef == null || o.hediffDef != highs[0] && o.hediffDef != Properties(def).chemical?.toleranceHediff))
                return "CP_SubstanceExtraEffect";
            return null;
        }
        public static bool SupportedOutcome(IngestionOutcomeDoer outcome) => outcome != null
            && (outcome.GetType() == typeof(IngestionOutcomeDoer_GiveHediff)
                || outcome.GetType() == typeof(IngestionOutcomeDoer_OffsetNeed)
                || outcome.GetType() == typeof(IngestionOutcomeDoer_OffsetPsyfocus));
        private static bool TemporaryHigh(HediffDef effect) => effect != null
            && (effect.hediffClass == typeof(Hediff_High) || effect.hediffClass == typeof(Hediff_Alcohol))
            && effect.comps?.Any(c => c is HediffCompProperties_Disappears
                || c is HediffCompProperties_SeverityPerDay decay && decay.severityPerDay < 0) == true
            && !effect.comps.Any(c => c is HediffCompProperties_HealPermanentWounds);
        public static HediffDef ExperimentEffect(ThingDef def) => SupportReason(def, true) == null
            ? def.ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_GiveHediff>()
                .First(o => o.severity > 0 && o.chance >= 1 && TemporaryHigh(o.hediffDef)).hediffDef : null;

    }
}
