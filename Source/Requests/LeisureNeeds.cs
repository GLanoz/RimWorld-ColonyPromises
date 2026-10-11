using Verse;

namespace ColonyPromises
{
    public static class LeisureNeeds
    {
        public static bool HasDeficit(Pawn pawn) => pawn.needs?.joy != null && pawn.needs.joy.CurLevel < RequestRules.LowJoy;
        public static bool CanObserve(bool onHomeMap, bool suspended, bool downed, bool asleep, bool busy) =>
            onHomeMap && !suspended && !downed && !asleep && !busy;

        public static RequestCandidate Evaluate(Pawn pawn, RequestEvaluation result)
        {
            var joy = pawn.needs?.joy;
            if (joy == null) { result.Reason = "CP_DebugNoJoy"; return null; }
            result.Current = "CP_DebugJoy".Translate(RequestGenerator.Number(joy.CurLevel * 100),
                RequestGenerator.Number(RequestRules.LowJoy * 100)).ToString()
                + "\n" + "CP_JoyEffect".Translate(PromiseText.Signed(EquipmentNeeds.NegativeMood(pawn, "NeedJoy")));
            result.HasDeficit = HasDeficit(pawn);
            if (!result.HasDeficit) { result.Reason = "CP_DebugSatisfied"; return null; }
            if (!CanObserve(pawn.Spawned && pawn.Map.IsPlayerHome, pawn.Suspended, pawn.Downed,
                pawn.jobs?.curDriver?.asleep == true, pawn.Drafted || pawn.InMentalState))
            { result.Reason = "CP_NeedSleeping"; result.ObservationPaused = true; result.DeficitSignature = "joy-need-v2"; return null; }
            return new RequestCandidate { Terms = new PromiseRecord { Kind = PromiseKind.Leisure },
                Signature = "joy-need-v2", ReasonKey = "CP_ReasonLeisure" };
        }
    }
}
