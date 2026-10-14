using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CatMiners.Core;
using CatMiners.Core.Outpost;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Медленные проверки всех 144 шахт (ТЗ §20). Запуск отдельно: --filter "TestCategory=OutpostSlow".
    /// 1) Изолированная карта: каждая зона, политика A, без экипировки — ReadyToContinue в пределах 24 ч и 2 000 000 событий.
    /// 2) Последовательная кампания 1..144, политика C: награды 1–4/10/25/50, дубликаты → детали, «Надеть лучшее», reset.
    /// Ожидаемые суммы — из Docs/TZ/analysis/outpost_v2_audit.json (base_gold, cargo_units, obligatory_targets, hp, tokens).
    /// </summary>
    [Category("OutpostSlow")]
    public class OutpostCampaignSlowTests
    {
        static IEnumerable<int> AllZones() => Enumerable.Range(1, 144);

        [TestCaseSource(nameof(AllZones))]
        public void EveryZone_PolicyA_NoEquipment_CompletesWithinLimits(int zone)
        {
            // ТЗ §20: «запусти ядро работников на каждой карте с двумя бесплатными котиками, без экипировки, покупок, выдач и мгновенного
            // разрушения… Лимит каждой карты — 24 часа симуляционного времени и 2 000 000 обработанных событий. Успех — достигнуто
            // ReadyToContinue, весь груз сдан, все обязательные цели уничтожены, ворота открыты, резервы/очередь пусты, награда
            // выдана один раз. Timeout или watchdog — провал с диагностикой».
            var fx = Real(zone);
            var sim = fx.NewSim();
            var audit = AuditZone(zone);
            Assert.AreEqual((long)audit["hp"], sim.State.Run.Targets.Sum(t => t.MaxHpUnits) / S, "Σ HP = аудит");
            long ticks = 0;
            var report = RunToReady(sim, null, DayTicks, EventLimit, (s, ev) =>
            {
                if (++ticks % 5000 == 0) AssertInvariants(s, "zone" + zone);
            });
            var run = sim.State.Run;
            // Диагностика темпа (ТЗ §12.6): время симуляции до ReadyToContinue, отметки и события.
            TestContext.WriteLine("zone" + zone + " A sim=" + (run.Counters.CompletedTick / (double)S).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                + "s ticks=" + report.Ticks + " events=" + report.Events);
            Assert.IsFalse(report.Watchdog, "zone" + zone + ": watchdog: " + run.WatchdogDiagnostic);
            Assert.IsTrue(report.Ready, "zone" + zone + ": timeout, t=" + run.Now + ", отметок " + report.Ticks + ", фаза " + run.Phase);
            Assert.LessOrEqual(report.Events, (long)EventLimit, "лимит событий");
            Assert.LessOrEqual(run.Counters.CompletedTick, DayTicks, "лимит 24 ч");
            Assert.AreEqual(AuditBig(audit["base_gold"]), run.TotalCredited, "без покупок и экипировки сдано Σ порций");
            Assert.AreEqual((int)audit["cargo_units"], run.Counters.DeliveredUnits, "весь груз сдан");
            Assert.AreEqual((int)audit["obligatory_targets"], run.Counters.ClearedTargets, "все обязательные цели");
            Assert.AreEqual(AuditTokens(zone, "p"), run.GatesOpened, "все ворота открыты");
            Assert.IsTrue(run.Targets.All(t => t.Destroyed && t.ReleasedUnits == t.TotalUnits));
            Assert.AreEqual(BigInteger.Zero, run.TotalSpent);
            Assert.AreEqual(0, run.Batches.Count);
            Assert.AreEqual(0, run.Queue.Count);
            AssertInvariants(sim, "zone" + zone + " финал");

            sim.Advance(sim.Now + 3600 * S);
            var p = sim.State.Profile;
            Assert.AreEqual(1, p.RewardLog.Count, "награда один раз");
            Assert.AreEqual(RewardKey(zone), p.RewardLog[0].Key);
            Assert.AreEqual(CompletionParts(zone), p.RewardLog[0].CompletionParts);
            Assert.AreEqual(AuditBig(audit["base_gold"]), sim.State.Run.Wallet, "после завершения доход остановлен");
            if (GuaranteedItem(zone) != null) Assert.AreEqual(GuaranteedItem(zone), p.RewardLog[0].ItemId);
            else Assert.AreEqual(ExpectedRandomItem(new Pcg32(Seed, 5)), p.RewardLog[0].ItemId, "свежий профиль: первый бросок потока 5");
        }

        // b4: кампания бота C длиннее в логическом времени в ~4 раза; лимит Unity по умолчанию (3 мин) мал для Mono.
        [Test, Timeout(1200000)]
        public void SequentialCampaign_PolicyC_RewardsDuplicatesEquipBest_ResetBetweenZones()
        {
            // ТЗ §20: «Отдельный последовательный прогон кампании проверяет награды 1–4/10/25/50, конвертацию дубликатов, экипировку
            // и reset между всеми 144 шахтами»; §10.2, §10.3, §11.2, §11.3 (после zone144 — CampaignComplete, zone145 нет).
            var state = Real(1).NewState();
            var oracle = new Pcg32(Seed, 5);
            var owned = new Dictionary<string, int>();
            long parts = 0;
            var runIds = new HashSet<string>();
            var simSeconds = new List<(int zone, double sec)>();
            for (int z = 1; z <= 144; z++)
            {
                var run = state.Run;
                Assert.AreEqual(z, run.Zone);
                Assert.AreEqual(z, state.Profile.CurrentZone);
                Assert.IsTrue(runIds.Add(run.RunId), "zone" + z + ": новый runId");
                Assert.AreEqual(2, run.Workers.Count, "zone" + z + ": reset — два котика");
                Assert.IsTrue(run.Levels.All(l => l == 0), "zone" + z + ": уровни 0");
                Assert.AreEqual(BigInteger.Zero, run.Wallet);
                Assert.AreEqual(RunPhase.Running, run.Phase);
                var sim = new OutpostSimulation(state, Map(z), Configs);
                AssertEquippedStats(sim, state.Profile, z);

                RunToReadyOrFail(sim, "кампания zone" + z, new PolicyCB());
                simSeconds.Add((z, sim.State.Run.Counters.CompletedTick / (double)S));

                string expected = GuaranteedItem(z) ?? ExpectedRandomItem(oracle);
                bool duplicate = owned.ContainsKey(expected);
                int dupParts = duplicate ? DuplicateParts[RarityOf(expected)] : 0;
                parts += CompletionParts(z) + dupParts;
                if (!duplicate) owned[expected] = 1;
                var p = sim.State.Profile;
                Assert.AreEqual(z, p.RewardLog.Count, "одна запись на шахту");
                var rec = p.RewardLog[z - 1];
                Assert.AreEqual(RewardKey(z), rec.Key);
                Assert.AreEqual(expected, rec.ItemId, "zone" + z + ": предмет награды");
                Assert.AreEqual(GuaranteedItem(z) != null, rec.Guaranteed);
                Assert.AreEqual(duplicate, rec.Duplicate);
                Assert.AreEqual(dupParts, rec.DuplicateParts);
                Assert.AreEqual(CompletionParts(z), rec.CompletionParts);
                Assert.AreEqual(parts, p.Parts, "детали: завершения + дубликаты");
                CollectionAssert.AreEquivalent(owned, p.Items, "коллекция: одна запись на предмет, уровень 1");
                Assert.AreEqual(oracle.State, p.RewardRng.State, "поток 5 продвигается только случайными наградами");
                Assert.AreEqual(oracle.Inc, p.RewardRng.Inc);
                Assert.AreEqual(z == 144, p.CampaignComplete);

                Assert.IsTrue(sim.Submit(EquipBestCmd()).Ok, "экипировка разрешена в ReadyToContinue");
                p = sim.State.Profile;
                for (int slot = 0; slot < 4; slot++)
                {
                    // Все предметы уровня 1: наибольший бонус = наибольшая редкость слота.
                    var best = owned.Keys.Where(id => SlotOf(id) == slot).OrderByDescending(RarityOf).FirstOrDefault();
                    Assert.AreEqual(best, p.Equipped[slot], "zone" + z + ": «Надеть лучшее», слот " + SlotNames[slot]);
                }

                if (z < 144)
                {
                    state = PrepareTransition(FromJson(Json(sim.State)), Configs, StartUtcMs + z * HourMs);
                }
                else
                {
                    Assert.IsNull(TryPrepareTransition(FromJson(Json(sim.State)), Configs, StartUtcMs + z * HourMs), "zone145 не создаётся");
                    Assert.AreEqual(144, p.CurrentZone);
                    CollectionAssert.AreEqual(Enumerable.Range(1, 144).ToArray(), p.CompletedZones);
                    var again = new OutpostSimulation(FromJson(Json(sim.State)), Map(144), Configs);
                    again.Advance(again.Now + 3600 * S);
                    Assert.AreEqual(144, again.State.Profile.RewardLog.Count, "итоговая награда не выдаётся повторно");
                    Assert.AreEqual(parts, again.State.Profile.Parts);
                }
            }
            var bySec = simSeconds.OrderBy(x => x.sec).ToList();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            TestContext.WriteLine("campaign C: min zone" + bySec[0].zone + " " + bySec[0].sec.ToString("0.0", inv) + "s; median "
                + bySec[72].sec.ToString("0.0", inv) + "s; max zone" + bySec[143].zone + " " + bySec[143].sec.ToString("0.0", inv)
                + "s; total " + (simSeconds.Sum(x => x.sec) / 3600).ToString("0.00", inv) + "h");
            Assert.AreEqual((long)Audit["controls"]["guaranteed_completion_parts_total_campaign"],
                Enumerable.Range(1, 144).Sum(CompletionParts), "контроль аудита: Σ CompletionParts = 11590");
        }

        /// <summary>Стартовые характеристики шахты = база при уровне 0 + бонусы надетых предметов (ТЗ §11.2, §12.3).</summary>
        static void AssertEquippedStats(OutpostSimulation sim, OutpostProfile p, int zone)
        {
            FracB Bonus(int slot)
            {
                string id = p.Equipped[slot];
                return id == null ? Zero : ItemBonus(RarityOf(id), p.Items[id]);
            }
            string pack = p.Equipped[2];
            int extra = pack == null ? 0 : RarityBaseCapacity[RarityOf(pack)] + p.Items[pack] - 1;
            string ctx = "zone" + zone + ": ";
            Assert.AreEqual(DamageUnitsOracle(5, 0, Bonus(0)), StatDamageUnits(sim), ctx + "урон с киркой");
            Assert.AreEqual(EdgeTicksOracle(new FracB(5, 2), 0, Bonus(3)), StatEdgeTicks(sim), ctx + "скорость с обувью");
            Assert.AreEqual(4 + extra, StatCapacity(sim), ctx + "сумка с рюкзаком");
            Assert.AreEqual(Unload0, StatUnloadTicks(sim), ctx + "разгрузка");
            Assert.AreEqual(2, StatWorkers(sim));
        }
    }
}
