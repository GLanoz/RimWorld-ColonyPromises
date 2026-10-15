namespace ColonyPromises
{
    public static class RequestLifecycle
    {
        public static void Accept(ColonistPromises state, ColonistRequest request, int now)
        {
            var r = request.Terms;
            PromiseConsequences.Snapshot(state, request, now);
            r.FrozenConsequences = true;
            r.CreatedTick = now;
            r.DeadlineTick = now + request.DeadlineDays * PromiseBalance.TicksPerDay;
            r.LastCheckTick = r.LastLeisureCreditTick = now;
            r.ResetHold();
            r.SubstanceConsumedTick = -1;
            state.Request = null;
            state.Active = r;
            RequestGenerator.ResetObservations(state, true);
        }

        public static PromiseRecord Close(ColonistPromises state, PromiseOutcome outcome, int now, bool recoverTrust = false)
        {
            var request = state.Request;
            if (request == null) return null;
            int recovery = recoverTrust && outcome == PromiseOutcome.SatisfiedUnaccepted
                ? TrustRecovery.Apply(state, request, now) : 0;
            state.Request = null;
            var r = request.Terms;
            r.CreatedTick = request.OfferedTick;
            r.RecordResolution(now);
            r.Outcome = outcome;
            r.RecordConsequences(0, recovery);
            SubstanceNeeds.StartCooldown(state, r, now);
            if (outcome == PromiseOutcome.Declined) PromiseConsequences.ApplyDecline(state, r);
            r.StableThing = null;
            r.ObservedRoom = null;
            state.CooldownUntilTick = now + RequestRules.UnacceptedCooldownDays * PromiseBalance.TicksPerDay;
            RequestGenerator.ResetObservations(state, true);
            return r;
        }
    }
}
