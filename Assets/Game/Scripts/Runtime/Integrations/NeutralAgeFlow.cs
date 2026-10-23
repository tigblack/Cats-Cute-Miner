using System;
using System.IO;
using Newtonsoft.Json.Linq;
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
        bool saveFailed;
        bool showOptionalServices;
        public static bool IsOpen { get; private set; }
        public static void OpenChoices(GameObject host, bool rewardedAds = false)
        {
            if (IsOpen || GameSdkRuntime.Instance == null) return;
            var flow = host.AddComponent<NeutralAgeFlow>();
            IsOpen = true;
            flow.config = GameSdkConfiguration.Load();
            flow.path = Path.Combine(Application.persistentDataPath, "cats-cute-miner-privacy.json");
            flow.privacy = JsonUtility.FromJson<IntegrationPrivacy>(JsonUtility.ToJson(GameSdkRuntime.Instance.Privacy));
            flow.showOptionalServices = rewardedAds;
            flow.complete = choices => { IsOpen = false; GameSdkRuntime.Instance?.ApplyPrivacy(choices); };
        }
        public static void Begin(GameObject host, Action<IntegrationPrivacy> next)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var saved = LoadStartupPrivacy(Path.Combine(Application.persistentDataPath, "cats-cute-miner-privacy.json"));
            RewardedStartupFlow.Begin(host, saved, next);
#else
            next(ProtectedDefaults());
