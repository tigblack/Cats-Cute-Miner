using System;
using System.Collections.Generic;
using System.Numerics;
using CatMiners.Core.Data;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Новые HP и груз целей (ТЗ 12.4): ранг по уникальным исходным HP r/s/p зоны, точный ceil без double,
    /// TotalCargoUnits = max(MinCargoUnits, ceil(MaxHp / CargoHpDivisor)). Значения фиксируются при создании run.
    /// Адаптер <c>adapter-2</c> (<see cref="OutpostIds.AdapterVersion"/>, ТЗ 2.2 §4.2): цепи завода (<c>s</c>) монет не дают —
    /// у них TotalCargoUnits = 0; HP цепей по-прежнему по ТЗ 2.1 §12.4 (ранг из level токена, TypeHpFactor = 2).
    /// </summary>
    public static class OutpostLevelBalanceAdapter
    {
        public static TargetBalance[] Compute(OutpostMap map, OutpostBalanceConfig cfg, BalanceRepository repo)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            var healths = repo.Root.MiningTargetHealths;
            var sourceHp = new double[map.Targets.Length];
            var unique = new List<double>();
            for (int i = 0; i < map.Targets.Length; i++)
            {
                var t = map.Targets[i];
                if (t.Kind == 'x') continue;
                if (!healths.TryGetValue(t.TypeId, out var table))
                    throw new OutpostDataException(map.ZoneId, t.Id, "MiningTargetHealths: нет таблицы " + t.TypeId);
                if (t.SourceLevel < 1 || t.SourceLevel > table.Length)
                    throw new OutpostDataException(map.ZoneId, t.Id, "MiningTargetHealths." + t.TypeId + ": уровень " + t.SourceLevel + " вне 1.." + table.Length);
                double hp = table[t.SourceLevel - 1];
                if (!(hp > 0) || double.IsInfinity(hp))
                    throw new OutpostDataException(map.ZoneId, t.Id, "MiningTargetHealths." + t.TypeId + "[" + (t.SourceLevel - 1) + "]: неположительный HP");
                sourceHp[i] = hp;
                unique.Add(hp);
            }
            unique.Sort();
            int u = 0;
            for (int i = 0; i < unique.Count; i++)
                if (i == 0 || unique[i] != unique[i - 1]) unique[u++] = unique[i];
            unique.RemoveRange(u, unique.Count - u);

            var result = new TargetBalance[map.Targets.Length];
            for (int i = 0; i < map.Targets.Length; i++)
            {
                var t = map.Targets[i];
                var type = cfg.Type(t.Kind);
                Rational rank;
                if (type.FixedRank.HasValue) rank = type.FixedRank.Value;
                else
                {
                    int j = unique.BinarySearch(sourceHp[i]);
                    rank = unique.Count > 1 ? Rational.Of(j, unique.Count - 1) : Rational.Zero;
                }
                int area = t.IsGate ? 1 : t.Cells.Length;
                long maxHp = MaxHp(cfg, map.Number, rank, area, t.Kind);
                var valueFactor = type.ValueFactor;
                // Мусор обстановки (Docs/Rework/MINE_DRESSING.md): HP и цена порции — как у завала x той же площади × ярус.
                if (t.IsDebris)
                {
                    maxHp = Math.Max(1L, ExactMath.ToLongChecked((t.Piece.HpFactor * maxHp).Ceil(), "MaxHp"));
                    valueFactor = valueFactor * t.Piece.ValueFactor;
                }
                // ТЗ 2.2 §4.2 (Р2): вся ценность завода — в продаже товара, удары и разрушение цепей порций не выпускают.
                var units = t.IsFactory ? BigInteger.Zero : BigInteger.Max(cfg.MinCargoUnits, ExactMath.CeilDiv(maxHp, cfg.CargoHpDivisor));
                result[i] = new TargetBalance
                {
                    TargetId = t.Id,
                    MaxHp = maxHp,
                    TotalCargoUnits = ExactMath.ToIntChecked(units, "TotalCargoUnits"),
                    ValueFactor = valueFactor,
                    Rank01 = rank,
                    Area = area,
                };
            }
            return result;
        }

        /// <summary>
        /// Темп шахты (профиль b6, <see cref="OutpostBalanceConfig.PaceScale"/>): HP целей r/x/p — ceil(MaxHp · K(z)), груз —
        /// max(MinCargoUnits, ceil(HP / CargoHpDivisor)) от нового HP, то есть монеты шахты растут вместе с HP. Цепи завода (s) не
        /// меняются: их разрушение запускает завод, а ценность завода — товар (Р2). Возвращает тот же массив; K = 1 ничего не
        /// меняет. Базовые HP §12.4 (<see cref="Compute"/>) от K не зависят.
        /// </summary>
        public static TargetBalance[] ApplyPace(OutpostMap map, OutpostBalanceConfig cfg, TargetBalance[] balances)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (balances == null) throw new ArgumentNullException(nameof(balances));
            var k = cfg.ZonePace(map.Number);
            if (k == Rational.One) return balances;
            for (int i = 0; i < balances.Length; i++)
            {
                var t = map.Targets[i];
                if (t.IsFactory) continue;
                var b = balances[i];
                b.MaxHp = ExactMath.ToLongChecked((k * b.MaxHp).Ceil(), "MaxHp");
                var units = BigInteger.Max(cfg.MinCargoUnits, ExactMath.CeilDiv(b.MaxHp, cfg.CargoHpDivisor));
                b.TotalCargoUnits = ExactMath.ToIntChecked(units, "TotalCargoUnits");
            }
            return balances;
        }

        /// <summary>
        /// MaxHp = ceil(a/b · AreaFactor), a/b = HpBase · (1 + (z−1)·ZoneHpStep) · (1 + RankSpan·rank01) · TypeHpFactor;
        /// n ≥ AreaCapFromCells → ceil(a/b · AreaFactorCap), иначе минимальное h с h²b² ≥ a²n (ТЗ 12.4).
        /// </summary>
        public static long MaxHp(OutpostBalanceConfig cfg, int zone, Rational rank01, int area, char kind)
        {
            var type = cfg.Type(kind);
            var c = cfg.HpBase * (Rational.One + cfg.ZoneHpStep * (zone - 1)) * (Rational.One + cfg.RankSpan * rank01) * type.HpFactor;
            BigInteger hp = area >= cfg.AreaCapFromCells ? (c * cfg.AreaFactorCap).Ceil() : ExactMath.CeilTimesSqrt(c, area);
            return ExactMath.ToLongChecked(hp, "MaxHp");
        }
    }

    /// <summary>Баланс одной цели при создании run.</summary>
    public sealed class TargetBalance
    {
        public int TargetId;
        public long MaxHp;
        public int TotalCargoUnits;
        public Rational ValueFactor;
        public Rational Rank01;
        public int Area;
    }
}
