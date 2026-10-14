using System;

namespace CatMiners.Core.Outpost
{
    public sealed partial class OutpostSimulation
    {
        // Буферы поиска клетки партии удара (BFS по слоям внутри маски); выделяются при первом ударе.
        int[] m_StrikeQueue;
        int[] m_StrikeSeen;
        int m_StrikeStamp;

        /// <summary>
        /// Причина, по которой кирка игрока не может ударить цель (ТЗ 6.6), или null. Шахта в Running; цель — живая r/s/x
        /// текущего сектора с достижимой рабочей позицией либо очередные ворота, когда в их секторе не осталось r/s/x.
        /// Старые карты допускают удар по ещё окружённой препятствиями цели своего сектора.
        /// Причины: "phase", "disabled" (TapDamageFactor = 0), "unknown", "destroyed", "locked" (недоступная цель).
        /// </summary>
        public string StrikeBlockReason(int targetId)
        {
            var run = Run;
            if (run.Phase != RunPhase.Running) return "phase";
            if (run.Balance.TapDamageFactor.Sign <= 0) return "disabled";
            int ti = m_Map.TargetIndex(targetId);
            if (ti < 0) return "unknown";
            if (run.Targets[ti].Destroyed) return "destroyed";
            var mt = m_Map.Targets[ti];
            int sector = run.GatesOpened;
            if (!mt.IsGate)
            {
                if (mt.Sector != sector) return "locked";
                if (m_Map.Topology != null)
                {
                    var reachable = Navigation.From(m_Map.IntakeCell);
                    bool accessible = false;
                    foreach (int cell in mt.WorkCells)
                        if (reachable.Dist[cell] >= 0) { accessible = true; break; }
                    if (!accessible) return "locked";
                }
                return null;
            }
            if (sector >= m_Map.Gates.Length || m_Map.Gates[sector].TargetId != mt.Id) return "locked";
            var targets = m_Map.Targets;
            for (int i = 0; i < targets.Length; i++)
                if (!targets[i].IsGate && targets[i].Sector == sector && !run.Targets[i].Destroyed) return "locked";
            return null;
        }

        /// <summary>Можно ли сейчас ударить цель киркой игрока.</summary>
        public bool CanStrike(int targetId) => StrikeBlockReason(targetId) == null;

        /// <summary>
        /// Удар кирки игрока (ТЗ 6.6): урон TapDamageUnits сразу на отметке Now, порции по тем же порогам HP, что у ударов котиков.
        /// Выпущенные порции — новая партия на земле (<see cref="StrikeDropCell"/>), денег удар не начисляет: золото даёт только сдача.
        /// Разрушение — как от котиков: footprint освобождается, добывающие эту цель перепланируются. Levels — выпущено порций,
        /// Cost — нанесённый урон в долях HP.
        /// </summary>
        CommandResult ExecuteStrike(OutpostCommand cmd, long t)
        {
            string reason = StrikeBlockReason(cmd.TargetId);
            if (reason != null) return CommandResult.Fail(reason);
            var run = Run;
            // Команды одной отметки идут до фазы 7: после покупки в той же отметке характеристики ещё не пересчитаны.
            var stats = m_StatsDirty ? ComputeStats() : m_Stats;
            int ti = m_Map.TargetIndex(cmd.TargetId);
            var ts = run.Targets[ti];
            var mt = m_Map.Targets[ti];
            long damage = Math.Min(stats.TapDamageUnits, ts.RemainingHpUnits);
            ts.RemainingHpUnits -= damage;
            Touch(t);
            var e = OutpostEvent.Create(OutpostEventType.TargetStruck, t);
            e.TargetId = mt.Id;
            e.Cell = m_Map.Cell(mt.Column, mt.Row);
            e.Value = damage;
            Emit(e);

            bool destroyed = ts.RemainingHpUnits == 0;
            int expected = destroyed
                ? ts.TotalUnits
                : (int)(checked((long)ts.TotalUnits * (ts.MaxHpUnits - ts.RemainingHpUnits)) / ts.MaxHpUnits);
            // Сначала разрушение: освобождённый footprint становится ближайшей клеткой для остатка груза.
            if (destroyed) DestroyTarget(ti, t);
            int released = expected - ts.ReleasedUnits;
            if (released > 0) ReleaseToGround(ts, mt, expected, stats, t);
            // Разрушение командой (фаза 6) сразу проходит проверки фазы 5: ворота открываются, при полной расчистке начинается
            // DrainingCargo — снимок после команды согласован, планировщик фазы 7 видит новый барьер и фазу (ТЗ 6.5, 11.1).
            if (destroyed) PhaseGatesAndCompletion(t);
            return new CommandResult { Ok = true, Levels = Math.Max(0, released), Cost = damage };
        }

