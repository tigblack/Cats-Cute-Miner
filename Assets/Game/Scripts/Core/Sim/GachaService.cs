using System;
using System.Collections.Generic;
using CatMiners.Core.Data;

namespace CatMiners.Core.Sim
{
    /// <summary>Контекст открытия сундука: текущая зона и карточки игрока.</summary>
    public sealed class GachaContext
    {
        public int Rank;
        public RankMultipliersDto Multipliers;
        public HashSet<string> GeneratorsInZone = new HashSet<string>();
        /// <summary>cardId → максимальный уровень, требуемый токенами s текущей зоны.</summary>
        public Dictionary<string, int> RequiredCardLevels = new Dictionary<string, int>();
        public Func<string, int> CardLevel = _ => 0;
    }

    /// <summary>
    /// Открытие сундука (ТЗ 10.3, Приложение C: E-11, E-12, E-13). Результат детерминирован при заданном ГСЧ.
    /// </summary>
    public static class GachaService
    {
        public static ChestResult Open(BalanceRepository b, GachaDto g, GachaContext ctx, Pcg32 rng)
        {
            var result = new ChestResult { GachaId = g.Id, Elixir = GameNumber.Zero };
            bool rangeMin = g.GuaranteedCardMin != null && g.GuaranteedCardMin.Length > 0;   // награда выхода без Crusher (E-13)

            // 1. Гарантированные карточки.
            if (g.GuaranteedCardIds != null)
            {
                for (int i = 0; i < g.GuaranteedCardIds.Length; i++)
                {
                    int count = rangeMin ? g.GuaranteedCardMin[i] : g.GuaranteedCardCounts[i];
                    if (count > 0) Add(result, g.GuaranteedCardIds[i], count);
                }
            }

            // 2. Класс множителей зоны по GachaType.
            double cardsMult = 1.0, softMult = 1.0;
            var m = ctx.Multipliers;
            if (m != null)
            {
                switch (g.GachaType)
                {
                    case 0: cardsMult = m.GachaCardsMultNormal; softMult = m.GachaSoftCurrencyMultNormal; break;
                    case 1: cardsMult = m.GachaCardsMultPremium; softMult = m.GachaSoftCurrencyMultPremium; break;
                    case 3: cardsMult = m.GachaCardsMultRare; softMult = m.GachaSoftCurrencyMultRare; break;
                }
            }

            // 3. Эликсир.
            double soft;
            if (rangeMin || g.SoftCurrencyMax <= g.SoftCurrencyMin) soft = g.SoftCurrencyMin;
            else soft = g.SoftCurrencyMin + rng.NextInt((int)Math.Floor(g.SoftCurrencyMax - g.SoftCurrencyMin) + 1);
            if (soft > 0) result.Elixir = Formula.Payout(GameNumber.FromDouble(soft * softMult));

            // 4–7. Случайные карточки.
            if (g.BaseNumCards > 0)
            {
                int n = Math.Max(1, Formula.RoundHalfUp(g.BaseNumCards * cardsMult));
                DrawRandomCards(b, g, ctx, rng, n, result);
            }
            return result;
        }

        static void Add(ChestResult r, string cardId, int count)
        {
            foreach (var c in r.Cards)
            {
                if (c.CardId == cardId)
                {
                    c.Count += count;
                    return;
                }
            }
            r.Cards.Add(new CardGrant { CardId = cardId, Count = count });
        }

        static double WeightFor(GachaDto g, int rarity)
        {
            switch (rarity)
            {
                case Rarity.Legendary: return g.LegendaryWeight;
                case Rarity.Epic: return g.EventEpicWeight;
                case Rarity.Rare: return g.RareWeight;
                case Rarity.Uncommon: return g.UncommonWeight;
                default: return 0;
            }
        }

