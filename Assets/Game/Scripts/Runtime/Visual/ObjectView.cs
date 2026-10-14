using CatMiners.Core;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Пиксельное представление объекта карты: камень, препятствие, шахта, ворота, выход, кузница
    /// (Docs/Art/PIXEL_STYLE_GUIDE.md, «Размеры объектов»). Корень стоит в опоре «ноги» — нижний центр footprint
    /// (x = col + W/2, z = Depth − row − 1); спрайты лежат в узле визуала с SortingGroup (порядок по z опоры),
    /// оверлеи (полоса HP, подписи, пузырь статуса, уголки выделения) — вне группы, с порядком PixelWorld.OverlayOrderAt.
    /// </summary>
    public sealed class ObjectView : MonoBehaviour
    {
        public int Ordinal;
        public Color BaseColor;

        // Кадры анимаций: строки готовятся один раз, в кадре без конкатенации.
        static readonly string[] s_FireFrames = { "forge_fire_0", "forge_fire_1", "forge_fire_2" };
        static readonly string[] s_WheelFrames = { "shaft_wheel_0", "shaft_wheel_1", "shaft_wheel_2", "shaft_wheel_3" };
        static readonly string[] s_GlintFrames = { "shaft_glint_0", "shaft_glint_1", "shaft_glint_2", "shaft_glint_3" };
        static readonly string[] s_WaterFrames = { "waterfall_0", "waterfall_1", "waterfall_2" };
        static readonly string[] s_PoolFrames = { "waterfall_pool_0", "waterfall_pool_1" };
        static readonly string[] s_DoorOpenFrames = { "gate_door_open_0", "gate_door_open_1", "gate_door_open" };
        static readonly string[] s_CrackFrames = { "rock_crack_1", "rock_crack_1", "rock_crack_2" };

        const float FireFrameSeconds = 0.12f;
        const float WheelFrameSeconds = 0.15f;
        const float GlintFrameSeconds = 0.12f;
        const float WaterFrameSeconds = 0.15f;
        const float DoorFrameSeconds = 0.12f;
        /// <summary>Дрожание цели от удара: ±1 пиксель арта.</summary>
        const float HitSeconds = 0.12f;
        const float HitStepSeconds = 0.03f;
        /// <summary>Дым из трубы кузницы, пока она работает.</summary>
        const float SmokeSeconds = 1.2f;
        const float Px = 1f / PixelArt.CellPixels;

        /// <summary>
        /// Устье трубы кузницы: пиксель холста forge_base (64×106) от левого верхнего угла — по текущему арту
        /// (короткая труба между ушами «кошачьей печи», кромка обода — строка 2; structures.py, CHIMNEY_TOP).
        /// При перерисовке кузницы сверить; холст и опора (32, 0) — из манифеста.
        /// </summary>
        public static readonly Vector2Int ForgeChimneyPixel = new Vector2Int(32, 2);
        const int ForgeCanvasW = 64, ForgeCanvasH = 106;

        // Слои внутри SortingGroup объекта.
        const int LayerShadow = 0, LayerBody = 10, LayerOver1 = 20, LayerOver2 = 21, LayerOver3 = 22, LayerOver4 = 23;
        // Оверлеи относительно PixelWorld.OverlayOrderAt(z опоры): уголки выделения, пузырь, тексты.
        const int OverlaySelect = 0, OverlayBubble = 1, OverlayText = 2;

        // Ключи состояния метки (≥ 0 — ETA, округлённая до видимой точности).
        const long InfoHidden = -1;
        const long InfoLevelOnly = -2;
        const long InfoNoWorkers = -3;
        // Метки создаются только когда впервые нужны (цели в закрытых секторах их не получают) и не больше
        // InfoCreateBudget за кадр — сборка зоны и открытие сектора не создают десятки TMP за один кадр (ТЗ 15.3).
        const int InfoCreateBudget = 12;
        static int s_InfoBudgetFrame = -1;
        static int s_InfoCreated;

        /// <summary>Дорожка кадров: кадр выбирается по времени, спрайт меняется только при смене кадра.</summary>
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

        ZoneObject m_Obj;
        BoardView m_Board;
        Transform m_Visual;
        Transform m_Overlay;
        float m_FeetZ;
        float m_TopZ;               // верх тела над опорой (клетки): подписи и пузырь — над ним
        int m_Sector;
        int m_OverlayOrder;
        GameNumber m_MaxHp;

        SpriteRenderer m_Body;
        SpriteRenderer m_Crack;
        string[] m_DamageSprites;      // камень: [0] целый, [1…3] стадии повреждения (null — нет спрайта)
        SpriteRenderer m_Rubble, m_ShaftLight;
        /// <summary>Состояния шахты: лента «готова к активации», горка и блеск готовой выплаты, табличка автоматизации.</summary>
        SpriteRenderer m_ShaftRibbon, m_ShaftHeap, m_ShaftGlint, m_ShaftPlaque;
        SpriteRenderer m_Door;
        SpriteRenderer m_ExitBoards, m_ExitGlow;
        SpriteRenderer m_Select;
        Track m_AnimA, m_AnimB;
        int m_CrackStage;

        HpBar m_Hp;
        bool m_HpKnown;
        GameNumber m_LastHp;
        float m_HpFrac = 1f;
        bool m_Damaged;
        bool m_HpShown = true;
        float m_HpShownFrac = -1f;

        Label3D m_Info;             // над телом: «ур. N», ETA при работе или выборе (ТЗ 8.2, 13.1); создаётся лениво
        string m_LevelText;
        long m_InfoKey = InfoHidden;
        bool m_InfoCompact = true;
        float m_InfoNextText;       // тексты обновляются не чаще 10 раз в секунду (ТЗ 15.3)

        // Пузырь статуса (активировать, забрать, авто, открыть ворота, выход открыт): создаётся при первой надобности.
        GameObject m_Bubble;
        // Зона тапа пузыря: отдельный узел на корне (покачивание пузыря не двигает коллайдер).
        GameObject m_BubbleHit;
        BoxCollider m_BubbleCollider;
        SpriteRenderer m_BubbleBody, m_BubbleTail;
        Label3D m_BubbleLabel;
        string m_BubbleText;
        bool m_BubbleBobbing;
        float m_BubbleOffset = -1f;

        bool m_Selected;
        ObjectStatus m_LastStatus = (ObjectStatus)(-1);
        bool m_LastOpen = true;
        bool m_LastPayout;
        bool m_LastAuto;
        bool m_Applied;
        float m_HitT;
        float m_JitterX;
        bool m_DoorAnim;
        float m_DoorT;
        int m_DoorFrame = -1;
        float m_NextSmoke;

        public static ObjectView Create(Transform parent, BoardView board, ZoneObject obj)
        {
            var go = new GameObject(obj.Kind + "_" + obj.Index);
            go.transform.SetParent(parent, false);
            float feetZ = board.Depth - obj.Row - 1f;
            go.transform.position = new Vector3(obj.Col + obj.Width / 2f, 0f, feetZ);
            var v = go.AddComponent<ObjectView>();
            v.Ordinal = obj.Ordinal;
            v.m_Obj = obj;
            v.m_Board = board;
            v.m_FeetZ = feetZ;
            v.m_OverlayOrder = PixelWorld.OverlayOrderAt(feetZ);
            var layout = board.Session.Layout;
            // Объекты в строке ворот (сами ворота и столбы по краям) относятся к сектору под воротами — как пол строки ворот (ТЗ 6.4).
            v.m_Sector = layout.CellSector[obj.Cells[0]];
            if (v.m_Sector < 0 || obj.Kind == ObjectKind.Gate) v.m_Sector = layout.SectorOfRow(obj.Row + 1);

            v.m_Visual = PixelWorld.Visual(go.transform);
            PixelWorld.Group(v.m_Visual.gameObject, feetZ);
            v.m_Overlay = PixelWorld.Visual(go.transform, "overlay");
            v.BuildBody();

            if (obj.Kind != ObjectKind.Obstruction)
            {
                // Тап сверху: коллайдер покрывает footprint (камера смотрит строго вниз).
                var col = go.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 0.5f, obj.Depth / 2f);
                col.size = new Vector3(obj.Width * 0.95f, 1f, obj.Depth * 0.95f);
                var p = go.AddComponent<Pickable>();
                p.Kind = Pickable.PickKind.Object;
                p.Id = obj.Ordinal;
            }
            if (obj.IsTarget)
            {
                v.m_MaxHp = board.Session.Balance.TargetHp(obj.TypeId, obj.Level);
                // Полоса HP у основания объекта, как на концепте (жёлтая полоска под камнем).
                v.m_Hp = HpBar.Create(go.transform, new Vector3(0f, 0f, 2f * Px), Mathf.Min(1.5f, 0.75f * obj.Width));
                v.m_LevelText = Loc.F("board.info.level", obj.Level);
            }
            return v;
        }

        SpriteRenderer Spr(string name, string sprite, Vector2 pos, int order, Color? color = null) =>
            PixelWorld.Sprite(m_Visual, name, sprite, pos, order, color);

        void Shadow(string sprite, float lift) => Spr("shadow", sprite, new Vector2(0f, lift), LayerShadow);

        void BuildBody()
        {
            var o = m_Obj;
            var balance = m_Board.Session?.Balance;
            float defaultTop = 1f;
            // Наложение выше базы (колесо копра над шахтой): подписи и пузырь — над ним.
            SpriteRenderer topOverlay = null;
            switch (o.Kind)
            {
                case ObjectKind.Rock:
                {
                    // Модель — из каталога отображения по данным камня (ТЗ 4.1), без проверки подстрок ID.
                    var kind = PresentationCatalog.RockVisual(balance, o.TypeId);
                    BaseColor = Palette.RockColor(kind);
                    Shadow("shadow_m", 4f * Px);
                    int variant = (int)(BoardView.Hash(o.Index, m_Board.Session.Layout.ZoneNumber, 17) % 3u);
                    string rock = PresentationCatalog.RockSprite(balance, o.TypeId, variant);
                    m_Body = Spr("rock", rock, Vector2.zero, LayerBody);
                    // Стадии повреждения — отдельные спрайты камня (_d1…_d3: трещины, сколы, откушенный верх и осыпь);
                    // без них — прежнее наложение трещин.
                    m_DamageSprites = new string[4];
                    m_DamageSprites[0] = rock;
                    for (int d = 1; d < 4; d++) m_DamageSprites[d] = PixelArt.Has(rock + "_d" + d) ? rock + "_d" + d : null;
                    m_Crack = Spr("crack", s_CrackFrames[1], Vector2.zero, LayerOver1);
                    m_Crack.enabled = false;
                    defaultTop = 0.95f;
                    break;
                }
                case ObjectKind.Obstruction:
                    BuildObstruction(o);
                    defaultTop = o.Depth;
                    break;
                case ObjectKind.Shaft:
                {
                    BaseColor = PresentationCatalog.ShaftGemColor(o.TypeId);
                    Shadow("shadow_l", 6f * Px);
                    // Скальные плечи входа и фон колеса — камень оформления шахты (PixelTheme); постройка одна во всех.
                    m_Body = Spr("base", PixelTheme.Name("shaft_base"), Vector2.zero, LayerBody);
                    m_ShaftLight = Spr("light", "shaft_light", Vector2.zero, LayerOver1);
                    Spr("gems", "shaft_gems", Vector2.zero, LayerOver2, BaseColor);
                    m_Rubble = Spr("rubble", "shaft_rubble", Vector2.zero, LayerOver3);
                    m_ShaftRibbon = Spr("ribbon", "shaft_ribbon", Vector2.zero, LayerOver3);
                    m_ShaftHeap = Spr("heap", "shaft_heap", Vector2.zero, LayerOver3, BaseColor);
                    m_ShaftPlaque = Spr("plaque", "shaft_plaque", Vector2.zero, LayerOver3);
                    m_ShaftGlint = Spr("glint", s_GlintFrames[0], Vector2.zero, LayerOver4);
                    var wheelFrames = PixelTheme.Names(s_WheelFrames);
                    var wheel = Spr("wheel", wheelFrames[0], Vector2.zero, LayerOver4);
                    m_AnimA = new Track { Renderers = new[] { wheel }, Frames = wheelFrames, Seconds = WheelFrameSeconds, Frame = 0 };
                    m_AnimB = new Track { Renderers = new[] { m_ShaftGlint }, Frames = s_GlintFrames, Seconds = GlintFrameSeconds, Frame = 0 };
                    // Колесо (24×24, низ на 88 px) выше базы shaft_base (104 px): верх копра — 112 px, 3.5 клетки.
                    topOverlay = wheel;
                    defaultTop = 3.5f;
                    break;
                }
                case ObjectKind.Gate:
                {
                    BaseColor = Palette.Wood;
                    Shadow("shadow_xl", 4f * Px);
                    m_Body = Spr("frame", "gate_frame", Vector2.zero, LayerBody);
                    m_Door = Spr("doors", "gate_door_closed", Vector2.zero, LayerOver1);
                    defaultTop = 1.7f;
                    break;
                }
                case ObjectKind.Exit:
                {
                    BaseColor = Palette.WoodDark;
                    m_Body = Spr("base", PixelTheme.Name("exit_base"), Vector2.zero, LayerBody);
                    m_ExitGlow = Spr("glow", "exit_glow", Vector2.zero, LayerOver1);
                    m_ExitBoards = Spr("boards", PixelTheme.Name("exit_boards"), Vector2.zero, LayerOver2);
                    defaultTop = 2.2f;
                    break;
                }
                case ObjectKind.Cart:
                {
                    BaseColor = Palette.Fire;
                    Shadow("shadow_l", 6f * Px);
                    m_Body = Spr("base", "forge_base", Vector2.zero, LayerBody);
                    // Ореол топки — под огнём (forge_glow, «под огнём» в манифесте): языки пламени остаются яркими.
                    Spr("glow", "forge_glow", Vector2.zero, LayerOver1);
                    var fire = Spr("fire", s_FireFrames[0], Vector2.zero, LayerOver2);
                    m_AnimA = new Track { Renderers = new[] { fire }, Frames = s_FireFrames, Seconds = FireFrameSeconds, Frame = 0 };
                    m_NextSmoke = Time.time + SmokeSeconds * 0.5f;
                    defaultTop = 2.6f;
                    break;
                }
            }
            // Верх тела — по реальному спрайту (обрезанный холст), иначе — типичная высота вида.
            float top = defaultTop;
            if (m_Body != null && m_Body.sprite != null && PixelArt.Has(m_Body.sprite.name)) top = m_Body.sprite.bounds.max.y;
            // Кадры колеса одного размера и с одной опорой: верх не меняется при вращении.
            if (topOverlay != null && topOverlay.sprite != null && PixelArt.Has(topOverlay.sprite.name))
                top = Mathf.Max(top, topOverlay.transform.localPosition.y + topOverlay.sprite.bounds.max.y);
            m_TopZ = PixelWorld.Snap(Mathf.Max(0.5f, top));
            // Кадры анимаций разных объектов не идут в ногу.
            float phase = (BoardView.Hash(m_Obj.Index, 7, 3) % 1000u) / 1000f;
            m_AnimA.Phase = phase;
            m_AnimB.Phase = phase * 0.7f;
        }

        /// <summary>
        /// Препятствие: водопад (верх, вода, пена), одиночный блок 1×1 по варианту из каталога, столбик ворот,
        /// вертикальный или горизонтальный массив шириной 1, иначе 9-тайловый массив ≥ 2×2. Тайлы — по клеткам footprint.
        /// </summary>
        void BuildObstruction(ZoneObject o)
        {
            int w = o.Width, d = o.Depth;
            float left = -w / 2f;
            if (PresentationCatalog.ObstructionVisual(o.TypeId) == ObstructionVisualKind.Water)
            {
                BaseColor = Palette.Water;
                // Каменные борта водопада — в оформлении шахты (PixelTheme), вода одна во всех.
                var waterFrames = PixelTheme.Names(s_WaterFrames);
                var poolFrames = PixelTheme.Names(s_PoolFrames);
                int middle = Mathf.Max(0, d - 2) * w;
                var water = middle > 0 ? new SpriteRenderer[middle] : null;
                var pool = new SpriteRenderer[w];
                int m = 0;
                for (int dx = 0; dx < w; dx++)
                {
                    float x = left + dx + 0.5f;
                    for (int dz = 0; dz < d; dz++)
                    {
                        int order = LayerBody + (d - 1 - dz);
                        if (dz == 0) pool[dx] = Spr("pool", poolFrames[0], new Vector2(x, 0f), order);
                        else if (dz == d - 1) Spr("top", PixelTheme.Name("waterfall_top"), new Vector2(x, dz), order);
                        else water[m++] = Spr("water", waterFrames[0], BoardView.CanvasOrigin(waterFrames[0], left + dx, dz), order);
                    }
                }
                if (water != null) m_AnimA = new Track { Renderers = water, Frames = waterFrames, Seconds = WaterFrameSeconds, Frame = 0 };
                m_AnimB = new Track { Renderers = pool, Frames = poolFrames, Seconds = WaterFrameSeconds * 2f, Frame = 0 };
                return;
            }
            BaseColor = Palette.Obstruction;
            if (w == 1 && d == 1)
            {
                Shadow("shadow_m", 4f * Px);
                // Скалы-препятствия и столбы ворот — камень стен: в оформлении шахты (PixelTheme).
                m_Body = Spr("block", PixelTheme.Name(PresentationCatalog.ObstructionSingleSprite(o.TypeId) ?? "block_single_1"), Vector2.zero, LayerBody);
                return;
            }
            for (int dz = 0; dz < d; dz++)
            {
                for (int dx = 0; dx < w; dx++)
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
                    var sr = Spr("block", PixelTheme.Name(tile), new Vector2(left + dx + 0.5f, dz), LayerBody + (d - 1 - dz));
                    if (m_Body == null) m_Body = sr;
                }
            }
        }

        static readonly string[] s_BlockTiles =
        {
            "block_tl", "block_t", "block_tr",
            "block_l", "block_c", "block_r",
            "block_bl", "block_b", "block_br",
        };

        public void SetSelected(bool on)
        {
            m_Selected = on;
            if (on && m_Select == null)
            {
                // Золотые уголки вокруг footprint (9-slice), поверх объектов.
                var size = new Vector2(m_Obj.Width + 4f * Px, m_Obj.Depth + 4f * Px);
                var pos = BoardView.CenteredPos("select_bracket", new Vector2(0f, m_Obj.Depth / 2f), size);
                m_Select = PixelWorld.Sliced(m_Overlay, "select", "select_bracket", pos, size, m_OverlayOrder + OverlaySelect);
            }
            if (m_Select != null && m_Select.enabled != on) m_Select.enabled = on;
        }

        /// <summary>Удар по цели: дрожание на 1 пиксель арта (~0.12 с) и осколки цвета объекта.</summary>
        public void OnHit(Effects fx)
        {
            m_HitT = HitSeconds;
            if (fx != null) fx.Chips(transform.position + new Vector3(0f, 0f, Mathf.Min(m_TopZ, m_Obj.Depth) * 0.5f), BaseColor);
        }

        public void ApplyState(GameSession s, bool force)
        {
            float time = Time.time;
            if (m_Obj.Kind == ObjectKind.Obstruction)
            {
                // У препятствий нет состояния: только вода водопада.
                m_AnimA.Tick(time);
                m_AnimB.Tick(time);
                return;
            }
            var st = s.Mine.Objects[Ordinal];
            bool open = s.IsSectorOpen(m_Sector);
            bool payout = st.Gen != null && st.Gen.PayoutReady;
            bool auto = st.Gen != null && st.Gen.Automated;
            float dt = Time.deltaTime;

            if (force || st.Status != m_LastStatus || open != m_LastOpen || payout != m_LastPayout || auto != m_LastAuto)
            {
                // Смена во время игры (не первая синхронизация после сборки) — створки ворот открываются анимацией.
                bool live = m_Applied;
                var prev = m_LastStatus;
                m_Applied = true;
                m_LastStatus = st.Status;
                m_LastOpen = open;
                m_LastPayout = payout;
                m_LastAuto = auto;
                bool destroyed = m_Obj.Kind == ObjectKind.Rock && st.Status == ObjectStatus.Destroyed;
                if (gameObject.activeSelf == destroyed) gameObject.SetActive(!destroyed);
                ApplyStatus(st.Status, prev, live);
                if (m_Obj.Kind == ObjectKind.Shaft) ApplyShaftState(st.Status, payout, auto);
                UpdateStatusBubble(st, payout, auto);
            }

            if (m_Hp != null) UpdateHp(s, st, open);

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

            switch (m_Obj.Kind)
            {
                case ObjectKind.Shaft:
                    // Колесо копра крутится, пока шахта производит (ручная — пока не готова выплата); готовая выплата блестит.
                    if (st.Status == ObjectStatus.Producing && (auto || !payout)) m_AnimA.Tick(time);
                    if (payout) m_AnimB.Tick(time);
                    break;
                case ObjectKind.Cart:
                    m_AnimA.Tick(time);
                    if (st.Status == ObjectStatus.Producing && time >= m_NextSmoke)
                    {
                        m_NextSmoke = time + SmokeSeconds;
                        var fx = m_Board.Fx;
                        if (fx != null) fx.Smoke(ChimneyWorld());
                    }
                    break;
                case ObjectKind.Gate:
                    if (m_DoorAnim)
                    {
                        m_DoorT += dt;
                        int f = Mathf.Min(s_DoorOpenFrames.Length - 1, (int)(m_DoorT / DoorFrameSeconds));
                        if (f != m_DoorFrame)
                        {
                            m_DoorFrame = f;
                            PixelWorld.SetSprite(m_Door, s_DoorOpenFrames[f]);
                        }
                        if (f >= s_DoorOpenFrames.Length - 1) m_DoorAnim = false;
                    }
                    break;
            }

            if (m_BubbleBobbing && m_Bubble != null)
            {
                // Пузырь, ждущий тапа, покачивается на 0–2 пикселя арта (целыми пикселями, без дрожания).
                float off = Mathf.Round((Mathf.Sin(time * 4f) * 0.5f + 0.5f) * 2f) * Px;
                if (off != m_BubbleOffset)
                {
                    m_BubbleOffset = off;
                    m_Bubble.transform.localPosition = new Vector3(0f, 0f, off);
                }
            }
        }

        /// <summary>Мировая точка устья трубы кузницы (для дыма).</summary>
        Vector3 ChimneyWorld()
        {
            float dx = (ForgeChimneyPixel.x - ForgeCanvasW / 2f) * Px;
            float dz = (ForgeCanvasH - ForgeChimneyPixel.y) * Px;
            return transform.position + new Vector3(dx, 0f, dz);
        }

        void ApplyStatus(ObjectStatus status, ObjectStatus prev, bool live)
        {
            switch (m_Obj.Kind)
            {
                case ObjectKind.Shaft:
                    SetEnabled(m_Rubble, status == ObjectStatus.Intact);
                    SetEnabled(m_ShaftLight, status == ObjectStatus.Producing);
                    break;
                case ObjectKind.Gate:
                    if (status == ObjectStatus.Opened)
                    {
                        if (live && prev != ObjectStatus.Opened)
                        {
                            m_DoorAnim = true;
                            m_DoorT = 0f;
                            m_DoorFrame = -1;
                        }
                        else if (!m_DoorAnim) PixelWorld.SetSprite(m_Door, "gate_door_open");
                    }
                    else
                    {
                        m_DoorAnim = false;
                        PixelWorld.SetSprite(m_Door, status == ObjectStatus.Broken ? "gate_door_broken" : "gate_door_closed");
                    }
                    break;
                case ObjectKind.Exit:
                    SetEnabled(m_ExitBoards, status == ObjectStatus.Intact);
                    SetEnabled(m_ExitGlow, status != ObjectStatus.Intact);
                    break;
            }
        }

        /// <summary>
        /// Различимые состояния шахты (ТЗ 8.3): завал — доски и камни (ApplyStatus); готова к активации — лента с бантом;
        /// работает — свет и колесо; готова выплата — горка самоцветов и блеск; автоматизирована — табличка управляющего.
        /// </summary>
        void ApplyShaftState(ObjectStatus status, bool payout, bool auto)
        {
            bool producing = status == ObjectStatus.Producing;
            SetEnabled(m_ShaftRibbon, status == ObjectStatus.ReadyToActivate);
            SetEnabled(m_ShaftHeap, producing && payout);
            SetEnabled(m_ShaftGlint, producing && payout);
            SetEnabled(m_ShaftPlaque, producing && auto);
        }

        static void SetEnabled(SpriteRenderer sr, bool on)
        {
            if (sr != null && sr.enabled != on) sr.enabled = on;
        }

        void UpdateHp(GameSession s, ObjectState st, bool open)
        {
            bool intact = st.Status == ObjectStatus.Intact && open;
            int workers = intact ? s.WorkersOn(Ordinal) : 0;
            if (!m_HpKnown || st.Hp != m_LastHp)
            {
                // Доля HP пересчитывается только при ударе, а не каждый кадр.
                m_HpKnown = true;
                m_LastHp = st.Hp;
                m_Damaged = st.Hp < m_MaxHp;
                m_HpFrac = m_MaxHp.IsPositive ? Mathf.Clamp01((float)(st.Hp / m_MaxHp).ToDouble()) : 0f;
            }
            // Остаток HP и прогресс — при работе, выборе или уже начатой цели (ТЗ 8.2).
            bool show = intact && (workers > 0 || m_Selected || m_Damaged);
            float frac = show ? m_HpFrac : 0f;
            if (show != m_HpShown || frac != m_HpShownFrac)
            {
                m_HpShown = show;
                m_HpShownFrac = frac;
                m_Hp.Set(frac, show);
            }
            if (m_Crack != null)
            {
                // Стадии повреждения камня: меньше 75 % HP — трещины, меньше 45 % — сколы, меньше 20 % — откушен и осыпь.
                int stage = st.Status != ObjectStatus.Intact || !m_Damaged ? 0 : (m_HpFrac < 0.2f ? 3 : (m_HpFrac < 0.45f ? 2 : (m_HpFrac < 0.75f ? 1 : 0)));
                if (stage != m_CrackStage)
                {
                    m_CrackStage = stage;
                    string sprite = m_DamageSprites != null ? m_DamageSprites[stage] : null;
                    if (stage == 0 || sprite != null)
                    {
                        PixelWorld.SetSprite(m_Body, m_DamageSprites[stage]);
                        m_Crack.enabled = false;
                    }
                    else
                    {
                        PixelWorld.SetSprite(m_Crack, s_CrackFrames[Mathf.Min(2, stage)]);
                        m_Crack.enabled = true;
                    }
                }
            }
            UpdateInfoLabel(s, intact, workers);
        }

        /// <summary>
        /// Метка над целью (ТЗ 8.2, 13.1): «ур. N»; при работе или выборе — «· ~ETA» по назначенным работникам,
        /// при выборе без работников — «· Нет работников» (не бесконечность). Текст TMP меняется,
        /// только когда меняется видимое значение (уровень или округлённое время).
        /// </summary>
        void UpdateInfoLabel(GameSession s, bool intact, int workers)
        {
            if (m_LevelText == null) return;
            long key = InfoHidden;
            double eta = 0;
            // В закрытом (затемнённом) секторе уровень не показывается: подписи там только загромождают поле.
            bool open = m_Obj.Kind == ObjectKind.Gate ? s.IsTargetAvailable(Ordinal) : s.IsCellOpen(m_Obj.Cells[0]);
            if (intact && (open || m_Selected))
            {
                key = InfoLevelOnly;
                if (workers > 0 || m_Selected)
                {
                    eta = s.EtaSeconds(Ordinal);
                    if (double.IsInfinity(eta) || double.IsNaN(eta)) key = m_Selected ? InfoNoWorkers : InfoLevelOnly;
                    else
                    {
                        eta = System.Math.Min(System.Math.Max(eta, 0), 1e15);
                        key = EtaKey(eta);
                    }
                }
            }
            if (key == m_InfoKey) return;
            bool visible = key != InfoHidden;
            // Смена текста у видимой метки — не чаще 10 раз в секунду (на ×100 ETA меняется каждый кадр).
            if (visible && m_InfoKey != InfoHidden && Time.unscaledTime < m_InfoNextText) return;
            if (visible && m_Info == null)
            {
                if (s_InfoBudgetFrame != Time.frameCount)
                {
                    s_InfoBudgetFrame = Time.frameCount;
                    s_InfoCreated = 0;
                }
                if (s_InfoCreated >= InfoCreateBudget) return;   // создадим в следующем кадре
                s_InfoCreated++;
                m_Info = Label3D.Create(transform, new Vector3(0f, 0f, InfoZ(true)), Label3D.SmallSize, Palette.TargetLabel);
                m_Info.SetOrder(m_OverlayOrder + OverlayText);
                m_InfoCompact = true;
            }
            m_InfoKey = key;
            if (m_Info == null) return;
            if (m_Info.gameObject.activeSelf != visible) m_Info.gameObject.SetActive(visible);
            if (!visible) return;
            // Только уровень — мелко, сразу над телом; с ETA (работа или выбор) — крупнее и чуть выше.
            bool compact = key == InfoLevelOnly;
            if (compact != m_InfoCompact)
            {
                m_InfoCompact = compact;
                m_Info.transform.localPosition = new Vector3(0f, 0f, InfoZ(compact));
                m_Info.Text.fontSize = Label3D.SmallSize;
            }
            m_InfoNextText = Time.unscaledTime + 0.1f;
            if (key == InfoNoWorkers) m_Info.Text.text = Loc.F("board.info.noWorkers", m_LevelText);
            else if (key >= 0) m_Info.Text.text = Loc.F("board.info.eta", m_LevelText, NumberFormat.FormatDuration(eta));
            else m_Info.Text.text = m_LevelText;
        }

        /// <summary>
        /// Высота подписи над опорой: над верхом тела, с ETA (две строки: уровень и время) — чуть выше. У камня подпись не
        /// выходит из его клеток: однострочная — не выше Depth − 5 px, двухстрочная — на 4 px ниже, чтобы верхняя строка
        /// стояла там же. Иначе подпись ложится на основание и полосу HP камня строкой выше.
        /// </summary>
        float InfoZ(bool compact)
        {
            float z = m_TopZ + (compact ? 4f : 7f) * Px;
            if (m_Obj.Kind == ObjectKind.Rock) z = Mathf.Min(z, m_Obj.Depth - (compact ? 5f : 9f) * Px);
            return PixelWorld.Snap(z);
        }

        /// <summary>Ключ ETA с точностью показа NumberFormat.FormatDuration: секунды, минуты (от часа), часы (от 48 ч).</summary>
        static long EtaKey(double eta)
        {
            long sec = (long)System.Math.Ceiling(eta - 1e-9);
            if (sec < 3600) return sec;
            if (sec < 48 * 3600) return (1L << 40) + sec / 60;
            return (2L << 40) + sec / 3600;
        }

        /// <summary>Пузырь статуса шахты, ворот и выхода; меняется только при смене состояния.</summary>
        void UpdateStatusBubble(ObjectState st, bool payout, bool auto)
        {
            string text = null;
            bool attention = false;
            switch (m_Obj.Kind)
            {
                case ObjectKind.Shaft:
                    if (st.Status == ObjectStatus.ReadyToActivate)
                    {
                        text = Loc.T("board.status.activate");
                        attention = true;
                    }
                    else if (payout)
                    {
                        text = Loc.T("board.status.collect");
                        attention = true;
                    }
                    else if (auto) text = Loc.T("board.status.auto");
                    break;
                case ObjectKind.Gate:
                    if (st.Status == ObjectStatus.Broken)
                    {
                        text = Loc.T("board.status.openGate");
                        attention = true;
                    }
                    break;
                case ObjectKind.Exit:
                    if (st.Status == ObjectStatus.Broken)
                    {
                        text = Loc.T("board.status.exitOpen");
                        attention = true;
                    }
                    break;
                default:
                    return;
            }
            if (string.IsNullOrEmpty(text))
            {
                if (m_Bubble != null && m_Bubble.activeSelf) m_Bubble.SetActive(false);
                if (m_BubbleHit != null && m_BubbleHit.activeSelf) m_BubbleHit.SetActive(false);
                m_BubbleBobbing = false;
                return;
            }
            if (m_Bubble == null) BuildBubble();
            if (!m_Bubble.activeSelf) m_Bubble.SetActive(true);
            if (!m_BubbleHit.activeSelf) m_BubbleHit.SetActive(true);
            m_BubbleBobbing = attention;
            if (!attention && m_BubbleOffset != 0f)
            {
                m_BubbleOffset = 0f;
                m_Bubble.transform.localPosition = Vector3.zero;
            }
            if (text == m_BubbleText) return;
            m_BubbleText = text;
            m_BubbleLabel.Text.text = text;
            LayoutBubble(text);
        }

        void BuildBubble()
        {
            m_Bubble = new GameObject("bubble");
            m_Bubble.transform.SetParent(transform, false);
            var visual = PixelWorld.Visual(m_Bubble.transform);
            m_BubbleTail = PixelWorld.Sprite(visual, "tail", "bubble_world_tail", Vector2.zero, m_OverlayOrder + OverlayBubble);
            m_BubbleBody = PixelWorld.Sliced(visual, "body", "bubble_world", Vector2.zero, Vector2.one, m_OverlayOrder + OverlayBubble);
            m_BubbleLabel = Label3D.Create(m_Bubble.transform, Vector3.zero, Label3D.SmallSize, PixelPalette.Ink2);
            m_BubbleLabel.SetOrder(m_OverlayOrder + OverlayText);
            m_BubbleOffset = 0f;

            // Пузырь висит над строкой выше объекта и зовёт тапнуть: тап по нему — тап по объекту, а не по камню
            // или пустой клетке под ним. Коллайдер выше коллайдеров поля (y до 1.1) — ближе к камере, выигрывает у них.
            m_BubbleHit = new GameObject("bubble_hit");
            m_BubbleHit.transform.SetParent(transform, false);
            m_BubbleCollider = m_BubbleHit.AddComponent<BoxCollider>();
            m_BubbleCollider.center = new Vector3(0f, 0.6f, m_TopZ + 0.5f);
            m_BubbleCollider.size = new Vector3(1f, 1f, 1f);
            var pick = m_BubbleHit.AddComponent<Pickable>();
            pick.Kind = Pickable.PickKind.Object;
            pick.Id = m_Obj.Ordinal;
            pick.Bubble = true;
        }

        /// <summary>Размер пузыря по тексту (целые пиксели арта): хвостик кончиком над телом объекта, текст по центру тела.</summary>
        void LayoutBubble(string text)
        {
            var pref = m_BubbleLabel.Text.GetPreferredValues(text);
            float w = Mathf.Ceil((Mathf.Max(16f * Px, pref.x + 10f * Px)) * PixelArt.CellPixels / 2f) * 2f * Px;
            float h = Mathf.Ceil(Mathf.Max(14f * Px, pref.y + 4f * Px) * PixelArt.CellPixels) * Px;
            float tip = m_TopZ + Px;
            var tail = m_BubbleTail.sprite;
            float tailH = tail != null && PixelArt.Has("bubble_world_tail") ? tail.bounds.size.y : 4f * Px;
            float bottom = PixelWorld.Snap(tip + tailH - Px);
            float cy = bottom + h / 2f;
            m_BubbleTail.transform.localPosition = new Vector3(0f, tip, 0f);
            m_BubbleBody.size = new Vector2(w, h);
            var pos = BoardView.CenteredPos("bubble_world", new Vector2(0f, cy), new Vector2(w, h));
            m_BubbleBody.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
            m_BubbleLabel.transform.localPosition = new Vector3(0f, 0f, cy);
            // Зона тапа — от кончика хвостика до верха тела с запасом на покачивание (2 пикселя арта).
            float top = bottom + h + 2f * Px;
            m_BubbleCollider.center = new Vector3(0f, 0.6f, (tip + top) / 2f);
            m_BubbleCollider.size = new Vector3(w, 1f, top - tip);
        }
    }
}
