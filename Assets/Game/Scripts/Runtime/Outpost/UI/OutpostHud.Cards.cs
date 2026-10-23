using System;
using System.Collections.Generic;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BigInteger = System.Numerics.BigInteger;

namespace CatMiners.Runtime.Outpost
{
    public sealed partial class OutpostHud
    {
        public const string TargetCards = "ui.cards";
        const float CardDetailHeight = 244 * Px, CardDetailButtonHeight = 36 * Px, CardDetailPriceHeight = 32 * Px;
        RectTransform m_CardsRoot, m_CardsViewport, m_CardsContent;
        WindowEntry m_CardsEntry, m_CardDetailEntry;
        GridLayoutGroup m_CardsGrid;
        readonly List<CardView> m_CardViews = new List<CardView>(61);
        readonly List<CardView> m_CardOrder = new List<CardView>(61);
        readonly HashSet<string> m_KnownCards = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> m_NewCardNames = new List<string>();
        PixelIconText m_CardsElixir, m_CardPrice;
        TextMeshProUGUI m_CardsFound, m_CardCurrentValue, m_CardNextValue;
        /// <summary>Блок завода в окне карточки (ТЗ 2.2 §7.1): «Factory: Dairy (Milk)», эффект и строка текущей шахты.</summary>
        TextMeshProUGUI m_CardFactory;
        RectTransform m_CardComparison;
        Ui.ButtonRefs m_CardsButton, m_CardsSort, m_CardUpgrade;
        PixelWindow m_CardWin;
        CardView m_DetailCard;
        string m_CardShown;
        bool m_CardsValid, m_SortUpgradable;
        ulong m_CardsSignature;
        OutpostProfile m_KnownProfile;
        Sfx m_CardSfx;
        ulong m_BadgeSignature;
        bool m_BadgeValid;
        int m_CardsBadgeShown = -1, m_ChestCount;
        BigInteger m_ChestElixir;
        bool m_TutorialChest;
        float m_ChestDue;
        public bool CardsOpen => m_CardsRoot != null && m_CardsRoot.gameObject.activeSelf;
        public bool CardDetailOpen => CardsOpen && m_CardWin != null && m_CardWin.Visible;

