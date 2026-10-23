using CatMiners.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Playable
{
    /// <summary>
    /// Пульс и блеск кнопки CTA (reference.md §5: кнопка пульсирует, блик каждые 3,2 с начиная с 1,5 с): поп кита
    /// (<see cref="PixelPop"/>, масштаб до 0,25 с — дробный масштаб допустим только так) каждые <see cref="PulseSeconds"/> и
    /// искра из атласа fx_spark_0…3 в правом верхнем углу каждые <see cref="ShineSeconds"/>. Реальное время кадров
    /// (<see cref="FrameTime"/>); отсчёт заново при каждом включении кнопки. Лучи не ловит.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayableShine : MonoBehaviour
    {
        public const float PulseSeconds = 1.6f;
        public const float ShineSeconds = 3.2f;
        public const float ShineDelay = 1.5f;
        const float FrameSeconds = 0.08f;
        static readonly string[] s_Frames = { "fx_spark_0", "fx_spark_1", "fx_spark_2", "fx_spark_3" };

        PixelPop m_Pop;
        Image m_Spark;
        int m_Scale = 1;
        float m_Pulse, m_Shine;
        int m_Frame = -1;

        /// <summary>Искра видна в этом кадре (тесты, снимки).</summary>
        public bool Sparkling => m_Frame >= 0;

        /// <summary>Пульс и искра (масштаб <paramref name="sparkScale"/>) на кнопке кита <paramref name="button"/>.</summary>
        public static PlayableShine Attach(Ui.ButtonRefs button, int sparkScale = 1)
        {
            var go = button.Rect.gameObject;
            var shine = go.AddComponent<PlayableShine>();
            shine.m_Pop = go.GetComponent<PixelPop>();
            if (shine.m_Pop == null) shine.m_Pop = go.AddComponent<PixelPop>();
            shine.m_Pop.From = 1.08f;
            shine.m_Pop.Duration = 0.25f;
            shine.m_Scale = Mathf.Max(1, sparkScale);
            var parent = button.Content != null ? button.Content : button.Rect;
            shine.m_Spark = PixelUi.Icon(parent, "spark", s_Frames[0], shine.m_Scale);
            var rt = shine.m_Spark.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.one;
            rt.anchoredPosition = new Vector2(-5 * PixelUi.Px, -5 * PixelUi.Px) * shine.m_Scale;
            shine.m_Spark.gameObject.SetActive(false);
            return shine;
        }

        void OnEnable()
        {
            m_Pulse = 0f;
            m_Shine = ShineSeconds - ShineDelay;
            SetFrame(-1);
        }

        /// <summary>Выключена (баннер притушен): искра не остаётся на кнопке.</summary>
        void OnDisable() => SetFrame(-1);

        void Update()
        {
            float dt = FrameTime.Delta;
            m_Pulse += dt;
            if (m_Pulse >= PulseSeconds)
            {
                m_Pulse -= PulseSeconds;
                if (m_Pop != null) m_Pop.Play();
            }
            m_Shine += dt;
            if (m_Shine >= ShineSeconds) m_Shine -= ShineSeconds;
            int frame = (int)(m_Shine / FrameSeconds);
            SetFrame(frame < s_Frames.Length ? frame : -1);
        }

        void SetFrame(int frame)
        {
            if (frame == m_Frame || m_Spark == null) return;
            m_Frame = frame;
            if (frame >= 0) PixelUi.SetIcon(m_Spark, s_Frames[frame], m_Scale);
            Ui.SetActive(m_Spark, frame >= 0);
        }
    }
}
