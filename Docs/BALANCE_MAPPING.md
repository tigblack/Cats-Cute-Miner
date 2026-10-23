# BALANCE_MAPPING — рабочий профиль баланса v1 (`rulesVersion = 1`)

Реестр правил по ТЗ, Приложение C: путь к коду, метка, контрольные значения и статус каждого правила (ТЗ 4.2, 19 п. 3).

Метки:
- [Д] — однозначно следует из JSON, проверено на всех записях;
- [П] — подтверждено публичными источниками референса;
- [Р] — решение владельца, рабочая трактовка.

Правила [Р] — рабочие правила нашей игры, а не восстановленный исходный алгоритм. Каждое правило реализовано в одном месте; профиль меняется только новой версией `rulesVersion` (`Formula.RulesVersion`).

Статусы:
- `реализовано; тест <Класс.Метод>` — правило в коде, тест проверяет контрольное значение числом (значение записано в тесте литералом);
- `реализовано, не покрыто тестом` — правило в коде, теста с контрольным значением нет;
- `не проверено` — ни один тест не проверяет это значение.

Тесты лежат в `Assets/Game/Tests/EditMode/` и проходят в Unity и через `dotnet test` (`Tools/CoreTests/run.ps1`). Основной набор контрольных значений Приложения C — `FormulaAppendixCAcceptanceTests` (в сессии) и `FormulaControlValueTests` в `NumberAndFormulaTests.cs` (формулы на данных).

## Токены `Zones[].Grid`

| Токен | Поля | Код |
|---|---|---|
| `r:id:level` | камень, уровень цели | `ZoneLayout.ParseToken` |
| `s:id:level:reward:card:cardLevel` | шахта, уровень расчистки (HP), награда активации, manager-карта, требуемый уровень | `ZoneLayout.ParseToken` |
| `x:id` | препятствие, размер из `Obstructions` | `ZoneLayout.ParseToken` |
| `p:checkpoint:level:reward` | ворота | `ZoneLayout.ParseToken` |
| `e:exit:level:reward` | выход | `ZoneLayout.ParseToken` |
| `c:spawningcart:level` | кузница | `ZoneLayout.ParseToken` |

Footprint `+column / −row` от якоря [Д]: `ZoneLayout.Parse`, проход 2. Индекс клетки: `column = i % W`, `row = i / W`.

## Формулы (C.1)

Почему формы именно такие [Д]: данные исключают альтернативы — `B·G^x` для FT1 дал бы ноль (`B = 0` у урона, найма и manager-карт), умножение на `M` во FT2 обнулило бы цену (у всех `UpgradeCost` `M = 0`, а цена растёт).

| ID | FormulaType | Форма | Метка | Код | Контрольные значения | Статус |
|---|---|---|---|---|---|---|
| F-0 | 0 | `B + M·x + G·x²` | [Д] | `Formula.Ft0` | Выход кузницы L1 = 0 + 3·1 = 3; неприменяемая `ca027` (0; 0.3; 0.1) на L9 = 10.8 — квадратичный член только у неприменяемых карт | реализовано; тест `FormulaAppendixCAcceptanceTests.F0_QuadraticTermAndFR_HalfAwayFromZero`, `FormulaControlValueTests.E03_UpgradeCosts` |
| F-1 | 1 | `B + M·G^(x−1)` | [Д] | `Formula.Ft1` | `Miners.Damage` L1 = `Multiplier` = 1; первая покупка = 2 золота; шестая `2·4^5` = 2048 точно (не 2048.0000000001) | реализовано; тест `FormulaControlValueTests.E02_CatDamage`, `FormulaControlValueTests.E05_HireCostAndLevel` |
| F-2 | 2 | `B·G^(x−1) + M·(x−1)` | [Д] | `Formula.Ft2` | Первое улучшение кузницы = `Base` = 72; интервал доставки при x = 1 = 22 с | реализовано; тест `FormulaControlValueTests.E03_UpgradeCosts`, `FormulaControlValueTests.E15_DeliveryIntervalAndQuantity` |
| F-3 | 3 | `R(B + M·(G·x)^P, \|Round\|)` | [Д] | `Formula.Ft3Number` (в `GameNumber`); `Formula.Ft3` — то же в double | Секунды `rockbasic` L1 = 105; Core id15 на ранге 4 = 60 | реализовано; тест `FormulaControlValueTests.E09_RockCoreSecondsAndSoft`, `FormulaControlValueTests.E15_DeliveryIntervalAndQuantity` |
| Аргумент | — | `x` — порядковый номер с 1 (уровень, номер покупки, номер порога, ранг); показатель `x − 1`. Исключение: уровень найма (E-05) — `x = n`, число уже совершённых покупок с 0; цена той же покупки — от `x = n + 1` | [Р] | `Formula.Evaluate`; `GameSession.HireLevel`, `GameSession.HirePrice` | Урон L1 = 1, первая покупка = 2, первое улучшение = `Base` = 72; уровень найма: n = 0 → 1, n = 5 → 2, n = 21 → 4 | реализовано; тест `FormulaControlValueTests.E05_HireCostAndLevel`, `FormulaAppendixCAcceptanceTests.E05_HirePriceAndLevelBySession` |
| F-R | — | Округление — шесть правил ниже | [Р] | `Formula.RoundToMultiple`, `Formula.Price`, `Formula.Payout`, `Formula.RoundHalfUp` | см. таблицу F-R | реализовано; тест `FormulaControlValueTests.RoundingRules`, `FormulaAppendixCAcceptanceTests.FR_RoundingIsIdentityAbove2Pow53AndNeverMovesWholeUnits` |

