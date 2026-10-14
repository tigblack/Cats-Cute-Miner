# Runtime режима CatOutpostV2 — спецификация реализации

> Дополнение 11 октября 2026: широкие ветвящиеся карты 25–53 клетки, физические ворота, маски навигации/затемнения, свободная камера X/Z и безопасная миграция активной шахты заданы в [MINE_TOPOLOGY.md](MINE_TOPOLOGY.md). Описания барьера по строке ниже применяются к старым шахтам до миграции.

Контракт для сцены, представления, UI, обучения, DevTools и Editor-инструментов нового режима. Источник правил —
ТЗ 2.1 (`Docs/TZ/Cat_Miners_Auto_Incremental_TZ_v2.1.md`), ядро — `Docs/Rework/CORE_SPEC.md` (код `Assets/Game/Scripts/Core/Outpost/`).
Карты переиспользования старого кода: `Docs/Rework/map/{controller,visual,ui,save,art-audio,tests-tools,old-core}.md`.
Новые спрайты и их раскладка: `Docs/Rework/ART_NAMES.md`. Стиль: `Docs/Art/PIXEL_STYLE_GUIDE.md` (UI только из `PixelUi`).

## 1. Принципы

- Новый режим — по умолчанию. `GameBootstrap` создаёт `OutpostController`; старый `GameController` остаётся только как
  изолированный режим разработки (`GameBootstrap.LegacyMode = true`, по умолчанию `false`; старые PlayMode-тесты включают его в SetUp).
  Старый `GameSession` в обычном режиме не создаётся вообще — его таймеры, автосбор, доставки и генераторы не работают.
- Код нового режима — `Assets/Game/Scripts/Runtime/Outpost/` (пространство имён `CatMiners.Runtime.Outpost`), в той же сборке
  `CatMiners.Runtime` (новые asmdef не заводить — dotnet-шаблоны компилируют по папкам). Старые классы не ломать: общие
  (`PixelUi`, `UiKit`, `PixelArt`, `PixelWorld`, `Effects`, `FloatingText`, `Label3D`, `HpBar`, `CameraRig`, `Sfx`, `MusicPlayer`,
  `Ambience`, `UiSound`, `TutorialHint`, `SafeArea`, `SystemClock`, `VisualSettings`) переиспользуются; если нужен новый метод —
  только добавлять, не меняя поведение для старого режима. Связанные со старой игрой классы (`BoardView`, `ObjectView`, `CatView`,
  `LootView`, `Hud*`, `TutorialController`, `PointerInput`) копируются в новые классы и адаптируются.
- Представление не меняет экономику: всё состояние — в `OutpostSimulation`; виды читают его и события `OutpostEvent`.
  Начисление золота — только фаза 4 ядра; полёт монет к кошельку запускается событием `CargoDeposited`.
- Каждый MonoBehaviour — в файле с тем же именем. Кириллица в Runtime-коде — только в словарях `Loc` (тесты это проверяют).
- Нет аллокаций в обычном кадре (TZ §19): кэш строк по значению (паттерн `ValueKey`), пулы видов, без LINQ в `Update`.

## 2. Файлы

