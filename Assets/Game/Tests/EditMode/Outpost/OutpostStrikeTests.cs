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
    /// Удар кирки игрока (ТЗ 6.6): урон TapDamageFactor · урон удара котика, порции по порогам HP на землю в ближайшую свободную
    /// клетку, деньги только при сдаче, цели текущего сектора и очередные ворота после расчистки сектора, разрушение и фаза 5
    /// сразу после команды, отказы без изменений, детерминизм и полный прогон zone1 с override арены.
    /// Ожидания посчитаны вручную по ТЗ: 20 HP камня, урон котика 5 → удар игрока 1,25 HP = 1 250 000 долей; 2 порции по 5.
    /// </summary>
    public class OutpostStrikeTests
    {
        const long Tap0 = 1_250_000;

        static OutpostCommand Strike(int id) => OutpostCommand.Strike(id);

        static List<OutpostEvent> Hit(OutpostSimulation sim, int id, int times)
        {
            var events = new List<OutpostEvent>();
            for (int i = 0; i < times; i++)
            {
                var r = sim.Submit(Strike(id));
                Assert.IsTrue(r.Ok, "удар " + (i + 1) + " по " + id + ": " + r.Reason);
                events.AddRange(Drain(sim));
            }
            return events;
        }

        // ───────────────────────────── Урон и груз ─────────────────────────────

        [Test]
        public void Strike_QuarterOfCatHit_PortionsByHpThreshold_OnGroundNearBase_WalletUnchanged()
        {
            // ТЗ 6.6: «удар игрока наносит TapDamageFactor (1/4) урона удара котика»; порции — по той же формуле §7.1;
            // груз ложится на землю, золото — только при сдаче (§7.3).
            var fx = SingleRock();
            var sim = fx.NewSim();
            Drain(sim);
            Assert.AreEqual(Tap0, sim.Stats.TapDamageUnits, "5 · 1/4 HP");
            var first = sim.Submit(Strike(SingleRockId));
            Assert.IsTrue(first.Ok);
            Assert.AreEqual(new BigInteger(Tap0), first.Cost, "Cost — урон в долях HP");
            Assert.AreEqual(0, first.Levels, "1,25 из 20 HP: порция ещё не выпущена");
            var ev = Drain(sim);
            var struck = ev.Single(e => e.Type == OutpostEventType.TargetStruck);
            Assert.AreEqual(SingleRockId, struck.TargetId);
            Assert.AreEqual(new BigInteger(Tap0), struck.Value);
            Assert.AreEqual(20 * S - Tap0, T(sim, SingleRockId).RemainingHpUnits);
            Assert.AreEqual(0, sim.State.Run.Batches.Count);

            ev = Hit(sim, SingleRockId, 7);   // всего 8 ударов = 10 HP → floor(2 · 10/20) = 1 порция
            Assert.AreEqual(1, T(sim, SingleRockId).ReleasedUnits);
            var rel = ev.Single(e => e.Type == OutpostEventType.CargoReleased);
            Assert.AreEqual(-1, rel.WorkerId, "выпуск без котика");
            Assert.AreEqual(1, rel.Count);
            var batch = sim.State.Run.Batches.Single();
            // Рабочие клетки камня (3,1): (3,0), (2,1), (4,1), (3,2); ближе всех к приёму (3,4) — (3,2), 2 ребра.
            Assert.AreEqual(Cell(sim.Map, 3, 2), batch.Cell);
            Assert.AreEqual(batch.Seq, rel.BatchSeq);
            Assert.AreEqual(new BigInteger(5), batch.Units.Single().Value);
            Assert.AreEqual(BigInteger.Zero, sim.State.Run.Wallet, "удар не начисляет золото");
            Assert.AreEqual(BigInteger.Zero, sim.State.Run.TotalCredited);
            AssertInvariants(sim, "после ударов");

            RunToReadyOrFail(sim, "доставка", null, (s, e) => AssertInvariants(s, "доставка"));
            Assert.AreEqual(new BigInteger(10), sim.State.Run.TotalCredited, "2 порции по 5 — как без ударов");
        }

        [Test]
        public void Strike_Destroying_FreesFootprint_DropsOnOwnCell_PhaseFiveRunsImmediately()
        {
            // ТЗ 6.6: разрушение ударом — как от котиков (§8.2: footprint освобождается); фаза 5 сразу после команды (§6.5, §11.1).
            var fx = SingleRock();
            var sim = fx.NewSim();
            Drain(sim);
            var ev = Hit(sim, SingleRockId, 16);
            var ts = T(sim, SingleRockId);
            Assert.IsTrue(ts.Destroyed);
            Assert.AreEqual(2, ts.ReleasedUnits);
            Assert.AreEqual(1, ev.Count(e => e.Type == OutpostEventType.TargetDestroyed));
            var last = ev.Last(e => e.Type == OutpostEventType.CargoReleased);
            Assert.AreEqual(SingleRockId, last.Cell, "остаток — в освобождённую клетку цели (3,1)");
            Assert.IsFalse(sim.IsOccupied(SingleRockId));
            Assert.AreEqual(RunPhase.DrainingCargo, sim.State.Run.Phase, "единственная цель расчищена той же командой");
            Assert.AreEqual(1, ev.Count(e => e.Type == OutpostEventType.DrainingStarted));
            Assert.IsTrue(sim.State.Run.Workers.All(w => w.Mode != WorkerMode.Mining), "добывающих разрушенной цели нет");
            AssertInvariants(sim, "после разрушения ударом");
            Assert.AreEqual("phase", sim.Submit(Strike(SingleRockId)).Reason, "в DrainingCargo удар запрещён");

            RunToReadyOrFail(sim, "доставка", null, (s, e) => AssertInvariants(s, "доставка"));
            Assert.AreEqual(new BigInteger(10), sim.State.Run.TotalCredited);
        }

        [Test]
        public void Strike_MinedTarget_DestroyedByPlayer_MinersReplanToCargo()
        {
            // ТЗ 6.2: добывающий, чью цель разрушили, сразу перепланируется; свободный груз — задача подбора (§6.3 п.2).
            var fx = TwoRocks();
            var sim = fx.NewSim();
            Drain(sim);
            while (W(sim, 1).Mode != WorkerMode.Mining) Assert.IsTrue(StepTick(sim));
            int target = W(sim, 1).TargetId;
            while (!T(sim, target).Destroyed)
            {
                Assert.IsTrue(sim.Submit(Strike(target)).Ok);
                AssertInvariants(sim, "удары по добываемой цели");
            }
            Assert.AreNotEqual(target, W(sim, 1).TargetId, "котик не держит разрушенную цель");
            Assert.AreEqual(RunPhase.Running, sim.State.Run.Phase, "второй камень жив");
            Assert.AreEqual("destroyed", sim.Submit(Strike(target)).Reason);
            RunToReadyOrFail(sim, "две цели", null, (s, e) => AssertInvariants(s, "две цели"));
            Assert.AreEqual(new BigInteger(20), sim.State.Run.TotalCredited);
        }

        [Test]
        public void Strike_EnclosedTarget_DropsToNearestFreeLayer_ReachableFirst()
        {
            // Цель (3,2) окружена камнями: слой 1 занят; в слое 2 свободны (3,0), (1,2), (5,2), (3,4) — достижима ближе всех (3,4).
            var fx = Synthetic(null, null,
                ". . . . . . " + Exit,
                ". . " + Rock1 + " " + Rock1 + " " + Rock1 + " . .",
                ". . " + Rock1 + " " + Rock1 + " " + Rock1 + " . .",
                ". . " + Rock1 + " " + Rock1 + " " + Rock1 + " . .",
                Camp + " . . . . . .");
            var sim = fx.NewSim();
            Drain(sim);
            int id = Cell(sim.Map, 3, 2);
            Assert.IsNull(sim.StrikeBlockReason(id), "закрытая соседями цель текущего сектора доступна удару");
            Hit(sim, id, 8);
            Assert.AreEqual(Cell(sim.Map, 3, 4), sim.State.Run.Batches.Single().Cell);
            AssertInvariants(sim, "закрытая цель");
            RunToReadyOrFail(sim, "закрытая цель", null, (s, e) => AssertInvariants(s, "закрытая цель"));
            Assert.AreEqual(new BigInteger(9 * 2 * 5), sim.State.Run.TotalCredited, "9 камней по 2 порции");
        }

        [Test]
        public void Strike_OnlyClosedPocketNearby_CargoWaitsInPocket_DeliveredAfterClearance()
        {
            // Слой 1 цели (3,2): свободна только замкнутая клетка (2,2) — груз ждёт в ней и сдаётся после расчистки (§11.1).
            string R = Rock1;
            var fx = Synthetic(null, null,
                ". . . . . . " + Exit,
                ". " + R + " " + R + " " + R + " " + R + " " + R + " .",
                ". " + R + " . " + R + " " + R + " " + R + " .",
                ". " + R + " " + R + " " + R + " " + R + " " + R + " .",
                Camp + " . . . . . .");
            var sim = fx.NewSim();
            Drain(sim);
            int id = Cell(sim.Map, 3, 2), pocket = Cell(sim.Map, 2, 2);
            Hit(sim, id, 8);
            var batch = sim.State.Run.Batches.Single();
            Assert.AreEqual(pocket, batch.Cell);
            Assert.AreEqual(-1, sim.Navigation.Distance(sim.Map.IntakeCell, pocket), "карман пока недостижим");
            AssertInvariants(sim, "карман");
            RunToReadyOrFail(sim, "карман", null, (s, e) => AssertInvariants(s, "карман"));
            Assert.AreEqual(new BigInteger(14 * 2 * 5), sim.State.Run.TotalCredited, "весь груз, включая карман, сдан");
        }

        // ───────────────────────────── Секторы и ворота ─────────────────────────────

        [Test]
        public void Strike_TargetsBehindGateLocked_GateOnlyAfterSector_OpensImmediately()
        {
            // ТЗ 8.4: за закрытыми воротами ничего не добывается; ворота — задача после расчистки сектора; открытие без золота.
            var fx = GateTwoSectors();
            var sim = fx.NewSim();
            Drain(sim);
            Assert.AreEqual("locked", sim.StrikeBlockReason(GateR2), "камень за воротами");
            Assert.AreEqual("locked", sim.StrikeBlockReason(GateId), "ворота до расчистки сектора");
            Assert.IsNull(sim.StrikeBlockReason(GateR1));
            string before = Json(sim.State);
            var denied = sim.Submit(Strike(GateR2));
            Assert.IsFalse(denied.Ok);
            Assert.AreEqual("locked", denied.Reason);
            AssertSameJson(before, Json(sim.State), "отказ ничего не меняет");

            Hit(sim, GateR1, 16);
            Assert.IsNull(sim.StrikeBlockReason(GateId), "сектор расчищен — ворота доступны");
            Assert.AreEqual("locked", sim.StrikeBlockReason(GateR2), "барьер ещё закрыт");
            // 300 HP / 1,25 = 240 ударов.
            var ev = Hit(sim, GateId, 239);
            Assert.AreEqual(0, sim.State.Run.GatesOpened);
            ev = Hit(sim, GateId, 1);
            Assert.AreEqual(1, sim.State.Run.GatesOpened, "ворота открылись той же командой");
            Assert.AreEqual(1, ev.Count(e => e.Type == OutpostEventType.SectorOpened));
            Assert.IsNull(sim.StrikeBlockReason(GateR2), "следующий сектор открыт");
            Assert.IsTrue(sim.State.Run.Batches.All(b => RowOf(sim.Map, b.Cell) >= GateRow), "груз ворот — на нижней стороне");
            AssertInvariants(sim, "ворота");
            RunToReadyOrFail(sim, "ворота", null, (s, e) => AssertInvariants(s, "ворота"));
            Assert.AreEqual(new BigInteger((2 + 30 + 2) * 5), sim.State.Run.TotalCredited);
        }

        // ───────────────────────────── Отказы ─────────────────────────────

        [Test]
        public void Strike_Rejections_LeaveStateUnchanged()
        {
            var fx = TwoRocks();
            var sim = fx.NewSim();
            Drain(sim);
            string before = Json(sim.State);
            Assert.AreEqual("unknown", sim.Submit(Strike(0)).Reason, "клетка без цели");
            Assert.AreEqual("unknown", sim.Submit(Strike(-5)).Reason);
            Assert.AreEqual("unknown", sim.Submit(Strike(10_000)).Reason);
            AssertSameJson(before, Json(sim.State), "отказы ничего не меняют");
            Assert.AreEqual(0, Drain(sim).Count, "и не создают событий");

            RunToReadyOrFail(sim, "две цели");
            before = Json(sim.State);
            Assert.AreEqual("phase", sim.Submit(Strike(TwoRocksA)).Reason, "ReadyToContinue");
            AssertSameJson(before, Json(sim.State), "после итогов удар ничего не меняет");

            var off = SingleRock(ConfigsWith(j => j["TapDamageFactor"] = "0"));
            var simOff = off.NewSim();
            Assert.AreEqual(0, simOff.Stats.TapDamageUnits);
            Assert.AreEqual("disabled", simOff.Submit(Strike(SingleRockId)).Reason, "TapDamageFactor = 0 выключает удары");
            Assert.Throws<OutpostConfigException>(() => ConfigsWith(j => j["TapDamageFactor"] = "-1/4"));
        }

        // ───────────────────────────── Характеристики ─────────────────────────────

        [Test]
        public void Strike_DamageFollowsPickaxesAndEquipment_ScheduledAfterPurchaseUsesNewDamage()
        {
            // ТЗ 12.2/12.3: урон удара игрока — доля FinalDamage команды (локальные «Кирки» и надетая кирка).
            var worn = SingleRock(null, p => Wear(p, "pickaxe.common"));
            Assert.AreEqual(1_350_000L, worn.NewSim().Stats.TapDamageUnits, "5 · 1,08 / 4 = 1,35 HP");

            var fx = SingleRock();
            var sim = fx.NewSim();
            GiveGold(sim.State, 1000);
            sim.Schedule(Buy(UpgradeBranch.Pickaxes), 100_000);
            sim.Schedule(Strike(SingleRockId), 100_000);
            sim.Advance(100_000);
            Assert.AreEqual(2, sim.ScheduledResults.Count);
            Assert.IsTrue(sim.ScheduledResults[0].Ok);
            Assert.IsTrue(sim.ScheduledResults[1].Ok);
            Assert.AreEqual(new BigInteger(1_525_000), sim.ScheduledResults[1].Cost, "та же отметка после покупки: 6,1 / 4 = 1,525 HP");
            Assert.AreEqual(1_525_000L, sim.Stats.TapDamageUnits);
            AssertInvariants(sim, "покупка и удар одной отметки");
        }

        // ───────────────────────────── Zone1 с override арены ─────────────────────────────

        static OutpostSimulation Zone1Arena()
        {
            var map = OutpostLevelAdapter.Build(TestData.Balance, 1, Configs.Overrides);
            var st = StageProgressionService.NewGame(TestData.Balance, Configs, Seed, StartUtcMs, 1);
            return new OutpostSimulation(st, map, Configs);
        }

        /// <summary>
        /// Политика A с ударом раз в <paramref name="periodTicks"/> по наибольшему (ближе к базе) или наименьшему Id. После
        /// <paramref name="strikeLimit"/> ударов либо продолжает без ударов до ReadyToContinue, либо (stopAtLimit) возвращается сразу.
        /// </summary>
        static int RunWithStrikes(OutpostSimulation sim, long periodTicks, bool nearBase, int strikeLimit, bool stopAtLimit = false)
        {
            long nextStrike = sim.Now + periodTicks;
            int strikes = 0;
            long steps = 0;
            Drain(sim);
            while (sim.State.Run.Phase != RunPhase.ReadyToContinue)
            {
                Assert.IsFalse(sim.State.Run.WatchdogFired, sim.State.Run.WatchdogDiagnostic);
                Assert.Less(++steps, (long)EventLimit);
                long next = sim.NextEventTick();
                if (strikes < strikeLimit && sim.State.Run.Phase == RunPhase.Running && nextStrike <= next)
                {
                    sim.Advance(nextStrike);
                    var ids = sim.Map.Targets.Where(t => sim.CanStrike(t.Id)).Select(t => t.Id).ToList();
                    if (ids.Count > 0)
                    {
                        var r = sim.Submit(Strike(nearBase ? ids.Max() : ids.Min()));
                        Assert.IsTrue(r.Ok, r.Reason);
                        strikes++;
                        if (strikes % 40 == 0) AssertInvariants(sim, "удар " + strikes);
                        if (stopAtLimit && strikes >= strikeLimit)
                        {
                            Drain(sim);
                            return strikes;
                        }
                    }
                    nextStrike += periodTicks;
                }
                else
                {
                    Assert.AreNotEqual(long.MaxValue, next, "нет событий при незавершённой карте");
                    sim.Advance(next);
                }
                Drain(sim);
            }
            return strikes;
        }

        [Test]
        public void Zone1Arena_StrikesSpeedUpClearance_TotalGoldUnchanged()
        {
            // Override арены (ТЗ 8.5) + удары игрока: сдано ровно Σ порций аудита (удары не создают золото), шахта быстрее.
            var audit = (Newtonsoft.Json.Linq.JObject)Audit["level_overrides"]["zone1"]["effective"];
            var plain = Zone1Arena();
            RunToReadyOrFail(plain, "zone1 без ударов");
            var tapped = Zone1Arena();
            Assert.AreEqual((int)audit["obligatory_targets"], tapped.State.Run.Targets.Count);
            int strikes = RunWithStrikes(tapped, 200_000, true, int.MaxValue);
            var run = tapped.State.Run;
            TestContext.WriteLine("zone1 arena: A " + plain.State.Run.Counters.CompletedTick / (double)S + " s, A+5 ударов/с " +
                run.Counters.CompletedTick / (double)S + " s, ударов " + strikes);
            Assert.Greater(strikes, 0);
            Assert.AreEqual(AuditBig(audit["base_gold"]), plain.State.Run.TotalCredited);
            Assert.AreEqual(AuditBig(audit["base_gold"]), run.TotalCredited, "удары не меняют сумму золота шахты");
            Assert.AreEqual((int)audit["cargo_units"], run.Counters.DeliveredUnits);
            Assert.Less(run.Counters.CompletedTick, plain.State.Run.Counters.CompletedTick, "удары ускоряют шахту");
            AssertInvariants(tapped, "итог zone1");
        }

        [Test]
        public void Zone1Arena_StrikesDeepInRock_Deterministic_SaveLoadInvariant()
        {
            // Удары по наименьшему Id — в глубине массива: груз в карманах, проходы появляются сверху. Повтор и save/load дают
            // побайтно тот же итог (детерминизм ТЗ 6.5, §17).
            string Play(bool reload)
            {
                var sim = Zone1Arena();
                Assert.AreEqual(120, RunWithStrikes(sim, 300_000, false, 120, stopAtLimit: true));
                AssertInvariants(sim, "после 120 ударов");
                if (reload) sim = new OutpostSimulation(FromJson(Json(sim.State)), sim.Map, Configs);
                RunToReadyOrFail(sim, "zone1 после ударов", null, (s, e) => { });
                AssertInvariants(sim, "итог");
                return Json(sim.State);
            }
            string a = Play(false), b = Play(false), c = Play(true);
            AssertSameJson(a, b, "повтор");
            AssertSameJson(a, c, "save/load после ударов");
            var final = FromJson(a);
            var audit = (Newtonsoft.Json.Linq.JObject)Audit["level_overrides"]["zone1"]["effective"];
            Assert.AreEqual(AuditBig(audit["base_gold"]), final.Run.TotalCredited);
        }
    }
}
