using System;
using System.Collections.Generic;
using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Раскладка труб заводов (ТЗ 2.2 §5, VIEW_API §11) — один раз на шахту, без Unity-объектов. Координаты — целые пиксели арта
    /// в корне поля (x = столбец × 32, z = (Depth − строка − 1) × 32 + пиксель в клетке); пиксель (x, z) занимает квадрат
    /// [x, x + 1] × [z, z + 1] пикселей. Ось трубы в клетке — пиксель 16 по обеим осям.
    /// <para>
    /// Путь трубы: заглушка у footprint завода → клетки <see cref="MapFactory.RouteCells"/> до IntakeCell → декоративный отрезок
    /// по лагерю (на строку вниз и вправо до столбца склада) → вход склада. Ребро — пара соседних узлов пути; одно ребро делят все
    /// трубы, идущие по нему. Порядок труб на ребре сохраняется по ходу: трубы, пришедшие слева от направления ребра, — слева,
    /// прямые — посередине, справа — справа; так трубы не пересекаются на поворотах и слияниях. Смещение k-й трубы от оси —
    /// (2k − n + 1) × 2 пикселя (шаг 4 пикселя, <see cref="LanePitch"/>), до <see cref="MaxLanes"/> труб на ребро. Больше труб и все
    /// рёбра лагеря при двух и более трубах — «жгут»: полоски товаров с шагом 1 пиксель в общем тёмном контуре; на входе трубы
    /// в жгут — муфта.
    /// </para>
    /// </summary>
    public sealed class PipeLayout
    {
        public const int CellPx = PixelArt.CellPixels;
        /// <summary>Пиксель оси трубы внутри клетки.</summary>
        public const int AxisPx = 16;
        /// <summary>Шаг соседних труб прохода (пиксели): толщина 3 и зазор 1.</summary>
        public const int LanePitch = 4;
        /// <summary>Сколько отдельных труб помещается в проход в клетку без наложения (7 × 4 − 1 = 27 из 32 пикселей).</summary>
        public const int MaxLanes = 7;
        /// <summary>Толщина отдельной трубы: контур и полоса цвета товара.</summary>
        public const int PipeWidth = 3;

        /// <summary>Отрезок трубы: от точки A до B включительно (оси совпадают по x или по z), режим отдельной трубы или жгута.</summary>
        public struct Segment
        {
            public Vector2Int A, B;
            public bool Harness;
            /// <summary>Расстояние от начала трубы до A (пиксели пути).</summary>
            public int Start;
            public int Length => Mathf.Abs(B.x - A.x) + Mathf.Abs(B.y - A.y);
        }

        /// <summary>Контур жгута на ребре: прямоугольник пикселей [Min, Max] включительно.</summary>
        public struct Band
        {
            public Vector2Int Min, Max;
            /// <summary>Трубы ребра (индексы <see cref="Pipes"/>) и расстояние от начала каждой до ребра.</summary>
            public int[] Pipes;
            public int[] StartOnPipe;
        }

        /// <summary>Муфта входа трубы в жгут: центр и трубы, которые здесь входят (с расстоянием до муфты).</summary>
        public struct Coupling
        {
            public Vector2Int Center;
            public int[] Pipes;
            public int[] StartOnPipe;
        }

        public sealed class Pipe
        {
            public int TargetId;
            public string TypeId;
            /// <summary>Точки ломаной (концы отрезков), от заглушки к входу склада.</summary>
            public readonly List<Vector2Int> Points = new List<Vector2Int>();
            public readonly List<Segment> Segments = new List<Segment>();
            /// <summary>Центр заглушки у завода и направление выхода трубы (от завода).</summary>
            public Vector2Int CapCenter;
            public Vector2Int OutDir;
            public int TotalLength;
        }

        public readonly List<Pipe> Pipes = new List<Pipe>();
        public readonly List<Band> Bands = new List<Band>();
        public readonly List<Coupling> Couplings = new List<Coupling>();
        /// <summary>Наибольшее число труб на одном ребре (для отчёта и тестов).</summary>
        public int MaxShared { get; private set; }

        // ───────────── Построение ─────────────

        sealed class Edge
        {
            public int From, To;          // индексы узлов (клетка или виртуальный узел)
            public Vector2Int Dir;
            public readonly List<int> Pipes = new List<int>();
            public readonly List<int> PathIndex = new List<int>();   // индекс ребра в пути трубы
            public int[] Order;          // трубы по порядку слева направо (относительно Dir)
            public bool Harness;
            public bool Camp;
            public int Rank;              // оставшиеся шаги пути от From (у всех труб ребра одинаковы)
        }

        readonly Dictionary<long, Edge> m_Edges = new Dictionary<long, Edge>();

        /// <summary>
        /// Раскладка по карте: трубы всех заводов с маршрутом (<see cref="OutpostMap.Factories"/>), конец — вход склада
        /// <paramref name="inletPx"/> в столбце <paramref name="warehouseCol"/> (−1 — склада нет: трубы кончаются у IntakeCell).
        /// </summary>
        public static PipeLayout Build(OutpostMap map, int warehouseCol, Vector2Int inletPx)
        {
            var layout = new PipeLayout();
            if (map?.Factories == null || map.Factories.Length == 0) return layout;
            layout.BuildInternal(map, warehouseCol, inletPx);
            return layout;
        }

        /// <summary>Узел пути: клетка карты (≥ 0) или вход склада (<see cref="InletNode"/>).</summary>
        const int InletNode = -2;

        Vector2Int NodePx(OutpostMap map, int node, Vector2Int inletPx) =>
            node == InletNode ? inletPx : CellAxis(map, node);

        /// <summary>Пиксель оси трубы в клетке.</summary>
        public static Vector2Int CellAxis(OutpostMap map, int cell) =>
            new Vector2Int(map.Col(cell) * CellPx + AxisPx, (map.Depth - map.Row(cell) - 1) * CellPx + AxisPx);

        static Vector2Int Cw(Vector2Int d) => new Vector2Int(d.y, -d.x);

        static Vector2Int Sign(Vector2Int v) => new Vector2Int(Math.Sign(v.x), Math.Sign(v.y));

        void BuildInternal(OutpostMap map, int warehouseCol, Vector2Int inletPx)
        {
            // Декоративный отрезок лагеря: от IntakeCell на строку вниз, затем по этой строке к столбцу склада и в его вход.
            var tail = new List<int>();
            int intake = map.IntakeCell;
            if (warehouseCol >= 0 && intake >= 0)
            {
                int col = map.Col(intake), row = map.Row(intake);
                if (row + 1 < map.Rows)
                {
                    row++;
                    tail.Add(map.Cell(col, row));
                    int step = warehouseCol > col ? 1 : -1;
                    for (int c = col; c != warehouseCol; )
                    {
                        c += step;
                        tail.Add(map.Cell(c, row));
                    }
                }
                tail.Add(InletNode);
            }

            // Пути труб (узлы) и направление выхода из footprint.
            var paths = new List<int[]>();
            foreach (var f in map.Factories)
            {
                if (f == null || !f.HasRoute || f.RouteCells == null || f.RouteCells.Length == 0) continue;
                var target = map.Target(f.TargetId);
                if (target == null) continue;
                var nodes = new List<int>(f.RouteCells.Length + tail.Count);
                nodes.AddRange(f.RouteCells);
                foreach (int t in tail)
                    if (nodes.Count == 0 || nodes[nodes.Count - 1] != t) nodes.Add(t);
                var pipe = new Pipe { TargetId = f.TargetId, TypeId = f.TypeId };
                pipe.OutDir = OutletDir(map, target, f.OutletCell);
                Pipes.Add(pipe);
                paths.Add(nodes.ToArray());
            }

            // Рёбра и трубы на них.
            for (int p = 0; p < paths.Count; p++)
            {
                var nodes = paths[p];
                for (int i = 0; i + 1 < nodes.Length; i++)
                {
                    long key = ((long)(nodes[i] + 4) << 32) | (uint)(nodes[i + 1] + 4);
                    if (!m_Edges.TryGetValue(key, out var e))
                    {
                        var a = NodePx(map, nodes[i], inletPx);
                        var b = NodePx(map, nodes[i + 1], inletPx);
                        e = new Edge
                        {
                            From = nodes[i], To = nodes[i + 1], Dir = Sign(b - a),
                            Camp = nodes[i] >= 0 && map.Row(nodes[i]) >= map.Depth,
                            Rank = nodes.Length - 1 - i,
                        };
                        m_Edges.Add(key, e);
                    }
                    e.Pipes.Add(p);
                    e.PathIndex.Add(i);
                }
            }

            // Порядок труб на рёбрах: от дальних рёбер к входу склада, с сохранением порядка входящих рёбер.
            var edges = new List<Edge>(m_Edges.Values);
            edges.Sort((x, y) => x.Rank != y.Rank ? y.Rank.CompareTo(x.Rank) : (x.From != y.From ? x.From.CompareTo(y.From) : x.To.CompareTo(y.To)));
            var keys = new List<(int Cat, int Index, int Id, int Pipe)>();
            foreach (var e in edges)
            {
                keys.Clear();
                for (int k = 0; k < e.Pipes.Count; k++)
                {
                    int p = e.Pipes[k], i = e.PathIndex[k];
                    Vector2Int a;
                    int index;
                    if (i == 0)
                    {
                        a = Pipes[p].OutDir;
                        index = 0;
                    }
                    else
                    {
                        var prev = EdgeOf(paths[p], i - 1);
                        a = prev.Dir;
                        index = Array.IndexOf(prev.Order, p);
                    }
                    int cat = a == Cw(e.Dir) ? 0 : (a == -Cw(e.Dir) ? 2 : 1);
                    keys.Add((cat, index, Pipes[p].TargetId, p));
                }
                keys.Sort((x, y) => x.Cat != y.Cat ? x.Cat.CompareTo(y.Cat) : (x.Index != y.Index ? x.Index.CompareTo(y.Index) : x.Id.CompareTo(y.Id)));
                e.Order = new int[keys.Count];
                for (int k = 0; k < keys.Count; k++) e.Order[k] = keys[k].Pipe;
                e.Harness = e.Order.Length > MaxLanes || (e.Camp && e.Order.Length >= 2) || (e.From == InletNode || e.To == InletNode) && e.Order.Length >= 2;
                if (e.Order.Length > MaxShared) MaxShared = e.Order.Length;
            }

            // Ломаные труб.
            var couplingAt = new Dictionary<Vector2Int, List<(int Pipe, int Start)>>();
            for (int p = 0; p < paths.Count; p++)
                BuildPolyline(map, paths[p], p, inletPx, couplingAt);

            // Контуры жгутов по рёбрам.
            foreach (var e in edges)
            {
                if (!e.Harness) continue;
                int n = e.Order.Length;
                var a = NodePx(map, e.From, inletPx);
                var b = NodePx(map, e.To, inletPx);
                int lo = HarnessOffset(0, n) - 1, hi = HarnessOffset(n - 1, n) + 1;
                // Вдоль ребра — с запасом на полуширину на обоих концах: углы и стыки рёбер закрыты.
                int ext = Math.Max(-lo, hi);
                var cw = Cw(e.Dir);
                var p0 = a - e.Dir * ext + cw * lo;
                var p1 = b + e.Dir * ext + cw * hi;
                var band = new Band
                {
                    Min = new Vector2Int(Math.Min(p0.x, p1.x), Math.Min(p0.y, p1.y)),
                    Max = new Vector2Int(Math.Max(p0.x, p1.x), Math.Max(p0.y, p1.y)),
                    Pipes = new int[n],
                    StartOnPipe = new int[n],
                };
                for (int k = 0; k < n; k++)
                {
                    int p = e.Order[k];
                    band.Pipes[k] = p;
                    band.StartOnPipe[k] = DistanceTo(Pipes[p], a);
                }
                Bands.Add(band);
            }
            foreach (var kv in couplingAt)
            {
                var c = new Coupling { Center = kv.Key, Pipes = new int[kv.Value.Count], StartOnPipe = new int[kv.Value.Count] };
                for (int k = 0; k < kv.Value.Count; k++)
                {
                    c.Pipes[k] = kv.Value[k].Pipe;
                    c.StartOnPipe[k] = kv.Value[k].Start;
                }
                Couplings.Add(c);
            }
            Couplings.Sort((x, y) => x.Center.y != y.Center.y ? x.Center.y.CompareTo(y.Center.y) : x.Center.x.CompareTo(y.Center.x));
        }

        Edge EdgeOf(int[] nodes, int i) =>
            m_Edges[((long)(nodes[i] + 4) << 32) | (uint)(nodes[i + 1] + 4)];

        static int LaneOffset(int k, int n) => (2 * k - n + 1) * (LanePitch / 2);

        static int HarnessOffset(int k, int n) => k - (n - 1) / 2;

        static int OffsetOn(Edge e, int pipe)
        {
            int k = Array.IndexOf(e.Order, pipe);
            return e.Harness ? HarnessOffset(k, e.Order.Length) : LaneOffset(k, e.Order.Length);
        }

        /// <summary>Направление выхода трубы из footprint: от клетки завода, соседней с клеткой выхода, к ней.</summary>
        static Vector2Int OutletDir(OutpostMap map, MapTarget target, int outlet)
        {
            if (outlet < 0) return Vector2Int.up;
            int oc = map.Col(outlet), orow = map.Row(outlet);
            foreach (int cell in target.Cells)
            {
                int c = map.Col(cell), r = map.Row(cell);
                if (c == oc && r == orow + 1) return Vector2Int.up;      // завод ниже: выход вверх
                if (c == oc && r == orow - 1) return Vector2Int.down;
                if (r == orow && c == oc - 1) return Vector2Int.right;
                if (r == orow && c == oc + 1) return Vector2Int.left;
            }
            return Vector2Int.up;
        }

        void BuildPolyline(OutpostMap map, int[] nodes, int p, Vector2Int inletPx, Dictionary<Vector2Int, List<(int, int)>> couplingAt)
        {
            var pipe = Pipes[p];
            var pts = pipe.Points;
            var modes = new List<bool>();   // modes[i] — режим отрезка pts[i] → pts[i + 1]
            var start = NodePx(map, nodes[0], inletPx);
            var outDir = pipe.OutDir;
            // Начало — граница footprint и клетки выхода; выход вверх — от верха здания (оно на 16 пикселей выше footprint).
            Vector2Int origin;
            if (outDir == Vector2Int.up) origin = start;
            else if (outDir == Vector2Int.down) origin = start + new Vector2Int(0, CellPx - 1 - AxisPx);
            else if (outDir == Vector2Int.right) origin = start - new Vector2Int(AxisPx, 0);
            else origin = start + new Vector2Int(CellPx - 1 - AxisPx, 0);
            pipe.CapCenter = origin + outDir * 6;
            pts.Add(origin);

            var joins = new List<Vector2Int>();
            Vector2Int inDir = outDir;
            int inOff = 0;
            bool inHarness = false;
            for (int i = 0; i < nodes.Length; i++)
            {
                var basePx = NodePx(map, nodes[i], inletPx);
                if (i == nodes.Length - 1)
                {
                    AddPoint(pts, modes, basePx + Cw(inDir) * inOff, inHarness);
                    break;
                }
                var e = EdgeOf(nodes, i);
                int outOff = OffsetOn(e, p);
                var b = e.Dir;
                if (e.Harness && !inHarness) joins.Add(basePx);
                if (b == inDir || b == -inDir)
                {
                    AddPoint(pts, modes, basePx + Cw(inDir) * inOff, inHarness);
                    // Сдвиг на соседнюю полосу (слияние) — короткая перемычка поперёк хода.
                    if (outOff != inOff) AddPoint(pts, modes, basePx + Cw(b) * outOff, e.Harness);
                }
                else AddPoint(pts, modes, basePx + Cw(inDir) * inOff + Cw(b) * outOff, inHarness);
                inDir = b;
                inOff = outOff;
                inHarness = e.Harness;
            }

            Simplify(pts, modes);
            int run = 0;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var seg = new Segment { A = pts[i], B = pts[i + 1], Harness = modes[i], Start = run };
                run += seg.Length;
                pipe.Segments.Add(seg);
            }
            pipe.TotalLength = run;

            // Муфты: вход трубы в жгут, с расстоянием по трубе до неё.
            foreach (var at in joins)
            {
                if (!couplingAt.TryGetValue(at, out var list)) couplingAt[at] = list = new List<(int, int)>();
                list.Add((p, DistanceTo(pipe, at)));
            }
        }

        static void AddPoint(List<Vector2Int> pts, List<bool> modes, Vector2Int v, bool segmentHarness)
        {
            if (pts.Count > 0 && pts[pts.Count - 1] == v) return;
            if (pts.Count > 0) modes.Add(segmentHarness);
            pts.Add(v);
        }

        /// <summary>Точки на одной прямой с одинаковым режимом соседних отрезков схлопываются; косой шаг (не бывает) — в два отрезка.</summary>
        static void Simplify(List<Vector2Int> pts, List<bool> modes)
        {
            for (int i = pts.Count - 2; i >= 1; i--)
            {
                var a = pts[i - 1];
                var b = pts[i];
                var c = pts[i + 1];
                bool collinear = (a.x == b.x && b.x == c.x && Math.Sign(b.y - a.y) == Math.Sign(c.y - b.y)) ||
                                 (a.y == b.y && b.y == c.y && Math.Sign(b.x - a.x) == Math.Sign(c.x - b.x));
                if (!collinear || modes[i - 1] != modes[i]) continue;
                pts.RemoveAt(i);
                modes.RemoveAt(i);
            }
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                if (a.x == b.x || a.y == b.y) continue;
                pts.Insert(i + 1, new Vector2Int(b.x, a.y));
                modes.Insert(i + 1, modes[i]);
            }
        }

        /// <summary>Расстояние по трубе до ближайшей к <paramref name="at"/> точки ломаной.</summary>
        static int DistanceTo(Pipe pipe, Vector2Int at)
        {
            int best = int.MaxValue, result = 0;
            foreach (var s in pipe.Segments)
            {
                int len = s.Length;
                var dir = Sign(s.B - s.A);
                int proj = dir.x != 0 ? Mathf.Clamp((at.x - s.A.x) * dir.x, 0, len) : Mathf.Clamp((at.y - s.A.y) * dir.y, 0, len);
                var q = s.A + dir * proj;
                int dist = Mathf.Abs(q.x - at.x) + Mathf.Abs(q.y - at.y);
                if (dist >= best) continue;
                best = dist;
                result = s.Start + proj;
            }
            return result;
        }

        /// <summary>Точка трубы на расстоянии <paramref name="distance"/> от заглушки и направление отрезка там (пиксели).</summary>
        public static Vector2 PointAt(Pipe pipe, float distance, out bool vertical)
        {
            vertical = true;
            var segs = pipe.Segments;
            if (segs.Count == 0) return pipe.CapCenter;
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                int len = s.Length;
                if (distance > s.Start + len && i < segs.Count - 1) continue;
                float t = Mathf.Clamp(distance - s.Start, 0f, len);
                var dir = Sign(s.B - s.A);
                vertical = dir.x == 0;
                return new Vector2(s.A.x + dir.x * t, s.A.y + dir.y * t);
            }
            var last = segs[segs.Count - 1];
            vertical = last.A.x == last.B.x;
            return last.B;
        }
    }
}
