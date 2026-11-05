using System;

namespace ColonyPromises
{
    public static class MoodTrust
    {
        public static float? Average(ColonistPromises state) => state.MoodTrustProgressTicks > 0
            ? (float?)(state.MoodTrustWeightedSum / state.MoodTrustProgressTicks) : null;

        public static void RestartSampling(ColonistPromises state)
        {
            state.LastMoodTrustTick = -1;
            state.HasMoodTrustSample = false;
        }

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
            if (state.LastMoodTrustTick >= 0 && now <= state.LastMoodTrustTick) return 0;
            long elapsed = (long)now - state.LastMoodTrustTick;
            bool valid = mood.HasValue && !float.IsNaN(mood.Value) && !float.IsInfinity(mood.Value)
                && mood.Value >= 0 && mood.Value <= 1;
            int before = state.Trust;
            if (valid && state.HasMoodTrustSample && state.LastMoodTrustTick >= 0
                && elapsed <= TrustRules.MoodObservationInterval)
            {
                // The previous mood weights only a regularly observed interval with
                // valid endpoints. Never fill missing time with that last value.
                int remaining = (int)elapsed;
                while (remaining > 0)
                {
                    int part = Math.Min(remaining, TrustRules.MoodTrustPeriodTicks - state.MoodTrustProgressTicks);
                    state.MoodTrustWeightedSum += (double)state.LastMoodTrustValue * part;
                    state.MoodTrustProgressTicks += part;
                    remaining -= part;
                    if (state.MoodTrustProgressTicks != TrustRules.MoodTrustPeriodTicks) continue;
                    int direction = Direction(state.Trust, Average(state));
                    if (direction > 0)
                        state.Trust = Math.Min(TrustRules.PassiveRecoveryTarget, state.Trust + TrustRules.MoodTrustStep);
                    else if (direction < 0)
                        state.Trust = Math.Max(PromiseBalance.MinTrust, state.Trust - TrustRules.MoodTrustStep);
                    // Neutral and capped outcomes also consume the completed period.
                    state.MoodTrustWeightedSum = 0;
                    state.MoodTrustProgressTicks = 0;
                }
            }
            state.LastMoodTrustTick = now;
            state.HasMoodTrustSample = valid;
            if (valid) state.LastMoodTrustValue = mood.Value;
            return state.Trust - before;
        }
    }
}
