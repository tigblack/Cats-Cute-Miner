using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CatMiners.Editor.GooglePlay
{
    // Separate local APK. extraScriptingDefines applies only to this BuildPlayer call;
    // production PlayerSettings and the existing release AAB are never changed.
    public static class BuildAppMetricaDiagnostic
    {
        public static void Apk()
        {
            int exit = 1;
            var restore = new Stack<Action>();
            try
            {
                var project = new DirectoryInfo(Directory.GetParent(Application.dataPath).FullName);
                if (!Application.isBatchMode || project.Name != "google-play-ci-macos" || project.Parent?.Name != "build")
                    throw new InvalidOperationException("Use the isolated macOS diagnostic builder.");
                var args = Environment.GetCommandLineArgs();
                string Option(string name, string fallback)
                {
                    int position = Array.IndexOf(args, name);
                    if (position < 0) return fallback;
                    if (position + 1 >= args.Length || args.Count(x => x == name) != 1) throw new InvalidOperationException("Invalid diagnostic argument.");
                    return args[position + 1];
                }
                string version = Option("-catminersDiagnosticVersion", "0.1.4");
                if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$") ||
                    !int.TryParse(Option("-catminersDiagnosticVersionCode", "5"), out int versionCode) || versionCode <= 0 || versionCode > 2100000000)
                    throw new InvalidOperationException("Invalid diagnostic version.");
                int i = Array.IndexOf(args, "-catminersDiagnosticOutput");
                if (i < 0 || i + 1 >= args.Length) throw new InvalidOperationException("Diagnostic output is required.");
                string output = Path.GetFullPath(args[i + 1]);
                string allowed = Path.Combine(project.Parent.FullName, "private") + Path.DirectorySeparatorChar;
                if (!output.StartsWith(allowed, StringComparison.Ordinal) || !output.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) || File.Exists(output))
                    throw new InvalidOperationException("Use a new APK under project build/private.");
                GooglePlayProjectConfiguration.Apply();
                var target = NamedBuildTarget.Android;
                void Set<T>(Func<T> get, Action<T> set, T value) { T old = get(); restore.Push(() => set(old)); set(value); }
                Set(() => PlayerSettings.GetApplicationIdentifier(target), v => PlayerSettings.SetApplicationIdentifier(target, v), "com.multicastgames.catscuteminer");
                Set(() => PlayerSettings.bundleVersion, v => PlayerSettings.bundleVersion = v, version);
                Set(() => PlayerSettings.Android.bundleVersionCode, v => PlayerSettings.Android.bundleVersionCode = v, versionCode);
                Set(() => PlayerSettings.Android.useCustomKeystore, v => PlayerSettings.Android.useCustomKeystore = v, false);
                Set(() => PlayerSettings.GetScriptingBackend(target), v => PlayerSettings.SetScriptingBackend(target, v), ScriptingImplementation.IL2CPP);
                Set(() => PlayerSettings.Android.targetArchitectures, v => PlayerSettings.Android.targetArchitectures = v, AndroidArchitecture.ARM64);
                Set(() => EditorUserBuildSettings.buildAppBundle, v => EditorUserBuildSettings.buildAppBundle = v, false);
                Set(() => EditorUserBuildSettings.exportAsGoogleAndroidProject, v => EditorUserBuildSettings.exportAsGoogleAndroidProject = v, false);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { ProjectSetup.ScenePath }, locationPathName = output,
                    target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.Development | BuildOptions.StrictMode,
                    extraScriptingDefines = new[] { "CATMINERS_APPMETRICA_DIAGNOSTICS" }
                });
                if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0 || !File.Exists(output))
                    throw new InvalidOperationException("Diagnostic APK build failed.");
                Debug.Log("[AppMetricaDiagnostic] Local APK built. Native verbose logs and safe console enabled; not a Play release.");
                exit = 0;
            }
            catch (Exception e) { Debug.LogError("[AppMetricaDiagnostic] Build failed: " + e.GetType().Name); }
            finally { while (restore.Count > 0) try { restore.Pop()(); } catch { exit = 1; } }
            EditorApplication.Exit(exit);
        }
    }
}
