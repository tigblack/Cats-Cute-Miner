# Переработка в CatOutpostV2: ход работы

## Нижняя панель: пять кнопок и замок на Market (16 октября 2026)

- **Состояние / точка продолжения.** Решение владельца 16 октября: кнопка «To base» убрана ради симметрии; панель —
  Store · Cards · **Base** (ровно по центру) · Equipment · Market; до первой шахты с заводами на Market вместо весов замок
  `icon_lock`, тап — тост «Market unlocks at mine 2». Ничего не закоммичено и не опубликовано.
- **Код:**
  - `OutpostHud.cs`: без `m_ToBaseButton`, `OnToBase`, `TargetToBase`; боковые кнопки снова 40 пикселей арта с зазором 3 (как до
    заводов), «Base» между `BarPad + 2 × (40 + 3)` с обеих сторон — по центру (≈55 пикселей арта на 1080×2400, ≈84 на 1080×1920).
  - `OutpostHud.Market.cs`: Market — крайняя правая; `RefreshMarketButton` ставит `icon_lock`, пока шахта раньше
    `MarketUnlockZone` (состояние кнопки — бит 4, иконка меняется только при смене), `OnMarketButtonDenied` → `outpost.market.locked`.
    В шахте без заводов после шахты 2 (сейчас таких нет) и в `ReadyToContinue` — прежние причины.
  - `IOutpostGame.MarketUnlockZone` / `OutpostController.MarketUnlockZone`: первая шахта, на карте которой есть заводы
    (`OutpostLevelAdapter.BuildMine`, источник + override + топология), считается один раз при первом запросе.
  - `Loc.Outpost.cs`: новый `outpost.market.locked` «Market unlocks at mine {0}» (как «Unlocks at mine {0}» у карточек), ключ
    `outpost.bottom.to_base` удалён. `IOutpostGame.FocusBase` оставлен (тесты и код); камера к лагерю — прокруткой.
- **Тесты (правки с комментарием-ссылкой на решение 16.10.2026):** `OutpostStoreUiTests.BottomBar_StoreCardsBaseMarketEquipmentToBase`
  → `BottomBar_StoreCardsBaseEquipmentMarket` (пять кнопок по порядку, «Base» по центру панели ±1 px, замок на Market в шахте 1);
  `OutpostMarketUiTests.Mine1_…` — `MarketUnlockZone == 2`, замок и тост «Market unlocks at mine 2» вместо «No factories in this
  mine»; `Mine2_…` — с шахты 2 весы вместо замка; `OutpostHudTests.FakeOutpostGame.MarketUnlockZone`.
- **Документы:** ТЗ 2.2 §6.2 и строка Р5, `UI_API.md`, `RUNTIME_SPEC.md`, `ART_NAMES.md`.

## ТЗ 2.2: правки по ответам владельца и темп b6 (15–16 октября 2026)

- **Состояние / точка продолжения.** Ответы владельца 15 октября внесены: правки интерфейса и профиль темпа
  `outpost-2.2-b6` сделаны, Unity компилируется без ошибок, затронутые тесты проходят. Ничего не закоммичено, не запушено и
  не опубликовано; `json-data.json` не менялся. Дальше — проверка владельцем (Validate, BuildPlayable, полный PlayMode) и
  ответы на вопросы В6–В8 ниже.
- **Решения владельца 15 октября 2026** (ТЗ 2.2 §1.2, таблица «Ответы на вопросы реализации»; В1–В5 —
  `Docs/Rework/BALANCE_FACTORIES.md`):
  - В1 (шахта 37, seed 4 — 121,3 с без покупки): оставить, ограничение проверяется на seed 1;
  - В2 (автодоставка у 0,9 % заводов): оставить уровни карточек;
  - В3 (товар пропадает при завершении шахты): оставить (Р3);
  - В4 (длинные циклы не окупаются): шахта длится около трёх циклов самого медленного завода — темп b6;
  - В5 (`PriceRate = 4`): оставить;
  - приняты перенос 17 заводов, отсутствие склада в шахте 1, звуки, фонари, трубы, цвет HP цепей и подписи кнопок; G5
    (картинки и снимки) утверждён;
  - «Deliver» в тесноте сокращается, подъём на один ряд над заводом остаётся; недоступная «Sell all» — просто серая; рынок —
    окно.
- **Интерфейс** (`FactoryOverlay.cs`, `OutpostHud.Market.cs`, тесты `OutpostMarketUiTests.cs`,
  `OutpostEnglishUiEvidenceTests.cs`, `Docs/Rework/UI_API.md`):
  - кнопки «Deliver» в тесноте — «×N»;
  - исправлено отставание оверлея от камеры. Холст ScreenSpaceCamera переезжает за камерой только при отрисовке, а
    `FactoryOverlay.ToLocal` бросал луч на прежнюю плоскость холста. Теперь точка переводится через прямоугольник корневого
    Canvas, добавлен `[DefaultExecutionOrder(100)]` (после CameraRig). Правило раскладки и тест
    `DeliverButtons_TopEdgeAndCameraSweep_StayOverOwnFactory` не менялись — после исправления он проходит;
  - рынок — окно `PixelUi.Window` «Market · Warehouse» между верхней полосой HUD и нижней панелью, высота по числу строк,
    прокрутка, «Sell all» внизу рамки, Back и крестик закрывают окно;
  - недоступная «Sell all» — серая кнопка без причины и без тоста; строки «Sell» по-прежнему называют причину;
  - правки тестов (с комментарием-ссылкой на решение 15.10.2026): путь шапки `Box/header/title` и строки
    `Box/body/viewport/content/good0`. Вместо тоста «Nothing to sell» проверяются серая «Sell all» без тоста
    (`AssertSellAllGray`) и рамка окна (`AssertMarketIsWindow`). Добавлен снимок `f17-market-nothing` (9:16, 9:19,5, 9:20).
- **Темп b6** (ТЗ 2.2 §9.1, ТЗ 2.1 §12.6, `BALANCE_FACTORIES.md` — «Темп b6: 3 цикла»):
  - `outpost_balance.json` — `Version outpost-2.2-b6` и `PaceScale` (144 дроби K(z): шахты 1–11 — 1, шахты 12–144 — от 1,92
    до 250,68). `BaseCost`, `Growth`, `Cap` веток, циклы и цены товара не менялись. Встроенный
    `OutpostConfigSet.Defaults.cs` сгенерирован во временный файл под `flock Temp/gen_defaults.lock` — совпадает побайтно;
  - ядро:
    - `OutpostBalanceConfig`: `PaceScale`, `ZonePace`, `PriceScale(z) = S·CostZoneFactor·K`, `WithoutPace`, проверка `K ≥ 1`;
    - `OutpostConfigSet.WithoutPace`;
    - `OutpostLevelBalanceAdapter.ApplyPace` (HP r/x/p × K, груз от нового HP, цепи `s` без изменений), вызов в
      `StageProgressionService.CreateRun` на снимке `Run.Balance`;
    - `TeamUpgradeService.NextCost` через `PriceScale`;
    - `OutpostIds.BalanceVersion`;
    - `OutpostBot.PaceLimitFactor` (пределы бота и `BalanceAudit` × ceil(K)), `BalanceAudit.RunMineFrom`;
  - инструмент `Tools/BalanceAudit/pace_scale.py`, генератор `OutpostBalanceAuditTests.Audit_Pace`, данные
    `Docs/Reports/balance-factories/pace/`. Три круга калибровки: `k0`/`kc13` → `r2` → `r3`;
  - итог `r3` (`free` seed 1): кампания 49,1 дня (b5 — 19,63 ч); 133 шахты с K > 1, `D / 3C` 0,95–1,05 (медиана 1,00);
    последняя покупка не раньше 86 % D; без доступной покупки — не больше 13,8 % D. Числа раздела BALANCE_FACTORIES сверены с
    `pace_scale.py summary r3` и `report r3` — совпадают, правок не понадобилось;
  - тест темпа `Campaign_FreePlayer_PacingWithinStageTargets`. Кампания на K = 1 возвращает ориентиры этапов §12.6, предел
    2,2 для шахт 1–11 и `MaxUnaffordableSec < 120`. Шахты 19, 31, 47, 68, 93, 114, 134 и 144 проверяются на b6: `D / 3C`
    0,75–1,25, покупки до 80 % D, отрезок без покупки короче `120·K` и 25 % D, товар продаётся. Новый тест слоя K —
    `Pace_B6_RunTargetsAndPrices`;
  - пересчитанные старые тесты:
    - эталоны цен умножают на K(z), взятый прямо из массива `PaceScale` встроенного json, мимо `ZonePace`
      (`NextCostOracle`, `ExpectedCost(paced)`). Так пересчитаны `OutpostUpgradeTests.BuyTen_*`, `BuyMax_*` и
      `OutpostMathTests.CostZoneFactor_ExactPrices`, проверки «×10 = десять ×1», «точно сверх 2^53», «cap блокирует» прежние;
    - контрольные числа аудита источника (`NextCost_Zone144_*`) проверяются на `BaseBalance` (без K), число прежнее;
    - тесты правил, которым длительность не важна, идут на `WithoutPace`, пределы 24 ч / 2 000 000 событий и все проверки
      прежние, причина — K(72) ≈ 48 и K(144) ≈ 151 растягивают шахту за эти пределы. Это `ConcurrentWorkers_…(72)`,
      `All144Mines_*`, `BranchingCampaign_*`, `OutpostCoreSmokeTests.AllZones_PolicyA_Complete`, `OutpostCampaignSlowTests`
      и `AllZones_EveryTargetHasExactHpAndCargo_SumsMatchAudit`;
    - `LateMines_UpgradeTreeNotTriviallyCheap` считает груз после `ApplyPace`, как в run;
    - ревью 16 октября: ослабленных несвязанных проверок не нашлось.
- **Ревью диффа 16 октября:**
  - проверены детерминизм (K — точные дроби, применяется один раз при создании run), снимок `Run.Balance` (цены и пределы
    берутся из него), совпадение Defaults с Resources и граничные шахты. Шахты 1 и 11 — K = 1; 12 — первая с K = 2,44; 144 —
    151,48. Шахт без заводов с K > 1 нет: при `C = 0` формула даёт K = 1;
  - исправлено:
    - DEV «Finish stage» (`OutpostController.CompleteStageNow`) ограничивал прогон 24 ч симуляции — глубокие шахты b6 за это
      время не завершались. Теперь предел × `OutpostBot.PaceLimitFactor`, как у бота;
    - в `OutpostBot` осиротевший комментарий `<summary>24 часа симуляции</summary>` возвращён к `DefaultTickLimit`;
  - замечено без правки: playable копирует профиль b6 вместе с `PaceScale`. Шахты 1–11 там с K = 1, но игрок, дошедший в
    playable до шахты 12+, получит K > 1 (в основной игре у `free` шахта 12 — 12,1 мин против 6,2 мин в b5). Версия playable осталась
    `outpost-2.2-b5-playable` (связано с В8).
- **Прогоны:**
  - UI: Unity `Compile` — 0 ошибок CS; PlayMode `OutpostFactoryViewTests|OutpostMarketUiTests|OutpostFactoryScreensEvidence|
    OutpostEnglishUiEvidenceTests|OutpostHudTests` — **24/24** (210 с); повтор `OutpostMarketUiTests|OutpostEnglishUiEvidenceTests`
    после переноса рамки — **11/11**, обрезанных подписей 0;
  - ядро темпа: фильтрованно 177/177, полное ядро **2 635 / 2 635** за 29,1 мин (`Docs/Reports/gate3/pc-core2.txt` на ПК);
    `Audit_Pace` r3 — 9,4 мин;
  - 16 октября: Unity `Compile -Target Ci` с ядром темпа (`r2-compile`) — exit 0, **0 ошибок CS**. После двух правок выше —
    `r2-compile2`, exit 0, 0 ошибок CS (пересобраны `CatMiners.Core` и `CatMiners.Runtime`). Фильтрованный прогон ядра
    `Pace_B6|OutpostUpgradeTests|OutpostBotTests` (`r2-core`) — **30 из 30**, 0 упали.
- **Не запускалось по просьбе владельца** («добавь изменения, я сам проверю»): `OutpostValidatorCli` (144 шахты),
  `Tools/unity.ps1 -Mode BuildPlayable` и полный PlayMode. PlayMode после темпа b6 не запускался: UI-прогоны 24/24 и 11/11
  шли до сборки с ядром темпа. Тесты PlayMode с `CompleteStageNow` работают на шахтах 1–2 (K = 1), шахта 140 в них — снимки и
  раскладка без завершения, поэтому влияния b6 там не ожидается.
- **Открытые вопросы владельцу:**
  - **офлайн-предел** (В7). `OfflineCapMs` = 3 ч и жёсткий `OutpostIds.MaxOfflineMs` = 3 ч, а шахты 12–144 теперь идут
    0,5–24 ч. Ночь даёт только 3 ч, `OfflineHours` карточек предел не продлевает, офлайн не покупает улучшения, тайм-скипы
    10/30/60 мин против шахт в 6–24 ч почти ничего не дают. **Рекомендация:**
    - поднять общий предел до 12 ч (`OfflineCapMs` и `MaxOfflineMs`), чтобы `OfflineHours` карточек его продлевал;
    - разрешить офлайну покупки по правилу модели `free`;
    - для шахт с K > 1 вернуть длинные скипы (4 / 12 ч);
  - В6 — самый медленный завод открывается на 61–99 % шахты и в 127 из 133 шахт не успевает ни одного цикла. Рекомендация —
    перенести его цепь в первый сектор;
  - В8 — шахты 7–11 и скачок 11 → 12 (3,0 при пределе 2,2);
  - кнопка «Deliver» закрывает примерно 80 % ряда над полосой цикла: «не залезает на камни» — это «не выше одного ряда» (как
    сейчас) или «вообще не на клетках с камнями»?
- **Выделения памяти Editor:** рост суммарного GC Editor (717–732 КБ на кадр против 106–116 КБ 9 октября) с заводами не
  связан. Он есть и в шахтах 50/101/144 с прежним тестом, узкие проверки «0 байт» (HUD, рынок, Deliver) проходят. Это
  счётчик всего Editor вместе с test runner; рост зависит от окружения прогона (`qualityLevel` 0 → 1, нагрузка ПК) и
  способа замера. Источник искать профайлером.

## ТЗ 2.2: заводы вместо месторождений — итог этапов 2 и 3, финальная интеграция (Claude, 15 октября 2026)

- **Состояние.** ТЗ 2.2 (`Docs/TZ/Cat_Miners_Factories_TZ_v2.2.md`) внедрено целиком: пакеты WP0–WP9 выполнены, ворота G1–G3
  пройдены. G4 (баланс): калибровка в пределе, одно исключение вынесено владельцу (В1 ниже). G5: снимки и сводные листы
  готовы и ждут утверждения владельца. G6: документы обновлены. Ничего не закоммичено, не запушено и не опубликовано; playable
  не публиковался; `json-data.json` не менялся; обучения нет.
- **Этап 2** (подробно — записи ворот G3 и WP7 ниже):
  - доработки ядра по ревью этапа 1: единственная функция выпуска `FactoryMath.UnitsPerCycle` (копии в `TeamStats` удалены,
    сканер `Units_SingleRuleFunction_NoCopiesInGameCode`); цикл есть только у `Working`; `FactoryCatalog.Build` проверяет
    порядок товаров; `review_cards.py --check` строже;
  - WP5a — вид заводов: цепи с трещинами, кадры работы, бар цикла, AUTO, трубы и жгуты, капсулы (пул 60), склад, тапы,
    `FactoryOverlay` с кнопками «Deliver ×N» и стрелками у края, тосты, звуки;
  - WP5b — HUD и рынок: шесть кнопок нижней панели, окно Market, сведения завода, тексты карточек заводов, строка «Goods sold»
    в итогах;
  - WP9, часть 1 — перенос ТЗ 2.2 в ТЗ 2.1 (список §0), PIXEL_STYLE_GUIDE, ART_NAMES, MINE_TOPOLOGY, спецификации;
  - ворота G3 — сведение в Unity, фонари лагеря в `b ± 3`, раскладка кнопок «Deliver» без наложений, тест сохранения схемы 2,
    54 снимка шахт.
- **Этап 3:**
  - WP7 — бот и модели аудита доставляют и продают товар (`OutpostBot.TendFactories`), калибровка `PriceRate`, замер b5,
    тест темпа по §9, `PlayableBalance.Version = outpost-2.2-b5-playable`, отчёт `Docs/Rework/BALANCE_FACTORIES.md`;
  - финальная интеграция (эта запись), список ниже.
