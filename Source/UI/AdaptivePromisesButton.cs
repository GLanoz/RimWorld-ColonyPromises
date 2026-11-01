using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyPromises
{
    public readonly struct ButtonSpace
    {
        public readonly bool Promises;
        public readonly bool Minimized;
        public readonly float RequiredWidth;
        public ButtonSpace(bool promises, bool minimized, float requiredWidth)
        { Promises = promises; Minimized = minimized; RequiredWidth = requiredWidth; }
    }

    public static class AdaptivePromisesButton
    {
        public const float IconSize = 32f;
        private static readonly Dictionary<MainButtonDef, (string Label, float Width)> labelWidths =
            new Dictionary<MainButtonDef, (string, float)>();
        private static LoadedLanguage language;
        private static float scale;
        private static bool warned;
        public static bool Available { get; internal set; }
        internal static void WarnFallback()
        {
            Available = false;
            if (warned) return;
            warned = true;
            Log.Warning("[ColonyPromises] Adaptive button layout is incompatible; using the standard compact button.");
        }
        public static float LabelWidth(MainButtonDef def)
        {
            if (language != LanguageDatabase.activeLanguage || scale != Prefs.UIScale)
            { labelWidths.Clear(); language = LanguageDatabase.activeLanguage; scale = Prefs.UIScale; }
            string label = def.LabelCap;
            if (labelWidths.TryGetValue(def, out var cached) && cached.Label == label) return cached.Width;
            var font = Text.Font;
            try
            {
                Text.Font = GameFont.Small;
                float width = Text.CalcSize(label).x;
                labelWidths[def] = (label, width); return width;
            }
            finally { Text.Font = font; }
        }
        public static float ExpandedWidth(float labelWidth) => IconSize + GenUI.GapSmall + labelWidth + 2 * GenUI.GapTiny;

        // Predict the game's native layout with Promises FULL, regardless of its current mode.
        // The same input produces the same mode; compaction cannot manufacture expansion space.
        public static bool CanExpand(IReadOnlyList<ButtonSpace> buttons, int screenWidth, bool currentlyExpanded)
        {
            if (screenWidth <= 0 || buttons.Count == 0) return false;
            float weight = 0; int own = 0;
            foreach (var button in buttons)
            { weight += button.Promises || !button.Minimized ? 1f : 0.5f; if (button.Promises) own++; }
            if (own != 1) return false;
            int full = (int)(screenWidth / weight), half = full / 2, used = 0;
            float reserve = currentlyExpanded ? 0 : GenUI.GapSmall;
            for (int i = 0; i < buttons.Count; i++)
            {
                var button = buttons[i];
                int width = i == buttons.Count - 1 ? screenWidth - used
                    : button.Promises || !button.Minimized ? full : half;
                if (width < button.RequiredWidth + reserve) return false;
                used += width;
            }
            return true;
        }

        internal static void Update(List<MainButtonDef> buttons)
        {
            var own = buttons?.Find(button => button?.defName == "CP_Promises");
            if (own == null) return;
            if (!Available) { own.minimized = true; return; }
            try
            {
                var spaces = new List<ButtonSpace>(buttons.Count);
                // Only the small UI definition list is inspected. Font measurements are cached;
                // there are no pawn, map, item or DefDatabase scans here.
                foreach (var button in buttons)
                {
                    bool promises = button.defName == "CP_Promises";
                    if (!button.Worker.Visible) continue;
                    float needed = promises ? ExpandedWidth(LabelWidth(button))
                        : !string.IsNullOrEmpty(button.iconPath) ? IconSize + 2 * GenUI.GapTiny
                        : LabelWidth(button) + 2 * GenUI.GapTiny;
                    spaces.Add(new ButtonSpace(promises, button.minimized, needed));
                }
                if (own != null) own.minimized = !Available || !CanExpand(spaces, UI.screenWidth, !own.minimized);
            }
            catch
            {
                if (own != null) own.minimized = true;
                WarnFallback();
            }
        }
    }
}
