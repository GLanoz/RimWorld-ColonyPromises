using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ColonyPromises
{
    public static class SharedPromiseText
    {
        public static Pawn PartnerFor(SharedBedroomCommitment shared, Pawn selected) => shared == null ? null
            : selected == shared.First ? shared.Second : selected == shared.Second ? shared.First : null;

        public static string RequestReason(ColonistRequest request)
        {
            if (request?.Terms?.IsShared != true) return null;
            var terms = request.Terms;
            var current = new List<HousingIssue>();
            if (terms.ParticipantA != null && terms.ParticipantB != null
                && RequestMaintenance.Observable(terms.ParticipantA) && RequestMaintenance.Observable(terms.ParticipantB)
                && terms.ParticipantA.Map == terms.ParticipantB.Map)
            {
                current.AddRange(HousingRules.Evaluate(terms.ParticipantA));
                current.AddRange(HousingRules.Evaluate(terms.ParticipantB));
            }
            return FormatRequestReason(request, current);
        }

        internal static string FormatRequestReason(ColonistRequest request, IEnumerable<HousingIssue> current)
        {
            var terms = request.Terms;
            var participants = new[] { terms.ParticipantA, terms.ParticipantB }.Where(p => p != null).ToList();
            var recorded = request.HousingReasons?.Where(i => i.Subject != null && participants.Contains(i.Subject)
                && i.Mood < 0 && HousingRules.SupportedCause(i.Cause)).ToList();
            if (recorded == null || recorded.Count == 0) return "CP_CardReasonUnknown".Translate();
            var reasons = new List<HousingWitness>(recorded);
            foreach (var issue in current.Where(i => participants.Contains(i.Pawn) && i.Current && i.Supported
                && HousingRules.SupportedCause(i.Cause) && i.MinimumStage >= 0 && terms.RoomStage >= i.MinimumStage
                && (i.Cause != "SleptOnGround" || terms.RequiresConstructedBed)
                && !recorded.Any(r => r.Subject == i.Pawn && r.Cause == i.Cause)))
                reasons.Add(new HousingWitness { Subject = issue.Pawn, Cause = issue.Cause, Mood = issue.Mood });
            return string.Join(" ", reasons.GroupBy(i => i.Cause).Take(2)
                .Select(g => PromiseCardText.Cause(g.Key, g.Select(i => i.Subject))));
        }

        public static string Names(Pawn a, Pawn b) => "CP_PairNames".Translate(
            a?.LabelShort ?? "CP_MissingColonist".Translate().ToString(),
            b?.LabelShort ?? "CP_MissingColonist".Translate().ToString());

        public static string Consequences(GameComponent_Promises component, ColonistPromises state,
            PromiseRecord terms, ColonistRequest request, int now)
        {
            return Effects(component, state, terms, request, now, PromiseText.Consequences);
        }

        public static string Rewards(GameComponent_Promises component, ColonistPromises state,
            PromiseRecord terms, ColonistRequest request, int now) => Effects(component, state, terms, request, now, PromiseCardText.Reward);

        public static string Penalties(GameComponent_Promises component, ColonistPromises state,
            PromiseRecord terms, ColonistRequest request, int now) => Effects(component, state, terms, request, now, PromiseCardText.Penalties);

        private static string Effects(GameComponent_Promises component, ColonistPromises state,
            PromiseRecord terms, ColonistRequest request, int now, System.Func<PromiseRecord, string> format)
        {
            if (state.Shared == null) return format(terms);
            var shared = state.Shared;
            var a = request != null ? PromiseConsequences.Preview(component.PeekState(shared.First), request, now) : shared.FirstEffects;
            var b = request != null ? PromiseConsequences.Preview(component.PeekState(shared.Second), request, now) : shared.SecondEffects;
            return "CP_PersonalEffects".Translate(shared.First.LabelShort, format(a)).ToString()
                + "\n" + "CP_PersonalEffects".Translate(shared.Second.LabelShort, format(b)).ToString();
        }

        public static string WithdrawConfirmation(ColonistPromises state, PromiseRecord record)
        {
            if (state.Shared == null) return "CP_WithdrawConfirm".Translate(
                record.FrozenConsequences ? record.WithdrawnMood : PromiseBalance.WithdrawnMood,
                record.FrozenConsequences ? record.WithdrawnDays : PromiseBalance.WithdrawnMemoryDays,
                record.FrozenConsequences ? record.WithdrawnTrust : PromiseBalance.WithdrawnTrust,
                record.FrozenConsequences ? record.CooldownDays : PromiseBalance.CooldownDays);
            var shared = state.Shared;
            return "CP_SharedWithdrawConfirm".Translate(Names(shared.First, shared.Second),
                EffectLine(shared.First, shared.FirstEffects), EffectLine(shared.Second, shared.SecondEffects));
        }

        private static string EffectLine(Pawn pawn, PromiseRecord r) => "CP_PersonalWithdrawal".Translate(
            pawn.LabelShort, PromiseText.Signed(r.WithdrawnMood), r.WithdrawnDays,
            PromiseText.Signed(r.WithdrawnTrust), r.CooldownDays);
    }
}
