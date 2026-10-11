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

        var prefsField = typeof(Prefs).GetField("data", BindingFlags.Static | BindingFlags.NonPublic);
        var oldPrefs = prefsField.GetValue(null);
        prefsField.SetValue(null, new PrefsData { devMode = false });
        var window = (MainTabWindow_Promises)RuntimeHelpers.GetUninitializedObject(typeof(MainTabWindow_Promises));
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
        var validate = typeof(MainTabWindow_Promises).GetMethod("ValidateSelection", Instance);
        validate.Invoke(window, new object[] { new List<Pawn> { b.Pawn, c.Pawn } });
        Check(Field(window, "pawn") == b.Pawn && !(bool)Field(window, "colonistsTab")
            && ((Vector2)Field(window, "detailsScroll")).y == 90, "Ordinary validation retains valid selection, tab, and scroll");
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
                foreach (string key in new[] { "CP_TabRequests", "CP_TabColonists", "CP_NoPendingRequests", "CP_ListRequest", "CP_ListPromise", "CP_ListWaiting", "CP_ListNone", "CP_More", "CP_DeadlineCompact" })
                    Check(!key.Translate().ToString().Contains("CP_"), "UI labels available: " + folder + ": " + key);
            }
        }
        finally { LanguageDatabase.activeLanguage = oldLanguage; LanguageDatabase.defaultLanguage = oldDefault; }
        return checks;
    }
}
