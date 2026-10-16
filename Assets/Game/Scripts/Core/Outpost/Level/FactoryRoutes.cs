using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Заводы карты и маршруты труб (ТЗ 2.2 §5). Вызывается из <see cref="OutpostMap"/>.BuildIndex в конце
    /// OutpostLevelAdapter.Build и MineTopologyAdapter.Apply; результат не сохраняется и при одинаковой карте одинаков.
    /// <para>Проходимость — итоговая карта: стены — непроходимые клетки (постоянная порода, <see cref="OutpostMap.IsPlayable"/>) и
    /// footprint всех заводов; живые цели r/x не учитываются (их всё равно разрушат), ворота открыты, клетки площадки лагеря
    /// проходимы (построек базы в навигации нет).</para>
    /// <para>Один BFS от <see cref="OutpostMap.IntakeCell"/> по 4 направлениям (вверх, влево, вправо, вниз) даёт расстояние до каждой
    /// клетки. Клетка выхода трубы — свободная соседняя с footprint клетка с наименьшим расстоянием, при равенстве — с меньшим
    /// индексом. <see cref="MapFactory.RouteSteps"/> — это расстояние в рёбрах («RouteCells» ТЗ §5), время в пути — по нему
    /// (<see cref="FactoryMath.TransitTicks"/>).</para>
    /// <para>Клетки маршрута (<see cref="MapFactory.RouteCells"/>, от выхода до IntakeCell включительно) — один из кратчайших путей:
    /// на каждом шаге — соседняя клетка, ближе к IntakeCell на 1; если можно идти прямо, труба не поворачивает, иначе — первая
    /// подходящая в порядке вверх, влево, вправо, вниз. Так трубы в общих проходах идут ровными отрезками.</para>
    /// <para>Нет свободной соседней клетки, связанной с IntakeCell, — маршрута нет: RouteSteps = −1, OutletCell = −1,
    /// RouteCells пуст. В топологии wide-branching-mines-3 маршрут есть у всех 1 232 заводов (валидатор
    /// <see cref="OutpostReachability"/>, случай 2).</para>
    /// </summary>
    public static class FactoryRoutes
    {
        public static MapFactory[] Build(OutpostMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            int count = 0;
            foreach (var t in map.Targets)
                if (t.IsFactory) count++;
            if (count == 0) return Array.Empty<MapFactory>();

            int n = map.CellCount;
            var wall = new bool[n];
            for (int cell = 0; cell < n; cell++) wall[cell] = !map.IsPlayable(cell);
            foreach (var t in map.Targets)
                if (t.IsFactory)
                    foreach (int cell in t.Cells)
                        wall[cell] = true;
            var dist = Distances(map, wall);

            var result = new MapFactory[count];
            int k = 0;
            foreach (var t in map.Targets)
            {
                if (!t.IsFactory) continue;
                var f = new MapFactory
                {
                    TargetId = t.Id,
                    TypeId = t.TypeId,
                    RequiredCardId = t.CardId,
                    RequiredCardLevel = t.CardLevel,
                };
                int outlet = Outlet(map, t, dist);
                if (outlet >= 0)
                {
                    f.OutletCell = outlet;
                    f.RouteSteps = dist[outlet];
                    f.RouteCells = Path(map, dist, outlet);
                }
                result[k++] = f;
            }
            return result;
        }

        /// <summary>Расстояние в рёбрах от IntakeCell до каждой клетки (−1 — недостижима).</summary>
        static int[] Distances(OutpostMap map, bool[] wall)
        {
            int n = map.CellCount;
            var dist = new int[n];
            for (int i = 0; i < n; i++) dist[i] = -1;
            int start = map.IntakeCell;
            if (start < 0 || start >= n || wall[start]) return dist;
            var queue = new int[n];
            int head = 0, tail = 0;
            dist[start] = 0;
            queue[tail++] = start;
            while (head < tail)
            {
                int cell = queue[head++];
                int col = map.Col(cell), row = map.Row(cell);
                Visit(col, row - 1);
                Visit(col - 1, row);
                Visit(col + 1, row);
                Visit(col, row + 1);

                void Visit(int c, int r)
                {
                    if (c < 0 || c >= map.Width || r < 0 || r >= map.Rows) return;
                    int next = map.Cell(c, r);
                    if (wall[next] || dist[next] >= 0) return;
                    dist[next] = dist[cell] + 1;
                    queue[tail++] = next;
                }
            }
            return dist;
        }

        /// <summary>Свободная соседняя с footprint клетка, ближайшая к IntakeCell; при равенстве — меньший индекс; −1 — нет.</summary>
        static int Outlet(OutpostMap map, MapTarget factory, int[] dist)
        {
            var own = new HashSet<int>(factory.Cells);
            int best = -1;
            foreach (int cell in factory.Cells)
            {
                int col = map.Col(cell), row = map.Row(cell);
                Consider(col, row - 1);
                Consider(col - 1, row);
                Consider(col + 1, row);
                Consider(col, row + 1);
            }
            return best;

            void Consider(int c, int r)
            {
                if (c < 0 || c >= map.Width || r < 0 || r >= map.Rows) return;
                int cell = map.Cell(c, r);
                if (own.Contains(cell) || dist[cell] < 0) return;
                if (best < 0 || dist[cell] < dist[best] || (dist[cell] == dist[best] && cell < best)) best = cell;
            }
        }

        /// <summary>Кратчайший путь от выхода до IntakeCell: шаг на клетку с расстоянием на 1 меньше, прямо — если можно.</summary>
        static int[] Path(OutpostMap map, int[] dist, int outlet)
        {
            var path = new int[dist[outlet] + 1];
            int at = outlet, dc = 0, dr = 0;
            path[0] = at;
            for (int i = 1; i < path.Length; i++)
            {
                int col = map.Col(at), row = map.Row(at), want = dist[at] - 1, next = -1;
                if ((dc != 0 || dr != 0) && Closer(col + dc, row + dr)) next = map.Cell(col + dc, row + dr);
                else if (Closer(col, row - 1)) next = map.Cell(col, row - 1);
                else if (Closer(col - 1, row)) next = map.Cell(col - 1, row);
                else if (Closer(col + 1, row)) next = map.Cell(col + 1, row);
                else if (Closer(col, row + 1)) next = map.Cell(col, row + 1);
                if (next < 0) throw new InvalidOperationException("FactoryRoutes: разрыв маршрута у клетки " + at);
                dc = map.Col(next) - col;
                dr = map.Row(next) - row;
                path[i] = at = next;

                bool Closer(int c, int r) => c >= 0 && c < map.Width && r >= 0 && r < map.Rows && dist[map.Cell(c, r)] == want;
            }
            return path;
        }
    }
}
