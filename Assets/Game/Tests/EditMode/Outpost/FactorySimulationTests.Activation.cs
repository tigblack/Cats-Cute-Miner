using System;
using System.Collections.Generic;
using System.Linq;
using CatMiners.Core.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    public partial class FactorySimulationTests
    {
        static OutpostSimulation ActivationMine(int zone = 2, string token = DairyNeeds1,
            Action<OutpostProfile> setup = null, bool endless = true)
        {
            var fx = OneFactory(token, CardConfigs, setup);
            var state = fx.NewState();
            // Синтетическая геометрия остаётся прежней; награды используют номер шахты run.
            state.Run.Zone = zone;
            state.Profile.CurrentZone = zone;
            state.Profile.HighestZone = zone;
            state.Profile.CardsVersion = 1;
            state.Profile.CardKeys.Add(CardService.TutorialKey);
            if (endless) Endless(state, RockId);
            ArmChains(state, FactoryA);
            return fx.Wrap(state);
        }

        static void BreakOnly(OutpostSimulation sim)
        {
            Assert.IsTrue(sim.Submit(OutpostCommand.Strike(FactoryA)).Ok);
            Assert.AreEqual(FactoryStatus.PendingActivation, sim.FactoryById(FactoryA).Status);
        }

        static int CopiesIn(OutpostEvent chest, string cardId) =>
            chest.Detail.Split(',').Where(x => x.StartsWith(cardId + ":", StringComparison.Ordinal))
                .Select(x => int.Parse(x.Substring(cardId.Length + 1))).Sum();

        [Test]
        public void Activation_ChainsWaitWithoutRewardsCyclesOrAutomation_CommandGrantsOneVisibleChest()
        {
            var sim = ActivationMine(setup: p => Card(p, "ca002", 1));
            Drain(sim);
            string before = Json(sim.State);
            Assert.AreEqual("locked", sim.ActivateBlockReason(FactoryA));
            Assert.AreEqual("locked", sim.Submit(OutpostCommand.ActivateFactory(FactoryA)).Reason);
            Assert.AreEqual("unknown", sim.ActivateBlockReason(RockId));
            Assert.AreEqual("unknown", sim.Submit(OutpostCommand.ActivateFactory(int.MaxValue)).Reason);
            AssertSameJson(before, Json(sim.State), "failed activation is read-only");
            string profile = OutpostJson.Serialize(sim.Profile);
            BreakOnly(sim);
            AssertSameJson(profile, OutpostJson.Serialize(sim.Profile), "chains grant no cards, elixir or RNG draws");
            var events = Drain(sim);
            Assert.AreEqual(1, events.Count(e => e.Type == OutpostEventType.FactoryUnlocked));
            Assert.IsFalse(events.Any(e => e.Type == OutpostEventType.ChestOpened || e.Type == OutpostEventType.FactoryAutomated));
            Assert.IsNull(sim.ActivateBlockReason(FactoryA));
            Assert.AreEqual("not-ready", sim.DeliverBlockReason(FactoryA));
            Assert.AreEqual(0, sim.DebugAutomateAll());
            Assert.AreEqual(0, sim.DebugFinishFactoryCycles());
            sim.Advance(50 * S);
            var f = sim.FactoryById(FactoryA);
            Assert.AreEqual((FactoryStatus.PendingActivation, false, -1L, -1L, 0, 0L),
                (f.Status, f.Automated, f.CycleStart, f.CycleEnd, f.Cycles, f.Produced));
            Drain(sim);
            long elixir = sim.Profile.Elixir;
            Assert.IsTrue(sim.Submit(OutpostCommand.ActivateFactory(FactoryA)).Ok);
            var granted = Drain(sim);
            var chest = granted.Single(e => e.Type == OutpostEventType.ChestOpened);
            Assert.AreEqual((FactoryA, Cell(sim.Map, 1, 3), "scg_m002_s01", 3, 1),
                (chest.TargetId, chest.Cell, chest.ItemId, chest.Count, chest.Extra));
            Assert.AreEqual("ca001:2,ca002:1", chest.Detail);
            Assert.AreEqual(33, (long)chest.Value);
            Assert.AreEqual((1, 3, 33L), (sim.Run.Counters.ChestsOpened, sim.Run.Counters.CardsGained, sim.Run.Counters.ElixirGained));
            Assert.AreEqual(elixir + 33, sim.Profile.Elixir);
            Assert.Less(granted.IndexOf(chest), granted.FindIndex(e => e.Type == OutpostEventType.FactoryAutomated));
            Assert.AreEqual((FactoryStatus.Working, true, sim.Now, sim.Now + Dairy), (f.Status, f.Automated, f.CycleStart, f.CycleEnd));
            Assert.Contains(CardService.FactorySeenKey("amethyst"), sim.Profile.CardKeys);
            AssertCore(sim, "activated");
        }

        [Test]
        public void Activation_ReloadBeforeAndAfter_AndRepeatedScheduledCommands_AwardExactlyOnce()
        {
            var sim = ActivationMine();
            BreakOnly(sim);
            string pending = Json(sim.State);
            sim = new OutpostSimulation(Clone(sim.State), sim.Map, CardConfigs);
            AssertSameJson(pending, Json(sim.State), "pending survives reload");
            Assert.IsFalse(sim.Profile.CardKeys.Contains(CardService.FactorySeenKey("amethyst")));
            sim.Schedule(OutpostCommand.ActivateFactory(FactoryA), 1);
            sim.Schedule(OutpostCommand.ActivateFactory(FactoryA), 1);
            sim.Advance(1);
            Assert.IsTrue(sim.ScheduledResults[0].Ok);
            Assert.AreEqual("already", sim.ScheduledResults[1].Reason);
            Assert.AreEqual(1, sim.Run.Counters.ChestsOpened);
            Assert.AreEqual(1, Drain(sim).Count(e => e.Type == OutpostEventType.ChestOpened));
            string active = Json(sim.State);
            sim = new OutpostSimulation(Clone(sim.State), sim.Map, CardConfigs);
            AssertSameJson(active, Json(sim.State), "activated reload neither grants nor restarts");
            Assert.AreEqual("already", sim.ActivateBlockReason(FactoryA));
            Assert.AreEqual("already", sim.Submit(OutpostCommand.ActivateFactory(FactoryA)).Reason);
            AssertSameJson(active, Json(sim.State), "repeat is read-only");
            Assert.IsEmpty(Drain(sim));
        }

        [Test]
        public void Activation_OfflinePending_DoesNotProduceAutomateOrClaimChest()
        {
            var sim = ActivationMine(setup: p => Card(p, "ca002", 3));
            BreakOnly(sim);
            var offline = new OfflineSimulator(sim.Map, CardConfigs).Begin(sim.State, StartUtcMs + 60_000);
            offline.RunToEnd();
            Assert.AreEqual((0, 0, 0L), (offline.Summary.ChestsOpened, offline.Summary.CardsGained, offline.Summary.ElixirGained));
            var loaded = new OutpostSimulation(offline.State, sim.Map, CardConfigs);
            var f = loaded.FactoryById(FactoryA);
            Assert.AreEqual((FactoryStatus.PendingActivation, false, 0, 0L), (f.Status, f.Automated, f.Cycles, f.Produced));
            Assert.IsEmpty(loaded.Run.PipeBatches);
            Assert.IsTrue(loaded.Submit(OutpostCommand.ActivateFactory(FactoryA)).Ok);
            Assert.AreEqual(loaded.Now, f.CycleStart);
            AssertCore(loaded, "offline pending then activation");
        }

        [Test]
        public void Activation_MineCompletesWhilePending_ChestStillClaimableAfterReload_ThenStopped()
        {
            var sim = ActivationMine(endless: false);
            BreakOnly(sim);
            RunToReady(sim);
            Assert.AreEqual(FactoryStatus.PendingActivation, sim.FactoryById(FactoryA).Status);
            sim = new OutpostSimulation(Clone(sim.State), sim.Map, CardConfigs);
            int opened = sim.Run.Counters.ChestsOpened;
            Drain(sim);
            Assert.IsNull(sim.ActivateBlockReason(FactoryA));
            Assert.IsTrue(sim.Submit(OutpostCommand.ActivateFactory(FactoryA)).Ok);
            var f = sim.FactoryById(FactoryA);
            Assert.AreEqual((FactoryStatus.Stopped, -1L, -1L, 0, 0L), (f.Status, f.CycleStart, f.CycleEnd, f.Cycles, f.Produced));
            Assert.AreEqual(opened + 1, sim.Run.Counters.ChestsOpened);
            Assert.AreEqual(1, Drain(sim).Count(e => e.Type == OutpostEventType.ChestOpened));
            Assert.AreEqual(long.MaxValue, sim.NextEventTick());
            sim.Advance(sim.Now + 100 * S);
            Assert.AreEqual(0, f.Cycles);
            Assert.IsEmpty(sim.Run.PipeBatches);
            Assert.AreEqual("already", sim.Submit(OutpostCommand.ActivateFactory(FactoryA)).Reason);
            AssertCore(sim, "claim after completion");
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        [TestCase(3, false)]
        public void Activation_LegacyNumericStatusesAndAbsentHistory_StayActivatedWithoutAnotherChest(int status, bool nullHistory)
        {
            Assert.AreEqual(4, (int)FactoryStatus.PendingActivation);
            Assert.AreEqual(13, (int)CommandKind.ActivateFactory);
            var sim = ActivationMine();
            BreakOnly(sim);
            Assert.IsTrue(sim.Submit(OutpostCommand.ActivateFactory(FactoryA)).Ok);
            var json = JObject.Parse(Json(sim.State));
            json["Run"]["Factories"][0]["Status"] = status;
            if (nullHistory) json["Profile"]["CardKeys"] = null;
            else ((JObject)json["Profile"]).Remove("CardKeys");
            var state = FromJson(json.ToString());
            var f = state.Run.Factories.Single();
            f.Automated = false;
            state.Profile.Cards.Remove("ca002");
            if (status == 2) { f.ReadyUnits = 20; f.Produced = 20; f.Cycles = 1; }
            if (status != 1) f.CycleStart = f.CycleEnd = -1;
            if (status == 3) state.Run.Phase = RunPhase.ReadyToContinue;
            int opened = state.Run.Counters.ChestsOpened;
            long elixir = state.Profile.Elixir;
            var loaded = new OutpostSimulation(state, sim.Map, CardConfigs);
            Assert.AreEqual((FactoryStatus)status, f.Status);
            Assert.AreEqual("already", loaded.Submit(OutpostCommand.ActivateFactory(FactoryA)).Reason);
            Assert.AreEqual((opened, elixir), (loaded.Run.Counters.ChestsOpened, loaded.Profile.Elixir));
            Assert.Contains(CardService.FactorySeenKey("amethyst"), loaded.Profile.CardKeys);
            Assert.IsFalse(Drain(loaded).Any(e => e.Type == OutpostEventType.ChestOpened));
        }

        [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void Activation_RealMines2To7_EveryMappedRequirementGetsEnoughRemainingCopies(int zone)
        {
            var configs = Configs.WithCards(TestData.Balance);
            var state = StageProgressionService.NewGame(TestData.Balance, configs, Seed, StartUtcMs, zone);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, state.Run);
            Assert.IsTrue(ProgressionService.Active(state.Profile, configs), "production first-session rules are active");
            Assert.IsFalse(ProgressionService.CardsHeld(state.Profile, configs, zone));
            var sim = new OutpostSimulation(state, map, configs);
            Assert.Greater(sim.DebugUnlockFactories(), 0);
            foreach (var mf in map.Factories)
            {
                var def = configs.Cards.Get(mf.RequiredCardId);
                var before = CardService.ProgressOf(sim.Profile, def.Id);
                int level = before?.Level ?? 0;
                long required = level == 0 ? 1 : 0;
                for (int l = Math.Max(1, level); l < mf.RequiredCardLevel; l++) required += def.UpgradeCopies[l - 1];
                required = Math.Max(0, required - (before?.Copies ?? 0));
                Drain(sim);
                Assert.IsTrue(sim.Submit(OutpostCommand.ActivateFactory(mf.TargetId)).Ok);
                var chest = Drain(sim).Single(e => e.Type == OutpostEventType.ChestOpened);
                Assert.GreaterOrEqual(CopiesIn(chest, def.Id), required, "mapped requirement " + mf.RequiredCardId + " L" + mf.RequiredCardLevel);
                sim.Profile.Elixir = 1_000_000;
                while (sim.Profile.CardLevel(def.Id) < mf.RequiredCardLevel)
                    Assert.IsTrue(sim.Submit(OutpostCommand.UpgradeCard(def.Id)).Ok);
                Assert.IsTrue(sim.FactoryById(mf.TargetId).Automated);
            }
            AssertCore(sim, "mine " + zone);
        }

        [TestCase(7, 18)] [TestCase(8, 14)] [TestCase(10, 14)]
        [TestCase(11, 9)] [TestCase(15, 9)] [TestCase(16, 5)] [TestCase(140, 5)]
        public void Activation_AssistanceTiers_UseMappedCardAndCumulativeCopies(int zone, int expected)
        {
            // Uncommon ca014, rather than the type's Common ca002: 1 + 2 + 5 + 10 = 18 copies to level 4.
            var sim = ActivationMine(zone, "s:amethyst:1:scg_m002_s01:ca014:4");
            BreakOnly(sim);
            Drain(sim);
            Assert.IsTrue(sim.Submit(OutpostCommand.ActivateFactory(FactoryA)).Ok);
            var chest = Drain(sim).Single(e => e.Type == OutpostEventType.ChestOpened);
            Assert.AreEqual(expected, CopiesIn(chest, "ca014"));
            Assert.AreEqual(1, sim.Profile.CardLevel("ca014"));
            Assert.AreEqual(expected - 1, sim.Profile.Cards["ca014"].Copies);
            Assert.AreEqual(0, sim.Profile.CardLevel("ca002"));
            Assert.IsFalse(sim.FactoryById(FactoryA).Automated, "copies are granted; upgrades remain explicit");
        }

        [TestCase(7, 14)] [TestCase(8, 11)] [TestCase(11, 7)] [TestCase(16, 4)]
        public void Activation_AssistanceSubtractsSpentAndHeldCopies(int zone, int expected)
        {
            var sim = ActivationMine(zone, "s:amethyst:1:scg_m002_s01:ca002:4", p => Card(p, "ca002", 2, 1));
            BreakOnly(sim);
            Drain(sim);
            Assert.IsTrue(sim.Submit(OutpostCommand.ActivateFactory(FactoryA)).Ok);
            Assert.AreEqual(expected, CopiesIn(Drain(sim).Single(e => e.Type == OutpostEventType.ChestOpened), "ca002"));
            Assert.AreEqual(2, sim.Profile.CardLevel("ca002"));
            Assert.AreEqual(1 + expected, sim.Profile.Cards["ca002"].Copies);
        }

        [Test]
        public void Activation_FirstTypeEvenIfOwned_LaterSameTypeAfterReloadGetsNoExtraMinimum()
        {
            var fx = TwoFactories(DairyNeeds1, DairyNeeds1, CardConfigs, p => Card(p, "ca002", 14));
            var state = fx.NewState();
            state.Run.Zone = 16;
            state.Profile.CurrentZone = state.Profile.HighestZone = 16;
            state.Profile.CardKeys = null; // Legacy optional field.
            Endless(state, RockId);
            ArmChains(state, FactoryA);
            ArmChains(state, FactoryB);
            var sim = fx.Wrap(state);
            Unlock(sim, FactoryA);
            Assert.AreEqual(1, CopiesIn(Drain(sim).Single(e => e.Type == OutpostEventType.ChestOpened), "ca002"));
            sim = fx.Load(Json(sim.State));
            Unlock(sim, FactoryB);
            var chest = Drain(sim).Single(e => e.Type == OutpostEventType.ChestOpened);
            Assert.AreEqual(0, CopiesIn(chest, "ca002"));
            Assert.AreEqual(2, chest.Count, "normal chest cards remain");
            Assert.AreEqual(1, sim.Profile.Cards["ca002"].Copies);
            Assert.AreEqual(1, sim.Profile.CardKeys.Count(x => x == CardService.FactorySeenKey("amethyst")));
        }

        [Test]
        public void Activation_MissingRewardId_UsesExistingChestWithCardsAndElixir()
        {
            var sim = ActivationMine(16, "s:amethyst:1::ca002:1");
            BreakOnly(sim);
            Drain(sim);
            Assert.IsTrue(sim.Submit(OutpostCommand.ActivateFactory(FactoryA)).Ok);
            var chest = Drain(sim).Single(e => e.Type == OutpostEventType.ChestOpened);
            Assert.AreEqual("GachaWooden", chest.ItemId);
            Assert.Greater(chest.Count, 0);
            Assert.Greater((long)chest.Value, 0);
            Assert.AreEqual(1, sim.Run.Counters.ChestsOpened);
        }

        [Test]
        public void Activation_SaveServiceAcceptsPendingAndActivated_WithoutSchemaReset()
        {
            var sim = ActivationMine();
            BreakOnly(sim);
            var fs = new MemoryFileSystemA();
            var map = sim.Map;
            var saves = new OutpostSaveService("/factory-activation-test", fs)
            {
                Configs = CardConfigs,
                MapResolver = _ => map,
            };
            Assert.IsTrue(saves.Save(sim.State, OutpostSaveSlot.Main, map).Ok);
            var pending = saves.Load(OutpostSaveSlot.Main);
            Assert.AreEqual(OutpostLoadStatus.Loaded, pending.Status, string.Join("; ", pending.Messages));
            AssertSameJson(Json(sim.State), Json(pending.State), "save service preserves pending snapshot");
            sim = new OutpostSimulation(pending.State, map, CardConfigs);
            Assert.IsTrue(sim.Submit(OutpostCommand.ActivateFactory(FactoryA)).Ok);
            Assert.IsTrue(saves.Save(sim.State, OutpostSaveSlot.Main, map).Ok);
            var active = saves.Load(OutpostSaveSlot.Main);
            Assert.AreEqual(OutpostLoadStatus.Loaded, active.Status, string.Join("; ", active.Messages));
            AssertSameJson(Json(sim.State), Json(active.State), "save service preserves activation receipt in status");
            sim = new OutpostSimulation(active.State, map, CardConfigs);
            Assert.AreEqual("already", sim.Submit(OutpostCommand.ActivateFactory(FactoryA)).Reason);
            Assert.AreEqual(1, sim.Run.Counters.ChestsOpened);
        }

        [Test]
        public void Activation_ChestMinimumMergesWithNormalCards_NewCardsTotalsAndRngStayAccurate()
        {
            var a = StageProgressionService.NewProfile(CardConfigs, Seed, 140);
            var b = Clone(new OutpostGameState { Profile = a }).Profile;
            var chest = CardConfigs.Cards.Chest("GachaIron");
            var normal = CardChestService.Open(a, CardConfigs.Cards, chest, 7, null);
            var assisted = CardChestService.Open(b, CardConfigs.Cards, chest, 7, null, "ca002", 1000);
            int ordinaryOwn = normal.Cards.Where(g => g.CardId == "ca002").Sum(g => g.Copies);
            Assert.AreEqual(1000, assisted.Cards.Single(g => g.CardId == "ca002").Copies);
            Assert.AreEqual(normal.TotalCopies + 1000 - ordinaryOwn, assisted.TotalCopies);
            Assert.AreEqual(assisted.Cards.Sum(g => g.Copies), assisted.TotalCopies);
            Assert.AreEqual(normal.Elixir, assisted.Elixir);
            Assert.AreEqual(OutpostJson.Serialize(a.CardRng), OutpostJson.Serialize(b.CardRng), "guarantee consumes no random draws");
            CollectionAssert.AreEquivalent(normal.Cards.Where(g => g.CardId != "ca002").Select(g => g.CardId + ":" + g.Copies),
                assisted.Cards.Where(g => g.CardId != "ca002").Select(g => g.CardId + ":" + g.Copies));
            CollectionAssert.AreEquivalent(normal.NewCards.Union(new[] { "ca002" }), assisted.NewCards);
        }

        [Test]
        public void Activation_InvariantsRejectPendingAutomationProductionAndCycles()
        {
            var sim = ActivationMine();
            BreakOnly(sim);
            AssertCore(sim, "valid pending");
            foreach (Action<FactoryState> corrupt in new Action<FactoryState>[]
                { f => f.Automated = true, f => f.CycleStart = 0, f => f.CycleEnd = 8 * S, f => f.Produced = 1, f => f.Cycles = 1 })
            {
                var state = Clone(sim.State);
                corrupt(state.Run.Factories.Single());
                Assert.IsTrue(OutpostInvariants.Check(state, sim.Map, CardConfigs.Equipment, CardConfigs.Cards)
                    .Any(e => e.Contains("PendingActivation")));
            }
        }
    }
}
