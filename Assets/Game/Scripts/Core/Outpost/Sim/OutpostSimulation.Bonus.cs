using System;
using System.Collections.Generic;
using System.Numerics;

namespace CatMiners.Core.Outpost
{
    public sealed partial class OutpostSimulation
    {
        // ───────────── Бонус за рекламу и мортира с динамитом (ТЗ 10.6) ─────────────

        public bool DamageBonusActive => Profile.DamageBonusSecondsLeft.Sign > 0;

        // A real event deadline prevents offline/event-driven advancement from skipping the expiry boundary.
        long NextDamageBonusTick() => DamageBonusActive
            ? checked(Now + ExactMath.ToLongChecked((Profile.DamageBonusSecondsLeft * Balance.TicksPerSecond).Ceil(), "DamageBonusExpiry"))
            : long.MaxValue;

        // Called before advancing Run.Now and before hits at t. The boost covers [grant, expiry).
        void AdvanceDamageBonus(long t)
        {
            if (!DamageBonusActive || t <= Now) return;
            var left = Profile.DamageBonusSecondsLeft - Rational.Of(t - Now, Balance.TicksPerSecond);
            Profile.DamageBonusSecondsLeft = left.Sign > 0 ? left : Rational.Zero;
            if (left.Sign > 0) return;
            m_StatsDirty = true;
            RecomputeStats(t);
            Touch(t);
        }

        /// <summary>
        /// Сумма бонуса вида kind сейчас; false — вид недоступен. Золото — на <see cref="RewardedBonusRules.GoldUpgrades"/> самых
        /// дешёвых покупок улучшений базы по одному уровню (нет — все ветки на пределе или шахта завершена), эликсир — на
        /// <see cref="RewardedBonusRules.ElixirUpgrades"/> самых дешёвых улучшений своих карточек (иначе ElixirFloor), гемы —
        /// <see cref="RewardedBonusRules.Gems"/>, динамит — 1, если бочки на поле нет и целей, доступных кирке, не меньше
        /// <see cref="RewardedBonusRules.DynamiteMinTargets"/>.
        /// </summary>
        public bool TryQuoteBonus(RewardedBonusKind kind, out BigInteger amount)
        {
            amount = BigInteger.Zero;
            var rules = m_Configs.Store?.RewardedBonus;
            if (rules == null) return false;
            switch (kind)
            {
                case RewardedBonusKind.Gold:
                    amount = CheapestUpgradesCost(rules.GoldUpgrades);
                    return amount.Sign > 0;
                case RewardedBonusKind.Elixir:
                    amount = CheapestCardUpgradesElixir(rules.ElixirUpgrades, rules.ElixirFloor);
                    return true;
                case RewardedBonusKind.Gems:
                    amount = rules.Gems;
                    return true;
                case RewardedBonusKind.Dynamite:
                    amount = BigInteger.One;
                    return Run.Dynamite == null && Run.Phase == RunPhase.Running && StrikeableTargetCount() >= rules.DynamiteMinTargets;
                case RewardedBonusKind.Damage:
                    amount = BigInteger.One;
                    return !DamageBonusActive && Run.Phase == RunPhase.Running;
                default:
                    return false;
            }
        }

        /// <summary>Оферта иконки: вид по весам среди доступных (roll ∈ [0, 1)) и его сумма сейчас.</summary>
        public RewardedBonusOffer RollBonus(double roll)
        {
            var rules = m_Configs.Store?.RewardedBonus;
            var amounts = new Dictionary<RewardedBonusKind, BigInteger>();
            bool Available(RewardedBonusKind k)
            {
                if (amounts.TryGetValue(k, out var a)) return a.Sign > 0;
                bool ok = TryQuoteBonus(k, out a);
                amounts[k] = ok ? a : BigInteger.Zero;
                return ok;
            }
            if (!RewardedBonus.Pick(rules, Available, roll, out var kind)) return default;
            return new RewardedBonusOffer { Valid = true, Kind = kind, Amount = amounts[kind] };
        }

        /// <summary>Сумма count самых дешёвых покупок по одному уровню (жадно: после покупки ветка предлагает следующий уровень).</summary>
        BigInteger CheapestUpgradesCost(int count)
        {
            var run = Run;
            if (run.Phase == RunPhase.ReadyToContinue) return BigInteger.Zero;
            var stats = m_StatsDirty ? ComputeStats() : m_Stats;
            var levels = (int[])run.Levels.Clone();
            var sum = BigInteger.Zero;
            for (int k = 0; k < count; k++)
            {
                int best = -1;
                var bestCost = BigInteger.Zero;
                for (int b = 0; b < levels.Length; b++)
                {
                    var cost = NextLevelCost(run, stats, (UpgradeBranch)b, levels[b]);
                    if (cost == null) continue;
                    if (best < 0 || cost.Value < bestCost)
                    {
                        best = b;
                        bestCost = cost.Value;
                    }
                }
                if (best < 0) break;
                sum += bestCost;
                levels[best]++;
            }
            return sum;
        }

