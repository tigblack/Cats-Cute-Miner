using System;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Кирка игрока «из воздуха» (ТЗ 6.6, 16): кадры fx_strike_pick_0…3 (замах, взмах, удар, отскок), pivot спрайта — точка касания.
    /// Урон наносит ядро в кадр удара: <see cref="Tick"/> вызывает обработчик удара, и только при успешной команде вид
    /// показывает отклик цели. Кольцевой пул без выделений в кадре; переполнение забирает самый старый удар.
    /// Точки — локальные в корне поля (x вправо, z вверх по экрану).
    /// </summary>
    public sealed class PlayerStrikeFx
    {
        const int PoolSize = 6;
        /// <summary>Поверх объектов и затемнения, под искрами контакта (частицы — FxOrder).</summary>
        const int Order = PixelWorld.FxOrder - 1;
        /// <summary>Кадры: 0 — замах (появление), 1 — взмах, 2 — удар, 3 — отскок; затем кирка исчезает.</summary>
        static readonly float[] s_FrameEnds = { 0.05f, 0.10f, 0.18f, 0.26f };
        const int ImpactFrame = 2;
        /// <summary>«Поп» появления 0,75 → 1 за первый кадр (короткое нецелое масштабирование, PIXEL_STYLE_GUIDE §1).</summary>
        const float PopFrom = 0.75f;
        static readonly string[] s_Frames = { "fx_strike_pick_0", "fx_strike_pick_1", "fx_strike_pick_2", "fx_strike_pick_3" };

        struct Strike
        {
            public bool Active;
            public int TargetId;
            public Vector3 Local;
            public float T;
            public int Frame;
            public bool Impacted;
        }

        readonly Strike[] m_S = new Strike[PoolSize];
        readonly SpriteRenderer[] m_Sr = new SpriteRenderer[PoolSize];
        readonly Transform[] m_Tr = new Transform[PoolSize];
        readonly Sprite[] m_FrameSprites = new Sprite[s_Frames.Length];
        int m_Next;
        int m_Active;

        public PlayerStrikeFx(Transform visual)
        {
            for (int f = 0; f < s_Frames.Length; f++) m_FrameSprites[f] = PixelArt.World(s_Frames[f]);
            for (int i = 0; i < PoolSize; i++)
            {
                var sr = PixelWorld.Sprite(visual, "playerStrike", s_Frames[0], Vector2.zero, Order);
                sr.enabled = false;
                m_Sr[i] = sr;
                m_Tr[i] = sr.transform;
            }
        }

        public int ActiveCount => m_Active;

        /// <summary>Длительность от тапа до кадра удара (секунды).</summary>
        public static float ImpactDelay => s_FrameEnds[ImpactFrame - 1];

        /// <summary>
        /// Запустить удар по цели <paramref name="targetId"/> в точке <paramref name="local"/> (кончик кирки);
        /// <paramref name="flip"/> — замах справа налево (у правого края поля).
        /// </summary>
        public void Launch(int targetId, Vector3 local, bool flip)
        {
            int i = Free();
            ref var s = ref m_S[i];
            if (!s.Active) m_Active++;
            s.Active = true;
            s.TargetId = targetId;
            s.Local = PixelWorld.Snap(new Vector3(local.x, 0f, local.z));
            s.T = 0f;
            s.Frame = -1;
            s.Impacted = false;
            var sr = m_Sr[i];
            sr.flipX = flip;
            sr.enabled = true;
            m_Tr[i].localPosition = new Vector3(s.Local.x, s.Local.z, 0f);
            Apply(i);
        }

        int Free()
        {
            for (int k = 0; k < PoolSize; k++)
            {
                int i = (m_Next + k) % PoolSize;
                if (!m_S[i].Active)
                {
                    m_Next = (i + 1) % PoolSize;
                    return i;
                }
            }
            int old = m_Next;
            m_Next = (m_Next + 1) % PoolSize;
            return old;
        }

        /// <summary>
        /// Шаг анимации. <paramref name="impact"/> вызывается один раз в кадр удара (targetId, точка) и возвращает, нанесён ли урон;
        /// <paramref name="vanish"/> — когда кирка исчезает (точка), для искорок.
        /// </summary>
        public void Tick(float dt, Func<int, Vector3, bool> impact, Action<Vector3> vanish)
        {
            if (m_Active == 0) return;
            for (int i = 0; i < PoolSize; i++)
            {
                ref var s = ref m_S[i];
                if (!s.Active) continue;
                s.T += dt;
                if (!s.Impacted && s.T >= s_FrameEnds[ImpactFrame - 1])
                {
                    s.Impacted = true;
                    impact?.Invoke(s.TargetId, s.Local);
                }
                if (s.T >= s_FrameEnds[s_FrameEnds.Length - 1])
                {
                    s.Active = false;
                    m_Sr[i].enabled = false;
                    m_Active--;
                    vanish?.Invoke(s.Local);
                    continue;
                }
                Apply(i);
            }
        }

        void Apply(int i)
        {
            ref var s = ref m_S[i];
            int frame = 0;
            while (frame < s_FrameEnds.Length - 1 && s.T >= s_FrameEnds[frame]) frame++;
            if (frame != s.Frame)
            {
                s.Frame = frame;
                m_Sr[i].sprite = m_FrameSprites[frame];
            }
            float scale = frame == 0 ? Mathf.Round(Mathf.Lerp(PopFrom, 1f, s.T / s_FrameEnds[0]) * 8f) / 8f : 1f;
            var current = m_Tr[i].localScale.x;
            if (!Mathf.Approximately(current, scale)) m_Tr[i].localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>Погасить все удары (перестройка поля, окно, офлайн) — без вызова обработчика урона.</summary>
        public void Clear()
        {
            for (int i = 0; i < PoolSize; i++)
            {
                m_S[i].Active = false;
                if (m_Sr[i] != null) m_Sr[i].enabled = false;
            }
            m_Active = 0;
        }
    }
}
