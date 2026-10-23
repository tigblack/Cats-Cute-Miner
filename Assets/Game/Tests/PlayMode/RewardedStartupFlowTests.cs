using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CatMiners.Core.Outpost;
using CatMiners.Runtime.Integrations;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace CatMiners.Tests
{
    public sealed class RewardedStartupFlowTests
    {
        static T Field<T>(object target, string name) => (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        static IntegrationPrivacy Unknown()
        {
            var privacy = new IntegrationPrivacy { AppMetricaDisabled = true };
            privacy.ChooseAge(NeutralAge.Unknown);
            return privacy;
        }

        static void WithFlow(IntegrationPrivacy privacy,
            Action<RewardedStartupFlow, string, Func<IntegrationPrivacy>, Func<int>> check, bool brokenPath = false)
        {
            if (!GameSdkConfiguration.AdsEnabled) Assert.Ignore("Android ad configuration required.");
            string folder = Path.Combine(Application.temporaryCachePath, "startup-ads-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, brokenPath ? "missing/privacy.json" : "privacy.json");
            var previousEvents = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            var host = new GameObject("Startup consent test");
            GameObject canvas = null;
            IntegrationPrivacy selected = null;
            int calls = 0;
            try
            {
                var flow = RewardedStartupFlow.Begin(host, privacy, choice => {
                    calls++;
                    selected = choice;
                    if (choice.Ads)
                    {
                        Assert.IsTrue(File.Exists(path), "The choice is durable before SDK/game startup.");
                        Assert.IsTrue(NeutralAgeFlow.LoadStartupPrivacy(path).Ads);
                    }
                }, path);
                canvas = Field<GameObject>(flow, "canvasObject");
                check(flow, path, () => selected, () => calls);
            }
            finally
            {
                Object.DestroyImmediate(host);
                if (canvas != null) Object.DestroyImmediate(canvas);
                foreach (var item in Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include))
                    if (!previousEvents.Contains(item)) Object.DestroyImmediate(item.gameObject);
                Directory.Delete(folder, true);
            }
            Assert.IsFalse(RewardedStartupFlow.IsOpen);
        }

        [Test]
        public void FirstLaunchWaitsForChoice_ConfirmSavesAdsBeforeStartingGame_OnlyOnce()
        {
            var original = Unknown();
            WithFlow(original, (flow, path, selected, calls) => {
                Assert.AreEqual(0, calls());
                Assert.IsTrue(RewardedStartupFlow.IsOpen);
                Assert.IsFalse(NeutralAgeFlow.IsOpen, "The analytics IMGUI window is not used.");
                Assert.IsFalse(File.Exists(path));
                var dialog = Field<OutpostDialog>(flow, "dialog");
                Assert.IsTrue(dialog.Visible);
                string text = string.Join(" ", dialog.Root.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Select(t => t.text));
                StringAssert.Contains("Privacy & ads", text);
                StringAssert.Contains("16 or older", text);
                Assert.IsNotNull(dialog.Root.Find("Box/body/extra/PrivacyPolicy"));
                dialog.ButtonAt(1).Button.onClick.Invoke();
                Assert.AreEqual(1, calls());
                Assert.IsTrue(selected().Ads && selected().RewardedStartupChoiceMade);
                Assert.AreEqual(NeutralAge.Age16OrOlder, selected().Age);
                Assert.IsFalse(selected().Adjust || selected().Meta || selected().AppMetrica);
                Assert.IsTrue(selected().AppMetricaDisabled);
                Assert.IsFalse(original.Ads, "A working copy is saved before changing live preferences.");
                dialog.ButtonAt(1).Button.onClick.Invoke();
                Assert.AreEqual(1, calls());
                Assert.IsFalse(RewardedStartupFlow.NeedsChoice(NeutralAgeFlow.LoadStartupPrivacy(path)));
            });
        }

        [Test]
        public void SkipStartsGameWithoutAdsOrInferredAge_AndDoesNotPromptAgain()
        {
            WithFlow(Unknown(), (flow, path, selected, calls) => {
                Field<OutpostDialog>(flow, "dialog").ButtonAt(0).Button.onClick.Invoke();
                Assert.AreEqual(1, calls());
                Assert.AreEqual(NeutralAge.Unknown, selected().Age);
                Assert.IsFalse(selected().Ads || selected().Adjust || selected().Meta);
                var saved = NeutralAgeFlow.LoadStartupPrivacy(path);
                Assert.IsTrue(saved.RewardedStartupChoiceMade);
                Assert.IsFalse(RewardedStartupFlow.NeedsChoice(saved));
            });
        }

        [Test]
        public void FailedConfirmCannotStartGameOrGrantAds_SkipStillAllowsGameplay()
        {
            WithFlow(Unknown(), (flow, path, selected, calls) => {
                var dialog = Field<OutpostDialog>(flow, "dialog");
                dialog.ButtonAt(1).Button.onClick.Invoke();
                Assert.AreEqual(0, calls());
                Assert.IsFalse(File.Exists(path));
                Assert.IsTrue(dialog.Visible);
                dialog.ButtonAt(0).Button.onClick.Invoke();
                Assert.AreEqual(1, calls());
                Assert.IsFalse(selected().Ads);
                Assert.AreEqual(NeutralAge.Unknown, selected().Age);
                Assert.IsFalse(selected().RewardedStartupChoiceMade, "No persisted refusal is invented after failed storage.");
            }, true);
        }

        [TestCase(NeutralAge.Under13)]
        [TestCase(NeutralAge.Age13To15)]
        public void EstablishedProtectedAgeIsNeverPromotedAtStartup(NeutralAge age)
        {
            var privacy = Unknown();
            privacy.ChooseAge(age);
            WithFlow(privacy, (flow, path, selected, calls) => {
                Assert.AreEqual(1, calls());
                Assert.AreEqual(age, selected().Age);
                Assert.IsFalse(selected().Ads);
                Assert.IsFalse(File.Exists(path));
                Assert.IsNull(Field<GameObject>(flow, "canvasObject"));
            });
        }

        [TestCase(ProviderState.Off, true)]
        [TestCase(ProviderState.WaitingForConfiguration, true)]
        [TestCase(ProviderState.Starting, true)]
        [TestCase(ProviderState.Failed, true)]
        [TestCase(ProviderState.Initialized, false)]
        public void EnabledAdsDeferOtherProvidersUntilNativeConsentCompletes(ProviderState state, bool awaiting)
        {
            var privacy = new IntegrationPrivacy { Age = NeutralAge.Age16OrOlder, ChoicesMade = true, Ads = true };
            Assert.AreEqual(awaiting, GameSdkRuntime.AwaitingAdConsent(privacy, state));
            privacy.Ads = false;
            Assert.IsFalse(GameSdkRuntime.AwaitingAdConsent(privacy, state));
        }
    }
}
