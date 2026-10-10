using ColonyPromises;

int assertions = 0;
void Equal<T>(T expected, T actual, string scenario) where T : IEquatable<T>
{
    assertions++;
    if (!expected.Equals(actual)) throw new Exception($"{scenario}: expected {expected}, got {actual}");
}

Equal(0, ProgressMath.VerifiedDelta(-1, 200, 1000), "No previous observation");
Equal(0, ProgressMath.VerifiedDelta(500, 499, 1000), "Time reversal cannot add progress");
Equal(0, ProgressMath.VerifiedDelta(500, 500, 1000), "Same-tick load adds no progress");
Equal(250, ProgressMath.VerifiedDelta(500, 750, 1000), "Regular interval");
Equal(0, ProgressMath.VerifiedDelta(500, 751, 1000), "Unobserved gap earns nothing");
Equal(100, ProgressMath.VerifiedDelta(900, 1100, 1000), "Deadline clips hold progress");
Equal(0, ProgressMath.VerifiedDelta(1100, 1200, 1000), "No hold credit after deadline");
Equal(125, ProgressMath.VerifiedDelta(900, 1025, 1025), "Partial final interval");

// Simulate one day with a save/load observation at the same game tick.
int progress = 0;
for (int tick = 250; tick <= 60000; tick += 250)
{
    progress += ProgressMath.VerifiedDelta(tick - 250, tick, 60000);
    if (tick == 30000) progress += ProgressMath.VerifiedDelta(tick, tick, 60000);
}
Equal(60000, progress, "A save/load does not alter a verified day");

Equal(0, ProgressMath.LeisureCredit(100, 250, 100, 1000, 100), "No leisure before issuing promise");
Equal(50, ProgressMath.LeisureCredit(150, 250, 100, 1000, 100), "First activity interval clips at creation");
Equal(250, ProgressMath.LeisureCredit(500, 250, 100, 1000, 150), "Actual leisure interval");
Equal(0, ProgressMath.LeisureCredit(500, 250, 100, 1000, 500), "Duplicate recreation callback");
Equal(50, ProgressMath.LeisureCredit(550, 250, 100, 1000, 500), "Overlapping callback cannot double count");
Equal(0, ProgressMath.LeisureCredit(900, -10, 100, 1000, 500), "Negative activity duration");
Equal(100, ProgressMath.LeisureCredit(1100, 250, 100, 1000, 900), "Leisure interval clips at expiry");
Equal(0, ProgressMath.LeisureCredit(1500, 250, 100, 1000, 900), "Entire activity after expiry");
Equal(0, ProgressMath.LeisureCredit(900, 250, 100, 1000, 950), "Reversed activity clock");

Equal(1f, PromiseBalance.RewardMultiplier(0), "First reward");
Equal(0.5f, PromiseBalance.RewardMultiplier(1), "First repeated type");
Equal(0.25f, PromiseBalance.RewardMultiplier(2), "Second repeated type");
Equal(0.25f, PromiseBalance.RewardMultiplier(30), "Repeat reward floor");
Equal(-6, PromiseBalance.FailureMood(0), "Minimum-trust penalty");
Equal(-8, PromiseBalance.FailureMood(50), "Initial-trust penalty");
Equal(-9, PromiseBalance.FailureMood(100), "Maximum-trust penalty");
Equal(-6, PromiseBalance.FailureMood(-100), "Failure clamps low trust");
Equal(-9, PromiseBalance.FailureMood(200), "Failure clamps high trust");
Equal(0, PromiseBalance.ClampTrust(-1), "Trust lower bound");
Equal(100, PromiseBalance.ClampTrust(105), "Trust upper bound");
Equal(50, PromiseBalance.ClampTrust(50), "Trust within bounds");

// Persistent deficits need contiguous observations; loading and gaps add nothing.
int deficit = 0;
int last = 0;
for (int tick = RequestRules.ObservationInterval; tick <= RequestRules.PersistentTicks; tick += RequestRules.ObservationInterval)
{
    deficit = RequestRules.ObserveTicks(deficit, last, tick, true);
    last = tick;
    if (tick < RequestRules.PersistentTicks) Equal(false, RequestRules.Ready(deficit, false, tick, 0), "No request before sustained deficit");
}
Equal(true, RequestRules.Ready(deficit, false, last, 0), "Two observed days unlock a request");
Equal(false, RequestRules.Ready(deficit, true, last, 0), "One request or promise per colonist");
Equal(false, RequestRules.Ready(deficit, false, last, last + 1), "Cooldown blocks generation");
Equal(0, RequestRules.ObserveTicks(deficit, last, last + 1, false), "Satisfied or changed need resets observations");
Equal(0, RequestRules.ObserveTicks(deficit, last, last + RequestRules.ObservationInterval + 1, true), "Unobserved gap restarts deficit");
Equal(deficit, RequestRules.ObserveTicks(deficit, last, last, true), "Reload at same tick adds nothing");
Equal(0, RequestRules.ObserveTicks(deficit, last, last - 1, true), "Reversed clock restarts observation");
Equal(4, RequestRules.LeisureHours(RequestRules.PersistentTicks, 0), "Moderate deficit asks for four hours");
Equal(6, RequestRules.LeisureHours(RequestRules.PersistentTicks, 60000), "Severe deficit asks for six hours");
Equal(8, RequestRules.LeisureHours(180000, 60000), "Prolonged severe deficit asks for eight hours");
Equal(8, RequestRules.LeisureHours(int.MaxValue, int.MaxValue), "Recreation target capped");
Equal(-1, RequestRules.RoomTarget(4, 4), "Satisfied bedroom creates no request");
Equal(-1, RequestRules.RoomTarget(6, 4), "Better bedroom creates no request");
Equal(3, RequestRules.RoomTarget(1, 5), "Upgrade capped at two stages");
Equal(5, RequestRules.BedroomStage(100, true, 10), "Expectations cannot demand highest stage without bound");
Equal(3, RequestRules.BedroomStage(0, true, 10), "Greedy trait raises room target");
Equal(3, RequestRules.Deadline(1, 0), "Basic recreation has short deadline");
Equal(7, RequestRules.Deadline(0, 1), "Substantial bedroom has longer deadline");
Equal(false, RequestRules.MoodReward(0, 1) == RequestRules.MoodReward(1, 0), "Room and recreation rewards differ");
Equal(true, RequestRules.WithdrawMood(1) > RequestRules.BrokenBase(0, 1), "Withdrawal is milder than failure");
Console.WriteLine($"Passed {assertions} core checks. Game behavior and Scribe integration require in-game tests.");
