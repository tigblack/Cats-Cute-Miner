using System.Linq;
using System.Numerics;
using CatMiners.Core.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    // One synthetic high-HP rock keeps these tests short without completing a mine or running a campaign.
    public sealed class OutpostDamageBonusTests
    {
        static OutpostConfigSet s_Configs;
        static OutpostConfigSet DamageConfigs => s_Configs ?? (s_Configs = ConfigsWith(j =>
        {
            j["HpBase"] = "1000000";
            j["CargoHpDivisor"] = 1000000;
        }));

        static OutpostFixtureB Fixture() => SingleRock(DamageConfigs);
        static RewardedOffer Earned(OutpostSimulation sim, string id = "damage-earned") => new RewardedOffer
        {
            Id = id, RunId = sim.Run.RunId, Bonus = "damage", Amount = 1, Earned = true,
        };

        static void Grant(OutpostSimulation sim, string id = "damage-earned")
        {
            var offer = Earned(sim, id);
            sim.Profile.PendingRewardedOffer = offer;
            Assert.That(sim.DeliverVerifiedReward(offer, StartUtcMs).Ok, Is.True);
        }

        [Test]
        public void Damage_AppendOnlyIdentity_WeightedOffer_ExcludedWhileActive()
        {
            Assert.That((int)RewardedBonusKind.Gold, Is.EqualTo(0));
            Assert.That((int)RewardedBonusKind.Dynamite, Is.EqualTo(3));
            Assert.That((int)RewardedBonusKind.Damage, Is.EqualTo(4));
            Assert.That(RewardedBonus.TryParse("damage", out var kind), Is.True);
            Assert.That(kind, Is.EqualTo(RewardedBonusKind.Damage));
            Assert.That(RewardedBonus.Placement(kind), Is.EqualTo("bonus_damage"));
            var sim = Fixture().NewSim();
            Assert.That(sim.RollBonus(0.9999).Kind, Is.EqualTo(kind));
            Grant(sim);
            Assert.That(sim.TryQuoteBonus(kind, out _), Is.False);
            Assert.That(sim.RollBonus(0.9999).Kind, Is.Not.EqualTo(kind));
            Assert.That(sim.TryQuoteBonus(RewardedBonusKind.Gems, out _), Is.True);
        }

        [Test]
        public void Damage_UnmatchedOrUnearnedAds_DoNotGrantOrConsumeReceipt()
        {
            var sim = Fixture().NewSim();
            var offer = Earned(sim);
            offer.Earned = false; // cancellation, no-fill and closing an unearned ad all leave this false
            sim.Profile.PendingRewardedOffer = offer;
            string before = OutpostJson.Serialize(sim.State);
            Assert.That(sim.DeliverVerifiedReward(offer, StartUtcMs).Reason, Is.EqualTo("unmatched"));
            Assert.That(OutpostJson.Serialize(sim.State), Is.EqualTo(before));
            offer.Earned = true;
            offer.RunId = "another-mine";
            Assert.That(sim.DeliverVerifiedReward(offer, StartUtcMs).Reason, Is.EqualTo("unmatched"));
            Assert.That(sim.DamageBonusActive, Is.False);
            Assert.That(sim.Profile.DeliveredRewardedOffers, Is.Empty);
        }

        [Test]
        public void Damage_DoublesCatAndPlayerStats_LeavesEconomyAndOtherStatsUnchanged()
        {
            var sim = Fixture().NewSim();
            var normal = sim.Stats;
            var wallet = sim.Run.Wallet;
            long gems = sim.Profile.Gems, elixir = sim.Profile.Elixir;
            Grant(sim);
            Assert.That(sim.Profile.DamageBonusSecondsLeft, Is.EqualTo((Rational)300));
            Assert.That(sim.Stats.DamageUnits, Is.EqualTo(2 * normal.DamageUnits));
            Assert.That(sim.Stats.TapDamageUnits, Is.EqualTo(2 * normal.TapDamageUnits));
            Assert.That(sim.Stats.DamageUnitsFor('r', "rockbasic"), Is.EqualTo(2 * normal.DamageUnitsFor('r', "rockbasic")));
            Assert.That(sim.Stats.DamageUnitsFor('p', "checkpoint"), Is.EqualTo(2 * normal.DamageUnitsFor('p', "checkpoint")));
            Assert.That(sim.Stats.FinalValue, Is.EqualTo(normal.FinalValue));
            Assert.That(sim.Stats.CoinValue(Rational.One), Is.EqualTo(normal.CoinValue(Rational.One)));
            Assert.That(sim.Stats.EdgeTicks, Is.EqualTo(normal.EdgeTicks));
            Assert.That(sim.Stats.Capacity, Is.EqualTo(normal.Capacity));
            Assert.That(sim.Run.Wallet, Is.EqualTo(wallet));
            Assert.That(sim.Run.BonusCredited, Is.EqualTo(BigInteger.Zero));
            Assert.That(sim.Profile.Gems, Is.EqualTo(gems));
            Assert.That(sim.Profile.Elixir, Is.EqualTo(elixir));
            var granted = sim.Events.Single(e => e.Type == OutpostEventType.BonusGranted);
            Assert.That(granted.ItemId, Is.EqualTo("damage"));
        }

        [Test]
        public void Damage_ExpiryBeforeSameTickCatHitsAndScheduledStrikes()
        {
            var sim = Fixture().NewSim();
            // Grant exactly when the first miner starts: its hits align with the expiry second.
            while (!sim.Run.Workers.Any(w => w.Mode == WorkerMode.Mining)) sim.AdvanceToNextEvent();
            long normalDamage = sim.Stats.DamageUnits, normalTap = sim.Stats.TapDamageUnits;
            Grant(sim);
            long expiry = sim.Now + 300 * S;
            sim.Schedule(OutpostCommand.Strike(SingleRockId), expiry - 1);
            sim.Schedule(OutpostCommand.Strike(SingleRockId), expiry);
            sim.Schedule(OutpostCommand.Strike(SingleRockId), expiry + 1);
            sim.Events.Clear();
            sim.Advance(expiry - 1);
            Assert.That(sim.DamageBonusActive, Is.True);
            Assert.That(sim.Profile.DamageBonusSecondsLeft, Is.EqualTo(Rational.Of(1, S)));
            Assert.That(sim.ScheduledResults[0].Cost, Is.EqualTo(new BigInteger(2 * normalTap)));
            sim.Advance(expiry + 1);
            Assert.That(sim.DamageBonusActive, Is.False);
            Assert.That(sim.Profile.DamageBonusSecondsLeft, Is.EqualTo(Rational.Zero));
            Assert.That(sim.Stats.DamageUnits, Is.EqualTo(normalDamage));
            Assert.That(sim.ScheduledResults.Skip(1).All(r => r.Ok && r.Cost == normalTap), Is.True);
            var boundary = sim.Events.Where(e => e.Type == OutpostEventType.TargetHit && e.Tick == expiry).ToArray();
            Assert.That(boundary, Is.Not.Empty, "a cat hits on the expiry boundary");
            foreach (var hit in boundary) Assert.That(hit.Value, Is.EqualTo(new BigInteger(normalDamage * hit.Count)));
            foreach (var hit in sim.Events.Where(e => e.Type == OutpostEventType.TargetHit && e.Tick < expiry))
                Assert.That(hit.Value, Is.EqualTo(new BigInteger(2 * normalDamage * hit.Count)));
        }

        [Test]
        public void Damage_LongAdvance_EqualsPartitionedAdvanceAcrossReload()
        {
            var f = Fixture();
            var whole = f.NewSim();
            Grant(whole);
            var parts = f.Reload(whole);
            whole.Advance(310 * S);
            parts.Advance(123 * S + 456789);
            Assert.That(parts.Profile.DamageBonusSecondsLeft, Is.EqualTo(Rational.Of(176543211, S)));
            parts = f.Reload(parts);
            parts.Advance(300 * S - 1);
            Assert.That(parts.DamageBonusActive, Is.True);
            while (!parts.Advance(310 * S, 1).Reached) { }
            Assert.That(OutpostJson.Serialize(parts.State), Is.EqualTo(OutpostJson.Serialize(whole.State)));
        }

        [Test]
        public void Damage_OfflineJob_ConsumesOnlySimulatedTime_ExpiresWithinOneLongAdvance()
        {
            var f = Fixture();
            var sim = f.NewSim();
            Grant(sim);
            string before = OutpostJson.Serialize(sim.State);
            var offline = new OfflineSimulator(f.Map, f.Configs);
            var shortJob = offline.Begin(sim.State, StartUtcMs + 125000);
            shortJob.RunToEnd();
            Assert.That(shortJob.State.Profile.DamageBonusSecondsLeft, Is.EqualTo((Rational)175));
            var longJob = offline.Begin(sim.State, StartUtcMs + 310000);
            while (!longJob.Step(3)) { }
            Assert.That(longJob.State.Profile.DamageBonusSecondsLeft, Is.EqualTo(Rational.Zero));
            Assert.That(OutpostJson.Serialize(sim.State), Is.EqualTo(before), "offline remains staged until committed");
            sim.Advance(310 * S);
            sim.Run.LastProcessedUtcMs = StartUtcMs + 310000;
            Assert.That(OutpostJson.Serialize(longJob.State), Is.EqualTo(OutpostJson.Serialize(sim.State)));
        }

        [Test]
        public void Damage_SaveServiceRoundTrip_PendingEarnedDeliveryAndReplayAreDurable()
        {
            var f = Fixture();
            var sim = f.NewSim();
            sim.Profile.PendingRewardedOffer = Earned(sim);
            var service = new OutpostSaveService("/damage-bonus", new MemoryFileSystemA())
            {
                Configs = f.Configs, MapResolver = _ => f.Map,
            };
            Assert.That(service.Save(sim.State, map: f.Map).Ok, Is.True);
            sim = f.Wrap(service.Load().State);
            var earned = sim.Profile.PendingRewardedOffer;
            Assert.That(sim.DeliverVerifiedReward(earned, StartUtcMs).Ok, Is.True);
            sim.Advance(27 * S + 123456);
            var left = sim.Profile.DamageBonusSecondsLeft;
            Assert.That(service.Save(sim.State, map: f.Map).Ok, Is.True);
            sim = f.Wrap(service.Load().State);
            Assert.That(sim.Profile.DamageBonusSecondsLeft, Is.EqualTo(left));
            Assert.That(sim.DamageBonusActive, Is.True);
            Assert.That(sim.DeliverVerifiedReward(earned, StartUtcMs).Reason, Is.EqualTo("duplicate"));
            Assert.That(sim.Profile.DamageBonusSecondsLeft, Is.EqualTo(left), "replay cannot refresh the timer");
        }

        [Test]
        public void Damage_FailedDeliveryCommitLeavesEarnedOffer_RetryCommitsBoostAndReceiptTogether()
        {
            var f = Fixture();
            var live = f.NewSim();
            live.Profile.PendingRewardedOffer = Earned(live);
            var fs = new MemoryFileSystemA();
            var saves = new OutpostSaveService("/damage-failure", fs) { Configs = f.Configs, MapResolver = _ => f.Map };
            Assert.That(saves.Save(live.State, map: f.Map).Ok, Is.True);
            var candidate = f.Reload(live);
            var offer = candidate.Profile.PendingRewardedOffer;
            Assert.That(candidate.DeliverVerifiedReward(offer, StartUtcMs).Ok, Is.True);
            fs.ArmCrash(1, false);
            Assert.That(saves.Save(candidate.State, map: f.Map).Ok, Is.False);
            var previous = saves.Load().State;
            Assert.That(previous.Profile.DamageBonusSecondsLeft, Is.EqualTo(Rational.Zero));
            Assert.That(previous.Profile.PendingRewardedOffer.Earned, Is.True);
            Assert.That(previous.Profile.DeliveredRewardedOffers, Is.Empty);
            Assert.That(live.DamageBonusActive, Is.False);
            fs = fs.CrashImage();
            saves = new OutpostSaveService("/damage-failure", fs) { Configs = f.Configs, MapResolver = _ => f.Map };
            Assert.That(saves.Save(candidate.State, map: f.Map).Ok, Is.True);
            var restarted = f.Wrap(saves.Load().State);
            Assert.That(restarted.Profile.DamageBonusSecondsLeft, Is.EqualTo((Rational)300));
            Assert.That(restarted.Profile.PendingRewardedOffer, Is.Null);
            Assert.That(restarted.DeliverVerifiedReward(offer, StartUtcMs).Reason, Is.EqualTo("duplicate"));
        }

        [Test]
        public void Damage_TimeSkipConsumesSimulationDurationWithoutChangingUtcBoundary()
        {
            var f = Fixture();
            var sim = f.NewSim();
            Grant(sim);
            var skip = new StoreItemDef { Id = "test-skip", Kind = StoreItemKind.TimeSkip, Minutes = 5 };
            var job = new OfflineSimulator(f.Map, f.Configs).BeginSkip(sim.State, skip, StartUtcMs);
            job.RunToEnd();
            Assert.That(job.State.Profile.DamageBonusSecondsLeft, Is.EqualTo(Rational.Zero));
            Assert.That(job.State.Run.Now, Is.EqualTo(300 * S));
            Assert.That(job.State.Run.LastProcessedUtcMs, Is.EqualTo(StartUtcMs));
            Assert.That(sim.Profile.DamageBonusSecondsLeft, Is.EqualTo((Rational)300));
        }

        [Test]
        public void Damage_OldSaveWithoutField_IsInactive()
        {
            var state = Fixture().NewState();
            var json = JObject.Parse(OutpostSaveService.SerializeState(state));
            ((JObject)json["Profile"]).Remove("DamageBonusSecondsLeft");
            var old = OutpostSaveService.DeserializeState(json.ToString());
            Assert.That(old.Profile.DamageBonusSecondsLeft, Is.EqualTo(Rational.Zero));
            Assert.That(Fixture().Wrap(old).DamageBonusActive, Is.False);
        }

        [Test]
        public void Damage_NewEarnedReceiptRefreshesDuration_NeverStacksMultiplier()
        {
            var sim = Fixture().NewSim();
            long normal = sim.Stats.DamageUnits;
            Grant(sim);
            sim.Advance(35 * S);
            Grant(sim, "another-earned-ad");
            Assert.That(sim.Stats.DamageUnits, Is.EqualTo(2 * normal));
            Assert.That(sim.Profile.DamageBonusSecondsLeft, Is.EqualTo((Rational)300));
            Assert.That(sim.Profile.DeliveredRewardedOffers.Count, Is.EqualTo(2));
        }

        [Test]
        public void Damage_MineTransitionCarriesFractionalSeconds_UsesNewClockOrigin()
        {
            var f = Fixture();
            var sim = f.NewSim();
            Grant(sim);
            sim.Advance(37 * S + 123456);
            var left = sim.Profile.DamageBonusSecondsLeft;
            sim.Run.Phase = RunPhase.ReadyToContinue; // isolate transition; no full mine run
            var next = StageProgressionService.PrepareTransition(sim.State, TestData.Balance, f.Configs, StartUtcMs);
            Assert.That(next.Ok, Is.True);
            var loaded = new OutpostSimulation(next.State, next.Map, f.Configs);
            Assert.That(loaded.Now, Is.Zero);
            Assert.That(loaded.Profile.DamageBonusSecondsLeft, Is.EqualTo(left));
            Assert.That(loaded.DamageBonusActive, Is.True);
            loaded.Advance(1);
            Assert.That(loaded.Profile.DamageBonusSecondsLeft, Is.EqualTo(left - Rational.Of(1, loaded.Balance.TicksPerSecond)));
        }

        [Test]
        public void Damage_EventDrivenExpiryStillRunsWhenMineHasNoOtherEvents()
        {
            var sim = Fixture().NewSim();
            Grant(sim);
            sim.Run.Phase = RunPhase.ReadyToContinue;
            foreach (var worker in sim.Run.Workers)
            {
                worker.Action = null;
                worker.Mode = WorkerMode.Idle;
            }
            Assert.That(sim.NextEventTick(), Is.EqualTo(300 * S));
            Assert.That(sim.Advance(310 * S, 0).Reached, Is.False);
            Assert.That(sim.Now, Is.Zero);
            Assert.That(sim.Profile.DamageBonusSecondsLeft, Is.EqualTo((Rational)300));
            Assert.That(sim.AdvanceToNextEvent(), Is.True);
            Assert.That(sim.Now, Is.EqualTo(300 * S));
            Assert.That(sim.DamageBonusActive, Is.False);
            Assert.That(sim.NextEventTick(), Is.EqualTo(long.MaxValue));
        }
    }
}
