using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Нажатие кнопки кита (<see cref="PixelUi.Button"/>, <see cref="PixelUi.IconButton"/>): пока палец на доступной кнопке,
    /// содержимое (подпись, иконка) опущено на 1 пиксель арта — как лицо спрайта *_pressed. Тап по кнопке, недоступной
    /// в момент нажатия, — звук отказа <see cref="UiSound.PlayDenied"/>. Спрайты меняет сам Button (Transition.SpriteSwap).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PixelButtonPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        static readonly Vector2 s_Sunk = new Vector2(0f, -PixelArt.UiPixel);

        Selectable m_Target;
        RectTransform m_Content;
        bool m_Down, m_Inside, m_Sunk, m_DeniedOnPress;

        /// <summary>Кнопка и контейнер, который опускается (растянут на всю кнопку, смещение 0).</summary>
        public void Init(Selectable target, RectTransform content)
        {
            m_Target = target;
            m_Content = content;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            m_DeniedOnPress = m_Target != null && !m_Target.IsInteractable();
            m_Down = true;
            m_Inside = true;
            Apply();
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            m_Down = false;
            Apply();
        }

        public void OnPointerEnter(PointerEventData e)
        {
            m_Inside = true;
            Apply();
        }

        public void OnPointerExit(PointerEventData e)
        {
            m_Inside = false;
            Apply();
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            // Button.OnPointerClick (компонент добавлен раньше) уже отработал, и его действие могло выключить эту же кнопку
            // (улучшение карты, последний бесплатный сундук): отказ решается по состоянию на момент нажатия. Вторая проверка —
            // кнопка стала доступной, пока палец лежал, и Button уже сработал: тогда отказа нет.
            bool denied = m_DeniedOnPress && m_Target != null && !m_Target.IsInteractable();
            m_DeniedOnPress = false;
            if (denied) UiSound.PlayDenied();
        }

        void OnDisable()
        {
            m_Down = false;
            m_Inside = false;
            m_DeniedOnPress = false;
            Apply();
        }

        void Apply()
        {
            bool sink = m_Down && m_Inside && m_Target != null && m_Target.IsInteractable();
            if (sink == m_Sunk || m_Content == null) return;
            m_Sunk = sink;
            m_Content.anchoredPosition = sink ? s_Sunk : Vector2.zero;
        }
    }
}
