using System;
using System.Numerics;

namespace CatMiners.Core.Outpost
{
    public sealed partial class OutpostSimulation
    {
        /// <summary>
        /// Выпуск порций Released..expected−1 с фиксированной стоимостью по текущим Stats (ТЗ 7.1–7.3):
        /// сначала свободные места сумок ударивших по Id, остаток — новая партия в рабочей клетке ударившего с наименьшим Id.
        /// </summary>
        void ReleaseCargo(TargetState ts, int expected, int from, int to, long t)
        {
            var run = Run;
            int count = expected - ts.ReleasedUnits;
            var value = m_TargetCoin[m_Map.TargetIndex(ts.Id)];
            m_NewUnits.Clear();
            for (int ord = ts.ReleasedUnits; ord < expected; ord++)
                m_NewUnits.Add(new CargoUnit { Source = ts.Id, Ordinal = ord, Value = value });
            ts.ReleasedUnits = expected;
            run.Counters.ReleasedUnits += count;
            run.Counters.ReleasedValue += value * count;

            int next = 0;
            int capacity = m_Stats.Capacity;
            for (int k = from; k < to && next < count; k++)
            {
                var w = m_Hitters[k];
                int free = Math.Max(0, capacity - w.Bag.Count);
                while (free > 0 && next < count)
                {
                    InsertSorted(w.Bag, m_NewUnits[next++]);
                    free--;
                }
            }
            long batchSeq = -1;
            int cell = m_Hitters[from].WorkCell;
            if (next < count)
            {
                if (cell < 0 || !Navigation.Passable(cell))
                    throw new InvalidOperationException("Ядро: клетка партии " + cell + " недоступна (цель " + ts.Id + ")");
                var batch = new CargoBatch
                {
                    Seq = run.NextBatchSeq++,
                    Cell = cell,
                    CreatedTick = t,
                };
                batch.Key = OutpostIds.BatchKey(batch.Seq);
                for (int i = next; i < count; i++) batch.Units.Add(m_NewUnits[i]);
                run.Batches.Add(batch);
                batchSeq = batch.Seq;
                run.PlanningDirty = true;
            }
            m_NewUnits.Clear();
            if (RecordEvents)
            {
                var e = OutpostEvent.Create(OutpostEventType.CargoReleased, t);
                e.TargetId = ts.Id;
                e.WorkerId = m_Hitters[from].Id;
                e.Cell = cell;
                e.Count = count;
                e.BatchSeq = batchSeq;
                e.Value = value;
                m_Events.Add(e);
            }
        }

        /// <summary>Подбор одной порции своего резерва (наименьшая по ключу), затем следующий подбор или новая задача.</summary>
        void PickOne(WorkerState w, long t)
        {
            var run = Run;
            var batch = BatchBySeq(w.CargoBatchSeq);
            int idx = -1;
            if (batch != null)
                for (int i = 0; i < batch.Units.Count; i++)
                    if (batch.Units[i].ReservedBy == w.Id)
                    {
                        idx = i;
                        break;
                    }
            if (idx < 0)
            {
                NeedTask(w, t);
                return;
            }
            var unit = batch.Units[idx];
            batch.Units.RemoveAt(idx);
            unit.ReservedBy = 0;
            unit.ReservedTask = 0;
            InsertSorted(w.Bag, unit);
            Touch(t);
            if (RecordEvents)
            {
                var e = OutpostEvent.Create(OutpostEventType.CargoPickedUp, t);
                e.WorkerId = w.Id;
                e.BatchSeq = batch.Seq;
                e.Cell = batch.Cell;
                e.TargetId = unit.Source;
                e.Count = 1;
                e.Value = unit.Value;
                m_Events.Add(e);
            }
            bool more = false;
            for (int i = 0; i < batch.Units.Count; i++)
                if (batch.Units[i].ReservedBy == w.Id)
                {
                    more = true;
                    break;
                }
            if (batch.Units.Count == 0) run.Batches.Remove(batch);
            if (more && w.Bag.Count < m_Stats.Capacity) SetTimer(w, ActionKind.Pickup, t, m_Stats.PickupTicks);
            else NeedTask(w, t);
        }

