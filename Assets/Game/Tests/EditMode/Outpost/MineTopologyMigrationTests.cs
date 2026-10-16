using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    public sealed class MineTopologyMigrationTests
    {
        static readonly OutpostConfigSet Configs = OutpostConfigSet.LoadDefault().WithCards(TestData.Balance);

        static OutpostGameState LegacyState(int zone, bool versionOne)
        {
            var legacy = Configs.WithTopologies(null);
            if (versionOne)
            {
                var path = Path.Combine(TestData.ProjectRoot, "Assets", "Game", "Tests", "Fixtures", "Levels", "mine_topology_v1_zone1.json");
                var topology = OutpostJson.Deserialize<MineTopology>(File.ReadAllText(path));
                var catalog = new MineTopologyCatalog { Version = MineTopologyCatalog.LegacyVersion, SourceSha256 = Configs.Topologies.SourceSha256 };
                catalog.Zones.Add("zone1", topology);
                legacy = Configs.WithTopologies(catalog);
            }
            return StageProgressionService.NewGame(TestData.Balance, legacy, 31337, 1_800_000_000_000L, zone);
        }

        static string Units(OutpostGameState state) => string.Join(";", state.Run.Batches.SelectMany(b => b.Units)
            .Concat(state.Run.Workers.SelectMany(w => w.Bag)).OrderBy(u => u.Source).ThenBy(u => u.Ordinal)
            .Select(u => u.Source + ":" + u.Ordinal + "=" + u.Value));

        [TestCase(1, false)]
        [TestCase(1, true)]
        [TestCase(72, false)]
        [TestCase(144, false)]
        public void ActiveMine_PreservesProgressAndEveryCargoUnit_ReplansAndFinishes(int zone, bool versionOne)
        {
            var state = LegacyState(zone, versionOne);
            var old = new OutpostSimulation(state, OutpostLevelAdapter.ForRun(TestData.Balance, state.Run), Configs) { RecordEvents = false };
            var bot = new OutpostBot(BotPolicy.C);
            for (int i = 0; i < 1000 && !old.IsEconomyStopped; i++)
            {
                old.Advance(old.NextEventTick());
                bot.ApplyPolicy(old);
                if (state.Run.Workers.Any(w => w.Bag.Count > 0) && state.Run.Counters.DeliveredUnits > 0) break;
            }
            // Exercise dropped resources and reservations as well as carried bags.
            for (int i = 0; i < 12; i++)
            {
                var target = old.Map.Targets.FirstOrDefault(t => !t.IsGate && old.CanStrike(t.Id));
                if (target != null) old.Submit(OutpostCommand.Strike(target.Id));
            }
            Assert.That(state.Run.Workers.Sum(w => w.Bag.Count), Is.GreaterThan(0));
            Assert.That(state.Run.Batches.Sum(b => b.Units.Count), Is.GreaterThan(0));
            string original = OutpostJson.Serialize(state), profile = OutpostJson.Serialize(state.Profile);
            string targets = OutpostJson.Serialize(state.Run.Targets), units = Units(state);
            Assert.That(MineTopologyMigration.TryPrepare(state, TestData.Balance, Configs, out var prepared, out var map, out var error), Is.True, error);
            Assert.That(OutpostJson.Serialize(state), Is.EqualTo(original), "Preparing does not mutate the loaded save");
            Assert.That(map.Width, Is.GreaterThanOrEqualTo(25));
            // ТЗ 2.2 §16.1 (г): перенос идёт в wide-branching-mines-3 (SupportedVersion); код миграции не менялся, новая карта
            // проходит оба случая валидатора со стоящими заводами (§4.1).
            Assert.That(prepared.Run.MapTopology.Version, Is.EqualTo(MineTopologyCatalog.SupportedVersion));
            Assert.That(OutpostReachability.Check(map).Passed, Is.True);
            Assert.That(OutpostJson.Serialize(prepared.Profile), Is.EqualTo(profile));
            Assert.That(OutpostJson.Serialize(prepared.Run.Targets), Is.EqualTo(targets));
            Assert.That(Units(prepared), Is.EqualTo(units));
            Assert.That(prepared.Run.Levels, Is.EqualTo(state.Run.Levels));
            Assert.That(prepared.Run.Wallet, Is.EqualTo(state.Run.Wallet));
            Assert.That(prepared.Run.TotalCredited, Is.EqualTo(state.Run.TotalCredited));
            Assert.That(prepared.Run.TotalSpent, Is.EqualTo(state.Run.TotalSpent));
            Assert.That(prepared.Run.Now, Is.EqualTo(state.Run.Now));
            Assert.That(prepared.Run.LastProcessedUtcMs, Is.EqualTo(state.Run.LastProcessedUtcMs));
            Assert.That(prepared.Run.GatesOpened, Is.EqualTo(state.Run.GatesOpened));
            Assert.That(prepared.Run.Workers.Select(w => w.Id), Is.EqualTo(state.Run.Workers.Select(w => w.Id)));
            Assert.That(OutpostInvariants.Check(prepared, map, Configs.Equipment, Configs.Cards), Is.Empty);
            var restored = OutpostJson.DeepCopy(prepared);
            var restoredSim = new OutpostSimulation(restored, OutpostLevelAdapter.ForRun(TestData.Balance, restored.Run), Configs);
            var result = new OutpostBot(BotPolicy.C).Run(restoredSim);
            Assert.That(result.Success, Is.True, result.Error ?? result.WatchdogDiagnostic);
            Assert.That(restored.Run.Batches, Is.Empty);
            Assert.That(restored.Run.Workers.All(w => w.Bag.Count == 0), Is.True);
            Assert.That(OutpostInvariants.Check(restored, restoredSim.Map, Configs.Equipment, Configs.Cards), Is.Empty);
            Assert.That(MineTopologyMigration.TryPrepare(restored, TestData.Balance, Configs, out _, out _, out _), Is.False, "Migration is applied once");
        }

        [Test]
        public void CompletedMine_KeepsRewardKeyAndRng_NoDuplicateCompletion()
        {
            var state = LegacyState(1, true);
            var old = new OutpostSimulation(state, OutpostLevelAdapter.ForRun(TestData.Balance, state.Run), Configs);
            Assert.That(new OutpostBot(BotPolicy.C).Run(old).Success, Is.True);
            string profile = OutpostJson.Serialize(state.Profile), counters = OutpostJson.Serialize(state.Run.Counters);
            Assert.That(MineTopologyMigration.TryPrepare(state, TestData.Balance, Configs, out var prepared, out var map, out var error), Is.True, error);
            Assert.That(OutpostJson.Serialize(prepared.Profile), Is.EqualTo(profile));
            Assert.That(OutpostJson.Serialize(prepared.Run.Counters), Is.EqualTo(counters));
            Assert.That(prepared.Run.CompletionRewardKey, Is.EqualTo(state.Run.CompletionRewardKey));
            var sim = new OutpostSimulation(prepared, map, Configs);
            sim.Advance(sim.Now + 3 * OutpostIds.TicksPerSecond);
            Assert.That(OutpostJson.Serialize(prepared.Profile), Is.EqualTo(profile));
            Assert.That(OutpostInvariants.Check(prepared, map, Configs.Equipment, Configs.Cards), Is.Empty);
        }

        [Test]
        public void OriginalSourceBeforeContentOverride_AdoptsWideGeometryWithoutAddingRewards()
        {
            var legacy = OutpostConfigSet.Parse(OutpostConfigSet.DefaultBalanceJson, OutpostConfigSet.DefaultEquipmentJson,
                OutpostConfigSet.DefaultRewardsJson, "{\"Version\":\"level-overrides-1\",\"Zones\":{}}", OutpostConfigSet.DefaultCardRulesJson)
                .WithCards(TestData.Balance);
            var state = StageProgressionService.NewGame(TestData.Balance, legacy, 19, 0);
            Assert.That(MineTopologyMigration.TryPrepare(state, TestData.Balance, Configs, out var prepared, out var map, out var error), Is.True, error);
            Assert.That(prepared.Run.Targets.Count, Is.EqualTo(state.Run.Targets.Count));
            Assert.That(OutpostJson.Serialize(prepared.Run.Targets), Is.EqualTo(OutpostJson.Serialize(state.Run.Targets)));
            Assert.That(prepared.Run.MapOverride, Is.Null);
            Assert.That(map.Width, Is.GreaterThanOrEqualTo(25));
            Assert.That(new OutpostBot(BotPolicy.C).Run(new OutpostSimulation(prepared, map, Configs)).Success, Is.True);
        }

        [Test]
        public void DisconnectedCatalog_FallsBackWithoutMutatingProgress()
        {
            var state = LegacyState(1, true);
            var snapshot = OutpostJson.DeepCopy(Configs.Topologies.For("zone1"));
            // This empty stretch contains no targets; severing it leaves valid footprints but unreachable ore.
            snapshot.FloorRows[8] = new string('#', snapshot.Width);
            snapshot.SectorRows[8] = new string('#', snapshot.Width);
            var broken = new MineTopologyCatalog { Version = MineTopologyCatalog.SupportedVersion, SourceSha256 = Configs.Topologies.SourceSha256 };
            broken.Zones.Add("zone1", snapshot);
            string payload = OutpostJson.Serialize(state);
            Assert.That(MineTopologyMigration.TryPrepare(state, TestData.Balance, Configs.WithTopologies(broken), out var prepared, out var map, out var error), Is.False);
            Assert.That(error, Does.Contain("route mismatch"));
            Assert.That(prepared, Is.Null);
            Assert.That(map, Is.Null);
            Assert.That(OutpostJson.Serialize(state), Is.EqualTo(payload));
        }

        [Test]
        public void IncompatibleSource_FallsBackWithoutMutatingOriginal()
        {
            var state = LegacyState(1, true);
            state.Run.SourceSha256 = "unsupported-source";
            string payload = OutpostJson.Serialize(state);
            Assert.That(MineTopologyMigration.TryPrepare(state, TestData.Balance, Configs, out var prepared, out var map, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(prepared, Is.Null);
            Assert.That(map, Is.Null);
            Assert.That(OutpostJson.Serialize(state), Is.EqualTo(payload));
        }
    }
}
