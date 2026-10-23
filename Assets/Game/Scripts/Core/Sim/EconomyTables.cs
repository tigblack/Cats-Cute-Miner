using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;

namespace CatMiners.Core.Sim
{
    /// <summary>
    /// Справочные таблицы темпа рабочего профиля v1 (ТЗ, Приложение C.4): time-to-kill, цены, приросты и окупаемость
    /// генераторов, стоимость улучшения чертежей. Считаются кодом игры (GameSession) без бонусов ворот и карт.
    /// </summary>
    public static class EconomyTables
    {
        public static string Build(BalanceRepository balance, int generatorLevels = 40, int generatorZones = 10)
        {
            var sb = new StringBuilder();
            TimeToKill(sb, balance, generatorZones);
            Generators(sb, balance, generatorLevels, generatorZones);
            CardCosts(sb, balance);
            return sb.ToString();
        }

        /// <summary>Короткая запись длительности для широких таблиц: 16 с, 4.5 мин, 2.1 ч, 3.4 д, 1.2 г.</summary>
        public static string Short(double seconds)
        {
            var inv = CultureInfo.InvariantCulture;
            if (double.IsInfinity(seconds) || double.IsNaN(seconds)) return "∞";
            if (seconds < 60) return seconds.ToString("0.#", inv) + " с";
            if (seconds < 3600) return (seconds / 60).ToString("0.#", inv) + " мин";
            if (seconds < 86400) return (seconds / 3600).ToString("0.#", inv) + " ч";
            if (seconds < 365 * 86400.0) return (seconds / 86400).ToString("0.#", inv) + " д";
            return NumberFormat.Format(GameNumber.FromDouble(seconds / (365 * 86400.0))) + " г";
        }

        /// <summary>Секунды работы одного котика уровня catLevel до разрушения цели (крит в v1 равен 0).</summary>
        public static double TimeToKill(GameSession s, string targetId, int targetLevel, int catLevel)
        {
            var hp = s.Balance.TargetHp(targetId, targetLevel);
            var hits = (hp / s.CatDamage(catLevel)).CeilingTolerant();
            return hits.ToDouble() * GameSession.TicksToSeconds(s.AttackDelayTicks(catLevel));
        }

        /// <summary>
        /// Цели таблицы time-to-kill (C.4) из данных, без литералов ID (ТЗ 4.1): базовый камень — самый частый тип камня
        /// в zone1..zones; шахты этих зон в порядке первого появления (у которых есть таблица HP); затем ворота и выход.
        /// </summary>
        public static List<string> TimeToKillTargets(BalanceRepository balance, int zones = 10)
        {
            var result = new List<string>();
            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            for (int z = 1; z <= Math.Min(zones, balance.ZoneCount); z++)
            {
                var layout = ZoneLayout.Build(balance, z);
                foreach (var o in layout.Objects)
                {
                    if (o.Kind != ObjectKind.Rock || !balance.HasHpTable(o.TypeId)) continue;
                    if (!counts.ContainsKey(o.TypeId))
                    {
                        counts[o.TypeId] = 0;
                        order.Add(o.TypeId);
                    }
                    counts[o.TypeId]++;
                }
            }
            string baseRock = null;
            int most = 0;
            foreach (var id in order)
                if (counts[id] > most)
                {
                    most = counts[id];
                    baseRock = id;
                }
            if (baseRock != null) result.Add(baseRock);
            foreach (var kv in GeneratorsInFirstZones(balance, zones))
                if (kv.Key != BalanceRepository.CartId && balance.HasHpTable(kv.Key) && !result.Contains(kv.Key)) result.Add(kv.Key);
            foreach (var id in new[] { BalanceRepository.CheckpointId, BalanceRepository.ExitId })
                if (balance.HasHpTable(id) && !result.Contains(id)) result.Add(id);
            return result;
        }

