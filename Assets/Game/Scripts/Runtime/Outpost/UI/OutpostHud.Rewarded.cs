using System;
using CatMiners.Core.Text;
using CatMiners.Runtime.Integrations;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    public sealed partial class OutpostHud
    {
        OutpostDialog m_RewardedAd;
        WindowEntry m_RewardedAdEntry;
        Action m_RewardedAction;
        Func<bool> m_RewardedStillValid;
        GameSdkRuntime m_RewardedServices;
        string m_RewardedRun;
        bool m_RewardedLoading, m_RewardedAdultConfirmed;
        float m_RewardedWait;

        bool TryPrepareRewardedAd(Action ready, Func<bool> valid)
        {
            if (!OutpostController.AndroidRuntime) return false;
            var services = GameSdkRuntime.Instance;
            if (services?.RewardReady == true || services?.NeedsRewardRetry == true) return false;
            BeginRewardedAd(ready, valid);
            return true;
        }

        void BeginRewardedAd(Action ready, Func<bool> valid)
        {
            if (m_RewardedAction != null) return;
            var services = GameSdkRuntime.Instance;
            if (!GameSdkConfiguration.AdsEnabled || services?.Privacy == null)
            { Toast(Loc.T("outpost.ad.unavailable")); return; }
            if (services.Privacy.Protected && !services.Privacy.CanChooseAnalyticsAge)
            { Toast(Loc.T("outpost.ad.protected")); return; }
            if (m_RewardedAd == null)
            {
                m_RewardedAd = OutpostDialog.Create(m_Safe, "RewardedAd", CancelRewardedAd);
                m_RewardedAdEntry = new WindowEntry { Root = m_RewardedAd.Root, Back = CancelRewardedAd };
            }
            m_RewardedAction = ready;
            m_RewardedStillValid = valid;
            m_RewardedServices = services;
            m_RewardedRun = m_Game?.State?.Run?.RunId;
            m_RewardedAdultConfirmed = false;
            if (services.Privacy.Protected)
                ShowRewardedAdStep("outpost.ad.age", "outpost.ad.adult", () => {
                    m_RewardedAdultConfirmed = true;
                    StartRewardedAdPreparation();
                });
            else StartRewardedAdPreparation();
        }

        void ShowRewardedAdStep(string body, string action = null, Action tap = null)
        {
            m_RewardedLoading = false;
            m_RewardedAd.Begin(Loc.T("outpost.ad.title"), "icon_ad_tv", Loc.T(body));
            m_RewardedAd.SetButton(0, Loc.T("outpost.store.cancel"), PixelButton.Dark, CancelRewardedAd);
            if (action != null)
                m_RewardedAd.SetButton(1, Loc.T(action), PixelUi.RewardedButtonStyle, () => tap?.Invoke());
            var extra = m_RewardedAd.ButtonAt(1);
            if (extra != null) extra.Rect.gameObject.SetActive(action != null);
            m_RewardedAd.Show();
            Opened(m_RewardedAdEntry);
        }

        void StartRewardedAdPreparation()
        {
            if (!RewardedAdRequestValid()) { CancelRewardedAd(); return; }
            var error = NeutralAgeFlow.EnableRewardedAds(m_RewardedServices, m_RewardedAdultConfirmed);
            if (error != null)
            {
                ShowRewardedAdStep(error == "save" ? "outpost.ad.save" : "outpost.ad.unavailable",
                    "outpost.ad.retry", StartRewardedAdPreparation);
                return;
            }
            m_RewardedServices.PrepareRewardedAd();
            ShowRewardedAdStep("outpost.ad.loading");
            m_RewardedWait = 0f;
            m_RewardedLoading = true;
            TickRewardedAd();
        }

        bool RewardedAdRequestValid() => m_Game?.State?.Run != null && m_RewardedServices != null && m_RewardedServices == GameSdkRuntime.Instance &&
            m_Game?.State?.Run?.RunId == m_RewardedRun && !InTransition && !m_Game.InTransition &&
            (m_RewardedStillValid == null || m_RewardedStillValid());

        void TickRewardedAd()
        {
            if (m_RewardedAction == null) return;
            if (!RewardedAdRequestValid()) { CancelRewardedAd(); return; }
            m_RewardedAd.Relayout();
            if (!m_RewardedLoading) return;
            if (m_RewardedServices.IsRewardShowing) return;
            if (m_RewardedServices.RewardReady)
            {
                var action = m_RewardedAction;
                CancelRewardedAd();
                action();
                return;
            }
            // Native consent can remain open as long as the player needs. Count only foreground ad loading.
            if (Application.isFocused && m_RewardedServices.MaxState != ProviderState.Starting)
                m_RewardedWait += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            if (m_RewardedServices.MaxState == ProviderState.Failed || m_RewardedWait >= 30f)
                ShowRewardedAdStep("outpost.ad.unavailable", "outpost.ad.retry", StartRewardedAdPreparation);
        }

        void CancelRewardedAd()
        {
            m_RewardedAction = null;
            m_RewardedStillValid = null;
            m_RewardedServices = null;
            m_RewardedLoading = false;
            if (m_RewardedAd != null) HideDialog(m_RewardedAd, m_RewardedAdEntry);
        }
    }
}
