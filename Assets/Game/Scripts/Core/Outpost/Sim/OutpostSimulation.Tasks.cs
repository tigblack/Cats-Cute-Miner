using System;

namespace CatMiners.Core.Outpost
{
    public sealed partial class OutpostSimulation
    {
        /// <summary>
        /// Фаза 7 (ТЗ 6.5 п.7): (a) пересчёт Stats и остатка рёбер/обслуживания при смене длительности;
        /// (b) задачи нуждающимся (и Idle, если PlanningDirty) по Id; (c) свободные места склада — очереди FIFO.
        /// После фазы ни один работник не остаётся в ChoosingTask.
        /// </summary>
        void Plan(long t)
        {
            var run = Run;
            bool statsChanged = m_StatsDirty;
            if (statsChanged) RecomputeStats(t);
            if (run.Phase == RunPhase.ReadyToContinue)
            {
                run.PlanningDirty = false;
                return;
            }
            if (statsChanged) StopOverCapacity(t);
            RecoverStuckMoves(t);
            bool dirty = run.PlanningDirty;
            var workers = run.Workers;
            for (int i = 0; i < workers.Count; i++)
            {
                var w = workers[i];
                // Идущий домой пустой котик тоже ждёт работы. Свободный груз будит его в ближайшей клетке,
                // сохраняя текущее ребро: окно/смена рюкзака не телепортирует работника (ТЗ 6.3–6.4).
                if (dirty && w.Mode == WorkerMode.GoingHome && !w.TaskInvalid && HasReachableCargo(w))
                    w.TaskInvalid = true;
                if (w.Mode == WorkerMode.ChoosingTask || (dirty && w.Mode == WorkerMode.Idle)) ChooseTask(w, t);
            }
            AssignDepotSlots(t);
            run.PlanningDirty = false;
        }

        bool HasReachableCargo(WorkerState worker)
        {
            if (Run.Batches.Count == 0) return false;
            var field = Navigation.From(worker.Cell);
            foreach (var batch in Run.Batches)
                if (batch.FreeCount() > 0 && field.Dist[batch.Cell] >= 0) return true;
            return false;
        }

        /// <summary>Один пересчёт пути после 3 с без продвижения; корректные таймеры никогда не прерываются (§6.4).</summary>
        void RecoverStuckMoves(long tick)
        {
            foreach (var worker in Run.Workers)
            {
                if (!IsMoving(worker) || HasAdvancingMove(worker) || worker.MoveRetryAttempted ||
                    tick - worker.LastMoveProgressTick < Balance.MoveStuckTicks) continue;
                worker.MoveRetryAttempted = true;
                int destination = -1;
                if (!worker.TaskInvalid)
                {
                    switch (worker.Mode)
                    {
                        case WorkerMode.MovingToWork:
                            var target = TargetById(worker.TargetId);
                            if (target != null && !target.Destroyed) destination = worker.WorkCell;
                            break;
                        case WorkerMode.MovingToCargo:
                            var batch = BatchBySeq(worker.CargoBatchSeq);
                            if (batch != null)
                                foreach (var unit in batch.Units)
                                    if (unit.ReservedBy == worker.Id && unit.ReservedTask == worker.TaskId)
                                    { destination = batch.Cell; break; }
                            break;
                        case WorkerMode.Returning:
                            destination = worker.DepotSlot >= 0 && worker.DepotSlot < m_Map.DepotCells.Length
                                ? m_Map.DepotCells[worker.DepotSlot] : m_Map.IntakeCell;
                            break;
                        case WorkerMode.GoingHome:
                            if (worker.Path != null && worker.Path.Length > 0) destination = worker.Path[worker.Path.Length - 1];
                            break;
                    }
                }
                if (destination < 0 || destination >= m_Map.CellCount || !Navigation.Passable(destination))
                { NeedTask(worker, tick); continue; }
                var path = Navigation.BuildPath(worker.Cell, destination);
                if (path == null) { NeedTask(worker, tick); continue; }
                if (path.Length == 0) { OnArrive(worker, tick); continue; }
                StartPath(worker, worker.Mode, path, tick);
                worker.MoveRetryAttempted = true;
                Touch(tick);
            }
        }

        /// <summary>
        /// Пересчёт характеристик. У Edge/Unload при смене длительности: p = P0 + (t − SegmentStart)/Duration (точно);
        /// p &lt; 1 → P0 = p, SegmentStart = t, Duration = D, Deadline = t + ceil((1 − p)·D) (ТЗ 6.5).
        /// </summary>
        void RecomputeStats(long t)
        {
            m_StatsDirty = false;
            var stats = ComputeStats();
            m_Stats = stats;
            RefreshTargetCaches();
            var workers = Run.Workers;
            for (int i = 0; i < workers.Count; i++)
            {
                var a = workers[i].Action;
                if (a == null) continue;
                long d = a.Kind == ActionKind.Edge ? stats.EdgeTicks : a.Kind == ActionKind.Unload ? stats.UnloadTicks : 0;
                if (d <= 0 || a.Duration == d) continue;
                var p = a.ProgressAt(t);
                if (p >= Rational.One) continue;
                a.P0 = p;
                a.SegmentStart = t;
                a.Duration = d;
                a.Deadline = t + ExactMath.ToLongChecked(((Rational.One - p) * d).Ceil(), "Deadline");
            }
        }

