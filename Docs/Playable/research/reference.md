# Kung Fight playable: design teardown and how it maps to Cat Miners

I only read and grepped the reference files. Nothing from the reference was executed, and no project files were changed. All scratch output is in `/mnt/claude-data/sargsyan-tinote/cats-cute-miner/Temp/playable/`.

## 0. Sources and line references

- **Original code:** `/mnt/claude-data/sargsyan-tinote/cats-cute-miner/Temp/ref_kungfight/app.js`. three.js plus the GLTF and meshopt loaders take lines 1 to 3833 (up to column 117477). The game's data tables start at `const Up=` on line 3833, column 117477: the asset table `Fs` (data URLs) at column 117594 and the global state `M` (`energy:75`) at column 1442749. Shaders and `zi` (the combo table) are on lines 3834 to 4062, with `zi` at line 3982, column 4913. Almost all game logic is on line 4063 (87 KB).
- **Formatted copy:** `/mnt/claude-data/sargsyan-tinote/cats-cute-miner/Temp/playable/kf/app_fmt.js`, 3280 lines, with base64 blobs replaced by `DATAURL`/`B64`. References written `fmt:N` point here. I built it with `Temp/playable/tools/fmt.py`.
- **CSS:** extracted from the `payload.bin` header into `/mnt/claude-data/sargsyan-tinote/cats-cute-miner/Temp/playable/kf/style_fmt.css`. References written `css:N` point here.
- **Payload layout:** a 4-byte little-endian header length, then JSON `{css (83 KB, including 2 woff2 fonts), js (2.13 MB), glb:{name:[off,len]}}`, then 8 GLB files totalling 2.77 MB: boss, girl, punk, player, props, trainer_bag, trainer_treadmill, and zone1 (1.36 MB).
- **Store links:** `play.google.com/...id=com.multicastgames.kungfight` and `apps.apple.com/app/id6786332690` (fmt:2825).

## 1. Quest and stage sequence

The quest list is at fmt:2993-3001 (line 4063, column 74057). The quest controller is class `Fg` (fmt:2980-3177).

| # | Panel title / step (`0/goal`) | Icon | Counts when | Speaker line (shown 3.6 s) | On complete |
|---|---|---|---|---|---|
| 0 | **Street Thugs** / "Defeat punks 0/3" | punk_idle | a `punk` is killed (any spawner) | Rick: "Punks took over our street! Show them some kung fu!" | after 0.6 s: "QUEST COMPLETE!" toast plus `win_pair` sound; Q1 starts 1.2 s later |
| 1 | **Rick's Training** / "Train on the Punching Bag 0/3" | st_bag | each `spin` event (a slot-machine spin) | Rick: "Not bad, kid! Now hit the punching bag to get stronger." | completes **only when the training window is closed** with progress ≥ 3 (fmt:3044-3046). Then after 0.9 s the **NEW COMBO** "Sweep Dreams" (common, slot 1); Q2 starts 1 s after claiming |
| 2 | **Girl Gang** / "Defeat the gang 0/4" | girl_idle | a `girl` is killed | Kung (`kung_power`): "A new combo! Try it on that gang over there!" | NEW COMBO "Punchinator" (uncommon, legs, slot 2), then Q3 |
| 3 | **Prison Boss** / "Defeat the Boss 0/1" | punk_idle | the boss is killed | Rick (`rick_scream`): "The Prison Boss is out! Take him down, kid!" | The boss spawns at quest start, with a 0.35 camera shake. On the kill: slow-mo ×0.3 for 0.45 s, shake 0.5, glow burst, **"VICTORY!"** banner. The end card opens 2.2 s later (fmt:3089-3096) |

- **Start:** `start()` fills every active spawner, then calls `startQuest(0)` 0.6 s later (fmt:3057-3061).
- **Timers:** `$e`/`Vt` run on game time (`zp(q)`, fmt:302-320, 3274). They freeze while `M.paused` is set (the reward popup and the interstitial) and slow down under `slowMo`.
- **Interstitial variant:** the URL flag `?inter` (fmt:2979, `mg`) inserts a "QUEST COMPLETE!" interstitial 1.1 s after quests 0 to 2. It shows the app icon, "KUNG FIGHT", ★★★★★, a per-quest line from `gg[]`, a **DOWNLOAD** button, and **CONTINUE ▶**, which unlocks after 600 ms (fmt:2928-2949). The three `gg` lines:
  - "Street cleared! Thousands of fights await in the full game."
  - "You got stronger! Train every stat and unlock 30+ combos."
  - "Gang defeated! Get the game and become a kung fu legend."
