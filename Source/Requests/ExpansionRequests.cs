using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;

namespace ColonyPromises
{
    public static class ExpansionRequests
    {
        private static readonly FieldInfo[] styleMaps = new[] { "hairFrequencies", "beardFrequencies", "tattooFrequencies" }
            .Select(name => typeof(IdeoStyleTracker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)).ToArray();
        // GetFrequency lazily initializes saved style maps. Inspection must not trigger that initialization.
        public static bool StyleDataReady(Pawn pawn) => ModsConfig.IdeologyActive && pawn?.Ideo?.style != null
            && styleMaps.All(field => field != null && field.GetValue(pawn.Ideo.style) != null);
        public static readonly string[] ApparelCauses = { "RoyalApparel", "IdeoRoleApparel", "IdeoDesiredApparel" };
        public static bool IsApparel(string cause) => ApparelCauses.Contains(cause);
        public static bool Enabled(string cause) => cause == "RoyalApparel" || cause == "ThroneRoom"
            ? ModsConfig.RoyaltyActive : cause == "HairStyle" || cause == "BeardStyle" || cause == "IdeoRoleApparel" || cause == "IdeoDesiredApparel"
                ? ModsConfig.IdeologyActive : true;
        public static string IdeologyKey(Pawn pawn) => pawn.Ideo?.GetUniqueLoadID();
        public static float PreceptMood(Pawn pawn, string name, Precept precept)
        {
            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(name);
            if (def == null || pawn.needs?.mood == null) return 0;
            var thought = ThoughtMaker.MakeThought(def) as Thought_Situational;
            if (thought == null) return 0;
            thought.pawn = pawn; thought.sourcePrecept = precept; thought.RecalculateState();
            return thought.Active ? Math.Min(0, thought.MoodOffset()) : 0;
        }
        public static PromiseRecord ThroneTerms(Pawn pawn)
        {
            if (!ModsConfig.RoyaltyActive) return null;
            var title = pawn.royalty?.HighestTitleWithThroneRoomRequirements();
            if (title == null || pawn.story?.traits?.HasTrait(TraitDefOf.Ascetic) == true) return null;
            var record = new PromiseRecord { Kind = PromiseKind.ThroneRoom, ExpansionRequest = true,
                SourceTitle = title.def, RequiredTicks = PromiseBalance.HoldTicks,
                BedroomRequirements = new List<BedroomRequirement>() };
            var room = pawn.ownership?.AssignedThrone?.GetRoom();
            foreach (var requirement in title.def.throneRoomRequirements)
            {
                if (requirement.Disabled(room, pawn)) continue;
                var copy = BedroomRequirement.Capture(requirement);
                if (copy == null) return null;
                record.BedroomRequirements.Add(copy);
            }
            return record;
        }
        public static RequestCandidate Throne(Pawn pawn, RequestEvaluation result)
        {
            result.Reason = "CP_ExpansionNotApplicable";
            var terms = ThroneTerms(pawn);
            if (terms == null)
            {
                if (ModsConfig.RoyaltyActive && pawn.royalty?.HighestTitleWithThroneRoomRequirements() != null
                    && pawn.story?.traits?.HasTrait(TraitDefOf.Ascetic) != true) result.Reason = "CP_ThroneUnsupported";
                return null;
            }
            result.Current = new ThroneRoomCondition().Describe(terms);
            if (EquipmentNeeds.NegativeMood(pawn, "TitleNoThroneRoom") >= 0
                && EquipmentNeeds.NegativeMood(pawn, "TitleThroneroomRequirementsNotMet") >= 0) return null;
            if (new ThroneRoomCondition().Observe(pawn, terms).Satisfied) { result.Reason = "CP_DebugSatisfied"; return null; }
            string signature = "throne-v1:" + terms.SourceTitle.defName + ":" + DlcCompatibility.RequirementSignature(terms.BedroomRequirements);
            result.HasDeficit = true; result.DeficitSignature = signature;
            result.Reason = null;
            return new RequestCandidate { Terms = terms, Signature = signature, ReasonKey = "CP_ThroneReason",
                Magnitude = pawn.ownership?.AssignedThrone == null ? 1 : 0 };
        }
        private static bool Applies(Pawn pawn, ApparelRequirementWithSource source) =>
            (source.sourceTitle != null ? ModsConfig.RoyaltyActive && pawn.royalty?.allowApparelRequirements == true
                : ModsConfig.IdeologyActive && source.sourceRole != null)
            && ApparelUtility.IsRequirementActive(source.requirement, source.Source, pawn, out _);
        private static PromiseRecord ApparelTerms(Pawn pawn, string cause, ThingDef def, ApparelRequirement requirement,
            RoyalTitleDef title, Precept precept, Thing problem = null, QualityCategory? quality = null)
        {
            var copy = new ApparelRequirement();
            if (requirement != null)
            {
                requirement.CopyTo(copy);
                copy.bodyPartGroupsMatchAny = requirement.bodyPartGroupsMatchAny == null ? null : new List<BodyPartGroupDef>(requirement.bodyPartGroupsMatchAny);
                copy.requiredDefs = requirement.requiredDefs == null ? null : new List<ThingDef>(requirement.requiredDefs);
                copy.requiredTags = requirement.requiredTags == null ? null : new List<string>(requirement.requiredTags);
                copy.allowedTags = requirement.allowedTags == null ? null : new List<string>(requirement.allowedTags);
            }
            return new PromiseRecord { Kind = PromiseKind.Equipment, ExpansionRequest = true,
                NeedBasedEquipment = true, EquipmentCause = cause, EquipmentDef = def,
                ApparelRequirement = requirement == null ? null : copy, SourceTitle = title,
                SourceIdeology = precept == null ? null : IdeologyKey(pawn), SourcePreceptId = precept?.Id ?? -1,
                QualityOptional = !quality.HasValue, MinimumQuality = quality ?? QualityCategory.Normal,
                ProblemThing = problem, AvoidHumanLeather = DlcCompatibility.AvoidHumanLeather(pawn),
                RequiredTicks = PromiseBalance.HoldTicks };
        }
        private static IEnumerable<PromiseRecord> ApparelNeeds(Pawn pawn, string cause, bool manual)
        {
            if (!Enabled(cause) || pawn.apparel == null) yield break;
            if (cause == "RoyalApparel" || cause == "IdeoRoleApparel")
            {
                bool royal = cause == "RoyalApparel";
                if (royal && pawn.royalty?.allowApparelRequirements != true) yield break;
                foreach (var source in pawn.apparel.AllRequirements)
                {
                    if ((source.sourceTitle != null) != royal || !Applies(pawn, source)
                        || !manual && source.requirement.IsMet(pawn)) continue;
                    if (!manual && (royal ? EquipmentNeeds.NegativeMood(pawn, "TitleApparelRequirementNotMet")
                        : PreceptMood(pawn, "IdeoRoleApparelRequirementNotMet", source.sourceRole)) >= 0) continue;
                    yield return ApparelTerms(pawn, cause, null, source.requirement, source.sourceTitle?.def, source.sourceRole);
                }
                if (royal && (manual || EquipmentNeeds.NegativeMood(pawn, "TitleApparelMinQualityNotMet") < 0))
                    foreach (var title in pawn.royalty.AllTitlesInEffectForReading.OrderByDescending(t => t.def.seniority))
                        foreach (var item in pawn.apparel.WornApparel.Where(a => a.TryGetQuality(out var q) && q < title.def.requiredMinimumApparelQuality))
                            yield return ApparelTerms(pawn, cause, item.def, null, title.def, null, item, title.def.requiredMinimumApparelQuality);
                yield break;
            }
            if (pawn.Ideo == null) yield break;
            foreach (var precept in pawn.Ideo.PreceptsListForReading.OfType<Precept_Apparel>().OrderBy(p => p.Id))
            {
                if (precept.apparelDef == null || precept.TargetGender != Gender.None && precept.TargetGender != pawn.gender
                    || !precept.def.comps.Any(c => c is PreceptComp_Apparel_Desired || c is PreceptComp_Apparel_DesiredStrong)
                    || !manual && pawn.apparel.WornApparel.Any(a => a.def == precept.apparelDef)) continue;
                yield return ApparelTerms(pawn, cause, precept.apparelDef, null, null, precept);
            }
        }
        public static IEnumerable<PromiseRecord> ApparelOptions(Pawn pawn, string cause, bool manual)
        {
            foreach (var need in ApparelNeeds(pawn, cause, manual))
                foreach (var target in need.EquipmentDef != null ? (IEnumerable<ThingDef>)new[] { need.EquipmentDef }
                    : need.ApparelRequirement.AllAllowedApparelForPawn(pawn, false, manual).OrderBy(d => d.defName, StringComparer.Ordinal))
                {
                    var terms = need.CopyForPreview(); terms.EquipmentDef = target; yield return terms;
                }
        }
        public static bool UsableApparel(Pawn pawn, PromiseRecord record) => new EquipmentCondition().ValidParameters(record)
            && EquipmentNeeds.UsableDefinition(pawn, record)
            && CompatibleDesired(pawn, record.EquipmentDef)
            && DlcCompatibility.ApparelDefinition(pawn, record.EquipmentDef)
            && (record.ApparelRequirement == null || record.ApparelRequirement.ApparelMeetsRequirement(record.EquipmentDef));
        private static bool CompatibleDesired(Pawn pawn, ThingDef target) => !ModsConfig.IdeologyActive || pawn.Ideo == null
            || pawn.Ideo.PreceptsListForReading.OfType<Precept_Apparel>().All(p => p.apparelDef == null
                || p.TargetGender != Gender.None && p.TargetGender != pawn.gender
                || !p.def.comps.Any(c => c is PreceptComp_Apparel_Desired || c is PreceptComp_Apparel_DesiredStrong)
                || p.apparelDef == target || ApparelUtility.CanWearTogether(target, p.apparelDef, pawn.RaceProps.body));
        public static RequestCandidate Apparel(Pawn pawn, RequestEvaluation result, string requestedCause)
        {
            var causes = requestedCause == null ? ApparelCauses.AsEnumerable() : new[] { requestedCause };
            foreach (string cause in causes.Where(IsApparel))
            {
                var needs = ApparelNeeds(pawn, cause, false).ToList();
                if (needs.Count == 0) continue;
                result.HasDeficit = true;
                result.DeficitSignature = "expansion-apparel-v1:" + cause + ":" + (needs[0].SourceTitle?.defName ?? needs[0].SourceIdeology)
                    + ":" + needs[0].SourcePreceptId;
                result.Current = "CP_ExpansionApparelPartial".Translate();
                result.Reason = "CP_DlcGearNoSolution";
                foreach (var terms in ApparelOptions(pawn, cause, false))
                {
                    string signature = "expansion-apparel-v1:" + cause + ":" + (terms.SourceTitle?.defName ?? terms.SourceIdeology)
                        + ":" + terms.SourcePreceptId;
                    result.HasDeficit = true; result.DeficitSignature = signature;
                    result.Current = "CP_ExpansionApparelPartial".Translate();
                    if (!UsableApparel(pawn, terms) || !EquipmentNeeds.CanObtain(pawn, terms, out string availability))
                    { result.Reason = "CP_DlcGearNoSolution"; continue; }
                    result.Current += "\n" + availability; result.Reason = null;
                    return new RequestCandidate { Terms = terms, Signature = signature, ReasonKey = "CP_GearReason_" + cause };
                }
            }
            if (requestedCause != null && !result.HasDeficit) result.Reason = "CP_ExpansionNotApplicable";
            return null;
        }
        public static bool CurrentApparel(Pawn pawn, PromiseRecord record) => Enabled(record.EquipmentCause)
            && ApparelOptions(pawn, record.EquipmentCause, false).Any(r => r.EquipmentDef == record.EquipmentDef
                && r.SourceTitle == record.SourceTitle && r.SourcePreceptId == record.SourcePreceptId
                && r.SourceIdeology == record.SourceIdeology && r.MinimumQuality == record.MinimumQuality
                && r.QualityOptional == record.QualityOptional) && UsableApparel(pawn, record);
        public static StyleItemDef CurrentStyle(Pawn pawn, bool beard) => beard ? (StyleItemDef)pawn.style?.beardDef : pawn.story?.hairDef;
        public static StyleItemDef TargetStyle(PromiseRecord record) => record.StyleBeard ? (StyleItemDef)record.TargetBeard : record.TargetHair;
        public static bool StyleAllowed(Pawn pawn, StyleItemDef def) => ModsConfig.IdeologyActive && def != null && pawn.style != null
            && StyleDataReady(pawn) && pawn.Ideo.style.GetFrequency(def) != StyleItemFrequency.Never
            && PawnStyleItemChooser.WantsToUseStyle(pawn, def)
            && PawnStyleItemChooser.FrequencyFromGender(def, pawn) > 0;
        public static IEnumerable<StyleItemDef> Styles(Pawn pawn, bool beard) =>
            (beard ? DefDatabase<BeardDef>.AllDefsListForReading.Cast<StyleItemDef>() : DefDatabase<HairDef>.AllDefsListForReading)
                .Where(d => d != CurrentStyle(pawn, beard) && StyleAllowed(pawn, d)).OrderBy(d => d.defName, StringComparer.Ordinal);
        public static bool UnwantedStyle(Pawn pawn, bool beard) => ModsConfig.IdeologyActive && pawn.Ideo != null
            && StyleDataReady(pawn) && pawn.style?.CanDesireLookChange == true && CurrentStyle(pawn, beard) != null
            && pawn.Ideo.style.GetFrequency(CurrentStyle(pawn, beard)) == StyleItemFrequency.Never
            && !PawnStyleItemChooser.WantsToUseStyle(pawn, CurrentStyle(pawn, beard));
        public static PromiseRecord StyleTerms(Pawn pawn, bool beard, StyleItemDef target) => new PromiseRecord {
            Kind = PromiseKind.Style, ExpansionRequest = true, StyleBeard = beard,
            TargetHair = target as HairDef, TargetBeard = target as BeardDef,
            OriginalHair = pawn.story?.hairDef, OriginalBeard = pawn.style?.beardDef,
            SourceIdeology = IdeologyKey(pawn), RequiredTicks = RequestRules.StyleRequiredTicks };
        public static bool StylingAvailable(Pawn pawn) => pawn.Map != null && pawn.Map.listerBuildings.allBuildingsColonist
            .Any(b => b.def == ThingDefOf.StylingStation && !b.IsForbidden(pawn)
                && pawn.CanReach(b, PathEndMode.InteractionCell, Danger.Some))
            && !pawn.Downed && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
        public static RequestCandidate Style(Pawn pawn, RequestEvaluation result, string subtype)
        {
            result.Reason = "CP_ExpansionNotApplicable";
            if (ModsConfig.IdeologyActive && pawn.Ideo != null && !StyleDataReady(pawn))
            { result.Reason = "CP_StyleDataUnavailable"; return null; }
            foreach (bool beard in new[] { false, true })
            {
                if (subtype != null && (subtype == "BeardStyle") != beard || !UnwantedStyle(pawn, beard)) continue;
                result.HasDeficit = true;
                result.DeficitSignature = "style-v1:" + IdeologyKey(pawn) + ":" + beard;
                result.Current = "CP_StyleCurrent".Translate(CurrentStyle(pawn, beard).LabelCap);
                var target = Styles(pawn, beard).FirstOrDefault();
                if (target == null || !StylingAvailable(pawn)) { result.Reason = "CP_StyleNoSolution"; continue; }
                result.Reason = null;
                return new RequestCandidate { Terms = StyleTerms(pawn, beard, target), Signature = result.DeficitSignature,
                    ReasonKey = beard ? "CP_BeardReason" : "CP_HairReason" };
            }
            return null;
        }
        public static bool Applicable(Pawn pawn, PromiseRecord record)
        {
            if (!record.ExpansionRequest) return true;
            if (record.SourceTitle != null)
            {
                if (!ModsConfig.RoyaltyActive || pawn.royalty == null) return false;
                if (record.Kind == PromiseKind.ThroneRoom)
                {
                    var current = ThroneTerms(pawn);
                    return current != null && current.BedroomRequirements.Count > 0 && (current.SourceTitle.seniority > record.SourceTitle.seniority
                        || DlcCompatibility.SameBedroomRequirements(current, record));
                }
                return pawn.royalty.allowApparelRequirements && pawn.story?.traits?.HasTrait(TraitDefOf.Ascetic) != true
                    && (record.ApparelRequirement != null
                        ? pawn.apparel.AllRequirements.Any(s => s.sourceTitle != null && Applies(pawn, s)
                            && s.requirement.ApparelMeetsRequirement(record.EquipmentDef))
                        : pawn.royalty.AllTitlesInEffectForReading.Any(t => t.def.requiredMinimumApparelQuality >= record.MinimumQuality));
            }
            if (!ModsConfig.IdeologyActive || IdeologyKey(pawn) != record.SourceIdeology) return false;
            if (record.Kind == PromiseKind.Style)
            {
                if (!StyleDataReady(pawn)) return true; // Unavailable preference data is not proof of lost applicability.
                var original = record.StyleBeard ? (StyleItemDef)record.OriginalBeard : record.OriginalHair;
                return pawn.style?.CanDesireLookChange == true && StyleAllowed(pawn, TargetStyle(record))
                    && (record.DevManual || original != null && !PawnStyleItemChooser.WantsToUseStyle(pawn, original));
            }
            return pawn.Ideo.PreceptsListForReading.Any(p => p.Id == record.SourcePreceptId
                && (p is Precept_Apparel apparel ? apparel.apparelDef == record.EquipmentDef
                    : pawn.apparel.AllRequirements.Any(s => s.sourceRole == p && Applies(pawn, s)
                        && s.requirement.ApparelMeetsRequirement(record.EquipmentDef))));
        }
    }
}
