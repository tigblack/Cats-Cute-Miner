# Cat Miners (CatOutpostV2, balance profile outpost-2.1-b4): what a new player gets in the first 30 minutes

ROOT = `/mnt/claude-data/sargsyan-tinote/cats-cute-miner`. I changed no project files. My only scratch file is `ROOT/Temp/playable/first30.py`; it pulls the per-mine figures out of the audit JSONs.

**Sources**

| Tag | Absolute path | What I used |
|---|---|---|
| [TZ] | `ROOT/Docs/TZ/Cat_Miners_Auto_Incremental_TZ_v2.1.md` | §12.6 pacing targets L773–797; formulas L605–617 and L710–736; store and skips L510, L525–531; tutorial §15 |
| [AUD] | `ROOT/Docs/Rework/BALANCE_AUDIT.md` | player models L95–101; stage medians L188–197; mine 1 L227; time ≈ cargo × distance (correlation 0.84) L228; limits L337–338 |
| [KM] | `ROOT/Docs/Reports/balance-audit/key-metrics.txt` | L22–23 |
| [RUN] | `ROOT/Docs/Reports/balance-audit/after/campaign-{free,active,idle}-seed1.json` | per-mine purchase logs, 5-second gold traces, how cats spend their time |
| [CSV] | `ROOT/Docs/Reports/balance-audit/per-mine-seed1.csv` | per-mine summary |
| [SAT] | `ROOT/Docs/Reports/balance-audit/after/saturation.json` | mine time vs number of cats |
| [TUNE] | `ROOT/Docs/Reports/balance-audit/tune-summary.json` | 54 measured profile variants |
| [BAL] | `ROOT/Assets/Game/Resources/Outpost/outpost_balance.json` | balance numbers |
| [RWD] | `ROOT/Assets/Game/Resources/Outpost/stage_rewards.json` | equipment rewards |
| [STORE] | `ROOT/Assets/Game/Resources/Outpost/store_catalog.json` | store and starter items |
| [CARDS] | `ROOT/Assets/Game/Resources/Outpost/card_rules.json` | card rules |
| [OVR] | `ROOT/Assets/Game/Resources/Outpost/level_overrides.json` | mine 1 arena |
| [TOPO] | `ROOT/Assets/Game/Resources/Outpost/mine_topology.json` | mine sizes |
| [SRC] | `ROOT/Assets/Game/Data/Source/json-data.json` | `Zones` grids, `Gacha` chest contents |
| [THEME] | `ROOT/Assets/Game/Resources/Visual/environment_themes.json` | L3–7 |
| [TUT] | `ROOT/Assets/Game/Scripts/Runtime/Outpost/OutpostTutorial.cs` | tutorial steps and timings |
| [CAM] | `ROOT/Assets/Game/Scripts/Runtime/CameraRig.cs` | L39 |
| [CTRL] | `ROOT/Assets/Game/Scripts/Runtime/Outpost/OutpostController.cs` L65–74, `.Loop.cs` L77, `.Commands.cs` L138–162 | sim speed, mine transition |
| [MAP] | `ROOT/Docs/Rework/CARD_EFFECT_MAPPING.md` | card unlock mine per card, chest sources L131–138 |

**What the numbers can and cannot tell you**
- **Audit seconds equal real seconds.** The main profile advances the simulation at 1× wall clock ([CTRL] Loop.cs:77).
- **Windows during a mine cost no time.** Base, Cards and chest windows don't pause the simulation (TZ §14). Only the gap from "mine finished" to the next mine start is truly lost, and the audit does not measure it. I estimated it at 30–45 s per mine.
- **Three old reports don't apply.** `bot-zones-1-10.md`, `bot-actions-zones-1-10.md` (7 Oct) and `economy-tables.md` (5 Oct) describe the old merge mode (forge, merges, mine automation, "aa" number suffixes). I did not use them.
- **"Engaged player" is my composite.** It uses the audit's `active` model times (buys within 5 s, taps 1/s) plus my estimated transition time. The reference `free` model (checks the Base every 30 s, never taps) is about 0.5 min slower per mine.

## 1. Timeline, 0–30 min (engaged player)

