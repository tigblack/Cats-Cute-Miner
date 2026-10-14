using System.Collections.Generic;
using System.Text;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Статическая проверка последовательной расчистки (ТЗ 8.5) — копия алгоритма Docs/TZ/analysis/outpost_v2_audit.py:
    /// BFS от площадки (Width/2, Depth+1) с барьером row &lt; gateRow, волны удаления доступных целей сектора,
    /// проверка рабочей клетки ворот и достижимости выхода. Это доказательство геометрии, не прогон планировщика.
    /// </summary>
    public static class OutpostReachability
    {
        public static ReachabilityReport Check(OutpostMap map)
        {
            var report = new ReachabilityReport { ZoneId = map.ZoneId };
            var occupied = (int[])map.InitialOccupant.Clone();
            var remaining = new List<MapTarget>();
            foreach (var t in map.Targets)
                if (!t.IsGate) remaining.Add(t);
            var seen = new bool[map.CellCount];
            var queue = new int[map.CellCount];
            int start = map.Cell(map.Width / 2, map.Depth + 1);

            for (int sector = 0; sector <= map.Gates.Length; sector++)
            {
                int gateRow = sector < map.Gates.Length ? map.Gates[sector].Row : -1;
                while (true)
                {
                    var pending = remaining.FindAll(o => o.Sector == sector);
                    if (pending.Count == 0) break;
                    Reach(map, occupied, gateRow, sector, start, seen, queue);
                    var available = new List<MapTarget>();
                    foreach (var o in pending)
                        if (TouchesFree(map, o, seen)) available.Add(o);
                    if (available.Count == 0)
                    {
                        var sb = new StringBuilder();
                        sb.Append(map.ZoneId).Append(": unreachable targets in sector ").Append(sector).Append(':');
                        foreach (var o in pending)
                        {
                            sb.Append(' ').Append(o.Id);
                            report.Unreachable.Add(o.Id);
                        }
                        report.Diagnostic = sb.ToString();
                        return report;
                    }
                    foreach (var o in available)
                    {
                        foreach (int cell in o.Cells) occupied[cell] = -1;
                        remaining.Remove(o);
                        report.Cleared++;
                    }
                    report.Waves++;
                }
                if (gateRow >= 0)
                {
                    Reach(map, occupied, gateRow, sector, start, seen, queue);
                    bool any = false;
                    foreach (int cell in map.Target(map.Gates[sector].TargetId).WorkCells)
                        if (seen[cell]) any = true;
                    if (!any)
                    {
                        report.Diagnostic = map.ZoneId + ": no gate working position in row " + gateRow;
                        report.Unreachable.Add(map.Gates[sector].TargetId);
                        return report;
                    }
                    report.GatesOpened++;
                }
            }
            Reach(map, occupied, -1, map.Gates.Length, start, seen, queue);
            if (remaining.Count > 0 || !seen[map.ExitCell])
            {
                report.Diagnostic = map.ZoneId + ": incomplete clearance or unreachable exit";
                return report;
            }
            report.Passed = true;
            return report;
        }

        /// <summary>BFS по 4 направлениям (вверх, влево, вправо, вниз), свободные клетки, row ≥ gateRow.</summary>
        static void Reach(OutpostMap map, int[] occupied, int gateRow, int gatesOpened, int start, bool[] seen, int[] queue)
        {
            System.Array.Clear(seen, 0, seen.Length);
            int head = 0, tail = 0;
            seen[start] = true;
            queue[tail++] = start;
            while (head < tail)
            {
                int cell = queue[head++];
                int col = map.Col(cell), row = map.Row(cell);
                Visit(col, row - 1);
                Visit(col - 1, row);
                Visit(col + 1, row);
                Visit(col, row + 1);
            }

            void Visit(int c, int r)
            {
                if (c < 0 || c >= map.Width || r < 0 || r >= map.Rows) return;
                if (map.Topology == null && gateRow >= 0 && r < gateRow) return;
                int n = map.Cell(c, r);
                if (!map.IsPlayable(n) || (map.Topology != null && map.CellGateOrder[n] >= gatesOpened)) return;
                if (occupied[n] >= 0 || seen[n]) return;
                seen[n] = true;
                queue[tail++] = n;
            }
        }

        static bool TouchesFree(OutpostMap map, MapTarget o, bool[] seen)
        {
            foreach (int wc in o.WorkCells)
                if (seen[wc]) return true;
            return false;
        }
    }

    public sealed class ReachabilityReport
    {
        public string ZoneId;
        public bool Passed;
        /// <summary>Освобождено r/s/x.</summary>
        public int Cleared;
        public int GatesOpened;
        public int Waves;
        public string Diagnostic;
        public readonly List<int> Unreachable = new List<int>();
    }
}
