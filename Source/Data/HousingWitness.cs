using Verse;

namespace ColonyPromises
{
    public sealed class HousingWitness : IExposable
    {
        public string Cause;
        public float Mood;
        public Pawn Subject;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Cause, "cause");
            Scribe_Values.Look(ref Mood, "mood");
            Scribe_References.Look(ref Subject, "subject");
        }
    }
}
