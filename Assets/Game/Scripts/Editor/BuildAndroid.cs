using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CatMiners.Editor.GooglePlay
{
    /// <summary>Local signed Android builds. No store/API calls or automatic project setup.</summary>
    public static class BuildAndroid
    {
        sealed class ReleaseBlocked : Exception { public ReleaseBlocked(string message) : base(message) { } }
        // Implement in the real provider integration, not a release manifest with claimed booleans.
        // Validate must inspect installed SDK/native dependencies, app-specific production config
        // and bootstrap wiring, and must never return credentials in diagnostics.
        public interface IReleaseGate
        {
            string Provider { get; }
            IEnumerable<string> Validate(string packageName);
        }

        static readonly NamedBuildTarget Target = NamedBuildTarget.Android;
        static readonly string[] Providers = { "AppMetrica", "Adjust", "Meta", "MAX", "Billing", "MulticastPayments" };

        public static void Aab() => Run(true);
        // Signed release APK companion. Legacy CatMiners.Editor.BuildAndroid.Apk remains
        // in BalanceValidatorCli.cs and is only a QA workflow, not a Play release.
        public static void Apk() => Run(false);

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var hits = args.Select((value, index) => new { value, index }).Where(x => x.value == name).ToArray();
            if (hits.Length != 1 || hits[0].index + 1 >= args.Length || args[hits[0].index + 1].StartsWith("-"))
                throw new ReleaseBlocked("Supply exactly one " + name + " value.");
            return args[hits[0].index + 1];
        }

        static string Env(string name, string fallback = null) => Environment.GetEnvironmentVariable(name) ?? fallback;

        static bool UnsafeDefine(string symbol) => Regex.IsMatch(symbol,
            "PLAYABLE|(^|_)(QA|DEBUG|DEV|DEVTOOLS|DEVELOPMENT|TEST|TESTS)(_|$)", RegexOptions.IgnoreCase);

        static List<string> Preconditions(string package)
        {
            var errors = new List<string>();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                errors.Add("Android Build Support module unavailable.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                errors.Add("Launch with -buildTarget Android in an isolated project.");
            var player = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer");
            foreach (var tool in new[] { new[] { "SDK", "AndroidSdkRoot", "SdkUseEmbedded", "platforms/android-36/android.jar" },
                                         new[] { "NDK", "AndroidNdkRoot", "NdkUseEmbedded", "source.properties" },
                                         new[] { "OpenJDK", "JdkPath", "JdkUseEmbedded", "bin/java.exe" } })
            {
                var path = EditorPrefs.GetBool(tool[2], true) ? Path.Combine(player, tool[0]) : EditorPrefs.GetString(tool[1], "");
                if (string.IsNullOrEmpty(path) || !File.Exists(Path.Combine(path, tool[3])))
                    errors.Add("Effective " + tool[0] + " selection incomplete (SDK requires API 36); check External Tools and preflight candidates.");
            }
            var playerSettings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input = playerSettings.FindProperty("activeInputHandler");
            if (input == null || input.intValue != 1) errors.Add("Input System only (activeInputHandler=1) required; configure and restart Unity first.");
            foreach (var path in new[] { ProjectSetup.ScenePath, ProjectSetup.InputActionsPath, ProjectSetup.BalancePath })
                if (!File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) == null) errors.Add("Required input/asset missing: " + path);
            if (!EditorBuildSettings.scenes.Any(s => s.enabled && s.path == ProjectSetup.ScenePath))
                errors.Add("CoreGame must be enabled in EditorBuildSettings.");
            if (File.Exists(ProjectSetup.ScenePath))
            {
                foreach (var path in AssetDatabase.GetDependencies(ProjectSetup.ScenePath, true))
                    if (!File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) == null) errors.Add("Scene dependency unavailable: " + path);
                // Also detect unresolved GUIDs: GetDependencies omits some missing references.
                foreach (Match match in Regex.Matches(File.ReadAllText(ProjectSetup.ScenePath), @"guid:\s*([a-fA-F0-9]{32})"))
                    if (match.Groups[1].Value.Any(c => c != '0') && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value)))
                        errors.Add("CoreGame contains an unresolved asset GUID.");
                var scene = SceneManager.GetSceneByPath(ProjectSetup.ScenePath);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(ProjectSetup.ScenePath, OpenSceneMode.Additive);
                try
                {
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                                errors.Add("CoreGame contains a missing script.");
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            var icons = PlayerSettings.GetSupportedIconKinds(Target).SelectMany(kind => PlayerSettings.GetPlatformIcons(Target, kind)).ToArray();
            if (icons.Length == 0 || icons.Any(icon => icon.GetTextures().Count(t => t != null) < icon.minLayerCount))
                errors.Add("Configure every required Android icon slot/layer before release.");
            // Reject active profile overrides too; do not silently clean user-defined release behavior.
            var defines = (PlayerSettings.GetScriptingDefineSymbols(Target) ?? "").Split(';', ',', ' ')
                .Concat(EditorUserBuildSettings.activeScriptCompilationDefines ?? Array.Empty<string>());
            if (defines.Any(UnsafeDefine)) errors.Add("Playable/QA/debug/test defines active; remove from Android settings/profile before release.");
            var gates = TypeCache.GetTypesDerivedFrom<IReleaseGate>().Where(t => !t.IsAbstract && !t.IsInterface).ToArray();
            foreach (var provider in Providers)
            {
                bool found = false;
                foreach (var type in gates)
                {
                    try
                    {
                        var gate = (IReleaseGate)Activator.CreateInstance(type);
                        if (gate.Provider != provider) continue;
                        found = true;
                        if ((gate.Validate(package) ?? throw new InvalidOperationException()).Any())
                            errors.Add(provider + " integration/configuration validation failed (inspect locally; diagnostics redacted).");
                    }
                    catch { errors.Add("Provider gate failed; diagnostics redacted to protect configuration."); }
                }
                if (!found) errors.Add(provider + " release integration/configuration validator missing. Billing must resolve native library >=8; purchases need server validation.");
            }
            return errors.Distinct().ToList();
        }

        static void Change<T>(Stack<Action> restore, Func<T> get, Action<T> set, T value)
        {
            T previous = get();
            restore.Push(() => set(previous));
            set(value);
        }

        static string ExecuteVerifier(string executable, string arguments)
        {
            using (var process = new System.Diagnostics.Process())
            {
                process.StartInfo = new System.Diagnostics.ProcessStartInfo(executable, arguments) {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process.Start();
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000)) { process.Kill(); throw new ReleaseBlocked("Artifact signing verifier timed out."); }
                System.Threading.Tasks.Task.WaitAll(stdout, stderr);
                if (process.ExitCode != 0) throw new ReleaseBlocked("Artifact signature verification failed; tool output suppressed.");
                return stdout.Result;
            }
        }

        static void VerifyBundleSignature(string output, string expected)
        {
            var player = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer");
            string jdk = EditorPrefs.GetBool("JdkUseEmbedded", true) ? Path.Combine(player, "OpenJDK") : EditorPrefs.GetString("JdkPath", "");
            if (output.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0) throw new ReleaseBlocked("Invalid artifact path.");
            string quoted = "\"" + output + "\"";
            string verified = ExecuteVerifier(Path.Combine(jdk, "bin", "jarsigner.exe"), "-J-Duser.language=en -J-Duser.country=US -verify " + quoted);
            if (!Regex.IsMatch(verified, @"(?m)^jar verified\.") || Regex.IsMatch(verified, "unsigned entries|jar is unsigned", RegexOptions.IgnoreCase))
                throw new ReleaseBlocked("Unsigned or partially unsigned AAB rejected.");
            string certificates = ExecuteVerifier(Path.Combine(jdk, "bin", "keytool.exe"), "-J-Duser.language=en -printcert -jarfile " + quoted + " -rfc");
            var matches = Regex.Matches(certificates, @"-----BEGIN CERTIFICATE-----\s*(.*?)\s*-----END CERTIFICATE-----", RegexOptions.Singleline);
            if (matches.Count != 1) throw new ReleaseBlocked("Expected a single upload certificate; inspect chain locally.");
            using (var sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(Convert.FromBase64String(Regex.Replace(matches[0].Groups[1].Value, @"\s", "")))).Replace("-", "");
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new ReleaseBlocked("AAB certificate differs from expected upload key.");
            }
        }

        static void Run(bool bundle)
        {
            int exit = 1;
            var restore = new Stack<Action>();
            string output = null;
            bool artifactStarted = false;
            try
            {
                if (!Application.isBatchMode) throw new ReleaseBlocked("Use the local batch wrapper; interactive release builds are disabled.");
                string package = Argument("-catminersPackage");
                string version = Argument("-catminersVersion");
                if (!Regex.IsMatch(package, @"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$") ||
                    package == ProjectSetup.BundleId || Regex.IsMatch(package, "prototype|example|placeholder", RegexOptions.IgnoreCase))
                    throw new ReleaseBlocked("An accepted actual release package ID is required; prototype/example IDs are forbidden.");
                if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+(?:[.-][A-Za-z0-9.-]+)?$") || version.Length > 50)
                    throw new ReleaseBlocked("Supply an explicit version name (maximum 50 characters).");
                if (!int.TryParse(Argument("-catminersVersionCode"), out int code) || code <= 0 || code > 2100000000)
                    throw new ReleaseBlocked("Version code must be 1..2100000000; confirm it is unused in Play.");
                output = Path.GetFullPath(Argument("-catminersOutput"));
                if (!bundle && !output.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) output = Path.Combine(output, "CatsCuteMiner-release.apk");
                if (!output.EndsWith(bundle ? ".aab" : ".apk", StringComparison.OrdinalIgnoreCase) || File.Exists(output) || File.Exists(output + ".build.json"))
                    throw new ReleaseBlocked("Use a new output file with the correct artifact extension; overwriting is disabled.");
                var errors = Preconditions(package);
                string keystore = Env("CATMINERS_UPLOAD_KEYSTORE", PlayerSettings.Android.useCustomKeystore ? PlayerSettings.Android.keystoreName : null);
                string alias = Env("CATMINERS_UPLOAD_ALIAS", PlayerSettings.Android.useCustomKeystore ? PlayerSettings.Android.keyaliasName : null);
                string storePass = Env("CATMINERS_UPLOAD_STORE_PASSWORD", PlayerSettings.Android.keystorePass);
                string aliasPass = Env("CATMINERS_UPLOAD_KEY_PASSWORD", PlayerSettings.Android.keyaliasPass);
                string expectedCertificate = (Env("CATMINERS_UPLOAD_CERT_SHA256", "") ?? "").Replace(":", "");
                if (bundle && !Regex.IsMatch(expectedCertificate, "^[a-fA-F0-9]{64}$"))
                    errors.Add("Expected upload certificate SHA-256 environment reference required.");
                if (string.IsNullOrWhiteSpace(keystore) || !File.Exists(keystore) || string.IsNullOrWhiteSpace(alias) ||
                    string.IsNullOrEmpty(storePass) || string.IsNullOrEmpty(aliasPass))
                    errors.Add("Existing upload keystore, alias and secret environment passwords (or configured Editor signing) required. No key is generated.");
                if (errors.Count > 0) throw new ReleaseBlocked("Release blocked:\n" + string.Join("\n", errors));

                Change(restore, () => PlayerSettings.GetApplicationIdentifier(Target), x => PlayerSettings.SetApplicationIdentifier(Target, x), package);
                Change(restore, () => PlayerSettings.bundleVersion, x => PlayerSettings.bundleVersion = x, version);
                Change(restore, () => PlayerSettings.Android.bundleVersionCode, x => PlayerSettings.Android.bundleVersionCode = x, code);
                Change(restore, () => PlayerSettings.GetScriptingBackend(Target), x => PlayerSettings.SetScriptingBackend(Target, x), ScriptingImplementation.IL2CPP);
                Change(restore, () => PlayerSettings.Android.targetArchitectures, x => PlayerSettings.Android.targetArchitectures = x, AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64);
                Change(restore, () => PlayerSettings.Android.targetSdkVersion, x => PlayerSettings.Android.targetSdkVersion = x, (AndroidSdkVersions)36);
                Change(restore, () => PlayerSettings.Android.useCustomKeystore, x => PlayerSettings.Android.useCustomKeystore = x, true);
                Change(restore, () => PlayerSettings.Android.keystoreName, x => PlayerSettings.Android.keystoreName = x, keystore);
                Change(restore, () => PlayerSettings.Android.keyaliasName, x => PlayerSettings.Android.keyaliasName = x, alias);
                Change(restore, () => PlayerSettings.Android.keystorePass, x => PlayerSettings.Android.keystorePass = x, storePass);
                Change(restore, () => PlayerSettings.Android.keyaliasPass, x => PlayerSettings.Android.keyaliasPass = x, aliasPass);
                Change(restore, () => PlayerSettings.Android.splitApplicationBinary, x => PlayerSettings.Android.splitApplicationBinary = x, false);
                Change(restore, () => EditorUserBuildSettings.buildAppBundle, x => EditorUserBuildSettings.buildAppBundle = x, bundle);
                Change(restore, () => EditorUserBuildSettings.exportAsGoogleAndroidProject, x => EditorUserBuildSettings.exportAsGoogleAndroidProject = x, false);
                Change(restore, () => EditorUserBuildSettings.development, x => EditorUserBuildSettings.development = x, false);
                Change(restore, () => EditorUserBuildSettings.allowDebugging, x => EditorUserBuildSettings.allowDebugging = x, false);
                Change(restore, () => EditorUserBuildSettings.connectProfiler, x => EditorUserBuildSettings.connectProfiler = x, false);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                artifactStarted = true;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { ProjectSetup.ScenePath }, locationPathName = output,
                    target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android, options = BuildOptions.StrictMode
                });
                if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0 ||
                    (report.summary.options & (BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler)) != 0 ||
                    !File.Exists(output) || new FileInfo(output).Length == 0)
                    throw new ReleaseBlocked("Android BuildPipeline failed or produced a development/missing artifact.");
                if (report.GetFiles().Any(file => Regex.IsMatch(file.path, @"CatMiners\.(DevTools|Playable)\.dll$", RegexOptions.IgnoreCase)))
                    throw new ReleaseBlocked("QA/playable assembly found in Android release BuildReport.");
                if (bundle) VerifyBundleSignature(output, expectedCertificate);
                string hash;
                using (var stream = File.OpenRead(output))
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                File.WriteAllText(output + ".build.json", JsonConvert.SerializeObject(new {
                    result = "BuiltPendingArtifactVerification", path = output, sha256 = hash,
                    package, version, versionCode = code, targetApi = 36, architecture = "ARMv7+ARM64", backend = "IL2CPP",
                    development = false, unity = Application.unityVersion, scene = ProjectSetup.ScenePath,
                    signatureVerified = bundle,
                    uploadCertificateSha256 = bundle ? expectedCertificate.ToUpperInvariant() : null,
                    verificationPending = (bundle ? Array.Empty<string>() : new[] { "APK artifact signature and expected upload certificate" })
                        .Concat(new[] { "Merged manifest identity, API and debuggable", "Resolved native Billing >=8", "Every native ELF and APK packaging 16 KB alignment", "Device/provider delivery and Play App Signing enrollment" }).ToArray()
                }, Formatting.Indented));
                Debug.Log("[CatMiners] Android artifact built; not upload-ready until final artifact checks pass.");
                exit = 0;
            }
            catch (ReleaseBlocked error) { Debug.LogError("[CatMiners] " + error.Message); }
            catch { Debug.LogError("[CatMiners] Android preparation/build failed. Exception details suppressed to protect signing/provider secrets."); }
            finally
            {
                while (restore.Count > 0) { try { restore.Pop()(); } catch { exit = 1; Debug.LogError("Android settings restoration failed."); } }
                if (artifactStarted) { try { AssetDatabase.SaveAssets(); } catch { exit = 1; Debug.LogError("Restored Android settings could not be saved."); } }
                if (exit != 0 && artifactStarted && output != null)
                    foreach (var path in new[] { output, output + ".build.json" })
                        try { if (File.Exists(path)) File.Delete(path); } catch { Debug.LogError("Failed artifact cleanup incomplete; do not distribute this output."); }
            }
            if (Application.isBatchMode) EditorApplication.Exit(exit);
        }
    }
}
