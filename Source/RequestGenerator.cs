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
        public static RequestCandidate Candidate(Pawn pawn, PromiseKind kind)
        {
            if (!PromiseConditions.Observable(pawn) || !pawn.Map.IsPlayerHome || pawn.Downed
                || pawn.InMentalState || pawn.Drafted) return null;
            switch (kind)
            {
                case PromiseKind.Bedroom: return Bedroom(pawn);
                case PromiseKind.Leisure:
                    return pawn.needs?.joy != null && pawn.needs.joy.CurLevel < RequestRules.LowJoy
                        ? new RequestCandidate { Terms = new PromiseRecord { Kind = kind }, Signature = "joy", ReasonKey = "CP_ReasonLeisure" } : null;
                case PromiseKind.Equipment: return Equipment(pawn);
                default: return null;
            }
        }

        private static bool Trait(Pawn pawn, TraitDef def) => pawn.story?.traits?.HasTrait(def) == true;

        private static RequestCandidate Bedroom(Pawn pawn)
        {
            if (Trait(pawn, TraitDefOf.Ascetic)) return null;
            var expectation = ExpectationsUtility.CurrentExpectationFor(pawn);
            if (expectation == null) return null;
            int desired = RequestRules.BedroomStage(expectation.order, Trait(pawn, TraitDefOf.Greedy),
                RoomStatDefOf.Impressiveness.scoreStages.Count);
            var current = new BedroomCondition().Observe(pawn, new PromiseRecord { RoomStage = 0 });
            int stage = current.Satisfied ? RoomStatDefOf.Impressiveness.GetScoreStageIndex(current.Room.GetStat(RoomStatDefOf.Impressiveness)) : -1;
            int target = stage < 0 ? desired : RequestRules.RoomTarget(stage, desired);
            if (target < 0) return null;
            return new RequestCandidate
            {
                Terms = new PromiseRecord { Kind = PromiseKind.Bedroom, RoomStage = target, RequiredTicks = PromiseBalance.HoldTicks },
                Signature = "room:" + target,
                ReasonKey = stage < 0 ? "CP_ReasonBedroomMissing" : "CP_ReasonBedroomImprove",
                Expectation = expectation,
                Magnitude = stage < 0 || target - stage >= 2 ? 1 : 0
            };
        }

        private static RequestCandidate Equipment(Pawn pawn)
        {
            // Only improve an item the pawn already uses. Loaded defs are discovered via actual stock.
            IEnumerable<Thing> used = pawn.apparel?.WornApparel.Cast<Thing>() ?? Enumerable.Empty<Thing>();
            if (!pawn.WorkTagIsDisabled(WorkTags.Violent))
                used = used.Concat(pawn.equipment?.AllEquipmentListForReading.Cast<Thing>() ?? Enumerable.Empty<Thing>());
            var stock = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            foreach (var item in used.OrderBy(t => t.thingIDNumber))
            {
                if (!EquipmentCondition.IsSupported(item.def) || !item.TryGetQuality(out var quality)) continue;
                if (item.def.IsApparel && (Trait(pawn, TraitDefOf.Nudist) || !ApparelUtility.HasPartsToWear(pawn, item.def))) continue;
                if (item.def.IsRangedWeapon && Trait(pawn, TraitDefOf.Brawler)) continue;
                bool worn = item.def.IsApparel && item.HitPoints / (float)item.MaxHitPoints < RequestRules.WornFraction;
                bool poor = quality < QualityCategory.Normal;
                if (!worn && !poor) continue;
                var targetQuality = poor ? (QualityCategory)((int)quality + 1) : quality;
                float durability = item.def.IsApparel ? RequestRules.ReplacementFraction : 0;
                bool available = stock.Any(t => t.def == item.def && t.Spawned && !t.IsForbidden(pawn)
                    && t.TryGetQuality(out var q) && q >= targetQuality
                    && (durability <= 0 || t.HitPoints / (float)t.MaxHitPoints >= durability)
                    && EquipmentUtility.CanEquip(t, pawn)
                    && pawn.CanReach(t, PathEndMode.Touch, Danger.Some));
                if (!available) continue;
                return new RequestCandidate
                {
                    Terms = new PromiseRecord { Kind = PromiseKind.Equipment, EquipmentDef = item.def,
                        MinimumQuality = targetQuality, MinimumHitPointsFraction = durability, RequiredTicks = PromiseBalance.HoldTicks },
                    Signature = "gear:" + item.thingIDNumber + ":" + (int)targetQuality + ":" + worn,
                    ReasonKey = worn ? "CP_ReasonEquipmentWorn" : "CP_ReasonEquipmentQuality",
                    Magnitude = worn && poor ? 1 : 0
                };
            }
            return null;
        }

        public static RequestCandidate Observe(ColonistPromises state, PromiseKind kind, int now)
        {
            var observation = state.Deficits.FirstOrDefault(d => d.Kind == kind);
            if (observation == null) state.Deficits.Add(observation = new DeficitObservation { Kind = kind });
            var candidate = Candidate(state.Pawn, kind);
            if (candidate == null)
            {
                observation.Ticks = observation.SevereTicks = 0;
                observation.Signature = null;
                observation.LastTick = now;
                return null;
            }
            bool same = observation.Signature == candidate.Signature;
            int previousTicks = observation.Ticks;
            observation.Ticks = RequestRules.ObserveTicks(previousTicks, observation.LastTick, now, same);
            if (!same || observation.Ticks < previousTicks) observation.SevereTicks = 0;
            if (kind == PromiseKind.Leisure && state.Pawn.needs.joy.CurLevel < RequestRules.SevereJoy)
                observation.SevereTicks += Math.Max(0, observation.Ticks - previousTicks);
            observation.Signature = candidate.Signature;
            observation.LastTick = now;
            if (!RequestRules.Ready(observation.Ticks, state.Request != null || state.Active != null, now, state.CooldownUntilTick)) return null;
            if (kind == PromiseKind.Leisure)
            {
                int hours = RequestRules.LeisureHours(observation.Ticks, observation.SevereTicks);
                candidate.Terms.RequiredTicks = hours * PromiseBalance.TicksPerHour;
                candidate.Magnitude = hours > RequestRules.MinHours ? 1 : 0;
            }
            return candidate;
        }

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