| Файл (`Runtime/Outpost/…`) | Класс | Ответственность |
|---|---|---|
| `OutpostController.cs` (+ partial `.Loop.cs`, `.Save.cs`, `.Commands.cs`, `.Present.cs`) | `OutpostController : MonoBehaviour` | Сборка сцены, загрузка конфигураций, сохранение, цикл времени, офлайн, переходы, команды, события → вид/звук/UI |
| `OutpostResources.cs` | `OutpostResources` | `Resources.Load<TextAsset>("Outpost/…")` → `OutpostConfigSet.Parse`; `GameBootstrap.BalanceJson` → `BalanceRepository.Load` (SHA-256 проверяется и в Android) |
| `OutpostInput.cs` | `OutpostInput : MonoBehaviour` | Тап по доступной цели — удар кирки игрока, иначе сведения; удержание без сдвига 0,45 с — сведения. Перетаскивание мышью/свайп перемещают карту по X/Z без удара при отпускании; инерция, колесо, отмена жеста. Back принадлежит HUD |
| `View/PlayerStrikeFx.cs`, `View/OutpostBoardView.Strike.cs` | `PlayerStrikeFx`, часть `OutpostBoardView` | Кирка игрока из воздуха (`fx_strike_pick_0…3`, пул 6): кадр удара вызывает `StrikeHandler` (команда ядра), при успехе — дрожание цели, сколы, `fx_impact`, подсказка `PlayerStrike` |
| `View/OutpostBoardView.cs` | `OutpostBoardView : MonoBehaviour` | Пол, стены, декор, тема, площадка лагеря и полоса построек, координаты, пулы видов, `Sync`, `OnEvent` |
| `View/OutpostTargetView.cs` | `OutpostTargetView : MonoBehaviour` | Цель `r/s/x/p`: спрайт по `VisualId`, стадии урона по доле HP, HP-бар при уроне, разрушение/расчистка, ворота-барьер |
| `View/OutpostCatView.cs` | `OutpostCatView : MonoBehaviour` | Котик: слои body/boots/pack/load/hat/pick, кадры (idle, walk, carry, mine, pickup, unload, spawn, joy), движение по пути ядра |
| `View/OutpostLook.cs` | `OutpostLook` (static) | Таблицы имён спрайтов: окрас по `WorkerState.Look`, предмет → вид слоя, кадры (индексы из `ART_NAMES.md`) |
| `View/CargoPileView.cs` | `CargoPileView : MonoBehaviour` | Пул визуальных мешочков партии (1/2/куча) по числу порций в клетке; не кликабелен |
| `View/BaseCampView.cs` | `BaseCampView : MonoBehaviour` | Склад с двумя местами, ящики с монетами, доска найма, верстак, стеллаж/тележка, шкаф; уровни построек по локальным веткам |
| `View/GateBarrierView.cs` | `GateBarrierView : MonoBehaviour` | Барьер ворот по всей ширине; трещины; открытие |
| `View/OutpostDebugOverlay.cs` | `OutpostDebugOverlay : MonoBehaviour` | (Dev) пути, резервы рабочих клеток/груза/мест склада, барьер |
| `UI/OutpostHud.cs` (+ partial `OutpostHud.*.cs` по окну) | `OutpostHud : MonoBehaviour` | Холст, верх/низ, статус доставки, тосты, полёт монет, все окна, стек окон и Back |
| `UI/OutpostHud.BasePanel.cs` | — | Панель «База»: 6 веток, шкала уровня до максимума каждой ветки, ×1/×10/Макс, эффект сейчас→после, цена, подсказка узкого места |
| `UI/OutpostHud.Equipment.cs` | — | «Экипировка»: 4 слота, коллекция с фильтром по слоту, сравнение, улучшение за детали, «Надеть лучшее» |
| `UI/OutpostHud.Info.cs` | — | Сведения о котике/цели (без кнопок назначения) |
| `UI/OutpostHud.Results.cs` | — | «Итоги шахты», «Пока вас не было», конец кампании, ошибки сохранения |
| `UI/OutpostHud.Settings.cs` | — | Настройки (музыка, SFX, атмосфера, вибрация, уменьшение эффектов) — копия раскладки старого окна |
| `OutpostTutorial.cs` | `OutpostTutorial` | 10 шагов ТЗ §15 по фактическим событиям, через `TutorialHint` |
| `OutpostAudio.cs` | `OutpostAudio` (static) | Новые сигналы через `Sfx` (шаг, подбор, сумка, склад, разгрузка, найм, экипировка) с ограничением голосов/частоты |

Локализация: `Core/Text/Loc.Outpost.cs` (ключи `outpost.*`), английский словарь (с 12 октября 2026; русский — в `Backups/before-english-ui-2026-10-12`). DevTools: `DevTools/OutpostDevPanel.cs`.
Editor: `Editor/OutpostSetup.cs` (+ правки `ProjectSetup`), `Editor/OutpostValidatorCli.cs`. Тесты: `Tests/PlayMode/Outpost/*.cs`.

