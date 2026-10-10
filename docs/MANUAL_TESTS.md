# Manual testing

These scenarios are prepared, not completed in-game. Use a copied save and a separate colony. Start with Core, Harmony, and Colony Promises without DLC. Keep production balance unchanged. Use controlled saves/dev mode to provide test conditions, and inspect the Promises tab, Needs tab, and error log.

## Request generation and review

| Scenario | Setup and action | Expected result |
| --- | --- | --- |
| Eligibility | Include adults, children, guests, prisoners, slaves, and quest lodgers | Only free eligible adult player colonists generate requests |
| Short deficit | Briefly lower joy, unclaim a bed, or remove equipment | No immediate request; missing equipment alone produces none |
| Persistent deficit | Leave joy below 35%, a bedroom below target, or qualifying poor/worn gear for two observed days | Relevant request with an accurate reason; no request before the threshold |
| Satisfied bedroom | Supply a private bedroom at or above target | No bedroom request |
| Existing bedroom | Keep a below-target private bedroom | Limited one- or two-stage improvement, never an arbitrary highest stage |
| Expectations and traits | Compare expectation levels and greedy/ascetic colonists | Bounded room targets follow rules; ascetics do not ask; wealth is not separately multiplied |
| Recreation severity | Compare two days below 35%, a day below 15%, and a prolonged waiting deficit | Targets of 4, 6, or 8 hours; never a manual one-hour option |
| Equipment quality | Equip below-normal quality; provide a usable higher-quality copy of the same def in reachable loose stock | Request for one quality step higher, held for one day |
| Worn apparel | Wear apparel below 50%; provide a same-type replacement at least 80% durable and no worse quality | Replacement request with durability visible; quality also improves if below normal |
| Equipment restrictions | Test nonviolent, brawler, nudist, missing body parts, biocoded stock, forbidden/unreachable stock | No unusable target or weapon request for a nonviolent pawn; no ranged request for a brawler or apparel request for a nudist |
| Loaded mod items | Repeat with quality-bearing apparel/weapons from a loaded mod, then a Core-only colony | Only actual loaded and available same-def stock is requested; no missing-DLC target |
| Gradual offers | Create persistent deficits for many colonists | At most one offer per half day across the colony; one open request or active promise per pawn |
| Review and cancellation | Inspect all fields in both languages; cancel acceptance/refusal dialogs | Exact signed mood/trust effects, durations, response time, deadline, reason; cancellation changes nothing |
| Accept after satisfaction | Satisfy an open request before clicking/confirming acceptance | Acceptance rejected; request closes without reward, including at response expiry |
| Obsolete request | Remove available replacement stock or eliminate the underlying deficit | Unaccepted request closes without penalty, with a distinct obsolete result |
| Refusal and expiry | Decline one request and leave another unanswered for two days | No mood/trust effect; one history result; three-day cooldown; repeat ledger retained |
| Fresh deficit after cooldown | Fulfill/decline a request with an old deficit, then keep the need unmet | No immediate follow-up: cooldown first, then two newly observed days |
| Frozen obligation | Accept; then change wealth, expectations, and let a previous success age out | Target, deadline, reward, penalties, and durations stay fixed |
| No manual farming | Inspect the whole tab and colonist menu | No arbitrary type/quality/hour/deadline creation controls; menu marks requests and active promises |

## Fulfillment, outcomes, and interruption

