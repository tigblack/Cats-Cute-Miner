# Карточки Cat Miners: соответствие эффектов

Возвращение 61 карточки исходного `json-data.json` в автоматический режим CatOutpostV2 (задание «Cat Miners возвращение
карточек», 9 октября 2026). Исходные поля карточек — `Id`, `Rarity`, `MineUnlock`, `SortingWeight`, `TargetIds`,
`IsManager`, `StatModifierType`, параметры модификатора — и `CardUpgradeCosts` читаются из источника без изменений
(`CardCatalog.Build`). Новые числа эффектов и сундуков — отдельно, в `Assets/Game/Resources/Outpost/card_rules.json`
(актуальная версия `cards-3`). Имена и описания — `Loc.Cards.cs`, арт — `card_art_<id>` (`Tools/PixelArt/sprites/card_art.py`).

**`cards-3` (ТЗ 2.2 «заводы вместо месторождений», §7.1, 15 октября 2026).** Месторождения `s` стали заводами товаров, поэтому
39 manager-карточек с `TargetIds` типа завода стали карточками заводов: 20 Common «… Foreman» и 19 Uncommon «… Manager» (у
`sunstone` Uncommon нет), имена — приложение A ТЗ 2.2. Записи `manager-1` и `manager-2` получили одинаковый эффект
`FactoryOutput` (`Base 1/10`, `PerLevel 1/20`, `Rule CatMiners`): +выпуск товара за цикл заводов своего типа,
`Output(L) = 1/10 + 1/20·(L − 1)` — уровень 1 даёт +10 %, Common 14 — +75 %, Uncommon 12 — +65 %. Бонусы Common и Uncommon
типа складываются: `Units = floor(UnitsPerCycle × (1 + Output(C) + Output(U)))`, при `UnitsPerCycle = 20` — 22 (Common 1),
26 (Common 3 + Uncommon 1), 48 (обе на максимуме, ×2,4); каждое улучшение даёт хотя бы +1 единицу. Правило — одна функция
ядра `FactoryMath.UnitsPerCycle(rule, profile, cards)` (сумма бонусов — `CardService.FactoryOutputBonus`); ядро, сведения
завода, бот и UI вызывают её, своих копий формулы не заводят (тест `Units_SingleRuleFunction_NoCopiesInGameCode`). Прежние «цена порций
месторождения» (Common, `TargetValue`) и «урон по месторождению» (Uncommon, `TargetDamage`) больше не действуют: цепи завода
бьются общим уроном и монет не дают. Длительность цикла карточки не меняют. **Автодоставку** завода включает не эффект, а
уровень карточки из его токена `s:typeId:level:rewardId:cardId:cardLevel` (`cardId` — Common или Uncommon этого типа,
`уровень ≥ cardLevel`; ядро, пакет WP1). `ca001` (`spawningcart`) — не завод: «Depot Master», разгрузка без изменений.
ID, редкости, `MineUnlock`, стоимость улучшений, уровни и копии игроков не меняются. Тексты: сетка `cards.short.FactoryOutput`
«{0} +{1}» («Milk +10%»), окно `cards.effect.FactoryOutput` «+{1} {0} per cycle» ({0} — товар); имена самоцветов
`cards.res.<typeId>` для карточек заводов не используются. Блок завода в окне карточки — ключи `cards.detail.factory` и
`cards.detail.mine_auto*`.

11 октября 2026 года по решению владельца общий предел офлайна снижен до трёх реальных часов, включая старые сохранения.
«Лежанка» (`ca051`) теперь ускоряет разгрузку на 5% за уровень вместо продления офлайна. Уровни и копии карточки сохраняются.

Это **адаптация**, а не восстановление оригинального баланса. Пометка «Происхождение» в таблице:

- **подтверждён прежним кодом** — смысл кода применялся в старом `GameSession` (`IsAppliedCardCode`: 9, 10, 7, 1); числа адаптированы;
- **предположение старого ТЗ** — коды 4, 5, 6, 8, 11, 13, 22, 23: в `Cat_Miners_Unity_Core_TZ_v1.3.md` (§10.1) записан
  «предполагаемый смысл» по совпадению с публичным списком; механики дальше адаптированы к новому ядру;
