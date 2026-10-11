# Technical reference

## Target and scope

Colony Promises targets **RimWorld 1.6**, with API signatures and relevant call paths checked against **1.6.4871 rev591** (`Assembly-CSharp.dll`, assembly version **1.6.9676.17735**). Harmony is the only required mod dependency; DLC is optional. Assembly inspection establishes API availability, not behavior in a running game. The [README](../README.md) is the player guide and contains the current reward and deadline table. This file is the single technical reference, including build instructions and test scenarios.

## Project structure

| Location | Responsibility |
| --- | --- |
| `About/`, `Defs/`, `Languages/` | Metadata, button/letter/thought definitions, English and Ukrainian localization |
| `LoadFolders.xml`, `1.6/Assemblies/` | RimWorld 1.6 loads root content and the versioned assembly |
| `Source/Data/` | Saved promises, requests, observations, trust, history, and independent success/recovery ledgers |
| `Source/Balance/` | `PromiseBalance`, `RequestRules`, `TrustRules`, and pure time arithmetic |
| `Source/Promises/` | Condition registry and shared continuous-hold progress |
| `Source/Requests/` | Candidate evaluation, observations, queue selection, acceptance/closure, and shared visibility/relevance predicates |
| `Source/Consequences/` | Previews and snapshots, memories, trust changes, and unaccepted recovery |
| `Source/Integration/` | Partial `GameComponent_Promises`, Harmony hooks, mod initialization, and settings |
| `Source/UI/` | Tab, list/count badge, letters, history, and formatted effect text |
| `Source/Diagnostics/` | Development-mode rendering using the generation evaluator |
| `Tests/`, `Tests/GameChecks/` | Pure rules, compiled fixtures, and the shared translator/Scribe host |
| `tools/` | Assembly, XML, and localization verification |

`Source/ColonyPromises.csproj` uses recursive SDK source inclusion. The core-check project links `Source/Balance/PromiseBalance.cs` and excludes game fixture sources. Moving files does not require changing the `ColonyPromises` namespace. Preserve saved type identities, enum values, Scribe keys, existing DefNames, and `lanoz.colonypromises` when reorganizing or extending the project.

## Build and runtime files

Use a .NET SDK and .NET Framework 4.7.2 reference assemblies to build the `net472` mod. Automated .NET hosts additionally require the **.NET 10 SDK**. The project references the Windows installation layout, `RimWorldWin64_Data/Managed`, and a separately installed Harmony `0Harmony.dll`.

Run from the repository root, replacing the example paths with your installations:

```powershell
dotnet build Source/ColonyPromises.csproj -c Release -p:RimWorldDir="C:\Path\To\RimWorld" -p:HarmonyPath="C:\Path\To\0Harmony.dll"
```

The output is `1.6/Assemblies/ColonyPromises.dll`. Game and Harmony references have `Private=false` and must not be redistributed. An installable package needs only `About/`, `Defs/`, `Languages/`, `LoadFolders.xml`, and the versioned DLL, plus resources actually used by the mod. Preserve an existing preview or Workshop ID when updating a package. Source, tests, tools, documentation, intermediates, and third-party DLLs do not belong in it.

## Main game APIs

| Purpose | APIs used |
| --- | --- |
| Component lifecycle and saves | `GameComponent`, `GameComponentTick`, `FinalizeInit`; `Scribe_Values`, `Scribe_References`, `Scribe_Defs`, `Scribe_Deep`, `Scribe_Collections` |
| Eligibility and discovery | `IsFreeNonSlaveColonist`, `Faction`, `HostFaction`, `Adult`, `QuestUtility.IsQuestLodger`; `PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists` |
| Expectations and traits | `ExpectationsUtility.CurrentExpectationFor(Pawn)`, `ExpectationDef.order`, `TraitSet.HasTrait`, Core Ascetic/Greedy/Nudist/Brawler defs |
| Need and stock observations | `Map.IsPlayerHome`, `Need_Joy.CurLevel`, `HitPoints/MaxHitPoints`, `ThingRequestGroup.HaulableEver` |
| Equipment usability | `EquipmentUtility.CanEquip`, `ApparelUtility.HasPartsToWear`, `WorkTagIsDisabled(WorkTags.Violent)`, `IsForbidden`, `CanReach(PathEndMode.Touch, Danger.Some)` |
| Bedroom | `OwnedBed`, `OwnersForReading`, `Medical`, `ForColonists`; `Room.Role`, `Owners`, `ContainedBeds`, `GetStat`; Impressiveness stages |
| Housing dissatisfaction | Exact Core `ThoughtDef.defName` allowlist; `Thought_Situational.RecalculateState` / `Active`, `Thought_Memory.ShouldDiscard`, `MoodOffset`, `ThoughtUtility.ThoughtNullified`, `MemoryThoughtHandler.Memories`; `CostListCalculator.CostListAdjusted` |
| Shared bedroom | `LovePartnerRelationUtility.IsLovePartnerRelation` / `LovePartnerRelationExists`, `Pawn.relations.DirectRelations`, `BedUtility.WillingToShareBed`, `GetSleepingSlotsCount`, `Building_Bed.SleepingSlotsCount`, `RestUtility.CanUseBedEver` / `CanUseBedNow` |
| Equipped items | `AllEquipmentListForReading`, `WornApparel`, `CompQuality`, `TryGetQuality`, `QualityCategory` |
| Recreation | `JoyUtility.JoyTickCheckEnd`, `curDriver.asleep`, `CurrentBed`, `RoomRoleDefOf.Hospital` |
| Effects | `ThoughtMaker.MakeThought`, `Thought_Memory`, `RemoveMemoriesOfDef`, `TryGainMemory`, `Messages.Message` |
| UI and notices | `MainTabWindow`, `Widgets`, `FloatMenu`, `Dialog_MessageBox.CreateConfirmation`; `ChoiceLetter`, `DiaOption`, `LetterMaker.MakeLetter`, `LetterStack.ReceiveLetter` |
| Navigation, badge, settings | `MainTabsRoot.SetCurrentTab`, `MainButtonDef.TabWindow`, `MainButtonWorker_ToggleTab.DoButton`; `Mod.GetSettings`, `ModSettings.ExposeData`, `Listing_Standard.CheckboxLabeled`, `Prefs.DevMode` |