        void BuildCards()
        {
            m_CardSfx = FindAnyObjectByType<Sfx>();
            m_CardsRoot = Ui.Rect(m_Safe, "Cards").Anchor(0, 0, 1, 1);
            var bg = PixelUi.Panel(m_CardsRoot, "background", PixelPanel.Dark);
            bg.rectTransform.Anchor(0, 0, 1, 1);
            m_CardsRoot.gameObject.AddComponent<PixelPop>();
            m_CardsEntry = new WindowEntry { Root = m_CardsRoot, Back = HideCards };
            var header = PixelUi.Panel(m_CardsRoot, "header", PixelPanel.Wood).rectTransform;
            header.Anchor(0, 1, 1, 1, new Vector2(EqPad, -40 * Px), new Vector2(-EqPad, -2 * Px));
            var title = PixelUi.Text(header, "title", Loc.T("cards.title"), PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            title.rectTransform.Anchor(0, 0.45f, 1, 1, new Vector2(26 * Px, 0), new Vector2(-26 * Px, -3 * Px));
            var subtitle = PixelUi.Text(header, "subtitle", Loc.T("cards.subtitle"), PixelUi.FontSmall, PixelUi.TextAccentLight, TextAlignmentOptions.Center, false, true);
            subtitle.rectTransform.Anchor(0, 0, 1, 0.45f, new Vector2(6 * Px, 3 * Px), new Vector2(-6 * Px, 0));
            var close = PixelUi.Button(header, "close", "", PixelButton.Red, HideCards);
            close.SetIcon("icon_close");
            PinSquare(close.Rect, 1, 22 * Px, 4 * Px);
            var counts = Ui.Rect(m_CardsRoot, "counts").Anchor(0, 1, 1, 1, new Vector2(EqPad, -65 * Px), new Vector2(-EqPad, -43 * Px));
            m_CardsElixir = PixelUi.IconText(counts, "elixir", "icon_elixir", "", PixelUi.FontBody, PixelUi.TextColor);
            m_CardsElixir.Root.anchorMin = m_CardsElixir.Root.anchorMax = new Vector2(0, 0.5f);
            m_CardsElixir.Root.pivot = new Vector2(0, 0.5f);
            m_CardsElixir.Root.anchoredPosition = new Vector2(3 * Px, 0);
            m_CardsFound = PixelUi.Text(counts, "found", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Right, false, true);
            m_CardsFound.rectTransform.Anchor(0.4f, 0, 1, 1, Vector2.zero, new Vector2(-3 * Px, 0));
            m_CardsSort = PixelUi.Button(m_CardsRoot, "sort", Loc.T("cards.sort.default"), PixelButton.Dark, ToggleCardSort, PixelUi.FontBody);
            m_CardsSort.Rect.Anchor(0, 1, 1, 1, new Vector2(EqPad, -88 * Px), new Vector2(-EqPad, -67 * Px));
            m_CardsSort.SetIcon("icon_sort");
            m_CardsSort.Icon.rectTransform.anchoredPosition = new Vector2(-80 * Px, 0);
            m_CardsViewport = Ui.Rect(m_CardsRoot, "viewport").Anchor(0, 0, 1, 1, new Vector2(EqPad, 2 * Px), new Vector2(-EqPad, -91 * Px));
            m_CardsViewport.gameObject.AddComponent<RectMask2D>();
            var input = m_CardsViewport.gameObject.AddComponent<Image>();
            input.color = Color.clear;
            m_CardsContent = Ui.Rect(m_CardsViewport, "content");
            m_CardsContent.anchorMin = new Vector2(0, 1);
            m_CardsContent.anchorMax = new Vector2(1, 1);
            m_CardsContent.pivot = new Vector2(0.5f, 1);
            m_CardsContent.offsetMin = m_CardsContent.offsetMax = Vector2.zero;
            m_CardsGrid = m_CardsContent.gameObject.AddComponent<GridLayoutGroup>();
            m_CardsGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            m_CardsGrid.constraintCount = 3;
            m_CardsGrid.spacing = new Vector2(3 * Px, 3 * Px);
            m_CardsGrid.childAlignment = TextAnchor.UpperCenter;
            m_CardsContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = m_CardsViewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = m_CardsContent;
            scroll.viewport = m_CardsViewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            m_CardWin = PixelUi.Window(m_CardsRoot, "CardDetail", "", HideCardDetail);
            m_CardWin.Box.Anchor(0.03f, 0.05f, 0.97f, 0.93f);
            m_CardWin.BodyText.gameObject.SetActive(false);
            m_CardDetailEntry = new WindowEntry { Root = m_CardWin.Root, Back = HideCardDetail };
            m_CardComparison = Ui.Rect(m_CardWin.Body, "comparison");
            m_CardCurrentValue = PixelUi.Text(m_CardComparison, "currentValue", "", PixelUi.FontTitle, PixelUi.TextColor,
                TextAlignmentOptions.Right, true, true);
            m_CardCurrentValue.rectTransform.Anchor(0, 0, 0.42f, 1);
            var arrow = PixelUi.Icon(m_CardComparison, "nextLevelArrow", "icon_upgrade");
            arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, -90);
            m_CardNextValue = PixelUi.Text(m_CardComparison, "nextValue", "", PixelUi.FontTitle, PixelPalette.Green6,
                TextAlignmentOptions.Left, true, true);
            m_CardNextValue.rectTransform.Anchor(0.58f, 0, 1, 1);
            // Под строкой сравнения — блок завода: FontBody с автоподбором до FontSmall и переносом по словам.
            m_CardFactory = PixelUi.Text(m_CardWin.Body, "factory", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Top, false, true);
            m_CardFactory.textWrappingMode = TextWrappingModes.Normal;
            Ui.AutoSize(m_CardFactory, PixelUi.FontSmall, PixelUi.FontBody);
            m_CardFactory.gameObject.SetActive(false);
            m_CardPrice = PixelUi.IconText(m_CardWin.Box, "price", "icon_elixir", "", PixelUi.FontTitle, PixelUi.TextColor, iconScale: 2);
            m_CardPrice.Root.anchorMin = m_CardPrice.Root.anchorMax = new Vector2(0.5f, 0);
            m_CardPrice.Root.anchoredPosition = new Vector2(0, 11 * Px + CardDetailButtonHeight + 4 * Px + CardDetailPriceHeight / 2);
            m_CardUpgrade = m_CardWin.AddButton("cardUpgrade", Loc.T("cards.detail.upgrade"), PixelButton.Green, UpgradeShownCard, PixelUi.FontTitle);
            m_CardUpgrade.Label.enableAutoSizing = false;
            m_CardWin.Buttons.offsetMax = new Vector2(m_CardWin.Buttons.offsetMax.x, m_CardWin.Buttons.offsetMin.y + CardDetailButtonHeight);
            AddDeniedTap(m_CardUpgrade, OnCardUpgradeDenied);
            m_CardsRoot.gameObject.SetActive(false);
        }