        /// <summary>
        /// Вместимость уменьшилась (смена/снятие рюкзака): при Bag.Count ≥ Capacity подбор и добыча запрещены до доставки
        /// (ТЗ 6.2: Mining — пока есть место в сумке; 6.3 п.1; 10.3: груз сохраняется, котик завершает доставку).
        /// Добывающий и подбирающий стоят на месте — сразу нуждаются в задаче (резерв и рабочая клетка снимаются).
        /// Идущий к работе/грузу не телепортируется: задача помечается некорректной, остановка в ближайшей клетке.
        /// При обычной работе условие не выполняется (сумка полна → задача уже снята), поэтому проверка идемпотентна.
        /// </summary>
        void StopOverCapacity(long t)
        {
            int capacity = m_Stats.Capacity;
            var workers = Run.Workers;
            for (int i = 0; i < workers.Count; i++)
            {
                var w = workers[i];
                if (w.Bag.Count < capacity) continue;
                switch (w.Mode)
                {
                    case WorkerMode.Mining:
                    case WorkerMode.PickingUp:
                        NeedTask(w, t);
                        Touch(t);
                        break;
                    case WorkerMode.MovingToWork:
                        if (w.TaskInvalid) break;
                        w.TaskInvalid = true;
                        ReleaseWorkCell(w);
                        Touch(t);
                        break;
                    case WorkerMode.MovingToCargo:
                        // Резерв держится до ближайшей клетки (инвариант MovingToCargo ⇒ резерв), затем снимается в NeedTask.
                        if (w.TaskInvalid) break;
                        w.TaskInvalid = true;
                        Run.PlanningDirty = true;
                        Touch(t);
                        break;
                }
            }
        }

        /// <summary>Выбор задачи работником в клетке Cell (ТЗ 6.3, CORE_SPEC §7.2).</summary>
        void ChooseTask(WorkerState w, long t)
        {
            var run = Run;
            // 1. Полная сумка — на базу.
            if (w.Bag.Count >= m_Stats.Capacity)
            {
                StartReturn(w, t);
                return;
            }
            // 2. Свободный груз.
            if (TryAssignCargo(w, t)) return;
            // 3. Добыча (только Running).
            if (run.Phase == RunPhase.Running && TryAssignMining(w, t)) return;
            // 4. Частичный груз — сдать.
            if (w.Bag.Count > 0)
            {
                StartReturn(w, t);
                return;
            }
            // 5. Ожидание либо возвращение в лагерь.
            if (run.Phase == RunPhase.Running) SetIdle(w, t);
            else GoHome(w, t);
        }

        /// <summary>
        /// Задача добычи: живые r/s/x сектора GatesOpened, иначе очередные ворота. Пары (цель, рабочая клетка):
        /// клетка проходима (без footprint, не за барьером), достижима, свободна от другого добывающего, у цели меньше
        /// MaxWorkersPerTarget назначенных. Новые карты сначала учитывают занятость ветви, затем путь, назначено, Id цели, клетка.
        /// </summary>
        bool TryAssignMining(WorkerState w, long t)
        {
            var run = Run;
            var nav = Navigation;
            var field = nav.From(w.Cell);
            int sector = run.GatesOpened;
            int maxPerTarget = run.Balance.MaxWorkersPerTarget;
            int bestTi = -1, bestCell = -1, bestDist = 0, bestScore = 0, bestAssigned = 0, bestId = 0;
            bool anyLiving = false;
            var targets = m_Map.Targets;
            int crowding = m_Map.Topology?.BranchCrowdingPenalty ?? 0;
            if (crowding > 0)
            {
                Array.Clear(m_BranchAssigned, 0, m_BranchAssigned.Length);
                for (int i = 0; i < targets.Length; i++)
                    if (targets[i].Branch > 0) m_BranchAssigned[targets[i].Branch] += m_Assigned[i];
            }

            for (int i = 0; i < targets.Length; i++)
            {
                var mt = targets[i];
                if (mt.IsGate || mt.Sector != sector || run.Targets[i].Destroyed) continue;
                anyLiving = true;
                Consider(i);
            }
            if (!anyLiving && sector < m_Map.Gates.Length)
            {
                int gi = m_Map.TargetIndex(m_Map.Gates[sector].TargetId);
                if (!run.Targets[gi].Destroyed) Consider(gi);
            }
            if (bestTi < 0) return false;

            long task = run.NextTaskId++;
            w.TaskId = task;
            w.TargetId = targets[bestTi].Id;
            w.WorkCell = bestCell;
            w.TaskInvalid = false;
            w.CargoBatchSeq = -1;
            m_WorkCellOwner[bestCell] = w.Id;
            m_Assigned[bestTi]++;
            Touch(t);
            if (bestDist == 0) StartMining(w, t);
            else StartPath(w, WorkerMode.MovingToWork, nav.BuildPath(w.Cell, bestCell), t);
            return true;

            void Consider(int ti)
            {
                int assigned = m_Assigned[ti];
                if (assigned >= maxPerTarget) return;
                var mt = targets[ti];
                foreach (int wc in mt.WorkCells)
                {
                    if (m_WorkCellOwner[wc] != 0 || !nav.Passable(wc)) continue;
                    int d = field.Dist[wc];
                    if (d < 0) continue;
                    int score = d + (mt.Branch > 0 ? crowding * m_BranchAssigned[mt.Branch] : 0);
                    if (bestTi >= 0)
                    {
                        if (score > bestScore) continue;
                        if (score == bestScore)
                        {
                            if (d > bestDist) continue;
                            if (d == bestDist)
                            {
                                if (assigned > bestAssigned) continue;
                                if (assigned == bestAssigned)
                                {
                                    if (mt.Id > bestId) continue;
                                    if (mt.Id == bestId && wc >= bestCell) continue;
                                }
                            }
                        }
                    }
                    bestTi = ti;
                    bestCell = wc;
                    bestDist = d;
                    bestScore = score;
                    bestAssigned = assigned;
                    bestId = mt.Id;
                }
            }
        }

