using System;
using System.Collections.Generic;
using System.Numerics;

namespace CatMiners.Core.Outpost
{
    /// <summary>Как модель игрока выбирает локальные улучшения (аудит баланса, Docs/Rework/BALANCE_AUDIT.md).</summary>
    public enum PurchaseStrategy
    {
        /// <summary>Без покупок.</summary>
        None,
        /// <summary>Круг «Кирки, Команда, Сумки, Обувь, Разгрузка, Оценка» по ×1, как бот C.</summary>
        RoundRobin,
        /// <summary>Самый дешёвый доступный уровень — типичная «покупаю что могу».</summary>
        Cheapest,
        /// <summary>Узкое место команды: доля времени котиков на действии × прирост скорости этого действия / цена.</summary>
        Bottleneck,
        /// <summary>Только урон, найм и оценка; логистику игрок не улучшает.</summary>
        DamageFocus,
    }

    /// <summary>
    /// Поведение игрока: частота заглядывания в «Базу», выбор покупок, удары киркой и действия между шахтами.
    /// Заводы (ТЗ 2.2 §9): при каждой проверке (раз в <see cref="CheckIntervalSec"/>; free — 30 с, active — 5 с, idle — 5 мин)
    /// игрок сначала доставляет все готовые партии и продаёт весь товар склада, затем покупает. Автодоставка включается
    /// улучшением карточек между шахтами (<see cref="BetweenMines"/>) и копиями из сундуков.
    /// Детерминировано: одна модель и один seed профиля дают один прогон.
    /// </summary>
    public sealed class PlayerModel
    {
        public string Name = "model";
        public PurchaseStrategy Strategy = PurchaseStrategy.Bottleneck;
        /// <summary>Период проверки панели «База», с; 0 — после каждого события (реакция бота C).</summary>
        public double CheckIntervalSec;
        /// <summary>Первый взгляд на панель после входа в шахту, с (только при CheckIntervalSec &gt; 0).</summary>
        public double FirstCheckSec;
        /// <summary>Частота ударов киркой во время серии; 0 — не бьёт.</summary>
        public double TapsPerSecond;
        /// <summary>Длительность серии ударов и отдыха между сериями, с; TapRestSec = 0 — непрерывно.</summary>
        public double TapBurstSec = 20;
        public double TapRestSec = 40;
        /// <summary>Сколько первых секунд шахты игрок вообще бьёт (0 — всю шахту).</summary>
        public double TapActiveSec;
        /// <summary>Между шахтами: «Надеть лучшее».</summary>
        public bool EquipBest = true;
        /// <summary>Между шахтами: детали на надетые предметы, сначала дешёвые.</summary>
        public bool SpendParts = true;
        /// <summary>Между шахтами: улучшить все доступные карточки, сначала дешёвые по эликсиру.</summary>
        public bool UpgradeCards = true;
        /// <summary>Абляция: слот, который игрок оставляет пустым (null — все слоты).</summary>
        public EquipSlot? SkipSlot;
        /// <summary>Карточки и детали тратятся только после каждой N-й шахты (1 — после каждой).</summary>
        public int MetaEveryMines = 1;
        /// <summary>При каждой проверке доставлять готовые партии заводов и продавать весь товар (ТЗ 2.2 §9).</summary>
        public bool ManageFactories = true;

        public PlayerModel Clone() => (PlayerModel)MemberwiseClone();

        public override string ToString() => Name + "(" + Strategy + " every " + CheckIntervalSec + "s, taps " + TapsPerSecond + "/s)";

        // ── Покупки ──

        static readonly UpgradeBranch[] s_Cycle =
        {
            UpgradeBranch.Pickaxes, UpgradeBranch.Team, UpgradeBranch.Bags, UpgradeBranch.Boots, UpgradeBranch.Unload, UpgradeBranch.Value,
        };

        static readonly UpgradeBranch[] s_All =
        {
            UpgradeBranch.Team, UpgradeBranch.Pickaxes, UpgradeBranch.Boots, UpgradeBranch.Bags, UpgradeBranch.Unload, UpgradeBranch.Value,
        };

        int m_CycleIndex;

        /// <summary>Сбросить состояние стратегии перед новой шахтой.</summary>
        public void BeginMine() => m_CycleIndex = 0;

