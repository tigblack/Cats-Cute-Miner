using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Барьер ворот по всей ширине шахты (ТЗ 8.4, ART_NAMES §5): рамка и створки — у <see cref="OutpostTargetView"/> ворот,
    /// здесь — пролёты gate_barrier_mid в боковых клетках строки ворот, где нет живого препятствия (столб wall1x1vL/R разрушен
    /// раньше ворот или клетка свободна), со столбами gate_barrier_post на внешней границе крайних клеток. Пока ворота закрыты —
    /// трещины по HP ворот; после SectorOpened пролёты сменяются на gate_barrier_broken. Только вид: барьер задаёт ядро.
    /// </summary>
    public sealed class GateBarrierView : MonoBehaviour
    {
        const float Px = 1f / PixelArt.CellPixels;
        static readonly string[] s_Cracks = { null, "block_crack_1", "block_crack_2", "block_crack_3" };
        const float HitSeconds = 0.12f, HitStepSeconds = 0.03f;

        /// <summary>Порядок ворот (0 — нижние).</summary>
        public int Order { get; private set; }
        public int TargetId { get; private set; }
        public int Row { get; private set; }

        OutpostMap m_Map;
        int[] m_Cols;              // боковые столбцы строки ворот вне рамки
        SpriteRenderer[] m_Mid, m_Crack, m_Post;
        Transform m_Visual;
        bool[] m_Shown;
        bool m_Opened;
        bool m_Applied;
        int m_Stage = -1;
        float m_HitT;
        float m_JitterX;

        public static GateBarrierView Create(Transform parent, OutpostMap map, MapGate gate, OutpostTargetView gateView)
        {
            var go = new GameObject("barrier" + gate.Order);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<GateBarrierView>();
            v.m_Map = map;
            v.Order = gate.Order;
            v.TargetId = gate.TargetId;
            v.Row = gate.Row;
            v.m_Visual = PixelWorld.Visual(go.transform);
            // Branching gates are drawn by the target over their physical Cells. No side spans.
            if (map.Topology != null)
            {
                v.m_Cols = new int[0];
                v.m_Mid = v.m_Crack = v.m_Post = new SpriteRenderer[0];
                v.m_Shown = new bool[0];
                return v;
            }
            int from = gateView != null ? gateView.Column : 0, to = gateView != null ? gateView.Column + gateView.Width : 0;
            int n = 0;
            for (int c = 0; c < map.Width; c++)
                if (c < from || c >= to) n++;
            v.m_Cols = new int[n];
            v.m_Mid = new SpriteRenderer[n];
            v.m_Crack = new SpriteRenderer[n];
            v.m_Post = new SpriteRenderer[n];
            v.m_Shown = new bool[n];
            float feetZ = map.Depth - gate.Row - 1f;
            int order = PixelWorld.OrderAt(feetZ);
            int k = 0;
            for (int c = 0; c < map.Width; c++)
            {
                if (c >= from && c < to) continue;
                v.m_Cols[k] = c;
                v.m_Mid[k] = PixelWorld.Sprite(v.m_Visual, "span", "gate_barrier_mid", new Vector2(c + 0.5f, feetZ), order);
                v.m_Crack[k] = PixelWorld.Sprite(v.m_Visual, "crack", "block_crack_1", new Vector2(c + 0.5f, feetZ), order + 1);
                // Столб — на внешней границе крайней клетки: x = 0 у столбца 0, x = Width у последнего.
                if (c == 0 || c == map.Width - 1)
                    v.m_Post[k] = PixelWorld.Sprite(v.m_Visual, "post", "gate_barrier_post", new Vector2(c == 0 ? 0f : map.Width, feetZ), order + 2);
                v.m_Mid[k].enabled = false;
                v.m_Crack[k].enabled = false;
                if (v.m_Post[k] != null) v.m_Post[k].enabled = false;
                k++;
            }
            return v;
        }

        /// <summary>
        /// Привести пролёты к ядру (каждый кадр, без выделений): видимость — по живой занятости боковых клеток, вид — открыты
        /// ли ворота (GatesOpened &gt; Order), трещины — по стадии HP ворот (<paramref name="gateHpFraction"/>).
        /// </summary>
        public void Sync(OutpostSimulation sim, float gateHpFraction, float dt)
        {
            bool opened = sim.Run.GatesOpened > Order;
            bool changed = !m_Applied || opened != m_Opened;
            m_Applied = true;
            m_Opened = opened;
            int stage = opened ? 0 : (gateHpFraction < 0.2f ? 3 : (gateHpFraction < 0.45f ? 2 : (gateHpFraction < 0.75f ? 1 : 0)));
            bool stageChanged = stage != m_Stage;
            m_Stage = stage;
            for (int k = 0; k < m_Cols.Length; k++)
            {
                int cell = m_Map.Cell(m_Cols[k], Row);
                bool show = !sim.IsOccupied(cell);
                if (show == m_Shown[k] && !changed && !stageChanged) continue;
                m_Shown[k] = show;
                var mid = m_Mid[k];
                if (mid.enabled != show) mid.enabled = show;
                if (show) PixelWorld.SetSprite(mid, opened ? "gate_barrier_broken" : "gate_barrier_mid");
                var post = m_Post[k];
                if (post != null && post.enabled != show) post.enabled = show;
                var crack = m_Crack[k];
                bool cracked = show && stage > 0;
                if (crack.enabled != cracked) crack.enabled = cracked;
                if (cracked) PixelWorld.SetSprite(crack, s_Cracks[stage]);
            }
            if (m_HitT > 0f || m_JitterX != 0f)
            {
                m_HitT = Mathf.Max(0f, m_HitT - dt);
                float x = m_HitT > 0f ? (((int)(m_HitT / HitStepSeconds) & 1) == 0 ? Px : -Px) : 0f;
                if (x != m_JitterX)
                {
                    m_JitterX = x;
                    m_Visual.localPosition = new Vector3(x, 0f, 0f);
                }
            }
        }

        /// <summary>Удар по воротам: пролёты дрожат вместе со створками.</summary>
        public void OnHit()
        {
            if (!m_Opened) m_HitT = HitSeconds;
        }

        /// <summary>Сектор открыт (живое событие): облачко пыли у каждого видимого пролёта; сам вид сменится в Sync.</summary>
        public void PlayOpen(Effects fx, Vector3 boardOrigin)
        {
            if (fx == null) return;
            float z = m_Map.Depth - Row - 0.5f;
            for (int k = 0; k < m_Cols.Length; k++)
                if (m_Shown[k]) fx.Puff(boardOrigin + new Vector3(m_Cols[k] + 0.5f, 0f, z));
        }
    }
}