- **новое правило Cat Miners** — коды 3, 12, 15, 16, 19, 20, 21, 24, 25, 26: смысл в старом коде и документах не найден.

Значение эффекта на уровне L ≥ 1: `Base + PerLevel·(L − 1)` (точные дроби); у целых эффектов — `floor`. Карточка без
копий (уровень 0) эффекта не даёт. Все эффекты суммируются один раз в `TeamStats.Compute` (`ApplyCards`), кроме
`FactoryOutput`: выпуск завода — не характеристика команды, его по текущим уровням профиля считает `FactoryMath.UnitsPerCycle`
на отметке конца цикла (ТЗ 2.2 §4.4). Правила вне характеристик команды — `CardService.EffectSum/EffectWhole`. Бонус цены применяется к новой порции при выпуске; уже
лежащие и переносимые монеты сохраняют стоимость. Скорость, вместимость и разгрузка пересчитываются фазой 7 ядра с
сохранением доли ребра/обслуживания — без потери груза и телепортации. Скидки ограничены `MaxDiscount = 9/10`.

Числа подобраны по замеру темпа (BALANCE_V2.md, «Карточки cards-1»): первый вариант ускорял кампанию вдвое, поэтому
разгрузка `ca001`, общая цена `ca034`, скорость `ca027`, подбор `ca030` ослаблены, а котики «Плаката найма» `ca036`
приходят в счёт предела «Команды». Итог модели игрока с жадными улучшениями: шахты 1–10 быстрее на 1–16 %, вся кампания —
на 38 %. Менять темп — только числами `card_rules.json` (затем `python3 Tools/gen_outpost_config_defaults.py` и эта таблица).

## Все 61 карточка

Генерируется: `python3 Tools/Cards/gen_card_mapping.py --write` (`--check` — сверка без записи). «Исходный код и формула» — `StatModifierType`, `ModifierFormulaType`
и коэффициенты источника (`FormulaType`: F-0 `B + M·x + G·x²`, F-1 `B + M·G^(x−1)`, `Formula.cs`).

