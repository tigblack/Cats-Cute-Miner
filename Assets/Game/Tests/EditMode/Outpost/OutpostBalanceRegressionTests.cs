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
        // Калибровка темпа b6 — по целям источника, без обстановки шахт (её темп меряет MineDressingTests.Report_SamplePacing).
        static OutpostConfigSet Configs => s_Configs ?? (s_Configs = OutpostConfigSet.LoadDefault().WithCards(TestData.Balance).WithDressing(null));

        /// <summary>Профиль b3 (до аудита): без удорожания по глубине, прежние HP, разгрузка и предел «Оценки».</summary>
        static OutpostBalanceConfig B3()
        {
            var b = Configs.Balance.Clone();
            b.Version = "outpost-2.1-b3";
            b.PaceScale = null; // снимки до b6 без множителя темпа шахт (решение владельца В4, 15 октября 2026)
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
            // ТЗ 2.2 §9, §10.1, §16.1 (б): профиль outpost-2.2-b5 — числа b4 без изменений, меняется только Version. Профиль b6
            // (решение владельца В4, 15 октября 2026): те же числа и множитель темпа шахт PaceScale; b7 — шестичасовой средний участок
            // шахты 7; b8 — первая сессия («Места разгрузки», пороги «Обуви», «Сумок», «Разгрузки») — проверки ниже прежние.
            Assert.AreEqual("outpost-2.2-b8", b.Version);
            Assert.AreEqual(OutpostIds.BalanceVersion, b.Version);
            Assert.AreEqual(Rational.Of(1, 10), b.CostZoneStep);
            Assert.AreEqual(Rational.Of(1, 25), b.ZoneHpStep);
            Assert.AreEqual(Rational.Of(1, 5), b.UnloadStep);
            Assert.AreEqual(40, b.Branch(UpgradeBranch.Value).Cap);
            CollectionAssert.AreEqual(new[] { 10, 25, 40 }, b.Branch(UpgradeBranch.Value).Milestones);
        }

        /// <summary>
        /// NextCost(z, L) = ceil(S(z) · (1 + (z−1)/10) · K(z) · BaseCost · Growth^L): в шахте 1 цены прежние (ТЗ 12.5), глубже — дороже.
        /// K(z) — множитель темпа профиля b6 (решение владельца В4, 15 октября 2026), в шахтах 1–11 — 1; эталон берёт K прямо из
        /// массива PaceScale встроенного outpost_balance.json.
        /// </summary>
        [Test]
        public void CostZoneFactor_ExactPrices()
        {
            var b = Configs.Balance;
            var pace = (Newtonsoft.Json.Linq.JArray)Newtonsoft.Json.Linq.JObject.Parse(OutpostConfigSet.DefaultBalanceJson)["PaceScale"];
            Rational K(int zone) => Rational.Parse((string)pace[zone - 1]);
            Assert.AreEqual(Rational.One, K(10));
            Assert.AreEqual(new BigInteger(20), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 0));
            Assert.AreEqual(new BigInteger(24), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 1));
            Assert.AreEqual(new BigInteger(89), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 9));
            Assert.AreEqual(new BigInteger(105), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 10));
            Assert.AreEqual(new BigInteger(50), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Team, 0));
            // Все семь веток профиля b8: у «Мест разгрузки» cap 2 — цены уровней 0 и 1, на cap цены нет.
            foreach (int zone in new[] { 2, 10, 50, 144 })
                foreach (UpgradeBranch br in Enum.GetValues(typeof(UpgradeBranch)))
                {
                    var cfg = b.Branch(br);
                    int levels = Math.Min(5, cfg.Cap);
                    for (int level = 0; level < levels; level++)
                    {
                        var expected = (Rational.Of(6, 5).Pow(zone - 1) * (Rational.One + Rational.Of(zone - 1, 10)) * K(zone) * cfg.BaseCost * cfg.Growth.Pow(level)).Ceil();
                        Assert.AreEqual(expected, TeamUpgradeService.NextCost(b, zone, br, level), br + " z" + zone + " L" + level);
                    }
                    if (levels < 5) Assert.IsNull(TeamUpgradeService.NextCost(b, zone, br, cfg.Cap), br + " z" + zone + ": на cap цены нет");
                }
            Assert.AreEqual(2, b.Branch(UpgradeBranch.Bays).Cap);
            // Скидка карточек входит в то же произведение до единственного ceil.
            var discounted = TeamUpgradeService.NextCost(b, 50, UpgradeBranch.Boots, 3, Rational.Of(1, 10));
            Assert.AreEqual((Rational.Of(6, 5).Pow(49) * Rational.Of(59, 10) * K(50) * 25 * Rational.Of(13, 10).Pow(3) * Rational.Of(9, 10)).Ceil(), discounted);
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
                // Профиль b6: груз шахты — после множителя темпа K(z), как в run (цены веток тоже × K; решение владельца В4).
                var tb = OutpostLevelBalanceAdapter.ApplyPace(map, b, OutpostLevelBalanceAdapter.Compute(map, b, TestData.Balance));
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
            // ТЗ 2.2 §16.1 (б): следующая шахта — на текущем профиле (b8: числа b4, множитель темпа b6 и ветки первой сессии).
            Assert.AreEqual("outpost-2.2-b8", next.Run.Balance.Version);
            Assert.AreEqual(OutpostIds.BalanceVersion, next.Run.Balance.Version);
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
        /// ТЗ 2.2 §9: бот доставляет готовые партии и продаёт товар у всех политик (A по-прежнему ничего не покупает); инварианты
        /// (в том числе учёт товара §10.2 и кошелёк с GoodsSoldGold §6.4) держатся на каждой отметке. Шахты 1–12 политики C.
        /// </summary>
        [Test]
        public void Bot_DeliversAndSellsGoods_InvariantsEveryTick()
        {
            var reports = OutpostBot.RunCampaign(TestData.Balance, Configs, BotPolicy.C, 5, 12, true, b => b.CheckInvariantsEveryTick = true);
            Assert.AreEqual(12, reports.Count, reports.Last().Error + " " + string.Join("; ", reports.Last().InvariantErrors));
            foreach (var r in reports)
            {
                Assert.IsTrue(r.Success, r.ToString() + " " + r.Error + " " + string.Join("; ", r.InvariantErrors));
                Assert.AreEqual(r.ValueCreated, r.Credited, "Credited — только монеты (§6.4)");
                if (r.Zone == 1)
                {
                    Assert.AreEqual((0, "0", 0), (r.Factories, r.GoodsSoldGold, r.ManualDeliveries), "в шахте 1 заводов нет");
                    continue;
                }
                Assert.Greater(r.Factories, 0, r.ZoneId);
                Assert.Greater(r.FactoryCycles, 0, r.ZoneId + ": заводы работали");
                Assert.That(BigInteger.Parse(r.GoodsSoldGold), Is.GreaterThan(BigInteger.Zero), r.ZoneId + ": бот продал товар");
                Assert.Greater(r.GoodsSoldUnits, 0, r.ZoneId);
            }
            Assert.Greater(reports.Sum(r => r.ManualDeliveries), 0, "ручные доставки");

            // Политика A: доставка и продажа без покупок.
            var a = OutpostBot.RunZone(TestData.Balance, Configs, 3, BotPolicy.A, 42, setup: b => b.CheckInvariantsEveryTick = true);
            Assert.IsTrue(a.Success, a.ToString() + " " + a.Error + " " + string.Join("; ", a.InvariantErrors));
            Assert.AreEqual("0", a.Spent);
            Assert.That(BigInteger.Parse(a.GoodsSoldGold), Is.GreaterThan(BigInteger.Zero));
            Assert.Greater(a.ManualDeliveries, 0);
            // Без заводов в поведении бота товар не продаётся.
            var off = OutpostBot.RunZone(TestData.Balance, Configs, 3, BotPolicy.A, 42, setup: b => b.ManageFactories = false);
            Assert.IsTrue(off.Success, off.ToString());
            Assert.AreEqual(("0", 0), (off.GoodsSoldGold, off.ManualDeliveries));
            Assert.AreEqual(a.Credited, off.Credited, "продажа не меняет монеты");
        }

        /// <summary>
        /// ТЗ 2.2 §9: модели аудита на каждой проверке доставляют и продают товар (none — нет); отчёт шахты разделяет монеты и
        /// товар, считает заводы, циклы и автодоставку; опыт отсутствия детерминирован и не меняет основной прогон.
        /// </summary>
        [Test]
        public void AuditModels_TendFactories_ReportGoods_AbsenceIsolated()
        {
            CampaignAudit Run(double[] away) =>
                BalanceAudit.RunCampaign(TestData.Balance, Configs, BalanceAudit.Model("free"), 3, 4, absenceHours: away);
            var plain = Run(null);
            var withAway = Run(new[] { 1.0 });
            Assert.IsTrue(plain.Success && withAway.Success);
            for (int i = 0; i < plain.Mines.Count; i++)
            {
                var m = plain.Mines[i];
                var w = withAway.Mines[i];
                Assert.AreEqual(m.CompletedSec, w.CompletedSec, "опыт отсутствия идёт на копии и не меняет кампанию");
                Assert.AreEqual(m.GoodsSoldBase, w.GoodsSoldBase);
                Assert.IsNull(m.Absences);
                if (m.Zone == 1)
                {
                    Assert.AreEqual(0, m.Factories.Count);
                    Assert.AreEqual(0.0, m.GoodsSoldBase);
                    continue;
                }
                Assert.Greater(m.Factories.Count, 0, "заводы шахты " + m.Zone);
                Assert.Greater(m.GoodsSoldBase, 0, "free продаёт товар, шахта " + m.Zone);
                Assert.Greater(m.Sales, 0);
                Assert.Greater(m.Factories.Sum(f => f.Cycles), 0);
                Assert.IsTrue(m.Factories.All(f => f.UnlockSec != null && f.UnitsStart >= 20 && f.PriceBase > 0 && f.RequiredCardId != null));
                Assert.IsTrue(m.Factories.All(f => !f.AutoAtEnd || f.AutoSec != null), "AutoSec у каждого завода на автомате");
                var a = w.Absences.Single();
                Assert.AreEqual(1.0, a.Hours);
                Assert.AreEqual(m.CompletedSec.Value / 2, a.StartSec, 1e-6, "уход на 50 % длительности");
                Assert.AreEqual(a.StartSec + 3600, a.ReturnSec, 1e-6);
                Assert.AreEqual("Completed", a.Outcome);
                Assert.AreEqual(a.Factories, a.Locked + a.AutoFactories + a.ManualFactories);
            }
            Assert.AreEqual(OutpostJson.Serialize(withAway.Mines.Select(m => m.Absences).ToList()),
                OutpostJson.Serialize(Run(new[] { 1.0 }).Mines.Select(m => m.Absences).ToList()), "детерминизм");

            // none: ни доставок, ни продаж.
            var state = StageProgressionService.NewGame(TestData.Balance, Configs, 5, 0, 3);
            var sim = new OutpostSimulation(state, OutpostLevelAdapter.ForRun(TestData.Balance, state.Run), Configs);
            var none = BalanceAudit.RunMine(sim, BalanceAudit.Model("none"), "none");
            Assert.AreEqual("Completed", none.Outcome, none.Error);
            Assert.AreEqual((0.0, 0, 0), (none.GoodsSoldBase, none.ManualDeliveries, none.Sales));
        }

        /// <summary>
        /// Темп кампании эталонного игрока (модель free: «База» раз в 30 с, узкое место, карточки/детали/«Надеть лучшее» между
        /// шахтами; на каждой проверке — доставка готовых партий и продажа товара, ТЗ 2.2 §9).
        /// <para>
        /// Профиль b6 (решение владельца В4, 15 октября 2026; ТЗ 2.2 §9, ТЗ 2.1 §12.6; Docs/Rework/BALANCE_FACTORIES.md «Темп b6:
        /// 3 цикла»): шахта, где есть медленные заводы, длится около трёх циклов самого медленного завода, D(z) ≈ 3·C(z); шахты, где
        /// 3·C(z) не длиннее b5, и шахты 1–11 (playable) не меняются (K = 1). Кампания b6 — около 50 дней логического времени,
        /// поэтому тест в два шага:
        /// 1) кампания free seed 1 на профиле без множителя темпа (K = 1, числа b5) по 144 шахтам: нет тупиков (audit.Success),
        ///    калибровка §9 (медиана шахт 2–5 в 4,22–5,70 мин), этапы 1 и 2–10 в ориентирах ТЗ 2.1 §12.6 (их шахты b6 не меняет),
        ///    ни одной шахты с отрезком без доступной покупки дольше 2 мин — в растянутой шахте это предел 120·K(z) с;
        /// 2) шахты выборки с K(z) &gt; 1 проходятся заново из записанного начала на действующем профиле b6
        ///    (<see cref="BalanceAudit.RunMineFrom"/>; награды между шахтами от длительности не зависят): D / 3·C(z) в 0,75–1,25,
        ///    последняя покупка не раньше 80 % шахты, отрезок без доступной покупки короче 120·K(z) с и 25 % шахты.
        /// Предел 2,2 для соседних шахт и «кривая этапов ≥ 0,95» проверяются для шахт с K = 1 (1–11, ТЗ 2.1 §12.6); к шахтам
        /// с K &gt; 1 они не относятся (длительность следует за циклами заводов).
        /// </para>
        /// </summary>
        [Test, Category("OutpostSlow"), Timeout(1800000)]
        public void Campaign_FreePlayer_PacingWithinStageTargets()
        {
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            var rules = Configs.WithoutPace();
            var starts = new Dictionary<int, OutpostGameState>();
            var audit = BalanceAudit.RunCampaign(TestData.Balance, rules, BalanceAudit.Model("free"), 1,
                beforeZone: (z, st) => { if (PaceSample.Contains(z)) starts[z] = OutpostJson.DeepCopy(st); });
            Assert.IsTrue(audit.Success, string.Join("; ", audit.Mines.Where(m => m.Outcome != "Completed").Select(m => m.Zone + ": " + m.Error)));
            Assert.AreEqual(144, audit.Mines.Count);

            // Калибровка ТЗ 2.2 §9 (G4): медиана шахт 2–5, seed 1 (K = 1 — те же шахты в b6).
            double calibration = OutpostBalanceAuditTests.CalibrationMedian(new[] { audit });
            TestContext.WriteLine("zones 2-5 (calibration, seed 1): median " + calibration.ToString("0.00", ic) + " min, target 4.22-5.70 (b4 4.96)");
            Assert.That(calibration, Is.InRange(4.22, 5.70), "ТЗ 2.2 §9: медиана шахт 2–5 у free в пределах ±15 % от b4");

            // Этапы ТЗ 2.1 §12.6, где все шахты с K = 1 (b6 их не меняет): ориентиры снова обязательны. Шахта 1 — первая сессия
            // (Docs/Rework/FIRST_SESSION.md): до шахты 2 открыты только Mining Power и Hire Cat, поэтому шахта целиком длиннее
            // прежнего (6,5 мин у free на этом профиле; спасение Mia — в ориентире 3–5 мин), верх этапа 1 — 7 мин, как у этапа 2–10.
            var stages = new[] { (1, 1, 3.0, 7.0), (2, 10, 3.0, 7.0) };
            double previous = 0;
            foreach (var (from, to, lo, hi) in stages)
            {
                for (int z = from; z <= to; z++) Assert.AreEqual(Rational.One, Configs.Balance.ZonePace(z), "шахта " + z + ": K = 1");
                var minutes = audit.Mines.Where(m => m.Zone >= from && m.Zone <= to).Select(m => m.CompletedSec.Value / 60).OrderBy(x => x).ToList();
                double median = minutes[minutes.Count / 2];
                TestContext.WriteLine(string.Format(ic, "zones {0}-{1}: median {2:0.00} min (TZ 2.1 §12.6 target {3}-{4})", from, to, median, lo, hi));
                Assert.That(median, Is.InRange(lo, hi), "ТЗ 2.1 §12.6: этап " + from + "–" + to);
                // Шахта 1 — учебная шахта первой сессии с двумя ветками: кривая этапов сравнивается начиная с этапа 2–10.
                if (from > 2) Assert.GreaterOrEqual(median, previous * 0.95, "кривая этапов не проваливается");
                previous = median;
            }

            // ТЗ 2.1 §12.6: отношение соседних шахт не больше 2,2 — снова обязательно для шахт с K = 1 (1–11; было отключено в b5
            // «до нового баланса владельца», решение владельца В4 15 октября 2026 — новый баланс b6).
            for (int z = 2; z <= 11; z++)
            {
                Assert.AreEqual(Rational.One, Configs.Balance.ZonePace(z), "шахта " + z + ": K = 1");
                Assert.Less(audit.Mines[z - 1].CompletedSec.Value / audit.Mines[z - 2].CompletedSec.Value, 2.2, "скачок шахта " + z);
            }

            // Отрезок без доступной покупки: в шахте с K(z) он растягивается в K раз — предел 120·K(z) с (b5: 120 с).
            var slow = audit.Mines.OrderByDescending(m => m.MaxUnaffordableSec).First();
            TestContext.WriteLine(string.Format(ic, "max unaffordable (K = 1 profile): {0:0.0} s (zone {1})", slow.MaxUnaffordableSec, slow.Zone));
            foreach (var m in audit.Mines)
                Assert.Less(m.MaxUnaffordableSec, 120, "шахта " + m.Zone + " (K = 1): слишком долго без доступной покупки");

            // Шахты выборки на профиле b6 из записанного начала.
            var zones = PaceSample.ToArray();
            var reports = new MineAudit[zones.Length];
            System.Threading.Tasks.Parallel.For(0, zones.Length, i =>
            {
                var repo = BalanceRepository.Load(TestData.BalanceBytes);
                var configs = OutpostConfigSet.LoadDefault().WithCards(repo).WithDressing(null);
                reports[i] = BalanceAudit.RunMineFrom(starts[zones[i]], repo, configs, BalanceAudit.Model("free"));
            });
            for (int i = 0; i < zones.Length; i++)
            {
                int z = zones[i];
                var m = reports[i];
                Assert.AreEqual("Completed", m.Outcome, "шахта " + z + ": " + m.Error);
                double k = Configs.Balance.ZonePace(z).ToDouble();
                double cycle = m.Factories.Max(f => (double)f.CycleSec);
                double d = m.CompletedSec.Value;
                double last = m.PurchaseLog.Max(p => p.Sec);
                double goods = m.GoodsSoldBase / Math.Max(1e-9, m.GoodsSoldBase + m.CreditedBase);
                TestContext.WriteLine(string.Format(ic,
                    "zone {0}: K {1:0.00}, D {2:0.00} h, 3C {3:0.00} h, D/3C {4:0.00}; last purchase {5:0}% D; max unaffordable {6:0} s ({7:0.0}% D); goods {8:0}%; base {9:0.0} min",
                    z, k, d / 3600, 3 * cycle / 3600, d / (3 * cycle), 100 * last / d, m.MaxUnaffordableSec, 100 * m.MaxUnaffordableSec / d,
                    100 * goods, audit.Mines[z - 1].CompletedSec.Value / 60));
                Assert.Greater(k, 1.0, "шахта " + z + ": выборка — шахты с K > 1");
                Assert.That(d / (3 * cycle), Is.InRange(0.75, 1.25), "шахта " + z + ": D ≈ 3 циклам самого медленного завода ±25 %");
                Assert.GreaterOrEqual(last / d, 0.8, "шахта " + z + ": покупки идут до 80 % шахты");
                Assert.Less(m.MaxUnaffordableSec, 120 * k, "шахта " + z + ": отрезок без доступной покупки ≥ 120·K(z) с");
                Assert.Less(m.MaxUnaffordableSec, 0.25 * d, "шахта " + z + ": отрезок без доступной покупки ≥ 25 % шахты");
                Assert.Greater(m.GoodsSoldBase, 0, "шахта " + z + ": товар продаётся");
            }
        }

        /// <summary>Выборка шахт темпа b6 в тесте (полная калибровка — OutpostBalanceAuditTests.Audit_Pace).</summary>
        static readonly int[] PaceSample = { 19, 31, 47, 68, 93, 114, 134, 144 };

        /// <summary>
        /// Слой темпа b6 (решение владельца В4, 15 октября 2026; Docs/Rework/BALANCE_FACTORIES.md «Темп b6: 3 цикла»):
        /// K(z) = 1 в шахтах 1–11 (playable не меняется); у run шахты с K &gt; 1 HP целей r/x/p — ceil(база §12.4 · K), груз —
        /// max(2, ceil(HP / 10)), цепи заводов (s) — без изменений и без груза; цены веток — ceil(S·CostZoneFactor·K·BaseCost·Growth^L);
        /// снимок без PaceScale (профили до b6) — K = 1; K &lt; 1 конфиг не принимает.
        /// </summary>
        [Test]
        public void Pace_B6_RunTargetsAndPrices()
        {
            var b6 = Configs.Balance;
            Assert.IsNotNull(b6.PaceScale);
            Assert.AreEqual(OutpostIds.LastZone, b6.PaceScale.Count, "PaceScale на все 144 шахты");
            for (int z = 1; z <= 11; z++) Assert.AreEqual(Rational.One, b6.ZonePace(z), "шахта " + z + " (playable): K = 1");
            for (int z = 1; z <= OutpostIds.LastZone; z++) Assert.IsTrue(b6.ZonePace(z).CompareTo(Rational.One) >= 0, "шахта " + z + ": K ≥ 1");
            Assert.Greater(b6.ZonePace(144).ToDouble(), 50.0, "шахта 144 — около суток");

            var basic = b6.WithoutPace();
            Assert.AreEqual(Rational.One, basic.ZonePace(144));
            foreach (int zone in new[] { 5, 47, 144 })
            {
                var k = b6.ZonePace(zone);
                var map = OutpostLevelAdapter.BuildMine(TestData.Balance, zone, Configs);
                var baseHp = OutpostLevelBalanceAdapter.Compute(map, b6, TestData.Balance);
                var state = StageProgressionService.NewGame(TestData.Balance, Configs, 7, 0, zone);
                var run = state.Run;
                Assert.AreEqual(map.Targets.Length, run.Targets.Count);
                for (int i = 0; i < map.Targets.Length; i++)
                {
                    var t = map.Targets[i];
                    long hp = t.IsFactory ? baseHp[i].MaxHp : (long)(k * baseHp[i].MaxHp).Ceil();
                    int units = t.IsFactory ? 0 : (int)Math.Max(2L, (hp + 9) / 10);
                    Assert.AreEqual(hp * OutpostIds.HpUnitsPerHp, run.Targets[i].MaxHpUnits, "шахта " + zone + ", цель " + t.Id + " (" + t.Kind + ")");
                    Assert.AreEqual(units, run.Targets[i].TotalUnits, "шахта " + zone + ", груз цели " + t.Id);
                }
                // Все семь веток профиля b8: «Места разгрузки» (cap 2) — уровни 0 и 1, на cap цены нет.
                foreach (UpgradeBranch br in Enum.GetValues(typeof(UpgradeBranch)))
                {
                    int levels = Math.Min(3, b6.Branch(br).Cap);
                    for (int level = 0; level < levels; level++)
                        Assert.AreEqual((basic.PriceScale(zone) * k * b6.Branch(br).BaseCost * b6.Branch(br).GrowthPow(level)).Ceil(),
                            TeamUpgradeService.NextCost(b6, zone, br, level), "шахта " + zone + " " + br + " L" + level);
                    if (levels < 3) Assert.IsNull(TeamUpgradeService.NextCost(b6, zone, br, levels), "шахта " + zone + " " + br + ": на cap цены нет");
                }
            }

            // Снимок профиля до b6 (без поля PaceScale) читается как K = 1.
            var json = Newtonsoft.Json.Linq.JObject.Parse(OutpostJson.Serialize(b6));
            json.Remove("PaceScale");
            var old = OutpostJson.Deserialize<OutpostBalanceConfig>(json.ToString());
            Assert.IsNull(old.PaceScale);
            Assert.AreEqual(Rational.One, old.ZonePace(144));
            Assert.AreEqual(TeamUpgradeService.NextCost(basic, 144, UpgradeBranch.Pickaxes, 3), TeamUpgradeService.NextCost(old, 144, UpgradeBranch.Pickaxes, 3));

            var broken = b6.Clone();
            broken.PaceScale[30] = Rational.Of(1, 2);
            Assert.Throws<OutpostConfigException>(() => broken.Validate());
        }
    }
}
