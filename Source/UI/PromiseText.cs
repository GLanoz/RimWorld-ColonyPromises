using System;
using System.Globalization;
using Verse;

namespace ColonyPromises
{
    public static class PromiseText
    {
        public static CultureInfo Culture => CultureInfo.GetCultureInfo(
            LanguageDatabase.activeLanguage?.folderName?.StartsWith("Ukrainian", StringComparison.OrdinalIgnoreCase) == true
                ? "uk-UA" : "en-US");
        public static string Number(float value) => value.ToString("0.##", Culture);
        public static string Signed(float value) => value.ToString("+0.##;-0.##;0", Culture).Replace('-', '\u2212');
        public static string Type(PromiseRecord record) => record.RequiresBarracks
            ? (record.IsShared ? "CP_Type_SharedBarracks" : "CP_Type_Barracks").Translate().ToString()
            : record.IsShared ? "CP_Type_SharedBedroom".Translate().ToString()
            : ("CP_Type_" + record.Kind).Translate().ToString();
        public static string Consequences(PromiseRecord r) => "CP_FixedConsequences".Translate(
            Signed(r.SuccessMood), r.SuccessDays.ToString(), Signed(r.SuccessTrust),
            Signed(r.BrokenMood), r.BrokenDays.ToString(), Signed(r.BrokenTrust),
            Signed(r.WithdrawnMood), r.WithdrawnDays.ToString(), Signed(r.WithdrawnTrust), r.CooldownDays.ToString());
    }
}
