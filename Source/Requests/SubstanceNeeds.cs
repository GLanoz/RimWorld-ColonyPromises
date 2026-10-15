using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;

namespace ColonyPromises
{
    public static class SubstanceNeeds
    {
        private static readonly FieldInfo policyField = typeof(Pawn_DrugPolicyTracker)
            .GetField("curPolicy", BindingFlags.Instance | BindingFlags.NonPublic);
        public static CompProperties_Drug Properties(ThingDef def) => def?.comps?.OfType<CompProperties_Drug>().FirstOrDefault();
        public static string Key(ThingDef def)
        {
            var chemical = Properties(def)?.chemical;
            return chemical != null ? "chemical:" + chemical.defName : "drug:" + def?.defName;
        }
        public static bool ExperimentTrait(int degree, bool suppressed, int addictions) => !suppressed
            && (degree == 1 || degree == 2) && addictions < RequestRules.ExperimentAddictionLimit;
        public static IEnumerable<Hediff_Addiction> Addictions(Pawn pawn) =>
            pawn?.health?.hediffSet?.hediffs?.OfType<Hediff_Addiction>() ?? Enumerable.Empty<Hediff_Addiction>();

        public static bool Standard(ThingDef def) => SubstanceSupport.Standard(def);
        public static string SupportReason(ThingDef def, bool experiment) => SubstanceSupport.SupportReason(def, experiment);
        public static HediffDef ExperimentEffect(ThingDef def) => SubstanceSupport.ExperimentEffect(def);

        public static string TargetReason(Pawn pawn, ThingDef def, bool experiment, bool manual = false) =>
            TargetReason(pawn, def, experiment, manual, null, out _);
        private static string TargetReason(Pawn pawn, ThingDef def, bool experiment, bool manual,
            SubstanceKnowledge knowledge, out string availability)
        {
            availability = null;
            var reason = SupportReason(def, experiment);
            if (reason != null) return reason;
            var pawnReason = AllowedReason(pawn, def, experiment);
            if (pawnReason != null) return pawnReason;
            if (!manual && experiment && (knowledge ?? new SubstanceKnowledge(pawn, GameComponent_Promises.Current?.PeekState(pawn))).Known(def))
                return "CP_SubstanceKnown";
            if (!experiment && !Addictions(pawn).Any(a => a.Chemical == Properties(def).chemical))
                return "CP_SubstanceNoAddiction";
            if (!manual && !CanObtain(pawn, def, out availability)) return "CP_SubstanceNoSupply";
            return null;
        }
        public static string TargetDiagnostics(Pawn pawn)
        {
            if (pawn == null) return "CP_DebugMissing".Translate();
            var knowledge = new SubstanceKnowledge(pawn, GameComponent_Promises.Current?.PeekState(pawn));
            return string.Join("\n", knowledge.Drugs.OrderBy(d => d.defName, StringComparer.Ordinal).Select(d => {
                string reason = TargetReason(pawn, d, true, false, knowledge, out _);
                string unsupported = reason == "CP_SubstanceOutcome" ? " (" + string.Join(", ",
                    d.ingestible.outcomeDoers.Where(o => !SubstanceSupport.SupportedOutcome(o))
                        .Select(o => o?.GetType().Name ?? "null")) + ")" : "";
                return d.LabelCap + ": " + (reason ?? "CP_SubstanceReady").Translate() + unsupported;
            }));
        }

        public static bool Allowed(Pawn pawn, ThingDef def, bool experiment) => AllowedReason(pawn, def, experiment) == null;
        public static string AllowedReason(Pawn pawn, ThingDef def, bool experiment)
        {
            if (!Standard(def)) return "CP_SubstanceStructure";
            if (pawn?.drugs == null || pawn.health?.capacities == null
                || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness)
                || !pawn.RaceProps.CanEverEat(def) || !FoodUtility.DrugIsSuitable(pawn, def)
                || !PawnUtility.CanTakeDrug(pawn, def)) return "CP_SubstancePawnBlocked";
            if (ModsConfig.IdeologyActive && (!IdeoUtility.DoerWillingToDo(HistoryEventDefOf.IngestedDrug, pawn)
                || def.IsNonMedicalDrug && !IdeoUtility.DoerWillingToDo(HistoryEventDefOf.IngestedRecreationalDrug, pawn)
                || def.ingestible.drugCategory == DrugCategory.Hard && !IdeoUtility.DoerWillingToDo(HistoryEventDefOf.IngestedHardDrug, pawn)))
                return "CP_SubstancePawnBlocked";
            if (ModsConfig.AnomalyActive && pawn.IsMutant && pawn.mutant.Def.disablePolicies) return "CP_SubstancePawnBlocked";
            // Read the assigned/default policy without invoking its mutating lazy initializer.
            var policy = policyField?.GetValue(pawn.drugs) as DrugPolicy
                ?? Current.Game?.drugPolicyDatabase?.AllPolicies.FirstOrDefault();
            return PolicyAllows(policy, def, experiment) ? null : "CP_SubstancePolicyBlocked";
        }
        public static bool PolicyAllows(DrugPolicy policy, ThingDef def, bool experiment)
        {
            if (policy == null) return false;
            DrugPolicyEntry entry = null;
            for (int i = 0; i < policy.Count; i++) if (policy[i].drug == def) { entry = policy[i]; break; }
            return entry != null && (experiment ? entry.allowedForJoy || entry.allowScheduled : entry.allowedForAddiction);
        }

