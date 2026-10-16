using System;
using System.Collections.Generic;
using System.Numerics;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Заводы, трубы, склад товаров и рынок (ТЗ 2.2 §4.2–§4.7, §5, §6, CORE_SPEC §16). Состояние — <see cref="OutpostRun.Factories"/>,
    /// <see cref="OutpostRun.PipeBatches"/>, склад и счётчики товара; правила — снимок <see cref="OutpostRun.FactoryRules"/> шахты;
    /// требование автодоставки и маршрут трубы — <see cref="OutpostMap.Factory"/>.
    /// <para>
    /// Порядок внутри отметки (§4.5): шаг 4б после разгрузки монет (фаза 4) и до ворот/завершения (фаза 5): 4б-1 — прибытия
    /// партий по (ArriveTick, ID завода, Seq), 4б-2 — концы циклов по (CycleEnd, ID завода). Команды «Deliver» и «Sell» —
    /// фаза 6. В ReadyToContinue шаг 4б не выполняется, сроки заводов в <see cref="NextEventTick"/> не входят. Циклы и прибытия
    /// не вызывают Touch: watchdog по-прежнему ловит застрявших котиков (для этого, пока у заводов есть сроки, ближайшая
    /// отметка не позже LastChangeTick + WatchdogTicks + 1).
    /// </para>
    /// </summary>
    public sealed partial class OutpostSimulation
    {
        // Переиспользуемые буферы шага 4б (без выделений в обычной отметке).
        readonly List<PipeBatch> m_Arrivals = new List<PipeBatch>();
        readonly List<FactoryState> m_CycleEnds = new List<FactoryState>();

        static readonly Comparison<PipeBatch> s_ByArrival = (a, b) =>
            a.ArriveTick != b.ArriveTick ? a.ArriveTick.CompareTo(b.ArriveTick) :
            a.FactoryId != b.FactoryId ? a.FactoryId.CompareTo(b.FactoryId) : a.Seq.CompareTo(b.Seq);

        static readonly Comparison<FactoryState> s_ByCycleEnd = (a, b) =>
            a.CycleEnd != b.CycleEnd ? a.CycleEnd.CompareTo(b.CycleEnd) : a.TargetId.CompareTo(b.TargetId);

        // ───────────────────────────── Сроки ─────────────────────────────

        /// <summary>
        /// Ближайший срок заводов (§4.5): минимум CycleEnd заводов в Working и ArriveTick партий в трубах — только в Running и
        /// DrainingCargo; long.MaxValue — сроков нет. Пока сроки есть и watchdog не сработал, добавляется отметка
        /// LastChangeTick + WatchdogTicks + 1: циклы и прибытия не трогают LastChangeTick, поэтому без неё застрявшие котики
        /// при длинном цикле (до 8 ч) обнаруживались бы только к концу цикла.
        /// </summary>
        long NextFactoryEventTick()
        {
            var run = Run;
            if (run.Phase == RunPhase.ReadyToContinue) return long.MaxValue;
            long min = long.MaxValue;
            var factories = run.Factories;
            if (factories != null)
                for (int i = 0; i < factories.Count; i++)
                {
                    var f = factories[i];
                    if (f.Status == FactoryStatus.Working && f.CycleEnd < min) min = f.CycleEnd;
                }
            var pipes = run.PipeBatches;
            if (pipes != null)
                for (int i = 0; i < pipes.Count; i++)
                    if (pipes[i].ArriveTick < min) min = pipes[i].ArriveTick;
            if (min == long.MaxValue) return min;
            if (!run.WatchdogFired)
            {
                long watchdog = run.LastChangeTick > long.MaxValue - run.Balance.WatchdogTicks - 1
                    ? long.MaxValue
                    : run.LastChangeTick + run.Balance.WatchdogTicks + 1;
                if (watchdog < min) min = watchdog;
            }
            return Math.Max(run.Now, min);
        }

        // ───────────────────────────── Шаг 4б ─────────────────────────────

        /// <summary>Шаг 4б (§4.5): прибытия на склад, затем концы циклов; автоматические заводы сразу отправляют партию.</summary>
        void PhaseFactories(long t)
        {
            var run = Run;
            if (run.Phase == RunPhase.ReadyToContinue) return;

            // 4б-1: партии с ArriveTick ≤ t — на склад, по (ArriveTick, ID завода, Seq).
            var pipes = run.PipeBatches;
            if (pipes != null && pipes.Count > 0)
            {
                m_Arrivals.Clear();
                for (int i = 0; i < pipes.Count; i++)
                    if (pipes[i].ArriveTick <= t) m_Arrivals.Add(pipes[i]);
                if (m_Arrivals.Count > 0)
                {
                    if (m_Arrivals.Count > 1) m_Arrivals.Sort(s_ByArrival);
                    for (int i = 0; i < m_Arrivals.Count; i++) Arrive(m_Arrivals[i], t);
                    // Удаление прибывших с сохранением порядка Seq остальных.
                    int keep = 0;
                    for (int i = 0; i < pipes.Count; i++)
                        if (pipes[i].ArriveTick > t) pipes[keep++] = pipes[i];
                    pipes.RemoveRange(keep, pipes.Count - keep);
                    m_Arrivals.Clear();
                }
            }

            // 4б-2: циклы с CycleEnd ≤ t, по (CycleEnd, ID завода). Повтор — только если срок отстал больше чем на цикл
            // (в обычной работе каждая отметка конца цикла обрабатывается ровно в свой тик, и проход один).
            var factories = run.Factories;
            if (factories == null || factories.Count == 0) return;
            while (true)
            {
                m_CycleEnds.Clear();
                for (int i = 0; i < factories.Count; i++)
                {
                    var f = factories[i];
                    if (f.Status == FactoryStatus.Working && f.CycleEnd <= t) m_CycleEnds.Add(f);
                }
                if (m_CycleEnds.Count == 0) break;
                if (m_CycleEnds.Count > 1) m_CycleEnds.Sort(s_ByCycleEnd);
                for (int i = 0; i < m_CycleEnds.Count; i++) CompleteCycle(m_CycleEnds[i], t);
            }
            m_CycleEnds.Clear();
        }

        /// <summary>Партия пришла на склад: Warehouse и GoodsArrived растут на её количество.</summary>
        void Arrive(PipeBatch b, long t)
        {
            var run = Run;
            m_ProcessedEvents++;
            AddCount(run.Warehouse, b.GoodId, b.Units);
            AddCount(run.GoodsArrived, b.GoodId, b.Units);
            if (!RecordEvents) return;
            var e = OutpostEvent.Create(OutpostEventType.GoodsArrived, t);
            e.TargetId = b.FactoryId;
            e.ItemId = b.GoodId;
            e.Count = b.Units;
            e.BatchSeq = b.Seq;
            m_Events.Add(e);
        }

        /// <summary>
        /// Конец цикла (§4.4): выпуск Units фиксируется по текущим уровням карточек (<see cref="FactoryMath.UnitsPerCycle"/>).
        /// Ручной завод — Ready с готовой партией (ждёт «Deliver»): цикла у него нет, CycleStart = CycleEnd = −1 (отметка готовности
        /// — Tick события FactoryReady); автоматический — партия сразу в трубу, следующий цикл [CycleEnd, CycleEnd + CycleTicks) —
        /// с конца предыдущего, без накопления сдвига.
        /// </summary>
        void CompleteCycle(FactoryState f, long t)
        {
            var rule = Run.FactoryRules?.ForType(f.TypeId);
            m_ProcessedEvents++;
            if (rule == null)
            {
                // Не бывает у run, созданного ядром: без правил завод не может работать.
                f.Status = FactoryStatus.Ready;
                f.CycleStart = -1;
                f.CycleEnd = -1;
                return;
            }
            int units = FactoryMath.UnitsPerCycle(rule, Profile, m_Cards);
            f.Produced = checked(f.Produced + units);
            f.Cycles = checked(f.Cycles + 1);
            if (f.Automated)
            {
                long end = f.CycleEnd;
                Dispatch(f, units, t, true);
                f.CycleStart = end;
                f.CycleEnd = checked(end + rule.CycleTicks);
                return;
            }
            f.Status = FactoryStatus.Ready;
            f.ReadyUnits = units;
            f.CycleStart = -1;
            f.CycleEnd = -1;
            if (!RecordEvents) return;
            var e = OutpostEvent.Create(OutpostEventType.FactoryReady, t);
            e.TargetId = f.TargetId;
            e.ItemId = f.GoodId;
            e.Count = units;
            m_Events.Add(e);
        }

        /// <summary>Партия в трубу: PipeBatch (Seq по порядку), Dispatched += units, событие GoodsDispatched.</summary>
        void Dispatch(FactoryState f, int units, long t, bool automatic)
        {
            var run = Run;
            var batch = new PipeBatch
            {
                Seq = run.NextPipeBatchSeq++,
                FactoryId = f.TargetId,
                GoodId = f.GoodId,
                Units = units,
                DepartTick = t,
                ArriveTick = checked(t + FactoryTransitTicks(f.TargetId)),
            };
            run.PipeBatches.Add(batch);
            f.Dispatched = checked(f.Dispatched + units);
            if (!RecordEvents) return;
            var e = OutpostEvent.Create(OutpostEventType.GoodsDispatched, t);
            e.TargetId = f.TargetId;
            e.ItemId = f.GoodId;
            e.Count = units;
            e.BatchSeq = batch.Seq;
            e.Extra = automatic ? 1 : 0;
            m_Events.Add(e);
        }

        /// <summary>Готовая партия Ready-завода уходит в трубу, следующий цикл [t, t + CycleTicks) начинается с отметки t.</summary>
        void DeliverReady(FactoryState f, long t, bool automatic)
        {
            var rule = Run.FactoryRules.ForType(f.TypeId);
            int units = f.ReadyUnits;
            f.ReadyUnits = 0;
            Dispatch(f, units, t, automatic);
            f.Status = FactoryStatus.Working;
            f.CycleStart = t;
            f.CycleEnd = checked(t + rule.CycleTicks);
        }

        static void AddCount(Dictionary<string, long> d, string key, long value)
        {
            d.TryGetValue(key, out long old);
            d[key] = checked(old + value);
        }

        // ───────────────────────────── Открытие, автодоставка, остановка ─────────────────────────────

        /// <summary>Завод run по ID цели или null (линейный поиск: заводов в шахте ≤ 15).</summary>
        FactoryState FactoryOf(int targetId)
        {
            var list = Run.Factories;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i].TargetId == targetId) return list[i];
            return null;
        }

        /// <summary>
        /// Цель — стоящий завод run: после разрушения цепей footprint остаётся зданием (ТЗ 2.2 §4.1) — стена навигации, на нём не
        /// стоят и не лежит груз. Run без заводов (без снимка правил) освобождает footprint <c>s</c>, как прежнее месторождение.
        /// </summary>
        bool IsStandingFactory(MapTarget mt) => mt.IsFactory && FactoryOf(mt.Id) != null;

        /// <summary>
        /// Цепи разбиты (§4.2): после сундука rewardId — завод в Working с первого цикла от отметки t, событие FactoryUnlocked,
        /// затем проверка требования автодоставки.
        /// </summary>
        void UnlockFactory(MapTarget mt, long t)
        {
            var f = FactoryOf(mt.Id);
            if (f == null || f.Status != FactoryStatus.Locked) return;
            var rule = Run.FactoryRules?.ForType(f.TypeId);
            if (rule == null) return;
            f.Status = FactoryStatus.Working;
            f.CycleStart = t;
            f.CycleEnd = checked(t + rule.CycleTicks);
            if (RecordEvents)
            {
                var e = OutpostEvent.Create(OutpostEventType.FactoryUnlocked, t);
                e.TargetId = f.TargetId;
                e.Cell = m_Map.Cell(mt.Column, mt.Row);
                e.ItemId = f.GoodId;
                e.Detail = f.TypeId;
                m_Events.Add(e);
            }
            CheckAutomation(f, t);
        }

        /// <summary>
        /// Требование автодоставки всех открытых заводов (§4.4) на отметке t: при любом росте уровня карточки (OnCardsChanged),
        /// при загрузке и пересоздании симуляции. Для снимка, записанного ядром, ничего не меняет.
        /// </summary>
        void SyncFactoryAutomation(long t)
        {
            var list = Run.Factories;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++) CheckAutomation(list[i], t);
        }

        /// <summary>
        /// Уровень карточки требования (cardId токена) ≥ cardLevel → Automated (не снимается), событие FactoryAutomated. Готовая
        /// партия Ready-завода в Running/DrainingCargo уходит в трубу на этой же отметке; в ReadyToContinue флаг включается без
        /// отправки (заводы стоят). Карточка без копий (уровень 0) требованию не удовлетворяет.
        /// </summary>
        bool CheckAutomation(FactoryState f, long t)
        {
            if (f.Automated || f.Status == FactoryStatus.Locked) return false;
            var mf = m_Map.Factory(f.TargetId);
            if (mf == null || mf.RequiredCardId == null) return false;
            int level = Profile.CardLevel(mf.RequiredCardId);
            if (level < 1 || level < mf.RequiredCardLevel) return false;
            SetAutomated(f, mf.RequiredCardId, level, t);
            return true;
        }

        void SetAutomated(FactoryState f, string cardId, int level, long t)
        {
            f.Automated = true;
            if (RecordEvents)
            {
                var e = OutpostEvent.Create(OutpostEventType.FactoryAutomated, t);
                e.TargetId = f.TargetId;
                e.ItemId = f.GoodId;
                e.Detail = cardId;
                e.Count = level;
                m_Events.Add(e);
            }
            if (f.Status == FactoryStatus.Ready && Run.Phase != RunPhase.ReadyToContinue && f.ReadyUnits > 0 && Run.FactoryRules != null)
                DeliverReady(f, t, true);
        }

        /// <summary>
        /// Шахта завершена (§4.6): открытые заводы — Stopped, циклов нет (CycleStart/CycleEnd = −1); готовая партия остаётся в
        /// ReadyUnits, партии в трубах не приходят — непроданное пропадёт при переходе. Вызывается до сундука выхода, поэтому
        /// карточка из него может включить AUTO, но ничего не отправит.
        /// </summary>
        void StopFactories()
        {
            var list = Run.Factories;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i];
                if (f.Status == FactoryStatus.Locked) continue;
                f.Status = FactoryStatus.Stopped;
                f.CycleStart = -1;
                f.CycleEnd = -1;
            }
        }

        // ───────────────────────────── Команды (фаза 6) ─────────────────────────────

        /// <summary>
        /// «Deliver» (фаза 6): готовая партия ReadyUnits в трубу (GoodsDispatched, Extra = 0), следующий цикл с отметки t,
        /// Counters.ManualDeliveries++. Levels — единиц товара. Отказ ничего не меняет.
        /// </summary>
        CommandResult ExecuteDeliverFactory(OutpostCommand cmd, long t)
        {
            string reason = DeliverBlockReason(cmd.TargetId);
            if (reason != null) return CommandResult.Fail(reason);
            var f = FactoryOf(cmd.TargetId);
            if (f == null || Run.FactoryRules?.ForType(f.TypeId) == null) return CommandResult.Fail("unknown");
            int units = f.ReadyUnits;
            DeliverReady(f, t, false);
            Run.Counters.ManualDeliveries++;
            return new CommandResult { Ok = true, Levels = units };
        }

        /// <summary>
        /// Продажа (фаза 6, §6.3–§6.4): весь остаток товара ItemId (null — всех товаров по номеру типа) по цене
        /// ceil(CoinBase × S(z) × PriceFactor) снимков шахты: Warehouse → 0, GoodsSold +, GoodsSoldGold +, Wallet +; событие
        /// GoodsSold на каждый проданный товар. Бонусы цены монет на товар не действуют; TotalCredited не меняется.
        /// Levels — продано единиц (с насыщением до int), Cost — золото.
        /// </summary>
        CommandResult ExecuteSellGoods(OutpostCommand cmd, long t)
        {
            string reason = SellBlockReason(cmd.ItemId);
            if (reason != null) return CommandResult.Fail(reason);
            long units = 0;
            var gold = BigInteger.Zero;
            if (cmd.ItemId != null) SellOne(cmd.ItemId, t, ref units, ref gold);
            else
            {
                var goods = Run.FactoryRules?.Goods;
                if (goods != null)
                    for (int i = 0; i < goods.Count; i++)
                    {
                        string good = goods[i].GoodId;
                        if (FactoryQueries.Stock(Run, good) > 0) SellOne(good, t, ref units, ref gold);
                    }
            }
            return new CommandResult { Ok = true, Levels = units > int.MaxValue ? int.MaxValue : (int)units, Cost = gold };
        }

        void SellOne(string goodId, long t, ref long units, ref BigInteger gold)
        {
            var run = Run;
            long n = FactoryQueries.Stock(run, goodId);
            if (n <= 0) return;
            var value = n * FactoryQueries.Price(run, goodId);
            run.Warehouse[goodId] = 0;
            AddCount(run.GoodsSold, goodId, n);
            run.GoodsSoldGold += value;
            run.Wallet += value;
            units = checked(units + n);
            gold += value;
            if (!RecordEvents) return;
            var e = OutpostEvent.Create(OutpostEventType.GoodsSold, t);
            e.ItemId = goodId;
            e.Count = n > int.MaxValue ? int.MaxValue : (int)n;
            e.Value = value;
            m_Events.Add(e);
        }

        // ───────────────────────────── Отладка (DevTools, только debug-профиль, ТЗ 2.2 §13) ─────────────────────────────

        /// <summary>
        /// DEV «Finish factory cycles»: все заводы в Working досрочно заканчивают цикл на отметке Now — срок цикла переносится на Now,
        /// дальше обычный конец цикла шага 4б (<see cref="CompleteCycle"/>): ручные — Ready с CycleStart = CycleEnd = −1, как после
        /// полного цикла; автоматические — партия в трубу и новый полный цикл [Now, Now + CycleTicks). Укороченный цикл в
        /// состоянии не остаётся. Возвращает число заводов. В ReadyToContinue — 0.
        /// </summary>
        public int DebugFinishFactoryCycles()
        {
            var run = Run;
            if (run.Phase == RunPhase.ReadyToContinue || run.Factories == null) return 0;
            int n = 0;
            foreach (var f in run.Factories)
                if (f.Status == FactoryStatus.Working)
                {
                    f.CycleEnd = run.Now;
                    n++;
                }
            if (n > 0) PhaseFactories(run.Now);
            return n;
        }

        /// <summary>
        /// DEV «Goods ×100»: по unitsPerGood единиц каждого товара заводов шахты на склад и в <see cref="OutpostRun.DebugGoods"/>
        /// (иначе запись упадёт на OutpostInvariants.Check). Только для debug-профиля: вне него инварианты требуют пустой DebugGoods.
        /// Возвращает число товаров.
        /// </summary>
        public int DebugGrantGoods(long unitsPerGood)
        {
            var run = Run;
            if (unitsPerGood <= 0 || run.Factories == null) return 0;
            int n = 0;
            var goods = run.FactoryRules?.Goods;
            if (goods == null) return 0;
            foreach (var g in goods)
            {
                if (!FactoryQueries.HasGood(run, g.GoodId)) continue;
                AddCount(run.Warehouse, g.GoodId, unitsPerGood);
                AddCount(run.DebugGoods, g.GoodId, unitsPerGood);
                n++;
            }
            return n;
        }

        /// <summary>
        /// DEV «Unlock factories»: разбить цепи всех закрытых заводов с наградами (сундук rewardId, FactoryUnlocked, проверка AUTO),
        /// по возрастанию ID; невыпущенный груз цепей (adapter-1) не выдаётся. Затем фазы 5 и 7 на Now. Только в Running.
        /// </summary>
        public int DebugUnlockFactories()
        {
            var run = Run;
            if (run.Phase != RunPhase.Running || run.Factories == null) return 0;
            long t = run.Now;
            int n = 0;
            foreach (var f in run.Factories)
            {
                if (f.Status != FactoryStatus.Locked) continue;
                int ti = m_Map.TargetIndex(f.TargetId);
                if (ti < 0 || run.Targets[ti].Destroyed) continue;
                var ts = run.Targets[ti];
                ts.TotalUnits = ts.ReleasedUnits;
                ts.RemainingHpUnits = 0;
                Touch(t);
                DestroyTarget(ti, t);
                n++;
            }
            if (n == 0) return 0;
            PhaseGatesAndCompletion(t);
            Plan(t);
            return n;
        }

        /// <summary>
        /// DEV «Auto all»: автодоставка всех заводов шахты без изменения карточек (флаг не снимается); готовые партии сразу уходят в
        /// трубу. Возвращает число заводов, у которых флаг включился.
        /// </summary>
        public int DebugAutomateAll()
        {
            var run = Run;
            if (run.Factories == null) return 0;
            int n = 0;
            foreach (var f in run.Factories)
            {
                if (f.Automated) continue;
                var mf = m_Map.Factory(f.TargetId);
                SetAutomated(f, mf?.RequiredCardId, mf != null ? Profile.CardLevel(mf.RequiredCardId) : 0, run.Now);
                n++;
            }
            return n;
        }
    }
}
