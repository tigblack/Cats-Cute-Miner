# Ядро режима CatOutpostV2 — спецификация реализации

> Дополнение 10 октября 2026: для новых шахт правила геометрии, ворот, масок навигации/затемнения и сохранений задаёт [MINE_TOPOLOGY.md](MINE_TOPOLOGY.md). Описания барьера по строке и исходного прямоугольного поля ниже применяются к старым сохранённым шахтам без `MapTopology`.

Документ — внутренний контракт реализации ТЗ 2.1 (`Docs/TZ/Cat_Miners_Auto_Incremental_TZ_v2.1.md`, далее «ТЗ»).
ТЗ имеет приоритет; здесь зафиксированы решения, которые ТЗ оставляет исполнителю, и точные API,
на которые опираются ядро, тесты, Runtime и UI. Всё ядро — чистый C# (netstandard2.1, C# 9, без UnityEngine),
сборка `CatMiners.Core`, пространство имён `CatMiners.Core.Outpost`, каталог `Assets/Game/Scripts/Core/Outpost/`.
Комментарии в коде — по-русски, как в остальном проекте.

## 1. Файлы и ответственность

| Файл (`Core/Outpost/…`) | Тип(ы) | Ответственность |
|---|---|---|
| `Math/Rational.cs` | `Rational` (readonly struct), `ExactMath` | Точная дробь на `BigInteger`, `Pow`, `Ceil/Floor/RoundHalfUp`, парсинг `"6/5"`, `"1.18"` |
| `Math/Money.cs` | `MoneyFormat` | Короткое отображение `BigInteger` (`1.23K`, `4.5M`, … через суффиксы `NumberFormat`), строка сохранения — десятичная |
| `Config/OutpostBalanceConfig.cs` | `OutpostBalanceConfig`, `UpgradeBranchConfig`, `TargetTypeConfig` | Все коэффициенты §9, §12; парсинг JSON, канонический hash |
| `Config/EquipmentCatalog.cs` | `EquipmentCatalog`, `EquipmentItemDef`, `EquipSlot`, `ItemRarity` | 16 предметов; бонусы §12.3; стоимость улучшения |
| `Config/StageRewardConfig.cs` | `StageRewardConfig` | Гарантии 1–4/10/25/50, веса, детали, дубликаты; `rewardRngVersion=1`, поток 5 |
| `Config/LevelOverrides.cs` | `LevelOverrides`, `ZoneOverride`, `OverrideToken` | Именованные overrides по зоне: добавленные цели `AddTokens` (сейчас один — `zone1-full-arena-1`) |
| `Config/OutpostConfigSet.cs` | `OutpostConfigSet` | Все четыре конфигурации + их hash; `LoadDefault()` из встроенных строк, `Parse(...)` из текстов Resources |
| `Level/SourceLevelImporter.cs` | `SourceLevelImporter`, `SourceZone`, `SourceToken` | Разбор `Zones[].Grid`, footprint, проверки |
| `Level/OutpostLevelAdapter.cs` | `OutpostLevelAdapter`, `OutpostMap`, `MapTarget`, `MapGate` | Секторы, ворота, база, рабочие клетки |
| `Level/OutpostLevelBalanceAdapter.cs` | `OutpostLevelBalanceAdapter` | Точный HP, порции, `BaseCoinValueFactor` §12.4 |
| `Level/OutpostReachability.cs` | `OutpostReachability` | Статическая проверка последовательной расчистки §8.5 |
| `Sim/OutpostIds.cs` | константы | `ModeId`, версии, тики |
| `Sim/OutpostState.cs` | `OutpostGameState`, `OutpostProfile`, `OutpostRun`, `WorkerState`, `TargetState`, `CargoUnit`, `CargoBatch`, `DepotSlotState`, `QueueEntry`, `TimedAction`, … | Сериализуемое состояние (Newtonsoft) |
| `Sim/GridNavigation.cs` | `GridNavigation` | BFS, кэш по `NavVersion`, барьер ворот |
| `Sim/TeamStats.cs` | `TeamStats`, `TeamUpgradeService` | Эффективные характеристики, цены, пакетная покупка |
| `Sim/EquipmentService.cs` | `EquipmentService` | Коллекция, надеть/снять/лучшее, улучшение предмета |
| `Sim/OutpostSimulation.cs` (+ partial `.Tick.cs`, `.Tasks.cs`, `.Cargo.cs`, `.Depot.cs`, `.Commands.cs`) | `OutpostSimulation` | Детерминированное ядро: `Advance`, 7 фаз отметки, команды |
| `Sim/OutpostEvents.cs` | `OutpostEvent`, `OutpostEventType` | События для представления (не сохраняются) |
| `Sim/StageProgressionService.cs` | `StageProgressionService`, `StageReward` | Награда завершения, создание следующего run, кампания |
| `Sim/OfflineSimulator.cs` | `OfflineSimulator`, `OfflineSummary`, `OfflineJob` | Офлайн тем же `Advance`, на копии, порционно |
| `Sim/OutpostInvariants.cs` | `OutpostInvariants` | Проверка инвариантов учёта (тесты, загрузка, debug) |
| `Save/OutpostSaveService.cs` | `OutpostSaveService`, `OutpostSaveEnvelope`, `OutpostLoadResult` | Конверт, контрольная сумма, атомарная запись, выбор commit |
| `Save/OutpostJson.cs` | `OutpostJson` | Настройки Newtonsoft, конвертер `BigInteger` (десятичная строка), `Rational` (`"n/d"`) |
| `Bot/OutpostBot.cs` | `OutpostBot`, `BotPolicy`, `BotRunReport` | Политики A/B/C, прогон зоны/кампании, отчёт |

Старый `GameSession`/`ZoneLayout`/`ScenarioBot` не меняются и в новом режиме не вызываются.

## 2. Точная арифметика

- `Rational`: `BigInteger Num`, `BigInteger Den > 0`, всегда несократимая. Операции `+ - * /`, сравнение, `Pow(int)`,
  `Ceil()`, `Floor()` → `BigInteger`, `RoundHalfUp()` → `BigInteger` (половина вверх для положительных),
  `Parse("6/5" | "1.18" | "5")` — десятичная запись читается точно (`1.18 = 59/50`). `ToString()` → `"n/d"` или `"n"`.
- Деньги, цены, стоимость порций, зачисления, расходы — `BigInteger`; в JSON — десятичная строка.
- HP в долях: `long` (`HpUnits = MaxHp * 1_000_000`). Урон удара в долях: `RoundHalfUp(FinalDamage * 1_000_000)`, минимум 1.
- Длительности: `long` тики, `TicksPerSecond = 1_000_000`.
- `double` допустим только для отображения (координаты, доли полос).

## 3. Конфигурации