| Clock | Mine (layout, size) | What the player does | New thing seen | Purchase cadence |
|---|---|---|---|---|
| 0:00–0:06 | 1 (winding-galleries-001, 29×21; 56 targets = 40 rocks + 16 rubble blocks; no gates; Warm theme) | Watches; the 2 cats walk out by themselves | Hint "The cats already work on their own" stays 5.5 s ([TUT] L39, L340–344). Camera shows 10 of 29 columns ([CAM] L39) | 0 gold |
| 0:06–0:14 | 1 | Taps a rock: 1.25 damage per tap, so a 20-HP rock takes 16 taps (TZ L204) | Paw/frame "Tap a rock", shown up to 8 visible seconds ([TUT] L41, L345–353) | First delivery at 8.75 s (active) / 10.5 s (free); wallet +40, so Pickaxes (20) is already affordable ([RUN] trace; [KM] L22) |
| 0:14–0:30 | 1 | Watches 3 one-shot hints in a row: bag → gold → "cats pick targets", 5.5 s each ([TUT] L355–367) | Tutorial card ca001 message at first delivery | Hiring the 3rd cat (50 gold) is affordable from about 0:23; active model buys it at 0:23 |
| 0:30–0:45 | 1 | Opens Base, buys Pickaxes, then hires (prompts at [TUT] L368–382) | "Team damage went up: 5 → 6.1" (4.5 s) | Active model: purchases at 0:33, 0:48, 0:53 |
| 0:45–4:27 | 1 | Buys a level every ~12 s; taps | Nothing new for ~3.7 min, no hints. Team reaches 4 cats at 1:53 and 5 at 4:13 | 25 purchases in 24 buy moments; average gap 11.6 s, longest 25 s. Median purchase costs 10 s of current income |
| 4:27–4:39 | 1 | Waits while the last cargo is delivered (13 s drain) | — | Nothing useful to buy |
| 4:39–5:19 | Transition | Exit chest (duplicate ca001 + 17 elixir, short message) → Results: 56/56 cleared, 1,258 gold, Common Pickaxe + 10 parts → tutorial "equip it in Equipment" → equip → "upgrades will reset" hint (5.5 s) → Next (0.5 s fade) | First item and first parts | Simulation stopped ([TUT] L315–331) |
| 5:19–9:15 | 2 (ore-archipelago-002, 27×12; 29 targets) | Rebuys everything from 0 gold and 2 cats | "Kept bonus" hint (damage 5.4 instead of 5). First deposit (Amethyst, 442 HP, takes 116 s) gives a chest. Exit chest gives the first NEW card (ca002) and opens the first chest-cards window | 21 purchases; average gap 12.4 s, longest 48 s; first purchase at 48 s |
| 9:15–9:45 | Transition | Results → equip Common Hat → Cards screen (2 cards, 91 elixir) | — | — |
| 9:45–14:34 | 3 (braided-tunnels-003, 25×23; 33 targets) | Same loop | First chest rock (wooden; ca003 NEW, window opens mid-mine); 2 deposits; exit gives ca014 NEW. Pickaxes level 10 doubles damage (×2 milestone) at about 13:53 (active; free only in mine 5) | 34 purchases; average gap 10.7 s, longest 53 s |
| 14:34–15:04 | Transition | Equip Common Backpack (+1 bag slot) | — | — |
| 15:04–19:11 | 4 (forked-veins-004, 29×22; 45 targets) | Same loop; first Loot Value levels | **Crystal theme** ([THEME] L5–6); chest rock ca004 NEW; exit ca026 NEW (first Rare card) | 40 purchases; average gap 9.5 s, longest 28 s |
| 19:11–19:41 | Transition | Equip Common Boots: all 4 slots now filled | — | — |
| 19:41–25:44 | 5 (winding-galleries-005, 27×33 deep; 61 targets + 1 gate) | Same loop; cats walk longer (cargo-weighted distance 27 cells vs 13–18 in mines 1–4) | **First gate** at about 23:20 (348 HP, 2 cells; "Sector opened" toast and camera pan). 13 chests; ca005, ca015, ca029 NEW. First random item roll (70/24/5/1 %) | 45 purchases; average gap 11.3 s; gate wait 19 s |
| 25:44–26:14 | Transition | Equip / upgrade with parts / upgrade cards | — | — |
| 26:14–30:00 | 6 (ore-archipelago-006, 25×34; 76 targets + 1 gate) — about 70 % done at 30:00 | Same loop | ca016 NEW. Seed 1 gives the first Rare item (Rare hat) only when mine 6 ends, at about 31–32 min | Average gap 9.7 s |

