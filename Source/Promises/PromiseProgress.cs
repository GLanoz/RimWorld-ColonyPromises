using Verse;

namespace ColonyPromises
{
    public static class PromiseProgress
    {
        public static void Observe(PromiseRecord record, IPromiseCondition condition, Pawn pawn, int now)
        {
            if (condition?.UsesContinuousHold == true)
                UpdateHold(record, condition.Observe(pawn, record), now);
            record.LastCheckTick = now;
        }

        public static void UpdateHold(PromiseRecord r, ConditionObservation observation, int now)
        {
            if (!observation.Satisfied) r.ResetHold();
            else
            {
                bool same = r.PreviouslySatisfied && observation.Thing == r.StableThing
                    && (r.Kind != PromiseKind.Bedroom || observation.Room == r.ObservedRoom);
                if (!same) r.ProgressTicks = 0;
                else r.ProgressTicks += ProgressMath.VerifiedDelta(r.LastCheckTick, now, r.DeadlineTick);
                r.StableThing = observation.Thing;
                r.ObservedRoom = observation.Room;
                r.PreviouslySatisfied = true;
            }
        }
    }
}
