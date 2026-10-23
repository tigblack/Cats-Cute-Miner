using System;
using System.Collections.Generic;
using CatMiners.Core.Outpost;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    public sealed class TargetPaceBandTests
    {
        const long HourMs = 3_600_000L;

        static OutpostConfigSet Configs(Rational factor, bool withBand = true)
        {
            var configs = OutpostTestHelpersB.Configs.WithDressing(null);
            var balance = configs.Balance.Clone();
            balance.OfflineCapMs = 3 * HourMs;
            balance.TargetPaceBands = withBand
                ? new List<TargetPaceBand> { Band(factor) }
                : null;
            return configs.WithBalance(balance);
        }

        static TargetPaceBand Band(Rational factor) => new TargetPaceBand
        {
            Name = "zone7-middle-regression",
            Zone = 7,
            FromRow = 10,
            ToRow = 16,
            Kinds = "rxs",
            HpFactor = factor,
            OfflineCapMs = 21_600_000L,
        };

        // These are deliberately test-owned factors, not a calibration of the shipped profile.
        // Six hours describes the middle section as a whole, not the duration of each target.
        [TestCase(1, 1)]
        [TestCase(5, 3)]
        [TestCase(7, 2)]
        public void ApplyPace_OnlyMatchingHpChanges_AfterCommonPaceAndCargo(int numerator, int denominator)
        {
            var local = Rational.Of(numerator, denominator);
            var balance = Configs(local).Balance.Clone();
            balance.PaceScale = new List<Rational>();
            for (int zone = 1; zone <= 8; zone++) balance.PaceScale.Add(Rational.Of(3, 2));
            var configs = Configs(local).WithBalance(balance);

            foreach (int zone in new[] { 6, 7, 8 })
            foreach (int row in new[] { 0, 9, 10, 13, 16, 17, 30 })
            foreach (char kind in "rxsp")
            {
                var map = new OutpostMap
                {
                    Number = zone,
                    Targets = new[] { new MapTarget { Id = 1, Kind = kind, Row = row } },
                };
                var target = new TargetBalance
                {
                    TargetId = 1, MaxHp = 101, TotalCargoUnits = kind == 's' ? 0 : 11,
                    ValueFactor = Rational.Of(7, 5), Rank01 = Rational.Of(1, 2), Area = 1,
                };
                var input = new[] { target };
                var actual = OutpostLevelBalanceAdapter.ApplyPace(map, configs.Balance, input);
                string context = "zone=" + zone + ", row=" + row + ", kind=" + kind;
                // ceil(101 * 3/2) = 152; factories skip the common K entirely.
                long commonHp = kind == 's' ? 101 : 152;
                bool matches = zone == 7 && row >= 10 && row <= 16 && kind != 'p';
                long expectedHp = matches ? (long)(local * commonHp).Ceil() : commonHp;
                Assert.AreSame(input, actual, context);
                Assert.AreEqual(expectedHp, actual[0].MaxHp, context);
                int commonCargo = kind == 's' ? 0 : Math.Max(balance.MinCargoUnits,
                    (152 + balance.CargoHpDivisor - 1) / balance.CargoHpDivisor);
                Assert.AreEqual(commonCargo, actual[0].TotalCargoUnits, context + ": cargo uses common HP only");
                Assert.AreEqual(matches ? local : Rational.One, balance.TargetPace(zone, row, kind), context);
                Assert.AreEqual(Rational.Of(7, 5), actual[0].ValueFactor, context);
                Assert.AreEqual(Rational.Of(1, 2), actual[0].Rank01, context);
                Assert.AreEqual(1, actual[0].Area, context);
                Assert.AreEqual(1, actual[0].TargetId, context);
            }
        }

        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void RealMap_LocalBandLeavesCargoAndEveryUpgradePriceUnchanged(int zone)
        {
            var factor = Rational.Of(7, 3);
            var legacy = Configs(factor, false);
            var current = Configs(factor);
            var map = OutpostTestHelpersB.Map(zone);
            var before = OutpostLevelBalanceAdapter.ApplyPace(map, legacy.Balance,
                OutpostLevelBalanceAdapter.Compute(map, legacy.Balance, TestData.Balance));
            var after = OutpostLevelBalanceAdapter.ApplyPace(map, current.Balance,
                OutpostLevelBalanceAdapter.Compute(map, current.Balance, TestData.Balance));
            int changed = 0;
            for (int i = 0; i < map.Targets.Length; i++)
            {
                var t = map.Targets[i];
                bool matches = zone == 7 && t.Row >= 10 && t.Row <= 16 && "rxs".IndexOf(t.Kind) >= 0;
                long expectedHp = matches ? (long)(factor * before[i].MaxHp).Ceil() : before[i].MaxHp;
                Assert.AreEqual(expectedHp, after[i].MaxHp, "target " + t.Id);
                Assert.AreEqual(before[i].TotalCargoUnits, after[i].TotalCargoUnits, "cargo " + t.Id);
                Assert.AreEqual(before[i].ValueFactor, after[i].ValueFactor, "coin value " + t.Id);
                if (t.IsFactory) Assert.Zero(after[i].TotalCargoUnits, "factory " + t.Id);
                if (matches) changed++;
            }
            if (zone == 7) Assert.Greater(changed, 0, "real middle section must exercise the band");
            Assert.AreEqual(legacy.Balance.PriceScale(zone), current.Balance.PriceScale(zone));
            foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
            for (int level = 0; level < legacy.Balance.Branch(branch).Cap; level++)
                Assert.AreEqual(TeamUpgradeService.NextCost(legacy.Balance, zone, branch, level),
                    TeamUpgradeService.NextCost(current.Balance, zone, branch, level), branch + " level " + level);
        }

        [TestCase(6, 24, 3)]
        [TestCase(7, 24, 6)]
        [TestCase(8, 24, 3)]
        [TestCase(7, 6, 6)]
        [TestCase(7, 2, 2)]
        public void OfflineJob_UsesCurrentZoneCap_WithoutAdvancingSimulation(int zone, int elapsedHours, int appliedHours)
        {
            var legacy = Configs(Rational.One, false);
            var current = Configs(Rational.Of(5, 3));
            // Old runs retain their economy, but offline limits use current policy.
            var state = OutpostTestHelpersB.Real(zone, legacy).NewState();
            string committed = OutpostJson.Serialize(state);
            var job = new OfflineSimulator(OutpostTestHelpersB.Map(zone), current)
                .Begin(state, state.Run.LastProcessedUtcMs + elapsedHours * HourMs);
            Assert.AreEqual((zone == 7 ? 6 : 3) * HourMs, current.Balance.ZoneOfflineCapMs(zone));
            Assert.AreEqual(elapsedHours * HourMs, job.Summary.ElapsedMs);
            Assert.AreEqual(appliedHours * HourMs, job.Summary.AppliedMs);
            Assert.AreEqual(state.Run.Now + appliedHours * HourMs * (state.Run.Balance.TicksPerSecond / 1000), job.TargetTick);
            Assert.AreEqual(state.Run.Now, job.State.Run.Now);
            Assert.AreEqual(committed, OutpostJson.Serialize(state), "Begin must not mutate the committed run");
        }

        [Test]
        public void LegacyJsonWithoutField_RetainsCanonicalJsonHashAndOldPacing()
        {
            var original = Configs(Rational.One, false).Balance;
            var payload = JObject.Parse(original.CanonicalJson());
            Assert.IsNull(payload.Property("TargetPaceBands"), "null field must be omitted, including in snapshots");
            string legacyJson = payload.ToString(Formatting.None);
            var parsed = OutpostBalanceConfig.Parse(legacyJson);
            Assert.IsNull(parsed.TargetPaceBands);
            Assert.AreEqual(legacyJson, parsed.CanonicalJson());
            Assert.AreEqual(OutpostJson.Sha256Hex(legacyJson), parsed.ComputeHash());
            Assert.AreEqual(legacyJson, parsed.Clone().CanonicalJson());
            foreach (int zone in new[] { 6, 7, 8 })
            {
                Assert.AreEqual(3 * HourMs, parsed.ZoneOfflineCapMs(zone));
                foreach (char kind in "rxsp") Assert.AreEqual(Rational.One, parsed.TargetPace(zone, 13, kind));
            }
            payload["TargetPaceBands"] = JValue.CreateNull();
            Assert.AreEqual(legacyJson, OutpostBalanceConfig.Parse(payload.ToString()).CanonicalJson());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NoBands_NullOrEmptyKeepsThreeHourOfflineJobInZoneSeven(bool emptyList)
        {
            var configs = Configs(Rational.One, false);
            if (emptyList)
            {
                var balance = configs.Balance.Clone();
                balance.TargetPaceBands = new List<TargetPaceBand>();
                configs = configs.WithBalance(balance);
            }
            var state = OutpostTestHelpersB.Real(7, configs).NewState();
            var job = new OfflineSimulator(OutpostTestHelpersB.Map(7), configs)
                .Begin(state, state.Run.LastProcessedUtcMs + 24 * HourMs);
            Assert.AreEqual(3 * HourMs, job.Summary.AppliedMs);
            Assert.AreEqual(state.Run.Now + 3 * HourMs * (state.Run.Balance.TicksPerSecond / 1000), job.TargetTick);
        }

        [Test]
        public void Band_SerializationAndClonePreserveRuleWithoutSharingMutableObjects()
        {
            var original = Configs(Rational.Of(11, 4)).Balance;
            var parsed = OutpostBalanceConfig.Parse(original.CanonicalJson());
            Assert.AreEqual(original.CanonicalJson(), parsed.CanonicalJson());
            var copy = parsed.Clone();
            Assert.AreNotSame(parsed.TargetPaceBands, copy.TargetPaceBands);
            Assert.AreNotSame(parsed.TargetPaceBands[0], copy.TargetPaceBands[0]);
            copy.TargetPaceBands[0].FromRow = 11;
            Assert.AreEqual(Rational.Of(11, 4), parsed.TargetPace(7, 10, 's'));
            Assert.AreEqual(Rational.One, copy.TargetPace(7, 10, 's'));
        }

        [Test]
        public void LegacyRun_SaveLoadWithNewPolicyPreservesTargetsAndBalanceSnapshot()
        {
            var legacy = Configs(Rational.One, false);
            var current = Configs(Rational.Of(9, 2));
            var state = OutpostTestHelpersB.Real(7, legacy).NewState();
            var sim = new OutpostSimulation(state, OutpostTestHelpersB.Map(7), legacy);
            sim.Advance(2 * OutpostIds.TicksPerSecond);
            string targets = OutpostJson.Serialize(state.Run.Targets);
            string balance = state.Run.Balance.CanonicalJson();
            var fs = new MemoryFileSystemA();
            var service = SaveKitA.NewService(fs, configs: current);
            Assert.IsTrue(service.Save(state).Ok, "save legacy run under new policy");
            var loaded = SaveKitA.NewService(fs, configs: current).Load();
            Assert.AreEqual(OutpostLoadStatus.Loaded, loaded.Status);
            Assert.IsNotNull(loaded.State);
            var resumed = new OutpostSimulation(loaded.State, OutpostTestHelpersB.Map(7), current);
            Assert.AreEqual(targets, OutpostJson.Serialize(resumed.State.Run.Targets), "HP, remaining HP and cargo stay saved");
            Assert.AreEqual(balance, resumed.State.Run.Balance.CanonicalJson());
            Assert.IsNull(resumed.State.Run.Balance.TargetPaceBands);
            Assert.AreEqual(state.Run.Now, resumed.State.Run.Now);
            foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
                Assert.AreEqual(sim.Quote(branch, BuyMode.One).NextCost, resumed.Quote(branch, BuyMode.One).NextCost);
        }

        [TestCase("null-rule")]
        [TestCase("empty-name")]
        [TestCase("blank-name")]
        [TestCase("zone-zero")]
        [TestCase("zone-too-large")]
        [TestCase("negative-row")]
        [TestCase("reversed-rows")]
        [TestCase("null-kinds")]
        [TestCase("empty-kinds")]
        [TestCase("gate-kind")]
        [TestCase("unknown-kind")]
        [TestCase("zero-factor")]
        [TestCase("negative-factor")]
        [TestCase("factor-below-one")]
        [TestCase("negative-cap")]
        [TestCase("cap-over-six-hours")]
        public void InvalidBand_IsRejectedByValidationAndJsonParse(string invalid)
        {
            var balance = Configs(Rational.Of(5, 3)).Balance.Clone();
            var band = balance.TargetPaceBands[0];
            switch (invalid)
            {
                case "null-rule": balance.TargetPaceBands[0] = null; break;
                case "empty-name": band.Name = ""; break;
                case "blank-name": band.Name = "  "; break;
                case "zone-zero": band.Zone = 0; break;
                case "zone-too-large": band.Zone = OutpostIds.LastZone + 1; break;
                case "negative-row": band.FromRow = -1; break;
                case "reversed-rows": band.ToRow = band.FromRow - 1; break;
                case "null-kinds": band.Kinds = null; break;
                case "empty-kinds": band.Kinds = ""; break;
                case "gate-kind": band.Kinds = "rxsp"; break;
                case "unknown-kind": band.Kinds = "rxq"; break;
                case "zero-factor": band.HpFactor = Rational.Zero; break;
                case "negative-factor": band.HpFactor = Rational.Of(-1, 1); break;
                case "factor-below-one": band.HpFactor = Rational.Of(9, 10); break;
                case "negative-cap": band.OfflineCapMs = -1; break;
                case "cap-over-six-hours": band.OfflineCapMs = 6 * HourMs + 1; break;
                default: Assert.Fail("Unknown invalid case: " + invalid); break;
            }
            Assert.Throws<OutpostConfigException>(() => balance.Validate(), invalid);
            Assert.Throws<OutpostConfigException>(() => OutpostBalanceConfig.Parse(balance.CanonicalJson()), invalid);
        }

        [TestCase(1, 0L)]
        [TestCase(144, 21600000L)]
        public void ValidBoundaryValues_AreAccepted(int zone, long cap)
        {
            var balance = Configs(Rational.One).Balance.Clone();
            var band = balance.TargetPaceBands[0];
            band.Zone = zone;
            band.FromRow = band.ToRow = 0;
            band.OfflineCapMs = cap;
            Assert.DoesNotThrow(() => balance.Validate());
            Assert.DoesNotThrow(() => OutpostBalanceConfig.Parse(balance.CanonicalJson()));
        }
    }
}
