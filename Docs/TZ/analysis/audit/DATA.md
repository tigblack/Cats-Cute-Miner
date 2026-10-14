## DATA-01 major improvement | 6.4, 3
Q: Размеры этих трёх объектов не заданы обычными полями. Получи их из исходных префабов/кода.
P: Префабов и кода нет, поэтому ТЗ фактически оставляет размеры e/p/c на усмотрение исполнителя. При этом данные однозначно задают логическую геометрию e и p. Если p сделать 1×1, клетки (2..5, row_p) не входят ни в один footprint, и ворота обходятся сбоку. Если e сделать 1×1, клетки (3,0) и (4,0) останутся свободными «карманами». Кроме того, в разделе 3 сказано, что проходимость не проверена, хотя достижимость целей уже доказана скриптом.
E: Проверено на всех 144 зонах (analysis/special_objects.py, reachability.py, scratch/explore17.py).
- e: якорь всегда (2,0). Клетки (1,0) и (5,0) заняты footprint препятствий 144/144, клетки (2..4,0) свободны 144/144. Под выходом клетки (2..4,1) в исходном состоянии заняты во всех 144 зонах.
- p: 280/280 стоят в col 1. Клетки (0,row) и (6,row) заняты препятствиями 280/280, клетки (1..5,row) свободны 280/280. Три строки над воротами свободны во всех 7 столбцах.
- c: всегда `c:spawningcart:1` (144/144), col 1, row = Depth−4 (142 зоны) или Depth−3 (только zone1 и zone2). Строки c−3..c+1 свободны во всех 7 столбцах. spawningcart отсутствует в MiningTargetHealths, Rocks, MineShafts и Obstructions.
- Ворот на зону: 0 (zone1–4, 7, 11), 1 (35 зон), 2 (64), 3 (39) — итого 424 сектора. Свободные клетки каждого сектора образуют одну 4-связную компоненту.
- При 4-соседстве, e=3×1 и p=5×1 все r/s/p/e во всех зонах достижимы.
FIX: Заменить список в 6.4 на: «Логическая геометрия особых объектов выведена из всех 144 карт и обязательна:
- `e` — 3×1, клетки (2..4, 0); рабочая сторона — строка 1.
- `p` — 5×1, клетки (1..5, row) от якоря в столбце 1. Клетки (0,row) и (6,row) всегда заняты препятствиями, поэтому после разметки строка ворот полностью перекрыта. Три строки над воротами — свободная площадка следующего сектора.
- `c` — якорь (1, Depth−4), в zone1/zone2 — (1, Depth−3). Уровень в токене во всех зонах равен 1. Это не цель добычи (HP-таблицы нет). Логическая коллизия — 1×1 или иная, не выходящая за свободную полосу строк c−3..c+1.
Валидатор проверяет: 0 пересечений e/p/c с footprint r/s/x; строка каждых ворот полностью занята; свободные клетки каждого сектора 4-связны; все r/s/p/e достижимы при 4-соседстве.»
Последний пункт раздела 3 заменить на: «Достижимость всех целей при 4-соседстве проверена на всех 144 картах; темп экономики не проверялся.»

## DATA-02 major factual-error | 11, 10.3
Q: Награда, непосредственно указанная в токене `e`, остаётся наградой выхода; дополнительные результаты бонусной мини-игры не симулируются и не выдаются.
P: По данным результат Crusher — не добавка к награде выхода. Он определяет размер самой награды из токена e. Скриптовые награды выхода zone1–10 заданы диапазонами, и без мини-игры неизвестно, какую точку диапазона выдавать. Фраза ТЗ «В ряде записей GuaranteedCardMin пуст» тоже неточна: Min есть только у наград выхода.
E: - Все 10 записей scg_m001_exit…scg_m010_exit: MimicId=GachaCrusher, CardMultMax 0.3…0.95, SoftCurrencyMin<SoftCurrencyMax, GuaranteedCardMin задан.
- Примеры: scg_m001_exit — ca001 Min[1] Count[1], Soft 17..250. scg_m003_exit — ca002/ca014 Min[3,1] Count[6,1], Soft 50..250. scg_m010_exit — Min[3,10,1,3,1] Count[9,18,3,7,1], Soft 100..250.
- GuaranteedCardMin непуст ровно в этих 10 записях. В остальных 35 scg_* он пуст, SoftCurrencyMin=Max, CardMultMax=0.
- CrushableRewards (10 типов) задают CardMult 0..0.4 и SoftCurrencyMult 0.0001..0.2 за раздавленные объекты.
- В zone11–144 токен e ссылается на GachaCrusher (134 зоны): BaseNumCards 35, Soft 250/250, EventEpicWeight 125.
FIX: В разделе 11 заменить абзац про Crusher: «Награды выхода zone1–zone10 (scg_m0XX_exit) диапазонные: карточки от GuaranteedCardMin[i] до GuaranteedCardCounts[i], эликсир от SoftCurrencyMin до SoftCurrencyMax. В референсе положение в диапазоне задаёт Crusher Truck: накопленные CardMult/SoftCurrencyMult из CrushableRewards с пределом CardMultMax. Crusher исключён, поэтому правило прототипа такое: выдавать нижнюю границу (GuaranteedCardMin, SoftCurrencyMin) — это нулевой результат мини-игры. Правило внести в реестр неподтверждённых интерпретаций, отличие от референса — в отчёт. С zone11 выход выдаёт GachaCrusher по общему алгоритму сундуков.»
В 10.3 заменить «в ряде записей» на: «GuaranteedCardMin непуст только в 10 наградах выхода; в остальных 35 скриптовых наградах он пуст, и количество фиксировано и равно GuaranteedCardCounts.»

