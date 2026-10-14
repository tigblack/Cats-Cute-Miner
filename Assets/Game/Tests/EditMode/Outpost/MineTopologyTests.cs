using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    public sealed class MineTopologyTests
    {
        static readonly OutpostConfigSet Configs = OutpostConfigSet.LoadDefault();
        static IEnumerable<int> Zones() => Enumerable.Range(1, 144);

        [TestCaseSource(nameof(Zones))]
        public void CampaignGeometry_SequentialClearance_PhysicalGates_StableEconomy(int zone)
        {
            var repo = TestData.Balance;
            var source = OutpostLevelAdapter.Build(repo, zone, Configs.Overrides);
            var map = OutpostLevelAdapter.BuildMine(repo, zone, Configs);
            var reach = OutpostReachability.Check(map);
            Assert.That(reach.Passed, Is.True, reach.Diagnostic);
            Assert.That(reach.Cleared, Is.EqualTo(map.Targets.Count(t => !t.IsGate)));
            Assert.That(reach.GatesOpened, Is.EqualTo(map.Gates.Length));
            Assert.That(map.Topology, Is.Not.Null);
            var before = OutpostLevelBalanceAdapter.Compute(source, Configs.Balance, repo);
            var after = OutpostLevelBalanceAdapter.Compute(map, Configs.Balance, repo);
            for (int i = 0; i < before.Length; i++)
            {
                var a = source.Targets[i];
                var b = map.Targets[i];
                Assert.That(b.Id, Is.EqualTo(a.Id));
                Assert.That(b.Kind, Is.EqualTo(a.Kind));
                Assert.That(b.TypeId, Is.EqualTo(a.TypeId));
                Assert.That(b.RewardId, Is.EqualTo(a.RewardId));
                Assert.That(b.Sector, Is.EqualTo(a.Sector));
                Assert.That(b.SourceLevel, Is.EqualTo(a.SourceLevel));
                Assert.That(after[i].MaxHp, Is.EqualTo(before[i].MaxHp));
                Assert.That(after[i].TotalCargoUnits, Is.EqualTo(before[i].TotalCargoUnits));
                Assert.That(after[i].ValueFactor, Is.EqualTo(before[i].ValueFactor));
                Assert.That(b.WorkCells.All(map.IsPlayable), Is.True);
                Assert.That(b.WorkCells.Intersect(b.Cells), Is.Empty);
            }
            var empty = Enumerable.Repeat(-1, map.CellCount).ToArray();
            var nav = new GridNavigation(map, empty);
            int mineCells = map.Width * map.Depth;
            int rock = Enumerable.Range(0, mineCells).Count(c => !map.IsPlayable(c));
            Assert.That(rock, Is.GreaterThan(mineCells / 5), "Structural rock survives all mining");
            for (int opened = 0; opened <= map.Gates.Length; opened++)
            {
                nav.SyncGates(opened, opened);
                var distances = nav.From(map.IntakeCell).Dist;
                for (int cell = 0; cell < map.CellCount; cell++)
                {
                    bool gateClosed = map.CellGateOrder[cell] >= opened;
                    if (!map.IsPlayable(cell) || gateClosed || map.SectorAt(cell) > opened)
                        Assert.That(distances[cell], Is.EqualTo(-1), "Gate bypass or walk through rock at " + cell);
                    else
                        Assert.That(distances[cell], Is.GreaterThanOrEqualTo(0), "Disconnected passage at " + cell);
                }
                if (opened < map.Gates.Length)
                {
                    var gate = map.Target(map.Gates[opened].TargetId);
                    Assert.That(gate.WorkCells.Any(c => distances[c] >= 0), Is.True);
                }
            }
            // A permanent interior rock band must separate paths with playable
            // cells on both sides, beyond merely changing the outer contour.
            bool interiorWall = false;
            for (int r = 0; r < map.Depth; r++)
            {
                int first = -1, last = -1;
                for (int c = 0; c < map.Width; c++)
                    if (map.IsPlayable(map.Cell(c, r))) { if (first < 0) first = c; last = c; }
                for (int c = first + 1; first >= 0 && c < last; c++)
                    if (!map.IsPlayable(map.Cell(c, r))) interiorWall = true;
            }
            Assert.That(interiorWall, Is.True);
        }

        [Test]
        public void VersionOneSavedMine_RetainsItsSnapshot_AfterWideCatalogUpdate()
        {
            string path = Path.Combine(TestData.ProjectRoot, "Assets", "Game", "Tests", "Fixtures", "Levels", "mine_topology_v1_zone1.json");
            var old = OutpostJson.Deserialize<MineTopology>(File.ReadAllText(path));
            old.Validate();
            var oldCatalog = new MineTopologyCatalog { Version = MineTopologyCatalog.LegacyVersion, SourceSha256 = Configs.Topologies.SourceSha256 };
            oldCatalog.Zones.Add("zone1", old);
            var state = StageProgressionService.NewGame(TestData.Balance, Configs.WithTopologies(oldCatalog), 71, 0, 1);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, state.Run);
            var sim = new OutpostSimulation(state, map, Configs);
            sim.Advance(20 * OutpostIds.TicksPerSecond);
            string payload = OutpostSaveService.SerializeState(state);
            var restored = OutpostSaveService.DeserializeState(payload);
            var same = OutpostLevelAdapter.ForRun(TestData.Balance, restored.Run);
            Assert.That(same.Width, Is.EqualTo(13));
            Assert.That(same.Topology.Version, Is.EqualTo(MineTopologyCatalog.LegacyVersion));
            Assert.That(OutpostJson.Serialize(same.Topology), Is.EqualTo(OutpostJson.Serialize(old)));
            Assert.That(OutpostSaveService.SerializeState(restored), Is.EqualTo(payload));
            Assert.That(OutpostLevelAdapter.BuildMine(TestData.Balance, 1, Configs).Width, Is.GreaterThanOrEqualTo(25));
        }

        [TestCase(1)]
        [TestCase(72)]
        [TestCase(144)]
        public void SaveReload_ContinuesIdenticalRoutesCargoAndRewards(int zone)
        {
            var state = StageProgressionService.NewGame(TestData.Balance, Configs.WithCards(TestData.Balance), 31337, 0, zone);
            var configs = Configs.WithCards(TestData.Balance);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, state.Run);
            var sim = new OutpostSimulation(state, map, configs);
            sim.Advance(90 * OutpostIds.TicksPerSecond);
            var copy = OutpostJson.DeepCopy(state);
            var restoredMap = OutpostLevelAdapter.ForRun(TestData.Balance, copy.Run);
            var restored = new OutpostSimulation(copy, restoredMap, configs);
            Assert.That(OutpostJson.Serialize(map.Topology), Is.EqualTo(OutpostJson.Serialize(restoredMap.Topology)));
            sim.Advance(sim.Now + 300 * OutpostIds.TicksPerSecond);
            restored.Advance(restored.Now + 300 * OutpostIds.TicksPerSecond);
            Assert.That(OutpostJson.Serialize(restored.State), Is.EqualTo(OutpostJson.Serialize(sim.State)));
            Assert.That(OutpostInvariants.Check(restored.State, restoredMap, configs.Equipment, configs.Cards), Is.Empty);
        }

        [Test]
        public void OldActiveSave_KeepsItsGeometry_NewMineUsesBranchingSnapshot()
        {
            var legacy = Configs.WithTopologies(null);
            var state = StageProgressionService.NewGame(TestData.Balance, legacy, 77, 0, 1);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, state.Run);
            Assert.That(map.Topology, Is.Null);
            var sim = new OutpostSimulation(state, map, Configs);
            sim.Advance(20 * OutpostIds.TicksPerSecond);
            string saved = OutpostSaveService.SerializeState(state);
            var restored = OutpostSaveService.DeserializeState(saved);
            var same = OutpostLevelAdapter.ForRun(TestData.Balance, restored.Run);
            Assert.That(same.Width, Is.EqualTo(map.Width));
            CollectionAssert.AreEqual(same.InitialOccupant, map.InitialOccupant);
            Assert.That(OutpostSaveService.SerializeState(restored), Is.EqualTo(saved));
            var finished = new OutpostBot(BotPolicy.C).Run(sim);
            Assert.That(finished.Success, Is.True, finished.Error ?? finished.WatchdogDiagnostic);
            var transition = StageProgressionService.PrepareTransition(state, TestData.Balance, Configs, 0);
            Assert.That(transition.Ok, Is.True);
            Assert.That(transition.Map.Topology, Is.Not.Null);
            Assert.That(transition.State.Run.MapTopology, Is.Not.Null);
            Assert.That(transition.State.Profile.RewardLog.Count, Is.EqualTo(1));
        }

        [Test]
        public void All144Mines_AutomaticallyMineAndDeliver_CompareTravelTimeWithLegacy()
        {
            var legacy = Configs.WithTopologies(null);
            var rows = new List<object>();
            double oldTotal = 0, newTotal = 0, maximum = 0;
            for (int zone = 1; zone <= 144; zone++)
            {
                var old = OutpostBot.RunZone(TestData.Balance, legacy, zone, BotPolicy.A, 31337);
                var current = OutpostBot.RunZone(TestData.Balance, Configs, zone, BotPolicy.A, 31337);
                Assert.That(old.Success, Is.True, old.ToString() + " " + old.Error);
                Assert.That(current.Success, Is.True, current.ToString() + " " + current.Error + " " + current.WatchdogDiagnostic);
                Assert.That(current.Credited, Is.EqualTo(old.Credited), "Unchanged economy, zone" + zone);
                Assert.That(current.UnitsCreated, Is.EqualTo(old.UnitsCreated));
                double ratio = current.CompletedSec.Value / old.CompletedSec.Value;
                oldTotal += old.CompletedSec.Value; newTotal += current.CompletedSec.Value;
                maximum = Math.Max(maximum, ratio);
                rows.Add(new { zone, legacySeconds = old.CompletedSec, branchingSeconds = current.CompletedSec, ratio,
                    firstDeliverySeconds = current.FirstDeliverySec, cargoUnits = current.UnitsCreated, credited = current.Credited });
            }
            string dir = Path.Combine(TestData.ProjectRoot, "Docs", "Reports", "topology");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "travel-comparison.json"), OutpostJson.Serialize(new {
                sourceSha256 = TestData.Balance.Sha256, policy = "A", cards = false, equipment = "empty",
                mines = rows.Count, legacyTotalSeconds = oldTotal, branchingTotalSeconds = newTotal,
                totalRatio = newTotal / oldTotal, maximumRatio = maximum, rows }));
            TestContext.WriteLine("144 mines; total completion ratio=" + newTotal / oldTotal + "; maximum=" + maximum);
            Assert.That(newTotal / oldTotal, Is.LessThan(1.25), "Campaign hauling must not multiply completion times");
            Assert.That(maximum, Is.LessThan(1.5), "Individual mine hauling must remain bounded");
        }

        // b4: кампания бота C (без карточек и деталей) длиннее в логическом времени в ~4 раза (BALANCE_AUDIT.md), Mono Unity
        // проходит её за 10–20 минут реального времени.
        [Test, Timeout(1800000)]
        public void BranchingCampaign_CardsEquipmentAndUpgrades_CompleteAll144()
        {
            var configs = Configs.WithCards(TestData.Balance);
            var reports = OutpostBot.RunCampaign(TestData.Balance, configs, BotPolicy.C, 31337, 144);
            Assert.That(reports.Count, Is.EqualTo(144));
            Assert.That(reports.All(r => r.Success), Is.True,
                string.Join("; ", reports.Where(r => !r.Success).Select(r => r.ToString() + " " + r.Error)));
            string dir = Path.Combine(TestData.ProjectRoot, "Docs", "Reports", "topology");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "campaign-cards.json"), OutpostJson.Serialize(reports));
        }

        [TestCase(1)]
        [TestCase(10)]
        public void ManualStrikes_RespectReachabilityAndClosedGates_DropCollectableCargo(int zone)
        {
            var state = StageProgressionService.NewGame(TestData.Balance, Configs, 19, 0, zone);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, state.Run);
            var sim = new OutpostSimulation(state, map, Configs) { RecordEvents = false };
            bool struckGate = false;
            int strikes = 0;
            for (int step = 0; step < 50000 && !sim.IsEconomyStopped; step++)
            {
                foreach (var locked in map.Targets.Where(t => t.Sector > state.Run.GatesOpened))
                    Assert.That(sim.CanStrike(locked.Id), Is.False);
                var target = map.Targets.FirstOrDefault(t => sim.CanStrike(t.Id));
                if (target != null)
                {
                    var wallet = state.Run.Wallet;
                    Assert.That(sim.Submit(OutpostCommand.Strike(target.Id)).Ok, Is.True);
                    Assert.That(state.Run.Wallet, Is.EqualTo(wallet), "Only unloading credits gold");
                    strikes++;
                    struckGate |= target.IsGate;
                    var distances = sim.Navigation.From(map.IntakeCell).Dist;
                    foreach (var batch in state.Run.Batches)
                        Assert.That(distances[batch.Cell], Is.GreaterThanOrEqualTo(0), "Strike drop trapped behind structural rock");
                }
                sim.Advance(sim.Now + OutpostIds.TicksPerSecond / 4);
            }
            Assert.That(strikes, Is.GreaterThan(0));
            Assert.That(sim.IsEconomyStopped, Is.True);
            if (map.Gates.Length > 0) Assert.That(struckGate, Is.True);
            Assert.That(state.Run.Batches, Is.Empty);
            Assert.That(OutpostInvariants.Check(state, map, Configs.Equipment), Is.Empty);
        }

        [TestCase(1)]
        [TestCase(72)]
        public void ConcurrentWorkers_UseDifferentBranches_KeepValidPathsWhileMining(int zone)
        {
            var state = StageProgressionService.NewGame(TestData.Balance, Configs, 31337, 0, zone);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, state.Run);
            var sim = new OutpostSimulation(state, map, Configs) { RecordEvents = false };
            var bot = new OutpostBot(BotPolicy.C);
            bool splitBranches = false;
            var activeBranches = new HashSet<int>();
            for (int step = 0; step < 50000 && !sim.IsEconomyStopped; step++)
            {
                sim.Advance(sim.NextEventTick());
                bot.ApplyPolicy(sim);
                activeBranches.Clear();
                foreach (var worker in state.Run.Workers)
                {
                    if (worker.Path != null)
                        for (int i = worker.PathIndex; i < worker.Path.Length; i++)
                            Assert.That(sim.Navigation.Passable(worker.Path[i]), Is.True);
                    if (worker.Mode != WorkerMode.Mining) continue;
                    var target = map.Target(worker.TargetId);
                    Assert.That(target.WorkCells, Does.Contain(worker.WorkCell));
                    Assert.That(sim.Navigation.Distance(map.IntakeCell, worker.WorkCell), Is.GreaterThanOrEqualTo(0));
                    if (target.Branch > 0) activeBranches.Add(target.Branch);
                }
                splitBranches |= activeBranches.Count > 1;
            }
            Assert.That(sim.IsEconomyStopped, Is.True);
            Assert.That(splitBranches, Is.True, "Cats should concurrently mine in separate branches");
            Assert.That(state.Run.WatchdogFired, Is.False);
        }
    }
}
