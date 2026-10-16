using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Завод на поле по состоянию ядра (ТЗ 2.2 §4.3, VIEW_API §11) поверх вида цели <see cref="OutpostTargetView"/> (здание, цепи,
    /// трещины и полоса HP — там): Working — кадры работы factory_&lt;good&gt;_work_0…3 и полоса цикла над зданием, Ready — полная
    /// зелёная полоса (кнопку «Deliver ×N» показывает <see cref="FactoryOverlay"/> над ней), Stopped — без анимации и полосы,
    /// значок AUTO у полосы автоматического завода. Разлёт цепей fx_chain_break_0…3 — <see cref="PlayUnlock"/> (FactoryUnlocked).
    /// Только читает FactoryState; доля цикла (Now − CycleStart) / (CycleEnd − CycleStart) — только для показа. Без выделений в кадре.
    /// </summary>
    public sealed class FactoryView : MonoBehaviour
    {
        const float Px = 1f / PixelArt.CellPixels;
        const float WorkFrameSeconds = 0.15f;
        const float BreakFrameSeconds = 0.07f;
        /// <summary>Ширина полосы цикла (пиксели арта), как полоса HP цели 2×3.</summary>
        const int BarWidthPx = 48;
        const int FrameHeightPx = 6, FillHeightPx = 4;
        /// <summary>Слой кадров работы в группе цели: над зданием (10), под цепями (21).</summary>
        const int WorkLayer = 11;
        // Звенья разлетаются из точек креста цепей и замка (пиксели от ног здания).
        static readonly Vector2[] s_BreakAt = { new Vector2(-14f, 42f), new Vector2(14f, 42f), new Vector2(0f, 56f), new Vector2(-12f, 70f), new Vector2(12f, 70f) };
        static readonly Vector2[] s_BreakDrift = { new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-1f, 1f), new Vector2(1f, 1f) };
        static readonly string[] s_BreakFrames = { "fx_chain_break_0", "fx_chain_break_1", "fx_chain_break_2", "fx_chain_break_3" };

        OutpostTargetView m_Target;
        SpriteRenderer m_Work;
        string[] m_WorkFrames;
        int m_WorkFrame = -1;
        float m_Phase;
        Transform m_Bar;
        SpriteRenderer m_BarFill;
        int m_FillPx = -1;
        bool m_FillReady;
        SpriteRenderer m_Auto;
        Label3D m_AutoLabel;
        SpriteRenderer[] m_Break;
        float m_BreakT = -1f;
        int m_BreakFrame = -1;

        bool m_Applied;
        FactoryStatus m_Status;
        bool m_AutoShown;
        bool m_BarShown;

        public int TargetId => m_Target != null ? m_Target.TargetId : -1;
        public string Good { get; private set; }
        /// <summary>Показанное состояние (последний <see cref="Sync"/>).</summary>
        public FactoryStatus Status => m_Status;
        /// <summary>Значок AUTO виден (по самому спрайту, а не по флагу).</summary>
        public bool AutoBadgeVisible => m_Auto != null && m_Auto.enabled && m_Auto.gameObject.activeInHierarchy;
        /// <summary>Полоса цикла видна и её заполнение 0…1.</summary>
        public bool BarVisible => m_BarShown;
        public float BarFraction { get; private set; }
        /// <summary>Идёт анимация работы (кадры factory_&lt;good&gt;_work_*).</summary>
        public bool WorkAnimating => m_Work != null && m_Work.enabled;
        /// <summary>Идёт разлёт цепей.</summary>
        public bool BreakPlaying => m_BreakT >= 0f;
        /// <summary>Верх полосы над зданием (локально в корне поля): над ним — кнопка «Deliver», стрелка ведёт сюда.</summary>
        public Vector3 TopLocal => m_Target != null ? m_Target.FeetLocal + new Vector3(0f, 0f, m_Target.TopZ + (4f + FrameHeightPx) * Px) : Vector3.zero;

        /// <summary>Вид завода на объекте цели <paramref name="target"/> с товаром <paramref name="good"/>.</summary>
        public static FactoryView Attach(OutpostTargetView target, string good)
        {
            var v = target.gameObject.AddComponent<FactoryView>();
            v.m_Target = target;
            v.Good = good;
            v.Build();
            return v;
        }

        void Build()
        {
            m_WorkFrames = new string[4];
            bool hasWork = true;
            for (int i = 0; i < m_WorkFrames.Length; i++)
            {
                m_WorkFrames[i] = "factory_" + Good + "_work_" + i;
                hasWork &= PixelArt.Has(m_WorkFrames[i]);
            }
            if (hasWork)
            {
                m_Work = PixelWorld.Sprite(m_Target.VisualNode, "work", m_WorkFrames[0], Vector2.zero, WorkLayer);
                m_Work.enabled = false;
            }
            m_Phase = (BoardView.Hash(m_Target.TargetId, 11, 5) % 1000u) / 1000f;

            // Полоса цикла: на месте полосы HP цели (над зданием), оверлей поверх объектов.
            var go = new GameObject("cycle");
            m_Bar = go.transform;
            m_Bar.SetParent(transform, false);
            float barZ = m_Target.TopZ + 4f * Px;
            m_Bar.localPosition = new Vector3(0f, 0f, barZ);
            m_Bar.rotation = PixelWorld.FaceCamera;
            PixelWorld.Sliced(m_Bar, "frame", "hp_frame", Vector2.zero, new Vector2(BarWidthPx * Px, FrameHeightPx * Px), 0);
            m_BarFill = PixelWorld.Sliced(m_Bar, "fill", "ui_bar_fill_gold", Vector2.zero, new Vector2(Px, FillHeightPx * Px), 1);
            m_Auto = PixelWorld.Sprite(m_Bar, "auto", "icon_auto", new Vector2(-(BarWidthPx / 2 + 6) * Px, 0f), 2);
            m_Auto.enabled = false;
            var group = go.AddComponent<SortingGroup>();
            group.sortAtRoot = true;
            group.sortingOrder = PixelWorld.OverlayOrderAt(m_Bar.position.z);
            go.SetActive(false);
            m_AutoLabel = Label3D.Create(transform, new Vector3(0f, 0.1f, barZ + 9f * Px), Label3D.SmallSize, PixelUi.TextGood);
            m_AutoLabel.Text.text = Loc.T("factories.auto");
            m_AutoLabel.gameObject.SetActive(false);
        }

        /// <summary>Привести вид к FactoryState (каждый кадр, без выделений).</summary>
        public void Sync(FactoryState f, long now, float time, float dt)
        {
            if (f == null) return;
            if (!m_Applied || f.Status != m_Status)
            {
                m_Applied = true;
                m_Status = f.Status;
                bool working = f.Status == FactoryStatus.Working;
                if (m_Work != null && m_Work.enabled != working) m_Work.enabled = working;
                if (!working) m_WorkFrame = -1;
                SetBar(f.Status == FactoryStatus.Working || f.Status == FactoryStatus.Ready);
                if (f.Status == FactoryStatus.Ready) SetFill(1f, true);
            }
            bool auto = f.Automated && m_BarShown;
            if (auto != m_AutoShown)
            {
                m_AutoShown = auto;
                m_Auto.enabled = auto;
                m_AutoLabel.gameObject.SetActive(auto);
            }
            if (f.Status == FactoryStatus.Working)
            {
                long span = f.CycleEnd - f.CycleStart;
                double frac = span <= 0 ? 1.0 : (double)(now - f.CycleStart) / span;
                SetFill(frac < 0.0 ? 0f : (frac > 1.0 ? 1f : (float)frac), false);
                if (m_Work != null)
                {
                    int frame = (int)((time + m_Phase) / WorkFrameSeconds) % m_WorkFrames.Length;
                    if (frame != m_WorkFrame)
                    {
                        m_WorkFrame = frame;
                        PixelWorld.SetSprite(m_Work, m_WorkFrames[frame]);
                    }
                }
            }
            TickBreak(dt);
        }

        void SetBar(bool on)
        {
            m_BarShown = on;
            if (m_Bar.gameObject.activeSelf != on) m_Bar.gameObject.SetActive(on);
            if (!on) BarFraction = 0f;
        }

        void SetFill(float frac, bool ready)
        {
            BarFraction = frac;
            if (ready != m_FillReady)
            {
                m_FillReady = ready;
                PixelWorld.SetSprite(m_BarFill, ready ? "ui_bar_fill_green" : "ui_bar_fill_gold");
                m_FillPx = -1;
            }
            int inner = BarWidthPx - 2;
            int px = Mathf.RoundToInt(inner * frac);
            if (px == 0 && frac > 0f) px = 1;
            if (px == m_FillPx) return;
            m_FillPx = px;
            m_BarFill.enabled = px > 0;
            if (px == 0) return;
            m_BarFill.size = new Vector2(px * Px, FillHeightPx * Px);
            // Заливка прижата к левому краю внутри контура (1 пиксель), как у полосы HP.
            float left = -BarWidthPx * 0.5f + 1f;
            m_BarFill.transform.localPosition = new Vector3((left + px * 0.5f) * Px, 0f, 0f);
        }

        // ───────────── Разлёт цепей ─────────────

        /// <summary>
        /// Цепи разбиты вживую (FactoryUnlocked): звенья и замок разлетаются кадрами fx_chain_break_0…3 по 0,07 с, частицы металла
        /// (<paramref name="fx"/> может быть null — только кадры), <paramref name="boardOrigin"/> — мировая точка корня поля.
        /// </summary>
        public void PlayUnlock(Effects fx, Vector3 boardOrigin)
        {
            if (m_Break == null)
            {
                m_Break = new SpriteRenderer[s_BreakAt.Length];
                var visual = PixelWorld.Visual(transform, "chainBreak");
                for (int i = 0; i < m_Break.Length; i++)
                {
                    m_Break[i] = PixelWorld.Sprite(visual, "link", s_BreakFrames[0], s_BreakAt[i] * Px, PixelWorld.FxOrder);
                    m_Break[i].enabled = false;
                }
            }
            m_BreakT = 0f;
            m_BreakFrame = -1;
            TickBreak(0f);
            if (fx == null || m_Target == null) return;
            var center = boardOrigin + m_Target.FeetLocal + new Vector3(0f, 0f, 1.7f);
            fx.Burst(center, PixelPalette.Slate5, 14);
            fx.Sparkle(center + new Vector3(0f, 0f, 0.4f), 4);
        }

        void TickBreak(float dt)
        {
            if (m_BreakT < 0f || m_Break == null) return;
            m_BreakT += dt;
            int frame = (int)(m_BreakT / BreakFrameSeconds);
            if (frame >= s_BreakFrames.Length)
            {
                m_BreakT = -1f;
                for (int i = 0; i < m_Break.Length; i++) m_Break[i].enabled = false;
                return;
            }
            if (frame == m_BreakFrame) return;
            m_BreakFrame = frame;
            for (int i = 0; i < m_Break.Length; i++)
            {
                var sr = m_Break[i];
                PixelWorld.SetSprite(sr, s_BreakFrames[frame]);
                sr.transform.localPosition = (Vector3)((s_BreakAt[i] + s_BreakDrift[i] * (3f * frame)) * Px);
                sr.enabled = true;
            }
        }
    }
}
