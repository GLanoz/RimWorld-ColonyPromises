using Verse;

namespace ColonyPromises
{
    public sealed class SubstanceCondition : IPromiseCondition
    {
        public PromiseKind Kind => PromiseKind.Substance;
        public bool UsesContinuousHold => false;
        public bool ValidParameters(PromiseRecord r) => SubstanceNeeds.Standard(r.SubstanceDef) && r.RequiredTicks == 1
            && (r.SubstanceExperiment ? r.SubstanceEffect != null && SubstanceNeeds.ExperimentEffect(r.SubstanceDef) == r.SubstanceEffect
                : SubstanceNeeds.DoseProduct(r.SubstanceDef, r.SubstanceChemical));
        public ConditionObservation Observe(Pawn pawn, PromiseRecord r) => new ConditionObservation {
            Satisfied = r.SubstanceExperiment
                ? SubstanceNeeds.Known(pawn, GameComponent_Promises.Current?.PeekState(pawn), r.SubstanceDef)
                : SubstanceNeeds.HasDose(pawn, r.SubstanceChemical)
        };
        public string Describe(PromiseRecord r) => (r.SubstanceExperiment ? "CP_SubstanceTryAction" : "CP_SubstanceAddictionAction")
            .Translate(r.SubstanceExperiment ? r.SubstanceDef?.LabelCap.ToString() : r.SubstanceChemical?.LabelCap.ToString())
            + "\n" + "CP_SubstanceDetails".Translate(RequestRules.SubstanceCooldownDays);
    }
}