## DATA-03 major omission | 10.2, 11, 12.1, 16
Q: Отдельно определить и сохранять счётчик каждого нужного цикла. Его область жизни и сброс зависят от исходной реализации и должны быть указаны в `BALANCE_MAPPING.md`.
P: ТЗ оставляет область жизни счётчика RewardCycle открытой, хотя данные практически исключают сброс в пределах зоны. Кроме того, раздел 11 не включает счётчики циклов в состояние, переносимое между зонами. Исполнитель, следуя §16 (MineProgressState отдельно от PlayerProgressState), положит счётчик в локальное состояние зоны и сбросит его при переходе.
E: - rockbasic: цикл из 500 записей. Впервые появляется в zone6, максимум 117 штук на зону (zone70). Всего на картах 9958 штук, это ≈19.9 полных цикла.
- Записи с индексом ≥117 недостижимы при сбросе на зону. Среди них GachaSilver (индексы 121, 368), GachaGolden (381), Hard×3 (261), GachaForged (188, 325, 438).
- Накопленно 500 rockbasic набирается только к zone16.
- Onboarding-камни: циклы и число экземпляров не совпадают 1:1. mine2 — цикл 8 при 11 экземплярах (переполнение), mine3 — 19 при 17, mine4 — 27 при 24.
FIX: Заменить абзац в 10.2: «Счётчик RewardCycle ведётся отдельно для каждого Rocks[].Id и хранится в постоянном PlayerProgressState: при переходе зоны он не сбрасывается. Индекс берётся по модулю длины цикла. Награда определяется и счётчик увеличивается в той же транзакции, что и завершение камня. Это интерпретация, выведенная из данных: при сбросе на зону 383 из 500 записей rockbasic были бы недостижимы. Её внести в реестр с пометкой «не подтверждено кодом».»
В разделе 11 в список «Сохранить…» добавить «счётчики циклов наград камней».

## DATA-04 major omission | 10.1, 9.1
Q: Имеющийся в файле код `StatModifierType = 9` у manager-карт требует расшифровки; нельзя назначать его эффект только по догадке.
P: ТЗ не разделяет два механизма: условие автоматизации (поля токена s) и числовой эффект manager-карты (код 9). По данным код 9 не может быть ключом автоматизации. Без этого уточнения исполнитель либо свяжет автоматизацию с кодом 9, либо сочтёт ca001 бесполезной. А ca001 — первая карточка в игре и самая частая в ранних наградах.
E: - ca001: IsManager=true, StatModifierType 9, TargetIds=['spawningcart']. Её не требует ни один токен s, а стартовое производство автоматическое (§9.1; публичная статья о Forge: «coins are instantly added»).
- Значения кода 9: у rarity 1 — Base 0 / Mult 1 / Growth 3 / FT1, у rarity 2 — 0 / 1 / 4 / FT1. Это растущее с уровнем число, а не флаг.
- ca001 выдаётся в 13 скриптовых наградах, суммарно 42 копии: scg_m001_exit ×1, scg_m002_s01 ×2, scg_m002_exit ×3, scg_m005_s01 ×5, scg_m009_exit ×9 и др.
- Публичный гайд по карточкам: managers «Multiply the passive income generated by the Forge and active Shafts».
FIX: Добавить в 10.1: «Условие автоматизации шахты полностью задано токеном s (cardId и требуемый уровень) и не зависит от StatModifierType. Код 9 — отдельный числовой модификатор manager-карты для генератора из TargetIds (FT1; rarity 1: 0/1/3, rarity 2: 0/1/4). По данным он не может быть ключом автоматизации: ca001 нацелена на автоматически работающий spawningcart и не требуется ни одним s. В реестре код 9 записать как «вероятно, множитель дохода цели, не подтверждено». В подтверждённом профиле эффект не применяется, но карточка показывается с пометкой. В PrototypeCompatibility допускается временная формула. Молча игнорировать ca001 нельзя.»