Room descriptions use standard Impressiveness stages and their game-localized labels. `CurrentExpectationFor` already derives map expectations from `WealthTotal`, with the game's applicable pawn title/role adjustments; no second wealth multiplier is added. Only represented Core traits affect targets. There is no invented preference or skill-based ranking of unrelated items.

## Request generation

### Registration, schedules, and queue

`Game.FillComponents` discovers non-abstract `GameComponent` subclasses and constructs missing components with a `Game` argument. `GameComponentUtility` forwards finalization and tick callbacks. This provides registration for new games and saves predating the mod. `FinalizeInit` rebuilds pawn, active-promise, and open-request indexes; opening the tab is not required for enrollment.

Generation scans every **2,500 ticks**. Active promises and indexed open requests are maintained separately every **250 ticks**, or sooner at a deadline. Open maintenance checks expiry, relevance, recovery proof, and reminders without scanning every colonist each tick. A new component's first scan is due immediately; its initial offer gate starts half a day after initialization. Loaded observation and offer schedules retain their saved values.

A scan discovers eligible colonists across maps, caravans, and travelling transporters, then evaluates tracked states:

1. Close requests for missing, dead, destroyed, or non-player-faction pawns; maintain existing requests.
2. Clear observations for occupied slots, individual cooldowns, or ineligible pawns.
3. Evaluate one candidate per direction for spawned, unsuspended colonists on a player home map, excluding downed, drafted, and mental-state pawns.
4. Project observation only while the signature matches and the previous sample is no more than one observation interval old. The first sample adds zero. Missing candidates, changed signatures, backwards time, and longer gaps restart evidence.
5. After **120,000 observed ticks**, collect ready candidates. When the colony-wide gate permits, choose one, clear that pawn's observations, and move the gate forward **30,000 ticks**.

Observation continues for others while the global gate is closed. Selection orders by observed duration, then saved `StartTick`, pawn ID, and kind. Duration caps at four days; start time preserves older candidates' priority at that cap. A finite queue of continuously eligible, stable deficits progresses, but changing conditions can postpone an individual indefinitely. Individual cooldowns do not observe deficits: a fresh two-day observation follows them.

### Candidate rules

| Direction | Evaluation and signature |
| --- | --- |
| Housing | A recognized negative situational thought or confirmed sleep memory plus matching current conditions. Use the first category that removes its penalty, not an expectation floor. Explicit barracks improvements retain the room type when quality is the issue. Versioned signatures track the pair/pawn, requirement, and persistent causal anchor. |
| Recreation | Joy must be strictly below 35%. `PrepareTerms` derives 4–8 hours from total and severe evidence. Signature is `joy`; general mood is not a candidate source. |
| Equipment | Examine supported equipped items in stable item-ID order. Below-Normal quality requests the next quality step of the same def; apparel below 50% durability requests replacement. All apparel targets require at least 80% durability. Signature includes physical item, target quality, and worn state. |

Equipment must have a quality component and a player-acquirable loaded def. Candidate stock must be spawned, reachable, non-forbidden loose stock of the same def, meet quality/durability, and pass equipping checks. A deficient item alone does not suffice: suitable stock must remain available during sampled observation. Nonviolent, Brawler, Nudist, and body-part restrictions apply as appropriate. No recipe, trader, inventory, or future-availability forecast is used.

`Relevant` rechecks the need before acceptance and during maintenance. Equipment terms must still match the current candidate's def, quality, and durability. Satisfied targets cannot be accepted. An offer stores its reason key, expectation def, target, significance, response expiry, and fulfillment deadline length.

### Housing evidence and targets

Core definitions and `Toils_LayDown.ApplyBedThoughts` distinguish one-day sleep memories from situational workers:

