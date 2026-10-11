using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ColonyPromises;
using RimWorld;
using Verse;

public static class CardChecks
{
    static int checks;
    static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    public static int Run(string root)
    {
        var oldLanguage = LanguageDatabase.activeLanguage;
        var oldDefault = LanguageDatabase.defaultLanguage;
        try
        {
            foreach (string language in new[] { "English", "Ukrainian" })
            {
                LanguageDatabase.activeLanguage = LanguageDatabase.defaultLanguage = HistoryChecks.Language(root, language);
                string fraction = language == "English" ? "4.5" : "4,5";
                var leisure = new PromiseRecord { Kind = PromiseKind.Leisure, RequiredTicks = 11250,
                    SuccessMood = 0.19f, SuccessDays = 5, SuccessTrust = 2, BrokenMood = -8, BrokenDays = 7,
                    BrokenTrust = -9, WithdrawnMood = -3, WithdrawnDays = 3, WithdrawnTrust = -4, CooldownDays = 2 };
                Check(PromiseCardText.Conditions(leisure).Contains(fraction), "Card preserves fractional recreation hours: " + language);
                string reward = PromiseCardText.Reward(leisure);
                Check(reward.Contains(language == "English" ? "+0.19" : "+0,19") && reward.Contains("5") && reward.Contains("+2"),
                    "Card shows exact saved reward and duration: " + language);
                string penalties = PromiseCardText.Penalties(leisure);
                Check(penalties.Contains("−8") && penalties.Contains("−9") && penalties.Contains("−3") && penalties.Contains("−4"),
                    "Details retain both exact penalty sets: " + language);
                string full = PromiseText.Consequences(leisure);
                Check(full.Contains("+2") && full.Contains("−8") && full.Contains("−3"), "Confirmation retains reward and penalties: " + language);
                var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
                def.defName = "CardCheckApparel"; def.label = "test clothing";
                def.apparel = new ApparelProperties();
                var gear = new PromiseRecord { Kind = PromiseKind.Equipment, EquipmentDef = def, RequiredTicks = 60000,
                    MinimumQuality = QualityCategory.Good, MinimumHitPointsFraction = 0.8f };
                string condition = PromiseCardText.Conditions(gear);
                Check(condition.Contains("80") && condition.Contains("24") && condition.Contains(QualityCategory.Good.GetLabel()),
                    "Equipment card keeps quality, condition and continuous hold visible: " + language);
                gear.QualityOptional = true;
                Check(!PromiseCardText.Conditions(gear).Contains(QualityCategory.Good.GetLabel()), "Optional quality does not become a new requirement: " + language);
                var a = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
                var b = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
                a.Name = new NameSingle("Iceman"); b.Name = new NameSingle(new string('W', 100));
                var shared = new PromiseRecord { Kind = PromiseKind.Bedroom, ParticipantA = a, ParticipantB = b };
                Check(PromiseCardText.Title(shared, a).Contains(b.LabelShort) && PromiseCardText.Title(shared, b).Contains("Iceman"),
                    "Personal titles switch partners without truncating long names: " + language);
                var request = new ColonistRequest { Terms = leisure };
                Check(!PromiseCardText.Reason(request).Contains("CP_") && !PromiseCardText.Reason(request).Contains("{"), "Recreation reason is localized: " + language);
                request.Terms = new PromiseRecord { Kind = PromiseKind.Bedroom };
                Check(PromiseCardText.Reason(request) == "CP_CardReasonUnknown".Translate().ToString(), "Missing old cause data uses neutral text: " + language);
                request.HousingReasons = new List<HousingWitness> { new HousingWitness { Cause = "SleptOnGround", Mood = -4 } };
                Check(!PromiseCardText.Reason(request).Contains("4"), "Individual housing reason omits source mood penalties: " + language);
                Check(leisure.SuccessMood == 0.19f && leisure.RequiredTicks == 11250 && !leisure.FrozenConsequences,
                    "Formatting leaves saved conditions and consequences unchanged: " + language);
                Check(!condition.Contains("CP_") && !condition.Contains("{"), "Equipment card has no unresolved keys or placeholders: " + language);
            }
        }
        finally { LanguageDatabase.activeLanguage = oldLanguage; LanguageDatabase.defaultLanguage = oldDefault; }
        return checks;
    }
}