Хранятся как JSON в `Assets/Game/Resources/Outpost/` (`outpost_balance.json`, `equipment_catalog.json`,
`stage_rewards.json`, `level_overrides.json`, `card_rules.json`, `mine_topology.json`, `store_catalog.json`) и загружаются Runtime через `Resources.Load<TextAsset>`;
в ядре те же значения продублированы как встроенные строки `OutpostConfigSet.Default*Json` (тесты проверяют совпадение
файлов и встроенных строк). Коэффициенты — строки дробей. Hash каждой конфигурации — SHA-256 канонической
сериализации разобранного объекта (Newtonsoft, `Formatting.None`, порядок полей класса), а не байтов файла —
переводы строк не влияют.

`outpost_balance.json` (`balanceVersion = "outpost-2.1-b4"`, аудит баланса 11 октября 2026 — `Docs/Rework/BALANCE_AUDIT.md`; снимки шахт b1–b3 без `CostZoneStep` читаются с 0):

```json
{
  "Version": "outpost-2.1-b4",
  "TicksPerSecond": 1000000,
  "HitIntervalTicks": 1000000,
  "PickupTicksPerUnit": 250000,
  "OfflineCapMs": 10800000,
  "StartWorkers": 2,
  "MaxWorkersPerTarget": 4,
  "MoveStuckTicks": 3000000,
  "WatchdogTicks": 60000000,
  "ZoneScale": "6/5",
  "CostZoneStep": "1/10",
  "Branches": [
    { "Id": "Team",     "BaseCost": "50", "Growth": "19/10", "Cap": 10, "Milestones": [] },
    { "Id": "Pickaxes", "BaseCost": "20", "Growth": "59/50", "Cap": 20, "Milestones": [10, 20] },
    { "Id": "Boots",    "BaseCost": "25", "Growth": "13/10", "Cap": 15, "Milestones": [] },
    { "Id": "Bags",     "BaseCost": "25", "Growth": "33/25", "Cap": 16, "Milestones": [] },
    { "Id": "Unload",   "BaseCost": "20", "Growth": "32/25", "Cap": 20, "Milestones": [] },
    { "Id": "Value",    "BaseCost": "30", "Growth": "31/25", "Cap": 40, "Milestones": [10, 25, 40] }
  ],
  "MilestoneMultiplier": "2",
  "BaseDamage": "5", "DamageGrowth": "61/50", "TapDamageFactor": "1/4",
  "BaseMoveSpeed": "5/2", "MoveSpeedStep": "2/25",
  "BaseCapacity": 4, "CapacityStep": 1,
  "BaseUnloadRate": "2", "UnloadStep": "1/5",
  "ValueGrowth": "11/10",
  "HpBase": "20", "ZoneHpStep": "1/25", "RankSpan": "5", "AreaFactorCap": "5/2", "AreaCapFromCells": 7,
  "CargoHpDivisor": 10, "MinCargoUnits": 2, "CoinBase": "5",
  "Types": {
    "r": { "HpFactor": "1",   "ValueFactor": "1"   },
    "x": { "HpFactor": "4/5", "ValueFactor": "1/2", "FixedRank": "1/2" },
    "s": { "HpFactor": "2",   "ValueFactor": "2"   },
    "p": { "HpFactor": "5/2", "ValueFactor": "1"   }
  }
}
```

Порядок веток (индекс в массиве уровней): `Team=0, Pickaxes=1, Boots=2, Bags=3, Unload=4, Value=5` — enum `UpgradeBranch`.

`equipment_catalog.json` (`Version = "equip-1"`): слоты в порядке `pickaxe, hat, backpack, boots` (enum `EquipSlot`
0..3), редкости `common, rare, epic, legendary` (enum `ItemRarity` 0..3), предметы `"{slot}.{rarity}"` в порядке слот × редкость
(стабильный порядок каталога = порядок сравнения при равенстве). `RarityBaseBonus = ["2/25","9/50","7/20","3/5"]`,
`BonusPerLevel = "1/20"`, `RarityBaseCapacity = [1,3,6,10]`, `RarityCostMultiplier = ["1","3/2","5/2","4"]`,
`PartsCostBase = "6"`, `PartsCostGrowth = "117/100"`, `MaxItemLevel = 20`. Визуальные ID предметов — в отдельном
каталоге Runtime.

`stage_rewards.json` (`Version = "rewards-1"`): `RewardRngVersion = 1`, `RewardRngStream = 5`,
`Guaranteed = {"1":"pickaxe.common","2":"hat.common","3":"backpack.common","4":"boots.common","10":"pickaxe.rare","25":"pickaxe.epic","50":"pickaxe.legendary"}`,
`RarityThresholds = [70, 94, 99, 100]` (бросок `NextInt(100)`: `<70` common, `<94` rare, `<99` epic, иначе legendary),
`CompletionPartsBase = 10`, `CompletionPartsPerFive = 5`, `DuplicateParts = [5, 15, 40, 120]`.

## 4. Импорт и адаптация карты

### 4.1 `SourceLevelImporter`

`SourceZone Import(BalanceRepository repo, int zoneNumber)` (репозиторий уже проверил SHA-256 исходника).
`SourceZone { string ZoneId; int Number; int Width; int Depth; int DecoSeed; SourceToken[] Tokens /* по индексу, null для "." */ }`.
`SourceToken { int Index; char Kind; string TypeId; int Level; string RewardId; string CardId; int CardLevel; int Column; int Row; int[] Cells /* линейные индексы footprint */; string MimicId; }`.
Разделение: `Grid.Split(',')`, затем `Split(':')`; число полей: `r`=3, `s`=6, `x`=2, `p`/`e`=4, `c`=3; иначе
`OutpostDataException(zoneId, index, причина)`. Footprint только для `r/s/x`: размеры из `Rocks`/`MineShafts`/`Obstructions`,
`occupied = (col + dx, row - dz)` для `dx < WidthCells`, `dz < DepthCells`; выход за поле или пересечение — ошибка.
`p`, `e`, `c`: footprint пустой. Проверка: ровно один `c`, один `e`; `typeId` найден в своей таблице или
`checkpoint/exit/spawningcart`.

`Import(repo, zoneNumber, ZoneOverride zoneOverride = null)`: до разбора токены `AddTokens` (`{Column, Row, Token}`) ставятся на якоря
`"."` (иначе ошибка: якорь занят, вне поля, повтор, вид не `r/s/x`); дальше общий разбор проверяет поля, тип и footprint.
Footprint добавленной цели не пересекает исходные и другие добавленные, не закрывает клетку `e` и не лежит в строке `p`.
`SourceZone.Override` — применённый override с геометрией или null; `SourceToken.Added` — цель из override.

### 4.2 `OutpostLevelAdapter` → `OutpostMap` (неизменяемая)