**F-R. Округление [Р]** (все шесть пунктов ТЗ C.1):

| Пункт | Правило | Код | Контрольные значения | Статус |
|---|---|---|---|---|
| 1 | `R(v, r)` — к ближайшему кратному `\|r\|`, половина от нуля, допуск 1e-9; при `r = 0` округления нет | `Formula.RoundToMultiple` | R(22.5, 15) = 30; R(−22.5, 15) = −30; R(7.5, −15) = 15; R(109.9, 15) = 105; R(109.9, 0) = 109.9 | реализовано; тест `FormulaAppendixCAcceptanceTests.F0_QuadraticTermAndFR_HalfAwayFromZero`, `FormulaControlValueTests.RoundingRules` |
| 2 | Цены (наём, улучшения) — вверх до целого, не меньше 1 | `Formula.Price` | Price(0.2) = 1; кузница 72; 103; 148; 211; Price(2048.0000000001) = 2048 | реализовано; тест `FormulaControlValueTests.RoundingRules`, `FormulaControlValueTests.E03_UpgradeCosts`, `FormulaAppendixCAcceptanceTests.FR_RoundingIsIdentityAbove2Pow53AndNeverMovesWholeUnits` |
| 3 | Выдаваемые золото и эликсир — `max(1, floor(…))` при положительном результате | `Formula.Payout` | Payout(0.3) = 1; Payout(0) = 0; камень L1 при 1.5/с: floor(157.5) = 157 | реализовано; тест `FormulaControlValueTests.RoundingRules`, `FormulaControlValueTests.E09_RockCoreSecondsAndSoft` |
| 4 | Число карт сундука `N` — к ближайшему целому, половина вверх (10.3 п. 4) | `Formula.RoundHalfUp` | 4.5 → 5; 6.5 → 7; 52.5 → 53; 0.7 → 1 | реализовано; тест `FormulaControlValueTests.RoundingRules` |
| 5 | HP, урон и выход генератора не округляются | `GameSession.CatDamage`, `GameSession.GeneratorOutput` (без `Price`/`Payout`) | Урон L2 = 2.1; котик L2 по камню L2 (HP 60) оставляет 57.9 HP | реализовано; тест `FormulaAppendixCAcceptanceTests.E02_CatParametersInSession` |
| 6 | Для значений больше 2^53 округление — тождественная операция `GameNumber` | `Formula.Price`, `Formula.Payout` | Price(2^53 + 2) = 2^53 + 2; Price(1e20) = 1e20; цена кузницы L92→93 (≈ 9.84e15) = сырая формула | реализовано; тест `FormulaAppendixCAcceptanceTests.FR_RoundingIsIdentityAbove2Pow53AndNeverMovesWholeUnits` |

Степени в диапазоне double считаются точным `Math.Pow`: `2·4^5` = 2048, а не 2048.0000000001. Вне диапазона — через логарифм (`GameNumber.Pow`).

### Инварианты данных, которые проверяет валидатор (C.1)

| № | Инвариант | Проверка `BalanceValidator.ValidateSchema` (путь ошибки) | Статус |
|---|---|---|---|
| 1 | `UpgradeCostMultiplier = 0` у всех 21 генератора | `SpawningCart[0].UpgradeCostMultiplier`, `MineShafts.<id>.UpgradeCostMultiplier` | реализовано; тест `DataValidatorFixTests.UpgradeMultiplierAndZeroBaseInvariantsAreValidatorErrors` |
| 2 | `B = 0` у `Miners.Damage`, `Reinforcements.Cost` и manager-карт | `Miners.<id>.DamageBase`, `Reinforcements[0].CostBase`, `Cards.<id>.ModifierBase` (manager) | реализовано; тест `DataValidatorFixTests.UpgradeMultiplierAndZeroBaseInvariantsAreValidatorErrors` |
| 3 | `G = 0` у всех применяемых в v1 записей FT0 | `RequireFt0Linear` (FormulaType = 0 и Growth = 0): `SpawningCart[0]` `CurrencyOutput`, `SpawnDelaySec`, `GenerationDelaySec`; `Reinforcements[0].Level`; `Rocks.<id>.SoftCurrency`; `GeneratorObjectives.<id>.SoftCurrencyReward`; `MineShafts.<id>` `CurrencyOutput`, `GenerationDelaySec`; `Miners.<id>` `AttackDelaySec`, `CritChance`, `CritPower`; `Spells[0]` `CritChance`, `CritPower`. `ValidateCheckpointFt0`: массивы `CheckPoint[0].Modifier*` длины `StatModifierType`, `ModifierFormulaType[i] = 0`, `ModifierGrowth[i] = 0`, `ModifierMultiplier[i] = 0`. Применяемые карты (коды 9, 10, 7, 1) с FT0: `Cards.<id>.ModifierGrowth` | реализовано; тест `DataValidatorFixTests.NonZeroGrowthOrNonFt0InAppliedRecordsAreValidatorErrors` |

