# Cats Cute Miner (Cat Miners) — прототип основного геймплея

Unity 6000.6.4f1, URP 17.6, Input System 1.20, uGUI 2.6 + TextMeshPro, Newtonsoft JSON 3.2.2.
ТЗ: `Docs/TZ/Cat_Miners_Unity_Core_TZ_v1.3.md`.

## Запуск

1. Откройте проект в Unity 6000.6.4f1 (версия из `ProjectSettings/ProjectVersion.txt`).
2. После первой компиляции редактор сам выполнит **Cat Miners/Setup Project** (`AutoSetup`). Настройка:
   - создаст папки `Assets/Game/{Scripts,Data,Scenes,Prefabs,Art,Audio,Tests,Input}`;
   - распакует TMP Essential Resources;
   - создаст четыре материала URP (`Assets/Game/Art/Materials`: `CatMiners_Lit`, `CatMiners_Unlit`, `CatMiners_Particles` и `CatMiners_Sprite` для спрайтов — назначается в `GameBootstrap.SpriteMaterial`) и сцену `Assets/Game/Scenes/CoreGame.unity`;
   - назначит ассет ввода `Assets/Game/Input/CatMinersInput.inputactions` (карты `Gameplay` и `UI`) как project-wide actions и удалит шаблонный `Assets/InputSystem_Actions.inputactions`;
   - выставит портретную ориентацию, идентификаторы приложения, IL2CPP/ARM64 и Build Settings (только `CoreGame`);
   - удалит шаблонные `TutorialInfo` и `Readme.asset`.

   Повторить настройку: при открытом Editor — меню **Cat Miners → Setup Project**; при закрытом — `powershell -ExecutionPolicy Bypass -File Tools/unity.ps1 -Mode Setup -Target Real`. В batchmode `AutoSetup` не срабатывает, поэтому `unity.ps1` сам запускает Setup перед `PlayMode` и `BuildApk`, если в проекте нет `ProjectSettings/CatMinersSetup.json` или сцены `CoreGame`.
3. Откройте `Assets/Game/Scenes/CoreGame.unity` (если не открылась сама) и нажмите **Play**. Начнётся новая игра или продолжится сохранение.

Поле, объекты и котики строятся в рантайме из данных в пиксельном стиле (`Docs/Art/PIXEL_STYLE_GUIDE.md`; арт-дирекция, каталог и визуальная приёмка — `Docs/Visual/`). Ассеты устроены так:
- **Спрайты** рисуются кодом в `Tools/PixelArt/sprites/*.py`. `python3 Tools/PixelArt/build.py` проверяет палитру, упаковывает атлас и пишет `Runtime/Pixel/PixelAtlasData.g.cs` — PNG в base64, потому что PNG не синхронизируются с хабом.
- **Звуки** — записанные CC0-сэмплы, оригинальная музыка и атмосфера (синтез кодом, `Tools/Audio`) в `Assets/Game/Resources/Audio/*.ogg`, только на ПК. Список, группы громкости и лицензии — `Docs/Audio/SOUNDS.md`. Без файлов `Sfx` играет мягкий синтезированный запасной звук, а музыка и атмосфера молчат.
- **Оформления шахт** — диапазоны зон в `Assets/Game/Resources/Visual/environment_themes.json` (тёплая штольня, кристальная пещера, медная выработка).
- **Шрифт** — Nunito SemiBold и ExtraBold (OFL) в `Assets/Game/Resources/Fonts/`, только на ПК.
- **Генерируемые ассеты** — четыре материала URP (в том числе `CatMiners_Sprite.mat` для спрайтов) и сцена `CoreGame`. Ассет ввода хранится в проекте текстом и подключается Setup.

Идентификаторы (не менять после первой установки на устройство — от них зависит путь сохранений):
`companyName = CatMiners`, `productName = Cats Cute Miner`, `applicationIdentifier = com.catminers.prototype`.

## Управление

Мышь и касания работают одинаково.