- **Combo slots:** there are 5 (fmt:2147). Slot 0 is Single Punch. Slots 1 and 2 are granted by quests. Slots 3 and 4 stay permanently empty with a lock icon as a teaser.

### Spawners (fmt:3242-3247)

The spawner's own HP value overrides the enemy type's HP.

| id | Content | Max alive | HP | Respawn |
|---|---|---|---|---|
| s1, s2 | punk | 2 each | 45 | 15 s after the whole group dies |
| s10 | punk | 2 | 52 | 15 s |
| s4 | punk | 3 | 35 | 15 s |
| s3 | girl | 4 | 40 | 15 s |
| boss | boss | 1 | 1100 | never; inactive until Q3 |
| crates c6, c2, c3, c5 | crate | 1 each | 27, 45, 45, 60 | 20 s |
| c1 | energy vending machine | 1 | 31 | 20 s |
| c4 | energy vending machine | 1 | 62 | 20 s |
| c7 | HP-regen vending machine | 1 | 58 | 20 s |

### Distances and enemy types

- **Distances:** the player starts at (-14.41, -97.75) and runs at 6 m/s. Start to s1 is 20.7 m, s1 to s2 is 21.9 m, s2 to s3 is about 44.7 m, and s3 to the boss is about 55 m. The punching bag's position comes from its GLB, so it is unknown.
- **Enemy types (fmt:3220-3238):**
  - punk: damage 7, 10 energy, speed 4, range 3
  - girl: damage 5 with a two-jab chain, 9 energy, speed 4.5
  - boss: damage 6 ×1.3, 60 energy, speed 3.6, range 4.5
- **Aggro:** enemies aggro at 5 m. They give up beyond 22 m from their spawner (boss: 32 m), walk back, and fully heal (fmt:1836-1851).

### Boss (`rA`, fmt:1281-1293; brain `Tm`, fmt:1294-1569)

- **Leap:** cooldown 8 s, first use at 2 s, 7–20 m, radius 3.5, damage ×1.8, knockback 3.5.
- **Charge:** cooldown 9 s, first at 4 s, 5–16 m, width 2.6, speed 26, damage ×1.6.
- **Meteors:** 4 per throw, cooldown 6 s, radius 2.4, damage ×1.2.
- **Blast:** fires after the boss takes 10 hits (shown as diamond pips). 2 s cast, radius 5 growing by 1.5 per blast up to 10, damage ×3. **It heals the boss 3% of max HP (about 33).**
- **Rage:** triggers at 22.5, 37.5 and 60 s of engagement (`averageFightTime:30` × [0.75, 1.25, 2]). Each level adds +30% damage, +8% speed, −15% cooldowns and +7% scale, and shows a "BOSS RAGE I/II/III" banner for 1.4 s.
- Every attack has a ground telegraph decal that fills up before it lands.

## 2. Core loop and economy

**Loop:** fight enemies and break props → get energy → spin the training stations (each spin costs energy and raises a permanent stat) → stronger hits → the quest unlocks a combo → tougher wave → boss.

- **Starting energy is 75** (state `M`, fmt:42). That is exactly 3 bag spins (25 each), and also exactly the Treadmill unlock price. So the training quest can never stall on energy unless the player spends it on the Treadmill first.
- **Energy sources:**
  - punk 10, girl 9, boss 60 (it is shown in the reward preview, but the game ends on the kill)
  - crate 8, energy vending 12, HP-regen vending 16
  - The amount appears above each HP bar as a loot preview (`trackUnit`, fmt:2282-2308). The boss bar also shows an uncommon combo icon.
- **Stations (`vo`, fmt:2973-2978):**
  - Punching Bag: "Increase Hands Damage", +1 Hands per spin, 25 energy, free to unlock.
  - Treadmill: "Increase Max HP", +8 HP per spin, 25 energy, **unlock 75**. The Treadmill is optional and not part of any quest.
