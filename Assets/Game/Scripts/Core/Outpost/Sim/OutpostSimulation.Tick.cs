using System;
using System.Numerics;

namespace CatMiners.Core.Outpost
{
    public sealed partial class OutpostSimulation
    {
        /// <summary>
        /// Одна отметка времени — строго семь фаз (ТЗ 6.5): движение, удары, подбор, склад, ворота/завершение,
        /// команды, планирование; после склада монет — шаг 4б заводов (ТЗ 2.2 §4.5: прибытия партий, затем концы циклов).
        /// Граница согласованного снимка — конец всех семи фаз.
        /// </summary>
        void ProcessTick(long t)
        {
            var run = Run;
            if (t < run.Now) throw new InvalidOperationException("Отметка " + t + " раньше текущего времени " + run.Now);
            AdvanceDamageBonus(t);
            run.Now = t;
            PhaseMovement(t);
            PhaseHits(t);
            PhasePickups(t);
            PhaseDepot(t);
            PhaseFactories(t);
            PhaseGatesAndCompletion(t);
            PhaseCommands(t);
            Plan(t);
            if (!run.WatchdogFired && run.Phase != RunPhase.ReadyToContinue && t - run.LastChangeTick > run.Balance.WatchdogTicks)
                FireWatchdog(t, "нет изменений дольше WatchdogTicks");
        }

        // ---------- Фаза 1: движение ----------

        void PhaseMovement(long t)
        {
            var workers = Run.Workers;
            for (int i = 0; i < workers.Count; i++)
            {
                var w = workers[i];
                var a = w.Action;
                if (!IsMoving(w) || !HasAdvancingMove(w) || a.Deadline != t) continue;
                m_ProcessedEvents++;
                w.Cell = w.Path[w.PathIndex++];
                w.LastMoveProgressTick = t;
                w.MoveRetryAttempted = false;
                Touch(t);
                if (w.PathIndex >= w.Path.Length) OnArrive(w, t);
                else if ((w.Mode == WorkerMode.MovingToWork || w.Mode == WorkerMode.MovingToCargo || w.Mode == WorkerMode.GoingHome) && w.TaskInvalid) NeedTask(w, t);
                else SetTimer(w, ActionKind.Edge, t, m_Stats.EdgeTicks);
            }
        }

        void OnArrive(WorkerState w, long t)
        {
            w.Path = null;
            w.PathIndex = 0;
            switch (w.Mode)
            {
                case WorkerMode.MovingToWork:
                    if (w.TaskInvalid || w.Bag.Count >= m_Stats.Capacity) NeedTask(w, t);
                    else StartMining(w, t);
                    break;
                case WorkerMode.MovingToCargo:
                    // Сумка заполнилась сверх уменьшенной вместимости — подбор запрещён до доставки (ТЗ 6.3 п.1).
                    if (w.TaskInvalid || w.Bag.Count >= m_Stats.Capacity)
                    {
                        NeedTask(w, t);
                        break;
                    }
                    SetMode(w, WorkerMode.PickingUp, t);
                    SetTimer(w, ActionKind.Pickup, t, m_Stats.PickupTicks);
                    break;
                case WorkerMode.Returning:
                    if (w.DepotSlot < 0) EnterQueue(w, t);
                    else StartUnloading(w, t);
                    break;
                case WorkerMode.GoingHome:
                    if (w.TaskInvalid) NeedTask(w, t);
                    else SetIdle(w, t);
                    break;
                default:
                    NeedTask(w, t);
                    break;
            }
        }

        void StartMining(WorkerState w, long t)
        {
            w.Path = null;
            w.PathIndex = 0;
            SetMode(w, WorkerMode.Mining, t);
            // Прибытие не даёт мгновенного удара: первый удар через полный интервал.
            SetTimer(w, ActionKind.Hit, t, Run.Balance.HitIntervalTicks);
        }

        // ---------- Фаза 2: удары ----------

        void PhaseHits(long t)
        {
            var workers = Run.Workers;
            m_Hitters.Clear();
            for (int i = 0; i < workers.Count; i++)
            {
                var w = workers[i];
                var a = w.Action;
                if (w.Mode == WorkerMode.Mining && a != null && a.Kind == ActionKind.Hit && a.Deadline == t) m_Hitters.Add(w);
            }
            if (m_Hitters.Count == 0) return;
            if (m_Hitters.Count > 1) m_Hitters.Sort(s_ByTargetThenId);
            int from = 0;
            while (from < m_Hitters.Count)
            {
                int target = m_Hitters[from].TargetId;
                int to = from + 1;
                while (to < m_Hitters.Count && m_Hitters[to].TargetId == target) to++;
                HitTarget(target, from, to, t);
                from = to;
            }
            m_Hitters.Clear();
        }

