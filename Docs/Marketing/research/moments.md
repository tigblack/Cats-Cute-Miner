# Cat Miners: ad-worthy moments, PlayMode recipes and capture timing

**Bottom line:** `Time.captureFramerate` plus `ManualClock.Advance(1/30)` on its own does **not** give deterministic, smooth 30 fps capture. The game logic, cat path positions, particles and sprite effects become deterministic. But the controller passes `Time.unscaledDeltaTime` (real wall-clock time) into `Board.Sync`, so many board animations still follow real time. These include cat pose timers, flying sacks, the player's pickaxe swing (which also decides when the strike lands), rock shake, gate doors, fog fade and building flashes. The HUD coin flights, camera smoothing and window pops do the same. The project already notes this at `OutpostStrikeEvidenceTests.cs:168-169`, and Unity's documentation confirms it. A stepper that fixes this is in §2.3.

Other key findings:
- There is no dynamite, cannon or barrel in the shipped mode. New cats pop out of the hire board in the camp. Upgrade levels never change how cats look; only equipped item rarity does.
- In the current mines the three visual themes differ only by recolouring floor, walls and posts.
- The debug profile, which is needed for `LoadZone`, `SimSpeed` and `GrantTest*`, prints a salmon **"[DEBUG]"** tag on the zone plaque.

Everything below is from reading the code; I modified no files.

---

## 1. Harness, profiles and debug tools

### 1.1 Base fixture
`OutpostCardsFixture` (`Assets/Game/Tests/PlayMode/Outpost/OutpostCardsUiTests.cs:20-61`) is the base class of `OutpostEnglishUiEvidenceTests` (`OutpostEnglishUiEvidenceTests.cs:27`).
- **SetUp (28-39):**
  - temporary save folder;
  - `CameraRig.OverrideAspect = 9f/16`;
  - `Clock = new ManualClock(1_800_000_000_000L)`, then `OutpostController.ClockOverride = Clock`;
  - `GameBootstrap.LegacyMode = false`, then load scene `"CoreGame"`;
  - asserts `Phase == Playing`.
  - The profile is a fresh **Main** profile (new game: mine 1, tutorial on).
- `Game => GameBootstrap.OutpostController` (24). `WaitPlaying()` waits up to 2000 frames for `Phase == Playing` (56-60).
- `OutpostEvidenceTests` and `OutpostStrikeEvidenceTests` copy the same setup without the base class (`OutpostStrikeEvidenceTests.cs:29-43`).
- `ManualClock` (`Assets/Game/Scripts/Core/Sim/Clock.cs:21-41`):
  - `Advance(double s)` moves both `UtcNowMs` and `MonotonicSeconds`.
  - The production `SystemClock` uses `Time.realtimeSinceStartupAsDouble` (`Runtime/SystemClock.cs`).
- `PlayModeUtil.WaitReal` uses `Time.realtimeSinceStartup` (`PlayModeUtil.cs:52-56`). `PlayModeUtil.Click(Button)` sends a real pointer click (78-82).
- **Render to file:** `OutpostEnglishUiEvidenceTests.Capture` (53-103). It points the camera at a 1080×1920 RenderTexture, sets the canvas to `ScreenSpaceCamera` with `planeDistance 1`, disables the scaler, sets `scaleFactor = sqrt(h/1920)`, calls `cam.Render()` and reads the pixels back.
- **Existing recorder:** `OutpostGameplayRecordingTests.RecordRealGameplay` (`OutpostGameplayRecordingTests.cs:81-235`). It records in real time (SystemClock) at 15 fps, 540×960, and captures audio through `GameplayRecordingTests.AudioTap` (`OnAudioFilterRead`, `GameplayRecordingTests.cs:268-300`) plus `AviWriter` (303+).

### 1.2 Controller API (`OutpostController`, contract in `Runtime/Outpost/IOutpostGame.cs:49-139`)

| API | Where | Gate |
|---|---|---|
| `SwitchProfile(bool debug)` | `OutpostController.cs:227-239` | editor or debug build; Playing or Error phase |
| `int SimSpeed {set}`: only 1/5/20/100, anything else becomes 1 | `OutpostController.cs:66-77` | **debug profile**, commands allowed |
| `LoadZone(int zone)`: fresh run of mine N, profile kept, wallet 0, levels 0 | `OutpostController.Commands.cs:238-247` | debug |
| `GrantTestGems()`: +10,000 gems | `Commands.cs:250-257` | debug |
| `GrantTestKit()`: all 16 items, +10,000 parts, then **EquipBest** (legendary everywhere) | `Commands.cs:258-267` | debug |
| `GrantTestCards()`: copies of every unlocked card, +100,000 elixir | `Commands.cs:272-281` | debug |
| `CompleteStageNow()`: finishes the mine off-screen in a coroutine | `Commands.cs:283-304` | debug |
| `Buy(UpgradeBranch, BuyMode)` / `Quote(...)` | `Commands.cs:12-13` | all profiles |
| `Equip(id)`, `Unequip(slot)`, `EquipBest()`, `UpgradeItem(id)`, `UpgradeCard(id)` | `Commands.cs:14-18` | all |
| `bool BeginStrike(int targetId, Vector3 world)`, `CanStrike(id)` | `Commands.cs:25-42` | all |
| `StoreBuy(string itemId)`, `UseTimeSkip(string itemId)` | `Commands.cs:62, 70-91` | all (test purchases on) |
| `ClaimOfflineDouble()`, `DismissOffline()`, `DismissSkip()` | `Commands.cs:97-110`, `Loop.cs:190-191` | all |
| `ContinueToNextMine()`, `CanContinue` | `Commands.cs:126-136`, `OutpostController.cs:43` | all |
| `FocusBase()`, `TryGetWorkerWorldPosition`, `TryGetTargetWorldPosition`, `BaseWorldPosition`, `DepotWorldPosition(slot)` | `Commands.cs:165-205` | all |
| Public members: `Sim`, `State`, `Map`, `Configs`, `Stats`, `Balance`, `Rig`, `Board`, `Hud`, `Tutorial`, `Fx`, events `EventPresented` and `Board.Cue` | `OutpostController.cs:23-57` | — |

