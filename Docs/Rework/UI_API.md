# UI режима CatOutpostV2 — API для контроллера, обучения и DevTools

Код: `Assets/Game/Scripts/Runtime/Outpost/UI/` (`namespace CatMiners.Runtime.Outpost`, сборка `CatMiners.Runtime`).
HUD работает **только через контракт** `IOutpostGame` (`Runtime/Outpost/IOutpostGame.cs`) и не знает класса контроллера.
Источники: ТЗ 2.1 §5, §9–§11, §13–§15; `RUNTIME_SPEC.md` §5–§6; карта старого UI — `map/ui.md`.

| Файл | Что внутри |
|---|---|
| `UI/OutpostHud.cs` | `OutpostHud : MonoBehaviour`: холст, верх и низ, полоса фазы, тост, полёт монет, доли панелей, стек окон и Back, переход, кадр/обновление, события ядра |
| `UI/OutpostHud.BasePanel.cs` | Панель «База»: 6 веток, шкала уровня до максимума каждой ветки, ×1/×10/«Макс», эффект «сейчас → после», цена, подсказка узкого места |
| `UI/OutpostHud.Equipment.cs` | «Экипировка»: детали, «Надеть лучшее», 4 слота, фильтр, коллекция (PixelCard), окно предмета |
| `UI/OutpostHud.Cards.cs` | «Карты»: эликсир, «Собрано N из 61», сортировка, сетка 61 `CardView` (3 колонки), окно сведений с «Улучшить», бейдж кнопки, сообщения о сундуках/эликсире/улучшении |
| `UI/CardView.cs` | Общая карточка коллекции и сведений: рамка и фон редкости, арт `card_art_<id>` (виден всегда), имя, короткий эффект, «Ур. N», копии X/Y; `detail: true` — крупные портрет, тексты и счётчик без повторной подписи у полученной карточки; закрытая — затемнение 60 % над артом и фоном портрета, яркий замок поверх |
| `UI/OutpostCardText.cs` | Имена 61 карточки, редкости, спрайты, полный и короткий эффект, величина бонуса `EffectValue` из `CardDef.EffectAt`, подписи состояний, причины отказа |
| `UI/OutpostHud.Info.cs` | Сведения о выбранном котике/цели (без кнопок) |
| `UI/OutpostHud.Results.cs` | «Итоги шахты», «Пока вас не было» + шкала расчёта, конец кампании, ошибка, общее модальное окно |
| `UI/OutpostHud.Settings.cs` | Настройки (громкость, музыка, звуки, атмосфера, вибрация, «меньше эффектов», подсказки обучения) |
| `UI/OutpostDialog.cs` | Диалог по содержимому на `PixelUi.Window` (иконка, текст, строки, блок `Extra`, кнопки `btn`) |
| `UI/OutpostFlowStats.cs` | Наблюдение за состояниями команды за ≈20 с для подсказки ограничения потока |
| `UI/OutpostUiText.cs` | Имена веток/слотов/редкостей/16 предметов, бонус предмета и его величина для сравнения уровней, задачи котика, имена целей, причины отказа, показ чисел |
| `OutpostTutorial.cs` | 10 шагов ТЗ §15 через `TutorialHint` |
| `Core/Text/Loc.Outpost.cs` | Раздел словаря `Outpost`, ключи `outpost.*` (≈210), формы слова `.one/.few/.many` |
| `Core/Text/Loc.Cards.cs` | Раздел `Cards`, ключи `cards.*`: имена, ресурсы, эффекты, состояния, сведения, сообщения, редкости |

## 1. Создание

```csharp
// RUNTIME_SPEC §3.1: после CameraRig.Create / Effects / Sfx / OutpostBoardView, до OutpostInput.
Hud = OutpostHud.Create(this, Rig);          // IOutpostGame game, CameraRig rig = null
Tutorial = new OutpostTutorial(this, Hud);   // подписывается на EventPresented и подключается к HUD
Hud.OnWindowOpened += () => Input.CancelGesture();   // ТЗ 14: отменить жест при открытии окна
```

