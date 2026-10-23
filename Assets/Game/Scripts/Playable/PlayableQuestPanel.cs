using CatMiners.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Playable
{
    /// <summary>
    /// Панель квеста слева сверху под табличкой шахты (ТЗ playable §3.3, reference.md §8 п. 1): иконка в ячейке, жёлтое
    /// название, шаг и сразу за ним зелёный счётчик «x/N» крупнее (FontLarge, «подпрыгивает» на прогрессе), как «Defeat punks
    /// 0/3» у референса; ширина — по содержимому, не меньше <see cref="MinWidthFraction"/> safe area. Новый квест въезжает
    /// слева. Только кит PixelUi; лучи не ловит — тап под панелью уходит на поле. Высота — между верхней полосой HUD и тостом.
    /// </summary>
    public sealed class PlayableQuestPanel
    {
        const float Px = PixelUi.Px;
        /// <summary>Высота панели: 26 пикселей арта (заканчивается на 2 пикселя выше тоста HUD).</summary>
        public const float Height = 26 * Px;
        /// <summary>Наименьшая ширина — доля ширины safe area; длиннее текст — шире, до края safe area.</summary>
        public const float MinWidthFraction = 0.5f;
        const float Pad = 3 * Px, SlotSize = 20 * Px, RowHeight = 10 * Px, CounterGap = 3 * Px, Margin = 2 * Px;
        const float SlideSeconds = 0.25f;

        public readonly RectTransform Root;
        readonly RectTransform m_Safe;
        readonly Image m_Icon, m_Check;
        readonly TextMeshProUGUI m_Title, m_Step, m_Counter;
        readonly PixelPop m_Bump;
        readonly Vector2 m_Home;
        float m_Slide = 1f;
        float m_FitSafeWidth = -1f;
        string m_IconName;

        public bool Visible => Root.gameObject.activeSelf;
        public string Title => m_Title.text;
        public string Step => m_Step.text;
        public string Counter => m_Counter.text;
        /// <summary>Квест выполнен: в ячейке галочка вместо иконки.</summary>
        public bool Checked => m_Check.gameObject.activeSelf;
        /// <summary>Подпись счётчика (тесты: размер и место сразу за шагом).</summary>
        public TextMeshProUGUI CounterText => m_Counter;
        public TextMeshProUGUI StepText => m_Step;

        /// <summary>Панель под верхней полосой: <paramref name="top"/> — отступ от верха safe area (единицы Canvas).</summary>
        public PlayableQuestPanel(RectTransform safe, float top)
        {
            m_Safe = safe;
            Root = Ui.Rect(safe, "QuestTracker");
            Root.anchorMin = Root.anchorMax = new Vector2(0f, 1f);
            Root.pivot = new Vector2(0f, 1f);
            Root.sizeDelta = new Vector2(160 * Px, Height);
            m_Home = Root.anchoredPosition = new Vector2(Margin, -top);
            var group = Root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            var panel = PixelUi.Panel(Root, "panel", PixelPanel.Dark);
            panel.raycastTarget = false;
            panel.rectTransform.Anchor(0, 0, 1, 1);

            var slot = PixelUi.Panel(panel.transform, "slot", PixelPanel.Inset);
            slot.raycastTarget = false;
            slot.rectTransform.Anchor(0, 0.5f, 0, 0.5f, new Vector2(Pad, -SlotSize / 2), new Vector2(Pad + SlotSize, SlotSize / 2));
            m_Icon = PixelUi.Icon(slot.transform, "icon", "icon_star");
            m_Check = PixelUi.Icon(slot.transform, "check", "icon_check");
            m_Check.rectTransform.anchoredPosition = CenterOffset("icon_check");
            m_Check.gameObject.SetActive(false);

            float left = Pad + SlotSize + Pad;
            m_Title = PixelUi.Text(panel.transform, "title", "", PixelUi.FontBody, PixelUi.TextAccent, TextAlignmentOptions.Left, true, true);
            m_Title.rectTransform.Anchor(0, 1, 1, 1, new Vector2(left, -(Pad + RowHeight)), new Vector2(-Pad, -Pad));
            m_Title.textWrappingMode = TextWrappingModes.NoWrap;

            m_Step = PixelUi.Text(panel.transform, "step", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Left, false, true);
            m_Step.textWrappingMode = TextWrappingModes.NoWrap;

            m_Counter = PixelUi.Text(panel.transform, "counter", "", PixelUi.FontLarge, PixelUi.TextGood, TextAlignmentOptions.Left, true, true);
            m_Counter.textWrappingMode = TextWrappingModes.NoWrap;
            m_Counter.overflowMode = TextOverflowModes.Overflow;
            m_Bump = m_Counter.gameObject.AddComponent<PixelPop>();
            m_Bump.From = 1.35f;
            m_Bump.Duration = 0.2f;

            Root.gameObject.SetActive(false);
        }

        /// <summary>Новый квест: тексты, иконка, счётчик без «прыжка»; панель по содержимому въезжает слева.</summary>
        public void Show(string icon, string title, string step, string counter)
        {
            SetIcon(string.IsNullOrEmpty(icon) || !PixelArt.Has(icon) ? "icon_star" : icon);
            Ui.SetText(m_Title, title);
            Ui.SetText(m_Step, step);
            Ui.SetText(m_Counter, counter);
            SetChecked(false);
            Fit();
            m_Slide = 0f;
            Place();
            Root.gameObject.SetActive(true);
        }

        /// <summary>Счётчик изменился: новый текст, раскладка и «прыжок» (0,2 с, только поп-анимация кита).</summary>
        public void SetCounter(string counter, bool bump)
        {
            if (string.Equals(m_Counter.text, counter, System.StringComparison.Ordinal)) return;
            Ui.SetText(m_Counter, counter);
            Fit();
            if (bump && Visible) m_Bump.Play();
        }

        public void SetChecked(bool done)
        {
            Ui.SetActive(m_Check, done);
            Ui.SetActive(m_Icon, !done);
        }

        public void Hide() => Ui.SetActive(Root, false);

        /// <summary>Въезд слева за <see cref="SlideSeconds"/> реального времени, по целым пикселям арта; смена ширины экрана — раскладка.</summary>
        public void Tick(float dt)
        {
            if (!Visible) return;
            if (!Mathf.Approximately(m_Safe.rect.width, m_FitSafeWidth)) Fit();
            if (m_Slide >= 1f) return;
            m_Slide = Mathf.Min(1f, m_Slide + dt / SlideSeconds);
            Place();
        }

        /// <summary>
        /// Ширина по содержимому: название или «шаг + счётчик» (счётчик — сразу за шагом), не меньше
        /// <see cref="MinWidthFraction"/> и не шире safe area; всё по сетке пикселей арта.
        /// </summary>
        void Fit()
        {
            m_FitSafeWidth = m_Safe.rect.width;
            float safeW = m_FitSafeWidth > 100 * Px ? m_FitSafeWidth : 1080f;
            float left = Pad + SlotSize + Pad;
            float stepW = Preferred(m_Step), counterW = Preferred(m_Counter), titleW = Preferred(m_Title);
            float content = left + Mathf.Max(titleW, stepW + CounterGap + counterW) + Pad + Px;
            float max = Mathf.Floor((safeW - 2 * Margin) / Px) * Px;
            float width = Mathf.Min(Mathf.Ceil(Mathf.Max(content, safeW * MinWidthFraction) / Px) * Px, max);
            Root.sizeDelta = new Vector2(width, Height);
            // Шаг и счётчик — на одной строке, центры на одной высоте; счётчик крупнее и выходит за строку на пиксель.
            float maxStep = Mathf.Max(0f, Root.sizeDelta.x - left - Pad - CounterGap - counterW);
            stepW = Mathf.Min(stepW, maxStep);
            m_Step.rectTransform.Anchor(0, 0, 0, 0, new Vector2(left, Pad), new Vector2(left + stepW, Pad + RowHeight));
            float x = left + stepW + CounterGap;
            m_Counter.rectTransform.Anchor(0, 0, 0, 0, new Vector2(x, Pad - Px), new Vector2(x + counterW, Pad + RowHeight + Px));
        }

        /// <summary>Ширина текста в его размере шрифта, вверх до пикселя арта.</summary>
        static float Preferred(TMP_Text text) =>
            string.IsNullOrEmpty(text.text) ? 0f : Mathf.Ceil((text.GetPreferredValues(text.text).x + 0.5f) / Px) * Px;

        void Place()
        {
            float k = 1f - (1f - m_Slide) * (1f - m_Slide);
            float width = Root.sizeDelta.x > 1f ? Root.sizeDelta.x : 160 * Px;
            Root.anchoredPosition = m_Home + new Vector2(PixelUi.Snap(-(1f - k) * (width + 4 * Px)), 0f);
        }

        void SetIcon(string icon)
        {
            if (icon == m_IconName) return;
            m_IconName = icon;
            PixelUi.SetIcon(m_Icon, icon);
            m_Icon.rectTransform.anchoredPosition = CenterOffset(icon);
        }

        /// <summary>Сдвиг pivot спрайта, при котором центр его обрезанного прямоугольника стоит в центре ячейки.</summary>
        static Vector2 CenterOffset(string sprite) =>
            PixelArt.TryGetDef(sprite, out var d) ? new Vector2((d.PivotX - d.W * 0.5f) * Px, (d.PivotY - d.H * 0.5f) * Px) : Vector2.zero;
    }
}
