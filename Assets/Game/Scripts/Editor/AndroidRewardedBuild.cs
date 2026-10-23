using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CatMiners.Editor.GooglePlay
{
    /// <summary>Resolve restored rewarded dependencies in the isolated Android build checkout.</summary>
    public static class AndroidRewardedBuild
    {
        public static void Prepare()
        {
            try
            {
                if (!Application.isBatchMode || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                    throw new InvalidOperationException("Use the isolated Android batch workflow.");
                GooglePlayProjectConfiguration.Apply();
                var resolver = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("GooglePlayServices.PlayServicesResolver", false)).FirstOrDefault(t => t != null);
                var resolve = resolver?.GetMethod("ResolveSync", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(bool) }, null);
                if (resolve == null || !(bool)resolve.Invoke(null, new object[] { true }))
                    throw new InvalidOperationException("EDM4U Android dependency resolution failed.");
                AssetDatabase.Refresh();
                var errors = new MaxRuntimeGate().Validate(CatMiners.Runtime.Integrations.GameSdkConfiguration.Package).ToArray();
                if (errors.Length != 0) throw new InvalidOperationException(string.Join("\n", errors));
                Directory.CreateDirectory(EditorPaths.ReportsDir());
                File.WriteAllText(EditorPaths.Report("android-rewarded-preparation.json"),
                    "{\"adsEnabled\":true,\"maxUnity\":\"8.6.6\",\"maxAndroid\":\"13.6.4\",\"nativeGatePassed\":true}\n");
                Debug.Log("[CatMiners] Rewarded Android dependencies and native consent settings verified.");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                // Never print reflection exception values: SDK settings can contain client identifiers.
                Debug.LogError("[CatMiners] Android rewarded preparation failed: " + error.GetType().Name);
                EditorApplication.Exit(1);
            }
        }
    }
}