## DATA-05 major factual-error | 13, 17.1 п.7
Q: 8. После получения нужных карточек показать улучшение и автоматизацию.
P: Порядок обучения (шаги 7–8 на zone1–3) и тест «ремонт → активация → ручная выплата → автоматизация» не учитывают, что в zone2 автоматизация невозможна по данным. Если исполнитель попытается показать её в zone2, тупик придётся «чинить» выдачей карточки. Первая автоматизация возможна только в zone3.
E: - zone2: `s:amethyst:1:scg_m002_s01:ca002:1`. Награда этой шахты scg_m002_s01 даёт ca001×2, а не ca002.
- Камни zone2 (rockonboardingmine2, rocksoftcurrencysmall) дают только Core/Soft. Доставки и FreeGachaCycle начинаются с ранга 4.
- Первый источник ca002 — scg_m002_exit (ca002 Min 1/Count 1), то есть только после выхода из zone2.
- zone3: `s:citrine:2:scg_m003_s02:ca003:1`. ca003 даёт камень rockonboardinggachawoodenmine3 через scg_m003_r01 (ca003×1), так что улучшать карту не нужно.
- zone3: `s:amethyst:1:scg_m003_s01:ca002:2`. Нужно улучшить ca002 до уровня 2 (rarity 1: Duplicates 2, SoftCurrency 50). Копии: 1 из scg_m002_exit и 3 из scg_m003_s01.
FIX: В разделе 13 заменить шаги 7–8 на:
«7. На zone2 восстановить и активировать amethyst и показать ручной сбор. По данным автоматизировать его в zone2 нельзя: ca002 выдаёт только награда выхода zone2.
8. На zone3 показать автоматизацию citrine после добычи rockonboardinggachawoodenmine3 (ca003, уровень 1), а затем улучшение ca002 до уровня 2 для автоматизации amethyst.»
В 17.1 п.7 указать: «проверять на zone3 (citrine/ca003:1); отдельно проверить, что в zone2 при обычной игре автоматизация amethyst недоступна.»

## DATA-06 major omission | 10.4
Q: В текущем файле есть `DeliveryStartupDelaySec = 20`, `DeliveryDelaySecBase = 22`, `DeliveryClaimCountResetSec = 3600`, `DeliveryMaxDupesResetSec = 14400`.
P: Параметры доставок перечислены не полностью: пропущены формула роста задержки, разброс и условия старта. ТЗ не говорит, что записи 1–5 — доставки котиков, которые занимают лимит, а поле Quantity у них, вероятно, означает смещение уровня. Не указано, что после исключения рекламы останутся только «−2»-варианты. Путь к полям наград задан неверно: они вложены в RewardModel.
E: - BalanceProperties: DeliveryDelaySecMultiplier 1, DeliveryDelaySecGrowth 1.02, DeliveryDelaySecFormulaType 2 (тот же FT, что у UpgradeCost), DeliveryDelaySecVariance 0.1, DeliveryStartRank 4, DeliveryStartMineShaftNum 2, DeliveryForeverAloneDelta 2.
- Поля записи Deliveries: Id, RewardModel{RewardType, DetailedType, Quantity}, Quantity{Base, Multiplier, Growth, Power, Round, FormulaType}, Weight, MaxDupes, RankUnlock, CheckPointUnlock, IsAd.
- Id 1–5: RewardType 4, DetailedType 'tinygoblin'. QuantityBase +2/Round +1 у рекламных (1, 2), −2/−1 у обычных (3, 4, 5), FT3.
- Id 6–9: Soft. Id 10–15: Core (FT3, Mult 300..7500, Power 2, Round 60). Id 16–21: Dynamite.
- IsAd=true: 1, 2, 6, 10. MaxDupes=−1 у id 15. RankUnlock 4..9.
FIX: Дополнить 10.4: «Полный набор параметров: DeliveryDelaySec{Base 22, Multiplier 1, Growth 1.02, FormulaType 2, Variance 0.1}, StartupDelay 20, ClaimCountReset 3600, MaxDupesReset 14400, DeliveryStartRank 4, DeliveryStartMineShaftNum 2, DeliveryForeverAloneDelta 2. Аргумент формулы задержки (вероятно, число получений в окне ClaimCountReset) и смысл ForeverAloneDelta/StartMineShaftNum внести в реестр. Поля наград читать из `Deliveries[].RewardModel`. Записи 1–5 (RewardType 4, tinygoblin) доставляют котика: они подчиняются лимиту BaseUnitCap так же, как бесплатный котик. Quantity* (±2, Round ±1, FT3), вероятно, задаёт смещение уровня относительно уровня найма, а не количество; это в реестр. После исключения IsAd остаются только записи со смещением −2. MaxDupes = −1 означает «без ограничения» (интерпретация).»

