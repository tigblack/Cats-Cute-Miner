using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CatMiners.Core.Outpost;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CatMiners.Tests
{
    public sealed partial class OutpostRuntimeTests
    {
        const BindingFlags RewardMembers = BindingFlags.Instance | BindingFlags.NonPublic;
        const string DamageReceipt = "runtime-damage-receipt";
        HashSet<string> EarnedCallbacks => RewardField<HashSet<string>>("m_EarnedCallbacks");
        OutpostSaveService RewardSaves => RewardField<OutpostSaveService>("m_Saves");
        T RewardField<T>(string name) => (T)typeof(OutpostController).GetField(name, RewardMembers).GetValue(Game);
        void RewardInvoke(string name, params object[] args) =>
            typeof(OutpostController).GetMethod(name, RewardMembers).Invoke(Game, args);
        void RewardProperty(string name, object value) => typeof(OutpostController).GetProperty(name).SetValue(Game, value);
        void RewardTick() => RewardInvoke("Update");

        OutpostSimulation PrepareDamageReceipt()
        {
            // Use the real controller/save coordinator on a synthetic high-HP rock, without completing a mine.
            // Drive Update explicitly so suspension and offline commit boundaries are deterministic.
            Game.enabled = false;
            OutpostController.SimulateRewardedOffersForTests = true;
            var repo = OutpostResources.LoadSource(Object.FindAnyObjectByType<GameBootstrap>().BalanceJson);
            repo.Zone(1).Grid = ".,.,.,.,.,.,e:exit:1:x,.,.,.,r:rockbasic:1,.,.,.,c:spawningcart:1,.,.,.,.,.,.";
            repo.Zone(1).WidthCells = 7;
            repo.Zone(1).DepthCells = 3;
            var balance = JObject.Parse(OutpostConfigSet.DefaultBalanceJson);
            balance["HpBase"] = "1000000";
            balance["CargoHpDivisor"] = 1000000;
            var configs = Game.Configs.WithBalance(OutpostBalanceConfig.Parse(balance.ToString()))
                .WithTopologies(null).WithDressing(null).WithFinalExcavations(null);
            RewardProperty("Balance", repo);
            RewardProperty("Configs", configs);
            RewardSaves.Configs = configs;
            var map = OutpostLevelAdapter.Build(repo, 1);
            Assert.That(map.Targets.Length, Is.EqualTo(1));
            var profile = StageProgressionService.NewProfile(configs, 31337);
            var state = new OutpostGameState { Profile = profile,
                Run = StageProgressionService.CreateRun(profile, 1, map, configs, repo, m_Clock.UtcNowMs) };
            state.Profile.CardKeys.Add(CardService.LegacyImportKey);
            state.Profile.Settings.LegacySettingsImported = true;
            StoreService.MigrateProfile(state.Profile, configs.Store);
            RewardInvoke("SetSimulation", state, map);
            for (int i = 0; i < 8; i++)
            {
                m_Clock.Advance(0.5);
                RewardInvoke("AdvanceOnline", true);
            }
            state.Profile.PendingRewardedOffer = new RewardedOffer {
                Id = DamageReceipt, RunId = state.Run.RunId, Bonus = "damage", Amount = 1,
                OfferedUtcMs = m_Clock.UtcNowMs,
            };
            RewardInvoke("Commit", state, Game.Map, null, false);
            Assert.That(Game.SaveFailed, Is.False);
            return new OutpostSimulation(OutpostJson.DeepCopy(state), Game.Map, configs);
        }

        void AssertReceiptWaiting()
        {
            Assert.That(Game.State.Profile.PendingRewardedOffer.Earned, Is.True);
            Assert.That(Game.State.Profile.DamageBonusSecondsLeft, Is.EqualTo(Rational.Zero));
            Assert.That(Game.State.Profile.DeliveredRewardedOffers, Is.Empty);
            var durable = RewardSaves.Load().State;
            Assert.That(durable.Profile.PendingRewardedOffer.Earned, Is.True);
            Assert.That(durable.Profile.DamageBonusSecondsLeft, Is.EqualTo(Rational.Zero));
            Assert.That(durable.Profile.DeliveredRewardedOffers, Is.Empty);
        }

        void AssertCaughtUpWithoutBoost(OutpostSimulation expected)
        {
            long elapsedMs = m_Clock.UtcNowMs - expected.Run.LastProcessedUtcMs;
            expected.Advance(expected.Now + elapsedMs * (OutpostIds.TicksPerSecond / 1000));
            expected.Run.LastProcessedUtcMs = m_Clock.UtcNowMs;
            Assert.That(Game.Sim.Now, Is.EqualTo(expected.Now));
            Assert.That(Game.State.Run.LastProcessedUtcMs, Is.EqualTo(m_Clock.UtcNowMs));
            Assert.That(OutpostJson.Serialize(Game.State.Run.Targets), Is.EqualTo(OutpostJson.Serialize(expected.Run.Targets)),
                "pre-reward hits must match an unboosted simulation");
        }

        void FinishRewardCatchUp()
        {
            for (int frame = 0; frame < 20 && Game.Phase == OutpostControllerPhase.OfflineCatchUp; frame++) RewardTick();
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
            AssertReceiptWaiting();
            RewardTick();
            Assert.That(Game.State.Profile.DamageBonusSecondsLeft, Is.EqualTo((Rational)300));
            Assert.That(Game.State.Profile.PendingRewardedOffer, Is.Null);
            CollectionAssert.AreEqual(new[] { DamageReceipt }, Game.State.Profile.DeliveredRewardedOffers);
            Assert.That(EarnedCallbacks, Does.Not.Contain(DamageReceipt));
            var durable = RewardSaves.Load().State;
            Assert.That(durable.Profile.DamageBonusSecondsLeft, Is.EqualTo((Rational)300));
            Assert.That(durable.Profile.PendingRewardedOffer, Is.Null);
            CollectionAssert.AreEqual(new[] { DamageReceipt }, durable.Profile.DeliveredRewardedOffers);
        }

        [UnityTest]
        public IEnumerator RewardDelivery_SuspendedReceiptPersistsImmediately_ResumeGetsFullDuration()
        {
            var expected = PrepareDamageReceipt();
            Game.SendMessage("OnApplicationPause", true);
            m_Clock.Advance(10);
            EarnedCallbacks.Add(DamageReceipt);
            var live = Game.Sim;
            RewardTick();
            AssertReceiptWaiting();
            Assert.That(Game.Sim, Is.SameAs(live), "receipt persistence must preserve the clock and simulation");
            Assert.That(Game.Sim.Now, Is.EqualTo(expected.Now));
            Assert.That(EarnedCallbacks, Does.Contain(DamageReceipt));
            int writes = m_Files.AttemptPayloads.Count;
            RewardTick();
            Assert.That(m_Files.AttemptPayloads.Count, Is.EqualTo(writes), "an earned receipt is persisted once");
            Game.SendMessage("OnApplicationPause", false);
            FinishRewardCatchUp();
            AssertCaughtUpWithoutBoost(expected);
            m_Clock.Advance(0.25);
            RewardTick();
            Assert.That(Game.State.Profile.DamageBonusSecondsLeft, Is.EqualTo(Rational.Of(1199, 4)));
            RewardTick();
            Assert.That(Game.State.Profile.DamageBonusSecondsLeft, Is.EqualTo(Rational.Of(1199, 4)));
            Assert.That(Game.State.Profile.DeliveredRewardedOffers.Count, Is.EqualTo(1));
            yield break;
        }

        [UnityTest]
        public IEnumerator RewardDelivery_FocusAndPauseMustBothResumeBeforeGrant()
        {
            var expected = PrepareDamageReceipt();
            Game.SendMessage("OnApplicationFocus", false);
            Game.SendMessage("OnApplicationPause", true);
            m_Clock.Advance(10);
            EarnedCallbacks.Add(DamageReceipt);
            RewardTick();
            Game.SendMessage("OnApplicationFocus", true);
            RewardTick();
            AssertReceiptWaiting();
            m_Clock.Advance(5);
            Game.SendMessage("OnApplicationPause", false);
            FinishRewardCatchUp();
            AssertCaughtUpWithoutBoost(expected);
            yield break;
        }

        [UnityTest]
        public IEnumerator RewardDelivery_OrdinaryFrameAdvancesBeforeGrant_WithoutLosingElapsedTime()
        {
            var expected = PrepareDamageReceipt();
            m_Clock.Advance(0.75);
            EarnedCallbacks.Add(DamageReceipt);
            RewardTick();
            Assert.That(Game.State.Profile.DamageBonusSecondsLeft, Is.EqualTo((Rational)300));
            AssertCaughtUpWithoutBoost(expected);
            yield break;
        }

        [UnityTest]
        public IEnumerator RewardDelivery_UnreportedGapIsCaughtUpBeforeGrant()
        {
            var expected = PrepareDamageReceipt();
            m_Clock.Advance(10);
            EarnedCallbacks.Add(DamageReceipt);
            RewardTick();
            AssertReceiptWaiting();
            FinishRewardCatchUp();
            AssertCaughtUpWithoutBoost(expected);
            yield break;
        }

        [UnityTest]
        public IEnumerator RewardDelivery_ReloadUsesDurableReceipt_AfterCatchingUpWholeAbsence()
        {
            var expected = PrepareDamageReceipt();
            Game.SendMessage("OnApplicationPause", true);
            m_Clock.Advance(10);
            EarnedCallbacks.Add(DamageReceipt);
            RewardTick();
            AssertReceiptWaiting();
            m_Clock.Advance(5);
            EarnedCallbacks.Clear(); // the earned SDK callback is no longer available after a restart
            RewardInvoke("LoadProfile", OutpostSaveSlot.Main);
            RewardTick();
            AssertReceiptWaiting();
            Game.SendMessage("OnApplicationPause", false);
            FinishRewardCatchUp();
            AssertCaughtUpWithoutBoost(expected);
            yield break;
        }

        [UnityTest]
        public IEnumerator RewardDelivery_PartialOfflineJobPreservesReceiptAcrossResuspension_AndProcessingTime()
        {
            var expected = PrepareDamageReceipt();
            m_Clock.Advance(10);
            RewardInvoke("BeginOffline", false);
            var job = RewardField<OfflineJob>("m_Offline");
            Assert.That(job.Step(1), Is.False);
            long partialTick = job.State.Run.Now;
            var live = Game.Sim;
            EarnedCallbacks.Add(DamageReceipt);
            RewardInvoke("StepRewardDelivery");
            AssertReceiptWaiting();
            Assert.That(Game.Sim, Is.SameAs(live));
            Assert.That(job.State.Profile.PendingRewardedOffer.Earned, Is.True);
            Assert.That(job.State.Run.Now, Is.EqualTo(partialTick));
            Game.SendMessage("OnApplicationPause", true);
            m_Clock.Advance(5);
            Game.SendMessage("OnApplicationPause", false);
            var restarted = RewardField<OfflineJob>("m_Offline");
            Assert.That(restarted, Is.Not.SameAs(job));
            Assert.That(restarted.State.Profile.PendingRewardedOffer.Earned, Is.True);
            m_Clock.Advance(0.5); // elapsed while processing belongs before the pending boost as well
            FinishRewardCatchUp();
            AssertCaughtUpWithoutBoost(expected);
            yield break;
        }

        [UnityTest]
        public IEnumerator RewardDelivery_ReceiptSaveRetryKeepsSnapshot_ThenCatchesUpBeforeGrant()
        {
            var expected = PrepareDamageReceipt();
            Game.SendMessage("OnApplicationPause", true);
            m_Clock.Advance(10);
            EarnedCallbacks.Add(DamageReceipt);
            m_Files.FailWrites = true;
            RewardTick();
            Assert.That(Game.SaveFailed, Is.True);
            Assert.That(Game.State.Profile.PendingRewardedOffer.Earned, Is.False);
            Assert.That(RewardSaves.Load().State.Profile.PendingRewardedOffer.Earned, Is.False);
            string snapshot = m_Files.AttemptPayload;
            int retry = m_Files.AttemptPayloads.Count;
            m_Clock.Advance(5);
            m_Files.FailWrites = false;
            Game.RetrySave();
            Assert.That(m_Files.AttemptPayloads[retry], Is.EqualTo(snapshot));
            AssertReceiptWaiting();
            Game.SendMessage("OnApplicationPause", false);
            FinishRewardCatchUp();
            AssertCaughtUpWithoutBoost(expected);
            yield break;
        }

        [UnityTest]
        public IEnumerator RewardDelivery_OfflineSaveRetryRetainsEarnedReceipt_AndCatchesUpRetryDelay()
        {
            var expected = PrepareDamageReceipt();
            Game.SendMessage("OnApplicationPause", true);
            m_Clock.Advance(10);
            EarnedCallbacks.Add(DamageReceipt);
            RewardTick();
            Game.SendMessage("OnApplicationPause", false);
            m_Files.FailWrites = true;
            for (int frame = 0; frame < 20 && !Game.SaveFailed; frame++) RewardTick();
            Assert.That(Game.SaveFailed, Is.True);
            string snapshot = m_Files.AttemptPayload;
            int retry = m_Files.AttemptPayloads.Count;
            m_Clock.Advance(5);
            m_Files.FailWrites = false;
            Game.RetrySave();
            Assert.That(m_Files.AttemptPayloads[retry], Is.EqualTo(snapshot));
            AssertReceiptWaiting();
            FinishRewardCatchUp();
            AssertCaughtUpWithoutBoost(expected);
            yield break;
        }

        [UnityTest]
        public IEnumerator RewardDelivery_GrantSaveRetryCommitsOnce_AndReloadCannotReplay()
        {
            PrepareDamageReceipt();
            Game.SendMessage("OnApplicationPause", true);
            EarnedCallbacks.Add(DamageReceipt);
            RewardTick();
            Game.SendMessage("OnApplicationPause", false);
            for (int frame = 0; frame < 20 && Game.Phase == OutpostControllerPhase.OfflineCatchUp; frame++) RewardTick();
            AssertReceiptWaiting();
            m_Files.FailWrites = true;
            RewardTick();
            Assert.That(Game.SaveFailed, Is.True);
            AssertReceiptWaiting();
            string snapshot = m_Files.AttemptPayload;
            RewardTick();
            m_Files.FailWrites = false;
            int retry = m_Files.AttemptPayloads.Count;
            Game.RetrySave();
            Assert.That(m_Files.AttemptPayloads[retry], Is.EqualTo(snapshot));
            Assert.That(Game.State.Profile.DamageBonusSecondsLeft, Is.EqualTo((Rational)300));
            Assert.That(EarnedCallbacks, Does.Not.Contain(DamageReceipt));
            RewardInvoke("LoadProfile", OutpostSaveSlot.Main);
            for (int frame = 0; frame < 20 && Game.Phase == OutpostControllerPhase.OfflineCatchUp; frame++) RewardTick();
            EarnedCallbacks.Add(DamageReceipt); // even a late duplicate callback cannot refresh the boost
            m_Clock.Advance(0.25);
            RewardTick();
            Assert.That(Game.State.Profile.DamageBonusSecondsLeft, Is.EqualTo(Rational.Of(1199, 4)));
            CollectionAssert.AreEqual(new[] { DamageReceipt }, Game.State.Profile.DeliveredRewardedOffers);
            yield break;
        }
    }
}