- Сетка навигации: `Width × Rows`, `Rows = Depth + 3`; клетка `cell = row * Width + col`. Строки `Depth..Depth+2` — площадка базы, всегда свободны.
- `SpawnCell = IntakeCell = (3, Depth+1)`, `DepotCells = [(2, Depth+1), (4, Depth+1)]`, `ExitCell` = клетка токена `e`. `CampCells` (для декоративного сбора) — все 21 клетки площадки.
- `MapTarget { int Id /* = индекс якоря */; char Kind /* r,s,x,p */; string TypeId; string VisualId /* MimicId или TypeId */; int Column, Row; int[] Cells; int Sector; int[] WorkCells /* кандидаты, см. ниже */; int SourceLevel; }`.
  Список `Targets` отсортирован по `Id`. Ворота — тоже `MapTarget` с `Kind='p'`, `Cells` пустой.
- `MapGate { int TargetId; int Row; int Order }`: ворота отсортированы по `Row` убыв., затем по `Id`; `Order` 0 — нижние.
  Повтор строки ворот — ошибка данных.
- Сектор `r/s/x`: `count(gate.Row > maxOccupiedRow)` (строка ворот принадлежит нижнему сектору). Сектор ворот `k` = `Order`.
- Кандидаты рабочих клеток: для `r/s/x` — клетки сетки (включая площадку), ортогонально соседние с footprint и не входящие в него,
  в порядке возрастания индекса. Для ворот — все клетки `(col, gate.Row)`, `col = 0..Width-1` (у `p` нет footprint; боковые
  `wall1x1vL/R` на строке ворот относятся к нижнему сектору и к моменту доступности ворот уже разрушены).
  Свободность, достижимость и барьер проверяются в рантайме.
- `RequiredCount` = число `r/s/x` + число ворот (для прогресса); `e`, `c` не входят.
- `Build(repo, zone, LevelOverrides)` — новая шахта с текущим override зоны; `BuildWithOverride(repo, zone, ZoneOverride)`;
  `ForRun(repo, run)` — карта сохранённой шахты по копии `run.MapOverride`. `Build(SourceZone, overrides)` отвергает импорт без
  override, если в `overrides` для зоны есть геометрия. `OutpostMap.Override`/`OverrideName`, `MapTarget.Added`.

### 4.3 `OutpostLevelBalanceAdapter`

`TargetBalance Compute(OutpostMap map, OutpostBalanceConfig cfg, BalanceRepository repo)` для каждой цели:
`MaxHp` (long), `TotalCargoUnits` (int), `ValueFactor` (Rational), `Rank01` (Rational). Алгоритм — ТЗ §12.4 буквально:
исходный HP `MiningTargetHealths[typeId][level-1]` (double; >0, уровень в диапазоне, иначе `OutpostDataException`);
уникальные отсортированные значения по `r/s/p` зоны; `rank01 = j/(u-1)` или 0; `x` → `1/2`.
`a/b = 20 * (1 + (z-1)/200) * (1 + 5*rank01) * HpFactor` (точная дробь), площадь `n` = число клеток footprint (`p` → 1):
`n >= 7` → `ceil(5a/(2b))`; иначе минимальный `h`, где `h*h*b*b >= a*a*n` (целочисленный `ISqrt` + поправка).
`TotalCargoUnits = max(2, ceil(MaxHp/10))`. Контроль: `zone13` индекс 78 → 318 HP.

### 4.4 `OutpostReachability`

`ReachabilityReport Check(OutpostMap map)` — копия алгоритма `Docs/TZ/analysis/outpost_v2_audit.py` (BFS от `(3, Depth+1)`,
барьер `row < gateRow`, волны удаления, проверка рабочей клетки ворот и выхода). Возвращает `Passed`, `Cleared`, `GatesOpened`, `Waves`, диагностику.

## 5. Состояние (сериализуемое)

Все классы — `public sealed class` с публичными полями (как старый `GameState`), сериализуются Newtonsoft с
`OutpostJson.Settings` (без `TypeNameHandling`, `BigInteger` → строка, `Rational` → `"n/d"`).

```text
OutpostGameState { OutpostProfile Profile; OutpostRun Run; }

OutpostProfile {
  string ModeId = "CatOutpostV2"; int SchemaVersion = 1;
  ulong Seed; Pcg32 RewardRng; int RewardRngVersion = 1;
  int CurrentZone = 1;              // зона текущего run
  bool CampaignComplete;
  List<int> CompletedZones;          // по возрастанию
  List<RewardRecord> RewardLog;      // RewardRecord { string Key /* "CatOutpostV2:zoneN:first" */; int Zone; string RunId; string ItemId; bool Duplicate; int DuplicateParts; int CompletionParts; bool Guaranteed; }
  Dictionary<string,int> Items;      // itemId → уровень 1..20 (SortedDictionary при сериализации — порядок ключей стабилен)
  string[] Equipped = new string[4]; // по EquipSlot, null — пусто
  long Parts;
  int CardsVersion;                 // 0 — нужна однократная миграция, 1 — выполнена
  Dictionary<string,CardProgress> Cards; // CardProgress { int Level; int Copies; }
  long Elixir;
  int HighestZone;
  Dictionary<string,int> RockCycles;
  Pcg32 CardRng;
  List<string> CardKeys;             // однократные сундуки/учебная карточка/импорт
  long NextRunSeq = 1;
  long PlayTimeMs;                  // реальное время активной игры; сохраняется при начале заново, старые профили — 0
  TutorialState Tutorial;            // { int Step; bool Dismissed; List<string> Seen; }
  OutpostSettings Settings;          // { float Master, Music, Sfx, Ambience; bool Vibration; bool ReduceEffects; bool LegacySettingsImported; }
}

OutpostRun {
  string RunId;                      // "run-{NextRunSeq}"
  int Zone; string ZoneId; string SourceSha256; string RulesVersion; string AdapterVersion = "adapter-1";
  ZoneOverride MapOverride;          // копия override карты (null — исходная; так же читаются run до появления override)
  OutpostBalanceConfig Balance;      // разрешённый снимок параметров (целиком)
  RunPhase Phase;                    // Running, DrainingCargo, ReadyToContinue
  long Now;                          // логическое время, тики
  long LastProcessedUtcMs;
  BigInteger Wallet, TotalCredited, TotalSpent;
  int[] Levels = new int[6];
  int ExtraWorkers;                  // стартовые котики карточек, фиксируются при создании run
  List<TargetState> Targets;         // по Id
  int GatesOpened;                   // = число открытых ворот (открываются строго по Order)
  List<WorkerState> Workers;         // по Id
  List<CargoBatch> Batches;          // по Seq
  DepotSlotState[] Slots = new DepotSlotState[2];
  List<QueueEntry> Queue;            // FIFO (ArrivalTick, WorkerId)
  long NextBatchSeq = 1, NextTaskId = 1; int NextWorkerId = 1;
  int NavVersion;
  RunCounters Counters;              // DeliveredUnits, DeliveredValue(=TotalCredited), ReleasedUnits, ClearedTargets, FirstDeliveryTick(-1), FirstPurchaseTick(-1), ClearedTick(-1), CompletedTick(-1), MaxQueue, QueueWaitTicks, ChestsOpened, CardsGained, ElixirGained
  bool PlanningDirty;
  long LastChangeTick; bool WatchdogFired; string WatchdogDiagnostic;
  string CompletionRewardKey;        // ключ выданной награды (ReadyToContinue)
}

TargetState { int Id; char Kind; long MaxHpUnits; long RemainingHpUnits; int TotalUnits; int ReleasedUnits; string ValueFactor /* Rational */; bool Destroyed; }

WorkerState {
  int Id; int Look;                  // Look — индекс окраса (декор, из Id)
  WorkerMode Mode;                   // ChoosingTask, MovingToWork, Mining, MovingToCargo, PickingUp, Returning, WaitingDepot, Unloading, Idle, GoingHome
  long TaskId;
  int Cell;                          // текущая логическая клетка (при движении — клетка начала текущего ребра)
  int[] Path; int PathIndex;         // Path — клетки после стартовой; PathIndex — индекс следующей клетки (ребро Cell → Path[PathIndex])
  TimedAction Action;                // null, если нет таймера
  int TargetId = -1; int WorkCell = -1;
  long CargoBatchSeq = -1;           // партия резерва подбора
  int DepotSlot = -1;                // зарезервированное место (подзадача Returning → место)
  bool TaskInvalid;                  // цель уничтожена другими во время пути к ней
  List<CargoUnit> Bag;               // по ключу (Source, Ordinal)
}

TimedAction { ActionKind Kind /* Edge, Hit, Pickup, Unload */; long SegmentStart; string P0 /* Rational, прогресс на SegmentStart */; long Duration; long Deadline; }

CargoUnit { int Source /* Id цели */; int Ordinal; BigInteger Value; int ReservedBy = 0 /* WorkerId, 0 — свободна; только на земле */; long ReservedTask; }
CargoBatch { long Seq; string Key /* "b{Seq}" */; int Cell; long CreatedTick; List<CargoUnit> Units; }
DepotSlotState { int WorkerId /* 0 — свободно */; long TaskId; }
QueueEntry { int WorkerId; long ArrivalTick; }
```

