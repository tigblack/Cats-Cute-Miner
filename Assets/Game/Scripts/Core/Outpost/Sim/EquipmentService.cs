using System;

namespace CatMiners.Core.Outpost
{
    /// <summary>Результат выдачи предмета: дубликат превращается в детали.</summary>
    public struct GrantResult
    {
        public bool Duplicate;
        public int Parts;
    }

    /// <summary>
    /// Постоянная экипировка (ТЗ 10): одна запись на предмет, общий комплект команды, «Надеть лучшее» по величине бонуса
    /// с учётом уровня, улучшение за детали. Методы возвращают причину отказа (null — успех) и не меняют состояние при отказе.
    /// </summary>
    public static class EquipmentService
    {
        /// <summary>Новый предмет — уровень 1; дубликат — детали DuplicateParts[rarity].</summary>
        public static GrantResult Grant(OutpostProfile profile, string itemId, OutpostConfigSet configs)
        {
            var def = configs.Equipment.Get(itemId);
            if (profile.Items.ContainsKey(itemId))
            {
                int parts = configs.Rewards.DuplicateParts[(int)def.Rarity];
                profile.Parts += parts;
                return new GrantResult { Duplicate = true, Parts = parts };
            }
            profile.Items[itemId] = 1;
            return new GrantResult { Duplicate = false, Parts = 0 };
        }

        /// <summary>Бонус надетого предмета слота (кирка/шляпа/обувь); пустой слот — 0.</summary>
        public static Rational SlotBonus(OutpostProfile profile, EquipmentCatalog catalog, EquipSlot slot)
        {
            string id = profile.Equipped != null && profile.Equipped.Length > (int)slot ? profile.Equipped[(int)slot] : null;
            if (id == null || !catalog.TryGet(id, out var def) || def.Slot != slot) return Rational.Zero;
            int q = profile.ItemLevel(id);
            if (q < 1) return Rational.Zero;
            return slot == EquipSlot.Backpack ? Rational.Zero : catalog.ItemBonus(def.Rarity, q);
        }

        /// <summary>Прибавка вместимости надетого рюкзака; пустой слот — 0.</summary>
        public static int BackpackExtra(OutpostProfile profile, EquipmentCatalog catalog)
        {
            string id = profile.Equipped != null && profile.Equipped.Length > (int)EquipSlot.Backpack ? profile.Equipped[(int)EquipSlot.Backpack] : null;
            if (id == null || !catalog.TryGet(id, out var def) || def.Slot != EquipSlot.Backpack) return 0;
            int q = profile.ItemLevel(id);
            return q < 1 ? 0 : catalog.BackpackExtra(def.Rarity, q);
        }

        /// <summary>Сравнимый эффект предмета коллекции (0, если предмета нет).</summary>
        public static Rational Effect(OutpostProfile profile, EquipmentCatalog catalog, string itemId)
        {
            if (!catalog.TryGet(itemId, out var def)) return Rational.Zero;
            int q = profile.ItemLevel(itemId);
            return q < 1 ? Rational.Zero : catalog.EffectValue(def, q);
        }

        public static string Equip(OutpostProfile profile, EquipmentCatalog catalog, string itemId)
        {
            if (!catalog.TryGet(itemId, out var def)) return "unknown";
            if (profile.ItemLevel(itemId) < 1) return "not-owned";
            EnsureSlots(profile);
            if (profile.Equipped[(int)def.Slot] == itemId) return "already";
            profile.Equipped[(int)def.Slot] = itemId;
            return null;
        }

        public static string Unequip(OutpostProfile profile, EquipSlot slot)
        {
            EnsureSlots(profile);
            if (profile.Equipped[(int)slot] == null) return "empty";
            profile.Equipped[(int)slot] = null;
            return null;
        }

        /// <summary>
        /// По каждому слоту — максимальный итоговый бонус; при равенстве остаётся надетый, иначе первый по порядку каталога.
        /// Возвращает true, если комплект изменился.
        /// </summary>
        public static bool EquipBest(OutpostProfile profile, EquipmentCatalog catalog)
        {
            EnsureSlots(profile);
            bool changed = false;
            for (int s = 0; s < OutpostIds.SlotCount; s++)
            {
                var slot = (EquipSlot)s;
                string current = profile.Equipped[s];
                string best = null;
                var bestValue = Rational.Zero;
                if (current != null && catalog.TryGet(current, out var cd) && cd.Slot == slot && profile.ItemLevel(current) >= 1)
                {
                    best = current;
                    bestValue = catalog.EffectValue(cd, profile.ItemLevel(current));
                }
                foreach (var def in catalog.Defs)
                {
                    if (def.Slot != slot) continue;
                    int q = profile.ItemLevel(def.Id);
                    if (q < 1) continue;
                    var v = catalog.EffectValue(def, q);
                    if (best == null || v > bestValue)
                    {
                        best = def.Id;
                        bestValue = v;
                    }
                }
                if (best != current)
                {
                    profile.Equipped[s] = best;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>Стоимость улучшения q → q+1 в деталях или null, если уровень максимальный.</summary>
        public static long? UpgradeCost(OutpostProfile profile, EquipmentCatalog catalog, string itemId)
        {
            if (!catalog.TryGet(itemId, out var def)) return null;
            int q = profile.ItemLevel(itemId);
            if (q < 1 || q >= catalog.MaxItemLevel) return null;
            return catalog.UpgradeCost(def.Rarity, q);
        }

        /// <summary>Улучшить предмет за детали: "unknown", "not-owned", "max", "parts" или null.</summary>
        public static string UpgradeItem(OutpostProfile profile, EquipmentCatalog catalog, string itemId, out long cost)
        {
            cost = 0;
            if (!catalog.TryGet(itemId, out var def)) return "unknown";
            int q = profile.ItemLevel(itemId);
            if (q < 1) return "not-owned";
            if (q >= catalog.MaxItemLevel) return "max";
            cost = catalog.UpgradeCost(def.Rarity, q);
            if (profile.Parts < cost) return "parts";
            profile.Parts -= cost;
            profile.Items[itemId] = q + 1;
            return null;
        }

        static void EnsureSlots(OutpostProfile profile)
        {
            if (profile.Equipped == null || profile.Equipped.Length != OutpostIds.SlotCount)
            {
                var arr = new string[OutpostIds.SlotCount];
                if (profile.Equipped != null) Array.Copy(profile.Equipped, arr, Math.Min(arr.Length, profile.Equipped.Length));
                profile.Equipped = arr;
            }
        }
    }
}
