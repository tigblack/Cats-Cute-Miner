using System.Collections;
using System.Collections.Generic;
using CatMiners.Core.Sim;
using CatMiners.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>
    /// Жесты через настоящий Input System (ТЗ 13.3, 18): тап, перенос, объединение, прокрутка, жест на UI, отмена касания,
    /// карта действий Gameplay, перенос динамита из ячейки.
    /// События ставятся в очередь и обрабатываются обычным обновлением ввода следующего кадра — как на устройстве.
    /// </summary>
    public class PointerInputTests : InputTestFixture
    {
        string m_SaveDir;
        LogWatcher m_Log;
        Mouse m_Mouse;

        /// <summary>
        /// Сцена предыдущего теста без InputTestFixture (например, PerformanceTests) остаётся загруженной, и её ввод
        /// включён в настоящей системе ввода. Его нужно выключить до подмены системы ввода: иначе PointerInput и модуль UI
        /// выключатся при загрузке сцены уже в тестовой системе, мониторы состояния останутся в настоящей, а освобождённое
        /// состояние карты PointerInput после TearDown даст NullReferenceException в InputActionState на первом событии мыши.
        /// </summary>
        [SetUp]
        public override void Setup()
        {
            DisableLeftoverSceneInput();
            base.Setup();
        }

        static void DisableLeftoverSceneInput()
        {
            var game = GameBootstrap.Controller;
            if (game != null && game.Input != null) game.Input.enabled = false;
            var es = EventSystem.current;
            var module = es != null ? es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() : null;
            if (module != null) module.enabled = false;
        }

        [UnitySetUp]
        public IEnumerator SetUpGame()
        {
            m_SaveDir = PlayModeUtil.UseTempSaveDir();
            CameraRig.OverrideAspect = 0f;
            // Остановленные часы: тестам ввода время симуляции не нужно. Задержка кадра больше 1 с в batchmode (например,
            // первая компиляция шейдера) иначе считалась бы офлайном и открывала «Пока вас не было» поверх поля (ТЗ 12.3).
            GameController.ClockOverride = new ManualClock(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 100.0);
            m_Log = new LogWatcher();
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDownGame()
        {
            m_Log.Dispose();
            GameController.ClockOverride = null;
            PlayModeUtil.DropTempSaveDir(m_SaveDir);
            yield break;
        }

        GameController Game => GameBootstrap.Controller;

        IEnumerator StartGame(bool mouse = true)
        {
            // В batchmode у окна нет фокуса, а модуль UI без фокуса ввод не обрабатывает. На устройстве фокус есть.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            if (mouse) m_Mouse = InputSystem.AddDevice<Mouse>();
            yield return PlayModeUtil.LoadCoreGame();
            Assert.IsNotNull(Game);
            // Ассет действий UI общий: после тестов с настоящей системой ввода его привязки указывают на старые устройства,
            // а InputTestFixture подменяет систему ввода. Модулю UI даётся свежая копия, привязанная к устройствам теста.
            var module = EventSystem.current.currentInputModule as UnityEngine.InputSystem.UI.InputSystemUIInputModule;
            if (module != null && module.actionsAsset != null) module.actionsAsset = Object.Instantiate(module.actionsAsset);
            TestContext.WriteLine("Screen " + Screen.width + "×" + Screen.height + ", dpi " + Screen.dpi);
            if (mouse)
            {
                Move(m_Mouse.position, new Vector2(1, 1), queueEventOnly: true);
                yield return null;
            }
        }

        // ───────────── Координаты ─────────────

        Vector2 Screen2(Vector3 world)
        {
            var p = Game.Rig.Cam.WorldToScreenPoint(world);
            return new Vector2(p.x, p.y);
        }

        /// <summary>Точка для тапа по котику — центр его коллайдера.</summary>
        Vector2 CatPoint(int catId) => Screen2(Game.Board.CatViewOf(catId).transform.position + Vector3.up * 0.5f);

        /// <summary>Точка на полу клетки (сброс переносом).</summary>
        Vector2 CellPoint(int cell) => Screen2(Game.Board.CellCenter(cell));

        bool OverUi(Vector2 pos)
        {
            var es = EventSystem.current;
            var hits = new List<RaycastResult>();
            es.RaycastAll(new PointerEventData(es) { position = pos }, hits);
            return hits.Count > 0;
        }

        void AssertOnBoard(Vector2 pos, string what)
        {
            Assert.IsTrue(pos.x >= 0 && pos.y >= 0 && pos.x < Screen.width && pos.y < Screen.height, what + ": точка за экраном " + pos);
            Assert.IsFalse(OverUi(pos), what + ": точка под интерфейсом " + pos + " — " + UiHitNames(pos));
        }

        string UiHitNames(Vector2 pos)
        {
            var es = EventSystem.current;
            var hits = new List<RaycastResult>();
            es.RaycastAll(new PointerEventData(es) { position = pos }, hits);
            var names = new List<string>();
            foreach (var h in hits)
            {
                names.Add(h.gameObject.name + (h.gameObject.transform.parent != null ? " < " + h.gameObject.transform.parent.name : ""));
                // Тексты окна, которое закрыло точку (чтобы по журналу было видно, какое это окно).
                var box = h.gameObject.transform;
                while (box != null && box.name != "Box") box = box.parent;
                if (box != null)
                    foreach (var t in box.GetComponentsInChildren<TMPro.TMP_Text>()) names.Add("«" + t.text + "»");
            }
            return string.Join(", ", names);
        }

        /// <summary>Поставить камеру так, чтобы точка мира оказалась в середине поля между панелями.</summary>
        IEnumerator Focus(Vector3 world)
        {
            Game.Rig.FocusOn(world.z);
            Game.ClearSelection();
            yield return PlayModeUtil.WaitReal(0.25f);
        }

        // ───────────── Мышь ─────────────

        IEnumerator MouseClick(Vector2 pos)
        {
            Move(m_Mouse.position, pos, queueEventOnly: true);
            yield return null;
            Press(m_Mouse.leftButton, queueEventOnly: true);
            yield return null;
            Release(m_Mouse.leftButton, queueEventOnly: true);
            yield return null;
            yield return null;
        }

        IEnumerator MouseDrag(Vector2 from, Vector2 to, int steps = 12)
        {
            Move(m_Mouse.position, from, queueEventOnly: true);
            yield return null;
            Press(m_Mouse.leftButton, queueEventOnly: true);
            yield return null;
            for (int i = 1; i <= steps; i++)
            {
                Move(m_Mouse.position, Vector2.Lerp(from, to, i / (float)steps), queueEventOnly: true);
                yield return null;
            }
            Release(m_Mouse.leftButton, queueEventOnly: true);
            yield return null;
            yield return null;
        }

        /// <summary>Нанять n котиков командой UI и выпустить их из бочек найма (тап по бочке — OpenContainer).</summary>
        IEnumerator HireCats(int n)
        {
            for (int i = 0; i < n; i++) Game.Hire();
            Assert.AreEqual(n, PlayModeUtil.OpenHireBarrels(Game), "наём ставит бочки найма");
            Assert.AreEqual(n, Game.Session.Mine.Cats.Count);
            // Котик появляется с анимацией масштаба; коллайдер растёт вместе с ним.
            yield return PlayModeUtil.WaitReal(0.8f);
        }

        static bool BoundTo(InputAction action, InputControl control)
        {
            foreach (var c in action.controls)
                if (c == control) return true;
            return false;
        }

        static Vector2 ButtonCenter(Button button)
        {
            var rt = (RectTransform)button.transform;
            return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        }

        // ───────────── Тесты ─────────────

        [UnityTest]
        public IEnumerator TapSelectsCatAndObject()
        {
            yield return StartGame();
            yield return HireCats(1);
            var s = Game.Session;
            var cat = s.Mine.Cats[0];

            yield return Focus(Game.Board.CellCenter(cat.Cell));
            var catPos = CatPoint(cat.Id);
            AssertOnBoard(catPos, "котик");
            yield return MouseClick(catPos);
            Assert.AreEqual(cat.Id, Game.SelectedCatId, "тап выбирает котика");

            int cart = s.Layout.Cart.Ordinal;
            yield return Focus(Game.Board.ObjectCenter(cart));
            var cartPos = Screen2(Game.Board.ObjectCenter(cart) + Vector3.up * 0.5f);
            AssertOnBoard(cartPos, "кузница");
            yield return MouseClick(cartPos);
            Assert.AreEqual(cart, Game.SelectedOrdinal, "тап выбирает кузницу");
            Assert.AreEqual(0, Game.SelectedCatId);
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        [UnityTest]
        public IEnumerator DragCatAssignsItToRock()
        {
            yield return StartGame();
            yield return HireCats(1);
            var s = Game.Session;
            var cat = s.Mine.Cats[0];
            int rockCell = s.Layout.CellIndex(2, 4);
            int rock = s.ObjectAt(rockCell);
            Assert.GreaterOrEqual(rock, 0);

            yield return Focus((Game.Board.CellCenter(cat.Cell) + Game.Board.CellCenter(rockCell)) * 0.5f);
            var from = CatPoint(cat.Id);
            var to = CellPoint(rockCell);
            AssertOnBoard(from, "котик");
            AssertOnBoard(to, "камень");
            yield return MouseDrag(from, to);
            Assert.AreEqual(s.Layout.Objects[rock].Index, cat.TargetIndex, "перенос на камень назначает котика");
            Assert.AreEqual(0, Game.Board.DraggedCatId, "перенос завершён");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        [UnityTest]
        public IEnumerator DragCatOntoSameLevelMerges()
        {
            yield return StartGame();
            yield return HireCats(2);
            var s = Game.Session;
            var a = s.Mine.Cats[0];
            var b = s.Mine.Cats[1];
            Assert.AreEqual(a.Level, b.Level);
            int level = a.Level;

            yield return Focus(Game.Board.CellCenter(b.Cell));
            var from = CatPoint(a.Id);
            var to = CellPoint(b.Cell);
            AssertOnBoard(from, "котик A");
            AssertOnBoard(to, "котик B");
            yield return MouseDrag(from, to);
            Assert.AreEqual(1, s.Mine.Cats.Count, "пара объединилась");
            Assert.AreEqual(level + 1, s.Mine.Cats[0].Level);
            Assert.AreEqual(1, s.Mine.Merges);
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        [UnityTest]
        public IEnumerator BackgroundDragAndWheelScroll()
        {
            yield return StartGame();
            // Глубокая карта, чтобы было куда прокручивать.
            Game.SwitchSlot(SaveSlots.Debug, true, 10);
            yield return PlayModeUtil.WaitReal(0.3f);
            var s = Game.Session;
            float z0 = Game.Rig.FocusZ;

            // Свободная клетка пола в середине экрана.
            Vector2 start = default;
            bool found = false;
            for (int cell = 0; cell < s.Layout.CellCount && !found; cell++)
            {
                if (s.ObjectAt(cell) >= 0 || s.CatAt(cell) != null || s.ContainerAt(cell) != null) continue;
                var p = CellPoint(cell);
                if (p.y < Screen.height * 0.45f || p.y > Screen.height * 0.6f || p.x < 0 || p.x > Screen.width || OverUi(p)) continue;
                start = p;
                found = true;
            }
            Assert.IsTrue(found, "нет свободной клетки на экране");

            // Палец вниз — карта едет вниз, камера смотрит выше по шахте.
            yield return MouseDrag(start, start + new Vector2(0, -Screen.height * 0.25f), 8);
            yield return PlayModeUtil.WaitReal(0.3f);
            float z1 = Game.Rig.FocusZ;
            Assert.Greater(z1, z0 + 0.5f, "перетаскивание фона прокручивает поле");
            Assert.AreEqual(-1, Game.SelectedOrdinal, "прокрутка не считается тапом");

            // Колесо мыши.
            Move(m_Mouse.position, start, queueEventOnly: true);
            yield return null;
            Set(m_Mouse.scroll, new Vector2(0, -120), queueEventOnly: true);
            yield return null;
            yield return null;
            Assert.Less(Game.Rig.FocusZ, z1, "колесо мыши прокручивает поле");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        [UnityTest]
        public IEnumerator GestureStartedOnUiDoesNotTouchBoard()
        {
            yield return StartGame();
            var s = Game.Session;
            var hire = PlayModeUtil.FindButton(Game.Hud, "Hire");
            var rt = (RectTransform)hire.transform;
            Vector2 hirePos = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            Assert.IsTrue(OverUi(hirePos));
            yield return PlayModeUtil.WaitReal(0.2f);

            // Нажатие на кнопке, отпускание над полем: ни найма, ни прокрутки, ни тапа по полю.
            float z0 = Game.Rig.FocusZ;
            yield return MouseDrag(hirePos, new Vector2(Screen.width * 0.5f, Screen.height * 0.55f), 8);
            yield return PlayModeUtil.WaitReal(0.2f);
            Assert.AreEqual(0, s.Mine.CatsHired, "кнопка не сработала при отпускании вне её");
            Assert.AreEqual(0, s.Mine.Containers.Count, "бочки найма нет");
            Assert.AreEqual(z0, Game.Rig.FocusZ, 1e-4f, "жест с UI не прокручивает поле");
            Assert.AreEqual(-1, Game.SelectedOrdinal);
            Assert.AreEqual(0, Game.SelectedCatId);

            // Обычный клик по кнопке нанимает котика и не уходит в поле.
            yield return MouseClick(hirePos);
            Assert.AreEqual(1, s.Mine.CatsHired, "клик мышью по «Нанять»");
            Assert.AreEqual(1, PlayModeUtil.HireBarrels(s).Count, "нанятый котик приехал в бочке");
            Assert.AreEqual(-1, Game.SelectedOrdinal);
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        [UnityTest]
        public IEnumerator TouchDragAssignsAndCancelRestores()
        {
            var touch = InputSystem.AddDevice<Touchscreen>();
            yield return StartGame(mouse: false);
            yield return HireCats(2);
            var s = Game.Session;
            var cat = s.Mine.Cats[0];
            int startCell = cat.Cell;
            int rockCell = s.Layout.CellIndex(2, 4);
            int rock = s.ObjectAt(rockCell);

            yield return Focus((Game.Board.CellCenter(cat.Cell) + Game.Board.CellCenter(rockCell)) * 0.5f);
            var from = CatPoint(cat.Id);
            var to = CellPoint(rockCell);
            AssertOnBoard(from, "котик");
            AssertOnBoard(to, "камень");

            // Отмена касания системой посреди переноса: котик остаётся на месте и свободен.
            BeginTouch(1, from, queueEventOnly: true, screen: touch);
            yield return null;
            for (int i = 1; i <= 6; i++)
            {
                MoveTouch(1, Vector2.Lerp(from, to, i / 12f), queueEventOnly: true, screen: touch);
                yield return null;
            }
            Assert.AreEqual(cat.Id, Game.Board.DraggedCatId, "перенос начался");
            CancelTouch(1, Vector2.Lerp(from, to, 0.5f), queueEventOnly: true, screen: touch);
            yield return null;
            yield return null;
            Assert.AreEqual(0, Game.Board.DraggedCatId, "отмена касания отменяет перенос");
            Assert.AreEqual(startCell, cat.Cell);
            Assert.AreEqual(-1, cat.TargetIndex, "отменённый перенос ничего не назначил");
            Assert.AreEqual(2, s.Mine.Cats.Count);

            // Обычный перенос пальцем.
            yield return PlayModeUtil.WaitReal(0.2f);
            from = CatPoint(cat.Id);
            BeginTouch(2, from, queueEventOnly: true, screen: touch);
            yield return null;
            for (int i = 1; i <= 12; i++)
            {
                MoveTouch(2, Vector2.Lerp(from, to, i / 12f), queueEventOnly: true, screen: touch);
                yield return null;
            }
            EndTouch(2, to, queueEventOnly: true, screen: touch);
            yield return null;
            yield return null;
            Assert.AreEqual(s.Layout.Objects[rock].Index, cat.TargetIndex, "перенос пальцем назначает котика");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>
        /// Бочка найма и добыча (ТЗ 13.3): клик мышью по бочке открывает её — котик уровня найма в клетке бочки; клик по кучке
        /// добычи собирает её — начислена ровно её сумма с ключом журнала камня. Слот Debug: камень разрушается инструментом.
        /// </summary>
        [UnityTest]
        public IEnumerator TapOpensHireBarrelAndCollectsLoot()
        {
            yield return StartGame();
            Game.SwitchSlot(SaveSlots.Debug, true, 1);
            yield return PlayModeUtil.WaitReal(0.3f);
            var s = Game.Session;
            int level = s.HireLevel();
            Game.Hire();
            var barrels = PlayModeUtil.HireBarrels(s);
            Assert.AreEqual(1, barrels.Count, "наём ставит бочку");
            Assert.AreEqual(0, s.Mine.Cats.Count, "котик ещё в бочке");
            var barrel = barrels[0];

            // Бочка появляется с «попом» масштаба; коллайдер растёт вместе с ней.
            yield return PlayModeUtil.WaitReal(0.5f);
            yield return Focus(Game.Board.CellCenter(barrel.Cell));
            var barrelPos = CellPoint(barrel.Cell);
            AssertOnBoard(barrelPos, "бочка");
            yield return MouseClick(barrelPos);
            Assert.AreEqual(0, PlayModeUtil.HireBarrels(s).Count, "клик по бочке открыл её");
            Assert.AreEqual(1, s.Mine.Cats.Count, "из бочки вышел котик");
            Assert.AreEqual(barrel.Cell, s.Mine.Cats[0].Cell, "котик в клетке бочки");
            Assert.AreEqual(level, s.Mine.Cats[0].Level, "котик уровня найма");

            // Награда камня выпадает добычей и ждёт клика (часы стоят — автосбора нет).
            var loot = PlayModeUtil.BreakRockForLoot(s);
            Assert.IsNotNull(loot, "в zone1 есть камень с валютной наградой");
            string key = loot.Currency + ":rock";
            var ledger = PlayModeUtil.Ledger(s, key);
            // Кучка вылетает из камня по дуге; сундуки других камней закрываются кнопками.
            yield return PlayModeUtil.WaitReal(0.8f);
            yield return PlayModeUtil.CloseModals(Game);
            Assert.IsFalse(Game.Hud.ModalOpen, "окна закрыты");
            var lootWorld = Game.Board.LootPos(loot.Cell);
            yield return Focus(lootWorld);
            var lootPos = Screen2(lootWorld);
            AssertOnBoard(lootPos, "добыча");
            yield return MouseClick(lootPos);
            Assert.IsNull(s.FindLoot(loot.Id), "клик по кучке собрал добычу");
            Assert.AreEqual(1, s.Mine.LootCollected);
            Assert.IsTrue(PlayModeUtil.Ledger(s, key) == ledger + loot.Amount, "начислена сумма добычи с ключом " + key);
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        [UnityTest]
        public IEnumerator PointerInputReadsGameplayActionMap()
        {
            yield return StartGame();

            // Сцена CoreGame (ProjectSetup) ссылается на CatMinersInput; модулю UI назначена его карта UI (ТЗ 13.3).
            var asset = GameBootstrap.ActiveInputActions;
            Assert.IsNotNull(asset, "в сцене не назначен CatMinersInput (Cat Miners/Setup Project)");
            Assert.AreEqual("CatMinersInput", asset.name);
            Assert.IsNotNull(asset.FindActionMap("UI"), "в ассете есть карта UI");
            var module = (UnityEngine.InputSystem.UI.InputSystemUIInputModule)EventSystem.current.currentInputModule;
            StringAssert.StartsWith("CatMinersInput", module.actionsAsset.name, "модуль UI работает на CatMinersInput");
            Assert.AreEqual("UI", module.point.action.actionMap.name);

            // PointerInput опрашивает личную копию карты Gameplay, привязанную к тестовой мыши.
            var map = Game.Input.GameplayMap;
            Assert.IsNotNull(map);
            Assert.AreEqual(PointerInput.GameplayMapName, map.name);
            Assert.IsTrue(map.enabled, "карта Gameplay включена");
            Assert.AreNotSame(asset, map.asset, "PointerInput не включает и не выключает общий ассет");
            var point = map.FindAction(PointerInput.PointActionName);
            var press = map.FindAction(PointerInput.PressActionName);
            var scroll = map.FindAction(PointerInput.ScrollActionName);
            Assert.IsNotNull(map.FindAction(PointerInput.TouchPhaseActionName), "есть действие фазы касания");
            Assert.IsTrue(BoundTo(point, m_Mouse.position), "Point = <Pointer>/position");
            Assert.IsTrue(BoundTo(press, m_Mouse.press), "Press = <Pointer>/press");
            Assert.IsTrue(BoundTo(scroll, m_Mouse.scroll.y), "Scroll = <Mouse>/scroll/y");

            var pos = new Vector2(Mathf.Round(Screen.width * 0.4f), Mathf.Round(Screen.height * 0.5f));
            Move(m_Mouse.position, pos, queueEventOnly: true);
            yield return null;
            Assert.AreEqual(pos.x, point.ReadValue<Vector2>().x, 1e-3f);
            Assert.AreEqual(pos.y, point.ReadValue<Vector2>().y, 1e-3f);
            Press(m_Mouse.leftButton, queueEventOnly: true);
            yield return null;
            Assert.IsTrue(press.IsPressed(), "нажатие мыши видно в Press");
            Release(m_Mouse.leftButton, queueEventOnly: true);
            yield return null;
            Assert.IsFalse(press.IsPressed());
            yield return null;
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        [UnityTest]
        public IEnumerator DisableReleasesGameplayMapAndEnableRestoresIt()
        {
            yield return StartGame();
            yield return HireCats(1);
            var input = Game.Input;
            var map = input.GameplayMap;
            Assert.IsNotNull(map);
            var point = map.FindAction(PointerInput.PointActionName);
            CollectionAssert.Contains(InputSystem.ListEnabledActions(), point);

            // Выключение сразу освобождает личную копию карты (так Setup гасит ввод сцены, оставшейся от прошлого теста).
            input.enabled = false;
            Assert.IsNull(input.GameplayMap, "карта освобождена при выключении");
            Assert.IsFalse(map.enabled);
            CollectionAssert.DoesNotContain(InputSystem.ListEnabledActions(), point);
            yield return null;
            Assert.IsTrue(map.asset == null, "копия ассета уничтожена");

            // Включение создаёт новую копию, привязанную к той же мыши: жесты снова работают.
            input.enabled = true;
            var map2 = input.GameplayMap;
            Assert.IsNotNull(map2, "карта создана заново");
            Assert.IsTrue(map2.enabled);
            Assert.IsTrue(BoundTo(map2.FindAction(PointerInput.PressActionName), m_Mouse.press));
            var cat = Game.Session.Mine.Cats[0];
            yield return Focus(Game.Board.CellCenter(cat.Cell));
            var catPos = CatPoint(cat.Id);
            AssertOnBoard(catPos, "котик");
            yield return MouseClick(catPos);
            Assert.AreEqual(cat.Id, Game.SelectedCatId, "после повторного включения тап выбирает котика");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        [UnityTest]
        public IEnumerator DynamiteHoldOnCellDoesNotAutoScroll()
        {
            yield return StartGame();
            // Глубокая карта и камера посередине: автопрокрутке есть куда ехать в обе стороны.
            Game.SwitchSlot(SaveSlots.Debug, true, 10);
            yield return PlayModeUtil.WaitReal(0.3f);
            var s = Game.Session;
            s.DebugAddDynamite(2);
            int stock = s.Player.Dynamite;
            Assert.Greater(stock, 0);
            Game.Rig.FocusOn(s.Layout.Depth * 0.5f);
            Game.ClearSelection();
            yield return PlayModeUtil.WaitReal(0.2f);
            float z0 = Game.Rig.FocusZ;

            var cell = PlayModeUtil.FindButton(Game.Hud, "Dynamite");
            Assert.IsNotNull(cell, "ячейка динамита");
            Vector2 cellPos = ButtonCenter(cell);
            Assert.IsTrue(OverUi(cellPos));
            Assert.Less(cellPos.y, Screen.height * Game.Hud.BottomInset, "ячейка ниже нижнего края поля");

            // Нажать и держать на ячейке: перенос начат, поле стоит (раньше ехало вниз на 12 строк/с).
            Move(m_Mouse.position, cellPos, queueEventOnly: true);
            yield return null;
            Press(m_Mouse.leftButton, queueEventOnly: true);
            yield return null;
            yield return PlayModeUtil.WaitReal(0.5f);
            Assert.IsTrue(Game.Input.IsDraggingDynamite, "нажатие на ячейку начинает перенос динамита");
            Assert.AreEqual(z0, Game.Rig.FocusZ, 1e-4f, "пока палец на ячейке, автопрокрутки нет");

            // Палец дошёл до поля и вернулся к нижнему краю: дальше автопрокрутка как при переносе котика.
            var field = new Vector2(cellPos.x, Screen.height * 0.55f);
            for (int i = 1; i <= 6; i++)
            {
                Move(m_Mouse.position, Vector2.Lerp(cellPos, field, i / 6f), queueEventOnly: true);
                yield return null;
            }
            yield return null;
            float z1 = Game.Rig.FocusZ;
            Move(m_Mouse.position, cellPos, queueEventOnly: true);
            yield return null;
            yield return PlayModeUtil.WaitReal(0.3f);
            Assert.Less(Game.Rig.FocusZ, z1 - 0.5f, "после входа на поле нижний край прокручивает поле вниз");

            // Отпускание над HUD завершает перенос без броска.
            Release(m_Mouse.leftButton, queueEventOnly: true);
            yield return null;
            yield return null;
            Assert.IsFalse(Game.Input.IsDraggingDynamite);
            Assert.AreEqual(stock, s.Player.Dynamite, "сброс над HUD не тратит динамит");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }
    }
}
