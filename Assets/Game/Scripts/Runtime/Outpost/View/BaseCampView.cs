using CatMiners.Core.Outpost;
using UnityEngine;
using BigInteger = System.Numerics.BigInteger;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Лагерь базы под картой (ТЗ 5, 7.4; RUNTIME_SPEC §4.3; раскладка ART_NAMES §3.2): наклейки мест склада (свободно/занято),
    /// жёлоба, приёмный склад со светом приёма, индикатор обслуживания над каждым местом (доля текущего Unload-действия,
    /// иконка склада и фиксированная стоимость обслуживаемой порции), ящики с монетами по доле доставленного груза шахты,
    /// доска найма / верстак / стеллаж / вагонетка по ступеням локальных веток, шкаф (открыт, когда есть ненадетые предметы),
    /// фонари и вывеска. Тап по постройкам — выбор «База». Склад товаров (ТЗ 2.2 §6.1, только в шахте с заводами) — столбец
    /// baseCol + 4 заднего ряда: пустой или с товаром, вход труб на коньке, свой коллайдер <see cref="OutpostPickKind.Warehouse"/>
    /// (тап — рынок). Лагерь не перекрашивается темами. Только вид: склад и золото — ядро.
    /// Координаты — локальные в корне поля (строки лагеря z = −0,5…−2,5, двор −3…−6).
    /// </summary>
    public sealed class BaseCampView : MonoBehaviour
    {
        const float Px = 1f / PixelArt.CellPixels;
        /// <summary>Ноги заднего ряда построек (нижняя кромка st1) и переднего ряда (нижняя кромка st2 + 1 px).</summary>
        const float BackFeetZ = -5f, FrontFeetZ = -6f + Px;
        const float FlashSeconds = 0.2f, FlashPeak = 1.125f;
        const float CrateBumpSeconds = 0.14f;
        static readonly string[] s_Hire = { "hire_board_0", "hire_board_1", "hire_board_2" };
        static readonly string[] s_Bench = { "bench_0", "bench_1", "bench_2" };
        static readonly string[] s_Rack = { "rack_0", "rack_1", "rack_2" };
        static readonly string[] s_Crates = { "depot_crate_0", "depot_crate_1", "depot_crate_2", "depot_crate_3" };
        static readonly string[] s_Lantern = { "camp_lantern_0", "camp_lantern_1" };

        enum Building { Hire, Bench, Rack, Cart, Depot, Wardrobe, CrateL, CrateR, Count }

        OutpostMap m_Map;
        Transform m_Visual, m_Overlay;
        SpriteRenderer[] m_Slot = new SpriteRenderer[OutpostIds.DepotSlotCount];
        SpriteRenderer[] m_Chute = new SpriteRenderer[OutpostIds.DepotSlotCount];
        SpriteRenderer[] m_Crate = new SpriteRenderer[OutpostIds.DepotSlotCount];
        SpriteRenderer[] m_SlotIcon = new SpriteRenderer[OutpostIds.DepotSlotCount];
        HpBar[] m_SlotBar = new HpBar[OutpostIds.DepotSlotCount];
        Label3D[] m_SlotValue = new Label3D[OutpostIds.DepotSlotCount];
        SpriteRenderer m_Depot, m_DepotGlow, m_Hire, m_Bench, m_Rack, m_Cart, m_Wardrobe, m_Select;
        // Склад товаров (ТЗ 2.2 §6.1): null — в шахте нет заводов.
        SpriteRenderer m_Warehouse, m_WarehouseInlet;
        int m_WarehouseFull = -1;
        float m_WarehouseBump;
        SpriteRenderer[] m_Lanterns = new SpriteRenderer[2];
        float[] m_LanternNext = new float[2];
        int[] m_LanternFrame = new int[2];
        readonly Transform[] m_Buildings = new Transform[(int)Building.Count];
        readonly float[] m_FlashT = new float[(int)Building.Count];
        readonly float[] m_ScaleShown = new float[(int)Building.Count];
        readonly float[] m_CrateBump = new float[OutpostIds.DepotSlotCount];

        // Последние показанные значения: спрайты и тексты меняются только при смене.
        int m_HireTier = -1, m_BenchTier = -1, m_RackTier = -1, m_CartTier = -1, m_ChuteTier = -1, m_CrateFill = -1;
        bool m_WardrobeOpen;
        /// <summary>Шкаф открыт: в коллекции есть ненадетые предметы.</summary>
        public bool WardrobeOpen => m_WardrobeOpen;
        bool m_WardrobeDirty = true;
        bool m_GlowOn = true;
        readonly bool[] m_SlotActive = { true, true };
        readonly bool[] m_SlotServing = { true, true };
        readonly float[] m_SlotFrac = { -1f, -1f };
        readonly BigInteger[] m_SlotValueShown = { BigInteger.MinusOne, BigInteger.MinusOne };
        int m_TotalUnits = -1;
        bool m_Selected;

        /// <summary>Центр лагеря (площадка и двор) локально в корне поля.</summary>
        public Vector3 CenterLocal { get; private set; }
        /// <summary>Перед доской найма: отсюда новый котик выходит к клетке спавна.</summary>
        public Vector3 HireExitLocal { get; private set; }
        public Vector3 DepotCenterLocal { get; private set; }
        /// <summary>Склад товаров построен (в шахте есть заводы).</summary>
        public bool HasWarehouse => m_Warehouse != null;
        /// <summary>Опора склада товаров (низ спрайта) локально в корне поля.</summary>
        public Vector3 WarehouseLocal { get; private set; }
        /// <summary>Вход труб на коньке склада (центр fpipe_inlet) локально в корне поля: сюда приходят трубы и капсулы.</summary>
        public Vector3 WarehouseInletLocal { get; private set; }
        /// <summary>Точка «+N» прибытия товара и начала полёта монет продажи — над складом.</summary>
        public Vector3 WarehouseIndicatorLocal { get; private set; }
        /// <summary>Центр склада (середина спрайта) локально в корне поля.</summary>
        public Vector3 WarehouseCenterLocal { get; private set; }

        public static BaseCampView Create(Transform parent, OutpostMap map)
        {
            var go = new GameObject("BaseCamp");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<BaseCampView>();
            v.Build(map);
            return v;
        }

        Vector3 CellLocal(int cell) => new Vector3(m_Map.Col(cell) + 0.5f, 0f, m_Map.Depth - m_Map.Row(cell) - 0.5f);

        /// <summary>Центр места склада slot (клетка DepotCells[slot]) локально в корне поля.</summary>
        public Vector3 SlotLocal(int slot) => CellLocal(m_Map.DepotCells[Mathf.Clamp(slot, 0, m_Map.DepotCells.Length - 1)]);

        /// <summary>Воронка склада над местом slot (верх depot_base): сюда падают сданные мешочки.</summary>
        public Vector3 FunnelLocal(int slot)
        {
            var s = SlotLocal(slot);
            return new Vector3(s.x, 0f, -3.2f);
        }

        /// <summary>Мировая точка «+N» и полёта монет над местом склада (контроллер передаёт её в Hud.FlyLoot).</summary>
        public Vector3 SlotIndicatorLocal(int slot)
        {
            var s = SlotLocal(slot);
            return new Vector3(s.x, 0f, s.z + 0.95f);
        }

        void Build(OutpostMap map)
        {
            m_Map = map;
            int width = map.Width;
            int baseCol = map.Col(map.SpawnCell);
            m_Visual = PixelWorld.Visual(transform, "camp");
            m_Overlay = PixelWorld.Visual(transform, "campOverlay");
            CenterLocal = new Vector3(width / 2f, 0f, -3f);
            DepotCenterLocal = new Vector3(baseCol + 0.5f, 0f, -4f);
            HireExitLocal = new Vector3(baseCol - 1.5f, 0f, -2.6f);

            for (int s = 0; s < OutpostIds.DepotSlotCount && s < map.DepotCells.Length; s++)
            {
                var c = SlotLocal(s);
                m_Slot[s] = PixelWorld.Sprite(m_Visual, "slot" + s, "depot_slot", new Vector2(c.x, c.z), PixelWorld.FloorDecalOrder);
                if (PixelArt.Has("depot_chute"))
                {
                    m_Chute[s] = PixelWorld.Sprite(m_Visual, "chute" + s, "depot_chute", new Vector2(c.x, c.z - 1f), PixelWorld.FloorDecalOrder + 1);
                    m_Chute[s].enabled = false;
                }
                m_Crate[s] = Decor("crate" + s, s_Crates[0], c.x, FrontFeetZ);
                m_Buildings[s == 0 ? (int)Building.CrateL : (int)Building.CrateR] = m_Crate[s].transform;

                // Индикатор обслуживания над местом: иконка склада, полоса доли порции, стоимость порции.
                float barZ = c.z + 0.72f;
                m_SlotBar[s] = HpBar.Create(transform, new Vector3(c.x + 4f * Px, 0f, barZ), 0.6f);
                m_SlotIcon[s] = PixelWorld.Sprite(m_Overlay, "slotIcon" + s, "icon_depot", new Vector2(c.x - 13f * Px, barZ), PixelWorld.OverlayOrderAt(transform.position.z + barZ));
                m_SlotIcon[s].enabled = false;
                m_SlotValue[s] = Label3D.Create(transform, new Vector3(c.x, 0.1f, c.z + 0.95f), Label3D.SmallSize, Palette.Highlight);
                m_SlotValue[s].gameObject.SetActive(false);
            }

            m_Depot = Decor("depot", "depot_base", baseCol + 0.5f, BackFeetZ);
            m_DepotGlow = PixelWorld.Sprite(m_Depot.transform, "glow", "depot_glow", Vector2.zero, m_Depot.sortingOrder + 1);
            m_Buildings[(int)Building.Depot] = m_Depot.transform;
            m_Wardrobe = Decor("wardrobe", "wardrobe_closed", Mathf.Max(0, baseCol - 3) + 0.5f, BackFeetZ);
            m_Buildings[(int)Building.Wardrobe] = m_Wardrobe.transform;
            m_Hire = Decor("hire", s_Hire[0], Mathf.Max(0, baseCol - 2) + 0.5f, BackFeetZ);
            m_Buildings[(int)Building.Hire] = m_Hire.transform;
            m_Bench = Decor("bench", s_Bench[0], Mathf.Min(width - 1, baseCol + 2) + 0.5f, BackFeetZ);
            m_Buildings[(int)Building.Bench] = m_Bench.transform;
            m_Rack = Decor("rack", s_Rack[0], Mathf.Min(width - 1, baseCol + 3) + 0.5f, BackFeetZ);
            m_Buildings[(int)Building.Rack] = m_Rack.transform;
            m_Cart = Decor("cart", "cart_0", Mathf.Min(width - 1, baseCol + 2) + 0.5f, FrontFeetZ);
            m_Buildings[(int)Building.Cart] = m_Cart.transform;
            Decor("sign", "camp_sign", Mathf.Max(0, baseCol - 2) + 0.5f, FrontFeetZ);
            if (map.Factories != null && map.Factories.Length > 0) BuildWarehouse(Mathf.Min(width - 1, baseCol + 4));
            // Фонари — передний ряд столбцов b − 3 и b + 3 (перед шкафом и стеллажом), как в прежней раскладке на 7 столбцов,
            // где это были края массива. В широких картах края массива — порода далеко за девятью столбцами лагеря (ворота G3).
            m_Lanterns[0] = Decor("lantern0", s_Lantern[0], Mathf.Max(0, baseCol - 3) + 0.5f, FrontFeetZ);
            m_Lanterns[1] = Decor("lantern1", s_Lantern[0], Mathf.Min(width - 1, baseCol + 3) + 0.5f, FrontFeetZ);
            for (int i = 0; i < 2; i++) m_LanternNext[i] = Time.time + Random.Range(0.3f, 0.8f);
            for (int i = 0; i < m_ScaleShown.Length; i++)
            {
                m_ScaleShown[i] = 1f;
                m_FlashT[i] = 1f;
            }

            // Тап по двору с постройками — панель «База» (площадку, где ходят котики, не занимаем).
            var col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(width / 2f, 0.4f, -4.5f);
            col.size = new Vector3(width, 0.8f, 3f);
            OutpostPickable.Add(gameObject, OutpostPickKind.Base, 0);
        }

        /// <summary>
        /// Склад товаров в столбце <paramref name="col"/> заднего ряда (ноги на нижней кромке st1, как у построек 1×2): здание
        /// warehouse_empty/_full, вход труб fpipe_inlet в пикселе (16, 4) холста от левого верхнего угла (над крышей) и коллайдер
        /// склада поверх общего коллайдера двора (приоритет выше «Базы»).
        /// </summary>
        void BuildWarehouse(int col)
        {
            float x = col + 0.5f;
            m_Warehouse = Decor("warehouse", "warehouse_empty", x, BackFeetZ);
            WarehouseLocal = new Vector3(PixelWorld.Snap(x), 0f, BackFeetZ);
            float inletZ = BackFeetZ + (64f - 4f) * Px;
            WarehouseInletLocal = new Vector3(PixelWorld.Snap(x), 0f, inletZ);
            WarehouseCenterLocal = new Vector3(PixelWorld.Snap(x), 0f, BackFeetZ + 1f);
            WarehouseIndicatorLocal = new Vector3(PixelWorld.Snap(x), 0f, inletZ + 10f * Px);
            if (PixelArt.Has("fpipe_inlet"))
                m_WarehouseInlet = PixelWorld.Sprite(m_Visual, "warehouseInlet", "fpipe_inlet", new Vector2(PixelWorld.Snap(x), inletZ), m_Warehouse.sortingOrder + 1);

            var pick = new GameObject("warehousePick");
            pick.transform.SetParent(transform, false);
            var box = pick.AddComponent<BoxCollider>();
            box.center = new Vector3(x, 0.45f, BackFeetZ + 1f);
            box.size = new Vector3(0.95f, 0.9f, 2f);
            OutpostPickable.Add(pick, OutpostPickKind.Warehouse, 0);
        }

        /// <summary>Партия пришла на склад: здание подпрыгивает на пиксель.</summary>
        public void OnGoodsArrived()
        {
            if (m_Warehouse != null) m_WarehouseBump = CrateBumpSeconds;
        }

        /// <summary>Постройка с опорой в (x, z): порядок по z опоры, как декор поля.</summary>
        SpriteRenderer Decor(string name, string sprite, float x, float z) =>
            PixelWorld.Sprite(m_Visual, name, sprite, new Vector2(PixelWorld.Snap(x), PixelWorld.Snap(z)), PixelWorld.OrderAt(z));

        /// <summary>Шкаф пересчитать (сменился комплект или коллекция): по событиям экипировки и награды.</summary>
        public void MarkWardrobeDirty() => m_WardrobeDirty = true;

        /// <summary>Привести лагерь к ядру (каждый кадр, без выделений; тексты — только при смене стоимости порции).</summary>
        public void Sync(OutpostSimulation sim, float dt)
        {
            var run = sim.Run;
            var b = run.Balance;
            SetTier(ref m_HireTier, OutpostLook.Tier(run.Levels[(int)UpgradeBranch.Team], b.Branch(UpgradeBranch.Team).Cap), m_Hire, s_Hire);
            SetTier(ref m_BenchTier, OutpostLook.Tier(run.Levels[(int)UpgradeBranch.Pickaxes], b.Branch(UpgradeBranch.Pickaxes).Cap), m_Bench, s_Bench);
            SetTier(ref m_RackTier, OutpostLook.Tier(run.Levels[(int)UpgradeBranch.Bags], b.Branch(UpgradeBranch.Bags).Cap), m_Rack, s_Rack);
            int cart = OutpostLook.Tier(run.Levels[(int)UpgradeBranch.Boots], b.Branch(UpgradeBranch.Boots).Cap) > 0 ? 1 : 0;
            if (cart != m_CartTier)
            {
                m_CartTier = cart;
                PixelWorld.SetSprite(m_Cart, cart > 0 ? "cart_1" : "cart_0");
            }
            int chute = OutpostLook.Tier(run.Levels[(int)UpgradeBranch.Unload], b.Branch(UpgradeBranch.Unload).Cap) > 0 ? 1 : 0;
            if (chute != m_ChuteTier)
            {
                m_ChuteTier = chute;
                for (int s = 0; s < m_Chute.Length; s++)
                    if (m_Chute[s] != null) m_Chute[s].enabled = chute > 0;
            }

            // Ящики: 0 — ничего не доставлено, 1 — что-то, 2 — ≥ ⅓ груза шахты, 3 — ≥ ⅔.
            if (m_TotalUnits < 0)
            {
                m_TotalUnits = 0;
                for (int i = 0; i < run.Targets.Count; i++) m_TotalUnits += run.Targets[i].TotalUnits;
            }
            int delivered = run.Counters.DeliveredUnits;
            int fill = delivered <= 0 ? 0 : (m_TotalUnits <= 0 || delivered * 3L >= m_TotalUnits * 2L ? 3 : (delivered * 3L >= m_TotalUnits ? 2 : 1));
            if (fill != m_CrateFill)
            {
                m_CrateFill = fill;
                for (int s = 0; s < m_Crate.Length; s++)
                    if (m_Crate[s] != null) PixelWorld.SetSprite(m_Crate[s], s_Crates[fill]);
            }

            if (m_WardrobeDirty)
            {
                m_WardrobeDirty = false;
                m_WardrobeOpen = HasUnequipped(sim.Profile);
                PixelWorld.SetSprite(m_Wardrobe, m_WardrobeOpen ? "wardrobe_open" : "wardrobe_closed");
            }

            // Места склада: занято (резерв или обслуживание) — люк открыт; обслуживание — свет приёма и индикатор.
            bool anyServing = false;
            long now = run.Now;
            for (int s = 0; s < run.Slots.Length && s < m_Slot.Length; s++)
            {
                int wid = run.Slots[s].WorkerId;
                bool active = wid != 0;
                if (active != m_SlotActive[s])
                {
                    m_SlotActive[s] = active;
                    PixelWorld.SetSprite(m_Slot[s], active ? "depot_slot_active" : "depot_slot");
                }
                var w = active ? sim.WorkerById(wid) : null;
                var a = w?.Action;
                bool serving = w != null && w.Mode == WorkerMode.Unloading && a != null && a.Kind == ActionKind.Unload && w.Bag.Count > 0;
                anyServing |= serving;
                if (serving != m_SlotServing[s])
                {
                    m_SlotServing[s] = serving;
                    m_SlotIcon[s].enabled = serving;
                    m_SlotValue[s].gameObject.SetActive(serving);
                    m_SlotFrac[s] = -1f;
                    if (!serving) m_SlotBar[s].Set(0f, false);
                }
                if (!serving) continue;
                float frac = (float)a.ProgressAtForDisplay(now);
                if (frac != m_SlotFrac[s])
                {
                    m_SlotFrac[s] = frac;
                    m_SlotBar[s].Set(Mathf.Max(0.02f, frac), true);
                }
                var value = w.Bag[0].Value;
                if (value != m_SlotValueShown[s])
                {
                    m_SlotValueShown[s] = value;
                    m_SlotValue[s].Text.text = OutpostBoardView.MoneyText(value);
                }
            }
            if (anyServing != m_GlowOn)
            {
                m_GlowOn = anyServing;
                m_DepotGlow.enabled = anyServing;
            }

            if (m_Warehouse != null)
            {
                int full = FactoryQueries.AnyStock(run) ? 1 : 0;
                if (full != m_WarehouseFull)
                {
                    m_WarehouseFull = full;
                    PixelWorld.SetSprite(m_Warehouse, full > 0 ? "warehouse_full" : "warehouse_empty");
                }
            }

            UpdateLanterns();
            UpdateFlash(dt);
        }

        static void SetTier(ref int shown, int tier, SpriteRenderer sr, string[] sprites)
        {
            if (tier == shown) return;
            shown = tier;
            PixelWorld.SetSprite(sr, sprites[Mathf.Clamp(tier, 0, sprites.Length - 1)]);
        }

        /// <summary>В коллекции есть предмет, который не надет (перебор словаря — без выделений).</summary>
        static bool HasUnequipped(OutpostProfile profile)
        {
            if (profile?.Items == null) return false;
            var eq = profile.Equipped;
            foreach (var kv in profile.Items)
            {
                if (kv.Value < 1) continue;
                bool worn = false;
                if (eq != null)
                    for (int i = 0; i < eq.Length; i++)
                        if (eq[i] == kv.Key)
                        {
                            worn = true;
                            break;
                        }
                if (!worn) return true;
            }
            return false;
        }

        void UpdateLanterns()
        {
            float time = Time.time;
            for (int i = 0; i < m_Lanterns.Length; i++)
            {
                if (time < m_LanternNext[i] || m_Lanterns[i] == null) continue;
                m_LanternFrame[i] ^= 1;
                m_LanternNext[i] = time + Random.Range(0.3f, 0.8f);
                PixelWorld.SetSprite(m_Lanterns[i], s_Lantern[m_LanternFrame[i]]);
            }
        }

        void UpdateFlash(float dt)
        {
            for (int i = 0; i < m_Buildings.Length; i++)
            {
                var t = m_Buildings[i];
                if (t == null) continue;
                float s = 1f;
                if (m_FlashT[i] < 1f)
                {
                    m_FlashT[i] = Mathf.Min(1f, m_FlashT[i] + dt / FlashSeconds);
                    s = 1f + Mathf.Sin(m_FlashT[i] * Mathf.PI) * (FlashPeak - 1f);
                }
                if (s == m_ScaleShown[i]) continue;
                m_ScaleShown[i] = s;
                t.localScale = new Vector3(s, s, 1f);
            }
            if (m_WarehouseBump > 0f && m_Warehouse != null)
            {
                m_WarehouseBump = Mathf.Max(0f, m_WarehouseBump - dt);
                var p = m_Warehouse.transform.localPosition;
                float baseZ = PixelWorld.Snap(BackFeetZ);
                p.y = m_WarehouseBump > CrateBumpSeconds * 0.5f ? baseZ + Px : baseZ;
                m_Warehouse.transform.localPosition = p;
            }
            for (int s = 0; s < m_CrateBump.Length; s++)
            {
                if (m_CrateBump[s] <= 0f || m_Crate[s] == null) continue;
                m_CrateBump[s] = Mathf.Max(0f, m_CrateBump[s] - dt);
                var p = m_Crate[s].transform.localPosition;
                float baseZ = PixelWorld.Snap(FrontFeetZ);
                p.y = m_CrateBump[s] > CrateBumpSeconds * 0.5f ? baseZ + Px : baseZ;
                m_Crate[s].transform.localPosition = p;
            }
        }

        /// <summary>Порция сдана на месте slot: ящик под воронкой подпрыгивает на пиксель.</summary>
        public void OnDeposit(int slot)
        {
            if (slot >= 0 && slot < m_CrateBump.Length) m_CrateBump[slot] = CrateBumpSeconds;
        }

        /// <summary>
        /// Вспышка постройки ветки (TeamUpgradePurchased): короткий «поп» спрайта, искорки и свет
        /// (<paramref name="boardOrigin"/> — мировая точка корня поля для эффектов).
        /// </summary>
        public void Flash(UpgradeBranch branch, Effects fx, Vector3 boardOrigin)
        {
            Building b;
            switch (branch)
            {
                case UpgradeBranch.Team: b = Building.Hire; break;
                case UpgradeBranch.Pickaxes: b = Building.Bench; break;
                case UpgradeBranch.Boots: b = Building.Cart; break;
                case UpgradeBranch.Bags: b = Building.Rack; break;
                case UpgradeBranch.Unload: b = Building.Depot; break;
                default: b = Building.CrateL; break;
            }
            FlashBuilding(b, fx, boardOrigin);
            if (b == Building.CrateL) FlashBuilding(Building.CrateR, fx, boardOrigin);
        }

        void FlashBuilding(Building b, Effects fx, Vector3 boardOrigin)
        {
            int i = (int)b;
            m_FlashT[i] = 0f;
            var t = m_Buildings[i];
            if (fx == null || t == null) return;
            var lp = t.localPosition;   // в узле визуала: x вправо, y — мировая z
            var at = boardOrigin + new Vector3(lp.x, 0f, lp.y + 0.8f);
            fx.Sparkle(at, 3);
            fx.Glow(at, PixelPalette.Amber4, 1.2f, 0.5f);
        }

        /// <summary>Мировая точка постройки ветки (камера).</summary>
        public Vector3 BranchLocal(UpgradeBranch branch)
        {
            Transform t;
            switch (branch)
            {
                case UpgradeBranch.Team: t = m_Buildings[(int)Building.Hire]; break;
                case UpgradeBranch.Pickaxes: t = m_Buildings[(int)Building.Bench]; break;
                case UpgradeBranch.Boots: t = m_Buildings[(int)Building.Cart]; break;
                case UpgradeBranch.Bags: t = m_Buildings[(int)Building.Rack]; break;
                case UpgradeBranch.Unload: t = m_Buildings[(int)Building.Depot]; break;
                default: t = m_Buildings[(int)Building.CrateL]; break;
            }
            if (t == null) return CenterLocal;
            var lp = t.localPosition;
            return new Vector3(lp.x, 0f, lp.y + 0.5f);
        }

        /// <summary>Выбор «База»: золотые уголки вокруг двора с постройками.</summary>
        public void SetSelected(bool on)
        {
            if (on == m_Selected) return;
            m_Selected = on;
            if (on && m_Select == null)
            {
                var size = new Vector2(m_Map.Width + 4f * Px, 3f + 4f * Px);
                var pos = BoardView.CenteredPos("select_bracket", new Vector2(m_Map.Width / 2f, -4.5f), size);
                m_Select = PixelWorld.Sliced(m_Overlay, "select", "select_bracket", pos, size, PixelWorld.OverlayOrderAt(transform.position.z - 3f));
            }
            if (m_Select != null) m_Select.enabled = on;
        }
    }
}
