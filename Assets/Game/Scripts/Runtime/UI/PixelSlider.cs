using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Ползунок кита (<see cref="PixelUi.Slider"/>): шкала ui_bar_frame + ui_bar_fill_gold (заливка целыми пикселями арта,
    /// <see cref="PixelBar"/>) и золотая ручка ui_slider_knob, которая встаёт на сетку пикселей арта. Ввод — стандартный
    /// Slider (тап и перетаскивание по всей строке); его невидимая заливка нужна только для расчёта значения.
    /// Значение 0…1; <see cref="OnChanged"/> вызывается при изменении игроком, <see cref="SetValue"/> его не вызывает.
    /// По отпусканию — мягкий щелчок (громкость звуков слышна сразу).
    /// </summary>
    public sealed class PixelSlider : MonoBehaviour, IPointerUpHandler
    {
        const float Px = PixelArt.UiPixel;
        /// <summary>Ручка 10×13 пикселей арта; шкала — 8 пикселей высотой по центру строки.</summary>
        const float KnobW = 10 * Px, BarH = 8 * Px;

        Slider m_Slider;
        PixelBar m_Bar;
        Image m_Knob;
        RectTransform m_Area;
        bool m_Silent;

        public UnityAction<float> OnChanged;
        public float Value => m_Slider != null ? m_Slider.value : 0f;
        public RectTransform Rect => (RectTransform)transform;
        public Slider Slider => m_Slider;

        internal static PixelSlider Create(Transform parent, string name, float value, UnityAction<float> onChanged)
        {
            var root = Ui.Rect(parent, name);
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            var s = root.gameObject.AddComponent<PixelSlider>();
            s.m_Bar = PixelUi.Bar(root, "bar", PixelBarFill.Gold);
            s.m_Bar.Rect.Anchor(0, 0.5f, 1, 0.5f, new Vector2(0f, -BarH / 2), new Vector2(0f, BarH / 2));
            // Область расчёта значения — без половины ручки по краям: центр ручки ходит от края до края шкалы.
            s.m_Area = Ui.Rect(root, "area").Anchor(0, 0, 1, 1, new Vector2(KnobW / 2, 0f), new Vector2(-KnobW / 2, 0f));
            var fill = Ui.Rect(s.m_Area, "fill").Anchor(0, 0, 0, 1);
            s.m_Knob = PixelUi.Icon(root, "knob", "ui_slider_knob", 1);
            s.m_Knob.rectTransform.anchorMin = s.m_Knob.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            var slider = root.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.targetGraphic = hit;
            slider.fillRect = fill;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            s.m_Slider = slider;
            s.SetValue(value);
            slider.onValueChanged.AddListener(s.Changed);
            s.OnChanged = onChanged;
            return s;
        }

        /// <summary>Показать значение без вызова OnChanged (из сохранения).</summary>
        public void SetValue(float value)
        {
            m_Silent = true;
            m_Slider.value = Mathf.Clamp01(value);
            m_Silent = false;
            Apply();
        }

        void Changed(float value)
        {
            Apply();
            if (!m_Silent) OnChanged?.Invoke(value);
        }

        public void OnPointerUp(PointerEventData eventData) => UiSound.PlayClick();

        void OnRectTransformDimensionsChange()
        {
            if (m_Slider != null) Apply();
        }

        void Apply()
        {
            float v = m_Slider.value;
            m_Bar.SetValue(v);
            float width = Rect.rect.width - KnobW;
            if (width <= 0f) return;
            // Центр ручки — на сетке пикселей арта.
            float x = Mathf.Round((KnobW / 2 + v * width) / Px) * Px;
            var pos = new Vector2(x, 0f);
            if (m_Knob.rectTransform.anchoredPosition != pos) m_Knob.rectTransform.anchoredPosition = pos;
        }
    }
}
