using System;
using System.Collections.Generic;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Grid;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// ТЗ 17.1, группа 5 «Наём и объединение» — дополнение к SimulationTests
    /// (NewZoneStartsWithoutDeadEnd, HireIsAtomicAndPriced, MergeKeepsTargetHpAndFreesSlot,
    /// DifferentLevelsDoNotMerge, GateBrokenThenOpenedAppliesBonusesOnce).
    /// Источники: ТЗ 7.1–7.5, 8.2, 11.1, 11.3, 12.1; Приложение C: E-02, E-05, E-08.
    /// </summary>
    public class HireMergeAcceptanceTests
    {
        const long S = GameSession.TicksPerSecond;

        // ───────────── Вспомогательные ─────────────

        static GameSession NewSession(int zone = 1, string slot = SaveSlots.Main) =>
            GameSession.NewGame(TestData.Balance, 12345UL, slot, 1_000_000L, zone);

        static int OrdAt(GameSession s, int col, int row) => s.ObjectAt(s.Layout.CellIndex(col, row));

        static string Snapshot(GameSession s) => SaveService.SerializeState(s.State);

        static GameSession Reload(GameSession s) =>
            GameSession.FromState(TestData.Balance, SaveService.DeserializeState(Snapshot(s)));

        static double Hp(GameSession s, int ord) => s.Mine.Objects[ord].Hp.ToDouble();

        static CatState NewestCat(GameSession s) => s.Mine.Cats.OrderBy(c => c.Id).Last();

        static DropOutcome DropOnto(GameSession s, CatState dragged, CatState receiver) =>
            s.DropCat(dragged.Id, s.Layout.Col(receiver.Cell) + 0.5, s.Layout.Row(receiver.Cell) + 0.5);

        /// <summary>E-05: цена покупки при n совершённых = ceil(2·4^n / 10^ворот), не меньше 1 (точная целая арифметика).</summary>
        static long ExpectedHirePrice(int n, int gates)
        {
            long raw = 2;
            for (int i = 0; i < n; i++) raw *= 4;
            long div = 1;
            for (int i = 0; i < gates; i++) div *= 10;
            return Math.Max(1L, (raw + div - 1) / div);
        }

        /// <summary>E-05: L_next = 1 + floor(0.4 + 0.125·n) = 1 + floor((16 + 5n) / 40).</summary>
        static int ExpectedHireLevel(int n) => 1 + (16 + 5 * n) / 40;

        /// <summary>E-05 / ТЗ 7.2: шкала до следующего уровня найма — дробная часть 0.4 + 0.125·n.</summary>
        static double ExpectedHireGauge(int n) => ((16 + 5 * n) % 40) / 40.0;

        /// <summary>
        /// Покупка с проверкой E-05 и ТЗ 7.2: цена и уровень до покупки, списание ровно показанной цены
        /// (UI и списание — один метод, 7.2, 15.1), счётчик покупок +1, новый котик нужного уровня
        /// в клетке появления в состоянии Idle. expectedPrice = null — точное значение цены не проверяется.
        /// </summary>
        static CatState HireChecked(GameSession s, long? expectedPrice, int expectedLevel, string label)
        {
            double shownPrice = s.HirePrice().ToDouble();
            if (expectedPrice.HasValue)
                Assert.AreEqual((double)expectedPrice.Value, shownPrice, label + ": цена следующей покупки");
            Assert.AreEqual(expectedLevel, s.HireLevel(), label + ": уровень следующей покупки");
            double goldBefore = s.Mine.Gold.ToDouble();
            int hiresBefore = s.Mine.HireCount;
            int catsBefore = s.Mine.Cats.Count;
            int spawnCell = s.FindSpawnCell();
            Assert.AreEqual(CommandResult.Ok, s.Hire(), label + ": покупка");
            Assert.AreEqual(goldBefore - shownPrice, s.Mine.Gold.ToDouble(), label + ": списана ровно показанная цена");
            Assert.AreEqual(hiresBefore + 1, s.Mine.HireCount, label + ": счётчик покупок зоны +1");
            Assert.AreEqual(catsBefore + 1, s.Mine.Cats.Count, label + ": создан ровно один котик");
            var cat = NewestCat(s);
            Assert.AreEqual(expectedLevel, cat.Level, label + ": уровень созданного котика");
            Assert.AreEqual(spawnCell, cat.Cell, label + ": котик в клетке появления (6.6)");
            Assert.AreEqual(-1, cat.TargetIndex, label + ": новый котик в Idle");
            return cat;
        }

        /// <summary>Объединяет пары котиков одного уровня, пока они есть (освобождает места лимита).</summary>
        static void MergeEqualLevels(GameSession s)
        {
            while (true)
            {
                CatState a = null, b = null;
                for (int i = 0; i < s.Mine.Cats.Count && a == null; i++)
                    for (int j = i + 1; j < s.Mine.Cats.Count; j++)
                        if (s.Mine.Cats[i].Level == s.Mine.Cats[j].Level)
                        {
                            a = s.Mine.Cats[i];
                            b = s.Mine.Cats[j];
                            break;
                        }
                if (a == null) return;
                int level = b.Level;
                var r = DropOnto(s, a, b);
                Assert.AreEqual(CommandResult.Ok, r.Result, "объединение котиков уровня " + level);
                Assert.IsTrue(r.Merged);
                Assert.AreEqual(level + 1, b.Level);
            }
        }

        static void OpenGates(GameSession s, int count)
        {
            for (int g = 0; g < count; g++)
            {
                var gate = s.Layout.Gates[g];
                s.DebugCompleteTarget(gate.Ordinal);
                Assert.AreEqual(CommandResult.Ok, s.OpenGate(gate.Ordinal), "открытие ворот " + g);
            }
            Assert.AreEqual(count, s.Mine.GatesOpened);
        }

        // ───────────── Нет тупика на старте (7.1) ─────────────

        /// <summary>
        /// ТЗ 7.1: в любой зоне на старте 25 золота, котиков нет, лимит 10, первая покупка стоит 2 и даёт котика L1;
        /// покупка проходит, и у купленного котика есть цель, которая его принимает (7.3).
        /// </summary>
        [Test]
        public void EveryZoneStartsWithoutDeadEnd()
        {
            for (int zone = 1; zone <= TestData.Balance.ZoneCount; zone++)
            {
                string z = "zone" + zone;
                var s = NewSession(zone);
                Assert.AreEqual(25.0, s.Mine.Gold.ToDouble(), z + ": стартовое золото");
                Assert.AreEqual(0, s.Mine.Cats.Count, z + ": котиков на старте нет");
                Assert.AreEqual(10, s.UnitCap(), z + ": лимит BaseUnitCap");
                Assert.AreEqual(2.0, s.HirePrice().ToDouble(), z + ": первая покупка — 2 золота");
                Assert.AreEqual(1, s.HireLevel(), z + ": первый котик — уровень 1");
                Assert.AreEqual(CommandResult.Ok, s.Hire(), z + ": первая покупка доступна");
                Assert.AreEqual(23.0, s.Mine.Gold.ToDouble(), z);
                var cat = s.Mine.Cats.Single();
                bool hasWork = false;
                for (int ord = 0; ord < s.Layout.Objects.Count && !hasWork; ord++)
                    hasWork = s.AcceptsCat(ord, cat.Id);
                Assert.IsTrue(hasWork, z + ": котику есть куда встать на работу");
            }
        }

        // ───────────── Цена и уровень по покупкам (7.2, E-05) ─────────────

        /// <summary>
        /// E-05 через команды сессии: цены 2; 8; 32; 128; 512; 2048 и далее ×4, уровни покупок 1–5 → 1, 6–13 → 2, 14–21 → 3,
        /// 22-я → 4; шкала стартует с 0.4 и растёт на 0.125 за покупку. Объединения не меняют счётчик покупок.
        /// Цена точная для всех 22 покупок, в том числе 21-й (2·4^20 = 2199023255552) и 22-й (2·4^21): ниже 2^53
        /// цена — ровно ceil (C.1 F-R), допуск округления не сдвигает целое значение вниз.
        /// </summary>
        [Test]
        public void HirePriceLevelAndGaugeFollowPurchaseNumber()
        {
            var s = NewSession(1, SaveSlots.Debug);
            s.DebugAddGold(GameNumber.FromDouble(1e15));
            double[] firstPrices = { 2, 8, 32, 128, 512, 2048 };
            int[] levels = { 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 4 };
            Assert.AreEqual(0.4, s.HireGauge(), 1e-9, "шкала уровня найма стартует с 0.4");
            for (int k = 1; k <= levels.Length; k++)
            {
                int n = k - 1;
                string label = "покупка " + k;
                if (n < firstPrices.Length)
                    Assert.AreEqual(firstPrices[n], (double)ExpectedHirePrice(n, 0), label + ": контрольное значение E-05");
                Assert.AreEqual(levels[n], ExpectedHireLevel(n), label + ": контрольное значение уровня E-05");
                Assert.AreEqual(ExpectedHireGauge(n), s.HireGauge(), 1e-9, label + ": шкала до следующего уровня");
                HireChecked(s, ExpectedHirePrice(n, 0), levels[n], label);
                MergeEqualLevels(s);
                Assert.AreEqual(k, s.Mine.HireCount, label + ": объединение не меняет счётчик покупок");
            }
        }

        /// <summary>
        /// E-05, C.1 F-R: цена найма при больших номерах покупки — ровно ceil(2·4^n / 10^ворот) ниже 2^53 и само значение
        /// от 2^53. Контрольные точки: 2·4^20 = 2199023255552, ceil(2·4^20 / 10) = 219902325556, 2·4^25 = 2^51,
        /// 2·4^26 = 2^53. Номер покупки выставлен через публичное поле HireCount (столько котиков не помещается в лимит).
        /// </summary>
        [Test]
        public void HirePriceIsExactCeilForLargePurchaseNumbers()
        {
            var noGate = NewSession(5, SaveSlots.Debug);
            var oneGate = NewSession(5, SaveSlots.Debug);
            OpenGates(oneGate, 1);
            Assert.AreEqual(2199023255552.0, (double)ExpectedHirePrice(20, 0), "2·4^20");
            Assert.AreEqual(219902325556.0, (double)ExpectedHirePrice(20, 1), "ceil(2·4^20 / 10)");
            Assert.AreEqual(2251799813685248.0, (double)ExpectedHirePrice(25, 0), "2·4^25 = 2^51");
            Assert.AreEqual(9007199254740992.0, (double)ExpectedHirePrice(26, 0), "2·4^26 = 2^53");
            var r = TestData.Balance.Reinforcement;
            for (int n = 0; n <= 27; n++)
            {
                noGate.Mine.HireCount = n;
                oneGate.Mine.HireCount = n;
                Assert.AreEqual((double)ExpectedHirePrice(n, 0), noGate.HirePrice().ToDouble(), "без ворот, n=" + n);
                Assert.AreEqual((double)ExpectedHirePrice(n, 1), oneGate.HirePrice().ToDouble(), "после одних ворот, n=" + n);
                Assert.GreaterOrEqual(noGate.HirePrice().ToDouble(), Formula.Evaluate(r.CostFormulaType, r.CostBase, r.CostMultiplier, r.CostGrowth, n + 1).ToDouble(), "цена не ниже сырого значения, n=" + n);
            }
        }

        /// <summary>E-05: бесплатные котики n не меняют — цена и уровень следующей покупки прежние.</summary>
        [Test]
        public void FreeCatDoesNotAdvanceHireCounter()
        {
            var s = NewSession();
            s.Advance(2400 * S);
            var box = s.Mine.Containers.Single(c => c.Kind == ContainerKind.FreeCat);
            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(box.Id));
            Assert.AreEqual(1, s.Mine.Cats.Count);
            Assert.AreEqual(0, s.Mine.HireCount, "бесплатный котик не считается покупкой");
            Assert.AreEqual(2.0, s.HirePrice().ToDouble());
            Assert.AreEqual(1, s.HireLevel());
            Assert.AreEqual(0.4, s.HireGauge(), 1e-9);
            HireChecked(s, 2, 1, "первая покупка после бесплатного котика");
            Assert.AreEqual(8.0, s.HirePrice().ToDouble());
        }

        /// <summary>E-05, 11.3: счётчик и уровень найма живут в пределах зоны и сбрасываются при переходе.</summary>
        [Test]
        public void HireCounterResetsOnZoneTransition()
        {
            var s = NewSession(2, SaveSlots.Debug);
            s.DebugAddGold(GameNumber.FromDouble(1e6));
            for (int n = 0; n < 6; n++) HireChecked(s, ExpectedHirePrice(n, 0), ExpectedHireLevel(n), "zone2, покупка " + (n + 1));
            Assert.AreEqual(8192.0, s.HirePrice().ToDouble());
            Assert.AreEqual(2, s.HireLevel(), "после 6 покупок уровень найма 2");
            s.DebugCompleteTarget(s.Layout.Exit.Ordinal);
            var shaft = s.Layout.Shafts.Single();
            s.DebugCompleteTarget(shaft.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(shaft.Ordinal));
            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            Assert.AreEqual(3, s.Mine.Zone);
            Assert.AreEqual(0, s.Mine.HireCount, "счётчик покупок новой зоны с нуля");
            Assert.AreEqual(2.0, s.HirePrice().ToDouble(), "цена снова 2");
            Assert.AreEqual(1, s.HireLevel(), "уровень снова 1");
            Assert.AreEqual(0.4, s.HireGauge(), 1e-9, "шкала снова 0.4");
        }

        // ───────────── Скидка ворот (7.2, 11.1, E-05, E-08) ─────────────

        /// <summary>
        /// E-05: «После одних ворот цены 1; 1; 4; 13» — стартового золота 25 хватает на все четыре.
        /// Уровень найма от ворот не зависит. Ворота делят текущую цену, счётчик покупок не сбрасывают.
        /// После save/load цена та же.
        /// </summary>
        [Test]
        public void OneGateDividesHirePriceByTen()
        {
            var s = NewSession(5, SaveSlots.Debug);
            OpenGates(s, 1);
            double[] afterGate = { 1, 1, 4, 13 };
            for (int n = 0; n < afterGate.Length; n++)
            {
                Assert.AreEqual(afterGate[n], (double)ExpectedHirePrice(n, 1), "контрольное значение E-05");
                HireChecked(s, (long)afterGate[n], 1, "после ворот, покупка " + (n + 1));
            }
            Assert.AreEqual(25.0 - 19.0, s.Mine.Gold.ToDouble(), "четыре покупки стоят 19 из стартовых 25");
            Assert.AreEqual(52.0, s.HirePrice().ToDouble(), "ceil(512 / 10)");
            var restored = Reload(s);
            Assert.AreEqual(52.0, restored.HirePrice().ToDouble(), "скидка ворот после save/load");
            Assert.AreEqual(1, restored.HireLevel());

            // Ворота открыты после трёх покупок: цена 128 становится ceil(12.8) = 13, уровень и счётчик не меняются.
            var t = NewSession(5, SaveSlots.Debug);
            t.DebugAddGold(GameNumber.FromDouble(1000));
            for (int n = 0; n < 3; n++) HireChecked(t, ExpectedHirePrice(n, 0), 1, "до ворот, покупка " + (n + 1));
            Assert.AreEqual(128.0, t.HirePrice().ToDouble());
            OpenGates(t, 1);
            Assert.AreEqual(3, t.Mine.HireCount, "ворота не сбрасывают счётчик покупок");
            Assert.AreEqual(13.0, t.HirePrice().ToDouble(), "ceil(128 / 10)");
            Assert.AreEqual(1, t.HireLevel());
            HireChecked(t, 13, 1, "после ворот, покупка 4");
        }

        /// <summary>
        /// E-08: после двух открытых ворот цена ÷100 и лимит 12 (+ уровень ca038, если карта выпала из сундука ворот).
        /// Лимит реально позволяет нанять ровно столько котиков; следующая покупка — CapReached без изменений состояния.
        /// </summary>
        [Test]
        public void TwoGatesDivideHirePriceBy100AndRaiseCapTo12()
        {
            var s = NewSession(10, SaveSlots.Debug);
            Assert.AreEqual(2, s.Layout.Gates.Count, "в zone10 двое ворот");
            OpenGates(s, 2);
            int expectedCap = 12 + s.CardLevel("ca038");
            Assert.AreEqual(expectedCap, s.UnitCap(), "лимит 10 + 1 за каждые ворота (+L за ca038)");
            s.DebugAddGold(GameNumber.FromDouble(1e9));
            Assert.AreEqual(1.0, (double)ExpectedHirePrice(0, 2));
            Assert.AreEqual(6.0, (double)ExpectedHirePrice(4, 2), "ceil(512 / 100)");
            Assert.AreEqual(21.0, (double)ExpectedHirePrice(5, 2), "ceil(2048 / 100)");
            for (int n = 0; n < expectedCap; n++)
                HireChecked(s, ExpectedHirePrice(n, 2), ExpectedHireLevel(n), "две пары ворот, покупка " + (n + 1));
            Assert.AreEqual(expectedCap, s.OccupiedSlots);
            Assert.AreEqual(CommandResult.CapReached, s.CanHire());
            var before = Snapshot(s);
            Assert.AreEqual(CommandResult.CapReached, s.Hire());
            Assert.AreEqual(before, Snapshot(s), "отказ по лимиту не меняет состояние");
        }

        // ───────────── Лимит (7.2) ─────────────

        /// <summary>
        /// ТЗ 7.2: при достижении лимита покупка не производится и счётчики не меняются;
        /// объединение уменьшает число занятых мест на одно, и покупка снова доступна по цене следующего номера.
        /// </summary>
        [Test]
        public void CapRejectsHireAtomicallyAndMergeFreesSlot()
        {
            var s = NewSession(1, SaveSlots.Debug);
            s.DebugAddGold(GameNumber.FromDouble(1e9));
            for (int n = 0; n < 10; n++) HireChecked(s, ExpectedHirePrice(n, 0), ExpectedHireLevel(n), "покупка " + (n + 1));
            Assert.AreEqual(10, s.UnitCap());
            Assert.AreEqual(10, s.OccupiedSlots);
            Assert.AreEqual(CommandResult.CapReached, s.CanHire());
            Assert.IsTrue(s.Mine.Gold >= s.HirePrice(), "золота хватает — отказ именно по лимиту");
            var before = Snapshot(s);
            for (int i = 0; i < 3; i++) Assert.AreEqual(CommandResult.CapReached, s.Hire(), "повторное нажатие " + i);
            Assert.AreEqual(before, Snapshot(s), "отказ по лимиту: золото, котики и счётчик покупок не меняются");
            Assert.AreEqual(10, s.Mine.HireCount);

            var l1 = s.Mine.Cats.Where(c => c.Level == 1).Take(2).ToArray();
            Assert.IsTrue(DropOnto(s, l1[0], l1[1]).Merged);
            Assert.AreEqual(9, s.OccupiedSlots, "объединение освобождает одно место");
            Assert.AreEqual(CommandResult.Ok, s.CanHire());
            HireChecked(s, ExpectedHirePrice(10, 0), ExpectedHireLevel(10), "покупка 11 после объединения");
            Assert.AreEqual(CommandResult.CapReached, s.CanHire());
        }

        /// <summary>ТЗ 7.2: неоткрытый контейнер бесплатного котика занимает место в лимите.</summary>
        [Test]
        public void FreeCatContainerCountsTowardCap()
        {
            var s = NewSession(1, SaveSlots.Debug);
            s.DebugAddGold(GameNumber.FromDouble(1e9));
            for (int n = 0; n < 9; n++) s.Hire();
            Assert.AreEqual(9, s.Mine.Cats.Count);
            s.Advance(2400 * S);
            Assert.AreEqual(1, s.Mine.Containers.Count(c => c.Kind == ContainerKind.FreeCat), "контейнер бесплатного котика");
            Assert.AreEqual(9, s.Mine.Cats.Count);
            Assert.AreEqual(10, s.OccupiedSlots, "котики на поле + контейнер с котиком");
            Assert.AreEqual(CommandResult.CapReached, s.CanHire());
            var before = Snapshot(s);
            Assert.AreEqual(CommandResult.CapReached, s.Hire());
            Assert.AreEqual(before, Snapshot(s), "отказ по лимиту не меняет состояние");
            var box = s.Mine.Containers.Single(c => c.Kind == ContainerKind.FreeCat);
            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(box.Id));
            Assert.AreEqual(10, s.Mine.Cats.Count);
            Assert.AreEqual(CommandResult.CapReached, s.CanHire());
        }

        // ───────────── Объединение (7.4, 7.5) ─────────────

        /// <summary>
        /// ТЗ 7.4: два котика уровня L дают одного L + 1 для разных L; результат — в клетке получателя,
        /// перетаскиваемый удаляется, его клетка освобождается, занятых мест на одно меньше.
        /// </summary>
        [Test]
        public void EqualLevelsMergeIntoNextLevelInReceiverCell()
        {
            var s = NewSession(1, SaveSlots.Debug);
            for (int level = 1; level <= 6; level++)
            {
                Assert.IsTrue(s.DebugSpawnCat(level));
                var dragged = NewestCat(s);
                Assert.IsTrue(s.DebugSpawnCat(level));
                var receiver = NewestCat(s);
                int draggedCell = dragged.Cell, receiverCell = receiver.Cell;
                int slots = s.OccupiedSlots;
                var r = DropOnto(s, dragged, receiver);
                string l = "L" + level + " + L" + level;
                Assert.AreEqual(CommandResult.Ok, r.Result, l);
                Assert.IsTrue(r.Merged, l);
                Assert.AreEqual(receiverCell, r.Cell, l + ": результат в клетке получателя");
                Assert.AreEqual(level + 1, receiver.Level, l + ": уровень результата");
                Assert.AreEqual(receiverCell, receiver.Cell, l);
                Assert.AreSame(receiver, s.CatAt(receiverCell), l);
                Assert.IsNull(s.FindCat(dragged.Id), l + ": перетаскиваемый удалён");
                Assert.IsNull(s.CatAt(draggedCell), l + ": клетка перетаскиваемого свободна");
                Assert.IsTrue(s.IsCellFree(draggedCell), l);
                Assert.AreEqual(slots - 1, s.OccupiedSlots, l + ": занятых мест на одно меньше");
            }
        }

        /// <summary>
        /// ТЗ 7.4, 7.5, E-02: перетаскиваемый работает у камня A, получатель — у камня B.
        /// Результат сохраняет назначение получателя (B), назначение перетаскиваемого не наследуется,
        /// его рабочая клетка освобождается; HP обеих целей не меняются;
        /// фаза удара обнуляется — первый удар L2 (урон 2.1) ровно через полную задержку 1 с, двойного удара нет.
        /// </summary>
        [Test]
        public void MergeKeepsReceiverAssignmentAndRestartsPhase()
        {
            var s = NewSession();
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            var a = s.Mine.Cats[0];
            var b = s.Mine.Cats[1];
            int rockA = OrdAt(s, 2, 4), rockB = OrdAt(s, 3, 4);
            Assert.AreEqual(16.0, Hp(s, rockA), "rockonboardingmine1 L1");
            Assert.AreEqual(16.0, Hp(s, rockB), "rockonboardingmine1 L1");
            var dropA = s.DropCat(a.Id, 2.5, 4.5);
            var dropB = s.DropCat(b.Id, 3.5, 4.5);
            Assert.AreEqual(s.Layout.Objects[rockA].Index, dropA.TargetIndex);
            Assert.AreEqual(s.Layout.Objects[rockB].Index, dropB.TargetIndex);
            int cellA = a.Cell, cellB = b.Cell;

            s.Advance(3 * S + S / 2);
            Assert.AreEqual(13.0, Hp(s, rockA), 1e-9, "3 удара по A");
            Assert.AreEqual(13.0, Hp(s, rockB), 1e-9, "3 удара по B");
            Assert.AreEqual(S / 2, b.PhaseTicks, "фаза получателя до объединения");

            var r = DropOnto(s, a, b);
            Assert.IsTrue(r.Merged);
            Assert.AreEqual(cellB, r.Cell);
            Assert.AreEqual(s.Layout.Objects[rockB].Index, r.TargetIndex, "результат продолжает работу получателя");
            Assert.AreEqual(2, b.Level);
            Assert.AreEqual(s.Layout.Objects[rockB].Index, b.TargetIndex);
            Assert.AreEqual(0L, b.PhaseTicks);
            Assert.AreEqual(13.0, Hp(s, rockA), 1e-9, "HP цели перетаскиваемого не меняется");
            Assert.AreEqual(13.0, Hp(s, rockB), 1e-9, "HP цели получателя не меняется");
            Assert.AreEqual(0, s.WorkersOn(rockA), "назначение перетаскиваемого не наследуется");
            Assert.AreEqual(1, s.WorkersOn(rockB));
            Assert.IsTrue(s.IsCellFree(cellA), "рабочая клетка перетаскиваемого освобождена");
            Assert.IsTrue(s.AcceptsCat(rockA, b.Id), "камень A снова принимает котика");

            // Старая фаза 0.5 с дала бы удар через 0.5 с — его нет.
            s.Advance(S / 2);
            Assert.AreEqual(13.0, Hp(s, rockB), 1e-9, "фаза не унаследована");
            s.Advance(S / 2 - 1);
            Assert.AreEqual(13.0, Hp(s, rockB), 1e-9, "до полной задержки удара нет");
            s.Advance(1);
            Assert.AreEqual(13.0 - 2.1, Hp(s, rockB), 1e-9, "первый удар L2 = 2.1 ровно через 1 с");
            s.Advance(S - 1);
            Assert.AreEqual(13.0 - 2.1, Hp(s, rockB), 1e-9, "двойного удара нет");
            Assert.AreEqual(13.0, Hp(s, rockA), 1e-9, "камень A никто не бьёт");
        }

        /// <summary>ТЗ 7.4: получатель в Idle, перетаскиваемый работал — результат в Idle, цель перетаскиваемого не трогается.</summary>
        [Test]
        public void MergeOntoIdleReceiverGivesIdleResult()
        {
            var s = NewSession();
            s.Hire();
            s.Hire();
            var a = s.Mine.Cats[0];
            var b = s.Mine.Cats[1];
            int rockA = OrdAt(s, 2, 4);
            s.DropCat(a.Id, 2.5, 4.5);
            int workCell = a.Cell;
            int receiverCell = b.Cell;
            s.Advance(2 * S + S / 2);
            Assert.AreEqual(14.0, Hp(s, rockA), 1e-9);
            Assert.AreEqual(-1, b.TargetIndex, "получатель в Idle");

            var r = DropOnto(s, a, b);
            Assert.IsTrue(r.Merged);
            Assert.AreEqual(receiverCell, r.Cell);
            Assert.AreEqual(-1, r.TargetIndex, "результат в Idle");
            Assert.AreEqual(2, b.Level);
            Assert.AreEqual(receiverCell, b.Cell);
            Assert.AreEqual(-1, b.TargetIndex);
            Assert.AreEqual(14.0, Hp(s, rockA), 1e-9);
            Assert.AreEqual(0, s.WorkersOn(rockA));
            Assert.IsTrue(s.IsCellFree(workCell), "рабочая клетка перетаскиваемого освобождена");

            s.Advance(10 * S);
            Assert.AreEqual(14.0, Hp(s, rockA), 1e-9, "без назначения ударов нет");
            Assert.AreEqual(-1, b.TargetIndex, "автоматического выбора цели нет (7.3)");
        }

        /// <summary>
        /// ТЗ 7.3, 7.5: отпускание на котика другого уровня — подсказка «Нужны котики одного уровня»,
        /// обмена нет, перетаскиваемый остаётся на рабочей клетке с прежним назначением и фазой.
        /// </summary>
        [Test]
        public void DifferentLevelsKeepAssignmentAndPhase()
        {
            var s = NewSession(1, SaveSlots.Debug);
            s.DebugSpawnCat(1);
            s.DebugSpawnCat(2);
            var a = s.Mine.Cats[0];
            var b = s.Mine.Cats[1];
            int rockA = OrdAt(s, 2, 4);
            s.DropCat(a.Id, 2.5, 4.5);
            s.Advance(2 * S + S / 2);
            Assert.AreEqual(14.0, Hp(s, rockA), 1e-9);
            var before = Snapshot(s);
            s.Events.Clear();

            var r = DropOnto(s, a, b);
            Assert.AreEqual(CommandResult.DifferentLevels, r.Result);
            Assert.IsFalse(r.Merged);
            Assert.IsTrue(s.Events.Any(e => e.Type == SimEventType.CommandRejected && e.Text == Loc.T("core.reject.different_levels")),
                "подсказка «Cats must be the same level»");
            Assert.AreEqual(before, Snapshot(s), "котики, клетки, назначение и фаза не изменились");

            s.Advance(S / 2);
            Assert.AreEqual(13.0, Hp(s, rockA), 1e-9, "фаза сохранена: удар в прежний момент");
        }

        // ───────────── Восстановление после save/load (7.3, 7.5, 8.2, 12.1) ─────────────

        /// <summary>
        /// После смеси покупок, объединения и назначений: снимок → загрузка → FromState.
        /// Котики (ID, уровень, клетка, цель, фаза), HP целей, счётчик покупок, цена, уровень и золото совпадают;
        /// исходные котики объединения не возвращаются. Продолжение Advance и тех же команд на восстановленной сессии
        /// даёт побайтно тот же снимок, что и непрерывная сессия.
        /// </summary>
        [Test]
        public void SaveLoadRestoresHireMergeStateAndContinuesIdentically()
        {
            var s = NewSession();
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            var a = s.Mine.Cats[0];
            var b = s.Mine.Cats[1];
            s.DropCat(a.Id, 2.5, 4.5);
            s.DropCat(b.Id, 3.5, 4.5);
            s.Advance(16 * S);
            Assert.AreEqual(-1, a.TargetIndex, "камень L1 разрушен за 16 с");
            Assert.AreEqual(-1, b.TargetIndex);
            Assert.IsTrue(DropOnto(s, a, b).Merged);
            int mergedAwayId = a.Id;
            var toRock23 = s.DropCat(b.Id, 2.5, 3.5);
            Assert.AreEqual(CommandResult.Ok, toRock23.Result);
            Assert.GreaterOrEqual(toRock23.TargetIndex, 0, "L2 назначен на камень (2, 3)");
            Assert.AreEqual(CommandResult.Ok, s.CanHire(), "золота хватает на третью покупку");
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            var c = NewestCat(s);
            var toRock33 = s.DropCat(c.Id, 3.5, 3.5);
            Assert.GreaterOrEqual(toRock33.TargetIndex, 0, "L1 назначен на камень (3, 3)");
            s.Advance(S + 370_000);
            Assert.IsTrue(s.Mine.Cats.Any(x => x.TargetIndex >= 0 && x.PhaseTicks % S != 0), "есть незавершённая фаза удара");

            var restored = Reload(s);
            Assert.AreEqual(Snapshot(s), Snapshot(restored), "повторная сериализация совпадает");
            Assert.AreEqual(s.Mine.Cats.Count, restored.Mine.Cats.Count);
            for (int i = 0; i < s.Mine.Cats.Count; i++)
            {
                var x = s.Mine.Cats[i];
                var y = restored.Mine.Cats[i];
                string l = "котик " + x.Id;
                Assert.AreEqual(x.Id, y.Id, l);
                Assert.AreEqual(x.Level, y.Level, l + ": уровень");
                Assert.AreEqual(x.Cell, y.Cell, l + ": клетка");
                Assert.AreEqual(x.TargetIndex, y.TargetIndex, l + ": назначение");
                Assert.AreEqual(x.PhaseTicks, y.PhaseTicks, l + ": фаза удара");
                Assert.AreEqual(y.Id, restored.CatAt(y.Cell)?.Id, l + ": котик найден по клетке");
            }
            Assert.IsNull(restored.FindCat(mergedAwayId), "перезагрузка не возвращает исходного котика объединения");
            for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
            {
                Assert.AreEqual(s.Mine.Objects[ord].Status, restored.Mine.Objects[ord].Status, "объект " + ord);
                Assert.AreEqual(s.Mine.Objects[ord].Hp, restored.Mine.Objects[ord].Hp, "HP объекта " + ord);
                if (s.Layout.Objects[ord].IsTarget) Assert.AreEqual(s.WorkersOn(ord), restored.WorkersOn(ord), "работники цели " + ord);
            }
            Assert.AreEqual(3, restored.Mine.HireCount);
            Assert.AreEqual(s.Mine.HireCount, restored.Mine.HireCount);
            Assert.AreEqual(128.0, restored.HirePrice().ToDouble());
            Assert.AreEqual(1, restored.HireLevel());
            Assert.AreEqual(s.HireGauge(), restored.HireGauge());
            Assert.AreEqual(s.Mine.Gold, restored.Mine.Gold, "золото");
            Assert.AreEqual(s.OccupiedSlots, restored.OccupiedSlots);
            Assert.AreEqual(s.FindSpawnCell(), restored.FindSpawnCell(), "та же клетка появления");

            // Продолжение: только время.
            foreach (long dt in new[] { 1L, S / 3, 7 * S + 12_345, 30 * S, 120 * S })
            {
                s.Advance(dt);
                restored.Advance(dt);
                Assert.AreEqual(Snapshot(s), Snapshot(restored), "после Advance(" + dt + ")");
            }
            // Продолжение: те же команды (покупка, объединение, назначение) и снова время.
            foreach (var x in new[] { s, restored })
            {
                Assert.AreEqual(CommandResult.Ok, x.Hire());
                var newest = NewestCat(x);
                var other = x.Mine.Cats.First(k => k.Id != newest.Id && k.Level == newest.Level);
                Assert.IsTrue(DropOnto(x, newest, other).Merged, "объединение после загрузки");
                x.DropCat(other.Id, 2.5, 2.5);
            }
            Assert.AreEqual(Snapshot(s), Snapshot(restored), "после команд");
            s.Advance(120 * S);
            restored.Advance(120 * S);
            Assert.AreEqual(Snapshot(s), Snapshot(restored), "после Advance(120 с)");
        }

        /// <summary>
        /// ТЗ 7.3: «При загрузке пара TargetKey/WorkCell сохраняется, если цель допустима, а WorkCell — её рабочая клетка.
        /// Иначе — Idle». Сохранение из другой сборки или после миграции моделируется правкой публичных полей
        /// десериализованного GameState: клетка котика не рабочая клетка цели; цель уже разрушена (HP 0).
        /// </summary>
        [Test]
        public void LoadResetsInvalidAssignmentToIdle()
        {
            var s = NewSession();
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            var cat = s.Mine.Cats[0];
            int rock = OrdAt(s, 2, 4);
            Assert.GreaterOrEqual(rock, 0, "камень (2, 4)");
            var drop = s.DropCat(cat.Id, 2.5, 4.5);
            Assert.AreEqual(CommandResult.Ok, drop.Result);
            int rockIndex = s.Layout.Objects[rock].Index;
            Assert.AreEqual(rockIndex, cat.TargetIndex, "котик назначен на камень (2, 4)");
            s.Advance(S * 3 / 2);
            Assert.Greater(cat.PhaseTicks, 0L, "идёт фаза удара");
            string saved = Snapshot(s);

            // Контроль: допустимая пара сохраняется вместе с фазой.
            var valid = GameSession.FromState(TestData.Balance, SaveService.DeserializeState(saved));
            Assert.AreEqual(rockIndex, valid.Mine.Cats[0].TargetIndex, "допустимая цель и рабочая клетка — назначение сохраняется");
            Assert.AreEqual(cat.PhaseTicks, valid.Mine.Cats[0].PhaseTicks, "фаза удара сохраняется");

            // 1. Сохранённая WorkCell — не рабочая клетка цели.
            int far = s.Layout.CellIndex(4, 8);
            Assert.IsTrue(s.IsCellFree(far), "клетка (4, 8) свободна");
            Assert.IsFalse(s.IsWorkCellOf(rock, far), "клетка (4, 8) не рабочая клетка камня (2, 4)");
            var stale = SaveService.DeserializeState(saved);
            stale.Mine.Cats[0].Cell = far;
            var restored = GameSession.FromState(TestData.Balance, stale);
            Assert.AreEqual(-1, restored.Mine.Cats[0].TargetIndex, "WorkCell не рабочая клетка цели → Idle");
            Assert.AreEqual(0L, restored.Mine.Cats[0].PhaseTicks, "у Idle нет фазы удара");
            Assert.AreEqual(0, restored.WorkersOn(rock), "котик не считается работником камня");
            var hpBefore = restored.Mine.Objects[rock].Hp;
            restored.Advance(3 * S);
            Assert.AreEqual(hpBefore, restored.Mine.Objects[rock].Hp, "котик не бьёт камень с чужой клетки");
            Assert.AreEqual(restored.Mine.Cats[0].Id, restored.CatAt(far)?.Id, "котик остаётся в своей клетке");

            // 2. Цель уже недопустима: камень разрушен, HP 0.
            var destroyed = SaveService.DeserializeState(saved);
            destroyed.Mine.Objects[rock].Status = ObjectStatus.Destroyed;
            destroyed.Mine.Objects[rock].Hp = GameNumber.Zero;
            var restored2 = GameSession.FromState(TestData.Balance, destroyed);
            Assert.IsFalse(restored2.IsTargetAvailable(rock), "разрушенный камень — недопустимая цель");
            Assert.AreEqual(-1, restored2.Mine.Cats[0].TargetIndex, "цель недопустима → Idle");
            Assert.AreEqual(0L, restored2.Mine.Cats[0].PhaseTicks, "у Idle нет фазы удара");
            Assert.AreEqual(0, restored2.WorkersOn(rock), "у разрушенного камня нет работников");
        }
    }
}