        /// <summary>
        /// Заводы на проверке (ТЗ 2.2 §9): «Deliver» по всем готовым партиям и «Sell all» (<see cref="OutpostBot.TendFactories"/>),
        /// если модель это делает. Вызывается перед <see cref="Buy"/>, чтобы золото продажи сразу шло в покупки.
        /// </summary>
        public (int Delivered, BigInteger Sold) Tend(OutpostSimulation sim) =>
            ManageFactories ? OutpostBot.TendFactories(sim) : (0, BigInteger.Zero);

        /// <summary>Сделать покупки на текущей отметке; probe — доли времени котиков. Возвращает число покупок.</summary>
        public int Buy(OutpostSimulation sim, BalanceProbe probe)
        {
            if (sim.IsEconomyStopped || Strategy == PurchaseStrategy.None) return 0;
            int bought = 0;
            for (int guard = 0; guard < 400; guard++)
            {
                UpgradeBranch? pick = null;
                switch (Strategy)
                {
                    case PurchaseStrategy.RoundRobin:
                        for (int k = 0; k < s_Cycle.Length; k++)
                        {
                            int idx = (m_CycleIndex + k) % s_Cycle.Length;
                            if (!sim.Quote(s_Cycle[idx], BuyMode.One).CanBuy) continue;
                            pick = s_Cycle[idx];
                            m_CycleIndex = (idx + 1) % s_Cycle.Length;
                            break;
                        }
                        break;
                    case PurchaseStrategy.Cheapest:
                        pick = CheapestAffordable(sim, s_All);
                        break;
                    case PurchaseStrategy.DamageFocus:
                        pick = CheapestAffordable(sim, new[] { UpgradeBranch.Pickaxes, UpgradeBranch.Team, UpgradeBranch.Value });
                        break;
                    case PurchaseStrategy.Bottleneck:
                        pick = BottleneckPick(sim, probe);
                        break;
                }
                if (pick == null) break;
                var before = sim.Run.Wallet;
                var r = sim.Submit(OutpostCommand.BuyUpgrade(pick.Value));
                if (!r.Ok) break;
                probe?.OnPurchase(sim, pick.Value, r.Cost);
                bought++;
            }
            return bought;
        }

        static UpgradeBranch? CheapestAffordable(OutpostSimulation sim, UpgradeBranch[] branches)
        {
            UpgradeBranch? best = null;
            BigInteger bestCost = BigInteger.Zero;
            foreach (var br in branches)
            {
                var q = sim.Quote(br, BuyMode.One);
                if (!q.CanBuy) continue;
                if (best == null || q.Cost < bestCost)
                {
                    best = br;
                    bestCost = q.Cost;
                }
            }
            return best;
        }

