using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CatMiners.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SafeArea = CatMiners.Runtime.SafeArea;

namespace CatMiners.Editor
{
    /// <summary>
    /// Идемпотентная настройка проекта (ТЗ 5.4): папки Assets/Game, TMP Essentials, шаблонные материалы, сцена CoreGame
    /// со ссылками и Canvas, ассет ввода CatMinersInput (project-wide actions, ТЗ 13.3), PlayerSettings и Build Settings.
    /// Меню Cat Miners/Setup Project; без GUI — -executeMethod CatMiners.Editor.ProjectSetup.RunBatch.
    /// </summary>
    public static class ProjectSetup
    {
        /// <summary>Версия настройки: при её смене сцена пересоздаётся (2 — ассет ввода, Canvas в сцене; 3 — материал пиксельных спрайтов; 4 — CatOutpostV2 по умолчанию).</summary>
        public const int Version = 4;
        public const string ScenePath = "Assets/Game/Scenes/CoreGame.unity";
        public const string BalancePath = "Assets/Game/Data/Source/json-data.json";
        public const string MaterialsDir = "Assets/Game/Art/Materials";
        public const string InputActionsPath = "Assets/Game/Input/CatMinersInput.inputactions";
        public const string TemplateInputActionsPath = "Assets/InputSystem_Actions.inputactions";
        public const string UiMapName = "UI";
        public const string MarkerPath = "ProjectSettings/CatMinersSetup.json";
        public const string CompanyName = "CatMiners";
        public const string ProductName = "Cat Miner: Idle Tycoon";
        public const string BundleId = "com.catminers.prototype";

        /// <summary>Папки внутри Assets/Game (ТЗ 5.4). Заглушки префабов и звуков не создаются: всё строится в рантайме.</summary>
        static readonly string[] GameFolders = { "Scripts", "Data", "Scenes", "Prefabs", "Art", "Art/Materials", "Audio", "Tests", "Input" };

