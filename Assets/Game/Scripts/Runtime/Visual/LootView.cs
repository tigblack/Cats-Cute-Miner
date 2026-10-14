using CatMiners.Core;
using CatMiners.Core.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Добыча на поле (GameSession.LootPickups): ореол loot_glow на полу, кучка loot_gold / loot_elixir / loot_hard и мелкая
    /// подпись «+N». Выпадает из разбитого камня короткой дугой (<see cref="Drop"/>), приземляется с облачком пыли и мягко
    /// подпрыгивает на месте, зовя тапнуть; тап — сбор (Pickable.Loot), после него кучка подлетает, сжимается и исчезает
    /// (<see cref="Collect"/>). Клетку не занимает: лежит в центре клетки якоря камня. Анимация — целыми пикселями арта,
    /// без выделений памяти в кадре.
    /// </summary>
    public sealed class LootView : MonoBehaviour
    {
        public int LootId;
        public string Currency;

        const float Px = 1f / PixelArt.CellPixels;
        /// <summary>Ноги кучки относительно центра клетки (вниз по экрану): кучка ~20×18 лежит в своей клетке.</summary>
        public const float FeetY = -9f * Px;
        /// <summary>Середина кучки над центром клетки: точка эффектов сбора, «+N» и полёта к счётчику.</summary>
        public const float PileCenterY = 0f;
        /// <summary>Подпись «+N» над кучкой, в пределах своей клетки.</summary>
        const float LabelZ = 13f * Px;

        // Выпадение: дуга из середины камня в клетку, масштаб от половины к полному.
        const float DropSeconds = 0.36f;
        const float DropArc = 0.7f;
        const float DropFrom = 0.5f;
        // Ожидание: прыжок на 2 пикселя раз в HopPeriod (кучки — вразнобой), ореол ярче в прыжке.
        const float HopPeriod = 1.4f;
        const float HopSeconds = 0.28f;
        const float HopPixels = 2f;
        const float GlowDim = 0.75f;
        // Сбор: подлёт на 10 пикселей, раздувание и сжатие в точку.
        const float CollectSeconds = 0.18f;
        const float CollectRise = 10f * Px;
        const float CollectPeak = 1.25f;

        const int GlowOrder = 0, PileOrder = 1;
        /// <summary>Облачков приземления в кадре: динамит роняет несколько куч сразу, пул эффектов не забивается.</summary>
        const int LandPuffsPerFrame = 3;
        static int s_PuffFrame = -1, s_Puffs;
        /// <summary>Летящая кучка — над объектами и затемнением, под частицами (как котик в переносе).</summary>
        const int FlyOrder = PixelWorld.FogOrder + 1;

        Transform m_Pile;
        SpriteRenderer m_Glow;
        Label3D m_Label;
        SortingGroup m_Group;
        BoxCollider m_Collider;
        Effects m_Fx;
        Vector3 m_Rest;
        Vector3 m_From;
        float m_DropT = 1f;         // < 1 — летит из камня
        float m_T;
        float m_CollectT = -1f;     // ≥ 0 — собирается
        float m_GlowAlpha = -1f;
        Vector3 m_PileShown = new Vector3(float.NaN, 0f, 0f);
        float m_ScaleShown = -1f;
        int m_Order = int.MinValue;

        static string Pick(string sprite, string fallback) => PixelArt.Has(sprite) ? sprite : fallback;

        /// <summary>Кучка по валюте ядра (gold, elixir, hard); пока арта нет — значок валюты.</summary>
        static string PileSprite(string currency)
        {
            switch (currency)
            {
                case LootState.Elixir: return Pick("loot_elixir", "icon_elixir");
                case LootState.Hard: return Pick("loot_hard", "icon_gem");
                default: return Pick("loot_gold", "icon_coin");
            }
        }

        static Color LabelColor(string currency)
        {
            switch (currency)
            {
                case LootState.Elixir: return PixelPalette.Purple5;
                case LootState.Hard: return PixelPalette.Teal5;
                default: return PixelPalette.Gold4;
            }
        }

        /// <summary>Кучка лежит в <paramref name="rest"/> (центр клетки). Без <see cref="Drop"/> — сразу на месте (загрузка, перестройка поля).</summary>
        public static LootView Create(Transform parent, LootState loot, Vector3 rest, Effects fx)
        {
            var root = new GameObject("loot" + loot.Id);
            root.transform.SetParent(parent, false);
            var restSnapped = PixelWorld.Snap(rest);
            root.transform.position = restSnapped;
            var v = root.AddComponent<LootView>();
            v.LootId = loot.Id;
            v.Currency = loot.Currency;
            v.m_Fx = fx;
            v.m_Rest = restSnapped;
            v.m_T = (loot.Id * 0.53f) % 1f * HopPeriod;   // кучки подпрыгивают вразнобой
            v.m_Group = PixelWorld.Group(root, restSnapped.z + FeetY);
            v.m_Group.sortingOrder = FloorOrder(restSnapped.z + FeetY);
            v.m_Order = v.m_Group.sortingOrder;

            var visual = PixelWorld.Visual(root.transform);
            if (PixelArt.Has("loot_glow"))
            {
                v.m_Glow = PixelWorld.Sprite(visual, "glow", "loot_glow", new Vector2(0f, FeetY + Px), GlowOrder);
                v.m_GlowAlpha = 1f;
            }
            var pile = new GameObject("pile").transform;
            pile.SetParent(visual, false);
            pile.localPosition = new Vector3(0f, FeetY, 0f);
            PixelWorld.Sprite(pile, "pile", PileSprite(loot.Currency), Vector2.zero, PileOrder);
            v.m_Pile = pile;
            v.m_PileShown = pile.localPosition;
            v.m_ScaleShown = 1f;

            v.m_Label = Label3D.Create(root.transform, new Vector3(0f, 0.1f, LabelZ), Label3D.SmallSize, LabelColor(loot.Currency));
            v.m_Label.Text.text = "+" + NumberFormat.Format(loot.Amount);

            // Тап сверху: клетка кучки с подписью; выше коллайдеров поля — ближе к камере (приоритет — в PointerInput.Tap).
            v.m_Collider = root.AddComponent<BoxCollider>();
            v.m_Collider.center = new Vector3(0f, 0.6f, 0.02f);
            v.m_Collider.size = new Vector3(0.9f, 1f, 0.9f);
            var p = root.AddComponent<Pickable>();
            p.Kind = Pickable.PickKind.Loot;
            p.Id = loot.Id;
            return v;
        }

        /// <summary>Выпасть из камня: дуга из <paramref name="from"/> (середина камня) в клетку; подпись — после приземления.</summary>
        public void Drop(Vector3 from)
        {
            m_From = from;
            m_DropT = 0f;
            if (m_Glow != null) m_Glow.enabled = false;
            m_Label.gameObject.SetActive(false);
            ApplyOrder(FlyOrder);
            ApplyPile(from.x - m_Rest.x, from.z - m_Rest.z, DropFrom);
        }

        /// <summary>
        /// Собрана (событие LootCollected): тап больше не ловится, подпись гаснет, кучка подлетает и исчезает.
        /// Модель добычу уже удалила — вид удаляет себя сам.
        /// </summary>
        public void Collect()
        {
            if (m_CollectT >= 0f) return;
            m_CollectT = 0f;
            if (m_Collider != null) m_Collider.enabled = false;
            var pick = GetComponent<Pickable>();
            if (pick != null) Destroy(pick);
            m_Label.gameObject.SetActive(false);
            if (m_Glow != null) m_Glow.enabled = false;
            ApplyOrder(FlyOrder);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (m_CollectT >= 0f)
            {
                m_CollectT += dt / CollectSeconds;
                float k = Mathf.Min(1f, m_CollectT);
                float s = k < 0.4f ? Mathf.Lerp(1f, CollectPeak, k / 0.4f) : Mathf.Lerp(CollectPeak, 0f, (k - 0.4f) / 0.6f);
                ApplyPile(0f, CollectRise * Mathf.Sin(k * Mathf.PI * 0.5f), s);
                if (k >= 1f) Destroy(gameObject);
                return;
            }
            if (m_DropT < 1f)
            {
                m_DropT = Mathf.Min(1f, m_DropT + dt / DropSeconds);
                float u = m_DropT;
                float k = u * (2f - u);   // быстрее в начале, мягче у пола
                float arc = Mathf.Sin(u * Mathf.PI) * DropArc;
                ApplyPile((m_From.x - m_Rest.x) * (1f - k), (m_From.z - m_Rest.z) * (1f - k) + arc, Mathf.Lerp(DropFrom, 1f, k));
                if (m_DropT >= 1f) Land();
                return;
            }
            m_T += dt;
            float t = m_T % HopPeriod;
            int px = t < HopSeconds ? Mathf.RoundToInt(Mathf.Sin(t / HopSeconds * Mathf.PI) * HopPixels) : 0;
            ApplyPile(0f, px * Px, 1f);
            if (m_Glow != null)
            {
                float a = px > 0 ? 1f : GlowDim;
                if (a != m_GlowAlpha)
                {
                    m_GlowAlpha = a;
                    m_Glow.color = new Color(1f, 1f, 1f, a);
                }
            }
        }

        void Land()
        {
            m_T = 0f;   // первый прыжок — сразу после приземления, как отскок
            if (m_Glow != null) m_Glow.enabled = true;
            m_Label.gameObject.SetActive(true);
            ApplyOrder(FloorOrder(m_Rest.z + FeetY));
            if (m_Fx == null) return;
            if (s_PuffFrame != Time.frameCount)
            {
                s_PuffFrame = Time.frameCount;
                s_Puffs = 0;
            }
            if (s_Puffs++ < LandPuffsPerFrame) m_Fx.Puff(m_Rest + new Vector3(0f, 0f, FeetY));
        }

        /// <summary>Смещение кучки от её ног (клетки, x вправо, y вверх по экрану) и масштаб; снап к пикселям арта.</summary>
        void ApplyPile(float x, float y, float scale)
        {
            var pos = new Vector3(PixelWorld.Snap(x), FeetY + PixelWorld.Snap(y), 0f);
            if (pos != m_PileShown)
            {
                m_PileShown = pos;
                m_Pile.localPosition = pos;
            }
            if (scale != m_ScaleShown)
            {
                m_ScaleShown = scale;
                m_Pile.localScale = new Vector3(scale, scale, 1f);
            }
        }

        /// <summary>
        /// Добыча лежит на полу: на полшага порядка позади предметов с той же опорой — котик, стоящий или идущий по клетке
        /// с кучкой, рисуется поверх неё, а не прячется за монетами.
        /// </summary>
        static int FloorOrder(float feetZ) => PixelWorld.OrderAt(feetZ) - PixelWorld.OrderPerCell / 2;

        void ApplyOrder(int order)
        {
            if (order == m_Order || m_Group == null) return;
            m_Order = order;
            m_Group.sortingOrder = order;
        }
    }
}
