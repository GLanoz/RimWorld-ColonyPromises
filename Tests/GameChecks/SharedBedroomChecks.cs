using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ColonyPromises;
using RimWorld;
using RimWorld.Planet;
using Verse;

public static class SharedBedroomChecks
{
    static readonly BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks;
    static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static void Set(object value, string name, object field)
    {
        for (var type = value.GetType(); type != null; type = type.BaseType)
        {
            var info = type.GetField(name, fields);
            if (info != null) { info.SetValue(value, field); return; }
        }
        throw new MissingFieldException(name);
    }
    static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    static object Call(GameComponent_Promises component, string method, params object[] args) =>
        typeof(GameComponent_Promises).GetMethod(method, fields).Invoke(component, args);
    static Pawn Pawn(int id, Faction faction)
    {
        var pawn = Empty<Pawn>(); pawn.thingIDNumber = id;
        pawn.def = Empty<ThingDef>(); pawn.def.defName = "SharedFixtureHuman";
        pawn.def.race = new RaceProperties {
            intelligence = Intelligence.Humanlike, lifeStageAges = new List<LifeStageAge>() };
        pawn.Name = new NameSingle("Partner" + id);
        pawn.health = Empty<Pawn_HealthTracker>(); Set(pawn.health, "healthState", PawnHealthState.Mobile);
        pawn.ageTracker = Empty<Pawn_AgeTracker>(); Set(pawn.ageTracker, "pawn", pawn);
        pawn.needs = Empty<Pawn_NeedsTracker>(); pawn.needs.mood = Empty<Need_Mood>();
        Set(pawn.ageTracker, "ageBiologicalTicksInt", 72000000L);
        Set(pawn, "mapIndexOrState", (sbyte)-1); Set(pawn, "factionInt", faction);
        pawn.relations = new Pawn_RelationsTracker(pawn);
        return pawn;
    }
    static SharedBedroomCommitment Shared(Pawn a, Pawn b, int number) => new SharedBedroomCommitment {
        Id = "shared-bedroom-" + number, First = a, Second = b,
        Request = RequestGenerator.Offer(new RequestCandidate { Terms = new PromiseRecord {
            Kind = PromiseKind.Bedroom, HousingBased = true, SharedId = "shared-bedroom-" + number,
            ParticipantA = a, ParticipantB = b, RequiredTicks = 60000 }, Magnitude = 1 }, 1000) };
    static void Attach(GameComponent_Promises component, SharedBedroomCommitment shared)
    {
        var list = (List<SharedBedroomCommitment>)typeof(GameComponent_Promises).GetField("sharedBedrooms", fields).GetValue(component);
        list.Add(shared);
        Call(component, "AttachShared", shared, component.StateFor(shared.First), component.StateFor(shared.Second));
    }
    static Building_Bed Bed(params Pawn[] owners)
    {
        var bed = Empty<Building_Bed>(); bed.def = Empty<ThingDef>(); bed.def.defName = "SharedFixtureBed";
        bed.def.size = new IntVec2(2, 2); bed.thingIDNumber = 200;
        bed.def.building = new BuildingProperties { bed_humanlike = true };
        var comp = new CompAssignableToPawn_Bed { parent = bed };
        typeof(CompAssignableToPawn).GetField("assignedPawns", fields).SetValue(comp, owners.ToList());
        typeof(ThingWithComps).GetField("comps", fields).SetValue(bed, new List<ThingComp> { comp });
        return bed;
    }
    static Room Room(Building_Bed bed, out Region region)
    {
        var room = Empty<Room>();
        Set(room, "districts", new List<District>()); Set(room, "tmpRegions", new List<Region>());
        Set(room, "uniqueContainedThingsSet", new HashSet<Thing>()); Set(room, "uniqueContainedThings", new List<Thing>());
        var district = new District(); region = (Region)Activator.CreateInstance(typeof(Region), true);
        ((List<District>)typeof(Room).GetField("districts", fields).GetValue(room)).Add(district);
        ((List<Region>)typeof(District).GetField("regions", fields).GetValue(district)).Add(region);
        Set(room, "cachedOpenRoofCount", 0); Set(room, "statsAndRoleDirty", false);
        Set(room, "role", RoomRoleDefOf.Bedroom);
        var groups = (List<Thing>[])typeof(ListerThings).GetField("listsByGroup", fields).GetValue(region.ListerThings);
        groups[(int)ThingRequestGroup.Everything] = new List<Thing> { bed };
        return room;
    }
    public static int Run(string root, string path)
    {
        var oldGame = Current.Game; var oldActive = LanguageDatabase.activeLanguage; var oldDefault = LanguageDatabase.defaultLanguage;
        try
        {
            // Bind fixture defs without invoking the game's mod loader or Unity logging.
            typeof(DefOfHelper).GetField("bindingNow", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
            LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, "English");
            var game = Empty<Game>(); game.tickManager = Empty<TickManager>(); Set(game.tickManager, "ticksGameInt", 1000);
            game.questManager = new QuestManager();
            var world = Empty<World>(); world.factionManager = Empty<FactionManager>();
            var faction = Empty<Faction>(); faction.def = new FactionDef { isPlayer = true };
            Set(world.factionManager, "ofPlayer", faction); Set(game, "worldInt", world); Current.Game = game;
            PawnRelationDefOf.Lover = new PawnRelationDef { defName = "Lover" };
            PawnRelationDefOf.Fiance = new PawnRelationDef { defName = "Fiance" };
            PawnRelationDefOf.Spouse = new PawnRelationDef { defName = "Spouse" };
            HistoryEventDefOf.SharedBed = new HistoryEventDef();
            HistoryEventDefOf.SharedBed_Spouse = new HistoryEventDef();
            HistoryEventDefOf.SharedBed_NonSpouse = new HistoryEventDef();
            RoomRoleDefOf.Bedroom = new RoomRoleDef { defName = "Bedroom" };
            RoomRoleDefOf.Barracks = new RoomRoleDef { defName = "Barracks" };
            RoomStatDefOf.Impressiveness = new RoomStatDef { scoreStages = new List<RoomStatScoreStage> {
                new RoomStatScoreStage { label = "Fixture room" } } };
            var a = Pawn(101, faction); var b = Pawn(102, faction); var other = Pawn(103, faction);
            a.relations.DirectRelations.Add(new DirectPawnRelation(PawnRelationDefOf.Lover, b, 0));
            b.relations.DirectRelations.Add(new DirectPawnRelation(PawnRelationDefOf.Lover, a, 0));
            Check(SharedBedroomRules.PairKey(a, b) == SharedBedroomRules.PairKey(b, a), "Pair identity ignores order");
            var pairCandidate = new RequestCandidate { Terms = new PromiseRecord { Kind = PromiseKind.Bedroom,
                ParticipantA = a, ParticipantB = b }, Signature = "pair:" + SharedBedroomRules.PairKey(a, b) + ":3" };
            var evidence = RequestGenerator.Project(null, pairCandidate, PromiseKind.Bedroom, 0, false);
            for (int i = 1; i <= 48; i++) evidence = RequestGenerator.Project(evidence, pairCandidate, PromiseKind.Bedroom, i * 2500, false);
            Check(evidence.Ticks == RequestRules.PersistentTicks, "Shared candidates retain two-day sustained-deficit accounting");
            var reverseCandidate = new RequestCandidate { Terms = pairCandidate.Terms, Signature = "pair:" + SharedBedroomRules.PairKey(b, a) + ":3" };
            Check(RequestGenerator.Project(evidence, reverseCandidate, PromiseKind.Bedroom, 122500, false).StartTick == 0,
                "Participant ordering cannot restart shared evidence");
            Check(SharedBedroomRules.Validity(a, b) == SharedPairValidity.Valid, "Core lovers may share without DLC");
            Check(SharedBedroomRules.Partners(a).Single() == b, "Partner comes from actual romantic relations");
            a.relations.DirectRelations.Add(new DirectPawnRelation(PawnRelationDefOf.Spouse, other, 0));
            var evaluation = new RequestEvaluation();
            Check(SharedBedroomRules.TryEvaluate(a, evaluation, out var candidate) && candidate == null && evaluation.Reason == "CP_PairAmbiguous",
                "Ambiguous partners block individual and shared bedroom candidates");
            a.relations.DirectRelations.RemoveAt(1);
            var state = new ColonistPromises();
            Check(SharedBedroomRules.SlotFree(state, 1000), "Empty slot is available");
            state.Request = Shared(a, b, 1).Request;
            Check(!SharedBedroomRules.SlotFree(state, 1000), "Existing request blocks the partner slot");
            state.Request = null; state.Active = new PromiseRecord();
            Check(!SharedBedroomRules.SlotFree(state, 1000), "Existing promise blocks the partner slot");
            state.Active = null; state.CooldownUntilTick = 1001;
            Check(!SharedBedroomRules.SlotFree(state, 1000), "Partner cooldown blocks creation");
            state.CooldownUntilTick = 0; state.SharedId = "occupied";
            Check(!SharedBedroomRules.SlotFree(state, 1000), "Shared slot cannot be duplicated");
            var bed = Bed(a, b);
            Check(SharedBedroomRules.BedOwnersMatch(bed, a, b), "Two-slot bed has exactly the two participants");
            Check(!SharedBedroomRules.BedOwnersMatch(Bed(a, other), a, b), "Foreign bed owner disqualifies shared ownership");
            var single = Bed(a, b); single.def.size = new IntVec2(1, 2);
            Check(!SharedBedroomRules.BedOwnersMatch(single, a, b), "A one-slot bed cannot fulfill shared sleeping");
            Check(!SharedBedroomRules.BedOwnersMatch(Bed(a, b, other), a, b), "Additional owner disqualifies shared ownership");
            Set(bed, "medicalInt", true);
            Check(!SharedBedroomRules.BedOwnersMatch(bed, a, b), "Medical bed cannot fulfill a shared bedroom");
            Set(bed, "medicalInt", false);
            var room = Room(bed, out var region);
            Check(SharedBedroomRules.PrivateRoom(room, a, b), "Private bedroom allows the partner as a room owner");
            Set(room, "role", RoomRoleDefOf.Barracks);
            Check(!SharedBedroomRules.PrivateRoom(room, a, b), "Barracks cannot fulfill the shared bedroom");
            Set(room, "role", RoomRoleDefOf.Bedroom);
            var groups = (List<Thing>[])typeof(ListerThings).GetField("listsByGroup", fields).GetValue(region.ListerThings);
            var otherBed = Bed(other); otherBed.thingIDNumber = 201;
            groups[(int)ThingRequestGroup.Everything].Add(otherBed);
            Check(!SharedBedroomRules.PrivateRoom(room, a, b), "Another room resident disqualifies privacy");
            Check(RequestRules.RoomTarget(3, 3) == -1, "Already sufficient room requires no improvement");
            checks += HousingChecks.Run(game, a, b, root, path);

            var component = new GameComponent_Promises(game);
            var shared = Shared(a, b, 1); Attach(component, shared);
            var sa = component.StateFor(a); var sb = component.StateFor(b);
            Check(ReferenceEquals(sa.Shared, sb.Shared) && ReferenceEquals(sa.Request, sb.Request), "Both panels use one authoritative request");
            Check(component.OpenRequests(1000).Count == 1 && component.OpenRequests(shared.Request.ExpiryTick).Count == 0,
                "Shared request occupies one live counter/list entry and expires once");
            Check(RequestNotices.TakeAnnouncement(sa.Request) && !RequestNotices.TakeAnnouncement(sb.Request), "Both panels share a single announcement flag");
            Check(RequestNotices.TakeReminder(sa.Request, shared.Request.ExpiryTick - 30000, true)
                && !RequestNotices.TakeReminder(sb.Request, shared.Request.ExpiryTick - 30000, true), "Both panels share a single reminder flag");
            Check(!component.CreateShared(new RequestCandidate { Terms = Shared(b, a, 2).Terms }, 1000), "Reverse-order duplicate cannot replace either slot");
            sa.Trust = 0; sb.Trust = 80;
            sb.RecentSuccesses.Add(new SuccessStamp { Kind = PromiseKind.Bedroom, Tick = 900 });
            Call(component, "AcceptShared", shared, 1000);
            Check(ReferenceEquals(sa.Active, sb.Active) && shared.Request == null, "Both panels use one accepted promise");
            Check(shared.FirstEffects.SuccessMood != shared.SecondEffects.SuccessMood
                && shared.FirstEffects.SuccessTrust != shared.SecondEffects.SuccessTrust, "Personal trust and repeats produce separate snapshots");
            float mood = shared.FirstEffects.SuccessMood; sa.Trust = 10;
            Check(shared.FirstEffects.SuccessMood == mood, "Later trust cannot change the frozen snapshot");
            var record = shared.Active; record.ProgressTicks = 20000; record.StableThing = bed; record.ObservedRoom = room; record.PreviouslySatisfied = true;
            component.InvalidateThing(null);
            Check(record.ProgressTicks == 20000, "Null invalidation preserves the common hold");
            component.InvalidateThing(bed);
            Check(sa.Active.ProgressTicks == 0 && sb.Active.ProgressTicks == 0, "Real bed invalidation resets both views of the same hold");
            record.ProgressTicks = 20000; record.StableThing = bed; record.ObservedRoom = room; record.PreviouslySatisfied = true;
            component.RoomChanged(room, true);
            Check(record.ProgressTicks == 0, "Room shape changes reset the common timer");
            record.ProgressTicks = 1000;
            PromiseProgress.UpdateHold(record, new ConditionObservation(), 1250);
            Check(record.ProgressTicks == 0, "Unverified shared conditions cannot accumulate hold time");
            foreach (string language in new[] { "English", "Ukrainian" })
            {
                LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, language);
                string text = SharedPromiseText.Consequences(component, sa, record, null, 1000);
                Check(text.Contains(a.LabelShort) && text.Contains(b.LabelShort) && !text.Contains("CP_") && !text.Contains("{"),
                    "Both personal effect sets format in " + language);
                Check(SharedPromiseText.WithdrawConfirmation(sb, record).Contains(a.LabelShort), "Withdrawal names both partners in " + language);
            }
            Check((bool)Call(component, "CompleteShared", shared, PromiseOutcome.Fulfilled, 1500, false), "One common result is committed");
            int trustA = sa.Trust, trustB = sb.Trust;
            Check(!(bool)Call(component, "CompleteShared", shared, PromiseOutcome.Fulfilled, 1500, false), "Shared resolution cannot run twice");
            Check(sa.Trust == trustA && sb.Trust == trustB && sa.History.Count == 1 && sb.History.Count == 1,
                "Both personal consequences and histories apply exactly once");
            Check(sa.History[0].HistoryPartnerLabel == b.LabelShort && sb.History[0].HistoryPartnerLabel == a.LabelShort,
                "Personal histories retain the other participant");
            Check(sa.History[0].AppliedMood == shared.FirstEffects.SuccessMood && sb.History[0].AppliedMood == shared.SecondEffects.SuccessMood,
                "History stores actual personal effects");
            foreach (string language in new[] { "English", "Ukrainian" })
            {
                LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, language);
                string history = PromiseHistory.Format(sa.History[0], 1600);
                Check(history.Contains(b.LabelShort) && !history.Contains("CP_") && !history.Contains("{"), "Partner and actual history format in " + language);
            }
            sa.CooldownUntilTick = sb.CooldownUntilTick = 0;
            shared = Shared(a, b, 2); Attach(component, shared); Call(component, "AcceptShared", shared, 2000);
            a.relations.DirectRelations.Clear(); b.relations.DirectRelations.Clear();
            Check(SharedBedroomRules.Validity(a, b) == SharedPairValidity.Invalid, "Breakup invalidates a shared obligation");
            Call(component, "CompleteShared", shared, PromiseOutcome.Closed, 2100, false);
            Check(sa.Trust == trustA && sb.Trust == trustB && sa.History[0].AppliedMood == 0 && sb.History[0].AppliedTrust == 0,
                "Breakup closure penalizes neither participant");
            Set(a.health, "healthState", PawnHealthState.Dead);
            Check(SharedBedroomRules.Validity(a, b) == SharedPairValidity.Invalid, "Death invalidates the pair");
            Set(a.health, "healthState", PawnHealthState.Mobile);
            a.relations.DirectRelations.Add(new DirectPawnRelation(PawnRelationDefOf.Lover, b, 0));
            b.relations.DirectRelations.Add(new DirectPawnRelation(PawnRelationDefOf.Lover, a, 0));
            sa.CooldownUntilTick = sb.CooldownUntilTick = 0;
            shared = Shared(a, b, 3); Attach(component, shared); Call(component, "AcceptShared", shared, 2200);
            Set(b.health, "healthState", PawnHealthState.Dead);
            Check(SharedBedroomRules.Validity(a, b) == SharedPairValidity.Invalid, "Partner death invalidates an accepted obligation");
            Call(component, "CompleteShared", shared, PromiseOutcome.Closed, 2300, false);
            Check(sa.Trust == trustA && sb.Trust == trustB && sa.Active == null && sb.Active == null,
                "Partner death closes both slots without a penalty");
            Set(b.health, "healthState", PawnHealthState.Mobile);
            foreach (var outcome in new[] { PromiseOutcome.Declined, PromiseOutcome.Expired })
            {
                sa.CooldownUntilTick = sb.CooldownUntilTick = 0;
                shared = Shared(a, b, 4); Attach(component, shared);
                Call(component, "CompleteShared", shared, outcome, 2400, false);
                Check(sa.Trust == trustA && sb.Trust == trustB && sa.Request == null && sb.Request == null,
                    "Shared " + outcome + " has no personal penalty and releases both slots");
            }
            sa.Trust = sb.Trust = 0; sa.CooldownUntilTick = sb.CooldownUntilTick = 0;
            shared = Shared(a, b, 5); Attach(component, shared); shared.Terms.ProgressTicks = 60000; shared.Terms.PreviouslySatisfied = true;
            Call(component, "CompleteShared", shared, PromiseOutcome.SatisfiedUnaccepted, 2500, true);
            Check(sa.Trust == 2 && sb.Trust == 2 && sa.History[0].AppliedMood == 0 && sb.History[0].AppliedMood == 0,
                "Verified unaccepted request restores only personal low trust, without mood");
            Call(component, "CompleteShared", shared, PromiseOutcome.SatisfiedUnaccepted, 2500, true);
            Check(sa.Trust == 2 && sb.Trust == 2 && sa.RecentRecoveries.Count == 1 && sb.RecentRecoveries.Count == 1,
                "Shared recovery cannot be credited twice");
            a.relations = null;
            Check(SharedBedroomRules.Validity(a, b) == SharedPairValidity.Unknown, "Unavailable relation data is not treated as breakup");
            foreach (var outcome in new[] { PromiseOutcome.Broken, PromiseOutcome.Withdrawn })
            {
                var effectsComponent = new GameComponent_Promises(game);
                var joint = Shared(a, b, 8); Attach(effectsComponent, joint);
                var first = effectsComponent.StateFor(a); var second = effectsComponent.StateFor(b);
                first.Trust = 10; second.Trust = 90;
                Call(effectsComponent, "AcceptShared", joint, 1000);
                int firstMood = outcome == PromiseOutcome.Broken ? joint.FirstEffects.BrokenMood : joint.FirstEffects.WithdrawnMood;
                int secondMood = outcome == PromiseOutcome.Broken ? joint.SecondEffects.BrokenMood : joint.SecondEffects.WithdrawnMood;
                Call(effectsComponent, "CompleteShared", joint, outcome, 1100, false);
                Check(first.History.Single().AppliedMood == firstMood && second.History.Single().AppliedMood == secondMood,
                    outcome + " uses each frozen personal snapshot");
                Check(!(bool)Call(effectsComponent, "CompleteShared", joint, outcome, 1100, false), outcome + " cannot be applied twice");
            }
            ScribeRoundTrip(game, a, b, path, true);
            ScribeRoundTrip(game, a, b, path, false);
        }
        finally {
            typeof(DefOfHelper).GetField("bindingNow", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
            Current.Game = oldGame; LanguageDatabase.activeLanguage = oldActive; LanguageDatabase.defaultLanguage = oldDefault;
        }
        return checks;
    }

    static void ScribeRoundTrip(Game game, Pawn a, Pawn b, string path, bool accepted)
    {
        var types = new[] { typeof(SharedBedroomCommitment), typeof(HousingWitness), typeof(SuccessStamp), typeof(DeficitObservation),
            typeof(PromiseRecord), typeof(ColonistRequest), typeof(ColonistPromises) };
        typeof(GenTypes).GetField("allTypesCached", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, types.ToList());
        var cache = (IDictionary)typeof(GenTypes).GetField("typeCache", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var keyType = typeof(GenTypes).GetNestedType("TypeCacheKey", BindingFlags.NonPublic);
        foreach (var type in types) cache[Activator.CreateInstance(keyType, fields, null, new object[] { type.FullName, null }, null)] = type;
        var component = new GameComponent_Promises(game); var shared = Shared(a, b, 7);
        shared.Request.Expectation = null;
        Attach(component, shared);
        component.StateFor(a).HousingWitnesses.Add(new HousingWitness { Cause = "SleptInBedroom", Mood = -2 });
        shared.Request.HousingReasons = new List<HousingWitness> { new HousingWitness { Subject = a, Cause = "SleptInBedroom", Mood = -2 } };
        shared.Request.Announced = shared.Request.ReminderSent = true;
        if (accepted) Call(component, "AcceptShared", shared, 1000);
        shared.Terms.ProgressTicks = 12345;
        var legacy = component.StateFor(Pawn(104, a.Faction));
        legacy.Active = new PromiseRecord { Kind = PromiseKind.Leisure, ProgressTicks = 777, DeadlineTick = 99999, SuccessMood = 9, FrozenConsequences = true };
        try {
            Scribe.saver.InitSaving(path, "save"); component.ExposeData();
            // Pawn bodies belong to the outer game save, not to this component fixture.
            var checker = typeof(ScribeSaver).GetField("loadIDsErrorsChecker", fields).GetValue(Scribe.saver);
            foreach (var pawn in new[] { a, b, legacy.Pawn })
                checker.GetType().GetMethod("RegisterDeepSaved", fields).Invoke(checker, new object[] { pawn, "fixturePawns" });
            Scribe.saver.FinalizeSaving();
        }
        finally { Scribe.saver.ForceStop(); }
        string xml = File.ReadAllText(path);
        Check(xml.Contains("sharedBedrooms") && xml.Contains("shared-bedroom-7"), "Canonical record and stable ID are saved");
        var loaded = new GameComponent_Promises(game);
        try
        {
            Scribe.loader.InitLoading(path); Scribe.loader.curParent = loaded; loaded.ExposeData();
            var directory = (LoadedObjectDirectory)typeof(CrossRefHandler).GetField("loadedObjectDirectory", fields).GetValue(Scribe.loader.crossRefs);
            directory.RegisterLoaded(a);
            directory.RegisterLoaded(b);
            directory.RegisterLoaded(legacy.Pawn);
            Scribe.loader.crossRefs.ResolveAllCrossReferences();
            Scribe.mode = LoadSaveMode.PostLoadInit; loaded.ExposeData();
            var savedStates = (List<ColonistPromises>)typeof(GameComponent_Promises).GetField("colonists", fields).GetValue(loaded);
            foreach (var state in savedStates) state.ExposeData();
            var central = (List<SharedBedroomCommitment>)typeof(GameComponent_Promises).GetField("sharedBedrooms", fields).GetValue(loaded);
            Check(central.Count == 1 && central[0].First == a && central[0].Second == b && central[0].Terms.ProgressTicks == 12345,
                "Scribe resolves both participants and retains saved shared progress");
            if (accepted) Check(central[0].FirstEffects.FrozenConsequences && central[0].SecondEffects.FrozenConsequences,
                "Both frozen personal snapshots survive Scribe");
            else Check(central[0].Request.Announced && central[0].Request.ReminderSent
                && central[0].Request.HousingReasons.Single().Subject == a, "Open shared notice flags and personal housing reasons survive Scribe");
            Scribe.mode = LoadSaveMode.Inactive;
            loaded.FinalizeInit();
            Check(ReferenceEquals(loaded.StateFor(a).Shared, loaded.StateFor(b).Shared)
                && (accepted ? ReferenceEquals(loaded.StateFor(a).Active, loaded.StateFor(b).Active)
                    : ReferenceEquals(loaded.StateFor(a).Request, loaded.StateFor(b).Request)), "Loading rebuilds both views of the same record");
            Check(loaded.StateFor(legacy.Pawn).Active.ProgressTicks == 777 && loaded.StateFor(legacy.Pawn).Active.SuccessMood == 9,
                "Legacy individual progress and consequences are untouched");
            Check(accepted ? loaded.StateFor(a).HousingWitnesses.Count == 0 : loaded.StateFor(a).HousingWitnesses.Single().Mood == -2, "Loading preserves open-request evidence and cleared accepted-request evidence");
        }
        finally { Scribe.loader.ForceStop(); Scribe.mode = LoadSaveMode.Inactive; }
    }
}
