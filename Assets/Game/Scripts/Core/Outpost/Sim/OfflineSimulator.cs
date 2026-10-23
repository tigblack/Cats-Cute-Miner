using System;
using System.Numerics;

namespace CatMiners.Core.Outpost
{
    /// <summary>Итог офлайна для окна «Пока вас не было» или тайм-скипа магазина (<see cref="SkipMinutes"/> &gt; 0).</summary>
    public sealed class OfflineSummary
    {
        /// <summary>Минуты тайм-скипа (ТЗ 10.5); 0 — обычный офлайн.</summary>
        public int SkipMinutes;
        /// <summary>ID плитки тайм-скипа (арт окна итога); null — обычный офлайн.</summary>
        public string SkipItemId;
        /// <summary>Фактическое отсутствие max(0, now − LastProcessedUtcMs).</summary>
        public long ElapsedMs;
        /// <summary>Учтённое отсутствие: не больше фактического интервала и трёх реальных часов.</summary>
        public long AppliedMs;
        public BigInteger GoldDelivered;
        public int UnitsDelivered;
        public int TargetsCleared;
        public int SectorsOpened;
        public bool StageCompleted;
        /// <summary>Сундуки, копии карточек и эликсир, полученные за отсутствие.</summary>
        public int ChestsOpened;
        public int CardsGained;
        public long ElixirGained;
        public int FinalProgressBefore = -1, FinalProgressAfter = -1;
    }

    /// <summary>
    /// Офлайн-прогресс (ТЗ 13): тот же Advance на копии последнего согласованного состояния, порционно.
    /// Результат подменяет состояние только после успешного commit (вызывающий сохраняет <see cref="OfflineJob.State"/>).
    /// </summary>
    public sealed class OfflineSimulator
    {
        readonly OutpostMap m_Map;
        readonly OutpostConfigSet m_Configs;

        public OfflineSimulator(OutpostMap map, OutpostConfigSet configs)
        {
            m_Map = map ?? throw new ArgumentNullException(nameof(map));
            m_Configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public OfflineJob Begin(OutpostGameState committed, long nowUtcMs)
        {
            if (committed?.Run == null) throw new ArgumentException("Нет состояния шахты", nameof(committed));
            var copy = OutpostJson.DeepCopy(committed);
            return new OfflineJob(copy, m_Map, m_Configs, nowUtcMs);
        }

        /// <summary>
        /// Тайм-скип магазина (ТЗ 10.5): тот же порционный расчёт на копии, но ровно <see cref="StoreItemDef.Minutes"/> минут работы
        /// шахты, без лимита отсутствия. Граница обработанного UTC остаётся «сейчас»: скип добавляет время шахты, а не реальное время.
        /// Оплату (<see cref="StoreService.PayTimeSkip"/>) вызывающий проводит на <see cref="OfflineJob.State"/> до первого шага,
        /// поэтому списание и результат записываются одним снимком.
        /// </summary>
        public OfflineJob BeginSkip(OutpostGameState committed, StoreItemDef skip, long nowUtcMs)
        {
            if (committed?.Run == null) throw new ArgumentException("Нет состояния шахты", nameof(committed));
            if (skip == null || skip.Kind != StoreItemKind.TimeSkip) throw new ArgumentException("Нужна плитка тайм-скипа", nameof(skip));
            if (skip.Minutes < 1) throw new ArgumentOutOfRangeException(nameof(skip), skip.Minutes, "Тайм-скип — не меньше минуты");
            var copy = OutpostJson.DeepCopy(committed);
            long duration = checked(skip.Minutes * 60_000L);
            var job = new OfflineJob(copy, m_Map, m_Configs, nowUtcMs, duration, duration);
            job.Summary.SkipMinutes = skip.Minutes;
            job.Summary.SkipItemId = skip.Id;
            return job;
        }
    }

    /// <summary>Порционный офлайн-расчёт: <see cref="Step"/> до завершения, затем <see cref="Summary"/> и <see cref="State"/>.</summary>
    public sealed class OfflineJob
    {
        readonly long m_NowUtcMs;
        readonly long m_TargetTick;
        readonly long m_StartTick;
        readonly BigInteger m_CreditedBefore;
        readonly int m_UnitsBefore;
        readonly int m_ClearedBefore;
        readonly int m_GatesBefore;
        readonly bool m_CompletedBefore;
        readonly int m_ChestsBefore, m_CardsBefore;
        readonly long m_ElixirBefore;

        internal OfflineJob(OutpostGameState copy, OutpostMap map, OutpostConfigSet configs, long nowUtcMs)
            : this(copy, map, configs, nowUtcMs, Elapsed(copy.Run, nowUtcMs), OfflineCap(configs, copy.Run.Zone)) { }

