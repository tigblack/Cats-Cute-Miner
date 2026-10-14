# Reuse map: tests and tools (subsystem `tests-tools`)

Scope: test helpers (`TestData`, `PlayModeUtil`, `CoreGameSmokeTests`), the list of all test files, Editor tools
(`ProjectSetup`, `BalanceValidatorCli` + `BuildAndroid` + `EditorPaths`, `UnityPackageExtractor`), DevTools (`DevPanel`, `LevelCheat`),
`Tools/unity.ps1`, `Tools/CoreTests`, `Tools/UnityCheck`, and the asmdefs. Read-only survey, 2026-10-08. All paths are relative to the project root.

Verdict legend: **REUSE** (as-is) / **ADAPT** (what to change) / **DROP** (not needed in the new mode; delete or keep only as history).

---

## 0. Key answers

| Question | Answer |
|---|---|
| `Assets/Game/Data/Source/json-data.json` on the hub? | **Yes.** 2 254 970 bytes, SHA-256 `8975a5a09dce8e2675d06b751c50b5cac80da3988f8ed098910492c442f85ee5`, matches `BalanceRepository.ExpectedSha256` (`Core/Data/BalanceRepository.cs:16`). Meta: guid `370762ecbf504618ee74507274ddf77b`, `TextScriptImporter`. |
| How tests find the JSON | `TestData.ProjectRoot` walks up from `Directory.GetCurrentDirectory()` and then `AppContext.BaseDirectory` until it finds `Assets/Game/Data/Source/json-data.json`, then reads it with `File.ReadAllBytes` (no AssetDatabase). Works in Unity (cwd = project root or `build/ci`) and in dotnet (bin under `obj/CoreTests/...`). `TestData.cs:19-41`. |
| How the player finds the JSON | **A serialized TextAsset reference in the scene, not Resources.** `GameBootstrap.BalanceJson` (`Runtime/GameBootstrap.cs:12`) is assigned by `ProjectSetup.CreateScene` (`Editor/ProjectSetup.cs:214`) and stored in `CoreGame.unity` (`BalanceJson: {fileID: 4900000, guid: 370762ec…}`). `GameController.Init` calls `BalanceRepository.Load(boot.BalanceJson.bytes)` (`Runtime/GameController.cs:143`) with the hash check on. If it is null, you get a fatal modal. |
| Other config precedent | `Resources.Load<TextAsset>("Visual/environment_themes")` (`Runtime/Visual/EnvironmentThemes.cs:28,49`; file `Assets/Game/Resources/Visual/environment_themes.json`). Both mechanisms work in an Android player. TZ v2.1 §18 forbids AssetDatabase-only loading. |
| Editor validator JSON | `AssetDatabase.LoadAssetAtPath<TextAsset>(ProjectSetup.BalancePath)` (`Editor/BalanceValidatorCli.cs:123`). |
| APK build | `unity.ps1 -Mode BuildApk` → `-buildTarget Android -executeMethod CatMiners.Editor.BuildAndroid.Apk -catminersOutput <root>\Builds\Android`. Before that it checks the Android module and runs Setup (always in Ci; in Real only when the marker or scene is missing). It builds two APKs (dev and noDevTools). Last run: `Docs/Reports/build-android.json` = `ModuleMissing` (SDK, NDK, OpenJDK). |

---

## 1. Assembly definitions (asmdef)

| asmdef | refs | key flags | notes |
|---|---|---|---|
| `Scripts/Core/CatMiners.Core.asmdef` | none + `Newtonsoft.Json.dll` | `noEngineReferences: true`, autoReferenced | **The new simulation must live here** so that EditMode and dotnet tests can reach it. No UnityEngine. |
| `Scripts/Runtime/CatMiners.Runtime.asmdef` | Core, InputSystem, TMP, UGUI + Newtonsoft | overrideReferences | Views, controller, HUD. |
| `Scripts/DevTools/CatMiners.DevTools.asmdef` | Core, Runtime, InputSystem, TMP, UGUI | `autoReferenced: false`, `defineConstraints: ["UNITY_EDITOR \|\| DEVELOPMENT_BUILD"]` | Runtime does **not** reference it, so it self-installs through `[RuntimeInitializeOnLoadMethod]`. Excluded from release APKs, and `BuildAndroid` checks for leaks. |
| `Scripts/Editor/CatMiners.Editor.asmdef` | Core, Runtime, InputSystem, TMP, UGUI + Newtonsoft | Editor only | Setup, validator CLI, APK build. |
| `Tests/EditMode/CatMiners.Tests.EditMode.asmdef` | **Core only**, TestRunner + nunit, Newtonsoft | Editor only, `UNITY_INCLUDE_TESTS` | EditMode tests cannot touch Runtime. rootNamespace `CatMiners.Tests`. |
| `Tests/PlayMode/CatMiners.Tests.PlayMode.asmdef` | Core, Runtime, **DevTools**, TestRunner, InputSystem(+TestFramework), UGUI, TMP | `UNITY_INCLUDE_TESTS` | It can call `LevelCheat.Attach` directly. |

Verdict: **REUSE**. Gotcha: the dotnet templates compile folders by glob (`Core\**`, `Runtime\**`, `Tests\EditMode\**` …). A new asmdef inside one of those folders would split in Unity but not in dotnet. A new top-level `Scripts/<X>` folder would not be compiled by `CoreTests` or `UnityCheck` at all. Keep the new code inside the existing asmdef folders (for example `Scripts/Core/Outpost/`, `Scripts/Runtime/Outpost/`), or update the asmdefs and all `*.csproj.template` files together.

---

## 2. `Assets/Game/Tests/EditMode/TestData.cs` (68 lines)

Shared test data for Unity EditMode and `dotnet test`. Verdict: **REUSE AS-IS**, then add accessors for the new configs.

```csharp
public static class TestData                                             // :11
public const string BalanceRelativePath = "Assets/Game/Data/Source/json-data.json";   // :13
public const string ZonesSummaryRelativePath = "Docs/TZ/analysis/zones_summary.csv";  // :14 (file exists, 33 350 B)
public static string ProjectRoot { get; }                                // :19-39  walk-up search, cached in s_Root
public static byte[] BalanceBytes => File.ReadAllBytes(...);             // :41
public static BalanceRepository Balance { get; }                         // :43-50  lazy, CACHED STATIC (shared by all tests)
public static string ZonesSummaryPath { get; }                           // :53-66  walks above ProjectRoot (build/ci has no Docs)
```

- Coupling: only `CatMiners.Core.Data.BalanceRepository`. There is no old-gameplay coupling.
- ADAPT (additive): add `OutpostConfigBytes` and similar members, built the same way from the project root, for new config files (for example `Assets/Game/Data/Outpost/*.json` or `Assets/Game/Resources/...`). Keep the file paths identical to what the player loads.
- Gotcha: `Balance` is a cached shared instance. Tests that mutate DTOs must load their own copy with `BalanceRepository.Load(TestData.BalanceBytes, requireExpectedHash:false)`, as `DataValidatorFixTests` does.
- Gotcha: in the `build/ci` copy, `ProjectRoot` = `build/ci`, because `Assets/` is mirrored there. `Docs/` is not mirrored, so anything under `Docs/` must be found by walking up (`build/ci` sits inside the real root). `LocalizationTests`, `ValidationReportGenerator` and `BotZones1To10Report` all use `ProjectRoot/Docs/Reports` directly. In Ci that directory does not exist, so they either skip writing (`Directory.Exists` guard) or must use `-catminersReports`.

