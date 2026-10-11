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
        private bool colonistsTab;
        private bool moreExpanded;
        private bool diagnosticsExpanded;
        private Vector2 requestsScroll;
        private Vector2 colonistsScroll;
        private Vector2 detailsScroll;
        private float colonistHeight;
        private float detailHeight;
        public override Vector2 RequestedTabSize => FitSize(UI.screenWidth, UI.screenHeight);

        internal static Vector2 FitSize(float screenWidth, float screenHeight) => new Vector2(
            Math.Max(1, Math.Min(1080, screenWidth - 32)), Math.Max(1, Math.Min(760, screenHeight - 90)));

        internal static IEnumerable<ColonistPromises> ByExpiry(IEnumerable<ColonistPromises> requests) =>
            requests.OrderBy(s => s.Request.ExpiryTick).ThenBy(s => s.Pawn.thingIDNumber);

        internal static string StatusKey(ColonistPromises state, int now, bool visibleRequest) =>
            state.Active != null ? "CP_ListPromise" : visibleRequest ? "CP_ListRequest"
            : now < state.CooldownUntilTick ? "CP_ListWaiting" : "CP_ListNone";

        public static void OpenFor(Pawn target)
        {
            var def = DefDatabase<MainButtonDef>.GetNamed("CP_Promises");
            Find.MainTabsRoot.SetCurrentTab(def);
            ((MainTabWindow_Promises)def.TabWindow).SelectPawn(target);
        }

        public void SelectPawn(Pawn target)
        {
            colonistsTab = true;
            pawn = target;
            detailsScroll = Vector2.zero;
            detailHeight = 0;
            moreExpanded = diagnosticsExpanded = false;
        }

        private bool CanKeepSelection(List<Pawn> candidates) => pawn != null
            && !pawn.Dead && !pawn.Destroyed
            && (candidates.Contains(pawn) || (Prefs.DevMode && pawn.Spawned));

        private void ValidateSelection(List<Pawn> candidates)
        {
            if (!CanKeepSelection(candidates)) SelectPawnWithoutNavigation(candidates.FirstOrDefault());
        }

        private void SelectPawnWithoutNavigation(Pawn target)
        {
            bool previousTab = colonistsTab;
            SelectPawn(target);
            colonistsTab = previousTab;
        }

        public override void PostOpen()
        {
            base.PostOpen();
            var candidates = Candidates().ToList();
            if (pawn == null)
            {
                var selected = Find.Selector.SingleSelectedThing as Pawn;
                if (PromiseConditions.Eligible(selected) || (Prefs.DevMode && selected != null && !selected.Dead))
                    SelectPawnWithoutNavigation(selected);
            }
            ValidateSelection(candidates);
        }

        private static IEnumerable<Pawn> Candidates() => PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists
            .Where(PromiseConditions.Eligible).OrderBy(p => p.LabelShort);

        public override void DoWindowContents(Rect inRect)
        {
            var component = GameComponent_Promises.Current;
            if (component == null) return;
            Text.Font = GameFont.Small;
            var candidates = Candidates().ToList();
            ValidateSelection(candidates);
            var bodyRect = new Rect(inRect.x, inRect.y + 34, inRect.width, Math.Max(1, inRect.height - 34));
            TabDrawer.DrawTabs(bodyRect, new List<TabRecord> {
                new TabRecord("CP_TabRequests".Translate(), () => colonistsTab = false, !colonistsTab),
                new TabRecord("CP_TabColonists".Translate(), () => colonistsTab = true, colonistsTab)
            });
            int now = Find.TickManager.TicksGame;
            if (!colonistsTab) DrawRequests(bodyRect, component, now);
            else DrawColonists(bodyRect, candidates, component, now);
        }

        private void DrawRequests(Rect rect, GameComponent_Promises component, int now)
        {
            var requests = ByExpiry(component.OpenRequests(now)).ToList();
            if (requests.Count == 0)
            {
                Widgets.Label(rect, "CP_NoPendingRequests".Translate());
                return;
            }
            float width = Math.Max(1, rect.width - 20);
            float height = requests.Sum(s => RequestRowHeight(s, now, width));
            Widgets.BeginScrollView(rect, ref requestsScroll, new Rect(0, 0, width, height));
            float y = 0;
            foreach (var state in requests)
            {
                string text = RequestRow(state, now);
                float rowHeight = RequestRowHeight(state, now, width);
                var row = new Rect(0, y, width, rowHeight - 6);
                Widgets.DrawHighlightIfMouseover(row);
                Widgets.Label(new Rect(8, y + 6, Math.Max(1, width - 16), row.height - 12), text);
                if (Widgets.ButtonInvisible(row)) SelectPawn(state.Pawn);
                y += rowHeight;
            }
            Widgets.EndScrollView();
        }

        private static string RequestRow(ColonistPromises state, int now) =>
            "CP_RequestSummaryRow".Translate(state.Shared == null ? state.Pawn.LabelShort : SharedPromiseText.Names(state.Shared.First, state.Shared.Second),
                PromiseText.Type(state.Request.Terms), ShortRequirement(state.Request.Terms),
                PromiseText.Number(Math.Max(0, state.Request.ExpiryTick - now) / (float)PromiseBalance.TicksPerDay));

        private static float RequestRowHeight(ColonistPromises state, int now, float width) =>
            Math.Max(80, Text.CalcHeight(RequestRow(state, now), Math.Max(1, width - 16)) + 24);

        internal static string ShortRequirement(PromiseRecord record)
        {
            if (record.Kind == PromiseKind.Bedroom)
                return (record.RequiresBarracks ? "CP_ShortBarracks" : record.IsShared ? "CP_ShortSharedBedroom" : "CP_ShortBedroom").Translate(BedroomCondition.RoomLabel(record.RoomStage));
            if (record.Kind == PromiseKind.Leisure)
                return "CP_ShortLeisure".Translate(PromiseText.Number(record.RequiredTicks / (float)PromiseBalance.TicksPerHour));
            return "CP_ShortEquipment".Translate(record.EquipmentDef?.LabelCap.ToString() ?? "CP_MissingParameter".Translate().ToString(),
                record.MinimumQuality.GetLabel()).ToString()
                + (record.MinimumHitPointsFraction > 0 ? " " + "CP_ShortDurability".Translate(
                    PromiseText.Number(record.MinimumHitPointsFraction * 100)).ToString() : "");
        }

        private void DrawColonists(Rect rect, List<Pawn> candidates, GameComponent_Promises component, int now)
        {
            var shown = new List<Pawn>(candidates);
            if (Prefs.DevMode && pawn != null && !shown.Contains(pawn)) shown.Add(pawn);
            float listWidth = Math.Min(260, rect.width * 0.28f);
            var listRect = new Rect(rect.x, rect.y, listWidth, rect.height);
            float width = Math.Max(1, listWidth - 20);
            var visible = new HashSet<Pawn>(component.OpenRequests(now).Select(s => s.Pawn));
            foreach (var entry in component.OpenRequests(now).Where(s => s.Shared != null)) visible.Add(entry.Shared.Second);
            var labels = shown.Select(p => new KeyValuePair<Pawn, string>(p,
                p.LabelShort + "\n" + StatusKey(component.PeekState(p), now, visible.Contains(p)).Translate())).ToList();
            colonistHeight = labels.Sum(entry => Math.Max(64, Text.CalcHeight(entry.Value, Math.Max(1, width - 16)) + 20));
            Widgets.BeginScrollView(listRect, ref colonistsScroll, new Rect(0, 0, width, colonistHeight));
            float y = 0;
            foreach (var entry in labels)
            {
                float height = Math.Max(64, Text.CalcHeight(entry.Value, Math.Max(1, width - 16)) + 20);
                var row = new Rect(0, y, width, height - 4);
                if (entry.Key == pawn) Widgets.DrawHighlightSelected(row);
                Widgets.DrawHighlightIfMouseover(row);
                Widgets.Label(new Rect(8, y + 6, Math.Max(1, width - 16), row.height - 12), entry.Value);
                if (Widgets.ButtonInvisible(row)) SelectPawn(entry.Key);
                y += height;
            }
            Widgets.EndScrollView();
            var detailRect = new Rect(rect.x + listWidth + 12, rect.y, Math.Max(1, rect.width - listWidth - 12), rect.height);
            if (pawn == null) Widgets.Label(detailRect, "CP_NoColonists".Translate());
            else DrawDetails(detailRect, component, now);
        }

        private void DrawDetails(Rect rect, GameComponent_Promises component, int now)
        {
            var state = component.PeekState(pawn);
            float width = Math.Max(1, rect.width - 20);
            Widgets.BeginScrollView(rect, ref detailsScroll, new Rect(0, 0, width, Math.Max(rect.height, detailHeight)));
            float y = 0;
            Label(ref y, width, "CP_Trust".Translate(pawn.LabelShort, state.Trust, PromiseBalance.MaxTrust));
            if (state.Shared != null)
            {
                var other = state.Pawn == state.Shared.First ? state.Shared.Second : state.Shared.First;
                Label(ref y, width, ((state.Active ?? state.Request?.Terms)?.RequiresBarracks == true
                    ? "CP_SharedBarracksWith" : "CP_SharedWith").Translate(other.LabelShort));
                if (Button(ref y, width, "CP_OpenPartner".Translate(other.LabelShort))) { SelectPawn(other); Widgets.EndScrollView(); return; }
            }
            if (state.Active != null)
            {
                var r = state.Active;
                Label(ref y, width, "CP_Active".Translate(PromiseText.Type(r)));
                Label(ref y, width, PromiseConditions.Get(r.Kind)?.Describe(r) ?? "CP_InvalidParameters".Translate().ToString());
                Label(ref y, width, "CP_Remaining".Translate(PromiseText.Number(Math.Max(0, r.DeadlineTick - now) / (float)PromiseBalance.TicksPerDay)));
                Label(ref y, width, "CP_Progress".Translate(PromiseText.Number(r.ProgressTicks / (float)PromiseBalance.TicksPerHour), PromiseText.Number(r.RequiredTicks / (float)PromiseBalance.TicksPerHour)));
                if (r.FrozenConsequences) Label(ref y, width, SharedPromiseText.Consequences(component, state, r, state.Active == null ? state.Request : null, now));
                else Label(ref y, width, "CP_LegacyPromise".Translate());
                Label(ref y, width, "CP_DeadlineCompact".Translate());
                if (Button(ref y, width, "CP_Withdraw".Translate()))
                {
                    var target = pawn;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        SharedPromiseText.WithdrawConfirmation(state, r), () => component.Withdraw(target, r)));
                }
            }
            else if (state.Request != null && RequestMaintenance.Visible(state, now))
            {
                var request = state.Request;
                var r = PromiseConsequences.Preview(state, request, now);
                Label(ref y, width, "CP_RequestTitle".Translate(PromiseText.Type(r)));
                Label(ref y, width, HousingRules.Reason(request) ?? request.ReasonKey.Translate(request.Expectation?.label ?? "", RequestRules.PersistentTicks / PromiseBalance.TicksPerDay).ToString());
                Label(ref y, width, PromiseConditions.Get(r.Kind)?.Describe(r) ?? "CP_MissingParameter".Translate().ToString());
                Label(ref y, width, "CP_ResponseTime".Translate(PromiseText.Number(Math.Max(0, request.ExpiryTick - now) / (float)PromiseBalance.TicksPerDay), request.DeadlineDays));
                Label(ref y, width, SharedPromiseText.Consequences(component, state, r, state.Active == null ? state.Request : null, now));
                if (state.Shared == null && TrustRecovery.Available(state, request, now))
                    Label(ref y, width, "CP_UnpromisedRecovery".Translate(TrustRules.UnpromisedRecoveryTrust,
                        r.Kind == PromiseKind.Leisure ? "CP_RecoveryLeisure".Translate(PromiseText.Number(r.RequiredTicks / (float)PromiseBalance.TicksPerHour)).ToString()
                            : "CP_RecoveryHold".Translate(PromiseText.Number(PromiseBalance.HoldTicks / (float)PromiseBalance.TicksPerHour)).ToString(),
                        PromiseText.Number(r.ProgressTicks / (float)PromiseBalance.TicksPerHour)));

                if (state.Shared != null)
                    foreach (var participant in new[] { state.Shared.First, state.Shared.Second })
                        if (TrustRecovery.Available(component.PeekState(participant), request, now))
                            Label(ref y, width, "CP_PairRecovery".Translate(participant.LabelShort, TrustRules.UnpromisedRecoveryTrust,
                                PromiseBalance.HoldTicks / PromiseBalance.TicksPerHour,
                                PromiseText.Number(request.Terms.ProgressTicks / (float)PromiseBalance.TicksPerHour)));
                Label(ref y, width, "CP_DeadlineCompact".Translate());
                string error = component.AcceptanceError(pawn, request);
                if (error != null) Label(ref y, width, error);
                if (error == null && Button(ref y, width, "CP_Accept".Translate()))
                {
                    var target = pawn;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "CP_AcceptConfirm".Translate(state.Shared == null ? target.LabelShort : SharedPromiseText.Names(state.Shared.First, state.Shared.Second), PromiseConditions.Get(r.Kind).Describe(r),
                            request.DeadlineDays, SharedPromiseText.Consequences(component, state, r, state.Active == null ? state.Request : null, now), "CP_DeadlineRule".Translate()),
                        () => component.TryAccept(target, request)));
                }
                if (Button(ref y, width, "CP_Decline".Translate()))
                {
                    var target = pawn;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        (state.Shared == null ? "CP_DeclineConfirm".Translate(RequestRules.UnacceptedCooldownDays).ToString()
                            : "CP_SharedDeclineConfirm".Translate(SharedPromiseText.Names(state.Shared.First, state.Shared.Second), RequestRules.UnacceptedCooldownDays).ToString()), () => component.Decline(target, request)));
                }
            }
            else
            {
                Label(ref y, width, "CP_NoRequest".Translate());
                if (now < state.CooldownUntilTick) Label(ref y, width, "CP_Cooldown".Translate(PromiseText.Number((state.CooldownUntilTick - now) / (float)PromiseBalance.TicksPerDay)));
            }
            if (Button(ref y, width, (moreExpanded ? "- " : "+ ") + "CP_More".Translate())) moreExpanded = !moreExpanded;
            if (moreExpanded)
            {
                Label(ref y, width, "CP_TrustImpact".Translate(TrustRules.LowTrustThreshold,
                    PromiseText.Number(TrustRules.MoodFactor(state.Trust) * 100)));
                Label(ref y, width, "CP_RepeatRules".Translate(PromiseBalance.RepeatWindowDays));
                Label(ref y, width, "CP_RequestRules".Translate());
            }
            if (Prefs.DevMode && Button(ref y, width, (diagnosticsExpanded ? "- " : "+ ") + "CP_DebugTitle".Translate()))
                diagnosticsExpanded = !diagnosticsExpanded;
            if (Prefs.DevMode && diagnosticsExpanded)
            {
                y += 12;
                string exclusion = PromiseConditions.EligibilityReason(pawn);
                Label(ref y, width, "CP_DebugEligible".Translate(
                    exclusion == null ? "CP_DebugYes".Translate().ToString() : exclusion.Translate().ToString()));
                Label(ref y, width, "CP_DebugOccupied".Translate(
                    state.Active != null ? "CP_StatusPromise".Translate() :
                    state.Request != null ? "CP_StatusRequest".Translate() : "CP_StatusWaiting".Translate()));
                Label(ref y, width, "CP_DebugCooldown".Translate(
                    RequestGenerator.Number(Math.Max(0, state.CooldownUntilTick - now) / (float)PromiseBalance.TicksPerDay)));
                Label(ref y, width, "CP_DebugSchedule".Translate(
                    DebugTick(state.LastGenerationCheck), DebugTick(component.LastObservation), DebugTick(component.NextObservation)));
                Label(ref y, width, "CP_DebugSpacing".Translate(
                    (now < component.NextOffer ? "CP_DebugYes" : "CP_DebugNo").Translate(),
                    RequestGenerator.Number(Math.Max(0, component.NextOffer - now) / (float)PromiseBalance.TicksPerDay)));
                foreach (PromiseKind kind in Enum.GetValues(typeof(PromiseKind)))
                    Label(ref y, width, RequestDiagnostics.Describe(state, kind, now));
                Label(ref y, width, "CP_DebugSampling".Translate());
                if (Button(ref y, width, "CP_DebugCheckNow".Translate())) component.CheckRequestsNow(pawn);
            }
            y += 16;
            Label(ref y, width, "CP_History".Translate());
            if (state.History.Count == 0) Label(ref y, width, "CP_NoHistory".Translate());
            foreach (var r in state.History)
            {
                Label(ref y, width, PromiseHistory.Format(r, now));
                Label(ref y, width, PromiseConditions.Get(r.Kind)?.Describe(r) ?? "CP_MissingParameter".Translate().ToString());
            }
            detailHeight = y + 8;
            Widgets.EndScrollView();
        }

        private static string DebugTick(int tick) => tick < 0 ? "CP_DebugNever".Translate().ToString()
            : "CP_DebugTick".Translate(tick.ToString(), RequestGenerator.Number(tick / (float)PromiseBalance.TicksPerDay)).ToString();

        private static void Label(ref float y, float width, string text)
        {
            float height = Text.CalcHeight(text, width) + 8;
            Widgets.Label(new Rect(0, y, width, height), text);
            y += height;
        }

        private static bool Button(ref float y, float width, string text)
        {
            float height = Math.Max(32, Text.CalcHeight(text, Math.Max(1, width - 16)) + 12);
            bool clicked = Widgets.ButtonText(new Rect(0, y, width, height), text);
            y += height + 6;
            return clicked;
        }
    }
}
