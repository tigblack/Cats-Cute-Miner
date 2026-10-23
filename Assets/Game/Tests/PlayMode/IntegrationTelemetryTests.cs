using System;
using System.IO;
using System.Reflection;
using CatMiners.Core.Outpost;
using CatMiners.Runtime.Integrations;
using NUnit.Framework;

namespace CatMiners.Tests
{
    public sealed class IntegrationTelemetryTests
    {
        const string LegalBase = "https://sites.aimulticast.org/sargsyan/cats-cute-miner-legal/";

        [TestCase(null)]
        [TestCase("{}")]
        [TestCase("{\"Age\":1,\"ChoicesMade\":true}")]
        [TestCase("{\"Age\":2,\"ChoicesMade\":false}")]
        public void NoCompletedAgeChoiceCanConfirmAdAgeInsteadOfRemainingPermanentlyRestricted(string saved)
        {
            if (!GameSdkConfiguration.AdsEnabled) Assert.Ignore("Android ad configuration required.");
            WithPrivacyFile(saved, path => {
                var runtime = GameSdkRuntime.Begin(NeutralAgeFlow.LoadStartupPrivacy(path));
                try
                {
                    Assert.AreEqual(NeutralAge.Unknown, runtime.Privacy.Age);
                    Assert.IsTrue(runtime.Privacy.CanChooseAnalyticsAge);
                    Assert.AreEqual("age", NeutralAgeFlow.EnableRewardedAds(runtime, false, path));
                    Assert.AreEqual(NeutralAge.Unknown, runtime.Privacy.Age, "A tap alone cannot infer age.");
                    Assert.IsNull(NeutralAgeFlow.EnableRewardedAds(runtime, true, path));
                    var restored = NeutralAgeFlow.LoadStartupPrivacy(path);
                    Assert.AreEqual(NeutralAge.Age16OrOlder, restored.Age);
                    Assert.IsTrue(restored.MayInitialize(restored.Ads));
                    Assert.IsFalse(restored.Adjust || restored.Meta);
                }
                finally { UnityEngine.Object.DestroyImmediate(runtime.gameObject); }
            });
        }

        [TestCase(NeutralAge.Unknown, false, false)]
        [TestCase(NeutralAge.Unknown, true, true)]
        [TestCase(NeutralAge.Under13, true, false)]
        [TestCase(NeutralAge.Age13To15, true, false)]
        [TestCase(NeutralAge.Age16OrOlder, false, true)]
        public void RewardButtonEnablesOnlyAdsAfterDurableExplicitAgeChoice(NeutralAge age, bool confirmAdult, bool accepted)
        {
            if (!GameSdkConfiguration.AdsEnabled) Assert.Ignore("Android ad configuration required.");
            WithPrivacyFile(null, path => {
                var privacy = new IntegrationPrivacy { AppMetricaDisabled = true };
                privacy.ChooseAge(age);
                var runtime = GameSdkRuntime.Begin(privacy);
                try
                {
                    string error = NeutralAgeFlow.EnableRewardedAds(runtime, confirmAdult, path);
                    Assert.AreEqual(accepted ? null : "age", error);
                    Assert.AreEqual(accepted, File.Exists(path));
                    Assert.AreEqual(accepted, runtime.Privacy.MayInitialize(runtime.Privacy.Ads));
                    Assert.IsFalse(runtime.Privacy.Adjust || runtime.Privacy.Meta || runtime.Privacy.AppMetrica);
                    Assert.IsTrue(runtime.Privacy.AppMetricaDisabled);
                    Assert.IsFalse(NeutralAgeFlow.IsOpen, "No analytics window is part of an ad request.");
                    if (accepted) Assert.IsTrue(NeutralAgeFlow.LoadStartupPrivacy(path).Ads);
                    else Assert.AreEqual(age, runtime.Privacy.Age);
                }
                finally { UnityEngine.Object.DestroyImmediate(runtime.gameObject); }
            });
        }

