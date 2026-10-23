using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Runtime.Outpost
{
    // Бонус за рекламу у левого края над нижней панелью (ТЗ 10.6): золотая кнопка с иконкой награды, значком телевизора и суммой,
    // под ней полоса оставшегося времени показа. Появляется «попом» и слегка покачивается; видна, пока у игры есть оферта и не
    // открыто окно. Тап — IOutpostGame.ClaimBonus; тосты награды — по событиям ядра BonusGranted и DynamiteLaunched.
    public sealed partial class OutpostHud
    {
        const float BonusWidth = 40 * Px, BonusHeight = 44 * Px, BonusBarHeight = 5 * Px;
        const float BonusPopSeconds = 0.25f;

        RectTransform m_BonusRoot;
        Ui.ButtonRefs m_Bonus;
        PixelBar m_BonusBar;
        Vector2 m_BonusHome;
        RewardedBonusOffer m_BonusShown;
        bool m_BonusVisible;
        float m_BonusPopT = 1f;
        RectTransform m_DamageBonusRoot;
        TextMeshProUGUI m_DamageBonusText;

        /// <summary>Кнопка бонуса на экране (тесты, съёмка).</summary>
        public bool BonusVisible => m_BonusVisible;
        public RectTransform BonusRect => m_BonusRoot;
        public bool DamageBonusVisible => m_DamageBonusRoot != null && m_DamageBonusRoot.gameObject.activeSelf &&
            m_Game?.Sim?.DamageBonusActive == true;
        public string DamageBonusLabel => m_DamageBonusText != null ? m_DamageBonusText.text : "";

        void BuildBonus()
        {
            m_BonusRoot = Ui.Rect(m_Safe, "Bonus");
            m_BonusRoot.anchorMin = m_BonusRoot.anchorMax = Vector2.zero;
            m_BonusRoot.pivot = new Vector2(0.5f, 0f);
            m_BonusRoot.sizeDelta = new Vector2(BonusWidth, BonusHeight + BonusBarHeight + Px);
            m_BonusHome = new Vector2(Margin + 2 * Px + BonusWidth / 2f, BottomUnits + Margin + 2 * Px);
            m_BonusRoot.anchoredPosition = m_BonusHome;
            m_Bonus = PixelUi.Button(m_BonusRoot, "button", "", PixelUi.RewardedButtonStyle, OnBonusTap, PixelUi.FontBody);
            m_Bonus.Rect.Anchor(0, 0, 1, 1, new Vector2(0f, BonusBarHeight + Px), Vector2.zero);
            m_Bonus.SetIcon("icon_coin");
            m_Bonus.Icon.rectTransform.anchoredPosition = new Vector2(0f, 7 * Px);
            m_Bonus.Label.rectTransform.Anchor(0, 0, 1, 0, new Vector2(2 * Px, 3 * Px), new Vector2(-2 * Px, 13 * Px));
            m_Bonus.Label.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_Bonus.Label, PixelUi.FontSmall, PixelUi.FontBody);
            // Телевизор в правом верхнем углу: награда — за просмотр рекламы.
            var tv = PixelUi.Icon(m_Bonus.Image.transform, "tv", "icon_ad_tv");
            tv.raycastTarget = false;
            var tvRt = tv.rectTransform;
            tvRt.anchorMin = tvRt.anchorMax = new Vector2(1f, 1f);
            tvRt.pivot = new Vector2(0.5f, 0.5f);
            tvRt.anchoredPosition = new Vector2(-3 * Px, -3 * Px);
            m_BonusBar = PixelUi.Bar(m_BonusRoot, "time", PixelBarFill.Gold);
            m_BonusBar.Rect.Anchor(0, 0, 1, 0, new Vector2(2 * Px, 0f), new Vector2(-2 * Px, BonusBarHeight));
            m_BonusRoot.gameObject.SetActive(false);

            // Separate from the offer button: other periodic rewards can appear while the boost is active.
            var badge = PixelUi.Panel(m_Safe, "DamageBonus", PixelPanel.Dark);
            badge.raycastTarget = false;
            m_DamageBonusRoot = badge.rectTransform;
            m_DamageBonusRoot.anchorMin = m_DamageBonusRoot.anchorMax = Vector2.zero;
            m_DamageBonusRoot.pivot = Vector2.zero;
            m_DamageBonusRoot.sizeDelta = new Vector2(78 * Px, 28 * Px);
            m_DamageBonusRoot.anchoredPosition = new Vector2(Margin + 2 * Px + BonusWidth + 3 * Px, m_BonusHome.y + BonusBarHeight + Px);
            var pickaxe = PixelUi.Icon(m_DamageBonusRoot, "pickaxe", "icon_pickaxe");
            pickaxe.raycastTarget = false;
            pickaxe.rectTransform.anchorMin = pickaxe.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            pickaxe.rectTransform.anchoredPosition = new Vector2(12 * Px, 0f);
            m_DamageBonusText = PixelUi.Text(m_DamageBonusRoot, "label", "", PixelUi.FontSmall,
                PixelUi.TextAccentLight, TextAlignmentOptions.Center, true, true);
            m_DamageBonusText.rectTransform.Anchor(0, 0, 1, 1, new Vector2(23 * Px, 2 * Px), new Vector2(-3 * Px, -2 * Px));
            m_DamageBonusText.raycastTarget = false;
            m_DamageBonusRoot.gameObject.SetActive(false);
        }

        static string BonusIcon(RewardedBonusKind kind)
        {
            switch (kind)
            {
                case RewardedBonusKind.Elixir: return "icon_elixir";
                case RewardedBonusKind.Gems: return "icon_gem";
                case RewardedBonusKind.Dynamite: return "icon_dynamite";
                case RewardedBonusKind.Damage: return "icon_pickaxe";
                default: return "icon_coin";
            }
        }

        static string BonusLabel(RewardedBonusOffer offer)
        {
            switch (offer.Kind)
            {
                case RewardedBonusKind.Gold: return "+" + OutpostUiText.Money(offer.Amount);
                case RewardedBonusKind.Dynamite: return "×1";
                case RewardedBonusKind.Damage: return "×2";
                default: return "+" + StoreTileView.FormatAmount((long)offer.Amount);
            }
        }

        /// <summary>Каждый кадр: видимость по оферте и окнам, смена вида награды, полоса времени, поп и покачивание.</summary>
        void TickBonus()
        {
            if (m_BonusRoot == null) return;
            var offer = m_Game != null ? m_Game.BonusOffer : default;
            bool hudVisible = !InTransition && !AnyWindowOpen && m_Bottom != null && m_Bottom.gameObject.activeSelf;
            bool active = m_Game?.Sim?.DamageBonusActive == true;
            bool damageOffer = offer.Valid && offer.Kind == RewardedBonusKind.Damage;
            m_DamageBonusRoot.gameObject.SetActive(hudVisible && (active || damageOffer));
            if (active)
            {
                long seconds = (long)m_Game.Sim.Profile.DamageBonusSecondsLeft.Ceil();
                Ui.SetText(m_DamageBonusText, Loc.F("outpost.bonus.damage.active", seconds / 60, seconds % 60));
            }
            else if (damageOffer) Ui.SetText(m_DamageBonusText, Loc.T("outpost.bonus.damage.offer"));
            bool show = offer.Valid && hudVisible;
            if (show != m_BonusVisible)
            {
                m_BonusVisible = show;
                m_BonusRoot.gameObject.SetActive(show);
                if (show) m_BonusPopT = 0f;
            }
            if (!show) return;
            if (offer.Kind != m_BonusShown.Kind || offer.Amount != m_BonusShown.Amount || !m_BonusShown.Valid)
            {
                m_BonusShown = offer;
                m_Bonus.SetIcon(BonusIcon(offer.Kind));
                m_Bonus.SetLabel(BonusLabel(offer));
                m_BonusPopT = 0f;
            }
            m_BonusBar.SetValue(m_Game.BonusTimeLeft01);
            float scale = 1f;
            if (m_BonusPopT < 1f)
            {
                m_BonusPopT = Mathf.Min(1f, m_BonusPopT + Time.unscaledDeltaTime / BonusPopSeconds);
                float u = m_BonusPopT;
                scale = u < 0.7f ? Mathf.Lerp(0.4f, 1.12f, u / 0.7f) : Mathf.Lerp(1.12f, 1f, (u - 0.7f) / 0.3f);
            }
            m_BonusRoot.localScale = new Vector3(scale, scale, 1f);
            // Покачивание целыми пикселями арта: зовёт тапнуть, но не дёргается.
            float bob = Mathf.Round(Mathf.Sin(Time.unscaledTime * 3f) * 1.5f) * Px;
            m_BonusRoot.anchoredPosition = m_BonusHome + new Vector2(0f, bob);
        }

        void OnBonusTap()
        {
            if (m_Game == null) return;
            var offer = m_Game.BonusOffer;
            if (TryPrepareRewardedAd(OnBonusTap, () => m_Game.BonusOffer.Valid &&
                m_Game.BonusOffer.Kind == offer.Kind && m_Game.BonusOffer.Amount == offer.Amount)) return;
            var result = m_Game.ClaimBonus();
            if (result.Ok)
            {
                m_BonusShown = default;
                return;
            }
            if (result.Reason == "unavailable") Toast(Loc.T("outpost.offline.ad_soon"));
            else Denied(result.Reason);
        }

        /// <summary>Тосты бонуса: «+N gold / gems / elixir» и «Dynamite incoming!».</summary>
        void OnBonusEvent(OutpostEvent e)
        {
            if (e.Type == OutpostEventType.BonusGranted)
            {
                switch (e.ItemId)
                {
                    case "gold": Toast(Loc.F("outpost.bonus.toast.gold", OutpostUiText.Money(e.Value))); break;
                    case "gems": Toast(Loc.F("outpost.store.toast.gems", StoreTileView.FormatAmount((long)e.Value))); break;
                    case "elixir": Toast(Loc.F("outpost.store.toast.elixir", StoreTileView.FormatAmount((long)e.Value))); break;
                    case "damage": Toast(Loc.T("outpost.bonus.damage.toast")); break;
                }
            }
            else if (e.Type == OutpostEventType.DynamiteLaunched) Toast(Loc.T("outpost.bonus.toast.dynamite"));
        }
    }
}
