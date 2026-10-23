using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CatMiners.Core.Data;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Player;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CatMiners.Editor
{
    /// <summary>
    /// Каталоги для запуска без GUI. Отчёты — из аргумента -catminersReports (запуск в копии build/ci) или Docs/Reports проекта;
    /// APK — из аргумента -catminersOutput (копия build/ci кладёт APK в Builds/Android настоящего проекта) или Builds/Android.
    /// </summary>
    public static class EditorPaths
    {
        public static string ReportsDir() => Argument("-catminersReports") ?? Path.GetFullPath("Docs/Reports");

        /// <summary>Каталог APK (ТЗ 19 п. 6: «APK кладётся на ПК в Builds/Android/»).</summary>
        public static string OutputDir() => Argument("-catminersOutput") ?? Path.GetFullPath("Builds/Android");

        public static string Report(string file)
        {
            string dir = ReportsDir();
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, file);
        }

        /// <summary>Значение аргумента командной строки Unity или null.</summary>
        public static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name && !string.IsNullOrEmpty(args[i + 1])) return args[i + 1];
            return null;
        }
    }

    /// <summary>Минимальная запись JSON для машинных отчётов (без зависимостей).</summary>
    static class JsonText
    {
        public static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);
        public static string Num(ulong v) => v.ToString(CultureInfo.InvariantCulture);
        public static string Bool(bool v) => v ? "true" : "false";

        public static string Array(IEnumerable<string> items)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var s in items)
            {
                if (!first) sb.Append(", ");
                sb.Append(Str(s));
                first = false;
            }
            return sb.Append(']').ToString();
        }
    }

    /// <summary>
    /// Валидатор баланса без GUI (ТЗ 17.3): -executeMethod CatMiners.Editor.BalanceValidatorCli.Run.
    /// Пишет Docs/Reports/validation.json и validation.txt, код выхода 0 — ошибок нет.
    /// В плеере (Development APK, IL2CPP) тот же валидатор запускает кнопка DEV → «Валидатор» (validation-player.json, ТЗ 5.1).
    /// </summary>
    public static class BalanceValidatorCli
    {
        [MenuItem("Cat Miners/Validate Balance")]
        public static void RunFromMenu()
        {
            int errors = Validate(out string summary);
            EditorUtility.DisplayDialog("Cat Miners", summary, "OK");
            Debug.Log("[CatMiners] Validation errors: " + errors);
        }

        public static void Run()
        {
            int code;
            try
            {
                code = Validate(out string summary) == 0 ? 0 : 2;
                Debug.Log(summary);
            }
            catch (Exception e)
            {
                Debug.LogError("[CatMiners] Validator crashed: " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        static int Validate(out string summary)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(ProjectSetup.BalancePath);
            if (asset == null) throw new FileNotFoundException(ProjectSetup.BalancePath);
            var repo = BalanceRepository.Load(asset.bytes);
            var report = BalanceValidator.Validate(repo);
            File.WriteAllText(EditorPaths.Report("validation.txt"), report.ToText(), new UTF8Encoding(false));
            var json = new StringBuilder();
            json.Append("{\n  \"sha256\": \"").Append(repo.Sha256).Append("\",\n");
            json.Append("  \"sizeBytes\": ").Append(repo.SizeBytes).Append(",\n");
            json.Append("  \"textAssetBytes\": ").Append(asset.bytes.Length).Append(",\n");
            json.Append("  \"zones\": ").Append(report.Zones.Count).Append(",\n");
            json.Append("  \"errors\": ").Append(report.ErrorCount).Append(",\n");
            json.Append("  \"warnings\": ").Append(report.Issues.Count - report.ErrorCount).Append(",\n");
            json.Append("  \"unityVersion\": \"").Append(Application.unityVersion).Append("\",\n");
            json.Append("  \"checkedAtUtc\": \"").Append(DateTime.UtcNow.ToString("o")).Append("\"\n}\n");
            File.WriteAllText(EditorPaths.Report("validation.json"), json.ToString(), new UTF8Encoding(false));
            summary = "Зон: " + report.Zones.Count + ", ошибок: " + report.ErrorCount + ", SHA-256: " + repo.Sha256;
            return report.ErrorCount;
        }
    }

    /// <summary>
    /// Сборка Android APK без GUI (ТЗ 19 п. 6): -buildTarget Android -executeMethod CatMiners.Editor.BuildAndroid.Apk.
    /// За один запуск собираются два APK (только сцена CoreGame, APK, а не AAB, debug keystore):
    /// «development» — Development Build с инструментами (CatMiners-&lt;версия&gt;-dev.apk) и «noDevTools» — сборка без
    /// DEVELOPMENT_BUILD (CatMiners-&lt;версия&gt;.apk), в которую CatMiners.DevTools не попадает по defineConstraints (ТЗ 5.2).
    /// Перед каждой сборкой скрипты плеера компилируются отдельно (PlayerBuildInterface.CompilePlayerScripts, необязательная
    /// быстрая проверка): строки «error CS» или найденная CatMiners.DevTools.dll в сборке без инструментов — провал варианта.
    /// Если проверка упала или не нашла CatMiners.Runtime (формат имён ScriptCompilationResult.assemblies не задокументирован),
    /// это только предупреждение (warnings, scriptCheck): сборка идёт дальше, а DevTools ищутся по файлам BuildReport.
    /// Отчёт — Docs/Reports/build-android.json; код выхода 0 — оба варианта Succeeded, 3 — нет модуля Android
    /// (указано, какого: AndroidPlayer, SDK, NDK, OpenJDK), 1 — иначе.
    /// </summary>
    public static class BuildAndroid
    {
        public const string ReportFile = "build-android.json";
        public const string DevToolsAssembly = "CatMiners.DevTools.dll";
        public const string RuntimeAssembly = "CatMiners.Runtime.dll";
        public const int ExitModuleMissing = 3;

        sealed class Variant
        {
            public string Name;
            public BuildOptions Options;
            public string Path;
            public string Result = "NotRun";
            public long SizeBytes;
            public ulong TotalSize;
            public string Sha256;
            public int TotalErrors;
            public int TotalWarnings;
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public bool DevToolsIncluded;
            public string DevToolsSource = "unknown";
            /// <summary>Итог быстрой проверки скриптов: NotRun, Ok, Errors, Inconclusive (нет CatMiners.Runtime), Exception.</summary>
            public string ScriptCheck = "NotRun";
            /// <summary>Имена из ScriptCompilationResult.assemblies как есть (по ним видно фактический формат).</summary>
            public readonly List<string> PlayerAssemblies = new List<string>();
            /// <summary>DLL, найденные в каталоге вывода CompilePlayerScripts.</summary>
            public readonly List<string> PlayerAssemblyFiles = new List<string>();
            public double BuildSeconds;
            public string BuiltAtUtc;

            public bool Succeeded => Result == BuildResult.Succeeded.ToString();
        }

        public static void Apk()
        {
            int code;
            try
            {
                code = Run();
            }
            catch (Exception e)
            {
                Debug.LogError("[CatMiners] BuildAndroid crashed: " + e);
                WriteReport("{\n  \"result\": \"Exception\",\n  \"message\": " + JsonText.Str(e.Message) + ",\n  \"internalOnly\": true,\n  \"builtAtUtc\": " +
                            JsonText.Str(DateTime.UtcNow.ToString("o")) + "\n}\n");
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        static int Run()
        {
            var missing = MissingAndroidModules();
            if (missing.Count > 0)
            {
                string message = "Нет модуля Android Build Support: " + string.Join(", ", missing) +
                                 ". Unity Hub → Installs → Add modules → Android Build Support (Android SDK & NDK Tools, OpenJDK).";
                Debug.LogError("[CatMiners] " + message);
                WriteReport("{\n  \"result\": \"ModuleMissing\",\n  \"missing\": " + JsonText.Array(missing) + ",\n  \"message\": " + JsonText.Str(message) +
                            ",\n  \"source\": \"BuildAndroid.Apk\",\n  \"internalOnly\": true,\n  \"checkedAtUtc\": " + JsonText.Str(DateTime.UtcNow.ToString("o")) + "\n}\n");
                return ExitModuleMissing;
            }
            if (!File.Exists(ProjectSetup.ScenePath))
            {
                string message = "Нет сцены " + ProjectSetup.ScenePath + ": выполните Cat Miners → Setup Project (или unity.ps1 -Mode Setup).";
                Debug.LogError("[CatMiners] " + message);
                WriteReport("{\n  \"result\": \"SceneMissing\",\n  \"message\": " + JsonText.Str(message) + ",\n  \"internalOnly\": true,\n  \"checkedAtUtc\": " +
                            JsonText.Str(DateTime.UtcNow.ToString("o")) + "\n}\n");
                return 1;
            }

            EditorUserBuildSettings.buildAppBundle = false;
            string version = PlayerSettings.bundleVersion;
            string dir = EditorPaths.OutputDir();
            Directory.CreateDirectory(dir);
            var variants = new[]
            {
                new Variant { Name = "development", Options = BuildOptions.Development, Path = Path.Combine(dir, "CatMiners-" + version + "-dev.apk") },
                new Variant { Name = "noDevTools", Options = BuildOptions.None, Path = Path.Combine(dir, "CatMiners-" + version + ".apk") },
            };

            // Debug keystore (ТЗ 19 п. 6): свой keystore на время сборки отключается и затем возвращается.
            bool customKeystore = PlayerSettings.Android.useCustomKeystore;
            if (customKeystore) PlayerSettings.Android.useCustomKeystore = false;
            try
            {
                foreach (var v in variants)
                {
                    try
                    {
                        BuildVariant(v);
                    }
                    catch (Exception e)
                    {
                        v.Result = "Exception";
                        v.Errors.Add(e.GetType().Name + ": " + e.Message);
                        Debug.LogError("[CatMiners] BuildAndroid " + v.Name + ": " + e);
                    }
                    if (v.BuiltAtUtc == null) v.BuiltAtUtc = DateTime.UtcNow.ToString("o");
                    Debug.Log("[CatMiners] BuildAndroid " + v.Name + ": " + v.Result + " " + v.Path + " devTools=" + v.DevToolsIncluded);
                }
            }
            finally
            {
                if (customKeystore) PlayerSettings.Android.useCustomKeystore = true;
            }

            bool ok = true;
            foreach (var v in variants) ok &= v.Succeeded;
            WriteReport(BuildJson(variants, ok, dir));
            return ok ? 0 : 1;
        }

        static void BuildVariant(Variant v)
        {
            bool dev = (v.Options & BuildOptions.Development) != 0;

            // 1. Быстрая проверка скриптов плеера (необязательная): компилируются ли скрипты и попадает ли в них CatMiners.DevTools.
            // Провал варианта — только настоящие строки «error CS» или найденная DevTools в сборке без инструментов.
            // Исключение проверки или ненайденная CatMiners.Runtime — предупреждение: дальше решают BuildPlayer и BuildReport.
            string scriptDir = Path.GetFullPath(Path.Combine("Temp", "CatMinersPlayerScripts", v.Name));
            List<string> scriptErrors = null;
            try
            {
                scriptErrors = CompilePlayerScripts(dev, scriptDir, v.PlayerAssemblies);
            }
            catch (Exception e)
            {
                v.ScriptCheck = "Exception";
                v.Warnings.Add("Быстрая проверка скриптов (CompilePlayerScripts) не выполнена: " + e.GetType().Name + ": " + e.Message +
                               ". DevTools проверяются по файлам BuildReport.");
                Debug.LogWarning("[CatMiners] BuildAndroid " + v.Name + ": CompilePlayerScripts: " + e);
            }
            if (scriptErrors != null)
            {
                ListDlls(scriptDir, v.PlayerAssemblyFiles);
                if (scriptErrors.Count > 0)
                {
                    v.ScriptCheck = "Errors";
                    v.Result = "ScriptCompileFailed";
                    AddErrors(v, scriptErrors);
                    return;
                }
                bool runtimeFound = HasAssembly(v.PlayerAssemblies, v.PlayerAssemblyFiles, RuntimeAssembly);
                bool devToolsFound = HasAssembly(v.PlayerAssemblies, v.PlayerAssemblyFiles, DevToolsAssembly);
                if (runtimeFound || devToolsFound)
                {
                    // Список сборок осмыслен: найденная или ненайденная DevTools — достоверный вывод проверки.
                    v.ScriptCheck = runtimeFound ? "Ok" : "Inconclusive";
                    v.DevToolsIncluded = devToolsFound;
                    v.DevToolsSource = "CompilePlayerScripts";
                }
                else
                {
                    v.ScriptCheck = "Inconclusive";
                }
                if (!runtimeFound)
                    v.Warnings.Add("Быстрая проверка скриптов не нашла " + RuntimeAssembly + " ни в ScriptCompilationResult.assemblies (" +
                                   v.PlayerAssemblies.Count + " шт.), ни в " + scriptDir + ": компиляцию подтверждает только BuildPlayer" +
                                   (devToolsFound ? "." : ", DevTools проверяются по файлам BuildReport."));
                if (!dev && devToolsFound)
                {
                    v.Result = "DevToolsLeak";
                    v.Errors.Add(DevToolsAssembly + " попала в сборку без DEVELOPMENT_BUILD (ТЗ 5.2: инструменты не попадают в релиз).");
                    return;
                }
            }
            if (v.DevToolsSource != "CompilePlayerScripts") v.DevToolsSource = "BuildReport";

            // 2. Сборка APK.
            if (File.Exists(v.Path)) File.Delete(v.Path);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = v.Path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = v.Options,
            };
            var started = DateTime.UtcNow;
            var report = BuildPipeline.BuildPlayer(options);
            v.BuildSeconds = (DateTime.UtcNow - started).TotalSeconds;
            v.BuiltAtUtc = DateTime.UtcNow.ToString("o");
            var summary = report.summary;
            v.Result = summary.result.ToString();
            v.TotalErrors = summary.totalErrors;
            v.TotalWarnings = summary.totalWarnings;
            v.TotalSize = summary.totalSize;

            var messages = new List<string>();
            foreach (var step in report.steps)
            {
                if (step.messages == null) continue;
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception || m.type == LogType.Assert) messages.Add(m.content);
            }
            AddErrors(v, messages);

            foreach (var f in report.GetFiles())
            {
                if (!SameAssembly(f.path, DevToolsAssembly)) continue;
                v.DevToolsIncluded = true;
                v.DevToolsSource = "BuildReport";
                break;
            }
            if (!dev && v.DevToolsIncluded && v.Succeeded)
            {
                v.Result = "DevToolsLeak";
                v.Errors.Add(DevToolsAssembly + " есть среди файлов сборки без DEVELOPMENT_BUILD.");
            }

            if (File.Exists(v.Path))
            {
                v.SizeBytes = new FileInfo(v.Path).Length;
                v.Sha256 = FileSha256(v.Path);
            }
            else if (v.Succeeded)
            {
                v.Result = "NoOutput";
                v.Errors.Add("BuildPipeline вернул Succeeded, но файла нет: " + v.Path);
            }
        }

        static void AddErrors(Variant v, List<string> messages)
        {
            foreach (var m in messages)
            {
                if (string.IsNullOrEmpty(m) || v.Errors.Contains(m)) continue;
                if (v.Errors.Count >= 50)
                {
                    v.Errors.Add("… и другие (см. unity-buildapk.log)");
                    return;
                }
                v.Errors.Add(m);
            }
        }

        /// <summary>
        /// Есть ли сборка dll («CatMiners.Runtime.dll») среди результатов CompilePlayerScripts. Формат имён в
        /// ScriptCompilationResult.assemblies не задокументирован (с «.dll» или без, с путём или без), поэтому имя
        /// сравнивается в обоих видах, а также по файлам, найденным в каталоге вывода.
        /// </summary>
        static bool HasAssembly(List<string> names, List<string> files, string dll)
        {
            foreach (var n in names)
                if (SameAssembly(n, dll)) return true;
            foreach (var f in files)
                if (SameAssembly(f, dll)) return true;
            return false;
        }

        /// <summary>
        /// Совпадает ли имя или путь с именем сборки: «CatMiners.DevTools.dll» и «CatMiners.DevTools» — одно и то же.
        /// Path.GetFileNameWithoutExtension здесь не подходит: у «CatMiners.DevTools» он отрежет «.DevTools».
        /// </summary>
        static bool SameAssembly(string nameOrPath, string dll)
        {
            if (string.IsNullOrEmpty(nameOrPath)) return false;
            int slash = nameOrPath.LastIndexOfAny(new[] { '/', '\\' });
            string file = (slash >= 0 ? nameOrPath.Substring(slash + 1) : nameOrPath).Trim();
            if (string.Equals(file, dll, StringComparison.OrdinalIgnoreCase)) return true;
            string bare = dll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? dll.Substring(0, dll.Length - 4) : dll;
            return string.Equals(file, bare, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Имена DLL в каталоге вывода (с подкаталогами); ошибки чтения каталога не мешают сборке.</summary>
        static void ListDlls(string dir, List<string> files)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories)) files.Add(Path.GetFileName(f));
                files.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CatMiners] BuildAndroid: не удалось прочитать " + dir + ": " + e.Message);
            }
        }

        /// <summary>
        /// Скомпилировать скрипты плеера Android (Development или без него) в каталог tmp.
        /// Возвращает ошибки компиляции (строки «error CS» из лога), assemblies — имена из ScriptCompilationResult как есть.
        /// Исключение (например, в batchmode) пробрасывается: вызывающий код считает его предупреждением.
        /// </summary>
        static List<string> CompilePlayerScripts(bool development, string tmp, List<string> assemblies)
        {
            var errors = new List<string>();
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            Application.LogCallback onLog = (condition, stackTrace, type) =>
            {
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                if (condition == null || condition.IndexOf("error CS", StringComparison.Ordinal) < 0) return;
                lock (errors) errors.Add(condition);
            };
            Application.logMessageReceivedThreaded += onLog;
            try
            {
                var settings = new ScriptCompilationSettings
                {
                    target = BuildTarget.Android,
                    group = BuildTargetGroup.Android,
                    options = development ? ScriptCompilationOptions.DevelopmentBuild : ScriptCompilationOptions.None,
                };
                var result = PlayerBuildInterface.CompilePlayerScripts(settings, tmp);
                if (result.assemblies != null) assemblies.AddRange(result.assemblies);
            }
            finally
            {
                Application.logMessageReceivedThreaded -= onLog;
            }
            lock (errors) return new List<string>(errors);
        }

        /// <summary>
        /// Недостающие части Android Build Support: AndroidPlayer (модуль не установлен), SDK, NDK, OpenJDK.
        /// Путь инструмента — из настроек External Tools (AndroidExternalToolsSettings, через отражение: сборка
        /// UnityEditor.Android.Extensions есть только с модулем), иначе встроенный каталог модуля.
        /// </summary>
        public static List<string> MissingAndroidModules()
        {
            var missing = new List<string>();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                missing.Add("AndroidPlayer");
                return missing;
            }
            string engine = PlaybackEngineDir();
            CheckTool(missing, "SDK", "sdkRootPath", engine);
            CheckTool(missing, "NDK", "ndkRootPath", engine);
            CheckTool(missing, "OpenJDK", "jdkRootPath", engine);
            return missing;
        }

        static string PlaybackEngineDir()
        {
            try
            {
                string dir = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None);
                if (!string.IsNullOrEmpty(dir)) return dir;
            }
            catch (Exception)
            {
                // Модуль не отвечает — ниже стандартный путь.
            }
            return Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer");
        }

        static void CheckTool(List<string> missing, string label, string property, string engineDir)
        {
            string path = ExternalToolPath(property);
            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(engineDir)) path = Path.Combine(engineDir, label);
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) missing.Add(label);
        }

        static string ExternalToolPath(string property)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try
                {
                    t = asm.GetType("UnityEditor.Android.AndroidExternalToolsSettings", false);
                }
                catch (Exception)
                {
                    continue;
                }
                if (t == null) continue;
                var p = t.GetProperty(property, BindingFlags.Public | BindingFlags.Static);
                if (p == null) return null;
                try
                {
                    return p.GetValue(null) as string;
                }
                catch (Exception)
                {
                    return null;
                }
            }
            return null;
        }

        static string FileSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        static string BuildJson(Variant[] variants, bool ok, string dir)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"result\": ").Append(JsonText.Str(ok ? "Succeeded" : "Failed")).Append(",\n");
            sb.Append("  \"outputDir\": ").Append(JsonText.Str(dir)).Append(",\n");
            sb.Append("  \"scriptingBackend\": ").Append(JsonText.Str(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android).ToString())).Append(",\n");
            sb.Append("  \"architectures\": ").Append(JsonText.Str(PlayerSettings.Android.targetArchitectures.ToString())).Append(",\n");
            sb.Append("  \"applicationIdentifier\": ").Append(JsonText.Str(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android))).Append(",\n");
            sb.Append("  \"bundleVersion\": ").Append(JsonText.Str(PlayerSettings.bundleVersion)).Append(",\n");
            sb.Append("  \"unityVersion\": ").Append(JsonText.Str(Application.unityVersion)).Append(",\n");
            sb.Append("  \"internalOnly\": true,\n");
            sb.Append("  \"builtAtUtc\": ").Append(JsonText.Str(DateTime.UtcNow.ToString("o"))).Append(",\n");
            sb.Append("  \"builds\": [");
            for (int i = 0; i < variants.Length; i++)
            {
                var v = variants[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    {\n");
                sb.Append("      \"variant\": ").Append(JsonText.Str(v.Name)).Append(",\n");
                sb.Append("      \"options\": ").Append(JsonText.Str(v.Options.ToString())).Append(",\n");
                sb.Append("      \"result\": ").Append(JsonText.Str(v.Result)).Append(",\n");
                sb.Append("      \"path\": ").Append(JsonText.Str(v.Path)).Append(",\n");
                sb.Append("      \"sizeBytes\": ").Append(JsonText.Num(v.SizeBytes)).Append(",\n");
                sb.Append("      \"totalSize\": ").Append(JsonText.Num(v.TotalSize)).Append(",\n");
                sb.Append("      \"sha256\": ").Append(JsonText.Str(v.Sha256)).Append(",\n");
                sb.Append("      \"totalErrors\": ").Append(JsonText.Num(v.TotalErrors)).Append(",\n");
                sb.Append("      \"totalWarnings\": ").Append(JsonText.Num(v.TotalWarnings)).Append(",\n");
                sb.Append("      \"errors\": ").Append(JsonText.Array(v.Errors)).Append(",\n");
                sb.Append("      \"warnings\": ").Append(JsonText.Array(v.Warnings)).Append(",\n");
                sb.Append("      \"devToolsIncluded\": ").Append(JsonText.Bool(v.DevToolsIncluded)).Append(",\n");
                sb.Append("      \"devToolsSource\": ").Append(JsonText.Str(v.DevToolsSource)).Append(",\n");
                sb.Append("      \"scriptCheck\": ").Append(JsonText.Str(v.ScriptCheck)).Append(",\n");
                sb.Append("      \"playerAssemblies\": ").Append(JsonText.Array(v.PlayerAssemblies)).Append(",\n");
                sb.Append("      \"playerAssemblyFiles\": ").Append(JsonText.Array(v.PlayerAssemblyFiles)).Append(",\n");
                sb.Append("      \"buildSeconds\": ").Append(v.BuildSeconds.ToString("0.0", CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("      \"internalOnly\": true,\n");
                sb.Append("      \"builtAtUtc\": ").Append(JsonText.Str(v.BuiltAtUtc)).Append('\n');
                sb.Append("    }");
            }
            sb.Append("\n  ]\n}\n");
            return sb.ToString();
        }

        static void WriteReport(string json) => File.WriteAllText(EditorPaths.Report(ReportFile), json, new UTF8Encoding(false));
    }
}
