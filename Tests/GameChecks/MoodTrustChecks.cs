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
    static bool Near(double a, double b) => Math.Abs(a - b) < 0.000001;
    static int ObserveFor(ColonistPromises state, float mood, int start, int duration, float? nextMood = null)
    {
        int delta = MoodTrust.Observe(state, mood, start);
        int end = start + duration;
        for (int now = start; now < end;)
        {
            now = Math.Min(end, now + TrustRules.MoodObservationInterval);
            delta += MoodTrust.Observe(state, now == end ? nextMood ?? mood : mood, now);
        }
        return delta;
    }
    static ColonistPromises Load(string path)
    {
        try
        {
            var loaded = new ColonistPromises();
            Scribe.loader.InitLoading(path); Scribe.loader.curParent = loaded; loaded.ExposeData();
            return loaded;
        }
        finally { Scribe.loader.ForceStop(); }
    }
    static ColonistPromises RoundTrip(ColonistPromises state, string path)
    {
        try { Scribe.saver.InitSaving(path, "save"); state.ExposeData(); Scribe.saver.FinalizeSaving(); }
        finally { Scribe.saver.ForceStop(); }
        return Load(path);
    }
    public static int Run(string path)
    {
        checks = 0;
        int period = TrustRules.MoodTrustPeriodTicks, interval = TrustRules.MoodObservationInterval;
        foreach (float mood in new[] { 0.65f, 0.35f, 0.5f })
        {
            var state = new ColonistPromises { Trust = 30 };
            Check(ObserveFor(state, mood, 0, period - interval) == 0 && state.Trust == 30,
                "A partial window never changes trust");
            int expected = mood == 0.65f ? 1 : mood == 0.35f ? -1 : 0;
            Check(MoodTrust.Observe(state, mood, period) == expected && state.Trust == 30 + expected,
                "Exact average thresholds and neutral average determine the result");
            Check(state.MoodTrustProgressTicks == 0 && state.MoodTrustWeightedSum == 0,
                "Every outcome consumes the entire non-overlapping period");
            Check(MoodTrust.Observe(state, mood, period) == 0 && state.Trust == 30 + expected,
                "Duplicate boundary event cannot apply a result twice");
            Check(ObserveFor(state, mood, period, period) == expected && state.Trust == 30 + 2 * expected,
                "The next period starts independently after any outcome");
        }
        foreach (int trust in new[] { 0, 49, 50, 80 })
        {
            var good = new ColonistPromises { Trust = trust };
            var poor = new ColonistPromises { Trust = trust };
            int gain = trust < 50 ? 1 : 0, loss = trust > 0 ? -1 : 0;
            Check(ObserveFor(good, 1f, 0, period) == gain && good.Trust == trust + gain,
                "Recovery respects zero, 49, 50 and trust above 50");
            Check(ObserveFor(poor, 0f, 0, period) == loss && poor.Trust == trust + loss,
                "Poor average can reduce high trust but never goes below zero");
            Check(good.MoodTrustProgressTicks == 0 && poor.MoodTrustProgressTicks == 0,
                "Capped results still consume their observation period");
        }
        var mixed = new ColonistPromises { Trust = 30 };
        ObserveFor(mixed, 0.8f, 0, period / 2, 0.5f);
        Check(ObserveFor(mixed, 0.5f, period / 2, period / 2) == 1 && mixed.Trust == 31,
            "Mixed good and neutral intervals averaging 65 percent grant recovery");
        mixed = new ColonistPromises { Trust = 30 };
        ObserveFor(mixed, 0.2f, 0, period / 2, 0.5f);
        Check(ObserveFor(mixed, 0.5f, period / 2, period / 2) == -1 && mixed.Trust == 29,
            "Mixed poor and neutral intervals averaging 35 percent cost trust");
        mixed = new ColonistPromises { Trust = 30 };
        ObserveFor(mixed, 1f, 0, period / 3, 0.5f);
        ObserveFor(mixed, 0.5f, period / 3, period / 3, 0f);
        Check(ObserveFor(mixed, 0f, 2 * period / 3, period / 3) == 0 && mixed.Trust == 30
            && mixed.MoodTrustProgressTicks == 0, "Good, neutral and poor mood all contribute without resetting the average");

        var varying = new ColonistPromises { Trust = 30 };
        MoodTrust.Observe(varying, 0.9f, 0);
        MoodTrust.Observe(varying, 0.3f, 1000);
        MoodTrust.Observe(varying, 0.6f, 3000);
        Check(varying.MoodTrustProgressTicks == 3000 && Near(MoodTrust.Average(varying).Value, 0.5),
            "The average weights intervals by duration rather than sample count");
        double sum = varying.MoodTrustWeightedSum;
        MoodTrust.Observe(varying, 1f, 3000);
        MoodTrust.Observe(varying, 1f, 2999);
        Check(varying.MoodTrustProgressTicks == 3000 && varying.MoodTrustWeightedSum == sum,
            "Duplicate and reversed timestamps never credit an interval twice");
        MoodTrust.Observe(varying, 1f, 3000 + interval + 1);
        Check(varying.MoodTrustProgressTicks == 3000 && varying.MoodTrustWeightedSum == sum,
            "A missed check preserves existing data without filling the gap");
        MoodTrust.Observe(varying, null, 10000);
        MoodTrust.Observe(varying, null, 100000);
        MoodTrust.Observe(varying, 0.7f, 200000);
        Check(varying.MoodTrustProgressTicks == 3000 && varying.MoodTrustWeightedSum == sum,
            "Absence and the first return observation never fill unverified time");
        MoodTrust.Observe(varying, 0.7f, 200000 + interval);
        Check(varying.MoodTrustProgressTicks == 3000 + interval, "Observation resumes after a pause without discarding prior data");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -0.1f, 1.1f })
        {
            int duration = varying.MoodTrustProgressTicks;
            MoodTrust.Observe(varying, invalid, varying.LastMoodTrustTick + interval);
            Check(varying.MoodTrustProgressTicks == duration && !varying.HasMoodTrustSample,
                "Unusable mood values pause sampling without contaminating the average");
        }
        var boundary = new ColonistPromises { Trust = 30, MoodTrustProgressTicks = period - 1000,
            MoodTrustWeightedSum = (double)0.7f * (period - 1000), LastMoodTrustTick = 0,
            LastMoodTrustValue = 0.7f, HasMoodTrustSample = true };
        Check(MoodTrust.Observe(boundary, 0.2f, interval) == 1 && boundary.Trust == 31
            && boundary.MoodTrustProgressTicks == interval - 1000
            && Near(boundary.MoodTrustWeightedSum, (double)0.7f * (interval - 1000)),
            "An interval crossing 60 hours is split exactly between the old and new periods");
        Check(MoodTrust.Observe(boundary, 0.2f, interval) == 0 && boundary.Trust == 31,
            "The split interval is never replayed");
        var loadedBoundary = RoundTrip(boundary, path);
        MoodTrust.RestartSampling(loadedBoundary);
        Check(MoodTrust.Observe(loadedBoundary, 0.7f, 10000) == 0 && loadedBoundary.Trust == 31
            && loadedBoundary.MoodTrustProgressTicks == interval - 1000,
            "Loading after a boundary cannot replay the already applied trust adjustment");
        Check(ObserveFor(loadedBoundary, 0.7f, 10000, period - loadedBoundary.MoodTrustProgressTicks) == 1
            && loadedBoundary.Trust == 32 && loadedBoundary.MoodTrustProgressTicks == 0,
            "Saved carry-over belongs only to the next independent window");
        var saved = new ColonistPromises { Trust = 30 };
        ObserveFor(saved, 0.7f, 0, 42 * PromiseBalance.TicksPerHour);
        var loaded = RoundTrip(saved, path);
        Check(loaded.MoodTrustAverageVersion == 1 && loaded.MoodTrustProgressTicks == saved.MoodTrustProgressTicks
            && loaded.MoodTrustWeightedSum == saved.MoodTrustWeightedSum && loaded.LastMoodTrustTick == saved.LastMoodTrustTick
            && loaded.HasMoodTrustSample && loaded.LastMoodTrustValue == saved.LastMoodTrustValue,
            "Scribe preserves the sum, duration, sample validity and timestamps");
        MoodTrust.Observe(loaded, 0.7f, loaded.LastMoodTrustTick);
        Check(loaded.MoodTrustProgressTicks == saved.MoodTrustProgressTicks && loaded.Trust == 30,
            "Loading at the saved tick adds no progress");
        MoodTrust.RestartSampling(loaded);
        Check(ObserveFor(loaded, 0.7f, 500000, period - saved.MoodTrustProgressTicks) == 1 && loaded.Trust == 31,
            "Restarting sampling preserves 42 verified hours and needs only 18 more after loading");
        foreach (string oldXml in new[] {
            "<save><trust>27</trust></save>",
            "<save><trust>27</trust><moodTrustProgressTicks>100000</moodTrustProgressTicks><moodTrustDirection>1</moodTrustDirection><lastMoodTrustTick>123000</lastMoodTrustTick></save>" })
        {
            File.WriteAllText(path, oldXml); loaded = Load(path);
            Check(loaded.Trust == 27 && loaded.MoodTrustProgressTicks == 0 && loaded.MoodTrustWeightedSum == 0
                && loaded.LastMoodTrustTick == -1 && !loaded.HasMoodTrustSample && loaded.MoodTrustAverageVersion == 1,
                "Old saves and streaks start an empty average without changing trust");
            loaded = RoundTrip(loaded, path);
            Check(loaded.Trust == 27 && loaded.MoodTrustProgressTicks == 0, "Migration remains stable on the next save");
        }
        var accepted = new ColonistPromises { Trust = 30 };
        accepted.Request = RequestGenerator.Offer(new RequestCandidate {
            Terms = new PromiseRecord { Kind = PromiseKind.Leisure, RequiredTicks = 10000 }, Magnitude = 0 }, 0);
        RequestLifecycle.Accept(accepted, accepted.Request, 0);
        int reward = accepted.Active.SuccessTrust;
        float moodReward = accepted.Active.SuccessMood;
        ObserveFor(accepted, 1f, 0, period);
        Check(accepted.Active.SuccessTrust == reward && accepted.Active.SuccessMood == moodReward,
            "Average-based changes never rewrite accepted rewards");
        PromiseConsequences.Apply(accepted, accepted.Active, PromiseOutcome.Fulfilled, period);
        Check(accepted.Trust == 31 + reward && accepted.Active.AppliedTrust == reward, "Promise rewards remain separate");
        sum = varying.MoodTrustWeightedSum;
        int ticks = varying.MoodTrustProgressTicks;
        MoodTrust.Direction(varying.Trust, MoodTrust.Average(varying));
        Check(sum == varying.MoodTrustWeightedSum && ticks == varying.MoodTrustProgressTicks,
            "Reading the average and projected outcome cannot mutate the observation");
        return checks;
    }
}
