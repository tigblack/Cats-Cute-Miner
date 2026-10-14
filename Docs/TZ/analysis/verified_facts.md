# Проверенные факты по json-data.json (сняты скриптами в этой папке)

JSON: /home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json
ТЗ v1.1: /home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-5f06e1ba-754a-49a2-82da-b23da5793978-md.md
Скрипты: python3 <script>.py <JSON>

## Подтверждено (совпадает с ТЗ)
- SHA-256 8975a5a0…85ee5, размер 2 254 970 байт, Id = evergreen.
- 144 зоны, ширина 7, глубина 12..64; в каждой ровно один `e` и один `c`; GridRowRanks=null, FogOfWarCheckpoint=0, RankMultipliers из 1 объекта; DecoSeed=0 во всех зонах.
- Токены только `. r s x p e c`; поля: r=3, s=6, x=2, p=4, e=4, c=3. Ссылки всех токенов на Rocks/MineShafts/Obstructions/Gacha/Cards находятся. Все уровни в HP-таблицах (макс. уровень цели 71 при 99 значениях).
- Footprint `+column / −row`: 0 пересечений и 0 выходов за границы; другие 3 направления: (+c,+r) 3372/142, (−c,−r) 2257/2269, (−c,+r) 3531/2411 (пересечения/выходы).
- Приложение A (zone1..zone10) совпадает. HP контрольные значения совпадают.
- zone2 amethyst якорь (0,5), занимает cols 0–1 rows 3–5. zone1 block2x2 якорь (0,1) → cols 0–1 rows 0–1.
- Cards 61, IsManager 40 (все с StatModifierType 9, ModifierFormulaType 1, Base 0, Mult 1, Growth 3, TargetIds из 1 элемента). Rarity manager-карт: 1 (21 шт.) и 2 (19 шт.).
- CardUpgradeCosts: rarity1 13 шагов, rarity2 11, rarity3 8, rarity4 6, rarity5 4, rarity6 4.
- Требуемый уровень manager-карт в `s`: макс. 14 (rarity 1, 20 карт), 12 (rarity 2, 15 карт), 11 (rarity 2, 4 карты) → согласуется: rarity1 max lvl 14 = 1+13, rarity2 max lvl 12 = 1+11.
- Все `s` ссылаются на manager-карту, у которой TargetIds содержит ID этой шахты. Каждый тип шахты имеет 2 manager-карты (одну rarity 1 и одну rarity 2), кроме sunstone (1). ca001 (manager, target spawningcart) НЕ требуется ни одним `s`-токеном.
- rockbasic RewardCycle = 500 записей.

## Факты, которых нет в ТЗ / уточняющие ТЗ
- Геометрия особых объектов (выведена по всем 144 картам):
  - `e` всегда (2,0); клетки (2..4, 0) всегда свободны от footprint; (1,0) и (5,0) всегда заняты препятствиями → выход логически 3×1 (cols 2–4, row 0), рабочая сторона — row 1.
  - `p` всегда в col 1; в строке ворот cols 1..5 всегда свободны, cols 0 и 6 ВСЕГДА заняты препятствиями (280/280) → ворота логически 5×1 (cols 1–5), строка полностью перекрыта. 3 строки над воротами (row p−3..p−1) полностью свободны во всех 7 столбцах (площадка следующего сектора).
  - `c` всегда col 1, row = Depth−4 (142 зоны) или Depth−3 (2 зоны); строки c−3..c+1 полностью свободны во всех 7 столбцах.