---

## 3. `Assets/Game/Tests/PlayMode/PlayModeUtil.cs` (283 lines)

Shared PlayMode steps: scene load, real-time wait, temp save dir, UI clicks, screenshots, log watcher.

### Generic, REUSE AS-IS
```csharp
public static string ReportsDir()                                     // :20  -catminersReports arg or Path.GetFullPath("Docs/Reports")
public static string UseTempSaveDir()                                 // :29  sets GameController.SaveDirectoryOverride = temp dir
public static void DropTempSaveDir(string dir)                        // :37  resets override, deletes dir
public static IEnumerator LoadCoreGame()                              // :43  LoadSceneAsync("CoreGame", Single) + 2 frames
public static IEnumerator WaitReal(float seconds)                     // :50  realtimeSinceStartup loop
public static Button FindButton(Component root, string name)          // :56  by GameObject name (incl. inactive)
public static Button FindModalButton(Component root, string label)    // :64  active "btn" with TMP label text
public static void Click(Button button)                               // :76  ExecuteEvents.pointerClickHandler (respects interactable)
public static IEnumerator CloseModals(GameController gc)              // :167 clicks first "btn" while gc.Hud.ModalOpen (max 50)
public static IEnumerator CaptureUi(GameController gc, string file)   // :187 1080x1920 → ReportsDir/screenshots
public static IEnumerator CaptureUi(GameController gc, string file, int height, string subdir) // :193 (CanvasScaler off, ScreenSpaceCamera)
public static void Capture(GameController gc, string file)            // :234 synchronous variant
public sealed class LogWatcher : IDisposable { List<string> Errors; string Report(); }  // :265-282 ignores "Licensing"/"Rider"
```
ADAPT: these need `gc.Hud` (`ModalOpen`, `Refresh()`, `Canvas`/`CanvasScaler` on the Hud GO) and `gc.Rig.Cam`. If the new controller class differs, retype the parameter to the new controller, or to an interface exposing `Hud` and `Rig`.

### Old gameplay, DROP or rewrite for the new mode
```csharp
public static int AssignIdleCats(GameController gc)                  // :83  manual assignment via gc.DropCat + s.Layout/AcceptsCat — DROP (cats auto-assign)
public static List<ContainerState> HireBarrels(GameSession s)        // :107 hire barrels — DROP
public static int OpenHireBarrels(GameController gc)                 // :119 DROP
public static int CollectAllLoot(GameController gc)                  // :131 tap-collect loot — DROP (cargo is carried, not tapped)
public static LootState BreakRockForLoot(GameSession s)              // :147 DebugCompleteTarget on ObjectKind.Rock — DROP
public static GameNumber Ledger(GameSession s, string key)           // :163 Player.Ledger — ADAPT to the new wallet/credit journal
```
Couplings: `using CatMiners.Core.Grid` (ZoneLayout, ObjectKind) and `CatMiners.Core.Sim` (GameSession, ContainerKind, LootState).
New helpers worth adding: `AdvanceSim(ManualClock, seconds, step<=0.5)`, `WaitUntil(Func<bool>, simSeconds)`, `DepotWallet(...)`.

---

## 4. How PlayMode tests drive the game (pattern to keep)

1. `[UnitySetUp]`: `m_SaveDir = PlayModeUtil.UseTempSaveDir()`, `CameraRig.OverrideAspect = 1080f/1920f` for screenshots (or 0), `m_Log = new LogWatcher()`, and optionally `GameController.ClockOverride = new ManualClock(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 100.0)`.
2. `yield return PlayModeUtil.LoadCoreGame()`, then `var gc = GameBootstrap.Controller` (static, set in `GameBootstrap.Awake`).
3. Drive the game only through the controller's UI commands (`gc.Hire()`, `gc.DropCat(...)`, `gc.SwitchSlot(SaveSlots.Debug, true, zone)` …), through `PlayModeUtil.Click(button)`, or through real input via `InputTestFixture` (`PointerInputTests`).
4. Time:
   - With `ClockOverride = ManualClock`, the controller (`GameController.Update`, `Runtime/GameController.cs:441-480`) advances only when the test calls `clock.Advance(dt)`. A frame with mono dt > `MaxFrameSeconds` (1.0) **or** a UTC gap > 1000 ms is processed **once as offline** (`OfflineProgress.Apply`). So step at most 0.5 s per frame (`TutorialFlowTests.FrameSeconds = 0.5`) or at most 0.05 s per frame (`VisualShowcaseTests.RunSim`).
   - `FrameGapIsProcessedAsOfflineOnce` (`CoreGameSmokeTests.cs:326`) is the reference test for clock semantics.
   - Without an override, `SystemClock` (`Runtime/SystemClock.cs`: `DateTimeOffset.UtcNow`, `Time.realtimeSinceStartupAsDouble`) is used, and `gc.SimSpeed` speeds things up **only in the Debug slot** (`GameController.cs:464`).
5. `[UnityTearDown]`: dispose the LogWatcher, reset `CameraRig.OverrideAspect = 0`, `GameController.ClockOverride = null`, `SimSpeed = 1` and `DropTempSaveDir`. Statics leak between tests otherwise.
6. Final assert: `Assert.IsEmpty(m_Log.Errors, m_Log.Report())`.

Static test seams on the controller (`Runtime/GameController.cs`): `public static string SaveDirectoryOverride` (:31), `public static IClock ClockOverride` (:34), `public static bool ClassicRulesOverride` (:40, old rules, DROP), `public static long UtcNowMs()` (:109). Clock types live in Core: `IClock { long UtcNowMs; double MonotonicSeconds; }` and `ManualClock(long utcNowMs, double monotonicSeconds = 0.0)` with `Advance(double seconds)` (`Core/Sim/Clock.cs:9-41`). These are **REUSE AS-IS** and generic. The new controller should keep the same three static seams and the same frame/offline split rule (TZ v2.1 §13 / §20 "Разбиение времени").

Batchmode gotchas from comments:
- A frame delay > 1 s in batchmode (first shader compile) would count as offline and open "Пока вас не было", so most tests stop the clock (`PointerInputTests.cs:52`, `EdgeIndicatorTests.cs:218`, `LevelCheatTests.cs:25`).
- The window has no focus in batchmode, so set `InputSystem.settings.backgroundBehavior = IgnoreFocus` (`PointerInputTests.cs:72`).
- A previous scene's input stays enabled, so disable `gc.Input` and the UI module before `InputTestFixture.Setup` (`PointerInputTests.cs:26-45`). Give the UI module `Object.Instantiate(module.actionsAsset)`.
- `WaitForEndOfFrame` never fires in batchmode (`GameplayRecordingTests.cs:85`).
- PlayMode runs **without** `-nographics` because screenshots need a GPU.
- `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` runs once per domain, not per `LoadSceneAsync`. In tests, DevPanel and LevelCheat are therefore not auto-installed on the test-loaded scene; tests call `LevelCheat.Attach(gc)` explicitly.
- `LoadSceneAsync("CoreGame")` by name requires the scene in Build Settings, which Setup writes.

