using System;
using System.Linq;
using System.Numerics;
using CatMiners.Core;
using CatMiners.Core.Outpost;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Детерминизм ядра (ТЗ §6.4, §6.5, §13, §20 «Разбиение времени», «Офлайн и онлайн»): Advance(a+b) == Advance(a); Advance(b),
    /// произвольные кадры, save/load посередине, после ReadyToContinue. Критерий — побайтно одинаковый JSON состояния
    /// (кошелёк, HP в долях, ключи/цены порций, резервы, очередь, задачи, пути, таймеры, PRNG).
    /// </summary>
    public class OutpostDeterminismTests
    {
        /// <summary>Длительность прогона: zone5 — с открытием ворот, остальные — 10 мин.</summary>
        static long Duration(int zone) => zone == 5 ? 1500 * S : 600 * S;

        static string Whole(OutpostFixtureB fx, long total)
        {
            var sim = fx.NewSim();
            sim.Advance(total);
            return Json(sim.State);
        }

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(10)]
        [TestCase(50)]
        public void SingleAdvance_EqualsTwoParts(int zone)
        {
            // ТЗ §13: «Для любого допустимого интервала без команд требуется Advance(a+b) == Advance(a); Advance(b)».
            var fx = Real(zone);
            long total = Duration(zone);
            string expected = Whole(fx, total);
            foreach (long a in new[] { 1L, 123_457L, 37 * S + 1, total / 3, total - 1 })
            {
                var split = fx.NewSim();
                split.Advance(a);
                split.Advance(total);
                AssertSameJson(expected, Json(split.State), "zone" + zone + ": разрез в " + a);
            }
        }

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(10)]
        [TestCase(50)]
        public void RandomFrameSteps_EqualSingleAdvance(int zone)
        {
            // ТЗ §13: «Онлайн … накопление точного остатка тиков; не округляй каждый deltaTime»; §20 «разные кадровые интервалы».
            var fx = Real(zone);
            long total = Duration(zone);
            string expected = Whole(fx, total);
            var rng = new Pcg32(777UL + (ulong)zone, 9);
            var framed = fx.NewSim();
            long now = 0;
            int frames = 0;
            while (now < total)
            {
                // От 1 мкс до 50 мс; каждый четвёртый кадр — короткий, попадающий внутрь рёбер и обслуживания.
                long step = rng.NextInt(4) == 0 ? 1 + rng.NextInt(1000) : 1 + rng.NextInt(50_000);
                now = Math.Min(total, now + step);
                framed.Advance(now);
                frames++;
            }
            Assert.Greater(frames, 1000);
            AssertSameJson(expected, Json(framed.State), "zone" + zone + ": " + frames + " кадров");
        }

        [Test]
        public void SixtyHzFrames_EqualSingleAdvance()
        {
            // Кадры 60 Гц: границы от начала серии, сумма N кадров ровно N/60 с.
            var fx = Real(1);
            long total = 300 * S;
            string expected = Whole(fx, total);
            var sim = fx.NewSim();
            for (long i = 0; i * S / 60 < total; i++)
                sim.Advance(Math.Min(total, (i + 1) * S / 60));
            AssertSameJson(expected, Json(sim.State), "60 Гц");
        }

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(10)]
        [TestCase(50)]
        public void SaveLoadBetweenParts_EqualSingleAdvance(int zone)
        {
            // ТЗ §6.5: «При save/load восстанавливаются прогресс, длительность и прежний deadline, без нового округления остатка»;
            // §13: равенство «в том числе с save/load между частями».
            var fx = Real(zone);
            long total = Duration(zone);
            string expected = Whole(fx, total);
            var sim = fx.NewSim();
            var rng = new Pcg32(4242UL + (ulong)zone, 9);
            long now = 0;
            int reloads = 0;
            while (now < total)
            {
                now = Math.Min(total, now + 1 + rng.NextInt(20_000_000));
                sim.Advance(now);
                string json = Json(sim.State);
                sim = fx.Load(json);
                AssertSameJson(json, Json(sim.State), "загрузка не меняет состояние");
                reloads++;
            }
            Assert.Greater(reloads, 10);
            AssertSameJson(expected, Json(sim.State), "zone" + zone + ": " + reloads + " перезагрузок");
        }

        [Test]
        public void AfterReadyToContinue_SplitsAndReloads_StayIdentical_EconomyStopped()
        {
            // ТЗ §13: равенство «после достижения ReadyToContinue»; §11.3: «Экономическое ядро останавливается».
            var fx = Real(1);
            long total = 3 * 3600 * S;
            var whole = fx.NewSim();
            whole.Advance(total);
            Assert.AreEqual(RunPhase.ReadyToContinue, whole.State.Run.Phase, "zone1 без покупок завершается за 3 ч");
            long done = whole.State.Run.Counters.CompletedTick;
            Assert.Greater(done, 0L);
            string expected = Json(whole.State);
            foreach (long cut in new[] { done - 1, done, done + 1, done + 7 * S })
            {
                var sim = fx.NewSim();
                sim.Advance(cut);
                sim = fx.Reload(sim);
                sim.Advance(cut + 3 * S);
                sim = fx.Reload(sim);
                sim.Advance(total);
                AssertSameJson(expected, Json(sim.State), "разрез и перезагрузка в " + cut);
            }
            var later = fx.Load(expected);
            Assert.AreEqual(long.MaxValue, later.NextEventTick(), "в ReadyToContinue событий ядра нет");
            later.Advance(total + 3600 * S);
            var a = whole.State.Run;
            var b = later.State.Run;
            Assert.AreEqual(a.Wallet, b.Wallet);
            Assert.AreEqual(a.TotalCredited, b.TotalCredited);
            Assert.AreEqual(total + 3600 * S, b.Now, "Advance только двигает Now");
            Assert.AreEqual(1, later.State.Profile.RewardLog.Count, "награда не повторяется");
            Assert.AreEqual(whole.State.Profile.RewardRng.State, later.State.Profile.RewardRng.State);
            Assert.AreEqual(whole.State.Profile.RewardRng.Inc, later.State.Profile.RewardRng.Inc);
        }

        [Test]
        public void TwoIndependentRuns_SameSeed_IdenticalJson()
        {
            // Нет скрытого глобального состояния и зависимости от порядка словарей (ТЗ §6.3 «Порядок строкового перечисления
            // словарей не определяет результат»).
            var fx = Real(5);
            var a = fx.NewSim();
            var b = fx.NewSim();
            a.Advance(900 * S);
            b.Advance(900 * S);
            AssertSameJson(Json(a.State), Json(b.State), "два независимых прогона");
        }

        [Test]
        public void RichMidRunState_JsonRoundTripExact_AndContinuationIdentical()
        {
            // ТЗ §17: сохранять пути, долю ребра/обслуживания, сроки, задачи и резервы; строки денег round-trip без потери единицы.
            // Правка теста: урон 100, сумка 2, 6 котиков, разгрузка 0.5/с — одновременно партии, резервы, очередь, разгрузка.
            var cfg = ConfigsWith(j => { j["BaseDamage"] = "100"; j["BaseCapacity"] = 2; j["StartWorkers"] = 6; j["BaseUnloadRate"] = "1/2"; });
            var fx = Real(5, cfg);
            var sim = fx.NewSim();
            bool rich = false;
            for (int i = 0; i < 100_000 && !rich; i++)
            {
                Assert.IsTrue(StepTick(sim));
                var r = sim.State.Run;
                // Интеграция: плюс хотя бы одно ребро/порция начато раньше текущей отметки — иначе дробного прогресса P0 не будет.
                rich = r.Batches.Any(bt => bt.Units.Any(u => u.ReservedBy != 0)) && r.Queue.Count > 0 &&
                       r.Workers.Any(w => w.Mode == WorkerMode.Unloading) && r.Workers.Any(IsMoving) &&
                       r.Workers.Any(w => w.Action != null && (w.Action.Kind == ActionKind.Edge || w.Action.Kind == ActionKind.Unload) && w.Action.SegmentStart < r.Now);
            }
            Assert.IsTrue(rich, "насыщенное состояние не найдено");
            var st = Clone(sim.State);
            GiveGold(st, BigInteger.Pow(10, 30));
            var full = fx.Wrap(st);
            Assert.IsTrue(full.Submit(Buy(UpgradeBranch.Boots)).Ok);
            Assert.IsTrue(full.Submit(Buy(UpgradeBranch.Unload)).Ok);
            Assert.IsTrue(full.State.Run.Workers.Any(w => w.Action != null && FracB.Parse(w.Action.P0) > Zero && FracB.Parse(w.Action.P0) < One),
                "есть точный дробный прогресс ребра или порции");
            Assert.Greater(full.State.Run.Wallet, BigInteger.Pow(2, 53), "кошелёк выше 2^53");
            string json = Json(full.State);
            AssertSameJson(json, Json(FromJson(json)), "двойная сериализация");
            var reloaded = fx.Load(json);
            full.Advance(full.Now + 60 * S);
            reloaded.Advance(reloaded.Now + 60 * S);
            AssertSameJson(Json(full.State), Json(reloaded.State), "продолжение после загрузки");
            AssertInvariants(reloaded, "после загрузки насыщенного состояния");
        }
    }
}
