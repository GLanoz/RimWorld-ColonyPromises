using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    internal static class AdaptivePromisesButtonPatch
    {
        internal static bool NativeLayout(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var minimized = AccessTools.Field(typeof(MainButtonDef), nameof(MainButtonDef.minimized));
            var screen = AccessTools.Field(typeof(UI), nameof(UI.screenWidth));
            return code.Count(i => i.opcode == OpCodes.Ldfld && Equals(i.operand, minimized)) == 2
                && code.Count(i => i.opcode == OpCodes.Ldsfld && Equals(i.operand, screen)) == 2
                && code.Count(i => i.opcode == OpCodes.Ldc_R4 && Equals(i.operand, 0.5f)) == 1
                && code.Any(i => i.opcode == OpCodes.Div)
                && code.Count(i => i.operand is MethodInfo m && m.DeclaringType == typeof(MainButtonWorker) && m.Name == "DoButton") == 1;
        }
        internal static void Install(Harmony harmony)
        {
            try
            {
                var target = AccessTools.DeclaredMethod(typeof(MainButtonsRoot), "DoButtons");
                var field = AccessTools.Field(typeof(MainButtonsRoot), "allButtonsInOrder");
                if (target == null || field?.FieldType != typeof(List<MainButtonDef>)
                    || !NativeLayout(PatchProcessor.GetCurrentInstructions(target)))
                { AdaptivePromisesButton.WarnFallback(); return; }
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(AdaptivePromisesButtonPatch), nameof(Prefix)));
                AdaptivePromisesButton.Available = true;
            }
            catch { AdaptivePromisesButton.WarnFallback(); }
        }
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(List<MainButtonDef> ___allButtonsInOrder) => AdaptivePromisesButton.Update(___allButtonsInOrder);
    }
}