- **Сделано на финальной интеграции** (запросы WP7 и G3):
  - ТЗ 2.2: приложение B п. 6 пересчитано под `PriceRate = 4` (молоко `8/5`, 10 золота за единицу в шахте 2, партия — 200;
    сталь `120`, 720 золота); в §3.2 — итог калибровки; в §7.1 фраза «эффект считается в `TeamStats`» заменена на
    `FactoryMath.UnitsPerCycle` (запрос ревью ядра); в §0 — статус реализации. ТЗ 2.1 §12.7 не менялся: правило `PriceFactor`
    и `UnitsPerCycle = 20` прежние (запрос WP7);
  - `Docs/Playable/PLAYABLE_TZ.md`: §3.2 — версия `outpost-2.2-b5-playable`, §5 — перемер на b5 (таблица из BALANCE_FACTORIES);
  - `PlayableTests.cs:25`: в комментарии профиль `outpost-2.2-b5-playable` (проверки и раньше брали `PlayableBalance.Version`);
  - старые расхождения из итогов WP9, часть 1: ТЗ 2.1 §16 — «все семь столбцов видны» заменено на «около десяти столбцов
    широкой карты» (§8.5); PIXEL_STYLE_GUIDE §6.3а «Ячейка карты» — по `CardView` (126 UI-пикселей, один запечённый арт
    48×44 на фоне 64×48, порядок строк);
  - **§15 «Производительность» на воротах G3 не выполнялся** — сделано: в `OutpostPerformanceTests` добавлена шахта 140
    (тест переименован в `EditorStress_Zones50_101_140_144_…`, тайм-аут 600 → 900 с). Все 15 заводов открыты и на автомате
    (DEV «Unlock factories», «Auto all»), перед замером DEV «Finish factory cycles». В начале выборки в трубах 14 капсул.
    Прежний отчёт сохранён как `Docs/Reports/outpost/performance-editor-pre-factories.json`, итог — VALIDATION_V2.md;
  - генераторы `OutpostEnglishUiEvidenceTests` перезапущены (запрос G3): снимки `f01–f16` теперь с новой раскладкой стрелок и
    кнопок «Deliver»;
  - встроенные конфиги, палитра и атлас не пересобирались — нечего: генерация `OutpostConfigSet.Defaults.cs` и
    `PixelPalette.g.cs` во временный файл совпала побайтно; после атласа `0befef2876ae0bde` менялись только описание группы в
    `manifest.py` и проверка `review_cards.py` (на атлас не влияют);
  - `Docs/Reports/gate3/wmi-core.ps1` — прогон ядра через WMI, как `wmi-run.ps1` у Unity: обрыв ssh его не убивает. Блокировку
    `Temp/unity.lock` держит хаб. Ядро и Unity шли по очереди, одновременно не запускались.
- **Итоговые проверки:**
  - ядро без фильтра (`run.ps1 -Instance final`): **2 634 теста, 2 634 прошли, 0 упали**, 8 Explicit пропущены, 9,2 мин.
    В том числе `Campaign_FreePlayer_PacingWithinStageTargets` (37 с), `Rules_Validate_RejectsBrokenRows`,
    `Units_SingleRuleFunction_NoCopiesInGameCode`. У WP7 было 2 635 вместе с черновиком `PlayablePacingDraft`;
  - `gen_factory_rules.py --check` — OK; `review_cards.py --check` — 22/22 прежних арта, атлас содержит все 61 актуальный арт;
  - Unity `Compile -Target Ci`: exit 0, **0 ошибок CS**, 0 предупреждений CS в логе. Пересобрана только `CatMiners.Tests.PlayMode`:
    правку `PlayableBalance.cs` Ci-копия уже собрала на воротах G3;
  - Unity PlayMode одним запуском `PlayableTests|OutpostEnglishUiEvidenceTests|OutpostPerformanceTests`: **35 из 35**, 268 с.
    PlayableTests 29/29, генераторы 5/5, производительность 1/1. В `text.tsv`, `store-text.tsv` и `factory-text.tsv` обрезанных
    подписей нет; «small» — только прежние `ZonePlaque/sector` и `TopBar/cargo/label`;
  - производительность (Editor, мс, медиана / p95): шахта 50 — 16,66 / 17,25; 101 — 16,65 / 17,30; **140 — 16,66 / 17,40**;
    144 — 16,65 / 17,64. Требование §15 выполнено: медиана ≤ 16,7, p95 на 1,9 % выше базы шахты 144 (17,07) при допуске
    10 %. Офлайн шахты 144 — 169,6 мс (бюджет Editor 3 000 мс).
  - Замечание: суммарный счётчик Editor GC вырос во всех четырёх шахтах — 717–732 КБ на кадр против 106–116 КБ 9 октября, по
    2 сборки gen0 за выборку. В том числе в шахтах 50, 101 и 144, где тест прежний. Это счётчик всего Editor вместе с test
    runner, игре его целиком приписывать нельзя. Узкие проверки «0 байт» (HUD, рынок, кнопка Deliver) проходят. Источник
    роста нужно искать профайлером. Между замерами также сменился `qualityLevel` (0 → 1);
  - за этапы 2–3 прогоны Unity по §16.2 (ворота G3): 105 тестов в одном запуске, 104 прошли. Упавший тест схемы 2 исправлен,
    затронутые наборы перезапущены (11/11 и 10/10).
- **Не проверено:**
  - сборка playable `Tools/unity.ps1 -Mode BuildPlayable` (ТЗ 2.2 §14) после ТЗ 2.2 не запускалась. Последняя сборка
    `Builds/Playable` — 14 октября. Это тяжёлая WebGL-сборка, а на ПК свободно около 2 ГБ: у владельца открыт Unity Editor
    (1,4 ГБ), в 19:44 ПК уже перезагружался под нагрузкой. PlayableTests проходят;
  - устройство Android, звуки на слух.
- **Баланс (WP7, `Docs/Rework/BALANCE_FACTORIES.md`):**
  - `PriceRate = 4` (ориентир ТЗ был 5/16); `UnitsPerCycle = 20`, правило `PriceFactor` и цены веток b4 не менялись;
  - медиана шахт 2–5 у `free` по 12 seed — **4,94 мин** при b4 4,96 (−0,4 %, предел 4,22–5,70). В предел попадают значения
    `PriceRate` от 5/4 до 12;
  - 24 кампании по 144 шахты, все успешны (тупиков нет); `free` seed 1: кампания 19,07 → 19,63 ч; `active` 18,45 → 17,96 ч;
    `idle` 35,75 → 37,95 ч;
  - `MaxUnaffordableSec`: у эталона seed 1 максимум 111,4 с (шахта 37), **у seed 4 — 121,3 с** (> 120, В1);
  - доля товара в золоте `free`: 56 % на шахтах 2–10 → 48 / 27 / 12 / 8 % на поздних этапах;
  - заводы типов 8–20 (цикл от 10 мин) у `free` не заканчивают ни одного цикла;
  - автодоставка на шахтах 31–144 — 0,9 % заводов (порог предложений 25 %);
  - ручных доставок у `active` не больше 10,3 в минуту (ориентир 12);
  - при отсутствии 1 и 3 ч шахта всегда завершается без игрока, весь товар за это время пропадает;
  - playable p3 на b5: 11 / 9 шахт за 30 мин (`active` / `free`), как на b4; пауза между покупками у `active` ≤ 20 с.
- **Снимки и листы для владельца (G5):**
  - шахты 2, 10, 30, 80, 140, 144 в 9:16 и 9:20, по пять видов — `Docs/Reports/factories/screens/` (54 PNG и `layout.txt`);
  - английский UI с рынком, сведениями завода и окном карточки завода в 9:16 / 9:19,5 / 9:20 — `Docs/Reports/english-ui/`
    (`f01–f16`, перезаписаны сегодня);
  - сводные листы арта — `Docs/Reports/factories/art/`: `cards.png` (39 новых артов карточек), `factories.png`,
    `factories_anim.png`, `goods.png`, `icons.png`, `pipes.png`; `cards_before.png` — прежние арты для сравнения;
  - графики баланса b4 → b5 — `Docs/Reports/balance-factories/charts/`;
  - PNG лежат на ПК, на хаб не синхронизируются.
- **Вопросы владельцу:**
  1. **Лист карточек** `Docs/Reports/factories/art/cards.png` — утвердить 39 артов карточек заводов (§16.3 «Карточки»).
     Отличия от задания: у Fishery Manager ручной безмен, у Foundry Manager напольные весы; у Steel Mill Foreman очки поверх
     каски, у Apiary Foreman шляпа пасечника поверх каски.
  2. **Звуки заводов не прослушаны.** 11 новых звуков проверены только анализом сигнала. Кроме того:
     - анимация работы у заводов общая, три вида дыма;
     - на вывеске склада ящик с монетой, а не весы;
     - атлас занят на 1 006 из 1 024 строк: следующее крупное добавление поднимет его до 2048×2048 (+8 МБ видеопамяти).
  3. **Перенос 17 заводов.** При исправлении топологии 17 заводов в 15 шахтах переехали в соседнюю камеру через ствол, на 11–29
     клеток. Шахты 6, 7, 13, 23, 37, 102, 118, 122, 126, 132, 133, 136, 139, 140, 142. Если заводы должны остаться в своей
     камере, можно ограничить обмен расстоянием; тогда сработает проверенный запасной путь «прорезать проход».
  4. **Баланс** (подробно — BALANCE_FACTORIES.md, «Вопросы владельцу»):
     - В1: шахта 37 у seed 4 — 121,3 с без доступной покупки. Причина — цепи без груза (`adapter-2`), от `PriceRate` не зависит;
     - В2: автодоставка у 0,9 % заводов на шахтах 31–144; варианты — `ManagerChanceMult`, `LowCardChanceMult`, `ChestChances`;
     - В3: товар пропадает при завершении шахты: 14 % проданного онлайн и весь товар за время отсутствия;
     - В4: длинные циклы (типы 8–20) не окупаются в шахтах по 6–10 мин;
     - В5: при `PriceRate = 4` товар даёт 55 % золота шахт 2–5; можно выбрать меньшее значение из предела (от 5/4);
     - в эталоне аудита `value_cap40_passes_necessary_bound_zone_count` упал с 61 до 0: цепи не дают монет.
  5. **Вид и интерфейс:**
     - кнопки «Deliver» соседних готовых заводов идут «ёлочкой», в 9:20 — тремя ступенями. Оставить так или в тесноте сокращать
       подпись до иконки и «×24»?
     - фонари лагеря у шкафа и стеллажа (`b ± 3`) — подходит?
     - раскладка труб: по оси коридора через 4 px, жгут — больше 7 труб или от 2 в лагере;
     - в шахте 1 нет склада;
     - полоса HP цепей — стандартная золотая, а не красная, как в макете;
     - капсулы в трубах в `ReadyToContinue` скрываются;
     - недоступная «Sell all» показывает причину вместо суммы;
     - подписи боковых кнопок разного размера (18 и 27);
     - панель сведений завода — 76–86 px;
     - строка «Auto delivery: on» после DEV «Auto all» при недостаточном уровне карточки;
     - рынок открывается во весь экран, а не листом снизу.
  6. **Ядро и сохранения** (решения приняты, владелец может отменить):
     - отметка watchdog `LastChangeTick + WatchdogTicks + 1` в `NextEventTick` (записана в ТЗ §4.5);
     - причина `no-factories` проверяется раньше `phase`: в шахте 1 после завершения — «No factories in this mine»;
     - файл будущей схемы рядом с устаревшим даёт `Incompatible`, а не `Outdated`;
     - после сброса гемы, в том числе купленные, и отложенная реклама не переносятся;
     - при ошибке записи архива — `Fatal`, старые файлы не трогаются;
     - сохранение схемы 3 из Editor- и dev-сборок этапа 1 с заводом в `Ready` загрузится как `Corrupt`; выпущенных сохранений
       схемы 3 нет;
     - добавлен ключ `cards.detail.mine_auto_missing` («(not collected)»), в §7.1 его нет.
- **Как продолжить:**
  - решения владельца по вопросам выше (G5 — утверждение снимков и листов);
  - `Tools/unity.ps1 -Mode BuildPlayable`, когда на ПК будет свободна память;
  - профайлер для роста счётчика Editor GC;
  - новый баланс прохождения (Р1) — по данным BALANCE_FACTORIES.md.

## ТЗ 2.2, этап 2: ворота G3 — интерфейс (Claude, агент ворот, 15 октября 2026)

- Этап 2 ТЗ 2.2 сведён в Unity: WP5a (вид заводов), WP5b (HUD и рынок), WP9 часть 1 (документы) и доработки ядра по ревью
  этапа 1. Баланс (WP7) идёт параллельно, его файлы ворота не трогали. Изменения не закоммичены, ничего не опубликовано.
  Прежний агент ворот оборвался вместе с сессией. Его правки проверены и доведены, повторно не делались.
- **Запросы пакетов:**
  - стрелки у края и кнопки «Deliver ×N» (`FactoryOverlay`) не ложатся на открытые сведения завода: низ полосы поля
    поднимается до `Hud.TryGetInfoTop`. Тест — `OutpostFactoryViewTests.InfoPanelOpen_DeliverButtonsAndArrowsStayAboveIt`;
  - коды отказа заводов (`not-ready`, `locked`, `no-goods`, `no-factories`) `OutpostUiText.Reason` передаёт в
    `FactoryText.Reason` (ключи `factories.reason.*`). Любой из двух вызовов даёт один текст;
  - комментарии:
    - `CardEffectKind.FactoryOutput` — «считает `FactoryMath.UnitsPerCycle`; `TeamStats` его не копит»;
    - `OutpostCardText` — ссылка на `CardService.FactoryOutputBonus` и `FactoryMath.UnitsPerCycle` вместо удалённого поля
      `TeamStats`;
  - фонари лагеря `camp_lantern_*` перенесены в передний ряд столбцов `b − 3` и `b + 3`, перед шкафом и стеллажом:
    - раньше они стояли на краях массива, а в картах 25–53 столбцов это порода за лагерем;
    - в картах из 7 столбцов это те же места, что и раньше;
    - `b ± 4` не подошли: в `b + 4` стоит склад товаров, и фонарь закрыл бы его дверь;
    - ART_NAMES §3.2 и §8 поправлены;
  - документы:
    - RUNTIME_SPEC §5: шесть кнопок, Market;
    - RUNTIME_SPEC §9: позиции из вида, `SetSimulation`, ветка `Outdated`, ввод и `FactoryOverlay`, тосты и звук, поля цикла,
      тесты;
    - UI_API §13: причины через `FactoryText`, поля цикла только у `Working`, элементы HUD рынка и сведений, ключи `Loc`, кто
      показывает тосты;
    - VIEW_API §4: приоритеты выбора — котик 0, цель 1, склад 2, база 3;
    - VIEW_API §11: поля цикла, члены `OutpostBoardView`, склад и фонари, `FactoryOverlay`, новые `OutpostViewCue` и поля
      аргументов, методы `Sfx`;
    - SOUNDS.md: настоящие имена методов `Sfx`, флаг `Chains` вместо `HitMaterial.Chain`.
- **Исправлено по снимкам:** у трёх готовых заводов подряд (шахты 30, 80, 140, 144) средняя кнопка «Deliver ×24» целиком
  пряталась под соседней, а стрелка у правого края лежала на кнопке. Здание завода — два столбца, а кнопка шире.
  - Теперь кнопка, которая легла бы на уже показанную, поднимается на ряд над ней (`FactoryOverlay.Unstack`).
  - Порядок — по ID цели, поэтому раскладка не прыгает. В 9:16 кнопки идут «ёлочкой», в 9:20 кнопки шире и встают в три
    ступени.
  - Стрелка у края обходит кнопки (`AvoidButtons`).
  - Новый тест `OutpostFactoryViewTests.DeliverButtons_AdjacentReadyFactories_DoNotOverlap`: шахта 140, поле и HUD рисуются в
    портретную текстуру 1080×1920. Тест проверяет, что кнопки соседей пересекаются по ширине, но не накладываются, и что
    стрелки не лежат на кнопках.
  - Счётчик наложений в генераторе снимков считал в мировых координатах наклонной плоскости холста и всегда давал 0. Теперь
    он считает в пикселях снимка и отдельно считает стрелки поверх кнопок.