| Verified Core cause | Effect and activation | Treatment |
| --- | --- | --- |
| `SleptInBedroom` | Awful stage −2; dull has no memory stage. Added for sleeping in the owned, non-prisoner bed in a Bedroom; Ascetics skip room-quality sleep memories. | Confirm current owned-bed role and projected negative room effect; first neutral category is sufficient. |
| `SleptInBarracks` | Stages range from −7 to −1 before becoming positive at stage 6. Uses the owned bed and Barracks role. | A negative memory is insufficient if the current barracks is already nonnegative. An explicit barracks target avoids unjustified relocation. |
| `SleptOnGround` | −4; the sleep code checks a missing bed or empty adjusted material-cost list, including free sleeping spots. | Confirm the usable assigned bed is missing or still has no material cost. A new ground-sleep requirement excludes free sleeping spots. |
| `SleptOutside` | −4; sleep occurs in a psychologically outdoor room. Rough-living precepts can nullify ground/outdoor memories. | Recheck that a usable assigned indoor bed has been provided and the thought is not nullified. Walking indoors without assigned housing does not erase a confirmed outdoor-sleep problem. |
| `Greedy` | Situational −8/−6/−4 depending on the owned room; first inactive stage corresponds to room stage 4. | Use the real worker and mood effect; its actual threshold constrains the housing target. |
| `WantToSleepWithSpouseOrLover` | Situational −4. The worker checks eligible known partners and spouse/non-spouse willingness; a common assigned bed **or owned room** suppresses it. | Require the actual negative state, then the existing unambiguous pair and shared-sleep eligibility rules. |

`ApplyBedThoughts` removes previous sleep memories and adds current ones when the laying job applies bed thoughts. Inspection establishes these call paths and effects, not a successful live sleeping scenario. No translated label determines classification. `HousingRules.Evaluate` recalculates temporary situational thoughts without writing to the pawn's thought cache, reads unexpired negative memories through `MoodOffset`, and returns the same issues used by generation and diagnostics. Unrelated food, pain, and general mood are excluded by exact cause identity.

`HousingWitness` preserves the observed negative memory effect per colonist. A retained witness counts only while current bed availability/material cost, room role, exposure, and projected room effect still confirm that same issue and it is not nullified. It carries no old observation duration. This bridges the gap before the next sleep memory; repaired conditions immediately invalidate it. Diagnostics distinguish current situational effects, active memories, retained effects at last confirmation, and unsupported/unverified conditions.

Targets use the first nonnegative game stage, with Greedy constraints for either participant. Expectations remain available to game thought eligibility and offer metadata; they do not manufacture dissatisfaction or add another wealth multiplier. `RequiresBarracks`, `RequiresConstructedBed`, and `HousingBased` apply only to new terms. Barracks targets allow other room owners but retain assigned bed identity, enclosure, medical exclusions, and one-day holds. Shared targets still require exactly the two participants to own one usable bed. Temperature, disturbances, jealousy, and Ascetic quality reduction are diagnosed as unsupported rather than converted to an unrelated quality increase.

Signatures begin `housing-v2:` and retain a still-current causal anchor even if the other partner develops an additional concern. A change of solution restarts evidence; if the anchor disappears, another concern starts fresh. The offer's localized reason is rendered from saved subject references, exact causes, and observed effects, not saved translated prose. Only the anchor is described as having met the two-day threshold. Unanswered requests recheck that original cause and target. Obsolete closure has zero consequences; actually fulfilled eligible recovery still uses the existing continuous proof. Pre-evidence open housing requests close obsolete, while accepted old terms never consult new thought-generation rules.

### Shared bedroom candidates

`SharedBedroomRules` selects a reciprocal, unambiguous living romantic partner from direct relations. `WillingToShareBed` checks the game's shared-bed and spouse/non-spouse willingness events for both pawns; relation type alone is insufficient. Core pawns without an ideology remain supported. Multiple partners, an ineligible partner, an Ascetic on either side, unavailable same-home-map observation, either occupied/cooling slot, or no player-acquirable loaded bed type usable by both blocks a candidate. These reasons come from the same evaluator used by diagnostics, and suppress conflicting new individual bedroom candidates.

The lower pawn ID owns the pair's observation and queue entry. The unordered `PairKey` prevents duplicate ordering. One partner's confirmed persistent issue is sufficient; the target resolves it while respecting both participants' actual Greedy and sharing constraints. The saved reason identifies the affected partner and effect, not assumed dissatisfaction of both. Generation retains hourly samples, two-day persistence, and colony-wide half-day spacing. Check now on the second participant also observes the canonical owner without bypassing the gates.

`SharedBedroomCommitment` is the authoritative record. It owns the stable ID, participants, one open request or active promise, and two frozen personal consequence snapshots. Pawn states expose runtime aliases to that record for both personal panels and indexes. Creation rechecks both slots; acceptance rechecks both participants, sharing rules, observability, cooldowns, and actual unsatisfied conditions. `AcceptShared` computes both previews before snapshotting the common terms and starts one new hold/deadline. Significance and deadlines use normal bedroom rules, not a two-person multiplier.

Shared observation requires the same spawned, non-medical colonist bed with at least two sleeping slots, exactly the two owners, and `CanUseBedNow` for both. Private terms require standard Bedroom role and no other room owners; new explicit barracks terms require Barracks role and permit other room owners. Both require enclosure and no medical beds. Both eligible pawns must be observable on the same map; physical simultaneous sleep is unnecessary. Common progress uses the existing hold arithmetic and interruption hooks. A lookup through either participant invalidates the same record; recreation is unaffected.

