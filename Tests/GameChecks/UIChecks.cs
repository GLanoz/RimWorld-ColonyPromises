using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ColonyPromises;
using UnityEngine;
using Verse;

public static class UIChecks
{
    static int checks;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static object Call(string name, params object[] args) => typeof(MainTabWindow_Promises).GetMethod(name, Static).Invoke(null, args);
    static object Field(MainTabWindow_Promises window, string name) => typeof(MainTabWindow_Promises).GetField(name, Instance).GetValue(window);
    static void Set(MainTabWindow_Promises window, string name, object value) => typeof(MainTabWindow_Promises).GetField(name, Instance).SetValue(window, value);
    static List<(string Heading, List<Pawn> Members)> Groups(IEnumerable<Pawn> pawns, Func<Pawn, ColonistPromises> stateFor, bool byName) =>
        ((System.Collections.IEnumerable)Call("GroupColonists", pawns, stateFor, byName)).Cast<object>()
            .Select(g => ((string)g.GetType().GetField("HeadingKey").GetValue(g),
                (List<Pawn>)g.GetType().GetField("Members").GetValue(g))).ToList();
    static Pawn Pawn(int id)
    {
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.thingIDNumber = id;
        pawn.def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        pawn.def.defName = "UICheckPawn";
        pawn.health = (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
        typeof(Pawn_HealthTracker).GetField("healthState", Instance).SetValue(pawn.health, PawnHealthState.Mobile);
        return pawn;
    }
    public static int Run(string root)
    {
        foreach (var screen in new[] { new Vector2(1920, 1080), new Vector2(1280, 720), new Vector2(853, 480), new Vector2(640, 360) })
        {
            var size = (Vector2)Call("FitSize", screen.x, screen.y);
            Check(size.x > 0 && size.y > 0 && size.x <= screen.x - 32 && size.y <= screen.y - 90,
                "Window fits logical screen after UI scaling");
        }
        var a = new ColonistPromises { Pawn = Pawn(2), Request = new ColonistRequest { ExpiryTick = 200 } };
        var b = new ColonistPromises { Pawn = Pawn(3), Request = new ColonistRequest { ExpiryTick = 100 } };
        var c = new ColonistPromises { Pawn = Pawn(1), Request = new ColonistRequest { ExpiryTick = 100 } };
        var input = new[] { a, b, c };
        var sorted = ((IEnumerable<ColonistPromises>)Call("ByExpiry", (object)input)).ToList();
        Check(sorted.SequenceEqual(new[] { c, b, a }) && input[0] == a, "Request order prioritizes expiry without mutating the input");
        a.CooldownUntilTick = 100;
        Check((string)Call("StatusKey", a, 50, false) == "CP_ListWaiting", "Cooldown is distinct from no request");
        Check((string)Call("StatusKey", a, 100, false) == "CP_ListNone", "Stale request does not give a pending status");
        Check((string)Call("StatusKey", a, 50, true) == "CP_ListRequest", "Visible request has its own status");
        a.Active = new PromiseRecord();
        Check((string)Call("StatusKey", a, 50, false) == "CP_ListPromise", "Active promise has its own status");
        a.Pawn.Name = new NameSingle("Iceman"); b.Pawn.Name = new NameSingle("Miyazaki"); c.Pawn.Name = new NameSingle("Alex");
        var d = new ColonistPromises { Pawn = Pawn(4), Active = a.Active }; d.Pawn.Name = new NameSingle("Zoe");
        var shared = new SharedBedroomCommitment { First = a.Pawn, Second = d.Pawn, Active = a.Active };
        a.Shared = d.Shared = shared;
        var states = new[] { a, b, c, d }.ToDictionary(s => s.Pawn);
        Func<Pawn, ColonistPromises> stateFor = p => states[p];
        var pawns = new[] { b.Pawn, d.Pawn, c.Pawn, a.Pawn };
        var groups = Groups(pawns, stateFor, false);
        Check(groups.Count == 2 && groups[0].Heading == "CP_GroupActivePromise"
            && groups[0].Members.SequenceEqual(new[] { a.Pawn, d.Pawn }) && groups[1].Members.SequenceEqual(new[] { c.Pawn, b.Pawn }),
            "Both accepted shared participants lead; pending requests belong to the other alphabetic group");
        Check(pawns[0] == b.Pawn, "Grouping does not mutate discovery order");
        var alphabetical = Groups(pawns, stateFor, true);
        Check(alphabetical.Count == 1 && alphabetical[0].Heading == null
            && alphabetical[0].Members.SequenceEqual(new[] { c.Pawn, a.Pawn, b.Pawn, d.Pawn }), "By-name mode ignores promise state");
        a.Active = d.Active = null; shared.Active = null;
        Check(Groups(pawns, stateFor, false).Single().Heading == "CP_GroupNoPromise", "Terminal promises disappear from the active group");
        b.Active = new PromiseRecord();
        Check(Groups(pawns, stateFor, false)[0].Members.Single() == b.Pawn, "Acceptance updates group membership on the next projection");
        Check(Groups(Array.Empty<Pawn>(), stateFor, false).Count == 0, "Empty groups are hidden");
        Check(SharedPromiseText.PartnerFor(shared, a.Pawn) == d.Pawn && SharedPromiseText.PartnerFor(shared, d.Pawn) == a.Pawn
            && SharedPromiseText.PartnerFor(shared, b.Pawn) == null, "Partner navigation follows the selected participant, never an unrelated pawn");

        var prefsField = typeof(Prefs).GetField("data", BindingFlags.Static | BindingFlags.NonPublic);
        var oldPrefs = prefsField.GetValue(null);
        prefsField.SetValue(null, new PrefsData { devMode = false });
        var window = (MainTabWindow_Promises)RuntimeHelpers.GetUninitializedObject(typeof(MainTabWindow_Promises));
        Check(!(bool)Field(window, "sortByName"), "Active-promises-first is the default sorting mode");
        window.SelectPawn(a.Pawn);
        Check((bool)Field(window, "colonistsTab") && Field(window, "pawn") == a.Pawn, "Notification/row selection opens colonist details");
        Set(window, "detailsScroll", new Vector2(0, 80));
        window.SelectPawn(a.Pawn);
        Check(((Vector2)Field(window, "detailsScroll")).y == 0, "Explicit navigation shows details from the top even for the same pawn");
        Set(window, "requestsScroll", new Vector2(0, 30));
        Set(window, "colonistsScroll", new Vector2(0, 40));
        window.SelectPawn(b.Pawn);
        Check(((Vector2)Field(window, "detailsScroll")).y == 0
            && ((Vector2)Field(window, "requestsScroll")).y == 30 && ((Vector2)Field(window, "colonistsScroll")).y == 40,
            "Changing pawn resets details independently from both lists");
        Check(!(bool)Field(window, "diagnosticsExpanded"), "Diagnostics default collapsed");
        Set(window, "colonistsTab", false);
        Set(window, "detailsScroll", new Vector2(0, 90));
        Set(window, "sortByName", true);
        var validate = typeof(MainTabWindow_Promises).GetMethod("ValidateSelection", Instance);
        validate.Invoke(window, new object[] { new List<Pawn> { b.Pawn, c.Pawn } });
        Check(Field(window, "pawn") == b.Pawn && !(bool)Field(window, "colonistsTab")
            && ((Vector2)Field(window, "detailsScroll")).y == 90, "Ordinary validation retains valid selection, tab, and scroll");
        Check((bool)Field(window, "sortByName") && ((Vector2)Field(window, "colonistsScroll")).y == 40,
            "Validation and regrouping retain the session sort mode and list scroll");
        typeof(Pawn_HealthTracker).GetField("healthState", Instance).SetValue(b.Pawn.health, PawnHealthState.Dead);
        validate.Invoke(window, new object[] { new List<Pawn> { c.Pawn } });
        Check(Field(window, "pawn") == c.Pawn && !(bool)Field(window, "colonistsTab"), "Missing/dead selection falls back without switching tabs");
        validate.Invoke(window, new object[] { new List<Pawn>() });
        Check(Field(window, "pawn") == null, "Empty colony clears the selection");
        prefsField.SetValue(null, oldPrefs);

        var oldLanguage = LanguageDatabase.activeLanguage;
        var oldDefault = LanguageDatabase.defaultLanguage;
        try
        {
            foreach (string folder in new[] { "English", "Ukrainian" })
            {
                LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, folder);
                var record = new PromiseRecord { Kind = PromiseKind.Leisure, RequiredTicks = 11250 };
                string requirement = (string)Call("ShortRequirement", record);
                Check(requirement.Contains(folder == "English" ? "4.5" : "4,5"), "Summary uses localized fractional hours: " + folder);
                string longName = new string('W', 120);
                string row = "CP_RequestSummaryRow".Translate(longName, "type", requirement, "0.5").ToString();
                Check(row.Contains(longName) && row.Contains(requirement) && row.Contains("0.5") && !row.Contains("CP_"),
                    "Long names and all request summary fields survive translation: " + folder);
                foreach (string key in new[] { "CP_TabRequests", "CP_TabColonists", "CP_NoPendingRequests", "CP_ListRequest", "CP_ListPromise", "CP_ListWaiting", "CP_ListNone", "CP_More", "CP_DeadlineCompact", "CP_SortName", "CP_SortPromises" })
                    Check(!key.Translate().ToString().Contains("CP_"), "UI labels available: " + folder + ": " + key);
                Check("CP_GroupActivePromise".Translate(2).ToString().Contains("2")
                    && "CP_GroupNoPromise".Translate(3).ToString().Contains("3"), "Group headers display pawn counts: " + folder);
                var terms = new PromiseRecord { Kind = PromiseKind.Bedroom, ParticipantA = a.Pawn, ParticipantB = b.Pawn, RoomStage = 3 };
                var request = new ColonistRequest { Terms = terms, HousingReasons = new List<HousingWitness> {
                    new HousingWitness { Subject = a.Pawn, Cause = "WantToSleepWithSpouseOrLover", Mood = -4 } } };
                Func<IEnumerable<HousingIssue>, string> reason = issues => (string)typeof(SharedPromiseText)
                    .GetMethod("FormatRequestReason", Static).Invoke(null, new object[] { request, issues });
                string single = reason(Array.Empty<HousingIssue>());
                Check(single.Contains("Iceman") && !single.Contains("Miyazaki") && !single.Contains("4") && !single.Contains("CP_")
                    && !single.Contains("{"), "Shared reason names only the actual complainant, without thought penalties: " + folder);
                var extra = new HousingIssue { Pawn = b.Pawn, Cause = "SleptInBedroom", Mood = -2, Current = true, Supported = true, MinimumStage = 1 };
                string both = reason(new[] { extra });
                Check(both.Contains("Iceman") && both.Contains("Miyazaki") && !both.Contains("2") && !both.Contains("4")
                    && !both.Contains("CP_"), "Different actual concerns receive short personal explanations: " + folder);
                Check(request.HousingReasons.Count == 1 && request.HousingReasons[0].Subject == a.Pawn,
                    "Shared display never replaces the authoritative causal anchor: " + folder);
                extra.Current = false;
                Check(reason(new[] { extra }) == single, "Stale current concerns are not shown: " + folder);
                request.HousingReasons.Add(new HousingWitness { Subject = b.Pawn, Cause = "WantToSleepWithSpouseOrLover", Mood = -4 });
                Check(reason(Array.Empty<HousingIssue>()) == PromiseCardText.Cause("WantToSleepWithSpouseOrLover", new[] { a.Pawn, b.Pawn }),
                    "Matching concerns are merged into one natural sentence: " + folder);
                request.HousingReasons[1].Cause = "SleptInBedroom"; request.HousingReasons[1].Mood = -2;
                Check(reason(Array.Empty<HousingIssue>()).Contains("Miyazaki") && !reason(Array.Empty<HousingIssue>()).Contains("2"),
                    "Different reasons identify the affected partner without showing source penalties: " + folder);
                request.HousingReasons = null;
                Check(reason(Array.Empty<HousingIssue>()).Contains("CP_CardReasonUnknown".Translate().ToString()), "Legacy records use neutral unavailable-data text: " + folder);
            }
        }
        finally { LanguageDatabase.activeLanguage = oldLanguage; LanguageDatabase.defaultLanguage = oldDefault; }
        return checks;
    }
}
