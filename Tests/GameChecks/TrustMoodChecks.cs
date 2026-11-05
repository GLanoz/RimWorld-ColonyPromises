using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using ColonyPromises;
using RimWorld;
using Verse;

public static class TrustMoodChecks
{
    sealed class LiveMoodSeeker : Need_Seeker
    {
        readonly Need_Mood source;
        public LiveMoodSeeker(Pawn pawn, Need_Mood source) : base(pawn) { this.source = source; def = source.def; }
        public override float CurInstantLevel => source.CurInstantLevel;
    }
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Set(object instance, string name, object value)
    {
        for (var type = instance.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Fields);
            if (field != null) { field.SetValue(instance, value); return; }
        }
        throw new MissingFieldException(name);
    }
    public static int Run(string root, string path, Pawn pawn)
    {
        checks = 0;
        var xml = XDocument.Load(Path.Combine(root, "Defs/ThoughtDefs/LeadershipTrust.xml")).Root.Element("ThoughtDef");
        var stages = xml.Element("stages").Elements("li").ToArray();
        Check(stages.Length == 10 && xml.Element("thoughtClass").Value == typeof(Thought_LeadershipTrust).FullName
            && xml.Element("workerClass").Value == typeof(ThoughtWorker_LeadershipTrust).FullName,
            "One situational definition provides all trust stages without memory duration or stacks");
        var ranges = new[] { (0, 9, -5), (10, 19, -4), (20, 29, -3), (30, 39, -2), (40, 49, -1),
            (50, 50, 0), (51, 60, 1), (61, 70, 2), (71, 80, 3), (81, 90, 4), (91, 100, 5) };
        foreach (var range in ranges)
            for (int trust = range.Item1; trust <= range.Item2; trust++)
            {
                int stage = TrustMood.Stage(trust);
                Check(TrustMood.Offset(trust) == range.Item3 && (range.Item3 == 0 ? stage == -1
                    : int.Parse(stages[stage].Element("baseMoodEffect").Value) == range.Item3),
                    "Actual XML effect matches every trust boundary: " + trust);
            }
        foreach (string language in new[] { "English", "Ukrainian" })
        {
            var localized = XDocument.Load(Path.Combine(root, "Languages", language, "DefInjected/ThoughtDef/Promises.xml")).Root;
            string low = language == "English" ? "No one here listens to me" : "\u041c\u0435\u043d\u0435 \u0442\u0443\u0442 \u043d\u0456\u0445\u0442\u043e \u043d\u0435 \u0441\u043b\u0443\u0445\u0430\u0454";
            string high = language == "English" ? "I feel heard here" : "\u0422\u0443\u0442 \u0434\u043e \u043c\u0435\u043d\u0435 \u0434\u043e\u0441\u043b\u0443\u0445\u0430\u044e\u0442\u044c\u0441\u044f";
            for (int i = 0; i < stages.Length; i++)
                Check(localized.Element("CP_LeadershipTrust.stages." + i + ".label")?.Value == (i < 5 ? low : high),
                    "Every directional stage uses the same requested label in " + language);
        }
        var game = Current.Game;
        var previousComponents = game.components;
        var originalMood = pawn.needs.mood;
        var originalHediffs = pawn.health.hediffSet;
        var originalStory = pawn.story;
        var originalLifeStages = pawn.def.race.lifeStageAges;
        var thoughtDefs = typeof(ThoughtUtility).GetField("situationalNonSocialThoughtDefs", Fields);
        var originalDefs = thoughtDefs.GetValue(null);
        var oldStoryteller = game.storyteller;
        try
        {
            var component = new GameComponent_Promises(game);
            game.components = new List<GameComponent> { component };
            var state = component.StateFor(pawn);
            pawn.story = (Pawn_StoryTracker)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Pawn_StoryTracker));
            pawn.story.traits = new TraitSet(pawn);
            pawn.def.race.lifeStageAges = new List<LifeStageAge> { new LifeStageAge {
                def = new LifeStageDef { developmentalStage = DevelopmentalStage.Adult }, minAge = 0 } };
            pawn.health.hediffSet = new HediffSet(pawn);
            game.storyteller = (Storyteller)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Storyteller));
            game.storyteller.difficulty = new Difficulty();
            var def = new ThoughtDef { defName = "CP_LeadershipTrust", thoughtClass = typeof(Thought_LeadershipTrust),
                workerClass = typeof(ThoughtWorker_LeadershipTrust), stages = stages.Select(s => new ThoughtStage {
                    label = s.Element("label").Value, baseMoodEffect = float.Parse(s.Element("baseMoodEffect").Value, CultureInfo.InvariantCulture) }).ToList() };
            thoughtDefs.SetValue(null, new List<ThoughtDef> { def });
            var live = new Need_Mood(pawn) { def = new NeedDef { baseLevel = 0.64f, seekerRisePerHour = 0.08f, seekerFallPerHour = 0.08f } };
            pawn.needs.mood = live;
            live.CurLevel = 0.64f;
            foreach (int trust in new[] { 49, 50, 51, 100, 50, 0 })
            {
                state.Trust = trust;
                var thoughts = new List<Thought>();
                live.thoughts.GetAllMoodThoughts(thoughts);
                Check(thoughts.Count(t => t.def == def) == (trust == 50 ? 0 : 1),
                    "Trust changes rebuild exactly one situational thought, or none at 50: trust=" + trust
                    + "; count=" + thoughts.Count + "; eligible=" + PromiseConditions.EligibilityReason(pawn)
                    + "; canGet=" + ThoughtUtility.CanGetThought(pawn, def) + "; stage=" + pawn.DevelopmentalStage
                    + "; worker=" + def.Worker.CurrentState(pawn).ActiveFor(def));
                Check(Math.Abs(live.thoughts.TotalMoodOffset() - TrustMood.Offset(trust)) < 0.00001,
                    "Actual thought handler applies the current trust stage immediately");
            }
            // The real getter clamps before mood moves. Removing an offset after this clamp would be wrong.
            int now = 1000;
            foreach (float baseline in new[] { 0.64f, 0.36f, 0.99f, 0.01f })
            {
                foreach (int trust in new[] { 0, 50, 100 })
                {
                    state.Trust = trust;
                    state.HasTrustNeutralMood = false;
                    state.NeutralMood = null;
                    state.LastTrustNeutralMoodTick = -1;
                    live.def.baseLevel = baseline;
                    live.CurLevel = baseline;
                    for (int i = 0; i < 30; i++)
                    {
                        now += 150;
                        Set(game.tickManager, "ticksGameInt", now);
                        TrustMood.Advance(state, live, now);
                        Check(Math.Abs(state.TrustNeutralMoodLevel - baseline) < 0.00001,
                            "Trust alone cannot shift the neutral mood trajectory, including clamped targets");
                        live.CurLevel = Math.Max(0, Math.Min(1, baseline + TrustMood.Offset(trust) / 100f));
                    }
                    Check(Math.Abs(state.NeutralMood.CurInstantLevel - baseline) < 0.00001,
                        "The actual game target excludes only the trust thought before clamping");
                }
            }
            foreach (var scenario in new[] { (0.64f, 100, 0), (0.36f, 20, 0), (0.65f, 49, 1), (0.35f, 80, -1) })
            {
                state.Trust = scenario.Item2;
                state.NeutralMood = null;
                state.HasTrustNeutralMood = true;
                state.TrustNeutralMoodLevel = scenario.Item1;
                state.LastTrustNeutralMoodTick = -1;
                state.MoodTrustProgressTicks = 0;
                state.MoodTrustWeightedSum = 0;
                MoodTrust.RestartSampling(state);
                live.def.baseLevel = scenario.Item1;
                live.CurLevel = scenario.Item1;
                var visible = new LiveMoodSeeker(pawn, live) { CurLevel = scenario.Item1 };
                int start = now;
                MoodTrust.Observe(state, state.TrustNeutralMoodLevel, start);
                for (int elapsed = 1; elapsed <= TrustRules.MoodTrustPeriodTicks; elapsed++)
                {
                    now = start + elapsed;
                    if (elapsed % 150 == 0)
                    {
                        Set(game.tickManager, "ticksGameInt", now);
                        TrustMood.Advance(state, live, now);
                        visible.NeedInterval();
                        live.CurLevel = visible.CurLevel;
                    }
                    if (elapsed % TrustRules.MoodObservationInterval == 0)
                        MoodTrust.Observe(state, state.TrustNeutralMoodLevel, now);
                }
                Check(state.Trust == scenario.Item2 + scenario.Item3 && state.MoodTrustProgressTicks == 0,
                    "Sixty verified hours use only neutral mood at/near thresholds despite opposite visible effects");
                Check(Math.Abs(visible.CurLevel - scenario.Item1) > 0.009f,
                    "The same game seeker actually changes visible mood while neutral mood stays independent");
            }
            var memory = new Thought_Memory { pawn = pawn, def = new ThoughtDef { defName = "CP_PromiseBroken",
                thoughtClass = typeof(Thought_Memory), stages = new List<ThoughtStage> { new ThoughtStage { baseMoodEffect = -8 } } } };
            live.def.baseLevel = 0.7f;
            live.thoughts.memories.Memories.Add(memory);
            state.Trust = 0;
            state.NeutralMood = null;
            state.HasTrustNeutralMood = false;
            live.CurLevel = 0.7f;
            now += 150; Set(game.tickManager, "ticksGameInt", now);
            TrustMood.Advance(state, live, now);
            Check(Math.Abs(state.NeutralMood.CurInstantLevel - 0.62f) < 0.00001,
                "Temporary promise memory remains in the real neutral target");
            Check(state.TrustNeutralMoodLevel < 0.7f && state.TrustNeutralMoodLevel > 0.62f,
                "The neutral mood follows the real gradual seeker instead of jumping to its target");
            float savedLevel = state.TrustNeutralMoodLevel;
            TrustMood.Advance(state, live, now);
            Check(state.TrustNeutralMoodLevel == savedLevel, "Same-tick callbacks cannot advance neutral mood twice");
            state.MoodTrustProgressTicks = 42 * PromiseBalance.TicksPerHour;
            state.MoodTrustWeightedSum = 0.7 * state.MoodTrustProgressTicks;
            // Pawn reference resolution is covered by the shared-record host. Keep this
            // scalar round-trip independent of native map/notification initialization.
            state.Pawn = null;
            try { Scribe.saver.InitSaving(path, "save"); state.ExposeData(); Scribe.saver.FinalizeSaving(); }
            finally { Scribe.saver.ForceStop(); state.Pawn = pawn; }
            var loaded = new ColonistPromises();
            try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = loaded; loaded.ExposeData(); }
            finally { Scribe.loader.ForceStop(); }
            Check(loaded.HasTrustNeutralMood && loaded.TrustNeutralMoodLevel == savedLevel && loaded.Trust == state.Trust
                && loaded.MoodTrustProgressTicks == state.MoodTrustProgressTicks && loaded.MoodTrustWeightedSum == state.MoodTrustWeightedSum,
                "Scribe preserves neutral mood, trust and the existing partially observed window");
            Check(!File.ReadAllText(path).Contains("CP_LeadershipTrust"), "No trust thought is saved as a stacking memory");
            loaded.Pawn = pawn;
            var restoredComponent = new GameComponent_Promises(game);
            Set(restoredComponent, "colonists", new List<ColonistPromises> { loaded });
            game.components = new List<GameComponent> { restoredComponent };
            restoredComponent.FinalizeInit();
            var restoredThoughts = new List<Thought>();
            live.thoughts.GetAllMoodThoughts(restoredThoughts);
            Check(restoredThoughts.Count(t => t.def == def) == 1 && loaded.LastTrustNeutralMoodTick == state.LastTrustNeutralMoodTick
                && loaded.NeutralMood == null && loaded.TrustNeutralMoodLevel == savedLevel
                && loaded.MoodTrustProgressTicks == state.MoodTrustProgressTicks,
                "Actual component load restores one thought and keeps the saved neutral mood and duplicate guard");
            TrustMood.Advance(loaded, live, now);
            Check(loaded.TrustNeutralMoodLevel == savedLevel && loaded.NeutralMood == null,
                "A repeated mood interval at the saved tick cannot move neutral mood again after loading");
            restoredComponent.FinalizeInit();
            live.thoughts.GetAllMoodThoughts(restoredThoughts);
            Check(restoredThoughts.Count(t => t.def == def) == 1, "Repeated initialization never duplicates the trust effect");
            File.WriteAllText(path, "<save><trust>29</trust><moodTrustAverageVersion>1</moodTrustAverageVersion><moodTrustProgressTicks>50000</moodTrustProgressTicks><moodTrustWeightedSum>35000</moodTrustWeightedSum></save>");
            try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = loaded; loaded.ExposeData(); }
            finally { Scribe.loader.ForceStop(); }
            Check(!loaded.HasTrustNeutralMood && loaded.Trust == 29 && loaded.MoodTrustProgressTicks == 50000
                && loaded.MoodTrustWeightedSum == 35000, "Older average windows retain trust and progress without invented neutral history");
        }
        finally
        {
            pawn.needs.mood = originalMood;
            pawn.health.hediffSet = originalHediffs;
            pawn.story = originalStory;
            pawn.def.race.lifeStageAges = originalLifeStages;
            game.components = previousComponents;
            game.storyteller = oldStoryteller;
            thoughtDefs.SetValue(null, originalDefs);
        }
        return checks;
    }
}