| Действие | Как |
|---|---|
| Выбор камня, шахты, ворот, кузницы, котика | Тап |
| Активировать расчищенную шахту, открыть разрушенные ворота, забрать выплату | Тап по объекту |
| Открыть бочку с нанятым котиком, контейнер бесплатного котика, ящик доставки | Тап по бочке или ящику: котик выходит в той же клетке, валюта или динамит из ящика начисляются |
| Собрать добычу с камня (монеты, эликсир, кристалл) | Тап по добыче: монетки летят к счётчику. Через 30 с игрового времени добыча собирается сама. Если под пальцем несколько целей, приоритет: добыча → пузырь над объектом → бочка/ящик → котик → объект |
| Назначить котика на цель | Перетащить котика на цель или на клетку рядом с ней |
| Объединить котиков | Перетащить котика на котика того же уровня |
| Прокрутка шахты | Перетаскивание по фону (с инерцией), колесо мыши |
| Автопрокрутка | Тащите котика или динамит к верхнему/нижнему краю поля. Параметры — поля `PointerInput.AutoScrollEdgeFraction` (доля высоты поля у края, по умолчанию 0.15) и `PointerInput.AutoScrollMaxRowsPerSec` (скорость у самого края, по умолчанию 12 строк/с) |
| Динамит | Перетащить из кнопки «Динамит» на цель |
| Найм | Золотая кнопка «Нанять» внизу (цена, уровень котика, шкала уровня найма). Котик приходит в бочке на поле |
| Улучшения | Тап по кузнице или шахте → «+1» или «До порога» |
| Чертежи и бесплатный сундук | Кнопка с карточками справа внизу (красный бейдж — готов бесплатный сундук) |
| Следующая шахта | Кнопка вверху появляется, когда выход открыт и все шахты активированы |
| Звук и вибрация | Окно настроек по шестерёнке в верхней панели; значения хранятся в сохранении игрока (`PlayerProgressState.Settings`: `SoundOn`, `VibrationOn`) и переносятся между зонами |

Ввод: `Assets/Game/Input/CatMinersInput.inputactions` — карта `Gameplay` (указатель, нажатие, колесо; её читает `PointerInput`) и карта `UI` (для `InputSystemUIInputModule`). Setup назначает ассет project-wide; шаблонный `InputSystem_Actions` удаляется.

## Инструменты разработки

Кнопка **DEV** слева на экране доступна в Editor и Development Build (сборка `CatMiners.DevTools`, `defineConstraints: UNITY_EDITOR || DEVELOPMENT_BUILD`). Все инструменты, кроме выбора слота, работают только в отдельном слоте **Debug** и не трогают основное сохранение: в слоте Main кнопки отвечают подсказкой, ускорение сбрасывается на ×1, сетка и окно расчёта закрываются.

- выбор любой из 144 зон («Перейти (Debug)»), новый чистый слот Debug, возврат в основной слот;
- ускорение ×1 / ×5 / ×20 / ×100;
- выдача эликсира, котика выбранного уровня, динамита, карточек зоны;
- «+золото» = 10 × цена следующего улучшения самой дорогой шахты зоны: ещё не активированная шахта считается с уровня 1, шахты на максимальном уровне пропускаются, нижней границы нет; если шахт нет или все на максимуме — берётся цена кузницы (об этом сообщает подсказка);
- принудительное завершение выбранной цели;
- «Сетка» — наложение, один квадрат на клетку; при совпадении цвет по приоритету:
  1. рабочие клетки выбранной цели — зелёные;
  2. клетка появления (`FindSpawnCell`) — синяя;
  3. якоря объектов — жёлтые;
  4. остальные клетки footprint — оранжевые (у разрушенных камней якорь и footprint приглушены);
  5. площадка появления `SpawnPad` (`GameSession.SpawnPadRows`: три строки над верхними открытыми воротами или стартовая полоса; клетки кузницы не входят) — голубые;
  6. строки ворот — красные, у открытых ворот тёмно-красные (якорь ворот показывается как якорь);
  7. остальные клетки — тон сектора: оттенок по номеру сектора, закрытый сектор темнее;
- «Формулы» — цепочка расчёта «база → правило Приложения C и аргумент → модификаторы → результат» для выбранного объекта. Значения считает `Formula.Evaluate` по полям данных и публичные методы `GameSession`:
  - котик — урон, задержка, криты (E-02);
  - генератор — выход за цикл (фактический FormulaType), пороги, manager-карты, ворота и карты, время цикла, следующий порог и его эликсир (E-04), цена `ceil(F-2)`;
  - камень — HP и каждая запись цикла наград до выплаты `max(1, floor(…))`: Core (секунды F-3 × ставка E-10), Soft (F-0 × `MiningSoftCurrencyMultiplier`), Hard (`Quantity`), сундук (содержимое, если открыть следующим), исключённые типы («не выдаётся»);
  - ворота — бонусы E-08 (коды 10/2/7/1 за одни ворота и итоги) и сундук награды;
  - выход — награда E-13;
  - неактивированная шахта — сундук активации и параметры уровня 1;
  - без выбора — найм `ceil(F-1(x = n + 1) / делитель)` и уровень `1 + floor(F-0(x = n))`, лимит котиков, бесплатный котик (интервал по слагаемым и уровень, E-06), динамит (E-16, если есть заряды), интервал доставок (E-15);
