using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CatMiners.Runtime.Integrations;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;

namespace CatMiners.Editor.GooglePlay
{
    // Inspects installed sources and native declarations; artifact/device/dashboard evidence remains a separate gate.
    public abstract class RuntimeSdkReleaseGate : BuildAndroid.IReleaseGate
    {
        public abstract string Provider { get; }
        protected abstract string PackageId { get; }
        protected abstract string[] SourceMarkers { get; }
        public virtual IEnumerable<string> Validate(string packageName)
        {
            var c = GameSdkConfiguration.Load();
            if (packageName != GameSdkConfiguration.Package || !c.IdentityValid) yield return "Own permanent Android package/config identity is missing.";
            if (!c.LegalValid) yield return "Published game-specific privacy and terms URLs are waiting for configuration.";
            string boot = "Assets/Game/Scripts/Runtime/GameBootstrap.cs";
            if (!File.Exists(boot) || !File.ReadAllText(boot).Contains("NeutralAgeFlow.Begin") || !File.ReadAllText(boot).Contains("GameSdkRuntime.Begin"))
                yield return "Neutral age flow and SDK owner must be called from the live bootstrap.";
            var sdk = "Assets/Game/Scripts/Runtime/Integrations/GameSdkRuntime.cs";
            if (!File.Exists(sdk) || !SourceMarkers.All(m => File.ReadAllText(sdk).Contains(m))) yield return Provider + " concrete runtime API consumers missing.";
            string resolved = null;
            if (PackageId != null)
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + PackageId);
                resolved = info?.resolvedPath;
                if (resolved == null || !Directory.Exists(resolved)) yield return Provider + " installed SDK sources are unavailable.";
                else if (!Directory.GetFiles(resolved, "*Dependencies*.xml", SearchOption.AllDirectories).Any() &&
                         !Directory.GetFiles(resolved, "*.aar", SearchOption.AllDirectories).Any())
                    yield return Provider + " Android native dependency declarations/binaries are missing.";
            }
            foreach (var issue in Specific(c)) yield return issue;
        }
        protected abstract IEnumerable<string> Specific(GameSdkConfiguration c);
        protected static bool MetaFlagOff(string flag)
        {
            var path = "Assets/Plugins/Android/AndroidManifest.xml";
            if (!File.Exists(path)) return false;
            try
            {
                XNamespace android = "http://schemas.android.com/apk/res/android";
                return XDocument.Load(path).Descendants("application").Elements("meta-data").Any(e =>
                    (string)e.Attribute(android + "name") == "com.facebook.sdk." + flag && (string)e.Attribute(android + "value") == "false");
            }
            catch { return false; }
        }
    }
    public sealed class AppMetricaRuntimeGate : RuntimeSdkReleaseGate
    {
        public override string Provider => "AppMetrica";
        protected override string PackageId => "io.appmetrica.analytics";
        protected override string[] SourceMarkers => new[] { "AppMetrica.Activate", "AppMetrica.ReportEvent", "AppMetrica.ReportAdRevenue", "LocationTracking = false", "RevenueAutoTrackingEnabled = false" };
        protected override IEnumerable<string> Specific(GameSdkConfiguration c)
        {
            // Скилл публикации 2026-10-15: AppMetrica как сервис пока недоступна и не блокирует релиз. Пустой ключ — отложено
            // (рантайм без ключа её не активирует: GameSdkRuntime проверяет AppMetricaValid); неверный непустой — ошибка.
            if (!c.AppMetricaValid && !string.IsNullOrWhiteSpace(c.appMetricaKey)) yield return "AppMetrica key is not this game's own client key.";
            var defines = PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Android).Split(';');
            if (defines.Contains("APPMETRICA_FEATURES_ADREVENUE_APPLOVIN_V8")) yield return "Disable AppMetrica automatic MAX ILRD: this game reports revenue manually.";
            string path = "ProjectSettings/AppMetricaSettings.xml";
            bool disabled = false;
            if (File.Exists(path)) try { disabled = XDocument.Load(path).Descendants("appMetricaSetting").Any(e =>
                (string)e.Attribute("name") == "Feature.AppLovinAdRevenueV8.Enabled" && (string)e.Attribute("value") == "False"); } catch { }
            if (!disabled) yield return "Explicitly disable Feature.AppLovinAdRevenueV8.Enabled in AppMetrica settings; package auto-detection can re-enable it.";
        }
    }
    public sealed class AdjustRuntimeGate : RuntimeSdkReleaseGate
    {
        public override string Provider => "Adjust";
        protected override string PackageId => "com.adjust.sdk";
        protected override string[] SourceMarkers => new[] { "Adjust.InitSdk", "AdjustEnvironment.Production", "AttributionChangedDelegate", "SessionSuccessDelegate", "Adjust.TrackAdRevenue" };
        protected override IEnumerable<string> Specific(GameSdkConfiguration c) { if (!c.AdjustValid) yield return "Waiting for own Adjust production token."; }
    }
    public sealed class MetaRuntimeGate : RuntimeSdkReleaseGate
    {
        public override string Provider => "Meta";
        protected override string PackageId => null;
        protected override string[] SourceMarkers => new[] { "FB.Init", "FB.ActivateApp", "FB.LogAppEvent" };
        protected override IEnumerable<string> Specific(GameSdkConfiguration c)
        {
            if (!c.MetaValid) yield return "Waiting for own Meta app ID/client token.";
            if (!File.Exists("Assets/FacebookSDK/Plugins/Facebook.Unity.dll") || !File.Exists("Assets/FacebookSDK/Plugins/Android/Facebook.Unity.Android.dll")) yield return "Meta managed/native bridge binaries missing.";
            foreach (string flag in new[] { "AutoInitEnabled", "AutoLogAppEventsEnabled", "AdvertiserIDCollectionEnabled" })
                if (!MetaFlagOff(flag)) yield return "Meta manifest must explicitly disable " + flag + " before neutral age selection.";
        }
    }
    public sealed class MaxRuntimeGate : RuntimeSdkReleaseGate
    {
        public override string Provider => "MAX";
        public override IEnumerable<string> Validate(string packageName)
        {
            if (GameSdkConfiguration.AdsEnabled)
            {
                foreach (var issue in base.Validate(packageName)) yield return issue;
                yield break;
            }
            // A no-ad release proves exclusion instead of accepting an empty MAX key.
            var manifest = JObject.Parse(File.ReadAllText("Packages/manifest.json"));
            if (manifest["dependencies"]?["com.applovin.mediation.ads"] != null)
                yield return "No-ad release must exclude the MAX package.";
            if (File.ReadAllText("Assets/Plugins/Android/mainTemplate.gradle").Contains("com.applovin"))
                yield return "No-ad release still declares the MAX Android dependency.";
            XNamespace android = "http://schemas.android.com/apk/res/android";
            XNamespace tools = "http://schemas.android.com/tools";
            bool removesAdId = XDocument.Load("Assets/Plugins/Android/AndroidManifest.xml").Root.Elements("uses-permission").Any(e =>
                (string)e.Attribute(android + "name") == "com.google.android.gms.permission.AD_ID" && (string)e.Attribute(tools + "node") == "remove");
            if (!removesAdId) yield return "No-ad release must remove advertising-ID permission from merged library manifests.";
        }
        protected override string PackageId => "com.applovin.mediation.ads";
        protected override string[] SourceMarkers => new[] { "MaxSdk.InitializeSdk", "MaxSdk.ShowRewardedAd", "OnAdReceivedRewardEvent", "OnAdRevenuePaidEvent", "HasSupportedCmp", "IsUserConsentSet" };
        protected override IEnumerable<string> Specific(GameSdkConfiguration c)
        {
            if (!c.MaxValid) yield return "Waiting for verified account SDK key and this game's rewarded unit.";
            var path = "ProjectSettings/AppLovinInternalSettings.json";
            JObject settings = null;
            if (File.Exists(path)) try { settings = JObject.Parse(File.ReadAllText(path)); } catch { }
            if (settings == null || (bool?)settings["consentFlowEnabled"] != true ||
                (string)settings["consentFlowPrivacyPolicyUrl"] != c.privacyUrl || (string)settings["consentFlowTermsOfServiceUrl"] != c.termsUrl)
                yield return "MAX native consent flow must be enabled with the game's published URLs.";
            if ((int?)settings?["debugUserGeography"] != null && (int?)settings?["debugUserGeography"] != 0)
                yield return "MAX debug consent geography must be cleared for release.";
        }
    }
}