<!-- cards:begin -->
| ID | Имя | Редкость | Шахта | TargetIds | Manager | Исходный код и формула | Новый эффект | Формула значения | L1 / макс. | Происхождение | Где применяется |
|---|---|---|---:|---|---|---|---|---|---|---|---|
| `ca001` | Depot Master | Common | 1 | spawningcart | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | UnloadRate | 1/20 + 1/100·(L−1) | 5% / 18% (L14) | подтверждён прежним кодом, адаптирован | TeamStats.UnloadRate/UnloadTicks → склад (фаза 4) |
| `ca002` | Dairy Foreman | Common | 1 | amethyst | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca003` | Sawmill Foreman | Common | 3 | citrine | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca004` | Fishery Foreman | Common | 4 | agate | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca005` | Yarn Mill Foreman | Common | 5 | topaz | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca006` | Cat Food Foreman | Common | 7 | opal | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca007` | Box Foreman | Common | 10 | jade | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca008` | Brickworks Foreman | Common | 14 | onyx | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca009` | Steel Mill Foreman | Common | 19 | sapphire | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca010` | Glassworks Foreman | Common | 24 | tourmaline | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca011` | Catnip Foreman | Common | 31 | aquamarine | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca012` | Toy Foreman | Common | 38 | emerald | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca013` | Scratcher Foreman | Common | 47 | diamond | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca014` | Dairy Manager | Uncommon | 1 | amethyst | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca015` | Sawmill Manager | Uncommon | 1 | citrine | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca016` | Fishery Manager | Uncommon | 6 | agate | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca017` | Yarn Mill Manager | Uncommon | 9 | topaz | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca018` | Cat Food Manager | Uncommon | 12 | opal | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca019` | Box Manager | Uncommon | 17 | jade | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca020` | Brickworks Manager | Uncommon | 22 | onyx | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca021` | Steel Mill Manager | Uncommon | 27 | sapphire | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca022` | Glassworks Manager | Uncommon | 33 | tourmaline | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca023` | Catnip Manager | Uncommon | 42 | aquamarine | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca024` | Toy Manager | Uncommon | 52 | emerald | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca025` | Scratcher Manager | Uncommon | 63 | diamond | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca026` | Master's Yarn | Rare | 1 | — | — | 12 F-0 B+M·x+G·x²; B=0 M=0 G=0.6 | CompletionParts | 1/10 + 1/10·(L−1) | 10% / 90% (L9) | новое правило Cat Miners | StageProgressionService.ApplyCompletion (детали) |
| `ca027` | Courier Cat | Rare | 30 | — | — | 13 F-0 B+M·x+G·x²; B=0 M=0.3 G=0.1 | MoveSpeed | 3/100 + 1/100·(L−1) | 3% / 11% (L9) | предположение старого ТЗ, адаптирован | TeamStats.FinalMoveSpeed/EdgeTicks → движение (фаза 1, остаток ребра сохраняется) |
| `ca028` | Fish Ration | Rare | 15 | — | — | 3 F-1 B+M·G^(x−1); B=0 M=0.5 G=10 | Capacity | 1 + 1/4·(L−1) | 1 / 3 (L9) | новое правило Cat Miners | TeamStats.Capacity → сумки |
| `ca029` | Pocket Watch | Rare | 1 | — | — | 7 F-0 B+M·x+G·x²; B=0 M=60 G=0 | HireDiscount | 1/20 + 1/20·(L−1) | 5% / 45% (L9) | подтверждён прежним кодом, адаптирован | TeamUpgradeService.Quote/NextCost («Команда») |
| `ca030` | Milk Bowl | Rare | 8 | — | — | 15 F-0 B+M·x+G·x²; B=0 M=0.1 G=0.001 | PickupSpeed | 1/20 + 1/40·(L−1) | 5% / 25% (L9) | новое правило Cat Miners | TeamStats.PickupTicks → подбор (фаза 3) |
| `ca031` | Lucky Paw | Rare | 23 | — | — | 5 F-0 B+M·x+G·x²; B=0 M=0.047 G=0.0022 | CritChance | 1/20 + 1/40·(L−1) | 5% / 25% (L9) | предположение старого ТЗ, адаптирован | TeamStats.CritChancePpm → HitTarget (детерминированный бросок) |
| `ca032` | Gate Bell | Rare | 40 | — | — | 8 F-0 B+M·x+G·x²; B=0 M=30 G=0 | GateDamage | 1/5 + 1/10·(L−1) | 20% / 100% (L9) | предположение старого ТЗ, адаптирован | TeamStats.DamageUnitsFor('p') → HitTarget |
| `ca033` | Catnip | Rare | 53 | — | — | 16 F-0 B+M·x+G·x²; B=0 M=0.042 G=0.0015 | GlobalDamage | 1/20 + 1/40·(L−1) | 5% / 25% (L9) | новое правило Cat Miners | TeamStats.FinalDamage → все удары |
| `ca034` | Golden Cart | Legendary | 1 | — | — | 10 F-1 B+M·G^(x−1); B=0 M=1 G=3 | GlobalValue | 1/10 + 1/10·(L−1) | 10% / 50% (L5) | подтверждён прежним кодом, адаптирован | TeamStats.FinalValue → цена новых порций |
| `ca035` | Sledgehammer | Legendary | 36 | — | — | 6 F-1 B+M·G^(x−1); B=0 M=1 G=2 | CritPower | 1/4 + 1/4·(L−1) | ×2,25 / ×3,25 (L5) | предположение старого ТЗ, адаптирован | TeamStats.CritMultiplier → HitTarget |
| `ca036` | Hiring Poster | Legendary | 50 | — | — | 4 F-0 B+M·x+G·x²; B=0 M=1 G=0 | StartWorkers | 1 + 1/4·(L−1) | 1 / 2 (L5) | предположение старого ТЗ, адаптирован | StageProgressionService.CreateRun (Run.ExtraWorkers) |
| `ca037` | Gold Vein | Legendary | 20 | — | — | 11 F-1 B+M·G^(x−1); B=0 M=1 G=2 | KindValue·r | 3/20 + 3/20·(L−1) | 15% / 75% (L5) | предположение старого ТЗ, адаптирован | TeamStats.CoinValueFor (вид цели) → ReleaseCargo |
| `ca038` | Golden Helmet | Legendary | 1 | — | — | 1 F-0 B+M·x+G·x²; B=0 M=1 G=0 | TeamCap | 1 + 1·(L−1) | 1 / 5 (L5) | подтверждён прежним кодом, адаптирован | TeamUpgradeService (cap «Команды»), OutpostInvariants |
| `ca039` | Cushion Foreman | Common | 57 | garnet | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca040` | Cushion Manager | Uncommon | 74 | garnet | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca041` | Foundry Foreman | Common | 68 | heliodor | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca042` | Foundry Manager | Uncommon | 86 | heliodor | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca043` | Toy Mouse | Rare | 66 | — | — | 19 F-0 B+M·x+G·x²; B=0 M=0.08 G=0 | BranchDiscount | 3/100 + 3/100·(L−1) | 3% / 27% (L9) | новое правило Cat Miners | TeamUpgradeService.Quote/NextCost (ветки кроме «Команды») |
| `ca044` | Collar Foreman | Common | 80 | ruby | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca045` | Collar Manager | Uncommon | 97 | ruby | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca046` | Gear Foreman | Common | 93 | alexandrite | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca047` | Gear Manager | Uncommon | 108 | alexandrite | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca048` | Lantern Foreman | Common | 104 | celestine | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca049` | Lantern Manager | Uncommon | 117 | celestine | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca050` | Bell Collar | Epic | 1 | — | — | 24 F-0 B+M·x+G·x²; B=0 M=0.08 G=0.009 | TapDamage | 1/4 + 1/4·(L−1) | 25% / 175% (L7) | новое правило Cat Miners | TeamStats.TapDamageUnits → OutpostSimulation.ExecuteStrike |
| `ca051` | Cat Bed | Epic | 1 | — | — | 20 F-0 B+M·x+G·x²; B=0.01 M=0.02 G=0 | UnloadRate | 1/20 + 1/20·(L−1) | 5% / 35% (L7) | новое правило Cat Miners | TeamStats.UnloadRate/UnloadTicks → склад (фаза 4) |
| `ca052` | Scratching Post | Epic | 16 | — | — | 21 F-0 B+M·x+G·x²; B=0.03 M=0.0205 G=0.0016 | KindDamage·r | 1/10 + 1/10·(L−1) | 10% / 70% (L7) | новое правило Cat Miners | TeamStats.DamageUnitsFor (вид цели) → HitTarget |
| `ca053` | Miner's Lantern | Epic | 28 | — | — | 25 F-0 B+M·x+G·x²; B=4.5 M=-0.5 G=0 | ChestElixir | 1/10 + 1/10·(L−1) | 10% / 70% (L7) | новое правило Cat Miners | CardChestService.Open (эликсир сундука) |
| `ca054` | Rubble Blaster | Epic | 44 | — | — | 22 F-0 B+M·x+G·x²; B=0 M=0.1 G=0.002 | KindDamage·x | 1/5 + 1/10·(L−1) | 20% / 80% (L7) | предположение старого ТЗ, адаптирован | TeamStats.DamageUnitsFor (вид цели) → HitTarget |
| `ca055` | Lucky Clover | Epic | 60 | — | — | 23 F-0 B+M·x+G·x²; B=0 M=0.0005 G=0 | ChestLuck | 3/20 + 3/20·(L−1) | 15% / 105% (L7) | предположение старого ТЗ, адаптирован | CardChestService.Open (шансы Rare+) |
| `ca056` | Apiary Foreman | Common | 114 | amber | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca057` | Apiary Manager | Uncommon | 126 | amber | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca058` | Silk Mill Foreman | Common | 123 | moonstone | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca059` | Silk Mill Manager | Uncommon | 138 | moonstone | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=4 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 65% (L12) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca060` | Cannery Foreman | Common | 134 | sunstone | да | 9 F-1 B+M·G^(x−1); B=0 M=1 G=3 | FactoryOutput | 1/10 + 1/20·(L−1) | 10% / 75% (L14) | новое правило Cat Miners | FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats |
| `ca062` | Treasure Map | Majestic | 7 | — | — | 26 F-0 B+M·x+G·x²; B=0.05 M=0.05 G=0 | ChestCards | 1 + 1/2·(L−1) | 1 / 3 (L5) | новое правило Cat Miners | CardChestService.Open (+случайные карты) |
<!-- cards:end -->

