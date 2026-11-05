using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ColonyPromises;
using RimWorld;
using RimWorld.Planet;
using Verse;

public static class FeatureChecks
{
    sealed class ManagedLog : UnityEngine.ILogHandler
    {
        public void LogFormat(UnityEngine.LogType type, UnityEngine.Object context, string format, params object[] args)
        { Console.Error.WriteLine(string.Format(format, args)); throw new Exception(string.Format(format, args)); }
        public void LogException(Exception exception, UnityEngine.Object context) { throw exception; }
    }
    static int checks;
    static readonly BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    static ColonistRequest Request(PromiseKind kind = PromiseKind.Bedroom) => RequestGenerator.Offer(new RequestCandidate {
        Terms = new PromiseRecord { Kind = kind, RequiredTicks = 60000, HousingBased = true }, Magnitude = 0 }, 100);
    static void Save(IExposable record, string path)
    {
        try { Scribe.saver.InitSaving(path, "save"); record.ExposeData(); Scribe.saver.FinalizeSaving(); }
        finally { Scribe.saver.ForceStop(); }
    }
    static void Load(IExposable record, string path)
    {
        try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = record; record.ExposeData(); }
        finally { Scribe.loader.ForceStop(); }
    }
    public static int Run(string root, string path, bool includeGameDefs = true)
    {
        UnityEngine.Debug.unityLogger.logHandler = new ManagedLog();
        // The standalone host has no LoadedModManager. Supply only this fixture's saved types.
        typeof(GenTypes).GetField("allTypesCached", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null,
            new List<Type> { typeof(SuccessStamp), typeof(PromiseRecord), typeof(ColonistRequest), typeof(ColonistPromises), typeof(BedroomRequirement) });
        typeof(BackCompatibility).GetField("conversionChain", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null,
            new List<BackCompatibilityConverter>());
        var typeCache = (System.Collections.IDictionary)typeof(GenTypes).GetField("typeCache", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var keyType = typeof(GenTypes).GetNestedType("TypeCacheKey", BindingFlags.NonPublic);
        foreach (var type in new[] { typeof(SuccessStamp), typeof(PromiseRecord), typeof(ColonistRequest) })
            typeCache[Activator.CreateInstance(keyType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { type.FullName, null }, null)] = type;
        checks += MoodTrustChecks.Run(path);
        Check(TrustRules.MoodFactor(0) == 0.25f && TrustRules.MoodFactor(40) == 1 && TrustRules.MoodFactor(100) == 1,
            "Low trust has bounded endpoints");
        float previous = 0;
        for (int trust = 0; trust <= 100; trust++)
        {
            float factor = TrustRules.MoodFactor(trust);
            Check(factor >= previous && factor <= 1, "Mood scaling is smooth and monotonic");
            previous = factor;
        }
        Check(TrustRules.MoodFactor(40) - TrustRules.MoodFactor(39) < 0.02f, "No reward cliff at the threshold");
        var state = new ColonistPromises { Trust = 0 };
        var request = Request(PromiseKind.Leisure);
        state.Request = request;
        var preview = PromiseConsequences.Preview(state, request, 100);
        Check(preview.SuccessMood == 0.75f && preview.SuccessTrust == 3, "Zero trust reduces mood without suppressing trust recovery");
        for (int i = 0; i < 5; i++) state.RecentSuccesses.Add(new SuccessStamp { Kind = PromiseKind.Leisure, Tick = 100 });
        preview = PromiseConsequences.Preview(state, request, 100);
        Check(preview.SuccessMood == 0.19f && preview.SuccessTrust == 1, "Repeated zero-trust success remains positive and restores trust");
        RequestLifecycle.Accept(state, request, 100);
        Check(state.Active.SuccessMood == 0.19f && state.Active.FrozenConsequences && state.Request == null,
            "New acceptance freezes low-trust effects");
        state.Trust = 60;
        PromiseConsequences.Snapshot(state, request, 1000);
        Check(state.Active.SuccessMood == 0.19f && state.Active.SuccessTrust == 1, "Later trust changes cannot rewrite an accepted snapshot");
        var frozen = Request();
        frozen.Terms.FrozenConsequences = true;
        frozen.Terms.SuccessMood = 7;
        frozen.Terms.SuccessTrust = 6;
        PromiseConsequences.Snapshot(new ColonistPromises { Trust = 0 }, frozen, 100);
        Check(frozen.Terms.SuccessMood == 7 && frozen.Terms.SuccessTrust == 6, "Existing accepted rewards are retained");
        var zero = new ColonistPromises { Trust = 0 };
        zero.Request = Request();
        RequestLifecycle.Accept(zero, zero.Request, 100);
        var result = zero.Active;
        PromiseConsequences.Apply(zero, result, PromiseOutcome.Fulfilled, 1000);
        Check(zero.Trust == 5 && result.AppliedMood == 1.25f && result.AppliedTrust == 5, "A fulfilled promise escapes zero trust");

        request = Request();
        Check(RequestNotices.TakeAnnouncement(request) && !RequestNotices.TakeAnnouncement(request), "One initial announcement");
        Check(!RequestNotices.TakeReminder(request, request.ExpiryTick - 30001, true), "No early reminder");
        Check(!RequestNotices.TakeReminder(request, request.ExpiryTick - 30000, false), "Reminder setting respected");
        Check(RequestNotices.TakeReminder(request, request.ExpiryTick - 30000, true)
            && !RequestNotices.TakeReminder(request, request.ExpiryTick - 1, true), "One reminder at the half-day boundary");
        var expired = Request();
        Check(!RequestNotices.TakeReminder(expired, expired.ExpiryTick, true), "No expiry-time reminder");
        request.Terms = null; // Only scalar request flags are round-tripped here.
        Save(request, path);
        var loadedRequest = new ColonistRequest();
        Load(loadedRequest, path);
        Check(loadedRequest.Announced && loadedRequest.ReminderSent && loadedRequest.RecoveryEnabled, "Notification and recovery flags persist");
        Check(!RequestNotices.TakeAnnouncement(loadedRequest) && !RequestNotices.TakeReminder(loadedRequest, 100000, true),
            "Reload cannot duplicate notifications");
        File.WriteAllText(path, "<save><expiryTick>120100</expiryTick></save>");
        var old = new ColonistRequest();
        Load(old, path);
        Check(old.Announced && !old.RecoveryEnabled && !old.ReminderSent, "Old requests do not gain retroactive announcements or trust recovery");
        var settings = new ColonyPromisesSettings { RemindersEnabled = false };
        Save(settings, path);
        var restoredSettings = new ColonyPromisesSettings();
        Load(restoredSettings, path);
        Check(!restoredSettings.RemindersEnabled, "Reminder preference persists");

        var recovery = new ColonistPromises { Trust = 0 };
        recovery.Request = Request();
        Check(TrustRecovery.Available(recovery, recovery.Request, 100), "Recovery is available at zero trust");
        recovery.Request.Terms.ProgressTicks = 59999;
        recovery.Request.Terms.PreviouslySatisfied = true;
        Check(TrustRecovery.Apply(recovery, recovery.Request, 60100) == 0, "A short hold cannot restore trust");
        recovery.Request.Terms.ProgressTicks = 60000;
        var recovered = RequestLifecycle.Close(recovery, PromiseOutcome.SatisfiedUnaccepted, 60100, true);
        Check(recovery.Trust == 2 && recovered.AppliedTrust == 2 && recovered.AppliedMood == 0,
            "Unaccepted fulfillment restores exactly two trust and no mood");
        Check(RequestLifecycle.Close(recovery, PromiseOutcome.SatisfiedUnaccepted, 60100, true) == null && recovery.Trust == 2,
            "Recovery closes once");
        Check(recovery.CooldownUntilTick == 240100 && recovery.NextUnpromisedRecoveryTick == 660100,
            "Request and recovery cooldowns are distinct");
        Check(recovery.RecentSuccesses.Count == 0 && recovery.RecentRecoveries.Count == 1,
            "Recovery cannot erase or replace accepted-promise repeat history");
        recovery.Request = Request();
        Check(!TrustRecovery.Available(recovery, recovery.Request, 60100), "New request cannot bypass recovery cooldown");
        recovery.Request.OfferedTick = 700000; recovery.Request.ExpiryTick = 820000;
        Check(!TrustRecovery.Available(recovery, recovery.Request, 700000), "Same-kind repeat ledger blocks recovery after the shorter cooldown");
        recovery.Request = Request(PromiseKind.Equipment);
        recovery.Request.OfferedTick = 700000; recovery.Request.ExpiryTick = 820000;
        Check(TrustRecovery.Available(recovery, recovery.Request, 700000), "Another direction can recover after the global cooldown");
        var refusal = new ColonistPromises { Trust = 0, Request = Request() };
        var declined = RequestLifecycle.Close(refusal, PromiseOutcome.Declined, 1000, true);
        Check(refusal.Trust == 0 && declined.AppliedTrust == 0 && declined.AppliedMood == -1, "Decline mood applies at zero trust without recovery or negative trust");
        refusal.Request = Request();
        Check(RequestLifecycle.Close(refusal, PromiseOutcome.Expired, 120100, true).AppliedTrust == 0, "Expiry grants no recovery");
        recovery.Request = null;
        Save(recovery, path);
        var loadedState = new ColonistPromises();
        Load(loadedState, path);
        Check(loadedState.NextUnpromisedRecoveryTick == 660100 && loadedState.RecentRecoveries.Count == 1,
            "Recovery cooldown and ledger survive Scribe");
        var accepted = new ColonistPromises { Trust = 0, Request = Request() };
        accepted.Request.Terms.ProgressTicks = 50000;
        RequestLifecycle.Accept(accepted, accepted.Request, 1000);
        Check(accepted.Active.ProgressTicks == 0 && accepted.Active.CreatedTick == 1000,
            "Unaccepted progress cannot carry into an accepted promise");

        var oldGame = Current.Game;
        var oldLanguage = LanguageDatabase.activeLanguage;
        var oldDefault = LanguageDatabase.defaultLanguage;
        try {
            var game = Empty<Game>();
            game.questManager = new QuestManager();
            game.tickManager = Empty<TickManager>();
            typeof(TickManager).GetField("ticksGameInt", fields).SetValue(game.tickManager, 1000);
            Current.Game = game;
            var component = new GameComponent_Promises(game);
            var pawn = Empty<Pawn>(); pawn.thingIDNumber = 42;
            pawn.def = Empty<ThingDef>(); pawn.def.defName = "FixtureHuman";
            pawn.def.race = new RaceProperties { intelligence = Intelligence.Humanlike,
                lifeStageAges = new List<LifeStageAge>() };
            pawn.health = Empty<Pawn_HealthTracker>();
            typeof(Pawn_HealthTracker).GetField("healthState", fields).SetValue(pawn.health, PawnHealthState.Mobile);
            pawn.ageTracker = Empty<Pawn_AgeTracker>();
            typeof(Pawn_AgeTracker).GetField("pawn", fields).SetValue(pawn.ageTracker, pawn);
            typeof(Pawn_AgeTracker).GetField("ageBiologicalTicksInt", fields).SetValue(pawn.ageTracker, 72000000L);
            pawn.needs = Empty<Pawn_NeedsTracker>(); pawn.needs.mood = Empty<Need_Mood>();
            typeof(Thing).GetField("mapIndexOrState", fields).SetValue(pawn, (sbyte)-1);
            var faction = Empty<Faction>(); faction.def = Empty<FactionDef>(); faction.def.isPlayer = true;
            typeof(Thing).GetField("factionInt", fields).SetValue(pawn, faction);
            var world = Empty<World>(); world.factionManager = Empty<FactionManager>();
            typeof(FactionManager).GetField("ofPlayer", fields).SetValue(world.factionManager, faction);
            typeof(Game).GetField("worldInt", fields).SetValue(game, world);
            world.worldPawns = new WorldPawns();
            typeof(Game).GetField("maps", fields).SetValue(game, new List<Map> { Empty<Map>() });
            var moodComponent = new GameComponent_Promises(game);
            var moodState = moodComponent.StateFor(pawn);
            moodState.Trust = 47;
            typeof(GameComponent_Promises).GetField("nextObservation", fields).SetValue(moodComponent, int.MaxValue);
            typeof(GameComponent_Promises).GetField("nextCheck", fields).SetValue(moodComponent, int.MaxValue);
            typeof(Thing).GetField("mapIndexOrState", fields).SetValue(pawn, (sbyte)0);
            checks += TrustMoodChecks.Run(root, path, pawn);
            pawn.needs.mood.CurLevel = 0.7f;
            int moodPeriodEnd = 1000 + TrustRules.MoodTrustPeriodTicks;
            for (int now = 1000; now <= moodPeriodEnd; now += TrustRules.MoodObservationInterval)
            {
                typeof(TickManager).GetField("ticksGameInt", fields).SetValue(game.tickManager, now);
                moodComponent.GameComponentTick();
            }
            Check(moodComponent.PeekState(pawn).Trust == 48 && moodState.LastMoodTrustTick == moodPeriodEnd,
                "Real component schedule reads actual mood and restores trust after a verified period: trust=" + moodState.Trust
                + "; mood=" + pawn.needs.mood.CurLevel + "; eligible=" + PromiseConditions.EligibilityReason(pawn)
                + "; spawned=" + pawn.Spawned + "; suspended=" + pawn.Suspended + "; progress=" + moodState.MoodTrustProgressTicks);
            moodComponent.GameComponentTick();
            Check(moodState.Trust == 48 && moodState.MoodTrustProgressTicks == 0,
                "Same-tick component calls cannot duplicate passive trust");
            typeof(Thing).GetField("mapIndexOrState", fields).SetValue(pawn, (sbyte)-1);
            for (int now = moodPeriodEnd + TrustRules.MoodObservationInterval;
                now <= moodPeriodEnd + TrustRules.MoodTrustPeriodTicks; now += TrustRules.MoodObservationInterval)
            {
                typeof(TickManager).GetField("ticksGameInt", fields).SetValue(game.tickManager, now);
                moodComponent.GameComponentTick();
            }
            Check(moodState.Trust == 48 && moodState.MoodTrustProgressTicks == 0 && !moodState.HasMoodTrustSample,
                "Component never credits an absent colonist's available mood value");
            moodState.MoodTrustProgressTicks = 5000;
            moodState.MoodTrustWeightedSum = (double)0.7f * 5000;
            moodState.HasMoodTrustSample = true;
            moodComponent.InvalidatePawn(pawn);
            Check(!moodState.HasMoodTrustSample && moodState.LastMoodTrustTick == -1
                && moodState.MoodTrustProgressTicks == 5000 && moodState.MoodTrustWeightedSum == (double)0.7f * 5000,
                "Departure between scheduled checks pauses sampling while preserving the collected average");
            typeof(TickManager).GetField("ticksGameInt", fields).SetValue(game.tickManager, 1000);
            // The 1.6 standalone host cannot initialize native Unity notification types.
            // The complete notification path is exercised against 1.7; both versions
            // retain the separate consequence, shared completion and Scribe fixtures.
            if (typeof(Pawn).Assembly.GetName().Version.Minor == 7)
            {
                var previousPositiveEvent = MessageTypeDefOf.PositiveEvent;
                try
                {
                    MessageTypeDefOf.PositiveEvent = new MessageTypeDef { defName = "FixturePositiveEvent" };
                    LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, "English");
                    pawn.Name = new NameSingle("RecoveryFixture");
                    foreach (int startingTrust in new[] { 0, 47, 99 })
                    {
                        var completionComponent = new GameComponent_Promises(game);
                        var completionState = completionComponent.StateFor(pawn);
                        completionState.Trust = startingTrust;
                        completionState.MoodTrustProgressTicks = 30000;
                        completionState.MoodTrustWeightedSum = (double)0.7f * 30000;
                        completionState.HasMoodTrustSample = true;
                        completionState.LastMoodTrustTick = 900;
                        completionComponent.FinalizeInit();
                        Check(completionState.LastMoodTrustTick == -1 && !completionState.HasMoodTrustSample
                            && completionState.MoodTrustProgressTicks == 30000
                            && completionState.MoodTrustWeightedSum == (double)0.7f * 30000,
                            "Component loading restarts mood sampling without erasing verified time");
                        completionState.Request = Request(PromiseKind.Leisure);
                        RequestLifecycle.Accept(completionState, completionState.Request, 1000);
                        var completionRecord = completionState.Active;
                        completionRecord.ProgressTicks = completionRecord.RequiredTicks;
                        int reward = completionRecord.SuccessTrust;
                        var activeIndex = (Dictionary<Pawn, ColonistPromises>)typeof(GameComponent_Promises)
                            .GetField("active", fields).GetValue(completionComponent);
                        activeIndex[pawn] = completionState;
                        var checkPromise = typeof(GameComponent_Promises).GetMethod("Check", fields);
                        checkPromise.Invoke(completionComponent, new object[] { completionState, 1250 });
                        Check(completionState.Active == null && !activeIndex.ContainsKey(pawn)
                            && completionState.History.Count == 1 && completionState.History[0].Outcome == PromiseOutcome.Fulfilled,
                            "GameComponent commits verified fulfillment and releases the active slot");
                        Check(completionComponent.PeekState(pawn).Trust == startingTrust + reward
                            && completionState.History[0].AppliedTrust == reward && reward > 0,
                            "GameComponent fulfillment updates the same trust state read by the UI");
                        checkPromise.Invoke(completionComponent, new object[] { completionState, 1500 });
                        Check(completionState.Trust == startingTrust + reward && completionState.History.Count == 1,
                            "Repeated GameComponent check cannot duplicate fulfillment or trust");
                    }
                }
                finally { MessageTypeDefOf.PositiveEvent = previousPositiveEvent; }
            }
            var open = (Dictionary<Pawn, ColonistPromises>)typeof(GameComponent_Promises).GetField("open", fields).GetValue(component);
            var thing = Empty<Thing>(); thing.thingIDNumber = 88;
            var tracked = new ColonistPromises { Pawn = pawn, Request = Request() };
            tracked.Request.Terms.StableThing = thing; tracked.Request.Terms.ProgressTicks = 50000;
            open[pawn] = tracked;
            Check(component.OpenRequests(1000).Count == 1, "Counter/list include a live unanswered request during map absence: "
                + PromiseConditions.EligibilityReason(pawn) + "; destroyed=" + pawn.Destroyed + "; dead=" + pawn.Dead);
            Check(component.OpenRequests(tracked.Request.ExpiryTick).Count == 0, "Counter/list exclude expiry without waiting for a scan");
            tracked.Trust = 0;
            Check(component.OpenRequests(1000).Count == 1, "Zero trust does not hide requests");
            typeof(Pawn_HealthTracker).GetField("healthState", fields).SetValue(pawn.health, PawnHealthState.Dead);
            Check(component.OpenRequests(1000).Count == 0, "Death immediately removes the live count/list entry");
            typeof(Pawn_HealthTracker).GetField("healthState", fields).SetValue(pawn.health, PawnHealthState.Mobile);
            typeof(Thing).GetField("factionInt", fields).SetValue(pawn, null);
            Check(component.OpenRequests(1000).Count == 0, "Faction departure immediately removes the live count/list entry");
            typeof(Thing).GetField("factionInt", fields).SetValue(pawn, faction);
            foreach (var outcome in new[] { PromiseOutcome.Declined, PromiseOutcome.Expired, PromiseOutcome.Obsolete,
                PromiseOutcome.SatisfiedUnaccepted, PromiseOutcome.Closed })
            {
                tracked.Request = Request();
                RequestLifecycle.Close(tracked, outcome, 1000);
                Check(component.OpenRequests(1000).Count == 0, "Counter/list exclude " + outcome);
            }
            tracked.Request = Request();
            RequestLifecycle.Accept(tracked, tracked.Request, 1000);
            Check(component.OpenRequests(1000).Count == 0, "Counter/list exclude accepted promises");
            tracked.Active = null;
            tracked.Request = Request();
            tracked.Request.Terms.StableThing = thing; tracked.Request.Terms.ProgressTicks = 50000;
            var otherPawn = Empty<Pawn>(); otherPawn.thingIDNumber = 43; otherPawn.def = pawn.def;
            var leisure = new ColonistPromises { Pawn = otherPawn, Request = Request(PromiseKind.Leisure) };
            leisure.Request.Terms.ProgressTicks = 5500;
            open[otherPawn] = leisure;
            component.InvalidateThing(null);
            Check(tracked.Request.Terms.ProgressTicks == 50000, "Null invalidation cannot reset an unanswered hold");
            component.InvalidateThing(thing);
            Check(tracked.Request.Terms.ProgressTicks == 0, "Real item invalidation resets unanswered hold");
            Check(leisure.Request.Terms.ProgressTicks == 5500, "Unanswered recreation survives another pawn's item invalidation");
            tracked.Request.Terms.ProgressTicks = 50000;
            var room = Empty<Room>(); tracked.Request.Terms.ObservedRoom = room;
            component.RoomChanged(room, true);
            Check(tracked.Request.Terms.ProgressTicks == 0, "Room-shape invalidation resets unanswered hold");
            tracked.Request.Terms.ProgressTicks = 50000;
            component.InvalidatePawn(pawn);
            Check(tracked.Request.Terms.ProgressTicks == 0, "Pawn departure resets unanswered continuous hold");
            foreach (string folder in new[] { "English", "Ukrainian" })
            {
                LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, folder);
                string history = PromiseHistory.Format(recovered, 60100);
                Check(history.Contains("+2") && !history.Contains("CP_") && !history.Contains("{"), "History shows actual recovery in " + folder);
                Check("CP_TrustImpact".Translate("40", "25").ToString().Contains("25"), "Trust explanation renders in " + folder);
                string moodTrustRules = PromiseText.MoodTrustRules();
                Check(moodTrustRules.Contains("65") && moodTrustRules.Contains("35") && moodTrustRules.Contains("50")
                    && moodTrustRules.Contains(PromiseText.Number(2.5f)) && !moodTrustRules.Contains("{") && !moodTrustRules.Contains("CP_"),
                    "Passive mood-trust rules use actual balance values in " + folder);
                var diagnosticState = new ColonistPromises { Trust = 49, MoodTrustProgressTicks = 42 * PromiseBalance.TicksPerHour,
                    MoodTrustWeightedSum = (double)0.7f * 42 * PromiseBalance.TicksPerHour };
                double diagnosticSum = diagnosticState.MoodTrustWeightedSum;
                string moodDiagnostic = RequestDiagnostics.MoodTrustSummary(diagnosticState);
                Check(moodDiagnostic.Contains("42/60") && moodDiagnostic.Contains("70%") && moodDiagnostic.Contains("+1")
                    && !moodDiagnostic.Contains("{") && !moodDiagnostic.Contains("CP_"),
                    "Development diagnostics show actual average, progress and expected result in " + folder);
                Check(diagnosticState.MoodTrustWeightedSum == diagnosticSum && diagnosticState.LastMoodTrustTick == -1,
                    "Viewing average diagnostics cannot mutate saved data in " + folder);
                diagnosticState.Trust = 50;
                Check(RequestDiagnostics.MoodTrustSummary(diagnosticState).EndsWith("0."),
                    "Development projection respects the recovery ceiling in " + folder);
                Check("CP_UnpromisedRecovery".Translate("2", "proof", "0").ToString().Contains("+2"), "Recovery preview renders in " + folder);
                Check("CP_RequestLetterText".Translate("Pawn", "conditions", "0.5").ToString().Contains("0.5"),
                    "Letter contents render with plain numeric placeholders in " + folder);
            }
        } finally { Current.Game = oldGame; LanguageDatabase.activeLanguage = oldLanguage; LanguageDatabase.defaultLanguage = oldDefault; }
        return checks + AdaptiveButtonChecks.Run() + ButtonIconChecks.Run(root) + CardChecks.Run(root) + UIChecks.Run(root) + SharedBedroomChecks.Run(root, path) + DevRequestChecks.Run(root, path)
            + (includeGameDefs ? NeedsChecks.Run(root, path) + DlcChecks.Run(root, path) + SubstanceChecks.Run(root, path) + ExpansionChecks.Run(root, path) : 0);
    }
}
