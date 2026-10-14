using System.Collections.Generic;
using CatMiners.Core.Outpost;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatMiners.Runtime.Outpost
{
    public sealed partial class OutpostBoardView
    {
        // Geometry stays on the floor plane: cutaway faces cannot occlude workers in narrow tunnels.
        readonly List<Mesh> m_TopologyMeshes = new List<Mesh>();
        Material m_TopologyOverlayMaterial;
        MeshRenderer[] m_SectorFog;
        MaterialPropertyBlock m_TopologyTint;

        sealed class TileBatch
        {
            readonly List<Vector3> m_Vertices = new List<Vector3>();
            readonly List<Vector2> m_Uvs = new List<Vector2>();
            readonly List<int> m_Indices = new List<int>();
            readonly Dictionary<string, Rect> m_UvCache = new Dictionary<string, Rect>();
            public bool Empty => m_Vertices.Count == 0;

            public void Add(string tile, float x, float z, float width = 1f, float height = 1f)
            {
                if (!m_UvCache.TryGetValue(tile, out var uv))
                {
                    uv = PixelArt.Uv(tile);
                    m_UvCache.Add(tile, uv);
                }
                var atlas = PixelArt.Atlas;
                float eu = atlas != null ? 0.01f / atlas.width : 0f;
                float ev = atlas != null ? 0.01f / atlas.height : 0f;
                int i = m_Vertices.Count;
                m_Vertices.Add(new Vector3(x, 0f, z));
                m_Vertices.Add(new Vector3(x, 0f, z + height));
                m_Vertices.Add(new Vector3(x + width, 0f, z + height));
                m_Vertices.Add(new Vector3(x + width, 0f, z));
                m_Uvs.Add(new Vector2(uv.xMin + eu, uv.yMin + ev));
                m_Uvs.Add(new Vector2(uv.xMin + eu, uv.yMax - ev));
                m_Uvs.Add(new Vector2(uv.xMax - eu, uv.yMax - ev));
                m_Uvs.Add(new Vector2(uv.xMax - eu, uv.yMin + ev));
                m_Indices.Add(i); m_Indices.Add(i + 1); m_Indices.Add(i + 2);
                m_Indices.Add(i); m_Indices.Add(i + 2); m_Indices.Add(i + 3);
            }

            public Mesh Make(string name)
            {
                var mesh = new Mesh { name = name };
                if (m_Vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(m_Vertices);
                mesh.SetUVs(0, m_Uvs);
                mesh.SetTriangles(m_Indices, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        MeshRenderer TopologyBatch(TileBatch batch, string name, int order, bool overlay = false)
        {
            if (batch.Empty) return null;
            var mesh = batch.Make(name);
            m_TopologyMeshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(m_Root, false);
            go.transform.localPosition = new Vector3(0f, overlay ? 0.01f : -0.02f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = overlay ? TopologyOverlayMaterial() : PixelArt.MeshMaterial;
            renderer.sortingOrder = order;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        Material TopologyOverlayMaterial()
        {
            if (m_TopologyOverlayMaterial != null) return m_TopologyOverlayMaterial;
            // MeshMaterial is URP Unlit, not the sprite shader (which requires SpriteRenderer state).
            var mat = new Material(PixelArt.MeshMaterial) { name = "outpost_topology_overlay" };
            if (!mat.HasProperty("_Surface"))
            {
                var shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
                if (shader != null) mat.shader = shader;
                mat.mainTexture = PixelArt.Atlas;
            }
            else
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.SetShaderPassEnabled("ShadowCaster", false);
            }
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            m_TopologyOverlayMaterial = mat;
            return mat;
        }

        void TintTopology(MeshRenderer renderer, Color color)
        {
            if (m_TopologyTint == null) m_TopologyTint = new MaterialPropertyBlock();
            m_TopologyTint.Clear();
            m_TopologyTint.SetColor("_BaseColor", color);
            m_TopologyTint.SetColor("_Color", color);
            renderer.SetPropertyBlock(m_TopologyTint);
        }

        void ReleaseTopologyMeshes()
        {
            foreach (var mesh in m_TopologyMeshes) if (mesh != null) Destroy(mesh);
            m_TopologyMeshes.Clear();
            if (m_TopologyOverlayMaterial != null) Destroy(m_TopologyOverlayMaterial);
            m_TopologyOverlayMaterial = null;
            m_SectorFog = null;
        }

        bool TunnelAt(int col, int row)
        {
            return col >= 0 && col < Width && row >= 0 && row < m_Map.Rows && m_Map.IsPlayable(m_Map.Cell(col, row));
        }

        void BuildTopologyFloor()
        {
            var ground = new TileBatch();
            var edges = new TileBatch();
            var shadows = new TileBatch();
            var noGateRows = new bool[Depth];
            for (int row = -TopRows; row <= WallRow + BelowRows; row++)
            {
                for (int col = -1 - SideCols; col <= Width + SideCols; col++)
                {
                    bool mine = col >= 0 && col < Width && row >= 0 && row < Depth;
                    uint hash = BoardView.Hash(col, row, m_Map.Number);
                    string tile = TileName(col, row, Width, Depth, m_Map.Number, noGateRows, out bool camp);
                    bool wide = m_Map.Topology.Version != MineTopologyCatalog.LegacyVersion;
                    bool campRock = wide && row >= Depth && col >= 0 && col < Width && Mathf.Abs(col - Width / 2) > 4;
                    bool rock = mine && !TunnelAt(col, row) || campRock;
                    if (rock) tile = Optional(s_WallTops[hash % (uint)s_WallTops.Length], "wall_top");
                    else if (mine && m_Map.CellGateOrder[m_Map.Cell(col, row)] >= 0) tile = "floor_gate";
                    ground.Add(camp && !campRock ? tile : PixelTheme.Name(tile), col, Depth - row - 1f);
                    if (!rock) continue;
                    float z = Depth - row - 1f;
                    // Edge detail is contained in the rock cell. Only the thin shadow spills onto floor.
                    if (TunnelAt(col - 1, row))
                    {
                        edges.Add(PixelTheme.Name("wall_edge_r_0"), col, z, 4f * Px, 1f);
                        shadows.Add("px_white", col - 2f * Px, z, 2f * Px, 1f);
                    }
                    if (TunnelAt(col + 1, row))
                    {
                        edges.Add(PixelTheme.Name("wall_edge_l_0"), col + 1f - 4f * Px, z, 4f * Px, 1f);
                        shadows.Add("px_white", col + 1f, z, 2f * Px, 1f);
                    }
                    if (TunnelAt(col, row + 1))
                    {
                        edges.Add(PixelTheme.Name("wall_top_edge"), col, z, 1f, 6f * Px);
                        shadows.Add("px_white", col, z - 3f * Px, 1f, 3f * Px);
                    }
                    if (TunnelAt(col, row - 1))
                    {
                        edges.Add(PixelTheme.Name("wall_bottom"), col, z + 1f - 3f * Px, 1f, 3f * Px);
                        shadows.Add("px_white", col, z + 1f, 1f, 2f * Px);
                    }
                }
            }
            TopologyBatch(ground, "TopologyFloorAndRock", PixelWorld.FloorOrder);
            TopologyBatch(edges, "RockCutawayEdges", PixelWorld.FloorOrder + 1);
            var shadow = TopologyBatch(shadows, "RockDropShadows", PixelWorld.FloorOrder + 2, true);
            if (shadow != null) TintTopology(shadow, new Color(0.04f, 0.03f, 0.06f, 0.32f));
        }

        void BuildTopologyDecor()
        {
            var decor = PixelWorld.Visual(m_Root, "TunnelDecor");
            var lanterns = new List<LanternProp>();
            var occupied = new List<Vector2>();
            m_Glints.Clear();
            m_Gears = new GearProp[0];
            for (int row = Depth - 1; row >= 0; row--)
            {
                for (int col = 0; col < Width; col++)
                {
                    if (!TunnelAt(col, row)) continue;
                    int cell = m_Map.Cell(col, row);
                    if (m_Map.CellGateOrder[cell] >= 0) continue;
                    int neighbours = (TunnelAt(col - 1, row) ? 1 : 0) + (TunnelAt(col + 1, row) ? 1 : 0)
                        + (TunnelAt(col, row - 1) ? 1 : 0) + (TunnelAt(col, row + 1) ? 1 : 0);
                    bool leftRock = col > 0 && !TunnelAt(col - 1, row);
                    bool rightRock = col + 1 < Width && !TunnelAt(col + 1, row);
                    bool backRock = row > 0 && !TunnelAt(col, row - 1);
                    if (!leftRock && !rightRock && !backRock) continue;
                    uint hash = BoardView.Hash(col, row, DecorSeed());
                    if (neighbours < 3 && hash % 5u != 0) continue;
                    var center = new Vector2(col + 0.5f, Depth - row - 0.5f);
                    bool near = false;
                    foreach (var other in occupied) if ((other - center).sqrMagnitude < 9f) { near = true; break; }
                    if (near) continue;
                    occupied.Add(center);
                    // Supports sit within structural rock, beside the route; keep their sort below cats.
                    bool side = leftRock || rightRock;
                    float x = side ? (leftRock ? col - 6f * Px : col + 1f + 6f * Px) : col + 0.5f;
                    float z = side ? Depth - row - 1f : Depth - row + 2f * Px;
                    var post = Decor(decor, PixelTheme.Name("beam_post"), x, z);
                    post.flipX = rightRock && !leftRock;
                    post.sortingOrder = PixelWorld.FloorDecalOrder + 3;
                    var lamp = Lantern(decor, x, z + 0.55f, "fx_glow_lantern", center.x, center.y);
                    lamp.Lamp.sortingOrder = PixelWorld.FloorDecalOrder + 5;
                    lamp.Glow.sortingOrder = PixelWorld.FloorDecalOrder + 4;
                    lamp.Glow.transform.localScale = new Vector3(0.55f, 0.55f, 1f);
                    lanterns.Add(lamp);
                }
            }
            m_Lanterns = lanterns.ToArray();
            if (m_Map.ExitCell < 0 || m_Map.ExitCell >= Width * Depth) return;
            var exit = CellCenterLocal(m_Map.ExitCell);
            // Marker follows the remapped exit rather than the old bounding wall.
            int order = PixelWorld.FloorDecalOrder + 6;
            var marker = PixelWorld.Sprite(decor, "exit", PixelTheme.Name("exit_base"), new Vector2(exit.x, exit.z - 0.4f), order);
            marker.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
            m_ExitGlow = PixelWorld.Sprite(decor, "exitGlow", "exit_glow", new Vector2(exit.x, exit.z - 0.4f), order + 1);
            m_ExitBoards = PixelWorld.Sprite(decor, "exitBoards", PixelTheme.Name("exit_boards"), new Vector2(exit.x, exit.z - 0.4f), order + 2);
            m_ExitGlow.transform.localScale = m_ExitBoards.transform.localScale = marker.transform.localScale;
            m_ExitGlow.enabled = false;
        }

        void BuildTopologyFog()
        {
            int sectors = m_Map.Gates.Length + 1;
            m_Fog = new SpriteRenderer[sectors];
            m_SectorFog = new MeshRenderer[sectors];
            m_FogLowOrder = new int[sectors];
            m_FogLevel = new float[sectors];
            m_FogFading = new bool[sectors];
            m_FogFadingCount = 0;
            var dark = PixelTheme.Dark;
            m_FogColor = new Color(dark.r, dark.g, dark.b, FogAlpha);
            var batches = new TileBatch[sectors];
            for (int k = 1; k < sectors; k++) batches[k] = new TileBatch();
            for (int row = 0; row < Depth; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    int cell = m_Map.Cell(col, row);
                    int sector = m_Map.SectorAt(cell);
                    if (!m_Map.IsPlayable(cell))
                    {
                        // Rock bordering an open chamber remains visible. Enclosed rock needs no fog.
                        sector = sectors;
                        for (int d = 0; d < 4; d++)
                        {
                            int x = col + (d == 0 ? -1 : d == 1 ? 1 : 0);
                            int r = row + (d == 2 ? -1 : d == 3 ? 1 : 0);
                            if (TunnelAt(x, r)) sector = Mathf.Min(sector, m_Map.SectorAt(m_Map.Cell(x, r)));
                        }
                    }
                    // The first closed gate must remain readable and pickable from its approach sector.
                    int gateOrder = m_Map.CellGateOrder[cell];
                    if (gateOrder >= 0) sector = gateOrder;
                    if (sector <= 0 || sector >= sectors) continue;
                    batches[sector].Add("px_white", col, Depth - row - 1f);
                }
            }
            for (int k = 1; k < sectors; k++)
            {
                m_SectorFog[k] = TopologyBatch(batches[k], "SectorMask" + k, PixelWorld.FogOrder, true);
                if (m_SectorFog[k] != null) { m_FogLevel[k] = 1f; TintTopology(m_SectorFog[k], m_FogColor); }
            }
        }

        bool HasFog(int sector) => m_Map.Topology != null ? m_SectorFog[sector] != null : m_Fog[sector] != null;

        void SetTopologyFog(int sector, float level)
        {
            var renderer = m_SectorFog[sector];
            if (renderer == null) return;
            renderer.enabled = level > 0f;
            var color = m_FogColor;
            color.a = FogAlpha * level;
            TintTopology(renderer, color);
        }

        int TopologyCatMinOrder(int cell, int targetId)
        {
            var target = m_Map.Target(targetId);
            if (target == null || !target.IsGate || target.Cells == null) return int.MinValue;
            // WorkCells are in the prior sector, so row-based gate sorting no longer applies.
            if (target.WorkCells != null)
                foreach (int approach in target.WorkCells)
                    if (approach == cell)
                    {
                        var view = TargetViewOf(targetId);
                        return view != null ? PixelWorld.OrderAt(view.FeetLocal.z) + 1 : int.MinValue;
                    }
            return int.MinValue;
        }
    }
}
