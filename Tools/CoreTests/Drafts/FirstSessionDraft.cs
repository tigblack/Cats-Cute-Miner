using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Черновые замеры первой сессии (Docs/Rework/FIRST_SESSION.md): раскладка клетки Mia в шахте 1 и время спасения/шахты по
    /// моделям игрока аудита. Не в Assets — Unity его не компилирует: Tools/CoreTests/run.sh -d FirstSessionDraft.cs.
    /// </summary>
    public class FirstSessionDraft
    {
        static OutpostConfigSet Configs => OutpostConfigSet.LoadDefault().WithCards(TestData.Balance);

        [Test]
        public void Zone1_RescueLayout()
        {
            var configs = Configs;
            var map = OutpostLevelAdapter.BuildMine(TestData.Balance, 1, configs);
            var rescue = map.Rescue;
            Assert.IsNotNull(rescue, "клетка Mia размещена");
            int cage = map.RescueCageCell;
            var lockT = map.Target(rescue.LockTargetId);
            var route = rescue.RouteTargetIds.ToHashSet();
            var sb = new StringBuilder();
            sb.AppendLine("zone1 " + map.Width + "x" + map.Depth + " cage=(" + map.Col(cage) + "," + map.Row(cage) + ") lock=(" + lockT.Column + "," + lockT.Row +
                          ") route=" + rescue.RouteTargetIds.Count + " exit=(" + map.Col(map.ExitCell) + "," + map.Row(map.ExitCell) + ")");
            for (int row = 0; row < map.Rows; row++)
            {
                for (int col = 0; col < map.Width; col++)
                {
                    int c = map.Cell(col, row);
                    char ch;
                    if (c == cage) ch = 'M';
                    else if (lockT.Cells.Contains(c)) ch = 'L';
                    else if (c == map.ExitCell) ch = 'E';
                    else if (map.IsProp(c)) ch = 'P';
                    else if (!map.IsPlayable(c)) ch = '#';
                    else
                    {
                        int id = map.InitialOccupant[c];
                        if (id < 0) ch = '.';
                        else if (route.Contains(id)) ch = '+';
                        else ch = map.Target(id).Piece != null ? (map.Target(id).Piece.IsOre ? '*' : 'd') : 'o';
                    }
                    sb.Append(ch);
                }
                sb.AppendLine();
            }
            TestContext.WriteLine(sb.ToString());
        }

        [TestCase("active")]
        [TestCase("free")]
        [TestCase("idle")]
        [TestCase("eager-rr")]
        public void Zone1_RescueTiming(string modelName)
        {
            var configs = Configs;
            var state = StageProgressionService.NewGame(TestData.Balance, configs, 1, 0, 1);
            var map = OutpostLevelAdapter.BuildMine(TestData.Balance, 1, configs);
            var sim = new OutpostSimulation(state, map, configs);
            var model = BalanceAudit.Model(modelName);
            var report = BalanceAudit.RunMine(sim, model, modelName);
            TestContext.WriteLine(modelName + ": outcome=" + report.Outcome + " rescue=" + Fmt(report.RescueSec) + " completed=" + Fmt(report.CompletedSec) +
                                  " firstDelivery=" + Fmt(report.FirstDeliverySec) + " firstPurchase=" + Fmt(report.FirstPurchaseSec) +
                                  " firstHire=" + Fmt(report.FirstHireSec) + " purchases=" + report.Purchases + " levels=" + string.Join(",", report.Levels ?? new int[0]) +
                                  " workers=" + report.WorkersEnd + " unlocked=" + string.Join(",", sim.Profile.Progression.Unlocked) +
                                  " crew=" + string.Join(",", sim.Profile.Progression.Crew));
            Assert.AreEqual("Completed", report.Outcome);
        }

        [Test]
        public void Zone1_RescueFocusSweep()
        {
            foreach (bool clear in new[] { false, true })
            foreach (int focus in new[] { 0, 8, 16, 30 })
                foreach (string modelName in new[] { "free", "active", "idle" })
                {
                    var sb = new StringBuilder();
                    sb.Append("clear=" + clear + " focus=" + focus + " " + modelName + ":");
                    foreach (ulong seed in new ulong[] { 1 })
                    {
                        var p = OutpostJson.DeepCopy(OutpostConfigSet.LoadDefault().Progression);
                        p.Rescue.RouteFocus = focus;
                        p.Rescue.ClearRouteOre = clear;
                        var configs = Configs.WithProgression(p);
                        var state = StageProgressionService.NewGame(TestData.Balance, configs, seed, 0, 1);
                        var map = OutpostLevelAdapter.BuildMine(TestData.Balance, 1, configs);
                        var sim = new OutpostSimulation(state, map, configs);
                        var report = BalanceAudit.RunMine(sim, BalanceAudit.Model(modelName), modelName);
                        sb.Append(" ").Append(Fmt(report.RescueSec)).Append("/").Append(Fmt(report.CompletedSec)).Append(" route=").Append(map.Rescue.RouteTargetIds.Count);
                    }
                    TestContext.WriteLine(sb.ToString());
                }
        }

        [Test]
        public void Zones1To6_Campaign()
        {
            var configs = Configs;
            var model = BalanceAudit.Model("free");
            var audit = BalanceAudit.RunCampaign(TestData.Balance, configs, model, 1, 8);
            foreach (var m in audit.Mines)
                TestContext.WriteLine("zone " + m.Zone + ": " + m.Outcome + " completed=" + Fmt(m.CompletedSec) + " rescue=" + Fmt(m.RescueSec) +
                                      " purchases=" + m.Purchases + " levels=" + string.Join(",", m.Levels ?? new int[0]) + " workers=" + m.WorkersEnd);
        }

        [Test]
        public void Zones1To6_Unlocks()
        {
            var configs = Configs;
            var model = BalanceAudit.Model("active");
            var audit = BalanceAudit.RunCampaign(TestData.Balance, configs, model, 1, 7, beforeZone: (zone, state) =>
            {
                var p = state.Profile;
                var pr = p.Progression;
                int cards = 0;
                if (p.Cards != null)
                    foreach (var kv in p.Cards)
                        if (kv.Value != null && kv.Value.Level >= 1) cards++;
                TestContext.WriteLine("zone " + zone + " start: unlocked=[" + string.Join(",", pr.Unlocked) + "] shown=[" + string.Join(",", pr.Shown) +
                                      "] done=[" + string.Join(",", pr.Done) + "] grants=[" + string.Join(",", pr.Grants) + "] crew=[" + string.Join(",", pr.Crew) +
                                      "] cards=" + cards + " elixir=" + p.Elixir + " items=" + (p.Items?.Count ?? 0) + " parts=" + p.Parts);
            });
            foreach (var m in audit.Mines)
                TestContext.WriteLine("zone " + m.Zone + ": " + m.Outcome + " completed=" + Fmt(m.CompletedSec) + " rescue=" + Fmt(m.RescueSec) +
                                      " purchases=" + m.Purchases + " levels=" + string.Join(",", m.Levels ?? new int[0]));
        }

        [Test]
        public void Zone1_ChestEvents()
        {
            var configs = Configs;
            var state = StageProgressionService.NewGame(TestData.Balance, configs, 1, 0, 1);
            var map = OutpostLevelAdapter.BuildMine(TestData.Balance, 1, configs);
            var sim = new OutpostSimulation(state, map, configs);
            long end = (long)(3600 * OutpostIds.TicksPerSecond);
            while (sim.Run.Phase != RunPhase.ReadyToContinue && sim.Now < end)
            {
                sim.Advance(sim.Now + OutpostIds.TicksPerSecond * 5);
                foreach (var e in sim.Events)
                    if (e.Type == OutpostEventType.ChestOpened || e.Type == OutpostEventType.FeatureUnlocked || e.Type == OutpostEventType.ElixirFound)
                        TestContext.WriteLine((e.Tick / (double)OutpostIds.TicksPerSecond).ToString("0") + "s " + e.Type + " item=" + e.ItemId + " target=" + e.TargetId +
                                              " extra=" + e.Extra + " count=" + e.Count + " value=" + e.Value + " detail=" + e.Detail);
                sim.Events.Clear();
            }
            TestContext.WriteLine("phase=" + sim.Run.Phase + " at " + (sim.Now / (double)OutpostIds.TicksPerSecond).ToString("0") + "s unlocked=" + string.Join(",", state.Profile.Progression.Unlocked));
            foreach (var mt in map.Targets)
                if (mt.RewardId != null) TestContext.WriteLine("target " + mt.Id + " kind=" + mt.Kind + " reward=" + mt.RewardId + " gate=" + mt.IsGate);
        }

        [Test]
        public void Zone1_NonDebrisTargetCount()
        {
            var with = OutpostLevelAdapter.BuildMine(TestData.Balance, 1, Configs);
            var without = OutpostLevelAdapter.BuildMine(TestData.Balance, 1, Configs.WithProgression(null));
            TestContext.WriteLine("zone1 non-debris with rescue=" + with.Targets.Count(t => !t.IsDebris) + " without rescue=" + without.Targets.Count(t => !t.IsDebris) +
                                  " source=" + without.Targets.Count(t => t.Piece == null) + " all=" + without.Targets.Length + "/" + with.Targets.Length);
        }

        [Test]
        public void Topology_WorkersChooseTargets()
        {
            foreach (var withRules in new[] { true, false })
                foreach (int zone in new[] { 1, 72, 144 })
                {
                    var configs = withRules ? Configs : Configs.WithProgression(null);
                    var state = StageProgressionService.NewGame(TestData.Balance, configs, 7, 0, zone);
                    var map = OutpostLevelAdapter.ForRun(TestData.Balance, state.Run);
                    var sim = new OutpostSimulation(state, map, configs);
                    var bot = new OutpostBot(BotPolicy.C);
                    var worked = new HashSet<int>();
                    for (int i = 0; i < 40; i++)
                    {
                        sim.Advance(sim.Now + 5 * OutpostIds.TicksPerSecond);
                        bot.ApplyPolicy(sim);
                        foreach (var w in state.Run.Workers)
                            if (w.TargetId >= 0) worked.Add(w.TargetId);
                    }
                    TestContext.WriteLine("rules=" + withRules + " zone " + zone + ": worked=" + worked.Count + " workers=" + state.Run.Workers.Count +
                                          " delivered=" + state.Run.Counters.DeliveredUnits + " levels=" + string.Join(",", state.Run.Levels));
                }
        }

        static string Fmt(double? sec) => sec.HasValue ? (sec.Value / 60).ToString("0.0") + "m" : "-";
    }
}
