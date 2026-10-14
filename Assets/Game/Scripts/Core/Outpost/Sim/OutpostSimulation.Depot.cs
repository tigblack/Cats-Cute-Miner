namespace CatMiners.Core.Outpost
{
    public sealed partial class OutpostSimulation
    {
        /// <summary>Прибытие к точке приёма без места: FIFO по (ArrivalTick, WorkerId), ожидание не создаёт копию груза.</summary>
        void EnterQueue(WorkerState w, long t)
        {
            var run = Run;
            w.Action = null;
            w.Path = null;
            w.PathIndex = 0;
            w.DepotSlot = -1;
            SetMode(w, WorkerMode.WaitingDepot, t);
            var queue = run.Queue;
            int i = queue.Count;
            while (i > 0 && (queue[i - 1].ArrivalTick > t || (queue[i - 1].ArrivalTick == t && queue[i - 1].WorkerId > w.Id))) i--;
            queue.Insert(i, new QueueEntry { WorkerId = w.Id, ArrivalTick = t });
            if (queue.Count > run.Counters.MaxQueue) run.Counters.MaxQueue = queue.Count;
        }

        /// <summary>
        /// Фаза 7(c): свободные места (0, затем 1) — первому в очереди. Работник выходит из очереди, резервирует место
        /// (подзадача Returning с новым TaskId) и идёт к его клетке; обслуживание — после прибытия.
        /// </summary>
        void AssignDepotSlots(long t)
        {
            var run = Run;
            var slots = run.Slots;
            for (int s = 0; s < slots.Length; s++)
            {
                if (slots[s].WorkerId != 0 || run.Queue.Count == 0) continue;
                var entry = run.Queue[0];
                run.Queue.RemoveAt(0);
                var w = WorkerById(entry.WorkerId);
                run.Counters.QueueWaitTicks += t - entry.ArrivalTick;
                run.Counters.QueueServed++;
                long task = run.NextTaskId++;
                slots[s].WorkerId = w.Id;
                slots[s].TaskId = task;
                w.DepotSlot = s;
                w.TaskId = task;
                Touch(t);
                int target = m_Map.DepotCells[s];
                if (w.Cell == target)
                {
                    SetMode(w, WorkerMode.Returning, t);
                    StartUnloading(w, t);
                }
                else StartPath(w, WorkerMode.Returning, Navigation.BuildPath(w.Cell, target), t);
            }
        }

        /// <summary>Начало обслуживания на месте склада: порция за ceil(TicksPerSecond / UnloadRate).</summary>
        void StartUnloading(WorkerState w, long t)
        {
            w.Path = null;
            w.PathIndex = 0;
            if (w.Bag.Count == 0)
            {
                NeedTask(w, t);
                return;
            }
            SetMode(w, WorkerMode.Unloading, t);
            SetTimer(w, ActionKind.Unload, t, m_Stats.UnloadTicks);
        }
    }
}
