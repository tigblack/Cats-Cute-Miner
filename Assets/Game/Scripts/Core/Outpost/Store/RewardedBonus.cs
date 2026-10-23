using System;
using System.Numerics;

namespace CatMiners.Core.Outpost
{
    /// <summary>Вид награды бонуса за рекламу (ТЗ 10.6); имена — ключи <see cref="RewardedBonusRules.Weights"/>.</summary>
    public enum RewardedBonusKind
    {
        Gold,
        Elixir,
        Gems,
        Dynamite,
        Damage,
    }

    /// <summary>Предложение иконки: вид и сумма, зафиксированные при её появлении. Valid = false — бонус сейчас не предлагается.</summary>
    public struct RewardedBonusOffer
    {
        public bool Valid;
        public RewardedBonusKind Kind;
        /// <summary>Золото, эликсир или гемы; у динамита и усиления урона — 1.</summary>
        public BigInteger Amount;
    }

    /// <summary>
    /// Бонус за рекламу у левого края экрана (ТЗ 10.6): ID видов для команды, оферты и аналитики и взвешенный выбор вида среди
    /// доступных. Суммы и доступность считает симуляция (<see cref="OutpostSimulation.TryQuoteBonus"/>).
    /// </summary>
    public static class RewardedBonus
    {
        public const int DamageDurationSeconds = 300;
        public const int DamageMultiplier = 2;

        static readonly RewardedBonusKind[] s_Kinds = { RewardedBonusKind.Gold, RewardedBonusKind.Elixir, RewardedBonusKind.Gems, RewardedBonusKind.Dynamite, RewardedBonusKind.Damage };

        /// <summary>ID вида в команде и оферте: gold, elixir, gems, dynamite, damage.</summary>
        public static string Id(RewardedBonusKind kind)
        {
            switch (kind)
            {
                case RewardedBonusKind.Gold: return "gold";
                case RewardedBonusKind.Elixir: return "elixir";
                case RewardedBonusKind.Gems: return "gems";
                case RewardedBonusKind.Dynamite: return "dynamite";
                case RewardedBonusKind.Damage: return "damage";
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public static bool TryParse(string id, out RewardedBonusKind kind)
        {
            foreach (var k in s_Kinds)
                if (string.Equals(Id(k), id, StringComparison.Ordinal))
                {
                    kind = k;
                    return true;
                }
            kind = RewardedBonusKind.Gold;
            return false;
        }

        /// <summary>Место показа рекламы для аналитики: bonus_gold, bonus_elixir, bonus_gems, bonus_dynamite, bonus_damage.</summary>
        public static string Placement(RewardedBonusKind kind) => "bonus_" + Id(kind);

        /// <summary>
        /// Взвешенный выбор вида: roll ∈ [0, 1) делит сумму весов доступных видов (порядок Gold, Elixir, Gems, Dynamite, Damage).
        /// false — доступных видов с весом &gt; 0 нет.
        /// </summary>
        public static bool Pick(RewardedBonusRules rules, Func<RewardedBonusKind, bool> available, double roll, out RewardedBonusKind kind)
        {
            kind = RewardedBonusKind.Gold;
            if (rules == null) return false;
            int total = 0;
            foreach (var k in s_Kinds)
                if (rules.WeightOf(k) > 0 && available(k)) total += rules.WeightOf(k);
            if (total <= 0) return false;
            double at = Math.Max(0.0, Math.Min(roll, 0.999999999)) * total;
            int acc = 0;
            foreach (var k in s_Kinds)
            {
                int w = rules.WeightOf(k);
                if (w <= 0 || !available(k)) continue;
                acc += w;
                if (at < acc)
                {
                    kind = k;
                    return true;
                }
            }
            return false;
        }
    }
}
