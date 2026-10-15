using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyPromises
{
    public static class DevRequestMenu
    {
        private static void Show(IEnumerable<FloatMenuOption> options)
        {
            if (!Prefs.DevMode) return;
            var list = options.ToList();
            if (list.Count == 0) { Report("CP_DevNoTargets".Translate()); return; }
            Find.WindowStack.Add(new FloatMenu(list));
        }
        private static void Report(string error)
        {
            if (error != null) Messages.Message(error, MessageTypeDefOf.RejectInput, false);
        }
        public static void Open(Pawn pawn)
        {
            if (!Prefs.DevMode) return;
            Show(new[] {
                new FloatMenuOption("CP_DevCurrent".Translate(), () => Types(pawn, false)),
                new FloatMenuOption("CP_DevManual".Translate(), () => Types(pawn, true)) });
        }
        private static void Types(Pawn pawn, bool manual) => Show(DevRequests.Types.Select(type =>
            new FloatMenuOption(("CP_DevType_" + type.Key).Translate(), () => {
                if (!Prefs.DevMode) return;
                var component = GameComponent_Promises.Current;
                if (DevRequests.Occupied(component.PeekState(pawn))) { Report("CP_DevOccupied".Translate()); return; }
                if (!manual) { Report(component.CreateDevRequest(pawn, type)); return; }
                if (type.Shared)
                    Show(SharedBedroomRules.Partners(pawn).Where(p => PromiseConditions.Eligible(p)
                        && SharedBedroomRules.Validity(pawn, p) == SharedPairValidity.Valid).Select(partner =>
                        new FloatMenuOption(partner.LabelShort, () => Configure(pawn, type, DevRequests.Manual(type, pawn, partner)))));
                else Configure(pawn, type, DevRequests.Manual(type, pawn));
            })));

        private static void Configure(Pawn pawn, DevRequestType type, PromiseRecord terms)
        {
            if (!Prefs.DevMode) return;
            if (type.Kind == PromiseKind.Bedroom)
            {
                Show(Enumerable.Range(0, RoomStatDefOf.Impressiveness.scoreStages.Count).Select(stage =>
                    new FloatMenuOption(BedroomCondition.RoomLabel(stage), () => { terms.RoomStage = stage; Create(pawn, type, terms); })));
                return;
            }
            if (type.Kind == PromiseKind.Leisure)
            {
                Show(Enumerable.Range(RequestRules.MinHours, RequestRules.MaxHours - RequestRules.MinHours + 1).Select(hours =>
                    new FloatMenuOption("CP_DevHours".Translate(hours), () => {
                        terms.RequiredTicks = hours * PromiseBalance.TicksPerHour; Create(pawn, type, terms);
                    })));
                return;
            }
            if (type.Kind == PromiseKind.Substance)
            {
                Show(DefDatabase<ThingDef>.AllDefsListForReading.OrderBy(d => d.defName, StringComparer.Ordinal)
                    .Where(d => SubstanceNeeds.SupportReason(d, type.Cause == "SubstanceTry") == null)
                    .Select(def => {
                        string reason = SubstanceNeeds.TargetReason(pawn, def, type.Cause == "SubstanceTry", true);
                        return new FloatMenuOption(def.LabelCap.ToString() + (reason == null ? "" : " - " + reason.Translate().ToString()),
                            reason == null ? (Action)(() => {
                                var record = SubstanceNeeds.Terms(def, type.Cause == "SubstanceTry");
                                record.DevTest = record.DevManual = true; Create(pawn, type, record);
                            }) : null);
                    }));
                return;
            }
            IEnumerable<ThingDef> targets = DefDatabase<ThingDef>.AllDefsListForReading;
            if (type.Cause == "WeaponUpgrade") targets = targets.Where(d => d == pawn.equipment?.Primary?.def);
            else if (EquipmentNeeds.AcquisitionCause(type.Cause)) targets = targets.Where(d =>
                EquipmentNeeds.WeaponTypeMatches(d, type.Cause == "WeaponRanged"));
            else targets = targets.Where(d => type.Cause == "BrawlerUnhappy" ? d.IsMeleeWeapon : d.IsApparel);
            Show(targets.OrderBy(d => d.label, StringComparer.CurrentCulture).ThenBy(d => d.defName, StringComparer.Ordinal)
                .Where(d => { var probe = terms.CopyForPreview(); probe.EquipmentDef = d; return DevRequests.ValidTerms(pawn, probe); })
                .Select(def => new FloatMenuOption(def.LabelCap, () => {
                    terms.EquipmentDef = def;
                    terms.AvoidHumanLeather = def.IsApparel && DlcCompatibility.AvoidHumanLeather(pawn);
                    Quality(pawn, type, terms);
                })));
        }
        private static void Quality(Pawn pawn, DevRequestType type, PromiseRecord terms)
        {
            if (!Prefs.DevMode) return;
            var options = new List<FloatMenuOption>();
            if (type.Cause != "WeaponUpgrade") options.Add(new FloatMenuOption("CP_QualityNotRequired".Translate(),
                () => { terms.QualityOptional = true; Condition(pawn, type, terms); }));
            if (terms.EquipmentDef.HasComp(typeof(CompQuality)))
                foreach (QualityCategory quality in Enum.GetValues(typeof(QualityCategory)))
                    options.Add(new FloatMenuOption(quality.GetLabel(), () => {
                        terms.QualityOptional = false; terms.MinimumQuality = quality; Condition(pawn, type, terms);
                    }));
            Show(options);
        }
        private static void Condition(Pawn pawn, DevRequestType type, PromiseRecord terms)
        {
            if (!Prefs.DevMode) return;
            if (!terms.EquipmentDef.useHitPoints) { terms.MinimumHitPointsFraction = 0; Create(pawn, type, terms); return; }
            Show(new[] { 0, 80, 90, 100 }.Select(percent => new FloatMenuOption("CP_DevCondition".Translate(percent), () => {
                terms.MinimumHitPointsFraction = percent / 100f; Create(pawn, type, terms);
            })));
        }
        private static void Create(Pawn pawn, DevRequestType type, PromiseRecord terms)
        {
            if (!Prefs.DevMode) return;
            Report(GameComponent_Promises.Current.CreateDevRequest(pawn, type, terms));
        }
        public static void Clear(Pawn pawn, PromiseRecord expected)
        {
            if (!Prefs.DevMode) return;
            string names = expected.IsShared ? SharedPromiseText.Names(expected.ParticipantA, expected.ParticipantB) : pawn.LabelShort;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("CP_DevClearConfirm".Translate(names), () => {
                if (Prefs.DevMode) GameComponent_Promises.Current.ClearDevRecord(pawn, expected);
            }));
        }
    }
}
