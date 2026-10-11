using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public enum SharedPairValidity { Valid, Invalid, Unknown }

    public static class SharedBedroomRules
    {
        public static string PairKey(Pawn a, Pawn b) => Math.Min(a.thingIDNumber, b.thingIDNumber)
            + ":" + Math.Max(a.thingIDNumber, b.thingIDNumber);

        public static List<Pawn> Partners(Pawn pawn) => pawn?.relations?.DirectRelations
            .Where(r => LovePartnerRelationUtility.IsLovePartnerRelation(r.def) && r.otherPawn != null
                && !r.otherPawn.Dead && !r.otherPawn.Destroyed).Select(r => r.otherPawn).Distinct().ToList()
            ?? new List<Pawn>();

        public static SharedPairValidity Validity(Pawn a, Pawn b)
        {
            if (a == null || b == null || a == b || a.Dead || b.Dead || a.Destroyed || b.Destroyed
                || a.Faction != Faction.OfPlayer || b.Faction != Faction.OfPlayer) return SharedPairValidity.Invalid;
            if (a.relations == null || b.relations == null) return SharedPairValidity.Unknown;
            if (!LovePartnerRelationUtility.LovePartnerRelationExists(a, b)
                || !LovePartnerRelationUtility.LovePartnerRelationExists(b, a)) return SharedPairValidity.Invalid;
            return BedUtility.WillingToShareBed(a, b) && BedUtility.WillingToShareBed(b, a)
                ? SharedPairValidity.Valid : SharedPairValidity.Invalid;
        }

        public static bool SlotFree(ColonistPromises state, int now) => state != null
            && state.Active == null && state.Request == null && state.SharedId == null && now >= state.CooldownUntilTick;

        public static bool TryEvaluate(Pawn pawn, RequestEvaluation result, out RequestCandidate candidate)
        {
            candidate = null;
            var partners = Partners(pawn);
            if (partners.Count == 0) return false;
            if (partners.Count != 1 || Partners(partners[0]).Count != 1
                || Partners(partners[0])[0] != pawn) { result.Reason = "CP_PairAmbiguous"; return true; }
            var partner = partners[0];
            result.Current = "CP_PairSelected".Translate(partner.LabelShort);
            if (!PromiseConditions.Eligible(partner)) { result.Reason = "CP_PairIneligible"; return true; }
            if (Validity(pawn, partner) != SharedPairValidity.Valid) { result.Reason = "CP_PairSharingForbidden"; return true; }
            if (pawn.story?.traits?.HasTrait(TraitDefOf.Ascetic) == true
                || partner.story?.traits?.HasTrait(TraitDefOf.Ascetic) == true)
            { result.Reason = "CP_PairConflictingNeeds"; return true; }
            if (!RequestMaintenance.Observable(partner) || pawn.Map != partner.Map)
            { result.Reason = "CP_PairUnobservable"; return true; }
            if (!DefDatabase<ThingDef>.AllDefsListForReading.Any(def => def.building != null && def.PlayerAcquirable
                && def.thingClass != null && typeof(Building_Bed).IsAssignableFrom(def.thingClass)
                && BedUtility.GetSleepingSlotsCount(def.size) >= 2
                && RestUtility.CanUseBedEver(pawn, def) && RestUtility.CanUseBedEver(partner, def)))
            { result.Reason = "CP_PairNoBed"; return true; }
            var component = GameComponent_Promises.Current;
            int now = Find.TickManager.TicksGame;
            if (component != null && (!SlotFree(component.PeekState(pawn), now) || !SlotFree(component.PeekState(partner), now)))
            { result.Reason = "CP_PairSlotBlocked"; return true; }
            var first = pawn.thingIDNumber < partner.thingIDNumber ? pawn : partner;
            var second = first == pawn ? partner : pawn;
            candidate = HousingRules.Candidate(first, second, result);
            result.Candidate = candidate;
            return true;
        }

        public static bool BedOwnersMatch(Building_Bed bed, Pawn a, Pawn b) => bed != null && bed.SleepingSlotsCount >= 2
            && !bed.Medical && bed.ForColonists && bed.OwnersForReading.Count == 2
            && bed.OwnersForReading.Contains(a) && bed.OwnersForReading.Contains(b);

        public static bool PrivateRoom(Room room, Pawn a, Pawn b) => room != null && room.ProperRoom
            && !room.PsychologicallyOutdoors && room.Role == RoomRoleDefOf.Bedroom
            && !room.ContainedBeds.Any(bed => bed.Medical) && !room.Owners.Any(owner => owner != a && owner != b);

        public static ConditionObservation Observe(PromiseRecord terms)
        {
            var result = new ConditionObservation();
            var a = terms.ParticipantA;
            var b = terms.ParticipantB;
            if (!PromiseConditions.Observable(a) || !PromiseConditions.Observable(b) || a.Map != b.Map
                || Validity(a, b) != SharedPairValidity.Valid) return result;
            var bed = a.ownership?.OwnedBed;
            if (bed != b.ownership?.OwnedBed || !BedOwnersMatch(bed, a, b) || !bed.Spawned || bed.Map != a.Map
                || !RestUtility.CanUseBedNow(bed, a, false) || !RestUtility.CanUseBedNow(bed, b, false)) return result;
            var room = bed.GetRoom();
            bool validRoom = terms.RequiresBarracks ? room != null && room.ProperRoom && !room.PsychologicallyOutdoors
                && room.Role == RoomRoleDefOf.Barracks && !room.ContainedBeds.Any(other => other.Medical) : PrivateRoom(room, a, b);
            if (!validRoom || terms.RequiresConstructedBed && CostListCalculator.CostListAdjusted(bed).Count == 0) return result;
            result.Thing = bed;
            result.Room = room;
            result.Satisfied = RoomStatDefOf.Impressiveness.GetScoreStageIndex(room.GetStat(RoomStatDefOf.Impressiveness)) >= terms.RoomStage;
            return result;
        }
    }
}