#endif
        }

        // Stored choices stay separate from progress. Android startup may collect the
        // explicit ad choice before gameplay; optional providers retain their own eligibility.
        public static IntegrationPrivacy LoadStartupPrivacy(string path)
        {
            if (!File.Exists(path)) return ProtectedDefaults();
            IntegrationPrivacy saved = null;
            foreach (var candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    string json = File.ReadAllText(candidate);
                    var document = JObject.Parse(json);
                    saved = JsonUtility.FromJson<IntegrationPrivacy>(json);
                    if (saved == null) throw new InvalidDataException();
                    var disabled = document[nameof(IntegrationPrivacy.AppMetricaDisabled)];
                    if (disabled != null && disabled.Type != JTokenType.Boolean) throw new InvalidDataException();
                    // Preserve a previously saved refusal by an eligible player.
                    // New records always serialize AppMetricaDisabled explicitly.
                    if (disabled == null && saved.Age == NeutralAge.Age16OrOlder && saved.ChoicesMade && !saved.AppMetrica)
                        saved.AppMetricaDisabled = true;
                    break;
                }
                catch { saved = null; }
            }
            if (saved == null) { var fallback = ProtectedDefaults(); fallback.AppMetricaDisabled = true; return fallback; }
            if (saved.Age == NeutralAge.Unselected || !saved.ChoicesMade || !Enum.IsDefined(typeof(NeutralAge), saved.Age))
            { var fallback = ProtectedDefaults(); fallback.AppMetricaDisabled = saved.AppMetricaDisabled; return fallback; }
            if (saved.Protected) saved.ChooseAge(saved.Age);
            if (!GameSdkConfiguration.AdsEnabled) saved.Ads = false;
            return saved;
        }

        static IntegrationPrivacy ProtectedDefaults()
        {
            var defaults = new IntegrationPrivacy();
            defaults.ChooseAge(NeutralAge.Unknown);
            return defaults;
        }
        // A reward-button tap enables only the requested ad service. The native CMP still owns consent.
        // An unknown age requires an explicit adult choice; an established protected age cannot be replaced.
        public static string EnableRewardedAds(GameSdkRuntime runtime, bool confirmAdult = false, string storagePath = null)
        {
            if (!GameSdkConfiguration.AdsEnabled || runtime?.Privacy == null) return "unavailable";
            var draft = RewardedAdChoice(runtime.Privacy, confirmAdult);
            if (draft == null) return "age";
            if (!WritePrivacy(storagePath ?? Path.Combine(Application.persistentDataPath, "cats-cute-miner-privacy.json"), draft)) return "save";
            runtime.ApplyPrivacy(draft);
            return null;
        }
        internal static IntegrationPrivacy RewardedAdChoice(IntegrationPrivacy previous, bool confirmAdult)
        {
            var draft = JsonUtility.FromJson<IntegrationPrivacy>(JsonUtility.ToJson(previous));
            if (draft.Protected)
            {
                if (!draft.CanChooseAnalyticsAge || !confirmAdult) return null;
                if (!draft.TryChooseAnalyticsAge(NeutralAge.Age16OrOlder)) return null;
            }
            if (!draft.ChoicesMade) draft.AppMetrica = draft.Adjust = draft.Meta = false;
            draft.Ads = draft.ChoicesMade = true;
            draft.RewardedStartupChoiceMade = true;
            return draft;
        }
        internal static bool WritePrivacy(string destination, IntegrationPrivacy draft)
        {
            try
            {
                File.WriteAllText(destination + ".tmp", JsonUtility.ToJson(draft));
                if (File.Exists(destination)) File.Replace(destination + ".tmp", destination, destination + ".bak");
                else File.Move(destination + ".tmp", destination);
                return true;
            }
            catch { return false; }
        }
        void Finish()
        {
            if (privacy.Protected) privacy.ChooseAge(privacy.Age);
            if (!GameSdkConfiguration.AdsEnabled) privacy.Ads = false;
            if (!WritePrivacy(path, privacy)) { saveFailed = true; return; }
            var next = complete;
            complete = null;
            next?.Invoke(privacy);
            Destroy(this);
        }
        void SelectAnalyticsAge(NeutralAge age)
        {
            if (!privacy.TryChooseAnalyticsAge(age)) return;
            if (privacy.Protected) Finish();
        }
        void CloseWithoutSaving()
        {
            complete = null;
            IsOpen = false;
            Destroy(this);
        }
        void OnDestroy()
        {
            // Closing/destroying the host must not leave gameplay locked.
            if (complete == null) return;
            complete = null;
            IsOpen = false;
        }
        void OnGUI()
        {
            if (complete == null) return;
            var scale = Mathf.Max(1, Screen.height / 650f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float w = Screen.width / scale, h = Screen.height / scale;
            GUI.Box(new Rect(0, 0, w, h), "");
            GUILayout.BeginArea(new Rect(20, 30, w - 40, h - 60));
            GUILayout.Label("Analytics & privacy");
            if (config.LegalValid)
            {
                if (GUILayout.Button("Game privacy policy", GUILayout.Height(36))) Application.OpenURL(config.privacyUrl);
                if (GUILayout.Button("Game terms", GUILayout.Height(36))) Application.OpenURL(config.termsUrl);
                GUILayout.Space(8);
            }
            if (saveFailed)
            {
                GUILayout.Label("Your settings could not be saved. Your previous settings still apply.");
                if (GUILayout.Button("Try again", GUILayout.Height(48))) Finish();
                if (GUILayout.Button("Cancel", GUILayout.Height(48))) CloseWithoutSaving();
            }
            else
            {
                GUILayout.Label("Gameplay and session measurement starts automatically. No privacy window is required to play.");
                privacy.AppMetricaDisabled = !GUILayout.Toggle(!privacy.AppMetricaDisabled, "AppMetrica: gameplay and sessions", GUILayout.Height(36));
                GUILayout.Label("Advertising identifiers and device location collection are disabled. You can turn gameplay measurement off here.");
                showOptionalServices = GUILayout.Toggle(showOptionalServices, "Additional optional services", GUILayout.Height(36));
                if (showOptionalServices && privacy.CanChooseAnalyticsAge)
                {
                    GUILayout.Label("Age is used only for additional services. Choosing it does not enable them.");
                    if (GUILayout.Button("Under 13", GUILayout.Height(36))) SelectAnalyticsAge(NeutralAge.Under13);
                    if (GUILayout.Button("13–15", GUILayout.Height(36))) SelectAnalyticsAge(NeutralAge.Age13To15);
                    if (GUILayout.Button("16 or older", GUILayout.Height(36))) SelectAnalyticsAge(NeutralAge.Age16OrOlder);
                    if (GUILayout.Button("Prefer not to say", GUILayout.Height(36))) SelectAnalyticsAge(NeutralAge.Unknown);
                }
                else if (showOptionalServices && privacy.Protected)
                    GUILayout.Label("Additional services are disabled for this age group.");
                else if (showOptionalServices && config.LegalValid)
                {
                    privacy.Adjust = GUILayout.Toggle(privacy.Adjust, "Adjust: game activity, sessions and attribution", GUILayout.Height(36));
                    privacy.Meta = GUILayout.Toggle(privacy.Meta, "Meta: app activity measurement", GUILayout.Height(36));
                    if (GameSdkConfiguration.AdsEnabled) privacy.Ads = GUILayout.Toggle(privacy.Ads, "Optional rewarded ads (ad privacy choices follow)");
                }
                GUILayout.Label("Changes apply after saving. Additional services do not receive earlier activity.");
                if (GameSdkRuntime.Instance?.CanManageAdPrivacy == true &&
                    GUILayout.Button("Manage ad privacy", GUILayout.Height(48)))
                {
                    var services = GameSdkRuntime.Instance;
                    CloseWithoutSaving();
                    services.ShowAdPrivacyChoices();
                }
                if (GUILayout.Button("Save settings", GUILayout.Height(48))) { privacy.ChoicesMade = true; Finish(); }
                if (GUILayout.Button("Cancel", GUILayout.Height(48))) CloseWithoutSaving();
            }
            GUILayout.EndArea();
        }
    }
}