        /// <summary>Расчёт <paramref name="elapsedMs"/> (не больше <paramref name="capMs"/>) от текущей отметки шахты.</summary>
        internal OfflineJob(OutpostGameState copy, OutpostMap map, OutpostConfigSet configs, long nowUtcMs, long elapsedMs, long capMs)
        {
            State = copy;
            Simulation = new OutpostSimulation(copy, map, configs) { RecordEvents = false };
            var run = copy.Run;
            m_NowUtcMs = nowUtcMs;
            long elapsed = elapsedMs;
            long applied = Math.Min(elapsed, capMs);
            m_StartTick = run.Now;
            m_TargetTick = checked(run.Now + applied * (run.Balance.TicksPerSecond / 1000L));
            m_CreditedBefore = run.TotalCredited;
            m_UnitsBefore = run.Counters.DeliveredUnits;
            m_ClearedBefore = run.Counters.ClearedTargets;
            m_GatesBefore = run.GatesOpened;
            m_CompletedBefore = run.Phase == RunPhase.ReadyToContinue;
            m_ChestsBefore = run.Counters.ChestsOpened;
            m_CardsBefore = run.Counters.CardsGained;
            m_ElixirBefore = run.Counters.ElixirGained;
            Summary = new OfflineSummary { ElapsedMs = elapsed, AppliedMs = applied };
            Summary.FinalProgressBefore = FinalProgress(run);
        }

        static long Elapsed(OutpostRun run, long nowUtcMs)
        {
            BigInteger difference = (BigInteger)nowUtcMs - run.LastProcessedUtcMs;
            return difference.Sign <= 0 ? 0L : difference > long.MaxValue ? long.MaxValue : (long)difference;
        }

        /// <summary>
        /// Лимит отсутствия — текущая политика, а не снимок экономики шахты:
        /// Обычный предел — 3 часа; явно настроенный долгий участок может разрешить до 6 часов в своей шахте.
        /// </summary>
        static long OfflineCap(OutpostConfigSet configs, int zone) => Math.Max(0L, configs.Balance.ZoneOfflineCapMs(zone));

        /// <summary>Копия состояния, над которой идёт расчёт (после завершения — результат для commit).</summary>
        public OutpostGameState State { get; }
        public OutpostSimulation Simulation { get; }
        public long TargetTick => m_TargetTick;
        public bool IsDone { get; private set; }
        public OfflineSummary Summary { get; }

        /// <summary>Доля выполненного интервала для индикатора (только отображение).</summary>
        public double Progress
        {
            get
            {
                if (IsDone) return 1;
                long span = m_TargetTick - m_StartTick;
                if (span <= 0) return 1;
                return Math.Max(0, Math.Min(1, (double)(State.Run.Now - m_StartTick) / span));
            }
        }

        /// <summary>Порция расчёта: не более maxTicks отметок. true — расчёт завершён.</summary>
        public bool Step(int maxTicks)
        {
            if (IsDone) return true;
            var r = Simulation.Advance(m_TargetTick, maxTicks);
            if (!r.Reached) return false;
            Finish();
            return true;
        }

        /// <summary>Весь расчёт за один вызов.</summary>
        public OfflineSummary RunToEnd()
        {
            while (!Step(100_000)) { }
            return Summary;
        }

        void Finish()
        {
            var run = State.Run;
            run.LastProcessedUtcMs = Math.Max(run.LastProcessedUtcMs, m_NowUtcMs);
            Summary.GoldDelivered = run.TotalCredited - m_CreditedBefore;
            Summary.UnitsDelivered = run.Counters.DeliveredUnits - m_UnitsBefore;
            Summary.TargetsCleared = run.Counters.ClearedTargets - m_ClearedBefore;
            Summary.SectorsOpened = run.GatesOpened - m_GatesBefore;
            Summary.StageCompleted = !m_CompletedBefore && run.Phase == RunPhase.ReadyToContinue;
            Summary.ChestsOpened = run.Counters.ChestsOpened - m_ChestsBefore;
            Summary.CardsGained = run.Counters.CardsGained - m_CardsBefore;
            Summary.ElixirGained = run.Counters.ElixirGained - m_ElixirBefore;
            Summary.FinalProgressAfter = FinalProgress(run);
            IsDone = true;
        }

        static int FinalProgress(OutpostRun run)
        {
            if(run.MapFinal==null)return -1;
            foreach(var t in run.Targets)
                if(t.Id==run.MapFinal.TargetId&&t.MaxHpUnits>0)
                    return (int)((BigInteger)(t.MaxHpUnits-t.RemainingHpUnits)*100/t.MaxHpUnits);
            return -1;
        }
    }
}
