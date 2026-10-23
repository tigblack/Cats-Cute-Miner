using System.Linq;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    public sealed class MiaCorridorTests
    {
        static OutpostConfigSet Configs => OutpostConfigSet.LoadDefault().WithCards(TestData.Balance);

        [Test]
        public void Mine1_TwoLanes_RightPocket_ExactlyFifteenObstacles()
        {
            var configs = Configs;
            var map = OutpostLevelAdapter.BuildMine(TestData.Balance, 1, configs);
            Assert.AreEqual(MiaCorridor.Layout, map.Topology.Layout);
            Assert.AreEqual(15, map.RequiredCount);
            Assert.AreEqual(15, map.Targets.Length);
            Assert.AreEqual(12, map.Targets.Count(t => t.Piece == null));
            Assert.AreEqual(0, map.Gates.Length);
            Assert.AreEqual(0, map.Factories.Length);
            Assert.IsNull(map.Final);
            for (int row = 3; row <= 8; row++)
                foreach (int col in new[] { map.Width / 2 - 1, map.Width / 2 })
                    Assert.GreaterOrEqual(map.InitialOccupant[map.Cell(col, row)], 0, "two columns of blocks");
            for (int row = 5; row <= 7; row++)
                Assert.IsTrue(map.IsPlayable(map.Cell(map.Width / 2 + 1, row)), "short right branch");
            Assert.IsFalse(map.IsPlayable(map.Cell(map.Width / 2 + 1, 4)), "branch ends after two cells upward");
            CollectionAssert.AreEquivalent(new[] { "planks", "rubble" }, map.Dressing.Pieces.Where(p => p.Branch == 1).Select(p => p.Family));
            Assert.AreEqual(1, Enumerable.Range(0, map.Width).Count(c => map.IsPlayable(map.Cell(c, 1))), "single approach to the lock, cage is blocked");
            Assert.AreEqual("mia", map.Dressing.Rescue.Character);
            var route = map.Dressing.Rescue.RouteTargetIds;
            Assert.AreEqual(6, route.Count, "short six-block route along one of the two lanes");
            Assert.IsTrue(route.All(id => map.Target(id) != null));
            Assert.IsTrue(OutpostReachability.Check(map).Passed);
        }

        [Test]
        public void CorridorSave_ContinuesSameTargetsAndHp_AfterReload()
        {
            var configs = Configs;
            var state = StageProgressionService.NewGame(TestData.Balance, configs, 51, 1_800_000_000_000L, 1);
            var sim = new OutpostSimulation(state, OutpostLevelAdapter.ForRun(TestData.Balance, state.Run), configs);
            sim.Advance(30L * OutpostIds.TicksPerSecond);
            var savedRun = OutpostJson.Serialize(state.Run);
            var loaded = OutpostJson.DeepCopy(state);
            OutpostStateUpgrade.Normalize(loaded);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, loaded.Run);
            Assert.AreEqual(savedRun, OutpostJson.Serialize(loaded.Run));
            Assert.AreEqual(15, map.Targets.Length);
            Assert.IsTrue(StageProgressionService.IsRunMapCurrent(loaded.Run, configs));
            CollectionAssert.IsEmpty(OutpostInvariants.Check(loaded, map, configs.Equipment, configs.Cards));
            var resumed = new OutpostSimulation(loaded, map, configs);
            resumed.Advance(1800L * OutpostIds.TicksPerSecond);
            Assert.IsFalse(resumed.RescuePending);
            Assert.AreEqual(15, loaded.Run.Targets.Count(t => t.Destroyed));
            Assert.IsTrue(loaded.Profile.Progression.HasCrew("mia"));
        }

        [Test]
        public void ReplacementGrid_IsExplicitAndRestrictedToMine1()
        {
            var replacement = OutpostJson.DeepCopy(Configs.Overrides.For("zone1"));
            var invalid = new LevelOverrides { Version = "test" };
            invalid.Zones.Add("zone2", replacement);
            Assert.Throws<OutpostConfigException>(() => LevelOverrides.Parse(invalid.CanonicalJson()));
            invalid.Zones.Clear();
            invalid.Zones.Add("zone1", replacement);
            replacement.ReplacementWidth++;
            Assert.Throws<OutpostConfigException>(() => LevelOverrides.Parse(invalid.CanonicalJson()));
            Assert.IsFalse(new ZoneOverride { Name = "old", Reason = "old" }.CanonicalJson().Contains("Replacement"),
                "Absent optional fields must not change old snapshot checksums");
        }
    }
}
