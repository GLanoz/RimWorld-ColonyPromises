# API reference

## Target version

Colony Promises targets **RimWorld 1.6**, checked against **1.6.4871 rev590**. Signatures, relevant call paths, and Core expectation definitions were inspected in the installed game. Assembly inspection proves API availability, not gameplay behavior. Player rules are in the [README](../README.md).

## Main APIs

| Purpose | APIs used |
| --- | --- |
| State and persistence | GameComponent, GameComponentTick, FinalizeInit, Scribe_Values, Scribe_References, Scribe_Defs, Scribe_Deep, Scribe_Collections |
| Eligibility and discovery | IsFreeNonSlaveColonist, Faction, HostFaction, Adult, IsQuestLodger; PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists |
| Expectations and traits | ExpectationsUtility.CurrentExpectationFor(Pawn), ExpectationDef.order; TraitSet.HasTrait, TraitDefOf.Ascetic/Greedy/Nudist/Brawler |
| Request observations | Map.IsPlayerHome, Need_Joy.CurLevel, Thing.HitPoints/MaxHitPoints, ThingRequestGroup.HaulableEver |
| Equipment availability | EquipmentUtility.CanEquip, ApparelUtility.HasPartsToWear, WorkTagIsDisabled(WorkTags.Violent), IsForbidden, CanReach(PathEndMode.Touch, Danger.Some) |
| Bedroom and stages | OwnedBed, OwnersForReading, Medical, ForColonists; Room.Role/Owners/ContainedBeds/GetStat; Impressiveness.scoreStages/GetScoreStageIndex |
| Equipped items | AllEquipmentListForReading, WornApparel, CompQuality, TryGetQuality, QualityCategory |
| Actual recreation | JoyUtility.JoyTickCheckEnd, curDriver.asleep, CurrentBed, RoomRoleDefOf.Hospital |
| Development diagnostics | Prefs.DevMode; existing game ticks, localization, and Widgets APIs |
| Consequences and UI | ThoughtMaker.MakeThought, Thought_Memory, RemoveMemoriesOfDef, TryGainMemory; MainTabWindow, Widgets, FloatMenu, CreateConfirmation, Messages.Message |

Room targets use the game's standard stages and localized labels. CurrentExpectationFor(Pawn) derives its map expectation from WealthTotal, then applies the game's relevant pawn adjustments. The mod does not add a second wealth multiplier. Trait rules use actual Core defs. Equipment comparisons stay within the same ThingDef, so skill-based ranking of unrelated weapons and invented preferences are unnecessary.

Generation and diagnostics share candidate evaluation and deficit projection; the selected-pawn Dev action shares the ordinary observation/offer path. See [generation diagnostics](GENERATION.md) for blockers, queue selection, schedules, and migration. Requests observe needs every 2,500 ticks, separately from 250-tick active-promise checks. Consecutive observations accumulate only bounded intervals with the same deficit signature. Losing observability, satisfying the need, changing the tracked equipment, or entering a cooldown restarts observation. This is sampled persistence, not proof of every intervening tick. Candidate stock must be spawned, reachable, non-forbidden, usable, and match a loaded equipped item's def.

## Recreation accounting

The hooked signature is:

```csharp
bool JoyUtility.JoyTickCheckEnd(
    Pawn pawn, int delta, JoyTickFullJoyAction fullJoyAction,
    float extraJoyGainFactor, Building joySource)
```

Examples of callers are JobDriver_Skygaze, JobDriver_SocialRelax, and JobDriver_GoForWalk. Their activity callbacks identify actual recreation more specifically than a timetable or joyKind alone.

A Harmony **Prefix** captures eligibility before the utility can end the job or advance its toil. CanCredit excludes absent, suspended, sleeping, downed, drafted, mentally disturbed, and medically confined pawns, and requires a joy need and a recreation job.

CreditLeisure selects the pawn's own active record. The saved LastLeisureCreditTick independently protects each colonist from duplicates. LeisureCredit clips the non-overlapping part of [now − max(0, delta), now] to acceptance, the last credited tick, and the deadline. Same-tick duplicates add nothing; the target caps progress; callbacks after expiry are rejected.

No activity is inferred between callbacks. The callback's delta is trusted, and eligibility is checked at callback time rather than at every preceding tick. Stale or oversized intervals from another mod can include time not independently verified by Colony Promises. The request conversion retains this accounting path.

The 4–8-hour targets over 3–4 days require about 1.3–2 hours of recognized recreation per day, with sessions allowed to accumulate. This arithmetic supports the initial range; job availability, joy saturation, tolerance, and player schedules still require gameplay testing.

## Why Harmony is needed

Periodic checks cannot detect every remove-and-return interruption within 250 ticks. Existing hooks remain narrowly targeted:

- Equipment/apparel removal notifications pass the physical removed item.
- UnclaimBed passes the previous bed; successful ClaimBedIfNonMedical passes the assigned bed. InvalidateThing ignores null and only resets continuous holds tracking that exact item. Recreation is excluded.
- Room notifications match cached ObservedRoom. Shape changes reset holds unconditionally; contents, terrain, bed type, and roof changes reset them if conditions fail.
- CompQuality.SetQuality invalidates the tracked item; Pawn.DeSpawn resets that pawn's continuous hold.
- Pawn.Kill and SetFaction check closure after the operation. Death, destruction, a missing pawn, or Faction != Faction.OfPlayer closes the obligation without penalty.

Invalidation skips events while Scribe is active. FinalizeInit rebuilds active indexes and room references, preserving the saved hold when the same tracked item still qualifies. Load-time engine events and full reference reconstruction remain subject to [manual save tests](MANUAL_TESTS.md); isolated field serialization does not establish them.

New request generation, acceptance, and persistence need no extra Harmony patches. Acceptance rechecks relevance and snapshots consequences. Unaccepted closures bypass PromiseConsequences, preserving trust and the repeat ledger.

History formatting uses plain NamedArgument placeholders and preformatted numeric strings. The inspected grammar resolver treats a colon as a language-function call, not a .NET numeric format clause. Completed records retain the applied mood/trust values and resolution time; saved availability flags distinguish known zeros from missing old fields. The history fixture exercises the actual translator and scalar Scribe round-trips, without loading Unity UI or a complete save.

## Known limitations

- Caravans generate no requests or simulated progress. Open requests can expire; accepted deadlines keep running. Recreation accumulated before departure remains; continuous holds restart.
- Equipment requests use existing loose stock, not recipes, inventory, traders, or a forecast of future purchases. Stock disappearing makes the unaccepted request obsolete. Later stock changes cannot rewrite accepted terms.
- Generic usability checks do not establish compatibility with every mod's custom restrictions.
- Other mods' recreation counts only through the hooked utility and eligibility checks.
- Changes bypassing standard ownership, container, quality, or room notifications can evade immediate invalidation. Sampled checks cannot prove every brief interruption.
- Room shape or item quality changes reset holds even when they improve conditions. Durability is checked periodically; unusual repair/damage cycles between checks may be missed.
- Old promises retain legacy consequence calculations; new promises use saved acceptance-time effects. Old save migration and full state/reference loading require gameplay tests.
- Runtime patch execution, mod combinations, and safe removal from saves remain unverified. See [development checks](DEVELOPMENT.md#verification) and [manual scenarios](MANUAL_TESTS.md).
