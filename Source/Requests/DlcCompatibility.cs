using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public static class DlcCompatibility
    {
        public static bool RoyalCause(string name) => name == "TitleBedroomRequirementsNotMet" || name == "TitleNoPersonalBedroom";

        public static bool CaptureBedroom(Pawn pawn, List<BedroomRequirement> destination)
        {
            if (pawn.royalty == null || !ModsConfig.RoyaltyActive) return true;
            var title = pawn.royalty.HighestTitleWithBedroomRequirements();
            if (title == null) return true;
            foreach (var requirement in title.def.GetBedroomRequirements(pawn))
            {
                if (requirement.Disabled(pawn.ownership?.OwnedRoom, pawn)) continue;
                var snapshot = BedroomRequirement.Capture(requirement);
                if (snapshot == null) return false;
                destination.Add(snapshot);
            }
            return true;
        }

        public static bool BedroomMet(Room room, Pawn pawn, PromiseRecord terms) => terms.BedroomRequirements == null
            || terms.BedroomRequirements.Count == 0 || !ModsConfig.RoyaltyActive
            || terms.BedroomRequirements.All(r => r.Rebuild()?.Met(room, pawn) == true);

        public static string BedroomText(PromiseRecord terms)
        {
            if (terms.BedroomRequirements == null || terms.BedroomRequirements.Count == 0 || !ModsConfig.RoyaltyActive) return "";
            return "\n" + "CP_RoyalBedroomConditions".Translate(string.Join("; ", terms.BedroomRequirements
                .Select(r => r.Rebuild()?.Label(null) ?? "CP_MissingParameter".Translate().ToString()).Distinct()));
        }

        public static bool ApparelDefinition(Pawn pawn, ThingDef target)
        {
            if (!target.IsApparel) return true;
            if (ModsConfig.IdeologyActive && pawn.Ideo != null)
            {
                if (target.apparel.countsAsClothingForNudity && IdeoUtility.IdeoPrefersNudityForGender(pawn.Ideo, pawn.gender)) return false;
                foreach (var precept in pawn.Ideo.PreceptsListForReading.OfType<Precept_Apparel>())
                {
                    if (precept.apparelDef == null || precept.TargetGender != Gender.None && precept.TargetGender != pawn.gender) continue;
                    if (ModsConfig.RoyaltyActive && pawn.royalty?.AllTitlesForReading.Any(t => t.def.requiredApparel?.Any(r =>
                        ApparelUtility.IsRequirementActive(r, ApparelRequirementSource.Title, pawn, out _)) == true) == true) continue;
                    if (precept.apparelDef != target && !ApparelUtility.CanWearTogether(target, precept.apparelDef, pawn.RaceProps.body)) return false;
                }
            }
            if (!ModsConfig.RoyaltyActive && !ModsConfig.IdeologyActive) return true;
            // A replacement must not remove the only currently satisfying royal/role garment.
            var remaining = pawn.apparel.WornApparel.Where(a => a.def != target
                && ApparelUtility.CanWearTogether(target, a.def, pawn.RaceProps.body)).Select(a => a.def).Concat(new[] { target }).ToList();
            foreach (var source in pawn.apparel.AllRequirements)
            {
                if (source.sourceRole != null && !ModsConfig.IdeologyActive) continue;
                if (source.sourceTitle != null && (!ModsConfig.RoyaltyActive || pawn.royalty?.allowApparelRequirements != true)) continue;
                var requirement = source.requirement;
                if (!ApparelUtility.IsRequirementActive(requirement, source.sourceTitle != null
                    ? ApparelRequirementSource.Title : ApparelRequirementSource.Role, pawn, out _)) continue;
                if (pawn.apparel.WornApparel.Any(a => requirement.RequiredForPawn(pawn, a.def))
                    && !remaining.Any(d => requirement.RequiredForPawn(pawn, d))) return false;
            }
            return true;
        }

        public static bool AvoidHumanLeather(Pawn pawn)
        {
            if (!ModsConfig.IdeologyActive || pawn.Ideo == null) return false;
            return new[] { "HumanLeatherApparel_Abhorrent", "HumanLeatherApparel_Horrible", "HumanLeatherApparel_Disapproved" }
                .Select(DefDatabase<ThoughtDef>.GetNamedSilentFail).Where(d => d != null)
                .Any(d => !ThoughtUtility.ThoughtNullified(pawn, d)
                    && pawn.Ideo.GetAllPreceptsAllowingSituationalThought(d).Count > 0);
        }

        public static bool SameBedroomRequirements(PromiseRecord a, PromiseRecord b) =>
            RequirementSignature(a.BedroomRequirements) == RequirementSignature(b.BedroomRequirements);

        public static string RequirementSignature(IEnumerable<BedroomRequirement> requirements) => requirements == null ? ""
            : string.Join("/", requirements.Select(r => r.Kind + ":" + r.Value + ":" + string.Join(",", r.Names)));

        public static bool PendingCompatible(Pawn pawn, PromiseRecord terms)
        {
            if (terms.Kind == PromiseKind.Bedroom)
            {
                var current = new List<BedroomRequirement>();
                if (!CaptureBedroom(terms.IsShared ? terms.ParticipantA : pawn, current)
                    || terms.IsShared && !CaptureBedroom(terms.ParticipantB, current)) return false;
                return RequirementSignature(current) == RequirementSignature(terms.BedroomRequirements);
            }
            return terms.Kind != PromiseKind.Equipment || ApparelDefinition(pawn, terms.EquipmentDef)
                && (!terms.EquipmentDef.IsApparel || !AvoidHumanLeather(pawn) || terms.AvoidHumanLeather);
        }
    }
}
