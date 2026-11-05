using System;

namespace ColonyPromises
{
    public static class MoodTrust
    {
        public static int Direction(int trust, float? mood)
        {
            if (!mood.HasValue || float.IsNaN(mood.Value)) return 0;
            if (mood.Value >= TrustRules.GoodMoodThreshold && trust < TrustRules.PassiveRecoveryTarget) return 1;
            if (mood.Value <= TrustRules.BadMoodThreshold && trust > PromiseBalance.MinTrust) return -1;
            return 0;
        }

        // Only the periodic component calls this mutating method.
        public static int Observe(ColonistPromises state, float? mood, int now)
        {
            int direction = Direction(state.Trust, mood);
            long elapsed = (long)now - state.LastMoodTrustTick;
            bool changed = direction != state.MoodTrustDirection;
            bool gap = state.LastMoodTrustTick >= 0 && (elapsed < 0 || elapsed > TrustRules.MoodObservationInterval);
            if (direction == 0 || changed || gap) state.MoodTrustProgressTicks = 0;
            else if (state.LastMoodTrustTick >= 0)
                state.MoodTrustProgressTicks += (int)elapsed;
            state.MoodTrustDirection = direction;
            state.LastMoodTrustTick = now;
            if (direction == 0 || state.MoodTrustProgressTicks < TrustRules.MoodTrustPeriodTicks) return 0;

            int before = state.Trust;
            state.Trust = direction > 0
                ? Math.Min(TrustRules.PassiveRecoveryTarget, before + TrustRules.MoodTrustStep)
                : Math.Max(PromiseBalance.MinTrust, before - TrustRules.MoodTrustStep);
            state.MoodTrustProgressTicks -= TrustRules.MoodTrustPeriodTicks;
            if (Direction(state.Trust, mood) == 0)
            {
                state.MoodTrustDirection = 0;
                state.MoodTrustProgressTicks = 0;
            }
            return state.Trust - before;
        }
    }
}
