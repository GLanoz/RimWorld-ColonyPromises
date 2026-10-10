using Verse;

namespace ColonyPromises
{
    public static class RequestMaintenance
    {
        public static bool IsOpen(ColonistPromises state, int now) =>
            state?.Request?.Terms != null && state.Active == null && state.Pawn != null
            && !state.Pawn.Dead && !state.Pawn.Destroyed && state.Pawn.Faction == RimWorld.Faction.OfPlayer
            && PromiseConditions.Eligible(state.Pawn) && now < state.Request.ExpiryTick;

        public static bool Observable(Pawn pawn) => PromiseConditions.Observable(pawn)
            && pawn.Map.IsPlayerHome && !pawn.Downed && !pawn.Drafted && !pawn.InMentalState;

        public static bool Satisfied(Pawn pawn, ColonistRequest request) => request.Terms.Kind == PromiseKind.Leisure
            ? pawn.needs?.joy != null && pawn.needs.joy.CurLevel >= RequestRules.LowJoy
            : PromiseConditions.Get(request.Terms.Kind)?.Observe(pawn, request.Terms).Satisfied == true;

        public static bool Visible(ColonistPromises state, int now)
        {
            if (!IsOpen(state, now)) return false;
            if (!Observable(state.Pawn)) return true; // Absence is not proof of obsolescence.
            if (Satisfied(state.Pawn, state.Request)) return TrustRecovery.Available(state, state.Request, now);
            return RequestGenerator.Relevant(state.Pawn, state.Request);
        }
    }
}