## 3. Контроллер

### 3.1 Init (порядок важен — как `GameController.Init`)

`Clock = ClockOverride ?? new SystemClock()` → `Materials/PixelArt.Init` → `EnsureSceneBasics` (свет, EventSystem; у fallback-камеры
добавить `AudioListener`) → `CameraRig.Create` → `Effects.Create` → `Sfx/MusicPlayer/Ambience.Create` → `OutpostBoardView` →
`OutpostHud.Create(this)` → `OutpostInput` → `OutpostTutorial` → загрузка исходника и конфигураций (ошибка — фатальное окно,
без пустой карты и мгновенной победы) → `OutpostSaveService(persistent/CatOutpostV2, profile = Main|Debug)` → загрузка.

Загрузка: `Loaded` → карточки → подготовить совместимую широкую топологию на копии → архив main/bak → commit → построить новый вид → офлайн (§3.4). Сбой миграционного commit оставляет прежнюю симуляцию в памяти и повторяет тот же снимок; несовместимая карта или ошибка архива сохраняют старую геометрию. `NotFound` → однократный архив старых `save_main.json`/`save_debug.json`
(копии в `CatOutpostV2/legacy-archive/`, оригиналы не трогать), однократный импорт совместимых настроек звука/вибрации/эффектов из
старого профиля (без запуска старой сессии), новый профиль (`Seed` из UTC и `Environment.TickCount`, один раз), однократный перенос
карточек и эликсира прежнего режима (`ImportLegacyCards`: новейший из `save_main.json`/`save_debug.json` и архива по `savedAtUtcMs`,
`CardService.ImportLegacy`, ключ `CatOutpostV2:cards:legacy-import`), `CreateRun(zone1)`, commit.

Карточки: `Configs = OutpostResources.LoadConfigs().WithCards(Balance)` (каталог 61 карточки из исходника + `card_rules.json`).
Загруженный профиль проходит `PrepareCards`: если `CardsVersion < 1` — копия `save_main.json`/`.bak` в `<профиль>/pre-cards/`
(один раз), `CardService.MigrateProfile` (HighestZone из текущей и завершённых шахт, генератор сундуков, учебная выдача помечена)
и перенос прежних карточек; профиль сразу записывается. Прогресс кампании не сбрасывается.
`Corrupt/Incompatible` → окно ошибки с объяснением; файлы уже в карантине; запись заблокирована (`m_SaveBlocked`) до явного
«Начать новую кампанию» (debug-профиль) — без скрытого reset.

### 3.2 Кадр

```
mono = Clock.MonotonicSeconds (в мкс, long, точная разность от базы сессии)
if (пауза/кадр > 1 с/разрыв UTC > 1 с) → завершить онлайн-интервал, офлайн (§3.4)
else targetTick = baseTick + (monoUs - baseMonoUs) * speed  (speed > 1 только в debug-профиле)
     sim.Advance(targetTick)   // целые события; Now == targetTick
     run.LastProcessedUtcMs = max(prev, utcBase + (Now - baseTick)/1000 при speed 1)
DrainEvents → Present (вид, звук, UI, обучение) с бюджетом на кадр
Board.Sync(sim, Time.deltaTime); Hud.Refresh (≤10 Гц); Tutorial.Update
autosave: каждые 2 с активной игры; немедленно после команд/наград/перехода/офлайна; при паузе/потере фокуса
```

Сохранение вызывается только между `Advance` (граница — конец всех 7 фаз). Ошибка записи — повторить запись того же снимка
позже (не повторять транзакцию), UI подтверждает команду только после успешного commit (для покупок достаточно применить и
сразу записать; окно награды/перехода — после записи).

### 3.3 Команды (используются UI и DevTools)

