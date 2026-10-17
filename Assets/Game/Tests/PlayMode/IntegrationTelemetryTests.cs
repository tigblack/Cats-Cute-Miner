using CatMiners.Runtime.Integrations;
using NUnit.Framework;

namespace CatMiners.Tests
{
    public sealed class IntegrationTelemetryTests
    {
        [Test]
        public void NoAdsReleaseClearsStaleConsentAndNeverIssuesRewardCallbacks()
        {
            if (GameSdkConfiguration.AdsEnabled) Assert.Ignore("Applies to the approved release without ads.");
            Assert.IsNull(GameSdkRuntime.Instance);
            var saved = new CatMiners.Core.Outpost.IntegrationPrivacy {
                Age = CatMiners.Core.Outpost.NeutralAge.Age16OrOlder, ChoicesMade = true, Ads = true
            };
            var runtime = GameSdkRuntime.Begin(saved);
            try
            {
                Assert.IsFalse(saved.Ads, "An older saved opt-in must not activate deferred ads.");
                Assert.AreEqual(ProviderState.Off, runtime.MaxState);
                Assert.IsFalse(runtime.RewardReady);
                int earned = 0, closed = 0;
                Assert.IsFalse(runtime.ShowReward("no_ads_offer", () => earned++, () => closed++));
                Assert.AreEqual(0, earned);
                Assert.AreEqual(0, closed);
                Assert.IsFalse(runtime.HasEarned("no_ads_offer"));
                var changed = new CatMiners.Core.Outpost.IntegrationPrivacy {
                    Age = CatMiners.Core.Outpost.NeutralAge.Age16OrOlder, ChoicesMade = true, Ads = true
                };
                runtime.ApplyPrivacy(changed);
                Assert.IsFalse(changed.Ads, "Reopening preferences cannot enable an absent provider.");
                Assert.IsFalse(runtime.Config.MaxValid);
            }
            finally { UnityEngine.Object.DestroyImmediate(runtime.gameObject); }
        }

        [Test]
        public void MinuteClockRetainsFractionAcrossBackgroundAndRewardedIntervals()
        {
            var clock = new ForegroundMinuteClock();
            Assert.AreEqual(0, clock.Sample(0, true));
            Assert.AreEqual(0, clock.Sample(40, true));
            Assert.AreEqual(0, clock.Sample(40, false));
            Assert.AreEqual(0, clock.Sample(400, false));
            Assert.AreEqual(0, clock.Sample(400, true));
            Assert.AreEqual(0, clock.Sample(419, true));
            Assert.AreEqual(1, clock.Sample(420, true));
            Assert.AreEqual(0, clock.Sample(420, false));
            Assert.AreEqual(0, clock.Sample(900, false));
            Assert.AreEqual(0, clock.Sample(900, true));
            Assert.AreEqual(1, clock.Sample(960, true));
        }
        [Test]
        public void MinuteClockDoesNotBackfillRevokedConsentOrRoundUpPartialMinute()
        {
            var clock = new ForegroundMinuteClock();
            clock.Sample(0, true);
            Assert.AreEqual(0, clock.Sample(59.5, true));
            clock.Revoke();
            Assert.AreEqual(0, clock.Sample(1000, true));
            Assert.AreEqual(0, clock.Sample(1059.5, true));
            Assert.AreEqual(1, clock.Sample(1060, true));
        }
        [Test]
        public void EventTokensFailClosedForDuplicatesRevenueReuseAndInvalidSyntax()
        {
            var config = new GameSdkConfiguration { adjustPurchaseEventToken = "abc123" };
            config.adjustEvents = new[] { Binding("game_loaded", "def456") };
            Assert.AreEqual("def456", config.EventToken("game_loaded"));
            Assert.IsNull(config.EventToken("mine_started"));
            foreach (var invalid in new[] { "", "def456\n", "ABC123", "abcdefg", "abc123" })
            {
                config.adjustEvents = new[] { Binding("game_loaded", invalid) };
                Assert.IsNull(config.EventToken("game_loaded"));
            }
            config.adjustEvents = new[] { Binding("game_loaded", "def456"), Binding("game_loaded", "ghi789") };
            Assert.IsNull(config.EventToken("game_loaded"));
            config.adjustEvents = new[] { Binding("game_loaded", "def456"), Binding("mine_started", "def456") };
            Assert.IsNull(config.EventToken("game_loaded"));
            foreach (var excluded in new[] { "purchase", "ad_revenue", "playtime_seconds_event" })
            {
                config.adjustEvents = new[] { Binding(excluded, "def456") };
                Assert.IsNull(config.EventToken(excluded));
            }
        }
        static GameSdkConfiguration.AdjustEventBinding Binding(string name, string token) =>
            new GameSdkConfiguration.AdjustEventBinding { name = name, token = token };
    }
}