- **Spin window (`rg`, fmt:2618-2824):**
  - 3 reels, 26 chasing light bulbs, a SPIN button with its cost, and an odds table showing x1 80%, x2 15%, x10 5%.
  - Real odds: 5% x10, then 16% of the rest for x2 (fmt:2712).
  - **The results are rigged:** every station's 2nd spin is forced to PAIR x2 and its 3rd to JACKPOT x10 (fmt:2713). Three bag spins therefore give a typical **Hands = 1 + 2 + 10 = 13**.
- **Damage formula:** `(combo.base + Hands×6) × strike weight`, with an 8% crit chance at ×1.5 (fmt:1570, 1728).
  - Single Punch: base 22, two strikes at weight 0.5 each. That is **11 per strike at Hands 0 and 50 per strike at Hands 13**, a ×4.5 jump. Punks (35–52 HP) go from about 4–5 strikes to 1.
  - Sweep Dreams: base 60, cooldown 6 s. Punchinator: base 100, cooldown 7 s (fmt:944-956).
- **Locked-station popup** (`makeLockPopup`/`unlockStation`, fmt:3101-3130; update at fmt:3150-3157):
  - Appears only within 2.2 m of a locked trainer, anchored above it in the world.
  - Shows a lock icon with the name, a frame with the station icon, "Unlock this trainer and unleash your power!", "+[icon] HP", a fill bar "x/75⚡", and a greyed "75⚡" button.
  - Once you can afford it, the button gains a glow and a pulse.
  - Tapping without enough energy: toast **"Not enough energy! Defeat enemies"** (red, 1.8 s), a low-pitched click, and a bump animation.
  - On unlock: the ring turns white, a glow burst, "TREADMILL UNLOCKED!" in green.
  - The same toast appears when SPIN is tapped without enough energy (`noEnergy` event, fmt:3038).
- **Healing:**
  - The player has 100 HP and regenerates 0.5 HP/s.
  - Inside the campfire ring (radius 3.05) the player heals 12% of max HP per second. Enemies standing in the ring heal 50%/s, which stops players exploiting it (fmt:2521-2559).
- **No fail state:**
  - **Death sequence** (`ng`, fmt:2569-2616): vibration [30, 40, 60], slow-mo drops to 0 then eases back to 1 over 0.6 s, the camera zooms in 30%, and a red "DEFEAT" band appears.
  - **Respawn:** 1.2 s later the screen blacks out and the player respawns at the campfire with 1% HP and a get-up animation. All enemies reset to full HP and lose aggro. No end card is shown.
  - The end card's loss text, "Train harder. Come back stronger!", exists but `end(false)` is never called.

## 3. Onboarding

**Hand pointer.** All rules are on fmt:3158. Every rule depends on game state; none is on an idle timer. Rules are checked in this order each frame:

1. Q0 and the player has not moved yet, at least 1.2 s after start, on touch devices only: a **"drag" hand** loops over the joystick (1.6 s loop).
2. Q0, no attack yet, and an enemy within 7 m: a **"tap" hand** on combo slot 0 (0.9 s loop).
3. Q1, standing inside the unlocked bag's ring: a tap hand on the round **interact** button.
4. Inside the spin window, until that station's first spin: a tap hand on **SPIN** (fmt:2647, 2674).
5. Q2, slot 1 not yet used, an enemy within 7 m, and the slot off cooldown: a tap hand on **slot 1**.

The hand hides during the death sequence and while windows are open. `M.lastInput` is recorded but never read, so there is no idle re-prompt and no idle autoplay. An autoplay feature exists in code (`player.auto`, `autoDir`, a blinking cyan `.banner.auto` style) but is switched off with `const n=!1` (fmt:3159).

**Other guidance:**

- **Off-screen pointer:** a yellow arrow sits on the screen edge with the quest icon in a circle and points at `quest.target()` whenever the target is off-screen (fmt:2389-2398).
- **Low-HP hint:**
  - Fires once, below 35% HP. Rick says "You look beaten up! Rest by the campfire to heal." (3.2 s), and the pointer switches to the campfire with an HP icon.
  - Below 30% the player's HP bar turns red. At or below 10% a pulsing red vignette appears (fmt:2343-2346).
