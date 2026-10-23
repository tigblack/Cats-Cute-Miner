# Cat Miners: Idle Tycoon — Blacksmith Rescue and Giant Final Excavations
## Implementation prompt for Claude Code / Opus 5.5

Implement the following changes in the existing Unity project. Deliver a polished, functioning result with finished visuals, correct progression, reliable saves, and measured excavation times.

These instructions supersede conflicting earlier guidance:
- Equipment unlocks after rescuing the blacksmith and completing Mine 2, before entering Mine 3. It must no longer depend solely on entering Mine 3.
- Starting with Mine 3, every mine has one giant final obstruction with deliberately high HP.
- The long final-obstruction durations below are intentional. Do not remove these difficulty spikes through general balance smoothing.
- Preserve the HP of ordinary existing blocks. Do not rebalance the rest of a mine to compensate for its final obstruction.
- Continue using bounded verification: approximately 25 representative levels, at most 30. Do not run the entire 144-level campaign.

Use “Mine” for player-facing campaign level numbers. Inspect the actual current implementation and configuration before choosing integration points.

## 1. Rescue the blacksmith in Mine 2

Keep Mia's rescue in Mine 1. Add a second distinct rescue near the end of Mine 2: a cat blacksmith, provisionally named Bruno.

Make Bruno visually recognizable through a smith's apron, hammer, gloves, or similar details. Create a matching portrait and in-world sprite. Preserve the existing cozy character style and pixel-art direction.

Place him in a cage or blocked rescue area on the intended final route through Mine 2. Use the existing rescue framework where possible, with a separate persistent identity and reward receipt.

His location must have a reachable approach and a valid route back to the base. Automated cats must be able to complete the rescue through the normal mining loop. Manual mining may help but is not required.

Mining effects must hit the lock or obstruction, not the character. Keep the cage and moving door out of permanent pipe-routing supports.

### Establish the purpose

Introduce the objective early enough for the player to anticipate the reward:
- “Rescue the blacksmith”
- “Bruno can help you equip better tools.”
- Equipment preview: “Rescue Bruno and complete Mine 2.”

Keep the existing Cards introduction in Mine 2. Queue its explanation and the rescue presentation so they do not compete through simultaneous modals or arrows.

The blacksmith rescue is the primary world objective. The card upgrade is a short supporting lesson.

### Finish the rescue properly

Use a short, finished rescue sequence: lock or debris clears, the door opens, Bruno reacts, and he follows a valid route toward the base.

Bruno becomes a persistent equipment-service character associated with a compact anvil or workbench at the base. He is not an extra free mining worker, and his presence must not silently change the mining-team limit.

Use the workbench as a visual connection to Equipment if it fits the existing base. Keep unloading positions, entrances, upgrade interactions, and walking routes clear.

Do not add an unrelated crafting economy, forging timers, or a city-building system for this task.

## 2. Unlock Equipment immediately after Mine 2

For new profiles, the unlock condition is:

BlacksmithRescued AND Mine2Completed.

Use actual completion state, not simply “currently in Mine 2.” Finish the normal required clearing and delivery conditions before completing that mine.

At completion:
1. Persist Bruno's rescue and the Mine 2 completion.
2. Unlock Equipment permanently.
3. Guarantee one functional starter pickaxe through the existing item system.
4. Present a short equipment introduction and a real equip action.
5. Allow the normal transition to Mine 3.

The Equipment screen must already be usable before Mine 3 loads. A skipped explanation must not prevent access or remove the reward.

Use the existing starter-item definition and avoid a duplicate grant from the old Mine 3 introduction. Merge the old and new reward paths through an idempotent receipt or the project's equivalent mechanism.

Explain the actual item benefit and who receives it. Show the real before/after stats and that equipment persists between mines.

If the player already owns an appropriate starter item, reuse it where appropriate instead of granting another copy unnecessarily.

Preserve existing equipment owners and their access. Players who already completed Mine 2 should receive the blacksmith service through a compatible once-only migration without replaying the mine or losing their items.

## 3. Add one giant final obstruction from Mine 3 onward

Every mine starting with Mine 3 must end with one prominent, mandatory excavation target.

