using UnityEngine;
using UnityEngine.InputSystem;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Точка входа сцены CoreGame. Ссылки (исходный JSON, шаблоны материалов, ассет ввода) назначает CatMiners.Editor.ProjectSetup.
    /// Нажатие Play запускает новую игру или продолжает сохранение.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public TextAsset BalanceJson;
        public Material LitMaterial;
        public Material UnlitMaterial;
        public Material ParticleMaterial;
        /// <summary>Шаблон материала пиксельных спрайтов (URP 2D Sprite-Unlit-Default): шейдер гарантированно попадает в сборку.</summary>
        public Material SpriteMaterial;
        /// <summary>Assets/Game/Input/CatMinersInput.inputactions: карты Gameplay и UI (ТЗ 13.3).</summary>
        public InputActionAsset InputActions;

        public static GameController Controller { get; private set; }
        public static Outpost.OutpostController OutpostController { get; private set; }
        /// <summary>Explicit development/test opt-in. Production always uses Outpost.</summary>
        public static bool LegacyMode;
        public bool LegacyDevelopmentMode;
        /// <summary>Ассет ввода текущей сцены; PointerInput берёт из него карту Gameplay.</summary>
        public static InputActionAsset ActiveInputActions { get; private set; }

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            ActiveInputActions = InputActions;
            var go = new GameObject("CatMiners");
            Integrations.NeutralAgeFlow.Begin(go, privacy => StartGame(go, privacy));
        }

        void StartGame(GameObject go, CatMiners.Core.Outpost.IntegrationPrivacy privacy)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Integrations.GameSdkRuntime.Begin(privacy);
#endif
            Controller = null;
            OutpostController = null;
            if (!Application.isMobilePlatform && (Application.isEditor || Debug.isDebugBuild) && (LegacyMode || LegacyDevelopmentMode))
            {
                Controller = go.AddComponent<GameController>();
                Controller.Init(this);
            }
            else
            {
                OutpostController = go.AddComponent<Outpost.OutpostController>();
                OutpostController.Init(this);
            }
        }
    }
}