- Холст как у старого `Hud`: переиспользуется корневой Canvas сцены «HUD» (если на нём нет `Hud`/`OutpostHud`), иначе создаётся;
  Screen Space Overlay, `sortingOrder = 30000`, CanvasScaler 1080×1920 match 0.5, GraphicRaycaster, ребёнок `SafeArea`
  (компонент `SafeArea`). Компонент `OutpostHud` живёт на GameObject Canvas. EventSystem с `InputSystemUIInputModule` создаёт
  контроллер (как `GameController.EnsureSceneBasics`).
- В `Create` HUD подписывается на `IOutpostGame.EventPresented` (отписка в `OnDestroy`).
- `Rig` (можно задать позже) получает доли панелей через `CameraRig.SetUiInsets` сразу при изменении; `WorldCamera` (по умолчанию
  `Rig.Cam`, затем `Camera.main`) — для перевода мировых точек в экранные (полёт монет).

## 2. Что вызывать каждый кадр

**Ничего обязательного.** `OutpostHud.Update` сам: прячет тост, опрашивает Back, зовёт `OutpostTutorial.Update`, ведёт полёт
монет, снимает пробы для подсказки узкого места, открывает «Базу» по выбору постройки, обновляет HUD не чаще 10 Гц.

- `Refresh()` — то же обновление с ограничением 10 Гц (можно звать каждый кадр, лишние вызовы пропускаются).
- `RefreshNow()` — немедленно (после загрузки, офлайна, перехода, внешней команды DevTools). Строки пересобираются только при
  смене исходных значений (кэш по значению), повторное обновление без изменений не выделяет память (проверено тестом).
- `OutpostTutorial.Update()` можно звать и из контроллера — второй вызов в том же кадре ничего не делает.

## 3. События и свойства

| Член | Назначение |
|---|---|
| `event Action OnWindowOpened` | Открылось любое окно (в том числе окно предмета поверх «Экипировки») — отменить текущий жест |
| `event Action<float, float> InsetsChanged` | Доли панелей изменились: `(top, bottom)` — доли высоты safe area (для `CameraRig.SetUiInsets`) |
| `float TopInset`, `float BottomInset` | Нижний край верхней полосы от верха / верхний край нижней части от низа safe area (как у старого `Hud`, те же высоты) |
| `void FieldBandPixels(out float bottom, out float top)` | Полоса поля между панелями в пикселях экрана (для ввода) |
| `bool AnyWindowOpen` | Открыто окно с затемнением — поле не должно получать касания (затемнения и так ловят лучи UI) |
| `bool BlockingWindowOpen` | Верхнее окно требует решения (ошибка, расчёт офлайна, модальное без отмены) |
| `BaseOpen`, `EquipmentOpen`, `ItemDetailOpen`/`ItemDetailId`, `CardsOpen`, `CardDetailOpen`, `SettingsOpen`, `ResultsOpen`, `OfflineOpen`, `OfflineCalcOpen`, `CampaignOpen`, `ErrorOpen`, `ModalOpen`, `InfoVisible` | Состояние окон |
| `ShowCards()`, `HideCards()`, `ShowCard(id)`, `HideCardDetail()`, `UpgradeShownCard()` | Экран «Карты» и окно сведений (открываются в любой фазе, как «Экипировка»; симуляция продолжается) |
| `ShowStore()`, `HideStore()`, `StoreOpen`, `ChestRewardOpen`, `ChestRewardChests`, `ChestRewardTileCount`, `ChestRewardTile(i)`, `SkipResultOpen` | Магазин (ТЗ 10.5, §8 ниже): открывается в любой фазе; окна награды сундука (магазин и сундук шахты с новой карточкой) и итога тайм-скипа |
| `BuyMode BuyMode` | Режим пакетной покупки панели «База» |
| `bool TryGetInfoTop(out float screenY)` | Верхний край панели сведений в пикселях — поднять выбранного котика/цель над ней (как `Hud.TryGetContextTop`) |
| `TutorialHint Hint`, `OutpostTutorial Tutorial` | Подсказка обучения и подключённое обучение |
| `bool PollBackButton = true`, `bool AutoFlyOnDeposit = true` | См. §4, §5 |

## 4. Back (ТЗ 14)

