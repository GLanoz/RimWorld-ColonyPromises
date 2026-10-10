using System;
using System.Globalization;
using Verse;

namespace ColonyPromises
{
    public static class PromiseHistory
    {
        public static string Format(PromiseRecord record, int now)
        {
            bool ukrainian = LanguageDatabase.activeLanguage?.folderName?.StartsWith("Ukrainian", StringComparison.OrdinalIgnoreCase) == true;
            var culture = CultureInfo.GetCultureInfo(ukrainian ? "uk-UA" : "en-US");
            string age = record.HasResolvedTick
                ? Age(Math.Max(0L, (long)now - record.ResolvedTick) / (double)PromiseBalance.TicksPerDay, culture, ukrainian)
                : "CP_HistoryTimeUnavailable".Translate().ToString();
            bool noConsequences = record.Outcome != PromiseOutcome.Fulfilled
                && record.Outcome != PromiseOutcome.Broken && record.Outcome != PromiseOutcome.Withdrawn;
            string effects = noConsequences ? "CP_HistoryNoEffects".Translate().ToString()
                : "CP_HistoryEffects".Translate(
                    record.HasAppliedMood ? Signed(record.AppliedMood, culture) : "CP_HistoryValueUnavailable".Translate().ToString(),
                    record.HasAppliedTrust ? Signed(record.AppliedTrust, culture) : "CP_HistoryValueUnavailable".Translate().ToString()).ToString();
            // RimWorld's NamedArgument formatter expects plain placeholders, not .NET format clauses.
            return "CP_HistoryEntry".Translate(("CP_Type_" + record.Kind).Translate(),
                ("CP_Outcome_" + record.Outcome).Translate(), age, effects).ToString();
        }

        private static string Signed(float value, CultureInfo culture) =>
            value.ToString("+0.##;-0.##;0", culture).Replace('-', '\u2212');

        private static string Age(double days, CultureInfo culture, bool ukrainian)
        {
            double rounded = Math.Round(days, 2, MidpointRounding.AwayFromZero);
            string key = "CP_HistoryDaysMany";
            if (!ukrainian)
            {
                if (rounded == 1) key = "CP_HistoryDaysOne";
            }
            else if (rounded % 1 != 0) key = "CP_HistoryDaysFraction";
            else
            {
                int lastTwo = (int)(rounded % 100);
                int last = lastTwo % 10;
                if (last == 1 && lastTwo != 11) key = "CP_HistoryDaysOne";
                else if (last >= 2 && last <= 4 && (lastTwo < 12 || lastTwo > 14)) key = "CP_HistoryDaysFew";
            }
            return "CP_HistoryAge".Translate(rounded.ToString("0.##", culture), key.Translate()).ToString();
        }
    }
}
