using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CatMiners.Core;
using CatMiners.Core.Grid;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using CatMiners.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CatMiners.Tests
{
    /// <summary>
    /// PlayMode-проверки сцены CoreGame (ТЗ 18, матрица приёмки): запуск без исключений, кнопки HUD меняют симуляцию,
    /// состояние и камера переживают перезапуск, разрыв кадра — офлайн ровно один раз, финальный экран при повторном запуске,
    /// очередь неоткрытых сундуков, поздние зоны в слоте Debug.
    /// Снимает скриншоты 1080×1920 в каталог отчётов. Сохранения пишутся во временный каталог.
    /// </summary>
    public class CoreGameSmokeTests
    {
        const long S = GameSession.TicksPerSecond;
        string m_SaveDir;
        LogWatcher m_Log;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_SaveDir = PlayModeUtil.UseTempSaveDir();
            CameraRig.OverrideAspect = 1080f / 1920f;
            m_Log = new LogWatcher();
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            m_Log.Dispose();
            CameraRig.OverrideAspect = 0f;
            GameController.ClockOverride = null;
            if (GameBootstrap.Controller != null) GameBootstrap.Controller.SimSpeed = 1;
            PlayModeUtil.DropTempSaveDir(m_SaveDir);
            yield break;
        }

        [UnityTest]
        public IEnumerator CoreGameRunsAndPlays()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            Assert.IsNotNull(gc, "GameBootstrap не создал контроллер");
            Assert.IsNotNull(gc.Session, "сессия не создана");
            Assert.AreEqual(1, gc.Session.Mine.Zone);
            Assert.AreEqual(SaveSlots.Main, gc.Session.State.Slot);

            // Те же команды, что вызывает UI. В игре нанятый котик приезжает в бочке и появляется по тапу по ней.
            Assert.IsTrue(gc.Session.BarrelHires && gc.Session.LootPickups, "в игре включены бочки найма и добыча");
            var goldBefore = gc.Session.Mine.Gold;
            gc.Hire();
            gc.Hire();
            Assert.AreEqual(2, PlayModeUtil.HireBarrels(gc.Session).Count, "наём через UI-команду ставит бочки");
            Assert.AreEqual(0, gc.Session.Mine.Cats.Count, "котики ещё в бочках");
            Assert.AreEqual(2, PlayModeUtil.OpenHireBarrels(gc));
            Assert.AreEqual(2, gc.Session.Mine.Cats.Count, "котики вышли из бочек");
            Assert.IsTrue(gc.Session.Mine.Gold < goldBefore);
            gc.DropCat(gc.Session.Mine.Cats[0].Id, 2.5, 4.5);
            gc.DropCat(gc.Session.Mine.Cats[1].Id, 3.5, 4.5);
            Assert.IsTrue(gc.Session.Mine.Cats[0].TargetIndex >= 0, "назначение на камень");

            // ТЗ 15.2, 17: в слоте Main ускорение не действует, симуляция идёт ×1.
            long mainT0 = gc.Session.Player.PlayerTimeTicks;
            float real0 = Time.realtimeSinceStartup;
            gc.SimSpeed = 20;
            yield return PlayModeUtil.WaitReal(1f);
            long mainTicks = gc.Session.Player.PlayerTimeTicks - mainT0;
            float realPassed = Time.realtimeSinceStartup - real0;
            gc.SimSpeed = 1;
            Assert.Less(mainTicks, (long)((realPassed + 1.5f) * S), "ускорение не действует в слоте Main");

            // ТЗ 18: 10 с на ×20 без ошибок в журнале — в слоте Debug, где ускорение разрешено.
            gc.SwitchSlot(SaveSlots.Debug, true, 1);
            yield return null;
            var s = gc.Session;
            Assert.AreEqual(SaveSlots.Debug, s.State.Slot);
            Assert.AreEqual(1, s.Mine.Zone);
            gc.Hire();
            gc.Hire();
            Assert.AreEqual(2, PlayModeUtil.OpenHireBarrels(gc), "наём в слоте Debug ставит бочки");
            Assert.AreEqual(2, s.Mine.Cats.Count, "наём в слоте Debug");
            gc.DropCat(s.Mine.Cats[0].Id, 2.5, 4.5);
            gc.DropCat(s.Mine.Cats[1].Id, 3.5, 4.5);
            Assert.IsTrue(s.Mine.Cats[0].TargetIndex >= 0, "назначение на камень в слоте Debug");
            long debugT0 = s.Player.PlayerTimeTicks;
            gc.SimSpeed = 20;
            yield return PlayModeUtil.WaitReal(10f);
            gc.SimSpeed = 1;
            Assert.Greater(s.Mine.RocksDestroyed, 0, "камни разрушаются в реальном цикле кадров");
            Assert.Greater(s.Player.PlayerTimeTicks - debugT0, 20 * S, "×20 ускоряет симуляцию в слоте Debug");
            yield return PlayModeUtil.WaitReal(1f);
            PlayModeUtil.Capture(gc, "zone1.png");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        [UnityTest]
        public IEnumerator HudButtonsChangeSimulation()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            var s = gc.Session;

            var hire = PlayModeUtil.FindButton(gc.Hud, "Hire");
            Assert.IsNotNull(hire, "кнопка найма");
            yield return PlayModeUtil.WaitReal(0.2f);
            Assert.IsTrue(hire.interactable);
            var goldBefore = s.Mine.Gold;
            PlayModeUtil.Click(hire);
            yield return null;
            Assert.AreEqual(1, s.Mine.CatsHired, "кнопка «Нанять» наняла котика");
            Assert.IsTrue(s.Mine.Gold < goldBefore, "кнопка «Нанять» списала золото");
            var barrels = PlayModeUtil.HireBarrels(s);
            Assert.AreEqual(1, barrels.Count, "нанятый котик приехал в бочке");
            Assert.AreEqual(0, s.Mine.Cats.Count, "до тапа по бочке котика на поле нет");
            yield return null;
            Assert.IsTrue(gc.Board.ContainerViewOf(barrels[0].Id) != null, "бочка на поле");
            gc.OpenContainer(barrels[0].Id);
            Assert.AreEqual(1, s.Mine.Cats.Count, "тап по бочке выпустил котика");
            Assert.AreEqual(barrels[0].Cell, s.Mine.Cats[0].Cell, "котик в клетке бочки");

            // Улучшение кузницы из контекстной панели. Золото копится на ×100 — ускорение есть только в слоте Debug (ТЗ 15.2).
            gc.SwitchSlot(SaveSlots.Debug, true, 1);
            yield return null;
            s = gc.Session;
            Assert.AreEqual(SaveSlots.Debug, s.State.Slot);
            int cart = s.Layout.Cart.Ordinal;
            gc.TapObject(cart);
            var cost = s.UpgradeCost(cart, s.Mine.Objects[cart].Gen.Level);
            gc.SimSpeed = 100;
            float until = Time.realtimeSinceStartup + 15f;
            while (s.Mine.Gold < cost && Time.realtimeSinceStartup < until) yield return null;
            gc.SimSpeed = 1;
            Assert.IsTrue(s.Mine.Gold >= cost, "кузница накопила золото на улучшение");
            yield return PlayModeUtil.WaitReal(0.25f);
            var plusOne = PlayModeUtil.FindButton(gc.Hud, "ctx0");
            Assert.IsNotNull(plusOne);
            Assert.IsTrue(plusOne.gameObject.activeInHierarchy && plusOne.interactable, "кнопка «+1» доступна");
            int level = s.Mine.Objects[cart].Gen.Level;
            PlayModeUtil.Click(plusOne);
            yield return null;
            Assert.AreEqual(level + 1, s.Mine.Objects[cart].Gen.Level, "кнопка «+1» улучшила кузницу");

            // Недоступная кнопка ничего не меняет.
            yield return PlayModeUtil.WaitReal(0.25f);
            if (!plusOne.interactable)
            {
                PlayModeUtil.Click(plusOne);
                yield return null;
                Assert.AreEqual(level + 1, s.Mine.Objects[cart].Gen.Level);
            }
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        [UnityTest]
        public IEnumerator StateSurvivesSceneRestart()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            gc.Hire();
            gc.Hire();
            Assert.AreEqual(2, PlayModeUtil.OpenHireBarrels(gc), "котики приехали в бочках");
            gc.DropCat(gc.Session.Mine.Cats[0].Id, 2.5, 4.5);
            yield return PlayModeUtil.WaitReal(0.5f);
            // Позиция камеры входит в сохранение (ТЗ 12.1, 13.2); FocusOn ограничивает её границами карты.
            gc.Rig.FocusOn(gc.Session.Layout.Depth * 0.5f);
            float focus = gc.Rig.FocusZ;
            gc.SaveNow();
            var before = gc.Session;
            var cats = new List<CatState>(before.Mine.Cats);
            int hireCount = before.Mine.HireCount;
            var gold = before.Mine.Gold;
            long nextOp = before.State.NextOpId;

            // Перезапуск: сцена загружается заново, новый контроллер читает сохранение.
            yield return PlayModeUtil.LoadCoreGame();
            var gc2 = GameBootstrap.Controller;
            Assert.AreNotSame(gc, gc2);
            var after = gc2.Session;
            Assert.AreEqual(SaveSlots.Main, after.State.Slot);
            Assert.AreEqual(cats.Count, after.Mine.Cats.Count, "котики не потерялись и не задвоились");
            var ids = new HashSet<int>();
            foreach (var c in after.Mine.Cats) Assert.IsTrue(ids.Add(c.Id), "повторный ID котика " + c.Id);
            for (int i = 0; i < cats.Count; i++)
            {
                var c = after.FindCat(cats[i].Id);
                Assert.IsNotNull(c);
                Assert.AreEqual(cats[i].Level, c.Level);
                Assert.AreEqual(cats[i].Cell, c.Cell);
            }
            Assert.IsTrue(after.FindCat(cats[0].Id).TargetIndex >= 0 || after.Mine.RocksDestroyed > 0, "назначение сохранилось");
            Assert.AreEqual(hireCount, after.Mine.HireCount);
            Assert.GreaterOrEqual(after.State.NextOpId, nextOp);
            Assert.IsTrue(after.Mine.Gold >= gold, "золото не уменьшилось");
            Assert.AreEqual(focus, gc2.Rig.FocusZ, 1e-3f, "загрузка восстановила позицию камеры");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>
        /// Бочки найма и добыча (ТЗ 7.2, 10.2, 12.1): неоткрытая бочка и несобранная добыча лежат в сохранении и после перезапуска
        /// снова на поле вместе со своими видами; тап по бочке выпускает котика уровня найма в её клетке, тап по кучке начисляет
        /// ровно её сумму с ключом журнала камня. Часы остановлены: добыча не стареет и автосбором не уходит.
        /// </summary>
        [UnityTest]
        public IEnumerator HireBarrelAndLootSurviveRestart()
        {
            GameController.ClockOverride = new ManualClock(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 100.0);
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            gc.SwitchSlot(SaveSlots.Debug, true, 1);
            yield return null;
            var s = gc.Session;
            Assert.IsTrue(s.BarrelHires && s.LootPickups, "в игре включены бочки найма и добыча");
            int level = s.HireLevel();
            gc.Hire();
            var barrels = PlayModeUtil.HireBarrels(s);
            Assert.AreEqual(1, barrels.Count, "наём ставит бочку");
            Assert.AreEqual(0, s.Mine.Cats.Count, "котик ещё в бочке");
            var barrel = barrels[0];
            Assert.AreEqual(level, barrel.CatLevel, "в бочке котик уровня найма");

            // Камень с валютной наградой разрушается инструментом: награда выпадает добычей и до сбора не начисляется.
            var loot = PlayModeUtil.BreakRockForLoot(s);
            Assert.IsNotNull(loot, "в zone1 есть камень с валютной наградой");
            string key = loot.Currency + ":rock";
            Assert.IsFalse(PlayModeUtil.Ledger(s, key).IsPositive, "награда камня до сбора не начислена");
            yield return PlayModeUtil.WaitReal(0.3f);
            yield return PlayModeUtil.CloseModals(gc);
            Assert.IsFalse(gc.Hud.ModalOpen, "окна закрыты");
            Assert.IsTrue(gc.Board.LootViewOf(loot.Id) != null, "кучка добычи на поле");
            gc.SaveNow();

            // Перезапуск: Play открывает Main, затем загружается сохранённый слот Debug.
            yield return PlayModeUtil.LoadCoreGame();
            var gc2 = GameBootstrap.Controller;
            Assert.AreNotSame(gc, gc2);
            gc2.SwitchSlot(SaveSlots.Debug, false);
            yield return null;
            var s2 = gc2.Session;
            Assert.IsTrue(s2.BarrelHires && s2.LootPickups, "правила игры включены и после загрузки");
            var barrels2 = PlayModeUtil.HireBarrels(s2);
            Assert.AreEqual(1, barrels2.Count, "бочка найма пережила перезапуск");
            Assert.AreEqual(barrel.Id, barrels2[0].Id);
            Assert.AreEqual(barrel.Cell, barrels2[0].Cell);
            Assert.AreEqual(level, barrels2[0].CatLevel);
            Assert.AreEqual(0, s2.Mine.Cats.Count, "бочка не открылась сама");
            var loot2 = s2.FindLoot(loot.Id);
            Assert.IsNotNull(loot2, "добыча пережила перезапуск");
            Assert.AreEqual(loot.Cell, loot2.Cell);
            Assert.AreEqual(loot.Currency, loot2.Currency);
            Assert.AreEqual(loot.SourceIndex, loot2.SourceIndex);
            Assert.IsTrue(loot.Amount == loot2.Amount, "сумма добычи " + loot2.Amount + " ≠ " + loot.Amount);
            Assert.IsTrue(gc2.Board.ContainerViewOf(barrel.Id) != null, "вид бочки построен после загрузки");
            Assert.IsTrue(gc2.Board.LootViewOf(loot.Id) != null, "вид добычи построен после загрузки");

            // Тап по бочке и по кучке — те же команды, что у PointerInput.
            gc2.OpenContainer(barrel.Id);
            Assert.AreEqual(0, PlayModeUtil.HireBarrels(s2).Count, "бочка открыта");
            Assert.AreEqual(1, s2.Mine.Cats.Count, "из бочки вышел котик");
            Assert.AreEqual(barrel.Cell, s2.Mine.Cats[0].Cell, "котик в клетке бочки");
            Assert.AreEqual(level, s2.Mine.Cats[0].Level, "котик уровня найма");
            Assert.AreEqual(-1, s2.Mine.Cats[0].TargetIndex, "котик из бочки свободен");
            var ledger = PlayModeUtil.Ledger(s2, key);
            gc2.CollectLoot(loot.Id);
            Assert.IsNull(s2.FindLoot(loot.Id), "добыча собрана");
            Assert.AreEqual(1, s2.Mine.LootCollected);
            Assert.IsTrue(PlayModeUtil.Ledger(s2, key) == ledger + loot.Amount, "начислена сумма добычи с ключом " + key);
            yield return PlayModeUtil.WaitReal(0.8f);
            Assert.IsTrue(gc2.Board.LootViewOf(loot.Id) == null, "кучка убрана с поля");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>
        /// ТЗ 13.2, 12.1: загрузка возвращает камеру в сохранённую позицию (zone10 выше кадра, есть куда сдвинуть),
        /// новая зона начинается со стартовой площадки.
        /// </summary>
        [UnityTest]
        public IEnumerator CameraPositionRestoredOnLoad()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            gc.SwitchSlot(SaveSlots.Debug, true, 10);
            yield return null;
            float start = gc.Rig.FocusZ;
            gc.Rig.FocusOn(gc.Session.Layout.Depth);
            float focus = gc.Rig.FocusZ;
            Assert.Greater(Mathf.Abs(focus - start), 2f, "камеру можно увести от стартовой площадки");
            gc.SaveNow();
            Assert.AreEqual(focus, gc.Session.Mine.CameraFocusZ, 1e-3f, "позиция камеры записана в MineProgressState");

            // Перезапуск: Play открывает Main, затем загружается сохранённый слот Debug.
            yield return PlayModeUtil.LoadCoreGame();
            var gc2 = GameBootstrap.Controller;
            Assert.AreNotSame(gc, gc2);
            gc2.SwitchSlot(SaveSlots.Debug, false);
            yield return null;
            var s = gc2.Session;
            Assert.AreEqual(10, s.Mine.Zone);
            Assert.AreEqual(focus, gc2.Rig.FocusZ, 1e-3f, "загрузка восстановила позицию камеры");

            // Новая зона — стартовая площадка, а не позиция прошлой зоны.
            FinishZone(gc2);
            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(11, s.Mine.Zone);
            float z = gc2.Rig.FocusZ;
            // «Домашняя» позиция (ТЗ 13.2): зона мержа и кузница над нижней панелью HUD.
            Assert.AreEqual(gc2.Rig.HomeFocusZ(s), z, 1e-3f, "новая зона начинается со стартовой площадки");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>
        /// ТЗ 15.2, 12.3: часы внедряются в контроллер. Разрыв кадра обрабатывается как офлайн ровно один раз; пока UTC позади
        /// метки, даже большой монотонный разрыв прогресса не даёт, а метка не сдвигается назад.
        /// </summary>
        [UnityTest]
        public IEnumerator FrameGapIsProcessedAsOfflineOnce()
        {
            var clock = new ManualClock(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 100.0);
            GameController.ClockOverride = clock;
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            Assert.AreSame(clock, gc.Clock, "контроллер взял внедрённые часы");
            var s = gc.Session;
            yield return PlayModeUtil.WaitReal(0.3f);
            long t0 = s.Player.PlayerTimeTicks;
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(t0, s.Player.PlayerTimeTicks, "часы стоят — симуляция не идёт");

            // Кадр после паузы: прошло 5 с по обоим часам.
            clock.Advance(5.0);
            for (int i = 0; i < 3; i++) yield return null;
            long passed = s.Player.PlayerTimeTicks - t0;
            Assert.AreEqual(5 * S, passed, "промежуток учтён один раз");
            long mark = s.State.LastProcessedUtcMs;
            Assert.AreEqual(clock.UtcNowMs, mark, "метка обработки — текущее UTC");
            for (int i = 0; i < 3; i++) yield return null;
            Assert.GreaterOrEqual(s.State.LastProcessedUtcMs, mark, "метка обработки монотонна");

            // Обычный кадр до 1 с — онлайн-шаг на тот же dt.
            long t1 = s.Player.PlayerTimeTicks;
            clock.Advance(0.5);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(S / 2, s.Player.PlayerTimeTicks - t1, "кадр 0,5 с — онлайн-шаг 0,5 с");

            // UTC переведено назад за метку, монотонных 2 ч: офлайн не начисляется, сырой dt в Advance не идёт.
            long t2 = s.Player.PlayerTimeTicks;
            long mark2 = s.State.LastProcessedUtcMs;
            clock.UtcNowMs = mark2 - 60_000;
            clock.MonotonicSeconds += 7200.0;
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(t2, s.Player.PlayerTimeTicks, "пока UTC позади метки, прогресса нет");
            Assert.AreEqual(mark2, s.State.LastProcessedUtcMs, "метка не сдвигается назад");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>
        /// ТЗ 12.2: валидного файла нет — повреждённый файл переименован (*.corrupt-*), не удалён; показан диалог «Начать заново»,
        /// и до него новая игра в слот не пишется.
        /// </summary>
        [UnityTest]
        public IEnumerator CorruptedSaveOffersRestartWithoutDestroyingFiles()
        {
            string main = Path.Combine(m_SaveDir, "save_main.json");
            File.WriteAllText(main, "{ повреждённое сохранение");
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            Assert.IsNotNull(gc.Session, "новая игра создана в памяти");
            Assert.IsFalse(File.Exists(main), "повреждённый файл отложен");
            Assert.IsNotEmpty(Directory.GetFiles(m_SaveDir, "save_main.json.corrupt-*"), "повреждённый файл не удалён");
            var restart = PlayModeUtil.FindModalButton(gc.Hud, Loc.T("controller.save.restart"));
            Assert.IsNotNull(restart, "диалог «Начать заново»");
            gc.SaveNow();
            yield return null;
            Assert.IsFalse(File.Exists(main), "до выбора игрока новая игра не записывается");
            PlayModeUtil.Click(restart);
            yield return null;
            Assert.IsTrue(File.Exists(main), "после «Начать заново» новая игра записана");
            Assert.IsNotEmpty(Directory.GetFiles(m_SaveDir, "save_main.json.corrupt-*"), "отложенный файл на месте");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>ТЗ 11.4: после конца кампании следующий запуск открывает финальный экран; zone1 заново не начинается.</summary>
        [UnityTest]
        public IEnumerator CampaignEndScreenOpensOnRelaunch()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            var s = gc.Session;
            Assert.IsFalse(gc.Hud.CampaignEndVisible, "в обычной игре финального экрана нет");
            s.Player.CampaignCompleted = true;
            s.Player.HighestCompletedZone = gc.Balance.ZoneCount;
            int zone = s.Mine.Zone;
            gc.SaveNow();

            yield return PlayModeUtil.LoadCoreGame();
            var gc2 = GameBootstrap.Controller;
            Assert.AreNotSame(gc, gc2);
            Assert.AreEqual(SaveSlots.Main, gc2.Session.State.Slot);
            Assert.IsTrue(gc2.Session.Player.CampaignCompleted);
            Assert.IsTrue(gc2.Hud.CampaignEndVisible, "Play открывает финальный экран");
            Assert.AreEqual(zone, gc2.Session.Mine.Zone, "зона не сменилась, новая игра не начата");
            yield return PlayModeUtil.WaitReal(0.5f);
            Assert.IsTrue(gc2.Hud.CampaignEndVisible, "экран не закрывается сам");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>
        /// ТЗ 10.3, 11.3: определённые, но не открытые сундуки лежат в очереди PlayerProgressState и переживают запись
        /// и перезапуск, в том числе с открытой панелью; из очереди сундук уходит только по «Забрать».
        /// </summary>
        [UnityTest]
        public IEnumerator QueuedChestsSurviveRestartUntilClaimed()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            var reveals = gc.Session.Player.ChestReveals;
            reveals.Add(new ChestResult { OpId = 900001, GachaId = "GachaWooden", Source = "test", Elixir = GameNumber.FromDouble(5) });
            reveals.Add(new ChestResult { OpId = 900002, GachaId = "GachaIron", Source = "test", Elixir = GameNumber.FromDouble(7) });
            gc.SaveNow();

            yield return PlayModeUtil.LoadCoreGame();
            var gc2 = GameBootstrap.Controller;
            reveals = gc2.Session.Player.ChestReveals;
            Assert.AreEqual(2, reveals.Count, "очередь сундуков пережила перезапуск");
            Assert.AreEqual(900001, reveals[0].OpId);
            yield return PlayModeUtil.WaitReal(0.3f);
            Assert.IsNull(PlayModeUtil.FindModalButton(gc2.Hud, Loc.T("hud.chest.claim")), "сундуки из очереди ждут тапа");
            yield return CloseOfflineWindows(gc2);
            yield return PlayModeUtil.WaitReal(0.25f);
            var badge = PlayModeUtil.FindButton(gc2.Hud, "ChestsBadge");
            Assert.IsNotNull(badge, "значок очереди сундуков");
            Assert.IsTrue(badge.gameObject.activeInHierarchy, "значок «Сундуки: N» виден");
            PlayModeUtil.Click(badge);
            yield return null;
            Assert.IsTrue(gc2.Hud.ModalOpen, "тап открыл панель первого сундука");
            Assert.AreEqual(2, reveals.Count, "до «Забрать» сундук остаётся в очереди");

            // Закрытие приложения при открытой панели сундук не теряет.
            gc2.SaveNow();
            yield return PlayModeUtil.LoadCoreGame();
            var gc3 = GameBootstrap.Controller;
            reveals = gc3.Session.Player.ChestReveals;
            Assert.AreEqual(2, reveals.Count, "открытая, но не забранная панель не убрала сундук из сохранения");
            yield return PlayModeUtil.WaitReal(0.3f);
            yield return CloseOfflineWindows(gc3);
            gc3.ShowPendingChests();
            yield return null;
            var claim = PlayModeUtil.FindModalButton(gc3.Hud, Loc.T("hud.chest.claim"));
            Assert.IsNotNull(claim, "кнопка «Забрать»");
            PlayModeUtil.Click(claim);
            yield return null;
            Assert.AreEqual(1, reveals.Count, "«Забрать» убрал сундук из очереди");
            Assert.AreEqual(900002, reveals[0].OpId, "второй сундук ждёт своей очереди");
            Assert.IsNull(PlayModeUtil.FindModalButton(gc3.Hud, Loc.T("hud.chest.claim")), "следующий сундук не открывается сам");

            // «Забрать» записал очередь сразу.
            yield return PlayModeUtil.LoadCoreGame();
            var gc4 = GameBootstrap.Controller;
            reveals = gc4.Session.Player.ChestReveals;
            Assert.AreEqual(1, reveals.Count);
            Assert.AreEqual(900002, reveals[0].OpId);
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>
        /// ТЗ 17.2, функциональная проверка в слоте Debug: zone5 — первые ворота, zone10 — несколько секторов, zone50, zone100
        /// и zone144 — построение поля, UI, большие значения, переходы. zone5, zone10 и zone144 — по 60 с на ×100, zone50 и zone100 —
        /// по 25 с; ворота открываются и зона завершается (CompleteZone) через инструменты; снимок каждой зоны; после zone144 —
        /// финальный экран (ТЗ 11.4); в журнале нет исключений и ошибок.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator DebugZonesRunWithoutExceptions()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            foreach (int zone in new[] { 5, 10, 50, 100, 144 })
            {
                gc.SwitchSlot(SaveSlots.Debug, true, zone);
                yield return null;
                yield return null;
                var s = gc.Session;
                Assert.AreEqual(zone, s.Mine.Zone);
                Assert.AreEqual(SaveSlots.Debug, s.State.Slot);
                s.DebugGiveZoneCards();
                int cart = s.Layout.Cart.Ordinal;
                s.DebugAddGold(s.UpgradeCost(cart, s.Mine.Objects[cart].Gen.Level) * GameNumber.FromDouble(1000));
                int catLevel = zone >= 100 ? 40 : (zone >= 50 ? 26 : (zone >= 10 ? 12 : 8));
                for (int i = 0; i < 6; i++) s.DebugSpawnCat(catLevel);
                PlayModeUtil.AssignIdleCats(gc);
                gc.SimSpeed = 100;
                yield return PlayModeUtil.WaitReal(1f);
                PlayModeUtil.Capture(gc, "zone" + zone + ".png");

                float seconds = zone == 50 || zone == 100 ? 25f : 60f;
                float until = Time.realtimeSinceStartup + seconds;
                float next = 0f;
                while (Time.realtimeSinceStartup < until)
                {
                    if (Time.realtimeSinceStartup >= next)
                    {
                        next = Time.realtimeSinceStartup + 3f;
                        ToolsStep(gc);
                    }
                    yield return null;
                }
                gc.SimSpeed = 1;
                Assert.Greater(s.Mine.GatesOpened + (s.Layout.Gates.Count == 0 ? 1 : 0), 0, "ворота открываются");

                FinishZone(gc);
                Assert.AreEqual(CommandResult.Ok, gc.Session.CompleteZone(), "CompleteZone через инструменты");
                for (int i = 0; i < 3; i++) yield return null;
                if (zone < gc.Balance.ZoneCount) Assert.AreEqual(zone + 1, gc.Session.Mine.Zone);
                else
                {
                    Assert.IsTrue(gc.Session.Player.CampaignCompleted);
                    Assert.IsTrue(gc.Hud.CampaignEndVisible, "после zone144 — финальный экран");
                }
                yield return PlayModeUtil.WaitReal(0.5f);
            }
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>Закрыть окна «Пока вас не было»: короткий разрыв перезапуска тоже может принести новости (ТЗ 12.3, без порога).</summary>
        static IEnumerator CloseOfflineWindows(GameController gc)
        {
            for (int i = 0; i < 5; i++)
            {
                var ok = PlayModeUtil.FindModalButton(gc.Hud, Loc.T("hud.offline.ok"));
                if (ok == null) yield break;
                PlayModeUtil.Click(ok);
                yield return null;
            }
        }

        /// <summary>
        /// Шаг «игрока с инструментами»: следующие ворота разрушаются и открываются, шахты активируются, выплаты, контейнеры
        /// и добыча собираются.
        /// </summary>
        static void ToolsStep(GameController gc)
        {
            var s = gc.Session;
            foreach (var gate in s.Layout.Gates)
            {
                var st = s.Mine.Objects[gate.Ordinal];
                if (st.Status == ObjectStatus.Opened) continue;
                if (st.Status == ObjectStatus.Intact) s.DebugCompleteTarget(gate.Ordinal);
                gc.OpenGate(gate.Ordinal);
                break;
            }
            for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
            {
                var st = s.Mine.Objects[ord];
                if (s.Layout.Objects[ord].Kind == ObjectKind.Shaft && st.Status == ObjectStatus.ReadyToActivate) gc.ActivateShaft(ord);
                if (st.Gen != null && st.Gen.PayoutReady) gc.CollectPayout(ord);
            }
            foreach (var c in s.Mine.Containers.ToArray()) gc.OpenContainer(c.Id);
            // Добыча камней собирается тапом, не дожидаясь автосбора (полёт к счётчику — не больше 6 за кадр).
            PlayModeUtil.CollectAllLoot(gc);
            PlayModeUtil.AssignIdleCats(gc);
        }

        static void FinishZone(GameController gc)
        {
            var s = gc.Session;
            foreach (var gate in s.Layout.Gates)
            {
                var st = s.Mine.Objects[gate.Ordinal];
                if (st.Status == ObjectStatus.Intact) s.DebugCompleteTarget(gate.Ordinal);
                if (st.Status == ObjectStatus.Broken) gc.OpenGate(gate.Ordinal);
            }
            foreach (var shaft in s.Layout.Shafts)
            {
                var st = s.Mine.Objects[shaft.Ordinal];
                if (st.Status == ObjectStatus.Intact) s.DebugCompleteTarget(shaft.Ordinal);
                if (st.Status == ObjectStatus.ReadyToActivate) gc.ActivateShaft(shaft.Ordinal);
            }
            if (s.Mine.Objects[s.Layout.Exit.Ordinal].Status == ObjectStatus.Intact) s.DebugCompleteTarget(s.Layout.Exit.Ordinal);
            Assert.IsTrue(s.CanCompleteZone(out string reason), reason);
        }
    }
}