- **Правки существующих тестов.** У каждой правки комментарий-ссылка на ТЗ 2.2, ни одна проверка не ослаблена.
  - Ядро, ревью этапа 1:
    - `FactorySimulationTests.Units_BothCardsOfType_EveryUpgradeAddsAtLeastOne_ControlValues` — сверка «`TeamStats` = прямой
      расчёт» заменена сверкой единственной функции выпуска с независимым floor-ожиданием; добавлена проверка, что чужой тип
      не меняется (§4.4);
    - `FactoryCatalogTests.State_FactoryFields_RoundTripByteIdentical` — невозможное состояние «Ready + Automated + цикл»
      разделено на допустимые `Working`+AUTO и `Ready` с −1 (§4.3);
    - `FactoryCatalogTests.Math_UnitsPriceTransit_ControlValues` — поправлен только комментарий;
    - новые тесты: `Ready_HasNoCycle_AfterFullCycleAndDevFinish_AutoRestartsFullCycle`,
      `Catalog_Build_RejectsGoodsOutOfMineShaftsOrder`, `Units_SingleRuleFunction_NoCopiesInGameCode`.
  - WP5b: `OutpostStoreUiTests.BottomBar_StoreCardsBaseEquipmentToBase` переименован в
    `BottomBar_StoreCardsBaseMarketEquipmentToBase` (§6.2, §16.2):
    - проверка порядка не ослаблена;
    - добавлены проверки: ровно 6 кнопок, «Base» не уже 44 px, подписи не мельче `FontSmall` и без многоточия, Market
      неактивна в шахте 1;
    - новый генератор `OutpostEnglishUiEvidenceTests.FactoryScreens` дописывает свои строки в `text.tsv`.
  - Ворота: помощник выпуска в `OutpostCardTests` вызывает `FactoryMath.UnitsPerCycle(CardConfigs.Factories.Get(typeId).Rule,
    profile, Cat)` и не повторяет формулу.
- **Новые PlayMode-тесты и генераторы ворот:**
  - `OutpostRuntimeTests.Schema2Save_LoadStartsNewCampaign_NoError_ResetToast` (§10.3, §16.2). Файл схемы 2 ядро узнаёт как
    `Outdated`. Загрузка сцены даёт новую кампанию схемы 3, фаза не `Error`, есть тост `outpost.toast.save_reset`. Архив
    `pre-factories/` совпадает с прежним файлом байт в байт, bak = main. Настройки и сыгранное время переносятся; гемы, детали,
    предметы и пройденные шахты — нет. При первом прогоне тест упал на своей же проверке «RunId другой»: у новой кампании RunId
    снова `run-1`. Теперь новый run узнаётся по времени шахты;
  - `OutpostFactoryViewTests.InfoPanelOpen_DeliverButtonsAndArrowsStayAboveIt` и `…DeliverButtons_AdjacentReadyFactories_DoNotOverlap`;
  - `OutpostFactoryScreensEvidence` (`[Explicit]`) — снимки для владельца, см. ниже.
- **Проверки:**
  - Unity `Compile -Target Ci`: exit 0, 0 ошибок.
  - Один прогон PlayMode со всеми наборами §16.2 и генератором снимков: **105 тестов, 104 прошли, 1 упал** (новый тест схемы 2,
    см. выше), 519 с:
    - OutpostRuntimeTests 30/31, OutpostHudTests 2, OutpostStoreUiTests 5, OutpostCardsUiTests 4, CameraExplorationTests 4,
      MineTopologyRuntimeTests 2, CoreGameSmokeTests 10, EdgeIndicatorTests 3, PlayableTests 29, OutpostFactoryViewTests 8,
      OutpostMarketUiTests 6, OutpostFactoryScreensEvidence 1.
  - Перезапуск после правок (только затронутое): `OutpostFactoryViewTests|OutpostRuntimeTests.Schema2Save|OutpostFactoryScreensEvidence`
    — 11/11. После поправки допуска раскладки `OutpostFactoryViewTests|OutpostFactoryScreensEvidence` — 10/10.
  - Наборы, кроме этих, после правки `FactoryOverlay` не перезапускались. Правка меняет только высоту кнопок, которые
    накладывались бы друг на друга.
  - Генераторы `OutpostEnglishUiEvidenceTests` ворота не перезапускали. WP5b прогнал их до правки стрелок: обрезанных подписей
    нет в `text.tsv`, `store-text.tsv` и `factory-text.tsv`. Снимок `f06-factory-ready-*` ещё со старой стрелкой.
  - Ядро не запускалось: ворота не меняли код ядра.
- **Снимки для владельца** — `Docs/Reports/factories/screens/`, PNG на ПК и на хабе:
  - шахты 2, 10, 30, 80, 140 и 144 в 9:16 (1080×1920) и 9:20 (1080×2400);
  - на каждую шахту: `a-locked` (ближайший завод в цепях), `b-ready` (самое плотное место с кнопками «Deliver»), `c-base`
    (лагерь, жгут, склад, фонари), `d-mid` (заводы на AUTO, капсулы) и `overview` (вся карта сверху);
  - всего 54 PNG и `layout.txt`;
  - сводка: заводов и труб — 1/1, 5/5, 9/9, 12/12, 15/15, 14/14; участков жгута — 0, 7, 9, 37, 24, 30; муфт — 0, 1, 1, 2, 2, 3;
    наложений кнопок и стрелок на кнопки — 0 во всех шахтах. Просмотрены все.
- **Как запускалась Unity.** Связь хаба с ПК за сессию дважды пропадала. В 19:44 UTC ПК перезагрузился: в журнале Windows —
  «Power Action Reboot, Reason: Kernel API», перед этим OpenSSH «terminated unexpectedly». Открытый у владельца Unity Editor
  при этом закрылся.
  - `on-remote … unity.ps1` при обрыве теряет обёртку, а процесс, запущенный через `Start-Process`, sshd убивает вместе с
    сеансом (прежний помощник `gate3/detached.ps1` поэтому не работал; удалён).
  - Ворота запускали `Tools/unity.ps1` через `Docs/Reports/gate3/wmi-run.ps1`: процесс создаётся через WMI
    (`Win32_Process.Create`) и не зависит от сеанса ssh.
  - Блокировку `Temp/unity.lock` держит хаб (`Temp/g3/urun3.sh`), пока в `gate3/<tag>.txt` нет строки `EXIT`. Unity
    по-прежнему одна.
- **Вопросы владельцу:**
  1. Кнопки «Deliver» соседних готовых заводов идут «ёлочкой», а в 9:20 — тремя ступенями над рядом руд (снимки `*-b-ready-*`).
     Можно вместо этого сократить подпись до «×24» с иконкой, когда кнопки тесно. Какой вариант лучше?
  2. Фонари лагеря стоят у шкафа и стеллажа (`b ± 3`), как в старой раскладке на 7 столбцов. Подходит?
  3. Вопросы WP5a и WP5b из этапа 2 остаются открытыми:
     - раскладка труб;
     - склад в шахте 1;
     - цвет HP цепей;
     - капсулы в `ReadyToContinue`;
     - причина на «Sell all»;
     - размеры подписей боковых кнопок;
     - высота сведений завода;
     - «Auto delivery: on» после DEV;
     - рынок во весь экран.

## ТЗ 2.2, WP7 «Баланс»: калибровка PriceRate и замер b5 (Claude, агент WP7, 15 октября 2026)

- Отчёт — `Docs/Rework/BALANCE_FACTORIES.md`, данные — `Docs/Reports/balance-factories/` (`calibration.*`, `tune/`, `b5/`,
  `factories-summary.json`, `factories-tables.md`, `playable/`). Ничего не закоммичено и не опубликовано, Unity не запускался.
- **Модели и бот с заводами (§9):** `OutpostBot.TendFactories` — «Deliver» по всем готовым партиям, затем «Sell all»; так
  делают бот (все политики) и модели аудита `free` (раз в 30 с), `active` (5 с), `idle` (5 мин); между шахтами карточки
  улучшаются, как раньше. Выпуск в отчётах — только `FactoryMath.UnitsPerCycle` (запрос ревью этапа 1). В отчёт шахты
  добавлены золото товара, продажи, ручные доставки, пропавший товар, заводы (циклы, автодоставка в начале и в конце) и
  опыты отсутствия 1 и 3 ч.
- **`PriceRate = 4`** (ориентир 5/16): перебор 16 значений, медиана шахт 2–5 у `free` по 12 seed — 4,94 мин при b4 4,96
  (предел 4,22–5,70). Записано `gen_factory_rules.py --price-rate 4` (значение по умолчанию генератора), встроенные копии
  пересобраны. `UnitsPerCycle`, правило `PriceFactor` и цены веток b4 не менялись.
- **Замер b5** (`Audit_After`, перемерен после последней правки аудита): 24 кампании по 144 шахты, все успешны; `free` seed 1 —
  19,07 → 19,63 ч. Тест `Campaign_FreePlayer_PacingWithinStageTargets` по §9 проходит (максимум без покупки 111,4 с, шахта 37);
  отключённые условия этапов фактически выполняются.
- **Playable (§14):** `PlayableBalance.Version = outpost-2.2-b5-playable`, числа p3 прежние; `PlayablePacingDraft` — 11 / 9 шахт
  за 30 мин у `active` / `free`, как на b4.
- **Правки тестов (§16.1):**
  - `OutpostBalanceRegressionTests.Campaign_FreePlayer_PacingWithinStageTargets` — по §9 и §16.1 (д);
  - новые `Bot_DeliversAndSellsGoods_InvariantsEveryTick`, `AuditModels_TendFactories_ReportGoods_AbsenceIsolated`;
  - `OutpostBalanceAuditTests`: `Audit_After` → `balance-factories/b5/`, `Audit_TuneFactories`;
  - файлы WP1, сломанные калибровкой: `FactoryCatalogTests` (`Rules_FileMatchesEmbeddedCopy_Version_PipesAndPrices`,
    `Rules_Validate_RejectsBrokenRows`, `State_FactoryFields_RoundTripByteIdentical`) и `FactorySimulationTests`
    (`Market_Zone2_ControlPrices_SellAccounting_Refusals`, `Market_SellAll_ByTypeNumber_PricesFromRunSnapshot`) — контрольные
    цены пересчитаны под `PriceRate = 4` по приложению B п. 6 (молоко 8/5, 10 золота в шахте 2; сталь 120, 720 золота).
- **Проверки:** ядро без фильтра — 2 635 тестов с черновиком, 2 634 прошли, 1 упал (`Rules_Validate_RejectsBrokenRows`, строки
  со старым «1/8»; исправлено, `FactoryCatalogTests` 16/16), 8 Explicit пропущены; `gen_factory_rules.py --check` — OK;
  встроенные копии совпадают с `Resources`.
- **Вопросы владельцу** (подробно — BALANCE_FACTORIES.md, «Вопросы владельцу»):
  - В1: шахта 37 у seed 4 (`free`, `idle`) — 121,3 с без доступной покупки (> 120; seed 1 — 111,4 с). Причина — цепи без груза
    (`adapter-2`), от `PriceRate` не зависит. Ограничение не ослаблено;
  - В2: автодоставка на шахтах 31–144 — 0,9 % заводов (порог 25 %); варианты `ManagerChanceMult`, `LowCardChanceMult`,
    `ChestChances`; эликсир не ограничивает;
  - В3: товар пропадает при завершении шахты — 14 % проданного у `free` онлайн и весь товар, сделанный за время отсутствия;
  - В4: заводы с циклом от 10 мин (типы 8–20) не заканчивают ни одного цикла;
  - В5: при `PriceRate = 4` товар — 55 % золота шахт 2–5; в пределе и значения от 5/4.

## ТЗ 2.2, этап 1 сведён: ворота G2 (Claude, агент ворот, 15 октября 2026)

- Этап 1 ТЗ 2.2 (`Docs/TZ/Cat_Miners_Factories_TZ_v2.2.md`, §17) — WP0 контракты → WP1 ядро, WP2 карта, WP3 карточки, WP4a арт
  и звук заводов, WP4b арты карточек, WP6 сохранения и DevTools — сведён в одно целое. Ядро и Unity компилируются, тесты ядра
  проходят все, кроме теста темпа: его по §9 и §16.1 (д) правит WP7 вместе с калибровкой. Изменения не закоммичены, ничего не
  опубликовано.
- Что есть после этапа 1:
  - **WP0:** `factory_rules.json` (`factories-1`, `PriceRate 5/16`, `UnitsPerCycle 20`, циклы из JSON) и генератор
    `Tools/Factories/gen_factory_rules.py`; `FactoryRules`, `FactoryCatalog`, `FactoryMath`/`FactoryQueries`. Контракт
    состояния, событий и команд (`DeliverFactory`, `SellGoods`). Версии: схема 3, `outpost-2.2-r1`, `outpost-2.2-b5`,
    `adapter-2`. Члены `IOutpostGame` с заглушками в контроллере. CORE_SPEC §16, RUNTIME_SPEC §9, UI_API §13, VIEW_API §11.
  - **WP1:** ядро заводов (`OutpostSimulation.Factories.cs`):
    - заводы создаются в `CreateRun` и сбрасываются при переходе; цепи → `FactoryUnlocked` → проверка автодоставки;
    - ручной буфер `Ready`, автодоставка (в том числе от первой копии карточки и при загрузке), трубы, шаг 4б, сроки в
      `NextEventTick`, остановка в `ReadyToContinue`;
    - команды `DeliverFactory`/`SellGoods`, `FactoryOutput` в `TeamStats`, инварианты §10.2, DEV-методы;
    - `FactorySimulationTests` — 23 теста; офлайн 3 ч шахты 140 с 15 автоматическими заводами — 65 мс.
  - **WP2:** топология `wide-branching-mines-3`:
    - 38 шахт списка §4.1 исправлены 44 обменами «завод ↔ блок r/x того же сектора», остальные 106 шахт побайтно прежние;
    - валидатор проверяет оба случая §4.1, `FactoryRoutes` окончательный — маршруты у 1 232 из 1 232 заводов;
    - `adapter-2` (у цепей груз 0), проверки токенов §3.1;
    - эталон аудита пересчитан скриптом, прежний сохранён как `outpost_v2_audit_2.1.json`.
  - **WP3:** `cards-3` — `manager-1`/`manager-2` = `FactoryOutput` 1/10 + 1/20·(L−1), `CardService.FactoryOutputBonus`,
    39 английских имён (приложение A), `CARD_EFFECT_MAPPING.md`.
  - **WP4a:** 20 зданий с кадрами работы, цепи и трещины, детали труб, товары 16×16 и 8×8, склад, `fx_chain_break`,
    5 иконок, `PixelPalette.ByName`, 11 звуков. Сводные листы — `Docs/Reports/factories/art/`.
  - **WP4b:** 39 артов карточек заводов. 22 прежних арта побайтно те же (`review_cards.py --check`).
  - **WP6:** статус загрузки `Outdated` и `OutpostSaveService.ResetOutdated` (архив `pre-factories/` и новая кампания), ветка в
    `LoadProfile`, DEV-кнопки заводов, `debugProfile` в `Save`/`Load`.
- **Сделано на воротах:**
  - выполнены запросы пакетов: `Editor/OutpostSetup.cs` читает конфиги через `ParseResources` (8 файлов); добавлен ключ
    `outpost.toast.save_reset`; поправлены тесты без владельца, сломанные изменениями WP2 (список ниже);
  - `OutpostTestHelpersB.ExtraViolations`: кошелёк теперь учитывает `GoodsSoldGold`; добавлена проверка «котики, их пути и груз
    не заходят на footprint открытого завода»;
  - `.meta` для 11 новых звуков — по шаблону остальных SFX, GUID как в `gen_meta.py`;
  - `outpost_balance.json`: `Version` → `outpost-2.2-b5` (числа b4 не тронуты, §9, §10.1). В тестах WP7 по §16.1 (б) обновлены
    только ожидания версии. Калибровку, тест темпа и `PlayableBalance.Version` оставил WP7;
  - пересобраны встроенные конфиги: изменилась только версия баланса. Атлас пересобран: 2048×1024, 3 731 спрайт
    (2 136 разных), hash `0befef2876ae0bde` не изменился. `gen_factory_rules.py --check` — OK; `build_topology.py --check` —
    up to date; `gen_card_mapping.py --check` — актуальна; `review_cards.py --check` — 22/22;
  - вопрос WP2 о run без снимка правил: так и задумано. `CreateRun` всегда кладёт снимок, сохранения схемы < 3 сбрасываются,
    поэтому run без заводов бывает только в синтетических тестах.
- **Проверки:**
  - Ядро без фильтра (`run.ps1 -Instance gate`): **2 629 тестов, 2 628 прошли, 1 упал**, 7 Explicit пропущены. Упал
    `Campaign_FreePlayer_PacingWithinStageTargets`: `audit.Success` по 144 шахтам выполнено, но медиана этапа 2–10 — 7,39 мин
    при пределе (3; 7). Темп упал, потому что цепи больше не дают монет, а бот ещё не продаёт товар; это работа WP7 (§9).
  - Unity `Compile -Target Ci`: exit 0, **0 ошибок**. Собраны Core, Runtime, Playable, DevTools, Editor, Tests.EditMode и
    Tests.PlayMode. Новых предупреждений нет: 14 прежних — FacebookSDK, `Haptics`, `GooglePaymentsRuntime`. 11 новых `.ogg`
    импортированы AudioImporter. Лог — `Docs/Reports/unity-compile.log`.
  - Не запускались: Unity PlayMode и EditMode (ворота G3, WP8) и Unity `Validate`: проверка 144 шахт со стоящими заводами и
    кампании бота есть в ядре.
