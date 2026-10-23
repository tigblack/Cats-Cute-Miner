using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Shrinks only the added legacy chamber. All target IDs, HP (including final damage percentage), cargo,
    /// credits, clocks and receipts survive unchanged. The legacy approach was open, so no fresh rocks/rewards are inserted.
    /// If ground loot or a loaded cat would end up in rock, defer and continue on the saved legacy layout.
    /// </summary>
    public static class FinalExcavationMigration
    {
        public static bool TryCompact(OutpostRun run, OutpostMap map, out string diagnostic)
        {
            diagnostic = null;
            var old = run?.MapFinal;
            if (old == null || old.IsCompact || old.Version != "final-excavations-1" ||
                old.Width != 13 || old.Depth != 3 || old.AddedRows != 8 || map.Topology == null) return false;
            if (run.Targets.Count != map.Targets.Length || map.Targets.Length == 0 ||
                map.Targets[map.Targets.Length - 1].Id != old.TargetId || map.Final?.Version != old.Version)
            { diagnostic = "Legacy final content does not match its snapshot"; return false; }
            for (int i = 0; i < map.Targets.Length; i++)
                if (run.Targets[i].Id != map.Targets[i].Id)
                { diagnostic = "Legacy target identity mismatch"; return false; }

            var next = OutpostJson.DeepCopy(old);
            next.Version = FinalExcavation.CompactVersion;
            next.MigratedFromVersion = old.Version;
            next.Width = 4; next.Depth = 2;
            next.Column = Math.Max(0, Math.Min(map.Width - 4, map.Topology.ExitColumn - 1));
            next.ApproachRocks = new List<DressingPiece>();
            int left = next.Column, right = left + 3, center = left + 1, end = old.AddedRows * map.Width;
            var playable = (bool[])map.PlayableCells.Clone();
            var initial = (int[])map.InitialOccupant.Clone();
            var cells = new List<int>();
            var work = new int[4];
            for (int cell = 0; cell < end; cell++)
            {
                int row = cell / map.Width, col = cell % map.Width;
                playable[cell] = row == 0 ? col == center : col >= left && col <= right;
                if (initial[cell] == old.TargetId) initial[cell] = -1;
                if (row >= 1 && row <= 2 && col >= left && col <= right)
                { cells.Add(cell); initial[cell] = old.TargetId; }
            }
            for (int i = 0; i < 4; i++) work[i] = 3 * map.Width + left + i;
            bool Passable(int cell)
            {
                if (cell < 0 || cell >= map.CellCount || !playable[cell] || map.IsProp(cell) ||
                    map.CellGateOrder[cell] >= run.GatesOpened) return false;
                int id = initial[cell], index = map.TargetIndex(id);
                return id < 0 || index >= 0 && run.Targets[index].Destroyed;
            }
            foreach (var batch in run.Batches)
                if (!Passable(batch.Cell))
                { diagnostic = "Deferred compact final: ground cargo occupies removed chamber floor"; return false; }

            var reset = new HashSet<int>();
            var relocate = new Dictionary<int, int>();
            foreach (var worker in run.Workers)
            {
                if (!Passable(worker.Cell))
                {
                    if (worker.Bag.Count > 0)
                    { diagnostic = "Deferred compact final: a loaded cat occupies removed chamber floor"; return false; }
                    int best = -1, distance = int.MaxValue;
                    for (int cell = 3 * map.Width; cell < end; cell++)
                    {
                        if (!Passable(cell)) continue;
                        int d = Math.Abs(cell / map.Width - worker.Cell / map.Width) + Math.Abs(cell % map.Width - worker.Cell % map.Width);
                        if (d < distance) { best = cell; distance = d; }
                    }
                    if (best < 0) { diagnostic = "No safe empty-cat position in compact neck"; return false; }
                    relocate[worker.Id] = best; reset.Add(worker.Id);
                }
                if (worker.TargetId == old.TargetId && worker.WorkCell >= 0) reset.Add(worker.Id);
                if (worker.Path != null)
                    for (int i = Math.Max(0, worker.PathIndex); i < worker.Path.Length; i++)
                        if (!Passable(worker.Path[i])) { reset.Add(worker.Id); break; }
            }
            // All deferral checks precede mutation. Never move ground cargo or a carried unit.
            map.PlayableCells = playable; map.InitialOccupant = initial;
            map.Final = OutpostJson.DeepCopy(next); map.ExitCell = center;
            var target = map.Target(old.TargetId);
            target.Column = left; target.Row = 1; target.Cells = cells.ToArray(); target.WorkCells = work; target.Final = map.Final;
            run.MapFinal = next;
            foreach (var batch in run.Batches)
                foreach (var unit in batch.Units)
                    if (reset.Contains(unit.ReservedBy)) { unit.ReservedBy = 0; unit.ReservedTask = 0; }
            foreach (var slot in run.Slots)
                if (reset.Contains(slot.WorkerId)) { slot.WorkerId = 0; slot.TaskId = 0; }
            run.Queue.RemoveAll(q => reset.Contains(q.WorkerId));
            foreach (var worker in run.Workers)
            {
                if (!reset.Contains(worker.Id)) continue;
                if (relocate.TryGetValue(worker.Id, out int cell)) worker.Cell = cell;
                worker.Mode = WorkerMode.Idle; worker.Action = null; worker.Path = null; worker.PathIndex = 0;
                worker.TargetId = -1; worker.WorkCell = -1; worker.TaskId = 0; worker.CargoBatchSeq = -1; worker.DepotSlot = -1;
                worker.TaskInvalid = false; worker.MoveRetryAttempted = false; worker.LastMoveProgressTick = run.Now;
            }
            run.NavVersion++; run.PlanningDirty = true;
            map.BuildIndex();
            return true;
        }
    }
}
