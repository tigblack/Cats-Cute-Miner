using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Появление элемента кита: масштаб 0,9 → 1 за 0,15 с реального времени при каждом включении объекта
    /// (руководство по стилю, раздел 6: дробный масштаб допустим только в «поп»-анимациях до 0,25 с).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PixelPop : MonoBehaviour
    {
        public float Duration = 0.15f;
        public float From = 0.9f;
        float m_T;
        bool m_Running;

        void OnEnable() => Play();

        /// <summary>Проиграть появление заново (например, при смене содержимого уже открытого окна).</summary>
        public void Play()
        {
            m_T = 0f;
            m_Running = true;
            transform.localScale = new Vector3(From, From, 1f);
        }

        void OnDisable()
        {
            m_Running = false;
            transform.localScale = Vector3.one;
        }

        void Update()
        {
            if (!m_Running) return;
            m_T += FrameTime.Delta;
            float k = Duration > 0f ? Mathf.Clamp01(m_T / Duration) : 1f;
            if (k >= 1f)
            {
                m_Running = false;
                transform.localScale = Vector3.one;
                return;
            }
            // Затухающая скорость: элемент «пружинит» к полному размеру.
            float s = Mathf.Lerp(From, 1f, 1f - (1f - k) * (1f - k));
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