        /// <summary>Сдача первой порции сумки на месте склада: единственное место зачисления золота (ТЗ 7.3, 7.4).</summary>
        void DepositOne(WorkerState w, int slot, long t)
        {
            var run = Run;
            var unit = w.Bag[0];
            w.Bag.RemoveAt(0);
            run.Wallet += unit.Value;
            run.TotalCredited += unit.Value;
            run.Counters.DeliveredUnits++;
            run.Counters.DeliveredValue += unit.Value;
            bool firstDelivery = run.Counters.FirstDeliveryTick < 0;
            if (firstDelivery) run.Counters.FirstDeliveryTick = t;
            Touch(t);
            if (m_Cards != null) TryTutorialGrant(t);
            // Первая сданная порция открывает «Кирки» (Mining Power) — первая сессия.
            if (firstDelivery) SyncProgression(t);
            if (RecordEvents)
            {
                var e = OutpostEvent.Create(OutpostEventType.CargoDeposited, t);
                e.WorkerId = w.Id;
                e.Slot = slot;
                e.TargetId = unit.Source;
                e.Count = 1;
                e.Value = unit.Value;
                e.Cell = m_Map.DepotCells[slot];
                m_Events.Add(e);
            }
            if (w.Bag.Count > 0) SetTimer(w, ActionKind.Unload, t, m_Stats.UnloadTicks);
            else NeedTask(w, t);
        }

        /// <summary>Снять оставшиеся резервы работника в его партии.</summary>
        void ReleaseReservation(WorkerState w)
        {
            if (w.CargoBatchSeq < 0) return;
            var batch = BatchBySeq(w.CargoBatchSeq);
            if (batch != null)
                for (int i = 0; i < batch.Units.Count; i++)
                {
                    var u = batch.Units[i];
                    if (u.ReservedBy != w.Id) continue;
                    u.ReservedBy = 0;
                    u.ReservedTask = 0;
                    Run.PlanningDirty = true;
                }
            w.CargoBatchSeq = -1;
        }

        /// <summary>
        /// Задача подбора (ТЗ 6.3 п.2): партия со свободными порциями по (путь, Seq, Key, клетка); резерв
        /// min(свободное место, свободные порции) наименьших по ключу — при назначении, до пути.
        /// </summary>
        bool TryAssignCargo(WorkerState w, long t)
        {
            var run = Run;
            if (run.Batches.Count == 0) return false;
            var field = Navigation.From(w.Cell);
            CargoBatch best = null;
            int bestDist = 0;
            for (int i = 0; i < run.Batches.Count; i++)
            {
                var b = run.Batches[i];
                int d = field.Dist[b.Cell];
                if (d < 0 || b.FreeCount() == 0) continue;
                if (best == null || d < bestDist || (d == bestDist && CompareBatch(b, best) < 0))
                {
                    best = b;
                    bestDist = d;
                }
            }
            if (best == null) return false;
            int k = Math.Min(m_Stats.Capacity - w.Bag.Count, best.FreeCount());
            if (k <= 0) return false;
            long task = run.NextTaskId++;
            w.TaskId = task;
            w.TargetId = -1;
            w.CargoBatchSeq = best.Seq;
            w.TaskInvalid = false;
            for (int i = 0; i < best.Units.Count && k > 0; i++)
            {
                var u = best.Units[i];
                if (u.ReservedBy != 0) continue;
                u.ReservedBy = w.Id;
                u.ReservedTask = task;
                k--;
            }
            Touch(t);
            if (bestDist == 0)
            {
                w.Path = null;
                w.PathIndex = 0;
                SetMode(w, WorkerMode.PickingUp, t);
                SetTimer(w, ActionKind.Pickup, t, m_Stats.PickupTicks);
            }
            else StartPath(w, WorkerMode.MovingToCargo, m_Nav.BuildPath(w.Cell, best.Cell), t);
            return true;
        }

        static int CompareBatch(CargoBatch a, CargoBatch b)
        {
            if (a.Seq != b.Seq) return a.Seq.CompareTo(b.Seq);
            int c = string.CompareOrdinal(a.Key, b.Key);
            return c != 0 ? c : a.Cell.CompareTo(b.Cell);
        }
    }
}
