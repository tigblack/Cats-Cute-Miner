using System;
using System.Collections.Generic;
using System.Numerics;

namespace CatMiners.Core.Outpost
{
    /// <summary>Итог <see cref="OutpostSimulation.Advance"/>.</summary>
    public struct AdvanceResult
    {
        /// <summary>Достигнута целевая отметка (false — остановлено лимитом отметок).</summary>
        public bool Reached;
        /// <summary>Обработано отметок времени (ProcessTick).</summary>
        public int ProcessedTicks;
        /// <summary>Обработано событий: завершённых таймеров работников и команд.</summary>
        public long ProcessedEvents;
    }

    /// <summary>
    /// Детерминированное ядро режима CatOutpostV2 (ТЗ 6–7, CORE_SPEC §7). Состояние меняется только на отметках событий,
    /// каждая отметка — строго семь фаз (<see cref="ProcessTick"/>). Онлайн, офлайн, после загрузки и в тестах — один код.
    /// Производные кэши (занятость, владельцы рабочих клеток, BFS, Stats) пересобираются из состояния в конструкторе.
    /// </summary>
    public sealed partial class OutpostSimulation
    {
        /// <summary>Число вариантов окраса котика (Look = (Id − 1) mod LookCount).</summary>
        public const int LookCount = 12;

        readonly OutpostGameState m_State;
        readonly OutpostMap m_Map;
        readonly OutpostConfigSet m_Configs;
        readonly GridNavigation m_Nav;
        /// <summary>Клетка → Id живой цели с footprint или −1.</summary>
        readonly int[] m_Occupant;
        /// <summary>Клетка → WorkerId добывающего (MovingToWork/Mining) или 0.</summary>
        readonly int[] m_WorkCellOwner;
        /// <summary>Индекс цели → число назначенных добывающих.</summary>
        readonly int[] m_Assigned;
        readonly int[] m_BranchAssigned;
        int m_LivingRsx;
        TeamStats m_Stats;
        /// <summary>Каталог карточек (null — без карточек: нет сундуков, учебной карты и бонусов).</summary>
        readonly CardCatalog m_Cards;
        /// <summary>Индекс цели → урон удара котика и цена новой порции при текущих Stats.</summary>
        readonly long[] m_TargetDamage;
        readonly BigInteger[] m_TargetCoin;
        readonly ulong m_CritSalt;
        bool m_StatsDirty;
        long m_ProcessedEvents;

        readonly List<OutpostEvent> m_Events = new List<OutpostEvent>();
        readonly List<ScheduledCommand> m_Scheduled = new List<ScheduledCommand>();
        long m_ScheduleSeq;

        // Переиспользуемые буферы горячего пути.
        readonly List<WorkerState> m_Hitters = new List<WorkerState>();
        readonly List<CargoUnit> m_NewUnits = new List<CargoUnit>();
        static readonly Comparison<WorkerState> s_ByTargetThenId =
            (a, b) => a.TargetId != b.TargetId ? a.TargetId.CompareTo(b.TargetId) : a.Id.CompareTo(b.Id);

        /// <summary>Записывать события для представления (бот и офлайн отключают).</summary>
        public bool RecordEvents = true;

        /// <summary>Результаты команд, выполненных из расписания (фаза 6), по порядку.</summary>
        public readonly List<CommandResult> ScheduledResults = new List<CommandResult>();