This is a deliberately large final task. Keep the existing ordinary blocks and their HP intact.

Place the obstruction in a final chamber or passage on the route to completion. It must be impossible to bypass through another branch. Its destruction is an additional required completion condition, alongside the existing cargo and other completion rules.

Do not add these giant high-HP targets to Mines 1 and 2. Their climaxes remain the character rescues.

Use one logical obstruction with shared HP, even if the art is assembled from multiple sprites or visual sections. Do not create independent hitboxes that accidentally multiply damage or count as several final targets.

The obstruction should exist in the level layout from the start and be discovered through normal exploration. Avoid visibly spawning a huge boulder after the player believes the chamber is clear.

The ordinary route must provide enough resources to hire the intended full crew and reach a sensible working upgrade state before this final phase. Inspect affordability; if a contradiction exists, report it and make the smallest necessary final-phase economy adjustment. Do not solve it by altering ordinary-block HP.

## 4. Make the final obstruction visually exceptional

Create a large, detailed mineral formation or ancient rockfall that feels valuable and exciting to uncover.

It should be several times larger than an ordinary obstacle, with an authored silhouette, multiple rock layers, embedded minerals, and a strong focal feature. Do not merely enlarge a generic rock sprite.

Suggested visual progression:

| Mine | Visual direction |
| --- | --- |
| 3 | A massive crystal-bearing rockfall with vivid geodes, precious mineral veins, and deep cracks. |
| 4 | An ancient amber formation with fossilized shells, ammonites, and petrified wood emerging from the stone. |
| 5 | A spectacular dinosaur fossil formation: a recognizable skull, rib cage, or partial skeleton embedded among rock and crystals. |
| 6 onward | Varied monumental combinations of hard rock, valuable crystals, large fossils, dinosaur bones, and ancient mineral layers. |

Use these themes as artwork, not as an instruction to invent additional currencies or collection systems.

Show progressive visual damage at meaningful thresholds, such as 75%, 50%, 25%, and 10% HP remaining. Expose more crystal faces and fossil detail as the stone is removed. These stages must reflect the same remaining HP and must not reset it.

If a fossil is the focal reward, animate clearing the surrounding rock rather than making the cats attack the exposed animal skeleton. A final fossil reveal can be decorative unless an appropriate existing reward system is already available.

Include:
- Material-specific hit feedback and localized chips.
- Controlled dust and crystal glints.
- Clear silhouettes and depth at normal mobile zoom.
- A satisfying final break/reveal.
- Readable HP and progress presentation.
- Sound appropriate to sustained idle play.

Keep effects restrained during repeated mining. Do not shake the camera or obscure the screen on every hit for a five-hour excavation.

Preserve the existing readable UI font, portrait presentation, and world art style.

## 5. Required destruction times

These targets apply to the final obstruction itself, not to total mine completion time.

Measure elapsed gameplay time from the first productive strike until the obstruction reaches zero HP. Include normal mining interruptions, carrying, travel, and unloading during this interval. Report any final outstanding-delivery time separately.

| Mine | Target time with the reference full crew | Seconds |
| --- | --- | --- |
| 3 | Approximately 10 minutes | 600 |
| 4 | Approximately 15 minutes | 900 |
| 5 | Approximately 30 minutes | 1,800 |
| 6 and every later mine | Approximately 5–6 hours | 18,000–21,600 |

Use a default tuning target of 5.5 hours, or 19,800 seconds, for Mine 6 onward unless a configured value within the requested 5–6-hour range is justified.

The jump from 30 minutes in Mine 5 to 5–6 hours in Mine 6 is intentional. Do not replace it with a gentler progression of 45, 60, or 90 minutes.

Do not make later final obstructions take increasingly longer than the requested range by blindly multiplying HP every mine. Their HP should scale with the intended progression of the reference team while preserving the requested duration band.

For Mines 3–5, use approximately ±10% as an initial verification tolerance. For Mine 6 onward, the calibrated reference case should remain within the 5–6-hour band.

## 6. Define the reference full crew honestly

Read the actual worker limits and upgrade system from the project. Do not assume a fixed crew size.

