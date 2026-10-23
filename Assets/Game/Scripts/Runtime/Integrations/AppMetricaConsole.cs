#if CATMINERS_APPMETRICA_DIAGNOSTICS
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Runtime.Integrations
{
    // Local diagnostic APK only; no commands, cheats, consent overrides or raw log capture.
    public sealed class AppMetricaConsole : MonoBehaviour
    {
        bool opened, nativeOnly, follow = true;
        Vector2 scroll;
        string filter = "", visible = "";
        float nextRefresh;
        GUIStyle textStyle, titleStyle, buttonStyle;
        GameObject inputBlocker;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var host = new GameObject("AppMetrica diagnostic console");
            DontDestroyOnLoad(host);
            host.AddComponent<AppMetricaConsole>();
#if UNITY_ANDROID && !UNITY_EDITOR
            AppMetricaNativeLog.Start();
#endif
        }
        void Awake()
        {
            inputBlocker = new GameObject("Diagnostic modal input blocker", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));
            inputBlocker.transform.SetParent(transform, false);
            var canvas = inputBlocker.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            inputBlocker.GetComponent<Image>().color = new Color(0.025f, 0.03f, 0.05f, 0.98f);
            inputBlocker.SetActive(false);
        }
        void Update()
        {
            if (!opened || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            var lines = nativeOnly ? AppMetricaNativeLog.Snapshot() :
                AppMetricaDiagnostics.Snapshot().Concat(AppMetricaNativeLog.Snapshot()).ToArray();
            visible = string.Join("\n", lines.Where(x => string.IsNullOrEmpty(filter) || x.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0));
            if (follow) scroll.y = float.MaxValue;
        }
        void OnGUI()
        {
            float scale = Screen.width / 420f;
            var oldMatrix = GUI.matrix;
            int oldDepth = GUI.depth;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUI.depth = -32000;
            try
            {
                if (textStyle == null)
                {
                    textStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, richText = false };
                    titleStyle = new GUIStyle(textStyle) { fontSize = 15, fontStyle = FontStyle.Bold };
                    buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13 };
                }
                float top = (Screen.height - Screen.safeArea.yMax) / scale + 8;
                float bottom = (Screen.height - Screen.safeArea.yMin) / scale - 8;
                if (!opened)
                {
                    if (GUI.Button(new Rect(8, top, 78, 38), "AM logs", buttonStyle))
                    {
                        opened = true; inputBlocker.SetActive(true); nextRefresh = 0;
                        if (GameSdkRuntime.Instance?.AppMetricaState == ProviderState.Initialized)
                            AppMetricaDiagnostics.RefreshNativeActivation();
                    }
                    return;
                }
                // Full opaque modal intercepts taps before the HUD underneath it.
                GUI.Box(new Rect(4, top, 412, bottom - top), GUIContent.none);
                GUI.Label(new Rect(12, top + 6, 315, 25), "AppMetrica · diagnostic APK", titleStyle);
                if (GUI.Button(new Rect(345, top + 4, 61, 30), "Close", buttonStyle)) { opened = false; inputBlocker.SetActive(false); return; }
                string status = "App 6374386 · " + Application.version + "\n" + AppMetricaDiagnostics.Summary + "\n" + AppMetricaNativeLog.Summary;
                GUI.Label(new Rect(12, top + 38, 394, 115), status, textStyle);
                GUI.Label(new Rect(12, top + 151, 394, 38), "Submitted = SDK call returned. NativeAccepted = SDK received accepted server response.", textStyle);
                if (GUI.Button(new Rect(12, top + 193, 121, 32), nativeOnly ? "Native only" : "All AM logs", buttonStyle)) { nativeOnly = !nativeOnly; nextRefresh = 0; }
                if (GUI.Button(new Rect(140, top + 193, 121, 32), follow ? "Follow: on" : "Follow: off", buttonStyle)) follow = !follow;
                if (GUI.Button(new Rect(268, top + 193, 137, 32), "Copy safe logs", buttonStyle)) GUIUtility.systemCopyBuffer = status + "\n" + visible;
                GUI.Label(new Rect(12, top + 233, 44, 25), "Filter", textStyle);
                string changed = GUI.TextField(new Rect(59, top + 230, 346, 29), filter, 64);
                if (changed != filter) { filter = changed; nextRefresh = 0; }
                var viewport = new Rect(12, top + 268, 394, Mathf.Max(80, bottom - top - 280));
                float height = Mathf.Max(viewport.height, textStyle.CalcHeight(new GUIContent(visible), 369));
                scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0, 0, 370, height));
                GUI.Label(new Rect(0, 0, 369, height), visible, textStyle);
                GUI.EndScrollView();
                var e = Event.current;
                if (e.type == EventType.MouseDrag && viewport.Contains(e.mousePosition))
                { follow = false; scroll.y = Mathf.Clamp(scroll.y - e.delta.y, 0, Mathf.Max(0, height - viewport.height)); e.Use(); }
            }
            finally { GUI.matrix = oldMatrix; GUI.depth = oldDepth; }
        }
    }
}
#endif
