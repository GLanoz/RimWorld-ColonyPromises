using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public sealed class DevRequestType
    {
        public string Key;
        public PromiseKind Kind;
        public string Cause;
        public bool Shared;
        public bool Barracks;
        public bool BuiltBed;
    }

    public static class DevRequests
    {
        // Test variants use the existing condition registry and persisted term fields.
        public static IEnumerable<DevRequestType> Types => Variants.Concat(EquipmentVariants)
            .Where(t => PromiseConditions.Get(t.Kind) != null);
        private static readonly DevRequestType[] EquipmentVariants = EquipmentNeeds.Causes.Select(c =>
            new DevRequestType { Key = c, Cause = c, Kind = PromiseKind.Equipment }).ToArray();
        private static readonly DevRequestType[] Variants = {
            new DevRequestType { Key = "Private", Kind = PromiseKind.Bedroom },
            new DevRequestType { Key = "Improve", Kind = PromiseKind.Bedroom },
            new DevRequestType { Key = "BuiltBed", Kind = PromiseKind.Bedroom, BuiltBed = true },
            new DevRequestType { Key = "Barracks", Kind = PromiseKind.Bedroom, Barracks = true },
            new DevRequestType { Key = "Shared", Kind = PromiseKind.Bedroom, Shared = true },
            new DevRequestType { Key = "SharedBarracks", Kind = PromiseKind.Bedroom, Shared = true, Barracks = true },
            new DevRequestType { Key = "Leisure", Kind = PromiseKind.Leisure },
            new DevRequestType { Key = "SubstanceTry", Cause = "SubstanceTry", Kind = PromiseKind.Substance },
            new DevRequestType { Key = "SubstanceAddiction", Cause = "SubstanceAddiction", Kind = PromiseKind.Substance }
        };
        public static bool Occupied(ColonistPromises state) => state?.Active != null || state?.Request != null || state?.SharedId != null;

        public static bool Matches(DevRequestType type, RequestCandidate candidate, Pawn pawn) => candidate != null
            && candidate.Terms.Kind == type.Kind && (type.Kind == PromiseKind.Leisure
            || type.Kind == PromiseKind.Equipment && candidate.Terms.EquipmentCause == type.Cause
            || type.Kind == PromiseKind.Substance && candidate.Terms.SubstanceExperiment == (type.Cause == "SubstanceTry")
            || type.Kind == PromiseKind.Bedroom && candidate.Terms.IsShared == type.Shared
                && candidate.Terms.RequiresBarracks == type.Barracks
                && (type.BuiltBed ? candidate.Terms.RequiresConstructedBed
                    : type.Shared || type.Barracks || (type.Key == "Improve") == (pawn.ownership?.OwnedRoom?.Role == RoomRoleDefOf.Bedroom)));

        public static PromiseRecord Manual(DevRequestType type, Pawn pawn, Pawn partner = null) => new PromiseRecord {
            Kind = type.Kind, HousingBased = type.Kind == PromiseKind.Bedroom,
            RequiresBarracks = type.Barracks, ParticipantA = type.Shared ? pawn : null, ParticipantB = partner,
            RequiresConstructedBed = type.BuiltBed,
            NeedBasedEquipment = type.Kind == PromiseKind.Equipment, EquipmentCause = type.Cause,
            RequireCoverage = type.Cause == "Naked", RequireMelee = type.Cause == "BrawlerUnhappy" || type.Cause == "WeaponMelee",
            QualityOptional = type.Cause != "WeaponUpgrade", MinimumQuality = QualityCategory.Normal,
            MinimumHitPointsFraction = type.Cause == "ApparelDamaged" ? RequestRules.ReplacementFraction : 0,
            RoomStage = RequestRules.BedroomBaseStage,
            SubstanceExperiment = type.Cause == "SubstanceTry",
            RequiredTicks = type.Kind == PromiseKind.Substance ? 1 : type.Kind == PromiseKind.Leisure ? RequestRules.MinHours * PromiseBalance.TicksPerHour : PromiseBalance.HoldTicks,
            DevTest = true, DevManual = true
        };

        public static bool ValidTerms(Pawn pawn, PromiseRecord r)
        {
            if (!PromiseConditions.Eligible(pawn) || !RequestMaintenance.Observable(pawn)
                || PromiseConditions.Get(r.Kind)?.ValidParameters(r) != true || r.RequiredTicks <= 0
                || float.IsNaN(r.MinimumHitPointsFraction) || r.MinimumHitPointsFraction < 0 || r.MinimumHitPointsFraction > 1) return false;
            if (r.IsShared && (!PromiseConditions.Eligible(r.ParticipantB)
                || SharedBedroomRules.Validity(r.ParticipantA, r.ParticipantB) != SharedPairValidity.Valid
                || !RequestMaintenance.Observable(r.ParticipantB) || r.ParticipantA.Map != r.ParticipantB.Map)) return false;
            if (r.IsShared && !SharedBedroomRules.HasUsableBedDefinition(r.ParticipantA, r.ParticipantB)) return false;
            if (r.Kind == PromiseKind.Leisure) return pawn.needs?.joy != null;
            if (r.Kind == PromiseKind.Substance) return SubstanceNeeds.Allowed(pawn, r.SubstanceDef, r.SubstanceExperiment)
                && (r.SubstanceExperiment || SubstanceNeeds.Addictions(pawn).Any(a => a.Chemical == r.SubstanceChemical));
            return r.Kind != PromiseKind.Equipment || EquipmentNeeds.UsableDefinition(pawn, r)
                && (!EquipmentNeeds.AcquisitionCause(r.EquipmentCause)
                    || EquipmentNeeds.WeaponTypeMatches(r.EquipmentDef, r.EquipmentCause == "WeaponRanged")
                        && !(r.EquipmentDef.IsRangedWeapon && pawn.story?.traits?.HasTrait(TraitDefOf.Brawler) == true))
                && DlcCompatibility.ApparelDefinition(pawn, r.EquipmentDef)
                && (!r.RequireCoverage || EquipmentNeeds.CoversMissingBasics(pawn, r.EquipmentDef));
        }
    }

    public sealed partial class GameComponent_Promises
    {
        public string CreateDevRequest(Pawn pawn, DevRequestType type, PromiseRecord manual = null)
        {
            if (!Prefs.DevMode) return "CP_DevDisabled".Translate();
            if (!DevRequests.Types.Contains(type)) return "CP_InvalidParameters".Translate();
            if (!PromiseConditions.Eligible(pawn)) return (PromiseConditions.EligibilityReason(pawn) ?? "CP_Ineligible").Translate();
            var state = StateFor(pawn);
            if (DevRequests.Occupied(state)) return "CP_DevOccupied".Translate();
            RequestCandidate candidate;
            if (manual == null)
            {
                var evaluation = RequestGenerator.Evaluate(pawn, type.Kind, true, type.Cause);
                if (!DevRequests.Matches(type, evaluation.Candidate, pawn))
                    return (evaluation.Reason ?? "CP_DevNoSubtype").Translate();
                candidate = evaluation.Candidate;
                candidate.Terms.DevTest = true;
            }
            else
            {
                if (!manual.DevTest || !manual.DevManual || manual.Kind != type.Kind || manual.IsShared != type.Shared
                    || manual.IsShared && manual.ParticipantA != pawn || !DevRequests.ValidTerms(pawn, manual))
                    return "CP_InvalidParameters".Translate();
                candidate = new RequestCandidate { Terms = manual, ReasonKey = "CP_DevManualReason" };
            }
            if (!DevRequests.ValidTerms(pawn, candidate.Terms)) return "CP_InvalidParameters".Translate();
            int now = Find.TickManager.TicksGame;
            if (candidate.Terms.IsShared)
            {
                if (DevRequests.Occupied(StateFor(candidate.Terms.ParticipantA)) || DevRequests.Occupied(StateFor(candidate.Terms.ParticipantB)))
                    return "CP_DevOccupied".Translate();
                return CreateShared(candidate, now) ? null : "CP_InvalidParameters".Translate().ToString();
            }
            state.Request = RequestGenerator.Offer(candidate, now);
            if (manual != null) state.Request.RecoveryEnabled = false;
            open[pawn] = state;
            if (RequestNotices.TakeAnnouncement(state.Request)) RequestNotices.Send(state, false);
            return null;
        }

        public bool ClearDevRecord(Pawn pawn, PromiseRecord expected)
        {
            if (!Prefs.DevMode || pawn == null || expected == null) return false;
            var state = PeekState(pawn);
            if ((state?.Active ?? state?.Request?.Terms) != expected) return false;
            var shared = state.Shared;
            var affected = shared == null ? new[] { state } : new[] { StateFor(shared.First), StateFor(shared.Second) };
            if (shared != null) { shared.Resolved = true; shared.Active = null; shared.Request = null; sharedBedrooms.Remove(shared); }
            foreach (var member in affected)
            {
                member.Active = null; member.Request = null; member.Shared = null; member.SharedId = null;
                active.Remove(member.Pawn); open.Remove(member.Pawn);
            }
            return true;
        }
    }
}