Maintenance runs the shared record once in the periodic active/open pass. Invalid relationship/sharing rules, death, destruction, or non-player faction closes it without penalty for both. Missing relationship trackers mean unknown, not breakup; unobservable conditions reset hold without cancelling the obligation. Acceptance/decline/withdrawal from either panel operates on the common record. `CompleteShared` marks it resolved and removes it before personal effects, releases both attached slots, and records each partner's actual effects and stored partner label. `ResolveShared` sends one result notification only after a successful transition. Unaccepted recovery independently checks each participant's existing trust, proof, cooldown, and repeat gates against the still-open aliases before consuming them.

### Development diagnostics

`Evaluate`, `Project`, and `PrepareTerms` serve both generation and diagnostics. `PeekState` avoids enrollment just to display a pawn; consequence previews operate on a copy. Viewing diagnostics therefore does not reset clocks, modify terms, or create requests.

Enable standard Development mode and expand diagnostics in the Colonists detail panel, above history. It shows exclusions, slot/cooldown state, current/target conditions, saved/projected evidence, selected-pawn last check, colony last/next scan, and global gating. Initial selection can include an ineligible selected map pawn in Development mode; a valid remembered selection takes precedence on reopening.

`Check now` uses ordinary observation and offer methods for the selected pawn. It respects eligibility, persistence, cooldown, occupancy, and frequency rules, but checks that pawn rather than the whole priority queue. It does not advance the scheduled scan. Same-tick repeats add no evidence; there is no forced reward-bearing request or per-scan logging.

## Promise and request lifecycle

Acceptance revalidates the request, snapshots effects, removes the open slot, and installs the terms as an active promise. It resets progress and credit clocks to acceptance, including any unaccepted proof. `FrozenConsequences` prevents resnapshotting. The deadline is acceptance plus the stored fulfillment length.

Active checks observe conditions, then resolve fulfillment before expiry. Withdrawal runs a normal check first, so completion or deadline resolution takes precedence. Resolution clears the active slot/index before external effects and the result notification. Unaccepted closure consumes the request before recording its outcome. These terminal transitions prevent repeat effects and notifications.

Decline, expiry, satisfaction without acceptance, and obsolescence bypass accepted-promise mood effects and the success ledger. They use the unaccepted cooldown. Death, destruction, a missing pawn, or `Faction != Faction.OfPlayer` closes an obligation without penalty; temporary absence does not establish faction departure.

### Continuous holds

`PromiseProgress` shares hold arithmetic between promises and eligible open recovery. Bedroom holds track the physical bed and runtime room; equipment holds track the physical item. Failed conditions or changed identity reset progress. `VerifiedDelta` credits only nonnegative intervals no greater than 250 ticks, clipped to the deadline. Missing samples or longer gaps earn no retrospective time.

### Recreation accounting

The hooked signature is:

```csharp
bool JoyUtility.JoyTickCheckEnd(
    Pawn pawn, int delta, JoyTickFullJoyAction fullJoyAction,
    float extraJoyGainFactor, Building joySource)
```

Callers such as `JobDriver_Skygaze`, `JobDriver_SocialRelax`, and `JobDriver_GoForWalk` provide activity callbacks rather than timetable intentions. A **Prefix** checks the job before the utility can end it or advance its toil. Eligibility requires a joy need, recreation job, awake driver, and observable pawn; downing, drafting, mental states, medical beds, and hospitals are excluded.

`CreditLeisure` selects that pawn's active record or eligible open recovery record. Saved `LastLeisureCreditTick` protects each pawn independently. `LeisureCredit` takes the non-overlapping part of `[now - max(0, delta), now]`, clipped to creation, last credited tick, and deadline. Same-tick duplicates add nothing; progress caps at target, and callbacks after expiry are rejected. Open recovery uses offer/response ticks; acceptance starts fresh.

No activity is inferred between callbacks. The supplied `delta` is trusted and eligibility checked at callback time, so stale or oversized intervals from another mod can include time not independently verified here. Other recreation counts only if it uses this utility. Targets imply roughly 1.3–2 hours daily; job availability, joy saturation, tolerance, and schedules still require gameplay testing.

## Harmony integration

Periodic checks cannot detect removing and returning an item between samples. Hooks cover standard interruptions and lifecycle changes:

| Targets | Hook and purpose |
| --- | --- |
| `JoyUtility.JoyTickCheckEnd` | Prefix credits activity before job state changes. |
| `Pawn_Ownership.UnclaimBed`, `ClaimBedIfNonMedical` | Prefix passes the previous bed; successful claim Postfix passes the assigned bed. |
| `Pawn_EquipmentTracker.Notify_EquipmentRemoved`, `Pawn_ApparelTracker.Notify_ApparelRemoved` | Prefix passes the removed physical item. |
| `CompQuality.SetQuality`, `Pawn.DeSpawn` | Prefix invalidates the item or that pawn's hold. |
| `Room.Notify_ContainedThingSpawnedOrDespawned`, `Notify_TerrainChanged`, `Notify_BedTypeChanged`, `Notify_RoofChanged`, `Notify_RoomShapeChanged` | Postfix matches cached `ObservedRoom`. Shape changes always reset; other changes reset if conditions fail. |
| `Pawn.Kill`, `Pawn.SetFaction` | Postfix checks closure after the operation. |

