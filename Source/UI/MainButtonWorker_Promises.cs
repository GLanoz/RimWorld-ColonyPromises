using System;
using System.Diagnostics;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyPromises
{
    public sealed class MainButtonWorker_Promises : MainButtonWorker_ToggleTab
    {
        private static readonly Color RequestColor = new Color(0.2f, 0.35f, 0.5f);
        private GameComponent_Promises statisticsOwner;
        private long statisticsStamp, nextStatistics, lastStatistics;
        private PromiseButtonStatistics statistics;
        private int badgeCount = -1;
        private float badgeScale, badgeWidth;
        private string badgeText;

        public PromiseButtonStatistics Statistics(GameComponent_Promises component, int tick, long milliseconds)
        {
            long stamp = component?.ButtonIndexStamp ?? 0;
            if (component != statisticsOwner || stamp != statisticsStamp || milliseconds >= nextStatistics || milliseconds < lastStatistics)
            {
                statistics = component?.ButtonStatistics(tick) ?? default;
                statisticsOwner = component; statisticsStamp = stamp;
                lastStatistics = milliseconds; nextStatistics = milliseconds + 250;
            }
            return statistics;
        }

        public static TipSignal RequestTooltip(MainButtonDef def, int requests, int promises = 0)
        {
            // Match the native ID to update its tooltip, retaining its hover delay.
            string native = def.LabelCap.Colorize(ColorLibrary.Yellow) + "\n\n" + def.description;
            string title = def.LabelCap.Colorize(ColoredText.TipSectionTitleColor);
            string requestText = "CP_ButtonRequestCount".Translate(requests.ToString());
            string promiseText = "CP_ButtonPromiseCount".Translate(promises.ToString());
            string text = title + "\n\n" + def.description + "\n\n" + requestText + "\n" + promiseText;
            return new TipSignal(text, native.GetHashCode());
        }

        public static bool TryBadgeRect(Rect button, float width, out Rect badge)
        {
            badge = new Rect(button.xMax - width - 1f, button.y + 1f, width, 17f);
            return badge.xMin >= button.center.x + 19f && badge.width > 0f && button.height >= 19f;
        }

        public static void ExpandedRects(Rect button, float labelWidth, bool hovered, out Rect icon, out Rect label)
        {
            float content = AdaptivePromisesButton.IconSize + GenUI.GapSmall + labelWidth;
            float x = button.center.x - content / 2 + (hovered ? 2 : 0);
            float y = button.center.y - AdaptivePromisesButton.IconSize / 2 - (hovered ? 2 : 0);
            icon = new Rect(x, y, AdaptivePromisesButton.IconSize, AdaptivePromisesButton.IconSize);
            label = new Rect(icon.xMax + GenUI.GapSmall, button.y - (hovered ? 2 : 0), labelWidth, button.height);
        }

        public static Rect CompactIconRect(Rect button, bool hovered)
        {
            float size = Mathf.Max(0f, Mathf.Min(AdaptivePromisesButton.IconSize,
                button.width - 2 * GenUI.GapTiny, button.height - GenUI.GapTiny));
            return new Rect(button.center.x - size / 2 + (hovered ? 2 : 0),
                button.center.y - size / 2 - (hovered ? 2 : 0), size, size);
        }

        private void DrawIconButton(Rect rect, bool expanded, float labelWidth = 0)
        {
            if (Widgets.ButtonTextSubtle(rect, "", ButtonBarPercent, -1f, SoundDefOf.Mouseover_Category)) InterfaceTryActivate();
            bool hovered = Mouse.IsOver(rect);
            ExpandedRects(rect, labelWidth, hovered, out var icon, out var label);
            if (!expanded) icon = CompactIconRect(rect, hovered);
            if (icon.width > 0) GUI.DrawTexture(icon, def.Icon);
            if (expanded)
            {
                var anchor = Text.Anchor; var font = Text.Font; bool wrap = Text.WordWrap;
                try
                {
                    Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleLeft; Text.WordWrap = false;
                    Widgets.Label(label, def.LabelCap);
                }
                finally { Text.Anchor = anchor; Text.Font = font; Text.WordWrap = wrap; }
            }
            if (Find.MainTabsRoot.OpenTab != def && !Find.WindowStack.NonImmediateDialogWindowOpen)
                UIHighlighter.HighlightOpportunity(rect, def.cachedHighlightTagClosed);
        }

        public override void DoButton(Rect rect)
        {
            bool expanded = AdaptivePromisesButton.Available && !def.minimized && !Disabled;
            if (expanded)
            {
                float labelWidth = AdaptivePromisesButton.LabelWidth(def);
                if (rect.width >= AdaptivePromisesButton.ExpandedWidth(labelWidth)) DrawIconButton(rect, true, labelWidth);
                else
                {
                    def.minimized = true; AdaptivePromisesButton.WarnFallback(); base.DoButton(rect);
                    expanded = false;
                }
            }
            else if (!Disabled && rect.width < AdaptivePromisesButton.IconSize + 2 * GenUI.GapTiny) DrawIconButton(rect, false);
            else base.DoButton(rect);
            long milliseconds = (long)(Stopwatch.GetTimestamp() * (1000d / Stopwatch.Frequency));
            var counts = Statistics(GameComponent_Promises.Current, Find.TickManager.TicksGame, milliseconds);
            if (Mouse.IsOver(rect)) TooltipHandler.TipRegion(rect, RequestTooltip(def, counts.Requests, counts.ActivePromises));
            if (counts.Requests == 0) return;
            var font = Text.Font; var anchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
                if (badgeCount != counts.Requests || badgeScale != Prefs.UIScale)
                {
                    badgeCount = counts.Requests; badgeScale = Prefs.UIScale;
                    badgeText = counts.Requests.ToString(); badgeWidth = Mathf.Max(22f, Text.CalcSize(badgeText).x + 6f);
                }
                float width = badgeWidth;
                if (!expanded && TryBadgeRect(rect, width, out var badge))
                { Widgets.DrawBoxSolid(badge, RequestColor); Widgets.Label(badge, badgeText); }
                else Widgets.DrawBoxSolid(new Rect(rect.x + 2f, rect.yMax - 2f, Mathf.Max(0f, rect.width - 4f), 2f), RequestColor);
            }
            finally { Text.Font = font; Text.Anchor = anchor; }
        }
    }
}