`DeliveryDelaySec` (FT2, `M = 1`) и урон `Spells` (FT1, `B = 750`) — допустимые записи, валидатор их не отмечает (`DataValidatorFixTests.LegitimateNonFt0RecordsAreNotFlagged`). На текущем JSON валидатор даёт 0 ошибок на 144 зонах (`LayoutTests.ValidatorPassesOnAllZones`).

## Геометрия (C.2)

| ID | Правило | Метка | Код | Проверка | Статус |
|---|---|---|---|---|---|
| G-01 | Footprint `+column / −row` от якоря | [Д] | `ZoneLayout.Parse` | Сводка зон совпадает с `zones_summary.csv`; `amethyst` zone2 — клетки (0..1, 3..5) | реализовано; тест `LayoutTests.MatchesZonesSummaryCsv`, `LayoutTests.AmethystFootprintInZone2`, `DataGeometryHpAcceptanceTests.G2_RsxFootprintsOccupyExpectedCellsInAllZones` |
| G-02 | Выход 3×1 `(2..4, 0)`, рабочая строка 1 | [Д] | `ZoneLayout.ExitWidth` | zone1: рабочие клетки выхода (2, 1), (3, 1) | реализовано; тест `FormulaAppendixCAcceptanceTests.G02_G03_ExitAndGateSidesAndWorkRows`, `LayoutTests.SpecialObjectGeometry` |
| G-03 | Ворота 5×1 `(1..5, row_p)`, строка перекрыта, сектора по строкам ворот, рабочая строка `row_p + 1`, площадка `row_p − 3 … row_p − 1` | [Д] | `ZoneLayout.GateWidth`, `ZoneLayout.SectorOfRow` | 280 ворот, у всех (0, row_p) и (6, row_p) — препятствия; zone5: ворота в строке 11, рабочие (2..4, 12) | реализовано; тест `FormulaAppendixCAcceptanceTests.G02_G03_ExitAndGateSidesAndWorkRows`, `DataGeometryHpAcceptanceTests.G2_GatesAreFiveByOneWithObstructionsAtBothEnds` |
| G-04 | Кузница: по данным 2×2 `(1..2, row_c − 1 … row_c)`, в игре 2×3 `(1..2, row_c − 1 … row_c + 1)` (ТЗ 6.4), не цель | [Р] | `ZoneLayout.CartWidth`, `CartDepth`, `RowC` | строка `Depth − 4` в 142 зонах; HP-таблицы нет | реализовано; тест `FormulaAppendixCAcceptanceTests.G04_CartFootprintPadAndNotATarget` |
| G-05 | Рабочие клетки — 4-соседство (6.5) | [Р] | `GameSession.WorkCells` | zone2: у `amethyst` после расчистки 4 рабочие клетки (2, 3), (2, 4), (2, 5), (1, 6) | реализовано; тест `FormulaAppendixCAcceptanceTests.G05_WorkCellsAreFourNeighbours`, `DataGeometryHpAcceptanceTests.G2_WorkCellsMatchTzExamples` |
| G-06 | Клетка появления — `SpawnPad` и BFS от нижнего ряда зоны мержа (6.6) | [Р] | `GameSession.FindSpawnCell` | Без ворот: BFS от (3, row_c − 2); с открытыми воротами — от (3, row_p − 1). Результат — сначала свободная клетка зоны мержа, затем остальная `SpawnPad`, затем вся открытая область | реализовано; тест `FormulaAppendixCAcceptanceTests.G06_SpawnCellOrder`, `DataGeometryHpAcceptanceTests.G2_SpawnCellsFollowBfsFromStartPadInAllZones` |
| G-07 | Ширина поля 7 (максимум сетки); зона мержа — самая нижняя пара соседних полностью свободных строк `SpawnPad` (6.4) | [Д]/[Р] | `ZoneLayout.TryMergeRows`, `GameSession.MergeRows`, `BalanceValidator` | Старт: `row_c − 3`, `row_c − 2`; после ворот: `row_p − 2`, `row_p − 1` | реализовано; тесты `MergeZoneTests` (все 144 зоны и 280 ворот) |