        /// <summary>Возвращение к точке приёма; в ней — сразу в очередь (ТЗ 7.4).</summary>
        void StartReturn(WorkerState w, long t)
        {
            var run = Run;
            w.TaskId = run.NextTaskId++;
            w.TargetId = -1;
            w.TaskInvalid = false;
            w.DepotSlot = -1;
            Touch(t);
            if (w.Cell == m_Map.IntakeCell)
            {
                w.Path = null;
                w.PathIndex = 0;
                SetMode(w, WorkerMode.Returning, t);
                EnterQueue(w, t);
                return;
            }
            var path = Navigation.BuildPath(w.Cell, m_Map.IntakeCell);
            if (path == null) throw new InvalidOperationException("Ядро: нет пути к базе от клетки " + w.Cell + " (работник " + w.Id + ")");
            StartPath(w, WorkerMode.Returning, path, t);
        }

        /// <summary>DrainingCargo без задач: к ближайшей клетке лагеря (декоративно), Idle по прибытии.</summary>
        void GoHome(WorkerState w, long t)
        {
            var field = Navigation.From(w.Cell);
            int best = -1, bestDist = 0;
            foreach (int cell in m_Map.CampCells)
            {
                int d = field.Dist[cell];
                if (d < 0) continue;
                if (best < 0 || d < bestDist || (d == bestDist && cell < best))
                {
                    best = cell;
                    bestDist = d;
                }
            }
            if (best < 0 || bestDist == 0)
            {
                SetIdle(w, t);
                return;
            }
            w.TaskId = Run.NextTaskId++;
            w.TargetId = -1;
            StartPath(w, WorkerMode.GoingHome, m_Nav.BuildPath(w.Cell, best), t);
        }

        void SetIdle(WorkerState w, long t)
        {
            w.Action = null;
            w.Path = null;
            w.PathIndex = 0;
            w.TaskId = 0;
            w.TargetId = -1;
            SetMode(w, WorkerMode.Idle, t);
        }

        void StartPath(WorkerState w, WorkerMode mode, int[] path, long t)
        {
            if (path == null || path.Length == 0)
                throw new InvalidOperationException("Ядро: пустой путь для " + mode + " (работник " + w.Id + ", клетка " + w.Cell + ")");
            w.Path = path;
            w.PathIndex = 0;
            w.LastMoveProgressTick = t;
            w.MoveRetryAttempted = false;
            SetMode(w, mode, t);
            SetTimer(w, ActionKind.Edge, t, m_Stats.EdgeTicks);
        }

        /// <summary>«Нуждается в задаче»: ChoosingTask, без таймера, резервы, рабочая клетка и место сняты.</summary>
        void NeedTask(WorkerState w, long t)
        {
            ReleaseWorkCell(w);
            ReleaseReservation(w);
            if (w.DepotSlot >= 0)
            {
                var slot = Run.Slots[w.DepotSlot];
                if (slot.WorkerId == w.Id)
                {
                    slot.WorkerId = 0;
                    slot.TaskId = 0;
                }
                w.DepotSlot = -1;
            }
            w.Action = null;
            w.Path = null;
            w.PathIndex = 0;
            w.TaskInvalid = false;
            w.MoveRetryAttempted = false;
            w.TargetId = -1;
            Run.PlanningDirty = true;
            SetMode(w, WorkerMode.ChoosingTask, t);
        }

        void ReleaseWorkCell(WorkerState w)
        {
            if (w.WorkCell < 0) return;
            if (m_WorkCellOwner[w.WorkCell] == w.Id) m_WorkCellOwner[w.WorkCell] = 0;
            int ti = m_Map.TargetIndex(w.TargetId);
            if (ti >= 0 && m_Assigned[ti] > 0) m_Assigned[ti]--;
            w.WorkCell = -1;
            Run.PlanningDirty = true;
        }
    }
}