---

## 5. `Assets/Game/Tests/PlayMode/CoreGameSmokeTests.cs` (591 lines, 10 tests)

Acceptance smoke for CoreGame: boot without exceptions, HUD buttons change the sim, save/restart, frame gap = offline once, corrupted save, campaign end, chest queue, debug zones.
Verdict: **ADAPT** the skeleton (SetUp/TearDown `:28-46`, restart idiom `LoadCoreGame` → `GameBootstrap.Controller` → `Assert.AreNotSame(gc, gc2)`); rewrite the bodies.

| Test (line) | Old coupling | New-mode equivalent |
|---|---|---|
| `CoreGameRunsAndPlays` :49 | `gc.Hire`, barrels, `DropCat`, `Mine.RocksDestroyed`, `SimSpeed` Main vs Debug | TZ §20 "Старт без действий": two cats deliver the first coins with no input; ×20 only in the debug profile. |
| `HudButtonsChangeSimulation` :107 | Hire button, forge `ctx0` +1 upgrade (`UpgradeCost`, `Gen.Level`) | Base panel: buying a team upgrade changes effective stats. |
| `StateSurvivesSceneRestart` :165 | cats/targets/HireCount/NextOpId/camera `FocusZ` | Restart mid-delivery: position and cargo are consistent, no double credit. |
| `HireBarrelAndLootSurviveRestart` :214 | barrels, `BreakRockForLoot`, Ledger `gold:rock` | Cargo on the ground and in bags survives restart. |
| `CameraPositionRestoredOnLoad` :287 | `Rig.FocusOn/FocusZ/HomeFocusZ`, `CompleteZone` | **REUSE** camera part; drop zone completion via gates/shafts. |
| `FrameGapIsProcessedAsOfflineOnce` :326 | `Player.PlayerTimeTicks`, `State.LastProcessedUtcMs` | **REUSE logic** (rename fields). This is the TZ §20 clock-back/no-double-credit check. |
| `CorruptedSaveOffersRestartWithoutDestroyingFiles` :371 | `save_main.json`, `*.corrupt-*`, modal "Начать заново" | **REUSE**. File names change if the new mode saves to new files. |
| `CampaignEndScreenOpensOnRelaunch` :394 | `Player.CampaignCompleted`, `HighestCompletedZone`, `Hud.CampaignEndVisible` | ADAPT ("Последняя шахта"). |
| `QueuedChestsSurviveRestartUntilClaimed` :421 | `ChestReveals`, gacha IDs, "Забрать", `ChestsBadge` | ADAPT into "Повторное открытие итогов" (stage reward is not re-issued). |
| `DebugZonesRunWithoutExceptions` :482 `[Timeout(900000)]` | zones 5/10/50/100/144, `DebugGiveZoneCards`, `DebugSpawnCat`, `AssignIdleCats`, `ToolsStep` and `FinishZone` (gates/shafts/exit/payouts) | ADAPT: run zones 5/10/50/101/144 in the debug profile at ×100 with **no instant destruction**, and assert progress and no log errors. |

Helpers `CloseOfflineWindows` :534 (label "Отлично"), `ToolsStep` :549 and `FinishZone` :572 are old (gates, shafts, payouts) and should be DROPPED.

---

## 6. All test files: one-line purpose and old-gameplay dependency

Count = number of `[Test]`/`[UnityTest]` attributes. "OLD" means the file depends on GameSession/ZoneLayout/merge/cards/generators/cannon/barrels.

### EditMode (`Assets/Game/Tests/EditMode`, also run by dotnet)
| File | Tests | Purpose | Old? | Verdict |
|---|---|---|---|---|
| AutoMiningTests.cs | 5 | Old auto-mining (a cat hits adjacent targets from its own cell) | OLD | DROP |
| BarrelLootTests.cs | 13 | Hire barrels and loot piles in zone1 | OLD | DROP |
| BigNumberAcceptanceTests.cs | 12 | Big costs via the session (sunstone, 1040-1200) | OLD (session) | DROP; rewrite for the exact-integer layer (§12.2, >2^53) |
| CannonTests.cs | 3 | Forge 2×3 and cannon geometry | OLD | DROP |
| CoreRulesFixTests.cs | 24 | Drag/drop, spawn BFS, ID data, offline automation | OLD | DROP (ZoneRecorder helper pattern only) |
| DataAndLayoutTests.cs | 12 | `BalanceLoadTests` (hash, counts, zone order, big literals, wrong hash) + `LayoutTests` (ZoneLayout.Build, CSV match, Appendix A) | partly | BalanceLoadTests REUSE (drop `CardMaxLevels`); LayoutTests ADAPT if the new importer reuses ZoneLayout geometry (`MatchesZonesSummaryCsv` is valuable) |
| DataGeometryHpAcceptanceTests.cs | 32 | TZ 17.1 groups 1-3: import/validator (G1), geometry (G2), HP (G3) | partly | G1 mostly REUSE; G2 ADAPT (merge zone/forge rows are old); G3 DROP (new HP §12.4) |
| DataValidatorFixTests.cs | 12 | JSON load/validator rules: one tinygoblin miner, integer weights, FT0 invariants, currency IDs | data-level | REUSE while BalanceRepository/BalanceValidator stay |
| DeliveryAcceptanceTests.cs | 10 | Old deliveries (E-15) | OLD | DROP |
| EconomyReportTests.cs | 5 (1 Explicit) | EconomyTables C.4 pace | OLD | DROP |
| FormulaAppendixCAcceptanceTests.cs | 33 | Appendix C control values via GameSession/GachaService | OLD | DROP |
| HireMergeAcceptanceTests.cs | 15 | Hire and merge | OLD | DROP |
| LocalizationTests.cs | 14 | Loc dictionary, key registration, **no Cyrillic literals in Runtime + Core UI files** | partly | ADAPT: `CoreUiFiles()` :276 hardcodes `Core/Sim/GameSession*.cs` + `NumberFormat.cs`; add new Core text files. Core-text tests use CommandResult/threshold events |
| MergeZoneTests.cs | 6 | Merge zone rows | OLD | DROP |
| NumberAndFormulaTests.cs | 13 | `GameNumberTests` (arith, beyond double, round-trip, format) + `FormulaControlValueTests` (E-02…E-16) | partly | GameNumberTests REUSE; FormulaControlValueTests DROP |
| OfflineAcceptanceTests.cs | 9 | Offline = online, 1/60 frames, "Пока вас не было" | OLD (session) | DROP; re-create the same invariants for the new sim |
| RewardChestAcceptanceTests.cs | 12 | RewardCycle, chests, card counts | OLD | DROP |
| SaveHardeningTests.cs | 16 (1 Explicit) | NaN rejection, byte-identical envelope, quarantine, migrations, ownership table, future schema; `RecordingFs` fault fs; fixture `Tests/Fixtures/Saves/save_v1_main.json` | partly (GameState) | ADAPT: patterns and fs-injection are reusable for the new schema (`modeId=CatOutpostV2`, schema 1) |
| SavePerformanceTests.cs | 1 | zone93 snapshot ≤ 10 ms median, allocations; writes `save-perf.json`; uses `CATMINERS_DOTNET_TESTS` | partly (HeavySession) | ADAPT (new heavy state: 12 cats, large map) |
| ShaftGateAcceptanceTests.cs | 9 | Shafts and gates | OLD | DROP |
| SimulationTests.cs | 31 (1 Explicit) | Old sim + `ScenarioBotTests` (`BotZones1To10Report` writes bot-*.md); `FailingFs` helper | OLD | DROP (FailingFs pattern) |
| TestData.cs | 0 | Shared data | no | REUSE |
| TransactionAcceptanceTests.cs | 21 | Double click, repeated opId, interrupted write; `OpTracker`, `CrashingFs` | OLD | DROP bodies; keep CrashingFs pattern for "Аварийная запись" |
| TransitionAcceptanceTests.cs | 11 | Zone transition 11.2/11.3, campaign end | OLD | DROP; rewrite for §11 |
| ValidationReportGenerator.cs | 5 (1 Explicit `WriteValidationReport`) | Builds `Docs/VALIDATION_REPORT.md` from NUnit XML + json reports; 13 `Criterion` entries bound to old test names | partly | ADAPT: reuse `ReadNUnit`/`ReadJson`/`Status`; write a new criteria table and output `VALIDATION_V2.md`; never present old reports as new |