## Экономика (C.3)

| ID | Правило | Метка | Код | Контрольные значения (ТЗ C.3) | Статус |
|---|---|---|---|---|---|
| E-01 | `Rank` = номер текущей зоны; `Cards.MineUnlock ≤ Rank` — карта может выпасть из случайного сундука; `CheckPointUnlock = N` — в зоне открыто не меньше N ворот | [Р] | `GameSession.Rank`, `GachaService.Candidates`, `GameSession.DeliveryPool` | zone1 → Rank 1 | реализовано; тест `FormulaAppendixCAcceptanceTests.E01_RankMineUnlockAndCheckPointUnlock` |
| E-02 | Урон котика `F-1(0, 1, 2.1; x = L) = 2.1^(L−1)`; задержка атаки 1 с; крит 0 / ×2 | [П] рост ×2.1; [Р] база | `GameSession.CatDamage`, `AttackDelayTicks`, `CritChance`, `CritPower` | L1…L4: 1; 2.1; 4.41; 9.261. Камень L1 (HP 16) — 16 ударов котика L1 | реализовано; тест `FormulaAppendixCAcceptanceTests.E02_CatParametersInSession`, `FormulaControlValueTests.E02_CatDamage` |
| E-03 | Выход за цикл `F-0(CurrencyOutput*; x = L)` × множители (9.4); цикл = `GenerationDelaySecBase`; цена L→L+1 `ceil(F-2(UpgradeCost*; x = L))` | [Р] | `GameSession.GeneratorOutput`, `GeneratorCycleTicks`, `UpgradeCost` | Кузница: выход L1 = 3 за 2 с; цены 72; 103; 148; 211. `amethyst`: выход L1 = 400 за 8 с; цены 512; 748; 1092 | реализовано; тест `FormulaAppendixCAcceptanceTests.E03_ForgeAndAmethystInSession`, `FormulaControlValueTests.E03_UpgradeCosts` |
| E-04 | Пороги `T_k = 1 + Σ_{i≤k} ObjectiveCount[i]`, множители перемножаются, последний порог 1200 — максимум уровня; эликсир за k-й порог `max(1, floor(F-0(SoftCurrencyReward*; x = k) × GenObjectiveSoftCurrencyMultiplier))` | [П] пороги; [Д] последний порог 1200; [Р] это максимум уровня; [Р] эликсир | `GameSession.DefFor`, `ApplyThresholds` | Исходные данные: `spawningcart` [9, 10, 10, 10] / [2, 2, 2, 10], `amethyst` [9, 10, 20, 20] / [2, 2, 2, 200]. Кузница: T = 10, 20, 30, 40 → ×2, ×2, ×2, ×10. `amethyst`: T = 10, 20, 40, 60 → ×2, ×2, ×2, ×200. Эликсир `amethyst` при k = 1 в zone2: floor(2.0 × 1.025) = 2 | реализовано; тест `FormulaAppendixCAcceptanceTests.E04_ForgeThresholds`, `FormulaAppendixCAcceptanceTests.E04_AmethystThresholdsAndElixirInZone2`, `FormulaAppendixCAcceptanceTests.E04_LastThresholdIsMaxLevel1200` |
| E-05 | Наём: `n` — покупки за золото в зоне; цена `ceil(F-1(0, 2, 4; x = n + 1) / Π(10 за каждые открытые ворота))`, не меньше 1; уровень `L_next = 1 + floor(F-0(0.4, 0.125, 0; x = n))`; бесплатные котики и котики доставок `n` не меняют; сброс при переходе зоны. В игре (`GameSession.BarrelHires`) покупка создаёт бочку `ContainerKind.Hire` с уровнем `L_next`; цена, `n` и уровень меняются при покупке, открытие бочки их не трогает (ТЗ 7.2) | [П] ×4 за покупку, скидка за ворота, шкала уровня; [Р] первая цена, 0.4, величина скидки | `GameSession.HirePrice`, `HireLevel` | Цены 2; 8; 32; 128; 512; 2048. Уровни по номеру покупки: 1–5 → 1, 6–13 → 2, 14–21 → 3. После одних ворот цены 1; 1; 4; 13 | реализовано; тест `FormulaAppendixCAcceptanceTests.E05_HirePriceAndLevelBySession`, `FormulaAppendixCAcceptanceTests.E05_PricesAfterOneGate`, `FormulaAppendixCAcceptanceTests.E05_FreeCatsDoNotCountAndZoneResets` |
| E-06 | Бесплатный котик: интервал `2400 − 60 × (открытые ворота) − F-0(ca029; x = уровень ca029)`, не меньше 60 с; уровень `max(1, L_next − 1)` | [П] 40 мин и «уровень покупки − 1»; [Р] остальное | `GameSession.FreeCatIntervalSeconds`, `TrySpawnPending` | 2400 с; после одних ворот 2340 с | реализовано; тест `FormulaAppendixCAcceptanceTests.E06_FreeCatIntervalAndLevel` |
| E-07 | Manager-карта (код 9): автоматизация шахты по токену `s` (уровень карты ≥ требуемого); множитель выхода генераторов из `TargetIds` `F-1(0, 1, G; x = уровень)`; несколько карт перемножаются; карты нет — множитель 1 | [П] автоматизация картой и эликсиром, «increase the profit»; [Р] формула множителя | `GameSession.CheckAutomation`, `ManagerMultiplier` | Common L1 ×1, L2 ×3, L14 ×1 594 323. Uncommon L12 ×4 194 304 | реализовано; тест `FormulaAppendixCAcceptanceTests.E07_ManagerMultipliersInSession`, `FormulaControlValueTests.E07_E16_CardsAndDynamite` |
| E-08 | Коды эффектов ворот и карт: 10 — множитель выхода всех генераторов (ворота ×2 за каждые; `ca034` F-1); 2 — делитель цены найма (ворота ÷10); 7 — сокращение интервала бесплатного котика (ворота 60 с, `ca029` 60·L с); 1 — прибавка к лимиту котиков (ворота +1, `ca038` +L); остальные коды не применяются | [Д] константы ворот; [Р] смысл кодов | `GameSession.CheckpointValue`, `GlobalIncomeMultiplier`, `HireDivisor`, `FreeCatIntervalSeconds`, `UnitCap` | После 2 открытых ворот: выход ×4, цена найма ÷100, интервал 2280 с, лимит 12 | реализовано; тест `FormulaAppendixCAcceptanceTests.E08_TwoGatesBonuses`, `FormulaAppendixCAcceptanceTests.E08_CardCodes`, `HireMergeAcceptanceTests.TwoGatesDivideHirePriceBy100AndRaiseCapTo12` |
| E-09 | Награды камней: счётчик на `Rocks.Id` в `PlayerProgressState`, `mod` длина цикла. `Core`: секунды `F-3(CoreCurrencySeconds*; x = уровень)`, золото `max(1, floor(секунды × GetRewardIncomeRate))`. `Soft`: `max(1, floor(F-0(SoftCurrency*; x = уровень) × MiningSoftCurrencyMultiplier))`. `Hard` = `Quantity`. `BattlePass` не выдаётся. В игре (`GameSession.LootPickups`) сумма та же, но падает добычей на поле и начисляется `CollectLoot` или автосбором через 30 с, с прежними ключами журнала (ТЗ 10.2); тесты профиля идут с выключенным переключателем | [Д] сквозной счётчик; [Р] формулы | `GameSession.GrantRockReward`, `CollectLoot` | Секунды: L1 105, L2 225, L3 360. Золото камня L1 при доходе 1.5/с = 157. Эликсир `rocksoftcurrencysmall` L2 в zone1 = max(1, floor(21 × 0.1)) = 2 | реализовано; тест `FormulaAppendixCAcceptanceTests.E09_RockRewardsInSession`, `FormulaControlValueTests.E09_RockCoreSecondsAndSoft` |
| E-10 | `GetRewardIncomeRate` = выход в секунду кузницы и автоматизированных шахт со всеми множителями, не меньше `NoGeneratorsCurrencyRate = 1.5` | [П] «Forge plus automated mineshafts»; [Р] нижняя граница | `GameSession.GetRewardIncomeRate` | Только кузница L1: 1.5 | реализовано; тест `FormulaAppendixCAcceptanceTests.E10_RewardIncomeRate` |
| E-11 | Сундук: класс множителей по `GachaType` (0 Normal, 1 Premium, 3 Rare, 2 — без множителей); `N = floor(BaseNumCards × GachaCardsMult + 0.5 + 1e-9)`, не меньше 1 при `BaseNumCards > 0`; редкость `floor(N / Weight)` плюс остаток с вероятностью дробной части; Common — остаток; эликсир равномерно в `[Min; Max]` × `GachaSoftCurrencyMult`, вниз | [П] `N`, классы, `floor(N/Weight)`; [Р] остаток, эликсир | `GachaService.Open` | zone44: Wooden 24 карты, не меньше 3 Uncommon; Iron 48, не меньше 6 Uncommon; Golden 102, не меньше 25 Uncommon и 2 Rare. zone1 Wooden: 1 карта, эликсир 15–19. Половины: zone2 Golden 4.5 → 5; zone5 Forged 6.5 → 7; zone14 Crusher 52.5 → 53 | реализовано; тест `FormulaAppendixCAcceptanceTests.E11_ChestCardCountsAtZone44`, `FormulaAppendixCAcceptanceTests.E11_HalvesRoundUpAndZone1Wooden`, `RewardChestAcceptanceTests.ChestCardCounts_Zone44ReproducesPublicTable` |
| E-12 | Выбор карты внутри редкости: `MineUnlock ≤ Rank`; вес 1 × `ManagerChanceMult` × `LowCardChanceMult`; стопки по `CardStackCounts` / `*StackSplitAt` (10.3 п. 7) | [Р] | `GachaService.DrawRandomCards`, `GachaService.StackCount` | Common 24 шт. → 2 разные карты по 12 (порог 7) | реализовано; тест `FormulaAppendixCAcceptanceTests.E12_Stacks` |
| E-13 | Награда выхода без Crusher: у записей с непустым `GuaranteedCardMin` — `GuaranteedCardMin` и `SoftCurrencyMin`; `CardMultMax` не применяется; `GachaCrusher` — обычный сундук | [Р] | `GachaService.Open` (`rangeMin`) | zone2: `ca001`×1, `ca002`×1, 30 эликсира. zone3: `ca002`×3, `ca014`×1, 50 эликсира | реализовано; тест `FormulaAppendixCAcceptanceTests.E13_ExitRewardLowerBound`, `TransitionAcceptanceTests.ExitRewardZones1To10UsesLowerBound` |
| E-14 | Улучшение карты: `[0]` = переход 1→2; списываются `Duplicates[L−1]` дубликатов сверх первой копии и `SoftCurrency[L−1]` эликсира; максимум = 1 + длина массива. Редкость 1…6 = Common … Majestic | [П] | `GameSession.CanUpgradeCard`, `UpgradeCard` | Common: 1→2 — 2 дубликата и 50 эликсира; максимум 14. Uncommon: максимум 12 | реализовано; тест `FormulaAppendixCAcceptanceTests.E14_CardUpgradeCostsAndMaxLevels` |
| E-15 | Доставки: включение при `Rank ≥ 4` и не меньше 2 активированных шахтах; первая через 20 с; интервал `F-2(22, 1, 1.02; x = полученных за 3600 с + 1) × U[0.9; 1.1]`; пул и пропуск рекламы по 10.5; котик `max(1, L_next + Q)`, догоняние при `DeliveryForeverAloneDelta`; Core и Soft `F-3(x = Rank)`; динамит — 1 заряд | [П] старт около 25 с, рост, «только золото» при перегреве; [Р] формы и аргументы | `GameSession.Time.cs` (`UpdateUnlocks`, `TryCreateDelivery`, `DeliveryCatLevel`, `NextDeliveryIntervalSeconds`) | Интервал без разброса: 22 с; после 10 получений 36.8 с; после 30 — 69.8 с. Core id15 на ранге 4: 60 с дохода; на ранге 10: 300 с. Котик при `L_next = 3`: уровень 1 | реализовано; тест `FormulaAppendixCAcceptanceTests.E15_DeliveriesInSession`, `FormulaControlValueTests.E15_DeliveryIntervalAndQuantity` |
| E-16 | Динамит: уровень = максимальный уровень котика на поле; урон `F-1(750, 300, 2.1; x = L)`; запас не больше 4 | [П] уровень, «5 минут работы», 4 шт.; [Р] 1 заряд за доставку | `GameSession.UseDynamite`, `DynamiteDamage` | L1 1050; L2 1380; L3 2073 | реализовано; тест `FormulaAppendixCAcceptanceTests.E16_DynamiteInSession`, `FormulaControlValueTests.E07_E16_CardsAndDynamite` |
| E-17 | Бесплатный сундук `FreeGachaCycle`: включается при первом выполнении условий доставок, флаг постоянный; первый — через 4 ч после включения; запас не больше 2, при полном запасе таймер стоит; строгий порядок `GachaIds`, после конца — с начала | [П] раз в 4 ч; [Р] остальное | `GameSession.ClaimFreeChest`, таймер в `ProcessDueEvents` | Первые: `scg_free_01` (`ca002`, `ca015`, 19 эликсира), `scg_free_02` (`ca001`×2, 22 эликсира). Включение в t0 → сундуки в t0 + 4 ч и t0 + 8 ч, в t0 + 12 ч запас 2 | реализовано; тест `FormulaAppendixCAcceptanceTests.E17_FreeChestTimerContentsAndPersistence` |
| E-18 | Ручная шахта: один буфер выплаты, следующий цикл после сбора | [П] | `ProcessDueEvents`, `CollectPayout` | — (в ТЗ числа нет); в тесте: буфер citrine 6000 не копится, офлайн 8 ч — один буфер 800 | реализовано; тест `ShaftGateAcceptanceTests.Zone3CitrineClearedByCatThenManualBufferThenAutomatedByCa003Level1`, `ShaftGateAcceptanceTests.ManualPayoutIsFixedAtCycleEndAndGateDoublesOnlyNextCycle` |
| E-19 | Офлайн-лимит 8 ч для всех систем | [Р] (в JSON значения нет) | `OfflineProgress.Apply` | — (в ТЗ числа нет); в тесте: 28 800 с, кузница L1 за 8 ч — 43 200 золота | реализовано; тест `FormulaAppendixCAcceptanceTests.E19_OfflineLimit8Hours`, `OfflineAcceptanceTests.OfflineCapIsExactlyEightHours` |
| E-20 | `MimicId` — только представление; игровые поля из собственной записи | [Д] (57 записей с полными полями) | — (код не читает `MimicId` для игровых полей; счётчик `RewardCycle` по `Rocks.Id` в `GrantRockReward`) | `rockonboardingmine2` даёт свой цикл из 8 записей, а не цикл `rockbasic` | реализовано; тест `FormulaAppendixCAcceptanceTests.E20_MimicRockUsesOwnRewardCycle`, `RewardChestAcceptanceTests.RewardCycle_OnboardingMine2_EightEntriesWrapOverElevenRocksInDestroyOrder` |

