using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>Adds a terminal blockage and its neck. Original targets/content are only translated, never replaced.</summary>
    public static class FinalExcavationAdapter
    {
        public static OutpostMap Generate(OutpostMap map, OutpostConfigSet configs, CatMiners.Core.Data.BalanceRepository repo)
        {
            var rules=configs.FinalExcavations;
            if (rules == null || map.Number < rules.FromMine || map.Topology == null) return map;
            var b = rules.ForMine(map.Number);
            if (b == null) return map;
            int id = 0;
            foreach (var t in map.Targets) id = Math.Max(id, t.Id + 1);
            int center = Math.Max(rules.Width / 2 + 1, Math.Min(map.Width - rules.Width / 2 - 2, map.Col(map.ExitCell)));
            string theme = b.Theme == "ancient" ? new[] { "crystal", "amber", "dinosaur", "ancient" }[(map.Number - 6) % 4] : b.Theme;
            var reference=FinalExcavationReference.Build(map,configs,repo);
            // Four adjacent front positions limit actual throughput. Fixture seconds are not a weak-team promise.
            int workers = rules.Version == FinalExcavation.CompactVersion ? Math.Min(reference.Workers, rules.Width) : reference.Workers;
            var nominal=Rational.Of(reference.Stats.DamageUnitsFor('x',"final-excavation"),OutpostIds.HpUnitsPerHp)*workers*
                Rational.Of(configs.Balance.TicksPerSecond,configs.Balance.HitIntervalTicks);
            long hp=ExactMath.ToLongChecked((nominal*b.TargetSeconds*rules.ThroughputFor(map.Number)).Ceil(),"Final HP");
            var snapshot = new FinalExcavation
            {
                Version = rules.Version, TargetId = id, AddedRows = rules.AddedRows, Column = center - rules.Width / 2,
                Width = rules.Width, Depth = rules.Depth, Theme = theme, MaxHp = hp,
                RewardUnits = b.RewardUnits, TargetSeconds = b.TargetSeconds, HiringCostFactor = Rational.One,
            };
            if (snapshot.IsCompact)
            {
                snapshot.Column = CompactColumn(map, snapshot.Width);
                MapTarget template = null;
                foreach (var target in map.Targets)
                    if (target.Kind == 'r' && target.Cells.Length == 1 &&
                        (template == null || target.Row < template.Row || target.Row == template.Row && target.Id < template.Id)) template = target;
                if (template == null) throw new OutpostConfigException("Final approach needs an ordinary one-cell rock template");
                for (int row = snapshot.Depth + 1; row <= snapshot.Depth + 2; row++)
                    for (int col = snapshot.Column; col < snapshot.Column + snapshot.Width; col++)
                        snapshot.ApproachRocks.Add(new DressingPiece { TargetId = id++, Family = "final_approach", Column = col,
                            Row = row, RockType = template.TypeId, RockVisual = template.VisualId, RockLevel = template.SourceLevel });
                snapshot.TargetId = id; // Always last; original IDs are untouched.
            }
            return Apply(map, snapshot);
        }

        internal static int CompactColumn(OutpostMap map, int width) =>
            Math.Max(0, Math.Min(map.Width - width, map.Col(map.ExitCell) - (width - 1) / 2));

        public static OutpostMap Apply(OutpostMap map, FinalExcavation snapshot)
        {
            if (snapshot == null) return map;
            int oldExit = map.ExitCell, w = map.Width, shift = snapshot.AddedRows * w, oldCount = map.CellCount;
            map.Final = OutpostJson.DeepCopy(snapshot);
            map.Depth += snapshot.AddedRows; map.Rows += snapshot.AddedRows; map.CellCount += shift;
            map.PlayableCells = Shift(map.PlayableCells, true);
            map.CellSectors = Shift(map.CellSectors, 0);
            map.CellGateOrder = Shift(map.CellGateOrder, -1);
            map.InitialOccupant = Shift(map.InitialOccupant, -1);
            if (map.PropAt != null) map.PropAt = Shift(map.PropAt, -1);
            map.SpawnCell += shift; map.IntakeCell += shift;
            MoveCells(map.DepotCells); MoveCells(map.CampCells);
            foreach (var p in map.Props) { MoveCells(p.Cells); p.Row += snapshot.AddedRows; }
            foreach (var g in map.Gates) { g.Row += snapshot.AddedRows; MoveCells(g.BarrierCells); }
            foreach (var t in map.Targets) { t.Row += snapshot.AddedRows; MoveCells(t.Cells); MoveCells(t.WorkCells); }
            int sector = map.Gates.Length, left = snapshot.Column, right = left + snapshot.Width - 1, center = (left + right) / 2;
            // No side passage past the formation: all chamber cross-section cells belong to its one logical footprint.
            for (int r = 0; r < snapshot.AddedRows; r++)
                for (int c = 0; c < w; c++)
                {
                    int cell = r * w + c;
                    map.PlayableCells[cell] = r == 0 ? c == center : snapshot.IsCompact ? c >= left && c <= right : r <= snapshot.Depth ? c >= left && c <= right :
                        r <= snapshot.Depth + 3 ? c >= left - 1 && c <= right + 1 : c >= Math.Min(center, oldExit % w) && c <= Math.Max(center, oldExit % w) + 1;
                    map.CellSectors[cell] = sector;
                }
            // Legacy snapshots retain their connecting apron; compact snapshots already have a four-cell neck.
            int entryRow = snapshot.AddedRows - 1;
            if (!snapshot.IsCompact)
                for (int c = Math.Min(center, oldExit % w); c <= Math.Max(center, oldExit % w) + 1; c++) map.PlayableCells[entryRow * w + c] = true;
            var cells = new List<int>(); var work = new List<int>();
            for (int r = 1; r <= snapshot.Depth; r++)
                for (int c = left; c <= right; c++) { int cell = r * w + c; cells.Add(cell); map.InitialOccupant[cell] = snapshot.TargetId; }
            for (int c = left; c <= right; c++) work.Add((snapshot.Depth + 1) * w + c);
            // Preserve the second work line only when playing an unmigrated legacy snapshot.
            if (!snapshot.IsCompact)
                for (int c = left; c <= right; c++) work.Add((snapshot.Depth + 2) * w + c);
            var targets = new List<MapTarget>(map.Targets);
            foreach (var piece in map.Final.ApproachRocks ?? new List<DressingPiece>())
            {
                int cell = map.Cell(piece.Column, piece.Row);
                if (!snapshot.IsCompact || !piece.IsOre || piece.W != 1 || piece.H != 1 ||
                    piece.TargetId >= snapshot.TargetId || !map.IsPlayable(cell) || map.InitialOccupant[cell] >= 0)
                    throw new ArgumentException("Invalid final approach rock: " + piece.TargetId);
                var target = new MapTarget { Id = piece.TargetId, Kind = 'r', TypeId = piece.RockType,
                    VisualId = piece.RockVisual ?? piece.RockType, SourceLevel = piece.RockLevel, Piece = piece,
                    Column = piece.Column, Row = piece.Row, Sector = sector, Cells = new[] {cell}, IsFinalApproach = true };
                target.WorkCells = MineTopologyAdapter.Neighbours(map, target);
                targets.Add(target); map.InitialOccupant[cell] = piece.TargetId;
            }
            targets.Add(new MapTarget { Id = snapshot.TargetId, Kind = 'x', TypeId = "final-excavation", VisualId = "final_" + snapshot.Theme,
                Column = left, Row = 1, Sector = sector, Cells = cells.ToArray(), WorkCells = work.ToArray(), Final = map.Final });
            targets.Sort((a, b) => a.Id.CompareTo(b.Id));
            map.Targets = targets.ToArray(); map.RequiredCount += 1 + (map.Final.ApproachRocks?.Count ?? 0); map.ExitCell = center;
            map.BuildIndex();
            return map;

            void MoveCells(int[] a) { if (a != null) for (int i = 0; i < a.Length; i++) a[i] += shift; }
            T[] Shift<T>(T[] source, T empty)
            {
                var a = new T[map.CellCount];
                for (int i = 0; i < shift; i++) a[i] = empty;
                if (source != null) Array.Copy(source, 0, a, shift, oldCount);
                else for (int i = shift; i < a.Length; i++) a[i] = empty;
                return a;
            }
        }
    }
}
