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

A housing request starts with a **negative housing thought actually recognized by the game**: a poor-bedroom or barracks sleep memory, sleeping on the ground or outdoors, an unmet Greedy room requirement, or sleeping apart from a romantic partner. A missing private room, high expectations, or unrelated low mood is not enough. A colonist who is happy in a good barracks does not ask to move out.

The problem must remain current for **two observed game days**. Sleep memories last only a day, so a confirmed memory can continue to support observation while the matching physical problem remains. Expiration alone does not reset this evidence; fixing the bed, room role, enclosure, or relevant mood-producing quality does. Old memories cannot produce requests about already repaired housing. Active situational thoughts are checked directly, including the game's restrictions and nullifiers.

The target is the lowest game category that removes the confirmed penalty, including a Greedy requirement where applicable. Under Core rules, an awful bedroom can become neutral at dull, while a negative barracks experience can be solved by improving the barracks to its first nonnegative category. The exact category and whether a **bedroom or barracks** is required appear in the request. Expectations and wealth do not create a penalty or add an unnecessary quality floor.

A private-bedroom promise requires the sole assigned owner of a usable non-medical colonist bed in a proper enclosed Bedroom, with no other room owners or medical beds. A barracks-improvement promise instead requires a qualifying Barracks and permits other room owners. A ground-sleep request also requires a bed with a material cost; a free sleeping spot does not solve it. The **same bed and room must qualify continuously for one game day**. A brief reassignment is not enough.

Temperature complaints, disturbed sleep, jealousy of another bedroom, and an Ascetic's dislike of a room that is too impressive are not converted into arbitrary quality targets. The current promise cannot reliably guarantee those solutions; Development diagnostics explain the limitation.

### Shared bedrooms

A romantic pair can make **one shared bedroom request**, occupying both colonists' request slots. Both must be eligible and able to share a bed under the game's rules, including any active restrictions on shared sleeping. Neither may already have a request, promise, or cooldown. The pair needs two observed days of an unmet bedroom requirement and takes one place in the colony's offer queue.

Confirmed dissatisfaction of **one partner is enough**. The reason names that person and their observed penalty, without claiming the other partner is dissatisfied. The target must address that problem and avoid a known conflicting requirement for either person. Having separate beds alone is insufficient: the game's sleeping-apart thought can be inactive when partners already share a room. Already resolved problems create no request. Pairs involving an Ascetic, ambiguous multiple partners, or no usable loaded double-bed type do not receive shared or conflicting new individual bedroom requests; Development diagnostics explain the blocker.

Assign **exactly these two colonists to the same non-medical colonist bed with at least two sleeping slots**. Both must be able to use it. Follow the displayed room requirement: a private Bedroom excludes other room owners; an explicit shared-barracks improvement permits them. Both exclude medical beds. Keep the same qualifying bed and room for a continuous day. They do not need to sleep at the same time. Removing either owner, changing the bed, or losing qualifying conditions resets the common progress.

Either participant can accept, decline, or withdraw the shared record. Both panels show the same deadline and progress, a link to the partner, and separate personal consequence figures. Rewards use the normal bedroom scale for each participant, adjusted by that person's trust and repeat history; being a pair does not increase the requirement's significance. Each person's effects, cooldown, and history apply once. Eligible unaccepted recovery remains personal and grants no mood reward.

The Requests list, button count, offer letter, and reminder include the shared record once. A breakup, either partner's death or faction departure, or an actual change making shared sleeping forbidden closes it without penalty for both. Temporary absence or unavailable relationship data does not establish a breakup. Both must be observable on the same map to earn hold progress; deadlines continue during absence. Existing individual promises are not converted into shared ones.

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
| Housing: smaller improvement | 5 days | +5 | +5 |
| Housing: missing room or improvement of two or more categories | 7 days | +7 | +6 |
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

Accepted housing promises remain binding even if their original negative thought disappears. Older unanswered housing requests based only on expectations close as obsolete without penalties or rewards; fresh requests need new confirmed housing evidence and two observed days. Old expectation-only observation time is not treated as evidence of dissatisfaction.

Equipment requests consider existing loose stock, not recipes, traders, inventory, or future purchases. Custom recreation may not count, and other mods' unusual equipment, ownership, room, or usability rules may not be fully recognized. Very brief changes outside normal game events can escape detection. Compatibility with every mod combination is not guaranteed.

Release builds and automated checks pass, and history display has been verified in-game by the player. Request generation, notifications, interface layout, full save loading and migration, and mod combinations still need in-game testing. Adding the mod to an existing save is implemented but has not been verified on a real save; safe removal is also unverified. Compilation and isolated checks do not establish correct gameplay behavior.

For implementation, build instructions, verification tools, and manual scenarios, see the [technical documentation](docs/API.md).
