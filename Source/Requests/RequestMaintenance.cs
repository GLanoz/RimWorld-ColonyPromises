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
            if (state.Request.Terms.DevManual) return true;
            if (state.Request.Terms.Kind == PromiseKind.Equipment && !state.Request.Terms.NeedBasedEquipment) return false;
            if (state.Request.Terms.Kind == PromiseKind.Bedroom && !state.Request.Terms.HousingBased) return false;
            if (state.Shared != null)
            {
                var shared = state.Shared;
                var validity = SharedBedroomRules.Validity(shared.First, shared.Second);
                if (validity == SharedPairValidity.Invalid) return false;
                if (validity == SharedPairValidity.Unknown || !Observable(shared.First) || !Observable(shared.Second)
                    || shared.First.Map != shared.Second.Map) return true;
                if (!DlcCompatibility.PendingCompatible(shared.First, shared.Terms)) return false;
                if (!Satisfied(state.Pawn, state.Request)) return RequestGenerator.Relevant(shared.First, state.Request);
                var component = GameComponent_Promises.Current;
                return component != null ? TrustRecovery.Available(component.PeekState(shared.First), shared.Request, now)
                    || TrustRecovery.Available(component.PeekState(shared.Second), shared.Request, now)
                    : TrustRecovery.Available(state, shared.Request, now);
            }
            if (!Observable(state.Pawn)) return true; // Absence is not proof of obsolescence.
            if (!DlcCompatibility.PendingCompatible(state.Pawn, state.Request.Terms)) return false;
            if (Satisfied(state.Pawn, state.Request)) return TrustRecovery.Available(state, state.Request, now);
            return RequestGenerator.Relevant(state.Pawn, state.Request);
        }
    }
}
