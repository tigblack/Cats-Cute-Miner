using System.Collections.Generic;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// «Экипировка» (ТЗ 10, 14; RUNTIME_SPEC §5): экран во весь safe area (как «Карты» старого HUD) с подписью
    /// «Сохраняется между шахтами». Счётчик деталей и «Надеть лучшее»; четыре слота с надетым предметом и его бонусом
    /// (тап — фильтр по слоту и окно предмета); фильтр «Все / Кирки / Шляпы / Рюкзаки / Обувь»; сетка коллекции из 16
    /// карточек PixelCard (рамка редкости, icon_item_*_big, уровень, отметка «надето» и красный пин доступного улучшения,
    /// без шкалы деталей; не полученный предмет — рубашкой). Окно предмета: бонус сейчас, зелёная стрелка и бонус после
    /// улучшения («Damage +8.4% ➜ +8.8%», как в окне карточки), «Equipped» или «сейчас → с этим предметом», детали,
    /// «Надеть»/«Снять», «Улучшить (N деталей)» / «Макс.». Доступно и в ReadyToContinue. Все величины — из каталога ядра.
    /// </summary>
    public sealed partial class OutpostHud
    {
        const float EqPad = 4 * Px, EqHeaderH = 30 * Px, EqPartsH = 22 * Px, EqSlotsH = 56 * Px, EqFilterH = 20 * Px, EqGap = 3 * Px;
        const float EqCellGap = 3 * Px;
        const int EqColumns = 4;
        const float ItemCardW = 72 * Px, ItemCardH = 96 * Px, ItemButtonH = 36 * Px, ItemPriceBottom = 5 * Px;

        sealed class SlotView
        {
            public Ui.ButtonRefs Button;
            public Image Frame;
            public TextMeshProUGUI Name, Bonus;
        }

        sealed class ItemCell
        {
            public EquipmentItemDef Def;
            public PixelCard Card;
            public Image Equipped;
            public PixelBadge Upgrade;
        }

        RectTransform m_EquipRoot, m_EquipViewport, m_GridContent;
        WindowEntry m_EquipEntry;
        GridLayoutGroup m_Grid;
        float m_GridWidth = -1f;
        PixelIconText m_PartsText;
        Ui.ButtonRefs m_EquipBestButton;
        readonly SlotView[] m_SlotViews = new SlotView[OutpostIds.SlotCount];
        readonly Ui.ButtonRefs[] m_FilterButtons = new Ui.ButtonRefs[OutpostIds.SlotCount + 1];
        readonly List<ItemCell> m_Cells = new List<ItemCell>(16);
        readonly string[] m_BestBefore = new string[OutpostIds.SlotCount];
        /// <summary>−1 — все слоты, иначе EquipSlot.</summary>
        int m_Filter = -1;
        bool m_EquipValid;
        int m_EquipSigVersion = int.MinValue, m_EquipSigAllowed = -1, m_EquipSigItems = -1;
        long m_EquipSigParts = long.MinValue;

        // Окно предмета.
        PixelWindow m_ItemWin;
        WindowEntry m_ItemEntry;
        PixelCard m_ItemCard;
        TextMeshProUGUI m_ItemInfo, m_ItemNote, m_ItemBonusNow, m_ItemBonusNext;
        RectTransform m_ItemBonusRow, m_ItemBonusArrow;
        Ui.ButtonRefs m_ItemEquip, m_ItemUpgrade;
        PixelIconText m_ItemPrice;
        string m_ItemShown, m_ItemEquipReason, m_ItemUpgradeReason;
        bool m_ItemValid;

        /// <summary>Экран «Экипировка» открыт (с окном предмета или без).</summary>
        public bool EquipmentOpen => m_EquipRoot != null && m_EquipRoot.gameObject.activeSelf;

        /// <summary>Окно предмета открыто поверх «Экипировки».</summary>
        public bool ItemDetailOpen => m_ItemWin != null && m_ItemWin.Visible && EquipmentOpen;

        /// <summary>ID предмета в окне предмета или null.</summary>
        public string ItemDetailId => ItemDetailOpen ? m_ItemShown : null;

        void BuildEquipment()
        {
            m_EquipRoot = Ui.Rect(m_Safe, "Equipment").Anchor(0, 0, 1, 1);
            var bg = m_EquipRoot.gameObject.AddComponent<Image>();
            bg.color = PixelPalette.Navy1;
            bg.raycastTarget = true;
            m_EquipRoot.gameObject.AddComponent<PixelPop>();
            m_EquipEntry = new WindowEntry { Root = m_EquipRoot, Back = HideEquipment };

            // Шапка: заголовок, «Сохраняется между шахтами», закрытие.
            float y = 2 * Px;
            var header = PixelUi.Panel(m_EquipRoot, "header", PixelPanel.Wood).rectTransform;
            header.Anchor(0, 1, 1, 1, new Vector2(EqPad, -(y + EqHeaderH)), new Vector2(-EqPad, -y));
            var title = PixelUi.Text(header, "title", Loc.T("outpost.equip.title"), PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            title.rectTransform.Anchor(0, 0.42f, 1, 1, new Vector2(30 * Px, 0f), new Vector2(-30 * Px, -3 * Px));
            title.textWrappingMode = TextWrappingModes.NoWrap;
            var subtitle = PixelUi.Text(header, "scope", Loc.T("outpost.equip.scope"), PixelUi.FontSmall, PixelUi.TextAccentLight, TextAlignmentOptions.Center, false, true);
            subtitle.rectTransform.Anchor(0, 0, 1, 0.42f, new Vector2(30 * Px, 4 * Px), new Vector2(-30 * Px, 0f));
            subtitle.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(subtitle, PixelUi.FontSmall * 0.7f, PixelUi.FontSmall);
            var close = PixelUi.Button(header, "close", "", PixelButton.Red, HideEquipment, PixelUi.FontBody);
            PinSquare(close.Rect, 1f, 22 * Px, 4 * Px);
            close.SetIcon("icon_close");
            y += EqHeaderH + EqGap;

            // Детали и «Надеть лучшее».
            var partsRow = Ui.Rect(m_EquipRoot, "partsRow").Anchor(0, 1, 1, 1, new Vector2(EqPad, -(y + EqPartsH)), new Vector2(-EqPad, -y));
            m_PartsText = PixelUi.IconText(partsRow, "parts", "icon_parts", "", PixelUi.FontLarge, PixelUi.TextColor);
            m_PartsText.Root.anchorMin = m_PartsText.Root.anchorMax = new Vector2(0f, 0.5f);
            m_PartsText.Root.pivot = new Vector2(0f, 0.5f);
            m_PartsText.Root.anchoredPosition = new Vector2(2 * Px, 0f);
            m_EquipBestButton = PixelUi.Button(partsRow, "best", Loc.T("outpost.equip.best"), PixelButton.Gold, OnEquipBest, PixelUi.FontBody);
            m_EquipBestButton.Rect.Anchor(0.5f, 0f, 1f, 1f);
            m_EquipBestButton.Label.textWrappingMode = TextWrappingModes.NoWrap;
            y += EqPartsH + EqGap;

            // Слоты надетого комплекта.
            var slots = Ui.Rect(m_EquipRoot, "slots").Anchor(0, 1, 1, 1, new Vector2(EqPad, -(y + EqSlotsH)), new Vector2(-EqPad, -y));
            for (int s = 0; s < OutpostIds.SlotCount; s++) m_SlotViews[s] = BuildSlot(slots, s);
            y += EqSlotsH + EqGap;

            // Фильтр коллекции по слоту.
            var filters = Ui.Rect(m_EquipRoot, "filters").Anchor(0, 1, 1, 1, new Vector2(EqPad, -(y + EqFilterH)), new Vector2(-EqPad, -y));
            int nf = m_FilterButtons.Length;
            for (int i = 0; i < nf; i++)
            {
                int filter = i - 1;
                string label = filter < 0 ? Loc.T("outpost.equip.filter.all") : OutpostUiText.FilterName((EquipSlot)filter);
                var b = PixelUi.Button(filters, "filter" + i, label, PixelButton.Dark, () => SetFilter(filter), PixelUi.FontBody);
                b.Rect.Anchor((float)i / nf, 0f, (float)(i + 1) / nf, 1f, new Vector2(i == 0 ? 0f : Px, 0f), new Vector2(i == nf - 1 ? 0f : -Px, 0f));
                b.Label.textWrappingMode = TextWrappingModes.NoWrap;
                Ui.AutoSize(b.Label, PixelUi.FontSmall * 0.7f, PixelUi.FontBody);
                m_FilterButtons[i] = b;
            }
            ApplyFilterStyles();
            y += EqFilterH + EqGap;

            // Коллекция в прокрутке (как сетка «Карт»: прозрачная подложка ловит жест между карточками).
            m_EquipViewport = Ui.Rect(m_EquipRoot, "viewport").Anchor(0, 0, 1, 1, new Vector2(EqPad, 2 * Px), new Vector2(-EqPad, -y));
            m_EquipViewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = m_EquipViewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0f);
            m_GridContent = Ui.Rect(m_EquipViewport, "content");
            m_GridContent.anchorMin = new Vector2(0, 1);
            m_GridContent.anchorMax = new Vector2(1, 1);
            m_GridContent.pivot = new Vector2(0.5f, 1);
            m_GridContent.offsetMin = Vector2.zero;
            m_GridContent.offsetMax = Vector2.zero;
            m_Grid = m_GridContent.gameObject.AddComponent<GridLayoutGroup>();
            m_Grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            m_Grid.constraintCount = EqColumns;
            m_Grid.spacing = new Vector2(EqCellGap, EqCellGap);
            m_Grid.childAlignment = TextAnchor.UpperCenter;
            m_Grid.padding = new RectOffset(0, 0, (int)(2 * Px), (int)(8 * Px));
            m_Grid.cellSize = new Vector2(56 * Px, 76 * Px);
            var fitter = m_GridContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = m_EquipViewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = m_GridContent;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;

            BuildItemWindow();
            m_EquipRoot.gameObject.SetActive(false);
        }

        SlotView BuildSlot(RectTransform parent, int slot)
        {
            var view = new SlotView();
            float x0 = (float)slot / OutpostIds.SlotCount, x1 = (float)(slot + 1) / OutpostIds.SlotCount;
            var cell = Ui.Rect(parent, "slot" + slot).Anchor(x0, 0, x1, 1, new Vector2(Px, 0f), new Vector2(-Px, 0f));
            var s = (EquipSlot)slot;
            view.Button = PixelUi.IconButton(cell, "button", OutpostUiText.EmptySlotIcon(s), () => OnSlotTap(slot), 2);
            var rt = view.Button.Rect;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(PixelUi.SlotSize, PixelUi.SlotSize);
            rt.anchoredPosition = Vector2.zero;
            // Рамка редкости под иконкой надетого предмета (первый ребёнок Content: иконка рисуется поверх).
            view.Frame = PixelUi.Sliced(view.Button.Content, "frame", PixelUi.CardFrameSprite(PixelRarity.Common));
            view.Frame.raycastTarget = false;
            view.Frame.rectTransform.Anchor(0, 0, 1, 1, new Vector2(3 * Px, 4 * Px), new Vector2(-3 * Px, -3 * Px));
            view.Frame.rectTransform.SetAsFirstSibling();
            view.Frame.gameObject.SetActive(false);
            view.Name = PixelUi.Text(cell, "name", OutpostUiText.SlotName(s), PixelUi.FontSmall, PixelUi.TextDim, TextAlignmentOptions.Center, false, true);
            view.Name.rectTransform.Anchor(0, 0, 1, 0, new Vector2(0f, 7 * Px), new Vector2(0f, 14 * Px));
            view.Name.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(view.Name, PixelUi.FontSmall * 0.7f, PixelUi.FontSmall);
            view.Bonus = PixelUi.Text(cell, "bonus", "", PixelUi.FontSmall, PixelUi.TextAccentLight, TextAlignmentOptions.Center, false, true);
            view.Bonus.rectTransform.Anchor(0, 0, 1, 0, new Vector2(0f, 0f), new Vector2(0f, 7 * Px));
            view.Bonus.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(view.Bonus, PixelUi.FontSmall * 0.6f, PixelUi.FontSmall);
            return view;
        }

        void BuildItemWindow()
        {
            m_ItemWin = PixelUi.Window(m_EquipRoot, "ItemDetail", "", HideItem);
            m_ItemEntry = new WindowEntry { Root = m_ItemWin.Root, Back = HideItem };
            float h = Mathf.Ceil((WindowTop + ItemCardH + 4 * Px + 11 * Px + ItemButtonH + 2 * Px) / (2 * Px)) * 2 * Px;
            m_ItemWin.Box.Anchor(0.04f, 0.5f, 0.96f, 0.5f, new Vector2(0f, -h / 2), new Vector2(0f, h / 2));
            Ui.SetActive(m_ItemWin.BodyText, false);
            var body = m_ItemWin.Body;
            m_ItemCard = PixelUi.Card(body, "card", PixelRarity.Common, "icon_item_pickaxe_common_big");
            var crt = m_ItemCard.Rect;
            crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.sizeDelta = new Vector2(ItemCardW, ItemCardH);
            crt.anchoredPosition = Vector2.zero;
            // Справа от карточки — столбец: имя и «редкость · уровень»; строка бонуса «сейчас ➜ после улучшения» с зелёной
            // стрелкой icon_upgrade (как в окне карточки); «Equipped» или сравнение с надетым и детали. Высоты — по тексту.
            var info = Ui.Rect(body, "info").Anchor(0, 0, 1, 1, new Vector2(ItemCardW + 6 * Px, 0f), Vector2.zero);
            var column = info.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = 2 * Px;
            column.childAlignment = TextAnchor.UpperLeft;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            m_ItemInfo = PixelUi.Text(info, "head", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.TopLeft, false, true);
            m_ItemBonusRow = Ui.Rect(info, "bonus");
            var row = m_ItemBonusRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 2 * Px;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            m_ItemBonusNow = ItemBonusText(m_ItemBonusRow, "now", PixelUi.TextColor);
            // Стрелка — в обёртке фиксированного размера: группа управляет обёрткой, пиксельный размер иконки не меняется.
            var arrowSize = PixelUi.IconSize("icon_upgrade");
            m_ItemBonusArrow = Ui.Rect(m_ItemBonusRow, "arrowBox");
            var arrowLayout = m_ItemBonusArrow.gameObject.AddComponent<LayoutElement>();
            arrowLayout.preferredWidth = arrowLayout.minWidth = arrowSize.x;
            arrowLayout.preferredHeight = arrowLayout.minHeight = arrowSize.y;
            var arrow = PixelUi.Icon(m_ItemBonusArrow, "nextLevelArrow", "icon_upgrade");
            arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, -90);
            m_ItemBonusNext = ItemBonusText(m_ItemBonusRow, "next", PixelPalette.Green6);
            m_ItemNote = PixelUi.Text(info, "note", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.TopLeft, false, true);

            m_ItemEquip = m_ItemWin.AddButton("equip", Loc.T("outpost.item.equip"), PixelButton.Blue, OnItemEquip, PixelUi.FontBody);
            m_ItemUpgrade = m_ItemWin.AddButton("upgrade", Loc.T("outpost.item.upgrade"), PixelButton.Green, OnItemUpgrade, PixelUi.FontBody);
            // Кнопки выше обычной: цена в деталях — под подписью «Улучшить».
            var buttons = m_ItemWin.Buttons;
            buttons.offsetMax = new Vector2(buttons.offsetMax.x, buttons.offsetMin.y + ItemButtonH);
            var bodyRt = m_ItemWin.Body;
            bodyRt.offsetMin = new Vector2(bodyRt.offsetMin.x, buttons.offsetMin.y + ItemButtonH + 4 * Px);
            m_ItemUpgrade.Label.enableAutoSizing = false;
            m_ItemUpgrade.Label.rectTransform.Anchor(0, 0.45f, 1, 1, new Vector2(4 * Px, 0f), new Vector2(-4 * Px, -2 * Px));
            m_ItemPrice = PixelUi.IconText(m_ItemUpgrade.Content, "price", "icon_parts", "", PixelUi.FontBody, PixelUi.TextColor);
            m_ItemPrice.Root.anchorMin = m_ItemPrice.Root.anchorMax = new Vector2(0.5f, 0f);
            m_ItemPrice.Root.pivot = new Vector2(0.5f, 0f);
            m_ItemPrice.Root.anchoredPosition = new Vector2(0f, ItemPriceBottom);
            AddDeniedTap(m_ItemEquip, () => Toast(OutpostUiText.Reason(m_ItemEquipReason)));
            AddDeniedTap(m_ItemUpgrade, () => Toast(OutpostUiText.Reason(m_ItemUpgradeReason)));
        }

        /// <summary>Величина в строке бонуса окна предмета: одна строка, ширина по тексту.</summary>
        static TextMeshProUGUI ItemBonusText(Transform row, string name, Color color)
        {
            var text = PixelUi.Text(row, name, "", PixelUi.FontBody, color, TextAlignmentOptions.Left, false, true);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        /// <summary>Квадратная кнопка у левого (x = 0) или правого (x = 1) края родителя, по центру высоты.</summary>
        static void PinSquare(RectTransform rt, float x, float size, float inset = 0f)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(x, 0.5f);
            rt.pivot = new Vector2(x, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(x < 0.5f ? inset : -inset, 0f);
        }

        void InvalidateEquipment()
        {
            m_EquipValid = false;
            m_ItemValid = false;
        }

        /// <summary>Открыть «Экипировку» (можно и в ReadyToContinue, и после завершения кампании).</summary>
        public void ShowEquipment()
        {
            if (m_EquipRoot == null || EquipmentOpen) return;
            if (!FeatureOpen(ProgressionConfig.Equipment)) { ShowPreview(ProgressionConfig.Equipment); return; }
            // Сначала показ, затем карточки: тексты TMP создаются в активной иерархии.
            m_EquipRoot.SetAsLastSibling();
            m_EquipRoot.gameObject.SetActive(true);
            UiSound.PlayOpen();
            Opened(m_EquipEntry);
            InvalidateEquipment();
            EnsureCells();
            RefreshEquipment();
        }

        public void HideEquipment()
        {
            if (m_EquipRoot == null) return;
            if (m_ItemWin != null && m_ItemWin.Visible) HideItem();
            if (m_EquipRoot.gameObject.activeSelf)
            {
                m_EquipRoot.gameObject.SetActive(false);
                UiSound.PlayClose();
            }
            Closed(m_EquipEntry);
        }

        void EnsureCells()
        {
            var catalog = m_Game != null && m_Game.Configs != null ? m_Game.Configs.Equipment : null;
            if (catalog == null || m_Cells.Count > 0) return;
            var defs = catalog.Defs;
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                string id = def.Id;
                var card = PixelUi.Card(m_GridContent, "item" + i, OutpostUiText.Frame(def.Rarity), OutpostUiText.ItemIcon(def, true), () => ShowItem(id));
                card.SetArt(OutpostUiText.ItemIcon(def, true), 1);
                card.SetTitle(OutpostUiText.RarityName(def.Rarity));
                card.Title.rectTransform.Anchor(0, 0.2f, 1, 0.38f, new Vector2(4 * Px, 0f), new Vector2(-4 * Px, 0f));
                card.SetProgress(-1f);
                var check = PixelUi.Icon(card.Frame.transform, "equipped", "icon_check");
                check.rectTransform.anchorMin = check.rectTransform.anchorMax = new Vector2(1f, 0f);
                check.rectTransform.anchoredPosition = new Vector2(-8 * Px, 8 * Px);
                check.gameObject.SetActive(false);
                var up = PixelUi.Badge(card.Frame.transform, "upgrade");
                up.PlaceAtCorner(new Vector2(1f, 1f), 5 * Px);
                up.SetDot(false);
                m_Cells.Add(new ItemCell { Def = def, Card = card, Equipped = check, Upgrade = up });
            }
        }

        void FitCells()
        {
            float w = m_EquipViewport.rect.width;
            if (w < 100 * Px) w = RefWidth - 2 * EqPad;
            if (Mathf.Abs(w - m_GridWidth) < 0.5f) return;
            m_GridWidth = w;
            float cell = Mathf.Floor((w - (EqColumns - 1) * EqCellGap) / EqColumns / Px) * Px;
            cell = Mathf.Max(40 * Px, cell);
            m_Grid.cellSize = new Vector2(cell, PixelUi.Snap(cell * 4f / 3f));
        }

        void ApplyFilterStyles()
        {
            for (int i = 0; i < m_FilterButtons.Length; i++)
            {
                var style = i - 1 == m_Filter ? PixelButton.Blue : PixelButton.Dark;
                if (m_FilterButtons[i].Style != style) m_FilterButtons[i].SetStyle(style);
            }
        }

        void SetFilter(int filter)
        {
            if (filter == m_Filter) return;
            m_Filter = filter;
            ApplyFilterStyles();
            for (int i = 0; i < m_Cells.Count; i++)
                Ui.SetActive(m_Cells[i].Card.Frame, m_Filter < 0 || (int)m_Cells[i].Def.Slot == m_Filter);
        }

        void OnSlotTap(int slot)
        {
            SetFilter(slot);
            var p = m_Game != null && m_Game.State != null ? m_Game.State.Profile : null;
            string id = p != null && p.Equipped != null && p.Equipped.Length > slot ? p.Equipped[slot] : null;
            if (id != null) ShowItem(id);
        }

        /// <summary>Пересобрать экран, только если изменились версия экипировки, детали, коллекция или доступность команд.</summary>
        void RefreshEquipment()
        {
            var state = m_Game != null ? m_Game.State : null;
            var catalog = m_Game != null && m_Game.Configs != null ? m_Game.Configs.Equipment : null;
            if (state == null || state.Profile == null || catalog == null) return;
            EnsureCells();
            FitCells();
            var p = state.Profile;
            int allowed = CommandsAllowed(out _) ? 1 : 0;
            int items = p.Items != null ? p.Items.Count : 0;
            if (m_EquipValid && m_EquipSigVersion == m_EquipVersion && m_EquipSigParts == p.Parts && m_EquipSigItems == items && m_EquipSigAllowed == allowed)
            {
                if (ItemDetailOpen && !m_ItemValid) RefreshItem();
                return;
            }
            m_EquipValid = true;
            m_EquipSigVersion = m_EquipVersion;
            m_EquipSigParts = p.Parts;
            m_EquipSigItems = items;
            m_EquipSigAllowed = allowed;

            m_PartsText.SetText(OutpostUiText.Parts(p.Parts));
            m_EquipBestButton.SetInteractable(allowed == 1 && items > 0);

            for (int s = 0; s < OutpostIds.SlotCount; s++)
            {
                var view = m_SlotViews[s];
                string id = p.Equipped != null && p.Equipped.Length > s ? p.Equipped[s] : null;
                bool has = id != null && catalog.TryGet(id, out var def) && p.ItemLevel(id) >= 1;
                if (has)
                {
                    catalog.TryGet(id, out def);
                    PixelUi.SetIcon(view.Button.Icon, OutpostUiText.ItemIcon(def, true), 1);
                    PixelUi.SetSliced(view.Frame, PixelUi.CardFrameSprite(OutpostUiText.Frame(def.Rarity)));
                    Ui.SetActive(view.Frame, true);
                    Ui.SetText(view.Bonus, OutpostUiText.ItemBonus(catalog, def, p.ItemLevel(id)));
                }
                else
                {
                    PixelUi.SetIcon(view.Button.Icon, OutpostUiText.EmptySlotIcon((EquipSlot)s), 2);
                    Ui.SetActive(view.Frame, false);
                    Ui.SetText(view.Bonus, EmptySlotHint(p, catalog, (EquipSlot)s));
                }
                view.Button.Icon.color = has ? Color.white : PixelUi.DisabledIconTint;
            }

            for (int i = 0; i < m_Cells.Count; i++)
            {
                var c = m_Cells[i];
                var def = c.Def;
                int q = p.ItemLevel(def.Id);
                bool owned = q >= 1;
                c.Card.SetFaceDown(!owned);
                Ui.SetActive(c.Card.Frame, m_Filter < 0 || (int)def.Slot == m_Filter);
                bool equipped = owned && p.Equipped != null && p.Equipped[(int)def.Slot] == def.Id;
                Ui.SetActive(c.Equipped, equipped);
                if (!owned)
                {
                    c.Upgrade.SetDot(false);
                    continue;
                }
                c.Card.SetLevel(Loc.F("outpost.equip.level", q));
                var cost = EquipmentService.UpgradeCost(p, catalog, def.Id);
                bool canUp = allowed == 1 && cost.HasValue && p.Parts >= cost.Value;
                c.Upgrade.SetDot(canUp);
            }
            m_ItemValid = false;
            if (ItemDetailOpen) RefreshItem();
        }

        /// <summary>
        /// Пустой слот — откуда придёт предмет: ближайшая гарантированная награда ещё не пройденной шахты для этого слота
        /// («Mine 4 reward», stage_rewards.json → Guaranteed), иначе случайные награды шахт.
        /// </summary>
        string EmptySlotHint(OutpostProfile p, EquipmentCatalog catalog, EquipSlot slot)
        {
            // Предмет для слота уже получен — его надевают из коллекции ниже.
            if (p.Items != null)
                foreach (var kv in p.Items)
                    if (kv.Value >= 1 && catalog.TryGet(kv.Key, out var owned) && owned.Slot == slot) return Loc.T("outpost.equip.slot_owned");
            var rewards = m_Game.Configs?.Rewards;
            int best = int.MaxValue;
            if (rewards?.Guaranteed != null)
                foreach (var kv in rewards.Guaranteed)
                {
                    if (!int.TryParse(kv.Key, out int zone) || zone >= best || (p.CompletedZones != null && p.CompletedZones.Contains(zone))) continue;
                    if (catalog.TryGet(kv.Value, out var def) && def.Slot == slot) best = zone;
                }
            return best != int.MaxValue ? Loc.F("outpost.equip.slot_reward", best) : Loc.T("outpost.equip.slot_drops");
        }

        /// <summary>Окно предмета поверх «Экипировки» (тап по карточке или слоту).</summary>
        public void ShowItem(string itemId)
        {
            var catalog = m_Game != null && m_Game.Configs != null ? m_Game.Configs.Equipment : null;
            if (catalog == null || !catalog.TryGet(itemId, out _)) return;
            if (!FeatureOpen(ProgressionConfig.Equipment)) { ShowPreview(ProgressionConfig.Equipment); return; }
            if (!EquipmentOpen) ShowEquipment();
            m_ItemShown = itemId;
            m_ItemValid = false;
            RefreshItem();
            m_ItemWin.Show();
            Opened(m_ItemEntry);
        }

        public void HideItem()
        {
            if (m_ItemWin == null) return;
            m_ItemWin.Hide();
            Closed(m_ItemEntry);
        }

        void RefreshItem()
        {
            var state = m_Game != null ? m_Game.State : null;
            var catalog = m_Game != null && m_Game.Configs != null ? m_Game.Configs.Equipment : null;
            if (state == null || state.Profile == null || catalog == null || m_ItemShown == null || !catalog.TryGet(m_ItemShown, out var def)) return;
            m_ItemValid = true;
            var p = state.Profile;
            int q = p.ItemLevel(def.Id), max = catalog.MaxItemLevel;
            bool owned = q >= 1;
            string current = p.Equipped != null && p.Equipped.Length > (int)def.Slot ? p.Equipped[(int)def.Slot] : null;
            bool equipped = owned && current == def.Id;
            bool allowed = CommandsAllowed(out string blocked);

            m_ItemWin.SetTitle(OutpostUiText.SlotName(def.Slot));
            m_ItemCard.SetRarity(OutpostUiText.Frame(def.Rarity));
            m_ItemCard.SetArt(OutpostUiText.ItemIcon(def, true), 2);
            m_ItemCard.SetTitle(OutpostUiText.RarityName(def.Rarity));
            m_ItemCard.SetLevel(owned ? Loc.F("outpost.equip.level", q) : "");
            m_ItemCard.SetProgress(-1f);
            m_ItemCard.SetFaceDown(!owned);

            long? cost = owned ? EquipmentService.UpgradeCost(p, catalog, def.Id) : null;
            var sb = m_Sb.Clear();
            sb.Append(PixelUi.AccentTag).Append(OutpostUiText.ItemName(def)).Append(ColorClose);
            if (owned) sb.Append('\n').Append(Loc.F("outpost.item.rarity_level", OutpostUiText.RarityName(def.Rarity), q, max));
            Ui.SetText(m_ItemInfo, sb.ToString());

            // Бонус сейчас ➜ после улучшения (зелёным); на максимуме — только бонус сейчас, без стрелки.
            Ui.SetActive(m_ItemBonusRow, owned);
            Ui.SetActive(m_ItemBonusArrow, cost.HasValue);
            Ui.SetActive(m_ItemBonusNext, cost.HasValue);
            if (owned) Ui.SetText(m_ItemBonusNow, OutpostUiText.ItemBonus(catalog, def, q));
            if (cost.HasValue) Ui.SetText(m_ItemBonusNext, OutpostUiText.ItemBonusValue(catalog, def, q + 1));

            sb = m_Sb.Clear();
            if (!owned) sb.Append(Loc.T("outpost.item.not_owned"));
            else
            {
                if (equipped) sb.Append(PixelUi.DimTag).Append(Loc.T("outpost.item.equipped")).Append(ColorClose).Append('\n');
                else
                {
                    // «Сейчас → с этим предметом»: бонус надетого в этом слоте против этого предмета (ТЗ 10.3).
                    string now = current != null && catalog.TryGet(current, out var cd) && p.ItemLevel(current) >= 1
                        ? OutpostUiText.ItemBonus(catalog, cd, p.ItemLevel(current))
                        : Loc.T("outpost.bonus.none");
                    sb.Append(Loc.F("outpost.item.compare", now, PixelUi.AccentTag + OutpostUiText.ItemBonusValue(catalog, def, q) + ColorClose)).Append('\n');
                }
                if (cost.HasValue)
                {
                    string have = p.Parts >= cost.Value ? p.Parts.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : BadTag + p.Parts.ToString(System.Globalization.CultureInfo.InvariantCulture) + ColorClose;
                    sb.Append(Loc.F("outpost.item.parts", have, cost.Value));
                }
                else sb.Append(Loc.T("outpost.item.max"));
            }
            if (owned)
            {
                sb.Append('\n').Append(Loc.T("outpost.equip.team"));
                if (def.Slot == EquipSlot.Pickaxe && m_Game.Sim != null)
                {
                    var preview = EquipDamagePreview(m_Game.Sim, def);
                    sb.Append('\n').Append(Loc.F("outpost.equip.damage_compare", preview.Before, preview.After));
                }
            }
            Ui.SetText(m_ItemNote, sb.ToString());

            // «Надеть» / «Снять».
            m_ItemEquip.SetLabel(equipped ? Loc.T("outpost.item.unequip") : Loc.T("outpost.item.equip"));
            var equipStyle = equipped ? PixelButton.Dark : PixelButton.Blue;
            if (m_ItemEquip.Style != equipStyle) m_ItemEquip.SetStyle(equipStyle);
            m_ItemEquipReason = !owned ? "not-owned" : !allowed ? blocked : null;
            m_ItemEquip.SetInteractable(m_ItemEquipReason == null);

            // «Улучшить (N деталей)» / «Макс.».
            if (!owned)
            {
                m_ItemUpgrade.SetLabel(Loc.T("outpost.item.upgrade"));
                Ui.SetActive(m_ItemPrice.Root, false);
                m_ItemUpgradeReason = "not-owned";
            }
            else if (!cost.HasValue)
            {
                m_ItemUpgrade.SetLabel(Loc.T("outpost.item.upgrade_max"));
                Ui.SetActive(m_ItemPrice.Root, false);
                m_ItemUpgradeReason = "max";
            }
            else
            {
                m_ItemUpgrade.SetLabel(Loc.T("outpost.item.upgrade"));
                Ui.SetActive(m_ItemPrice.Root, true);
                m_ItemPrice.SetText(OutpostUiText.Parts(cost.Value));
                bool enough = p.Parts >= cost.Value;
                m_ItemPrice.Text.color = enough ? PixelUi.TextColor : PixelUi.TextBad;
                m_ItemUpgradeReason = !allowed ? blocked : enough ? null : "parts";
            }
            m_ItemUpgrade.SetInteractable(m_ItemUpgradeReason == null);
        }

        /// <summary>Actual current damage and the same team's damage after this pickaxe, including cards/local ranks.</summary>
        (string Before, string After) EquipDamagePreview(OutpostSimulation sim, EquipmentItemDef item)
        {
            var stats = sim.Stats;
            var bonus = m_Game.Configs.Equipment.EffectValue(item, sim.Profile.ItemLevel(item.Id));
            var after = stats.FinalDamage / (Rational.One + stats.PickaxeBonus) * (Rational.One + bonus);
            return (OutpostUiText.Num(stats.FinalDamage), OutpostUiText.Num(after));
        }

        void OnItemEquip()
        {
            var catalog = m_Game != null && m_Game.Configs != null ? m_Game.Configs.Equipment : null;
            if (catalog == null || m_ItemShown == null || !catalog.TryGet(m_ItemShown, out var def)) return;
            if (!CommandsAllowed(out string blocked))
            {
                Denied(blocked);
                return;
            }
            var p = m_Game.State.Profile;
            bool equipped = p.Equipped != null && p.Equipped[(int)def.Slot] == def.Id;
            var result = equipped ? m_Game.Unequip(def.Slot) : m_Game.Equip(def.Id);
            if (!result.Ok) Denied(result.Reason);
            else if (!equipped) Toast(Loc.F("outpost.toast.equipped", OutpostUiText.ItemName(def)));
            m_EquipVersion++;
            InvalidateEquipment();
            InvalidateBase();
            RefreshNow();
        }

        void OnItemUpgrade()
        {
            var catalog = m_Game != null && m_Game.Configs != null ? m_Game.Configs.Equipment : null;
            if (catalog == null || m_ItemShown == null || !catalog.TryGet(m_ItemShown, out var def)) return;
            if (!CommandsAllowed(out string blocked))
            {
                Denied(blocked);
                return;
            }
            var result = m_Game.UpgradeItem(def.Id);
            if (!result.Ok) Denied(result.Reason);
            else Toast(Loc.F("outpost.toast.upgraded", OutpostUiText.ItemName(def), m_Game.State.Profile.ItemLevel(def.Id)));
            m_EquipVersion++;
            InvalidateEquipment();
            InvalidateBase();
            RefreshNow();
        }

        void OnEquipBest()
        {
            if (!CommandsAllowed(out string blocked))
            {
                Denied(blocked);
                return;
            }
            var p = m_Game.State.Profile;
            for (int s = 0; s < m_BestBefore.Length; s++) m_BestBefore[s] = p.Equipped != null && p.Equipped.Length > s ? p.Equipped[s] : null;
            var result = m_Game.EquipBest();
            if (!result.Ok)
            {
                Denied(result.Reason);
                return;
            }
            bool changed = false;
            for (int s = 0; s < m_BestBefore.Length; s++)
                if (!string.Equals(m_BestBefore[s], p.Equipped != null && p.Equipped.Length > s ? p.Equipped[s] : null, System.StringComparison.Ordinal)) changed = true;
            Toast(changed ? Loc.T("outpost.toast.best_done") : Loc.T("outpost.toast.best_equipped"));
            m_EquipVersion++;
            InvalidateEquipment();
            InvalidateBase();
            RefreshNow();
        }
    }
}
