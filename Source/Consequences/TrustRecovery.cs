using System.Linq;

namespace ColonyPromises
{
    public static class TrustRecovery
    {
        public static bool Available(ColonistPromises state, ColonistRequest request, int now) =>
            request?.Terms != null && request.RecoveryEnabled && state.Request == request && state.Active == null
            && now >= request.OfferedTick && now <= request.ExpiryTick && state.Trust <= TrustRules.RecoveryThreshold
            && now >= state.NextUnpromisedRecoveryTick
            && !(state.RecentRecoveries ?? new System.Collections.Generic.List<SuccessStamp>()).Any(s =>
                s.Kind == request.Terms.Kind && now - s.Tick <= TrustRules.RecoveryRepeatDays * PromiseBalance.TicksPerDay);

        public static int Apply(ColonistPromises state, ColonistRequest request, int now)
        {
            var r = request?.Terms;
            if (state.Request != request || r == null || now > request.ExpiryTick
                || !Available(state, request, now) || r.RequiredTicks <= 0 || r.ProgressTicks < r.RequiredTicks) return 0;
            if (r.Kind == PromiseKind.Leisure)
            {
                if (state.Pawn?.needs?.joy == null || state.Pawn.needs.joy.CurLevel < RequestRules.LowJoy) return 0;
            }
            else if (!r.PreviouslySatisfied) return 0;
            int before = state.Trust;
            state.Trust = PromiseBalance.ClampTrust(before + TrustRules.UnpromisedRecoveryTrust);
            state.NextUnpromisedRecoveryTick = now + TrustRules.RecoveryCooldownDays * PromiseBalance.TicksPerDay;
            if (state.RecentRecoveries == null) state.RecentRecoveries = new System.Collections.Generic.List<SuccessStamp>();
            state.RecentRecoveries.RemoveAll(s => now - s.Tick > TrustRules.RecoveryRepeatDays * PromiseBalance.TicksPerDay);
            state.RecentRecoveries.Add(new SuccessStamp { Kind = r.Kind, Tick = now });
            return state.Trust - before;
        }
    }
}