        [Test]
        public void RewardButtonSaveFailureDoesNotEnableAdsOrChangeAge()
        {
            if (!GameSdkConfiguration.AdsEnabled) Assert.Ignore("Android ad configuration required.");
            WithPrivacyFile(null, path => {
                var runtime = GameSdkRuntime.Begin(new IntegrationPrivacy { Age = NeutralAge.Unknown });
                try
                {
                    Assert.AreEqual("save", NeutralAgeFlow.EnableRewardedAds(runtime, true, Path.Combine(path, "missing.json")));
                    Assert.AreEqual(NeutralAge.Unknown, runtime.Privacy.Age);
                    Assert.IsFalse(runtime.Privacy.Ads || runtime.Privacy.Adjust || runtime.Privacy.Meta);
                    Assert.IsFalse(runtime.Privacy.MayInitialize(true));
                }
                finally { UnityEngine.Object.DestroyImmediate(runtime.gameObject); }
            });
        }

        [TestCase(true, false, false, true, true)]
        [TestCase(true, false, true, true, false)]
        [TestCase(true, true, true, true, true)]
        [TestCase(true, true, false, false, false)]
        [TestCase(false, true, false, true, false)]
        public void MaxConsentReadinessRespectsRegionAndNeverInventsConsent(bool cmp, bool consent, bool gdpr, bool known, bool expected)
        {
            Assert.AreEqual(expected, GameSdkRuntime.MaxConsentReady(cmp, consent, gdpr, known));
        }

        [Test]
        public void OptionalProviderGrantNeverBackfillsAutomaticAppMetricaEvents()
        {
            WithAnalyticsSettings((runtime, flow, path) =>
            {
                runtime.Track(GameEvent.GameLoaded);
                Assert.AreEqual(2, PendingEvents(runtime));
                AssertPendingRecipients(runtime, 1, 1);
                var draft = Field<IntegrationPrivacy>(flow, "privacy");
                draft.Age = NeutralAge.Age16OrOlder;
                draft.Adjust = draft.ChoicesMade = true;
                Invoke(flow, "Finish");
                Assert.IsFalse(NeutralAgeFlow.IsOpen);
                Assert.IsTrue(runtime.Privacy.AppMetricaEnabled);
                Assert.IsTrue(runtime.Privacy.MayInitialize(runtime.Privacy.Adjust));
                Assert.IsFalse(runtime.Privacy.MayInitialize(runtime.Privacy.Meta));
                Assert.IsFalse(runtime.Privacy.Ads);
                AssertPendingRecipients(runtime, 1, 1, 3);
                runtime.Track(GameEvent.MineStarted);
                AssertPendingRecipients(runtime, 1, 1, 3, 3);
                // Revoking an optional provider discards its backlog, retaining only a new manual choice event.
                runtime.ApplyPrivacy(new IntegrationPrivacy());
                AssertPendingRecipients(runtime, 1);
                runtime.ApplyPrivacy(new IntegrationPrivacy { AppMetricaDisabled = true });
                Assert.AreEqual(0, PendingEvents(runtime));
            });
        }

        [Test]
        public void SavingExplicitAppMetricaOptOutWorksWithoutChoosingAge()
        {
            WithAnalyticsSettings((runtime, flow, path) =>
            {
                Field<IntegrationPrivacy>(flow, "privacy").AppMetricaDisabled = true;
                Invoke(flow, "Finish");
                Assert.IsFalse(runtime.Privacy.AppMetricaEnabled);
                Assert.IsFalse(NeutralAgeFlow.LoadStartupPrivacy(path).AppMetricaEnabled);
                Assert.AreEqual(0, PendingEvents(runtime));
                Assert.IsFalse(runtime.Privacy.MayInitialize(true));
            });
        }

        [Test]
        public void CancellingOptionalSettingsDoesNotSaveOrChangeAutomaticPolicy()
        {
            WithAnalyticsSettings((runtime, flow, path) =>
            {
                Field<IntegrationPrivacy>(flow, "privacy").AppMetricaDisabled = true;
                Invoke(flow, "CloseWithoutSaving");
                AssertProtected(runtime.Privacy);
                Assert.IsTrue(runtime.Privacy.AppMetricaEnabled);
                AssertPendingRecipients(runtime, 1);
                Assert.IsFalse(File.Exists(path));
                Assert.IsFalse(NeutralAgeFlow.IsOpen);
            });
        }