        [MenuItem("Cat Miners/Setup Project")]
        public static void RunFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Cat Miners", "Остановите Play и повторите настройку.", "OK");
                return;
            }
            bool restart = Run(force: true);
            EditorUtility.DisplayDialog("Cat Miners", restart
                ? "Проект настроен, но ввод переключён на Input System. Перезапустите редактор, затем откройте Assets/Game/Scenes/CoreGame.unity и нажмите Play."
                : "Проект настроен. Откройте Assets/Game/Scenes/CoreGame.unity и нажмите Play.", "OK");
        }

        /// <summary>Для batchmode: настроить и выйти с кодом 0/1 (1 — ошибка или нужен перезапуск редактора).</summary>
        public static void RunBatch()
        {
            try
            {
                bool restart = Run(force: true);
                if (restart)
                {
                    Debug.LogWarning("[CatMiners] ProjectSetup: activeInputHandler переключён на Input System, перезапустите Unity и повторите настройку.");
                    EditorApplication.Exit(1);
                    return;
                }
                Debug.Log("[CatMiners] ProjectSetup: OK");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[CatMiners] ProjectSetup failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Версия из маркера ProjectSettings/CatMinersSetup.json; 0 — настройка не выполнялась.</summary>
        public static int InstalledVersion()
        {
            if (!File.Exists(MarkerPath)) return 0;
            var m = Regex.Match(File.ReadAllText(MarkerPath), "\"version\"\\s*:\\s*(\\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out int v) ? v : 0;
        }

        public static bool IsUpToDate() => InstalledVersion() == Version && File.Exists(ScenePath);

        /// <summary>Выполнить настройку. Возвращает true, если переключён activeInputHandler и редактор нужно перезапустить.</summary>
        public static bool Run(bool force)
        {
            OutpostSetup.ValidateData();
            bool outdated = InstalledVersion() != Version;
            foreach (var folder in GameFolders) EnsureFolderPath("Assets/Game/" + folder);
            ImportTmpEssentialsIfMissing();
            var lit = EnsureMaterial(MaterialsDir + "/CatMiners_Lit.mat", "Universal Render Pipeline/Lit");
            var unlit = EnsureMaterial(MaterialsDir + "/CatMiners_Unlit.mat", "Universal Render Pipeline/Unlit");
            var particles = EnsureMaterial(MaterialsDir + "/CatMiners_Particles.mat", "Universal Render Pipeline/Particles/Unlit");
            var sprite = EnsureMaterial(MaterialsDir + "/CatMiners_Sprite.mat", "Universal Render Pipeline/2D/Sprite-Unlit-Default");
            var input = LoadInputActions();
            if (force || outdated || !File.Exists(ScenePath)) CreateScene(lit, unlit, particles, sprite, input);
            bool restart = ApplyPlayerSettings();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssignProjectWideActions(input);
            RemoveTemplateLeftovers();
            File.WriteAllText(MarkerPath, "{\n  \"version\": " + Version + ",\n  \"note\": \"CatMiners ProjectSetup marker\"\n}\n");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return restart;
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        /// <summary>Создать папку вместе с недостающими родителями: "Assets/Game/Art/Materials".</summary>
        static void EnsureFolderPath(string path)
        {
            var parts = path.Split('/');
            string parent = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                EnsureFolder(parent, parts[i]);
                parent += "/" + parts[i];
            }
        }

        static void ImportTmpEssentialsIfMissing()
        {
            if (File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset")) return;
            var info = UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.ugui");
            string root = info != null ? info.resolvedPath : Path.GetFullPath("Packages/com.unity.ugui");
            string package = Path.Combine(root, "Package Resources", "TMP Essential Resources.unitypackage");
            if (!File.Exists(package))
            {
                Debug.LogWarning("[CatMiners] TMP Essential Resources не найдены: " + package);
                return;
            }
            int files = UnityPackageExtractor.Extract(package, Path.GetFullPath("."));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[CatMiners] TMP Essential Resources распакованы: " + files + " файлов");
        }

        static Material EnsureMaterial(string path, string shaderName)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning("[CatMiners] Шейдер не найден: " + shaderName);
                return null;
            }
            mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Ассет ввода с картами Gameplay и UI (ТЗ 13.3). Без него настройка не продолжается.</summary>
        static InputActionAsset LoadInputActions()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (asset == null && File.Exists(InputActionsPath))
            {
                AssetDatabase.ImportAsset(InputActionsPath, ImportAssetOptions.ForceSynchronousImport);
                asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            }
            if (asset == null) throw new FileNotFoundException("[CatMiners] Не найден ассет ввода " + InputActionsPath, InputActionsPath);
            if (asset.FindActionMap(PointerInput.GameplayMapName) == null || asset.FindActionMap(UiMapName) == null)
                throw new InvalidOperationException("[CatMiners] В " + InputActionsPath + " нужны карты " + PointerInput.GameplayMapName + " и " + UiMapName);
            return asset;
        }

        static void CreateScene(Material lit, Material unlit, Material particles, Material sprite, InputActionAsset input)
        {
            bool batch = Application.isBatchMode;
            // Открытую CoreGame пересоздаём на месте: сохранять новую сцену поверх загруженной нельзя.
            var openCore = default(Scene);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.path == ScenePath) openCore = s;
            }
            bool single = batch || (openCore.IsValid() && SceneManager.sceneCount == 1);
            if (!single && openCore.IsValid()) EditorSceneManager.CloseScene(openCore, true);
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, single ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 8f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.06f, 0.08f);
            camGo.AddComponent<AudioListener>();
            camGo.transform.SetPositionAndRotation(new Vector3(3.5f, 30f, -20f), Quaternion.Euler(CameraRig.Pitch, 0, 0));

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(52f, -28f, 0f);

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            var module = esGo.AddComponent<InputSystemUIInputModule>();
            AssignUiActions(module, input);

            CreateHudCanvas();

            var bootGo = new GameObject("Bootstrap");
            var boot = bootGo.AddComponent<GameBootstrap>();
            boot.LegacyDevelopmentMode = false;
            boot.BalanceJson = AssetDatabase.LoadAssetAtPath<TextAsset>(BalancePath);
            boot.LitMaterial = lit;
            boot.UnlitMaterial = unlit;
            boot.ParticleMaterial = particles;
            boot.SpriteMaterial = sprite;
            boot.InputActions = input;
            if (boot.BalanceJson == null) Debug.LogError("[CatMiners] Не найден " + BalancePath);

            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!single)
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            Debug.Log("[CatMiners] Сцена создана: " + ScenePath);
        }

        /// <summary>
        /// Модулю UI — карта UI из CatMinersInput (ТЗ 13.3). Ссылки на действия берутся из подассетов импортёра,
        /// иначе сцена сохранила бы ссылки на временные объекты.
        /// </summary>
        static void AssignUiActions(InputSystemUIInputModule module, InputActionAsset asset)
        {
            // Сначала сбросить ссылки: иначе сеттер actionsAsset заменит их временными InputActionReference,
            // а присваивание ссылки на то же самое действие модуль пропускает.
            module.point = null;
            module.leftClick = null;
            module.rightClick = null;
            module.middleClick = null;
            module.scrollWheel = null;
            module.move = null;
            module.submit = null;
            module.cancel = null;
            module.trackedDevicePosition = null;
            module.trackedDeviceOrientation = null;
            module.actionsAsset = asset;
            var refs = new Dictionary<string, InputActionReference>();
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(InputActionsPath))
            {
                if (!(obj is InputActionReference reference) || (reference.hideFlags & HideFlags.HideInHierarchy) != 0) continue;
                var action = reference.action;
                if (action != null && action.actionMap != null && action.actionMap.name == UiMapName) refs[action.name] = reference;
            }
            module.point = UiAction(refs, "Point");
            module.leftClick = UiAction(refs, "Click");
            module.rightClick = UiAction(refs, "RightClick");
            module.middleClick = UiAction(refs, "MiddleClick");
            module.scrollWheel = UiAction(refs, "ScrollWheel");
            module.move = UiAction(refs, "Navigate");
            module.submit = UiAction(refs, "Submit");
            module.cancel = UiAction(refs, "Cancel");
            module.trackedDevicePosition = UiAction(refs, "TrackedDevicePosition");
            module.trackedDeviceOrientation = UiAction(refs, "TrackedDeviceOrientation");
        }

        static InputActionReference UiAction(Dictionary<string, InputActionReference> refs, string name)
        {
            if (refs.TryGetValue(name, out var reference)) return reference;
            Debug.LogWarning("[CatMiners] В карте " + UiMapName + " ассета " + InputActionsPath + " нет действия " + name);
            return null;
        }

        /// <summary>Корневой Canvas HUD (ТЗ 5.4): Screen Space Overlay, CanvasScaler 1080×1920 (match 0.5), дочерний SafeArea.</summary>
        static void CreateHudCanvas()
        {
            var hudGo = new GameObject("HUD", typeof(RectTransform));
            var canvas = hudGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = hudGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            hudGo.AddComponent<GraphicRaycaster>();

            var safeGo = new GameObject("SafeArea", typeof(RectTransform));
            var safe = (RectTransform)safeGo.transform;
            safe.SetParent(hudGo.transform, false);
            safe.anchorMin = Vector2.zero;
            safe.anchorMax = Vector2.one;
            safe.offsetMin = Vector2.zero;
            safe.offsetMax = Vector2.zero;
            safeGo.AddComponent<SafeArea>();
        }

        /// <summary>Настройки 5.1. Возвращает true, если переключён activeInputHandler и нужен перезапуск редактора.</summary>
        static bool ApplyPlayerSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            // Google Play (скилл публикации 2026-10-15): ARM64 и ARMv7 — без 32-битной сборки Play прячет установку на бюджетных телефонах.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);
            return EnsureInputSystemHandler();
        }

        /// <summary>
        /// activeInputHandler = 1 (только Input System, ТЗ 5.1). Публичного API нет, поэтому через SerializedObject настроек игрока.
        /// Unity применяет значение только при запуске, поэтому после изменения редактор нужно перезапустить.
        /// </summary>
        static bool EnsureInputSystemHandler()
        {
            var objects = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (objects == null || objects.Length == 0 || objects[0] == null)
            {
                Debug.LogWarning("[CatMiners] Не удалось прочитать ProjectSettings/ProjectSettings.asset: activeInputHandler не проверен.");
                return false;
            }
            var settings = new SerializedObject(objects[0]);
            var handler = settings.FindProperty("activeInputHandler");
            if (handler == null)
            {
                Debug.LogWarning("[CatMiners] В PlayerSettings нет свойства activeInputHandler: значение не проверено.");
                return false;
            }
            if (handler.intValue == 1) return false;
            int previous = handler.intValue;
            handler.intValue = 1;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Debug.LogWarning("[CatMiners] activeInputHandler: " + previous + " → 1 (Input System). Перезапустите редактор: настройка вступает в силу только после перезапуска.");
            return true;
        }

        /// <summary>
        /// CatMinersInput — project-wide actions (ТЗ 13.3). Шаблонный InputSystem_Actions удаляется только после того,
        /// как назначение подтверждено: до этого на него ссылается EditorBuildSettings.
        /// </summary>
        static void AssignProjectWideActions(InputActionAsset asset)
        {
            if (InputSystem.actions != asset) InputSystem.actions = asset;
            if (InputSystem.actions != asset)
            {
                Debug.LogWarning("[CatMiners] Не удалось назначить " + InputActionsPath + " как project-wide actions; " + TemplateInputActionsPath + " не удалён.");
                return;
            }
            if (!File.Exists(TemplateInputActionsPath)) return;
            if (AssetDatabase.DeleteAsset(TemplateInputActionsPath)) Debug.Log("[CatMiners] Удалён шаблонный " + TemplateInputActionsPath);
            else Debug.LogWarning("[CatMiners] Не удалось удалить " + TemplateInputActionsPath);
        }

        static void RemoveTemplateLeftovers()
        {
            if (AssetDatabase.IsValidFolder("Assets/TutorialInfo")) AssetDatabase.DeleteAsset("Assets/TutorialInfo");
            if (File.Exists("Assets/Readme.asset")) AssetDatabase.DeleteAsset("Assets/Readme.asset");
        }
    }

    /// <summary>Однократная автонастройка в открытом редакторе после компиляции (в batchmode не запускается).</summary>
    [InitializeOnLoad]
    static class AutoSetup
    {
        static AutoSetup()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += Check;
        }

        static void Check()
        {
            // Редактор занят: проверка откладывается без записи в лог, а не пропускается до следующей перезагрузки домена.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Check;
                return;
            }
            if (ProjectSetup.IsUpToDate()) return;
            Debug.Log("[CatMiners] Первичная настройка проекта…");
            bool restart;
            try
            {
                restart = ProjectSetup.Run(force: false);
            }
            catch (Exception e)
            {
                Debug.LogError("[CatMiners] Автонастройка не выполнена (повторите через Cat Miners/Setup Project): " + e);
                return;
            }
            var active = SceneManager.GetActiveScene();
            if (!active.isDirty && (string.IsNullOrEmpty(active.path) || active.path.EndsWith("SampleScene.unity", StringComparison.Ordinal)))
                EditorSceneManager.OpenScene(ProjectSetup.ScenePath, OpenSceneMode.Single);
            if (restart) Debug.LogWarning("[CatMiners] Готово, но ввод переключён на Input System: перезапустите редактор, затем нажмите Play.");
            else Debug.Log("[CatMiners] Готово: сцена " + ProjectSetup.ScenePath + ". Нажмите Play.");
        }

        /// <summary>Play закончился — повторить отложенную проверку.</summary>
        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.delayCall += Check;
        }
    }
}
