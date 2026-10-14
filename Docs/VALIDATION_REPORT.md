# VALIDATION_REPORT — проверка прототипа Cat Miners

> Файл собирается автоматически тестом `ValidationReportGenerator.WriteValidationReport` (`Tools/CoreTests/run.ps1 -Filter WriteValidationReport`) из машинных отчётов `Docs/Reports/`. Вручную не редактируется: после новых прогонов соберите заново.

| Параметр | Значение |
|---|---|
| Отчёт собран (UTC) | 2026-10-08 10:05 |
| Unity | 6000.3.11f1 |
| Баланс | `Assets/Game/Data/Source/json-data.json`, 2 254 970 байт, SHA-256 `8975a5a09dce8e2675d06b751c50b5cac80da3988f8ed098910492c442f85ee5` (совпадает с эталоном) |
| Профиль правил | `rulesVersion = 1` (Приложение C) |
| Проверенные платформы | Windows Editor (batchmode, изолированная копия `build/ci`); `dotnet test` (.NET 8) для ядра; Android — см. «Сборка Android» |

## Итог

1. **Основные механики работают** — в Unity 6000.3.11f1 упавших тестов нет: EditMode — прошло 320 из 324 (пропущено 4: «пишет отчёт: dotnet test --filter EconomyTablesReport»; «Создаёт Assets/Game/Tests/Fixtures/Saves/save_v1_main.json; запускать вручную»; «долгий прогон: dotnet test --filter BotZones1To10Report»; «пишет Docs/VALIDATION_REPORT.md: dotnet test --filter WriteValidationReport»), PlayMode — прошло 47 из 50 (пропущено 3: «пишет build/recording/gameplay.avi, ≈45 с»; «Unstable due to 1252825» ×2).
2. **Вычисления соответствуют рабочему профилю v1** — контрольные значения Приложения C: 41/41 тестов прошло. Это соответствие нашему профилю, а не восстановленному алгоритму референса.
3. **Темп экономики по сценарному боту** — zone1 пройдена за 1 мин 11 с; zone2 пройдена за 2 мин 18 с; zone3 пройдена за 4 мин 29 с; zone4 пройдена за 9 мин 27 с; zone5 пройдена за 4 ч 20 мин; zone6 пройдена за 4 ч 46 мин; zone7 пройдена за 13 ч 05 мин; zone8 пройдена за 18 ч 00 мин; zone9 пройдена за 31 ч 45 мин; zone10 пройдена за 40 ч 13 мин. Это оценка темпа рабочего профиля, решение по калибровке — за владельцем.

## Матрица приёмки (ТЗ 18)

| Критерий | Доказательство | Кто проверяет | Статус | Подробности |
|---|---|---|---|---|
| Нет ошибок компиляции | `Docs/Reports/unity-compile.log`: нет строк `error CS` | ИИ | готово | лог без error CS |
| Исходный JSON сохранён, hash совпадает (Editor и плеер) | Тест импорта, `validation.json` | ИИ | частично | 2/2 тестов прошло; validation.json: hash совпадает (Editor); в плеере не проверено — APK не собран, validation-player.json нет |
| Все 144 планировки поддерживаются | EditMode-тесты групп 1–2, `validation.json` | ИИ | готово | 44/44 тестов прошло; validation.json: зон 144, ошибок 0 |
| Формулы соответствуют Приложению C | EditMode-тесты групп 4, 12 | ИИ | готово | 62/62 тестов прошло |
| Объединение, транзакции, шахта, ворота, переход, офлайн, большие числа | EditMode-тесты групп 5–13 | ИИ | готово | 108/108 тестов прошло |
| Сцена запускается без исключений; мышь и касания (тап, перенос, объединение, прокрутка, жест на UI, отмена касания) | PlayMode-тесты с `InputTestFixture`, smoke-тест `CoreGame` (10 с на ×20) | ИИ | готово | 11/11 тестов прошло |
| Нет исключений в игровом цикле | PlayMode, слот Debug: zone5, zone10, zone144 по 60 с на ×100, ворота и `CompleteZone` через инструменты | ИИ | готово | 1/1 тестов прошло |
| Кнопки покупки и улучшения реально меняют симуляцию | PlayMode-тест | ИИ | готово | 1/1 тестов прошло |
| Состояние переживает закрытие без дубликатов и потерь | Тесты 6, 10 и PlayMode save/load | ИИ | готово | 4/4 тестов прошло |
| Сценарный бот прогнан по zone1–zone10 | Раздел «Темп экономики» ниже (`Docs/Reports/bot-zones-1-10.md`), журнал действий `Docs/Reports/bot-actions-zones-1-10.md` | ИИ — прогон; владелец — решение по темпу | готово | все зоны пройдены |
| APK собран | `Docs/Reports/build-android.json` | ИИ | не проверено | ModuleMissing: нет модулей SDK, NDK, OpenJDK |
| Касания на устройстве, safe area, сворачивание, визуал и звук, ручное прохождение zone1–3 | Чек-лист README | Владелец | не проверено | ручная проверка владельца |
| Будущий город не реализован и не влияет на экономику | Ревью кода: события `RockDestroyed`, `MineShaftActivated`, `CheckpointOpened`, `MineCompleted` используют только представление (эффекты, звук, вибрация, обучение), немедленное сохранение и диагностический обработчик `ZoneCompletionLog` (Editor/Development); экономику они не меняют, городских обработчиков, ресурсов, экрана и кнопки нет | ИИ | готово | ревью кода |

## Сводка тестов (из NUnit XML)

| Набор | Источник | Всего | Прошло | Упало | Пропущено | Начало (UTC) | Unity | JSON |
|---|---|---:|---:|---:|---:|---|---|---|
| EditMode (Unity) | `Docs/Reports/editmode-results.xml` | 324 | 320 | 0 | 4 | 2026-10-08 10:04:04Z | 6000.3.11f1 | `8975a5a09dce…` |
| PlayMode (Unity) | `Docs/Reports/playmode-results.xml` | 50 | 47 | 0 | 3 | 2026-10-08 09:55:23Z | 6000.3.11f1 | `8975a5a09dce…` |

Пропущенные тесты и причина пропуска (из XML):

- `CatMiners.Tests.EconomyReportTests.EconomyTablesReport` — «пишет отчёт: dotnet test --filter EconomyTablesReport»
- `CatMiners.Tests.GameplayRecordingTests.RecordGameplay` — «пишет build/recording/gameplay.avi, ≈45 с»
- `CatMiners.Tests.SaveHardeningTests.GenerateSaveV1MainFixture` — «Создаёт Assets/Game/Tests/Fixtures/Saves/save_v1_main.json; запускать вручную»
- `CatMiners.Tests.ScenarioBotTests.BotZones1To10Report` — «долгий прогон: dotnet test --filter BotZones1To10Report»
- `CatMiners.Tests.ValidationReportGenerator.WriteValidationReport` — «пишет Docs/VALIDATION_REPORT.md: dotnet test --filter WriteValidationReport»
- `IntegrationTests.WindowsInput_MouseMovements_AreDetected` — «Unstable due to 1252825»
- `IntegrationTests.WindowsInput_RemoteDesktopMouseMovements_AreDetected` — «Unstable due to 1252825»

<details><summary>EditMode (Unity): список тестов</summary>