The Dev panel (`DevTools/OutpostDevPanel.cs:41-60`) only wraps these: Main/Debug, mine ±1/±10 + Load, ×1/×5/×20/×100, Test kit, Cards, Gems, Finish. It installs via `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` and only if the controller already exists (21-27). In PlayMode tests it is normally **absent**; hide `"OutpostDevToggle"` defensively in case it is present.

### 1.3 Granting resources

| Resource | Recipe |
|---|---|
| **Gold, any profile, keeps save checks valid** | `Game.Sim.Submit(OutpostCommand.BonusGold(amount)); Game.Sim.Events.Clear();` (see notes below) |
| Gems | `GrantTestGems()` (debug), or `Game.StoreBuy("gems_pile")`: +28,000 in test-payment mode, works in Main (`store_catalog.json`, `"TestPurchases": true`) |
| Elixir | `GrantTestCards()` (+100k), or `StoreBuy("elixir_wagon")` (100k for 4,500 gems) |
| Cards | `GrantTestCards()` only covers cards unlocked up to the highest mine reached. Use `LoadZone(138)` first for all 61 (as `OutpostCardsUiTests.cs:199-200` does). |
| Items and parts | `GrantTestKit()`; `UpgradeItem(id)` up to level 20 (`equipment_catalog.json` `MaxItemLevel`) |
| Cheap upgrades in Main | Swap in a cheap balance clone and restore it before rendering (§5.3) |

Notes on the gold recipe:
- `BonusGold` keeps `Wallet == TotalCredited + BonusCredited − TotalSpent` true (`Sim/OutpostSimulation.Store.cs:30-41`). That rule is checked on every save (`OutpostInvariants.cs:121`).
- Never write `Run.Wallet` directly: the save check fails and the phase becomes `Error`.
- Clear `Sim.Events` right after, otherwise the HUD shows the toast "Income doubled: +X gold" (`OutpostHud.Store.cs:300`).

### 1.4 Global gotchas
1. **[DEBUG] tag.** In the debug profile the plaque under "Mine N" reads `sector a/b [DEBUG]` (`OutpostHud.cs:60, 1018`). It is rewritten only when the mine, the opened-gates count or the profile changes (1009-1021). Workarounds:
   - strip it after every refresh: find the TMP text `ZonePlaque/sector` and remove `" <color=#FF9070>[DEBUG]</color>"`; or
   - prepare the state in debug, then set the private `m_Slot` to `OutpostSaveSlot.Main` through reflection (`OutpostController.cs:30, 59`; this also turns SimSpeed off, see §3); or
   - mask the plaque in compositing.
2. **Tutorial.** Each profile has its own tutorial. A fresh debug profile starts at mine 1 with the tutorial on. After `LoadZone(2)` a "kept bonus" hint bubble appears once (`OutpostTutorial.cs:297-312`). Call `Game.Tutorial.Skip()` (148).
   - The strike step may smooth-pan the camera to a rock (`OutpostTutorial.cs:421-426`).
   - "Skip tutorial" shows under the plaque while a hint is up (`OutpostHud.cs:415-418, 1208-1215`).
3. **Windows that open by themselves** (`OutpostHud.Results.cs:130-158`, run from `RefreshNow`):
   - "Counting time away" bar during offline catch-up;
   - "While you were away" when offline time is at least 5 s (`Loop.cs:160`);
   - time-skip result;
   - mine results;
   - **mine chest window whenever a chest gives a NEW card** (`OutpostHud.Cards.cs:318-324`).

   Pre-own the cards (`GrantTestCards`) to turn those chests into toasts. Close windows with `Game.Hud.HandleBack()` or `CloseAllWindows()`.
4. **Any single clock jump over 1 s starts offline mode** (`Loop.cs:71`): calculation window, then the summary if the jump is 5 s or more. Keep each per-frame `Clock.Advance` at or below 1.0.
5. **These calls rebuild the board** through `SetSimulation`, which runs `Board.Build` and `Rig.SetBoard`: `GrantTestKit`, `GrantTestCards`, `GrantTestGems`, `LoadZone`, `SwitchProfile`, the end of offline or skip, and mine transitions (`OutpostController.cs:155-174`). Effects:
   - the camera resets to the camp;
   - any `orthographicSize` override is lost;
   - cats snap without animation.

   Do grants **before** you frame a shot.
6. The 2-slot depot queue, effect budgets (8 effects and 3 texts per frame, `OutpostBoardView.cs:32`) and the 96-events-per-frame cap (`Loop.cs:106`) count per frame. At 30 fps capture you get half the budget of 60 fps.

---

## 2. Time sources and deterministic capture

### 2.1 Which clock drives what

