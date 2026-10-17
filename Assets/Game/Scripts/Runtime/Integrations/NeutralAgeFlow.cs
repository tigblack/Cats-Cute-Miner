using System;
using System.IO;
using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Integrations
{
    // Installation privacy state is separate from progress: reset/debug profiles cannot turn a child into an adult.
    public sealed class NeutralAgeFlow : MonoBehaviour
    {
        IntegrationPrivacy privacy;
        GameSdkConfiguration config;
        Action<IntegrationPrivacy> complete;
        string path;
        bool saveFailed, preferences;
        public static bool IsOpen { get; private set; }
        public static void OpenChoices(GameObject host)
        {
            if (IsOpen || GameSdkRuntime.Instance == null) return;
            var flow = host.AddComponent<NeutralAgeFlow>();
            flow.preferences = true;
            IsOpen = true;
            flow.config = GameSdkConfiguration.Load();
            flow.path = Path.Combine(Application.persistentDataPath, "cats-cute-miner-privacy.json");
            flow.privacy = JsonUtility.FromJson<IntegrationPrivacy>(JsonUtility.ToJson(GameSdkRuntime.Instance.Privacy));
            flow.complete = choices => { IsOpen = false; GameSdkRuntime.Instance?.ApplyPrivacy(choices); };
        }
        public static void Begin(GameObject host, Action<IntegrationPrivacy> next)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var flow = host.AddComponent<NeutralAgeFlow>();
            IsOpen = true;
            flow.complete = choices => { IsOpen = false; next(choices); };
            flow.config = GameSdkConfiguration.Load();
            flow.path = Path.Combine(Application.persistentDataPath, "cats-cute-miner-privacy.json");
            try { flow.privacy = File.Exists(flow.path) ? JsonUtility.FromJson<IntegrationPrivacy>(File.ReadAllText(flow.path)) : null; }
            catch { flow.privacy = null; }
            flow.privacy = flow.privacy ?? new IntegrationPrivacy();
            if (flow.privacy.Age != NeutralAge.Unselected && flow.privacy.ChoicesMade)
            {
                // Corrupt/unrecognized age values are protected as well.
                if (flow.privacy.Protected) flow.privacy.ChooseAge(flow.privacy.Age);
                flow.Finish();
            }
#else
            next(new IntegrationPrivacy());
#endif
        }
        void Finish()
        {
            if (!GameSdkConfiguration.AdsEnabled) privacy.Ads = false;
            try
            {
                File.WriteAllText(path + ".tmp", JsonUtility.ToJson(privacy));
                if (File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak");
                else File.Move(path + ".tmp", path);
            }
            catch { saveFailed = true; return; }
            var next = complete;
            complete = null;
            next?.Invoke(privacy);
            Destroy(this);
        }
        void Age(NeutralAge age) { privacy.ChooseAge(age); if (privacy.Protected) Finish(); }
        void OnGUI()
        {
            if (complete == null) return;
            var scale = Mathf.Max(1, Screen.height / 650f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float w = Screen.width / scale, h = Screen.height / scale;
            GUI.Box(new Rect(0, 0, w, h), "");
            GUILayout.BeginArea(new Rect(20, 30, w - 40, h - 60));
            // Documents are available before age selection and in protected mode, without SDK initialization.
            if (config.LegalValid)
            {
                if (GUILayout.Button("Game privacy policy", GUILayout.Height(36))) Application.OpenURL(config.privacyUrl);
                if (GUILayout.Button("Game terms", GUILayout.Height(36))) Application.OpenURL(config.termsUrl);
                GUILayout.Space(8);
            }
            if (saveFailed)
            {
                GUILayout.Label("Your choice could not be saved. You can still play with privacy protection.");
                if (GUILayout.Button("Play with protection", GUILayout.Height(48)))
                { var next = complete; complete = null; next(new IntegrationPrivacy { Age = NeutralAge.Unknown, ChoicesMade = true }); Destroy(this); }
                if (GUILayout.Button("Try again", GUILayout.Height(48))) Finish();
            }
            else if (privacy.Age == NeutralAge.Unselected)
            {
                GUILayout.Label("Choose your age group");
                GUILayout.Label("This helps us use the right privacy settings. All age groups can play the game.");
                if (GUILayout.Button("Under 13", GUILayout.Height(48))) Age(NeutralAge.Under13);
                if (GUILayout.Button("13–15", GUILayout.Height(48))) Age(NeutralAge.Age13To15);
                if (GUILayout.Button("16 or older", GUILayout.Height(48))) Age(NeutralAge.Age16OrOlder);
                if (GUILayout.Button("Prefer not to say", GUILayout.Height(48))) Age(NeutralAge.Unknown);
            }
            else if (preferences && privacy.Protected)
            {
                GUILayout.Label("Privacy protection is on. Analytics and ads are off.");
                if (GUILayout.Button("Back to game", GUILayout.Height(48))) Finish();
            }
            else
            {
                GUILayout.Label("Optional privacy choices");
                GUILayout.Label(GameSdkConfiguration.AdsEnabled ? "You can play with every option off. Choosing analytics does not give consent for ads." : "You can play with every option off.");
                if (config.LegalValid)
                {
                    // AppMetrica отложена (скилл публикации 2026-10-15): без своего ключа переключатель не показывается.
                    if (config.AppMetricaValid) privacy.AppMetrica = GUILayout.Toggle(privacy.AppMetrica, "AppMetrica: gameplay analytics");
                    privacy.Adjust = GUILayout.Toggle(privacy.Adjust, "Adjust: game activity, sessions and attribution");
                    privacy.Meta = GUILayout.Toggle(privacy.Meta, "Meta: app activity measurement");
                    if (GameSdkConfiguration.AdsEnabled) privacy.Ads = GUILayout.Toggle(privacy.Ads, "Optional rewarded ads (ad privacy choices follow)");
                }
                if (GUILayout.Button("Continue", GUILayout.Height(48))) { privacy.ChoicesMade = true; Finish(); }
                if (!preferences && GUILayout.Button("Play with protection", GUILayout.Height(48))) Age(NeutralAge.Unknown);
            }
            GUILayout.EndArea();
        }
    }
}
