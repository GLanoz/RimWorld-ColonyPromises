using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using ColonyPromises;
using RimWorld;
using RimWorld.Planet;
using Verse;

public static class ExpansionChecks
{
    static int checks;
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Set(object target, string name, object value)
    {
        for (var t = target.GetType(); t != null; t = t.BaseType)
        { var field = t.GetField(name, Fields); if (field != null) { field.SetValue(target, value); return; } }
        throw new MissingFieldException(name);
    }
    static ThingDef Item(string name)
    {
        var existing = DefDatabase<ThingDef>.GetNamedSilentFail(name);
        if (existing != null) return existing;
        var result = Empty<ThingDef>(); result.defName = result.label = name; DefDatabase<ThingDef>.Add(result); return result;
    }
    static RoomRequirement RoomRule(XElement node)
    {
        var type = typeof(RoomRequirement).Assembly.GetType("RimWorld." + node.Attribute("Class").Value, true);
        var result = (RoomRequirement)Activator.CreateInstance(type);
        foreach (var child in node.Elements())
        {
            var field = type.GetField(child.Name.LocalName, Fields);
            if (field == null) continue;
            if (field.FieldType == typeof(int)) field.SetValue(result, int.Parse(child.Value, CultureInfo.InvariantCulture));
            else if (field.FieldType == typeof(string)) field.SetValue(result, child.Value);
            else if (field.FieldType == typeof(List<string>)) field.SetValue(result, child.Elements().Select(e => e.Value).ToList());
            else if (field.FieldType == typeof(List<ThingDef>)) field.SetValue(result, child.Elements().Select(e => Item(e.Value)).ToList());
            else if (field.FieldType == typeof(ThingDef)) field.SetValue(result, Item(child.Value));
        }
        return result;
    }
    static void CheckApparelAlternatives(Pawn pawn, RoyalTitleDef title, XDocument titles, string path)
    {
        var source = titles.Descendants("requiredApparel").Elements("li").First();
        var groups = source.Element("bodyPartGroupsMatchAny").Elements().Select(e => {
            var group = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail(e.Value);
            if (group == null) { group = new BodyPartGroupDef { defName = e.Value }; DefDatabase<BodyPartGroupDef>.Add(group); }
            return group;
        }).ToList();
        var required = source.Element("requiredTags")?.Elements().Select(e => e.Value).ToList();
        var allowed = source.Element("allowedTags")?.Elements().Select(e => e.Value).ToList();
        var requirement = new ApparelRequirement { bodyPartGroupsMatchAny = groups, requiredTags = required, allowedTags = allowed };
        string tag = (required ?? allowed).First();
        var body = new BodyDef { defName = "ExpansionFixtureBody" };
        var part = new BodyPartRecord { groups = groups, def = new BodyPartDef { defName = "ExpansionFixturePart" } };
        Set(body, "cachedAllParts", new List<BodyPartRecord> { part });
        pawn.def.race.body = body;
        pawn.def.race.lifeStageAges = new List<LifeStageAge> { new LifeStageAge { minAge = 0, def = new LifeStageDef { developmentalStage = DevelopmentalStage.Adult } } };
        pawn.apparel = new Pawn_ApparelTracker(pawn);
        var target = Item("ExpansionFixtureRoyalGarment");
        var alternative = Item("ExpansionFixtureRoyalAlternative");
        foreach (var def in new[] { target, alternative })
        {
            def.thingClass = typeof(Apparel); def.comps = new List<CompProperties>();
            def.apparel = new ApparelProperties { bodyPartGroups = groups, tags = new List<string> { tag },
                layers = new List<ApparelLayerDef>(), developmentalStageFilter = DevelopmentalStage.Adult };
        }
        pawn.royalty.allowApparelRequirements = true;
        title.requiredApparel = new List<ApparelRequirement> { requirement };
        Set(pawn.apparel, "allRequirements", new List<ApparelRequirementWithSource> {
            new ApparelRequirementWithSource(requirement, new RoyalTitle { def = title }) });
        Set(pawn.apparel, "allRequirementsDirty", false);
        Check(requirement.ApparelMeetsRequirement(target) && requirement.ApparelMeetsRequirement(alternative),
            "Actual royal tag category admits alternative models rather than a hardcoded garment list");
        var manual = ExpansionRequests.ApparelOptions(pawn, "RoyalApparel", true).ToList();
        Check(manual.Any(r => r.EquipmentDef == target) && manual.Any(r => r.EquipmentDef == alternative),
            "Manual royal clothing uses actual allowed alternatives without an existing deficit");
        var captured = manual.First(r => r.EquipmentDef == target);
        (requirement.requiredTags ?? requirement.allowedTags).Clear();
        Check(captured.ApparelRequirement.ApparelMeetsRequirement(target), "Changing source title tags does not rewrite fixed garment terms");
        typeof(ModsConfig).GetField("royaltyActive", Fields).SetValue(null, false);
        var previousRoyalty = pawn.royalty; pawn.royalty = null;
        var ideology = Empty<Ideo>(); Set(ideology, "precepts", new List<Precept>());
        ideology.style = Empty<IdeoStyleTracker>();
        pawn.ideo = Empty<Pawn_IdeoTracker>(); Set(pawn.ideo, "ideo", ideology);
        Check(!ExpansionRequests.StyleDataReady(pawn) && !ExpansionRequests.StyleAllowed(pawn, pawn.story.hairDef),
            "Inspection of uninitialized style maps rejects a target without lazily creating saved preferences");
        Check(typeof(IdeoStyleTracker).GetField("hairFrequencies", Fields).GetValue(ideology.style) == null,
            "Reading style eligibility leaves the original frequency map untouched");
        var role = new Precept_RoleSingle { def = new PreceptDef { defName = "ExpansionFixtureRole" } };
        Set(role, "ID", 81101); Set(ideology, "precepts", new List<Precept> { role });
        Set(pawn.apparel, "allRequirements", new List<ApparelRequirementWithSource> {
            new ApparelRequirementWithSource(captured.ApparelRequirement, role) });
        var roleRecord = captured.CopyForPreview(); roleRecord.SourceTitle = null; roleRecord.EquipmentCause = "IdeoRoleApparel";
        roleRecord.SourceIdeology = ExpansionRequests.IdeologyKey(pawn); roleRecord.SourcePreceptId = role.Id;
        bool roleActive = ApparelUtility.IsRequirementActive(captured.ApparelRequirement, ApparelRequirementSource.Role, pawn, out string roleReason);
        Check(ExpansionRequests.Applicable(pawn, roleRecord), "An active role/category keeps its fixed garment applicable: " + roleActive + "/" + roleReason + "/" + roleRecord.SourceIdeology + "/" + ExpansionRequests.IdeologyKey(pawn));
        Set(ideology, "precepts", new List<Precept>());
        Check(!ExpansionRequests.Applicable(pawn, roleRecord), "Actual role removal closes rather than retargets a garment promise");
        var desired = new Precept_Apparel { def = new PreceptDef { defName = "ExpansionFixtureDesired",
            comps = new List<PreceptComp> { new PreceptComp_Apparel_Desired() } }, apparelDef = target };
        Set(desired, "ID", 81102); Set(ideology, "precepts", new List<Precept> { desired });
        var wishes = ExpansionRequests.ApparelOptions(pawn, "IdeoDesiredApparel", false).ToList();
        Check(wishes.Count == 1 && wishes[0].EquipmentDef == target && ExpansionRequests.Applicable(pawn, wishes[0]),
            "Applicable desired-precept data creates a concrete one-item wish without inventing a negative thought");
        var owner = (ThingOwner<Apparel>)typeof(Pawn_ApparelTracker).GetField("wornApparel", Fields).GetValue(pawn.apparel);
        owner.InnerListForReading.Add(new Apparel { def = target });
        Check(!ExpansionRequests.ApparelOptions(pawn, "IdeoDesiredApparel", false).Any(), "Already worn desired clothing removes the wish");
        Check(ExpansionRequests.ApparelOptions(pawn, "IdeoDesiredApparel", true).Any(), "Manual testing can retain already satisfied desired terms");
        Set(ideology, "precepts", new List<Precept>());
        Check(!ExpansionRequests.Applicable(pawn, wishes[0]), "Removing the actual desired precept makes only that obligation obsolete");
        pawn.royalty = previousRoyalty;
        typeof(ModsConfig).GetField("royaltyActive", Fields).SetValue(null, true);
        var savedTypes = new[] { typeof(PromiseRecord), typeof(ApparelRequirement) };
        var all = (List<Type>)typeof(GenTypes).GetField("allTypesCached", Fields).GetValue(null);
        foreach (var type in savedTypes) if (!all.Contains(type)) all.Add(type);
        var cache = (System.Collections.IDictionary)typeof(GenTypes).GetField("typeCache", Fields).GetValue(null);
        var key = typeof(GenTypes).GetNestedType("TypeCacheKey", BindingFlags.NonPublic);
        foreach (var type in savedTypes) cache[Activator.CreateInstance(key, Fields, null, new object[] { type.FullName, null }, null)] = type;
        try { Scribe.saver.InitSaving(path, "save"); captured.ExposeData(); Scribe.saver.FinalizeSaving(); } finally { Scribe.saver.ForceStop(); }
        var loaded = new PromiseRecord();
        try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = loaded; loaded.ExposeData(); } finally { Scribe.loader.ForceStop(); }
        Check(loaded.ExpansionRequest && loaded.SourceTitle == title && loaded.EquipmentDef == target
            && loaded.ApparelRequirement.ApparelMeetsRequirement(target), "Royal clothing source and category snapshot survive Scribe");
    }
    public static int Run(string root, string path)
    {
        string gameRoot = Environment.GetEnvironmentVariable("COLONY_PROMISES_GAME_ROOT");
        var flags = new[] { "royaltyActive", "ideologyActive", "biotechActive", "anomalyActive", "odysseyActive" }
            .Select(n => typeof(ModsConfig).GetField(n, Fields)).ToArray();
        var oldFlags = flags.Select(f => f.GetValue(null)).ToArray();
        var oldGame = Current.Game; var oldLanguage = LanguageDatabase.activeLanguage; var oldDefault = LanguageDatabase.defaultLanguage;
        try
        {
            Check((int)PromiseKind.Bedroom == 0 && (int)PromiseKind.Leisure == 1 && (int)PromiseKind.Equipment == 2
                && (int)PromiseKind.Substance == 3, "Existing promise kind values remain stable");
            var titles = XDocument.Load(Path.Combine(gameRoot, "Data", "Royalty", "Defs", "RoyalTitles", "RoyalTitles_Empire.xml"));
            var snapshots = new List<BedroomRequirement>();
            foreach (var node in titles.Descendants("throneRoomRequirements").Elements("li"))
            {
                var rule = RoomRule(node); var snapshot = BedroomRequirement.Capture(rule);
                Check(snapshot != null && snapshot.Rebuild().GetType() == rule.GetType(), "Actual royal throne rule round-trips: " + rule.GetType().Name);
                snapshots.Add(snapshot);
            }
            Check(snapshots.Any(s => s.Kind == "AssignedThrone") && snapshots.Any(s => s.Kind == "Area")
                && snapshots.Any(s => s.Kind == "Impressiveness"), "Actual throne requirements include assigned furniture and room constraints");
            var apparelXml = XDocument.Load(Path.Combine(gameRoot, "Data", "Ideology", "Defs", "PreceptDefs", "Precepts_Apparel.xml"));
            foreach (var thought in apparelXml.Root.Elements("ThoughtDef"))
                Check(thought.Descendants("baseMoodEffect").All(m => float.Parse(m.Value, CultureInfo.InvariantCulture) > 0),
                    "Desired apparel is a positive preference, not an invented negative thought");
            var thoughtXml = XDocument.Load(Path.Combine(gameRoot, "Data", "Royalty", "Defs", "RoyalTitles", "Thoughts_RoyalTitles.xml"));
            foreach (string cause in new[] { "TitleNoThroneRoom", "TitleThroneroomRequirementsNotMet", "TitleApparelRequirementNotMet", "TitleApparelMinQualityNotMet" })
                Check(thoughtXml.Root.Elements("ThoughtDef").Single(n => (string)n.Element("defName") == cause)
                    .Descendants("baseMoodEffect").Any(m => float.Parse(m.Value, CultureInfo.InvariantCulture) < 0), "Actual negative royal cause exists: " + cause);
            var hairs = XDocument.Load(Path.Combine(gameRoot, "Data", "Core", "Defs", "Misc", "HairDefs", "HairsGeneral.xml"))
                .Root.Elements("HairDef").Where(e => e.Element("defName") != null).Take(2).ToList();
            var oldHair = new HairDef { defName = hairs[0].Element("defName").Value, label = hairs[0].Element("label").Value };
            var newHair = new HairDef { defName = hairs[1].Element("defName").Value, label = hairs[1].Element("label").Value };
            DefDatabase<HairDef>.Add(oldHair); DefDatabase<HairDef>.Add(newHair);
            var beardNodes = XDocument.Load(Path.Combine(gameRoot, "Data", "Core", "Defs", "BeardDefs", "BeardDefs.xml"))
                .Root.Elements("BeardDef").Where(e => e.Element("defName") != null).Take(2).ToList();
            var oldBeard = new BeardDef { defName = beardNodes[0].Element("defName").Value, label = beardNodes[0].Element("label").Value };
            var newBeard = new BeardDef { defName = beardNodes[1].Element("defName").Value, label = beardNodes[1].Element("label").Value };
            DefDatabase<BeardDef>.Add(oldBeard); DefDatabase<BeardDef>.Add(newBeard);
            var game = Empty<Game>(); game.tickManager = Empty<TickManager>(); Set(game.tickManager, "ticksGameInt", 1000);
            game.questManager = new QuestManager();
            var world = Empty<World>(); world.factionManager = Empty<FactionManager>();
            var faction = Empty<Faction>(); faction.def = new FactionDef { isPlayer = true };
            Set(world.factionManager, "ofPlayer", faction); Set(game, "worldInt", world); Current.Game = game;
            var pawn = (Pawn)typeof(SharedBedroomChecks).GetMethod("Pawn", Fields).Invoke(null, new object[] { 81001, faction });
            pawn.kindDef = new PawnKindDef { defName = "ExpansionFixtureColonist" };
            pawn.story = new Pawn_StoryTracker(pawn); pawn.story.hairDef = oldHair;
            pawn.style = Empty<Pawn_StyleTracker>(); Set(pawn.style, "pawn", pawn); pawn.style.beardDef = oldBeard;
            var title = new RoyalTitleDef { defName = "ExpansionFixtureTitle", seniority = 2,
                throneRoomRequirements = new List<RoomRequirement> { new RoomRequirement_Area { area = 40 } } };
            DefDatabase<RoyalTitleDef>.Add(title);
            pawn.royalty = Empty<Pawn_RoyaltyTracker>(); Set(pawn.royalty, "pawn", pawn);
            Set(pawn.royalty, "titles", new List<RoyalTitle> { new RoyalTitle { def = title } }); pawn.royalty.allowRoomRequirements = true;
            for (int mask = 0; mask < 32; mask++)
            {
                for (int i = 0; i < flags.Length; i++) flags[i].SetValue(null, (mask & (1 << i)) != 0);
                Check(DevRequests.Types.Any(t => t.Key == "ThroneRoom") == ((mask & 1) != 0), "Throne Dev actions follow Royalty: " + mask);
                Check(DevRequests.Types.Any(t => t.Key == "RoyalApparel") == ((mask & 1) != 0), "Royal apparel Dev actions follow Royalty: " + mask);
                Check(DevRequests.Types.Any(t => t.Key == "HairStyle") == ((mask & 2) != 0)
                    && DevRequests.Types.Any(t => t.Key == "BeardStyle") == ((mask & 2) != 0), "Both style Dev actions follow Ideology: " + mask);
                var capture = ExpansionRequests.ThroneTerms(pawn);
                Check((capture != null) == ((mask & 1) != 0), "Title capture never enables absent Royalty: " + mask);
                var style = new PromiseRecord { Kind = PromiseKind.Style, ExpansionRequest = true, TargetHair = newHair,
                    OriginalHair = oldHair, RequiredTicks = 1, FrozenConsequences = true, CreatedTick = 100, DeadlineTick = 2000 };
                pawn.story.hairDef = newHair;
                Check(GameComponent_Promises.CreditStyle(style, pawn, 1000) == ((mask & 2) != 0), "Style credit is guarded in every DLC switch combination: " + mask);
                pawn.story.hairDef = oldHair;
            }
            flags[0].SetValue(null, true); flags[1].SetValue(null, true);
            var roomTerms = ExpansionRequests.ThroneTerms(pawn);
            ((RoomRequirement_Area)title.throneRoomRequirements[0]).area = 99;
            Check(roomTerms.BedroomRequirements.Single().Value == 40, "Later title edits do not rewrite captured room thresholds");
            title.seniority = 1;
            var removed = new PromiseRecord { Kind = PromiseKind.ThroneRoom, ExpansionRequest = true,
                SourceTitle = new RoyalTitleDef { seniority = 2 }, BedroomRequirements = roomTerms.BedroomRequirements };
            Check(!ExpansionRequests.Applicable(pawn, removed), "Demotion removes the old requirement rather than substituting terms");
            title.seniority = 3; Check(ExpansionRequests.Applicable(pawn, removed), "Promotion cannot cancel an older obligation as an escape");
            pawn.royalty.allowRoomRequirements = false;
            Check(!ExpansionRequests.Applicable(pawn, removed), "An actual exemption closes the new throne obligation without failure");
            pawn.royalty.allowRoomRequirements = true;
            var item = Item("ExpansionFixtureThrone"); var throne = new Building_Throne { def = item };
            var room = Empty<Room>(); var otherRoom = Empty<Room>();
            roomTerms.DeadlineTick = 200000; roomTerms.LastCheckTick = 0;
            var observation = new ConditionObservation { Satisfied = true, Thing = throne, Room = room };
            PromiseProgress.UpdateHold(roomTerms, observation, 0);
            for (int tick = 250; tick <= 60000; tick += 250) { PromiseProgress.UpdateHold(roomTerms, observation, tick); roomTerms.LastCheckTick = tick; }
            Check(roomTerms.ProgressTicks == 60000, "Throne room requires a full verified day");
            observation.Room = otherRoom; PromiseProgress.UpdateHold(roomTerms, observation, 60250);
            Check(roomTerms.ProgressTicks == 0, "Changing throne rooms resets the continuous day");
            var component = new GameComponent_Promises(game); var state = component.StateFor(pawn); state.Active = roomTerms;
            ((Dictionary<Pawn, ColonistPromises>)typeof(GameComponent_Promises).GetField("active", Fields).GetValue(component)).Add(pawn, state);
            roomTerms.ProgressTicks = 30000; roomTerms.StableThing = throne;
            component.InvalidateThing(throne); Check(roomTerms.ProgressTicks == 0, "Unassigning the tracked throne resets its hold");
            foreach (bool beard in new[] { false, true })
            {
                pawn.story.hairDef = oldHair; pawn.style.beardDef = oldBeard;
                var record = ExpansionRequests.StyleTerms(pawn, beard, beard ? (StyleItemDef)newBeard : newHair);
                record.FrozenConsequences = true; record.CreatedTick = 100; record.DeadlineTick = 2000;
                Check(!GameComponent_Promises.CreditStyle(record, pawn, 500), "Unchanged appearance cannot count");
                if (beard) pawn.story.hairDef = newHair; else pawn.style.beardDef = newBeard;
                Check(!GameComponent_Promises.CreditStyle(record, pawn, 500), "Changing only the other element cannot count");
                if (beard) pawn.style.beardDef = newBeard; else pawn.story.hairDef = newHair;
                Check(!GameComponent_Promises.CreditStyle(record, pawn, 99) && !GameComponent_Promises.CreditStyle(record, pawn, 2001), "Style change stays within acceptance/deadline bounds");
                Check(GameComponent_Promises.CreditStyle(record, pawn, 500) && !GameComponent_Promises.CreditStyle(record, pawn, 500), "Correct actual style change counts exactly once");
                var offer = RequestGenerator.Offer(new RequestCandidate { Terms = record }, 0);
                record.FrozenConsequences = false;
                PromiseConsequences.Snapshot(new ColonistPromises(), offer, 0);
                record.FrozenConsequences = true;
                Check(record.SuccessMood == 4 && record.SuccessTrust == 4 && offer.DeadlineDays == 4 && !offer.RecoveryEnabled,
                    "Styles use ordinary equipment rewards without unaccepted recovery");
                var closureState = new ColonistPromises();
                PromiseConsequences.Apply(closureState, record, PromiseOutcome.Closed, 1000);
                Check(closureState.Trust == 50 && record.AppliedMood == 0 && record.AppliedTrust == 0
                    && closureState.CooldownUntilTick == 1000 + record.CooldownDays * PromiseBalance.TicksPerDay,
                    "Lost applicability closes with zero effects and the fixed cooldown");
                foreach (string lang in new[] { "English", "Ukrainian" })
                {
                    LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, lang);
                    string text = PromiseCardText.Conditions(record) + PromiseText.Type(record) + PromiseCardText.Reason(offer);
                    Check(!text.Contains("CP_") && !text.Contains("{"), "Style card is localized: " + lang);
                }
                try { Scribe.saver.InitSaving(path, "save"); record.ExposeData(); Scribe.saver.FinalizeSaving(); } finally { Scribe.saver.ForceStop(); }
                var loaded = new PromiseRecord();
                try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = loaded; loaded.ExposeData(); } finally { Scribe.loader.ForceStop(); }
                Check(loaded.StyleBeard == beard && loaded.StyleChangedTick == 500 && loaded.TargetHair == record.TargetHair
                    && loaded.TargetBeard == record.TargetBeard && loaded.FrozenConsequences, "Style identity and one-time credit survive Scribe");
                Check(!GameComponent_Promises.CreditStyle(loaded, pawn, 600), "Loading cannot replay a credited change");
            }
            CheckApparelAlternatives(pawn, title, titles, path);
            File.WriteAllText(path, "<save><kind>Equipment</kind><progressTicks>12345</progressTicks><deadlineTick>90000</deadlineTick></save>");
            var legacy = new PromiseRecord();
            try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = legacy; legacy.ExposeData(); } finally { Scribe.loader.ForceStop(); }
            Check(!legacy.ExpansionRequest && legacy.SourceTitle == null && legacy.SourcePreceptId == -1 && legacy.StyleChangedTick == -1
                && legacy.ProgressTicks == 12345 && legacy.DeadlineTick == 90000, "Old promises keep safe defaults, deadline and progress");
        }
        finally
        {
            for (int i = 0; i < flags.Length; i++) flags[i].SetValue(null, oldFlags[i]);
            Current.Game = oldGame; LanguageDatabase.activeLanguage = oldLanguage; LanguageDatabase.defaultLanguage = oldDefault;
        }
        return checks;
    }
}