        void EnsureCardViews()
        {
            if (m_CardViews.Count != 0 || m_Game?.Configs?.Cards == null) return;
            foreach (var def in m_Game.Configs.Cards.Cards)
            {
                string id = def.Id;
                var view = CardView.Create(m_CardsContent, def, 1, () => ShowCard(id), m_Game.Configs.Cards.Rules);
                m_CardViews.Add(view);
                m_CardOrder.Add(view);
            }
        }

        ulong CardSignature(OutpostProfile p)
        {
            unchecked
            {
                ulong hash = (ulong)p.Elixir * 1099511628211UL ^ (ulong)p.HighestZone;
                var defs = m_Game.Configs.Cards.Cards;
                for (int i = 0; i < defs.Count; i++)
                {
                    var c = CardService.ProgressOf(p, defs[i].Id);
                    hash = (hash ^ (ulong)(c?.Level ?? 0)) * 1099511628211UL;
                    hash = (hash ^ (ulong)(c?.Copies ?? 0)) * 1099511628211UL;
                }
                return hash;
            }
        }

        public void ShowCards()
        {
            if (CardsOpen || m_Game?.State?.Profile == null) return;
            m_CardsRoot.SetAsLastSibling();
            m_CardsRoot.gameObject.SetActive(true);
            UiSound.PlayOpen();
            Opened(m_CardsEntry);
            EnsureCardViews();
            m_CardsValid = false;
            Canvas.ForceUpdateCanvases();
            RefreshCards();
        }
        public void HideCards()
        {
            HideCardDetail();
            if (CardsOpen) { m_CardsRoot.gameObject.SetActive(false); UiSound.PlayClose(); }
            Closed(m_CardsEntry);
        }
        void ToggleCardSort()
        {
            m_SortUpgradable = !m_SortUpgradable;
            m_CardsSort.SetLabel(Loc.T(m_SortUpgradable ? "cards.sort.upgradable" : "cards.sort.default"));
            m_CardsValid = false;
            RefreshCards();
        }
        int CompareCards(CardView a, CardView b)
        {
            var p = m_Game.State.Profile;
            if (m_SortUpgradable)
            {
                int up = CardService.Quote(p, b.Def).CanUpgrade.CompareTo(CardService.Quote(p, a.Def).CanUpgrade);
                if (up != 0) return up;
            }
            bool ao = p.CardLevel(a.Def.Id) >= 1, bo = p.CardLevel(b.Def.Id) >= 1;
            int owned = bo.CompareTo(ao);
            if (owned != 0) return owned;
            if (!ao) { int zone = a.Def.MineUnlock.CompareTo(b.Def.MineUnlock); if (zone != 0) return zone; }
            int weight = b.Def.SortingWeight.CompareTo(a.Def.SortingWeight);
            return weight != 0 ? weight : string.CompareOrdinal(a.Def.Id, b.Def.Id);
        }
        void RefreshCards()
        {
            var p = m_Game?.State?.Profile;
            if (p == null) return;
            float w = m_CardsViewport.rect.width;
            if (w < 100 * Px) w = RefWidth - 2 * EqPad;
            float cell = Mathf.Floor((w - 6 * Px) / 3 / Px) * Px;
            m_CardsGrid.cellSize = new Vector2(cell, CardView.CellHeight * Px);
            if (CardDetailOpen) LayoutCardDetail();
            ulong signature = CardSignature(p);
            if (m_CardsValid && signature == m_CardsSignature) return;
            m_CardsSignature = signature;
            m_CardsValid = true;
            int owned = 0;
            for (int i = 0; i < m_CardViews.Count; i++)
            {
                m_CardViews[i].Refresh(p);
                if (p.CardLevel(m_CardViews[i].Def.Id) >= 1) owned++;
            }
            m_CardsElixir.SetText(OutpostUiText.Money(p.Elixir));
            Ui.SetText(m_CardsFound, Loc.F("cards.found", owned, m_CardViews.Count));
            m_CardOrder.Sort(CompareCards);
            for (int i = 0; i < m_CardOrder.Count; i++) m_CardOrder[i].Rect.SetSiblingIndex(i);
            if (CardDetailOpen) RefreshCardDetail();
        }
        public void ShowCard(string id)
        {
            var def = m_Game?.Configs?.Cards?.Get(id);
            if (def == null) return;
            ShowCards();
            if (m_CardShown != id || m_DetailCard == null)
            {
                if (m_DetailCard != null) { m_DetailCard.gameObject.SetActive(false); Destroy(m_DetailCard.gameObject); }
                m_DetailCard = CardView.Create(m_CardWin.Body, def, 3, null, m_Game.Configs.Cards.Rules, detail: true);
            }
            m_CardShown = id;
            RefreshCardDetail();
            m_CardWin.Show();
            Opened(m_CardDetailEntry);
        }
        public void HideCardDetail()
        {
            m_CardWin?.Hide();
            Closed(m_CardDetailEntry);
        }
        void RefreshCardDetail()
        {
            var def = m_Game.Configs.Cards.Get(m_CardShown);
            var p = m_Game.State.Profile;
            var q = CardService.Quote(p, def);
            m_CardWin.SetTitle(OutpostCardText.Name(def));
            m_DetailCard.Refresh(p);
            bool canLevel = q.Level > 0 && q.Level < q.MaxLevel;
            Ui.SetActive(m_CardComparison, canLevel);
            if (canLevel)
            {
                Ui.SetText(m_CardCurrentValue, OutpostCardText.EffectValue(def, q.Level, m_Game.Configs.Cards.Rules));
                Ui.SetText(m_CardNextValue, OutpostCardText.EffectValue(def, q.Level + 1, m_Game.Configs.Cards.Rules));
            }
            m_CardPrice.Root.gameObject.SetActive(canLevel);
            m_CardPrice.SetText(OutpostUiText.Money(q.ElixirNeeded));
            m_CardPrice.Text.color = p.Elixir >= q.ElixirNeeded ? PixelUi.TextColor : PixelUi.TextBad;
            m_CardUpgrade.SetActive(canLevel);
            m_CardUpgrade.SetInteractable(q.CanUpgrade && CommandsAllowed(out _));
            string factory = CardFactoryText(def, q.Level);
            Ui.SetActive(m_CardFactory, factory != null);
            if (factory != null) Ui.SetText(m_CardFactory, factory);
            LayoutCardDetail();
        }