Идентичность порции: `(RunId, Source, Ordinal)`; строковый ключ для логов `"{RunId}:{Source}:{Ordinal}"`.
Порядок ключей — по `Source`, затем `Ordinal`. WorkerId начинаются с 1.

## 6. Эффективные характеристики (`TeamStats`)

`TeamStats Compute(OutpostBalanceConfig b, int[] levels, OutpostProfile p, EquipmentCatalog cat, CardCatalog cards = null, int extraWorkers = 0)`.
Базовые формулы ниже даны без карточек; их дополнительные множители, целочисленные бонусы
и расчёты для конкретной цели описаны в §8.1.

- `Workers = StartWorkers + L[Team]` (с карточками: `+ Run.ExtraWorkers`, §8.1).
- `Milestone(branch, L) = 2^count(L >= t for t in Milestones)`.
- `LocalDamage = BaseDamage * DamageGrowth^L * Milestone(Pickaxes)`; `FinalDamage = LocalDamage * (1 + PickBonus)`; `DamageUnits = max(1, RoundHalfUp(FinalDamage * 10^6))`.
- Удар игрока: `TapDamageUnits = max(1, RoundHalfUp(FinalDamage * TapDamageFactor * (1 + CardTapDamage) * 10^6))`, 0 при `TapDamageFactor = 0` (профиль b2: 1/4). Удар не использует специалистов, бонусы по виду цели или крит котиков.
- `LocalMoveSpeed = BaseMoveSpeed * (1 + MoveSpeedStep * L)`; `FinalMoveSpeed = Local * (1 + BootsBonus)`; `EdgeTicks = ceil(TicksPerSecond / FinalMoveSpeed)`.
- `Capacity = BaseCapacity + CapacityStep * L + BackpackExtra`.
- `UnloadRate = BaseUnloadRate * (1 + UnloadStep * L)`; `UnloadTicks = ceil(TicksPerSecond / UnloadRate)`.
- `LocalValue = ValueGrowth^L * Milestone(Value)`; `FinalValue = LocalValue * (1 + HatBonus)`.
- `ItemBonus = RarityBaseBonus[r] * (1 + BonusPerLevel * (q-1))`; `BackpackExtra = RarityBaseCapacity[r] + (q-1)`; пустой слот — 0.
- `CoinValue(zone, valueFactor) = max(1, ceil(CoinBase * ZoneScale^(z-1) * valueFactor * FinalValue))`.
- `NextCost(zone, branch, L) = ceil(ZoneScale^(z-1) * (1 + CostZoneStep * (z-1)) * BaseCost * Growth^L * (1 - скидка))`; на cap — нет цены.

Контроль (§12.5): zone1, L=0 кирки → цена 20, урон 5; L=1 → 6.1, цена 24; L9/L10 цена 89/105; x zone1 площадь 2 → 80 HP, 8 порций по 3.

`TeamUpgradeService.Quote(state, branch, mode)` → `UpgradeQuote { int Levels; BigInteger Cost; string BlockReason }`:
`mode ∈ {One, Ten, Max}`; покупает последовательно, пока следующий уровень ≤ cap и суммарная цена ≤ `Wallet`, до 1/10/∞ уровней.
`Team` поддерживает только `One`. `BlockReason`: `"cap"`, `"gold"` (не хватает доставленного золота), `"phase"` (`ReadyToContinue`).
Покупка — одна атомарная команда: списать сумму, увеличить уровень на `Levels`; при `Team` — создать работников.

## 7. Симуляция (`OutpostSimulation`)

```csharp
public sealed partial class OutpostSimulation {
  public OutpostSimulation(OutpostGameState state, OutpostMap map, OutpostConfigSet configs);
  public OutpostGameState State { get; }  public OutpostMap Map { get; }
  public TeamStats Stats { get; }          // кэш, пересчитывается в фазе 7
  public long Now => State.Run.Now;
  public List<OutpostEvent> Events { get; } // накопленные с последнего DrainEvents()
  public void DrainEvents(List<OutpostEvent> into);
  public long NextEventTick();             // long.MaxValue, если событий нет
  public AdvanceResult Advance(long targetTick, int maxTicksToProcess = int.MaxValue); // { Reached, ProcessedTicks, ProcessedEvents }
  public CommandResult Submit(OutpostCommand cmd);              // немедленно на отметке Now: фаза 6 + фаза 7
  public void Schedule(OutpostCommand cmd, long tick);           // тесты/бот: фаза 6 указанной отметки (tick > Now)
  public bool IsEconomyStopped => Run.Phase == RunPhase.ReadyToContinue;
}
```

`Advance(target)`: пока `t = NextEventTick() <= target` — `ProcessTick(t)`; затем `Now = target` (если не остановлено лимитом).
Состояние меняется только на отметках событий; поэтому `Advance(a+b) == Advance(a); Advance(b)` и save/load посередине
(сохраняются `SegmentStart/P0/Duration/Deadline`) дают побайтно одинаковый JSON. Если экономика остановлена
(`ReadyToContinue`) — `Advance` только двигает `Now`.