```csharp
public OutpostSimulation Sim; public OutpostGameState State; public OutpostMap Map; public OutpostConfigSet Configs;
public TeamStats Stats => Sim.Stats;
public UpgradeQuote Quote(UpgradeBranch b, BuyMode m);   public CommandResult Buy(UpgradeBranch b, BuyMode m);
public CommandResult Equip(string itemId); public CommandResult Unequip(EquipSlot s); public CommandResult EquipBest();
public CommandResult UpgradeItem(string itemId);
public CommandResult UpgradeCard(string cardId);   // одна транзакция (копии + эликсир → уровень) и commit; отказы not-owned/max/copies/elixir/unknown
public void FocusBase(); public void SelectWorker(int id); public void SelectTarget(int id); public void ClearSelection();
public bool CanStrike(int targetId); public bool BeginStrike(int targetId, Vector3 world);   // тап по цели: анимация, урон — в кадр удара
// StrikeNow (Board.StrikeHandler): AdvanceOnline → Sim.Submit(Strike) → события; без немедленного Commit (автосохранение 2 с)
public OutpostSelection Selection { get; }   // { Kind None|Worker|Target|Base; int Id }
public bool CanContinue { get; }   public void ContinueToNextMine();   public bool InTransition { get; }
public void SetVolume(AudioGroup g, float v); public void SetVibration(bool on); public void SetReduceEffects(bool on);
public OfflineSummary PendingOffline;   public event Action<OutpostEvent> EventPresented;
// магазин (ТЗ 10.5): UtcNowMs — часы таймеров; StoreBuy — команда ядра StoreBuy(itemId, UtcNowMs) и commit;
// UseTimeSkip — OfflineSimulator.BeginSkip на копии + PayTimeSkip, фаза OfflineCatchUp, итог — PendingSkip (DismissSkip);
// ClaimOfflineDouble — BonusGold(GoldDelivered) один раз (тестовый режим), затем DismissOffline.
public long UtcNowMs; public CommandResult StoreBuy(string itemId); public CommandResult UseTimeSkip(string itemId);
public int SkipMinutesInProgress; public OfflineSummary PendingSkip; public void DismissSkip(); public CommandResult ClaimOfflineDouble();
// debug (только debug-профиль): SimSpeed, SwitchProfile(bool debug), LoadZone(int z), GrantTestKit(), GrantTestCards(), GrantTestGems(),
// CompleteStageNow() — запрещено в Main. LoadZone поднимает HighestZone только debug-профиля (Main не меняется).
```

### 3.4 Офлайн и жизненный цикл

`OnApplicationPause(true)`/`OnApplicationFocus(false)`: один флаг `m_Suspended` (повторные callbacks не дублируются),
`Advance` до текущего момента, маркер UTC, commit, `AudioListener.pause = true`, отмена жестов. Возврат: `OfflineSimulator.Begin`
на копии, порционно `Step` по стендовому бюджету (≈8 мс/кадр) с индикатором; во время расчёта покупки/переход недоступны; результат —
одна транзакция (commit), затем окно «Пока вас не было»: только полученное золото крупной суммой, включая 0.
Слева — «Double income» с телевизором и «Watch ad» (тестовый режим рекламы, ТЗ 13: `ClaimOfflineDouble` начисляет доход ещё раз
командой `BonusGold`, окно закрывается; при `TestPurchases = false` — тост «Ads are coming soon», окно остаётся),
справа — «Отлично»; Back также закрывает сводку. Остальные результаты остаются в состоянии и `OfflineSummary`, но в этом окне не показаны.
Общий предел — три реальных часа за одно отсутствие,
включая старые сохранения; после этого работники отдыхают. UTC-маркер поглощает всё отсутствие, чтобы остаток не доначислялся.
«Лежанка» (`ca051`, `cards-2`) даёт +5% к скорости разгрузки за уровень и не продлевает офлайн.
Обычные окна симуляцию не останавливают.