        public OutpostSimulation(OutpostGameState state, OutpostMap map, OutpostConfigSet configs)
        {
            m_State = state ?? throw new ArgumentNullException(nameof(state));
            m_Map = map ?? throw new ArgumentNullException(nameof(map));
            m_Configs = configs ?? throw new ArgumentNullException(nameof(configs));
            var run = state.Run ?? throw new ArgumentException("Нет состояния шахты (Run)", nameof(state));
            if (state.Profile == null) throw new ArgumentException("Нет профиля", nameof(state));
            if (run.Balance == null) throw new ArgumentException("Run без снимка баланса", nameof(state));
            if (run.ZoneId != map.ZoneId) throw new ArgumentException("Карта " + map.ZoneId + " не соответствует run " + run.ZoneId);
            if (!ZoneOverride.SameAs(run.MapOverride, map.Override))
                throw new ArgumentException("Карта " + map.ZoneId + " построена с override " + (map.OverrideName ?? "—") + ", run — с " + (run.MapOverride?.Name ?? "—"));
            if (OutpostJson.Serialize(run.MapTopology) != OutpostJson.Serialize(map.Topology))
                throw new ArgumentException("Run topology snapshot does not match map");
            if (OutpostJson.Serialize(run.MapDressing) != OutpostJson.Serialize(map.Dressing))
                throw new ArgumentException("Run dressing snapshot does not match map");
            if (OutpostJson.Serialize(run.MapFinal) != OutpostJson.Serialize(map.Final))
                throw new ArgumentException("Run final excavation snapshot does not match map");
            if (run.Targets.Count != map.Targets.Length) throw new ArgumentException("Число целей run не совпадает с картой");
            // Снимок до мест разгрузки: уровень новой ветки (0) дополняется здесь же, как при загрузке.
            OutpostStateUpgrade.NormalizeRun(run);

            m_Occupant = (int[])map.InitialOccupant.Clone();
            m_WorkCellOwner = new int[map.CellCount];
            m_Assigned = new int[map.Targets.Length];
            int maxBranch = 0;
            foreach (var target in map.Targets) maxBranch = Math.Max(maxBranch, target.Branch);
            m_BranchAssigned = new int[maxBranch + 1];
            for (int i = 0; i < map.Targets.Length; i++)
            {
                var ts = run.Targets[i];
                var mt = map.Targets[i];
                if (ts.Id != mt.Id) throw new ArgumentException("Цель run " + ts.Id + " не совпадает с картой " + mt.Id);
                if (ts.Destroyed)
                {
                    // ТЗ 2.2 §4.1: footprint открытого завода остаётся зданием — постоянной стеной навигации.
                    if (!IsStandingFactory(mt))
                        foreach (int cell in mt.Cells) m_Occupant[cell] = -1;
                }
                else if (!mt.IsGate) m_LivingRsx++;
            }
            bool repairedWork = false;
            foreach (var w in run.Workers)
            {
                if (w.WorkCell < 0) continue;
                int ti = map.TargetIndex(w.TargetId);
                // Never let a duplicate or stale saved reservation overwrite the first owner's cell.
                if (w.WorkCell >= map.CellCount || ti < 0 || run.Targets[ti].Destroyed ||
                    Array.IndexOf(map.Targets[ti].WorkCells, w.WorkCell) < 0 || m_WorkCellOwner[w.WorkCell] != 0 ||
                    w.Mode != WorkerMode.MovingToWork && w.Mode != WorkerMode.Mining)
                {
                    w.WorkCell = -1;
                    if (w.Mode == WorkerMode.MovingToWork || w.Mode == WorkerMode.Mining)
                    {
                        w.Mode = WorkerMode.ChoosingTask; w.Action = null; w.Path = null; w.PathIndex = 0;
                        w.TargetId = -1; w.TaskInvalid = false; w.MoveRetryAttempted = false;
                    }
                    repairedWork = true; run.PlanningDirty = true;
                    continue;
                }
                m_WorkCellOwner[w.WorkCell] = w.Id;
                m_Assigned[ti]++;
            }
            m_Nav = new GridNavigation(map, m_Occupant);
            m_Cards = configs.Cards;
            m_TargetDamage = new long[map.Targets.Length];
            m_TargetCoin = new BigInteger[map.Targets.Length];
            m_CritSalt = CritSalt(state.Profile.Seed, run.RunId);
            m_Stats = ComputeStats();
            RefreshTargetCaches();
            InitProgression();
            // Первая фаза 7 после загрузки сверит длительности таймеров с характеристиками (идемпотентно).
            m_StatsDirty = true;
            // ТЗ 2.2 §4.4: при загрузке и пересоздании — та же проверка требования автодоставки. Снимок, записанный ядром, она не
            // меняет; изменение даёт только выдача карточек вне ядра (DevTools «Cards»).
            ReconstructFactoryActivationHistory();
            SyncFactoryAutomation(run.Now);
            if (repairedWork || run.MapFinal?.MigratedFromVersion != null && run.PlanningDirty) PlanNow();
        }

