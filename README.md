# Colony Promises

Colonists ask for a better bedroom, time for recreation, or improved equipment after a need has remained unmet. You can accept a request as a promise, decline it, or leave it unanswered. Keeping and breaking promises affects mood and trust in the colony's leadership, without changing personal relationships between colonists.

## Requirements and installation

- RimWorld **1.6**; the current build targets **1.6.4871 rev591**.
- **Harmony**, enabled and loaded before Colony Promises.
- No DLC required. English and Ukrainian interfaces are included.

Copy the ready-to-use Colony Promises folder into RimWorld's `Mods` folder. It should contain `About/`, `Defs/`, `Languages/`, `LoadFolders.xml`, and `1.6/Assemblies/ColonyPromises.dll`. Enable Harmony and Colony Promises, keep Harmony before Colony Promises in the load order, then restart. Harmony is a separate dependency; its DLL and the game's DLLs are not included.

## Receiving and answering requests

Only free adult humanlike player colonists with a mood need can make requests. Guests, quest lodgers, prisoners, and slaves are excluded. Each colonist can have **one open request or one active promise**.

A qualifying unmet need must be observed for **two game days** on a player home map. Evidence restarts when the need is satisfied, the colonist becomes unavailable for observation, or the relevant conditions change. Drafted, downed, suspended, and mentally disturbed colonists do not build this evidence. Low general mood alone does not cause a request.

New requests appear at least **half a day apart across the whole colony**, including different home maps. Longer-standing deficits have priority, so a busy colony can delay an individual request. You have **two days** to respond once it appears.

Select a colonist and open **Promises** on the bottom bar, or use **Open Promises** in a request letter. Review the reason, exact requirement, response time, fulfillment deadline, and mood and trust changes before accepting. Acceptance checks that the request is still relevant. You cannot choose arbitrary targets or deadlines.

- **Accept:** turn the request into a timed promise. Meet its conditions before the deadline to earn the displayed reward.
- **Decline:** close it without a mood or trust penalty.
- **Leave unanswered:** it expires without a penalty. Satisfying it before acceptance normally gives no reward; limited recovery at very low trust is described below.
- **Withdraw an accepted promise:** confirm the displayed penalty. Withdrawal is less costly than breaking it.

An unaccepted request also closes without penalty when it becomes obsolete, such as when its required replacement equipment is no longer available. Already-satisfied requests cannot be accepted for a reward.

## What colonists request

### Bedroom

A colonist may ask for a private bedroom or an improvement to an inadequate one. The target follows the game's expectations, which already account for colony wealth. Under the standard game categories, targets range from mediocre to somewhat impressive; an existing bedroom is asked to improve by at most two categories. The exact category appears in the request.

Greedy colonists expect one category more, within the same upper limit. Ascetics do not make bedroom requests. A bedroom already meeting the desired standard produces none.

The colonist must be the sole assigned owner of a non-medical colonist bed in a proper enclosed bedroom. Other room owners, medical beds, barracks, hospitals, and outdoor spaces disqualify it. The **same bed and room must qualify continuously for one game day**. A brief reassignment is not enough.

### Recreation

Recreation requests require the recreation need to stay below **35%** for two observed days. The target starts at **4 hours** of actual recreation. It rises by two hours if at least a day of the observed deficit was below 15%, and by another two if the deficit lasted at least three days while waiting for an offer, up to **8 hours**.

Recognized games, social relaxation, skygazing, and recreational walking count. Scheduling recreation, travelling to an activity, sleeping, being downed, medical confinement, drafting, mental breaks, and forced idleness do not. Accepted promises accumulate activity from acceptance onward, across multiple sessions.

### Equipment

Equipment requests improve an item the colonist already wears or wields. Simply lacking a weapon or clothing is not enough.

- An item below Normal quality calls for the **same type, one quality step higher**.
- Apparel below **50% durability** calls for the same type at no worse quality and at least **80% durability**.
- Apparel both worn and poor must meet both improvements. All requested apparel replacements must remain at least 80% durable throughout the hold, including requests prompted only by quality.

A usable replacement must already exist as reachable, non-forbidden loose stock on the home map. Core and loaded-mod items can qualify if they have quality. Nonviolent colonists do not request weapons, brawlers do not request ranged weapons, and nudists do not request apparel. Body-part and equipping restrictions also apply.

The **same physical item must remain equipped for one game day**. Storage and inventory do not count. Comparisons stay within the same item type; higher quality does not make an unrelated weapon a preferred replacement.

## Deadlines and consequences

These are base success rewards at trust 40 or above, before repeat reductions. Mood rewards last **five days**. The review window shows exact current values and durations. Acceptance fixes the requirement, deadline, effects, and cooldown; later changes to wealth, expectations, or trust do not rewrite those terms. A longer deadline does not itself increase the reward.

| Request | Deadline after acceptance | Success mood | Success trust |
| --- | --- | --- | --- |
| Bedroom: one-category improvement | 5 days | +5 | +5 |
| Bedroom: missing room or two-category improvement | 7 days | +7 | +6 |
| Recreation: 4 hours | 3 days | +3 | +3 |
| Recreation: 6–8 hours | 4 days | +4 | +4 |
| Equipment: one deficiency | 4 days | +4 | +4 |
| Equipment: worn and below-normal quality | 5 days | +5 | +5 |

