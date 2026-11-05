using System;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public static class TrustMood
    {
        [ThreadStatic] private static int neutralReadDepth;
        internal static bool Excluded => neutralReadDepth > 0;

        public static int Offset(int trust)
        {
            trust = PromiseBalance.ClampTrust(trust);
            int distance = trust - TrustRules.MoodNeutralTrust;
            int strength = Math.Min(TrustRules.MaxOngoingMood,
                (Math.Abs(distance) + TrustRules.MoodTrustBandSize - 1) / TrustRules.MoodTrustBandSize);
            return Math.Sign(distance) * strength;
        }

        public static int Stage(int trust)
        {
            int offset = Offset(trust);
            return offset == 0 ? -1 : offset < 0 ? offset + TrustRules.MaxOngoingMood
                : offset + TrustRules.MaxOngoingMood - 1;
        }

        public static void NotifyChanged(Pawn pawn) => pawn?.needs?.mood?.thoughts?.situational?.Notify_SituationalThoughtsDirty();

        public static float TargetWithoutTrust(Need_Mood reader)
        {
            // This reader has its own per-tick cache. Never disturb the live mood cache.
            neutralReadDepth++;
            try { return reader.CurInstantLevel; }
            finally { neutralReadDepth--; }
        }

        public static void Advance(ColonistPromises state, Need_Mood live, int now)
        {
            if (!PromiseConditions.Observable(state.Pawn) || now <= state.LastTrustNeutralMoodTick) return;
            if (!state.HasTrustNeutralMood)
            {
                state.TrustNeutralMoodLevel = live.CurLevel;
                state.HasTrustNeutralMood = true;
            }
            if (state.NeutralMood == null || state.NeutralMood.Source != live)
                state.NeutralMood = new TrustNeutralMood(state.Pawn, live);
            state.NeutralMood.CurLevel = state.TrustNeutralMoodLevel;
            state.NeutralMood.NeedInterval();
            state.TrustNeutralMoodLevel = state.NeutralMood.CurLevel;
            state.LastTrustNeutralMoodTick = now;
        }
    }

    public sealed class ThoughtWorker_LeadershipTrust : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            if (!PromiseConditions.Eligible(pawn)) return ThoughtState.Inactive;
            int stage = TrustMood.Stage(GameComponent_Promises.Current?.PeekState(pawn).Trust ?? PromiseBalance.InitialTrust);
            return stage < 0 ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(stage);
        }
    }

    public sealed class Thought_LeadershipTrust : Thought_Situational
    {
        public override float MoodOffset() => TrustMood.Excluded ? 0 : base.MoodOffset();
    }

    // Not installed as a pawn need. Only the real mood interval advances this saved counterfactual.
    public sealed class TrustNeutralMood : Need_Seeker
    {
        public readonly Need_Mood Source;
        private readonly Need_Mood targetReader;
        public TrustNeutralMood(Pawn pawn, Need_Mood source) : base(pawn)
        {
            Source = source;
            def = source.def;
            targetReader = new Need_Mood(pawn) { def = source.def, thoughts = source.thoughts };
        }
        public override float CurInstantLevel => TrustMood.TargetWithoutTrust(targetReader);
    }
}
