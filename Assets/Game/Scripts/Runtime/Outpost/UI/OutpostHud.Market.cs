using System.Collections.Generic;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BigInteger = System.Numerics.BigInteger;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// «Market» (ТЗ 2.2 §6.2–§6.3, UI_API §13): крайняя правая кнопка нижней панели и окно рынка на ките (<see cref="PixelUi.Window"/>, как
    /// настройки и окно карточки; решение владельца 15.10.2026 — не на весь экран): затемнённое поле вокруг рамки, планка-заголовок
    /// «Market · Warehouse» (склад — §6.1) и крестик; строка с золотом кошелька и общей стоимостью склада («Total value»); строки —
    /// товары заводов текущей шахты по номеру типа (без повторов): иконка, имя, количество, «each N gold», сумма и «Sell»; товар с
    /// нулём — серый. Высота окна — по строкам, рамка с планкой — между верхней полосой HUD и нижней панелью кнопок (затемнённые,
    /// видны); длинный список (15 товаров шахт 130 и 140) прокручивается. Внизу рамки «Sell all +X»; недоступная — просто серая кнопка «Sell all» без причины и без тоста
    /// (решение владельца 15.10.2026). Продажа — команды ядра SellGoods(goodId) / SellGoods(null) через <see cref="IOutpostGame"/>
    /// (сразу запись); по событиям GoodsSold одной команды — тост «+X gold» и полёт монет к кошельку (в окне — к его строке золота).
    /// Недоступная «Sell» строки называет причину тапом: «Nothing to sell», «Mine complete». Окно открывает кнопка Market, тап по
    /// складу (Selection = Warehouse) и <see cref="ShowMarket"/>; Back и крестик закрывают. Цена единицы постоянна в пределах шахты и
    /// кэшируется по run; тексты пересобираются только при смене значений.
    /// </summary>
    public sealed partial class OutpostHud
    {
        public const string TargetMarket = "ui.market";
        const float MarketCountsH = 21 * Px, MarketRowH = 36 * Px, MarketRowGap = 2 * Px;
        const float MarketSellW = 40 * Px, MarketSellH = 24 * Px, MarketSumW = 46 * Px, MarketSellAllH = 30 * Px;
        /// <summary>Поле рамки слева и справа — доля ширины safe area (как у окна карточки и листа «Базы»).</summary>
        const float MarketSide = 0.03f;
        /// <summary>Зазор рамки (с планкой-заголовком) от верхней полосы HUD и от нижней панели кнопок.</summary>
        const float MarketBarGap = 4 * Px;
        /// <summary>Планка-заголовок окна кита выступает над рамкой на столько (PixelUi.Window).</summary>
        const float MarketHeaderReach = 10 * Px;
        /// <summary>Высота области списка в шахте без заводов (причина и подсказка вместо строк).</summary>
        const float MarketEmptyH = 60 * Px;
        /// <summary>Строка рынка: товар, элементы и показанные значения (кэш по значению).</summary>
        sealed class MarketRow
        {
            public int Index;
            public string GoodId;
            public RectTransform Root;
            public Image Icon;
            public TextMeshProUGUI Name, Count, Each;
            public PixelIconText Sum;
            public Ui.ButtonRefs Sell;
            /// <summary>Код причины, по которой «Sell» сейчас недоступна (тост по тапу); null — доступна.</summary>
            public string Reason;
            public long CountShown = -1;
            public BigInteger ValueShown = BigInteger.MinusOne, PriceShown = BigInteger.MinusOne;
            public int StateShown = -1;
        }

        PixelWindow m_MarketWin;
        RectTransform m_MarketRoot, m_MarketViewport, m_MarketContent;
        ScrollRect m_MarketScroll;
        // Раскладка рамки: для какого размера safe area и числа строк посчитана (пересчёт — только при смене).
        Vector2 m_MarketLayoutSize;
        int m_MarketLayoutRows = -1;
        WindowEntry m_MarketEntry;
        Ui.ButtonRefs m_MarketButton, m_SellAll;
        PixelIconText m_MarketWallet;
        RectTransform m_MarketCoin;
        TextMeshProUGUI m_MarketTotal, m_MarketEmpty;
        readonly List<MarketRow> m_MarketRows = new List<MarketRow>(20);
        int m_MarketRowCount;
        /// <summary>RunId шахты, для которой собраны строки (новая шахта, загрузка — строки и цены заново).</summary>
        string m_MarketRunId;
        OutpostRun m_MarketRun;
        BigInteger m_MarketWalletShown = BigInteger.MinusOne, m_MarketTotalShown = BigInteger.MinusOne;
        int m_MarketSellAllState = -1, m_MarketEmptyShown = -1;
        // Кнопка Market нижней панели: показанное состояние (доступна | точка) и причина недоступности для тоста.
        int m_MarketButtonShown = -1;
        string m_MarketButtonReason;
        // Продажа: события GoodsSold одной команды приходят в одном кадре — тост и монеты одним разом после короткой паузы.
        BigInteger m_SoldGold;
        int m_SoldEvents;
        float m_SoldDue;
        /// <summary>Откуда летят монеты продажи в открытом окне: нажатая «Sell» или «Sell all».</summary>
        RectTransform m_MarketFlyFrom;

        /// <summary>Окно рынка открыто.</summary>
        public bool MarketOpen => m_MarketWin != null && m_MarketWin.Visible;

        /// <summary>Строк товара в открытом окне (заводы текущей шахты без повторов товара).</summary>
        public int MarketRowCount => m_MarketRowCount;

        /// <summary>Товар строки index окна рынка (по номеру типа) или null.</summary>
        public string MarketRowGood(int index) => index >= 0 && index < m_MarketRowCount ? m_MarketRows[index].GoodId : null;

        void BuildMarketButton(RectTransform bar)
        {
            // Крайняя правая: Base · Equipment · Sell Goods (решение владельца 16.10.2026; первая сессия — Market переименован в Sell
            // Goods, иконка — ящик с товарами заводов). Красная точка — на складе есть товар; до первой шахты с заводами ящик и подпись
            // остаются читаемыми, в углу — маленький замок, тап — окно превью с шахтой первого завода (RefreshMarketButton).
            m_MarketButton = SideButton(bar, "Market", SellGoodsIcon, Loc.T("outpost.bottom.market"), true, OnMarketButton);
            m_MarketButton.Badge = PixelUi.Badge(m_MarketButton.Image.transform, "badge");
            m_MarketButton.Badge.PlaceAtCorner(new Vector2(1f, 1f), PixelUi.BadgeCornerOffset);
            m_MarketButton.Badge.SetDot(false);
            AddDeniedTap(m_MarketButton, OnMarketButtonDenied);
        }

        void BuildMarket()
        {
            // Окно кита, как настройки и окно карточки: затемнение поля (ловит касания), рамка ui_window, планка-заголовок и крестик.
            // Шапка «Market · Warehouse»: окно продаёт содержимое склада, и тап по складу во дворе открывает его (ТЗ 2.2 §6.1 —
            // склад называется в интерфейсе Warehouse). Одной строкой; на узком экране шрифт уменьшается (автоподбор кита).
            m_MarketWin = PixelUi.Window(m_Safe, "Market", Loc.F("factories.market_title", Loc.T("outpost.market.title"), Loc.T("factories.warehouse")), HideMarket);
            m_MarketRoot = m_MarketWin.Root;
            m_MarketEntry = new WindowEntry { Root = m_MarketRoot, Back = HideMarket };
            Ui.SetActive(m_MarketWin.BodyText, false);
            var body = m_MarketWin.Body;

            // «Sell all +X» — ряд кнопок внизу рамки.
            m_SellAll = m_MarketWin.AddButton("sellAll", Loc.T("outpost.market.sell_all"), PixelButton.Gold, OnSellAll, PixelUi.FontLarge);
            m_MarketWin.ButtonsHeight = MarketSellAllH;
            m_SellAll.Label.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_SellAll.Label, PixelUi.FontBody, PixelUi.FontLarge);

            // Золото кошелька слева (куда летят монеты продажи) и «Total value» — стоимость всего склада справа.
            var counts = Ui.Rect(body, "marketCounts").Anchor(0, 1, 1, 1, new Vector2(0f, -MarketCountsH), Vector2.zero);
            var walletCell = PixelUi.Panel(counts, "wallet", PixelPanel.Inset);
            walletCell.raycastTarget = false;
            walletCell.rectTransform.Anchor(0, 0, 0.44f, 1, Vector2.zero, new Vector2(-2 * Px, 0f));
            m_MarketWallet = PixelUi.IconText(walletCell.transform, "value", "icon_coin", "", PixelUi.FontLarge, PixelUi.TextColor);
            m_MarketWallet.Root.anchorMin = m_MarketWallet.Root.anchorMax = new Vector2(0f, 0.5f);
            m_MarketWallet.Root.pivot = new Vector2(0f, 0.5f);
            m_MarketWallet.Root.anchoredPosition = new Vector2(4 * Px, 0f);
            m_MarketCoin = m_MarketWallet.Icon.rectTransform;
            var totalCell = PixelUi.Panel(counts, "total", PixelPanel.Inset);
            totalCell.raycastTarget = false;
            totalCell.rectTransform.Anchor(0.44f, 0, 1, 1, new Vector2(2 * Px, 0f), Vector2.zero);
            m_MarketTotal = PixelUi.Text(totalCell.transform, "text", "", PixelUi.FontBody, PixelUi.TextAccentLight, TextAlignmentOptions.Center, false, true);
            m_MarketTotal.rectTransform.Anchor(0, 0, 1, 1, new Vector2(4 * Px, Px), new Vector2(-4 * Px, -Px));
            m_MarketTotal.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_MarketTotal, PixelUi.FontSmall, PixelUi.FontBody);

            // Строки товаров — прокрутка, как в «Cards» (15 заводов в шахтах 130 и 140), под строкой золота до ряда кнопок.
            float listTop = MarketCountsH + 3 * Px;
            m_MarketViewport = Ui.Rect(body, "viewport").Anchor(0, 0, 1, 1, Vector2.zero, new Vector2(0f, -listTop));
            m_MarketViewport.gameObject.AddComponent<RectMask2D>();
            var input = m_MarketViewport.gameObject.AddComponent<Image>();
            input.color = Color.clear;
            m_MarketContent = Ui.Rect(m_MarketViewport, "content");
            m_MarketContent.anchorMin = new Vector2(0, 1);
            m_MarketContent.anchorMax = new Vector2(1, 1);
            m_MarketContent.pivot = new Vector2(0.5f, 1);
            m_MarketContent.offsetMin = m_MarketContent.offsetMax = Vector2.zero;
            var column = m_MarketContent.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = MarketRowGap;
            column.childAlignment = TextAnchor.UpperCenter;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            m_MarketContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            m_MarketScroll = m_MarketViewport.gameObject.AddComponent<ScrollRect>();
            m_MarketScroll.content = m_MarketContent;
            m_MarketScroll.viewport = m_MarketViewport;
            m_MarketScroll.horizontal = false;
            m_MarketScroll.movementType = ScrollRect.MovementType.Clamped;
            m_MarketScroll.scrollSensitivity = 30;

            // Шахта без заводов (тап по складу в шахте 1): причина и как товар попадает на склад — на месте списка.
            m_MarketEmpty = PixelUi.Text(body, "empty", "", PixelUi.FontBody, PixelUi.TextDim, TextAlignmentOptions.Center, false, true);
            m_MarketEmpty.rectTransform.Anchor(0, 0, 1, 1, new Vector2(4 * Px, 0f), new Vector2(-4 * Px, -listTop));
            Ui.AutoSize(m_MarketEmpty, PixelUi.FontSmall, PixelUi.FontBody);
            m_MarketEmpty.gameObject.SetActive(false);
        }

        /// <summary>
        /// Рамка окна рынка: ширина — safe area без <see cref="MarketSide"/> с каждой стороны, высота — по строкам товаров (или по
        /// причине в шахте без заводов) вместе с планкой, строкой золота и рядом «Sell all». Рамка с планкой-заголовком — между
        /// верхней полосой HUD и нижней панелью кнопок (по <see cref="MarketBarGap"/> от каждой, по центру между ними; на 9:16 — до
        /// 78 % safe area), дальше список прокручивается; полосы и поле вокруг видны под затемнением. Чётное число пикселей арта.
        /// Только при смене числа строк или размера safe area (поворот, окно редактора).
        /// </summary>
        void LayoutMarket()
        {
            m_MarketLayoutSize = m_Safe.rect.size;
            m_MarketLayoutRows = m_MarketRowCount;
            float safeH = m_Safe.rect.height;
            if (safeH < 100 * Px) safeH = RefHeight;
            int n = m_MarketRowCount;
            float list = n > 0 ? n * MarketRowH + (n - 1) * MarketRowGap : MarketEmptyH;
            // Поля Body в рамке кита (PixelWindow): 14 пикселей арта сверху, снизу — ряд кнопок с отступами 11 и 4.
            float chrome = WindowTop + 11 * Px + m_MarketWin.ButtonsHeight + 4 * Px;
            float h = Mathf.Ceil((chrome + MarketCountsH + 3 * Px + list) / (2 * Px)) * 2 * Px;
            // Место между верхней полосой (с планкой над рамкой) и нижней панелью кнопок (без строки фазы над ней).
            float below = Margin + BarHeight + Margin + MarketBarGap;
            float above = TopUnits + MarketBarGap + MarketHeaderReach;
            float max = Mathf.Floor((safeH - below - above) / (2 * Px)) * 2 * Px;
            h = Mathf.Max(2 * Px, Mathf.Min(h, max));
            float y = below + Mathf.Floor((max - h) * 0.5f / Px) * Px;
            m_MarketWin.Box.Anchor(MarketSide, 0f, 1f - MarketSide, 0f, new Vector2(0f, y), new Vector2(0f, y + h));
        }

        MarketRow CreateMarketRow(int index)
        {
            var row = new MarketRow { Index = index };
            var panel = PixelUi.Panel(m_MarketContent, "good" + index, PixelPanel.Inset);
            panel.raycastTarget = false;
            row.Root = panel.rectTransform;
            row.Root.gameObject.AddComponent<LayoutElement>().preferredHeight = MarketRowH;
            row.Icon = PixelUi.Icon(panel.transform, "icon", "icon_factory", 2);
            row.Icon.rectTransform.anchorMin = row.Icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            row.Icon.rectTransform.anchoredPosition = new Vector2(3 * Px + 16 * Px, 0f);

            const float textLeft = 3 * Px + 32 * Px + 4 * Px;
            float textRight = 3 * Px + MarketSellW + 3 * Px + MarketSumW + 2 * Px;
            row.Name = PixelUi.Text(panel.transform, "name", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Left, true, true);
            row.Name.rectTransform.Anchor(0, 1, 1, 1, new Vector2(textLeft, -17 * Px), new Vector2(-textRight, -4 * Px));
            row.Name.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(row.Name, PixelUi.FontSmall, PixelUi.FontBody);
            // Количество — слева второй строки, цена единицы — справа (обе в колонке текста; на 9:20 ≈ 104 пикселя арта).
            row.Count = PixelUi.Text(panel.transform, "count", "", PixelUi.FontBody, PixelUi.TextAccentLight, TextAlignmentOptions.Left, false, true);
            row.Count.rectTransform.Anchor(0, 0, 0.5f, 0, new Vector2(textLeft, 3 * Px), new Vector2(0f, 16 * Px));
            row.Count.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(row.Count, PixelUi.FontSmall, PixelUi.FontBody);
            row.Each = PixelUi.Text(panel.transform, "each", "", PixelUi.FontSmall, PixelUi.TextDim, TextAlignmentOptions.Right, false, true);
            row.Each.rectTransform.Anchor(0, 0, 1, 0, new Vector2(textLeft, 4 * Px), new Vector2(-textRight, 14 * Px));
            row.Each.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(row.Each, PixelUi.FontSmall * 0.75f, PixelUi.FontSmall);

            row.Sum = PixelUi.IconText(panel.transform, "sum", "icon_coin", "", PixelUi.FontBody, PixelUi.TextAccentLight);
            row.Sum.Root.anchorMin = row.Sum.Root.anchorMax = new Vector2(1f, 0.5f);
            row.Sum.Root.pivot = new Vector2(1f, 0.5f);
            row.Sum.Root.anchoredPosition = new Vector2(-(3 * Px + MarketSellW + 4 * Px), 0f);

            int k = index;
            row.Sell = PixelUi.Button(panel.transform, "sell", Loc.T("outpost.market.sell"), PixelButton.Green, () => OnSell(k), PixelUi.FontBody);
            var srt = row.Sell.Rect;
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 0.5f);
            srt.pivot = new Vector2(1f, 0.5f);
            srt.sizeDelta = new Vector2(MarketSellW, MarketSellH);
            srt.anchoredPosition = new Vector2(-3 * Px, 0f);
            row.Sell.Label.textWrappingMode = TextWrappingModes.NoWrap;
            AddDeniedTap(row.Sell, () => MarketDenied(k < m_MarketRows.Count ? m_MarketRows[k].Reason : null));
            return row;
        }

        // ───────────── Открытие и закрытие ─────────────

        /// <summary>
        /// Открыть рынок (кнопка Market, тап по складу во дворе лагеря — WP5a зовёт этот метод). Открывается и в шахте без заводов
        /// (причина вместо строк) и в завершённой шахте (кнопки продажи недоступны с причиной «Mine complete»).
        /// </summary>
        public void ShowMarket()
        {
            if (m_MarketWin == null || m_Game == null || m_Game.Sim == null) return;
            bool opening = !MarketOpen;
            // Поверх соседей; звук открытия — у кита (только если окно было скрыто).
            m_MarketWin.Show();
            Opened(m_MarketEntry);
            InvalidateMarket();
            RefreshMarket();
            Canvas.ForceUpdateCanvases();
            if (opening) m_MarketScroll.verticalNormalizedPosition = 1f;
        }

        public void HideMarket()
        {
            m_MarketWin?.Hide();
            if (m_MarketEntry != null) Closed(m_MarketEntry);
        }

        /// <summary>Иконка Sell Goods: ящик, полный товаров заводов (без атласа первой сессии — прежние весы).</summary>
        static string SellGoodsIcon => PixelArt.Has("icon_sell_goods") ? "icon_sell_goods" : "icon_market";

        void OnMarketButton()
        {
            if (!CommandsAllowed(out string blocked))
            {
                Denied(blocked);
                return;
            }
            // До первой шахты с заводами — окно превью: что это и где открывается (рабочее окно пустое).
            if (MarketLockZone() > 0 && m_Game.Configs?.Progression?.Feature(ProgressionConfig.SellGoods) != null)
            {
                ShowPreview(ProgressionConfig.SellGoods);
                return;
            }
            ShowMarket();
        }

        void OnMarketButtonDenied()
        {
            int unlock = MarketLockZone();
            if (!CommandsAllowed(out string blocked)) Toast(OutpostUiText.Reason(blocked));
            else if (unlock > 0) Toast(Loc.F("outpost.market.locked", unlock));
            else Toast(FactoryText.Reason(m_MarketButtonReason ?? m_Game?.SellBlockReason(null)));
        }

        /// <summary>
        /// Шахта, с которой открывается Market, если текущая шахта раньше неё (на кнопке замок, тап называет шахту; решение
        /// владельца 16.10.2026); иначе 0 — в том числе в шахте без заводов после первой шахты с заводами («No factories in this mine»).
        /// </summary>
        int MarketLockZone()
        {
            var run = m_Game?.State?.Run;
            if (run == null) return 0;
            int unlock = m_Game.MarketUnlockZone;
            return unlock > 0 && run.Zone < unlock ? unlock : 0;
        }

        void MarketDenied(string reason)
        {
            if (!CommandsAllowed(out string blocked)) Toast(OutpostUiText.Reason(blocked));
            else if (reason != null) Toast(FactoryText.Reason(reason));
        }

        void InvalidateMarket()
        {
            m_MarketWalletShown = m_MarketTotalShown = BigInteger.MinusOne;
            m_MarketSellAllState = m_MarketEmptyShown = -1;
            for (int i = 0; i < m_MarketRows.Count; i++)
            {
                var row = m_MarketRows[i];
                row.CountShown = -1;
                row.ValueShown = BigInteger.MinusOne;
                row.StateShown = -1;
            }
        }

        // ───────────── Кнопка нижней панели ─────────────

        /// <summary>
        /// Кнопка Market (UI_API §13): неактивна при SellBlockReason(null) ∈ {no-factories, phase} и когда команды недоступны;
        /// красная точка — на складе есть товар и продать его можно; до первой шахты с заводами вместо весов замок (тап — «Market
        /// unlocks at mine N»). Без выделений: причины ядра — строковые литералы, шахта открытия считается один раз.
        /// </summary>
        void RefreshMarketButton(bool allowed)
        {
            string reason = m_Game.SellBlockReason(null);
            bool locked = reason == "no-factories" && MarketLockZone() > 0;
            // Закрытая по прогрессу кнопка нажимается (тап — превью с требованием); затемнение и замок — как у остальных разделов.
            bool on = allowed && (locked || (reason != "no-factories" && reason != "phase"));
            bool dot = on && !locked && reason == null;
            m_MarketButtonReason = reason;
            int state = (on ? 1 : 0) | (dot ? 2 : 0) | (locked ? 4 : 0);
            if (state == m_MarketButtonShown) return;
            m_MarketButtonShown = state;
            m_MarketButton.SetInteractable(on);
            m_MarketButton.Badge.SetDot(dot);
            m_MarketButton.SetIcon(SellGoodsIcon);
            SetLocked(m_MarketButton, m_MarketLock, locked, on);
        }

        // ───────────── Окно ─────────────

        /// <summary>Строки — товары заводов текущей шахты в порядке типов снимка правил (номер типа), каждый товар один раз.</summary>
        void EnsureMarketRows(OutpostRun run)
        {
            if (ReferenceEquals(run, m_MarketRun) && run.RunId == m_MarketRunId) return;
            m_MarketRun = run;
            m_MarketRunId = run.RunId;
            m_MarketRowCount = 0;
            var goods = run.FactoryRules != null ? run.FactoryRules.Goods : null;
            if (goods != null)
                for (int i = 0; i < goods.Count; i++)
                {
                    string good = goods[i]?.GoodId;
                    if (good == null || !FactoryQueries.HasGood(run, good)) continue;
                    if (m_MarketRowCount == m_MarketRows.Count) m_MarketRows.Add(CreateMarketRow(m_MarketRowCount));
                    var row = m_MarketRows[m_MarketRowCount++];
                    row.GoodId = good;
                    string icon = FactoryText.GoodIcon(good);
                    PixelUi.SetIcon(row.Icon, icon, 2);
                    row.Icon.rectTransform.anchoredPosition = new Vector2(3 * Px + 16 * Px, 0f) + OutpostDialog.CenterOffset(icon, 2);
                    Ui.SetText(row.Name, FactoryText.GoodName(good));
                    // Цена постоянна в пределах шахты (снимки баланса и правил run): считается один раз на строку.
                    row.PriceShown = m_Game.GoodsPrice(good);
                    Ui.SetText(row.Each, Loc.F("outpost.market.each", OutpostUiText.Money(row.PriceShown)));
                }
            for (int i = 0; i < m_MarketRows.Count; i++) Ui.SetActive(m_MarketRows[i].Root, i < m_MarketRowCount);
            InvalidateMarket();
        }

        /// <summary>
        /// Окно рынка по состоянию ядра (10 Гц и после команд). Без изменений — без выделений: количества и причины читаются из
        /// ядра (поиск по словарю, литералы причин), а стоимости (WarehouseValue — BigInteger-арифметика) пересчитываются, только
        /// когда изменилось количество товара; цена единицы — один раз на строку и шахту.
        /// </summary>
        void RefreshMarket()
        {
            var sim = m_Game != null ? m_Game.Sim : null;
            if (sim == null || sim.Run == null) return;
            var run = sim.Run;
            EnsureMarketRows(run);
            if (m_MarketRowCount != m_MarketLayoutRows || m_Safe.rect.size != m_MarketLayoutSize) LayoutMarket();
            bool allowed = CommandsAllowed(out string blocked);

            if (run.Wallet != m_MarketWalletShown)
            {
                m_MarketWalletShown = run.Wallet;
                m_MarketWallet.SetText(OutpostUiText.Money(run.Wallet));
            }

            int empty = m_MarketRowCount == 0 ? 1 : 0;
            if (empty != m_MarketEmptyShown)
            {
                m_MarketEmptyShown = empty;
                Ui.SetActive(m_MarketEmpty, empty == 1);
                if (empty == 1) Ui.SetText(m_MarketEmpty, FactoryText.Reason("no-factories") + "\n" + Loc.T("outpost.market.hint"));
            }

            bool countsChanged = m_MarketTotalShown.Sign < 0;
            for (int i = 0; i < m_MarketRowCount; i++)
            {
                var row = m_MarketRows[i];
                long count = m_Game.GoodsCount(row.GoodId);
                string reason = allowed ? m_Game.SellBlockReason(row.GoodId) : blocked;
                row.Reason = reason;
                int state = (count > 0 ? 1 : 0) | (reason == null ? 2 : 0);
                if (count != row.CountShown)
                {
                    countsChanged = true;
                    row.CountShown = count;
                    Ui.SetText(row.Count, Loc.F("outpost.market.count", OutpostUiText.Money(count)));
                    var value = m_Game.WarehouseValue(row.GoodId);
                    if (value != row.ValueShown)
                    {
                        row.ValueShown = value;
                        row.Sum.SetText(OutpostUiText.Money(value));
                    }
                }
                if (state == row.StateShown) continue;
                row.StateShown = state;
                bool has = count > 0;
                // Нулевой товар — серый: приглушённые иконка и подписи (ТЗ 2.2 §6.3).
                row.Icon.color = has ? Color.white : PixelUi.DisabledIconTint;
                row.Name.color = has ? PixelUi.TextColor : PixelUi.TextDim;
                row.Count.color = has ? PixelUi.TextAccentLight : PixelUi.TextDim;
                row.Sum.Text.color = has ? PixelUi.TextAccentLight : PixelUi.TextDim;
                row.Sum.Icon.color = has ? Color.white : PixelUi.DisabledIconTint;
                row.Sell.SetInteractable(reason == null);
            }

            bool totalChanged = false;
            if (countsChanged)
            {
                var total = m_Game.WarehouseValue(null);
                if (total != m_MarketTotalShown)
                {
                    totalChanged = true;
                    m_MarketTotalShown = total;
                    Ui.SetText(m_MarketTotal, Loc.F("outpost.market.total", OutpostUiText.Money(total)));
                }
            }
            // «Sell all +X». Недоступная («Nothing to sell», «Mine complete», команды закрыты) — просто серая «Sell all» кита
            // (SetInteractable(false)): без причины на кнопке и без тоста по тапу (решение владельца 15.10.2026).
            bool sellAll = allowed && m_Game.SellBlockReason(null) == null;
            int allState = sellAll ? 1 : 0;
            if (allState != m_MarketSellAllState || sellAll && totalChanged)
            {
                m_MarketSellAllState = allState;
                m_SellAll.SetInteractable(sellAll);
                m_SellAll.SetLabel(sellAll && m_MarketTotalShown.Sign > 0
                    ? Loc.F("outpost.market.sell_all_value", OutpostUiText.Money(m_MarketTotalShown))
                    : Loc.T("outpost.market.sell_all"));
            }
        }

        // ───────────── Продажа ─────────────

        void OnSell(int index)
        {
            if (index < 0 || index >= m_MarketRowCount) return;
            var row = m_MarketRows[index];
            Sell(row.GoodId, row.Sell.Rect);
        }

        void OnSellAll() => Sell(null, m_SellAll.Rect);

        /// <summary>Команда ядра SellGoods (null — «Sell all») через контроллер; тост и монеты — по событиям GoodsSold (<see cref="FlushGoodsSold"/>).</summary>
        void Sell(string goodId, RectTransform from)
        {
            if (m_Game == null) return;
            if (!CommandsAllowed(out string blocked))
            {
                Denied(blocked);
                return;
            }
            var result = m_Game.SellGoods(goodId);
            if (!result.Ok)
            {
                // Звук отказа команды играет контроллер; здесь — только причина.
                Toast(FactoryText.Reason(result.Reason));
                return;
            }
            m_MarketFlyFrom = from;
            RefreshNow();
        }

        /// <summary>Событие ядра для рынка: GoodsSold копит золото одной команды (тост и монеты — <see cref="FlushGoodsSold"/>).</summary>
        void OnMarketEvent(OutpostEvent e)
        {
            if (e.Type != OutpostEventType.GoodsSold) return;
            if (m_SoldEvents == 0) m_SoldDue = Time.unscaledTime + 0.05f;
            m_SoldEvents++;
            m_SoldGold += e.Value;
            m_NextRefresh = 0f;
        }

        /// <summary>
        /// Тост «+X gold» (сумма событий GoodsSold одной команды — «Sell all» даёт событие на товар) и полёт монет: в открытом
        /// окне — от нажатой кнопки к строке золота окна, иначе — от склада во дворе лагеря к кошельку верхней полосы.
        /// </summary>
        void FlushGoodsSold()
        {
            if (m_SoldEvents == 0 || Time.unscaledTime < m_SoldDue) return;
            Toast(Loc.F("outpost.toast.goods_sold", OutpostUiText.Money(m_SoldGold)));
            int coins = Mathf.Clamp(2 + m_SoldEvents, 3, FlyMaxPerDrop);
            if (MarketOpen && m_MarketFlyFrom != null && m_MarketFlyFrom.gameObject.activeInHierarchy)
                FlyCoinsBetween(m_MarketFlyFrom, m_MarketCoin, coins);
            else if (m_Game != null) FlyCoins(m_Game.WarehouseWorldPosition, coins);
            m_SoldEvents = 0;
            m_SoldGold = BigInteger.Zero;
        }

        /// <summary>Монеты от элемента HUD <paramref name="from"/> к иконке <paramref name="to"/> (окна HUD не мешают), «пульс» цели.</summary>
        void FlyCoinsBetween(RectTransform from, RectTransform to, int count)
        {
            if (m_FlyLayer == null || from == null || to == null) return;
            Vector2 start = m_FlyLayer.InverseTransformPoint(from.position);
            Vector2 end = m_FlyLayer.InverseTransformPoint(to.position);
            LaunchCoins(start, end, count, to);
        }
    }
}
