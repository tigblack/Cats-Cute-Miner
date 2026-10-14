using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Навигация по клеточной сетке (ТЗ 6.4, 8.4): BFS по 4 направлениям, порядок соседей — вверх (row−1), влево, вправо,
    /// вниз. Проходимы клетки маски без живого footprint и закрытых ворот; старые карты используют row ≥ BarrierRow.
    /// Поля BFS кэшируются по (источник, NavVersion);
    /// массивы переиспользуются, поэтому запросы без смены версии не выделяют память.
    /// </summary>
    public sealed class GridNavigation
    {
        readonly OutpostMap m_Map;
        readonly int[] m_Occupant;
        readonly Dictionary<int, BfsField> m_Cache = new Dictionary<int, BfsField>();
        readonly Stack<BfsField> m_Pool = new Stack<BfsField>();
        readonly int[] m_Queue;
        int m_Version = int.MinValue;
        int m_BarrierRow;
        int m_GatesOpened;

        /// <param name="occupant">Живая занятость клеток (Id цели или −1); массив принадлежит симуляции.</param>
        public GridNavigation(OutpostMap map, int[] occupant)
        {
            m_Map = map;
            m_Occupant = occupant;
            m_Queue = new int[map.CellCount];
        }

        public int BarrierRow => m_BarrierRow;
        public int Version => m_Version;

        /// <summary>Сменить версию навигации: кэш BFS сбрасывается, если версия или барьер изменились.</summary>
        public void Sync(int navVersion, int barrierRow)
        {
            if (navVersion == m_Version && barrierRow == m_BarrierRow) return;
            m_Version = navVersion;
            m_BarrierRow = barrierRow;
            foreach (var kv in m_Cache) m_Pool.Push(kv.Value);
            m_Cache.Clear();
        }

        /// <summary>Branching geometry: gates block passage cells, independently of their row.</summary>
        public void SyncGates(int navVersion, int gatesOpened)
        {
            if (navVersion == m_Version && gatesOpened == m_GatesOpened) return;
            m_Version = navVersion;
            m_GatesOpened = gatesOpened;
            m_BarrierRow = 0;
            foreach (var kv in m_Cache) m_Pool.Push(kv.Value);
            m_Cache.Clear();
        }

        public bool Passable(int cell) =>
            m_Map.IsPlayable(cell) && m_Occupant[cell] < 0 && (m_Map.Topology == null
                ? cell / m_Map.Width >= m_BarrierRow
                : m_Map.CellGateOrder[cell] < 0 || m_Map.CellGateOrder[cell] < m_GatesOpened);

        /// <summary>Поле BFS от клетки (кэш по версии).</summary>
        public BfsField From(int source)
        {
            if (m_Cache.TryGetValue(source, out var field)) return field;
            field = m_Pool.Count > 0 ? m_Pool.Pop() : new BfsField(m_Map.CellCount);
            Fill(field, source);
            m_Cache[source] = field;
            return field;
        }

        /// <summary>Длина кратчайшего пути в рёбрах или −1.</summary>
        public int Distance(int from, int to) => From(from).Dist[to];

        /// <summary>Клетки пути после стартовой до цели включительно; пустой массив, если from == to; null — недостижимо.</summary>
        public int[] BuildPath(int from, int to)
        {
            var f = From(from);
            int d = f.Dist[to];
            if (d < 0) return null;
            var path = new int[d];
            int cell = to;
            for (int i = d - 1; i >= 0; i--)
            {
                path[i] = cell;
                cell = f.Parent[cell];
            }
            return path;
        }

        void Fill(BfsField f, int source)
        {
            var dist = f.Dist;
            var parent = f.Parent;
            for (int i = 0; i < dist.Length; i++)
            {
                dist[i] = -1;
                parent[i] = -1;
            }
            f.Source = source;
            if (!Passable(source)) return;
            int width = m_Map.Width, rows = m_Map.Rows;
            int head = 0, tail = 0;
            dist[source] = 0;
            m_Queue[tail++] = source;
            while (head < tail)
            {
                int cell = m_Queue[head++];
                int col = cell % width, row = cell / width;
                int nd = dist[cell] + 1;
                // вверх, влево, вправо, вниз
                if (row - 1 >= 0) Visit(cell - width);
                if (col - 1 >= 0) Visit(cell - 1);
                if (col + 1 < width) Visit(cell + 1);
                if (row + 1 < rows) Visit(cell + width);

                void Visit(int n)
                {
                    if (dist[n] >= 0 || !Passable(n)) return;
                    dist[n] = nd;
                    parent[n] = cell;
                    m_Queue[tail++] = n;
                }
            }
        }
    }

    /// <summary>Расстояния и родительские ссылки BFS от одной клетки (−1 — недостижимо).</summary>
    public sealed class BfsField
    {
        public int Source;
        public readonly int[] Dist;
        public readonly int[] Parent;

        public BfsField(int cellCount)
        {
            Dist = new int[cellCount];
            Parent = new int[cellCount];
        }
    }
}