        public static bool Known(Pawn pawn, ColonistPromises state, ThingDef def) =>
            new SubstanceKnowledge(pawn, state).Known(def);

        public static void SeedKnowledge(ColonistPromises state)
        {
            var knowledge = new SubstanceKnowledge(state.Pawn, state);
            foreach (var def in knowledge.Drugs)
            {
                if (state.Pawn.drugs?.HasEverTaken(def) == true) Remember(state, def);
                else if (knowledge.Known(def) && !state.KnownSubstances.Contains(Key(def)))
                    state.KnownSubstances.Add(Key(def));
            }
            state.SubstanceHistoryInitialized = true;
        }
        public static void Remember(ColonistPromises state, ThingDef def)
        {
            string key = Key(def);
            if (!state.KnownSubstances.Contains(key)) state.KnownSubstances.Add(key);
            string product = "drug:" + def.defName;
            if (!state.KnownSubstances.Contains(product)) state.KnownSubstances.Add(product);
        }
        public static void StartCooldown(ColonistPromises state, PromiseRecord record, int now)
        {
            if (record.Kind == PromiseKind.Substance)
                state.SubstanceCooldownUntilTick = Math.Max(state.SubstanceCooldownUntilTick,
                    now + RequestRules.SubstanceCooldownDays * PromiseBalance.TicksPerDay);
        }

