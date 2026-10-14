using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Летящие мешочки (ТЗ 16: порционное появление добычи, перенос и сдача): из цели — на землю или в сумку, с земли — в сумку,
    /// из сумки — в воронку склада. Кольцевой пул спрайтов без выделений в кадре; переполнение забирает самый старый полёт.
    /// Только вид: учёт порций ведёт ядро, а куча на земле показывает прибавку после приземления (<see cref="CargoPileView.HoldIncrease"/>).
    /// Точки — локальные в корне поля; полёт к котику следует за ним (цель уточняется каждый кадр).
    /// </summary>
    public sealed class CargoFlights
    {
        const int PoolSize = 24;
        /// <summary>Летящий мешочек — над объектами и затемнением, под частицами (как LootView в полёте).</summary>
        const int FlyOrder = PixelWorld.FogOrder + 1;

        struct Flight
        {
            public bool Active;
            public Vector3 From, To;
            public OutpostCatView Follow;
            public float Lift;
            public float T, Duration, Arc;
            public float ScaleFrom, ScaleTo;
            public Vector3 Shown;
            public float ScaleShown;
        }

        readonly Flight[] m_F = new Flight[PoolSize];
        readonly SpriteRenderer[] m_Sr = new SpriteRenderer[PoolSize];
        readonly Transform[] m_Tr = new Transform[PoolSize];
        int m_Next;
        int m_Active;

        public CargoFlights(Transform visual)
        {
            for (int i = 0; i < PoolSize; i++)
            {
                var sr = PixelWorld.Sprite(visual, "flyingSack", "cargo_sack_1", Vector2.zero, FlyOrder);
                sr.enabled = false;
                m_Sr[i] = sr;
                m_Tr[i] = sr.transform;
            }
        }

        public int ActiveCount => m_Active;

        /// <summary>
        /// Запустить мешочек из <paramref name="from"/> в <paramref name="to"/> (локально в корне поля) за <paramref name="seconds"/>
        /// по дуге высотой <paramref name="arc"/> клеток; <paramref name="follow"/> — котик-получатель (к его сумке).
        /// </summary>
        public void Launch(Vector3 from, Vector3 to, float seconds, float arc, OutpostCatView follow = null, float followLift = 0f,
            float scaleFrom = 0.6f, float scaleTo = 1f)
        {
            int i = Free();
            ref var f = ref m_F[i];
            if (!f.Active) m_Active++;
            f.Active = true;
            f.From = from;
            f.To = to;
            f.Follow = follow;
            f.Lift = followLift;
            f.T = 0f;
            f.Duration = Mathf.Max(0.05f, seconds);
            f.Arc = arc;
            f.ScaleFrom = scaleFrom;
            f.ScaleTo = scaleTo;
            f.Shown = new Vector3(float.NaN, 0f, 0f);
            f.ScaleShown = -1f;
            m_Sr[i].enabled = true;
            Apply(i, 0f);
        }

        int Free()
        {
            for (int k = 0; k < PoolSize; k++)
            {
                int i = (m_Next + k) % PoolSize;
                if (!m_F[i].Active)
                {
                    m_Next = (i + 1) % PoolSize;
                    return i;
                }
            }
            // Все заняты: самый старый (следующий по кругу) уступает место.
            int old = m_Next;
            m_Next = (m_Next + 1) % PoolSize;
            return old;
        }

        public void Tick(float dt)
        {
            if (m_Active == 0) return;
            for (int i = 0; i < PoolSize; i++)
            {
                ref var f = ref m_F[i];
                if (!f.Active) continue;
                f.T += dt;
                float u = Mathf.Min(1f, f.T / f.Duration);
                Apply(i, u);
                if (u < 1f) continue;
                f.Active = false;
                f.Follow = null;
                m_Sr[i].enabled = false;
                m_Active--;
            }
        }

        void Apply(int i, float u)
        {
            ref var f = ref m_F[i];
            var to = f.To;
            if (f.Follow != null && f.Follow.isActiveAndEnabled) to = f.Follow.LocalPosition + new Vector3(0f, 0f, f.Lift);
            float k = u * (2f - u);   // быстрее в начале, мягче в конце
            var p = Vector3.LerpUnclamped(f.From, to, k);
            p.z += Mathf.Sin(u * Mathf.PI) * f.Arc;
            p.y = 0f;
            var snapped = PixelWorld.Snap(p);
            if (snapped != f.Shown)
            {
                f.Shown = snapped;
                m_Tr[i].localPosition = new Vector3(snapped.x, snapped.z, 0f);
            }
            float s = Mathf.Lerp(f.ScaleFrom, f.ScaleTo, k);
            s = Mathf.Round(s * 8f) / 8f;
            if (s != f.ScaleShown)
            {
                f.ScaleShown = s;
                m_Tr[i].localScale = new Vector3(s, s, 1f);
            }
        }

        /// <summary>Погасить все полёты (перестройка поля, офлайн).</summary>
        public void Clear()
        {
            for (int i = 0; i < PoolSize; i++)
            {
                m_F[i].Active = false;
                m_F[i].Follow = null;
                if (m_Sr[i] != null) m_Sr[i].enabled = false;
            }
            m_Active = 0;
        }
    }
}
