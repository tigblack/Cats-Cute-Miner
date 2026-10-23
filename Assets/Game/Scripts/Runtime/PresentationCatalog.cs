using System;
using System.Collections.Generic;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>Модель камня на поле: выводится из цикла наград камня (ТЗ 4.1), а не из подстрок ID.</summary>
    public enum RockVisualKind { Plain, Chest, Elixir, Gems, Pass }

    /// <summary>Материал цели для звука удара кирки (Sfx.Hit) и цвета сколов.</summary>
    public enum HitMaterial { Stone, Crystal, Wood, Metal }

    /// <summary>
    /// Оформление окружения шахты (ART_DIRECTION.md, «Окружение»): тёплая деревянная штольня, прохладная кристальная пещера,
    /// глубокая медная выработка. Только вид: карты, награды и баланс не меняются.
    /// </summary>
    public enum EnvironmentTheme { Warm, Crystal, Copper }

    /// <summary>Модель препятствия на поле: каменный блок или водопад.</summary>
    public enum ObstructionVisualKind { Wall, Water }

    /// <summary>
    /// Каталог отображения (ТЗ 4.1): внутренние ID данных → имена, модели и материалы. В JSON ничего не переименовывается;
    /// литералы ID из данных допустимы только здесь, в адаптере данных и в тестах.
    /// Тексты — в словаре <see cref="Loc"/> (раздел Catalog): имена шахт и сундуков по ключам из ID
    /// («catalog.shaft.&lt;ID&gt;», «catalog.gacha.&lt;ID&gt;»).
    /// </summary>
    public static class PresentationCatalog
    {
        public static string CatName => Loc.T("catalog.cat.name");

        const string ShaftKeyPrefix = "catalog.shaft.";
        const string GachaKeyPrefix = "catalog.gacha.";

        // Значения DetailedType валютных наград из схемы данных (не ID записей).
        const string CurrencyCore = "Core";
        const string CurrencySoft = "Soft";
        const string CurrencyHard = "Hard";

        /// <summary>
        /// Цвет самоцвета шахты: тонирует светлый (cream) спрайт кристаллов shaft_gems и вспышки шахты.
        /// Тона из палитры пиксель-арта, чтобы кристаллы не выбивались из стиля.
        /// </summary>
        static readonly Dictionary<string, Color> s_ShaftGems = new Dictionary<string, Color>
        {
            { "amethyst", PixelPalette.Purple5 }, { "citrine", PixelPalette.Gold4 },
            { "agate", PixelPalette.Red5 }, { "topaz", PixelPalette.Blue5 },
            { "opal", PixelPalette.Blue7 }, { "jade", PixelPalette.Green5 },
            { "onyx", PixelPalette.Navy6 }, { "sapphire", PixelPalette.Blue4 },
            { "tourmaline", PixelPalette.Pink2 }, { "aquamarine", PixelPalette.Teal5 },
            { "emerald", PixelPalette.Teal4 }, { "diamond", PixelPalette.Blue7 },
            { "garnet", PixelPalette.Red3 }, { "heliodor", PixelPalette.Gold4 },
            { "ruby", PixelPalette.Red4 }, { "alexandrite", PixelPalette.Teal3 },
            { "celestine", PixelPalette.Blue6 }, { "amber", PixelPalette.Amber3 },
            { "moonstone", PixelPalette.Navy8 }, { "sunstone", PixelPalette.Amber2 },
        };

        static readonly Color s_DefaultGem = PixelPalette.Navy8;

        /// <summary>Материал сундука в породе: суффикс спрайтов rock_chest_* и ui-иконок по ID сундука (gacha).</summary>
        static readonly Dictionary<string, string> s_ChestTiers = new Dictionary<string, string>
        {
            { "GachaWooden", "wooden" }, { "GachaIron", "iron" }, { "GachaForged", "forged" }, { "GachaSilver", "silver" },
            { "GachaGolden", "golden" }, { "GachaRoyal", "royal" }, { "GachaMythical", "mythical" }, { "GachaLegendary", "legendary" },
        };

        /// <summary>Сундук неизвестного уровня (данные без записи каталога) — деревянный.</summary>
        public const string DefaultChestTier = "wooden";

        /// <summary>Крупные жилы эликсира и самоцветов (rock_*_big); остальные валютные камни — малые.</summary>
        static readonly HashSet<string> s_BigRocks = new HashSet<string> { "rocksoftcurrencybig", "rockhardcurrencybig" };

        static readonly string[] s_PlainRocks = { "rock_plain_0", "rock_plain_1", "rock_plain_2" };

        /// <summary>
        /// Спрайты препятствий 1×1 по ID: варианты блока v1…v6 (v6L/v6R прижаты к стене) и каменные столбы у ворот.
        /// Остальные препятствия собираются из автотайлов block_* по размеру footprint.
        /// </summary>
        static readonly Dictionary<string, string> s_SingleBlocks = new Dictionary<string, string>
        {
            { "block1x1v1", "block_single_1" }, { "block1x1v2", "block_single_2" }, { "block1x1v3", "block_single_3" },
            { "block1x1v4", "block_single_4" }, { "block1x1v5", "block_single_5" }, { "block1x1v6", "block_single_6" },
            { "block1x1v6L", "block_single_6l" }, { "block1x1v6R", "block_single_6r" },
            { "wall1x1vL", "wall_gate_l" }, { "wall1x1vR", "wall_gate_r" },
        };

        /// <summary>
        /// Водопады: схема данных не различает воду и камень, поэтому модель задаётся записью каталога по ID.
        /// Все прочие препятствия — каменные блоки.
        /// </summary>
        static readonly Dictionary<string, ObstructionVisualKind> s_Obstructions = new Dictionary<string, ObstructionVisualKind>
        {
            { "waterfall1x4vL", ObstructionVisualKind.Water }, { "waterfall1x4vR", ObstructionVisualKind.Water },
        };

        /// <summary>Имя шахты по ключу «catalog.shaft.&lt;ID&gt;»; для ID без записи — сам ID.</summary>
        public static string ShaftName(string shaftId) => Loc.TryGet(ShaftKeyPrefix + shaftId, out var n) ? n : shaftId;

        /// <summary>Имя сундука по ключу «catalog.gacha.&lt;ID&gt;»; false — записи нет.</summary>
        static bool TryGachaName(string gachaId, out string name) => Loc.TryGet(GachaKeyPrefix + gachaId, out name);

        /// <summary>Цвет самоцвета шахты; для ID без записи — нейтральный светлый кристалл.</summary>
        public static Color ShaftGemColor(string shaftId) =>
            shaftId != null && s_ShaftGems.TryGetValue(shaftId, out var c) ? c : s_DefaultGem;

        /// <summary>
        /// Модель камня по его циклу наград: все награды — сундуки → сундук в породе; все — эликсир → эликсирная жила;
        /// все — твёрдая валюта → самоцветы; все — валюта вне Core/Soft/Hard → серый валун; иначе обычная порода.
        /// </summary>
        public static RockVisualKind RockVisual(BalanceRepository b, string rockId)
        {
            if (b == null || rockId == null || !b.Rocks.TryGetValue(rockId, out var rock)) return RockVisualKind.Plain;
            var cycle = rock.RewardCycle;
            if (cycle == null || cycle.Length == 0) return RockVisualKind.Plain;
            bool allGacha = true, allSoft = true, allHard = true, allOther = true;
            foreach (var r in cycle)
            {
                bool currency = r != null && r.RewardType == RewardTypes.Currency;
                string type = r?.DetailedType;
                allGacha &= r != null && r.RewardType == RewardTypes.Gacha;
                allSoft &= currency && type == CurrencySoft;
                allHard &= currency && type == CurrencyHard;
                allOther &= currency && !string.IsNullOrEmpty(type) && type != CurrencyCore && type != CurrencySoft && type != CurrencyHard;
            }
            if (allGacha) return RockVisualKind.Chest;
            if (allSoft) return RockVisualKind.Elixir;
            if (allHard) return RockVisualKind.Gems;
            if (allOther) return RockVisualKind.Pass;
            return RockVisualKind.Plain;
        }

        /// <summary>Модель препятствия: водопад или каменный блок.</summary>
        public static ObstructionVisualKind ObstructionVisual(string typeId) =>
            typeId != null && s_Obstructions.TryGetValue(typeId, out var kind) ? kind : ObstructionVisualKind.Wall;

        /// <summary>Спрайт одиночного препятствия 1×1 (block_single_*, wall_gate_*); null — ID без записи (берётся вариант по умолчанию).</summary>
        public static string ObstructionSingleSprite(string typeId) =>
            typeId != null && s_SingleBlocks.TryGetValue(typeId, out var sprite) ? sprite : null;

        /// <summary>
        /// Уровень сундука в породе (суффикс rock_chest_*): wooden, iron, forged, silver, golden, royal, mythical, legendary.
        /// Берётся из ID сундука в цикле наград камня; ID обучения (scg_…) — через MimicId сундука; камень-двойник — через
        /// MimicId камня. Иначе — <see cref="DefaultChestTier"/>.
        /// </summary>
        public static string ChestTier(BalanceRepository b, string rockId)
        {
            if (b == null || rockId == null || !b.Rocks.TryGetValue(rockId, out var rock)) return DefaultChestTier;
            string tier = ChestTierOfCycle(b, rock);
            if (tier == null && !string.IsNullOrEmpty(rock.MimicId) && b.Rocks.TryGetValue(rock.MimicId, out var mimic))
                tier = ChestTierOfCycle(b, mimic);
            return tier ?? DefaultChestTier;
        }

        /// <summary>Уровень сундука по его ID (иконки icon_chest_* окна награды): прямо по каталогу или через MimicId сундука.</summary>
        public static string GachaTier(BalanceRepository b, string gachaId)
        {
            if (gachaId == null) return DefaultChestTier;
            if (s_ChestTiers.TryGetValue(gachaId, out var tier)) return tier;
            if (b != null && b.Gacha.TryGetValue(gachaId, out var g) && !string.IsNullOrEmpty(g.MimicId) && s_ChestTiers.TryGetValue(g.MimicId, out tier))
                return tier;
            return DefaultChestTier;
        }

        /// <summary>
        /// Уровень сундука без BalanceRepository (HUD нового режима: MimicId — из каталога карточек ядра): по своему ID,
        /// иначе по сундуку-образцу, иначе <see cref="DefaultChestTier"/>.
        /// </summary>
        public static string GachaTierOrMimic(string gachaId, string mimicId)
        {
            if (gachaId != null && s_ChestTiers.TryGetValue(gachaId, out var tier)) return tier;
            if (!string.IsNullOrEmpty(mimicId) && s_ChestTiers.TryGetValue(mimicId, out tier)) return tier;
            return DefaultChestTier;
        }

        static string ChestTierOfCycle(BalanceRepository b, RockDto rock)
        {
            if (rock.RewardCycle == null) return null;
            foreach (var r in rock.RewardCycle)
            {
                if (r == null || r.RewardType != RewardTypes.Gacha || r.DetailedType == null) continue;
                if (s_ChestTiers.TryGetValue(r.DetailedType, out var tier)) return tier;
                if (b.Gacha.TryGetValue(r.DetailedType, out var g) && !string.IsNullOrEmpty(g.MimicId) && s_ChestTiers.TryGetValue(g.MimicId, out tier))
                    return tier;
            }
            return null;
        }

        /// <summary>Крупная жила (rock_elixir_big / rock_gems_big): по ID камня или его двойника (MimicId).</summary>
        public static bool RockIsBig(BalanceRepository b, string rockId)
        {
            if (rockId == null) return false;
            if (s_BigRocks.Contains(rockId)) return true;
            return b != null && b.Rocks.TryGetValue(rockId, out var rock) && !string.IsNullOrEmpty(rock.MimicId) && s_BigRocks.Contains(rock.MimicId);
        }

        /// <summary>
        /// Спрайт камня на поле (Docs/Art/PIXEL_STYLE_GUIDE.md, «Размеры объектов»): модель из цикла наград
        /// (<see cref="RockVisual"/>), размер жилы, уровень сундука; обычная порода — один из трёх валунов по <paramref name="variant"/>.
        /// </summary>
        public static string RockSprite(BalanceRepository b, string rockId, int variant)
        {
            switch (RockVisual(b, rockId))
            {
                case RockVisualKind.Chest: return "rock_chest_" + ChestTier(b, rockId);
                case RockVisualKind.Elixir: return RockIsBig(b, rockId) ? "rock_elixir_big" : "rock_elixir_small";
                case RockVisualKind.Gems: return RockIsBig(b, rockId) ? "rock_gems_big" : "rock_gems_small";
                case RockVisualKind.Pass: return "rock_pass";
                default: return s_PlainRocks[(variant % s_PlainRocks.Length + s_PlainRocks.Length) % s_PlainRocks.Length];
            }
        }

        /// <summary>ID шахт из данных, для которых в каталоге нет цвета самоцвета или имени (подставляется оформление по умолчанию).</summary>
        public static IReadOnlyList<string> MissingVisualEntries(BalanceRepository b)
        {
            var missing = new List<string>();
            if (b == null) return missing;
            foreach (var id in b.Shafts.Keys)
                if (!s_ShaftGems.ContainsKey(id) || !Loc.Has(ShaftKeyPrefix + id)) missing.Add(id);
            missing.Sort(System.StringComparer.Ordinal);
            return missing;
        }

        public static string GachaName(BalanceRepository b, string gachaId)
        {
            if (TryGachaName(gachaId, out var n)) return n;
            if (b != null && b.Gacha.TryGetValue(gachaId, out var g) && !string.IsNullOrEmpty(g.MimicId) && TryGachaName(g.MimicId, out var mimic))
                return mimic;
            return Loc.T("catalog.chest.generic");
        }

        /// <summary>Имя сундука без BalanceRepository: свой ключ каталога, иначе ключ сундука-образца, иначе «Chest».</summary>
        public static string GachaNameOrMimic(string gachaId, string mimicId)
        {
            if (gachaId != null && TryGachaName(gachaId, out var n)) return n;
            if (!string.IsNullOrEmpty(mimicId) && TryGachaName(mimicId, out n)) return n;
            return Loc.T("catalog.chest.generic");
        }

        /// <summary>Имя камня по модели из данных (тот же вывод, что и <see cref="RockVisual"/>).</summary>
        public static string RockName(BalanceRepository b, string rockId)
        {
            switch (RockVisual(b, rockId))
            {
                case RockVisualKind.Elixir: return Loc.T("catalog.rock.elixir");
                case RockVisualKind.Gems: return Loc.T("catalog.rock.gems");
                case RockVisualKind.Pass: return Loc.T("catalog.rock.pass");
                case RockVisualKind.Chest: return Loc.T("catalog.rock.chest");
                default: return Loc.T("catalog.rock.plain");
            }
        }

        public static string RockName(string rockId)
        {
            if (rockId.Contains("softcurrency")) return Loc.T("catalog.rock.elixir");
            if (rockId.Contains("hardcurrency")) return Loc.T("catalog.rock.gems");
            if (rockId.Contains("battlepass")) return Loc.T("catalog.rock.pass");
            if (rockId.Contains("gacha")) return Loc.T("catalog.rock.chest");
            return Loc.T("catalog.rock.plain");
        }

        public static string ObjectName(ZoneObject o)
        {
            switch (o.Kind)
            {
                case ObjectKind.Rock: return RockName(o.TypeId);
                case ObjectKind.Shaft: return Loc.F("catalog.object.shaft", ShaftName(o.TypeId));
                case ObjectKind.Gate: return Loc.T("catalog.object.gate");
                case ObjectKind.Exit: return Loc.T("catalog.object.exit");
                case ObjectKind.Cart: return Loc.T("catalog.object.cart");
                default: return Loc.T("catalog.object.obstruction");
            }
        }

        /// <summary>Полное имя карты (окно карты, причины в контекстной панели): управляющий шахты или кузницы, предмет.</summary>
        /// <summary>Материал цели для звука удара: самоцветы и эликсир — кристалл, сундук в породе и жетон — металл, ворота и выход — дерево.</summary>
        public static HitMaterial HitMaterialOf(BalanceRepository b, ZoneObject o)
        {
            switch (o.Kind)
            {
                case ObjectKind.Gate:
                case ObjectKind.Exit: return HitMaterial.Wood;
                case ObjectKind.Rock:
                    switch (RockVisual(b, o.TypeId))
                    {
                        case RockVisualKind.Gems:
                        case RockVisualKind.Elixir: return HitMaterial.Crystal;
                        case RockVisualKind.Chest: return ChestTier(b, o.TypeId) == "wooden" ? HitMaterial.Wood : HitMaterial.Metal;
                        case RockVisualKind.Pass: return HitMaterial.Metal;
                        default: return HitMaterial.Stone;
                    }
                default: return HitMaterial.Stone;
            }
        }

        /// <summary>
        /// Оформление шахты по номеру зоны — по диапазонам зон из визуальной конфигурации (<see cref="EnvironmentThemes"/>):
        /// тёплая штольня (zone1–3, как на концепте), кристальная пещера, медная выработка. Только вид.
        /// </summary>
        public static EnvironmentTheme ThemeOf(int zone) => EnvironmentThemes.Of(zone);

        /// <summary>Трек музыки оформления: тёплая и медная — «Тёплая штольня», кристальная — «Кристальная глубина».</summary>
        public static int MusicOf(EnvironmentTheme theme) => theme == EnvironmentTheme.Crystal ? MusicPlayer.Crystal : MusicPlayer.Warm;

        public static string CardName(BalanceRepository b, string cardId)
        {
            if (!b.Cards.TryGetValue(cardId, out var c)) return cardId;
            if (c.IsManager)
            {
                string target = CardTarget(c);
                if (target == BalanceRepository.CartId) return Loc.T("catalog.card.manager.cart");
                return Loc.F(c.Rarity == Rarity.Uncommon ? "catalog.card.manager.shaft_senior" : "catalog.card.manager.shaft", ShaftName(target));
            }
            string item = CardItem(c);
            return item != null ? Loc.T("catalog.card.item." + item) : Loc.F("catalog.card.item.unknown", cardId);
        }

        /// <summary>Имя на ячейке карты, как на референсе: у управляющих — шахта («Аметист») или «Кузница», у предметов — полное.</summary>
        public static string CardShortName(BalanceRepository b, string cardId)
        {
            if (!b.Cards.TryGetValue(cardId, out var c) || !c.IsManager) return CardName(b, cardId);
            string target = CardTarget(c);
            return target == BalanceRepository.CartId ? Loc.T("catalog.card.short.cart") : ShaftName(target);
        }

        /// <summary>Первая цель карты из TargetIds (шахта или кузница) или пустая строка.</summary>
        public static string CardTarget(CardDto c) => c.TargetIds != null && c.TargetIds.Length > 0 ? c.TargetIds[0] : "";

        /// <summary>
        /// Спрайт портрета карты (экран «Карты», окно карты, каталог): котик-управляющий шахты — card_cat (каска или шляпа
        /// и самоцвет — наложениями), управляющий кузницы — card_forge, предмет — card_item_&lt;код&gt;; нет арта — icon_cards_big.
        /// </summary>
        public static string CardArt(CardDto c)
        {
            bool cat = c.IsManager && CardTarget(c) != BalanceRepository.CartId;
            string art = cat ? "card_cat" : (c.IsManager ? "card_forge" : CardItem(c));
            if (art == null) return "icon_cards_big";
            if (!art.StartsWith("card_", StringComparison.Ordinal) && !art.StartsWith("icon_", StringComparison.Ordinal)) art = "card_item_" + art;
            return PixelArt.Has(art) ? art : "icon_cards_big";
        }

        /// <summary>
        /// Предмет карты по коду эффекта (портрет card_item_&lt;ключ&gt;, имя catalog.card.item.&lt;ключ&gt;); у управляющих — null.
        /// По одной карте на код (ТЗ 10.1): применяемые 10, 7, 1 и ожидаемые 4, 5, 6, 8, 11, 13, 22, 23 — по смыслу эффекта,
        /// коды без известного смысла — кошачьи вещи.
        /// </summary>
        public static string CardItem(CardDto c)
        {
            if (c.IsManager) return null;
            switch (c.StatModifierType)
            {
                case 10: return "golden_cart";
                case 7: return "pocket_watch";
                case 1: return "golden_helmet";
                case 4: return "recruit_poster";
                case 5: return "lucky_paw";
                case 6: return "sledgehammer";
                case 8: return "gate_bell";
                case 11: return "gold_vein";
                case 13: return "fish_parcel";
                case 22: return "big_dynamite";
                case 23: return "clover";
                case 3: return "fish";
                case 12: return "yarn";
                case 15: return "milk_bowl";
                case 16: return "catnip";
                case 19: return "toy_mouse";
                case 20: return "cat_bed";
                case 21: return "scratcher";
                case 24: return "collar";
                case 25: return "lantern";
                case 26: return "treasure_map";
                default: return null;
            }
        }

        /// <summary>Лента эффекта на портрете: доход (коды 9, 10), время (7), лимит (1), «скоро» — эффект появится позже.</summary>
        public static string CardTag(CardDto c)
        {
            switch (c.StatModifierType)
            {
                case 9:
                case 10: return Loc.T("catalog.card.tag.income");
                case 7: return Loc.T("catalog.card.tag.time");
                case 1: return Loc.T("catalog.card.tag.cap");
                default: return Loc.T("catalog.card.tag.soon");
            }
        }

        /// <summary>Фон портрета по редкости: Common — сине-фиолетовый, Uncommon — бирюзовый, дальше синий, фиолетовый, янтарный.</summary>
        public static string CardBackground(int rarity)
        {
            switch (rarity)
            {
                case Rarity.Common: return "card_bg_common";
                case Rarity.Uncommon: return "card_bg_uncommon";
                case Rarity.Rare: return "card_bg_rare";
                case Rarity.Epic: return "card_bg_epic";
                default: return "card_bg_legendary";
            }
        }

        /// <summary>Эффект карты на её текущем уровне (окно карты); карты нет — «Нет в коллекции».</summary>
        public static string CardEffect(GameSession s, CardDto c)
        {
            if (!GameSession.IsAppliedCardCode(c.StatModifierType)) return CardFutureEffect(c);
            var v = s.CardEffectValue(c);
            return v.HasValue ? CardEffectText(c, v.Value) : Loc.T("catalog.card.effect.not_owned");
        }

        /// <summary>Эффект применяемой карты при значении <paramref name="value"/> (текущий или следующий уровень).</summary>
        public static string CardEffectText(CardDto c, Core.GameNumber value)
        {
            string v = Core.NumberFormat.Format(value);
            switch (c.StatModifierType)
            {
                case 9:
                {
                    string target = CardTarget(c);
                    if (target == BalanceRepository.CartId) return Loc.F("catalog.card.effect.cart_income", v);
                    return Loc.F("catalog.card.effect.shaft_automation", ShaftName(target), v);
                }
                case 10: return Loc.F("catalog.card.effect.all_income", v);
                case 7: return Loc.F("catalog.card.effect.free_cat_sooner", v);
                case 1: return Loc.F("catalog.card.effect.cat_cap", v);
                default: return CardFutureEffect(c);
            }
        }

        /// <summary>Неприменяемый код: ожидаемый смысл (ТЗ 10.1, кандидаты rulesVersion 2) с пометкой «появится позже».</summary>
        static string CardFutureEffect(CardDto c)
        {
            string key;
            switch (c.StatModifierType)
            {
                case 4: key = "hire_level"; break;
                case 5: key = "crit_chance"; break;
                case 6: key = "crit_power"; break;
                case 8: key = "gate_timer"; break;
                case 11: key = "rock_gold"; break;
                case 13: key = "deliveries"; break;
                case 22: key = "dynamite"; break;
                case 23: key = "legendary"; break;
                default: return Loc.T("catalog.card.effect.pending");
            }
            return Loc.F("catalog.card.effect.future", Loc.T("catalog.card.effect.future." + key));
        }
    }
}
