using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace ColonyPromises
{
    public static class PromiseCardText
    {
        public static string Title(PromiseRecord r, Pawn selected)
        {
            if (r.IsShared) return (r.RequiresBarracks ? "CP_CardSharedBarracks" : "CP_CardSharedBedroom")
                .Translate(r.PartnerOf(selected)?.LabelShort ?? "CP_MissingColonist".Translate().ToString());
            return r.Kind == PromiseKind.Equipment && r.NeedBasedEquipment
                ? (r.EquipmentDef.IsApparel ? "CP_CardClothing" : "CP_CardWeapon").Translate().ToString() : PromiseText.Type(r);
        }

        public static string Cause(string cause, IEnumerable<Pawn> subjects)
        {
            var people = subjects.Where(p => p != null).Distinct().ToList();
            string names = people.Count == 2 ? SharedPromiseText.Names(people[0], people[1]) : people.FirstOrDefault()?.LabelShort;
            return ("CP_CardCause_" + cause + (people.Count == 2 ? "_Pair" : "")).Translate(names ?? "CP_MissingColonist".Translate().ToString());
        }

        public static string Reason(ColonistRequest request)
        {
            var r = request.Terms;
            if (r.DevManual) return "CP_DevManualReason".Translate();
            if (r.IsShared) return SharedPromiseText.RequestReason(request);
            if (r.Kind == PromiseKind.Leisure) return "CP_CardReasonLeisure".Translate();
            if (r.NeedBasedEquipment) return ("CP_CardReason_" + r.EquipmentCause).Translate(r.EquipmentDef.LabelCap);
            var reasons = request.HousingReasons?.Where(i => i.Mood < 0 && HousingRules.SupportedCause(i.Cause)).ToList();
            return reasons?.Count > 0 ? string.Join(" ", reasons.Select(i => i.Cause).Distinct().Take(2)
                .Select(c => ("CP_CardReason_" + c).Translate().ToString())) : "CP_CardReasonUnknown".Translate().ToString();
        }

        public static string Conditions(PromiseRecord r)
        {
            string hours = PromiseText.Number(r.RequiredTicks / (float)PromiseBalance.TicksPerHour);
            if (r.Kind == PromiseKind.Leisure) return "CP_CardLeisure".Translate(hours);
            if (r.Kind == PromiseKind.Bedroom)
                return (r.RequiresBarracks ? (r.IsShared ? "CP_CardSharedBarracksAction" : "CP_CardBarracksAction")
                    : r.IsShared ? "CP_CardSharedBedroomAction" : "CP_CardBedroomAction")
                    .Translate(BedroomCondition.RoomLabel(r.RoomStage), hours).ToString()
                    + (r.RequiresConstructedBed ? " " + "CP_CardBuiltBed".Translate() : "") + DlcCompatibility.BedroomText(r);
            string quality = r.QualityOptional ? "" : "CP_CardQuality".Translate(r.MinimumQuality.GetLabel()).ToString();
            string condition = r.MinimumHitPointsFraction <= 0 ? "" : "CP_CardCondition".Translate(
                PromiseText.Number(r.MinimumHitPointsFraction * 100)).ToString();
            return (r.EquipmentDef?.IsApparel == true ? "CP_CardWear" : r.RequireMelee || EquipmentNeeds.AcquisitionCause(r.EquipmentCause) ? "CP_CardPrimary" : "CP_CardEquip")
                .Translate(r.EquipmentDef?.LabelCap.ToString() ?? "CP_MissingParameter".Translate().ToString(), quality, condition, hours).ToString()
                + (r.RequireCoverage ? " " + "CP_CardCoverage".Translate() : "")
                + (r.AvoidHumanLeather ? " " + "CP_NoHumanLeather".Translate() : "");
        }

        public static string Reward(PromiseRecord r) => r == null || r.SuccessDays <= 0 ? "CP_LegacyPromise".Translate().ToString()
            : "CP_CardReward".Translate(PromiseText.Signed(r.SuccessMood), r.SuccessDays, PromiseText.Signed(r.SuccessTrust)).ToString();

        public static string Penalties(PromiseRecord r) => "CP_CardPenalties".Translate(
            PromiseText.Signed(r.BrokenMood), r.BrokenDays, PromiseText.Signed(r.BrokenTrust),
            PromiseText.Signed(r.WithdrawnMood), r.WithdrawnDays, PromiseText.Signed(r.WithdrawnTrust), r.CooldownDays);

        public static string ProgressBlock(Pawn pawn, PromiseRecord r)
        {
            if (!PromiseConditions.Observable(pawn)) return "CP_CardAway".Translate();
            if (r.IsShared && (!PromiseConditions.Observable(r.ParticipantA) || !PromiseConditions.Observable(r.ParticipantB)
                || r.ParticipantA.Map != r.ParticipantB.Map)) return "CP_CardPartnerAway".Translate();
            if (r.Kind == PromiseKind.Leisure) return LeisureCondition.CanCredit(pawn) ? null : "CP_CardNotResting".Translate().ToString();
            var condition = PromiseConditions.Get(r.Kind);
            if (condition == null || !condition.ValidParameters(r)) return "CP_InvalidParameters".Translate();
            return condition.Observe(pawn, r).Satisfied ? null
                : (r.Kind == PromiseKind.Bedroom ? "CP_CardRoomPending" : "CP_CardGearPending").Translate().ToString();
        }
    }
}