### PlayMode (`Assets/Game/Tests/PlayMode`)
| File | Tests | Purpose | Old? | Verdict |
|---|---|---|---|---|
| CannonShotTests.cs | 1 | Cannon shot animation on hire | OLD | DROP |
| CoreGameSmokeTests.cs | 10 | See §5 | OLD bodies | ADAPT |
| EdgeIndicatorTests.cs | 3 | Edge arrows to ready shafts, gate bonus labels | OLD | ADAPT only if edge arrows survive (for example toward cargo or base); otherwise DROP |
| GameplayRecordingTests.cs | 1 Explicit | ~45 s AVI (MJPEG+PCM) to `build/recording/gameplay.avi`; `AviWriter.Write(path, frames, w, h, fps, audio, ch, rate)` :305, `AviWriter.Check` :446, `AudioTap` :268 | script OLD | ADAPT: the AVI/audio tooling is reusable for the 60-90 s recording required by TZ §22; rewrite the script |
| LevelCheatTests.cs | 1 | Cheat jump to a zone in Debug, finish zone → next | OLD (gates) | ADAPT with LevelCheat |
| PerformanceTests.cs | 1 | zone5/93/117 frame times, GC per frame, ProfilerRecorder probe → `perf.json` | scenario OLD | ADAPT: the harness (`Run`/`SteadyRun`/`SimAlloc` :80-110) is reusable; new load = 12 cats, large map, 2 depot slots (§19) |
| PlayModeUtil.cs | 0 | See §3 | partly | ADAPT |
| PointerInputTests.cs | 10 | Real Input System gestures: tap, drag-assign, merge, scroll, UI gesture, touch cancel, action map, dynamite | mostly OLD | Keep `BackgroundDragAndWheelScroll`, `GestureStartedOnUiDoesNotTouchBoard`, `PointerInputReadsGameplayActionMap`, `DisableReleasesGameplayMapAndEnableRestoresIt`; DROP drag/merge/dynamite/barrels |
| TutorialFlowTests.cs | 4 | Old zone1 tutorial (hire → barrel → drag → loot → merge → forge) | OLD | DROP; the new tutorial (§15) reuses the ManualClock-step pattern |
| UiScreenshotTests.cs | 4 | `ui_*.png` screenshots of HUD/context/windows/campaign end | OLD screens | ADAPT to new screens (base panel, equipment, stage results) |
| VisualAssetCatalogTests.cs | 1 | Writes `Docs/Visual/VISUAL_ASSET_CATALOG.csv` (source ID → atlas sprites) | light (PixelArt.Has) | ADAPT for new IDs (bags, depot, equipment) |
| VisualPolishTests.cs | 9 | Theme config, atlas completeness, cat frames per coat/helmet/weapon, shaft states, volume groups | partly (shaft states, level→equipment cycle) | Theme/atlas/volume tests REUSE; `ShaftStatesAreVisiblyDistinct` DROP; `WeaponAndHelmetFollowEightLevelCycle` and `CatsShowWeaponAndHelmetOfTheirLevel` ADAPT (looks come from equipment, not cat level) |
| VisualShowcaseTests.cs | 1 | Zones 1,2,5,10,50,101,144 at 9:16/9:19.5/9:20 → `Docs/Reports/visual/` | light (`DebugSpawnCat`, `AssignIdleCats`) | ADAPT |

Last recorded runs: `Docs/Reports/playmode-results.xml` total=50 passed=47 failed=0 (old game). These are historical, not evidence for the rework.

---

## 7. Adding new EditMode tests that also run in dotnet (`CATMINERS_DOTNET_TESTS`)

- Put the file anywhere under `Assets/Game/Tests/EditMode/`. `Tools/CoreTests/Tests/Tests.csproj.template:10` compiles `..\..\..\Assets\Game\Tests\EditMode\**\*.cs`, and `CoreLib.csproj.template:9` compiles `Assets\Game\Scripts\Core\**\*.cs` (netstandard2.1). There is no registration step.
- Use `namespace CatMiners.Tests` and NUnit 3 (`[Test]`, `[TestCase]`, `[Category]`, `[Explicit("…")]` for report generators). Reference **only** `CatMiners.Core.*`, NUnit, Newtonsoft and BCL. In dotnet no UnityEngine is referenced; in Unity the EditMode asmdef only references Core.
- `CATMINERS_DOTNET_TESTS` is defined only in the dotnet test project (`Tests.csproj.template:7`). Use `#if CATMINERS_DOTNET_TESTS` / `#else` for environment-specific bits. The only current use is `SavePerformanceTests.cs:56` (`EnvironmentName` "dotnet"/"unity"). Do not use it in Core code: CoreLib does not define it.
- Language: `LangVersion 9.0`, `Nullable disable`, `ImplicitUsings disable` (`Tools/CoreTests/Directory.Build.props:8-10`). No C# 10+ features (file-scoped namespaces, global usings, record structs). The codebase has no `IsExternalInit` polyfill, so avoid `init` accessors and `record` types, or add the polyfill in Core.
- Reports: resolve the directory as `-catminersReports` arg ?? `TestData.ProjectRoot/Docs/Reports` (copy `SavePerformanceTests.ReportsDir()` :47-53).
- Draft tests outside Unity: `Tools/CoreTests/Drafts/<Name>.cs` (currently empty) with `run.ps1 -Draft <Name>.cs` (`Tests.csproj.template:13-15`). Unity does not see them.
- `.meta` files: new files and folders under `Assets/Game` need `.meta`. `python3 Tools/gen_meta.py` (project root) creates deterministic md5-of-path GUIDs, so the PC editor, `build/ci` and the hub agree. Existing metas are untouched.
- Run: `powershell -ExecutionPolicy Bypass -File Tools/CoreTests/run.ps1 [-Filter "FullyQualifiedName~CatMiners.Tests.Outpost"] [-Instance x]`. The filter uses `dotnet test --filter` syntax (`Name=`, `FullyQualifiedName~`, `TestCategory=`). Exit code = dotnet's.