- **Desktop:** a key hint, "W A S D move · SPACE punch · E train", shows for the first 40 s while in Q0–Q1. The game is framed as a 19.5:9 phone with rounded corners (fmt:2102-2106; css:295-301).
- **First seconds:** a loading screen with a bar tracking GLB parsing → all shaders and effects are compiled before the loader fades (`qg`, fmt:3188-3210) → the loader fades over 0.45 s → `gameReady()` → the quest panel slides in at 0.6 s → the speaker bubble shows for 3.6 s → the download banner slides in at 0.8 s → the drag hand at 1.2 s. Two punks are about 20 m ahead, with a crate on the way at about 12 m.

## 4. Feedback effects

- **Hit pause:** on every player hit, 0.08 s with the player's animation at 10% speed and the enemy frozen for 0.04 s. Breaking a prop pauses 0.06 s. Each hit also vibrates 12 ms, randomises the `hit` pitch between 0.92 and 1.08, and spawns a star, glow and 8 sparks (14 on a crit) (fmt:1725-1732, 590-720).
- **Enemy reaction:** a white flash, squash and stretch (0.26 s), and recoil (0.08 s out, 0.18 s back). Normal enemies are launched (6 sideways, 4 up), then fade to smoke after 1.3 s (fmt:1789-1920).
- **Damage numbers** (css:86-91, fmt:2276, 2360-2372):
  - Normal: yellow, size 58u.
  - Crit: orange, 78u, with a small "CRIT" label.
  - Damage taken by the player: red, 46u.
  - Loot: amber, "+10⚡".
  - Animation: pops from scale 0.6 to 1.35 to 1.0, rises, and fades over the last 30% of its 0.85 s life (1.1 s for loot).
- **Energy collection** (`Nm`, fmt:1925-1950): a beam to the dead enemy, then a glowing orb flies to the player with a trail, an `absorb1-3` sound, then "+N⚡". Three icons fly to the energy counter 70 ms apart (650 ms arc). The counter bumps and its number counts up.
- **Camera:** shake (boss spawn 0.35, victory 0.5, boss attacks 0.2–0.7), a 4.5% field-of-view kick plus afterimages while dashing, slow-mo on victory and death. The `Hp` rig is at app.js line 3959, column 5585.
- **Spin window feedback** (fmt:2704-2792):
  - **Reel stops:** reels stop at 0.7, 1.0 and 1.3 s. When the first two reels match, the third waits an extra 1 s with a "tease" glow and sound.
  - **Results:** "+1", "PAIR x2" or "JACKPOT x10", with shake of 6, 14 or 26.
  - **Lights and flash:** the bulbs chase during the spin and blink after. Pairs and jackpots get a flash; jackpots add light rays for 2.8 s.
  - **Particles:** a fountain of 4, 8, or 24+12+8 icons. Then 3, 7 or 16 icons fly to the stat counter.
  - **3D view:** the character visibly trains in a scissored 3D preview, and the punching bag swings.
- **NEW COMBO popup** (`dg` and `ug`, fmt:2877-2914):
  - **Popup:** the game pauses. Two layers of rays counter-rotate; the title drops in; a rarity frame pops in with a glow; the name and rarity tag appear. "Tap to claim" becomes tappable and starts pulsing after **0.9 s**, as protection against accidental taps.
  - **After claiming:** the icon flies into the HUD slot (550 ms, overshoot easing) and the slot plays an unlock spin-in (0.8 s).
- **Other text:** toasts (fmt:2924), centre banners (VICTORY!, BOSS RAGE) with a scale-down entrance, and speaker bubbles that slide in from the left (fmt:2915).
- **Spawner timers:** empty spawners within 30 m show a ring countdown with "15s" (fmt:2189-2215).
- **Sounds** (`Fs.sfx`):
  - Combat: hit, hit_player, whiff1-4, dash, absorb1-3, pickup, click.
  - Upgrades and the slot machine: upgrade, spin_press, reel_spin, reel_stop1-3, tease.
  - Wins: win_small, win_pair, win_jackpot.
  - Boss: boss_roar, boss_leap, boss_slam, boss_charge, boss_blast_charge, boss_blast, boss_throw, boss_meteor.
  - Music: a looped `music` track at 0.25 volume.
  - The same sound never replays within 30 ms (fmt:2407-2458).

## 5. Monetization UI and ad-network handling