        /// <summary>
        /// Блок завода в окне карточки (ТЗ 2.2 §7.1), null — карточка не завода. Строки: «Factory: Dairy (Milk)», эффект на
        /// текущем уровне (у неполученной — уровня 1) и строка шахты по требованию токена s завода этого типа в текущей шахте
        /// (завода типа нет — строки нет): «auto delivery: on», «needs Lv 2 (you have Lv 1)», «(not collected)» или имя другой
        /// карточки типа, если требование указывает её.
        /// </summary>
        string CardFactoryText(CardDef def, int level)
        {
            string type = OutpostCardText.FactoryType(def);
            if (type == null) return null;
            string good = OutpostCardText.GoodOfType(type), factory = FactoryText.FactoryName(good);
            var sb = m_Sb.Clear();
            sb.Append(Loc.F("cards.detail.factory", factory, FactoryText.GoodName(good)));
            sb.Append('\n').Append(OutpostCardText.Effect(def, level, m_Game.Configs.Cards.Rules));
            var map = m_Game.Map;
            var profile = m_Game.State?.Profile;
            var factories = map != null ? map.Factories : null;
            if (factories != null && profile != null)
                for (int i = 0; i < factories.Length; i++)
                {
                    var mf = factories[i];
                    if (mf == null || mf.TypeId != type || mf.RequiredCardId == null) continue;
                    int have = profile.CardLevel(mf.RequiredCardId);
                    var state = m_Game.FactoryOf(mf.TargetId);
                    string line;
                    if ((state != null && state.Automated) || (have >= 1 && have >= mf.RequiredCardLevel))
                        line = Loc.F("cards.detail.mine_auto_on", factory);
                    else if (mf.RequiredCardId != def.Id)
                        line = Loc.F("cards.detail.mine_auto_other", factory, OutpostCardText.Name(m_Game.Configs.Cards.Get(mf.RequiredCardId)), mf.RequiredCardLevel);
                    else if (have < 1)
                        line = Loc.F("cards.detail.mine_auto_missing", factory, mf.RequiredCardLevel);
                    else
                        line = Loc.F("cards.detail.mine_auto", factory, mf.RequiredCardLevel, have);
                    sb.Append('\n').Append(line);
                    break;
                }
            return sb.ToString();
        }

