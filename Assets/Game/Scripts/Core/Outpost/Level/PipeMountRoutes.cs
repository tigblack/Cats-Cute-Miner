using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>Маршрут трубы завода по постоянным поверхностям (вид, не производство): клетки от завода до точки входа лагеря.</summary>
    public sealed class PipeMountRoute
    {
        public int TargetId;
        /// <summary>Клетки маршрута от первой клетки у footprint завода до конечной клетки стены лагеря включительно.</summary>
        public int[] Cells = new int[0];
        /// <summary>Клетка маршрута — пол (переход через проём под настилом), а не порода, опора или машина.</summary>
        public bool[] Crossing = new bool[0];
        /// <summary>Клеток пола на маршруте (переходы через проёмы).</summary>
        public int CrossingCells;
        /// <summary>Клеток глубокой породы (запасной путь, когда у стены прохода нет).</summary>
        public int DeepRockCells;

        public bool HasRoute => Cells.Length > 0;
    }

    /// <summary>
    /// Прокладка видимых труб заводов вдоль постоянных поверхностей (Docs/Rework/MINE_DRESSING.md, «Трубы»): по клеткам породы,
    /// граничащим с полом (стены тоннелей и камер, в том числе вокруг лагеря), и по постоянным препятствиям с креплением. Пол
    /// пересекается только в проёмах (вход в камеру, устье над лагерем) — короткими переходами под настилом, ворота, footprint
    /// заводов и площадка лагеря не пересекаются никогда. Глубокая порода — дорогой запасной путь, без прямой через поле.
    /// <para>Дейкстра по клеткам с поворотом в состоянии (штраф за поворот — ровные трубы), источники — клетки у footprint
    /// завода, цель — клетка правой стены лагеря у склада. Заводы прокладываются по возрастанию ID, уже проложенные клетки
    /// дешевле — трубы сходятся в общие стволы. Результат детерминирован и не сохраняется; время в пути и производство
    /// считаются по <see cref="MapFactory.RouteSteps"/> (кратчайший путь, ТЗ 2.2 §5) и от этого вида не зависят.</para>
    /// </summary>
    public static class PipeMountRoutes
    {
        public const int RimCost = 4;
        public const int SharedRimCost = 2;
        public const int MountPropCost = 4;
        public const int PropCost = 10;
        public const int CrossCost = 30;
        public const int OccupiedExtra = 30;
        public const int DeepRockCost = 90;
        public const int TurnCost = 3;

        /// <summary>Клетка стены лагеря, где труба уходит к складу: порода справа от площадки в нижнем ряду лагеря.</summary>
        public static int CampEndCell(OutpostMap map)
        {
            int col = map.Width / 2 + 5, row = map.Depth + OutpostLevelAdapter.BaseRows - 1;
            if (col >= map.Width) return -1;
            int cell = map.Cell(col, row);
            return map.IsPlayable(cell) ? -1 : cell;
        }

        public static PipeMountRoute[] Build(OutpostMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var factories = map.Factories ?? new MapFactory[0];
            var result = new PipeMountRoute[factories.Length];
            int end = CampEndCell(map);
            int n = map.CellCount;
            var cost = new int[n];
            var forbidden = new bool[n];
            var floor = new bool[n];
            var rim = new bool[n];
            for (int c = 0; c < n; c++)
            {
                int row = map.Row(c);
                bool camp = row >= map.Depth;
                floor[c] = map.IsFloor(c);
                if (floor[c] && (camp || map.CellGateOrder[c] >= 0)) forbidden[c] = true;
            }
            foreach (var t in map.Targets)
                if (t.IsFactory)
                    foreach (int c in t.Cells) forbidden[c] = true;
            for (int c = 0; c < n; c++)
            {
                if (floor[c]) continue;
                int col = map.Col(c), row = map.Row(c);
                for (int dy = -1; dy <= 1 && !rim[c]; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = col + dx, y = row + dy;
                        if ((dx == 0 && dy == 0) || x < 0 || x >= map.Width || y < 0 || y >= map.Rows) continue;
                        if (floor[map.Cell(x, y)]) { rim[c] = true; break; }
                    }
            }
            var used = new bool[n];
            for (int c = 0; c < n; c++)
            {
                if (forbidden[c]) continue;
                if (map.IsProp(c)) cost[c] = map.Props[map.PropAt[c]].PipeMount ? MountPropCost : PropCost;
                else if (floor[c]) cost[c] = CrossCost + (map.InitialOccupant[c] >= 0 ? OccupiedExtra : 0);
                else cost[c] = rim[c] ? RimCost : DeepRockCost;
            }
            if (end < 0) return Fill(result, factories);
            var dist = new int[n * 4];
            var prev = new int[n * 4];
            var heap = new SortedSet<(int Cost, int State)>();
            for (int k = 0; k < factories.Length; k++)
            {
                var f = factories[k];
                var target = map.Target(f.TargetId);
                result[k] = new PipeMountRoute { TargetId = f.TargetId };
                if (target == null) continue;
                for (int i = 0; i < dist.Length; i++) { dist[i] = int.MaxValue; prev[i] = -1; }
                heap.Clear();
                var own = new HashSet<int>(target.Cells);
                foreach (int fc in target.Cells)
                {
                    int col = map.Col(fc), row = map.Row(fc);
                    for (int d = 0; d < 4; d++)
                    {
                        int x = col + Dx(d), y = row + Dy(d);
                        if (x < 0 || x >= map.Width || y < 0 || y >= map.Rows) continue;
                        int c = map.Cell(x, y);
                        if (own.Contains(c) || forbidden[c]) continue;
                        int s = c * 4 + d;
                        int v = Step(c);
                        if (v < dist[s]) { dist[s] = v; heap.Add((v, s)); }
                    }
                }
                int best = -1;
                while (heap.Count > 0)
                {
                    var top = heap.Min;
                    heap.Remove(top);
                    if (top.Cost != dist[top.State]) continue;
                    int cell = top.State / 4, dir = top.State % 4;
                    if (cell == end) { best = top.State; break; }
                    int col = map.Col(cell), row = map.Row(cell);
                    for (int d = 0; d < 4; d++)
                    {
                        int x = col + Dx(d), y = row + Dy(d);
                        if (x < 0 || x >= map.Width || y < 0 || y >= map.Rows) continue;
                        int c = map.Cell(x, y);
                        if (forbidden[c]) continue;
                        int s = c * 4 + d;
                        int v = top.Cost + Step(c) + (d != dir ? TurnCost : 0);
                        if (v < dist[s])
                        {
                            if (dist[s] != int.MaxValue) heap.Remove((dist[s], s));
                            dist[s] = v;
                            prev[s] = top.State;
                            heap.Add((v, s));
                        }
                    }
                }
                if (best < 0) continue;
                var cells = new List<int>();
                for (int s = best; s >= 0; s = prev[s]) cells.Add(s / 4);
                cells.Reverse();
                var route = result[k];
                route.Cells = cells.ToArray();
                route.Crossing = new bool[cells.Count];
                for (int i = 0; i < cells.Count; i++)
                {
                    int c = cells[i];
                    route.Crossing[i] = floor[c] && !map.IsProp(c);
                    if (route.Crossing[i]) route.CrossingCells++;
                    else if (!floor[c] && !rim[c]) route.DeepRockCells++;
                    used[c] = true;
                }
            }
            return result;

            int Step(int c) => used[c] && !floor[c] && rim[c] ? SharedRimCost : cost[c];
        }

        static PipeMountRoute[] Fill(PipeMountRoute[] result, MapFactory[] factories)
        {
            for (int k = 0; k < factories.Length; k++) result[k] = new PipeMountRoute { TargetId = factories[k].TargetId };
            return result;
        }

        // Порядок направлений как у навигации: вверх, влево, вправо, вниз.
        static int Dx(int d) => d == 1 ? -1 : d == 2 ? 1 : 0;
        static int Dy(int d) => d == 0 ? -1 : d == 3 ? 1 : 0;
    }
}