| Тест | Результат | Время, с |
|---|---|---:|
| `CatMiners.Tests.AutoMiningTests.DraggedCatIsNotReassigned` | Passed | 0.409 |
| `CatMiners.Tests.AutoMiningTests.OfflineAndClassicRulesLeaveCatIdle` | Passed | 0.001 |
| `CatMiners.Tests.AutoMiningTests.RealDamageBreaksRockAndCatKeepsMining` | Passed | 0.013 |
| `CatMiners.Tests.AutoMiningTests.SideTargetIsTakenWithoutMoving` | Passed | 0.003 |
| `CatMiners.Tests.AutoMiningTests.StaysInPlaceWhenNothingInReach` | Passed | 0.001 |
| `CatMiners.Tests.BalanceLoadTests.BigLiteralsAndHpControlValues` | Passed | 0.008 |
| `CatMiners.Tests.BalanceLoadTests.CardMaxLevels` | Passed | 0.001 |
| `CatMiners.Tests.BalanceLoadTests.HashSizeAndCounts` | Passed | 0.001 |
| `CatMiners.Tests.BalanceLoadTests.WrongHashIsRejected` | Passed | 0.037 |
| `CatMiners.Tests.BalanceLoadTests.ZonesInCampaignOrder` | Passed | 0.001 |
| `CatMiners.Tests.BarrelLootTests.CatCapCountsUnopenedBarrels` | Passed | 0.031 |
| `CatMiners.Tests.BarrelLootTests.CollectLootCreditsExactAmountWithRockLedgerKey` | Passed | 0.01 |
| `CatMiners.Tests.BarrelLootTests.CompleteZoneCarriesElixirLootAndDropsGoldLoot` | Passed | 0.013 |
| `CatMiners.Tests.BarrelLootTests.DestroyedRockDropsLootAndGrantsNoGoldYet` | Passed | 0.003 |
| `CatMiners.Tests.BarrelLootTests.HardLootCreditsHardCurrencyAndCarriesOver` | Passed | 0.003 |
| `CatMiners.Tests.BarrelLootTests.HireSpendsGoldAndPutsBarrelInMergeZoneSpawnCell` | Passed | 0.004 |
| `CatMiners.Tests.BarrelLootTests.LootAutoCollectsAfterThirtySecondsAndNotBefore` | Passed | 0.071 |
| `CatMiners.Tests.BarrelLootTests.OfflineProgressAutoCollectsLoot` | Passed | 0.003 |
| `CatMiners.Tests.BarrelLootTests.OldSaveWithoutLootFieldsLoadsWithEmptyLoot` | Passed | 0.039 |
| `CatMiners.Tests.BarrelLootTests.OpenBarrelSpawnsIdleCatOfHireLevelInBarrelCell` | Passed | 0.002 |
| `CatMiners.Tests.BarrelLootTests.SaveLoadRoundTripsBarrelsAndLoot` | Passed | 0.037 |
| `CatMiners.Tests.BarrelLootTests.SwitchesAreOffByDefault` | Passed | 0 |
| `CatMiners.Tests.BarrelLootTests.TutorialFlowHireOpenAssignCollectHireAgain` | Passed | 0.069 |
| `CatMiners.Tests.BigNumberAcceptanceTests.BigFormInsideDoubleRangeRoundTripsExactly` | Passed | 0.028 |
| `CatMiners.Tests.BigNumberAcceptanceTests.ControlPrice1042FitsDouble1043DoesNotBothFiniteAndPayable` | Passed | 0.067 |
| `CatMiners.Tests.BigNumberAcceptanceTests.FormattingOfBigValuesHasNoNaNInfinityOrHang` | Passed | 0.013 |
| `CatMiners.Tests.BigNumberAcceptanceTests.LateShaftsLeaveDoubleFrom1043SunstoneTo1183Diamond` | Passed | 0.054 |
| `CatMiners.Tests.BigNumberAcceptanceTests.OutputWithAllThresholdsAndMaximumModifiersIsFinite` | Passed | 0.002 |
| `CatMiners.Tests.BigNumberAcceptanceTests.OutputWithProductOfAllThresholdsIsFiniteAndPaysOut` | Passed | 0.216 |
| `CatMiners.Tests.BigNumberAcceptanceTests.PlusOneBuysEveryLevelFrom1040To1200WithoutHang` | Passed | 0.02 |
| `CatMiners.Tests.BigNumberAcceptanceTests.PlusOnePriceOnLevels1040To1199FollowsE03AndStaysFinite` | Passed | 0.008 |
| `CatMiners.Tests.BigNumberAcceptanceTests.SaveLoadOfBigValuesIsExactWithoutNaNOrInfinity` | Passed | 0.155 |
| `CatMiners.Tests.BigNumberAcceptanceTests.SunstoneZoneShaftAndThresholdTable` | Passed | 0.003 |
| `CatMiners.Tests.BigNumberAcceptanceTests.ToThresholdBuysOnlyAffordableLevelsAcrossDoubleLimit` | Passed | 0.004 |
| `CatMiners.Tests.BigNumberAcceptanceTests.ToThresholdPreviewAndBuyOnLevels1040To1200` | Passed | 0.004 |
| `CatMiners.Tests.CannonTests.AllZones_ForgeIsTwoByThreeAndCannonStandsAtRightEdge` | Passed | 0.072 |
| `CatMiners.Tests.CannonTests.CannonCells_TakeNoCatsOrContainers` | Passed | 0.002 |
| `CatMiners.Tests.CannonTests.LegacySave_CatAndBarrelOnCannonMoveToSpawnCells` | Passed | 0.001 |
| `CatMiners.Tests.CoreRulesFixTests.CardUpgradeCostLookupIsWhatUpgradeDebits` | Passed | 0.002 |
| `CatMiners.Tests.CoreRulesFixTests.CommandsAreRejectedAfterCampaignCompleted` | Passed | 0.017 |
| `CatMiners.Tests.CoreRulesFixTests.DeliveryIntervalVarianceDrawsFromTimersStream` | Passed | 0.004 |
| `CatMiners.Tests.CoreRulesFixTests.DragStateIsTransientAndClearedOnLoadAndZoneEntry` | Passed | 0.002 |
| `CatMiners.Tests.CoreRulesFixTests.DropOnOwnWorkCellOfSameTargetRestoresPhase` | Passed | 0.002 |
| `CatMiners.Tests.CoreRulesFixTests.EtaAndWorkersExcludeHeldCat_TargetFinishedByOthersLeavesHeldCatIdle` | Passed | 0.003 |
| `CatMiners.Tests.CoreRulesFixTests.ExcludedAndChestRocksAreDerivedFromRewardCycles` | Passed | 0.005 |
| `CatMiners.Tests.CoreRulesFixTests.Ft3NumberMatchesDoubleOnDataAndSurvivesBeyondDouble` | Passed | 0.172 |
| `CatMiners.Tests.CoreRulesFixTests.HeldCatNeitherHitsNorGainsPhase_CancelRestoresPhaseWithoutExtraHit` | Passed | 0.012 |
| `CatMiners.Tests.CoreRulesFixTests.IdleCatDroppedOnOwnCellNextToTargetGetsAssigned` | Passed | 0.002 |
| `CatMiners.Tests.CoreRulesFixTests.InvalidLoadedAssignmentBecomesIdle` | Passed | 0.003 |
| `CatMiners.Tests.CoreRulesFixTests.ManualClockAdvancesUtcAndMonotonicTogether` | Passed | 0.001 |
| `CatMiners.Tests.CoreRulesFixTests.MiningCatDroppedOnOwnCellIsReassignedToNearerAdjacentTarget` | Passed | 0.001 |
| `CatMiners.Tests.CoreRulesFixTests.NearestInactiveShaftToReferenceRow` | Passed | 0.004 |
| `CatMiners.Tests.CoreRulesFixTests.NewAssignmentOrMergeDuringDragStartsFromZeroPhase` | Passed | 0.001 |
| `CatMiners.Tests.CoreRulesFixTests.OfflineCardDefersAutomationUntilFirstOnlineStep` | Passed | 0.017 |
| `CatMiners.Tests.CoreRulesFixTests.OfflineSummaryHasNewsAndSeparatesGateExitShaft` | Passed | 0.144 |
| `CatMiners.Tests.CoreRulesFixTests.PeekChestRevealDoesNotRemove` | Passed | 0.001 |
| `CatMiners.Tests.CoreRulesFixTests.RejectedDropRestoresPhaseAndEndsDrag` | Passed | 0.001 |
| `CatMiners.Tests.CoreRulesFixTests.SettingsLiveInPlayerStateCameraInMineState` | Passed | 0.005 |
| `CatMiners.Tests.CoreRulesFixTests.SimEventBusCallsZoneHandlersOncePerOperation` | Passed | 0.005 |
| `CatMiners.Tests.CoreRulesFixTests.SpawnBfsCrossesOpenedGateRow_TopSectorFullSpillsBelowGate` | Passed | 0.006 |
| `CatMiners.Tests.CoreRulesFixTests.SpawnCellsFollowReferenceBfsUntilNoSpace` | Passed | 0.028 |
| `CatMiners.Tests.CoreRulesFixTests.SpawnPadAndStartCellAreOpenInEveryZoneAndGateCount` | Passed | 0.075 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G1_CoreCurrencyMaxAndIntegerLiteralsBeyondInt64` | Passed | 0.047 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G1_CorruptedGridTokensReportZoneIdAndTokenIndex` | Passed | 0.189 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G1_CrlfOrBomCopyIsRejectedWithBothHashesInMessage` | Passed | 0.244 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G1_EmptyOrTruncatedDocumentGivesConfigErrorWithPath` | Passed | 0.027 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G1_EveryGridHasCellCountKnownTokensReferencesAndOneExitAndCart` | Passed | 0.361 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G1_FileBytesMatchTzHashSizeAndEncoding` | Passed | 0.062 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G1_MissingSectionOrIdReportsFieldPath` | Passed | 0.48 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G1_ValidatorDetectsGeometryViolationsOf64` | Passed | 0.159 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G1_ValidatorReportsMissingFieldsAndBadValuesWithPath` | Passed | 0.132 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G1_WrongTypeInDocumentReportsFieldPath` | Passed | 0.287 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G1_ZonesAreOrderedNumericallyEvenIfSourceIsLexicographic` | Passed | 0.195 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_CartIsTwoByThreeOnFreeStartBandAndNotATarget` | Passed | 0.055 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_CompletedTargetsTakeNoWorkers` | Passed | 0.002 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_ExitIsThreeByOneAtTopWithRocksOnWorkRow` | Passed | 0.044 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_FreeCellsOfEverySectorFormOne4ConnectedArea` | Passed | 0.042 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_GatesAreFiveByOneWithObstructionsAtBothEnds` | Passed | 0.067 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_HireFreeCatAndOpenedGateUseSameSpawnRule` | Passed | 0.011 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_Neighbors4AreUpLeftRightDownWithoutDiagonals` | Passed | 0.001 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_OnlyPlusColumnMinusRowFootprintHasNoConflicts` | Passed | 0.181 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_RsxFootprintsOccupyExpectedCellsInAllZones` | Passed | 0.74 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_SectorsAreBoundedByGateRows` | Passed | 0.194 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_SectorsOpenOneByOneThroughTheirLowerGate` | Passed | 0.001 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_SpawnCellsFollowBfsAboveTopOpenedGate` | Passed | 0.117 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_SpawnCellsFollowBfsFromStartPadInAllZones` | Passed | 0.074 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_SpawnFillsEveryFreeOpenCellThenNoSpace` | Passed | 0.004 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_WorkCellsFollowDefinitionInAllZones` | Passed | 0.22 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G2_WorkCellsMatchTzExamples` | Passed | 0.003 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G3_AllHpValuesThroughGameNumberMatchRawJson` | Passed | 0.017 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G3_HpTablesCoverEveryTargetTypeButNotCart` | Passed | 0.003 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G3_LevelOutsideTableThrowsForEveryTarget` | Passed | 0.004 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G3_Rockbasic74IsAboveInt64AndSunstone99IsFileMaximum` | Passed | 0.001 |
| `CatMiners.Tests.DataGeometryHpAcceptanceTests.G3_SessionHpComesFromTokenLevelNotZoneNumber` | Passed | 0.003 |
| `CatMiners.Tests.DataValidatorFixTests.CheckpointArraysMustMatchStatModifierTypeLength` | Passed | 0.276 |
| `CatMiners.Tests.DataValidatorFixTests.CurrencyIdsMustBeGoldAndElixir` | Passed | 0.326 |
| `CatMiners.Tests.DataValidatorFixTests.DeliveryRewardDetailedTypeMustMatchRewardType` | Passed | 0.167 |
| `CatMiners.Tests.DataValidatorFixTests.FractionalLiteralInIntFieldIsLoadErrorWithPath` | Passed | 0.356 |
| `CatMiners.Tests.DataValidatorFixTests.LegitimateNonFt0RecordsAreNotFlagged` | Passed | 0.08 |
| `CatMiners.Tests.DataValidatorFixTests.MinerIdCycleEntriesMustBeMinersIds` | Passed | 0.342 |
| `CatMiners.Tests.DataValidatorFixTests.NonWholeWeightsAreValidatorErrorsAtTheirPaths` | Passed | 0.221 |
| `CatMiners.Tests.DataValidatorFixTests.NonZeroGrowthOrNonFt0InAppliedRecordsAreValidatorErrors` | Passed | 0.156 |
| `CatMiners.Tests.DataValidatorFixTests.SourceHasSingleTinygoblinMiner` | Passed | 0 |
| `CatMiners.Tests.DataValidatorFixTests.SourceWeightsAreWholeAndNonNegative` | Passed | 0.002 |
| `CatMiners.Tests.DataValidatorFixTests.TwoMinersIsLoadErrorNamingMiners` | Passed | 0.496 |
| `CatMiners.Tests.DataValidatorFixTests.UpgradeMultiplierAndZeroBaseInvariantsAreValidatorErrors` | Passed | 0.162 |
| `CatMiners.Tests.DeliveryAcceptanceTests.AdSlot_SkippedRecordedAndTimerRestarted` | Passed | 0.053 |
| `CatMiners.Tests.DeliveryAcceptanceTests.CatBox_HoldsSlot_CellBlocked_OpensAtFullCap` | Passed | 0.01 |
| `CatMiners.Tests.DeliveryAcceptanceTests.CatchUp_SingleWeakestCatGetsPair` | Passed | 0.007 |
| `CatMiners.Tests.DeliveryAcceptanceTests.Content_FixedAtCreation_SameResultAfterReload` | Passed | 0.017 |
| `CatMiners.Tests.DeliveryAcceptanceTests.Enabling_Rank4AndTwoActivatedShafts_FirstSlot20sAfterEnabling` | Passed | 0.017 |
| `CatMiners.Tests.DeliveryAcceptanceTests.NoFreeCell_CreationDeferredAndTimerStands` | Passed | 0.008 |
| `CatMiners.Tests.DeliveryAcceptanceTests.OneBoxOnField_TimerStandsUntilClaim` | Passed | 0.021 |
| `CatMiners.Tests.DeliveryAcceptanceTests.Pool_CatRecordsNeedFreeSlot_UnopenedCatContainerCounts` | Passed | 0.012 |
| `CatMiners.Tests.DeliveryAcceptanceTests.Pool_MaxDupesCountsDropsInLast14400s` | Passed | 0.057 |
| `CatMiners.Tests.DeliveryAcceptanceTests.Pool_RankUnlockCheckPointUnlockAndDynamiteStock` | Passed | 0.004 |
| `CatMiners.Tests.EconomyReportTests.EconomyTablesReport` | Skipped | 0 |
| `CatMiners.Tests.EconomyReportTests.IncomePerSecondAtIsSideEffectFree` | Passed | 0.001 |
| `CatMiners.Tests.EconomyReportTests.TablesCoverForgeAndShaftsOfFirstZones` | Passed | 0.021 |
| `CatMiners.Tests.EconomyReportTests.TimeToKillControlValues` | Passed | 0 |
| `CatMiners.Tests.EconomyReportTests.TimeToKillTargetsComeFromData` | Passed | 0.017 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E01_RankMineUnlockAndCheckPointUnlock` | Passed | 0.044 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E02_CatParametersInSession` | Passed | 0.002 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E03_ForgeAndAmethystInSession` | Passed | 0.001 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E04_AmethystThresholdsAndElixirInZone2` | Passed | 0.002 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E04_ForgeThresholds` | Passed | 0.001 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E04_LastThresholdIsMaxLevel1200` | Passed | 0.002 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E05_FreeCatsDoNotCountAndZoneResets` | Passed | 0.011 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E05_HirePriceAndLevelBySession` | Passed | 0.002 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E05_PricesAfterOneGate` | Passed | 0.001 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E06_FreeCatIntervalAndLevel` | Passed | 0.02 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E07_ManagerMultipliersInSession` | Passed | 0.001 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E08_CardCodes` | Passed | 0.001 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E08_TwoGatesBonuses` | Passed | 0.001 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E09_RockRewardsInSession` | Passed | 0.001 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E10_RewardIncomeRate` | Passed | 0.001 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E11_AtLeastOneCardWhenBaseNumCardsPositive` | Passed | 0.001 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E11_ChestCardCountsAtZone44` | Passed | 0.022 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E11_HalvesRoundUpAndZone1Wooden` | Passed | 0.016 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E11_MultiplierClassByGachaType` | Passed | 0.004 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E12_Stacks` | Passed | 0.001 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E13_ExitRewardLowerBound` | Passed | 0.003 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E14_CardUpgradeCostsAndMaxLevels` | Passed | 0.001 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E15_DeliveriesInSession` | Passed | 0.012 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E16_DynamiteInSession` | Passed | 0.001 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E17_FreeChestTimerContentsAndPersistence` | Passed | 0.176 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E19_OfflineLimit8Hours` | Passed | 0.059 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.E20_MimicRockUsesOwnRewardCycle` | Passed | 0.002 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.F0_QuadraticTermAndFR_HalfAwayFromZero` | Passed | 0.001 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.FR_RoundingIsIdentityAbove2Pow53AndNeverMovesWholeUnits` | Passed | 0.001 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.G02_G03_ExitAndGateSidesAndWorkRows` | Passed | 0.042 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.G04_CartFootprintPadAndNotATarget` | Passed | 0.057 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.G05_WorkCellsAreFourNeighbours` | Passed | 0.037 |
| `CatMiners.Tests.FormulaAppendixCAcceptanceTests.G06_SpawnCellOrder` | Passed | 0.001 |
| `CatMiners.Tests.FormulaControlValueTests.E02_CatDamage` | Passed | 0.001 |
| `CatMiners.Tests.FormulaControlValueTests.E03_UpgradeCosts` | Passed | 0.001 |
| `CatMiners.Tests.FormulaControlValueTests.E05_HireCostAndLevel` | Passed | 0 |
| `CatMiners.Tests.FormulaControlValueTests.E07_E16_CardsAndDynamite` | Passed | 0 |
| `CatMiners.Tests.FormulaControlValueTests.E09_RockCoreSecondsAndSoft` | Passed | 0 |
| `CatMiners.Tests.FormulaControlValueTests.E11_BigCostsStayFinite` | Passed | 0 |
| `CatMiners.Tests.FormulaControlValueTests.E15_DeliveryIntervalAndQuantity` | Passed | 0 |
| `CatMiners.Tests.FormulaControlValueTests.RoundingRules` | Passed | 0 |
| `CatMiners.Tests.GameNumberTests.ArithmeticAndComparison` | Passed | 0 |
| `CatMiners.Tests.GameNumberTests.BeyondDoubleRange` | Passed | 0 |
| `CatMiners.Tests.GameNumberTests.FloorCeiling` | Passed | 0 |
| `CatMiners.Tests.GameNumberTests.Formatting` | Passed | 0.001 |
| `CatMiners.Tests.GameNumberTests.StorageRoundTrip` | Passed | 0 |
| `CatMiners.Tests.HireMergeAcceptanceTests.CapRejectsHireAtomicallyAndMergeFreesSlot` | Passed | 0.005 |
| `CatMiners.Tests.HireMergeAcceptanceTests.DifferentLevelsKeepAssignmentAndPhase` | Passed | 0.001 |
| `CatMiners.Tests.HireMergeAcceptanceTests.EqualLevelsMergeIntoNextLevelInReceiverCell` | Passed | 0.001 |
| `CatMiners.Tests.HireMergeAcceptanceTests.EveryZoneStartsWithoutDeadEnd` | Passed | 0.051 |
| `CatMiners.Tests.HireMergeAcceptanceTests.FreeCatContainerCountsTowardCap` | Passed | 0.012 |
| `CatMiners.Tests.HireMergeAcceptanceTests.FreeCatDoesNotAdvanceHireCounter` | Passed | 0.011 |
| `CatMiners.Tests.HireMergeAcceptanceTests.HireCounterResetsOnZoneTransition` | Passed | 0.001 |
| `CatMiners.Tests.HireMergeAcceptanceTests.HirePriceIsExactCeilForLargePurchaseNumbers` | Passed | 0.001 |
| `CatMiners.Tests.HireMergeAcceptanceTests.HirePriceLevelAndGaugeFollowPurchaseNumber` | Passed | 0.002 |
| `CatMiners.Tests.HireMergeAcceptanceTests.LoadResetsInvalidAssignmentToIdle` | Passed | 0.003 |
| `CatMiners.Tests.HireMergeAcceptanceTests.MergeKeepsReceiverAssignmentAndRestartsPhase` | Passed | 0.001 |
| `CatMiners.Tests.HireMergeAcceptanceTests.MergeOntoIdleReceiverGivesIdleResult` | Passed | 0.001 |
| `CatMiners.Tests.HireMergeAcceptanceTests.OneGateDividesHirePriceByTen` | Passed | 0.003 |
| `CatMiners.Tests.HireMergeAcceptanceTests.SaveLoadRestoresHireMergeStateAndContinuesIdentically` | Passed | 0.009 |
| `CatMiners.Tests.HireMergeAcceptanceTests.TwoGatesDivideHirePriceBy100AndRaiseCapTo12` | Passed | 0.004 |
| `CatMiners.Tests.LayoutTests.AmethystFootprintInZone2` | Passed | 0.001 |
| `CatMiners.Tests.LayoutTests.AppendixAFirstZones` | Passed | 0.123 |
| `CatMiners.Tests.LayoutTests.Block2x2InZone1` | Passed | 0.002 |
| `CatMiners.Tests.LayoutTests.MatchesZonesSummaryCsv` | Passed | 0.105 |
| `CatMiners.Tests.LayoutTests.SpecialObjectGeometry` | Passed | 0.045 |
| `CatMiners.Tests.LayoutTests.UnknownTokenIsAnErrorNotARock` | Passed | 0.001 |
| `CatMiners.Tests.LayoutTests.ValidatorPassesOnAllZones` | Passed | 0.075 |
| `CatMiners.Tests.LocalizationTests.AutomationRequirementKeepsText` | Passed | 0.001 |
| `CatMiners.Tests.LocalizationTests.CommandRejectReasonsKeepText` | Passed | 0.004 |
| `CatMiners.Tests.LocalizationTests.CoreUiFilesHaveNoCyrillicLiterals` | Passed | 0.012 |
| `CatMiners.Tests.LocalizationTests.CyrillicLiteralScannerSelfCheck` | Passed | 0.002 |
| `CatMiners.Tests.LocalizationTests.DictionaryIsBuiltWithoutProblems` | Passed | 0.003 |
| `CatMiners.Tests.LocalizationTests.DurationUnitsKeepText` | Passed | 0.006 |
| `CatMiners.Tests.LocalizationTests.EveryKeyUsedInCodeIsRegistered` | Passed | 0.088 |
| `CatMiners.Tests.LocalizationTests.FormattingAllReasonTextsReportsNoMissingKeys` | Passed | 0.005 |
| `CatMiners.Tests.LocalizationTests.MissingKeyIsVisibleAndReported` | Passed | 0.001 |
| `CatMiners.Tests.LocalizationTests.PlayerFacingCodeHasNoCyrillicLiterals` | Passed | 28.626 |
| `CatMiners.Tests.LocalizationTests.PluralPicksRussianForm` | Passed | 0.02 |
| `CatMiners.Tests.LocalizationTests.RarityNamesKeepText` | Passed | 0.001 |
| `CatMiners.Tests.LocalizationTests.ThresholdIncomeTextKeepsFormat` | Passed | 0.008 |
| `CatMiners.Tests.LocalizationTests.ZoneCompletionReasonsKeepText` | Passed | 0.003 |
| `CatMiners.Tests.MergeZoneTests.AllZones_EveryGateLandingHasMergeRowsRightAboveGate` | Passed | 0.052 |
| `CatMiners.Tests.MergeZoneTests.AllZones_StartPadHasMergeRowsRightAboveForge` | Passed | 0.065 |
| `CatMiners.Tests.MergeZoneTests.AllZones_WidthIsSeven` | Passed | 0.001 |
| `CatMiners.Tests.MergeZoneTests.Layout_RockInMergeRowBreaksMergeZone` | Passed | 0.001 |
| `CatMiners.Tests.MergeZoneTests.Session_MergeRowsFollowOpenedGates` | Passed | 0.001 |
| `CatMiners.Tests.MergeZoneTests.Validator_ReportsNoMergeZoneErrorsOnSourceData` | Passed | 0.105 |
| `CatMiners.Tests.OfflineAcceptanceTests.ClockBackwardsGivesNothingUntilItCatchesUpWithTheMark` | Passed | 0.128 |
| `CatMiners.Tests.OfflineAcceptanceTests.ClockSequenceKeepsMarkMonotonicAndCreditsOnlyNewTime` | Passed | 0.328 |
| `CatMiners.Tests.OfflineAcceptanceTests.ConsecutivePauseFocusDoesNotCreditTwice` | Passed | 0.078 |
| `CatMiners.Tests.OfflineAcceptanceTests.ExtremeClockValueIsCappedWithoutOverflow` | Passed | 0.181 |
| `CatMiners.Tests.OfflineAcceptanceTests.FrameGapOverOneSecondIsCreditedOnceAsOffline` | Passed | 0.016 |
| `CatMiners.Tests.OfflineAcceptanceTests.FrameGapUsesOfflineRulesThenReturnsOnline` | Passed | 0.001 |
| `CatMiners.Tests.OfflineAcceptanceTests.Frames60HzEqualSingleAdvanceEqualOfflineByCriterion15_2` | Passed | 1.56 |
| `CatMiners.Tests.OfflineAcceptanceTests.OfflineCapAppliesToAllSystems` | Passed | 0.361 |
| `CatMiners.Tests.OfflineAcceptanceTests.OfflineCapIsExactlyEightHours` | Passed | 0.64 |
| `CatMiners.Tests.RewardChestAcceptanceTests.ChestCardCounts_E11AndE12ControlValues` | Passed | 0.056 |
| `CatMiners.Tests.RewardChestAcceptanceTests.ChestCardCounts_Zone44ReproducesPublicTable` | Passed | 0.601 |
| `CatMiners.Tests.RewardChestAcceptanceTests.ChestOpening_FixedSeedGivesIdenticalResult` | Passed | 0.036 |
| `CatMiners.Tests.RewardChestAcceptanceTests.ChestOpening_SessionResultIsFixedBySavedRng` | Passed | 0.009 |
| `CatMiners.Tests.RewardChestAcceptanceTests.ExitReward_FromZone11IsOrdinaryGachaCrusher` | Passed | 0.007 |
| `CatMiners.Tests.RewardChestAcceptanceTests.ExitReward_RangeRecordsAlwaysOpenAtLowerBound` | Passed | 0.075 |
| `CatMiners.Tests.RewardChestAcceptanceTests.Resolver_EachDeliveryKindCreditsItsResource` | Passed | 0.014 |
| `CatMiners.Tests.RewardChestAcceptanceTests.Resolver_EveryRewardPairInCoreSectionsIsKnown` | Passed | 0.009 |
| `CatMiners.Tests.RewardChestAcceptanceTests.Resolver_EveryRockCycleEntryCreditsItsResource` | Passed | 0.076 |
| `CatMiners.Tests.RewardChestAcceptanceTests.RewardCycle_CountersSurviveCompleteZoneAndSaveLoadAndContinueTheCycle` | Passed | 0.006 |
| `CatMiners.Tests.RewardChestAcceptanceTests.RewardCycle_OnboardingMine2_EightEntriesWrapOverElevenRocksInDestroyOrder` | Passed | 0.005 |
| `CatMiners.Tests.RewardChestAcceptanceTests.RewardCycle_RockbasicSourceOrderContinuesFromZone6IntoZone7` | Passed | 0.006 |
| `CatMiners.Tests.SaveHardeningTests.CorruptedMainIsQuarantinedAndReportedWithSourcePath` | Passed | 0.075 |
| `CatMiners.Tests.SaveHardeningTests.FutureSchemaVersionIsNotLoaded` | Passed | 0.009 |
| `CatMiners.Tests.SaveHardeningTests.GenerateSaveV1MainFixture` | Skipped | 0 |
| `CatMiners.Tests.SaveHardeningTests.IncompatibleBalanceQuarantinesCorruptLeftoverAsCorrupt` | Passed | 0.011 |
| `CatMiners.Tests.SaveHardeningTests.IncompatibleBalanceQuarantinesMainTmpAndBak` | Passed | 0.048 |
| `CatMiners.Tests.SaveHardeningTests.MigrateAppliesStepsInOrderAndRejectsGaps` | Passed | 0.01 |
| `CatMiners.Tests.SaveHardeningTests.MigrationTableCoversEveryVersionBelowCurrent` | Passed | 0.002 |
| `CatMiners.Tests.SaveHardeningTests.NaNInSnapshotIsRejectedBeforeAnyFileIsWritten` | Passed | 0.026 |
| `CatMiners.Tests.SaveHardeningTests.NaNSnapshotTouchesNoFileOperation` | Passed | 0.002 |
| `CatMiners.Tests.SaveHardeningTests.OwnershipTableInSaveServiceCoversEveryPersistedField` | Passed | 0.012 |
| `CatMiners.Tests.SaveHardeningTests.PayloadAndEnvelopeAreByteIdenticalToPlainSerializer` | Passed | 0.038 |
| `CatMiners.Tests.SaveHardeningTests.QuarantineFailureIsReportedInLoadMessage` | Passed | 0.009 |
| `CatMiners.Tests.SaveHardeningTests.QuarantineRenamesWithReasonAndUtcStampAndAvoidsCollisions` | Passed | 0.013 |
| `CatMiners.Tests.SaveHardeningTests.RulesVersionMismatchIsReportedWithSourcePath` | Passed | 0.007 |
| `CatMiners.Tests.SaveHardeningTests.SaveLoadSaveKeepsPayloadSha` | Passed | 0.014 |
| `CatMiners.Tests.SaveHardeningTests.SaveV1FixtureLoadsThroughMigrationPath` | Passed | 0.019 |
| `CatMiners.Tests.SavePerformanceTests.Zone93SnapshotSerializationMedianWithin10Ms` | Passed | 0.809 |
| `CatMiners.Tests.ScenarioBotTests.AssignIdleCatsPicksNearestFreeWorkCell` | Passed | 0.007 |
| `CatMiners.Tests.ScenarioBotTests.BotCompletesFirstZones` | Passed | 0.047 |
| `CatMiners.Tests.ScenarioBotTests.BotWritesActionLogWithSimulationTime` | Passed | 0.008 |
| `CatMiners.Tests.ScenarioBotTests.BotZones1To10Report` | Skipped | 0 |
| `CatMiners.Tests.ScenarioBotTests.BuyUpgradesPicksBestPaybackNotCheapest` | Passed | 0.009 |
| `CatMiners.Tests.ScenarioBotTests.BuyUpgradesSavesForBestOption` | Passed | 0.002 |
| `CatMiners.Tests.ScenarioBotTests.CardUpgradesKeepElixirForAutomation` | Passed | 0.001 |
| `CatMiners.Tests.ScenarioBotTests.PayoutAloneIsNotAnImprovement` | Passed | 0.002 |
| `CatMiners.Tests.ScenarioBotTests.SessionClockCarriesAcrossZones` | Passed | 0.171 |
| `CatMiners.Tests.ScenarioBotTests.StarvedZoneEndsAsWallNotLimit` | Passed | 0.358 |
| `CatMiners.Tests.ShaftGateAcceptanceTests.ClosedSectorTargetsRejectDropAssignmentAndDynamite` | Passed | 0.007 |
| `CatMiners.Tests.ShaftGateAcceptanceTests.GateBrokenBySimultaneousHitsStaysClosedUntilTap` | Passed | 0.097 |
| `CatMiners.Tests.ShaftGateAcceptanceTests.GateReachesBrokenOfflineButOpensOnlyByTap` | Passed | 0.013 |
| `CatMiners.Tests.ShaftGateAcceptanceTests.GateRewardAndFourBonusesApplyOnceAcrossDoubleTapAndSaveLoad` | Passed | 0.005 |
| `CatMiners.Tests.ShaftGateAcceptanceTests.ManualPayoutIsFixedAtCycleEndAndGateDoublesOnlyNextCycle` | Passed | 0.119 |
| `CatMiners.Tests.ShaftGateAcceptanceTests.TwoGatesOpenBottomUpAndStackBonusesOncePerGate` | Passed | 0.007 |
| `CatMiners.Tests.ShaftGateAcceptanceTests.WrongCardOrTooLowCardLevelDoesNotAutomate` | Passed | 0.002 |
| `CatMiners.Tests.ShaftGateAcceptanceTests.Zone2AmethystStaysManualInNormalPlayAndZoneIsStillCompleted` | Passed | 0.008 |
| `CatMiners.Tests.ShaftGateAcceptanceTests.Zone3CitrineClearedByCatThenManualBufferThenAutomatedByCa003Level1` | Passed | 0.026 |
| `CatMiners.Tests.SimulationTests.AdvanceIsAdditive` | Passed | 0.006 |
| `CatMiners.Tests.SimulationTests.CatMinesRockInExpectedTime` | Passed | 0.002 |
| `CatMiners.Tests.SimulationTests.ClosedSectorAndObstacleRejectDrop` | Passed | 0.001 |
| `CatMiners.Tests.SimulationTests.DeliveriesStartAfterTwoShaftsFromRank4` | Passed | 0.005 |
| `CatMiners.Tests.SimulationTests.DifferentLevelsDoNotMerge` | Passed | 0.001 |
| `CatMiners.Tests.SimulationTests.DynamiteDamagesAvailableTargetOnly` | Passed | 0.001 |
| `CatMiners.Tests.SimulationTests.FreeCatComesInContainer` | Passed | 0.025 |
| `CatMiners.Tests.SimulationTests.FreeChestTimer` | Passed | 0.145 |
| `CatMiners.Tests.SimulationTests.GateBrokenThenOpenedAppliesBonusesOnce` | Passed | 0.002 |
| `CatMiners.Tests.SimulationTests.HireIsAtomicAndPriced` | Passed | 0.001 |
| `CatMiners.Tests.SimulationTests.InterruptedSaveKeepsLastValidState` | Passed | 0.022 |
| `CatMiners.Tests.SimulationTests.MergeKeepsTargetHpAndFreesSlot` | Passed | 0.001 |
| `CatMiners.Tests.SimulationTests.NewZoneStartsWithoutDeadEnd` | Passed | 0.001 |
| `CatMiners.Tests.SimulationTests.OfflineIsCappedAndMonotonic` | Passed | 0.074 |
| `CatMiners.Tests.SimulationTests.OfflineMatchesOnlineWithoutActions` | Passed | 0.05 |
| `CatMiners.Tests.SimulationTests.RewardCycleCounterPersistsAcrossZones` | Passed | 0.001 |
| `CatMiners.Tests.SimulationTests.SaveLoadRoundTripAndBackup` | Passed | 0.016 |
| `CatMiners.Tests.SimulationTests.ShaftClearActivateManualPayoutAndAutomation` | Passed | 0.001 |
| `CatMiners.Tests.SimulationTests.SpawnCellIsOnStartPad` | Passed | 0 |
| `CatMiners.Tests.SimulationTests.Zone2ShaftCannotBeAutomatedInNormalPlay` | Passed | 0 |
| `CatMiners.Tests.SimulationTests.ZoneTransitionRules` | Passed | 0.001 |
| `CatMiners.Tests.TransactionAcceptanceTests.CardUpgradeDoubleClickChargesOnce` | Passed | 0.002 |
| `CatMiners.Tests.TransactionAcceptanceTests.ChestResultIsFixedAcrossRestartAndRevealDoesNotCreditAgain` | Passed | 0.004 |
| `CatMiners.Tests.TransactionAcceptanceTests.CompleteZoneOnceAcrossDoubleTapInterruptedSaveAndReload` | Passed | 0.019 |
| `CatMiners.Tests.TransactionAcceptanceTests.DeliveryBoxSecondOpenIsRejected` | Passed | 0.004 |
| `CatMiners.Tests.TransactionAcceptanceTests.DynamiteAndPendingCatHitCompleteRockOnce` | Passed | 0.002 |
| `CatMiners.Tests.TransactionAcceptanceTests.ForgeUpgradeDoubleClickChargesOnce` | Passed | 0.002 |
| `CatMiners.Tests.TransactionAcceptanceTests.FreeCatContainerSecondOpenIsRejected` | Passed | 0.012 |
| `CatMiners.Tests.TransactionAcceptanceTests.FreeChestWithoutStockIsRejectedAndDoubleClaimCreditsOnce` | Passed | 0.045 |
| `CatMiners.Tests.TransactionAcceptanceTests.HireDoubleClickAtCapCreatesExactlyOneCat` | Passed | 0.002 |
| `CatMiners.Tests.TransactionAcceptanceTests.InterruptedSaveStepNeverDuplicatesCatsOrRewards("crash-after-replace","new")` | Passed | 0.017 |
| `CatMiners.Tests.TransactionAcceptanceTests.InterruptedSaveStepNeverDuplicatesCatsOrRewards("crash-before-replace","old")` | Passed | 0.015 |
| `CatMiners.Tests.TransactionAcceptanceTests.InterruptedSaveStepNeverDuplicatesCatsOrRewards("fallback-crash-after-move","new")` | Passed | 0.017 |
| `CatMiners.Tests.TransactionAcceptanceTests.InterruptedSaveStepNeverDuplicatesCatsOrRewards("fallback-crash-before-copy","old")` | Passed | 0.019 |
| `CatMiners.Tests.TransactionAcceptanceTests.InterruptedSaveStepNeverDuplicatesCatsOrRewards("fallback-crash-before-delete","old")` | Passed | 0.017 |
| `CatMiners.Tests.TransactionAcceptanceTests.InterruptedSaveStepNeverDuplicatesCatsOrRewards("fallback-crash-before-move","new")` | Passed | 0.018 |
| `CatMiners.Tests.TransactionAcceptanceTests.InterruptedSaveStepNeverDuplicatesCatsOrRewards("first-crash-before-move","new")` | Passed | 0.008 |
| `CatMiners.Tests.TransactionAcceptanceTests.InterruptedSaveStepNeverDuplicatesCatsOrRewards("first-torn-tmp","none")` | Passed | 0.01 |
| `CatMiners.Tests.TransactionAcceptanceTests.InterruptedSaveStepNeverDuplicatesCatsOrRewards("replace-partial","new")` | Passed | 0.018 |
| `CatMiners.Tests.TransactionAcceptanceTests.InterruptedSaveStepNeverDuplicatesCatsOrRewards("torn-tmp","old")` | Passed | 0.014 |
| `CatMiners.Tests.TransactionAcceptanceTests.OperationIdsAreUniqueIncreasingAndSurviveReload` | Passed | 0.023 |
| `CatMiners.Tests.TransactionAcceptanceTests.TwoCatsFinishSameRockInOneTickRewardOnce` | Passed | 0.003 |
| `CatMiners.Tests.TransitionAcceptanceTests.CampaignCompletedAfterZone144` | Passed | 0.009 |
| `CatMiners.Tests.TransitionAcceptanceTests.CompleteZoneRejectedUntilEveryGateOpened` | Passed | 0.002 |
| `CatMiners.Tests.TransitionAcceptanceTests.CompleteZoneRejectedWhileClearedShaftNotActivated` | Passed | 0.002 |
| `CatMiners.Tests.TransitionAcceptanceTests.ExitRewardZones1To10UsesLowerBound` | Passed | 0.008 |
| `CatMiners.Tests.TransitionAcceptanceTests.Table11_3_ChestQueueAboveFiftyIsKeptAndCarriedOver` | Passed | 0.012 |
| `CatMiners.Tests.TransitionAcceptanceTests.Table11_3_MineProgressIsCreatedAnew` | Passed | 0.087 |
| `CatMiners.Tests.TransitionAcceptanceTests.Table11_3_NewZoneRestartsFreeCatTimerAndDeliveryWaitsForUnlock` | Passed | 0.014 |
| `CatMiners.Tests.TransitionAcceptanceTests.Table11_3_PlayerProgressIsCarriedOver` | Passed | 0.006 |
| `CatMiners.Tests.TransitionAcceptanceTests.Table11_3_UnopenedDynamiteDeliveryIsCreditedButNotAboveFour` | Passed | 0.006 |
| `CatMiners.Tests.TransitionAcceptanceTests.Table11_3_UnopenedGoldAndCatDeliveriesAreLost` | Passed | 0.004 |
| `CatMiners.Tests.TransitionAcceptanceTests.TransitionAllowedWithAllRocksAndChestRocksIntact` | Passed | 0.006 |
| `CatMiners.Tests.ValidationReportGenerator.ApkReportReadsLegacyNewAndModuleMissingFormats` | Passed | 0.007 |
| `CatMiners.Tests.ValidationReportGenerator.PerfReportMarksStubAllocCounterAsNotMeasured` | Passed | 0.006 |
| `CatMiners.Tests.ValidationReportGenerator.PerfReportShowsAcceptanceGcReading` | Passed | 0.001 |
| `CatMiners.Tests.ValidationReportGenerator.PerfReportWithoutCounterProbeIsNotVerified` | Passed | 0.001 |
| `CatMiners.Tests.ValidationReportGenerator.WriteValidationReport` | Skipped | 0 |

</details>

<details><summary>PlayMode (Unity): список тестов</summary>

| Тест | Результат | Время, с |
|---|---|---:|
| `CatMiners.Tests.CannonShotTests.HireShootsBarrelFromCannonAtRightEdge` | Passed | 5.785 |
| `CatMiners.Tests.CoreGameSmokeTests.CameraPositionRestoredOnLoad` | Passed | 1.651 |
| `CatMiners.Tests.CoreGameSmokeTests.CampaignEndScreenOpensOnRelaunch` | Passed | 1.746 |
| `CatMiners.Tests.CoreGameSmokeTests.CoreGameRunsAndPlays` | Passed | 12.923 |
| `CatMiners.Tests.CoreGameSmokeTests.CorruptedSaveOffersRestartWithoutDestroyingFiles` | Passed | 0.661 |
| `CatMiners.Tests.CoreGameSmokeTests.DebugZonesRunWithoutExceptions` | Passed | 240.663 |
| `CatMiners.Tests.CoreGameSmokeTests.FrameGapIsProcessedAsOfflineOnce` | Passed | 2.529 |
| `CatMiners.Tests.CoreGameSmokeTests.HireBarrelAndLootSurviveRestart` | Passed | 2.505 |
| `CatMiners.Tests.CoreGameSmokeTests.HudButtonsChangeSimulation` | Passed | 1.798 |
| `CatMiners.Tests.CoreGameSmokeTests.QueuedChestsSurviveRestartUntilClaimed` | Passed | 3.522 |
| `CatMiners.Tests.CoreGameSmokeTests.StateSurvivesSceneRestart` | Passed | 1.84 |
| `CatMiners.Tests.EdgeIndicatorTests.ArrowLeadsToOffscreenReadyShaft` | Passed | 2.45 |
| `CatMiners.Tests.EdgeIndicatorTests.ArrowsOnlyForTargetsCameraCanReveal` | Passed | 2.244 |
| `CatMiners.Tests.EdgeIndicatorTests.HudPanelsFollowTz` | Passed | 0.952 |
| `CatMiners.Tests.GameplayRecordingTests.RecordGameplay` | Skipped | 0.001 |
| `CatMiners.Tests.LevelCheatTests.JumpGoesToZoneInDebugSlotAndFinishMovesToNext` | Passed | 1.368 |
| `CatMiners.Tests.PerformanceTests.EarlyAndLateZoneFrameTimes` | Passed | 77.138 |
| `CatMiners.Tests.PointerInputTests.BackgroundDragAndWheelScroll` | Passed | 6.034 |
| `CatMiners.Tests.PointerInputTests.DisableReleasesGameplayMapAndEnableRestoresIt` | Passed | 4.629 |
| `CatMiners.Tests.PointerInputTests.DragCatAssignsItToRock` | Passed | 3.45 |
| `CatMiners.Tests.PointerInputTests.DragCatOntoSameLevelMerges` | Passed | 2.063 |
| `CatMiners.Tests.PointerInputTests.DynamiteHoldOnCellDoesNotAutoScroll` | Passed | 2.285 |
| `CatMiners.Tests.PointerInputTests.GestureStartedOnUiDoesNotTouchBoard` | Passed | 1.39 |
| `CatMiners.Tests.PointerInputTests.PointerInputReadsGameplayActionMap` | Passed | 0.733 |
| `CatMiners.Tests.PointerInputTests.TapOpensHireBarrelAndCollectsLoot` | Passed | 2.941 |
| `CatMiners.Tests.PointerInputTests.TapSelectsCatAndObject` | Passed | 2.174 |
| `CatMiners.Tests.PointerInputTests.TouchDragAssignsAndCancelRestores` | Passed | 2.516 |
| `CatMiners.Tests.TutorialFlowTests.ClearHintsShowOnlyOnce` | Passed | 13.265 |
| `CatMiners.Tests.TutorialFlowTests.HireHintOnlyWhenAffordableAndStepsCompleteFromState` | Passed | 2.124 |
| `CatMiners.Tests.TutorialFlowTests.Zone1ExitDoorNextMineAndTransition` | Passed | 15.26 |
| `CatMiners.Tests.TutorialFlowTests.Zone1StepsLikeAPlayer` | Passed | 21.824 |
| `CatMiners.Tests.UiScreenshotTests.CampaignEndScreen` | Passed | 1.533 |
| `CatMiners.Tests.UiScreenshotTests.MainHudContextAndWindows` | Passed | 3.908 |
| `CatMiners.Tests.UiScreenshotTests.ShaftContextAndBlueprints` | Passed | 2.643 |
| `CatMiners.Tests.UiScreenshotTests.TutorialHintPointsAtHire` | Passed | 1.948 |
| `CatMiners.Tests.VisualAssetCatalogTests.WriteVisualAssetCatalog` | Passed | 0.891 |
| `CatMiners.Tests.VisualPolishTests.Atlas_HasEveryCatFrameForEveryCoatHelmetAndWeapon` | Passed | 0.011 |
| `CatMiners.Tests.VisualPolishTests.Atlas_HasThemeVariantsOfEveryTileObstructionAndWallDecor` | Passed | 0.002 |
| `CatMiners.Tests.VisualPolishTests.CatsShowWeaponAndHelmetOfTheirLevel` | Passed | 4.182 |
| `CatMiners.Tests.VisualPolishTests.ShaftStatesAreVisiblyDistinct` | Passed | 1.205 |
| `CatMiners.Tests.VisualPolishTests.ThemeConfig_AssignsZoneRangesAndRepeats` | Passed | 0.012 |
| `CatMiners.Tests.VisualPolishTests.ThemeConfig_RejectsBrokenConfig` | Passed | 0.005 |
| `CatMiners.Tests.VisualPolishTests.VolumeGroupsAreSavedAndApplied` | Passed | 0.626 |
| `CatMiners.Tests.VisualPolishTests.WeaponAndHelmetFollowEightLevelCycle` | Passed | 0.002 |
| `CatMiners.Tests.VisualPolishTests.ZoneThemeDrivesBackgroundFogDecorAndMusic` | Passed | 1.565 |
| `CatMiners.Tests.VisualShowcaseTests.ZonesAtThreeAspects` | Passed | 26.718 |
| `IntegrationTests.Integration_CanChangeInputBackendPlayerSettingInEditor` | Passed | 0.031 |
| `IntegrationTests.Integration_CanSendAndReceiveEvents` | Passed | 0.083 |
| `IntegrationTests.WindowsInput_MouseMovements_AreDetected` | Skipped | 0 |
| `IntegrationTests.WindowsInput_RemoteDesktopMouseMovements_AreDetected` | Skipped | 0 |

</details>

## Валидатор баланса: все зоны

Зон: 144, ошибок: 0, предупреждений: 0. Прогон в Unity (`Docs/Reports/validation.json`, 10/06/2026 22:23:53): зон 144, ошибок 0, предупреждений 0.

<details><summary>Сводка по зонам</summary>

| Зона | Размер | Камни | Шахты | Ворота | Препятствия | Клеток r/s/x | Секторы | Выход | Шахты (id:ур.:карта:ур.) |
|---|---|---:|---:|---:|---:|---:|---:|---|---|
| zone1 | 7×12 | 8 | 0 | 0 | 12 | 43 | 1 | ур. 1, scg_m001_exit |  |
| zone2 | 7×13 | 13 | 1 | 0 | 15 | 52 | 1 | ур. 2, scg_m002_exit | amethyst:1:ca002:1 |
| zone3 | 7×16 | 20 | 2 | 0 | 11 | 63 | 1 | ур. 3, scg_m003_exit | citrine:2:ca003:1, amethyst:1:ca002:2 |
| zone4 | 7×18 | 28 | 3 | 0 | 14 | 74 | 1 | ур. 4, scg_m004_exit | agate:3:ca004:2, citrine:3:ca003:2, amethyst:2:ca002:3 |
| zone5 | 7×26 | 38 | 4 | 1 | 19 | 101 | 2 | ур. 5, scg_m005_exit | topaz:4:ca005:3, agate:3:ca004:3, citrine:3:ca003:3, amethyst:2:ca002:4 |
| zone6 | 7×30 | 50 | 4 | 1 | 22 | 118 | 2 | ур. 6, scg_m006_exit | topaz:5:ca005:4, agate:5:ca016:3, citrine:4:ca003:4, amethyst:2:ca014:3 |
| zone7 | 7×18 | 33 | 4 | 0 | 14 | 78 | 1 | ур. 7, scg_m007_exit | opal:7:ca006:4, topaz:6:ca005:5, agate:4:ca004:4, citrine:3:ca003:5 |
| zone8 | 7×30 | 56 | 4 | 1 | 19 | 130 | 2 | ур. 8, scg_m008_exit | opal:8:ca006:5, topaz:6:ca005:5, agate:5:ca004:5, amethyst:3:ca014:4 |
| zone9 | 7×30 | 66 | 5 | 1 | 21 | 135 | 2 | ур. 9, scg_m009_exit | opal:8:ca006:6, topaz:6:ca017:3, agate:8:ca016:4, citrine:5:ca015:4, amethyst:3:ca002:5 |
| zone10 | 7×38 | 75 | 5 | 2 | 27 | 162 | 3 | ур. 10, scg_m010_exit | jade:7:ca007:4, opal:9:ca006:6, topaz:6:ca005:6, citrine:5:ca015:5, amethyst:4:ca002:6 |
| zone11 | 7×18 | 36 | 4 | 0 | 11 | 76 | 1 | ур. 9, GachaCrusher | jade:9:ca007:5, topaz:8:ca017:4, agate:5:ca016:5, citrine:3:ca003:6 |
| zone12 | 7×31 | 69 | 5 | 1 | 27 | 140 | 2 | ур. 10, GachaCrusher | jade:10:ca007:6, opal:7:ca018:3, agate:9:ca004:6, amethyst:3:ca014:5, citrine:6:ca003:7 |
| zone13 | 7×40 | 81 | 6 | 2 | 30 | 172 | 3 | ур. 11, GachaCrusher | jade:10:ca007:7, opal:9:ca018:4, topaz:7:ca017:5, agate:5:ca004:7, citrine:8:ca015:6, amethyst:4:ca002:7 |
| zone14 | 7×31 | 69 | 5 | 1 | 25 | 143 | 2 | ур. 11, GachaCrusher | onyx:10:ca008:5, opal:7:ca006:7, topaz:9:ca005:7, agate:6:ca016:6, amethyst:3:ca014:6 |
| zone15 | 7×32 | 67 | 6 | 1 | 26 | 149 | 2 | ур. 12, GachaCrusher | onyx:11:ca008:6, jade:10:ca007:7, opal:7:ca018:5, agate:8:ca016:6, topaz:10:ca005:7, citrine:4:ca015:6 |
| zone16 | 7×42 | 96 | 7 | 2 | 30 | 190 | 3 | ур. 13, GachaCrusher | onyx:13:ca008:7, jade:9:ca007:8, opal:11:ca018:5, topaz:7:ca017:6, agate:9:ca004:8, citrine:6:ca015:6, amethyst:4:ca014:6 |
| zone17 | 7×38 | 85 | 5 | 2 | 29 | 163 | 3 | ур. 13, GachaCrusher | onyx:7:ca008:7, jade:11:ca019:4, topaz:6:ca017:6, citrine:8:ca003:8, amethyst:4:ca002:8 |
| zone18 | 7×30 | 63 | 5 | 1 | 25 | 133 | 2 | ур. 12, GachaCrusher | onyx:12:ca008:8, jade:6:ca019:5, opal:9:ca006:8, topaz:7:ca005:8, agate:4:ca004:8 |
| zone19 | 7×40 | 82 | 6 | 2 | 30 | 176 | 3 | ур. 13, GachaCrusher | sapphire:12:ca009:6, onyx:9:ca008:8, jade:11:ca019:5, opal:7:ca018:6, agate:9:ca016:7, citrine:4:ca003:8 |
| zone20 | 7×52 | 126 | 8 | 3 | 37 | 233 | 4 | ур. 14, GachaCrusher | sapphire:12:ca009:7, onyx:13:ca008:8, jade:8:ca019:6, topaz:10:ca005:8, opal:11:ca018:6, agate:7:ca016:7, citrine:9:ca015:7, amethyst:5:ca002:8 |
| zone21 | 7×42 | 100 | 6 | 2 | 31 | 188 | 3 | ур. 13, GachaCrusher | sapphire:8:ca009:7, opal:6:ca006:9, jade:12:ca019:6, topaz:11:ca017:7, agate:8:ca004:9, citrine:4:ca015:7 |
| zone22 | 7×31 | 74 | 5 | 1 | 22 | 140 | 2 | ур. 14, GachaCrusher | sapphire:12:ca009:8, onyx:7:ca020:4, opal:9:ca006:9, topaz:6:ca005:9, amethyst:4:ca014:7 |
| zone23 | 7×43 | 105 | 7 | 2 | 29 | 194 | 3 | ур. 15, GachaCrusher | sapphire:16:ca009:8, onyx:9:ca020:5, jade:14:ca007:9, topaz:12:ca017:7, agate:7:ca004:9, amethyst:4:ca014:7, citrine:5:ca003:9 |
| zone24 | 7×41 | 100 | 6 | 2 | 27 | 184 | 3 | ур. 16, GachaCrusher | tourmaline:17:ca010:6, sapphire:12:ca009:9, onyx:14:ca020:6, jade:13:ca007:9, opal:10:ca018:7, agate:5:ca004:9 |
| zone25 | 7×32 | 76 | 6 | 1 | 23 | 148 | 2 | ур. 15, GachaCrusher | tourmaline:15:ca010:7, sapphire:14:ca009:9, onyx:8:ca020:6, opal:12:ca018:7, agate:10:ca004:10, amethyst:5:ca002:9 |
| zone26 | 7×43 | 104 | 7 | 2 | 33 | 199 | 3 | ур. 16, GachaCrusher | tourmaline:10:ca010:8, onyx:16:ca008:9, jade:14:ca019:7, topaz:9:ca005:9, agate:12:ca004:10, citrine:10:ca015:8, amethyst:6:ca002:9 |
| zone27 | 7×52 | 122 | 8 | 3 | 38 | 232 | 4 | ур. 17, GachaCrusher | sapphire:12:ca021:5, tourmaline:18:ca010:8, jade:15:ca019:7, onyx:16:ca008:9, opal:14:ca006:10, agate:8:ca004:10, citrine:10:ca015:8, amethyst:6:ca002:9 |
| zone28 | 7×42 | 96 | 6 | 2 | 30 | 190 | 3 | ур. 17, GachaCrusher | tourmaline:12:ca010:9, sapphire:15:ca021:5, onyx:13:ca020:7, opal:8:ca006:10, topaz:9:ca005:9, amethyst:7:ca002:10 |
| zone29 | 7×43 | 108 | 7 | 2 | 32 | 197 | 3 | ур. 18, GachaCrusher | sapphire:17:ca021:6, onyx:9:ca020:7, jade:16:ca007:10, opal:8:ca018:8, agate:10:ca016:8, topaz:12:ca005:10, citrine:5:ca015:8 |
| zone30 | 7×55 | 132 | 9 | 3 | 41 | 249 | 4 | ур. 19, GachaCrusher | tourmaline:20:ca010:9, sapphire:19:ca021:6, onyx:17:ca020:7, jade:10:ca007:10, opal:15:ca018:8, topaz:14:ca005:10, agate:8:ca016:8, citrine:10:ca015:8, amethyst:6:ca002:10 |
| zone31 | 7×42 | 97 | 7 | 2 | 31 | 192 | 3 | ур. 18, GachaCrusher | aquamarine:18:ca011:6, tourmaline:11:ca010:9, onyx:16:ca008:10, jade:15:ca007:10, topaz:13:ca017:8, amethyst:5:ca014:8, citrine:8:ca003:10 |
| zone32 | 7×32 | 79 | 6 | 1 | 22 | 150 | 2 | ур. 17, GachaCrusher | aquamarine:18:ca011:7, sapphire:17:ca021:7, jade:15:ca007:10, opal:13:ca018:8, topaz:12:ca017:8, agate:8:ca016:8 |
| zone33 | 7×44 | 116 | 8 | 2 | 27 | 200 | 3 | ур. 18, GachaCrusher | tourmaline:10:ca022:5, aquamarine:19:ca011:7, sapphire:18:ca021:7, onyx:16:ca008:10, opal:8:ca018:8, agate:12:ca016:8, amethyst:6:ca014:8, citrine:7:ca003:10 |
| zone34 | 7×42 | 104 | 7 | 2 | 26 | 189 | 3 | ур. 19, GachaCrusher | aquamarine:9:ca011:8, tourmaline:17:ca022:5, sapphire:16:ca021:7, jade:7:ca019:8, topaz:12:ca017:8, agate:11:ca016:8, citrine:5:ca003:10 |
| zone35 | 7×44 | 107 | 8 | 2 | 36 | 204 | 3 | ур. 20, GachaCrusher | tourmaline:19:ca022:6, sapphire:18:ca009:10, onyx:11:ca008:10, jade:18:ca019:8, opal:16:ca006:11, topaz:14:ca017:8, citrine:11:ca003:10, amethyst:8:ca014:8 |
| zone36 | 7×56 | 143 | 9 | 3 | 45 | 252 | 4 | ур. 21, GachaCrusher | aquamarine:20:ca011:9, tourmaline:18:ca022:7, sapphire:19:ca009:10, jade:12:ca019:8, opal:17:ca006:11, topaz:9:ca017:8, agate:13:ca004:11, citrine:10:ca003:11, amethyst:6:ca014:8 |
| zone37 | 7×32 | 80 | 7 | 1 | 21 | 150 | 2 | ур. 20, GachaCrusher | aquamarine:19:ca011:9, tourmaline:17:ca022:7, onyx:9:ca020:8, jade:15:ca019:8, opal:13:ca006:11, topaz:11:ca017:8, citrine:8:ca003:11 |
| zone38 | 7×45 | 111 | 8 | 2 | 32 | 212 | 3 | ур. 21, GachaCrusher | emerald:20:ca012:7, aquamarine:19:ca011:9, tourmaline:18:ca010:10, sapphire:16:ca021:8, onyx:8:ca020:8, opal:14:ca006:11, topaz:11:ca005:11, agate:7:ca004:11 |
| zone39 | 7×43 | 109 | 7 | 2 | 35 | 197 | 3 | ур. 22, GachaCrusher | emerald:18:ca012:8, aquamarine:10:ca011:9, tourmaline:18:ca010:10, sapphire:15:ca021:8, onyx:9:ca020:8, agate:12:ca004:11, amethyst:10:ca014:8 |
| zone40 | 7×54 | 143 | 10 | 3 | 36 | 250 | 4 | ур. 23, GachaCrusher | emerald:22:ca012:8, aquamarine:21:ca011:10, tourmaline:19:ca022:8, sapphire:20:ca021:8, jade:18:ca007:11, opal:10:ca006:11, agate:13:ca016:9, topaz:16:ca005:11, amethyst:6:ca002:11, citrine:9:ca003:11 |
| zone41 | 7×32 | 77 | 6 | 1 | 25 | 150 | 2 | ур. 21, GachaCrusher | aquamarine:18:ca011:10, emerald:20:ca012:8, tourmaline:16:ca022:8, jade:15:ca007:11, citrine:10:ca003:11, agate:13:ca016:9 |
| zone42 | 7×44 | 119 | 8 | 2 | 24 | 201 | 3 | ур. 22, GachaCrusher | emerald:23:ca012:9, aquamarine:21:ca023:5, tourmaline:11:ca022:8, sapphire:18:ca009:11, onyx:9:ca008:11, jade:16:ca007:11, topaz:14:ca005:11, agate:7:ca016:9 |
| zone43 | 7×55 | 135 | 9 | 3 | 43 | 256 | 4 | ур. 23, GachaCrusher | emerald:23:ca012:9, aquamarine:12:ca023:6, tourmaline:22:ca022:8, sapphire:21:ca009:11, onyx:19:ca008:11, jade:17:ca007:11, agate:14:ca016:9, citrine:13:ca015:9, amethyst:9:ca002:11 |
| zone44 | 7×40 | 95 | 6 | 2 | 26 | 174 | 3 | ур. 22, GachaCrusher | emerald:11:ca012:9, tourmaline:9:ca010:11, aquamarine:18:ca023:6, sapphire:16:ca009:11, opal:12:ca006:11, citrine:10:ca015:9 |
| zone45 | 7×32 | 85 | 7 | 1 | 19 | 151 | 2 | ур. 23, GachaCrusher | aquamarine:22:ca023:7, tourmaline:20:ca010:11, sapphire:16:ca009:11, onyx:17:ca008:11, topaz:14:ca017:9, agate:10:ca016:9, amethyst:6:ca002:11 |
| zone46 | 7×52 | 123 | 8 | 3 | 37 | 237 | 4 | ур. 25, GachaCrusher | emerald:26:ca012:10, aquamarine:13:ca023:7, tourmaline:22:ca010:11, onyx:11:ca008:11, topaz:18:ca017:9, agate:9:ca016:9, citrine:14:ca015:9, amethyst:7:ca002:11 |
| zone47 | 7×43 | 98 | 7 | 2 | 27 | 195 | 3 | ур. 24, GachaCrusher | diamond:18:ca013:7, emerald:12:ca012:10, sapphire:19:ca009:11, opal:16:ca018:9, topaz:15:ca017:9, citrine:10:ca015:9, amethyst:8:ca014:9 |
| zone48 | 7×44 | 109 | 9 | 2 | 34 | 206 | 3 | ур. 25, GachaCrusher | diamond:23:ca013:8, emerald:21:ca012:10, aquamarine:19:ca011:11, tourmaline:20:ca010:11, onyx:17:ca008:11, jade:10:ca019:9, opal:16:ca018:9, agate:13:ca004:12, amethyst:9:ca014:9 |
| zone49 | 7×31 | 66 | 6 | 1 | 22 | 142 | 2 | ур. 23, GachaCrusher | diamond:21:ca013:8, aquamarine:11:ca011:11, sapphire:18:ca009:11, jade:16:ca019:9, topaz:14:ca017:9, citrine:10:ca003:12 |
| zone50 | 7×56 | 138 | 11 | 3 | 39 | 261 | 4 | ур. 26, GachaCrusher | diamond:25:ca013:9, emerald:14:ca012:10, aquamarine:23:ca011:11, onyx:12:ca020:9, tourmaline:16:ca010:11, jade:20:ca019:9, opal:17:ca018:9, topaz:10:ca017:9, agate:15:ca004:12, citrine:11:ca003:12, amethyst:6:ca014:9 |
| zone51 | 7×43 | 106 | 7 | 2 | 31 | 197 | 3 | ур. 25, GachaCrusher | diamond:24:ca013:9, aquamarine:20:ca023:8, onyx:21:ca020:9, opal:18:ca018:9, topaz:11:ca005:12, citrine:13:ca003:12, amethyst:10:ca014:9 |
| zone52 | 7×41 | 97 | 8 | 2 | 25 | 185 | 3 | ур. 24, GachaCrusher | emerald:23:ca024:5, aquamarine:22:ca023:8, tourmaline:14:ca010:11, sapphire:20:ca021:9, onyx:13:ca020:9, jade:14:ca019:9, topaz:12:ca005:12, agate:7:ca004:12 |
| zone53 | 7×56 | 156 | 10 | 3 | 30 | 262 | 4 | ур. 28, GachaCrusher | diamond:26:ca013:9, emerald:15:ca024:6, aquamarine:24:ca023:8, tourmaline:14:ca022:9, sapphire:21:ca021:9, jade:17:ca019:9, opal:12:ca006:12, topaz:16:ca005:12, citrine:13:ca003:12, amethyst:8:ca014:9 |
| zone54 | 7×33 | 85 | 7 | 1 | 19 | 157 | 2 | ур. 25, GachaCrusher | emerald:22:ca012:11, tourmaline:20:ca022:9, onyx:16:ca020:9, opal:17:ca006:12, agate:15:ca004:12, citrine:11:ca003:12, amethyst:9:ca002:12 |
| zone55 | 7×42 | 105 | 8 | 2 | 26 | 192 | 3 | ур. 26, GachaCrusher | diamond:25:ca013:10, emerald:19:ca024:6, aquamarine:23:ca023:8, sapphire:17:ca021:9, onyx:11:ca020:9, opal:16:ca006:12, topaz:12:ca005:12, amethyst:7:ca002:12 |
| zone56 | 7×46 | 132 | 9 | 2 | 26 | 220 | 3 | ур. 27, GachaCrusher | diamond:26:ca013:10, emerald:18:ca012:11, tourmaline:24:ca022:9, sapphire:22:ca021:9, topaz:10:ca005:12, jade:15:ca007:12, agate:17:ca004:12, citrine:14:ca015:10, amethyst:8:ca002:12 |
| zone57 | 7×54 | 150 | 8 | 3 | 28 | 245 | 4 | ур. 28, GachaCrusher | diamond:23:ca013:10, garnet:26:ca039:7, tourmaline:21:ca022:9, emerald:25:ca024:7, jade:18:ca007:12, sapphire:22:ca021:9, citrine:10:ca015:10, opal:15:ca006:12 |
| zone58 | 7×33 | 91 | 6 | 1 | 21 | 157 | 2 | ур. 26, GachaCrusher | garnet:23:ca039:8, emerald:21:ca024:7, aquamarine:19:ca023:9, jade:18:ca007:12, citrine:15:ca015:10, amethyst:11:ca002:12 |
| zone59 | 7×48 | 126 | 9 | 2 | 30 | 234 | 3 | ур. 28, GachaCrusher | garnet:25:ca039:8, diamond:24:ca013:10, tourmaline:23:ca022:9, aquamarine:21:ca023:9, onyx:15:ca008:12, opal:15:ca006:12, jade:17:ca007:12, citrine:11:ca015:10, amethyst:8:ca014:10 |
| zone60 | 7×60 | 165 | 11 | 3 | 39 | 290 | 4 | ур. 29, GachaCrusher | garnet:27:ca039:9, diamond:20:ca013:11, emerald:26:ca024:8, aquamarine:24:ca023:9, tourmaline:21:ca010:12, sapphire:23:ca009:12, onyx:22:ca008:12, jade:18:ca007:12, topaz:19:ca005:12, agate:16:ca016:10, amethyst:13:ca014:10 |
| zone61 | 7×45 | 130 | 7 | 2 | 22 | 205 | 3 | ур. 27, GachaCrusher | garnet:26:ca039:9, emerald:25:ca024:8, aquamarine:22:ca023:9, sapphire:20:ca009:12, onyx:16:ca008:12, agate:17:ca016:10, citrine:14:ca015:10 |
| zone62 | 7×46 | 126 | 8 | 2 | 29 | 219 | 3 | ур. 28, GachaCrusher | diamond:27:ca013:11, emerald:19:ca024:8, tourmaline:24:ca010:12, sapphire:21:ca009:12, onyx:17:ca008:12, opal:19:ca018:10, topaz:16:ca005:12, agate:9:ca016:10 |
| zone63 | 7×58 | 162 | 10 | 3 | 28 | 276 | 4 | ур. 32, GachaCrusher | garnet:27:ca039:10, diamond:28:ca025:5, emerald:23:ca024:8, tourmaline:25:ca010:12, sapphire:20:ca009:12, onyx:15:ca008:12, opal:20:ca018:10, topaz:17:ca005:12, citrine:14:ca015:10, amethyst:10:ca014:10 |
| zone64 | 7×34 | 97 | 6 | 1 | 20 | 162 | 2 | ур. 27, GachaCrusher | garnet:25:ca039:10, diamond:23:ca025:6, aquamarine:21:ca011:12, jade:19:ca007:12, topaz:17:ca017:10, amethyst:12:ca014:10 |
| zone65 | 7×47 | 138 | 9 | 2 | 23 | 227 | 3 | ур. 31, GachaCrusher | garnet:28:ca039:10, diamond:25:ca025:6, emerald:26:ca024:8, aquamarine:23:ca011:12, tourmaline:20:ca010:12, opal:21:ca018:10, topaz:18:ca017:10, agate:15:ca016:10, citrine:10:ca015:10 |
| zone66 | 7×61 | 171 | 11 | 3 | 36 | 297 | 4 | ур. 35, GachaCrusher | garnet:33:ca039:11, diamond:25:ca025:7, emerald:30:ca024:8, aquamarine:27:ca011:12, sapphire:26:ca009:12, onyx:24:ca008:12, jade:21:ca007:12, opal:18:ca018:10, agate:20:ca016:10, citrine:16:ca015:10, amethyst:12:ca014:10 |
| zone67 | 7×34 | 89 | 7 | 1 | 22 | 162 | 2 | ур. 30, GachaCrusher | diamond:27:ca025:7, tourmaline:25:ca010:12, sapphire:21:ca009:12, jade:22:ca019:10, topaz:20:ca017:10, agate:17:ca016:10, amethyst:13:ca002:13 |
| zone68 | 7×48 | 142 | 9 | 2 | 24 | 234 | 3 | ур. 33, GachaCrusher | heliodor:31:ca041:8, garnet:30:ca039:11, emerald:24:ca012:12, aquamarine:27:ca011:12, onyx:25:ca008:12, jade:23:ca019:10, opal:22:ca018:10, citrine:17:ca015:10, amethyst:12:ca002:13 |
| zone69 | 7×45 | 113 | 8 | 2 | 31 | 212 | 3 | ур. 32, GachaCrusher | heliodor:30:ca041:9, diamond:25:ca025:8, emerald:27:ca024:9, tourmaline:24:ca010:12, sapphire:20:ca009:12, onyx:21:ca020:10, topaz:19:ca017:10, agate:16:ca004:13 |
| zone70 | 7×62 | 185 | 12 | 3 | 34 | 304 | 4 | ур. 37, GachaCrusher | heliodor:35:ca041:9, garnet:33:ca039:11, diamond:31:ca025:8, emerald:32:ca012:12, aquamarine:30:ca011:12, tourmaline:27:ca010:12, sapphire:28:ca021:10, onyx:26:ca020:10, jade:23:ca019:10, opal:22:ca018:10, agate:18:ca004:13, amethyst:13:ca002:13 |
| zone71 | 7×48 | 138 | 9 | 2 | 30 | 234 | 3 | ур. 34, GachaCrusher | heliodor:32:ca041:10, diamond:28:ca013:12, emerald:26:ca024:9, tourmaline:29:ca022:10, sapphire:27:ca021:10, opal:24:ca006:13, topaz:20:ca017:10, agate:21:ca004:13, citrine:14:ca003:13 |
| zone72 | 7×34 | 93 | 8 | 1 | 17 | 164 | 2 | ур. 31, GachaCrusher | aquamarine:28:ca011:12, diamond:29:ca013:12, onyx:21:ca020:10, tourmaline:26:ca022:10, opal:22:ca006:13, jade:24:ca019:10, topaz:12:ca005:13, citrine:18:ca003:13 |
| zone73 | 7×60 | 174 | 10 | 3 | 35 | 291 | 4 | ур. 36, GachaCrusher | heliodor:34:ca041:10, garnet:31:ca039:12, emerald:32:ca024:9, aquamarine:30:ca023:10, sapphire:26:ca021:10, onyx:27:ca020:10, topaz:25:ca005:13, agate:22:ca004:13, citrine:19:ca003:13, amethyst:15:ca002:13 |
| zone74 | 7×47 | 127 | 9 | 2 | 29 | 227 | 3 | ур. 35, GachaCrusher | garnet:33:ca040:5, diamond:30:ca025:9, aquamarine:26:ca023:10, tourmaline:27:ca022:10, sapphire:25:ca021:10, jade:20:ca007:13, opal:22:ca006:13, agate:18:ca004:13, amethyst:13:ca002:13 |
| zone75 | 7×46 | 128 | 9 | 2 | 27 | 220 | 3 | ур. 33, GachaCrusher | garnet:30:ca040:6, heliodor:32:ca041:10, emerald:27:ca012:12, diamond:29:ca013:12, aquamarine:25:ca023:10, tourmaline:23:ca022:10, jade:20:ca007:13, topaz:17:ca005:13, agate:14:ca004:13 |
| zone76 | 7×61 | 187 | 11 | 3 | 36 | 299 | 4 | ур. 38, GachaCrusher | heliodor:36:ca041:11, garnet:33:ca040:7, diamond:33:ca025:9, emerald:31:ca012:12, aquamarine:26:ca023:10, sapphire:29:ca021:10, onyx:27:ca008:13, opal:23:ca006:13, topaz:24:ca005:13, citrine:21:ca003:13, amethyst:16:ca014:11 |
| zone77 | 7×48 | 138 | 8 | 2 | 31 | 235 | 3 | ур. 34, GachaCrusher | garnet:33:ca040:7, emerald:29:ca012:12, tourmaline:30:ca022:10, onyx:27:ca008:13, jade:24:ca007:13, topaz:23:ca005:13, agate:20:ca016:11, citrine:15:ca003:13 |
| zone78 | 7×33 | 83 | 7 | 1 | 21 | 156 | 2 | ур. 32, GachaCrusher | heliodor:31:ca041:11, diamond:29:ca025:9, aquamarine:26:ca023:10, onyx:25:ca008:13, opal:22:ca006:13, agate:18:ca016:11, amethyst:13:ca014:11 |
| zone79 | 7×49 | 142 | 10 | 2 | 29 | 242 | 3 | ур. 37, GachaCrusher | heliodor:35:ca041:11, garnet:32:ca040:8, aquamarine:28:ca023:10, tourmaline:30:ca022:10, sapphire:29:ca009:13, jade:27:ca007:13, topaz:23:ca005:13, agate:24:ca016:11, citrine:20:ca003:13, amethyst:17:ca014:11 |
| zone80 | 7×63 | 182 | 12 | 3 | 39 | 313 | 4 | ур. 40, GachaCrusher | ruby:38:ca044:8, heliodor:36:ca041:11, garnet:34:ca040:8, diamond:35:ca025:9, emerald:32:ca024:10, aquamarine:26:ca023:10, tourmaline:31:ca010:13, sapphire:28:ca009:13, onyx:24:ca008:13, topaz:25:ca005:13, agate:22:ca016:11, citrine:15:ca015:11 |
| zone81 | 7×46 | 112 | 8 | 2 | 33 | 221 | 3 | ур. 36, GachaCrusher | ruby:35:ca044:9, garnet:33:ca039:12, emerald:31:ca024:10, tourmaline:30:ca010:13, jade:27:ca007:13, opal:22:ca006:13, topaz:23:ca017:11, agate:18:ca016:11 |
| zone82 | 7×48 | 130 | 9 | 2 | 33 | 235 | 3 | ур. 38, GachaCrusher | ruby:37:ca044:10, diamond:35:ca025:10, emerald:32:ca024:10, aquamarine:30:ca011:13, sapphire:25:ca009:13, onyx:26:ca008:13, topaz:23:ca017:11, amethyst:16:ca014:11, citrine:20:ca015:11 |
| zone83 | 7×60 | 170 | 11 | 3 | 37 | 292 | 4 | ур. 41, GachaCrusher | heliodor:37:ca041:12, ruby:39:ca044:10, diamond:35:ca025:10, garnet:36:ca040:9, aquamarine:33:ca011:13, tourmaline:32:ca010:13, sapphire:30:ca009:13, jade:25:ca007:13, citrine:22:ca015:11, agate:24:ca016:11, amethyst:18:ca014:11 |
| zone84 | 7×49 | 146 | 10 | 2 | 26 | 245 | 3 | ур. 39, GachaCrusher | heliodor:38:ca041:12, garnet:36:ca040:9, emerald:32:ca012:13, aquamarine:33:ca011:13, tourmaline:31:ca010:13, onyx:29:ca008:13, opal:26:ca018:11, topaz:27:ca017:11, agate:22:ca016:11, citrine:17:ca015:11 |
| zone85 | 7×34 | 81 | 8 | 1 | 23 | 165 | 2 | ур. 35, GachaCrusher | ruby:32:ca044:10, heliodor:31:ca041:12, diamond:30:ca025:10, emerald:27:ca012:13, sapphire:28:ca009:13, opal:25:ca018:11, topaz:21:ca017:11, amethyst:16:ca014:11 |
| zone86 | 7×62 | 175 | 12 | 3 | 39 | 305 | 4 | ур. 42, GachaCrusher | ruby:40:ca044:11, heliodor:38:ca042:6, garnet:37:ca040:9, diamond:36:ca025:10, emerald:34:ca012:13, aquamarine:31:ca011:13, tourmaline:33:ca010:13, onyx:30:ca008:13, jade:27:ca019:11, topaz:26:ca017:11, agate:21:ca016:11, citrine:15:ca015:11 |
| zone87 | 7×47 | 121 | 10 | 2 | 25 | 228 | 3 | ур. 40, GachaCrusher | ruby:35:ca044:11, heliodor:33:ca042:7, garnet:34:ca040:9, diamond:32:ca025:10, sapphire:26:ca009:13, aquamarine:28:ca011:13, jade:27:ca019:11, opal:25:ca018:11, agate:20:ca016:11, amethyst:17:ca014:11 |
| zone88 | 7×48 | 125 | 9 | 2 | 31 | 236 | 3 | ур. 37, GachaCrusher | heliodor:36:ca042:7, diamond:35:ca013:13, emerald:33:ca012:13, aquamarine:32:ca011:13, tourmaline:30:ca010:13, onyx:27:ca020:11, jade:25:ca019:11, topaz:22:ca017:11, citrine:19:ca015:11 |
| zone89 | 7×61 | 163 | 11 | 3 | 44 | 299 | 4 | ур. 43, GachaCrusher | ruby:41:ca044:11, heliodor:39:ca042:8, garnet:37:ca039:13, diamond:36:ca013:13, emerald:35:ca012:13, tourmaline:32:ca010:13, onyx:31:ca020:11, opal:29:ca018:11, topaz:26:ca017:11, agate:24:ca016:11, amethyst:18:ca014:11 |
| zone90 | 7×63 | 167 | 13 | 3 | 38 | 315 | 4 | ур. 45, GachaCrusher | ruby:42:ca044:11, heliodor:38:ca042:8, garnet:39:ca039:13, diamond:37:ca013:13, emerald:35:ca012:13, aquamarine:33:ca011:13, tourmaline:34:ca010:13, sapphire:31:ca021:11, jade:27:ca019:11, opal:28:ca018:11, topaz:26:ca017:11, citrine:23:ca003:14, amethyst:20:ca014:11 |
| zone91 | 7×34 | 87 | 7 | 1 | 20 | 164 | 2 | ур. 38, GachaCrusher | heliodor:35:ca042:8, garnet:33:ca039:13, aquamarine:28:ca011:13, sapphire:29:ca021:11, jade:26:ca019:11, agate:22:ca016:11, citrine:17:ca003:14 |
| zone92 | 7×49 | 137 | 9 | 2 | 30 | 243 | 3 | ур. 41, GachaCrusher | garnet:38:ca039:13, ruby:39:ca044:12, diamond:34:ca013:13, tourmaline:33:ca022:11, sapphire:32:ca021:11, onyx:30:ca020:11, agate:25:ca004:14, opal:27:ca018:11, amethyst:21:ca002:11 |
| zone93 | 7×62 | 183 | 12 | 3 | 40 | 310 | 4 | ур. 46, GachaCrusher | alexandrite:43:ca046:7, ruby:42:ca044:12, heliodor:38:ca042:9, diamond:39:ca013:13, emerald:37:ca012:13, aquamarine:34:ca023:11, tourmaline:35:ca022:11, onyx:32:ca020:11, jade:29:ca019:11, topaz:28:ca005:14, agate:24:ca004:14, amethyst:19:ca014:11 |
| zone94 | 7×48 | 132 | 9 | 2 | 30 | 235 | 3 | ур. 43, GachaCrusher | alexandrite:40:ca046:8, heliodor:38:ca042:9, garnet:34:ca039:13, aquamarine:33:ca023:11, sapphire:31:ca021:11, onyx:28:ca020:11, opal:26:ca018:11, topaz:24:ca005:14, citrine:18:ca003:14 |
| zone95 | 7×49 | 137 | 10 | 2 | 31 | 242 | 3 | ур. 42, GachaCrusher | alexandrite:40:ca046:9, ruby:38:ca044:12, heliodor:35:ca042:9, diamond:34:ca013:13, emerald:32:ca024:11, tourmaline:30:ca022:11, jade:27:ca019:11, topaz:27:ca005:14, agate:23:ca004:14, amethyst:17:ca014:11 |
| zone96 | 7×63 | 181 | 12 | 3 | 35 | 313 | 4 | ур. 47, GachaCrusher | alexandrite:45:ca046:10, ruby:43:ca044:12, heliodor:41:ca042:9, emerald:37:ca024:11, garnet:38:ca039:13, sapphire:34:ca021:11, aquamarine:36:ca023:11, onyx:33:ca020:11, opal:31:ca018:11, agate:29:ca004:14, amethyst:22:ca002:14, citrine:26:ca003:14 |
| zone97 | 7×47 | 132 | 9 | 2 | 25 | 227 | 3 | ур. 44, GachaCrusher | alexandrite:41:ca046:10, ruby:39:ca045:6, heliodor:37:ca041:13, diamond:35:ca025:11, tourmaline:33:ca022:11, onyx:31:ca020:11, jade:28:ca019:11, topaz:25:ca005:14, citrine:22:ca003:14 |
| zone98 | 7×34 | 81 | 8 | 1 | 24 | 166 | 2 | ур. 39, GachaCrusher | ruby:35:ca045:7, alexandrite:37:ca046:10, heliodor:33:ca041:13, garnet:30:ca040:10, aquamarine:29:ca023:11, sapphire:27:ca021:11, opal:24:ca006:14, agate:19:ca004:14 |
| zone99 | 7×61 | 163 | 11 | 3 | 36 | 299 | 4 | ур. 48, GachaCrusher | ruby:44:ca045:7, garnet:41:ca040:10, diamond:40:ca025:11, emerald:38:ca024:11, tourmaline:34:ca022:11, sapphire:34:ca021:11, onyx:32:ca020:11, opal:27:ca006:14, topaz:28:ca005:14, citrine:25:ca003:14, amethyst:20:ca002:14 |
| zone100 | 7×64 | 179 | 13 | 3 | 38 | 320 | 4 | ур. 50, GachaCrusher | alexandrite:47:ca046:11, ruby:45:ca045:8, heliodor:42:ca041:13, diamond:39:ca025:11, garnet:41:ca040:10, emerald:37:ca024:11, aquamarine:36:ca023:11, tourmaline:34:ca022:11, onyx:31:ca020:11, jade:29:ca007:14, opal:27:ca006:14, agate:24:ca004:14, citrine:20:ca003:14 |
| zone101 | 7×49 | 146 | 9 | 2 | 28 | 241 | 3 | ур. 45, GachaCrusher | alexandrite:42:ca046:11, heliodor:40:ca041:13, diamond:37:ca025:11, aquamarine:35:ca023:11, sapphire:33:ca021:11, jade:31:ca007:14, topaz:28:ca005:14, agate:24:ca004:14, amethyst:21:ca002:14 |
| zone102 | 7×34 | 83 | 7 | 1 | 24 | 164 | 2 | ур. 40, GachaCrusher | ruby:36:ca045:8, garnet:34:ca040:10, emerald:32:ca024:11, tourmaline:29:ca022:11, onyx:28:ca008:14, opal:24:ca006:14, citrine:18:ca003:14 |
| zone103 | 7×62 | 165 | 10 | 3 | 53 | 306 | 4 | ур. 49, GachaCrusher | alexandrite:46:ca046:11, heliodor:42:ca042:10, garnet:41:ca040:10, diamond:38:ca025:11, aquamarine:34:ca023:11, onyx:35:ca008:14, jade:33:ca007:14, topaz:29:ca005:14, agate:26:ca004:14, amethyst:21:ca014:12 |
| zone104 | 7×46 | 118 | 9 | 2 | 28 | 221 | 3 | ур. 47, GachaCrusher | celestine:43:ca048:8, alexandrite:41:ca046:12, ruby:38:ca045:8, heliodor:36:ca042:10, emerald:34:ca024:11, tourmaline:31:ca022:11, sapphire:30:ca009:14, topaz:22:ca005:14, jade:27:ca007:14 |
| zone105 | 7×47 | 121 | 10 | 2 | 30 | 228 | 3 | ур. 46, GachaCrusher | celestine:44:ca048:9, alexandrite:42:ca046:12, ruby:40:ca045:8, garnet:37:ca040:11, diamond:34:ca025:11, aquamarine:30:ca023:11, onyx:29:ca008:14, opal:27:ca006:14, agate:24:ca004:14, amethyst:21:ca014:12 |
| zone106 | 7×63 | 177 | 12 | 3 | 35 | 313 | 4 | ур. 51, GachaCrusher | celestine:48:ca048:10, alexandrite:46:ca046:12, ruby:42:ca044:13, heliodor:40:ca042:10, garnet:37:ca040:11, emerald:36:ca024:11, aquamarine:34:ca023:11, sapphire:32:ca009:14, jade:29:ca007:14, topaz:28:ca005:14, citrine:25:ca015:12, amethyst:20:ca014:12 |
| zone107 | 7×34 | 87 | 7 | 1 | 24 | 166 | 2 | ур. 42, GachaCrusher | celestine:38:ca048:10, ruby:36:ca044:13, diamond:33:ca025:11, tourmaline:31:ca010:14, onyx:29:ca008:14, opal:26:ca006:14, citrine:23:ca015:12 |
| zone108 | 7×62 | 158 | 13 | 3 | 47 | 306 | 4 | ур. 52, GachaCrusher | alexandrite:49:ca047:6, heliodor:40:ca042:10, ruby:46:ca045:9, garnet:42:ca040:11, aquamarine:41:ca011:14, tourmaline:39:ca010:14, sapphire:36:ca009:14, onyx:35:ca008:14, jade:33:ca007:14, topaz:29:ca005:14, agate:30:ca004:14, citrine:28:ca015:12, amethyst:24:ca002:14 |
| zone109 | 7×48 | 117 | 9 | 2 | 34 | 235 | 3 | ур. 48, GachaCrusher | celestine:45:ca048:11, alexandrite:43:ca047:7, ruby:39:ca045:9, diamond:37:ca025:11, aquamarine:34:ca011:14, sapphire:31:ca009:14, opal:29:ca006:14, amethyst:22:ca002:12, topaz:26:ca005:14 |
| zone110 | 7×64 | 162 | 14 | 3 | 41 | 320 | 4 | ур. 53, GachaCrusher | celestine:50:ca048:12, alexandrite:48:ca047:8, ruby:45:ca044:13, heliodor:44:ca042:10, garnet:42:ca040:11, diamond:40:ca025:11, emerald:38:ca012:14, tourmaline:37:ca010:14, sapphire:36:ca009:14, onyx:34:ca008:14, jade:30:ca007:14, opal:31:ca006:14, agate:28:ca016:12, citrine:25:ca003:14 |
| zone111 | 7×34 | 92 | 8 | 1 | 14 | 166 | 2 | ур. 43, GachaCrusher | alexandrite:40:ca047:8, ruby:38:ca045:9, garnet:36:ca040:11, emerald:33:ca012:14, tourmaline:32:ca010:14, jade:30:ca007:14, topaz:27:ca005:14, agate:23:ca016:12 |
| zone112 | 7×47 | 125 | 10 | 2 | 28 | 228 | 3 | ур. 47, GachaCrusher | celestine:46:ca048:12, alexandrite:44:ca046:13, ruby:41:ca045:9, garnet:40:ca040:11, diamond:38:ca013:14, aquamarine:36:ca011:14, sapphire:33:ca009:14, onyx:32:ca008:14, topaz:29:ca017:12, amethyst:25:ca014:12 |
| zone113 | 7×34 | 78 | 8 | 1 | 25 | 165 | 2 | ур. 41, GachaCrusher | alexandrite:39:ca046:13, heliodor:37:ca042:11, emerald:36:ca012:14, aquamarine:33:ca011:14, tourmaline:31:ca010:14, jade:30:ca007:14, agate:27:ca016:12, citrine:24:ca015:12 |
| zone114 | 7×62 | 166 | 11 | 3 | 51 | 306 | 4 | ур. 52, GachaCrusher | amber:50:ca056:10, celestine:48:ca048:13, ruby:45:ca045:10, heliodor:43:ca042:11, garnet:40:ca039:14, diamond:38:ca013:14, sapphire:35:ca009:14, onyx:30:ca008:14, opal:32:ca006:14, topaz:28:ca017:12, amethyst:24:ca002:14 |
| zone115 | 7×49 | 123 | 10 | 2 | 30 | 242 | 3 | ур. 49, GachaCrusher | amber:47:ca056:11, alexandrite:45:ca046:13, ruby:42:ca045:10, garnet:39:ca039:14, emerald:37:ca012:14, tourmaline:33:ca010:14, jade:32:ca007:14, sapphire:34:ca009:14, opal:28:ca018:12, agate:23:ca016:12 |
| zone116 | 7×34 | 84 | 8 | 1 | 23 | 166 | 2 | ур. 44, GachaCrusher | celestine:42:ca048:13, alexandrite:40:ca047:9, heliodor:37:ca042:11, diamond:33:ca013:14, onyx:31:ca008:14, aquamarine:33:ca011:14, topaz:28:ca017:12, citrine:24:ca003:14 |
| zone117 | 7×64 | 179 | 13 | 3 | 42 | 320 | 4 | ур. 53, GachaCrusher | amber:52:ca056:12, alexandrite:47:ca047:9, celestine:50:ca049:6, ruby:46:ca045:10, heliodor:44:ca041:14, emerald:39:ca012:14, garnet:42:ca039:14, tourmaline:38:ca010:14, jade:36:ca007:14, opal:32:ca018:12, agate:29:ca004:14, topaz:31:ca017:12, amethyst:25:ca014:12 |
| zone118 | 7×34 | 77 | 8 | 1 | 21 | 165 | 2 | ур. 45, GachaCrusher | amber:42:ca056:12, celestine:41:ca049:7, heliodor:37:ca041:14, diamond:33:ca013:14, emerald:32:ca012:14, sapphire:30:ca009:14, opal:27:ca018:12, citrine:24:ca015:12 |
| zone119 | 7×49 | 114 | 11 | 2 | 33 | 242 | 3 | ур. 49, GachaCrusher | amber:45:ca056:13, alexandrite:43:ca047:9, ruby:38:ca044:14, diamond:39:ca013:14, garnet:40:ca039:14, tourmaline:37:ca010:14, onyx:34:ca008:14, topaz:31:ca017:12, jade:33:ca019:12, agate:27:ca016:12, amethyst:25:ca002:14 |
| zone120 | 7×64 | 169 | 14 | 3 | 44 | 320 | 4 | ур. 54, GachaCrusher | celestine:52:ca049:8, alexandrite:50:ca046:14, ruby:48:ca044:14, heliodor:46:ca041:14, garnet:43:ca039:14, emerald:39:ca012:14, sapphire:38:ca009:14, aquamarine:40:ca011:14, jade:36:ca019:12, opal:34:ca018:12, topaz:33:ca005:14, agate:31:ca016:12, citrine:28:ca003:14, amethyst:26:ca014:12 |
| zone121 | 7×48 | 114 | 10 | 2 | 36 | 235 | 3 | ур. 50, GachaCrusher | celestine:47:ca049:8, alexandrite:44:ca046:14, heliodor:42:ca041:14, diamond:41:ca013:14, emerald:39:ca012:14, tourmaline:38:ca010:14, onyx:35:ca020:12, opal:33:ca018:12, agate:30:ca004:14, citrine:25:ca015:12 |
| zone122 | 7×34 | 78 | 8 | 1 | 20 | 166 | 2 | ур. 46, GachaCrusher | amber:43:ca056:14, ruby:40:ca044:14, garnet:37:ca039:14, aquamarine:35:ca011:14, onyx:34:ca020:12, jade:32:ca019:12, agate:29:ca016:12, amethyst:26:ca002:14 |
| zone123 | 7×62 | 162 | 12 | 3 | 45 | 306 | 4 | ур. 55, GachaCrusher | moonstone:53:ca058:11, celestine:51:ca048:14, alexandrite:48:ca046:14, ruby:46:ca045:11, heliodor:44:ca041:14, garnet:41:ca039:14, diamond:40:ca013:14, tourmaline:38:ca010:14, sapphire:36:ca021:12, jade:33:ca019:12, opal:31:ca006:14, topaz:27:ca017:12 |
| zone124 | 7×46 | 122 | 9 | 2 | 31 | 224 | 3 | ур. 51, GachaCrusher | moonstone:49:ca058:12, amber:47:ca056:14, celestine:42:ca049:9, heliodor:40:ca041:14, sapphire:34:ca021:12, emerald:37:ca012:14, onyx:33:ca020:12, topaz:30:ca005:14, citrine:26:ca015:12 |
| zone125 | 7×47 | 111 | 10 | 2 | 34 | 228 | 3 | ур. 52, GachaCrusher | celestine:50:ca049:9, alexandrite:47:ca047:10, ruby:44:ca045:11, garnet:41:ca039:14, aquamarine:39:ca011:14, sapphire:37:ca021:12, jade:34:ca007:14, topaz:32:ca017:12, agate:29:ca004:14, amethyst:25:ca014:12 |
| zone126 | 7×64 | 170 | 13 | 3 | 44 | 320 | 4 | ур. 56, GachaCrusher | moonstone:54:ca058:13, amber:52:ca057:7, celestine:50:ca048:14, alexandrite:49:ca047:10, ruby:47:ca045:11, heliodor:45:ca041:14, diamond:41:ca013:14, emerald:39:ca012:14, aquamarine:37:ca011:14, tourmaline:34:ca022:12, onyx:33:ca020:12, agate:27:ca016:12, opal:30:ca018:12 |
| zone127 | 7×33 | 83 | 7 | 1 | 22 | 159 | 2 | ур. 46, GachaCrusher | amber:44:ca057:8, ruby:42:ca045:11, diamond:40:ca013:14, tourmaline:38:ca022:12, sapphire:35:ca021:12, onyx:31:ca008:14, opal:26:ca018:12 |
| zone128 | 7×49 | 130 | 11 | 2 | 28 | 242 | 3 | ур. 53, GachaCrusher | amber:51:ca057:8, celestine:48:ca049:10, alexandrite:45:ca047:10, heliodor:43:ca041:14, garnet:41:ca039:14, emerald:39:ca012:14, sapphire:38:ca021:12, jade:35:ca019:12, topaz:33:ca017:12, agate:30:ca004:14, citrine:27:ca015:12 |
| zone129 | 7×48 | 125 | 10 | 2 | 31 | 235 | 3 | ур. 54, GachaCrusher | moonstone:51:ca058:14, celestine:49:ca049:10, diamond:42:ca013:14, ruby:45:ca044:14, aquamarine:41:ca023:12, tourmaline:39:ca022:12, onyx:37:ca020:12, citrine:32:ca003:14, opal:35:ca006:12, amethyst:28:ca002:14 |
| zone130 | 7×64 | 156 | 15 | 3 | 39 | 320 | 4 | ур. 58, GachaCrusher | moonstone:55:ca058:14, amber:53:ca057:9, alexandrite:51:ca047:11, ruby:49:ca044:14, heliodor:47:ca041:14, garnet:45:ca039:14, diamond:42:ca013:14, emerald:43:ca024:12, aquamarine:41:ca023:12, sapphire:38:ca021:12, onyx:35:ca008:14, jade:36:ca019:12, topaz:34:ca005:14, agate:31:ca016:12, amethyst:27:ca014:12 |
| zone131 | 7×34 | 88 | 8 | 1 | 18 | 166 | 2 | ур. 48, GachaCrusher | amber:47:ca057:9, celestine:44:ca048:14, alexandrite:42:ca047:11, garnet:40:ca039:14, emerald:36:ca024:12, tourmaline:33:ca022:12, opal:30:ca006:14, citrine:26:ca015:12 |
| zone132 | 7×49 | 120 | 12 | 2 | 33 | 242 | 3 | ур. 55, GachaCrusher | moonstone:52:ca058:14, amber:50:ca057:9, alexandrite:47:ca047:11, ruby:44:ca044:14, diamond:43:ca013:14, aquamarine:42:ca023:12, sapphire:40:ca021:12, onyx:38:ca020:12, topaz:34:ca017:12, jade:36:ca007:14, agate:31:ca004:14, amethyst:28:ca014:12 |
| zone133 | 7×64 | 164 | 14 | 3 | 48 | 320 | 4 | ур. 59, GachaCrusher | amber:56:ca057:10, celestine:54:ca048:14, alexandrite:52:ca047:11, ruby:50:ca044:14, heliodor:48:ca041:14, diamond:46:ca025:12, emerald:43:ca024:12, tourmaline:41:ca022:12, sapphire:39:ca009:14, jade:36:ca019:12, opal:37:ca018:12, agate:35:ca016:12, citrine:32:ca003:14, amethyst:27:ca014:12 |
| zone134 | 7×47 | 122 | 9 | 2 | 32 | 228 | 3 | ур. 56, GachaCrusher | sunstone:53:ca060:11, celestine:50:ca049:11, heliodor:47:ca041:14, garnet:44:ca039:14, diamond:42:ca025:12, aquamarine:40:ca023:12, onyx:36:ca020:12, topaz:33:ca005:14, agate:29:ca016:12 |
| zone135 | 7×49 | 125 | 10 | 2 | 30 | 242 | 3 | ур. 57, GachaCrusher | sunstone:54:ca060:12, amber:51:ca057:10, alexandrite:49:ca047:11, ruby:45:ca044:14, diamond:43:ca025:12, emerald:41:ca024:12, tourmaline:38:ca010:14, sapphire:37:ca021:12, jade:34:ca007:14, topaz:28:ca017:12 |
| zone136 | 7×34 | 81 | 8 | 1 | 24 | 166 | 2 | ур. 50, GachaCrusher | sunstone:48:ca060:13, celestine:45:ca049:11, heliodor:43:ca041:14, garnet:40:ca040:12, aquamarine:36:ca023:12, sapphire:34:ca009:14, jade:32:ca019:12, citrine:29:ca015:12 |
| zone137 | 7×62 | 163 | 13 | 3 | 44 | 310 | 4 | ур. 60, GachaCrusher | sunstone:57:ca060:14, moonstone:55:ca058:14, alexandrite:53:ca047:11, ruby:51:ca044:14, garnet:49:ca040:12, diamond:46:ca013:14, emerald:44:ca024:12, tourmaline:42:ca010:14, onyx:40:ca020:12, opal:37:ca018:12, topaz:36:ca005:14, agate:32:ca016:12, amethyst:27:ca002:14 |
| zone138 | 7×48 | 120 | 9 | 2 | 35 | 235 | 3 | ур. 58, GachaCrusher | sunstone:55:ca060:14, moonstone:52:ca059:7, amber:46:ca057:11, garnet:44:ca040:12, aquamarine:42:ca023:12, sapphire:38:ca021:12, jade:36:ca007:14, topaz:33:ca017:12, citrine:28:ca003:14 |
| zone139 | 7×49 | 118 | 11 | 2 | 38 | 245 | 3 | ур. 58, GachaCrusher | moonstone:56:ca059:8, amber:54:ca057:11, alexandrite:51:ca046:14, ruby:49:ca044:14, garnet:45:ca040:12, emerald:43:ca024:12, aquamarine:41:ca023:12, onyx:38:ca008:14, opal:37:ca018:12, agate:34:ca004:14, amethyst:29:ca014:12 |
| zone140 | 7×60 | 161 | 15 | 3 | 34 | 296 | 4 | ур. 62, GachaCrusher | sunstone:60:ca060:14, moonstone:58:ca059:9, amber:56:ca057:11, celestine:53:ca048:14, alexandrite:51:ca046:14, heliodor:48:ca042:12, diamond:45:ca025:12, tourmaline:43:ca022:12, emerald:45:ca012:14, sapphire:41:ca009:14, onyx:40:ca020:12, jade:37:ca019:12, topaz:35:ca005:14, agate:33:ca016:12, citrine:30:ca015:12 |
| zone141 | 7×34 | 82 | 7 | 1 | 23 | 166 | 2 | ур. 52, GachaCrusher | moonstone:50:ca059:10, celestine:47:ca048:14, heliodor:44:ca042:12, diamond:42:ca025:12, tourmaline:38:ca010:14, opal:35:ca018:12, agate:31:ca004:14 |
| zone142 | 7×49 | 120 | 12 | 2 | 32 | 245 | 3 | ур. 60, GachaCrusher | sunstone:58:ca060:14, moonstone:56:ca059:10, amber:53:ca057:11, alexandrite:51:ca046:14, heliodor:47:ca042:12, garnet:46:ca039:14, emerald:44:ca024:12, aquamarine:42:ca011:14, sapphire:39:ca021:12, jade:37:ca007:14, topaz:34:ca017:12, citrine:29:ca003:14 |
| zone143 | 7×47 | 120 | 10 | 2 | 33 | 231 | 3 | ур. 59, GachaCrusher | moonstone:56:ca059:11, celestine:54:ca048:14, ruby:49:ca045:12, diamond:46:ca025:12, aquamarine:45:ca023:12, tourmaline:43:ca010:14, onyx:41:ca020:12, jade:38:ca019:12, agate:35:ca004:14, amethyst:30:ca014:12 |
| zone144 | 7×63 | 166 | 14 | 3 | 41 | 313 | 4 | ур. 64, GachaCrusher | sunstone:61:ca060:14, amber:57:ca057:11, moonstone:59:ca059:11, ruby:52:ca045:12, alexandrite:54:ca046:14, heliodor:50:ca042:12, emerald:45:ca024:12, diamond:47:ca013:14, sapphire:42:ca009:14, tourmaline:43:ca022:12, onyx:39:ca020:12, topaz:37:ca005:14, agate:34:ca016:12, citrine:31:ca015:12 |

</details>

## Темп экономики: сценарный бот, zone1–zone10 (ТЗ 17.2, C.4)

Бот играет в слоте Main без инструментов теми же командами, что UI (`ScenarioBot`): покупает, назначает свободных котиков на ближайшие цели, объединяет пары, улучшает самый выгодный генератор (наименьшая окупаемость), активирует шахты, открывает ворота, сундуки и доставки, улучшает чертежи для автоматизации шахт, затем доходные. Сессии — непрерывный цикл через все зоны: 30 мин игры, затем 4 ч офлайна; предел 48 ч симуляции на зону; «стена» — 2 ч симуляции без доступной покупки, улучшения или назначения, которое увеличивает доход или урон.

Журнал действий и симуляционного времени по зонам: `Docs/Reports/bot-actions-zones-1-10.md`.

Ориентиры владельца (C.4): zone1 — 2–6 мин живой игры; zone1–3 — до 20–30 мин; первые ворота zone5 — примерно за час; в zone1–10 нет «стены». Для сравнения, предварительная оценка ТЗ по упрощённому боту (без карт, бесплатных котиков, доставок и динамита): zone1 ≈ 77 с, zone3 ≈ 5 мин, первые ворота zone5 ≈ 43 мин, zone7–8 не проходились за 5,5 ч. Сравнение с ориентирами и предложение по профилю — `Docs/IMPLEMENTATION_NOTES.md`, раздел 5.

### Темп по зонам

Время — симуляционное, вместе с офлайном. Сессии идут непрерывным циклом через все зоны: 30 мин игры, затем 4 ч офлайна (ТЗ 17.2, 12.3); «Сессия в начале» — сколько текущей сессии уже сыграно в прошлых зонах. «Стена» — 2 ч симуляции без единой доступной покупки, улучшения или назначения, которое увеличивает доход или урон. Наймы, объединения, уровень котика и кузницы — к концу зоны. Журнал действий — `Docs/Reports/bot-actions-zones-1-10.md`.

| Зона | Итог | Из них онлайн | Сессия в начале | Наймов | Объединений | Макс. уровень котика | Кузница | Шахты авто/всего | Доставок | Доставок в час онлайн | Пропущено рекламных |
|---|---|---|---|---:|---:|---:|---:|---|---:|---:|---:|
| zone1 | пройдена за 1 мин 11 с | 1 мин 11 с | 0 с | 5 | 3 | 3 | 3 | 0/0 | 0 | 0.0 | 0 |
| zone2 | пройдена за 2 мин 18 с | 2 мин 18 с | 1 мин 11 с | 7 | 5 | 4 | 4 | 0/1 | 0 | 0.0 | 0 |
| zone3 | пройдена за 4 мин 29 с | 4 мин 29 с | 3 мин 29 с | 12 | 9 | 5 | 22 | 2/2 | 0 | 0.0 | 0 |
| zone4 | пройдена за 9 мин 27 с | 9 мин 27 с | 7 мин 58 с | 14 | 20 | 6 | 20 | 3/3 | 9 | 57.1 | 0 |
| zone5 | пройдена за 4 ч 20 мин | 20 мин 28 с | 17 мин 25 с | 19 | 29 | 6 | 30 | 3/4 | 21 | 61.6 | 4 |
| zone6 | пройдена за 4 ч 46 мин | 46 мин 41 с | 7 мин 53 с | 22 | 32 | 7 | 30 | 4/4 | 44 | 56.6 | 4 |
| zone7 | пройдена за 13 ч 05 мин | 1 ч 05 мин | 24 мин 34 с | 23 | 44 | 7 | 30 | 4/4 | 68 | 62.4 | 8 |
| zone8 | пройдена за 18 ч 00 мин | 2 ч 00 мин | 0 с | 29 | 65 | 8 | 41 | 4/4 | 122 | 60.7 | 17 |
| zone9 | пройдена за 31 ч 45 мин | 3 ч 45 мин | 40 с | 30 | 88 | 9 | 50 | 5/5 | 214 | 56.9 | 28 |
| zone10 | пройдена за 40 ч 13 мин | 4 ч 13 мин | 16 мин 18 с | 35 | 106 | 9 | 40 | 5/5 | 250 | 59.1 | 31 |

### Золото по источникам за зону

| Зона | Кузница | Шахты | Камни | Доставки | Всего | Потрачено на наём | Потрачено на улучшения |
|---|---:|---:|---:|---:|---:|---:|---:|
| zone1 | 219 (11%) | 0 | 1.77K (89%) | 0 | 1.99K | 682 | 175 |
| zone2 | 1.62K (5%) | 16K (50%) | 14.6K (45%) | 0 | 32.2K | 10.9K | 4.26K |
| zone3 | 37.5K (0%) | 1.45M (5%) | 25.8M (95%) | 0 | 27.2M | 11.1M | 7.01M |
| zone4 | 68.2K (0%) | 90M (13%) | 613M (86%) | 12.3M (2%) | 715M | 178M | 222M |
| zone5 | 7.2M (0%) | 5.61B (9%) | 48.8B (82%) | 4.75B (8%) | 59.2B | 18.4B | 2.56B |
| zone6 | 58.7M (0%) | 836B (36%) | 1.04T (45%) | 421B (18%) | 2.3T | 1.21T | 33.6B |
| zone7 | 166M (0%) | 249T (86%) | 18.3T (6%) | 23.6T (8%) | 291T | 46.9T | 3.65T |
| zone8 | 29.7B (0%) | 14.7aa (69%) | 2.87aa (14%) | 3.66aa (17%) | 21.3aa | 19.8aa | 75.6T |
| zone9 | 47.4B (0%) | 194aa (63%) | 45.1aa (15%) | 66.9aa (22%) | 306aa | 77aa | 14.5aa |
| zone10 | 201B (0%) | 6.7ab (60%) | 1.58ab (14%) | 2.9ab (26%) | 11.1ab | 7.88ab | 91.8aa |

### Эликсир по источникам за зону

Сундуки включают награды выхода, шахт, ворот, камней-сундуков и бесплатный сундук. Траты — улучшение чертежей.

| Зона | Камни | Пороги | Сундуки | Доставки | Всего за зону | Потрачено на чертежи | Улучшений чертежей | Баланс в конце |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| zone1 | 2 (11%) | 0 | 17 (89%) | 0 | 19 | 0 | 0 | 2 |
| zone2 | 7 (10%) | 0 | 63 (90%) | 0 | 70 | 50 | 1 | 9 |
| zone3 | 9 (6%) | 6 (4%) | 138 (90%) | 0 | 153 | 50 | 1 | 92 |
| zone4 | 2 (1%) | 19 (9%) | 190 (90%) | 0 | 211 | 200 | 3 | 93 |
| zone5 | 12 (2%) | 32 (5%) | 594 (92%) | 8 (1%) | 646 | 650 | 6 | 79 |
| zone6 | 64 (5%) | 40 (3%) | 824 (69%) | 270 (23%) | 1.19K | 1.1K | 7 | 167 |
| zone7 | 97 (8%) | 56 (5%) | 684 (57%) | 368 (31%) | 1.2K | 1.35K | 7 | 12 |
| zone8 | 131 (6%) | 55 (2%) | 1.48K (65%) | 599 (26%) | 2.26K | 2K | 6 | 267 |
| zone9 | 169 (5%) | 111 (4%) | 1.65K (53%) | 1.17K (38%) | 3.1K | 3K | 9 | 374 |
| zone10 | 235 (6%) | 110 (3%) | 2.17K (54%) | 1.48K (37%) | 4K | 3.65K | 9 | 726 |
| итого | | | | | 12.8K | | | |

### Чертежи (карточки) по источникам за зону, штук

| Зона | Камни-сундуки | Шахты | Ворота | Выход | Бесплатный сундук | Всего |
|---|---:|---:|---:|---:|---:|---:|
| zone1 | 0 | 0 | 0 | 1 | 0 | 1 |
| zone2 | 0 | 2 | 0 | 2 | 0 | 4 |
| zone3 | 1 | 6 | 0 | 4 | 0 | 11 |
| zone4 | 3 | 11 | 0 | 5 | 0 | 19 |
| zone5 | 20 | 20 | 7 | 7 | 2 | 56 |
| zone6 | 39 | 24 | 8 | 9 | 2 | 82 |
| zone7 | 28 | 32 | 0 | 11 | 8 | 79 |
| zone8 | 88 | 40 | 14 | 15 | 40 | 197 |
| zone9 | 109 | 60 | 17 | 17 | 85 | 288 |
| zone10 | 145 | 70 | 40 | 18 | 83 | 356 |
| итого | | | | | | 1.09K |

### Автоматизация шахт

Время — от начала зоны. «нет» — шахта не была автоматизирована до перехода.

| Зона | Шахта | Нужен чертёж | Активирована | Автоматизирована |
|---|---|---|---|---|
| zone2 | amethyst | ca002 ур. 1 | 45 с | нет |
| zone3 | citrine | ca003 ур. 1 | 4 мин 28 с | 4 мин 28 с |
| zone3 | amethyst | ca002 ур. 2 | 1 мин 00 с | 1 мин 00 с |
| zone4 | agate | ca004 ур. 2 | 7 мин 31 с | 7 мин 31 с |
| zone4 | citrine | ca003 ур. 2 | 5 мин 11 с | 5 мин 11 с |
| zone4 | amethyst | ca002 ур. 3 | 3 мин 17 с | 7 мин 31 с |
| zone5 | topaz | ca005 ур. 3 | 4 ч 20 мин | нет |
| zone5 | agate | ca004 ур. 3 | 4 ч 13 мин | 4 ч 14 мин |
| zone5 | citrine | ca003 ур. 3 | 5 мин 59 с | 8 мин 28 с |
| zone5 | amethyst | ca002 ур. 4 | 2 мин 13 с | 4 ч 12 мин |
| zone6 | topaz | ca005 ур. 4 | 4 ч 30 мин | 4 ч 30 мин |
| zone6 | agate | ca016 ур. 3 | 15 мин 38 с | 4 ч 42 мин |
| zone6 | citrine | ca003 ур. 4 | 8 мин 36 с | 9 мин 38 с |
| zone6 | amethyst | ca014 ур. 3 | 2 мин 12 с | 5 мин 46 с |
| zone7 | opal | ca006 ур. 4 | 9 ч 00 мин | 9 ч 00 мин |
| zone7 | topaz | ca005 ур. 5 | 8 ч 35 мин | 8 ч 35 мин |
| zone7 | agate | ca004 ур. 4 | 4 ч 06 мин | 4 ч 32 мин |
| zone7 | citrine | ca003 ур. 5 | 2 мин 20 с | 2 мин 20 с |
| zone8 | opal | ca006 ур. 5 | 13 ч 30 мин | 13 ч 30 мин |
| zone8 | topaz | ca005 ур. 5 | 9 ч 11 мин | 9 ч 11 мин |
| zone8 | agate | ca004 ур. 5 | 15 мин 47 с | 15 мин 47 с |
| zone8 | amethyst | ca014 ур. 4 | 1 мин 27 с | 4 ч 32 мин |
| zone9 | opal | ca006 ур. 6 | 26 ч 59 мин | 26 ч 59 мин |
| zone9 | topaz | ca017 ур. 3 | 26 ч 59 мин | 26 ч 59 мин |
| zone9 | agate | ca016 ур. 4 | 22 ч 29 мин | 22 ч 29 мин |
| zone9 | citrine | ca015 ур. 4 | 4 ч 41 мин | 4 ч 41 мин |
| zone9 | amethyst | ca002 ур. 5 | 2 мин 45 с | 2 мин 45 с |
| zone10 | jade | ca007 ур. 4 | 26 ч 52 мин | 26 ч 52 мин |
| zone10 | opal | ca006 ур. 6 | 26 ч 43 мин | 26 ч 43 мин |
| zone10 | topaz | ca005 ур. 6 | 8 ч 57 мин | 9 ч 12 мин |
| zone10 | citrine | ca015 ур. 5 | 4 ч 35 мин | 8 ч 45 мин |
| zone10 | amethyst | ca002 ур. 6 | 4 ч 13 мин | 4 ч 13 мин |

### Невыданные награды (ТЗ 4.3)

| Зона | Награда | Количество |
|---|---|---:|
| zone7 | BattlePass | 1 |
| zone8 | BattlePass | 1 |


Обучение, шаг 8 (ТЗ 13.4): в слоте Main без инструментов автоматизация в zone3 становится доступной (см. строки zone3 в таблице «Автоматизация шахт»).

## Таблицы темпа (Приложение C.4)

### Time-to-kill: один котик, время до разрушения цели

`ceil(HP / урон) × задержка удара`; урон `2.1^(L−1)`, задержка 1 с (E-02). Строки — уровень котика, столбцы — уровень цели. Несколько котиков на одной цели делят время пропорционально суммарному урону. Цели: самый частый камень zone1–10, все шахты этих зон, ворота и выход.

#### rockbasic (базовый камень)

| Котик \ цель | L1 | L2 | L3 | L4 | L5 | L6 | L7 | L8 | L9 | L10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| L1 | 16 с | 1 мин | 3.3 мин | 11.7 мин | 36.7 мин | 2.2 ч | 9.7 ч | 1.9 д | 6.1 д | 11.2 д |
| L2 | 8 с | 29 с | 1.6 мин | 5.6 мин | 17.5 мин | 1.1 ч | 4.6 ч | 21.2 ч | 2.9 д | 5.3 д |
| L3 | 4 с | 14 с | 46 с | 2.7 мин | 8.3 мин | 30.3 мин | 2.2 ч | 10.1 ч | 1.4 д | 2.5 д |
| L4 | 2 с | 7 с | 22 с | 1.3 мин | 4 мин | 14.4 мин | 1.1 ч | 4.8 ч | 15.9 ч | 1.2 д |
| L5 | 1 с | 4 с | 11 с | 36 с | 1.9 мин | 6.9 мин | 30 мин | 2.3 ч | 7.6 ч | 13.9 ч |
| L6 | 1 с | 2 с | 5 с | 18 с | 54 с | 3.3 мин | 14.3 мин | 1.1 ч | 3.6 ч | 6.6 ч |
| L7 | 1 с | 1 с | 3 с | 9 с | 26 с | 1.6 мин | 6.8 мин | 31.1 мин | 1.7 ч | 3.1 ч |
| L8 | 1 с | 1 с | 2 с | 4 с | 13 с | 45 с | 3.3 мин | 14.8 мин | 49.1 мин | 1.5 ч |

#### amethyst (шахта, с zone2)

| Котик \ цель | L1 | L2 | L3 | L4 | L5 | L6 | L7 | L8 | L9 | L10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| L1 | 1.1 мин | 2.7 мин | 10.8 мин | 43.3 мин | 2.9 ч | 9.1 ч | 1.6 д | 5.2 д | 13.9 д | 44.9 д |
| L2 | 31 с | 1.3 мин | 5.2 мин | 20.7 мин | 1.4 ч | 4.3 ч | 18.5 ч | 2.5 д | 6.6 д | 21.4 д |
| L3 | 15 с | 37 с | 2.5 мин | 9.8 мин | 39.3 мин | 2.1 ч | 8.8 ч | 1.2 д | 3.1 д | 10.2 д |
| L4 | 7 с | 18 с | 1.2 мин | 4.7 мин | 18.7 мин | 59 мин | 4.2 ч | 13.5 ч | 1.5 д | 4.8 д |
| L5 | 4 с | 9 с | 34 с | 2.2 мин | 8.9 мин | 28.1 мин | 2 ч | 6.4 ч | 17.1 ч | 2.3 д |
| L6 | 2 с | 4 с | 16 с | 1.1 мин | 4.3 мин | 13.4 мин | 57.1 мин | 3.1 ч | 8.2 ч | 1.1 д |
| L7 | 1 с | 2 с | 8 с | 31 с | 2 мин | 6.4 мин | 27.2 мин | 1.5 ч | 3.9 ч | 12.6 ч |
| L8 | 1 с | 1 с | 4 с | 15 с | 58 с | 3.1 мин | 13 мин | 41.7 мин | 1.9 ч | 6 ч |

#### citrine (шахта, с zone3)

| Котик \ цель | L1 | L2 | L3 | L4 | L5 | L6 | L7 | L8 | L9 | L10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| L1 | 1.2 мин | 3.8 мин | 12.5 мин | 46.7 мин | 3.2 ч | 10 ч | 1.8 д | 5.5 д | 16.2 д | 49.4 д |
| L2 | 34 с | 1.8 мин | 6 мин | 22.2 мин | 1.5 ч | 4.8 ч | 20.4 ч | 2.6 д | 7.7 д | 23.5 д |
| L3 | 16 с | 52 с | 2.9 мин | 10.6 мин | 43.3 мин | 2.3 ч | 9.7 ч | 1.2 д | 3.7 д | 11.2 д |
| L4 | 8 с | 25 с | 1.4 мин | 5.1 мин | 20.6 мин | 1.1 ч | 4.6 ч | 14.2 ч | 1.7 д | 5.3 д |
| L5 | 4 с | 12 с | 39 с | 2.4 мин | 9.8 мин | 30.9 мин | 2.2 ч | 6.8 ч | 20 ч | 2.5 д |
| L6 | 2 с | 6 с | 19 с | 1.2 мин | 4.7 мин | 14.7 мин | 1 ч | 3.2 ч | 9.5 ч | 1.2 д |
| L7 | 1 с | 3 с | 9 с | 33 с | 2.2 мин | 7 мин | 29.9 мин | 1.5 ч | 4.5 ч | 13.8 ч |
| L8 | 1 с | 2 с | 5 с | 16 с | 1.1 мин | 3.4 мин | 14.3 мин | 44 мин | 2.2 ч | 6.6 ч |

#### agate (шахта, с zone4)

| Котик \ цель | L1 | L2 | L3 | L4 | L5 | L6 | L7 | L8 | L9 | L10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| L1 | 1.3 мин | 6 мин | 14.2 мин | 50 мин | 3.5 ч | 10.9 ч | 1.9 д | 5.8 д | 18.5 д | 53.9 д |
| L2 | 37 с | 2.9 мин | 6.8 мин | 23.8 мин | 1.7 ч | 5.2 ч | 22.2 ч | 2.8 д | 8.8 д | 25.7 д |
| L3 | 18 с | 1.4 мин | 3.2 мин | 11.4 мин | 47.2 мин | 2.5 ч | 10.6 ч | 1.3 д | 4.2 д | 12.2 д |
| L4 | 9 с | 39 с | 1.5 мин | 5.4 мин | 22.5 мин | 1.2 ч | 5 ч | 15 ч | 2 д | 5.8 д |
| L5 | 4 с | 19 с | 44 с | 2.6 мин | 10.7 мин | 33.7 мин | 2.4 ч | 7.1 ч | 22.9 ч | 2.8 д |
| L6 | 2 с | 9 с | 21 с | 1.2 мин | 5.1 мин | 16.1 мин | 1.1 ч | 3.4 ч | 10.9 ч | 1.3 д |
| L7 | 1 с | 5 с | 10 с | 35 с | 2.4 мин | 7.7 мин | 32.7 мин | 1.6 ч | 5.2 ч | 15.1 ч |
| L8 | 1 с | 2 с | 5 с | 17 с | 1.2 мин | 3.7 мин | 15.6 мин | 46.3 мин | 2.5 ч | 7.2 ч |

#### topaz (шахта, с zone5)

| Котик \ цель | L1 | L2 | L3 | L4 | L5 | L6 | L7 | L8 | L9 | L10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| L1 | 1.4 мин | 6.5 мин | 23.4 мин | 53.3 мин | 3.8 ч | 11.8 ч | 2.1 д | 9.6 д | 20.8 д | 58.4 д |
| L2 | 40 с | 3.1 мин | 11.2 мин | 25.4 мин | 1.8 ч | 5.6 ч | 1 д | 4.6 д | 9.9 д | 27.8 д |
| L3 | 19 с | 1.5 мин | 5.3 мин | 12.1 мин | 51.1 мин | 2.7 ч | 11.5 ч | 2.2 д | 4.7 д | 13.2 д |
| L4 | 9 с | 43 с | 2.5 мин | 5.8 мин | 24.3 мин | 1.3 ч | 5.5 ч | 1 д | 2.2 д | 6.3 д |
| L5 | 5 с | 21 с | 1.2 мин | 2.8 мин | 11.6 мин | 36.6 мин | 2.6 ч | 11.9 ч | 1.1 д | 3 д |
| L6 | 3 с | 10 с | 35 с | 1.3 мин | 5.5 мин | 17.4 мин | 1.2 ч | 5.7 ч | 12.2 ч | 1.4 д |
| L7 | 1 с | 5 с | 17 с | 38 с | 2.6 мин | 8.3 мин | 35.4 мин | 2.7 ч | 5.8 ч | 16.3 ч |
| L8 | 1 с | 3 с | 8 с | 18 с | 1.3 мин | 4 мин | 16.9 мин | 1.3 ч | 2.8 ч | 7.8 ч |

#### opal (шахта, с zone7)

| Котик \ цель | L1 | L2 | L3 | L4 | L5 | L6 | L7 | L8 | L9 | L10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| L1 | 1.5 мин | 7 мин | 25.2 мин | 1.3 ч | 4 ч | 12.8 ч | 2.3 д | 10.4 д | 23.1 д | 62.9 д |
| L2 | 43 с | 3.3 мин | 12 мин | 37.8 мин | 1.9 ч | 6.1 ч | 1.1 д | 4.9 д | 11 д | 29.9 д |
| L3 | 21 с | 1.6 мин | 5.7 мин | 18 мин | 55 мин | 2.9 ч | 12.3 ч | 2.4 д | 5.2 д | 14.3 д |
| L4 | 10 с | 46 с | 2.7 мин | 8.6 мин | 26.2 мин | 1.4 ч | 5.9 ч | 1.1 д | 2.5 д | 6.8 д |
| L5 | 5 с | 22 с | 1.3 мин | 4.1 мин | 12.5 мин | 39.4 мин | 2.8 ч | 12.8 ч | 1.2 д | 3.2 д |
| L6 | 3 с | 11 с | 38 с | 2 мин | 6 мин | 18.8 мин | 1.3 ч | 6.1 ч | 13.6 ч | 1.5 д |
| L7 | 2 с | 5 с | 18 с | 56 с | 2.8 мин | 8.9 мин | 38.1 мин | 2.9 ч | 6.5 ч | 17.6 ч |
| L8 | 1 с | 3 с | 9 с | 27 с | 1.4 мин | 4.3 мин | 18.2 мин | 1.4 ч | 3.1 ч | 8.4 ч |

#### jade (шахта, с zone10)

| Котик \ цель | L1 | L2 | L3 | L4 | L5 | L6 | L7 | L8 | L9 | L10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| L1 | 1.6 мин | 7.5 мин | 27 мин | 1.4 ч | 4.3 ч | 13.7 ч | 2.4 д | 11.1 д | 36.8 д | 67.4 д |
| L2 | 46 с | 3.6 мин | 12.9 мин | 40.5 мин | 2.1 ч | 6.5 ч | 1.2 д | 5.3 д | 17.5 д | 32.1 д |
| L3 | 22 с | 1.7 мин | 6.1 мин | 19.3 мин | 59 мин | 3.1 ч | 13.2 ч | 2.5 д | 8.3 д | 15.3 д |
| L4 | 11 с | 49 с | 2.9 мин | 9.2 мин | 28.1 мин | 1.5 ч | 6.3 ч | 1.2 д | 4 д | 7.3 д |
| L5 | 5 с | 24 с | 1.4 мин | 4.4 мин | 13.4 мин | 42.2 мин | 3 ч | 13.7 ч | 1.9 д | 3.5 д |
| L6 | 3 с | 12 с | 40 с | 2.1 мин | 6.4 мин | 20.1 мин | 1.4 ч | 6.5 ч | 21.6 ч | 1.6 д |
| L7 | 2 с | 6 с | 19 с | 1 мин | 3 мин | 9.6 мин | 40.8 мин | 3.1 ч | 10.3 ч | 18.8 ч |
| L8 | 1 с | 3 с | 9 с | 29 с | 1.5 мин | 4.6 мин | 19.4 мин | 1.5 ч | 4.9 ч | 9 ч |

#### checkpoint (ворота)

| Котик \ цель | L1 | L2 | L3 | L4 | L5 | L6 | L7 | L8 | L9 | L10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| L1 | 2.5 мин | 5.8 мин | 13.3 мин | 33.3 мин | 5.8 ч | 13.9 ч | 1.7 д | 4.1 д | 11.6 д | 40.5 д |
| L2 | 1.2 мин | 2.8 мин | 6.4 мин | 15.9 мин | 2.8 ч | 6.6 ч | 19.8 ч | 1.9 д | 5.5 д | 19.3 д |
| L3 | 35 с | 1.3 мин | 3 мин | 7.6 мин | 1.3 ч | 3.1 ч | 9.4 ч | 22 ч | 2.6 д | 9.2 д |
| L4 | 17 с | 38 с | 1.5 мин | 3.6 мин | 37.4 мин | 1.5 ч | 4.5 ч | 10.5 ч | 1.2 д | 4.4 д |
| L5 | 8 с | 18 с | 42 с | 1.7 мин | 17.8 мин | 42.9 мин | 2.1 ч | 5 ч | 14.3 ч | 2.1 д |
| L6 | 4 с | 9 с | 20 с | 49 с | 8.5 мин | 20.4 мин | 1 ч | 2.4 ч | 6.8 ч | 23.8 ч |
| L7 | 2 с | 5 с | 10 с | 24 с | 4.1 мин | 9.7 мин | 29.2 мин | 1.1 ч | 3.2 ч | 11.3 ч |
| L8 | 1 с | 2 с | 5 с | 12 с | 1.9 мин | 4.6 мин | 13.9 мин | 32.4 мин | 1.5 ч | 5.4 ч |

#### exit (выход)

| Котик \ цель | L1 | L2 | L3 | L4 | L5 | L6 | L7 | L8 | L9 | L10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| L1 | 1.7 мин | 3.3 мин | 10 мин | 23.3 мин | 1.4 ч | 6.9 ч | 1.7 д | 6.9 д | 17.4 д | 46.3 д |
| L2 | 48 с | 1.6 мин | 4.8 мин | 11.1 мин | 39.7 мин | 3.3 ч | 19.8 ч | 3.3 д | 8.3 д | 22 д |
| L3 | 23 с | 46 с | 2.3 мин | 5.3 мин | 18.9 мин | 1.6 ч | 9.4 ч | 1.6 д | 3.9 д | 10.5 д |
| L4 | 11 с | 22 с | 1.1 мин | 2.5 мин | 9 мин | 45 мин | 4.5 ч | 18 ч | 1.9 д | 5 д |
| L5 | 6 с | 11 с | 31 с | 1.2 мин | 4.3 мин | 21.4 мин | 2.1 ч | 8.6 ч | 21.4 ч | 2.4 д |
| L6 | 3 с | 5 с | 15 с | 35 с | 2.1 мин | 10.2 мин | 1 ч | 4.1 ч | 10.2 ч | 1.1 д |
| L7 | 2 с | 3 с | 7 с | 17 с | 59 с | 4.9 мин | 29.2 мин | 1.9 ч | 4.9 ч | 13 ч |
| L8 | 1 с | 2 с | 4 с | 8 с | 28 с | 2.3 мин | 13.9 мин | 55.5 мин | 2.3 ч | 6.2 ч |

### Генераторы: цена, прирост и окупаемость, уровни 1–40

Без ворот и карт (множители ×1). «Цена» — переход L → L+1 (E-03). «Доход/с» — выход за цикл с учётом достигнутых порогов (E-04), делённый на длительность цикла. «Окупаемость» — цена / прирост дохода в секунду после покупки. ★ — на уровне L+1 достигается порог.

#### spawningcart (кузница)

| L | Цена L→L+1 | Выход за цикл | Цикл | Доход/с | Прирост дохода/с | Окупаемость |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 72 | 3 | 2 с | 1.5 | 1.5 | 48 с |
| 2 | 103 | 6 | 2 с | 3 | 1.5 | 1.1 мин |
| 3 | 148 | 9 | 2 с | 4.5 | 1.5 | 1.6 мин |
| 4 | 211 | 12 | 2 с | 6 | 1.5 | 2.3 мин |
| 5 | 302 | 15 | 2 с | 7.5 | 1.5 | 3.4 мин |
| 6 | 431 | 18 | 2 с | 9 | 1.5 | 4.8 мин |
| 7 | 616 | 21 | 2 с | 10.5 | 1.5 | 6.8 мин |
| 8 | 881 | 24 | 2 с | 12 | 1.5 | 9.8 мин |
| 9 ★ | 1.25K | 27 | 2 с | 13.5 | 16.5 | 1.3 мин |
| 10 | 1.8K | 60 | 2 с | 30 | 3 | 10 мин |
| 11 | 2.57K | 66 | 2 с | 33 | 3 | 14.3 мин |
| 12 | 3.68K | 72 | 2 с | 36 | 3 | 20.5 мин |
| 13 | 5.26K | 78 | 2 с | 39 | 3 | 29.3 мин |
| 14 | 7.52K | 84 | 2 с | 42 | 3 | 41.8 мин |
| 15 | 10.7K | 90 | 2 с | 45 | 3 | 59.8 мин |
| 16 | 15.3K | 96 | 2 с | 48 | 3 | 1.4 ч |
| 17 | 22K | 102 | 2 с | 51 | 3 | 2 ч |
| 18 | 31.4K | 108 | 2 с | 54 | 3 | 2.9 ч |
| 19 ★ | 45K | 114 | 2 с | 57 | 63 | 11.9 мин |
| 20 | 64.3K | 240 | 2 с | 120 | 6 | 3 ч |
| 21 | 92K | 252 | 2 с | 126 | 6 | 4.3 ч |
| 22 | 131K | 264 | 2 с | 132 | 6 | 6.1 ч |
| 23 | 188K | 276 | 2 с | 138 | 6 | 8.7 ч |
| 24 | 269K | 288 | 2 с | 144 | 6 | 12.5 ч |
| 25 | 384K | 300 | 2 с | 150 | 6 | 17.8 ч |
| 26 | 550K | 312 | 2 с | 156 | 6 | 1.1 д |
| 27 | 787K | 324 | 2 с | 162 | 6 | 1.5 д |
| 28 | 1.12M | 336 | 2 с | 168 | 6 | 2.2 д |
| 29 ★ | 1.6M | 348 | 2 с | 174 | 186 | 2.4 ч |
| 30 | 2.3M | 720 | 2 с | 360 | 12 | 2.2 д |
| 31 | 3.29M | 744 | 2 с | 372 | 12 | 3.2 д |
| 32 | 4.7M | 768 | 2 с | 384 | 12 | 4.5 д |
| 33 | 6.73M | 792 | 2 с | 396 | 12 | 6.5 д |
| 34 | 9.62M | 816 | 2 с | 408 | 12 | 9.3 д |
| 35 | 13.7M | 840 | 2 с | 420 | 12 | 13.3 д |
| 36 | 19.6M | 864 | 2 с | 432 | 12 | 19 д |
| 37 | 28.1M | 888 | 2 с | 444 | 12 | 27.1 д |
| 38 | 40.2M | 912 | 2 с | 456 | 12 | 38.8 д |
| 39 ★ | 57.5M | 936 | 2 с | 468 | 4.33K | 3.7 ч |
| 40 | 82.3M | 9.6K | 2 с | 4.8K | 120 | 7.9 д |

#### amethyst (с zone2)

| L | Цена L→L+1 | Выход за цикл | Цикл | Доход/с | Прирост дохода/с | Окупаемость |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 512 | 400 | 8 с | 50 | 50 | 10.2 с |
| 2 | 748 | 800 | 8 с | 100 | 50 | 15 с |
| 3 | 1.09K | 1.2K | 8 с | 150 | 50 | 21.8 с |
| 4 | 1.59K | 1.6K | 8 с | 200 | 50 | 31.9 с |
| 5 | 2.32K | 2K | 8 с | 250 | 50 | 46.5 с |
| 6 | 3.39K | 2.4K | 8 с | 300 | 50 | 1.1 мин |
| 7 | 4.95K | 2.8K | 8 с | 350 | 50 | 1.7 мин |
| 8 | 7.24K | 3.2K | 8 с | 400 | 50 | 2.4 мин |
| 9 ★ | 10.5K | 3.6K | 8 с | 450 | 550 | 19.2 с |
| 10 | 15.4K | 8K | 8 с | 1K | 100 | 2.6 мин |
| 11 | 22.5K | 8.8K | 8 с | 1.1K | 100 | 3.8 мин |
| 12 | 32.8K | 9.6K | 8 с | 1.2K | 100 | 5.5 мин |
| 13 | 48K | 10.4K | 8 с | 1.3K | 100 | 8 мин |
| 14 | 70.1K | 11.2K | 8 с | 1.4K | 100 | 11.7 мин |
| 15 | 102K | 12K | 8 с | 1.5K | 100 | 17.1 мин |
| 16 | 149K | 12.8K | 8 с | 1.6K | 100 | 24.9 мин |
| 17 | 218K | 13.6K | 8 с | 1.7K | 100 | 36.4 мин |
| 18 | 318K | 14.4K | 8 с | 1.8K | 100 | 53.1 мин |
| 19 ★ | 465K | 15.2K | 8 с | 1.9K | 2.1K | 3.7 мин |
| 20 | 679K | 32K | 8 с | 4K | 200 | 56.6 мин |
| 21 | 991K | 33.6K | 8 с | 4.2K | 200 | 1.4 ч |
| 22 | 1.44M | 35.2K | 8 с | 4.4K | 200 | 2 ч |
| 23 | 2.11M | 36.8K | 8 с | 4.6K | 200 | 2.9 ч |
| 24 | 3.08M | 38.4K | 8 с | 4.8K | 200 | 4.3 ч |
| 25 | 4.5M | 40K | 8 с | 5K | 200 | 6.3 ч |
| 26 | 6.57M | 41.6K | 8 с | 5.2K | 200 | 9.1 ч |
| 27 | 9.6M | 43.2K | 8 с | 5.4K | 200 | 13.3 ч |
| 28 | 14M | 44.8K | 8 с | 5.6K | 200 | 19.5 ч |
| 29 | 20.4M | 46.4K | 8 с | 5.8K | 200 | 1.2 д |
| 30 | 29.8M | 48K | 8 с | 6K | 200 | 1.7 д |
| 31 | 43.6M | 49.6K | 8 с | 6.2K | 200 | 2.5 д |
| 32 | 63.7M | 51.2K | 8 с | 6.4K | 200 | 3.7 д |
| 33 | 93M | 52.8K | 8 с | 6.6K | 200 | 5.4 д |
| 34 | 135M | 54.4K | 8 с | 6.8K | 200 | 7.9 д |
| 35 | 198M | 56K | 8 с | 7K | 200 | 11.5 д |
| 36 | 289M | 57.6K | 8 с | 7.2K | 200 | 16.8 д |
| 37 | 422M | 59.2K | 8 с | 7.4K | 200 | 24.5 д |
| 38 | 617M | 60.8K | 8 с | 7.6K | 200 | 35.7 д |
| 39 ★ | 900M | 62.4K | 8 с | 7.8K | 8.2K | 1.3 д |
| 40 | 1.31B | 128K | 8 с | 16K | 400 | 38.1 д |

#### citrine (с zone3)

| L | Цена L→L+1 | Выход за цикл | Цикл | Доход/с | Прирост дохода/с | Окупаемость |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 4.09K | 6K | 15 с | 400 | 400 | 10.2 с |
| 2 | 6.1K | 12K | 15 с | 800 | 400 | 15.3 с |
| 3 | 9.09K | 18K | 15 с | 1.2K | 400 | 22.7 с |
| 4 | 13.5K | 24K | 15 с | 1.6K | 400 | 33.9 с |
| 5 | 20.1K | 30K | 15 с | 2K | 400 | 50.5 с |
| 6 | 30K | 36K | 15 с | 2.4K | 400 | 1.3 мин |
| 7 | 44.8K | 42K | 15 с | 2.8K | 400 | 1.9 мин |
| 8 | 66.7K | 48K | 15 с | 3.2K | 400 | 2.8 мин |
| 9 ★ | 99.5K | 54K | 15 с | 3.6K | 8.4K | 11.8 с |
| 10 | 148K | 180K | 15 с | 12K | 1.2K | 2.1 мин |
| 11 | 220K | 198K | 15 с | 13.2K | 1.2K | 3.1 мин |
| 12 | 329K | 216K | 15 с | 14.4K | 1.2K | 4.6 мин |
| 13 | 490K | 234K | 15 с | 15.6K | 1.2K | 6.8 мин |
| 14 | 730K | 252K | 15 с | 16.8K | 1.2K | 10.1 мин |
| 15 | 1.08M | 270K | 15 с | 18K | 1.2K | 15.1 мин |
| 16 | 1.62M | 288K | 15 с | 19.2K | 1.2K | 22.5 мин |
| 17 | 2.41M | 306K | 15 с | 20.4K | 1.2K | 33.6 мин |
| 18 | 3.6M | 324K | 15 с | 21.6K | 1.2K | 50 мин |
| 19 ★ | 5.36M | 342K | 15 с | 22.8K | 49.2K | 1.8 мин |
| 20 | 7.99M | 1.08M | 15 с | 72K | 3.6K | 37 мин |
| 21 | 11.9M | 1.13M | 15 с | 75.6K | 3.6K | 55.2 мин |
| 22 | 17.7M | 1.18M | 15 с | 79.2K | 3.6K | 1.4 ч |
| 23 | 26.4M | 1.24M | 15 с | 82.8K | 3.6K | 2 ч |
| 24 | 39.4M | 1.29M | 15 с | 86.4K | 3.6K | 3 ч |
| 25 | 58.7M | 1.35M | 15 с | 90K | 3.6K | 4.5 ч |
| 26 | 87.5M | 1.4M | 15 с | 93.6K | 3.6K | 6.8 ч |
| 27 | 130M | 1.45M | 15 с | 97.2K | 3.6K | 10.1 ч |
| 28 | 194M | 1.51M | 15 с | 100K | 3.6K | 15 ч |
| 29 | 289M | 1.56M | 15 с | 104K | 3.6K | 22.3 ч |
| 30 | 431M | 1.62M | 15 с | 108K | 3.6K | 1.4 д |
| 31 | 642M | 1.67M | 15 с | 111K | 3.6K | 2.1 д |
| 32 | 957M | 1.72M | 15 с | 115K | 3.6K | 3.1 д |
| 33 | 1.42B | 1.78M | 15 с | 118K | 3.6K | 4.6 д |
| 34 | 2.12B | 1.83M | 15 с | 122K | 3.6K | 6.8 д |
| 35 | 3.16B | 1.89M | 15 с | 126K | 3.6K | 10.2 д |
| 36 | 4.71B | 1.94M | 15 с | 129K | 3.6K | 15.2 д |
| 37 | 7.03B | 1.99M | 15 с | 133K | 3.6K | 22.6 д |
| 38 | 10.4B | 2.05M | 15 с | 136K | 3.6K | 33.7 д |
| 39 ★ | 15.6B | 2.1M | 15 с | 140K | 291K | 14.9 ч |
| 40 | 23.2B | 6.48M | 15 с | 432K | 10.8K | 24.9 д |

#### agate (с zone4)

| L | Цена L→L+1 | Выход за цикл | Цикл | Доход/с | Прирост дохода/с | Окупаемость |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 32.7K | 125K | 30 с | 4.16K | 4.16K | 7.9 с |
| 2 | 49.8K | 250K | 30 с | 8.33K | 4.16K | 12 с |
| 3 | 75.7K | 375K | 30 с | 12.5K | 4.16K | 18.2 с |
| 4 | 115K | 500K | 30 с | 16.6K | 4.16K | 27.6 с |
| 5 | 174K | 625K | 30 с | 20.8K | 4.16K | 42 с |
| 6 | 265K | 750K | 30 с | 25K | 4.16K | 1.1 мин |
| 7 | 404K | 875K | 30 с | 29.1K | 4.16K | 1.6 мин |
| 8 | 614K | 1M | 30 с | 33.3K | 4.16K | 2.5 мин |
| 9 ★ | 933K | 1.12M | 30 с | 37.5K | 45.8K | 20.4 с |
| 10 | 1.41M | 2.5M | 30 с | 83.3K | 8.33K | 2.8 мин |
| 11 | 2.15M | 2.75M | 30 с | 91.6K | 8.33K | 4.3 мин |
| 12 | 3.27M | 3M | 30 с | 100K | 8.33K | 6.6 мин |
| 13 | 4.98M | 3.25M | 30 с | 108K | 8.33K | 10 мин |
| 14 | 7.57M | 3.5M | 30 с | 116K | 8.33K | 15.2 мин |
| 15 | 11.5M | 3.75M | 30 с | 125K | 8.33K | 23 мин |
| 16 | 17.5M | 4M | 30 с | 133K | 8.33K | 35 мин |
| 17 | 26.6M | 4.25M | 30 с | 141K | 8.33K | 53.2 мин |
| 18 | 40.4M | 4.5M | 30 с | 150K | 8.33K | 1.3 ч |
| 19 ★ | 61.4M | 4.75M | 30 с | 158K | 341K | 3 мин |
| 20 | 93.4M | 15M | 30 с | 500K | 25K | 1 ч |
| 21 | 142M | 15.7M | 30 с | 525K | 25K | 1.6 ч |
| 22 | 215M | 16.5M | 30 с | 550K | 25K | 2.4 ч |
| 23 | 328M | 17.2M | 30 с | 575K | 25K | 3.6 ч |
| 24 | 498M | 18M | 30 с | 600K | 25K | 5.5 ч |
| 25 | 758M | 18.7M | 30 с | 625K | 25K | 8.4 ч |
| 26 | 1.15B | 19.5M | 30 с | 650K | 25K | 12.8 ч |
| 27 | 1.75B | 20.2M | 30 с | 675K | 25K | 19.5 ч |
| 28 | 2.66B | 21M | 30 с | 700K | 25K | 1.2 д |
| 29 ★ | 4.04B | 21.7M | 30 с | 725K | 2.27M | 29.6 мин |
| 30 | 6.15B | 90M | 30 с | 3M | 100K | 17.1 ч |
| 31 | 9.34B | 93M | 30 с | 3.1M | 100K | 1.1 д |
| 32 | 14.2B | 96M | 30 с | 3.2M | 100K | 1.6 д |
| 33 | 21.5B | 99M | 30 с | 3.3M | 100K | 2.5 д |
| 34 | 32.8B | 102M | 30 с | 3.4M | 100K | 3.8 д |
| 35 | 49.9B | 105M | 30 с | 3.5M | 100K | 5.8 д |
| 36 | 75.8B | 108M | 30 с | 3.6M | 100K | 8.8 д |
| 37 | 115B | 111M | 30 с | 3.7M | 100K | 13.3 д |
| 38 | 175B | 114M | 30 с | 3.8M | 100K | 20.3 д |
| 39 ★ | 266B | 117M | 30 с | 3.9M | 16.1M | 4.6 ч |
| 40 | 404B | 600M | 30 с | 20M | 500K | 9.4 д |

#### topaz (с zone5)

| L | Цена L→L+1 | Выход за цикл | Цикл | Доход/с | Прирост дохода/с | Окупаемость |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 262K | 1.7M | 1 мин | 28.3K | 28.3K | 9.3 с |
| 2 | 406K | 3.4M | 1 мин | 56.6K | 28.3K | 14.3 с |
| 3 | 629K | 5.1M | 1 мин | 85K | 28.3K | 22.2 с |
| 4 | 976K | 6.8M | 1 мин | 113K | 28.3K | 34.5 с |
| 5 | 1.51M | 8.5M | 1 мин | 141K | 28.3K | 53.4 с |
| 6 | 2.34M | 10.2M | 1 мин | 170K | 28.3K | 1.4 мин |
| 7 | 3.63M | 11.9M | 1 мин | 198K | 28.3K | 2.1 мин |
| 8 | 5.63M | 13.6M | 1 мин | 226K | 28.3K | 3.3 мин |
| 9 ★ | 8.73M | 15.3M | 1 мин | 255K | 595K | 14.7 с |
| 10 | 13.5M | 51M | 1 мин | 850K | 85K | 2.7 мин |
| 11 | 20.9M | 56.1M | 1 мин | 935K | 85K | 4.1 мин |
| 12 | 32.5M | 61.2M | 1 мин | 1.02M | 85K | 6.4 мин |
| 13 | 50.4M | 66.3M | 1 мин | 1.1M | 85K | 9.9 мин |
| 14 | 78.1M | 71.4M | 1 мин | 1.19M | 85K | 15.3 мин |
| 15 | 121M | 76.5M | 1 мин | 1.27M | 85K | 23.7 мин |
| 16 | 187M | 81.6M | 1 мин | 1.36M | 85K | 36.8 мин |
| 17 | 290M | 86.7M | 1 мин | 1.44M | 85K | 57.1 мин |
| 18 | 450M | 91.8M | 1 мин | 1.53M | 85K | 1.5 ч |
| 19 ★ | 699M | 96.9M | 1 мин | 1.61M | 3.48M | 3.3 мин |
| 20 | 1.08B | 306M | 1 мин | 5.1M | 255K | 1.2 ч |
| 21 | 1.67B | 321M | 1 мин | 5.35M | 255K | 1.8 ч |
| 22 | 2.6B | 336M | 1 мин | 5.61M | 255K | 2.8 ч |
| 23 | 4.03B | 351M | 1 мин | 5.86M | 255K | 4.4 ч |
| 24 | 6.25B | 367M | 1 мин | 6.12M | 255K | 6.8 ч |
| 25 | 9.69B | 382M | 1 мин | 6.37M | 255K | 10.6 ч |
| 26 | 15B | 397M | 1 мин | 6.63M | 255K | 16.4 ч |
| 27 | 23.2B | 413M | 1 мин | 6.88M | 255K | 1.1 д |
| 28 | 36B | 428M | 1 мин | 7.14M | 255K | 1.6 д |
| 29 | 55.9B | 443M | 1 мин | 7.39M | 255K | 2.5 д |
| 30 | 86.7B | 459M | 1 мин | 7.65M | 255K | 3.9 д |
| 31 | 134B | 474M | 1 мин | 7.9M | 255K | 6.1 д |
| 32 | 208B | 489M | 1 мин | 8.16M | 255K | 9.5 д |
| 33 | 322B | 504M | 1 мин | 8.41M | 255K | 14.7 д |
| 34 | 500B | 520M | 1 мин | 8.67M | 255K | 22.7 д |
| 35 | 775B | 535M | 1 мин | 8.92M | 255K | 35.2 д |
| 36 | 1.2T | 550M | 1 мин | 9.18M | 255K | 54.6 д |
| 37 | 1.86T | 566M | 1 мин | 9.43M | 255K | 84.6 д |
| 38 | 2.88T | 581M | 1 мин | 9.69M | 255K | 131.1 д |
| 39 ★ | 4.47T | 596M | 1 мин | 9.94M | 20.6M | 2.5 д |
| 40 | 6.94T | 1.83B | 1 мин | 30.6M | 765K | 105 д |

#### opal (с zone7)

| L | Цена L→L+1 | Выход за цикл | Цикл | Доход/с | Прирост дохода/с | Окупаемость |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 1.04M | 48M | 2 мин | 400K | 400K | 2.6 с |
| 2 | 1.65M | 96M | 2 мин | 800K | 400K | 4.1 с |
| 3 | 2.61M | 144M | 2 мин | 1.2M | 400K | 6.5 с |
| 4 | 4.13M | 192M | 2 мин | 1.6M | 400K | 10.3 с |
| 5 | 6.53M | 240M | 2 мин | 2M | 400K | 16.3 с |
| 6 | 10.3M | 288M | 2 мин | 2.4M | 400K | 25.8 с |
| 7 | 16.3M | 336M | 2 мин | 2.8M | 400K | 40.8 с |
| 8 | 25.7M | 384M | 2 мин | 3.2M | 400K | 1.1 мин |
| 9 ★ | 40.7M | 432M | 2 мин | 3.6M | 12.4M | 3.3 с |
| 10 | 64.3M | 1.92B | 2 мин | 16M | 1.6M | 40.2 с |
| 11 | 101M | 2.11B | 2 мин | 17.6M | 1.6M | 1.1 мин |
| 12 | 160M | 2.3B | 2 мин | 19.2M | 1.6M | 1.7 мин |
| 13 | 253M | 2.49B | 2 мин | 20.8M | 1.6M | 2.6 мин |
| 14 | 400M | 2.68B | 2 мин | 22.4M | 1.6M | 4.2 мин |
| 15 | 633M | 2.88B | 2 мин | 24M | 1.6M | 6.6 мин |
| 16 | 1B | 3.07B | 2 мин | 25.6M | 1.6M | 10.4 мин |
| 17 | 1.58B | 3.26B | 2 мин | 27.2M | 1.6M | 16.5 мин |
| 18 | 2.49B | 3.45B | 2 мин | 28.8M | 1.6M | 26 мин |
| 19 ★ | 3.94B | 3.64B | 2 мин | 30.4M | 97.6M | 40.5 с |
| 20 | 6.23B | 15.3B | 2 мин | 128M | 6.4M | 16.2 мин |
| 21 | 9.85B | 16.1B | 2 мин | 134M | 6.4M | 25.7 мин |
| 22 | 15.5B | 16.8B | 2 мин | 140M | 6.4M | 40.6 мин |
| 23 | 24.6B | 17.6B | 2 мин | 147M | 6.4M | 1.1 ч |
| 24 | 38.8B | 18.4B | 2 мин | 153M | 6.4M | 1.7 ч |
| 25 | 61.4B | 19.2B | 2 мин | 160M | 6.4M | 2.7 ч |
| 26 | 97B | 19.9B | 2 мин | 166M | 6.4M | 4.2 ч |
| 27 | 153B | 20.7B | 2 мин | 172M | 6.4M | 6.7 ч |
| 28 | 242B | 21.5B | 2 мин | 179M | 6.4M | 10.5 ч |
| 29 | 382B | 22.2B | 2 мин | 185M | 6.4M | 16.6 ч |
| 30 | 604B | 23B | 2 мин | 192M | 6.4M | 1.1 д |
| 31 | 955B | 23.8B | 2 мин | 198M | 6.4M | 1.7 д |
| 32 | 1.5T | 24.5B | 2 мин | 204M | 6.4M | 2.7 д |
| 33 | 2.38T | 25.3B | 2 мин | 211M | 6.4M | 4.3 д |
| 34 | 3.76T | 26.1B | 2 мин | 217M | 6.4M | 6.8 д |
| 35 | 5.95T | 26.8B | 2 мин | 224M | 6.4M | 10.8 д |
| 36 | 9.41T | 27.6B | 2 мин | 230M | 6.4M | 17 д |
| 37 | 14.8T | 28.4B | 2 мин | 236M | 6.4M | 26.9 д |
| 38 | 23.4T | 29.1B | 2 мин | 243M | 6.4M | 42.5 д |
| 39 ★ | 37.1T | 29.9B | 2 мин | 249M | 774M | 13.3 ч |
| 40 | 58.6T | 122B | 2 мин | 1.02B | 25.6M | 26.5 д |

#### jade (с zone10)

| L | Цена L→L+1 | Выход за цикл | Цикл | Доход/с | Прирост дохода/с | Окупаемость |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 9.76M | 760M | 4 мин | 3.16M | 3.16M | 3.1 с |
| 2 | 15.7M | 1.52B | 4 мин | 6.33M | 3.16M | 5 с |
| 3 | 25.3M | 2.28B | 4 мин | 9.5M | 3.16M | 8 с |
| 4 | 40.7M | 3.04B | 4 мин | 12.6M | 3.16M | 12.9 с |
| 5 | 65.6M | 3.8B | 4 мин | 15.8M | 3.16M | 20.7 с |
| 6 | 105M | 4.56B | 4 мин | 19M | 3.16M | 33.4 с |
| 7 | 170M | 5.32B | 4 мин | 22.1M | 3.16M | 53.7 с |
| 8 | 273M | 6.08B | 4 мин | 25.3M | 3.16M | 1.4 мин |
| 9 ★ | 440M | 6.84B | 4 мин | 28.5M | 34.8M | 12.7 с |
| 10 | 709M | 15.2B | 4 мин | 63.3M | 6.33M | 1.9 мин |
| 11 | 1.14B | 16.7B | 4 мин | 69.6M | 6.33M | 3 мин |
| 12 | 1.83B | 18.2B | 4 мин | 76M | 6.33M | 4.8 мин |
| 13 | 2.96B | 19.7B | 4 мин | 82.3M | 6.33M | 7.8 мин |
| 14 | 4.76B | 21.2B | 4 мин | 88.6M | 6.33M | 12.6 мин |
| 15 | 7.67B | 22.8B | 4 мин | 95M | 6.33M | 20.2 мин |
| 16 | 12.3B | 24.3B | 4 мин | 101M | 6.33M | 32.5 мин |
| 17 | 19.9B | 25.8B | 4 мин | 107M | 6.33M | 52.4 мин |
| 18 | 32B | 27.3B | 4 мин | 114M | 6.33M | 1.4 ч |
| 19 ★ | 51.5B | 28.8B | 4 мин | 120M | 1.14B | 45 с |
| 20 | 83B | 304B | 4 мин | 1.26B | 63.3M | 21.9 мин |
| 21 | 133B | 319B | 4 мин | 1.33B | 63.3M | 35.2 мин |
| 22 | 215B | 334B | 4 мин | 1.39B | 63.3M | 56.7 мин |
| 23 | 346B | 349B | 4 мин | 1.45B | 63.3M | 1.5 ч |
| 24 | 558B | 364B | 4 мин | 1.52B | 63.3M | 2.4 ч |
| 25 | 898B | 380B | 4 мин | 1.58B | 63.3M | 3.9 ч |
| 26 | 1.44T | 395B | 4 мин | 1.64B | 63.3M | 6.3 ч |
| 27 | 2.32T | 410B | 4 мин | 1.71B | 63.3M | 10.2 ч |
| 28 | 3.74T | 425B | 4 мин | 1.77B | 63.3M | 16.4 ч |
| 29 | 6.03T | 440B | 4 мин | 1.83B | 63.3M | 1.1 д |
| 30 | 9.71T | 456B | 4 мин | 1.9B | 63.3M | 1.8 д |
| 31 | 15.6T | 471B | 4 мин | 1.96B | 63.3M | 2.9 д |
| 32 | 25.1T | 486B | 4 мин | 2.02B | 63.3M | 4.6 д |
| 33 | 40.5T | 501B | 4 мин | 2.09B | 63.3M | 7.4 д |
| 34 | 65.3T | 516B | 4 мин | 2.15B | 63.3M | 11.9 д |
| 35 | 105T | 532B | 4 мин | 2.21B | 63.3M | 19.2 д |
| 36 | 169T | 547B | 4 мин | 2.28B | 63.3M | 30.9 д |
| 37 | 272T | 562B | 4 мин | 2.34B | 63.3M | 49.8 д |
| 38 | 438T | 577B | 4 мин | 2.4B | 63.3M | 80.2 д |
| 39 ★ | 706T | 592B | 4 мин | 2.47B | 2.59B | 3.1 д |
| 40 | 1.13aa | 1.21T | 4 мин | 5.06B | 126M | 103.9 д |

### Улучшение чертежей (`CardUpgradeCosts`, E-14)

Переход L → L+1: копии и эликсир; в скобках — эликсир нарастающим итогом от уровня 1. Показаны первые 10 переходов.

| Редкость | Макс. ур. | 1→2 | 2→3 | 3→4 | 4→5 | 5→6 | 6→7 | 7→8 | 8→9 | 9→10 | 10→11 |
|---|---:|---|---|---|---|---|---|---|---|---|---|
| Обычная | 14 | 2 шт. + 50 (50) | 5 шт. + 100 (150) | 10 шт. + 200 (350) | 20 шт. + 300 (650) | 50 шт. + 400 (1.05K) | 100 шт. + 800 (1.85K) | 200 шт. + 1.5K (3.35K) | 400 шт. + 3K (6.35K) | 800 шт. + 6K (12.3K) | 1000 шт. + 12K (24.3K) |
| Необычная | 12 | 2 шт. + 100 (100) | 5 шт. + 200 (300) | 10 шт. + 400 (700) | 20 шт. + 1K (1.7K) | 50 шт. + 2K (3.7K) | 100 шт. + 4K (7.7K) | 200 шт. + 8K (15.7K) | 400 шт. + 16K (31.7K) | 800 шт. + 32K (63.7K) | 1000 шт. + 64K (127K) |
| Редкая | 9 | 2 шт. + 500 (500) | 5 шт. + 2K (2.5K) | 10 шт. + 5K (7.5K) | 20 шт. + 10K (17.5K) | 50 шт. + 25K (42.5K) | 100 шт. + 50K (92.5K) | 150 шт. + 75K (167K) | 200 шт. + 100K (267K) | — | — |
| Эпическая | 7 | 2 шт. + 2K (2K) | 5 шт. + 8K (10K) | 10 шт. + 15K (25K) | 20 шт. + 30K (55K) | 50 шт. + 60K (115K) | 100 шт. + 100K (215K) | — | — | — | — |
| Легендарная | 5 | 2 шт. + 5K (5K) | 5 шт. + 20K (25K) | 10 шт. + 50K (75K) | 20 шт. + 100K (175K) | — | — | — | — | — | — |
| Величественная | 5 | 4 шт. + 10K (10K) | 6 шт. + 25K (35K) | 9 шт. + 60K (95K) | 12 шт. + 100K (195K) | — | — | — | — | — | — |


## Производительность (ТЗ 15.3)

Замер — PlayMode-тест `PerformanceTests.EarlyAndLateZoneFrameTimes` (ранняя zone5 ×1, поздние zone93 и zone117 ×1 и ×100) в Editor (batchmode), рендер в `RenderTexture` 1080×1920 вместе с HUD, без ограничения частоты кадров, прокрутка всей карты за время замера. На устройстве не измерялось: APK не собран (`build-android.json`: ModuleMissing).

| Параметр | Значение |
|---|---|
| unityVersion | 6000.3.11f1 |
| platform | WindowsEditor (batchmode) |
| deviceModel | 82RG (LENOVO) |
| cpu | AMD Ryzen 5 6600H with Radeon Graphics  x12 |
| gpu | AMD Radeon(TM) Graphics (Direct3D11) |
| ramMb | 15557 |
| quality | Mobile |
| render | 1080x1920 RenderTexture, HUD ScreenSpaceCamera, targetFrameRate=-1, vSync=0 |
| balanceLoadMs | 55.314 |
| simAllocBytesPerFrameX1 | — |
| simAllocBytesPerFrameX100 | — |

| Зона | Скорость | Объектов | Котиков (работают) | Построение зоны, мс | Медиана кадра, мс | p95, мс | Макс., мс | Средний FPS | GC за кадр, байт (сред./макс.) |
|---|---:|---:|---|---:|---:|---:|---:|---:|---|
| zone5 | ×1 | 64 | 10 (4) | 42.271 | 2.184 | 2.947 | 21.904 | 426.895 | 638.573 / 68504 |
| zone93 | ×1 | 240 | 10 (7) | 301.783 | 2.626 | 3.765 | 113.144 | 291.027 | 614.613 / 46016 |
| zone93 | ×100 | 240 | 10 (7) | 301.783 | 2.337 | 3.223 | 10.12 | 414.789 | 706.08 / 97802 |
| zone117 | ×1 | 239 | 10 (7) | 184.727 | 2.354 | 3.185 | 5.049 | 413.111 | 537.003 / 13626 |
| zone117 | ×100 | 239 | 10 (7) | 184.727 | 2.48 | 3.353 | 21.351 | 387.841 | 621.745 / 45387 |

GC в этой таблице — вторичное чтение: ProfilerRecorder «GC Allocated In Frame» во время прокрутки, ×1 и ×100.

Выделения по ТЗ 15.3 — приёмочное чтение: не измерено: GC.GetAllocatedBytesForCurrentThread не видит пробу 4096 байт (разница показаний 0 байт), в этом рантайме счётчик не работает; независимая проверка — recorderAllocBytesPerFrame* и simAllocSamplesX1.

**Счётчик `GC.GetAllocatedBytesForCurrentThread` в этом рантайме не работает**: проба 4096 байт дала разницу 0 байт. Приёмка ТЗ 15.3 по нему **не измерена**; ниже — независимая проверка со своей пробой.

| Показатель | Значение | Цель | Выполнено |
|---|---:|---:|---|
| Симуляция ×1, байт на кадр | не измерено | 0 | **не измерено** |
| Всего, байт на кадр, среднее (все кадры) — приёмка | не измерено | 1024 | **не измерено** |
| Всего, байт на кадр, среднее без кадров автосохранения | не измерено | 1024 | **не измерено** |
| Проверка: симуляция ×1, вызовов GC.Alloc за 600 шагов (×100: 0) | 0 | 0 | да |
| Проверка: ProfilerRecorder «GC Allocated In Frame», байт на кадр, среднее (все кадры) | 853.717 | 1024 | да |
| Проверка: ProfilerRecorder, байт на кадр, среднее без кадров автосохранения | 485.313 | 1024 | да |

| Зона | Кадров | FPS | Кадров с автосохранением | Кадров с панелью | Всего: среднее / медиана / макс., байт | Без автосохранения: среднее / медиана / макс., байт | ≤ 1 КБ (все / без автосохр.) |
|---|---:|---:|---:|---:|---|---|---|
| zone5 | 600 | 57.029 | 5 | 0 | не измерено / не измерено / не измерено | не измерено / не измерено / не измерено | **не измерено** / **не измерено** |
| zone93 | 600 | 53.976 | 5 | 0 | не измерено / не измерено / не измерено | не измерено / не измерено / не измерено | **не измерено** / **не измерено** |
| zone117 | 600 | 60.005 | 5 | 0 | не измерено / не измерено / не измерено | не измерено / не измерено / не измерено | **не измерено** / **не измерено** |

Проверка: recorderAllocBytesPerFrame*: ProfilerRecorder GC Allocated In Frame, отдельные 600 кадров x1 в темпе 60 FPS (editor-spin) без панелей, прокрутки и обращений к диску, кадры от 20000 байт считаются автосохранением; simAllocSamples*: вызовы GC.Alloc главного потока (Recorder GC.Alloc) за 600 шагов симуляции.

| Зона | Проба счётчика, байт (из 4096) | Проба ProfilerRecorder, байт | Кадров | Кадров автосохранения (≥ 50 КБ) | ProfilerRecorder: среднее / медиана / макс., байт | Без автосохранения: среднее / медиана / макс., байт | ≤ 1 КБ (все / без автосохр.) |
|---|---:|---:|---:|---:|---|---|---|
| zone5 | 0 | 4586 | 600 | 5 | 671.338 / 458 / 22829 | 485.313 / 458 / 10202 | да / да |
| zone93 | 0 | 4586 | 600 | 5 | 850.417 / 458 / 44796 | 481.126 / 458 / 10202 | да / да |
| zone117 | 0 | 4586 | 600 | 5 | 853.717 / 458 / 45192 | 481.126 / 458 / 10202 | да / да |

Цели ТЗ: Android p95 ≤ 33 мс; Editor с Quality = Mobile — ориентир 60 FPS; в установившемся режиме ×1 без панелей, 600 кадров — 0 байт на кадр в симуляции и не больше 1 КБ на кадр всего (`GC.GetAllocatedBytesForCurrentThread`). В Editor главный поток включает и выделения самого редактора.

Сохранение (`Docs/Reports/save-perf.json`, dotnet, zone93):

| Параметр | Значение |
|---|---|
| Сериализация, медиана, мс | 0.368 |
| Запись на диск, медиана, мс | 13.717 |
| Запись на диск, p95, мс | 16.795 |

## Сборка Android

Итог: ModuleMissing: нет модулей SDK, NDK, OpenJDK.

Недостающие модули: `SDK`, `NDK`, `OpenJDK`.

| Поле | Значение |
|---|---|
| result | ModuleMissing |
| missing | [    "SDK",    "NDK",    "OpenJDK"  ] |
| message | Нет модуля Android Build Support: SDK, NDK, OpenJDK. Unity Hub → Installs → Add modules → Android Build Support (Android SDK & NDK Tools, OpenJDK). |
| source | unity.ps1 |
| editor | C:\Unity\Editor\Unity.exe |
| internalOnly | True |
| checkedAtUtc | 08/10/2026 00:03:37 |

APK — internal only (ТЗ 4.5): не публикуется.

## Открытые вопросы

Решения по умолчанию и альтернативы — ТЗ, Приложение D (D-1…D-11); статус каждого правила — `Docs/BALANCE_MAPPING.md`; отличия и предложения — `Docs/IMPLEMENTATION_NOTES.md`.