        static void DrawRandomCards(BalanceRepository b, GachaDto g, GachaContext ctx, Pcg32 rng, int n, ChestResult result)
        {
            var counts = new int[7];
            int assigned = 0;
            foreach (int rarity in new[] { Rarity.Legendary, Rarity.Epic, Rarity.Rare, Rarity.Uncommon })
            {
                double w = WeightFor(g, rarity);
                if (w <= 0) continue;
                double q = n / w;
                int c = (int)Math.Floor(q + 1e-9);
                double frac = q - c;
                if (frac > 1e-12 && rng.NextDouble() < frac) c++;
                counts[rarity] = c;
                assigned += c;
            }
            // Если сумма превысила N — снимаем с самой младшей редкости.
            foreach (int rarity in new[] { Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary })
            {
                if (assigned <= n) break;
                int take = Math.Min(counts[rarity], assigned - n);
                counts[rarity] -= take;
                assigned -= take;
            }
            counts[Rarity.Common] = n - assigned;

            // Кандидаты; если их нет — карточки переходят в младшую редкость.
            var candidatesByRarity = new List<CardDto>[7];
            for (int rarity = Rarity.Legendary; rarity >= Rarity.Common; rarity--)
            {
                var cands = Candidates(b, ctx, rarity);
                candidatesByRarity[rarity] = cands;
                if (counts[rarity] > 0 && cands.Count == 0)
                {
                    if (rarity > Rarity.Common) counts[rarity - 1] += counts[rarity];
                    counts[rarity] = 0;
                }
            }

            for (int rarity = Rarity.Legendary; rarity >= Rarity.Common; rarity--)
            {
                int count = counts[rarity];
                if (count <= 0) continue;
                var cands = candidatesByRarity[rarity];
                int stacks = Math.Min(StackCount(b, rarity, count), cands.Count);
                stacks = Math.Max(1, Math.Min(stacks, count));
                var chosen = PickDistinct(cands, stacks, ctx, g, rng);
                int per = count / chosen.Count;
                int rem = count - per * chosen.Count;
                for (int i = 0; i < chosen.Count; i++)
                {
                    int amount = per + (i == 0 ? rem : 0);
                    if (amount > 0) Add(result, chosen[i].Id, amount);
                }
            }
        }

        static List<CardDto> Candidates(BalanceRepository b, GachaContext ctx, int rarity)
        {
            var list = new List<CardDto>();
            if (rarity >= Rarity.Majestic) return list;
            foreach (var c in b.CardsInOrder)
                if (c.Rarity == rarity && c.MineUnlock <= ctx.Rank) list.Add(c);
            return list;
        }

        /// <summary>Число разных ID в редкости (ТЗ 10.3 п. 7).</summary>
        public static int StackCount(BalanceRepository b, int rarity, int count)
        {
            int[] splits;
            switch (rarity)
            {
                case Rarity.Common: splits = b.Props.CommonStackSplitAt; break;
                case Rarity.Uncommon: splits = b.Props.UncommonStackSplitAt; break;
                case Rarity.Rare: splits = b.Props.RareStackSplitAt; break;
                case Rarity.Epic: splits = b.Props.EventEpicStackSplitAt; break;
                default: return 1;
            }
            int stacks = 1;
            if (splits == null) return stacks;
            var stackCounts = b.Props.CardStackCounts;
            for (int j = 0; j < splits.Length && j < stackCounts.Length; j++)
                if (count >= splits[j]) stacks = stackCounts[j];
            return stacks;
        }

        static double CandidateWeight(CardDto c, GachaContext ctx, GachaDto g)
        {
            double w = 1.0;
            if (c.IsManager && c.TargetIds != null)
            {
                foreach (var t in c.TargetIds)
                {
                    if (ctx.GeneratorsInZone.Contains(t))
                    {
                        if (g.ManagerChanceMult > 0) w *= g.ManagerChanceMult;
                        break;
                    }
                }
            }
            if (ctx.RequiredCardLevels.TryGetValue(c.Id, out int req) && ctx.CardLevel(c.Id) < req && g.LowCardChanceMult > 0)
                w *= g.LowCardChanceMult;
            return w;
        }

        static List<CardDto> PickDistinct(List<CardDto> cands, int count, GachaContext ctx, GachaDto g, Pcg32 rng)
        {
            var pool = new List<CardDto>(cands);
            var weights = new List<double>(pool.Count);
            foreach (var c in pool) weights.Add(CandidateWeight(c, ctx, g));
            var chosen = new List<CardDto>(count);
            while (chosen.Count < count && pool.Count > 0)
            {
                double total = 0;
                foreach (var w in weights) total += w;
                double r = rng.NextDouble() * total;
                int pick = pool.Count - 1;
                for (int i = 0; i < pool.Count; i++)
                {
                    r -= weights[i];
                    if (r < 0) { pick = i; break; }
                }
                chosen.Add(pool[pick]);
                pool.RemoveAt(pick);
                weights.RemoveAt(pick);
            }
            return chosen;
        }
    }
}