        /// <summary>Цена уровня level → level+1 ветки с теми же скидками и пределом, что у <see cref="TeamUpgradeService.Quote"/>.</summary>
        static BigInteger? NextLevelCost(OutpostRun run, TeamStats stats, UpgradeBranch branch, int level)
        {
            var b = run.Balance;
            int extraCap = TeamUpgradeService.ExtraCapFor(stats, branch);
            if (branch == UpgradeBranch.Team) extraCap = Math.Max(-b.Branch(branch).Cap, extraCap - run.ExtraWorkers);
            var prices = run.PriceFactor;
            return TeamUpgradeService.NextCost(b, run.Zone, branch, level, TeamUpgradeService.DiscountFor(stats, branch), extraCap, prices);
        }

        /// <summary>Эликсир count самых дешёвых улучшений своих карточек ниже максимума (жадно, как у золота); нечего — floor.</summary>
        long CheapestCardUpgradesElixir(int count, long floor)
        {
            if (m_Cards == null) return floor;
            var levels = new Dictionary<string, int>(StringComparer.Ordinal);
            long sum = 0;
            for (int k = 0; k < count; k++)
            {
                CardDef best = null;
                long bestPrice = 0;
                foreach (var def in m_Cards.Cards)
                {
                    if (!levels.TryGetValue(def.Id, out int level)) levels[def.Id] = level = CardService.ProgressOf(Profile, def.Id)?.Level ?? 0;
                    if (level < 1 || level >= def.MaxLevel) continue;
                    long price = def.UpgradeElixir[level - 1];
                    if (best == null || price < bestPrice)
                    {
                        best = def;
                        bestPrice = price;
                    }
                }
                if (best == null) break;
                sum = checked(sum + bestPrice);
                levels[best.Id]++;
            }
            return sum > 0 ? sum : floor;
        }

        /// <summary>Целей, доступных кирке сейчас (<see cref="StrikeBlockReason"/> == null).</summary>
        public int StrikeableTargetCount()
        {
            var mask = StrikeableMask();
            int n = 0;
            foreach (bool m in mask)
                if (m) n++;
            return n;
        }

        /// <summary>Снимок доступности кирке по индексам целей карты.</summary>
        bool[] StrikeableMask()
        {
            var targets = m_Map.Targets;
            var mask = new bool[targets.Length];
            for (int i = 0; i < targets.Length; i++) mask[i] = !Run.Targets[i].Destroyed && StrikeBlockReason(targets[i].Id) == null;
            return mask;
        }

        /// <summary>Цель в радиусе взрыва: расстояние от центра клетки бочки до центра ближайшей клетки footprint ≤ radius.</summary>
        bool InBlast(MapTarget mt, int cell, Rational radius)
        {
            int bc = m_Map.Col(cell), br = m_Map.Row(cell);
            var r2 = radius * radius;
            foreach (int c in mt.Cells)
            {
                long dx = m_Map.Col(c) - bc, dy = m_Map.Row(c) - br;
                if ((Rational)(dx * dx + dy * dy) <= r2) return true;
            }
            return false;
        }

        /// <summary>Награда бонуса (ТЗ 10.6): золото — в кошелёк и BonusCredited, эликсир и гемы — в профиль, динамит — выстрел мортиры.</summary>
        CommandResult ExecuteRewardBonus(OutpostCommand cmd, long t)
        {
            if (!RewardedBonus.TryParse(cmd.ItemId, out var kind)) return CommandResult.Fail("unknown");
            if (m_Configs.Store?.RewardedBonus == null) return CommandResult.Fail("unavailable");
            var run = Run;
            switch (kind)
            {
                case RewardedBonusKind.Damage:
                    if (cmd.Amount != BigInteger.One) return CommandResult.Fail("unknown");
                    // A second earned receipt refreshes to five minutes, never multiplies or adds duration.
                    Profile.DamageBonusSecondsLeft = RewardedBonus.DamageDurationSeconds;
                    m_StatsDirty = true;
                    break;
                case RewardedBonusKind.Dynamite:
                    // Награда уже заработана: прежняя бочка (её не может быть при оферте из интерфейса) взрывается, новая летит.
                    if (run.Dynamite != null) ExecuteDetonate(OutpostCommand.Detonate(), t);
                    LaunchDynamite(t);
                    return new CommandResult { Ok = true, Levels = 1 };
                case RewardedBonusKind.Gold:
                    if (cmd.Amount.Sign <= 0) return CommandResult.Fail("unknown");
                    run.Wallet += cmd.Amount;
                    run.BonusCredited += cmd.Amount;
                    break;
                case RewardedBonusKind.Elixir:
                    if (cmd.Amount.Sign <= 0 || cmd.Amount > long.MaxValue) return CommandResult.Fail("unknown");
                    Profile.Elixir = checked(Profile.Elixir + (long)cmd.Amount);
                    break;
                case RewardedBonusKind.Gems:
                    if (cmd.Amount.Sign <= 0 || cmd.Amount > long.MaxValue) return CommandResult.Fail("unknown");
                    Profile.Gems = checked(Profile.Gems + (long)cmd.Amount);
                    break;
            }
            Touch(t);
            var e = OutpostEvent.Create(OutpostEventType.BonusGranted, t);
            e.ItemId = RewardedBonus.Id(kind);
            e.Value = cmd.Amount;
            Emit(e);
            return new CommandResult { Ok = true, Cost = cmd.Amount };
        }

