using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BigInteger = System.Numerics.BigInteger;
using OfflineSummary = CatMiners.Core.Outpost.OfflineSummary;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// «Store» (ТЗ 10.5): экран во весь safe area, как «Cards». Шапка «Store»; под ней, когда Mia спасена (первая сессия), — Mia за
    /// прилавком: портрет в рамке и одноразовая реплика на две секунды; строка валют — эликсир (soft) и гемы (hard), «+» прокручивает к их разделам; ниже —
    /// разделы store_catalog.json сверху вниз: баннер и плитки <see cref="StoreTileView"/> по три в ряд. Бесплатное (реклама готова, расходник в наличии) срабатывает сразу, покупка за гемы и деньги — после
    /// подтверждения. Сундук магазина (ChestOpened с источником <see cref="OutpostIds.StoreSource"/>) и сундук шахты с новой
    /// карточкой показываются окном выпавших карточек, тайм-скип — окном расчёта и итогом (<see cref="IOutpostGame.PendingSkip"/>).
    /// </summary>
    public sealed partial class OutpostHud
    {
        public const string TargetStore = "ui.store";
        /// <summary>Карточек картинками в окне сундука (два ряда по три); остальные — одной строкой «+N more».</summary>
        const int ChestRewardCards = 6;
        const float ChestTileGap = 3 * Px;
        /// <summary>Mia за прилавком: высота строки (рамка портрета 52 пикселя арта) и отступ до строки валют.</summary>
        const float KeeperHeight = 52 * Px, KeeperGap = 2 * Px;
        /// <summary>Mia приветствует один раз за профиль, две реальные секунды; портрет остаётся и моргает раз в 3–5 с.</summary>
        const float KeeperWelcomeTime = 2f, KeeperTalkStep = 0.15f, KeeperTalkTime = 1.2f, KeeperBlinkTime = 0.12f;

        RectTransform m_StoreRoot, m_StoreViewport, m_StoreContent, m_StoreCounts, m_StoreKeeper;
        Image m_KeeperPortrait, m_KeeperBubble;
        readonly Sprite[] m_KeeperFrames = new Sprite[3];
        bool m_KeeperShown;
        int m_KeeperFrame = -1;
        float m_KeeperTalkUntil, m_KeeperBlinkAt, m_KeeperWelcomeUntil = -1f;
        ScrollRect m_StoreScroll;
        WindowEntry m_StoreEntry;
        Ui.ButtonRefs m_StoreButton;
        PixelIconText m_StoreElixir, m_StoreGems;
        Ui.ButtonRefs m_StoreElixirPlus, m_StoreGemsPlus;
        readonly List<StoreTileView> m_StoreTiles = new List<StoreTileView>(16);
        readonly List<GridLayoutGroup> m_StoreGrids = new List<GridLayoutGroup>(4);
        readonly Dictionary<string, RectTransform> m_StoreSections = new Dictionary<string, RectTransform>(StringComparer.Ordinal);
        float m_StoreGridWidth = -1f;
        OutpostDialog m_ChestReward, m_DropRates, m_SkipResult;
        WindowEntry m_ChestRewardEntry, m_DropRatesEntry, m_SkipResultEntry;
        OfflineSummary m_SkipResultShown;
        int m_StoreBadgeShown = -1;
        readonly List<ChestCardTile> m_ChestTiles = new List<ChestCardTile>(ChestRewardCards);
        int m_ChestTilesShown;
        ChestBatch m_ChestBatch;

        /// <summary>Что показывает окно сундука: один сундук магазина или сундуки шахты, открытые, пока окно на экране.</summary>
        sealed class ChestBatch
        {
            public bool Store;
            public string Title, Icon;
            public int Chests;
            public long Cards;
            public BigInteger Elixir;
            public readonly List<ChestGrant> Grants = new List<ChestGrant>();
        }

        struct ChestGrant
        {
            public CardDef Def;
            public long Copies;
            public bool New;
        }

        public bool StoreOpen => m_StoreRoot != null && m_StoreRoot.gameObject.activeSelf;
        /// <summary>Mia за прилавком сверху открытого магазина (тесты, съёмка).</summary>
        public bool StoreKeeperVisible => StoreOpen && m_KeeperShown;
        public bool StoreWelcomeVisible => StoreKeeperVisible && m_KeeperBubble != null && m_KeeperBubble.gameObject.activeSelf;
        public bool ChestRewardOpen => m_ChestReward != null && m_ChestReward.Visible;
        /// <summary>Окно сундуков шахты (не магазина) открыто: следующие сундуки шахты добавляются в него.</summary>
        bool MineChestRewardOpen => ChestRewardOpen && m_ChestBatch != null && !m_ChestBatch.Store;
        /// <summary>Сундуков в открытом окне награды (0 — окна нет).</summary>
        public int ChestRewardChests => ChestRewardOpen && m_ChestBatch != null ? m_ChestBatch.Chests : 0;
        /// <summary>Карточек картинками в открытом окне награды; <see cref="ChestRewardTile"/> — по порядку показа.</summary>
        public int ChestRewardTileCount => ChestRewardOpen ? m_ChestTilesShown : 0;
        public ChestCardTile ChestRewardTile(int index) => index >= 0 && index < ChestRewardTileCount ? m_ChestTiles[index] : null;
        public bool SkipResultOpen => m_SkipResult != null && m_SkipResult.Visible;

        /// <summary>
        /// Крючок playable-сборки (урезанный каталог без пакетов, гемов на старте нет): скрыть «+» эликсира и гемов, если раздела
        /// пакетов этой валюты в каталоге нет (кнопке некуда прокрутить), и плитки, цену которых нечем заплатить (не хватает
        /// гемов, например тайм-скип, когда стартовые закончились); раздел без видимых плиток скрыт целиком. Поле экземпляра,
        /// по умолчанию false — магазин основной игры показывает всё; основная игра его не задаёт.
        /// </summary>
        public bool StoreHideUnpayable;

        void BuildStore()
        {
            m_StoreRoot = Ui.Rect(m_Safe, "Store").Anchor(0, 0, 1, 1);
            var bg = PixelUi.Panel(m_StoreRoot, "background", PixelPanel.Dark);
            bg.rectTransform.Anchor(0, 0, 1, 1);
            m_StoreRoot.gameObject.AddComponent<PixelPop>();
            m_StoreEntry = new WindowEntry { Root = m_StoreRoot, Back = HideStore };
            var header = PixelUi.Panel(m_StoreRoot, "header", PixelPanel.Wood).rectTransform;
            header.Anchor(0, 1, 1, 1, new Vector2(EqPad, -30 * Px), new Vector2(-EqPad, -2 * Px));
            var title = PixelUi.Text(header, "title", Loc.T("outpost.store.title"), PixelUi.FontTitle, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            title.rectTransform.Anchor(0, 0, 1, 1, new Vector2(26 * Px, 2 * Px), new Vector2(-26 * Px, -2 * Px));
            var close = PixelUi.Button(header, "close", "", PixelButton.Red, HideStore);
            close.SetIcon("icon_close");
            PinSquare(close.Rect, 1, 22 * Px, 4 * Px);

            BuildStoreKeeper();

            // Валюты: эликсир (soft) слева, гемы (hard) справа; «+» прокручивает к разделу пакетов.
            m_StoreCounts = Ui.Rect(m_StoreRoot, "currencies");
            m_StoreElixir = CurrencyCounter(m_StoreCounts, "elixir", "icon_elixir", 0f, 0.5f, PixelButton.Green, () => ScrollStoreTo("elixir"), out m_StoreElixirPlus);
            m_StoreGems = CurrencyCounter(m_StoreCounts, "gems", "icon_gem", 0.5f, 1f, PixelButton.Gold, () => ScrollStoreTo("gems"), out m_StoreGemsPlus);

            m_StoreViewport = Ui.Rect(m_StoreRoot, "viewport");
            LayoutStoreTop(false);
            m_StoreViewport.gameObject.AddComponent<RectMask2D>();
            var input = m_StoreViewport.gameObject.AddComponent<Image>();
            input.color = Color.clear;
            m_StoreContent = Ui.Rect(m_StoreViewport, "content");
            m_StoreContent.anchorMin = new Vector2(0, 1);
            m_StoreContent.anchorMax = new Vector2(1, 1);
            m_StoreContent.pivot = new Vector2(0.5f, 1);
            m_StoreContent.offsetMin = m_StoreContent.offsetMax = Vector2.zero;
            var column = m_StoreContent.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = 4 * Px;
            column.padding = new RectOffset(0, 0, 0, (int)(6 * Px));
            column.childAlignment = TextAnchor.UpperCenter;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            m_StoreContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            m_StoreScroll = m_StoreViewport.gameObject.AddComponent<ScrollRect>();
            m_StoreScroll.content = m_StoreContent;
            m_StoreScroll.viewport = m_StoreViewport;
            m_StoreScroll.horizontal = false;
            m_StoreScroll.movementType = ScrollRect.MovementType.Clamped;
            m_StoreScroll.scrollSensitivity = 30;

            m_ChestReward = OutpostDialog.Create(m_Safe, "ChestReward", HideChestReward);
            // Карточки — над строками «+N more» и эликсира; окно шире обычного, чтобы три портрета 64×48 встали в ряд.
            m_ChestReward.ExtraFirst = true;
            m_ChestReward.SideMargin = 0.03f;
            m_ChestReward.AfterLayout = LayoutChestTiles;
            m_ChestRewardEntry = new WindowEntry { Root = m_ChestReward.Root, Back = HideChestReward };
            m_DropRates = OutpostDialog.Create(m_Safe, "DropRates", HideDropRates);
            m_DropRatesEntry = new WindowEntry { Root = m_DropRates.Root, Back = HideDropRates };
            m_SkipResult = OutpostDialog.Create(m_Safe, "TimeSkip");
            m_SkipResult.BodyFontSize = PixelUi.FontLarge;
            m_SkipResultEntry = new WindowEntry { Root = m_SkipResult.Root, Back = DismissSkipWindow };
            m_StoreRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Mia за прилавком (первая сессия, Docs/Rework/FIRST_SESSION.md): под шапкой слева портрет в рамке ui_portrait_frame (кадры
        /// mia_portrait_0 — спокойно, _1 — говорит, _2 — моргает), справа светлый пузырь с её репликой. Скрыта, пока Mia не спасена
        /// (и без правил первой сессии — playable); нет спрайтов — не строится.
        /// </summary>
        void BuildStoreKeeper()
        {
            string character = ProgressionService.RescueCharacter(m_Game?.Configs) ?? "mia";
            if (!PixelArt.Has("ui_portrait_frame") || !PixelArt.Has(character + "_portrait_0")) return;
            m_StoreKeeper = Ui.Rect(m_StoreRoot, "keeper").Anchor(0, 1, 1, 1, new Vector2(EqPad, -(32 * Px + KeeperHeight)), new Vector2(-EqPad, -32 * Px));
            var frame = PixelUi.Icon(m_StoreKeeper, "frame", "ui_portrait_frame");
            var frt = frame.rectTransform;
            frt.anchorMin = frt.anchorMax = new Vector2(0f, 0.5f);
            frt.anchoredPosition = new Vector2(frt.sizeDelta.x * frt.pivot.x, (frt.pivot.y - 0.5f) * frt.sizeDelta.y);
            m_KeeperPortrait = PixelUi.Icon(frt, "portrait", character + "_portrait_0");
            m_KeeperPortrait.rectTransform.anchorMin = m_KeeperPortrait.rectTransform.anchorMax = frt.pivot;
            m_KeeperPortrait.rectTransform.anchoredPosition = Vector2.zero;
            for (int i = 0; i < m_KeeperFrames.Length; i++)
            {
                string name = character + "_portrait_" + i;
                m_KeeperFrames[i] = PixelArt.Ui(PixelArt.Has(name) ? name : character + "_portrait_0");
            }
            // Пузырь правее рамки, низ — выше середины портрета; хвостик у левого нижнего угла клонится к Mia (как у TutorialHint).
            var bubble = PixelUi.Panel(m_StoreKeeper, "bubble", PixelPanel.Bubble);
            m_KeeperBubble = bubble;
            bubble.gameObject.SetActive(false);
            bubble.raycastTarget = false;
            float left = frt.sizeDelta.x + 4 * Px;
            bubble.rectTransform.Anchor(0, 0, 1, 1, new Vector2(left, 16 * Px), new Vector2(0f, -2 * Px));
            var line = PixelUi.Text(bubble.transform, "text", Loc.T("outpost.store.keeper"), PixelUi.FontBody, PixelUi.TextDark, TextAlignmentOptions.Center);
            line.rectTransform.Anchor(0, 0, 1, 1, new Vector2(6 * Px, 4 * Px), new Vector2(-6 * Px, -4 * Px));
            Ui.AutoSize(line, PixelUi.FontSmall, PixelUi.FontBody);
            if (PixelArt.Has("ui_bubble_tail"))
            {
                var tail = PixelUi.Icon(bubble.transform, "tail", "ui_bubble_tail");
                var trt = tail.rectTransform;
                trt.anchorMin = trt.anchorMax = Vector2.zero;
                trt.anchoredPosition = new Vector2(10 * Px, -4 * Px);
                trt.localScale = new Vector3(-1f, 1f, 1f);
            }
            m_StoreKeeper.gameObject.SetActive(false);
        }

        /// <summary>Верх магазина: с Mia за прилавком строка валют и список опускаются на её строку.</summary>
        void LayoutStoreTop(bool keeper)
        {
            m_KeeperShown = keeper && m_StoreKeeper != null;
            if (m_StoreKeeper != null) Ui.SetActive(m_StoreKeeper, m_KeeperShown);
            float shift = m_KeeperShown ? KeeperHeight + KeeperGap : 0f;
            m_StoreCounts.Anchor(0, 1, 1, 1, new Vector2(EqPad, -54 * Px - shift), new Vector2(-EqPad, -33 * Px - shift));
            m_StoreViewport.Anchor(0, 0, 1, 1, new Vector2(EqPad, 2 * Px), new Vector2(-EqPad, -57 * Px - shift));
            m_StoreGridWidth = -1f;
        }

        /// <summary>Кадр Mia за прилавком (только при открытом магазине): говорит после открытия, иначе моргает раз в 3–5 с.</summary>
        void TickStoreKeeper()
        {
            if (!m_KeeperShown || m_KeeperPortrait == null) return;
            float now = Time.unscaledTime;
            if (StoreWelcomeVisible && now >= m_KeeperWelcomeUntil) m_KeeperBubble.gameObject.SetActive(false);
            int frame;
            if (now < m_KeeperTalkUntil) frame = Mathf.FloorToInt((m_KeeperTalkUntil - now) / KeeperTalkStep) % 2 == 0 ? 1 : 0;
            else if (now >= m_KeeperBlinkAt)
            {
                frame = 2;
                if (now >= m_KeeperBlinkAt + KeeperBlinkTime) m_KeeperBlinkAt = now + UnityEngine.Random.Range(3f, 5f);
            }
            else frame = 0;
            if (frame == m_KeeperFrame) return;
            m_KeeperFrame = frame;
            m_KeeperPortrait.sprite = m_KeeperFrames[frame];
        }

        /// <summary>Счётчик валюты: иконка и число слева, «+» справа (кнопка прокрутки к разделу пакетов).</summary>
        PixelIconText CurrencyCounter(RectTransform row, string name, string icon, float from, float to, PixelButton plusStyle, Action onPlus,
            out Ui.ButtonRefs plusButton)
        {
            var cell = Ui.Rect(row, name).Anchor(from, 0, to, 1, new Vector2(from > 0 ? 3 * Px : 0f, 0f), new Vector2(to < 1 ? -3 * Px : 0f, 0f));
            var panel = PixelUi.Panel(cell, "panel", PixelPanel.Inset);
            panel.raycastTarget = false;
            panel.rectTransform.Anchor(0, 0, 1, 1, Vector2.zero, new Vector2(-22 * Px, 0f));
            var counter = PixelUi.IconText(panel.transform, "value", icon, "", PixelUi.FontLarge, PixelUi.TextColor);
            counter.Root.anchorMin = counter.Root.anchorMax = new Vector2(0f, 0.5f);
            counter.Root.pivot = new Vector2(0f, 0.5f);
            counter.Root.anchoredPosition = new Vector2(4 * Px, 0f);
            var plus = PixelUi.Button(cell, "plus", "", plusStyle, () => onPlus());
            plus.SetIcon("icon_plus");
            PinSquare(plus.Rect, 1, 20 * Px);
            plusButton = plus;
            return counter;
        }

        TextMeshProUGUI m_PurchaseStatus;
        void EnsureStoreTiles()
        {
            var store = m_Game?.Configs?.Store;
            if (store == null || m_StoreTiles.Count > 0) return;
            if (OutpostController.AndroidRuntime)
            {
                var recover = PixelUi.Button(m_StoreContent, "RecoverPurchases", "Enable / recover purchases", PixelButton.Dark, () => {
                    var payments = CatMiners.Runtime.Purchasing.GooglePaymentsRuntime.Instance;
                    if (payments == null) return;
                    if (!payments.Ready) payments.Enable();
                    payments.Recover();
                });
                recover.Rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 24 * Px;
                m_PurchaseStatus = PixelUi.Text(m_StoreContent, "PurchaseStatus", "Store unavailable", PixelUi.FontSmall, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
                m_PurchaseStatus.gameObject.AddComponent<LayoutElement>().preferredHeight = 28 * Px;
            }
            foreach (string section in store.Sections)
            {
                var items = store.InSection(section).Where(def => def.Currency != StoreCurrency.Ad || OutpostController.RewardedOffersSupported).ToList();
                if (items.Count == 0) continue;
                var banner = PixelUi.Panel(m_StoreContent, "section_" + section, PixelPanel.Header).rectTransform;
                banner.gameObject.AddComponent<LayoutElement>().preferredHeight = 20 * Px;
                var text = PixelUi.Text(banner, "title", Loc.T("outpost.store.section." + section), PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
                text.rectTransform.Anchor(0, 0, 1, 1, new Vector2(8 * Px, 2 * Px), new Vector2(-8 * Px, -Px));
                m_StoreSections[section] = banner;
                var grid = Ui.Rect(m_StoreContent, "grid_" + section);
                var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
                layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                layout.constraintCount = 3;
                layout.spacing = new Vector2(3 * Px, 4 * Px);
                layout.childAlignment = TextAnchor.UpperCenter;
                m_StoreGrids.Add(layout);
                foreach (var def in items) m_StoreTiles.Add(StoreTileView.Create(grid, def, OnStoreTile, ShowDropRates));
            }
            m_StoreGridWidth = -1f;
        }

        /// <summary>Открыть магазин (кнопка Store нижней панели); доступен в любой фазе, как «Карты».</summary>
        public void ShowStore()
        {
            if (m_StoreRoot == null || StoreOpen || m_Game?.Configs?.Store == null) return;
            m_StoreRoot.SetAsLastSibling();
            m_StoreRoot.gameObject.SetActive(true);
            UiSound.PlayOpen();
            Opened(m_StoreEntry);
            // Портрет остаётся в магазине; реплика — только при первом открытии после спасения.
            LayoutStoreTop(ProgressionService.Rescued(m_Game.State?.Profile, m_Game.Configs));
            bool welcome = m_KeeperShown && m_KeeperBubble != null &&
                m_Game.State?.Profile?.Progression?.WasShown(ProgressionService.StoreWelcomeShown) == false;
            if (m_KeeperBubble != null) Ui.SetActive(m_KeeperBubble, welcome);
            m_KeeperWelcomeUntil = welcome ? Time.unscaledTime + KeeperWelcomeTime : -1f;
            m_KeeperTalkUntil = welcome ? Time.unscaledTime + KeeperTalkTime : -1f;
            m_KeeperBlinkAt = Time.unscaledTime + (welcome ? KeeperTalkTime : 0f) + UnityEngine.Random.Range(3f, 5f);
            m_KeeperFrame = -1;
            if (welcome) m_Game.MarkProgress(ProgressionService.StoreWelcomeShown);
            EnsureStoreTiles();
            if (OutpostController.AndroidRuntime && CatMiners.Runtime.Integrations.GameSdkRuntime.Instance?.Privacy?.Protected == false) CatMiners.Runtime.Purchasing.GooglePaymentsRuntime.Instance?.OpenStore();
            Canvas.ForceUpdateCanvases();
            RefreshStore();
            m_StoreContent.anchoredPosition = Vector2.zero;
        }

        public void HideStore()
        {
            if (m_KeeperBubble != null) m_KeeperBubble.gameObject.SetActive(false);
            m_KeeperWelcomeUntil = -1f;
            HideChestReward();
            HideDropRates();
            if (StoreOpen)
            {
                m_StoreRoot.gameObject.SetActive(false);
                UiSound.PlayClose();
            }
            Closed(m_StoreEntry);
        }

        void RefreshStore()
        {
            var p = m_Game?.State?.Profile;
            var store = m_Game?.Configs?.Store;
            if (p == null || store == null) return;
            float w = m_StoreViewport.rect.width;
            if (w < 100 * Px) w = RefWidth - 2 * EqPad;
            if (!Mathf.Approximately(w, m_StoreGridWidth))
            {
                m_StoreGridWidth = w;
                float cell = Mathf.Floor((w - 6 * Px) / 3 / Px) * Px;
                foreach (var g in m_StoreGrids) g.cellSize = new Vector2(cell, StoreTileView.CellHeight * Px);
            }
            if (m_PurchaseStatus != null) Ui.SetText(m_PurchaseStatus, CatMiners.Runtime.Purchasing.GooglePaymentsRuntime.Instance?.Status ?? "Store unavailable");
            m_StoreElixir.SetText(StoreTileView.FormatAmount(p.Elixir));
            m_StoreGems.SetText(StoreTileView.FormatAmount(p.Gems));
            bool allowed = CommandsAllowed(out string blocked);
            long now = m_Game.UtcNowMs;
            bool layout = false;
            foreach (var tile in m_StoreTiles)
            {
                var q = StoreService.Quote(p, store, tile.Def, now);
                tile.Refresh(q, allowed ? null : blocked, RewardedAdsCanPrepare);
                if (StoreHideUnpayable) layout |= SetStoreItemShown(tile.Root, q.BlockReason != "gems");
            }
            if (!StoreHideUnpayable) return;
            // Раздел — баннер и сетка — виден, пока в нём есть видимая плитка.
            foreach (var pair in m_StoreSections)
            {
                var grid = pair.Value.parent.Find("grid_" + pair.Key);
                bool any = false;
                if (grid != null)
                    for (int i = 0; i < grid.childCount && !any; i++) any = grid.GetChild(i).gameObject.activeSelf;
                layout |= SetStoreItemShown(pair.Value, any);
                if (grid != null) layout |= SetStoreItemShown((RectTransform)grid, any);
            }
            if (layout) LayoutRebuilder.MarkLayoutForRebuild(m_StoreContent);
            // «+» валюты — только если есть видимый раздел её пакетов (иначе кнопке некуда прокрутить).
            Ui.SetActive(m_StoreElixirPlus.Rect, StoreSectionShown("elixir"));
            Ui.SetActive(m_StoreGemsPlus.Rect, StoreSectionShown("gems"));
        }

        bool StoreSectionShown(string section) => m_StoreSections.TryGetValue(section, out var banner) && banner.gameObject.activeSelf;

        /// <summary>Показать или скрыть плитку/раздел магазина; true — видимость изменилась (нужна перекладка столбца).</summary>
        static bool SetStoreItemShown(RectTransform rt, bool on)
        {
            if (rt.gameObject.activeSelf == on) return false;
            rt.gameObject.SetActive(on);
            return true;
        }

        /// <summary>Прокрутить магазин так, чтобы баннер раздела оказался сверху.</summary>
        void ScrollStoreTo(string section)
        {
            if (!m_StoreSections.TryGetValue(section, out var banner)) return;
            Canvas.ForceUpdateCanvases();
            float top = -(banner.anchoredPosition.y + (1f - banner.pivot.y) * banner.rect.height);
            float max = Mathf.Max(0f, m_StoreContent.rect.height - m_StoreViewport.rect.height);
            m_StoreScroll.StopMovement();
            m_StoreContent.anchoredPosition = new Vector2(m_StoreContent.anchoredPosition.x, Mathf.Clamp(top, 0f, max));
        }

        void OnStoreTile(StoreItemDef def)
        {
            if (m_Game?.State == null) return;
            if (!CommandsAllowed(out string blocked))
            {
                Denied(blocked);
                return;
            }
            if (def.Currency == StoreCurrency.Ad && TryPrepareRewardedAd(() => OnStoreTile(def), () => StoreOpen)) return;
            var q = OutpostController.PlatformStoreQuote(StoreService.Quote(m_Game.State.Profile, m_Game.Configs.Store, def, m_Game.UtcNowMs));
            if (def.Kind == StoreItemKind.TimeSkip && m_Game.Sim != null && m_Game.Sim.Run.Phase == RunPhase.ReadyToContinue)
            {
                Denied("phase");
                return;
            }
            if (!q.CanBuy)
            {
                if (q.BlockReason == "cooldown") Toast(Loc.F("outpost.store.free_in", CatMiners.Core.NumberFormat.FormatDuration(q.CooldownLeftMs / 1000.0)));
                else Denied(q.BlockReason);
                if (q.BlockReason == "gems") ScrollStoreTo("gems");
                return;
            }
            if (q.Free)
            {
                DoStoreAction(def);
                return;
            }
            string name = OutpostUiText.StoreItemName(def);
            string body = def.Currency == StoreCurrency.Real ? (OutpostController.AndroidRuntime ? "Buy for " + CatMiners.Runtime.Purchasing.GooglePaymentsRuntime.Instance?.Price(def.Id) + "?" : Loc.F("outpost.store.confirm_real", def.PriceUsd))
                : Loc.F("outpost.store.confirm_gems", StoreTileView.FormatAmount(def.Price));
            Modal(Loc.F("outpost.store.confirm_title", name), body, StoreTileView.ArtOf(def),
                new ModalButton { Label = Loc.T("outpost.store.cancel"), Style = PixelButton.Dark, Cancel = true },
                new ModalButton { Label = Loc.T("outpost.store.buy"), Style = PixelButton.Green, Action = () => DoStoreAction(def) });
        }

        void DoStoreAction(StoreItemDef def)
        {
            var result = def.Kind == StoreItemKind.TimeSkip ? m_Game.UseTimeSkip(def.Id) : m_Game.StoreBuy(def.Id);
            if (!result.Ok) Denied(result.Reason);
            m_NextRefresh = 0f;
            RefreshNow();
        }

        /// <summary>Красная точка на кнопке Store: рекламная плитка готова (бесплатный сундук ждёт); закрытый магазин — без точки.</summary>
        void RefreshStoreBadge(bool allowed)
        {
            var p = m_Game?.State?.Profile;
            var store = m_Game?.Configs?.Store;
            int n = 0;
            if (allowed && p != null && store != null && FeatureOpen(ProgressionConfig.Store))
            {
                long now = m_Game.UtcNowMs;
                foreach (var def in store.Items)
                    if (def.Currency == StoreCurrency.Ad && OutpostController.PlatformStoreQuote(StoreService.Quote(p, store, def, now)).Free) n = 1;
            }
            if (n == m_StoreBadgeShown) return;
            m_StoreBadgeShown = n;
            m_StoreButton.Badge.SetDot(n > 0);
        }

        // ───────────── События и окна магазина ─────────────

        void OnStoreEvent(OutpostEvent e)
        {
            var store = m_Game?.Configs?.Store;
            if (store == null) return;
            if (e.Type == OutpostEventType.ChestOpened && e.TargetId == OutpostIds.StoreSource) ShowChestReward(e, true);
            else if (e.Type == OutpostEventType.StorePurchased && !e.Value.IsZero && store.TryGet(e.ItemId, out var def))
                Toast(Loc.F(def.Kind == StoreItemKind.Gems ? "outpost.store.toast.gems" : "outpost.store.toast.elixir", StoreTileView.FormatAmount((long)e.Value)));
            else if (e.Type == OutpostEventType.BonusGold) Toast(Loc.F("outpost.store.toast.double", OutpostUiText.Money(e.Value)));
        }

        /// <summary>
        /// Окно награды сундука (ТЗ 10.5): карточки картинками — новые первыми, затем по редкости и копиям, до шести, остальные
        /// строкой «+N more», ниже эликсир. Сундук магазина (<paramref name="store"/>) — всегда своё окно. Сундук шахты показывает
        /// <see cref="OnCardEvent"/>, когда выпала новая карточка; пока окно сундуков шахты открыто, в него добавляются и следующие.
        /// </summary>
        void ShowChestReward(OutpostEvent e, bool store)
        {
            var cards = m_Game?.Configs?.Cards;
            if (cards == null) return;
            if (store || !MineChestRewardOpen)
            {
                m_ChestBatch = new ChestBatch { Store = store };
                if (store)
                {
                    StoreItemDef item = null;
                    foreach (var d in m_Game.Configs.Store.Items)
                        if (d.Kind == StoreItemKind.Chest && d.ChestId == e.ItemId) { item = d; break; }
                    m_ChestBatch.Title = item != null ? OutpostUiText.StoreItemName(item) : PresentationCatalog.GachaNameOrMimic(e.ItemId, cards.Chest(e.ItemId)?.MimicId);
                    m_ChestBatch.Icon = item != null ? StoreTileView.ArtOf(item) : "icon_chest_big";
                }
                else
                {
                    string mimic = cards.Chest(e.ItemId)?.MimicId;
                    m_ChestBatch.Title = PresentationCatalog.GachaNameOrMimic(e.ItemId, mimic);
                    m_ChestBatch.Icon = "icon_chest_" + PresentationCatalog.GachaTierOrMimic(e.ItemId, mimic);
                }
            }
            var batch = m_ChestBatch;
            batch.Chests++;
            batch.Cards += e.Count;
            batch.Elixir += e.Value;
            if (batch.Chests > 1) batch.Title = Loc.F("outpost.chest.title_many", batch.Chests);
            if (!string.IsNullOrEmpty(e.Detail))
                foreach (string part in e.Detail.Split(','))
                {
                    int colon = part.IndexOf(':');
                    string id = colon > 0 ? part.Substring(0, colon) : part;
                    long copies = colon > 0 && long.TryParse(part.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long c) ? c : 1;
                    var def = cards.Get(id);
                    if (def == null) continue;
                    // Снимок полученных карточек до этого показа отличает новые (как у тостов сундуков шахты).
                    bool isNew = m_KnownCards.Add(id);
                    int at = 0;
                    while (at < batch.Grants.Count && batch.Grants[at].Def != def) at++;
                    if (at == batch.Grants.Count) batch.Grants.Add(new ChestGrant { Def = def, Copies = copies, New = isNew });
                    else
                    {
                        var g = batch.Grants[at];
                        g.Copies += copies;
                        g.New |= isNew;
                        batch.Grants[at] = g;
                    }
                }
            RenderChestReward();
        }

        void RenderChestReward()
        {
            var batch = m_ChestBatch;
            batch.Grants.Sort((a, b) => a.New != b.New ? (a.New ? -1 : 1) : a.Def.Rarity != b.Def.Rarity ? b.Def.Rarity.CompareTo(a.Def.Rarity) : b.Copies.CompareTo(a.Copies));
            m_ChestReward.Begin(batch.Title, batch.Icon, Loc.F("outpost.store.chest_total", batch.Cards));
            int shown = Math.Min(batch.Grants.Count, ChestRewardCards);
            int rows = (shown + 2) / 3;
            var grid = m_ChestReward.UseExtra(rows > 0 ? rows * ChestCardTile.HeightPx * Px + (rows - 1) * ChestTileGap : 0f);
            while (m_ChestTiles.Count < shown) m_ChestTiles.Add(ChestCardTile.Create(grid, "card" + m_ChestTiles.Count));
            for (int i = 0; i < m_ChestTiles.Count; i++)
            {
                m_ChestTiles[i].SetActive(i < shown);
                if (i < shown) m_ChestTiles[i].Set(batch.Grants[i].Def, batch.Grants[i].Copies, batch.Grants[i].New);
            }
            m_ChestTilesShown = shown;
            if (batch.Grants.Count > shown) m_ChestReward.AddRow("icon_cards", Loc.Plural("outpost.store.chest_more", batch.Grants.Count - shown), PixelUi.TextDim);
            if (!batch.Elixir.IsZero) m_ChestReward.AddRow("icon_elixir", Loc.F("cards.toast.elixir", OutpostUiText.Money(batch.Elixir)), PixelUi.TextElixir);
            m_ChestReward.SetButton(0, Loc.T("outpost.offline.ok"), PixelButton.Green, HideChestReward);
            if (!ChestRewardOpen)
            {
                m_ChestReward.Show();
                Opened(m_ChestRewardEntry);
            }
            else m_ChestReward.Layout();
            m_NextRefresh = 0f;
        }

        /// <summary>Сетка карточек окна сундука: по три в ряд, ширина — целые пиксели арта, неполный ряд — по центру.</summary>
        void LayoutChestTiles(float width)
        {
            int shown = m_ChestTilesShown;
            if (shown == 0) return;
            float tile = Mathf.Min(ChestCardTile.FullWidthPx + 4, Mathf.Floor((width - 2 * ChestTileGap) / 3f / Px)) * Px;
            for (int i = 0; i < shown; i++)
            {
                int row = i / 3, inRow = Math.Min(3, shown - row * 3);
                float left = Mathf.Floor((width - inRow * tile - (inRow - 1) * ChestTileGap) / 2f / Px) * Px;
                m_ChestTiles[i].Place(left + i % 3 * (tile + ChestTileGap), row * (ChestCardTile.HeightPx * Px + ChestTileGap), tile);
            }
        }

        /// <summary>Закрыть окно награды сундука (кнопка OK, Back; playable закрывает сундук выхода сам).</summary>
        public void HideChestReward()
        {
            if (m_ChestReward != null) HideDialog(m_ChestReward, m_ChestRewardEntry);
            m_ChestBatch = null;
        }

        /// <summary>Шансы сундука (раскрытие содержимого лутбокса): число карточек и эликсир в этой шахте, редкость — card_rules.json.</summary>
        void ShowDropRates(StoreItemDef def)
        {
            var cards = m_Game?.Configs?.Cards;
            var chest = cards?.Chest(def.ChestId);
            if (chest == null || m_Game.Sim == null) return;
            CardChestService.Preview(cards, chest, m_Game.Sim.Run.Zone, m_Game.Stats, out int count, out long min, out long max);
            string elixir = min == max ? OutpostUiText.Money(min) : OutpostUiText.Money(min) + "–" + OutpostUiText.Money(max);
            m_DropRates.Begin(Loc.F("outpost.store.rates_title", OutpostUiText.StoreItemName(def)), StoreTileView.ArtOf(def),
                Loc.F("outpost.store.rates_body", count, elixir));
            var chances = chest.Chances ?? m_Game.Configs.CardRules.DefaultChances;
            long total = 0;
            foreach (int w in chances) total += w;
            for (int r = 0; r < chances.Length; r++)
            {
                if (chances[r] <= 0) continue;
                string percent = (chances[r] * 100.0 / Math.Max(1, total)).ToString("0.##", CultureInfo.InvariantCulture) + "%";
                m_DropRates.AddRow("icon_cards", Loc.F("outpost.store.rates_row", OutpostCardText.RarityName(r + 1), percent), PixelUi.TextColor);
            }
            m_DropRates.SetButton(0, Loc.T("outpost.campaign.close"), PixelButton.Blue, HideDropRates);
            m_DropRates.Show();
            Opened(m_DropRatesEntry);
        }

        void HideDropRates()
        {
            if (m_DropRates != null) HideDialog(m_DropRates, m_DropRatesEntry);
        }

        /// <summary>Итог тайм-скипа: золото крупно (как «Пока вас не было»), расчищено, завершение шахты, карточки и эликсир.</summary>
        void ShowSkipResult(OfflineSummary s)
        {
            m_SkipResultShown = s;
            string gold = PixelUi.AccentTag + "<size=200%>" + OutpostUiText.Money(s.GoldDelivered) + "</size></color>";
            var store = m_Game?.Configs?.Store;
            string art = store != null && store.TryGet(s.SkipItemId, out var def) ? StoreTileView.ArtOf(def) : "icon_clock";
            m_SkipResult.Begin(Loc.F("outpost.store.skip_title", OutpostUiText.SkipDuration(s.SkipMinutes)), art, Loc.F("outpost.offline.gold", gold));
            if (s.TargetsCleared > 0) m_SkipResult.AddRow("icon_pickaxe", Loc.F("outpost.store.skip_cleared", s.TargetsCleared), PixelUi.TextColor);
            if (s.CardsGained > 0 || s.ElixirGained > 0)
                m_SkipResult.AddRow("icon_cards", Loc.F("cards.offline", s.CardsGained, OutpostUiText.Money(s.ElixirGained)), PixelUi.TextColor);
            if (s.StageCompleted) m_SkipResult.AddRow("icon_check", Loc.T("outpost.store.skip_completed"), PixelUi.TextGood);
            m_SkipResult.SetButton(0, Loc.T("outpost.offline.ok"), PixelButton.Green, DismissSkipWindow);
            m_SkipResult.Show();
            Opened(m_SkipResultEntry);
        }

        void DismissSkipWindow()
        {
            HideDialog(m_SkipResult, m_SkipResultEntry);
            m_Game?.DismissSkip();
            m_NextRefresh = 0f;
        }
    }
}
