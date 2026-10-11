using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public sealed class ConditionObservation
    {
        public bool Satisfied;
        public Thing Thing;
        public Room Room;
    }

    public interface IPromiseCondition
    {
        PromiseKind Kind { get; }
        bool UsesContinuousHold { get; }
        bool ValidParameters(PromiseRecord record);
        ConditionObservation Observe(Pawn pawn, PromiseRecord record);
        string Describe(PromiseRecord record);
    }

    public static class PromiseConditions
    {
        private static readonly Dictionary<PromiseKind, IPromiseCondition> Conditions = new[]
        {
            (IPromiseCondition)new BedroomCondition(), new LeisureCondition(), new EquipmentCondition()
        }.ToDictionary(c => c.Kind);
        public static IPromiseCondition Get(PromiseKind kind) => Conditions.TryGetValue(kind, out var value) ? value : null;
        public static IEnumerable<IPromiseCondition> All => Conditions.Values;

        public static string EligibilityReason(Pawn pawn)
        {
            if (pawn == null) return "CP_DebugMissing";
            if (pawn.Dead || pawn.Destroyed) return "CP_DebugDead";
            if (pawn.Faction != Faction.OfPlayer) return "CP_DebugFaction";
            if (!pawn.IsFreeNonSlaveColonist || pawn.HostFaction != null || QuestUtility.IsQuestLodger(pawn))
                return "CP_DebugGuest";
            if (!pawn.RaceProps.Humanlike || !pawn.ageTracker.Adult) return "CP_DebugAdult";
            if (pawn.needs?.mood == null) return "CP_DebugMood";
            return null;
        }

        public static bool Eligible(Pawn pawn) => EligibilityReason(pawn) == null;

        public static bool Observable(Pawn pawn) => Eligible(pawn) && pawn.Spawned && !pawn.Suspended;
    }

    public sealed class BedroomCondition : IPromiseCondition
    {
        public PromiseKind Kind => PromiseKind.Bedroom;
        public bool UsesContinuousHold => true;
        public bool ValidParameters(PromiseRecord r) => r.RoomStage >= 0 && r.RoomStage < RoomStatDefOf.Impressiveness.scoreStages.Count;

        public ConditionObservation Observe(Pawn pawn, PromiseRecord r)
        {
            if (r.IsShared) return ValidParameters(r) ? SharedBedroomRules.Observe(r) : new ConditionObservation();
            var result = new ConditionObservation();
            if (!PromiseConditions.Observable(pawn) || !ValidParameters(r)) return result;
            var bed = pawn.ownership?.OwnedBed;
            if (bed == null || !bed.Spawned || bed.Map != pawn.Map || bed.Medical || !bed.ForColonists
                || bed.OwnersForReading.Count != 1 || !bed.IsOwner(pawn)) return result;
            if (r.HousingBased && (!HousingRules.BedAvailable(pawn, bed)
                || r.RequiresConstructedBed && CostListCalculator.CostListAdjusted(bed).Count == 0)) return result;
            var room = bed.GetRoom();
            if (room == null || !room.ProperRoom || room.PsychologicallyOutdoors
                || room.Role != (r.RequiresBarracks ? RoomRoleDefOf.Barracks : RoomRoleDefOf.Bedroom)
                || room.ContainedBeds.Any(b => b.Medical) || !r.RequiresBarracks && room.Owners.Any(owner => owner != pawn)) return result;
            result.Satisfied = RoomStatDefOf.Impressiveness.GetScoreStageIndex(room.GetStat(RoomStatDefOf.Impressiveness)) >= r.RoomStage
                && DlcCompatibility.BedroomMet(room, pawn, r);
            result.Thing = bed;
            result.Room = room;
            return result;
        }

        public string Describe(PromiseRecord r) => (r.RequiresBarracks ? (r.IsShared ? "CP_SharedBarracksConditions" : "CP_BarracksConditions")
            : r.IsShared ? "CP_SharedBedroomConditions" : "CP_BedroomConditions").Translate(RoomLabel(r.RoomStage), PromiseBalance.HoldTicks / PromiseBalance.TicksPerHour)
            + (r.RequiresConstructedBed ? "\n" + "CP_ConstructedBed".Translate().ToString() : "") + DlcCompatibility.BedroomText(r);
        public static string RoomLabel(int stage) => stage >= 0 && stage < RoomStatDefOf.Impressiveness.scoreStages.Count
            ? RoomStatDefOf.Impressiveness.scoreStages[stage].label : "CP_MissingParameter".Translate().ToString();
    }

    public sealed class LeisureCondition : IPromiseCondition
    {
        public PromiseKind Kind => PromiseKind.Leisure;
        public bool UsesContinuousHold => false;
        public bool ValidParameters(PromiseRecord r) => r.RequiredTicks >= PromiseBalance.MinLeisureHours * PromiseBalance.TicksPerHour
            && r.RequiredTicks <= PromiseBalance.MaxLeisureHours * PromiseBalance.TicksPerHour;
        public ConditionObservation Observe(Pawn pawn, PromiseRecord r) => new ConditionObservation();
        public string Describe(PromiseRecord r) => "CP_LeisureConditions".Translate(r.RequiredTicks / (float)PromiseBalance.TicksPerHour);

        // Only activity ticks emitted by the game's recreation utility are credited.
        public static bool CanCredit(Pawn pawn)
        {
            if (!PromiseConditions.Observable(pawn) || pawn.Downed || pawn.Drafted || pawn.InMentalState
                || pawn.jobs?.curDriver == null || pawn.jobs.curDriver.asleep || pawn.needs?.joy == null
                || pawn.CurJobDef?.joyKind == null) return false;
            var bed = pawn.CurrentBed();
            if (bed != null && bed.Medical) return false;
            return pawn.GetRoom()?.Role != RoomRoleDefOf.Hospital;
        }
    }

    public sealed class EquipmentCondition : IPromiseCondition
    {
        public PromiseKind Kind => PromiseKind.Equipment;
        public bool UsesContinuousHold => true;
        public bool ValidParameters(PromiseRecord r) => (r.QualityOptional ? IsEquippable(r.EquipmentDef) : IsSupported(r.EquipmentDef))
            && (int)r.MinimumQuality >= 0 && (int)r.MinimumQuality < QualityUtility.QualityCount;
        public static bool IsEquippable(ThingDef def) => def != null && def.PlayerAcquirable && (def.IsWeapon || def.IsApparel);
        public static bool IsSupported(ThingDef def) => IsEquippable(def) && def.HasComp(typeof(CompQuality));

        public ConditionObservation Observe(Pawn pawn, PromiseRecord r)
        {
            var result = new ConditionObservation();
            if (!PromiseConditions.Observable(pawn) || !ValidParameters(r)) return result;
            IEnumerable<Thing> equipped = r.EquipmentDef.IsApparel
                ? pawn.apparel?.WornApparel.Cast<Thing>() ?? Enumerable.Empty<Thing>()
                : pawn.equipment?.AllEquipmentListForReading.Cast<Thing>() ?? Enumerable.Empty<Thing>();
            var matching = equipped.Where(t => EquipmentNeeds.Matches(pawn, t, r)).ToList();
            result.Thing = matching.FirstOrDefault(t => t == r.StableThing) ?? matching.FirstOrDefault();
            result.Satisfied = result.Thing != null;
            return result;
        }

        public string Describe(PromiseRecord r) => (r.QualityOptional ? "CP_GearConditions" : "CP_EquipmentConditions").Translate(
            r.EquipmentDef?.LabelCap.ToString() ?? "CP_MissingParameter".Translate().ToString(),
            r.QualityOptional ? "CP_QualityNotRequired".Translate().ToString() : r.MinimumQuality.GetLabel(), PromiseBalance.HoldTicks / PromiseBalance.TicksPerHour).ToString()
            + (r.RequireCoverage ? "\n" + "CP_GearCoverage".Translate().ToString() : "")
            + (r.RequireMelee ? "\n" + "CP_GearMelee".Translate().ToString() : "")
            + (r.MinimumHitPointsFraction > 0 ? " " + "CP_Durability".Translate(PromiseText.Number(r.MinimumHitPointsFraction * 100)).ToString() : "")
            + (r.AvoidHumanLeather ? " " + "CP_NoHumanLeather".Translate().ToString() : "");
    }
}
