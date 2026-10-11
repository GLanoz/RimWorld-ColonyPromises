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
        private bool sortByName; // Session-local; the default groups active promises first.
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

        internal sealed class ColonistGroup
        {
            public string HeadingKey;
            public List<Pawn> Members;
        }

        internal static List<ColonistGroup> GroupColonists(IEnumerable<Pawn> candidates,
            Func<Pawn, ColonistPromises> stateFor, bool byName)
        {
            var ordered = candidates.OrderBy(p => p.LabelShort, StringComparer.Create(PromiseText.Culture, true))
                .ThenBy(p => p.thingIDNumber).ToList();
            if (byName) return new List<ColonistGroup> { new ColonistGroup { Members = ordered } };
            return new[] { true, false }.Select(active => new ColonistGroup {
                HeadingKey = active ? "CP_GroupActivePromise" : "CP_GroupNoPromise",
                Members = ordered.Where(p => (stateFor(p).Active != null) == active).ToList()
            }).Where(g => g.Members.Count > 0).ToList();
        }

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
            return (record.QualityOptional ? "CP_GearShort" : "CP_ShortEquipment").Translate(record.EquipmentDef?.LabelCap.ToString() ?? "CP_MissingParameter".Translate().ToString(),
                record.QualityOptional ? "CP_QualityNotRequired".Translate().ToString() : record.MinimumQuality.GetLabel()).ToString()
                + (record.MinimumHitPointsFraction > 0 ? " " + "CP_ShortDurability".Translate(
                    PromiseText.Number(record.MinimumHitPointsFraction * 100)).ToString() : "");
        }

        private void DrawColonists(Rect rect, List<Pawn> candidates, GameComponent_Promises component, int now)
        {
            var shown = new List<Pawn>(candidates);
            if (Prefs.DevMode && pawn != null && !shown.Contains(pawn)) shown.Add(pawn);
            float listWidth = Math.Min(260, rect.width * 0.28f);
            string sortLabel = (sortByName ? "CP_SortName" : "CP_SortPromises").Translate();
            float sortHeight = Math.Max(32, Text.CalcHeight(sortLabel, Math.Max(1, listWidth - 16)) + 12);
            if (Widgets.ButtonText(new Rect(rect.x, rect.y, listWidth, sortHeight), sortLabel))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> {
                    new FloatMenuOption("CP_SortName".Translate(), () => sortByName = true),
                    new FloatMenuOption("CP_SortPromises".Translate(), () => sortByName = false)
                }));
            var listRect = new Rect(rect.x, rect.y + sortHeight + 6, listWidth, Math.Max(1, rect.height - sortHeight - 6));
            float width = Math.Max(1, listWidth - 20);
            var visible = new HashSet<Pawn>(component.OpenRequests(now).Select(s => s.Pawn));
            foreach (var entry in component.OpenRequests(now).Where(s => s.Shared != null)) visible.Add(entry.Shared.Second);
            var groups = GroupColonists(shown, component.PeekState, sortByName);
            Func<Pawn, string> label = p => p.LabelShort + "\n" + StatusKey(component.PeekState(p), now, visible.Contains(p)).Translate();
            Func<string, float> rowHeight = text => Math.Max(64, Text.CalcHeight(text, Math.Max(1, width - 16)) + 20);
            Func<ColonistGroup, float> headingHeight = g => g.HeadingKey == null ? 0
                : Text.CalcHeight(g.HeadingKey.Translate(g.Members.Count), Math.Max(1, width - 16)) + 16;
            colonistHeight = groups.Sum(g => headingHeight(g) + g.Members.Sum(p => rowHeight(label(p))));
            Widgets.BeginScrollView(listRect, ref colonistsScroll, new Rect(0, 0, width, colonistHeight));
            float y = 0;
            foreach (var group in groups)
            {
                if (group.HeadingKey != null)
                {
                    float height = headingHeight(group);
                    Widgets.Label(new Rect(8, y + 4, Math.Max(1, width - 16), height - 8), group.HeadingKey.Translate(group.Members.Count));
                    y += height;
                }
                foreach (var member in group.Members)
                {
                    string text = label(member);
                    float height = rowHeight(text);
                    var row = new Rect(0, y, width, height - 4);
                    if (member == pawn) Widgets.DrawHighlightSelected(row);
                    Widgets.DrawHighlightIfMouseover(row);
                    Widgets.Label(new Rect(8, y + 6, Math.Max(1, width - 16), row.height - 12), text);
                    if (Widgets.ButtonInvisible(row)) SelectPawn(member);
                    y += height;
                }
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
            var other = SharedPromiseText.PartnerFor(state.Shared, pawn);
            PromiseRecord displayed = null;
            ColonistRequest displayedRequest = null;
            if (state.Active != null)
            {
                var r = displayed = state.Active;
                Label(ref y, width, "CP_Active".Translate(PromiseCardText.Title(r, pawn)));
                Label(ref y, width, PromiseCardText.Conditions(r));
                Label(ref y, width, "CP_Progress".Translate(PromiseText.Number(r.ProgressTicks / (float)PromiseBalance.TicksPerHour), PromiseText.Number(r.RequiredTicks / (float)PromiseBalance.TicksPerHour)));
                Label(ref y, width, "CP_Remaining".Translate(PromiseText.Number(Math.Max(0, r.DeadlineTick - now) / (float)PromiseBalance.TicksPerDay)));
                string blocked = PromiseCardText.ProgressBlock(pawn, r);
                if (blocked != null) Label(ref y, width, blocked);
                Label(ref y, width, SharedPromiseText.Rewards(component, state, r, null, now));
                if (Button(ref y, width, "CP_Withdraw".Translate()))
                {
                    var target = pawn;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        SharedPromiseText.WithdrawConfirmation(state, r), () => component.Withdraw(target, r)));
                }
            }
            else if (state.Request != null && RequestMaintenance.Visible(state, now))
            {
                var request = displayedRequest = state.Request;
                var r = displayed = PromiseConsequences.Preview(state, request, now);
                Label(ref y, width, PromiseCardText.Title(r, pawn));
                Label(ref y, width, PromiseCardText.Reason(request));
                Label(ref y, width, PromiseCardText.Conditions(r));
                Label(ref y, width, "CP_ResponseTime".Translate(PromiseText.Number(Math.Max(0, request.ExpiryTick - now) / (float)PromiseBalance.TicksPerDay), request.DeadlineDays));
                Label(ref y, width, SharedPromiseText.Rewards(component, state, r, request, now));
                string error = component.AcceptanceError(pawn, request);
                if (error != null) Label(ref y, width, error);
                if (error == null && Button(ref y, width, "CP_Accept".Translate()))
                {
                    var target = pawn;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "CP_AcceptConfirm".Translate(state.Shared == null ? target.LabelShort : SharedPromiseText.Names(state.Shared.First, state.Shared.Second), PromiseConditions.Get(r.Kind).Describe(r),
                            request.DeadlineDays, SharedPromiseText.Consequences(component, state, r, request, now), "CP_DeadlineRule".Translate()),
                        () => component.TryAccept(target, request)));
                }
                if (Button(ref y, width, "CP_Decline".Translate()))
                {
                    var target = pawn;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        state.Shared == null ? "CP_DeclineConfirm".Translate(RequestRules.UnacceptedCooldownDays).ToString()
                            : "CP_SharedDeclineConfirm".Translate(SharedPromiseText.Names(state.Shared.First, state.Shared.Second), RequestRules.UnacceptedCooldownDays).ToString(),
                        () => component.Decline(target, request)));
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
                if (displayed != null)
                {
                    Label(ref y, width, PromiseConditions.Get(displayed.Kind)?.Describe(displayed) ?? "CP_InvalidParameters".Translate().ToString());
                    if (displayed.SuccessDays > 0) Label(ref y, width, SharedPromiseText.Penalties(component, state, displayed, displayedRequest, now));
                    else Label(ref y, width, "CP_LegacyPromise".Translate());
                    Label(ref y, width, "CP_DeadlineRule".Translate());
                }
                if (displayedRequest != null)
                {
                    foreach (var participant in state.Shared == null ? new[] { pawn } : new[] { state.Shared.First, state.Shared.Second })
                        if (TrustRecovery.Available(component.PeekState(participant), displayedRequest, now))
                            Label(ref y, width, "CP_CardRecoveryPerson".Translate(participant.LabelShort, "CP_UnpromisedRecovery".Translate(TrustRules.UnpromisedRecoveryTrust,
                                displayed.Kind == PromiseKind.Leisure ? "CP_RecoveryLeisure".Translate(PromiseText.Number(displayed.RequiredTicks / (float)PromiseBalance.TicksPerHour)).ToString()
                                    : "CP_RecoveryHold".Translate(PromiseText.Number(displayed.RequiredTicks / (float)PromiseBalance.TicksPerHour)).ToString(),
                                PromiseText.Number(displayed.ProgressTicks / (float)PromiseBalance.TicksPerHour)).ToString()));
                }
                Label(ref y, width, "CP_TrustImpact".Translate(TrustRules.LowTrustThreshold,
                    PromiseText.Number(TrustRules.MoodFactor(state.Trust) * 100)));
                Label(ref y, width, "CP_RepeatRules".Translate(PromiseBalance.RepeatWindowDays));
                Label(ref y, width, "CP_RequestRules".Translate());
            }
            if (other != null && Button(ref y, width, "CP_OpenPartner".Translate(other.LabelShort)))
            { SelectPawn(other); Widgets.EndScrollView(); return; }
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
                Label(ref y, width, PromiseCardText.Conditions(r));
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