- **Правки существующих тестов по §16.1.** Ни одна проверка не удалена; у каждой правки комментарий-ссылка на ТЗ 2.2.
  - WP1: `FactoryCatalogTests.State_FactoryFields_RoundTripByteIdentical` — завод шахты 2 теперь создаётся в `CreateRun`, тест
    проверяет его как `Locked` и убирает перед сериализацией. `…Readers_UnitsPerCycleAndTransit…` — добавлена проверка снимка
    правил.
  - WP2:
    - (а) `OutpostKitA.ExpectedUnits(hp, kind)` и `OutpostTestHelpersB.CargoUnitsOracle(hp, kind)`: у `s` груз 0;
    - (б) `NewProfile` в обеих фикстурах пишет `SchemaVersion = OutpostIds.SchemaVersion`;
    - (г) `MineTopologyMigrationTests` дополнительно проверяет, что перенесённая карта проходит валидатор;
    - `MineTopologyTests.CampaignGeometry` ужесточён: стоящие заводы, груз `s` = 0, оба случая валидатора;
    - новые тесты в `MineTopologyTests` и `OutpostImportTests` (§3.1).
  - WP3 (а), `OutpostCardTests`:
    - `Catalog_All61…`: у ca002 и ca014 теперь `FactoryOutput`;
    - `Effects_TeamStats_EachAppliedOnce…`: урон и цена `s` без бонуса, добавлены проверки выпуска;
    - `Zone2_ShaftChest_…_AndManagerBonusRaisesThatResource` → `…_AndFactoryCardRaisesOnlyItsOutput`;
    - три новых теста `FactoryCards_*`.
  - WP6:
    - (б) `OutpostConfigTests.Versions_MatchTz`, `OutpostSaveTests.Envelope_HasAllFields_AndValidChecksum`,
      `OutpostCoreSmokeTests.ConfigFiles_MatchEmbeddedDefaults_AndExactParsing`, `OutpostRewardTests.NewProfile_RewardRngIsStream5OfSeed`;
    - (в) `PaymentDeliveryTests.V1MigratesLedgerAndV2RejectsNullLedger` заменён на
      `OutdatedSchema1And2StartOverWithLedgerAndNullLedgerIsRejected(1|2)`; его последняя проверка перенесена без изменений.
      Добавлены ещё два теста;
    - новые тесты `OutpostSaveTests` по §10.2, §10.3 и §13.
  - Ворота:
    - `OutpostMathTests.Zone144_MaxStageGold_SumIsExactAbove2Pow53`: 13206982907366054057 → 7421129543330699456 — `controls`
      нового эталона, §4.2;
    - `OutpostSimulationTests.AllZones_EveryTargetHasExactHpAndCargo_SumsMatchAudit`: `CargoUnitsOracle(hp, kind)`, §16.1 (а);
    - `OutpostTestHelpersB.ExtraViolations`: кошелёк с `GoodsSoldGold` (§6.4) и новая проверка footprint открытого завода (§4.1);
    - `FactoryCatalogTests.Map_AllZones_RoutesDeterministic_…` ужесточён до «маршрут есть у всех 1 232 заводов»;
    - `OutpostBalanceRegressionTests.ProfileB4_Values` и `B3SaveMidMine_…`: версия b5, §16.1 (б).
- **Вопросы владельцу** (из результатов пакетов):
  1. `NextEventTick` при работающих заводах дополнительно учитывает отметку `LastChangeTick + WatchdogTicks + 1`. Без неё при
     застрявших котиках watchdog сработал бы только к концу цикла, а цикл длится до 8 ч. ТЗ §4.5 называет только `CycleEnd` и
     `ArriveTick` (WP1).
  2. Если у продажи несколько причин отказа, `no-factories` проверяется раньше `phase`: в шахте 1 после завершения кнопка
     показывает «No factories in this mine», а не «Mine complete» (WP0).
  3. В эталоне аудита `value_cap40_passes_necessary_bound_zone_count` упал с 61 до 0. Цепи больше не дают монет, и бюджета не
     хватает, чтобы гарантированно довести «Оценку» до 40. Это данные для нового баланса (WP2).
  4. При исправлении топологии 17 заводов в 15 шахтах переехали в соседнюю камеру через ствол, на 11–29 клеток. Шахты: 6, 7,
     13, 23, 37, 102, 118, 122, 126, 132, 133, 136, 139, 140, 142; посчитано воротами по
     `Docs/Reports/topology/factories.json`. Если заводы должны оставаться в своей камере, обмен можно ограничить расстоянием;
     тогда сработает запасной путь «прорезать проход», он проверен (WP2).
  5. Сохранения (WP6):
     - если рядом с устаревшим файлом лежит файл будущей схемы, загрузка даёт `Incompatible`, а не `Outdated`;
     - гемы прежней кампании, в том числе купленные, и отложенная реклама после сброса не переносятся;
     - если не удалось записать архив, игра показывает `Fatal` и старые файлы не трогает.
  6. Арт карточек (WP4b):
     - у Fishery Manager — ручной безмен, у Foundry Manager — напольные весы, а не весы оценщика;
     - у Steel Mill Foreman — очки поверх каски, у Apiary Foreman — шляпа пасечника поверх каски;
     - лист `cards.png` ждёт утверждения (G5).
  7. Арт и звук заводов (WP4a):
     - атлас занят на 1 006 из 1 024 строк: следующее заметное добавление поднимет его до 2048×2048 (+8 МБ видеопамяти);
     - анимация работы общая: три вида дыма;
     - на вывеске склада ящик с монетой, а не весы;
     - звуки проверены только анализом сигнала, на слух не прослушаны.
  8. Для карточки уровня 0 добавлен ключ `cards.detail.mine_auto_missing` («(not collected)»); в §7.1 такого текста нет (WP3).
- **Известные незакрытые места этапа 2:**
  - `OutpostCardText` не знает `FactoryOutput`: окно карточки завода пока покажет сырой шаблон и пишет в `Loc.FormatErrors`
    (WP5);
  - позиции завода и склада в контроллере — заглушки; вида заводов, труб, склада и рынка нет; HUD не обрабатывает
    `Selection.Warehouse`; после `SetSimulation` не снимаются `sim.Events` (WP5);
  - бот и аудит не доставляют и не продают товар; `PlayableBalance.Version` ещё `outpost-2.1-b4-playable` (WP7, §9, §14);
  - PlayMode-наборы не запускались (WP8);
  - спецификации и ТЗ 2.1 не обновлены (WP9).
- **Точка продолжения — этап 2:**
  - WP5: вид заводов, труб и склада, `OutpostPickKind.Warehouse`, Market и окно рынка, нижняя панель из 6 кнопок, сведения
    завода, окно карточки, стрелки, тосты, звук, ключи `Loc`, квесты playable;
  - затем WP7: бот и аудит с заводами, калибровка `PriceRate`, тест темпа по §9, `PlayableBalance.Version`,
    `BALANCE_FACTORIES.md`;
  - WP8: PlayMode по §16.2 и снимки;
  - WP9: ТЗ 2.1, спецификации, `PIXEL_STYLE_GUIDE`, `ART_NAMES.md`.
  - Запросы пакетов этапа 1 к WP5, WP7, WP8 и WP9 — в их результатах.

## ТЗ 2.2: заводы вместо месторождений — черновик (Claude, 15 октября 2026)

- Запрос пользователя: написать ТЗ для Opus 5.5 (ultracode) — месторождения `s` (аметист, цитрин…) становятся заводами
  товаров (молоко, дерево, сталь, корм…), закованными в цепи; у каждого своя труба до склада базы; рынок продаёт товар
  за золото; прогресс-бар цикла из `MineShafts` JSON, кнопка «Deliver»; автодоставка — по уровню Common/Uncommon карточки
  из токена `s` (`cardId`, `cardLevel`).
- Файл: `Docs/TZ/Cat_Miners_Factories_TZ_v2.2.md`. **Не реализовано.** Решения владельца 15 октября внесены (§1.2): циклы —
  как в JSON (8 с – 8 ч), баланс прохождения владелец меняет отдельно; цепи монет не дают, вся ценность — в продаже товара;
  Common и Uncommon одинаково увеличивают выпуск товара, автодоставка — по уровню карточки из токена каждой шахты;
  непроданный товар при переходе пропадает; Market — шестая кнопка нижней панели; список 20 заводов, товаров и 39 карточек
  утверждён, названия на английском (приложение A).
- Многоагентная проверка ТЗ (5 направлений, объединение, опровергающая перепроверка): 68 замечаний → 39 после объединения →
  29 подтверждено, 10 опровергнуто. Главное: в 38 из 144 шахт стоящий завод перекрывает проход (топология
  `wide-branching-mines-3` обязательна); шестая кнопка не помещалась (новые ширины нижней панели); автодоставка должна
  включаться и от первой копии из сундука; сроки циклов — события ядра (`NextEventTick`); склад — постройка вида, маршрут
  до `IntakeCell`; `UnitsPerCycle = 20`; цена через `CoinBase`; правила теста темпа и калибровки шахт 2–5; владение файлами.
  Решение владельца Р9: игры нет в сторе — старые сохранения архивируются, игра начинается заново, миграций нет. Добавлен
  §7.2 — картинки 39 карточек заводов. Второй и третий круги проверки (13 и 2 новых подтверждённых недочёта) внесены; ТЗ
  готово. По команде пользователя начато внедрение (этап 1: WP0 → WP1/WP2/WP3/WP4/WP6 → ворота G2).

## Обучение убрано из игры (Claude, 15 октября 2026)

- Запрос пользователя: «Убери все туторы с игры». Убрано всё обучение основной игры: `OutpostTutorial` (10 шагов §15 —
  автодобыча, ручной удар, сумка, золото, «Кирки», найм, выбор целей, награда, сброс, сохранённый бонус), подсказка
  `TutorialHint` в HUD (наставник, лапка, рамка), кнопка «Skip tutorial», строка «Tutorial hints» в настройках (окно — 8 строк),
  `IOutpostGame.SaveTutorialProgress`, ключи `outpost.tutorial.*` и `outpost.settings.tutorial`; из текстов Reset Progress и
  DEV-сброса убрано «the tutorial will start again». Новая игра и сброс сразу запускают шахту 1.
- Остаётся: учебная карточка ca001 первой доставки (награда и тост «First card», баланс не меняется); `Profile.Tutorial` в
  сохранении — только совместимость, не читается (миграции нет, старые сохранения читаются); класс `TutorialHint` — для квестов
  playable и старого режима. Старый режим (`GameController`/`TutorialController`, только редактор с `LegacyMode`) не тронут.
- Playable: `PlayableDirector` больше ничего не выключает; `PlayableGuide` находит кнопки HUD через новый
  `OutpostHud.FindTarget(id)` (имена `Target*` прежние). Квесты и подсказки playable не менялись.
- Тесты: `OutpostHudTests.HudWindowsAndCommandsWork` (было `…AndTutorialWork`) проверяет, что в HUD нет подсказки, кнопки пропуска
  и строки настроек; `PointerTapOnRock…` выбирает ближайший к базе камень без подсказки; `PlayableHooks…` и `PlayableTests` —
  в HUD нет подсказки основной игры; генератор `OutpostStrikeEvidenceTests` больше не снимает `tutorial-player-strike.png`;
  из остальных тестов убраны `Game.Tutorial.Skip()`.
- Документы: ТЗ (вводная 15 октября, §4, §11.2, §14, §15 «Обучения нет», §17, §21), RUNTIME_SPEC §1–§6 и §8, UI_API §1–§3,
  §10 и §12, CORE_SPEC §5, README_REWORK, VIEW_API, PLAYABLE_TZ, PIXEL_STYLE_GUIDE §6.5. Исходное состояние — коммит `a483c56`
  репозитория `tigblack/Cats-Cute-Miner`; изменения не закоммичены. Сайт (GitHub Pages) не пересобирался — в нём обучение ещё есть.
- Проверки (Unity 6000.3.11f1, `build/ci`): компиляция — 0 ошибок, новых предупреждений нет; PlayMode
  `OutpostHudTests|OutpostRuntimeTests|OutpostStoreUiTests|OutpostCardsUiTests|EdgeIndicatorTests|CoreGameSmokeTests|
  CameraExplorationTests|MineTopologyRuntimeTests|PlayableTests` + генераторы `OutpostEnglishUiEvidenceTests.SettingsScreens|
  EnglishScreens` — **91/91**; после правки фокуса камеры `PointerTapOnRock` — 1/1; ядро (`Tools/CoreTests/run.ps1` без фильтра) —
  **2 551/2 551**. Снимки `Docs/Reports/english-ui/01-hud.png`, `13-settings-play-time.png`, `14-settings-reset.png` просмотрены:
  подсказки и кнопки пропуска нет, в настройках нет «Tutorial hints», в диалоге сброса нет фразы об обучении.
- Замечание по инструменту: при длинном `-TestFilter` `unity.ps1` падает на архивации XML (`PathTooLongException`, имя файла
  из фильтра > 260 символов) и возвращает код 1 даже при 91/91 — смотреть строку `tests total=…`.

## Магазин: скидка на гемы убрана (Claude, 14 октября 2026)

- Запрос пользователя: «Убери в магазине скидку на кристаллы, на хард валюту». Скидкой был бонус первой покупки пакетов
  гемов: бейджи +20…+100 %, зачёркнутое количество, зелёное увеличенное число и «Available: 1».
- `store_catalog.json` → `store-3`: у пакетов гемов убраны `BonusAmount`/`BonusPercent`; каждая покупка даёт обычное
  количество (160 / 500 / 1 200 / 2 500 / 6 500 / 14 000), цены прежние. Встроенная копия пересобрана
  (`Tools/gen_outpost_config_defaults.py`). Механизм бонуса в коде остаётся и включается только данными; поле профиля
  `StoreBonusUsed` сохранено для совместимости. `StoreHash` не входит в `CatalogHash` — сохранения совместимы.
- Тесты: `OutpostStoreTests` — каталог без бонусов, покупка пакета даёт обычное количество, механизм бонуса проверяется на
  тестовом каталоге; `OutpostStoreUiTests` — у плиток гемов нет бейджа, зачёркнутого числа и «Available». Playable не
  затронут (в нём нет пакетов гемов).
- Документы: ТЗ (вводная 14 октября, §10.5), CORE_SPEC §8.2, UI_API, RUNTIME_SPEC. Копии до правок —
  `Backups/before-gems-no-bonus-2026-10-14/`. Сайт (GitHub Pages) не пересобирался.

## Playable: первые 30 минут, динамичнее (Claude, 13–14 октября 2026)

- Решение пользователя: полноценные первые 30 минут настоящей игры в отдельной WebGL-сборке (не короткий рекламный HTML).
  Референс Kung Fight разобран: `Docs/Playable/research/reference.md`; ТЗ — `Docs/Playable/PLAYABLE_TZ.md`.
- Темп: профиль `outpost-2.1-b4-playable` (p3: 3 котика, золото ×2, без удорожания по шахтам, найм ×1,5, тап ½, вехи
  кирки [5, 10]) — 11 / 9 шахт за 30 минут (active / free) вместо 5; замер `Tools/CoreTests/Drafts/PlayablePacingDraft.cs`.
- Код: сборка `CatMiners.Playable` (`Assets/Game/Scripts/Playable/`, только редактор или `CATMINERS_PLAYABLE`): квесты по
  шахтам 1–10 с наградой золотом и подсказками (лапка, наставник, стрелка к цели за краем), всплывающие NEW CAT! / GATE OPEN! /
  PICKAXES ×2!, автопереход к следующей шахте, баннер CAT MINERS · PLAY FREE, финальный экран после 30 минут или шахты 10,
  магазин только с деревянным сундуком и тайм-скипами, своё сохранение и «Play again». Переход в магазин — `.jslib` с SDK
  сетей (mraid / FbPlayableAd / dapi / ExitApi / install), ссылка `?store=` или полная веб-версия игры.
- Основная игра: только выключенные по умолчанию крючки (`ConfigsOverride`, `SuppressOfflineSummary`, `BottomReserve`,
  `ResetProgressOverride`, `StoreHideUnpayable`, `TutorialHint.TipAnchor/PointDown`, `GrantRewardGold`, `WithStore`),
  проверка `OutpostRuntimeTests.PlayableHooks_OffByDefault…`.
- Сборка: `Tools/unity.ps1 -Mode BuildPlayable` → `Builds/Playable` (31,0 МБ, без заставки Unity), отчёт
  `Docs/Reports/build-playable.json`; локально `node Tools/Web/serve.cjs --playable --port 8095` →
  http://localhost:8095/Cat-Miners/playable/. Не опубликовано.
- Проверки: PlayMode основной игры 108/108 (5 Explicit-генераторов пропущены), playable 29/29 + снимки
  `Docs/Reports/playable/screens/`; ядро 2477/2477; живая проверка WebGL в браузере: квест, награда, следующий квест, баннер.
  Бот-прохождение: шахта 10 за ~26 минут, 14 котиков.

