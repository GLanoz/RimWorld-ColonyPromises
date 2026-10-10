using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyPromises
{
    public sealed class MainTabWindow_Promises : MainTabWindow
    {
        private Pawn pawn;
        private Vector2 scroll;
        private float contentHeight = 1000f;
        public override Vector2 RequestedTabSize => new Vector2(760f, 640f);

        public override void PostOpen()
        {
            base.PostOpen();
            var selected = Find.Selector.SingleSelectedThing as Pawn;
            var candidates = Candidates().ToList();
            if (PromiseConditions.Eligible(selected)) pawn = selected;
            else if (pawn == null || !candidates.Contains(pawn)) pawn = candidates.FirstOrDefault();
        }

        private static IEnumerable<Pawn> Candidates() => PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists
            .Where(PromiseConditions.Eligible).OrderBy(p => p.LabelShort);

        public override void DoWindowContents(Rect inRect)
        {
            var component = GameComponent_Promises.Current;
            if (component == null) return;
            Text.Font = GameFont.Small;
            var candidates = Candidates().ToList();
            if (pawn != null && !candidates.Contains(pawn)) pawn = candidates.FirstOrDefault();
            if (Widgets.ButtonText(new Rect(0, 0, inRect.width, 32), pawn?.LabelShort ?? "CP_SelectColonist".Translate().ToString()))
            {
                var options = candidates.Select(p =>
                {
                    var s = component.StateFor(p);
                    string status = s.Active != null ? "CP_StatusPromise".Translate().ToString()
                        : s.Request != null ? "CP_StatusRequest".Translate().ToString() : "CP_StatusWaiting".Translate().ToString();
                    return new FloatMenuOption("CP_ColonistChoice".Translate(p.LabelShort, status), () => { pawn = p; scroll = Vector2.zero; });
                }).ToList();
                if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
            }
            if (pawn == null)
            {
                Widgets.Label(new Rect(0, 45, inRect.width, 80), "CP_NoColonists".Translate());
                return;
            }
            var state = component.StateFor(pawn);
            Widgets.Label(new Rect(0, 40, inRect.width, 30), "CP_Trust".Translate(pawn.LabelShort, state.Trust, PromiseBalance.MaxTrust));
            var outer = new Rect(0, 78, inRect.width, inRect.height - 78);
            var view = new Rect(0, 0, outer.width - 20, contentHeight);
            Widgets.BeginScrollView(outer, ref scroll, view);
            float y = 0;
            float width = view.width;
            int now = Find.TickManager.TicksGame;
            if (state.Active != null)
            {
                var r = state.Active;
                Label(ref y, width, "CP_Active".Translate(("CP_Type_" + r.Kind).Translate()));
                Label(ref y, width, PromiseConditions.Get(r.Kind)?.Describe(r) ?? "CP_InvalidParameters".Translate().ToString());
                Label(ref y, width, "CP_Remaining".Translate(Math.Max(0, r.DeadlineTick - now) / (float)PromiseBalance.TicksPerDay));
                Label(ref y, width, "CP_Progress".Translate(r.ProgressTicks / (float)PromiseBalance.TicksPerHour, r.RequiredTicks / (float)PromiseBalance.TicksPerHour));
                if (r.FrozenConsequences) Label(ref y, width, Consequences(r));
                else Label(ref y, width, "CP_LegacyPromise".Translate());
                Label(ref y, width, "CP_DeadlineRule".Translate());
                if (Button(ref y, width, "CP_Withdraw".Translate()))
                {
                    var target = pawn;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "CP_WithdrawConfirm".Translate(r.FrozenConsequences ? r.WithdrawnMood : PromiseBalance.WithdrawnMood,
                            r.FrozenConsequences ? r.WithdrawnDays : PromiseBalance.WithdrawnMemoryDays,
                            r.FrozenConsequences ? r.WithdrawnTrust : PromiseBalance.WithdrawnTrust,
                            r.FrozenConsequences ? r.CooldownDays : PromiseBalance.CooldownDays), () => component.Withdraw(target, r)));
                }
            }
            else if (state.Request != null)
            {
                var request = state.Request;
                var r = request.Terms;
                PromiseConsequences.Snapshot(state, request, now);
                Label(ref y, width, "CP_RequestTitle".Translate(("CP_Type_" + r.Kind).Translate()));
                Label(ref y, width, request.ReasonKey.Translate(request.Expectation?.label ?? "", RequestRules.PersistentTicks / PromiseBalance.TicksPerDay));
                Label(ref y, width, PromiseConditions.Get(r.Kind)?.Describe(r) ?? "CP_MissingParameter".Translate().ToString());
                Label(ref y, width, "CP_ResponseTime".Translate(Math.Max(0, request.ExpiryTick - now) / (float)PromiseBalance.TicksPerDay, request.DeadlineDays));
                Label(ref y, width, Consequences(r));
                Label(ref y, width, "CP_DeadlineRule".Translate());
                string error = component.AcceptanceError(pawn, request);
                if (error != null) Label(ref y, width, error);
                if (error == null && Button(ref y, width, "CP_Accept".Translate()))
                {
                    var target = pawn;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "CP_AcceptConfirm".Translate(target.LabelShort, PromiseConditions.Get(r.Kind).Describe(r),
                            request.DeadlineDays, Consequences(r), "CP_DeadlineRule".Translate()),
                        () => component.TryAccept(target, request)));
                }
                if (Button(ref y, width, "CP_Decline".Translate()))
                {
                    var target = pawn;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "CP_DeclineConfirm".Translate(RequestRules.UnacceptedCooldownDays), () => component.Decline(target, request)));
                }
            }
            else
            {
                Label(ref y, width, "CP_NoRequest".Translate());
                if (now < state.CooldownUntilTick) Label(ref y, width, "CP_Cooldown".Translate((state.CooldownUntilTick - now) / (float)PromiseBalance.TicksPerDay));
                Label(ref y, width, "CP_RequestRules".Translate());
            }
            y += 16;
            Label(ref y, width, "CP_History".Translate());
            if (state.History.Count == 0) Label(ref y, width, "CP_NoHistory".Translate());
            foreach (var r in state.History)
            {
                Label(ref y, width, "CP_HistoryEntry".Translate(("CP_Type_" + r.Kind).Translate(), ("CP_Outcome_" + r.Outcome).Translate(),
                    (now - r.ResolvedTick) / (float)PromiseBalance.TicksPerDay, r.AppliedMood, r.AppliedTrust));
                Label(ref y, width, PromiseConditions.Get(r.Kind)?.Describe(r) ?? "CP_MissingParameter".Translate().ToString());
            }
            contentHeight = y + 8;
            Widgets.EndScrollView();
        }

        private static string Consequences(PromiseRecord r) => "CP_FixedConsequences".Translate(
            r.SuccessMood, r.SuccessDays, r.SuccessTrust, r.BrokenMood, r.BrokenDays, r.BrokenTrust,
            r.WithdrawnMood, r.WithdrawnDays, r.WithdrawnTrust, r.CooldownDays);

        private static void Label(ref float y, float width, string text)
        {
            float height = Text.CalcHeight(text, width) + 8;
            Widgets.Label(new Rect(0, y, width, height), text);
            y += height;
        }

        private static bool Button(ref float y, float width, string text)
        {
            bool clicked = Widgets.ButtonText(new Rect(0, y, width, 32), text);
            y += 38;
            return clicked;
        }
    }
}