| Driven by | What |
|---|---|
| **Game clock** (`IClock`, i.e. `ManualClock`) | Game logic: `AdvanceOnline` turns monotonic µs × speed into `Sim.Advance` (`Loop.cs:65-87`). Cat positions on path edges are computed from `sim.Now` (`OutpostBoardView.cs:513-584`). Mining wind-up `TimeToHit`, pickup/unload frames and the depot progress bar also follow the game clock (`BaseCampView.cs:233`), as do the strike tap limit (`Commands.cs:35-39`, `StrikeTapInterval = 0.4`), autosave every 2 s and store timers. Walk-cycle frames depend on distance travelled, not time (`OutpostCatView.cs:386-398`). |
| **`Time.unscaledDeltaTime` / `unscaledTime`** — *not* fixed by captureDeltaTime | `Board.Sync(Sim, Time.unscaledDeltaTime)` (`Loop.cs:45`) feeds:<br>• cat idle, blink and stretch, strike swing 0.31 s, spawn 0.85 s, joy 0.69 s, pop and pulse, catch-up glide (`OutpostCatView.cs:50-72, 336-422`)<br>• flying sacks 0.2–0.36 s (`OutpostBoardView.cs:34-36, 410`) and cargo piles (409)<br>• rock shake 0.12 s and gate door frames 3×0.12 s (`OutpostTargetView.cs:466-503`)<br>• gate barrier (`OutpostBoardView.cs:402`)<br>• camp building flash 0.2 s and crate bump (`BaseCampView.cs:296-321`)<br>• fog fade 0.8 s (`OutpostBoardView.Floor.cs:17, 500-514`)<br>• **player pickaxe, 0.26 s, which fires the Strike command at 0.10 s** (`OutpostBoardView.Strike.cs:45-51`, `PlayerStrikeFx.cs:18`)<br>Elsewhere:<br>• HUD coin fly 0.6 s, gold-icon pulse, toast lifetime, 10 Hz refresh (`OutpostHud.cs:591-595, 886-899`), chest-toast batching (`OutpostHud.Cards.cs:325, 340`)<br>• window pop 0.15 s (`UI/PixelPop.cs:36`, added to every window at `PixelUi.cs:602`)<br>• camera smooth focus, fling, kick (`CameraRig.cs:389-421`)<br>• next-mine fade 0.25+0.25 s (`Commands.cs:144, 154`)<br>• tutorial paw and mentor (`UI/TutorialHint.cs:306-383`) and the tutorial's 4 Hz checks (`OutpostTutorial.cs:233`)<br>• all audio throttles (`OutpostAudio.cs:40`, `Sfx.cs`) |
| **`Time.time` / `Time.deltaTime`** (scaled, fixed by `captureDeltaTime`) | Waterfall, pool and deposit-wheel frame tracks: `ApplyState(ts, …, Time.time, dt)` (`OutpostBoardView.cs:382, 397`; `OutpostTargetView.cs:55-62`). Lantern flicker (`OutpostBoardView.Floor.cs:347`, `BaseCampView.cs:143, 286`). Sprite effects such as sparkle, puff, coin pop, impact and glow (`Visual/Effects.cs:378-438`). Chip particles (`ParticleSystem`, scaled time, `Effects.cs:95-130`). Floating "+N" text (`Visual/FloatingText.cs:56`). |
| `Time.frameCount` | Effect and text budgets (`OutpostBoardView.cs:690-708`), one impact per frame (`Effects.cs:173`) |
| Unseeded `UnityEngine.Random` | Cat blink and stretch timers (`OutpostCatView.cs:197-199, 359, 368`), lanterns, coin-fly angles (`OutpostHud.cs:657-658`), particle lifetimes (`Effects.cs` Emit). Call `Random.InitState(seed)` for repeatable takes. |

### 2.2 Verdict on the simple approach
`Time.captureDeltaTime = 1/30` plus `Clock.Advance(1/30)` fixes the game logic, positions, particles and `Time.time` animations. Everything in the unscaled row still runs on wall-clock time:
- If rendering plus readback takes about 100 ms per frame, sack flights finish in 1–3 video frames.
- The pickaxe can hit on the first frame.
- Coin flights to the counter take about 6 frames.

Unity confirms that captureDeltaTime does not affect unscaled time, and the project comment says the same (`OutpostStrikeEvidenceTests.cs:168-169`).

### 2.3 Deterministic stepper (copies `OutpostController.Update`, `Loop.cs:27-48`, with a fixed dt)
```csharp
const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
static readonly MethodInfo Adv  = typeof(OutpostController).GetMethod("AdvanceOnline", NP); // (bool detectGap) Loop.cs:65
static readonly MethodInfo Pres = typeof(OutpostController).GetMethod("PresentEvents", NP); // Loop.cs:103
static readonly MethodInfo Off  = typeof(OutpostController).GetMethod("StepOffline", NP);   // Loop.cs:141
static readonly MethodInfo Fly  = typeof(OutpostHud).GetMethod("UpdateLootFly", NP);       // (float dt) OutpostHud.cs:682
static readonly MethodInfo Rel  = typeof(OutpostHud).GetMethod("RelayoutDialogs", NP);     // OutpostHud.Results.cs:112
static readonly MethodInfo Flush= typeof(OutpostHud).GetMethod("FlushCardToasts", NP);     // OutpostHud.Cards.cs:338
const float Dt = 1f/30f;
// setup: Time.captureDeltaTime = Dt; Random.InitState(42); Game.enabled = false; Game.Hud.enabled = false;
//        canvas -> ScreenSpaceCamera on the 1080x1920 RT once (as in Capture()).
void Step(double simSeconds /* = Dt for 1x; 0.2 = 6x; max 1.0 */) {
    Clock.Advance(simSeconds);
    if (Game.Phase == OutpostControllerPhase.OfflineCatchUp) Off.Invoke(Game, null);
    else if (Game.Phase == OutpostControllerPhase.Playing) Adv.Invoke(Game, new object[]{ true });
    if (Game.Phase == OutpostControllerPhase.Playing) { Pres.Invoke(Game, null); Game.Board.Sync(Game.Sim, Dt); }
    Flush.Invoke(Game.Hud, null); Game.Tutorial?.Update(); Fly.Invoke(Game.Hud, new object[]{ Dt });
    Rel.Invoke(Game.Hud, null); if (frame++ % 3 == 0) Game.Hud.RefreshNow();   // also opens auto windows
    Game.Rig.FocusOn(new Vector2(camX(frame), camZ(frame)));   // your own easing; cancels FocusOnSmooth
    Canvas.ForceUpdateCanvases(); cam.Render(); /* readback */  // then: yield return null
}
```
- Commands such as `Buy` and `BeginStrike` still go through the controller (`Execute` drains into its event list). Your next `PresentEvents` shows their effects: building flash, hired-cat pop and so on.
- With the HUD disabled, toasts never hide by themselves. Call `Game.Hud.Toast("")` when you want one gone.
- Remaining real-time pieces: the tutorial paw (`TutorialHint.LateUpdate`), window pops and the transition fade. Re-enable `Game.enabled` around `ContinueToNextMine` and `CompleteStageNow`, which start coroutines on the controller, or composite your own fade.
- **Audio:** sound cannot be synchronised in a non-real-time capture. Log `Game.Board.Cue` (`OutpostBoardView.cs:82`; cue types in `OutpostViewCue.cs:11-40`, mapping to sounds in `OutpostAudio.cs:37-66`) and `Game.EventPresented` with the frame number. Mix sound effects in Python from `Assets/Game/Resources/Audio/*.ogg` (70 files, PC-only; get them with `on-remote --fetch`).