## DATA-07 major ambiguity | 10.3, 10.4
Q: Для обычных доставок сохраняются условия `RankUnlock`, `CheckPointUnlock`, `Weight`, `MaxDupes` и счётчики сброса.
P: Нигде в ТЗ не определено, что такое «ранг» игрока и «ограничения по открытым картам» (поле Cards.MineUnlock не названо). Без этого нельзя реализовать RankUnlock, DeliveryStartRank и фильтр карт в случайных сундуках. Данные позволяют дать твёрдую рабочую трактовку.
E: - Cards.MineUnlock у 19 из 20 manager-карт rarity 1 равен номеру зоны, где целевая шахта появляется впервые: ca003=3 (citrine, zone3), ca006=7 (opal), ca007=10 (jade), ca008=14, ca013=47, ca060=134 (sunstone, zone134). Исключение — ca002=1 (amethyst впервые в zone2).
- rockbattlepasscurrency впервые стоит в zone7, а BattlePass.ActivationRank = 7.
- RankMultipliers задаются для каждой зоны, а GridRowRanks=null во всех 144 зонах, то есть ранга по строкам нет.
- Quests.Rank 8..141 и StoreItems BundleRank5…BundleRank140 — все значения ≤144.
- Поправка к analysis/verified_facts.md: MineUnlock лежит в диапазоне 1..138, а не 1..66.
FIX: Добавить в 10.3/10.4: «Ранг игрока (RankUnlock, DeliveryStartRank, FreeGachaCycle.StartRank, RewardWheel/BattlePass.ActivationRank, Quests.Rank) принять равным порядковому номеру текущей зоны (1..144). Cards.MineUnlock — номер зоны, начиная с которой карточка может выпадать из случайных (не скриптовых) сундуков: условие MineUnlock ≤ номер текущей зоны. Обе трактовки выведены из данных: MineUnlock совпадает с первой зоной целевой шахты у 19 из 20 manager-карт, rockbattlepasscurrency появляется в zone7 при BattlePass.ActivationRank 7. Внести их в реестр как data-derived. Смысл DeliveryStartMineShaftNum/StartMineshaftNum = 2 (вероятно, число активированных шахт) — открытый вопрос.»

## DATA-08 major omission | 3 (таблица «Проверенные сведения»), 2
Q: —
P: Таблица разделов неполная: в JSON 33 корневых ключа, в таблице 13. Пропущены разделы, которые нужны ядру (Reinforcements, SpawningCart, CheckPoint, Spells, Exit, CrushableRewards). Не классифицирован FreeGachaCycle — немонетизированный источник сундуков и карт, который не назван ни среди включённых, ни среди исключённых систем.
E: - Пропущенные корневые ключи: Reinforcements(1), SpawningCart(1), CheckPoint(1), Spells(1), Exit({}), CrushableRewards(10), FreeGachaCycle(1), CostConversions(1), StoreItems(183), Bundles(107), RewardWheel(1), Achievements(18), ProgressiveOffers(13), Quests(47), MultiOffers(12), ProgressiveOffersABTest(13), MultiOffersAbTest(12), BundleReplacementsAbTest(52), BattlePassRegions(8), BattlePassBonusRewards(15).
- FreeGachaCycle: StartRank 4, StartMineshaftNum 2, MaxFreeGachas 2, Hours 4, 377 GachaIds (Wooden 300, Iron 55, Forged 12, Silver 4, Golden 2, Legendary 1, Royal 1, scg_free_01, scg_free_02).
- scg_free_01 даёт ca002 + ca015, scg_free_02 — ca001×2.
FIX: Дополнить таблицу раздела 3 строками: `Reinforcements` (1; наём), `SpawningCart` (1; кузница и бесплатный котик), `CheckPoint` (1; бонусы ворот), `Spells` (1; динамит), `Exit` ({}), `CrushableRewards` (10; только Crusher), `FreeGachaCycle` (1). Остальные корневые ключи перечислить строкой «исключённые системы».
В раздел 2 добавить явное решение: «FreeGachaCycle (бесплатный сундук раз в 4 часа, не более 2 в запасе, старт с ранга 4, 377 записей в фиксированном порядке) — [включить в первую версию / исключить и указать в отчёте как отключённый источник карточек].»