### Адаптация удалённых механик

| Прежняя функция | Новое правило |
|---|---|
| Доход manager конкретной шахты (код 9) | `cards-3`: Common «… Foreman» и Uncommon «… Manager» одинаково увеличивают выпуск товара за цикл заводов своего `TargetIds` (`FactoryOutput`), бонусы складываются. Другие объекты карточка не усиливает. До `cards-3`: Common «Знаток» — цена порций месторождения, Uncommon «Мастер» — урон по нему |
| Стартовый узел `spawningcart` (`ca001`) | Склад базы: +скорость разгрузки |
| Автоматизация шахты по `cardId/cardLevel` токена `s` | Возвращена в `cards-3` как автодоставка завода (ТЗ 2.2 §4.4): уровень карточки `cardId` ≥ `cardLevel` — партии уходят в трубу без кнопки «Deliver». Требование пересчитывается при каждом росте уровня карточки (первая копия из сундука, улучшение). Токен `s` по-прежнему даёт свой сундук (`rewardId`) |
| Бесплатный котик (код 7), за ворота (код 8) | Скидка на найм; +урон по воротам |
| Уровень нанимаемых котиков (код 4) | котики с начала каждой новой шахты (`Run.ExtraWorkers`) — бесплатный найм в счёт предела «Команды»: покупаемых уровней на столько же меньше, общий предел команды не растёт |
| Доставки (код 13) | «Котик-курьер»: +скорость перемещения |
| Урон динамита (код 22) | +урон по завалам `x` |
| Шанс легендарной карты (код 23) | +шансы Rare и выше в сундуках |
| Сундуки и эликсир | Сундуки восстановлены: источники и состав ниже |

