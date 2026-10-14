using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Каталог постоянной экипировки (ТЗ 10, 12.3): 16 предметов "{slot}.{rarity}" в порядке слот × редкость.
    /// Порядок каталога — стабильный порядок сравнения при равенстве бонусов.
    /// </summary>
    [Serializable]
    public sealed class EquipmentCatalog
    {
        public string Version;
        /// <summary>Имена слотов по порядку <see cref="EquipSlot"/>: pickaxe, hat, backpack, boots.</summary>
        public string[] Slots;
        /// <summary>Имена редкостей по порядку <see cref="ItemRarity"/>: common, rare, epic, legendary.</summary>
        public string[] Rarities;
        /// <summary>ID предметов в порядке каталога (слот × редкость).</summary>
        public string[] Items;
        public Rational[] RarityBaseBonus;
        public Rational BonusPerLevel;
        public int[] RarityBaseCapacity;
        public Rational[] RarityCostMultiplier;
        public Rational PartsCostBase;
        public Rational PartsCostGrowth;
        public int MaxItemLevel;

        List<EquipmentItemDef> m_Defs;
        Dictionary<string, EquipmentItemDef> m_ById;

        /// <summary>Предметы в порядке каталога.</summary>
        [JsonIgnore]
        public IReadOnlyList<EquipmentItemDef> Defs
        {
            get
            {
                EnsureIndex();
                return m_Defs;
            }
        }

        public bool TryGet(string itemId, out EquipmentItemDef def)
        {
            EnsureIndex();
            def = null;
            return itemId != null && m_ById.TryGetValue(itemId, out def);
        }

        public EquipmentItemDef Get(string itemId)
        {
            if (!TryGet(itemId, out var def)) throw new OutpostConfigException("equipment_catalog: нет предмета " + (itemId ?? "null"));
            return def;
        }

        public EquipmentItemDef Get(EquipSlot slot, ItemRarity rarity)
        {
            EnsureIndex();
            return m_Defs[(int)slot * Rarities.Length + (int)rarity];
        }

        public static string ItemId(string slot, string rarity) => slot + "." + rarity;

        public string SlotName(EquipSlot slot) => Slots[(int)slot];
        public string RarityName(ItemRarity rarity) => Rarities[(int)rarity];

        /// <summary>ItemBonus = RarityBaseBonus[r] · (1 + BonusPerLevel · (q − 1)) — для кирки, шляпы, обуви.</summary>
        public Rational ItemBonus(ItemRarity rarity, int level) =>
            RarityBaseBonus[(int)rarity] * (Rational.One + BonusPerLevel * (level - 1));

        /// <summary>BackpackExtra = RarityBaseCapacity[r] + (q − 1).</summary>
        public int BackpackExtra(ItemRarity rarity, int level) => RarityBaseCapacity[(int)rarity] + (level - 1);

        /// <summary>
        /// Сравнимая величина основного бонуса предмета: рюкзак — прибавка порций, остальные — доля бонуса.
        /// </summary>
        public Rational EffectValue(EquipmentItemDef def, int level) =>
            def.Slot == EquipSlot.Backpack ? (Rational)BackpackExtra(def.Rarity, level) : ItemBonus(def.Rarity, level);

        /// <summary>PartsCost(q → q+1) = ceil(PartsCostBase · PartsCostGrowth^(q−1) · RarityCostMultiplier[r]).</summary>
        public long UpgradeCost(ItemRarity rarity, int level)
        {
            var cost = (PartsCostBase * PartsCostGrowth.Pow(level - 1) * RarityCostMultiplier[(int)rarity]).Ceil();
            return ExactMath.ToLongChecked(cost, "PartsCost");
        }

        public string CanonicalJson() => OutpostJson.Serialize(this);
        public string ComputeHash() => OutpostJson.Sha256Hex(CanonicalJson());

        public static EquipmentCatalog Parse(string json)
        {
            EquipmentCatalog cat;
            try
            {
                cat = OutpostJson.Deserialize<EquipmentCatalog>(json);
            }
            catch (Exception e)
            {
                throw new OutpostConfigException("equipment_catalog: ошибка разбора: " + e.Message, e);
            }
            if (cat == null) throw new OutpostConfigException("equipment_catalog: пустой документ");
            cat.Validate();
            return cat;
        }

        public void Validate()
        {
            void Fail(string m) => throw new OutpostConfigException("equipment_catalog: " + m);
            if (string.IsNullOrEmpty(Version)) Fail("нет Version");
            string[] slots = { "pickaxe", "hat", "backpack", "boots" };
            string[] rarities = { "common", "rare", "epic", "legendary" };
            if (Slots == null || Slots.Length != slots.Length) Fail("ожидалось 4 слота");
            for (int i = 0; i < slots.Length; i++)
                if (Slots[i] != slots[i]) Fail("слот " + i + ": ожидался " + slots[i]);
            if (Rarities == null || Rarities.Length != rarities.Length) Fail("ожидалось 4 редкости");
            for (int i = 0; i < rarities.Length; i++)
                if (Rarities[i] != rarities[i]) Fail("редкость " + i + ": ожидалась " + rarities[i]);
            if (Items == null || Items.Length != 16) Fail("ожидалось 16 предметов");
            for (int s = 0; s < 4; s++)
                for (int r = 0; r < 4; r++)
                    if (Items[s * 4 + r] != ItemId(slots[s], rarities[r]))
                        Fail("предмет " + (s * 4 + r) + ": ожидался " + ItemId(slots[s], rarities[r]));
            if (RarityBaseBonus == null || RarityBaseBonus.Length != 4) Fail("RarityBaseBonus: 4 значения");
            if (RarityBaseCapacity == null || RarityBaseCapacity.Length != 4) Fail("RarityBaseCapacity: 4 значения");
            if (RarityCostMultiplier == null || RarityCostMultiplier.Length != 4) Fail("RarityCostMultiplier: 4 значения");
            for (int i = 0; i < 4; i++)
            {
                if (RarityBaseBonus[i].Sign < 0) Fail("RarityBaseBonus < 0");
                if (RarityBaseCapacity[i] < 0) Fail("RarityBaseCapacity < 0");
                if (RarityCostMultiplier[i].Sign <= 0) Fail("RarityCostMultiplier ≤ 0");
            }
            if (BonusPerLevel.Sign < 0) Fail("BonusPerLevel < 0");
            if (PartsCostBase.Sign <= 0 || PartsCostGrowth.Sign <= 0) Fail("стоимость деталей ≤ 0");
            if (MaxItemLevel < 1) Fail("MaxItemLevel < 1");
            m_Defs = null;
            EnsureIndex();
        }

        void EnsureIndex()
        {
            if (m_Defs != null) return;
            var defs = new List<EquipmentItemDef>(16);
            var byId = new Dictionary<string, EquipmentItemDef>(StringComparer.Ordinal);
            for (int s = 0; s < Slots.Length; s++)
                for (int r = 0; r < Rarities.Length; r++)
                {
                    var def = new EquipmentItemDef
                    {
                        Id = ItemId(Slots[s], Rarities[r]),
                        Slot = (EquipSlot)s,
                        Rarity = (ItemRarity)r,
                        CatalogIndex = defs.Count,
                    };
                    defs.Add(def);
                    byId[def.Id] = def;
                }
            m_ById = byId;
            m_Defs = defs;
        }
    }

    /// <summary>Запись каталога: слот, редкость и стабильный индекс.</summary>
    public sealed class EquipmentItemDef
    {
        public string Id;
        public EquipSlot Slot;
        public ItemRarity Rarity;
        public int CatalogIndex;
    }
}