## DATA-09 major omission | 10.2, 4.2
Q: `Quantity = 0` не означает автоматически «ничего не выдать». В файле так записаны вычисляемые валютные награды и выдача сундуков.
P: ТЗ требует отдельный resolver для каждого вида награды, но не называет поля, из которых в данных вычисляются суммы. В 4.2 назван только CoreCurrencySeconds*. Эликсирные формулы камней и зональный множитель не упомянуты. Кроме того, RewardType/CurrencyType записаны в 4.2 как нерасшифрованные, хотя в разделах ядра их смысл однозначно задаёт DetailedType.
E: - Soft: Rocks[].SoftCurrency{Base, Multiplier, Growth, FT0}. Значения: rocksoftcurrencysmall 16/2.5, rocksoftcurrencybig 45/7, rockbasic и onboarding 4/0.3. У камней без Soft в цикле — нули (кроме rockonboardingmine1). Зональный множитель: Zones[].RankMultipliers[0].MiningSoftCurrencyMultiplier — от 0.1 (zone1) до 5 (zone144).
- Core: CoreCurrencySeconds{FT3, 10, 635, 0.2, 1.15, 15} — только у rockbasic и onboarding.
- Hard: явное Quantity 1/2/3/10. Gacha: DetailedType = Gacha.Id.
- В разделах ядра встречаются только RewardType 0 (DetailedType Core/Soft/Hard/BattlePass), 1 (Gacha.Id), 4 ('tinygoblin'), 9 ('Dynamite'). Типы 2/3/5/6/7/8/12/13 есть только в StoreItems, Bundles, Quests, Achievements, RewardWheel, Offers.
- CurrencyType = 1 у всех 20 MineShafts и у SpawningCart; CoreCurrencyId = gold.
FIX: Добавить в 10.2 таблицу resolver'ов:
- (0, Core) — секунды дохода по Rocks.CoreCurrencySeconds* через GetRewardIncomeRate.
- (0, Soft) — Rocks.SoftCurrency* × RankMultipliers.MiningSoftCurrencyMultiplier (аргумент и порядок применения в реестр).
- (0, Hard) — Quantity из записи.
- (1, <GachaId>) — один сундук указанной записи.
- (0, BattlePass) — см. правило исключённых наград.
В 4.2 строку про CurrencyType/RewardType заменить на: «В разделах ядра RewardType ∈ {0, 1, 4, 9}, и вид награды задаёт DetailedType. CurrencyType везде равен 1 (gold). Валидатор отклоняет любые иные значения в разделах ядра.»

## DATA-10 major omission | 2, 4.3, 10.2
Q: Данные этих систем сохраняются в исходном JSON, но не создают экраны и игровые процессы первой версии.
P: На основных картах стоит физический объект исключённой системы — камень с наградой сезонного пропуска. ТЗ не говорит, что с ним делать. Исполнитель может удалить его (это изменит геометрию и проходимость), заменить награду золотом или эликсиром (изменит экономику) или упасть на неизвестной валюте.
E: - rockbattlepasscurrency встречается на картах 72 раза, в zone7…zone144, например `r:rockbattlepasscurrency:67` в zone144 (0,0).
- HP-таблица MiningTargetHealths.rockbattlepasscurrency есть. RewardCycle = [{RewardType 0, DetailedType 'BattlePass', Quantity 1}].
- AntiCheatSettings.BattlePassCurrencyMax = 0; BattlePass.ActivationRank = 7.
FIX: Добавить в 4.3/10.2: «rockbattlepasscurrency (72 экземпляра, zone7–zone144) остаётся обычной добываемой целью: занимает клетку, имеет HP из MiningTargetHealths.rockbattlepasscurrency и освобождает клетку после разрушения. Награда (0, BattlePass, 1) не превращается в золото, эликсир или кристаллы. Она записывается в технический инвентарь как неподдерживаемая валюта (или в журнал) и указывается в отчёте как отключённый источник. Удалять или подменять такие камни при импорте запрещено. То же правило действует для любых наград исключённых систем.»