| Scenario | Action | Expected result |
| --- | --- | --- |
| Bedroom fulfillment | Accept, provide the requested private bedroom, retain the same sole-owned non-medical bed/room | Fulfillment after 24 verified continuous hours; one memory/result |
| Invalid room | Use a barracks, shared/medical bed, hospital, outdoors, or another room owner | No qualifying progress |
| Bed interruption | Unclaim/reclaim a tracked bed while paused, or add/remove another owner | Only that hold resets, even between periodic checks |
| Room changes | Drop below target, change shape, or improve contents while staying above target | Failure/shape resets; valid ordinary improvements preserve the hold |
| Recreation fulfillment | Accept; try horseshoes, chess, television, skygazing, social relaxation, and walking | Only actual recognized activity counts; sessions accumulate to the requested hours |
| Recreation practicality | Complete 4 hours in 3 days and 6–8 in 4 days with realistic schedules, expectations, and tolerances | Record whether deadlines are practical; do not assume arithmetic alone establishes balance |
| Excluded leisure | Schedule recreation while working; sleep, become downed, draft, idle, or watch from a medical bed/hospital | These intervals and travel to recreation do not count |
| Equipment fulfillment | Leave the target in storage/inventory, then equip it | Only the same equipped physical item qualifies for a continuous day |
| Durability/quality | Supply below-target quality or durability, then a qualifying copy | Only qualifying items count; durability must remain at least 80% when required |
| Brief removal/copy swap | Remove and return gear while paused; separately replace with an identical copy | Hold resets without disturbing anyone else's promise |
| Quality improvement | Change tracked item's quality | Hold resets even if quality improves |
| Differentiated success | Complete basic recreation and a substantial bedroom request | +3 mood/+3 trust versus +7 mood/+6 trust before reductions/caps, for five mood days |
| Broken/high trust | Let an unmet accepted request expire at known trust | Exactly the saved penalty shown on acceptance, one result, five-day cooldown |
| Withdrawal | Confirm before expiry; separately confirm at/after expiry | Saved milder penalty before expiry; fulfillment/failure takes precedence at expiry |
| Exact deadline | Arrange sufficient verified progress at expiry | Fulfillment checked before failure |
| Repeat reductions | Complete repeated requests after cooldown and fresh deficit within 15 days | Acceptance freezes half/quarter rewards; refusals/reloads do not clear ledger; penalties never diminish |
| Memory replacement | Complete different directions and repeat a negative outcome | One positive promise memory; same-outcome negative memories replace themselves |
| Death/faction exit | Kill or remove a pawn with an open request or active promise; reload afterward | Close without penalty; no repeated result or revival |
| Caravan | Depart with an open request or each promise type, return before/after expiry | Response/fulfillment clocks keep running; no away-time credit or new requests; recreation remains, holds restart |
| Capture without faction change | Capture and rescue an obligated pawn | Absence alone does not cancel; deadline continues |

## Item invalidation regression

1. Accept colonist A's recreation request and accumulate partial progress.
2. Ensure B has no assigned bed. Assign B a first bed while paused; the game calls UnclaimBed with no previous bed.
3. A's progress and credit clock must remain unchanged.
4. With A still accumulating recreation, accept B's bedroom request and C's equipment request. Establish partial holds and track a separate fourth pawn's item.
5. Unassign B's actual bed while paused: only B's hold resets.
6. Remove C's tracked weapon/apparel: only C's hold resets. Removing an unrelated item must change no tracked progress.

The [automated fixture](../Tests/Verify-Invalidation.ps1) calls the compiled Prefix and isolated handlers; this scenario validates actual Harmony execution in-game.

## Saves, migration, and presentation

| Scenario | Action | Expected result |
| --- | --- | --- |
| Existing save without mod | Add the mod and load | Trust defaults to 50; no fabricated deficit/progress; gradual offers after observation |
| Old mod save | Load saved bedroom, equipment, and one-hour recreation promises from the manual-creation version | Same active records, targets, progress, deadlines, credit clocks, trust/history/ledger; old consequence rules and three-day outcome cooldown |
| Deficit/request save | Save partial deficits and open requests; reload several times at the same tick | Deficit clocks/signatures and request terms/expiry preserved; no load bonus or duplicate offer |
| Accepted save | Save partial holds/recreation and frozen consequences; reload | Qualifying progress and saved effects remain; room references reconstruct without spurious reset |
| Outcome/cooldown save | Reload after each outcome | No repeated notification, memory, trust change, cooldown restart, or erased repeat ledger |
| Caravan save | Reload an absent pawn's request/obligation | No unverified away-time progress; deadlines continue |
| Localization | Switch English/Ukrainian, including a saved request and long mod item names | Reasons, expectation labels, signs, durations, and history localize; no raw keys |
| Layout and scale | Use small screens, UI scaling, and 12 history entries | Scrolling, buttons, confirmations, conditions, and consequences remain readable |
| Performance | Use a large colony with mixed requests/promises | Observations hourly, active checks every 250 ticks; no repeated result spam |

Full reference loading, real save migration, safe removal, and compatibility with other mods cannot be established by isolated field checks. After the baseline run, test relevant DLC and normal mod combinations separately. DLC remains optional.