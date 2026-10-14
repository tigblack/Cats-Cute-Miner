# Вид поля режима CatOutpostV2 — API для контроллера, HUD, обучения и DevTools

> Дополнение 10 октября 2026: для новых шахт правила геометрии, ворот, масок навигации/затемнения и сохранений задаёт [MINE_TOPOLOGY.md](MINE_TOPOLOGY.md). Описания барьера по строке и исходного прямоугольного поля ниже применяются к старым сохранённым шахтам без `MapTopology`.

Код: `Assets/Game/Scripts/Runtime/Outpost/View/` (пространство имён `CatMiners.Runtime.Outpost`, сборка `CatMiners.Runtime`).
Контракты: `Docs/Rework/RUNTIME_SPEC.md` §2, §4; ТЗ 2.1 §5, §7, §8, §16. Имена спрайтов: `Docs/Rework/ART_NAMES.md`.
Вид **только читает** `OutpostSimulation` и события `OutpostEvent`: экономику не меняет, анимация ничего не начисляет.
`IOutpostGame` не расширялся.

## 1. Файлы

| Файл | Тип | Что делает |
|---|---|---|
| `OutpostBoardView.cs` (+ `.Floor.cs`, `.Events.cs`) | `MonoBehaviour` | Сборка поля, координаты, пулы, `Sync`, `OnEvent`, выбор, подсказки звука |
| `OutpostTargetView.cs` | `MonoBehaviour` | Цель `r/s/x/p`: спрайт по `VisualId`, стадии урона, HP-бар, разрушение/расчистка, створки ворот |
| `GateBarrierView.cs` | `MonoBehaviour` | Пролёты барьера ворот в боковых клетках строки ворот, трещины, сломанный барьер после открытия |
| `OutpostCatView.cs` | `MonoBehaviour` + `struct OutpostCatPose` | Котик: 9 слоёв, кадры, движение по пути ядра, догонялка при разрывах |
| `OutpostLook.cs` | `static` | Таблицы имён спрайтов котика (окрас, экипировка, груз, мешочек), ступени построек |
| `CargoPileView.cs` | `MonoBehaviour` | Груз на земле: один вид на клетку по числу порций, некликабелен |
| `CargoFlights.cs` | класс (не MB) | Пул летящих мешочков (цель → земля/сумка, земля → сумка, сумка → воронка склада) |
| `PlayerStrikeFx.cs` | класс (не MB) | Пул ударов кирки игрока (`fx_strike_pick_0…3`, 6 мест): замах 0–0,05, взмах до 0,10 (кадр удара), удар до 0,18, отскок до 0,26 с |
| `BaseCampView.cs` | `MonoBehaviour` | Лагерь: места склада, индикатор обслуживания, ящики, постройки по ступеням, шкаф, фонари |
| `OutpostDebugOverlay.cs` | `MonoBehaviour` | Отладочный слой: пути, резервы, места склада, очередь, барьер |
| `OutpostPickable.cs` | `MonoBehaviour` + `enum OutpostPickKind` | Метка коллайдера для тапа и выбор лучом без выделений |
| `OutpostViewCue.cs` | `enum OutpostViewCue` + `struct OutpostViewCueArgs` | Точки звука/отклика вида |

## 2. Жизненный цикл (что вызывает контроллер)

```csharp
// Init (после Effects.Create, до HUD): поле в начале координат, под контроллером.
Board = new GameObject("Board").AddComponent<OutpostBoardView>();
Board.transform.SetParent(transform, false);
Board.Fx = Fx;                                  // Effects
Board.Cue += OnBoardCue;                        // звук (§6)
Board.StrikeHandler = StrikeNow;                // кадр удара кирки игрока → команда Strike ядра, true — урон нанесён

// Построение / перестройка (загрузка, переход, смена профиля, LoadZone):
Board.Build(Map, Sim, balanceRepository);       // balance — BalanceRepository исходника (оформление камней), можно null
Cam.backgroundColor = PixelTheme.Dark;          // тема уже выставлена Build (PixelTheme.Current)
Rig.SetBoard(Board.Width, Board.CameraDepth, Board.BaseFocusZ);

// Кадр (после Sim.Advance и DrainEvents):
foreach (var e in events) Board.OnEvent(Sim, e, Fx);   // СНАЧАЛА все события кадра (перегрузка с Sim пропускает чужую карту)
Board.Sync(Sim, Time.deltaTime);                  // ЗАТЕМ один Sync

// После офлайн-расчёта, загрузки, CompleteStageNow и любых изменений без событий (RecordEvents = false):
Board.Refresh();                                  // следующий Sync — без анимаций и догонялок

// Выбор (при каждой смене Selection):
Board.SetSelection(Selection);
```

