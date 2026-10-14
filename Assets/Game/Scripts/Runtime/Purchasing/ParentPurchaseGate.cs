using System;
using CatMiners.Runtime.Integrations;
using UnityEngine;

namespace CatMiners.Runtime.Purchasing
{
    // Access challenge only. It does not alter age/privacy or constitute legal parental consent.
    public sealed class ParentPurchaseGate : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        Action success;
        int a, b;
        string answer = "";
        bool wrong;
        public static void Show(GameObject host, Action next)
        {
            if (IsOpen) return;
            if (GameSdkRuntime.Instance?.Privacy?.Protected == false) { next(); return; }
            var gate = host.AddComponent<ParentPurchaseGate>();
            IsOpen = true; gate.success = next; gate.a = UnityEngine.Random.Range(12, 30); gate.b = UnityEngine.Random.Range(3, 9);
        }
        void Finish(bool passed)
        {
            var next = success; success = null; IsOpen = false; Destroy(this); if (passed) next?.Invoke();
        }
        void OnGUI()
        {
            if (success == null) return;
            float scale = Mathf.Max(1, Screen.height / 650f); GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float w = Screen.width / scale, h = Screen.height / scale;
            GUI.Box(new Rect(0, 0, w, h), ""); GUILayout.BeginArea(new Rect(24, 40, w - 48, h - 80));
            GUILayout.Label("For parents: access purchases");
            GUILayout.Label("Purchases use real money. Complete this check to continue to Google Play.");
            GUILayout.Label(a + " × " + b + " = ?"); answer = GUILayout.TextField(answer, 8, GUILayout.Height(48));
            if (wrong) GUILayout.Label("That answer does not match. Try again or go back.");
            if (GUILayout.Button("Continue", GUILayout.Height(48))) { if (int.TryParse(answer, out var value) && value == a * b) Finish(true); else wrong = true; }
            if (GUILayout.Button("Back to game", GUILayout.Height(48))) Finish(false);
            GUILayout.EndArea();
        }
        void OnDestroy() { if (success != null) { success = null; IsOpen = false; } }
    }
}