        /// <summary>Общий удар по цели: один раз обновить HP, выпустить порции, разрушить (ТЗ 7.1, 7.2).</summary>
        void HitTarget(int targetId, int from, int to, long t)
        {
            var run = Run;
            int ti = m_Map.TargetIndex(targetId);
            m_ProcessedEvents += to - from;
            if (ti < 0 || run.Targets[ti].Destroyed)
            {
                // Защита: добывающие освобождаются при разрушении, сюда попадать не должны.
                for (int k = from; k < to; k++) NeedTask(m_Hitters[k], t);
                return;
            }
            var ts = run.Targets[ti];
            if (ts.FirstStrikeTick < 0) ts.FirstStrikeTick = t;
            var stats = m_Stats;
            // Урон по цели с бонусами карточек к виду/типу цели; крит — детерминированный бросок по (котик, отметка).
            long units = m_TargetDamage[ti], sum = 0;
            int crits = 0;
            for (int k = from; k < to; k++)
            {
                if (stats.CritChancePpm > 0 && CritRoll(m_Hitters[k].Id, t) < stats.CritChancePpm)
                {
                    sum = checked(sum + stats.CritUnits(units));
                    crits++;
                }
                else sum = checked(sum + units);
            }
            ts.RemainingHpUnits = Math.Max(0L, ts.RemainingHpUnits - sum);
            ts.UpdateDamageStage();
            Touch(t);
            if (RecordEvents)
            {
                var hit = OutpostEvent.Create(OutpostEventType.TargetHit, t);
                hit.TargetId = targetId;
                hit.Count = to - from;
                hit.Value = sum;
                hit.WorkerId = m_Hitters[from].Id;
                hit.Extra = crits;
                m_Events.Add(hit);
            }

            int expected = ts.ExpectedReleasedUnits();
            if (expected > ts.ReleasedUnits) ReleaseCargo(ts, expected, from, to, t);

            bool destroyed = ts.RemainingHpUnits == 0;
            if (destroyed) DestroyTarget(ti, t);

            int capacity = stats.Capacity;
            for (int k = from; k < to; k++)
            {
                var w = m_Hitters[k];
                if (destroyed || w.Bag.Count >= capacity) NeedTask(w, t);
                else SetTimer(w, ActionKind.Hit, t, run.Balance.HitIntervalTicks);
            }
        }

        void DestroyTarget(int ti, long t)
        {
            var run = Run;
            var ts = run.Targets[ti];
            var mt = m_Map.Targets[ti];
            ts.Destroyed = true;
            ts.DestroyedTick = t;
            ts.UpdateDamageStage();
            ts.RemainingHpUnits = 0;
            // ТЗ 2.2 §4.1: footprint открытого завода не освобождается (здание — стена навигации, груз на нём не лежит).
            bool standing = IsStandingFactory(mt);
            if (!standing)
                foreach (int cell in mt.Cells) m_Occupant[cell] = -1;
            if (!mt.IsGate) m_LivingRsx--;
            run.NavVersion++;
            // Идущие к цели теряют задачу (дойдут до ближайшей клетки), рабочая клетка освобождается сразу.
            // Добывающие эту цель в другой фазе удара тоже перепланируются сразу (ТЗ 6.2: Mining — пока цель жива).
            var workers = run.Workers;
            for (int i = 0; i < workers.Count; i++)
            {
                var w = workers[i];
                if (w.TargetId != mt.Id) continue;
                if (w.Mode == WorkerMode.MovingToWork)
                {
                    w.TaskInvalid = true;
                    ReleaseWorkCell(w);
                }
                else if (w.Mode == WorkerMode.Mining) NeedTask(w, t);
            }
            run.Counters.ClearedTargets++;
            run.PlanningDirty = true;
            var e = OutpostEvent.Create(OutpostEventType.TargetDestroyed, t);
            e.TargetId = mt.Id;
            e.Cell = m_Map.Cell(mt.Column, mt.Row);
            Emit(e);
            // Цепи открывают кнопку активации; сундук и производство — только по ActivateFactory.
            if (!standing && m_Cards != null) GrantTargetReward(mt, t);
            if (standing) UnlockFactory(mt, t);
            // Первая сессия: замок клетки разрушен — персонаж спасён.
            if (ti == m_RescueLockIndex) OnRescueLockDestroyed(mt, t);
        }

        // ---------- Фаза 3: подбор ----------

        void PhasePickups(long t)
        {
            var workers = Run.Workers;
            for (int i = 0; i < workers.Count; i++)
            {
                var w = workers[i];
                var a = w.Action;
                if (w.Mode != WorkerMode.PickingUp || a == null || a.Kind != ActionKind.Pickup || a.Deadline != t) continue;
                m_ProcessedEvents++;
                PickOne(w, t);
            }
        }

