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
        // Native palette and pixel density match the mine. These never create mining targets.
        static readonly string[] s_SalvageNarrow =
        {
            "prop_winch_0", "prop_reel_0", "prop_reel_1", "prop_support_0",
            "prop_support_1", "prop_support_2", "prop_tower_0", "prop_statue_1",
        };
        static readonly string[] s_SalvageWide =
        {
            "prop_minecart_0", "prop_minecart_1", "prop_minecart_2",
            "prop_drill_0", "prop_hopper_0", "prop_hopper_1",
        };

        string SalvageSprite(uint hash, bool wide, string fallback)
        {
            var pool = wide ? s_SalvageWide : s_SalvageNarrow;
            string sprite = pool[(hash / 17u) % (uint)pool.Length];
            return PixelArt.Has(sprite) ? sprite : fallback;
        }

        sealed class SceneryNiche
        {
            public RectInt Cells;
            public int Sector;
            public string Sprite;
            public bool Flip;
        }
        readonly List<SceneryNiche> m_SceneryNiches = new List<SceneryNiche>();
        readonly Dictionary<int, int> m_SceneryFloorSectors = new Dictionary<int, int>();
        static readonly Color SceneryTint = new Color(0.78f, 0.82f, 0.9f, 1f);

        // Rendering only: recesses belong to stationary equipment, never to worker navigation or target placement.
        void PrepareSceneryNiches()
        {
            m_SceneryNiches.Clear();
            m_SceneryFloorSectors.Clear();
            if (m_Map.Topology == null) return;
            if (m_Map.Topology.Layout == MiaCorridor.Layout)
            {
                AddSceneryNiche(new RectInt(3, 3, 2, 2), 0);
                AddSceneryNiche(new RectInt(3, 7, 2, 2), 0);
                AddSceneryNiche(new RectInt(8, 5, 2, 2), 0);
                return;
            }
            int limit = Mathf.Clamp(Depth / 8 + 1, 2, 8);
            int wideIndex = m_Map.Number, narrowIndex = m_Map.Number;
            for (int row = Depth - 2; row > 1 && m_SceneryNiches.Count < limit; row--)
                for (int col = 1; col < Width - 1 && m_SceneryNiches.Count < limit; col++)
                {
                    if (m_Map.Final != null && row <= m_Map.Final.AddedRows + 1) continue;
                    int cell = m_Map.Cell(col, row);
                    if (!m_Map.IsPlayable(cell) || m_Map.CellGateOrder[cell] >= 0) continue;
                    uint hash = BoardView.Hash(col, row, DecorSeed() ^ 0x25CE);
                    if (hash % 7u != 0) continue;
                    bool close = false;
                    foreach (var existing in m_SceneryNiches)
                        if ((new Vector2(col, row) - existing.Cells.center).sqrMagnitude < 16f) { close = true; break; }
                    if (close) continue;
                    bool left = (hash & 1u) == 0;
                    for (int side = 0; side < 2; side++, left = !left)
                    {
                        bool added = false;
                        for (int width = 2; width >= 1; width--)
                        {
                            var cells = new RectInt(left ? col - width : col + 1, row - 1, width, 2);
                            if (!AddSceneryNiche(cells, m_Map.SectorAt(cell))) continue;
                            var niche = m_SceneryNiches[m_SceneryNiches.Count - 1];
                            var palette = width > 1 ? s_SalvageWide : s_SalvageNarrow;
                            int index = width > 1 ? wideIndex++ : narrowIndex++;
                            niche.Sprite = palette[index % palette.Length];
                            niche.Flip = left;
                            added = true;
                            break;
                        }
                        if (added) break;
                    }
                }
        }

        bool AddSceneryNiche(RectInt cells, int sector)
        {
            foreach (var p in cells.allPositionsWithin)
            {
                if (p.x < 1 || p.x >= Width - 1 || p.y < 0 || p.y >= Depth) return false;
                int cell = m_Map.Cell(p.x, p.y);
                if (m_Map.IsFloor(cell) || m_SceneryFloorSectors.ContainsKey(cell) || HasWallProp(p.x, p.y)) return false;
            }
            m_SceneryNiches.Add(new SceneryNiche { Cells = cells, Sector = sector });
            foreach (var p in cells.allPositionsWithin) m_SceneryFloorSectors.Add(m_Map.Cell(p.x, p.y), sector);
            return true;
        }

        void SceneryEquipment(Transform parent, string sprite, float x, float row, float maxWidth, float scale = 1f, bool flip = false)
        {
            float z = Depth - row - 1f;
            var shadow = Decor(parent, maxWidth > 1f ? "shadow_l" : "shadow_m", x, z + 0.08f);
            shadow.sortingOrder = PixelWorld.FloorDecalOrder + 4;
            shadow.color = new Color(1f, 1f, 1f, 0.6f);
            shadow.transform.localScale = new Vector3(maxWidth > 1f ? 0.8f : 0.75f, 0.65f, 1f);
            var view = Decor(parent, sprite, PixelWorld.Snap(x), PixelWorld.Snap(z));
            view.sortingOrder = PixelWorld.FloorDecalOrder + 5;
            view.color = SceneryTint;
            view.flipX = flip;
            float fit = Mathf.Min(scale, maxWidth / Mathf.Max(0.1f, view.sprite.bounds.size.x));
            view.transform.localScale = new Vector3(fit, fit, 1f);
        }

        void BuildAmbientNicheScenery(Transform decor)
        {
            var scenery = PixelWorld.Visual(m_Root, "EquipmentNiches");
            foreach (var niche in m_SceneryNiches)
                SceneryEquipment(scenery, niche.Sprite, niche.Cells.center.x, niche.Cells.yMax - 1,
                    niche.Cells.width * 0.9f, 0.9f, niche.Flip);
        }

        void BuildMiaScenery(Transform decor, List<LanternProp> lanterns)
        {
            var scenery = PixelWorld.Visual(m_Root, "MiaMineScenery");
            // Three small work/storage bays, with matching native art and room for each object's feet.
            SceneryEquipment(scenery, "prop_reel_0", 3.9f, 4f, 0.9f, 0.9f);
            SceneryEquipment(scenery, "barrel", 4.65f, 4f, 0.45f);
            SceneryEquipment(scenery, "wall_rack_0", 3.85f, 3f, 1f, 0.8f);
            SceneryEquipment(scenery, "prop_minecart_0", 4f, 8f, 1.75f, 0.95f);
            SceneryEquipment(scenery, "crate", 3.5f, 7f, 0.45f);
            SceneryEquipment(scenery, "prop_drill_0", 9f, 6f, 1.8f, 0.85f);
            SceneryEquipment(scenery, "sack", 8.25f, 5.2f, 0.45f);
            foreach (var position in new[] { new Vector2(4.8f, 3f), new Vector2(8.1f, 5f) })
            {
                float z = Depth - position.y - 1f;
                var lamp = Lantern(decor, position.x, z + 0.5f, "fx_glow_lantern", position.x, z);
                lamp.Lamp.sortingOrder = PixelWorld.FloorDecalOrder + 7;
                lamp.Glow.sortingOrder = PixelWorld.FloorDecalOrder + 6;
                lamp.Glow.transform.localScale = new Vector3(0.45f, 0.45f, 1f);
                lanterns.Add(lamp);
            }
        }

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
                // Клетка спасения первой сессии — своими слоями (Mia внутри), см. OutpostBoardView.Rescue.
                if (TryBuildRescueCage(root, prop)) continue;
                // Опора — нижняя кромка footprint (якорь — нижняя левая клетка), как у целей.
                float feetZ = Depth - prop.Row - 1f;
                var go = new GameObject(prop.Sprite);
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(prop.Column + prop.Width / 2f, 0f, feetZ);
                var visual = PixelWorld.Visual(go.transform);
                PixelWorld.Group(visual.gameObject, feetZ);
                PixelWorld.Sprite(visual, "shadow", prop.Width > 1 ? "shadow_l" : "shadow_m", new Vector2(0f, 4f * Px), 0);
                uint variation = BoardView.Hash(prop.Column, prop.Row, DecorSeed() ^ 0x51A6);
                string sprite = variation % 5u == 0u ? prop.Sprite : SalvageSprite(variation, prop.Width > 1, prop.Sprite);
                var body = PixelWorld.Sprite(visual, "prop", sprite, Vector2.zero, 10, SceneryTint);
                float fit = Mathf.Min(1f, prop.Width * 0.94f / body.sprite.bounds.size.x);
                body.transform.localScale = new Vector3(fit, fit, 1f);
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
            if (m_SceneryFloorSectors.TryGetValue(cell, out int nicheSector)) return nicheSector;
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