        static void TimeToKill(StringBuilder sb, BalanceRepository balance, int zones)
        {
            var s = GameSession.NewGame(balance, 1UL, SaveSlots.Debug, 0L, 1);
            var targets = TimeToKillTargets(balance, zones);
            var shaftZones = new Dictionary<string, int>();
            foreach (var kv in GeneratorsInFirstZones(balance, zones)) shaftZones[kv.Key] = kv.Value;
            sb.AppendLine("## Time-to-kill: один котик, время до разрушения цели");
            sb.AppendLine();
            sb.AppendLine("`ceil(HP / урон) × задержка удара`; урон `2.1^(L−1)`, задержка 1 с (E-02). Строки — уровень котика, столбцы — уровень цели. Несколько котиков на одной цели делят время пропорционально суммарному урону. " +
                          "Цели: самый частый камень zone1–" + zones.ToString(CultureInfo.InvariantCulture) + ", все шахты этих зон, ворота и выход.");
            foreach (var id in targets)
            {
                string label;
                if (id == BalanceRepository.CheckpointId) label = " (ворота)";
                else if (id == BalanceRepository.ExitId) label = " (выход)";
                else if (shaftZones.TryGetValue(id, out int fromZone)) label = " (шахта, с zone" + fromZone.ToString(CultureInfo.InvariantCulture) + ")";
                else label = " (базовый камень)";
                sb.AppendLine();
                sb.AppendLine("### " + id + label);
                sb.AppendLine();
                var header = new StringBuilder("| Котик \\ цель |");
                var line = new StringBuilder("|---|");
                for (int t = 1; t <= 10; t++)
                {
                    header.Append(" L").Append(t.ToString(CultureInfo.InvariantCulture)).Append(" |");
                    line.Append("---:|");
                }
                sb.AppendLine(header.ToString());
                sb.AppendLine(line.ToString());
                for (int cat = 1; cat <= 8; cat++)
                {
                    var row = new StringBuilder("| L" + cat.ToString(CultureInfo.InvariantCulture) + " |");
                    for (int t = 1; t <= 10; t++) row.Append(' ').Append(HasLevel(balance, id, t) ? Short(TimeToKill(s, id, t, cat)) : "—").Append(" |");
                    sb.AppendLine(row.ToString());
                }
            }
            sb.AppendLine();
        }

        static bool HasLevel(BalanceRepository balance, string id, int level)
        {
            try
            {
                balance.TargetHp(id, level);
                return true;
            }
            catch (BalanceLoadException)
            {
                return false;
            }
        }

        /// <summary>Генераторы для таблиц: кузница и шахты в порядке первого появления в zone1..zones.</summary>
        public static List<KeyValuePair<string, int>> GeneratorsInFirstZones(BalanceRepository balance, int zones)
        {
            var result = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>(BalanceRepository.CartId, 1) };
            var seen = new HashSet<string>();
            for (int z = 1; z <= Math.Min(zones, balance.ZoneCount); z++)
            {
                var layout = ZoneLayout.Build(balance, z);
                foreach (var sh in layout.Shafts)
                    if (seen.Add(sh.TypeId)) result.Add(new KeyValuePair<string, int>(sh.TypeId, z));
            }
            return result;
        }