- `Build(OutpostMap map, OutpostSimulation sim, BalanceRepository balance = null)` — строит пол/стены/декор/затемнение,
  цели, барьеры, лагерь; котиков и груз — из состояния без анимаций; сразу делает первый `Sync`. Пересборка возвращает
  котиков и мешочки в пулы (пулы живут между шахтами).
- `Sync(OutpostSimulation sim, float dt)` — каждый кадр; без выделений памяти. Если `sim.Map` не та, для которой построено поле
  (`Matches(sim) == false`, например новая шахта до `Build`), ничего не делает — поле «заморожено» на старой карте.
- `OnEvent(OutpostSimulation sim, OutpostEvent e, Effects fx)` — каждое событие кадра до `Sync`; событие симуляции, для карты
  которой поле не построено, пропускается. `OnEvent(OutpostEvent e, Effects fx)` — то же без проверки. `fx == null` — берётся `Fx`.
- `Refresh()` — сбросить «живость»: котики встают в точки ядра без догонялки, груз — без прыжков, полёты гаснут.
- `SetSelection(OutpostSelection s)` — кольцо под котиком / уголки цели / уголки двора базы.
- `bool Matches(OutpostSimulation sim)`; `OutpostMap Map`; `int Width`, `int Depth`; `Effects Fx { get; set; }`.
- `bool Gathering` — после `StageCompleted` (и при загрузке в `ReadyToContinue`) котики собираются в лагере (только вид).
- `PlayStrike(int targetId, Vector3 world)` — кирка игрока с кончиком в точке касания (зажата в пределах спрайта цели, у правого
  края поля зеркально), искорка появления. В кадр удара (`Sync`, после всего остального) вызывается `StrikeHandler(targetId)`:
  при успехе цель вздрагивает (`OnHit`), летят сколы, `Impact` в точке касания, подсказка `PlayerStrike`; при отказе — `Puff`.
  `int ActiveStrikes` — активные анимации. Пересборка поля гасит удары без вызова обработчика.

Анимации идут по `Time.deltaTime`; положение котиков — по `Sim.Now`, поэтому ускорение ×100 двигает котиков быстрее,
а ходьба считается по пройденному пути (лапы не скользят). `VisualSettings.ReduceEffects` уменьшает бюджеты эффектов.

## 3. Координаты (все публичные позиции — мировые)

Локально в корне поля — формула RUNTIME_SPEC §4.1: клетка `(col, row)` → `(col + 0.5, 0, Depth − row − 0.5)`; лагерь
(строки `Depth..Depth+2`) — `z = −0,5…−2,5`, двор построек — `−3…−6`, нижняя стена — `−6…−7`.
**Корень поля поднят на `OriginZ = BaseRows + StripRows = 6`**: низ двора лежит в мировой `z = 0`. `CameraRig` считает низ поля
нулём, поэтому `Rig.SetBoard(Width, CameraDepth, …)` с `CameraDepth = Depth + 6` показывает и лагерь, и всю карту, а
`FloatingText` (порядок `OverlayBase + 100`) остаётся поверх оверлеев. Сам `OutpostBoardView` стоит в начале координат;
**не считайте позиции формулой вручную** — берите методы ниже.