## Загрузка JSON и типы чисел (5.1)

| Правило | Код | Статус |
|---|---|---|
| Массивы-одиночки `BalanceProperties`, `Reinforcements`, `SpawningCart`, `CheckPoint`, `Spells`, `FreeGachaCycle`, `Miners` — ровно один элемент; у `Miners` — ровно один с Id `tinygoblin` (`BalanceRepository.MinerId`), иначе `BalanceLoadException` с разделом и Id | `BalanceRepository.BuildIndexes` (`Require`) | реализовано; тест `DataValidatorFixTests.TwoMinersIsLoadErrorNamingMiners`, `DataGeometryHpAcceptanceTests.G1_MissingSectionOrIdReportsFieldPath` |
| Поля `int` (коды, счётчики, `ObjectiveCount`, `MaxDupes`, `BaseUnitCap`, `MaxFreeGachas`) целые уже при загрузке: Newtonsoft не читает дробный литерал (даже `2.0`) в `int`, загрузка падает с `BalanceLoadException` и JSON-путём поля | `BalanceRepository.Load` | реализовано; тест `DataValidatorFixTests.FractionalLiteralInIntFieldIsLoadErrorWithPath` |
| `BaseUnitCap` (лимит котиков) и `MaxFreeGachas` (запас бесплатных сундуков) — счётчики штук, а не балансовые пределы, поэтому `int` | `BalanceDto.cs` | реализовано; тест `DataValidatorFixTests.FractionalLiteralInIntFieldIsLoadErrorWithPath` (дробное значение в этих полях — ошибка загрузки с путём поля) |
| Веса `Deliveries[].Weight` и `Gacha` `UncommonWeight` / `RareWeight` / `LegendaryWeight` / `EventEpicWeight` хранятся как `double` (тип DTO не менялся); валидатор требует конечное целое ≥ 0, путь ошибки `Deliveries.<Id>.Weight` / `Gacha.<Id>.<поле>` | `BalanceValidator.RequireWholeCount` | реализовано; тест `DataValidatorFixTests.NonWholeWeightsAreValidatorErrorsAtTheirPaths` |

