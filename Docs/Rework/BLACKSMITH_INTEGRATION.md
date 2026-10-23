# Bruno integration handoff (shared checkout)

> Integrated and verified by parent: core 74/74; Unity Bruno + Mia save 2/2. Current implementation and evidence: [FINAL_EXCAVATIONS.md](FINAL_EXCAVATIONS.md). Remaining text is the original integration handoff, not outstanding work.

Rescue/progression agent owns ProgressionConfig/Service, progression.json, MineRescuePlacer, HUD guide/progress/equipment/results, rescue rendering, Bruno camp service, procedural Bruno art and targeted tests. No simulation partials, OutpostState, StageProgressionService or OutpostConfigSet edits by this agent.

Parent integration required:

1. `Assets/Game/Scripts/Core/Outpost/Level/OutpostLevelAdapter.cs`, BuildMine: replace `configs.Progression?.Rescue` in `MineRescuePlacer.Place(...)` with `configs.Progression?.RescueForZone(map.Number)`. Keep saved MapDressing untouched; only new runs receive the Mine 2 cage.
2. Run `python3 Tools/gen_outpost_config_defaults.py` after progression.json is changed (agent will not modify OutpostConfigSet.Defaults.cs).
3. Completion already calls `ProgressionService.Sync` after `StageProgressionService.ApplyCompletion` in OutpostSimulation.Tick.cs. Updated Sync grants the idempotent starter immediately when Equipment unlocks. Preserve this ordering (CompletedZones must contain 2 first); no extra simulation hook needed.
4. No OutpostState schema change required: progression Crew, Grants, Shown and Version store rescue identity, once-only grants and compatible migration.

Targeted filters and verification status will be appended here when ready. Please do not run full campaign or independent Unity while calibration is running.

## Ready for parent verification

- `RescueForZone(map.Number)` is available and parent reports BuildMine patched.
- Parent reports atlas built with both `bruno` and `excavations`; Bruno source has not changed since that build.
- Parent still owns final `python3 Tools/gen_outpost_config_defaults.py` regeneration.
- No additional completion/rescue simulation calls are required: existing `OnRescueLockDestroyed` persists distinct Bruno identity, and post-ApplyCompletion `ProgressionService.Sync` unlocks Equipment and grants/repairs the starter immediately. Normal clearing/delivery completion stays in the existing simulation.
- No direct edits to OutpostSimulation.*, OutpostState, StageProgressionService, OutpostConfigSet, OutpostLevelAdapter or the defaults generator by this agent.

Implementation: progression profile Version 2; Equipment event `blacksmith-and-mine2` (Zone 0); `Blacksmith` rescue in Mine 2, separate Crew/Grants/Shown keys. Existing Version 0/1 completed-Mine-2 profiles and equipment owners receive silent once-only service/access migration. Existing incomplete Mine 2 snapshots without a cage finish intact and receive the service at completion. New layouts require the rescue. Starter grant shares `grant.equipment` with the old path, reuses an owned pickaxe and repairs missing functional starter state without duplicate-parts conversion. New profiles launched directly in Mine 3 without completion history intentionally do not unlock Equipment.

Runtime: same cage animation and actual navigation route, distinct Bruno appearance, no worker creation; compact decorative anvil at the base yard edge, with Bruno persisting into subsequent mines. Mia remains the Store keeper. Store/Sell Goods layout/icons unchanged. Rescue callout precedes the brief Cards lesson; rescue reveal precedes results; completion queues Equipment introduction, which opens the real item/equip screen. Later/Back dismisses only the explanation. Item screen explains team-wide benefit/persistence and displays actual before/after damage including cards/local ranks.

### Targeted test filters (not independently run)

Core: `FullyQualifiedName~BlacksmithProgressionTests|FullyQualifiedName~FirstSessionTests`.

Focused PlayMode/evidence: `CatMiners.Tests.FirstSessionEvidenceTests.Blacksmith_Mine2_RescueWalkBaseAndEquipBeforeMine3`.

Existing first-session regression/evidence updated: `CatMiners.Tests.FirstSessionEvidenceTests.FirstSession_Mines1To3`, `CatMiners.Tests.FirstSessionSaveTests`.

`FirstSessionTestUtil.SkipPresentation` now includes Bruno, the short Cards lesson and Equipment intro, so unrelated gameplay screenshot suites can suppress these explicitly.

Focused Bruno evidence outputs under the existing `Docs/Reports/first-session/` directory:
`bruno-01-cage.png`, `bruno-02-door-opening.png`, `bruno-03-walk.png`, `bruno-04-base-anvil.png`, `bruno-05-equipment-unlock-mine2.png`, `bruno-06-equipped-mine2.png`, `report-bruno.json`.

Actually run by this agent: procedural `build.py --only bruno` passed (84 sprites), visually inspected sprite contact sheet, JSON parse/gate assertions passed, C# tree-sitter syntax parse passed across 48 relevant files. These are static/source checks, not compilation or gameplay verification. No Unity, .NET tests, campaign runs or benchmarks were run independently.

### Files changed by rescue agent

- `Assets/Game/Scripts/Core/Outpost/Config/ProgressionConfig.cs`
- `Assets/Game/Scripts/Core/Outpost/Sim/ProgressionService.cs`
- `Assets/Game/Scripts/Core/Outpost/Level/MineRescuePlacer.cs`
- `Assets/Game/Resources/Outpost/progression.json`
- `Assets/Game/Scripts/Core/Text/Loc.Outpost.cs` (new rescue/equipment/cards strings only; parent also adds final-target strings)
- `Assets/Game/Scripts/Runtime/Outpost/View/OutpostBoardView.Rescue.cs`
- `Assets/Game/Scripts/Runtime/Outpost/View/BaseCampView.cs`
- `Assets/Game/Scripts/Runtime/Outpost/View/OutpostLook.cs`
- `Assets/Game/Scripts/Runtime/Outpost/View/OutpostCatView.cs`
- `Assets/Game/Scripts/Runtime/Outpost/UI/OutpostHud.Progress.cs`
- `Assets/Game/Scripts/Runtime/Outpost/UI/OutpostHud.Guide.cs`
- `Assets/Game/Scripts/Runtime/Outpost/UI/OutpostHud.Equipment.cs`
- `Assets/Game/Scripts/Runtime/Outpost/UI/OutpostHud.Results.cs`
- `Tools/PixelArt/sprites/bruno.py`
- `Tools/PixelArt/build.py` (register bruno; parent subsequently registers excavations)
- `Assets/Game/Tests/EditMode/Outpost/BlacksmithProgressionTests.cs` and `.meta`
- `Assets/Game/Tests/EditMode/Outpost/FirstSessionTests.cs`
- `Assets/Game/Tests/PlayMode/Outpost/FirstSessionEvidenceTests.cs`
- `Assets/Game/Tests/PlayMode/Outpost/FirstSessionSaveTests.cs`
- `Assets/Game/Tests/PlayMode/Outpost/FirstSessionTestUtil.cs`
- This integration document.