## Рекламные ролики для Meta (Claude, 13 октября 2026)

- По заданию пользователя сделаны три 45-секундных ролика из настоящей игры: Level 1 → Level 100, Stop tapping!,
  Chests & collection — `Docs/Marketing/ads/*.mp4`, сценарии и порядок пересборки — `Docs/Marketing/AD_SCRIPTS.md`.
- Съёмка: `Assets/Game/Tests/PlayMode/Outpost/AdCaptureTests.cs` ([Explicit]; Mine1, Mine13, Mine100, Collection, Extras),
  кадры 1080×1920 через AsyncGPUReadback прямо в ffmpeg (`Tools/AdVideo/.venv`), журнал тапов/звуков/событий —
  `Docs/Reports/ads/*.json`. Запуск без привязки к сессии: `Tools/AdVideo/start_capture.ps1`.
- Правка в игре (поведение без записи не меняется): `Runtime/FrameTime.cs` — шаг кадра анимаций равен
  `Time.captureDeltaTime`, если запись его задала; иначе прежнее `Time.unscaledDeltaTime`. Заменены: `Board.Sync` в
  `OutpostController.Loop`, переход между шахтами, полёт монет HUD, `PixelPop`, `CameraRig`.
- Монтаж, музыка, графика — на хабе: `Tools/AdVideo/compose.py`, `edl.py`, `overlays.py`, `ad_music.py`.
- Проверки: съёмочные тесты 5/5 прошли; независимый QA (4 агента) — правки внесены (порядок прогресса, правдивые подписи,
  пометка о случайных предметах, безопасная зона, A/V-сдвиг). Полный регрессионный PlayMode после правки FrameTime
  не запускался.

Ориентир — `Docs/TZ/Cat_Miners_Auto_Incremental_TZ_v2.1.md` (ТЗ, главный документ) и контракт ядра `Docs/Rework/CORE_SPEC.md`.
Если SPEC и ТЗ расходятся, действует ТЗ, а расхождение записывается здесь.

## Настройки: Reset Progress и сыгранное время (Codex, 13 октября 2026)

- По уточнению пользователя Set Progress означает начало игры заново. В Settings добавлена кнопка **Reset Progress**, доступная
  во всех сборках, включая WebGL Release, с диалогом Start over / Cancel. Сбрасывается текущий профиль; используется прежняя
  атомарная запись main + bak, архив restart-*, повтор того же снимка при ошибке; прочие DEV-команды остаются только для Debug.
- В Settings добавлена строка **Play time** (например, 2h 6m). Постоянный `Profile.PlayTimeMs` считает реальное время активной
  игры и меню, переживает переходы, повторный запуск и Reset Progress. Фон и симулируемые офлайн/тайм-скипы/скорость не входят.
  Старые сохранения совместимы: отсутствующее поле равно 0, учёт начинается с этого обновления. Отдельные слоты независимы.
- ТЗ §14/17, CORE_SPEC, RUNTIME_SPEC и UI_API обновлены. Добавлены проверки старого payload, round-trip, отрицательного
  значения, активных меню, фона, скипа, скорости, профилей, перезапуска, отмены и подтверждения сброса, длительного кадра.
- Проверки: dotnet OutpostSaveTests + LocalizationTests — **52/52**. Первый PlayMode-прогон **36/36**; независимый review
  выявил потерю времени на кадрах длиннее секунды: отсечка удалена, добавлен отдельный регрессионный тест; итоговый прогон **37/37**.
- Окна настроек и подтверждения на 1080×1920 и 1080×2400 просмотрены, обрезанных текстов нет; снимки:
  `Docs/Reports/english-ui/13-settings-play-time*.png`, `14-settings-reset*.png`.
- **WebGL опубликован:** https://tigblack.github.io/Cat-Miners/, коммит `d6d23ebcd7a626e25d4bd677872d5c41c19e8587`, Pages **built**.
  Release (`development=false`), 30 960 277 байт; все шесть файлов сайта сверены со сборкой (SHA-256, у HTML нормализованы окончания строк).
- В настоящей WebGL-игре: новый счётчик есть в старом профиле; сохранены seed, шахта 2, карточки, два предмета и скипы 2 + 1;
  в Settings видны Reset Progress и Play time (0h 4m → 0h 5m); подтверждение открывается, Cancel возвращает в Settings без сброса.
  Перезагрузка сайта сохраняет время: 228 703 → 476 572 мс вместе с прежним прогрессом. В изолированном localhost-профиле
  release-команда ResetProgress создаёт новую шахту 1 и сохраняет время. В проверочном браузере для активной игры применён window.focus().
- Отчёты, хеши и снимки сайта: `Docs/Reports/github-pages/update-2026-10-13-settings/`; итог — `validation.json`.

## Тайм-скипы 10/30/60 минут, окно сундука с карточками, кнопка Base (Claude, 13 октября 2026)

- Запрос пользователя: убрать «For this mine», окно после открытия сундука с тем, что выпало, проверить тайм-скипы
  («за час котики проходят всю шахту»). Решения пользователя (AskUserQuestion): скипы 10 / 30 / 60 минут; окно — у сундука
  магазина всегда и у сундука шахты с новой карточкой; «For this mine» убрать только с кнопки.
- **Тайм-скипы.** Ошибки расчёта нет: скип — то же ядро, что офлайн, ровно N времени работы котиков без покупок. Замер
  черновиком `Tools/CoreTests/Drafts/TimeSkipStrengthDraft.cs` (кампания модели free, скип в начале шахты и после 60/120 с
  игры): эталонный игрок проходит шахту за 4–12 мин, котики без покупок — за 16 мин (шахта 1) … 113 мин (шахта 144); скип 1 ч
  завершал шахты 1–75 с самого старта и любую шахту после 1–2 минут игры, 4 и 12 ч — всегда.
  - `StoreItemDef.Hours` → `Minutes`; `store_catalog.json` `store-2`: 10 / 30 / 60 минут, цены 50 / 150 / 350 и стартовый набор
    (3 коротких + 1 средний) прежние. ID `skip_1h/4h/12h` сохранены — по ним в сохранениях лежат расходники.
  - `OfflineSimulator.BeginSkip(state, def, utcMs)`; `OfflineSummary.SkipMinutes`, `SkipItemId`; `SkipMinutesInProgress`.
  - Плитки «10m / 30m / 1h», окно расчёта «The cats work 10m in a flash…», итог «Time Skip: 30m» с артом плитки.
- **Окно сундука.** `ChestCardTile` — карточки картинками по три в ряд: рамка и фон редкости, арт, «×N», имя, «NEW»; до 6,
  затем «+N more cards», эликсир, «Great». Окно шире обычного (`OutpostDialog.SideMargin = 0.03`, блок `Extra` над строками —
  `ExtraFirst`, раскладка плиток — `AfterLayout`); на 9:20 фон портрета обрезается маской, арт виден целиком.
  - Сундук шахты (не магазин, не учебная карточка) с `Extra > 0` открывает то же окно; пока оно открыто, следующие сундуки шахты
    добавляются в него («N Chests», копии складываются); окно магазина не подменяется; остальные сундуки — прежний тост.
  - Автоокно «Итоги шахты» ждёт закрытия окна сундука (сундук выхода приходит вместе с завершением).
  - Имя и арт сундука шахты без `BalanceRepository`: `PresentationCatalog.GachaNameOrMimic/GachaTierOrMimic` (MimicId из каталога карточек).
- **Кнопка Base:** одна подпись по центру; ключ `outpost.bottom.base_sub` удалён. В панели «База» «For this mine» остаётся (ТЗ §9).
- Документы: ТЗ (вводная 13 октября, §10.5 — окно сундука, тайм-скип с таблицей замера, стартовый набор), CORE_SPEC §8.2,
  RUNTIME_SPEC §3.4 и §5, UI_API §3, §6, §8. Копия ТЗ до правок — `Backups/before-chest-window-2026-10-13/`.
- Проверка:
  - dotnet: `OutpostStoreTests|OutpostConfig*` 60/60; полный EditMode (`Tools/CoreTests/run.ps1` без фильтра) — 2 475/2 475.
  - Unity 6000.3.11f1 (`build/ci`): компиляция без ошибок; PlayMode `OutpostStoreUiTests|OutpostHudTests|OutpostRuntimeTests|
    OutpostCardsUiTests|TutorialFlowTests|EdgeIndicatorTests|CoreGameSmokeTests|OfflineGoldPortrait|OutpostEnglishUiEvidenceTests`
    — 58/58. Новый тест `OutpostHudTests.MineChestWindow_NewCardsMerge_ToastOtherwise_ResultsWait`; генератор
    `MineChestScreens` (свежий debug-профиль, шахта 5, ×100 до первого сундука с новой карточкой).
  - Снимки просмотрены: `Docs/Reports/english-ui/s05`, `s07` (9:16 и 9:20), `s08`, `s09`, `m01-chest-mine*`, `01-hud`.
    Обрезанных подписей нет.
- **Сайт обновлён:** https://tigblack.github.io/Cat-Miners/, коммит `213f579` (сообщение с Co-Authored-By через новый параметр
  `Publish-GitHubPages.ps1 -MessageFile`). WebGL Release 30 959 940 байт, сборка 13 мин; четыре файла на сайте побайтно совпадают
  со сборкой (SHA-256). Проверка в браузере (сохранение — из IndexedDB): старый профиль версии `8dec284` загрузился, стартовые
  скипы 3 + 1 не выданы повторно; кнопка «Base» без подписи; окно Wooden Chest с картинкой карточки, NEW и «×1»; короткий скип —
  запас 3 → 2, время шахты +606 с (10 минут и секунды игры), заголовок «Time Skip: 10m». Отчёт:
  `Docs/Reports/github-pages/update-2026-10-13-chest/browser-validation.json`.

## Обновление WebGL на GitHub Pages (Codex, 13 октября 2026)

- Пересобрана и опубликована текущая реализованная версия: магазин, английский интерфейс, Pixeloid Sans,
  обновлённое окно офлайн-дохода. Появляющиеся rewarded-иконки остаются только в плане и в этот билд не включены.
- Адрес: https://tigblack.github.io/Cat-Miners/. Коммит `8dec2844af2b2c3af9ae23a1e9e708bdc402dfc6`;
  Pages `built`, `main` → `/(root)`, HTTPS. Пять публичных файлов игры побайтно совпадают с новым билдом (SHA-256).
- Unity 6000.3.11f1: WebGL Release, `development=false`, Gzip с Decompression Fallback, IndexedDB auto-sync.
  Сборка успешна, exit 0, размер 30 959 603 байта. Перед сборкой сверены 810 файлов проекта с ПК, расхождений нет.
  Первый запуск прервался при потере подключения; повторный запуск завершился успешно.
- Проверки настоящей WebGL-игры в отдельном браузерном профиле:
  - локально: запуск, магазин, Wooden Chest (карточка, 17 эликсира, таймер 4 ч), тайм-скип 1 ч (запас 3 → 2,
    начисление золота), сохранение и перезагрузка без повторной выдачи;
  - старый профиль опубликованной версии: реальная покупка кирки касанием и перезагрузка до обновления;
    после обновления сохранены seed, шахта и улучшение, стартовые расходники магазина выданы один раз;
  - локальные окна 540×960 (9:16), 540×1200 (9:20), 1280×900 и 390×844 — без горизонтальной прокрутки;
  - публично: запуск обновлённой версии со старым сохранением, магазин, удвоение офлайн-дохода (+906 золота),
    сохранение этого бонуса и улучшения кирки после перезагрузки. Ошибок JavaScript и HTTP-ошибок файлов игры нет.
- Результаты и снимки: `Docs/Reports/github-pages/update-2026-10-13/`; сведения о публикации:
  `Docs/Reports/github-pages-publication.json`. Ранее существующие проверки магазина — EditMode 11/11,
  PlayMode 54/54 от 12 октября; в этой задаче они не запускались повторно.

## Магазин: сундуки, тайм-скипы, эликсир и гемы (Claude, 12 октября 2026)

- По скриншотам пользователя (магазин Zombie Merge, без ZM Club) добавлен полноценный магазин, ТЗ §10.5. Решения
  пользователя:
  - soft-валюта — эликсир, hard — новые гемы;
  - реклама и платежи — тестовый режим: награда сразу; удвоение офлайна за рекламу тоже работает;
  - нижняя панель: Store · Cards · Base · Equipment · To base;
  - тайм-скипы: стартовый набор 3 × 1 ч + 1 × 4 ч, дальше за гемы 50 / 150 / 350.
  Цены совпадают с `StoreItems` исходного JSON (сундук Mythical 3 500, эликсир 60 / 500 / 4 500, тайм-скипы 50 / 150 / 350),
  цены пакетов гемов и бонусы первой покупки — по скриншотам. Копии файлов до правок — `Backups/before-store-2026-10-12/`.
- **Данные.**
  - `Resources/Outpost/store_catalog.json` (`store-1`): разделы, плитки, цены, таймер 240 мин, стартовый набор, `TestPurchases`.
    Встроенная копия генерируется `Tools/gen_outpost_config_defaults.py`.
  - `StoreHash` не входит в `CatalogHash`.
  - Шансы сундуков остаются в `card_rules.json → ChestChances`; таблица шансов — в ТЗ.
- **Ядро.**
  - `StoreCatalog`, `StoreService`: котировка, оплата, выдача, бонус первой покупки, `PayTimeSkip`, миграция профиля 0 → 1.
  - Команды `StoreBuy` и `BonusGold`, события `StorePurchased` и `BonusGold`.
  - Сундук магазина открывает тот же `OpenChest` (источник `OutpostIds.StoreSource = −2`), счётчики забега он не трогает.
  - `OfflineSimulator.BeginSkip` — тайм-скип ровно на N часов, без лимита 3 ч.
  - Профиль: `Gems`, `Consumables`, `StoreCooldownUntilUtcMs`, `StoreBonusUsed`, `StoreVersion`. Забег: `BonusCredited`.
  - Инвариант кошелька: `Wallet = TotalCredited + BonusCredited − TotalSpent`.
- **Runtime/UI.**
  - Контроллер: `StoreBuy`, `UseTimeSkip` (фаза `OfflineCatchUp`, итог `PendingSkip`), `ClaimOfflineDouble`, debug `GrantTestGems`
    (кнопка «Gems» в DEV).
  - `OutpostHud.Store.cs` + `StoreTileView.cs`:
    - счётчики эликсира и гемов с «+»;
    - разделы по три плитки; зелёная плитка — бесплатно или своё, синяя — покупка, тёмная — таймер рекламы;
    - бейдж «+N%», плашка «Available: N», «i» — шансы;
    - подтверждение трат;
    - окна сундука («+N more»), шансов и итога тайм-скипа.
  - Пиксель-арт: `icon_store` и 12 артов плиток `store_*`, атлас пересобран.
- **Важно о составе сундуков.** Число карточек и эликсир масштабируются множителями шахты источника (`RankMultipliers`), как у
  сундуков шахты. На шахте 1 обычные сундуки дают 7 × 0,1 → 1 карточку: Wooden — 1 карточка и 15–19 эликсира, Iron — 1 карточка
  и 31–38. Mythical даёт 300 карточек. Масштаб растёт до ×1 на шахте 10 и ×4 с шахты 100.
- Верхняя полоса при 9:20: генератор снимков нашёл обрезанную сумму «1.42K» после тайм-скипа — пятизначная сумма не
  помещалась даже в `FontSmall`. Поля у краёв полосы 5 → 3 пикселя арта, зазор иконка–число 2 → 1. Суммы золота и груза
  с двухбуквенным суффиксом уменьшаются до `FontSmall × 0.75`, как прогресс и котики, и не обрезаются.
- Документы: ТЗ §0, §2, §10.5, §13, §14; CORE_SPEC §3, §8, §8.2, §11, §13, §14; RUNTIME_SPEC §3.3, §3.4, §5; UI_API §3, §6, §8;
  ART_NAMES §6. Web-сборка не трогалась.
- Проверка Unity 6000.3.11f1 (копия `build/ci`), компиляция без ошибок:
  - Полный EditMode: 2 482 теста — 2 474 прошли, 7 пропущены, 1 упал. Упал `OutpostStoreTests`: тест ждал 7 карточек из Wooden, а на шахте 1 их 7 × 0,1 → 1. Ожидание исправлено; после этого `OutpostStoreTests` — 11/11.
  - PlayMode `OutpostStoreUiTests|OutpostHudTests|OutpostRuntimeTests|OutpostCardsUiTests|TutorialFlowTests|EdgeIndicatorTests|CoreGameSmokeTests|OfflineGoldPortrait` — 54/54, уже после правки верхней полосы. В `LoadingNarrowMine…` сравнение профиля учитывает однократный стартовый набор. Офлайн-тесты ждут начисления за рекламу.
  - Генератор `OutpostEnglishUiEvidenceTests` (EnglishScreens + StoreScreens) — 2/2. Обрезанных подписей нет; мельче `FontSmall` только «en route» при 9:20 (15.3).
  - Снимки магазина просмотрены: s01–s09, 9:16 и 9:20.
  - Не запускались полный PlayMode, APK и проверка на устройстве.