- **Persistent banner:** `.dl-banner` (fmt:2987-2990; css:262-273, 345).
  - Placement: bottom-left in landscape, bottom-centre in portrait, 560×132u, offset by the safe area.
  - Content: app icon, "KUNG FIGHT", ★★★★★, a pulsing green "DOWNLOAD" button, and a shine sweep every 3.2 s starting at 1.5 s. It slides in 0.8 s after load.
  - It is visible for the whole session, except while the training window is open (`body.slot-open .hud>*{visibility:hidden}`) and behind other windows. The whole banner is clickable.
- **End card** (`pg`, fmt:2950-2972):
  - Content: a dimmed background, rays, a 360u app icon with a yellow ring, a big "KUNG FIGHT", the text "You beat the boss! Your training has just begun.", the 3 earned combos, 2 locked combos, "30+ combos • 9 zones • bosses", and a pulsing **PLAY NOW**.
  - **The whole card is the CTA.** It plays win_jackpot and calls `window.gameEnd?.()`.
  - **The only trigger is boss victory + 2.2 s.** There is no time limit, no interaction count, no end card on death, and no automatic store redirect. The store opens only on a user tap.
- **CTA routing** (`gl`, fmt:2827-2876; line 4063, column 67510): calls within 500 ms are ignored, and a click sound plays. The first available exit wins:
  1. `install()` (only if `gameReady` is also a function; Mintegral style)
  2. `mraid.open(storeUrl)`
  3. `dapi.openStoreUrl()` (ironSource)
  4. `FbPlayableAd.onCTAClick()`
  5. `ScPlayableAd.onCTAClick()`
  6. `ExitApi.exit()` (Google)
  7. `openAppStore()`
  8. Fallback: `location.href` on touch devices, otherwise `window.open(_blank)`, then `location.href` if that is blocked.
  - The store URL is chosen by platform (iOS, or iPadOS detected as Macintosh with touch). A packager can override everything through `window.openStore`.
- **Lifecycle** (`Hm`, fmt:1996-2083; line 4063, column 32168):
  - `gameReady()` is called after loading (fmt:1989).
  - ironSource: viewability, volume and resize events through `dapi` (`isReady`/`ready`, `isViewable`, `viewableChange`, `getAudioVolume`, `audioVolumeChange`, `adResized`).
  - MRAID: `getState`, `ready`, `viewableChange`, `sizeChange`, `audioVolumeChange`.
  - It also exposes `window.gameStart` and `gameClose` (Mintegral style). If the page is hidden, not viewable, or the ad is closed, the game loop stops and audio is suspended.

## 6. Session length and difficulty curve

This is my estimate from the numbers above; I did not time a real run. A typical run lasts **about 2–3 minutes**:

| Segment | Estimate |
|---|---|
| Q0: walking about 7 s plus 3 punks at about 4 s each | ~20–25 s |
| Q1: walk to the bag, 3 spins at 2–2.6 s each plus icon flights, then close | ~20–25 s |
| Reward popup | ≥ 2.5 s |
| Q2: about 7 s of walking; girls die in 1 hit after training | ~15–20 s |
| Reward popup | ≥ 2.5 s |
| Q3: walk about 9 s, then the boss | 20–40 s |
| Victory to end card | ~3 s |

The boss's rage timings are built around a 30 s fight. Each death costs about 2 s plus healing time at the campfire.

**Difficulty curve:** basic enemies → a group of 4 faster enemies (but the player is ×4.5 stronger by then) → a boss with 4 telegraphed attacks plus rage. **Something new arrives about every 20–40 s** (a quest, a reward or an enemy type), and the biggest power jump lands right before the second fight.

## 7. Technical patterns worth copying

