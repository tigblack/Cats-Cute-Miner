Factory activation core verification — 2026-10-22

Public integration API:
- OutpostCommand.ActivateFactory(targetId), appended CommandKind.ActivateFactory = 13.
- sim.ActivateBlockReason(targetId): null, "locked", "unknown", or "already". ReadyToContinue does not block a pending claim.
- FactoryStatus.PendingActivation = 4, appended after Locked/Working/Ready/Stopped = 0/1/2/3.

Behavior:
- Breaking standing factory chains emits TargetDestroyed then FactoryUnlocked, without a chest, cycle, or automation.
- Activation grants exactly one ChestOpened result, then starts Working and checks automation. After mine completion it grants the chest and becomes Stopped with no cycle.
- Pending factories neither block mining/completion nor participate in automation, debug automation, production, or offline production. Pending survives mine completion and reload.
- RequiredCardId/RequiredCardLevel from the map control the guaranteed card and cumulative copies. Existing levels and held copies reduce the missing amount. Mines <=7 receive 100%; 8–10 75%; 11–15 50%; 16+ 25%, rounded up. First type always gets at least one own-card copy, even if already owned.
- Minimum own-card copies merge into the existing chest result before grants, so random cards/elixir, detail, totals, new-card counts, and RNG remain correct. Missing reward IDs use existing GachaWooden.
- Seen types persist as Profile.CardKeys "CatOutpostV2:cards:factory-seen:<typeId>". Legacy active statuses reconstruct seen-type keys, remain activated, and reject another claim. Absent/null CardKeys is supported. No save schema/rules/hash invalidation or reset was introduced.

Files changed:
- Assets/Game/Scripts/Core/Outpost/Sim/OutpostState.cs
- Assets/Game/Scripts/Core/Outpost/Sim/OutpostSimulation.Commands.cs
- Assets/Game/Scripts/Core/Outpost/Sim/OutpostSimulation.Tick.cs
- Assets/Game/Scripts/Core/Outpost/Sim/OutpostSimulation.Factories.cs
- Assets/Game/Scripts/Core/Outpost/Sim/OutpostSimulation.Cards.cs
- Assets/Game/Scripts/Core/Outpost/Sim/OutpostSimulation.cs
- Assets/Game/Scripts/Core/Outpost/Sim/OutpostInvariants.cs
- Assets/Game/Scripts/Core/Outpost/Sim/OutpostEvents.cs
- Assets/Game/Scripts/Core/Outpost/Factories/FactoryMath.cs
- Assets/Game/Scripts/Core/Outpost/Cards/CardService.cs
- Assets/Game/Scripts/Core/Outpost/Cards/CardChestService.cs
- Assets/Game/Tests/EditMode/Outpost/FactorySimulationTests.cs
- Assets/Game/Tests/EditMode/Outpost/FactorySimulationTests.Activation.cs (+ .meta generated narrowly with Tools/gen_meta.py)
- Assets/Game/Tests/EditMode/Outpost/OutpostSaveTests.cs
- Assets/Game/Tests/EditMode/Outpost/OutpostCardTests.cs

Final targeted run: 140 passed, 0 failed (29 activation cases), Mac Unity .NET SDK, no restore.
Command:
```
on-remote nice -n15 /Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test Tools/CoreTests/Tests/Tests.csproj --no-restore --nologo --filter '((FullyQualifiedName~FactorySimulationTests&FullyQualifiedName!~Offline3h)|FullyQualifiedName~FactoryCatalogTests|FullyQualifiedName~OutpostCardTests|FullyQualifiedName~OutpostSaveTests)&FullyQualifiedName!~Zone1_NewProfile_CardFindsWaitForCardsIntro_GoldUnchanged&FullyQualifiedName!~Zone1_WithoutFirstSessionRules_TutorialCard_RockElixir_ExitChest_AsBefore' --logger 'trx;LogFileName=factory-activation-focused.trx' --results-directory Tools/CoreTests/TestResults/FactoryActivation
```

An earlier broader run found two out-of-scope Mine 1 assertions, left unchanged and excluded from the final focused run:
- OutpostCardTests.Zone1_NewProfile_CardFindsWaitForCardsIntro_GoldUnchanged: expected 1258 gold, actual 120.
- OutpostCardTests.Zone1_WithoutFirstSessionRules_TutorialCard_RockElixir_ExitChest_AsBefore: expected 2 rock elixir events, actual 0.
That earlier TRX also includes the old factory-card test failure, which was subsequently updated to explicitly activate and passed in the final run.

No Runtime/UI/controller/IOutpostGame changes, full campaign, heavy Unity jobs, user save modifications, commits, pushes, or branch switches.

Exact new activation test cases (all passed):
- Activation_AssistanceSubtractsSpentAndHeldCopies(11,7)
- Activation_AssistanceSubtractsSpentAndHeldCopies(16,4)
- Activation_AssistanceSubtractsSpentAndHeldCopies(7,14)
- Activation_AssistanceSubtractsSpentAndHeldCopies(8,11)
- Activation_AssistanceTiers_UseMappedCardAndCumulativeCopies(10,14)
- Activation_AssistanceTiers_UseMappedCardAndCumulativeCopies(11,9)
- Activation_AssistanceTiers_UseMappedCardAndCumulativeCopies(140,5)
- Activation_AssistanceTiers_UseMappedCardAndCumulativeCopies(15,9)
- Activation_AssistanceTiers_UseMappedCardAndCumulativeCopies(16,5)
- Activation_AssistanceTiers_UseMappedCardAndCumulativeCopies(7,18)
- Activation_AssistanceTiers_UseMappedCardAndCumulativeCopies(8,14)
- Activation_ChainsWaitWithoutRewardsCyclesOrAutomation_CommandGrantsOneVisibleChest
- Activation_ChestMinimumMergesWithNormalCards_NewCardsTotalsAndRngStayAccurate
- Activation_FirstTypeEvenIfOwned_LaterSameTypeAfterReloadGetsNoExtraMinimum
- Activation_InvariantsRejectPendingAutomationProductionAndCycles
- Activation_LegacyNumericStatusesAndAbsentHistory_StayActivatedWithoutAnotherChest(1,False)
- Activation_LegacyNumericStatusesAndAbsentHistory_StayActivatedWithoutAnotherChest(2,True)
- Activation_LegacyNumericStatusesAndAbsentHistory_StayActivatedWithoutAnotherChest(3,False)
- Activation_MineCompletesWhilePending_ChestStillClaimableAfterReload_ThenStopped
- Activation_MissingRewardId_UsesExistingChestWithCardsAndElixir
- Activation_OfflinePending_DoesNotProduceAutomateOrClaimChest
- Activation_RealMines2To7_EveryMappedRequirementGetsEnoughRemainingCopies(2)
- Activation_RealMines2To7_EveryMappedRequirementGetsEnoughRemainingCopies(3)
- Activation_RealMines2To7_EveryMappedRequirementGetsEnoughRemainingCopies(4)
- Activation_RealMines2To7_EveryMappedRequirementGetsEnoughRemainingCopies(5)
- Activation_RealMines2To7_EveryMappedRequirementGetsEnoughRemainingCopies(6)
- Activation_RealMines2To7_EveryMappedRequirementGetsEnoughRemainingCopies(7)
- Activation_ReloadBeforeAndAfter_AndRepeatedScheduledCommands_AwardExactlyOnce
- Activation_SaveServiceAcceptsPendingAndActivated_WithoutSchemaReset