- По умолчанию HUD сам опрашивает `Keyboard.current.escapeKey` (на Android кнопка Back = Escape в Input System) и действие
  `cancel` текущего `InputSystemUIInputModule`. Если Back обрабатывает `OutpostInput`, поставьте `PollBackButton = false` и
  вызывайте `bool HandleBack()`; повторный вызов в том же кадре возвращает прежний результат и ничего не делает.
- Порядок: верхнее окно стека (окно предмета → «Экипировка» → итоги/«База»/настройки …) → снять выбор (`ClearSelection`) →
  `false` (не обработано). Окна ошибки, расчёта офлайна и модальное окно без кнопки `Cancel` Back поглощает, не закрывая.
  Во время затемнения перехода Back поглощается. «Пока вас не было» по Back = «Отлично» (`DismissOffline`).

## 5. Полёт монет

Только на `CargoDeposited` (ТЗ 7.3): HUD сам запускает `FlyCoins(DepotWorldPosition(e.Slot), 1)` из `EventPresented`. Контроллер
**не** зовёт `FlyCoins` на сдаче, пока `AutoFlyOnDeposit = true`. `public void FlyCoins(Vector3 world, int count)` — пул 24, 1…6 монет,
не чаще раза в 0,06 с; под окнами и вне экрана — только «пульс» иконки кошелька.

## 6. Окна по состоянию контракта (открываются сами, 10 Гц)

| Условие | Окно | Кнопки |
|---|---|---|
| `Phase == Error` | Ошибка: `ErrorText` (или общий текст) | «Повторить» → `RetrySave()`, если `SaveFailed`; Back не закрывает |
| `Phase == OfflineCatchUp` | Шкала расчёта (`OfflineProgress01`); также строка в полосе | нет; Back не закрывает |
| `PendingOffline != null` и `Phase == Playing` | «Пока вас не было»: только полученное золото, включая 0 (один раз на объект сводки) | Слева «Double income» / телевизор «Watch ad» → `ClaimOfflineDouble()` (тестовый режим: доход ещё раз, окно закрывается; `unavailable` → тост «Ads are coming soon»); справа «Great» → `DismissOffline()` |
| `Phase == OfflineCatchUp` и `SkipMinutesInProgress > 0` | Та же шкала с заголовком «Time Skip» и «The cats work 10m in a flash…» | нет; Back не закрывает |
| `PendingSkip != null` и `Phase == Playing` | Итог тайм-скипа: «Time Skip: 10m» (`SkipMinutes`, арт плитки `SkipItemId`), «Gold earned» крупно, расчищено, карточки и эликсир, «шахта завершена» (один раз на объект) | «Great» / Back → `DismissSkip()`; итоги шахты открываются после него |
| `Run.Phase == ReadyToContinue`, нет офлайна и перехода | «Итоги шахты» — один раз на `RunId`; после закрытия в HUD — «Перейти на следующую шахту» | «Экипировка»; «Следующая шахта» в окне или «Перейти на следующую шахту» в HUD → `ContinueToNextMine()` (доступна при `CanContinue` и без перехода) / «Повторить сохранение» → `RetrySave()` при `SaveFailed` / «Далее» → окно конца кампании при `CampaignComplete` |
| `SaveFailed` в обычной игре | Кнопка «Не сохранено — повторить» в полосе над панелью | `RetrySave()` |

Награда в итогах читается из `StageProgressionService.CompletionReward(State)` — окно ничего не бросает заново. Итоги и окно
конца кампании закрываются сами при смене `RunId` (новая шахта) и в `BeginTransition`.

Кнопка перехода в HUD занимает всю ширину полосы с полями Safe Area; высота 168 единиц Canvas при эталоне 1080×1920,
надпись — `FontTitle` (56), перенос разрешён, автоматическое уменьшение выключено. Полоса высотой 176 учтена в
`BottomInset` и резерве обучения. Нажатие запускает переход напрямую; `CanContinue` и `CommandsAllowed` обновляют
доступность кнопки даже без смены фазы шахты. После последней шахты она открывает завершение кампании.

