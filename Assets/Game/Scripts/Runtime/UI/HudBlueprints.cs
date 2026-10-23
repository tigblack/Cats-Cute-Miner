using System;
using System.Collections.Generic;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Экран «Карты» (ТЗ 10.1, 10.7, 13.1) по образцу Gold &amp; Goblins, на ките. Открывается кнопкой с картами справа
    /// от «Нанять»; её бейдж — сколько карт можно улучшить и сколько бесплатных сундуков готово. Экран на весь safe area:
    /// сверху «i» (пояснение) и валюты (золото, эликсир, самоцветы); деревянная шапка — сортировка, «Карты»,
    /// «Все карты дают постоянные бонусы!», закрытие; «Найдено N/M» и бесплатный сундук; ниже — прокручиваемая сетка
    /// по 3 карты в ряд. Карта — рамка редкости, короткое имя, портрет на фоне редкости (котик-управляющий с самоцветом
    /// цвета шахты, в каске или шляпе; кузница; предмет), лента эффекта, уровень и шкала копий «есть/нужно» — зелёная
    /// со стрелкой, когда карту можно улучшить. Ещё не найденная — рубашкой: «?» и шахта, с которой она выпадает.
    /// Тап по карте — окно карты: эффект сейчас и на следующем уровне, копии и эликсир, «Улучшить» с ценой.
    /// Ячейки создаются один раз и переиспользуются; тексты меняются только при смене значений.
    /// </summary>
    public sealed partial class Hud
    {
        const int CardColumns = 3;
        /// <summary>Ячейка: высота 106 пикселей арта, ширина — по экрану (от 72: экраны до 9:21), промежуток 4.</summary>
        const float CellH = 106 * Px, CellGap = 4 * Px, CellMaxW = 80 * Px, GridPad = 6 * Px;
        /// <summary>Шапка экрана: строка валют, деревянная планка, «Найдено», бесплатный сундук.</summary>
        const float CardsTopH = 18 * Px, CardsHeaderH = 34 * Px, CardsFoundH = 16 * Px, FreeChestHeight = 20 * Px;
        /// <summary>Кнопки шапки (сортировка, закрытие) и «i».</summary>
        const float CardsButton = 22 * Px;
        /// <summary>Окно карты: ячейка слева, текст справа; кнопка улучшения выше обычной — подпись над ценой.</summary>
        const float UpgradeHeight = 36 * Px, UpgradePriceBottom = 4 * Px, UpgradeLabelBottom = 20 * Px;
        /// <summary>Рубашка ещё не найденной карты приглушена (умножение цвета спрайта), лапка и «?» — тоже.</summary>
        static readonly Color FaceDownTint = new Color(0.55f, 0.6f, 0.78f, 1f);

        /// <summary>Ячейка карты: рамка, имя, портрет (до четырёх слоёв), лента, уровень, шкала копий; рубашка.</summary>
        sealed class CardCell
        {
            public RectTransform Root;
            public Image Frame, Bg, Art, Hat, Gem, Paws, Tag, BackPaw, CardIcon, Arrow;
            public TextMeshProUGUI Title, TagText, Level, Count, Unknown, Unlock;
            public PixelBar Bar;
            public string CardId;
            public bool FaceDown;
        }

        RectTransform m_CardsRoot;
        PixelIconText m_BpGold, m_BpElixir, m_BpHard;
        TextMeshProUGUI m_BpFound, m_BlueprintsEmpty;
        Ui.ButtonRefs m_FreeChest;
        RectTransform m_BlueprintsList;
        GridLayoutGroup m_CardsGrid;
        readonly List<CardCell> m_CardCells = new List<CardCell>();
        readonly List<CardDto> m_CardOrder = new List<CardDto>();
        readonly HashSet<string> m_Required = new HashSet<string>();
        GameSession m_SortSession;
        Comparison<CardDto> m_CardComparison;
        bool m_CardsSortReady;
        float m_CardsCellW = -1f;

        // Окно карты.
        PixelWindow m_CardWindow;
        CardCell m_CardBig;
        TextMeshProUGUI m_CardInfo;
        Ui.ButtonRefs m_CardUpgrade;
        PixelIconText m_CardPrice;
        string m_CardShown;

        // Бейдж кнопки «Карты»: сколько карт можно улучшить (пересчёт — только при смене карт, эликсира или кампании).
        int m_UpgradeableSignature = int.MinValue, m_Upgradeable;

        void BuildBlueprints()
        {
            m_CardsRoot = Ui.Rect(m_Safe, "Blueprints").Anchor(0, 0, 1, 1);
            var bg = m_CardsRoot.gameObject.AddComponent<Image>();
            bg.color = PixelPalette.Navy1;
            bg.raycastTarget = true;
            m_CardsRoot.gameObject.AddComponent<PixelPop>();

            // Строка валют: «i» слева, золото, эликсир, самоцветы.
            var top = Ui.Rect(m_CardsRoot, "top").Anchor(0, 1, 1, 1, new Vector2(GridPad, -(CardsTopH + 2 * Px)), new Vector2(-GridPad, -2 * Px));
            var info = PixelUi.Button(top, "info", "", PixelButton.Blue, () => Toast(BlueprintsHint, 5f), PixelUi.FontBody);
            PinSquare(info.Rect, 0f, CardsTopH);
            info.SetIcon("icon_info");
            m_BpGold = PixelUi.IconText(top, "gold", "icon_coin", "", PixelUi.FontLarge, PixelUi.TextColor);
            PinTopLeft(m_BpGold.Root, 0.14f);
            m_BpElixir = PixelUi.IconText(top, "elixir", "icon_elixir", "", PixelUi.FontLarge, PixelUi.TextElixir);
            PinTopLeft(m_BpElixir.Root, 0.45f);
            m_BpHard = PixelUi.IconText(top, "hard", "icon_gem", "", PixelUi.FontLarge, PixelUi.TextColor);
            PinTopLeft(m_BpHard.Root, 0.72f);

            // Деревянная шапка: сортировка, заголовок с подзаголовком, закрытие.
            float y = CardsTopH + 4 * Px;
            var header = PixelUi.Panel(m_CardsRoot, "header", PixelPanel.Wood).rectTransform;
            header.Anchor(0, 1, 1, 1, new Vector2(0f, -(y + CardsHeaderH)), new Vector2(0f, -y));
            var sort = PixelUi.Button(header, "sort", "", PixelButton.Blue, ToggleCardsSort, PixelUi.FontBody);
            PinSquare(sort.Rect, 0f, CardsButton, 4 * Px);
            sort.SetIcon("icon_sort");
            var close = PixelUi.Button(header, "close", "", PixelButton.Red, () => ToggleBlueprints(), PixelUi.FontBody);
            PinSquare(close.Rect, 1f, CardsButton, 4 * Px);
            close.SetIcon("icon_close");
            var title = PixelUi.Text(header, "title", Loc.T("hud.blueprints.title"), PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            title.rectTransform.Anchor(0, 0.42f, 1, 1, new Vector2(CardsButton + 8 * Px, 0f), new Vector2(-(CardsButton + 8 * Px), -3 * Px));
            title.textWrappingMode = TextWrappingModes.NoWrap;
            var subtitle = PixelUi.Text(header, "subtitle", Loc.T("hud.cards.subtitle"), PixelUi.FontSmall, PixelUi.TextColor, TextAlignmentOptions.Center, false, true);
            subtitle.rectTransform.Anchor(0, 0, 1, 0.42f, new Vector2(CardsButton + 8 * Px, 4 * Px), new Vector2(-(CardsButton + 8 * Px), 0f));
            subtitle.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(subtitle, PixelUi.FontSmall * 0.7f, PixelUi.FontSmall);
            y += CardsHeaderH + 3 * Px;

            // «Найдено N/M»: синяя плашка с картами.
            var found = PixelUi.Sliced(m_CardsRoot, "found", "ui_btn_blue");
            found.raycastTarget = false;
            found.rectTransform.Anchor(0, 1, 1, 1, new Vector2(GridPad, -(y + CardsFoundH)), new Vector2(-GridPad, -y));
            var foundIcon = PixelUi.Icon(found.transform, "icon", "icon_cards", 1);
            foundIcon.rectTransform.anchorMin = foundIcon.rectTransform.anchorMax = new Vector2(0.28f, 0.5f);
            foundIcon.rectTransform.anchoredPosition = new Vector2(0f, Px);
            m_BpFound = PixelUi.Text(found.transform, "text", "", PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            m_BpFound.rectTransform.Anchor(0.32f, 0, 0.9f, 1, new Vector2(0f, 2 * Px), Vector2.zero);
            m_BpFound.textWrappingMode = TextWrappingModes.NoWrap;
            y += CardsFoundH + 3 * Px;

            m_FreeChest = PixelUi.Button(m_CardsRoot, "freeChest", "", PixelButton.Gold, () => m_Game.ClaimFreeChest(), PixelUi.FontBody);
            m_FreeChest.Rect.Anchor(0, 1, 1, 1, new Vector2(GridPad, -(y + FreeChestHeight)), new Vector2(-GridPad, -y));
            y += FreeChestHeight + 3 * Px;

            // Сетка карт в прокрутке.
            var viewport = Ui.Rect(m_CardsRoot, "viewport").Anchor(0, 0, 1, 1, new Vector2(0f, 2 * Px), new Vector2(0f, -y));
            viewport.gameObject.AddComponent<RectMask2D>();
            // Прозрачная подложка ловит лучи UI: сетка прокручивается жестом в любом месте, в том числе между картами.
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0f);
            m_BlueprintsList = Ui.Rect(viewport, "content");
            m_BlueprintsList.anchorMin = new Vector2(0, 1);
            m_BlueprintsList.anchorMax = new Vector2(1, 1);
            m_BlueprintsList.pivot = new Vector2(0.5f, 1);
            m_BlueprintsList.offsetMin = Vector2.zero;
            m_BlueprintsList.offsetMax = Vector2.zero;
            m_CardsGrid = m_BlueprintsList.gameObject.AddComponent<GridLayoutGroup>();
            m_CardsGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            m_CardsGrid.constraintCount = CardColumns;
            m_CardsGrid.spacing = new Vector2(CellGap, CellGap);
            m_CardsGrid.childAlignment = TextAnchor.UpperCenter;
            m_CardsGrid.padding = new RectOffset(0, 0, (int)(2 * Px), (int)(8 * Px));
            m_CardsGrid.cellSize = new Vector2(CellMaxW, CellH);
            var fitter = m_BlueprintsList.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = m_BlueprintsList;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            m_BlueprintsEmpty = PixelUi.Text(viewport, "empty", Loc.T("hud.blueprints.empty"), PixelUi.FontBody, PixelUi.TextDim, TextAlignmentOptions.Center, false, true);
            m_BlueprintsEmpty.rectTransform.Anchor(0, 0.6f, 1, 1);
            m_BlueprintsEmpty.gameObject.SetActive(false);

            BuildCardWindow();
            m_CardsRoot.gameObject.SetActive(false);
        }

        /// <summary>Квадратная кнопка у левого (x = 0) или правого (x = 1) края родителя, по центру высоты.</summary>
        static void PinSquare(RectTransform rt, float x, float size, float inset = 0f)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(x, 0.5f);
            rt.pivot = new Vector2(x, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(x < 0.5f ? inset : -inset, 0f);
        }

        /// <summary>Строка «иконка + число» по центру высоты родителя, от доли ширины <paramref name="x"/>.</summary>
        static void PinTopLeft(RectTransform rt, float x)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(x, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>Окно карты: та же ячейка крупно слева, сведения справа, «Улучшить» с ценой в эликсире внизу.</summary>
        void BuildCardWindow()
        {
            m_CardWindow = PixelUi.Window(m_CardsRoot, "CardDetail", "", () => m_CardWindow.Hide());
            m_CardWindow.Box.Anchor(0.04f, 0.5f, 0.96f, 0.5f, new Vector2(0f, -92 * Px), new Vector2(0f, 92 * Px));
            Ui.SetActive(m_CardWindow.BodyText, false);
            var body = m_CardWindow.Body;
            m_CardBig = CreateCardCell(body, false);
            var crt = m_CardBig.Root;
            crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.sizeDelta = new Vector2(CellMaxW, CellH);
            crt.anchoredPosition = Vector2.zero;
            m_CardInfo = PixelUi.Text(body, "info", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.TopLeft, false, true);
            m_CardInfo.rectTransform.Anchor(0, 0, 1, 1, new Vector2(CellMaxW + 6 * Px, 0f), Vector2.zero);
            Ui.AutoSize(m_CardInfo, PixelUi.FontSmall, PixelUi.FontBody);

            m_CardUpgrade = m_CardWindow.AddButton("upgrade", Loc.T("hud.card.upgrade"), PixelButton.Green, OnCardUpgrade, PixelUi.FontBody);
            var buttons = m_CardWindow.Buttons;
            buttons.offsetMax = new Vector2(buttons.offsetMax.x, buttons.offsetMin.y + UpgradeHeight);
            // Кнопка выше обычной: тело окна кончается над ней.
            var bodyRt = m_CardWindow.Body;
            bodyRt.offsetMin = new Vector2(bodyRt.offsetMin.x, buttons.offsetMin.y + UpgradeHeight + 4 * Px);
            m_CardUpgrade.Label.enableAutoSizing = false;
            m_CardPrice = PixelUi.IconText(m_CardUpgrade.Content, "price", "icon_elixir", "", PixelUi.FontBody, PixelUi.TextColor);
            m_CardPrice.Root.anchorMin = m_CardPrice.Root.anchorMax = new Vector2(0.5f, 0f);
            // Строка ставится нижним краем: её высота (иконка 16 px) не опускает цену на губу кнопки.
            m_CardPrice.Root.pivot = new Vector2(0.5f, 0f);
            m_CardPrice.Root.anchoredPosition = new Vector2(0f, UpgradePriceBottom);
        }

        public void ToggleBlueprints()
        {
            bool open = !BlueprintsOpen;
            // Под экраном завершения кампании экран не открывается: его бы не было видно.
            if (open && CampaignEndVisible) return;
            if (!open)
            {
                if (m_CardWindow.Visible) m_CardWindow.Hide();
                m_CardsRoot.gameObject.SetActive(false);
                UiSound.PlayClose();
                return;
            }
            // Сначала показ, затем ячейки: новые создаются в активной иерархии (тексты TMP инициализированы).
            m_CardsRoot.SetAsLastSibling();
            m_CardsRoot.gameObject.SetActive(true);
            UiSound.PlayOpen();
            RebuildBlueprints();
        }

        /// <summary>Окно карты открыто (поверх экрана «Карты»).</summary>
        public bool CardDetailOpen => m_CardWindow != null && m_CardWindow.Visible;

        /// <summary>Карта, показанная в окне карты, или null.</summary>
        public string CardDetailId => CardDetailOpen ? m_CardShown : null;

        void ToggleCardsSort()
        {
            m_CardsSortReady = !m_CardsSortReady;
            Toast(Loc.T(m_CardsSortReady ? "hud.cards.sort_ready" : "hud.cards.sort_collection"));
            RebuildBlueprints();
        }

        /// <summary>Сколько карт коллекции (редкость ниже Majestic) найдено; подпись состояния карт для пересборки ячеек.</summary>
        static void CountCards(GameSession s, out int owned, out int total, out int signature)
        {
            owned = 0;
            total = 0;
            signature = 17;
            var have = s.Player.Cards;
            var cards = s.Balance.CardsInOrder;
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c.Rarity >= Rarity.Majestic) continue;
                total++;
                unchecked
                {
                    if (have.TryGetValue(c.Id, out var cs))
                    {
                        if (cs.Level > 0) owned++;
                        signature = signature * 31 + cs.Level * 1009 + cs.Duplicates;
                    }
                    else signature *= 31;
                }
            }
        }

        /// <summary>Сколько карт можно улучшить сейчас (бейдж кнопки «Карты»): пересчёт при смене карт, эликсира или кампании.</summary>
        int UpgradeableCards(GameSession s)
        {
            CountCards(s, out _, out _, out int signature);
            unchecked
            {
                signature = signature * 31 + s.Player.Elixir.GetHashCode();
                signature = signature * 31 + (s.Player.CampaignCompleted ? 1 : 0);
            }
            if (signature == m_UpgradeableSignature) return m_Upgradeable;
            m_UpgradeableSignature = signature;
            int n = 0;
            var cards = s.Balance.CardsInOrder;
            for (int i = 0; i < cards.Count; i++)
                if (cards[i].Rarity < Rarity.Majestic && s.CanUpgradeCard(cards[i].Id) == CommandResult.Ok) n++;
            m_Upgradeable = n;
            return n;
        }

        /// <summary>
        /// Шапка «Карт» (ТЗ 10.1, 10.7, 13.1): валюты (самоцветы — только здесь, не в главном HUD), «Найдено N/M», кнопка
        /// бесплатного сундука с запасом и таймером следующего. Ячейки пересобираются, когда меняются карты или эликсир.
        /// </summary>
        void RefreshBlueprintsHeader(GameSession s)
        {
            var fc = s.Player.FreeChest;
            var cycle = s.Balance.FreeGachaCycle;
            CountCards(s, out int owned, out int total, out int signature);
            int campaign = s.Player.CampaignCompleted ? 1 : 0;
            var rowsKey = new ValueKey { A = signature, B = owned, C = campaign, N1 = s.Player.Elixir };
            if (m_BpRowsValid && !rowsKey.Equals(m_BpRowsKey))
            {
                RebuildBlueprints();
                return;
            }
            long timer = fc.Enabled && fc.Stock < cycle.MaxFreeGachas ? CeilSeconds(GameSession.TicksToSeconds(fc.TimerTicks)) : -1;
            var key = new ValueKey
            {
                A = owned, B = total, C = fc.Enabled ? 1 : 0, D = fc.Stock, E = campaign, L = timer,
                N1 = s.Player.Elixir, N2 = s.Player.Hard, N3 = s.Mine.Gold,
            };
            if (m_BpValid && key.Equals(m_BpKey)) return;
            m_BpKey = key;
            m_BpValid = true;

            m_BpGold.SetText(NumberFormat.Format(s.Mine.Gold));
            m_BpElixir.SetText(NumberFormat.Format(s.Player.Elixir));
            m_BpHard.SetText(NumberFormat.Format(s.Player.Hard));
            Ui.SetText(m_BpFound, Loc.F("hud.cards.found", owned, total));
            if (!fc.Enabled)
            {
                int n = cycle.StartMineshaftNum;
                m_FreeChest.SetLabel(Loc.F("hud.free_chest.locked", cycle.StartRank, Loc.Plural("hud.free_chest.activated_shafts", n)));
                m_FreeChest.SetInteractable(false);
            }
            else
            {
                var sb = m_Sb.Clear();
                sb.Append(fc.Stock > 0 ? Loc.F("hud.free_chest.open", fc.Stock, cycle.MaxFreeGachas) : Loc.F("hud.free_chest.stock", fc.Stock, cycle.MaxFreeGachas));
                if (fc.Stock < cycle.MaxFreeGachas) sb.Append(" · ").Append(Loc.F("hud.free_chest.next", NumberFormat.FormatDuration(GameSession.TicksToSeconds(fc.TimerTicks))));
                m_FreeChest.SetLabel(sb.ToString());
                m_FreeChest.SetInteractable(fc.Stock > 0);
            }
        }

        /// <summary>
        /// Сетка «Карт» (ТЗ 10.1): все карты редкостей 1–5 (ca062 не показывается). Порядок коллекции, как на референсе:
        /// найденные по SortingWeight из данных (кузница, управляющие по шахтам, старшие, предметы), затем ненайденные — по шахте,
        /// с которой они выпадают. Сортировка «готовые к улучшению» поднимает их наверх. Ячейки переиспользуются.
        /// </summary>
        public void RebuildBlueprints()
        {
            var s = m_Game.Session;
            if (s == null || m_CardsRoot == null) return;
            FitCardCells();
            m_Required.Clear();
            foreach (var sh in s.Layout.Shafts) if (!string.IsNullOrEmpty(sh.CardId)) m_Required.Add(sh.CardId);
            m_CardOrder.Clear();
            foreach (var c in s.Balance.CardsInOrder) if (c.Rarity < Rarity.Majestic) m_CardOrder.Add(c);
            m_SortSession = s;
            m_CardOrder.Sort(m_CardComparison);
            for (int i = 0; i < m_CardOrder.Count; i++)
            {
                if (i == m_CardCells.Count) m_CardCells.Add(CreateCardCell(m_BlueprintsList, true));
                var cell = m_CardCells[i];
                Ui.SetActive(cell.Root, true);
                if (cell.Root.GetSiblingIndex() != i) cell.Root.SetSiblingIndex(i);
                FillCardCell(s, cell, m_CardOrder[i]);
            }
            for (int i = m_CardOrder.Count; i < m_CardCells.Count; i++) Ui.SetActive(m_CardCells[i].Root, false);
            Ui.SetActive(m_BlueprintsEmpty, m_CardOrder.Count == 0);

            CountCards(s, out int owned, out _, out int signature);
            m_BpRowsKey = new ValueKey { A = signature, B = owned, C = s.Player.CampaignCompleted ? 1 : 0, N1 = s.Player.Elixir };
            m_BpRowsValid = true;
            m_BpValid = false;
            RefreshBlueprintsHeader(s);
            if (CardDetailOpen) FillCardWindow(s);
        }

        /// <summary>Ширина ячейки по ширине экрана: 3 в ряд, целые пиксели арта, не шире 80.</summary>
        void FitCardCells()
        {
            float width = m_CardsRoot.rect.width - 2 * GridPad;
            if (width <= 0f) return;
            float cell = Mathf.Min(CellMaxW, Mathf.Floor((width - (CardColumns - 1) * CellGap) / CardColumns / Px) * Px);
            if (Mathf.Approximately(cell, m_CardsCellW)) return;
            m_CardsCellW = cell;
            m_CardsGrid.cellSize = new Vector2(cell, CellH);
        }

        int CompareCards(CardDto a, CardDto b)
        {
            var s = m_SortSession;
            int la = s.CardLevel(a.Id), lb = s.CardLevel(b.Id);
            if (m_CardsSortReady)
            {
                int ua = s.CanUpgradeCard(a.Id) == CommandResult.Ok ? 0 : 1, ub = s.CanUpgradeCard(b.Id) == CommandResult.Ok ? 0 : 1;
                if (ua != ub) return ua.CompareTo(ub);
            }
            int oa = la > 0 ? 0 : 1, ob = lb > 0 ? 0 : 1;
            if (oa != ob) return oa.CompareTo(ob);
            if (oa == 1 && a.MineUnlock != b.MineUnlock) return a.MineUnlock.CompareTo(b.MineUnlock);
            if (a.SortingWeight != b.SortingWeight) return b.SortingWeight.CompareTo(a.SortingWeight);
            return string.CompareOrdinal(a.Id, b.Id);
        }

        // ───────────── Ячейка карты ─────────────

        /// <summary>Ячейка 80×106 пикселей арта (ширину задаёт сетка); <paramref name="tap"/> — тап открывает окно карты.</summary>
        CardCell CreateCardCell(Transform parent, bool tap)
        {
            var cell = new CardCell();
            cell.Frame = PixelUi.Sliced(parent, "card", PixelUi.CardFrameSprite(PixelRarity.Common));
            cell.Root = cell.Frame.rectTransform;
            cell.Root.sizeDelta = new Vector2(CellMaxW, CellH);
            var t = cell.Frame.transform;
            if (tap)
            {
                cell.Frame.raycastTarget = true;
                var btn = cell.Frame.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.navigation = new Navigation { mode = Navigation.Mode.None };
                btn.targetGraphic = cell.Frame;
                btn.onClick.AddListener(UiSound.PlayClick);
                btn.onClick.AddListener(() => OpenCardWindow(cell.CardId));
            }
            else cell.Frame.raycastTarget = false;

            cell.Title = PixelUi.Text(t, "title", "", PixelUi.FontSmall, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            cell.Title.rectTransform.Anchor(0, 1, 1, 1, new Vector2(4 * Px, -19 * Px), new Vector2(-4 * Px, -5 * Px));
            Ui.AutoSize(cell.Title, PixelUi.FontSmall * 0.7f, PixelUi.FontSmall);

            // Портрет: фон редкости и слои арта с общей опорой (центр холста 48×44) чуть ниже центра фона.
            cell.Bg = PixelUi.Icon(t, "bg", "card_bg_common", 1);
            cell.Bg.rectTransform.anchorMin = cell.Bg.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            cell.Bg.rectTransform.anchoredPosition = new Vector2(0f, -43 * Px);
            cell.Art = Layer(cell.Bg.transform, "art");
            cell.Hat = Layer(cell.Bg.transform, "hat");
            cell.Gem = Layer(cell.Bg.transform, "gem");
            cell.Paws = Layer(cell.Bg.transform, "paws");

            // Лента эффекта на нижней кромке портрета.
            cell.Tag = PixelUi.Sliced(t, "tag", "ui_card_tag");
            cell.Tag.raycastTarget = false;
            var trt = cell.Tag.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(44 * Px, 11 * Px);
            trt.anchoredPosition = new Vector2(0f, -61 * Px);
            cell.TagText = PixelUi.Text(cell.Tag.transform, "text", "", PixelUi.FontSmall, PixelUi.TextAccentLight, TextAlignmentOptions.Center, true, true);
            cell.TagText.rectTransform.Anchor(0, 0, 1, 1, new Vector2(2 * Px, Px), new Vector2(-2 * Px, 0f));
            cell.TagText.textWrappingMode = TextWrappingModes.NoWrap;

            cell.Level = PixelUi.Text(t, "level", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            cell.Level.rectTransform.Anchor(0, 1, 1, 1, new Vector2(3 * Px, -85 * Px), new Vector2(-3 * Px, -73 * Px));
            cell.Level.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(cell.Level, PixelUi.FontSmall, PixelUi.FontBody);

            // Шкала копий с числом поверх и иконкой карты (или стрелкой улучшения) у левого края.
            cell.Bar = PixelUi.Bar(t, "copies", PixelBarFill.Purple);
            cell.Bar.Rect.Anchor(0, 1, 1, 1, new Vector2(9 * Px, -99 * Px), new Vector2(-5 * Px, -87 * Px));
            cell.Count = PixelUi.Text(cell.Bar.Rect, "count", "", PixelUi.FontSmall, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            cell.Count.rectTransform.Anchor(0, 0, 1, 1, new Vector2(4 * Px, Px), new Vector2(-Px, 0f));
            cell.Count.textWrappingMode = TextWrappingModes.NoWrap;
            cell.CardIcon = PixelUi.Icon(t, "cardIcon", "icon_card_small", 1);
            cell.CardIcon.rectTransform.anchorMin = cell.CardIcon.rectTransform.anchorMax = new Vector2(0f, 1f);
            cell.CardIcon.rectTransform.anchoredPosition = new Vector2(9 * Px, -93 * Px);
            cell.Arrow = PixelUi.Icon(t, "arrow", "icon_upgrade", 1);
            cell.Arrow.rectTransform.anchorMin = cell.Arrow.rectTransform.anchorMax = new Vector2(0f, 1f);
            cell.Arrow.rectTransform.anchoredPosition = new Vector2(9 * Px, -92 * Px);

            // Рубашка: лапка, «?» и шахта, с которой карта выпадает.
            cell.BackPaw = PixelUi.Icon(t, "paw", "ui_card_paw", 2);
            cell.BackPaw.rectTransform.anchorMin = cell.BackPaw.rectTransform.anchorMax = new Vector2(0.5f, 0.62f);
            cell.BackPaw.color = FaceDownTint;
            cell.Unknown = PixelUi.Text(t, "unknown", Loc.T("hud.cards.unknown"), PixelUi.FontHero, PixelUi.TextDim, TextAlignmentOptions.Center, true, true);
            cell.Unknown.rectTransform.Anchor(0, 0.25f, 1, 0.45f);
            cell.Unlock = PixelUi.Text(t, "unlock", "", PixelUi.FontSmall, PixelUi.TextDim, TextAlignmentOptions.Center, true, true);
            cell.Unlock.rectTransform.Anchor(0, 0, 1, 0, new Vector2(4 * Px, 8 * Px), new Vector2(-4 * Px, 18 * Px));
            cell.Unlock.textWrappingMode = TextWrappingModes.NoWrap;
            cell.FaceDown = true;
            SetFaceDown(cell, false);
            return cell;
        }

        /// <summary>Слой портрета: иконка с опорой из атласа в центре фона (сдвиг на 2 пикселя арта вниз).</summary>
        static Image Layer(Transform bg, string name)
        {
            var img = PixelUi.Icon(bg, name, "card_cat", 1);
            img.rectTransform.anchoredPosition = new Vector2(0f, -2 * Px);
            return img;
        }

        static void SetFaceDown(CardCell cell, bool down)
        {
            if (cell.FaceDown == down) return;
            cell.FaceDown = down;
            if (down) PixelUi.SetSliced(cell.Frame, PixelArt.Has("ui_card_back_plain") ? "ui_card_back_plain" : "ui_card_back");
            // Ненайденная — приглушённой рубашкой: найденные карты на экране заметнее (как на референсе).
            cell.Frame.color = down ? FaceDownTint : Color.white;
            Ui.SetActive(cell.Title, !down);
            Ui.SetActive(cell.Bg, !down);
            Ui.SetActive(cell.Tag, !down);
            Ui.SetActive(cell.Level, !down);
            Ui.SetActive(cell.Bar, !down);
            Ui.SetActive(cell.CardIcon, !down);
            Ui.SetActive(cell.BackPaw, down);
            Ui.SetActive(cell.Unknown, down);
            Ui.SetActive(cell.Unlock, down);
            if (down) Ui.SetActive(cell.Arrow, false);
        }

        /// <summary>Портрет карты: управляющий — котик с самоцветом цвета шахты (каска или шляпа), кузница или предмет.</summary>
        static void FillPortrait(CardCell cell, CardDto c)
        {
            PixelUi.SetIcon(cell.Bg, PresentationCatalog.CardBackground(c.Rarity), 1);
            string target = PresentationCatalog.CardTarget(c);
            bool cat = c.IsManager && target != BalanceRepository.CartId;
            string art = PresentationCatalog.CardArt(c);
            PixelUi.SetIcon(cell.Art, art, art.StartsWith("icon_", StringComparison.Ordinal) ? 2 : 1);
            Ui.SetActive(cell.Hat, cat);
            Ui.SetActive(cell.Gem, cat);
            Ui.SetActive(cell.Paws, cat);
            if (!cat) return;
            PixelUi.SetIcon(cell.Hat, c.Rarity == Rarity.Uncommon ? "card_cat_hat" : "card_cat_helmet", 1);
            PixelUi.SetIcon(cell.Gem, "card_gem", 1);
            cell.Gem.color = PresentationCatalog.ShaftGemColor(target);
            PixelUi.SetIcon(cell.Paws, "card_cat_paws", 1);
        }

        void FillCardCell(GameSession s, CardCell cell, CardDto c)
        {
            if (cell.CardId != c.Id)
            {
                cell.CardId = c.Id;
                cell.Root.gameObject.name = "card_" + c.Id;
            }
            int level = s.CardLevel(c.Id);
            SetFaceDown(cell, level < 1);
            if (level < 1)
            {
                Ui.SetText(cell.Unlock, Loc.F("hud.cards.unlock", c.MineUnlock));
                return;
            }
            PixelUi.SetSliced(cell.Frame, PixelUi.CardFrameSprite(PixelUi.RarityOf(c.Rarity)));
            Ui.SetText(cell.Title, PresentationCatalog.CardShortName(s.Balance, c.Id));
            FillPortrait(cell, c);
            Ui.SetText(cell.TagText, PresentationCatalog.CardTag(c));
            cell.TagText.color = GameSession.IsAppliedCardCode(c.StatModifierType) ? PixelUi.TextAccentLight : PixelUi.TextDim;
            Ui.SetText(cell.Level, Loc.F("hud.cards.level", level));

            int duplicates = s.Player.Cards.TryGetValue(c.Id, out var cs) ? cs.Duplicates : 0;
            bool hasCost = s.TryGetCardUpgradeCost(c.Id, out int need, out _);
            bool ready = s.CanUpgradeCard(c.Id) == CommandResult.Ok;
            if (hasCost)
            {
                Ui.SetText(cell.Count, PixelUi.CountText(duplicates) + "/" + PixelUi.CountText(need));
                cell.Bar.SetValue(need > 0 ? Mathf.Clamp01(duplicates / (float)need) : 1f);
            }
            else
            {
                Ui.SetText(cell.Count, Loc.T("hud.cards.max"));
                cell.Bar.SetValue(1f);
            }
            // Можно улучшить — зелёная шкала и стрелка вместо иконки карты; иначе — фиолетовая (копии копятся).
            cell.Bar.SetFill(ready ? PixelBarFill.Green : PixelBarFill.Purple);
            Ui.SetActive(cell.Arrow, ready);
            Ui.SetActive(cell.CardIcon, !ready);
        }

        /// <summary>Иконка 16×16 карты для строк списков (окно сундука): управляющий — шахта или кузница, предмет — по коду.</summary>
        static string CardIcon(CardDto c)
        {
            if (c.IsManager) return PresentationCatalog.CardTarget(c) == BalanceRepository.CartId ? "icon_forge" : "icon_shaft";
            switch (c.StatModifierType)
            {
                case 10: return "icon_coin";
                case 7: return "icon_clock";
                case 1: return "icon_cat";
                case 22: return "icon_dynamite";
                case 23: return "icon_star";
                default: return "icon_cards";
            }
        }

        // ───────────── Окно карты ─────────────

        void OpenCardWindow(string cardId)
        {
            var s = m_Game.Session;
            if (s == null || string.IsNullOrEmpty(cardId)) return;
            m_CardShown = cardId;
            m_CardWindow.Show();
            FillCardWindow(s);
        }

        /// <summary>Окно карты: имя, редкость и уровень, эффект сейчас и на следующем уровне, копии и эликсир, «Улучшить».</summary>
        void FillCardWindow(GameSession s)
        {
            if (m_CardShown == null || !s.Balance.Cards.TryGetValue(m_CardShown, out var c)) return;
            m_CardWindow.SetTitle(PresentationCatalog.CardName(s.Balance, c.Id));
            FillCardCell(s, m_CardBig, c);
            int level = s.CardLevel(c.Id);
            int max = s.Balance.CardMaxLevel(c.Rarity);
            int duplicates = s.Player.Cards.TryGetValue(c.Id, out var cs) ? cs.Duplicates : 0;
            bool hasCost = s.TryGetCardUpgradeCost(c.Id, out int need, out GameNumber elixir);
            bool applied = GameSession.IsAppliedCardCode(c.StatModifierType);

            var sb = m_Sb.Clear();
            sb.Append(PixelUi.AccentTag).Append(Loc.F("hud.card.detail.level", Rarity.Name(c.Rarity), level, max)).Append(ColorClose).Append('\n');
            if (level < 1) sb.Append(Loc.F("hud.card.detail.not_owned", c.MineUnlock)).Append('\n');
            if (applied && level >= 1) sb.Append(Loc.F("hud.card.detail.now", PresentationCatalog.CardEffect(s, c))).Append('\n');
            if (applied && hasCost)
                sb.Append(Loc.F("hud.card.detail.next", level + 1, PresentationCatalog.CardEffectText(c, GameSession.CardEffectAtLevel(c, level + 1)))).Append('\n');
            if (!applied || level < 1) sb.Append(PresentationCatalog.CardEffect(s, c)).Append('\n');
            if (level >= 1)
            {
                if (hasCost) sb.Append(Loc.F("hud.card.detail.copies", duplicates, need, NumberFormat.Format(elixir)));
                else sb.Append(Loc.F("hud.card.detail.copies_max", duplicates));
            }
            if (m_Required.Contains(c.Id))
            {
                int reqLevel = 0;
                foreach (var sh in s.Layout.Shafts) if (sh.CardId == c.Id) reqLevel = Math.Max(reqLevel, sh.CardLevel);
                sb.Append('\n').Append(PixelUi.AccentTag).Append(Loc.F("hud.card.required", reqLevel)).Append(ColorClose);
            }
            Ui.SetText(m_CardInfo, sb.ToString());

            var check = s.CanUpgradeCard(c.Id);
            string label;
            if (check == CommandResult.Ok) label = Loc.T("hud.card.upgrade");
            else if (check == CommandResult.MaxLevel) label = Loc.T("hud.card.max");
            // Короткие подписи кнопки: причины Core (core.reject.*) длиннее и нужны тостам отказа.
            else if (check == CommandResult.NotEnoughDuplicates) label = Loc.T("hud.card.need_copies");
            else if (check == CommandResult.NotEnoughElixir) label = Loc.T("hud.card.need_elixir");
            else label = GameSession.Describe(check);
            bool ok = check == CommandResult.Ok;
            m_CardUpgrade.SetLabel(label);
            m_CardUpgrade.SetInteractable(ok);
            // Цена в эликсире под подписью: когда улучшение доступно или упирается в копии и эликсир; на что не хватает — красная.
            bool showPrice = hasCost && level >= 1 &&
                             (ok || check == CommandResult.NotEnoughDuplicates || check == CommandResult.NotEnoughElixir);
            Ui.SetActive(m_CardPrice.Root, showPrice);
            var labelRt = m_CardUpgrade.Label.rectTransform;
            if (showPrice)
            {
                m_CardPrice.SetText(NumberFormat.Format(elixir));
                m_CardPrice.Text.color = s.Player.Elixir >= elixir ? (ok ? PixelUi.TextColor : PixelUi.TextDim) : PixelUi.TextBad;
                m_CardPrice.Icon.color = ok ? Color.white : PixelUi.DisabledIconTint;
                labelRt.Anchor(0, 0, 1, 1, new Vector2(3 * Px, UpgradeLabelBottom), new Vector2(-3 * Px, -2 * Px));
            }
            else labelRt.Anchor(0, 0, 1, 1, new Vector2(3 * Px, 3 * Px), new Vector2(-3 * Px, -Px));
        }

        void OnCardUpgrade()
        {
            if (string.IsNullOrEmpty(m_CardShown)) return;
            m_Game.UpgradeCard(m_CardShown);
            RebuildBlueprints();
        }
    }
}
