using UnityEngine;
using UnityEngine.Rendering;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Бейдж уровня котика (Docs/Art/PIXEL_STYLE_GUIDE.md, раздел 5): тёмная плашка <c>badge_level</c> и цифры <c>digit_*</c>.
    /// Для 2–3 цифр плашка собирается из копий 13×13 со сдвигом 5 пикселей; швы закрываются пикселями тех же цветов
    /// палитры (заливка navy1, кайма cream3/cream1 или gold4/gold2), поэтому ширина 13 / 18 / 23 пикселя без масштабирования.
    /// Бейдж — оверлей: своя SortingGroup с sortAtRoot выводит его из группы котика поверх объектов (OverlayOrderAt).
    /// Координаты — пиксели арта от левого нижнего угла плашки.
    /// </summary>
    public sealed class LevelBadge
    {
        const float Px = 1f / PixelArt.CellPixels;
        const int Plate = 13;        // сторона badge_level
        const int Shift = 5;         // сдвиг следующей копии плашки
        const int DigitW = 5;
        const int DigitH = 7;
        const int MaxDigits = 3;
        const int PlateOrder = 0;    // копии плашки: 0, 1, 2 — правая поверх левой
        const int SeamOrder = 10;
        const int DigitOrder = 20;

        static readonly string[] DigitNames = { "digit_0", "digit_1", "digit_2", "digit_3", "digit_4", "digit_5", "digit_6", "digit_7", "digit_8", "digit_9" };

        readonly Transform m_Root;
        readonly SortingGroup m_Group;
        readonly SpriteRenderer[] m_Plates = new SpriteRenderer[MaxDigits];
        // На каждый шов: заливка 3×9, пиксель светлой каймы сверху, пиксель тёмной снизу.
        readonly SpriteRenderer[] m_SeamFill = new SpriteRenderer[MaxDigits - 1];
        readonly SpriteRenderer[] m_SeamTop = new SpriteRenderer[MaxDigits - 1];
        readonly SpriteRenderer[] m_SeamBottom = new SpriteRenderer[MaxDigits - 1];
        readonly SpriteRenderer[] m_Digits = new SpriteRenderer[MaxDigits];
        readonly int m_CenterX, m_Bottom;
        int m_Value = -1;
        int m_Count;
        bool m_Hi;
        int m_Order = int.MinValue;

        public Transform Transform => m_Root;

        /// <summary>
        /// Бейдж внутри узла визуала <paramref name="parent"/> (x вправо, y вверх по экрану): центр по X —
        /// <paramref name="centerX"/>, низ — <paramref name="bottom"/>, в пикселях арта от начала узла.
        /// </summary>
        public LevelBadge(Transform parent, int centerX, int bottom)
        {
            var go = new GameObject("badge");
            m_Root = go.transform;
            m_Root.SetParent(parent, false);
            m_CenterX = centerX;
            m_Bottom = bottom;
            m_Group = go.AddComponent<SortingGroup>();
            m_Group.sortAtRoot = true;
        }

        public void SetOrder(int order)
        {
            if (order == m_Order) return;
            m_Order = order;
            m_Group.sortingOrder = order;
        }

        public void SetActive(bool on)
        {
            if (m_Root.gameObject.activeSelf != on) m_Root.gameObject.SetActive(on);
        }

        /// <summary>Показать уровень (1–999) и рамку: обычная cream или золотая (кандидат на объединение).</summary>
        public void Set(int value, bool highlighted)
        {
            value = Mathf.Clamp(value, 1, 999);
            if (value == m_Value && highlighted == m_Hi) return;
            bool layout = value != m_Value || m_Count == 0;
            m_Value = value;
            m_Hi = highlighted;
            int count = value >= 100 ? 3 : (value >= 10 ? 2 : 1);
            if (layout) Layout(count);
            ApplyColours();
        }

        void Layout(int count)
        {
            m_Count = count;
            int width = Plate + Shift * (count - 1);
            int left = m_CenterX - width / 2;
            m_Root.localPosition = new Vector3(left * Px, m_Bottom * Px, 0f);

            for (int i = 0; i < MaxDigits; i++)
            {
                bool on = i < count;
                if (on && m_Plates[i] == null)
                    m_Plates[i] = PixelWorld.Sprite(m_Root, "plate", "badge_level", Pos(Shift * i, 0, Plate, Plate), PlateOrder + i);
                if (m_Plates[i] != null) m_Plates[i].enabled = on;
            }

            for (int i = 0; i < MaxDigits - 1; i++)
            {
                bool on = i + 1 < count;
                if (on && m_SeamFill[i] == null)
                {
                    int x = Shift * (i + 1);
                    // Шов: левая кромка следующей копии легла на заливку предыдущей — закрываем её цветами плашки.
                    m_SeamFill[i] = Pixel("seamFill", x, 2, 3, 9);
                    m_SeamTop[i] = Pixel("seamTop", x + 2, 11, 1, 1);
                    m_SeamBottom[i] = Pixel("seamBottom", x + 2, 1, 1, 1);
                }
                if (m_SeamFill[i] != null)
                {
                    m_SeamFill[i].enabled = on;
                    m_SeamTop[i].enabled = on;
                    m_SeamBottom[i].enabled = on;
                }
            }

            // Цифры 5×7 на строках 3–9 плашки; одна — по центру, две — через 2 пикселя, три — через 1.
            int first = count == 1 ? 4 : 3;
            int step = count == 2 ? DigitW + 2 : DigitW + 1;
            int v = m_Value;
            for (int i = 0; i < MaxDigits; i++)
            {
                bool on = i < count;
                if (on)
                {
                    int pow = count - 1 - i;
                    int d = (pow == 2 ? v / 100 : (pow == 1 ? v / 10 : v)) % 10;
                    if (m_Digits[i] == null) m_Digits[i] = PixelWorld.Sprite(m_Root, "digit", DigitNames[d], Vector2.zero, DigitOrder);
                    else PixelWorld.SetSprite(m_Digits[i], DigitNames[d]);
                    m_Digits[i].transform.localPosition = Pos(first + step * i, 3, DigitW, DigitH);
                }
                if (m_Digits[i] != null) m_Digits[i].enabled = on;
            }
        }

        void ApplyColours()
        {
            string plate = m_Hi ? "badge_level_hi" : "badge_level";
            for (int i = 0; i < m_Count; i++) PixelWorld.SetSprite(m_Plates[i], plate);
            var rimLight = m_Hi ? PixelPalette.Gold4 : PixelPalette.Cream3;
            var rimDark = m_Hi ? PixelPalette.Gold2 : PixelPalette.Cream1;
            for (int i = 0; i + 1 < m_Count; i++)
            {
                m_SeamTop[i].color = rimLight;
                m_SeamBottom[i].color = rimDark;
            }
        }

        SpriteRenderer Pixel(string name, int x, int y, int w, int h)
        {
            var sr = PixelWorld.Sprite(m_Root, name, "px_white", Pos(x, y, w, h), SeamOrder, PixelPalette.Navy1);
            sr.transform.localScale = new Vector3(w, h, 1f);
            return sr;
        }

        /// <summary>Центр прямоугольника w×h с левым нижним углом (x, y) — для спрайтов с pivot в центре холста.</summary>
        static Vector2 Pos(int x, int y, int w, int h) => new Vector2((x + w * 0.5f) * Px, (y + h * 0.5f) * Px);
    }
}
