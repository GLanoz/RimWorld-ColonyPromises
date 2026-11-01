using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Linq;
using System.Xml.Linq;
using ColonyPromises;
using RimWorld;
using UnityEngine;
using Verse;

public static class ButtonIconChecks
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    public static int Run(string root)
    {
        var definition = XDocument.Load(Path.Combine(root, "Defs", "MainButtonDefs", "Promises.xml"))
            .Root.Elements("MainButtonDef").Single(d => (string)d.Element("defName") == "CP_Promises");
        Check((bool)definition.Element("minimized"), "Promises uses the native compact slot");
        Check((string)definition.Element("iconPath") == "UI/MainButtons/Promises", "Texture uses the game's extensionless content path");
        Check(File.Exists(Path.Combine(root, "Textures", "UI", "MainButtons", "Promises.png")), "The referenced texture exists");
        Check((string)definition.Element("workerClass") == typeof(MainButtonWorker_Promises).FullName, "The existing native toggle-tab worker remains registered");
        Check(typeof(PromiseRecord).Assembly.GetType("ColonyPromises.AdaptivePromisesButtonPatch") != null,
            "A narrow native-layout mode hook is available");
        // Full game language startup supplies these regexes from Core/faction data.
        // Plain mod labels need neither date nor colonist-count patterns in this isolated host.
        var dateRegex = typeof(ColoredText).GetField("DateTimeRegexes", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var colonistRegex = typeof(ColoredText).GetField("ColonistCountRegex", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var oldDates = dateRegex.GetValue(null); var oldColonists = colonistRegex.GetValue(null);
        if (oldDates == null) dateRegex.SetValue(null, new List<Regex>());
        if (oldColonists == null) colonistRegex.SetValue(null, new Regex("(?!)"));
        ColoredText.ClearCache();
        var active = LanguageDatabase.activeLanguage; var fallback = LanguageDatabase.defaultLanguage;
        try
        {
            foreach (string language in new[] { "English", "Ukrainian" })
            {
                LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, language);
                var translated = XDocument.Load(Path.Combine(root, "Languages", language, "DefInjected", "MainButtonDef", "Promises.xml"));
                var def = new MainButtonDef { label = translated.Root.Element("CP_Promises.label").Value,
                    description = translated.Root.Element("CP_Promises.description").Value };
                int? stableId = null;
                foreach (int count in new[] { 0, 1, 9, 10, 100, 1234 })
                {
                    var tooltip = MainButtonWorker_Promises.RequestTooltip(def, count, count + 2);
                    var native = new TipSignal(def.LabelCap.Colorize(ColorLibrary.Yellow) + "\n\n" + def.description);
                    Check(tooltip.uniqueId == native.uniqueId, "Custom text updates the native tooltip rather than duplicating it");
                    Check(!stableId.HasValue || tooltip.uniqueId == stableId.Value, "Changing count preserves the hover delay ID");
                    stableId = tooltip.uniqueId;
                    Check(tooltip.text.Contains(def.LabelCap) && tooltip.text.Contains(count.ToString()), "Localized name and exact request count appear in tooltip");
                    Check(tooltip.text.Contains(language == "English" ? "Open requests:" : "\u0412\u0456\u0434\u043a\u0440\u0438\u0442\u0456 \u043f\u0440\u043e\u0445\u0430\u043d\u043d\u044f:"), "Tooltip uses the selected interface language");
                    string expectedTitle = def.LabelCap.Colorize(ColoredText.TipSectionTitleColor);
                    Check(tooltip.text.StartsWith(expectedTitle + "\n\n"), "Tooltip uses the standard section-title color and blank line");
                    Check(tooltip.text.Contains("\n\n" + def.description + "\n\n"), "Description and statistics are separate native tooltip blocks");
                    Check(tooltip.text.Contains("CP_ButtonPromiseCount".Translate((count + 2).ToString())), "Active promises have their own localized statistics row");
                    Check(tooltip.text.Contains(def.description) && !tooltip.text.Contains("{0}"), "Description is retained and numeric placeholder resolved");
                    Check(tooltip.delay == native.delay && tooltip.priority == native.priority, "Native tooltip timing and priority are preserved");
                }
            }
        }
        finally {
            LanguageDatabase.activeLanguage = active; LanguageDatabase.defaultLanguage = fallback;
            dateRegex.SetValue(null, oldDates); colonistRegex.SetValue(null, oldColonists); ColoredText.ClearCache();
        }
        foreach (float x in new[] { 0f, 13f, 1400f })
        foreach (float width in new[] { 28f, 32f, 40f, 48f, 64f, 84f, 120f })
        foreach (float countWidth in new[] { 22f, 30f, 45f })
        {
            var button = new Rect(x, 50, width, 36);
            if (MainButtonWorker_Promises.TryBadgeRect(button, countWidth, out var badge))
            {
                var icon = new Rect(button.center.x - 16 + 2, button.center.y - 16 - 2, 32, 32);
                Check(!badge.Overlaps(icon), "Visible counter cannot overlap even the hovered native icon");
                Check(badge.xMin >= button.xMin && badge.xMax <= button.xMax && badge.yMin >= button.yMin && badge.yMax <= button.yMax,
                    "Counter is confined to its own button at any panel position");
            }
            else Check(width < 2 * (countWidth + 20), "Narrow slots use a non-text indicator instead of overlapping digits");
        }
        return checks;
    }
}
