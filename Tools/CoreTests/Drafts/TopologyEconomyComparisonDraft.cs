using System;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    // Run separately from Unity: paired campaigns use the same policy, seed,
    // equipment and card rules. This complements the no-purchase travel audit.
    public sealed class TopologyEconomyComparisonDraft
    {
        [Test]
        public void FirstMineWithOneManualUpgrade()
        {
            var configs = OutpostConfigSet.LoadDefault().WithCards(TestData.Balance);
            var state = StageProgressionService.NewGame(TestData.Balance, configs, 1800000000000, 1800000000000);
            var sim = new OutpostSimulation(state, OutpostLevelAdapter.ForRun(TestData.Balance, state.Run), configs);
            sim.Advance(20 * OutpostIds.TicksPerSecond);
            Assert.That(sim.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Pickaxes)).Ok, Is.True);
            var report = new OutpostBot(BotPolicy.A).Run(sim);
            Assert.That(report.Success, Is.True, report.Error ?? report.WatchdogDiagnostic);
            File.WriteAllText(Path.Combine(TestData.ProjectRoot, "Docs", "Reports", "topology", "onboarding-single-purchase.json"),
                OutpostJson.Serialize(report));
            TestContext.WriteLine("Single manual pickaxe purchase at 20 seconds: " + report.CompletedSec + " seconds");
        }

        [Test]
        public void CompareCampaignsWithCardsAndUpgrades()
        {
            var configs = OutpostConfigSet.LoadDefault().WithCards(TestData.Balance);
            var legacy = OutpostBot.RunCampaign(TestData.Balance, configs.WithTopologies(null), BotPolicy.C, 31337, 144);
            var branching = OutpostBot.RunCampaign(TestData.Balance, configs, BotPolicy.C, 31337, 144);
            Assert.That(legacy.All(r => r.Success), Is.True);
            Assert.That(branching.All(r => r.Success), Is.True);
            double legacySeconds = legacy.Sum(r => r.CompletedSec.Value);
            double branchingSeconds = branching.Sum(r => r.CompletedSec.Value);
            var rows = legacy.Zip(branching, (a, b) => new { zone = a.Zone,
                legacySeconds = a.CompletedSec, branchingSeconds = b.CompletedSec,
                ratio = b.CompletedSec.Value / a.CompletedSec.Value,
                legacyFirstDeliverySeconds = a.FirstDeliverySec, branchingFirstDeliverySeconds = b.FirstDeliverySec,
                legacyFirstPurchaseSeconds = a.FirstPurchaseSec, branchingFirstPurchaseSeconds = b.FirstPurchaseSec,
                legacyCredited = a.Credited, branchingCredited = b.Credited,
                legacyUnits = a.UnitsCreated, branchingUnits = b.UnitsCreated }).ToArray();
            string dir = Path.Combine(TestData.ProjectRoot, "Docs", "Reports", "topology");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "campaign-comparison.json"), OutpostJson.Serialize(new {
                sourceSha256 = TestData.Balance.Sha256, seed = 31337, policy = "C", cards = true,
                legacyTotalSeconds = legacySeconds, branchingTotalSeconds = branchingSeconds,
                totalRatio = branchingSeconds / legacySeconds,
                maximumRatio = rows.Max(r => r.ratio), rows }));
            File.WriteAllText(Path.Combine(dir, "campaign-cards-legacy.json"), OutpostJson.Serialize(legacy));
            TestContext.WriteLine("C, same seed/cards/upgrades: " + legacySeconds / 3600 + "h -> " +
                branchingSeconds / 3600 + "h; ratio=" + branchingSeconds / legacySeconds);
            Assert.That(branchingSeconds / legacySeconds, Is.LessThan(1.25));
        }
    }
}
