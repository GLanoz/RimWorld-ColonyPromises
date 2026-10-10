using UnityEngine;
using Verse;

namespace ColonyPromises
{
    public sealed class ColonyPromisesSettings : ModSettings
    {
        public bool RemindersEnabled = true;
        public override void ExposeData() => Scribe_Values.Look(ref RemindersEnabled, "remindersEnabled", true);
    }
}
