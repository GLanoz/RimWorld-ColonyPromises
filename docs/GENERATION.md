# Request generation and Development-mode diagnostics

## Execution path

RimWorld 1.6.4871 rev590 discovers non-abstract GameComponent subclasses through Game.FillComponents, creating missing components with a Game argument. This covers a new game and a save that predates the mod. GameComponentUtility forwards FinalizeInit and GameComponentTick to these components. The inspected call paths establish registration and callback availability, not successful execution on a live map.

GameComponent_Promises rebuilds pawn indexes in FinalizeInit. A new component starts with no observations and a due observation schedule; its first offer cannot precede initialization plus 30,000 ticks. Existing nextRequestObservation and nextRequestOffer values survive loading. The next game tick executes a due scan. Deficit gaps longer than 2,500 ticks restart observation, so loading does not credit missing time.

Each scheduled scan:

1. Discovers alive colonists across maps, caravans, and travelling transporters and enrolls eligible pawns. Opening the tab is not required.
2. Closes requests for missing, dead, destroyed, or departed pawns. Existing open requests are checked for relevance and expiry.
3. Skips occupied slots and ineligible pawns. Active promises and individual cooldowns clear deficit observations.
4. Evaluates one candidate per direction on a player home map, excluding suspended, downed, drafted, or mentally disturbed pawns.
5. Accumulates at most the verified interval since the previous sample when the candidate signature matches. The first sample earns zero time. A changed signature, missing candidate, or observation gap restarts the clock.
6. Once two observed days are reached, gathers ready candidates. If the global offer gate allows it, selects one, creates the request, clears that pawn's observations, and advances the global gate by half a day.

Observation is hourly (2,500 ticks); active-promise checks remain separate. Two days means 48 consecutive hourly intervals after the first eligible sample, not two days since installing the mod or opening the tab.

## Actual blockers and targets

| Direction | Qualifying deficit and candidate | What prevents or restarts observation |
| --- | --- | --- |
| Bedroom | Missing qualifying private bedroom, or its impressiveness below the bounded target | Ascetic trait, no game expectation, room already adequate, or a changed target signature |
| Recreation | Joy strictly below 35% | Missing joy need or joy reaching 35%; general mood alone is irrelevant |
| Equipment | An already equipped item below Normal quality or apparel below 50% durability, with a suitable same-def loose replacement available | No qualifying equipped item, trait/usability exclusion, or no reachable, allowed replacement; changing the physical equipped item or its worn-state signature |

Equipment stock must meet quality and, for apparel, at least 80% durability. It must be spawned, non-forbidden, reachable, and pass CanEquip. The request therefore does not arise merely because a pawn wears poor gear: a qualifying replacement must also remain available throughout sampled observation. The panel distinguishes an equipment deficit from the absence of a usable target.

Bedroom expectations come from ExpectationsUtility.CurrentExpectationFor(Pawn). The game's map expectation already uses WealthTotal, with relevant pawn title/role adjustments when installed DLC applies. Colony Promises adds no second wealth multiplier. The target uses expectation order, a one-stage Greedy adjustment, and the existing stage cap; Ascetic suppresses bedroom requests. Nudist suppresses apparel requests, Brawler suppresses ranged requests, and disabled Violent work excludes weapons. No invented preferences or skill-based comparisons are used.

## Cadence, queue, and cooldown

The half-day offer interval is **colony-wide**, including different home maps. At most one request is created per ordinary scan. Scans still update eligible observations while this gate is closed.

Selection favors the longest verified deficit. At the four-day accumulation cap, saved StartTick now gives older observations priority before pawn ID and kind. Previously all capped observations tied on duration and repeatedly favored smaller pawn IDs; sufficiently long queues could leave other pawns waiting indefinitely. A regression fixture reproduces the capped tie and checks that the older high-ID pawn wins.

For a finite set of continuously eligible, continuously deficient candidates, newly restarting deficits cannot repeatedly overtake older capped observations. Conditions that keep changing or becoming unobservable can still postpone a request indefinitely. There is no fixed maximum wait when the colony queue is busy.

Individual cooldowns do **not** observe deficits. After cooldown, the first eligible sample starts at zero, followed by two fresh observed days. Refusal/expiry/obsolete closure waits three days; completed accepted promises wait five days. An open request or active promise also occupies the slot. No balance thresholds or reward values changed with the diagnostics.

## Panel and Check now

Enable RimWorld's standard Development mode, select a pawn, and open Promises. The diagnostic block is in the tab's scrollable content, above history. In Development mode the currently selected map pawn can also be inspected when ineligible; reopen the tab after selecting it. The normal colonist menu remains restricted to eligible colonists.

The panel shows exclusion, slot state, cooldown, per-direction deficit and target availability, current conditions, candidate terms, saved and projected observation duration, selected-pawn last check, colony last/next scan ticks, and the global offer gate. A projected duration is exactly what the shared Project function would calculate if checked now; it is not saved by viewing.

Evaluate supplies both real generation candidates and diagnostic reasons. Project supplies both observation updates and the read-only projection. PeekState does not enroll an untracked pawn. Request consequence previews operate on a copy, so opening the tab does not rewrite saved unaccepted terms.

**Check now** runs the same selected-pawn observation and offer methods as the scheduled scan. It can update observations, maintain/close an existing request, or create a normally eligible request. It does not bypass persistence, eligibility, cooldown, occupied slots, or the global offer gate. It checks only the selected pawn rather than running the colony queue; it does not move the scheduled scan. Repeated same-tick clicks add no observation time. There is no force-offer button or per-scan log spam.

LastGenerationCheck, lastRequestObservation, and observation StartTick are saved. Missing last-check fields show not recorded. Missing old StartTick is conservatively inferred from the last saved sample minus saved duration; time beyond the old four-day cap cannot be reconstructed. Existing progress, requests, obligations, cooldowns, trust, and history are retained.

## Verification limits

Automated checks cover shared projection arithmetic, non-mutating reads, same-tick and gap handling, capped queue priority, conservative old-field defaults, saved observation ages/schedule markers, new/existing component initialization, the Development-mode button guard, and bilingual diagnostic rendering with the real translator. They use isolated game objects and Scribe fields, not a complete map or save.

Live pawn discovery, map candidates and stock/path restrictions, actual scheduling, window layout, and complete save loading still require the [manual generation scenarios](MANUAL_TESTS.md#reproducible-generation-checks). Compilation and assembly inspection do not confirm these behaviors.