## DATA-11 major improvement | 9.3
Q: Гипотеза, требующая подтверждения: числа обозначают количество улучшений между порогами, а не абсолютные уровни.
P: Данные опровергают трактовку ObjectiveCount как абсолютных уровней и дают проверяемый инвариант для гипотезы интервалов. В ТЗ также не названы поля массива множителей и награды эликсиром за порог.
E: - ObjectiveCount содержит повторы и немонотонен ([9,10,10,10…], [19,20,40,40…]), поэтому абсолютными уровнями он быть не может.
- Сумма ObjectiveCount = 1199 у всех 21 генератора (длины массивов 120/61/31). При старте с уровня 1 все пороги кратны 10 (10,20,30… / 10,20,40,60… / 20,40,80…), последний порог — ровно 1200.
- onyx, garnet и sunstone начинают с 19, то есть первый порог у них — 20.
- Поля записи: GeneratorId, ObjectiveCount, CoreCurrencyMultiplier, SoftCurrencyReward{Base, Multiplier, Growth, FormulaType=0}. Пример — spawningcart 0.5/0.125/0, amethyst 1.4/0.6/0.
- GenObjectiveSoftCurrencyMultiplier лежит в Zones[].RankMultipliers[0] и меняется от 1 до 5.
FIX: Заменить абзац в 9.3: «`GeneratorObjectives[].ObjectiveCount` — длины интервалов между порогами; абсолютными уровнями они быть не могут (в массивах есть повторы). Порог k = 1 + Σ ObjectiveCount[0..k]. Инвариант валидатора: у всех 21 генератора сумма равна 1199, все пороги кратны 10, последний порог — уровень 1200. На пороге k доход умножается на `CoreCurrencyMultiplier[k]` (накопительно; порядок применения — в реестр) и один раз выдаётся эликсир по `SoftCurrencyReward*` × `RankMultipliers.GenObjectiveSoftCurrencyMultiplier`. Уровень 1200 считать вероятным максимумом генератора (в реестр). Выше последнего порога бонусов нет.»

## DATA-12 major improvement | 11, 10.1
Q: Параметры бонусов находятся в `CheckPoint`: коды `[10, 2, 7, 1]`, значения `ModifierBase = [2, 10, 60, 1]`.
P: ТЗ не сообщает, что три из четырёх кодов CheckPoint совпадают с кодами карточек, а сами бонусы ворот — константы. Также не определено, какие не-manager карточки входят в «минимальный слой». По данным это конечный список: эти карточки гарантированно выдаются на zone4–10.
E: - CheckPoint: ModifierMultiplier, Growth, Power и Round = [0,0,0,0], ModifierFormulaType = [0,0,0,0], то есть значения не зависят от аргумента.
- Совпадающие коды: 10 — ca034 (rarity 5, FT1 0/1/3, TargetIds []); 7 — ca029 (rarity 3, FT0 Mult 60); 1 — ca038 (rarity 5, FT0 Mult 1). Кода 2 нет ни у одной карточки.
- Не-manager карточки в скриптовых наградах: ca026 (код 12) в scg_m004/m006/m009_exit; ca029 (код 7) в scg_m005/m008_exit; ca050 (код 24) в scg_m007_exit; ca030 (код 15) в scg_m010_exit.
FIX: Дополнить раздел 11: «Бонусы CheckPoint применять через тот же реестр StatModifierType, что и карточки. Коды 10/7/1 совпадают с ca034/ca029/ca038, код 2 встречается только у ворот. Значения — константы (FT0, все коэффициенты кроме Base равны 0).»
Дополнить 10.1: «Минимальный слой карточек — все карточки, гарантированно выдаваемые в zone1–10: manager ca001–ca007, ca014–ca017 и не-manager ca026 (код 12), ca029 (7), ca030 (15), ca050 (24). Для каждого кода в BALANCE_MAPPING указать статус: подтверждён / временная формула / не поддерживается.»