`InvalidateThing` ignores null and resets only continuous records whose `StableThing` is that exact item, across active and open indexes. A bedless pawn's `UnclaimBed` cannot clear another pawn's recreation. Pawn invalidation affects its own individual hold or the common hold shared with its partner. Shape and quality changes reset holds even when they improve conditions. Relationship and shared-sleep rule changes are checked during maintenance; no new relationship Harmony patch is required.

Invalidation is suppressed while Scribe is active. `FinalizeInit` observes saved holds, preserving progress if the same item qualifies and rebuilding the room cache. This does not establish that all engine reconstruction events preserve a real save's progress. Notices, badges, settings, generation, and recovery use ordinary APIs without extra patches.

## Effects, balance, and extension

`Source/Balance/PromiseBalance.cs` is authoritative: `PromiseBalance` contains time units, legacy effects, trust bounds, repeats, and history limits; `RequestRules` contains generation/new terms; `TrustRules` contains low-trust scaling, recovery, and reminders. Thought XML supplies templates, not the final variable reward. `PromiseConsequences` applies saved mood through `moodPowerFactor` and duration through `durationTicksOverride`, replacing the same outcome's prior memory.

Significance is binary: missing bedroom/two-stage improvement, more than four recreation hours, or apparel both worn and poor is larger. New failure mood is `-(base success mood + 2) - round(3 * trust / 100)`, rounding midpoint disappointment away from zero. Repeat factor is `max(0.25, 0.5^recent same-kind successes)` over 15 days; penalties do not use it. Trust reward uses midpoint-to-even rounding and a minimum of one before clamping.

New success mood also multiplies by `0.25 + 0.75 * min(clamped trust, 40) / 40`, rounded to two decimals away from zero. Acceptance freezes effects, durations, and cooldown, including trust deltas clamped against acceptance-time trust. Application clamps again and records the actual delta. Existing frozen records stay unchanged; unfrozen legacy records retain their completion-time calculation.

### Unaccepted recovery

Only new offers set `RecoveryEnabled`. Availability requires the same open request, no active promise, trust at most 20, a passed ten-day recovery gate, and no same-kind recovery within fifteen days. Old requests default disabled; history is never replayed.

Continuous conditions require a verified day; recreation requires offered actual hours plus joy at least 35%. Proof is checked before response expiry. A satisfied request remains open while eligible proof is incomplete. The satisfied-unaccepted transition may grant two trust without a mood memory, records the actual change, stamps `RecentRecoveries` and `NextUnpromisedRecoveryTick`, and starts the normal unaccepted cooldown. Decline, expiry, obsolescence, or improvements without a request grant none. Recovery and success ledgers are independent of visible history and each other.

### Adding a request type

Append a stable `PromiseKind` value, register an `IPromiseCondition`, and extend shared candidate evaluation, relevance, requirement preparation, and balance. Reuse continuous-hold machinery where appropriate; accumulation needs a verified activity source with duplicate/time safeguards. Update recovery satisfaction/proof explicitly rather than assuming it fits every type. Add saved fields with conservative defaults, both localizations, and generation/acceptance/serialization/outcome/invalidation coverage. UI and diagnostics should consume shared rules, not calculate them independently.

## Persistence and localization

Scribe stores pawn/item references and defs, requests, observations, active progress, trust, history, cooldowns, snapshots, and separate success/recovery ledgers. Schedule keys include `nextRequestObservation`, `nextRequestOffer`, and `lastRequestObservation`. Deficits save signatures, durations, severe time, sample ticks, and `StartTick`; missing old starts use last sample minus saved duration without reconstructing evidence beyond the cap.

Missing collections initialize empty, trust defaults to 50 and is clamped, and absent last-check markers mean not recorded. Legacy targets, deadlines, progress, credit clocks, trust, history, and success stamps remain, including one-hour recreation records. Missing `FrozenConsequences=false` selects legacy effects and three-day cooldown. New frozen promises retain their effects. Runtime indexes and `ObservedRoom` are rebuilt, not serialized. Same-tick loading adds no time; observation gaps restart evidence and hold gaps earn no retrospective credit.

Housing migration adds `housingWitnesses` to pawn states and `housingReasons` to offers. New term flags default false, preserving old accepted room requirements and progress. Witnesses default empty. Old `room:`/`pair:` signatures cannot satisfy the new `housing-v2:` persistence gate. No accepted promise is cancelled because a thought disappears; only unanswered requests run the new relevance policy.

Shared records are deep-saved once under `sharedBedrooms`, with `nextSharedId` maintaining stable IDs. Participant states save `sharedId` and omit duplicate request/active aliases. Central records save participant references, terms, progress, notice flags, and both personal snapshots. After cross-reference resolution, finalization reconnects both states to the same object and rebuilds the common room cache. Conflicting saved individual obligations are never overwritten. Missing shared fields initialize empty; existing individual records retain their conditions and effects. Personal history adds a stored partner label so later relationship changes cannot rewrite it.

Letters save `requestPawn`; requests save `announced`, `reminderSent`, and `recoveryEnabled`. Missing old announcement flags default true to avoid re-announcement; recovery defaults false. Settings save `remindersEnabled`, default true. Notice flags are consumed before sending. The reminder window starts 30,000 ticks before expiry; disabling leaves the pending flag intact, so re-enabling before expiry can send the one remaining reminder.

