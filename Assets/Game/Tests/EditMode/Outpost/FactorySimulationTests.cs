using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Outpost;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// ТЗ 2.2 (заводы вместо месторождений), пакет WP1 «Ядро», проверки §16.1: открытие завода (сундук → FactoryUnlocked →
    /// автодоставка), цикл и ручной буфер (E-18), автодоставка при открытии, после улучшения и от первой копии из сундука (шахта 3),
    /// выпуск обеих карточек типа (каждое улучшение +1), шаг 4б и NextEventTick (§4.5), watchdog при работающих заводах, остановка в
    /// ReadyToContinue (§4.6), трубы и склад, рынок и учёт золота (§6.4), онлайн = офлайн = тайм-скип, save/load посреди цикла и
    /// трубы, сброс при переходе, стоящий завод в навигации (§4.1), цепи без груза (§4.2), инварианты (§10.2), DEV-выдачи (§13).
    /// Ожидаемые значения — из ТЗ 2.2 (§4, §5, §6.4, приложение B) и исходного JSON, а не из кода ядра.
    /// </summary>
    public class FactorySimulationTests
    {
        // ───────────────────────────── Синтетические шахты ─────────────────────────────

        /// <summary>Токены s: тип, ранг цепей, сундук (запись Gacha), карточка и уровень требования автодоставки.</summary>
        const string DairyNeeds1 = "s:amethyst:1:scg_m002_s01:ca002:1";
        const string DairyNeeds2 = "s:amethyst:1:scg_m002_s01:ca002:2";
        const string SawmillNeeds1 = "s:citrine:1:scg_m002_s01:ca003:1";

        /// <summary>Циклы JSON (приложение B п. 4): amethyst — 8 с, citrine — 15 с.</summary>
        const long Dairy = 8 * S, Sawmill = 15 * S;

        /// <summary>Камень (0,0) и заводы с якорями (1,3) и (4,3): footprint 2×3 — столбцы c…c+1, строки r−2…r.</summary>
        const int RockId = 0, FactoryA = 22, FactoryB = 25;
        /// <summary>Заводы, замыкающие камень (0,0): S1 — якорь (0,3), S2 — якорь (2,2).</summary>
        const int EnclosingS1 = 21, EnclosingS2 = 16;

        static OutpostFixtureB OneFactory(string token, OutpostConfigSet configs = null, Action<OutpostProfile> setup = null) =>
            Synthetic(configs, setup,
                Rock1 + " . . . . . " + Exit,
                ". . . . . . .",
                ". . . . . . .",
                ". " + token + " . . . . .",
                Camp + " . . . . . .");

        static OutpostFixtureB TwoFactories(string a, string b, OutpostConfigSet configs = null, Action<OutpostProfile> setup = null) =>
            Synthetic(configs, setup,
                Rock1 + " . . . . . " + Exit,
                ". . . . . . .",
                ". . . . . . .",
                ". " + a + " . . " + b + " . .",
                Camp + " . . . . . .");

        /// <summary>Камень (0,0): его рабочие клетки (1,0) и (0,1) после открытия заводов S1 и S2 становятся недостижимыми.</summary>
        static OutpostFixtureB EnclosedRock(string s1, string s2, OutpostConfigSet configs = null, Action<OutpostProfile> setup = null) =>
            Synthetic(configs, setup,
                Rock1 + " . . . . . " + Exit,
                ". . . . . . .",
                ". . " + s2 + " . . . .",
                s1 + " . . . . . .",
                Camp + " . . . . . .");

        // ───────────────────────────── Конфигурации с карточками cards-3 ─────────────────────────────

        static OutpostConfigSet s_CardConfigs;

        /// <summary>
        /// Набор с каталогом карточек: manager-1/manager-2 — FactoryOutput (Base 1/10, PerLevel 1/20, ТЗ 2.2 §7.1) независимо от того,
        /// пересобрана ли уже встроенная копия card_rules (WP3); прежняя геометрия (без топологии), как в синтетических шахтах.
        /// </summary>
        static OutpostConfigSet CardConfigs => s_CardConfigs ?? (s_CardConfigs = OutpostConfigSet.Parse(OutpostConfigSet.DefaultBalanceJson,
            OutpostConfigSet.DefaultEquipmentJson, OutpostConfigSet.DefaultRewardsJson, OutpostConfigSet.DefaultOverridesJson, FactoryCardRulesJson(),
            OutpostConfigSet.DefaultTopologyJson, OutpostConfigSet.DefaultStoreJson, OutpostConfigSet.DefaultFactoryRulesJson)
            .WithCards(TestData.Balance).WithTopologies(null));

        static string FactoryCardRulesJson()
        {
            var j = JObject.Parse(OutpostConfigSet.DefaultCardRulesJson);
            foreach (string key in new[] { "manager-1", "manager-2" })
            {
                var e = (JObject)j["Effects"][key];
                e["Kind"] = "FactoryOutput";
                e["Base"] = "1/10";
                e["PerLevel"] = "1/20";
                e["Rule"] = "CatMiners";
                e.Remove("TargetKind");
            }
            return j.ToString(Formatting.None);
        }

        // ───────────────────────────── Помощники ─────────────────────────────

        static TargetState Target(OutpostGameState st, int id) => st.Run.Targets.First(t => t.Id == id);

        /// <summary>Цепи без груза (adapter-2, ТЗ 2.2 §4.2) и с одной долей HP — один удар киркой открывает завод на нужной отметке.</summary>
        static void ArmChains(OutpostGameState st, int id)
        {
            var t = Target(st, id);
            t.TotalUnits = 0;
            t.RemainingHpUnits = 1;
        }

        /// <summary>Камень, который котики не разобьют за время теста: шахта не завершается, котики всё время заняты.</summary>
        static void Endless(OutpostGameState st, int id)
        {
            var t = Target(st, id);
            t.MaxHpUnits = 1_000_000_000_000_000L;
            t.RemainingHpUnits = t.MaxHpUnits;
        }

        /// <summary>Камень с hp единицами HP (полный): котики разбивают его дольше, груз выпускается по тем же порогам.</summary>
        static void Tough(OutpostGameState st, int id, long hp)
        {
            var t = Target(st, id);
            t.MaxHpUnits = hp * OutpostIds.HpUnitsPerHp;
            t.RemainingHpUnits = t.MaxHpUnits;
        }

        static void Card(OutpostProfile p, string id, int level, long copies = 0) => p.Cards[id] = new CardProgress { Level = level, Copies = copies };

        static void Unlock(OutpostSimulation sim, int id)
        {
            var r = sim.Submit(OutpostCommand.Strike(id));
            Assert.IsTrue(r.Ok, "удар по цепям: " + r);
            Assert.IsTrue(sim.TargetById(id).Destroyed, "цепи разбиты");
        }

        /// <summary>Товар на складе так, будто он пришёл по трубе (Produced = Dispatched, GoodsArrived = Warehouse) — учёт сохраняется.</summary>
        static void GiveGoods(OutpostGameState st, int factoryId, long units)
        {
            var f = st.Run.Factories.First(x => x.TargetId == factoryId);
            f.Produced += units;
            f.Dispatched += units;
            st.Run.GoodsArrived[f.GoodId] = st.Run.GoodsArrived.TryGetValue(f.GoodId, out long a) ? a + units : units;
            st.Run.Warehouse[f.GoodId] = st.Run.Warehouse.TryGetValue(f.GoodId, out long w) ? w + units : units;
        }

        static void AssertCore(OutpostSimulation sim, string context, bool debugProfile = false)
        {
            var errors = OutpostInvariants.Check(sim.State, sim.Map, sim.Configs.Equipment, sim.Configs.Cards, debugProfile);
            if (errors.Count > 0) Assert.Fail(context + " t=" + sim.Now + ":\n" + string.Join("\n", errors.Take(20)));
        }

        static bool IsFactoryEvent(OutpostEventType type) => type >= OutpostEventType.FactoryUnlocked;

        static List<OutpostEvent> FactoryEvents(IEnumerable<OutpostEvent> events) => events.Where(e => IsFactoryEvent(e.Type)).ToList();

        static string Describe(IEnumerable<OutpostEvent> events) =>
            string.Join(", ", events.Select(e => e.Type + "@" + e.Tick + " t" + e.TargetId + " " + e.ItemId + " n" + e.Count + " b" + e.BatchSeq));

        /// <summary>По отметкам событий до ReadyToContinue (или watchdog) с проверкой инвариантов; события копятся в sink.</summary>
        static void RunToReady(OutpostSimulation sim, List<OutpostEvent> sink = null, long limit = DayTicks)
        {
            int guard = 0;
            while (sim.Run.Phase != RunPhase.ReadyToContinue && !sim.Run.WatchdogFired)
            {
                long t = sim.NextEventTick();
                Assert.AreNotEqual(long.MaxValue, t, "нет событий до завершения");
                Assert.LessOrEqual(t, limit, "шахта не завершилась за лимит");
                sim.Advance(t);
                if (sink != null) sim.DrainEvents(sink);
                if (++guard % 64 == 0) AssertCore(sim, "прогон");
                Assert.Less(guard, 3_000_000);
            }
            Assert.IsFalse(sim.Run.WatchdogFired, sim.Run.WatchdogDiagnostic);
            AssertCore(sim, "ReadyToContinue");
        }

        static string FactoryJson(OutpostRun run) =>
            OutpostJson.Serialize(new object[] { run.Factories, run.PipeBatches, run.NextPipeBatchSeq, run.Warehouse, run.GoodsArrived, run.GoodsSold,
                run.GoodsSoldGold, run.DebugGoods, run.Wallet, run.Counters.ManualDeliveries });

        // ───────────────────────────── Создание шахты ─────────────────────────────

        [Test]
        public void CreateRun_FactoriesLockedFromTokens_RulesSnapshot_EmptyPipesAndWarehouse()
        {
            var fx = TwoFactories(DairyNeeds1, SawmillNeeds1);
            var run = fx.NewState().Run;
            Assert.IsNotNull(run.FactoryRules, "§3.2: снимок factory_rules при создании шахты");
            Assert.AreEqual(Configs.FactoryRules.CanonicalJson(), run.FactoryRules.CanonicalJson());
            CollectionAssert.AreEqual(new[] { FactoryA, FactoryB }, run.Factories.Select(f => f.TargetId).ToArray(), "по TargetId");
            Assert.AreEqual(("amethyst", "milk"), (run.Factories[0].TypeId, run.Factories[0].GoodId));
            Assert.AreEqual(("citrine", "wood"), (run.Factories[1].TypeId, run.Factories[1].GoodId));
            foreach (var f in run.Factories)
            {
                Assert.AreEqual(FactoryStatus.Locked, f.Status);
                Assert.IsFalse(f.Automated);
                Assert.AreEqual((-1L, -1L, 0, 0L, 0L, 0), (f.CycleStart, f.CycleEnd, f.ReadyUnits, f.Produced, f.Dispatched, f.Cycles));
            }
            Assert.AreEqual(0, run.PipeBatches.Count);
            Assert.AreEqual(1L, run.NextPipeBatchSeq);
            Assert.IsTrue(run.Warehouse.Values.All(v => v == 0) && run.GoodsArrived.Values.All(v => v == 0) && run.GoodsSold.Values.All(v => v == 0));
            Assert.AreEqual(0, run.DebugGoods.Count);
            Assert.AreEqual(BigInteger.Zero, run.GoodsSoldGold);

            var zone1 = StageProgressionService.NewGame(TestData.Balance, Configs, Seed, StartUtcMs, 1).Run;
            Assert.AreEqual(0, zone1.Factories.Count, "в шахте 1 заводов нет");
            Assert.IsNotNull(zone1.FactoryRules);
            Assert.AreEqual("no-factories", FactoryQueries.SellBlockReason(zone1, null));
        }

        // ───────────────────────────── Цикл и ручной буфер ─────────────────────────────

        [Test]
        public void Cycle_ManualBuffer_ReadyWaitsForDeliver_PipeToWarehouse()
        {
            var fx = OneFactory(DairyNeeds1);
            var state = fx.NewState();
            Endless(state, RockId);
            ArmChains(state, FactoryA);
            var sim = fx.Wrap(state);
            // Отметка 1 — раньше первого удара котиков: цепи разбивает кирка игрока, первый цикл — с этой отметки.
            const long t0 = 1;
            sim.Advance(t0);
            Drain(sim);
            Unlock(sim, FactoryA);
            var ev = Drain(sim);
            int destroyed = ev.FindIndex(e => e.Type == OutpostEventType.TargetDestroyed && e.TargetId == FactoryA);
            int unlocked = ev.FindIndex(e => e.Type == OutpostEventType.FactoryUnlocked);
            Assert.That(destroyed, Is.GreaterThanOrEqualTo(0));
            Assert.Greater(unlocked, destroyed, "FactoryUnlocked после разрушения цепей");
            var u = ev[unlocked];
            Assert.AreEqual((FactoryA, "milk", "amethyst", Cell(sim.Map, 1, 3), t0), (u.TargetId, u.ItemId, u.Detail, u.Cell, u.Tick));
            Assert.IsFalse(ev.Any(e => e.Type == OutpostEventType.FactoryAutomated), "карточки ca002 нет — ручной режим");

            var f = sim.FactoryById(FactoryA);
            Assert.AreEqual((FactoryStatus.Working, t0, t0 + Dairy), (f.Status, f.CycleStart, f.CycleEnd), "первый цикл — с отметки разбитых цепей");
            Assert.AreEqual(20, sim.FactoryUnitsPerCycle(FactoryA), "UnitsPerCycle 20 без карточек");
            Assert.AreEqual("not-ready", sim.Submit(OutpostCommand.DeliverFactory(FactoryA)).Reason);
            AssertCore(sim, "после открытия");

            sim.Advance(t0 + Dairy - 1);
            Assert.AreEqual(FactoryStatus.Working, f.Status);
            sim.Advance(t0 + Dairy);
            Assert.AreEqual((FactoryStatus.Ready, 20, 20L, 0L, 1), (f.Status, f.ReadyUnits, f.Produced, f.Dispatched, f.Cycles));
            var ready = FactoryEvents(Drain(sim));
            Assert.AreEqual(1, ready.Count, Describe(ready));
            Assert.AreEqual((OutpostEventType.FactoryReady, t0 + Dairy, FactoryA, "milk", 20), (ready[0].Type, ready[0].Tick, ready[0].TargetId, ready[0].ItemId, ready[0].Count));

            // Ручной буфер (E-18): пока партия ждёт, завод стоит — новых циклов и партий нет.
            sim.Advance(t0 + 10 * Dairy);
            Assert.AreEqual((FactoryStatus.Ready, 20, 1), (f.Status, f.ReadyUnits, f.Cycles));
            Assert.AreEqual(0, sim.Run.PipeBatches.Count);
            Assert.AreEqual(0, FactoryEvents(Drain(sim)).Count);
            AssertCore(sim, "Ready ждёт");

            long td = sim.Now;
            var r = sim.Submit(OutpostCommand.DeliverFactory(FactoryA));
            Assert.IsTrue(r.Ok, r.ToString());
            Assert.AreEqual(20, r.Levels, "Levels — единиц товара");
            Assert.AreEqual((FactoryStatus.Working, td, td + Dairy, 0, 20L), (f.Status, f.CycleStart, f.CycleEnd, f.ReadyUnits, f.Dispatched),
                "следующий цикл — после нажатия");
            Assert.AreEqual(1, sim.Run.Counters.ManualDeliveries);
            long transit = sim.FactoryTransitTicks(FactoryA);
            Assert.AreEqual(FactoryMath.TransitTicks(sim.Run.FactoryRules, sim.Map.Factory(FactoryA).RouteSteps), transit);
            Assert.That(transit, Is.GreaterThanOrEqualTo(S), "не меньше MinTransitSec");
            var b = sim.Run.PipeBatches.Single();
            Assert.AreEqual((1L, FactoryA, "milk", 20, td, td + transit), (b.Seq, b.FactoryId, b.GoodId, b.Units, b.DepartTick, b.ArriveTick));
            var sent = FactoryEvents(Drain(sim)).Single();
            Assert.AreEqual((OutpostEventType.GoodsDispatched, 1L, 0, 20), (sent.Type, sent.BatchSeq, sent.Extra, sent.Count), "Extra 0 — команда");
            Assert.AreEqual("not-ready", sim.Submit(OutpostCommand.DeliverFactory(FactoryA)).Reason, "повторная доставка без партии");
            AssertCore(sim, "партия в трубе");

            sim.Advance(td + transit - 1);
            Assert.AreEqual(0L, sim.GoodsCount("milk"), "товар в пути, а не на складе");
            sim.Advance(td + transit);
            Assert.AreEqual(20L, sim.GoodsCount("milk"));
            Assert.AreEqual(20L, sim.Run.GoodsArrived["milk"]);
            Assert.AreEqual(0, sim.Run.PipeBatches.Count, "партия удаляется при прибытии");
            var arrived = FactoryEvents(Drain(sim)).Single();
            Assert.AreEqual((OutpostEventType.GoodsArrived, td + transit, FactoryA, "milk", 20, 1L),
                (arrived.Type, arrived.Tick, arrived.TargetId, arrived.ItemId, arrived.Count, arrived.BatchSeq));
            AssertCore(sim, "на складе");
        }

        // ───────────────────────────── Автодоставка ─────────────────────────────

        [Test]
        public void Auto_OnUnlock_CardMeetsToken_BatchesFlowWithoutDrift()
        {
            var fx = OneFactory(DairyNeeds1, CardConfigs, p => Card(p, "ca002", 1));
            var state = fx.NewState();
            Endless(state, RockId);
            ArmChains(state, FactoryA);
            var sim = fx.Wrap(state);
            const long t0 = 1_000_003;
            sim.Advance(t0);
            Drain(sim);
            Unlock(sim, FactoryA);
            var ev = Drain(sim);
            int chest = ev.FindIndex(e => e.Type == OutpostEventType.ChestOpened && e.ItemId == "scg_m002_s01");
            int unlocked = ev.FindIndex(e => e.Type == OutpostEventType.FactoryUnlocked);
            int auto = ev.FindIndex(e => e.Type == OutpostEventType.FactoryAutomated);
            Assert.That(chest, Is.GreaterThanOrEqualTo(0), "сундук rewardId цепей");
            Assert.Greater(unlocked, chest, "§4.2: сундук → FactoryUnlocked");
            Assert.Greater(auto, unlocked, "§4.2: FactoryUnlocked → проверка автодоставки");
            Assert.AreEqual((FactoryA, "milk", "ca002", 1, t0), (ev[auto].TargetId, ev[auto].ItemId, ev[auto].Detail, ev[auto].Count, ev[auto].Tick));
            var f = sim.FactoryById(FactoryA);
            Assert.IsTrue(f.Automated);
            Assert.AreEqual(22, sim.FactoryUnitsPerCycle(FactoryA), "приложение B п. 5: Common ур. 1 — floor(20 × 11/10) = 22");

            long transit = sim.FactoryTransitTicks(FactoryA);
            sim.Advance(t0 + 5 * Dairy + transit);
            var fev = FactoryEvents(Drain(sim));
            Assert.IsFalse(fev.Any(e => e.Type == OutpostEventType.FactoryReady), "у автоматического завода нет Ready");
            var dispatched = fev.Where(e => e.Type == OutpostEventType.GoodsDispatched).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(1, 5).Select(k => t0 + k * Dairy).ToArray(), dispatched.Select(e => e.Tick).ToArray(),
                "партии уходят в конце каждого цикла, без сдвига");
            Assert.IsTrue(dispatched.All(e => e.Extra == 1 && e.Count == 22));
            var arrivals = fev.Where(e => e.Type == OutpostEventType.GoodsArrived).ToList();
            CollectionAssert.AreEqual(dispatched.Select(e => e.Tick + transit).ToArray(), arrivals.Select(e => e.Tick).ToArray());
            Assert.AreEqual((FactoryStatus.Working, t0 + 5 * Dairy, t0 + 6 * Dairy, 5, 110L, 110L), (f.Status, f.CycleStart, f.CycleEnd, f.Cycles, f.Produced, f.Dispatched));
            Assert.AreEqual(110L, sim.GoodsCount("milk"));
            Assert.AreEqual(0, sim.Run.Counters.ManualDeliveries);
            AssertCore(sim, "автодоставка");
        }

        [Test]
        public void Auto_AfterUpgrade_ReadyBatchLeavesSameTick_NextCycleAt23Units()
        {
            // Приложение B п. 2: улучшение 1 → 2 — 2 копии и 50 эликсира; требование токена — ca002 ур. 2.
            var fx = OneFactory(DairyNeeds2, CardConfigs, p =>
            {
                Card(p, "ca002", 1, 2);
                p.Elixir = 50;
            });
            var state = fx.NewState();
            Endless(state, RockId);
            ArmChains(state, FactoryA);
            var sim = fx.Wrap(state);
            Unlock(sim, FactoryA);
            var f = sim.FactoryById(FactoryA);
            Assert.IsFalse(f.Automated, "ур. 1 < требования 2");
            sim.Advance(Dairy + 3 * S);
            Assert.AreEqual((FactoryStatus.Ready, 22), (f.Status, f.ReadyUnits), "выпуск фиксируется в конце цикла (ур. 1)");
            Drain(sim);

            long t = sim.Now;
            var r = sim.Submit(OutpostCommand.UpgradeCard("ca002"));
            Assert.IsTrue(r.Ok, r.ToString());
            var ev = Drain(sim);
            var order = ev.Where(e => e.Type == OutpostEventType.CardUpgraded || IsFactoryEvent(e.Type)).Select(e => e.Type).ToArray();
            CollectionAssert.AreEqual(new[] { OutpostEventType.CardUpgraded, OutpostEventType.FactoryAutomated, OutpostEventType.GoodsDispatched }, order, Describe(ev));
            var auto = ev.First(e => e.Type == OutpostEventType.FactoryAutomated);
            Assert.AreEqual(("ca002", 2, t), (auto.Detail, auto.Count, auto.Tick));
            var sent = ev.First(e => e.Type == OutpostEventType.GoodsDispatched);
            Assert.AreEqual((t, 22, 1), (sent.Tick, sent.Count, sent.Extra), "готовая партия уходит на этой же отметке");
            Assert.AreEqual((true, FactoryStatus.Working, t, t + Dairy, 0, 22L), (f.Automated, f.Status, f.CycleStart, f.CycleEnd, f.ReadyUnits, f.Dispatched));
            Assert.AreEqual(0, sim.Run.Counters.ManualDeliveries, "отправка автодоставкой — не ручная доставка");
            Assert.AreEqual(23, sim.FactoryUnitsPerCycle(FactoryA), "ур. 2: floor(20 × (1 + 3/20)) = 23");
            AssertCore(sim, "после улучшения");

            sim.Advance(t + Dairy);
            var next = FactoryEvents(Drain(sim)).Single(e => e.Type == OutpostEventType.GoodsDispatched);
            Assert.AreEqual((t + Dairy, 23, 1), (next.Tick, next.Count, next.Extra));
            Assert.IsTrue(f.Automated, "Automated не снимается");
        }

        [Test]
        public void Auto_FirstCopy_Mine3_RockChestAfterSawmillChains_SameTick_SaveLoadEqual()
        {
            // Приложение B п. 3: шахта 3, Sawmill (citrine, цель 33) требует ca003 ур. 1; камень 17 (rockonboardinggachawoodenmine3)
            // открывает сундук scg_m003_r01 с гарантированной ca003 ×1. Камень разбит после цепей — AUTO на той же отметке.
            const int sawmill = 33, rock = 17;
            var configs = CardConfigs;
            string plain = Scenario(configs, false, out var events);
            string saved = Scenario(configs, true, out _);
            AssertSameJson(plain, saved, "без save/load = с save/load");

            var chest = events.Single(e => e.Type == OutpostEventType.ChestOpened && e.ItemId == "scg_m003_r01");
            var auto = events.Single(e => e.Type == OutpostEventType.FactoryAutomated);
            Assert.AreEqual((sawmill, "wood", "ca003", 1), (auto.TargetId, auto.ItemId, auto.Detail, auto.Count));
            Assert.AreEqual(chest.Tick, auto.Tick, "первая копия из сундука камня включает AUTO на той же отметке");
            Assert.Less(events.IndexOf(chest), events.IndexOf(auto));
            Assert.IsFalse(events.Any(e => e.Type == OutpostEventType.CardUpgraded), "без команды улучшения");
            var first = events.First(e => e.Type == OutpostEventType.GoodsDispatched && e.TargetId == sawmill);
            Assert.AreEqual((1, 15 * S + 2 * S), (first.Extra, first.Tick), "первый цикл 15 с от разбитых цепей — автодоставкой");

            string Scenario(OutpostConfigSet cfg, bool reload, out List<OutpostEvent> sink)
            {
                sink = new List<OutpostEvent>();
                var state = StageProgressionService.NewGame(TestData.Balance, cfg, Seed, StartUtcMs, 3);
                var map = OutpostLevelAdapter.BuildMine(TestData.Balance, 3, cfg);
                Assert.AreEqual(0, state.Profile.CardLevel("ca003"), "новый профиль без ca003");
                Assert.AreEqual(("citrine", "ca003", 1), (map.Factory(sawmill).TypeId, map.Factory(sawmill).RequiredCardId, map.Factory(sawmill).RequiredCardLevel));
                Assert.AreEqual("rockonboardinggachawoodenmine3", map.Target(rock).TypeId);
                var sim = new OutpostSimulation(state, map, cfg);
                sim.Advance(2 * S);
                for (int i = 0; i < 100_000 && !sim.TargetById(sawmill).Destroyed; i++) Assert.IsTrue(sim.Submit(OutpostCommand.Strike(sawmill)).Ok);
                Assert.IsTrue(sim.TargetById(sawmill).Destroyed);
                Assert.IsFalse(sim.FactoryById(sawmill).Automated);
                sim.DrainEvents(sink);
                if (reload) sim = new OutpostSimulation(Clone(sim.State), map, cfg);
                sim.Advance(5 * S + 300_001);
                Assert.IsFalse(sim.TargetById(rock).Destroyed, "камень ещё цел");
                for (int i = 0; i < 100_000 && !sim.TargetById(rock).Destroyed; i++) Assert.IsTrue(sim.Submit(OutpostCommand.Strike(rock)).Ok);
                Assert.IsTrue(sim.TargetById(rock).Destroyed);
                Assert.IsTrue(sim.FactoryById(sawmill).Automated, "AUTO на отметке сундука");
                sim.DrainEvents(sink);
                AssertCore(sim, "после первой копии");
                if (reload) sim = new OutpostSimulation(Clone(sim.State), map, cfg);
                sim.Advance(60 * S);
                sim.DrainEvents(sink);
                AssertCore(sim, "60 с");
                return Json(sim.State);
            }
        }

        [Test]
        public void Auto_NeedsCopies_LevelZeroDoesNotCount_FlagNeverDrops()
        {
            var fx = OneFactory(DairyNeeds1, CardConfigs, p => Card(p, "ca002", 0));
            var state = fx.NewState();
            Endless(state, RockId);
            ArmChains(state, FactoryA);
            var sim = fx.Wrap(state);
            Unlock(sim, FactoryA);
            var f = sim.FactoryById(FactoryA);
            Assert.IsFalse(f.Automated, "уровень 0 требованию не удовлетворяет");
            Assert.AreEqual(20, sim.FactoryUnitsPerCycle(FactoryA), "и бонуса не даёт");

            // Карточка выдана вне ядра (DevTools «Cards»): та же проверка при пересоздании симуляции.
            sim.Advance(Dairy + S);
            Assert.AreEqual(FactoryStatus.Ready, f.Status);
            var st = Clone(sim.State);
            Card(st.Profile, "ca002", 1);
            var again = fx.Wrap(st);
            var ev = Drain(again);
            CollectionAssert.AreEqual(new[] { OutpostEventType.FactoryAutomated, OutpostEventType.GoodsDispatched }, FactoryEvents(ev).Select(e => e.Type).ToArray(), Describe(ev));
            var g = again.FactoryById(FactoryA);
            Assert.AreEqual((true, FactoryStatus.Working, again.Now), (g.Automated, g.Status, g.CycleStart));
            Assert.AreEqual(1, again.Run.PipeBatches.Count, "готовая партия ушла при загрузке");
            AssertCore(again, "после пересоздания");

            // Снимок, записанный ядром: проверка при загрузке ничего не меняет и событий не даёт.
            again.Advance(again.Now + 3 * Dairy);
            string json = Json(again.State);
            var loaded = fx.Load(json);
            AssertSameJson(json, Json(loaded.State), "загрузка не меняет снимок ядра");
            Assert.AreEqual(0, FactoryEvents(Drain(loaded)).Count);

            // Флаг не снимается, даже если уровень карточки в профиле «пропал» (только вне ядра).
            loaded.State.Profile.Cards.Remove("ca002");
            loaded.Advance(loaded.Now + 2 * Dairy);
            var reloaded = fx.Load(Json(loaded.State));
            Assert.IsTrue(reloaded.FactoryById(FactoryA).Automated);
        }

        // ───────────────────────────── Выпуск: обе карточки типа ─────────────────────────────

        // Ревью этапа 1 (ТЗ 2.2 §4.4): правило выпуска — одна функция ядра FactoryMath.UnitsPerCycle (Σ Output — только
        // CardService.FactoryOutputBonus); копии в TeamStats (CardFactoryOutput, FactoryOutputFor, FactoryUnits, FactoryOutputBonus)
        // удалены, поэтому проверки «TeamStats и прямой расчёт совпадают» заменены сверкой единственной функции с независимым
        // ожиданием floor(UnitsPerCycle × (1 + Output(C) + Output(U))) и с отсутствием влияния на чужой тип.
        [Test]
        public void Units_BothCardsOfType_EveryUpgradeAddsAtLeastOne_ControlValues()
        {
            var configs = CardConfigs;
            var cat = configs.Factories;
            Assert.IsNotNull(cat);
            foreach (var type in cat.Types)
            {
                var common = configs.Cards.Get(type.CommonCardId);
                var uncommon = type.UncommonCardId != null ? configs.Cards.Get(type.UncommonCardId) : null;
                Assert.AreEqual(CardEffectKind.FactoryOutput, common.Effect.Kind, type.TypeId);
                if (uncommon != null) Assert.AreEqual(CardEffectKind.FactoryOutput, uncommon.Effect.Kind, type.TypeId);
                int maxU = uncommon == null ? 0 : type.UncommonMaxLevel;
                int[,] grid = new int[type.CommonMaxLevel + 1, maxU + 1];
                for (int lc = 0; lc <= type.CommonMaxLevel; lc++)
                    for (int lu = 0; lu <= maxU; lu++)
                    {
                        var p = new OutpostProfile();
                        if (lc > 0) Card(p, common.Id, lc);
                        if (lu > 0) Card(p, uncommon.Id, lu);
                        var bonus = CardService.FactoryOutputBonus(p, configs.Cards, type.TypeId);
                        var expected = (lc == 0 ? Rational.Zero : Rational.Of(1, 10) + Rational.Of(1, 20) * (lc - 1)) +
                                       (lu == 0 ? Rational.Zero : Rational.Of(1, 10) + Rational.Of(1, 20) * (lu - 1));
                        Assert.AreEqual(expected, bonus, type.TypeId + " C" + lc + " U" + lu + ": Output(L) = 1/10 + 1/20·(L − 1), бонусы складываются");
                        grid[lc, lu] = FactoryMath.UnitsPerCycle(type.Rule, p, configs.Cards);
                        Assert.AreEqual((int)(Rational.FromLong(type.UnitsPerCycle) * (Rational.One + expected)).Floor(), grid[lc, lu],
                            type.TypeId + " C" + lc + " U" + lu + ": floor(UnitsPerCycle × (1 + Output(C) + Output(U)))");
                        var otherType = cat.Get(type.TypeId == "amethyst" ? "citrine" : "amethyst");
                        Assert.AreEqual(otherType.UnitsPerCycle, FactoryMath.UnitsPerCycle(otherType.Rule, p, configs.Cards), "чужой тип без бонуса");
                        if (lc > 0) Assert.GreaterOrEqual(grid[lc, lu], grid[lc - 1, lu] + 1, type.TypeId + ": улучшение Common " + (lc - 1) + "→" + lc);
                        if (lu > 0) Assert.GreaterOrEqual(grid[lc, lu], grid[lc, lu - 1] + 1, type.TypeId + ": улучшение Uncommon " + (lu - 1) + "→" + lu);
                    }
                Assert.AreEqual(20, grid[0, 0]);
                Assert.AreEqual(22, grid[1, 0], "приложение B п. 5");
                if (uncommon != null)
                {
                    Assert.AreEqual(22, grid[0, 1], "Uncommon действует так же, как Common");
                    Assert.AreEqual(26, grid[3, 1], "приложение B п. 5: C3 + U1");
                    Assert.AreEqual(48, grid[14, 12], "приложение B п. 5: C14 + U12");
                }
            }

            // Карточки чужого типа, ca001 и глобальные выпуска не меняют.
            var other = new OutpostProfile();
            Card(other, "ca003", 14);
            Card(other, "ca001", 5);
            Card(other, "ca034", 3);
            Assert.AreEqual(Rational.Zero, CardService.FactoryOutputBonus(other, configs.Cards, "amethyst"));
            Assert.AreEqual(20, FactoryMath.UnitsPerCycle(cat.Get("amethyst").Rule, other, configs.Cards));
            Assert.AreEqual(35, FactoryMath.UnitsPerCycle(cat.Get("citrine").Rule, other, configs.Cards), "ca003 ур. 14: floor(20 × 7/4) = 35");
            Assert.AreEqual(20, FactoryMath.UnitsPerCycle(cat.Get("citrine").Rule, other, null), "без каталога карточек бонуса нет");
            Assert.AreEqual(0, FactoryMath.UnitsPerCycle(null, other, configs.Cards), "нет правила — 0");
        }

        [Test]
        public void Units_FixedAtCycleEnd_SimReaderMatchesBothCards()
        {
            var fx = OneFactory(DairyNeeds2, CardConfigs, p =>
            {
                Card(p, "ca002", 3);
                Card(p, "ca014", 1);
            });
            var state = fx.NewState();
            Endless(state, RockId);
            ArmChains(state, FactoryA);
            var sim = fx.Wrap(state);
            Unlock(sim, FactoryA);
            Assert.IsTrue(sim.FactoryById(FactoryA).Automated, "ca002 ур. 3 ≥ 2");
            Assert.AreEqual(26, sim.FactoryUnitsPerCycle(FactoryA), "C3 + U1: floor(20 × 13/10) = 26");
            sim.Advance(Dairy);
            Assert.AreEqual(26, sim.Run.PipeBatches.Single().Units);
            Assert.AreEqual(0, sim.FactoryUnitsPerCycle(RockId), "не завод");
        }

        // ───────────────────────────── Ready: поля цикла (ревью этапа 1) ─────────────────────────────

        /// <summary>
        /// ТЗ 2.2 §4.3, §4.4, §13; CORE_SPEC §16.4: цикл [CycleStart, CycleEnd) длиной CycleTicks есть только у Working. У готовой
        /// партии (Ready) цикла нет — оба поля −1, одинаково после полного цикла и после досрочного DEV «Finish factory cycles»
        /// (раньше DEV менял только CycleEnd, и в Ready оставался укороченный цикл). Автоматический завод после DEV начинает новый
        /// полный цикл с Now. Инвариант ловит Ready с полями цикла.
        /// </summary>
        [Test]
        public void Ready_HasNoCycle_AfterFullCycleAndDevFinish_AutoRestartsFullCycle()
        {
            var fx = TwoFactories(DairyNeeds1, SawmillNeeds1, CardConfigs, p => Card(p, "ca003", 1));
            var state = fx.NewState();
            Endless(state, RockId);
            ArmChains(state, FactoryA);
            ArmChains(state, FactoryB);
            var sim = fx.Wrap(state);
            const long t0 = 1;
            sim.Advance(t0);
            Unlock(sim, FactoryA);
            Unlock(sim, FactoryB);
            var dairy = sim.FactoryById(FactoryA);
            var sawmill = sim.FactoryById(FactoryB);
            Assert.IsFalse(dairy.Automated, "ca002 нет — Dairy вручную");
            Assert.IsTrue(sawmill.Automated, "ca003 ур. 1 ≥ 1 — Sawmill сам");
            Drain(sim);

            // Полный цикл: Ready без цикла; отметка готовности — Tick события FactoryReady.
            sim.Advance(t0 + Dairy);
            Assert.AreEqual((FactoryStatus.Ready, -1L, -1L, 20, 1), (dairy.Status, dairy.CycleStart, dairy.CycleEnd, dairy.ReadyUnits, dairy.Cycles));
            Assert.AreEqual(t0 + Dairy, FactoryEvents(Drain(sim)).Single(e => e.Type == OutpostEventType.FactoryReady).Tick);
            AssertCore(sim, "Ready после полного цикла");

            // Доставка — новый полный цикл с отметки команды.
            sim.Advance(t0 + Dairy + 2 * S);
            long td = sim.Now;
            Assert.IsTrue(sim.Submit(OutpostCommand.DeliverFactory(FactoryA)).Ok);
            Assert.AreEqual((FactoryStatus.Working, td, td + Dairy), (dairy.Status, dairy.CycleStart, dairy.CycleEnd));

            // DEV посреди циклов обоих заводов (Dairy: 3 с из 8, Sawmill: 13 с из 15).
            sim.Advance(td + 3 * S);
            long tf = sim.Now;
            Assert.AreEqual((FactoryStatus.Working, t0, t0 + Sawmill, 0), (sawmill.Status, sawmill.CycleStart, sawmill.CycleEnd, sawmill.Cycles));
            Assert.Greater(dairy.CycleEnd, tf);
            Drain(sim);
            Assert.AreEqual(2, sim.DebugFinishFactoryCycles(), "«Finish factory cycles»");
            Assert.AreEqual((FactoryStatus.Ready, -1L, -1L, 20, 2), (dairy.Status, dairy.CycleStart, dairy.CycleEnd, dairy.ReadyUnits, dairy.Cycles),
                "ручной: тот же Ready без цикла, что после полного цикла");
            Assert.AreEqual((FactoryStatus.Working, tf, tf + Sawmill, 1), (sawmill.Status, sawmill.CycleStart, sawmill.CycleEnd, sawmill.Cycles),
                "автоматический: новый полный цикл с Now");
            var ev = FactoryEvents(Drain(sim));
            Assert.AreEqual(tf, ev.Single(e => e.Type == OutpostEventType.FactoryReady).Tick, Describe(ev));
            var sent = ev.Single(e => e.Type == OutpostEventType.GoodsDispatched);
            Assert.AreEqual((FactoryB, tf, 22, 1), (sent.TargetId, sent.Tick, sent.Count, sent.Extra),
                "партия Sawmill ушла сама: ca003 ур. 1 — floor(20 × 11/10) = 22");
            AssertCore(sim, "после DEV");

            // Без нового округления после save/load; дальше всё идёт обычным порядком.
            string json = OutpostJson.Serialize(sim.State);
            Assert.AreEqual(json, OutpostJson.Serialize(OutpostJson.Deserialize<OutpostGameState>(json)));
            sim.Advance(tf + Sawmill);
            Assert.AreEqual((FactoryStatus.Working, tf + Sawmill, tf + 2 * Sawmill, 2), (sawmill.Status, sawmill.CycleStart, sawmill.CycleEnd, sawmill.Cycles));
            Assert.AreEqual((FactoryStatus.Ready, -1L, -1L), (dairy.Status, dairy.CycleStart, dairy.CycleEnd), "ручной ждёт доставки");
            AssertCore(sim, "после DEV и полного цикла Sawmill");

            // Инвариант: Ready с полями цикла — нарушение (оба поля по отдельности).
            foreach (Action<FactoryState> breakIt in new Action<FactoryState>[] { f => f.CycleStart = td, f => f.CycleEnd = tf })
            {
                var broken = OutpostJson.Deserialize<OutpostGameState>(json);
                breakIt(broken.Run.Factories.First(x => x.TargetId == FactoryA));
                var errors = OutpostInvariants.Check(broken, sim.Map, CardConfigs.Equipment, CardConfigs.Cards);
                Assert.IsTrue(errors.Any(e => e.Contains("Ready с циклом")), string.Join("; ", errors));
            }
        }

        // ───────────────────────────── Шаг 4б и сроки ─────────────────────────────

        [Test]
        public void Step4b_ArrivalsThenCycleEnds_OrderedByTickFactorySeq()
        {
            // FactoryB (Sawmill, 15 с) и FactoryA (Dairy, 8 с) открыты на 0; цикл A сдвинут так, будто цепи A разбиты на 7 с:
            // оба цикла кончаются на 15 с.
            var fx = TwoFactories(DairyNeeds1, SawmillNeeds1, null, p => Card(p, "ca003", 1));
            var state = fx.NewState();
            Endless(state, RockId);
            ArmChains(state, FactoryA);
            ArmChains(state, FactoryB);
            var sim = fx.Wrap(state);
            Unlock(sim, FactoryB);
            Unlock(sim, FactoryA);
            sim.Advance(7 * S);
            Assert.IsTrue(sim.FactoryById(FactoryB).Automated);
            Assert.IsFalse(sim.FactoryById(FactoryA).Automated);

            // Партии, приходящие на 15 с: Seq не совпадает с порядком заводов.
            var st = Clone(sim.State);
            var run = st.Run;
            var a = run.Factories.First(f => f.TargetId == FactoryA);
            Assert.AreEqual(FactoryStatus.Working, a.Status);
            a.CycleStart = 7 * S;
            a.CycleEnd = 15 * S;
            run.PipeBatches.Add(new PipeBatch { Seq = 1, FactoryId = FactoryB, GoodId = "wood", Units = 5, DepartTick = 7 * S, ArriveTick = 15 * S });
            run.PipeBatches.Add(new PipeBatch { Seq = 2, FactoryId = FactoryA, GoodId = "milk", Units = 7, DepartTick = 7 * S, ArriveTick = 15 * S });
            run.PipeBatches.Add(new PipeBatch { Seq = 3, FactoryId = FactoryA, GoodId = "milk", Units = 4, DepartTick = 7 * S, ArriveTick = 15 * S });
            run.PipeBatches.Add(new PipeBatch { Seq = 4, FactoryId = FactoryB, GoodId = "wood", Units = 6, DepartTick = 7 * S, ArriveTick = 14 * S });
            run.NextPipeBatchSeq = 5;
            var fa = run.Factories.First(f => f.TargetId == FactoryA);
            var fb = run.Factories.First(f => f.TargetId == FactoryB);
            fa.Produced = fa.Dispatched = 11;
            fb.Produced = fb.Dispatched = 11;
            var sim2 = fx.Wrap(st);
            AssertCore(sim2, "подготовленный снимок");
            Drain(sim2);
            Assert.LessOrEqual(sim2.NextEventTick(), 14 * S, "срок прибытия входит в NextEventTick");
            sim2.Advance(15 * S);
            var ev = FactoryEvents(Drain(sim2));
            var expected = new[]
            {
                (OutpostEventType.GoodsArrived, 14 * S, FactoryB, 4L),
                (OutpostEventType.GoodsArrived, 15 * S, FactoryA, 2L),
                (OutpostEventType.GoodsArrived, 15 * S, FactoryA, 3L),
                (OutpostEventType.GoodsArrived, 15 * S, FactoryB, 1L),
                (OutpostEventType.FactoryReady, 15 * S, FactoryA, -1L),
                (OutpostEventType.GoodsDispatched, 15 * S, FactoryB, 5L),
            };
            CollectionAssert.AreEqual(expected, ev.Select(e => (e.Type, e.Tick, e.TargetId, e.BatchSeq)).ToArray(), Describe(ev));
            Assert.AreEqual((11L, 11L), (sim2.GoodsCount("milk"), sim2.GoodsCount("wood")));
            AssertCore(sim2, "после шага 4б");
        }

        [Test]
        public void Step4b_RunsAfterCoinDepotAndBeforeCompletion()
        {
            // Камень (0,0) — единственная цель после открытия завода: шахта завершается на отметке сдачи последней порции.
            var fx = OneFactory(DairyNeeds1);
            var probe = fx.NewState();
            ArmChains(probe, FactoryA);
            var dry = fx.Wrap(Clone(probe));
            Unlock(dry, FactoryA);
            RunToReady(dry);
            long done = dry.Run.Counters.CompletedTick;
            Assert.Greater(done, 0L);

            var sim = fx.Wrap(Clone(probe));
            Unlock(sim, FactoryA);
            var run = sim.Run;
            var f = run.Factories.Single();
            run.PipeBatches.Add(new PipeBatch { Seq = 1, FactoryId = FactoryA, GoodId = "milk", Units = 9, DepartTick = 0, ArriveTick = done });
            run.PipeBatches.Add(new PipeBatch { Seq = 2, FactoryId = FactoryA, GoodId = "milk", Units = 3, DepartTick = 0, ArriveTick = done + 1 });
            run.NextPipeBatchSeq = 3;
            f.Produced = f.Dispatched = 12;
            sim = fx.Wrap(Clone(sim.State));
            var events = new List<OutpostEvent>();
            RunToReady(sim, events);
            Assert.AreEqual(done, sim.Run.Counters.CompletedTick, "сроки заводов не меняют работу котиков");
            int deposit = events.FindLastIndex(e => e.Type == OutpostEventType.CargoDeposited);
            int arrival = events.FindIndex(e => e.Type == OutpostEventType.GoodsArrived && e.BatchSeq == 1);
            int completed = events.FindIndex(e => e.Type == OutpostEventType.StageCompleted);
            Assert.AreEqual(done, events[deposit].Tick);
            Assert.Less(deposit, arrival, "шаг 4б после разгрузки монет (фаза 4)");
            Assert.Less(arrival, completed, "шаг 4б до завершения (фаза 5)");
            Assert.AreEqual(9L, sim.GoodsCount("milk"), "партия на отметке завершения пришла");
            Assert.AreEqual(2L, sim.Run.PipeBatches.Single().Seq, "следующая — осталась в трубе");
        }

        [Test]
        public void Timing_CycleEndAndArrivalBetweenCatEvents_ExactTicks()
        {
            var fx = OneFactory(DairyNeeds1, null, p => Card(p, "ca002", 1));
            var state = fx.NewState();
            Endless(state, RockId);
            ArmChains(state, FactoryA);
            var sim = fx.Wrap(state);
            // Отметка 1: котики идут по рёбрам с отметки 0 и в этот тик ничего не начинают — концы цикла и прибытия (целые секунды
            // от t0) не совпадают с их событиями (кратными 0,2 с от 0).
            const long t0 = 1;
            sim.Advance(t0);
            Unlock(sim, FactoryA);
            Drain(sim);
            long end = t0 + Dairy;
            long arrive = end + sim.FactoryTransitTicks(FactoryA);
            var ticks = new List<long>();
            var byTick = new Dictionary<long, List<OutpostEvent>>();
            while (sim.Now < arrive + 2 * S)
            {
                Assert.IsTrue(sim.AdvanceToNextEvent());
                ticks.Add(sim.Now);
                byTick[sim.Now] = Drain(sim);
            }
            CollectionAssert.Contains(ticks, end, "конец цикла — отдельная отметка");
            CollectionAssert.Contains(ticks, arrive, "прибытие — отдельная отметка");
            Assert.IsTrue(byTick[end].All(e => IsFactoryEvent(e.Type)), "на отметке конца цикла нет событий котиков: " + Describe(byTick[end]));
            Assert.AreEqual(OutpostEventType.GoodsDispatched, byTick[end].Single().Type);
            Assert.IsTrue(byTick[arrive].Any(e => e.Type == OutpostEventType.GoodsArrived));
            Assert.IsTrue(ticks.Any(t => t > t0 && t < end && byTick[t].Any(e => !IsFactoryEvent(e.Type))), "котики работают между событиями завода");

            // Ручной завод: FactoryReady ровно на CycleEnd между событиями котиков.
            var manual = OneFactory(DairyNeeds2, null, p => Card(p, "ca002", 1));
            var ms = manual.NewState();
            Endless(ms, RockId);
            ArmChains(ms, FactoryA);
            var msim = manual.Wrap(ms);
            msim.Advance(t0);
            Unlock(msim, FactoryA);
            Drain(msim);
            msim.Advance(end - 1);
            Assert.AreEqual(FactoryStatus.Working, msim.FactoryById(FactoryA).Status);
            Assert.LessOrEqual(msim.NextEventTick(), end, "конец цикла — в NextEventTick");
            Assert.AreEqual(end, AdvanceUntil(msim, end));
            var ready = FactoryEvents(Drain(msim)).Single();
            Assert.AreEqual((OutpostEventType.FactoryReady, end), (ready.Type, ready.Tick));
        }

        static long AdvanceUntil(OutpostSimulation sim, long tick)
        {
            while (sim.Now < tick) Assert.IsTrue(sim.AdvanceToNextEvent());
            return sim.Now;
        }

        [Test]
        public void Watchdog_AutoFactory8s_StuckCats_FiresAfterWatchdogTicks()
        {
            // Открытые заводы S1 и S2 — стены (§4.1): камень (0,0) недостижим, котики стоят; S1 работает сам каждые 8 с.
            var fx = EnclosedRock(DairyNeeds1, SawmillNeeds1, null, p => Card(p, "ca002", 1));
            var state = fx.NewState();
            ArmChains(state, EnclosingS1);
            ArmChains(state, EnclosingS2);
            var sim = fx.Wrap(state);
            Unlock(sim, EnclosingS1);
            Unlock(sim, EnclosingS2);
            Assert.IsTrue(sim.FactoryById(EnclosingS1).Automated);
            Assert.AreEqual(RunPhase.Running, sim.Run.Phase, "камень жив");
            var events = new List<OutpostEvent>();
            sim.Advance(300 * S);
            sim.DrainEvents(events);
            Assert.IsTrue(sim.Run.WatchdogFired, "watchdog ловит застрявших котиков и при работающих заводах");
            var fired = events.Single(e => e.Type == OutpostEventType.WatchdogFired);
            long watchdog = sim.Balance.WatchdogTicks;
            Assert.AreEqual(sim.Run.LastChangeTick + watchdog + 1, fired.Tick, "ровно через WatchdogTicks после последнего изменения котиков");
            Assert.Less(sim.Run.LastChangeTick, 5 * S);
            Assert.Greater(events.Count(e => e.Type == OutpostEventType.GoodsDispatched && e.Tick < fired.Tick), 5, "циклы шли, но Touch не вызывали");
            Assert.IsTrue(events.Any(e => e.Type == OutpostEventType.FactoryReady && e.TargetId == EnclosingS2), "ручной завод готов и ждёт");
            Assert.IsTrue(sim.Run.WatchdogDiagnostic.Contains("нет изменений"));
        }

        // ───────────────────────────── Завершение шахты ─────────────────────────────

        [Test]
        public void ReadyToContinue_StopsFactories_PipesFrozen_SellAndDeliverRefused()
        {
            var fx = TwoFactories(DairyNeeds2, SawmillNeeds1, CardConfigs, p =>
            {
                Card(p, "ca002", 1, 2);
                Card(p, "ca003", 1);
                p.Elixir = 50;
            });
            var probe = fx.NewState();
            ArmChains(probe, FactoryA);
            ArmChains(probe, FactoryB);
            var sim = fx.Wrap(probe);
            Unlock(sim, FactoryA);
            Unlock(sim, FactoryB);
            // Партия, которая не успеет прийти до завершения.
            var run = sim.Run;
            run.PipeBatches.Add(new PipeBatch { Seq = run.NextPipeBatchSeq++, FactoryId = FactoryA, GoodId = "milk", Units = 5, DepartTick = 0, ArriveTick = 3600 * S });
            var fa = run.Factories.First(f => f.TargetId == FactoryA);
            fa.Produced += 5;
            fa.Dispatched += 5;
            sim = fx.Wrap(Clone(sim.State));
            RunToReady(sim);
            run = sim.Run;
            Assert.AreEqual(RunPhase.ReadyToContinue, run.Phase);
            foreach (var f in run.Factories)
                Assert.AreEqual((FactoryStatus.Stopped, -1L, -1L), (f.Status, f.CycleStart, f.CycleEnd), "§4.6: заводы стоят");
            Assert.IsTrue(run.PipeBatches.Any(b => b.ArriveTick == 3600 * S), "партия в трубе не пришла");
            Assert.AreEqual(long.MaxValue, sim.NextEventTick(), "сроки заводов в ReadyToContinue не учитываются");

            string before = FactoryJson(run);
            var r = sim.Advance(run.Now + 7200 * S);
            Assert.IsTrue(r.Reached);
            Assert.AreEqual(0, r.ProcessedTicks, "Advance завершается, не зацикливаясь на партии");
            AssertSameJson(before, FactoryJson(sim.Run), "Advance в ReadyToContinue не меняет заводы, трубы и склад");
            Assert.IsFalse(sim.Run.WatchdogFired);

            Assert.AreEqual("phase", sim.Submit(OutpostCommand.SellGoods(null)).Reason, "Mine complete");
            Assert.AreEqual("phase", sim.Submit(OutpostCommand.SellGoods("milk")).Reason);
            Assert.AreEqual("phase", sim.Submit(OutpostCommand.DeliverFactory(FactoryA)).Reason);
            AssertSameJson(before, FactoryJson(sim.Run), "отказы ничего не меняют");

            // Улучшение в ReadyToContinue: флаг включается, партии не отправляются.
            Drain(sim);
            Assert.IsTrue(sim.Submit(OutpostCommand.UpgradeCard("ca002")).Ok);
            var g = sim.FactoryById(FactoryA);
            Assert.AreEqual((true, FactoryStatus.Stopped), (g.Automated, g.Status));
            var ev = FactoryEvents(Drain(sim));
            CollectionAssert.AreEqual(new[] { OutpostEventType.FactoryAutomated }, ev.Select(e => e.Type).ToArray(), Describe(ev));
            Assert.AreEqual(run.PipeBatches.Count, sim.Run.PipeBatches.Count);
            AssertCore(sim, "ReadyToContinue после улучшения");
        }

        [Test]
        public void Transition_ResetsFactoriesPipesWarehouseAndCounters_CardsKept()
        {
            var fx = OneFactory(DairyNeeds1, CardConfigs, p => Card(p, "ca002", 2));
            var probe = fx.NewState();
            Tough(probe, RockId, 400);
            ArmChains(probe, FactoryA);
            var sim = fx.Wrap(probe);
            Unlock(sim, FactoryA);
            sim.Advance(Dairy + 5 * S);
            Assert.IsTrue(sim.Submit(OutpostCommand.SellGoods(null)).Ok);
            Assert.Greater(sim.Run.GoodsSoldGold.Sign, 0);
            RunToReady(sim);
            var next = PrepareTransition(sim.State, CardConfigs, StartUtcMs + 600_000);
            var run = next.Run;
            Assert.AreEqual(2, run.Zone);
            Assert.AreEqual(1, run.Factories.Count, "шахта 2: Dairy");
            Assert.AreEqual((FactoryStatus.Locked, false, 0L), (run.Factories[0].Status, run.Factories[0].Automated, run.Factories[0].Produced));
            Assert.AreEqual(0, run.PipeBatches.Count);
            Assert.AreEqual(1L, run.NextPipeBatchSeq);
            Assert.IsTrue(run.Warehouse.Values.All(v => v == 0));
            Assert.AreEqual(BigInteger.Zero, run.GoodsSoldGold);
            Assert.AreEqual(BigInteger.Zero, run.Wallet);
            Assert.AreEqual(0, run.Counters.ManualDeliveries);
            Assert.AreEqual(2, next.Profile.CardLevel("ca002"), "карточки остаются");
            Assert.AreEqual(sim.Run.FactoryRules.CanonicalJson(), run.FactoryRules.CanonicalJson());
            var nextSim = new OutpostSimulation(next, OutpostLevelAdapter.ForRun(TestData.Balance, run), CardConfigs);
            AssertCore(nextSim, "шахта 2");
        }

        // ───────────────────────────── Рынок и учёт золота ─────────────────────────────

        [Test]
        public void Market_Zone2_ControlPrices_SellAccounting_Refusals()
        {
            var configs = OutpostConfigSet.LoadDefault().WithTopologies(null);
            var state = StageProgressionService.NewGame(TestData.Balance, configs, Seed, StartUtcMs, 2);
            var map = OutpostLevelAdapter.BuildMine(TestData.Balance, 2, configs);
            var sim = new OutpostSimulation(state, map, configs);
            int dairy = map.Factories.Single().TargetId;
            // ТЗ 2.2 §9, приложение B п. 6 (WP7): контрольные цены пересчитаны под итог калибровки PriceRate = 4 (было 5/16).
            Assert.AreEqual(new BigInteger(10), sim.GoodsPrice("milk"), "приложение B п. 6: ceil(5 × 6/5 × 8/5) = ceil(9,6) = 10");
            Assert.AreEqual(new BigInteger(720), sim.GoodsPrice("steel"), "сталь в шахте 2: ceil(5 × 6/5 × 120) = 720");
            Assert.AreEqual("no-goods", sim.Submit(OutpostCommand.SellGoods(null)).Reason, "Nothing to sell");
            Assert.AreEqual("no-goods", sim.Submit(OutpostCommand.SellGoods("milk")).Reason);
            Assert.AreEqual("unknown", sim.Submit(OutpostCommand.SellGoods("steel")).Reason, "товара нет среди заводов шахты");
            Assert.AreEqual("locked", sim.Submit(OutpostCommand.DeliverFactory(dairy)).Reason);
            Assert.AreEqual("unknown", sim.Submit(OutpostCommand.DeliverFactory(map.Targets.First(t => !t.IsFactory).Id)).Reason);

            Assert.AreEqual(1, sim.DebugUnlockFactories());
            sim.Advance(Dairy);
            Assert.IsTrue(sim.Submit(OutpostCommand.DeliverFactory(dairy)).Ok);
            sim.Advance(Dairy + sim.FactoryTransitTicks(dairy));
            Assert.AreEqual(20L, sim.GoodsCount("milk"));
            Assert.AreEqual(new BigInteger(200), sim.WarehouseValue());
            Drain(sim);

            var run = sim.Run;
            var wallet = run.Wallet;
            var credited = run.TotalCredited;
            var counters = run.Counters.DeliveredValue;
            var r = sim.Submit(OutpostCommand.SellGoods("milk"));
            Assert.IsTrue(r.Ok, r.ToString());
            Assert.AreEqual((20, new BigInteger(200)), (r.Levels, r.Cost));
            Assert.AreEqual(wallet + 200, run.Wallet, "золото сразу в кошелёк");
            Assert.AreEqual(credited, run.TotalCredited, "TotalCredited — только монеты");
            Assert.AreEqual(counters, run.Counters.DeliveredValue);
            Assert.AreEqual(new BigInteger(200), run.GoodsSoldGold);
            Assert.AreEqual((0L, 20L), (run.Warehouse["milk"], run.GoodsSold["milk"]));
            Assert.AreEqual(run.TotalCredited + run.BonusCredited + run.GoodsSoldGold - run.TotalSpent, run.Wallet);
            var sold = FactoryEvents(Drain(sim)).Single();
            Assert.AreEqual((OutpostEventType.GoodsSold, "milk", 20, new BigInteger(200)), (sold.Type, sold.ItemId, sold.Count, sold.Value));
            AssertCore(sim, "после продажи");

            string json = Json(sim.State);
            Assert.AreEqual("no-goods", sim.Submit(OutpostCommand.SellGoods("milk")).Reason);
            Assert.AreEqual("no-goods", sim.Submit(OutpostCommand.SellGoods(null)).Reason);
            AssertSameJson(json, Json(sim.State), "отказ продажи ничего не меняет");

            // Покупка на золото товара: траты учитываются, равенство кошелька держится.
            var q = sim.Quote(UpgradeBranch.Pickaxes, BuyMode.One);
            if (q.CanBuy)
            {
                Assert.IsTrue(sim.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Pickaxes)).Ok);
                AssertCore(sim, "покупка на золото товара");
            }

            var zone1 = StageProgressionService.NewGame(TestData.Balance, configs, Seed, StartUtcMs, 1);
            var z1 = new OutpostSimulation(zone1, OutpostLevelAdapter.BuildMine(TestData.Balance, 1, configs), configs);
            Assert.AreEqual("no-factories", z1.Submit(OutpostCommand.SellGoods(null)).Reason, "No factories in this mine");
        }

        [Test]
        public void Market_SellAll_ByTypeNumber_PricesFromRunSnapshot()
        {
            var fx = TwoFactories(DairyNeeds1, SawmillNeeds1);
            var state = fx.NewState();
            Endless(state, RockId);
            ArmChains(state, FactoryA);
            ArmChains(state, FactoryB);
            var sim = fx.Wrap(state);
            Unlock(sim, FactoryA);
            Unlock(sim, FactoryB);
            var st = Clone(sim.State);
            GiveGoods(st, FactoryB, 40);
            GiveGoods(st, FactoryA, 30);
            // Синтетическая шахта — зона 1 (S = 1). ТЗ 2.2 §9 (WP7): цены при PriceRate = 4 — молоко ceil(5 × 8/5) = 8, дерево ceil(5 × 3) = 15.
            var changed = ConfigsWithMilkFactor("1/2");
            var sim2 = new OutpostSimulation(st, fx.Map, changed);
            Assert.AreEqual(new BigInteger(8), sim2.GoodsPrice("milk"), "цена — из снимка шахты, правка конфигурации на неё не влияет");
            Assert.AreEqual(new BigInteger(15), sim2.GoodsPrice("wood"));
            Assert.AreEqual(new BigInteger(240 + 600), sim2.WarehouseValue());
            Drain(sim2);
            var r = sim2.Submit(OutpostCommand.SellGoods(null));
            Assert.IsTrue(r.Ok);
            Assert.AreEqual((70, new BigInteger(840)), (r.Levels, r.Cost));
            var ev = FactoryEvents(Drain(sim2));
            CollectionAssert.AreEqual(new[] { ("milk", 30, new BigInteger(240)), ("wood", 40, new BigInteger(600)) },
                ev.Select(e => (e.ItemId, e.Count, e.Value)).ToArray(), "по событию на товар, по номеру типа");
            Assert.AreEqual(new BigInteger(840), sim2.Run.GoodsSoldGold);
            AssertCore(sim2, "Sell all");

            // Новая шахта берёт новые правила.
            var fresh = StageProgressionService.NewGame(TestData.Balance, changed, Seed, StartUtcMs, 2).Run;
            Assert.AreEqual(new BigInteger(3), FactoryQueries.Price(fresh, "milk"), "ceil(5 × 6/5 × 1/2) = 3");
        }

        static OutpostConfigSet ConfigsWithMilkFactor(string factor)
        {
            var j = JObject.Parse(OutpostConfigSet.DefaultFactoryRulesJson);
            foreach (var g in (JArray)j["Goods"])
                if ((string)g["GoodId"] == "milk") g["PriceFactor"] = factor;
            return OutpostConfigSet.Parse(OutpostConfigSet.DefaultBalanceJson, OutpostConfigSet.DefaultEquipmentJson, OutpostConfigSet.DefaultRewardsJson,
                OutpostConfigSet.DefaultOverridesJson, OutpostConfigSet.DefaultCardRulesJson, OutpostConfigSet.DefaultTopologyJson,
                OutpostConfigSet.DefaultStoreJson, j.ToString(Formatting.None)).WithTopologies(null);
        }

        // ───────────────────────────── Онлайн = офлайн = тайм-скип, save/load ─────────────────────────────

        /// <summary>Снимок: Dairy (ручной) готов и ждёт, Sawmill (AUTO) работает, партия в трубе.</summary>
        static OutpostGameState Committed(out OutpostFixtureB fx)
        {
            fx = TwoFactories(DairyNeeds1, SawmillNeeds1, CardConfigs, p => Card(p, "ca003", 1));
            var state = fx.NewState();
            Endless(state, RockId);
            ArmChains(state, FactoryA);
            ArmChains(state, FactoryB);
            var sim = fx.Wrap(state);
            Unlock(sim, FactoryA);
            Unlock(sim, FactoryB);
            sim.Advance(15 * S + 523_457);
            Assert.AreEqual(FactoryStatus.Ready, sim.FactoryById(FactoryA).Status);
            Assert.IsTrue(sim.FactoryById(FactoryB).Automated);
            Assert.AreEqual(1, sim.Run.PipeBatches.Count, "партия Sawmill в пути");
            var st = Clone(sim.State);
            st.Run.LastProcessedUtcMs = StartUtcMs + sim.Now / 1000;
            return st;
        }

        [Test]
        public void OnlineEqualsOfflineEqualsSkip_ManualWaitsAutoRuns()
        {
            var committed = Committed(out var fx);
            const long minutes = 50;
            long ms = minutes * 60_000;

            var online = fx.Wrap(Clone(committed));
            online.Advance(committed.Run.Now + ms * 1000);

            var offline = RunOffline(committed, fx.Map, CardConfigs, committed.Run.LastProcessedUtcMs + ms, out var summary);
            Assert.AreEqual(ms, summary.AppliedMs);
            var chunked = RunOffline(committed, fx.Map, CardConfigs, committed.Run.LastProcessedUtcMs + ms, out _, 1);

            var skipDef = new StoreItemDef { Id = "skip_test", Kind = StoreItemKind.TimeSkip, Minutes = (int)minutes };
            var skipJob = new OfflineSimulator(fx.Map, CardConfigs).BeginSkip(committed, skipDef, committed.Run.LastProcessedUtcMs);
            skipJob.RunToEnd();
            var skip = skipJob.State;

            // Офлайн сдвигает только UTC-границу обработанного интервала; остальное — побайтно одинаково.
            var onlineState = Clone(online.State);
            onlineState.Run.LastProcessedUtcMs = offline.Run.LastProcessedUtcMs;
            AssertSameJson(Json(onlineState), Json(offline), "онлайн = офлайн");
            AssertSameJson(Json(offline), Json(chunked), "офлайн одним шагом = офлайн по одной отметке");
            skip.Run.LastProcessedUtcMs = offline.Run.LastProcessedUtcMs;
            AssertSameJson(Json(offline), Json(skip), "офлайн = тайм-скип");

            var run = offline.Run;
            var dairy = run.Factories.First(f => f.TargetId == FactoryA);
            var saw = run.Factories.First(f => f.TargetId == FactoryB);
            Assert.AreEqual((FactoryStatus.Ready, 1), (dairy.Status, dairy.Cycles), "§4.7: ручной завод — одна партия и ждёт");
            Assert.AreEqual(1 + (int)((run.Now - 15 * S) / Sawmill), saw.Cycles, "AUTO работает весь интервал");
            Assert.AreEqual(saw.Cycles * 22L, saw.Dispatched, "ca003 ур. 1: 22 единицы за цикл");
            Assert.AreEqual(saw.Dispatched, run.Warehouse["wood"] + run.PipeBatches.Where(b => b.GoodId == "wood").Sum(b => (long)b.Units));
            Assert.Greater(saw.Cycles, 190);
            Assert.AreEqual(run.TotalCredited - committed.Run.TotalCredited, summary.GoldDelivered, "сводка — только золото монет");
            var check = OutpostInvariants.Check(offline, fx.Map, CardConfigs.Equipment, CardConfigs.Cards);
            Assert.IsEmpty(check, string.Join("; ", check));
        }

        [Test]
        public void Offline3h_Mine140_FifteenAutoFactories_EqualsOnline()
        {
            // ТЗ 2.2 §4.7, §15: 15 автоматических заводов шахты 140 (топология текущего набора), 3 ч офлайна — то же ядро.
            var configs = OutpostConfigSet.LoadDefault();
            var state = StageProgressionService.NewGame(TestData.Balance, configs, Seed, StartUtcMs, 140);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, state.Run);
            var sim = new OutpostSimulation(state, map, configs);
            Assert.AreEqual(15, sim.Run.Factories.Count, "шахта 140: 15 заводов");
            Assert.AreEqual(15, sim.DebugUnlockFactories());
            Assert.AreEqual(15, sim.DebugAutomateAll());
            sim.Advance(30 * S);
            AssertCore(sim, "шахта 140");
            var committed = Clone(sim.State);
            committed.Run.LastProcessedUtcMs = StartUtcMs + 30_000;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var offline = RunOffline(committed, map, configs, committed.Run.LastProcessedUtcMs + OfflineCapMs, out var summary, 4096);
            long offlineMs = watch.ElapsedMilliseconds;
            Assert.AreEqual(OfflineCapMs, summary.AppliedMs);
            var online = new OutpostSimulation(Clone(committed), map, configs);
            online.Advance(committed.Run.Now + OfflineCapMs * 1000);
            var onlineState = Clone(online.State);
            onlineState.Run.LastProcessedUtcMs = offline.Run.LastProcessedUtcMs;
            AssertSameJson(Json(onlineState), Json(offline), "онлайн = офлайн, 3 ч, 15 заводов");
            var errors = OutpostInvariants.Check(offline, map, configs.Equipment, configs.Cards);
            Assert.IsEmpty(errors, string.Join("; ", errors.Take(10)));
            long cycles = offline.Run.Factories.Sum(f => (long)f.Cycles);
            TestContext.WriteLine("офлайн 3 ч, 15 заводов: " + offlineMs + " мс, циклов " + cycles + ", фаза " + offline.Run.Phase +
                ", товара на складе на " + FactoryQueries.StockValue(offline.Run) + " золота");
            Assert.Greater(cycles, 15L, "автоматические заводы работали весь интервал");
        }

        [Test]
        public void SaveLoad_MidCycle_InPipe_Ready_EqualsContinuous()
        {
            var committed = Committed(out var fx);
            long end = committed.Run.Now + 400 * S;

            var continuous = fx.Wrap(Clone(committed));
            continuous.Advance(end);

            var stepped = fx.Wrap(Clone(committed));
            var rng = new Random(20261015);
            int reloads = 0;
            while (stepped.Now < end)
            {
                long next = Math.Min(end, stepped.Now + 1 + rng.Next(2_500_000));
                stepped.Advance(next);
                string json = Json(stepped.State);
                stepped = fx.Load(json);
                AssertSameJson(json, Json(stepped.State), "загрузка снимка ядра ничего не меняет");
                reloads++;
                if (stepped.Run.PipeBatches.Count > 0) AssertCore(stepped, "партия в трубе после загрузки");
            }
            Assert.Greater(reloads, 100);
            AssertSameJson(Json(continuous.State), Json(stepped.State), "save/load посреди цикла и трубы без нового округления");

            // Посреди трубы и в Ready: дальнейшая доставка и продажа совпадают.
            Assert.IsTrue(continuous.Submit(OutpostCommand.DeliverFactory(FactoryA)).Ok);
            Assert.IsTrue(stepped.Submit(OutpostCommand.DeliverFactory(FactoryA)).Ok);
            continuous.Advance(end + 30 * S);
            stepped = fx.Load(Json(stepped.State));
            stepped.Advance(end + 30 * S);
            Assert.IsTrue(continuous.Submit(OutpostCommand.SellGoods(null)).Ok);
            Assert.IsTrue(stepped.Submit(OutpostCommand.SellGoods(null)).Ok);
            AssertSameJson(Json(continuous.State), Json(stepped.State), "команды после загрузки");
            AssertCore(stepped, "конец");
        }

        // ───────────────────────────── Карта и цепи ─────────────────────────────

        [Test]
        public void StandingFactory_FootprintStaysWall_AfterUnlockAndReload()
        {
            var fx = OneFactory(DairyNeeds1);
            var state = fx.NewState();
            ArmChains(state, FactoryA);
            var sim = fx.Wrap(state);
            var cells = sim.Map.Target(FactoryA).Cells;
            Assert.AreEqual(6, cells.Length, "footprint 2×3");
            Unlock(sim, FactoryA);
            foreach (int c in cells)
            {
                Assert.IsTrue(sim.IsOccupied(c), "§4.1: после открытия footprint занят зданием");
                Assert.IsFalse(sim.Navigation.Passable(c));
            }
            var reloaded = fx.Load(Json(sim.State));
            foreach (int c in cells) Assert.IsFalse(reloaded.Navigation.Passable(c), "и после загрузки");
            var events = new List<OutpostEvent>();
            RunToReady(reloaded, events);
            foreach (var w in reloaded.Run.Workers) CollectionAssert.DoesNotContain(cells, w.Cell);
            Assert.IsFalse(events.Any(e => (e.Type == OutpostEventType.CargoReleased || e.Type == OutpostEventType.CargoPickedUp) && cells.Contains(e.Cell)),
                "груз на footprint завода не лежит");
        }

        [Test]
        public void Chains_WithoutCargo_CatsBreakThem_NoCoins_MineCompletes()
        {
            // adapter-2 (WP2): у цепей TotalCargoUnits = 0. Ядро допускает цель без груза: котик бьёт до разрушения и не ждёт добычи.
            var fx = TwoFactories(DairyNeeds1, SawmillNeeds1);
            var state = fx.NewState();
            foreach (var t in state.Run.Targets.Where(t => t.Kind == 's')) t.TotalUnits = 0;
            var sim = fx.Wrap(state);
            var events = new List<OutpostEvent>();
            RunToReady(sim, events);
            foreach (int id in new[] { FactoryA, FactoryB })
            {
                Assert.IsTrue(events.Any(e => e.Type == OutpostEventType.TargetHit && e.TargetId == id), "котики бьют цепи сами");
                Assert.IsFalse(events.Any(e => e.Type == OutpostEventType.CargoReleased && e.TargetId == id), "ни одной порции монет из цепей");
                Assert.IsTrue(events.Any(e => e.Type == OutpostEventType.FactoryUnlocked && e.TargetId == id));
            }
            Assert.AreEqual(sim.Run.Counters.DeliveredValue, sim.Run.TotalCredited);
            Assert.IsTrue(sim.Run.Factories.All(f => f.Status == FactoryStatus.Stopped));
        }

        // ───────────────────────────── Инварианты ─────────────────────────────

        [Test]
        public void Invariants_DetectBrokenFactoryAccounting()
        {
            var committed = Committed(out var fx);
            var sim = fx.Wrap(Clone(committed));
            sim.Advance(sim.Now + 30 * S); // 45,5 с: партия Sawmill (45 с) в трубе, Dairy ждёт доставки
            Assert.AreEqual(1, sim.Run.PipeBatches.Count);
            Assert.IsTrue(sim.Submit(OutpostCommand.SellGoods(null)).Ok);
            AssertCore(sim, "исходный снимок");
            var good = sim.State;
            Assert.Greater(good.Run.GoodsSoldGold.Sign, 0);

            Expect(st => st.Run.Factories[0].Produced++, "Produced");
            Expect(st => st.Run.Factories[1].Dispatched++, "отправлено");
            Expect(st => st.Run.PipeBatches.RemoveAt(0), "отправлено");
            Expect(st => st.Run.Warehouse["wood"]++, "на складе");
            Expect(st => st.Run.GoodsArrived["wood"]++, "отправлено");
            Expect(st => st.Run.GoodsSold["wood"]++, "на складе");
            Expect(st => st.Run.DebugGoods["milk"] = 5, "DebugGoods вне debug-профиля");
            Expect(st => st.Run.GoodsSoldGold += 1, "Wallet");
            Expect(st =>
            {
                st.Run.GoodsSoldGold += 1;
                st.Run.Wallet += 1;
            }, "GoodsSoldGold");
            Expect(st => st.Run.Targets.First(t => t.Id == FactoryA).RemainingHpUnits = 5, "открыт при HP > 0");
            Expect(st => st.Run.Factories[0].Status = FactoryStatus.Locked, "Locked");
            Expect(st => st.Run.Factories[0].Automated = true, "автоматический завод ждёт доставки");
            Expect(st => st.Run.Factories[1].CycleEnd = st.Run.Now, "просроченный конец цикла");
            Expect(st => st.Run.Factories[1].CycleEnd += 1, "длительность цикла");
            Expect(st => st.Run.PipeBatches[0].ArriveTick = st.Run.Now, "просроченное прибытие");
            Expect(st => st.Run.PipeBatches[0].Seq = st.Run.NextPipeBatchSeq, "Seq");
            Expect(st => st.Run.Factories[1].Status = FactoryStatus.Stopped, "Stopped вне ReadyToContinue");
            Expect(st => st.Run.Factories.RemoveAt(1), "завод карты без состояния");

            // Отладочная выдача товара допустима только в debug-профиле.
            var dbg = fx.Wrap(Clone(good));
            Assert.AreEqual(2, dbg.DebugGrantGoods(100));
            Assert.AreEqual((100L, 100L), (dbg.Run.DebugGoods["milk"], dbg.Run.DebugGoods["wood"]));
            Assert.IsTrue(OutpostInvariants.Check(dbg.State, dbg.Map).Any(e => e.Contains("DebugGoods")));
            AssertCore(dbg, "debug-профиль", true);

            void Expect(Action<OutpostGameState> breakIt, string fragment)
            {
                var st = Clone(good);
                breakIt(st);
                var errors = OutpostInvariants.Check(st, fx.Map, CardConfigs.Equipment, CardConfigs.Cards);
                Assert.IsTrue(errors.Any(e => e.Contains(fragment)), "ожидалось нарушение «" + fragment + "»: " + string.Join("; ", errors));
            }
        }

        // ───────────────────────────── DEV-кнопки (ТЗ 2.2 §13) ─────────────────────────────

        [Test]
        public void Debug_UnlockFinishAuto_KeepInvariants()
        {
            var fx = TwoFactories(DairyNeeds1, SawmillNeeds1, CardConfigs);
            var state = fx.NewState();
            Endless(state, RockId);
            var sim = fx.Wrap(state);
            Drain(sim);
            Assert.AreEqual(2, sim.DebugUnlockFactories(), "«Unlock factories»");
            var ev = Drain(sim);
            Assert.AreEqual(2, ev.Count(e => e.Type == OutpostEventType.FactoryUnlocked));
            Assert.AreEqual(2, ev.Count(e => e.Type == OutpostEventType.ChestOpened), "с наградами");
            Assert.IsTrue(sim.Run.Factories.All(f => f.Status == FactoryStatus.Working));
            AssertCore(sim, "Unlock factories");
            Assert.AreEqual(0, sim.DebugUnlockFactories());

            sim.Advance(3 * S);
            Assert.AreEqual(2, sim.DebugFinishFactoryCycles(), "«Finish factory cycles»");
            Assert.IsTrue(sim.Run.Factories.All(f => f.Status == FactoryStatus.Ready && f.Cycles == 1));
            AssertCore(sim, "Finish factory cycles");

            Assert.AreEqual(2, sim.DebugAutomateAll(), "«Auto all»");
            Assert.IsTrue(sim.Run.Factories.All(f => f.Automated && f.Status == FactoryStatus.Working && f.CycleStart == sim.Now));
            Assert.AreEqual(2, sim.Run.PipeBatches.Count, "готовые партии ушли");
            Assert.AreEqual(0, sim.Profile.CardLevel("ca003"), "карточки не меняются");
            AssertCore(sim, "Auto all");
            sim.Advance(sim.Now + 2 * Sawmill);
            AssertCore(sim, "после Auto all");
        }
    }
}
