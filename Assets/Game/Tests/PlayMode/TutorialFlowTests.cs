using System;
using System.Collections;
using System.Collections.Generic;
using CatMiners.Core;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using CatMiners.Runtime;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>
    /// Обучение zone1 по образцу Gold &amp; Goblins (ТЗ 13.4) в настоящей игре: новый слот Main, часы остановлены и идут только
    /// по <see cref="ManualClock.Advance"/>. Пять шагов проходятся как игроком: 1) «Нанять» (экран затемнён вокруг кнопки, тап
    /// проходит) → 2) тап по бочке найма → 3) котик переносится на камень, к которому ведёт жест лапки → 4) камень разбит,
    /// тап по кучке добычи, монетки летят к счётчику → 5) второй наём. На каждом шаге — реплика наставника из Loc (портрет
    /// в рамке и пузырь), лапка и рамка фокуса у цели (тап в этой точке выбирает именно её) и снимок
    /// Docs/Reports/screenshots/tutorial_stepN.png в 1080×1920. Дальше, как в эталоне: 6) тап по второй бочке →
    /// 7) жест переноса котика на котика того же уровня, объединение → 8) объединённый котик переносится на камень →
    /// 9) тап по кузнице и «+1» в её панели (улучшение за монеты). В журнале нет ошибок.
    /// </summary>
    public class TutorialFlowTests
    {
        /// <summary>Цикл жеста переноса TutorialHint: зависание 0,25 + нажатие 0,2 + ведение 0,9 + отпускание 0,2 + пауза 0,55 с.</summary>
        const float DragCycle = 2.1f;
        /// <summary>Шаг игрового времени за кадр: меньше 1 с, иначе кадр считается офлайном (GameController.MaxFrameSeconds).</summary>
        const double FrameSeconds = 0.5;

        static readonly string[] s_Zone1Steps = { "t1", "t1b", "t2", "t2b", "t3" };
        /// <summary>Шаги zone1, которые засчитываются переходом в zone2 без действий (t4 и t5 — только настоящим действием).</summary>
        static readonly string[] s_Zone1StateSteps = { "t3b", "t4b" };

        string m_SaveDir;
        LogWatcher m_Log;
        ManualClock m_Clock;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_SaveDir = PlayModeUtil.UseTempSaveDir();
            CameraRig.OverrideAspect = 1080f / 1920f;
            m_Clock = new ManualClock(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 100.0);
            GameController.ClockOverride = m_Clock;
            m_Log = new LogWatcher();
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            m_Log.Dispose();
            CameraRig.OverrideAspect = 0f;
            GameController.ClockOverride = null;
            PlayModeUtil.DropTempSaveDir(m_SaveDir);
            yield break;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Zone1StepsLikeAPlayer()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            Assert.IsNotNull(gc, "GameBootstrap не создал контроллер");
            Assert.AreSame(m_Clock, gc.Clock, "контроллер взял внедрённые часы");
            var s = gc.Session;
            Assert.AreEqual(SaveSlots.Main, s.State.Slot, "новая игра в слоте Main");
            Assert.AreEqual(1, s.Mine.Zone);
            Assert.IsTrue(s.BarrelHires && s.LootPickups, "в игре включены бочки найма и добыча");
            Assert.AreEqual(0, s.Mine.CatsHired);
            Assert.AreEqual(0, s.Mine.Cats.Count, "новая игра без котиков");
            foreach (var id in s_Zone1Steps) Assert.IsFalse(s.Player.TutorialDone.Contains(id), "шаг " + id + " ещё не пройден");
            Assert.IsFalse(gc.Hud.AnyWindowOpen, "новая игра без окон");
            var hint = gc.Hud.Hint;
            Assert.IsNotNull(hint, "HUD создал подсказку обучения");
            var hire = PlayModeUtil.FindButton(gc.Hud, "Hire");
            Assert.IsNotNull(hire, "кнопка «Нанять»");

            // ── Шаг 1: «Потратьте золото…» — лапка на «Нанять», вокруг кнопки затемнение (только визуальное).
            Assert.AreEqual(CommandResult.Ok, s.CanHire(), "первый наём по карману");
            yield return WaitHint(gc, "tutorial.hint.hire");
            AssertMentorAndPointer(hint, "шаг 1");
            Assert.IsTrue(hint.Dim, "шаг 1: экран затемнён вокруг «Нанять»");
            Assert.IsTrue(hint.Rect.Find("dim0").gameObject.activeSelf, "шаг 1: затемнение показано");
            AssertPointsAtButton(hint, hire, "шаг 1");
            AssertHintLetsTapsThrough(gc, hire);
            yield return Shot(gc, 1);
            Assert.IsTrue(hire.interactable, "«Нанять» доступна");
            var gold0 = s.Mine.Gold;
            var price = s.HirePrice();
            int level = s.HireLevel();
            PlayModeUtil.Click(hire);
            Assert.AreEqual(1, s.Mine.CatsHired, "кнопка «Нанять» наняла котика");
            Assert.IsTrue(s.Mine.Gold == gold0 - price, "списана цена найма " + price);
            Assert.AreEqual(0, s.Mine.Cats.Count, "котик приезжает в бочке, а не сразу на поле");
            var barrels = PlayModeUtil.HireBarrels(s);
            Assert.AreEqual(1, barrels.Count, "на поле бочка найма");
            var barrel = barrels[0];
            Assert.AreEqual(level, barrel.CatLevel, "в бочке котик уровня найма");
            yield return null;
            Assert.IsTrue(gc.Board.ContainerViewOf(barrel.Id) != null, "вид бочки на поле");
            // Бочку выстреливает пушка у кузницы (ТЗ 7.2): пока она летит, подсказок нет — лапка не показывает на пустую клетку.
            Assert.IsTrue(gc.Board.IsFlying(barrel.Id), "бочка вылетела из пушки");
            float flightStart = Time.realtimeSinceStartup;
            bool hiddenInFlight = false;
            while (gc.Board.IsFlying(barrel.Id) && Time.realtimeSinceStartup - flightStart < 2f)
            {
                Assert.AreNotEqual(Loc.T("tutorial.hint.open_barrel"), gc.Tutorial.CurrentHint, "лапка не показывает на бочку в полёте");
                if (gc.Tutorial.CurrentHint == null && !gc.Hud.Hint.Visible) hiddenInFlight = true;
                yield return null;
            }
            Assert.IsFalse(gc.Board.IsFlying(barrel.Id), "бочка приземлилась");
            Assert.IsTrue(hiddenInFlight, "пока бочка летела, подсказка была скрыта");

            // ── Шаг 2: «Нажмите на бочку…» — лапка на бочке; тап открывает её, котик появляется.
            yield return WaitHint(gc, "tutorial.hint.open_barrel");
            Assert.IsTrue(s.Player.TutorialDone.Contains("t1"), "шаг 1 засчитан");
            Assert.IsFalse(hint.Dim, "шаг 2 без затемнения");
            AssertMentorAndPointer(hint, "шаг 2");
            // Бочка появляется с «попом» масштаба: коллайдер растёт вместе с ней.
            yield return PlayModeUtil.WaitReal(0.5f);
            var pick = PickAt(gc, PointerCenter(hint));
            Assert.IsNotNull(pick, "шаг 2: под лапкой нет ничего, что выбирает тап");
            Assert.AreEqual(Pickable.PickKind.Container, pick.Kind, "шаг 2: лапка показывает на бочку");
            Assert.AreEqual(barrel.Id, pick.Id, "шаг 2: лапка показывает на бочку найма");
            yield return Shot(gc, 2);
            gc.OpenContainer(pick.Id);
            Assert.AreEqual(0, PlayModeUtil.HireBarrels(s).Count, "бочка открыта");
            Assert.AreEqual(1, s.Mine.Cats.Count, "из бочки вышел котик");
            var cat = s.Mine.Cats[0];
            Assert.AreEqual(barrel.Cell, cat.Cell, "котик в клетке бочки");
            Assert.AreEqual(level, cat.Level, "котик уровня найма");
            Assert.AreEqual(-1, cat.TargetIndex, "котик из бочки свободен");
            Assert.IsTrue(s.Player.TutorialDone.Contains("t1b"), "шаг 2 засчитан открытием бочки");

            // ── Шаг 3: «Перетащите котика на камень…» — жест лапки от котика к ближайшему камню; перенос назначает котика.
            yield return WaitHint(gc, "tutorial.hint.drag_to_rock");
            // Жест (TutorialHint.ShowDrag) начался в этом кадре: Evaluate и показ — в Update контроллера до тестовой корутины.
            float dragShownAt = Time.unscaledTime;
            AssertMentorAndPointer(hint, "шаг 3");
            Assert.IsFalse(hint.Dim, "шаг 3 без затемнения");
            // Котик появляется с анимацией масштаба; коллайдеры — в полный размер.
            yield return PlayModeUtil.WaitReal(0.8f);
            var rockPick = PickAt(gc, PointerCenter(hint));
            Assert.IsNotNull(rockPick, "шаг 3: под рамкой фокуса нет ничего, что выбирает тап");
            Assert.AreEqual(Pickable.PickKind.Object, rockPick.Kind, "шаг 3: жест ведёт к объекту поля");
            int rockOrd = rockPick.Id;
            var rock = s.Layout.Objects[rockOrd];
            Assert.AreEqual(ObjectKind.Rock, rock.Kind, "шаг 3: жест ведёт к камню, а не к " + rock.Kind);
            Assert.IsTrue(s.IsTargetAvailable(rockOrd) && s.AcceptsCat(rockOrd, cat.Id), "шаг 3: камень " + rock.TypeId + " принимает котика");
            AssertNearestRock(gc, cat, rockOrd);
            yield return AssertDragGesture(gc, hint, cat.Cell, rockOrd);
            // Снимок — посреди ведения лапки (рендер через ~0,3 с после начала снимка).
            yield return WaitDragPhase(dragShownAt, 0.5f, 0.72f);
            yield return Shot(gc, 3);
            yield return DragCatTo(gc, cat.Id, gc.Board.ObjectCenter(rockOrd));
            Assert.AreEqual(rock.Index, s.FindCat(cat.Id).TargetIndex, "перенос на камень назначил котика");
            yield return WaitHintGone(gc, "tutorial.hint.drag_to_rock");
            Assert.IsTrue(s.Player.TutorialDone.Contains("t2"), "шаг 3 засчитан назначением");

            // ── Шаг 4: время идёт, камень разбит — награда выпадает добычей; «Нажмите, чтобы собрать добычу» — тап по кучке.
            double passed = 0;
            while (s.Mine.Objects[rockOrd].Status == ObjectStatus.Intact && passed < 180)
            {
                m_Clock.Advance(FrameSeconds);
                passed += FrameSeconds;
                yield return null;
            }
            Assert.AreEqual(ObjectStatus.Destroyed, s.Mine.Objects[rockOrd].Status, "котик разбил камень за " + passed + " с");
            TestContext.WriteLine("zone1: камень " + rock.TypeId + " разбит за " + passed + " с игрового времени");
            Assert.AreEqual(1, s.Mine.Loot.Count, "награда камня " + rock.TypeId + " выпала добычей");
            var loot = s.Mine.Loot[0];
            Assert.AreEqual(rock.Index, loot.SourceIndex, "добыча этого камня");
            Assert.AreEqual(rock.Index, loot.Cell, "добыча лежит в клетке якоря камня");
            string key = loot.Currency + ":rock";
            Assert.IsFalse(PlayModeUtil.Ledger(s, key).IsPositive, "награда камня до сбора не начислена");
            Assert.IsTrue(gc.Board.LootViewOf(loot.Id) != null, "кучка добычи на поле");
            yield return WaitHint(gc, "tutorial.hint.collect_loot");
            AssertMentorAndPointer(hint, "шаг 4");
            // Кучка вылетает из камня по дуге и приземляется в клетке.
            yield return PlayModeUtil.WaitReal(0.8f);
            var lootPick = PickAt(gc, PointerCenter(hint));
            Assert.IsNotNull(lootPick, "шаг 4: под лапкой нет ничего, что выбирает тап");
            Assert.AreEqual(Pickable.PickKind.Loot, lootPick.Kind, "шаг 4: лапка показывает на добычу");
            Assert.AreEqual(loot.Id, lootPick.Id);
            yield return Shot(gc, 4);
            Assert.AreSame(loot, s.FindLoot(loot.Id), "часы стоят — добыча не собрана автосбором");
            var gold1 = s.Mine.Gold;
            gc.CollectLoot(lootPick.Id);
            Assert.IsNull(s.FindLoot(loot.Id), "тап собрал добычу");
            Assert.AreEqual(1, s.Mine.LootCollected);
            Assert.IsTrue(PlayModeUtil.Ledger(s, key) == loot.Amount, "начислена сумма добычи с ключом " + key);
            if (loot.Currency == LootState.Gold) Assert.IsTrue(s.Mine.Gold == gold1 + loot.Amount, "золото выросло на " + loot.Amount);
            Assert.IsTrue(s.Player.TutorialDone.Contains("t2b"), "шаг 4 засчитан сбором");
            yield return AssertLootFliesToCounter(gc);

            // ── Шаг 5: «Наймите второго котика» — лапка на «Нанять» (без затемнения); покупка по карману.
            yield return WaitHint(gc, "tutorial.hint.hire_second");
            AssertMentorAndPointer(hint, "шаг 5");
            Assert.IsFalse(hint.Dim, "шаг 5 без затемнения");
            AssertPointsAtButton(hint, hire, "шаг 5");
            Assert.AreEqual(CommandResult.Ok, s.CanHire(), "второй наём по карману: обучение не навязывает недоступную покупку");
            yield return Shot(gc, 5);
            Assert.IsTrue(hire.interactable, "«Нанять» доступна");
            int level2 = s.HireLevel();
            PlayModeUtil.Click(hire);
            Assert.AreEqual(2, s.Mine.CatsHired, "второй наём");
            Assert.AreEqual(1, PlayModeUtil.HireBarrels(s).Count, "второй котик тоже приехал в бочке");

            // ── Шаг 6: «Нажмите на бочку…» — новая бочка найма; тап по ней выпускает второго котика.
            yield return WaitHint(gc, "tutorial.hint.open_barrel");
            foreach (var id in s_Zone1Steps) Assert.IsTrue(s.Player.TutorialDone.Contains(id), "шаг " + id + " засчитан");
            AssertMentorAndPointer(hint, "шаг 6");
            Assert.IsFalse(hint.Dim, "шаг 6 без затемнения");
            yield return PlayModeUtil.WaitReal(0.5f);
            var barrel2 = PlayModeUtil.HireBarrels(s)[0];
            var pick2 = PickAt(gc, PointerCenter(hint));
            Assert.IsNotNull(pick2, "шаг 6: под лапкой нет ничего, что выбирает тап");
            Assert.AreEqual(Pickable.PickKind.Container, pick2.Kind, "шаг 6: лапка показывает на бочку");
            Assert.AreEqual(barrel2.Id, pick2.Id, "шаг 6: лапка на новой бочке найма");
            yield return Shot(gc, 6);
            gc.OpenContainer(pick2.Id);
            Assert.AreEqual(2, s.Mine.Cats.Count, "из второй бочки вышел котик");
            Assert.IsTrue(s.Player.TutorialDone.Contains("t3b"), "шаг 6 засчитан открытием второй бочки");
            Assert.AreEqual(cat.Level, level2, "второй котик того же уровня — пара для объединения (первые наёмы — ур. 1)");

            // ── Шаг 7: «Объедините котиков одного уровня…» — жест переноса котика на котика; отпускание объединяет пару.
            yield return WaitHint(gc, "tutorial.hint.merge");
            float mergeShownAt = Time.unscaledTime;
            AssertMentorAndPointer(hint, "шаг 7");
            Assert.IsFalse(hint.Dim, "шаг 7 без затемнения");
            StringAssert.Contains("<color=", hint.Text, "шаг 7: «одного уровня» выделено цветом, как в эталоне");
            // Получатель — свободный котик ближе к камням (меньший row): результат тоже свободен (шаг 8 отправит его на камень).
            var ca = s.Mine.Cats[0];
            var cb = s.Mine.Cats[1];
            bool aIdle = ca.TargetIndex < 0, bIdle = cb.TargetIndex < 0;
            var receiver = aIdle != bIdle ? (aIdle ? ca : cb) : (s.Layout.Row(ca.Cell) <= s.Layout.Row(cb.Cell) ? ca : cb);
            var dragged = ReferenceEquals(receiver, ca) ? cb : ca;
            yield return PlayModeUtil.WaitReal(0.8f);
            var receiverPick = PickAt(gc, PointerCenter(hint));
            Assert.IsNotNull(receiverPick, "шаг 7: под рамкой фокуса нет ничего, что выбирает тап");
            Assert.AreEqual(Pickable.PickKind.Cat, receiverPick.Kind, "шаг 7: жест ведёт к котику");
            Assert.AreEqual(receiver.Id, receiverPick.Id, "шаг 7: рамка фокуса на котике-получателе");
            yield return WaitDragPhase(mergeShownAt, 0.5f, 0.72f);
            yield return Shot(gc, 7);
            int receiverCell = receiver.Cell;
            yield return DragCatTo(gc, dragged.Id, gc.Board.CellCenter(receiverCell));
            Assert.AreEqual(1, s.Mine.Merges, "перенос на котика того же уровня объединил пару");
            Assert.AreEqual(1, s.Mine.Cats.Count);
            var merged = s.Mine.Cats[0];
            Assert.AreEqual(level2 + 1, merged.Level, "уровень объединённого котика");
            Assert.AreEqual(receiverCell, merged.Cell, "результат — в клетке получателя");
            Assert.IsTrue(s.Player.TutorialDone.Contains("t4"), "шаг 7 засчитан объединением");

            // ── Шаг 8: «Объединённые котики копают быстрее!» — жест от объединённого котика к ближайшему камню.
            yield return WaitHint(gc, "tutorial.hint.merged_mine");
            float minedShownAt = Time.unscaledTime;
            AssertMentorAndPointer(hint, "шаг 8");
            Assert.IsFalse(hint.Dim, "шаг 8 без затемнения");
            yield return PlayModeUtil.WaitReal(0.8f);
            var rock2Pick = PickAt(gc, PointerCenter(hint));
            Assert.IsNotNull(rock2Pick, "шаг 8: под рамкой фокуса нет ничего, что выбирает тап");
            Assert.AreEqual(Pickable.PickKind.Object, rock2Pick.Kind, "шаг 8: жест ведёт к объекту поля");
            Assert.AreEqual(ObjectKind.Rock, s.Layout.Objects[rock2Pick.Id].Kind, "шаг 8: жест ведёт к камню");
            Assert.IsTrue(s.IsTargetAvailable(rock2Pick.Id) && s.AcceptsCat(rock2Pick.Id, merged.Id), "шаг 8: камень принимает котика");
            AssertNearestRock(gc, merged, rock2Pick.Id);
            yield return WaitDragPhase(minedShownAt, 0.5f, 0.72f);
            yield return Shot(gc, 8);
            yield return DragCatTo(gc, merged.Id, gc.Board.ObjectCenter(rock2Pick.Id));
            Assert.AreEqual(s.Layout.Objects[rock2Pick.Id].Index, s.FindCat(merged.Id).TargetIndex, "объединённый котик назначен на камень");
            yield return WaitHintGone(gc, "tutorial.hint.merged_mine");
            Assert.IsTrue(s.Player.TutorialDone.Contains("t4b"), "шаг 8 засчитан назначением объединённого котика");

            // ── Шаг 9: «Улучшите кузницу…» — лапка на кузнице; тап открывает её панель; «Потратьте монеты…» — лапка на «+1».
            int cartOrd = s.Layout.Cart.Ordinal;
            int forgeLevel = s.Mine.Objects[cartOrd].Gen.Level;
            Assert.IsTrue(s.Mine.Gold >= s.UpgradeCost(cartOrd, forgeLevel), "улучшение кузницы по карману: обучение не навязывает недоступную покупку");
            yield return WaitHint(gc, "tutorial.hint.upgrade_forge");
            AssertMentorAndPointer(hint, "шаг 9");
            yield return PlayModeUtil.WaitReal(0.3f);
            var forgePick = PickAt(gc, PointerCenter(hint));
            Assert.IsNotNull(forgePick, "шаг 9: под лапкой нет ничего, что выбирает тап");
            Assert.AreEqual(Pickable.PickKind.Object, forgePick.Kind, "шаг 9: лапка на объекте поля");
            Assert.AreEqual(cartOrd, forgePick.Id, "шаг 9: лапка на кузнице");
            gc.TapObject(cartOrd);
            Assert.AreEqual(cartOrd, gc.SelectedOrdinal, "тап выбрал кузницу");
            yield return WaitHint(gc, "tutorial.hint.upgrade_forge_spend");
            var plus = PlayModeUtil.FindButton(gc.Hud, "ctx0");
            Assert.IsNotNull(plus, "кнопка «+1» панели кузницы");
            Assert.IsTrue(plus.interactable, "«+1» доступна");
            AssertMentorAndPointer(hint, "шаг 9, панель");
            AssertPointsAtButton(hint, plus, "шаг 9");
            yield return Shot(gc, 9);
            PlayModeUtil.Click(plus);
            Assert.AreEqual(forgeLevel + 1, s.Mine.Objects[cartOrd].Gen.Level, "«+1» улучшила кузницу");
            Assert.IsTrue(s.Player.TutorialDone.Contains("t5"), "шаг 9 засчитан улучшением");
            yield return WaitHintGone(gc, "tutorial.hint.upgrade_forge_spend");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>
        /// Конец zone1 (ТЗ 13.4 шаги 10–11, 11.2): путь к выходу закрыт — «Расчистите путь»; путь расчищен — жест переноса
        /// котика к двери выхода; дверь разбита — «Отличная работа!» и лапка на «Следующая шахта»; окно перехода; после
        /// «Перейти» — транзакция уже записана (модель в zone2), поле заморожено, котики уходят в штольню, затем новая карта.
        /// Слот Debug: камни и дверь разбиваются инструментами (DebugCompleteTarget), первые шаги обучения отмечены пройденными.
        /// </summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator Zone1ExitDoorNextMineAndTransition()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            gc.SwitchSlot(SaveSlots.Debug, true, 1);
            yield return null;
            yield return PlayModeUtil.CloseModals(gc);
            var s = gc.Session;
            foreach (var id in new[] { "t1", "t1b", "t2", "t2b", "t3", "t3b", "t4", "t4b", "t5" }) s.Player.TutorialDone.Add(id);
            Assert.IsTrue(s.DebugSpawnCat(3), "котик уровня 3, как в эталоне");
            var cat = s.Mine.Cats[0];
            var layout = s.Layout;
            var exit = layout.Exit;
            int exitOrd = exit.Ordinal;
            var hint = gc.Hud.Hint;

            // Путь к двери закрыт камнями: подсказка — расчистить путь. Она одноразовая: ≈5 с на экране, потом не мешает.
            yield return WaitHint(gc, "tutorial.hint.clear_exit");
            yield return WaitHintGone(gc, "tutorial.hint.clear_exit", 8f);
            Assert.IsTrue(s.Player.TutorialDone.Contains("t5a"), "одноразовая подсказка «Расчистите путь» засчитана");
            yield return PlayModeUtil.WaitReal(0.6f);
            Assert.AreNotEqual(Loc.T("tutorial.hint.clear_exit"), gc.Tutorial.CurrentHint, "подсказка «Расчистите путь» не возвращается");

            // Столбец 2 от строки 4 до двери: камни разбиты — клетка (2, 1) перед выходом свободна.
            for (int row = 4; row >= 1; row--)
            {
                int ord = s.ObjectAt(layout.CellIndex(2, row));
                if (ord >= 0 && layout.Objects[ord].Kind == ObjectKind.Rock) s.DebugCompleteTarget(ord);
            }
            Assert.IsTrue(s.IsTargetAvailable(exitOrd), "дверь выхода можно бить");

            // ── Шаг 10: «Отправьте котиков разбить дверь выхода» — жест переноса котика к двери.
            yield return WaitHint(gc, "tutorial.hint.exit_door");
            float doorShownAt = Time.unscaledTime;
            AssertMentorAndPointer(hint, "шаг 10");
            yield return PlayModeUtil.WaitReal(0.8f);
            var doorPick = PickAt(gc, PointerCenter(hint));
            Assert.IsNotNull(doorPick, "шаг 10: под рамкой фокуса нет ничего, что выбирает тап");
            Assert.AreEqual(Pickable.PickKind.Object, doorPick.Kind);
            Assert.AreEqual(exitOrd, doorPick.Id, "шаг 10: жест ведёт к двери выхода");
            yield return WaitDragPhase(doorShownAt, 0.5f, 0.72f);
            yield return Shot(gc, 10);
            yield return DragCatTo(gc, cat.Id, gc.Board.ObjectCenter(exitOrd));
            Assert.AreEqual(exit.Index, s.FindCat(cat.Id).TargetIndex, "котик бьёт дверь выхода");
            yield return WaitHintGone(gc, "tutorial.hint.exit_door");
            Assert.IsTrue(s.Player.TutorialDone.Contains("t5b"), "шаг 10 засчитан назначением на выход");

            // ── Шаг 11: дверь разбита — «Отличная работа!…», лапка на «Следующая шахта».
            s.DebugCompleteTarget(exitOrd);
            Assert.AreEqual(ObjectStatus.Broken, s.Mine.Objects[exitOrd].Status);
            Assert.IsTrue(s.CanCompleteZone(out string reason), reason);
            yield return WaitHint(gc, "tutorial.hint.next_mine");
            AssertMentorAndPointer(hint, "шаг 11");
            var next = PlayModeUtil.FindButton(gc.Hud, "NextMine");
            Assert.IsNotNull(next, "кнопка «Следующая шахта»");
            Assert.IsTrue(next.gameObject.activeInHierarchy && next.interactable, "«Следующая шахта» показана");
            AssertPointsAtButton(hint, next, "шаг 11");
            yield return Shot(gc, 11);

            // Окно перехода с кнопкой «Перейти».
            PlayModeUtil.Click(next);
            yield return null;
            Assert.IsTrue(gc.Hud.ModalOpen, "окно перехода");
            Assert.IsFalse(hint.Visible, "под окном подсказка скрыта");
            yield return Shot(gc, 12);
            var go = PlayModeUtil.FindModalButton(gc.Hud, Loc.T("controller.next_mine.go"));
            Assert.IsNotNull(go, "кнопка «Перейти»");
            // Ещё трое котиков в зоне мержа: на снимке видно, как они идут к выходу по расчищенному столбцу.
            for (int i = 0; i < 3; i++) Assert.IsTrue(s.DebugSpawnCat(1 + i), "котик в зоне мержа");
            gc.Board.Sync(0f);
            var catView = gc.Board.CatViewOf(cat.Id);
            Assert.IsNotNull(catView);
            var walkers = new List<CatView>();
            foreach (var c in s.Mine.Cats) walkers.Add(gc.Board.CatViewOf(c.Id));
            PlayModeUtil.Click(go);

            // Транзакция записана сразу (11.2), поле заморожено, котики уходят в штольню.
            Assert.IsTrue(gc.InTransition, "идёт анимация перехода");
            Assert.AreEqual(2, s.Mine.Zone, "CompleteZone выполнен до анимации");
            Assert.IsTrue(gc.Board.Frozen, "старая карта заморожена на время ухода котиков");
            foreach (var w in walkers) Assert.IsTrue(w != null && w.Walking, "все котики идут к выходу");
            Assert.IsTrue(gc.Hud.InTransition, "HUD заморожен, касания ловит экран перехода");
            yield return PlayModeUtil.WaitReal(0.7f);
            yield return PlayModeUtil.CaptureUi(gc, "tutorial_step13_transition.png");

            float until = Time.realtimeSinceStartup + 10f;
            while ((gc.InTransition || gc.Hud.InTransition) && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsFalse(gc.InTransition, "анимация перехода закончилась");
            Assert.IsFalse(gc.Hud.InTransition, "экран перехода убран");
            Assert.IsFalse(gc.Board.Frozen, "новая карта построена");
            Assert.AreEqual(s.Layout.Depth, gc.Board.Depth, "поле — карта zone2");
            Assert.IsTrue(catView == null, "виды старой карты удалены");
            Assert.IsTrue(s.Player.TutorialDone.Contains("t6"), "шаг 11 засчитан переходом");
            yield return PlayModeUtil.CloseModals(gc);
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>
        /// Подсказки найма не навязывают покупку без золота (ТЗ 13.4), а шаги zone1 засчитываются по состоянию: в zone2 они
        /// пройдены сразу и подсказок zone1 нет.
        /// </summary>
        [UnityTest]
        public IEnumerator HireHintOnlyWhenAffordableAndStepsCompleteFromState()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            gc.SwitchSlot(SaveSlots.Debug, true, 1);
            yield return null;
            var s = gc.Session;
            var start = s.Mine.Gold;
            s.Mine.Gold = GameNumber.Zero;
            Assert.AreNotEqual(CommandResult.Ok, s.CanHire());
            yield return PlayModeUtil.WaitReal(0.6f);
            Assert.AreNotEqual(Loc.T("tutorial.hint.hire"), gc.Tutorial.CurrentHint, "без золота «Нанять» не предлагается");
            Assert.IsFalse(s.Player.TutorialDone.Contains("t1"), "шаг 1 не засчитан без найма");
            s.Mine.Gold = start;
            yield return WaitHint(gc, "tutorial.hint.hire");

            gc.SwitchSlot(SaveSlots.Debug, true, 2);
            yield return PlayModeUtil.WaitReal(0.6f);
            yield return PlayModeUtil.CloseModals(gc);
            foreach (var id in s_Zone1Steps) Assert.IsTrue(gc.Session.Player.TutorialDone.Contains(id), "zone2: шаг " + id + " засчитан по состоянию");
            foreach (var id in s_Zone1StateSteps) Assert.IsTrue(gc.Session.Player.TutorialDone.Contains(id), "zone2: шаг " + id + " засчитан по состоянию");
            foreach (var key in new[] { "tutorial.hint.hire", "tutorial.hint.open_barrel", "tutorial.hint.drag_to_rock", "tutorial.hint.collect_loot", "tutorial.hint.hire_second", "tutorial.hint.merged_mine" })
                Assert.AreNotEqual(Loc.T(key), gc.Tutorial.CurrentHint, "zone2: подсказки zone1 нет");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>
        /// Подсказки «Расчистите шахту» (zone2, t7a) и «Чертёж получен — расчистите шахту» (zone3, t8a) не висят на экране:
        /// показываются один раз (≈5 с), засчитываются и больше не возвращаются; подсказки к действию (активировать шахту)
        /// остаются.
        /// </summary>
        [UnityTest]
        public IEnumerator ClearHintsShowOnlyOnce()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            gc.SwitchSlot(SaveSlots.Debug, true, 2);
            yield return PlayModeUtil.WaitReal(0.3f);
            yield return PlayModeUtil.CloseModals(gc);
            var s = gc.Session;
            yield return WaitHint(gc, "tutorial.hint.clear_shaft");
            yield return WaitHintGone(gc, "tutorial.hint.clear_shaft", 8f);
            Assert.IsTrue(s.Player.TutorialDone.Contains("t7a"), "zone2: «Расчистите шахту» засчитана");
            yield return PlayModeUtil.WaitReal(0.6f);
            Assert.AreNotEqual(Loc.T("tutorial.hint.clear_shaft"), gc.Tutorial.CurrentHint, "zone2: «Расчистите шахту» не возвращается");
            s.DebugCompleteTarget(s.Layout.Shafts[0].Ordinal);
            yield return WaitHint(gc, "tutorial.hint.activate_shaft");

            gc.SwitchSlot(SaveSlots.Debug, true, 3);
            yield return PlayModeUtil.WaitReal(0.3f);
            yield return PlayModeUtil.CloseModals(gc);
            s = gc.Session;
            s.DebugGiveZoneCards();
            var shaft = s.Layout.Shafts[0];
            string blueprint = Loc.F("tutorial.hint.blueprint_ready", PresentationCatalog.ShaftName(shaft.TypeId));
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until && !(gc.Tutorial.CurrentHint == blueprint && gc.Hud.Hint.Visible)) yield return null;
            Assert.AreEqual(blueprint, gc.Tutorial.CurrentHint, "zone3: «Чертёж получен» показана");
            until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until && gc.Tutorial.CurrentHint == blueprint) yield return null;
            Assert.AreNotEqual(blueprint, gc.Tutorial.CurrentHint, "zone3: «Чертёж получен» снята");
            Assert.IsTrue(s.Player.TutorialDone.Contains("t8a"), "zone3: «Чертёж получен» засчитана");
            yield return PlayModeUtil.WaitReal(0.6f);
            Assert.AreNotEqual(blueprint, gc.Tutorial.CurrentHint, "zone3: «Чертёж получен» не возвращается");
            s.DebugCompleteTarget(shaft.Ordinal);
            yield return WaitHint(gc, "tutorial.hint.activate_automated");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        // ───────────── Помощники ─────────────

        /// <summary>Дождаться подсказки <paramref name="key"/>: обучение выбрало её текст, и она показана с тем же текстом.</summary>
        static IEnumerator WaitHint(GameController gc, string key, float seconds = 3f)
        {
            string text = Loc.T(key);
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until && !(gc.Tutorial.CurrentHint == text && gc.Hud.Hint.Visible && gc.Hud.Hint.Text == text))
                yield return null;
            Assert.AreEqual(text, gc.Tutorial.CurrentHint, "реплика наставника " + key);
            Assert.IsTrue(gc.Hud.Hint.Visible, "подсказка " + key + " видна");
            Assert.AreEqual(text, gc.Hud.Hint.Text, "текст подсказки " + key);
        }

        /// <summary>Дождаться, пока обучение уйдёт с подсказки <paramref name="key"/> (шаг засчитан).</summary>
        static IEnumerator WaitHintGone(GameController gc, string key, float seconds = 3f)
        {
            string text = Loc.T(key);
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until && gc.Tutorial.CurrentHint == text) yield return null;
            Assert.AreNotEqual(text, gc.Tutorial.CurrentHint, "подсказка " + key + " снята");
        }

        static IEnumerator Shot(GameController gc, int step)
        {
            yield return PlayModeUtil.CaptureUi(gc, "tutorial_step" + step + ".png");
            // Canvas вернулся в оверлей: раскладка подсказки — под экран.
            yield return null;
            yield return null;
        }

        /// <summary>Наставник в рамке, пузырь с текстом подсказки, лапка и рамка фокуса у цели.</summary>
        static void AssertMentorAndPointer(TutorialHint hint, string what)
        {
            var root = hint.Rect;
            var portrait = root.Find("dialog/portrait");
            Assert.IsTrue(portrait != null && portrait.gameObject.activeInHierarchy, what + ": портрет наставника (ui_portrait_frame и tutor_cat_* в атласе)");
            Assert.AreEqual("ui_portrait_frame", portrait.GetComponent<Image>().sprite.name, what + ": рамка портрета");
            var mentor = portrait.Find("mentor");
            var mentorImage = mentor != null ? mentor.GetComponent<Image>() : null;
            Assert.IsTrue(mentorImage != null && mentorImage.sprite != null && mentor.gameObject.activeInHierarchy, what + ": кот-наставник в рамке");
            StringAssert.StartsWith("tutor_cat_", mentorImage.sprite.name, what + ": кадр наставника");
            var bubble = root.Find("dialog/bubble");
            Assert.IsTrue(bubble != null && bubble.gameObject.activeInHierarchy, what + ": пузырь реплики");
            var text = bubble.GetComponentInChildren<TMP_Text>(true);
            Assert.IsNotNull(text, what + ": текст в пузыре");
            Assert.AreEqual(hint.Text, text.text, what + ": в пузыре текст подсказки");
            var hand = root.Find("hand");
            Assert.IsTrue(hand != null && hand.gameObject.activeInHierarchy, what + ": лапка у цели");
            Assert.AreEqual("icon_hand_big", hand.GetComponent<Image>().sprite.name, what + ": большая лапка");
            var focus = root.Find("focus");
            Assert.IsTrue(focus != null && focus.gameObject.activeInHierarchy, what + ": рамка фокуса у цели");
        }

        /// <summary>Центр рамки фокуса подсказки — точка, на которую показывает лапка (у переноса — куда), экранные пиксели.</summary>
        static Vector2 PointerCenter(TutorialHint hint)
        {
            var focus = (RectTransform)hint.Rect.Find("focus");
            Assert.IsTrue(focus.gameObject.activeInHierarchy, "рамка фокуса у цели");
            return ScreenCenter(focus);
        }

        static Vector2 ScreenCenter(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));

        static Rect ScreenRect(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(null, corners[0]), b = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static void AssertPointsAtButton(TutorialHint hint, Button button, string what)
        {
            var at = PointerCenter(hint);
            var rect = ScreenRect((RectTransform)button.transform);
            Assert.IsTrue(rect.Contains(at), what + ": рамка фокуса " + at + " на кнопке «" + button.name + "» " + rect);
        }

        /// <summary>
        /// Затемнение и подсказка только визуальные: тап по «Нанять» доходит до кнопки, тап по полю — до поля; ни один элемент
        /// подсказки не ловит лучи UI.
        /// </summary>
        static void AssertHintLetsTapsThrough(GameController gc, Button hire)
        {
            var root = gc.Hud.Hint.Rect;
            var es = EventSystem.current;
            var hits = new List<RaycastResult>();
            es.RaycastAll(new PointerEventData(es) { position = ScreenCenter((RectTransform)hire.transform) }, hits);
            Assert.Greater(hits.Count, 0, "луч UI в центре «Нанять»");
            Assert.IsTrue(hits[0].gameObject.transform.IsChildOf(hire.transform), "тап по «Нанять» под затемнением доходит до кнопки, а не до " + hits[0].gameObject.name);
            foreach (var h in hits) Assert.IsFalse(h.gameObject.transform.IsChildOf(root), "подсказка ловит луч UI: " + h.gameObject.name);
            var field = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            hits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = field }, hits);
            foreach (var h in hits) Assert.IsFalse(h.gameObject.transform.IsChildOf(root), "затемнение ловит тап по полю: " + h.gameObject.name);
        }

        /// <summary>
        /// Что выбрал бы тап в экранной точке — тот же порядок, что у PointerInput.Tap: добыча, пузырь статуса, контейнер, котик,
        /// объект; при равенстве — ближе к камере. null — под точкой ничего.
        /// </summary>
        static Pickable PickAt(GameController gc, Vector2 screen)
        {
            var hits = Physics.RaycastAll(gc.Rig.Cam.ScreenPointToRay(screen), 200f);
            Pickable best = null;
            float bestDist = float.MaxValue;
            int bestPriority = int.MaxValue;
            foreach (var h in hits)
            {
                var p = h.collider.GetComponent<Pickable>();
                if (p == null) continue;
                int priority = p.Kind == Pickable.PickKind.Loot ? -2
                    : (p.Bubble ? -1 : (p.Kind == Pickable.PickKind.Container ? 0 : (p.Kind == Pickable.PickKind.Cat ? 1 : 2)));
                if (priority < bestPriority || (priority == bestPriority && h.distance < bestDist))
                {
                    best = p;
                    bestDist = h.distance;
                    bestPriority = priority;
                }
            }
            return best;
        }

        /// <summary>Камень жеста — ближайший к котику из тех, что его принимают (расстояние до центра footprint).</summary>
        static void AssertNearestRock(GameController gc, CatState cat, int rockOrd)
        {
            var s = gc.Session;
            var from = gc.Board.CellCenter(cat.Cell);
            float chosen = Vector3.Distance(from, gc.Board.ObjectCenter(rockOrd)), best = float.MaxValue;
            string bestId = null;
            for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
            {
                var o = s.Layout.Objects[ord];
                if (o.Kind != ObjectKind.Rock || !s.IsTargetAvailable(ord) || !s.AcceptsCat(ord, cat.Id)) continue;
                float d = Vector3.Distance(from, gc.Board.ObjectCenter(ord));
                if (d < best)
                {
                    best = d;
                    bestId = o.TypeId;
                }
            }
            Assert.LessOrEqual(chosen, best + 1e-3f, "жест ведёт к ближайшему камню (" + bestId + ", " + best + " клетки), а не к камню в " + chosen + " клетки");
        }

        /// <summary>
        /// Жест переноса за один цикл: лапка видна всё время и проходит от котика к камню — ход вдоль линии котик → камень
        /// не меньше 70% расстояния между ними на экране.
        /// </summary>
        static IEnumerator AssertDragGesture(GameController gc, TutorialHint hint, int catCell, int rockOrd)
        {
            var cam = gc.Rig.Cam;
            Vector2 a = cam.WorldToScreenPoint(gc.Board.CellCenter(catCell)), b = cam.WorldToScreenPoint(gc.Board.ObjectCenter(rockOrd));
            float len = (b - a).magnitude;
            Assert.Greater(len, 1f, "котик и камень в разных точках экрана");
            var dir = (b - a) / len;
            var hand = (RectTransform)hint.Rect.Find("hand");
            float lo = float.MaxValue, hi = float.MinValue;
            float until = Time.realtimeSinceStartup + DragCycle + 0.3f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                Assert.IsTrue(hand.gameObject.activeInHierarchy, "лапка видна весь жест");
                float p = Vector2.Dot((Vector2)hand.position - a, dir);
                lo = Mathf.Min(lo, p);
                hi = Mathf.Max(hi, p);
            }
            Assert.GreaterOrEqual(hi - lo, 0.7f * len, "лапка ведёт от котика к камню: ход " + (hi - lo) + " из " + len + " px");
        }

        /// <summary>Дождаться фазы цикла жеста переноса в [from, to] с (только для кадра снимка; без проверок).</summary>
        static IEnumerator WaitDragPhase(float shownAt, float from, float to)
        {
            float until = Time.realtimeSinceStartup + DragCycle * 2f;
            while (Time.realtimeSinceStartup < until)
            {
                float t = Mathf.Repeat(Time.unscaledTime - shownAt, DragCycle);
                if (t >= from && t <= to) yield break;
                yield return null;
            }
        }

        /// <summary>Перенос как у PointerInput: взять котика, провести над полем и отпустить в точке мира <paramref name="world"/>.</summary>
        static IEnumerator DragCatTo(GameController gc, int catId, Vector3 world)
        {
            var start = gc.Board.CellCenter(gc.Session.FindCat(catId).Cell);
            gc.BeginCatDrag(catId);
            yield return null;
            for (int i = 1; i <= 4; i++)
            {
                gc.UpdateCatDrag(Vector3.Lerp(start, world, i / 4f));
                yield return null;
            }
            var g = gc.Board.WorldToGrid(world);
            gc.DropCat(catId, g.x, g.y);
            yield return null;
        }

        /// <summary>После сбора иконки валюты летят к счётчику HUD (слой SafeArea/LootFly) и за ~1 с долетают.</summary>
        static IEnumerator AssertLootFliesToCounter(GameController gc)
        {
            var layer = gc.Hud.transform.Find("SafeArea/LootFly");
            Assert.IsNotNull(layer, "слой полёта добычи HUD");
            int most = 0;
            float until = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                most = Mathf.Max(most, ActiveChildren(layer));
            }
            Assert.Greater(most, 0, "монетки летят к счётчику");
            yield return PlayModeUtil.WaitReal(1.2f);
            Assert.AreEqual(0, ActiveChildren(layer), "монетки долетели");
        }

        static int ActiveChildren(Transform t)
        {
            int n = 0;
            for (int i = 0; i < t.childCount; i++) if (t.GetChild(i).gameObject.activeSelf) n++;
            return n;
        }
    }
}