---

## 8. `Tools/CoreTests/run.ps1` and templates

Fast Core build plus EditMode tests without Unity. **REUSE AS-IS.**
- `param([string]$Filter='', [string]$Instance='', [string]$Draft='')` (:8). `Sync-Template` copies `*.csproj.template` → `*.csproj` only when the content differs (:11-19). `*.csproj` is in the Mutagen ignores, which is why templates exist.
- `dotnet test Tests\Tests.csproj --nologo -v q --logger console;verbosity=normal [--filter] [-p:CoreTestsInstance=] [-p:CoreTestsDraft=]` (:21-25); noise filtered.
- `Directory.Build.props`: obj/bin → `<root>\obj\CoreTests[-<inst>]\<proj>\` (root `/obj` is not synced and not seen by Unity). `-Instance` allows parallel runs.
- `CoreLib.csproj.template`: netstandard2.1, AssemblyName `CatMiners.Core`, Newtonsoft 13.0.3. `Tests.csproj.template`: net8.0, NUnit 3.14.0, NUnit3TestAdapter 4.6.0, Microsoft.NET.Test.Sdk 17.11.1.
- ADAPT only if new config files must be embedded or copied. Today tests read files from the project root, so nothing is needed.

## 9. `Tools/UnityCheck/run.ps1` and templates

Compile-only check of Runtime, DevTools, Editor and PlayMode tests in seconds with dotnet build. **REUSE AS-IS.**
- `param([string]$Instance='', [ValidateSet('All','Runtime','DevTools','Editor','PlayModeTests')]$Project='All')` (:8).
- Needs `build\ci\Library\ScriptAssemblies` and `PackageCache` (:14-16). That folder exists only after a **Ci-target** Unity run (`unity.ps1 -Mode Compile -Target Ci`, or Auto when the editor is open). A Real-target Compile does not create it.
- Unity managed DLLs come from `C:\Unity\Editor\Data\Managed`, or `$env:UNITY_EXE`'s `Data\Managed` (:12-13). NUnit is taken from PackageCache `*unity-custom*`, Newtonsoft (non-AOT) from PackageCache.
- Defines (`Directory.Build.props:15`): `UNITY_EDITOR;UNITY_INCLUDE_TESTS;UNITY_2021_3_OR_NEWER;UNITY_2022_3_OR_NEWER;UNITY_6000_0_OR_NEWER;DEVELOPMENT_BUILD;ENABLE_INPUT_SYSTEM`. The **player/non-editor branches (`#if !UNITY_EDITOR`) are not compiled** here; only `unity.ps1 -Mode Compile` / BuildApk prove them.
- Templates: `Runtime` (Scripts\Runtime\**), `DevTools` (Scripts\DevTools\** → Runtime), `Editor` (Scripts\Editor\** + UnityEditor.*.dll), `PlayModeTests` (Tests\PlayMode\** + DevTools + InputSystem.TestFramework + TestRunner + NUnit). EditMode tests are not compiled here; CoreTests covers them.
- Output: `[UnityCheck] <proj>: OK|ОШИБКИ`; exit code = number of failed projects. It does not prove scene start, IL2CPP or input (TZ §18).

---

## 10. `Tools/unity.ps1`: headless Unity

`param([ValidateSet('Compile','EditMode','PlayMode','Validate','Setup','BuildApk')]$Mode='Compile', [ValidateSet('Auto','Real','Ci')]$Target='Auto', [string]$UnityExe=$env:UNITY_EXE, [string]$TestFilter='', [ValidateSet('','Android','StandaloneWindows64')]$BuildTarget='', [switch]$NoSync)` (:24-34).

- Unity lookup (:42-58): `UNITY_EXE`, then `C:\Program Files\Unity\Hub\Editor\<ver>\Editor\Unity.exe`, `C:\Unity\<ver>\…`, `C:\Unity\Editor\Unity.exe`. The version comes from `ProjectSettings/ProjectVersion.txt` and ProductVersion must match.
- Target: `Auto` = `Real` unless `Temp\UnityLockfile` is locked (editor open), else `Ci`. Ci = `build\ci` mirror (robocopy `/MIR` of Assets, Packages, ProjectSettings; own Library) (:72-81). `build` is ignored by Mutagen anywhere, so the mirror and Library stay on the PC.
- Common args (:140): `-batchmode -projectPath <p> -logFile Docs\Reports\unity-<mode>.log -catminersReports <root>\Docs\Reports`. The log is written to the **real** project's Docs/Reports even for Ci, and Docs is synced to the hub.

| Mode | Unity args | Entry point | Outputs |
|---|---|---|---|
| Compile | `-nographics -quit` | (import/compile) | `unity-compile.log` → copied to `compile.log`; fails if `error CS` appears even with exit 0 (:198-211) |
| EditMode | `-nographics -runTests -testPlatform EditMode -testResults editmode-results.xml [-testFilter "<f>"]` | Test Runner | `editmode-results.xml`, summary + first 8 lines of each failure printed (:182-197) |
| PlayMode | `-runTests -testPlatform PlayMode -testResults playmode-results.xml [-testFilter]` (**no -nographics**) | Test Runner | `playmode-results.xml`, screenshots under `Docs/Reports/screenshots`, `visual/`, `perf.json` |
| Validate | `-nographics -quit -executeMethod CatMiners.Editor.BalanceValidatorCli.Run` | `BalanceValidatorCli.Run` | `validation.json`, `validation.txt`; exit 0 ok / 2 errors / 1 crash |
| Setup | `-quit -executeMethod CatMiners.Editor.ProjectSetup.RunBatch` | `ProjectSetup.RunBatch` | `unity-setup.log`; exit 1 + "activeInputHandler" in log → automatic second run (:126-136) |
| BuildApk | `-quit -buildTarget Android -executeMethod CatMiners.Editor.BuildAndroid.Apk -catminersOutput <root>\Builds\Android` | `BuildAndroid.Apk` | `Builds/Android/CatMiners-<bundleVersion>-dev.apk` and `CatMiners-<bundleVersion>.apk`, `Docs/Reports/build-android.json` (enriched by ps1 with `missingScriptLines`, `logCheck`, `exitCode`) |

- `-TestFilter` is passed verbatim to Unity `-testFilter` (semicolon-separated names, or a regex over the full test name, for example `"CatMiners.Tests.Outpost"`). There is no `-testCategory` switch in the script; add one if needed.
- `-BuildTarget` (not for BuildApk) adds `-buildTarget X` for measurements with the Android quality profile (:160).
- Pre-steps (:226-257):
  - BuildApk first checks the Android module (`Data\PlaybackEngines\AndroidPlayer\{SDK,NDK,OpenJDK}` or EditorPrefs `AndroidSdkRoot`/`AndroidNdkRoot`/`JdkPath` in `HKCU:\Software\Unity Technologies\Unity Editor 5.x`). If missing, it writes `build-android.json` `ModuleMissing` and exits **3**.
  - Ci + (PlayMode or BuildApk) → always Setup first.
  - Real + (PlayMode or BuildApk) → Setup only if `ProjectSettings\CatMinersSetup.json` **or** `Assets\Game\Scenes\CoreGame.unity` is missing (`Test-ProjectSetUp` :120-123). **It does not compare the Setup version.**