`NextEventTick` = минимум `Action.Deadline` работников и отметок запланированных команд.

### 7.1 Отметка `ProcessTick(t)` — строго семь фаз

1. **Движение.** Работники с `Action.Kind == Edge && Deadline == t` (по `Id`): `Cell = Path[PathIndex++]`. Если путь
   закончен — прибытие: `MovingToWork` → если `TaskInvalid` или сумка полна → нуждается в задаче; иначе `Mining`, `Hit`
   с `Deadline = t + HitIntervalTicks`. `MovingToCargo` → `PickingUp`, `Pickup` с `t + PickupTicksPerUnit`.
   `Returning` без места (`DepotSlot == -1`) → `WaitingDepot`, `Queue.Add(t, Id)`. `Returning` к месту → `Unloading`,
   первая порция `Unload` (`Duration = UnloadTicks`). `GoingHome` → `Idle`. Иначе (путь продолжается): если `TaskInvalid`
   у `MovingToWork` — остановиться в этой клетке и нуждаться в задаче; иначе следующее ребро `Edge` (`Duration = EdgeTicks`).
2. **Удары.** Работники `Mining` с `Hit.Deadline == t`, сгруппированные по цели; цели по возрастанию `Id`.
   Для цели: `sum = Σ DamageUnits` (текущие `Stats`); `Remaining = max(0, Remaining - sum)`;
   `expected = Remaining == 0 ? Total : floor(Total * (MaxU - Remaining) / MaxU)`; новые порции `Ordinal = Released..expected-1`,
   `Value = CoinValue(zone, target.ValueFactor)` по текущим `Stats`. Раздать в свободные места сумок ударивших (по `Id`, `free = max(0, Capacity - Bag.Count)`),
   остаток — новая партия `Seq = NextBatchSeq++` в `WorkCell` ударившего с наименьшим `Id`. Событие `CargoReleased`.
   Разрушение: `Destroyed = true`, footprint освобождён, `NavVersion++`, `TaskInvalid = true` у всех `MovingToWork` к этой цели,
   `ClearedTargets++`, событие `TargetDestroyed`. Затем у каждого ударившего: цель уничтожена или `Bag.Count >= Capacity` →
   нуждается в задаче (освобождает `WorkCell`); иначе следующий `Hit` в `t + HitIntervalTicks`.
3. **Подбор.** `PickingUp` с `Deadline == t` (по `Id`): взять из партии наименьшую по ключу порцию с `ReservedBy == Id`, в сумку
   (сохраняя порядок ключей), снять резерв. Пустая партия удаляется. Если ещё есть свой резерв и место — следующий `Pickup`;
   иначе снять остаток резерва и нуждаться в задаче. Событие `CargoPickedUp`.
4. **Склад.** Места 0, 1: работник `Unloading` с `Deadline == t`: первая порция сумки → `Wallet += Value`, `TotalCredited += Value`,
   `DeliveredUnits++`, событие `CargoDeposited(workerId, slot, value)`. Если сумка не пуста — следующая порция; иначе место освобождается,
   работник нуждается в задаче.
5. **Ворота и завершение.** Если цель ворот `Order == GatesOpened` уничтожена — `GatesOpened++`, `NavVersion++`, событие `SectorOpened`.
   `Running` и все `r/s/x` уничтожены и `GatesOpened == Gates.Count` → `DrainingCargo` (событие). `DrainingCargo` и нет партий,
   все сумки пусты, нет `Unloading` → награда (`StageProgressionService.ApplyCompletion`, один раз по ключу), `ReadyToContinue`,
   все таймеры работников сброшены (`Action = null`), очередь/места пусты, событие `StageCompleted`.
6. **Команды**, запланированные на `t`, в порядке поступления.
7. **Планирование.** (a) пересчитать `Stats`; у работников с `Edge` или `Unload`, если `Duration` изменилась:
   `p = P0 + (t - SegmentStart)/Duration` (точно, `min 1`); `p < 1` → `P0 = p`, `SegmentStart = t`, `Duration = new`,
   `Deadline = t + ceil((1-p) * new)`; `p == 1` — без изменений. (b) Назначить задачи нуждающимся (и `Idle`, если
   `PlanningDirty`) по `Id` — §7.2. (c) Свободные места склада (0, затем 1) отдать очереди FIFO по `(ArrivalTick, WorkerId)`:
   работник выходит из очереди, `DepotSlot = s`, `Mode = Returning` (подзадача, новый `TaskId`), путь на 1 ребро до клетки места.
   `PlanningDirty = false`.

`Submit(cmd)` на `Now`: фаза 6 (одна команда) + фаза 7. Отказ команды не меняет состояние.

### 7.2 Выбор задачи (работник в клетке `c`, по возрастанию `Id`)

1. `Bag.Count >= Capacity` → `Returning` к `IntakeCell` (если `c == IntakeCell` — сразу `WaitingDepot`, очередь с `ArrivalTick = t`).
2. Есть партия со свободными порциями: сортировка `(dist(c, batch.Cell), Seq, Key, Cell)`; резерв `k = min(Capacity - Bag.Count, свободных)`
   наименьших по ключу порций (`ReservedBy = Id`, `ReservedTask = TaskId`); `MovingToCargo` (или сразу `PickingUp`, если `dist == 0`).
3. Только `Running`: доступные цели — живые `r/s/x` сектора `GatesOpened`; если их нет — ворота `Order == GatesOpened`, если живы.
   Пары (цель, рабочая клетка): клетка свободна, не за барьером, достижима, не занята другим добывающим (`MovingToWork/Mining`),
   у цели меньше `MaxWorkersPerTarget` назначенных. Сортировка `(dist, assigned(target), target.Id, cell)`. `MovingToWork` (или сразу
   `Mining` с первым ударом через полный интервал, если `dist == 0`).
4. `Bag.Count > 0` → `Returning`.
5. Иначе: `Running` → `Idle`; `DrainingCargo`/`ReadyToContinue` → `GoingHome` к ближайшей клетке площадки (`Idle` по прибытии).

Каждая новая задача получает `TaskId = NextTaskId++`. «Нуждается в задаче» = `Mode = ChoosingTask`, `Action = null`,
резервы сняты; после фазы 7 ни один работник не остаётся в `ChoosingTask`.

### 7.3 Навигация

BFS по 4 направлениям, порядок соседей: вверх (`row-1`), влево, вправо, вниз. Проходимы клетки сетки, не занятые живым footprint,
с `row >= BarrierRow`, где `BarrierRow = Gates[GatesOpened].Row`, если остались закрытые ворота, иначе 0.
Путь — по родительским ссылкам BFS от стартовой клетки; сохраняется в `WorkerState.Path`. Кэш BFS по `(source, NavVersion)`.
Открытие прохода не пересоздаёт уже выбранный путь.