        /// <summary>Порции Released..expected−1 с фиксированной стоимостью по текущим Stats — одна партия на земле (ТЗ 7.1–7.3).</summary>
        void ReleaseToGround(TargetState ts, MapTarget mt, int expected, TeamStats stats, long t)
        {
            var run = Run;
            int count = expected - ts.ReleasedUnits;
            var value = stats.CoinValueFor(mt.Kind, mt.TypeId, ts.ValueFactor);
            int cell = StrikeDropCell(mt);
            var batch = new CargoBatch
            {
                Seq = run.NextBatchSeq++,
                Cell = cell,
                CreatedTick = t,
            };
            batch.Key = OutpostIds.BatchKey(batch.Seq);
            for (int ord = ts.ReleasedUnits; ord < expected; ord++)
                batch.Units.Add(new CargoUnit { Source = ts.Id, Ordinal = ord, Value = value });
            ts.ReleasedUnits = expected;
            run.Counters.ReleasedUnits += count;
            run.Counters.ReleasedValue += value * count;
            run.Batches.Add(batch);
            run.PlanningDirty = true;
            if (RecordEvents)
            {
                var e = OutpostEvent.Create(OutpostEventType.CargoReleased, t);
                e.TargetId = ts.Id;
                e.Cell = cell;
                e.Count = count;
                e.BatchSeq = batch.Seq;
                e.Value = value;
                m_Events.Add(e);
            }
        }

        /// <summary>
        /// Клетка партии удара: BFS по слоям от footprint цели (у ворот — от рабочих клеток) внутри маски и доступного сектора.
        /// Выбирается достижимая от точки приёма клетка ближайшего слоя, затем кратчайший путь и наименьший индекс.
        /// Только старые карты сохраняют fallback в замкнутый карман, который позднее откроется расчисткой.
        /// </summary>
        int StrikeDropCell(MapTarget mt)
        {
            var nav = Navigation;
            var field = nav.From(m_Map.IntakeCell);
            int width = m_Map.Width, rows = m_Map.Rows, barrier = BarrierRow;
            if (m_StrikeQueue == null)
            {
                m_StrikeQueue = new int[m_Map.CellCount];
                m_StrikeSeen = new int[m_Map.CellCount];
            }
            if (m_StrikeStamp == int.MaxValue)
            {
                Array.Clear(m_StrikeSeen, 0, m_StrikeSeen.Length);
                m_StrikeStamp = 0;
            }
            int stamp = ++m_StrikeStamp;
            var queue = m_StrikeQueue;
            int head = 0, tail = 0;
            var seeds = mt.IsGate ? mt.WorkCells : mt.Cells;
            foreach (int c in seeds)
                Enqueue(c);
            while (head < tail)
            {
                int layerEnd = tail;
                int best = -1, bestDist = 0, pocket = -1;
                for (int i = head; i < layerEnd; i++)
                {
                    int c = queue[i];
                    if (!nav.Passable(c)) continue;
                    int d = field.Dist[c];
                    if (d >= 0)
                    {
                        if (best < 0 || d < bestDist || (d == bestDist && c < best))
                        {
                            best = c;
                            bestDist = d;
                        }
                    }
                    else if (pocket < 0 || c < pocket) pocket = c;
                }
                if (best >= 0) return best;
                if (pocket >= 0 && m_Map.Topology == null) return pocket;
                for (int i = head; i < layerEnd; i++)
                {
                    int c = queue[i];
                    int col = c % width, row = c / width;
                    if (row - 1 >= 0) Enqueue(c - width);
                    if (col - 1 >= 0) Enqueue(c - 1);
                    if (col + 1 < width) Enqueue(c + 1);
                    if (row + 1 < rows) Enqueue(c + width);
                }
                head = layerEnd;
            }
            // Площадка базы проходима всегда, сюда попасть нельзя.
            throw new InvalidOperationException("Ядро: нет клетки для груза удара по цели " + mt.Id);

            void Enqueue(int c)
            {
                if (!m_Map.IsPlayable(c) || c / width < barrier || m_StrikeSeen[c] == stamp) return;
                if (m_Map.Topology != null && (m_Map.SectorAt(c) > Run.GatesOpened || m_Map.CellGateOrder[c] >= Run.GatesOpened)) return;
                m_StrikeSeen[c] = stamp;
                queue[tail++] = c;
            }
        }
    }
}
