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
            if (!c.LegalValid && !BuildAndroid.LocalArtifactOnly) yield return "Published game-specific privacy and terms URLs are waiting for configuration.";
            string boot = "Assets/Game/Scripts/Runtime/GameBootstrap.cs";
            if (!File.Exists(boot) || !File.ReadAllText(boot).Contains("NeutralAgeFlow.Begin") || !File.ReadAllText(boot).Contains("GameSdkRuntime.Begin"))
                yield return "Neutral age flow and SDK owner must be called from the live bootstrap.";
            var sdk = "Assets/Game/Scripts/Runtime/Integrations/GameSdkRuntime.cs";
            var runtimeSources = Directory.GetFiles("Assets/Game/Scripts/Runtime/Integrations", "GameSdkRuntime*.cs")
                .Select(File.ReadAllText);
            string runtime = string.Join("\n", runtimeSources);
            if (!File.Exists(sdk) || !SourceMarkers.All(runtime.Contains)) yield return Provider + " concrete runtime API consumers missing.";
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
            if (!GameSdkConfiguration.AdsEnabled)
            {
                foreach (var issue in ValidateNoAds()) yield return issue;
                yield break;
            }
            var defines = PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Android).Split(';');
            if (!defines.Contains("CATMINERS_MAX")) yield return "Rewarded Android release requires CATMINERS_MAX.";
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                yield return "Select Android before validating rewarded runtime configuration.";
            var manifest = JObject.Parse(File.ReadAllText("Packages/manifest.json"));
            if ((string)manifest["dependencies"]?["com.applovin.mediation.ads"] != "8.6.6")
                yield return "MAX must use the reviewed official 8.6.6 package pin.";
            if (!(manifest["scopedRegistries"] as JArray ?? new JArray()).OfType<JObject>().Any(r =>
                (string)r["url"] == "https://unity.packages.applovin.com" &&
                (r["scopes"] as JArray ?? new JArray()).Values<string>().Contains("com.applovin.mediation")))
                yield return "MAX official AppLovin UPM registry is missing.";
            var installed = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.applovin.mediation.ads");
            if (installed != null && installed.version != "8.6.6") yield return "Resolved MAX version differs from the reviewed 8.6.6 pin.";
            var assembly = JObject.Parse(File.ReadAllText("Assets/Game/Scripts/Runtime/CatMiners.Runtime.asmdef"));
            if (!(assembly["references"] as JArray ?? new JArray()).Values<string>().Contains("MaxSdk.Scripts"))
                yield return "Runtime assembly must reference MaxSdk.Scripts.";
            string gradle = "Assets/Plugins/Android/mainTemplate.gradle";
            if (!File.Exists(gradle) || !File.ReadAllText(gradle).Contains("com.applovin:applovin-sdk:13.6.4"))
                yield return "Run EDM4U Android Force Resolve: pinned MAX Android SDK 13.6.4 is missing from the Gradle template.";
            XNamespace android = "http://schemas.android.com/apk/res/android";
            XNamespace tools = "http://schemas.android.com/tools";
            string nativeManifest = "Assets/Plugins/Android/AndroidManifest.xml";
            if (!File.Exists(nativeManifest)) yield return "Android manifest is missing.";
            else if (XDocument.Load(nativeManifest).Root.Elements("uses-permission").Any(e =>
                (string)e.Attribute(android + "name") == "com.google.android.gms.permission.AD_ID" &&
                (string)e.Attribute(tools + "node") == "remove"))
                yield return "Apply native project configuration to retire the previous no-ads AD_ID merge veto; review the final merged permissions.";
            foreach (var issue in base.Validate(packageName)) yield return issue;
        }
        internal static IEnumerable<string> ValidateNoAds()
        {
            if (PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Android).Split(';').Contains("CATMINERS_MAX"))
                yield return "Remove CATMINERS_MAX from the first release without ads.";
            var manifest = JObject.Parse(File.ReadAllText("Packages/manifest.json"));
            if (((JObject)manifest["dependencies"]).Properties().Any(p => p.Name.StartsWith("com.applovin.mediation", StringComparison.Ordinal)))
                yield return "Remove MAX packages from the first release.";
            var assembly = JObject.Parse(File.ReadAllText("Assets/Game/Scripts/Runtime/CatMiners.Runtime.asmdef"));
            if (((JArray)assembly["references"]).Values<string>().Contains("MaxSdk.Scripts"))
                yield return "The runtime assembly must not reference MAX in the first release.";
            foreach (string name in new[] { "Assets/Plugins/Android/mainTemplate.gradle", "ProjectSettings/AndroidResolverDependencies.xml" })
                if (File.Exists(name) && File.ReadAllText(name).Contains("com.applovin:"))
                    yield return "Remove native AppLovin dependencies from " + name + ".";
            if (Directory.Exists("Assets/MaxSdk") || Directory.Exists("Assets/AppLovin") ||
                AssetDatabase.FindAssets("AppLovinSettings t:ScriptableObject").Length != 0)
                yield return "Keep deferred MAX assets outside Assets for the first release.";
            XNamespace android = "http://schemas.android.com/apk/res/android";
            XNamespace tools = "http://schemas.android.com/tools";
            const string path = "Assets/Plugins/Android/AndroidManifest.xml";
            if (!File.Exists(path) || !XDocument.Load(path).Root.Elements("uses-permission").Any(e =>
                (string)e.Attribute(android + "name") == "com.google.android.gms.permission.AD_ID" &&
                (string)e.Attribute(tools + "node") == "remove"))
                yield return "The release without ads must exclude the advertising ID permission.";
        }
        protected override string PackageId => "com.applovin.mediation.ads";
        protected override string[] SourceMarkers => new[] { "MaxSdk.InitializeSdk", "MaxSdk.ShowRewardedAd", "OnAdReceivedRewardEvent", "OnAdRevenuePaidEvent", "HasSupportedCmp", "IsUserConsentSet" };
        protected override IEnumerable<string> Specific(GameSdkConfiguration c)
        {
            if (!c.MaxValid) yield return "Waiting for verified account SDK key and this game's rewarded unit.";
            // Inspect the serialized official asset without creating it or logging client keys.
            var maxAssets = AssetDatabase.FindAssets("AppLovinSettings t:ScriptableObject");
            if (maxAssets.Length != 1) yield return "Configure exactly one official AppLovinSettings asset in MAX Integration Manager.";
            else
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(maxAssets[0]));
                var serialized = new SerializedObject(asset);
                if (serialized.FindProperty("sdkKey")?.stringValue != c.maxSdkKey || !c.MaxValid)
                    yield return "MAX Integration Manager SDK key must match this game's verified runtime SDK key.";
                // Google UMP needs this ID even when no Google ad adapter is installed.
                if (!c.AdMobAppIdValid || serialized.FindProperty("adMobAndroidAppId")?.stringValue != c.admobAndroidAppId)
                    yield return "MAX Google UMP requires the game's own AdMob Android app ID in Integration Manager.";
            }
            XNamespace android = "http://schemas.android.com/apk/res/android";
            var native = XDocument.Load("Assets/Plugins/Android/AndroidManifest.xml");
            if (!c.AdMobAppIdValid || !native.Root.Element("application").Elements("meta-data").Any(e =>
                (string)e.Attribute(android + "name") == "com.google.android.gms.ads.APPLICATION_ID" &&
                (string)e.Attribute(android + "value") == c.admobAndroidAppId))
                yield return "Android manifest must provide the game's own AdMob app ID for Google UMP.";
            // In 8.6.6 this enabled flow automatically includes Google UMP. Runtime
            // additionally requires HasSupportedCmp and IsUserConsentSet before loading.
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