---

## 3. How SimSpeed works

`Sim.Advance(m_BaseTick + elapsedMonoUs * (IsDebugProfile ? m_Speed : 1))` (`Loop.cs:76-77`); the clock is rebased whenever the speed changes (`OutpostController.cs:75`).

How fast play looks:
- Base move speed is 2.5 cells/s (`OutpostConfigSet.Defaults.cs:30`); one hit per second (`HitIntervalTicks 1e6`, line 11).
- At 30 fps: ×5 moves about 0.42 cells per frame, which is watchable. ×20 is about 1.7 cells per frame: cats cut corners, walk frames alias and they appear stuck mid-swing. ×100 is about 8 cells per frame and looks like teleporting.
- Positions stay exact (no "jump" correction, `OutpostBoardView.cs:430-431, 457`). The visual mess comes from frame skipping, not wrong logic.

Recommendations:
- Use ×1 for hero shots and ×5 for time-lapse. Use ×20/×100 only to fast-forward state off-camera; `MineChestScreens` uses ×100 with 0.25 s steps (`OutpostEnglishUiEvidenceTests.cs:300-306`).
- With the stepper you get any speed in **any** profile by passing `simSeconds > Dt` (≤1.0), with no [DEBUG] tag.

---

## 4. Camera, mine size and clean shots

- **Rig** (`Runtime/CameraRig.cs`): a top-down orthographic camera (pitch 90°).
  - Every mine has a branching layout (all 144 in `mine_topology.json`), so `FreeExploration` is on (`OutpostController.cs:168-169`).
  - The view is **10 columns wide** (`CameraRig.cs:39, 216`): `orthographicSize = 5/0.5625 ≈ 8.89`, so the full frame is 17.8 rows tall.
  - HUD panels take 128/1920 at the top and 400/1920 at the bottom (`OutpostHud.cs:33-45`), leaving about 12.9 rows of field.
- **Mine sizes** (width × depth, in cells; plus 3 camp rows and 3 yard rows):

  | Mine | Size |
  |---|---|
  | 1 | 29×21 |
  | 2 | 27×12 |
  | 5 | 27×33 |
  | 10 | 29×52 |
  | 48 | 31×38 |
  | 100 | 47×64 |
  | 120 (largest) | 43×76 |
  | 144 | 49×57 |

  A normal frame shows roughly one third of mine 1's width.
- **Pan and follow:**
  - `FocusOn(Vector2 xz)` is instant (`CameraRig.cs:329-336`).
  - `FocusOnSmooth` uses unscaled time (342-361); `ScrollWorld` (317-324).
  - `FocusZ` is the centre of the band between HUD panels.
  - Positions snap to one art pixel (1/32 cell, 436).
  - Follow a cat each frame: `Game.TryGetWorkerWorldPosition(id, out w); Rig.FocusOn(new Vector2(w.x, w.z));`
  - `Board.GateWorldPosition(order)` and `Board.BaseFocusZ` are public (`OutpostBoardView.cs:94, 163-167`).
- **Zoom:** there is no API for it. Set `Game.Rig.Cam.orthographicSize` directly:
  - 7.5 → exactly 4 screen px per art pixel, 8.44 columns;
  - 10 → 3 px, 11.25 columns;
  - 15 → 2 px;
  - the default 8.89 gives a non-integer 3.375 px.

  `FitWidth` resets it on `SetBoard`/`Refit` (`CameraRig.cs:88-103, 187-228`), i.e. after the board-rebuild calls in §1.4.5. Pan limits are not recomputed. For a whole-mine overview, set ortho size and camera position directly with the canvas off; `MineTopologyRuntimeTests.cs:177-233` does this.
- **Hide the HUD:** either set `Game.Hud.GetComponent<Canvas>().enabled = false`, or keep the canvas in `ScreenSpaceOverlay` (overlay is not drawn into the RT). Then call `Rig.SetUiInsets(0,0)` to re-centre the band; this also re-runs `FitWidth`, so set the zoom afterwards. World-space pieces stay visible: HP bars, depot "+N", floating text, the player pickaxe and the selection ring.

---

## 5. Moments, with recipes
Clip lengths are in real seconds at speed ×1.

