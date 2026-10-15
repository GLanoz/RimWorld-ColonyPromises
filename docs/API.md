# Technical reference

## Target and scope

Colony Promises targets **RimWorld 1.6**, with API signatures and relevant call paths checked against **1.6.4871 rev591** (`Assembly-CSharp.dll`, assembly version **1.6.9676.17735**). Harmony is the only required mod dependency; DLC is optional. Assembly inspection establishes API availability, not behavior in a running game. The [README](../README.md) is the player guide and contains the current reward and deadline table. This file is the single technical reference, including build instructions and test scenarios.

Mod version **0.2.3** is separate from the supported game version. The local `Version.txt` reports **1.6.4871 rev590**, while the referenced DLL's `VersionControl` derives **1.6.4871 rev591** from assembly version **1.6.9676.17735**. API checks target that DLL; `About.xml`, `LoadFolders.xml` and the versioned output folder continue to declare RimWorld **1.6** only.

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

`SubstanceSupport` handles definition-only recognition; `SubstanceNeeds` handles pawn/policy/supply evaluation and ingestion credit; `SubstanceCondition` lives beside the other promise conditions. `SubstanceKnowledge` is a readonly snapshot for one evaluation, grouping loaded drug definitions and effects once. It is never retained across ticks, ingestion, Def changes or save loading, so familiarity and ambiguous-effect detection cannot become stale. Public recognition entry points and the condition's namespace/type identity are preserved.

Natural and Dev shared-room checks use `SharedBedroomRules.HasUsableBedDefinition`; housing signatures reuse `RequirementSignature`. Manual tests retain their explicit persisted bypass and ordinary execution. Acceptance previews and Dev menu occupancy checks use `PeekState`, not enrollment. The colonist list evaluates open requests once per draw. No timer, balance, enum, Scribe key or frozen consequence changes accompany these reorganizations.

## Build and runtime files

Use a .NET SDK and .NET Framework 4.7.2 reference assemblies to build the `net472` mod. Automated .NET hosts additionally require the **.NET 10 SDK**. The project references the Windows installation layout, `RimWorldWin64_Data/Managed`, and a separately installed Harmony `0Harmony.dll` for RimWorld 1.6. In a versioned Harmony package, use `1.6/Assemblies/0Harmony.dll`; override `HarmonyPath` when your installation differs.

Run from the repository root, replacing the example paths with your installations:

```powershell
dotnet build Source/ColonyPromises.csproj -c Release -p:RimWorldDir="C:\Path\To\RimWorld" -p:HarmonyPath="C:\Path\To\0Harmony.dll"
```

The output is `1.6/Assemblies/ColonyPromises.dll`. Game and Harmony references have `Private=false` and must not be redistributed. An installable package needs only `About/`, `Defs/`, `Languages/`, `LoadFolders.xml`, and the versioned DLL, plus resources actually used by the mod. Preserve an existing preview or Workshop ID when updating a package. Source, tests, tools, documentation, intermediates, and third-party DLLs do not belong in it.