`OpenRequests` is the read-only source for list and badge. Expired, terminal, accepted, and observable obsolete states are excluded immediately; absence alone does not prove obsolescence. Fulfilled requests remain visible only for eligible proof. `ChoiceLetter_Request` opens the existing window through `SetCurrentTab`, then `SelectPawn` switches to Colonists and resets detail scrolling. `MainButtonWorker_Promises` inherits toggle behavior and draws a badge without mutating Def labels.

The UI uses checked `TabDrawer.DrawTabs`/`TabRecord` APIs. Requests sorts a copy of current entries by response expiry, then pawn ID; it never displays a selected pawn's personal data. Colonists has separate list/detail scroll positions, highlighted selection, and read-only statuses from current visibility and cooldown. Ordinary reopening retains session-local navigation; selection validation handles removed pawns without changing the internal tab. This state is not added to game saves. `RequestedTabSize` is bounded by logical `UI.screenWidth/Height` with margin for game controls. Text and button heights use wrapped `Text.CalcHeight`, including long names. More details and Development-only diagnostics start collapsed; conditions, times, progress, and numeric effects remain visible.

History stores `ResolvedTick`, `AppliedMood`, and actual post-clamp `AppliedTrust`; rendering never recalculates effects. Availability flags distinguish zeros from absent fields. Older entries without the metadata marker use only explicit XML values; omitted defaults are not guessed. Penalty-free outcomes have a no-change description; recovery displays its recorded change. History trimming does not trim repeat ledgers.

Custom UI text belongs in both keyed/DefInjected localizations. Format numbers through `PromiseText`/`PromiseHistory` before plain translation placeholders. Fixed-arity `Translate` uses `NamedArgument` and `GrammarResolverSimple`: a colon invokes a language function, so `{2:0.00}` is not a safe .NET format. English decimal points, Ukrainian commas/day forms, signed effects, and unavailable-data messages are covered by translator fixtures.

## Automatic verification

Build Release first so fixtures use the current DLL. Run from the repository root; PowerShell fixtures use fresh Windows PowerShell processes. Content verification also requires a Mono.Cecil DLL through `CecilPath`.

```powershell
dotnet run --project Tests/ColonyPromises.CoreChecks.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/Verify-Requests.ps1 -RimWorldDir "C:\Path\To\RimWorld"
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/Verify-Invalidation.ps1 -RimWorldDir "C:\Path\To\RimWorld"
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/Verify-Generation.ps1 -RimWorldDir "C:\Path\To\RimWorld"
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/Verify-Features.ps1 -RimWorldDir "C:\Path\To\RimWorld"
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/Verify-History.ps1 -RimWorldDir "C:\Path\To\RimWorld"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Content.ps1 -RimWorldDir "C:\Path\To\RimWorld" -CecilPath "C:\Path\To\Mono.Cecil.dll"
```

| Check | Purpose |
| --- | --- |
| Core | Pure progress clipping, duplicate credit, deficit gaps, bounded targets, cooldowns, reward arithmetic |
| Requests | Compiled acceptance/closure, penalty-free outcomes, snapshots, repeat retention, old/new Scribe fields |
| Invalidation | Bedless `UnclaimBed` and Prefix; exact-item hold resets without clearing unrelated progress |
| Generation | Shared projection, read-only diagnostics, capped queue fairness, schedules, migration defaults, Dev guard, bilingual text |
| Features | Notice/settings flags, list/count transitions, low-trust/repeat rewards, recovery proof/gates/ledgers, pending invalidation; UI navigation and bilingual summaries; shared pair identity, slots, bed/room ownership, common aliases/holds, personal effects, penalty-free closure, notices, recovery, and Scribe references |
| History | Real translator for every outcome in both languages, applied effects/clamping, missing metadata, serialization |
| Content | Patch signatures/arguments, XML, source localization keys, bilingual placeholder parity |

`Tests/GameChecks` runs the real translator under .NET 10 because its .NET Standard 2.1 dependencies are absent from Windows PowerShell's CLR. Fixtures provide isolated language data and saved-type lookup, excluding engine back-compatibility converters from field round-trips. Shared fixtures register external pawn references with the actual Scribe cross-reference resolver and check canonical alias restoration. Cached room/bed fixtures exercise standard ownership/role predicates; they do not simulate a full map, live room calculation, ideologies, Unity windows, or complete migration. Signature/IL checks do not confirm live Harmony execution.

Housing fixtures read effect stages from the referenced installation's Core XML, exercise the compiled evidence predicates, minimum-category selector, candidate builder with supplied verified issues, anchoring, acceptance, bilingual attribution, and Scribe defaults/references. The .NET host cannot initialize the game's Unity-dependent mod configuration to run complete thought workers and nullifiers. Those runtime paths are inspected against the game assembly and remain in-game checks. If the installation differs from the project default, set `COLONY_PROMISES_GAME_ROOT` for the housing fixture's XML input.

Release compilation and these checks pass. History display has been verified in-game by the player. Generation, diagnostics, notices/navigation, badge layout, settings across full reloads, realistic recreation balance, full migration, room reconstruction, and mod combinations remain gameplay checks. No safe-removal guarantee is established.