## Открытие, получение, улучшение

- **Доступность:** `HighestZone ≥ MineUnlock`, где `OutpostProfile.HighestZone` — максимальная шахта, в которую игрок вошёл
  (новая игра, переход, debug `LoadZone` только в debug-профиле). Снятие замка карточку не выдаёт. Уже полученная карточка
  не блокируется повторно. На шахте 1 открыты 10 карточек, на шахте 138 — все 61.
- **Получение:** первая копия даёт уровень 1, следующие копятся (прежнее правило `GameSession.Rewards`). Особой активации
  «собрать X/Y» в прежнем коде нет; состояние `Collecting` поддержано интерфейсом, но правилами `cards-1` не используется.
- **Улучшение:** переход L → L+1 списывает `Duplicates[L−1]` копий и `SoftCurrency[L−1]` эликсира `CardUpgradeCosts` своей
  редкости; максимум — `1 + длина массива` (Common 14, Uncommon 12, Rare 9, Epic 7, Legendary 5, Majestic 5). Команда ядра
  `UpgradeCard` атомарна, контроллер сразу записывает снимок; доступна и в итогах шахты.
- **Эликсир** — отдельная постоянная валюта профиля `OutpostProfile.Elixir`; золото шахты и детали экипировки не тратятся.
- **Учебная карта:** новый профиль получает первую копию `ca001` при первой доставке на базу (ключ
  `CatOutpostV2:cards:tutorial-first-delivery`, один раз). Профили, созданные до карточек, помечаются выполненными при миграции.

## Сундуки (`CardChestService`)

Источники в новом ядре (все — при разрушении цели или завершении шахты, сразу в профиль, детерминированно):

| Источник | Что даёт | Данные |
|---|---|---|
| Камень `r` | очередная запись своего `RewardCycle` (счётчик по `Rocks[].Id` в профиле, переносится между шахтами): сундук или эликсир; золото — по-прежнему порциями груза | `rockbasic` 500 записей, `rockgacha*`, `rocksoftcurrency*`, onboarding-камни |
| Цепи завода `s` | сундук `rewardId` токена при открытии завода | `GachaIron` или скриптовые `scg_m0XX_sYY` (гарантированные карточки) |
| Ворота `p` | сундук `rewardId` при разрушении | `GachaForged` или `scg_m0XX_c01` |
| Выход `e` | сундук при завершении шахты | `scg_m001_exit…scg_m010_exit` (zone1: `ca001` ×1 и 17 эликсира), далее `GachaCrusher` |

Состав сундука:

