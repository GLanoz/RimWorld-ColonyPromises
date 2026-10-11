using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using ColonyPromises;
using RimWorld;
using Verse;

public static class HousingChecks
{
    static int checks;
    static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    static ThoughtDef ReadMemory(string file, string name)
    {
        var xml = new XmlDocument(); xml.Load(file);
        var node = xml.SelectSingleNode("Defs/ThoughtDef[defName='" + name + "']");
        var def = new ThoughtDef { defName = name, durationDays = float.Parse(node["durationDays"].InnerText,
            System.Globalization.CultureInfo.InvariantCulture), stages = new List<ThoughtStage>() };
        foreach (XmlNode stage in node["stages"].ChildNodes)
        {
            if (stage.NodeType != XmlNodeType.Element) continue;
            def.stages.Add(stage.Attributes?["IsNull"]?.Value == "True" ? null : new ThoughtStage {
                baseMoodEffect = float.Parse(stage["baseMoodEffect"].InnerText, System.Globalization.CultureInfo.InvariantCulture) });
        }
        DefDatabase<ThoughtDef>.Add(def);
        return def;
    }
    public static int Run(Game game, Pawn a, Pawn b, string root, string path)
    {
        var oldComponents = game.components;
        var oldRoomStat = RoomStatDefOf.Impressiveness;
        var oldThoughtsA = a.needs.mood.thoughts; var oldThoughtsB = b.needs.mood.thoughts;
        var oldStoryA = a.story; var oldStoryB = b.story;
        var oldHealthA = a.health.hediffSet; var oldHealthB = b.health.hediffSet;
        try
        {
            var component = new GameComponent_Promises(game); game.components = new List<GameComponent> { component };
            a.health.hediffSet = new HediffSet(a); b.health.hediffSet = new HediffSet(b);
            a.story = new Pawn_StoryTracker(a); b.story = new Pawn_StoryTracker(b);
            RoomStatDefOf.Impressiveness = new RoomStatDef { scoreStages = Enumerable.Range(0, 10)
                .Select(i => new RoomStatScoreStage { label = "Stage " + i }).ToList() };
            // Read the actual Core values from the same installation used by this host.
            string gameRoot = Environment.GetEnvironmentVariable("COLONY_PROMISES_GAME_ROOT")
                ?? @"C:\Games\Steam\steamapps\common\RimWorld";
            string core = Path.Combine(gameRoot, "Data", "Core", "Defs", "ThoughtDefs");
            var bedroom = ReadMemory(Path.Combine(core, "Thoughts_Memory_RoomStats.xml"), "SleptInBedroom");
            var barracks = ReadMemory(Path.Combine(core, "Thoughts_Memory_RoomStats.xml"), "SleptInBarracks");
            var ground = ReadMemory(Path.Combine(core, "Thoughts_Memory_Misc.xml"), "SleptOnGround");
            var food = ReadMemory(Path.Combine(core, "Thoughts_Memory_Misc.xml"), "AteWithoutTable");
            Check(bedroom.stages[0].baseMoodEffect == -2 && barracks.stages[0].baseMoodEffect == -7
                && barracks.stages[6].baseMoodEffect == 1 && ground.stages[0].baseMoodEffect == -4,
                "Housing fixture uses actual Core bedroom/barracks/ground effects");
            Check(HousingRules.FirstNonnegative(bedroom.stages.Select(s => s?.baseMoodEffect ?? 0)) == 1
                && HousingRules.FirstNonnegative(barracks.stages.Select(s => s?.baseMoodEffect ?? 0)) == 6,
                "Targets choose the first nonnegative game category, not an expectation floor");
            Check(!HousingRules.SupportedCause(food.defName), "Unrelated negative food memory cannot start housing observation");
            Check(!HousingRules.MemoryProblem("SleptInBarracks", true, true, false, true, barracks.stages[6].baseMoodEffect, false),
                "A satisfied barracks resident has no housing request evidence");
            var issues = new List<HousingIssue> { new HousingIssue { Pawn = a, Cause = ground.defName, Mood = -4, Memory = true,
                Current = HousingRules.NegativeEvidence(-4, HousingRules.MemoryProblem(ground.defName, false, false, false, false, 0, false), true, false),
                Supported = true, MinimumStage = 1 } };
            Check(issues[0].Current && issues[0].Mood == -4, "Negative ground evidence and missing bed confirm housing dissatisfaction");
            component.StateFor(a).HousingWitnesses = HousingRules.Witnesses(issues);
            issues[0].Retained = true;
            Check(HousingRules.NegativeEvidence(-4, true, false, component.StateFor(a).HousingWitnesses.Count == 1),
                "Memory expiration alone does not erase a still-current confirmed issue");
            Check(!HousingRules.MemoryProblem(ground.defName, true, true, false, false, 0, false),
                "A usable material-cost bed resolves the ground problem even while the memory remains");
            Check(!HousingRules.NegativeEvidence(-4, false, true, true), "A stale negative memory cannot survive repaired physical conditions");
            var evaluation = new RequestEvaluation { HousingIssues = issues };
            var candidate = HousingRules.Candidate(a, null, evaluation);
            Check(candidate != null && candidate.Terms.HousingBased && candidate.Terms.RequiresConstructedBed
                && candidate.Terms.RoomStage == 1, "Missing-bed evidence creates a targeted minimum-quality request");
            var evidence = RequestGenerator.Project(null, candidate, PromiseKind.Bedroom, 1000, false);
            evidence = RequestGenerator.Project(evidence, candidate, PromiseKind.Bedroom, 3500, false);
            Check(!RequestRules.Ready(evidence.Ticks, false, 3500, 0), "One bad night cannot bypass two-day persistence");
            for (int i = 2; i <= 48; i++) evidence = RequestGenerator.Project(evidence, candidate, PromiseKind.Bedroom, 1000 + i * 2500, false);
            Check(RequestRules.Ready(evidence.Ticks, false, 121000, 0), "A matching unresolved cause reaches the existing two-day gate");
            var old = new DeficitObservation { Signature = "room:3", Ticks = 120000, LastTick = 1000, StartTick = 0 };
            Check(RequestGenerator.Project(old, candidate, PromiseKind.Bedroom, 3500, false).Ticks == 0,
                "Expectation-only legacy evidence restarts for the new housing signature");
            var pair = HousingRules.Candidate(a, b, new RequestEvaluation { HousingIssues = issues });
            Check(pair != null && pair.Terms.IsShared && pair.HousingReasons.Count == 1 && pair.HousingReasons[0].Subject == a,
                "One dissatisfied partner can produce one shared request without attributing dissatisfaction to the other");
            var partnerIssue = new HousingIssue { Pawn = b, Cause = "SleptOnGround", Mood = -4, Current = true, Supported = true, Memory = true, MinimumStage = 1 };
            component.StateFor(a).Deficits.Add(new DeficitObservation { Kind = PromiseKind.Bedroom, Signature = pair.Signature });
            var both = HousingRules.Candidate(a, b, new RequestEvaluation { HousingIssues = new List<HousingIssue> { issues[0], partnerIssue } });
            Check(both.Signature == pair.Signature && both.HousingReasons.Single().Subject == a,
                "A new partner concern does not erase the older qualifying anchor");
            var onlyPartner = HousingRules.Candidate(a, b, new RequestEvaluation { HousingIssues = new List<HousingIssue> { partnerIssue } });
            Check(onlyPartner != null && onlyPartner.HousingReasons.Single().Subject == b && onlyPartner.Signature != pair.Signature,
                "The second partner alone can qualify, but a changed anchor starts fresh evidence");
            Check(!HousingRules.OriginalCauseCurrent(RequestGenerator.Offer(pair, 1000), new[] { partnerIssue }),
                "Another person's new problem cannot keep an obsolete original request alive");
            var state = new ColonistPromises { Pawn = a, Request = RequestGenerator.Offer(pair, 1000) };
            state.HousingWitnesses.Add(new HousingWitness { Cause = "SleptOnGround", Mood = -4 });
            RequestLifecycle.Accept(state, state.Request, 1000);
            Check(state.HousingWitnesses.Count == 0, "Acceptance clears evidence so a later request needs fresh confirmation");
            int deadline = state.Active.DeadlineTick; int stage = state.Active.RoomStage; float mood = state.Active.SuccessMood;
            component.StateFor(a).HousingWitnesses.Clear();
            Check(state.Active.RoomStage == stage && state.Active.DeadlineTick == deadline && state.Active.SuccessMood == mood,
                "Accepted terms are independent of later housing thoughts");
            var legacyRequest = RequestGenerator.Offer(new RequestCandidate { Terms = new PromiseRecord { Kind = PromiseKind.Bedroom } }, 1000);
            Check(!RequestGenerator.Relevant(a, legacyRequest), "Legacy unaccepted expectation-only requests are obsolete without recovery");
            foreach (string language in new[] { "English", "Ukrainian" })
            {
                LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, language);
                string reason = HousingRules.Reason(RequestGenerator.Offer(pair, 1000));
                Check(reason.Contains(a.LabelShort) && !reason.Contains(b.LabelShort) && !reason.Contains("CP_") && !reason.Contains("{"),
                    "Personal housing reason is localized and attributes only the affected partner in " + language);
                Check(HousingRules.Describe(issues).Contains(PromiseText.Signed(-4)), "Diagnostic shows the observed effect in " + language);
                var barracksTerms = new PromiseRecord { Kind = PromiseKind.Bedroom, RoomStage = 6, RequiresBarracks = true, HousingBased = true };
                string conditions = PromiseConditions.Get(PromiseKind.Bedroom).Describe(barracksTerms);
                Check(!conditions.Contains("CP_") && !conditions.Contains("{"), "Explicit barracks conditions format in " + language);
            }
            var witness = new HousingWitness { Cause = "SleptOnGround", Mood = -4 };
            try { Scribe.saver.InitSaving(path, "save"); witness.ExposeData(); Scribe.saver.FinalizeSaving(); }
            finally { Scribe.saver.ForceStop(); }
            var loaded = new HousingWitness();
            try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = loaded; loaded.ExposeData(); }
            finally { Scribe.loader.ForceStop(); }
            Check(loaded.Cause == witness.Cause && loaded.Mood == witness.Mood, "Confirmed housing evidence survives Scribe");
            File.WriteAllText(path, "<save><kind>Bedroom</kind><roomStage>3</roomStage><progressTicks>12345</progressTicks><deadlineTick>123456</deadlineTick></save>");
            var oldRecord = new PromiseRecord();
            try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = oldRecord; oldRecord.ExposeData(); }
            finally { Scribe.loader.ForceStop(); }
            Check(!oldRecord.HousingBased && !oldRecord.RequiresBarracks && oldRecord.RoomStage == 3
                && oldRecord.ProgressTicks == 12345 && oldRecord.DeadlineTick == 123456, "Old accepted records retain exact conditions and progress");
        }
        finally {
            DefDatabase<ThoughtDef>.Clear(); game.components = oldComponents; RoomStatDefOf.Impressiveness = oldRoomStat;
            a.needs.mood.thoughts = oldThoughtsA; b.needs.mood.thoughts = oldThoughtsB;
            a.story = oldStoryA; b.story = oldStoryB; a.health.hediffSet = oldHealthA; b.health.hediffSet = oldHealthB;
        }
        return checks;
    }
}
