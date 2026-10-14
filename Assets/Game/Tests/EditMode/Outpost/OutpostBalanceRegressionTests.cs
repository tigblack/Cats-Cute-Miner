using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Регрессии аудита баланса b4 (Docs/Rework/BALANCE_AUDIT.md): формулы глубины шахты, предел «Оценки», перенос сохранений
    /// со снимком b3, отсутствие «дешёвого дерева» в поздних шахтах, точность учёта времени аудита и равенство офлайна онлайну.
    /// </summary>
    public sealed class OutpostBalanceRegressionTests
    {
        static OutpostConfigSet s_Configs;
        static OutpostConfigSet Configs => s_Configs ?? (s_Configs = OutpostConfigSet.LoadDefault().WithCards(TestData.Balance));

        /// <summary>Профиль b3 (до аудита): без удорожания по глубине, прежние HP, разгрузка и предел «Оценки».</summary>
        static OutpostBalanceConfig B3()
        {
            var b = Configs.Balance.Clone();
            b.Version = "outpost-2.1-b3";
            b.CostZoneStep = Rational.Zero;
            b.ZoneHpStep = Rational.Of(1, 200);
            b.UnloadStep = Rational.Of(3, 20);
            var value = b.Branch(UpgradeBranch.Value);
            value.Cap = 50;
            value.Milestones = new[] { 10, 25, 50 };
            return b;
        }

        [Test]
        public void ProfileB4_Values()
        {
            var b = Configs.Balance;
            Assert.AreEqual("outpost-2.1-b4", b.Version);
            Assert.AreEqual(OutpostIds.BalanceVersion, b.Version);
            Assert.AreEqual(Rational.Of(1, 10), b.CostZoneStep);
            Assert.AreEqual(Rational.Of(1, 25), b.ZoneHpStep);
            Assert.AreEqual(Rational.Of(1, 5), b.UnloadStep);
            Assert.AreEqual(40, b.Branch(UpgradeBranch.Value).Cap);
            CollectionAssert.AreEqual(new[] { 10, 25, 40 }, b.Branch(UpgradeBranch.Value).Milestones);
        }

        /// <summary>NextCost(z, L) = ceil(S(z) · (1 + (z−1)/10) · BaseCost · Growth^L): в шахте 1 цены прежние (ТЗ 12.5), глубже — дороже.</summary>
        [Test]
        public void CostZoneFactor_ExactPrices()
        {
            var b = Configs.Balance;
            Assert.AreEqual(new BigInteger(20), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 0));
            Assert.AreEqual(new BigInteger(24), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 1));
            Assert.AreEqual(new BigInteger(89), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 9));
            Assert.AreEqual(new BigInteger(105), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 10));
            Assert.AreEqual(new BigInteger(50), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Team, 0));
            foreach (int zone in new[] { 2, 10, 50, 144 })
                foreach (UpgradeBranch br in Enum.GetValues(typeof(UpgradeBranch)))
                    for (int level = 0; level < 5; level++)
                    {
                        var cfg = b.Branch(br);
                        var expected = (Rational.Of(6, 5).Pow(zone - 1) * (Rational.One + Rational.Of(zone - 1, 10)) * cfg.BaseCost * cfg.Growth.Pow(level)).Ceil();
                        Assert.AreEqual(expected, TeamUpgradeService.NextCost(b, zone, br, level), br + " z" + zone + " L" + level);
                    }
            // Скидка карточек входит в то же произведение до единственного ceil.
            var discounted = TeamUpgradeService.NextCost(b, 50, UpgradeBranch.Boots, 3, Rational.Of(1, 10));
            Assert.AreEqual((Rational.Of(6, 5).Pow(49) * Rational.Of(59, 10) * 25 * Rational.Of(13, 10).Pow(3) * Rational.Of(9, 10)).Ceil(), discounted);
        }

        [Test]
        public void ValueCap40_MilestoneAtCap()
        {
            var b = Configs.Balance;
            Assert.AreEqual(Rational.Of(4, 1), b.Milestone(UpgradeBranch.Value, 39));
            Assert.AreEqual(Rational.Of(8, 1), b.Milestone(UpgradeBranch.Value, 40));
            Assert.IsNotNull(TeamUpgradeService.NextCost(b, 144, UpgradeBranch.Value, 39));
            Assert.IsNull(TeamUpgradeService.NextCost(b, 144, UpgradeBranch.Value, 40));
        }

        /// <summary>
        /// Найденная проблема: в b3 дерево местных улучшений стоило одинаково (в базовых монетах) во всех шахтах, а груз поздних
        /// шахт вырос в 11 раз — поздние шахты покупали всё за первую половину, 59 % золота оставалось. Цена всех пределов, кроме
        /// «Оценки», относительно груза шахты падает к шахте 144 не больше чем вдвое: b3 этот критерий нарушает, b4 — нет.
        /// </summary>
        [Test]
        public void LateMines_UpgradeTreeNotTriviallyCheap()
        {
            double Ratio(OutpostBalanceConfig b, int zone)
            {
                var map = OutpostLevelAdapter.BuildMine(TestData.Balance, zone, Configs);
                var tb = OutpostLevelBalanceAdapter.Compute(map, b, TestData.Balance);
                double yield = tb.Sum(t => t.TotalCargoUnits * t.ValueFactor.ToDouble());
                var scale = (b.CoinBase * b.ZoneMoneyScale(zone)).ToDouble();
                double cost = 0;
                foreach (UpgradeBranch br in Enum.GetValues(typeof(UpgradeBranch)))
                {
                    if (br == UpgradeBranch.Value) continue;
                    for (int level = 0; level < b.Branch(br).Cap; level++) cost += (double)TeamUpgradeService.NextCost(b, zone, br, level).Value;
                }
                return cost / scale / yield;
            }
            var b4 = Configs.Balance;
            var b3 = B3();
            double b4Drop = Ratio(b4, 144) / Ratio(b4, 10), b3Drop = Ratio(b3, 144) / Ratio(b3, 10);
            TestContext.WriteLine("non-Value caps / mine yield, z144 relative to z10: b3 " + b3Drop + ", b4 " + b4Drop +
                "; b4 z10/z60/z144 = " + Ratio(b4, 10) + " / " + Ratio(b4, 60) + " / " + Ratio(b4, 144));
            // Критерий: дерево шахты 144 относительно её груза дешевле, чем в шахте 10, не больше чем вдвое.
            // Исходный b3 нарушает его (≈0.24 — вчетверо дешевле: найденная проблема), b4 — ≈0.66.
            Assert.Less(b3Drop, 0.5, "профиль b3 должен воспроизводить найденную проблему");
            Assert.GreaterOrEqual(b4Drop, 0.5);
        }

        /// <summary>
        /// Сохранение посреди шахты со снимком b3 загружается под b4: цены, предел «Оценки», HP и груз текущей шахты прежние;
        /// следующая шахта — b4; награда завершения выдаётся один раз; предметы, детали, карточки и эликсир не теряются.
        /// </summary>
        [Test]
        public void B3SaveMidMine_KeepsSnapshot_NextMineUsesB4_NoDuplicateRewards()
        {
            var repo = TestData.Balance;
            var b3 = Configs.WithBalance(B3());
            var state = StageProgressionService.NewGame(repo, b3, 77, 0, 5);
            state.Profile.Items["pickaxe.rare"] = 4;
            state.Profile.Equipped[(int)EquipSlot.Pickaxe] = "pickaxe.rare";
            state.Profile.Parts = 123;
            state.Profile.Elixir = 456;
            var map = OutpostLevelAdapter.ForRun(repo, state.Run);
            var sim = new OutpostSimulation(state, map, b3);
            sim.Advance(90 * OutpostIds.TicksPerSecond);
            while (sim.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Pickaxes)).Ok) { }
            var hpBefore = state.Run.Targets.Select(t => t.MaxHpUnits).ToArray();
            var cardsBefore = OutpostJson.Serialize(state.Profile.Cards);

            // «Перезапуск игры»: состояние из JSON, игра уже с конфигурацией b4.
            var loaded = OutpostJson.Deserialize<OutpostGameState>(OutpostJson.Serialize(state));
            Assert.AreEqual("outpost-2.1-b3", loaded.Run.Balance.Version);
            Assert.AreEqual(Rational.Zero, loaded.Run.Balance.CostZoneStep);
            CollectionAssert.IsEmpty(OutpostInvariants.Check(loaded, OutpostLevelAdapter.ForRun(repo, loaded.Run), Configs.Equipment, Configs.Cards));
            var sim2 = new OutpostSimulation(loaded, OutpostLevelAdapter.ForRun(repo, loaded.Run), Configs);
            int level = loaded.Run.Levels[(int)UpgradeBranch.Boots];
            var q = sim2.Quote(UpgradeBranch.Boots, BuyMode.One);
            var b3Price = (Rational.Of(6, 5).Pow(4) * 25 * Rational.Of(13, 10).Pow(level)).Ceil();
            Assert.AreEqual(b3Price, q.NextCost, "текущая шахта продолжает цены своего снимка");
            Assert.AreEqual(50, loaded.Run.Balance.Branch(UpgradeBranch.Value).Cap);
            CollectionAssert.AreEqual(hpBefore, loaded.Run.Targets.Select(t => t.MaxHpUnits).ToArray());

            var report = new OutpostBot(BotPolicy.C).Run(sim2);
            Assert.IsTrue(report.Success, report.ToString());
            var tr = StageProgressionService.PrepareTransition(loaded, repo, Configs, 0);
            Assert.IsTrue(tr.Ok, tr.Reason);
            var next = tr.State;
            Assert.AreEqual(6, next.Run.Zone);
            Assert.AreEqual("outpost-2.1-b4", next.Run.Balance.Version);
            Assert.AreEqual(TeamUpgradeService.NextCost(Configs.Balance, 6, UpgradeBranch.Boots, 0),
                TeamUpgradeService.Quote(next.Run, UpgradeBranch.Boots, BuyMode.One).NextCost);
            Assert.AreEqual(1, next.Profile.RewardLog.Count(r => r.Key == OutpostIds.FirstCompletionKey(5)));
            Assert.AreEqual(4, next.Profile.ItemLevel("pickaxe.rare"));
            Assert.AreEqual("pickaxe.rare", next.Profile.Equipped[(int)EquipSlot.Pickaxe]);
            Assert.GreaterOrEqual(next.Profile.Parts, 123);
            Assert.GreaterOrEqual(next.Profile.Elixir, 456);
            foreach (var kv in OutpostJson.Deserialize<Dictionary<string, CardProgress>>(cardsBefore))
                Assert.GreaterOrEqual(next.Profile.CardLevel(kv.Key), kv.Value.Level, kv.Key);

            // Повторная выдача той же награды невозможна.
            Assert.IsNull(StageProgressionService.ApplyCompletion(loaded, Configs));
        }

        /// <summary>Снимок без поля CostZoneStep (профили b1–b3) читается как 0: цены прежние.</summary>
        [Test]
        public void OldSnapshotWithoutCostZoneStep_ReadsAsZero()
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(OutpostJson.Serialize(B3()));
            json.Remove("CostZoneStep");
            var b = OutpostJson.Deserialize<OutpostBalanceConfig>(json.ToString());
            Assert.AreEqual(Rational.Zero, b.CostZoneStep);
            Assert.AreEqual(Rational.One, b.CostZoneFactor(144));
            Assert.AreEqual((Rational.Of(6, 5).Pow(143) * 30).Ceil(), TeamUpgradeService.NextCost(b, 144, UpgradeBranch.Value, 0));
        }

        /// <summary>Учёт аудита: два котика без покупок — сумма котико-секунд равна 2 × время шахты; прогон детерминирован.</summary>
        [Test]
        public void AuditProbe_AttributesAllWorkerTime_Deterministic()
        {
            MineAudit Run()
            {
                var state = StageProgressionService.NewGame(TestData.Balance, Configs, 5, 0, 3);
                var sim = new OutpostSimulation(state, OutpostLevelAdapter.ForRun(TestData.Balance, state.Run), Configs);
                return BalanceAudit.RunMine(sim, BalanceAudit.Model("none"), "probe");
            }
            var a = Run();
            Assert.AreEqual("Completed", a.Outcome, a.Error);
            Assert.AreEqual(2 * a.CompletedSec.Value, a.WorkerSecTotal, 1e-6);
            Assert.Greater(a.WorkerSec["Mining"], 0);
            Assert.Greater(a.WorkerSec["Returning"], 0);
            Assert.AreEqual(a.Sectors.Count, a.Gates + 1);
            Assert.IsTrue(a.Sectors.All(s => s.OpenSec != null && s.RsxClearSec != null));
            Assert.Greater(a.CargoWeightedDistance, 0);
            Assert.AreEqual(OutpostJson.Serialize(a), OutpostJson.Serialize(Run()));
        }

        /// <summary>
        /// Офлайн согласован с экономикой: idle-игрок, который покупает и уходит на 5 минут, получает то же состояние, что и при
        /// непрерывной онлайн-игре с теми же покупками (тот же Advance; ТЗ 13).
        /// </summary>
        [Test]
        public void IdleSessions_OfflineEqualsOnline()
        {
            var repo = TestData.Balance;
            var online = StageProgressionService.NewGame(repo, Configs, 9, 0, 4);
            var offline = OutpostJson.DeepCopy(online);
            var map = OutpostLevelAdapter.ForRun(repo, online.Run);
            var simOn = new OutpostSimulation(online, map, Configs);
            long nowUtc = 0;
            for (int visit = 0; visit < 6; visit++)
            {
                // Онлайн: покупки, затем 5 минут.
                BuyCheapest(simOn);
                simOn.Advance(simOn.Now + 300 * OutpostIds.TicksPerSecond);
                // Офлайн: те же покупки в момент возвращения, затем офлайн-расчёт 5 минут.
                var simOff = new OutpostSimulation(offline, map, Configs);
                BuyCheapest(simOff);
                nowUtc += 300_000;
                var job = new OfflineSimulator(map, Configs).Begin(offline, nowUtc);
                job.RunToEnd();
                offline = job.State;
                offline.Run.LastProcessedUtcMs = nowUtc;
                online.Run.LastProcessedUtcMs = nowUtc;
                Assert.AreEqual(OutpostJson.Serialize(online), OutpostJson.Serialize(offline), "визит " + visit);
            }
            Assert.Greater(online.Run.TotalCredited, BigInteger.Zero);
        }

        static void BuyCheapest(OutpostSimulation sim)
        {
            var model = new PlayerModel { Strategy = PurchaseStrategy.Cheapest };
            model.Buy(sim, null);
        }

        /// <summary>
        /// Темп кампании эталонного игрока (модель free: «База» раз в 30 с, узкое место, карточки/детали/«Надеть лучшее» между
        /// шахтами) в ориентирах ТЗ 12.6 по этапам: кривая растёт, нет тупиков, долгих пауз без доступной покупки и резких скачков.
        /// </summary>
        [Test, Category("OutpostSlow")]
        public void Campaign_FreePlayer_PacingWithinStageTargets()
        {
            var audit = BalanceAudit.RunCampaign(TestData.Balance, Configs, BalanceAudit.Model("free"), 1);
            Assert.IsTrue(audit.Success, string.Join("; ", audit.Mines.Where(m => m.Outcome != "Completed").Select(m => m.Zone + ": " + m.Error)));
            Assert.AreEqual(144, audit.Mines.Count);
            var stages = new[] { (1, 1, 3.0, 5.5), (2, 10, 3.0, 7.0), (11, 30, 4.0, 9.0), (31, 60, 5.0, 11.0), (61, 100, 6.0, 13.0), (101, 144, 8.0, 15.0) };
            double previous = 0;
            foreach (var (from, to, lo, hi) in stages)
            {
                var minutes = audit.Mines.Where(m => m.Zone >= from && m.Zone <= to).Select(m => m.CompletedSec.Value / 60).OrderBy(x => x).ToList();
                double median = minutes[minutes.Count / 2];
                TestContext.WriteLine("zones " + from + "-" + to + ": median " + median.ToString("0.00") + " min");
                Assert.That(median, Is.InRange(lo, hi), "этап " + from + "–" + to);
                if (from > 2) Assert.GreaterOrEqual(median, previous * 0.95, "кривая этапов не проваливается");
                previous = median;
            }
            foreach (var m in audit.Mines)
                Assert.Less(m.MaxUnaffordableSec, 120, "шахта " + m.Zone + ": слишком долго без доступной покупки");
            for (int i = 1; i < audit.Mines.Count; i++)
                Assert.Less(audit.Mines[i].CompletedSec.Value / audit.Mines[i - 1].CompletedSec.Value, 2.2, "скачок шахта " + audit.Mines[i].Zone);
        }
    }
}
