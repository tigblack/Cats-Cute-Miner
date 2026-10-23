using System;
using System.IO;
using CatMiners.Runtime.Integrations;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.EngineDiagnostics;
using UnityEngine;

namespace CatMiners.Editor
{
    public static class BuildMac
    {
        public const string AppPath = "Builds/macOS/Cat Miner.app";
        public const string DevelopmentAppPath = "Builds/macOS/Cat Miner Dev.app";

        [MenuItem("Cat Miners/Build/macOS App")]
        public static void FromMenu() => Build();

        [MenuItem("Cat Miners/Build/macOS Development App")]
        public static void DevelopmentFromMenu() => BuildDevelopment();

        public static BuildReport Build() => Build(AppPath, BuildOptions.None);

        public static BuildReport BuildDevelopment() => Build(DevelopmentAppPath, BuildOptions.Development);

        static BuildReport Build(string appPath, BuildOptions options)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before building the macOS app.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
                throw new BuildFailedException("Install macOS Build Support for this Unity version through Unity Hub.");
            OutpostSetup.ValidateData();
            if (GameSdkConfiguration.Load().MetaValid) GooglePlay.GooglePlayProjectConfiguration.ConfigureMeta();
            Directory.CreateDirectory(Path.GetDirectoryName(appPath));
            int width = PlayerSettings.defaultScreenWidth, height = PlayerSettings.defaultScreenHeight;
            var fullscreen = PlayerSettings.fullScreenMode;
            bool resizable = PlayerSettings.resizableWindow;
            bool diagnostics = EngineDiagnosticsSettings.enabled;
            try
            {
                PlayerSettings.defaultScreenWidth = 540;
                PlayerSettings.defaultScreenHeight = 960;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.resizableWindow = true;
                // A local build can run without access to the original project's symbol-upload service.
                EngineDiagnosticsSettings.enabled = false;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { ProjectSetup.ScenePath }, locationPathName = appPath,
                    target = BuildTarget.StandaloneOSX, options = options
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("macOS build failed. See the Unity Console and build report.");
                Debug.Log("[CatMiners] macOS app ready: " + Path.GetFullPath(appPath));
                return report;
            }
            finally
            {
                PlayerSettings.defaultScreenWidth = width;
                PlayerSettings.defaultScreenHeight = height;
                PlayerSettings.fullScreenMode = fullscreen;
                PlayerSettings.resizableWindow = resizable;
                EngineDiagnosticsSettings.enabled = diagnostics;
                AssetDatabase.SaveAssets();
            }
        }
    }
}
