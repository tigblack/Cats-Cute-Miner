using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Outpost;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Локальные улучшения и смена экипировки во время шахты (ТЗ §6.5, §7.3, §9, §10.3, §12.2, §12.5, §14, §20
    /// «Обычные улучшения», «Пакетная покупка», «Замена полной сумки», «Смена шляпы»). Цены сверяются с независимой
    /// формулой ceil(S(z)·CostZoneFactor(z)·BaseCost·Growth^L) профиля b4, с профиля b6 — ещё × K(z) из PaceScale (решение
    /// владельца В4, 15 октября 2026; шахты 1–11 — K = 1); золото для покупок даёт фикстура GiveGold (согласованно с инвариантами §13).
    /// </summary>
    public class OutpostUpgradeTests
    {
        static readonly BigInteger Rich = BigInteger.Pow(10, 40);

        /// <summary>Симуляция зоны после at тиков работы (рёбра и обслуживание в процессе) с добавленным золотом.</summary>
        static OutpostSimulation WithGold(OutpostFixtureB fx, BigInteger gold, long at)
        {
            var sim = fx.NewSim();
            if (at > 0) sim.Advance(at);
            var st = Clone(sim.State);
            GiveGold(st, gold);
            return fx.Wrap(st);
        }

        static readonly BuyMode[] Modes = { BuyMode.One, BuyMode.Ten, BuyMode.Max };

        // ───────────────────────────── Контроль §12.5 ─────────────────────────────

        [Test]
        public void Zone1Controls_StartStats_FirstPickaxe20_Damage6_1_Next24()
        {
            // ТЗ §12.5: «два котика, урон 5, скорость 2.5, сумка 4, разгрузка 2 порции/с, стоимость первой Кирки — 20.
            // После покупки: урон 6.1; следующая цена — 24».
            var fx = Real(1);
            var sim = WithGold(fx, 20, 0);
            Assert.AreEqual(2, StatWorkers(sim));
            Assert.AreEqual(Damage0, StatDamageUnits(sim));
            Assert.AreEqual(Edge0, StatEdgeTicks(sim));
            Assert.AreEqual(4, StatCapacity(sim));
            Assert.AreEqual(Unload0, StatUnloadTicks(sim));
            var q = Quote(sim, UpgradeBranch.Pickaxes, BuyMode.One);
            Assert.AreEqual(1, q.Levels);
            Assert.AreEqual(new BigInteger(20), q.Cost);
            var r = sim.Submit(Buy(UpgradeBranch.Pickaxes));
            Assert.IsTrue(r.Ok, r.Reason);
            Assert.AreEqual(new BigInteger(20), r.Cost);
            Assert.AreEqual(6_100_000L, StatDamageUnits(sim), "5·1.22 = 6.1");
            Assert.AreEqual(BigInteger.Zero, sim.State.Run.Wallet);
            Assert.AreEqual(new BigInteger(20), sim.State.Run.TotalSpent);
            Assert.AreEqual(new BigInteger(24), NextCostOracle(1, UpgradeBranch.Pickaxes, 1));
            var st = Clone(sim.State);
            GiveGold(st, 24);
            var sim2 = fx.Wrap(st);
            Assert.AreEqual(new BigInteger(24), Quote(sim2, UpgradeBranch.Pickaxes, BuyMode.One).Cost);
        }

        // ───────────────────────────── Пакетные покупки ─────────────────────────────

        [TestCase(UpgradeBranch.Pickaxes, 1)]
        [TestCase(UpgradeBranch.Boots, 1)]
        [TestCase(UpgradeBranch.Bags, 1)]
        [TestCase(UpgradeBranch.Unload, 1)]
        [TestCase(UpgradeBranch.Value, 1)]
        [TestCase(UpgradeBranch.Pickaxes, 37)]
        [TestCase(UpgradeBranch.Boots, 37)]
        [TestCase(UpgradeBranch.Unload, 37)]
        [TestCase(UpgradeBranch.Value, 37)]
        public void BuyTen_TotalEqualsSumOfSequentialOnes_FinalStateIdentical(UpgradeBranch branch, int zone)
        {
            // ТЗ §9: «Стоимость пакетной покупки равна сумме точных последовательных цен. Последовательные бонусы на порогах
            // учитываются»; §20 «Пакетная покупка: итог совпадает с соответствующими последовательными покупками».
            var fx = Real(zone);
            string start = Json(WithGold(fx, Rich, 20 * S).State);
            BigInteger expected = SumCostOracle(zone, branch, 0, 10);

            var batch = fx.Load(start);
            var q = Quote(batch, branch, BuyMode.Ten);
            Assert.AreEqual(10, q.Levels);
            Assert.AreEqual(expected, q.Cost, "×10 = Σ ceil(S(z)·CostZoneFactor(z)·K(z)·Base·Growth^L), L = 0..9");
            var r = batch.Submit(Buy(branch, BuyMode.Ten));
            Assert.IsTrue(r.Ok, r.Reason);
            Assert.AreEqual(10, r.Levels);
            Assert.AreEqual(expected, r.Cost);

            var seq = fx.Load(start);
            BigInteger paid = 0;
            for (int level = 0; level < 10; level++)
            {
                var one = seq.Submit(Buy(branch));
                Assert.IsTrue(one.Ok, one.Reason);
                Assert.AreEqual(NextCostOracle(zone, branch, level), one.Cost, "цена уровня " + level);
                paid += one.Cost;
            }
            Assert.AreEqual(expected, paid);
            Assert.AreEqual(10, batch.State.Run.Levels[(int)branch]);
            AssertSameJson(Json(seq.State), Json(batch.State), "×10 и десять ×1 — одно состояние (включая точный остаток рёбер/порций)");
            AssertInvariants(batch, "×10");
        }

        [TestCase(UpgradeBranch.Pickaxes)]
        [TestCase(UpgradeBranch.Boots)]
        [TestCase(UpgradeBranch.Bags)]
        [TestCase(UpgradeBranch.Unload)]
        [TestCase(UpgradeBranch.Value)]
        public void BuyMax_ReachesCap_ExactCostBeyond2Pow53_ThenCapBlocks(UpgradeBranch branch)
        {
            // ТЗ §9: «Макс покупает все доступные уровни до cap… Покупка выше максимума невозможна»; zone144 — суммы > 2^53
            // списываются точно (§12.2, §20 «Большие целые»). Профиль b6 (решение владельца В4, 15 октября 2026): цены × K(144),
            // эталон NextCostOracle учитывает K; пределы веток прежние.
            const int zone = 144;
            var fx = Real(zone);
            var sim = WithGold(fx, Rich, 10 * S);
            int cap = CapOf(branch);
            BigInteger expected = SumCostOracle(zone, branch, 0, cap);
            var q = Quote(sim, branch, BuyMode.Max);
            Assert.AreEqual(cap, q.Levels);
            Assert.AreEqual(expected, q.Cost);
            BigInteger before = sim.State.Run.Wallet;
            var r = sim.Submit(Buy(branch, BuyMode.Max));
            Assert.IsTrue(r.Ok, r.Reason);
            Assert.AreEqual(cap, sim.State.Run.Levels[(int)branch]);
            Assert.AreEqual(before - expected, sim.State.Run.Wallet, "списание точно до единицы");
            Assert.AreEqual(expected, sim.State.Run.TotalSpent);
            if (branch == UpgradeBranch.Value) Assert.Greater(expected, BigInteger.Pow(2, 53));
            string json = Json(sim.State);
            foreach (var mode in Modes)
            {
                var blocked = Quote(sim, branch, mode);
                Assert.AreEqual(0, blocked.Levels, mode.ToString());
                Assert.AreEqual("cap", blocked.BlockReason, mode.ToString());
                var fail = sim.Submit(Buy(branch, mode));
                Assert.IsFalse(fail.Ok);
                Assert.AreEqual("cap", fail.Reason);
            }
            AssertSameJson(json, Json(sim.State), "отказ не меняет состояние (§8)");
            AssertInvariants(sim, "cap");
        }

        [Test]
        public void BuyTen_BuysOnlyAffordableLevels()
        {
            // ТЗ §9: «×10 покупает до десяти доступных уровней: столько, сколько одновременно позволяют кошелёк и cap».
            var fx = Real(1);
            BigInteger three = SumCostOracle(1, UpgradeBranch.Pickaxes, 0, 3);
            BigInteger fourth = NextCostOracle(1, UpgradeBranch.Pickaxes, 3);
            Assert.AreEqual(new BigInteger(20 + 24 + 28), three, "20, 24, 28");
            Assert.AreEqual(new BigInteger(33), fourth);
            var sim = WithGold(fx, three + fourth - 1, 0);
            var q = Quote(sim, UpgradeBranch.Pickaxes, BuyMode.Ten);
            Assert.AreEqual(3, q.Levels);
            Assert.AreEqual(three, q.Cost);
            var qm = Quote(sim, UpgradeBranch.Pickaxes, BuyMode.Max);
            Assert.AreEqual(3, qm.Levels, "«Макс» тоже ограничен кошельком");
            var r = sim.Submit(Buy(UpgradeBranch.Pickaxes, BuyMode.Ten));
            Assert.IsTrue(r.Ok, r.Reason);
            Assert.AreEqual(3, sim.State.Run.Levels[(int)UpgradeBranch.Pickaxes]);
            Assert.AreEqual(fourth - 1, sim.State.Run.Wallet);
            var none = Quote(sim, UpgradeBranch.Pickaxes, BuyMode.Ten);
            Assert.AreEqual(0, none.Levels);
            Assert.AreEqual("gold", none.BlockReason, "доступных уровней ноль — кнопка неактивна");
        }

        [Test]
        public void BuyTen_StopsAtCap_MilestoneAtCapApplies()
        {
            // ТЗ §9 cap; §12.2: «Порог на cap действует при покупке последнего уровня» (Кирки 20; «Оценка» 40 в профиле b4).
            var sim = WithGold(Real(1), Rich, 0);
            for (int i = 0; i < 15; i++) Assert.IsTrue(sim.Submit(Buy(UpgradeBranch.Pickaxes)).Ok);
            var q = Quote(sim, UpgradeBranch.Pickaxes, BuyMode.Ten);
            Assert.AreEqual(5, q.Levels, "до cap 20 осталось 5");
            Assert.AreEqual(SumCostOracle(1, UpgradeBranch.Pickaxes, 15, 5), q.Cost);
            Assert.IsTrue(sim.Submit(Buy(UpgradeBranch.Pickaxes, BuyMode.Ten)).Ok);
            Assert.AreEqual(20, sim.State.Run.Levels[(int)UpgradeBranch.Pickaxes]);
            Assert.AreEqual(DamageUnitsOracle(5, 20, Zero), StatDamageUnits(sim), "L20: множитель 4");
        }

        [Test]
        public void NoDeliveredGold_EveryPurchaseBlocked_StateUnchanged()
        {
            // ТЗ §7.3 «Только сданные монеты доступны для покупок»; §14 причина «не хватает доставленного золота».
            var sim = Real(1).NewSim();
            string json = Json(sim.State);
            foreach (var branch in PolicyCOrder)
                foreach (var mode in Modes)
                {
                    if (branch == UpgradeBranch.Team && mode != BuyMode.One) continue;
                    var q = Quote(sim, branch, mode);
                    Assert.AreEqual(0, q.Levels, branch + " " + mode);
                    Assert.AreEqual("gold", q.BlockReason, branch + " " + mode);
                    var r = sim.Submit(Buy(branch, mode));
                    Assert.IsFalse(r.Ok);
                    Assert.AreEqual("gold", r.Reason);
                }
            AssertSameJson(json, Json(sim.State), "отказ не меняет состояние");
        }

        // ───────────────────────────── Команда ─────────────────────────────

        [Test]
        public void Team_OnlySingleHire_NewWorkerAtSpawnGetsTaskImmediately_CapTwelve()
        {
            // ТЗ §9: «Покупка нового работника мгновенно создаёт котика в базе и включает его в планировщик… Для найма достаточно ×1»;
            // «Они применяются ко всей команде, включая котиков, нанятых позднее»; cap 10 → 12 котиков.
            var fx = Real(1);
            var sim = WithGold(fx, Rich, 10 * S);
            string json = Json(sim.State);
            foreach (var mode in new[] { BuyMode.Ten, BuyMode.Max })
            {
                var bad = sim.Submit(Buy(UpgradeBranch.Team, mode));
                Assert.IsFalse(bad.Ok, "Команда — только ×1 (CORE_SPEC §6)");
                Assert.AreEqual("mode", bad.Reason, "код причины ядра для ×10/Max у Команды");
            }
            AssertSameJson(json, Json(sim.State), "отказ не меняет состояние");

            Assert.IsTrue(sim.Submit(Buy(UpgradeBranch.Boots)).Ok, "сначала «Обувь»: новый котик сразу с общей скоростью");
            Drain(sim);
            var r = sim.Submit(Buy(UpgradeBranch.Team));
            Assert.IsTrue(r.Ok, r.Reason);
            Assert.AreEqual(NextCostOracle(1, UpgradeBranch.Team, 0), r.Cost, "§12.2: 50");
            var run = sim.State.Run;
            Assert.AreEqual(3, run.Workers.Count);
            var hired = W(sim, 3);
            Assert.AreEqual(fx.Map.SpawnCell, hired.Cell, "создан в базе (SpawnCell)");
            Assert.AreNotEqual(WorkerMode.ChoosingTask, hired.Mode);
            Assert.AreNotEqual(WorkerMode.Idle, hired.Mode, "на карте есть работа — задача назначена сразу");
            Assert.Greater(hired.TaskId, 0L);
            if (IsMoving(hired))
                Assert.AreEqual(EdgeTicksOracle(new FracB(5, 2), 1, Zero), hired.Action.Duration, "общая скорость команды");
            Assert.AreEqual(3, StatWorkers(sim));
            Assert.IsTrue(Drain(sim).Any(e => e.Type == OutpostEventType.WorkerHired && e.WorkerId == 3), "событие WorkerHired");
            for (int i = 1; i < 10; i++) Assert.IsTrue(sim.Submit(Buy(UpgradeBranch.Team)).Ok, "найм " + (i + 1));
            Assert.AreEqual(12, sim.State.Run.Workers.Count, "§9: 10 улучшений — 12 котиков");
            CollectionAssert.AreEqual(Enumerable.Range(1, 12).ToArray(), sim.State.Run.Workers.Select(w => w.Id).ToArray());
            var capped = sim.Submit(Buy(UpgradeBranch.Team));
            Assert.IsFalse(capped.Ok);
            Assert.AreEqual("cap", capped.Reason);
            sim.Advance(sim.Now + 30 * S);
            AssertInvariants(sim, "найм");
            Assert.IsTrue(sim.State.Run.Workers.Where(w => w.Id >= 3).Any(w => w.Mode == WorkerMode.Mining || w.Bag.Count > 0), "новые котики работают");
        }

        // ───────────────────────────── Фаза ─────────────────────────────

        [Test]
        public void ReadyToContinue_PurchasesRejectedWithPhase_EquipmentAllowed()
        {
            // ТЗ §14: «Во время ReadyToContinue локальные покупки отключены… Панель экипировки остаётся доступной»; CORE_SPEC §8.
            var fx = Real(1);
            var sim = fx.NewSim();
            RunToReadyOrFail(sim, "zone1");
            Assert.Greater(sim.State.Run.Wallet, NextCostOracle(1, UpgradeBranch.Pickaxes, 0), "золото есть, но фаза запрещает");
            string json = Json(sim.State);
            foreach (var branch in PolicyCOrder)
            {
                var q = Quote(sim, branch, BuyMode.One);
                Assert.AreEqual(0, q.Levels);
                Assert.AreEqual("phase", q.BlockReason, branch.ToString());
                var r = sim.Submit(Buy(branch));
                Assert.IsFalse(r.Ok);
                Assert.AreEqual("phase", r.Reason);
            }
            AssertSameJson(json, Json(sim.State), "отказ не меняет состояние");
            Assert.IsTrue(sim.State.Profile.Items.ContainsKey("pickaxe.common"), "награда zone1");
            var eq = sim.Submit(EquipCmd("pickaxe.common"));
            Assert.IsTrue(eq.Ok, eq.Reason);
            Assert.AreEqual("pickaxe.common", sim.State.Profile.Equipped[(int)EquipSlot.Pickaxe]);
            Assert.AreEqual(RunPhase.ReadyToContinue, sim.State.Run.Phase);
            Assert.AreEqual(FromJson(json).Run.Wallet, sim.State.Run.Wallet);
            Assert.IsTrue(sim.State.Run.Workers.All(w => w.Action == null), "экипировка не запускает экономику");
        }

        // ───────────────────────────── Пересчёт прогресса ─────────────────────────────

        [Test]
        public void Boots_MidEdge_KeepsExactProgress_RemainingIsCeilOfRest()
        {
            // ТЗ §6.5: «Смена обуви… сохраняет долю уже пройденного ребра… новая длительность D даёт остаток ceil((1−p)·D)…
            // При save/load восстанавливаются прогресс, длительность и прежний deadline»; §9 «Скорость не телепортирует по пути».
            var fx = Real(1);
            var st = fx.NewState();
            GiveGold(st, Rich);
            var sim = fx.Wrap(st);
            var w1 = W(sim, 1);
            Assert.IsTrue(IsMoving(w1));
            Assert.AreEqual(0L, w1.Action.SegmentStart);
            Assert.AreEqual(Edge0, w1.Action.Duration);
            Assert.AreEqual(Zero, FracB.Parse(w1.Action.P0));
            long t1 = 123_457;
            Assert.Less(t1, sim.NextEventTick(), "до первой отметки событий");
            sim.Advance(t1);
            Assert.IsTrue(sim.Submit(Buy(UpgradeBranch.Boots)).Ok);
            long d1 = EdgeTicksOracle(new FracB(5, 2), 1, Zero);
            Assert.AreEqual(370_371L, d1, "ceil(1e6 / 2.7)");
            var p1 = new FracB(t1, Edge0);
            w1 = W(sim, 1);
            Assert.AreEqual(ActionKind.Edge, w1.Action.Kind);
            Assert.AreEqual(t1, w1.Action.SegmentStart);
            Assert.AreEqual(d1, w1.Action.Duration);
            Assert.AreEqual(p1, FracB.Parse(w1.Action.P0), "P0 — точная доля пройденного ребра");
            Assert.AreEqual(t1 + (long)((One - p1) * d1).Ceil(), w1.Action.Deadline);
            foreach (var w in sim.State.Run.Workers.Where(IsMoving))
                Assert.AreEqual(d1, w.Action.Duration, "w" + w.Id + ": новая скорость — у всех котиков");

            // Вторая покупка на том же ребре: доля накапливается точно.
            long t2 = t1 + 77_777;
            Assert.Less(t2, w1.Action.Deadline);
            sim.Advance(t2);
            Assert.IsTrue(sim.Submit(Buy(UpgradeBranch.Boots)).Ok);
            long d2 = EdgeTicksOracle(new FracB(5, 2), 2, Zero);
            var p2 = p1 + new FracB(t2 - t1, d1);
            w1 = W(sim, 1);
            Assert.AreEqual(p2, FracB.Parse(w1.Action.P0));
            Assert.AreEqual(d2, w1.Action.Duration);
            Assert.AreEqual(t2 + (long)((One - p2) * d2).Ceil(), w1.Action.Deadline);

            // Save/load сохраняет прежний срок без нового округления; ребро завершается ровно в срок.
            var reloaded = fx.Reload(sim);
            var wr = W(reloaded, 1);
            Assert.AreEqual(w1.Action.Deadline, wr.Action.Deadline);
            Assert.AreEqual(w1.Action.P0, wr.Action.P0);
            int cell0 = wr.Cell;
            long dl = wr.Action.Deadline;
            reloaded.Advance(dl - 1);
            Assert.AreEqual(cell0, W(reloaded, 1).Cell, "до срока клетка не меняется");
            reloaded.Advance(dl);
            Assert.AreNotEqual(cell0, W(reloaded, 1).Cell, "в срок — следующая клетка");
        }

        [Test]
        public void Unload_MidService_KeepsExactProgressOfCurrentPortion()
        {
            // ТЗ §7.4: «При улучшении сохраняй долю обслуживания текущей порции»; §6.5 — общее правило для движения и разгрузки.
            var fx = Real(1);
            var sim = fx.NewSim();
            WorkerState u = null;
            for (int i = 0; i < 100_000 && u == null; i++)
            {
                Assert.IsTrue(StepTick(sim));
                u = sim.State.Run.Workers.FirstOrDefault(w => w.Mode == WorkerMode.Unloading && w.Action.SegmentStart == sim.Now);
            }
            Assert.IsNotNull(u, "разгрузка не началась");
            int id = u.Id;
            long start = sim.Now;
            long t = start + 123_457;
            Assert.Less(t, u.Action.Deadline);
            sim.Advance(t);
            Assert.AreEqual(start, W(sim, id).Action.SegmentStart, "порция ещё обслуживается");
            var st = Clone(sim.State);
            GiveGold(st, Rich);
            sim = fx.Wrap(st);
            Assert.IsTrue(sim.Submit(Buy(UpgradeBranch.Unload)).Ok);
            long d1 = UnloadTicksOracle(2, 1);
            Assert.AreEqual(416_667L, d1, "ceil(1e6 / 2.4), шаг разгрузки 0.2 (профиль b4)");
            var p = new FracB(t - start, Unload0);
            var a = W(sim, id).Action;
            Assert.AreEqual(ActionKind.Unload, a.Kind);
            Assert.AreEqual(d1, a.Duration);
            Assert.AreEqual(p, FracB.Parse(a.P0));
            Assert.AreEqual(t + (long)((One - p) * d1).Ceil(), a.Deadline);
            int bag = W(sim, id).Bag.Count;
            long dl = a.Deadline;
            BigInteger value = W(sim, id).Bag[0].Value;
            BigInteger wallet = sim.State.Run.Wallet;
            sim.Advance(dl - 1);
            Assert.AreEqual(bag, W(sim, id).Bag.Count, "до срока порция не сдана");
            sim.Advance(dl);
            Assert.GreaterOrEqual(sim.State.Run.Wallet - wallet, value, "порция зачислена в новый срок");
            if (bag > 1) Assert.AreEqual(dl + d1, W(sim, id).Action.Deadline, "следующая порция — полная новая длительность");
        }

        [Test]
        public void Pickaxes_NewDamageFromNextHit_HitTimerNotReset()
        {
            // ТЗ §9: «новый урон действует со следующего удара»; §6.5: урон — на момент удара.
            var fx = Real(1);
            var sim = fx.NewSim();
            WorkerState m = null;
            for (int i = 0; i < 100_000 && m == null; i++)
            {
                Assert.IsTrue(StepTick(sim));
                m = sim.State.Run.Workers.FirstOrDefault(w => w.Mode == WorkerMode.Mining && w.Action.Deadline - sim.Now > 1);
            }
            Assert.IsNotNull(m);
            int id = m.Id, target = m.TargetId;
            long hitAt = m.Action.Deadline;
            var st = Clone(sim.State);
            GiveGold(st, Rich);
            sim = fx.Wrap(st);
            Assert.IsTrue(sim.Submit(Buy(UpgradeBranch.Pickaxes)).Ok);
            Assert.AreEqual(hitAt, W(sim, id).Action.Deadline, "покупка не сбрасывает таймер удара");
            long d1 = DamageUnitsOracle(5, 1, Zero);
            sim.Advance(hitAt - 1);
            int n = sim.State.Run.Workers.Count(w => w.Mode == WorkerMode.Mining && w.TargetId == target && w.Action.Deadline == hitAt);
            long before = T(sim, target).RemainingHpUnits;
            sim.Advance(hitAt);
            Assert.AreEqual(Math.Max(0, before - n * d1), T(sim, target).RemainingHpUnits, "удар после покупки — уроном 6.1");
        }

        [Test]
        public void Bags_CapacityIncrease_KeepsCargo()
        {
            // ТЗ §9: «вместимость не удаляет груз».
            var fx = Real(1);
            var sim = fx.NewSim();
            for (int i = 0; i < 100_000 && !sim.State.Run.Workers.Any(w => w.Bag.Count >= 2); i++) Assert.IsTrue(StepTick(sim));
            var st = Clone(sim.State);
            GiveGold(st, Rich);
            sim = fx.Wrap(st);
            var before = CargoMap(sim.State.Run);
            Assert.IsTrue(sim.Submit(Buy(UpgradeBranch.Bags, BuyMode.Ten)).Ok);
            Assert.AreEqual(14, StatCapacity(sim), "4 + 10");
            CollectionAssert.AreEqual(before, CargoMap(sim.State.Run), "груз не удалён и не перемещён");
            AssertInvariants(sim, "сумки");
        }

        // ───────────────────────────── Экипировка во время шахты ─────────────────────────────

        [Test]
        public void BackpackSwapToSmaller_OverflowCargoKept_WorkerDeliversThenUsesNewCapacity()
        {
            // ТЗ §10.3: «Если после смены рюкзака текущий груз превышает новую вместимость, он сохраняется. Котик завершает доставку,
            // затем использует новую вместимость»; §6.3 п.1; §20 «Замена полной сумки: груз не исчезает и не размножается».
            var fx = Real(1, null, p => Wear(p, "backpack.legendary"));
            var sim = fx.NewSim();
            Assert.AreEqual(4 + 10, StatCapacity(sim), "§12.3: легендарный рюкзак уровня 1 → +10");
            WorkerState heavy = null;
            for (int i = 0; i < 100_000 && heavy == null; i++)
            {
                Assert.IsTrue(StepTick(sim));
                heavy = sim.State.Run.Workers.FirstOrDefault(w => w.Bag.Count >= 6 && w.Mode == WorkerMode.Mining);
            }
            Assert.IsNotNull(heavy);
            int id = heavy.Id, target = heavy.TargetId;
            var carried = heavy.Bag.Select(Key).ToList();
            var r = sim.Submit(UnequipCmd(EquipSlot.Backpack));
            Assert.IsTrue(r.Ok, r.Reason);
            Assert.AreEqual(4, StatCapacity(sim));
            CollectionAssert.AreEqual(carried, W(sim, id).Bag.Select(Key).ToList(), "груз сверх вместимости сохраняется");
            // §6.2: Mining — «пока … есть место в сумке»; §6.3 п.1: при cargoCount ≥ FinalCapacity добыча запрещена до доставки.
            // Добывающий котик стоит в рабочей клетке — уходит к базе на той же отметке, без ещё одного удара по цели.
            Assert.That(W(sim, id).Mode, Is.EqualTo(WorkerMode.Returning).Or.EqualTo(WorkerMode.WaitingDepot),
                "сразу после смены рюкзака — к базе, не добыча цели " + target);
            Assert.AreEqual(-1, W(sim, id).WorkCell, "рабочая клетка освобождена");
            AssertInvariants(sim, "смена рюкзака", capacityMayDrop: true);
            int delivered0 = sim.State.Run.Counters.DeliveredUnits;
            bool emptied = false;
            for (int i = 0; i < 100_000 && !emptied; i++)
            {
                Assert.IsTrue(StepTick(sim));
                var run = sim.State.Run;
                var w = W(sim, id);
                foreach (var k in carried)
                {
                    Assert.IsFalse(GroundUnits(run).Any(u => Key(u) == k), "порция не выпала на землю");
                    Assert.IsFalse(run.Workers.Where(o => o.Id != id).SelectMany(o => o.Bag).Any(u => Key(u) == k), "порция не размножилась");
                }
                Assert.IsTrue(w.Bag.All(u => carried.Contains(Key(u))), "до доставки — без новой добычи и подбора");
                if (w.Bag.Count == 0) { emptied = true; break; }
                Assert.That(w.Mode, Is.EqualTo(WorkerMode.Returning).Or.EqualTo(WorkerMode.WaitingDepot).Or.EqualTo(WorkerMode.Unloading),
                    "с грузом сверх вместимости — без подбора и добычи, только доставка");
                AssertInvariants(sim, "смена рюкзака", capacityMayDrop: true);
            }
            Assert.IsTrue(emptied, "котик довёз весь груз");
            Assert.GreaterOrEqual(sim.State.Run.Counters.DeliveredUnits - delivered0, carried.Count, "унесённые порции сданы");
            for (int i = 0; i < 3000 && sim.State.Run.Phase != RunPhase.ReadyToContinue; i++)
            {
                Assert.IsTrue(StepTick(sim));
                Assert.LessOrEqual(W(sim, id).Bag.Count, 4, "дальше — новая вместимость");
                AssertInvariants(sim, "после доставки");
            }
        }

        [TestCase(WorkerMode.PickingUp)]
        [TestCase(WorkerMode.MovingToCargo)]
        [TestCase(WorkerMode.MovingToWork)]
        public void BackpackSwapToSmaller_PickupOrWalkingWorker_NoNewCargoUntilDelivered(WorkerMode mode)
        {
            // ТЗ §6.3 п.1: «Если cargoCount >= FinalCapacity — сразу направиться к базе; подбор и добыча запрещены до доставки»;
            // §10.3: груз сверх новой вместимости сохраняется, котик завершает доставку; §6.4: без телепортации по пути —
            // идущий котик доходит до ближайшей логической клетки, затем возвращается к базе. Резерв подбора снимается.
            // Поиск отметки: обычный баланс, затем (только для изоляции сценария) мгновенное разрушение и 6 котиков —
            // крупные партии на земле, частичные сумки идут к чужому грузу. Правила ядра те же.
            OutpostSimulation sim = null;
            WorkerState heavy = null;
            var variants = new[] { Configs, ConfigsWith(j => { j["BaseDamage"] = "1000"; j["StartWorkers"] = 6; }) };
            foreach (var cfg in variants)
                for (int zone = 1; zone <= 30 && heavy == null; zone++)
                {
                    sim = Real(zone, cfg, p => Wear(p, "backpack.legendary")).NewSim();
                    for (int i = 0; i < 200_000 && heavy == null; i++)
                    {
                        if (!StepTick(sim) || sim.State.Run.Phase == RunPhase.ReadyToContinue) break;
                        heavy = sim.State.Run.Workers.FirstOrDefault(w => w.Mode == mode && w.Bag.Count >= 4);
                    }
                }
            Assert.IsNotNull(heavy, "не найдена отметка, где котик в режиме " + mode + " несёт ≥ 4 порций");
            int id = heavy.Id;
            var carried = heavy.Bag.Select(Key).ToList();
            // Подбирающий стоит на месте — останавливается сразу; идущий — по завершении текущего ребра.
            long edgeDeadline = heavy.Action.Deadline;
            long stopAt = mode == WorkerMode.PickingUp ? sim.Now : edgeDeadline;
            var r = sim.Submit(UnequipCmd(EquipSlot.Backpack));
            Assert.IsTrue(r.Ok, r.Reason);
            Assert.AreEqual(4, StatCapacity(sim));
            var w0 = W(sim, id);
            CollectionAssert.AreEqual(carried, w0.Bag.Select(Key).ToList(), "груз сверх вместимости сохраняется");
            if (mode == WorkerMode.PickingUp)
            {
                Assert.That(w0.Mode, Is.EqualTo(WorkerMode.Returning).Or.EqualTo(WorkerMode.WaitingDepot), "подбор прекращён — к базе");
                Assert.IsFalse(GroundUnits(sim.State.Run).Any(u => u.ReservedBy == id), "резерв подбора снят");
            }
            else
            {
                Assert.AreEqual(mode, w0.Mode, "в пути — без телепортации, до ближайшей клетки");
                Assert.IsTrue(w0.TaskInvalid, "задача пути отменена");
                Assert.AreEqual(edgeDeadline, w0.Action.Deadline, "текущее ребро не прерывается и не пересчитывается");
                if (mode == WorkerMode.MovingToWork) Assert.AreEqual(-1, w0.WorkCell, "рабочая клетка освобождена сразу");
            }
            AssertInvariants(sim, "смена рюкзака (" + mode + ")", capacityMayDrop: true);

            bool emptied = false;
            for (int i = 0; i < 100_000 && !emptied; i++)
            {
                Assert.IsTrue(StepTick(sim));
                var w = W(sim, id);
                Assert.IsTrue(w.Bag.All(u => carried.Contains(Key(u))), "до доставки — без нового подбора и добычи");
                if (w.Bag.Count == 0) { emptied = true; break; }
                AssertInvariants(sim, "смена рюкзака (" + mode + ")", capacityMayDrop: true);
                if (sim.Now < stopAt)
                {
                    Assert.AreEqual(mode, w.Mode, "до конца текущего ребра");
                    continue;
                }
                Assert.That(w.Mode, Is.EqualTo(WorkerMode.Returning).Or.EqualTo(WorkerMode.WaitingDepot).Or.EqualTo(WorkerMode.Unloading),
                    "после ближайшей клетки — только доставка");
                Assert.AreEqual(-1, w.WorkCell, "без рабочей клетки до доставки");
                Assert.IsFalse(GroundUnits(sim.State.Run).Any(u => u.ReservedBy == id), "резервов подбора нет до доставки");
            }
            Assert.IsTrue(emptied, "котик довёз весь груз");
        }

        [Test]
        public void HatChange_ExistingCargoKeepsValue_NewPortionsUseNewValue()
        {
            // ТЗ §7.3: «Последующее улучшение или смена шляпы влияют на новые порции. Существующий груз на земле, в сумке и в очереди
            // не пересчитывается»; §10.3 «Экипировка не пересчитывает уже созданные порции»; §20 «Смена шляпы».
            var fx = Real(1, null, p => Own(p, "hat.legendary"));
            var sim = fx.NewSim();
            sim.Advance(25 * S);
            // Интеграция: ровно в 25 с zone1 весь груз может быть уже сдан (цикл 2 котиков), берём первую отметку после 25 с с грузом.
            for (int i = 0; i < 1000 && !AllUnits(sim.State.Run).Any(); i++) Assert.IsTrue(StepTick(sim));
            var seen = AllUnits(sim.State.Run).ToDictionary(Key, u => u.Value);
            Assert.Greater(seen.Count, 0, "к 25 с есть груз");
            BigInteger creditedBefore = sim.State.Run.TotalCredited;
            var r = sim.Submit(EquipCmd("hat.legendary"));
            Assert.IsTrue(r.Ok, r.Reason);
            var hat = ItemBonus(3, 1);
            Assert.AreEqual(new FracB(3, 5), hat, "легендарная шляпа уровня 1: +60%");
            int fresh = 0;
            RunToReadyOrFail(sim, "шляпа", null, (s, ev) =>
            {
                foreach (var unit in AllUnits(s.State.Run))
                {
                    if (seen.TryGetValue(Key(unit), out var v))
                        Assert.AreEqual(v, unit.Value, "стоимость порции неизменна");
                    else
                    {
                        var vf = FracB.Parse(T(s, unit.Source).ValueFactor);
                        Assert.AreEqual(CoinOracle(1, vf, 0, hat), unit.Value, "новая порция — с шляпой");
                        seen[Key(unit)] = unit.Value;
                        fresh++;
                    }
                }
            });
            BigInteger all = seen.Values.Aggregate(BigInteger.Zero, (acc, v) => acc + v);
            Assert.AreEqual(creditedBefore + all, sim.State.Run.TotalCredited, "сдано = Σ стоимостей, зафиксированных при выпуске");
            Assert.Greater(fresh, 0);
            Assert.IsTrue(seen.Values.Any(v => v == 8), "r: ceil(5·1.6) = 8");
        }
    }
}
