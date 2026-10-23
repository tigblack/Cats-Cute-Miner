using System;
using System.Collections.Generic;
using System.Numerics;
using CatMiners.Core.Outpost;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    public class IntegrationPrivacyRewardTests
    {
        [TestCase(NeutralAge.Unselected)]
        [TestCase(NeutralAge.Unknown)]
        [TestCase(NeutralAge.Under13)]
        [TestCase(NeutralAge.Age13To15)]
        [TestCase((NeutralAge)999)]
        public void YoungerUnknownAndCorruptAgesCannotInitializeEvenWithStaleConsent(NeutralAge age)
        {
            var privacy = new IntegrationPrivacy { Age = age, ChoicesMade = true, AppMetrica = true, Adjust = true, Ads = true, Meta = true };
            Assert.IsFalse(privacy.MayInitialize(privacy.AppMetrica));
            Assert.IsFalse(privacy.MayInitialize(privacy.Adjust));
            Assert.IsFalse(privacy.MayInitialize(privacy.Ads));
            Assert.IsFalse(privacy.MayInitialize(privacy.Meta));
            privacy.ChooseAge(age);
            Assert.IsFalse(privacy.AppMetrica || privacy.Adjust || privacy.Ads || privacy.Meta);
        }
        [Test]
        public void OlderAgeAloneDoesNotGrantAnyConsentAndChoicesAreIndependent()
        {
            var privacy = new IntegrationPrivacy();
            privacy.ChooseAge(NeutralAge.Age16OrOlder);
            Assert.IsFalse(privacy.MayInitialize(true));
            privacy.ChoicesMade = true;
            privacy.AppMetrica = true;
            Assert.IsTrue(privacy.MayInitialize(privacy.AppMetrica));
            Assert.IsFalse(privacy.MayInitialize(privacy.Adjust));
            Assert.IsFalse(privacy.MayInitialize(privacy.Meta));
            Assert.IsFalse(privacy.MayInitialize(privacy.Ads));
        }
        [TestCase(NeutralAge.Unknown)]
        [TestCase(NeutralAge.Unselected)]
        public void UnspecifiedAgeCanBeChosenVoluntarilyWithoutGrantingAnalytics(NeutralAge previous)
        {
            var privacy = new IntegrationPrivacy { Age = previous, ChoicesMade = true, AppMetrica = true };
            Assert.IsTrue(privacy.CanChooseAnalyticsAge);
            Assert.IsTrue(privacy.TryChooseAnalyticsAge(NeutralAge.Age16OrOlder));
            Assert.AreEqual(NeutralAge.Age16OrOlder, privacy.Age);
            Assert.IsFalse(privacy.ChoicesMade);
            Assert.IsFalse(privacy.MayInitialize(true));
            Assert.IsFalse(privacy.AppMetrica || privacy.Adjust || privacy.Meta || privacy.Ads);
        }
        [TestCase(NeutralAge.Under13)]
        [TestCase(NeutralAge.Age13To15)]
        [TestCase(NeutralAge.Age16OrOlder)]
        [TestCase((NeutralAge)999)]
        public void SettingsCannotReplaceAnEstablishedOrInvalidAge(NeutralAge previous)
        {
            var privacy = new IntegrationPrivacy { Age = previous };
            Assert.IsFalse(privacy.CanChooseAnalyticsAge);
            Assert.IsFalse(privacy.TryChooseAnalyticsAge(NeutralAge.Age16OrOlder));
            Assert.AreEqual(previous, privacy.Age);
        }
        [TestCase(NeutralAge.Unselected)]
        [TestCase((NeutralAge)999)]
        public void OptionalAgeChoiceRejectsInvalidSelections(NeutralAge selection)
        {
            var privacy = new IntegrationPrivacy { Age = NeutralAge.Unknown };
            Assert.IsFalse(privacy.TryChooseAnalyticsAge(selection));
            Assert.AreEqual(NeutralAge.Unknown, privacy.Age);
        }
        [TestCase(NeutralAge.Unselected)]
        [TestCase(NeutralAge.Unknown)]
        [TestCase(NeutralAge.Under13)]
        [TestCase(NeutralAge.Age13To15)]
        [TestCase(NeutralAge.Age16OrOlder)]
        [TestCase((NeutralAge)999)]
        public void BasicAppMetricaDefaultsOnAndAgeSelectionPreservesExplicitOptOut(NeutralAge age)
        {
            var privacy = new IntegrationPrivacy { Age = age };
            Assert.IsTrue(privacy.AppMetricaEnabled);
            Assert.IsFalse(privacy.ChoicesMade);
            Assert.IsFalse(privacy.AppMetrica);
            privacy.AppMetricaDisabled = true;
            privacy.AppMetrica = true; // Legacy consent cannot override an explicit opt-out.
            Assert.IsFalse(privacy.AppMetricaEnabled);
            privacy.ChooseAge(age);
            Assert.IsTrue(privacy.AppMetricaDisabled);
            Assert.IsFalse(privacy.AppMetricaEnabled);
            privacy.AppMetricaDisabled = false;
            privacy.ChooseAge(age);
            Assert.IsTrue(privacy.AppMetricaEnabled);
        }

        static OutpostSimulation Arena()
        {
            var configs = Configs.WithCards(TestData.Balance);
            var state = StageProgressionService.NewGame(TestData.Balance, configs, Seed, StartUtcMs, 1);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, state.Run);
            return new OutpostSimulation(state, map, configs);
        }
        static RewardedOffer Offer(OutpostSimulation sim, bool earned = true) => new RewardedOffer {
            Id = Guid.NewGuid().ToString("N"), RunId = sim.Run.RunId, Gold = "173", Earned = earned };
        [Test]
        public void ClosedUnrewardedAndWrongRunCannotGrant()
        {
            var sim = Arena(); var wallet = sim.Run.Wallet;
            Assert.IsFalse(sim.DeliverVerifiedReward(Offer(sim, false), StartUtcMs).Ok);
            var old = Offer(sim); old.RunId = "old-run";
            Assert.IsFalse(sim.DeliverVerifiedReward(old, StartUtcMs).Ok);
            Assert.AreEqual(wallet, sim.Run.Wallet);
        }
        [Test]
        public void DeliverySnapshotRetryAndRestartDoNotReplayGold()
        {
            var sim = Arena(); var wallet = sim.Run.Wallet;
            var offer = Offer(sim);
            sim.Profile.PendingRewardedOffer = offer;
            // Prepare on a copy, as runtime does: a failed durable save leaves the original unmodified.
            var prepared = OutpostJson.DeepCopy(sim.State);
            var candidate = new OutpostSimulation(prepared, sim.Map, Configs.WithCards(TestData.Balance));
            Assert.IsTrue(candidate.DeliverVerifiedReward(prepared.Profile.PendingRewardedOffer, StartUtcMs).Ok);
            Assert.AreEqual(wallet, sim.Run.Wallet);
            Assert.AreEqual(wallet + 173, candidate.Run.Wallet);
            Assert.IsNull(prepared.Profile.PendingRewardedOffer);
            // The same immutable snapshot is the retry; reload it to model restart after successful persistence.
            var reload = new OutpostSimulation(OutpostJson.DeepCopy(prepared), sim.Map, Configs.WithCards(TestData.Balance));
            Assert.IsFalse(reload.DeliverVerifiedReward(offer, StartUtcMs).Ok);
            Assert.AreEqual(wallet + 173, reload.Run.Wallet);
            Assert.AreEqual(1, reload.Profile.DeliveredRewardedOffers.Count);
        }
        [Test]
        public void FailedWriteRetainsEarnedOfferAndRetryPersistsGrantAndLedgerTogether()
        {
            var sim = Arena(); var wallet = sim.Run.Wallet;
            var configs = Configs.WithCards(TestData.Balance);
            var fs = new MemoryFileSystemA();
            var saves = new OutpostSaveService("reward-test", fs) { Configs = configs, MapResolver = state => sim.Map };
            var offer = Offer(sim); sim.Profile.PendingRewardedOffer = offer;
            var initial = saves.Save(sim.State, map: sim.Map);
            Assert.IsTrue(initial.Ok, initial.Error);
            var prepared = OutpostJson.DeepCopy(sim.State);
            var candidate = new OutpostSimulation(prepared, sim.Map, configs);
            Assert.IsTrue(candidate.DeliverVerifiedReward(prepared.Profile.PendingRewardedOffer, StartUtcMs).Ok);
            fs.ArmCrash(1, false);
            Assert.IsFalse(saves.Save(prepared, map: sim.Map).Ok);
            var unchanged = saves.Load();
            Assert.AreEqual(wallet, unchanged.State.Run.Wallet);
            Assert.IsTrue(unchanged.State.Profile.PendingRewardedOffer.Earned);
            fs = fs.CrashImage();
            saves = new OutpostSaveService("reward-test", fs) { Configs = configs, MapResolver = state => sim.Map };
            var retry = saves.Save(prepared, map: sim.Map);
            Assert.IsTrue(retry.Ok, retry.Error);
            var durable = saves.Load().State;
            Assert.AreEqual(wallet + 173, durable.Run.Wallet);
            Assert.IsNull(durable.Profile.PendingRewardedOffer);
            Assert.Contains(offer.Id, durable.Profile.DeliveredRewardedOffers);
            var restarted = new OutpostSimulation(durable, sim.Map, configs);
            Assert.IsFalse(restarted.DeliverVerifiedReward(offer, StartUtcMs).Ok);
            Assert.AreEqual(wallet + 173, restarted.Run.Wallet);
        }
        [Test]
        public void VerifiedChestCannotDeliverRealMoneySkuAndHasAtomicCooldownAndReplayLedger()
        {
            var sim = Arena(); var offer = Offer(sim); offer.ItemId = "gems_160";
            Assert.IsFalse(sim.DeliverVerifiedReward(offer, StartUtcMs).Ok);
            offer.ItemId = "chest_wooden";
            Assert.IsTrue(sim.DeliverVerifiedReward(offer, StartUtcMs).Ok);
            Assert.Greater(sim.Profile.StoreCooldownUntilUtcMs[offer.ItemId], StartUtcMs);
            Assert.IsFalse(sim.DeliverVerifiedReward(offer, StartUtcMs).Ok);
            Assert.IsFalse(sim.DeliverVerifiedReward(new RewardedOffer { Id = "second", RunId = sim.Run.RunId, Earned = true, ItemId = offer.ItemId }, StartUtcMs).Ok);
        }
    }
}
