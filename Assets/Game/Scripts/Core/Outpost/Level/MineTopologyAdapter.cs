using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>Reposition existing content without changing IDs, footprint areas, source ranks or rewards.</summary>
    public static class MineTopologyAdapter
    {
        public static OutpostMap Apply(OutpostMap source, MineTopology snapshot)
        {
            if (source == null || snapshot == null) throw new ArgumentNullException();
            snapshot.Validate();
            if (snapshot.ZoneId != source.ZoneId || snapshot.Placements.Length != source.Targets.Length || snapshot.Gates.Length != source.Gates.Length)
                throw new ArgumentException("Topology content mismatch: " + source.ZoneId);
            var map = new OutpostMap
            {
                ZoneId = source.ZoneId, Number = source.Number, DecoSeed = source.DecoSeed, SourceSha256 = source.SourceSha256,
                Override = source.Override, OverrideName = source.OverrideName, Topology = OutpostJson.DeepCopy(snapshot),
                Width = snapshot.Width, Depth = snapshot.Depth, Rows = snapshot.Depth + OutpostLevelAdapter.BaseRows,
                RequiredCount = source.RequiredCount, ExitRewardId = source.ExitRewardId,
            };
            map.CellCount = map.Width * map.Rows;
            map.PlayableCells = new bool[map.CellCount];
            map.CellSectors = new int[map.CellCount];
            map.CellGateOrder = new int[map.CellCount];
            map.InitialOccupant = new int[map.CellCount];
            int baseCol = map.Width / 2;
            for (int cell = 0; cell < map.CellCount; cell++)
            {
                int row = map.Row(cell), col = map.Col(cell);
                bool camp = row >= map.Depth;
                map.PlayableCells[cell] = camp ? snapshot.Version == MineTopologyCatalog.LegacyVersion || Math.Abs(col - baseCol) <= 4 : snapshot.FloorRows[row][col] == '.';
                map.CellSectors[cell] = !map.PlayableCells[cell] ? -1 : camp ? 0 : snapshot.SectorRows[row][col] - '0';
                map.CellGateOrder[cell] = -1;
                map.InitialOccupant[cell] = -1;
            }
            map.SpawnCell = map.IntakeCell = map.Cell(baseCol, map.Depth + 1);
            map.DepotCells = new[] { map.Cell(baseCol - 1, map.Depth + 1), map.Cell(baseCol + 1, map.Depth + 1) };
            var campCells = new List<int>();
            for (int cell = map.Depth * map.Width; cell < map.CellCount; cell++)
                if (map.PlayableCells[cell]) campCells.Add(cell);
            map.CampCells = campCells.ToArray();
            map.ExitCell = map.Cell(snapshot.ExitColumn, snapshot.ExitRow);
            if (!map.IsPlayable(map.ExitCell)) throw new ArgumentException("Exit in structural rock");
            var placements = new Dictionary<int, MinePlacement>();
            foreach (var p in snapshot.Placements)
            {
                if (p == null || placements.ContainsKey(p.TargetId)) throw new ArgumentException("Duplicate topology target");
                if (p.Branch < 0 || p.Branch > source.Targets.Length) throw new ArgumentException("Invalid work branch");
                placements.Add(p.TargetId, p);
            }
            map.Gates = new MapGate[source.Gates.Length];
            for (int i = 0; i < map.Gates.Length; i++)
            {
                var oldGate = source.Gates[i];
                var gate = snapshot.Gates[i];
                if (gate == null || gate.TargetId != oldGate.TargetId || gate.Cells == null || gate.Cells.Length == 0)
                    throw new ArgumentException("Gate identity mismatch");
                var p = placements[gate.TargetId];
                map.Gates[i] = new MapGate { TargetId = gate.TargetId, Order = i, Row = p.Row, BarrierCells = (int[])gate.Cells.Clone() };
                foreach (int cell in gate.Cells)
                {
                    if (!map.IsPlayable(cell) || map.CellGateOrder[cell] >= 0 || map.SectorAt(cell) != i)
                        throw new ArgumentException("Gate not in a valid passage");
                    map.CellGateOrder[cell] = i;
                }
            }
            map.Targets = new MapTarget[source.Targets.Length];
            for (int i = 0; i < map.Targets.Length; i++)
            {
                var old = source.Targets[i];
                if (!placements.TryGetValue(old.Id, out var p)) throw new ArgumentException("Missing target placement: " + old.Id);
                var target = new MapTarget
                {
                    Id = old.Id, Kind = old.Kind, TypeId = old.TypeId, VisualId = old.VisualId, Sector = old.Sector,
                    SourceLevel = old.SourceLevel, Added = old.Added, RewardId = old.RewardId, Column = p.Column, Row = p.Row, Branch = p.Branch,
                    CardId = old.CardId, CardLevel = old.CardLevel,
                };
                if (old.IsGate) target.Cells = (int[])map.Gates[old.Sector].BarrierCells.Clone();
                else
                {
                    int minCol = old.Column, maxCol = old.Column, minRow = old.Row;
                    foreach (int cell in old.Cells)
                    {
                        minCol = Math.Min(minCol, source.Col(cell));
                        maxCol = Math.Max(maxCol, source.Col(cell));
                        minRow = Math.Min(minRow, source.Row(cell));
                    }
                    int w = maxCol - minCol + 1, h = old.Row - minRow + 1;
                    var cells = new List<int>(old.Cells.Length);
                    for (int dy = 0; dy < h; dy++)
                        for (int dx = 0; dx < w; dx++)
                        {
                            int c = p.Column + dx, r = p.Row - dy;
                            if (c < 0 || c >= map.Width || r < 0 || r >= map.Depth) throw new ArgumentException("Footprint outside topology");
                            int cell = map.Cell(c, r);
                            if (!map.IsPlayable(cell) || map.SectorAt(cell) != target.Sector || map.InitialOccupant[cell] >= 0 || map.CellGateOrder[cell] >= 0)
                                throw new ArgumentException("Invalid target footprint: " + old.Id + " at " + cell);
                            map.InitialOccupant[cell] = old.Id;
                            cells.Add(cell);
                        }
                    cells.Sort();
                    target.Cells = cells.ToArray();
                    if (target.Cells.Length != old.Cells.Length) throw new ArgumentException("Footprint area changed");
                }
                target.WorkCells = Neighbours(map, target);
                if (target.WorkCells.Length == 0) throw new ArgumentException("No playable work position: " + target.Id);
                map.Targets[i] = target;
            }
            map.BuildIndex();
            return map;
        }

        static int[] Neighbours(OutpostMap map, MapTarget target)
        {
            var own = new HashSet<int>(target.Cells);
            var result = new SortedSet<int>();
            foreach (int cell in target.Cells)
            {
                int c = map.Col(cell), r = map.Row(cell);
                Add(c, r - 1); Add(c - 1, r); Add(c + 1, r); Add(c, r + 1);
            }
            var cells = new int[result.Count];
            result.CopyTo(cells);
            return cells;

            void Add(int c, int r)
            {
                if (c < 0 || c >= map.Width || r < 0 || r >= map.Rows) return;
                int cell = map.Cell(c, r);
                if (!own.Contains(cell) && map.IsPlayable(cell) && (!target.IsGate || map.SectorAt(cell) == target.Sector) && map.CellGateOrder[cell] < 0)
                    result.Add(cell);
            }
        }
    }
}
