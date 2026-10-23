using System.IO;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>Таблицы темпа Приложения C.4 (EconomyTables) и их контрольные точки.</summary>
    public class EconomyReportTests
    {
        [Test]
        public void TimeToKillControlValues()
        {
            var s = GameSession.NewGame(TestData.Balance, 1UL, SaveSlots.Debug, 0L, 1);
            // rockbasic L1 = 16 HP, котик L1 бьёт на 1 раз в секунду.
            Assert.AreEqual(16.0, EconomyTables.TimeToKill(s, "rockbasic", 1, 1), 1e-9);
            // rockbasic L2 = 60 HP, котик L2 бьёт на 2.1: ceil(60 / 2.1) = 29 ударов.
            Assert.AreEqual(29.0, EconomyTables.TimeToKill(s, "rockbasic", 2, 2), 1e-9);
            // amethyst L1 = 64 HP.
            Assert.AreEqual(64.0, EconomyTables.TimeToKill(s, "amethyst", 1, 1), 1e-9);
            // citrine L1 = 70 HP (MiningTargetHealths.citrine[0]).
            Assert.AreEqual(70.0, TestData.Balance.TargetHp("citrine", 1).ToDouble(), 1e-9);
            Assert.AreEqual(70.0, EconomyTables.TimeToKill(s, "citrine", 1, 1), 1e-9);
        }

        /// <summary>
        /// Цели таблицы time-to-kill (C.4) берутся из данных: самый частый камень zone1–10, все шахты этих зон, ворота, выход.
        /// </summary>
        [Test]
        public void TimeToKillTargetsComeFromData()
        {
            var balance = TestData.Balance;
            var targets = EconomyTables.TimeToKillTargets(balance, 10);
            Assert.AreEqual("rockbasic", targets[0], "самый частый камень zone1–10");
            Assert.AreEqual(BalanceRepository.CheckpointId, targets[targets.Count - 2]);
            Assert.AreEqual(BalanceRepository.ExitId, targets[targets.Count - 1]);
            CollectionAssert.DoesNotContain(targets, BalanceRepository.CartId);
            CollectionAssert.AllItemsAreUnique(targets);
            var shafts = EconomyTables.GeneratorsInFirstZones(balance, 10).Where(g => g.Key != BalanceRepository.CartId).Select(g => g.Key).ToList();
            Assert.Greater(shafts.Count, 1, "в zone1–10 больше одного типа шахт");
            CollectionAssert.AreEqual(shafts, targets.Skip(1).Take(shafts.Count).ToList(), "все шахты zone1–10 в порядке появления");
            string text = EconomyTables.Build(balance);
            foreach (var id in targets) StringAssert.Contains("### " + id + " (", text);
            StringAssert.Contains("### citrine (шахта, с zone3)", text);
        }

        /// <summary>
        /// Доход генератора на уровне L (основа выбора «самого выгодного» улучшения ботом): выход с порогами и множителями
        /// за цикл; состояние генератора не меняется.
        /// </summary>
        [Test]
        public void IncomePerSecondAtIsSideEffectFree()
        {
            var s = GameSession.NewGame(TestData.Balance, 1UL, SaveSlots.Debug, 0L, 1);
            int forge = s.Layout.Cart.Ordinal;
            var gen = s.Mine.Objects[forge].Gen;
            var def = s.DefFor(s.Layout.Objects[forge]);
            var now = s.GeneratorOutput(forge) / GameNumber.FromDouble(GameSession.TicksToSeconds(s.GeneratorCycleTicks(forge)));
            Assert.AreEqual(now.ToDouble(), EconomyTables.IncomePerSecondAt(s, forge, gen.Level).ToDouble(), 1e-9);
            // Кузница: 1.5/с на L1, 30/с на первом пороге (выход ×2 к L10, см. economy-tables.md).
            Assert.AreEqual(1.5, EconomyTables.IncomePerSecondAt(s, forge, 1).ToDouble(), 1e-9);
            int t1 = def.Thresholds[0];
            var below = EconomyTables.IncomePerSecondAt(s, forge, t1 - 1);
            var at = EconomyTables.IncomePerSecondAt(s, forge, t1);
            Assert.Greater(at.ToDouble(), below.ToDouble() * 2, "порог умножает выход");
            Assert.AreEqual(1, gen.Level, "уровень восстановлен");
            Assert.AreEqual(0, gen.ThresholdsReached, "счётчик порогов восстановлен");
            Assert.AreEqual(now.ToDouble(), s.AutoIncomePerSecond().ToDouble(), 1e-9);
        }

        [Test]
        public void TablesCoverForgeAndShaftsOfFirstZones()
        {
            var gens = EconomyTables.GeneratorsInFirstZones(TestData.Balance, 10);
            Assert.AreEqual("spawningcart", gens[0].Key);
            Assert.IsTrue(gens.Exists(g => g.Key == "amethyst" && g.Value == 2));
            Assert.IsTrue(gens.Exists(g => g.Key == "citrine"));
            string text = EconomyTables.Build(TestData.Balance);
            StringAssert.Contains("### spawningcart", text);
            StringAssert.DoesNotContain("NaN", text);
            StringAssert.DoesNotContain("Infinity", text);
        }

        /// <summary>Пишет Docs/Reports/economy-tables.md. Запуск: run.ps1 -Filter EconomyTablesReport.</summary>
        [Test, Explicit("пишет отчёт: dotnet test --filter EconomyTablesReport")]
        public void EconomyTablesReport()
        {
            string text = EconomyTables.Build(TestData.Balance);
            string dir = Path.Combine(TestData.ProjectRoot, "Docs", "Reports");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "economy-tables.md"), text);
            TestContext.WriteLine(text);
        }
    }
}