For each calibrated mine, define a reproducible reference profile:
- The maximum normally available mining-worker count at that mine, fully hired.
- Realistic local upgrade ranks obtainable before the final phase.
- Equipment and cards normally obtainable by that progression point.
- The guaranteed blacksmith starter item included where appropriate.
- No paid temporary multipliers, debug bonuses, or manual player damage.
- The actual movement, carrying, unloading, and work-position rules.

“Full crew” means the full available worker population, not an account with every legendary item and card at maximum rank.

Write down the exact reference state used for each benchmark. Use continuous legitimate progression or explicit reproducible fixtures, not unexplained hand-picked stats.

The full crew must be able to contribute effectively. Provide enough accessible work positions around the giant target and a usable approach area. Inspect the existing per-target worker cap; a cap intended for small rocks must not leave most of the team waiting beside the final target.

If additional work positions are needed, implement them specifically for this large-target class. Keep ordinary-object behavior intact.

Respect the real logistics loop: cats carrying resources must still be able to leave, unload, and return. Count actual productive time, not an imaginary state in which every worker attacks continuously despite full bags.

Stronger equipment, better cards, further upgrades, or active manual mining should genuinely reduce completion time. Fewer workers or weaker development should take longer.

## 7. Tune static HP; preserve normal blocks

Use the measured effective damage output of the reference team to estimate final-obstruction HP:

Initial final-obstruction HP = reference effective damage per second × target duration.

This is a starting estimate. Verify it against the actual simulation, including resource-release rules, hauling, critical hits if implemented, and changing worker availability.

For a deterministic benchmark, keep the declared loadout and upgrade ranks fixed during the excavation unless the benchmark explicitly tests an upgrade-purchase policy. Also check a realistic player case that buys additional upgrades from earned income.

If resource release or interruptions depend on the chosen HP, iterate the simulation until the reference time fits. Do not rely solely on a raw DPS formula.

Store the resulting HP in the final-obstruction configuration or a deterministic per-mine progression model. Do not derive it from the actual player's current strength when entering the chamber.

Once an obstruction is spawned, its maximum HP stays fixed for that run. Upgrading a pickaxe or card must not secretly increase the target's HP to cancel the benefit.

Hard requirements:
- Do not lower or increase ordinary-block HP to normalize the whole mine.
- Do not redistribute the giant target's HP into ordinary blocks.
- Do not reduce earlier block counts or remove normal content to compensate for the long final phase.
- Do not nerf existing cards, equipment, or global attack rates to force the target time.
- Do not replace the HP system with a hidden fixed countdown.
- Do not change the live game's timescale to make the excavation take longer.
- Do not let automatic balance-smoothing tools classify these intended final targets as errors and flatten them.

Use a separate final-obstruction category and explicit balance metadata so future balancing respects this design.

Preserve ordinary-block definitions and verify representative runtime HP values before and after the change.

## 8. Preserve income and reward clarity during long excavations

Use the existing partial-mining reward and cargo systems so the long phase can still produce visible work and income.

Budget the final target's rewards separately from its very large HP. Do not assume that multiplying HP must multiply every reward by the same factor and accidentally trivialize later mines.

Define a finite total reward and release it according to real mining progress through the existing collection/delivery rules. Do not repeatedly award the whole reward on stage changes or reloads.

Keep milestone presentations and ordinary partial rewards consistent. The sum of intermediate releases and the final remainder must equal the configured total.

Aggregate visual drops when necessary so a multi-hour excavation does not leave thousands of independent objects on the ground. Preserve actual resource values and carrying rules.

Use the target's HP/progress display to communicate continued progress. An optional remaining-time estimate must be based on observed effective throughput, update when conditions change, and be labeled as an estimate. It must not control the object's destruction.

Do not add an artificial timer gate after the final HP reaches zero.

## 9. Save progress and support offline excavation

The long phase must continue through the game's offline progression rules; it must not require the player to leave the phone running for five or six hours.

Extend the existing offline implementation to include the giant target and its normal mining/logistics behavior. If the project lacks an applicable offline path, implement a focused compatible solution for this phase using the same gameplay calculations.

Respect any existing offline cap and document it. If no offline cap exists, provide a configurable default large enough to cover a six-hour absence. Do not invent automatic purchases, equipment changes, or automatic transitions to later mines.