Тайм-скип магазина (`UseTimeSkip`, ТЗ 10.5) использует тот же слот `m_Offline` и ту же фазу `OfflineCatchUp`: `AdvanceOnline`,
отказ в `ReadyToContinue` (`phase`), `BeginSkip(State, def, now)` (`def.Minutes` — 10/30/60), оплата на копии, порционный расчёт с окном «Time Skip»; по
окончании commit одной транзакцией и `PendingSkip` (окно итога вместо «Пока вас не было»). Магазинная миграция профиля
(`StoreService.MigrateProfile`) — при загрузке, новой игре и сбросе прогресса.

### 3.5 Завершение и переход

`DrainingStarted` → статус «Доставляем оставшиеся монеты». `StageCompleted` (награда уже в профиле и записана) → котики идут в лагерь
(вид), окно «Итоги шахты»: расчищено N/N, предмет (новый/дубликат → детали), детали за завершение, объяснение сброса («местные
улучшения сбросятся, экипировка останется»), кнопки «Экипировка» и «Следующая шахта». `ContinueToNextMine`: `transitionId`,
блок повторного тапа, `StageProgressionService.PrepareTransition` → commit → только потом затемнение, перестройка вида, музыка зоны,
`Rig.SetBoard`, фокус на базе, проявление. В основном HUD после закрытия итогов — широкая крупная кнопка «Перейти на следующую
шахту», напрямую вызывающая тот же переход. Ошибка записи — остаёмся, кнопка «Повторить». После `zone144` — окно завершения кампании
(экипировка и настройки доступны, кнопки следующей шахты нет).

## 4. Представление

### 4.1 Координаты

Как `BoardView`: 1 клетка = 1 юнит = 32 px, плоскость XZ, +Z вверх экрана; `CellCenter(cell) = (col + 0.5, 0, Depth - row - 0.5)`.
Строки лагеря `Depth..Depth+2` → `z = -0.5 … -2.5`; полоса построек ниже (`z < -3`). Камера: `Rig.SetBoard(width, Depth + 3 + StripRows)`,
«К базе» = `Rig.FocusOnSmooth(z лагеря)`.

### 4.2 Котик

Позиция: если `Action.Kind == Edge` — интерполяция между `CellCenter(Cell)` и `CellCenter(Path[PathIndex])` по
`p = P0 + (Now - SegmentStart)/Duration` (float только для отображения); иначе центр `Cell`. Небольшое стабильное смещение по
`Id` внутри клетки (котики в одной клетке расходятся визуально; логика не меняется). Очередь склада отображается в нижней строке
лагеря без блокировки. Кадры по `Mode`: `MovingToWork/MovingToCargo/GoingHome/Returning` — walk (carry при `Bag.Count > 0`);
`Mining` — mine с замахом по времени до `Hit.Deadline` и `Strike` на `TargetHit`; `PickingUp` — pickup; `WaitingDepot` — idle с грузом;
`Unloading` — unload; `Idle` — idle/stretch. Слой `load_{1|2|3}` по заполнению (`<50%`, `<100%`, `>=100%`). Слои экипировки — по
надетому комплекту команды (`OutpostLook.ForItem`), обновляются на `EquipmentChanged`; локальные уровни внешность не меняют.
Тап по котику — сведения (текущая задача, сумка `n/cap`, стоимость груза). Тап по цели, которую можно ударить (`CanStrike`), —
удар кирки игрока (ТЗ 6.6): `BeginStrike` → `Board.PlayStrike` (кончик кирки в точке касания, у правого края — зеркально; не чаще
0,4 с реального времени по монотонным часам). Интервал один для всех целей, мыши и пальцев; повторные касания игнорируются без
очереди, продления паузы и открытия сведений. Новые замахи не накладываются на активный. Через 0,10 с кадр удара выполняет команду,
отскок и исчезновение с искоркой к 0,26 с. Удержание без сдвига — сведения
о любом объекте; в сведениях доступной цели строка «Нажмите на цель — ударите своей киркой».

### 4.3 Цели, груз, лагерь