### Поля «в области» (3.1)

| Поле | Как используется | Проверка валидатора | Статус |
|---|---|---|---|
| `Reinforcements[0].MinerIdCycle`, `SpawningCart[0].MinerIdCycle` | Читаются валидатором. В v1 один тип котика — `BalanceRepository.MinerId` (наём, бесплатный котик и котик доставки — `tinygoblin`) | Непустой список, каждый элемент — Id из `Miners` (`…MinerIdCycle[i]`) | реализовано; тест `DataValidatorFixTests.MinerIdCycleEntriesMustBeMinersIds` |
| `BalanceProperties.CoreCurrencyId`, `SoftCurrencyId` | Журнал начислений (`gold:forge`, `elixir:rock`) и тексты событий построены на этих ID | Заданы, различны и равны `gold` / `elixir` (`BalanceValidator.CoreCurrencyIdExpected`, `SoftCurrencyIdExpected`) | реализовано; тест `DataValidatorFixTests.CurrencyIdsMustBeGoldAndElixir` |
| `Deliveries[].RewardModel.DetailedType` | Тип содержимого ящика доставки | `RewardType 4` → Id из `Miners`; `RewardType 0` → `Core` или `Soft` | реализовано; тест `DataValidatorFixTests.DeliveryRewardDetailedTypeMustMatchRewardType` |

