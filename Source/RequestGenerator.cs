using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace ColonyPromises
{
    public sealed class RequestCandidate
    {
        public PromiseRecord Terms;
        public string Signature;
        public string ReasonKey;
        public ExpectationDef Expectation;
        public int Magnitude;
    }

    public static class RequestGenerator
    {
        public static RequestCandidate Candidate(Pawn pawn, PromiseKind kind) => Evaluate(pawn, kind).Candidate;

        public static RequestEvaluation Evaluate(Pawn pawn, PromiseKind kind)
        {
            var result = new RequestEvaluation();
            result.Reason = PromiseConditions.EligibilityReason(pawn);
            if (result.Reason != null) return result;
            if (!pawn.Spawned || pawn.Suspended) result.Reason = "CP_DebugAbsent";
            else if (!pawn.Map.IsPlayerHome) result.Reason = "CP_DebugHome";
            else if (pawn.Downed || pawn.InMentalState || pawn.Drafted) result.Reason = "CP_DebugBusy";
            if (result.Reason != null) return result;
            switch (kind)
            {
                case PromiseKind.Bedroom: result.Candidate = Bedroom(pawn, result); break;
                case PromiseKind.Leisure:
                    result.Current = pawn.needs?.joy == null ? "CP_DebugNoJoy".Translate().ToString()
                        : "CP_DebugJoy".Translate(Number(pawn.needs.joy.CurLevel * 100), Number(RequestRules.LowJoy * 100)).ToString();
                    if (pawn.needs?.joy == null) result.Reason = "CP_DebugNoJoy";
                    else if (pawn.needs.joy.CurLevel >= RequestRules.LowJoy) result.Reason = "CP_DebugSatisfied";
                    else
                    {
                        result.HasDeficit = true;
                        result.Candidate = new RequestCandidate { Terms = new PromiseRecord { Kind = kind },
                            Signature = "joy", ReasonKey = "CP_ReasonLeisure" };
                    }
                    break;
                case PromiseKind.Equipment: result.Candidate = Equipment(pawn, result); break;
            }
            return result;
        }

        public static string Number(float value) => value.ToString("0.##",
            LanguageDatabase.activeLanguage?.folderName == "Ukrainian"
                ? System.Globalization.CultureInfo.GetCultureInfo("uk-UA")
                : System.Globalization.CultureInfo.GetCultureInfo("en-US"));

        private static bool Trait(Pawn pawn, TraitDef def) => pawn.story?.traits?.HasTrait(def) == true;

        private static RequestCandidate Bedroom(Pawn pawn, RequestEvaluation result)
        {
            if (Trait(pawn, TraitDefOf.Ascetic)) { result.Reason = "CP_DebugAscetic"; return null; }
            var expectation = ExpectationsUtility.CurrentExpectationFor(pawn);
            if (expectation == null) { result.Reason = "CP_DebugExpectation"; return null; }
            int desired = RequestRules.BedroomStage(expectation.order, Trait(pawn, TraitDefOf.Greedy),
                RoomStatDefOf.Impressiveness.scoreStages.Count);
            var current = new BedroomCondition().Observe(pawn, new PromiseRecord { RoomStage = 0 });
            int stage = current.Satisfied ? RoomStatDefOf.Impressiveness.GetScoreStageIndex(current.Room.GetStat(RoomStatDefOf.Impressiveness)) : -1;
            int target = stage < 0 ? desired : RequestRules.RoomTarget(stage, desired);
            result.Current = "CP_DebugRoom".Translate(stage < 0 ? "CP_DebugNoBedroom".Translate().ToString()
                : BedroomCondition.RoomLabel(stage), expectation.label, BedroomCondition.RoomLabel(desired)).ToString();
            if (target < 0) { result.Reason = "CP_DebugSatisfied"; return null; }
            result.HasDeficit = true;
            return new RequestCandidate
            {
                Terms = new PromiseRecord { Kind = PromiseKind.Bedroom, RoomStage = target, RequiredTicks = PromiseBalance.HoldTicks },
                Signature = "room:" + target,
                ReasonKey = stage < 0 ? "CP_ReasonBedroomMissing" : "CP_ReasonBedroomImprove",
                Expectation = expectation,
                Magnitude = stage < 0 || target - stage >= 2 ? 1 : 0
            };
        }

        private static RequestCandidate Equipment(Pawn pawn, RequestEvaluation result)
        {
            // Only improve an item the pawn already uses. Loaded defs are discovered via actual stock.
            IEnumerable<Thing> used = pawn.apparel?.WornApparel.Cast<Thing>() ?? Enumerable.Empty<Thing>();
            if (!pawn.WorkTagIsDisabled(WorkTags.Violent))
                used = used.Concat(pawn.equipment?.AllEquipmentListForReading.Cast<Thing>() ?? Enumerable.Empty<Thing>());
            var stock = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            var descriptions = new List<string>();
            bool deficient = false;
            result.Reason = "CP_DebugNoGear";
            foreach (var item in used.OrderBy(t => t.thingIDNumber))
            {
                if (!EquipmentCondition.IsSupported(item.def) || !item.TryGetQuality(out var quality)) continue;
                if (item.def.IsApparel && (Trait(pawn, TraitDefOf.Nudist) || !ApparelUtility.HasPartsToWear(pawn, item.def))) continue;
                if (item.def.IsRangedWeapon && Trait(pawn, TraitDefOf.Brawler)) continue;
                descriptions.Add("CP_DebugItem".Translate(item.LabelCap.ToString(), quality.GetLabel(),
                    Number(item.HitPoints * 100f / item.MaxHitPoints)).ToString());
                bool worn = item.def.IsApparel && item.HitPoints / (float)item.MaxHitPoints < RequestRules.WornFraction;
                bool poor = quality < QualityCategory.Normal;
                if (!worn && !poor) continue;
                deficient = true;
                result.HasDeficit = true;
                var targetQuality = poor ? (QualityCategory)((int)quality + 1) : quality;
                float durability = item.def.IsApparel ? RequestRules.ReplacementFraction : 0;
                bool available = stock.Any(t => t.def == item.def && t.Spawned && !t.IsForbidden(pawn)
                    && t.TryGetQuality(out var q) && q >= targetQuality
                    && (durability <= 0 || t.HitPoints / (float)t.MaxHitPoints >= durability)
                    && EquipmentUtility.CanEquip(t, pawn)
                    && pawn.CanReach(t, PathEndMode.Touch, Danger.Some));
                result.Current = string.Join("; ", descriptions);
                if (!available) { result.Reason = "CP_DebugStock"; continue; }
                result.Reason = null;
                return new RequestCandidate
                {
                    Terms = new PromiseRecord { Kind = PromiseKind.Equipment, EquipmentDef = item.def,
                        MinimumQuality = targetQuality, MinimumHitPointsFraction = durability, RequiredTicks = PromiseBalance.HoldTicks },
                    Signature = "gear:" + item.thingIDNumber + ":" + (int)targetQuality + ":" + worn,
                    ReasonKey = worn ? "CP_ReasonEquipmentWorn" : "CP_ReasonEquipmentQuality",
                    Magnitude = worn && poor ? 1 : 0
                };
            }
            result.Current = string.Join("; ", descriptions);
            if (!deficient && descriptions.Count > 0) result.Reason = "CP_DebugSatisfied";
            return null;
        }

        public static DeficitObservation Project(DeficitObservation saved, RequestCandidate candidate, PromiseKind kind, int now, bool severe)
        {
            var result = new DeficitObservation { Kind = kind, LastTick = now };
            if (candidate == null) return result;
            int previous = saved?.Ticks ?? 0;
            result.Ticks = RequestRules.ObserveTicks(previous, saved?.LastTick ?? -1, now,
                saved?.Signature == candidate.Signature);
            result.SevereTicks = saved?.SevereTicks ?? 0;
            if (saved?.Signature != candidate.Signature || result.Ticks < previous) result.SevereTicks = 0;
            if (severe) result.SevereTicks += Math.Max(0, result.Ticks - previous);
            int delta = now - (saved?.LastTick ?? -1);
            bool continuous = saved != null && saved.Signature == candidate.Signature
                && saved.LastTick >= 0 && delta >= 0 && delta <= RequestRules.ObservationInterval;
            result.StartTick = continuous ? (saved.StartTick >= 0 ? saved.StartTick : saved.LastTick - saved.Ticks) : now;
            result.Signature = candidate.Signature;
            return result;
        }

        public static RequestCandidate Observe(ColonistPromises state, PromiseKind kind, int now)
        {
            var candidate = Candidate(state.Pawn, kind);
            var observation = Project(state.Deficits.FirstOrDefault(d => d.Kind == kind), candidate, kind, now,
                kind == PromiseKind.Leisure && state.Pawn.needs?.joy?.CurLevel < RequestRules.SevereJoy);
            state.Deficits.RemoveAll(d => d.Kind == kind);
            state.Deficits.Add(observation);
            if (candidate == null) return null;
            if (!RequestRules.Ready(observation.Ticks, state.Request != null || state.Active != null, now, state.CooldownUntilTick)) return null;
            if (kind == PromiseKind.Leisure)
            {
                int hours = RequestRules.LeisureHours(observation.Ticks, observation.SevereTicks);
                candidate.Terms.RequiredTicks = hours * PromiseBalance.TicksPerHour;
                candidate.Magnitude = hours > RequestRules.MinHours ? 1 : 0;
            }
            return candidate;
        }

        public static KeyValuePair<ColonistPromises, RequestCandidate> SelectReady(
            IEnumerable<KeyValuePair<ColonistPromises, RequestCandidate>> ready) =>
            ready.OrderByDescending(x => x.Key.Deficits.First(d => d.Kind == x.Value.Terms.Kind).Ticks)
                .ThenBy(x => {
                    var d = x.Key.Deficits.First(o => o.Kind == x.Value.Terms.Kind);
                    return d.StartTick >= 0 ? d.StartTick : d.LastTick - d.Ticks;
                })
                .ThenBy(x => x.Key.Pawn.thingIDNumber).ThenBy(x => x.Value.Terms.Kind).First();

        public static bool Relevant(Pawn pawn, ColonistRequest request)
        {
            if (request?.Terms == null) return false;
            var condition = PromiseConditions.Get(request.Terms.Kind);
            if (condition == null || !condition.ValidParameters(request.Terms)) return false;
            if (condition.UsesContinuousHold && condition.Observe(pawn, request.Terms).Satisfied) return false;
            var candidate = Candidate(pawn, request.Terms.Kind);
            if (candidate == null) return false;
            return request.Terms.Kind != PromiseKind.Equipment
                || (candidate.Terms.EquipmentDef == request.Terms.EquipmentDef
                    && candidate.Terms.MinimumQuality == request.Terms.MinimumQuality
                    && candidate.Terms.MinimumHitPointsFraction == request.Terms.MinimumHitPointsFraction);
        }

        public static ColonistRequest Offer(RequestCandidate candidate, int now) => new ColonistRequest
        {
            Terms = candidate.Terms, ReasonKey = candidate.ReasonKey, Expectation = candidate.Expectation,
            Magnitude = candidate.Magnitude, OfferedTick = now,
            ExpiryTick = now + RequestRules.ResponseDays * PromiseBalance.TicksPerDay,
            DeadlineDays = RequestRules.Deadline((int)candidate.Terms.Kind, candidate.Magnitude)
        };

        public static void ResetObservations(ColonistPromises state) => state.Deficits.Clear();
    }
}
