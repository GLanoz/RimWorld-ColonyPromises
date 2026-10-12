using System;

namespace ColonyPromises
{
    public static class PromiseBalance
    {
        public const int CheckInterval = 250;
        public const int TicksPerDay = 60000;
        public const int TicksPerHour = 2500;
        public const int HoldTicks = TicksPerDay;
        public const int InitialTrust = 50;
        public const int MinTrust = 0;
        public const int MaxTrust = 100;
        public const int SuccessMood = 4;
        public const int BrokenMood = -6;
        public const int HighTrustExtraPenalty = 3;
        public const int WithdrawnMood = -3;
        public const int SuccessTrust = 5;
        public const int BrokenTrust = -10;
        public const int WithdrawnTrust = -4;
        public const int SuccessMemoryDays = 5;
        public const int BrokenMemoryDays = 5;
        public const int WithdrawnMemoryDays = 3;
        public const int CooldownDays = 3;
        public const int RepeatWindowDays = 15;
        public const float RepeatMultiplier = 0.5f;
        public const float MinimumRewardMultiplier = 0.25f;
        public const int HistoryLimit = 12;
        // Retain the old parameter range so saved manual promises still validate.
        public const int MinLeisureHours = 1;
        public const int MaxLeisureHours = 24;

        public static int ClampTrust(int value) => Math.Max(MinTrust, Math.Min(MaxTrust, value));
        public static float RewardMultiplier(int repeats) => Math.Max(MinimumRewardMultiplier, (float)Math.Pow(RepeatMultiplier, repeats));
        public static int FailureMood(int trust) => BrokenMood - (int)Math.Round(HighTrustExtraPenalty * ClampTrust(trust) / (double)MaxTrust, MidpointRounding.AwayFromZero);
    }

    public static class TrustRules
    {
        public const int LowTrustThreshold = 40;
        public const float ZeroTrustMoodFactor = 0.25f;
        public const int RecoveryThreshold = 20;
        public const int UnpromisedRecoveryTrust = 2;
        public const int RecoveryCooldownDays = 10;
        public const int RecoveryRepeatDays = 15;
        public const int ReminderLeadTicks = PromiseBalance.TicksPerDay / 2;
        public static float RewardMood(int baseMood, float repeatFactor, int trust) =>
            (float)Math.Round(baseMood * repeatFactor * MoodFactor(trust), 2, MidpointRounding.AwayFromZero);
        public static float MoodFactor(int trust) => ZeroTrustMoodFactor + (1 - ZeroTrustMoodFactor)
            * Math.Min(LowTrustThreshold, PromiseBalance.ClampTrust(trust)) / LowTrustThreshold;
    }

    public static class ProgressMath
    {
        // A missing observation never credits elapsed time retroactively.
        public static int VerifiedDelta(int previous, int now, int deadline)
        {
            int delta = now - previous;
            return previous < 0 || delta < 0 || delta > PromiseBalance.CheckInterval
                ? 0 : Math.Max(0, Math.Min(now, deadline) - previous);
        }

        public static int LeisureCredit(int now, int interval, int created, int deadline, int lastCredit)
        {
            int end = Math.Min(now, deadline);
            int start = Math.Max(Math.Max(now - Math.Max(0, interval), created), lastCredit);
            return Math.Max(0, end - start);
        }
    }

    public static class RequestRules
    {
        public const int ObservationInterval = 2500;
        public const int PersistentTicks = 2 * PromiseBalance.TicksPerDay;
        public const int OfferSpacing = PromiseBalance.TicksPerDay / 2;
        public const int ResponseDays = 2;
        public const int UnacceptedCooldownDays = 3;
        public const int CompletedCooldownDays = 5;
        public const float LowJoy = 0.35f;
        public const float SevereJoy = 0.15f;
        public const float WornFraction = 0.5f;
        public const float ReplacementFraction = 0.8f;
        public const int WeaponSkillDifference = 4;
        public const int WeaponUpgradeSkill = 10;
        // Quality enum values: Normal = 2, Good = 3. Keep this rules file game-independent.
        public const int WeaponUpgradeBaseQuality = 2;
        public const int WeaponUpgradeSkilledQuality = 3;
        public const int MinHours = 4;
        public const int MaxHours = 8;
        public const int BedroomBaseStage = 2;
        public const int BedroomMaxStage = 5;
        public const int GreedyStageBonus = 1;
        public const int MaxRoomStep = 2;

        public static int ObserveTicks(int accumulated, int previous, int now, bool sameDeficit)
        {
            int delta = now - previous;
            return !sameDeficit || previous < 0 || delta < 0 || delta > ObservationInterval
                ? 0 : Math.Min(4 * PromiseBalance.TicksPerDay, accumulated + delta);
        }
        public static bool Ready(int ticks, bool occupied, int now, int cooldown) =>
            ticks >= PersistentTicks && !occupied && now >= cooldown;
        public static int BedroomStage(int expectationOrder, bool greedy, int stageCount) =>
            Math.Min(stageCount - 1, Math.Min(BedroomMaxStage, BedroomBaseStage + Math.Max(0, expectationOrder) / 2 + (greedy ? GreedyStageBonus : 0)));
        public static int RoomTarget(int current, int desired) => current >= desired ? -1 : Math.Min(desired, current + MaxRoomStep);
        public static int LeisureHours(int deficitTicks, int severeTicks) =>
            Math.Min(MaxHours, MinHours + (severeTicks >= PromiseBalance.TicksPerDay ? 2 : 0)
                + (deficitTicks >= 3 * PromiseBalance.TicksPerDay ? 2 : 0));
        public static int Deadline(int kind, int magnitude) => kind == 1 ? (magnitude == 0 ? 3 : 4)
            : kind == 0 ? (magnitude == 0 ? 5 : 7) : (magnitude == 0 ? 4 : 5);
        public static int MoodReward(int kind, int magnitude) => kind == 0 ? (magnitude == 0 ? 5 : 7)
            : kind == 1 ? (magnitude == 0 ? 3 : 4) : (magnitude == 0 ? 4 : 5);
        public static int TrustReward(int kind, int magnitude) => kind == 0 ? (magnitude == 0 ? 5 : 6)
            : kind == 1 ? (magnitude == 0 ? 3 : 4) : (magnitude == 0 ? 4 : 5);
        public static int BrokenBase(int kind, int magnitude) => -(MoodReward(kind, magnitude) + 2);
        public static int BrokenTrust(int magnitude) => magnitude == 0 ? -8 : -10;
        public static int WithdrawMood(int magnitude) => magnitude == 0 ? -2 : -3;
        public static int WithdrawTrust(int magnitude) => magnitude == 0 ? -3 : -4;
    }
}