## Окно «While you were away»: крупный текст и телевизор на кнопке рекламы (Claude, 12 октября 2026)

- По снимку пользователя: «Gold earned» и подпись кнопки «Double income / Ad» были мелкими, а из кнопки не было ясно,
  что удвоение — за просмотр рекламы. Текст окна теперь `FontLarge` (36), сумма — 200 % = 72 (`FontHuge`, целые пиксели):
  `OutpostDialog.BodyFontSize`. Кнопки окна выше обычных — 36 пикселей арта (`PixelWindow.ButtonsHeight`, Body
  пересчитывается; у остальных окон — прежние 28). На кнопке рекламы сверху «Double income» (36), под ним новая иконка
  `icon_ad_tv` (красный ретро-телевизор с «play», `Tools/PixelArt/sprites/icons.py`, обязательная в `manifest.py`, атлас
  пересобран `Tools/PixelArt/build.py`) и «Watch ad» (36) вместо прежнего «Ad» в 70 %.
- ТЗ §13 и таблица окон, ART_NAMES §6 обновлены. Копии файлов до правок — `Backups/before-offline-ad-button-2026-10-12/`.
- Генератор `OutpostEnglishUiEvidenceTests` теперь снимает и это окно (настоящий офлайн: пауза приложения и 3 часа;
  `12-offline.png`, `12-offline-2400.png`). Он же нашёл старую проблему верхней полосы: при 9:20 счётчик прогресса
  «56/56» не помещался даже в минимальном размере (ячейка ≈40 ед., цифры Pixeloid Bold — 0,78 em). Табличка шахты
  сужена 24 → 20 % ширины, доли сегментов — золото/груз/прогресс/котики 27/26/27/20 % (было 29/27/23/21): у прогресса
  ≈74 ед. при 9:20, остальные не уже прежнего.
- Проверка Unity 6000.3.11f1 (копия `build/ci`): PlayMode `OutpostEnglishUiEvidenceTests|OutpostHudTests|OutpostRuntimeTests|
  OfflineGoldPortrait|VisualAssetCatalogTests|TutorialFlowTests|OutpostCardsUiTests` — 38/38; обрезанных подписей нет,
  мельче `FontSmall` только «en route» при 9:20 (14.5). Снимки окна 9:16 и верхней полосы 9:20 просмотрены.

## Читаемый шрифт Pixeloid Sans вместо Tiny5 (Claude, 12 октября 2026)

- Пользователь оценил английскую версию: «текст не читаемый». Пять пиксельных шрифтов сравнены на одних и тех же строках
  игры в игровых цветах и с тенью: `Docs/Reports/font-candidates/font-comparison.png`, генератор —
  `Temp/font-tools/compare.py` (не синхронизируется). Пользователь выбрал **Pixeloid Sans** (GGBotNet, SIL OFL 1.1,
  версия 0.4 с DaFont): заглавные 7 пикселей шрифта вместо 5, строчные 5 вместо 4, есть «→» и настоящее жирное
  начертание. Отклонены Pixel Operator (тонкий), Pixelify Sans (цифры 2/8 путаются), DotGothic16 (точечный стиль).
- `Resources/Fonts/PixeloidSans-Regular.ttf` и `-Bold.ttf` с лицензией `PixeloidSans-OFL.txt`. Файлы `.ttf` на хаб не
  синхронизируются: на ПК отправлены через `on-remote --push`. `.meta` — `TrueTypeFontImporter` с GUID по схеме
  `Tools/gen_meta.py`. Tiny5 остаётся в `Resources/Fonts`, но не используется.
- `UiFonts`: обычный и жирный ассеты TMP (`Bold` — настоящее начертание, `HasRealBold`), сетка `EmPixels = 9`,
  растеризация 72 (8 текселей на пиксель шрифта), отступ атласа 8, тень `8/9` градиента = 1 пиксель шрифта.
  Размеры `PixelUi` кратны 9: Small 24→18, Body 32→27, Large 48→36, Title 56→45, Hero 64→54, Huge 88→72 — высота
  заглавных и ширина строк почти прежние. Подписи на поле `Label3D`: 2,5→2,8125 и 5→5,625 (пиксель шрифта = 1 и 2
  пикселя арта). PIXEL_STYLE_GUIDE §6.1 и ТЗ §16 обновлены. Копии файлов до правок — `Backups/before-pixeloid-font-2026-10-12/`.
- Проверка Unity 6000.3.11f1 (копия `build/ci`, компиляция без `error CS`, предупреждений о шрифте в логе нет):
  `OutpostEnglishUiEvidenceTests` — 1/1, 15 снимков 9:16/9:20 просмотрены, обрезанных подписей нет; мельче `FontSmall`
  только «en route» (13.6) и счётчик «0/56» (15.3) верхней полосы при 9:20 — физически как у Tiny5 (заглавные ≈11 ед.).
  PlayMode `OutpostHudTests|OutpostRuntimeTests|OutpostCardsUiTests|CardsPortraitsAndUpgrade|EdgeIndicatorTests|
  CoreGameSmokeTests|TutorialFlowTests|UiScreenshotTests|PointerInputTests` — 63/63; замер окон всех 61 карточки
  (`Docs/Reports/card-detail-layout/layout.tsv`, 488 подписей при 9:16 и 9:20) — обрезанных 0. Полные наборы
  EditMode/PlayMode не запускались; Web-сборка по просьбе пользователя не трогалась.

## Английская версия игры и окно предмета (Claude, 12 октября 2026)

- По запросу пользователя вся игра переведена на английский: все восемь разделов словаря `Loc` (Core, Hud, Controller,
  Tutorial, Catalog, Board, Outpost, Cards; около 700 строк), `Loc.Language = "en"`. Формы слова — английское правило
  `Loc.Plural` (`.one` для ±1, иначе `.many`), русские формы `.few` удалены. Длительности — «45s», «1m 05s», «2h 03m»,
  «2d 5h»; десятичный разделитель в карточках — точка («7.5%», «×2.25»); «+3 h» карточки офлайна берётся из словаря.
  Названия разделов в подсказках пишутся с заглавной буквы без кавычек («open the Base», «equip it in Equipment»).
  Карточки: «Знаток» → Appraiser (цена порций), «Мастер» → Master (урон); подпись груза в верхней полосе — «en route»
  («in transit» не помещается в ячейку 63 единицы при 9:20). Веб-шаблон (заголовок вкладки, загрузка, ошибка) — тоже на
  английском. Окно фатальной ошибки начинается с английской фразы («Could not load/save the game.»), технический текст
  контроллера — ниже, приглушённо.
- Окно предмета по снимку пользователя: «Надето на всю команду» → «Equipped»; строки «После улучшения» больше нет —
  бонус показан как в окне карточки: «Damage +8.4%», зелёная стрелка `icon_upgrade` вправо, «+8.8%» зелёным (`Green6`);
  на максимуме — без стрелки. Сравнение с надетым — «Now: Damage +8.4% → with this: +60%» (название величины один раз; слова
  нужны, потому что «→» в Tiny5 похож на плюс). Детали — без изменений. Правая колонка
  окна собрана `VerticalLayoutGroup` (head / bonus / note) без автоуменьшения шрифта; `OutpostUiText.ItemBonusValue` —
  величина бонуса для сравнения уровней.
- Шрифт не менялся (Tiny5): пользователь сначала оценит читаемость английского текста. Замечено: «→» в Tiny5 — фигура 5×5
  пикселей, похожая на плюс, «%» — 4×4 и похож на «×».
- Не переводились служебные тексты разработчика: логи, исключения, сообщения валидаторов и инвариантов, поля `Comment`/
  `Source`/`Reason` в JSON, панель DevPanel старого режима.
- Тесты: `LocalizationTests` — `DictionaryIsEnglish` (в словаре нет кириллицы), `NoRussianPluralForms`,
  `PluralPicksEnglishForm`, ожидаемые тексты Core на английском; остальные тесты сравнивают текст через ключи `Loc`.
  Новый генератор `OutpostEnglishUiEvidenceTests` (Explicit) снимает 15 экранов 9:16/9:20 в `Docs/Reports/english-ui/`
  и падает на подписях, обрезанных многоточием (`text.tsv`: обрезанные и уменьшенные мельче `FontSmall`).
- Документы: ТЗ §10.3 (окно предмета) и §14 (язык), RUNTIME_SPEC, UI_API, PIXEL_STYLE_GUIDE §6.1; таблица
  `CARD_EFFECT_MAPPING.md` перегенерирована `Tools/Cards/gen_card_mapping.py` с английскими именами.
  Копии файлов до правок (включая русский словарь) — `Backups/before-english-ui-2026-10-12/`.
- Проверка Unity 6000.3.11f1 (копия `build/ci`): компиляция без `error CS` (45 с). EditMode
  `LocalizationTests|HireMergeAcceptanceTests|SimulationTests|TransitionAcceptanceTests|OutpostCardTests|OutpostEquipmentTests` —
  185/186: новый тест форм слова ошибочно принял `outpost.base.mode.one` за форму, исправлен; повтор `LocalizationTests` — 16/16.
  PlayMode `OutpostHudTests|OutpostRuntimeTests|EdgeIndicatorTests|CoreGameSmokeTests|OutpostCardsUiTests|OutpostEnglishUiEvidenceTests` —
  сначала 43/45 (русские ожидания бонусов ворот в `HudPanelsFollowTz`; генератор нашёл обрезанное «in transit» при 9:20),
  после исправлений 45/45. Итоговый прогон с окончательными строками `OutpostEnglishUiEvidenceTests|OutpostHudTests` — 2/2.
  Снимки 9:16 и 9:20 просмотрены: обрезанных подписей нет, мельче `FontSmall` только «en route» при 9:20 (18.1).
  Полные наборы EditMode/PlayMode (старый режим, баланс) не запускались; Web-сборка и GitHub Pages не обновлялись.

## Игра опубликована на GitHub Pages (Codex, 12 октября 2026)

- Адрес игры: https://tigblack.github.io/Cat-Miners/. В публичном репозитории
  `tigblack/Cat-Miners` опубликована WebGL Release-сборка, коммит
  `250960afdc10cf484e103105e0e7f179f9d2c4ae`. Pages использует `main` → `/(root)`, HTTPS включён,
  ссылка сайта установлена в About. Проверены HTTP 200 и статус Pages `built`.
- Добавлены `BuildWeb.cs`, режим `BuildWeb` в `Tools/unity.ps1`, вебшаблон `CatMiners`
  и скрипты `Tools/Web/`. Сборка проходит в `build/ci`, готовые файлы — `Builds/Web`.
  Используются Gzip с Decompression Fallback, хеши в именах файлов и `.nojekyll`.
  Вертикальный экран подстраивается под окно; кнопка полного экрана скрыта в узком портретном окне.
- Вебшаблон включает `autoSyncPersistentDataPath` для IndexedDB. В браузере проверены
  покупка «Кирок» и восстановление уровня после обновления страницы, продолжение той же шахты,
  мобильный размер 390×844 без горизонтальной прокрутки. На публичном сайте в браузере с эмуляцией
  касаний проверены реальный ручной удар, запись `o10_strike` и сохранение того же профиля после перезагрузки.
  Ошибок JavaScript и неудачных запросов к файлам игры в этих проверках нет.
- Вызовы `Handheld.Vibrate` ограничены мобильными сборками, чтобы код компилировался для Web.
  Проверка файловых сохранений в настоящей WebGL-сборке прошла; менять файловое ядро не потребовалось.
- Unity 6000.3.11f1: Release-сборка успешна, итоговый размер 30 858 500 байт.
  Первый проход занял около 30 минут, повтор после правки вебшаблона — 19,7 секунды.
  Баланс опубликованной игры — `outpost-2.1-b4`.
- Инструкция: `Docs/WEB_PUBLISHING.md`. Логи, JSON и снимки —
  `Docs/Reports/github-pages/`, `Docs/Reports/build-web.json` и
  `Docs/Reports/github-pages-publication.json`. Копии файлов до правок —
  `Backups/before-github-pages-2026-10-12/`.

## Отдельный урок ручного удара на первой шахте (Codex, 12 октября 2026)

- По запросу пользователя после автодобычи добавлен шаг `o10_strike`: «Вы тоже можете разбивать камни! Нажмите на
  камень — ударьте своей киркой». В обучении теперь 10 шагов; прежние идентификаторы сохранены.
- Рамка и лапка показывают живой видимый камень, доступный для `Sim.CanStrike`; при разрушении или уходе с экрана
  выбирается другой. Если сначала виден только лагерь, камера один раз плавно подводится к ближайшему камню.
- Шаг засчитывается и сохраняется по `TargetStruck` с положительным уроном на zone1, включая ранний удар;
  автоматические `TargetHit` котиков его не завершают. Предложение показывается 8 видимых секунд, после чего
  остальные уроки продолжаются. В новой сессии первой шахты непройденный шаг предлагается снова.
  Состояние предложения сбрасывается по `RunId`, в том числе при сбросе прогресса в том же контроллере.
- На zone2+ все пропущенные уроки первой шахты завершаются молча, даже если старый профиль уже прошёл шаг сброса.
  В подсказке награды после закрытия итогов указатель направлен на «Экипировку», учитывая прямой переход кнопкой HUD.
- ТЗ §15, RUNTIME_SPEC и UI_API обновлены. Копии девяти файлов до правок —
  `Backups/before-player-strike-tutorial-2026-10-12/`.
- Unity 6000.3.11f1: `OutpostHudTests`, реальный ввод
  `OutpostRuntimeTests.PointerTapOnRock_StrikesWithPickaxe_LongPressShowsInfo`, `OutpostStrikeEvidenceTests` — 3/3 passed,
  exit 0. Проверены отдельная подсказка, указатель, незасчитывание ударов котиков, урон после реального нажатия,
  сохранение нового ID и переход старого прогресса на шахту 2. Генератор portrait повторён отдельно — 1/1 passed;
  снимок 1080×1920 просмотрен: лапка у камня, текст полностью виден. SHA-256 пяти C# файлов CI совпадает с исходниками.
  XML, логи и PNG — `Docs/Reports/player-strike-tutorial/`.

## Крупное и упрощённое окно карточки (Codex, 12 октября 2026)

- По запросу пользователя переработано общее окно всех 61 карточек. Карточка занимает примерно верхнюю половину
  экрана и ширину окна; арт до масштаба ×3, имя — 56 единиц Canvas, уровень, короткий текущий эффект и счётчик копий
  X/Y с увеличенной иконкой — 48. Под карточкой — текущая величина бонуса → следующая, зелёная стрелка вправо.
- Убраны повторные абзацы редкости, ресурса, достигнутой шахты, уже выполненного открытия, «Сейчас / После улучшения»,
  копий и «Нужно копий». Условие открытия остаётся только у закрытой карточки; доступная неполученная показывает
  «Доступна в сундуках», максимум — «Макс. уровень» без сравнения, цены и кнопки.
- «Улучшить» — шрифт 56 и высота 144 вместо 112; цена в эликсире — шрифт 56 и иконка ×2. Причина недоступности
  показывается тостом при нажатии отключённой кнопки. `OutpostCardText.EffectValue` использует `CardDef.EffectAt`:
  проценты, целые бонусы и множитель крита с `BaseCritMultiplier` показываются в своих единицах.
- ТЗ §10.4, RUNTIME_SPEC и UI_API обновлены. Резервные копии восьми файлов до правок —
  `Backups/before-card-detail-layout-2026-10-12/`.
- Проверка Unity 6000.3.11f1: `OutpostCardsUiTests`, `OutpostCardsEvidenceTests.CardStatesCa003` и
  `OutpostCardsEvidenceTests.CardsPortraitsAndUpgrade` — 6/6 passed, exit 0. Все 61 окна замерены в 1080×1920 (9:16)
  и 1080×2400 (9:20) с масштабом CanvasScaler: по 244 подписи, обрезанных — 0. Проверены списание копий/эликсира,
  закрытая, доступная, полученная, улучшаемая, максимальная карточка и нехватка копий. Скриншоты разгрузки, крита,
  длинного имени и максимума просмотрены. SHA-256 четырёх проверенных C# файлов CI совпадает с исходниками.
  XML, лог, замеры и PNG — `Docs/Reports/card-detail-layout/`.

## Крупная кнопка перехода на следующую шахту (Codex, 12 октября 2026)

- По запросу пользователя кнопка HUD «Итоги шахты» заменена на «Перейти на следующую шахту» и запускает переход
  напрямую через общий обработчик и `ContinueToNextMine`. Итоговое окно продолжает показывать награды при завершении.