        /// <summary>
        /// Выстрел мортиры: бочка ложится на клетку груза удара (<see cref="StrikeDropCell"/>) той доступной кирке цели, чей взрыв
        /// задел бы больше всего доступных целей, при равенстве — с большим остатком HP, затем с меньшим индексом. Доступных
        /// целей нет — бочка падает у точки приёма и взрывается без урона (награда уже заработана).
        /// </summary>
        void LaunchDynamite(long t)
        {
            var rules = m_Configs.Store.RewardedBonus;
            var run = Run;
            var targets = m_Map.Targets;
            var mask = StrikeableMask();
            int bestTi = -1, bestCell = -1, bestHits = -1;
            long bestHp = -1;
            for (int i = 0; i < targets.Length; i++)
            {
                if (!mask[i]) continue;
                int cell = StrikeDropCell(targets[i]);
                int hits = 0;
                for (int j = 0; j < targets.Length; j++)
                    if (mask[j] && InBlast(targets[j], cell, rules.DynamiteRadius)) hits++;
                long hp = run.Targets[i].RemainingHpUnits;
                if (hits > bestHits || (hits == bestHits && hp > bestHp))
                {
                    bestTi = i;
                    bestCell = cell;
                    bestHits = hits;
                    bestHp = hp;
                }
            }
            var barrel = new DynamiteBarrel
            {
                Seq = run.NextDynamiteSeq++,
                Cell = bestTi >= 0 ? bestCell : m_Map.IntakeCell,
                TargetId = bestTi >= 0 ? targets[bestTi].Id : -1,
                LaunchedTick = t,
            };
            run.Dynamite = barrel;
            Touch(t);
            var e = OutpostEvent.Create(OutpostEventType.DynamiteLaunched, t);
            e.Cell = barrel.Cell;
            e.TargetId = barrel.TargetId;
            e.BatchSeq = barrel.Seq;
            Emit(e);
        }

        /// <summary>
        /// Взрыв бочки (ТЗ 10.6): цели, доступные кирке до взрыва и лежащие в радиусе, получают урон — цель мортиры
        /// DynamiteCenterShare, остальные DynamiteRingShare × (1 + карточка DynamitePower) от максимальной прочности, не больше
        /// остатка; порции и разрушение — как у кирки (<see cref="ApplyHit"/>), золото — только сдачей груза. Бочка исчезает
        /// в любой фазе; вне Running доступных целей нет и взрыв без урона.
        /// </summary>
        CommandResult ExecuteDetonate(OutpostCommand cmd, long t)
        {
            var run = Run;
            var barrel = run.Dynamite;
            if (barrel == null) return CommandResult.Fail("unknown");
            var rules = m_Configs.Store?.RewardedBonus;
            run.Dynamite = null;
            Touch(t);
            var hits = new List<(int ti, long damage)>();
            TeamStats stats = null;
            if (rules != null)
            {
                stats = m_StatsDirty ? ComputeStats() : m_Stats;
                var ring = rules.DynamiteRingShare * (Rational.One + stats.CardDynamitePower);
                var targets = m_Map.Targets;
                var mask = StrikeableMask();
                for (int i = 0; i < targets.Length; i++)
                {
                    if (!mask[i] || !InBlast(targets[i], barrel.Cell, rules.DynamiteRadius)) continue;
                    var ts = run.Targets[i];
                    var share = targets[i].Id == barrel.TargetId ? rules.DynamiteCenterShare : ring;
                    var raw = (ts.MaxHpUnits * share).Ceil();
                    long damage = raw >= ts.RemainingHpUnits ? ts.RemainingHpUnits : (long)raw;
                    if (damage > 0) hits.Add((i, damage));
                }
            }
            long total = 0;
            foreach (var h in hits) total += h.damage;
            var e = OutpostEvent.Create(OutpostEventType.DynamiteExploded, t);
            e.Cell = barrel.Cell;
            e.TargetId = barrel.TargetId;
            e.BatchSeq = barrel.Seq;
            e.Count = hits.Count;
            e.Value = total;
            Emit(e);
            bool destroyedAny = false;
            int released = 0;
            foreach (var h in hits)
            {
                destroyedAny |= ApplyHit(h.ti, h.damage, stats, t, out int r);
                released += Math.Max(0, r);
            }
            if (destroyedAny) PhaseGatesAndCompletion(t);
            return new CommandResult { Ok = true, Levels = released, Cost = total };
        }
    }
}