Other player types over the same 30 minutes:
- **Free model:** mine ends at 5:10 / 10:02 / 15:58 / 20:57 / 27:33; at 30:00 it is 34 % into mine 6.
- **Idle model (checks every 5 min):** only 3 mines (10.3 + 7.5 + 9.9 min).

TZ targets (TZ L778–781) are met:
- First delivery 10.5 s.
- First affordable upgrade about 10–15 s.
- First extra cat at 23–45 s.

The exception is mine 1 for the free model: 5.17 min against the 3–5 min target ([AUD] L227).

## 2. Dead time in the 30-minute window (engaged player)

| # | Moment | Seconds lost in 30 min | Evidence |
|---|---|---|---|
| 1 | Between mines: exit chest window → Results → Equipment (manual equip, part upgrades) → Cards → Next. Simulation is stopped | **150–225 s** (5 × 30–45 s; my estimate, not measured) | [CTRL] Commands.cs:138–162; Results UI `OutpostHud.Results.cs:150–157` |
| 2 | Each mine starts at 0 gold and 2 cats: nothing to do until the first delivery, then a stretch with nothing affordable | ~90 s until first deliveries (8.8 / 15.9 / 15.9 / 18.3 / 15.5 / 17.5 s); longest no-buy stretch per mine 11–27 s, ≈115 s total | [RUN] `FirstDeliverySec`, `MaxUnaffordableSec` |
| 3 | Mine-end drain: last rock gone, cats still carrying cargo | 69 s (13 / 8 / 20 / 9 / 19 s) | [RUN] `DrainSec` |
| 4 | Gate wait while the next sector is closed (mines 5–6) | 26 s (19 + 7) | [RUN] `GateWaitSec` |
| 5 | Last rock of a sector (tail) | ~20 s | [RUN] `SectorTailSec` |
| 6 | Tutorial gating: Pickaxes is affordable at ~10–15 s, but the prompt comes at ~30 s (4 hints × 5.5 s + up to 8 s strike prompt). After the hire, no hints for ~4 min | 15–20 s | [TUT] L37–43, L340–383 |
| 7 | Walking and empty screen: cats spend 55–67 % of their time walking and only 11–31 % mining. Camera shows 10 of 25–29 columns, so cats are often off-screen. Deep mines 5–6 reach 41–44 cells from base | continuous; it is the main pacing factor | [RUN] `WorkerSec`; [CAM] L39 |
| 8 | Taps pay off weakly: taps deal 25–54 % of the damage but cut mine time only 7–12 % (logistics is the bottleneck). Tapped cargo falls to the ground, raising "walking to cargo" from 3 % to 14–28 % | — (it feels pointless) | [RUN] `StrikeDamageShare`; active vs free |
| 9 | Same menu loop: base levels reset every mine, so the player rebuys ~200 levels in 30 min (25 + 21 + 34 + 40 + 45 + 36). Six near-identical 4–6 min loops; Team goes 2→5 cats and Pickaxes 0→7–10 every mine | — | [RUN] `PurchaseLog` |
| 10 | Slow single targets that the cats crowd around (not on the critical path) | — | Deposits of 442–659 HP take 59–116 s; rubble 56–160 HP takes 31–85 s ([RUN] `SlowTargets`) |

Rows 1–6 add up to about **6–8 minutes of 30 (20–27 %)** with no meaningful action. Buying is not the problem: something is affordable at least every 11–53 s, typically every 10–12 s. The drag comes from sameness, walking, and the screens between mines.

## 3. What fits in 30 minutes and what does not