## Числа (15.1)

`GameNumber` (`Assets/Game/Scripts/Core/Numbers/GameNumber.cs`) — сознательный гибрид, один тип для всех экономических величин:
- при `|v| < 1e300` значение хранится обычным `double`;
- бо́льшие значения — нормализованная мантисса `double` (1 ≤ |m| < 10) и показатель `long`;
- свойства `Mantissa` / `Exponent` всегда возвращают нормализованную пару, поэтому снаружи тип выглядит как «мантисса + показатель» из ТЗ 15.1.

Причины: в диапазоне `double` арифметика и `Math.Pow` точные (`2·4^5` = 2048, а не 2048.0000000001); округление F-R ниже 2^53 точное до целых, выше — тождественное (пункт 6 F-R); сохранение без потерь (строка round-trip). Покрыто тестами `BigNumberAcceptanceTests` (в том числе `BigFormInsideDoubleRangeRoundTripsExactly`, `SaveLoadOfBigValuesIsExactWithoutNaNOrInfinity`) и `FormulaAppendixCAcceptanceTests.FR_RoundingIsIdentityAbove2Pow53AndNeverMovesWholeUnits`.

Экономические результаты FT3 (секунды дохода камней, количество в доставках) считаются в `GameNumber` (`Formula.Ft3Number`, его вызывают `GameSession.GrantRockReward` и `TryCreateDelivery`); для значений меньше 1e300 результат совпадает с double-версией `Formula.Ft3` бит в бит.

