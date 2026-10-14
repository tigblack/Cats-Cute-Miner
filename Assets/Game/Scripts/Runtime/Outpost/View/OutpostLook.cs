using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Имена спрайтов котика нового режима (Docs/Rework/ART_NAMES.md §2): окрас по <see cref="WorkerState.Look"/>, слои экипировки
    /// по надетому комплекту команды, груз по заполнению сумки, мешочек в кадрах подбора и сдачи. Индексы кадров — как
    /// manifest.CAT_FRAMES (прежние 62 + carry/pickup/unload). Таблицы строятся один раз, в кадре — без конкатенации.
    /// Локальные уровни веток внешний вид не меняют (ТЗ 16): только надетые предметы.
    /// </summary>
    public static class OutpostLook
    {
        /// <summary>Окрасы тела (manifest.FURS).</summary>
        public static readonly string[] Furs = { "ginger", "gray", "black", "white", "cream", "brown" };

        /// <summary>Ключ вида слоя: 0 — base (пустой слот), 1…4 — common, rare, epic, legendary (1 + ItemRarity).</summary>
        public static readonly string[] Kinds = { "base", "common", "rare", "epic", "legendary" };
        public const int KindBase = 0, KindCount = 5;

        /// <summary>Слои экипировки: порядок — как слоты ТЗ (кирка, шляпа, рюкзак, обувь), префиксы — как в атласе.</summary>
        public enum Layer
        {
            Pick = 0,
            Hat = 1,
            Pack = 2,
            Boots = 3,
        }

        public const int LayerCount = 4;
        static readonly string[] s_LayerPrefix = { "cat_pick_", "cat_hat_", "cat_pack_", "cat_boots_" };

        // Кадры (индексы manifest.CAT_FRAMES).
        public const int Idle = 0, IdleCount = 6;
        public const int Blink = 6, BlinkDown = 7;
        public const int Stretch = 8, StretchCount = 10;
        public const int Walk = 18, WalkCount = 8;
        public const int MineReady = 26, MineReady2 = 27, MineAntic = 28, MineRaise = 29, MineTop = 30, MineStrike = 31,
            MineImpact = 32, MineRecoil = 33, MineReturn = 34;
        public const int Drag = 35, DragCount = 4;
        public const int Spawn = 39, SpawnCount = 8;
        public const int Joy = 47, JoyCount = 6;
        public const int ToolReady = 53, ToolCount = 9;
        public const int Carry = 62, CarryCount = 8;
        public const int Pickup = 70, PickupCount = 4;
        public const int Unload = 74, UnloadCount = 4;
        public const int FrameCount = 78;

        /// <summary>Стадии груза: 1 — меньше 50 %, 2 — меньше 100 %, 3 — от 100 % (RUNTIME_SPEC §4.2).</summary>
        public const int LoadStages = 3;

        /// <summary>Имена кадров в порядке индексов.</summary>
        public static readonly string[] Frames = BuildFrames();

        static string[,] s_Body;
        static string[,,] s_Equip;
        static string[,] s_Load;
        static string[] s_Sack;

        static string[] BuildFrames()
        {
            var f = new string[FrameCount];
            for (int i = 0; i < IdleCount; i++) f[Idle + i] = "idle" + i;
            f[Blink] = "blink";
            f[BlinkDown] = "blink_d";
            for (int i = 0; i < StretchCount; i++) f[Stretch + i] = "stretch" + i;
            for (int i = 0; i < WalkCount; i++) f[Walk + i] = "walk" + i;
            string[] mine = { "ready", "ready2", "antic", "raise", "top", "strike", "impact", "recoil", "return" };
            for (int i = 0; i < mine.Length; i++)
            {
                f[MineReady + i] = "mine_" + mine[i];
                f[ToolReady + i] = "tool_" + mine[i];
            }
            for (int i = 0; i < DragCount; i++) f[Drag + i] = "drag" + i;
            for (int i = 0; i < SpawnCount; i++) f[Spawn + i] = "spawn" + i;
            for (int i = 0; i < JoyCount; i++) f[Joy + i] = "joy" + i;
            for (int i = 0; i < CarryCount; i++) f[Carry + i] = "carry" + i;
            for (int i = 0; i < PickupCount; i++) f[Pickup + i] = "pickup" + i;
            for (int i = 0; i < UnloadCount; i++) f[Unload + i] = "unload" + i;
            return f;
        }

        /// <summary>Кадр набора EQUIP_FRAMES (есть слои обуви, рюкзака, груза и убора): всё, кроме drag* и tool_*.</summary>
        public static bool IsEquipFrame(int f) => f >= 0 && f < FrameCount && !(f >= Drag && f < Drag + DragCount) && !(f >= ToolReady && f < ToolReady + ToolCount);

        /// <summary>Кадр набора PICK_FRAMES (есть кирка): EQUIP_FRAMES без carry/pickup/unload.</summary>
        public static bool IsPickFrame(int f) => IsEquipFrame(f) && f < Carry;

        /// <summary>Кадр набора SACK_FRAMES (мешочек в лапах): pickup1, pickup2, unload0, unload1, unload2.</summary>
        public static bool IsSackFrame(int f) => f == Pickup + 1 || f == Pickup + 2 || (f >= Unload && f <= Unload + 2);

        static void EnsureNames()
        {
            if (s_Body != null) return;
            s_Body = new string[Furs.Length, FrameCount];
            s_Equip = new string[LayerCount, KindCount, FrameCount];
            s_Load = new string[LoadStages, FrameCount];
            s_Sack = new string[FrameCount];
            for (int f = 0; f < FrameCount; f++)
            {
                for (int c = 0; c < Furs.Length; c++) s_Body[c, f] = "cat_body_" + Furs[c] + "_" + Frames[f];
                bool equip = IsEquipFrame(f), pick = IsPickFrame(f);
                for (int l = 0; l < LayerCount; l++)
                {
                    bool has = l == (int)Layer.Pick ? pick : equip;
                    for (int k = 0; k < KindCount; k++) s_Equip[l, k, f] = has ? s_LayerPrefix[l] + Kinds[k] + "_" + Frames[f] : null;
                }
                for (int s = 0; s < LoadStages; s++) s_Load[s, f] = equip ? "cat_load_" + (s + 1) + "_" + Frames[f] : null;
                s_Sack[f] = IsSackFrame(f) ? "cat_sack_" + Frames[f] : null;
            }
        }

        /// <summary>Окрас по WorkerState.Look (0…LookCount−1): стабилен после загрузки.</summary>
        public static int FurOf(int look) => ((look % Furs.Length) + Furs.Length) % Furs.Length;

        /// <summary>Слой экипировки слота ТЗ.</summary>
        public static Layer LayerOf(EquipSlot slot) =>
            slot == EquipSlot.Pickaxe ? Layer.Pick : (slot == EquipSlot.Hat ? Layer.Hat : (slot == EquipSlot.Backpack ? Layer.Pack : Layer.Boots));

        /// <summary>
        /// Ключ вида предмета (<see cref="Kinds"/>): пустой слот или неизвестный ID — base. Каталог даёт редкость; без каталога —
        /// суффикс ID «{slot}.{rarity}».
        /// </summary>
        public static int KindOf(string itemId, EquipmentCatalog catalog)
        {
            if (string.IsNullOrEmpty(itemId)) return KindBase;
            if (catalog != null && catalog.TryGet(itemId, out var def)) return 1 + (int)def.Rarity;
            int dot = itemId.LastIndexOf('.');
            if (dot < 0) return KindBase;
            for (int k = 1; k < KindCount; k++)
                if (itemId.Length - dot - 1 == Kinds[k].Length && string.CompareOrdinal(itemId, dot + 1, Kinds[k], 0, Kinds[k].Length) == 0) return k;
            return KindBase;
        }

        /// <summary>Ключ вида слоя по надетому комплекту профиля.</summary>
        public static int KindFor(OutpostProfile profile, EquipmentCatalog catalog, EquipSlot slot)
        {
            var eq = profile?.Equipped;
            string id = eq != null && eq.Length > (int)slot ? eq[(int)slot] : null;
            return KindOf(id, catalog);
        }

        /// <summary>Стадия груза 1…3 по заполнению сумки; 0 — сумка пуста (слоя нет).</summary>
        public static int LoadStage(int count, int capacity)
        {
            if (count <= 0) return 0;
            if (capacity <= 0 || count >= capacity) return 3;
            return count * 2 < capacity ? 1 : 2;
        }

        /// <summary>Ступень постройки лагеря: 0 — уровень ветки &lt; ⅓ cap, 1 — ≥ ⅓, 2 — ≥ ⅔ (ART_NAMES §3.2).</summary>
        public static int Tier(int level, int cap)
        {
            if (cap <= 0 || level <= 0) return 0;
            if (level * 3 >= cap * 2) return 2;
            return level * 3 >= cap ? 1 : 0;
        }

        public static string Body(int fur, int frame)
        {
            EnsureNames();
            return s_Body[fur, frame];
        }

        /// <summary>Имя слоя экипировки кадра; null — в этом кадре слоя нет.</summary>
        public static string Equip(Layer layer, int kind, int frame)
        {
            EnsureNames();
            return s_Equip[(int)layer, kind, frame];
        }

        /// <summary>Имя слоя груза стадии 1…3; null — в кадре слоя нет.</summary>
        public static string Load(int stage, int frame)
        {
            EnsureNames();
            return stage < 1 || stage > LoadStages ? null : s_Load[stage - 1, frame];
        }

        /// <summary>Мешочек в лапах (SACK_FRAMES); null — в кадре его нет.</summary>
        public static string Sack(int frame)
        {
            EnsureNames();
            return s_Sack[frame];
        }

        /// <summary>Заполнить массив спрайтов кадров по таблице имён (null-имя → null-спрайт). Вызывается при создании и смене вида.</summary>
        public static void FillBody(Sprite[] into, int fur)
        {
            for (int f = 0; f < FrameCount; f++) into[f] = PixelArt.World(Body(fur, f));
        }

        public static void FillEquip(Sprite[] into, Layer layer, int kind)
        {
            for (int f = 0; f < FrameCount; f++)
            {
                string n = Equip(layer, kind, f);
                into[f] = n != null ? PixelArt.World(n) : null;
            }
        }

        public static void FillLoad(Sprite[] into, int stage)
        {
            for (int f = 0; f < FrameCount; f++)
            {
                string n = Load(stage, f);
                into[f] = n != null ? PixelArt.World(n) : null;
            }
        }

        public static void FillSack(Sprite[] into)
        {
            for (int f = 0; f < FrameCount; f++)
            {
                string n = Sack(f);
                into[f] = n != null ? PixelArt.World(n) : null;
            }
        }
    }
}
