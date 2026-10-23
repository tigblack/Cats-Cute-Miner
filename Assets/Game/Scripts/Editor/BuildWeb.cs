using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CatMiners.Editor
{
    public static class BuildWeb
    {
        /// <summary>Флаг сборки playable: компилирует CatMiners.Playable и включает его при старте (Docs/Playable/PLAYABLE_TZ.md §3.1).</summary>
        public const string PlayableDefine = "CATMINERS_PLAYABLE";

        /// <summary>
        /// JS-плагин магазина playable. Define constraints Unity у нативных плагинов (.jslib) не работают — только у управляемых
        /// DLL, поэтому включение решает делегат сборки: плагин попадает в сборку <see cref="Playable"/> и в любую сборку, где
        /// <see cref="PlayableDefine"/> есть среди активных scripting defines (настройки игрока WebGL, активный Build Profile), в
        /// остальные — нет. Без делегата он безвреден: в основной сборке его функции никто не вызывает (сборка
        /// CatMiners.Playable не компилируется), и emscripten их не связывает.
        /// </summary>
        public const string PlayablePluginPath = "Assets/Game/Scripts/Playable/Plugins/CatMinersPlayable.jslib";

        static bool s_IncludePlayablePlugin;

        [InitializeOnLoadMethod]
        static void RegisterPlayablePluginOnLoad() => EditorApplication.delayCall += () => RegisterPlayablePlugin();

        /// <summary>Делегат включения JS-плагина playable; false — плагина нет в проекте.</summary>
        static bool RegisterPlayablePlugin()
        {
            if (!(AssetImporter.GetAtPath(PlayablePluginPath) is PluginImporter plugin)) return false;
            plugin.SetIncludeInBuildDelegate(_ => s_IncludePlayablePlugin || PlayableDefineActive());
            return true;
        }

        /// <summary><see cref="PlayableDefine"/> среди активных defines (сборка из Build Profiles или с флагом в настройках игрока).</summary>
        static bool PlayableDefineActive()
        {
            var active = EditorUserBuildSettings.activeScriptCompilationDefines;
            if (active != null && active.Contains(PlayableDefine)) return true;
            string player = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL) ?? "";
            return player.Split(';', ',', ' ').Any(d => string.Equals(d.Trim(), PlayableDefine, StringComparison.Ordinal));
        }

        /// <summary>Параметры одной веб-сборки.</summary>
        sealed class WebBuild
        {
            public string Output, Template, Report;
            public string[] Defines;
            /// <summary>Заставка Unity выключена на время сборки и восстанавливается после неё.</summary>
            public bool NoSplash;
            /// <summary>Каталог основной веб-сборки (Builds/Web) запрещён: playable его не стирает и не перезаписывает.</summary>
            public bool KeepMainWeb;
        }

        [MenuItem("Cat Miners/Build Web for GitHub Pages")]
        public static void Pages() => Run(new WebBuild
        {
            Output = EditorPaths.Argument("-catminersOutput") ?? "Builds/Web",
            Template = "PROJECT:CatMiners",
            Report = "build-web.json",
        });

        /// <summary>
        /// Playable — первые 30 минут игры отдельной сборкой (ТЗ playable §3.1, §3.6): флаг <see cref="PlayableDefine"/>, шаблон
        /// CatMinersPlayable, без заставки Unity, каталог Builds/Playable и отчёт build-playable.json. Builds/Web не трогается.
        /// </summary>
        [MenuItem("Cat Miners/Build Playable (Web)")]
        public static void Playable() => Run(new WebBuild
        {
            Output = EditorPaths.Argument("-catminersOutput") ?? "Builds/Playable",
            Template = "PROJECT:CatMinersPlayable",
            Report = "build-playable.json",
            Defines = new[] { PlayableDefine },
            NoSplash = true,
            KeepMainWeb = true,
        });

        static void Run(WebBuild spec)
        {
            string output = Path.GetFullPath(spec.Output);
            string reports = EditorPaths.ReportsDir();
            Directory.CreateDirectory(reports);
            int exitCode = 1;
            string template = PlayerSettings.WebGL.template;
            bool splash = PlayerSettings.SplashScreen.show;
            try
            {
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                    throw new InvalidOperationException("WebGL Build Support is not installed.");
                if (!File.Exists(ProjectSetup.ScenePath))
                    throw new FileNotFoundException("Run Cat Miners/Setup Project first.", ProjectSetup.ScenePath);
                if (spec.KeepMainWeb && IsMainWebOutput(output))
                    throw new InvalidOperationException("Playable build must not write into Builds/Web: " + output);

                bool playableOnly = PlayableOnly(spec);
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
                PlayerSettings.WebGL.decompressionFallback = true;
                PlayerSettings.WebGL.nameFilesAsHashes = true;
                PlayerSettings.WebGL.dataCaching = true;
                if (!playableOnly) PlayerSettings.WebGL.template = spec.Template;
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);
                EditorUserBuildSettings.development = false;
                EditorUserBuildSettings.allowDebugging = false;
                EditorUserBuildSettings.connectProfiler = false;
                AssetDatabase.SaveAssets();
                // Настройки только этой сборки (шаблон playable, без заставки) — в памяти, без записи: прерванная сборка
                // (Unity закрыли) не оставляет их в ProjectSettings основной; finally возвращает прежние.
                if (playableOnly)
                {
                    PlayerSettings.WebGL.template = spec.Template;
                    if (spec.NoSplash) PlayerSettings.SplashScreen.show = false;
                }

                // Only the generated web files are published. Clean an earlier build to avoid
                // shipping obsolete hash-named files with every new release.
                if (Directory.Exists(output)) Directory.Delete(output, true);
                Directory.CreateDirectory(output);
                s_IncludePlayablePlugin = spec.Defines != null && spec.Defines.Contains(PlayableDefine);
                if (!RegisterPlayablePlugin() && s_IncludePlayablePlugin)
                    throw new FileNotFoundException("Playable store plugin is missing.", PlayablePluginPath);
                var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ProjectSetup.ScenePath },
                    locationPathName = output,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = BuildOptions.None,
                    extraScriptingDefines = spec.Defines
                });
                if (build.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Web build: " + build.summary.result + ", errors=" + build.summary.totalErrors);

                File.WriteAllText(Path.Combine(output, ".nojekyll"), "");
                var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path)).ToArray();
                if (!File.Exists(Path.Combine(output, "index.html")))
                    throw new InvalidOperationException("Web build has no index.html.");
                if (files.Any(file => file.Length >= 100L * 1024 * 1024))
                    throw new InvalidOperationException("A web build file exceeds GitHub's 100 MiB limit.");
                var report = JObject.FromObject(new
                {
                    result = "Succeeded",
                    path = output,
                    unityVersion = Application.unityVersion,
                    development = false,
                    compression = "Gzip",
                    decompressionFallback = true,
                    autoSyncPersistentDataPath = true,
                    sizeBytes = files.Sum(file => file.Length),
                    durationSeconds = build.summary.totalTime.TotalSeconds,
                    files = files.Select(file => new { name = Path.GetRelativePath(output, file.FullName).Replace('\\', '/'), sizeBytes = file.Length }),
                    checkedAtUtc = DateTime.UtcNow.ToString("o")
                });
                if (spec.Defines != null)
                {
                    report["template"] = spec.Template;
                    report["scriptingDefines"] = new JArray(spec.Defines.Cast<object>().ToArray());
                    report["splashScreen"] = !spec.NoSplash && splash;
                }
                WriteReport(reports, spec.Report, report);
                Debug.Log("[CatMiners] Web build ready: " + output);
                exitCode = 0;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                WriteReport(reports, spec.Report, JObject.FromObject(new { result = "Failed", error = error.ToString(), path = output }));
            }
            finally
            {
                s_IncludePlayablePlugin = false;
                // Pages() задаёт шаблон каждый раз сам, заставку — нет: настройки проекта возвращаются как были (запись — уже
                // прежних значений, на случай если сборка сохранила проект сама).
                if (PlayableOnly(spec))
                {
                    PlayerSettings.SplashScreen.show = splash;
                    PlayerSettings.WebGL.template = template;
                    AssetDatabase.SaveAssets();
                }
            }
            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        /// <summary>Сборка со своими настройками проекта (шаблон, заставка), которые не должны пережить её.</summary>
        static bool PlayableOnly(WebBuild spec) => spec.NoSplash || spec.Defines != null;

        /// <summary>Каталог основной веб-сборки: …/Builds/Web или любой каталог внутри него.</summary>
        static bool IsMainWebOutput(string output)
        {
            for (var dir = new DirectoryInfo(output); dir != null; dir = dir.Parent)
                if (string.Equals(dir.Name, "Web", StringComparison.OrdinalIgnoreCase) && dir.Parent != null &&
                    string.Equals(dir.Parent.Name, "Builds", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static void WriteReport(string reports, string file, JObject report) =>
            File.WriteAllText(Path.Combine(reports, file), report.ToString(Formatting.Indented) + "\n");
    }
}