1. **Single HTML file.** The payload is gzip then base64 inside `<script type="text/plain">`. It is decoded with `DecompressionStream('gzip')`, falling back to a bundled inflater (`window.__gunzip`). CSS goes in as `<style>` and JS as an inline `<script>`. GLBs are slices exposed as `window.__glb` and parsed with `GLTFLoader.parseAsync`. Every PNG, sound and font is a data URL. The final `index.html` is 3.6 MB.
2. **Resolution-independent UI.** One CSS variable, `--u = min(W/2338, H/1080×1.15)` in landscape or `min(W/1080, H/2338×1.15)` in portrait (fmt:2170-2173). Every size is `calc(N*var(--u))`, and portrait has its own layout overrides (css:342-356).
3. **Safe area.** A hidden probe element with `padding: env(safe-area-inset-*)` is read and copied into `--sl/--st/--sr/--sb`. The values are re-mapped when the screen is rotated (fmt:2085-2118).
4. **Orientation.**
   - The game is designed for landscape. Outside ad networks, a portrait phone gets the whole app rotated 90° with CSS, and input coordinates are re-mapped (`xo`/`ci`, fmt:2119-2136).
   - Inside an ad network (any SDK object detected, `Jm`, fmt:2096-2099), or with `?portrait`, it uses a true portrait layout. The camera field of view is 34 in landscape and 52 in portrait (fmt:408-420).
   - It re-lays out on resize and visualViewport resize, and at 0/120/350/800 ms after orientation or visibility changes.
   - On Android outside ad networks, the first tap requests fullscreen and locks landscape.
5. **Preventing zoom and gestures** (fmt:1996-2018; index.html meta; css:4-6):
   - Viewport: `user-scalable=no`, `maximum-scale=1`, `viewport-fit=cover`.
   - CSS: `touch-action:none`, `overscroll-behavior:none`, no text selection or long-press callout.
   - Script-level blocking: `gesturestart/change/end`, `touchmove`, and `touchstart` when more than one finger or within 22 px of a screen edge (iOS back swipe). A second `touchend` within 320 ms is blocked (double-tap zoom) except on the CTA and end card. Context menu, text selection, drag and double-click are also blocked.
6. **Robustness:**
   - Audio unlocks on the first input with a silent 1-sample sound.
   - Pixel density is capped at 1.5 on touch devices and 2 elsewhere. It drops by 0.25 (down to 1) when the average frame takes longer than 1/45 s over 2 s (fmt:3263-3268).
   - Frame time is clamped to 1/20 s.
   - If the WebGL context is lost, its default handling is blocked and the page reloads when it comes back.
   - Telegram: `expand()` and `disableVerticalSwipes()`.
   - Debug globals `window.G` and `game`; URL flags `?inter`, `?portrait`, `?frame=0`.

**How this applies to us:** our template `Assets/WebGLTemplates/CatMiners/index.html` already has `viewport-fit=cover`, `touch-action:none` on the canvas, and a pixel-density cap of 2. It does **not** have `user-scalable=no`, the double-tap and gesture blocking, edge-swipe blocking, pausing on visibility changes, or a CTA function.

A Unity WebGL build will not fit the roughly 5 MB single-file limit ad networks usually impose (that limit is my general knowledge, not something I checked). So our build will work as a browser playable on hub-site. Points 2–6 transfer through the template plus a `.jslib`; point 1 does not.

## 8. Mapping to Cat Miners, ranked by impact on the first 30 minutes

Context for the targets: TZ §12.6 asks for the first delivery in 10–20 s, the first upgrade in 20–35 s, the first extra cat within about 1 min, and Mine 1 in 3–5 min (b4 free median 5.2 min). A player tap does 1.25 HP against a 20 HP rock, about 16 taps, with a 0.4 s minimum between taps (TZ §6.6).

1. **Quest panel with chained goals and an off-screen arrow.** This is the highest-impact change: it turns waiting into a series of goals.
   - **Layout:** a top-left panel with an icon, a yellow title, the step text and a green "0/N" that bumps on progress. On completion: "QUEST COMPLETE!" → 0.6–1.2 s → the next quest. Quests advance on real game events (`OutpostTutorial` already listens to them).
   - **Proposed chain:**
     - **Mine 1:** "Rock Smasher: Strike rocks 0/10" → "Payday: Deliver gold 0/3" → "Sharper Picks: Upgrade Pickaxes to Lv.3 0/3" → "Bigger Crew: Hire cats 0/1" → "Break the gate 0/1" → "Treasure!: Open a chest 0/1" → "Clear Mine 1 0/56 targets" → "Gear Up: Equip the new pickaxe".
     - **Mines 2–3:** "Upgrade Bags to Lv.2", "Upgrade Unloading", "Level up a card", "Use a 10-min Time Skip" (the starter pack is a real feature), "Clear Mine 2", "Clear Mine 3".
   - **Cadence:** the arrow points at the depot, the gate, the chest or the right button. Aim for one goal every 20–60 s in Mine 1 and every 1–3 min later.