Breaking a promise causes a **five-day** negative mood memory and costs **8 trust** for smaller requests or **10 trust** for larger ones. Its mood penalty is two points stronger than the base success reward, with up to three additional points of disappointment at high trust. The exact penalty is shown before acceptance.

Withdrawal causes **−2 mood and −3 trust** for smaller requests, or **−3 mood and −4 trust** for larger ones. Its mood memory lasts **three days**. Trust changes are bounded by 0 and 100.

Fulfillment is checked before failure at the deadline. Each result applies once. A new positive promise memory replaces the previous one instead of stacking; repeated negative memories of the same outcome also replace themselves.

## Trust, recovery, and repeat limits

Trust starts at **50** and stays between **0 and 100**. It represents confidence in leadership, with no permanent mood bonus or penalty. Below **40 trust**, new promises give a smoothly reduced positive mood reward: 25% of the normal reward at zero trust, rising to the full reward at 40. Trust gains are not reduced by low trust, and zero trust still allows requests and acceptance.

Successful promises of the same type within the preceding **15 days** halve positive rewards for each recent success, down to 25%. Low-trust and repeat reductions combine. Success still grants at least one trust point before the upper bound; even at zero trust, the smallest repeated recreation reward remains positive. Failure and withdrawal penalties are never reduced.

An accepted promise's completion, failure, or withdrawal starts a **five-day cooldown**. Declining, expiry, obsolete closure, or satisfying an unaccepted request starts a **three-day cooldown**. Deficit observation starts afresh afterward; old unmet time and cooldown time cannot immediately produce another request. Refusal, reloading, and the visible history limit do not erase repeat tracking. Older promises from the manual-creation version retain their previous consequence rules and three-day outcome cooldown.

### Recovery without a promise

At **20 trust or below**, fulfilling a new, still-open request without accepting it may restore **+2 trust**, with **no mood bonus**. The tab shows when this option is available and its progress.

Bedroom and equipment conditions must hold for a full continuous day. Recreation requires the requested actual hours since the offer and a recreation need restored to at least 35%. Proof must be complete by the two-day response deadline. Brief bed assignments, quick equipment swaps, or merely raising the need do not qualify. Accepting midway starts the promise's progress from zero.

Recovery happens once per request, at most once per colonist every **10 days**, and not again for the same request type within **15 days**. The normal request cooldown and fresh observation also apply. Old open requests, completed history entries, and improvements made without a request do not grant retroactive recovery.

## Interface, history, and settings

The Promises button shows the number of current unanswered requests. Inside the window, **Requests** lists only unanswered requests, ordered by the nearest response deadline. Each row shows the colonist, request type, a short requirement, and time remaining. Click it to open that colonist's details in **Colonists**; request letters take you there too.

**Colonists** has a permanent list on the left, with request, active-promise, cooldown, or no-request status. The selected colonist's trust, conditions, exact consequences, response time or active progress, actions, and personal history appear on the right. Lists and details scroll independently. Ordinary reopening retains the last internal tab and valid selection for the current session; a departed colonist is replaced by an available selection. Acceptance, refusal, and withdrawal use confirmation dialogs. General trust and repeat rules are available under **More details**.

History shows the result, elapsed time, and mood and trust changes actually applied, including trust clamping. Later balance or trust changes do not recalculate history. Results without consequences say so; missing information in older records is marked unavailable.

Each new request sends one letter naming the colonist and requirement. A single reminder arrives **half a day before the response deadline**. Turn reminders off in the Colony Promises mod settings; initial request letters remain enabled. Notification state is saved to prevent repeated reminders after loading. Closed, expired, and accepted requests leave the count and list.

With standard **Development mode** enabled, expand the diagnostic block in a colonist's details to see generation reasons, observation time, target availability, cooldowns, and upcoming checks. **Check now** runs a normal check without bypassing rules. Diagnostics start collapsed; viewing them does not change progress or create requests.

## Absence, saves, and limitations

Illness, combat, caravans, and cryptosleep **do not pause deadlines**. Caravans do not generate requests or earn progress. Open requests can expire while away, and accepted promises remain active. Accumulated recreation is retained, but bedroom and equipment holds reset on departure and need a fresh continuous day on return. Death, destruction, or leaving the player faction closes the obligation without penalty; temporary absence alone does not cancel it.

Bed reassignment, failed room conditions, switching rooms or physical equipment, and removing equipment reset the affected hold. Room shape and item quality changes reset it even when they improve conditions. These events do not reset accumulated recreation.

Existing promises retain their saved requirements, progress, deadlines, trust, history, and previous consequence rules, including old one-hour recreation targets. New promises use the current rules described here.

Equipment requests consider existing loose stock, not recipes, traders, inventory, or future purchases. Custom recreation may not count, and other mods' unusual equipment, ownership, room, or usability rules may not be fully recognized. Very brief changes outside normal game events can escape detection. Compatibility with every mod combination is not guaranteed.

Release builds and automated checks pass, and history display has been verified in-game by the player. Request generation, notifications, interface layout, full save loading and migration, and mod combinations still need in-game testing. Adding the mod to an existing save is implemented but has not been verified on a real save; safe removal is also unverified. Compilation and isolated checks do not establish correct gameplay behavior.

For implementation, build instructions, verification tools, and manual scenarios, see the [technical documentation](docs/API.md).
