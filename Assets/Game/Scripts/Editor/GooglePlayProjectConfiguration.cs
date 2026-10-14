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
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var entry = settings.FindProperty("androidApplicationEntry");
            if (entry == null) throw new InvalidOperationException("Unity Android entry selection missing.");
            int entryFlags = entry.intValue;
            string engine = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer");
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
                SetProperty(meta, "AppLabels", new List<string> { "Cats Cute Miner" });
                SetProperty(meta, "ClientTokens", new List<string> { config.metaClientToken });
                SetProperty(meta, "SelectedAppIndex", 0);
            }
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("release/google-play/evidence");
            File.WriteAllText("release/google-play/evidence/native-project-configuration.json", JsonConvert.SerializeObject(new {
                package = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
                reservedIosBundle = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS), targetApi = 36,
                metaAutomaticStartup = false, metaAutomaticEvents = false, metaAdvertisingId = false,
                appMetricaAutomaticMaxRevenue = false, versionChanged = false,
                status = "source_settings_applied_pending_merged_manifest_and_device_verification"
            }, Formatting.Indented));
            Debug.Log("[GooglePlay] Accepted identity and protected native settings applied; artifact verification remains pending.");
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