Вручную: `ShowBase/HideBase/ToggleBase`, `ShowEquipment/HideEquipment`, `ShowItem(itemId)/HideItem`, `ShowSettings/HideSettings`,
`ShowResults/HideResults`, `ShowCampaign/HideCampaign`, `ShowOffline(OfflineSummary)`, `CloseAllWindows()`, `Toast(text, seconds)`,
`Modal(title, body[, icon], params OutpostHud.ModalButton[])` — очередь FIFO; кнопка закрывает окно и затем вызывает `Action`;
`ModalButton.Cancel = true` — эту кнопку нажимает Back.

Настройки во всех сборках (включая WebGL Release) содержат Reset Progress. В Editor/Development Build меню «DEV 4» также
использует этот диалог для кнопки «Сбросить прогресс»: «Отмена»/Back закрывают вопрос,
«Начать заново» вызывает `OutpostController.ResetProgress()` для выбранного Main или Debug. Кнопка включена только при
`CanResetProgress`; после успешной записи игра показывает шахту 1 с новым прогрессом и обучением.
Настройки показывают `State.Profile.PlayTimeMs` как часы/минуты (`Play time: 2h 6m`), обновляя строку при смене минуты.
Счётчик сохраняется между запусками и при Reset Progress; считается только реальное активное время, без фона и симулируемых скипов.

## 7. Выбор и сведения

- `Selection.Kind == Worker/Target` → панель сведений над нижней частью (котик: задача, сумка n/cap со шкалой, стоимость груза;
  цель: имя типа из словаря, HP со шкалой, котики на цели `AssignedTo`, оставшиеся порции `Total − Released`, причина
  «Закрытый сектор» / «Ждёт расчистки сектора» / «Нет пути» / «Расчищено»). Под окнами панель скрыта.
- `Selection.Kind == Base` (тап по постройке лагеря) → HUD открывает «Базу» и сам вызывает `ClearSelection()`.
- «К базе» → `FocusBase()`.

## 8. Команды и причины отказа

Команды (`Buy`, `Equip`, `Unequip`, `EquipBest`, `UpgradeItem`, `ContinueToNextMine`) HUD отправляет только при
`Phase == Playing` и без перехода (`InTransition` контракта и HUD), иначе — тост «Идёт расчёт офлайна» / «Идёт переход …» /
«Игра загружается». Отказ `CommandResult.Reason` показывается тостом и звуком отказа; тап по недоступной кнопке тоже называет
причину. Коды → тексты (`OutpostUiText.Reason`): `gold` «Не хватает доставленного золота», `cap` «Максимальный уровень», `phase`
«Шахта завершена», `mode` «Найм — только по одному», `max` «Предмет максимального уровня», `parts` «Не хватает деталей»,
`not-owned`, `already`, `empty`, `offline`, `transition`, `loading`, `closed_sector`, `no_path`; прочие — «Сейчас недоступно».
Контроллер может вернуть эти же коды в своих отказах.

Панель «База»: цена и число уровней — из `Quote` (×10/«Макс» — фактическое число и точная сумма), «после» — `TeamStats.Compute`
ядра с уровнем после покупки. У всех шести веток шкала `CurrentLevel / Cap` из текущей котировки, с учётом бонусов карточек:
до максимума золотая, на максимуме полная зелёная, остаётся видимой вместе с «Макс.» без цены. Не отражает ещё не купленный пакет.
Подписи и отдельные шкалы порогов «Кирок» и «Оценки добычи» удалены. Бейдж «Базы» — число веток, где `Wallet ≥ Quote(×1).NextCost`
(0 в ReadyToContinue).
Красный пин «Экипировки» без числа — есть хотя бы один полученный предмет ниже максимального уровня, на улучшение которого
хватает текущего общего запаса деталей. На карточке такого предмета — тот же пин справа сверху; отметка «надето» — снизу.
Шкалы деталей на карточках скрыты. Пины показываются только при доступных командах и обновляются после наград и улучшений;
открытие экрана их не снимает. Цена в деталях остаётся в окне предмета.
Окно предмета: справа от карточки столбец (`VerticalLayoutGroup`) — `head` (имя, «редкость · уровень»), строка `bonus`
(`now` — `OutpostUiText.ItemBonus`, `arrowBox` с `icon_upgrade`, повёрнутой вправо, `next` — `OutpostUiText.ItemBonusValue`
зелёным `Green6`; на максимуме стрелка и `next` скрыты), `note` («Equipped» или «Now: бонус надетого → with this: величина
этого предмета», детали / «Max level»).

