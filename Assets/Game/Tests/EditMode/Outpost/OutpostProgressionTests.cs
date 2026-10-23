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
    /// Завершение шахты, награда и переход (ТЗ §10.2, §10.3, §11, §12.3, §12.5, §20 «Переход», «Повторное открытие итогов»,
    /// «Перезапуск во время перехода»). Полные прогоны реальных зон политикой A или C набора B.
    /// </summary>
    public class OutpostProgressionTests
    {
        static OutpostSimulation CompleteZone(int zone, Action<OutpostProfile> setup = null, PolicyCB policy = null)
        {
            var sim = Real(zone, null, setup).NewSim();
            RunToReadyOrFail(sim, "zone" + zone, policy);
            return sim;
        }

        [Test]
        public void Zone1Completion_NoItem_PartsGrantedOnce()
        {
            // Первая сессия (rewards-2, NoItem; прежде ТЗ §10.2 «Шахта 1: гарантированная обычная кирка»): награда шахты 1 — спасённая
            // Mia, за первое завершение только детали, предмета нет и поток 5 не продвигается; §12.3: CompletionParts(1) = 10;
            // §11.1: автоматического перехода нет.
            var sim = CompleteZone(1);
            var p = sim.State.Profile;
            var run = sim.State.Run;
            Assert.AreEqual(1, p.RewardLog.Count);
            var rec = p.RewardLog[0];
            Assert.AreEqual(RewardKey(1), rec.Key, "ключ (modeId, zoneId, firstCompletion)");
            Assert.AreEqual(1, rec.Zone);
            Assert.AreEqual(run.RunId, rec.RunId);
            Assert.IsNull(rec.ItemId, "предмета нет");
            Assert.IsFalse(rec.Guaranteed);
            Assert.IsFalse(rec.Duplicate);
            Assert.AreEqual(10, rec.CompletionParts);
            Assert.AreEqual(0, rec.DuplicateParts);
            CollectionAssert.IsEmpty(p.Items, "коллекция экипировки пуста");
            Assert.AreEqual(10L, p.Parts);
            CollectionAssert.AreEqual(new[] { 1 }, p.CompletedZones);
            Assert.AreEqual(RewardKey(1), run.CompletionRewardKey);
            var fresh = new Pcg32(Seed, 5);
            Assert.AreEqual(fresh.State, p.RewardRng.State, "шахта без предмета не продвигает поток 5");
            Assert.AreEqual(fresh.Inc, p.RewardRng.Inc);
            Assert.IsTrue(p.Equipped.All(e => e == null), "комплект пуст");
            Assert.IsFalse(p.CampaignComplete);
            Assert.AreEqual(1, p.CurrentZone, "следующая шахта сама не начинается");
            Assert.AreEqual(1, run.Zone);
        }

        [Test]
        public void ReadyToContinue_ReloadAdvanceAndRepeatedApply_DoNotRegrant()
        {
            // ТЗ §11.3: «Повторный тап, загрузка после выдачи и закрытие приложения… не создают дубликаты»; §10.2 «Перезапуск окна
            // или приложения не является новым броском».
            var sim = CompleteZone(1);
            var fx = Real(1);
            string json = Json(sim.State);
            var reloaded = fx.Load(json);
            reloaded.Advance(reloaded.Now + 3600 * S);
            reloaded = fx.Reload(reloaded);
            reloaded.Advance(reloaded.Now + 60 * S);
            var p = reloaded.State.Profile;
            Assert.AreEqual(1, p.RewardLog.Count);
            Assert.AreEqual(10L, p.Parts);
            // Шахта 1 без предмета (rewards-2): после перезагрузок предмет не появляется и поток 5 не бросается.
            CollectionAssert.IsEmpty(p.Items);
            Assert.AreEqual(new Pcg32(Seed, 5).State, p.RewardRng.State, "поток 5 не тронут");
            var st = FromJson(json);
            ApplyCompletion(st, Configs);
            AssertSameJson(json, Json(st), "повторный ApplyCompletion по выданному ключу ничего не меняет");
        }

        [Test]
        public void Transition_ResetsLocalProgress_KeepsEquipmentPartsAndRewards()
        {
            // ТЗ §11.2: золото/уровни/работники сбрасываются, экипировка, детали и награды сохраняются; §20 «Переход»;
            // §12.5: «Обычная кирка уровня 1 даёт +8%: стартовый урон следующей шахты будет 5.4 при нулевом локальном уровне».
            // Шахта 1 предмета не даёт (rewards-2) — обычная кирка уже в коллекции, как прочие предметы фикстуры.
            var policy = new PolicyCB();
            var sim = CompleteZone(1, pr => { Own(pr, "pickaxe.common", 1); Own(pr, "backpack.rare", 3); Own(pr, "hat.epic", 2); }, policy);
            Assert.Greater(policy.Purchases, 0, "политика C покупала");
            var r1 = sim.State.Run;
            Assert.IsTrue(r1.Levels.Any(l => l > 0));
            var eb = sim.Submit(EquipBestCmd());
            Assert.IsTrue(eb.Ok, eb.Reason);
            var p1 = sim.State.Profile;
            CollectionAssert.AreEqual(new[] { "pickaxe.common", "hat.epic", "backpack.rare", null }, p1.Equipped, "«Надеть лучшее» по слотам");

            string before = Json(sim.State);
            long nowUtc = StartUtcMs + 3_600_000;
            var next = PrepareTransition(sim.State, Configs, nowUtc);
            AssertSameJson(before, Json(sim.State), "подготовка перехода не меняет исходный снимок (§11.3)");
            var run = next.Run;
            var p = next.Profile;
            Assert.AreEqual(2, run.Zone);
            Assert.AreEqual("zone2", run.ZoneId);
            Assert.AreEqual(2, p.CurrentZone);
            Assert.AreEqual(RunPhase.Running, run.Phase);
            Assert.AreNotEqual(r1.RunId, run.RunId, "новый runId");
            Assert.AreEqual("run-2", run.RunId);
            CollectionAssert.AreEqual(new int[OutpostIds.BranchCount], run.Levels, "все семь веток (профиль b8) — 0");
            Assert.AreEqual(BigInteger.Zero, run.Wallet, "золото не переносится");
            Assert.AreEqual(BigInteger.Zero, run.TotalCredited);
            Assert.AreEqual(BigInteger.Zero, run.TotalSpent);
            Assert.AreEqual(2, run.Workers.Count, "два стартовых котика");
            CollectionAssert.AreEqual(new[] { 1, 2 }, run.Workers.Select(w => w.Id).ToArray());
            Assert.IsTrue(run.Workers.All(w => w.Cell == Map(2).SpawnCell && w.Bag.Count == 0 && w.Mode != WorkerMode.ChoosingTask),
                "§11.2: новая шахта сразу запускает двух котиков");
            Assert.AreEqual(0, run.Batches.Count);
            Assert.AreEqual(0, run.Queue.Count);
            Assert.AreEqual(0, run.Counters.DeliveredUnits);
            Assert.AreEqual(0L, run.Now);
            Assert.AreEqual(nowUtc, run.LastProcessedUtcMs);
            Assert.IsTrue(run.Targets.All(t => !t.Destroyed && t.ReleasedUnits == 0), "HP и ворота — по следующей карте");

            CollectionAssert.AreEquivalent(p1.Items, p.Items, "коллекция сохранена");
            CollectionAssert.AreEqual(p1.Equipped, p.Equipped, "надетый комплект сохранён");
            Assert.AreEqual(p1.Parts, p.Parts, "детали сохранены");
            Assert.AreEqual(p1.RewardLog.Count, p.RewardLog.Count, "награда не выдаётся повторно");
            CollectionAssert.AreEqual(p1.CompletedZones, p.CompletedZones);
            Assert.AreEqual(p1.RewardRng.State, p.RewardRng.State);
            Assert.AreEqual(p1.Seed, p.Seed);

            var sim2 = new OutpostSimulation(next, Map(2), Configs);
            Assert.AreEqual(5_400_000L, StatDamageUnits(sim2), "§12.5: 5·1.08 = 5.4 при локальном уровне 0");
            Assert.AreEqual(DamageUnitsOracle(5, 0, ItemBonus(0, 1)), StatDamageUnits(sim2));
            Assert.AreEqual(4 + RarityBaseCapacity[1] + (3 - 1), StatCapacity(sim2), "редкий рюкзак уровня 3: +3 + 2");
            Assert.AreEqual(Edge0, StatEdgeTicks(sim2), "обувь не надета, уровень 0");
            Assert.AreEqual(Unload0, StatUnloadTicks(sim2));
            sim2.Advance(60 * S);
            foreach (var u in AllUnits(sim2.State.Run))
                Assert.AreEqual(CoinOracle(2, FracB.Parse(T(sim2, u.Source).ValueFactor), 0, ItemBonus(2, 2)), u.Value,
                    "шляпа эпическая ур. 2 влияет на порции zone2");
            AssertInvariants(sim2, "после перехода");
        }

        [Test]
        public void RepeatedTransitionFromSameReadyState_SameSnapshot_NoExtraReward()
        {
            // ТЗ §11.3: «При аварии до commit загружается прежнее ReadyToContinue, после commit — следующая Running… награда не
            // бросается заново»; §20 «Перезапуск во время перехода».
            var sim = CompleteZone(1);
            string ready = Json(sim.State);
            var a = PrepareTransition(FromJson(ready), Configs, StartUtcMs + 1000);
            var b = PrepareTransition(FromJson(ready), Configs, StartUtcMs + 1000);
            AssertSameJson(Json(a), Json(b), "повтор из того же ReadyToContinue — тот же снимок");
            Assert.AreEqual(sim.State.Profile.Parts, a.Profile.Parts);
            Assert.AreEqual(1, a.Profile.RewardLog.Count);
            var loaded = new OutpostSimulation(FromJson(Json(a)), Map(2), Configs);
            loaded.Advance(30 * S);
            Assert.AreEqual(1, loaded.State.Profile.RewardLog.Count, "загрузка после commit не выдаёт награду zone1 снова");
            Assert.AreEqual(sim.State.Profile.Parts, loaded.State.Profile.Parts);
        }

        [Test]
        public void Transition_RejectedBeforeReadyToContinue()
        {
            // ТЗ §11.3: фазы Running → DrainingCargo → ReadyToContinue → Transitioning; переход — только по кнопке после выдачи.
            var sim = Real(1).NewSim();
            sim.Advance(30 * S);
            string json = Json(sim.State);
            Assert.IsNull(TryPrepareTransition(sim.State, Configs, StartUtcMs), "переход до ReadyToContinue запрещён");
            AssertSameJson(json, Json(sim.State), "отказ не меняет состояние");
        }

        [Test]
        public void DuplicateReward_ConvertedToParts_ItemLevelUnchanged()
        {
            // ТЗ §10.3: «Повторное получение того же предмета превращается в постоянные Детали: 5 / 15 / 40 / 120»;
            // §12.3: детали завершения выдаются вместе с предметом независимо от того, новая это копия или дубликат.
            // Первый предмет-награда кампании (rewards-2) — гарантированная обычная шляпа шахты 3.
            var sim = CompleteZone(3, pr => Own(pr, "hat.common", 3));
            var prof = sim.State.Profile;
            Assert.AreEqual(3, prof.Items["hat.common"], "уровень имеющегося предмета не меняется");
            Assert.AreEqual(1, prof.Items.Count, "второй записи нет");
            Assert.AreEqual(10L + 5L, prof.Parts);
            var rec = prof.RewardLog.Single();
            Assert.AreEqual("hat.common", rec.ItemId);
            Assert.IsTrue(rec.Duplicate);
            Assert.AreEqual(5, rec.DuplicateParts);
            Assert.AreEqual(10, rec.CompletionParts);
        }

        [Test]
        public void Zone6Reward_RandomFromStream5_SlotThenRarity_StoredOnce()
        {
            // ТЗ §10.2: «Сначала NextInt(4) выбирает слот, затем NextInt(100) выбирает редкость: 0–69 / 70–93 / 94–98 / 99»;
            // поток 5; результат определяется и сохраняется один раз. Первая сессия (rewards-2): шахта 5 — гарантия обычной обуви,
            // первая случайная награда — шахта 6; §12.3: CompletionParts(6) = 15.
            var fx = Real(6);
            var sim = fx.NewSim();
            RunToReadyOrFail(sim, "zone6", new PolicyCB());
            var oracle = new Pcg32(Seed, 5);
            string item = ExpectedRandomItem(oracle);
            var p = sim.State.Profile;
            var rec = p.RewardLog.Single();
            Assert.AreEqual(RewardKey(6), rec.Key);
            Assert.AreEqual(item, rec.ItemId);
            Assert.IsFalse(rec.Guaranteed);
            Assert.AreEqual(15, rec.CompletionParts);
            Assert.AreEqual(15L, p.Parts);
            Assert.AreEqual(1, p.Items[item]);
            Assert.AreEqual(oracle.State, p.RewardRng.State, "два шага потока 5");
            Assert.AreEqual(oracle.Inc, p.RewardRng.Inc);
            var again = fx.Reload(sim);
            again.Advance(again.Now + 600 * S);
            Assert.AreEqual(oracle.State, again.State.Profile.RewardRng.State, "перезагрузка не бросает заново");
            Assert.AreEqual(1, again.State.Profile.RewardLog.Count);
        }

        [Test]
        public void SequentialZones1To5_NoItemThenCommonItems_ResetAndEquipmentCarryOver()
        {
            // ТЗ §10.2 и первая сессия (rewards-2): шахты 1–2 — только детали, 3–5 — обычные шляпа, рюкзак, обувь; §11.2 reset;
            // §15 п.9 «На следующей шахте показать сохранённый бонус предмета при локальном уровне 0». Последовательная кампания, не
            // изолированная карта (§20). Профиль фикстуры создан до прогрессии (Version 0): стартовой кирки шахты 3 нет — урон прежний.
            var state = Real(1).NewState();
            var runIds = new HashSet<string>();
            long parts = 0;
            for (int z = 1; z <= 5; z++)
            {
                var run = state.Run;
                Assert.AreEqual(z, run.Zone);
                Assert.IsTrue(runIds.Add(run.RunId), "новый runId");
                Assert.AreEqual(2, run.Workers.Count);
                Assert.IsTrue(run.Levels.All(l => l == 0));
                Assert.AreEqual(BigInteger.Zero, run.Wallet);
                var sim = new OutpostSimulation(state, Map(z), Configs);
                Assert.AreEqual(Damage0, StatDamageUnits(sim), "zone" + z + ": кирки среди наград шахт 1–5 нет");
                Assert.AreEqual(CoinOracle(z, One, 0, z >= 4 ? ItemBonus(0, 1) : Zero), sim.Stats.CoinValue(Rational.One),
                    "zone" + z + ": шляпа из zone3");
                Assert.AreEqual(z >= 5 ? 5 : 4, StatCapacity(sim), "zone" + z + ": рюкзак из zone4");
                RunToReadyOrFail(sim, "zone" + z, new PolicyCB());
                parts += CompletionParts(z);
                var p = sim.State.Profile;
                Assert.AreEqual(z, p.RewardLog.Count);
                Assert.AreEqual(GuaranteedItem(z), p.RewardLog[z - 1].ItemId, "zone" + z + ": награда (шахты 1–2 — без предмета)");
                Assert.AreEqual(parts, p.Parts);
                Assert.IsTrue(sim.Submit(EquipBestCmd()).Ok);
                p = sim.State.Profile;
                if (NoItemZone(z)) Assert.IsTrue(p.Equipped.All(e => e == null), "zone" + z + ": надеть нечего");
                else Assert.AreEqual(GuaranteedItem(z), p.Equipped[SlotOf(GuaranteedItem(z))], "«Надеть лучшее»: новый обычный предмет в пустой слот");
                if (z < 5) state = PrepareTransition(FromJson(Json(sim.State)), Configs, StartUtcMs + z * HourMs);
                else CollectionAssert.AreEqual(new[] { null, "hat.common", "backpack.common", "boots.common" }, p.Equipped);
            }
            Assert.AreEqual(55L, parts, "по 10 деталей за шахты 1–4 и 15 за шахту 5");
        }
    }
}
