using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ColonyPromises;
using Verse;

public static class DevRequestChecks
{
    static int checks;
    static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    public static int Run(string root, string path)
    {
        var prefField = typeof(Prefs).GetField("data", BindingFlags.Static | BindingFlags.NonPublic);
        var oldPrefs = prefField.GetValue(null);
        var prefs = new PrefsData { devMode = false }; prefField.SetValue(null, prefs);
        var oldActive = LanguageDatabase.activeLanguage; var oldDefault = LanguageDatabase.defaultLanguage;
        try
        {
            LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, "English");
            var component = new GameComponent_Promises(null);
            var a = Empty<Pawn>(); a.thingIDNumber = 70001;
            var b = Empty<Pawn>(); b.thingIDNumber = 70002;
            var other = Empty<Pawn>(); other.thingIDNumber = 70003;
            var pawnDef = Empty<ThingDef>(); pawnDef.defName = "DevFixtureHuman";
            a.def = b.def = other.def = pawnDef;
            var sa = component.StateFor(a); var sb = component.StateFor(b); var so = component.StateFor(other);
            sa.Trust = 27; sa.CooldownUntilTick = 12345;
            sa.History.Add(new PromiseRecord()); sa.RecentSuccesses.Add(new SuccessStamp());
            so.Deficits.Add(new DeficitObservation { Ticks = 54321 });
            var record = new PromiseRecord { Kind = PromiseKind.Leisure, DevTest = true, DevManual = true,
                RequiredTicks = 10000 };
            sa.Request = RequestGenerator.Offer(new RequestCandidate { Terms = record }, 100);
            foreach (var type in DevRequests.Types)
            {
                Check(component.CreateDevRequest(a, type) != null && sa.Request.Terms == record,
                    "Current-needs action is guarded outside Development mode: " + type.Key);
                Check(component.CreateDevRequest(a, type, DevRequests.Manual(type, a)) != null && sa.Request.Terms == record,
                    "Manual action is guarded outside Development mode: " + type.Key);
                var manual = DevRequests.Manual(type, a, type.Shared ? b : null);
                Check(manual.DevTest && manual.DevManual && manual.Kind == type.Kind && manual.RequiredTicks > 0,
                    "Registered manual variant produces persisted test terms: " + type.Key);
            }
            Check(DevRequests.Types.Select(t => t.Key).Distinct().Count() == DevRequests.Types.Count(), "No duplicate test variants");
            Check(DevRequests.Types.Where(t => t.Kind == PromiseKind.Equipment).Select(t => t.Cause)
                .SequenceEqual(EquipmentNeeds.Causes), "Equipment test variants share the generator's supported causes");
            Check(!component.ClearDevRecord(a, record) && sa.Request != null, "Cleanup has a code-level Dev guard");
            prefs.devMode = true;
            Check(DevRequests.Occupied(sa), "Open request blocks replacement");
            Check(!component.ClearDevRecord(a, new PromiseRecord()) && sa.Request != null, "Stale confirmation cannot remove a replacement record");
            Check(component.ClearDevRecord(a, record) && !DevRequests.Occupied(sa), "Confirmed cleanup releases only the selected slot");
            Check(sa.Trust == 27 && sa.CooldownUntilTick == 12345 && sa.History.Count == 1 && sa.RecentSuccesses.Count == 1
                && so.Deficits.Single().Ticks == 54321, "Cleanup leaves trust, history, repeats, existing cooldown and other observation unchanged");
            var sharedTerms = DevRequests.Manual(DevRequests.Types.First(t => t.Key == "Shared"), a, b);
            sharedTerms.SharedId = "shared-dev-fixture";
            var shared = new SharedBedroomCommitment { Id = sharedTerms.SharedId, First = a, Second = b,
                Request = RequestGenerator.Offer(new RequestCandidate { Terms = sharedTerms }, 100) };
            var list = (List<SharedBedroomCommitment>)typeof(GameComponent_Promises).GetField("sharedBedrooms", Fields).GetValue(component);
            list.Add(shared);
            typeof(GameComponent_Promises).GetMethod("AttachShared", Fields).Invoke(component, new object[] { shared, sa, sb });
            Check(ReferenceEquals(sa.Request, sb.Request) && DevRequests.Occupied(sa) && DevRequests.Occupied(sb), "Shared test occupies both slots with one request");
            Check(component.ClearDevRecord(b, sharedTerms) && sa.Request == null && sb.Request == null && list.Count == 0,
                "Cleanup from the partner releases both canonical aliases without a history result");
            Check(sa.History.Count == 1 && sb.History.Count == 0 && sa.RecentSuccesses.Count == 1, "Shared cleanup applies no personal result or repeat changes");

            var manualLeisure = DevRequests.Manual(DevRequests.Types.First(t => t.Kind == PromiseKind.Leisure), a);
            var request = RequestGenerator.Offer(new RequestCandidate { Terms = manualLeisure }, 200);
            sa.Request = request; prefs.devMode = false;
            RequestLifecycle.Accept(sa, request, 300);
            Check(sa.Active == manualLeisure && manualLeisure.DevManual && manualLeisure.FrozenConsequences
                && manualLeisure.DeadlineTick > 300 && sa.Request == null, "Manual test uses real acceptance and consequences after Dev mode is disabled");
            try { Scribe.saver.InitSaving(path, "save"); manualLeisure.ExposeData(); Scribe.saver.FinalizeSaving(); }
            finally { Scribe.saver.ForceStop(); }
            var loaded = new PromiseRecord();
            try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = loaded; loaded.ExposeData(); }
            finally { Scribe.loader.ForceStop(); }
            Check(loaded.DevTest && loaded.DevManual && loaded.FrozenConsequences && loaded.RequiredTicks == manualLeisure.RequiredTicks
                && loaded.DeadlineTick == manualLeisure.DeadlineTick, "Dev flags and real accepted terms round-trip through Scribe");
            File.WriteAllText(path, "<save><kind>Leisure</kind><requiredTicks>10000</requiredTicks></save>");
            loaded = new PromiseRecord();
            try { Scribe.loader.InitLoading(path); Scribe.loader.curParent = loaded; loaded.ExposeData(); }
            finally { Scribe.loader.ForceStop(); }
            Check(!loaded.DevTest && !loaded.DevManual, "Old records default to ordinary rules");
            foreach (string language in new[] { "English", "Ukrainian" })
            {
                LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, language);
                foreach (var type in DevRequests.Types) Check(!("CP_DevType_" + type.Key).Translate().ToString().Contains("CP_"), "Test subtype translates in " + language);
                Check(!PromiseCardText.Reason(request).Contains("CP_"), "Manual test invents no original complaint in " + language);
            }
        }
        finally { prefField.SetValue(null, oldPrefs); LanguageDatabase.activeLanguage = oldActive; LanguageDatabase.defaultLanguage = oldDefault; }
        return checks;
    }
}