«Карты»: весь каталог (61) виден с начала; порядок — полученные, затем по шахте открытия, весу `SortingWeight` и ID
(переключатель «Сначала улучшаемые»). Состояние карточки — `CardService.StateOf`, котировка — `CardService.Quote`; кнопка
«Улучшить» → `IOutpostGame.UpgradeCard(id)` (одна транзакция и запись), видна только у полученной карточки ниже максимума и
активна при `quote.CanUpgrade` и доступных командах; причина (`cards.reason.*`: копии, эликсир) — тостом при нажатии отключённой
кнопки. Окно всех 61 карточек: крупная `CardView` примерно в верхней половине экрана; имя — 56 единиц Canvas, уровень,
короткий бонус и один счётчик копий X/Y с иконкой — 48. Под ней — `EffectValue(L)` → `EffectValue(L+1)`, зелёная стрелка,
шрифт 56; такие же увеличенные цена в эликсире (иконка ×2) и «Улучшить» высотой 144. Повторные абзацы редкости, ресурса,
достигнутой шахты, «Сейчас / После улучшения», копий и причины нехватки убраны. У неполученной — условие получения только
на карточке («Откроется на шахте N» / «Доступна в сундуках»), без кнопки. На максимуме скрыты сравнение, цена и покупка.
Сетка пересобирает тексты только при
смене подписи профиля (уровни, копии, эликсир, HighestZone). Бейдж «Карт» — `CardService.AnyUpgradable`.
События: `ChestOpened` → один сводный тост за 0.1 с («Сундук: карточек +N, эликсира +M», новые карточки по имени,
учебная — «Первая карточка: …»), `ElixirFound` → «+N» над целью (`FloatingText`), `CardUpgraded` → «Имя: уровень N».
Окно «Итоги шахты» показывает строку карточек и эликсира, если они были. В «Пока вас не было» показана только сумма золота;
карточки и эликсир за офлайн начислены и сохранены вместе с остальным результатом и доступны в обычных экранах.

«Store» (`OutpostHud.Store.cs`, `StoreTileView.cs`): каталог — `Configs.Store` (`store_catalog.json`), котировка плитки —
`StoreService.Quote(profile, store, def, UtcNowMs)`. Кнопка Store крайняя слева: Store · Cards · Base · Equipment · To base;
красная точка — есть готовая рекламная плитка (`Quote.Free` у `Currency == Ad`) при доступных командах. Плитка:
`ui_btn_green` — `Quote.Free` (реклама готова, свой тайм-скип: «USE», «Available: N»), `ui_btn_blue` — покупка (цена в гемах,
красная при `BlockReason == "gems"`, или `$N.NN`), `ui_btn_dark` — таймер рекламы (`icon_clock` + время) или `unavailable`
(«Soon»). Бонус первой покупки (только если задан в каталоге; в `store-3` бонусов нет): бейдж «+N%», зачёркнутое обычное количество, «Available: 1». Тап: команды заблокированы →
причина; `cooldown` → тост «Free again in …»; `gems` → «Not enough gems» и прокрутка к Gems; `Free` → сразу; иначе
`Modal` «Buy X?» (Cancel / Buy). Действие: `TimeSkip` → `IOutpostGame.UseTimeSkip(id)` (`phase` в `ReadyToContinue`),
остальное → `IOutpostGame.StoreBuy(id)`. События: `ChestOpened` с `TargetId == OutpostIds.StoreSource` → окно «ChestReward»
(карточки плитками `ChestCardTile` по три в ряд: рамка и фон редкости, арт, «×N», имя, «NEW»; новые первыми, до 6, «+N more»,
эликсир; тост сундуков шахты для него не показывается). Сундук шахты (`ChestOpened`, не `StoreSource`, не «tutorial») с новой
карточкой (`Extra > 0`) открывает то же окно, если не открыто окно магазина; пока окно сундуков шахты открыто, следующие сундуки
шахты добавляются в него (заголовок «N Chests»); остальные — тост «Chest: cards +N». Автоокно итогов шахты ждёт его закрытия.
`StorePurchased` → «+N gems» / «+N elixir», `BonusGold` → «Income doubled: +N gold». Кнопка «i» сундука → окно «DropRates» (`CardChestService.Preview` и
`ChestChances`). Причины: `gems` «Not enough gems», `cooldown` «Not ready yet», `unavailable` «Coming soon».