        [Test]
        public void FailedPrivacySaveCannotApplyOptionalProviderChoiceOrOptOut()
        {
            WithAnalyticsSettings((runtime, flow, path) =>
            {
                var draft = Field<IntegrationPrivacy>(flow, "privacy");
                draft.Age = NeutralAge.Age16OrOlder;
                draft.Adjust = draft.ChoicesMade = draft.AppMetricaDisabled = true;
                SetField(flow, "path", Path.Combine(Path.GetDirectoryName(path), "missing", "privacy.json"));
                Invoke(flow, "Finish");
                AssertProtected(runtime.Privacy);
                Assert.IsTrue(runtime.Privacy.AppMetricaEnabled);
                AssertPendingRecipients(runtime, 1);
                Assert.IsTrue(NeutralAgeFlow.IsOpen, "The player can retry or cancel.");
                Assert.IsFalse(File.Exists(path));
            });
        }

        static void WithAnalyticsSettings(Action<GameSdkRuntime, NeutralAgeFlow, string> check)
        {
            WithPrivacyFile(null, path =>
            {
                Assert.IsNull(GameSdkRuntime.Instance);
                var runtime = GameSdkRuntime.Begin(NeutralAgeFlow.LoadStartupPrivacy(path));
                var host = new UnityEngine.GameObject("Optional analytics settings test");
                try
                {
                    NeutralAgeFlow.OpenChoices(host);
                    var flow = host.GetComponent<NeutralAgeFlow>();
                    Assert.IsNotNull(flow);
                    SetField(flow, "path", path);
                    check(runtime, flow, path);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(host);
                    UnityEngine.Object.DestroyImmediate(runtime.gameObject);
                }
                Assert.IsFalse(NeutralAgeFlow.IsOpen);
            });
        }
        static T Field<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static void Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        static int PendingEvents(GameSdkRuntime runtime) =>
            ((System.Collections.ICollection)Field<object>(runtime, "telemetry")).Count;

        static void AssertPendingRecipients(GameSdkRuntime runtime, params int[] expected)
        {
            var actual = new System.Collections.Generic.List<int>();
            foreach (var pending in (System.Collections.IEnumerable)Field<object>(runtime, "telemetry"))
                actual.Add((int)pending.GetType().GetField("Recipients").GetValue(pending));
            CollectionAssert.AreEqual(expected, actual);
        }
        static void AssertPendingKinds(GameSdkRuntime runtime, params GameEvent[] expected)
        {
            var actual = new System.Collections.Generic.List<GameEvent>();
            foreach (var pending in (System.Collections.IEnumerable)Field<object>(runtime, "telemetry"))
                actual.Add((GameEvent)pending.GetType().GetField("Kind").GetValue(pending));
            CollectionAssert.AreEqual(expected, actual);
        }

        [TestCase(NeutralAge.Unselected)]
        [TestCase(NeutralAge.Unknown)]
        [TestCase(NeutralAge.Under13)]
        [TestCase(NeutralAge.Age13To15)]
        [TestCase(NeutralAge.Age16OrOlder)]
        [TestCase((NeutralAge)999)]
        public void AutomaticStartupQueuesSessionAndGameplayForEveryAgeWithoutConsent(NeutralAge age)
        {
            var privacy = new IntegrationPrivacy { Age = age };
            Assert.IsNull(GameSdkRuntime.Instance);
            var runtime = GameSdkRuntime.Begin(privacy);
            try
            {
                runtime.Track(GameEvent.GameLoaded);
                runtime.Track(GameEvent.MineStarted);
                AssertPendingKinds(runtime, GameEvent.SessionStarted, GameEvent.GameLoaded, GameEvent.MineStarted);
                AssertPendingRecipients(runtime, 1, 1, 1);
                Assert.IsNotNull(AppMetricaDiagnostics.Summary);
                Assert.IsNotNull(AppMetricaDiagnostics.Snapshot());
                AppMetricaDiagnostics.RefreshNativeActivation();
                AssertPendingRecipients(runtime, 1, 1, 1);
                Assert.AreEqual(age, privacy.Age);
                Assert.IsFalse(privacy.ChoicesMade);
                Assert.IsFalse(privacy.AppMetrica || privacy.Adjust || privacy.Meta || privacy.Ads);
                Assert.AreEqual(ProviderState.Off, runtime.AppMetricaState, "Editor must never activate native SDKs.");
                Assert.AreEqual(ProviderState.Off, runtime.AdjustState);
                Assert.AreEqual(ProviderState.Off, runtime.MetaState);
                Assert.AreEqual(ProviderState.Off, runtime.MaxState);
            }
            finally { UnityEngine.Object.DestroyImmediate(runtime.gameObject); }
        }

