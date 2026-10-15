using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public sealed class HousingIssue
    {
        public Pawn Pawn;
        public string Cause;
        public float Mood;
        public bool Current;
        public bool Supported;
        public bool Memory;
        public bool Retained;
        public int MinimumStage;
    }

    public static class HousingRules
    {
        private static readonly string[] memories = { "SleptInBedroom", "SleptInBarracks", "SleptOnGround", "SleptOutside" };
        private static readonly string[] situations = { "Greedy", "WantToSleepWithSpouseOrLover", "Ascetic", "Jealous" };
        private static readonly string[] unsupportedMemories = { "SleptInCold", "SleptInHeat", "SleepDisturbed" };
        public static bool SupportedCause(string cause) => memories.Contains(cause)
            || cause == "Greedy" || cause == "WantToSleepWithSpouseOrLover" || DlcCompatibility.RoyalCause(cause);

        public static bool MemoryProblem(string cause, bool usableBed, bool materialBed, bool outdoors,
            bool correctRoomRole, float projectedMood, bool ascetic) => cause == "SleptOnGround" ? !usableBed || !materialBed
            : cause == "SleptOutside" ? !usableBed || outdoors : (cause == "SleptInBedroom" || cause == "SleptInBarracks")
                && usableBed && correctRoomRole && !ascetic && projectedMood < 0;

        public static bool NegativeEvidence(float mood, bool current, bool negativeNow, bool witnessed) =>
            current && mood < 0 && (negativeNow || witnessed);

        public static List<HousingIssue> Evaluate(Pawn pawn)
        {
            var result = new List<HousingIssue>();
            var saved = GameComponent_Promises.Current?.PeekState(pawn).HousingWitnesses ?? new List<HousingWitness>();
            var activeMemories = pawn.needs?.mood?.thoughts?.memories?.Memories;
            foreach (string name in memories)
            {
                var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(name);
                if (def == null) continue;
                var negative = activeMemories?.Where(m => m.def == def && !m.ShouldDiscard && m.CurStage != null && m.MoodOffset() < 0)
                    .OrderBy(m => m.MoodOffset()).FirstOrDefault();
                var witness = saved.FirstOrDefault(w => w.Cause == name && w.Mood < 0);
                if (negative == null && witness == null) continue;
                bool current = CurrentMemoryProblem(pawn, def);
                float mood = negative?.MoodOffset() ?? witness.Mood;
                result.Add(new HousingIssue { Pawn = pawn, Cause = name, Mood = mood, Memory = true,
                    Retained = negative == null,
                    Current = NegativeEvidence(mood, current, negative != null, witness != null), Supported = true,
                    MinimumStage = name == "SleptInBedroom" ? NeutralStage(pawn, def, 0) : 1 });
            }
            foreach (string name in situations.Concat(ModsConfig.RoyaltyActive
                ? new[] { "TitleBedroomRequirementsNotMet", "TitleNoPersonalBedroom" } : new string[0]))
            {
                var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(name);
                if (def == null) continue;
                var thought = ThoughtMaker.MakeThought(def) as Thought_Situational;
                if (thought == null) continue;
                thought.pawn = pawn;
                thought.RecalculateState();
                if (!thought.Active || thought.MoodOffset() >= 0) continue;
                result.Add(new HousingIssue { Pawn = pawn, Cause = name, Mood = thought.MoodOffset(), Current = true,
                    Supported = name == "Greedy" || name == "WantToSleepWithSpouseOrLover" || DlcCompatibility.RoyalCause(name),
                    MinimumStage = name == "Greedy" ? NeutralStage(pawn, def, 1) : 1 });
            }
            foreach (var memory in activeMemories ?? new List<Thought_Memory>())
                if (unsupportedMemories.Contains(memory.def.defName) && !memory.ShouldDiscard && memory.CurStage != null && memory.MoodOffset() < 0)
                    result.Add(new HousingIssue { Pawn = pawn, Cause = memory.def.defName, Mood = memory.MoodOffset(), Memory = true });
            return result;
        }

        public static bool CurrentMemoryProblem(Pawn pawn, ThoughtDef def)
        {
            if (ThoughtUtility.ThoughtNullified(pawn, def)) return false;
            var bed = pawn.ownership?.OwnedBed;
            var room = bed?.GetRoom();
            bool usable = BedAvailable(pawn, bed);
            bool role = room != null && room.Role == (def.defName == "SleptInBedroom" ? RoomRoleDefOf.Bedroom : RoomRoleDefOf.Barracks);
            float projected = role ? StageMood(pawn, def,
                RoomStatDefOf.Impressiveness.GetScoreStageIndex(room.GetStat(RoomStatDefOf.Impressiveness))) : 0;
            return MemoryProblem(def.defName, usable, usable && CostListCalculator.CostListAdjusted(bed).Count > 0,
                room?.PsychologicallyOutdoors == true,
                role, projected, pawn.story?.traits?.HasTrait(TraitDefOf.Ascetic) == true);
        }

        public static bool BedAvailable(Pawn pawn, Building_Bed bed) => bed != null && bed.Spawned && !bed.Medical
            && bed.ForColonists && bed.IsOwner(pawn) && RestUtility.CanUseBedNow(bed, pawn, false);

        private static float StageMood(Pawn pawn, ThoughtDef def, int stage)
        {
            if (stage < 0 || stage >= def.stages.Count || def.stages[stage] == null) return 0;
            var thought = new Thought_Memory { def = def, pawn = pawn };
            thought.SetForcedStage(stage);
            return thought.MoodOffset();
        }

        public static int NeutralStage(Pawn pawn, ThoughtDef def, int stageOffset)
        {
            return FirstNonnegative(Enumerable.Range(0, RoomStatDefOf.Impressiveness.scoreStages.Count)
                .Select(stage => stage + stageOffset < def.stages.Count ? StageMood(pawn, def, stage + stageOffset) : float.NaN));
        }

        public static int FirstNonnegative(IEnumerable<float> effects)
        {
            int index = 0;
            foreach (float mood in effects) { if (mood >= 0) return index; index++; }
            return -1;
        }

        public static List<HousingWitness> Witnesses(IEnumerable<HousingIssue> issues) => issues
            .Where(i => i.Current && i.Memory && i.Supported).Select(i => new HousingWitness { Cause = i.Cause, Mood = i.Mood }).ToList();

        public static string Describe(IEnumerable<HousingIssue> issues) => string.Join("\n", issues.Select(i =>
            "CP_HousingEvidence".Translate(i.Pawn.LabelShort, ("CP_HousingCause_" + i.Cause).Translate(),
                PromiseText.Signed(i.Mood), (i.Retained ? "CP_HousingWitness" : i.Memory ? "CP_HousingMemory" : "CP_HousingSituation").Translate(),
                (!i.Supported && i.Memory ? "CP_HousingUnverified" : i.Current ? "CP_DebugYes" : "CP_DebugNo").Translate()).ToString()));

        public static string Reason(ColonistRequest request) => request.HousingReasons == null ? null
            : string.Join("\n", request.HousingReasons.Select(i => "CP_HousingPersonalReason".Translate(
                i.Subject?.LabelShort ?? "CP_MissingColonist".Translate().ToString(),
                ("CP_HousingCause_" + i.Cause).Translate(), PromiseText.Signed(i.Mood),
                RequestRules.PersistentTicks / PromiseBalance.TicksPerDay).ToString()));

        public static bool OriginalCauseCurrent(ColonistRequest request, IEnumerable<HousingIssue> issues) =>
            request.HousingReasons?.Any(source => issues.Any(i => i.Pawn == source.Subject && i.Cause == source.Cause
                && i.Current && i.Supported)) == true;

        public static RequestCandidate Candidate(Pawn first, Pawn second, RequestEvaluation evaluation)
        {
            var issues = evaluation.HousingIssues ?? Evaluate(first);
            if (evaluation.HousingIssues == null && second != null) issues.AddRange(Evaluate(second));
            evaluation.HousingIssues = issues;
            evaluation.Current = (evaluation.Current == null ? "" : evaluation.Current + "\n") + Describe(issues);
            var current = issues.Where(i => i.Current && i.Supported && SupportedCause(i.Cause)).ToList();
            if (current.Count == 0)
            {
                evaluation.Reason = issues.Any(i => !i.Supported) ? "CP_HousingUnsupported" : "CP_HousingNoEvidence";
                return null;
            }
            if (current.Any(i => i.Cause == "WantToSleepWithSpouseOrLover") && second == null)
            { evaluation.Reason = "CP_HousingUnsupported"; return null; }
            if (first.story?.traits?.HasTrait(TraitDefOf.Ascetic) == true || second?.story?.traits?.HasTrait(TraitDefOf.Ascetic) == true)
            { evaluation.Reason = second == null ? "CP_HousingUnsupported" : "CP_PairConflictingNeeds"; return null; }
            bool barracks = current.All(i => i.Cause != "WantToSleepWithSpouseOrLover" && !DlcCompatibility.RoyalCause(i.Cause))
                && first.ownership?.OwnedBed?.GetRoom()?.Role == RoomRoleDefOf.Barracks
                && (second == null || first.ownership?.OwnedBed == second.ownership?.OwnedBed);
            int target = current.Max(i => i.MinimumStage);
            if (barracks)
            {
                foreach (var pawn in new[] { first, second }.Where(p => p != null))
                {
                    var def = DefDatabase<ThoughtDef>.GetNamedSilentFail("SleptInBarracks");
                    if (def == null) { evaluation.Reason = "CP_HousingUnsupported"; return null; }
                    target = System.Math.Max(target, NeutralStage(pawn, def, 0));
                }
            }
            // A new room must not introduce a known Greedy bedroom penalty for either participant.
            foreach (var pawn in new[] { first, second }.Where(p => p != null && p.story?.traits?.HasTrait(TraitDefOf.Greedy) == true))
            {
                var def = DefDatabase<ThoughtDef>.GetNamedSilentFail("Greedy");
                if (def != null) target = System.Math.Max(target, NeutralStage(pawn, def, 1));
            }
            if (target < 0 || target >= RoomStatDefOf.Impressiveness.scoreStages.Count || current.Any(i => i.MinimumStage < 0))
            { evaluation.Reason = "CP_HousingUnsupported"; return null; }
            var terms = new PromiseRecord { Kind = PromiseKind.Bedroom, ParticipantA = second == null ? null : first,
                ParticipantB = second, RoomStage = target, RequiresBarracks = barracks, HousingBased = true,
                RequiresConstructedBed = current.Any(i => i.Cause == "SleptOnGround"), RequiredTicks = PromiseBalance.HoldTicks };
            var requirements = new List<BedroomRequirement>();
            if (!DlcCompatibility.CaptureBedroom(first, requirements)
                || second != null && !DlcCompatibility.CaptureBedroom(second, requirements))
            { evaluation.Reason = "CP_RoyalBedroomUnsupported"; return null; }
            if (requirements.Count > 0) { terms.BedroomRequirements = requirements; terms.RequiresBarracks = false; }
            var observed = PromiseConditions.Get(PromiseKind.Bedroom).Observe(first, terms);
            if (observed.Satisfied) { evaluation.Reason = "CP_DebugSatisfied"; return null; }
            var room = first.ownership?.OwnedBed?.GetRoom();
            int stage = room == null ? -1 : RoomStatDefOf.Impressiveness.GetScoreStageIndex(room.GetStat(RoomStatDefOf.Impressiveness));
            evaluation.HasDeficit = true;
            evaluation.Reason = null;
            string prefix = "housing-v2:" + (second == null ? first.thingIDNumber.ToString() : SharedBedroomRules.PairKey(first, second))
                + ":" + terms.RequiresBarracks + ":" + target + ":" + terms.RequiresConstructedBed
                + (requirements.Count == 0 ? "" : ":royal:" + DlcCompatibility.RequirementSignature(requirements)) + "|";
            string saved = GameComponent_Promises.Current?.PeekState(first).Deficits.FirstOrDefault(d => d.Kind == PromiseKind.Bedroom)?.Signature;
            var anchor = current.FirstOrDefault(i => saved == prefix + i.Pawn.thingIDNumber + "=" + i.Cause)
                ?? current.OrderBy(i => i.Pawn.thingIDNumber).ThenBy(i => i.Cause).First();
            // Preserve a still-current anchor: a second partner's new concern must not erase its age.
            return new RequestCandidate { Terms = terms, Signature = prefix + anchor.Pawn.thingIDNumber + "=" + anchor.Cause,
                ReasonKey = "CP_HousingReason", HousingReasons = new List<HousingWitness> { new HousingWitness {
                    Subject = anchor.Pawn, Cause = anchor.Cause, Mood = anchor.Mood } },
                Expectation = ExpectationsUtility.CurrentExpectationFor(first), Magnitude = stage < 0 || target - stage >= 2 ? 1 : 0 };
        }
    }
}
