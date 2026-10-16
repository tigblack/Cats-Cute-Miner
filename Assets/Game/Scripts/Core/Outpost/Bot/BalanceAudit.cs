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

        // Покупки и доход: монеты + продажа товара (ТЗ 2.2 §6.4 — доход для окупаемости покупок).
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

        // Заводы (ТЗ 2.2 §9): по одной записи на OutpostMap.Factories, индекс цели и снимок «автомат уже включён».
        readonly int[] m_FactoryTarget;
        readonly bool[] m_FactoryAutoSeen;

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
            var factories = map.Factories ?? new MapFactory[0];
            m_FactoryTarget = new int[factories.Length];
            m_FactoryAutoSeen = new bool[factories.Length];
            var cards = sim.Configs.Cards;
            for (int k = 0; k < factories.Length; k++)
            {
                var mf = factories[k];
                int ti = map.TargetIndex(mf.TargetId);
                m_FactoryTarget[k] = ti;
                var rule = run.FactoryRules?.ForType(mf.TypeId);
                int level = sim.Profile.CardLevel(mf.RequiredCardId);
                report.Factories.Add(new FactoryAudit
                {
                    Id = mf.TargetId,
                    TypeId = mf.TypeId,
                    GoodId = rule?.GoodId,
                    TypeNumber = rule != null ? run.FactoryRules.NumberOfGood(rule.GoodId) : 0,
                    CycleSec = rule?.CycleSec ?? 0,
                    Sector = ti >= 0 ? map.Targets[ti].Sector : -1,
                    RequiredCardId = mf.RequiredCardId,
                    RequiredCardLevel = mf.RequiredCardLevel,
                    CardLevelStart = level,
                    AutoAtStart = mf.RequiredCardId != null && level >= 1 && level >= mf.RequiredCardLevel,
                    UnitsStart = FactoryMath.UnitsPerCycle(rule, sim.Profile, cards),
                    PriceBase = rule != null ? Base(FactoryQueries.Price(run, rule.GoodId)) : 0,
                });
            }
            m_Last = run.Now;
            m_Income.Add((run.Now, Income(run)));
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

        /// <summary>Доход шахты для окупаемости покупок: монеты + продажа товара (ТЗ 2.2 §6.4).</summary>
        static BigInteger Income(OutpostRun run) => run.TotalCredited + run.GoodsSoldGold;

        /// <summary>Стоимость товара по цене шахты: на складе, в трубах, в готовых партиях (базовые монеты).</summary>
        public (double Warehouse, double Pipes, double Ready) GoodsValue()
        {
            var run = m_Sim.Run;
            double warehouse = Base(FactoryQueries.StockValue(run)), pipes = 0, ready = 0;
            if (run.PipeBatches != null)
                foreach (var b in run.PipeBatches) pipes += Base(b.Units * FactoryQueries.Price(run, b.GoodId));
            if (run.Factories != null)
                foreach (var f in run.Factories)
                    if (f.ReadyUnits > 0) ready += Base(f.ReadyUnits * FactoryQueries.Price(run, f.GoodId));
            return (warehouse, pipes, ready);
        }

        readonly HashSet<int> m_AutoAtLeave = new HashSet<int>();

        /// <summary>Игрок ушёл: запомнить заводы, которые уже на автомате.</summary>
        public void MarkLeave()
        {
            m_AutoAtLeave.Clear();
            var list = m_Sim.Run.Factories;
            if (list != null)
                foreach (var f in list)
                    if (f.Automated) m_AutoAtLeave.Add(f.TargetId);
        }

        /// <summary>
        /// Игрок вернулся после отсутствия (ТЗ 2.2 §9): склад по ручным и автоматическим заводам, готовые партии, трубы.
        /// Товар однозначно относится к заводу: каждый тип в шахте встречается не больше одного раза (§3.1). Автоматический —
        /// завод, который был на автомате при уходе или включил его, пока шахта шла; если шахта завершилась за время отсутствия,
        /// флаг от карточки сундука выхода (включается уже в ReadyToContinue) завод автоматическим не делает.
        /// </summary>
        public void MeasureReturn(AbsenceAudit a)
        {
            var run = m_Sim.Run;
            bool running = run.Phase != RunPhase.ReadyToContinue;
            a.Factories = run.Factories?.Count ?? 0;
            a.AutoAtLeave = m_AutoAtLeave.Count;
            if (run.Factories != null)
                foreach (var f in run.Factories)
                {
                    if (f.Status == FactoryStatus.Locked)
                    {
                        a.Locked++;
                        continue;
                    }
                    var price = FactoryQueries.Price(run, f.GoodId);
                    double stock = Base(FactoryQueries.Stock(run, f.GoodId) * price);
                    if (m_AutoAtLeave.Contains(f.TargetId) || (running && f.Automated))
                    {
                        a.AutoFactories++;
                        a.WarehouseAutoBase += stock;
                    }
                    else
                    {
                        a.ManualFactories++;
                        a.WarehouseManualBase += stock;
                        a.ReadyManualBase += Base(f.ReadyUnits * price);
                    }
                }
            a.PipesBase = GoodsValue().Pipes;
        }

        public double CreditedBaseNow => Base(m_Sim.Run.TotalCredited);

        /// <summary>Модель доставила и продала на проверке.</summary>
        public void OnFactories(int delivered, BigInteger sold)
        {
            if (sold > 0) m_Report.Sales++;
        }

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
                        GoodsSoldBase = Base(run.GoodsSoldGold),
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

            var income = Income(run);
            if (m_Income[m_Income.Count - 1].Credited != income) m_Income.Add((now, income));

            // Заводы: первая отметка с автодоставкой (цепи разбиты — по m_Destroyed цели).
            var rf = run.Factories;
            if (rf != null)
                for (int k = 0; k < m_FactoryAutoSeen.Length && k < rf.Count; k++)
                    if (!m_FactoryAutoSeen[k] && rf[k].Automated)
                    {
                        m_FactoryAutoSeen[k] = true;
                        m_Report.Factories[k].AutoSec = Sec(now);
                    }

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
            double rate = window > 0 ? Base(Income(run) - startCredited) / window : 0;
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
            r.GoodsSoldBase = Base(run.GoodsSoldGold);
            r.ManualDeliveries = c.ManualDeliveries;
            var lost = GoodsValue();
            r.GoodsLostWarehouseBase = lost.Warehouse;
            r.GoodsLostPipesBase = lost.Pipes;
            r.GoodsLostBase = lost.Warehouse + lost.Pipes;
            r.GoodsLostReadyBase = lost.Ready;
            var rf = run.Factories;
            for (int k = 0; k < r.Factories.Count; k++)
            {
                var fa = r.Factories[k];
                var f = rf != null && k < rf.Count && rf[k].TargetId == fa.Id ? rf[k] : FactoryQueries.Find(run, fa.Id);
                int ti = m_FactoryTarget[k];
                if (ti >= 0 && m_Destroyed[ti] >= 0) fa.UnlockSec = Sec(m_Destroyed[ti]);
                fa.CardLevelEnd = sim.Profile.CardLevel(fa.RequiredCardId);
                if (f == null) continue;
                fa.AutoAtEnd = f.Automated;
                if (f.Automated && fa.AutoSec == null) fa.AutoSec = Sec(run.Now);
                fa.Cycles = f.Cycles;
                fa.Produced = f.Produced;
            }
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

        /// <summary>
        /// Пройти текущую шахту моделью игрока до ReadyToContinue; отчёт по шахте. На каждой проверке модель сначала доставляет
        /// готовые партии и продаёт товар (<see cref="PlayerModel.Tend"/>), затем покупает. absence — опыт отсутствия (ТЗ 2.2 §9):
        /// на отметке StartSec игрок уходит (ни проверок, ни ударов), ядро считает интервал тем же Advance, что и офлайн
        /// (OfflineSimulator, ТЗ 2.1 §13), по возвращении игрок сразу делает проверку и доигрывает шахту; итог — report.Absences.
        /// </summary>
        public static MineAudit RunMine(OutpostSimulation sim, PlayerModel model, string scenario, bool trace = false, AbsencePlan absence = null)
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
            // Профиль b6: пределы растут с множителем темпа шахты K(z) (OutpostBot.PaceLimitFactor).
            long pace = OutpostBot.PaceLimitFactor(run);
            long tickLimit = TickLimit * pace, eventLimit = EventLimit * pace;
            long awayAt = absence != null ? Math.Max(run.Now, (long)Math.Round(absence.StartSec * tps)) : long.MaxValue;
            AbsenceAudit away = null;

            void Act()
            {
                var (delivered, sold) = model.Tend(sim);
                probe.OnFactories(delivered, sold);
                model.Buy(sim, probe);
            }

            try
            {
                if (checkEvery == 0 || nextCheck <= run.Now)
                {
                    Act();
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
                    if (awayAt < t) t = awayAt;
                    if (t == long.MaxValue)
                    {
                        sim.AdvanceToNextEvent();
                        report.Outcome = "Watchdog";
                        report.Error = run.WatchdogDiagnostic;
                        break;
                    }
                    if (t > tickLimit)
                    {
                        report.Outcome = "Timeout";
                        break;
                    }
                    if (sim.ProcessedEvents - events0 > eventLimit)
                    {
                        report.Outcome = "EventLimit";
                        break;
                    }
                    probe.AdvanceTo(t);
                    sim.Advance(t);
                    if (t == awayAt)
                    {
                        // Игрок уходит до своей проверки на этой отметке; интервал отсутствия — одним Advance, как офлайн.
                        long back = checked(t + (long)Math.Round(absence.Hours * 3600 * tps));
                        double coins0 = probe.CreditedBaseNow;
                        away = new AbsenceAudit { Hours = absence.Hours, StartSec = t / (double)tps, ReturnSec = back / (double)tps };
                        probe.Snapshot();
                        probe.MarkLeave();
                        probe.AdvanceTo(back);
                        sim.Advance(back);
                        away.ReturnedBeforeComplete = !sim.IsEconomyStopped;
                        away.CoinsDuringBase = probe.CreditedBaseNow - coins0;
                        probe.MeasureReturn(away);
                        awayAt = long.MaxValue;
                        t = run.Now;
                        nextCheck = t;
                        nextTap = model.NextTapTick(t, tps);
                    }
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
                        Act();
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
            if (away != null)
            {
                away.CompletedSec = report.CompletedSec;
                away.Outcome = report.Outcome;
                away.LostBase = report.GoodsLostBase;
                away.LostReadyBase = report.GoodsLostReadyBase;
                away.GoodsSoldBase = report.GoodsSoldBase;
                report.Absences = new List<AbsenceAudit> { away };
            }
            return report;
        }

        /// <summary>
        /// Последовательная кампания 1..toZone: награды, карточки, детали и экипировка переносятся законным путём;
        /// между шахтами модель игрока выполняет действия экрана итогов. Останавливается на первой неуспешной шахте.
        /// absenceHours — опыт отсутствия ТЗ 2.2 §9 в каждой шахте: копия начала шахты проходится той же моделью, игрок уходит на
        /// 50 % длительности основного прогона на каждое из значений (часы); итог — MineAudit.Absences основного отчёта. Копии
        /// не влияют на кампанию.
        /// </summary>
        public static CampaignAudit RunCampaign(BalanceRepository repo, OutpostConfigSet configs, PlayerModel model, ulong seed,
            int toZone = OutpostIds.LastZone, ICollection<int> traceZones = null, Action<int, OutpostGameState> beforeZone = null,
            IReadOnlyList<double> absenceHours = null)
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
                FactoryRulesVersion = configs.FactoryRules?.Version,
                PriceRate = configs.FactoryRules?.PriceRate.ToString(),
                Success = true,
            };
            var state = StageProgressionService.NewGame(repo, configs, seed, 0, 1);
            var map = OutpostLevelAdapter.BuildMine(repo, 1, configs);
            while (true)
            {
                beforeZone?.Invoke(state.Run.Zone, state);
                var start = absenceHours != null && absenceHours.Count > 0 ? OutpostJson.DeepCopy(state) : null;
                var sim = new OutpostSimulation(state, map, configs);
                if (model.EquipBest) sim.Submit(OutpostCommand.EquipBest());
                if (model.SkipSlot != null) sim.Submit(OutpostCommand.Unequip(model.SkipSlot.Value));
                var report = RunMine(sim, model, model.Name, traceZones != null && traceZones.Contains(state.Run.Zone));
                audit.Mines.Add(report);
                if (start != null && report.Outcome == "Completed" && report.CompletedSec != null)
                {
                    report.Absences = new List<AbsenceAudit>();
                    foreach (double hours in absenceHours)
                    {
                        var copy = OutpostJson.DeepCopy(start);
                        var awaySim = new OutpostSimulation(copy, map, configs);
                        var awayModel = model.Clone();
                        if (awayModel.EquipBest) awaySim.Submit(OutpostCommand.EquipBest());
                        if (awayModel.SkipSlot != null) awaySim.Submit(OutpostCommand.Unequip(awayModel.SkipSlot.Value));
                        var plan = new AbsencePlan { StartSec = report.CompletedSec.Value / 2, Hours = hours };
                        var awayReport = RunMine(awaySim, awayModel, model.Name + "-away" + hours, false, plan);
                        if (awayReport.Absences != null) report.Absences.AddRange(awayReport.Absences);
                    }
                }
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
        /// Одна шахта из записанного начала кампании (калибровка темпа b6, Docs/Rework/BALANCE_FACTORIES.md «Темп b6: 3 цикла»):
        /// копия профиля start (снимок beforeZone кампании), run шахты создаётся заново на configs с тем же RunId (криты те же),
        /// затем модель проходит шахту, как в <see cref="RunCampaign"/>. Награды, карточки и детали между шахтами от длительности
        /// шахты не зависят, поэтому начало шахты z из кампании с K = 1 — то же, что в кампании с K(z).
        /// </summary>
        public static MineAudit RunMineFrom(OutpostGameState start, BalanceRepository repo, OutpostConfigSet configs, PlayerModel model,
            bool trace = false)
        {
            if (start?.Run == null) throw new ArgumentNullException(nameof(start));
            var profile = OutpostJson.DeepCopy(start.Profile);
            profile.NextRunSeq--;
            int zone = start.Run.Zone;
            var map = OutpostLevelAdapter.BuildMine(repo, zone, configs);
            var state = new OutpostGameState
            {
                Profile = profile,
                Run = StageProgressionService.CreateRun(profile, zone, map, configs, repo, start.Run.LastProcessedUtcMs),
            };
            if (state.Run.RunId != start.Run.RunId) throw new InvalidOperationException("RunMineFrom: другой RunId " + state.Run.RunId);
            var sim = new OutpostSimulation(state, map, configs);
            var m = model.Clone();
            if (m.EquipBest) sim.Submit(OutpostCommand.EquipBest());
            if (m.SkipSlot != null) sim.Submit(OutpostCommand.Unequip(m.SkipSlot.Value));
            return RunMine(sim, m, m.Name, trace);
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
                var model = new PlayerModel { Name = "saturation", Strategy = PurchaseStrategy.None, EquipBest = false, SpendParts = false, UpgradeCards = false,
                    ManageFactories = false };
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
                    // между шахтами улучшает карточки, тратит детали и надевает лучшее. Киркой не бьёт. Заводы (ТЗ 2.2 §9): на каждой
                    // проверке доставляет все готовые партии и продаёт весь товар (так же у всех моделей, кроме none).
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.Bottleneck, CheckIntervalSec = 30, FirstCheckSec = 15 };
                case "free-cheapest":
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.Cheapest, CheckIntervalSec = 30, FirstCheckSec = 15 };
                case "free-roundrobin":
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.RoundRobin, CheckIntervalSec = 30, FirstCheckSec = 15 };
                case "free-damage":
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.DamageFocus, CheckIntervalSec = 30, FirstCheckSec = 15 };
                case "idle":
                    // Idle: котики работают сами, игрок возвращается раз в 5 минут, доставляет и продаёт товар и покупает самое
                    // дешёвое, пока хватает.
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.Cheapest, CheckIntervalSec = 300, FirstCheckSec = 60 };
                case "active":
                    // Активный: реагирует за 5 с (доставка, продажа, покупки), выбирает узкое место, бьёт киркой 2 раза/с
                    // (интерфейс пропускает не чаще раза в 0,4 с) сериями 30 с через 30 с отдыха — в среднем удар в секунду.
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
                    // Никаких действий игрока: ни покупок, ни доставок, ни продаж.
                    return new PlayerModel { Name = name, Strategy = PurchaseStrategy.None, SpendParts = false, UpgradeCards = false,
                        ManageFactories = false };
                default:
                    throw new ArgumentException("Неизвестная модель игрока " + name);
            }
        }
    }
}
