using Verse;

namespace ColonyPromises
{
    public sealed class SharedBedroomCommitment : IExposable
    {
        public string Id;
        public Pawn First;
        public Pawn Second;
        public ColonistRequest Request;
        public PromiseRecord Active;
        public PromiseRecord FirstEffects;
        public PromiseRecord SecondEffects;
        public bool Resolved;
        public PromiseRecord Terms => Active ?? Request?.Terms;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_References.Look(ref First, "first");
            Scribe_References.Look(ref Second, "second");
            Scribe_Deep.Look(ref Request, "request");
            Scribe_Deep.Look(ref Active, "active");
            Scribe_Deep.Look(ref FirstEffects, "firstEffects");
            Scribe_Deep.Look(ref SecondEffects, "secondEffects");
            Scribe_Values.Look(ref Resolved, "resolved");
        }
    }
}
