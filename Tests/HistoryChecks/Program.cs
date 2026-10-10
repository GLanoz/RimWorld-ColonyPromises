using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using ColonyPromises;
using Verse;

public static class HistoryChecks
{
    static int checks;
    static readonly BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    static LoadedLanguage Language(string root, string folder)
    {
        // Supply keyed data to the actual game's translator without loading Unity assets.
        var language = (LoadedLanguage)RuntimeHelpers.GetUninitializedObject(typeof(LoadedLanguage));
        language.folderName = folder;
        typeof(LoadedLanguage).GetField("dataIsLoaded", fields).SetValue(language, true);
        typeof(LoadedLanguage).GetField("workerInt", fields).SetValue(language,
            folder == "English" ? (LanguageWorker)new LanguageWorker_English() : new LanguageWorker_Default());
        var keyedField = typeof(LoadedLanguage).GetField("keyedReplacements", fields);
        var dictionary = (IDictionary)Activator.CreateInstance(keyedField.FieldType);
        var replacementType = keyedField.FieldType.GetGenericArguments()[1];
        var xml = new XmlDocument();
        xml.Load(Path.Combine(root, "Languages", folder, "Keyed", "Promises.xml"));
        foreach (XmlNode node in xml.DocumentElement.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element) continue;
            AddKey(dictionary, replacementType, node.Name, node.InnerText);
        }
        AddKey(dictionary, replacementType, "CP_RegressionHistory",
            "{0}: {1}, {2:0.00} days ago. Mood {3:+0.##;-0.##;0}; trust {4:+0;-0;0}.");
        keyedField.SetValue(language, dictionary);
        return language;
    }
    static void AddKey(IDictionary dictionary, Type type, string key, string value)
    {
        var replacement = Activator.CreateInstance(type, true);
        type.GetField("key", fields).SetValue(replacement, key);
        type.GetField("value", fields).SetValue(replacement, value);
        dictionary[key] = replacement;
    }
    static PromiseRecord Load(string path)
    {
        var record = new PromiseRecord();
        try {
            Scribe.loader.InitLoading(path);
            Scribe.loader.curParent = record;
            record.ExposeData();
        } finally { Scribe.loader.ForceStop(); }
        return record;
    }
    static PromiseRecord RoundTrip(PromiseRecord record, string path)
    {
        try {
            Scribe.saver.InitSaving(path, "save");
            record.ExposeData();
            Scribe.saver.FinalizeSaving();
        } finally { Scribe.saver.ForceStop(); }
        return Load(path);
    }
    public static int Run(string root, string temporaryFile)
    {
        var oldActive = LanguageDatabase.activeLanguage;
        var oldDefault = LanguageDatabase.defaultLanguage;
        var oldCulture = CultureInfo.CurrentCulture;
        try {
            foreach (string folder in new[] { "English", "Ukrainian" })
            {
                LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = Language(root, folder);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(folder == "English" ? "uk-UA" : "en-US");
                string old = "CP_RegressionHistory".Translate("bedroom", "withdrawn", 0.5f, -3f, -4).ToString();
                Check(old.Contains("days ago. Mood ; trust .") && !old.Contains("0.5") && !old.Contains("-3") && !old.Contains("-4"),
                    folder + ": reproduce the original missing-number bug with the real translator: [" + old + "]");
                var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
                foreach (PromiseOutcome outcome in Enum.GetValues(typeof(PromiseOutcome)))
                {
                    var state = new ColonistPromises { Pawn = pawn };
                    var record = new PromiseRecord { Kind = PromiseKind.Bedroom, Outcome = outcome };
                    record.RecordResolution(30000);
                    PromiseConsequences.Apply(state, record, outcome, 30000);
                    string row = PromiseHistory.Format(record, 60000);
                    Check(row.Contains(folder == "English" ? "0.5 days ago" : "0,5 дня тому"), folder + ": elapsed time for " + outcome + ": " + row);
                    Check(row.IndexOf(("CP_Outcome_" + outcome).Translate().ToString(), StringComparison.OrdinalIgnoreCase) >= 0, folder + ": result label for " + outcome);
                    Check(!row.Contains("{"), folder + ": no unresolved placeholders for " + outcome);
                    if (outcome == PromiseOutcome.Fulfilled)
                        Check(row.Contains("+4") && row.Contains("+5"), folder + ": actual success effects");
                    else if (outcome == PromiseOutcome.Broken)
                        Check(row.Contains("\u22128") && row.Contains("\u221210"), folder + ": actual failure effects");
                    else if (outcome == PromiseOutcome.Withdrawn)
                        Check(row.Contains("\u22123") && row.Contains("\u22124"), folder + ": actual withdrawal effects");
                    else
                        Check(row.Contains("CP_HistoryNoEffects".Translate().ToString()), folder + ": clear penalty-free result");
                    if (outcome == PromiseOutcome.Withdrawn) Console.WriteLine(folder + " sample: " + row);
                    var loaded = RoundTrip(record, temporaryFile);
                    Check(loaded.ResolvedTick == 30000 && loaded.AppliedMood == record.AppliedMood && loaded.AppliedTrust == record.AppliedTrust,
                        folder + ": Scribe preserves applied results for " + outcome);
                    Check(loaded.HasResolvedTick && loaded.HasAppliedMood && loaded.HasAppliedTrust,
                        folder + ": known zero values stay known for " + outcome);
                    Check(PromiseHistory.Format(loaded, 60000) == row, folder + ": same history after scalar Scribe round-trip for " + outcome);
                    state.Trust = 0;
                    loaded.SuccessMood = 99;
                    loaded.BrokenMood = -99;
                    loaded.WithdrawnMood = -99;
                    Check(PromiseHistory.Format(loaded, 60000) == row, folder + ": history never recomputes effects from current state or stored forecasts");
                }

                File.WriteAllText(temporaryFile, "<save><kind>Bedroom</kind><outcome>Withdrawn</outcome><resolvedTick>30000</resolvedTick><appliedMood>-3</appliedMood><appliedTrust>-4</appliedTrust></save>");
                var legacy = Load(temporaryFile);
                Check(legacy.HasResolvedTick && legacy.HasAppliedMood && legacy.HasAppliedTrust, folder + ": preserve complete old history");
                string legacyRow = PromiseHistory.Format(legacy, 60000);
                Check(legacyRow.Contains("\u22123") && legacyRow.Contains("\u22124"), folder + ": display actual old values");
                Check(RoundTrip(legacy, temporaryFile).HasAppliedTrust, folder + ": complete legacy history remains available after another save");

                File.WriteAllText(temporaryFile, "<save><outcome>Fulfilled</outcome><resolvedTick>0</resolvedTick><appliedMood>0</appliedMood><appliedTrust>0</appliedTrust></save>");
                legacy = Load(temporaryFile);
                Check(legacy.HasResolvedTick && legacy.HasAppliedMood && legacy.HasAppliedTrust && legacy.AppliedMood == 0 && legacy.AppliedTrust == 0,
                    folder + ": explicitly stored old zero values are known");
                Check(RoundTrip(legacy, temporaryFile).HasAppliedMood, folder + ": known old zero survives a new save that omits numeric defaults");

                File.WriteAllText(temporaryFile, "<save><outcome>Withdrawn</outcome><appliedMood>-3</appliedMood></save>");
                legacy = Load(temporaryFile);
                Check(!legacy.HasResolvedTick && legacy.HasAppliedMood && !legacy.HasAppliedTrust, folder + ": distinguish absent old fields");
                legacyRow = PromiseHistory.Format(legacy, 60000);
                Check(legacyRow.Contains("\u22123") && legacyRow.IndexOf("CP_HistoryTimeUnavailable".Translate().ToString(), StringComparison.OrdinalIgnoreCase) >= 0
                    && legacyRow.IndexOf("CP_HistoryValueUnavailable".Translate().ToString(), StringComparison.OrdinalIgnoreCase) >= 0,
                    folder + ": missing data is visible without inventing zero: " + legacyRow);
                legacy = RoundTrip(legacy, temporaryFile);
                Check(!legacy.HasResolvedTick && !legacy.HasAppliedTrust && PromiseHistory.Format(legacy, 60000) == legacyRow,
                    folder + ": missing-data state survives a new save");

                File.WriteAllText(temporaryFile, "<save><outcome>Closed</outcome><resolvedTick>30000</resolvedTick></save>");
                legacy = Load(temporaryFile);
                Check(PromiseHistory.Format(legacy, 60000).Contains("CP_HistoryNoEffects".Translate().ToString()), folder + ": old penalty-free closure remains clear");

                var cappedState = new ColonistPromises { Pawn = pawn, Trust = 100 };
                var capped = new PromiseRecord { Kind = PromiseKind.Leisure, Outcome = PromiseOutcome.Fulfilled };
                capped.RecordResolution(0);
                PromiseConsequences.Apply(cappedState, capped, capped.Outcome, 0);
                capped = RoundTrip(capped, temporaryFile);
                string cappedRow = PromiseHistory.Format(capped, 0);
                Check(capped.HasResolvedTick && capped.HasAppliedTrust && capped.AppliedTrust == 0, folder + ": genuine tick-zero resolution and clamped zero trust survive omission of default numeric tags");
                Check(cappedRow.Contains(folder == "English" ? "trust: 0" : "довіра: 0"), folder + ": show actual clamped zero trust");

                var fractional = new PromiseRecord { Outcome = PromiseOutcome.Fulfilled };
                fractional.RecordResolution(30000);
                fractional.RecordConsequences(1.75f, 1);
                Check(PromiseHistory.Format(fractional, 60000).Contains(folder == "English" ? "+1.75" : "+1,75"), folder + ": signed fractional mood uses interface culture");
                foreach (int days in new[] { 0, 1, 2, 5, 11, 12, 14, 21, 22, 25 })
                {
                    fractional.RecordResolution(0);
                    string word = folder == "English" ? (days == 1 ? "day" : "days")
                        : days == 1 || days == 21 ? "день" : days == 2 || days == 22 ? "дні" : "днів";
                    string period = days + " " + word + (folder == "English" ? " ago" : " тому");
                    Check(PromiseHistory.Format(fractional, days * 60000).Contains(period), folder + ": localized day forms for " + days);
                }
                Console.WriteLine(folder + " missing-data sample: " + legacyRow);
            }
        } finally {
            LanguageDatabase.activeLanguage = oldActive;
            LanguageDatabase.defaultLanguage = oldDefault;
            CultureInfo.CurrentCulture = oldCulture;
        }
        return checks;
    }
}
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Pass the absolute mod root.");
        string temporaryFile = Path.GetTempFileName();
        try {
            int count = HistoryChecks.Run(args[0], temporaryFile);
            Console.WriteLine("Passed " + count + " bilingual history, applied-result, and isolated Scribe field checks. Unity UI and full save loading remain unverified.");
            return 0;
        } catch (Exception exception) {
            Console.Error.WriteLine(exception);
            return 1;
        } finally { File.Delete(temporaryFile); }
    }
}
