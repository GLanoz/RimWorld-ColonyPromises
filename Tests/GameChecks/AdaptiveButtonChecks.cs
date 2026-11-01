using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using ColonyPromises;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

public static class AdaptiveButtonChecks
{
    static int checks;
    static readonly List<(MainButtonDef Def, Rect Rect)> drawn = new List<(MainButtonDef, Rect)>();
    static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    sealed class Recorder : MainButtonWorker
    {
        public bool Show = true;
        public override bool Visible => Show;
        public override void Activate() { }
        public override void DoButton(Rect rect) => drawn.Add((def, rect));
    }
    static MainButtonDef Button(string name, bool minimized = false)
    {
        var def = new MainButtonDef { defName = name, label = name, minimized = minimized };
        typeof(MainButtonDef).GetField("workerInt", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(def, new Recorder { def = def });
        return def;
    }
    static void Emit(ILGenerator il, CodeInstruction instruction)
    {
        foreach (var label in instruction.labels) il.MarkLabel(label);
        var op = instruction.opcode; object value = instruction.operand;
        if (value == null) il.Emit(op);
        else if (value is LocalBuilder local) il.Emit(op, local);
        else if (value is Label label) il.Emit(op, label);
        else if (value is Label[] labels) il.Emit(op, labels);
        else if (value is FieldInfo field) il.Emit(op, field);
        else if (value is ConstructorInfo ctor) il.Emit(op, ctor);
        else if (value is MethodInfo method) il.Emit(op, method);
        else if (value is Type type) il.Emit(op, type);
        else if (value is float number) il.Emit(op, number);
        else if (value is byte index) il.Emit(op, index);
        else if (value is sbyte small) il.Emit(op, small);
        else if (value is short slot) il.Emit(op, slot);
        else if (value is int integer) il.Emit(op, integer);
        else throw new Exception("Unexpected native layout operand: " + value);
    }
    public static int Run()
    {
        var patchType = typeof(PromiseRecord).Assembly.GetType("ColonyPromises.AdaptivePromisesButtonPatch");
        var validate = patchType.GetMethod("NativeLayout", BindingFlags.Static | BindingFlags.NonPublic);
        var original = AccessTools.DeclaredMethod(typeof(MainButtonsRoot), "DoButtons");
        var dynamic = new DynamicMethod("VerifyNativeAdaptiveButtonBar", typeof(void), new[] { typeof(MainButtonsRoot) }, typeof(AdaptiveButtonChecks).Module, true);
        var generator = dynamic.GetILGenerator();
        var code = PatchProcessor.GetOriginalInstructions(original, generator);
        Check((bool)validate.Invoke(null, new object[] { code }), "The actual game's sizing contract is recognized");
        var changed = code.Where(i => i.opcode != OpCodes.Div).ToList();
        Check(!(bool)validate.Invoke(null, new object[] { changed }), "A different allocation contract is rejected");
        foreach (var i in code)
        {
            // Only replace the native color sink; preserve the game's allocation, visibility,
            // minimized weighting, rectangle construction, cursor and worker dispatch verbatim.
            if (i.operand is MethodInfo color && color.DeclaringType == typeof(GUI) && color.Name == "set_color")
            { i.opcode = OpCodes.Pop; i.operand = null; }
            Emit(generator, i);
        }
        var draw = (Action<MainButtonsRoot>)dynamic.CreateDelegate(typeof(Action<MainButtonsRoot>));
        var root = (MainButtonsRoot)RuntimeHelpers.GetUninitializedObject(typeof(MainButtonsRoot));
        var field = typeof(MainButtonsRoot).GetField("allButtonsInOrder", BindingFlags.Instance | BindingFlags.NonPublic);
        int oldWidth = UI.screenWidth, oldHeight = UI.screenHeight;
        try
        {
            foreach (int resolution in new[] { 1280, 1920, 3440 })
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            foreach (int others in new[] { 5, 15, 30 })
            foreach (float labelWidth in new[] { 65f, 90f, 130f })
            foreach (int position in new[] { 0, 2, others })
            {
                UI.screenWidth = (int)(resolution / scale); UI.screenHeight = 900;
                var buttons = Enumerable.Range(0, others).Select(i => Button("OtherMod" + i, i % 3 == 0)).ToList();
                var own = Button("CP_Promises", true); buttons.Insert(position, own);
                var hidden = Button("HiddenMod"); ((Recorder)hidden.Worker).Show = false; buttons.Insert(0, hidden);
                var spaces = buttons.Where(b => b.Worker.Visible).Select(b => new ButtonSpace(b == own, b.minimized,
                    b == own ? AdaptivePromisesButton.ExpandedWidth(labelWidth) : b.minimized ? 40 : 70)).ToList();
                bool expanded = AdaptivePromisesButton.CanExpand(spaces, UI.screenWidth, false);
                own.minimized = !expanded;
                Check(AdaptivePromisesButton.CanExpand(spaces, UI.screenWidth, expanded) == expanded,
                    "A mode does not oscillate with unchanged counterfactual full-width inputs");
                field.SetValue(root, buttons); drawn.Clear(); draw(root);
                Check(drawn.Count == others + 1, "Native visibility still includes mod buttons and excludes hidden ones");
                float x = 0;
                foreach (var item in drawn)
                {
                    Check(item.Rect.x == x && item.Rect.width > 0 && item.Rect.height == 36,
                        "Native drawing and interaction Rects remain contiguous and keep their height");
                    x = item.Rect.xMax;
                }
                Check(x == UI.screenWidth, "Mode switching leaves neither a panel gap nor overlap");
                Check(buttons.Where(b => b != own && b != hidden).Select((b, i) => b.minimized == (i % 3 == 0)).All(v => v),
                    "Only the Promises mode can change; foreign settings remain intact");
                if (!expanded)
                {
                    var rect = drawn.Single(d => d.Def == own).Rect;
                    foreach (bool hovered in new[] { false, true })
                    {
                        var icon = MainButtonWorker_Promises.CompactIconRect(rect, hovered);
                        Check(icon.width <= AdaptivePromisesButton.IconSize && icon.xMin >= rect.xMin && icon.xMax <= rect.xMax
                            && icon.yMin >= rect.yMin && icon.yMax <= rect.yMax,
                            "An overcrowded compact slot keeps the icon inside its own hit/highlight rectangle");
                    }
                }
                if (expanded)
                {
                    var rect = drawn.Single(d => d.Def == own).Rect;
                    Check(rect.width >= AdaptivePromisesButton.ExpandedWidth(labelWidth), "Expanded native slot fits the full current-language label");
                    foreach (bool hovered in new[] { false, true })
                    {
                        MainButtonWorker_Promises.ExpandedRects(rect, labelWidth, hovered, out var icon, out var label);
                        Check(icon.xMin >= rect.xMin && icon.xMax < label.xMin && label.xMax <= rect.xMax,
                            "Icon and full text fit without touching a neighbor, including hover offset");
                        Check(icon.yMin >= rect.yMin && icon.yMax <= rect.yMax, "Native icon size and hover offset stay inside the button");
                    }
                }
            }
        }
        finally { UI.screenWidth = oldWidth; UI.screenHeight = oldHeight; }
        var narrow = new[] { new ButtonSpace(false, false, 90), new ButtonSpace(true, true, 130), new ButtonSpace(false, false, 90) };
        Check(!AdaptivePromisesButton.CanExpand(narrow, 380, false), "Its own compact slot cannot falsely create enough space for expansion");
        var boundary = new[] { new ButtonSpace(true, false, 120), new ButtonSpace(false, false, 60) };
        Check(AdaptivePromisesButton.CanExpand(boundary, 240, true) && !AdaptivePromisesButton.CanExpand(boundary, 240, false),
            "Hysteresis retains a fitting full label without re-expanding a tight compact button");
        Check(AdaptivePromisesButton.CanExpand(boundary, 260, false), "Extra native padding permits expansion again");
        Check(AdaptivePromisesButton.CanExpand(narrow, 1920, false) && !AdaptivePromisesButton.CanExpand(
            Enumerable.Range(0, 40).Select(i => new ButtonSpace(i == 0, false, 130)).ToList(), 1920, false),
            "Available width and visible-tab count determine mode, not a fixed scale threshold");
        bool fullMode = true;
        foreach (var step in new[] { (240, true), (239, false), (259, false), (260, true), (260, true) })
        { fullMode = AdaptivePromisesButton.CanExpand(boundary, step.Item1, fullMode); Check(fullMode == step.Item2, "Resize hysteresis transitions are stable in both directions"); }
        var english = new[] { new ButtonSpace(true, true, AdaptivePromisesButton.ExpandedWidth(65)), new ButtonSpace(false, false, 60) };
        var longerTranslation = new[] { new ButtonSpace(true, true, AdaptivePromisesButton.ExpandedWidth(90)), new ButtonSpace(false, false, 60) };
        Check(AdaptivePromisesButton.CanExpand(english, 260, false) && !AdaptivePromisesButton.CanExpand(longerTranslation, 260, false),
            "The actual localized text width can change the mode at the same resolution and scale");
        Statistics();
        Fallback();
        return checks;
    }
    static void Fallback()
    {
        var available = typeof(AdaptivePromisesButton).GetProperty(nameof(AdaptivePromisesButton.Available));
        var warned = typeof(AdaptivePromisesButton).GetField("warned", BindingFlags.Static | BindingFlags.NonPublic);
        var warn = typeof(AdaptivePromisesButton).GetMethod("WarnFallback", BindingFlags.Static | BindingFlags.NonPublic);
        bool previousAvailable = AdaptivePromisesButton.Available, previousWarning = (bool)warned.GetValue(null);
        try
        {
            available.SetValue(null, true); warned.SetValue(null, false);
            try { warn.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException is System.Security.SecurityException) { }
            Check(!AdaptivePromisesButton.Available && (bool)warned.GetValue(null), "Incompatibility disables adaptive rendering and arms the one-time warning");
            int messages = (int)typeof(Log).GetField("messageCount", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            warn.Invoke(null, null);
            Check((int)typeof(Log).GetField("messageCount", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == messages,
                "Repeated fallback cannot spam the log or retry native logging");
            var own = Button("CP_Promises", false); var other = Button("OtherMod", false);
            typeof(AdaptivePromisesButton).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { new List<MainButtonDef> { own, other } });
            Check(own.minimized && !other.minimized, "Fallback restores only Promises to the native compact mode");
        }
        finally { available.SetValue(null, previousAvailable); warned.SetValue(null, previousWarning); }
    }
    static void Statistics()
    {
        var component = new GameComponent_Promises(null);
        var active = (Dictionary<Pawn, ColonistPromises>)typeof(GameComponent_Promises)
            .GetField("active", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component);
        var a = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn)); a.thingIDNumber = 9201;
        var b = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn)); b.thingIDNumber = 9202;
        a.def = b.def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        a.def.defName = "ButtonFixturePawn";
        var common = new PromiseRecord { Kind = PromiseKind.Bedroom, SharedId = "button-shared" };
        var first = component.StateFor(a); var second = component.StateFor(b);
        first.Active = second.Active = common; active.Add(a, first); active.Add(b, second);
        Check(component.ButtonStatistics(0).ActivePromises == 1, "One shared obligation counts once, not once per participant");
        var worker = new MainButtonWorker_Promises();
        Check(worker.Statistics(component, 0, 0).ActivePromises == 1, "Statistics initialize for the current game");
        second.Active = new PromiseRecord { Kind = PromiseKind.Leisure };
        Check(worker.Statistics(component, 0, 100).ActivePromises == 1, "Unchanged indexes reuse the snapshot between refreshes, including paused games");
        Check(worker.Statistics(component, 0, 250).ActivePromises == 2, "Real-time refresh notices data changes without advancing game time");
        active.Remove(b);
        Check(worker.Statistics(component, 0, 251).ActivePromises == 1, "Index changes invalidate counts immediately");
        Check(worker.Statistics(new GameComponent_Promises(null), 0, 252).ActivePromises == 0, "A new or loaded game cannot reuse another game's counts");
    }
}
