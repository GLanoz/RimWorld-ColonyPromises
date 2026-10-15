using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    // Read-only, evaluation-local snapshot: never retained across ingestion, loading or Def changes.
    public sealed class SubstanceKnowledge
    {
        public IReadOnlyList<ThingDef> Drugs { get; }
        private readonly HashSet<string> known;
        public SubstanceKnowledge(Pawn pawn, ColonistPromises state)
        {
            Drugs = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsDrug).ToList();
            known = new HashSet<string>(state?.KnownSubstances ?? Enumerable.Empty<string>());
            foreach (var addiction in SubstanceNeeds.Addictions(pawn))
                if (addiction.Chemical != null) known.Add("chemical:" + addiction.Chemical.defName);
            foreach (var drug in Drugs)
                if (pawn.drugs?.HasEverTaken(drug) == true) known.Add(SubstanceNeeds.Key(drug));
            foreach (var group in Drugs.Select(d => new { Def = d, Effect = SubstanceNeeds.ExperimentEffect(d) })
                .Where(d => d.Effect != null).GroupBy(d => d.Effect))
            {
                var keys = group.Select(d => SubstanceNeeds.Key(d.Def)).Distinct().ToList();
                if (keys.Count == 1 && pawn.health?.hediffSet?.HasHediff(group.Key) == true) known.Add(keys[0]);
            }
        }
        public bool Known(ThingDef def) => known.Contains(SubstanceNeeds.Key(def));
    }
}
