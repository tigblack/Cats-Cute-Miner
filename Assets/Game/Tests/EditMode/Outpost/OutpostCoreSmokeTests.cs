using System;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Дымовые проверки нового ядра CatOutpostV2: полный цикл zone1 без покупок (политика A) и с покупками (C),
    /// контрольные значения ТЗ 12.5, разбиение времени с save/load, статическая доступность всех 144 карт.
    /// </summary>
    public class OutpostCoreSmokeTests
    {
        static OutpostConfigSet Configs => OutpostConfigSet.LoadDefault().WithTopologies(null);

        [Test]
        public void Zone1_PolicyA_ReachesReadyToContinue_AllCargoDelivered()
        {
            var report = OutpostBot.RunZone(TestData.Balance, Configs, 1, BotPolicy.A, 42, setup: b => b.CheckInvariantsEveryTick = true);
            TestContext.WriteLine(report.ToString());
            Assert.AreEqual(BotOutcome.Completed, report.Outcome, report.Error + " " + report.WatchdogDiagnostic + " " + string.Join("; ", report.InvariantErrors));
            CollectionAssert.IsEmpty(report.InvariantErrors);
            Assert.IsNull(report.FirstPurchaseSec, "политика A ничего не покупает");
            Assert.AreEqual(report.ValueCreated, report.Credited, "весь выпущенный груз сдан");
            Assert.AreEqual("0", report.Spent);
            Assert.AreEqual("pickaxe.common", report.RewardItemId);
            Assert.AreEqual(10, report.RewardParts);
            Assert.That(report.FirstDeliverySec, Is.Not.Null.And.GreaterThan(0));
        }

        [Test]
        public void Zone1_FullState_IsConsistentAfterCompletion()
        {
            var cfg = Configs;
            var state = StageProgressionService.NewGame(TestData.Balance, cfg, 7, 1000, 1);
            var map = OutpostLevelAdapter.Build(TestData.Balance, 1, cfg.Overrides);
            var sim = new OutpostSimulation(state, map, cfg) { RecordEvents = false };
            sim.Advance(24L * 3600 * OutpostIds.TicksPerSecond);
            var run = state.Run;
            Assert.AreEqual(RunPhase.ReadyToContinue, run.Phase, run.WatchdogDiagnostic);
            Assert.IsTrue(run.Targets.All(t => t.Destroyed && t.ReleasedUnits == t.TotalUnits));
            Assert.AreEqual(run.Targets.Sum(t => t.TotalUnits), run.Counters.DeliveredUnits);
            Assert.AreEqual(0, run.Batches.Count);
            Assert.IsTrue(run.Workers.All(w => w.Bag.Count == 0 && w.Action == null));
            Assert.AreEqual(run.Wallet, run.TotalCredited);
            Assert.AreEqual(OutpostIds.FirstCompletionKey(1), run.CompletionRewardKey);
            Assert.AreEqual(1, state.Profile.RewardLog.Count);
            Assert.AreEqual(1, state.Profile.ItemLevel("pickaxe.common"));
            CollectionAssert.IsEmpty(OutpostInvariants.Check(state, map, cfg.Equipment));
            Assert.IsFalse(run.WatchdogFired);
        }

        [Test]
        public void Zone1_PolicyC_FinishesFasterThanA()
        {
            var a = OutpostBot.RunZone(TestData.Balance, Configs, 1, BotPolicy.A, 42);
            var c = OutpostBot.RunZone(TestData.Balance, Configs, 1, BotPolicy.C, 42, setup: b => b.CheckInvariantsEveryTick = true);
            TestContext.WriteLine(a.ToString());
            TestContext.WriteLine(c.ToString());
            Assert.AreEqual(BotOutcome.Completed, c.Outcome, c.Error + " " + c.WatchdogDiagnostic + " " + string.Join("; ", c.InvariantErrors));
            CollectionAssert.IsEmpty(c.InvariantErrors);
            Assert.That(c.FirstPurchaseSec, Is.Not.Null);
            Assert.That(c.Purchases, Is.GreaterThan(0));
            Assert.That(c.CompletedSec, Is.LessThan(a.CompletedSec));
            Assert.AreEqual(c.ValueCreated, c.Credited);
        }

        [Test]
        public void ControlValues_TZ_12_5()
        {
            var cfg = Configs;
            var b = cfg.Balance;
            // Цены кирок zone1: L0/L1/L9/L10 → 20/24/89/105.
            Assert.AreEqual(new BigInteger(20), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 0));
            Assert.AreEqual(new BigInteger(24), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 1));
            Assert.AreEqual(new BigInteger(89), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 9));
            Assert.AreEqual(new BigInteger(105), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 10));
            Assert.IsNull(TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 20));
            // Множители порогов («Оценка» профиля b4: пороги 10/25/40, cap 40 — на L40 цены нет).
            CollectionAssert.AreEqual(new[] { 1, 2, 2, 4 }, new[] { 9, 10, 19, 20 }.Select(l => (int)b.Milestone(UpgradeBranch.Pickaxes, l).Num).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 2, 4, 4, 8 }, new[] { 9, 10, 24, 25, 39, 40 }.Select(l => (int)b.Milestone(UpgradeBranch.Value, l).Num).ToArray());
            Assert.IsNotNull(TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Value, 39));
            Assert.IsNull(TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Value, 40));

            // Стартовые характеристики zone1 без экипировки.
            var profile = StageProgressionService.NewProfile(cfg, 1);
            var s0 = TeamStats.Compute(b, new int[6], profile, cfg.Equipment, 1);
            Assert.AreEqual(2, s0.Workers);
            Assert.AreEqual(Rational.FromLong(5), s0.FinalDamage);
            Assert.AreEqual(5_000_000L, s0.DamageUnits);
            Assert.AreEqual(Rational.Of(5, 2), s0.FinalMoveSpeed);
            Assert.AreEqual(400_000L, s0.EdgeTicks);
            Assert.AreEqual(4, s0.Capacity);
            Assert.AreEqual(500_000L, s0.UnloadTicks);
            var s1 = TeamStats.Compute(b, new[] { 0, 1, 0, 0, 0, 0 }, profile, cfg.Equipment, 1);
            Assert.AreEqual(Rational.Of(61, 10), s1.FinalDamage);

            // Обычная кирка/рюкзак уровня 1: урон 5.4, сумка 5.
            profile.Items["pickaxe.common"] = 1;
            profile.Items["backpack.common"] = 1;
            Assert.IsTrue(EquipmentService.EquipBest(profile, cfg.Equipment));
            var s2 = TeamStats.Compute(b, new int[6], profile, cfg.Equipment, 1);
            Assert.AreEqual(Rational.Of(27, 5), s2.FinalDamage);
            Assert.AreEqual(5, s2.Capacity);

            // HP/порции: r площадь 1 rank 0 → 20 HP, 2 порции по 5; x площадь 2 rank 1/2 → 80 HP, 8 порций по 3.
            Assert.AreEqual(20L, OutpostLevelBalanceAdapter.MaxHp(b, 1, Rational.Zero, 1, 'r'));
            Assert.AreEqual(80L, OutpostLevelBalanceAdapter.MaxHp(b, 1, Rational.Half, 2, 'x'));
            Assert.AreEqual(new BigInteger(5), s0.CoinValue(Rational.One));
            Assert.AreEqual(new BigInteger(3), s0.CoinValue(Rational.Half));

            // zone13, индекс 78 (ворота, rank 1) → 444 HP по профилю b4: 20 · (1 + 12/25) · 6 · 5/2 = 444 ровно (точный ceil;
            // наивный double 20·(1 + 0.04·12)·6·2.5 = 444.00000000000006 дал бы 445). В ТЗ 2.1 с шагом 1/200 эта цель имела 318 HP.
            var map13 = OutpostLevelAdapter.Build(TestData.Balance, 13, cfg.Overrides);
            var tb13 = OutpostLevelBalanceAdapter.Compute(map13, b, TestData.Balance);
            Assert.AreEqual(444L, tb13[map13.TargetIndex(78)].MaxHp);

            // Легендарный предмет уровня 20 → +117%.
            Assert.AreEqual(Rational.Of(117, 100), cfg.Equipment.ItemBonus(ItemRarity.Legendary, 20));
            // Детали за zone1/4/5/10/144.
            CollectionAssert.AreEqual(new[] { 10, 10, 15, 20, 150 }, new[] { 1, 4, 5, 10, 144 }.Select(cfg.Rewards.CompletionParts).ToArray());
            // Полная стоимость улучшения 1→20 по редкостям.
            var totals = Enumerable.Range(0, 4).Select(r => Enumerable.Range(1, 19).Sum(q => cfg.Equipment.UpgradeCost((ItemRarity)r, q))).ToArray();
            CollectionAssert.AreEqual(new long[] { 673, 1001, 1665, 2655 }, totals);
        }

        [Test]
        public void SplitAdvance_WithSaveLoad_IsByteIdentical()
        {
            var cfg = Configs;
            var map = OutpostLevelAdapter.Build(TestData.Balance, 5, cfg.Overrides);
            var a = StageProgressionService.NewGame(TestData.Balance, cfg, 99, 0, 5);
            var b = OutpostSaveService.DeserializeState(OutpostSaveService.SerializeState(a));
            long total = 157_345_678L;
            new OutpostSimulation(a, map, cfg).Advance(total);

            var simB = new OutpostSimulation(b, map, cfg);
            simB.Advance(41_234_567L);
            var b2 = OutpostSaveService.DeserializeState(OutpostSaveService.SerializeState(b));
            var simB2 = new OutpostSimulation(b2, map, cfg);
            simB2.Advance(90_000_001L);
            simB2.Advance(total);
            Assert.AreEqual(OutpostSaveService.SerializeState(a), OutpostSaveService.SerializeState(b2));
            CollectionAssert.IsEmpty(OutpostInvariants.Check(b2, map, cfg.Equipment));
        }

        [Test]
        public void StaticReachability_All144Zones()
        {
            var cfg = Configs;
            int cleared = 0, gates = 0, sourceCleared = 0;
            for (int z = 1; z <= OutpostIds.LastZone; z++)
            {
                var map = OutpostLevelAdapter.Build(TestData.Balance, z, cfg.Overrides);
                var r = OutpostReachability.Check(map);
                Assert.IsTrue(r.Passed, r.Diagnostic);
                cleared += r.Cleared;
                gates += r.GatesOpened;
                var source = OutpostReachability.Check(OutpostLevelAdapter.Build(TestData.Balance, z));
                Assert.IsTrue(source.Passed, source.Diagnostic);
                sourceCleared += source.Cleared;
            }
            // ТЗ 8.5: источник — 22 129 r/s/x и 280 ворот; override zone1 добавляет 36 целей (аудит static_clearance_totals).
            Assert.AreEqual(22129, sourceCleared);
            Assert.AreEqual(22165, cleared);
            Assert.AreEqual(280, gates);
        }
        /// <summary>Все 144 карты, политика A, пустой комплект, без покупок (ТЗ 20): успех — ReadyToContinue и весь груз сдан.</summary>
        [Test]
        public void AllZones_PolicyA_Complete()
        {
            var cfg = Configs;
            var failures = new System.Collections.Generic.List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int z = 1; z <= OutpostIds.LastZone; z++)
            {
                var r = OutpostBot.RunZone(TestData.Balance, cfg, z, BotPolicy.A, 42);
                TestContext.WriteLine(r.ToString());
                if (!r.Success || r.ValueCreated != r.Credited)
                    failures.Add(r.ZoneId + " " + r.Outcome + " " + r.Error + " " + r.WatchdogDiagnostic + " " + string.Join("; ", r.InvariantErrors));
            }
            TestContext.WriteLine("elapsed " + sw.Elapsed);
            CollectionAssert.IsEmpty(failures);
        }
        [Test]
        public void Campaign_PolicyC_Zones1To12_RewardsAndReset()
        {
            var cfg = Configs;
            var reports = OutpostBot.RunCampaign(TestData.Balance, cfg, BotPolicy.C, 5, 12, true, b => b.CheckInvariantsEveryTick = true);
            foreach (var r in reports) TestContext.WriteLine(r.ToString() + " equipped=[" + string.Join(",", r.Equipped) + "]");
            Assert.AreEqual(12, reports.Count, reports.Last().Error + " " + string.Join("; ", reports.Last().InvariantErrors));
            Assert.IsTrue(reports.All(r => r.Success));
            CollectionAssert.AreEqual(new[] { "pickaxe.common", "hat.common", "backpack.common", "boots.common" }, reports.Take(4).Select(r => r.RewardItemId).ToArray());
            Assert.AreEqual("pickaxe.rare", reports[9].RewardItemId);
            // После перехода каждая шахта начинается с двух котиков; экипировка надета с зоны 2.
            Assert.AreEqual("pickaxe.common", reports[1].Equipped[(int)EquipSlot.Pickaxe]);
        }

        [Test]
        public void SaveService_RoundTrip_AndOfflineMatchesOnline()
        {
            var cfg = Configs;
            var map = OutpostLevelAdapter.Build(TestData.Balance, 2, cfg.Overrides);
            var state = StageProgressionService.NewGame(TestData.Balance, cfg, 3, 1_000_000, 2);
            var sim = new OutpostSimulation(state, map, cfg);
            sim.Advance(30 * OutpostIds.TicksPerSecond);

            string dir = System.IO.Path.Combine(TestData.ProjectRoot, "obj", "OutpostSmoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                var svc = new OutpostSaveService(dir) { Configs = cfg, MapResolver = s => map };
                var w1 = svc.Save(state, OutpostSaveSlot.Main, map);
                Assert.IsTrue(w1.Ok, w1.Error);
                sim.Advance(35 * OutpostIds.TicksPerSecond);
                var w2 = svc.Save(state, OutpostSaveSlot.Main, map);
                Assert.IsTrue(w2.Ok, w2.Error);
                Assert.Greater(w2.CommitSequence, w1.CommitSequence);
                var loaded = new OutpostSaveService(dir) { Configs = cfg, MapResolver = s => map }.Load();
                Assert.AreEqual(OutpostLoadStatus.Loaded, loaded.Status, string.Join("; ", loaded.Messages));
                Assert.AreEqual(w2.CommitSequence, loaded.CommitSequence);
                Assert.AreEqual(OutpostSaveService.SerializeState(state), OutpostSaveService.SerializeState(loaded.State));

                // Офлайн 120 с на копии == онлайн Advance на 120 с (кроме UTC-маркера).
                var job = new OfflineSimulator(map, cfg).Begin(loaded.State, state.Run.LastProcessedUtcMs + 120_000);
                job.RunToEnd();
                Assert.AreEqual(120_000, job.Summary.AppliedMs);
                sim.Advance(sim.Now + 120 * OutpostIds.TicksPerSecond);
                state.Run.LastProcessedUtcMs = job.State.Run.LastProcessedUtcMs;
                Assert.AreEqual(OutpostSaveService.SerializeState(state), OutpostSaveService.SerializeState(job.State));
                Assert.Greater(job.Summary.UnitsDelivered, 0);
            }
            finally
            {
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            }
        }
        [Test]
        public void ConfigFiles_MatchEmbeddedDefaults_AndExactParsing()
        {
            string dir = System.IO.Path.Combine(TestData.ProjectRoot, "Assets", "Game", "Resources", "Outpost");
            string Read(string name) => System.IO.File.ReadAllText(System.IO.Path.Combine(dir, name)).Replace("\r\n", "\n");
            Assert.AreEqual(OutpostConfigSet.DefaultBalanceJson.Replace("\r\n", "\n"), Read("outpost_balance.json"));
            Assert.AreEqual(OutpostConfigSet.DefaultEquipmentJson.Replace("\r\n", "\n"), Read("equipment_catalog.json"));
            Assert.AreEqual(OutpostConfigSet.DefaultRewardsJson.Replace("\r\n", "\n"), Read("stage_rewards.json"));
            Assert.AreEqual(OutpostConfigSet.DefaultOverridesJson.Replace("\r\n", "\n"), Read("level_overrides.json"));
            var a = OutpostConfigSet.LoadDefault();
            var b = OutpostConfigSet.Parse(Read("outpost_balance.json").Replace("\n", "\r\n"), Read("equipment_catalog.json"),
                Read("stage_rewards.json"), Read("level_overrides.json"));
            Assert.AreEqual(a.BalanceHash, b.BalanceHash, "hash не зависит от переводов строк");
            Assert.AreEqual(a.CatalogHash, b.CatalogHash);
            Assert.AreEqual("outpost-2.1-b4", a.Balance.Version);
            Assert.AreEqual(OutpostIds.BalanceVersion, b.Balance.Version);

            Assert.AreEqual(Rational.Of(59, 50), Rational.Parse("1.18"));
            Assert.AreEqual(Rational.Of(6, 5), Rational.Parse("6/5"));
            Assert.AreEqual("-3/20", Rational.Parse("-0.15").ToString());
            Assert.AreEqual(new BigInteger(3), Rational.Of(5, 2).RoundHalfUp());
            Assert.AreEqual(new BigInteger(-2), Rational.Of(-3, 2).Floor());
            Assert.AreEqual(new BigInteger(-1), Rational.Of(-3, 2).Ceil());
            Assert.AreEqual("1.23K", MoneyFormat.Format(1234));
            Assert.AreEqual("45K", MoneyFormat.Format(45_000));
            Assert.AreEqual("4.5M", MoneyFormat.Format(4_500_000));
            var big = BigInteger.Pow(2, 80) + 1;
            Assert.AreEqual(big, MoneyFormat.Parse(MoneyFormat.ToStorage(big)), "строка сохранения без потери единицы выше 2^53");
        }
        [Test]
        public void PurchasesWithBot_SaveLoadAtEventBoundary_IsByteIdentical()
        {
            var cfg = Configs;
            var map = OutpostLevelAdapter.Build(TestData.Balance, 3, cfg.Overrides);
            string Run(bool split)
            {
                var st = StageProgressionService.NewGame(TestData.Balance, cfg, 11, 0, 3);
                var sim = new OutpostSimulation(st, map, cfg) { RecordEvents = false };
                var bot = new OutpostBot(BotPolicy.C);
                bot.ApplyPolicy(sim);
                bool reloaded = false;
                while (!sim.IsEconomyStopped && sim.Now < 150 * OutpostIds.TicksPerSecond)
                {
                    if (split && !reloaded && sim.Now >= 60 * OutpostIds.TicksPerSecond)
                    {
                        st = OutpostSaveService.DeserializeState(OutpostSaveService.SerializeState(st));
                        sim = new OutpostSimulation(st, map, cfg) { RecordEvents = false };
                        reloaded = true;
                    }
                    Assert.IsTrue(sim.AdvanceToNextEvent());
                    bot.ApplyPolicy(sim);
                }
                CollectionAssert.IsEmpty(OutpostInvariants.Check(st, map, cfg.Equipment));
                Assert.Greater(bot.Purchases, 0);
                return OutpostSaveService.SerializeState(st);
            }
            Assert.AreEqual(Run(false), Run(true));
        }

        [Test]
        public void BootsPurchaseMidEdge_KeepsExactProgress()
        {
            var cfg = Configs;
            var map = OutpostLevelAdapter.Build(TestData.Balance, 1, cfg.Overrides);
            var st = StageProgressionService.NewGame(TestData.Balance, cfg, 1, 0, 1);
            // Тестовое золото с согласованным учётом (как будто выпущено и сдано): иначе инварианты ловят «скрытые монеты».
            st.Run.Wallet += 1000;
            st.Run.TotalCredited += 1000;
            st.Run.Counters.DeliveredValue += 1000;
            st.Run.Counters.ReleasedValue += 1000;
            var sim = new OutpostSimulation(st, map, cfg);
            var w = st.Run.Workers[0];
            Assert.AreEqual(WorkerMode.MovingToWork, w.Mode);
            Assert.AreEqual(400_000L, w.Action.Deadline);
            sim.Advance(100_000);
            var r = sim.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Boots));
            Assert.IsTrue(r.Ok, r.Reason);
            Assert.AreEqual(new BigInteger(25), r.Cost);
            // Скорость 2.7 → ребро ceil(10^6 / 2.7) = 370371; пройдено 1/4 → остаток ceil(3/4 · 370371) = 277779.
            Assert.AreEqual(Rational.Of(1, 4), w.Action.P0);
            Assert.AreEqual(370_371L, w.Action.Duration);
            Assert.AreEqual(100_000L + 277_779L, w.Action.Deadline);
            // Save/load посреди ребра восстанавливает прежний срок без нового округления.
            var copy = OutpostSaveService.DeserializeState(OutpostSaveService.SerializeState(st));
            Assert.AreEqual(377_779L, copy.Run.Workers[0].Action.Deadline);
            var simCopy = new OutpostSimulation(copy, map, cfg);
            sim.Advance(5_000_000);
            simCopy.Advance(5_000_000);
            Assert.AreEqual(OutpostSaveService.SerializeState(st), OutpostSaveService.SerializeState(copy));
            CollectionAssert.IsEmpty(OutpostInvariants.Check(st, map, cfg.Equipment));
        }
    }
}
