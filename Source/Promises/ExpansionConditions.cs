using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public sealed class ThroneRoomCondition : IPromiseCondition
    {
        public PromiseKind Kind => PromiseKind.ThroneRoom;
        public bool UsesContinuousHold => true;
        public bool ValidParameters(PromiseRecord r) => r.ExpansionRequest && r.SourceTitle != null
            && r.RequiredTicks == PromiseBalance.HoldTicks && r.BedroomRequirements?.Count > 0
            && r.BedroomRequirements.All(x => x.Rebuild() != null);
        public ConditionObservation Observe(Pawn pawn, PromiseRecord r)
        {
            var result = new ConditionObservation();
            if (!ModsConfig.RoyaltyActive || !PromiseConditions.Observable(pawn) || !ValidParameters(r)) return result;
            var throne = pawn.ownership?.AssignedThrone;
            if (throne == null || !throne.Spawned || throne.Map != pawn.Map || throne.IsForbidden(pawn)
                || throne.TryGetComp<CompAssignableToPawn>()?.AssignedPawnsForReading.Contains(pawn) != true) return result;
            var room = throne.GetRoom();
            if (room == null || !room.ProperRoom || RoomRoleWorker_ThroneRoom.Validate(room) != null) return result;
            result.Thing = throne; result.Room = room;
            result.Satisfied = r.BedroomRequirements.All(x => x.Rebuild().Met(room, pawn));
            return result;
        }
        public string Describe(PromiseRecord r) => "CP_ThroneAction".Translate(PromiseBalance.HoldTicks / PromiseBalance.TicksPerHour)
            + "\n" + string.Join("; ", r.BedroomRequirements?.Select(x => x.Rebuild()?.Label(null) ?? "CP_MissingParameter".Translate().ToString())
                ?? Enumerable.Empty<string>());
    }
    public sealed class StyleCondition : IPromiseCondition
    {
        public PromiseKind Kind => PromiseKind.Style;
        public bool UsesContinuousHold => false;
        public bool ValidParameters(PromiseRecord r) => r.ExpansionRequest && r.RequiredTicks == RequestRules.StyleRequiredTicks
            && (r.StyleBeard ? r.TargetBeard != null && DefDatabase<BeardDef>.AllDefsListForReading.Contains(r.TargetBeard)
                : r.TargetHair != null && DefDatabase<HairDef>.AllDefsListForReading.Contains(r.TargetHair));
        public ConditionObservation Observe(Pawn pawn, PromiseRecord r) => new ConditionObservation {
            Satisfied = ModsConfig.IdeologyActive && r.StyleChangedTick >= r.CreatedTick && r.StyleChangedTick <= r.DeadlineTick
                && ExpansionRequests.CurrentStyle(pawn, r.StyleBeard) == ExpansionRequests.TargetStyle(r) };
        public string Describe(PromiseRecord r) => (r.StyleBeard ? "CP_BeardAction" : "CP_HairAction")
            .Translate(ExpansionRequests.TargetStyle(r)?.LabelCap.ToString() ?? "CP_MissingParameter".Translate().ToString());
    }
}