- Число ворот на зону: 0 (6 зон), 1 (35), 2 (64), 3 (39) → 424 сектора. В каждом секторе свободные клетки образуют ровно 1 связную (4-соседство) компоненту — «карманов» нет.
- Проходимость: при 4-соседстве и расчистке от стартовой площадки все r/s/p/e во всех 144 зонах достижимы.
- Шахты `s`: все 1232 используют footprint 2×3 (все 20 типов MineShafts 2×3).
- Gacha-ссылки: `s` → `GachaIron` (1216) или scg_m0XX_sYY (16 скриптовых); `p` → `GachaForged` (275) или scg_m0XX_c01 (5); `e` → scg_m001..m010_exit (zone1..10), затем `GachaCrusher` (134 зоны, zone11+).
- Все scg_*_exit имеют MimicId=GachaCrusher, CardMultMax 0.3..0.95, SoftCurrencyMin<SoftCurrencyMax (17..100 → 250), непустой GuaranteedCardMin (< GuaranteedCardCounts). У ВСЕХ остальных scg_* GuaranteedCardMin пуст и SoftCurrencyMin=Max. Т.е. Min/Count-диапазон и CardMultMax присутствуют только у наград выхода → вероятно связаны с результатом мини-игры Crusher (CrushableRewards: CardMult/SoftCurrencyMult за раздавленные объекты).
- GachaType: 0 (6: Wooden, Iron, Forged, Silver, Golden, Crusher), 1 (Royal, Mythical), 2 (45 скриптовых scg_* + GachaLegendary = 46), 3 (Epic, LongEpic, Rare, Uncommon). Не используются зонами/камнями: GachaEpic, GachaLongEpic, GachaRare, GachaUncommon, scg_free_01, scg_free_02 (последние два — в FreeGachaCycle).
- Gacha веса растут с редкостью (Wooden: Uncommon 7, Rare 1000, Legendary 50000) → похожи на «1 из N», а не веса рулетки.
- RewardCycle камней: RewardType 0 = валюта (DetailedType Core/Soft/Hard/BattlePass), RewardType 1 = сундук (DetailedType = Gacha Id). Core/Soft/Gacha имеют Quantity 0, Hard — явные 1/2/3/10, BattlePass — 1.
- rockbattlepasscurrency (награда BattlePass, исключённая система) стоит на картах 72 раза, начиная с zone7.
- rockbasic: максимум 117 на одну зону, впервые в zone6; накопленно 500 штук набирается только к zone16 → 500-элементный цикл не может быть пройден счётчиком «в пределах зоны»; данные указывают на счётчик на тип камня, живущий между зонами.
- Onboarding-камни: rockonboardingmine1..5 используются только в zone1..zone5 соответственно (7/11/17/24/33 шт.), длины циклов 1/8/19/27/33. MimicId onboarding-камней = rockbasic/rockgacha*, но у них собственные RewardCycle (scg_m00X_*) и собственные HP-таблицы, совпадающие по значениям с rockbasic.
- CoreCurrencySeconds* заданы (FormulaType 3: Base 10, Mult 635, Growth 0.2, Power 1.15, Round 15) у rockbasic и onboarding; у остальных камней нули.
- Формулы: используются FormulaType 0 (линейная по виду), 1, 2, 3. FT3 имеет поля Base/Multiplier/Growth/Power/Round. FT2 встречается у UpgradeCost (Mult=0) и DeliveryDelaySec (Base 22, Mult 1, Growth 1.02).
- GeneratorObjectives: поле множителей называется `CoreCurrencyMultiplier`; у ВСЕХ 21 генераторов сумма ObjectiveCount = 1199 (длины массивов 120/61/31) → при старте с уровня 1 последний порог = уровень 1200 (вероятный максимум уровня генератора). Есть SoftCurrencyReward{Base,Multiplier,Growth,FormulaType=0} — награда эликсиром за порог; в зонах GenObjectiveSoftCurrencyMultiplier.
- Числа: стоимость улучшения при гипотезе Base·Growth^(L−1) на уровне 1200 достигает 1e188 (cart) … 1e352 (sunstone) → выход за double (1.8e308) для поздних шахт; произведение всех пороговых множителей 1e119..1e154; базовый выход sunstone 7.39e35; макс HP 1.87e25; CoreCurrencyMax 1e200.
- MineShafts: CurrencyType = 1 у всех и у SpawningCart → 1 = основная валюта (gold) практически однозначно.
- Deliveries (21): RewardType 4 «tinygoblin» (id 1–5; QuantityBase +2 у рекламных, −2 у обычных, Round ±1), RewardType 0 Soft (6–9), Core (10–15), RewardType 9 Dynamite (16–21, все не рекламные, RankUnlock 6–9, CheckPointUnlock 0/1). IsAd=true: id 1, 2, 6, 10. MaxDupes = −1 у id 15. RankUnlock 4..9.
- BalanceProperties, не упомянутые в ТЗ: DeliveryDelaySecMultiplier 1, DeliveryDelaySecGrowth 1.02, DeliveryDelaySecFormulaType 2, DeliveryDelaySecVariance 0.1, DeliveryStartMineShaftNum 2, DeliveryStartRank 4, DeliveryForeverAloneDelta 2, CardStackCounts, *StackSplitAt, RankUpType 1, CoreCurrencyId gold, SoftCurrencyId elixir.
- FreeGachaCycle: StartRank 4, StartMineshaftNum 2, MaxFreeGachas 2, Hours 4, 377 GachaIds (начинается со scg_free_01, scg_free_02). В ТЗ не упомянут вообще.
- Spells: 1 запись, SpellType 1, Effect Base 750 / Mult 300 / Growth 2.1 / FT1, крит 0/×2.
- CheckPoint: StatModifierType [10,2,7,1], ModifierBase [2,10,60,1]. Карты с теми же кодами: 10 → ca034 (rarity5), 7 → ca029 (rarity3, Mult 60, FT0), 1 → ca038 (rarity5). Код 2 ни одной карте не соответствует.
- Корневые разделы, не перечисленные в таблице ТЗ: CrushableRewards (10), FreeGachaCycle, StoreItems (183, ID вида com.redcellgames.goldandgoblins.*), CostConversions, Bundles (107), RewardWheel, Spells, Achievements (18), ProgressiveOffers (13), Quests (47), MultiOffers (12), ProgressiveOffersABTest, MultiOffersAbTest, BundleReplacementsAbTest (52), BattlePassRegions (8), BattlePassBonusRewards (15), а также Reinforcements, SpawningCart, CheckPoint, Exit({}).
- Rank: RankMultipliers задаются на зону; DeliveryStartRank 4, FreeGachaCycle.StartRank 4, RewardWheel.ActivationRank 5, BattlePass.ActivationRank 7, Quests.Rank — «ранг», вероятно = номер зоны (не подтверждено). Cards используют отдельное поле MineUnlock (1..138); у 36 из 39 требуемых manager-карт оно равно номеру первой зоны с требованием этой карты (у ca002, ca014, ca015 — 1).
- RankMultipliers меняются по зонам (пример GachaCardsMultNormal: z1 0.1, z5 0.325, z10 1.0, z144 4).
- Происхождение данных: StoreItems содержит ID `com.redcellgames.goldandgoblins.*` → JSON — конфигурация коммерческой игры Gold & Goblins.

## Unity-проект (фактическое состояние)
- /mnt/claude-data/sargsyan-tinote/cats-cute-miner: пустой шаблон URP blank. Unity 6000.3.11f1 (6.3). Пакеты: URP 17.3.0, Input System 1.19.0, uGUI 2.0.0 (TMP входит в uGUI 2.0), Test Framework 1.6.0, AI Navigation, Timeline, Visual Scripting, Multiplayer Center, Collab proxy. Newtonsoft (com.unity.nuget.newtonsoft-json) НЕ установлен.
- activeInputHandler = 1 (только новый Input System). defaultScreenOrientation = 4 (AutoRotation, нужен Portrait). applicationIdentifier — шаблонный (com.UnityTechnologies.com.unity.template.urpblank). productName "Cats Cute Miner". В Build Settings только Assets/Scenes/SampleScene.unity. AndroidMinSdkVersion 25, scriptingBackend Android = IL2CPP.
- Исходного кода игры, префабов, DTO, enum'ов, редактора уровней, тестов — нет.