- BuildApk post-check (:260-295): lines matching `the referenced script|missing script|missing (mono script)|script … is missing` → exit **4**. No report → `NoReport` and exit 1.
- Log scan (:167-181) prints `error CS` lines and `(Exception|Error):` lines (except Licensing/Rider/pipe).
- ADAPT: hardcoded `CoreGame.unity` (:122) and the marker path. Keep the scene path or update them together (§11). Consider adding a Setup-version check to `Test-ProjectSetUp` so the Real target re-runs Setup after a bump.

---

## 11. `Assets/Game/Scripts/Editor/ProjectSetup.cs` (425 lines)

Idempotent project setup: folders, TMP Essentials, 4 URP materials, CoreGame scene, input asset as project-wide actions, PlayerSettings, Build Settings, marker. Menu `Cat Miners/Setup Project`; batch `RunBatch`; `AutoSetup` runs once in an open editor.

Public API (REUSE):
```csharp
public const int Version = 3;                                           // :26  bump → scene re-created by AutoSetup
public const string ScenePath = "Assets/Game/Scenes/CoreGame.unity";    // :27  used by BuildAndroid, unity.ps1, PlayModeUtil("CoreGame")
public const string BalancePath = "Assets/Game/Data/Source/json-data.json"; // :28 used by BalanceValidatorCli
public const string MaterialsDir = "Assets/Game/Art/Materials";         // :29
public const string InputActionsPath = "Assets/Game/Input/CatMinersInput.inputactions"; // :30 (maps Gameplay + UI)
public const string MarkerPath = "ProjectSettings/CatMinersSetup.json"; // :33 currently {"version": 3}
public const string CompanyName="CatMiners", ProductName="Cats Cute Miner", BundleId="com.catminers.prototype"; // :34-36
[MenuItem("Cat Miners/Setup Project")] public static void RunFromMenu() // :41-53
public static void RunBatch()                                            // :56  EditorApplication.Exit(0|1)
public static int InstalledVersion()                                     // :78  regex on marker
public static bool IsUpToDate() => InstalledVersion() == Version && File.Exists(ScenePath); // :85
public static bool Run(bool force)                                       // :88  returns "restart needed" (activeInputHandler)
```

What `Run` does (:88-107), in order:
1. `EnsureFolderPath("Assets/Game/" + Scripts|Data|Scenes|Prefabs|Art|Art/Materials|Audio|Tests|Input)`.
2. `ImportTmpEssentialsIfMissing` (via `UnityPackageExtractor`).
3. `EnsureMaterial` ×4: `CatMiners_Lit.mat` (URP/Lit), `CatMiners_Unlit.mat` (URP/Unlit), `CatMiners_Particles.mat` (URP/Particles/Unlit), `CatMiners_Sprite.mat` (URP/2D/Sprite-Unlit-Default).
4. `LoadInputActions` throws if the asset or the `Gameplay`/`UI` maps are missing.
5. `CreateScene` if `force || outdated || !exists`. Batch and menu always pass `force:true`; AutoSetup passes `false`.
6. `ApplyPlayerSettings`.
7. `EditorBuildSettings.scenes = { ScenePath }`.
8. `AssignProjectWideActions` (deletes the template `Assets/InputSystem_Actions.inputactions` after a confirmed assignment).
9. `RemoveTemplateLeftovers`.
10. Write the marker, `SaveAssets`, `Refresh`.

### Generated CoreGame scene (`CreateScene` :172-229)
- `Main Camera` (tag MainCamera): `Camera` ortho size 8, SolidColor `(0.07,0.06,0.08)`, `AudioListener`, pos `(3.5,30,-20)`, rot `Euler(CameraRig.Pitch=90,0,0)`. CameraRig takes over at runtime.
- `Directional Light`: intensity 1.15, color `(1,0.93,0.82)`, soft shadows, rot `(52,-28,0)`.
- `EventSystem`: `EventSystem` + `InputSystemUIInputModule`. `AssignUiActions` (:235-267) binds Point, Click, RightClick, MiddleClick, ScrollWheel, Navigate, Submit, Cancel, TrackedDevicePosition and TrackedDeviceOrientation from the `UI` map, using the importer's sub-asset `InputActionReference`s (not temp ones; references are reset to null first).
- `HUD` (`CreateHudCanvas` :277-298): `Canvas` ScreenSpaceOverlay sortingOrder 10, `CanvasScaler` 1080×1920 match 0.5, `GraphicRaycaster`; child `SafeArea` (stretch) + `SafeArea` component. Hud content is built at runtime (`Hud.Create`), and DevPanel/LevelCheat look up `Hud.transform.Find("SafeArea")`.
- `Bootstrap` + `GameBootstrap` with **exactly these fields assigned**: `BalanceJson` (TextAsset at BalancePath), `LitMaterial`, `UnlitMaterial`, `ParticleMaterial`, `SpriteMaterial`, `InputActions`. A null BalanceJson logs an error.
- Batchmode or single-scene mode: `NewScene(EmptyScene, Single)`. In an editor with other scenes open: additive create, then close (:174-186, 222-227).

### PlayerSettings (`ApplyPlayerSettings` :301-321), overwritten on every Setup
company and product, bundle ID for Android/iOS/Standalone, **`bundleVersion = "0.1.0"`**, `Android.bundleVersionCode = 1`, Portrait only, IL2CPP Android, ARM64, minSdk 25, targetSdk Auto, managed stripping Minimal, `activeInputHandler = 1` via SerializedObject (:327-348; returns restart = true when it changes it).

### Dependencies on old gameplay
Only indirect: the scene hosts `GameBootstrap`, whose `Awake` unconditionally builds the old `GameController` (`Runtime/GameBootstrap.cs:25-33`). Other references: `CameraRig.Pitch`, `PointerInput.GameplayMapName`, `SafeArea` (all Runtime, generic). The setup code itself contains no merge, card or generator logic.

### What Setup must change so the NEW mode is default (ADAPT)
1. **Bootstrap**: make `GameBootstrap.Awake` create the new-mode controller (or add a `GameMode Mode = Outpost` field set by Setup). TZ §21 says not to run two economies. Keep `GameBootstrap.Controller` (or add an equivalent static), because PlayMode tests, DevPanel and LevelCheat read it.
2. **New configs reachable in the player**:
   - Option (a): add `public TextAsset OutpostBalanceJson, EquipmentCatalogJson, StageRewardJson;` (and so on) to `GameBootstrap` and assign them in `CreateScene` next to `:214` with `AssetDatabase.LoadAssetAtPath<TextAsset>(...)`, logging an error when null.
   - Option (b): put them under `Assets/Game/Resources/...` and load them with `Resources.Load<TextAsset>`, following the EnvironmentThemes precedent.
   - Add the paths as constants in `ProjectSetup` next to `BalancePath` so `BalanceValidatorCli` and `TestData` share them. `json-data.json` stays unchanged.
