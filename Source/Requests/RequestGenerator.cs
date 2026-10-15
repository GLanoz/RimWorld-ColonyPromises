using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace ColonyPromises
{
    public sealed class RequestEvaluation
    {
        public RequestCandidate Candidate;
        public bool HasDeficit;
        public string Reason;
        public string Current;
        public List<HousingIssue> HousingIssues;
        public string DeficitSignature;
        public bool ObservationPaused;
    }

    public sealed class RequestCandidate
    {
        public PromiseRecord Terms;
        public string Signature;
        public string ReasonKey;
        public List<HousingWitness> HousingReasons;
        public ExpectationDef Expectation;
        public int Magnitude;
    }

    public static class RequestGenerator
    {
        public static RequestCandidate Candidate(Pawn pawn, PromiseKind kind) => Evaluate(pawn, kind).Candidate;

        public static RequestEvaluation Evaluate(Pawn pawn, PromiseKind kind, bool devBypassCooldown = false, string equipmentCause = null)
        {
            var result = new RequestEvaluation();
            result.Reason = PromiseConditions.EligibilityReason(pawn);
            if (result.Reason != null) return result;
            if (!pawn.Spawned || pawn.Suspended) result.Reason = "CP_DebugAbsent";
            else if (!pawn.Map.IsPlayerHome) result.Reason = "CP_DebugHome";
            else if (kind != PromiseKind.Leisure && (pawn.Downed || pawn.InMentalState || pawn.Drafted)) result.Reason = "CP_DebugBusy";
            if (result.Reason != null) return result;
            switch (kind)
            {
                case PromiseKind.Bedroom: result.Candidate = Bedroom(pawn, result, devBypassCooldown && Prefs.DevMode); break;
                case PromiseKind.Leisure: result.Candidate = LeisureNeeds.Evaluate(pawn, result); break;
                case PromiseKind.Equipment: result.Candidate = EquipmentNeeds.Evaluate(pawn, result,
                    devBypassCooldown && Prefs.DevMode ? equipmentCause : null); break;
                case PromiseKind.Substance: result.Candidate = SubstanceNeeds.Evaluate(pawn, result,
                    devBypassCooldown && Prefs.DevMode, devBypassCooldown && Prefs.DevMode ? equipmentCause : null); break;
            }
            if (kind == PromiseKind.Substance && pawn.jobs?.curDriver?.asleep == true)
            {
                result.Candidate = null;
                result.ObservationPaused = true;
                result.Reason = "CP_SubstanceAsleep";
            }
            if (kind == PromiseKind.Bedroom && result.HousingIssues == null)
            {
                result.HousingIssues = HousingRules.Evaluate(pawn);
                var partners = SharedBedroomRules.Partners(pawn);
                if (partners.Count == 1 && PromiseConditions.Eligible(partners[0])) result.HousingIssues.AddRange(HousingRules.Evaluate(partners[0]));
                result.Current = (result.Current == null ? "" : result.Current + "\n") + HousingRules.Describe(result.HousingIssues);
            }
            return result;
        }

        public static string Number(float value) => PromiseText.Number(value);

        private static RequestCandidate Bedroom(Pawn pawn, RequestEvaluation result, bool devBypassCooldown = false)
        {
            if (SharedBedroomRules.TryEvaluate(pawn, result, out var shared, devBypassCooldown)) return shared;
            return HousingRules.Candidate(pawn, null, result);
        }

        public static RequestCandidate ObservationCandidate(RequestEvaluation evaluation, PromiseKind kind) => evaluation.Candidate
            ?? (evaluation.DeficitSignature == null ? null : new RequestCandidate {
                Terms = new PromiseRecord { Kind = kind }, Signature = evaluation.DeficitSignature });

        public static DeficitObservation ProjectEvaluation(DeficitObservation saved, RequestEvaluation evaluation,
            PromiseKind kind, int now, bool severe)
        {
            var candidate = ObservationCandidate(evaluation, kind);
            var result = Project(saved, candidate, kind, now, severe);
            if ((kind != PromiseKind.Leisure && kind != PromiseKind.Substance) || candidate == null) return result;
            bool continuous = saved != null && saved.Signature == candidate.Signature && saved.LastTick >= 0
                && now >= saved.LastTick && now - saved.LastTick <= RequestRules.ObservationInterval;
            if (evaluation.ObservationPaused || saved?.ObservationPaused == true)
            {
                result.Ticks = continuous ? saved.Ticks : 0;
                result.SevereTicks = continuous ? saved.SevereTicks : 0;
                result.StartTick = continuous ? saved.StartTick : now;
            }
            result.ObservationPaused = evaluation.ObservationPaused;
            return result;
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

        public static void PrepareTerms(RequestCandidate candidate, DeficitObservation observation)
        {
            if (candidate.Terms.Kind != PromiseKind.Leisure) return;
            int hours = RequestRules.LeisureHours(observation.Ticks, observation.SevereTicks);
            candidate.Terms.RequiredTicks = hours * PromiseBalance.TicksPerHour;
            candidate.Magnitude = hours > RequestRules.MinHours ? 1 : 0;
        }

        public static RequestCandidate Observe(ColonistPromises state, PromiseKind kind, int now)
        {
            var evaluation = Evaluate(state.Pawn, kind);
            var candidate = evaluation.Candidate;
            if (kind == PromiseKind.Bedroom && evaluation.HousingIssues != null)
                foreach (var group in evaluation.HousingIssues.GroupBy(i => i.Pawn))
                {
                    var participant = group.Key == state.Pawn ? state : GameComponent_Promises.Current?.StateFor(group.Key);
                    if (participant != null) participant.HousingWitnesses = HousingRules.Witnesses(group);
                }
            if (kind == PromiseKind.Bedroom && evaluation.HousingIssues != null
                && !evaluation.HousingIssues.Any(i => i.Pawn == state.Pawn)) state.HousingWitnesses.Clear();
            if (candidate?.Terms.IsShared == true && candidate.Terms.ParticipantA != state.Pawn)
            {
                state.Deficits.RemoveAll(d => d.Kind == kind);
                return null;
            }
            var observation = ProjectEvaluation(state.Deficits.FirstOrDefault(d => d.Kind == kind), evaluation, kind, now,
                kind == PromiseKind.Leisure && state.Pawn.needs?.joy?.CurLevel < RequestRules.SevereJoy);
            state.Deficits.RemoveAll(d => d.Kind == kind);
            state.Deficits.Add(observation);
            if (candidate == null) return null;
            if (!RequestRules.Ready(observation.Ticks, state.Request != null || state.Active != null, now, state.CooldownUntilTick)) return null;
            PrepareTerms(candidate, observation);
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
            if (request.Terms.DevManual) return DevRequests.ValidTerms(pawn, request.Terms);
            var condition = PromiseConditions.Get(request.Terms.Kind);
            if (condition == null || !condition.ValidParameters(request.Terms)) return false;
            if (request.Terms.Kind == PromiseKind.Bedroom)
            {
                var terms = request.Terms;
                if (!terms.HousingBased) return false;
                var evaluation = new RequestEvaluation();
                var target = HousingRules.Candidate(terms.IsShared ? terms.ParticipantA : pawn,
                    terms.IsShared ? terms.ParticipantB : null, evaluation);
                return target != null && target.Terms.RoomStage == terms.RoomStage
                    && target.Terms.RequiresBarracks == terms.RequiresBarracks
                    && target.Terms.RequiresConstructedBed == terms.RequiresConstructedBed
                      && DlcCompatibility.SameBedroomRequirements(target.Terms, terms)
                    && HousingRules.OriginalCauseCurrent(request, evaluation.HousingIssues);
            }
            if (request.Terms.Kind == PromiseKind.Equipment) return EquipmentNeeds.CauseCurrent(pawn, request.Terms)
                && EquipmentNeeds.WeaponTargetCurrent(pawn, request.Terms)
                && EquipmentNeeds.UsableDefinition(pawn, request.Terms)
                  && DlcCompatibility.ApparelDefinition(pawn, request.Terms.EquipmentDef)
                  && (!request.Terms.EquipmentDef.IsApparel || !DlcCompatibility.AvoidHumanLeather(pawn) || request.Terms.AvoidHumanLeather)
                && EquipmentNeeds.CanObtain(pawn, request.Terms, out _);
            if (request.Terms.Kind == PromiseKind.Leisure) return LeisureNeeds.HasDeficit(pawn);
            if (request.Terms.Kind == PromiseKind.Substance) return SubstanceNeeds.Relevant(pawn, request.Terms);
            return false;
        }

        public static ColonistRequest Offer(RequestCandidate candidate, int now)
        {
            var terms = candidate.Terms;
            terms.CreatedTick = terms.LastCheckTick = terms.LastLeisureCreditTick = now;
            terms.DeadlineTick = now + RequestRules.ResponseDays * PromiseBalance.TicksPerDay;
            return new ColonistRequest
            {
                Terms = terms, ReasonKey = candidate.ReasonKey, Expectation = candidate.Expectation,
                HousingReasons = candidate.HousingReasons,
                Magnitude = candidate.Magnitude, OfferedTick = now, ExpiryTick = terms.DeadlineTick,
                DeadlineDays = terms.Kind == PromiseKind.Substance ? RequestRules.SubstanceDeadlineDays
                    : RequestRules.Deadline((int)terms.Kind, candidate.Magnitude),
                Announced = false, RecoveryEnabled = terms.Kind != PromiseKind.Substance
            };
        }

        public static void ResetObservations(ColonistPromises state, bool clearHousing = false)
        {
            state.Deficits.Clear();
            if (clearHousing) state.HousingWitnesses.Clear();
        }
    }
}