        [TestCase(NeutralAge.Unselected)]
        [TestCase(NeutralAge.Unknown)]
        [TestCase(NeutralAge.Under13)]
        [TestCase(NeutralAge.Age13To15)]
        [TestCase(NeutralAge.Age16OrOlder)]
        public void ExplicitOptOutBlocksAutomaticStartupEvenWithLegacyConsent(NeutralAge age)
        {
            var runtime = GameSdkRuntime.Begin(new IntegrationPrivacy {
                Age = age, ChoicesMade = true, AppMetrica = true, AppMetricaDisabled = true });
            try
            {
                runtime.Track(GameEvent.GameLoaded);
                runtime.Track(GameEvent.MineStarted);
                Assert.AreEqual(0, PendingEvents(runtime));
                Assert.IsFalse(runtime.Privacy.AppMetricaEnabled);
            }
            finally { UnityEngine.Object.DestroyImmediate(runtime.gameObject); }
        }

        [Test]
        public void InPlaceOptOutIsObservedBeforeFlushAndCannotResurrectQueuedEvents()
        {
            var privacy = new IntegrationPrivacy();
            var runtime = GameSdkRuntime.Begin(privacy);
            try
            {
                runtime.Track(GameEvent.GameLoaded);
                privacy.AppMetricaDisabled = true;
                Invoke(runtime, "FlushTelemetry");
                Assert.AreEqual(0, PendingEvents(runtime));
                privacy.AppMetricaDisabled = false;
                runtime.Track(GameEvent.MineStarted);
                AssertPendingKinds(runtime, GameEvent.MineStarted);
                AssertPendingRecipients(runtime, 1);
            }
            finally { UnityEngine.Object.DestroyImmediate(runtime.gameObject); }
        }

