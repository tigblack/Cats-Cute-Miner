using System;
using System.Collections.Generic;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Grid;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// ТЗ 17.1, группы 7 (шахта) и 8 (ворота): проверки, которых нет в SimulationTests
    /// (ShaftClearActivateManualPayoutAndAutomation, Zone2ShaftCannotBeAutomatedInNormalPlay,
    /// ClosedSectorAndObstacleRejectDrop, GateBrokenThenOpenedAppliesBonusesOnce).
    /// </summary>
    public class ShaftGateAcceptanceTests
    {
        const long S = GameSession.TicksPerSecond;

        static GameSession NewSession(int zone, string slot = SaveSlots.Debug) =>
            GameSession.NewGame(TestData.Balance, 12345UL, slot, 1_000_000L, zone);

        static int OrdAt(GameSession s, int col, int row) => s.ObjectAt(s.Layout.CellIndex(col, row));

        static string Snapshot(GameSession s) => SaveService.SerializeState(s.State);

        static GameSession Reload(GameSession s) =>
            GameSession.FromState(TestData.Balance, SaveService.DeserializeState(Snapshot(s)));

        static int Count(GameSession s, SimEventType type, string text = null) =>
            s.Events.Count(e => e.Type == type && (text == null || e.Text == text));

        static double Gold(GameSession s) => s.Mine.Gold.ToDouble();

        static GeneratorState Gen(GameSession s, ZoneObject shaft) => s.Mine.Objects[shaft.Ordinal].Gen;

        // ───────────────────────────── Группа 7. Шахта ─────────────────────────────

        /// <summary>
        /// zone3: расчистка котиком → активация → ручной буфер (один, сбор тапом, следующий цикл после сбора)
        /// → автоматизация citrine при ca003 ур. 1 (ТЗ 8.3, 9.2, 9.4, E-02, E-03, E-18, 12.3).
        /// </summary>
        [Test]
        public void Zone3CitrineClearedByCatThenManualBufferThenAutomatedByCa003Level1()
        {
            var s = NewSession(3);
            var citrine = s.Layout.Shafts.Single(x => x.TypeId == "citrine");
            Assert.AreEqual(2, citrine.Level, "уровень расчистки из токена s:citrine:2");
            // HP расчистки = MiningTargetHealths.citrine[2 − 1] = 225 (JSON).
            Assert.AreEqual(225.0, s.Mine.Objects[citrine.Ordinal].Hp.ToDouble());

            // Все рабочие клетки шахты закрыты камнями и препятствиями — освобождаем (4, 3).
            int rock = OrdAt(s, 4, 3);
            Assert.AreEqual(ObjectKind.Rock, s.Layout.Objects[rock].Kind);
            s.DebugCompleteTarget(rock);
            Assert.IsTrue(s.DebugSpawnCat(1));
            var cat = s.Mine.Cats.Single();
            var drop = s.DropCat(cat.Id, citrine.Col + 0.5, citrine.Row - 1 + 0.5);
            Assert.AreEqual(CommandResult.Ok, drop.Result, "отпускание на footprint шахты Blocked");
            Assert.AreEqual(s.Layout.CellIndex(4, 3), cat.Cell, "единственная свободная рабочая клетка");
            Assert.AreEqual(citrine.Index, cat.TargetIndex);

            // E-02: котик L1 наносит 1 урон раз в 1 с → 225 HP ровно за 225 с.
            s.Advance(225 * S - 1);
            Assert.AreEqual(ObjectStatus.Intact, s.Mine.Objects[citrine.Ordinal].Status, "Blocked до последнего удара");
            s.Advance(1);
            Assert.AreEqual(ObjectStatus.ReadyToActivate, s.Mine.Objects[citrine.Ordinal].Status);
            Assert.AreEqual(-1, cat.TargetIndex, "после расчистки котик в Idle и к шахте не привязан");
            Assert.IsFalse(s.IsTargetAvailable(citrine.Ordinal), "расчищенная шахта больше не цель");

            // Активация только онлайн (12.3): офлайн шахта остаётся ReadyToActivate.
            OfflineProgress.Apply(s, s.State.LastProcessedUtcMs + 3600L * 1000);
            Assert.AreEqual(ObjectStatus.ReadyToActivate, s.Mine.Objects[citrine.Ordinal].Status, "офлайн не активирует");
            Assert.IsNull(Gen(s, citrine), "производства до активации нет");

            Assert.AreEqual(0, s.CardLevel("ca003"));
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(citrine.Ordinal));
            var gen = Gen(s, citrine);
            Assert.AreEqual(1, gen.Level, "производство начинается с уровня 1, а не с уровня расчистки");
            Assert.AreEqual(0, s.CardLevel("ca003"), "награда scg_m003_s02 содержит только ca001");
            Assert.IsFalse(gen.Automated, "без ca003 шахта ручная");
            StringAssert.Contains("ca003", s.AutomationRequirement(citrine.Ordinal), "индикатор называет карту из токена");
            // Ручные выплаты в автоматический доход не входят (9.4): только кузница L1 — 3 за 2 с.
            Assert.AreEqual(1.5, s.AutoIncomePerSecond().ToDouble(), 1e-12);

            long cycle = s.GeneratorCycleTicks(citrine.Ordinal);
            Assert.AreEqual(15 * S, cycle, "цикл citrine — GenerationDelaySecBase = 15 с");
            s.Advance(cycle - 1);
            Assert.IsFalse(gen.PayoutReady);
            s.Advance(1);
            Assert.IsTrue(gen.PayoutReady, "после цикла выплата ждёт сбора");
            Assert.AreEqual(6000.0, gen.PendingPayout.ToDouble(), 1e-9, "выход citrine L1 = 6000 × 1 (E-03)");

            // Буфер один (E-18): ещё 4 цикла — золото идёт только от кузницы (60 с = 30 циклов × 3), выплата прежняя.
            double before = Gold(s);
            s.Advance(4 * cycle);
            Assert.AreEqual(90.0, Gold(s) - before, 1e-9, "ручная шахта сама золото не зачисляет");
            Assert.IsTrue(gen.PayoutReady);
            Assert.AreEqual(6000.0, gen.PendingPayout.ToDouble(), 1e-9, "буфер не накапливает несколько выплат");

            before = Gold(s);
            Assert.AreEqual(CommandResult.Ok, s.CollectPayout(citrine.Ordinal));
            Assert.AreEqual(6000.0, Gold(s) - before, 1e-9, "сбор зачисляет ровно одну выплату");
            Assert.IsFalse(gen.PayoutReady);
            Assert.AreNotEqual(CommandResult.Ok, s.CollectPayout(citrine.Ordinal), "пустой буфер не собирается");

            // Следующий цикл начинается после сбора.
            s.Advance(cycle - 1);
            Assert.IsFalse(gen.PayoutReady);
            s.Advance(1);
            Assert.IsTrue(gen.PayoutReady);
            Assert.AreEqual(CommandResult.Ok, s.CollectPayout(citrine.Ordinal));

            // ca003 ур. 1 — ровно требование токена: автоматизация без кнопки и платы.
            s.DebugGiveZoneCards();
            Assert.AreEqual(1, s.CardLevel("ca003"));
            Assert.IsTrue(gen.Automated);
            Assert.AreEqual(1.5 + 6000.0 / 15.0, s.AutoIncomePerSecond().ToDouble(), 1e-9, "кузница + автоматизированная citrine");
            before = Gold(s);
            s.Advance(2 * cycle);
            Assert.AreEqual(2 * 6000.0 + 45.0, Gold(s) - before, 1e-9, "2 выплаты citrine зачислены сами + 15 циклов кузницы");
            Assert.IsFalse(gen.PayoutReady, "автоматизированная шахта сбора не ждёт");

            // Вернуться в ручной режим шахта не может — и после перезапуска.
            var restored = Reload(s);
            Assert.IsTrue(Gen(restored, citrine).Automated);
        }

        /// <summary>Отказ автоматизации: чужая карта (любого уровня) и нужная карта ниже требуемого уровня (ТЗ 9.2, E-07).</summary>
        [Test]
        public void WrongCardOrTooLowCardLevelDoesNotAutomate()
        {
            var s = NewSession(3);
            var citrine = s.Layout.Shafts.Single(x => x.TypeId == "citrine");
            var amethyst = s.Layout.Shafts.Single(x => x.TypeId == "amethyst");
            Assert.AreEqual("ca003", citrine.CardId);
            Assert.AreEqual(1, citrine.CardLevel);
            Assert.AreEqual("ca002", amethyst.CardId);
            Assert.AreEqual(2, amethyst.CardLevel, "zone3: s:amethyst:1:scg_m003_s01:ca002:2");

            // citrine: награда активации scg_m003_s02 — ca001 × 3 (manager кузницы), а не ca003.
            s.DebugCompleteTarget(citrine.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(citrine.Ordinal));
            Assert.AreEqual(1, s.CardLevel("ca001"));
            Assert.AreEqual(0, s.CardLevel("ca003"));
            Assert.IsFalse(Gen(s, citrine).Automated, "неверная карта ca001 не автоматизирует citrine");

            // amethyst: награда scg_m003_s01 — ca002 × 3 → ур. 1 и 2 копии; требуется ур. 2.
            s.DebugCompleteTarget(amethyst.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(amethyst.Ordinal));
            Assert.AreEqual(1, s.CardLevel("ca002"));
            Assert.AreEqual(2, s.CardStateOf("ca002").Duplicates);
            Assert.IsFalse(Gen(s, amethyst).Automated, "ca002 ур. 1 ниже требуемого ур. 2");
            Assert.IsFalse(Gen(s, citrine).Automated, "ca002 — не карта citrine");

            // Улучшение чужой карты: проверка после улучшения citrine не автоматизирует.
            s.DebugAddElixir(GameNumber.FromDouble(100));
            Assert.AreEqual(CommandResult.Ok, s.UpgradeCard("ca001"));
            Assert.AreEqual(2, s.CardLevel("ca001"));
            Assert.IsFalse(Gen(s, citrine).Automated, "ca001 ур. 2 — всё ещё неверная карта");
            Assert.IsFalse(Gen(s, amethyst).Automated);

            // Улучшение нужной карты до требуемого уровня — автоматизация сразу после улучшения (9.2).
            s.Events.Clear();
            Assert.AreEqual(CommandResult.Ok, s.UpgradeCard("ca002"));
            Assert.AreEqual(2, s.CardLevel("ca002"));
            Assert.IsTrue(Gen(s, amethyst).Automated, "ca002 ур. 2 = требованию токена");
            Assert.AreEqual(1, Count(s, SimEventType.ShaftAutomated), "автоматизирована только amethyst");
            Assert.IsFalse(Gen(s, citrine).Automated, "ca002 ур. 2 — не карта citrine");

            // Нужная карта, но ниже требования: zone5 citrine требует ca003 ур. 3.
            s.DebugGiveZoneCards();
            Assert.AreEqual(1, s.CardLevel("ca003"), "предусловие: ca003 ур. 1 из требований zone3");
            s.DebugGotoZone(5);
            var citrine5 = s.Layout.Shafts.Single(x => x.TypeId == "citrine");
            Assert.AreEqual("ca003", citrine5.CardId);
            Assert.AreEqual(3, citrine5.CardLevel, "zone5: s:citrine:3:scg_m005_s02:ca003:3");
            s.DebugCompleteTarget(citrine5.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(citrine5.Ordinal));
            Assert.AreEqual(1, s.CardLevel("ca003"), "уровень карты переносится и сундуком не растёт");
            // scg_m005_s02 даёт ca015 — manager-карту citrine, но не ту, что указана в токене.
            Assert.GreaterOrEqual(s.CardLevel("ca015"), 1);
            Assert.IsFalse(Gen(s, citrine5).Automated, "ca003 ур. 1 < 3, ca015 в токене не указана");
        }

        /// <summary>
        /// zone2 в слоте Main (без инструментов): amethyst остаётся ручной, зона всё равно проходится —
        /// это не тупик (ТЗ 17.1 п. 7, 11.2).
        /// </summary>
        [Test]
        public void Zone2AmethystStaysManualInNormalPlayAndZoneIsStillCompleted()
        {
            var s = GameSession.NewGame(TestData.Balance, 12345UL, SaveSlots.Main, 1_000_000L);
            Assert.IsFalse(s.IsDebugSlot);
            var bot = new ScenarioBot(s);
            var z1 = bot.PlayZone();
            Assert.AreEqual(BotZoneOutcome.Completed, z1.Outcome, ScenarioBot.FormatReport(new[] { z1 }));
            Assert.AreEqual(2, s.Mine.Zone);
            Assert.AreEqual(0, s.CardLevel("ca002"), "к началу zone2 карты ca002 у игрока нет");

            var z2 = bot.PlayZone();
            string report = ScenarioBot.FormatReport(new[] { z1, z2 });
            Assert.AreEqual(BotZoneOutcome.Completed, z2.Outcome, "zone2 проходится без автоматизации\n" + report);
            Assert.AreEqual(3, s.Mine.Zone);
            var info = z2.ShaftInfo.Single();
            Assert.AreEqual("amethyst", info.TypeId);
            Assert.GreaterOrEqual(info.ActivatedAt, 0.0, "шахта активирована (условие перехода)");
            Assert.Less(info.AutomatedAt, 0.0, "в zone2 без инструментов amethyst не автоматизируется\n" + report);
            Assert.AreEqual(0, z2.ShaftsAutomated);
            Assert.AreEqual(1, z2.Shafts);
        }

        /// <summary>
        /// Ручная выплата фиксируется на момент завершения цикла: открытие ворот (доход ×2) не меняет готовую выплату,
        /// следующий цикл уже ×2; офлайн ручная шахта доходит не дальше одного буфера (ТЗ 9.2, 9.4, 11.1, 12.3, E-18).
        /// </summary>
        [Test]
        public void ManualPayoutIsFixedAtCycleEndAndGateDoublesOnlyNextCycle()
        {
            var s = NewSession(5);
            var amethyst = s.Layout.Shafts.Single(x => x.TypeId == "amethyst");
            Assert.AreEqual(4, amethyst.CardLevel, "zone5: ca002 ур. 4 — автоматизации нет");
            s.DebugCompleteTarget(amethyst.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(amethyst.Ordinal));
            var gen = Gen(s, amethyst);
            Assert.IsFalse(gen.Automated);
            long cycle = s.GeneratorCycleTicks(amethyst.Ordinal);
            Assert.AreEqual(8 * S, cycle, "цикл amethyst 8 с (9.2)");
            s.Advance(cycle);
            Assert.IsTrue(gen.PayoutReady);
            Assert.AreEqual(400.0, gen.PendingPayout.ToDouble(), 1e-9, "выход amethyst L1 = 400 (E-03)");

            var gate = s.Layout.Gates.Single();
            s.DebugCompleteTarget(gate.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.OpenGate(gate.Ordinal));
            Assert.AreEqual(1, s.CardLevel("ca002"), "предусловие: ca002 ур. 1 даёт множитель ×1");
            Assert.AreEqual(400.0, gen.PendingPayout.ToDouble(), 1e-9, "готовая выплата зафиксирована до открытия ворот");
            double before = Gold(s);
            Assert.AreEqual(CommandResult.Ok, s.CollectPayout(amethyst.Ordinal));
            Assert.AreEqual(400.0, Gold(s) - before, 1e-9);

            s.Advance(cycle);
            Assert.IsTrue(gen.PayoutReady);
            Assert.AreEqual(800.0, gen.PendingPayout.ToDouble(), 1e-9, "следующий цикл — с бонусом ворот ×2");
            Assert.AreEqual(CommandResult.Ok, s.CollectPayout(amethyst.Ordinal));

            // Офлайн 8 ч: ручная шахта — до одного буфера, золото идёт только от кузницы: 14 400 циклов × 3 × 2.
            before = Gold(s);
            OfflineProgress.Apply(s, s.State.LastProcessedUtcMs + 8L * 3600 * 1000);
            Assert.IsTrue(gen.PayoutReady, "офлайн ручная шахта доходит до готового буфера");
            Assert.AreEqual(800.0, gen.PendingPayout.ToDouble(), 1e-9, "буфер один и офлайн");
            Assert.AreEqual(86400.0, Gold(s) - before, 1e-6, "ручные выплаты в офлайн-поток не подмешиваются");
        }

        // ───────────────────────────── Группа 8. Ворота ─────────────────────────────

        /// <summary>
        /// Закрытый сектор: цели в нём недопустимы, перенос на них отклоняется с возвратом назначения и фазы,
        /// динамит не тратится; ворота принимают работников только со стороны row_p + 1 (ТЗ 6.4, 6.5, 7.3, 10.6).
        /// </summary>
        [Test]
        public void ClosedSectorTargetsRejectDropAssignmentAndDynamite()
        {
            var s = NewSession(5);
            var gate = s.Layout.Gates.Single();
            Assert.AreEqual(11, gate.Row);
            // В zone5 клетки (1..5, 12) заняты камнями и препятствиями; освобождаем (3, 12).
            s.DebugCompleteTarget(OrdAt(s, 3, gate.Row + 1));

            // Рабочие клетки закрытых ворот — только строка row_p + 1 (нижний, открытый сектор), не строка row_p − 1.
            var work = new List<int>();
            s.WorkCells(gate.Ordinal, work);
            Assert.AreEqual(new[] { s.Layout.CellIndex(3, gate.Row + 1) }, work.ToArray(), "рабочая сторона ворот — row_p + 1");

            Assert.IsTrue(s.DebugSpawnCat(1));
            var cat = s.Mine.Cats.Single();
            var closedTargets = s.Layout.Objects
                .Where(o => o.IsTarget && o.Kind != ObjectKind.Gate && s.Layout.CellSector[o.Cells[0]] == 1)
                .ToList();
            Assert.IsTrue(closedTargets.Contains(s.Layout.Exit), "выход лежит в верхнем секторе");
            Assert.IsTrue(closedTargets.Any(o => o.Kind == ObjectKind.Shaft));
            Assert.IsTrue(closedTargets.Any(o => o.Kind == ObjectKind.Rock));
            foreach (var o in closedTargets)
            {
                Assert.IsFalse(s.IsTargetAvailable(o.Ordinal), o.Token + ": цель закрытого сектора недопустима");
                Assert.IsFalse(s.AcceptsCat(o.Ordinal, cat.Id), o.Token + ": цель закрытого сектора не принимает котика");
            }

            // Котик работает у камня (2, 12) открытого сектора с клетки (3, 12).
            int openRock = OrdAt(s, 2, gate.Row + 1);
            var r = s.DropCat(cat.Id, 2.5, gate.Row + 1 + 0.5);
            Assert.AreEqual(CommandResult.Ok, r.Result);
            Assert.AreEqual(s.Layout.CellIndex(3, gate.Row + 1), cat.Cell);
            Assert.AreEqual(s.Layout.Objects[openRock].Index, cat.TargetIndex);
            s.Advance(S / 2);
            int cell = cat.Cell;
            int target = cat.TargetIndex;
            long phase = cat.PhaseTicks;
            Assert.AreEqual(S / 2, phase);

            // Перенос на камень, шахту и выход закрытого сектора и на свободную клетку в нём.
            var drops = new List<(string what, int col, int row)>();
            foreach (var o in new[]
                     {
                         closedTargets.First(o => o.Kind == ObjectKind.Rock),
                         closedTargets.First(o => o.Kind == ObjectKind.Shaft),
                         s.Layout.Exit,
                     })
                drops.Add((o.Token, s.Layout.Col(o.Cells[0]), s.Layout.Row(o.Cells[0])));
            int freeCol = 4, freeRow = gate.Row - 3;
            Assert.AreEqual(-1, s.Layout.CellOwner[s.Layout.CellIndex(freeCol, freeRow)], "предусловие: (4, 8) свободна");
            drops.Add(("свободная клетка (4, 8)", freeCol, freeRow));
            foreach (var d in drops)
            {
                var res = s.DropCat(cat.Id, d.col + 0.5, d.row + 0.5);
                Assert.AreNotEqual(CommandResult.Ok, res.Result, d.what + ": закрытый сектор");
                Assert.AreEqual(cell, cat.Cell, d.what + ": котик возвращается в исходную клетку");
                Assert.AreEqual(target, cat.TargetIndex, d.what + ": прежнее назначение сохраняется");
                Assert.AreEqual(phase, cat.PhaseTicks, d.what + ": фаза удара восстанавливается");
            }
            Assert.IsTrue(s.Mine.Cats.All(c => s.Layout.CellSector[c.Cell] == 0));

            // Динамит в закрытом секторе не применяется и не тратится.
            var closedRock = closedTargets.First(o => o.Kind == ObjectKind.Rock);
            var hp = s.Mine.Objects[closedRock.Ordinal].Hp;
            s.DebugAddDynamite(1);
            Assert.AreNotEqual(CommandResult.Ok, s.UseDynamite(s.Layout.Col(closedRock.Cells[0]) + 0.5, s.Layout.Row(closedRock.Cells[0]) + 0.5));
            Assert.AreEqual(1, s.Player.Dynamite, "заряд не потрачен");
            Assert.AreEqual(hp, s.Mine.Objects[closedRock.Ordinal].Hp);
        }

        /// <summary>
        /// Два котика добивают ворота в один момент: одно завершение, Broken; без тапа сектор закрыт
        /// при любом течении времени, онлайн и офлайн, и после перезапуска (ТЗ 8.3, 11.1, 12.3).
        /// </summary>
        [Test]
        public void GateBrokenBySimultaneousHitsStaysClosedUntilTap()
        {
            var s = NewSession(5);
            var gate = s.Layout.Gates.Single();
            // checkpoint ур. 5: MiningTargetHealths.checkpoint[4] = 20800 (JSON).
            Assert.AreEqual(20800.0, s.Mine.Objects[gate.Ordinal].Hp.ToDouble());
            Assert.AreNotEqual(CommandResult.Ok, s.OpenGate(gate.Ordinal), "закрытые ворота тапом не открываются");
            Assert.AreEqual(0, s.Mine.GatesOpened);

            // Освобождаем рабочие клетки (2..4, row_p + 1), ставим двух котиков L10 на ворота одновременно.
            for (int col = 2; col <= 4; col++) s.DebugCompleteTarget(OrdAt(s, col, gate.Row + 1));
            Assert.IsTrue(s.DebugSpawnCat(10));
            Assert.IsTrue(s.DebugSpawnCat(10));
            var a = s.Mine.Cats[0];
            var b = s.Mine.Cats[1];
            foreach (var c in new[] { a, b })
            {
                var r = s.DropCat(c.Id, 3.5, gate.Row + 0.5);
                Assert.AreEqual(CommandResult.Ok, r.Result);
                Assert.AreEqual(gate.Index, c.TargetIndex);
                Assert.AreEqual(gate.Row + 1, s.Layout.Row(c.Cell), "котик встаёт со стороны row_p + 1");
            }
            Assert.AreEqual(0L, a.PhaseTicks);
            Assert.AreEqual(0L, b.PhaseTicks);

            // E-02: урон L10 = 2.1^9, задержка 1 с; оба бьют в одни и те же моменты.
            int seconds = (int)Math.Ceiling(20800.0 / (2 * Math.Pow(2.1, 9)));
            s.Events.Clear();
            s.Advance(seconds * S - 1);
            Assert.AreEqual(ObjectStatus.Intact, s.Mine.Objects[gate.Ordinal].Status);
            s.Advance(1);
            Assert.AreEqual(ObjectStatus.Broken, s.Mine.Objects[gate.Ordinal].Status);
            Assert.AreEqual(1, Count(s, SimEventType.GateBroken), "одновременные удары — одно завершение");
            Assert.AreEqual(-1, a.TargetIndex, "после Broken котики в Idle");
            Assert.AreEqual(-1, b.TargetIndex, "после Broken котики в Idle");
            Assert.AreEqual(0, Count(s, SimEventType.CheckpointOpened));
            AssertNoGateApplied(s, "Broken");

            // Broken — не цель, граница закрыта для переноса.
            Assert.IsFalse(s.IsTargetAvailable(gate.Ordinal));
            int aCell = a.Cell;
            Assert.AreNotEqual(CommandResult.Ok, s.DropCat(a.Id, 3.5, gate.Row + 0.5).Result, "на разрушенные ворота нельзя");
            Assert.AreNotEqual(CommandResult.Ok, s.DropCat(a.Id, 3.5, gate.Row - 2 + 0.5).Result, "Broken не открывает сектор для переноса");
            Assert.AreEqual(aCell, a.Cell);

            // Время не открывает ворота: час онлайн и 8 ч офлайна.
            s.Advance(3600 * S);
            OfflineProgress.Apply(s, s.State.LastProcessedUtcMs + 8L * 3600 * 1000);
            Assert.AreEqual(ObjectStatus.Broken, s.Mine.Objects[gate.Ordinal].Status);
            AssertNoGateApplied(s, "Broken после онлайна и офлайна");

            // Перезапуск в состоянии Broken: ворота по-прежнему не открыты.
            var restored = Reload(s);
            Assert.AreEqual(ObjectStatus.Broken, restored.Mine.Objects[gate.Ordinal].Status);
            AssertNoGateApplied(restored, "Broken после save/load");

            // Тап открывает; клетки стоящих котиков не меняются (6.6).
            var ra = restored.Mine.Cats.Single(c => c.Id == a.Id);
            var rb = restored.Mine.Cats.Single(c => c.Id == b.Id);
            int raCell = ra.Cell, rbCell = rb.Cell;
            Assert.AreEqual(CommandResult.Ok, restored.OpenGate(gate.Ordinal));
            Assert.IsTrue(restored.IsSectorOpen(1));
            Assert.AreEqual(raCell, ra.Cell);
            Assert.AreEqual(rbCell, rb.Cell);
            // Теперь перенос в верхний сектор разрешён, а клетки открытых створок остаются занятыми.
            var up = restored.DropCat(ra.Id, 3.5, gate.Row - 2 + 0.5);
            Assert.AreEqual(CommandResult.Ok, up.Result);
            Assert.AreEqual(restored.Layout.CellIndex(3, gate.Row - 2), ra.Cell);
            Assert.AreNotEqual(CommandResult.Ok, restored.DropCat(rb.Id, 3.5, gate.Row + 0.5).Result, "на открытые ворота ставить нельзя");
            Assert.AreEqual(rbCell, rb.Cell);
        }

        /// <summary>Офлайн ворота доходят до Broken, но не до Opened (ТЗ 11.1, 12.3).</summary>
        [Test]
        public void GateReachesBrokenOfflineButOpensOnlyByTap()
        {
            var s = NewSession(5);
            var gate = s.Layout.Gates.Single();
            for (int col = 2; col <= 4; col++) s.DebugCompleteTarget(OrdAt(s, col, gate.Row + 1));
            for (int i = 0; i < 2; i++)
            {
                Assert.IsTrue(s.DebugSpawnCat(10));
                var c = s.Mine.Cats[i];
                Assert.AreEqual(CommandResult.Ok, s.DropCat(c.Id, 3.5, gate.Row + 0.5).Result);
            }
            s.Events.Clear();
            OfflineProgress.Apply(s, s.State.LastProcessedUtcMs + 3600L * 1000);
            Assert.AreEqual(ObjectStatus.Broken, s.Mine.Objects[gate.Ordinal].Status, "офлайн ворота доходят до Broken");
            Assert.AreEqual(1, Count(s, SimEventType.GateBroken));
            Assert.AreEqual(0, Count(s, SimEventType.CheckpointOpened), "офлайн ворота не открываются");
            AssertNoGateApplied(s, "после офлайна");
            Assert.AreEqual(CommandResult.Ok, s.OpenGate(gate.Ordinal));
            Assert.IsTrue(s.IsSectorOpen(1));
        }

        /// <summary>
        /// Награда scg_m005_c01 и четыре бонуса (E-05, E-06, E-08: доход ×2, найм ÷10, таймер −60 с, лимит +1)
        /// применяются ровно один раз: при двойном тапе, после save/load и повторного тапа после перезапуска.
        /// </summary>
        [Test]
        public void GateRewardAndFourBonusesApplyOnceAcrossDoubleTapAndSaveLoad()
        {
            var s = NewSession(5);
            var gate = s.Layout.Gates.Single();
            Assert.AreEqual("scg_m005_c01", gate.RewardId);
            AssertNoGateApplied(s, "до ворот");
            Assert.IsTrue(s.Player.Elixir.IsZero);
            Assert.AreEqual(0, s.CardLevel("ca002"));
            Assert.AreEqual(0, s.CardLevel("ca015"));

            s.DebugCompleteTarget(gate.Ordinal);
            s.Events.Clear();
            Assert.AreEqual(CommandResult.Ok, s.OpenGate(gate.Ordinal));
            Assert.AreNotEqual(CommandResult.Ok, s.OpenGate(gate.Ordinal), "повторный тап");
            Assert.AreEqual(1, Count(s, SimEventType.CheckpointOpened));
            Assert.AreEqual(1, Count(s, SimEventType.ChestOpened, "scg_m005_c01"), "сундук ворот открыт один раз");
            AssertOneGateApplied(s, gate, "после двойного тапа");
            AssertZone5GateReward(s, "после двойного тапа");

            string snap = Snapshot(s);
            var restored = Reload(s);
            Assert.AreEqual(snap, Snapshot(restored), "загрузка ничего не применяет повторно");
            AssertOneGateApplied(restored, gate, "после save/load");
            AssertZone5GateReward(restored, "после save/load");

            restored.Events.Clear();
            Assert.AreNotEqual(CommandResult.Ok, restored.OpenGate(gate.Ordinal), "перезапуск не даёт повторного открытия");
            Assert.AreEqual(0, Count(restored, SimEventType.ChestOpened));
            Assert.AreEqual(0, Count(restored, SimEventType.CheckpointOpened));
            AssertOneGateApplied(restored, gate, "тап после save/load");
            AssertZone5GateReward(restored, "тап после save/load");

            var again = Reload(restored);
            AssertOneGateApplied(again, gate, "второй save/load");
            // E-05: после одних ворот цены найма 1; 1; 4; 13.
            foreach (double price in new[] { 1.0, 1.0, 4.0, 13.0 })
            {
                Assert.AreEqual(price, again.HirePrice().ToDouble(), "цена найма после одних ворот");
                Assert.AreEqual(CommandResult.Ok, again.Hire());
            }
        }

        /// <summary>
        /// zone10, двое ворот: верхние недоступны, пока закрыт сектор под ними; бонусы складываются по одним за ворота,
        /// контроль E-08 после двух ворот — выход ×4, цена ÷100, интервал 2280 с, лимит 12.
        /// </summary>
        [Test]
        public void TwoGatesOpenBottomUpAndStackBonusesOncePerGate()
        {
            var s = NewSession(10);
            Assert.AreEqual(2, s.Layout.Gates.Count);
            var lower = s.Layout.Gates[0];
            var upper = s.Layout.Gates[1];
            Assert.Greater(lower.Row, upper.Row, "Gates[0] — нижние");
            Assert.IsTrue(s.IsTargetAvailable(lower.Ordinal));
            Assert.IsFalse(s.IsTargetAvailable(upper.Ordinal), "рабочая строка верхних ворот в закрытом секторе 1");
            Assert.IsTrue(s.DebugSpawnCat(1));
            var cat = s.Mine.Cats.Single();
            int cell = cat.Cell;
            Assert.AreNotEqual(CommandResult.Ok, s.DropCat(cat.Id, 3.5, upper.Row + 0.5).Result, "назначение на верхние ворота");
            Assert.AreEqual(cell, cat.Cell);
            Assert.AreEqual(-1, cat.TargetIndex);

            s.DebugCompleteTarget(lower.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.OpenGate(lower.Ordinal));
            Assert.IsTrue(s.IsSectorOpen(1));
            Assert.IsFalse(s.IsSectorOpen(2), "открывается только сектор над этими воротами");
            Assert.IsTrue(s.IsTargetAvailable(upper.Ordinal), "сектор под верхними воротами открыт");
            Assert.IsFalse(s.IsTargetAvailable(s.Layout.Exit.Ordinal), "выход в секторе 2 ещё закрыт");

            s.DebugCompleteTarget(upper.Ordinal);
            s.Events.Clear();
            Assert.AreEqual(CommandResult.Ok, s.OpenGate(upper.Ordinal));
            Assert.AreNotEqual(CommandResult.Ok, s.OpenGate(upper.Ordinal));
            Assert.AreNotEqual(CommandResult.Ok, s.OpenGate(lower.Ordinal));
            Assert.AreEqual(1, Count(s, SimEventType.CheckpointOpened));
            Assert.IsTrue(s.IsSectorOpen(2));
            Assert.IsTrue(s.IsTargetAvailable(s.Layout.Exit.Ordinal));

            // Предусловие: сундуки ворот не дали карт кодов 1/2/7/10 — иначе их эффект законно сдвинет контроль.
            var effectCodes = new[] { 1, 2, 7, 10 };
            foreach (var c in TestData.Balance.CardsInOrder.Where(c => effectCodes.Contains(c.StatModifierType)))
                Assert.AreEqual(0, s.CardLevel(c.Id), "предусловие: нет карты " + c.Id);

            AssertTwoGatesApplied(s, "после двух ворот и повторных тапов");
            var restored = Reload(s);
            AssertTwoGatesApplied(restored, "после save/load");
            Assert.AreNotEqual(CommandResult.Ok, restored.OpenGate(lower.Ordinal));
            Assert.AreNotEqual(CommandResult.Ok, restored.OpenGate(upper.Ordinal));
            AssertTwoGatesApplied(restored, "тапы после save/load");
            // E-05: цена = ceil(2·4^n / 100), не меньше 1 → 1; 1; 1; 2; 6.
            foreach (double price in new[] { 1.0, 1.0, 1.0, 2.0, 6.0 })
            {
                Assert.AreEqual(price, restored.HirePrice().ToDouble(), "цена найма после двух ворот");
                Assert.AreEqual(CommandResult.Ok, restored.Hire());
            }
        }

        // ───────────────────────────── Помощники ─────────────────────────────

        /// <summary>Бонусов ворот нет: лимит 10, цена найма 2, интервал 2400 с, доход ×1 (E-05, E-06, E-08, E-10).</summary>
        static void AssertNoGateApplied(GameSession s, string when)
        {
            Assert.AreEqual(0, s.Mine.GatesOpened, when);
            Assert.IsFalse(s.IsSectorOpen(1), when + ": сектор закрыт");
            Assert.AreEqual(10, s.UnitCap(), when + ": лимит");
            Assert.AreEqual(2.0, s.HirePrice().ToDouble(), when + ": цена первого найма");
            Assert.AreEqual(2400.0, s.FreeCatIntervalSeconds(), 1e-9, when + ": интервал бесплатного котика");
            Assert.AreEqual(1.0, s.GlobalIncomeMultiplier().ToDouble(), 1e-12, when + ": множитель дохода");
        }

        /// <summary>Одни ворота: доход ×2, цена 2 ÷ 10 → 1, интервал 2340 с, лимит 11 (E-05, E-06, E-08).</summary>
        static void AssertOneGateApplied(GameSession s, ZoneObject gate, string when)
        {
            Assert.AreEqual(1, s.Mine.GatesOpened, when);
            Assert.AreEqual(ObjectStatus.Opened, s.Mine.Objects[gate.Ordinal].Status, when);
            Assert.IsTrue(s.IsSectorOpen(1), when + ": сектор открыт");
            Assert.AreEqual(2.0, s.GlobalIncomeMultiplier().ToDouble(), 1e-12, when + ": доход ×2");
            Assert.AreEqual(3.0, s.AutoIncomePerSecond().ToDouble(), 1e-12, when + ": кузница L1 3 за 2 с × 2");
            Assert.AreEqual(1.0, s.HirePrice().ToDouble(), when + ": цена найма 2 ÷ 10 → 1");
            Assert.AreEqual(2340.0, s.FreeCatIntervalSeconds(), 1e-9, when + ": интервал 2400 − 60");
            Assert.AreEqual(11, s.UnitCap(), when + ": лимит 10 + 1");
        }

        /// <summary>scg_m005_c01 (GachaType 2, без множителей): ca002 × 6, ca015 × 1, 80 эликсира (ТЗ 10.3, E-14).</summary>
        static void AssertZone5GateReward(GameSession s, string when)
        {
            Assert.AreEqual(80.0, s.Player.Elixir.ToDouble(), 1e-9, when + ": эликсир один раз");
            Assert.AreEqual(1, s.CardLevel("ca002"), when);
            Assert.AreEqual(5, s.CardStateOf("ca002").Duplicates, when + ": 6 карт = ур. 1 + 5 копий");
            Assert.AreEqual(1, s.CardLevel("ca015"), when);
            Assert.AreEqual(0, s.CardStateOf("ca015").Duplicates, when);
        }

        /// <summary>Двое ворот, контроль E-08: выход ×4, цена ÷100, интервал 2280 с, лимит 12.</summary>
        static void AssertTwoGatesApplied(GameSession s, string when)
        {
            Assert.AreEqual(2, s.Mine.GatesOpened, when);
            Assert.AreEqual(4.0, s.GlobalIncomeMultiplier().ToDouble(), 1e-12, when + ": доход ×4");
            Assert.AreEqual(6.0, s.AutoIncomePerSecond().ToDouble(), 1e-12, when + ": кузница L1 3 за 2 с × 4");
            Assert.AreEqual(100.0, s.HireDivisor().ToDouble(), 1e-9, when + ": цена найма ÷100");
            Assert.AreEqual(1.0, s.HirePrice().ToDouble(), when + ": первая цена 2 ÷ 100 → 1");
            Assert.AreEqual(2280.0, s.FreeCatIntervalSeconds(), 1e-9, when + ": интервал 2280 с");
            Assert.AreEqual(12, s.UnitCap(), when + ": лимит 12");
        }
    }
}