        public OutpostGameState State => m_State;
        public OutpostMap Map => m_Map;
        public OutpostConfigSet Configs => m_Configs;
        public OutpostRun Run => m_State.Run;
        public OutpostProfile Profile => m_State.Profile;
        public OutpostBalanceConfig Balance => m_State.Run.Balance;
        /// <summary>Текущие эффективные характеристики (пересчитываются в фазе 7 после команд).</summary>
        public TeamStats Stats => m_Stats;
        public long Now => m_State.Run.Now;
        public bool IsEconomyStopped => m_State.Run.Phase == RunPhase.ReadyToContinue;
        /// <summary>Накопленные события с последнего <see cref="DrainEvents"/>.</summary>
        public List<OutpostEvent> Events => m_Events;
        /// <summary>Всего обработано событий этим экземпляром.</summary>
        public long ProcessedEvents => m_ProcessedEvents;

        /// <summary>Навигация текущей версии (синхронизирована с NavVersion и барьером).</summary>
        public GridNavigation Navigation
        {
            get
            {
                SyncNav();
                return m_Nav;
            }
        }

        /// <summary>Строка барьера: строка очередных закрытых ворот или 0.</summary>
        public int BarrierRow => m_Map.Topology == null && Run.GatesOpened < m_Map.Gates.Length ? m_Map.Gates[Run.GatesOpened].Row : 0;

        public void DrainEvents(List<OutpostEvent> into)
        {
            if (into != null) into.AddRange(m_Events);
            m_Events.Clear();
        }

        /// <summary>
        /// Ближайшая отметка: минимум сроков таймеров работников, запланированных команд и заводов (концы циклов и прибытия партий —
        /// только в Running/DrainingCargo, ТЗ 2.2 §4.5); long.MaxValue — событий нет.
        /// </summary>
        public long NextEventTick()
        {
            long min = long.MaxValue;
            var workers = Run.Workers;
            for (int i = 0; i < workers.Count; i++)
            {
                var a = workers[i].Action;
                if (IsMoving(workers[i]) && !HasAdvancingMove(workers[i]))
                {
                    var w = workers[i];
                    long retry = w.MoveRetryAttempted
                        ? checked(Run.LastChangeTick + Balance.WatchdogTicks + 1)
                        : checked(w.LastMoveProgressTick + Balance.MoveStuckTicks);
                    retry = Math.Max(Run.Now, retry);
                    if (retry < min) min = retry;
                    continue;
                }
                if (a != null && a.Deadline < min) min = a.Deadline;
            }
            if (m_Scheduled.Count > 0 && m_Scheduled[0].Tick < min) min = m_Scheduled[0].Tick;
            long factory = NextFactoryEventTick();
            if (factory < min) min = factory;
            long damageBonus = NextDamageBonusTick();
            if (damageBonus < min) min = damageBonus;
            return min;
        }

        static bool IsMoving(WorkerState worker) => worker.Mode == WorkerMode.MovingToWork ||
            worker.Mode == WorkerMode.MovingToCargo || worker.Mode == WorkerMode.Returning || worker.Mode == WorkerMode.GoingHome;

        // Любое корректное ребро продвигается непрерывно по точной доле, даже при длительности > MoveStuckTicks.
        // Потерянный таймер/некорректное ребро нельзя принять за движение или выполнять бесконечно на одной отметке.
        bool HasAdvancingMove(WorkerState worker)
        {
            var action = worker.Action;
            if (action == null || action.Kind != ActionKind.Edge || action.Duration <= 0 ||
                worker.Path == null || worker.PathIndex < 0 || worker.PathIndex >= worker.Path.Length) return false;
            int next = worker.Path[worker.PathIndex];
            if (!Navigation.Passable(next)) return false;
            int distance = Math.Abs(next / m_Map.Width - worker.Cell / m_Map.Width) +
                Math.Abs(next % m_Map.Width - worker.Cell % m_Map.Width);
            return distance == 1;
        }

