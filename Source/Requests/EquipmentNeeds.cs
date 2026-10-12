using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace ColonyPromises
{
    public static class EquipmentNeeds
    {
        public static readonly IReadOnlyList<string> Causes = Array.AsReadOnly(new[] { "ApparelDamaged", "Naked", "BrawlerUnhappy", "WeaponUpgrade" });
        public static bool? ChooseRanged(bool violent, bool brawler, bool shootingDisabled, bool meleeDisabled,
            int shooting, int melee, Passion shootingPassion, Passion meleePassion, bool? equippedRanged, out string reason)
        {
            reason = "CP_WeaponChoiceBlocked";
            if (!violent || shootingDisabled && meleeDisabled) return null;
            if (brawler) { reason = "CP_WeaponChoiceBrawler"; return meleeDisabled ? (bool?)null : false; }
            if (shootingDisabled || meleeDisabled) { reason = "CP_WeaponChoiceRestriction"; return !shootingDisabled; }
            if (Math.Abs(shooting - melee) >= RequestRules.WeaponSkillDifference)
            { reason = "CP_WeaponChoiceLevelGap"; return shooting > melee; }
            if (equippedRanged.HasValue) { reason = "CP_WeaponChoiceEquipped"; return equippedRanged; }
            if (shootingPassion != meleePassion) { reason = "CP_WeaponChoicePassion"; return shootingPassion > meleePassion; }
            if (shooting != melee) { reason = "CP_WeaponChoiceLevel"; return shooting > melee; }
            reason = "CP_WeaponChoiceTie";
            return true;
        }

        public static bool? PreferredRanged(Pawn pawn, out string reason)
        {
            var shooting = pawn.skills?.GetSkill(SkillDefOf.Shooting);
            var melee = pawn.skills?.GetSkill(SkillDefOf.Melee);
            var primary = pawn.equipment?.Primary;
            bool? equipped = primary != null && UsableDefinition(pawn, new PromiseRecord { EquipmentDef = primary.def })
                && EquipmentUtility.CanEquip(primary, pawn) ? (bool?)primary.def.IsRangedWeapon : null;
            return ChooseRanged(!pawn.WorkTagIsDisabled(WorkTags.Violent)
                && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation),
                pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true,
                shooting == null || shooting.TotallyDisabled, melee == null || melee.TotallyDisabled,
                shooting?.Level ?? 0, melee?.Level ?? 0, shooting?.passion ?? Passion.None,
                melee?.passion ?? Passion.None, equipped, out reason);
        }

        public static bool WeaponTargetCurrent(Pawn pawn, PromiseRecord terms)
        {
            if (!terms.EquipmentDef.IsWeapon) return true;
            var ranged = PreferredRanged(pawn, out _);
            return WeaponTypeMatches(terms.EquipmentDef, ranged);
        }

        public static bool WeaponTypeMatches(ThingDef def, bool? ranged) => ranged.HasValue
            && (ranged.Value ? def.IsRangedWeapon : def.IsMeleeWeapon);

        public static QualityCategory? UpgradeQuality(QualityCategory quality, int skill)
        {
            if ((int)quality < RequestRules.WeaponUpgradeBaseQuality) return (QualityCategory)RequestRules.WeaponUpgradeBaseQuality;
            if ((int)quality == RequestRules.WeaponUpgradeBaseQuality && skill >= RequestRules.WeaponUpgradeSkill)
                return (QualityCategory)RequestRules.WeaponUpgradeSkilledQuality;
            return null;
        }

        public static bool UpgradeTarget(Pawn pawn, out Thing primary, out QualityCategory minimum)
        {
            primary = pawn.equipment?.Primary;
            minimum = QualityCategory.Normal;
            if (primary == null || !primary.def.IsWeapon || !primary.TryGetQuality(out var quality)
                || !WeaponTargetCurrent(pawn, new PromiseRecord { EquipmentDef = primary.def })
                || !UsableDefinition(pawn, new PromiseRecord { EquipmentDef = primary.def })
                || !EquipmentUtility.CanEquip(primary, pawn)) return false;
            var skill = pawn.skills.GetSkill(primary.def.IsRangedWeapon ? SkillDefOf.Shooting : SkillDefOf.Melee);
            var target = UpgradeQuality(quality, skill.Level);
            if (!target.HasValue) return false;
            minimum = target.Value;
            return true;
        }

        // Read a fresh, unattached situational thought; viewing diagnostics does not dirty the pawn's cache.
        public static float NegativeMood(Pawn pawn, string name)
        {
            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(name);
            if (def == null || pawn.needs?.mood == null) return 0;
            var thought = ThoughtMaker.MakeThought(def) as Thought_Situational;
            if (thought == null) return 0;
            thought.pawn = pawn;
            thought.RecalculateState();
            return thought.Active ? Math.Min(0, thought.MoodOffset()) : 0;
        }

        public static bool Damaged(bool hitPoints, bool cares, bool locked, float fraction) =>
            hitPoints && cares && !locked && fraction < RequestRules.WornFraction;
        public static bool Damaged(Pawn pawn, Apparel item) => Damaged(item.def.useHitPoints,
            item.def.apparel.careIfDamaged, pawn.apparel.IsLocked(item), item.HitPoints / (float)item.MaxHitPoints);
        public static bool Confirmed(string cause, float mood, bool current) => mood < 0 && current
            && (cause == "ApparelDamaged" || cause == "Naked" || cause == "BrawlerUnhappy");

        public static RequestCandidate Evaluate(Pawn pawn, RequestEvaluation result)
        {
            result.Reason = "CP_GearNoCause";
            var details = new List<string>();
            var ranged = PreferredRanged(pawn, out string weaponReason);
            details.Add("CP_WeaponChoice".Translate((ranged.HasValue ? ranged.Value ? "CP_WeaponRanged" : "CP_WeaponMelee"
                : "CP_DebugNo").Translate(), weaponReason.Translate()).ToString());
            bool brawlerDeficit = false;
            foreach (string cause in Causes.Where(c => c != "WeaponUpgrade"))
            {
                float mood = NegativeMood(pawn, cause);
                if (mood >= 0) continue;
                Thing source = null;
                if (cause == "ApparelDamaged") source = pawn.apparel?.WornApparel
                    .Where(a => Damaged(pawn, a)).OrderBy(a => a.HitPoints / (float)a.MaxHitPoints)
                    .ThenBy(a => a.thingIDNumber).FirstOrDefault();
                if (cause == "BrawlerUnhappy") source = pawn.equipment?.Primary;
                bool current = cause == "Naked" ? pawn.apparel?.PsychologicallyNude == true
                    : cause == "ApparelDamaged" ? source != null
                    : source?.def.IsRangedWeapon == true && !pawn.WorkTagIsDisabled(WorkTags.Violent);
                details.Add("CP_GearEvidence".Translate(("CP_GearCause_" + cause).Translate(), PromiseText.Signed(mood),
                    (current ? "CP_DebugYes" : "CP_DebugNo").Translate()).ToString());
                if (source != null) details.Add("CP_GearPiece".Translate(source.LabelCap,
                    source.def.useHitPoints ? PromiseText.Number(source.HitPoints * 100f / source.MaxHitPoints) : "100").ToString());
                if (!Confirmed(cause, mood, current)) continue;
                if (cause == "BrawlerUnhappy") brawlerDeficit = true;
                result.HasDeficit = true;
                string signature = "equipment-need-v2:" + cause + ":" + (source?.thingIDNumber ?? 0);
                if (result.DeficitSignature == null) result.DeficitSignature = signature;
                IEnumerable<ThingDef> targets = cause == "ApparelDamaged" ? new[] { source.def }
                    : DefDatabase<ThingDef>.AllDefsListForReading.Where(d => cause == "Naked" ? d.IsApparel
                        : ranged == false && WeaponTypeMatches(d, ranged))
                        .OrderBy(d => d.defName, StringComparer.Ordinal);
                foreach (var def in targets)
                {
                    var terms = new PromiseRecord { Kind = PromiseKind.Equipment, EquipmentDef = def,
                        NeedBasedEquipment = true, QualityOptional = true, EquipmentCause = cause, ProblemThing = source,
                        CauseMood = mood,
                        RequireCoverage = cause == "Naked", RequireMelee = cause == "BrawlerUnhappy",
                        MinimumHitPointsFraction = def.IsApparel && def.useHitPoints ? RequestRules.ReplacementFraction : 0,
                        RequiredTicks = PromiseBalance.HoldTicks };
                    if (terms.RequireCoverage && !CoversMissingBasics(pawn, def)) continue;
                    if (def.IsApparel && !DlcCompatibility.ApparelDefinition(pawn, def)) continue;
                    terms.AvoidHumanLeather = def.IsApparel && DlcCompatibility.AvoidHumanLeather(pawn);
                    if (!UsableDefinition(pawn, terms) || !CanObtain(pawn, terms, out string availability)) continue;
                    result.DeficitSignature = signature;
                    result.Current = string.Join("\n", details) + "\n" + availability;
                    result.Reason = null;
                    return new RequestCandidate { Terms = terms, Signature = signature,
                        ReasonKey = "CP_GearReason_" + cause,
                        // Retain the existing equipment reward tier without making quality a fulfillment requirement.
                        Magnitude = cause == "ApparelDamaged" && source.TryGetQuality(out var previousQuality)
                            && previousQuality < QualityCategory.Normal ? 1 : 0 };
                }
                result.Reason = ModsConfig.IdeologyActive || ModsConfig.RoyaltyActive ? "CP_DlcGearNoSolution" : "CP_GearNoSolution";
            }
            if (!brawlerDeficit && UpgradeTarget(pawn, out var weapon, out var minimum))
            {
                // Model and threshold identify the desire; swapping copies does not supply extra observed time.
                string signature = "weapon-upgrade-v1:" + weapon.def.defName + ":" + (int)minimum;
                var terms = new PromiseRecord { Kind = PromiseKind.Equipment, EquipmentDef = weapon.def,
                    NeedBasedEquipment = true, EquipmentCause = "WeaponUpgrade", MinimumQuality = minimum,
                    RequiredTicks = PromiseBalance.HoldTicks };
                result.HasDeficit = true;
                result.DeficitSignature = signature;
                details.Add("CP_WeaponUpgradeEvidence".Translate(weapon.LabelCap, minimum.GetLabel()).ToString());
                if (UsableDefinition(pawn, terms) && CanObtain(pawn, terms, out var availability))
                {
                    result.Current = string.Join("\n", details) + "\n" + availability;
                    result.Reason = null;
                    return new RequestCandidate { Terms = terms, Signature = signature,
                        ReasonKey = "CP_GearReason_WeaponUpgrade", Magnitude = 0 };
                }
                result.Reason = "CP_GearNoSolution";
            }
            result.Current = string.Join("\n", details);
            if (ModsConfig.IdeologyActive) result.Current += "\n" + "CP_GearIdeology".Translate();
            return null;
        }

        public static string Reason(ColonistRequest request) => request?.Terms?.NeedBasedEquipment == true
            ? request.ReasonKey.Translate(request.Terms.EquipmentDef.LabelCap,
                PromiseText.Signed(request.Terms.CauseMood), RequestRules.PersistentTicks / PromiseBalance.TicksPerDay).ToString()
            : null;

        public static bool CauseCurrent(Pawn pawn, PromiseRecord r)
        {
            if (r.EquipmentCause == "WeaponUpgrade") return r.NeedBasedEquipment
                && UpgradeTarget(pawn, out var weapon, out var minimum)
                && weapon.def == r.EquipmentDef && minimum == r.MinimumQuality
                && NegativeMood(pawn, "BrawlerUnhappy") >= 0;
            if (!r.NeedBasedEquipment || NegativeMood(pawn, r.EquipmentCause) >= 0) return false;
            if (r.EquipmentCause == "ApparelDamaged") return r.ProblemThing is Apparel a
                && pawn.apparel.WornApparel.Contains(a) && Damaged(pawn, a);
            if (r.EquipmentCause == "Naked") return pawn.apparel?.PsychologicallyNude == true;
            return r.EquipmentCause == "BrawlerUnhappy" && !pawn.WorkTagIsDisabled(WorkTags.Violent)
                && pawn.equipment?.Primary?.def.IsRangedWeapon == true;
        }

        public static bool CoversMissingBasics(Pawn pawn, ThingDef def)
        {
            pawn.apparel.HasBasicApparel(out bool legs, out bool torso);
            bool hasLegs = pawn.health.hediffSet.GetNotMissingParts().Any(p => p.IsInGroup(BodyPartGroupDefOf.Legs));
            return CoverageSolution(!legs && hasLegs, pawn.gender == Gender.Female && !torso,
                def.apparel.bodyPartGroups.Contains(BodyPartGroupDefOf.Legs), def.apparel.bodyPartGroups.Contains(BodyPartGroupDefOf.Torso));
        }

        public static bool CoverageSolution(bool needsLegs, bool needsTorso, bool legs, bool torso) =>
            (!needsLegs || legs) && (!needsTorso || torso);

        public static bool UsableDefinition(Pawn pawn, PromiseRecord r)
        {
            var def = r.EquipmentDef;
            if (def == null || !def.PlayerAcquirable || (!def.IsApparel && !def.IsWeapon)) return false;
            if (def.IsApparel)
            {
                if (pawn.apparel == null || !def.apparel.PawnCanWear(pawn) || !ApparelUtility.HasPartsToWear(pawn, def)) return false;
                if (r.RequireCoverage && pawn.apparel.WornApparel.Any(a => a.def != def
                    && !ApparelUtility.CanWearTogether(def, a.def, pawn.RaceProps.body))) return false;
                if (pawn.apparel.WornApparel.Any(a => a.def != def && pawn.apparel.IsLocked(a)
                    && !ApparelUtility.CanWearTogether(def, a.def, pawn.RaceProps.body))) return false;
            }
            else if (pawn.WorkTagIsDisabled(WorkTags.Violent) || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                || r.RequireMelee && !def.IsMeleeWeapon) return false;
            // A definition-only probe allocates neither a game ID nor a spawned object.
            ThingWithComps probe = def.IsApparel ? new Apparel { def = def } : new ThingWithComps { def = def };
            return EquipmentUtility.CanEquip(probe, pawn);
        }

        public static bool Matches(Pawn pawn, Thing item, PromiseRecord r)
        {
            if (!MatchesParameters(item, r)) return false;
            if (!r.NeedBasedEquipment) return true;
            if (r.EquipmentCause == "ApparelDamaged" && r.ProblemThing is Apparel original && original != item
                && pawn.apparel.WornApparel.Contains(original) && Damaged(pawn, original)) return false;
            return UsableDefinition(pawn, r) && EquipmentUtility.CanEquip(item, pawn)
                && (!r.RequireCoverage || !pawn.apparel.PsychologicallyNude)
                && (!r.RequireMelee || pawn.equipment?.Primary == item);
        }

        public static bool AcceptsParameters(bool correctDef, bool hasQuality, QualityCategory quality,
            float condition, bool usesHitPoints, PromiseRecord r) => correctDef
            && (r.QualityOptional || hasQuality && quality >= r.MinimumQuality)
            && (r.MinimumHitPointsFraction <= 0 || (!r.NeedBasedEquipment || usesHitPoints)
                && condition >= r.MinimumHitPointsFraction);

        public static bool MatchesParameters(Thing item, PromiseRecord r)
        {
            if (r.AvoidHumanLeather && item.Stuff?.defName == "Leather_Human") return false;
            bool quality = item.TryGetQuality(out var category);
            return AcceptsParameters(item.def == r.EquipmentDef, quality, category,
                r.MinimumHitPointsFraction > 0 ? item.HitPoints / (float)item.MaxHitPoints : 1,
                item.def.useHitPoints, r);
        }

        public static bool CanObtain(Pawn pawn, PromiseRecord terms, out string reason)
        {
            var stock = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            if (stock.Any(t => MatchesParameters(t, terms) && !t.IsForbidden(pawn)
                && EquipmentUtility.CanEquip(t, pawn) && pawn.CanReach(t, PathEndMode.Touch, Danger.Some)))
            { reason = "CP_GearStock".Translate(); return true; }
            var target = terms.EquipmentDef;
            if ((target.thingClass != typeof(Apparel) && target.thingClass != typeof(ThingWithComps))
                || target.comps?.Any(c => c.compClass?.Assembly != typeof(Thing).Assembly
                    || c.GetType().Assembly != typeof(Thing).Assembly) == true)
            { reason = "CP_GearNoSolution".Translate(); return false; }
            foreach (var table in pawn.Map.listerBuildings.allBuildingsColonist.OfType<Building_WorkTable>())
            {
                if (!table.CurrentlyUsableForBills()) continue;
                foreach (var recipe in table.def.AllRecipes.Where(r => r.products?.Any(p => p.thingDef == terms.EquipmentDef) == true))
                {
                    // Custom recipe workers and special products need dedicated feasibility support.
                    if (recipe.Worker.GetType() != typeof(RecipeWorker) || !recipe.AvailableNow || !recipe.AvailableOnNow(table)
                        || recipe.ingredients == null || recipe.specialProducts?.Count > 0) continue;
                    bool crafter = pawn.Map.mapPawns.FreeColonistsSpawned.Any(p => PromiseConditions.Eligible(p)
                        && !p.Downed && p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                        && (recipe.requiredGiverWorkType == null || !p.WorkTypeIsDisabled(recipe.requiredGiverWorkType))
                        && (recipe.workSkill == null || p.skills?.GetSkill(recipe.workSkill)?.TotallyDisabled == false)
                        && (recipe.skillRequirements == null || recipe.skillRequirements.All(s => p.skills?.GetSkill(s.skill)?.Level >= s.minLevel))
                        && p.CanReach(table, PathEndMode.InteractionCell, Danger.Some));
                    if (!crafter || !IngredientsAvailable(pawn, recipe, stock, terms)) continue;
                    reason = "CP_GearCraft".Translate(table.LabelCap); return true;
                }
            }
            reason = "CP_GearNoSolution".Translate(); return false;
        }

        private static bool IngredientsAvailable(Pawn pawn, RecipeDef recipe, List<Thing> stock, PromiseRecord terms)
        {
            // Reserve stacks across requirements. Accept a conservative single-def material choice for each ingredient.
            var remaining = stock.Where(t => (!terms.AvoidHumanLeather || t.def.defName != "Leather_Human")
                && !t.IsForbidden(pawn) && pawn.CanReach(t, PathEndMode.Touch, Danger.Some))
                .GroupBy(t => t.def).ToDictionary(g => g.Key, g => g.Sum(t => t.stackCount));
            foreach (var ingredient in recipe.ingredients)
            {
                var def = remaining.Keys.OrderBy(d => d.defName, StringComparer.Ordinal).FirstOrDefault(d => ingredient.filter.Allows(d)
                    && recipe.fixedIngredientFilter.Allows(d) && (!recipe.productHasIngredientStuff || !d.IsStuff
                        || recipe.products.All(p => !p.thingDef.MadeFromStuff || GenStuff.AllowedStuffsFor(p.thingDef).Contains(d)))
                    && remaining[d] >= ingredient.CountRequiredOfFor(d, recipe));
                if (def == null) return false;
                remaining[def] -= ingredient.CountRequiredOfFor(def, recipe);
            }
            return true;
        }
    }
}