        /// <summary>
        /// Оценка «разумного» игрока: цикл котика — последовательность действий, поэтому ускорение действия с долей времени
        /// share на долю g сокращает цикл примерно на share·g. Найм ценен, пока котики не простаивают; оценка добычи —
        /// пропорционально оставшемуся грузу шахты (будущие покупки). Покупается лучший вариант, а если на него не хватает —
        /// доступный с оценкой не ниже половины лучшего; иначе игрок копит.
        /// </summary>
        UpgradeBranch? BottleneckPick(OutpostSimulation sim, BalanceProbe probe)
        {
            var shares = probe != null ? probe.RecentShares() : null;
            double mine = Share(shares, WorkerMode.Mining);
            double walk = Share(shares, WorkerMode.MovingToWork) + Share(shares, WorkerMode.MovingToCargo) + Share(shares, WorkerMode.Returning);
            double depot = Share(shares, WorkerMode.WaitingDepot) + Share(shares, WorkerMode.Unloading);
            double idle = Share(shares, WorkerMode.Idle) + Share(shares, WorkerMode.GoingHome);
            if (shares == null)
            {
                mine = 0.5;
                walk = 0.4;
                depot = 0.1;
                idle = 0;
            }
            var run = sim.Run;
            var stats = sim.Stats;
            double remaining = RemainingCargoFraction(sim);
            double bestScore = 0, bestAffordableScore = 0;
            UpgradeBranch? best = null, bestAffordable = null;
            foreach (var br in s_All)
            {
                var q = sim.Quote(br, BuyMode.One);
                if (q.NextCost == null || q.BlockReason == "cap" || q.BlockReason == "phase" || q.BlockReason == "mode") continue;
                var next = NextStats(sim, br);
                double gain;
                switch (br)
                {
                    case UpgradeBranch.Team:
                        gain = idle >= 0.3 ? 0 : (1 - idle / 0.3) / Math.Max(1, stats.Workers);
                        if (run.Phase != RunPhase.Running) gain *= 0.25;
                        break;
                    case UpgradeBranch.Pickaxes:
                        gain = mine * HitGain(sim, stats.DamageUnits, next.DamageUnits);
                        break;
                    case UpgradeBranch.Boots:
                        gain = walk * (1 - stats.FinalMoveSpeed.ToDouble() / next.FinalMoveSpeed.ToDouble());
                        break;
                    case UpgradeBranch.Bags:
                        gain = walk * 0.7 * (1 - (double)stats.Capacity / next.Capacity);
                        break;
                    case UpgradeBranch.Unload:
                        gain = depot * (1 - stats.UnloadRate.ToDouble() / next.UnloadRate.ToDouble());
                        break;
                    default:
                        gain = run.Phase != RunPhase.Running ? 0 : 0.5 * remaining * remaining * (next.FinalValue.ToDouble() / stats.FinalValue.ToDouble() - 1);
                        break;
                }
                if (gain <= 0) continue;
                double score = gain / Math.Max(1.0, (double)q.NextCost.Value);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = br;
                }
                if (q.CanBuy && score > bestAffordableScore)
                {
                    bestAffordableScore = score;
                    bestAffordable = br;
                }
            }
            if (best == null) return null;
            if (sim.Quote(best.Value, BuyMode.One).CanBuy) return best;
            return bestAffordable != null && bestAffordableScore >= 0.5 * bestScore ? bestAffordable : null;
        }

        static double Share(Dictionary<WorkerMode, double> shares, WorkerMode mode) =>
            shares != null && shares.TryGetValue(mode, out double v) ? v : 0;

        static TeamStats NextStats(OutpostSimulation sim, UpgradeBranch br)
        {
            var run = sim.Run;
            var levels = (int[])run.Levels.Clone();
            levels[(int)br]++;
            return TeamStats.Compute(run.Balance, levels, sim.Profile, sim.Configs.Equipment, run.Zone, sim.Configs.Cards, run.ExtraWorkers);
        }

        /// <summary>Доля ударов, которую сэкономит новый урон на живых целях текущего сектора (и его воротах).</summary>
        static double HitGain(OutpostSimulation sim, long damage, long nextDamage)
        {
            if (nextDamage <= damage || damage <= 0) return 0;
            var run = sim.Run;
            var map = sim.Map;
            long hits = 0, hitsNext = 0;
            for (int i = 0; i < map.Targets.Length; i++)
            {
                var mt = map.Targets[i];
                var ts = run.Targets[i];
                if (ts.Destroyed || mt.Sector != run.GatesOpened) continue;
                long rem = ts.RemainingHpUnits;
                hits += (rem + damage - 1) / damage;
                hitsNext += (rem + nextDamage - 1) / nextDamage;
            }
            return hits <= 0 ? 0 : 1 - (double)hitsNext / hits;
        }

        static double RemainingCargoFraction(OutpostSimulation sim)
        {
            long total = 0, left = 0;
            foreach (var ts in sim.Run.Targets)
            {
                total += ts.TotalUnits;
                left += ts.TotalUnits - ts.ReleasedUnits;
            }
            return total <= 0 ? 0 : (double)left / total;
        }

        // ── Удары киркой ──

        int m_StrikeTarget = -1;

        /// <summary>Следующая отметка удара после t (с учётом серий и окна активности) или long.MaxValue.</summary>
        public long NextTapTick(long t, long ticksPerSecond)
        {
            if (TapsPerSecond <= 0) return long.MaxValue;
            long period = Math.Max(1, (long)Math.Round(ticksPerSecond / TapsPerSecond));
            long next = t + period;
            if (TapRestSec > 0)
            {
                long burst = (long)(TapBurstSec * ticksPerSecond), cycle = burst + (long)(TapRestSec * ticksPerSecond);
                long phase = next % cycle;
                if (phase >= burst) next += cycle - phase;
            }
            if (TapActiveSec > 0 && next > (long)(TapActiveSec * ticksPerSecond)) return long.MaxValue;
            return next;
        }

