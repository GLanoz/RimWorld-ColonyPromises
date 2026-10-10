# Development notes

See the [README](../README.md) for player rules, the [API reference](API.md) for game integrations, and [manual tests](MANUAL_TESTS.md) for behavior that automated checks cannot establish.

## Architecture

| Source | Responsibility |
| --- | --- |
| PromiseData.cs | Saved request, deficit observations, promise terms/effects, trust, history, cooldown, and separate success ledger |
| RequestGenerator.cs | Game-backed candidates, deficit observation, relevance checks, request offers |
| RequestLifecycle.cs | Acceptance and unaccepted closure state transitions |
| PromiseBalance.cs | Legacy balance, progress arithmetic, and centralized RequestRules |
| PromiseConditions.cs | Condition registry, bedroom/equipment continuous holds, recreation eligibility |
| GameComponent_Promises.cs | Separate observation and active schedules, indexing, outcomes, notifications |
| PromiseConsequences.cs | Acceptance snapshots, legacy effects, mood memories, trust, repeat reductions |
| PromisePatches.cs | Actual recreation callbacks and existing interruption/lifecycle hooks |
| MainTabWindow_Promises.cs | Request review, acceptance/refusal, active progress, withdrawal, history |

Observation runs once per game hour (2,500 ticks), active checks every 250 ticks or sooner at a deadline. Only active promises enter the progress index. Requests are offered at least half a day apart across the colony. Longest observed deficit wins, followed by pawn ID and kind as deterministic tie breakers.

Generation derives one candidate per direction. The equipment signature includes the current physical item, target quality, and worn state. A request copies its candidate terms; accepting revalidates the need and stock, then transfers that record into the active slot. No reward is granted for meeting terms before acceptance. Unanswered request outcomes never enter the consequence applicator.

To add a direction, extend PromiseKind, register an IPromiseCondition, add candidate/relevance rules and balance, and provide localization. Reuse continuous-hold handling or supply a verified activity source for a new accumulation type.

## Persistence and migration

Scribe stores open requests (reason key, expectation def, terms, offer/expiry ticks, deadline days, significance), per-direction deficit clocks and signatures, cooldowns, trust, active progress, history, and success timestamps. Observation and global offer schedules are saved. Expectation defs rather than translated labels let language changes use the current game translation.

New promises save FrozenConsequences and the exact success/broken/withdrawn mood, trust, duration, and cooldown values. Snapshot refuses to overwrite a frozen record. Reward history and trust are evaluated at acceptance; later expectations, wealth, or passage of the repeat window do not change it.

The existing keys and enum values for old promise outcomes are retained. Missing request fields default to no request and empty observations. Missing FrozenConsequences defaults to false, selecting the old consequence calculation (+4 base success mood, +5 base trust, old failure/withdrawal rules, three-day cooldown, completion-time repeat factor). Old targets, deadlines, progress, credit clocks, trust, history, and the independent repeat ledger keep their stored values. In particular, old one-hour recreation records remain valid.

Continuous holds save their physical item and observation tick, with room identity restored by FinalizeInit. Same-tick loads add no time, and unobserved gaps earn no retrospective hold credit. Deficit gaps also restart observation. Completed and unaccepted outcomes clear deficit observations; cooldown time never contributes to a new request.

## History display

History reads ResolvedTick, AppliedMood, and AppliedTrust from the completed record. AppliedTrust is the actual change after clamping, not the promised delta. Rendering does not call the consequence calculator or use current trust or balance.

RimWorld's fixed-arity Translate overloads use NamedArgument and GrammarResolverSimple. A colon in a placeholder invokes a language function, so .NET clauses such as {2:0.00} can disappear instead of formatting a number. PromiseHistory formats numeric strings first, then supplies plain placeholders. English uses a decimal point; Ukrainian uses a comma, with localized day forms and explicit signed effects.

HasHistoryMetadata, HasResolvedTick, HasAppliedMood, and HasAppliedTrust distinguish known zeros from missing data. Resolution and consequence application set these flags; Scribe preserves them even when numeric defaults are omitted. When an old record lacks the metadata marker, only explicitly stored numeric fields are marked available. A missing old trust delta, including an omitted clamped zero, is conservatively shown as unavailable. Penalty-free outcomes have a clear no-change message. No missing effect is reconstructed from current balance.

## Balance

RequestRules and PromiseBalance are the authoritative sources. The README lists deadlines and base rewards. Significance is binary: a missing bedroom or two-stage room improvement, more than four recreation hours, or apparel that is both worn and below-normal quality is larger.

New broken mood = −(base success mood + 2) − round(3 × trust / 100), with midpoint values rounded away from zero. Broken trust is −8 or −10. Withdrawal is −2 mood/−3 trust or −3 mood/−4 trust. Success and broken memories last five days; withdrawal lasts three. Penalties never use the repeat factor.

Success factor = max(0.25, 0.5^recent successes of the same kind within 15 days). Mood is multiplied directly; trust uses midpoint-to-even rounding with a minimum of one before clamping. The displayed snapshot includes actual trust changes at the 0–100 boundaries. New resolutions wait five days; unaccepted closures wait three days, followed by fresh deficit observation.

Positive memories share one def and replace each other. Same-outcome negative memories also replace each other. Thought XML provides templates; moodPowerFactor and durationTicksOverride apply the saved effects. Longer deadlines do not independently contribute to rewards.

## Verification

Build the mod first; see the README for paths. Rule checks require .NET 10. Compiled checks need a fresh Windows PowerShell process and the game assemblies:

```powershell
dotnet run --project Tests/ColonyPromises.CoreChecks.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/Verify-Requests.ps1 -RimWorldDir "C:\Path\To\RimWorld"
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/Verify-Invalidation.ps1 -RimWorldDir "C:\Path\To\RimWorld"
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/Verify-History.ps1 -RimWorldDir "C:\Path\To\RimWorld"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Verify-Content.ps1 -RimWorldDir "C:\Path\To\RimWorld" -CecilPath "C:\Path\To\Mono.Cecil.dll"
```

Core checks cover progress clipping, duplicate credit, deficit duration/gaps, already-satisfied room targets, cooldown readiness, bounded requirements, and differentiated rewards. Compiled request fixtures cover penalty-free unaccepted closure, preserved repeat history, acceptance snapshots, frozen terms, and legacy field loading. Real Scribe reads old record/state fields and round-trips consequence fields, request metadata, and deficit clocks; these fixtures deliberately exclude engine finalization, non-null reference resolution, full map state, and game lifecycle. They do not validate a complete save migration.

History checks run under .NET 10 because the game formatter uses .NET Standard 2.1 APIs absent from Windows PowerShell's CLR. They reproduce the original missing-number failure through the real translator, then render English/Ukrainian history for every outcome, exercise actual applied effects and trust clamping, and round-trip history fields through Scribe. Keyed data is supplied to isolated language objects; no Unity window or complete game save is loaded.

Invalidation fixtures call the compiled bed-unclaim Prefix and the game's bedless UnclaimBed, then test real tracked-item selection with isolated game objects. Content checks validate patch targets/arguments, XML, localization keys, and placeholder parity.

No completed in-game run is documented. Full generation on real maps, equipment availability under mod restrictions, recreation practicality, UI, notifications, full save migration, and load-time room reconstruction remain manual tests. Successful compilation or IL inspection cannot establish those behaviors.