### 1. Fresh start, mine 1, 2 cats, tutorial paw
- **On screen:**
  - The camp at the bottom: depot, hire board, workbench, rack, cart, wardrobe, sign and lanterns (`BaseCampView.cs:95-155`).
  - 2 cats (StartWorkers 2, `Defaults.cs:14`) walk up a winding tunnel to 8 starter rocks (`MINE_TOPOLOGY.md`).
  - A mentor-cat portrait and bubble: "The cats already work on their own: they break rocks and collect coins" (5.5 s). Then the paw on a rock: "You can break rocks too! Tap a rock to hit it with your pickaxe" (up to 8 s visible) (`OutpostTutorial.cs:37-41, 340-354`; text in `Loc.Outpost.cs:334-335`).
- **Recipe:**
  - Fixture default (Main profile; no tag).
  - To jump straight to the paw: `Game.State.Profile.Tutorial.Seen.Add(OutpostTutorial.StepAuto)`, then wait at least 0.25 s, as `OutpostStrikeEvidenceTests.cs:149-152` does.
- **Length:** first delivery within roughly 10–60 s. Mine 1 takes about 5 min and ends with 1,717 gold in the audit run (`BALANCE_AUDIT.md:324`).
- **Gotchas:**
  - The hint uses `Screen` size for projection and choosing the paw target (`OutpostTutorial.cs:388-416`). Set the Game view to 1080×1920, or the paw may pick a different rock.
  - The tutorial may pan the camera (421-426).
  - The hint is drawn on the HUD canvas, so it only appears with the canvas in camera mode.

### 2. Player tap-strike
- **On screen:**
  - A pickaxe sprite (`fx_strike_pick_0..3`) appears at the tap with a scale pop, swings and hits at 0.10 s; gone at 0.26 s (`PlayerStrikeFx.cs:18-22`).
  - On hit: impact spark, chip particles in the rock's colour, 1-px shake for 0.12 s, sparkles at the pick, HP bar appears (`OutpostBoardView.Strike.cs:53-83`, `OutpostTargetView.cs:650-655`).
  - Damage stages: below 75 / 45 / 20 % HP the rock switches to damaged sprites `_d1.._d3` or crack overlays. Gates crack below ⅔ and break below ⅓ (`OutpostTargetView.cs:507-568`).
  - Cargo crossing HP thresholds flies out as sacks onto the floor (`CargoReleased`, 0.36 s arc). **No coins and no damage numbers** (`OutpostSimulation.Strike.cs:57-121`). Cats then pick the sacks up and deliver them.
  - On break: dust puff plus a burst of 10–24 particles (`OutpostTargetView.cs:658`); the cats mining that rock jump with joy (`Events.cs:72-93`).
- **Recipe:**
  ```
  id = Game.Map.Targets.First(t => !t.IsGate && Game.CanStrike(t.Id)).Id
  Game.TryGetTargetWorldPosition(id, out w)
  Game.BeginStrike(id, w + new Vector3(0, 0, 0.35f))
  ```
  Repeat every ≥0.4 s of game clock and only when `Game.Board.ActiveStrikes == 0` (`Commands.cs:38`).
- **Damage:** a tap does ¼ of a cat's hit (`TapDamageFactor 1/4`, `Defaults.cs:29`; base damage 5). Starter rocks have HP about 20 (`HpBase 20`), so roughly 16 taps alone, or about 6 taps with 2 cats helping.
- **Gotchas:**
  - Only targets in the current sector that cats can reach are strikable. A gate becomes strikable only once its sector is cleared (`Strike.cs:18-46`).
  - With the stepper, the hit lands deterministically on frame 3.

### 3. Base panel purchases, hiring, what changes
- **Base panel** (`OutpostHud.BasePanel.cs`; `ShowBase()` 184):
  - A bottom sheet with ×1 / ×10 / Max buttons named `mode0..2` (84).
  - Six rows `branch{i}`, each with: icon, "Name Lv a/b", "now → after" effect, progress bar (gold, green at max), gold `buy` button with price (128).
  - **Team is always ×1** (216, and `TeamStats.cs:282-285`).
  - Caps: Team 10, Pickaxes 20, Boots 15, Bags 16, Unloading 20, Loot Value 40 (`Defaults.cs:20-27`).
  - Click with `PlayModeUtil.Click`, or call `Game.Buy(...)` directly.
- **On purchase:** the camp building for that branch scales up 1→1.125→1 in 0.2 s, with 3 sparkles and an amber glow (r 1.2, 0.5 s) (`BaseCampView.cs:333-359`; `Events.cs:123-135`). An upgrade sound plays.
- **Hiring:** `WorkerHired` → a new cat **pops out in front of the hire board**:
  - 8 spawn frames, about 0.85 s, scale 0.7→1.12→1, dust puff;
  - it then glides to the spawn cell and starts work (`Events.cs:136-153`; `OutpostCatView.cs:62-68, 253-260`; `BaseCampView.cs:104`);
  - HUD cat counter +1.
  - **There is no cannon or barrel.** Those exist only in the retired legacy mode (`Visual/CannonView.cs`, `GameController`, entered via `GameBootstrap.LegacyMode`, `GameBootstrap.cs:37-45`).
- **What changes visually:**
  - Cats do **not** change with branch levels (`OutpostLook.cs:10`).
  - Camp buildings change tier at ⅓ and ⅔ of the cap (`OutpostLook.cs:152-157`; `BaseCampView.cs:169-184`):
    - hire board, workbench and rack have 3 tiers each;
    - the cart switches to `cart_1` with any Boots level;
    - the depot chute appears with any Unloading level;
    - depot crates fill in 4 stages by share of cargo delivered (186-199).
  - Coat colour: 6 furs by worker id (`OutpostLook.cs:15, 114`).
  - The sack on a cat's back grows in 3 stages with bag fill (144-149). A full bag sparkles (`OutpostBoardView.cs:470-477`).
