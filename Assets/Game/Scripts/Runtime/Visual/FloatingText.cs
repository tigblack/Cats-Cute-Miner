using System.Collections.Generic;
using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Всплывающая надпись над полем (награды): пиксельный шрифт, поднимается вверх по экрану (+Z) на 0,8 клетки за 1 с
    /// шагами в пиксель арта и гаснет несколькими ступенями альфы. Оверлей поверх полос HP, бейджей и подписей.
    /// Экземпляры переиспользуются: в кадре нет выделений, текст и цвет меняются только при запуске.
    /// </summary>
    public sealed class FloatingText : MonoBehaviour
    {
        const float Seconds = 1f;
        const float RiseCells = 0.8f;
        /// <summary>Крупнее подписей объектов (2–2.8): награда читается на ходу.</summary>
        const float FontSize = Label3D.LargeSize;
        /// <summary>
        /// Поверх всех оверлеев поля: OverlayOrderAt(z) для z ≥ 0 не больше OverlayBase. Порядок постоянный — надпись,
        /// поднимаясь, не уходит под подписи и полосы HP объектов ниже неё.
        /// </summary>
        const int Order = PixelWorld.OverlayBase + 100;

        // Свободные надписи. Уничтоженные вместе со сценой (Unity-null) пропускаются при выдаче.
        static readonly Stack<FloatingText> s_Free = new Stack<FloatingText>();

        Label3D m_Label;
        Vector3 m_Start;
        Color m_Color;
        float m_T;
        int m_RiseStep;
        int m_AlphaStep;

        public static void Spawn(Vector3 pos, string text, Color color)
        {
            FloatingText f = null;
            while (f == null && s_Free.Count > 0) f = s_Free.Pop();
            if (f == null)
            {
                var go = new GameObject("float");
                f = go.AddComponent<FloatingText>();
                f.m_Label = Label3D.Create(go.transform, Vector3.zero, FontSize, color);
                f.m_Label.SetOrder(Order);
            }
            else f.gameObject.SetActive(true);
            f.m_Start = PixelWorld.Snap(pos);
            f.m_Color = color;
            f.m_T = 0f;
            f.m_RiseStep = -1;
            f.m_AlphaStep = -1;
            f.m_Label.Text.text = text;
            f.Apply();
        }

        void Update()
        {
            m_T += Time.deltaTime;
            if (m_T >= Seconds)
            {
                gameObject.SetActive(false);
                s_Free.Push(this);
                return;
            }
            Apply();
        }

        void Apply()
        {
            float u = m_T / Seconds;
            // Подъём с замедлением, целыми пикселями арта.
            float rise = RiseCells * (1f - (1f - u) * (1f - u));
            int step = Mathf.RoundToInt(rise * PixelArt.CellPixels);
            if (step != m_RiseStep)
            {
                m_RiseStep = step;
                var p = m_Start;
                p.z += step / (float)PixelArt.CellPixels;
                transform.position = p;
            }
            // Видна полностью до 60 % жизни, затем гаснет тремя ступенями.
            int alphaStep = u < 0.6f ? 0 : (u < 0.73f ? 1 : (u < 0.86f ? 2 : 3));
            if (alphaStep != m_AlphaStep)
            {
                m_AlphaStep = alphaStep;
                var c = m_Color;
                c.a *= 1f - alphaStep * 0.25f;
                m_Label.Text.color = c;
            }
        }
    }
}
