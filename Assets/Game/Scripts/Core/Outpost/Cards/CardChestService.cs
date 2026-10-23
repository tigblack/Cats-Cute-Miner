using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace CatMiners.Core.Outpost
{
    /// <summary>Что дал сундук: карточки, эликсир, новые (впервые полученные) карточки.</summary>
    public sealed class ChestResult
    {
        public string ChestId;
        /// <summary>Копии по карточкам в порядке первой выдачи.</summary>
        public readonly List<CardGrant> Cards = new List<CardGrant>();
        public long Elixir;
        public readonly List<string> NewCards = new List<string>();
        public int TotalCopies;

        /// <summary>Компактная запись для события вида: "ca002:2,ca014:1".</summary>
        public string Detail()
        {
            var sb = new StringBuilder();
            foreach (var g in Cards)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(g.CardId).Append(':').Append(g.Copies);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Открытие сундука (адаптация прежнего GachaService к Cat Miners, Docs/Rework/CARD_EFFECT_MAPPING.md §Сундуки):
    /// (1) гарантированные карточки записи Gacha (у записей с GuaranteedCardMin — нижние границы, как в прежней игре);
    /// (2) эликсир: нижняя граница у таких записей и у фиксированного диапазона, иначе бросок NextInt в [min, max]; × множитель
    /// эликсира зоны по GachaType (2 — без множителя) × (1 + бонус «Шахтёрский фонарь»), floor;
    /// (3) случайные карты: max(1, RoundHalfUp(BaseNumCards × множитель карт зоны)) + бонус «Карта сокровищ»; редкость каждой —
    /// бросок по нормализованным шансам Cat Miners (card_rules.json; веса прежнего Gacha не считаются вероятностями), «Клевер
    /// удачи» усиливает Rare и выше; карточка — равновероятно среди открытых по прогрессу (MineUnlock ≤ HighestZone) этой
    /// редкости, при отсутствии — следующая редкость ниже. Генератор — Profile.CardRng; порядок бросков фиксирован.
    /// </summary>
    public static class CardChestService
    {
        /// <summary>
        /// Открыть сундук и применить к профилю. minimumCardCopies — минимум копий minimumCardId в результате:
        /// дополняет обычные выдачи после всех бросков, не меняя поток RNG. stats — бонусы (null — без бонусов).
        /// </summary>
        public static ChestResult Open(OutpostProfile p, CardCatalog cat, ChestDef chest, int zone, TeamStats stats,
            string minimumCardId = null, int minimumCardCopies = 0)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (cat == null) throw new ArgumentNullException(nameof(cat));
            if (chest == null) throw new ArgumentNullException(nameof(chest));
            if (minimumCardCopies > 0 && cat.Get(minimumCardId) == null) throw new ArgumentException("Unknown minimum card", nameof(minimumCardId));
            if (p.CardRng == null) p.CardRng = new Pcg32(p.Seed, cat.Rules.CardRngStream);
            var rng = p.CardRng;
            var scale = cat.Zone(zone);
            var result = new ChestResult { ChestId = chest.Id };

            foreach (var g in chest.Guaranteed) Add(result, g.CardId, g.Copies);

            BigInteger elixirMin = chest.ElixirMin.Floor(), elixirMax = chest.ElixirMax.Floor();
            BigInteger elixir = elixirMin;
            if (!chest.UsesMinimums && elixirMax > elixirMin)
            {
                var span = elixirMax - elixirMin + 1;
                elixir = elixirMin + rng.NextInt((int)BigInteger.Min(span, int.MaxValue));
            }
            var elixirBonus = stats != null ? stats.CardChestElixir : Rational.Zero;
            var elixirValue = (new Rational(elixir) * scale.ElixirFor(chest.GachaType) * (Rational.One + elixirBonus)).Floor();
            result.Elixir = (long)BigInteger.Max(BigInteger.Zero, BigInteger.Min(elixirValue, long.MaxValue / 4));

            int random = 0;
            if (chest.BaseNumCards > 0)
            {
                var n = (new Rational(chest.BaseNumCards) * scale.CardsFor(chest.GachaType)).RoundHalfUp();
                random = (int)BigInteger.Max(BigInteger.One, BigInteger.Min(n, 100_000));
            }
            random += stats != null ? stats.CardChestCards : 0;
            if (random > 0)
            {
                var weights = Weights(chest.Chances ?? cat.Rules.DefaultChances, stats != null ? stats.CardChestLuck : Rational.Zero, out long total);
                for (int i = 0; i < random; i++)
                {
                    int rarity = RollRarity(rng, weights, total);
                    var def = PickCard(p, cat, rarity, rng);
                    if (def != null) Add(result, def.Id, 1);
                }
            }

            if (minimumCardCopies > 0)
            {
                int existing = 0;
                foreach (var g in result.Cards)
                    if (g.CardId == minimumCardId) existing = g.Copies;
                Add(result, minimumCardId, minimumCardCopies - existing);
            }

            foreach (var g in result.Cards)
            {
                var def = cat.Get(g.CardId);
                if (CardService.Grant(p, def, g.Copies)) result.NewCards.Add(def.Id);
                result.TotalCopies = checked(result.TotalCopies + g.Copies);
            }
            if (result.Elixir > 0) p.Elixir = checked(p.Elixir + result.Elixir);
            return result;
        }

        /// <summary>
        /// Состав сундука в шахте <paramref name="zone"/> без открытия (окно шансов магазина, ТЗ 10.5): число случайных карточек и
        /// диапазон эликсира — те же формулы, что у <see cref="Open"/>, с бонусами карточек игрока; генератор не трогается.
        /// </summary>
        public static void Preview(CardCatalog cat, ChestDef chest, int zone, TeamStats stats, out int cards, out long elixirMin, out long elixirMax)
        {
            if (cat == null) throw new ArgumentNullException(nameof(cat));
            if (chest == null) throw new ArgumentNullException(nameof(chest));
            var scale = cat.Zone(zone);
            var elixirScale = scale.ElixirFor(chest.GachaType) * (Rational.One + (stats != null ? stats.CardChestElixir : Rational.Zero));
            long Elixir(Rational v) => (long)BigInteger.Max(BigInteger.Zero, BigInteger.Min((new Rational(v.Floor()) * elixirScale).Floor(), long.MaxValue / 4));
            elixirMin = Elixir(chest.ElixirMin);
            elixirMax = chest.UsesMinimums ? elixirMin : Elixir(chest.ElixirMax);
            cards = 0;
            if (chest.BaseNumCards > 0)
            {
                var n = (new Rational(chest.BaseNumCards) * scale.CardsFor(chest.GachaType)).RoundHalfUp();
                cards = (int)BigInteger.Max(BigInteger.One, BigInteger.Min(n, 100_000));
            }
            cards += stats != null ? stats.CardChestCards : 0;
            foreach (var g in chest.Guaranteed) cards += g.Copies;
        }

        /// <summary>Эликсир камня (Soft в RewardCycle): max(1, floor(F-0(SoftCurrency*, x = уровень камня) × MiningSoftCurrencyMultiplier зоны)).</summary>
        public static long RockElixir(CardCatalog cat, RockRewardDef rock, int rockLevel, int zone)
        {
            var x = new Rational(Math.Max(1, rockLevel));
            var v = (rock.SoftBase + rock.SoftMultiplier * x + rock.SoftGrowth * x * x) * cat.Zone(zone).MiningElixir;
            return (long)BigInteger.Max(BigInteger.One, BigInteger.Min(v.Floor(), long.MaxValue / 4));
        }

        static void Add(ChestResult r, string cardId, int copies)
        {
            if (copies <= 0) return;
            foreach (var g in r.Cards)
                if (g.CardId == cardId)
                {
                    g.Copies += copies;
                    return;
                }
            r.Cards.Add(new CardGrant { CardId = cardId, Copies = copies });
        }

        /// <summary>Веса редкостей 1…6 (ppm) с «Клевером»: Rare и выше × (1 + luck), Common — остаток (не меньше 0).</summary>
        static long[] Weights(int[] chances, Rational luck, out long total)
        {
            var w = new long[CardRules.RarityCount];
            long others = 0;
            for (int r = 1; r < w.Length; r++)
            {
                w[r] = r >= 2 ? (long)(new Rational(chances[r]) * (Rational.One + luck)).Floor() : chances[r];
                others += w[r];
            }
            w[0] = Math.Max(0, CardRules.ChanceScale - others);
            total = w[0] + others;
            if (total <= 0)
            {
                w[0] = 1;
                total = 1;
            }
            return w;
        }

        static int RollRarity(Pcg32 rng, long[] weights, long total)
        {
            long roll = rng.NextInt((int)Math.Min(total, int.MaxValue));
            for (int r = 0; r < weights.Length; r++)
            {
                if (roll < weights[r]) return r + 1;
                roll -= weights[r];
            }
            return 1;
        }

        static CardDef PickCard(OutpostProfile p, CardCatalog cat, int rarity, Pcg32 rng)
        {
            for (int r = rarity; r >= 1; r--)
            {
                var pool = cat.OfRarity(r);
                int open = 0;
                foreach (var d in pool)
                    if (CardService.IsUnlocked(p, d)) open++;
                if (open == 0) continue;
                int k = rng.NextInt(open);
                foreach (var d in pool)
                {
                    if (!CardService.IsUnlocked(p, d)) continue;
                    if (k-- == 0) return d;
                }
            }
            return null;
        }
    }
}
