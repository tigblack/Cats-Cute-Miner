using System;
using System.IO;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using CatMiners.Runtime.Outpost;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CatMiners.Runtime.Integrations
{
    // First-run consent precedes gameplay. The native CMP remains the only source of regional consent.
    public sealed class RewardedStartupFlow : MonoBehaviour
    {
        static RewardedStartupFlow current;
        IntegrationPrivacy privacy;
        GameSdkConfiguration config;
        Action<IntegrationPrivacy> complete;
        string path;
        GameObject canvasObject;
        OutpostDialog dialog;
        GameSdkRuntime services;
        bool waiting;
        float loadingSeconds;

        public static bool IsOpen => current != null;
        public static bool NeedsChoice(IntegrationPrivacy privacy) => GameSdkConfiguration.AdsEnabled && privacy != null &&
            !privacy.RewardedStartupChoiceMade && !privacy.MayInitialize(privacy.Ads) &&
            (privacy.CanChooseAnalyticsAge || !privacy.Protected);

        public static RewardedStartupFlow Begin(GameObject host, IntegrationPrivacy privacy,
            Action<IntegrationPrivacy> next, string storagePath = null)
        {
            if (!GameSdkConfiguration.AdsEnabled) { next(privacy); return null; }
            if (current != null) return current;
            var flow = host.AddComponent<RewardedStartupFlow>();
            current = flow;
            flow.privacy = privacy;
            flow.config = GameSdkConfiguration.Load();
            flow.services = GameSdkRuntime.Instance;
            flow.complete = next;
            flow.path = storagePath ?? Path.Combine(Application.persistentDataPath, "cats-cute-miner-privacy.json");
            if (NeedsChoice(privacy)) flow.ShowChoice();
            else flow.Launch(privacy);
            return flow;
        }

        void EnsureUi()
        {
            if (canvasObject != null) return;
            canvasObject = new GameObject("Startup privacy", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var background = canvasObject.AddComponent<Image>();
            background.color = PixelPalette.Ink0;
            var safe = Ui.Rect(canvasObject.transform, "SafeArea").Anchor(0, 0, 1, 1);
            safe.gameObject.AddComponent<SafeArea>();
            dialog = OutpostDialog.Create(safe, "StartupAdChoice");
            var eventSystem = FindAnyObjectByType<EventSystem>();
            if (eventSystem == null) eventSystem = new GameObject("EventSystem").AddComponent<EventSystem>();
            var input = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (input == null) input = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            if (input.actionsAsset == null) input.AssignDefaultActions();
        }

        void ShowChoice()
        {
            EnsureUi();
            dialog.Begin(Loc.T("outpost.ad.startup.title"), "icon_ad_tv",
                Loc.T(config.LegalValid ? "outpost.ad.startup.body" : "outpost.ad.unavailable"));
            var links = dialog.UseExtra(24 * PixelUi.Px);
            var policy = PixelUi.Button(links, "PrivacyPolicy", Loc.T("outpost.ad.startup.policy"), PixelButton.Dark,
                () => { if (config.LegalValid) Application.OpenURL(config.privacyUrl); }, PixelUi.FontBody);
            policy.Rect.Anchor(0, 0, 0.49f, 1);
            var terms = PixelUi.Button(links, "TermsOfUse", Loc.T("outpost.ad.startup.terms"), PixelButton.Dark,
                () => { if (config.LegalValid) Application.OpenURL(config.termsUrl); }, PixelUi.FontBody);
            terms.Rect.Anchor(0.51f, 0, 1, 1);
            dialog.SetButton(0, Loc.T("outpost.ad.startup.skip"), PixelButton.Dark, ContinueWithoutAds);
            dialog.SetButton(1, Loc.T("outpost.ad.startup.confirm"), PixelUi.RewardedButtonStyle, Confirm)
                .SetInteractable(config.LegalValid);
            dialog.Show();
        }

        void Confirm()
        {
            if (!config.LegalValid || complete == null) return;
            var selected = NeutralAgeFlow.RewardedAdChoice(privacy, true);
            if (selected == null) { ContinueWithoutAds(); return; }
            if (!NeutralAgeFlow.WritePrivacy(path, selected))
            { dialog.SetBody(Loc.T("outpost.ad.save")); dialog.Relayout(); return; }
            Launch(selected);
        }

        void ContinueWithoutAds()
        {
            if (complete == null) return;
            var selected = JsonUtility.FromJson<IntegrationPrivacy>(JsonUtility.ToJson(privacy));
            selected.Ads = false;
            selected.RewardedStartupChoiceMade = true;
            // Declining optional ads must not block gameplay when storage is unavailable.
            if (!NeutralAgeFlow.WritePrivacy(path, selected)) selected.RewardedStartupChoiceMade = false;
            Finish(selected);
        }

        void Launch(IntegrationPrivacy selected)
        {
            privacy = selected;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (selected.MayInitialize(selected.Ads))
            {
                EnsureUi();
                services = GameSdkRuntime.Begin(selected);
                StartWaiting();
                return;
            }
#endif
            Finish(selected);
        }

        void StartWaiting()
        {
            dialog.UseExtra(0);
            dialog.Begin(Loc.T("outpost.ad.startup.title"), "icon_ad_tv", Loc.T("outpost.ad.startup.preparing"));
            dialog.SetButton(0, Loc.T("outpost.ad.startup.play"), PixelButton.Dark, () => Finish(privacy));
            dialog.HideButtonsFrom(1);
            dialog.Show();
            waiting = true;
            loadingSeconds = 0f;
            services.PrepareRewardedAd();
        }

        void Update()
        {
            if (dialog != null) dialog.Relayout();
            if (!waiting || services == null || complete == null) return;
            if (services.RewardReady) { Finish(privacy); return; }
            if (services.IsRewardShowing) return;
            if (Application.isFocused && services.MaxState != ProviderState.Starting)
                loadingSeconds += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            if (services.MaxState == ProviderState.Failed || services.MaxState == ProviderState.WaitingForConfiguration ||
                services.MaxState == ProviderState.Initialized && services.RewardStatus == "unavailable" || loadingSeconds >= 15f)
            {
                waiting = false;
                dialog.SetBody(Loc.T("outpost.ad.unavailable"));
                dialog.SetButton(1, Loc.T("outpost.ad.retry"), PixelUi.RewardedButtonStyle, StartWaiting);
                dialog.Relayout();
            }
        }

        void Finish(IntegrationPrivacy selected)
        {
            if (complete == null) return;
            var next = complete;
            complete = null;
            waiting = false;
            if (current == this) current = null;
            if (canvasObject != null) { canvasObject.SetActive(false); Destroy(canvasObject); }
            Destroy(this);
            next(selected);
        }

        void OnDestroy()
        {
            complete = null;
            if (current == this) current = null;
            if (canvasObject != null) { canvasObject.SetActive(false); Destroy(canvasObject); }
        }
    }
}