- Цель: спрайт по `VisualId` (`MimicId` или `TypeId`) через существующий `PresentationCatalog`/таблицы `ObjectView`; стадии урона
  `_d1.._d3` или оверлеи трещин (`ART_NAMES.md`) по доле `RemainingHpUnits/MaxHpUnits`; HP-бар, пока цель повреждена; разрушение —
  `Effects` (обломки, пыль), футпринт исчезает. Завалы `waterfall*`/`wall*` — анимация расчистки. Ворота — барьер по всей ширине,
  трещины по HP, открытие (`SectorOpened`).
- Партии: в каждой клетке один `CargoPileView` с суммарным числом порций на земле (1 → мешочек, 2–4 → два, ≥5 → куча); пул видов.
  `CargoReleased` → «вылет» мешочка из цели; `CargoPickedUp` → мешочек «прыгает» в сумку.
- Лагерь: `BaseCampView` по раскладке `ART_NAMES.md`. Места склада подсвечиваются при обслуживании; круговой/полосовой индикатор
  обслуживания текущей порции (доля из `Unload`-действия) и её фиксированная стоимость. Ящики наполняются по суммарно доставленному.
  Постройки получают уровни: доска найма — `Levels[Team]`, верстак — `Levels[Pickaxes]`, стеллаж/тележка — `Levels[Bags]`/`Levels[Boots]`/`Levels[Unload]`
  (3 ступени: 0, ≥⅓ cap, ≥⅔ cap); шкаф открыт, когда в коллекции есть ненадетые предметы. Тап по постройке открывает панель «База».

### 4.4 События → эффекты (с бюджетом на кадр)

| Событие | Вид | Звук/прочее |
|---|---|---|
| `TargetHit` | `Strike`, сколы | удар по материалу (ограничение частоты) |
| `CargoReleased` | мешочек вылетает (в сумку или на землю) | мягкий «плюх» (редко) |
| `CargoPickedUp` | мешочек к котику | подбор |
| `CargoDeposited` | порция в ящик, «+N» над местом, монеты летят к кошельку (`FlyLoot`) | сдача (голоса ≤2, интервал) |
| `TargetDestroyed` | разрушение/расчистка | разлом |
| `SectorOpened` | ворота открываются, камера кратко к воротам (если игрок у базы — нет) | ворота |
| `DrainingStarted`/`StageCompleted` | статус/сбор команды | фанфара |
| `TeamUpgradePurchased` / `WorkerHired` | вспышка постройки / котик выходит из лагеря | улучшение / найм |
| `EquipmentChanged` / `ItemUpgraded` | смена слоёв у всех котиков | экипировка |
| `TargetStruck` (удар игрока) | отклик показывает кадр удара `PlayerStrikeFx` (вид события не дублирует) | `PlayerStrike`: удар по материалу громче и с интервалом 0,06 с |

## 5. UI

- Холст, масштаб, SafeArea, порядок детей и вставки — как `Hud` (`ui.md` §1, §4); всё из `PixelUi`.
- Верх: плашка «Шахта N» (+ «Сектор k/m»), сегменты: **Золото** (доставленный кошелёк, иконка монеты), **Груз** (`icon_sack`: несданные
  порции и их стоимость — на земле + в сумках, другое оформление и подпись), **Прогресс** `разрушено/всего` (включая ворота, без выхода и базы),
  **Котики** (число работников); шестерёнка настроек.
- Низ (с 12 октября 2026): слева «Store» (красная точка: рекламный сундук готов) и «Карты» (бейдж: есть что улучшить), в центре «База»
  (широкая золотая кнопка с одной подписью «Base» — «For this mine» убрана 13 октября, бейдж числа доступных покупок), справа «Экипировка» (красный пин без числа: есть доступное улучшение
  предмета) и «К базе».
