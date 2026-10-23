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
    /// Порядок одновременных событий одной отметки (ТЗ §6.5, CORE_SPEC §7.1): 1 движение → 2 удары (цели по якорю) → 3 подбор →
    /// 4 склад (по номеру места) → 5 ворота/завершение → 6 команды → 7 планирование. Сравниваются две копии одного предсостояния:
    /// A — без команд, B — с командами, запланированными через Schedule(cmd, t) на ту же отметку.
    /// </summary>
    public class OutpostOrderingTests
    {
        /// <summary>Правка профиля для теста: все длительности кратны 1 с, поэтому отметки событий часто совпадают.</summary>
        static OutpostConfigSet IntegerSecondConfig(int startWorkers, int capacity, string damage) => ConfigsWith(j =>
        {
            j["BaseMoveSpeed"] = "1";
            j["PickupTicksPerUnit"] = 1_000_000;
            j["BaseUnloadRate"] = "1";
            j["BaseDamage"] = damage;
            j["BaseCapacity"] = capacity;
            j["StartWorkers"] = startWorkers;
        });

        /// <summary>Шагать по отметкам до первой t, для которой pred(предсостояние, t); вернуть JSON предсостояния и t.</summary>
        static (string Pre, long Tick) FindTick(OutpostSimulation sim, Func<OutpostSimulation, long, bool> pred, int maxTicks, string what)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                long t = sim.NextEventTick();
                Assert.AreNotEqual(long.MaxValue, t, what + ": события закончились");
                if (pred(sim, t)) return (Json(sim.State), t);
                sim.Advance(t);
                Drain(sim);
                Assert.AreNotEqual(RunPhase.ReadyToContinue, sim.State.Run.Phase, what + ": карта завершилась раньше");
            }
            Assert.Fail(what + ": отметка не найдена за " + maxTicks + " шагов");
            return (null, -1);
        }

        /// <summary>Шагать до первой t, после обработки которой post(предсостояние-JSON, симуляция) истинно.</summary>
        static (string Pre, long Tick) FindTickAfter(OutpostSimulation sim, Func<OutpostGameState, OutpostSimulation, bool> post, int maxTicks, string what)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                long t = sim.NextEventTick();
                Assert.AreNotEqual(long.MaxValue, t, what + ": события закончились");
                string pre = Json(sim.State);
                var preState = FromJson(pre);
                sim.Advance(t);
                Drain(sim);
                if (post(preState, sim)) return (pre, t);
                Assert.AreNotEqual(RunPhase.ReadyToContinue, sim.State.Run.Phase, what + ": карта завершилась раньше");
            }
            Assert.Fail(what + ": отметка не найдена за " + maxTicks + " шагов");
            return (null, -1);
        }

        /// <summary>Выпустят ли удары отметки tick хотя бы одну порцию (по предсостоянию, независимая формула §7.1).</summary>
        static bool ReleasesAt(OutpostSimulation s, long tick, long dmg)
        {
            var groups = s.State.Run.Workers
                .Where(w => w.Mode == WorkerMode.Mining && w.Action != null && w.Action.Kind == ActionKind.Hit && w.Action.Deadline == tick)
                .GroupBy(w => w.TargetId);
            foreach (var g in groups)
            {
                var t = T(s, g.Key);
                if (t.Destroyed) continue;
                long rem = Math.Max(0, t.RemainingHpUnits - g.Count() * dmg);
                long expected = rem == 0 ? t.TotalUnits : (long)(new BigInteger(t.TotalUnits) * (t.MaxHpUnits - rem) / t.MaxHpUnits);
                if (expected > t.ReleasedUnits) return true;
            }
            return false;
        }

        static Dictionary<int, int> HittersAt(OutpostRun run, long tick) => run.Workers
            .Where(w => w.Mode == WorkerMode.Mining && w.Action != null && w.Action.Kind == ActionKind.Hit && w.Action.Deadline == tick)
            .GroupBy(w => w.TargetId).ToDictionary(g => g.Key, g => g.Count());

        /// <summary>Фазы 1–5 не зависят от команд отметки: груз, HP, выпуск, клетки и сумки совпадают.</summary>
        static void AssertSamePhases1To5(OutpostRun a, OutpostRun b, string context)
        {
            CollectionAssert.AreEqual(CargoMap(a), CargoMap(b), context + ": груз и стоимость");
            for (int i = 0; i < a.Targets.Count; i++)
            {
                Assert.AreEqual(a.Targets[i].RemainingHpUnits, b.Targets[i].RemainingHpUnits, context + ": HP цели " + a.Targets[i].Id);
                Assert.AreEqual(a.Targets[i].ReleasedUnits, b.Targets[i].ReleasedUnits, context + ": выпуск цели " + a.Targets[i].Id);
            }
            CollectionAssert.AreEqual(a.Workers.Select(w => w.Cell).ToList(), b.Workers.Select(w => w.Cell).ToList(), context + ": клетки");
            Assert.AreEqual(a.Counters.DeliveredUnits, b.Counters.DeliveredUnits, context + ": сданные порции");
            Assert.AreEqual(a.GatesOpened, b.GatesOpened);
            Assert.AreEqual(a.Phase, b.Phase);
        }

        [Test]
        public void SameTick_MoveHitPickupUnloadAndCommands_CommandsApplyAfterAllEvents()
        {
            // ТЗ §6.5: «Выполнить команды игрока по порядку поступления после всех событий этой отметки»; «Урон рассчитывается…
            // на момент удара, а цена груза — на момент выпуска порций. Команда покупки или смены шляпы на этой же отметке
            // применяется к последующим событиям»; §20 «Одинаковая отметка: удары/подборы/сдача/команда используют заданный порядок».
            const int zone = 10;
            var cfg = IntegerSecondConfig(6, 2, "30");
            var fx = Real(zone, cfg, p => Own(p, "hat.legendary"));
            var sim = fx.NewSim();
            long dmg0 = StatDamageUnits(sim);
            Assert.AreEqual(30 * S, dmg0);
            BigInteger cost = NextCostOracle(zone, UpgradeBranch.Pickaxes, 0);
            var (pre, t) = FindTick(sim, (s, tick) =>
            {
                var due = s.State.Run.Workers.Where(w => w.Action != null && w.Action.Deadline == tick).Select(w => w.Action.Kind).ToList();
                return s.State.Run.Wallet >= cost && due.Contains(ActionKind.Edge) && due.Contains(ActionKind.Hit) &&
                       due.Contains(ActionKind.Pickup) && due.Contains(ActionKind.Unload) && ReleasesAt(s, tick, dmg0);
            }, 200_000, "совпадение движения, удара, подбора и разгрузки");

            var a = fx.Load(pre);
            a.Advance(t);
            var b = fx.Load(pre);
            b.Schedule(Buy(UpgradeBranch.Pickaxes), t);
            b.Schedule(EquipCmd("hat.legendary"), t);
            b.Advance(t);
            var ra = a.State.Run;
            var rb = b.State.Run;

            AssertSamePhases1To5(ra, rb, "отметка t");
            Assert.AreEqual(ra.Wallet - cost, rb.Wallet, "покупка — после зачислений этой отметки");
            Assert.AreEqual(0, ra.Levels[(int)UpgradeBranch.Pickaxes]);
            Assert.AreEqual(1, rb.Levels[(int)UpgradeBranch.Pickaxes]);
            Assert.AreEqual("hat.legendary", b.State.Profile.Equipped[(int)EquipSlot.Hat]);

            // Порции, выпущенные в t, оценены по прежним шляпе и уровню.
            var preKeys = UnitKeys(FromJson(pre).Run);
            var released = AllUnits(rb).Where(u => !preKeys.Contains(Key(u))).ToList();
            Assert.Greater(released.Count, 0, "в t выпущены порции");
            foreach (var u in released)
                Assert.AreEqual(CoinOracle(zone, FracB.Parse(T(b, u.Source).ValueFactor), 0, Zero), u.Value, "цена — на момент выпуска");

            // После отметки — новые характеристики: урон 30·1.22 со следующего удара, шляпа +60% для новых порций.
            long dmg1 = DamageUnitsOracle(30, 1, Zero);
            Assert.AreEqual(dmg1, StatDamageUnits(b));
            Assert.AreEqual(dmg0, StatDamageUnits(a));
            // Следующая отметка ударов. Интеграция: при целых секундах все таймеры синхронны, и в t единственный добывающий может
            // уйти с полной сумкой — тогда ударов ещё нет; шагаем обе копии по отметкам до первого назначенного удара
            // (без ударов HP и выпуск не меняются, копии идут одинаково).
            long t2 = long.MaxValue;
            for (int guard = 0; guard < 10_000; guard++)
            {
                var hitting = rb.Workers.Where(w => w.Action != null && w.Action.Kind == ActionKind.Hit).ToList();
                if (hitting.Count > 0)
                {
                    t2 = hitting.Min(w => w.Action.Deadline);
                    break;
                }
                long next = b.NextEventTick();
                Assert.AreEqual(next, a.NextEventTick(), "до следующего удара копии идут одинаково");
                Assert.AreNotEqual(long.MaxValue, next, "события закончились до следующего удара");
                a.Advance(next);
                b.Advance(next);
                ra = a.State.Run;
                rb = b.State.Run;
            }
            Assert.AreNotEqual(long.MaxValue, t2, "следующий удар не найден");
            a.Advance(t2 - 1);
            b.Advance(t2 - 1);
            ra = a.State.Run;
            rb = b.State.Run;
            var hitters = HittersAt(rb, t2);
            CollectionAssert.AreEquivalent(HittersAt(ra, t2), hitters, "задачи после команды не изменились");
            var hpA = ra.Targets.ToDictionary(x => x.Id, x => x.RemainingHpUnits);
            var hpB = rb.Targets.ToDictionary(x => x.Id, x => x.RemainingHpUnits);
            CollectionAssert.AreEquivalent(hpA, hpB, "между отметками ударов HP не менялся");
            var keysB = UnitKeys(rb);
            a.Advance(t2);
            b.Advance(t2);
            rb = b.State.Run;
            foreach (var kv in hitters)
            {
                Assert.AreEqual(Math.Max(0, hpA[kv.Key] - kv.Value * dmg0), T(a, kv.Key).RemainingHpUnits, "A: прежний урон");
                Assert.AreEqual(Math.Max(0, hpB[kv.Key] - kv.Value * dmg1), T(b, kv.Key).RemainingHpUnits, "B: новый урон со следующего удара");
            }
            foreach (var u in AllUnits(rb).Where(q => !keysB.Contains(Key(q))))
                Assert.AreEqual(CoinOracle(zone, FracB.Parse(T(b, u.Source).ValueFactor), 0, ItemBonus(3, 1)), u.Value, "новые порции — с шляпой");
            AssertInvariants(a, "A");
            AssertInvariants(b, "B");
        }

        [Test]
        public void DepositAtTick_FundsCommandScheduledAtSameTick()
        {
            // ТЗ §6.5 п.4 → п.6: зачисление раньше команд этой же отметки; §7.3: покупки — только из доставленного золота.
            var fx = Real(1);
            var sim = fx.NewSim();
            BigInteger cost = NextCostOracle(1, UpgradeBranch.Pickaxes, 0);
            Assert.AreEqual(new BigInteger(20), cost, "§12.5: первая «Кирка» — 20");
            var (pre, t) = FindTickAfter(sim, (before, s) => before.Run.Wallet < cost && s.State.Run.Wallet >= cost, 100_000, "сдача до 20 золота");
            BigInteger walletAtT = sim.State.Run.Wallet;

            var a = fx.Load(pre);
            a.Schedule(Buy(UpgradeBranch.Pickaxes), t);
            a.Advance(t);
            Assert.AreEqual(1, a.State.Run.Levels[(int)UpgradeBranch.Pickaxes], "команда видит золото, сданное в эту отметку");
            Assert.AreEqual(walletAtT - cost, a.State.Run.Wallet);
            Assert.AreEqual(t, a.State.Run.Counters.FirstPurchaseTick);
            Assert.AreEqual(DamageUnitsOracle(5, 1, Zero), StatDamageUnits(a), "§12.5: после покупки урон 6.1");

            long preNow = FromJson(pre).Run.Now;
            if (preNow + 1 < t)
            {
                var c = fx.Load(pre);
                c.Schedule(Buy(UpgradeBranch.Pickaxes), preNow + 1);
                c.Advance(preNow + 1);
                Assert.AreEqual(0, c.State.Run.Levels[(int)UpgradeBranch.Pickaxes], "до сдачи золота не хватает — покупки нет");
                Assert.AreEqual(BigInteger.Zero, c.State.Run.TotalSpent);
            }
        }

        [Test]
        public void CapacityPurchaseAtOverflowTick_DistributionUsesOldCapacity()
        {
            // ТЗ §6.5: команда на отметке применяется к последующим событиям — распределение удара (фаза 2) идёт по старой
            // вместимости. Правка теста: урон 100, сумка 2 — переполнение при ударе частое.
            const int zone = 5;
            var cfg = ConfigsWith(j => { j["BaseDamage"] = "100"; j["BaseCapacity"] = 2; });
            var fx = Real(zone, cfg);
            var sim = fx.NewSim();
            BigInteger cost = NextCostOracle(zone, UpgradeBranch.Bags, 0);
            var (pre, t) = FindTickAfter(sim, (before, s) => before.Run.Wallet >= cost && s.State.Run.NextBatchSeq > before.Run.NextBatchSeq,
                200_000, "переполнение сумки при ударе");

            var a = fx.Load(pre);
            a.Advance(t);
            var b = fx.Load(pre);
            b.Schedule(Buy(UpgradeBranch.Bags), t);
            b.Advance(t);
            AssertSamePhases1To5(a.State.Run, b.State.Run, "переполнение");
            Assert.AreEqual(a.State.Run.NextBatchSeq, b.State.Run.NextBatchSeq, "партия создана и при покупке на этой отметке");
            Assert.AreEqual(2, StatCapacity(a));
            Assert.AreEqual(3, StatCapacity(b), "новая вместимость — после фаз 1–5");
            Assert.AreEqual(1, b.State.Run.Levels[(int)UpgradeBranch.Bags]);
            AssertInvariants(b, "после покупки сумок");
        }

        [Test]
        public void HatEquipAtReleaseTick_UnitsReleasedAtThatTickKeepOldValue()
        {
            // ТЗ §6.5 «цена груза — на момент выпуска порций»; §7.3 «смена шляпы влияет на новые порции»; §20 «Смена шляпы».
            var fx = Real(1, null, p => Own(p, "hat.legendary"));
            var sim = fx.NewSim();
            var (pre, t) = FindTick(sim, (s, tick) => ReleasesAt(s, tick, Damage0), 100_000, "отметка выпуска");
            var a = fx.Load(pre);
            a.Advance(t);
            var b = fx.Load(pre);
            b.Schedule(EquipCmd("hat.legendary"), t);
            b.Advance(t);
            AssertSamePhases1To5(a.State.Run, b.State.Run, "шляпа в отметку выпуска");
            var preKeys = UnitKeys(FromJson(pre).Run);
            var released = AllUnits(b.State.Run).Where(u => !preKeys.Contains(Key(u))).ToList();
            Assert.Greater(released.Count, 0);
            foreach (var u in released)
                Assert.AreEqual(CoinOracle(1, FracB.Parse(T(b, u.Source).ValueFactor), 0, Zero), u.Value, "выпущено до команды — без шляпы");

            // Следующий выпуск в B — уже с шляпой; уже созданные порции не пересчитаны.
            var known = AllUnits(b.State.Run).ToDictionary(Key, u => u.Value);
            bool checkedNew = false;
            for (int i = 0; i < 100_000 && !checkedNew; i++)
            {
                Assert.IsTrue(StepTick(b));
                foreach (var u in AllUnits(b.State.Run))
                {
                    if (known.TryGetValue(Key(u), out var v)) Assert.AreEqual(v, u.Value, "существующий груз не пересчитывается");
                    else
                    {
                        Assert.AreEqual(CoinOracle(1, FracB.Parse(T(b, u.Source).ValueFactor), 0, ItemBonus(3, 1)), u.Value, "новая порция — с шляпой +60%");
                        checkedNew = true;
                    }
                }
            }
            Assert.IsTrue(checkedNew);
        }

        [Test]
        public void PickaxePurchaseAtHitTick_HitUsesOldDamage_NextHitNewDamage()
        {
            // ТЗ §6.5: «Урон рассчитывается по экипировке и локальному уровню на момент удара»; команда этой отметки — после ударов.
            var fx = Real(1);
            var sim = fx.NewSim();
            BigInteger cost = NextCostOracle(1, UpgradeBranch.Pickaxes, 0);
            var (pre, t) = FindTick(sim, (s, tick) => s.State.Run.Wallet >= cost && HittersAt(s.State.Run, tick).Count > 0, 100_000, "удар при 20 золота");
            var hitters = HittersAt(FromJson(pre).Run, t);
            var hp0 = FromJson(pre).Run.Targets.ToDictionary(x => x.Id, x => x.RemainingHpUnits);
            var a = fx.Load(pre);
            a.Advance(t);
            var b = fx.Load(pre);
            b.Schedule(Buy(UpgradeBranch.Pickaxes), t);
            b.Advance(t);
            AssertSamePhases1To5(a.State.Run, b.State.Run, "удар и покупка в одной отметке");
            foreach (var kv in hitters)
                Assert.AreEqual(Math.Max(0, hp0[kv.Key] - kv.Value * Damage0), T(b, kv.Key).RemainingHpUnits, "удар этой отметки — прежним уроном 5");
            Assert.AreEqual(DamageUnitsOracle(5, 1, Zero), StatDamageUnits(b));
        }

        [Test]
        public void BootsPurchaseAtEdgeCompletionTick_NextEdgeUsesNewSpeedFully()
        {
            // ТЗ §6.5: перемещение до логической клетки (фаза 1) завершается до команды; следующее ребро начинается с p = 0,
            // поэтому после пересчёта фазы 7 его срок — полная новая длительность.
            var fx = Real(1);
            var sim = fx.NewSim();
            BigInteger cost = NextCostOracle(1, UpgradeBranch.Boots, 0);
            int id = -1;
            var (pre, t) = FindTick(sim, (s, tick) =>
            {
                if (s.State.Run.Wallet < cost) return false;
                var w = s.State.Run.Workers.FirstOrDefault(x => IsMoving(x) && x.Action.Deadline == tick && !x.TaskInvalid &&
                                                               x.Path != null && x.PathIndex + 1 < x.Path.Length);
                if (w == null) return false;
                id = w.Id;
                return true;
            }, 100_000, "завершение ребра посреди пути");
            long edge1 = EdgeTicksOracle(new FracB(5, 2), 1, Zero);
            var a = fx.Load(pre);
            a.Advance(t);
            var b = fx.Load(pre);
            b.Schedule(Buy(UpgradeBranch.Boots), t);
            b.Advance(t);
            AssertSamePhases1To5(a.State.Run, b.State.Run, "ребро и покупка");
            var wa = W(a, id).Action;
            var wb = W(b, id).Action;
            Assert.AreEqual(ActionKind.Edge, wa.Kind);
            Assert.AreEqual(t + Edge0, wa.Deadline, "A: следующее ребро 0.4 с");
            Assert.AreEqual(ActionKind.Edge, wb.Kind);
            Assert.AreEqual(edge1, wb.Duration);
            Assert.AreEqual(t + edge1, wb.Deadline, "B: p = 0 → остаток ceil(1·D_new)");
            Assert.AreEqual(Zero, FracB.Parse(wb.P0));
        }

        [Test]
        public void SameTickHitsOnTwoTargets_ProcessedByAnchorIndex_NotByWorkerId()
        {
            // ТЗ §6.5 п.2: «Цели сортируются по индексу якоря». Правка теста: скорость 1, урон 10, MaxWorkersPerTarget 1.
            // W1 → B (3,3) id 24 (1 ребро, удары 2,3,4,5 с); W2 → A (5,2) id 19 (4 ребра, удары 5,6 с). На 5 с обе цели выпускают порцию.
            var cfg = ConfigsWith(j => { j["BaseMoveSpeed"] = "1"; j["BaseDamage"] = "10"; j["MaxWorkersPerTarget"] = 1; });
            var fx = Synthetic(cfg, null,
                Rock1 + " . . . . . " + Exit,
                ". . . . . . .",
                ". . . . . " + Rock2 + " .",
                Camp + " . . " + Rock2 + " . . .");
            const int idA = 19, idB = 24;
            var sim = fx.NewSim();
            Assert.AreEqual(idB, W(sim, 1).TargetId, "W1: ближайшая цель B");
            Assert.AreEqual(idA, W(sim, 2).TargetId, "W2: B занята (лимит 1) — A");
            sim.Advance(5 * S - 1);
            Drain(sim);
            sim.Advance(5 * S);
            var ev = Drain(sim).Where(e => e.Type == OutpostEventType.CargoReleased || e.Type == OutpostEventType.TargetHit).ToList();
            int firstA = ev.FindIndex(e => e.TargetId == idA), firstB = ev.FindIndex(e => e.TargetId == idB);
            Assert.GreaterOrEqual(firstA, 0);
            Assert.GreaterOrEqual(firstB, 0);
            Assert.Less(firstA, firstB, "цель с меньшим якорем обрабатывается раньше, хотя её бьёт больший workerId");
            CollectionAssert.AreEqual(new[] { idA, idB },
                ev.Where(e => e.Type == OutpostEventType.CargoReleased).Select(e => e.TargetId).Distinct().ToList());
            Assert.AreEqual(4, T(sim, idB).ReleasedUnits, "B: 4 удара по 10 из 120 → 4 порции");
            Assert.AreEqual(1, T(sim, idA).ReleasedUnits);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, Ordinals(W(sim, 1).Bag));
            CollectionAssert.AreEqual(new[] { 0 }, Ordinals(W(sim, 2).Bag));
        }

        [Test]
        public void GateDestroyedAtTick_OpensInPhase5_BeforePlanning()
        {
            // ТЗ §6.5 п.5 → п.7: ворота открываются после ударов этой отметки; назначение задач уже видит открытый сектор.
            var fx = GateTwoSectors();
            var sim = fx.NewSim();
            int nav0 = sim.State.Run.NavVersion;
            while (!T(sim, GateId).Destroyed)
            {
                Assert.IsTrue(StepTick(sim));
                Drain(sim);
            }
            var run = sim.State.Run;
            Assert.AreEqual(1, run.GatesOpened, "открытие в ту же отметку");
            Assert.Greater(run.NavVersion, nav0, "§6.4: версия навигации меняется при открытии ворот");
            Assert.AreEqual(RunPhase.Running, run.Phase, "наверху остался R2");
            Assert.IsFalse(run.Workers.Any(w => w.Mode == WorkerMode.Idle), "нуждавшиеся в задаче не ждут: сектор 1 уже открыт");
            foreach (var w in run.Workers.Where(w => w.TargetId == GateR2 && w.Mode == WorkerMode.MovingToWork))
                Assert.IsTrue(RemainingPath(w).Any(c => RowOf(fx.Map, c) < GateRow), "путь в верхний сектор");
            AssertInvariants(sim, "открытие ворот");
        }
    }
}
