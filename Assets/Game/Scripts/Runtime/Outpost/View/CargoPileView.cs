using UnityEngine;
using UnityEngine.Rendering;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Груз на земле в одной клетке (ТЗ 7.2, ART_NAMES §4): один вид на клетку с суммарным числом порций всех партий —
    /// 1 → cargo_sack_1, 2–4 → cargo_sack_2, 5–9 → cargo_heap_s, ≥ 10 → cargo_heap_l, под ним пятно cargo_glow. Учёт не трогает:
    /// число порций приходит из ядра каждый кадр. Некликабелен (тап по монетам ничего не зачисляет, ТЗ 5).
    /// Прибавка порций показывается после приземления летящего мешочка (<see cref="HoldIncrease"/>) с прыжком кучки,
    /// убыль (подбор) — сразу с проседанием. Виды живут в пуле <see cref="OutpostBoardView"/>.
    /// </summary>
    public sealed class CargoPileView : MonoBehaviour
    {
        const float Px = 1f / PixelArt.CellPixels;
        /// <summary>Ноги груза относительно центра клетки, как у LootView.</summary>
        public const float FeetY = -9f * Px;
        /// <summary>Середина кучки над центром клетки: точка вылета мешочка при подборе.</summary>
        public const float PileCenterY = -3f * Px;
        const float HopSeconds = 0.16f, HopPixels = 3f;
        const float DipSeconds = 0.12f;
        const int GlowOrder = 0, PileOrder = 1;

        public int Cell { get; private set; } = -1;
        /// <summary>Порций в клетке по ядру.</summary>
        public int Count { get; private set; }
        /// <summary>Сколько порций сейчас показано (прибавка ждёт приземления).</summary>
        public int Shown => m_Shown;
        /// <summary>Вид можно вернуть в пул: порций нет и анимации закончились.</summary>
        public bool Idle => Count == 0 && m_Shown == 0 && m_HopT < 0f && m_DipT < 0f && m_Hold <= 0f;

        SortingGroup m_Group;
        Transform m_Pile;
        SpriteRenderer m_Sprite, m_Glow;
        int m_Shown = -1;
        float m_Hold;
        float m_HopT = -1f, m_DipT = -1f;
        float m_OffsetShown = float.NaN;
        Vector3 m_Center;

        public static CargoPileView Create(Transform parent)
        {
            var root = new GameObject("cargo");
            root.transform.SetParent(parent, false);
            var v = root.AddComponent<CargoPileView>();
            v.m_Group = root.AddComponent<SortingGroup>();
            var visual = PixelWorld.Visual(root.transform);
            v.m_Glow = PixelWorld.Sprite(visual, "glow", "cargo_glow", new Vector2(0f, FeetY + 3f * Px), GlowOrder);
            var pile = new GameObject("pile").transform;
            pile.SetParent(visual, false);
            pile.localPosition = new Vector3(0f, FeetY, 0f);
            v.m_Pile = pile;
            v.m_Sprite = PixelWorld.Sprite(pile, "sack", "cargo_sack_1", Vector2.zero, PileOrder);
            return v;
        }

        /// <summary>Поставить вид (новый или из пула) в клетку: <paramref name="centerLocal"/> — центр клетки в корне поля.</summary>
        public void Place(int cell, Vector3 centerLocal)
        {
            Cell = cell;
            gameObject.name = "cargo" + cell;
            m_Center = PixelWorld.Snap(centerLocal);
            transform.localPosition = m_Center;
            Count = 0;
            m_Shown = -1;
            m_Hold = 0f;
            m_HopT = m_DipT = -1f;
            m_OffsetShown = float.NaN;
            // Груз на полу: на полшага порядка позади предметов с той же опорой — котик в клетке рисуется поверх (ART_NAMES §4).
            m_Group.sortingOrder = PixelWorld.OrderAt(m_Center.z + FeetY) - PixelWorld.OrderPerCell / 2;
            Show(0);
        }

        /// <summary>Мешочек летит в эту клетку: прибавка порций покажется через <paramref name="seconds"/> (по приземлении).</summary>
        public void HoldIncrease(float seconds)
        {
            if (seconds > m_Hold) m_Hold = seconds;
        }

        /// <summary>Число порций по ядру (каждый кадр) и анимации; <paramref name="live"/> = false — сразу без прыжков (сборка, офлайн).</summary>
        public void Tick(int count, bool live, float dt)
        {
            Count = count;
            if (m_Hold > 0f) m_Hold -= dt;
            if (!live)
            {
                m_Hold = 0f;
                m_HopT = m_DipT = -1f;
                Show(count);
            }
            else if (count < m_Shown || m_Shown < 0)
            {
                if (m_Shown > 0) m_DipT = 0f;
                Show(count);
            }
            else if (count > m_Shown && m_Hold <= 0f)
            {
                m_HopT = 0f;
                Show(count);
            }

            float off = 0f;
            if (m_HopT >= 0f)
            {
                m_HopT += dt;
                if (m_HopT >= HopSeconds) m_HopT = -1f;
                else off = Mathf.Round(Mathf.Sin(m_HopT / HopSeconds * Mathf.PI) * HopPixels) * Px;
            }
            else if (m_DipT >= 0f)
            {
                m_DipT += dt;
                if (m_DipT >= DipSeconds) m_DipT = -1f;
                else off = -Px;
            }
            if (off != m_OffsetShown)
            {
                m_OffsetShown = off;
                m_Pile.localPosition = new Vector3(0f, FeetY + off, 0f);
            }
        }

        void Show(int count)
        {
            if (count == m_Shown) return;
            m_Shown = count;
            bool on = count > 0;
            if (m_Sprite.enabled != on) m_Sprite.enabled = on;
            if (m_Glow.enabled != on) m_Glow.enabled = on;
            if (on) PixelWorld.SetSprite(m_Sprite, SpriteFor(count));
        }

        /// <summary>Спрайт груза по числу порций (ART_NAMES §4).</summary>
        public static string SpriteFor(int count) =>
            count >= 10 ? "cargo_heap_l" : (count >= 5 ? "cargo_heap_s" : (count >= 2 ? "cargo_sack_2" : "cargo_sack_1"));
    }
}
