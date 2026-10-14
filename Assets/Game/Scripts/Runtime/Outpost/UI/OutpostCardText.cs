using System.Globalization;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Тексты и спрайты карточек для окна «Карты» и сообщений: имя по ID (не технический ID), редкость, арт card_art_&lt;id&gt;,
    /// рамка и фон по редкости, эффект на уровне и после улучшения, подпись состояния (CardViewState). Числа эффектов — из
    /// каталога ядра (CardDef.EffectAt), UI их не пересчитывает по своим формулам.
    /// </summary>
    public static class OutpostCardText
    {
        static readonly string[] s_RarityTone = { "common", "uncommon", "rare", "epic", "legendary", "majestic" };

        public static string Name(CardDef def) => def == null ? "" : Loc.T("cards.name." + def.Id);
        public static string RarityName(int rarity) => Loc.T("cards.rarity." + Clamp(rarity).ToString(CultureInfo.InvariantCulture));
        public static string Tone(int rarity) => s_RarityTone[Clamp(rarity) - 1];
        /// <summary>Рамка редкости UI (9-slice): ui_card_frame_&lt;редкость&gt;.</summary>
        public static string Frame(int rarity) => "ui_card_frame_" + Tone(rarity);
        /// <summary>Фон портрета 64×48: card_bg_&lt;редкость&gt;.</summary>
        public static string Background(int rarity) => "card_bg_" + Tone(rarity);
        /// <summary>Иллюстрация 48×44 карточки (есть у каждой из 61).</summary>
        public static string Art(CardDef def) => "card_art_" + def.Id;

        static int Clamp(int rarity) => rarity < 1 ? 1 : rarity > CardRules.RarityCount ? CardRules.RarityCount : rarity;

        /// <summary>Ресурс специалиста («Amethyst»), или null у глобальных карточек.</summary>
        public static string Resource(CardDef def)
        {
            if (def == null || def.TargetIds.Length == 0) return null;
            string key = "cards.res." + def.TargetIds[0];
            return Loc.Has(key) ? Loc.T(key) : def.TargetIds[0];
        }

        /// <summary>Эффект карточки на уровне level (≥ 1), например «Coin value from Amethyst deposits: +20%».</summary>
        public static string Effect(CardDef def, int level, CardRules rules = null)
        {
            if (def == null) return "";
            if (level < 1) level = 1;
            var e = def.Effect;
            var v = def.EffectAt(level);
            switch (e.Kind)
            {
                case CardEffectKind.TargetValue:
                case CardEffectKind.TargetDamage:
                    return Loc.F("cards.effect." + e.Kind, Resource(def) ?? "", Percent(v));
                case CardEffectKind.KindValue:
                case CardEffectKind.KindDamage:
                    return Loc.F("cards.effect." + e.Kind + "." + e.TargetKind, Percent(v));
                case CardEffectKind.TeamCap:
                case CardEffectKind.StartWorkers:
                case CardEffectKind.Capacity:
                case CardEffectKind.OfflineHours:
                case CardEffectKind.ChestCards:
                    return Loc.F("cards.effect." + e.Kind, v.Floor().ToString(CultureInfo.InvariantCulture));
                case CardEffectKind.CritPower:
                    var baseMult = rules != null ? rules.BaseCritMultiplier : Rational.Of(2, 1);
                    return Loc.F("cards.effect.CritPower", Number(baseMult + v));
                default:
                    return Loc.F("cards.effect." + e.Kind, Percent(v));
            }
        }

        /// <summary>Короткая строка эффекта для сетки («Unloading +15%», «Amethyst value +10%»); числа — те же CardDef.EffectAt.</summary>
        public static string ShortEffect(CardDef def, int level, CardRules rules = null)
        {
            if (def == null) return "";
            if (level < 1) level = 1;
            var e = def.Effect;
            var v = def.EffectAt(level);
            switch (e.Kind)
            {
                case CardEffectKind.TargetValue:
                case CardEffectKind.TargetDamage:
                    return Loc.F("cards.short." + e.Kind, Resource(def) ?? "", Percent(v));
                case CardEffectKind.KindValue:
                case CardEffectKind.KindDamage:
                    return Loc.F("cards.short." + e.Kind + "." + e.TargetKind, Percent(v));
                case CardEffectKind.TeamCap:
                case CardEffectKind.StartWorkers:
                case CardEffectKind.Capacity:
                case CardEffectKind.OfflineHours:
                case CardEffectKind.ChestCards:
                    return Loc.F("cards.short." + e.Kind, v.Floor().ToString(CultureInfo.InvariantCulture));
                case CardEffectKind.CritPower:
                    var baseMult = rules != null ? rules.BaseCritMultiplier : Rational.Of(2, 1);
                    return Loc.F("cards.short.CritPower", Number(baseMult + v));
                default:
                    return Loc.F("cards.short." + e.Kind, Percent(v));
            }
        }

        /// <summary>Только величина бонуса для сравнения уровней: +5%, +2, +1 h или ×2.25.</summary>
        public static string EffectValue(CardDef def, int level, CardRules rules = null)
        {
            if (def == null) return "";
            var value = def.EffectAt(System.Math.Max(1, level));
            switch (def.Effect.Kind)
            {
                case CardEffectKind.TeamCap:
                case CardEffectKind.StartWorkers:
                case CardEffectKind.Capacity:
                case CardEffectKind.ChestCards:
                    return "+" + value.Floor().ToString(CultureInfo.InvariantCulture);
                case CardEffectKind.OfflineHours:
                    return Loc.F("cards.value.hours", value.Floor().ToString(CultureInfo.InvariantCulture));
                case CardEffectKind.CritPower:
                    return "×" + Number((rules != null ? rules.BaseCritMultiplier : Rational.Of(2, 1)) + value);
                case CardEffectKind.CritChance:
                case CardEffectKind.HireDiscount:
                case CardEffectKind.BranchDiscount:
                    return Percent(value);
                default:
                    return "+" + Percent(value);
            }
        }

        /// <summary>Подпись под артом в сетке по состоянию (у активной карточки уровень показан отдельной строкой).</summary>
        public static string Status(CardViewState state, CardDef def, OutpostProfile profile)
        {
            switch (state)
            {
                case CardViewState.Locked: return Loc.F("cards.state.locked", def.MineUnlock);
                case CardViewState.Available: return Loc.T("cards.state.available");
                case CardViewState.Collecting:
                    var q = CardService.Quote(profile, def);
                    return Loc.F("cards.state.collecting", q.CopiesOwned, q.CopiesNeeded);
                case CardViewState.Upgradable: return Loc.T("cards.state.upgradable");
                case CardViewState.Max: return Loc.T("cards.state.max");
                default: return "";
            }
        }

        /// <summary>Происхождение правила для окна подробностей (честная пометка адаптации).</summary>
        public static string Rule(CardDef def) => def?.Effect?.Rule != null && Loc.Has("cards.rule." + def.Effect.Rule) ? Loc.T("cards.rule." + def.Effect.Rule) : "";

        /// <summary>Причина, по которой улучшение недоступно.</summary>
        public static string Reason(CardUpgradeQuote q)
        {
            switch (q.BlockReason)
            {
                case null: return "";
                case "copies": return Loc.F("cards.reason.copies", q.CopiesNeeded);
                case "elixir": return Loc.F("cards.reason.elixir", OutpostUiText.Money(q.ElixirNeeded));
                default: return Loc.T("cards.reason." + q.BlockReason);
            }
        }

        /// <summary>Доля как проценты: 1/5 → «20%», 3/40 → «7.5%» (до одного знака после точки).</summary>
        public static string Percent(Rational v)
        {
            var tenths = (v * 1000).RoundHalfUp();
            var whole = System.Numerics.BigInteger.DivRem(tenths, 10, out var rem);
            return rem.IsZero ? whole.ToString(CultureInfo.InvariantCulture) + "%" :
                whole.ToString(CultureInfo.InvariantCulture) + "." + System.Numerics.BigInteger.Abs(rem).ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>Число с точкой до двух знаков: 13/4 → «3.25».</summary>
        public static string Number(Rational v)
        {
            var hundredths = (v * 100).RoundHalfUp();
            var whole = System.Numerics.BigInteger.DivRem(hundredths, 100, out var rem);
            if (rem.IsZero) return whole.ToString(CultureInfo.InvariantCulture);
            string frac = System.Numerics.BigInteger.Abs(rem).ToString("00", CultureInfo.InvariantCulture).TrimEnd('0');
            return whole.ToString(CultureInfo.InvariantCulture) + "." + frac;
        }
    }
}