- **Recipe for many purchases in Main:** `OutpostEvidenceTests.cs:131-175`:
  1. Set `Game.State.Run.Balance` to a clone with `BaseCost = Growth = 1`.
  2. Earn or grant gold and buy.
  3. Restore the real balance before rendering.

  Or use `BonusGold` (§1.3) with the real prices.

### 4. Many cats swarming / max team
- **Size:** max team = 2 + 10 Team levels = **12**, plus card **ca038** (TeamCap, +L, max level 5) (`card_rules.json:43-48`; `TeamStats.cs:265-268`). The audit saw 16 at mine 144 (`BALANCE_AUDIT.md:224`).
- **Behaviour:**
  - At most 4 cats per target (`MaxWorkersPerTarget 4`).
  - Cats line up at the 2-slot depot (`OutpostBoardView.cs:534-540, 607-617`).
- **Recipe (debug):**
  1. `LoadZone(z)`, then `Tutorial.Skip()`.
  2. `GrantTestCards()`, then `UpgradeCard("ca038")` while possible.
  3. `Sim.Submit(BonusGold(big))`, clear events.
  4. `Buy(Team, One)` ×N, one every 0.4–0.5 s, to stagger the pops.
  5. Run 20–40 s at ×5 so cats spread out, then record at ×1.
  - Main alternative: the cheap-balance trick.

### 5. Gold counter and big numbers
- **HUD top bar** (`OutpostHud.cs:1004-1059`), left to right:
  - delivered gold, labelled "gold" — the per-mine wallet, which **starts at 0 every mine** (`StageProgressionService.cs:75`);
  - "en route" cargo value (blue);
  - cleared/required;
  - cat count.
- **Updates:** text changes at most 10 times a second with **no count-up tween**. Each delivery (`CargoDeposited`) launches up to 6 coin icons on a 0.6 s curve to the gold icon, which pulses +25 % (`OutpostHud.cs:635-711, 1240-1241`). A gold "+N" floats over the depot for 1 s, coins pop at the funnel (`Events.cs:237-250`).
- **Number format** (`Core/Outpost/Math/Money.cs:14-28`, `Core/Numbers/NumberFormat.cs:56-66`): 3 significant digits, truncated, with K, M, B, T, then **aa (1e15), ab (1e18)…**
  - Examples: 1,717 → "1.71K"; 421,219,114 → "421M"; 1.1e17 → "110aa".
- **Real gold per mine** (audit sessions, `BALANCE_AUDIT.md:322-326`): mine 1 ≈ 1.7K, mine 48 ≈ 421M, mine 144 ≈ 1.1e17.
  - Interpolated growth is about ×1.22 per mine. Expect "B" from about mine 52 and "T" from about mine 86. Estimate only.

### 6. Gates, sector opening, exit, results, next mine
- **Which mines have gates** (`mine_topology.json`): 5, 6, 8, 9, 10 (2), 12, 13 (2), 14–15, 16–17 (2), …; mine 144 has 3. Mines 1–4, 7 and 11 have none.
- **Gate:** a 2-cell passage with a frame and doors (closed → cracked → broken), plus a barrier and dark fog over the locked sector.
- **On opening** (`Events.cs:94-116`, `GateBarrierView.cs:129-135`):
  - door-open frames, 3×0.12 s;
  - dust puffs at each span;
  - amber glow r 3.5 for 1.6 s and 4 sparkles;
  - fog fades in 0.8 s;
  - toast "Sector 2/3 opened" (`OutpostHud.cs:1243-1246`); plaque "sector 2/3";
  - the camera smooth-pans to the gate only if it is far above the camp (`Loop.cs:111-115`).
- **Recipe:**
  1. `LoadZone(13)` (2 gates, crystal theme).
  2. Run ×20 until `Game.Sim.CanStrike(Game.Map.Gates[0].TargetId)` is true, i.e. the sector is cleared.
  3. `Rig.FocusOn` the gate position, switch to ×1 and record cats plus taps on the gate.
- **Exit / completion:**
  1. All cleared → status pill "Delivering the remaining coins" (`DrainingStarted`).
  2. Last sack delivered → `StageCompleted` (`OutpostSimulation.Tick.cs:240-317`):
     - the cats walk to camp rows and jump with joy (`OutpostBoardView.cs:480-484, 620-636`);
     - 5 sparkles at camp;
     - the exit marker switches from boards to glow (`Floor.cs:392-401`; in branching mines it is drawn at half size, `Topology.cs:224-234`);
     - the exit chest opens (mines 1–10 have scripted cards). If the card is new, the chest window comes first.
  3. Results window "Mine N Results": Cleared, Gold delivered, New item, Mine reward parts, cards/elixir; buttons Equipment / Next mine (`OutpostHud.Results.cs:172-208`). The strip button reads "Go to the next mine".
  4. `ContinueToNextMine()` (or `PlayModeUtil.Click` on `StripAction`): navy fade 0.25 + 0.25 s, then the "Counting time away" window may flash for a frame or two (`Commands.cs:138-163`; `Results.cs:138-139`).

### 7. Chests: in the mine and in the store
- **Store tiles** (`store_catalog.json`):
  - `chest_wooden`: free ad tile, 240-min cooldown, free at start;
  - `chest_iron`: 145 gems;
  - `chest_mythical`: 3,500 gems.
  - Golden, Silver, Forged, Royal and Legendary chests come **only from mines**.
- **Recipe:**
  - `Game.StoreBuy("gems_pile")` (+28k gems), then `Game.StoreBuy("chest_mythical")`. The window opens on the next presented frame (`OutpostEnglishUiEvidenceTests.cs:245-262`).
  - For the UI flow: `Hud.ShowStore()`, click the tile (`Tile(id)` helper, lines 208-210), then the confirm modal.
