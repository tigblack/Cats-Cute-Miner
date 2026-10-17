using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Цель шахты r/s/x/p (RUNTIME_SPEC §4.3, ТЗ 8.2): оформление по VisualId через <see cref="PresentationCatalog"/> и прежние
    /// таблицы <see cref="ObjectView"/>, стадии урона по доле RemainingHpUnits/MaxHpUnits (спрайты _d1.._d3 или наложения трещин
    /// из ART_NAMES §5), полоса HP, пока цель повреждена или выбрана, разрушение и расчистка (сухое русло водопада, обломки
    /// завала, след месторождения — на полу, проходимо), створки ворот открываются при SectorOpened.
    /// Завод ТЗ 2.2 (цель <c>s</c> с заводом в run): здание factory_&lt;good&gt; — затемнено, пока цепи целы (factory_chains и трещины
    /// factory_chains_crack_1…3 по HP, полоса HP над зданием); после открытия здание остаётся стоять, коллайдер и выбор работают
    /// (тап — доставка или сведения). Работа, полоса цикла и AUTO — <see cref="FactoryView"/>.
    /// Корень — в опоре «ноги» (нижний центр footprint, локально в корне поля), спрайты — в узле визуала с SortingGroup.
    /// Вид только читает TargetState и ничего не меняет в ядре.
    /// </summary>
    public sealed class OutpostTargetView : MonoBehaviour
    {
        const float Px = 1f / PixelArt.CellPixels;
        /// <summary>Ширина ворот в клетках (рамка gate_frame 160 px), как ZoneLayout.GateWidth старой карты.</summary>
        public const int GateWidth = 5;

        static readonly string[] s_WheelFrames = { "shaft_wheel_0", "shaft_wheel_1", "shaft_wheel_2", "shaft_wheel_3" };
        static readonly string[] s_WaterFrames = { "waterfall_0", "waterfall_1", "waterfall_2" };
        static readonly string[] s_PoolFrames = { "waterfall_pool_0", "waterfall_pool_1" };
        static readonly string[] s_DoorOpenFrames = { "gate_door_open_0", "gate_door_open_1", "gate_door_open" };
        static readonly string[] s_RockCracks = { null, "rock_crack_1", "rock_crack_1", "rock_crack_2" };
        static readonly string[] s_BlockCracks = { null, "block_crack_1", "block_crack_2", "block_crack_3" };
        static readonly string[] s_ShaftCracks = { null, "shaft_crack_1", "shaft_crack_2", "shaft_crack_3" };
        static readonly string[] s_ChainCracks = { null, "factory_chains_crack_1", "factory_chains_crack_2", "factory_chains_crack_3" };
        /// <summary>Закрытый завод: здание затемнено (factories.py, Locked).</summary>
        static readonly Color LockedTint = new Color(0.45f, 0.45f, 0.55f, 1f);
        static readonly string[] s_WaterClear = { "waterfall_clear_0", "waterfall_clear_1", "waterfall_clear_2", "waterfall_clear_3" };
        static readonly string[] s_Rubble = { "rubble_debris_0", "rubble_debris_1", "rubble_debris_2" };
        static readonly string[] s_BlockTiles =
        {
            "block_tl", "block_t", "block_tr",
            "block_l", "block_c", "block_r",
            "block_bl", "block_b", "block_br",
        };

        const float WaterFrameSeconds = 0.15f;
        const float WheelFrameSeconds = 0.15f;
        const float DoorFrameSeconds = 0.12f;
        const float HitSeconds = 0.12f, HitStepSeconds = 0.03f;

        // Слои в группе цели.
        const int LayerShadow = 0, LayerBody = 10, LayerOver1 = 20, LayerOver2 = 21, LayerOver3 = 22, LayerOver4 = 23;

        enum Look { Rock, Water, Single, Gate1x1, Blocks, Shaft, Gate, Factory, Debris }

        struct Track
        {
            public SpriteRenderer[] Renderers;
            public string[] Frames;
            public float Seconds;
            public float Phase;
            public int Frame;

            public void Tick(float time)
            {
                if (Renderers == null) return;
                int f = (int)((time + Phase) / Seconds) % Frames.Length;
                if (f == Frame) return;
                Frame = f;
                for (int i = 0; i < Renderers.Length; i++) PixelWorld.SetSprite(Renderers[i], Frames[f]);
            }
        }

        public int TargetId { get; private set; }
        /// <summary>Индекс в OutpostMap.Targets и OutpostRun.Targets.</summary>
        public int Index { get; private set; }
        public char Kind { get; private set; }
        /// <summary>Цвет сколов и обломков.</summary>
        public Color BaseColor { get; private set; }
        /// <summary>Материал для звука удара и разлома.</summary>
        public HitMaterial Material { get; private set; }
        /// <summary>Центр footprint (у ворот — рамки) локально в корне поля.</summary>
        public Vector3 CenterLocal { get; private set; }
        /// <summary>Опора (нижний центр footprint) локально в корне поля.</summary>
        public Vector3 FeetLocal { get; private set; }
        /// <summary>Верх тела над опорой (клетки): подписи, сведения, вылет мешочка.</summary>
        public float TopZ { get; private set; }
        public int Width { get; private set; }
        public int DepthCells { get; private set; }
        public int Column { get; private set; }
        public int Row { get; private set; }
        public bool Destroyed => m_Destroyed;
        /// <summary>Цель — завод ТЗ 2.2 (здание стоит и после открытия).</summary>
        public bool IsFactory => m_Look == Look.Factory;
        /// <summary>Цель — куча мусора обстановки шахты.</summary>
        public bool IsDebris => m_Look == Look.Debris;
        /// <summary>Товар завода ("milk") или null.</summary>
        public string FactoryGood { get; private set; }
        /// <summary>Удары сейчас приходятся по цепям завода (звук цепей).</summary>
        public bool Chains => m_Look == Look.Factory && !m_Destroyed;
        /// <summary>Узел визуала (группа сортировки цели): слои вида завода.</summary>
        internal Transform VisualNode => m_Visual;
        /// <summary>Узел оверлеев (уголки выбора): полосы и значки над зданием.</summary>
        internal Transform OverlayNode => m_Overlay;
        /// <summary>Доля оставшегося HP 0…1.</summary>
        public float HpFraction => m_HpFrac;

        Look m_Look;
        Transform m_Visual, m_Overlay;
        UnityEngine.Rendering.SortingGroup m_Group;
        BoxCollider m_Collider;
        BoxCollider[] m_PassageColliders;
        SpriteRenderer[] m_PassageDoors;
        int m_LiveOrder;
        int m_OverlayOrder;

        SpriteRenderer m_Shadow, m_Body, m_Crack, m_Door, m_Rubble, m_Gems, m_Wheel, m_Cleared, m_Select, m_Chains;
        string[] m_DamageSprites;          // камень и столбы ворот: [0] целый, [1…3] стадии (null — нет спрайта)
        SpriteRenderer[] m_Tiles;          // массив блоков: тайлы по клеткам
        SpriteRenderer[] m_TileCracks;
        SpriteRenderer[] m_Debris;         // обломки на полу после расчистки (по клеткам)
        SpriteRenderer[] m_WaterMid, m_WaterTop, m_WaterPool;
        Track m_AnimA, m_AnimB;
        HpBar m_Hp;

        long m_LastHp = long.MinValue;
        float m_HpFrac = 1f;
        bool m_Damaged;
        int m_Stage;
        bool m_Destroyed;
        bool m_Applied;
        bool m_Selected;
        bool m_HpShown = true;
        float m_HpShownFrac = -1f;
        float m_HitT;
        float m_JitterX;
        bool m_DoorAnim;
        float m_DoorT;
        int m_DoorFrame = -1;
        bool m_WheelOn;

        /// <param name="factoryGood">Товар завода ("milk") — цель <c>s</c> рисуется заводом ТЗ 2.2; null — прежний вид.</param>
        public static OutpostTargetView Create(Transform parent, OutpostMap map, MapTarget mt, int index, BalanceRepository balance,
            string factoryGood = null)
        {
            int minCol = mt.Column, maxCol = mt.Column, minRow = mt.Row, maxRow = mt.Row;
            if (mt.Cells != null && mt.Cells.Length > 0)
            {
                minCol = maxCol = map.Col(mt.Cells[0]);
                minRow = maxRow = map.Row(mt.Cells[0]);
                foreach (int c in mt.Cells)
                {
                    int col = map.Col(c), row = map.Row(c);
                    if (col < minCol) minCol = col;
                    if (col > maxCol) maxCol = col;
                    if (row < minRow) minRow = row;
                    if (row > maxRow) maxRow = row;
                }
            }
            bool passageGate = mt.IsGate && map.Topology != null;
            int w = mt.IsGate && !passageGate ? Mathf.Min(GateWidth, map.Width - mt.Column) : maxCol - minCol + 1;
            int d = mt.IsGate && !passageGate ? 1 : maxRow - minRow + 1;
            int col0 = mt.IsGate && !passageGate ? mt.Column : minCol;
            int rowBottom = mt.IsGate && !passageGate ? mt.Row : maxRow;

            var go = new GameObject(mt.Kind + "_" + mt.Id);
            go.transform.SetParent(parent, false);
            float feetZ = map.Depth - rowBottom - 1f;
            var feet = new Vector3(col0 + w / 2f, 0f, feetZ);
            go.transform.localPosition = feet;
            var v = go.AddComponent<OutpostTargetView>();
            v.TargetId = mt.Id;
            v.Index = index;
            v.Kind = mt.Kind;
            v.Width = w;
            v.DepthCells = d;
            v.Column = col0;
            v.Row = rowBottom;
            v.FeetLocal = feet;
            v.CenterLocal = new Vector3(feet.x, 0f, feetZ + d / 2f);
            v.m_LiveOrder = PixelWorld.OrderAt(feetZ);
            // Оверлеи (уголки выбора) — по мировой z, как HpBar и Label3D.
            v.m_OverlayOrder = PixelWorld.OverlayOrderAt(go.transform.position.z);
            v.m_Visual = PixelWorld.Visual(go.transform);
            v.m_Group = PixelWorld.Group(v.m_Visual.gameObject, feetZ);
            v.m_Overlay = PixelWorld.Visual(go.transform, "overlay");
            v.FactoryGood = mt.Kind == 's' ? factoryGood : null;
            v.BuildBody(map, mt, balance);

            // Тап сверху: коллайдер покрывает footprint (у ворот — рамку в строке ворот).
            if (passageGate)
            {
                v.m_PassageColliders = new BoxCollider[mt.Cells.Length];
                for (int k = 0; k < mt.Cells.Length; k++)
                {
                    int cell = mt.Cells[k];
                    var collider = go.AddComponent<BoxCollider>();
                    collider.center = new Vector3(map.Col(cell) + 0.5f - feet.x, 0.5f,
                        map.Depth - map.Row(cell) - 0.5f - feetZ);
                    collider.size = new Vector3(0.95f, 1f, 0.95f);
                    v.m_PassageColliders[k] = collider;
                }
            }
            else
            {
                v.m_Collider = go.AddComponent<BoxCollider>();
                v.m_Collider.center = new Vector3(0f, 0.5f, d / 2f);
                v.m_Collider.size = new Vector3(w * 0.95f, 1f, d * 0.95f);
            }
            OutpostPickable.Add(go, OutpostPickKind.Target, mt.Id);
            // Полоса HP у основания, как у ObjectView; у завода — над зданием (там же потом полоса цикла).
            var hpAt = v.m_Look == Look.Factory ? new Vector3(0f, 0f, v.TopZ + 4f * Px) : new Vector3(0f, 0f, 2f * Px);
            v.m_Hp = HpBar.Create(go.transform, hpAt, Mathf.Min(1.5f, 0.75f * w));
            return v;
        }

        SpriteRenderer Spr(string name, string sprite, Vector2 pos, int order, Color? color = null) =>
            PixelWorld.Sprite(m_Visual, name, sprite, pos, order, color);

        void BuildBody(OutpostMap map, MapTarget mt, BalanceRepository balance)
        {
            float defaultTop = 1f;
            SpriteRenderer topOverlay = null;
            string visualId = mt.VisualId ?? mt.TypeId;
            switch (mt.Kind)
            {
                case 'r':
                {
                    m_Look = Look.Rock;
                    var kind = PresentationCatalog.RockVisual(balance, visualId);
                    BaseColor = Palette.RockColor(kind);
                    Material = RockMaterial(balance, kind, visualId);
                    m_Shadow = Spr("shadow", "shadow_m", new Vector2(0f, 4f * Px), LayerShadow);
                    int variant = (int)(BoardView.Hash(mt.Id, map.Number, 17) % 3u);
                    string rock = PresentationCatalog.RockSprite(balance, visualId, variant);
                    m_Body = Spr("rock", rock, Vector2.zero, LayerBody);
                    m_DamageSprites = new string[4];
                    m_DamageSprites[0] = rock;
                    for (int s = 1; s < 4; s++) m_DamageSprites[s] = PixelArt.Has(rock + "_d" + s) ? rock + "_d" + s : null;
                    m_Crack = Spr("crack", "rock_crack_1", Vector2.zero, LayerOver1);
                    m_Crack.enabled = false;
                    defaultTop = 0.95f;
                    break;
                }
                case 'x' when mt.IsDebris:
                    BuildDressingDebris(map, mt);
                    defaultTop = 0.8f;
                    break;
                case 'x':
                    BuildObstruction(map, mt, visualId);
                    defaultTop = DepthCells;
                    break;
                case 's' when FactoryGood != null:
                {
                    // Завод (ТЗ 2.2 §4.3): здание, кадр работы — FactoryView; цепи с замком и трещины — наложения того же холста.
                    m_Look = Look.Factory;
                    BaseColor = PixelPalette.Slate5;
                    Material = HitMaterial.Metal;
                    m_Shadow = Spr("shadow", "shadow_l", new Vector2(0f, 6f * Px), LayerShadow);
                    string building = "factory_" + FactoryGood;
                    m_Body = Spr("factory", PixelArt.Has(building) ? building : PixelTheme.Name("shaft_base"), Vector2.zero, LayerBody);
                    m_Chains = Spr("chains", "factory_chains", Vector2.zero, LayerOver2);
                    m_Crack = Spr("chainCrack", s_ChainCracks[1], Vector2.zero, LayerOver3);
                    m_Crack.enabled = false;
                    defaultTop = 3.5f;
                    break;
                }
                case 's':
                {
                    m_Look = Look.Shaft;
                    BaseColor = PresentationCatalog.ShaftGemColor(mt.TypeId);
                    Material = HitMaterial.Crystal;
                    m_Shadow = Spr("shadow", "shadow_l", new Vector2(0f, 6f * Px), LayerShadow);
                    m_Body = Spr("base", PixelTheme.Name("shaft_base"), Vector2.zero, LayerBody);
                    m_Gems = Spr("gems", "shaft_gems", Vector2.zero, LayerOver1, BaseColor);
                    m_Rubble = Spr("rubble", "shaft_rubble", Vector2.zero, LayerOver2);
                    m_Crack = Spr("crack", PixelTheme.Name("shaft_crack_1"), Vector2.zero, LayerOver3);
                    m_Crack.enabled = false;
                    var wheelFrames = PixelTheme.Names(s_WheelFrames);
                    m_Wheel = Spr("wheel", wheelFrames[0], Vector2.zero, LayerOver4);
                    m_AnimA = new Track { Renderers = new[] { m_Wheel }, Frames = wheelFrames, Seconds = WheelFrameSeconds, Frame = 0 };
                    topOverlay = m_Wheel;
                    defaultTop = 3.5f;
                    break;
                }
                case 'p':
                    m_Look = Look.Gate;
                    BaseColor = Palette.Wood;
                    Material = HitMaterial.Wood;
                    if (map.Topology != null)
                    {
                        BuildPassageGate(map, mt);
                        defaultTop = DepthCells;
                        break;
                    }
                    m_Shadow = Spr("shadow", "shadow_xl", new Vector2(0f, 4f * Px), LayerShadow);
                    m_Body = Spr("frame", "gate_frame", Vector2.zero, LayerBody);
                    m_Door = Spr("doors", "gate_door_closed", Vector2.zero, LayerOver1);
                    defaultTop = 1.7f;
                    break;
            }
            float top = defaultTop;
            if (m_Body != null && m_Body.sprite != null && PixelArt.Has(m_Body.sprite.name)) top = m_Body.sprite.bounds.max.y;
            if (topOverlay != null && topOverlay.sprite != null && PixelArt.Has(topOverlay.sprite.name))
                top = Mathf.Max(top, topOverlay.transform.localPosition.y + topOverlay.sprite.bounds.max.y);
            if (m_PassageDoors != null) top = DepthCells;
            TopZ = PixelWorld.Snap(Mathf.Max(0.5f, top));
            float phase = (BoardView.Hash(mt.Id, 7, 3) % 1000u) / 1000f;
            m_AnimA.Phase = phase;
            m_AnimB.Phase = phase * 0.7f;
        }

        void BuildPassageGate(OutpostMap map, MapTarget target)
        {
            m_PassageDoors = new SpriteRenderer[target.Cells.Length];
            // Rotate the frame when the approach crosses a vertical barrier in a horizontal tunnel.
            int approachCol = 0, approachRow = 0;
            if (target.WorkCells != null)
                foreach (int cell in target.WorkCells)
                {
                    approachCol += map.Col(cell);
                    approachRow += map.Row(cell);
                }
            float count = target.WorkCells != null ? target.WorkCells.Length : 0;
            bool vertical = count > 0 && Mathf.Abs(approachCol / count - (Column + Width / 2f - 0.5f))
                > Mathf.Abs(approachRow / count - (Row - DepthCells / 2f + 0.5f));
            for (int k = 0; k < target.Cells.Length; k++)
            {
                int cell = target.Cells[k];
                var center = new Vector2(map.Col(cell) + 0.5f - FeetLocal.x,
                    map.Depth - map.Row(cell) - 0.5f - FeetLocal.z);
                var frame = Spr("passageFrame", "gate_frame", center, LayerBody);
                var door = Spr("passageDoor", "gate_door_closed", center, LayerOver1);
                // Scale the existing art to the physical cell, with a small margin from rock edges.
                var bounds = frame.sprite.bounds;
                var scale = new Vector3(0.94f / Mathf.Max(0.01f, bounds.size.x),
                    0.9f / Mathf.Max(0.01f, bounds.size.y), 1f);
                FitPassageSprite(frame, center, scale, vertical);
                FitPassageSprite(door, center, scale, vertical);
                m_PassageDoors[k] = door;
            }
        }

        static void FitPassageSprite(SpriteRenderer sprite, Vector2 center, Vector3 scale, bool vertical)
        {
            var rotation = Quaternion.Euler(0f, 0f, vertical ? 90f : 0f);
            sprite.transform.localScale = scale;
            sprite.transform.localRotation = rotation;
            var offset = rotation * Vector3.Scale(sprite.sprite.bounds.center, scale);
            sprite.transform.localPosition = new Vector3(center.x, center.y, 0f) - offset;
        }

        void SetGateSprite(string sprite)
        {
            if (m_PassageDoors == null) { PixelWorld.SetSprite(m_Door, sprite); return; }
            foreach (var door in m_PassageDoors) PixelWorld.SetSprite(door, sprite);
        }

        static HitMaterial RockMaterial(BalanceRepository balance, RockVisualKind kind, string visualId)
        {
            switch (kind)
            {
                case RockVisualKind.Gems:
                case RockVisualKind.Elixir: return HitMaterial.Crystal;
                case RockVisualKind.Chest: return PresentationCatalog.ChestTier(balance, visualId) == PresentationCatalog.DefaultChestTier ? HitMaterial.Wood : HitMaterial.Metal;
                case RockVisualKind.Pass: return HitMaterial.Metal;
                default: return HitMaterial.Stone;
            }
        }

        /// <summary>
        /// Завал x: водопад (верх, вода, пена), одиночный блок 1×1 (block_single_*), столб у ворот (wall_gate_l/r со стадиями
        /// _d1/_d2), вертикальный или горизонтальный массив шириной 1, иначе 9-тайловый массив — как ObjectView.BuildObstruction.
        /// Под каждой клеткой заранее лежат обломки rubble_debris_* (видны после расчистки, на полу).
        /// </summary>
        void BuildObstruction(OutpostMap map, MapTarget mt, string visualId)
        {
            int w = Width, d = DepthCells;
            float left = -w / 2f;
            Material = HitMaterial.Stone;
            if (PresentationCatalog.ObstructionVisual(visualId) == ObstructionVisualKind.Water)
            {
                m_Look = Look.Water;
                BaseColor = Palette.Water;
                var waterFrames = PixelTheme.Names(s_WaterFrames);
                var poolFrames = PixelTheme.Names(s_PoolFrames);
                int middle = Mathf.Max(0, d - 2) * w;
                m_WaterMid = new SpriteRenderer[middle];
                m_WaterPool = new SpriteRenderer[w];
                m_WaterTop = new SpriteRenderer[d > 1 ? w : 0];
                int m = 0;
                for (int dx = 0; dx < w; dx++)
                {
                    float x = left + dx + 0.5f;
                    for (int dz = 0; dz < d; dz++)
                    {
                        int order = LayerBody + (d - 1 - dz);
                        if (dz == 0) m_WaterPool[dx] = Spr("pool", poolFrames[0], new Vector2(x, 0f), order);
                        else if (dz == d - 1) m_WaterTop[dx] = Spr("top", PixelTheme.Name("waterfall_top"), new Vector2(x, dz), order);
                        else m_WaterMid[m++] = Spr("water", waterFrames[0], BoardView.CanvasOrigin(waterFrames[0], left + dx, dz), order);
                    }
                }
                if (middle > 0) m_AnimA = new Track { Renderers = m_WaterMid, Frames = waterFrames, Seconds = WaterFrameSeconds, Frame = 0 };
                m_AnimB = new Track { Renderers = m_WaterPool, Frames = poolFrames, Seconds = WaterFrameSeconds * 2f, Frame = 0 };
                return;
            }
            BaseColor = Palette.Obstruction;
            if (w == 1 && d == 1)
            {
                m_Shadow = Spr("shadow", "shadow_m", new Vector2(0f, 4f * Px), LayerShadow);
                string single = PresentationCatalog.ObstructionSingleSprite(visualId) ?? "block_single_1";
                bool pillar = single == "wall_gate_l" || single == "wall_gate_r";
                m_Look = pillar ? Look.Gate1x1 : Look.Single;
                m_Body = Spr("block", PixelTheme.Name(single), Vector2.zero, LayerBody);
                if (pillar)
                {
                    // Столб у ворот: стадии _d1 (HP < ⅔) и _d2 (HP < ⅓) заменяют спрайт (ART_NAMES §5).
                    m_DamageSprites = new string[4];
                    m_DamageSprites[0] = PixelTheme.Name(single);
                    string d1 = PixelArt.Has(single + "_d1") ? PixelTheme.Name(single + "_d1") : null;
                    string d2 = PixelArt.Has(single + "_d2") ? PixelTheme.Name(single + "_d2") : null;
                    m_DamageSprites[1] = d1;
                    m_DamageSprites[2] = d2 ?? d1;
                    m_DamageSprites[3] = d2 ?? d1;
                }
                else
                {
                    m_Crack = Spr("crack", PixelTheme.Name(s_BlockCracks[1]), Vector2.zero, LayerOver1);
                    m_Crack.enabled = false;
                }
                BuildDebris(map, mt);
                return;
            }
            m_Look = Look.Blocks;
            m_Tiles = new SpriteRenderer[w * d];
            m_TileCracks = new SpriteRenderer[w * d];
            int k = 0;
            for (int dz = 0; dz < d; dz++)
            {
                for (int dx = 0; dx < w; dx++, k++)
                {
                    string tile;
                    if (w == 1) tile = dz == d - 1 ? "block_vt" : (dz == 0 ? "block_vb" : "block_vm");
                    else if (d == 1) tile = dx == 0 ? "block_hl" : (dx == w - 1 ? "block_hr" : "block_hm");
                    else
                    {
                        int col = dx == 0 ? 0 : (dx == w - 1 ? 2 : 1);
                        int row = dz == d - 1 ? 0 : (dz == 0 ? 2 : 1);
                        tile = s_BlockTiles[row * 3 + col];
                    }
                    // Нижние клетки рисуются позже верхних: передняя стенка массива перекрывает тайл над ней.
                    int order = LayerBody + 2 * (d - 1 - dz);
                    var pos = new Vector2(left + dx + 0.5f, dz);
                    m_Tiles[k] = Spr("block", PixelTheme.Name(tile), pos, order);
                    m_TileCracks[k] = Spr("crack", PixelTheme.Name(s_BlockCracks[1]), pos, order + 1);
                    m_TileCracks[k].enabled = false;
                    if (m_Body == null) m_Body = m_Tiles[k];
                }
            }
            BuildDebris(map, mt);
        }

        /// <summary>Обломки на полу по клеткам footprint (вариант — по хешу клетки), скрыты до расчистки.</summary>
        /// <summary>
        /// Куча мусора обстановки (Docs/Rework/MINE_DRESSING.md): спрайт debris_&lt;семейство&gt;_&lt;вариант&gt; и стадии _d1/_d2 по HP,
        /// цвет сколов и материал звука — по семейству (<see cref="DressingLook"/>); после разрушения на полу остаются клочки
        /// debris_&lt;семейство&gt;_rest, котики ходят поверх.
        /// </summary>
        void BuildDressingDebris(OutpostMap map, MapTarget mt)
        {
            m_Look = Look.Debris;
            var look = DressingLook.Of(mt.Piece.Family);
            BaseColor = look.Chips;
            Material = look.Material;
            m_Shadow = Spr("shadow", Width > 1 ? "shadow_l" : "shadow_m", new Vector2(0f, 4f * Px), LayerShadow);
            string sprite = PixelArt.Has(mt.VisualId) ? mt.VisualId : PixelTheme.Name("block_single_1");
            m_Body = Spr("debris", sprite, Vector2.zero, LayerBody);
            // Одиночные кучи отражаются по X: одинаковые варианты рядом не повторяют друг друга.
            if (Width == 1) m_Body.flipX = (BoardView.Hash(mt.Id, map.Number, 29) & 1u) == 1u;
            string d1 = PixelArt.Has(sprite + "_d1") ? sprite + "_d1" : null;
            string d2 = PixelArt.Has(sprite + "_d2") ? sprite + "_d2" : d1;
            m_DamageSprites = new[] { sprite, d1, d2, d2 };
            m_Crack = d1 == null ? Spr("crack", PixelTheme.Name(s_BlockCracks[1]), Vector2.zero, LayerOver1) : null;
            if (m_Crack != null) m_Crack.enabled = false;
            string rest = DressingLook.RestSprite(mt.Piece.Family);
            m_Debris = new SpriteRenderer[mt.Cells.Length];
            for (int i = 0; i < mt.Cells.Length; i++)
            {
                int c = mt.Cells[i];
                float x = map.Col(c) + 0.5f - FeetLocal.x;
                float z = (map.Depth - map.Row(c) - 1f) - FeetLocal.z;
                var sr = PixelWorld.Sprite(m_Overlay, "rest", rest, new Vector2(x, z), PixelWorld.FloorDecalOrder + 2);
                sr.flipX = (BoardView.Hash(map.Col(c), map.Row(c), map.Number) & 1u) == 1u;
                sr.enabled = false;
                m_Debris[i] = sr;
            }
        }

        void BuildDebris(OutpostMap map, MapTarget mt)
        {
            if (mt.Cells == null || mt.Cells.Length == 0) return;
            m_Debris = new SpriteRenderer[mt.Cells.Length];
            for (int i = 0; i < mt.Cells.Length; i++)
            {
                int c = mt.Cells[i];
                int col = map.Col(c), row = map.Row(c);
                float x = col + 0.5f - FeetLocal.x;
                float z = (map.Depth - row - 1f) - FeetLocal.z;
                string sprite = PixelTheme.Name(s_Rubble[BoardView.Hash(col, row, map.Number) % (uint)s_Rubble.Length]);
                var sr = PixelWorld.Sprite(m_Overlay, "debris", sprite, new Vector2(x, z), PixelWorld.FloorDecalOrder + 2);
                sr.enabled = false;
                m_Debris[i] = sr;
            }
        }

        // ───────────── Состояние ─────────────

        /// <summary>
        /// Привести вид к TargetState (каждый кадр, без выделений): разрушение, стадия урона, полоса HP, анимации.
        /// <paramref name="assigned"/> — назначенных добывающих (колесо месторождения крутится, пока его добывают).
        /// </summary>
        public void ApplyState(TargetState ts, int assigned, float time, float dt)
        {
            bool live = m_Applied;
            m_Applied = true;
            if (ts.Destroyed != m_Destroyed || !live) SetDestroyed(ts.Destroyed, live);
            if (ts.RemainingHpUnits != m_LastHp)
            {
                // Доля HP пересчитывается только при ударе, а не каждый кадр.
                m_LastHp = ts.RemainingHpUnits;
                long max = ts.MaxHpUnits;
                m_Damaged = ts.RemainingHpUnits < max;
                m_HpFrac = max > 0 ? Mathf.Clamp01((float)((double)ts.RemainingHpUnits / max)) : 0f;
                ApplyStage(StageOf(m_HpFrac, m_Damaged));
            }
            bool show = !m_Destroyed && (m_Damaged || m_Selected);
            float frac = show ? m_HpFrac : 0f;
            if (show != m_HpShown || frac != m_HpShownFrac)
            {
                m_HpShown = show;
                m_HpShownFrac = frac;
                m_Hp.Set(frac, show);
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

            switch (m_Look)
            {
                case Look.Water:
                    if (!m_Destroyed)
                    {
                        if (m_Stage == 0) m_AnimA.Tick(time);
                        m_AnimB.Tick(time);
                    }
                    break;
                case Look.Shaft:
                    m_WheelOn = !m_Destroyed && assigned > 0;
                    if (m_WheelOn) m_AnimA.Tick(time);
                    break;
                case Look.Gate:
                    if (m_DoorAnim)
                    {
                        m_DoorT += dt;
                        int f = Mathf.Min(s_DoorOpenFrames.Length - 1, (int)(m_DoorT / DoorFrameSeconds));
                        if (f != m_DoorFrame)
                        {
                            m_DoorFrame = f;
                            SetGateSprite(s_DoorOpenFrames[f]);
                        }
                        if (f >= s_DoorOpenFrames.Length - 1) m_DoorAnim = false;
                    }
                    break;
            }
        }

        /// <summary>Стадия урона: 0 — целая, 1 — HP &lt; 0,75, 2 — &lt; 0,45, 3 — &lt; 0,2 (у ворот и столбов: &lt; ⅔, &lt; ⅓).</summary>
        int StageOf(float frac, bool damaged)
        {
            if (!damaged) return 0;
            if (m_Look == Look.Gate || m_Look == Look.Gate1x1) return frac < 1f / 3f ? 2 : (frac < 2f / 3f ? 1 : 0);
            return frac < 0.2f ? 3 : (frac < 0.45f ? 2 : (frac < 0.75f ? 1 : 0));
        }

        /// <summary>Спрайты стадии урона; вызывается только при смене HP и при снятии разрушения (не каждый кадр).</summary>
        void ApplyStage(int stage)
        {
            m_Stage = stage;
            if (m_Destroyed) return;
            switch (m_Look)
            {
                case Look.Rock:
                {
                    string sprite = m_DamageSprites[stage];
                    if (stage == 0 || sprite != null)
                    {
                        PixelWorld.SetSprite(m_Body, sprite ?? m_DamageSprites[0]);
                        m_Crack.enabled = false;
                    }
                    else
                    {
                        PixelWorld.SetSprite(m_Crack, s_RockCracks[stage]);
                        m_Crack.enabled = true;
                    }
                    break;
                }
                case Look.Gate1x1:
                {
                    string sprite = m_DamageSprites[stage] ?? m_DamageSprites[0];
                    PixelWorld.SetSprite(m_Body, sprite);
                    break;
                }
                case Look.Debris:
                {
                    string sprite = m_DamageSprites[stage];
                    PixelWorld.SetSprite(m_Body, sprite ?? m_DamageSprites[0]);
                    if (m_Crack != null)
                    {
                        m_Crack.enabled = stage > 0 && sprite == null;
                        if (m_Crack.enabled) PixelWorld.SetSprite(m_Crack, PixelTheme.Name(s_BlockCracks[stage]));
                    }
                    break;
                }
                case Look.Single:
                case Look.Shaft:
                    m_Crack.enabled = stage > 0;
                    if (stage > 0) PixelWorld.SetSprite(m_Crack, PixelTheme.Name(m_Look == Look.Shaft ? s_ShaftCracks[stage] : s_BlockCracks[stage]));
                    break;
                case Look.Factory:
                    m_Crack.enabled = stage > 0;
                    if (stage > 0) PixelWorld.SetSprite(m_Crack, s_ChainCracks[stage]);
                    break;
                case Look.Blocks:
                    for (int i = 0; i < m_TileCracks.Length; i++)
                    {
                        var cr = m_TileCracks[i];
                        cr.enabled = stage > 0;
                        if (stage > 0) PixelWorld.SetSprite(cr, PixelTheme.Name(s_BlockCracks[stage]));
                    }
                    break;
                case Look.Water:
                    // Расчистка водопада: вода в колонне сменяется кадрами waterfall_clear_0…2 по HP (без анимации течения).
                    if (m_WaterMid != null)
                    {
                        string mid = stage > 0 ? PixelTheme.Name(s_WaterClear[stage - 1]) : PixelTheme.Names(s_WaterFrames)[0];
                        for (int i = 0; i < m_WaterMid.Length; i++) PixelWorld.SetSprite(m_WaterMid[i], mid);
                        m_AnimA.Frame = -1;
                    }
                    break;
                case Look.Gate:
                    SetGateSprite(stage >= 2 ? "gate_door_broken" : (stage == 1 ? "gate_door_cracked" : "gate_door_closed"));
                    break;
            }
        }

        /// <summary>
        /// Разрушение: камень исчезает; завал оставляет обломки на полу, водопад — сухое русло, месторождение — след;
        /// ворота открываются (живое разрушение — анимацией створок). Коллайдер и выбор снимаются.
        /// </summary>
        void SetDestroyed(bool destroyed, bool live)
        {
            m_Destroyed = destroyed;
            if (m_Look == Look.Factory)
            {
                // Открытый завод стоит дальше: коллайдер и выбор остаются (тап — доставка или сведения, ТЗ 2.2 §8).
                SetEnabled(m_Chains, !destroyed);
                SetEnabled(m_Crack, !destroyed && m_Stage > 0);
                if (m_Body != null) m_Body.color = destroyed ? Color.white : LockedTint;
                if (!destroyed) ApplyStage(m_Stage);
                return;
            }
            if (m_Collider != null) m_Collider.enabled = !destroyed;
            if (m_PassageColliders != null)
                foreach (var collider in m_PassageColliders) collider.enabled = !destroyed;
            var pick = GetComponent<OutpostPickable>();
            if (pick != null) pick.enabled = !destroyed;
            if (destroyed && m_Selected) SetSelected(false);
            switch (m_Look)
            {
                case Look.Rock:
                    m_Visual.gameObject.SetActive(!destroyed);
                    break;
                case Look.Single:
                case Look.Gate1x1:
                case Look.Blocks:
                case Look.Debris:
                    m_Visual.gameObject.SetActive(!destroyed);
                    if (m_Debris != null)
                        for (int i = 0; i < m_Debris.Length; i++) m_Debris[i].enabled = destroyed;
                    break;
                case Look.Shaft:
                    SetEnabled(m_Body, !destroyed);
                    SetEnabled(m_Gems, !destroyed);
                    SetEnabled(m_Rubble, !destroyed);
                    SetEnabled(m_Wheel, !destroyed);
                    SetEnabled(m_Shadow, !destroyed);
                    SetEnabled(m_Crack, !destroyed && m_Stage > 0);
                    if (destroyed && m_Cleared == null) m_Cleared = Spr("cleared", PixelTheme.Name("shaft_cleared"), Vector2.zero, LayerBody);
                    SetEnabled(m_Cleared, destroyed);
                    m_Group.sortingOrder = destroyed ? PixelWorld.FloorDecalOrder + 2 : m_LiveOrder;
                    break;
                case Look.Water:
                {
                    // Сухое русло лежит на полу: котики ходят поверх.
                    string mid = destroyed ? PixelTheme.Name(s_WaterClear[3]) : PixelTheme.Names(s_WaterFrames)[0];
                    if (m_WaterMid != null)
                        for (int i = 0; i < m_WaterMid.Length; i++) PixelWorld.SetSprite(m_WaterMid[i], mid);
                    string top = PixelTheme.Name(destroyed ? "waterfall_top_dry" : "waterfall_top");
                    for (int i = 0; i < m_WaterTop.Length; i++) PixelWorld.SetSprite(m_WaterTop[i], top);
                    if (destroyed)
                        for (int i = 0; i < m_WaterPool.Length; i++) PixelWorld.SetSprite(m_WaterPool[i], PixelTheme.Name("waterfall_pool_dry"));
                    m_Group.sortingOrder = destroyed ? PixelWorld.FloorDecalOrder + 2 : m_LiveOrder;
                    if (!destroyed) ApplyStage(m_Stage);
                    break;
                }
                case Look.Gate:
                    if (m_PassageDoors != null)
                        m_Group.sortingOrder = destroyed ? PixelWorld.FloorDecalOrder + 7 : m_LiveOrder;
                    if (destroyed)
                    {
                        if (live)
                        {
                            m_DoorAnim = true;
                            m_DoorT = 0f;
                            m_DoorFrame = -1;
                        }
                        else if (!m_DoorAnim) SetGateSprite("gate_door_open");
                    }
                    else
                    {
                        m_DoorAnim = false;
                        ApplyStage(m_Stage);
                    }
                    break;
            }
        }

        static void SetEnabled(SpriteRenderer sr, bool on)
        {
            if (sr != null && sr.enabled != on) sr.enabled = on;
        }

        // ───────────── Отклик ─────────────

        /// <summary>Удар: дрожание на 1 пиксель арта (~0,12 с) и сколы цвета цели (<paramref name="chipsWorld"/> — мировая точка).</summary>
        public void OnHit(Effects fx, Vector3 chipsWorld)
        {
            if (m_Destroyed) return;
            m_HitT = HitSeconds;
            if (fx != null) fx.Chips(chipsWorld, BaseColor);
            // Металлический лом — сдержанные искры (обстановка шахты).
            if (fx != null && m_Look == Look.Debris && Material == HitMaterial.Metal) fx.Sparks(chipsWorld);
        }

        /// <summary>Число обломков разлома по размеру footprint (как BoardView.OnEvent).</summary>
        public int BurstCount => Mathf.Clamp(8 + 3 * Width * DepthCells, 10, 24);

        public void SetSelected(bool on)
        {
            m_Selected = on && (!m_Destroyed || m_Look == Look.Factory);
            if (m_Selected && m_Select == null)
            {
                var size = new Vector2(Width + 4f * Px, DepthCells + 4f * Px);
                var pos = BoardView.CenteredPos("select_bracket", new Vector2(0f, DepthCells / 2f), size);
                m_Select = PixelWorld.Sliced(m_Overlay, "select", "select_bracket", pos, size, m_OverlayOrder);
            }
            if (m_Select != null && m_Select.enabled != m_Selected) m_Select.enabled = m_Selected;
        }
    }
}
