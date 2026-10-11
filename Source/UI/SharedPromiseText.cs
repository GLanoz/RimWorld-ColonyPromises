using Verse;

namespace ColonyPromises
{
    public static class SharedPromiseText
    {
        public static string Names(Pawn a, Pawn b) => "CP_PairNames".Translate(
            a?.LabelShort ?? "CP_MissingColonist".Translate().ToString(),
            b?.LabelShort ?? "CP_MissingColonist".Translate().ToString());

        public static string Consequences(GameComponent_Promises component, ColonistPromises state,
            PromiseRecord terms, ColonistRequest request, int now)
        {
            if (state.Shared == null) return PromiseText.Consequences(terms);
            var shared = state.Shared;
            var a = request != null ? PromiseConsequences.Preview(component.PeekState(shared.First), request, now) : shared.FirstEffects;
            var b = request != null ? PromiseConsequences.Preview(component.PeekState(shared.Second), request, now) : shared.SecondEffects;
            return "CP_PersonalEffects".Translate(shared.First.LabelShort, PromiseText.Consequences(a)).ToString()
                + "\n\n" + "CP_PersonalEffects".Translate(shared.Second.LabelShort, PromiseText.Consequences(b)).ToString();
        }

        public static string WithdrawConfirmation(ColonistPromises state, PromiseRecord record)
        {
            if (state.Shared == null) return "CP_WithdrawConfirm".Translate(
                record.FrozenConsequences ? record.WithdrawnMood : PromiseBalance.WithdrawnMood,
                record.FrozenConsequences ? record.WithdrawnDays : PromiseBalance.WithdrawnMemoryDays,
                record.FrozenConsequences ? record.WithdrawnTrust : PromiseBalance.WithdrawnTrust,
                record.FrozenConsequences ? record.CooldownDays : PromiseBalance.CooldownDays);
            var shared = state.Shared;
            return "CP_SharedWithdrawConfirm".Translate(Names(shared.First, shared.Second),
                EffectLine(shared.First, shared.FirstEffects), EffectLine(shared.Second, shared.SecondEffects));
        }

        private static string EffectLine(Pawn pawn, PromiseRecord r) => "CP_PersonalWithdrawal".Translate(
            pawn.LabelShort, PromiseText.Signed(r.WithdrawnMood), r.WithdrawnDays,
            PromiseText.Signed(r.WithdrawnTrust), r.CooldownDays);
    }
}