        // ---------- Фаза 4: склад ----------

        void PhaseDepot(long t)
        {
            var slots = Run.Slots;
            for (int s = 0; s < slots.Length; s++)
            {
                int wid = slots[s].WorkerId;
                if (wid == 0) continue;
                var w = WorkerById(wid);
                var a = w?.Action;
                if (w == null || w.Mode != WorkerMode.Unloading || a == null || a.Kind != ActionKind.Unload || a.Deadline != t) continue;
                m_ProcessedEvents++;
                DepositOne(w, s, t);
            }
        }

        // ---------- Фаза 5: ворота и завершение ----------

        void PhaseGatesAndCompletion(long t)
        {
            var run = Run;
            if (run.Phase == RunPhase.ReadyToContinue) return;
            if (run.GatesOpened < m_Map.Gates.Length)
            {
                var gate = m_Map.Gates[run.GatesOpened];
                if (TargetById(gate.TargetId).Destroyed)
                {
                    run.GatesOpened++;
                    run.NavVersion++;
                    run.PlanningDirty = true;
                    Touch(t);
                    var e = OutpostEvent.Create(OutpostEventType.SectorOpened, t);
                    e.TargetId = gate.TargetId;
                    e.Count = run.GatesOpened;
                    Emit(e);
                }
            }
            if (run.Phase == RunPhase.Running && m_LivingRsx == 0 && run.GatesOpened == m_Map.Gates.Length)
            {
                run.Phase = RunPhase.DrainingCargo;
                run.Counters.ClearedTick = t;
                run.PlanningDirty = true;
                Touch(t);
                Emit(OutpostEvent.Create(OutpostEventType.DrainingStarted, t));
            }
            if (run.Phase == RunPhase.DrainingCargo && IsCargoDrained()) Complete(t);
        }

        bool IsCargoDrained()
        {
            var run = Run;
            if (run.Batches.Count > 0) return false;
            foreach (var w in run.Workers)
                if (w.Bag.Count > 0 || w.Mode == WorkerMode.Unloading) return false;
            return true;
        }

        /// <summary>
        /// Единственная награда завершения, ReadyToContinue, остановка всех таймеров (ТЗ 11). Заводы останавливаются первыми
        /// (ТЗ 2.2 §4.6), до сундука выхода: его карточка может включить AUTO, но партия уже не уйдёт.
        /// </summary>
        void Complete(long t)
        {
            var run = Run;
            StopFactories();
            var reward = StageProgressionService.ApplyCompletion(m_State, m_Configs);
            // Сундук выхода (Exit e): zone1–10 — скриптовые записи с гарантированными карточками, далее GachaCrusher. Первая сессия:
            // до шахты открытия карточек (сундук шахты 1 — ca001) не открывается: та же карточка с ресурсами ровно на одно
            // улучшение приходит при входе в шахту 2 вместе с разделом (ProgressionService.CardsHeld, ApplyEntryGrants).
            if (m_Cards != null && m_Map.ExitRewardId != null && !ProgressionService.CardsHeld(Profile, m_Configs, Run.Zone))
                OpenChest(m_Map.ExitRewardId, -1, m_Map.ExitCell, t);
            // Награда завершения (предмет, пройденная шахта спасения) — факты открытия первой сессии.
            SyncProgression(t);
            run.Phase = RunPhase.ReadyToContinue;
            run.Counters.CompletedTick = t;
            foreach (var w in run.Workers)
            {
                ReleaseWorkCell(w);
                ReleaseReservation(w);
                w.Action = null;
                w.Path = null;
                w.PathIndex = 0;
                w.TaskId = 0;
                w.TargetId = -1;
                w.DepotSlot = -1;
                w.TaskInvalid = false;
                SetMode(w, WorkerMode.Idle, t);
            }
            run.Queue.Clear();
            foreach (var s in run.Slots)
            {
                s.WorkerId = 0;
                s.TaskId = 0;
            }
            run.PlanningDirty = false;
            Touch(t);
            var e = OutpostEvent.Create(OutpostEventType.StageCompleted, t);
            if (reward != null)
            {
                e.ItemId = reward.ItemId;
                e.Count = reward.CompletionParts + reward.DuplicateParts;
            }
            Emit(e);
        }

        // ---------- Фаза 6: команды ----------

        void PhaseCommands(long t)
        {
            while (m_Scheduled.Count > 0 && m_Scheduled[0].Tick == t)
            {
                var sc = m_Scheduled[0];
                m_Scheduled.RemoveAt(0);
                m_ProcessedEvents++;
                ScheduledResults.Add(Execute(sc.Command, t));
            }
        }
    }
}
