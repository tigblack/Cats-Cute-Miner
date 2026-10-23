using System;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    public class HiringEconomyTests
    {
        static OutpostConfigSet Configs => OutpostConfigSet.LoadDefault().WithCards(TestData.Balance);

        [TestCase(3, 82521L)] [TestCase(4, 124094L)] [TestCase(5, 320320L)]
        [TestCase(6, 2995563L)] [TestCase(144, 5490940L)]
        public void LegacyFinalHp_StaysAtPublishedCalibration_CompactHpUsesFourFrontSlots(int mine, long hp)
        {
            var cfg = Configs;
            var rules = OutpostJson.DeepCopy(cfg.FinalExcavations);
            rules.Version = "final-excavations-1"; rules.Width = 13; rules.Depth = 3;
            var map = OutpostLevelAdapter.BuildMine(TestData.Balance, mine, cfg.WithFinalExcavations(rules));
            Assert.That(map.Final.MaxHp, Is.EqualTo(hp));
            var ordinary = OutpostLevelAdapter.BuildMine(TestData.Balance, mine, cfg.WithFinalExcavations(null));
            var reference = FinalExcavationReference.Build(ordinary, cfg, TestData.Balance);
            var nominal = Rational.Of(reference.Stats.DamageUnitsFor('x', "final-excavation"), OutpostIds.HpUnitsPerHp) *
                Math.Min(reference.Workers, 4) * Rational.Of(cfg.Balance.TicksPerSecond, cfg.Balance.HitIntervalTicks);
            var expected = (nominal * rules.ForMine(mine).TargetSeconds * rules.ThroughputFor(mine)).Ceil();
            var compact = OutpostLevelAdapter.BuildMine(TestData.Balance, mine, cfg);
            Assert.That(compact.Final.MaxHp, Is.EqualTo((long)expected));
            TestContext.WriteLine("Mine " + mine + ": legacy HP " + hp + ", compact HP " + expected + ", fixture crew " + reference.Workers);
        }

        [TestCase(0)] [TestCase(5)] [TestCase(50)]
        public void All144Mines_HiringUsesNormalPrices_RegardlessOfLegacyFinalDiscount(int discountPercent)
        {
            var b = Configs.Balance;
            var discount = Rational.Of(discountPercent, 100);
            var stats = new TeamStats { CardHireDiscount = discount };
            for (int mine = 1; mine <= 144; mine++)
            {
                var run = new OutpostRun
                {
                    Balance = b, Zone = mine, Levels = new int[OutpostIds.BranchCount],
                    PriceFactor = Rational.Of(211, 100),
                    MapFinal = new FinalExcavation { HiringCostFactor = Rational.Of(1, 100) },
                };
                for (int level = 0; level < 10; level++)
                {
                    run.Levels[(int)UpgradeBranch.Team] = level;
                    var expected = (b.PriceScale(mine) * run.PriceFactor * 50 * Rational.Of(19, 10).Pow(level) *
                        (Rational.One - discount)).Ceil();
                    var withFinal = TeamUpgradeService.Quote(run, UpgradeBranch.Team, BuyMode.One, stats);
                    Assert.That(withFinal.NextCost, Is.EqualTo(expected), "Mine " + mine + " hire " + level);
                    var snapshot = run.MapFinal;
                    run.MapFinal = null;
                    Assert.That(TeamUpgradeService.Quote(run, UpgradeBranch.Team, BuyMode.One, stats).NextCost,
                        Is.EqualTo(withFinal.NextCost));
                    run.MapFinal = snapshot;
                    // At equal ranks hiring costs more than each repeatable team-stat upgrade.
                    if (discountPercent == 0) foreach (var branch in new[] { UpgradeBranch.Pickaxes, UpgradeBranch.Boots, UpgradeBranch.Bags, UpgradeBranch.Unload, UpgradeBranch.Value })
                    {
                        run.Levels[(int)branch] = level;
                        Assert.That(TeamUpgradeService.Quote(run, branch, BuyMode.One, stats).NextCost.Value,
                            Is.LessThan(withFinal.NextCost.Value), "Mine " + mine + " " + branch + " L" + level);
                    }
                }
            }
        }

        [TestCase(3)] [TestCase(7)] [TestCase(37)] [TestCase(144)]
        public void LegacySave_RetainsCrewAndFinalHp_AndChargesFullQuotedPrice(int mine)
        {
            var cfg = Configs;
            var state = StageProgressionService.NewGame(TestData.Balance, cfg, 1, 0, mine);
            Assert.That(state.Run.MapFinal.HiringCostFactor, Is.EqualTo(Rational.One), "new runs carry no live subsidy");
            state.Run.MapFinal.HiringCostFactor = Rational.Of(1, 100); // pre-fix snapshot
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, state.Run);
            var sim = new OutpostSimulation(state, map, cfg) { RecordEvents = false };
            Assert.That(sim.Submit(OutpostCommand.RewardBonus(RewardedBonusKind.Gold, BigInteger.Pow(10, 40))).Ok, Is.True);
            for (int i = 0; i < 4; i++) Assert.That(sim.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Team)).Ok, Is.True);
            string json = OutpostJson.Serialize(state);
            var saved = OutpostJson.Deserialize<OutpostGameState>(json);
            var restoredMap = OutpostLevelAdapter.ForRun(TestData.Balance, saved.Run);
            var restored = new OutpostSimulation(saved, restoredMap, cfg) { RecordEvents = false };
            Assert.That(saved.Run.Workers.Count, Is.EqualTo(state.Run.Workers.Count));
            Assert.That(saved.Run.Level(UpgradeBranch.Team), Is.EqualTo(4));
            Assert.That(saved.Run.MapFinal.MaxHp, Is.EqualTo(state.Run.MapFinal.MaxHp));
            Assert.That(saved.Run.Targets.Last().RemainingHpUnits, Is.EqualTo(state.Run.Targets.Last().RemainingHpUnits));
            var expected = (saved.Run.Balance.PriceScale(mine) * saved.Run.PriceFactor * 50 * Rational.Of(19, 10).Pow(4) *
                (Rational.One - restored.Stats.CardHireDiscount)).Ceil();
            Assert.That(restored.Quote(UpgradeBranch.Team, BuyMode.One).NextCost, Is.EqualTo(expected));
            var wallet = saved.Run.Wallet;
            var spent = saved.Run.TotalSpent;
            int workers = saved.Run.Workers.Count;
            var purchase = restored.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Team));
            Assert.That(purchase.Ok, Is.True);
            Assert.That(purchase.Cost, Is.EqualTo(expected));
            Assert.That(saved.Run.Wallet, Is.EqualTo(wallet - expected));
            Assert.That(saved.Run.TotalSpent, Is.EqualTo(spent + expected));
            Assert.That(saved.Run.Workers.Count, Is.EqualTo(workers + 1));
            Assert.That(OutpostInvariants.Check(saved, restoredMap, cfg.Equipment, cfg.Cards), Is.Empty);
            TestContext.WriteLine("Mine " + mine + ": hire rank 4 costs " + expected);
        }

        [Test]
        public void LegacySave_CannotBuyHireForOldOnePercentPrice()
        {
            var cfg = Configs;
            var state = StageProgressionService.NewGame(TestData.Balance, cfg, 1, 0, 3);
            state.Run.MapFinal.HiringCostFactor = Rational.Of(1, 100);
            var sim = new OutpostSimulation(state, OutpostLevelAdapter.ForRun(TestData.Balance, state.Run), cfg);
            var full = sim.Quote(UpgradeBranch.Team, BuyMode.One).NextCost.Value;
            var oldPrice = (new Rational(full) / 100).Ceil();
            sim.Submit(OutpostCommand.RewardBonus(RewardedBonusKind.Gold, oldPrice));
            string before = OutpostJson.Serialize(state);
            var result = sim.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Team));
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Reason, Is.EqualTo("gold"));
            Assert.That(OutpostJson.Serialize(state), Is.EqualTo(before));
        }

        [Test]
        public void GoldBonus_UsesFullHirePricesWhenOnlyTeamRemains()
        {
            var cfg = Configs;
            var state = StageProgressionService.NewGame(TestData.Balance, cfg, 1, 0, 3);
            state.Run.MapFinal.HiringCostFactor = Rational.Of(1, 100);
            foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
                if (branch != UpgradeBranch.Team) state.Run.Levels[(int)branch] = cfg.Balance.Branch(branch).Cap;
            var sim = new OutpostSimulation(state, OutpostLevelAdapter.ForRun(TestData.Balance, state.Run), cfg);
            var scale = state.Run.Balance.PriceScale(3) * state.Run.PriceFactor * 50 * (Rational.One - sim.Stats.CardHireDiscount);
            Assert.That(sim.TryQuoteBonus(RewardedBonusKind.Gold, out var gold), Is.True);
            Assert.That(gold, Is.EqualTo(scale.Ceil() + (scale * Rational.Of(19, 10)).Ceil()));
        }
    }
}