Save:
- Target identity and layout/configuration version.
- Initial maximum HP and remaining HP.
- Damage-stage state.
- Resources already released, carried, and delivered.
- Completion and final-reward receipts.
- Relevant offline timestamp and worker state.

On restart, do not respawn a pristine target, recalculate its maximum HP from the player's new strength, or replay already granted rewards.

Offline and online progression must not award the same elapsed time twice. Handle short absences, long absences, zero/negative elapsed time, and the existing cap consistently.

When offline work destroys the target, resolve the appropriate rewards and cargo through the existing rules and return to a completed or ready-to-complete mine. Do not automatically start the next mine unless that is already an explicitly supported game rule.

Show a concise return summary and the correct final visual state. Avoid replaying hours of particles and intermediate popups.

Use the project's existing large-number representation and safe arithmetic for HP and cumulative rewards. Avoid overflow, negative HP, and loss of meaningful damage at high values.

## 10. Integration and existing saves

Keep the two commerce destinations unchanged:
- Store stays on the left with its exact current icon and currency-purchase function.
- Sell Goods stays on the right with the product-filled crate icon and factory-product sales function.

Preserve the full card collection, current local reset rules, persistent equipment, and existing ordinary mine content.

Use reusable final-target definitions and placement rules. Do not manually rewrite or open all 144 level files.

For already started mines, preserve their established state unless a safe migration is available. Never reset partial progress or add a new obstacle behind an already recorded completion. Use the new final targets for newly started eligible mines when necessary.

Existing completed Mine 2 accounts retain Equipment access and receive Bruno's service state once. Do not duplicate the starter item or force another rescue tutorial.

## 11. Focused verification

Keep the sample to approximately 25 distinct levels and never more than 30. Include Mines 1–6 and representative later mines.

Do not wait five real hours for a benchmark. Use accelerated or headless simulation that reuses actual gameplay calculations. Validate the important behaviors in Unity gameplay, and distinguish simulation results from hands-on verification.

Check:
- Mine 1 rescue remains intact.
- Mine 2 contains a reachable blacksmith rescue.
- Equipment unlocks after Mine 2 completion and is usable before entering Mine 3.
- The starter item is granted once and the old Mine 3 path does not duplicate it.
- Mines 1–2 have no new giant-duration final target.
- Mines 3, 4, and 5 meet the 10/15/30-minute reference targets.
- Sampled Mine 6+ targets meet the 5–6-hour reference range.
- The full reference crew can contribute without a small-target concurrency cap.
- Mining, hauling, unloading, and return routes function.
- Better player stats genuinely accelerate clearing.
- Ordinary-block HP is unchanged.
- Partial saves and offline sessions preserve HP, stage progress, and rewards.
- Target destruction, pending cargo, and mine completion occur in the correct order.
- The large visual target and accumulated drops remain performant on the target mobile layout.

For each measured target, report:
- Mine number.
- Worker count and reference upgrades.
- Equipment and card assumptions.
- Configured maximum HP.
- Effective damage throughput.
- Target duration and measured duration.
- Online/offline method and relevant limitations.

Compare a weaker crew, the reference full crew, and an improved crew on a few representative targets. This verifies real progression benefits without expanding into a full-campaign audit.

## 12. Deliverables and acceptance

Deliver the implemented code, configuration, art, animation, UI, and save changes.

Provide actual screenshots of Bruno's rescue, Equipment unlocking after Mine 2, and the final formations for Mines 3, 4, and 5. Show at least one later formation and intermediate damage states.

Include the measured timing table, a concise explanation of HP calibration, the locations of the tuning parameters, and the sampled ordinary-HP comparison.

State which checks were actually run. Do not fabricate measurements, present mockups as implemented scenes, or claim all 144 levels were verified.

The result must deliver:
1. A meaningful second rescue that opens Equipment immediately after Mine 2.
2. Beautiful giant final excavations starting in Mine 3.
3. The explicitly requested 10-minute, 15-minute, 30-minute, then 5–6-hour clearing rhythm for the defined full-crew reference.
4. Unchanged ordinary-block HP and genuine benefits from player upgrades.
5. Reliable progress across long sessions, offline play, and save/load.
