using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public sealed class ChoiceLetter_Request : ChoiceLetter
    {
        public Pawn Pawn;
        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                yield return new DiaOption("CP_OpenPromises".Translate())
                {
                    action = () => { MainTabWindow_Promises.OpenFor(Pawn); Find.LetterStack.RemoveLetter(this); },
                    resolveTree = true
                };
                yield return Option_Close;
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref Pawn, "requestPawn");
        }
    }

    public static class RequestNotices
    {
        public static bool TakeAnnouncement(ColonistRequest request)
        {
            if (request.Announced) return false;
            request.Announced = true;
            return true;
        }
        public static bool TakeReminder(ColonistRequest request, int now, bool enabled)
        {
            if (!enabled || request.ReminderSent || now >= request.ExpiryTick
                || now < request.ExpiryTick - TrustRules.ReminderLeadTicks) return false;
            request.ReminderSent = true;
            return true;
        }
        public static void Send(ColonistPromises state, bool reminder)
        {
            var request = state.Request;
            string names = state.Shared == null ? state.Pawn.LabelShort : SharedPromiseText.Names(state.Shared.First, state.Shared.Second);
            var letter = (ChoiceLetter_Request)LetterMaker.MakeLetter(
                (reminder ? "CP_RequestReminderTitle" : "CP_RequestLetterTitle").Translate(names),
                "CP_RequestLetterText".Translate(names, PromiseConditions.Get(request.Terms.Kind).Describe(request.Terms),
                    PromiseText.Number(System.Math.Max(0, request.ExpiryTick - Find.TickManager.TicksGame) / (float)PromiseBalance.TicksPerDay)),
                DefDatabase<LetterDef>.GetNamed("CP_RequestLetter"), new LookTargets(state.Pawn));
            letter.Pawn = state.Pawn;
            Find.LetterStack.ReceiveLetter(letter);
        }
    }
}