### 7.4 Watchdog

`LastChangeTick` обновляется при изменении HP, груза, клетки движущегося, разгрузке, появлении задач. Если не `ReadyToContinue`
и `t - LastChangeTick > WatchdogTicks`, либо событий нет (`NextEventTick == MaxValue`) при незавершённой карте — один раз
`WatchdogFired = true`, `WatchdogDiagnostic` (runId, зона, живые цели сектора, резервы, барьер, блокирующие клетки, режимы работников).
Ничего не начисляется и не разрушается.

## 8. Команды

`OutpostCommand { CommandKind Kind; UpgradeBranch Branch; BuyMode Mode; string ItemId; EquipSlot Slot; int TargetId; }`:
`BuyUpgrade(branch, mode)`, `Equip(itemId)`, `Unequip(slot)`, `EquipBest`, `UpgradeItem(itemId)`, `Strike(targetId)`,
`UpgradeCard(cardId)` (§8.1), `StoreBuy(itemId, utcMs)` и `BonusGold(amount)` (§8.2).
`CommandResult { bool Ok; string Reason; int Levels; BigInteger Cost; }`. В `ReadyToContinue` покупки отклоняются (`"phase"`),
экипировка разрешена (действует на следующую шахту). После команды фаза 7 пересчитывает `Stats` (смена обуви/разгрузки — пересчёт
остатка ребра/обслуживания; новый урон — со следующего удара; вместимость не удаляет груз). Найм создаёт работника в `SpawnCell`
(`Mode = ChoosingTask`).
Переход и офлайн — не команды симуляции (выполняются `StageProgressionService`/`OfflineSimulator` над копией состояния).

`Strike(targetId)` — удар кирки игрока (ТЗ 6.6, `OutpostSimulation.Strike.cs`). `StrikeBlockReason(id)`/`CanStrike(id)`: `"phase"`
(не Running), `"disabled"` (`TapDamageFactor = 0`), `"unknown"`, `"destroyed"`, `"locked"` (сектор выше `GatesOpened` или ворота
при живых `r/s/x` их сектора). Урон `Stats.TapDamageUnits` (если на отметке уже была покупка, характеристики считаются заново без
побочных эффектов); событие `TargetStruck` (Value — урон). Порции — по формуле §7.1 одной партией на земле (`CargoReleased`,
WorkerId −1) в `StrikeDropCell`: BFS по слоям от footprint (ворота — от строки) через любые клетки не выше барьера, в первом слое
с проходимыми клетками — достижимая от `IntakeCell` с наименьшим путём, затем наименьший индекс, иначе замкнутый карман с наименьшим
индексом. Разрушение — `DestroyTarget` до выпуска (груз в освобождённый footprint), затем сразу проверки фазы 5. Результат:
`Levels` — выпущено порций, `Cost` — урон в долях HP. Ограничение пользовательского ввода — в контроллере: один удар за 0,4 с реального времени, общий интервал для всех целей и устройств, без очереди лишних тапов и наложения замахов. Детерминированная команда ядра получает только принятый удар в кадр контакта.

## 8.1 Карточки (`Core/Outpost/Cards`, CARD_EFFECT_MAPPING.md)

- `CardCatalog.Build(repo, CardRules)` — 61 `CardDef` из `Cards`/`CardUpgradeCosts` (исходные поля без изменений) + эффект
  `CardEffectRule` (`card_rules.json`: по ID или `manager-<редкость>`), `ChestDef` из `Gacha`, `RockRewardDef` из
  `Rocks[].RewardCycle`, `ZoneChestScale` из `RankMultipliers`; дроби источника — точные (`Exact`). Подключается
  `OutpostConfigSet.WithCards(repo)`; `configs.Cards == null` — режим без карточек (тесты ядра).
- Профиль: `Cards[id] = {Level, Copies}`, `Elixir`, `HighestZone`, `RockCycles`, `CardRng` (Pcg32, поток 6), `CardKeys`,
  `CardsVersion`. `CardService`: `Grant` (первая копия → уровень 1), `Quote/Upgrade` (`Duplicates[L−1]`, `SoftCurrency[L−1]`,
  максимум 1 + длина), `StateOf` (Locked/Available/Collecting/Active/Upgradable/Max), `IsUnlocked` (`HighestZone ≥ MineUnlock`),
  `ReachZone`, `EffectSum/EffectWhole`, `MigrateProfile`, `ImportLegacy`.
- Эффекты — `TeamStats.Compute(..., cards, extraWorkers)`: общие бонусы входят в `FinalDamage/FinalValue/FinalMoveSpeed/
  UnloadRate/Capacity/PickupTicks/TapDamageUnits`; по цели — `DamageUnitsFor(kind, typeId)`, `CoinValueFor(kind, typeId, vf)`
  (кэш `m_TargetDamage/m_TargetCoin` по индексу цели, обновляется при пересчёте Stats); крит — `CritChancePpm`, `CritMultiplier`,
  бросок `CritRoll(workerId, tick)` (SplitMix64 от seed профиля и RunId), `TargetHit.Extra` — число критов. Скидки и предел
  «Команды» — `TeamUpgradeService.Quote(run, branch, mode, stats)`; стартовые котики — `Run.ExtraWorkers` в `CreateRun`, в счёт
  предела «Команды» (`Quote` уменьшает покупаемые уровни на `ExtraWorkers`, общий предел — `Cap + TeamCap`);
  предел офлайна — общая политика `OfflineJob`, независимо от карточек; детали — `ApplyCompletion`.
- Награды: `DestroyTarget` → `GrantTargetReward` (камень — `RewardCycle[counter % len]`: сундук или эликсир; `s/p` — сундук
  `rewardId`), `Complete` → сундук `ExitRewardId`, `DepositOne` → учебная карточка (`CardService.TutorialKey`). `CardChestService.Open`
  — гарантированные карты, эликсир × множитель зоны, случайные по шансам `ChestChances` из открытого пула. События `ChestOpened`
  (`Detail` "ca002:2,…", `Extra` — новых карточек), `ElixirFound`, `CardUpgraded`; счётчики run и офлайн-итога.
- Команда `UpgradeCard(cardId)`: отказы "unknown", "not-owned", "max", "copies", "elixir"; разрешена в любой фазе.
- Проверки: `OutpostInvariants.Check(state, map, equipment, cards)` — уровни/копии/эликсир, `HighestZone ≥ CurrentZone`,
  работники = StartWorkers + ExtraWorkers + Team, предел «Команды» с бонусом карточек.

## 8.2 Магазин (`Core/Outpost/Store`, ТЗ §10.5)