        static void Generators(StringBuilder sb, BalanceRepository balance, int levels, int zones)
        {
            sb.AppendLine("## Генераторы: цена, прирост и окупаемость, уровни 1–" + levels.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.AppendLine("Без ворот и карт (множители ×1). «Цена» — переход L → L+1 (E-03). «Доход/с» — выход за цикл с учётом достигнутых порогов (E-04), делённый на длительность цикла. «Окупаемость» — цена / прирост дохода в секунду после покупки. ★ — на уровне L+1 достигается порог.");
            foreach (var kv in GeneratorsInFirstZones(balance, zones))
            {
                var s = GameSession.NewGame(balance, 1UL, SaveSlots.Debug, 0L, kv.Value);
                int ord = -1;
                for (int i = 0; i < s.Layout.Objects.Count; i++)
                {
                    var o = s.Layout.Objects[i];
                    bool match = kv.Key == BalanceRepository.CartId ? o.Kind == ObjectKind.Cart : (o.Kind == ObjectKind.Shaft && o.TypeId == kv.Key);
                    if (match) { ord = i; break; }
                }
                if (ord < 0) continue;
                if (s.Layout.Objects[ord].Kind == ObjectKind.Shaft)
                {
                    s.DebugCompleteTarget(ord);
                    s.ActivateShaft(ord);
                }
                s.Player.Cards.Clear();
                s.Events.Clear();
                var gen = s.Mine.Objects[ord].Gen;
                var def = s.DefFor(s.Layout.Objects[ord]);
                int max = Math.Min(levels, def.MaxLevel);

                sb.AppendLine();
                sb.AppendLine("### " + kv.Key + (kv.Key == BalanceRepository.CartId ? " (кузница)" : " (с zone" + kv.Value.ToString(CultureInfo.InvariantCulture) + ")"));
                sb.AppendLine();
                sb.AppendLine("| L | Цена L→L+1 | Выход за цикл | Цикл | Доход/с | Прирост дохода/с | Окупаемость |");
                sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|");
                for (int L = 1; L <= max; L++)
                {
                    var now = Measure(s, ord, gen, def, L, out var output, out double cycle);
                    string cost = "—", delta = "—", payback = "—";
                    bool thresholdNext = false;
                    if (L < def.MaxLevel)
                    {
                        var price = s.UpgradeCost(ord, L);
                        var next = Measure(s, ord, gen, def, L + 1, out _, out _);
                        var gain = next - now;
                        cost = NumberFormat.Format(price);
                        delta = NumberFormat.Format(gain);
                        payback = gain.IsPositive ? Short((price / gain).ToDouble()) : "∞";
                        thresholdNext = Array.IndexOf(def.Thresholds, L + 1) >= 0;
                    }
                    sb.AppendLine("| " + L.ToString(CultureInfo.InvariantCulture) + (thresholdNext ? " ★" : "") + " | " + cost + " | " + NumberFormat.Format(output) + " | " +
                                  Short(cycle) + " | " + NumberFormat.Format(now) + " | " + delta + " | " + payback + " |");
                }
                Measure(s, ord, gen, def, 1, out _, out _);
            }
            sb.AppendLine();
        }

        /// <summary>
        /// Доход генератора в секунду на уровне level без изменения состояния: выход за цикл с порогами, достигнутыми к этому
        /// уровню (E-04), множителями manager-карт и глобальным (ворота, карты кода 10), делённый на длительность цикла.
        /// Уровень и счётчик порогов генератора восстанавливаются. Генератора нет — 0.
        /// </summary>
        public static GameNumber IncomePerSecondAt(GameSession s, int ord, int level)
        {
            var gen = s.Mine.Objects[ord].Gen;
            if (gen == null) return GameNumber.Zero;
            var def = s.DefFor(s.Layout.Objects[ord]);
            int savedLevel = gen.Level, savedReached = gen.ThresholdsReached;
            try
            {
                return Measure(s, ord, gen, def, Math.Max(1, Math.Min(level, def.MaxLevel)), out _, out _);
            }
            finally
            {
                gen.Level = savedLevel;
                gen.ThresholdsReached = savedReached;
            }
        }

        static GameNumber Measure(GameSession s, int ord, GeneratorState gen, GameSession.GeneratorDef def, int level, out GameNumber output, out double cycle)
        {
            gen.Level = level;
            int reached = 0;
            while (reached < def.Thresholds.Length && level >= def.Thresholds[reached]) reached++;
            gen.ThresholdsReached = reached;
            output = s.GeneratorOutput(ord);
            cycle = GameSession.TicksToSeconds(s.GeneratorCycleTicks(ord));
            return output / GameNumber.FromDouble(cycle);
        }

        static void CardCosts(StringBuilder sb, BalanceRepository balance)
        {
            sb.AppendLine("## Улучшение чертежей (`CardUpgradeCosts`, E-14)");
            sb.AppendLine();
            sb.AppendLine("Переход L → L+1: копии и эликсир; в скобках — эликсир нарастающим итогом от уровня 1. Показаны первые 10 переходов.");
            sb.AppendLine();
            sb.AppendLine("| Редкость | Макс. ур. | 1→2 | 2→3 | 3→4 | 4→5 | 5→6 | 6→7 | 7→8 | 8→9 | 9→10 | 10→11 |");
            sb.AppendLine("|---|---:|---|---|---|---|---|---|---|---|---|---|");
            var rarities = new List<int>(balance.CardUpgradeCosts.Keys);
            rarities.Sort();
            foreach (int rarity in rarities)
            {
                var c = balance.CardUpgradeCosts[rarity];
                var row = new StringBuilder("| " + Rarity.Name(rarity) + " | " + balance.CardMaxLevel(rarity).ToString(CultureInfo.InvariantCulture) + " |");
                double cumulative = 0;
                for (int i = 0; i < 10; i++)
                {
                    if (i >= c.Duplicates.Length)
                    {
                        row.Append(" — |");
                        continue;
                    }
                    cumulative += c.SoftCurrency[i];
                    row.Append(' ').Append(c.Duplicates[i].ToString(CultureInfo.InvariantCulture)).Append(" шт. + ")
                       .Append(NumberFormat.Format(GameNumber.FromDouble(c.SoftCurrency[i]))).Append(" (")
                       .Append(NumberFormat.Format(GameNumber.FromDouble(cumulative))).Append(") |");
                }
                sb.AppendLine(row.ToString());
            }
            sb.AppendLine();
        }
    }
}