- **Window** (`OutpostHud.Store.cs:308-384`):
  - title (e.g. "N Chests" when several stack), chest icon, "Cards: N";
  - up to **6 card tiles**: portrait, name, ×copies, gold **"NEW"** badge, new cards sorted first (`ChestCardTile.cs:63-73`);
  - "+N more cards", "Elixir +X", button "Great".
  - Static except a 0.15 s pop. **There is no lid or opening animation**; add drama in post.
- **Chest rocks in mines** (`rock_chest_<tier>`): rocks whose whole reward cycle is chests (`PresentationCatalog.cs:114-134, 204-213`). My Python copy of that rule, run on `json-data.json` (verify at runtime with `PresentationCatalog.RockVisual(Game.Balance, t.TypeId)`), gives:

  | Tier | Mines |
  |---|---|
  | Wooden | from 3 |
  | Iron | from 5 |
  | Forged | from 7 |
  | Silver | 9, 16, 20 … |
  | Golden | 30, 40, 46, 50, 60, 65, 70, 76, 83, 90, 100, 106, 117, 123, 126, 130 |
  | Royal | 86, 92, 98, 133, 140 |
  | Legendary | 56, 66, 80, 96, 110, 120, 126, 137, 144 |
  | Mythical | 100, 130 |

  - Deposits (`s`) and gates (`p`) also give chests (`OutpostSimulation.Cards.cs:12-34`).
  - Breaking one shows the normal break effect. It then opens the chest window **only if a card is new** (`OutpostHud.Cards.cs:318-324`); otherwise a toast "Chest: cards +N, elixir +M".
  - Elixir rocks float "+N" (`OutpostHud.Cards.cs:309-310`).

### 8. Card collection and card upgrade
- **Collection:** `Hud.ShowCards()` (`OutpostHud.Cards.cs:143-154`) opens a full-screen 3-column grid of 61 `CardView`s:
  - rarity frame, portrait `card_art_<id>`, name, effect, level, copies bar;
  - locked cards are dimmed with a lock icon (`CardView.cs:10-16`);
  - header "Cards", elixir, "Collected X of 61", sort button.
  - All unlocked: `LoadZone(138)`, then `GrantTestCards()`. Scrolling: `ScrollRect.verticalNormalizedPosition` (`OutpostCardsUiTests.cs:199-260`).
- **Detail and upgrade:** `Hud.ShowCard(id)` shows a big card, "current → next" in green, elixir price and a green **Upgrade** button named `cardUpgrade`. Upgrading (`UpgradeShownCard`, 271-288) plays a 0.15 s window pop, the upgrade sound and a toast "<Card>: level N".

### 9. Equipment
- **Data:** 16 items: pickaxe / hat / backpack / boots × common / rare / epic / legendary (`Defaults.cs:43-58`).
- **On the cats:** each slot is a sprite layer on **every** cat, with 5 looks (base, common, rare, epic, legendary) (`OutpostLook.cs:17-31, 124-141`; `OutpostCatView.SetLook` 217-237).
- **Effect when gear changes:** `Equip` / `EquipBest` / `UpgradeItem` → every cat pulses 1→1.125 in 0.2 s, up to 4 sparkles, chime (`Events.cs:154-177`).
- **Recipe for a visible "glow-up":**
  1. `GrantTestKit()` (it auto-equips the best).
  2. `Unequip(EquipSlot.X)` for all 4 slots, or `Equip("*.common")`.
  3. Frame the cats, then `Equip("pickaxe.legendary")`, `Equip("hat.legendary")`, … about 0.5 s apart.
- **UI:** `ShowEquipment()`, `ShowItem(id)` (`OutpostHud.Equipment.cs:280, 439`) with `equip` / `upgrade` buttons. The wardrobe in camp is open while unequipped items exist (`BaseCampView.cs:201-206`).

### 10. Offline window "While you were away"
- **Recipe:** with the controller running (or the stepper), `Clock.Advance(3*3600)` in one step. The 1 s gap check triggers it (`Loop.cs:71`), or use pause/unpause (`OutpostEvidenceTests.cs:180-183`).
- **Sequence:**
  1. Calculation window "The cats kept working…" with a gold bar "N%" (`Results.cs:330-346`).
  2. Summary window: title, coin icon, "Gold earned\n" plus the amount at **200 %** size; gold button "Double income" with a TV icon and "Watch ad"; green "Great" (`Results.cs:70-82, 295-302`).
- **Double income:** `ClaimOfflineDouble()` or click the button. Wallet += amount, window closes, toast "Income doubled: +X gold" (`Commands.cs:97-110`, `Store.cs:300`).
- **Limits:** capped at 3 h (`OutpostIds.cs:21`). If the mine finishes during the absence, results follow.
- **UI-only option:** `Hud.ShowOffline(new OfflineSummary{ GoldDelivered = … })` shows any number, but that number is made up and the double button will be refused.

### 11. Time skip
- **Tiles:** `skip_1h`, `skip_4h`, `skip_12h` are actually **10 / 30 / 60 minutes** (`store_catalog.json`). Starter stock: 3×10 min and 1×30 min (`StarterConsumables`).
- **Recipe:** `Game.UseTimeSkip("skip_4h")`, or click the tile in `ShowStore()`.
- **Sequence:**
  1. Calculation window "Time Skip — The cats work 30 min in a flash…" with a bar.
  2. Result window "Time Skip: 30 min": "Gold earned …", "Cleared: N", cards line, "The mine was cleared during the skip!" (`Store.cs:436-450`; `Loc.Outpost.cs:303-307`).