## DATA-13 minor improvement | 4.2
Q: Точные функции, входной аргумент, база индекса, порядок округления
P: ТЗ верно запрещает угадывать FormulaType, но не фиксирует структурные ограничения, которые прямо следуют из данных. Без них временный PrototypeCompatibility-профиль легко получит вырожденные формулы: нулевую стоимость, постоянную цену или нулевой эффект карты.
E: - FT2: UpgradeCost у всех 21 генератора — Base>0 (72…7.34e17), Multiplier=0, Growth 1.43…1.9. Формулы вида Mult·… дадут 0, Base+Mult·… — постоянную цену. DeliveryDelaySec FT2 = 22/1/1.02.
- FT1: Base=0 у Damage (0/1/2.1), Cost (0/2/4), manager-карт (0/1/3|4), ca034/35/37 (0/1/2–3); у Spells — 750/300/2.1. Формула Base·Growth^x дала бы 0.
- FT0: 8 карт с Growth≠0 (ca026 0/0/0.6, ca027 0/0.3/0.1, ca030 0/0.1/0.001 и др.). У ca026 при FT0 = Base+Mult·x эффект тождественно равен 0.
- FT3 использует все пять параметров (CoreCurrencySeconds 10/635/0.2/1.15/15, Deliveries.Quantity).
FIX: Добавить под таблицей 4.2: «Структурные ограничения из данных (временные формулы обязаны им соответствовать, валидатор проверяет их на контрольных значениях):
- FT2 не умножает на Multiplier: у всех UpgradeCost Multiplier=0 при росте цены.
- FT1 не умножает на Base: Base=0 у урона, найма и manager-карт.
- FT0 содержит член с Growth, не зависящий от Multiplier: у ca026 Mult=0, Growth=0.6.
- FT3 использует Base, Multiplier, Growth, Power и Round.
Нарушение любого ограничения — ошибка профиля.»

## DATA-14 minor improvement | 4.2, 10.2
Q: Визуальная подмена, наследование полей либо специальная семантика
P: Данные позволяют сузить неопределённость MimicId. Все записи с MimicId содержат полный собственный набор игровых полей, а MimicId всегда указывает на «класс» объекта того же слота. Наследование полей не требуется, и его применение исказило бы скриптовые награды.
E: - 12 камней с MimicId (rockonboarding* → rockbasic/rockgacha*): собственные RewardCycle, HP-таблицы совпадают с таблицей MimicId 12/12.
- Все 45 scg_* имеют MimicId. После разрешения MimicId: s → GachaIron 1232/1232, p → GachaForged 280/280, e → GachaCrusher 144/144, скриптовые камни → GachaWooden/GachaIron.
- У scg_* BaseNumCards=0 и веса = 0 явно записаны. При наследовании от GachaIron (BaseNumCards 14) скриптовая награда получила бы лишние случайные карты.
FIX: В 4.2/10.2: «MimicId — класс представления: модель камня или сундука, анимация открытия, иконка. Все игровые поля (HP, RewardCycle, состав награды, BaseNumCards, веса) берутся только из собственной записи, без наследования. Это согласуется со всеми 57 записями с MimicId. Валидатор: MimicId ссылается на существующую запись того же раздела, у камня с MimicId есть собственная HP-таблица.»

## DATA-15 minor improvement | 10.3
Q: Веса `RareWeight`, `LegendaryWeight` и другие не интерпретировать как обычную roulette-weight таблицу без проверки.
P: Данные не просто «не доказывают» roulette-трактовку весов, а опровергают её. Кроме того, ТЗ требует учитывать «множители текущей зоны», не уточнив, какие поля к каким типам сундуков относятся. Буквальное применение к скриптовым наградам обнуляет первые карточки игры.
E: - Поля CommonWeight нет. В обычных сундуках веса растут с редкостью: Wooden — Uncommon 7, Rare 1000, Legendary 50000; Iron — 7/500/25000. При roulette-трактовке Legendary выпадала бы в ≈98% случаев.
- Одноредкостные паки (GachaRare: RareWeight 1; GachaLegendary: LegendaryWeight 1, BaseNumCards 1) согласуются с трактовкой «1 из N».
- В RankMultipliers 12 полей; 5 из них постоянно равны 1: MiningLeaderboardCurrencyMultiplier, три GachaLeaderboard* и GachaSoftCurrencyMultRare. GachaCardsMultNormal в zone1 = 0.1, а scg_m001_exit даёт ca001×1, и 1×0.1 → 0 карт.
- GachaType: 0 (Wooden…Golden, Crusher), 1 (Royal, Mythical), 2 (45 scg_* и GachaLegendary), 3 (Epic, LongEpic, Rare, Uncommon). Мелкая поправка к verified_facts: скриптовых scg_* 45, а не 46.
FIX: Заменить абзац 10.3 о весах: «Roulette-трактовка весов опровергнута данными: поля CommonWeight нет, а веса монотонно растут с редкостью. Рабочая интерпретация (в реестр): шанс ≈ 1/Weight для каждой карты, Common — остаток. Учитывать LowCardChanceMult и ManagerChanceMult как неподтверждённые параметры.»
Добавить: «Зональные множители Gacha{Cards,SoftCurrency}Mult{Normal,Premium,Rare} не применять к GuaranteedCardCounts и фиксированному эликсиру скриптовых наград (GachaType 2): в zone1 GachaCardsMultNormal = 0.1 обнулил бы награду. Соответствие GachaType ↔ Normal/Premium/Rare — открытый вопрос (вероятно 0→Normal, 1→Premium). Leaderboard-множители не используются.»