3. **Bump `Version` to 4** (and update the comment at :25). AutoSetup then regenerates the scene in open editors. For the Real target, run `unity.ps1 -Mode Setup` once, because `Test-ProjectSetUp` does not check the version.
4. Change `bundleVersion` in `ApplyPlayerSettings` (:308; for example `"0.2.0"`) so the rework APK names differ (`CatMiners-0.2.0[-dev].apk`). Setup overwrites any manual change.
5. Keep `ScenePath`. If you rename it, update `ProjectSetup.ScenePath`, `unity.ps1:122`, `PlayModeUtil.LoadCoreGame` (:45 scene name) and `GameBootstrap` docs. BuildAndroid uses `ProjectSetup.ScenePath`.
6. `GameFolders` (:39): add new data folders if needed (for example `Data/Outpost`).
7. Never hand-edit `CoreGame.unity`. The Ci target re-runs Setup with `force:true` before every PlayMode or BuildApk run, so anything not generated by `CreateScene` is lost.

`AutoSetup` (:375-424, `[InitializeOnLoad]`): skipped in batchmode. It defers while playing, compiling or updating, runs `Run(force:false)` only when `!IsUpToDate()`, and opens CoreGame when the active scene is empty or `SampleScene`. **REUSE.**

---

## 12. `Assets/Game/Scripts/Editor/BalanceValidatorCli.cs` (598 lines, 4 types)

### `EditorPaths` (:21-43): REUSE AS-IS
```csharp
public static string ReportsDir() => Argument("-catminersReports") ?? Path.GetFullPath("Docs/Reports");
public static string OutputDir() => Argument("-catminersOutput") ?? Path.GetFullPath("Builds/Android");
public static string Report(string file)      // creates dir, returns path
public static string Argument(string name)    // Environment.GetCommandLineArgs lookup
```
### `JsonText` (internal static, :46-88): REUSE
Dependency-free JSON writer: `Str(string)`, `Num(long|ulong)`, `Bool`, `Array(IEnumerable<string>)`.

### `BalanceValidatorCli` (:95-141): ADAPT
```csharp
[MenuItem("Cat Miners/Validate Balance")] public static void RunFromMenu()
public static void Run()      // -executeMethod target; Exit(0 ok | 2 errors | 1 crash)
static int Validate(out string summary)  // AssetDatabase TextAsset → BalanceRepository.Load → BalanceValidator.Validate
```
- It writes `validation.txt` (`report.ToText()`) and `validation.json` {sha256, sizeBytes, textAssetBytes, zones, errors, warnings, unityVersion, checkedAtUtc}.
- Coupling: `BalanceValidator` is the old geometry/data validator (merge zone, forge and so on belong to the old layout rules; see the core map).
- ADAPT: also run the **new** importer/accessibility validator over all 144 zones (TZ §20), load the new configs, and add `modeId`, rules/balance versions and config hashes to the json. Consider separate `validation-v2.json`, since old reports are historical.

### `BuildAndroid` (:155-597): REUSE AS-IS
```csharp
public const string ReportFile = "build-android.json"; DevToolsAssembly = "CatMiners.DevTools.dll"; RuntimeAssembly = "CatMiners.Runtime.dll"; ExitModuleMissing = 3;
public static void Apk()                                  // -executeMethod target; Exit(code)
public static List<string> MissingAndroidModules()        // :476 AndroidPlayer / SDK / NDK / OpenJDK (AndroidExternalToolsSettings via reflection)
```
- Flow (`Run` :206-267):
  1. Module check → `ModuleMissing` (3).
  2. If the scene is missing → `SceneMissing` (1).
  3. `buildAppBundle=false`, debug keystore (a custom keystore is temporarily disabled).
  4. Variants `development` (`BuildOptions.Development`) and `noDevTools` (`None`). Per variant (`BuildVariant` :269-377):
     - optional `PlayerBuildInterface.CompilePlayerScripts` into `Temp/CatMinersPlayerScripts/<variant>`: `error CS` → `ScriptCompileFailed`; DevTools in noDevTools → `DevToolsLeak`; an exception is only a warning;
     - then `BuildPipeline.BuildPlayer(scenes = { ProjectSetup.ScenePath })`, error collection from steps, a DevTools check in `report.GetFiles()`, size and SHA-256.
  5. The JSON lists the scripting backend, architectures, app ID and bundleVersion.
- Gotchas:
  - Only CoreGame is built.
  - DevTools exclusion relies on the asmdef `defineConstraints`. Any Runtime → DevTools reference breaks it.
  - `SameAssembly` (:411) avoids `GetFileNameWithoutExtension`, which would cut `.DevTools`.

---

## 13. `Assets/Game/Scripts/Editor/UnityPackageExtractor.cs` (122 lines): REUSE AS-IS

Synchronous `.unitypackage` (gzip+tar) extractor. `AssetDatabase.ImportPackage` is async and does not finish before `-quit` in batchmode.
```csharp
public static int Extract(string packagePath, string projectRoot, bool overwrite = false)  // :23 returns files written; only "Assets/" paths; writes .meta
```
Used only by `ProjectSetup.ImportTmpEssentialsIfMissing` (TMP Essential Resources from `com.unity.ugui/Package Resources`). There is no gameplay coupling.

---

## 14. `Assets/Game/Scripts/DevTools/DevPanel.cs` (880 lines)

Development panel (Editor and Development Build only; tools work only in the Debug slot): DEV toggle, zone ±1/±10 jump, slot switch, player validator, speed, resource gives, cat spawn, target completion, cell grid overlay, formula chains.

Install and structure:
- `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)] static void Install()` :48 (needs `GameBootstrap.Controller`).
- `Build()` :58-115 (uses `Ui.Button/Panel/Rect/VList/Text/Layout`, `Palette`; legacy Ui kit, **not** PixelUi).
- `Row`/`Btn` helpers :149-169.
- `public const string PlayerValidationFile = "validation-player.json"` :21.

