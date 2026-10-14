using CatMiners.Core;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using BigInteger = System.Numerics.BigInteger;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Тексты и иконки режима CatOutpostV2 для HUD и обучения: имена веток, слотов, редкостей и 16 предметов, задачи котика,
    /// имена целей (никогда не архивные ID), причины отказа команд (ТЗ 14), показ чисел. Все строки — из словаря
    /// Loc (раздел Outpost), числа — уже посчитанные ядром величины (UI ничего не пересчитывает по своим формулам).
    /// Таблицы ключей и имён спрайтов — статические массивы: показ не собирает строки из частей.
    /// </summary>
    public static class OutpostUiText
    {
        // Порядок — UpgradeBranch: Team, Pickaxes, Boots, Bags, Unload, Value.
        static readonly string[] s_BranchKeys =
        {
            "outpost.branch.team", "outpost.branch.pickaxes", "outpost.branch.boots",
            "outpost.branch.bags", "outpost.branch.unload", "outpost.branch.value",
        };

        static readonly string[] s_BranchIcons =
        {
            "icon_branch_team", "icon_branch_pickaxes", "icon_branch_boots",
            "icon_branch_bags", "icon_branch_unload", "icon_branch_value",
        };

        // Эффект ветки: «сейчас» (на пределе) и «сейчас → после».
        static readonly string[] s_EffectNowKeys =
        {
            "outpost.effect.team.now", "outpost.effect.pickaxes.now", "outpost.effect.boots.now",
            "outpost.effect.bags.now", "outpost.effect.unload.now", "outpost.effect.value.now",
        };

        static readonly string[] s_EffectChangeKeys =
        {
            "outpost.effect.team.change", "outpost.effect.pickaxes.change", "outpost.effect.boots.change",
            "outpost.effect.bags.change", "outpost.effect.unload.change", "outpost.effect.value.change",
        };

        // Порядок — EquipSlot: Pickaxe, Hat, Backpack, Boots.
        static readonly string[] s_SlotKeys = { "outpost.slot.pickaxe", "outpost.slot.hat", "outpost.slot.backpack", "outpost.slot.boots" };

        static readonly string[] s_FilterKeys =
        {
            "outpost.equip.filter.pickaxe", "outpost.equip.filter.hat", "outpost.equip.filter.backpack", "outpost.equip.filter.boots",
        };

        static readonly string[] s_SlotIcons = { "icon_slot_pickaxe", "icon_slot_hat", "icon_slot_backpack", "icon_slot_boots" };

        // Порядок — ItemRarity: Common, Rare, Epic, Legendary.
        static readonly string[] s_RarityKeys = { "outpost.rarity.common", "outpost.rarity.rare", "outpost.rarity.epic", "outpost.rarity.legendary" };

        // Предметы в порядке каталога (слот × редкость): ключ имени и иконки 16×16 / 32×32.
        static readonly string[] s_ItemKeys =
        {
            "outpost.item.pickaxe.common", "outpost.item.pickaxe.rare", "outpost.item.pickaxe.epic", "outpost.item.pickaxe.legendary",
            "outpost.item.hat.common", "outpost.item.hat.rare", "outpost.item.hat.epic", "outpost.item.hat.legendary",
            "outpost.item.backpack.common", "outpost.item.backpack.rare", "outpost.item.backpack.epic", "outpost.item.backpack.legendary",
            "outpost.item.boots.common", "outpost.item.boots.rare", "outpost.item.boots.epic", "outpost.item.boots.legendary",
        };

        static readonly string[] s_ItemIcons = BuildItemIcons(false);
        static readonly string[] s_ItemIconsBig = BuildItemIcons(true);

        // Порядок — WorkerMode.
        static readonly string[] s_TaskKeys =
        {
            "outpost.task.choosing", "outpost.task.moving_to_work", "outpost.task.mining", "outpost.task.moving_to_cargo",
            "outpost.task.picking_up", "outpost.task.returning", "outpost.task.waiting_depot", "outpost.task.unloading",
            "outpost.task.idle", "outpost.task.going_home",
        };

        static string[] BuildItemIcons(bool big)
        {
            string[] slots = { "pickaxe", "hat", "backpack", "boots" };
            string[] rarities = { "common", "rare", "epic", "legendary" };
            var icons = new string[16];
            for (int s = 0; s < 4; s++)
                for (int r = 0; r < 4; r++)
                    icons[s * 4 + r] = "icon_item_" + slots[s] + "_" + rarities[r] + (big ? "_big" : "");
            return icons;
        }

        static int ItemIndex(EquipmentItemDef def) => def == null ? 0 : ((int)def.Slot * 4 + (int)def.Rarity) & 15;

        // ───────────── Ветки базы ─────────────

        public static string BranchName(UpgradeBranch branch) => Loc.T(s_BranchKeys[(int)branch]);

        public static string BranchIcon(UpgradeBranch branch) => s_BranchIcons[(int)branch];

        /// <summary>Величина ветки в характеристиках команды (итоговая, с постоянными бонусами экипировки).</summary>
        public static string BranchValue(UpgradeBranch branch, TeamStats s)
        {
            if (s == null) return "-";
            switch (branch)
            {
                case UpgradeBranch.Team: return PixelUi.CountText(s.Workers);
                case UpgradeBranch.Pickaxes: return Num(s.FinalDamage);
                case UpgradeBranch.Boots: return Num(s.FinalMoveSpeed);
                case UpgradeBranch.Bags: return PixelUi.CountText(s.Capacity);
                case UpgradeBranch.Unload: return Num(s.UnloadRate);
                default: return Num(s.FinalValue);
            }
        }

        /// <summary>«Урон 5» — эффект на пределе ветки.</summary>
        public static string EffectNow(UpgradeBranch branch, TeamStats now) => Loc.F(s_EffectNowKeys[(int)branch], BranchValue(branch, now));

        /// <summary>«Урон 5 → 5.5»: значение «после» — из TeamStats ядра с повышенным уровнем (подсвечено акцентом).</summary>
        public static string EffectChange(UpgradeBranch branch, TeamStats now, TeamStats after) =>
            Loc.F(s_EffectChangeKeys[(int)branch], BranchValue(branch, now), PixelUi.AccentTag + BranchValue(branch, after) + "</color>");

        // ───────────── Экипировка ─────────────

        public static string SlotName(EquipSlot slot) => Loc.T(s_SlotKeys[(int)slot]);

        /// <summary>Подпись фильтра коллекции (множественное число: «Pickaxes», «Hats»…).</summary>
        public static string FilterName(EquipSlot slot) => Loc.T(s_FilterKeys[(int)slot]);

        public static string EmptySlotIcon(EquipSlot slot) => s_SlotIcons[(int)slot];

        public static string RarityName(ItemRarity rarity) => Loc.T(s_RarityKeys[(int)rarity]);

        public static string ItemName(EquipmentItemDef def) => Loc.T(s_ItemKeys[ItemIndex(def)]);

        /// <summary>Иконка предмета: 16×16 (строки, тосты) или 32×32 (слоты, карточки, окно предмета).</summary>
        public static string ItemIcon(EquipmentItemDef def, bool big) => big ? s_ItemIconsBig[ItemIndex(def)] : s_ItemIcons[ItemIndex(def)];

        /// <summary>Рамка карточки по редкости ТЗ (обычная, редкая, эпическая, легендарная; «необычная» не используется).</summary>
        public static PixelRarity Frame(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Rare: return PixelRarity.Rare;
                case ItemRarity.Epic: return PixelRarity.Epic;
                case ItemRarity.Legendary: return PixelRarity.Legendary;
                default: return PixelRarity.Common;
            }
        }

        /// <summary>Основной бонус предмета на уровне <paramref name="level"/> (из каталога ядра): «Damage +10%», «Bag +3 slots».</summary>
        public static string ItemBonus(EquipmentCatalog catalog, EquipmentItemDef def, int level)
        {
            if (catalog == null || def == null || level < 1) return Loc.T("outpost.bonus.none");
            switch (def.Slot)
            {
                case EquipSlot.Backpack: return Loc.Plural("outpost.bonus.backpack", catalog.BackpackExtra(def.Rarity, level));
                case EquipSlot.Hat: return Loc.F("outpost.bonus.hat", Pct(catalog.ItemBonus(def.Rarity, level)));
                case EquipSlot.Boots: return Loc.F("outpost.bonus.boots", Pct(catalog.ItemBonus(def.Rarity, level)));
                default: return Loc.F("outpost.bonus.pickaxe", Pct(catalog.ItemBonus(def.Rarity, level)));
            }
        }

        /// <summary>
        /// Только величина бонуса предмета на уровне <paramref name="level"/> — справа от стрелки сравнения уровней в окне
        /// предмета: «+8.8%», «+4 slots». Числа — те же, что у <see cref="ItemBonus"/>.
        /// </summary>
        public static string ItemBonusValue(EquipmentCatalog catalog, EquipmentItemDef def, int level)
        {
            if (catalog == null || def == null || level < 1) return Loc.T("outpost.bonus.none");
            return def.Slot == EquipSlot.Backpack
                ? Loc.Plural("outpost.bonus.value.backpack", catalog.BackpackExtra(def.Rarity, level))
                : Loc.F("outpost.bonus.value.percent", Pct(catalog.ItemBonus(def.Rarity, level)));
        }

        /// <summary>Название плитки магазина по её ID (outpost.store.item.&lt;ID&gt;).</summary>
        public static string StoreItemName(StoreItemDef def) => def == null ? "" : Loc.T("outpost.store.item." + def.Id);

        /// <summary>Длительность тайм-скипа: «10m», «1h», «1h 30m».</summary>
        public static string SkipDuration(int minutes) =>
            minutes < 60 ? Loc.F("outpost.store.minutes", minutes)
            : minutes % 60 == 0 ? Loc.F("outpost.store.hours", minutes / 60)
            : Loc.F("outpost.store.hours_minutes", minutes / 60, minutes % 60);

        /// <summary>Детали с формой слова: «12 parts».</summary>
        public static string Parts(long n) => Loc.Plural("outpost.parts.count", n);

        /// <summary>Полученные детали: «+5 parts».</summary>
        public static string PartsGain(long n) => Loc.Plural("outpost.parts.gain", n);

        // ───────────── Котик и цели ─────────────

        public static string Task(WorkerMode mode)
        {
            int i = (int)mode;
            return Loc.T(i >= 0 && i < s_TaskKeys.Length ? s_TaskKeys[i] : "outpost.task.idle");
        }

        /// <summary>Имя цели по виду токена: порода, богатое месторождение, завал или водопад, ворота сектора.</summary>
        public static string TargetName(MapTarget t)
        {
            if (t == null) return Loc.T("outpost.target.unknown");
            switch (t.Kind)
            {
                case 'p':
                    return Loc.T("outpost.target.gate");
                case 's':
                {
                    // Имя шахты-месторождения из каталога; без записи в словаре — общее имя, а не ID данных.
                    string name = t.TypeId != null ? PresentationCatalog.ShaftName(t.TypeId) : null;
                    return string.IsNullOrEmpty(name) || name == t.TypeId ? Loc.T("outpost.target.deposit_generic") : Loc.F("outpost.target.deposit", name);
                }
                case 'x':
                    return PresentationCatalog.ObstructionVisual(t.TypeId) == ObstructionVisualKind.Water
                        ? Loc.T("outpost.target.waterfall")
                        : Loc.T("outpost.target.wall");
                default:
                    return PresentationCatalog.RockName(t.VisualId ?? t.TypeId ?? string.Empty);
            }
        }

        /// <summary>Спрайт портрета цели в сведениях: иконка ×2 (ворота, месторождение) или спрайт поля ×1 (порода, завал).</summary>
        public static string TargetPortrait(MapTarget t, out int scale)
        {
            scale = 2;
            if (t == null) return "icon_info";
            switch (t.Kind)
            {
                case 'p': return "icon_gate";
                case 's': return "icon_shaft";
                case 'x':
                {
                    string sprite = PresentationCatalog.ObstructionSingleSprite(t.TypeId);
                    if (sprite != null && PixelArt.Has(sprite))
                    {
                        scale = 1;
                        return sprite;
                    }
                    return "icon_lock";
                }
                default:
                {
                    string sprite = PresentationCatalog.RockSprite(null, t.VisualId ?? t.TypeId, t.Id);
                    if (sprite != null && PixelArt.Has(sprite))
                    {
                        scale = 1;
                        return sprite;
                    }
                    return "icon_pickaxe";
                }
            }
        }

        // ───────────── Причины отказа (ТЗ 14) ─────────────

        /// <summary>Короткая причина по коду отказа ядра/контроллера ("gold", "cap", "phase", "max", "parts"…).</summary>
        public static string Reason(string code)
        {
            switch (code)
            {
                case "gold": return Loc.T("outpost.reason.gold");
                case "cap": return Loc.T("outpost.reason.cap");
                case "phase": return Loc.T("outpost.reason.phase");
                case "mode": return Loc.T("outpost.reason.mode");
                case "max": return Loc.T("outpost.reason.item_max");
                case "parts": return Loc.T("outpost.reason.parts");
                case "not-owned": return Loc.T("outpost.reason.not_owned");
                case "already": return Loc.T("outpost.reason.already");
                case "empty": return Loc.T("outpost.reason.empty");
                case "offline": return Loc.T("outpost.reason.offline");
                case "transition": return Loc.T("outpost.reason.transition");
                case "loading": return Loc.T("outpost.reason.loading");
                case "closed_sector": return Loc.T("outpost.reason.closed_sector");
                case "no_path": return Loc.T("outpost.reason.no_path");
                case "gems": return Loc.T("outpost.reason.gems");
                case "cooldown": return Loc.T("outpost.reason.cooldown");
                case "unavailable": return Loc.T("outpost.reason.unavailable");
                default: return Loc.T("outpost.reason.unknown");
            }
        }

        // ───────────── Числа ─────────────

        /// <summary>Деньги — точное BigInteger ядра, короткий показ с усечением вниз (MoneyFormat).</summary>
        public static string Money(BigInteger value) => MoneyFormat.Format(value);

        /// <summary>Дробная величина для показа (урон, скорость, множитель): три значащие цифры, усечение вниз.</summary>
        public static string Num(Rational value) => NumberFormat.Format(value.ToDouble());

        /// <summary>Доля как проценты без знака: 2/25 → «8».</summary>
        public static string Pct(Rational value) => NumberFormat.Format((value * 100).ToDouble());

        /// <summary>HP в долях ядра → целые единицы HP (вверх, чтобы живая цель не показывала 0).</summary>
        public static string Hp(long units)
        {
            long hp = units <= 0 ? 0 : (units + OutpostIds.HpUnitsPerHp - 1) / OutpostIds.HpUnitsPerHp;
            return hp < 100 ? PixelUi.CountText((int)hp) : NumberFormat.Format((double)hp);
        }
    }
}