- «Валидатор» — `BalanceValidator` на загруженном балансе прямо в плеере (проверка IL2CPP, ТЗ 5.1, см. ниже).

Исходный токен и стабильный ID выбранного объекта (`zoneN:индекс`) показываются только в слоте Debug: в контекстной панели и в цепочке расчёта.

### Валидатор на устройстве (IL2CPP)

1. Соберите APK (`Tools/unity.ps1 -Mode BuildApk`) и установите Development-сборку: `adb install -r Builds\Android\CatMiners-0.1.0-dev.apk`.
2. В игре: **DEV → Новый Debug → Валидатор**. Окно покажет «Зон: N, ошибок: E, предупреждений: W, SHA-256: …», платформу, версию Unity и бэкенд (`IL2CPP`).
3. Строка в логе: `adb logcat -s Unity | findstr PlayerValidation` — `[CatMiners] PlayerValidation zones=… errors=… warnings=… sha256=… hashOk=… platform=Android unity=… backend=IL2CPP …`.
4. Отчёт (поля как в `validation.json` плюс `backend`, `platform`): `adb pull /sdcard/Android/data/com.catminers.prototype/files/validation-player.json Docs/Reports/validation-player.json`.

## Проверки без GUI

Unity, тесты и сборка работают на ПК; с хаба — через `on-remote`.

