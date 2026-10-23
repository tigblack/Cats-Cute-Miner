using System.Collections.Generic;
using CatMiners.Core.Outpost;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatMiners.Runtime.Outpost
{
    // Пол, стены, декор стен и затемнение закрытых секторов: логика BoardView (тот же хеш вариантов, DecoSeed, оформление
    // PixelTheme), но площадка лагеря — строки Depth..Depth+2 с тайлами camp_*, ниже — двор построек и нижняя стена (ART_NAMES §3.2).
    public sealed partial class OutpostBoardView
    {
        /// <summary>Столбцов стены за краевыми (−1 и Width) с каждой стороны.</summary>
        const int SideCols = 3;
        /// <summary>Строк задней стены над строкой 0 и грунта под нижней стеной лагеря.</summary>
        const int TopRows = 8, BelowRows = 4;
        const float FogAlpha = 0.62f;
        const float RevealSeconds = 0.8f;
        const float RevealGlowRadius = 3.5f, RevealGlowSeconds = 1.6f;
        const float BeamInsetPx = 16f, LanternArmPx = 11f, LanternHangPx = 22f;
        const float LanternGlowInsetPx = 7f, LanternGlowLiftPx = 9f;
        const float GearFrameSeconds = 0.2f;
        const int GearMeshFrames = 2;
        const float GearPitchPx = 19f;
        const float GearInsetPx = 12f;
        const float GlintMinSeconds = 1.6f, GlintMaxSeconds = 4.5f;

        static readonly string[] s_Floors = { "floor_0", "floor_1", "floor_2", "floor_3" };
        static readonly string[] s_WallSides = { "wall_side_0", "wall_side_1", "wall_side_2", "wall_side_3" };
        static readonly string[] s_WallTops = { "wall_top", "wall_top", "wall_top_1", "wall_top_2", "wall_top_3" };
        static readonly string[] s_WallEdgesL = { "wall_edge_l_0", "wall_edge_l_1" };
        static readonly string[] s_WallEdgesR = { "wall_edge_r_0", "wall_edge_r_1" };
        static readonly string[] s_CampFloors = { "camp_floor_0", "camp_floor_1", "camp_floor_2", "camp_floor_3" };
        static readonly string[] s_CampGround = { "camp_ground_0", "camp_ground_1" };
        static readonly string[] s_LanternFrames = { "lantern_0", "lantern_1" };
        static readonly string[] s_Props = { "barrel", "crate", "sack" };
        static readonly string[] s_GearFrames =
        {
            "gear_copper_0", "gear_copper_1", "gear_copper_2", "gear_copper_3",
            "gear_copper_4", "gear_copper_5", "gear_copper_6", "gear_copper_7",
        };

        struct LanternProp
        {
            public SpriteRenderer Lamp;
            public SpriteRenderer Glow;
            public float Next;
            public int Frame;
        }

        struct GearProp
        {
            public SpriteRenderer Gear;
            public int Phase;
            public bool Reverse;
        }

        Mesh m_FloorMesh;
        LanternProp[] m_Lanterns;
        GearProp[] m_Gears;
        int m_GearFrame = -1;
        readonly List<(Vector3 Pos, int Row)> m_Glints = new List<(Vector3, int)>();
        float m_NextGlint;
        SpriteRenderer m_ExitBoards, m_ExitGlow;
        int m_ExitShown = -1;

        SpriteRenderer[] m_Fog;
        float[] m_FogLevel;
        int[] m_FogLowOrder;
        bool[] m_FogFading;
        int m_FogFadingCount;
        Color m_FogColor;
        /// <summary>Сектор строки карты: число ворот ниже неё (строка ворот — нижнему сектору, ТЗ 8.4).</summary>
        int[] m_RowSector;

        // ───────────── Пол и стены ─────────────

        /// <summary>
        /// Пол и стены одним мешем с UV атласа (как BoardView): плиты floor_0…3, порог floor_gate в строках ворот, края стен,
        /// задняя стена над строкой 0; под картой — площадка лагеря (camp_edge_t, camp_floor_*, camp_edge_b), двор camp_ground_*,
        /// нижняя стена wall_bottom с углами; по бокам лагеря — wall_edge_l/r. Лагерь темами не перекрашивается.
        /// </summary>
        void BuildFloorAndWalls()
        {
            if (m_Map.Topology != null) { BuildTopologyFloor(); return; }
            int width = m_Map.Width, depth = m_Map.Depth, zone = m_Map.Number;
            var gateRow = new bool[depth];
            foreach (var g in m_Map.Gates)
                if (g.Row >= 0 && g.Row < depth) gateRow[g.Row] = true;

            int colMin = -1 - SideCols, colMax = width + SideCols;
            int rowMin = -TopRows, rowMax = WallRow + BelowRows;
            int cols = colMax - colMin + 1, rows = rowMax - rowMin + 1;
            int quads = cols * rows;
            var verts = new Vector3[quads * 4];
            var uvs = new Vector2[quads * 4];
            var colors = new Color32[quads * 4];
            var tris = new int[quads * 6];
            var uvCache = new Dictionary<string, Rect>();
            var atlas = PixelArt.Atlas;
            float epsU = atlas != null ? 0.01f / atlas.width : 0f, epsV = atlas != null ? 0.01f / atlas.height : 0f;
            var white = new Color32(255, 255, 255, 255);

            int q = 0;
            for (int r = rowMin; r <= rowMax; r++)
            {
                for (int c = colMin; c <= colMax; c++, q++)
                {
                    string name = TileName(c, r, width, depth, zone, gateRow, out bool camp);
                    if (!camp) name = PixelTheme.Name(name);
                    if (!uvCache.TryGetValue(name, out var uv))
                    {
                        uv = PixelArt.Uv(name);
                        uvCache[name] = uv;
                    }
                    float x0 = c, x1 = c + 1, z0 = depth - r - 1, z1 = depth - r;
                    float u0 = uv.xMin + epsU, u1 = uv.xMax - epsU, v0 = uv.yMin + epsV, v1 = uv.yMax - epsV;
                    int i = q * 4;
                    verts[i] = new Vector3(x0, 0f, z0);
                    verts[i + 1] = new Vector3(x0, 0f, z1);
                    verts[i + 2] = new Vector3(x1, 0f, z1);
                    verts[i + 3] = new Vector3(x1, 0f, z0);
                    uvs[i] = new Vector2(u0, v0);
                    uvs[i + 1] = new Vector2(u0, v1);
                    uvs[i + 2] = new Vector2(u1, v1);
                    uvs[i + 3] = new Vector2(u1, v0);
                    colors[i] = colors[i + 1] = colors[i + 2] = colors[i + 3] = white;
                    int t = q * 6;
                    tris[t] = i;
                    tris[t + 1] = i + 1;
                    tris[t + 2] = i + 2;
                    tris[t + 3] = i;
                    tris[t + 4] = i + 2;
                    tris[t + 5] = i + 3;
                }
            }

            m_FloorMesh = new Mesh { name = "floor_" + m_Map.ZoneId };
            if (verts.Length > 65000) m_FloorMesh.indexFormat = IndexFormat.UInt32;
            m_FloorMesh.vertices = verts;
            m_FloorMesh.uv = uvs;
            m_FloorMesh.colors32 = colors;
            m_FloorMesh.triangles = tris;
            m_FloorMesh.RecalculateBounds();

            var go = new GameObject("Floor");
            go.transform.SetParent(m_Root, false);
            go.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = m_FloorMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = PixelArt.MeshMaterial;
            mr.sortingOrder = PixelWorld.FloorOrder;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>Строка нижней стены лагеря (ART_NAMES §3.2: Depth + 6).</summary>
        int WallRow => m_Map.Depth + BaseRows + StripRows;

        static string TileName(int c, int r, int width, int depth, int zone, bool[] gateRow, out bool camp)
        {
            camp = false;
            bool inCols = c >= 0 && c < width;
            uint h = BoardView.Hash(c, r, zone);
            int wallRow = depth + BaseRows + StripRows;
            if (r < 0)
            {
                if (inCols) return r == -1 ? "wall_top_edge" : Optional(s_WallTops[h % (uint)s_WallTops.Length], "wall_top");
                if (r == -1 && c == -1) return Optional("wall_corner_tl", s_WallSides[0]);
                if (r == -1 && c == width) return Optional("wall_corner_tr", s_WallSides[0]);
                return s_WallSides[h % (uint)s_WallSides.Length];
            }
            if (r >= wallRow)
            {
                if (inCols) return "wall_bottom";
                if (r == wallRow && c == -1) return Optional("wall_corner_bl", s_WallSides[0]);
                if (r == wallRow && c == width) return Optional("wall_corner_br", s_WallSides[0]);
                return s_WallSides[h % (uint)s_WallSides.Length];
            }
            if (c == -1) return s_WallEdgesL[h % (uint)s_WallEdgesL.Length];
            if (c == width) return s_WallEdgesR[h % (uint)s_WallEdgesR.Length];
            if (!inCols) return s_WallSides[h % (uint)s_WallSides.Length];
            if (r >= depth)
            {
                // Лагерь: pad0 — порог у плит карты, pad1 — настил, pad2 — кромка со ступенькой (очередь), ниже — земля двора.
                camp = true;
                int k = r - depth;
                if (k == 0) return Optional("camp_edge_t", "floor_0");
                if (k == 1) return Optional(s_CampFloors[h % (uint)s_CampFloors.Length], "floor_0");
                if (k == 2) return Optional("camp_edge_b", "floor_0");
                return Optional(s_CampGround[h % (uint)s_CampGround.Length], "floor_0");
            }
            if (gateRow[r]) return "floor_gate";
            uint v = h % 10u;
            return s_Floors[v < 4u ? 0 : (v < 7u ? 1 : (v < 9u ? 2 : 3))];
        }

        static string Optional(string sprite, string fallback) => PixelArt.Has(sprite) ? sprite : fallback;

        // ───────────── Декор стен ─────────────

        int DecorSeed() => unchecked((int)BoardView.Hash(m_Map.Number, m_Map.DecoSeed, 0x5EED));

        float WallX(int side, float inset) => PixelWorld.Snap(side == 0 ? -inset : m_Map.Width + inset);

        float RowZ(int row, float lift) => PixelWorld.Snap(m_Map.Depth - row - 1f + lift);

        static SpriteRenderer Decor(Transform visual, string sprite, float x, float z, int layer = 0, Color? color = null) =>
            PixelWorld.Sprite(visual, sprite, sprite, new Vector2(x, z), PixelWorld.OrderAt(z) + layer, color);

        /// <summary>
        /// Окружение шахты как у BoardView (детерминировано по DecoSeed зоны): опоры с фонарями, друзы, медные шестерни и трубы,
        /// знамёна и мелочи на стенах, задняя стена; табличка «?» у выхода. Рельсы старой кузницы не ставятся (кузницы нет).
        /// Выход e — пассивный маркер в задней стене над своей строкой: доски, пока шахта не завершена, затем свет.
        /// </summary>
        void BuildDecor()
        {
            m_ExitBoards = m_ExitGlow = null;
            if (m_Map.Topology != null) { BuildTopologyDecor(); return; }
            var decor = PixelWorld.Visual(m_Root, "Decor");
            int depth = m_Map.Depth, width = m_Map.Width;
            int seed = DecorSeed();
            var rng = new System.Random(seed);
            var used = new bool[2, depth];
            var lanterns = new List<LanternProp>();
            var gears = new List<GearProp>();
            var theme = PixelTheme.Current;
            m_Glints.Clear();
            foreach (var g in m_Map.Gates)
                for (int r = g.Row - 1; r <= g.Row; r++)
                    if (r >= 0 && r < depth) used[0, r] = used[1, r] = true;

            if (depth > 1)
            {
                Decor(decor, PixelTheme.Name("sign_question"), WallX(1, 11f * Px), RowZ(0, 0.1f));
                used[1, 0] = true;
            }

            for (int k = 0, r = 2; r < depth; r += 4, k++)
            {
                int side = (k + seed) & 1;
                if (used[side, r]) side ^= 1;
                if (used[side, r]) continue;
                float z = RowZ(r, 2f * Px);
                var post = Decor(decor, PixelTheme.Name("beam_post"), WallX(side, BeamInsetPx * Px), z);
                post.flipX = side == 1;
                float lampZ = z + LanternHangPx * Px;
                lanterns.Add(Lantern(decor, WallX(side, (BeamInsetPx - LanternArmPx) * Px), lampZ,
                    "fx_glow_lantern", WallX(side, LanternGlowInsetPx * Px), PixelWorld.Snap(lampZ + LanternGlowLiftPx * Px)));
                used[side, r] = true;
                if (r > 0) used[side, r - 1] = true;
            }

            bool cave = theme == EnvironmentTheme.Crystal;
            for (int k = 0, r = (cave ? 1 : 4) + rng.Next(3); r < depth; r += (cave ? 3 : 6) + rng.Next(3), k++)
            {
                int side = (k + 1 + seed) & 1;
                if (used[side, r]) side ^= 1;
                if (used[side, r]) continue;
                float z = RowZ(r, 0.12f);
                bool spire = cave && (k & 1) == 1 && r > 0;
                Decor(decor, PixelTheme.CrystalGlow, WallX(side, 16f * Px), PixelWorld.Snap(z + (spire ? 0.45f : 0.25f)), -1);
                string druse = rng.Next(2) == 0 ? "crystal_cluster_a" : "crystal_cluster_b";
                Decor(decor, spire ? "crystal_spire" : PixelTheme.Name(druse), WallX(side, (spire ? 13f : 12f) * Px), z);
                m_Glints.Add((new Vector3(WallX(side, (spire ? 13f : 12f) * Px), 0f, z + (spire ? 22f : 10f) * Px), r));
                used[side, r] = true;
                if (spire) used[side, r - 1] = true;
            }

            if (theme == EnvironmentTheme.Copper)
            {
                for (int k = 0, r = 3 + rng.Next(3); r < depth; r += 5 + rng.Next(3), k++)
                {
                    int side = (k + seed) & 1;
                    if (used[side, r]) side ^= 1;
                    if (used[side, r] || r < 1 || used[side, r - 1]) continue;
                    if ((k & 1) == 0)
                    {
                        float z = RowZ(r, 0.35f);
                        gears.Add(new GearProp { Gear = Decor(decor, s_GearFrames[0], WallX(side, GearInsetPx * Px), z), Phase = 0 });
                        gears.Add(new GearProp
                        {
                            Gear = Decor(decor, s_GearFrames[GearMeshFrames], WallX(side, GearInsetPx * Px), PixelWorld.Snap(z + GearPitchPx * Px), -1),
                            Phase = GearMeshFrames, Reverse = true,
                        });
                    }
                    else Decor(decor, "pipe_copper", WallX(side, 10f * Px), RowZ(r, 0.05f));
                    used[side, r] = used[side, r - 1] = true;
                }
            }

            for (int r = 6 + rng.Next(4); r < depth; r += 10 + rng.Next(5))
            {
                int side = rng.Next(2);
                if (used[side, r]) side ^= 1;
                if (used[side, r]) continue;
                Decor(decor, "banner_paw", WallX(side, 11f * Px), RowZ(r, 0.08f));
                used[side, r] = true;
            }

            for (int r = 0; r < depth; r++)
            {
                for (int side = 0; side < 2; side++)
                {
                    if (used[side, r]) continue;
                    double roll = rng.NextDouble();
                    if (roll < 0.10) Decor(decor, s_Props[rng.Next(s_Props.Length)], WallX(side, 11f * Px), RowZ(r, 0.12f));
                    else if (roll < 0.15) Decor(decor, PixelTheme.Name("mushrooms"), WallX(side, 9f * Px), RowZ(r, 0.2f));
                    else if (roll < 0.19) Decor(decor, "chain_hang", WallX(side, 16f * Px), RowZ(r, 0.3f));
                    else continue;
                    used[side, r] = true;
                }
            }

            float wallZ = depth;
            float backZ = PixelWorld.Snap(wallZ + 0.4f), backGlowZ = PixelWorld.Snap(backZ + 0.25f);
            lanterns.Add(Lantern(decor, PixelWorld.Snap(1.0f), backZ, "fx_glow_warm", PixelWorld.Snap(1.0f), backGlowZ));
            lanterns.Add(Lantern(decor, PixelWorld.Snap(width - 1.0f), backZ, "fx_glow_warm", PixelWorld.Snap(width - 1.0f), backGlowZ));
            Decor(decor, "chain_hang", PixelWorld.Snap(0.4f), PixelWorld.Snap(wallZ + 0.9f));
            Decor(decor, "chain_hang", PixelWorld.Snap(width - 0.4f), PixelWorld.Snap(wallZ + 0.9f));
            Decor(decor, PixelTheme.CrystalGlow, PixelWorld.Snap(width - 1.6f), PixelWorld.Snap(wallZ + 1.55f), -1);
            Decor(decor, PixelTheme.Name("crystal_cluster_a"), PixelWorld.Snap(width - 1.6f), PixelWorld.Snap(wallZ + 1.3f));
            if (theme == EnvironmentTheme.Copper)
                gears.Add(new GearProp { Gear = Decor(decor, s_GearFrames[0], PixelWorld.Snap(1.9f), PixelWorld.Snap(wallZ + 1.2f)), Phase = 0 });

            // Выход: устье штольни в задней стене над строкой маркера (клетки маркера проходимы, ТЗ 8.3).
            if (m_Map.ExitCell >= 0 && m_Map.ExitCell < m_Map.Width * m_Map.Depth)
            {
                float ex = Mathf.Clamp(m_Map.Col(m_Map.ExitCell) + 1.5f, 1.5f, width - 1.5f);
                float ez = PixelWorld.Snap(depth - m_Map.Row(m_Map.ExitCell));
                int order = PixelWorld.OrderAt(ez);
                PixelWorld.Sprite(decor, "exit", PixelTheme.Name("exit_base"), new Vector2(ex, ez), order);
                m_ExitGlow = PixelWorld.Sprite(decor, "exitGlow", "exit_glow", new Vector2(ex, ez), order + 1);
                m_ExitBoards = PixelWorld.Sprite(decor, "exitBoards", PixelTheme.Name("exit_boards"), new Vector2(ex, ez), order + 2);
                m_ExitGlow.enabled = false;
            }

            m_Lanterns = lanterns.ToArray();
            m_Gears = gears.ToArray();
        }

        LanternProp Lantern(Transform decor, float x, float z, string glow, float glowX, float glowZ)
        {
            var halo = Decor(decor, glow, glowX, glowZ, -1);
            var lamp = Decor(decor, s_LanternFrames[0], x, z);
            return new LanternProp { Lamp = lamp, Glow = halo, Next = Time.time + Random.Range(0.3f, 0.8f), Frame = 0 };
        }

        void UpdateGears()
        {
            if (m_Gears == null || m_Gears.Length == 0) return;
            int frame = (int)(Time.time / GearFrameSeconds);
            if (frame == m_GearFrame) return;
            m_GearFrame = frame;
            int n = s_GearFrames.Length;
            for (int i = 0; i < m_Gears.Length; i++)
            {
                ref var g = ref m_Gears[i];
                if (g.Gear == null) continue;
                int f = g.Reverse ? g.Phase - frame : g.Phase + frame;
                PixelWorld.SetSprite(g.Gear, s_GearFrames[(f % n + n) % n]);
            }
        }

        void UpdateLanterns()
        {
            if (m_Lanterns == null) return;
            float time = Time.time;
            for (int i = 0; i < m_Lanterns.Length; i++)
            {
                ref var l = ref m_Lanterns[i];
                if (time < l.Next || l.Lamp == null) continue;
                l.Frame ^= 1;
                l.Next = time + Random.Range(0.3f, 0.8f);
                PixelWorld.SetSprite(l.Lamp, s_LanternFrames[l.Frame]);
                if (l.Glow != null) l.Glow.color = new Color(1f, 1f, 1f, l.Frame == 0 ? 1f : 0.8f);
            }
        }

        /// <summary>Редкий блеск друзы открытого сектора (раз в 1,6–4,5 с).</summary>
        void UpdateGlints(OutpostRun run)
        {
            if (m_Glints.Count == 0 || Fx == null || Time.time < m_NextGlint) return;
            m_NextGlint = Time.time + Random.Range(GlintMinSeconds, GlintMaxSeconds) * (VisualSettings.ReduceEffects ? 2f : 1f);
            var g = m_Glints[Random.Range(0, m_Glints.Count)];
            if (g.Row >= 0 && g.Row < m_RowSector.Length && m_RowSector[g.Row] > run.GatesOpened) return;
            Fx.Glint(m_Root.position + g.Pos);
        }

        /// <summary>Выход: доски, пока шахта не завершена; свет — после завершения (ReadyToContinue).</summary>
        void UpdateExit(OutpostRun run)
        {
            if (m_ExitGlow == null) return;
            int open = run.Phase == RunPhase.ReadyToContinue ? 1 : 0;
            if (open == m_ExitShown) return;
            m_ExitShown = open;
            m_ExitGlow.enabled = open == 1;
            if (m_ExitBoards != null) m_ExitBoards.enabled = open == 0;
        }

        // ───────────── Закрытые секторы ─────────────

        void BuildRowSectors()
        {
            m_RowSector = new int[m_Map.Depth];
            for (int r = 0; r < m_Map.Depth; r++)
            {
                int s = 0;
                foreach (var g in m_Map.Gates)
                    if (g.Row > r) s++;
                m_RowSector[r] = s;
            }
        }

        /// <summary>
        /// Затемнение сектора k ≥ 1 (как BoardView): строки над воротами k − 1 до ворот k включительно (у верхнего — и задняя стена).
        /// Строка ворот k − 1 относится к сектору под ней. Порядок нижнего закрытого сектора — над его объектами и под воротами.
        /// </summary>
        void BuildFog()
        {
            if (m_Map.Topology != null) { BuildTopologyFog(); return; }
            var gates = m_Map.Gates;
            int sectors = gates.Length + 1;
            m_Fog = new SpriteRenderer[sectors];
            m_FogLowOrder = new int[sectors];
            m_FogLevel = new float[sectors];
            m_FogFading = new bool[sectors];
            m_FogFadingCount = 0;
            var dark = PixelTheme.Dark;
            m_FogColor = new Color(dark.r, dark.g, dark.b, FogAlpha);
            var fog = PixelWorld.Visual(m_Root, "Fog");
            float x0 = -1 - SideCols, x1 = m_Map.Width + 1 + SideCols;
            for (int k = 1; k < sectors; k++)
            {
                int bottomRow = gates[k - 1].Row - 1;
                int topRow = k < gates.Length ? gates[k].Row : -TopRows;
                if (bottomRow < topRow) continue;
                float z0 = m_Map.Depth - bottomRow - 1f, z1 = m_Map.Depth - topRow;
                var center = new Vector2((x0 + x1) / 2f, (z0 + z1) / 2f);
                var size = new Vector2(x1 - x0, z1 - z0);
                m_Fog[k] = BoardView.FillRect(fog, "fog" + k, center, size, PixelWorld.FogOrder, m_FogColor);
                m_FogLowOrder[k] = PixelWorld.OrderAt(z0) + PixelWorld.OrderPerCell / 2;
                m_FogLevel[k] = 1f;
            }
        }

        void ApplyFogOrders()
        {
            if (m_Map.Topology != null) return;
            for (int k = 1; k < m_Fog.Length; k++)
            {
                var sr = m_Fog[k];
                if (sr == null) continue;
                bool low = k <= m_Gates + 1 && (k == 1 || !m_FogFading[k - 1]);
                int order = low ? m_FogLowOrder[k] : PixelWorld.FogOrder;
                if (sr.sortingOrder != order) sr.sortingOrder = order;
            }
        }

        void ApplyFog(int before, int opened)
        {
            for (int k = 1; k < m_Fog.Length; k++)
            {
                if (!HasFog(k)) continue;
                bool closed = k > opened;
                bool reveal = !closed && before >= 0 && k > before;
                if (reveal)
                {
                    if (!m_FogFading[k] && m_FogLevel[k] > 0f)
                    {
                        m_FogFading[k] = true;
                        m_FogFadingCount++;
                    }
                    continue;
                }
                if (m_FogFading[k])
                {
                    m_FogFading[k] = false;
                    m_FogFadingCount--;
                }
                SetFog(k, closed ? 1f : 0f);
            }
            ApplyFogOrders();
        }

        void SetFog(int k, float level)
        {
            m_FogLevel[k] = level;
            if (m_Map.Topology != null) { SetTopologyFog(k, level); return; }
            var sr = m_Fog[k];
            bool on = level > 0f;
            if (sr.enabled != on) sr.enabled = on;
            if (!on) return;
            var c = m_FogColor;
            c.a = FogAlpha * level;
            sr.color = c;
        }

        void UpdateFogFade(float dt)
        {
            if (m_FogFadingCount <= 0) return;
            for (int k = 1; k < m_Fog.Length; k++)
            {
                if (!m_FogFading[k]) continue;
                float level = Mathf.Max(0f, m_FogLevel[k] - dt / RevealSeconds);
                SetFog(k, level);
                if (level > 0f) continue;
                m_FogFading[k] = false;
                m_FogFadingCount--;
                ApplyFogOrders();
            }
        }
    }
}