- `StoreCatalog` (`store_catalog.json`, `Version = "store-3"`): `TestPurchases`, `StartGems`, `StarterConsumables`, `Sections`,
  `Items[] = StoreItemDef { Id, Section, Kind (Chest|TimeSkip|Elixir|Gems), ChestId, Minutes, Amount, BonusAmount, BonusPercent,
  Currency (Gems|Ad|Real), Price, PriceUsd, CooldownMinutes }`. `Validate` — по видам и валютам; `ValidateChests(cards)` — сундук есть
  в `Gacha` (вызывает `WithCards`). `OutpostConfigSet.Store`, `StoreHash` — отдельно от `CatalogHash` (цены не влияют на совместимость).
- Профиль: `Gems`, `Consumables[id]`, `StoreCooldownUntilUtcMs[id]`, `StoreBonusUsed`, `StoreVersion`. `StoreService.MigrateProfile`
  (0 → 1) один раз выдаёт `StartGems` и `StarterConsumables` (3 × `skip_1h`, 1 × `skip_4h`); вызывает контроллер (загрузка, новая
  игра, сброс), не `NewProfile` — фикстуры ядра без магазина не меняются.
- `StoreService.Quote(p, store, def, nowUtcMs)` → `StoreQuote { Free, Owned, CooldownLeftMs, Amount, BonusAvailable, BlockReason }`:
  `Free` — реклама готова или расходник в наличии; `BlockReason` — `gems`, `cooldown`, `unavailable` (реклама/деньги при
  `TestPurchases = false`). `Pay` — списание гемов, таймер рекламы (`now + CooldownMinutes`), деньги — только в тестовом режиме;
  `GrantAmount` — эликсир или гемы (`BonusAmount` один раз, затем `StoreBonusUsed`; в `store-3` бонусов нет — скидка на гемы убрана 14 октября 2026); `PayTimeSkip` — сначала расходник, затем гемы.
- Команда `StoreBuy(itemId, utcMs)`: сундук — `OpenChest(chestId, OutpostIds.StoreSource = −2, −1, t)` (тот же `CardChestService` и
  `CardRng`; счётчики run не меняются — это не находка шахты), эликсир/гемы — в профиль; событие `StorePurchased`. Отказы: `unknown`
  (нет плитки, тайм-скип, нет сундука), `gems`, `cooldown`, `unavailable`; отказ ничего не меняет. Разрешена в любой фазе.
- Тайм-скип — не команда: контроллер строит `OfflineSimulator.BeginSkip(state, def, utcMs)` (§11) — ровно `def.Minutes` минут работы
  шахты (10/30/60 с `store-2`; ID плиток `skip_1h/4h/12h` прежние — ключи расходников в сохранениях) — и платит `PayTimeSkip` на копии.
  Итог: `OfflineSummary.SkipMinutes`, `SkipItemId`.
- Команда `BonusGold(amount)` — золото за рекламу (удвоение офлайна): `Wallet += amount`, `BonusCredited += amount`
  (`TotalCredited` — по-прежнему стоимость доставленного), событие `BonusGold`; `amount <= 0` — `unknown`.

## 9. Экипировка (`EquipmentService`)

- `Grant(profile, itemId)` → `(bool duplicate, int parts)`: новый — `Items[id] = 1`; дубликат — `Parts += DuplicateParts[rarity]`.
- `Equip/Unequip/EquipBest`: бесплатно. `EquipBest` по слотам: максимальный итоговый бонус (рюкзак — `BackpackExtra`, остальные — `ItemBonus`);
  при равенстве — оставить надетый, иначе первый по порядку каталога.
- `UpgradeCost(itemId, q) = ceil(PartsCostBase * PartsCostGrowth^(q-1) * RarityCostMultiplier)`; `q == 20` — нельзя (`"max"`); нет деталей — `"parts"`.
- Контроль: полная стоимость 1→20: 673 / 1001 / 1665 / 2655; легендарный 20 → +117%.

## 10. Завершение и переход (`StageProgressionService`)

- `ApplyCompletion(state, configs)`: ключ `"CatOutpostV2:zone{z}:first"`. Если ключ уже в `RewardLog` — ничего не выдавать (повтор).
  Иначе: предмет по гарантии (поток не продвигается) или `slot = NextInt(4)`, `roll = NextInt(100)`; `Grant`; детали
  `CompletionParts(z) = 10 + 5*floor(z/5)`; запись `RewardRecord`; `CompletedZones` += z; при `z == 144` `CampaignComplete = true`.
  `Run.CompletionRewardKey = key`.
- `CreateRun(profile, zone, map, configs, repo, nowUtcMs)` → новый `OutpostRun` (`RunId = "run-{NextRunSeq++}"`, уровни 0, два работника в `SpawnCell`, `Wallet = 0`,
  HP/порции из адаптера, `Balance` = текущая конфигурация, `MapOverride` = копия `map.Override`, `LastProcessedUtcMs = nowUtcMs`),
  затем фаза 7 на `Now = 0`. Конструктор `OutpostSimulation` отвергает карту с другим override, чем `run.MapOverride`.
- `IsRunMapCurrent(run, configs)` — копия override в run совпадает (по канонической сериализации) с override зоны в конфигурации.
  `RestartRunIfMapChanged(state, repo, configs, nowUtcMs, out map)`: незавершённая шахта со старой геометрией заменяется новым run
  той же зоны на актуальной карте (профиль сохраняется, `NextRunSeq++`); `ReadyToContinue` не перезапускается. Контроллер вызывает
  это после загрузки, сразу записывает снимок и показывает тост.
- `PrepareTransition(state)` → новый `OutpostGameState` (глубокая копия профиля, `CurrentZone = z+1`, новый run) — только при
  `ReadyToContinue` и `z < 144`. Контроллер сохраняет снимок целиком и только после успешной записи переключает сцену.

## 11. Офлайн (`OfflineSimulator`)

`OfflineJob Begin(OutpostGameState committed, long nowUtcMs)`: копия (сериализация/десериализация),
`elapsed = max(0, now - LastProcessedUtcMs)` с защитой от переполнения;
`cap = min(10_800_000, max(0, configs.Balance.OfflineCapMs))` — актуальная политика, не снимок `run.Balance`;
`applied = min(elapsed, cap)`, `target = Now + applied * (TicksPerSecond / 1000)`.
Предел — три реальных часа за одно отсутствие, в том числе для прежних run с 8 часами. Карточки и ускорение debug не продлевают интервал. «Лежанка» (`ca051`, `cards-2`) даёт +5% скорости разгрузки за уровень. Отброшенный остаток отсутствия не переносится на следующий запуск; экономика сохранённого run не переписывается.
`bool Step(int maxTicks)` — порционный `Advance(target, maxTicks)`. По окончании:
`LastProcessedUtcMs = max(prev, now)`, `OfflineSummary { int SkipMinutes; string SkipItemId; long ElapsedMs, AppliedMs; BigInteger GoldDelivered; int UnitsDelivered; int TargetsCleared; int SectorsOpened; bool StageCompleted; int ChestsOpened, CardsGained; long ElixirGained; }`.
Результат подменяет состояние только после успешного commit.