| Команда | Что делает |
|---|---|
| `powershell -ExecutionPolicy Bypass -File Tools/CoreTests/run.ps1` | Быстрая сборка `CatMiners.Core` (netstandard2.1, C# 9) и EditMode-тесты через `dotnet test` |
| `… -File Tools/CoreTests/run.ps1 -Filter BotZones1To10Report` | Сценарный бот по zone1–10, отчёт `Docs/Reports/bot-zones-1-10.md` |
| `… -File Tools/CoreTests/run.ps1 -Filter EconomyTablesReport` | Таблицы Приложения C.4, `Docs/Reports/economy-tables.md` |
| `… -File Tools/CoreTests/run.ps1 -Filter WriteValidationReport` | Собрать `Docs/VALIDATION_REPORT.md` из отчётов `Docs/Reports` (после прогонов Unity) |
| `… -File Tools/UnityCheck/run.ps1` | Быстрая проверка компиляции Runtime, DevTools, Editor и PlayMode-тестов без Unity (`dotnet build`) |
| `… -File Tools/unity.ps1 -Mode Compile` | Компиляция всех сборок в Unity. Успех — код 0 и ни одной строки `error CS` в логе (иначе код 1). Лог: `Docs/Reports/compile.log` (копия `unity-compile.log`) |
| `… -File Tools/unity.ps1 -Mode EditMode` | EditMode-тесты в Unity, `Docs/Reports/editmode-results.xml` |
| `… -File Tools/unity.ps1 -Mode PlayMode -BuildTarget Android` | PlayMode-тесты сцены `CoreGame` (smoke, кнопки, перезапуск, слот Debug zone5–144, ввод, производительность с Quality = Mobile) и скриншоты 1080×1920 в `Docs/Reports/screenshots/` (на ПК). `-TestFilter <полное имя>` — один тест |
| `… -File Tools/unity.ps1 -Mode Validate` | Валидатор баланса, `Docs/Reports/validation.json` |
| `… -File Tools/unity.ps1 -Mode Setup -Target Real` | `ProjectSetup` в самом проекте (Editor должен быть закрыт) |
| `… -File Tools/unity.ps1 -Mode BuildApk` | Два APK (internal only) в `Builds/Android/` и отчёт `Docs/Reports/build-android.json`, см. «Сборка Android» |

Если проект открыт в Editor, `-Target Auto` выбирает изолированную копию `build/ci`: Assets, Packages и ProjectSettings зеркалятся туда, Library у копии своя. Перед `PlayMode` и `BuildApk` в копии выполняется Setup. Отчёты и APK из копии всё равно пишутся в `Docs/Reports` и `Builds/Android` проекта (аргументы `-catminersReports` и `-catminersOutput`).

### Сборка Android

`Tools/unity.ps1 -Mode BuildApk` вызывает `CatMiners.Editor.BuildAndroid.Apk()` (`-batchmode -quit -buildTarget Android -executeMethod …`):

1. До запуска Unity проверяется модуль Android Build Support: `<Editor>/Data/PlaybackEngines/AndroidPlayer` и его `SDK`, `NDK`, `OpenJDK` (или внешние пути из Preferences → External Tools). Если чего-то нет, `build-android.json` получает `result = ModuleMissing` и список `missing` (например `["SDK", "NDK", "OpenJDK"]`), код выхода 3. Ту же проверку повторяет `BuildAndroid.Apk` внутри Unity. Установить: Unity Hub → Installs → Add modules → Android Build Support (Android SDK & NDK Tools, OpenJDK).
2. За один запуск собираются два APK (APK, а не AAB; debug keystore; только сцена `CoreGame`; IL2CPP, ARM64):
   - `CatMiners-<версия>-dev.apk` — Development Build с инструментами (`variant = development`);
   - `CatMiners-<версия>.apk` — сборка без `DEVELOPMENT_BUILD`, в неё `CatMiners.DevTools` не попадает (`variant = noDevTools`).
   Перед каждой сборкой скрипты плеера компилируются отдельно (`PlayerBuildInterface.CompilePlayerScripts`, необязательная быстрая проверка): строки `error CS` или найденная `CatMiners.DevTools` в варианте без инструментов — провал варианта. Сборки ищутся и по именам из `ScriptCompilationResult.assemblies` (с `.dll` и без), и по DLL в каталоге вывода. Если проверка бросила исключение или не нашла `CatMiners.Runtime`, это только предупреждение (`warnings`, `scriptCheck = Exception/Inconclusive`): APK всё равно собирается, а `CatMiners.DevTools` ищется среди файлов `BuildReport` (`devToolsSource = BuildReport`).
3. APK кладутся в `Builds/Android/` проекта на ПК (каталог не синхронизируется на хаб).
4. `Docs/Reports/build-android.json`: общий `result` (`Succeeded`, только если собраны оба), бэкенд, архитектуры, идентификатор, версия и массив `builds` — для каждого варианта `variant`, `options`, `result` (`BuildReport.summary.result`), `path`, `sizeBytes`, `totalSize`, `sha256`, `totalErrors`, `totalWarnings`, `errors` (сообщения шагов `BuildReport`), `warnings`, `devToolsIncluded` и `devToolsSource`, `scriptCheck` (`Ok`, `Errors`, `Inconclusive`, `Exception`), `playerAssemblies` (имена как их вернул Unity) и `playerAssemblyFiles`, `internalOnly`, `builtAtUtc`.
5. После сборки лог `Docs/Reports/unity-buildapk.log` проверяется на missing script (`The referenced script … is missing`, `Missing script`): найденные строки попадают в `missingScriptLines`, код выхода — 4.

Коды выхода: 0 — оба APK собраны и missing script нет; 1 — ошибка сборки; 3 — нет модуля Android; 4 — missing script в логе. Установка на устройство — только если его видит `adb`; иначе статус «APK собран, на устройстве не проверен». APK — internal only (ТЗ 4.5).

## Данные

- Исходный баланс: `Assets/Game/Data/Source/json-data.json`, хранится побайтно, SHA-256 `8975a5a0…85ee5`. Проверяется при старте и в тестах.
- Внутренний ID `tinygoblin` в интерфейсе показывается котиком; имена шахт, сундуков и чертежей — в `PresentationCatalog`.
- **JSON — конфигурация коммерческой игры, сборки с ним только для внутренней проверки (internal only).** См. ТЗ 4.5.

## Структура

| Путь | Содержимое |
|---|---|
| `Assets/Game/Scripts/Core` | `CatMiners.Core`: DTO, загрузка и валидатор баланса, `GameNumber`, формулы профиля v1, разбор сеток, симуляция, сохранение, офлайн, сценарный бот, словарь строк UI (`Core/Text/Loc*.cs`, ТЗ 5.1). Без UnityEngine |
| `Assets/Game/Scripts/Runtime` | `CatMiners.Runtime`: сцена, вид поля, котики, камера, ввод, HUD, обучение |
| `Assets/Game/Scripts/DevTools` | Инструменты разработки (только Editor и Development Build) |
| `Assets/Game/Scripts/Editor` | Настройка проекта, валидатор CLI, сборка APK, распаковка `.unitypackage` |
| `Assets/Game/Input` | Ассет ввода `CatMinersInput.inputactions` |
| `Assets/Game/Tests` | EditMode (ядро) и PlayMode (сцена) |
| `Tools/` | `unity.ps1`, `CoreTests/`, `UnityCheck/`, `gen_meta.py` (детерминированные `.meta`), `PixelArt/` (генератор пиксельных спрайтов и атласа) |
| `Docs/` | ТЗ, этот README, `BALANCE_MAPPING.md`, `VALIDATION_REPORT.md`, `IMPLEMENTATION_NOTES.md`, отчёты |

### Модули ТЗ 5.2 → код

Экономика — один класс `GameSession` (partial-файлы в `Core/Sim`), чтобы команды UI и тестов шли через одну транзакцию; роли модулей ТЗ разнесены по файлам и методам:

| Модуль ТЗ 5.2 | Где в коде |
|---|---|
| `BalanceRepository` / `BalanceValidator` | `Core/Data/BalanceRepository.cs`, `Core/Data/BalanceValidator.cs` (DTO — `BalanceDto.cs`) |
| `FormulaEvaluator` | `Core/Numbers/Formula.cs` (`Formula.Evaluate`, F-0…F-3, `Price`, `Payout`) |
| `ZoneParser` | `Core/Grid/ZoneLayout.cs` (`ZoneLayout.Build`: разбор сетки в два прохода, логическое поле) |
| `ZoneBuilder` (представления) | `Runtime/Visual/BoardView.cs` (`BoardView.Build`); виды объектов карты — `Runtime/Visual/ObjectView.cs` (`ObjectView`); котики, контейнеры, полоски HP и подписи — `Runtime/Visual/Views.cs` (`CatView`, `ContainerView`, `HpBar`, `Label3D`, `Pickable`) |
| `GridOccupancy` | `ZoneLayout.CellOwner` / `CellSector` + `GameSession`: `IsCellFree`, `WorkCells`, `SpawnPadRows`, `FindSpawnCell` (`Core/Sim/GameSession.cs`) |
| `CatMinerService` | `GameSession.Commands.cs`: `Hire`, `BeginDrag` / `DropCat`, `Merge`, `Assign`; лимит — `GameSession.UnitCap` |
| `MiningSimulation` | `GameSession.Time.cs`: `Advance`, `ProcessDueEvents`, `ApplyDamage`, `CompleteTarget` |
| `GeneratorService` | `GameSession.Commands.cs`: `ActivateShaft`, `CollectPayout`, `UpgradeGenerator`, `UpgradeToThreshold`, `ApplyThresholds`, `CheckAutomation`; `GameSession.cs`: `DefFor`, `GeneratorOutput`, `GeneratorCycleTicks`, `UpgradeCost`; циклы — `GameSession.Time.cs` |
| `RewardService` | `GameSession.Rewards.cs` (награды камней, сундуки, очередь показа) |
| `GachaService` | `Core/Sim/GachaService.cs` |
| `CardService` | `GameSession.Commands.cs`: `TryGetCardUpgradeCost`, `CanUpgradeCard`, `UpgradeCard`; модификаторы — `GameSession.cs` (`CardEffectValue`, `ManagerMultiplier`, `GlobalIncomeMultiplier`) |
| `DeliveryService` | `GameSession.Time.cs`: `TryCreateDelivery`, `DeliveryPool`, `NextDeliveryIntervalSeconds` |
| `FreeChestService` | `GameSession.Commands.cs`: `ClaimFreeChest`; таймер — `GameSession.Time.cs`; бесплатный котик — `GameSession.Time.cs` (`TrySpawnPending`), `OpenContainer` |
| `ProgressionService` | `GameSession.Commands.cs`: `OpenGate`, `CanCompleteZone`, `CompleteZone`; `GameSession.cs`: `EnterZone` |
| `SaveService` | `Core/Save/SaveService.cs` |
| `OfflineProgressService` | `Core/Sim/OfflineProgress.cs` |
| `InputController` / `CameraController` | `Runtime/PointerInput.cs` / `Runtime/CameraRig.cs` |

## Изменения пакетов

Добавлены `com.unity.nuget.newtonsoft-json` 3.2.1 и `"testables": ["com.unity.inputsystem"]`. Шаблонные пакеты не удалялись.

## Ручной чек-лист владельца

- [ ] Касания на устройстве: перенос, объединение, прокрутка, динамит, тап по бочке и добыче.
- [ ] Обучение zone1 в новом слоте Main: наём → бочка → перенос на камень → сбор добычи → второй наём (наставник, лапка, жест переноса; затемнение не мешает нажатиям).
- [ ] Safe area на 9:16, 9:19.5, 9:20 (Device Simulator).
- [ ] Сворачивание во время добычи, открытия сундука, переноса и перехода зоны.
- [ ] Визуал, анимации, звук.
- [ ] Ручное прохождение zone1–zone3 в слоте Main с записью действий и времени каждой зоны (для сравнения с ориентирами C.4: zone1 — 2–6 мин, zone1–3 — до 20–30 мин).
- [ ] Валидатор в Development APK на устройстве (DEV → Валидатор, см. «Валидатор на устройстве»), если `adb` видит устройство.
