using System;
using System.Collections.Generic;
using System.Globalization;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Runtime.Outpost
{
    public sealed partial class OutpostHud
    {
        OutpostDialog m_FactoryDialog;
        WindowEntry m_FactoryEntry, m_FactoryChestEntry;
        CardView m_FactoryCard;
        RectTransform m_FactoryExtra;
        TextMeshProUGUI m_FactoryRarity, m_FactoryLevel, m_FactoryState, m_FactoryEffect, m_FactoryElixir;
        Image m_FactoryOwnedCheck, m_FactoryLevelCheck;
        PixelIconText m_FactoryCopyPrice, m_FactoryElixirPrice;
        int m_FactoryId = -1;
        ulong m_FactorySignature;

        readonly Queue<OutpostEvent> m_FactoryChestQueue = new Queue<OutpostEvent>();
        readonly Queue<OutpostEvent> m_DeferredMineChests = new Queue<OutpostEvent>();
        readonly List<ChestCardTile> m_FactoryRewardTiles = new List<ChestCardTile>();
        RectTransform m_FactoryChestRoot, m_FactoryChestAnimation, m_FactoryChestLid, m_FactoryChestBase;
        RectTransform m_FactoryRewardViewport, m_FactoryRewardContent;
        readonly List<Image> m_FactoryChestStars = new List<Image>();
        TextMeshProUGUI m_FactoryChestTitle, m_FactoryChestSubtitle;
        PixelIconText m_FactoryChestElixir;
        Ui.ButtonRefs m_FactoryChestContinue;
        OutpostEvent m_FactoryChestEvent;
        float m_FactoryChestStarted;
        bool m_FactoryRewardsShown;
        int m_FactoryRewardCount;

        public bool FactoryDetailOpen => m_FactoryDialog != null && m_FactoryDialog.Visible;
        public int FactoryDetailId => FactoryDetailOpen ? m_FactoryId : -1;
        public string FactoryManagerCardId => FactoryDetailOpen ? m_FactoryCard?.Def.Id : null;
        public bool FactoryChestOpen => m_FactoryChestRoot != null && m_FactoryChestRoot.gameObject.activeSelf;
        public bool FactoryChestRewardsVisible => FactoryChestOpen && m_FactoryRewardsShown;
        bool FactoryChestPending => FactoryChestOpen || m_FactoryChestQueue.Count > 0 || m_DeferredMineChests.Count > 0;

        void BuildFactoryDialogs()
        {
            m_FactoryDialog = OutpostDialog.Create(m_Safe, "FactoryDetail", HideFactory);
            m_FactoryEntry = new WindowEntry { Root = m_FactoryDialog.Root, Back = HideFactory };
            m_FactoryDialog.Window.ButtonsHeight = 38 * Px;
            m_FactoryExtra = m_FactoryDialog.UseExtra(202 * Px);
            m_FactoryRarity = FactoryTextBlock("rarity", 0.48f, 0, 18);
            m_FactoryLevel = FactoryTextBlock("requiredLevel", 0.48f, 42, 24);
            m_FactoryOwnedCheck = PixelUi.Icon(m_FactoryExtra, "ownedCheck", "icon_check");
            m_FactoryLevelCheck = PixelUi.Icon(m_FactoryExtra, "levelCheck", "icon_check");
            PlaceFactoryCheck(m_FactoryOwnedCheck, 28);
            PlaceFactoryCheck(m_FactoryLevelCheck, 76);
            m_FactoryElixir = FactoryTextBlock("elixirBalance", 0.48f, 90, 21);
            m_FactoryState = FactoryTextBlock("state", 0.48f, 114, 28);
            m_FactoryEffect = FactoryTextBlock("effect", 0, 151, 46);
            var upgrade = m_FactoryDialog.SetButton(0, Loc.T("cards.detail.upgrade"), PixelButton.Green, UpgradeFactoryCard);
            upgrade.Label.rectTransform.Anchor(0, 0.48f, 1, 1, new Vector2(Px, 0), new Vector2(-Px, -Px));
            m_FactoryCopyPrice = PixelUi.IconText(upgrade.Content, "copiesPrice", "icon_card_small", "", PixelUi.FontBody, PixelUi.TextColor);
            m_FactoryElixirPrice = PixelUi.IconText(upgrade.Content, "elixirPrice", "icon_elixir", "", PixelUi.FontBody, PixelUi.TextColor);
            m_FactoryCopyPrice.Root.anchorMin = m_FactoryCopyPrice.Root.anchorMax = new Vector2(0.29f, 0);
            m_FactoryCopyPrice.Root.anchoredPosition = new Vector2(0, 10 * Px);
            m_FactoryElixirPrice.Root.anchorMin = m_FactoryElixirPrice.Root.anchorMax = new Vector2(0.76f, 0);
            m_FactoryElixirPrice.Root.anchoredPosition = new Vector2(0, 10 * Px);
            m_FactoryDialog.SetButton(1, Loc.T("factories.activate"), PixelButton.Gold, OnFactoryAction);
            AddDeniedTap(upgrade, OnFactoryUpgradeDenied);
            BuildFactoryChest();
        }

        TextMeshProUGUI FactoryTextBlock(string name, float left, float top, float height)
        {
            var text = PixelUi.Text(m_FactoryExtra, name, "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            text.rectTransform.Anchor(left, 1, 1, 1, new Vector2(2 * Px, -(top + height) * Px), new Vector2(-2 * Px, -top * Px));
            Ui.AutoSize(text, PixelUi.FontSmall, PixelUi.FontBody);
            return text;
        }

        static void PlaceFactoryCheck(Image image, float top)
        {
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.74f, 1);
            image.rectTransform.anchoredPosition = new Vector2(0, -top * Px);
        }

        public void ShowFactory(int targetId)
        {
            var mf = m_Game?.Map?.Factory(targetId);
            if (mf == null || m_Game.FactoryOf(targetId) == null || !CommandsAllowed(out _)) return;
            m_Game.ClearSelection();
            HideInfo();
            m_FactoryId = targetId;
            m_FactorySignature = ulong.MaxValue;
            RefreshFactoryDetail();
            m_FactoryDialog.Show();
            Opened(m_FactoryEntry);
        }

        public void HideFactory()
        {
            if (m_FactoryDialog != null) HideDialog(m_FactoryDialog, m_FactoryEntry);
            m_FactoryId = -1;
        }

        void RefreshFactoryDetail()
        {
            var f = m_Game?.FactoryOf(m_FactoryId);
            var mf = m_Game?.Map?.Factory(m_FactoryId);
            var p = m_Game?.State?.Profile;
            if (f == null || mf == null || p == null) { HideFactory(); return; }
            ulong signature = CardSignature(p) ^ ((ulong)(int)f.Status << 48) ^ ((ulong)(uint)m_FactoryId << 24) ^
                (f.Automated ? 0xA55AUL : 0UL) ^ (ulong)f.ReadyUnits ^ (CommandsAllowed(out _) ? 1UL << 63 : 0UL);
            if (signature == m_FactorySignature) { m_FactoryDialog.Relayout(); return; }
            m_FactorySignature = signature;
            string name = FactoryText.FactoryName(f.GoodId);
            var def = m_Game.Configs.Cards.Get(mf.RequiredCardId);
            m_FactoryDialog.Begin(Loc.F("factories.detail.title", name), null, Loc.T("factories.detail.help"));
            if (def != null)
            {
                if (m_FactoryCard == null || m_FactoryCard.Def.Id != def.Id)
                {
                    if (m_FactoryCard != null) { m_FactoryCard.gameObject.SetActive(false); Destroy(m_FactoryCard.gameObject); }
                    m_FactoryCard = CardView.Create(m_FactoryExtra, def, 1, rules: m_Game.Configs.Cards.Rules);
                    m_FactoryCard.Rect.Anchor(0, 1, 0.45f, 1, new Vector2(0, -142 * Px), new Vector2(-3 * Px, 0));
                }
                m_FactoryCard.Refresh(p);
                var q = CardService.Quote(p, def);
                Ui.SetText(m_FactoryRarity, OutpostCardText.RarityName(def.Rarity));
                Ui.SetText(m_FactoryLevel, Loc.F("factories.detail.level", mf.RequiredCardLevel, q.Level));
                PixelUi.SetIcon(m_FactoryOwnedCheck, q.Level > 0 ? "icon_check" : "icon_lock");
                PixelUi.SetIcon(m_FactoryLevelCheck, q.Level >= mf.RequiredCardLevel && q.Level > 0 ? "icon_check" : "icon_lock");
                Ui.SetText(m_FactoryEffect, Loc.F("factories.detail.effect", OutpostCardText.EffectValue(def, Math.Max(1, q.Level), m_Game.Configs.Cards.Rules),
                    OutpostCardText.EffectValue(def, Math.Min(def.MaxLevel, Math.Max(1, q.Level + 1)), m_Game.Configs.Cards.Rules)));
                bool level = q.Level > 0 && q.Level < q.MaxLevel;
                var upgrade = m_FactoryDialog.SetButton(0, Loc.T(q.Level < 1 ? "factories.detail.find" : q.Level >= q.MaxLevel ? "factories.detail.max" : "cards.detail.upgrade"), PixelButton.Green, UpgradeFactoryCard);
                upgrade.SetInteractable(level && q.CanUpgrade && CommandsAllowed(out _));
                m_FactoryCopyPrice.Root.gameObject.SetActive(q.Level < q.MaxLevel);
                m_FactoryElixirPrice.Root.gameObject.SetActive(q.Level < q.MaxLevel);
                int neededCopies = q.Level < 1 ? 1 : q.CopiesNeeded;
                m_FactoryCopyPrice.SetText(OutpostUiText.Money(q.CopiesOwned) + "/" + neededCopies);
                m_FactoryCopyPrice.Text.color = q.CopiesOwned >= neededCopies ? PixelUi.TextColor : PixelUi.TextBad;
                m_FactoryElixirPrice.SetText(OutpostUiText.Money(q.ElixirNeeded));
                m_FactoryElixirPrice.Text.color = p.Elixir >= q.ElixirNeeded ? PixelUi.TextColor : PixelUi.TextBad;
            }
            Ui.SetText(m_FactoryElixir, Loc.F("factories.detail.elixir", OutpostUiText.Money(p.Elixir)));
            Ui.SetText(m_FactoryState, Loc.T(f.Status == FactoryStatus.Locked ? "factories.reason.locked" :
                f.Status == FactoryStatus.PendingActivation ? "factories.detail.activate" : f.Status == FactoryStatus.Stopped ? "outpost.factory.stopped" :
                f.Automated ? "factories.detail.automated" : "factories.detail.manual"));
            bool pending = f.Status == FactoryStatus.PendingActivation;
            bool ready = f.Status == FactoryStatus.Ready && !f.Automated;
            m_FactoryDialog.SetButton(1, pending ? Loc.T("factories.activate") : ready ? FactoryText.DeliverLabel(f.ReadyUnits) : Loc.T("outpost.campaign.close"),
                pending ? PixelButton.Gold : ready ? PixelButton.Green : PixelButton.Dark, OnFactoryAction).SetInteractable(!pending || (CommandsAllowed(out _) && m_Game.ActivateBlockReason(m_FactoryId) == null));
            m_FactoryDialog.Layout();
        }

        void UpgradeFactoryCard()
        {
            if (m_FactoryCard == null || !CommandsAllowed(out _)) { OnFactoryUpgradeDenied(); return; }
            var result = m_Game.UpgradeCard(m_FactoryCard.Def.Id);
            if (!result.Ok) OnFactoryUpgradeDenied();
            else m_CardSfx?.Upgrade();
            m_FactorySignature = ulong.MaxValue;
            RefreshNow();
        }

        void OnFactoryUpgradeDenied()
        {
            if (!CommandsAllowed(out string reason)) Toast(OutpostUiText.Reason(reason));
            else if (m_FactoryCard != null) Toast(OutpostCardText.Reason(CardService.Quote(m_Game.State.Profile, m_FactoryCard.Def)));
        }

        void OnFactoryAction()
        {
            var f = m_Game?.FactoryOf(m_FactoryId);
            if (f == null) return;
            if (f.Status == FactoryStatus.PendingActivation)
            {
                var result = m_Game.ActivateFactory(m_FactoryId);
                if (!result.Ok) Toast(FactoryText.Reason(result.Reason));
            }
            else if (f.Status == FactoryStatus.Ready && !f.Automated)
            {
                var result = m_Game.DeliverFactory(m_FactoryId);
                if (!result.Ok) Toast(FactoryText.Reason(result.Reason));
            }
            else HideFactory();
            m_FactorySignature = ulong.MaxValue;
            if (FactoryDetailOpen) RefreshFactoryDetail();
        }

        void BuildFactoryChest()
        {
            m_FactoryChestRoot = Ui.Rect(m_Safe, "FactoryChest").Anchor(0, 0, 1, 1);
            var shade = m_FactoryChestRoot.gameObject.AddComponent<Image>();
            shade.color = new Color(PixelPalette.Navy0.r, PixelPalette.Navy0.g, PixelPalette.Navy0.b, 0.98f);
            m_FactoryChestEntry = new WindowEntry { Root = m_FactoryChestRoot, Back = FactoryChestBack };
            m_FactoryChestTitle = PixelUi.Text(m_FactoryChestRoot, "title", "", PixelUi.FontTitle, PixelUi.TextAccentLight, TextAlignmentOptions.Center, true, true);
            m_FactoryChestTitle.rectTransform.Anchor(0.04f, 0.89f, 0.96f, 0.96f);
            Ui.AutoSize(m_FactoryChestTitle, PixelUi.FontLarge, PixelUi.FontTitle);
            m_FactoryChestSubtitle = PixelUi.Text(m_FactoryChestRoot, "subtitle", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Center, false, true);
            m_FactoryChestSubtitle.rectTransform.Anchor(0.06f, 0.81f, 0.94f, 0.88f);
            m_FactoryChestAnimation = Ui.Rect(m_FactoryChestRoot, "opening");
            m_FactoryChestAnimation.anchorMin = m_FactoryChestAnimation.anchorMax = new Vector2(0.5f, 0.56f);
            m_FactoryChestAnimation.sizeDelta = new Vector2(140 * Px, 140 * Px);
            for (int i = 0; i < 8; i++) m_FactoryChestStars.Add(PixelUi.Icon(m_FactoryChestAnimation, "spark" + i, "icon_star", 2));
            m_FactoryChestBase = ChestHalf("base", false);
            m_FactoryChestLid = ChestHalf("lid", true);
            m_FactoryRewardViewport = Ui.Rect(m_FactoryChestRoot, "rewards").Anchor(0.05f, 0.25f, 0.95f, 0.78f);
            m_FactoryRewardViewport.gameObject.AddComponent<RectMask2D>();
            var input = m_FactoryRewardViewport.gameObject.AddComponent<Image>(); input.color = Color.clear;
            m_FactoryRewardContent = Ui.Rect(m_FactoryRewardViewport, "content");
            m_FactoryRewardContent.anchorMin = new Vector2(0, 1); m_FactoryRewardContent.anchorMax = Vector2.one;
            m_FactoryRewardContent.pivot = new Vector2(0.5f, 1);
            var scroll = m_FactoryRewardViewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = m_FactoryRewardContent; scroll.viewport = m_FactoryRewardViewport; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            m_FactoryChestElixir = PixelUi.IconText(m_FactoryChestRoot, "elixir", "icon_elixir", "", PixelUi.FontTitle, PixelUi.TextElixir, iconScale: 2);
            m_FactoryChestElixir.Root.anchorMin = m_FactoryChestElixir.Root.anchorMax = new Vector2(0.5f, 0.19f);
            m_FactoryChestContinue = PixelUi.Button(m_FactoryChestRoot, "continue", Loc.T("outpost.rescue.continue"), PixelButton.Green, CloseFactoryChest, PixelUi.FontLarge);
            m_FactoryChestContinue.Rect.Anchor(0.12f, 0.06f, 0.88f, 0.13f);
            m_FactoryChestRoot.gameObject.SetActive(false);
        }

        RectTransform ChestHalf(string name, bool lid)
        {
            var clip = Ui.Rect(m_FactoryChestAnimation, name);
            clip.anchorMin = clip.anchorMax = new Vector2(0.5f, 0.5f);
            clip.pivot = new Vector2(0.5f, lid ? 0 : 1);
            var size = PixelUi.IconSize("icon_chest_big", 4);
            clip.sizeDelta = new Vector2(size.x, Mathf.Round(size.y * (lid ? 0.43f : 0.57f)));
            clip.gameObject.AddComponent<Image>();
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var icon = PixelUi.Icon(clip, "sprite", "icon_chest_big", 4);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, lid ? 1 : 0);
            icon.rectTransform.pivot = new Vector2(0.5f, lid ? 1 : 0);
            icon.rectTransform.anchoredPosition = Vector2.zero;
            return clip;
        }

        void QueueFactoryChest(OutpostEvent e)
        {
            m_FactoryChestQueue.Enqueue(e);
            m_NextRefresh = 0;
        }

        void TickFactoryChest()
        {
            if (!FactoryChestOpen && m_FactoryChestQueue.Count == 0 && m_DeferredMineChests.Count > 0 &&
                CommandsAllowed(out _) && !OfflineOpen && !ChestRewardOpen && !IntroOpen && (!AnyWindowOpen || FactoryDetailOpen))
                OnCardEvent(m_DeferredMineChests.Dequeue());
            if (!FactoryChestOpen)
            {
                if (m_FactoryChestQueue.Count == 0 || !CommandsAllowed(out _) || InTransition || OfflineOpen || OfflineCalcOpen ||
                    ErrorOpen || ChestRewardOpen || IntroOpen || (AnyWindowOpen && !FactoryDetailOpen)) return;
                BeginFactoryChest(m_FactoryChestQueue.Dequeue());
            }
            float elapsed = Time.unscaledTime - m_FactoryChestStarted;
            if (!m_FactoryRewardsShown)
            {
                float open = Mathf.Clamp01((elapsed - 0.45f) / 0.55f);
                bool reduced = VisualSettings.ReduceEffects;
                m_FactoryChestAnimation.localRotation = Quaternion.Euler(0, 0, reduced || elapsed > 0.45f ? 0 : Mathf.Sin(elapsed * 55) * 7);
                m_FactoryChestLid.anchoredPosition = new Vector2(-12 * Px * open, 23 * Px * open);
                m_FactoryChestLid.localRotation = Quaternion.Euler(0, 0, reduced ? 0 : -25 * open);
                for (int i = 0; i < m_FactoryChestStars.Count; i++)
                {
                    var star = m_FactoryChestStars[i];
                    float angle = i * Mathf.PI / 4 + (reduced ? 0 : elapsed * 0.4f);
                    star.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ((18 + open * 40) * Px);
                    star.color = new Color(1, 1, 1, open);
                }
                if (elapsed >= (reduced ? 0.35f : 1.2f)) RevealFactoryChest();
            }
            if (m_FactoryRewardsShown) LayoutFactoryRewards();
        }

        void BeginFactoryChest(OutpostEvent e)
        {
            m_FactoryChestEvent = e;
            m_FactoryChestStarted = Time.unscaledTime;
            m_FactoryRewardsShown = false;
            m_FactoryChestRoot.SetAsLastSibling();
            m_FactoryChestRoot.gameObject.SetActive(true);
            m_FactoryChestAnimation.gameObject.SetActive(true);
            m_FactoryChestContinue.SetActive(false);
            m_FactoryRewardViewport.gameObject.SetActive(false);
            m_FactoryChestElixir.Root.gameObject.SetActive(false);
            string factory = FactoryText.FactoryName(m_Game.FactoryOf(e.TargetId)?.GoodId);
            Ui.SetText(m_FactoryChestTitle, Loc.F("factories.chest.opening", factory));
            Ui.SetText(m_FactoryChestSubtitle, Loc.T("factories.chest.hint"));
            m_FactoryChestLid.anchoredPosition = Vector2.zero;
            m_FactoryChestLid.localRotation = Quaternion.identity;
            foreach (var star in m_FactoryChestStars) star.color = Color.clear;
            UiSound.PlayOpen();
            Opened(m_FactoryChestEntry);
        }

        void RevealFactoryChest()
        {
            if (m_FactoryRewardsShown) return;
            m_FactoryRewardsShown = true;
            var e = m_FactoryChestEvent;
            m_FactoryRewardCount = 0;
            if (!string.IsNullOrEmpty(e.Detail)) foreach (string grant in e.Detail.Split(','))
            {
                int colon = grant.IndexOf(':');
                string id = colon > 0 ? grant.Substring(0, colon) : grant;
                long copies = colon > 0 && long.TryParse(grant.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 1;
                var def = m_Game.Configs.Cards.Get(id); if (def == null) continue;
                if (m_FactoryRewardTiles.Count <= m_FactoryRewardCount) m_FactoryRewardTiles.Add(ChestCardTile.Create(m_FactoryRewardContent, "card" + m_FactoryRewardCount));
                m_FactoryRewardTiles[m_FactoryRewardCount].Set(def, copies, m_KnownCards.Add(id));
                m_FactoryRewardCount++;
            }
            for (int i = 0; i < m_FactoryRewardTiles.Count; i++) m_FactoryRewardTiles[i].SetActive(i < m_FactoryRewardCount);
            m_FactoryRewardContent.anchoredPosition = Vector2.zero;
            m_FactoryChestAnimation.gameObject.SetActive(false);
            m_FactoryRewardViewport.gameObject.SetActive(true);
            m_FactoryChestElixir.Root.gameObject.SetActive(e.Value.Sign > 0);
            m_FactoryChestElixir.SetText("+" + OutpostUiText.Money(e.Value));
            m_FactoryChestContinue.SetActive(true);
            Ui.SetText(m_FactoryChestTitle, Loc.T("factories.chest.rewards"));
            Ui.SetText(m_FactoryChestSubtitle, Loc.F("outpost.store.chest_total", e.Count));
            LayoutFactoryRewards();
        }

        void LayoutFactoryRewards()
        {
            float width = m_FactoryRewardViewport.rect.width;
            int columns = m_FactoryRewardCount == 1 ? 1 : 3;
            float scale = columns == 1 ? 2 : 1;
            float tile = Mathf.Min(ChestCardTile.FullWidthPx + 4, Mathf.Floor((width - 4 * Px) / columns / scale / Px)) * Px;
            int rows = (m_FactoryRewardCount + columns - 1) / columns;
            float rowHeight = (ChestCardTile.HeightPx * scale + 3) * Px;
            float contentHeight = Mathf.Max(m_FactoryRewardViewport.rect.height, rows * rowHeight);
            float top = Mathf.Max(0, (contentHeight - rows * rowHeight) / 2);
            m_FactoryRewardContent.sizeDelta = new Vector2(0, contentHeight);
            for (int i = 0; i < m_FactoryRewardCount; i++)
            {
                int row = i / columns, inRow = Math.Min(columns, m_FactoryRewardCount - row * columns);
                float left = (width - inRow * tile * scale - (inRow - 1) * 2 * Px) / 2;
                m_FactoryRewardTiles[i].Root.localScale = Vector3.one * scale;
                m_FactoryRewardTiles[i].Place(left + i % columns * (tile * scale + 2 * Px), top + row * rowHeight, tile);
            }
        }

        void FactoryChestBack() { if (!m_FactoryRewardsShown) RevealFactoryChest(); else CloseFactoryChest(); }

        public void CloseFactoryChest()
        {
            if (!FactoryChestOpen) return;
            m_FactoryChestRoot.gameObject.SetActive(false);
            Closed(m_FactoryChestEntry);
            m_FactorySignature = ulong.MaxValue;
            m_NextRefresh = 0;
        }

        void ClearFactoryChests() { CloseFactoryChest(); m_FactoryChestQueue.Clear(); m_DeferredMineChests.Clear(); }
    }
}