        public static bool DoseProduct(ThingDef def, ChemicalDef chemical) => chemical != null && Standard(def)
            && Properties(def).chemical == chemical && Properties(def).needLevelOffset > 0;
        public static List<Thing> Doses(Pawn pawn, ChemicalDef chemical) =>
            (pawn.Map?.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver) ?? Enumerable.Empty<Thing>())
                .Concat(pawn.inventory?.innerContainer.AsEnumerable() ?? Enumerable.Empty<Thing>())
                .Where(t => DoseProduct(t.def, chemical) && !t.Destroyed && t.stackCount > 0 && t.IngestibleNow).Distinct().ToList();
        public static bool Accessible(Pawn pawn, Thing thing) => thing.IngestibleNow && !thing.IsForbidden(pawn)
            && (pawn.inventory?.innerContainer.Contains(thing) == true
                || thing.Spawned && pawn.CanReach(thing, PathEndMode.Touch, Danger.Some));
        public static bool HasDose(Pawn pawn, ChemicalDef chemical) => Doses(pawn, chemical)
            .Any(t => Allowed(pawn, t.def, false) && Accessible(pawn, t));

        public static bool CanObtain(Pawn pawn, ThingDef def, out string availability)
        {
            availability = "CP_SubstanceNoSupply".Translate();
            if (pawn?.Map == null) return false;
            if (pawn.inventory?.innerContainer.Any(t => t.def == def && Accessible(pawn, t)) == true)
            { availability = "CP_GearStock".Translate(); return true; }
            // Reuse the equipment stock/ordinary-production solver with an ingestible stock predicate.
            return EquipmentNeeds.CanObtain(pawn, new PromiseRecord { EquipmentDef = def, QualityOptional = true },
                out availability, t => t.def == def && t.IngestibleNow);
        }

        public static PromiseRecord Terms(ThingDef def, bool experiment) => new PromiseRecord {
            Kind = PromiseKind.Substance, SubstanceExperiment = experiment, SubstanceDef = def,
            SubstanceChemical = Properties(def)?.chemical, SubstanceEffect = experiment ? ExperimentEffect(def) : null,
            RequiredTicks = 1
        };
        private static RequestCandidate Candidate(PromiseRecord terms, string signature) => new RequestCandidate {
            Terms = terms, Signature = signature, ReasonKey = terms.SubstanceExperiment ? "CP_SubstanceTryReason" : "CP_SubstanceAddictionReason"
        };

        public static RequestCandidate Evaluate(Pawn pawn, RequestEvaluation result, bool bypassCooldown = false, string subtype = null)
        {
            var state = GameComponent_Promises.Current?.PeekState(pawn);
            int now = Find.TickManager.TicksGame;
            int count = Addictions(pawn).Count();
            var trait = pawn.story?.traits?.GetTrait(TraitDefOf.DrugDesire);
            result.Current = "CP_SubstanceEvidence".Translate(trait?.Degree ?? 0, count,
                RequestGenerator.Number(Math.Max(0, (state?.SubstanceCooldownUntilTick ?? 0) - now) / (float)PromiseBalance.TicksPerDay),
                state?.KnownSubstances?.Count ?? 0);
            if (!bypassCooldown && now < (state?.SubstanceCooldownUntilTick ?? 0))
            { result.Reason = "CP_SubstanceCooldownBlocked"; return null; }
            var defs = DefDatabase<ThingDef>.AllDefsListForReading.OrderBy(d => d.defName, StringComparer.Ordinal).ToList();
            bool missing = false, inaccessible = false, prohibited = false;
            if (subtype != "SubstanceTry")
                foreach (var chemical in Addictions(pawn).Select(a => a.Chemical).Where(c => c != null).Distinct()
                    .OrderBy(c => c.defName, StringComparer.Ordinal))
                {
                    if (HasDose(pawn, chemical)) continue;
                    missing = true;
                    result.HasDeficit = true;
                    string signature = "substance-addiction-v1:" + chemical.defName;
                    result.DeficitSignature = signature;
                    var stock = Doses(pawn, chemical);
                    result.Current += "\n" + "CP_SubstanceMissing".Translate(chemical.LabelCap)
                        + " " + "CP_SubstanceStockEvidence".Translate(stock.Count);
                    var usable = defs.Where(d => DoseProduct(d, chemical) && Allowed(pawn, d, false)).ToList();
                    if (usable.Count == 0) { prohibited = true; continue; }
                    // Existing unusable doses are an access problem, never a reason to request extra production.
                    if (stock.Count > 0) { inaccessible = true; continue; }
                    foreach (var def in usable)
                    {
                        if (!CanObtain(pawn, def, out var availability)) continue;
                        result.Current += "\n" + availability;
                        return Candidate(Terms(def, false), signature);
                    }
                }
            if (missing)
            {
                result.Reason = prohibited ? "CP_SubstancePolicyBlocked" : inaccessible ? "CP_SubstanceAccessBlocked" : "CP_SubstanceNoSupply";
                return null; // A confirmed addiction shortage precedes experimentation.
            }
            if (subtype == "SubstanceAddiction") { result.Reason = "CP_SubstanceNoShortage"; return null; }
            if (!ExperimentTrait(trait?.Degree ?? 0, trait?.Suppressed ?? false, count))
            { result.Reason = count >= RequestRules.ExperimentAddictionLimit ? "CP_SubstanceLimit" : "CP_SubstanceNoTrait"; return null; }
            // A mod-owned persistent desire. Addiction alone is never this reason.
            result.HasDeficit = true;
            result.DeficitSignature = "substance-try-v1";
            var knowledge = new SubstanceKnowledge(pawn, state);
            int unsupported = 0;
            foreach (var def in defs.Where(d => d.IsDrug))
            {
                if (ExperimentEffect(def) == null) { unsupported++; continue; }
                if (TargetReason(pawn, def, true, false, knowledge, out var availability) != null) continue;
                result.Current += "\n" + availability;
                return Candidate(Terms(def, true), result.DeficitSignature);
            }
            result.Current += "\n" + "CP_SubstanceUnsupported".Translate(unsupported);
            result.Reason = "CP_SubstanceNoNewTarget";
            return null;
        }

        public static bool Relevant(Pawn pawn, PromiseRecord record)
        {
            if (!new SubstanceCondition().ValidParameters(record)) return false;
            if (record.SubstanceExperiment)
            {
                var trait = pawn.story?.traits?.GetTrait(TraitDefOf.DrugDesire);
                return ExperimentTrait(trait?.Degree ?? 0, trait?.Suppressed ?? false, Addictions(pawn).Count())
                    && !Known(pawn, GameComponent_Promises.Current?.PeekState(pawn), record.SubstanceDef)
                    && Allowed(pawn, record.SubstanceDef, true) && CanObtain(pawn, record.SubstanceDef, out _);
            }
            if (!Addictions(pawn).Any(a => a.Chemical == record.SubstanceChemical) || HasDose(pawn, record.SubstanceChemical)) return false;
            return Doses(pawn, record.SubstanceChemical).Count == 0
                && DefDatabase<ThingDef>.AllDefsListForReading.Any(d => DoseProduct(d, record.SubstanceChemical)
                    && Allowed(pawn, d, false) && CanObtain(pawn, d, out _));
        }

        public static bool Credit(PromiseRecord record, ThingDef ingested, int now, bool effectPresent)
        {
            if (record?.Kind != PromiseKind.Substance || record.SubstanceConsumedTick >= 0
                || !record.FrozenConsequences || now < record.CreatedTick || now > record.DeadlineTick || !Standard(ingested)) return false;
            bool matches = record.SubstanceExperiment ? ingested == record.SubstanceDef && effectPresent
                : DoseProduct(ingested, record.SubstanceChemical);
            if (!matches) return false;
            record.SubstanceConsumedTick = now;
            record.ProgressTicks = record.RequiredTicks;
            return true;
        }
        public static bool Credit(ColonistPromises state, PromiseRecord expected, ThingDef ingested, int now, bool effectGained) =>
            expected != null && state.Active == expected && Credit(expected, ingested, now, effectGained);
    }

}
