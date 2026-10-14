using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Шкала кита (<see cref="PixelUi.Bar"/>): рамка ui_bar_frame и заливка ui_bar_fill_* с отступом 2 пикселя арта.
    /// Ширина заливки — целое число пикселей арта: не ноль при значении больше 0 и не полная при значении меньше 1.
    /// Пересчёт — только при смене значения или размера рамки.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PixelBar : MonoBehaviour
    {
        /// <summary>Отступ заливки от края рамки (жёлоб ui_bar_frame), единицы Canvas.</summary>
        const float Inset = 2 * PixelArt.UiPixel;

        Image m_Frame;
        Image m_Fill;
        PixelBarFill m_FillStyle;
        float m_Value;
        int m_ShownPixels = -1;
        float m_ShownWidth = -1f;

        public Image Frame => m_Frame;
        public Image Fill => m_Fill;
        public RectTransform Rect => (RectTransform)transform;
        public float Value => m_Value;
        public PixelBarFill FillStyle => m_FillStyle;

        internal void Init(Image frame, Image fill, PixelBarFill style)
        {
            m_Frame = frame;
            m_Fill = fill;
            m_FillStyle = style;
            var rt = fill.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            Apply(true);
        }

        /// <summary>Значение 0…1.</summary>
        public void SetValue(float value)
        {
            value = Mathf.Clamp01(value);
            if (value == m_Value && m_ShownPixels >= 0) return;
            m_Value = value;
            Apply(false);
        }

        public void SetFill(PixelBarFill style)
        {
            if (style == m_FillStyle) return;
            m_FillStyle = style;
            PixelUi.SetSliced(m_Fill, PixelUi.BarFillSprite(style));
        }

        void OnRectTransformDimensionsChange()
        {
            if (m_Fill != null) Apply(false);
        }

        void Apply(bool force)
        {
            float width = Rect.rect.width;
            int total = Mathf.Max(0, Mathf.FloorToInt((width - 2 * Inset) / PixelArt.UiPixel + 1e-3f));
            int px = Mathf.RoundToInt(total * m_Value);
            if (m_Value > 0f && px == 0 && total > 0) px = 1;
            if (m_Value < 1f && px == total && total > 1) px = total - 1;
            if (!force && px == m_ShownPixels && Mathf.Approximately(width, m_ShownWidth)) return;
            m_ShownPixels = px;
            m_ShownWidth = width;
            var rt = m_Fill.rectTransform;
            rt.offsetMin = new Vector2(Inset, Inset);
            rt.offsetMax = new Vector2(Inset + px * PixelArt.UiPixel, -Inset);
            Ui.SetActive(m_Fill, px > 0);
        }
    }
}
