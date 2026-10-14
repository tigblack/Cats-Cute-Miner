using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>Подгонка содержимого под Screen.safeArea.</summary>
    /// <remarks>Отдельный файл: имя файла совпадает с именем класса, поэтому у Unity есть MonoScript
    /// и компонент, сохранённый в CoreGame.unity (ProjectSetup), не загружается как Missing script.</remarks>
    public sealed class SafeArea : MonoBehaviour
    {
        Rect m_Last;
        float m_LastReserve;

        /// <summary>
        /// Единицы родительского Canvas, оставленные свободными у нижнего края safe area (баннер playable-сборки): низ
        /// содержимого поднимается на это значение. 0 — вся safe area.
        /// </summary>
        public float BottomReserve;

        void Update()
        {
            var safe = Screen.safeArea;
            float reserve = Mathf.Max(0f, BottomReserve);
            if (safe == m_Last && reserve == m_LastReserve || Screen.width <= 0 || Screen.height <= 0) return;
            m_Last = safe;
            m_LastReserve = reserve;
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            rt.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            rt.offsetMin = new Vector2(0f, reserve);
            rt.offsetMax = Vector2.zero;
        }
    }
}
