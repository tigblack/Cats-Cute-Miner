using System.Collections.Generic;
using System.Text;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Статический валидатор карты шахты (ТЗ 2.1 §8.5, ТЗ 2.2 §4.1). Это доказательство геометрии, не прогон планировщика.
    /// Завод (бывшее месторождение <c>s</c>) после открытия остаётся зданием: его footprint занят всегда — до открытия живой
    /// целью, после — заводом. Проверяются два случая, оба обязательны для <see cref="ReachabilityReport.Passed"/>:
    /// <list type="number">
    /// <item>Волновая расчистка — копия алгоритма Docs/TZ/analysis/outpost_v2_audit.py: BFS от площадки (Width/2, Depth+1)
    /// по 4 направлениям (вверх, влево, вправо, вниз), закрытые ворота — барьер (у ветвящейся топологии — клетки прохода,
    /// у исходной сетки — строки выше gateRow); за волну снимаются все цели сектора с достижимой рабочей клеткой; снятые r/x
    /// освобождают клетки, открытый завод в тот же момент становится стеной. Затем рабочая клетка ворот и выход.</item>
    /// <item>Итоговая карта: r/x сняты, заводы стоят. Для каждой стадии k = 0…G (открыто k ворот): ни одна клетка дальнего
    /// сектора или закрытых ворот не достижима (ворота не обходятся), каждая клетка пола открытых секторов, кроме footprint
    /// заводов, связана с базой. При всех открытых воротах у каждой обязательной цели есть достижимая рабочая клетка, выход
    /// достижим, у каждого завода есть свободная соседняя клетка, связанная с <see cref="OutpostMap.IntakeCell"/>, и маршрут
    /// трубы (<see cref="OutpostMap.Factories"/>, ТЗ 2.2 §5).</item>
    /// </list>
    /// Груз выпадает на рабочую клетку ударившего котика, а проходимость со временем только растёт (снимаются r/x,
    /// открываются ворота; footprint заводов занят всегда), поэтому достижимая рабочая клетка гарантирует доставку груза.
    /// Python-копия обоих случаев для топологии — Tools/Topology/topology_check.py.
    /// </summary>
    public static class OutpostReachability
    {
        public static ReachabilityReport Check(OutpostMap map)
        {
            var report = new ReachabilityReport { ZoneId = map.ZoneId };
            foreach (var f in map.Factories)
            {
                report.Factories++;
                if (f.HasRoute) report.FactoriesWithRoute++;
            }
            var seen = new bool[map.CellCount];
            var queue = new int[map.CellCount];
            report.WavesPassed = CheckWaves(map, report, seen, queue);
            if (!report.WavesPassed) return report;
            report.FinalPassed = CheckFinal(map, report, seen, queue);
            report.Passed = report.FinalPassed;
            return report;
        }

        /// <summary>
        /// Только случай 2 (итоговая карта со стоящими заводами), без волновой расчистки: все нарушения собираются в отчёт
        /// (<see cref="ReachabilityReport.CutOffCells"/>, <see cref="ReachabilityReport.FactoriesWithoutRoute"/>,
        /// <see cref="ReachabilityReport.Unreachable"/>). Для диагностики и тестов валидатора.
        /// </summary>
        public static ReachabilityReport CheckFinalMap(OutpostMap map)
        {
            var report = new ReachabilityReport { ZoneId = map.ZoneId };
            foreach (var f in map.Factories)
            {
                report.Factories++;
                if (f.HasRoute) report.FactoriesWithRoute++;
            }
            report.FinalPassed = CheckFinal(map, report, new bool[map.CellCount], new int[map.CellCount]);
            report.Passed = report.FinalPassed;
            return report;
        }

        /// <summary>Случай 1: волновая расчистка, открытый завод — стена с момента открытия.</summary>
        static bool CheckWaves(OutpostMap map, ReachabilityReport report, bool[] seen, int[] queue)
        {
            var occupied = (int[])map.InitialOccupant.Clone();
            var remaining = new List<MapTarget>();
            foreach (var t in map.Targets)
                if (!t.IsGate) remaining.Add(t);
            int start = Start(map);

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
                        if (TouchesFree(o, seen)) available.Add(o);
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
                        return false;
                    }
                    foreach (var o in available)
                    {
                        // ТЗ 2.2 §4.1: footprint открытого завода остаётся занят зданием.
                        if (!o.IsFactory)
                            foreach (int cell in o.Cells) occupied[cell] = -1;
                        remaining.Remove(o);
                        report.Cleared++;
                    }
                    report.Waves++;
                }
                if (gateRow >= 0)
                {
                    Reach(map, occupied, gateRow, sector, start, seen, queue);
                    if (!TouchesFree(map.Target(map.Gates[sector].TargetId), seen))
                    {
                        report.Diagnostic = map.ZoneId + ": no gate working position in row " + gateRow;
                        report.Unreachable.Add(map.Gates[sector].TargetId);
                        return false;
                    }
                    report.GatesOpened++;
                }
            }
            Reach(map, occupied, -1, map.Gates.Length, start, seen, queue);
            if (remaining.Count > 0 || !seen[map.ExitCell])
            {
                report.Diagnostic = map.ZoneId + ": incomplete clearance or unreachable exit";
                return false;
            }
            return true;
        }

        /// <summary>Случай 2: итоговая карта — r/x сняты, ворота открываются по очереди, заводы стоят.</summary>
        static bool CheckFinal(OutpostMap map, ReachabilityReport report, bool[] seen, int[] queue)
        {
            var factories = new int[map.CellCount];
            for (int i = 0; i < factories.Length; i++) factories[i] = -1;
            foreach (var t in map.Targets)
                if (t.IsFactory)
                    foreach (int cell in t.Cells) factories[cell] = t.Id;
            int start = Start(map);
            bool topology = map.Topology != null;
            var cut = new SortedSet<int>();
            for (int opened = 0; opened <= map.Gates.Length; opened++)
            {
                int gateRow = !topology && opened < map.Gates.Length ? map.Gates[opened].Row : -1;
                Reach(map, factories, gateRow, opened, start, seen, queue);
                for (int cell = 0; cell < map.CellCount; cell++)
                {
                    if (!map.IsPlayable(cell) || factories[cell] >= 0) continue;
                    bool closed = topology ? map.CellGateOrder[cell] >= opened : gateRow >= 0 && map.Row(cell) < gateRow;
                    bool ahead = topology && map.SectorAt(cell) > opened;
                    if (seen[cell] && (closed || ahead))
                    {
                        report.Diagnostic = map.ZoneId + ": gate bypass at cell " + cell + " with " + opened + " gates open";
                        return false;
                    }
                    if (!seen[cell] && !closed && !ahead && cut.Add(cell) && report.Diagnostic == null)
                        report.Diagnostic = map.ZoneId + ": floor cell " + cell + " cut off from the base by standing factories with " +
                                            opened + " gates open";
                }
            }
            report.CutOffCells.AddRange(cut);
            // Последняя стадия: все ворота открыты, seen — достижимость итоговой карты.
            foreach (var t in map.Targets)
            {
                if (TouchesFree(t, seen)) continue;
                report.Unreachable.Add(t.Id);
                if (report.Diagnostic == null)
                    report.Diagnostic = map.ZoneId + ": " + (t.IsFactory ? "factory " : "target ") + t.Id + " has no reachable work position in the final map";
            }
            foreach (var f in map.Factories)
            {
                if (f.HasRoute && seen[f.OutletCell]) continue;
                report.FactoriesWithoutRoute.Add(f.TargetId);
                if (report.Diagnostic == null) report.Diagnostic = map.ZoneId + ": factory " + f.TargetId + " has no pipe route to IntakeCell";
            }
            if (!seen[map.ExitCell] && report.Diagnostic == null) report.Diagnostic = map.ZoneId + ": exit unreachable in the final map";
            return report.CutOffCells.Count == 0 && report.Unreachable.Count == 0 && report.FactoriesWithoutRoute.Count == 0 && seen[map.ExitCell];
        }

        static int Start(OutpostMap map) => map.Cell(map.Width / 2, map.Depth + 1);

        /// <summary>BFS по 4 направлениям (вверх, влево, вправо, вниз), свободные клетки; исходная сетка — row ≥ gateRow.</summary>
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

        static bool TouchesFree(MapTarget o, bool[] seen)
        {
            foreach (int wc in o.WorkCells)
                if (seen[wc]) return true;
            return false;
        }
    }

    public sealed class ReachabilityReport
    {
        public string ZoneId;
        /// <summary>Оба случая ТЗ 2.2 §4.1 пройдены.</summary>
        public bool Passed;
        /// <summary>Случай 1 — волновая расчистка со стоящими заводами.</summary>
        public bool WavesPassed;
        /// <summary>Случай 2 — итоговая карта со стоящими заводами (проверяется, только если пройден случай 1).</summary>
        public bool FinalPassed;
        /// <summary>Освобождено r/s/x (открытые заводы считаются расчищенными целями).</summary>
        public int Cleared;
        public int GatesOpened;
        public int Waves;
        /// <summary>Заводов на карте и заводов с маршрутом трубы до IntakeCell.</summary>
        public int Factories;
        public int FactoriesWithRoute;
        public string Diagnostic;
        /// <summary>Цели без достижимой рабочей клетки (случай 1 — оставшиеся в секторе, случай 2 — в итоговой карте).</summary>
        public readonly List<int> Unreachable = new List<int>();
        /// <summary>Клетки пола, отрезанные стоящими заводами от базы хотя бы на одной стадии ворот (случай 2), по возрастанию.</summary>
        public readonly List<int> CutOffCells = new List<int>();
        /// <summary>Заводы без свободной соседней клетки, связанной с IntakeCell (случай 2).</summary>
        public readonly List<int> FactoriesWithoutRoute = new List<int>();
    }
}