**Reached:**
- Mines 1–5 complete, mine 6 about 35–70 % done.
- Warm theme (mines 1–3) and Crystal theme from ~15 min.
- 4 guaranteed Common items (pickaxe, hat, backpack, boots — [RWD] L6–9) plus one random roll at mine 5.
- 9–10 cards owned with about 15 card levels; elixir rises to ~650–1,000.
- The first chest windows, from mine 2 onwards.
- The first gate and second sector at ~23–25 min.
- The Pickaxes ×2 milestone (mine 3 for an active player, mine 5 for free).
- Gold per mine of 1.3k → 15.8k; a team of 5–7 cats.

**Not reached:**

| Feature | Where it is | When it arrives (play time) |
|---|---|---|
| Copper theme | Mine 7 ([THEME] L7) | ~31–36 min |
| Bell Collar card (ca050, +tap damage) | Mine 7 exit chest ([SRC] Gacha) | ~31–36 min |
| Treasure Map card (ca062, Majestic) unlock | Mine 7 | ~31–36 min |
| Guaranteed Rare Pickaxe | Mine 10 ([RWD] L10) | ~51–55 min sim, ~1 h with menus |
| 2-gate / 3-sector mine | Mine 10 | ~1 h |
| Last scripted exit chest (scg_m010_exit) | Mine 10 | ~1 h |
| Crusher exit chests | Mine 11+ | ~1 h |
| First random Epic+ item | Mine 16 | 1.6 h |
| 3 gates and Loot Value ×2 milestone (level 10) | Mine 20 | 2.1 h |
| Millions of gold | Mine 20 | 2.1 h |
| Guaranteed Epic Pickaxe | Mine 25 | 2.7 h |
| Guaranteed Legendary Pickaxe | Mine 50 | 5.9 h |
| Pickaxes level 20 | Mine 66 | 7.8 h |

- **Card effects stay small in this window.** Cards speed early mines up by only 4–6 % ([TUNE] t0 vs t0-nocards).
- **Gems are effectively unavailable.** The game starts with 0 gems ([STORE] L4). Hard-currency rocks give nothing ([CardCatalog.cs](/mnt/claude-data/sargsyan-tinote/cats-cute-miner/Assets/Game/Scripts/Core/Outpost/Cards/CardCatalog.cs) L136–143 map only chests and elixir). Gem packs only work through the test-mode purchase stub.
- **Store options in the window:**
  - The ad-for-a-Wooden-Chest offer. On mine 1 it gives only 1 card and 15–19 elixir (the zone multiplier is ×0.1).
  - The starter time skips: 3 × 10 min + 1 × 30 min ([STORE] L5, L11–12). A 10-minute skip clears 42–86 % of mines 1–3, or 45–100 % after two minutes of purchases (TZ L529).

## 4. Levers for a denser first 30 minutes

How I estimated the effects:
- **Upgrade cost.** Mine time grows with cost to the power of about 0.3–0.4 ([TUNE] CostZoneStep variants t0–t4 on mines 2–10: 4.64 → 4.78 / 4.99 / 5.17 / 6.07 min).
- **Walking distance.** A 2.25× longer cargo path made mine 1 about 1.5× longer: 256.8 s → 6.4 min ([TZ] L781; [KM] L23). That is roughly time ∝ distance^0.5. The audit also finds time ≈ cargo × distance (correlation 0.84, [AUD] L228).
- **Cats.** With levels held fixed, mine 1 takes 335 / 238 / 182 / 153 / 144 s with 2 / 3 / 4 / 5 / 6 cats ([SAT]); mine 10 takes 602 / 413 / 324 / 266 / 222 s.
- **Rock HP scaling** with depth hardly matters on mines 2–10 (−1 %, [TUNE] h3-c0).

