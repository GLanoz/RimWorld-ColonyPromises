using System;
using System.Linq;
using Verse;

namespace ColonyPromises
{
    public static class RequestDiagnostics
    {
        public static string Describe(ColonistPromises state, PromiseKind kind, int now)
        {
            var evaluation = RequestGenerator.Evaluate(state.Pawn, kind);
            var observationState = evaluation.Candidate?.Terms.IsShared == true
                ? GameComponent_Promises.Current?.PeekState(evaluation.Candidate.Terms.ParticipantA) ?? state : state;
            var saved = observationState.Deficits.FirstOrDefault(d => d.Kind == kind);
            var observation = RequestGenerator.ProjectEvaluation(saved, evaluation, kind, now,
                kind == PromiseKind.Leisure && state.Pawn?.needs?.joy?.CurLevel < RequestRules.SevereJoy);
            bool blocked = state.Request != null || state.Active != null || now < state.CooldownUntilTick
                || !PromiseConditions.Eligible(state.Pawn);
            int ticks = blocked ? 0 : observation.Ticks;
            string text = "CP_DebugType".Translate(("CP_Type_" + kind).Translate(),
                (evaluation.HasDeficit ? "CP_DebugYes" : "CP_DebugNo").Translate(),
                RequestGenerator.Number(ticks / (float)PromiseBalance.TicksPerDay),
                RequestGenerator.Number(RequestRules.PersistentTicks / (float)PromiseBalance.TicksPerDay)).ToString();
            if (saved != null) text += " " + "CP_DebugStored".Translate(RequestGenerator.Number(saved.Ticks / (float)PromiseBalance.TicksPerDay));
            if (!string.IsNullOrEmpty(evaluation.Current)) text += "\n" + evaluation.Current;
            text += "\n" + "CP_DebugTarget".Translate((evaluation.Candidate == null ? "CP_DebugNo" : "CP_DebugYes").Translate());
            if (evaluation.Candidate == null) return text + "\n" + (evaluation.Reason ?? "CP_DebugNoGear").Translate();
            var terms = evaluation.Candidate.Terms;
            RequestGenerator.PrepareTerms(evaluation.Candidate, observation);
            return text + "\n" + PromiseConditions.Get(kind).Describe(terms);
        }
    }
}