- **Gotchas:**
  - The board **rebuilds and snaps**; cleared rocks simply vanish. Cut before/after.
  - Refused once the mine is complete (`Commands.cs:77`).
  - The test waited up to 600 frames for it (`OutpostEnglishUiEvidenceTests.cs:268-272`).

### 12. Visual themes
- **Assignment** (`Resources/Visual/environment_themes.json`): Cycle 9 — mines 1–3 Warm, 4–6 Crystal, 7–9 Copper, repeating (`EnvironmentThemes.cs:40-45`; applied in `OutpostBoardView.cs:202`).
- **What actually changes:** in the current branching mines only a recolour of floor, walls, edges, beam posts, exit, rubble, waterfalls and deposit cracks, plus the background and fog colour (`PixelTheme.cs:25-64`; `Topology.cs:132-235`).
  - Warm: navy/slate.
  - Crystal: cave-teal / frost.
  - Copper: rust / copper.
  - Copper gears, pipes, crystal spires and wall glints exist only in the old single-column layout code (`Floor.cs:217-220, 265, 288`). They do **not** appear.
- **Most different mines:**
  - Warm 10 (2 gates, 29×52);
  - Crystal **13** (2 gates);
  - Copper **16 or 17** (2 gates);
  - plus layout variety: winding galleries, ore archipelago, forked veins; mine 120 is 43×76.
- **Variety of objects:** animated waterfalls (mines 1, 2, 4, 6, 8, …); spinning deposit wheels with gems (from mine 2, wheel turns while mined, `OutpostTargetView.cs:486-489`); chest, elixir and gem rocks.
- **Music:** the track rule is `(zone-1)%3==1` → Crystal (`OutpostController.cs:171`). It does not match the theme cycle.

### 13. Special effects
- **None of these exist in the shipped mode:** dynamite, explosions, camera shake (`Kick` is never called from Outpost code), crits or damage numbers. Dynamite and cannon live only in legacy `Core/Sim` and `GameController`.
- **Available effects:** chips, burst, puff, impact, sparkle, glow, coin pop, floating "+N", sack arcs, gate open plus fog fade, building flash, gear pulse, cat joy/spawn/stretch (`Visual/Effects.cs:161-330`).
- **Settings:** keep `ReduceEffects` off; it halves particles and budgets (`OutpostBoardView.cs:701`).

### 14. Camera rig
See §4.

---

## 6. Top 12 hero shots, ranked

1. **Cat swarm (crystal, mine 13):**
   - Setup: debug `LoadZone(13)`, `Tutorial.Skip`, `GrantTestCards` and ca038 upgrades, `BonusGold`, `Buy(Team)` ×10, 30 s at ×5.
   - Shot: record ×1, ortho 7.5, follow the depot queue. 12+ cats, sacks flying, coins to the counter.
2. **Hire burst:**
   - Setup: ortho 7.5 on the camp, `BonusGold`.
   - Shot: `Buy(Team, One)` every 0.5 s ×10. Cats pop from the hire board with dust; the board flashes up its tiers; the counter goes 2→12.
3. **Tap-combo rock break:** `BeginStrike` every 0.4 s on a starter rock in mine 1, with the 2 cats → cracks, burst, sacks pop, cats grab, "+N" and coin flight. Add a finger overlay in post.
4. **Sector reveal (mine 13 or 16):** fast-forward until `CanStrike(gate)`, focus on `GateWorldPosition(0)`, tap the gate → doors open, fog lifts, amber glow, toast "Sector 2/3 opened".
5. **Legendary glow-up:** `GrantTestKit`, unequip all, frame 8+ cats, then `Equip("*.legendary")` ×4 at 0.5 s → every cat pulses and sparkles with a new pick, hat, pack and boots.
6. **Mythical chest:** `StoreBuy("gems_pile")`, then `StoreBuy("chest_mythical")` on a profile missing cards → 6 card tiles with gold "NEW" plus "+N more". Add the reveal drama in post.
7. **Offline jackpot:** debug `LoadZone(60–90)` with gear, `Clock.Advance(3*3600)` → "While you were away" with a 200 % B/T-scale amount → `ClaimOfflineDouble()` → toast "Income doubled".
8. **Mine-clear finale:** run to `DrainingCargo`, record ×1 → last sacks, cats gather and jump, exit glows, exit chest, then "Mine N Results" and "Next mine" with the fade.
9. **Max-buy spree:** `ShowBase()`, `BonusGold`, `mode2` (Max), then click `buy` on Pickaxes / Value / Bags / Unloading → prices tick, bars fill to green "Max", camp buildings swap tiers. Use the half-height window as a split shot.
10. **Big-number climb (mine ~90):** ×1 with full Team and gear; the gold counter rises through "xxB → T" with steady coin flights. Frame the top bar and depot.
11. **Time skip:** `UseTimeSkip("skip_4h")` → bar "The cats work 30 min in a flash…" → result "Gold earned / Cleared N" → cut to the cleared tunnels.
12. **Fresh start plus tutorial paw (Main, no tag):** fixture default, `Seen.Add(StepAuto)` → mentor bubble and paw on a rock, first tap.

B-roll: whole-mine zoom-out (ortho about 30 = 1 px per art pixel, canvas off), the theme montage (mines 10 / 13 / 16), and scrolling the cards collection after `LoadZone(138)` + `GrantTestCards`.

Sources:
- [Unity Scripting API: Time.captureDeltaTime](https://docs.unity3d.com/ScriptReference/Time-captureDeltaTime.html)
- [Unity Recorder vs Systems with Unscaled Time (Unity Discussions)](https://discussions.unity.com/t/unity-recorder-vs-systems-with-unscaled-time/846491)