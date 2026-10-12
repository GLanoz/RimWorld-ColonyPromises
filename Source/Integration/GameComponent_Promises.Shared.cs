using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public sealed partial class GameComponent_Promises
    {
        private List<SharedBedroomCommitment> sharedBedrooms = new List<SharedBedroomCommitment>();
        private int nextSharedId = 1;

        public bool CreateShared(RequestCandidate candidate, int now)
        {
            var terms = candidate?.Terms;
            if (terms?.DevTest == true && !Prefs.DevMode) return false;
            if (terms?.IsShared != true || !PromiseConditions.Eligible(terms.ParticipantA) || !PromiseConditions.Eligible(terms.ParticipantB)
                || SharedBedroomRules.Validity(terms.ParticipantA, terms.ParticipantB) != SharedPairValidity.Valid)
                return false;
            var a = StateFor(terms.ParticipantA);
            var b = StateFor(terms.ParticipantB);
            if (terms.DevTest ? DevRequests.Occupied(a) || DevRequests.Occupied(b)
                : !SharedBedroomRules.SlotFree(a, now) || !SharedBedroomRules.SlotFree(b, now)) return false;
            while (sharedBedrooms.Any(s => s.Id == "shared-bedroom-" + nextSharedId)) nextSharedId++;
            var shared = new SharedBedroomCommitment { Id = "shared-bedroom-" + nextSharedId++,
                First = terms.ParticipantA, Second = terms.ParticipantB, Request = RequestGenerator.Offer(candidate, now) };
            terms.SharedId = shared.Id;
            if (terms.DevManual) shared.Request.RecoveryEnabled = false;
            sharedBedrooms.Add(shared);
            AttachShared(shared, a, b);
            RequestGenerator.ResetObservations(a);
            RequestGenerator.ResetObservations(b);
            nextCheck = System.Math.Min(nextCheck, now);
            if (RequestNotices.TakeAnnouncement(shared.Request)) RequestNotices.Send(a, false);
            return true;
        }

        private void AttachShared(SharedBedroomCommitment shared, ColonistPromises a, ColonistPromises b)
        {
            foreach (var state in new[] { a, b })
            {
                state.Shared = shared;
                state.SharedId = shared.Id;
                state.Request = shared.Request;
                state.Active = shared.Active;
                if (shared.Active != null) { active[state.Pawn] = state; open.Remove(state.Pawn); }
                else if (shared.Request != null) { open[state.Pawn] = state; active.Remove(state.Pawn); }
            }
        }

        private void RestoreSharedBedrooms()
        {
            foreach (var shared in sharedBedrooms.ToList())
            {
                if (shared.Resolved || shared.Terms == null) { sharedBedrooms.Remove(shared); continue; }
                if (shared.First == null || shared.Second == null)
                { ResolveShared(shared, PromiseOutcome.Closed, Find.TickManager.TicksGame); continue; }
                var a = StateFor(shared.First);
                var b = StateFor(shared.Second);
                // Never replace unrelated records, including malformed or conflicting save data.
                if (a.Active != null || a.Request != null || b.Active != null || b.Request != null
                    || (a.SharedId != null && a.SharedId != shared.Id) || (b.SharedId != null && b.SharedId != shared.Id))
                { ResolveShared(shared, PromiseOutcome.Closed, Find.TickManager.TicksGame); continue; }
                AttachShared(shared, a, b);
                RestoreHold(shared.First, shared.Terms);
            }
            foreach (var state in colonists.Where(s => s.Shared == null && s.SharedId != null)) state.SharedId = null;
        }

        private string SharedAcceptanceError(SharedBedroomCommitment shared, ColonistRequest request, int now)
        {
            if (shared.Resolved || shared.Request != request || request == null || now >= request.ExpiryTick)
                return "CP_RequestUnavailable".Translate();
            if (!PromiseConditions.Eligible(shared.First) || !PromiseConditions.Eligible(shared.Second)) return "CP_Ineligible".Translate();
            if (SharedBedroomRules.Validity(shared.First, shared.Second) != SharedPairValidity.Valid
                || !RequestMaintenance.Observable(shared.First) || !RequestMaintenance.Observable(shared.Second)
                || shared.First.Map != shared.Second.Map || !RequestGenerator.Relevant(shared.First, request))
                return "CP_RequestOutdated".Translate();
            foreach (var pawn in new[] { shared.First, shared.Second })
            {
                var state = PeekState(pawn);
                if (state.Shared != shared || state.Active != null || state.Request != request || !request.Terms.DevTest && now < state.CooldownUntilTick)
                    return "CP_PairSlotBlocked".Translate();
            }
            return null;
        }

        private void AcceptShared(SharedBedroomCommitment shared, int now)
        {
            var a = StateFor(shared.First);
            var b = StateFor(shared.Second);
            shared.FirstEffects = PromiseConsequences.Preview(a, shared.Request, now);
            shared.SecondEffects = PromiseConsequences.Preview(b, shared.Request, now);
            shared.FirstEffects.FrozenConsequences = shared.SecondEffects.FrozenConsequences = true;
            RequestLifecycle.Accept(a, shared.Request, now);
            shared.Active = a.Active;
            shared.Request = null;
            AttachShared(shared, a, b);
            RequestGenerator.ResetObservations(b, true);
            nextCheck = System.Math.Min(nextCheck, now);
        }

        private bool SharedRecoveryAvailable(SharedBedroomCommitment shared, int now) => shared.Request != null
            && new[] { shared.First, shared.Second }.Where(p => p != null)
                .Any(p => TrustRecovery.Available(PeekState(p), shared.Request, now));

        private void CheckShared(SharedBedroomCommitment shared, int now)
        {
            if (shared.Resolved || shared.Terms == null) return;
            var validity = SharedBedroomRules.Validity(shared.First, shared.Second);
            if (validity == SharedPairValidity.Invalid) { ResolveShared(shared, PromiseOutcome.Closed, now); return; }
            var terms = shared.Terms;
            if (shared.Active != null)
            {
                PromiseProgress.Observe(terms, PromiseConditions.Get(PromiseKind.Bedroom), shared.First, now);
                if (terms.RequiredTicks > 0 && terms.ProgressTicks >= terms.RequiredTicks) ResolveShared(shared, PromiseOutcome.Fulfilled, now);
                else if (now >= terms.DeadlineTick) ResolveShared(shared, PromiseOutcome.Broken, now);
                return;
            }
            bool observable = validity == SharedPairValidity.Valid && RequestMaintenance.Observable(shared.First)
                && RequestMaintenance.Observable(shared.Second) && shared.First.Map == shared.Second.Map;
            if (terms.DevManual)
            {
                if (now >= shared.Request.ExpiryTick) ResolveShared(shared, PromiseOutcome.Expired, now);
                else if (RequestNotices.TakeReminder(shared.Request, now, ColonyPromisesMod.Settings.RemindersEnabled))
                    RequestNotices.Send(StateFor(shared.First), true);
                return;
            }
            if (observable && !DlcCompatibility.PendingCompatible(shared.First, terms))
            { ResolveShared(shared, PromiseOutcome.Obsolete, now); return; }
            bool satisfied = observable && SharedBedroomRules.Observe(terms).Satisfied;
            if (!terms.HousingBased || observable && !satisfied && !RequestGenerator.Relevant(shared.First, shared.Request))
            { ResolveShared(shared, PromiseOutcome.Obsolete, now); return; }
            bool recovery = SharedRecoveryAvailable(shared, now);
            if (recovery)
            {
                if (observable) PromiseProgress.Observe(terms, PromiseConditions.Get(PromiseKind.Bedroom), shared.First, now);
                else { terms.ResetHold(); terms.LastCheckTick = now; }
            }
            if (satisfied && (!recovery || terms.RequiredTicks > 0 && terms.ProgressTicks >= terms.RequiredTicks))
            { ResolveShared(shared, PromiseOutcome.SatisfiedUnaccepted, now, recovery); return; }
            if (now >= shared.Request.ExpiryTick) { ResolveShared(shared, PromiseOutcome.Expired, now); return; }
            if (RequestNotices.TakeReminder(shared.Request, now, ColonyPromisesMod.Settings.RemindersEnabled))
                RequestNotices.Send(StateFor(shared.First), true);
        }

        private void ResolveShared(SharedBedroomCommitment shared, PromiseOutcome outcome, int now, bool recoverTrust = false)
        {
            if (!CompleteShared(shared, outcome, now, recoverTrust)) return;
            Messages.Message("CP_SharedResult".Translate(SharedPromiseText.Names(shared.First, shared.Second),
                ("CP_Outcome_" + outcome).Translate()), outcome == PromiseOutcome.Fulfilled ? MessageTypeDefOf.PositiveEvent
                : outcome == PromiseOutcome.Broken || outcome == PromiseOutcome.Withdrawn ? MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.NeutralEvent);
        }

        // Commit both personal results before sending the single shared notification.
        private bool CompleteShared(SharedBedroomCommitment shared, PromiseOutcome outcome, int now, bool recoverTrust)
        {
            if (shared.Resolved) return false;
            var terms = shared.Terms;
            if (terms == null) return false;
            var request = shared.Request;
            bool accepted = shared.Active != null;
            shared.Resolved = true;
            sharedBedrooms.Remove(shared);
            int[] recovery = { 0, 0 };
            var pawns = new[] { shared.First, shared.Second };
            var states = pawns.Select(p => p == null ? null : StateFor(p)).ToArray();
            // Recovery must inspect the still-open aliases before the shared slot is consumed.
            if (!accepted && recoverTrust && outcome == PromiseOutcome.SatisfiedUnaccepted)
                for (int i = 0; i < 2; i++) if (states[i]?.Shared == shared) recovery[i] = TrustRecovery.Apply(states[i], request, now);
            for (int i = 0; i < 2; i++)
            {
                var state = states[i];
                if (state == null) continue;
                bool attached = state.Shared == shared;
                if (attached)
                {
                    state.Shared = null; state.SharedId = null; state.Active = null; state.Request = null;
                    active.Remove(state.Pawn); open.Remove(state.Pawn);
                }
                var personal = (accepted ? (i == 0 ? shared.FirstEffects : shared.SecondEffects) : terms)?.CopyForPreview() ?? terms.CopyForPreview();
                personal.HistoryPartnerLabel = pawns[1 - i]?.LabelShort ?? "CP_MissingColonist".Translate().ToString();
                personal.ProgressTicks = terms.ProgressTicks;
                personal.CreatedTick = terms.CreatedTick;
                personal.DeadlineTick = terms.DeadlineTick;
                personal.StableThing = null; personal.ObservedRoom = null;
                personal.Outcome = outcome;
                personal.RecordResolution(now);
                if (accepted)
                {
                    state.RecentSuccesses.RemoveAll(s => now - s.Tick > PromiseBalance.RepeatWindowDays * PromiseBalance.TicksPerDay);
                    PromiseConsequences.Apply(state, personal, outcome, now);
                }
                else if (outcome == PromiseOutcome.Declined && attached) PromiseConsequences.ApplyDecline(state, personal);
                else personal.RecordConsequences(0, recovery[i]);
                if (attached)
                {
                    state.CooldownUntilTick = now + (accepted && outcome != PromiseOutcome.Closed
                        ? personal.CooldownDays : RequestRules.UnacceptedCooldownDays) * PromiseBalance.TicksPerDay;
                    RequestGenerator.ResetObservations(state, true);
                }
                AddHistory(state, personal);
            }
            shared.Active = null; shared.Request = null;
            return true;
        }
    }
}