| Член | Назначение |
|---|---|
| `const int BaseRows = 3`, `const int StripRows = 3` | Строки площадки и двора |
| `float OriginZ` | Подъём корня поля (6) |
| `int CameraDepth` | Глубина для `Rig.SetBoard` (`Depth + 6`) |
| `float BaseFocusZ` | z «К базе»: `Rig.FocusOnSmooth(Board.BaseFocusZ)` (камера упрётся в низ поля и покажет лагерь) |
| `float TopZ` | Мировая z верхней кромки строки 0 |
| `Vector3 CellCenter(int cell)`, `CellCenter(int col, int row)` | Центр клетки (включая строки лагеря) |
| `Vector3 CellCenterLocal(int cell)` | То же в корне поля (для своих видов) |
| `Vector3 GridToWorld(double gx, double gy)`, `Vector2 WorldToGrid(Vector3)` | Непрерывная сетка ↔ мир |
| `int WorldToCell(Vector3 world)` | Клетка навигации (карта + площадка) или −1 |
| `Vector3 BaseWorldPosition` | Центр лагеря → `IOutpostGame.BaseWorldPosition` |
| `Vector3 DepotWorldPosition(int slot)` | Центр места склада → `IOutpostGame.DepotWorldPosition` |
| `Vector3 DepotIndicatorWorldPosition(int slot)` | Над местом склада: «+N» и **старт полёта монет к кошельку** (`FlyLoot`) |
| `Vector3 BranchWorldPosition(UpgradeBranch b)` | Постройка ветки (подсказки обучения) |
| `bool TryGetWorkerWorldPosition(int id, out Vector3)` | Показанная точка котика → `IOutpostGame.TryGetWorkerWorldPosition` |
| `bool TryGetTargetWorldPosition(int targetId, out Vector3)` | Центр footprint цели (у ворот — рамки) → `IOutpostGame.TryGetTargetWorldPosition` |
| `Vector3 GateWorldPosition(int order)` | Центр рамки ворот (камера к воротам на `SectorOpened`: `order = e.Count − 1`) |
| `OutpostCatView CatViewOf(int id)`, `OutpostTargetView TargetViewOf(int targetId)`, `BaseCampView Camp` | Виды (для сведений и обучения) |

## 4. Выбор тапом

- Метка `OutpostPickable { OutpostPickKind Kind; int Id; int Priority; OutpostSelection ToSelection(); }` на коллайдерах:

  | Kind | Id | Priority | Коллайдер | Команда контроллера |
  |---|---|---|---|---|
  | `Worker` | WorkerId | 0 | клетка котика + голова (0,8 × 1) | `SelectWorker(id)` |
  | `Target` | Id цели (индекс якоря) | 1 | footprint (у ворот — 5 клеток рамки); снимается при разрушении | `SelectTarget(id)` |
  | `Base` | 0 | 2 | двор построек (строки `Depth+3..Depth+5`) | `SelectBase()` → панель «База» |

  Груз на земле коллайдеров не имеет (ТЗ 5).
- `static bool OutpostPickable.TryPickBest(Ray ray, out OutpostPickable best, float maxDistance = 500f)` — `RaycastNonAlloc`,
  меньший приоритет, затем ближайший. Ввод: `Rig.Cam.ScreenPointToRay(pos)` → `TryPickBest` → `best.ToSelection()`.
- Запасной путь без физики: `bool Board.TryPickAtWorld(Vector3 world, out OutpostSelection sel)` (котик в радиусе 0,45 клетки →
  живая цель под точкой → двор базы).

## 5. События → эффекты (`OnEvent`, бюджет 8 эффектов и 3 «+N» на кадр; 4 при «меньше эффектов»)