| Part (lines) | Old coupling | Verdict |
|---|---|---|
| Toggle, panel and rows shell :58-169 | none | ADAPT: re-skin with PixelUi (style decision 2026-10-06) or keep as dev-only |
| Zone ±/Goto :74-79, 171-187 | `Session.IsDebugSlot`, `Session.DebugGotoZone`, `RebuildBoard`, `SwitchSlot(SaveSlots.Debug,true,zone)` | ADAPT to the new debug profile (TZ §19: "выбор зоны, отдельный тестовый профиль") |
| New Debug / Main slot :81-83 | `SwitchSlot` | ADAPT ("отдельный тестовый профиль", which must not touch the normal save) |
| Speed ×1/×5/×20/×100 :86-91, 189-198; forced ×1 outside Debug :776-779 | `SimSpeed`, `IsDebugSlot` | REUSE (exactly TZ §19) |
| +gold `RichGold` :238-283 | shafts/forge `UpgradeCost`, `IsGenerator`, `MaxGeneratorLevel` | DROP (rewrite: "+local gold" sized from the next team-upgrade price) |
| +elixir / +dynamite / zone cards :95-97 | Elixir, dynamite, cards | DROP |
| Cat level ± / +cat :99-102 | `DebugSpawnCat(level)` (merge levels) | DROP (the new team size comes from an upgrade branch) |
| Complete target :105-108 | `DebugCompleteTarget(SelectedOrdinal)` | DROP or ADAPT (TZ §20 forbids instant destruction as evidence; allowed as a dev tool only) |
| Player validator `RunPlayerValidation` :303-373 | `BalanceValidator.Validate`, `BalanceRepository.ExpectedSha256`, `GameBootstrap.BalanceJson` | ADAPT: run the new validator in an IL2CPP player and keep the `validation-player.json` format plus the `[CatMiners] PlayerValidation` logcat line |
| Formula chains `ShowFormulaChain` :400-745 | entirely old (E-xx, cards, gates, gacha, hire) | DROP; replace with "вклад локальных и постоянных бонусов" (TZ §19) |
| Grid overlay `DrawGrid` :816-864 | `ZoneLayout` CellOwner/CellSector/Gates/Cart, `WorkCells`, `FindSpawnCell`, `SpawnPadRows`, `IsSectorOpen` | ADAPT: the technique (one quad per cell, rebuild only on color change, every 0.5 s) suits "просмотр пути и бронирований"; replace the color sources with paths/reservations/sectors/base |
| Status text :761-774 | `State.Slot`, `SimSpeed`, `SaveDirectory`, `Formula.RulesVersion` | ADAPT (add cargo counters: created/on ground/carried/delivered) |
| `Str` JSON escaper :375 | none | REUSE (duplicate of `JsonText.Str`) |

Controller API it uses (all `GameController`): `Session`, `Balance`, `Hud` (`Toast`, `Modal(title, body, Hud.ModalButton)`), `SwitchSlot`, `RebuildBoard`, `SaveNow`, `MarkDirty`, `SimSpeed`, `SelectedOrdinal`, `SelectedCatId`, `SaveDirectory`, `Board.CellCenter(cell)`.
Performance note (:40-46, 753): Update allocates nothing when the panel is hidden and rebuilds text only when inputs change. Keep this, because the panel exists in every Editor and Development run.

## 15. `Assets/Game/Scripts/DevTools/LevelCheat.cs` (201 lines)

"ЧИТ" button under the mine plate plus a PixelUi window: zone picker (±1/±10, first/current/last), "Перейти" and "Пройти шахту". Keys `]`/`[` (Shift = ±10) and `\` (finish). It always uses the Debug slot.
```csharp
public sealed class LevelCheat : MonoBehaviour                         // :17
[RuntimeInitializeOnLoadMethod(AfterSceneLoad)] static void Install()  // :28
public static LevelCheat Attach(GameController game)                   // :35 (null if no Hud); used by tests
public void JumpTo(int zone)                                            // :125
public void FinishCurrentZone()                                         // :146
```
- UI (:45-96) uses the **PixelUi kit** (`PixelUi.Button/Window/Text`, `PixelButton.Red/Dark/Blue/Gold`, `PixelWindow.AddButton`, `PixelUi.Px`, `Ui.Rect().Anchor`). **REUSE.**
- Position: under the top strip (`anchoredPosition (2px, -34px)`); recheck it against the new HUD layout.
- `JumpTo` :125-140: `Session.IsDebugSlot`, `ClearSelection`, `Session.DebugGotoZone`, `RebuildBoard`, `SaveNow`, else `SwitchSlot(Debug, true, zone)`. Verdict: **ADAPT** (swap to the new-mode debug profile and goto).
- `FinishCurrentZone` :146-172: gates/shafts/exit through `DebugCompleteTarget`, `OpenGate`, `ActivateShaft`, `CompleteZoneNow`. Old concepts, **DROP or rewrite**. New meaning: clear the stage (debug only), then the normal §11.3 transition. It must be visibly marked as a cheat (TZ §19/§20: tools are not evidence of an honest run).
- `Ready()` :113 blocks on `InTransition`/`Hud.ModalOpen`. **REUSE.**
- Update :174-199: keyboard via `Keyboard.current`; text refresh only on change. **REUSE.**
- Visible strings are Cyrillic literals. That is allowed because LocalizationTests scans only `Runtime` + Core UI files, not DevTools.

---

## 16. Gotchas summary

- **Mutagen**:
  - `*.csproj`/`*.sln`, `/obj`, `build` (anywhere), `/Builds`, `/Library`, `/Temp` and `.git` are not synced. APKs, the `build/ci` mirror and dotnet outputs exist only on the PC.
  - `Docs/Reports` **is** synced, so read results on the hub after a run.
  - CoreTests/UnityCheck generate csproj from templates on every run, so edit the `*.template` files only.
- **Meta files**: create them with `python3 Tools/gen_meta.py` for any new `Assets/Game` file or folder, to keep GUIDs stable between PC, ci and hub. GameBootstrap scene references depend on GUIDs.
- **Setup version vs Real target**: `unity.ps1` only checks that the marker and scene exist, so run `-Mode Setup` after bumping `ProjectSetup.Version`.
- **activeInputHandler**: Setup may exit 1 and ask for a restart. unity.ps1 retries once automatically, but the menu variant needs a manual editor restart.
- **Batchmode**: no window focus, no `WaitForEndOfFrame`, slow first frame (stop the clock), `AutoSetup` disabled, `ImportPackage` async (use the extractor), `-nographics` is not used for PlayMode, Setup or BuildApk.
- **Statics**: `ClockOverride`, `SaveDirectoryOverride`, `CameraRig.OverrideAspect`, `SimSpeed` and `TestData.Balance` survive between tests, so always reset them in TearDown.
- **LocalizationTests** fail the EditMode run if any new **Runtime** file has Cyrillic string literals outside `Loc.*.cs`. Exempt are `throw`/`Exception(...)`, `Debug.Log*`/Assert, and statements with a `// loc:ignore` comment. `EveryKeyUsedInCodeIsRegistered` checks every literal `Loc.T/F/Plural("key")` across all of `Assets/Game/Scripts` against `Core/Text/Loc*.cs`.
- **Ci and Docs**: in `build/ci`, `TestData.ProjectRoot` has no `Docs/`. Write reports via `-catminersReports`, and resolve Docs inputs (CSV, TZ) by walking up.
- **DevTools leak**: never reference `CatMiners.DevTools` from Runtime. BuildAndroid fails `noDevTools` with `DevToolsLeak`.
- **Old reports**: `Docs/Reports/*.xml|json|md` belong to the merge game, and TZ §18 says not to present them as rework results. Consider a `Docs/Reports/v2/` subfolder, or clearly regenerate them.
- **Explicit tests** (report generators: `WriteValidationReport`, `EconomyTablesReport`, `BotZones1To10Report`, `GenerateSaveV1MainFixture`, `RecordGameplay`) run only through a filter, and `ValidationReportGenerator` ignores them as evidence.
