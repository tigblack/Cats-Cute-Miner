using System.Collections.Generic;
using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Трубы заводов на поле (ТЗ 2.2 §5, VIEW_API §11): своя труба толщиной 3 пикселя арта у каждого завода (тёмный контур и полоса
    /// цвета товара <c>Run.FactoryRules.ForGood(g).Color</c> → <see cref="PixelPalette.ByName"/>), заглушка с иконкой товара у завода,
    /// жгуты с муфтами, капсулы партий в пути. Раскладка — <see cref="PipeLayout"/>, спрайты создаются один раз на шахту.
    /// Труба видна только у открытого завода; при открытии вживую прокладывается от завода к складу за <see cref="RevealSeconds"/>.
    /// Капсула движется равномерно от DepartTick до ArriveTick (доля пути — только для показа), на трубе видно не больше
    /// <see cref="CapsulesPerPipe"/> капсул, пул — <see cref="CapsulePool"/>. Слои: над разметкой пола, под котиками, грузом и
    /// неразрушенными целями (их спрайты прячут трубу). Обычный кадр не выделяет память.
    /// </summary>
    public sealed class PipeNetworkView
    {
        const float Px = 1f / PixelArt.CellPixels;
        /// <summary>Прокладка трубы при открытии завода вживую (секунды, ТЗ: до 1 с).</summary>
        public const float RevealSeconds = 0.8f;
        public const int CapsulesPerPipe = 3;
        public const int CapsulePool = 60;

        // Порядок отрисовки: над разметкой пола и обломками, под котиками и целями (OrderAt ≥ −20000).
        const int PipeOrder = PixelWorld.FloorDecalOrder + 20;       // + 2k контур, + 2k + 1 полоса трубы k
        const int BandOrder = PixelWorld.FloorDecalOrder + 60;
        const int HarnessStripeOrder = BandOrder + 1;
        const int CouplingOrder = BandOrder + 4;
        const int CapOrder = BandOrder + 5, CapIconOrder = BandOrder + 6;
        const int CapsuleOrder = BandOrder + 8;

        sealed class PipeView
        {
            public PipeLayout.Pipe Data;
            public string Good;
            public Color Color;
            public SpriteRenderer[] Outline;   // по отрезкам (null у отрезков жгута)
            public SpriteRenderer[] Stripe;    // по отрезкам
            public SpriteRenderer Cap, CapIcon;
            public bool Shown;
            public bool PendingReveal;
            public float RevealT = 1f;         // 0…1 — идёт прокладка
            public int Revealed;               // открытая длина пути (пиксели)
            public int FactoryIndex = -1;
            public int Capsules;
        }

        readonly Transform m_Visual;
        readonly PipeLayout m_Layout;
        readonly PipeView[] m_Pipes;
        readonly SpriteRenderer[] m_Bands;
        readonly SpriteRenderer[] m_Couplings;
        readonly SpriteRenderer[] m_Capsule = new SpriteRenderer[CapsulePool];
        readonly int[] m_CapsulePipe = new int[CapsulePool];
        readonly bool[] m_CapsuleVertical = new bool[CapsulePool];
        int m_CapsulesShown;
        bool m_Dirty = true;
        bool m_Revealing;

        public PipeLayout Layout => m_Layout;
        /// <summary>Капсул на поле сейчас (тесты, производительность).</summary>
        public int ActiveCapsules => m_CapsulesShown;

        PipeNetworkView(Transform root, OutpostMap map, OutpostRun run, BaseCampView camp)
        {
            m_Visual = PixelWorld.Visual(root, "Pipes");
            bool warehouse = camp != null && camp.HasWarehouse;
            int warehouseCol = warehouse ? Mathf.FloorToInt(camp.WarehouseLocal.x) : -1;
            // Вход склада: ось трубы столбца склада, высота — центр fpipe_inlet на коньке.
            var inlet = warehouse
                ? new Vector2Int(warehouseCol * PixelArt.CellPixels + PipeLayout.AxisPx, Mathf.RoundToInt(camp.WarehouseInletLocal.z * PixelArt.CellPixels))
                : Vector2Int.zero;
            m_Layout = PipeLayout.Build(map, warehouseCol, inlet);
            var rules = run?.FactoryRules;

            m_Pipes = new PipeView[m_Layout.Pipes.Count];
            for (int k = 0; k < m_Pipes.Length; k++)
            {
                var data = m_Layout.Pipes[k];
                var rule = rules?.ForType(data.TypeId);
                string good = rule?.GoodId;
                var color = rule != null && PixelPalette.TryGet(rule.Color, out var c) ? c : PixelPalette.Cream4;
                var v = new PipeView { Data = data, Good = good, Color = color };
                int n = data.Segments.Count;
                v.Outline = new SpriteRenderer[n];
                v.Stripe = new SpriteRenderer[n];
                for (int i = 0; i < n; i++)
                {
                    var s = data.Segments[i];
                    if (!s.Harness) v.Outline[i] = Rect("pipe", PipeOrder + 2 * k, PixelPalette.Ink1);
                    v.Stripe[i] = Rect("stripe", s.Harness ? HarnessStripeOrder : PipeOrder + 2 * k + 1, color);
                }
                // Заглушка и иконка чётного размера (pivot на границе пикселей) — на углу пикселя оси.
                v.Cap = PixelWorld.Sprite(m_Visual, "cap", "fpipe_cap", PxPos(data.CapCenter, false), CapOrder);
                v.CapIcon = PixelWorld.Sprite(m_Visual, "capIcon", FactoryText.GoodIconSmall(good), PxPos(data.CapCenter, false), CapIconOrder);
                m_Pipes[k] = v;
                SetPipeVisible(v, false);
            }
            m_Bands = new SpriteRenderer[m_Layout.Bands.Count];
            for (int i = 0; i < m_Bands.Length; i++)
            {
                var b = m_Layout.Bands[i];
                m_Bands[i] = Rect("harness", BandOrder, PixelPalette.Ink1);
                Place(m_Bands[i], b.Min.x, b.Min.y, b.Max.x, b.Max.y);
                m_Bands[i].enabled = false;
            }
            m_Couplings = new SpriteRenderer[m_Layout.Couplings.Count];
            for (int i = 0; i < m_Couplings.Length; i++)
            {
                m_Couplings[i] = PixelWorld.Sprite(m_Visual, "coupling", "fpipe_coupling", PxPos(m_Layout.Couplings[i].Center, false), CouplingOrder);
                m_Couplings[i].enabled = false;
            }
            for (int i = 0; i < CapsulePool; i++)
            {
                m_Capsule[i] = PixelWorld.Sprite(m_Visual, "capsule", "fpipe_capsule", Vector2.zero, CapsuleOrder);
                m_Capsule[i].enabled = false;
                m_CapsulePipe[i] = -1;
                m_CapsuleVertical[i] = true;
            }
        }

        public static PipeNetworkView Create(Transform root, OutpostMap map, OutpostRun run, BaseCampView camp) =>
            new PipeNetworkView(root, map, run, camp);

        SpriteRenderer Rect(string name, int order, Color color) =>
            PixelWorld.Sprite(m_Visual, name, "px_white", Vector2.zero, order, color);

        /// <summary>Пиксель (x, z) → точка в узле визуала: центр пикселя (<paramref name="center"/>) или его нижний левый угол.</summary>
        static Vector2 PxPos(Vector2Int p, bool center) => center ? new Vector2((p.x + 0.5f) * Px, (p.y + 0.5f) * Px) : new Vector2(p.x * Px, p.y * Px);

        /// <summary>Прямоугольник пикселей [x0, x1] × [z0, z1] включительно из px_white.</summary>
        static void Place(SpriteRenderer sr, int x0, int z0, int x1, int z1)
        {
            var t = sr.transform;
            t.localPosition = new Vector3((x0 + x1 + 1) * 0.5f * Px, (z0 + z1 + 1) * 0.5f * Px, 0f);
            t.localScale = new Vector3(x1 - x0 + 1, z1 - z0 + 1, 1f);
        }

        /// <summary>Отрезок A → B (пиксели оси) толщиной <paramref name="width"/>, открытый на длину <paramref name="shown"/> от A.</summary>
        static void PlaceSegment(SpriteRenderer sr, PipeLayout.Segment s, int width, int shown)
        {
            if (sr == null) return;
            int len = s.Length;
            if (shown < 0 || (shown == 0 && len > 0))
            {
                if (sr.enabled) sr.enabled = false;
                return;
            }
            var a = s.A;
            var dir = new Vector2Int(System.Math.Sign(s.B.x - a.x), System.Math.Sign(s.B.y - a.y));
            var b = a + dir * Mathf.Min(len, shown);
            int half = (width - 1) / 2;
            Place(sr, Mathf.Min(a.x, b.x) - half, Mathf.Min(a.y, b.y) - half, Mathf.Max(a.x, b.x) + half, Mathf.Max(a.y, b.y) + half);
            if (!sr.enabled) sr.enabled = true;
        }

        static void SetPipeVisible(PipeView v, bool on)
        {
            for (int i = 0; i < v.Stripe.Length; i++)
            {
                if (v.Outline[i] != null) v.Outline[i].enabled = on;
                v.Stripe[i].enabled = on;
            }
            v.Cap.enabled = on;
            v.CapIcon.enabled = on;
        }

        /// <summary>Отрезки трубы, открытые на <paramref name="revealed"/> пикселей пути (прокладка при открытии завода).</summary>
        static void ApplyReveal(PipeView v, int revealed)
        {
            var segs = v.Data.Segments;
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                int shown = revealed - s.Start;
                PlaceSegment(v.Outline[i], s, PipeLayout.PipeWidth, shown);
                PlaceSegment(v.Stripe[i], s, 1, shown);
            }
            v.Revealed = revealed;
        }

        // ───────────── Запросы ─────────────

        int IndexOf(int targetId)
        {
            for (int k = 0; k < m_Pipes.Length; k++)
                if (m_Pipes[k].Data.TargetId == targetId) return k;
            return -1;
        }

        /// <summary>У завода есть труба (маршрут есть).</summary>
        public bool HasPipe(int targetId) => IndexOf(targetId) >= 0;

        /// <summary>Труба завода показана (завод открыт).</summary>
        public bool IsShown(int targetId)
        {
            int k = IndexOf(targetId);
            return k >= 0 && m_Pipes[k].Shown;
        }

        /// <summary>Доля проложенной трубы 0…1 (1 — целиком; 0 — скрыта или нет трубы).</summary>
        public float RevealProgress(int targetId)
        {
            int k = IndexOf(targetId);
            if (k < 0 || !m_Pipes[k].Shown) return 0f;
            var v = m_Pipes[k];
            return v.Data.TotalLength <= 0 ? 1f : Mathf.Clamp01(v.Revealed / (float)v.Data.TotalLength);
        }

        /// <summary>Капсул на трубе завода сейчас.</summary>
        public int VisibleCapsules(int targetId)
        {
            int k = IndexOf(targetId);
            return k >= 0 ? m_Pipes[k].Capsules : 0;
        }

        /// <summary>Центр заглушки трубы завода (локально в корне поля): отсюда выходит капсула.</summary>
        public bool TryGetCapLocal(int targetId, out Vector3 local)
        {
            int k = IndexOf(targetId);
            local = Vector3.zero;
            if (k < 0) return false;
            var c = PxPos(m_Pipes[k].Data.CapCenter, true);
            local = new Vector3(c.x, 0f, c.y);
            return true;
        }

        /// <summary>Мировая точка капсулы с номером i пула (тесты); false — капсула не показана.</summary>
        public bool TryGetCapsuleLocal(int i, out Vector3 local, out int targetId)
        {
            local = Vector3.zero;
            targetId = -1;
            if (i < 0 || i >= m_CapsulesShown) return false;
            var p = m_Capsule[i].transform.localPosition;
            local = new Vector3(p.x, 0f, p.y);
            targetId = m_CapsulePipe[i] >= 0 ? m_Pipes[m_CapsulePipe[i]].Data.TargetId : -1;
            return true;
        }

        // ───────────── События и кадр ─────────────

        /// <summary>Цепи завода разбиты вживую (FactoryUnlocked): труба прокладывается анимацией, а не появляется сразу.</summary>
        public void BeginReveal(int targetId)
        {
            int k = IndexOf(targetId);
            if (k >= 0) m_Pipes[k].PendingReveal = true;
        }

        /// <summary>Привести трубы и капсулы к ядру (каждый кадр, без выделений).</summary>
        public void Sync(OutpostRun run, float dt)
        {
            var factories = run.Factories;
            for (int k = 0; k < m_Pipes.Length; k++)
            {
                var v = m_Pipes[k];
                var f = FactoryAt(factories, v);
                bool open = f != null && f.Status != FactoryStatus.Locked;
                if (open != v.Shown)
                {
                    v.Shown = open;
                    m_Dirty = true;
                    if (!open)
                    {
                        SetPipeVisible(v, false);
                        v.RevealT = 1f;
                        v.PendingReveal = false;
                    }
                    else
                    {
                        v.Cap.enabled = true;
                        v.CapIcon.enabled = true;
                        if (v.PendingReveal)
                        {
                            v.RevealT = 0f;
                            m_Revealing = true;
                            ApplyReveal(v, 0);
                        }
                        else
                        {
                            v.RevealT = 1f;
                            ApplyReveal(v, v.Data.TotalLength);
                        }
                    }
                }
                v.PendingReveal = false;
            }
            if (m_Revealing)
            {
                m_Revealing = false;
                for (int k = 0; k < m_Pipes.Length; k++)
                {
                    var v = m_Pipes[k];
                    if (!v.Shown || v.RevealT >= 1f) continue;
                    v.RevealT = Mathf.Min(1f, v.RevealT + dt / RevealSeconds);
                    // Ход прокладки с замедлением к складу, целыми пикселями.
                    float e = 1f - (1f - v.RevealT) * (1f - v.RevealT);
                    ApplyReveal(v, v.RevealT >= 1f ? v.Data.TotalLength : Mathf.RoundToInt(v.Data.TotalLength * e));
                    m_Revealing |= v.RevealT < 1f;
                    m_Dirty = true;
                }
            }
            if (m_Dirty)
            {
                m_Dirty = false;
                SyncShared();
            }
            SyncCapsules(run);
        }

        static FactoryState FactoryAt(List<FactoryState> factories, PipeView v)
        {
            if (factories == null) return null;
            int i = v.FactoryIndex;
            if (i >= 0 && i < factories.Count && factories[i] != null && factories[i].TargetId == v.Data.TargetId) return factories[i];
            for (i = 0; i < factories.Count; i++)
                if (factories[i] != null && factories[i].TargetId == v.Data.TargetId)
                {
                    v.FactoryIndex = i;
                    return factories[i];
                }
            return null;
        }

        /// <summary>Контуры жгутов и муфты: видны, когда до них дошла хотя бы одна показанная труба.</summary>
        void SyncShared()
        {
            for (int i = 0; i < m_Bands.Length; i++)
            {
                var b = m_Layout.Bands[i];
                bool on = false;
                for (int k = 0; k < b.Pipes.Length && !on; k++)
                {
                    var v = m_Pipes[b.Pipes[k]];
                    on = v.Shown && v.Revealed >= b.StartOnPipe[k];
                }
                if (m_Bands[i].enabled != on) m_Bands[i].enabled = on;
            }
            for (int i = 0; i < m_Couplings.Length; i++)
            {
                var c = m_Layout.Couplings[i];
                bool on = false;
                for (int k = 0; k < c.Pipes.Length && !on; k++)
                {
                    var v = m_Pipes[c.Pipes[k]];
                    on = v.Shown && v.Revealed >= c.StartOnPipe[k];
                }
                if (m_Couplings[i].enabled != on) m_Couplings[i].enabled = on;
            }
        }

        /// <summary>
        /// Партии в трубах (Run.PipeBatches по Seq): на трубе — не больше <see cref="CapsulesPerPipe"/> первых (ближайших к складу),
        /// остальные учитываются только ядром. Доля пути (Now − Depart) / (Arrive − Depart) — только для показа.
        /// </summary>
        void SyncCapsules(OutpostRun run)
        {
            for (int k = 0; k < m_Pipes.Length; k++) m_Pipes[k].Capsules = 0;
            int n = 0;
            var batches = run.PipeBatches;
            long now = run.Now;
            if (batches != null && run.Phase != RunPhase.ReadyToContinue)
            {
                for (int i = 0; i < batches.Count && n < CapsulePool; i++)
                {
                    var b = batches[i];
                    int k = IndexOf(b.FactoryId);
                    if (k < 0) continue;
                    var v = m_Pipes[k];
                    if (!v.Shown || v.Capsules >= CapsulesPerPipe) continue;
                    long span = b.ArriveTick - b.DepartTick;
                    double frac = span <= 0 ? 1.0 : (double)(now - b.DepartTick) / span;
                    if (frac < 0.0) frac = 0.0;
                    else if (frac > 1.0) frac = 1.0;
                    float dist = (float)(frac * v.Data.TotalLength);
                    if (dist > v.Revealed) dist = v.Revealed;
                    var p = PipeLayout.PointAt(v.Data, dist, out bool vertical);
                    var sr = m_Capsule[n];
                    if (m_CapsulePipe[n] != k)
                    {
                        m_CapsulePipe[n] = k;
                        sr.color = v.Color;
                    }
                    if (m_CapsuleVertical[n] != vertical)
                    {
                        m_CapsuleVertical[n] = vertical;
                        PixelWorld.SetSprite(sr, vertical ? "fpipe_capsule" : "fpipe_capsule_h");
                    }
                    // Капсула — на оси трубы (центр пикселя поперёк хода), вдоль хода — целыми пикселями.
                    float x = vertical ? p.x + 0.5f : Mathf.Round(p.x);
                    float z = vertical ? Mathf.Round(p.y) : p.y + 0.5f;
                    sr.transform.localPosition = new Vector3(x * Px, z * Px, 0f);
                    if (!sr.enabled) sr.enabled = true;
                    v.Capsules++;
                    n++;
                }
            }
            for (int i = n; i < m_CapsulesShown; i++)
                if (m_Capsule[i].enabled) m_Capsule[i].enabled = false;
            m_CapsulesShown = n;
        }
    }
}
