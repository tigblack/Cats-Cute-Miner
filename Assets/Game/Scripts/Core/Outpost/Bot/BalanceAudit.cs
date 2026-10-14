using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Data;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Наблюдатель прогона для аудита баланса. Состояние ядра меняется только на отметках событий, поэтому интервал
    /// между соседними отметками целиком принадлежит режимам работников после предыдущей отметки: учёт времени точный.
    /// </summary>
    public sealed class BalanceProbe
    {
        const int ModeCount = 10;
        const double EmaTauSec = 30;

        readonly OutpostSimulation m_Sim;
        readonly MineAudit m_Report;
        readonly double m_Tps;
        readonly double m_BaseCoin;
        readonly int[] m_Modes = new int[ModeCount];
        readonly double[] m_ModeTicks = new double[ModeCount];
        readonly double[] m_Ema = new double[ModeCount];
        bool m_EmaReady;
        long m_Last;
        double m_RunningWorkerTicks, m_RunningBusyTicks, m_RunningTicks;

        // Секторы и цели.
        readonly long[] m_FirstDamage;
        readonly long[] m_Destroyed;
        int m_Sector;
        int m_ClearedSeen = -1;
        readonly int[] m_SectorLiving;

        // Покупки и доход.
        readonly List<(long Tick, BigInteger Credited)> m_Income = new List<(long, BigInteger)>();
        long m_LastPurchaseTick = -1;
        readonly List<double> m_Gaps = new List<double>();
        BigInteger? m_MinNextCost;
        string m_MinKey;
        bool m_Affordable = true;
        long m_UnaffordableSince = -1;
        double m_Unaffordable, m_MaxUnaffordable;
        long m_StrikeUnits;
        long m_TotalHpUnits;
        readonly HashSet<int> m_TraceZones;
        long m_NextTrace;

        public BalanceProbe(OutpostSimulation sim, MineAudit report, bool trace)
        {
            m_Sim = sim;
            m_Report = report;
            var run = sim.Run;
            m_Tps = run.Balance.TicksPerSecond;
            m_BaseCoin = (run.Balance.CoinBase * run.Balance.ZoneMoneyScale(run.Zone)).ToDouble();
            var map = sim.Map;
            m_FirstDamage = new long[map.Targets.Length];
            m_Destroyed = new long[map.Targets.Length];
            for (int i = 0; i < m_FirstDamage.Length; i++)
            {
                m_FirstDamage[i] = run.Targets[i].RemainingHpUnits < run.Targets[i].MaxHpUnits ? run.Now : -1;
                m_Destroyed[i] = run.Targets[i].Destroyed ? run.Now : -1;
                m_TotalHpUnits += run.Targets[i].MaxHpUnits;
            }
            m_SectorLiving = new int[map.Gates.Length + 1];
            report.Targets = map.Targets.Count(t => !t.IsGate);
            report.Gates = map.Gates.Length;
            report.CargoUnits = run.Targets.Sum(t => t.TotalUnits);
            report.TotalHp = m_TotalHpUnits / OutpostIds.HpUnitsPerHp;
            report.RawYieldBase = run.Targets.Sum(t => t.TotalUnits * t.ValueFactor.ToDouble());
            MeasureDistances(map, run, report);
            for (int s = 0; s <= map.Gates.Length; s++)
            {
                var sa = new SectorAudit { Sector = s };
                for (int i = 0; i < map.Targets.Length; i++)
                {
                    var mt = map.Targets[i];
                    if (mt.IsGate || mt.Sector != s) continue;
                    sa.Targets++;
                    sa.Hp += run.Targets[i].MaxHpUnits / OutpostIds.HpUnitsPerHp;
                    sa.CargoUnits += run.Targets[i].TotalUnits;
                }
                if (s < map.Gates.Length)
                {
                    int gi = map.TargetIndex(map.Gates[s].TargetId);
                    sa.GateHp = run.Targets[gi].MaxHpUnits / OutpostIds.HpUnitsPerHp;
                    sa.GateWorkCells = Math.Min(run.Balance.MaxWorkersPerTarget, map.Targets[gi].WorkCells.Length);
                }
                report.Sectors.Add(sa);
            }
            m_Sector = run.GatesOpened;
            if (m_Sector < report.Sectors.Count) report.Sectors[m_Sector].OpenSec = Sec(run.Now);
            if (trace) report.Trace = new List<TraceRow>();
            m_Last = run.Now;
            m_Income.Add((run.Now, run.TotalCredited));
            Snapshot();
        }

        double Sec(long ticks) => ticks / m_Tps;

        /// <summary>BFS от точки приёма по всем клеткам пола (карта после расчистки): путь до ближайшей рабочей клетки каждой цели.</summary>
        static void MeasureDistances(OutpostMap map, OutpostRun run, MineAudit report)
        {
            var dist = new int[map.CellCount];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            var queue = new Queue<int>();
            dist[map.IntakeCell] = 0;
            queue.Enqueue(map.IntakeCell);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int col = map.Col(c), row = map.Row(c);
                foreach (int n in new[] { col > 0 ? c - 1 : -1, col < map.Width - 1 ? c + 1 : -1, c - map.Width, c + map.Width })
                {
                    if (n < 0 || n >= map.CellCount || dist[n] >= 0 || !map.IsPlayable(n)) continue;
                    dist[n] = dist[c] + 1;
                    queue.Enqueue(n);
                }
            }
            double weighted = 0, units = 0;
            int max = 0;
            for (int i = 0; i < map.Targets.Length; i++)
            {
                int best = int.MaxValue;
                foreach (int wc in map.Targets[i].WorkCells)
                    if (dist[wc] >= 0 && dist[wc] < best) best = dist[wc];
                if (best == int.MaxValue) continue;
                weighted += (double)best * run.Targets[i].TotalUnits;
                units += run.Targets[i].TotalUnits;
                if (best > max) max = best;
            }
            report.CargoWeightedDistance = units > 0 ? weighted / units : 0;
            report.MaxDistance = max;
        }
        double Base(BigInteger v) => (double)v / m_BaseCoin;

        /// <summary>Учесть интервал [последняя отметка, t) по режимам предыдущего снимка. Вызывать до Advance(t).</summary>
        public void AdvanceTo(long t)
        {
            long dt = t - m_Last;
            if (dt <= 0) return;
            var run = m_Sim.Run;
            int workers = 0, busy = 0;
            for (int m = 0; m < ModeCount; m++)
            {
                m_ModeTicks[m] += (double)m_Modes[m] * dt;
                workers += m_Modes[m];
                if (m != (int)WorkerMode.Idle && m != (int)WorkerMode.GoingHome && m != (int)WorkerMode.WaitingDepot &&
                    m != (int)WorkerMode.ChoosingTask) busy += m_Modes[m];
            }
            if (run.Phase == RunPhase.Running)
            {
                m_RunningTicks += dt;
                m_RunningWorkerTicks += (double)workers * dt;
                m_RunningBusyTicks += (double)busy * dt;
                if (!m_Affordable)
                {
                    m_Unaffordable += Sec(dt);
                }
            }
            double a = Math.Exp(-Sec(dt) / EmaTauSec);
            for (int m = 0; m < ModeCount; m++)
                m_Ema[m] = m_EmaReady ? m_Ema[m] * a + m_Modes[m] * (1 - a) : m_Modes[m];
            m_EmaReady = true;
            if (m_Report.Trace != null)
            {
                while (m_NextTrace < t)
                {
                    m_Report.Trace.Add(new TraceRow
                    {
                        Sec = Sec(m_NextTrace),
                        WalletBase = Base(run.Wallet),
                        CreditedBase = Base(run.TotalCredited),
                        Workers = run.Workers.Count,
                        Sector = run.GatesOpened,
                        LevelSum = run.Levels.Sum(),
                        Modes = (int[])m_Modes.Clone(),
                    });
                    m_NextTrace += (long)(5 * m_Tps);
                }
            }
            m_Last = t;
        }

        /// <summary>Снимок после обработки отметки и команд: режимы, секторы, цели, доступность покупки, доход.</summary>
        public void Snapshot()
        {
            var sim = m_Sim;
            var run = sim.Run;
            Array.Clear(m_Modes, 0, ModeCount);
            foreach (var w in run.Workers) m_Modes[(int)w.Mode]++;
            long now = run.Now;
            var map = sim.Map;

            // Цели текущего сектора: первый урон и разрушение.
            for (int i = 0; i < map.Targets.Length; i++)
            {
                if (m_Destroyed[i] >= 0) continue;
                var ts = run.Targets[i];
                if (m_FirstDamage[i] < 0 && ts.RemainingHpUnits < ts.MaxHpUnits) m_FirstDamage[i] = now;
                if (ts.Destroyed)
                {
                    m_Destroyed[i] = now;
                    if (m_FirstDamage[i] < 0) m_FirstDamage[i] = now;
                }
            }
            if (run.Counters.ClearedTargets != m_ClearedSeen)
            {
                m_ClearedSeen = run.Counters.ClearedTargets;
                Array.Clear(m_SectorLiving, 0, m_SectorLiving.Length);
                for (int i = 0; i < map.Targets.Length; i++)
                    if (!map.Targets[i].IsGate && !run.Targets[i].Destroyed && map.Targets[i].Sector < m_SectorLiving.Length)
                        m_SectorLiving[map.Targets[i].Sector]++;
                for (int s = 0; s < m_SectorLiving.Length; s++)
                {
                    var sa = m_Report.Sectors[s];
                    if (sa.LastTargetSec == null && m_SectorLiving[s] == 1 && run.GatesOpened == s)
                    {
                        sa.LastTargetSec = Sec(now);
                        for (int i = 0; i < map.Targets.Length; i++)
                            if (!map.Targets[i].IsGate && !run.Targets[i].Destroyed && map.Targets[i].Sector == s)
                                sa.LastTargetHp = run.Targets[i].MaxHpUnits / OutpostIds.HpUnitsPerHp;
                    }
                    if (sa.RsxClearSec == null && m_SectorLiving[s] == 0 && run.GatesOpened >= s) sa.RsxClearSec = Sec(now);
                }
            }
            while (m_Sector < run.GatesOpened)
            {
                m_Report.Sectors[m_Sector].GateSec = Sec(now);
                m_Sector++;
                if (m_Sector < m_Report.Sectors.Count)
                {
                    m_Report.Sectors[m_Sector].OpenSec = Sec(now);
                    if (m_SectorLiving[m_Sector] == 0 && m_Report.Sectors[m_Sector].RsxClearSec == null)
                        m_Report.Sectors[m_Sector].RsxClearSec = Sec(now);
                }
            }

            if (m_Income[m_Income.Count - 1].Credited != run.TotalCredited) m_Income.Add((now, run.TotalCredited));

            // Доступность самого дешёвого уровня; цены зависят только от уровней и скидок.
            string key = string.Join(",", run.Levels) + "|" + sim.Stats.CardBranchDiscount + "|" + sim.Stats.CardHireDiscount;
            if (key != m_MinKey)
            {
                m_MinKey = key;
                m_MinNextCost = null;
                foreach (UpgradeBranch br in Enum.GetValues(typeof(UpgradeBranch)))
                {
                    var q = sim.Quote(br, BuyMode.One);
                    if (q.NextCost == null || q.BlockReason == "cap" || q.BlockReason == "mode") continue;
                    if (m_MinNextCost == null || q.NextCost.Value < m_MinNextCost.Value) m_MinNextCost = q.NextCost.Value;
                }
            }
            bool affordable = m_MinNextCost == null || run.Wallet >= m_MinNextCost.Value || run.Phase != RunPhase.Running;
            if (!affordable && m_Affordable) m_UnaffordableSince = now;
            if (affordable && !m_Affordable && m_UnaffordableSince >= 0)
                m_MaxUnaffordable = Math.Max(m_MaxUnaffordable, Sec(now - m_UnaffordableSince));
            m_Affordable = affordable;
        }

        /// <summary>Сглаженные (τ = 30 с) доли котиков по режимам; null до первого интервала.</summary>
        public Dictionary<WorkerMode, double> RecentShares()
        {
            if (!m_EmaReady) return null;
            double sum = m_Ema.Sum();
            if (sum <= 0) return null;
            var d = new Dictionary<WorkerMode, double>();
            for (int m = 0; m < ModeCount; m++) d[(WorkerMode)m] = m_Ema[m] / sum;
            return d;
        }

        public void OnPurchase(OutpostSimulation sim, UpgradeBranch branch, BigInteger cost)
        {
            var run = sim.Run;
            long now = run.Now;
            // Доход последних 60 с.
            long from = now - (long)(60 * m_Tps);
            BigInteger startCredited = m_Income[0].Credited;
            long startTick = m_Income[0].Tick;
            for (int i = m_Income.Count - 1; i >= 0; i--)
                if (m_Income[i].Tick <= from)
                {
                    startCredited = m_Income[i].Credited;
                    startTick = from;
                    break;
                }
            double window = Sec(now - Math.Max(startTick, m_Income[0].Tick));
            double rate = window > 0 ? Base(run.TotalCredited - startCredited) / window : 0;
            var rec = new PurchaseRecord
            {
                Sec = Sec(now),
                Branch = branch.ToString(),
                Level = run.Levels[(int)branch],
                CostBase = Base(cost),
                CostIncomeSec = rate > 0 ? Base(cost) / rate : (double?)null,
            };
            m_Report.PurchaseLog.Add(rec);
            if (branch == UpgradeBranch.Team && m_Report.FirstHireSec == null) m_Report.FirstHireSec = Sec(now);
            if (run.Phase == RunPhase.Running)
            {
                if (m_LastPurchaseTick >= 0) m_Gaps.Add(Sec(now - m_LastPurchaseTick));
                m_LastPurchaseTick = now;
            }
        }

        public void OnStrike(long damageUnits) => m_StrikeUnits += damageUnits;

        /// <summary>Заполнить итог шахты.</summary>
        public void Finish()
        {
            var sim = m_Sim;
            var run = sim.Run;
            var r = m_Report;
            var c = run.Counters;
            r.CompletedSec = c.CompletedTick >= 0 ? Sec(c.CompletedTick) : (double?)null;
            r.ClearedSec = c.ClearedTick >= 0 ? Sec(c.ClearedTick) : (double?)null;
            r.FirstDeliverySec = c.FirstDeliveryTick >= 0 ? Sec(c.FirstDeliveryTick) : (double?)null;
            r.FirstPurchaseSec = c.FirstPurchaseTick >= 0 ? Sec(c.FirstPurchaseTick) : (double?)null;
            r.DrainSec = r.CompletedSec != null && r.ClearedSec != null ? r.CompletedSec.Value - r.ClearedSec.Value : 0;
            r.Purchases = r.PurchaseLog.Count;
            r.Levels = (int[])run.Levels.Clone();
            r.WorkersEnd = run.Workers.Count;
            if (!m_Affordable && m_UnaffordableSince >= 0) m_MaxUnaffordable = Math.Max(m_MaxUnaffordable, Sec(run.Now - m_UnaffordableSince));
            r.MaxUnaffordableSec = m_MaxUnaffordable;
            r.UnaffordableSec = m_Unaffordable;
            if (m_Gaps.Count > 0)
            {
                var sorted = m_Gaps.OrderBy(x => x).ToList();
                r.MaxPurchaseGapSec = sorted[sorted.Count - 1];
                r.MedianPurchaseGapSec = sorted[sorted.Count / 2];
            }
            var ci = r.PurchaseLog.Where(p => p.CostIncomeSec != null).Select(p => p.CostIncomeSec.Value).OrderBy(x => x).ToList();
            if (ci.Count > 0)
            {
                r.MedianCostIncomeSec = ci[ci.Count / 2];
                r.MaxCostIncomeSec = ci[ci.Count - 1];
            }
            double total = 0;
            for (int m = 0; m < ModeCount; m++)
            {
                double s = m_ModeTicks[m] / m_Tps;
                r.WorkerSec[((WorkerMode)m).ToString()] = s;
                total += s;
            }
            r.WorkerSecTotal = total;
            r.AvgWorkers = m_RunningTicks > 0 ? m_RunningWorkerTicks / m_RunningTicks : 0;
            r.AvgBusyWorkers = m_RunningTicks > 0 ? m_RunningBusyTicks / m_RunningTicks : 0;
            double gateWait = 0;
            foreach (var sa in r.Sectors)
                if (sa.RsxClearSec != null && sa.GateSec != null) gateWait += sa.GateSec.Value - sa.RsxClearSec.Value;
            r.GateWaitSec = gateWait;
            r.SectorTailSec = r.Sectors.Where(sa => sa.LastTargetSec != null && sa.RsxClearSec != null)
                .Sum(sa => sa.RsxClearSec.Value - sa.LastTargetSec.Value);
            var map = sim.Map;
            var slow = new List<TargetAudit>();
            for (int i = 0; i < map.Targets.Length; i++)
            {
                if (m_Destroyed[i] < 0 || m_FirstDamage[i] < 0) continue;
                slow.Add(new TargetAudit
                {
                    Id = map.Targets[i].Id,
                    Kind = map.Targets[i].Kind,
                    Sector = map.Targets[i].Sector,
                    Hp = run.Targets[i].MaxHpUnits / OutpostIds.HpUnitsPerHp,
                    FirstDamageSec = Sec(m_FirstDamage[i]),
                    DestroyedSec = Sec(m_Destroyed[i]),
                    DurationSec = Sec(m_Destroyed[i] - m_FirstDamage[i]),
                });
            }
            r.SlowTargets = slow.OrderByDescending(x => x.DurationSec).ThenBy(x => x.Id).Take(5).ToList();
            r.LongestTargetSec = r.SlowTargets.Count > 0 ? r.SlowTargets[0].DurationSec : 0;
            r.CreditedBase = Base(run.TotalCredited);
            r.SpentBase = Base(run.TotalSpent);
            r.UnspentBase = Base(run.Wallet);
            r.IncomeBasePerMin = r.CompletedSec > 0 ? r.CreditedBase / (r.CompletedSec.Value / 60) : 0;
            r.StrikeDamageShare = m_TotalHpUnits > 0 ? (double)m_StrikeUnits / m_TotalHpUnits : 0;
            r.MaxQueue = c.MaxQueue;
            r.AvgQueueWaitSec = c.QueueServed > 0 ? Sec(c.QueueWaitTicks) / c.QueueServed : 0;
            r.ChestsOpened = c.ChestsOpened;
            var p = sim.Profile;
            r.CardsOwned = p.Cards?.Count(kv => kv.Value != null && kv.Value.Level >= 1) ?? 0;
            r.CardLevels = p.Cards?.Sum(kv => kv.Value?.Level ?? 0) ?? 0;
            r.Elixir = p.Elixir;
            r.Equipped = (string[])p.Equipped.Clone();
            r.EquippedLevels = p.Equipped.Select(id => p.ItemLevel(id)).ToArray();
            r.Parts = p.Parts;
            var reward = StageProgressionService.CompletionReward(sim.State);
            r.RewardItemId = reward?.ItemId;
        }
    }

    /// <summary>
    /// Прогоны аудита баланса тем же ядром, что работает в игре (Advance, Submit, переход между шахтами).
    /// Ускорение — только скорость исполнения: модель игрока действует на отметках логического времени.
    /// </summary>
    public static class BalanceAudit
    {
        public const long TickLimit = 86_400_000_000L;
        public const long EventLimit = 4_000_000L;

        /// <summary>Пройти текущую шахту моделью игрока до ReadyToContinue; отчёт по шахте.</summary>
        public static MineAudit RunMine(OutpostSimulation sim, PlayerModel model, string scenario, bool trace = false)
        {
            sim.RecordEvents = false;
            var run = sim.Run;
            var report = new MineAudit { Zone = run.Zone, Scenario = scenario, Seed = sim.Profile.Seed, WorkersStart = run.Workers.Count };
            var st = sim.Stats;
            report.Start = new StartStats
            {
                Workers = st.Workers,
                Damage = st.FinalDamage.ToDouble(),
                MoveSpeed = st.FinalMoveSpeed.ToDouble(),
                Capacity = st.Capacity,
                UnloadRate = st.UnloadRate.ToDouble(),
                Value = st.FinalValue.ToDouble(),
                TeamDiscount = st.CardHireDiscount.ToDouble(),
                BranchDiscount = st.CardBranchDiscount.ToDouble(),
                TeamCapBonus = st.CardTeamCap,
                CritChance = st.CritChance.ToDouble(),
            };
            var probe = new BalanceProbe(sim, report, trace);
            model.BeginMine();
            model.ResetStrikeTarget();
            long tps = run.Balance.TicksPerSecond;
            long checkEvery = (long)(model.CheckIntervalSec * tps);
            long nextCheck = checkEvery > 0 ? (long)(model.FirstCheckSec * tps) : 0;
            long nextTap = model.NextTapTick(run.Now, tps);
            long events0 = sim.ProcessedEvents;
            try
            {
                if (checkEvery == 0 || nextCheck <= run.Now)
                {
                    model.Buy(sim, probe);
                    if (checkEvery > 0) nextCheck = run.Now + checkEvery;
                    probe.Snapshot();
                }
                while (!sim.IsEconomyStopped)
                {
                    if (run.WatchdogFired)
                    {
                        report.Outcome = "Watchdog";
                        report.Error = run.WatchdogDiagnostic;
                        break;
                    }
                    long tEvent = sim.NextEventTick();
                    long t = tEvent;
                    if (checkEvery > 0 && nextCheck < t) t = nextCheck;
                    if (nextTap < t) t = nextTap;
                    if (t == long.MaxValue)
                    {
                        sim.AdvanceToNextEvent();
                        report.Outcome = "Watchdog";
                        report.Error = run.WatchdogDiagnostic;
                        break;
                    }
                    if (t > TickLimit)
                    {
                        report.Outcome = "Timeout";
                        break;
                    }
                    if (sim.ProcessedEvents - events0 > EventLimit)
                    {
                        report.Outcome = "EventLimit";
                        break;
                    }
                    probe.AdvanceTo(t);
                    sim.Advance(t);
                    if (t == nextTap)
                    {
                        long dmg = model.Strike(sim);
                        if (dmg > 0)
                        {
                            report.Strikes++;
                            probe.OnStrike(dmg);
                        }
                        nextTap = model.NextTapTick(t, tps);
                    }
                    if (!sim.IsEconomyStopped && (checkEvery == 0 || t >= nextCheck))
                    {
                        model.Buy(sim, probe);
                        if (checkEvery > 0) nextCheck = t + checkEvery;
                    }
                    probe.Snapshot();
                }
                if (sim.IsEconomyStopped) report.Outcome = "Completed";
            }
            catch (Exception e)
            {
                report.Outcome = "Error";
                report.Error = e.GetType().Name + ": " + e.Message;
            }
            probe.Finish();
            return report;
        }

        /// <summary>
        /// Последовательная кампания 1..toZone: награды, карточки, детали и экипировка переносятся законным путём;
        /// между шахтами модель игрока выполняет действия экрана итогов. Останавливается на первой неуспешной шахте.
        /// </summary>
        public static CampaignAudit RunCampaign(BalanceRepository repo, OutpostConfigSet configs, PlayerModel model, ulong seed,
            int toZone = OutpostIds.LastZone, ICollection<int> traceZones = null, Action<int, OutpostGameState> beforeZone = null)
        {
            var audit = new CampaignAudit
            {
                Scenario = model.Name,
                Seed = seed,
                Model = model.Clone(),
                BalanceVersion = configs.Balance.Version,
                BalanceHash = configs.BalanceHash,
                CatalogHash = configs.CatalogHash,
                TopologyVersion = configs.Topologies?.Version,
                Success = true,
            };
            var state = StageProgressionService.NewGame(repo, configs, seed, 0, 1);
            var map = OutpostLevelAdapter.BuildMine(repo, 1, configs);
            while (true)
            {
                beforeZone?.Invoke(state.Run.Zone, state);
                var sim = new OutpostSimulation(state, map, configs);
                if (model.EquipBest) sim.Submit(OutpostCommand.EquipBest());
                if (model.SkipSlot != null) sim.Submit(OutpostCommand.Unequip(model.SkipSlot.Value));
                var report = RunMine(sim, model, model.Name, traceZones != null && traceZones.Contains(state.Run.Zone));
                audit.Mines.Add(report);
                if (report.Outcome != "Completed")
                {
                    audit.Success = false;
                    break;
                }
                audit.TotalSec += report.CompletedSec ?? 0;
                if (state.Run.Zone >= toZone) break;
                model.BetweenMines(state.Profile, configs);
                var tr = StageProgressionService.PrepareTransition(state, repo, configs, 0);
                if (!tr.Ok)
                {
                    audit.Success = false;
                    report.Error = "transition: " + tr.Reason;
                    break;
                }
                state = tr.State;
                map = tr.Map;
            }
            return audit;
        }

        /// <summary>
        /// Насыщение команды: шахта zone с профилем profile (копия), фиксированными уровнями fixedLevels (кроме «Команды»)
        /// и каждым числом покупок «Команды» из teamLevels; без покупок во время прогона. Показывает, растёт ли выработка
        /// от новых котиков или упирается в рабочие места, ворота и склад.
        /// </summary>
        public static SaturationAudit RunSaturation(BalanceRepository repo, OutpostConfigSet configs, OutpostProfile profile, int zone,
            int[] fixedLevels, IEnumerable<int> teamLevels, string profileSource)
        {
            var audit = new SaturationAudit { Zone = zone, ProfileSource = profileSource, FixedLevels = (int[])fixedLevels.Clone() };
            foreach (int team in teamLevels)
            {
                var p = OutpostJson.DeepCopy(profile);
                var map = OutpostLevelAdapter.BuildMine(repo, zone, configs);
                var state = new OutpostGameState { Profile = p, Run = StageProgressionService.CreateRun(p, zone, map, configs, repo, 0) };
                var sim = new OutpostSimulation(state, map, configs);
                sim.Submit(OutpostCommand.EquipBest());
                var run = sim.Run;
                run.Wallet = BigInteger.Pow(10, 400);
                for (int b = 0; b < OutpostIds.BranchCount; b++)
                {
                    int want = b == (int)UpgradeBranch.Team ? team : fixedLevels[b];
                    for (int k = run.Levels[b]; k < want; k++)
                        if (!sim.Submit(OutpostCommand.BuyUpgrade((UpgradeBranch)b)).Ok) break;
                }
                run.Wallet = BigInteger.Zero;
                run.TotalSpent = BigInteger.Zero;
                var model = new PlayerModel { Name = "saturation", Strategy = PurchaseStrategy.None, EquipBest = false, SpendParts = false, UpgradeCards = false };
                var rep = RunMine(sim, model, "saturation");
                var row = new SaturationRow { Workers = run.Workers.Count, ClearedSec = rep.ClearedSec, CompletedSec = rep.CompletedSec, AvgBusyWorkers = rep.AvgBusyWorkers };
                foreach (var kv in rep.WorkerSec) row.Share[kv.Key] = rep.WorkerSecTotal > 0 ? kv.Value / rep.WorkerSecTotal : 0;
                audit.Rows.Add(row);
                if (team > 0 && run.Levels[(int)UpgradeBranch.Team] < team) break;
            }
            return audit;
        }

        /// <summary>Стандартные модели игрока аудита (Docs/Rework/BALANCE_AUDIT.md, «Допущения»).</summary>
        public static PlayerModel Model(string name)
        {
            switch (name)
            {
                case "free":
                    // Новый игрок без покупок за деньги: заглядывает в «Базу» раз в 30 с, разумно выбирает узкое место,
                    // между шахтами улучшает карточки, тратит детали и надевает лучшее. Киркой не бьёт.
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.Bottleneck, CheckIntervalSec = 30, FirstCheckSec = 15 };
                case "free-cheapest":
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.Cheapest, CheckIntervalSec = 30, FirstCheckSec = 15 };
                case "free-roundrobin":
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.RoundRobin, CheckIntervalSec = 30, FirstCheckSec = 15 };
                case "free-damage":
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.DamageFocus, CheckIntervalSec = 30, FirstCheckSec = 15 };
                case "idle":
                    // Idle: котики работают сами, игрок возвращается раз в 5 минут и покупает самое дешёвое, пока хватает.
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.Cheapest, CheckIntervalSec = 300, FirstCheckSec = 60 };
                case "active":
                    // Активный: реагирует за 5 с, выбирает узкое место, бьёт киркой 2 раза/с (интерфейс пропускает не чаще
                    // раза в 0,4 с) сериями 30 с через 30 с отдыха — в среднем удар в секунду.
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.Bottleneck, CheckIntervalSec = 5, FirstCheckSec = 3,
                        TapsPerSecond = 2, TapBurstSec = 30, TapRestSec = 30 };
                case "eager-rr":
                    // Прежний бот C: ×1 по кругу после каждого события; карточки не улучшает, детали не тратит.
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.RoundRobin, CheckIntervalSec = 0,
                        SpendParts = false, UpgradeCards = false };
                case "free-noequip":
                    // Абляция: тот же free, но экипировка не надевается и детали не тратятся.
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.Bottleneck, CheckIntervalSec = 30, FirstCheckSec = 15,
                        EquipBest = false, SpendParts = false };
                case "free-nocardup":
                    // Абляция: карточки выпадают, но игрок их не улучшает.
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.Bottleneck, CheckIntervalSec = 30, FirstCheckSec = 15,
                        UpgradeCards = false };
                case "free-lazy":
                    // Игрок, который заходит в «Карты» и «Экипировку» раз в 5 шахт; «Надеть лучшее» — всегда.
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.Bottleneck, CheckIntervalSec = 30, FirstCheckSec = 15,
                        MetaEveryMines = 5 };
                case "free-nometa":
                    // Игрок, который надевает лучшее, но не улучшает ни предметы, ни карточки.
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.Bottleneck, CheckIntervalSec = 30, FirstCheckSec = 15,
                        SpendParts = false, UpgradeCards = false };
                case "free-no-pickaxe":
                case "free-no-hat":
                case "free-no-backpack":
                case "free-no-boots":
                {
                    var m = Model("free");
                    m.Name = name;
                    m.SkipSlot = name == "free-no-pickaxe" ? EquipSlot.Pickaxe : name == "free-no-hat" ? EquipSlot.Hat :
                        name == "free-no-backpack" ? EquipSlot.Backpack : EquipSlot.Boots;
                    return m;
                }
                case "none":
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.None, SpendParts = false, UpgradeCards = false };
                default:
                    throw new ArgumentException("Неизвестная модель игрока " + name);
            }
        }
    }
}
