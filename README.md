# Colony Promises

Colonists ask for improvements after a need has remained unmet. You can promise to fulfill a request, decline it, or leave it unanswered. Accepted promises affect mood and trust in the colony's leadership; they do not change personal relationships between pawns.

## Requirements and installation

- RimWorld **1.6**, built against **1.6.4871 rev590**.
- **Harmony**, enabled and loaded before Colony Promises.
- No DLC required. English and Ukrainian interfaces are included.

Copy the prepared mod folder into your game's Mods directory. It must contain About, Defs, Languages, LoadFolders.xml, and 1.6/Assemblies/ColonyPromises.dll. Enable Harmony and Colony Promises, then restart the game. Harmony and the game's DLLs are not included in the package.

## Requests and promises

Select a colonist and open **Promises** on the bottom bar. The colonist menu marks open requests and active promises. The window shows the reason, exact requirement, response time, fulfillment deadline, mood effects with their durations, and trust changes. Accepting requires confirmation and a fresh relevance check. Arbitrary promise creation and manual deadline selection are no longer available.

Only free adult humanlike player colonists with a mood need are eligible. Guests, quest lodgers, prisoners, and slaves are excluded. Each colonist can have one open request or one active promise.

Requests require **two observed game days** of an unmet need on a player home map. Short interruptions do not immediately produce requests. The mod offers at most one new request across the colony every half day, favoring the longest observed deficit. An open request allows **two days** to respond. Declining, ignoring, or satisfying an unaccepted request gives no reward or penalty. Requests that cease to be relevant also close without penalty.

For testing, enable the game's **Development mode** to see a generation diagnostic panel in Promises. It shows eligibility, unmet needs, observation clocks, candidate requirements, cooldowns, and the colony-wide offer interval. **Check now** runs an ordinary check for the selected pawn without bypassing rules. See [generation diagnostics](docs/GENERATION.md) and [reproducible manual checks](docs/MANUAL_TESTS.md#reproducible-generation-checks).

### Bedroom

A colonist can request a private bedroom or an improvement to an existing one. The target uses the game's current expectations, which already reflect colony wealth. It starts at impressiveness stage 2, rises with expectations, and is capped at stage 5; an existing bedroom is asked to improve by at most two stages. The interface uses the game's localized category names.

Greedy colonists have a target one stage higher within that cap. Ascetics do not generate bedroom requests. A room already at the target produces no request.

The colonist must be the sole assigned owner of a non-medical bed in a proper enclosed bedroom, with no other room owners or medical beds. Barracks and hospitals do not qualify. The same bed and room must qualify continuously for **one game day**.

### Recreation

Recreation requests require joy to remain below **35%** for two observed days. The target is **4 hours** of actual activity, plus 2 hours if at least a day of the deficit was below 15%, and another 2 hours if the deficit lasted at least three days while waiting to be offered. The maximum is **8 hours**.

Recognized games, social relaxation, skygazing, and recreational walking count through the game's recreation activity callbacks. Timetable assignments, travel to activities, sleep, downing, hospitals, drafting, mental breaks, and forced idleness do not count. Hours accumulate only after acceptance and can span several sessions.

### Equipment

Requests improve something the colonist already wears or wields. Missing weapons or clothing do not automatically create a request.

- Below-normal quality: request the same item type, one quality step higher.
- Apparel below 50% durability: request the same type at no worse quality and at least **80% durability**. The durability requirement applies throughout the hold.
- If both apply, the replacement must meet both requirements.

A usable, reachable, non-forbidden replacement must already exist as loose stock on the home map. This discovers items from Core and loaded mods without demanding an uninstalled expansion or speculative crafting recipe. Items without quality are excluded. Nonviolent colonists do not request weapons; brawlers do not request ranged weapons; nudists do not request apparel. Body-part and equipping checks restrict usable replacements.

The same physical item must remain actually equipped for **one game day**. Inventory and storage do not count. The mod compares copies of the same type, rather than treating a higher-quality unrelated weapon as universally better.

## Balance and deadlines

Terms and consequences are fixed when a request is accepted. Changes to expectations or colony wealth do not rewrite the obligation. A longer deadline does not itself increase rewards.

| Request | Deadline after acceptance | Success mood for 5 days | Success trust |
| --- | --- | --- | --- |
| Bedroom, one-stage improvement | 5 days | +5 | +5 |
| Bedroom, missing room or two-stage improvement | 7 days | +7 | +6 |
| Recreation, 4 hours | 3 days | +3 | +3 |
| Recreation, 6–8 hours | 4 days | +4 | +4 |
| Equipment, one deficiency | 4 days | +4 | +4 |
| Equipment, worn and below-normal quality | 5 days | +5 | +5 |

Breaking a promise gives a five-day negative memory: −(base success mood + 2), with up to 3 additional points of disappointment at high trust. Trust falls by 8 for smaller requests or 10 for larger ones. Withdrawal gives −2 mood and −3 trust for smaller requests, or −3 mood and −4 trust for larger ones; its mood memory lasts three days. The window shows the exact values before acceptance, including trust bounds and repeat reductions.

Trust starts at **50**, stays within **0–100**, and gives no permanent mood bonus. Successful promises of the same type in the preceding **15 days** halve positive rewards per recent success, down to 25%; the reduction is fixed at acceptance. Failure and withdrawal penalties never diminish. A new positive promise memory replaces the previous one.

Completed promises start a **five-day cooldown**. Declined, expired, satisfied unaccepted, or obsolete requests start a **three-day cooldown**. Old deficit observations are cleared; cooldown time does not accumulate a new deficit. A fresh two-day observation is needed afterward. Declining or reloading does not clear the repeat ledger.

## Progress, absence, and saves

Illness, combat, caravans, and cryptosleep do not pause deadlines. Fulfillment is checked before failure at expiry. Death, destruction, or leaving the player faction closes the obligation without a penalty.

Caravans do not generate requests or earn progress. Open requests can expire while away. Accepted promises remain active; accumulated recreation remains, while bedroom and equipment holds reset on departure. Returning requires a new continuous day for those holds.

Bed reassignment, losing room conditions, switching rooms or physical items, and removing equipment reset the affected hold. Room shape or item quality changes also reset it, even if they improve conditions. Bed and equipment events do not reset recreation progress.

Existing promises retain their requirements, progress, deadlines, trust, history, and previous consequence rules, including old one-hour recreation targets. Newly accepted requests use the new frozen consequences.

Adding the mod to existing saves is implemented but has not been verified on a real save; safe removal is also unverified. Release compilation, automated rule checks, compiled lifecycle/invalidation checks, and isolated Scribe field checks pass. History display has been checked in-game by the player. Request generation and the Development-mode diagnostics still require in-game testing; automated checks do not establish full save migration, Harmony execution, or compatibility with other mods. See the [manual scenarios](docs/MANUAL_TESTS.md).

## Building from source

Use a .NET SDK with .NET Framework 4.7.2 references, RimWorld's managed assemblies, and Harmony's 0Harmony.dll:

```powershell
dotnet build Source/ColonyPromises.csproj -c Release -p:RimWorldDir="C:\Path\To\RimWorld" -p:HarmonyPath="C:\Path\To\0Harmony.dll"
```

The output is 1.6/Assemblies/ColonyPromises.dll. The project uses the Windows RimWorldWin64_Data/Managed layout. Balance values live in [PromiseBalance.cs](Source/PromiseBalance.cs).

See [development notes](docs/DEVELOPMENT.md) for architecture, persistence, and test commands, and the [API reference](docs/API.md) for checked APIs and integration limits.
