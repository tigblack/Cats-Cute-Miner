using System.Collections.Generic;
using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Отладочный слой поля (ТЗ 19, RUNTIME_SPEC §8): пути котиков (точки оставшихся клеток цветом котика), закреплённые
    /// рабочие клетки, партии груза (зелёная — есть свободные порции, оранжевая — всё зарезервировано), места склада
    /// (красное — резерв, бирюзовое — обслуживание), очередь у точки приёма и линия барьера закрытых ворот.
    /// Включается статическим флагом <see cref="Enabled"/> (DevTools, только debug-профиль); обновляется 10 раз в секунду
    /// из пула прямоугольников px_white — выделения только при росте пула. Ничего не меняет в ядре.
    /// </summary>
    public sealed class OutpostDebugOverlay : MonoBehaviour
    {
        /// <summary>Показывать слой (выставляет OutpostDevPanel). Выключенный слой не стоит ничего.</summary>
        public static bool Enabled;

        const float Px = 1f / PixelArt.CellPixels;
        const float RefreshSeconds = 0.1f;
        const int Order = PixelWorld.FogOrder + 10;

        static readonly Color[] s_WorkerColors =
        {
            PixelPalette.Gold4, PixelPalette.Teal5, PixelPalette.Pink2, PixelPalette.Green5,
            PixelPalette.Blue6, PixelPalette.Amber3, PixelPalette.Purple5, PixelPalette.Red4,
        };
        static readonly Color FreeCargo = new Color(0.3f, 0.95f, 0.4f, 0.45f);
        static readonly Color ReservedCargo = new Color(1f, 0.6f, 0.15f, 0.45f);
        static readonly Color SlotReserved = new Color(1f, 0.25f, 0.25f, 0.4f);
        static readonly Color SlotServing = new Color(0.2f, 0.9f, 0.9f, 0.45f);
        static readonly Color BarrierColor = new Color(1f, 0.15f, 0.15f, 0.85f);
        static readonly Color QueueColor = new Color(1f, 1f, 1f, 0.8f);

        Transform m_Visual;
        readonly List<SpriteRenderer> m_Pool = new List<SpriteRenderer>();
        int m_Used;
        float m_Next;
        Vector2 m_PxSize = new Vector2(Px, Px);
        Vector2 m_Pivot = new Vector2(0.5f, 0.5f);
        bool m_Hidden;

        public static OutpostDebugOverlay Create(Transform parent)
        {
            var go = new GameObject("DebugOverlay");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<OutpostDebugOverlay>();
            v.m_Visual = PixelWorld.Visual(go.transform, "debug");
            var s = PixelArt.World("px_white");
            if (s != null && s.pixelsPerUnit > 0f && s.rect.width > 0f && s.rect.height > 0f)
            {
                v.m_PxSize = new Vector2(s.rect.width / s.pixelsPerUnit, s.rect.height / s.pixelsPerUnit);
                v.m_Pivot = new Vector2(s.pivot.x / s.rect.width, s.pivot.y / s.rect.height);
            }
            return v;
        }

        public static Color WorkerColor(int workerId) => s_WorkerColors[(workerId % s_WorkerColors.Length + s_WorkerColors.Length) % s_WorkerColors.Length];

        /// <summary>Перерисовать (не чаще 10 Гц) или спрятать, если слой выключен.</summary>
        public void Sync(OutpostSimulation sim, OutpostMap map)
        {
            if (!Enabled || sim == null || map == null)
            {
                if (!m_Hidden) Hide();
                return;
            }
            float t = Time.unscaledTime;
            if (!m_Hidden && t < m_Next) return;
            m_Next = t + RefreshSeconds;
            m_Hidden = false;
            m_Used = 0;
            var run = sim.Run;

            // Барьер: граница между строкой ворот и строкой над ней, во всю ширину.
            int barrier = sim.BarrierRow;
            if (barrier > 0) Rect(new Vector2(map.Width / 2f, map.Depth - barrier), new Vector2(map.Width, 2f * Px), BarrierColor);

            // Партии груза.
            for (int i = 0; i < run.Batches.Count; i++)
            {
                var bt = run.Batches[i];
                var c = Cell(map, bt.Cell);
                Rect(c, new Vector2(0.8f, 0.8f), bt.FreeCount() > 0 ? FreeCargo : ReservedCargo);
            }

            // Места склада.
            for (int s = 0; s < run.Slots.Length && s < map.DepotCells.Length; s++)
            {
                int wid = run.Slots[s].WorkerId;
                if (wid == 0) continue;
                var w = sim.WorkerById(wid);
                bool serving = w != null && w.Mode == WorkerMode.Unloading;
                Rect(Cell(map, map.DepotCells[s]), new Vector2(0.9f, 0.9f), serving ? SlotServing : SlotReserved);
            }

            // Очередь: по точке на ожидающего у точки приёма.
            var intake = Cell(map, map.IntakeCell);
            for (int q = 0; q < run.Queue.Count; q++)
                Rect(intake + new Vector2(-0.4f + (q % 8) * 0.1f, -0.42f - (q / 8) * 0.1f), new Vector2(3f * Px, 3f * Px), QueueColor);

            // Рабочие клетки и пути.
            for (int i = 0; i < run.Workers.Count; i++)
            {
                var w = run.Workers[i];
                var col = WorkerColor(w.Id);
                if (w.WorkCell >= 0)
                {
                    var wc = col;
                    wc.a = 0.35f;
                    Rect(Cell(map, w.WorkCell), new Vector2(0.86f, 0.86f), wc);
                }
                if (w.Path == null) continue;
                var prev = Cell(map, w.Cell);
                for (int k = w.PathIndex; k < w.Path.Length; k++)
                {
                    var next = Cell(map, w.Path[k]);
                    // Отрезок ребра — тонкий прямоугольник от клетки к клетке, конец пути — крупная точка.
                    var mid = (prev + next) * 0.5f;
                    var d = next - prev;
                    var size = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? new Vector2(Mathf.Abs(d.x) + 2f * Px, 2f * Px) : new Vector2(2f * Px, Mathf.Abs(d.y) + 2f * Px);
                    Rect(mid + new Vector2(0f, (w.Id % 4) * Px), size, col);
                    prev = next;
                }
                Rect(prev, new Vector2(5f * Px, 5f * Px), col);
            }

            for (int i = m_Used; i < m_Pool.Count; i++)
                if (m_Pool[i].enabled) m_Pool[i].enabled = false;
        }

        static Vector2 Cell(OutpostMap map, int cell) => new Vector2(map.Col(cell) + 0.5f, map.Depth - map.Row(cell) - 0.5f);

        void Rect(Vector2 center, Vector2 size, Color color)
        {
            SpriteRenderer sr;
            if (m_Used < m_Pool.Count) sr = m_Pool[m_Used];
            else
            {
                sr = PixelWorld.Sprite(m_Visual, "dbg", "px_white", Vector2.zero, Order);
                m_Pool.Add(sr);
            }
            m_Used++;
            var t = sr.transform;
            // Позиция с учётом pivot px_white: центр прямоугольника — в center (как BoardView.CenteredPos).
            t.localPosition = new Vector3(center.x + (m_Pivot.x - 0.5f) * size.x, center.y + (m_Pivot.y - 0.5f) * size.y, 0f);
            t.localScale = new Vector3(size.x / m_PxSize.x, size.y / m_PxSize.y, 1f);
            if (sr.color != color) sr.color = color;
            if (!sr.enabled) sr.enabled = true;
        }

        void Hide()
        {
            m_Hidden = true;
            m_Used = 0;
            for (int i = 0; i < m_Pool.Count; i++)
                if (m_Pool[i].enabled) m_Pool[i].enabled = false;
        }
    }
}
