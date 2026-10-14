using System.Collections.Generic;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Пиксельное поле зоны (Docs/Art/PIXEL_STYLE_GUIDE.md, раздел 4). Клетка (column, row) → мир (x = column + 0.5, z = Depth − row − 0.5):
    /// строка 0 — верх карты, камера смотрит строго сверху, мировая +Z — вверх по экрану.
    /// Пол и стены — один меш с UV атласа; декор стоит только на стенах; закрытые секторы закрыты полупрозрачным затемнением;
    /// пустые клетки зоны мержа помечены slot_marker. Вид только отображает состояние симуляции и не хранит экономику.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        /// <summary>Столбцов стены за краевыми (−1 и Width) с каждой стороны: хватает до квадратного экрана.</summary>
        const int SideCols = 3;
        /// <summary>Строк задней стены над строкой 0 и грунта под последней строкой (камера у упоров видит до ~4).</summary>
        const int TopRows = 8, BottomRows = 8;
        /// <summary>Затемнение закрытого сектора (около navy0) и время его исчезновения при открытии (ТЗ 11.1, без вспышки).</summary>
        const float FogAlpha = 0.62f;
        const float RevealSeconds = 0.8f;
        /// <summary>Свет на площадке нового сектора: радиус (клетки) и длительность.</summary>
        const float RevealGlowRadius = 3.5f, RevealGlowSeconds = 1.6f;
        const float Px = 1f / PixelArt.CellPixels;
        // Опора beam_post (20×52, pivot x = 6): кронштейн тянется к полю, фонарь висит под его концом (низ кронштейна — 39 px).
        const float BeamInsetPx = 16f, LanternArmPx = 11f, LanternHangPx = 22f;
        // Узкий ореол фонаря на опоре (fx_glow_lantern 14×32, центр): правый край — кромка поля, центр — на уровне фонаря.
        const float LanternGlowInsetPx = 7f, LanternGlowLiftPx = 9f;

        static readonly string[] s_Floors = { "floor_0", "floor_1", "floor_2", "floor_3" };
        static readonly string[] s_WallSides = { "wall_side_0", "wall_side_1", "wall_side_2", "wall_side_3" };
        // Варианты задней стены и углы стен — дополнительные спрайты (не в манифесте): без них берутся обязательные.
        static readonly string[] s_WallTops = { "wall_top", "wall_top", "wall_top_1", "wall_top_2", "wall_top_3" };
        static readonly string[] s_WallEdgesL = { "wall_edge_l_0", "wall_edge_l_1" };
        static readonly string[] s_WallEdgesR = { "wall_edge_r_0", "wall_edge_r_1" };
        static readonly string[] s_LanternFrames = { "lantern_0", "lantern_1" };
        static readonly string[] s_Props = { "barrel", "crate", "sack" };

        GameSession m_S;
        ZoneLayout m_L;
        Transform m_Root;
        Transform m_ObjectsRoot;
        Transform m_CatsRoot;
        Transform m_ContainersRoot;
        Transform m_LootRoot;
        Mesh m_FloorMesh;
        ObjectView[] m_Objects;
        CannonView m_Cannon;
        readonly Dictionary<int, CatView> m_Cats = new Dictionary<int, CatView>();
        readonly Dictionary<int, ContainerView> m_Containers = new Dictionary<int, ContainerView>();
        readonly Dictionary<int, LootView> m_Loot = new Dictionary<int, LootView>();
        readonly List<int> m_Remove = new List<int>();
        // Эффекты сбора добычи в кадре: автосбор многих куч сразу не забивает пул частиц.
        const int LootFxPerFrame = 6;
        int m_LootFxFrame = -1, m_LootFxCount;
        int m_SelectedOrdinal = -1;
        bool m_Building;            // первая синхронизация после Build: котики без анимации появления
        int m_Gates = -1;           // GatesOpened, под которое приведены затемнение и объекты

        // Затемнение закрытых секторов: [сектор] (0 — стартовый, всегда открыт). Сектор, открытый во время игры, гаснет плавно.
        SpriteRenderer[] m_Fog;
        float[] m_FogLevel;         // 1 — полное затемнение, 0 — нет
        int[] m_FogLowOrder;        // порядок нижнего закрытого сектора: под воротами открытого сектора, над своими объектами
        bool[] m_FogFading;
        int m_FogFadingCount;
        Color m_FogColor;

        // Разметка зоны мержа: [0, Width) — верхний ряд, [Width, 2·Width) — нижний.
        SpriteRenderer[] m_Markers;
        Transform m_MarkersRoot;
        int m_MarkUpper = int.MinValue, m_MarkLower = int.MinValue;
        int m_MarkMask = -1;

        /// <summary>Фонарь на стене: 2 кадра мерцания со случайным темпом 0.3–0.8 с и тёплый ореол.</summary>
        struct LanternProp
        {
            public SpriteRenderer Lamp;
            public SpriteRenderer Glow;
            public float Next;
            public int Frame;
        }
        LanternProp[] m_Lanterns;

        /// <summary>Шестерня медной выработки: кадр = (время / GearFrameSeconds + Phase) по кругу, Reverse — навстречу.</summary>
        struct GearProp
        {
            public SpriteRenderer Gear;
            public int Phase;
            public bool Reverse;
        }
        GearProp[] m_Gears;
        int m_GearFrame = -1;
        static readonly string[] s_GearFrames =
        {
            "gear_copper_0", "gear_copper_1", "gear_copper_2", "gear_copper_3",
            "gear_copper_4", "gear_copper_5", "gear_copper_6", "gear_copper_7",
        };
        const float GearFrameSeconds = 0.2f;
        /// <summary>Сдвиг второй шестерни пары — ползубца (8 зубцов, кадр 11,25°: 22,5° = 2 кадра).</summary>
        const int GearMeshFrames = 2;
        /// <summary>Межосевое расстояние пары (пиксели арта): внешний радиус + радиус впадин.</summary>
        const float GearPitchPx = 19f;
        const float GearInsetPx = 12f;

        /// <summary>Друзы на стенах: точка блеска (мир) и строка (блестят только друзы открытых секторов).</summary>
        readonly List<(Vector3 Pos, int Row)> m_Glints = new List<(Vector3, int)>();
        float m_NextGlint;
        const float GlintMinSeconds = 1.6f, GlintMaxSeconds = 4.5f;

        static BalanceRepository s_CatalogChecked;

        public int Depth => m_L != null ? m_L.Depth : 0;
        public int Width => m_L != null ? m_L.Width : 7;
        public int DraggedCatId { get; set; }
        public Vector3 DragWorldPos { get; set; }

        // Метка клетки посадки при переносе: золотая рамка — котика можно отпустить здесь (свободная клетка, цель с местом,
        // котик того же уровня), красная — нельзя (закрытый сектор, препятствие, занято, другой уровень).
        SpriteRenderer m_DropMark;
        int m_DropCell = -2;
        bool m_DropOk;
        static readonly Color DropOkColor = Color.white;
        static readonly Color DropBadColor = new Color(1f, 0.45f, 0.4f, 0.85f);
        /// <summary>Выбранный котик (0 — нет): под ним рисуется кольцо выделения. Контроллер выставляет каждый кадр.</summary>
        public int SelectedCatId { get; set; }
        /// <summary>Эффекты (дым кузницы и т. п.); контроллер задаёт до <see cref="Build"/>, OnEvent подхватывает переданные.</summary>
        public Effects Fx { get; set; }
        internal GameSession Session => m_S;

        /// <summary>Пушка у кузницы (ТЗ 7.2), из которой вылетают бочки с котиками; null — у карты нет места для неё.</summary>
        public CannonView Cannon => m_Cannon;

        /// <summary>Бочка из пушки приземлилась в своей клетке (ID контейнера): звук приземления — в GameController.</summary>
        public event System.Action<int> ContainerLanded;

        public Vector3 CellCenter(int cell) => new Vector3(m_L.Col(cell) + 0.5f, 0f, m_L.Depth - m_L.Row(cell) - 0.5f);
        public Vector3 GridToWorld(double gx, double gy) => new Vector3((float)gx, 0f, (float)(m_L.Depth - gy));
        public Vector2 WorldToGrid(Vector3 world) => new Vector2(world.x, m_L.Depth - world.z);

        /// <summary>Середина кучки добычи в клетке <paramref name="cell"/>: эффекты сбора, «+N» и полёт к счётчику HUD.</summary>
        public Vector3 LootPos(int cell) => CellCenter(cell) + new Vector3(0f, 0f, LootView.PileCenterY);

        /// <summary>Центр footprint объекта на плоскости поля.</summary>
        public Vector3 ObjectCenter(int ordinal)
        {
            var o = m_L.Objects[ordinal];
            float x = o.Col + o.Width / 2f;
            float z = m_L.Depth - o.Row + o.Depth / 2f - 1f;
            return new Vector3(x, 0, z);
        }

        public void Build(GameSession session)
        {
            m_S = session;
            m_L = session.Layout;
            PixelTheme.Current = PresentationCatalog.ThemeOf(m_L.ZoneNumber);
            Frozen = false;
            m_WalkViews.Clear();
            m_WalkPaths.Clear();
            CheckCatalog(session.Balance);
            if (m_Root != null) Destroy(m_Root.gameObject);
            if (m_FloorMesh != null) Destroy(m_FloorMesh);
            m_Cats.Clear();
            m_Containers.Clear();
            m_Loot.Clear();
            m_SelectedOrdinal = -1;
            m_Gates = -1;
            m_GearFrame = -1;
            m_DropMark = null;
            m_DropCell = -2;
            m_MarkUpper = m_MarkLower = int.MinValue;
            m_MarkMask = -1;
            m_Root = new GameObject("Board_" + m_L.ZoneId).transform;
            m_Root.SetParent(transform, false);
            BuildFloorAndWalls();
            BuildDecor();
            BuildFog();
            BuildMarkers();
            m_ObjectsRoot = new GameObject("Objects").transform;
            m_ObjectsRoot.SetParent(m_Root, false);
            m_ContainersRoot = new GameObject("Containers").transform;
            m_ContainersRoot.SetParent(m_Root, false);
            m_LootRoot = new GameObject("Loot").transform;
            m_LootRoot.SetParent(m_Root, false);
            m_CatsRoot = new GameObject("Cats").transform;
            m_CatsRoot.SetParent(m_Root, false);
            m_Objects = new ObjectView[m_L.Objects.Count];
            for (int i = 0; i < m_L.Objects.Count; i++)
                m_Objects[i] = ObjectView.Create(m_ObjectsRoot, this, m_L.Objects[i]);
            m_Building = true;
            // Пушка — у правого края под зоной мержа (ZoneLayout.CannonCol/CannonRow), смотрит влево, на зону мержа.
            m_Cannon = m_L.HasCannon
                ? CannonView.Create(m_ObjectsRoot, new Vector3(m_L.CannonCol + 0.5f, 0f, m_L.Depth - m_L.CannonRow - 1f), m_L.CannonCol + 0.5f > m_L.Width / 2f)
                : null;
            Sync(0f);
            m_Building = false;
        }

        void OnDestroy()
        {
            if (m_FloorMesh != null) Destroy(m_FloorMesh);
        }

        /// <summary>Каталог отображения покрывает все шахты из данных? Иначе — предупреждение (оформление по умолчанию).</summary>
        static void CheckCatalog(BalanceRepository balance)
        {
            if (balance == null || ReferenceEquals(s_CatalogChecked, balance)) return;
            s_CatalogChecked = balance;
            var missing = PresentationCatalog.MissingVisualEntries(balance);
            if (missing.Count > 0)
                Debug.LogWarning("[CatMiners] В PresentationCatalog нет оформления шахт (цвет/имя): " + string.Join(", ", missing) + ". Используется оформление по умолчанию."); // loc:ignore — журнал разработчика
        }

        // ───────────── Размещение спрайтов ─────────────

        /// <summary>
        /// Позиция спрайта в узле визуала, при которой его холст (без обрезки: тайлы extrude) лежит левым нижним углом
        /// в (<paramref name="left"/>, <paramref name="bottom"/>) — для тайлов, у которых pivot в манифесте не задан.
        /// </summary>
        internal static Vector2 CanvasOrigin(string sprite, float left, float bottom)
        {
            var s = PixelArt.World(sprite);
            float ppu = Mathf.Max(1e-3f, s.pixelsPerUnit);
            return new Vector2(left + s.pivot.x / ppu, bottom + s.pivot.y / ppu);
        }

        /// <summary>Позиция растягиваемого спрайта (9-slice, px_white) размером <paramref name="size"/>, чтобы его центр встал в <paramref name="center"/>.</summary>
        internal static Vector2 CenteredPos(string sprite, Vector2 center, Vector2 size)
        {
            var s = PixelArt.World(sprite);
            var r = s.rect;
            float px = r.width > 0f ? s.pivot.x / r.width : 0.5f;
            float py = r.height > 0f ? s.pivot.y / r.height : 0.5f;
            return new Vector2(center.x + (px - 0.5f) * size.x, center.y + (py - 0.5f) * size.y);
        }

        /// <summary>Прямоугольник из px_white (затемнение): центр и размер в клетках, порядок абсолютный.</summary>
        internal static SpriteRenderer FillRect(Transform visual, string name, Vector2 center, Vector2 size, int order, Color color)
        {
            var sr = PixelWorld.Sprite(visual, name, "px_white", CenteredPos("px_white", center, size), order, color);
            var s = sr.sprite;
            float ppu = Mathf.Max(1e-3f, s.pixelsPerUnit);
            float w = Mathf.Max(1e-4f, s.rect.width / ppu), h = Mathf.Max(1e-4f, s.rect.height / ppu);
            sr.transform.localScale = new Vector3(size.x / w, size.y / h, 1f);
            return sr;
        }

        /// <summary>Детерминированный хеш клетки и зоны: варианты плит, стен и декора не «прыгают» между запусками.</summary>
        internal static uint Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)a * 0x8DA6B343u ^ (uint)b * 0xD8163841u ^ (uint)c * 0xCB1AB31Fu;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return h;
            }
        }

        // ───────────── Пол и стены ─────────────

        /// <summary>
        /// Пол и стены одним мешем с UV атласа: плиты floor_0…3 (вариант по хешу), порог floor_gate в строках ворот,
        /// край стены wall_edge_* в столбцах −1 и Width, заливка wall_side_* дальше наружу, задняя стена над строкой 0
        /// (wall_top_edge, выше wall_top) и грунт wall_bottom под последней строкой. Строится один раз на зону.
        /// </summary>
        void BuildFloorAndWalls()
        {
            int width = m_L.Width, depth = m_L.Depth, zone = m_L.ZoneNumber;
            var gateRow = new bool[depth];
            foreach (var g in m_L.Gates)
                if (g.Row >= 0 && g.Row < depth) gateRow[g.Row] = true;

            int colMin = -1 - SideCols, colMax = width + SideCols;
            int rowMin = -TopRows, rowMax = depth + BottomRows - 1;
            int cols = colMax - colMin + 1, rows = rowMax - rowMin + 1;
            int quads = cols * rows;
            var verts = new Vector3[quads * 4];
            var uvs = new Vector2[quads * 4];
            var colors = new Color32[quads * 4];
            var tris = new int[quads * 6];
            var uvCache = new Dictionary<string, Rect>();
            var atlas = PixelArt.Atlas;
            // Небольшой отступ UV внутрь тайла: на дробном масштабе соседний тексель атласа не даёт шва.
            float epsU = atlas != null ? 0.01f / atlas.width : 0f, epsV = atlas != null ? 0.01f / atlas.height : 0f;
            var white = new Color32(255, 255, 255, 255);

            int q = 0;
            for (int r = rowMin; r <= rowMax; r++)
            {
                for (int c = colMin; c <= colMax; c++, q++)
                {
                    string name = PixelTheme.Name(TileName(c, r, width, depth, zone, gateRow));
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

            m_FloorMesh = new Mesh { name = "floor_" + m_L.ZoneId };
            if (verts.Length > 65000) m_FloorMesh.indexFormat = IndexFormat.UInt32;
            m_FloorMesh.vertices = verts;
            m_FloorMesh.uv = uvs;
            m_FloorMesh.colors32 = colors;
            m_FloorMesh.triangles = tris;
            m_FloorMesh.RecalculateBounds();

            var go = new GameObject("Floor");
            go.transform.SetParent(m_Root, false);
            // Непрозрачный пол чуть ниже плоскости спрайтов: глубина не спорит со спрайтами в y = 0.
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

        static string TileName(int c, int r, int width, int depth, int zone, bool[] gateRow)
        {
            bool inCols = c >= 0 && c < width;
            uint h = Hash(c, r, zone);
            if (r < 0)
            {
                if (inCols) return r == -1 ? "wall_top_edge" : Optional(s_WallTops[h % (uint)s_WallTops.Length], "wall_top");
                if (r == -1 && c == -1) return Optional("wall_corner_tl", s_WallSides[0]);
                if (r == -1 && c == width) return Optional("wall_corner_tr", s_WallSides[0]);
                return s_WallSides[h % (uint)s_WallSides.Length];
            }
            if (r >= depth)
            {
                if (inCols) return "wall_bottom";
                if (r == depth && c == -1) return Optional("wall_corner_bl", s_WallSides[0]);
                if (r == depth && c == width) return Optional("wall_corner_br", s_WallSides[0]);
                return s_WallSides[h % (uint)s_WallSides.Length];
            }
            if (c == -1) return s_WallEdgesL[h % (uint)s_WallEdgesL.Length];
            if (c == width) return s_WallEdgesR[h % (uint)s_WallEdgesR.Length];
            if (!inCols) return s_WallSides[h % (uint)s_WallSides.Length];
            if (gateRow[r]) return "floor_gate";
            // Простые плиты чаще, с трещинами и камешками — реже.
            uint k = h % 10u;
            return s_Floors[k < 4u ? 0 : (k < 7u ? 1 : (k < 9u ? 2 : 3))];
        }

        /// <summary>Дополнительный спрайт атласа, если он есть, иначе обязательный из манифеста (сборка поля, не кадр).</summary>
        static string Optional(string sprite, string fallback) => PixelArt.Has(sprite) ? sprite : fallback;

        // ───────────── Декор стен ─────────────

        int DecorSeed()
        {
            int deco = 0;
            var balance = m_S?.Balance;
            if (balance != null && m_L.ZoneNumber >= 1 && m_L.ZoneNumber <= balance.ZoneCount) deco = balance.Zone(m_L.ZoneNumber).DecoSeed;
            return unchecked((int)Hash(m_L.ZoneNumber, deco, 0x5EED));
        }

        /// <summary>x декора на стене: сторона 0 — левая (x &lt; 0), 1 — правая (x &gt; Width); inset — расстояние от края поля.</summary>
        float WallX(int side, float inset) => PixelWorld.Snap(side == 0 ? -inset : m_L.Width + inset);

        /// <summary>z нижней кромки строки с подъёмом lift (клетки), снап к пикселю арта.</summary>
        float RowZ(int row, float lift) => PixelWorld.Snap(m_L.Depth - row - 1f + lift);

        /// <summary>Спрайт декора с опорой (ноги) в мировой точке (x, z): порядок по z опоры, как у объектов.</summary>
        static SpriteRenderer Decor(Transform visual, string sprite, float x, float z, int layer = 0, Color? color = null) =>
            PixelWorld.Sprite(visual, sprite, sprite, new Vector2(x, z), PixelWorld.OrderAt(z) + layer, color);

        /// <summary>
        /// Окружение шахты (ТЗ 14, концепт): опоры с фонарями через ~4 строки попеременно слева и справа, друзы кристаллов,
        /// знамёна, бочки, ящики, мешки, грибы и цепи; вагонетка с рудой на рельсах у лагеря кузницы; табличка «?» у выхода.
        /// Всё стоит на стенах (видимый край ≈0.65 клетки) и в клетки поля не заходит; раскладка детерминирована по зоне.
        /// Декор закрытых секторов закрывает то же затемнение, что и поле.
        /// Оформление (<see cref="PixelTheme"/>): камень декора и друзы перекрашены; в кристальной пещере друз вдвое больше
        /// и через одну — высокие, в медной выработке на стенах медные трубы и пары медленных шестерён.
        /// </summary>
        void BuildDecor()
        {
            var decor = PixelWorld.Visual(m_Root, "Decor");
            int depth = m_L.Depth, width = m_L.Width;
            int seed = DecorSeed();
            var rng = new System.Random(seed);
            var used = new bool[2, depth];
            var lanterns = new List<LanternProp>();
            var gears = new List<GearProp>();
            var theme = PixelTheme.Current;
            m_Glints.Clear();
            // У строк ворот стоят каменные столбы (wall_gate_*) выше своей клетки: стену рядом не загромождаем.
            foreach (var g in m_L.Gates)
            {
                for (int r = g.Row - 1; r <= g.Row; r++)
                {
                    if (r < 0 || r >= depth) continue;
                    used[0, r] = used[1, r] = true;
                }
            }

            // Лагерь кузницы (кузница 2×3 всегда в столбцах 1–2): рельсы вдоль левой стены и вагонетка с рудой.
            if (m_L.Cart != null)
            {
                int top = Mathf.Max(0, m_L.Cart.Row - m_L.Cart.Depth), bottom = Mathf.Min(depth - 1, m_L.Cart.Row + 1);
                float railsLeft = WallX(0, 20f * Px);
                for (int r = top; r <= bottom; r++)
                {
                    var rail = CanvasOrigin("rails_v", railsLeft, depth - r - 1f);
                    PixelWorld.Sprite(decor, "rails_v", "rails_v", rail, PixelWorld.FloorOrder + 1);
                    used[0, r] = true;
                }
                float cartZ = RowZ(m_L.Cart.Row, 0.5f);
                Decor(decor, "minecart_ore", WallX(0, 12f * Px), cartZ, 1);
            }

            // Табличка «?» на правой стене у выхода (строка 0).
            if (depth > 1)
            {
                Decor(decor, PixelTheme.Name("sign_question"), WallX(1, 11f * Px), RowZ(0, 0.1f));
                used[1, 0] = true;
            }

            // Опоры с фонарями: через 4 строки, попеременно слева и справа. Кронштейн опоры смотрит к полю
            // (на правой стене спрайт отражён), фонарь висит под его концом. Ореол — узкий fx_glow_lantern за самим фонарём:
            // между столбом и кромкой поля, на клетки не заходит; столб (опора ниже) рисуется поверх него.
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

            // Друзы кристаллов с ореолом цвета оформления (аметист, бирюза, азурит). В кристальной пещере — вдвое чаще,
            // и каждая вторая — высокая (crystal_spire, ≈1 клетка стены).
            bool cave = theme == EnvironmentTheme.Crystal;
            for (int k = 0, r = (cave ? 1 : 4) + rng.Next(3); r < depth; r += (cave ? 3 : 6) + rng.Next(3), k++)
            {
                int side = (k + 1 + seed) & 1;
                if (used[side, r]) side ^= 1;
                if (used[side, r]) continue;
                float z = RowZ(r, 0.12f);
                bool spire = cave && (k & 1) == 1 && r > 0;
                // Ореол 32×32 сдвинут к краю стены: его край совпадает с кромкой поля, на клетки свет не заходит.
                Decor(decor, PixelTheme.CrystalGlow, WallX(side, 16f * Px), PixelWorld.Snap(z + (spire ? 0.45f : 0.25f)), -1);
                string druse = rng.Next(2) == 0 ? "crystal_cluster_a" : "crystal_cluster_b";
                Decor(decor, spire ? "crystal_spire" : PixelTheme.Name(druse), WallX(side, (spire ? 13f : 12f) * Px), z);
                m_Glints.Add((m_Root.position + new Vector3(WallX(side, (spire ? 13f : 12f) * Px), 0f, z + (spire ? 22f : 10f) * Px), r));
                used[side, r] = true;
                if (spire) used[side, r - 1] = true;
            }

            // Медная выработка: пары шестерён (медленно крутятся навстречу друг другу) и медные трубы с вентилем.
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
                        // Вторая шестерня выше на межосевое расстояние, повёрнута на ползубца: зубцы входят во впадины.
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

            // Знамёна с лапкой.
            for (int r = 6 + rng.Next(4); r < depth; r += 10 + rng.Next(5))
            {
                int side = rng.Next(2);
                if (used[side, r]) side ^= 1;
                if (used[side, r]) continue;
                Decor(decor, "banner_paw", WallX(side, 11f * Px), RowZ(r, 0.08f));
                used[side, r] = true;
            }

            // Мелочи: бочки, ящики, мешки, грибы, цепи — в свободных строках стен.
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

            // Задняя стена над строкой 0 (вне выхода в столбцах 2–4): фонари, цепи и друза.
            float wallZ = depth;
            // Ореол 32×32 — по центру фонаря: над строкой 0 до клеток он не достаёт.
            float backZ = PixelWorld.Snap(wallZ + 0.4f), backGlowZ = PixelWorld.Snap(backZ + 0.25f);
            lanterns.Add(Lantern(decor, PixelWorld.Snap(1.0f), backZ, "fx_glow_warm", PixelWorld.Snap(1.0f), backGlowZ));
            lanterns.Add(Lantern(decor, PixelWorld.Snap(width - 1.0f), backZ, "fx_glow_warm", PixelWorld.Snap(width - 1.0f), backGlowZ));
            Decor(decor, "chain_hang", PixelWorld.Snap(0.4f), PixelWorld.Snap(wallZ + 0.9f));
            Decor(decor, "chain_hang", PixelWorld.Snap(width - 0.4f), PixelWorld.Snap(wallZ + 0.9f));
            Decor(decor, PixelTheme.CrystalGlow, PixelWorld.Snap(width - 1.6f), PixelWorld.Snap(wallZ + 1.55f), -1);
            Decor(decor, PixelTheme.Name("crystal_cluster_a"), PixelWorld.Snap(width - 1.6f), PixelWorld.Snap(wallZ + 1.3f));
            if (theme == EnvironmentTheme.Copper)
                gears.Add(new GearProp { Gear = Decor(decor, s_GearFrames[0], PixelWorld.Snap(1.9f), PixelWorld.Snap(wallZ + 1.2f)), Phase = 0 });

            m_Lanterns = lanterns.ToArray();
            m_Gears = gears.ToArray();
        }

        /// <summary>
        /// Фонарь (опора — в (<paramref name="x"/>, <paramref name="z"/>)) и его ореол <paramref name="glow"/> с центром
        /// в (<paramref name="glowX"/>, <paramref name="glowZ"/>) под ним; темп мерцания случайный.
        /// </summary>
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

        /// <summary>
        /// Акцент оружия в точке удара поверх общей вспышки: пила и бензопила — искры, лазер — красная вспышка луча на цели,
        /// молот — облачко пыли. Остальное оружие — только общая вспышка и сколы цели.
        /// </summary>
        void WeaponHitFx(Effects fx, int catId, Vector3 at)
        {
            var cat = m_S.FindCat(catId);
            if (cat == null) return;
            switch (CatLook.Weapon(cat.Level))
            {
                case CatLook.Saw:
                case CatLook.Chainsaw:
                    fx.Sparkle(at, 2);
                    break;
                case CatLook.Laser:
                    fx.Glow(at, PixelPalette.Red4, 0.45f, 0.18f);
                    break;
                case CatLook.Hammer:
                    fx.Puff(at);
                    break;
            }
        }

        /// <summary>Редкий блеск кристалла (задание, раздел 6): раз в 1,6–4,5 с одна друза открытого сектора вспыхивает искрой.</summary>
        void UpdateGlints()
        {
            if (m_Glints.Count == 0 || Fx == null || Time.time < m_NextGlint) return;
            m_NextGlint = Time.time + Random.Range(GlintMinSeconds, GlintMaxSeconds) * (VisualSettings.ReduceEffects ? 2f : 1f);
            var g = m_Glints[Random.Range(0, m_Glints.Count)];
            if (g.Row >= 0 && g.Row < m_L.Depth && !m_S.IsCellOpen(m_L.CellIndex(0, g.Row))) return;
            Fx.Glint(g.Pos);
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

        // ───────────── Закрытые секторы ─────────────

        /// <summary>
        /// Затемнение сектора k ≥ 1 — строки над воротами k − 1 до ворот k включительно (у верхнего сектора — и задняя стена),
        /// во всю ширину вместе со стенами. Строка ворот k − 1 относится к сектору под ней и не затемняется.
        /// Ворота и их столбы выше своей строки и заходят в затемнение сектора над ними: у нижнего закрытого сектора
        /// (и у гаснущих открытых) порядок — по нижнему краю, OrderAt(край) + OrderPerCell / 2. Он выше всего, что стоит
        /// опорой в самом секторе, и ниже объектов строки ворот (опора на клетку ниже края) — ворота, которые ломает игрок,
        /// видны целиком. Остальные закрытые секторы — FogOrder: ворота между двумя закрытыми секторами затемнены целиком.
        /// </summary>
        void BuildFog()
        {
            int sectors = m_L.SectorCount;
            m_Fog = new SpriteRenderer[sectors];
            m_FogLowOrder = new int[sectors];
            m_FogLevel = new float[sectors];
            m_FogFading = new bool[sectors];
            m_FogFadingCount = 0;
            var dark = PixelTheme.Dark;
            m_FogColor = new Color(dark.r, dark.g, dark.b, FogAlpha);
            var fog = PixelWorld.Visual(m_Root, "Fog");
            float x0 = -1 - SideCols, x1 = m_L.Width + 1 + SideCols;
            for (int k = 1; k < sectors; k++)
            {
                int bottomRow = m_L.Gates[k - 1].Row - 1;
                int topRow = k < m_L.Gates.Count ? m_L.Gates[k].Row : -TopRows;
                if (bottomRow < topRow) continue;
                float z0 = m_L.Depth - bottomRow - 1f, z1 = m_L.Depth - topRow;
                var center = new Vector2((x0 + x1) / 2f, (z0 + z1) / 2f);
                var size = new Vector2(x1 - x0, z1 - z0);
                m_Fog[k] = FillRect(fog, "fog" + k, center, size, PixelWorld.FogOrder, m_FogColor);
                m_FogLowOrder[k] = PixelWorld.OrderAt(z0) + PixelWorld.OrderPerCell / 2;
                m_FogLevel[k] = 1f;
            }
        }

        /// <summary>
        /// Порядок затемнения по числу открытых ворот (m_Gates): «низкий» у открытых (гаснущих) секторов и у
        /// нижнего закрытого, если сектор под ним уже открыт полностью. Пока сектор под ним гаснет, ворота над ним остаются
        /// в затемнении и светлеют вместе с концом угасания, а не раньше него. Вызывается только при смене ворот и в конце угасания.
        /// </summary>
        void ApplyFogOrders()
        {
            for (int k = 1; k < m_Fog.Length; k++)
            {
                var sr = m_Fog[k];
                if (sr == null) continue;
                bool low = k <= m_Gates + 1 && (k == 1 || !m_FogFading[k - 1]);
                int order = low ? m_FogLowOrder[k] : PixelWorld.FogOrder;
                if (sr.sortingOrder != order) sr.sortingOrder = order;
            }
        }

        /// <summary>Привести затемнение к числу открытых ворот; открытые во время игры секторы гаснут плавно.</summary>
        void ApplyFog(int before, int opened)
        {
            for (int k = 1; k < m_Fog.Length; k++)
            {
                if (m_Fog[k] == null) continue;
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
                // Сектор открылся полностью: ворота над ним — над затемнением следующего.
                ApplyFogOrders();
            }
        }

        // ───────────── Зона мержа ─────────────

        void BuildMarkers()
        {
            m_MarkersRoot = PixelWorld.Visual(m_Root, "MergeMarkers");
            m_Markers = new SpriteRenderer[m_L.Width * 2];
            for (int i = 0; i < m_Markers.Length; i++)
            {
                m_Markers[i] = PixelWorld.Sprite(m_MarkersRoot, "slot", "slot_marker", Vector2.zero, PixelWorld.FloorDecalOrder);
                m_Markers[i].enabled = false;
            }
        }

        /// <summary>
        /// Пунктирные «+» на свободных открытых клетках двух рядов зоны мержа (<see cref="GameSession.MergeRows"/>).
        /// Проверка — 14 клеток без выделений памяти; спрайты переключаются, только когда меняется набор свободных клеток.
        /// </summary>
        void UpdateMergeMarkers()
        {
            if (!m_S.MergeRows(out int upper, out int lower)) upper = lower = -1;
            int width = m_L.Width;
            if (upper != m_MarkUpper || lower != m_MarkLower)
            {
                m_MarkUpper = upper;
                m_MarkLower = lower;
                m_MarkMask = -1;
                if (upper >= 0)
                {
                    for (int i = 0; i < m_Markers.Length; i++)
                    {
                        int row = i < width ? upper : lower;
                        var p = new Vector2(i % width + 0.5f, m_L.Depth - row - 0.5f);
                        m_Markers[i].transform.localPosition = new Vector3(p.x, p.y, 0f);
                    }
                }
            }
            int mask = 0;
            if (upper >= 0)
            {
                for (int i = 0; i < m_Markers.Length; i++)
                {
                    int row = i < width ? upper : lower;
                    if (m_S.IsCellFree(m_L.CellIndex(i % width, row))) mask |= 1 << i;
                }
            }
            if (mask == m_MarkMask) return;
            m_MarkMask = mask;
            for (int i = 0; i < m_Markers.Length; i++)
            {
                bool on = (mask & (1 << i)) != 0;
                if (m_Markers[i].enabled != on) m_Markers[i].enabled = on;
            }
        }

        // ───────────── Синхронизация ─────────────

        public void Select(int ordinal)
        {
            if (m_SelectedOrdinal >= 0 && m_SelectedOrdinal < m_Objects.Length) m_Objects[m_SelectedOrdinal].SetSelected(false);
            m_SelectedOrdinal = ordinal;
            if (ordinal >= 0 && ordinal < m_Objects.Length) m_Objects[ordinal].SetSelected(true);
        }

        public CatView CatViewOf(int id) => m_Cats.TryGetValue(id, out var v) ? v : null;
        public ContainerView ContainerViewOf(int id) => m_Containers.TryGetValue(id, out var v) ? v : null;

        /// <summary>Бочка с этим ID ещё летит из пушки.</summary>
        public bool IsFlying(int containerId) => m_Containers.TryGetValue(containerId, out var v) && v != null && v.Flying;

        /// <summary>Хотя бы одна бочка летит из пушки (обучение ждёт приземления).</summary>
        public bool AnyContainerFlying
        {
            get
            {
                foreach (var kv in m_Containers)
                    if (kv.Value != null && kv.Value.Flying) return true;
                return false;
            }
        }
        public LootView LootViewOf(int id) => m_Loot.TryGetValue(id, out var v) ? v : null;
        public ObjectView ObjectViewOf(int ordinal) => ordinal >= 0 && ordinal < m_Objects.Length ? m_Objects[ordinal] : null;

        // ───────────── Уход котиков в следующую шахту (ТЗ 11.2) ─────────────

        /// <summary>Скорость ухода, клеток/с: не меньше этой; дальний котик доходит не дольше <see cref="WalkMaxSeconds"/>.</summary>
        const float WalkMinSpeed = 3f;
        const float WalkMaxSeconds = 2.2f;
        // Интервал между котиками: идущие гуськом не сливаются в одну фигуру (≈0,5 клетки при обычной скорости).
        const float WalkStagger = 0.18f, WalkStaggerMax = 1f;
        /// <summary>Котик скрывается в штольне (CatView.EnterSeconds) и пауза перед затемнением.</summary>
        const float WalkTail = 0.5f;

        readonly List<CatView> m_WalkViews = new List<CatView>();
        readonly List<Vector3[]> m_WalkPaths = new List<Vector3[]>();

        /// <summary>
        /// Поле заморожено на время перехода: модель уже в новой зоне (CompleteZone выполнен и записан), а на экране — старая
        /// карта, по которой котики уходят в штольню. <see cref="Sync"/> ничего не делает; новая <see cref="Build"/> снимает заморозку.
        /// </summary>
        public bool Frozen { get; private set; }

        /// <summary>Центр выхода старой карты (куда смотрит камера во время ухода котиков).</summary>
        public Vector3 ExitCenter => m_L != null && m_L.Exit != null ? ObjectCenter(m_L.Exit.Ordinal) : Vector3.zero;

        /// <summary>
        /// Пути котиков к выходу по текущей карте — вызывать ДО CompleteZone, пока состояние ещё старое. Обратный BFS
        /// (верх, лево, право, низ) от клеток перед выходом (строка под ним) по проходимым клеткам: открытый сектор или
        /// строка открытых ворот, без объекта (разрушенные камни свободны, открытые ворота проходимы); котики, контейнеры
        /// и добыча не мешают. Путь — центры клеток со срезанными прямыми участками, в конце — шаг в устье штольни.
        /// Котик без пути идёт к ближайшей клетке перед выходом напрямую.
        /// </summary>
        public void PrepareExitWalk()
        {
            m_WalkViews.Clear();
            m_WalkPaths.Clear();
            if (m_S == null || m_L == null || m_L.Exit == null) return;
            var exit = m_L.Exit;
            int count = m_L.CellCount;
            var next = new int[count];
            for (int i = 0; i < count; i++) next[i] = -2;
            var queue = new Queue<int>();
            var sources = new List<int>(exit.Width);
            int frontRow = exit.Row + 1;
            for (int c = exit.Col; c < exit.Col + exit.Width; c++)
            {
                if (!m_L.InBounds(c, frontRow)) continue;
                int cell = m_L.CellIndex(c, frontRow);
                if (!Walkable(cell)) continue;
                next[cell] = -1;
                sources.Add(cell);
                queue.Enqueue(cell);
            }
            var nb = new List<int>(4);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                m_L.Neighbors4(cur, nb);
                foreach (int n in nb)
                {
                    if (next[n] != -2 || !Walkable(n)) continue;
                    next[n] = cur;
                    queue.Enqueue(n);
                }
            }
            var points = new List<Vector3>(16);
            foreach (var kv in m_Cats)
            {
                var view = kv.Value;
                var cat = m_S.FindCat(kv.Key);
                if (view == null || cat == null) continue;
                points.Clear();
                points.Add(view.transform.position);
                int cell = cat.Cell;
                int front;
                if (next[cell] != -2)
                {
                    // По клеткам к выходу; повороты — только там, где меняется направление.
                    while (next[cell] >= 0)
                    {
                        int n = next[cell];
                        int after = next[n] >= 0 ? next[n] : -1;
                        bool turn = after < 0 || (m_L.Col(n) - m_L.Col(cell)) != (m_L.Col(after) - m_L.Col(n)) || (m_L.Row(n) - m_L.Row(cell)) != (m_L.Row(after) - m_L.Row(n));
                        if (turn) points.Add(CellCenter(n));
                        cell = n;
                    }
                    front = cell;
                }
                else front = NearestFront(sources, cat.Cell, exit);
                points.Add(CellCenter(front));
                // Шаг в устье штольни: через строку выхода внутрь, за арку (её спрайт закрывает котика).
                var mouth = CellCenter(front);
                mouth.z += 0.75f;
                points.Add(mouth);
                m_WalkViews.Add(view);
                m_WalkPaths.Add(points.ToArray());
            }
        }

        bool Walkable(int cell)
        {
            int row = m_L.Row(cell);
            bool open = m_S.IsCellOpen(cell) || OpenedGateRow(row);
            if (!open) return false;
            int ord = m_S.ObjectAt(cell);
            if (ord < 0) return true;
            return m_L.Objects[ord].Kind == ObjectKind.Gate && m_S.Mine.Objects[ord].Status == ObjectStatus.Opened;
        }

        bool OpenedGateRow(int row)
        {
            foreach (var g in m_L.Gates)
                if (g.Row == row && g.GateOrder >= 0 && g.GateOrder < m_S.Mine.GatesOpened) return true;
            return false;
        }

        int NearestFront(List<int> sources, int from, ZoneObject exit)
        {
            if (sources.Count == 0) return m_L.CellIndex(exit.Col + exit.Width / 2, Mathf.Min(exit.Row + 1, m_L.Depth - 1));
            int best = sources[0], bestD = int.MaxValue;
            foreach (int c in sources)
            {
                int d = Mathf.Abs(m_L.Col(c) - m_L.Col(from)) + Mathf.Abs(m_L.Row(c) - m_L.Row(from));
                if (d < bestD)
                {
                    bestD = d;
                    best = c;
                }
            }
            return best;
        }

        /// <summary>
        /// Заморозить поле и отправить котиков по путям <see cref="PrepareExitWalk"/>: ближние уходят первыми, скорость
        /// такая, чтобы дальний дошёл не дольше <see cref="WalkMaxSeconds"/>. Возвращает длительность ухода в секундах.
        /// </summary>
        public float PlayExitWalk()
        {
            Frozen = true;
            int n = m_WalkViews.Count;
            if (n == 0) return WalkTail;
            var lengths = new float[n];
            var order = new int[n];
            float longest = 0f;
            for (int i = 0; i < n; i++)
            {
                var path = m_WalkPaths[i];
                float len = 0f;
                for (int k = 1; k < path.Length; k++) len += Vector3.Distance(path[k - 1], path[k]);
                lengths[i] = len;
                order[i] = i;
                longest = Mathf.Max(longest, len);
            }
            System.Array.Sort(order, (a, b) => lengths[a].CompareTo(lengths[b]));
            float speed = Mathf.Max(WalkMinSpeed, longest / WalkMaxSeconds);
            float duration = 0f;
            for (int k = 0; k < n; k++)
            {
                int i = order[k];
                float delay = Mathf.Min(WalkStaggerMax, k * WalkStagger);
                m_WalkViews[i].WalkInto(m_WalkPaths[i], delay, speed, Fx);
                duration = Mathf.Max(duration, delay + lengths[i] / speed);
            }
            return duration + WalkTail;
        }

        /// <summary>Привести вид к состоянию симуляции.</summary>
        public void Sync(float dt)
        {
            if (m_S == null || Frozen) return;
            bool force = false;
            if (m_Gates != m_S.Mine.GatesOpened)
            {
                int before = m_Gates;
                m_Gates = m_S.Mine.GatesOpened;
                // Сектор открылся во время игры (не загрузка и не перестройка поля) — затемнение гаснет плавно.
                ApplyFog(before, m_Gates);
                force = true;
            }
            UpdateFogFade(dt);
            for (int i = 0; i < m_Objects.Length; i++) m_Objects[i].ApplyState(m_S, force);

            // Котики.
            m_Remove.Clear();
            foreach (var kv in m_Cats) if (m_S.FindCat(kv.Key) == null) m_Remove.Add(kv.Key);
            foreach (int id in m_Remove)
            {
                Destroy(m_Cats[id].gameObject);
                m_Cats.Remove(id);
            }
            foreach (var cat in m_S.Mine.Cats)
            {
                if (!m_Cats.TryGetValue(cat.Id, out var view))
                {
                    // Во время игры котик появляется прыжком (событие CatSpawned приходит раньше Sync); при сборке поля
                    // (загрузка, переход) — просто стоит на месте.
                    view = CatView.Create(m_CatsRoot, cat, !m_Building);
                    view.transform.position = CellCenter(cat.Cell);
                    m_Cats[cat.Id] = view;
                }
                view.SetLevel(cat.Level);
                view.SetSelected(SelectedCatId != 0 && cat.Id == SelectedCatId);
                bool dragged = cat.Id == DraggedCatId;
                view.Dragged = dragged;
                view.TargetPos = dragged ? DragWorldPos : CellCenter(cat.Cell);
                view.TargetIndex = cat.TargetIndex;
                if (cat.TargetIndex >= 0)
                {
                    int ord = m_S.OrdinalOf(cat.TargetIndex);
                    view.SetMining(true, ord >= 0 ? ObjectCenter(ord) : view.TargetPos);
                    // Замах — заранее, по фазе симуляции (время до удара); удар — в кадре TargetHit (OnEvent идёт раньше Sync).
                    long delay = m_S.AttackDelayTicks(cat.Level);
                    view.SetTimeToHit((float)GameSession.TicksToSeconds(System.Math.Max(0L, delay - cat.PhaseTicks)),
                        (float)GameSession.TicksToSeconds(delay));
                }
                else view.SetMining(false, Vector3.zero);
            }

            // Контейнеры.
            m_Remove.Clear();
            foreach (var kv in m_Containers)
            {
                bool alive = false;
                foreach (var c in m_S.Mine.Containers) if (c.Id == kv.Key) alive = true;
                if (!alive) m_Remove.Add(kv.Key);
            }
            foreach (int id in m_Remove)
            {
                Destroy(m_Containers[id].gameObject);
                m_Containers.Remove(id);
            }
            foreach (var c in m_S.Mine.Containers)
                if (!m_Containers.ContainsKey(c.Id))
                    m_Containers[c.Id] = ContainerView.Create(m_ContainersRoot, c, CellCenter(c.Cell));

            SyncLoot();
            UpdateMergeMarkers();
            UpdateDropMark();
            UpdateLanterns();
            UpdateGears();
            UpdateGlints();
        }

        /// <summary>
        /// Метка клетки под переносимым котиком: та же клетка, что выберет отпускание (GameSession.DropCat по точке).
        /// Цвет — по тем же правилам ядра: свободная открытая клетка, цель, которая принимает котика, котик того же уровня.
        /// </summary>
        void UpdateDropMark()
        {
            int cell = -1;
            bool ok = false;
            if (DraggedCatId != 0)
            {
                var g = WorldToGrid(DragWorldPos);
                int col = Mathf.FloorToInt(g.x), row = Mathf.FloorToInt(g.y);
                if (m_L.InBounds(col, row))
                {
                    cell = m_L.CellIndex(col, row);
                    ok = DropAllowed(cell);
                }
            }
            if (m_DropMark == null)
            {
                if (cell < 0) return;
                m_DropMark = PixelWorld.Sprite(PixelWorld.Visual(m_Root, "DropMark"), "mark", "slot_marker_hi", Vector2.zero, PixelWorld.FogOrder - 1);
            }
            if (cell == m_DropCell && ok == m_DropOk) return;
            m_DropCell = cell;
            m_DropOk = ok;
            m_DropMark.enabled = cell >= 0;
            if (cell < 0) return;
            var c = CellCenter(cell);
            m_DropMark.transform.localPosition = new Vector3(c.x, c.z, 0f);
            m_DropMark.color = ok ? DropOkColor : DropBadColor;
        }

        bool DropAllowed(int cell)
        {
            var dragged = m_S.FindCat(DraggedCatId);
            if (dragged == null) return false;
            var other = m_S.CatAt(cell);
            if (other != null) return other.Id == dragged.Id || other.Level == dragged.Level;
            int ord = m_S.ObjectAt(cell);
            if (ord >= 0) return m_S.AcceptsCat(ord, dragged.Id);
            if (m_S.ContainerAt(cell) != null) return false;
            return m_S.IsCellOpen(cell);
        }

        /// <summary>
        /// Кучки добычи по Mine.Loot, как контейнеры: исчезнувшая без события сбора (офлайн, автосбор вне показа) удаляется сразу,
        /// новая без события выпадения (загрузка) появляется на месте. Без выделений памяти.
        /// </summary>
        void SyncLoot()
        {
            var loot = m_S.Mine.Loot;
            m_Remove.Clear();
            foreach (var kv in m_Loot) if (m_S.FindLoot(kv.Key) == null) m_Remove.Add(kv.Key);
            foreach (int id in m_Remove)
            {
                var view = m_Loot[id];
                m_Loot.Remove(id);
                if (view != null) Destroy(view.gameObject);
            }
            if (loot == null) return;
            for (int i = 0; i < loot.Count; i++)
            {
                var l = loot[i];
                if (!m_Loot.ContainsKey(l.Id)) m_Loot[l.Id] = LootView.Create(m_LootRoot, l, CellCenter(l.Cell), Fx);
            }
        }

        System.Action<ContainerView> m_OnLanded;

        /// <summary>Бочка из пушки приземлилась: пыль из-под неё и событие для звука.</summary>
        void OnContainerLanded(ContainerView view)
        {
            if (view == null) return;
            if (Fx != null) Fx.Puff(view.transform.position + new Vector3(0f, 0f, -0.3f));
            ContainerLanded?.Invoke(view.ContainerId);
        }

        ContainerState FindContainer(int id)
        {
            var list = m_S.Mine.Containers;
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return list[i];
            return null;
        }

        /// <summary>Бюджет эффектов сбора добычи на кадр (<see cref="LootFxPerFrame"/>).</summary>
        bool TakeLootFx()
        {
            if (m_LootFxFrame != Time.frameCount)
            {
                m_LootFxFrame = Time.frameCount;
                m_LootFxCount = 0;
            }
            return m_LootFxCount++ < LootFxPerFrame;
        }

        public void HighlightMergeCandidates(int level, bool on)
        {
            foreach (var kv in m_Cats)
                kv.Value.SetHighlighted(on && kv.Value.Level == level && kv.Key != DraggedCatId);
        }

        public void OnEvent(SimEvent e, Effects fx)
        {
            if (fx != null) Fx = fx;
            else fx = Fx;
            switch (e.Type)
            {
                case SimEventType.TargetHit:
                {
                    int ord = m_S.OrdinalOf(e.ObjectIndex);
                    if (ord >= 0) m_Objects[ord].OnHit(fx);
                    // В переносе котик не замахивается (ТЗ 7.5): страховка вида, если удар всё же пришёл.
                    if (e.CatId != DraggedCatId && m_Cats.TryGetValue(e.CatId, out var cv))
                    {
                        cv.Strike();
                        // Контактный акцент — на стороне цели, обращённой к котику, на высоте тела камня.
                        if (fx != null && ord >= 0)
                        {
                            var center = ObjectCenter(ord);
                            var d = cv.transform.position - center;
                            d.y = 0f;
                            var at = PixelWorld.Snap(center + (d.sqrMagnitude > 1e-4f ? d.normalized * 0.32f : Vector3.zero) + new Vector3(0f, 0f, 0.18f));
                            fx.Impact(at);
                            WeaponHitFx(fx, e.CatId, at);
                        }
                    }
                    break;
                }
                case SimEventType.RockDestroyed:
                case SimEventType.ShaftCleared:
                case SimEventType.GateBroken:
                case SimEventType.ExitBroken:
                {
                    // Работники цели радуются (короткий прыжок): их цель по последней синхронизации — эта.
                    foreach (var kv in m_Cats)
                        if (kv.Value != null && kv.Value.TargetIndex == e.ObjectIndex && kv.Key != DraggedCatId) kv.Value.PlayJoy();
                    int ord = m_S.OrdinalOf(e.ObjectIndex);
                    if (ord < 0) break;
                    if (fx == null) break;
                    var o = m_L.Objects[ord];
                    var center = ObjectCenter(ord);
                    fx.Puff(center);
                    fx.Burst(center, m_Objects[ord].BaseColor, Mathf.Clamp(8 + 3 * o.Width * o.Depth, 10, 24));
                    break;
                }
                case SimEventType.CatMerged:
                {
                    // Объединение (ТЗ 14): стягивание двух котиков, вспышка, новый номер уровня.
                    // OnEvent идёт раньше Sync в том же кадре: вид втягиваемого котика забираем, пока Sync его не удалил.
                    m_Cats.TryGetValue(e.CatId, out var receiver);
                    if (e.OtherCatId != 0 && e.OtherCatId != e.CatId && m_Cats.TryGetValue(e.OtherCatId, out var other))
                    {
                        m_Cats.Remove(e.OtherCatId);
                        other.AbsorbInto(receiver, fx);   // по прибытии: вспышка, частицы и мягкая пульсация получателя
                    }
                    else if (receiver != null)
                    {
                        receiver.MergeFlash();
                        if (fx != null) fx.Burst(receiver.transform.position + new Vector3(0f, 0f, 0.4f), Palette.Highlight, 18);
                    }
                    break;
                }
                case SimEventType.CatSpawned:
                    if (m_Cats.TryGetValue(e.CatId, out var spawned)) spawned.Pop();
                    break;
                case SimEventType.ContainerSpawned:
                {
                    // Контейнер появился во время игры: вид — сразу (Sync идёт позже). Бочку с котиком (найм, бесплатный котик)
                    // выстреливает пушка у кузницы — она летит дугой в свою клетку (ТЗ 7.2); ящик доставки — «поп» на месте.
                    if (e.Zone != m_L.ZoneNumber) break;
                    var box = FindContainer(e.CatId);
                    if (box == null) break;
                    if (!m_Containers.TryGetValue(box.Id, out var cv))
                    {
                        cv = ContainerView.Create(m_ContainersRoot, box, CellCenter(box.Cell));
                        m_Containers[box.Id] = cv;
                    }
                    if (m_Cannon != null && (box.Kind == ContainerKind.Hire || box.Kind == ContainerKind.FreeCat))
                    {
                        // Цель по другую сторону — пушка разворачивается; прямо над ней — смотрит, куда смотрела.
                        float dx = CellCenter(box.Cell).x - m_Cannon.transform.position.x;
                        m_Cannon.Fire(dx < -0.01f || (dx <= 0.01f && m_Cannon.FacingLeft), fx);
                        if (m_OnLanded == null) m_OnLanded = OnContainerLanded;   // один делегат на все бочки, без выделений
                        cv.Launch(m_Cannon.Muzzle, m_Cannon.MuzzleGround, m_OnLanded);
                        break;
                    }
                    cv.Pop();
                    if (fx != null) fx.Puff(CellCenter(box.Cell) + new Vector3(0f, 0f, -0.3f));
                    break;
                }
                case SimEventType.ContainerOpened:
                    // Бочка или ящик открыты: облачко на месте контейнера, из него появляется котик или награда.
                    if (e.Zone == m_L.ZoneNumber && fx != null)
                    {
                        var at = CellCenter(e.Cell);
                        fx.Puff(at);
                        fx.Sparkle(at, 3);
                    }
                    break;
                case SimEventType.LootDropped:
                {
                    // Награда камня выпала добычей: кучка вылетает из середины камня дугой в клетку якоря.
                    if (e.Zone != m_L.ZoneNumber) break;
                    var loot = m_S.FindLoot(e.CatId);
                    if (loot == null || m_Loot.ContainsKey(loot.Id)) break;
                    var rest = CellCenter(loot.Cell);
                    var view = LootView.Create(m_LootRoot, loot, rest, fx);
                    m_Loot[loot.Id] = view;
                    int ord = m_S.OrdinalOf(e.ObjectIndex);
                    view.Drop((ord >= 0 ? ObjectCenter(ord) : rest) + new Vector3(0f, 0f, 0.2f));
                    break;
                }
                case SimEventType.LootCollected:
                {
                    // Модель добычу уже удалила: вид доигрывает сбор сам. Полёт к счётчику — в GameController (HUD).
                    if (m_Loot.TryGetValue(e.CatId, out var lv))
                    {
                        m_Loot.Remove(e.CatId);
                        if (lv != null) lv.Collect();
                    }
                    if (e.Zone != m_L.ZoneNumber || fx == null || !TakeLootFx()) break;
                    var at = LootPos(e.Cell);
                    // Тап — горсть побольше, автосбор (OtherCatId = 1) — скромнее.
                    fx.LootPop(at, e.Text, e.OtherCatId != 0 ? 2 : 4);
                    fx.Sparkle(at, 2);
                    break;
                }
                case SimEventType.GeneratorUpgraded:
                case SimEventType.MineShaftActivated:
                {
                    int ord = m_S.OrdinalOf(e.ObjectIndex);
                    if (ord < 0 || fx == null) break;
                    var center = ObjectCenter(ord);
                    fx.Sparkle(center, 4);
                    fx.CoinPop(center, 3);
                    break;
                }
                case SimEventType.CheckpointOpened:
                {
                    int ord = m_S.OrdinalOf(e.ObjectIndex);
                    if (ord < 0 || fx == null) break;
                    // Свет в новом секторе (ТЗ 11.1, 14): мягкий тёплый ореол над посадочной площадкой (зона мержа над воротами);
                    // затемнение сектора гаснет в Sync. Без белой вспышки.
                    var gate = m_L.Objects[ord];
                    float padZ = m_S.MergeRows(out _, out int lower) ? m_L.Depth - lower : m_L.Depth - (gate.Row - 2) - 0.5f;
                    fx.Glow(new Vector3(m_L.Width / 2f, 0f, padZ), PixelPalette.Amber4, RevealGlowRadius, RevealGlowSeconds);
                    fx.Sparkle(ObjectCenter(ord), 4);
                    break;
                }
            }
        }
    }
}
