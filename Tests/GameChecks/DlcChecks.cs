using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using ColonyPromises;
using RimWorld;
using Verse;

public static class DlcChecks
{
    static int checks;
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Set(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Fields);
            if (field != null) { field.SetValue(target, value); return; }
        }
        throw new MissingFieldException(name);
    }
    public static int Run(string root, string path)
    {
        var flags = new[] { "royaltyActive", "ideologyActive", "biotechActive", "anomalyActive", "odysseyActive" }
            .Select(n => typeof(ModsConfig).GetField(n, Fields)).ToArray();
        var previous = flags.Select(f => f.GetValue(null)).ToArray();
        var bindingField = typeof(DefOfHelper).GetField("bindingNow", Fields);
        var oldBinding = bindingField.GetValue(null);
        bindingField.SetValue(null, true);
        var oldLanguage = LanguageDatabase.activeLanguage; var oldDefault = LanguageDatabase.defaultLanguage;
        try
        {
            var pawn = Empty<Pawn>(); pawn.needs = Empty<Pawn_NeedsTracker>();
            pawn.apparel = new Pawn_ApparelTracker(pawn);
            var target = Empty<ThingDef>(); target.defName = "DlcFixtureShirt"; target.apparel = new ApparelProperties();
            var room = Empty<Room>(); Set(room, "cachedCellCount", 32);
            var requirement = new RoomRequirement_Area { area = 40 };
            var snapshot = BedroomRequirement.Capture(requirement);
            var terms = new PromiseRecord { BedroomRequirements = new List<BedroomRequirement> { snapshot },
                CreatedTick = 100, DeadlineTick = 200000, ProgressTicks = 30000, SuccessMood = 6, FrozenConsequences = true };
            // Cached DLC switches test optional branches; this does not run the actual mod/Defs loader.
            for (int mask = 0; mask < 32; mask++)
            {
                for (int i = 0; i < flags.Length; i++) flags[i].SetValue(null, (mask & (1 << i)) != 0);
                Check(DlcCompatibility.CaptureBedroom(pawn, new List<BedroomRequirement>()), "Untitled pawn needs no DLC room data: " + mask);
                Check(DlcCompatibility.ApparelDefinition(pawn, target), "No ideology/title data falls back to Core apparel rules: " + mask);
                Check(!DlcCompatibility.AvoidHumanLeather(pawn), "Absent ideology does not invent a material requirement: " + mask);
                Check(LeisureNeeds.Evaluate(pawn, new RequestEvaluation()) == null, "Missing joy need never creates recreation requests: " + mask);
                Check(DlcCompatibility.BedroomMet(room, pawn, terms) == ((mask & 1) == 0), "Royal room area is gated by active Royalty: " + mask);
                Check(terms.ProgressTicks == 30000 && terms.DeadlineTick == 200000 && terms.SuccessMood == 6,
                    "DLC projection does not rewrite accepted progress or effects: " + mask);
            }
            flags[0].SetValue(null, true);
            requirement.area = 100;
            Set(room, "cachedCellCount", 40);
            Check(DlcCompatibility.BedroomMet(room, pawn, terms) && snapshot.Value == 40, "Title changes do not mutate the captured threshold");
            terms.BedroomRequirements.Add(new BedroomRequirement { Kind = "Area", Value = 48 });
            Check(!DlcCompatibility.BedroomMet(room, pawn, terms), "Shared room must satisfy both participants' captured requirements");
            Check(DlcCompatibility.BedroomMet(room, pawn, new PromiseRecord()), "Old records without snapshots retain their original conditions");
            var missing = new PromiseRecord { BedroomRequirements = new List<BedroomRequirement> {
                new BedroomRequirement { Kind = "Thing", Names = new List<string> { "MissingOptionalDlcThing" } } } };
            Check(!DlcCompatibility.BedroomMet(room, pawn, missing), "Missing optional Def is handled without rewarding an unverifiable room");
            Check(BedroomRequirement.Capture(new RoomRequirement_HasAssignedThroneAnyOf()) == null, "Unsupported room requirements cannot become partial promises");
            var floor = BedroomRequirement.Capture(new RoomRequirement_TerrainWithTags { tags = new List<string> { "FineFloor" } });
            Check(((RoomRequirement_TerrainWithTags)floor.Rebuild()).tags.Single() == "FineFloor", "Fine flooring remains a separate requirement");
            var forbidden = BedroomRequirement.Capture(new RoomRequirement_ForbiddenBuildings { buildingTags = new List<string> { "Production", "Anomaly" } });
            Check(((RoomRequirement_ForbiddenBuildings)forbidden.Rebuild()).buildingTags.SequenceEqual(new[] { "Production", "Anomaly" }),
                "Cross-DLC forbidden building tags survive capture");
            var items = new RoomRequirement_ThingAnyOf { things = new List<ThingDef> { target } };
            var itemSnapshot = BedroomRequirement.Capture(items); items.things.Clear();
            Check(itemSnapshot.Names.Single() == target.defName, "Room furniture alternatives are value snapshots rather than shared lists");
            Check(!DlcCompatibility.SameBedroomRequirements(terms, new PromiseRecord()), "Changed pending title requirements are detected by relevance");
            CheckApparel(flags, pawn, target);
            foreach (string language in new[] { "English", "Ukrainian" })
            {
                LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, language);
                string text = "CP_RoyalBedroomConditions".Translate("40 / FineFloor").ToString();
                Check(text.Contains("40 / FineFloor") && !text.Contains("CP_") && !text.Contains("{"), "Additional room conditions are localized: " + language);
                Check(!"CP_NoHumanLeather".Translate().ToString().Contains("CP_"), "Material condition is localized: " + language);
                Check(!PromiseCardText.Cause("TitleNoPersonalBedroom", new[] { NamedPawn("Noble") }).Contains("CP_"), "Royal reason identifies its subject: " + language);
            }
            terms.AvoidHumanLeather = true;
            var savedTypes = new[] { typeof(BedroomRequirement), typeof(PromiseRecord) };
            typeof(GenTypes).GetField("allTypesCached", Fields).SetValue(null, savedTypes.ToList());
            var cache = (System.Collections.IDictionary)typeof(GenTypes).GetField("typeCache", Fields).GetValue(null);
            var keyType = typeof(GenTypes).GetNestedType("TypeCacheKey", BindingFlags.NonPublic);
            foreach (var type in savedTypes)
                cache[Activator.CreateInstance(keyType, Fields, null, new object[] { type.FullName, null }, null)] = type;
            try { Scribe.saver.InitSaving(path, "save"); terms.ExposeData(); Scribe.saver.FinalizeSaving(); }
            finally { Scribe.saver.ForceStop(); }
            var loaded = new PromiseRecord();
            try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = loaded; loaded.ExposeData(); }
            finally { Scribe.loader.ForceStop(); }
            Check(loaded.BedroomRequirements.Count == 2 && loaded.BedroomRequirements[1].Value == 48 && loaded.AvoidHumanLeather,
                "DLC snapshots and material restrictions survive isolated Scribe loading");
            Check(loaded.ProgressTicks == 30000 && loaded.DeadlineTick == 200000 && loaded.SuccessMood == 6, "Scribe preserves accepted effects and progress");
            File.WriteAllText(path, "<save><kind>Bedroom</kind><progressTicks>12345</progressTicks><deadlineTick>210000</deadlineTick></save>");
            loaded = new PromiseRecord();
            try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = loaded; loaded.ExposeData(); }
            finally { Scribe.loader.ForceStop(); }
            Check(loaded.BedroomRequirements == null && !loaded.AvoidHumanLeather && loaded.ProgressTicks == 12345
                && loaded.DeadlineTick == 210000, "Absent new fields preserve legacy promises");

            string gameRoot = Environment.GetEnvironmentVariable("COLONY_PROMISES_GAME_ROOT") ?? @"C:\Games\Steam\steamapps\common\RimWorld";
            CheckActualSpecialStates(gameRoot, flags);
            foreach (string dlc in new[] { "Royalty", "Ideology", "Biotech", "Anomaly", "Odyssey" })
                Check(Directory.Exists(Path.Combine(gameRoot, "Data", dlc, "Defs")), "Local DLC Defs available for inspection: " + dlc);
            var royal = XDocument.Load(Path.Combine(gameRoot, "Data", "Royalty", "Defs", "RoyalTitles", "RoyalTitles_Empire.xml"));
            var supported = new[] { "RoomRequirement_Area", "RoomRequirement_Impressiveness", "RoomRequirement_TerrainWithTags",
                "RoomRequirement_Thing", "RoomRequirement_ThingCount", "RoomRequirement_ThingAnyOf", "RoomRequirement_ForbiddenBuildings" };
            Check(royal.Descendants("bedroomRequirements").Elements("li").All(e => supported.Contains((string)e.Attribute("Class"))),
                "Every shipped royal bedroom requirement type has a snapshot implementation");
            var metadata = XDocument.Load(Path.Combine(root, "About", "About.xml"));
            Check(metadata.Descendants("modDependencies").Descendants("packageId").Select(e => e.Value).SequenceEqual(new[] { "brrainz.harmony" }),
                "Harmony remains the sole mandatory dependency");
        }
        finally
        {
            for (int i = 0; i < flags.Length; i++) flags[i].SetValue(null, previous[i]);
            bindingField.SetValue(null, oldBinding);
            LanguageDatabase.activeLanguage = oldLanguage; LanguageDatabase.defaultLanguage = oldDefault;
        }
        return checks;
    }
    static void CheckApparel(FieldInfo[] flags, Pawn pawn, ThingDef target)
    {
        flags[0].SetValue(null, false); flags[1].SetValue(null, true);
        var torso = new BodyPartGroupDef { defName = "DlcFixtureTorso" };
        var layer = new ApparelLayerDef { defName = "DlcFixtureLayer" };
        var body = new BodyDef { defName = "DlcFixtureBody" };
        Set(body, "cachedAllParts", new List<BodyPartRecord> { new BodyPartRecord { groups = new List<BodyPartGroupDef> { torso } } });
        DefDatabase<BodyDef>.Add(body);
        body.index = (ushort)(DefDatabase<BodyDef>.DefCount - 1);
        pawn.def = Empty<ThingDef>(); pawn.def.race = new RaceProperties { body = body };
        target.apparel.bodyPartGroups = new List<BodyPartGroupDef> { torso };
        target.apparel.layers = new List<ApparelLayerDef> { layer };
        target.apparel.tags = new List<string>(); target.apparel.developmentalStageFilter = DevelopmentalStage.Adult;
        var required = Empty<ThingDef>(); required.defName = "DlcFixtureRoleShirt";
        required.apparel = new ApparelProperties { bodyPartGroups = new List<BodyPartGroupDef> { torso },
            layers = new List<ApparelLayerDef> { layer }, tags = new List<string>(), developmentalStageFilter = DevelopmentalStage.Adult };
        var interference = Enumerable.Range(0, DefDatabase<BodyDef>.DefCount).Select(i => new[] { torso }).ToArray();
        Set(target.apparel, "interferingBodyPartGroups", interference);
        Set(required.apparel, "interferingBodyPartGroups", interference);
        var worn = new Apparel { def = required };
        var owner = (ThingOwner<Apparel>)typeof(Pawn_ApparelTracker).GetField("wornApparel", Fields).GetValue(pawn.apparel);
        owner.InnerListForReading.Add(worn);
        var requirement = new ApparelRequirement { bodyPartGroupsMatchAny = new List<BodyPartGroupDef> { torso },
            requiredDefs = new List<ThingDef> { required } };
        var source = new ApparelRequirementWithSource(requirement, new Precept_RoleSingle());
        Set(pawn.apparel, "allRequirements", new List<ApparelRequirementWithSource> { source });
        Set(pawn.apparel, "allRequirementsDirty", false);
        Check(!DlcCompatibility.ApparelDefinition(pawn, target), "Replacement cannot strip the only garment meeting an active ideology role requirement");
        Check(DlcCompatibility.ApparelDefinition(pawn, required), "A fresh copy of the required role garment remains a valid replacement");
        flags[0].SetValue(null, true); flags[1].SetValue(null, false);
        pawn.royalty = Empty<Pawn_RoyaltyTracker>(); pawn.royalty.allowApparelRequirements = true;
        source = new ApparelRequirementWithSource(requirement, new RoyalTitle { def = new RoyalTitleDef() });
        Set(pawn.apparel, "allRequirements", new List<ApparelRequirementWithSource> { source });
        Check(!DlcCompatibility.ApparelDefinition(pawn, target), "Replacement cannot remove required royal clothing");
        Check(DlcCompatibility.ApparelDefinition(pawn, required), "Same-def replacement preserves royal clothing requirements");
        pawn.story = new Pawn_StoryTracker(pawn);
        pawn.kindDef = new PawnKindDef { defName = "DlcFixtureColonist" };
        Set(pawn.royalty, "pawn", pawn); pawn.royalty.allowRoomRequirements = true;
        var lower = new RoyalTitle { def = new RoyalTitleDef { seniority = 1,
            bedroomRequirements = new List<RoomRequirement> { new RoomRequirement_Area { area = 16 } } } };
        var upper = new RoyalTitle { def = new RoyalTitleDef { seniority = 2,
            bedroomRequirements = new List<RoomRequirement> { new RoomRequirement_Area { area = 32 } } } };
        Set(pawn.royalty, "titles", new List<RoyalTitle> { lower, upper });
        var captured = new List<BedroomRequirement>();
        Check(DlcCompatibility.CaptureBedroom(pawn, captured) && captured.Single().Value == 32,
            "Game title selection captures the highest applicable bedroom requirement");
        var capturedTerms = new PromiseRecord { Kind = PromiseKind.Bedroom, BedroomRequirements = captured, ProgressTicks = 30000 };
        ((RoomRequirement_Area)upper.def.bedroomRequirements[0]).area = 48;
        Check(!DlcCompatibility.PendingCompatible(pawn, capturedTerms) && captured[0].Value == 32 && capturedTerms.ProgressTicks == 30000,
            "Changed pending requirements are obsolete without rewriting accepted snapshots");
        flags[1].SetValue(null, true);
        pawn.ideo = Empty<Pawn_IdeoTracker>();
        var waivedIdeology = Empty<Ideo>(); Set(pawn.ideo, "ideo", waivedIdeology);
        var waiver = new PreceptDef { defName = "DlcFixtureSlabWaiver", prefersSlabBed = true };
        var waiverPrecept = new Precept_Apparel { def = waiver };
        Set(waivedIdeology, "precepts", new List<Precept> { waiverPrecept });
        upper.def.bedroomRequirements.Add(new RoomRequirement_ThingAnyOf { things = new List<ThingDef> { required },
            disablingPrecepts = new List<PreceptDef> { waiver } });
        var waived = new List<BedroomRequirement>();
        Check(DlcCompatibility.CaptureBedroom(pawn, waived) && waived.Count == 1,
            "Royalty plus Ideology uses the game's precept waiver instead of requiring conflicting furniture");
        Set(waivedIdeology, "precepts", new List<Precept>());
        waived.Clear();
        Check(DlcCompatibility.CaptureBedroom(pawn, waived) && waived.Count == 2,
            "Unwaived royal furniture remains required");
        pawn.ideo = null;
        var human = Empty<ThingDef>(); human.defName = "Leather_Human";
        Set(worn, "stuffInt", human);
        var record = new PromiseRecord { EquipmentDef = required, QualityOptional = true, AvoidHumanLeather = true };
        Check(!EquipmentNeeds.MatchesParameters(worn, record), "Saved material restriction rejects human leather rather than checking only apparel type");
        record.AvoidHumanLeather = false;
        Check(EquipmentNeeds.MatchesParameters(worn, record), "Old equipment promises do not acquire a new material condition");
        pawn.royalty = null;
        flags[0].SetValue(null, false); flags[1].SetValue(null, true);
        pawn.ideo = Empty<Pawn_IdeoTracker>();
        var ideology = Empty<Ideo>(); Set(pawn.ideo, "ideo", ideology);
        var nudity = new Precept_Apparel { def = new PreceptDef { prefersNudity = true, genderPrefersNudity = Gender.Female } };
        Set(ideology, "precepts", new List<Precept> { nudity });
        target.apparel.countsAsClothingForNudity = true;
        pawn.gender = Gender.Female;
        Check(!DlcCompatibility.ApparelDefinition(pawn, target), "An applicable ideological nudity preference blocks contradictory clothing");
        pawn.gender = Gender.Male;
        Check(DlcCompatibility.ApparelDefinition(pawn, target), "Gender-specific nudity preference does not apply to another colonist");
        flags[1].SetValue(null, false);
        Check(DlcCompatibility.ApparelDefinition(pawn, target), "Ideology data cannot enable restrictions with DLC inactive");
        pawn.ideo = null;
    }
    static Pawn NamedPawn(string name) { var pawn = Empty<Pawn>(); pawn.Name = new NameSingle(name); return pawn; }

    static void CheckActualSpecialStates(string gameRoot, FieldInfo[] flags)
    {
        var source = XDocument.Load(Path.Combine(gameRoot, "Data", "Anomaly", "Defs", "Misc", "Mutants.xml"));
        var ghoul = source.Root.Elements("MutantDef").Single(n => (string)n.Element("defName") == "Ghoul");
        var parent = source.Root.Elements("MutantDef").Single(n => (string)n.Attribute("Name") == (string)ghoul.Attribute("ParentName"));
        string Value(string field) => (string)(ghoul.Element(field) ?? parent.Element(field));
        var mutant = new MutantDef { defName = "Ghoul", canUseDrugs = bool.Parse(Value("canUseDrugs")),
            disablePolicies = bool.Parse(Value("disablePolicies")), disableNeeds = bool.Parse(Value("disableNeeds")),
            disableApparel = bool.Parse(Value("disableApparel")), drugWhitelist = new List<ThingDef>() };
        foreach (var name in ghoul.Element("drugWhitelist").Elements())
        { var def = Empty<ThingDef>(); def.defName = name.Value; mutant.drugWhitelist.Add(def); }
        var excluded = Empty<ThingDef>(); excluded.defName = "NotOnActualGhoulWhitelist";
        var pawn = Empty<Pawn>(); pawn.mutant = Empty<Pawn_MutantTracker>();
        Set(pawn.mutant, "pawn", pawn); Set(pawn.mutant, "def", mutant); Set(pawn.mutant, "hasTurned", true);
        var tracker = pawn.mutant;
        for (int mask = 0; mask < 32; mask++)
        {
            for (int i = 0; i < flags.Length; i++) flags[i].SetValue(null, (mask & (1 << i)) != 0);
            // Inactive configurations have no loaded mutant state; the game API reads that state directly.
            pawn.mutant = (mask & 8) == 0 ? null : tracker;
            Check(FoodUtility.DrugIsSuitable(pawn, mutant.drugWhitelist[0]), "Actual ghoul whitelist accepts its serum under switches: " + mask);
            Check(FoodUtility.DrugIsSuitable(pawn, excluded) == ((mask & 8) == 0),
                "Active Anomaly enforces actual ghoul whitelist without leaking into Core: " + mask);
            Check(mutant.disablePolicies && mutant.disableApparel && mutant.disableNeeds
                && !ghoul.Element("needWhitelist").Elements().Any(n => n.Value == "Mood" || n.Value == "Joy"),
                "Actual ghoul has no ordinary mood/joy request needs and disables policies/apparel: " + mask);
        }
        var genes = XDocument.Load(Path.Combine(gameRoot, "Data", "Biotech", "Defs", "GeneDefs", "GeneTemplateDefs.xml"));
        var dependency = genes.Root.Elements("GeneTemplateDef").Single(n => (string)n.Element("defName") == "ChemicalDependency");
        Check((string)dependency.Element("geneClass") == "Gene_ChemicalDependency"
            && (string)dependency.Element("addictionChanceFactor") == "0",
            "Actual genetic dependency is distinct from an ordinary addiction request cause");
        var swimming = XDocument.Load(Path.Combine(gameRoot, "Data", "Odyssey", "Defs", "JobDefs", "Jobs_Joy.xml"));
        Check(swimming.Root.Elements("JobDef").Any(n => (string)n.Element("driverClass") == "JobDriver_GoSwimming"
            && n.Element("joyKind") != null), "Actual swimming exposes a joy activity without a new promise type");
    }
}
