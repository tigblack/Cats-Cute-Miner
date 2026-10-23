using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    public sealed class MineTopologyTests
    {
        // Геометрия кампании без обстановки шахт: мусор и препятствия проверяет MineDressingTests на выборке (Docs/Rework/MINE_DRESSING.md).
        static readonly OutpostConfigSet Configs = OutpostConfigSet.LoadDefault().WithDressing(null);
        static IEnumerable<int> Zones() => Enumerable.Range(1, 144);

        [TestCaseSource(nameof(Zones))]
        public void CampaignGeometry_SequentialClearance_PhysicalGates_StableEconomy(int zone)
        {
            var repo = TestData.Balance;
            var source = OutpostLevelAdapter.Build(repo, zone, Configs.Overrides);
            var map = OutpostLevelAdapter.BuildMine(repo, zone, Configs);
            // ТЗ 2.2 §4.1: оба случая валидатора со стоящими заводами — волновая расчистка и итоговая карта.
            var reach = OutpostReachability.Check(map);
            Assert.That(reach.Passed, Is.True, reach.Diagnostic);
            Assert.That(reach.WavesPassed && reach.FinalPassed, Is.True);
            Assert.That(reach.Cleared, Is.EqualTo(map.Targets.Count(t => !t.IsGate)));
            Assert.That(reach.GatesOpened, Is.EqualTo(map.Gates.Length));
            Assert.That(reach.FactoriesWithRoute, Is.EqualTo(map.Targets.Count(t => t.IsFactory)), "труба у каждого завода (ТЗ 2.2 §5)");
            Assert.That(map.Topology, Is.Not.Null);
            Assert.That(map.Topology.Version, Is.EqualTo(MineTopologyCatalog.SupportedVersion));
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
                // ТЗ 2.2 §4.2 (adapter-2): цепи завода монет не дают.
                if (b.IsFactory) Assert.That(after[i].TotalCargoUnits, Is.EqualTo(0), "zone" + zone + " #" + b.Id + ": груз цепей");
                else Assert.That(after[i].TotalCargoUnits, Is.GreaterThanOrEqualTo(2));
                Assert.That(after[i].ValueFactor, Is.EqualTo(before[i].ValueFactor));
                Assert.That(b.WorkCells.All(map.IsPlayable), Is.True);
                Assert.That(b.WorkCells.Intersect(b.Cells), Is.Empty);
            }
            // ТЗ 2.2 §4.1: r/x сняты, footprint заводов остаётся занят зданием (раньше — пустая занятость, все цели сняты).
            var standing = Enumerable.Repeat(-1, map.CellCount).ToArray();
            foreach (var t in map.Targets.Where(t => t.IsFactory))
                foreach (int cell in t.Cells) standing[cell] = t.Id;
            var nav = new GridNavigation(map, standing);
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
                    if (!map.IsPlayable(cell) || gateClosed || map.SectorAt(cell) > opened || standing[cell] >= 0)
                        Assert.That(distances[cell], Is.EqualTo(-1), "Gate bypass, walk through rock or a standing factory at " + cell);
                    else
                        Assert.That(distances[cell], Is.GreaterThanOrEqualTo(0), "Disconnected passage at " + cell + " (standing factories)");
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

        /// <summary>ТЗ 2.2 §4.1 и §10.1: каталог и все 144 снимка — wide-branching-mines-3; прежняя wide-версия не принимается.</summary>
        [Test]
        public void Catalog_WideBranchingMines3_AllSnapshots_LegacyStillSupported()
        {
            Assert.That(MineTopologyCatalog.SupportedVersion, Is.EqualTo("wide-branching-mines-3"));
            Assert.That(Configs.Topologies.Version, Is.EqualTo(MineTopologyCatalog.SupportedVersion));
            Assert.That(Configs.Topologies.SourceSha256, Is.EqualTo(TestData.Balance.Sha256));
            for (int z = 1; z <= 144; z++)
                Assert.That(Configs.Topologies.For("zone" + z).Version, Is.EqualTo(MineTopologyCatalog.SupportedVersion), "zone" + z);
            Assert.That(MineTopologyCatalog.SupportsSnapshot(MineTopologyCatalog.SupportedVersion), Is.True);
            Assert.That(MineTopologyCatalog.SupportsSnapshot(MineTopologyCatalog.LegacyVersion), Is.True, "MineTopologyMigration");
            Assert.That(MineTopologyCatalog.SupportsSnapshot("wide-branching-mines-2"), Is.False, "схема < 3 сбрасывается до построения карты (§10.3)");
            var old = OutpostJson.DeepCopy(Configs.Topologies.For("zone2"));
            old.Version = "wide-branching-mines-2";
            Assert.Throws<ArgumentException>(() => old.Validate());
        }

        /// <summary>
        /// ТЗ 2.2 §4.1, §5, §16.1: валидатор 144/144 со стоящими заводами (оба случая), маршруты всех 1 232 заводов существуют и
        /// детерминированы, выход трубы и длина маршрута совпадают с независимым BFS по итоговой карте.
        /// </summary>
        [Test]
        public void StandingFactories_All144Mines_PassBothCases_AllPipeRoutesDeterministic()
        {
            int factories = 0, passed = 0;
            for (int z = 1; z <= 144; z++)
            {
                var map = OutpostLevelAdapter.BuildMine(TestData.Balance, z, Configs);
                var again = OutpostLevelAdapter.BuildMine(TestData.Balance, z, Configs);
                var report = OutpostReachability.Check(map);
                Assert.That(report.Passed, Is.True, report.Diagnostic);
                passed++;
                Assert.That(report.Factories, Is.EqualTo(map.Targets.Count(t => t.IsFactory)), "zone" + z);
                Assert.That(map.Factories.Select(f => f.TargetId), Is.Ordered, "zone" + z + ": по TargetId");
                var walls = new HashSet<int>(map.Targets.Where(t => t.IsFactory).SelectMany(t => t.Cells));
                var dist = IndependentDistances(map, walls);
                for (int i = 0; i < map.Factories.Length; i++)
                {
                    var f = map.Factories[i];
                    var g = again.Factories[i];
                    string at = "zone" + z + " #" + f.TargetId;
                    Assert.That(f.HasRoute, Is.True, at + ": труба до IntakeCell");
                    Assert.That((g.TargetId, g.OutletCell, g.RouteSteps), Is.EqualTo((f.TargetId, f.OutletCell, f.RouteSteps)), at + ": детерминизм");
                    CollectionAssert.AreEqual(f.RouteCells, g.RouteCells, at + ": детерминизм клеток");
                    var target = map.Target(f.TargetId);
                    Assert.That((f.TypeId, f.RequiredCardId, f.RequiredCardLevel), Is.EqualTo((target.TypeId, target.CardId, target.CardLevel)), at);
                    // Выход — свободная соседняя клетка с наименьшим расстоянием до IntakeCell, при равенстве — меньший индекс.
                    var own = new HashSet<int>(target.Cells);
                    var outlet = target.Cells.SelectMany(c => Around(map, c)).Where(c => !own.Contains(c) && dist[c] >= 0)
                        .OrderBy(c => dist[c]).ThenBy(c => c).First();
                    Assert.That(f.OutletCell, Is.EqualTo(outlet), at + ": клетка выхода");
                    Assert.That(f.RouteSteps, Is.EqualTo(dist[outlet]), at + ": кратчайший путь");
                    Assert.That(f.RouteCells.Length, Is.EqualTo(f.RouteSteps + 1));
                    Assert.That(f.RouteCells[0], Is.EqualTo(f.OutletCell));
                    Assert.That(f.RouteCells[f.RouteCells.Length - 1], Is.EqualTo(map.IntakeCell));
                    for (int k = 0; k < f.RouteCells.Length; k++)
                    {
                        int c = f.RouteCells[k];
                        Assert.That(map.IsPlayable(c) && !walls.Contains(c), Is.True, at + ": труба по полу, не через заводы");
                        Assert.That(dist[c], Is.EqualTo(f.RouteSteps - k), at + ": каждый шаг ближе к IntakeCell");
                    }
                    Assert.That(FactoryMath.TransitTicks(Configs.FactoryRules, f.RouteSteps), Is.GreaterThanOrEqualTo(OutpostIds.TicksPerSecond));
                    factories++;
                }
            }
            Assert.That(passed, Is.EqualTo(144));
            Assert.That(factories, Is.EqualTo(1232), "все заводы кампании с трубой");
        }

        /// <summary>
        /// ТЗ 2.2 §4.1: валидатор ловит оба случая на прежней топологии wide-branching-mines-2. Её расстановка восстанавливается из
        /// отчёта генератора (обратные перестановки Docs/Reports/topology/factories.json; маска пола в отчёте не менялась):
        /// случай 1 не проходит ровно в 38 шахтах списка ТЗ, в итоговой карте у 35 заводов нет трубы, в 10 шахтах стоящий завод
        /// отрезает карман пола. Остальные 106 шахт в отчёт не попали и совпадают с прежними побайтно (проверяет генератор).
        /// </summary>
        [Test]
        public void Validator_DetectsBothCases_OnWideBranchingMines2_ReconstructedFromGeneratorReport()
        {
            var report = JObject.Parse(File.ReadAllText(TopologyReportPath("factories.json")));
            Assert.That((string)report["version"], Is.EqualTo(MineTopologyCatalog.SupportedVersion));
            Assert.That((int)report["changedMines"], Is.EqualTo(38));
            Assert.That((int)report["factoriesWithPipeRoute"], Is.EqualTo(1232));
            var expected = new[] { 6, 7, 13, 17, 23, 37, 49, 54, 59, 66, 68, 71, 82, 84, 85, 91, 96, 102, 104, 105, 110, 112, 115, 118, 119,
                122, 123, 126, 128, 129, 132, 133, 136, 139, 140, 142, 143, 144 };
            var pockets = new[] { 17, 23, 49, 59, 68, 104, 110, 115, 122, 140 };
            var zones = ((JArray)report["zones"]).Cast<JObject>().ToList();
            CollectionAssert.AreEqual(expected, zones.Select(z => (int)z["zone"]).ToArray(), "список шахт ТЗ 2.2 §4.1");
            int withoutRoute = 0;
            var cutOff = new List<int>();
            foreach (var entry in zones)
            {
                int z = (int)entry["zone"];
                Assert.That(((JArray)entry["carved"]).Count + ((JArray)entry["filledCells"]).Count, Is.EqualTo(0), "zone" + z + ": маска пола прежняя");
                var snapshot = OutpostJson.DeepCopy(Configs.Topologies.For("zone" + z));
                var placements = snapshot.Placements.ToDictionary(p => p.TargetId);
                foreach (var swap in ((JArray)entry["swaps"]).Cast<JObject>().Reverse())
                {
                    int dc = (int)swap["fromColumn"] - (int)swap["toColumn"], dr = (int)swap["fromRow"] - (int)swap["toRow"];
                    var f = placements[(int)swap["factory"]];
                    f.Column += dc;
                    f.Row += dr;
                    foreach (var moved in (JArray)swap["movedTargets"])
                    {
                        var p = placements[(int)moved];
                        p.Column -= dc;
                        p.Row -= dr;
                    }
                }
                var source = OutpostLevelAdapter.Build(TestData.Balance, z, Configs.Overrides);
                var fixedMap = MineTopologyAdapter.Apply(source, Configs.Topologies.For("zone" + z));
                Assert.That(OutpostReachability.Check(fixedMap).Passed, Is.True, "zone" + z + ": после исправления");
                var oldMap = MineTopologyAdapter.Apply(source, snapshot);
                var waves = OutpostReachability.Check(oldMap);
                Assert.That(waves.Passed || waves.WavesPassed, Is.False, "zone" + z + ": случай 1 не проходит: " + waves.Diagnostic);
                Assert.That(waves.Unreachable, Is.Not.Empty);
                var final = OutpostReachability.CheckFinalMap(oldMap);
                Assert.That(final.Passed, Is.False, "zone" + z + ": случай 2 не проходит");
                withoutRoute += final.FactoriesWithoutRoute.Count;
                Assert.That(final.FactoriesWithoutRoute.Count, Is.EqualTo(oldMap.Factories.Count(f => !f.HasRoute)), "zone" + z);
                if (final.CutOffCells.Count > 0) cutOff.Add(z);
            }
            Assert.That(withoutRoute, Is.EqualTo(35), "заводов без свободной соседней клетки, связанной с базой");
            CollectionAssert.AreEqual(pockets, cutOff, "шахты, где стоящий завод отрезает карман пола");
        }

        static IEnumerable<int> Around(OutpostMap map, int cell)
        {
            int c = map.Col(cell), r = map.Row(cell);
            if (r > 0) yield return cell - map.Width;
            if (c > 0) yield return cell - 1;
            if (c + 1 < map.Width) yield return cell + 1;
            if (r + 1 < map.Rows) yield return cell + map.Width;
        }

        /// <summary>BFS от IntakeCell по итоговой карте: стены — порода и footprint заводов, ворота открыты, r/x не мешают.</summary>
        static int[] IndependentDistances(OutpostMap map, HashSet<int> walls)
        {
            var dist = Enumerable.Repeat(-1, map.CellCount).ToArray();
            var queue = new Queue<int>();
            dist[map.IntakeCell] = 0;
            queue.Enqueue(map.IntakeCell);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                foreach (int n in Around(map, cell))
                    if (dist[n] < 0 && map.IsPlayable(n) && !walls.Contains(n))
                    {
                        dist[n] = dist[cell] + 1;
                        queue.Enqueue(n);
                    }
            }
            return dist;
        }

        /// <summary>Отчёты лежат в Docs/ основного проекта; в копии build/ci ищем выше по дереву (как аудит в OutpostKitA).</summary>
        static string TopologyReportPath(string file)
        {
            for (var dir = new DirectoryInfo(TestData.ProjectRoot); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "Docs", "Reports", "topology", file);
                if (File.Exists(candidate)) return candidate;
            }
            return Path.Combine(TestData.ProjectRoot, "Docs", "Reports", "topology", file);
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
            // Профиль b6 (решение владельца В4, 15 октября 2026): сравнение ходьбы двух топологий — без множителя темпа K(z)
            // (K = 1, числа b5): K одинаково растягивает обе топологии, а 144 шахты по часам и суткам тест не уложит.
            var branching = Configs.WithoutPace();
            var legacy = branching.WithTopologies(null);
            var rows = new List<object>();
            double oldTotal = 0, newTotal = 0, maximum = 0;
            for (int zone = 1; zone <= 144; zone++)
            {
                var old = OutpostBot.RunZone(TestData.Balance, legacy, zone, BotPolicy.A, 31337);
                var current = OutpostBot.RunZone(TestData.Balance, branching, zone, BotPolicy.A, 31337);
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
            // Профиль b6 (решение владельца В4, 15 октября 2026): кампания из 144 шахт с K(z) — около 50 дней логического времени;
            // проверка «карточки, экипировка и покупки проходят все шахты» — без множителя темпа (K = 1, числа b5). Шахты с K > 1
            // проходит Campaign_FreePlayer_PacingWithinStageTargets (выборка).
            var configs = Configs.WithoutPace().WithCards(TestData.Balance);
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
            // Профиль b6 (решение владельца В4, 15 октября 2026): K(72) ≈ 48 растягивает шахту 72 в десятки раз, и 50 000 событий
            // её не доводят. Проверка путей и разных ветвей — правило топологии, длительность ей не важна: без множителя темпа
            // (K = 1, числа b5), предел событий и все проверки прежние.
            var configs = Configs.WithoutPace();
            var state = StageProgressionService.NewGame(TestData.Balance, configs, 31337, 0, zone);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, state.Run);
            var sim = new OutpostSimulation(state, map, configs) { RecordEvents = false };
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