2. **A reward after every quest.** Kung Fight grants a new combo every time.
   - **Popup:** reuse the ChestReward window as a "NEW CARD!" or "NEW PICKAXE!" popup with rays, rarity frame, name and rarity tag. "Tap to claim" becomes tappable after 0.9 s, and the icon flies to the Cards or Equipment button, which bumps.
   - **Rewards:** each quest pays something real: a chest, elixir, gold, or a skip.
   - **Main game untouched:** this should be data only in the playable build.
3. **A guaranteed early power jump.** In Kung Fight the reels are rigged so damage jumps ×4.5 before the second fight.
   - **Cat Miners equivalent:** a playable-only balance profile so that the first 2–3 Pickaxes levels and the 3rd cat are affordable within the first 60 s. Make the first chest guarantee a strong card (for example tap damage or crit).
   - **Make it visible:** show "Rock: 4 hits → 2 hits" before and after, and bump the team stat.
   - Don't hide anything unfair, in line with our existing ad honesty rules.
4. **Showing where gold comes from and goes.** At the depot: a "+N" coin pop and coins flying to the gold counter with a short stagger, a counter bump, and a counting-up number. Show a chest or reward icon above targets that contain chests and above gates.
5. **Hand pointer driven by game state, plus hints from a foreman cat.** Use Kung Fight's rules adapted:
   - Pointer rules:
     - hand on the nearest rock you can strike if there has been no strike 1.2 s after start;
     - hand on Base when an upgrade becomes affordable and has never been bought, then on the right row;
     - hand on Equipment or Cards when their action is first available.
   - **Speaker bubble:** a foreman-cat portrait bubble for 3.6 s at each quest start.
   - **Bottleneck hints** (our version of the low-HP hint): "Cats are queuing at the depot, upgrade Unloading!" or "Bags fill too fast, upgrade Bags", reusing the bottleneck logic from BalanceAudit.
   - Never put hints on idle timers.
6. **Tap-strike feel.** Damage numbers in yellow, with crits in orange plus a "CRIT" label. Hit pause of about 80 ms on the rock sprite only, squash, a white flash, a 12 ms vibration, and screen shake when a rock or gate breaks.
7. **Finales like boss fights.** Present the gate and the exit as "bosses": a big HP bar, pips for "rocks left in the sector", then "GATE OPEN!" / "MINE CLEARED!" with a 0.45 s slow-mo and a shake. This gives every mine a climax every 3–5 min.
8. **"Next upgrade" popups in the world.** Above the workbench or hiring board: "Pickaxes Lv.2 · 34/50 gold" with a fill bar, glowing and pulsing when affordable; tapping opens Base on that row. When you can't afford it: "Not enough gold! Deliver more ore".
9. **CTA layer**, which matters for the ad rather than for pace:
   - **Banner:** a persistent download banner at the top-centre or just above the nav bar. The bottom of the screen is taken by Store · Cards · Base · Equipment · To base.
   - **Interstitial:** a "MINE CLEARED!" interstitial with DOWNLOAD / CONTINUE (continue unlocks after 600 ms).
   - **End card:** after Clear Mine 3 or 30 min, with earned cards and gear, locked silhouettes, and "144 mines • 61 cards • up to 17 cats". Call `gameEnd`.
   - **Routing and redirects:** use `gl`-style SDK routing in a `.jslib`, and never redirect automatically.
10. **Showing locked content.** Kung Fight shows 2 locked combo slots and lists locked combos on the end card. Our equivalent is the locked cards ("Opens on mine N") and locked equipment slots, highlighted in the end card.
11. **A suspenseful chest opening.** Reuse the reel-stop pacing for reveals: staggered reveals by rarity, a "tease" glow before an uncommon or better card, and particle fountains. Do not add a slot machine, because it isn't in the real game.
12. **Template hardening.** Section 7 points 2–6 plus a desktop phone frame. This is basic hygiene rather than pace, but it stops zoom and scroll accidents on mobile.

Not applicable to us:

- **Death and respawn:** Cat Miners has no way to lose, and running out of gold never blocks the game (TZ §12.6).
- **Enemy respawn timers:** our mines are finite.
- **Autoplay:** our cats already work on their own.