        /// <summary>
        /// Продвинуть время до targetTick: пока ближайшая отметка ≤ targetTick — ProcessTick; затем Now = targetTick.
        /// maxTicksToProcess ограничивает число отметок (порционный офлайн); при остановке лимитом Now не двигается дальше
        /// последней обработанной отметки. Advance(a+b) ≡ Advance(a); Advance(b).
        /// </summary>
        public AdvanceResult Advance(long targetTick, int maxTicksToProcess = int.MaxValue)
        {
            var result = new AdvanceResult();
            long eventsBefore = m_ProcessedEvents;
            while (true)
            {
                long t = NextEventTick();
                if (t == long.MaxValue)
                {
                    if (!IsEconomyStopped) FireWatchdog(Run.Now, "нет запланированных событий при незавершённой карте");
                    break;
                }
                if (t > targetTick) break;
                if (result.ProcessedTicks >= maxTicksToProcess)
                {
                    result.Reached = false;
                    result.ProcessedEvents = m_ProcessedEvents - eventsBefore;
                    return result;
                }
                ProcessTick(t);
                result.ProcessedTicks++;
            }
            if (targetTick > Run.Now)
            {
                AdvanceDamageBonus(targetTick);
                Run.Now = targetTick;
            }
            result.Reached = true;
            result.ProcessedEvents = m_ProcessedEvents - eventsBefore;
            return result;
        }

        /// <summary>Продвинуть ровно до ближайшего события (бот, тесты). false — событий нет.</summary>
        public bool AdvanceToNextEvent()
        {
            long t = NextEventTick();
            if (t == long.MaxValue)
            {
                if (!IsEconomyStopped) FireWatchdog(Run.Now, "нет запланированных событий при незавершённой карте");
                return false;
            }
            Advance(t);
            return true;
        }

        /// <summary>Фаза 7 на текущей отметке: первичное назначение задач новой шахты.</summary>
        internal void PlanNow() => Plan(Run.Now);

        // ---------- Поиск ----------

        public WorkerState WorkerById(int id)
        {
            var list = Run.Workers;
            if (id >= 1 && id <= list.Count && list[id - 1].Id == id) return list[id - 1];
            for (int i = 0; i < list.Count; i++)
                if (list[i].Id == id) return list[i];
            return null;
        }

        public TargetState TargetById(int id)
        {
            int i = m_Map.TargetIndex(id);
            return i < 0 ? null : Run.Targets[i];
        }

        public CargoBatch BatchBySeq(long seq)
        {
            var list = Run.Batches;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Seq == seq) return list[i];
            return null;
        }

        /// <summary>Живых r/s/x на карте.</summary>
        public int LivingMiningTargets => m_LivingRsx;

        /// <summary>WorkerId добывающего, закрепившего клетку, или 0.</summary>
        public int WorkCellOwner(int cell) => cell >= 0 && cell < m_WorkCellOwner.Length ? m_WorkCellOwner[cell] : 0;

        /// <summary>Число назначенных добывающих цели.</summary>
        public int AssignedTo(int targetId)
        {
            int i = m_Map.TargetIndex(targetId);
            return i < 0 ? 0 : m_Assigned[i];
        }

        /// <summary>Занята ли клетка живым footprint.</summary>
        public bool IsOccupied(int cell) => m_Occupant[cell] >= 0;

        // ---------- Внутреннее ----------

        TeamStats ComputeStats() => TeamStats.Compute(Run.Balance, Run.Levels, Profile, m_Configs.Equipment, Run.Zone, m_Cards, Run.ExtraWorkers);

        /// <summary>Урон и цена порции по каждой цели при текущих Stats (бонусы карточек к виду и типу цели).</summary>
        void RefreshTargetCaches()
        {
            var targets = m_Map.Targets;
            for (int i = 0; i < targets.Length; i++)
            {
                var mt = targets[i];
                m_TargetDamage[i] = m_Stats.DamageUnitsFor(mt.Kind, mt.TypeId);
                m_TargetCoin[i] = m_Stats.CoinValueFor(mt.Kind, mt.TypeId, Run.Targets[i].ValueFactor);
            }
        }

        static ulong CritSalt(ulong seed, string runId)
        {
            ulong h = 1469598103934665603UL ^ seed;
            if (runId != null)
                foreach (char c in runId)
                {
                    h ^= c;
                    h *= 1099511628211UL;
                }
            return h;
        }

        /// <summary>Детерминированный бросок крита [0, 10^6) по (seed профиля, run, котик, отметка): одинаков онлайн, офлайн и после загрузки.</summary>
        int CritRoll(int workerId, long tick)
        {
            ulong z = m_CritSalt ^ ((ulong)(uint)workerId * 0x9E3779B97F4A7C15UL) ^ ((ulong)tick * 0xBF58476D1CE4E5B9UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z % (ulong)CardRules.ChanceScale);
        }

        void SyncNav()
        {
            if (m_Map.Topology == null) m_Nav.Sync(Run.NavVersion, BarrierRow);
            else m_Nav.SyncGates(Run.NavVersion, Run.GatesOpened);
        }