        void LayoutCardDetail()
        {
            if (m_DetailCard == null) return;
            m_CardWin.UpdateBodyArea();
            bool upgrade = m_CardUpgrade.Image.gameObject.activeSelf;
            float bottom = upgrade ? 11 * Px + CardDetailButtonHeight + 8 * Px + CardDetailPriceHeight : 12 * Px;
            m_CardWin.Body.Anchor(0, 0, 1, 1, new Vector2(12 * Px, bottom), new Vector2(-12 * Px, -14 * Px));
            float height = m_CardWin.Body.rect.height;
            if (height < 100 * Px) height = 320 * Px;
            bool factory = m_CardFactory.gameObject.activeSelf;
            bool comparison = m_CardComparison.gameObject.activeSelf;
            // Блок завода (три строки и перенос строки шахты, ≈ 4 × 8 пикселей арта) помещается под строкой сравнения и на 9:16
            // (≈ 48 пикселей арта), поэтому портрет прежнего размера.
            float cardHeight = Mathf.Min(CardDetailHeight, Mathf.Floor(height * 0.74f / Px) * Px);
            m_DetailCard.Rect.Anchor(0, 1, 1, 1, new Vector2(Px, -cardHeight), new Vector2(-Px, 0));
            m_CardComparison.Anchor(0, 1, 1, 1, new Vector2(0, -cardHeight - 34 * Px), new Vector2(0, -cardHeight - 4 * Px));
            if (factory)
            {
                float top = cardHeight + (comparison ? 36 * Px : 4 * Px);
                m_CardFactory.rectTransform.Anchor(0, 0, 1, 1, new Vector2(2 * Px, 0f), new Vector2(-2 * Px, -top));
            }
            m_CardUpgrade.SetInteractable(CardService.Quote(m_Game.State.Profile, m_DetailCard.Def).CanUpgrade && CommandsAllowed(out _));
        }

        void OnCardUpgradeDenied()
        {
            if (!CommandsAllowed(out string blocked)) Toast(OutpostUiText.Reason(blocked));
            else Toast(OutpostCardText.Reason(CardService.Quote(m_Game.State.Profile, m_Game.Configs.Cards.Get(m_CardShown))));
        }

