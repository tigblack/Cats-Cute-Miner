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
    /// Автономный цикл ядра CatOutpostV2 (ТЗ §6, §7, §8.2, §8.4, §11.1, §20): старт без действий, первый удар через интервал,
    /// Idle, порционный выпуск, учёт монет, разгрузка и два места склада, полная сумка, большой дроп, совместная добыча,
    /// фаза доставки, ворота, все виды x, длинный прогон с инвариантами после каждой отметки.
    /// Ожидания выведены из ТЗ/CORE_SPEC независимо от реализации; точные расписания посчитаны вручную в комментариях.
    /// </summary>
    public class OutpostSimulationTests
    {
        // ───────────────────────────── Старт, Idle, первый удар ─────────────────────────────

        [Test]
        public void Start_NoActions_TwoWorkersFindWorkAndDeliver()
        {
            // ТЗ §3 п.1–2, §6.3 «На старте … котик выбирает задачу автоматически»; §20 «Старт без действий», «Две минуты без касаний».
            var fx = Real(1);
            var sim = fx.NewSim();
            var run = sim.State.Run;
            Assert.AreEqual(2, run.Workers.Count, "§11.2: два стартовых котика");
            CollectionAssert.AreEqual(new[] { 1, 2 }, run.Workers.Select(w => w.Id).ToArray(), "workerId с 1");
            Assert.AreEqual(BigInteger.Zero, run.Wallet, "§12.1: стартовое золото 0");
            Assert.AreEqual(RunPhase.Running, run.Phase);
            foreach (var w in run.Workers)
            {
                Assert.AreEqual(fx.Map.SpawnCell, w.Cell, "w" + w.Id + ": спавн (3, Depth+1) (§8.3)");
                Assert.That(w.Mode, Is.EqualTo(WorkerMode.MovingToWork).Or.EqualTo(WorkerMode.Mining), "w" + w.Id + ": задача выбрана без игрока");
                Assert.Greater(w.TaskId, 0L);
                Assert.GreaterOrEqual(w.TargetId, 0);
            }
            Assert.AreNotEqual(run.Workers[0].WorkCell, run.Workers[1].WorkCell, "§6.3: одна рабочая клетка — один добывающий");
            AssertInvariants(sim, "старт");

            var report = RunToReady(sim, null, 120 * S, EventLimit, (s, ev) => AssertInvariants(s, "две минуты без касаний"));
            run = sim.State.Run;
            Assert.IsFalse(report.Watchdog, run.WatchdogDiagnostic);
            Assert.Greater(run.Counters.DeliveredUnits, 0, "§20: котики сами доставили первые монеты");
            Assert.Greater(run.Wallet, BigInteger.Zero);
            Assert.Greater(run.Counters.ClearedTargets, 0, "§20: продолжается добыча и выбор новых целей");
            Assert.AreEqual(BigInteger.Zero, run.TotalSpent, "покупок не было");
        }

        [Test]
        public void Arrival_FirstHitOnlyAfterFullSecond()
        {
            // ТЗ §6.2: «Прибытие к новой цели не наносит мгновенный удар: первый удар через полный интервал в 1 секунду».
            var fx = SingleRock();
            var map = fx.Map;
            var sim = fx.NewSim();
            Assert.AreEqual(SingleRockId, W(sim, 1).TargetId);
            Assert.AreEqual(Cell(map, 3, 2), W(sim, 1).WorkCell, "W1: ближайшая рабочая клетка (3,2), 2 ребра");
            Assert.AreEqual(SingleRockId, W(sim, 2).TargetId);
            Assert.AreEqual(Cell(map, 2, 1), W(sim, 2).WorkCell, "W2: (2,1) и (4,1) по 4 ребра — меньший индекс клетки (§6.3)");
            long maxU = T(sim, SingleRockId).MaxHpUnits;
            Assert.AreEqual(20 * S, maxU, "§12.5: r площади 1, rank01 = 0, zone1 → 20 HP");

            long arrive = 2 * Edge0;
            sim.Advance(arrive);
            var w1 = W(sim, 1);
            Assert.AreEqual(Cell(map, 3, 2), w1.Cell);
            Assert.AreEqual(WorkerMode.Mining, w1.Mode);
            Assert.AreEqual(ActionKind.Hit, w1.Action.Kind);
            Assert.AreEqual(arrive + HitTicks, w1.Action.Deadline, "первый удар — через полный интервал после прибытия");
            Assert.AreEqual(maxU, T(sim, SingleRockId).RemainingHpUnits, "прибытие не наносит удар");

            sim.Advance(arrive + HitTicks - 1);
            Assert.AreEqual(maxU, T(sim, SingleRockId).RemainingHpUnits);
            sim.Advance(arrive + HitTicks);
            Assert.AreEqual(maxU - Damage0, T(sim, SingleRockId).RemainingHpUnits, "§12.5: урон 5");
        }

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(10)]
        public void Hits_NewTargetWaitsFullInterval_DamageSummedOncePerTarget(int zone)
        {
            // ТЗ §6.2 (первый удар через 1 с), §6.5 п.2 (урон по цели суммируется, HP обновляется один раз),
            // §7.1 (избыточный урон не переносится). Проверка на каждой отметке первых 300 с.
            var fx = Real(zone);
            var sim = fx.NewSim();
            long dmg = StatDamageUnits(sim);
            Assert.AreEqual(Damage0, dmg);
            var prev = sim.State.Run.Workers.ToDictionary(w => w.Id, w => (w.Mode, w.TargetId));
            int entered = 0;
            while (sim.Now < 300 * S && sim.State.Run.Phase != RunPhase.ReadyToContinue)
            {
                long t = sim.NextEventTick();
                Assert.AreNotEqual(long.MaxValue, t, "нет событий при незавершённой карте");
                var hitters = sim.State.Run.Workers
                    .Where(w => w.Mode == WorkerMode.Mining && w.Action != null && w.Action.Kind == ActionKind.Hit && w.Action.Deadline == t)
                    .GroupBy(w => w.TargetId).ToDictionary(g => g.Key, g => g.Count());
                var hpBefore = sim.State.Run.Targets.ToDictionary(x => x.Id, x => x.RemainingHpUnits);
                sim.Advance(t);
                foreach (var target in sim.State.Run.Targets)
                {
                    hitters.TryGetValue(target.Id, out int n);
                    Assert.AreEqual(Math.Max(0, hpBefore[target.Id] - n * dmg), target.RemainingHpUnits,
                        "zone" + zone + " t=" + t + " цель " + target.Id + ": HP −= Σурон ударивших, один раз");
                }
                foreach (var w in sim.State.Run.Workers)
                {
                    if (w.Mode == WorkerMode.Mining)
                    {
                        Assert.AreEqual(ActionKind.Hit, w.Action.Kind);
                        Assert.LessOrEqual(w.Action.Deadline - t, HitTicks);
                        if (!prev.TryGetValue(w.Id, out var p) || p.Mode != WorkerMode.Mining || p.TargetId != w.TargetId)
                        {
                            Assert.AreEqual(t + HitTicks, w.Action.Deadline, "w" + w.Id + ": новая цель " + w.TargetId + " — удар через полный интервал");
                            entered++;
                        }
                    }
                    prev[w.Id] = (w.Mode, w.TargetId);
                }
                Drain(sim);
                AssertInvariants(sim, "zone" + zone);
            }
            Assert.Greater(entered, 2, "котики несколько раз начинали добычу новой цели");
        }

        [Test]
        public void Idle_WithoutAvailableWork_ProducesNothing()
        {
            // ТЗ §6.2 «Idle не создаёт доход»; §6.3 п.5. Одна цель и MaxWorkersPerTarget = 1 (правка теста): второму работы нет.
            var cfg = ConfigsWith(j => j["MaxWorkersPerTarget"] = 1);
            var fx = SingleRock(cfg);
            var sim = fx.NewSim();
            Assert.AreEqual(WorkerMode.MovingToWork, W(sim, 1).Mode);
            Assert.AreEqual(WorkerMode.Idle, W(sim, 2).Mode, "§6.3 п.5: работы и груза нет — ожидание");
            Assert.IsNull(W(sim, 2).Action, "Idle без таймера");
            long hits = 0;
            while (true)
            {
                long hpBefore = T(sim, SingleRockId).RemainingHpUnits;
                Assert.IsTrue(StepTick(sim));
                var events = Drain(sim);
                Assert.IsFalse(events.Any(e => e.WorkerId == 2 && (e.Type == OutpostEventType.TargetHit ||
                    e.Type == OutpostEventType.CargoReleased || e.Type == OutpostEventType.CargoDeposited)), "Idle не добывает и не сдаёт");
                if (T(sim, SingleRockId).RemainingHpUnits != hpBefore)
                {
                    hits++;
                    Assert.AreEqual(hpBefore - Damage0 > 0 ? hpBefore - Damage0 : 0, T(sim, SingleRockId).RemainingHpUnits, "бьёт только W1");
                }
                if (T(sim, SingleRockId).Destroyed) break;
                Assert.AreEqual(WorkerMode.Idle, W(sim, 2).Mode, "W2 ждёт, пока цель занята");
                Assert.AreEqual(0, W(sim, 2).Bag.Count);
                Assert.AreEqual(BigInteger.Zero, sim.State.Run.Wallet);
                AssertInvariants(sim, "idle");
            }
            Assert.AreEqual(4L, hits, "20 HP / 5 — четыре удара одного W1");
            Assert.AreEqual(2, W(sim, 1).Bag.Count, "обе порции у W1");
            RunToReadyOrFail(sim, "idle");
            Assert.AreEqual(new BigInteger(10), sim.State.Run.TotalCredited);
        }

        // ───────────────────────────── Выпуск и учёт ─────────────────────────────

        [Test]
        public void Control_RankZeroRock_TwoPortionsOfFive_WalletOnlyAfterUnload()
        {
            // ТЗ §12.5: «r площади 1 и rank01 = 0 на шахте 1 имеет 20 HP, две порции по 5 монет. После суммарного урона 10
            // создаётся первая порция, после разрушения — вторая. В кошельке всё ещё 0, пока котик не сдаст груз. Полная сдача — 10».
            // Расписание: W1 (3,2) прибывает 0.8 с, бьёт 1.8/2.8 с; W2 (2,1) прибывает 1.6 с, бьёт 2.6/3.6 с.
            var fx = SingleRock();
            var map = fx.Map;
            var sim = fx.NewSim();
            var rock = T(sim, SingleRockId);
            Assert.AreEqual(2, rock.TotalUnits);
            Assert.AreEqual(One, FracB.Parse(rock.ValueFactor), "r: BaseCoinValueFactor 1");

            sim.Advance(2_600_000 - 1);
            Assert.AreEqual(15 * S, T(sim, SingleRockId).RemainingHpUnits, "после первого удара W1");
            Assert.AreEqual(0, T(sim, SingleRockId).ReleasedUnits, "floor(2·5/20) = 0");

            sim.Advance(2_600_000);
            rock = T(sim, SingleRockId);
            Assert.AreEqual(10 * S, rock.RemainingHpUnits);
            Assert.AreEqual(1, rock.ReleasedUnits, "суммарный урон 10 → первая порция");
            CollectionAssert.AreEqual(new[] { 0 }, Ordinals(W(sim, 2).Bag), "порция — в сумку ударившего W2");
            Assert.AreEqual(new BigInteger(5), W(sim, 2).Bag[0].Value, "§12.4: ceil(5·S(1)·1·1) = 5");
            Assert.AreEqual(BigInteger.Zero, sim.State.Run.Wallet, "§7.3: выпуск не зачисляет");

            sim.Advance(3_600_000);
            rock = T(sim, SingleRockId);
            Assert.IsTrue(rock.Destroyed, "удары 1.8/2.6/2.8/3.6 с: 20 урона");
            Assert.AreEqual(2, rock.ReleasedUnits, "§7.1: при разрушении — до Total, полной награды сверху нет");
            CollectionAssert.AreEqual(new[] { 0, 1 }, Ordinals(W(sim, 2).Bag));
            Assert.AreEqual(0, W(sim, 1).Bag.Count);
            Assert.AreEqual(BigInteger.Zero, sim.State.Run.Wallet, "разрушение не меняет кошелёк");
            Assert.AreEqual(RunPhase.DrainingCargo, sim.State.Run.Phase, "§11.1: всё расчищено, ворот нет — доставка остатков");
            Assert.AreEqual(WorkerMode.Returning, W(sim, 2).Mode, "§6.3 п.4: работы нет, сумка непустая — частичный груз на базу");

            // Возврат W2: (2,1) → приём (3,4) — 4 ребра → 5.2 с; место 0 (2,4) — 1 ребро → 5.6 с; порции по 0.5 с.
            sim.Advance(5_200_000);
            Assert.AreEqual(map.IntakeCell, W(sim, 2).Cell, "§7.4: возвращение сначала к точке приёма");
            Assert.AreEqual(0, W(sim, 2).DepotSlot, "свободное место с меньшим номером");
            sim.Advance(5_600_000);
            Assert.AreEqual(WorkerMode.Unloading, W(sim, 2).Mode, "обслуживание — после прибытия на место");
            Assert.AreEqual(map.DepotCells[0], W(sim, 2).Cell);
            sim.Advance(6_100_000 - 1);
            Assert.AreEqual(BigInteger.Zero, sim.State.Run.Wallet, "до полного обслуживания порции золото в сумке");
            sim.Advance(6_100_000);
            Assert.AreEqual(new BigInteger(5), sim.State.Run.Wallet, "порция зачислена целиком одной транзакцией");
            Assert.AreEqual(1, W(sim, 2).Bag.Count);
            sim.Advance(6_600_000);
            var run = sim.State.Run;
            Assert.AreEqual(new BigInteger(10), run.Wallet, "§12.5: полная сдача приносит 10");
            Assert.AreEqual(new BigInteger(10), run.TotalCredited);
            Assert.AreEqual(2, run.Counters.DeliveredUnits);
            Assert.AreEqual(RunPhase.ReadyToContinue, run.Phase, "§11.1: последний груз сдан — завершение в ту же отметку");
            Assert.AreEqual(6_600_000L, run.Counters.CompletedTick);
            AssertInvariants(sim, "контроль §12.5");
        }

        [Test]
        public void PartialRelease_UnitsAppearBeforeDestruction_DestructionForcesTotal()
        {
            // ТЗ §7.1, §20 «Частичная добыча: порции появляются до окончательного разрушения прочного объекта».
            // Камень 120 HP / 12 порций, урон 5: выпуск floor(12·(120 − rem)/120) — порция на каждые 10 HP.
            var fx = BigRock();
            var sim = fx.NewSim();
            Assert.AreEqual(120 * S, T(sim, BigRockId).MaxHpUnits, "§12.4: rank01 = 1 → 20·(1+5) = 120");
            Assert.AreEqual(12, T(sim, BigRockId).TotalUnits);
            long firstRelease = -1;
            int lastBeforeDestroy = 0, prevReleased = 0;
            while (!T(sim, BigRockId).Destroyed)
            {
                Assert.IsTrue(StepTick(sim));
                var rock = T(sim, BigRockId);
                Assert.GreaterOrEqual(rock.ReleasedUnits, prevReleased, "Released монотонен");
                prevReleased = rock.ReleasedUnits;
                if (!rock.Destroyed)
                {
                    lastBeforeDestroy = rock.ReleasedUnits;
                    if (firstRelease < 0 && rock.ReleasedUnits > 0)
                    {
                        firstRelease = sim.Now;
                        Assert.LessOrEqual(rock.RemainingHpUnits, 110 * S, "первая порция — после 10 урона");
                        Assert.AreEqual(1, rock.ReleasedUnits);
                        Assert.AreEqual(1, AllUnits(sim.State.Run).Count(u => u.Source == BigRockId), "порция физически существует");
                    }
                }
                AssertInvariants(sim, "частичный выпуск");
            }
            Assert.Greater(firstRelease, 0, "порции появились до разрушения");
            Assert.AreEqual(11, lastBeforeDestroy, "перед добивающим ударом остаток 5 или 10 HP → floor(12·(120−r)/120) = 11");
            Assert.AreEqual(12, T(sim, BigRockId).ReleasedUnits, "разрушение — принудительно до Total");
            var keys = AllUnits(sim.State.Run).Where(u => u.Source == BigRockId).Select(u => u.Ordinal).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count(), "каждая порция существует один раз");
            Assert.IsTrue(keys.All(o => o >= 0 && o < 12));
            RunToReadyOrFail(sim, "частичный выпуск", null, (s, ev) => AssertInvariants(s, "доставка"));
            Assert.AreEqual(new BigInteger(14 * 5), sim.State.Run.TotalCredited, "12 + 2 порции по 5; отдельной награды за разрушение нет");
        }

        [Test]
        public void Zone1_WalletChangesOnlyByDeposits_TotalEqualsSumOfPortions()
        {
            // ТЗ §7.1 «Отдельная полная награда … не начисляется», §7.3, §20 «Учёт монет: разрушение не увеличивает кошелёк;
            // сдача увеличивает его ровно на стоимость груза». Без покупок/экипировки итог = аудит base_gold.
            var fx = Real(1);
            var sim = fx.NewSim();
            var audit = AuditZone(1);
            Assert.AreEqual((long)audit["hp"], sim.State.Run.Targets.Sum(t => t.MaxHpUnits) / S, "Σ MaxHp = аудит");
            Assert.AreEqual((int)audit["cargo_units"], sim.State.Run.Targets.Sum(t => t.TotalUnits), "Σ порций = аудит");
            BigInteger prevWallet = 0;
            int destroyWithoutDeposit = 0;
            RunToReadyOrFail(sim, "zone1", null, (s, ev) =>
            {
                var run = s.State.Run;
                BigInteger deposited = ev.Where(e => e.Type == OutpostEventType.CargoDeposited).Aggregate(BigInteger.Zero, (a, e) => a + e.Value);
                Assert.AreEqual(prevWallet + deposited, run.Wallet, "t=" + s.Now + ": кошелёк меняется только сдачей");
                if (deposited.IsZero && ev.Any(e => e.Type == OutpostEventType.TargetDestroyed)) destroyWithoutDeposit++;
                prevWallet = run.Wallet;
                AssertInvariants(s, "zone1");
            });
            var r = sim.State.Run;
            Assert.Greater(destroyWithoutDeposit, 0, "были разрушения без сдачи — кошелёк на них не менялся");
            Assert.AreEqual(AuditBig(audit["base_gold"]), r.TotalCredited, "сдано ровно Σ порций (аудит base_gold)");
            Assert.AreEqual(r.TotalCredited, r.Wallet);
            Assert.AreEqual((int)audit["cargo_units"], r.Counters.DeliveredUnits);
            Assert.AreEqual((int)audit["obligatory_targets"], r.Counters.ClearedTargets);
            Assert.IsTrue(r.Targets.All(t => t.Destroyed && t.ReleasedUnits == t.TotalUnits));
        }

        // ───────────────────────────── Разгрузка и склад ─────────────────────────────

        [Test]
        public void Unload_EachUnitCreditedAfterFullServiceTime_InKeyOrder()
        {
            // ТЗ §7.4: «Одна порция обслуживается за ceil(1e6/FinalUnloadRate) тиков… зачисляется одной транзакцией…
            // Сумка разгружается в порядке ключей порций»; §6.5 п.4 — по номеру места.
            var fx = Real(1);
            var sim = fx.NewSim();
            int deposits = 0;
            Drain(sim);
            while (sim.State.Run.Phase != RunPhase.ReadyToContinue)
            {
                long t = sim.NextEventTick();
                Assert.AreNotEqual(long.MaxValue, t);
                var due = sim.State.Run.Workers
                    .Where(w => w.Action != null && w.Action.Kind == ActionKind.Unload && w.Action.Deadline == t)
                    .ToDictionary(w => w.Id, w => (Value: w.Bag[0].Value, Key: Key(w.Bag[0]), Count: w.Bag.Count, Start: w.Action.SegmentStart));
                sim.Advance(t);
                var ev = Drain(sim).Where(e => e.Type == OutpostEventType.CargoDeposited).ToList();
                Assert.AreEqual(due.Count, ev.Count, "t=" + t + ": по одной сдаче на каждое завершённое обслуживание");
                foreach (var e in ev)
                {
                    Assert.IsTrue(due.ContainsKey(e.WorkerId));
                    var d = due[e.WorkerId];
                    Assert.AreEqual(d.Value, e.Value, "сдаётся первая по ключу порция с фиксированной стоимостью");
                    Assert.AreEqual(t - Unload0, d.Start, "обслуживание длилось ровно ceil(1e6/2)");
                    var w = W(sim, e.WorkerId);
                    Assert.IsFalse(w.Bag.Any(u => Key(u) == d.Key), "порция покинула сумку");
                    Assert.AreEqual(d.Count - 1, w.Bag.Count);
                    if (w.Bag.Count > 0)
                    {
                        Assert.AreEqual(WorkerMode.Unloading, w.Mode, "сумка не пуста — следующая порция");
                        Assert.AreEqual(t + Unload0, w.Action.Deadline);
                    }
                    deposits++;
                }
                for (int i = 1; i < ev.Count; i++) Assert.Less(ev[i - 1].Slot, ev[i].Slot, "место 0 раньше места 1 в одной отметке");
            }
            Assert.AreEqual(sim.State.Run.Counters.DeliveredUnits, deposits);
        }

        [Test]
        public void Depot_SimultaneousArrival_LowerWorkerIdGetsSlot0_BothSlotsServeInParallel()
        {
            // ТЗ §7.4: «FIFO определяется временем прибытия, при равенстве — workerId; место с меньшим номером выделяется первым»;
            // «два параллельных места», скорость каждого не зависит от очереди.
            var fx = TwoRocks();
            var map = fx.Map;
            var sim = fx.NewSim();
            Assert.AreEqual(TwoRocksA, W(sim, 1).TargetId, "равный путь 4: меньший якорь");
            Assert.AreEqual(Cell(map, 2, 2), W(sim, 1).WorkCell);
            Assert.AreEqual(TwoRocksB, W(sim, 2).TargetId, "у A уже один назначенный — B с тем же путём (§6.3)");
            Assert.AreEqual(Cell(map, 4, 2), W(sim, 2).WorkCell);

            // Прибытие 1.6 с, удары 2.6/3.6/4.6/5.6 с: на 5.6 с обе цели разрушены, у каждого по 2 порции.
            sim.Advance(5_600_000);
            Assert.IsTrue(T(sim, TwoRocksA).Destroyed && T(sim, TwoRocksB).Destroyed);
            Assert.AreEqual(2, W(sim, 1).Bag.Count);
            Assert.AreEqual(2, W(sim, 2).Bag.Count);
            Assert.AreEqual(RunPhase.DrainingCargo, sim.State.Run.Phase);

            // Возврат: по 4 ребра до приёма (3,5) → оба прибывают в 7.2 с.
            sim.Advance(7_200_000);
            var run = sim.State.Run;
            Assert.AreEqual(0, W(sim, 1).DepotSlot, "равное прибытие: W1 (меньший id) — место 0");
            Assert.AreEqual(1, W(sim, 2).DepotSlot, "W2 — место 1");
            Assert.AreEqual(0, run.Queue.Count);
            Assert.AreEqual(1, run.Slots[0].WorkerId);
            Assert.AreEqual(2, run.Slots[1].WorkerId);
            Assert.AreEqual(WorkerMode.Returning, W(sim, 1).Mode, "подход к месту — подзадача Returning (§7.4)");

            // Подход 1 ребро → 7.6 с; порции на 8.1 и 8.6 с — параллельно.
            sim.Advance(7_600_000);
            Assert.AreEqual(WorkerMode.Unloading, W(sim, 1).Mode);
            Assert.AreEqual(WorkerMode.Unloading, W(sim, 2).Mode);
            Assert.AreEqual(map.DepotCells[0], W(sim, 1).Cell);
            Assert.AreEqual(map.DepotCells[1], W(sim, 2).Cell);
            Drain(sim);
            sim.Advance(8_100_000);
            var ev = Drain(sim).Where(e => e.Type == OutpostEventType.CargoDeposited).ToList();
            Assert.AreEqual(2, ev.Count, "обе порции сданы на одной отметке");
            Assert.AreEqual(0, ev[0].Slot);
            Assert.AreEqual(1, ev[0].WorkerId);
            Assert.AreEqual(1, ev[1].Slot);
            Assert.AreEqual(2, ev[1].WorkerId);
            Assert.AreEqual(new BigInteger(10), sim.State.Run.Wallet);
            sim.Advance(8_600_000);
            Assert.AreEqual(new BigInteger(20), sim.State.Run.Wallet);
            Assert.AreEqual(RunPhase.ReadyToContinue, sim.State.Run.Phase);
            AssertInvariants(sim, "два места");
        }

        [Test]
        public void Depot_FifoByArrivalThenWorkerId_UnderQueuePressure()
        {
            // ТЗ §7.4, §20 «Два места склада: нет потери очереди и блокировки движения через базу».
            // Правка теста: 8 котиков, сумка 2, разгрузка 0.5 порции/с — очередь образуется постоянно.
            var cfg = ConfigsWith(j => { j["StartWorkers"] = 8; j["BaseCapacity"] = 2; j["BaseUnloadRate"] = "1/2"; });
            var fx = Real(5, cfg);
            var sim = fx.NewSim();
            Assert.AreEqual(8, sim.State.Run.Workers.Count);
            Assert.AreEqual(2_000_000L, StatUnloadTicks(sim));
            int maxQueue = 0, parallelTicks = 0, leaves = 0;
            for (int i = 0; i < 60_000 && sim.Now < 1200 * S && sim.State.Run.Phase != RunPhase.ReadyToContinue; i++)
            {
                long t = sim.NextEventTick();
                Assert.AreNotEqual(long.MaxValue, t);
                var preQueue = sim.State.Run.Queue.Select(q => (q.ArrivalTick, q.WorkerId)).ToList();
                var preSlot = sim.State.Run.Workers.ToDictionary(w => w.Id, w => w.DepotSlot);
                sim.Advance(t);
                var run = sim.State.Run;
                var leavers = run.Workers.Where(w => w.DepotSlot >= 0 && preSlot[w.Id] < 0).ToList();
                if (leavers.Count > 0)
                {
                    var arrival = preQueue.ToDictionary(q => q.WorkerId, q => q.ArrivalTick);
                    var candidates = new List<(long, int)>(preQueue);
                    foreach (var q in run.Queue)
                        if (!arrival.ContainsKey(q.WorkerId)) candidates.Add((q.ArrivalTick, q.WorkerId));
                    foreach (var w in leavers)
                        if (!arrival.ContainsKey(w.Id)) candidates.Add((t, w.Id));
                    candidates.Sort();
                    var leaverKeys = leavers.Select(w => (arrival.TryGetValue(w.Id, out long a) ? a : t, w.Id)).OrderBy(k => k).ToList();
                    CollectionAssert.AreEqual(candidates.Take(leavers.Count).ToList(), leaverKeys, "t=" + t + ": место получает голова FIFO");
                    for (int k = 1; k < leaverKeys.Count; k++)
                        Assert.Less(W(sim, leaverKeys[k - 1].Item2).DepotSlot, W(sim, leaverKeys[k].Item2).DepotSlot, "раньше в очереди — меньший номер места");
                    foreach (var w in leavers) Assert.AreEqual(WorkerMode.Returning, w.Mode, "подход к месту — подзадача Returning");
                    leaves += leavers.Count;
                }
                maxQueue = Math.Max(maxQueue, run.Queue.Count);
                if (run.Workers.Count(w => w.Mode == WorkerMode.Unloading) == 2) parallelTicks++;
                Drain(sim);
                AssertInvariants(sim, "очередь склада");
            }
            Assert.Greater(maxQueue, 1, "очередь образовалась");
            Assert.Greater(parallelTicks, 0, "оба места обслуживали одновременно");
            Assert.Greater(leaves, 20);
            Assert.GreaterOrEqual(sim.State.Run.Counters.MaxQueue, maxQueue);
        }

        // ───────────────────────────── Сумка, большой дроп, совместная добыча ─────────────────────────────

        [Test]
        public void BagFull_WorkerStopsCollectingAndReturnsToBase()
        {
            // ТЗ §6.3 п.1 «подбор и добыча запрещены до доставки»; §20 «Полная сумка: котик прекращает набор и возвращается».
            var fx = Real(1);
            var sim = fx.NewSim();
            int cap = StatCapacity(sim);
            Assert.AreEqual(4, cap, "§9: сумка 4 порции");
            var full = new HashSet<int>();
            int fullTrips = 0;
            RunToReadyOrFail(sim, "zone1", null, (s, ev) =>
            {
                foreach (var w in s.State.Run.Workers)
                {
                    if (w.Bag.Count == 0) { full.Remove(w.Id); continue; }
                    if (w.Bag.Count >= cap && full.Add(w.Id)) fullTrips++;
                    if (full.Contains(w.Id))
                        Assert.That(w.Mode, Is.EqualTo(WorkerMode.Returning).Or.EqualTo(WorkerMode.WaitingDepot).Or.EqualTo(WorkerMode.Unloading),
                            "w" + w.Id + ": после заполнения — только путь на базу и разгрузка");
                }
            });
            Assert.Greater(fullTrips, 0, "в zone1 сумки заполняются");
        }

        [Test]
        public void BigDrop_OverflowGoesToGroundAtLowestIdHitterCell_DeliveredLater()
        {
            // ТЗ §7.2: «Заполни свободные места сумок наносивших удар котиков в порядке workerId. Оставшиеся порции помести в одну
            // учётную партию на рабочую клетку котика с наименьшим ID»; §20 «Большой дроп». Правка теста: урон 100.
            var cfg = ConfigsWith(j => j["BaseDamage"] = "100");
            var fx = BigRock(cfg);
            var map = fx.Map;
            var sim = fx.NewSim();
            Assert.AreEqual(BigRockId, W(sim, 1).TargetId);
            Assert.AreEqual(Cell(map, 3, 3), W(sim, 1).WorkCell);
            Assert.AreEqual(BigRockId, W(sim, 2).TargetId);
            Assert.AreEqual(Cell(map, 2, 2), W(sim, 2).WorkCell);

            sim.Advance(2 * Edge0 + HitTicks); // 1.8 с: первый удар W1
            var run = sim.State.Run;
            var rock = T(sim, BigRockId);
            Assert.AreEqual(20 * S, rock.RemainingHpUnits);
            Assert.AreEqual(10, rock.ReleasedUnits, "floor(12·100/120) = 10");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, Ordinals(W(sim, 1).Bag), "сначала свободные места сумки (4)");
            Assert.AreEqual(1, run.Batches.Count, "остаток — одна партия");
            var batch = run.Batches[0];
            Assert.AreEqual(Cell(map, 3, 3), batch.Cell, "на рабочей клетке ударившего с наименьшим id");
            CollectionAssert.AreEqual(new[] { 4, 5, 6, 7, 8, 9 }, Ordinals(batch.Units));
            Assert.IsTrue(batch.Units.All(u => u.Source == BigRockId && u.Value == 5 && u.ReservedBy == 0));
            Assert.AreEqual(BigInteger.Zero, run.Wallet);
            Assert.AreEqual(WorkerMode.Returning, W(sim, 1).Mode, "сумка полна — к базе");

            sim.Advance(4 * Edge0 + HitTicks); // 2.6 с: W2 добивает 20 HP
            rock = T(sim, BigRockId);
            Assert.IsTrue(rock.Destroyed);
            Assert.AreEqual(12, rock.ReleasedUnits);
            CollectionAssert.AreEqual(new[] { 10, 11 }, Ordinals(W(sim, 2).Bag), "новые порции — в свободные места W2");
            Assert.AreEqual(1, sim.State.Run.Batches.Count);
            var w2 = W(sim, 2);
            Assert.AreEqual(WorkerMode.MovingToCargo, w2.Mode, "§6.3: существующий груз в приоритете перед новой добычей");
            var reserved = sim.State.Run.Batches[0].Units.Where(u => u.ReservedBy == 2).Select(u => u.Ordinal).OrderBy(o => o).ToArray();
            CollectionAssert.AreEqual(new[] { 4, 5 }, reserved, "резерв при назначении: min(свободно 2, на земле 6), наименьшие ключи (§7.2)");

            RunToReadyOrFail(sim, "большой дроп", null, (s, ev) => AssertInvariants(s, "большой дроп"));
            run = sim.State.Run;
            Assert.AreEqual(14, run.Counters.DeliveredUnits, "12 + 2 порции малого камня: всё с земли доставлено");
            Assert.AreEqual(new BigInteger(70), run.TotalCredited);
            Assert.AreEqual(0, run.Batches.Count);
        }

        [Test]
        public void JointMining_DamageSummedOnce_UnitsCreatedOnce_DistributedByWorkerId()
        {
            // ТЗ §6.5 п.2, §7.1 «Совместная добыча изменяет один общий объект и один счётчик выдачи», §7.2 порядок workerId,
            // §20 «Совместная добыча». Правка теста: скорость 1 клетка/с (события на целых секундах), урон 15.
            var cfg = ConfigsWith(j => { j["BaseMoveSpeed"] = "1"; j["BaseDamage"] = "15"; });
            var fx = BigRock(cfg);
            var sim = fx.NewSim();
            Assert.AreEqual(S, StatEdgeTicks(sim));
            Assert.AreEqual(15 * S, StatDamageUnits(sim));
            // W1: прибытие 2 с, удары 3, 4, 5 с…; W2: прибытие 4 с, удары 5, 6 с…
            sim.Advance(4 * S);
            var rock = T(sim, BigRockId);
            Assert.AreEqual(90 * S, rock.RemainingHpUnits, "два одиночных удара W1");
            Assert.AreEqual(3, rock.ReleasedUnits, "floor(12·30/120) = 3");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Ordinals(W(sim, 1).Bag));
            Assert.AreEqual(WorkerMode.Mining, W(sim, 2).Mode);
            Assert.AreEqual(5 * S, W(sim, 1).Action.Deadline);
            Assert.AreEqual(5 * S, W(sim, 2).Action.Deadline, "оба удара на 5 с");

            sim.Advance(5 * S);
            rock = T(sim, BigRockId);
            Assert.AreEqual(60 * S, rock.RemainingHpUnits, "Σурон 30 применён один раз");
            Assert.AreEqual(6, rock.ReleasedUnits, "floor(12·60/120) = 6: три новых порции, каждая создана один раз");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, Ordinals(W(sim, 1).Bag), "сначала свободное место W1 (меньший id)");
            CollectionAssert.AreEqual(new[] { 4, 5 }, Ordinals(W(sim, 2).Bag), "остаток — в сумку W2");
            Assert.AreEqual(0, sim.State.Run.Batches.Count);
            Assert.AreEqual(6, AllUnits(sim.State.Run).Count(u => u.Source == BigRockId));
            Assert.AreEqual(WorkerMode.Returning, W(sim, 1).Mode, "W1 полон");
            Assert.AreEqual(WorkerMode.Mining, W(sim, 2).Mode);
            Assert.AreEqual(6 * S, W(sim, 2).Action.Deadline);
            AssertInvariants(sim, "совместная добыча");
        }

        // ───────────────────────────── Фаза доставки и ворота ─────────────────────────────

        [Test]
        public void Draining_StartsOnlyAfterAllRsxDestroyedAndAllGatesOpen()
        {
            // ТЗ §11.1: «Фаза доставки остатков начинается после уничтожения всех r/x/s и открытия всех p. Последний обычный
            // камень при оставшихся закрытых воротах ещё не запускает эту фазу»; завершение — когда земля и сумки пусты.
            var fx = GateOnly();
            var sim = fx.NewSim();
            Assert.AreEqual(300 * S, T(sim, GateId).MaxHpUnits, "ворота: rank01 = 1, TypeHpFactor 2.5, AreaFactor 1 → 300 HP");
            Assert.AreEqual(30, T(sim, GateId).TotalUnits);
            while (!T(sim, GateR1).Destroyed)
            {
                Assert.IsTrue(StepTick(sim));
                AssertInvariants(sim, "сектор 0");
            }
            Assert.AreEqual(RunPhase.Running, sim.State.Run.Phase, "r/s/x расчищены, но ворота закрыты — ещё Running");
            Assert.AreEqual(0, sim.State.Run.GatesOpened);
            while (!T(sim, GateId).Destroyed)
            {
                Assert.IsTrue(StepTick(sim));
                if (!T(sim, GateId).Destroyed) Assert.AreEqual(RunPhase.Running, sim.State.Run.Phase);
                AssertInvariants(sim, "ворота");
            }
            Assert.AreEqual(1, sim.State.Run.GatesOpened, "ворота открыты в отметку обнуления HP (§6.5 п.5)");
            Assert.AreEqual(RunPhase.DrainingCargo, sim.State.Run.Phase, "после последних ворот — доставка остатков в ту же отметку");
            Assert.AreEqual(BigInteger.Zero, sim.State.Run.TotalSpent, "открытие ворот без расходов");

            RunToReadyOrFail(sim, "доставка остатков", null, (s, ev) => AssertInvariants(s, "доставка остатков"));
            var run = sim.State.Run;
            Assert.AreEqual(new BigInteger(2 * 5 + 30 * 5), run.TotalCredited, "камень 2×5 и ворота 30×5");
            Assert.AreEqual(RewardKey(1), run.CompletionRewardKey);
            Assert.AreEqual(1, sim.State.Profile.RewardLog.Count, "награда один раз");
            // §11.3: при ReadyToContinue нет продуктивных задач, резервов, очереди и частично обслуживаемых порций.
            Assert.AreEqual(0, run.Batches.Count);
            Assert.IsTrue(run.Workers.All(w => w.Bag.Count == 0 && w.Action == null && w.DepotSlot < 0 && w.CargoBatchSeq < 0));
            Assert.AreEqual(0, run.Queue.Count);
            Assert.IsTrue(run.Slots.All(sl => sl == null || sl.WorkerId == 0));
            Assert.AreEqual(long.MaxValue, sim.NextEventTick(), "экономика остановлена");
        }

        [TestCase(WorkerMode.PickingUp)]
        [TestCase(WorkerMode.MovingToCargo)]
        public void Draining_ReleasedReservation_WakesWorkerGoingHome_AtNextCell(WorkerMode pickerMode)
        {
            // ТЗ §6.3: «Если работы временно нет из-за чужих резервов, … пустой ждёт события. Освобождение резерва будит ожидающих»;
            // §6.2: «Ожидание → освободился резерв → Выбор задачи». В DrainingCargo ожидание пустого котика — путь в лагерь (§6.4),
            // поэтому идущий в лагерь тоже ожидающий. CORE_SPEC §7.1(b) будит только Idle — ТЗ приоритетнее.
            // §6.4: без телепортации — текущее ребро не прерывается, новый выбор в ближайшей логической клетке.
            // В DrainingCargo резерв освобождается только при падении вместимости: подбирающий (или идущий к грузу)
            // с сумкой ≥ 4 снимает рюкзак — вместимость 4, резерв снят сразу (PickingUp) или в ближайшей клетке (MovingToCargo).
            // Изоляция: свободного груза и Idle нет; до конца ближайшего ребра идущих в лагерь ни у кого, кроме них и снявшего
            // рюкзак, нет событий — иначе груз мог бы забрать перепланируемый по другой причине котик.
            // Согласованный снимок доставки из синтетической карты: 4 порции в сумке, 8 в резерве,
            // второй котик идёт домой. Поиск такого редкого состояния в кампании не проверяет правило детерминированно.
            var sim = DrainingReservationFixture(pickerMode);
            AssertInvariants(sim, "перед снятием рюкзака");
            Scenario found = TryScenario(sim, pickerMode);
            Assert.IsNotNull(found, "не найдена изолированная отметка DrainingCargo: " + pickerMode + " с сумкой ≥ 4 и пустой котик в пути к лагерю");

            string ctx = pickerMode + ", zone" + sim.State.Run.Zone + ", t=" + found.ReleaseTick;
            Assert.IsTrue(found.Woken.Count > 0, ctx);
            foreach (var id in found.Woken)
            {
                Assert.IsTrue(found.InvalidAtRelease[id], "w" + id + ": освобождение резерва будит идущего в лагерь (§6.3): " + ctx);
                Assert.IsTrue(found.EdgeKept[id], "w" + id + ": текущее ребро не прерывается и не пересчитывается (§6.4): " + ctx);
            }
            Assert.IsNotNull(found.Claimer, "к концу ближайшего ребра идущего в лагерь освобождённый груз снова зарезервирован: " + ctx);
            CollectionAssert.Contains(found.Woken, found.Claimer.Value, "груз взял разбуженный котик: " + ctx);
            Assert.That(W(sim, found.Claimer.Value).Mode, Is.EqualTo(WorkerMode.MovingToCargo).Or.EqualTo(WorkerMode.PickingUp), ctx);
            Assert.Greater(W(sim, found.PickerId).Bag.Count, 0, "груз забран раньше, чем снявший рюкзак разгрузился: " + ctx);
            AssertInvariants(sim, ctx, capacityMayDrop: true);

            RunToReadyOrFail(sim, ctx, null, (s, ev) => AssertInvariants(s, ctx, capacityMayDrop: true));
            Assert.AreEqual(0, sim.State.Run.Batches.Count, "весь груз сдан");
        }

        sealed class Scenario
        {
            public int PickerId;
            public long ReleaseTick;
            public List<int> Woken = new List<int>();
            public Dictionary<int, bool> InvalidAtRelease = new Dictionary<int, bool>();
            public Dictionary<int, bool> EdgeKept = new Dictionary<int, bool>();
            public int? Claimer;
        }

        static OutpostSimulation DrainingReservationFixture(WorkerMode pickerMode)
        {
            var fixture = BigRock(null, p => Wear(p, "backpack.legendary"));
            var state = fixture.NewState();
            var run = state.Run;
            foreach (var target in run.Targets)
            {
                target.RemainingHpUnits = 0;
                target.Destroyed = true;
                target.ReleasedUnits = target.TotalUnits;
            }
            run.Phase = RunPhase.DrainingCargo;
            run.NavVersion++;
            run.Counters.ClearedTargets = 2;
            run.Counters.ClearedTick = 0;
            run.Counters.ReleasedUnits = 14;
            run.Counters.ReleasedValue = 70;
            run.Counters.DeliveredUnits = 2;
            run.Counters.DeliveredValue = 10;
            run.TotalCredited = run.Wallet = 10;
            run.NextTaskId = 3;
            run.NextBatchSeq = 2;
            int cargoCell = Cell(fixture.Map, 3, 1);
            var batch = new CargoBatch { Seq = 1, Key = OutpostIds.BatchKey(1), Cell = cargoCell };
            run.Batches.Add(batch);
            for (int ordinal = 0; ordinal < 12; ordinal++)
            {
                var unit = new CargoUnit { Source = BigRockId, Ordinal = ordinal, Value = 5 };
                if (ordinal < 4) run.Workers[0].Bag.Add(unit);
                else
                {
                    unit.ReservedBy = 1;
                    unit.ReservedTask = 1;
                    batch.Units.Add(unit);
                }
            }
            foreach (var worker in run.Workers)
            {
                worker.TargetId = worker.WorkCell = worker.DepotSlot = -1;
                worker.TaskInvalid = false;
                worker.PathIndex = 0;
                worker.TaskId = worker.Id;
            }
            var picker = run.Workers[0];
            picker.Mode = pickerMode;
            picker.Cell = pickerMode == WorkerMode.PickingUp ? cargoCell : Cell(fixture.Map, 3, 2);
            picker.CargoBatchSeq = 1;
            picker.Path = pickerMode == WorkerMode.PickingUp ? null : new[] { cargoCell };
            long duration = pickerMode == WorkerMode.PickingUp ? Pickup0 : Edge0;
            picker.Action = new TimedAction
            {
                Kind = pickerMode == WorkerMode.PickingUp ? ActionKind.Pickup : ActionKind.Edge,
                Duration = duration,
                Deadline = pickerMode == WorkerMode.PickingUp ? duration : duration * 3 / 4,
                P0 = pickerMode == WorkerMode.PickingUp ? Rational.Zero : Rational.Of(1, 4),
            };
            var home = run.Workers[1];
            home.Mode = WorkerMode.GoingHome;
            home.Cell = Cell(fixture.Map, 4, 0);
            home.CargoBatchSeq = -1;
            home.Path = Enumerable.Range(1, 4).Select(row => Cell(fixture.Map, 4, row)).ToArray();
            home.Action = new TimedAction { Kind = ActionKind.Edge, Duration = Edge0, Deadline = Edge0, P0 = Rational.Zero };
            return new OutpostSimulation(state, fixture.Map, fixture.Configs);
        }

        /// <summary>Снять рюкзак, дойти до освобождения резерва и до конца ближайшего ребра идущих в лагерь; null — сценарий не изолирован.</summary>
        static Scenario TryScenario(OutpostSimulation sim, WorkerMode pickerMode)
        {
            var run = sim.State.Run;
            var picker = run.Workers.First(w => w.Mode == pickerMode && w.Bag.Count >= 4);
            int pid = picker.Id;
            var reserved = new HashSet<string>(GroundUnits(run).Where(u => u.ReservedBy == pid).Select(Key));
            if (reserved.Count == 0) return null;
            var before = EdgesOfGoingHome(sim);
            var r = sim.Submit(UnequipCmd(EquipSlot.Backpack));
            Assert.IsTrue(r.Ok, r.Reason);
            Assert.AreEqual(4, StatCapacity(sim));
            // MovingToCargo: резерв держится до ближайшей клетки снявшего рюкзак.
            for (int i = 0; i < 1000 && GroundUnits(sim.State.Run).Any(u => u.ReservedBy == pid); i++)
            {
                before = EdgesOfGoingHome(sim);
                if (!StepTick(sim)) return null;
            }
            run = sim.State.Run;
            if (GroundUnits(run).Any(u => u.ReservedBy == pid)) return null;
            var freed = GroundUnits(run).Where(u => reserved.Contains(Key(u))).ToList();
            if (freed.Count == 0 || freed.Any(u => u.ReservedBy != 0)) return null; // забран кем-то в той же отметке — не изолировано
            var walking = run.Workers.Where(w => w.Mode == WorkerMode.GoingHome && before.ContainsKey(w.Id)).ToList();
            if (walking.Count == 0) return null;
            long d = walking.Min(w => w.Action.Deadline);
            var walkingIds = new HashSet<int>(walking.Select(w => w.Id));
            foreach (var w in run.Workers)
            {
                if (w.Id == pid || walkingIds.Contains(w.Id)) continue;
                if (w.Mode == WorkerMode.Idle || (w.Action != null && w.Action.Deadline <= d)) return null;
            }
            var s = new Scenario { PickerId = pid, ReleaseTick = sim.Now };
            foreach (var w in walking)
            {
                s.Woken.Add(w.Id);
                s.InvalidAtRelease[w.Id] = w.TaskInvalid;
                s.EdgeKept[w.Id] = before[w.Id] == w.Action.Deadline;
            }
            while (sim.NextEventTick() <= d) Assert.IsTrue(StepTick(sim));
            var keys = new HashSet<string>(freed.Select(Key));
            var claimed = GroundUnits(sim.State.Run).Where(u => keys.Contains(Key(u)) && u.ReservedBy != 0).Select(u => u.ReservedBy)
                .Concat(sim.State.Run.Workers.Where(w => w.Bag.Any(u => keys.Contains(Key(u)))).Select(w => w.Id)).ToList();
            if (claimed.Count > 0) s.Claimer = claimed.Min();
            return s;
        }

        static Dictionary<int, long> EdgesOfGoingHome(OutpostSimulation sim) =>
            sim.State.Run.Workers.Where(w => w.Mode == WorkerMode.GoingHome).ToDictionary(w => w.Id, w => w.Action.Deadline);

        [Test]
        public void Gates_CannotBeBypassed_BecomeTaskAfterSector_OpenAutomaticallyWithoutGold()
        {
            // ТЗ §8.4: «Закрытые ворота задают логическую границу по всей ширине… После разрушения всех r/x/s текущего сектора
            // ворота становятся доступной задачей… Новые денежные затраты… не нужны»; §20 «Ворота: их нельзя обойти».
            var fx = GateTwoSectors();
            var map = fx.Map;
            var sim = fx.NewSim();
            Assert.AreEqual(1, map.Gates.Length);
            Assert.AreEqual(GateRow, map.Gates[0].Row);
            Assert.AreEqual(GateId, map.Gates[0].TargetId);
            Assert.AreEqual(0, MT(map, GateR1).Sector, "строка ворот 2 > 3? нет → сектор 0");
            Assert.AreEqual(1, MT(map, GateR2).Sector, "2 > 0 → сектор 1");
            Assert.AreEqual(0, MT(map, GateId).Cells.Length, "§8.4: у p нет footprint");
            long openTick = -1;
            bool gateWorked = false;
            RunToReadyOrFail(sim, "ворота", null, (s, ev) =>
            {
                var run = s.State.Run;
                Assert.AreEqual(BigInteger.Zero, run.TotalSpent, "открытие ворот не тратит золото");
                if (run.GatesOpened == 0)
                {
                    foreach (var w in run.Workers)
                    {
                        Assert.GreaterOrEqual(RowOf(map, w.Cell), GateRow, "котик не заходит выше закрытых ворот");
                        Assert.IsTrue(RemainingPath(w).All(c => RowOf(map, c) >= GateRow), "путь не ведёт за барьер");
                        Assert.AreNotEqual(GateR2, w.TargetId, "цель верхнего сектора недоступна");
                        if (w.TargetId == GateId && (w.Mode == WorkerMode.Mining || w.Mode == WorkerMode.MovingToWork))
                        {
                            Assert.IsTrue(T(s, GateR1).Destroyed, "ворота — задача только после расчистки нижнего сектора");
                            Assert.AreEqual(GateRow, RowOf(map, w.WorkCell), "рабочие позиции ворот — строка ворот");
                            gateWorked = true;
                        }
                    }
                    Assert.AreEqual(0, T(s, GateR2).ReleasedUnits);
                }
                else if (openTick < 0)
                {
                    openTick = s.Now;
                    Assert.IsTrue(T(s, GateId).Destroyed, "ворота открываются в отметку обнуления их HP");
                    Assert.IsTrue(ev.Any(e => e.Type == OutpostEventType.SectorOpened), "событие SectorOpened");
                    Assert.AreEqual(RunPhase.Running, run.Phase, "наверху остался R2");
                }
                AssertInvariants(s, "ворота");
            });
            Assert.IsTrue(gateWorked, "ворота добывались с нижней стороны");
            Assert.Greater(openTick, 0L);
            var r = sim.State.Run;
            Assert.IsTrue(T(sim, GateR2).Destroyed, "после открытия котики продолжили выше");
            Assert.AreEqual(new BigInteger(5 * (2 + 30 + 2)), r.TotalCredited, "сданы оба камня и груз ворот");
            Assert.AreEqual(BigInteger.Zero, r.TotalSpent);
        }

        // ───────────────────────────── Все виды x ─────────────────────────────

        static readonly string[] ObstructionTypes =
        {
            "block1x1v1", "block1x1v2", "block1x1v3", "block1x1v4", "block1x1v5", "block1x1v6", "block1x1v6L", "block1x1v6R",
            "block1x2", "block2x1", "block2x2", "block3x2", "block2x3", "block2x3vL", "block2x3vR", "block3x3",
            "waterfall1x4vL", "waterfall1x4vR", "wall1x1vL", "wall1x1vR",
        };

        [Test]
        public void ObstructionTypeList_MatchesSource()
        {
            // ТЗ §8.1: 20 типов Obstructions.
            CollectionAssert.AreEquivalent(TestData.Balance.Obstructions.Keys.ToArray(), ObstructionTypes);
        }

        /// <summary>
        /// ТЗ §8.2: «x — добываемый завал… получает HP и груз», «разрушенный x освобождает свои клетки»; §20 «Все виды x».
        /// Планировка: полоса копий типа поперёк всей ширины (для ширины 2/3 — второй слой копии над остатком столбцов),
        /// над ней камень (0,0). Пока все копии целы, область выше полосы недостижима; добыча камня доказывает освобождение footprint.
        /// </summary>
        [TestCaseSource(nameof(ObstructionTypes))]
        public void Obstruction_HasHp_BlocksUntilDestroyed_ThenFreesFootprint(string typeId)
        {
            var dto = TestData.Balance.Obstructions[typeId];
            int w = dto.WidthCells, h = dto.DepthCells, k = 7 / w;
            bool layer2 = 7 % w != 0;
            int top1 = 2 + (layer2 ? h : 0), depth = top1 + h + 1;
            var grid = new string[depth, 7];
            for (int r = 0; r < depth; r++)
                for (int c = 0; c < 7; c++) grid[r, c] = ".";
            grid[0, 0] = Rock1;
            grid[0, 6] = Exit;
            grid[depth - 1, 0] = Camp;
            var ids = new List<int>();
            for (int i = 0; i < k; i++)
            {
                grid[top1 + h - 1, i * w] = "x:" + typeId;
                ids.Add((top1 + h - 1) * 7 + i * w);
            }
            if (layer2)
            {
                grid[2 + h - 1, 7 - w] = "x:" + typeId;
                ids.Add((2 + h - 1) * 7 + 7 - w);
            }
            var rows = Enumerable.Range(0, depth).Select(r => string.Join(" ", Enumerable.Range(0, 7).Select(c => grid[r, c]))).ToArray();
            var fx = Synthetic(null, null, rows);
            var sim = fx.NewSim();

            long hp = AdaptedHpOracle(1, new FracB(1, 2), w * h, 'x');
            foreach (int id in ids)
            {
                var mt = MT(fx.Map, id);
                Assert.AreEqual('x', mt.Kind);
                Assert.AreEqual(w * h, mt.Cells.Length, typeId + ": footprint из Obstructions");
                var ts = T(sim, id);
                Assert.AreEqual(hp * S, ts.MaxHpUnits, typeId + ": HP §12.4 (rank01 = 1/2, TypeHpFactor 0.8, площадь " + w * h + ")");
                Assert.AreEqual(CargoUnitsOracle(hp), ts.TotalUnits);
                Assert.AreEqual(new FracB(1, 2), FracB.Parse(ts.ValueFactor), "x: BaseCoinValueFactor 0.5");
            }
            bool breached = false;
            RunToReadyOrFail(sim, typeId, null, (s, ev) =>
            {
                if (!ids.Any(id => T(s, id).Destroyed))
                    foreach (var wk in s.State.Run.Workers)
                        Assert.GreaterOrEqual(RowOf(fx.Map, wk.Cell), top1, typeId + ": пока завалы целы, область выше полосы недоступна");
                if (s.State.Run.Workers.Any(wk => RowOf(fx.Map, wk.Cell) < top1)) breached = true;
                AssertInvariants(s, typeId);
            });
            Assert.IsTrue(breached, typeId + ": после разрушения котики прошли через освобождённые клетки");
            Assert.IsTrue(T(sim, 0).Destroyed, "камень за полосой добыт");
            foreach (int id in ids)
            {
                Assert.IsTrue(T(sim, id).Destroyed);
                Assert.AreEqual(T(sim, id).TotalUnits, T(sim, id).ReleasedUnits);
            }
            BigInteger expected = ids.Count * CargoUnitsOracle(hp) * CoinOracle(1, new FracB(1, 2), 0, Zero) + 2 * 5;
            Assert.AreEqual(expected, sim.State.Run.TotalCredited, "x: порции по ceil(5·0.5) = 3, камень 2×5");
        }

        [Test]
        public void AllZones_EveryTargetHasExactHpAndCargo_SumsMatchAudit()
        {
            // ТЗ §8.2 «Нельзя оставить обычные x навсегда неразрушимыми», §12.4: HP/порции фиксируются при создании шахты.
            // Профиль b6: суммы аудита источника — базовые HP §12.4, поэтому run — на профиле без множителя темпа K(z) (решение
            // владельца В4, 15 октября 2026); слой K проверяет OutpostBalanceRegressionTests.Pace_B6_RunTargetsAndPrices.
            var rules = Configs.WithoutPace();
            for (int z = 1; z <= 144; z++)
            {
                var map = Map(z);
                var run = Real(z, rules).NewState().Run;
                var audit = AuditZone(z);
                Assert.AreEqual((long)audit["hp"], run.Targets.Sum(t => t.MaxHpUnits) / S, "zone" + z + ": Σ HP");
                Assert.AreEqual((int)audit["cargo_units"], run.Targets.Sum(t => t.TotalUnits), "zone" + z + ": Σ порций");
                Assert.AreEqual((int)audit["obligatory_targets"], run.Targets.Count, "zone" + z + ": обязательные r/s/x/p");
                Assert.AreEqual((long)audit["min_adapted_hp"], run.Targets.Min(t => t.MaxHpUnits) / S);
                Assert.AreEqual((long)audit["max_adapted_hp"], run.Targets.Max(t => t.MaxHpUnits) / S);
                foreach (var t in run.Targets)
                {
                    Assert.AreEqual(0, t.MaxHpUnits % S, "MaxHpUnits = MaxHp·10^6");
                    Assert.AreEqual(t.MaxHpUnits, t.RemainingHpUnits);
                    Assert.AreEqual(0, t.ReleasedUnits);
                    var mt = MT(map, t.Id);
                    // ТЗ 2.2 §4.2, §16.1 (а): adapter-2 — у цепей заводов (s) груза нет, у r/x/p — прежняя формула.
                    Assert.AreEqual(CargoUnitsOracle(t.MaxHpUnits / S, mt.Kind), t.TotalUnits);
                    if (mt.Kind == 'x')
                        Assert.AreEqual(AdaptedHpOracle(z, new FracB(1, 2), mt.Cells.Length, 'x') * S, t.MaxHpUnits, "zone" + z + " x " + t.Id);
                }
            }
        }

        // ───────────────────────────── Длинный прогон ─────────────────────────────

        [Test]
        public void LongRun_Zone5_PolicyC_InvariantsHoldAfterEveryTickAndReload()
        {
            // ТЗ §20: «Для тестов целостности инварианты проверяются после событий и save/load, а не только в финале».
            var fx = Real(5);
            var sim = fx.NewSim();
            var policy = new PolicyCB();
            long ticks = 0;
            Drain(sim);
            while (sim.State.Run.Phase != RunPhase.ReadyToContinue)
            {
                Assert.IsTrue(StepTick(sim), "нет событий при незавершённой карте");
                Drain(sim);
                AssertInvariants(sim, "zone5/C после событий");
                if (policy.Act(sim)) AssertInvariants(sim, "zone5/C после покупки");
                if (++ticks % 997 == 0)
                {
                    string json = Json(sim.State);
                    sim = fx.Load(json);
                    AssertSameJson(json, Json(sim.State), "save/load не меняет состояние");
                    AssertInvariants(sim, "zone5/C после загрузки");
                }
                Assert.IsFalse(sim.State.Run.WatchdogFired, sim.State.Run.WatchdogDiagnostic);
                Assert.Less(ticks, (long)EventLimit);
                Assert.LessOrEqual(sim.Now, DayTicks);
            }
            Assert.Greater(policy.Purchases, 5, "политика C покупала");
            var run = sim.State.Run;
            Assert.AreEqual(run.TotalCredited - run.TotalSpent, run.Wallet);
            Assert.AreEqual((int)AuditZone(5)["cargo_units"], run.Counters.DeliveredUnits, "весь груз сдан");
            Assert.AreEqual(1, run.GatesOpened);
            Assert.AreEqual(1, sim.State.Profile.RewardLog.Count);
        }
    }
}