        /// <summary>
        /// Удар по цели, которую уже бьют котики (больше всего назначенных), иначе по ближайшей к базе доступной.
        /// Игрок держит выбранную цель, пока она жива и доступна. Возвращает урон в долях HP или 0.
        /// </summary>
        public long Strike(OutpostSimulation sim)
        {
            if (sim.Run.Phase != RunPhase.Running) return 0;
            if (m_StrikeTarget < 0 || !sim.CanStrike(m_StrikeTarget)) m_StrikeTarget = PickStrikeTarget(sim);
            if (m_StrikeTarget < 0) return 0;
            var r = sim.Submit(OutpostCommand.Strike(m_StrikeTarget));
            if (!r.Ok)
            {
                m_StrikeTarget = -1;
                return 0;
            }
            return (long)r.Cost;
        }

        static int PickStrikeTarget(OutpostSimulation sim)
        {
            var map = sim.Map;
            var run = sim.Run;
            var field = sim.Navigation.From(map.IntakeCell);
            int best = -1, bestAssigned = -1, bestDist = int.MaxValue;
            for (int i = 0; i < map.Targets.Length; i++)
            {
                var mt = map.Targets[i];
                if (run.Targets[i].Destroyed || !sim.CanStrike(mt.Id)) continue;
                int assigned = sim.AssignedTo(mt.Id);
                int dist = int.MaxValue;
                foreach (int wc in mt.WorkCells)
                {
                    int d = field.Dist[wc];
                    if (d >= 0 && d < dist) dist = d;
                }
                if (assigned > bestAssigned || (assigned == bestAssigned && dist < bestDist))
                {
                    best = mt.Id;
                    bestAssigned = assigned;
                    bestDist = dist;
                }
            }
            return best;
        }

        // ── Между шахтами ──

        /// <summary>
        /// Действия на экране итогов (ReadyToContinue): карточки, детали, «Надеть лучшее». Меняет только постоянный профиль,
        /// поэтому следующая шахта создаётся уже с новыми бонусами (в том числе «Плакат найма»).
        /// </summary>
        public void BetweenMines(OutpostProfile profile, OutpostConfigSet configs)
        {
            bool meta = MetaEveryMines <= 1 || profile.CurrentZone % MetaEveryMines == 0;
            if (UpgradeCards && meta && configs.Cards != null)
            {
                for (int guard = 0; guard < 10000; guard++)
                {
                    CardDef best = null;
                    long bestCost = long.MaxValue;
                    foreach (var def in configs.Cards.Cards)
                    {
                        var q = CardService.Quote(profile, def);
                        if (q.CanUpgrade && q.ElixirNeeded < bestCost)
                        {
                            best = def;
                            bestCost = q.ElixirNeeded;
                        }
                    }
                    if (best == null || CardService.Upgrade(profile, best, out _) != null) break;
                }
            }
            if (EquipBest) EquipBestAllowed(profile, configs);
            if (SpendParts && meta)
            {
                for (int guard = 0; guard < 10000; guard++)
                {
                    string pick = null;
                    long pickCost = long.MaxValue;
                    foreach (var id in profile.Equipped)
                    {
                        if (id == null) continue;
                        var cost = EquipmentService.UpgradeCost(profile, configs.Equipment, id);
                        if (cost == null || cost.Value > profile.Parts) continue;
                        if (cost.Value < pickCost)
                        {
                            pick = id;
                            pickCost = cost.Value;
                        }
                    }
                    if (pick == null || EquipmentService.UpgradeItem(profile, configs.Equipment, pick, out _) != null) break;
                }
                if (EquipBest) EquipBestAllowed(profile, configs);
            }
        }

        void EquipBestAllowed(OutpostProfile profile, OutpostConfigSet configs)
        {
            EquipmentService.EquipBest(profile, configs.Equipment);
            if (SkipSlot != null) EquipmentService.Unequip(profile, SkipSlot.Value);
        }

        public void ResetStrikeTarget() => m_StrikeTarget = -1;
    }
}
