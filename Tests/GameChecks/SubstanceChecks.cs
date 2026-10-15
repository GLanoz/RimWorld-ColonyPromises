using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using ColonyPromises;
using RimWorld;
using Verse;

public static class SubstanceChecks
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static ThingDef Drug(string name, ChemicalDef chemical, HediffDef effect, DrugCategory category = DrugCategory.Social)
    {
        var def = Empty<ThingDef>(); def.defName = name; def.label = name;
        def.thingClass = typeof(ThingWithComps);
        def.comps = new List<CompProperties> { new CompProperties_Drug { chemical = chemical, needLevelOffset = .5f } };
        def.ingestible = new IngestibleProperties { drugCategory = category,
            outcomeDoers = new List<IngestionOutcomeDoer> { new IngestionOutcomeDoer_GiveHediff { hediffDef = effect, severity = .5f } } };
        return def;
    }
    public static int Run(string root, string path)
    {
        var binding = typeof(DefOfHelper).GetField("bindingNow", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        object previousBinding = binding.GetValue(null);
        try { binding.SetValue(null, true); RuntimeHelpers.RunClassConstructor(typeof(ThingDefOf).TypeHandle); }
        finally { binding.SetValue(null, previousBinding); }
        CoreDefinitions();
        Check((int)PromiseKind.Bedroom == 0 && (int)PromiseKind.Leisure == 1 && (int)PromiseKind.Equipment == 2,
            "Existing kind values remain unchanged");
        foreach (int degree in new[] { -1, 0, 1, 2 })
            foreach (int count in new[] { 0, 2, 3, 4 })
                Check(SubstanceNeeds.ExperimentTrait(degree, false, count) == (degree > 0 && count < 3),
                    "Experiment trait/addiction boundary: " + degree + "/" + count);
        Check(!SubstanceNeeds.ExperimentTrait(1, true, 0), "Suppressed chemical interest cannot create a desire");
        var chemical = new ChemicalDef { defName = "SubstanceFixtureChemical", label = "fixture chemical" };
        var effect = new HediffDef { defName = "SubstanceFixtureHigh", label = "fixture high", hediffClass = typeof(Hediff_High),
            comps = new List<HediffCompProperties> { new HediffCompProperties_SeverityPerDay { severityPerDay = -1 } } };
        var drug = Drug("SubstanceFixtureA", chemical, effect);
        var alternate = Drug("SubstanceFixtureB", chemical, effect);
        var other = Drug("SubstanceFixtureOther", new ChemicalDef { defName = "SubstanceOtherChemical" }, effect);
        DefDatabase<ChemicalDef>.Add(chemical); DefDatabase<HediffDef>.Add(effect); DefDatabase<ThingDef>.Add(drug);
        var policy = Empty<DrugPolicy>();
        var entry = new DrugPolicyEntry { drug = drug, allowedForJoy = false, allowScheduled = false, allowedForAddiction = true };
        typeof(DrugPolicy).GetField("entriesInt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(policy, new List<DrugPolicyEntry> { entry });
        Check(SubstanceNeeds.PolicyAllows(policy, drug, false) && !SubstanceNeeds.PolicyAllows(policy, drug, true),
            "Addiction permission does not authorize experimentation");
        entry.allowedForAddiction = false; entry.allowedForJoy = true;
        Check(!SubstanceNeeds.PolicyAllows(policy, drug, false) && SubstanceNeeds.PolicyAllows(policy, drug, true),
            "Joy permission does not silently change addiction policy");
        entry.allowedForJoy = false; entry.allowScheduled = true;
        Check(SubstanceNeeds.PolicyAllows(policy, drug, true), "Existing scheduled permission permits a testable target");
        Check(!SubstanceNeeds.PolicyAllows(policy, alternate, true) && !SubstanceNeeds.PolicyAllows(null, drug, true),
            "Missing policy data cannot authorize a substance");
        Check(SubstanceNeeds.Standard(drug) && SubstanceNeeds.ExperimentEffect(drug) == effect, "Standard temporary drug is recognized");
        Check(SubstanceNeeds.Key(drug) == SubstanceNeeds.Key(alternate), "Different products share chemical familiarity");
        Check(SubstanceNeeds.DoseProduct(alternate, chemical) && !SubstanceNeeds.DoseProduct(other, chemical)
            && !SubstanceNeeds.DoseProduct(drug, null), "Shared dose matching requires a real matching chemical and need restoration");
        var ledger = new ColonistPromises(); SubstanceNeeds.Remember(ledger, drug); SubstanceNeeds.Remember(ledger, drug);
        Check(ledger.KnownSubstances.Count == 2 && ledger.KnownSubstances.Contains(SubstanceNeeds.Key(alternate)),
            "Actual-use journal preserves product and category without duplicate entries");
        var readerPawn = Empty<Pawn>();
        readerPawn.health = Empty<Pawn_HealthTracker>();
        readerPawn.health.hediffSet = Empty<HediffSet>();
        readerPawn.health.hediffSet.hediffs = new List<Hediff> { new Hediff_High { def = effect } };
        var readState = new ColonistPromises();
        Check(new SubstanceKnowledge(readerPawn, readState).Known(drug), "Unambiguous active effect confirms familiarity");
        DefDatabase<ThingDef>.Add(other);
        Check(!new SubstanceKnowledge(readerPawn, readState).Known(drug), "Fresh snapshot detects an ambiguous effect after Def changes");
        readerPawn.health.hediffSet.hediffs.Clear();
        var beforeUse = new SubstanceKnowledge(readerPawn, readState);
        SubstanceNeeds.Remember(readState, drug);
        Check(!beforeUse.Known(drug) && new SubstanceKnowledge(readerPawn, readState).Known(alternate),
            "Snapshots are evaluation-local; next evaluation observes new consumption across the chemical");
        Check(readState.KnownSubstances.Count == 2 && readState.Deficits.Count == 0 && readState.SubstanceCooldownUntilTick == 0,
            "Reading familiarity changes neither journal nor observation/cooldown state");
        var medical = Drug("MedicalFixture", chemical, effect, DrugCategory.Medical);
        Check(SubstanceNeeds.ExperimentEffect(medical) == null, "Medical drug is not an experimental target");
        var permanent = new HediffDef { hediffClass = typeof(Hediff_High) };
        Check(SubstanceNeeds.ExperimentEffect(Drug("PermanentFixture", chemical, permanent)) == null,
            "Non-temporary high cannot be an experimental target");
        var food = Drug("FoodFixture", chemical, effect, DrugCategory.None);
        Check(!SubstanceNeeds.Standard(food), "An ingestible without drug category is not a substance target");
        var special = Drug("CustomFixture", chemical, effect);
        special.ingestible.outcomeDoers.Add(new IngestionOutcomeDoer_OffsetNeed());
        Check(SubstanceNeeds.Standard(special) && SubstanceNeeds.ExperimentEffect(special) == effect, "Standard auxiliary need offset retains the verifiable high");
        special.comps.Add(new AuxiliaryProperties());
        effect.comps.Add(new AuxiliaryEffectProperties());
        Check(SubstanceNeeds.ExperimentEffect(special) == effect, "Unknown auxiliary components and their assembly do not reject a standard high");
        special.ingestible.outcomeDoers.Add(new UnknownOutcome());
        Check(SubstanceNeeds.SupportReason(special, true) == "CP_SubstanceOutcome", "Unknown ingestion effect mechanism has an explicit rejection reason");
        var irreversible = Drug("ExtraPermanentFixture", chemical, effect);
        irreversible.ingestible.outcomeDoers.Add(new IngestionOutcomeDoer_GiveHediff { hediffDef = permanent, severity = 1 });
        Check(SubstanceNeeds.ExperimentEffect(irreversible) == null, "A temporary high cannot hide an additional permanent outcome");
        foreach (bool experiment in new[] { false, true })
        {
            var record = SubstanceNeeds.Terms(drug, experiment);
            Check(new SubstanceCondition().ValidParameters(record), "Subtype has structurally valid fixed terms");
            var candidate = new RequestCandidate { Terms = record, Signature = experiment ? "substance-try-v1" : "substance-addiction-v1:fixture" };
            var observation = RequestGenerator.Project(null, candidate, PromiseKind.Substance, 0, false);
            for (int tick = 2500; tick <= 120000; tick += 2500)
                observation = RequestGenerator.Project(observation, candidate, PromiseKind.Substance, tick, false);
            Check(observation.Ticks == RequestRules.PersistentTicks && RequestRules.Ready(observation.Ticks, false, 120000, 0),
                "Both subtypes require normal two-day observation");
            var paused = new RequestEvaluation { DeficitSignature = candidate.Signature, ObservationPaused = true };
            Check(RequestGenerator.ProjectEvaluation(observation, paused, PromiseKind.Substance, 122500, false).Ticks == observation.Ticks,
                "Known sleep adds no time and retains substance observation");
            var state = new ColonistPromises { Trust = 50 };
            var offer = RequestGenerator.Offer(candidate, 120000); state.Request = offer;
            Check(!SubstanceNeeds.Credit(record, drug, 120050, true), "Unaccepted requests cannot earn ingestion credit");
            Check(offer.DeadlineDays == RequestRules.SubstanceDeadlineDays && !offer.RecoveryEnabled,
                "Substance offers use their deadline without unaccepted rewards");
            RequestLifecycle.Accept(state, offer, 120100);
            Check(record.FrozenConsequences && record.SuccessMood == (experiment ? 6 : 4)
                && record.SuccessTrust == (experiment ? 7 : 4) && record.SuccessDays == (experiment ? 3 : 5),
                "Subtype snapshots use exact specified baseline rewards");
            Check(!SubstanceNeeds.Credit(record, drug, 120099, true), "Consumption before acceptance cannot count");
            Check(!SubstanceNeeds.Credit(record, other, 120200, true), "Unrelated chemistry cannot count");
            var another = new ColonistPromises { Active = SubstanceNeeds.Terms(drug, experiment) };
            Check(!SubstanceNeeds.Credit(another, record, drug, 120200, true), "Another participant cannot credit this promise");
            Check(!SubstanceNeeds.Credit(state, null, drug, 120200, true), "An ingestion started before acceptance has no captured promise");
            Check(!SubstanceNeeds.Credit(record, drug, record.DeadlineTick + 1, true), "Post-deadline consumption cannot count");
            if (experiment) Check(!SubstanceNeeds.Credit(record, drug, 120200, false), "Experiment requires a confirmed new effect");
            Check(SubstanceNeeds.Credit(record, experiment ? drug : alternate, 120200, true),
                "Experiment requires its model; addiction accepts a matching chemical product");
            Check(!SubstanceNeeds.Credit(record, drug, 120200, true) && !SubstanceNeeds.Credit(record, drug, 120300, true),
                "Repeated events cannot apply consumption twice");
            int deadline = record.DeadlineTick;
            state.Active = null; PromiseConsequences.Apply(state, record, PromiseOutcome.Fulfilled, 120200);
            Check(state.SubstanceCooldownUntilTick == 120200 + 15 * 60000 && state.RecentSuccesses.Count == 1,
                "Completion starts separate fifteen-day gate and normal repeat tracking");
            try { Scribe.saver.InitSaving(path, "save"); record.ExposeData(); Scribe.saver.FinalizeSaving(); }
            finally { Scribe.saver.ForceStop(); }
            var loaded = new PromiseRecord();
            try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = loaded; loaded.ExposeData(); }
            finally { Scribe.loader.ForceStop(); }
            Check(loaded.SubstanceDef == drug && loaded.SubstanceChemical == chemical && loaded.SubstanceExperiment == experiment
                && loaded.SubstanceConsumedTick == 120200 && loaded.DeadlineTick == deadline && loaded.FrozenConsequences,
                "Substance terms and consumption survive existing Scribe pipeline");
            Check(!SubstanceNeeds.Credit(loaded, drug, 120300, true), "Reload cannot replay a credited ingestion");
        }
        foreach (var outcome in new[] { PromiseOutcome.Declined, PromiseOutcome.Expired, PromiseOutcome.Obsolete, PromiseOutcome.Closed })
        {
            var state = new ColonistPromises(); var record = SubstanceNeeds.Terms(drug, true);
            state.Request = RequestGenerator.Offer(new RequestCandidate { Terms = record }, 0);
            RequestLifecycle.Close(state, outcome, 1000);
            Check(state.SubstanceCooldownUntilTick == 901000 && state.CooldownUntilTick == 181000,
                "Every ordinary closure starts both independent gates: " + outcome);
            Check(state.Trust == (outcome == PromiseOutcome.Declined ? 49 : 50)
                && record.AppliedMood == (outcome == PromiseOutcome.Declined ? -1 : 0), "Decline/expiry distinction survives new subtype");
            Check(RequestLifecycle.Close(state, outcome, 2000) == null && state.SubstanceCooldownUntilTick == 901000,
                "Duplicate closure adds neither consequences nor cooldown");
        }
        var lowTrust = new ColonistPromises { Trust = 0 };
        var experimentPreview = RequestGenerator.Offer(new RequestCandidate { Terms = SubstanceNeeds.Terms(drug, true) }, 100);
        PromiseConsequences.Snapshot(lowTrust, experimentPreview, 100);
        Check(experimentPreview.Terms.SuccessMood == 1.5f && experimentPreview.Terms.SuccessTrust == 7,
            "Zero-trust experimentation still has a positive reward and recovery");
        lowTrust.RecentSuccesses.Add(new SuccessStamp { Kind = PromiseKind.Substance, Tick = 100 });
        lowTrust.RecentSuccesses.Add(new SuccessStamp { Kind = PromiseKind.Substance, Tick = 100 });
        PromiseConsequences.Snapshot(lowTrust, experimentPreview, 100);
        Check(experimentPreview.Terms.SuccessMood == .38f && experimentPreview.Terms.SuccessTrust == 2,
            "Substance rewards retain combined low-trust and repeat reductions");
        var savedState = new ColonistPromises { SubstanceCooldownUntilTick = 901000, SubstanceHistoryInitialized = true,
            KnownSubstances = ledger.KnownSubstances };
        try { Scribe.saver.InitSaving(path, "save"); savedState.ExposeData(); Scribe.saver.FinalizeSaving(); }
        finally { Scribe.saver.ForceStop(); }
        var restored = new ColonistPromises();
        try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = restored; restored.ExposeData(); }
        finally { Scribe.loader.ForceStop(); }
        Check(restored.SubstanceHistoryInitialized && restored.SubstanceCooldownUntilTick == 901000
            && restored.KnownSubstances.SequenceEqual(ledger.KnownSubstances), "Journal and independent cooldown survive Scribe");
        File.WriteAllText(path, "<save><kind>Equipment</kind><progressTicks>12345</progressTicks><deadlineTick>300000</deadlineTick></save>");
        var legacy = new PromiseRecord();
        try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = legacy; legacy.ExposeData(); }
        finally { Scribe.loader.ForceStop(); }
        Check(legacy.Kind == PromiseKind.Equipment && legacy.ProgressTicks == 12345 && legacy.DeadlineTick == 300000
            && legacy.SubstanceConsumedTick == -1 && legacy.SubstanceDef == null, "Old accepted terms retain safe defaults and progress");
        var oldLanguage = LanguageDatabase.activeLanguage; var oldDefault = LanguageDatabase.defaultLanguage;
        try
        {
            foreach (string language in new[] { "English", "Ukrainian" })
            {
                LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, language);
                foreach (bool experiment in new[] { false, true })
                {
                    var terms = SubstanceNeeds.Terms(drug, experiment);
                    string text = PromiseCardText.Conditions(terms) + PromiseCardText.Reason(new ColonistRequest { Terms = terms })
                        + new SubstanceCondition().Describe(terms) + PromiseCardText.Title(terms, null);
                    Check(!text.Contains("CP_") && !text.Contains("{"), "Substance cards and details format in " + language);
                    string summary = (string)typeof(MainTabWindow_Promises).GetMethod("ShortRequirement",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { terms });
                    Check(summary.Contains(experiment ? drug.LabelCap.ToString() : chemical.LabelCap.ToString())
                        && !summary.Contains("CP_MissingParameter".Translate().ToString()) && !summary.Contains("CP_"),
                        "Requests row names substance/chemical instead of a missing equipment parameter: " + language);
                }
            }
        }
        finally { LanguageDatabase.activeLanguage = oldLanguage; LanguageDatabase.defaultLanguage = oldDefault; }
        return checks;
    }

    // Read recognition-relevant fields from installed Core XML, rather than duplicating their structure.
    // This is not the game's complete Def loader or an ingestion simulation.
    static void CoreDefinitions()
    {
        string directory = Environment.GetEnvironmentVariable("COLONY_PROMISES_GAME_DATA");
        if (string.IsNullOrEmpty(directory)) throw new Exception("Core drug XML directory was not supplied by Verify-Features.ps1");
        var nodes = Directory.GetFiles(directory, "*.xml").SelectMany(f => XDocument.Load(f).Root.Elements()).ToList();
        string gameRoot = Environment.GetEnvironmentVariable("COLONY_PROMISES_GAME_ROOT");
        string psilocapPath = Path.Combine(gameRoot, "Data", "Odyssey", "Defs", "Drugs", "Psilocap.xml");
        nodes.AddRange(XDocument.Load(psilocapPath).Root.Elements());
        var assembly = typeof(Thing).Assembly;
        Type GameType(string name) => assembly.GetTypes().Single(t => t.Name == name);
        float Number(XElement node, float fallback = 0) => node == null ? fallback : float.Parse(node.Value, CultureInfo.InvariantCulture);
        HediffDef Effect(string name)
        {
            var node = nodes.Single(n => n.Name == "HediffDef" && (string)n.Element("defName") == name);
            var result = new HediffDef { defName = name,
                hediffClass = node.Element("hediffClass") == null ? typeof(HediffWithComps) : GameType(node.Element("hediffClass").Value),
                comps = new List<HediffCompProperties>() };
            foreach (var compNode in node.Element("comps")?.Elements() ?? Enumerable.Empty<XElement>())
            {
                var comp = (HediffCompProperties)Activator.CreateInstance(GameType((string)compNode.Attribute("Class")));
                if (comp is HediffCompProperties_SeverityPerDay decay) decay.severityPerDay = Number(compNode.Element("severityPerDay"));
                result.comps.Add(comp);
            }
            return result;
        }
        foreach (string name in new[] { "Beer", "GoJuice", "WakeUp", "SmokeleafJoint", "Ambrosia", "Yayo", "Flake", "PsychiteTea", "Luciferium", "Penoxycyline", "Drug_Psilocap" })
        {
            var node = nodes.Single(n => n.Name == "ThingDef" && (string)n.Element("defName") == name);
            var chain = new List<XElement> { node };
            while (chain.Last().Attribute("ParentName") is XAttribute parent)
                chain.Add(nodes.Single(n => (string)n.Attribute("Name") == parent.Value));
            var drug = Empty<ThingDef>(); drug.defName = name; drug.label = name;
            drug.thingClass = GameType(chain.Select(n => n.Element("thingClass")).First(n => n != null).Value);
            var chemicalNode = node.Element("comps").Elements().Single(n => (string)n.Attribute("Class") == "CompProperties_Drug");
            string chemicalName = (string)chemicalNode.Element("chemical");
            ChemicalDef chemical = null;
            if (chemicalName != null)
            {
                var source = nodes.Single(n => n.Name == "ChemicalDef" && (string)n.Element("defName") == chemicalName);
                chemical = new ChemicalDef { defName = chemicalName };
                if (source.Element("toleranceHediff") != null) chemical.toleranceHediff = Effect(source.Element("toleranceHediff").Value);
            }
            drug.comps = new List<CompProperties> { new CompProperties_Drug { chemical = chemical,
                needLevelOffset = Number(chemicalNode.Element("needLevelOffset")) } };
            drug.ingestible = new IngestibleProperties { drugCategory = Enum.Parse<DrugCategory>(node.Element("ingestible").Element("drugCategory").Value),
                outcomeDoers = new List<IngestionOutcomeDoer>() };
            var effects = new Dictionary<string, HediffDef>();
            if (chemical?.toleranceHediff != null) effects.Add(chemical.toleranceHediff.defName, chemical.toleranceHediff);
            foreach (var outcomeNode in node.Element("ingestible").Element("outcomeDoers").Elements())
            {
                var outcome = (IngestionOutcomeDoer)Activator.CreateInstance(GameType((string)outcomeNode.Attribute("Class")));
                outcome.chance = Number(outcomeNode.Element("chance"), 1);
                if (outcome is IngestionOutcomeDoer_GiveHediff give)
                {
                    string effectName = outcomeNode.Element("hediffDef").Value;
                    if (!effects.ContainsKey(effectName)) effects.Add(effectName, Effect(effectName));
                    give.hediffDef = effects[effectName]; give.severity = Number(outcomeNode.Element("severity"));
                }
                drug.ingestible.outcomeDoers.Add(outcome);
            }
            bool supported = name != "Luciferium" && name != "Penoxycyline" && name != "Drug_Psilocap";
            Check((SubstanceNeeds.ExperimentEffect(drug) != null) == supported, "Installed Core XML experiment recognition: " + name);
            if (name == "Drug_Psilocap") Check(SubstanceNeeds.SupportReason(drug, true) == "CP_SubstanceOutcome",
                "Actual Odyssey psilocap retains a specific unsupported special-effect reason");
            if (supported)
            {
                var policy = Empty<DrugPolicy>();
                var entry = new DrugPolicyEntry { drug = drug };
                typeof(DrugPolicy).GetField("entriesInt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .SetValue(policy, new List<DrugPolicyEntry> { entry });
                Check(!SubstanceNeeds.PolicyAllows(policy, drug, true), "Core target support does not bypass policy: " + name);
                entry.allowedForJoy = true;
                Check(SubstanceNeeds.PolicyAllows(policy, drug, true), "Core target can be permitted independently of technical recognition: " + name);
                var savedKnowledge = new ColonistPromises();
                SubstanceNeeds.Remember(savedKnowledge, drug);
                Check(savedKnowledge.KnownSubstances.Contains(SubstanceNeeds.Key(drug)), "Core target use preserves chemical familiarity: " + name);
            }
            if (name == "GoJuice")
            {
                Check(drug.ingestible.outcomeDoers.Any(o => o is IngestionOutcomeDoer_OffsetNeed)
                    && drug.ingestible.outcomeDoers.Any(o => o is IngestionOutcomeDoer_OffsetPsyfocus), "Core GoJuice includes both auxiliary actions");
                var record = SubstanceNeeds.Terms(drug, true); record.FrozenConsequences = true; record.CreatedTick = 100; record.DeadlineTick = 1000;
                Check(!SubstanceNeeds.Credit(record, drug, 101, false), "Existing high without new effect cannot fulfill Core GoJuice terms");
                Check(SubstanceNeeds.Credit(record, drug, 102, true) && !SubstanceNeeds.Credit(record, drug, 102, true), "Core GoJuice confirmed ingestion credits only once");
            }
        }
    }
    sealed class AuxiliaryProperties : CompProperties { public AuxiliaryProperties() { compClass = typeof(ThingComp); } }
    sealed class AuxiliaryEffectProperties : HediffCompProperties { }
    sealed class UnknownOutcome : IngestionOutcomeDoer
    {
        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount) { }
    }
}
