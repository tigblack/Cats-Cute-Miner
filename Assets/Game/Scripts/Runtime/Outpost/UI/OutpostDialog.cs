using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Окно-диалог HUD нового режима на ките (PixelUi.Window): крупная иконка ×2, текст, строки «иконка + текст» на панелях
    /// Inset, необязательный блок <see cref="Extra"/> (шкала расчёта офлайна) и ряд кнопок с именем «btn» (их ищут тесты).
    /// Высота рамки — по содержимому, не выше 86 % safe area (как модальное окно старого HUD, HudWindows.LayoutModal):
    /// не помещается — строки сжимаются до 12 пикселей арта, текст уменьшается автоподбором. Элементы создаются один раз
    /// и переиспользуются; раскладка — при показе и при смене размера safe area (<see cref="Relayout"/>), не каждый кадр.
    /// </summary>
    public sealed class OutpostDialog
    {
        const float Px = PixelUi.Px;
        const float RefWidth = 1080f, RefHeight = 1920f;
        const float RowHeight = 18 * Px, RowGap = 2 * Px;
        const float WindowTop = 14 * Px, WindowBottom = 12 * Px;

        struct Row
        {
            public string Icon;
            public string Text;
            public Color Color;
        }

        sealed class RowView
        {
            public Image Panel, Icon;
            public TextMeshProUGUI Text;
        }

        readonly RectTransform m_Safe;
        readonly Image m_Icon;
        readonly RectTransform m_Rows;
        readonly List<Row> m_RowData = new List<Row>(8);
        readonly List<RowView> m_RowViews = new List<RowView>(8);
        readonly List<Ui.ButtonRefs> m_Buttons = new List<Ui.ButtonRefs>(3);
        readonly List<Action> m_Actions = new List<Action>(3);
        RectTransform m_Extra;
        float m_ExtraHeight;
        string m_IconName;
        string m_Body;
        Vector2 m_LayoutSize;

        /// <summary>Окно кита: Root (затемнение, ловит лучи), Box, Title, Body, Buttons, Close.</summary>
        public readonly PixelWindow Window;

        OutpostDialog(RectTransform safe, PixelWindow window)
        {
            m_Safe = safe;
            Window = window;
            m_Icon = PixelUi.Icon(window.Body, "icon", "icon_star", 2);
            m_Icon.rectTransform.anchorMin = m_Icon.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            m_Icon.gameObject.SetActive(false);
            m_Rows = Ui.Rect(window.Body, "rows").Anchor(0, 1, 1, 1);
        }

        /// <summary>Диалог под <paramref name="safe"/>; <paramref name="onClose"/> != null — кнопка закрытия в углу рамки.</summary>
        public static OutpostDialog Create(RectTransform safe, string name, UnityAction onClose = null) =>
            new OutpostDialog(safe, PixelUi.Window(safe, name, "", onClose));

        public bool Visible => Window.Visible;
        public RectTransform Root => Window.Root;

        /// <summary>
        /// Размер текста тела (по умолчанию <see cref="PixelUi.FontBody"/>); если окно не помещается по высоте,
        /// автоподбор уменьшает его до <see cref="PixelUi.FontSmall"/>.
        /// </summary>
        public float BodyFontSize { get; set; } = PixelUi.FontBody;

        /// <summary>Блок под строками (своё содержимое — сюда), высота задаётся в <see cref="UseExtra"/>; null — не создан.</summary>
        public RectTransform Extra => m_Extra;

        /// <summary>Блок <see cref="Extra"/> — сразу под текстом, над строками (окно сундука: карточки, ниже «+N more» и эликсир).</summary>
        public bool ExtraFirst { get; set; }

        /// <summary>Поле рамки слева и справа — доля ширины safe area (по умолчанию 6 %).</summary>
        public float SideMargin { get; set; } = 0.06f;

        /// <summary>После каждой раскладки: ширина тела окна (единицы Canvas) — для своего содержимого в <see cref="Extra"/>.</summary>
        public Action<float> AfterLayout { get; set; }

        /// <summary>Новое содержимое: заголовок, крупная иконка (null — без неё), текст (null — без него); строки очищаются.</summary>
        public void Begin(string title, string icon, string body)
        {
            Window.SetTitle(title);
            m_IconName = string.IsNullOrEmpty(icon) ? null : icon;
            m_Body = body;
            m_RowData.Clear();
        }

        /// <summary>Сменить текст без перестройки строк (раскладка — при следующем <see cref="Layout"/>).</summary>
        public void SetBody(string body)
        {
            if (string.Equals(body, m_Body, StringComparison.Ordinal)) return;
            m_Body = body;
            m_LayoutSize = Vector2.zero;
        }

        public void AddRow(string icon, string text, Color color) =>
            m_RowData.Add(new Row { Icon = icon, Text = text, Color = color });

        /// <summary>Блок своего содержимого высотой <paramref name="height"/> под строками (0 — скрыть).</summary>
        public RectTransform UseExtra(float height)
        {
            if (m_Extra == null) m_Extra = Ui.Rect(Window.Body, "extra").Anchor(0, 1, 1, 1);
            m_ExtraHeight = Mathf.Max(0f, height);
            Ui.SetActive(m_Extra, m_ExtraHeight > 0f);
            return m_Extra;
        }

        /// <summary>
        /// Кнопка <paramref name="index"/> ряда (создаётся при первом обращении, имя «btn»). Действие читается в момент
        /// нажатия, окно само не закрывается: решает действие.
        /// </summary>
        public Ui.ButtonRefs SetButton(int index, string label, PixelButton style, Action action)
        {
            while (m_Buttons.Count <= index)
            {
                int k = m_Buttons.Count;
                m_Buttons.Add(Window.AddButton("btn", "", style, () => OnButton(k)));
                m_Actions.Add(null);
            }
            var b = m_Buttons[index];
            b.SetLabel(label);
            if (b.Style != style) b.SetStyle(style);
            b.SetInteractable(true);
            b.SetActive(true);
            m_Actions[index] = action;
            return b;
        }

        /// <summary>Скрыть кнопки начиная с <paramref name="index"/>.</summary>
        public void HideButtonsFrom(int index)
        {
            for (int i = index; i < m_Buttons.Count; i++)
            {
                m_Buttons[i].SetActive(false);
                m_Actions[i] = null;
            }
        }

        public Ui.ButtonRefs ButtonAt(int index) => index >= 0 && index < m_Buttons.Count ? m_Buttons[index] : null;

        void OnButton(int index)
        {
            var action = index < m_Actions.Count ? m_Actions[index] : null;
            action?.Invoke();
        }

        /// <summary>Разложить и показать поверх соседей (звук открытия — у кита).</summary>
        public void Show()
        {
            Window.UpdateBodyArea();
            Layout();
            Window.Show();
        }

        public void Hide() => Window.Hide();

        /// <summary>Повторить раскладку, если размер safe area изменился (поворот, окно редактора).</summary>
        public void Relayout()
        {
            if (Visible && m_Safe.rect.size != m_LayoutSize) Layout();
        }

        /// <summary>
        /// Высота рамки — по содержимому (иконка, текст, строки, блок Extra), не выше 86 % safe area; элементы тела — сверху вниз.
        /// Ширина — safe area без <see cref="SideMargin"/> с каждой стороны. Высота — чётное число пикселей арта (центр рамки на сетке).
        /// </summary>
        public void Layout()
        {
            Window.UpdateBodyArea();
            bool hasButtons = false;
            for (int i = 0; i < m_Buttons.Count; i++)
                if (m_Buttons[i].Rect.gameObject.activeSelf) hasButtons = true;
            m_LayoutSize = m_Safe.rect.size;
            float safeW = m_Safe.rect.width, safeH = m_Safe.rect.height;
            if (safeW < 100 * Px) safeW = RefWidth;
            if (safeH < 100 * Px) safeH = RefHeight;
            float inner = safeW * (1f - 2f * SideMargin) - 24 * Px;
            float chrome = WindowTop + (hasButtons ? 11 * Px + Window.ButtonsHeight + 4 * Px : WindowBottom);

            float y = 0f;
            bool hasIcon = m_IconName != null;
            Ui.SetActive(m_Icon, hasIcon);
            if (hasIcon)
            {
                PixelUi.SetIcon(m_Icon, m_IconName, 2);
                float iconH = PixelUi.IconSize(m_IconName, 2).y;
                m_Icon.rectTransform.anchoredPosition = CenterOffset(m_IconName, 2) + new Vector2(0f, -iconH * 0.5f);
                y = iconH + 4 * Px;
            }

            var text = Window.BodyText;
            bool hasText = !string.IsNullOrEmpty(m_Body);
            Ui.SetActive(text, hasText);
            float textH = 0f;
            if (hasText)
            {
                text.enableAutoSizing = false;
                text.fontSize = BodyFontSize;
                Ui.SetText(text, m_Body);
                textH = PixelUi.Snap(text.GetPreferredValues(m_Body, inner, 0f).y + Px);
            }

            int n = m_RowData.Count;
            bool hasExtra = m_Extra != null && m_ExtraHeight > 0f;
            // gap — после текста, extraGap — между строками и блоком Extra (в порядке ExtraFirst).
            float gap = hasText && (n > 0 || ExtraFirst && hasExtra) ? 4 * Px : 0f;
            float extraGap = hasExtra && (ExtraFirst ? n > 0 : hasText || n > 0) ? 4 * Px : 0f;
            float max = safeH * 0.86f;
            float rowH = RowHeight;
            if (n > 0)
            {
                float free = max - chrome - y - textH - gap - extraGap - m_ExtraHeight - (n - 1) * RowGap;
                if (n * rowH > free) rowH = Mathf.Max(12 * Px, Mathf.Floor(free / n / Px) * Px);
            }
            float rowsH = n > 0 ? n * rowH + (n - 1) * RowGap : 0f;
            float h = chrome + y + textH + gap + rowsH + extraGap + m_ExtraHeight;
            if (h > max)
            {
                h = max;
                textH = Mathf.Max(20 * Px, max - chrome - y - gap - rowsH - extraGap - m_ExtraHeight);
                text.enableAutoSizing = true;
                text.fontSizeMin = PixelUi.FontSmall;
                text.fontSizeMax = BodyFontSize;
            }
            h = Mathf.Max(h, chrome + 24 * Px);
            h = Mathf.Ceil(h / (2 * Px)) * 2 * Px;
            Window.Box.Anchor(SideMargin, 0.5f, 1f - SideMargin, 0.5f, new Vector2(0f, -h / 2), new Vector2(0f, h / 2));

            if (hasText)
            {
                text.rectTransform.Anchor(0, 1, 1, 1, new Vector2(0f, -(y + textH)), new Vector2(0f, -y));
                y += textH + gap;
            }
            if (ExtraFirst && hasExtra)
            {
                m_Extra.Anchor(0, 1, 1, 1, new Vector2(0f, -(y + m_ExtraHeight)), new Vector2(0f, -y));
                y += m_ExtraHeight + extraGap;
            }
            m_Rows.Anchor(0, 1, 1, 1, new Vector2(0f, -(y + rowsH)), new Vector2(0f, -y));
            for (int i = 0; i < n; i++)
            {
                if (i == m_RowViews.Count) m_RowViews.Add(CreateRow(m_Rows));
                var view = m_RowViews[i];
                var row = m_RowData[i];
                float top = i * (rowH + RowGap);
                view.Panel.rectTransform.Anchor(0, 1, 1, 1, new Vector2(0f, -(top + rowH)), new Vector2(0f, -top));
                string icon = row.Icon ?? "icon_star";
                PixelUi.SetIcon(view.Icon, icon);
                view.Icon.rectTransform.anchoredPosition = new Vector2(12 * Px, 0f) + CenterOffset(icon, 1);
                Ui.SetText(view.Text, row.Text);
                view.Text.color = row.Color;
                Ui.SetActive(view.Panel, true);
            }
            for (int i = n; i < m_RowViews.Count; i++) Ui.SetActive(m_RowViews[i].Panel, false);
            if (!ExtraFirst && hasExtra)
            {
                y += rowsH + extraGap;
                m_Extra.Anchor(0, 1, 1, 1, new Vector2(0f, -(y + m_ExtraHeight)), new Vector2(0f, -y));
            }
            AfterLayout?.Invoke(inner);
        }

        static RowView CreateRow(RectTransform parent)
        {
            var view = new RowView { Panel = PixelUi.Panel(parent, "row", PixelPanel.Inset) };
            view.Panel.raycastTarget = false;
            view.Icon = PixelUi.Icon(view.Panel.transform, "icon", "icon_star");
            view.Icon.rectTransform.anchorMin = view.Icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            view.Text = PixelUi.Text(view.Panel.transform, "text", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Left, false, true);
            view.Text.rectTransform.Anchor(0, 0, 1, 1, new Vector2(22 * Px, 0f), new Vector2(-4 * Px, 0f));
            view.Text.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(view.Text, PixelUi.FontSmall * 0.8f, PixelUi.FontBody);
            return view;
        }

        /// <summary>Сдвиг pivot спрайта, при котором центр его обрезанного прямоугольника стоит в центре родителя.</summary>
        internal static Vector2 CenterOffset(string sprite, int scale) =>
            PixelArt.TryGetDef(sprite, out var d) ? new Vector2((d.PivotX - d.W * 0.5f) * Px * scale, (d.PivotY - d.H * 0.5f) * Px * scale) : Vector2.zero;
    }
}