`OfflineJob BeginSkip(OutpostGameState committed, StoreItemDef skip, long nowUtcMs)` — тайм-скип магазина (§8.2): та же копия и тот же
порционный расчёт, но `elapsed = applied = skip.Minutes × 60 000` (лимит офлайна не действует), `Summary.SkipMinutes = skip.Minutes`,
`Summary.SkipItemId = skip.Id`.
`LastProcessedUtcMs = max(prev, now)` — реальное время не сдвигается. Оплату (`StoreService.PayTimeSkip`) вызывающий выполняет на
`job.State.Profile` до расчёта: списание и результат записываются одним снимком. Завершённая шахта останавливается на `ReadyToContinue`.

## 12. Сохранение (`OutpostSaveService`)

Каталоги `{persistent}/CatOutpostV2/main/` и `{persistent}/CatOutpostV2/debug/`; файлы `state.json`, `state.json.tmp`, `state.json.bak`.
Конверт:

```json
{ "ModeId": "CatOutpostV2", "SchemaVersion": 1, "RulesVersion": "outpost-2.1-r1", "BalanceVersion": "outpost-2.1-b4",
  "SourceSha256": "8975…", "CatalogHash": "<sha256 equipment+rewards+card_rules>", "CommitSequence": 17,
  "PayloadSha256": "<sha256 строки Payload>", "Payload": "<JSON OutpostGameState одной строкой>" }
```

Запись: сериализовать, разобрать обратно и проверить инварианты (§13), записать tmp с flush/`Flush(true)`, затем
`File.Replace(tmp, main, bak)` (если `main` существует) или `File.Move(tmp, main)`. Если `Replace` не поддерживается —
`main → bak` (старый bak удалить), `tmp → main`. Загрузка: прочитать `main` и `bak`, отбросить невалидные (разбор, ModeId, SchemaVersion,
PayloadSha256, инварианты); выбрать наибольший `CommitSequence`; tmp не считается commit. Будущая/несовместимая схема или повреждение обоих —
`OutpostLoadStatus.Incompatible/Corrupt`, файлы копируются в `quarantine-{utc}`, ничего не перезаписывается. Через `IFileSystem` (тест-инъекция
сбоя на каждом шаге). Старые `save_main.json`/`save_debug.json` не трогаются, кроме однократной архивной копии в `CatOutpostV2/legacy-archive/`.

Явный сброс из настроек (Reset Progress, все сборки, включая WebGL Release) или меню «DEV 4» архивирует прежние main/bak/tmp выбранного профиля в `restart-*`
и создаёт начальный профиль/шахту 1. `Save(..., resetBackup: true)` после commit main записывает тот же новый конверт в bak с
`Flush(true)`; успех объявляется после обеих записей. При ошибке `reset-backup` контроллер удерживает прежнюю игру и повторяет
тот же подготовленный снимок. После завершённого сброса восстановление из bak возвращает новую игру, а другой слот не изменяется.
`PlayTimeMs >= 0` сохраняется вместе с настройками; изменение не требует повышения схемы: отсутствующее поле старого профиля равно 0.

## 13. Инварианты (`OutpostInvariants.Check(state, map) → List<string>`)

- Для каждой цели: `0 <= Released <= Total`; `Total == (Total - Released) + наземные + в сумках + сданные` этой цели (сданные = `Released - наземные - в сумках`, должно быть ≥ 0).
- Ключи порций уникальны среди земли и сумок; резерв: `ReservedBy` указывает на работника в `MovingToCargo/PickingUp` с той же `CargoBatchSeq`; число резервов работника ≤ свободного места на момент назначения (не проверяется после смены рюкзака).
- Одна рабочая клетка — не более одного `MovingToWork/Mining`; не более `MaxWorkersPerTarget` на цель.
- Места склада и `DepotSlot` взаимно согласованы; работник не в двух местах; очередь без дублей; `WaitingDepot` ⇔ в очереди.
- `Wallet == TotalCredited + BonusCredited - TotalSpent >= 0`; `TotalCredited == Σ Value` сданных (счётчик `DeliveredValue`); `BonusCredited >= 0` — золото за рекламу (§8.2), мимо учёта доставки.
- Магазин: `Gems >= 0`, счётчики `Consumables >= 0`, таймеры `StoreCooldownUntilUtcMs >= 0`.
- Уровни в `[0, cap]`, cap «Команды» — с учётом `CardTeamCap` и уже выданных `ExtraWorkers`; число работников `== StartWorkers + ExtraWorkers + Levels[Team]`; цены/детали/эликсир неотрицательны; `Items` уровни в `[1, 20]`.
- Карточки имеют известные ID, допустимый уровень по `CardUpgradeCosts` и неотрицательные копии; `CardKeys` уникальны, циклы наград допустимы. Старый профиль с `CardsVersion = 0` проверяется по правилам миграции.
- `ReadyToContinue` ⇒ нет партий, сумки пусты, очередь и места пусты, `Action == null` у всех.

## 14. События для представления

`OutpostEvent { OutpostEventType Type; long Tick; int WorkerId; int TargetId; int Cell; int Count; long BatchSeq; int Slot; BigInteger Value; int Branch; string ItemId; WorkerMode Mode; int Extra; string Detail; }`;
типы: `WorkerModeChanged`, `TargetHit`, `CargoReleased`, `CargoPickedUp`, `CargoDeposited`, `TargetDestroyed`, `SectorOpened`,
`DrainingStarted`, `StageCompleted`, `TeamUpgradePurchased`, `WorkerHired`, `EquipmentChanged`, `ItemUpgraded`, `WatchdogFired`,
`TargetStruck` (удар игрока), `ChestOpened`, `ElixirFound`, `CardUpgraded` (§8.1), `StorePurchased` (`ItemId` — плитка, `Value` — выданное количество эликсира/гемов, у сундука 0), `BonusGold` (`Value` — золото за рекламу).
Представление не меняет состояние; начислением управляет только фаза 4.

## 15. Бот и прогоны (`OutpostBot`)

Политики: `A` — без покупок; `B` — `Pickaxes ×1` при доступности до cap; `C` — круговой список `Pickaxes, Team, Bags, Boots, Unload, Value`
с переходом к следующей ветке после покупки, пропуск недоступных/cap, `×1`. Бот проверяет политику на каждой отметке события (после `Advance` до
следующего события) и подаёт команду через `Submit`. Лимит: 24 ч симуляции (`86_400_000_000` тиков) и 2 000 000 событий; timeout/watchdog — провал.
`BotRunReport { Zone; Policy; EquipmentLabel; Seed; Outcome; FirstDeliverySec; FirstPurchaseSec (null если нет); ClearedSec; CompletedSec; Events;
UnitsCreated; ValueCreated; Credited; Spent; MaxQueue; AvgQueueWaitSec; RewardItemId; RewardParts; WatchdogDiagnostic; FinalLevels }`.
