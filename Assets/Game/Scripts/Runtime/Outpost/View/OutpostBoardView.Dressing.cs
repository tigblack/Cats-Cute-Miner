using System.Collections.Generic;
using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Обстановка шахты на поле (Docs/Rework/MINE_DRESSING.md): постоянные препятствия (спрайты prop_* в своей группе сортировки,
    /// без коллайдера — по ним не бьют), детали стен в породе над полом и мелкие пятна пола (decal_*: камешки, краска, следы лап,
    /// пятна масла). Пятна выбираются хешем клетки один раз при сборке и ни на что не влияют. Разрушаемый мусор — обычные цели
    /// (<see cref="OutpostTargetView"/>).
    /// </summary>
    public sealed partial class OutpostBoardView
    {
        static readonly string[] s_FloorDecals =
        {
            "decal_pebbles_0", "decal_pebbles_1", "decal_pebbles_2", "decal_pebbles_0", "decal_pebbles_1",
            "decal_paint_0", "decal_paint_1", "decal_paws_0", "decal_stain_0", "decal_stain_1", "decal_board_0",
        };

        /// <summary>Доля клеток пола с пятном, из 100.</summary>
        const int FloorDecalPercent = 14;

        readonly List<Transform> m_PropViews = new List<Transform>();

        /// <summary>Постоянных препятствий на поле (тесты, отчёт).</summary>
        public int PropViewCount => m_PropViews.Count;

        void BuildDressing()
        {
            m_PropViews.Clear();
            if (m_Map.Topology == null) return;
            BuildFloorDecals();
            if (m_Map.Props == null || m_Map.Props.Length == 0) return;
            var root = new GameObject("Dressing").transform;
            root.SetParent(m_Root, false);
            var walls = PixelWorld.Visual(root, "WallDressing");
            foreach (var prop in m_Map.Props)
            {
                if (prop.Wall)
                {
                    int cell = prop.Cells[0];
                    var pos = new Vector2(m_Map.Col(cell) + 0.5f, Depth - m_Map.Row(cell) - 1f);
                    PixelWorld.Sprite(walls, prop.Sprite, prop.Sprite, pos, PixelWorld.FloorDecalOrder + 6);
                    continue;
                }
                // Опора — нижняя кромка footprint (якорь — нижняя левая клетка), как у целей.
                float feetZ = Depth - prop.Row - 1f;
                var go = new GameObject(prop.Sprite);
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(prop.Column + prop.Width / 2f, 0f, feetZ);
                var visual = PixelWorld.Visual(go.transform);
                PixelWorld.Group(visual.gameObject, feetZ);
                PixelWorld.Sprite(visual, "shadow", prop.Width > 1 ? "shadow_l" : "shadow_m", new Vector2(0f, 4f * Px), 0);
                var body = PixelWorld.Sprite(visual, "prop", prop.Sprite, Vector2.zero, 10);
                if (prop.Width == 1) body.flipX = (BoardView.Hash(prop.Column, prop.Row, m_Map.Number) & 2u) != 0u;
                m_PropViews.Add(go.transform);
            }
        }

        void BuildFloorDecals()
        {
            var decals = PixelWorld.Visual(m_Root, "FloorDecals");
            int seed = DecorSeed();
            for (int row = 0; row < Depth; row++)
                for (int col = 0; col < Width; col++)
                {
                    int cell = m_Map.Cell(col, row);
                    if (!m_Map.IsPlayable(cell) || m_Map.CellGateOrder[cell] >= 0 || cell == m_Map.ExitCell) continue;
                    if (m_Map.InitialOccupant[cell] >= 0) continue;
                    uint hash = BoardView.Hash(col, row, seed ^ 0x0DEC);
                    if (hash % 100u >= FloorDecalPercent) continue;
                    string name = s_FloorDecals[(hash / 100u) % (uint)s_FloorDecals.Length];
                    if (!PixelArt.Has(name)) continue;
                    var sr = PixelWorld.Sprite(decals, "decal", name, new Vector2(col + 0.5f, Depth - row - 1f), PixelWorld.FloorDecalOrder + 1);
                    sr.flipX = (hash & 0x100u) != 0u;
                }
        }

        /// <summary>Сектор клетки пола для затемнения: проходимая клетка — свой сектор, под постоянным препятствием — его сектор.</summary>
        int FloorSector(int cell)
        {
            if (m_Map.IsPlayable(cell)) return m_Map.SectorAt(cell);
            return m_Map.IsProp(cell) ? m_Map.Props[m_Map.PropAt[cell]].Sector : -1;
        }

        /// <summary>Клетка породы занята деталью стены обстановки (на ней не ставятся опоры с фонарями).</summary>
        bool HasWallProp(int col, int row)
        {
            if (m_Map.Props == null) return false;
            foreach (var p in m_Map.Props)
                if (p.Wall && p.Column == col && p.Row == row) return true;
            return false;
        }
    }
}