- Кнопка расширена до всей ширины полосы с полями Safe Area; высота 88 → 168 единиц Canvas, шрифт 32 → 56,
  автоматическое уменьшение выключено, перенос строк разрешён. Новая высота полосы учтена в отступе камеры и обучения.
  Доступность обновляется при `CanContinue` и блокировке команд; повторный тап при переходе не запускает новую шахту.
  На последней шахте открывается завершение кампании, при ошибке записи остаётся повтор сохранения.
- Обновлены ТЗ §11.1, RUNTIME_SPEC и UI_API. Копии девяти файлов до правок — `Backups/before-next-mine-button-2026-10-12/`.
- Проверка Unity 6000.3.11f1: `OutpostHudTests` и реальная сцена `OutpostEvidenceTests.PortraitScene_HudBaseEquipmentCargoAndResults`
  — 2/2 passed, exit 0. В реальной сцене нажатие кнопки HUD дважды переводит только в шахту 2, журнал наград содержит
  одну запись, инварианты проходят. SHA-256 пяти проверенных C# файлов в CI совпадает с исходниками.
  Скриншот portrait 1080×1920 просмотрен: новая надпись крупная, помещается без обрезки. Результат, лог и PNG —
  `Docs/Reports/next-mine-button/`.

## Красные пины улучшения экипировки (Codex, 12 октября 2026)

- По запросу пользователя убрана шкала деталей с карточек экипировки. На полученном предмете ниже максимального
  уровня красный пин без числа показывается, если хватает деталей и команды доступны. На кнопке «Экипировка» — такой же
  пин, если доступно хотя бы одно улучшение. Наличие более сильного ненадетого предмета само по себе пин не включает.
- Открытие коллекции не снимает пины; расход деталей и повышение уровня обновляют их через обычный `RefreshNow`.
  Отметка «надето» перенесена вниз; пин карточки находится внутри верхнего правого угла, чтобы прокрутка его не обрезала.
  `PixelBadge.SetDot(bool)` переиспользует `ui_badge_red` без подписи. Индикатор кнопки создаётся при построении HUD.
- Обновлены ТЗ §10.3, RUNTIME_SPEC, UI_API и PIXEL_STYLE_GUIDE. Копии восьми затронутых файлов до правок —
  `Backups/before-equipment-upgrade-pin-2026-10-12/`.
- Проверка: Unity 6000.3.11f1, PlayMode `OutpostHudTests.HudWindowsCommandsAndTutorialWork` — 1/1 passed, exit 0;
  сценарий открывает экипировку, надевает и улучшает предмет, использует «Надеть лучшее», проверяет переходы и окна.
  SHA-256 трёх проверенных C# файлов в CI совпадает с исходниками. Результат и лог — `Docs/Reports/equipment-upgrade-pin/`.

## Аудит баланса кампании, профиль b4 (Claude, 11 октября 2026)

Задание: найти и исправить слишком трудные, слишком лёгкие, медленные и экономически
несогласованные участки кампании. Отчёт — [Docs/Rework/BALANCE_AUDIT.md](Docs/Rework/BALANCE_AUDIT.md).

- Инструмент аудита на настоящем ядре: `Core/Outpost/Bot/BalancePlayerModel.cs`,
  `BalanceAudit.cs`, `BalanceAuditReport.cs`; генераторы
  `Tests/EditMode/Outpost/OutpostBalanceAuditTests.cs` (`Audit_Before`, `Audit_After`,
  `Audit_Tune`), разбор `Tools/BalanceAudit/analyze.py`, `compare_tune.py`. Данные —
  `Docs/Reports/balance-audit/`. Конфигурации до правок — `configs/before/`.
- Реализационных багов, искажающих баланс, не найдено (20 подозрений, по два
  скептика на каждое); исправлен устаревший тест версии профиля.
- Проблема: кривая сложности шла вниз (эталонный игрок 5,1 → 3,1 мин на шахту при
  ориентире 8–15 к концу), 59 % золота поздних шахт не тратилось, очередь у склада
  16 %. Причины: постоянная относительная цена дерева при росте груза в 11 раз,
  прочность ×1,7 за кампанию, постоянные бонусы ×3,3.
- Профиль `outpost-2.1-b4`: `CostZoneStep = 1/10` (новое поле, цена × (1 + (z−1)/10)),
  `ZoneHpStep = 1/25`, `UnloadStep = 1/5`, «Оценка добычи» — предел 40, пороги
  10/25/40. Исходный JSON, карты, карточки и экипировка не менялись. Текущая шахта
  сохранения доигрывается по своему снимку b3; следующая — b4.
- Итог free: 5,2 / 5,4 / 7,2 / 7,5 / 7,7 / 9,1 мин по этапам, 127 из 144 шахт в
  ориентире (было 18), кампания 8,1 → 19,1 ч; idle 16,2 → 35,8 ч; active 7,2 → 18,5 ч.
- ТЗ обновлено: §0, §9, §12.1, §12.2, §12.4, §12.5, §12.6; CORE_SPEC — профиль и
  формула цены. Резервная копия ТЗ — `Backups/TZ_v2.1-before-balance-audit-2026-10-11.md`.
- Новые проверки: `OutpostBalanceRegressionTests` (формулы b4, предел «Оценки»,
  сохранение b3 → b4 без повторных наград, дерево поздних шахт не дешевеет, точный
  учёт времени аудита, офлайн idle = онлайн, медленный тест темпа кампании) и
  PlayMode `OutpostBalanceSessionTests` (сессия Unity совпадает с повтором в ядре).
- Проверки: ядро dotnet 2462/2462 (7 Explicit); Unity Compile exit 0; Unity EditMode
  2462 passed, 0 failed, 7 skipped; Unity PlayMode полный — все прежние сценарии
  прошли (88), новая сверка сессии исправлена и повторена отдельно; Unity-валидатор
  b4 — 0 ошибок (144 A, максимум шахта 137 — 18,0 ч из 24 ч; кампания C 144/144;
  84 стратегии). Тесты-оракулы и `Docs/TZ/analysis/outpost_v2_audit.py` переведены на
  b4; два рецензента не нашли ослабленных проверок. Двум тестам кампании бота C подняты
  лимиты реального времени (кампания в 4 раза длиннее в логическом времени) и добавлен
  кэш множителя цены зоны.

## Финальная сверка карточек и ТЗ (Codex, 10 октября 2026)

- Завершена проверка продолжения: последний полный Unity PlayMode — 69 passed,
  0 failed, 10 skipped (8 Explicit-генераторов и 2 Windows Input-теста пакета).
  Финальная компиляция — exit 0, без error CS; после правки окна карточки отдельно
  прошли `OutpostCardsUiTests` 4/4 и `OutpostCardsEvidenceTests` 4/4.
  Актуальный EditMode — 2273 passed, 0 failed, 4 Explicit-генератора отчётов/фикстур.
- `Tools/unity.ps1` сохраняет полные XML/логи в `Docs/Reports/outpost/*-all-results.xml`
  и `unity-*-all.log`, фильтры — в отдельные файлы. Генераторы больше не заменяют
  результат полного набора; отсутствие XML/нулевой набор/падения не дают exit 0.
- ТЗ уточнено по фактическим правилам cards-1: скидки до 9/10 и один итоговый ceil,
  цена порции с общим и ресурсным бонусом без повторного применения, отдельный
  бонус удара игрока, предел команды 12+L каски (до 17), стартовые котики в счёт
  предела, офлайн 8+L лежанки (до 15 ч) с фиксацией лимита на начало расчёта.
  Расширены таблицы перехода/профиля и приёмка карточек. `CORE_SPEC`,
  `RUNTIME_SPEC`, README согласованы; бюджетные оценки без карточек помечены явно.
- Источник повторно проверен по SHA-256; каталог ровно 61 ID и шесть редкостей,
  таблица сопоставления покрывает каждый ID. Все 144 изолированные карты, 144
  шахты кампании и 84 сравнения завершены без ошибок; зачислено ровно всё
  выпущенное золото. Актуальные hash перенесены в `BALANCE_V2.md`.
- Кампания валидатора с карточками L1 без улучшений — 10.43 ч (seed 2101001).
  Отдельная модель с улучшениями — 8.22 ч против 13.23 без карточек (seed 1,
  −37.9%). Эти политики разделены в документах. Исправлена формула рюкзака в
  отчёте: `RarityBaseCapacity + (q-1)`, как в коде и ТЗ; высокие уровни карточек
  ограничены и копиями, и эликсиром. Параметры баланса не менялись.
- Из окна игрока убрана техническая пометка происхождения эффекта; источник
  правил остаётся в `CARD_EFFECT_MAPPING.md`. Финальные рендеры пересняты после
  этой правки. GIF обозначен как сценарная проверка: добыча ×20, для улучшения
  есть явный шаг debug-выдачи, затем настоящее списание 2 копий/50 эликсира.
- Автоматическая проверка Editor завершена. Действительные ограничения приёмки
  перечислены в `VALIDATION_V2.md`: Android/телефон, ручное прохождение/звук и
  отдельные замеры максимума карточек (17 котиков, 15-часовой офлайн).
  До правок сохранены архивы `Backups/before-cards-*-2026-10-10.tar.gz`.

## Возвращение карточек (Claude, 9 октября 2026, ночь)

Задание `Cat_Miners_Restore_Cards_Prompt_Opus_5_5.md`: вернуть полную систему 61 карточки источника в автоматический режим (указания удалить карточки отменены). Таблица по каждой карточке — `Docs/Rework/CARD_EFFECT_MAPPING.md` (генератор `Tools/Cards/gen_card_mapping.py`), ТЗ §10.4.

- **Git-истории нет** (ни на хабе, ни на ПК), поэтому восстановление шло по живому legacy-коду (`GameSession`, `GachaService`, `HudBlueprints`, `Formula`) и старому ТЗ v1.3. Подтверждены кодом только коды 9, 10, 7, 1; коды 4, 5, 6, 8, 11, 13, 22, 23 — «предполагаемый смысл» старого ТЗ; остальные 10 — новые правила Cat Miners.
- **Ядро** (`Core/Outpost/Cards/`): `CardCatalog` (61 запись `Cards`, `CardUpgradeCosts`, записи `Gacha`, циклы наград камней, множители зон — точные дроби), `CardService` (выдача, котировка, атомарное улучшение, состояния, `MigrateProfile`, `ImportLegacy`), `CardChestService` (сундуки, эликсир камней), `Config/CardRules.cs` + `Resources/Outpost/card_rules.json` (`cards-1`, пятая конфигурация, входит в CatalogHash). Эффекты 23 видов суммируются один раз в `TeamStats.ApplyCards`; урон и цена по цели кэшируются по индексу цели (`RefreshTargetCaches`); криты — детерминированный SplitMix64. Награды: `DestroyTarget` → `GrantTargetReward` (камень: `RewardCycle` по счётчику профиля; `s`/`p`: `rewardId`), выход — в `Complete`, учебная `ca001` — в `DepositOne`. Команда `UpgradeCard`. Профиль: `CardsVersion`, `Cards`, `Elixir`, `HighestZone`, `RockCycles`, `CardRng`, `CardKeys`; run: `ExtraWorkers`, счётчики сундуков/карт/эликсира; офлайн-итог тоже.
- **Арт:** 61 уникальная иллюстрация `card_art_<id>` 48×44 (`Tools/PixelArt/sprites/card_art.py`), `card_bg_majestic`, `ui_card_frame_majestic`; обзоры `Tools/PixelArt/out/cards_overview.png`, `cards_states.png` (закрытое состояние 30 % чёрного над артом — узнаваемо).
- **Контроллер:** `Configs.WithCards(Balance)`, при загрузке `PrepareCards` (миграция + перенос прежних карточек, копия `pre-cards/` перед первой записью), для новой игры — перенос, debug `GrantTestCards` и кнопка DEV «Cards». Тексты — `Loc.Cards.cs`, `OutpostCardText`.
- **Интерфейс:** нижняя полоса «Экипировка | Карты | База | К базе»; экран «Карты» (`OutpostHud.Cards.cs`) — вся коллекция 61 общего `CardView` (рамка и фон редкости, арт виден всегда, короткий эффект, «Ур. N», состояние, копии X/Y; закрытая — 30 % чёрного только над артом, маленький замок в углу, «Откроется на шахте N»), сортировка, окно сведений (текущий и следующий эффект, копии, цена в эликсире, «Улучшить» только у полученной ниже максимума), бейдж кнопки, сводные тосты сундуков, «+N» эликсира над целью, строки в «Итогах шахты» и «Пока вас не было». Превью панели «База» учитывает карточки.
- **Темп (BALANCE_V2.md «Карточки cards-1»):** первый вариант чисел ускорял кампанию вдвое (поздние шахты на 55–60 %). После замера вкладов ослаблены `ca001`, `ca034`, `ca027`, `ca030`, а котики `ca036` идут в счёт предела «Команды»; итог модели игрока с жадными улучшениями — шахты 1–10 быстрее на 1–16 %, вся кампания на 37,9 %. Все 61 карточка получены к шахте 138, все шесть редкостей — к шахте 10.
- **Миграция настоящего сохранения** проверена на копии (черновик `Tools/CoreTests/Drafts/UserSaveMigrationDraft.cs`, оригиналы только читаются): шахта 1 (24/56, 88 золота, 4 котика) загружается без перезапуска, переносятся `ca001` ур. 1 + 2 копии и 56 эликсира, повторный запуск ничего не добавляет, инварианты и запись чистые.
- **Проверка:** тесты ядра dotnet 2273/2273 (`OutpostCardTests` 15/15); Unity Compile exit 0; EditMode 2277: 2273 passed, 0 failed, 4 Explicit пропущены; PlayMode 79: 69 passed, 0 failed, 10 пропущены (явные генераторы и 2 теста Windows Input), в том числе `OutpostCardsUiTests` 4/4, `OutpostHudTests`, `OutpostRuntimeTests`. Валидатор и рендеры — ниже в VALIDATION_V2.
- **Доказательства:** `Docs/Reports/outpost/screenshots/cards-collection-{zone1,zone3,zone10,states}.png` (вся коллекция одной склейкой), `card-ca003-states.png` (закрыта / доступна / получена), `cards-zone{1,2,3,10,138}.png`, `card-ca003-locked-detail.png`, `card-upgrade-before/after.png`, запись `cards-obtain-upgrade.gif` (кадры Unity: учебная карточка, эликсир, сундук выхода, улучшение). Генераторы — `OutpostCardsEvidenceTests` (Explicit), сборка — `python3 Tools/Cards/make_card_evidence.py`.

## Арена zone1 и удар кирки игрока (Claude, 9 октября 2026, вечер)

Запрос владельца продукта: заполнить первую шахту целями почти целиком (раньше камни стояли только в верхних рядах и в нижнем ряду 11, ряды 6–10 пустовали) и дать игроку бить камни тапом/кликом — с анимацией кирки «из воздуха».