1. Гарантированные карточки записи `Gacha`; у записей с `GuaranteedCardMin` — нижние границы (как прежний `GachaService`).
2. Эликсир: нижняя граница у таких записей и у фиксированного диапазона, иначе бросок в `[min, max]`; × множитель эликсира
   зоны по `GachaType` (`RankMultipliers`; тип 2 — без множителя) × (1 + «Шахтёрский фонарь»), `floor`.
3. Случайные карты: `max(1, RoundHalfUp(BaseNumCards × множитель карт зоны))` + «Карта сокровищ». Редкость каждой — по
   нормализованным шансам Cat Miners (ниже), «Клевер удачи» умножает шансы Rare и выше; карточка — равновероятно среди
   открытых по прогрессу этой редкости, при отсутствии — следующая редкость ниже. В `cards-3` выбор не менялся: карточки
   заводов выпадают наравне с остальными своей редкости; поля `Gacha` `ManagerChanceMult` и `LowCardChanceMult` (их читал
   только прежний `GachaService`) в CatOutpostV2 не применяются (ТЗ 2.2 §7.1).

Веса прежнего `Gacha` (`UncommonWeight`, `RareWeight`, …) **не считаются вероятностями**: прежний код трактовал их как
делители, при этом Legendary и Majestic были почти недоступны (`ca062` не выпадала вовсе). Шансы одной карты, ppm, правило
Cat Miners (`card_rules.json` → `ChestChances`):

| Сундук | Common | Uncommon | Rare | Epic | Legendary | Majestic |
|---|---:|---:|---:|---:|---:|---:|
| Wooden (и по умолчанию) | 800 000 | 150 000 | 40 000 | 8 000 | 1 800 | 200 |
| Iron | 700 000 | 200 000 | 70 000 | 20 000 | 8 500 | 1 500 |
| Forged | 580 000 | 250 000 | 110 000 | 45 000 | 12 000 | 3 000 |
| Silver | 500 000 | 270 000 | 150 000 | 60 000 | 16 000 | 4 000 |
| Golden | 400 000 | 300 000 | 180 000 | 90 000 | 25 000 | 5 000 |
| Royal | 300 000 | 300 000 | 220 000 | 120 000 | 50 000 | 10 000 |
| Mythical | 200 000 | 280 000 | 250 000 | 170 000 | 80 000 | 20 000 |
| Crusher (выходы с zone11) | 450 000 | 280 000 | 160 000 | 80 000 | 25 000 | 5 000 |
| Legendary / Epic / Rare / Uncommon | только своей редкости | | | | | |

Генератор — `OutpostProfile.CardRng` (Pcg32, поток 6, отдельно от наград экипировки); порядок бросков фиксирован, поэтому
онлайн, офлайн и после загрузки результат одинаков. Не восстановлены: бесплатный сундук по таймеру и доставки (в новом
режиме нет таймеров рекламы и контейнеров), твёрдая валюта, бонусы Crusher-мини-игры (`CrushableRewards`).

## Критический удар

Шанс — «Счастливая лапка», множитель — 2 + «Кувалда». Бросок детерминирован: SplitMix64 от seed профиля, `RunId`,
`WorkerId` и отметки удара; урон крита `RoundHalfUp(урон · множитель)`. Событие `TargetHit.Extra` — число критов.

## Сохранения и миграция

- Новые поля профиля: `CardsVersion`, `Cards`, `Elixir`, `HighestZone`, `RockCycles`, `CardRng`, `CardKeys`; run —
  `ExtraWorkers`, счётчики `ChestsOpened/CardsGained/ElixirGained`. Схема снимка остаётся 1 (поля добавлены, старые снимки
  читаются со значениями по умолчанию).
- `CardService.MigrateProfile` (CardsVersion 0 → 1, один раз): `HighestZone` из текущей и завершённых шахт, генератор сундуков
  из seed профиля, учебная выдача помечена выполненной. Перед первой записью main/backup копируются в `<слот>/pre-cards/`.
- `CardService.ImportLegacy` (ключ `CatOutpostV2:cards:legacy-import`, один раз): карточки и эликсир прежнего режима по
  стабильным ID из `save_main.json`/`save_debug.json` или его копии в `CatOutpostV2/legacy-archive` (берётся более поздний).
  Уже полученные в новом профиле карточки не перезаписываются. У пользователя на 9 октября: `ca001` уровень 1 с двумя копиями
  и 56 эликсира.