        [Test]
        public void ProtectedAgeTransitionDoesNotDiscardStillAllowedBasicEvents()
        {
            var privacy = new IntegrationPrivacy { Age = NeutralAge.Age16OrOlder };
            var runtime = GameSdkRuntime.Begin(privacy);
            try
            {
                runtime.Track(GameEvent.GameLoaded);
                privacy.Age = NeutralAge.Under13;
                Invoke(runtime, "RefreshConsent");
                AssertPendingKinds(runtime, GameEvent.SessionStarted, GameEvent.GameLoaded);
                AssertPendingRecipients(runtime, 1, 1);
            }
            finally { UnityEngine.Object.DestroyImmediate(runtime.gameObject); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OptOutOrMissingPrivacyDiscardsBacklogAndClockWithoutReplayingOnEnable(bool missingPrivacy)
        {
            var runtime = GameSdkRuntime.Begin(new IntegrationPrivacy());
            try
            {
                var clock = Field<ForegroundMinuteClock>(runtime, "gameplayClock");
                clock.Sample(0, true);
                clock.Sample(59, true);
                runtime.ApplyPrivacy(missingPrivacy ? null : new IntegrationPrivacy { AppMetricaDisabled = true });
                Assert.AreEqual(0, PendingEvents(runtime));
                runtime.Track(GameEvent.MineCompleted);
                Assert.AreEqual(0, PendingEvents(runtime));
                runtime.ApplyPrivacy(new IntegrationPrivacy());
                AssertPendingKinds(runtime, GameEvent.ConsentChosen);
                AssertPendingRecipients(runtime, 1);
                Assert.AreEqual(0, clock.Sample(1000, true));
                Assert.AreEqual(0, clock.Sample(1059, true));
                Assert.AreEqual(1, clock.Sample(1060, true));
            }
            finally { UnityEngine.Object.DestroyImmediate(runtime.gameObject); }
        }

        [Test]
        public void NullPrivacyStartsClosedAndDiagnosticsCannotEnableAnyProvider()
        {
            var runtime = GameSdkRuntime.Begin(null);
            try
            {
                runtime.Track(GameEvent.GameLoaded);
                var summary = AppMetricaDiagnostics.Summary;
                var lines = AppMetricaDiagnostics.Snapshot();
                AppMetricaDiagnostics.RefreshNativeActivation();
                Assert.AreEqual(0, PendingEvents(runtime));
                Assert.AreEqual(ProviderState.Off, runtime.AppMetricaState);
                Assert.AreEqual(ProviderState.Off, runtime.AdjustState);
                Assert.AreEqual(ProviderState.Off, runtime.MetaState);
                Assert.AreEqual(ProviderState.Off, runtime.MaxState);
                StringAssert.Contains("server_delivery=unverified", summary);
                StringAssert.DoesNotContain("consent=", summary);
                Assert.IsNotNull(lines);
            }
            finally { UnityEngine.Object.DestroyImmediate(runtime.gameObject); }
        }

        [Test]
        public void StartupContinuesImmediatelyWithoutCreatingPrivacyScreens()
        {
            var host = new UnityEngine.GameObject("Startup privacy test");
            try
            {
                int calls = 0;
                NeutralAgeFlow.Begin(host, privacy => { calls++; AssertProtected(privacy); });
                Assert.AreEqual(1, calls);
                Assert.IsFalse(NeutralAgeFlow.IsOpen);
                Assert.IsNull(host.GetComponent<NeutralAgeFlow>());
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [TestCase(null, true)]
        [TestCase("", false)]
        [TestCase("broken JSON", false)]
        [TestCase("null", false)]
        [TestCase("{}", true)]
        [TestCase("{\"Age\":999,\"ChoicesMade\":true,\"AppMetrica\":true,\"Adjust\":true,\"Meta\":true,\"Ads\":true}", true)]
        [TestCase("{\"Age\":0,\"ChoicesMade\":true,\"AppMetrica\":true}", true)]
        [TestCase("{\"Age\":4,\"ChoicesMade\":false,\"AppMetrica\":true,\"Adjust\":true,\"Meta\":true,\"Ads\":true}", true)]
        public void StartupDistinguishesNewInstallFromUnreadablePrivacyWithoutEnablingOtherServices(string contents, bool basicEnabled)
        {
            WithPrivacyFile(contents, path =>
            {
                var privacy = NeutralAgeFlow.LoadStartupPrivacy(path);
                Assert.AreEqual(NeutralAge.Unknown, privacy.Age);
                AssertProtected(privacy);
                Assert.AreEqual(basicEnabled, privacy.AppMetricaEnabled);
                if (contents == null) Assert.IsFalse(File.Exists(path));
                else Assert.AreEqual(contents, File.ReadAllText(path));
            });
        }

        [TestCase(NeutralAge.Unknown)]
        [TestCase(NeutralAge.Under13)]
        [TestCase(NeutralAge.Age13To15)]
        public void ProtectedSavedAgeCannotActivateStaleAnalyticsChoices(NeutralAge age)
        {
            var saved = new IntegrationPrivacy { Age = age, ChoicesMade = true,
                AppMetrica = true, Adjust = true, Meta = true, Ads = true };
            WithPrivacyFile(UnityEngine.JsonUtility.ToJson(saved), path =>
            {
                var privacy = NeutralAgeFlow.LoadStartupPrivacy(path);
                Assert.AreEqual(age, privacy.Age);
                AssertProtected(privacy);
            });
        }

        [Test]
        public void StartupPreservesExistingCompletedChoicesWithoutEnablingOtherProviders()
        {
            var saved = new IntegrationPrivacy { Age = NeutralAge.Age16OrOlder, ChoicesMade = true,
                AppMetrica = true, Adjust = false, Meta = false, Ads = true };
            WithPrivacyFile(UnityEngine.JsonUtility.ToJson(saved), path =>
            {
                var privacy = NeutralAgeFlow.LoadStartupPrivacy(path);
                Assert.IsTrue(privacy.MayInitialize(privacy.AppMetrica));
                Assert.IsFalse(privacy.MayInitialize(privacy.Adjust));
                Assert.IsFalse(privacy.MayInitialize(privacy.Meta));
                Assert.AreEqual(GameSdkConfiguration.AdsEnabled && saved.Ads, privacy.Ads);
                Assert.AreEqual(UnityEngine.JsonUtility.ToJson(saved), File.ReadAllText(path));
            });
        }

        static void AssertProtected(IntegrationPrivacy privacy)
        {
            Assert.IsTrue(privacy.Protected);
            Assert.IsFalse(privacy.AppMetrica);
            Assert.IsFalse(privacy.Adjust);
            Assert.IsFalse(privacy.Meta);
            Assert.IsFalse(privacy.Ads);
            Assert.IsFalse(privacy.MayInitialize(true));
        }

        static void WithPrivacyFile(string contents, Action<string> check)
        {
            var folder = Path.Combine(Directory.GetParent(UnityEngine.Application.dataPath).FullName,
                "build", "privacy-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "privacy.json");
            try
            {
                if (contents != null) File.WriteAllText(path, contents);
                check(path);
            }
            finally { Directory.Delete(folder, true); }
        }

        [TestCase(LegalBase + "privacy/", LegalBase + "terms/", true)]
        [TestCase("https://multicastgames.com/cats-cute-miner/privacy/", "https://multicastgames.com/cats-cute-miner/terms/", true)]
        [TestCase("", "", false)]
        [TestCase("https://multicastgames.com/policy", "https://multicastgames.com/termsofuse", false)]
        [TestCase(LegalBase + "terms/", LegalBase + "privacy/", false)]
        [TestCase(LegalBase + "privacy/", LegalBase + "privacy/", false)]
        [TestCase("https://sites.aimulticast.org/other/cats-cute-miner-legal/privacy/", LegalBase + "terms/", false)]
        [TestCase("https://sites.aimulticast.org.attacker.invalid/sargsyan/cats-cute-miner-legal/privacy/", LegalBase + "terms/", false)]
        [TestCase(LegalBase + "privacy/?redirect=other", LegalBase + "terms/", false)]
        [TestCase(LegalBase + "privacy/#draft", LegalBase + "terms/", false)]
        [TestCase("http://sites.aimulticast.org/sargsyan/cats-cute-miner-legal/privacy/", LegalBase + "terms/", false)]
        public void LegalDocumentsRequireOwnedCanonicalPagesAndCorrectRoles(string privacy, string terms, bool expected)
        {
            Assert.AreEqual(expected, new GameSdkConfiguration { privacyUrl = privacy, termsUrl = terms }.LegalValid);
        }

        [Test]
        public void ClientResourceContainsPublishedDocumentsAndMatchesPlatformAdvertisingSupport()
        {
            var config = GameSdkConfiguration.Load();
            Assert.IsTrue(config.IdentityValid);
            Assert.AreEqual(LegalBase + "privacy/", config.privacyUrl);
            Assert.AreEqual(LegalBase + "terms/", config.termsUrl);
            Assert.IsTrue(config.LegalValid);
            if (GameSdkConfiguration.AdsEnabled) Assert.IsTrue(config.MaxValid, "Android MAX builds require a valid provider configuration.");
            else Assert.IsFalse(config.MaxValid, "Platforms without ads must not activate MAX.");
        }

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