## DATA-16 minor omission | 10.5
Q: Его сила рассчитывается по `Spells` и расшифрованной логике уровня, а не по нарисованному номеру камня.
P: Количество и уровень динамита в данных не заданы: все Quantity-коэффициенты динамитных доставок равны нулю. У Spells нет Id, через который можно сослаться на запись. Без этого исполнитель придумает количество зарядов или сочтёт награду пустой.
E: - Deliveries 16–21: RewardModel {RewardType 9, DetailedType 'Dynamite', Quantity 0}; QuantityBase, Multiplier, Growth, Power и Round = 0 при FT3. Weight 15/30/40/5/20/10, MaxDupes 3–5, RankUnlock 9/9/8/8/7/6, CheckPointUnlock = 1 у id 16/18/21.
- Spells: одна запись без Id — SpellType 1, Effect 750/300/2.1 FT1 (тот же Growth 2.1 и FT1, что у Miners.Damage), CritChance 0, CritPower 2.
FIX: Дополнить 10.5: «Критерий доставки — `Deliveries[].RewardModel.RewardType == 9 && DetailedType == 'Dynamite'` (id 16–21, все не рекламные, RankUnlock 6–9, у id 16/18/21 CheckPointUnlock = 1). Количество зарядов в данных не задано (все Quantity* = 0). Правило прототипа — 1 заряд за доставку, в реестр. Параметры силы — единственная запись Spells[0] (SpellType 1; Effect 750/300/2.1 FT1, крит 0/×2). Связь Dynamite → SpellType 1 и аргумент уровня (вероятно, тот же, что у Miners.Damage) — неподтверждённые интерпретации.»

## DATA-17 minor improvement | 15
Q: Если реальные вычисления превышают его диапазон, заменить внутреннее представление этого слоя на mantissa/exponent без переписывания UI и систем.
P: Данные позволяют сказать определённее, где double достаточен, а где нет. Достижимые значения ограничены CoreCurrencyMax = 1e200, но стоимость улучшения поздних шахт на недостижимых уровнях (до 1200) выходит за 1.8e308. Такие уровни вычисляются в «До порога», в предпросмотре и в инструментах «выдать уровень/валюту».
E: - Максимальный HP — 1.87e25; базовый выход sunstone — 7.39e35; произведение всех CoreCurrencyMultiplier — 1e119…1e154; AntiCheatSettings.CoreCurrencyMax = 1e200.
- При UpgradeCostBase·Growth^(L−1) стоимость превышает 1e308 у 9 шахт: diamond при L≈1182, garnet ≈1157, … sunstone ≈1043. Порог 1e200 при этом достигается уже при L≈654 (sunstone) … 750 (diamond).
- Порядок величин от сдвига индекса на 1 не зависит.
FIX: Дополнить раздел 15: «Достижимые значения валюты не превышают CoreCurrencyMax = 1e200, поэтому double допустим как хранилище. Но FormulaEvaluator обязан насыщать результат: стоимость выше CoreCurrencyMax или бесконечная считается «недоступно», а не NaN или Infinity. Для 9 поздних шахт (diamond…sunstone) формула стоимости при уровнях около 1040–1200 выходит за диапазон double. Тест №11 должен включать расчёт «До порога» и инструмент выдачи уровня для sunstone на уровне 1200.»