## Уточнения реализации

Правила, где код уточняет ТЗ (Core/Sim):
- **G-06.** BFS клетки появления проходит через строки уже открытых ворот транзитом и никогда не выбирает их как клетку появления, хотя по 6.4 строка ворот не принадлежит ни одному сектору (`GameSession.FindSpawnCell`, `openedGateRow`).
- **G-04, кузница 2×3.** По данным кузница 2×2; в игре она расширена на свободную строку `row_c + 1` (ТЗ 6.4): опора footprint — строкой ниже токена, `RowC` — строка токена, от неё по-прежнему считаются стартовая площадка и зона мержа.
- **G-06, пушка.** Клетки `(6, row_c − 1 … row_c)` у правого края заняты пушкой (ТЗ 6.4, 7.2): в данных её нет, но `ZoneLayout` закрепляет эти клетки за кузницей (`CellOwner`), поэтому клетка появления их не выдаёт. Вместе с третьей строкой кузницы во всей открытой области стартового сектора на 4 клетки меньше; порядок первых 10 клеток (зона мержа) не меняется.
- **Офлайн и автоматизация.** Карты из сундуков, разрушенных офлайн, начисляются в момент разрушения, а проверка автоматизации шахты откладывается до первого онлайн-шага (`MineProgressState.AutomationCheckPending`): ТЗ 12.3 «ручная шахта офлайн — не больше одного буфера» приоритетнее равенства офлайна и онлайна из 15.2. Затрагивает только `citrine` в zone3 (карта `ca003` из сундука).
- **Камни исключённых наград** (`BattlePass`) определяются по данным — записи цикла с исключённым `DetailedType` (`GameSession.IsExcludedRewardRock`), а не по Id камня.
- **Разброс интервала доставки** `U[0.9; 1.1]` берётся из потока PRNG `Timers` (15.2: отдельные потоки для сундуков, доставок, критов и разброса таймеров).
- **Целые значения и веса** — см. «Загрузка JSON и типы чисел»: `int` проверяется при загрузке, веса `double` — валидатором.
- **Поля «в области»** `MinerIdCycle`, `CoreCurrencyId`, `SoftCurrencyId` читаются и проверяются валидатором; тип котика в v1 один — `BalanceRepository.MinerId`.

## Коды карточек (`StatModifierType`)

| Код | Карты | Статус v1 |
|---|---|---|
| 9 | 40 manager-карт | Применяется (E-07) |
| 10 | ca034 | Применяется (множитель дохода) |
| 7 | ca029 | Применяется (таймер бесплатного котика) |
| 1 | ca038 | Применяется (лимит котиков) |
| 4, 5, 6, 8, 11, 13, 22, 23 | см. ТЗ 10.1 | Кандидаты для `rulesVersion = 2`: уровень найма, шанс и сила крита, таймер за ворота, золото с камней, доставки, урон динамита, шанс Legendary |
| 3, 12, 15, 16, 19, 20, 21, 24, 25, 26 | — | Смысл неизвестен; не применяются |

## Открытые вопросы

Переносятся из ТЗ, Приложение D: база индекса (D-1), форма FT3 (D-2), смысл кодов ворот (D-3), награда выхода (D-4), бесплатный сундук (D-5), аргумент эликсира за порог (D-6), уровень найма (D-7), смещение котика из доставки (D-8), офлайн-лимит (D-9), неприменяемые коды карточек (D-10), правовой статус данных (D-11).
