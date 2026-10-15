using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public static class PromiseConsequences
    {
        public static int DeclineTrustChange(ColonistPromises state) => PromiseBalance.ClampTrust(state.Trust + RequestRules.DeclineTrust) - state.Trust;
        public static string DeclineText(ColonistPromises state) => "CP_DeclineEffects".Translate(
            PromiseText.Signed(RequestRules.DeclineMood), RequestRules.DeclineMemoryDays, PromiseText.Signed(DeclineTrustChange(state)));
        public static void ApplyDecline(ColonistPromises state, PromiseRecord record)
        {
            int delta = DeclineTrustChange(state);
            state.Trust += delta;
            record.RecordConsequences(RequestRules.DeclineMood, delta);
            AddMemory(state, "CP_RequestDeclined", RequestRules.DeclineMood, RequestRules.DeclineMemoryDays);
        }

        public static PromiseRecord Preview(ColonistPromises state, ColonistRequest request, int tick)
        {
            var copy = request.Terms.CopyForPreview();
            Snapshot(state, new ColonistRequest { Terms = copy, Magnitude = request.Magnitude }, tick);
            return copy;
        }

        public static void Snapshot(ColonistPromises state, ColonistRequest request, int tick)
        {
            var r = request.Terms;
            if (r.FrozenConsequences) return;
            int kind = (int)r.Kind;
            int magnitude = request.Magnitude;
            float factor = RewardFactor(state, r.Kind, tick);
            bool experiment = r.Kind == PromiseKind.Substance && r.SubstanceExperiment;
            int baseMood = experiment ? RequestRules.ExperimentMood : RequestRules.MoodReward(kind, magnitude);
            int baseTrust = experiment ? RequestRules.ExperimentTrust : RequestRules.TrustReward(kind, magnitude);
            r.SuccessMood = TrustRules.RewardMood(baseMood, factor, state.Trust);
            r.SuccessTrust = PromiseBalance.ClampTrust(state.Trust + System.Math.Max(1,
                (int)System.Math.Round(baseTrust * factor))) - state.Trust;
            r.BrokenMood = -(baseMood + 2)
                + PromiseBalance.FailureMood(state.Trust) - PromiseBalance.BrokenMood;
            r.BrokenTrust = PromiseBalance.ClampTrust(state.Trust + RequestRules.BrokenTrust(magnitude)) - state.Trust;
            r.WithdrawnMood = RequestRules.WithdrawMood(magnitude);
            r.WithdrawnTrust = PromiseBalance.ClampTrust(state.Trust + RequestRules.WithdrawTrust(magnitude)) - state.Trust;
            r.SuccessDays = experiment ? RequestRules.ExperimentMemoryDays : PromiseBalance.SuccessMemoryDays;
            r.BrokenDays = PromiseBalance.BrokenMemoryDays;
            r.WithdrawnDays = PromiseBalance.WithdrawnMemoryDays;
            r.CooldownDays = RequestRules.CompletedCooldownDays;
        }

        public static float RewardFactor(ColonistPromises state, PromiseKind kind, int tick) => PromiseBalance.RewardMultiplier(
            state.RecentSuccesses.Count(s => s.Kind == kind && tick - s.Tick <= PromiseBalance.RepeatWindowDays * PromiseBalance.TicksPerDay));

        public static void Apply(ColonistPromises state, PromiseRecord record, PromiseOutcome outcome, int tick)
        {
            SubstanceNeeds.StartCooldown(state, record, tick);
            if (outcome != PromiseOutcome.Fulfilled && outcome != PromiseOutcome.Broken && outcome != PromiseOutcome.Withdrawn)
            {
                record.RecordConsequences(0, 0);
                return;
            }
            string defName;
            int days;
            float mood;
            int trust;
            switch (outcome)
            {
                case PromiseOutcome.Fulfilled:
                    float factor = RewardFactor(state, record.Kind, tick);
                    mood = PromiseBalance.SuccessMood * factor;
                    trust = System.Math.Max(1, (int)System.Math.Round(PromiseBalance.SuccessTrust * factor));
                    days = PromiseBalance.SuccessMemoryDays;
                    defName = "CP_PromiseFulfilled";
                    state.RecentSuccesses.Add(new SuccessStamp { Kind = record.Kind, Tick = tick });
                    break;
                case PromiseOutcome.Broken:
                    mood = PromiseBalance.FailureMood(state.Trust);
                    trust = PromiseBalance.BrokenTrust;
                    days = PromiseBalance.BrokenMemoryDays;
                    defName = "CP_PromiseBroken";
                    break;
                default:
                    mood = PromiseBalance.WithdrawnMood;
                    trust = PromiseBalance.WithdrawnTrust;
                    days = PromiseBalance.WithdrawnMemoryDays;
                    defName = "CP_PromiseWithdrawn";
                    break;
            }
            // Old records deliberately retain the previous consequence calculation.
            if (record.FrozenConsequences)
            {
                if (outcome == PromiseOutcome.Fulfilled) { mood = record.SuccessMood; trust = record.SuccessTrust; days = record.SuccessDays; }
                else if (outcome == PromiseOutcome.Broken) { mood = record.BrokenMood; trust = record.BrokenTrust; days = record.BrokenDays; }
                else { mood = record.WithdrawnMood; trust = record.WithdrawnTrust; days = record.WithdrawnDays; }
            }
            int oldTrust = state.Trust;
            state.Trust = PromiseBalance.ClampTrust(oldTrust + trust);
            record.RecordConsequences(mood, state.Trust - oldTrust);
            AddMemory(state, defName, mood, days);
            state.CooldownUntilTick = tick + (record.FrozenConsequences ? record.CooldownDays : PromiseBalance.CooldownDays) * PromiseBalance.TicksPerDay;
        }

        private static void AddMemory(ColonistPromises state, string defName, float mood, int days)
        {
            var memories = state.Pawn?.needs?.mood?.thoughts?.memories;
            if (memories != null)
            {
                var def = DefDatabase<ThoughtDef>.GetNamed(defName);
                // Replace, rather than stack, the same outcome's memory.
                memories.RemoveMemoriesOfDef(def);
                var memory = (Thought_Memory)ThoughtMaker.MakeThought(def);
                memory.moodPowerFactor = mood / def.stages[0].baseMoodEffect;
                memory.durationTicksOverride = days * PromiseBalance.TicksPerDay;
                memories.TryGainMemory(memory);
            }
        }
    }
}