        public void UpgradeShownCard()
        {
            if (m_CardShown == null) return;
            if (!CommandsAllowed(out _)) { OnCardUpgradeDenied(); return; }
            var result = m_Game.UpgradeCard(m_CardShown);
            if (result.Ok)
            {
                m_CardWin.Box.GetComponent<PixelPop>()?.Play();
                m_CardSfx?.Upgrade();
            }
            else
            {
                string reason = OutpostCardText.Reason(CardService.Quote(m_Game.State.Profile, m_Game.Configs.Cards.Get(m_CardShown)));
                Toast(string.IsNullOrEmpty(reason) ? OutpostUiText.Reason(result.Reason) : reason);
            }
            m_CardsValid = false;
            RefreshNow();
        }
        void RefreshCardsBadge()
        {
            var p = m_Game?.State?.Profile;
            if (p == null || m_Game.Configs.Cards == null) return;
            // Раздел закрыт первой сессией (замок на кнопке) — без бейджа; открытие входит в подпись.
            bool open = FeatureOpen(ProgressionConfig.Cards);
            ulong signature = CardSignature(p) ^ (open ? 0x9E3779B97F4A7C15UL : 0UL);
            if (m_BadgeValid && ReferenceEquals(m_KnownProfile, p) && signature == m_BadgeSignature) return;
            m_BadgeValid = true;
            m_BadgeSignature = signature;
            if (!ReferenceEquals(m_KnownProfile, p))
            {
                m_KnownCards.Clear();
                foreach (var def in m_Game.Configs.Cards.Cards)
                    if (p.CardLevel(def.Id) > 0) m_KnownCards.Add(def.Id);
            }
            m_KnownProfile = p;
            int badge = open && CardService.AnyUpgradable(p, m_Game.Configs.Cards) ? 1 : 0;
            if (badge != m_CardsBadgeShown) { m_CardsBadgeShown = badge; m_CardsButton.SetBadge(badge); }
        }
        void OnCardEvent(OutpostEvent e)
        {
            if (e.Type == OutpostEventType.ElixirFound && m_Game.TryGetTargetWorldPosition(e.TargetId, out var world))
                FloatingText.Spawn(world, "+" + OutpostUiText.Money(e.Value), PixelUi.TextAccentLight);
            if (e.Type == OutpostEventType.CardUpgraded)
            {
                Toast(Loc.F("cards.toast.upgraded", OutpostCardText.Name(m_Game.Configs.Cards.Get(e.ItemId)), e.Count));
                m_NextRefresh = 0;
            }
            // Сундук магазина показывается своим окном (OutpostHud.Store), без тоста.
            if (e.Type != OutpostEventType.ChestOpened || e.TargetId == OutpostIds.StoreSource) return;
            if (m_Game.Map?.Factory(e.TargetId) != null) { QueueFactoryChest(e); return; }
            if (FactoryChestOpen || m_FactoryChestQueue.Count > 0) { m_DeferredMineChests.Enqueue(e); return; }
            // Сундук шахты с новой карточкой — тем же окном (их в шахте до 20–60, остальные — тостом); пока окно сундуков шахты
            // открыто, следующие добавляются в него. Окно сундука магазина не подменяется; учебная карточка — не сундук.
            if (e.ItemId != "tutorial" && (MineChestRewardOpen || (e.Extra > 0 && !ChestRewardOpen)))
            {
                ShowChestReward(e, false);
                return;
            }
            if (m_ChestCount == 0) m_ChestDue = Time.unscaledTime + 0.1f;
            m_ChestCount += e.Count;
            m_ChestElixir += e.Value;
            m_TutorialChest |= e.ItemId == "tutorial";
            if (e.Extra > 0 && !string.IsNullOrEmpty(e.Detail))
                foreach (var grant in e.Detail.Split(','))
                {
                    string id = grant.Split(':')[0];
                    // Snapshot of owned IDs before this presentation burst identifies newly acquired cards.
                    if (m_KnownCards.Add(id)) m_NewCardNames.Add(OutpostCardText.Name(m_Game.Configs.Cards.Get(id)));
                }
            m_NextRefresh = 0;
        }
        void FlushCardToasts()
        {
            if (m_ChestCount == 0 || Time.unscaledTime < m_ChestDue) return;
            string text = Loc.F(m_ChestElixir.IsZero ? "cards.toast.chest_cards" : "cards.toast.chest", m_ChestCount, OutpostUiText.Money(m_ChestElixir));
            if (m_NewCardNames.Count > 0) text += "\n" + Loc.F(m_TutorialChest ? "cards.toast.tutorial" : "cards.toast.new", string.Join(", ", m_NewCardNames));
            Toast(text, 4f);
            m_ChestCount = 0;
            m_ChestElixir = BigInteger.Zero;
            m_TutorialChest = false;
            m_NewCardNames.Clear();
        }
    }
}