## Manual test scenarios

Use copied saves, first with Core, Harmony, and Colony Promises only. Enable Development mode; inspect Promises, Needs, and the error log. Add DLC/mod combinations after a baseline run. These are expectations derived from code, not a record of completed gameplay tests.

### Reproducing generation

Use one eligible adult home-map colonist with no request/promise/cooldown, undrafted, conscious, and outside mental breaks. Suppress other deficits with joy at least 35%, a qualifying stage-5 bedroom, Normal-or-better gear, and apparel at least 50% durable. Use a separate branch per case, relaxing only its target need. Time starts at the first sample showing zero: **48 further hourly scans**, or two days plus up to an hour from setup, normally produce a request. Check the global gate/reset reasons instead of assuming elapsed time proves persistence.

| Case | Setup and expected result |
| --- | --- |
| Missing bed | Non-Ascetic without a usable assigned bed: first obtain an actual −4 `SleptOnGround` memory. Observe the still-unresolved bed problem for two days; expect a material-cost bed and minimally neutral room, not a request from absence alone. |
| Shared housing | Eligible reciprocal pair, no occupied/cooling slots, sharing permitted. Confirm an actual −4 sleeping-apart state or a supported negative housing memory of one partner. After two observed days plus queue delay: one request identifying that partner. Separate beds in the same satisfactory owned room, with no negative housing thought, create none. |
| Room upgrade | Sole-owned bed in a stage-0 Bedroom: obtain the actual −2 sleep memory. Keep the same problem for two days; expect the first neutral stage (dull under Core) and the existing smaller-request rewards. Improve before the threshold: old negative memory creates no request. Memory expiration without repair preserves evidence. |
| Barracks quality | Obtain a negative barracks sleep memory at stage 0; keep its physical cause for two days. Expect a barracks-quality requirement at the first nonnegative stage, not forced privacy. Stage-6 positive barracks memory and no other negative housing issue creates none. |
| Housing exclusions | Food/pain alone: no housing observation. Check Greedy's actual active/inactive boundary, rough-living nullification, and disabled spouse/non-spouse sharing. Temperature, disturbed sleep, Jealous, and negative Ascetic thoughts show unsupported reasons without arbitrary room demands. |
| Recreation | Joy below 15% for two observed days: normally six hours over four accepted days. Between 15% and 35% yields four hours over three days. Longer queued evidence can yield eight. Joy reaching 35% or lost observability restarts evidence. |
| Weapon | Violence-capable non-Brawler with Poor revolver and reachable/non-forbidden Normal revolver loose stock: same-def Normal target after two observed days, four-day deadline. Removed stock resets; restored stock needs fresh evidence. |
| Apparel | Non-Nudist with suitable body parts, Normal button-down shirt at 40%, same-def Normal replacement at 100% in usable stock: Normal/80% target after two observed days. Repairing to 50% removes deficit. |

Also test children, guests, prisoners, slaves, quest lodgers, trait/work restrictions, missing body parts, biocoded/forbidden/unreachable stock, missing quality, and loaded mod items. No unusable/missing-DLC target should appear. Short deficits and absent equipment alone create no request.

### Lifecycle, recovery, and saves