        void Touch(long t) => Run.LastChangeTick = t;

        void Emit(OutpostEvent e)
        {
            if (RecordEvents) m_Events.Add(e);
        }

        void SetMode(WorkerState w, WorkerMode mode, long t)
        {
            if (w.Mode == mode) return;
            w.Mode = mode;
            if (!RecordEvents) return;
            var e = OutpostEvent.Create(OutpostEventType.WorkerModeChanged, t);
            e.WorkerId = w.Id;
            e.Cell = w.Cell;
            e.TargetId = w.TargetId;
            e.Mode = mode;
            m_Events.Add(e);
        }

        static void SetTimer(WorkerState w, ActionKind kind, long start, long duration)
        {
            var a = w.Action;
            if (a == null)
            {
                a = new TimedAction();
                w.Action = a;
            }
            a.Kind = kind;
            a.SegmentStart = start;
            a.P0 = Rational.Zero;
            a.Duration = duration;
            a.Deadline = start + duration;
        }

        static void InsertSorted(List<CargoUnit> list, CargoUnit u)
        {
            int i = list.Count;
            while (i > 0 && list[i - 1].CompareKey(u) > 0) i--;
            list.Insert(i, u);
        }

        void FireWatchdog(long t, string reason)
        {
            var run = Run;
            if (run.WatchdogFired || run.Phase == RunPhase.ReadyToContinue) return;
            run.WatchdogFired = true;
            run.WatchdogDiagnostic = BuildDiagnostic(t, reason);
            var e = OutpostEvent.Create(OutpostEventType.WatchdogFired, t);
            Emit(e);
        }

        /// <summary>Диагностика: runId, зона, живые цели сектора, резервы, барьер, блокирующие клетки, режимы работников.</summary>
        public string BuildDiagnostic(long t, string reason)
        {
            var run = Run;
            var sb = new System.Text.StringBuilder();
            sb.Append("watchdog ").Append(run.RunId).Append(' ').Append(run.ZoneId).Append(" t=").Append(t)
              .Append(" phase=").Append(run.Phase).Append(" reason=").Append(reason)
              .Append(" barrierRow=").Append(BarrierRow).Append(" gates=").Append(run.GatesOpened).Append('/').Append(m_Map.Gates.Length)
              .Append(" livingRsx=").Append(m_LivingRsx).Append(" batches=").Append(run.Batches.Count).Append(" queue=").Append(run.Queue.Count);
            sb.Append(" sectorTargets=[");
            int shown = 0;
            for (int i = 0; i < m_Map.Targets.Length && shown < 40; i++)
            {
                var mt = m_Map.Targets[i];
                if (run.Targets[i].Destroyed) continue;
                if (mt.IsGate ? mt.Sector != run.GatesOpened : mt.Sector != run.GatesOpened) continue;
                sb.Append(mt.Id).Append(mt.Kind).Append("(assigned=").Append(m_Assigned[i]).Append(" workCells=");
                SyncNav();
                foreach (int wc in mt.WorkCells)
                {
                    string st = !m_Nav.Passable(wc) ? (m_Occupant[wc] >= 0 ? "occ" + m_Occupant[wc] : "bar") :
                        m_WorkCellOwner[wc] != 0 ? "w" + m_WorkCellOwner[wc] :
                        m_Nav.Distance(m_Map.IntakeCell, wc) < 0 ? "unreach" : "ok";
                    sb.Append(wc).Append(':').Append(st).Append(',');
                }
                sb.Append(") ");
                shown++;
            }
            sb.Append("] workers=[");
            foreach (var w in run.Workers)
            {
                sb.Append(w.Id).Append(':').Append(w.Mode).Append("@").Append(w.Cell).Append(" task=").Append(w.TaskId)
                  .Append(" target=").Append(w.TargetId).Append(" bag=").Append(w.Bag.Count).Append(" res=").Append(w.CargoBatchSeq)
                  .Append(" slot=").Append(w.DepotSlot).Append(" deadline=").Append(w.Action?.Deadline ?? -1).Append("; ");
            }
            sb.Append(']');
            return sb.ToString();
        }

        sealed class ScheduledCommand
        {
            public long Tick;
            public long Seq;
            public OutpostCommand Command;
        }
    }
}