| Lever | Config key (file:line) | Current | Example change | Estimated effect on minutes per mine (mines 1–6) |
|---|---|---|---|---|
| Simulation speed | `m_Speed` ([CTRL] OutpostController.cs:65–74, Loop.cs:77); debug profile only, values 1/5/20/100 | 1 | 1.5 (needs a playable-only switch) | ×0.67 on everything, animation included |
| Starting cats | `StartWorkers` ([BAL] L7; TeamStats.cs:80) | 2 | 3 / 4 | −15–20 % / −25–30 %, from average cats 3.7 → 4.7 / 5.7 on the [SAT] curve; also removes the first hire wait |
| Gold per portion | `CoinBase` ([BAL] L28; TeamStats.cs:114) | 5 | 10 | Same as halving all upgrade costs: −19–24 %; numbers also look bigger |
| Upgrade costs | `Branches[].BaseCost` / `Growth` ([BAL] L14–19) | Team 50 × 1.9; Pickaxes 20 × 1.18; Boots 25 × 1.3; Bags 25 × 1.32; Unload 20 × 1.28; Value 30 × 1.24 | BaseCost ×0.5; Team Growth 1.9 → 1.5 | −19–24 %; cheaper Team adds about −10 % via faster hiring (estimate) |
| Cost growth with depth | `CostZoneStep` ([BAL] L12) | 1/10 (costs ×1.1–1.5 on mines 2–6) | 0 | −3 to −11 %; measured −10 % on the mines 2–10 median |
| Walk speed | `BaseMoveSpeed`, `MoveSpeedStep` ([BAL] L23) | 2.5 cells/s; +8 %/level | 3.75 | ≈ −18–20 % (walking is ~60 % of cat time) |
| Bag size | `BaseCapacity`, `CapacityStep` ([BAL] L24) | 4; +1/level | 6–8 | −12–25 % (fewer trips) |
| Rock HP (cargo and gold scale with it) | `HpBase`, `CargoHpDivisor` ([BAL] L27–28) | 20; cargo = HP/10 | HpBase 10 | −30–35 % alone; −45–50 % with CoinBase 10 (keeps gold per mine) |
| Cat hit rate | `HitIntervalTicks` ([BAL] L4) | 1 hit/s | 2 hits/s | −5–15 % (mining is 11–31 % of cat time) |
| Tap strength | `TapDamageFactor` ([BAL] L22) | 1/4 (1.25 damage) | 1 | −5–10 % for a tapper; mostly makes taps feel useful |
| Unloading | `BaseUnloadRate` ([BAL] L25) | 2 per second | 4 | −5–7 % |
| Pickaxes ×2 milestone | `Branches[Pickaxes].Milestones` ([BAL] L15) | [10, 20] | [5, 10] | Not a pace lever: brings a "×2" moment to minutes 2–5 |
| Mine size / route | [OVR] L5–7 (mine 1: 36 added targets); [TOPO] (generated by `Tools/Topology/build_topology.py`) | Mine 1: 56 targets, 306 cargo units | Original 20 targets, or a shorter path | Time ∝ cargo × distance; mine 1 could drop to ~2–3 min. Needs topology regeneration |
| Which mines / item rewards | Mine list; [RWD] L5–14 | 1–6 fit; Rare at 10, Epic at 25, Legendary at 50; random rarity 70/24/5/1 | Put Rare/Epic/Legendary at mines 3 / 6 / 9 | Content density, not pace |
| Time skips | [STORE] L5 | 3 × 10 min + 1 × 30 min | Use as a scripted boost | A 10-min skip clears 45–100 % of mines 1–3 |

**How many mines fit in 30 minutes** (active model times, 30–40 s per transition; a speed-up factor applies to time inside mines only):

| Speed-up factor | Mines finished in 30 min |
|---|---|
| ×1 (today) | 5 |
| ×0.8 | 7 |
| ×0.67 | 7 (mine 8 almost done) |
| ×0.55 | 9 |
| ×0.45 | 11 |

- **Reaching mine 10 needs about 2× compression.** Mine 10 brings the Rare pickaxe, 2 gates and the Copper theme. One combination that should get there: speed 1.5 × StartWorkers 3 × CoinBase 10, roughly 0.67 × 0.82 × 0.8 ≈ 0.44.
- **The estimates simply multiply.** Each one was measured on its own, so a combined profile should be checked with `Audit_Tune` (JSON patches in `ROOT/Docs/Reports/balance-audit/tune-variants.json`). The simulation speed can't be tested there because it lives in the controller, not the balance file.
- **The main game is not at risk.** Each mine snapshots its balance (`Run.Balance`), so a separate playable profile leaves it untouched.