| Scenario | Action and expected result |
| --- | --- |
| Diagnostics/cadence | Pause/reopen: saved state unchanged. Check now before persistence, during cooldown, and behind global gate: no bypass or same-tick credit. Dev off hides panel. Several deficits get at most one offer per half day; older capped evidence keeps priority. Record sample/reset/offer ticks. |
| Review/relevance | Inspect both languages, exact effects/durations and confirmations. Cancel unchanged. Satisfy before accepting: reject reward-bearing acceptance. Remove stock: obsolete closure without penalty. No arbitrary creation controls. |
| Housing hold | Private individual terms require a sole-owned bed; shared terms require exactly the pair as bed owners. Both need the same qualifying bed/room for one day. Explicit new barracks terms allow other room owners; private and old individual terms do not. Medical beds, wrong room roles, and outdoors give no progress. A ground-sleep target cannot use a free spot. Unclaim/reclaim either partner, failed conditions, or shape changes reset the common hold; valid ordinary content improvements preserve. |
| Shared UI/results | One list row/count/letter/reminder; both panels reference the same request/progress/deadline. Accept/decline/withdraw from either panel, confirm both names. Compare trust 0/80 and different repeat ledgers: two exact personal snapshots and once-only histories/effects. Change trust after acceptance: frozen effects remain. Test both languages and partner navigation. |
| Shared relationship rules | Multiple partners: no random selection or individual bedroom fallback; Dev reason explains. Ascetic pair: blocked. Test optional shared-sleep prohibitions and changing them after acceptance, breakup, death, destruction, and faction exit: both close without penalty. Caravan, drafting, illness: deadline continues, no cancellation solely for absence. |
| Shared recovery/save | With both at trust 0, satisfy an unanswered shared request for one verified day before reply expiry: each eligible pawn gains +2 trust once, no mood. Repeat with only one eligible recovery ledger. Save during observation, open request, partial accepted hold, recovery, and after reminder/result; reload preserves one canonical record, both personal snapshots, flags, and qualifying hold. Load old individual records alongside it unchanged. |
| Equipment hold | Storage/inventory gives none; qualifying physical item needs a full equipped day. Brief removal, identical-copy swaps, quality changes, or failed durability reset only that hold. |
| Recreation | Recognized games, skygazing, social relaxation, walking accumulate. Timetable-only, travel, sleep, downing, drafting, hospitals, mental states, idle do not. Assess practical 4-hour/3-day and 6–8-hour/4-day targets with normal tolerance/saturation. |
| Null-item regression | A has partial recreation; bedless B claims a first bed paused: A's progress/clock unchanged. Unclaim B's tracked bed/remove C's tracked gear: only their holds reset. Unrelated items change none. Repeat with pending recovery proof. |
| Outcomes | Complete, break at high trust, withdraw before expiry, and finish exactly at deadline: one saved effect/result; fulfillment wins. Withdrawal at/after expiry cannot replace resolution. Death/faction exit closes penalty-free without repeats after reload. |
| Cooldowns/repeats | Decline/expire: no effects, three-day cooldown plus two fresh observed days. Accepted outcomes: five days. Repeated successes within fifteen days reduce rewards, not penalties; refusal/reload/history trimming retain ledger. Positive and same-outcome negative memories replace. |
| Frozen terms | Accept; change wealth/expectations/trust and age successes out. Terms/deadline/effects/durations stay fixed. Compare basic recreation versus substantial bedroom rewards. |
| Low/zero trust | New four-hour recreation at zero: +0.75 mood/+3 trust before repeats. Compare 39/40/41 for smooth scaling; maximum repeats at zero give +0.19 and at least +1 trust. No passive penalty/blocked acceptance. Existing frozen/legacy rules retained. |
| Unaccepted recovery | At trust at most 20, fulfill new unanswered room/gear early enough for one day before reply expiry. Recreation needs offered actual hours plus joy 35%. Once: +2 trust, no memory, actual history. Brief fulfillment/need manipulation alone gives none. |
| Recovery boundaries | Accept during proof: reset. Exact-expiry verified proof wins; insufficient proof expires without effects. Ten-day pawn gate/fifteen-day same-kind ledger survive refusal, reload, history trimming. Old requests/history gain none retroactively. |
| Letters/list/badge | One offer letter opens right pawn. Accept/decline/expire/satisfy/obsolete/kill/faction exit: no stale entry; eligible unfinished proof stays open. Archived letters do not count. |
| Reminders/settings | Offer +90,000 ticks: one reminder within maintenance cadence. Reload before/after: no duplicate. Disabled: announcements only; re-enable before expiry: at most pending reminder. Check setting after restart. |
| Caravan/capture | Depart with each request/promise, reload absent: clocks continue, requests can expire, no away credit/new requests, recreation retained, holds restart. Temporary capture without faction change does not alone cancel. |
| New/old saves | New game and pre-mod save initialize trust 50/fresh evidence without opening tab. Old manual promises, including one-hour recreation, preserve terms/progress/deadlines/clocks/trust/history and legacy effects. |
| Save continuity | Save partial deficits, requests, accepted/pending holds, recreation, snapshots, cooldowns, outcomes. Same-tick reload adds nothing/duplicates none; gaps earn no retrospective credit. Real item/room reconstruction preserves qualifying holds. |
| History/languages | Every outcome, fractional rewards, clamped zeros, recovery in both languages. Half-day age uses 0.5/0,5 with signed actual effects. Reload keeps effects. Explicit old values remain; missing fields say unavailable. |
| Layout/performance | Requests contains no personal data, sorts nearest expiry first, and has an empty state. Rows/letters open the right Colonists details. Reopen retains tab/selection; removing the pawn or closing its request leaves no stale actions. Test independent scrolling, long names, both languages, small screens/UI scaling, full history, collapsed rules/Dev diagnostics, and no periodic result spam. Isolated layout bounds do not establish visual correctness. |

For missing-history migration, use a copied older save with individual `appliedMood`, `appliedTrust`, or `resolvedTick` absent, then save/reload again. This field exercise does not establish full reference loading or safe removal.

## Technical limits

- Deficits are sampled hourly, not verified every intervening tick. Unobservable/cooldown time is excluded; busy queues have no fixed maximum wait.
- Caravans generate no requests or simulated progress. Deadlines continue through absence, illness, combat, and suspension.
- Custom usability/recreation may fall outside standard APIs. Callback intervals are not independently reconstructed.
- Shared bedrooms require an unambiguous reciprocal pair and exclude Ascetics conservatively. Loaded usable bed types are checked, but research/material availability is not forecast. Simultaneous multi-partner housing is unsupported. Full-map shared generation, live ideology restrictions, room reconstruction, and visual pair navigation still need gameplay testing.
- Changes bypassing ownership, equipment, quality, or room notifications can evade immediate invalidation. Periodic durability checks can miss unusual damage/repair cycles.
- Shape and quality changes deliberately reset holds even when favorable. Engine reconstruction, full migration, live patches, and safe removal need game-level validation.

Housing witnesses remain available while a request is open. Acceptance, closure and cooldown observation clear them, so later requests require fresh evidence rather than reusing a previous problem.
