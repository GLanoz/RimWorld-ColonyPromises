using System;
using System.IO;
using System.Linq;
using System.Xml;
using ColonyPromises;
using RimWorld;
using Verse;

public static class NeedsChecks
{
    static int checks;
    static void Check(bool condition, string name) { checks++; if (!condition) throw new Exception(name); }
    public static int Run(string root, string path)
    {
        bool? Choice(int shooting, int melee, bool? equipped = null, Passion shootPassion = Passion.None,
            Passion meleePassion = Passion.None, bool brawler = false, bool violent = true,
            bool shootingDisabled = false, bool meleeDisabled = false) => EquipmentNeeds.ChooseRanged(violent,
                brawler, shootingDisabled, meleeDisabled, shooting, melee, shootPassion, meleePassion, equipped, out _);
        Check(Choice(15, 2) == true, "Shooting specialist selects ranged weapons");
        Check(Choice(2, 15) == false, "Melee specialist selects melee weapons");
        Check(Choice(8, 4, false) == true, "Exactly four overrides equipped melee");
        Check(Choice(4, 8, true) == false, "Exactly four overrides equipped ranged");
        Check(Choice(7, 4, false) == false, "Difference of three preserves melee");
        Check(Choice(4, 7, true) == true, "Difference of three preserves ranged");
        Check(Choice(5, 6, null, Passion.Major, Passion.Minor) == true, "Passion precedes close levels without equipment");
        Check(Choice(6, 5, null, Passion.Minor, Passion.Major) == false, "Melee passion precedes close levels");
        Check(Choice(6, 5) == true && Choice(5, 6) == false, "Equal passions use higher level");
        Check(Choice(0, 0) == true && Choice(0, 0, false) == false, "Zero skills are usable and ties are stable");
        Check(Choice(15, 2, null, brawler: true) == false, "Brawler overrides high Shooting");
        Check(Choice(15, 2, null, violent: false) == null, "Nonviolent pawns select no weapon");
        Check(Choice(15, 0, null, shootingDisabled: true) == false, "Real skill restriction precedes level difference");
        Check(Choice(15, 2, null, brawler: true, meleeDisabled: true) == null, "Restricted Brawler has no contradictory fallback");
        var meleeDef = (ThingDef)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        var rangedDef = (ThingDef)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        foreach (var def in new[] { meleeDef, rangedDef }) { def.category = ThingCategory.Item;
            def.tools = new System.Collections.Generic.List<Tool> { new Tool() }; }
        typeof(ThingDef).GetField("verbs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(rangedDef, new System.Collections.Generic.List<VerbProperties> { new VerbProperties { verbClass = typeof(Verse.Verb_Shoot) } });
        Check(rangedDef.IsRangedWeapon && !rangedDef.IsMeleeWeapon, "Ranged weapon with melee tools remains ranged by game API");
        Check(!new[] { meleeDef }.Any(d => EquipmentNeeds.WeaponTypeMatches(d, Choice(15, 2))), "Wrong-type-only stock cannot switch a specialist");
        Check(!EquipmentNeeds.WeaponTypeMatches(rangedDef, null), "Blocked choice has no available target");
        foreach (var quality in new[] { QualityCategory.Awful, QualityCategory.Poor })
            Check(EquipmentNeeds.UpgradeQuality(quality, 0) == QualityCategory.Normal, "Below Normal targets Normal even at level zero");
        Check(EquipmentNeeds.UpgradeQuality(QualityCategory.Normal, 9) == null, "Level nine does not request Good");
        Check(EquipmentNeeds.UpgradeQuality(QualityCategory.Normal, 10) == QualityCategory.Good, "Level ten targets Good");
        Check(EquipmentNeeds.UpgradeQuality(QualityCategory.Good, 20) == null
            && EquipmentNeeds.UpgradeQuality(QualityCategory.Legendary, 20) == null, "Good and better create no upgrade desire");
        var frozenWeapon = new PromiseRecord { Kind = PromiseKind.Equipment, EquipmentDef = rangedDef,
            MinimumQuality = QualityCategory.Good, ProgressTicks = 4000, DeadlineTick = 50000, SuccessMood = 6 };
        Choice(0, 20);
        Check(frozenWeapon.EquipmentDef == rangedDef && frozenWeapon.MinimumQuality == QualityCategory.Good
            && frozenWeapon.ProgressTicks == 4000 && frozenWeapon.DeadlineTick == 50000 && frozenWeapon.SuccessMood == 6,
            "Selecting a new type does not rewrite accepted terms");
        Check(EquipmentNeeds.AcceptsParameters(true, true, QualityCategory.Good, 1, true, frozenWeapon)
            && !EquipmentNeeds.AcceptsParameters(false, true, QualityCategory.Legendary, 1, true, frozenWeapon),
            "Upgrade fulfillment requires the same model, not a higher quality unrelated model");
        var upgradeTerms = new PromiseRecord { Kind = PromiseKind.Equipment, NeedBasedEquipment = true,
            EquipmentCause = "WeaponUpgrade", EquipmentDef = rangedDef, MinimumQuality = QualityCategory.Good,
            RequiredTicks = PromiseBalance.HoldTicks };
        var upgradeCandidate = new RequestCandidate { Terms = upgradeTerms, Signature = "weapon-upgrade-v1:fixture:3",
            ReasonKey = "CP_GearReason_WeaponUpgrade", Magnitude = 0 };
        var upgradeObservation = RequestGenerator.Project(null, upgradeCandidate, PromiseKind.Equipment, 0, false);
        for (int tick = RequestRules.ObservationInterval; tick <= RequestRules.PersistentTicks; tick += RequestRules.ObservationInterval)
        {
            upgradeObservation = RequestGenerator.Project(upgradeObservation, upgradeCandidate, PromiseKind.Equipment, tick, false);
            Check(RequestRules.Ready(upgradeObservation.Ticks, false, tick, 0) == (tick == RequestRules.PersistentTicks),
                "Weapon desire cannot bypass the two-day observation threshold");
        }
        Check(!RequestRules.Ready(upgradeObservation.Ticks, false, 120000, 120001), "Weapon desire respects individual cooldown");
        var upgradeState = new ColonistPromises();
        var upgradeRequest = RequestGenerator.Offer(upgradeCandidate, 120000);
        upgradeState.Request = upgradeRequest;
        RequestLifecycle.Accept(upgradeState, upgradeRequest, 120100);
        Check(upgradeState.Active == upgradeTerms && upgradeState.Request == null && upgradeTerms.FrozenConsequences
            && upgradeTerms.EquipmentDef == rangedDef && upgradeTerms.MinimumQuality == QualityCategory.Good,
            "Upgrade offer and acceptance retain target and freeze the standard effects");
        int acceptedDeadline = upgradeTerms.DeadlineTick;
        float acceptedMood = upgradeTerms.SuccessMood;
        var upgradeItem = (Thing)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Thing));
        PromiseProgress.UpdateHold(upgradeTerms, new ConditionObservation { Satisfied = true, Thing = upgradeItem }, 120100);
        PromiseProgress.UpdateHold(upgradeTerms, new ConditionObservation { Satisfied = true, Thing = upgradeItem }, 120350);
        Choice(0, 20);
        Check(upgradeTerms.ProgressTicks == 250 && upgradeTerms.DeadlineTick == acceptedDeadline && upgradeTerms.SuccessMood == acceptedMood,
            "Changed preference leaves the accepted physical-item hold and consequences intact");
        rangedDef.defName = "WeaponUpgradeFixture"; rangedDef.label = "fixture rifle";
        DefDatabase<ThingDef>.Add(rangedDef);
        // Serialize parameters and progress; the physical item itself is tested by the shared cross-reference fixtures.
        upgradeTerms.StableThing = null;
        try { Scribe.saver.InitSaving(path, "save"); upgradeTerms.ExposeData(); Scribe.saver.FinalizeSaving(); }
        finally { Scribe.saver.ForceStop(); }
        var savedUpgrade = new PromiseRecord();
        try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = savedUpgrade; savedUpgrade.ExposeData(); }
        finally { Scribe.loader.ForceStop(); }
        Check(savedUpgrade.EquipmentCause == "WeaponUpgrade" && savedUpgrade.EquipmentDef == rangedDef
            && savedUpgrade.MinimumQuality == QualityCategory.Good && !savedUpgrade.QualityOptional
            && savedUpgrade.ProgressTicks == 250 && savedUpgrade.DeadlineTick == acceptedDeadline
            && savedUpgrade.SuccessMood == acceptedMood, "Accepted upgrade fields survive existing Scribe keys");
        Check(!EquipmentNeeds.Confirmed("Pain", -10, true), "Unrelated negative mood is not an equipment cause");
        Check(!EquipmentNeeds.Confirmed("ApparelDamaged", 0, true), "An uncomplaining pawn does not request apparel");
        Check(!EquipmentNeeds.Confirmed("Naked", 0, true), "Nullified nudity cannot request clothing");
        Check(!EquipmentNeeds.Confirmed("BrawlerUnhappy", -10, false), "Repaired conditions override a negative effect");
        Check(!EquipmentNeeds.Confirmed("LowQuality", -1, true), "Low weapon quality is not a supported cause");
        Check(EquipmentNeeds.Confirmed("ApparelDamaged", -3, true), "Current apparel complaint starts evidence");
        Check(EquipmentNeeds.Damaged(true, true, false, .49f), "Ratty threshold matches the game worker");
        Check(!EquipmentNeeds.Damaged(true, true, false, .5f), "Apparel at 50 percent is not ratty");
        Check(!EquipmentNeeds.Damaged(true, true, true, .1f), "Locked clothing is ignored by the damage worker");
        Check(!EquipmentNeeds.Damaged(true, false, false, .1f), "Clothing that does not care about damage is ignored");
        Check(!EquipmentNeeds.CoverageSolution(true, true, true, false), "One garment must cover all missing basics");
        Check(EquipmentNeeds.CoverageSolution(true, false, true, false), "Existing torso coverage permits a legs-only solution");
        Check(!LeisureNeeds.CanObserve(true, false, false, true, false), "Sleep cannot add observed joy deficit time");
        Check(!LeisureNeeds.CanObserve(true, false, true, false, false), "Downed pawns cannot add observed deficit time");
        Check(!LeisureNeeds.CanObserve(false, false, false, false, false), "Caravans cannot add observed deficit time");
        var evaluation = new RequestEvaluation { HasDeficit = true, DeficitSignature = "equipment-need-v2:ApparelDamaged:1" };
        var evidence = RequestGenerator.ObservationCandidate(evaluation, PromiseKind.Equipment);
        var observation = RequestGenerator.Project(null, evidence, PromiseKind.Equipment, 100, false);
        observation = RequestGenerator.Project(observation, evidence, PromiseKind.Equipment, 2600, false);
        Check(observation.Ticks == 2500 && evaluation.Candidate == null, "Unavailable stock preserves need observation without a target");
        var legacy = new DeficitObservation { Signature = "gear:1:2:True", Ticks = 120000, LastTick = 100 };
        Check(RequestGenerator.Project(legacy, evidence, PromiseKind.Equipment, 2600, false).Ticks == 0,
            "Old quality-based observation is not evidence of dissatisfaction");
        var joy = new RequestCandidate { Terms = new PromiseRecord { Kind = PromiseKind.Leisure }, Signature = "joy-need-v2" };
        var shortfall = RequestGenerator.Project(null, joy, PromiseKind.Leisure, 100, false);
        shortfall = RequestGenerator.Project(shortfall, joy, PromiseKind.Leisure, 2600, false);
        Check(!RequestRules.Ready(shortfall.Ticks, false, 2600, 0), "A short recreation deficit cannot bypass two days");
        var interrupted = RequestGenerator.Project(shortfall, null, PromiseKind.Leisure, 5100, false);
        Check(RequestGenerator.Project(interrupted, joy, PromiseKind.Leisure, 7600, false).Ticks == 0,
            "An unobservable sleep sample restarts evidence without crediting sleep");
        var sleeping = new RequestEvaluation { ObservationPaused = true, HasDeficit = true, DeficitSignature = joy.Signature };
        var paused = RequestGenerator.ProjectEvaluation(shortfall, sleeping, PromiseKind.Leisure, 5100, true);
        Check(paused.Ticks == shortfall.Ticks && paused.SevereTicks == shortfall.SevereTicks && paused.ObservationPaused,
            "Known sleep pauses observed need time without erasing waking evidence");
        var awake = new RequestEvaluation { HasDeficit = true, Candidate = joy };
        var resumed = RequestGenerator.ProjectEvaluation(paused, awake, PromiseKind.Leisure, 7600, true);
        Check(resumed.Ticks == paused.Ticks && !resumed.ObservationPaused, "Waking adds no interval that included unverified sleep");
        Check(RequestGenerator.ProjectEvaluation(resumed, awake, PromiseKind.Leisure, 10100, false).Ticks == resumed.Ticks + 2500,
            "Subsequent observed waking deficit advances normally");
        Check(RequestGenerator.ProjectEvaluation(paused, awake, PromiseKind.Leisure, 20000, true).Ticks == 0,
            "An unknown long observation gap still restarts evidence");
        try { Scribe.saver.InitSaving(path, "save"); paused.ExposeData(); Scribe.saver.FinalizeSaving(); }
        finally { Scribe.saver.ForceStop(); }
        var savedPause = new DeficitObservation();
        try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = savedPause; savedPause.ExposeData(); }
        finally { Scribe.loader.ForceStop(); }
        Check(savedPause.ObservationPaused && savedPause.Ticks == paused.Ticks && savedPause.LastTick == paused.LastTick,
            "Pause state and observed time survive Scribe without adding sleep");
        var replacement = new PromiseRecord { Kind = PromiseKind.Equipment, QualityOptional = true,
            NeedBasedEquipment = true, EquipmentCause = "ApparelDamaged", CauseMood = -3, MinimumHitPointsFraction = .8f };
        Check(!EquipmentNeeds.AcceptsParameters(true, true, QualityCategory.Legendary, .4f, true, replacement),
            "Legendary worn apparel cannot satisfy an unworn replacement");
        Check(EquipmentNeeds.AcceptsParameters(true, false, QualityCategory.Awful, .8f, true, replacement),
            "No quality comp is required for a durability solution");
        Check(!EquipmentNeeds.AcceptsParameters(false, false, QualityCategory.Normal, 1, true, replacement),
            "Durable clothing of another type cannot satisfy the fixed target");
        var oldTerms = new PromiseRecord { MinimumQuality = QualityCategory.Good, MinimumHitPointsFraction = .8f };
        Check(!EquipmentNeeds.AcceptsParameters(true, true, QualityCategory.Normal, 1, true, oldTerms),
            "Legacy promises retain their quality minimum");
        Check(!EquipmentNeeds.AcceptsParameters(true, false, QualityCategory.Good, 1, true, oldTerms),
            "Legacy promises still require CompQuality");
        var hold = new PromiseRecord { Kind = PromiseKind.Equipment, RequiredTicks = PromiseBalance.HoldTicks, LastCheckTick = 0, DeadlineTick = 1000000 };
        var physicalItem = (Thing)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Thing));
        PromiseProgress.UpdateHold(hold, new ConditionObservation { Satisfied = true, Thing = physicalItem }, 0);
        PromiseProgress.UpdateHold(hold, new ConditionObservation { Satisfied = true, Thing = physicalItem }, 250);
        Check(hold.ProgressTicks == 250, "New gear uses the existing same-physical-item continuous hold");
        PromiseProgress.UpdateHold(hold, new ConditionObservation { Satisfied =
            EquipmentNeeds.AcceptsParameters(true, true, QualityCategory.Legendary, .4f, true, replacement), Thing = physicalItem }, 500);
        Check(hold.ProgressTicks == 0 && !hold.PreviouslySatisfied, "Falling below fixed durability resets continuous hold despite high quality");
        try { Scribe.saver.InitSaving(path, "save"); replacement.ExposeData(); Scribe.saver.FinalizeSaving(); }
        finally { Scribe.saver.ForceStop(); }
        var restored = new PromiseRecord();
        try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = restored; restored.ExposeData(); }
        finally { Scribe.loader.ForceStop(); }
        Check(restored.QualityOptional && restored.NeedBasedEquipment && restored.EquipmentCause == "ApparelDamaged"
            && restored.CauseMood == -3 && restored.MinimumHitPointsFraction == .8f, "New equipment fields survive Scribe");
        File.WriteAllText(path, "<save><kind>Equipment</kind><minimumQuality>Good</minimumQuality><progressTicks>30000</progressTicks><deadlineTick>200000</deadlineTick></save>");
        restored = new PromiseRecord();
        try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = restored; restored.ExposeData(); }
        finally { Scribe.loader.ForceStop(); }
        Check(!restored.QualityOptional && !restored.NeedBasedEquipment && restored.MinimumQuality == QualityCategory.Good
            && restored.ProgressTicks == 30000 && restored.DeadlineTick == 200000, "Missing new fields preserve accepted legacy promises");
        string gameRoot = Environment.GetEnvironmentVariable("COLONY_PROMISES_GAME_ROOT") ?? @"C:\Games\Steam\steamapps\common\RimWorld";
        foreach (var entry in new[] { ("Thoughts_Situation_General.xml", "ApparelDamaged", "-3"),
            ("Thoughts_Situation_Special.xml", "Naked", "-6"), ("Thoughts_Situation_Traits.xml", "BrawlerUnhappy", "-10") })
        {
            var xml = new XmlDocument(); xml.Load(Path.Combine(gameRoot, "Data", "Core", "Defs", "ThoughtDefs", entry.Item1));
            var def = xml.SelectSingleNode("Defs/ThoughtDef[defName='" + entry.Item2 + "']");
            Check(def?["stages"]?.FirstChild?["baseMoodEffect"]?.InnerText == entry.Item3, "Core defines the inspected effect: " + entry.Item2);
        }
        var oldLanguage = LanguageDatabase.activeLanguage; var oldDefault = LanguageDatabase.defaultLanguage;
        replacement.EquipmentDef = (ThingDef)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        replacement.EquipmentDef.label = "test apparel"; replacement.EquipmentDef.defName = "TestApparel";
        var request = new ColonistRequest { Terms = replacement, ReasonKey = "CP_GearReason_ApparelDamaged" };
        try {
            foreach (string language in new[] { "English", "Ukrainian" })
            {
                LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, language);
                string reason = EquipmentNeeds.Reason(request), conditions = new EquipmentCondition().Describe(replacement);
                Check(reason.Contains("−3") && reason.IndexOf("test apparel", StringComparison.OrdinalIgnoreCase) >= 0 && !reason.Contains("{") && !reason.Contains("CP_"), "Specific equipment reason formats in " + language);
                Check(conditions.Contains("80") && !conditions.Contains("CP_") && !conditions.Contains("{"), "Durability and optional quality format in " + language);
                string upgradeReason = PromiseCardText.Reason(upgradeRequest), upgradeConditions = PromiseCardText.Conditions(upgradeTerms);
                Check(!upgradeReason.Contains("CP_") && !upgradeReason.Contains("{") && !upgradeReason.Contains("0"),
                    "Personal upgrade reason invents no mood penalty in " + language);
                Check(upgradeConditions.IndexOf("fixture rifle", StringComparison.OrdinalIgnoreCase) >= 0 && upgradeConditions.Contains(QualityCategory.Good.GetLabel())
                    && !upgradeConditions.Contains("CP_"), "Upgrade card names its same model and fixed quality in " + language);
            }
        } finally { LanguageDatabase.activeLanguage = oldLanguage; LanguageDatabase.defaultLanguage = oldDefault; }
        return checks;
    }
}
