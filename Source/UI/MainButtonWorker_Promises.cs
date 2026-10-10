using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyPromises
{
    public sealed class MainButtonWorker_Promises : MainButtonWorker_ToggleTab
    {
        public override void DoButton(Rect rect)
        {
            base.DoButton(rect);
            int count = GameComponent_Promises.Current?.OpenRequests(Find.TickManager.TicksGame).Count ?? 0;
            if (count == 0) return;
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            var badge = new Rect(rect.xMax - 23, rect.y + 1, 22, 17);
            Widgets.DrawBoxSolid(badge, new Color(0.2f, 0.35f, 0.5f));
            Widgets.Label(badge, count.ToString());
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }
    }
}
