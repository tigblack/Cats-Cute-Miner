using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using CatMiners.Runtime.Integrations;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace CatMiners.Editor.GooglePlay
{
    // Applies accepted public identity and child-safe native startup settings only.
    public static class GooglePlayProjectConfiguration
    {
        [MenuItem("Cat Miners/Google Play/Apply approved identity and native privacy")]
        public static void Apply()
        {
            var config = GameSdkConfiguration.Load();
            if (!config.IdentityValid) throw new InvalidOperationException("Accepted game identity/configuration missing.");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, GameSdkConfiguration.Package);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, GameSdkConfiguration.Package);
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            if (!GameSdkConfiguration.AdsEnabled)
                foreach (var target in new[] { NamedBuildTarget.Android, NamedBuildTarget.Standalone, NamedBuildTarget.WebGL })
                {
                    var symbols = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(x => x != "CATMINERS_MAX");
                    PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
                }
            GooglePlayIcons.Apply();

            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var entry = settings.FindProperty("androidApplicationEntry");
            if (entry == null) throw new InvalidOperationException("Unity Android entry selection missing.");
            int entryFlags = entry.intValue;
            string engine = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None);
            string output = "Assets/Plugins/Android";
            Directory.CreateDirectory(output);
            foreach (var pair in new[] {
                new[] { "mainTemplate.gradle", "useCustomMainGradleTemplate" },
                new[] { "gradleTemplate.properties", "useCustomGradlePropertiesTemplate" },
                new[] { "settingsTemplate.gradle", "useCustomGradleSettingsTemplate" }
            })
            {
                string destination = Path.Combine(output, pair[0]);
                if (!File.Exists(destination))
                    File.Copy(Path.Combine(engine, "Tools", "GradleTemplates", pair[0]), destination);
                SetFlag(settings, pair[1]);
            }

            string manifest = Path.Combine(output, "AndroidManifest.xml");
            var xml = XDocument.Load(File.Exists(manifest) ? manifest : Path.Combine(engine, "Apk", "UnityManifest.xml"));
            var app = xml.Root?.Element("application") ?? throw new InvalidOperationException("Android application element missing.");
            XNamespace android = "http://schemas.android.com/apk/res/android";
            XNamespace tools = "http://schemas.android.com/tools";
            xml.Root.SetAttributeValue(XNamespace.Xmlns + "tools", tools.NamespaceName);
            foreach (var activity in app.Elements("activity").ToArray())
            {
                string name = (string)activity.Attribute(android + "name");
                if ((name == "com.unity3d.player.UnityPlayerActivity" && (entryFlags & 1) == 0) ||
                    (name == "com.unity3d.player.UnityPlayerGameActivity" && (entryFlags & 2) == 0))
                { activity.Remove(); continue; }
                if (name == "com.unity3d.player.UnityPlayerActivity" || name == "com.unity3d.player.UnityPlayerGameActivity")
                    activity.SetAttributeValue(android + "exported", "true");
            }
            foreach (string name in new[] { "AutoInitEnabled", "AutoLogAppEventsEnabled", "AdvertiserIDCollectionEnabled" })
            {
                var element = app.Elements("meta-data").FirstOrDefault(x => (string)x.Attribute(android + "name") == "com.facebook.sdk." + name);
                if (element == null) { element = new XElement("meta-data"); app.Add(element); }
                element.SetAttributeValue(android + "name", "com.facebook.sdk." + name);
                element.SetAttributeValue(android + "value", "false");
                element.SetAttributeValue(tools + "replace", "android:value");
            }
            var adId = xml.Root.Elements("uses-permission").Where(e =>
                (string)e.Attribute(android + "name") == "com.google.android.gms.permission.AD_ID").ToArray();
            foreach (var permission in adId) permission.Remove();
            if (GameSdkConfiguration.AdsEnabled)
            {
                if (!config.AdMobAppIdValid)
                    throw new InvalidOperationException("The game's own AdMob Android app ID is required for Google UMP consent.");
                var googleId = app.Elements("meta-data").FirstOrDefault(e =>
                    (string)e.Attribute(android + "name") == "com.google.android.gms.ads.APPLICATION_ID");
                if (googleId == null) { googleId = new XElement("meta-data"); app.Add(googleId); }
                googleId.SetAttributeValue(android + "name", "com.google.android.gms.ads.APPLICATION_ID");
                googleId.SetAttributeValue(android + "value", config.admobAndroidAppId);
            }
            if (!GameSdkConfiguration.AdsEnabled)
                xml.Root.AddFirst(new XElement("uses-permission",
                    new XAttribute(android + "name", "com.google.android.gms.permission.AD_ID"),
                    new XAttribute(tools + "node", "remove")));
            xml.Save(manifest);
            SetFlag(settings, "useCustomMainManifest");
            settings.ApplyModifiedPropertiesWithoutUndo();

            // Exact official 6.7.0 editor API; this is settings configuration, not a runtime SDK substitute.
            Type appMetrica = FindType("Io.AppMetrica.Editor.AppMetricaSettings");
            MethodInfo setBool = appMetrica?.GetMethod("SetBool", BindingFlags.Public | BindingFlags.Static);
            if (setBool == null) throw new InvalidOperationException("Resolved AppMetrica settings API missing.");
            setBool.Invoke(null, new object[] { "Feature.AppLovinAdRevenueV8.Enabled", false });
            foreach (var target in new[] { NamedBuildTarget.Android, NamedBuildTarget.Standalone, NamedBuildTarget.WebGL })
            {
                var symbols = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(x => x != "APPMETRICA_FEATURES_ADREVENUE_APPLOVIN_V8");
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
            }

            ApplyMetaSettings(config);
            if (Application.isBatchMode) PrepareMetaAndroidEditorTools();
            if (GameSdkConfiguration.AdsEnabled) ApplyMaxConsentSettings(config);
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("release/google-play/evidence");
            File.WriteAllText("release/google-play/evidence/native-project-configuration.json", JsonConvert.SerializeObject(new {
                package = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
                reservedIosBundle = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS), targetApi = 36,
                architectures = PlayerSettings.Android.targetArchitectures.ToString(),
                metaAutomaticStartup = false, metaAutomaticEvents = false, metaAdvertisingId = false,
                appMetricaAutomaticMaxRevenue = false, versionChanged = false,
                status = "source_settings_applied_pending_merged_manifest_and_device_verification"
            }, Formatting.Indented));
            Debug.Log("[GooglePlay] Accepted identity and protected native settings applied; artifact verification remains pending.");
        }

        // Runtime supplies the same public app identity to FB.Init. Editor build hooks need it too.
        [MenuItem("Cat Miners/Maintenance/Configure Meta SDK from game settings")]
        public static void ConfigureMeta()
        {
            var config = GameSdkConfiguration.Load();
            if (!config.MetaValid) throw new InvalidOperationException("Game Meta SDK configuration is incomplete.");
            ApplyMetaSettings(config);
            AssetDatabase.SaveAssets();
        }

        static void ApplyMetaSettings(GameSdkConfiguration config)
        {
            // Exact public Meta 18.1.1 settings properties; preserve another configured application.
            Type meta = FindType("Facebook.Unity.Settings.FacebookSettings");
            if (meta == null) throw new InvalidOperationException("Resolved Meta settings API missing.");
            var current = (IEnumerable<string>)meta.GetProperty("AppIds").GetValue(null);
            if (current.Any(x => !string.IsNullOrEmpty(x) && x != "0" && x != config.metaAppId))
                throw new InvalidOperationException("Preserving different existing Meta app settings; select explicitly.");
            SetProperty(meta, "AutoLogAppEventsEnabled", false);
            SetProperty(meta, "AdvertiserIDCollectionEnabled", false);
            if (config.MetaValid)
            {
                SetProperty(meta, "AppIds", new List<string> { config.metaAppId });
                SetProperty(meta, "AppLabels", new List<string> { "Cat Miner: Idle Tycoon" });
                SetProperty(meta, "ClientTokens", new List<string> { config.metaClientToken });
                SetProperty(meta, "SelectedAppIndex", 0);
            }
        }

        static void PrepareMetaAndroidEditorTools()
        {
            // The official postprocessor checks a developer DEBUG keystore even for a signed release.
            // Keep that editor prerequisite private in the mirror; the upload key is never changed.
            Type tools = FindType("Facebook.Unity.Editor.FacebookAndroidUtil");
            var ready = tools?.GetProperty("SetupProperly", BindingFlags.Public | BindingFlags.Static);
            if (ready == null) throw new InvalidOperationException("Meta Android editor diagnostics API missing.");
            if ((bool)ready.GetValue(null)) return;
            string reason = tools.GetProperty("SetupError", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
            if (reason != "no_android_keystore") throw new InvalidOperationException("Meta Android editor prerequisites incomplete: " + reason);
            const string relativeKey = "build/private/meta-editor-debug.keystore";
            if (!File.Exists(relativeKey))
            {
                Directory.CreateDirectory("build/private");
                string keytool = Path.Combine(BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None), "OpenJDK", "bin", "keytool");
                var start = new System.Diagnostics.ProcessStartInfo(keytool,
                    "-genkeypair -noprompt -keystore " + relativeKey + " -alias androiddebugkey -storepass android -keypass android -keyalg RSA -keysize 2048 -validity 10000 -dname CN=AndroidDebug,O=Android,C=US")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (var process = System.Diagnostics.Process.Start(start))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(30000)) { process.Kill(); throw new InvalidOperationException("Meta editor debug prerequisite timed out."); }
                    System.Threading.Tasks.Task.WaitAll(output, error);
                    if (process.ExitCode != 0 || !File.Exists(relativeKey)) throw new InvalidOperationException("Meta editor debug prerequisite could not be prepared.");
                }
            }
            SetProperty(FindType("Facebook.Unity.Settings.FacebookSettings"), "AndroidKeystorePath", relativeKey);
            if (!(bool)ready.GetValue(null)) throw new InvalidOperationException("Meta Android editor setup readback failed.");
            Debug.Log("[GooglePlay] Meta Android editor prerequisites verified; release upload signing is separate.");
        }

        static void ApplyMaxConsentSettings(GameSdkConfiguration config)
        {
            if (!config.LegalValid) throw new InvalidOperationException("MAX consent configuration requires published game-specific legal URLs.");
            ApplyMaxClientKey(config);
            // Public settings API in the reviewed MAX 8.6.6 package. Account SDK key is
            // supplied by the owner and synchronized through the official settings API.
            Type type = FindType("AppLovinMax.Scripts.IntegrationManager.Editor.AppLovinInternalSettings");
            var instance = type?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (instance == null) throw new InvalidOperationException("Resolved MAX consent settings API missing.");
            type.GetProperty("ConsentFlowEnabled").SetValue(instance, true);
            type.GetProperty("ConsentFlowPrivacyPolicyUrl").SetValue(instance, config.privacyUrl);
            type.GetProperty("ConsentFlowTermsOfServiceUrl").SetValue(instance, config.termsUrl);
            var geography = type.GetProperty("DebugUserGeography");
            geography.SetValue(instance, Enum.ToObject(geography.PropertyType, 0));
            type.GetMethod("Save").Invoke(instance, null);
        }

        // Key setup is independent of adopting legal documents; it never starts the SDK
        // or grants consent. Native consent setup still requires LegalValid above.
        static void ApplyMaxClientKey(GameSdkConfiguration config)
        {
            if (!config.IdentityValid || string.IsNullOrWhiteSpace(config.maxSdkKey) ||
                config.maxSdkKey.Length < 60 ||
                !System.Text.RegularExpressions.Regex.IsMatch(config.maxSdkKey, "^[A-Za-z0-9_-]+$"))
                throw new InvalidOperationException("Owner-supplied MAX client key and own game identity are required.");
            var type = FindType("AppLovinSettings");
            var instance = type?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var property = type?.GetProperty("SdkKey", BindingFlags.Public | BindingFlags.Instance);
            if (!(instance is UnityEngine.Object asset) || property?.CanWrite != true)
                throw new InvalidOperationException("Official MAX settings API is unavailable.");
            var previous = property.GetValue(instance) as string;
            if (!string.IsNullOrWhiteSpace(previous) && previous != config.maxSdkKey)
                throw new InvalidOperationException("Existing MAX Integration Manager key differs; preserve it for review.");
            property.SetValue(instance, config.maxSdkKey);
            var googleProperty = type.GetProperty("AdMobAndroidAppId", BindingFlags.Public | BindingFlags.Instance);
            if (googleProperty?.CanWrite != true || !config.AdMobAppIdValid)
                throw new InvalidOperationException("The game's own AdMob Android app ID is required for the MAX consent flow.");
            googleProperty.SetValue(instance, config.admobAndroidAppId);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            if ((string)property.GetValue(instance) != config.maxSdkKey)
                throw new InvalidOperationException("MAX client key settings did not persist.");
        }

        public static void ApplyMaxClientKeyBatch()
        {
            try
            {
                ApplyMaxClientKey(GameSdkConfiguration.Load());
                Debug.Log("[GooglePlay] MAX client key synchronized; consent and device verification remain separate.");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("[GooglePlay] MAX client key setup failed: " + error.GetType().Name + ": " + error.Message);
                EditorApplication.Exit(1);
            }
        }

        public static void RunBatch()
        {
            try { Apply(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogError("[GooglePlay] Configuration failed: " + error.GetType().Name + ": " + error.Message); EditorApplication.Exit(1); }
        }

        static void SetFlag(SerializedObject settings, string name)
        {
            var property = settings.FindProperty(name);
            if (property == null) throw new InvalidOperationException("Unity setting missing: " + name);
            property.boolValue = true;
        }
        static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType(name, false)).FirstOrDefault(x => x != null);
        static void SetProperty(Type type, string name, object value)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (property?.CanWrite != true) throw new InvalidOperationException("Official settings property missing: " + name);
            property.SetValue(null, value);
        }
    }
}
