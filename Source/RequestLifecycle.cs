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
            state.Request = null;
            state.Active = r;
            RequestGenerator.ResetObservations(state);
        }

        public static PromiseRecord Close(ColonistPromises state, PromiseOutcome outcome, int now)
        {
            var request = state.Request;
            if (request == null) return null;
            state.Request = null;
            var r = request.Terms;
            r.CreatedTick = request.OfferedTick;
            r.ResolvedTick = now;
            r.Outcome = outcome;
            r.AppliedMood = 0;
            r.AppliedTrust = 0;
            state.CooldownUntilTick = now + RequestRules.UnacceptedCooldownDays * PromiseBalance.TicksPerDay;
            RequestGenerator.ResetObservations(state);
            return r;
        }
    }
}