- **Override арены.** Исходный JSON не меняется (ТЗ 8.5): override `zone1-full-arena-1` в `level_overrides.json` (версия `overrides-2`) добавляет 36 целей на свободные клетки рядов 5–10 (32 камня тех же типов/уровней и 4 завала, блок 1×2 закрывает бывшую метку `c`). Итог zone1: 56 целей, Σ HP 3018, 306 порций, 1258 монет. Импорт ставит `AddTokens` только на якоря `.` и отвергает пересечения, выход и строки ворот; адаптер помечает `MapTarget.Added`. Просмотр: `python3 Tools/LevelOverrides/show_zone.py 1 --overrides …`.
- **Run хранит копию override** (`OutpostRun.MapOverride`), карта сохранённой шахты — `OutpostLevelAdapter.ForRun`. Незавершённая шахта со старой геометрией при загрузке перезапускается (`StageProgressionService.RestartRunIfMapChanged`, тост, немедленная запись); профиль, предметы, детали, награды, обучение сохраняются; `ReadyToContinue` не трогается. `AdapterVersion`/`RulesVersion` не менялись: старые сохранения читаются.
- **Удар кирки игрока** — команда ядра `Strike(targetId)` (`Sim/OutpostSimulation.Strike.cs`, ТЗ 6.6): цели текущего сектора и очередные ворота после расчистки сектора; урон `¼ FinalDamage` (профиль баланса `outpost-2.1-b2`, поле `TapDamageFactor`); порции по порогам HP — одной партией на землю в ближайшую свободную клетку (достижимая первой); деньги только при сдаче; разрушение сразу проходит фазу 5. Отказы `phase/disabled/unknown/destroyed/locked` ничего не меняют.
- **Runtime.** Тап/клик по доступной цели — `OutpostController.BeginStrike` → `PlayerStrikeFx` (кадры `fx_strike_pick_0…3`, пул 6), команда выполняется в кадр удара (0,10 с), затем дрожание, сколы, `fx_impact`, звук `Sfx.PlayerStrike`; без немедленной записи. Удержание 0,45 с — сведения. В сведениях цели строка «Нажмите на цель — ударите своей киркой»; подсказка первого шага обучения упоминает тап.
- **Аудит ТЗ** получил `--overrides`: записи `zones` остались исходными, итог override — в `level_overrides`, `static_clearance_totals` (источник 22 129, с override 22 165, ворот 280).
- **Тесты.** Новые `OutpostStrikeTests` (10) и `OutpostOverrideTests` (8), два PlayMode-теста в `OutpostRuntimeTests` (тап мышью по камню и удержание; старое сохранение zone1 перезапускается на арене), генератор рендеров `OutpostStrikeEvidenceTests` (Explicit). Обновлены проверки версий b2, `Overrides_OnlyNamedZone1Arena_MatchAudit`, статическая доступность 22 165.
- **Результаты:** чистые тесты ядра (dotnet, все EditMode вместе со старыми) — 2258 passed, 0 failed, генераторы пропущены; Unity Compile exit 0; Unity EditMode — 2262: 2258 passed, 0 failed, 4 Explicit пропущены; Unity PlayMode — полный прогон 70: 64 passed, 1 failed, 5 skipped; упал старый `OfflineSaveRetry_AccumulatesOriginalSummary` (ждал завершения zone1 за 10 минут офлайна, арена идёт 638,4 с) — окно увеличено до 15 минут, повтор `OutpostRuntimeTests` 16/16. Unity `Validate`: exit 0, 0 ошибок — 144/144 изолированных A, кампания C 144/144 (13,45 ч), 84/84 сравнений, геометрия 22 165 `r/s/x` и 280 ворот; отчёты `Docs/Reports/outpost/*.json` обновлены, `summary.json` содержит hash overrides. Рендеры `strike-01…05.png` проверены глазами: арена 0/56, кадры замаха, взмаха, удара и отскока.
- **Темп zone1 с ареной:** A 638,4 с (первая доставка 9,7 с), A + 5 ударов/с 431,2 с, C 256,8 с (первая покупка 11,5 с). Ориентир «2–4 минуты» превышен — если в ручной проверке шахта покажется длинной, следующий шаг — понизить уровни части добавленных камней новым именем override.
- Резервная копия изменённых файлов до правок (git в проекте нет): `Backups/before-arena-tap-2026-10-09.tar.gz`.

## Продолжение интеграции (Codex, 9 октября 2026)

- Режим по ТЗ 2.1 подключён по умолчанию в `CoreGame`: контроллер Unity, ввод, звук, lifecycle/офлайн, переходы, профили, HUD, представление, обучение и инструменты работают вместе. Старые сценовые тесты включают LegacyMode явно.
- Итоговая компиляция в настоящем Unity 6000.3.11f1 прошла с exit 0; проверка `UnityCheck` также прошла. Для запусков используется `build/ci`, открытый пользовательский Editor не закрывался.
- Повторный независимый аудит исходного JSON записан в `Docs/Reports/outpost/source-audit.json`. SHA-256 источника совпадает с ТЗ; исходник не изменён.
- Исправлены два дефекта нового SaveService: после загрузки из backup первая запись сохраняет выбранный валидный backup; ошибка ввода-вывода `Replace` возвращается вызывающему без разрушительного fallback (операция могла уже переместить main в backup).
- Добавлены 6 регрессионных случаев: отказ `Replace` без изменений и после переименования main, а также сбои каждого шага записи после восстановления повреждённого main или более нового backup, с поддержкой и без поддержки `Replace`. Все 6 воспроизводили дефект до исправления; после исправления 30/30 тестов `OutpostSaveTests` прошли.
- Исправлено пробуждение пустого `GoingHome` при освобождении доступного груза: новый выбор в ближайшей клетке без прерывания текущего ребра. Два найденных незавершённых теста этого правила использовали поиск редкого состояния в 30 картах; они переведены на согласованную детерминированную фикстуру с теми же строгими утверждениями, оба проходят.
- Разность UTC для офлайна вычисляется без переполнения; две крайние границы Int64 проверяют нулевой интервал назад и cap 8 часов вперёд.
- Реализован `MoveStuckTicks`: если у движущегося котика потерян таймер или некорректно ребро, через 3 с один раз восстанавливается путь либо снимается некорректная задача. Нормальная непрерывная доля медленного ребра не считается зависанием. Положение, груз и награды восстановление не меняет.
- Исправлены и проверены переход без лишней офлайн-сводки, учёт времени порционного расчёта, объединение сводки после ошибки сохранения, повтор подготовленного commit и защита Debug Finish от одновременного New Debug. Число котиков больше не обрезается портретным HUD.
- Итоговые чистые тесты нового ядра: **1920/1920**, 0 пропусков. В Unity: **2240 EditMode**, 0 ошибок, 4 явных генератора пропущены; **63 PlayMode**, 0 ошибок, 5 пропусков (3 явных генератора и 2 нестабильных теста Input System). Все 16 обычных Outpost-тестов сцены/контроллера/HUD прошли.
- Новый Unity-валидатор: **144/144** карт с политикой A, **144/144** последовательной кампании C и **84/84** сравнений A/B/C с пустой/заработанной экипировкой, 0 ошибок. Инварианты проверяются после каждого события; источник JSON неизменён. Основной бот C проходит кампанию за 13.439 ч; исторический тест PolicyCB ниже использует другую частоту покупок.
- Сохранены семь портретных рендеров окон, шесть снимков нагрузки и **75.13 с MP4 со звуком**: обычный Main, реальные часы ×1, покупки за доставленные деньги, итоги, экипировка и начало второй шахты. Запись прошла отдельным Unity-тестом; из ожидания вырезан один промежуток, ускорения нет. Каталог визуальных ресурсов теперь воспроизводимо включает новые семейства; генератор атласа прошёл `--check`.
- Финальный тест производительности Editor прошёл: зоны 50/101/144, 12 котиков, два обслуживания, очереди и партии груза; Main не изменён. Медиана кадра 16.65–16.66 мс при 640×480, офлайн 8 ч в сильном Debug-комплекте 31.60 мс, 200 шагов активного ядра между событиями — 0 байт. Замеры полного Editor GC не подтверждают бюджет игрового кадра 1 КБ.
- Итоговые документы: [запуск](Docs/Rework/README_REWORK.md), [формулы и измеренный баланс](Docs/Rework/BALANCE_V2.md), [проверки и артефакты](Docs/Rework/VALIDATION_V2.md). Реализация и доступные автоматические проверки завершены. Приёмка Android/IL2CPP, ручной UX/звук и абсолютный максимум партий остаются непроверенными; APK заблокирован отсутствующими SDK, NDK и OpenJDK. Измеренный b1 ещё требует продуктовой настройки темпа. Все ограничения явно перечислены в VALIDATION_V2.

## Архив этапа «Ядро CatOutpostV2» (8–9 октября 2026)

Ниже сохранены результаты и точка продолжения предыдущего этапа. Текущий статус,
итоговые числа и ограничения находятся выше и в VALIDATION_V2; вопросы о ещё не
подключённом runtime и непроверенной компиляции Unity уже закрыты.

### Что сделано

**Ядро** (чистый C#, сборка `CatMiners.Core`, без UnityEngine):
- код в `Assets/Game/Scripts/Core/Outpost/`: Math, Config, Level, Sim, Save, Bot;
- четыре конфигурации в `Assets/Game/Resources/Outpost/`;
- встроенные копии конфигураций в `Config/OutpostConfigSet.Defaults.cs`; их генерирует `Tools/gen_outpost_config_defaults.py`.

**Тесты** — 16 файлов в `Assets/Game/Tests/EditMode/Outpost/`:
- дымовые тесты реализации: `OutpostCoreSmokeTests`;
- набор A: помощники `OutpostTestHelpersA`, тесты Math, Config, Import, Equipment, Reward, Save;
- набор B: помощники `OutpostTestHelpersB`, тесты Simulation, Ordering, Determinism, Upgrade, Progression, Offline и медленный `OutpostCampaignSlowTests` (категория `OutpostSlow`).

Черновики из `Tools/CoreTests/Drafts/` перенесены в `Assets`, папка Drafts удалена. Отладочный `ZzDebug.cs` удалён. `.meta` созданы через `Tools/gen_meta.py`.

**Интеграция** — первая сессия прервана, вторая завершила работу:
- Все метки `// API?` в тестах приведены к настоящим именам ядра. Сделано в прерванной сессии, черновики не сохранились, сравнить нельзя. Проверено поиском: в тестах нет `Ignore`, `Explicit`, `Assert.Pass` и закомментированных утверждений.
- Сделано в прерванной сессии: `EquipBest` без изменений теперь возвращает `Ok`, а не отказ `"unchanged"` из отчёта реализации. Состояние и события при этом не меняются. Так требует тест кампании: «Надеть лучшее» разрешено в `ReadyToContinue`.

**Исправление ядра** — смена рюкзака на меньший во время шахты:
- По ТЗ §6.2 добыча продолжается, «пока … есть место в сумке». По §6.3 п.1, если `cargoCount >= FinalCapacity`, подбор и добыча запрещены до доставки. По §10.3 груз сохраняется, котик завершает доставку.
- Раньше ядро после снятия или замены рюкзака давало котику ещё один удар по цели, а подбирающий котик продолжал подбор. SPEC этот случай не описывает, поэтому прав ТЗ.
- Теперь `OutpostSimulation.StopOverCapacity` выполняется в фазе 7 после пересчёта характеристик:
  - `Mining` и `PickingUp` сразу получают новую задачу (возврат к базе); резерв и рабочая клетка снимаются;
  - `MovingToWork` и `MovingToCargo` получают `TaskInvalid`, доходят до ближайшей клетки без телепортации (§6.4) и идут к базе;
  - у `MovingToWork` рабочая клетка освобождается сразу.
- Затронутые файлы:
  - `Sim/OutpostSimulation.Tasks.cs` — `Plan` и `StopOverCapacity`;
  - `Sim/OutpostSimulation.Tick.cs` — ребро и прибытие для `MovingToCargo` с `TaskInvalid`;
  - `Sim/OutpostState.cs` — комментарий к `TaskInvalid`.

**Правки тестов в этой сессии** (только ужесточение):
- `OutpostUpgradeTests.BackpackSwapToSmaller_OverflowCargoKept_…`: убрано допущение «ещё один удар по текущей цели». Теперь сразу после команды котик идёт к базе, рабочая клетка освобождена, до доставки в сумке только прежние порции.
- Новый `BackpackSwapToSmaller_PickupOrWalkingWorker_NoNewCargoUntilDelivered`, 3 случая: `PickingUp`, `MovingToCargo`, `MovingToWork`.
- `OutpostTestHelpersB.ExtraViolations`: проверка §6.3 «полная сумка — без добычи и подбора» теперь действует и после уменьшения вместимости. Исключение одно: идущему разрешено с `TaskInvalid` дойти до ближайшей клетки.
- Четыре новых и изменённых проверки падали без исправления ядра и проходят с ним (красный → зелёный проверен).
- `OutpostCampaignSlowTests` выводит диагностику темпа: время каждой зоны и сводку кампании C. Утверждения не менялись.

### Результаты тестов (9 октября 2026, `Tools/CoreTests/run.ps1`)

| Набор | Фильтр | Всего | Пройдено | Провал | Пропуск |
|---|---|---:|---:|---:|---:|
| Быстрый | `FullyQualifiedName~Outpost&TestCategory!=OutpostSlow` | 1763 | 1763 | 0 | 0 |
| Медленный | `TestCategory=OutpostSlow` | 145 | 145 | 0 | 0 |
| Все EditMode, вместе со старыми | без фильтра | 2228 | 2228 | 0 | 0 |

Ещё 4 старых генератора отчётов помечены `[Explicit]` и не запускаются; это штатно. Старые тесты не сломаны.

**Медленный набор** — около 27 с реального времени. Политика A: без покупок и экипировки, 2 котика.
- Пройдено 144 зоны из 144, провалов нет. В каждой зоне: watchdog не сработал, весь груз сдан, `TotalCredited = base_gold` аудита, ворота открыты, награда выдана один раз.
- Самая долгая зона — zone133: 18 855.9 с (≈5.24 ч) из лимита 24 ч, 83 109 событий из лимита 2 000 000.
- Медиана — zone59, 8 244.5 с. Быстрее всех zone1, 346.0 с. Сумма по всем зонам ≈344.1 ч.

**Кампания 1→144, политика C** (`PolicyCB`), с наградами, дубликатами, «Надеть лучшее» и сбросом между шахтами — пройдена:
- zone1 — 174.5 с;
- медиана — 390.8 с;
- максимум — zone126, 516.9 с;
- всего ≈15.57 ч.

### Открытые вопросы

1. `MoveStuckTicks` реализован при продолжении Codex (см. выше); на валидных текущих картах движение непрерывно и не блокируется котиками.
2. `LevelOverrides` хранит только имя переопределения; геометрические переопределения не применяются. Сейчас файл переопределений пуст.
3. Ядро и тесты проверены только через `dotnet` (netstandard2.1 / C# 9). Компиляция в Unity не проверялась, хотя API тестов совместимы с профилем Unity.
4. Решения на стыке ТЗ и SPEC, принятые как допустимые:
   - «Команда» ×10/Макс отклоняется с причиной `"mode"` — ТЗ §9: «для найма достаточно ×1»;
   - в `ReadyToContinue` ядро не планирует `GoingHome`, все таймеры пусты — по §11.3 возвращение декоративное, это задача runtime;
   - добывающий, чью цель разрушил другой котик, перепланируется сразу — §6.2.
5. Темп по §12.6 — ориентиры, не требования, но стоит учесть при балансировке. Первая доставка в zone1 — 9.7 с (ориентир 10–20 с), первая покупка в политике C — около 11.5 с (ориентир 20–35 с). Ранний темп чуть быстрее ориентиров.
6. Параллельно идёт этап runtime: появилась папка `Assets/Game/Scripts/Runtime/Outpost/`. `gen_meta.py` создал для неё `.meta`, сами файлы этот этап не трогал.

### Точка продолжения

- Ядро готово, и все наборы зелёные; следующий этап — runtime и UI нового режима поверх `OutpostSimulation`, `OfflineSimulator`, `OutpostSaveService` и `StageProgressionService`.
- Команды проверки:
  - `powershell -ExecutionPolicy Bypass -File Tools/CoreTests/run.ps1 -Instance <имя> -Filter "FullyQualifiedName~Outpost&TestCategory!=OutpostSlow"` — около 15 с;
  - `... -Filter "TestCategory=OutpostSlow"` — около 30 с;
  - без фильтра — все EditMode, около 1.2 мин.
- Первым делом при открытии проекта в Unity — проверить компиляцию сборок `CatMiners.Core` и `CatMiners.Tests.EditMode`.

## 10 октября 2026 — ветвящиеся шахты

Новая топология реализована в игре для всех 144 шахт. Прямое задание владельца
отменяет сохранение прямоугольных арен и автоматический сброс активной шахты
при изменении геометрии. Актуальные документы:
[MINE_TOPOLOGY.md](Docs/Rework/MINE_TOPOLOGY.md) и
[VALIDATION_TOPOLOGY.md](Docs/Rework/VALIDATION_TOPOLOGY.md).

- Добавлены отдельный `mine_topology.json` и воспроизводимый генератор; все
  144 маски различаются, содержат 1420 камер и 184 локальные петли.
- Постоянная порода, реальные footprints, рабочие позиции и физические ворота
  используются навигацией и view. Котики распределяются между ветвями;
  ручная добыча учитывает доступность, груз появляется на достижимом полу.
- Новая шахта сохраняет полный снимок топологии. Старая активная шахта сохраняет
  геометрию и прогресс; следующая получает новый план. JSON-источник не изменён.
- View строит стены, срезы, тени, опоры, фонари и секторный туман по той же маске;
  исправлены выбор клетки постоянной породы и возврат камеры к базе.
- Пройдены 166/166 целевых проверок ядра и парные кампании A/C. Общий темп
  независимых A изменился на +16,24%, кампании C с карточками — на +8,02%.
- Все обычные проверки Unity выполнены успешно с отдельным повтором двух
  сценариев времени: 2427 различных EditMode и 70 различных PlayMode.
  Полные XML и повторы сохранены раздельно; подробности в отчёте проверки.
- Финальный Unity-валидатор после каждого события: exit 0, все 144 геометрии,
  144 самостоятельные A, 144 последовательные C и 84 сравнения стратегий
  прошли без ошибок и watchdog.
- Окончательный генератор Unity: 2/2 passed; сохранены шесть кадров шахт
  1/48/144 во время добычи и после полной расчистки. Исходник, все XML,
  результаты 372 прогонов и метаданные кадров сверены в
  [final-validation.json](Docs/Reports/topology/final-validation.json).

Прежние записи этого файла описывают исторические этапы. Утверждения о
сохранении прямоугольной геометрии, пустых overrides и отсутствии проверки
Unity из ранних этапов к текущей реализации не относятся.
