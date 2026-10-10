using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ColonyPromises;
using Verse;

public static class GenerationChecks
{
    static int checks;
    static readonly BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static void Check(bool value, string name)
    {
        checks++;
        if (!value) throw new Exception(name);
    }
    static Pawn Pawn(int id)
    {
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.thingIDNumber = id;
        pawn.def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        pawn.def.defName = "GenerationCheckPawn";
        return pawn;
    }
    static KeyValuePair<ColonistPromises, RequestCandidate> Entry(int id, int start, int ticks = 240000)
    {
        var state = new ColonistPromises { Pawn = Pawn(id) };
        state.Deficits.Add(new DeficitObservation { Kind = PromiseKind.Leisure, Signature = "joy",
            Ticks = ticks, LastTick = 500000, StartTick = start });
        return new KeyValuePair<ColonistPromises, RequestCandidate>(state,
            new RequestCandidate { Terms = new PromiseRecord { Kind = PromiseKind.Leisure }, Signature = "joy" });
    }
    public static int Run(string root, string path)
    {
        var candidate = new RequestCandidate { Terms = new PromiseRecord { Kind = PromiseKind.Leisure }, Signature = "joy" };
        var previewRequest = RequestGenerator.Offer(candidate, 100);
        var previewState = new ColonistPromises { Request = previewRequest };
        var preview = PromiseConsequences.Preview(previewState, previewRequest, 100);
        Check(preview.SuccessMood == 3 && previewRequest.Terms.SuccessMood == 0
            && !previewRequest.Terms.FrozenConsequences && previewState.Request == previewRequest,
            "Viewing a request's consequence preview does not change saved terms");
        var saved = RequestGenerator.Project(null, candidate, PromiseKind.Leisure, 100, true);
        Check(saved.Ticks == 0 && saved.StartTick == 100, "A new deficit credits no earlier time");
        for (int i = 1; i <= 48; i++)
        {
            int previous = saved.Ticks;
            var next = RequestGenerator.Project(saved, candidate, PromiseKind.Leisure, 100 + i * 2500, true);
            Check(saved.Ticks == previous, "Projection never mutates its input");
            saved = next;
        }
        Check(saved.Ticks == 120000 && saved.SevereTicks == 120000, "Two days of hourly samples reach persistence");
        Check(RequestRules.Ready(saved.Ticks, false, 120100, 0), "Persistence enables ordinary readiness");
        Check(!RequestRules.Ready(saved.Ticks, true, 120100, 0), "Occupied slots still block");
        Check(!RequestRules.Ready(saved.Ticks, false, 120100, 120101), "Cooldown still blocks");
        var duplicate = RequestGenerator.Project(saved, candidate, PromiseKind.Leisure, saved.LastTick, true);
        Check(duplicate.Ticks == saved.Ticks && duplicate.SevereTicks == saved.SevereTicks, "Same-tick checks add no time");
        var gap = RequestGenerator.Project(saved, candidate, PromiseKind.Leisure, saved.LastTick + 2501, true);
        Check(gap.Ticks == 0 && gap.SevereTicks == 0 && gap.StartTick == saved.LastTick + 2501, "Unobserved gaps reset age and progress");
        var changed = RequestGenerator.Project(saved, new RequestCandidate { Signature = "other" }, PromiseKind.Leisure, saved.LastTick + 2500, false);
        Check(changed.Ticks == 0 && changed.StartTick == changed.LastTick, "Changed requirements restart");
        var satisfied = RequestGenerator.Project(saved, null, PromiseKind.Leisure, saved.LastTick + 2500, false);
        Check(satisfied.Ticks == 0 && satisfied.StartTick == -1 && satisfied.Signature == null, "Satisfied conditions clear observations");
        var capped = new DeficitObservation { Signature = "joy", Ticks = 240000, LastTick = 500000, StartTick = 100 };
        var afterCap = RequestGenerator.Project(capped, candidate, PromiseKind.Leisure, 502500, false);
        Check(afterCap.Ticks == 240000 && afterCap.StartTick == 100, "Age survives the four-day progress cap");
        var newerLowId = Entry(1, 200000);
        var olderHighId = Entry(99, 100);
        Check(new[] { newerLowId, olderHighId }.OrderByDescending(x => x.Key.Deficits[0].Ticks)
            .ThenBy(x => x.Key.Pawn.thingIDNumber).First().Key == newerLowId.Key,
            "Reproduce the old capped queue's pawn-ID bias");
        Check(RequestGenerator.SelectReady(new[] { newerLowId, olderHighId }).Key == olderHighId.Key,
            "Regression: the older capped deficit wins over a smaller pawn ID");
        var trueTie = Entry(2, 100);
        Check(RequestGenerator.SelectReady(new[] { olderHighId, trueTie }).Key == trueTie.Key, "Pawn ID breaks genuine age ties");
        Check(RequestGenerator.SelectReady(new[] { Entry(1, 0, 120000), Entry(2, 100, 180000) }).Key.Pawn.thingIDNumber == 2,
            "Existing uncapped duration priority is retained");
        var legacy = new DeficitObservation { Signature = "joy", Ticks = 100000, LastTick = 200000 };
        var migrated = RequestGenerator.Project(legacy, candidate, PromiseKind.Leisure, 202500, false);
        Check(migrated.StartTick == 100000 && migrated.Ticks == 102500, "Old observations preserve progress and infer a conservative age");

        try {
            Scribe.saver.InitSaving(path, "save");
            afterCap.ExposeData();
            Scribe.saver.FinalizeSaving();
            var loaded = new DeficitObservation();
            Scribe.loader.InitLoading(path);
            Scribe.loader.curParent = loaded;
            loaded.ExposeData();
            Check(loaded.StartTick == 100 && loaded.Ticks == 240000 && loaded.LastTick == 502500, "Observation age round-trips through actual Scribe");
        } finally { Scribe.saver.ForceStop(); Scribe.loader.ForceStop(); }
        File.WriteAllText(path, "<save><kind>Leisure</kind><signature>joy</signature><ticks>100000</ticks><lastTick>200000</lastTick></save>");
        try {
            var oldObservation = new DeficitObservation();
            Scribe.loader.InitLoading(path);
            Scribe.loader.curParent = oldObservation;
            oldObservation.ExposeData();
            Check(oldObservation.StartTick == -1 && oldObservation.Ticks == 100000,
                "Actual old XML loads the new field default without losing deficit progress");
        } finally { Scribe.loader.ForceStop(); }

        var oldGame = Current.Game;
        var oldPrefs = typeof(Prefs).GetField("data", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        try {
            var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            game.tickManager = (TickManager)RuntimeHelpers.GetUninitializedObject(typeof(TickManager));
            typeof(TickManager).GetField("ticksGameInt", fields).SetValue(game.tickManager, 600000);
            Current.Game = game;
            var component = new GameComponent_Promises(game);
            component.FinalizeInit();
            Check(component.NextOffer == 630000 && component.LastObservation == -1 && component.NextObservation == 0,
                "Fresh or newly added component initializes spacing and an immediate observation");
            var pawn = Pawn(12);
            var peek = component.PeekState(pawn);
            var colonists = (List<ColonistPromises>)typeof(GameComponent_Promises).GetField("colonists", fields).GetValue(component);
            Check(colonists.Count == 0 && peek.Pawn == pawn, "Reading an untracked pawn does not enroll it");
            var state = component.StateFor(pawn);
            state.LastGenerationCheck = 590000;
            Check(object.ReferenceEquals(component.PeekState(pawn), state) && colonists.Count == 1, "Reading tracked state preserves identity");
            typeof(GameComponent_Promises).GetField("nextObservation", fields).SetValue(component, 602500);
            typeof(GameComponent_Promises).GetField("nextOffer", fields).SetValue(component, 610000);
            typeof(GameComponent_Promises).GetField("lastObservation", fields).SetValue(component, 600000);
            component.FinalizeInit();
            Check(component.NextOffer == 610000 && component.NextObservation == 602500 && component.LastObservation == 600000,
                "Existing schedules survive FinalizeInit");
            var ready = new List<KeyValuePair<ColonistPromises, RequestCandidate>> { olderHighId };
            typeof(GameComponent_Promises).GetMethod("OfferReady", fields).Invoke(component, new object[] { 600000, ready });
            Check(olderHighId.Key.Request == null && component.NextOffer == 610000,
                "The actual offer path rejects a ready candidate while the colony-wide gate is blocked");
            var prefs = new PrefsData { devMode = false };
            typeof(Prefs).GetField("data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, prefs);
            component.CheckRequestsNow(pawn);
            Check(state.LastGenerationCheck == 590000 && component.NextOffer == 610000, "Check-now is guarded when Development mode is off");
            state.Pawn = null;
            prefs.devMode = true;
            component.CheckRequestsNow(pawn);
            Check(state.LastGenerationCheck == 600000 && state.Deficits.Count == 0 && state.Request == null,
                "Check-now uses ordinary exclusion and reset logic without forcing a request");
            Check(component.NextObservation == 602500 && component.NextOffer == 610000, "Selected checks cannot bypass or advance global schedules");
            colonists.Clear(); // Reference resolution is outside this isolated scheduler fixture.
            try {
                Scribe.saver.InitSaving(path, "save");
                component.ExposeData();
                Scribe.saver.FinalizeSaving();
                var restored = new GameComponent_Promises(game);
                Scribe.loader.InitLoading(path);
                Scribe.loader.curParent = restored;
                restored.ExposeData();
                Check(restored.NextObservation == 602500 && restored.NextOffer == 610000 && restored.LastObservation == 600000,
                    "Global generation schedule and last scan round-trip through Scribe");
            } finally { Scribe.saver.ForceStop(); Scribe.loader.ForceStop(); }
            var marker = new ColonistPromises { LastGenerationCheck = 600000 };
            try {
                Scribe.saver.InitSaving(path, "save");
                marker.ExposeData();
                Scribe.saver.FinalizeSaving();
                var restored = new ColonistPromises();
                Scribe.loader.InitLoading(path);
                Scribe.loader.curParent = restored;
                restored.ExposeData();
                Check(restored.LastGenerationCheck == 600000, "The selected pawn's last check round-trips through Scribe");
            } finally { Scribe.saver.ForceStop(); Scribe.loader.ForceStop(); }
        } finally {
            Current.Game = oldGame;
            typeof(Prefs).GetField("data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, oldPrefs);
        }

        var oldActive = LanguageDatabase.activeLanguage;
        var oldDefault = LanguageDatabase.defaultLanguage;
        try {
            foreach (string folder in new[] { "English", "Ukrainian" })
            {
                LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, folder);
                string half = PromiseText.Number(0.5f);
                Check("CP_Remaining".Translate(half).ToString().Contains(half), "Remaining time uses a plain placeholder");
                Check("CP_Progress".Translate(half, PromiseText.Number(24)).ToString().Contains(half), "Progress numbers survive translation");
                Check("CP_ResponseTime".Translate(half, "3").ToString().Contains(half), "Response time survives translation");
                Check("CP_Cooldown".Translate(half).ToString().Contains(half), "Cooldown survives translation");
                Check("CP_Durability".Translate("80").ToString().Contains("80"), "Durability survives translation");
                Check(PromiseText.Consequences(new PromiseRecord { SuccessMood = 3, BrokenMood = -7 }).Contains("+3"),
                    "Consequence preview retains explicit signed values");
                var state = new ColonistPromises();
                state.Deficits.Add(saved);
                state.CooldownUntilTick = 900000;
                foreach (PromiseKind kind in Enum.GetValues(typeof(PromiseKind)))
                {
                    var evaluation = RequestGenerator.Evaluate(null, kind);
                    Check(evaluation.Candidate == null && evaluation.Reason == "CP_DebugMissing", "Shared evaluator excludes a missing pawn");
                    string diagnostic = RequestDiagnostics.Describe(state, kind, 120100);
                    Check(!diagnostic.Contains("CP_") && !diagnostic.Contains("{") && !diagnostic.Contains("??"),
                        "Diagnostic text resolves in " + folder);
                }
                Check(state.Deficits.Count == 1 && state.Deficits[0] == saved && saved.Ticks == 120000 && state.CooldownUntilTick == 900000,
                    "Rendering all diagnostics leaves saved observations and cooldown untouched");
            }
        } finally { LanguageDatabase.activeLanguage = oldActive; LanguageDatabase.defaultLanguage = oldDefault; }
        return checks;
    }
}
