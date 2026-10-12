using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public sealed partial class GameComponent_Promises
    {
        private void ObserveRequests(int now)
        {
            foreach (var pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Where(PromiseConditions.Eligible))
                StateFor(pawn);
            var ready = new List<KeyValuePair<ColonistPromises, RequestCandidate>>();
            lastObservation = now;
            foreach (var state in colonists.ToList()) ObserveRequest(state, now, ready);
            OfferReady(now, ready);
        }

        public void CheckRequestsNow(Pawn pawn)
        {
            if (!Prefs.DevMode || pawn == null) return;
            int now = Find.TickManager.TicksGame;
            var ready = new List<KeyValuePair<ColonistPromises, RequestCandidate>>();
            var selected = StateFor(pawn);
            ObserveRequest(selected, now, ready);
            var partners = SharedBedroomRules.Partners(pawn);
            if (partners.Count == 1 && PromiseConditions.Eligible(partners[0])
                && partners[0].thingIDNumber < pawn.thingIDNumber)
            {
                var owner = StateFor(partners[0]);
                if (SharedBedroomRules.SlotFree(owner, now))
                {
                    var candidate = RequestGenerator.Observe(owner, PromiseKind.Bedroom, now);
                    if (candidate?.Terms.IsShared == true) ready.Add(new KeyValuePair<ColonistPromises, RequestCandidate>(owner, candidate));
                }
            }
            OfferReady(now, ready);
        }

        private void ObserveRequest(ColonistPromises state, int now, List<KeyValuePair<ColonistPromises, RequestCandidate>> ready)
        {
            state.LastGenerationCheck = now;
            if (state.Shared != null) { CheckShared(state.Shared, now); return; }
            if (state.Pawn == null || state.Pawn.Dead || state.Pawn.Destroyed || state.Pawn.Faction != Faction.OfPlayer)
            {
                if (state.Request != null) CloseRequest(state, PromiseOutcome.Closed, now);
                RequestGenerator.ResetObservations(state, true);
                return;
            }
            if (state.Request != null)
            {
                CheckOpenRequest(state, now);
                return;
            }
            if (state.Active != null || now < state.CooldownUntilTick || !PromiseConditions.Eligible(state.Pawn))
            {
                RequestGenerator.ResetObservations(state, true);
                return;
            }
            foreach (PromiseKind kind in Enum.GetValues(typeof(PromiseKind)))
            {
                var candidate = RequestGenerator.Observe(state, kind, now);
                if (candidate != null) ready.Add(new KeyValuePair<ColonistPromises, RequestCandidate>(state, candidate));
            }
        }

        private void OfferReady(int now, List<KeyValuePair<ColonistPromises, RequestCandidate>> ready)
        {
            if (now < nextOffer || ready.Count == 0) return;
            // Preserve age priority after the accumulation cap; pawn and kind break true ties.
            var chosen = RequestGenerator.SelectReady(ready);
            if (chosen.Value.Terms.IsShared)
            {
                if (!CreateShared(chosen.Value, now)) return;
                nextOffer = now + RequestRules.OfferSpacing;
                return;
            }
            chosen.Key.Request = RequestGenerator.Offer(chosen.Value, now);
            RequestGenerator.ResetObservations(chosen.Key);
            nextOffer = now + RequestRules.OfferSpacing;
            open[chosen.Key.Pawn] = chosen.Key;
            nextCheck = Math.Min(nextCheck, now);
            if (RequestNotices.TakeAnnouncement(chosen.Key.Request)) RequestNotices.Send(chosen.Key, false);
        }

        private void CheckOpenRequest(ColonistPromises state, int now)
        {
            var request = state.Request;
            if (request?.Terms == null) return;
            var pawn = state.Pawn;
            if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Faction != Faction.OfPlayer)
            {
                CloseRequest(state, PromiseOutcome.Closed, now);
                return;
            }
            bool observable = RequestMaintenance.Observable(pawn);
            if (request.Terms.DevManual)
            {
                if (now >= request.ExpiryTick) CloseRequest(state, PromiseOutcome.Expired, now);
                else if (RequestNotices.TakeReminder(request, now, ColonyPromisesMod.Settings.RemindersEnabled)) RequestNotices.Send(state, true);
                return;
            }
            if (observable && !DlcCompatibility.PendingCompatible(pawn, request.Terms))
            { CloseRequest(state, PromiseOutcome.Obsolete, now); return; }
            bool satisfied = observable && RequestMaintenance.Satisfied(pawn, request);
            bool recovery = TrustRecovery.Available(state, request, now);
            var terms = request.Terms;
            if (terms.Kind == PromiseKind.Equipment && !terms.NeedBasedEquipment)
            { CloseRequest(state, PromiseOutcome.Obsolete, now); return; }
            if (terms.Kind == PromiseKind.Bedroom && (!terms.HousingBased || observable && !satisfied && !RequestGenerator.Relevant(pawn, request)))
            { CloseRequest(state, PromiseOutcome.Obsolete, now); return; }
            var condition = PromiseConditions.Get(terms.Kind);
            if (recovery && condition?.UsesContinuousHold == true)
            {
                if (observable) PromiseProgress.Observe(terms, condition, pawn, now);
                else { terms.ResetHold(); terms.LastCheckTick = now; }
            }
            if (satisfied && recovery)
            {
                if (terms.RequiredTicks > 0 && terms.ProgressTicks >= terms.RequiredTicks)
                {
                    CloseRequest(state, PromiseOutcome.SatisfiedUnaccepted, now, true);
                    return;
                }
                // Keep a satisfied request open only while proving its recovery requirement.
            }
            else if (observable && !RequestGenerator.Relevant(pawn, request))
            {
                CloseRequest(state, satisfied ? PromiseOutcome.SatisfiedUnaccepted : PromiseOutcome.Obsolete, now);
                return;
            }
            if (now >= request.ExpiryTick) { CloseRequest(state, PromiseOutcome.Expired, now); return; }
            if (RequestNotices.TakeReminder(request, now, ColonyPromisesMod.Settings.RemindersEnabled))
                RequestNotices.Send(state, true);
        }

    }
}