- «Store» (`OutpostHud.Store.cs`, `StoreTileView.cs`, ТЗ 10.5): экран во весь safe area, шапка, счётчики эликсира и гемов с «+»
  (прокрутка к разделу), разделы `store_catalog.json` баннером и сеткой по три. Плитка: зелёная (бесплатно — реклама готова или
  свой тайм-скип), синяя (гемы или $), тёмная (таймер рекламы); бейдж бонуса (если задан в каталоге; в `store-3` нет), плашка «Available: N», «i» у сундуков (окно
  шансов). Трата гемов и денег — после модального подтверждения; без гемов — причина и прокрутка к Gems. Окна: награда сундука
  (`ChestCardTile` — карточки картинками; по `ChestOpened` с `StoreSource` и по сундуку шахты с новой карточкой), шансы, итог
  тайм-скипа (`PendingSkip`); тосты покупки и удвоения (`StorePurchased`, `BonusGold`). Итоги шахты ждут закрытия окна сундука.
- «Карты» (`OutpostHud.Cards.cs`, `CardView.cs`, UI_API.md): вся коллекция 61 карточки, состояния по `CardService.StateOf`,
  окно сведений: крупная карточка в верхней половине экрана (имя, уровень, текущий короткий бонус, одна иконка копий X/Y),
  под ней сравнение величин бонуса через зелёную стрелку вправо, увеличенные цена в эликсире и «Улучшить» (`UpgradeCard`).
  Повторные абзацы и уже выполненные условия открытия скрыты; условие шахты — только у закрытой карточки. На максимуме
  нет сравнения и покупки. При нажатии недоступного улучшения причина показывается тостом.
  В фазе доставки — строка «Доставляем оставшиеся монеты»; в `ReadyToContinue` — «Перейти на следующую шахту»:
  кнопка на всю ширину полосы Safe Area, высота 168 единиц Canvas, шрифт 56 с переносом без уменьшения.
  Полоса высотой 176 входит в нижний отступ камеры и обучения. Недоступна при переходе и блокировке команд.
- Панель «База» (окно-лист снизу): вкладки «На эту шахту» (6 строк: иконка, название, уровень `L/cap`, эффект «сейчас → после»,
  цена, кнопка покупки; переключатель ×1/×10/Макс сверху; у каждой ветки золотая шкала купленного уровня до фактического cap
  из `Quote`, включая бонусы карточек; пороги не отображаются. На cap — полная зелёная шкала и «Макс.» без цены;
  у «Команды» только ×1) и подсказка ограничения потока по реальным состояниям (доля времени в добыче/пути/
  очереди за последние N секунд). Явная подпись «На эту шахту» против «Сохраняется между шахтами» (экипировка).
- «Экипировка»: 4 слота с текущим предметом и бонусом; фильтр по слоту; коллекция (карточки `PixelCard`: рамка редкости, иконка,
  уровень, красный пин доступного улучшения справа сверху, отметка «надето» снизу, без шкалы деталей; «нет» — рубашкой);
  выбранный предмет: имя, «редкость · уровень», бонус «сейчас ➜ после улучшения» с зелёной стрелкой (на максимуме — без
  стрелки), «Equipped» или «сейчас → с этим предметом», детали, «Надеть», «Улучшить (N деталей)» / «Макс.»;
  «Надеть лучшее»; счётчик деталей. Доступно и в `ReadyToContinue`.
- Сведения о котике/цели — компактная панель над нижней полосой (как контекстная панель старого HUD), без кнопок назначения;
  у цели: HP, тип (имя из словаря, не архивный ID), текущие работники, оставшаяся добыча (`Total - Released` порций), причина
  недоступности («закрытый сектор», «нет пути», «ждёт расчистки сектора»).
- Причины недоступности кнопок: «Не хватает доставленного золота», «Максимальный уровень», «Шахта завершена», «Предмет максимального уровня»,
  «Не хватает деталей».
- Стек окон: Back закрывает верхнее окно (деталь → окно → панель), затем снимает выбор; открытие любого окна вызывает
  `Input.CancelGesture()`. Модальное окно блокирует касания поля, симуляция продолжается.

## 6. Обучение (ТЗ §15)

