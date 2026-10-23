using System;
using System.IO;
using CatMiners.Core.Outpost;
using CatMiners.Runtime.Integrations;
using NUnit.Framework;
using UnityEngine;

namespace CatMiners.Tests
{
    public sealed class AppMetricaPrivacyFileMigrationTests
    {
        static void WithPath(Action<string> check)
        {
            string directory = Path.Combine(Application.temporaryCachePath, "privacy-migration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { check(Path.Combine(directory, "privacy.json")); }
            finally { Directory.Delete(directory, true); }
        }
        static void AssertOtherProvidersOff(IntegrationPrivacy privacy)
        {
            Assert.IsFalse(privacy.MayInitialize(privacy.Adjust));
            Assert.IsFalse(privacy.MayInitialize(privacy.Meta));
            Assert.IsFalse(privacy.MayInitialize(privacy.Ads));
        }
        [Test]
        public void MissingFileEnablesBasicMeasurementWithoutChangingAgeOrOptingIntoOtherProviders()
        {
            WithPath(path => {
                var privacy = NeutralAgeFlow.LoadStartupPrivacy(path);
                Assert.IsTrue(privacy.AppMetricaEnabled);
                Assert.AreEqual(NeutralAge.Unknown, privacy.Age);
                AssertOtherProvidersOff(privacy);
                Assert.IsFalse(File.Exists(path), "Automatic startup does not need a privacy dialog or file write.");
            });
        }
        [Test]
        public void LegacySavedExplicitRefusalSurvivesUpgrade()
        {
            WithPath(path => {
                File.WriteAllText(path, "{\"Age\":4,\"ChoicesMade\":true,\"AppMetrica\":false,\"Adjust\":false,\"Meta\":false,\"Ads\":false}");
                Assert.IsFalse(NeutralAgeFlow.LoadStartupPrivacy(path).AppMetricaEnabled);
            });
        }
        [Test]
        public void LegacyProtectedRecordDoesNotRequireOptingIntoBasicMeasurement()
        {
            WithPath(path => {
                File.WriteAllText(path, "{\"Age\":1,\"ChoicesMade\":true,\"AppMetrica\":false}");
                var privacy = NeutralAgeFlow.LoadStartupPrivacy(path);
                Assert.IsTrue(privacy.AppMetricaEnabled);
                AssertOtherProvidersOff(privacy);
            });
        }
        [TestCase(true)]
        [TestCase(false)]
        public void NewExplicitPreferenceTakesPrecedenceOverObsoleteOptInField(bool disabled)
        {
            WithPath(path => {
                File.WriteAllText(path, JsonUtility.ToJson(new IntegrationPrivacy {
                    Age = NeutralAge.Age16OrOlder, ChoicesMade = true,
                    AppMetrica = false, AppMetricaDisabled = disabled
                }));
                Assert.AreEqual(!disabled, NeutralAgeFlow.LoadStartupPrivacy(path).AppMetricaEnabled);
            });
        }
        [Test]
        public void UnfinishedAgeChoiceDoesNotEraseExplicitDisablePreference()
        {
            WithPath(path => {
                File.WriteAllText(path, JsonUtility.ToJson(new IntegrationPrivacy { AppMetricaDisabled = true }));
                var privacy = NeutralAgeFlow.LoadStartupPrivacy(path);
                Assert.IsFalse(privacy.AppMetricaEnabled);
                AssertOtherProvidersOff(privacy);
            });
        }
        [Test]
        public void DamagedPrimaryFileRecoversSavedDisableFromBackup()
        {
            WithPath(path => {
                File.WriteAllText(path, "{broken");
                File.WriteAllText(path + ".bak", JsonUtility.ToJson(new IntegrationPrivacy {
                    Age = NeutralAge.Unknown, ChoicesMade = true, AppMetricaDisabled = true
                }));
                Assert.IsFalse(NeutralAgeFlow.LoadStartupPrivacy(path).AppMetricaEnabled);
            });
        }
        [TestCase("{broken")]
        [TestCase("{\"AppMetricaDisabled\":\"false\"}")]
        public void UnrecoverablePrivacyDataDoesNotOverridePotentiallySavedDisable(string json)
        {
            WithPath(path => {
                File.WriteAllText(path, json);
                Assert.IsFalse(NeutralAgeFlow.LoadStartupPrivacy(path).AppMetricaEnabled);
            });
        }
    }
}