## 9. Переход (необязательно)

`BeginTransition()` (экран на весь Canvas ловит касания, окна и сведения закрыты, подсказка скрыта) → `SetTransitionFade(0…1)`
(ступени по 1/8, navy0) → `EndTransition()` (немедленное обновление). Пока идёт затемнение, HUD не обновляется.

## 10. Обучение

```csharp
var tutorial = new OutpostTutorial(game, hud);   // IDisposable: Dispose отписывает от EventPresented
tutorial.Skip();                 // «Пропустить обучение» (кнопка под табличкой, пока видна подсказка)
tutorial.SetEnabled(bool);       // строка «Подсказки обучения» в настройках
tutorial.IsDone(OutpostTutorial.StepPickaxes); tutorial.CurrentHint; tutorial.Active; OutpostTutorial.Steps
```

- Состояние — `Profile.Tutorial`: `Seen` — id пройденных шагов `o1_auto, o10_strike, o2_bag, o3_gold, o4_pickaxes, o5_team, o6_targets,
  o7_reward, o8_reset, o9_kept`; новый `o10_strike` стоит после автодобычи, идентификаторы прежних шагов сохранены.
  `Step` — номер последнего пройденного в текущем порядке; `Dismissed` — обучение закрыто. Каждое изменение
  записывается через `IOutpostGame.SaveTutorialProgress()`.
- Шаги 1–7 — только на zone1, 8–9 — в ReadyToContinue zone1 (лапка на кнопках итогов поверх окна), 10 — на zone2. На следующих
  шахтах пропущенные шаги засчитываются молча. Подсказка не ловит касания и не останавливает добычу; под окнами скрыта, кроме
  шагов, указывающих в верхнее окно (строка «Кирки»/«Команда» в «Базе», кнопки итогов).
- `StepStrike`: рамка и лапка на живом видимом камне из `Sim.CanStrike`; `StrikeTargetId` — ID показанной цели или −1.
  При разрушении или уходе цели с экрана выбирается другая. Подсказка предлагает ручной удар 8 видимых секунд и не задерживает
  остальные уроки; если камни ещё не видны, камера один раз плавно переводится к ближайшему доступному.
  `TargetStruck` с положительным уроном на zone1 завершает шаг и сохраняет его, `TargetHit` котиков — нет.
  Ранний ручной удар тоже засчитывается. Непройденный шаг повторяется при загрузке первой шахты; на zone2+ засчитывается молча.
- Цели подсказок (`TutorialHint.RegisterTarget`), константы `OutpostHud`: `TargetBase`, `TargetEquipment`, `TargetCards`, `TargetToBase`,
  `TargetGold`, `TargetCargo`, `TargetStripAction`, `TargetResultsEquipment`, `TargetResultsNext`, `TargetEquipBest`,
  `BranchTarget(UpgradeBranch)` — кнопки покупки веток.

## 11. Изменения общих файлов

- `IOutpostGame.cs`: члены не менялись; добавлены `using`-псевдонимы `CommandResult`/`OfflineSummary` на типы `Core.Outpost`
  (с `using CatMiners.Core.Sim` ссылки были неоднозначны — CS0104, файл не компилировался).
- `Core/Text/Loc.cs`: раздел `Outpost` (объявление `RegisterOutpost`, `AddSection`, запись в `Sections`).
- Общие классы кита (`PixelUi`, `UiKit`, `TutorialHint`, `SafeArea`, `CameraRig` …) не менялись.

## 12. Тест

`Assets/Game/Tests/PlayMode/Outpost/OutpostHudTests.cs` — HUD и обучение с поддельным `IOutpostGame` поверх настоящего ядра zone1:
все окна, Back, команды в ядро, автооткрытие итогов, шаги обучения 1 и 7, словарь без пропусков, 0 выделений при обновлении без
изменений. Запуск: `powershell -ExecutionPolicy Bypass -File Tools/unity.ps1 -Mode PlayMode -TestFilter OutpostHudTests`.
