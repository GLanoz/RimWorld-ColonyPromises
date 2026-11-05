using System;
using System.IO;
using ColonyPromises;
using Verse;

public static class MoodTrustChecks
{
    static int checks;
    static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    static int ObserveFor(ColonistPromises state, float? mood, int start, int duration)
    {
        int delta = MoodTrust.Observe(state, mood, start);
        for (int now = start + TrustRules.MoodObservationInterval; now <= start + duration; now += TrustRules.MoodObservationInterval)
            delta += MoodTrust.Observe(state, mood, now);
        return delta;
    }
    static ColonistPromises RoundTrip(ColonistPromises state, string path)
    {
        try
        {
            Scribe.saver.InitSaving(path, "save"); state.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded = new ColonistPromises();
            Scribe.loader.InitLoading(path); Scribe.loader.curParent = loaded; loaded.ExposeData();
            return loaded;
        }
        finally { Scribe.saver.ForceStop(); Scribe.loader.ForceStop(); }
    }
    public static int Run(string path)
    {
        checks = 0;
        int period = TrustRules.MoodTrustPeriodTicks, interval = TrustRules.MoodObservationInterval;
        var state = new ColonistPromises { Trust = 47 };
        Check(ObserveFor(state, 1f, 0, PromiseBalance.TicksPerDay) == 0 && state.Trust == 47,
            "One good day is insufficient for the new 2.5-day period");
        state = new ColonistPromises { Trust = 47 };
        Check(ObserveFor(state, 0.65f, 0, period - interval) == 0 && state.Trust == 47,
            "Good mood cannot restore trust before a verified period");
        Check(MoodTrust.Observe(state, 0.65f, period) == 1 && state.Trust == 48,
            "Exactly 65 percent mood restores one trust per 2.5-day period");
        Check(ObserveFor(state, 1f, period, 4 * period) == 2 && state.Trust == 50,
            "Passive recovery stops at 50 rather than exceeding it");
        state = new ColonistPromises { Trust = 80 };
        Check(ObserveFor(state, 1f, 0, period) == 0 && state.Trust == 80,
            "Good mood does not reduce trust earned above 50");
        Check(ObserveFor(state, 0.35f, period, period) == -1 && state.Trust == 79,
            "Exactly 35 percent mood costs one trust per 2.5-day period even above 50");
        state = new ColonistPromises { Trust = 1 };
        Check(ObserveFor(state, 0f, 0, 3 * period) == -1 && state.Trust == 0,
            "Poor mood stops at zero");
        Check(ObserveFor(state, 1f, 3 * period, period) == 1 && state.Trust == 1,
            "Zero trust can recover through good mood without a promise");
        foreach (float? mood in new float?[] { 0.36f, 0.64f, null, float.NaN })
        {
            state = new ColonistPromises { Trust = 30 };
            ObserveFor(state, 1f, 0, period / 2);
            Check(MoodTrust.Observe(state, mood, period / 2 + interval) == 0 && state.MoodTrustProgressTicks == 0,
                "Neutral or unavailable mood resets the continuous count");
            Check(ObserveFor(state, 1f, period, period - interval) == 0 && state.Trust == 30,
                "A new good mood streak cannot reuse old time");
        }
        state = new ColonistPromises { Trust = 30 };
        ObserveFor(state, 0f, 0, period / 2);
        Check(ObserveFor(state, 1f, period / 2, period - interval) == 0 && state.Trust == 30,
            "A short poor mood streak cannot count toward good mood recovery");
        int ticks = state.MoodTrustProgressTicks;
        int last = state.LastMoodTrustTick;
        Check(MoodTrust.Observe(state, 1f, last) == 0 && state.MoodTrustProgressTicks == ticks,
            "Repeated same-tick observation adds nothing");
        MoodTrust.Observe(state, 1f, last + interval + 1);
        Check(state.MoodTrustProgressTicks == 0 && state.Trust == 30, "Unobserved time resets the streak without credit");
        ObserveFor(state, 1f, 3 * period, period / 2);
        MoodTrust.Observe(state, 1f, 1);
        Check(state.MoodTrustProgressTicks == 0, "A reversed clock cannot add mood time");

        state = new ColonistPromises { Trust = 30 };
        ObserveFor(state, 1f, 0, period / 2);
        var loaded = RoundTrip(state, path);
        Check(loaded.Trust == 30 && loaded.MoodTrustProgressTicks == period / 2
            && loaded.MoodTrustDirection == 1 && loaded.LastMoodTrustTick == period / 2,
            "Mood observation fields and trust survive real Scribe field serialization");
        Check(MoodTrust.Observe(loaded, 1f, period / 2) == 0 && loaded.MoodTrustProgressTicks == period / 2,
            "Loading at the same tick adds no progress");
        loaded.LastMoodTrustTick = -1; // GameComponent.FinalizeInit restarts sampling after loading.
        Check(MoodTrust.Observe(loaded, 1f, period / 2) == 0 && loaded.MoodTrustProgressTicks == period / 2,
            "A fresh sampling clock retains verified progress but credits no loading time");
        Check(ObserveFor(loaded, 1f, period / 2, period / 2) == 1 && loaded.Trust == 31,
            "A saved partial period completes only after the remaining verified time");
        File.WriteAllText(path, "<save><trust>27</trust></save>");
        try
        {
            loaded = new ColonistPromises(); Scribe.loader.InitLoading(path); Scribe.loader.curParent = loaded; loaded.ExposeData();
            Check(loaded.Trust == 27 && loaded.LastMoodTrustTick == -1 && loaded.MoodTrustProgressTicks == 0
                && loaded.MoodTrustDirection == 0, "Old saves retain trust without invented mood history");
        }
        finally { Scribe.loader.ForceStop(); }

        state = new ColonistPromises { Trust = 30 };
        state.Request = RequestGenerator.Offer(new RequestCandidate {
            Terms = new PromiseRecord { Kind = PromiseKind.Leisure, RequiredTicks = 10000 }, Magnitude = 0 }, 0);
        RequestLifecycle.Accept(state, state.Request, 0);
        int reward = state.Active.SuccessTrust;
        float moodReward = state.Active.SuccessMood;
        ObserveFor(state, 1f, 0, period);
        Check(state.Active.SuccessTrust == reward && state.Active.SuccessMood == moodReward,
            "Passive changes cannot rewrite accepted promise rewards");
        PromiseConsequences.Apply(state, state.Active, PromiseOutcome.Fulfilled, period);
        Check(state.Trust == 31 + reward && state.Active.AppliedTrust == reward,
            "Promise rewards remain separate from passive recovery");
        state = new ColonistPromises { Trust = 30 };
        int direction = MoodTrust.Direction(state.Trust, 1f);
        Check(direction == 1 && state.MoodTrustProgressTicks == 0 && state.LastMoodTrustTick == -1,
            "Read-only mood evaluation does not enroll time or modify state");
        return checks;
    }
}