| Событие | Вид | Что делает контроллер/HUD |
|---|---|---|
| `TargetHit` | цель дрожит, сколы; `Strike` у ударивших (первый из события + все `Mining` этой цели с таймером от `e.Tick`); контакт `Impact` | — (звук по `Cue.Hit`) |
| `CargoReleased` | мешочек вылетает из цели: на землю (куча растёт по приземлении) и/или в сумку ударившего | — |
| `CargoPickedUp` | мешочек прыгает из кучи в сумку котика, куча проседает | — |
| `CargoDeposited` | мешочек из сумки в воронку, ящик подпрыгивает, «+N» над местом (`FloatingText`), монетки `CoinPop`; последняя порция — `unload3` | **полёт монет к кошельку** из `DepotIndicatorWorldPosition(e.Slot)`, пульс счётчика золота |
| `TargetDestroyed` | `Puff` + `Burst` по размеру; камень исчезает; завал → обломки на полу; водопад → сухое русло; месторождение → след; ворота — створки открываются; работники цели радуются | — |
| `SectorOpened` | пролёты барьера → сломанные (пыль), свет над новым сектором, искры у ворот; затемнение сектора гаснет за 0,8 с (в `Sync`) | камера кратко к `GateWorldPosition(e.Count − 1)`, если игрок не у базы |
| `DrainingStarted` | — (котики без задач сами идут в лагерь по пути ядра) | статус «Доставляем оставшиеся монеты» |
| `StageCompleted` | сбор команды: котики идут к местам сбора в лагере, радость по приходу; выход — свет вместо досок | окно «Итоги шахты», фанфара |
| `TeamUpgradePurchased` | «поп» и свет постройки ветки (Команда — доска найма, Кирки — верстак, Обувь — вагонетка, Сумки — стеллаж, Разгрузка — склад, Оценка — ящики) | — |
| `WorkerHired` | котик выпрыгивает у доски найма и выходит к клетке спавна (догонялка вида) | — |
| `EquipmentChanged` / `ItemUpgraded` | слои экипировки всех котиков, пульс, искорки (≤ 4); шкаф пересчитывается | — |
| `TargetStruck` | — (отклик уже показан кадром удара `PlayStrike`) | — |
| `WorkerModeChanged`, `WatchdogFired` | — | — |
| `ChestOpened`, `CardUpgraded` | — | HUD: сводный тост о сундуке (карточки, эликсир, новые имена) / «Имя: уровень N» (UI_API §8) |
| `ElixirFound` | — | HUD: «+N» эликсира над целью (`FloatingText` у `TryGetTargetWorldPosition`) |

Состояния без событий (`Sync`): ступени построек по `Levels` (0 / ≥ ⅓ cap / ≥ ⅔ cap; вагонетка — Обувь ≥ ⅓; жёлоб `depot_chute` —
Разгрузка ≥ ⅓), ящики по доле доставленных порций шахты (0 / > 0 / ≥ ⅓ / ≥ ⅔), шкаф открыт, если в коллекции есть ненадетый
предмет, место склада `depot_slot_active` при резерве, свет `depot_glow` и индикатор (иконка `icon_depot`, полоса — доля текущего
`Unload`-действия, подпись — фиксированная стоимость обслуживаемой порции `Bag[0].Value`) при обслуживании.

## 6. Звук: `event Action<OutpostViewCueArgs> Cue`

Вид звук не играет. Подсказка приходит в момент показа (после бюджета), аргументы — структура без выделений:
`Cue`, `Position` (мир), `WorkerId`, `TargetId`, `Slot`, `Branch`, `Count`, `Material` (`HitMaterial`), `Value` (`BigInteger`).

| Cue | Источник | Рекомендация (RUNTIME_SPEC §7) |
|---|---|---|
| `Footstep` | кадр касания лапы (≤ 1 на кадр поля) | очень тихо, ≤ 1 голос, ≥ 0,25 с |
| `Hit` | `TargetHit` | `Sfx.Hit(args.Material)`, ограничение частоты |
| `SackPop` | `CargoReleased` | мягкий «плюх», редко |
| `Pickup` | `CargoPickedUp` | подбор |
| `BagFull` | `Bag.Count` достиг вместимости (Sync) | «сумка заполнена» |
| `Deposit` | `CargoDeposited` (`Value`, `Slot`) | сдача: ≤ 2 голоса, ≥ 0,12 с, питч по стоимости |
| `UnloadDone` | последняя порция сумки сдана | «разгрузка завершена» |
| `TargetBroken` | `TargetDestroyed` (`Material`) | разлом |
| `SectorOpened` | `SectorOpened` | `Sfx.GateOpen()` |
| `WorkerSpawned` | `WorkerHired` | найм |
| `Upgrade` | `TeamUpgradePurchased` (`Branch`, `Count`) | `Sfx.Upgrade()` |
| `Equip` | `EquipmentChanged` / `ItemUpgraded` | экипировка |
| `TeamGathered` | `StageCompleted` | фанфара итогов |
| `PlayerStrike` | кадр удара кирки игрока (`Material`) | `Sfx.PlayerStrike(material)`: удар громче, ≥ 0,06 с |

