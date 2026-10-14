using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

namespace CatMiners.Editor.GooglePlay
{
    // Compiles Android player branches; it does not create or certify an Android artifact.
    public static class AndroidPlayerApiCheck
    {
        public static void RunBatch()
        {
            bool compilerError = false;
            Application.LogCallback capture = (message, stack, kind) => {
                if (message.Contains("error CS")) compilerError = true;
            };
            Application.logMessageReceived += capture;
            try
            {
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                    throw new InvalidOperationException("Android build support is unavailable.");
                string output = Path.Combine("Temp", "GooglePlayAndroidApi", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(output);
                var result = PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings {
                    target = BuildTarget.Android, group = BuildTargetGroup.Android,
                    options = ScriptCompilationOptions.None
                }, output);
                var assemblies = result.assemblies?.Select(Path.GetFileName).ToArray();
                if (compilerError || assemblies == null || !assemblies.Contains("CatMiners.Runtime.dll"))
                    throw new InvalidOperationException("Android player runtime compilation did not succeed.");
                Directory.CreateDirectory("release/google-play/evidence");
                File.WriteAllText("release/google-play/evidence/android-player-api-compile.json", JsonConvert.SerializeObject(new {
                    unityVersion = Application.unityVersion, target = "Android", assemblies,
                    status = "player_scripts_compiled_pending_native_gradle_artifact_and_device_checks"
                }, Formatting.Indented));
                Debug.Log("[GooglePlay] Android player C# API compilation passed; native artifact checks remain pending.");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("[GooglePlay] Android player API compilation failed: " + error.GetType().Name + ": " + error.Message);
                EditorApplication.Exit(1);
            }
            finally { Application.logMessageReceived -= capture; }
        }
    }
}