After a successful build, run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Prepare-Release.ps1`. It updates the sibling `ColonyPromises-release` directory; `-ReleaseRoot` selects another destination outside the source tree. The script preserves the release preview and Workshop ID before cleanup, copies a source-only ID when needed, and restores the ID to `About/PublishedFileId.txt`. Different source/release IDs stop packaging before any changes. When neither folder has an ID, none is created. `PublishedFileId.txt` is local metadata ignored by Git at every directory level.

## Main game APIs

| Purpose | APIs used |
| --- | --- |
| Component lifecycle and saves | `GameComponent`, `GameComponentTick`, `FinalizeInit`; `Scribe_Values`, `Scribe_References`, `Scribe_Defs`, `Scribe_Deep`, `Scribe_Collections` |
| Eligibility and discovery | `IsFreeNonSlaveColonist`, `Faction`, `HostFaction`, `Adult`, `QuestUtility.IsQuestLodger`; `PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists` |
| Expectations and traits | `ExpectationsUtility.CurrentExpectationFor(Pawn)`, `ExpectationDef.order`, `TraitSet.HasTrait`, Core Ascetic/Greedy/Nudist/Brawler defs |
| Need and stock observations | `Map.IsPlayerHome`, `Need_Joy.CurLevel`, `HitPoints/MaxHitPoints`, `ThingRequestGroup.HaulableEver` |
| Equipment usability | `EquipmentUtility.CanEquip`, `ApparelUtility.HasPartsToWear` / `CanWearTogether`, `ApparelProperties.PawnCanWear`, `HasBasicApparel`, `PsychologicallyNude`, `IsLocked`, `WorkTagIsDisabled(WorkTags.Violent)`, `IsForbidden`, `CanReach(PathEndMode.Touch, Danger.Some)` |
| Bedroom | `OwnedBed`, `OwnersForReading`, `Medical`, `ForColonists`; `Room.Role`, `Owners`, `ContainedBeds`, `GetStat`; Impressiveness stages |
| Optional DLC compatibility | `ModsConfig` active switches; `HighestTitleWithBedroomRequirements`, `GetBedroomRequirements`, `RoomRequirement.Disabled` / `Met` / `Label`; `Pawn_ApparelTracker.AllRequirements`, `RequiredForPawn`, `IsRequirementActive`, `Precept_Apparel.TargetGender`, `IdeoPrefersNudityForGender`, `GetAllPreceptsAllowingSituationalThought` |
| Housing dissatisfaction | Exact Core `ThoughtDef.defName` allowlist; `Thought_Situational.RecalculateState` / `Active`, `Thought_Memory.ShouldDiscard`, `MoodOffset`, `ThoughtUtility.ThoughtNullified`, `MemoryThoughtHandler.Memories`; `CostListCalculator.CostListAdjusted` |
| Shared bedroom | `LovePartnerRelationUtility.IsLovePartnerRelation` / `LovePartnerRelationExists`, `Pawn.relations.DirectRelations`, `BedUtility.WillingToShareBed`, `GetSleepingSlotsCount`, `Building_Bed.SleepingSlotsCount`, `RestUtility.CanUseBedEver` / `CanUseBedNow` |
| Manufacturing feasibility | `RecipeDef.AvailableNow` / `AvailableOnNow`, `ThingDef.AllRecipes`, `Building_WorkTable.CurrentlyUsableForBills`, work/skill and manipulation capacity checks, ingredient filters and `CountRequiredOfFor`, `GenStuff.AllowedStuffsFor` |
| Equipped items | `AllEquipmentListForReading`, `WornApparel`, `CompQuality`, `TryGetQuality`, `QualityCategory` |
| Substances | `CompProperties_Drug`, `ChemicalDef`, `Hediff_Addiction.Chemical`, `IngestionOutcomeDoer_GiveHediff`, `Thing.Ingested`, `PawnUtility.CanTakeDrug`, `FoodUtility.DrugIsSuitable`, `HasEverTaken` |
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
3. Evaluate one candidate per direction on a player home map. Housing and equipment exclude downed, drafted and mental-state pawns; recreation can retain known low-need evidence in a paused state without creating a candidate.
4. Project observation only while the signature matches and the previous sample is no more than one observation interval old. The first sample adds zero. Missing needs, changed signatures, backwards time, and longer gaps restart evidence. An equipment need with no feasible solution still has an observation signature; known paused recreation uses `ProjectEvaluation` to exclude unverifiable intervals.
5. After **120,000 observed ticks**, collect ready candidates. When the colony-wide gate permits, choose one, clear that pawn's observations, and move the gate forward **30,000 ticks**.

Observation continues for others while the global gate is closed. Selection orders by observed duration, then saved `StartTick`, pawn ID, and kind. Duration caps at four days; start time preserves older candidates' priority at that cap. A finite queue of continuously eligible, stable deficits progresses, but changing conditions can postpone an individual indefinitely. Individual cooldowns do not observe deficits: a fresh two-day observation follows them.

### Candidate rules

| Direction | Evaluation and signature |
| --- | --- |
| Housing | A recognized negative situational thought or confirmed sleep memory plus matching current conditions. Use the first category that removes its penalty, not an expectation floor. Explicit barracks improvements retain the room type when quality is the issue. Versioned signatures track the pair/pawn, requirement, and persistent causal anchor. |
| Recreation | `LeisureNeeds` evaluates joy below 35%; negative `NeedJoy` is optional corroboration. `PrepareTerms` retains 4–8 hours. The versioned signature is `joy-need-v2`. Known incapacity pauses evidence, never credits it. |
| Equipment | `EquipmentNeeds` requires a fresh negative situational effect plus its current physical cause for `ApparelDamaged`, Core `Naked`, or `BrawlerUnhappy`. `WeaponRanged` and `WeaponMelee` are skill-based acquisition desires; `WeaponUpgrade` is a separate personal desire for a better copy of an appropriate equipped weapon, without a new thought. Its versioned signature identifies model and target quality rather than physical item ID. |

Need evaluation is independent of target feasibility. `DeficitSignature` lets `ObservationCandidate` retain two-day evidence when a complaint or desire has no obtainable target. Generation, relevance and diagnostics share the same evaluator. Worn apparel retains the existing larger reward tier when the original item was below Normal; reward values and all other balance constants remain unchanged. Only weapon-upgrade fulfillment requires a quality comp among these new equipment requests.

`EquipmentNeeds.PreferredRanged` reads `SkillDefOf.Shooting` / `Melee`, `SkillRecord.Level`, `passion`, `TotallyDisabled`, and `TraitDefOf.Brawler`. Real violence/manipulation restrictions, Brawler and disabled skills precede level comparison. `RequestRules.WeaponSkillDifference` is four. A smaller difference retains an actually usable primary weapon type; without one, passion, level, then ranged-first tie order determine the type. `ThingDef.IsRangedWeapon` identifies a weapon with a non-melee verb; extra melee tools do not make it a melee target. `IsMeleeWeapon` is the game's complementary weapon predicate. DefName ordering keeps candidate selection stable. Stock/recipe feasibility is unchanged and never supplies a wrong-type fallback.

`UpgradeQuality` uses the centralized Normal/Good thresholds and `WeaponUpgradeSkill` (10): below Normal targets Normal; Normal with the matching skill at least 10 targets Good. The primary must already have quality, be usable, and match the selected type. Generation and unanswered-request relevance use `UpgradeTarget`; Brawler's active complaint preempts this desire even without a feasible melee solution. Terms use the same weapon Def, required quality, normal equipment reward tier and one-day physical-item hold. `EquipmentCause = WeaponUpgrade` fits existing string serialization; no enum or Scribe-key changes are needed. Acceptance freezes the ordinary equipment consequences. Completion does not consult current skill preference or desire, so later skill changes cannot reset an accepted hold. Swapping copies gives no extra observation time, and the existing cooldown/repeat ledgers remain authoritative.

For manual weapon checks, compare Shooting/Melee 15/2, 2/15, exactly four apart, and three apart with each equipped type. Then test unequipped pawns with different passions, ties and zero levels; Brawler with high Shooting; nonviolent pawns; wrong-type-only stock; and a manufacturing-only target. Equip a usable Poor weapon: no request before two observed days, then a Normal copy request. For Normal, test matching skill 9 versus 10; Good produces none. Accept, raise the other skill, save/reload, equip the promised same-model quality and retain it for a day. Confirm the target and effects stay fixed; a Legendary different model fails. These are reproducible scenarios, not completed in-game tests.

`Relevant` rechecks current causes before acceptance and during unanswered maintenance. New equipment targets must remain usable and obtainable; repairing or removing the original problematic piece invalidates its complaint. An accepted obligation never reevaluates its thought. Legacy unanswered quality-based offers close as obsolete before any recovery can apply.

### Recreation and equipment evidence

The inspected Core `NeedJoy` worker maps the recreation category to its stage. Need values below 35% are the primary evidence regardless of a pending thought update. `LeisureNeeds` reads the current negative effect only as corroboration; no variety requirement is inferred from stage descriptions. `ProjectEvaluation` pauses a still-low need during known sleep, downing, drafting or mental breaks. It preserves accumulated waking evidence, credits neither the paused interval nor the first waking interval, and resets on a missing/long sample gap or recovered need. Off-map time is never counted. A normally sleeping pawn needs more than two elapsed days to accumulate two observed awake days.

`EquipmentNeeds.NegativeMood` creates an unattached situational thought and calls `RecalculateState`, `Active` and `MoodOffset`, respecting the worker's prerequisites and nullifiers. It does not use translated names, mood totals, or old memories.

| Cause | Verified game rule | Target |
| --- | --- | --- |
| `ApparelDamaged` | Worker examines worn hit-point apparel with `careIfDamaged`, excluding locked pieces. Below 50% gives −3; below 20% gives −5 before modifiers. | Same def as one identified damaged piece; at least 80% condition throughout the hold. It must be replaced or cease to be damaged. Other damaged pieces are explicitly outside the request. |
| Core `Naked` | −6 before modifiers. `ThoughtWorker_PsychologicallyNude` is inactive with Ideology; otherwise uses `PsychologicallyNude`. Nudist and optional Furskin nullifiers apply. `HasBasicApparel` checks Legs and Torso; gender and missing legs affect the worker's result. | One compatible garment covering every missing required basic area. `HasPartsToWear`, `PawnCanWear`, body-part groups and `CanWearTogether` constrain selection. Completion also requires `PsychologicallyNude=false`. No single-garment solution means no target. |
| `BrawlerUnhappy` | Required Brawler trait, equipped ranged primary, −10 before modifiers. | Usable melee primary. No request from low quality, missing weapon or shooting skill alone; disabled violence excludes it. |

`CanObtain` first checks reachable, permitted loose stock against fixed parameters and `EquipmentUtility.CanEquip`. Otherwise it checks loaded worktable recipes, `AvailableNow` (including research/precepts), `AvailableOnNow`, usable tables, capable colonists, relevant work/skill restrictions, and reachable ingredient quantities. Ingredient stacks are reserved across requirements; each uses a conservative single-def material choice and stuff compatibility. No bills are created. Custom recipe workers, special products, speculative trades, mixed-material solutions and unsupported access rules are not treated as proof of feasibility. A definition-only `ThingWithComps` probe has no assigned game ID, spawned state or initialized comps; actual stock and equipped items also undergo instance checks. Manufacturing targets with custom item classes or non-game component/property classes are excluded; their availability requires dedicated support.

New terms save `needBasedEquipment`, `qualityOptional`, `equipmentCause`, `causeMood`, `problemThing`, `requireCoverage` and `requireMelee`, in addition to the existing durability minimum. Missing fields default false/null/zero, preserving legacy accepted quality constraints. New `equipment-need-v2:` and `joy-need-v2` signatures cannot reuse old observation durations. `ProblemThing` uses Scribe reference resolution. New equipment completion checks def, any required quality, durability, usability, coverage/primary role and the original piece, then uses the existing same-item one-day hold. The disappearance of a thought cannot close an accepted promise.

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

Signatures begin `housing-v2:` and retain a still-current causal anchor even if the other partner develops an additional concern. A change of solution restarts evidence; if the anchor disappears, another concern starts fresh. The offer's localized reason is rendered from saved subject references, exact causes, and observed effects, not saved translated prose. Player-facing reasons describe the actual issue without displaying the observation machinery or source thought penalties. Unanswered requests recheck that original cause and target. Obsolete closure has zero consequences; actually fulfilled eligible recovery still uses the existing continuous proof. Pre-evidence open housing requests close obsolete, while accepted old terms never consult new thought-generation rules.

### Shared bedroom candidates

`SharedBedroomRules` selects a reciprocal, unambiguous living romantic partner from direct relations. `WillingToShareBed` checks the game's shared-bed and spouse/non-spouse willingness events for both pawns; relation type alone is insufficient. Core pawns without an ideology remain supported. Multiple partners, an ineligible partner, an Ascetic on either side, unavailable same-home-map observation, either occupied/cooling slot, or no player-acquirable loaded bed type usable by both blocks a candidate. These reasons come from the same evaluator used by diagnostics, and suppress conflicting new individual bedroom candidates.

The lower pawn ID owns the pair's observation and queue entry. The unordered `PairKey` prevents duplicate ordering. One partner's confirmed persistent issue is sufficient; the target resolves it while respecting both participants' actual Greedy and sharing constraints. The saved reason identifies the affected partner and effect, not assumed dissatisfaction of both. Generation retains hourly samples, two-day persistence, and colony-wide half-day spacing. Check now on the second participant also observes the canonical owner without bypassing the gates.

`SharedBedroomCommitment` is the authoritative record. It owns the stable ID, participants, one open request or active promise, and two frozen personal consequence snapshots. Pawn states expose runtime aliases to that record for both personal panels and indexes. Creation rechecks both slots; acceptance rechecks both participants, sharing rules, observability, cooldowns, and actual unsatisfied conditions. `AcceptShared` computes both previews before snapshotting the common terms and starts one new hold/deadline. Significance and deadlines use normal bedroom rules, not a two-person multiplier.

Shared observation requires the same spawned, non-medical colonist bed with at least two sleeping slots, exactly the two owners, and `CanUseBedNow` for both. Private terms require standard Bedroom role and no other room owners; new explicit barracks terms require Barracks role and permit other room owners. Both require enclosure and no medical beds. Both eligible pawns must be observable on the same map; physical simultaneous sleep is unnecessary. Common progress uses the existing hold arithmetic and interruption hooks. A lookup through either participant invalidates the same record; recreation is unaffected.

Maintenance runs the shared record once in the periodic active/open pass. Invalid relationship/sharing rules, death, destruction, or non-player faction closes it without penalty for both. Missing relationship trackers mean unknown, not breakup; unobservable conditions reset hold without cancelling the obligation. Acceptance/decline/withdrawal from either panel operates on the common record. `CompleteShared` marks it resolved and removes it before personal effects, releases both attached slots, and records each partner's actual effects and stored partner label. `ResolveShared` sends one result notification only after a successful transition. Unaccepted recovery independently checks each participant's existing trust, proof, cooldown, and repeat gates against the still-open aliases before consuming them.

### Development diagnostics

`Evaluate`, `Project`, and `PrepareTerms` serve both generation and diagnostics. `PeekState` avoids enrollment just to display a pawn; consequence previews operate on a copy. Viewing diagnostics therefore does not reset clocks, modify terms, or create requests.

Enable standard Development mode and expand diagnostics in the Colonists detail panel, above history. It shows exclusions, slot/cooldown state, current/target conditions, saved/projected evidence, selected-pawn last check, colony last/next scan, and global gating. Initial selection can include an ineligible selected map pawn in Development mode; a valid remembered selection takes precedence on reopening.

`Check now` uses ordinary observation and offer methods for the selected pawn. It respects eligibility, persistence, cooldown, occupancy, and frequency rules, but checks that pawn rather than the whole priority queue. It does not advance the scheduled scan. Same-tick repeats add no evidence; this action does not force a request or log each scan.

`DevRequests` and `DevRequestMenu` provide the separate test creator. Bedroom variants use the condition registry; equipment variants are drawn from `EquipmentNeeds.Causes`, also used by ordinary evaluation. Current-needs creation calls `Evaluate` with a guarded Dev cooldown bypass and optional equipment subtype filter. It skips persistence and offer spacing while retaining real cause, usability and availability checks. Manual creation validates saved parameters and pawn/partner usability without requiring a need or loose stock. It supports private/improved/constructed-bed bedrooms, barracks, shared bedroom/barracks, recreation, damaged-clothing replacement, nudity coverage, Brawler melee weapons, skill-based ranged/melee acquisition and same-model weapon upgrades. Manual acquisition enforces the selected type, actual usability and the Brawler ranged restriction while bypassing skill thresholds.

The saved `PromiseRecord.devTest` and `devManual` flags default false. Manual open requests bypass need-based obsolescence, already-satisfied closure and mutable pending DLC checks; structural acceptance, pair validity, response expiry, reminders, real conditions, effect snapshots and accepted resolution remain. Manual creation disables unaccepted trust recovery. Both test modes can be accepted despite an existing cooldown. Switching Development mode off does not alter an existing record. Creation/cleanup entry points and UI callbacks guard `Prefs.DevMode`; ordinary evaluator calls retain their previous defaults. Test creation does not change `nextRequestObservation`, `nextRequestOffer` or another colonist's observation. Shared creation reuses `CreateShared` and canonical aliases. Confirmed cleanup verifies the expected record identity, removes the canonical shared record and both aliases when needed, and touches no history, trust, repeat ledger or cooldown.

`DevRequestChecks` exercises registered variants, disabled actions, stale confirmations, occupied/cleared slots, shared aliases, real acceptance, bilingual labels and Scribe defaults/round-trips. Full map-based creation and menus remain unverified outside the game. In a copied save, test every menu subtype in both modes, insufficient need/availability, occupied partner slots and invalid equipment; accept manual terms without a deficit, save/reload, turn Dev mode off and complete the normal hold/activity. Decline, expire and withdraw separate records; inspect applied history. Check that shared actions resolve once for both partners, cleanup awards nothing, and natural schedule/other observations do not change. Preview all nested menus at different UI scales.

## Promise and request lifecycle

Acceptance revalidates the request, snapshots effects, removes the open slot, and installs the terms as an active promise. It resets progress and credit clocks to acceptance, including any unaccepted proof. `FrozenConsequences` prevents resnapshotting. The deadline is acceptance plus the stored fulfillment length.

Active checks observe conditions, then resolve fulfillment before expiry. Withdrawal runs a normal check first, so completion or deadline resolution takes precedence. Resolution clears the active slot/index before external effects and the result notification. Unaccepted closure consumes the request before recording its outcome. These terminal transitions prevent repeat effects and notifications.

Unaccepted closure bypasses accepted-promise rewards and the success ledger. Explicit decline uses `ApplyDecline`: −1 mood for one day and clamped −1 trust, recorded as actual personal effects. It replaces an existing decline memory rather than stacking it. Expiry and obsolescence have no penalty; satisfaction may use the existing limited trust recovery. These transitions use the unaccepted cooldown. Individual closure consumes the request before applying effects; shared closure resolves its canonical record before applying each personal penalty. Death, destruction, a missing pawn, or `Faction != Faction.OfPlayer` closes an obligation without penalty; temporary absence does not establish faction departure.

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

Scribe stores pawn/item references and defs, requests, observations, active progress, trust, history, cooldowns, snapshots, and separate success/recovery ledgers. Schedule keys include `nextRequestObservation`, `nextRequestOffer`, and `lastRequestObservation`. Deficits save signatures, durations, severe time, sample ticks, `ObservationPaused`, and `StartTick`; missing old starts use last sample minus saved duration without reconstructing evidence beyond the cap.

Missing collections initialize empty, trust defaults to 50 and is clamped, and absent last-check markers mean not recorded. Legacy targets, deadlines, progress, credit clocks, trust, history, and success stamps remain, including one-hour recreation records. Missing `FrozenConsequences=false` selects legacy effects and three-day cooldown. New frozen promises retain their effects. Runtime indexes and `ObservedRoom` are rebuilt, not serialized. Same-tick loading adds no time; observation gaps restart evidence and hold gaps earn no retrospective credit.

Housing migration adds `housingWitnesses` to pawn states and `housingReasons` to offers. New term flags default false, preserving old accepted room requirements and progress. Witnesses default empty. Old `room:`/`pair:` signatures cannot satisfy the new `housing-v2:` persistence gate. No accepted promise is cancelled because a thought disappears; only unanswered requests run the new relevance policy.

Shared records are deep-saved once under `sharedBedrooms`, with `nextSharedId` maintaining stable IDs. Participant states save `sharedId` and omit duplicate request/active aliases. Central records save participant references, terms, progress, notice flags, and both personal snapshots. After cross-reference resolution, finalization reconnects both states to the same object and rebuilds the common room cache. Conflicting saved individual obligations are never overwritten. Missing shared fields initialize empty; existing individual records retain their conditions and effects. Personal history adds a stored partner label so later relationship changes cannot rewrite it.

Letters save `requestPawn`; requests save `announced`, `reminderSent`, and `recoveryEnabled`. Missing old announcement flags default true to avoid re-announcement; recovery defaults false. Settings save `remindersEnabled`, default true. Notice flags are consumed before sending. The reminder window starts 30,000 ticks before expiry; disabling leaves the pending flag intact, so re-enabling before expiry can send the one remaining reminder.

`OpenRequests` is the read-only source for list and badge. Expired, terminal, accepted, and observable obsolete states are excluded immediately; absence alone does not prove obsolescence. Fulfilled requests remain visible only for eligible proof. `ChoiceLetter_Request` opens the existing window through `SetCurrentTab`, then `SelectPawn` switches to Colonists and resets detail scrolling. `MainButtonWorker_Promises` inherits toggle behavior and draws a badge without mutating Def labels.

The UI uses checked `TabDrawer.DrawTabs`/`TabRecord` APIs. Requests sorts a copy of current entries by response expiry, then pawn ID; it never displays a selected pawn's personal data. Colonists has separate list/detail scroll positions, highlighted selection, and read-only statuses from current visibility and cooldown. Ordinary reopening retains session-local navigation; selection validation handles removed pawns without changing the internal tab. The session-local `sortByName` flag defaults false. `GroupColonists` projects active and inactive groups from `PeekState().Active`, preserving alphabetical order with a localized case-insensitive comparer and pawn-ID tie break. Pending requests never enter the active group; shared participants use their authoritative runtime aliases. Empty groups are omitted. Each draw refreshes membership without calling selection/navigation or assigning scroll positions. This state is not added to game saves. `RequestedTabSize` is bounded by logical `UI.screenWidth/Height` with margin for game controls. Text and button heights use wrapped `Text.CalcHeight`, including long names. More details and Development-only diagnostics start collapsed; conditions, times, progress, and numeric effects remain visible.

History stores `ResolvedTick`, `AppliedMood`, and actual post-clamp `AppliedTrust`; rendering never recalculates effects. Availability flags distinguish zeros from absent fields. Older entries without the metadata marker use only explicit XML values; omitted defaults are not guessed. Penalty-free outcomes have a no-change description; recovery displays its recorded change. History trimming does not trim repeat ledgers.

Custom UI text belongs in both keyed/DefInjected localizations. Format numbers through `PromiseText`/`PromiseHistory` before plain translation placeholders. Fixed-arity `Translate` uses `NamedArgument` and `GrammarResolverSimple`: a colon invokes a language function, so `{2:0.00}` is not a safe .NET format. English decimal points, Ukrainian commas/day forms, signed effects, and unavailable-data messages are covered by translator fixtures.

## Automatic verification

### Optional DLC rules

`DlcCompatibility` applies new-request compatibility and pending relevance; `BedroomRequirement` holds value snapshots. No DLC dependency or DLC-specific XML is added. Optional thought lookup is guarded by the active expansion and uses silent lookup; furniture is stored as DefName strings rather than optional `Scribe_Defs` references. A missing optional furniture definition cannot throw during loading or falsely satisfy a room. Without Royalty, title conditions are not used. This fallback is not a guarantee that removing an expansion from a save is safe.

Royalty's `TitleNoPersonalBedroom` and `TitleBedroomRequirementsNotMet` are negative situational thoughts (Royalty XML base effect -6). Their actual workers enforce title eligibility, home-map context, grace periods and trait nullifiers. They use the existing two-day observation and queue. The highest applicable title's active bedroom requirements are copied for each participant; `Disabled(room, pawn)` respects Ideology exemptions such as slab-bed preference. The supported shipped types are area, exact impressiveness, terrain tags, a required thing, a required count, alternatives, and forbidden building tags. Completion delegates to reconstructed game `Met` methods, not a substitute Impressiveness-only rule. Custom requirement subclasses are rejected with a diagnostic reason. Core generation and reward tiers are retained.

Snapshots are stored under the new optional `bedroomRequirements` key; old accepted records leave it null. Shared snapshots combine both participants' requirements without adding another authoritative record. They are fixed values, so titles or beliefs changing later cannot rewrite accepted conditions. Before pending satisfaction/recovery, visibility, lifecycle checks and acceptance check the current requirement signature; an incompatible unanswered request closes obsolete without effects. Existing room/terrain events and periodic checks evaluate the additional conditions through the same observer.

New equipment evaluation uses actual apparel compatibility and active title/role requirements to avoid stripping their only satisfying garment. Desired ideological apparel uses its target gender; active royal requirements take priority as in the inspected game rule. Ideological nudity preference is checked for the affected gender. Exact negative human-leather precept thoughts select a saved `avoidHumanLeather` condition, checked against both equipped/stock items and recipe materials. Missing flags default false for old accepted gear. The game uses `requiredMinimumApparelQuality` in apparel scoring; the inspected missing-apparel thought checks required garment coverage, not that quality. No new quality floor is inferred from the scoring preference. This integration does not implement arbitrary ideological clothing/material complaints or multi-garment solutions.

Core housing thoughts are still evaluated through their game workers and nullifiers; no privacy request is inferred from ideology or expectations alone. `ThoughtWorker.FailState` calls `CanGetThought`, including precept/gene/trait restrictions. Shared sleep continues to use `WillingToShareBed` for both pawns, including spouse and non-spouse history-event rules. Biotech development stage/body coverage, nonviolence, manipulation, role use and biocoding remain checked through the existing APIs. Anomaly pawns without mood or joy do not receive the corresponding requests. Odyssey swimming can count only through the ordinary recognized recreation activity hook; travel, flight, research or other expansion activities are not added to recreation accounting.

### Commands and fixture boundaries

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

Run the GameChecks-based scripts sequentially: Features, Generation and History share the output directory, and Features temporarily patches its disposable game DLL copy. Concurrent builds/runs can overwrite that copy. The script passes the selected installation to Core/DLC XML fixtures and restores the environment afterwards.

`Verify-Features.ps1` also uses Mono.Cecil (pass `-CecilPath` if it is not available in the local NuGet cache). In the disposable test copy of `Assembly-CSharp.dll`, it skips `ModsConfig`'s initializer and substitutes null for the royal tracker's command-icon load, preserving its collections and all tested rule methods. This avoids Unity startup and any access to the player's mod configuration. It restores that copy afterwards; the installed game and packaged mod DLL are unchanged. DLC fixtures set the actual cached active switches, covering Core, each individual switch and all combinations. These are branch tests with supplied data, not full DLC Def loading. Fixtures cover absent trackers/joy, highest-title selection, room thresholds, captured values, unknown requirements, clothing-role preservation, gender-specific nudity, material matching, localization and Scribe defaults. Local Royalty XML is checked for supported requirement types.

| Configuration | Automated coverage | Documented in-game coverage |
| --- | --- | --- |
| Core without DLC | All-disabled branches, Core drug/housing XML, existing lifecycle/UI/save-field suites | Configuration not recorded |
| Royalty only | Captured title/room/clothing fixtures; shipped title requirement classes and API call paths | Configuration not recorded |
| Ideology only | Clothing-role, gender preference and absence fallbacks; local precepts and shared-sleep/drug rule inspection | Configuration not recorded |
| Biotech only | Missing-need branches; actual chemical-dependency template; bed/dependency API paths | Configuration not recorded |
| Anomaly only | Actual ghoul whitelist/need restrictions projected into game suitability checks | Configuration not recorded |
| Odyssey only | Actual swimming job and recreation callback; psilocap rejection from actual XML; gravship home API path | Configuration not recorded |
| Combined DLC | All cached switch combinations; Royalty + Ideology furniture/clothing interaction; Anomaly suitability with other switches | Complete loaded configurations not tested |

The game assembly and Defs for all five expansions are locally available. Switch fixtures supply selected data and do not emulate the expansion loader. Metadata requires only Harmony; runtime XML contains no unconditional expansion-specific references. Common game APIs handle enabled content without exposing DLC assets when absent.

| Existing function | Expansion integration and boundary |
| --- | --- |
| Eligibility, generation, observation and queue | Actual humanlike adult/free-colonist/mood eligibility and existing home-map rules; missing needs do not invent a deficit. No expansion-specific queue, timing or slot limit. |
| Individual/shared rooms | Royalty title snapshots retain area/floor/furniture requirements; Ideology nullifiers and `WillingToShareBed` remain authoritative. `CanUseBedEver/Now` cover Biotech deathrest and Odyssey vacuum restrictions. Shared records remain canonical and do not convert to individual promises. |
| Recreation | Actual joy need and `JoyTickCheckEnd` activity callback; Odyssey swimming uses that callback. Deathrest, travel and unrelated jobs are not recreation. No joy need means no recreation request. |
| Clothing and weapons | Body/developmental-stage/locked-apparel checks plus `CanEquip` for biocoding, Royalty bonds and Ideology role limits; stored title/role/material constraints remain fixed. Skills and Brawler keep their existing priority. Custom item/manufacturing implementations remain conservative. |
| Substances | Beliefs, physical suitability, real policy entries and Anomaly drug whitelist precede stock/production. Biotech's genetic dependency exception does not silently bypass beliefs. Only `Hediff_Addiction` creates ordinary addiction supply; gene dependency is a documented unsupported cause. Odyssey psilocap's special outcome remains excluded from experimentation. |
| Movement and special states | `Map.IsPlayerHome` includes the game's gravship rules. Absence, sleep, downing and suspension do not add unverified time; deadlines keep running. Actual ingestion away from maps retains its existing one-dose credit path. |
| Acceptance, outcomes, recovery and saves | Expansion-neutral terminal guards, personal consequence snapshots, repeat ledgers and independent substance gate. Decline remains penalized; unanswered expiry remains penalty-free. Saved enum values, keys, type identities, targets and effects are unchanged. |
| Notices, history, UI and Dev tools | One authoritative shared record, saved notice flags, actual applied history figures and readonly diagnostics. Manual Dev bypass does not bypass structural/usage restrictions. Substance rows now name the substance/chemical rather than a missing equipment target. |

`Verify-Content.ps1` checks the relied-on game call paths for bed sharing/usability, equipment roles, drug suitability/dependency, gene/ideology/hediff need rules, gravship home recognition and swimming's joy callback. These checks establish available API paths, not live outcomes.

Housing fixtures read effect stages from the referenced installation's Core XML, exercise the compiled evidence predicates, minimum-category selector, candidate builder with supplied verified issues, anchoring, acceptance, bilingual attribution, and Scribe defaults/references. The .NET host cannot initialize the game's Unity-dependent mod configuration to run complete thought workers and nullifiers. Those runtime paths are inspected against the game assembly and remain in-game checks. If the installation differs from the project default, set `COLONY_PROMISES_GAME_ROOT` for the housing fixture's XML input.

The player has confirmed that the current mod works in RimWorld. Specific DLC configurations and individual test scenarios have not been recorded, so that confirmation is not a claim of exhaustive DLC compatibility. Automated regressions separately cover readonly acceptance, substance summaries, fresh/ambiguous familiarity snapshots, existing timing and frozen outcomes. Full configuration coverage, realistic map transitions and complete save reconstruction require targeted gameplay tests. Safe removal remains unverified.

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
| Recreation | Keep joy below 15% while awake on the home map for two observed days; known sleep pauses. Normally six hours over four accepted days. Between 15% and 35% yields four hours over three days. Check paused and first-waking intervals add nothing; reaching 35% or an unknown gap resets evidence. |
| Weapon | Brawler with an equipped ranged primary and actual negative `BrawlerUnhappy`: hold the complaint for two observed days; expect a usable melee target. A Poor revolver without that complaint generates no request. Removing or changing the ranged primary before acceptance makes the offer obsolete. |
| Apparel | An actual `ApparelDamaged` effect and unlocked shirt at 40%: observe two days, expect the same shirt def at 80%, no quality floor. Legendary apparel at 40% fails; a plain 100% replacement passes. Test multiple worn pieces: only the named piece is promised. Repairing it to 50% invalidates an unanswered offer. |
| Nudity | Without Ideology, obtain actual negative `Naked`; after two observed days expect one garment covering all missing basics. A hat alone fails. Nudist/Furskin nullification, missing legs and incompatible or locked clothing must affect selection. An unsupported Ideology dress complaint produces no arbitrary target. |
| Manufacture | Remove ready replacements but provide an unlocked normal recipe, usable table, able crafter and sufficient reachable materials. The need continues accumulating and a craftable target can appear. Remove materials/research/bench: need remains, target is blocked. Restore a feasible route: no extra two-day stock requirement. |

Also test children, guests, prisoners, slaves, quest lodgers, trait/work restrictions, missing body parts, biocoded/forbidden/unreachable stock, missing quality, and loaded mod items. No unusable/missing-DLC target should appear. Short deficits create no request. Missing weapons support only the explicitly qualified skill-based desire; missing apparel alone is not a universal complaint.

For DLC tests, use a separate copied save for Core, each expansion alone, Royalty + Ideology, Royalty + Biotech, and all available expansions:

- Royalty: a non-Ascetic titled colonist with an actual negative bedroom thought and no cooldown. Keep the problem for two observed days. Raise Impressiveness while withholding required flooring, area or furniture: no hold credit. Meet every displayed condition for a continuous day; verify one result. Promote after acceptance and reload: saved requirements and effects stay fixed. Promote an unanswered request: it closes obsolete, without recovery or penalty.
- Royalty + Ideology: verify a slab-bed precept waives the game's corresponding title furniture requirement. Check a pair with different title requirements and shared-sleep restrictions; one record must satisfy both sets. A satisfied barracks resident and a nullified housing complaint create no privacy request.
- Ideology: keep an actually negative worn-clothing complaint for two observed days. Offer only a replacement conflicting with required role/desired clothes or an applicable nudity preference: no target. Test both genders and a compatible same-def replacement. With human leather forbidden, human-leather stock or ingredients alone cannot establish a solution or fulfill a new promise; other materials can.
- Biotech: check gene-nullified nudity, missing body parts, absent joy, deathrest and usable beds, plus royal furniture alternatives. Unusable targets and absent needs produce no corresponding requests. Do not treat deathrest as recreation.
- Anomaly and Odyssey: check humanlike eligibility and actual needs, movement between maps/gravship, and a standard swimming activity. Only recognized recreation activity may earn time. Compare flight, travel and unrelated expansion jobs; they earn none. Save/reload an individual and shared active promise with the new fields and repeat without DLC in a separate fresh save.

Shared cause rendering is a UI-only projection. `SharedPromiseText.PartnerFor` resolves the other participant from the selected pawn. `FormatRequestReason` groups matching causes into one natural sentence and names the affected subjects; different causes receive separate short sentences. Additional current complaints come from read-only `HousingRules.Evaluate`, only when both participants are observable on the same map and the fixed requirement can cover the concern. Displaying them does not assert a second two-day observation or modify the causal anchor. Missing original reason data uses neutral text. There is no selection-dependent text cache.

`PromiseCardText` formats short titles, reasons, requirements, saved rewards, and known progress blockers. Main requirements retain room level, quality when required, item condition, actual wear/equip action, and hold duration. Source thought mood offsets remain in Development diagnostics. Request cards put reason, action, deadlines, and rewards before the actions; shared rewards are individual compact lines. Active cards show progress and known blockers from existing condition observations. More details contains full eligibility/continuity rules, penalties and cooldowns; confirmation still includes the full requirement, deadline, rewards and penalties. Formatting does not change requests, consequence snapshots, generation or save fields. Legacy promises without frozen effects retain their original-rules label rather than invented fixed rewards.

### Lifecycle, recovery, and saves

| Scenario | Action and expected result |
| --- | --- |
| Colonist sorting | Default active-first: accept a request, fulfill/withdraw/close it and verify immediate regrouping on the next draw. Both accepted shared participants lead; pending requests remain inactive. Switch By name, close/reopen, and verify mode, selected pawn and independent scrolling persist. Test empty groups, long names, Ukrainian labels and small logical screens. |
| Shared reasons/navigation | Switch between Iceman and Miyazaki: headings/buttons point to the other participant. One recorded complainant keeps their actual name on both pages. Test two matching recorded causes, different causes, an additional current partner complaint, repaired current conditions and legacy missing reason data. No invented attribution or stale selection text. |
| Diagnostics/cadence | Pause/reopen: saved state unchanged. Check now before persistence, during cooldown, and behind global gate: no bypass or same-tick credit. Dev off hides panel. Several deficits get at most one offer per half day; older capped evidence keeps priority. Record sample/reset/offer ticks. |
| Review/relevance | Inspect both languages, exact effects/durations and confirmations. Cancel unchanged. Satisfy before accepting: reject reward-bearing acceptance. Remove stock while a verified craft route remains: request stays relevant; remove every supported route: obsolete closure without penalty. No arbitrary creation controls. |
| Housing hold | Private individual terms require a sole-owned bed; shared terms require exactly the pair as bed owners. Both need the same qualifying bed/room for one day. Explicit new barracks terms allow other room owners; private and old individual terms do not. Medical beds, wrong room roles, and outdoors give no progress. A ground-sleep target cannot use a free spot. Unclaim/reclaim either partner, failed conditions, or shape changes reset the common hold; valid ordinary content improvements preserve. |
| Shared UI/results | One list row/count/letter/reminder; both panels reference the same request/progress/deadline. Accept/decline/withdraw from either panel, confirm both names. Compare trust 0/80 and different repeat ledgers: two exact personal snapshots and once-only histories/effects. Change trust after acceptance: frozen effects remain. Test both languages and partner navigation. |
| Shared relationship rules | Multiple partners: no random selection or individual bedroom fallback; Dev reason explains. Ascetic pair: blocked. Test optional shared-sleep prohibitions and changing them after acceptance, breakup, death, destruction, and faction exit: both close without penalty. Caravan, drafting, illness: deadline continues, no cancellation solely for absence. |
| Shared recovery/save | With both at trust 0, satisfy an unanswered shared request for one verified day before reply expiry: each eligible pawn gains +2 trust once, no mood. Repeat with only one eligible recovery ledger. Save during observation, open request, partial accepted hold, recovery, and after reminder/result; reload preserves one canonical record, both personal snapshots, flags, and qualifying hold. Load old individual records alongside it unchanged. |
| Equipment hold | Storage/inventory gives none; qualifying physical item needs a full equipped day. Brief removal, identical-copy swaps, quality changes, or failed durability reset only that hold. |
| Recreation | Recognized games, skygazing, social relaxation, walking accumulate. Timetable-only, travel, sleep, downing, drafting, hospitals, mental states, idle do not. Assess practical 4-hour/3-day and 6–8-hour/4-day targets with normal tolerance/saturation. |
| Null-item regression | A has partial recreation; bedless B claims a first bed paused: A's progress/clock unchanged. Unclaim B's tracked bed/remove C's tracked gear: only their holds reset. Unrelated items change none. Repeat with pending recovery proof. |
| Outcomes | Complete, break at high trust, withdraw before expiry, and finish exactly at deadline: one saved effect/result; fulfillment wins. Withdrawal at/after expiry cannot replace resolution. Death/faction exit closes penalty-free without repeats after reload. |
| Cooldowns/repeats | Decline: −1 mood for one day and clamped −1 trust, once per participant. Expiry: no effects. Both have a three-day cooldown plus two fresh observed days. Accepted outcomes: five days. Repeated successes within fifteen days reduce rewards, not penalties; refusal/reload/history trimming retain ledger. Positive and same-outcome negative memories replace. |
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
| Layout/performance | Check all card types in English and Ukrainian: main requirements include quality/condition/hold hours; source thought penalties appear only in diagnostics. Shared titles follow the selected partner; common reasons merge without inventing dissatisfaction. Confirm rewards, penalties and full conditions before accepting; expand More details and compare. Test fractional hours/rewards and long names. Requests contains no personal data, sorts nearest expiry first, and has an empty state. Rows/letters open the right Colonists details. Reopen retains tab/selection; removing the pawn or closing its request leaves no stale actions. Test independent scrolling, long names, both languages, small screens/UI scaling, full history, collapsed rules/Dev diagnostics, and no periodic result spam. Isolated layout bounds do not establish visual correctness. |

For missing-history migration, use a copied older save with individual `appliedMood`, `appliedTrust`, or `resolvedTick` absent, then save/reload again. This field exercise does not establish full reference loading or safe removal.

## Technical limits

- Deficits are sampled hourly, not verified every intervening tick. Unobservable/cooldown time is excluded; busy queues have no fixed maximum wait.
- Caravans generate no requests or simulated progress. Deadlines continue through absence, illness, combat, and suspension.
  Actual substance ingestion is event-based and can count during travel; there is no simulated dose credit.
- Custom usability/recreation may fall outside standard APIs. Callback intervals are not independently reconstructed.
- Shared bedrooms require an unambiguous reciprocal pair and exclude Ascetics conservatively. Loaded usable bed types are checked, but research/material availability is not forecast. Simultaneous multi-partner housing is unsupported. Automated fixtures do not establish full-map shared generation, live ideology restrictions, room reconstruction or visual pair navigation for every configuration.
- Changes bypassing ownership, equipment, quality, or room notifications can evade immediate invalidation. Periodic durability checks can miss unusual damage/repair cycles.
- Shape and quality changes deliberately reset holds even when favorable. Engine reconstruction, full migration, live patches, and safe removal need game-level validation.

Housing witnesses remain available while a request is open. Acceptance, closure and cooldown observation clear them, so later requests require fresh evidence rather than reusing a previous problem.

Equipment/recreation checks exercise the compiled cause predicates, pauses, versioned observation migration, fixed parameter filters, Core XML effects, bilingual text and isolated Scribe fields. Actual thought-worker execution, crafting usability/material selection, body coverage, item-reference reconstruction and continuous damage/repair in Unity remain manual checks.

### Skill-based weapon acquisition

`AcquisitionType` requires `WeaponAcquisitionSkill` (6) and the existing four-level difference after the ordinary preferred-type restrictions. It requires no primary weapon or the opposite primary type. Brawler complaints take priority; an appropriate primary suppresses acquisition, leaving quality upgrades separate. Candidates use the selected type only, stable ordinal DefName order, and existing `UsableDefinition`/`CanObtain` checks. There is no wrong-type fallback or comparison of combat effectiveness. The `weapon-acquire-v1:` signature identifies the desired type, not a physical item, so copy changes cannot add observation time twice. New terms are quality-optional and require the primary role. Causes use existing string fields; accepted conditions never reevaluate skills.

In a copied test save, use Shooting 15 / Melee 2, first unarmed, then with a sword, then with a usable gun. Only the first two support ranged acquisition. Repeat symmetrically for Melee 15 / Shooting 2. Check level 6 and a difference of exactly 4, below-threshold and close skills, a Brawler with high Shooting, and inability to fight. Opposite-type stock without a verified production route must produce no target. A suitable low-quality primary suppresses acquisition while retaining the existing same-model quality desire.

Use **Create test request…** with **Obtain a ranged weapon** or **Obtain a melee weapon** in both current-needs and manual modes. Natural/current-needs evaluation shares the cause and availability rules; natural generation still needs two observed days. Accept, reverse skill levels, equip the fixed primary for a day, save/reload, and compare stored terms and progress. Decline individual and shared requests at trust zero and above zero: one actual penalty, no repeat after reload. Let separate requests expire: zero effects.

Isolated checks cover thresholds, type exclusion, observation arithmetic, acceptance snapshots, existing-field Scribe round-trips, Dev variants and decline transitions. They do not establish live map generation, recipe feasibility, UI layout or full save loading.

## Traits and substance requests

Core TraitDefs and the available Royalty, Ideology, Biotech, Anomaly and Odyssey data are reviewed against the existing thought/need adapters. Trait lookups through `TraitSet.GetTrait`/`HasTrait` exclude suppressed traits. No blanket trait bonus or second mood interpretation is added.

| Trait or group | Relevant behavior |
| --- | --- |
| Greedy | Actual negative Greedy thought can establish a housing cause; its neutral stage constrains the target. |
| Ascetic | Housing evaluation avoids a conflicting quality target; shared generation remains conservatively blocked. |
| Jealous | Its actual housing complaint is diagnosed but is not converted into an unsupported comparison with another colonist's room. |
| Nudist | Game thought eligibility/nullifiers prevent unwanted-nudity requests when nudity is acceptable. A separate wish to remove clothing is not implemented. |
| Brawler | Actual ranged-weapon complaint precedes other weapon causes; melee preference overrides skills. |
| Chemical interest/fascination | Active `DrugDesire` degrees 1/2 establish the mod-owned experimentation desire. These are not inferred from addiction. |
| Teetotaler | The negative `DrugDesire` degree cannot start experimentation; `PawnUtility.CanTakeDrug` governs actual permitted items, with additional explicit belief/policy checks. |
| Biotech Recluse; Anomaly BodyMastery | Their game thought/need eligibility is retained. Missing needs create no invented deficit; neither trait gains a separate request. |
| Mood, nerves, work speed, memory, combat modifiers, social/sexual preferences and other Core/DLC traits | No independent cause or reward modifier. They can influence the game's actual needs/thoughts or learned skills, which existing evaluators use. NightOwl and Undergrounder discomfort, food/body-mod desires, and Anomaly-specific wishes are not substituted for housing, recreation or equipment causes. |

Royalty adds no independent pawn TraitDefs here; Ideology patches existing traits and supplies precept rules. Biotech Delicate and Anomaly PerfectMemory, Occultist, Joyous, Disturbing and VoidFascination receive no fabricated request effects. Odyssey weapon traits are item mechanics, not colonist TraitDefs. This review does not claim live testing of every trait or DLC combination.

### Substance API and eligibility

`SubstanceNeeds` evaluates the new appended `PromiseKind.Substance` (3); existing enum values and Scribe keys remain unchanged. It uses `ThingDef.IsDrug`/`IsIngestible`, `CompProperties_Drug.chemical` and `needLevelOffset`, `Hediff_Addiction.Chemical`, `Pawn_DrugPolicyTracker.HasEverTaken`, and standard `IngestionOutcomeDoer_GiveHediff` outcomes. A chemical maps through `ChemicalDef.addictionHediff`, rather than translated labels or a hardcoded drug list.

Experiments require active chemical interest/fascination and fewer than three actual `Hediff_Addiction` instances. `Standard()` recognizes an acquirable drug with a `ThingWithComps`-compatible item class, a `CompDrug`-compatible component and ingestion outcomes. It does not require every auxiliary component to come from the game assembly. `SupportReason()` separately requires Social/Hard category and exactly one guaranteed positive `Hediff_High` or `Hediff_Alcohol` outcome with disappearance or negative severity decay. Auxiliary high components do not need to match a small whitelist; permanent-wound healing remains excluded. Other GiveHediff outcomes may only supply the mapped chemical tolerance.

Standard `OffsetNeed` and `OffsetPsyfocus` ingestion actions are allowed alongside the high. Core GoJuice includes both: the former all-GiveHediff filter rejected it before policy, familiarity or supply checks. These actions adjust existing need/psyfocus values; the experimental completion target remains `GoJuiceHigh`. Unknown outcome implementations, ambiguous/missing temporary highs, medical targets and additional unsupported hediff outcomes remain excluded. Odyssey psilocap's custom outcome is not supported. Genetic chemical dependency without `Hediff_Addiction` is not a supply cause.

Technical support, pawn suitability/policy, familiarity and availability are separate checks. `TargetReason()` reuses them for natural experimentation and readonly per-product diagnostics. Diagnostics name an unsupported outcome class and distinguish pawn restrictions, policy refusal, a known chemical and unavailable supply. Manual Dev selection lists technically supported products regardless of familiarity or deficit; blocked pawn/policy/addiction requirements appear as disabled options with a reason. It does not bypass those requirements. Auxiliary mod components do not alone make a drug technically unsupported, but the ordinary manufacturing solver remains conservative about custom production implementations.

Both subtypes check consciousness, race ingestion capability, `FoodUtility.DrugIsSuitable` (including mutant rules), `PawnUtility.CanTakeDrug`, and existing policy entries. Experiments require joy or scheduled permission; addiction supply requires addiction permission. Active Ideology separately checks willingness for drug, recreational-drug and hard-drug history events, so the game's dependency exception cannot silently authorize a conflicting request. Mutants with disabled policies are excluded. The inspected `CurrentPolicy` getter assigns a default when uninitialized; evaluation instead reads the verified private `curPolicy` field and existing `DrugPolicyDatabase.AllPolicies` default without assigning or creating a policy. This keeps diagnostics read-only. No policies or jobs are changed.

For an addiction, loose map stock and the pawn's inventory are checked across the chemical's supported dose products. An accessible permitted dose removes the need. Existing blocked/unsuitable stacks prevent asking for extra manufacturing; the evaluator reports the access problem. Otherwise stable ordinal DefName selection reuses `EquipmentNeeds.CanObtain` with an ingestible stock predicate and its existing ordinary-recipe/ingredient solver. This optional predicate leaves equipment checks unchanged. Speculative trade, planting/harvest forecasts, carried items on other pawns, special recipe workers and unusual drug implementations are not proof of availability.

Both subtypes share the normal two-day observation, global offer interval, occupancy rules, snapshots, history and repeat ledger. Addiction shortage precedes experimentation. Experiment signatures identify the continuing unfamiliar-substance desire, not a physical stack; addiction signatures identify the chemical. Known sleep pauses observation without adding time. Unknown gaps restart it. A separately saved fifteen-day substance gate blocks only this direction and starts after every normal closure, including decline, expiry, obsolescence and penalty-free closure. Fresh evidence must follow it. Dev cleanup retains its existing no-consequence behavior.

### Ingestion, persistence and outcomes

The Harmony Prefix/Postfix on `Thing.Ingested(Pawn, float)` captures the pawn's already-active record, original Def, stack count and pre-ingestion effect severity. The original's outcome calls precede its return; a reduced stack or destruction confirms consumption, since its nutrition return is not a dose count. The Postfix journals real use even without an obligation, and credits only the same captured active record. Experiments additionally require the saved effect's severity to appear/increase. Addiction promises accept supported products matching their saved chemical and a positive chemical-need offset. These checks do not reinterpret an accepted promise through later traits, skill changes or policy changes.

`substanceConsumedTick` defaults to −1 and guards duplicate credit; accepted records alone can credit, within their fixed issue/deadline bounds. Progress represents one dose, not ticks of effect duration. Completion uses the existing terminal transition and personal consequences exactly once. Requests fulfilled before acceptance close without an accepted reward; substance requests do not enable unaccepted trust recovery.

New saved fields are `substanceExperiment`, `substanceDef`, `substanceChemical`, `substanceEffect`, `substanceConsumedTick`, `substanceCooldownUntilTick`, `knownSubstances` and `substanceHistoryInitialized`. The familiarity ledger keeps product and chemical keys independently of visible history. Game `HasEverTaken` records, mapped addictions and unambiguous current standard effects seed reliable knowledge; inference does not claim every product in a category was actually consumed. Unknown older use remains unknown. Missing fields preserve empty knowledge, no gate, no dose credit and all old accepted terms.

Experiments snapshot baseline +6 mood for three days/+7 trust; addiction supply uses ordinary equipment's +4 mood for five days/+4 trust. Both use a four-day fulfillment deadline, normal low-trust/repeat scaling and existing failure/withdrawal calculations. Refusal remains −1 mood for one day and clamped −1 trust; response expiry remains zero. No other request balance changes.

### Checks and game scenarios

`SubstanceChecks` runs through `Verify-Features.ps1`. Isolated fixtures cover traits/addiction limits, suppressed traits, supported versus medical/permanent/custom effects, category identity, journal deduplication, two-day observation and sleep pause, acceptance snapshots, different chemical/actor/pre-acceptance/late events, effect confirmation, replay protection, independent cooldowns, refusal versus expiry, Scribe fields/defaults and bilingual cards. Existing suites continue checking the other directions. The same suite reads recognition-relevant fields and inherited item classes from the installed Core drug XML, including Beer and GoJuice, and checks GoJuice's additional need/psyfocus actions. Extra-action, unknown auxiliary component and unsupported-effect fixtures supplement those definitions. This projection is not full game Def loading. Map availability, real ingestion/Harmony callbacks and complete save reconstruction still require RimWorld.

In a copied save:

- Give an eligible adult chemical interest, fewer than three addictions, policy permission and obtainable unfamiliar beer/smokeleaf/psychite. Keep the desire observable for two waking days; expect one queued offer. Accept and consume the named item: one result, no sustained-effect timer. Test teetotaler, suppressed interest, prohibiting beliefs and exactly three addictions: no experiment.
- Create a genuine psychite addiction, remove all doses, and provide unlocked ordinary production, a usable bench, a capable crafter and ingredients. After two observed days expect supply terms for the chemical. Manufacture a permitted product, accept, and consume it; another supported psychite product must also satisfy the chemical. Existing forbidden/unreachable or policy-disallowed stacks must explain access rather than trigger extra production.
- Consume without any request, save/reload, and offer a different form of that chemical: it is familiar. On an older save, verify the game log/addiction/specific effect seeds only reliable familiarity; no claim of never-used history appears.
- Use both substance variants in **Create test request…**, in both modes. Manual tests bypass the initial desire but still require a permitted supported target; addiction terms still require the actual addiction. Repeat consumption before acceptance, by another pawn, after expiry and after reload: no accepted reward. Check both languages and the one-dose progress display.
- Decline: unchanged −1 mood/−1 trust, normal personal gate plus fifteen-day substance gate. Expire another request: zero effects and the same gates. Other directions may resume after their usual personal gate. Complete/withdraw/break/close a substance obligation: its separate gate must persist through reload, with no duplicate history or effects.
- Run the cases in Core and with the available DLC separately, especially Ideology drug prohibitions, Biotech genes/suppressed traits and Anomaly mutant ingestion restrictions. Local XML/API inspection and fixture tests are not live DLC compatibility certification.

- GoJuice regression: in Development mode select **Create test request... > Set terms manually > Try a new substance**. GoJuice should be listed even if familiar or absent from stock. A disallowing policy leaves it visible with a reason; enable permission yourself and create/accept the request. An existing high without ingestion must not complete it. Consume a dose after acceptance and confirm its high appears/increases, one result is recorded and reload cannot replay it. Compare Beer. For natural generation use chemical interest/fascination, fewer than three addictions, an unfamiliar chemical and reachable permitted stock; diagnostics should show each product's actual blocker. The usual observation, cooldown and queue still apply.

For a refactor smoke test, open a copied existing save, verify an individual and shared active promise retain their exact targets/progress/effects, then test both substance rows in Requests in English and Ukrainian. Open/close diagnostics without advancing time: saved observation/cooldown/journal data must not change. Consume an unfamiliar permitted substance and reload: familiarity must update once. Repeat with your normal expansion set, checking shared-bed beliefs, royal constraints, role clothing, gene/suppressed traits, mutant exclusions, swimming and gravship transitions. Also verify decline still applies its displayed penalty while response expiry applies none.