Звуки, не связанные с полем (покупка отклонена, окна), контроллер играет по своим командам и `EventPresented`.

## 7. Котик (`OutpostCatView`)

- Положение — из ядра: на ребре `CellCenter(Cell) → CellCenter(Path[PathIndex])` по `p = P0 + (Now − SegmentStart)/Duration`
  (`P0` переводится в double один раз на отрезок), иначе центр `Cell`; плюс стабильное смещение по Id (±5 px по x, ±3 px по z).
  Ожидающие склада (`WaitingDepot`) стоят в строке pad2 вокруг точки приёма с шагом ½ клетки по порядку `Run.Queue`.
  Разрыв положения (вход/выход из очереди, найм, конец шахты, сбор команды) вид догоняет шагом (≥ 2,4 кл/с, ≤ 2 с) — логика
  не меняется. Разрыв определяется как смещение больше `ΔNow / EdgeTicks` клетки за кадр.
- Кадры по `Mode`: в пути — `walk*` (сумка пуста) / `carry*` (есть груз); `Mining` — стойка, замах по времени до `Hit.Deadline`
  (окно `min(0,3 с, 0,45 · HitInterval)`), `Strike` по `TargetHit`; `PickingUp` — `pickup0..3` по доле действия; `Unloading` —
  `unload0..2` по доле, `unload3` после последней порции; `WaitingDepot` — дыхание/моргание с грузом; `Idle` — + потягивание.
  Разовые: появление (найм), радость (цель разрушена, приход на сбор).
- Слои: тело по `WorkerState.Look % 6`; `boots/pack/hat/pick` — по надетому комплекту команды (`OutpostLook.KindFor`, пустой
  слот — `base`), меняются только на `EquipmentChanged`/`ItemUpgraded`; `cat_load_{1|2|3}` по заполнению (< 50 %, < 100 %, ≥ 100 %);
  `cat_sack_*` в кадрах подбора/сдачи. Взгляд — `flipX` всех слоёв.
- Котик в строке закрытых ворот рисуется перед створками (он бьёт ворота снизу, стоя в строке ворот).

## 8. Отладка (DevTools)

- `OutpostDebugOverlay.Enabled = true/false` (статический флаг; только debug-профиль). Слой обновляется 10 раз в секунду:
  пути (цвет котика `OutpostDebugOverlay.WorkerColor(id)`), закреплённые рабочие клетки, партии (зелёная — есть свободные порции,
  оранжевая — всё в резерве), места склада (красное — резерв, бирюзовое — обслуживание), очередь (точки у точки приёма),
  линия барьера между `BarrierRow` и строкой над ней.
- Счётчики груза DevPanel берёт из ядра (`Run.Batches`, `Workers[].Bag`, `Run.Counters`), вид их не дублирует.

## 9. Что переиспользовано

`BoardView`: меш пола с UV атласа, хеш вариантов, `TileName` (+ строки лагеря), декор стен по `DecoSeed`, фонари/шестерни/блеск,
затемнение секторов с плавным открытием, статические `Hash`/`CanvasOrigin`/`CenteredPos`/`FillRect`. `ObjectView`: сборка камня
(`PresentationCatalog.RockSprite`, `_d1.._d3` с откатом на `rock_crack_*`), завалов (водопад, одиночные блоки, 9-тайлы), шахты,
ворот, дрожание и сколы, уголки выбора, HP-бар. `CatView`: машина кадров (дыхание, моргание, потягивание, замах/удар, появление,
радость, «поп»), снап визуала и порядок по ногам. `LootView`: ноги и порядок груза, дуга вылета. Общие `PixelWorld`, `PixelTheme`,
`PixelArt`, `Effects`, `FloatingText`, `Label3D`, `HpBar`, `Palette`, `VisualSettings` — без изменений.

## 10. Открытые пункты

- `CameraRig` не менялся: при подъёме корня на 6 он работает как есть; «камера кратко к воротам» и возврат — у контроллера.
- Анимации на устройстве не проверены (ТЗ 16): ходьба с грузом, очередь, смена комплекта, расчистка водопада.
- Экспорт сведений о цели (русское имя типа, причины недоступности) — у HUD через `PresentationCatalog`/`Loc`; вид имён не даёт.