`OutpostTutorial` хранит шаг в `Profile.Tutorial`. Шаги по событиям: (1) автодобыча; (2) ручной удар — лапка и рамка на видимом
доступном камне, «Вы тоже можете разбивать камни! Нажмите на камень — ударьте своей киркой»; (3) сумка и доставка;
(4) `CargoDeposited` — подсветка золота; (5) «База» → «Кирки», после покупки — рост урона команды; (6) найм;
(7) самостоятельный выбор целей; (8) награда и экипировка; (9) сброс перед переходом; (10) сохранённый бонус на zone2.
Урок ручного удара завершается только по `TargetStruck` с положительным уроном на zone1, включая ранний удар. Его подсказка
показывается 8 видимых секунд, затем остальные уроки продолжаются; после перезапуска непройденный урок предлагается снова.
Если камень разрушен или ушёл с экрана, указатель выбирает другую видимую цель. Если на старте виден только лагерь, камера
один раз плавно подводится к ближайшему доступному камню. На zone2+ уроки первой шахты, включая новый,
засчитываются молча даже у профилей со старым пройденным обучением. Можно закрыть обучение; добыча не останавливается.

## 7. Звук

Новые сигналы через `Sfx` (вариации существующих клипов допустимы): шаг (очень тихо, ≤1 голос, ≥0.25 с), подбор, сумка заполнена,
сдача на складе (≤2 голоса, ≥0.12 с, лёгкий питч по стоимости), разгрузка завершена, найм, экипировка/улучшение предмета, открытие
сектора, итоги, удар кирки игрока (`Sfx.PlayerStrike(material)` — вариант клипов удара, громче, ≥0,06 с). Музыка: «Тёплая штольня»/«Кристальная глубина» по теме зоны через `MusicPlayer` без перезапуска от окон. Группы
Master/Music/SFX/Ambience, пауза приложения приостанавливает звук.

## 8. DevTools и Editor

- `OutpostDevPanel` (Editor/Development Build): выбор зоны, отдельный debug-профиль, скорость ×1/×5/×20/×100, выдача тестового
  комплекта, `OutpostDebugOverlay` (пути, резервы), счётчики груза (создано/на земле/в сумках/сдано), вклад локальных и постоянных бонусов
  в `TeamStats`; кнопка «Cards» — `GrantTestCards()` (открытым по прогрессу карточкам — копии на три улучшения, +100 000 эликсира).
  Эти команды — только в debug-профиле. В настройках во всех сборках, включая WebGL Release, есть Reset Progress;
  в меню «DEV 4» также есть кнопка «Сбросить прогресс» (`ResetProgress`, доступность — `CanResetProgress`), которая
  работает для выбранного Main или Debug после подтверждения; отмена и Back не меняют состояние. Сбрасываются вся кампания,
  награды, золото, улучшения, карты/копии/эликсир, экипировка/детали и обучение; начинается шахта 1 с двумя котиками и скоростью ×1.
  Настройки звука/вибрации/эффектов и суммарный `Profile.PlayTimeMs` сохраняются. Другой профиль остаётся целым. Кандидаты старого сохранения копируются в `restart-*`,
  новые main/bak содержат один начальный снимок; архив и legacy-карточки не восстанавливают прогресс автоматически.
  При ошибке записи новая игра не показывается, `RetrySave` повторяет тот же снимок, включая новую bak.
  Счётчик активного времени основан на монотонных часах контроллера, учитывает меню и переходы, останавливается в фоне;
  скорость симуляции, офлайн-интервал и длительность тайм-скипа не применяются к нему. Подготовленные копии run при commit
  получают актуальный счётчик без изменения неизменяемого снимка повторной записи. У старого профиля `PlayTimeMs = 0`.
- `ProjectSetup`: сцена `CoreGame` по-прежнему с `GameBootstrap` (новый режим по умолчанию), Resources-конфиги включены в сборку.
- `OutpostValidatorCli.Run` (`-executeMethod`): импорт и статическая доступность 144 зон, прогон ядра A на всех зонах, стратегии A/B/C на
  1–10 и 50/93/101/144, кампания — отчёты в `Docs/Reports/outpost/*.json|md`. `Tools/unity.ps1 -Mode Validate` вызывает его.
