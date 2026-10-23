using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Генератор обстановки новой шахты (Docs/Rework/MINE_DRESSING.md) по правилам <see cref="MineDressingRules"/>. Детерминирован:
    /// ГСЧ PCG32 от номера шахты, DecoSeed и версии правил, все перечисления по возрастанию клеток. Порядок:
    /// <list type="number">
    /// <item>Разметка участков на итоговой карте (заводы стоят, ворота открыты): широкий (камера), главный проход (на кратчайших
    /// путях база → выход), боковой ход; исключения — устье над базой, выход, ворота, подходы к заводам.</item>
    /// <item>Постоянные препятствия у стен камер и боковых ходов, по одному на <see cref="DressingPermanentRules.FreeCellsPerProp"/>
    /// клеток, с равномерным распределением по секторам. Из нескольких мест выбирается дающее наименьший обход; каждое
    /// принятое проверяет <see cref="OutpostReachability"/> (оба случая), а выход трубы и длина маршрута каждого завода не
    /// меняются — производство прежнее.</item>
    /// <item>Детали стен (заколоченный ход) — в породе над свободным полом, навигацию не меняют.</item>
    /// <item>Мусор: композиции у постоянных препятствий, затем каждый участок (связная группа подходящих клеток сектора одного типа)
    /// получает долю <see cref="MineDressingRules.Density"/> своих клеток группами по 1–<see cref="MineDressingRules.ClusterMax"/>
    /// кучи; участок от <see cref="MineDressingRules.SectionMinCells"/> клеток — хотя бы одну кучу.</item>
    /// </list>
    /// Мусор не может отрезать цель: на итоговой карте он снят, а волна расчистки снимает каждую кучу, как только к ней есть
    /// путь. Итог всё равно проверяется валидатором; при нарушении мусор снимается целиком (потом и препятствия).
    /// </summary>
    public static class MineDressingPlacer
    {
        const ulong Stream = 0xD2E55E5UL;

        public static MineDressing Generate(OutpostMap map, MineDressingRules rules)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (map.Topology == null) throw new ArgumentException("Dressing needs a topology map: " + map.ZoneId);
            return new Generator(map, rules).Run();
        }

        /// <summary>Тип участка каждой клетки шахты (−1 — не пол), как его видит генератор. Для отчётов и тестов.</summary>
        public static int[] ClassifyAreas(OutpostMap map, MineDressingRules rules)
        {
            var g = new Generator(map, rules);
            return (int[])g.Areas.Clone();
        }

        /// <summary>Seed снимка: номер шахты, DecoSeed и версия правил.</summary>
        public static ulong SeedOf(OutpostMap map, MineDressingRules rules)
        {
            ulong h = 1469598103934665603UL;
            foreach (char c in rules.Version ?? "")
            {
                h ^= c;
                h *= 1099511628211UL;
            }
            unchecked
            {
                h ^= (ulong)map.Number * 0x9E3779B97F4A7C15UL;
                h ^= (ulong)(uint)map.DecoSeed << 17;
            }
            return h;
        }

        sealed class Generator
        {
            readonly OutpostMap m_Map;
            readonly MineDressingRules m_Rules;
            readonly Pcg32 m_Rng;
            readonly int W, D, N;
            /// <summary>Итоговая проходимость: пол без footprint заводов (без постоянных препятствий обстановки).</summary>
            readonly bool[] m_Pass;
            readonly int[] m_DistBase;
            readonly int[] m_Area;
            readonly bool[] m_Excluded;
            readonly bool[] m_Used;
            readonly int[] m_Queue;
            readonly List<DressingProp> m_Props = new List<DressingProp>();
            readonly List<DressingPiece> m_Pieces = new List<DressingPiece>();
            readonly List<DressingPiece> m_Ores = new List<DressingPiece>();
            readonly List<int[]> m_PropCells = new List<int[]>();
            /// <summary>Сечения проходов, закрытые кучами целиком (завалы): между ними не меньше Barricades.Spacing.</summary>
            readonly List<int[]> m_Blocks = new List<int[]>();
            readonly Dictionary<string, int> m_FamilyUse = new Dictionary<string, int>(StringComparer.Ordinal);

            public int[] Areas => m_Area;

            public Generator(OutpostMap map, MineDressingRules rules)
            {
                m_Map = map;
                m_Rules = rules;
                m_Rng = new Pcg32(SeedOf(map, rules), Stream);
                W = map.Width;
                D = map.Depth;
                N = map.CellCount;
                m_Queue = new int[N];
                m_Pass = new bool[N];
                for (int c = 0; c < N; c++) m_Pass[c] = map.IsPlayable(c);
                foreach (var t in map.Targets)
                    if (t.IsFactory)
                        foreach (int c in t.Cells) m_Pass[c] = false;
                m_DistBase = Bfs(map.IntakeCell, m_Pass, null);
                var distExit = Bfs(map.ExitCell, m_Pass, null);
                int route = map.ExitCell >= 0 && m_DistBase[map.ExitCell] >= 0 ? m_DistBase[map.ExitCell] : -1;
                m_Area = new int[N];
                m_Excluded = new bool[N];
                m_Used = new bool[N];
                for (int c = 0; c < N; c++)
                {
                    m_Area[c] = -1;
                    m_Excluded[c] = true;
                    if (map.Row(c) >= D || !map.IsPlayable(c)) continue;
                    if (OpenCells(c) >= rules.ChamberOpenCells) m_Area[c] = (int)DressingArea.Chamber;
                    else if (route >= 0 && m_DistBase[c] >= 0 && distExit[c] >= 0 && m_DistBase[c] + distExit[c] <= route + 2)
                        m_Area[c] = (int)DressingArea.MainPassage;
                    else m_Area[c] = (int)DressingArea.SidePassage;
                    m_Excluded[c] = map.InitialOccupant[c] >= 0 || map.CellGateOrder[c] >= 0 || m_DistBase[c] < 0 ||
                                    m_DistBase[c] <= rules.Exclusions.BaseClearance;
                }
                var ex = rules.Exclusions;
                if (map.ExitCell >= 0) ExcludeAround(map.ExitCell, ex.ExitClearance);
                foreach (var t in map.Targets)
                {
                    if (t.IsGate)
                    {
                        foreach (int c in t.Cells) ExcludeAround(c, ex.GateClearance);
                        foreach (int c in t.WorkCells) ExcludeAround(c, ex.GateClearance);
                    }
                    else if (t.IsFactory && ex.FactoryClearance)
                        foreach (int c in t.WorkCells) m_Excluded[c] = true;
                }
                if (ex.FactoryClearance)
                    foreach (var f in map.Factories)
                        if (f.OutletCell >= 0) ExcludeAround(f.OutletCell, 0);
            }

            public MineDressing Run()
            {
                PlaceProps();
                PlaceWallProps();
                PlaceArrangements();
                PlaceBarricades();
                PlaceSections();
                PlaceOreFill();
                var result = Snapshot(true, true);
                if (Validate(result)) return result;
                // Страховка: мусор и руда не должны ломать проходимость (см. описание класса); иначе шахта остаётся сначала без
                // руды-заполнителя, затем и без мусора.
                result = Snapshot(true, false);
                if (Validate(result)) return result;
                result = Snapshot(false, false);
                if (Validate(result)) return result;
                return new MineDressing { RulesVersion = m_Rules.Version, ZoneId = m_Map.ZoneId, Seed = SeedOf(m_Map, m_Rules) };
            }

            // ───────────── Постоянные препятствия ─────────────

            void PlaceProps()
            {
                int eligible = 0;
                for (int c = 0; c < N; c++)
                    if (!m_Excluded[c]) eligible++;
                var p = m_Rules.Permanent;
                int want = Math.Max(p.Min, Math.Min(p.Max, eligible / p.FreeCellsPerProp));
                var families = m_Rules.Families.FindAll(f => f.Category == DressingCategory.Permanent);
                if (families.Count == 0) return;
                var sectorProps = new int[m_Map.Gates.Length + 1];
                for (int k = 0; k < want; k++)
                {
                    bool placed = false;
                    var order = FamilyOrder(families);
                    foreach (var family in order)
                    {
                        if (TryPlaceProp(family, sectorProps))
                        {
                            placed = true;
                            break;
                        }
                    }
                    if (!placed) break;
                }
            }

            bool TryPlaceProp(DressingFamily family, int[] sectorProps)
            {
                var p = m_Rules.Permanent;
                foreach (int v in VariantOrder(family))
                {
                    var variant = family.Variants[v];
                    var candidates = new List<(int Sector, uint Key, int Col, int Row, int[] Cells)>();
                    for (int row = 0; row < D; row++)
                        for (int col = 0; col < W; col++)
                        {
                            var cells = FitFloor(col, row, variant.W, variant.H, family, out int sector);
                            if (cells == null || !TouchesWall(cells) || !Spaced(cells, p.Spacing)) continue;
                            candidates.Add((sector, m_Rng.NextUInt(), col, row, cells));
                        }
                    if (candidates.Count == 0) continue;
                    candidates.Sort((a, b) =>
                    {
                        int sa = sectorProps[a.Sector], sb = sectorProps[b.Sector];
                        return sa != sb ? sa.CompareTo(sb) : a.Key.CompareTo(b.Key);
                    });
                    int take = Math.Min(p.Candidates, candidates.Count);
                    var scored = new List<(long Score, uint Key, int Index)>();
                    for (int i = 0; i < take; i++)
                    {
                        long score = DetourScore(candidates[i].Cells, p.MaxDetour);
                        if (score >= 0) scored.Add((score, candidates[i].Key, i));
                    }
                    scored.Sort((a, b) => a.Score != b.Score ? a.Score.CompareTo(b.Score) : a.Key.CompareTo(b.Key));
                    foreach (var s in scored)
                    {
                        var c = candidates[s.Index];
                        var prop = new DressingProp
                        {
                            Family = family.Id, Variant = v, Column = c.Col, Row = c.Row, W = variant.W, H = variant.H,
                            PipeMount = family.PipeMount,
                        };
                        m_Props.Add(prop);
                        if (PropsValid())
                        {
                            m_PropCells.Add(c.Cells);
                            foreach (int cell in c.Cells)
                            {
                                m_Used[cell] = true;
                                m_Pass[cell] = false;
                            }
                            sectorProps[c.Sector]++;
                            Count(family.Id);
                            return true;
                        }
                        m_Props.RemoveAt(m_Props.Count - 1);
                    }
                }
                return false;
            }

            /// <summary>Сумма удлинений кратчайших путей от базы при перекрытии клеток; −1 — что-то отрезано или обход длиннее предела.</summary>
            long DetourScore(int[] cells, int maxDetour)
            {
                foreach (int c in cells) m_Pass[c] = false;
                var dist = Bfs(m_Map.IntakeCell, m_Pass, null);
                foreach (int c in cells) m_Pass[c] = true;
                var before = Bfs(m_Map.IntakeCell, m_Pass, null);
                long sum = 0;
                for (int c = 0; c < N; c++)
                {
                    if (!m_Pass[c] || Array.IndexOf(cells, c) >= 0 || before[c] < 0) continue;
                    if (dist[c] < 0) return -1;
                    int extra = dist[c] - before[c];
                    if (extra > maxDetour) return -1;
                    sum += extra;
                }
                return sum;
            }

            /// <summary>Карта с текущими препятствиями проходит оба случая валидатора, выходы и маршруты труб заводов прежние.</summary>
            bool PropsValid()
            {
                var snapshot = new MineDressing { RulesVersion = m_Rules.Version, ZoneId = m_Map.ZoneId, Props = new List<DressingProp>(m_Props) };
                OutpostMap trial;
                try
                {
                    trial = MineDressingAdapter.Apply(m_Map, snapshot);
                }
                catch (ArgumentException)
                {
                    return false;
                }
                if (!OutpostReachability.Check(trial).Passed) return false;
                return FactoriesUnchanged(trial);
            }

            bool FactoriesUnchanged(OutpostMap trial)
            {
                foreach (var f in m_Map.Factories)
                {
                    var g = trial.Factory(f.TargetId);
                    if (g == null || g.OutletCell != f.OutletCell || g.RouteSteps != f.RouteSteps) return false;
                }
                return true;
            }

            // ───────────── Детали стен ─────────────

            void PlaceWallProps()
            {
                var families = m_Rules.Families.FindAll(f => f.Category == DressingCategory.Wall);
                if (families.Count == 0) return;
                var rim = new List<(uint Key, int Cell, int Sector)>();
                for (int row = 0; row + 1 < D; row++)
                    for (int col = 0; col < W; col++)
                    {
                        int cell = m_Map.Cell(col, row), below = cell + W;
                        if (m_Map.IsPlayable(cell) || !m_Map.IsPlayable(below) || m_Excluded[below] || m_Used[below]) continue;
                        if (m_Map.InitialOccupant[below] >= 0) continue;
                        rim.Add((m_Rng.NextUInt(), cell, m_Map.SectorAt(below)));
                    }
                var p = m_Rules.Permanent;
                int want = Math.Max(p.WallMin, Math.Min(p.WallMax, rim.Count / p.WallCellsPerProp));
                var sectorUse = new int[m_Map.Gates.Length + 1];
                var placed = new List<int>();
                for (int k = 0; k < want && rim.Count > 0; k++)
                {
                    rim.Sort((a, b) =>
                    {
                        int sa = sectorUse[a.Sector], sb = sectorUse[b.Sector];
                        return sa != sb ? sa.CompareTo(sb) : a.Key.CompareTo(b.Key);
                    });
                    int pick = -1;
                    for (int i = 0; i < rim.Count && pick < 0; i++)
                    {
                        bool ok = true;
                        foreach (int other in placed)
                            if (Chebyshev(other, rim[i].Cell) < p.Spacing) { ok = false; break; }
                        if (ok)
                            foreach (var cells in m_PropCells)
                                if (Chebyshev(cells, rim[i].Cell) < 2) { ok = false; break; }
                        if (ok) pick = i;
                    }
                    if (pick < 0) break;
                    var family = FamilyOrder(families)[0];
                    int v = VariantOrder(family)[0];
                    int c = rim[pick].Cell;
                    m_Props.Add(new DressingProp { Family = family.Id, Variant = v, Column = m_Map.Col(c), Row = m_Map.Row(c), Wall = true, PipeMount = family.PipeMount });
                    placed.Add(c);
                    sectorUse[rim[pick].Sector]++;
                    Count(family.Id);
                    rim.RemoveAt(pick);
                }
            }

            // ───────────── Мусор ─────────────

            void PlaceArrangements()
            {
                if (m_Rules.Arrangements == null) return;
                for (int i = 0; i < m_Props.Count; i++)
                {
                    var prop = m_Props[i];
                    if (prop.Wall) continue;
                    DressingArrangement arrangement = null;
                    foreach (var a in m_Rules.Arrangements)
                        if (a.Anchor == prop.Family) { arrangement = a; break; }
                    if (arrangement == null) continue;
                    var cells = MineDressingAdapter.Footprint(m_Map, prop.Column, prop.Row, prop.W, prop.H, prop.Family);
                    int sector = SectorOfProp(cells);
                    int count = arrangement.Min + m_Rng.NextInt(arrangement.Max - arrangement.Min + 1);
                    var ring = new List<(int Dist, uint Key, int Cell)>();
                    for (int c = 0; c < m_Map.Width * D; c++)
                    {
                        if (m_Excluded[c] || m_Used[c] || m_Map.SectorAt(c) != sector) continue;
                        int d = Chebyshev(cells, c);
                        if (d > 2) continue;
                        ring.Add((Manhattan(cells, c), m_Rng.NextUInt(), c));
                    }
                    ring.Sort((a, b) => a.Dist != b.Dist ? a.Dist.CompareTo(b.Dist) : a.Key.CompareTo(b.Key));
                    int placed = 0;
                    foreach (var r in ring)
                    {
                        if (placed >= count) break;
                        if (m_Used[r.Cell]) continue;
                        var family = m_Rules.Family(arrangement.Debris[m_Rng.NextInt(arrangement.Debris.Count)]);
                        if (family == null || !family.Allows((DressingArea)m_Area[r.Cell])) continue;
                        if (PlacePiece(family, r.Cell) > 0) placed++;
                    }
                }
            }

            // ───────────── Завалы поперёк проходов ─────────────

            /// <summary>Сечение прохода между стенами: клетки по возрастанию; горизонтальное — поперёк вертикального прохода.</summary>
            sealed class Cut
            {
                public int[] Cells;
                public bool Horizontal;
                public int Section;
                public DressingArea Area;
                public bool Entrance;
                public uint Key;
            }

            /// <summary>
            /// Завалы (<see cref="MineDressingRules.Barricades"/>): участок прохода получает по завалу на CellsPer своих клеток.
            /// Сечение — пролёт пола от стены до стены шириной до MaxWidth, по обе стороны которого проход продолжается; оно
            /// занимается кучами целиком (2×1, где есть вариант, иначе 1×1). Сначала входы в ветки (рядом участок другого типа),
            /// затем самые далёкие от уже поставленных завалов сечения — равномерно по длине прохода.
            /// </summary>
            void PlaceBarricades()
            {
                var b = m_Rules.Barricades;
                if (b == null) return;
                var section = Sections(out var sectionCells);
                var quota = new int[sectionCells.Count];
                for (int s = 0; s < quota.Length; s++)
                {
                    var area = (DressingArea)m_Area[sectionCells[s][0]];
                    int per = b.CellsPerOf(area);
                    quota[s] = per <= 0 ? 0 : (sectionCells[s].Count + per / 2) / per;
                }
                var cuts = new List<Cut>();
                for (int row = 0; row < D; row++)
                    for (int col = 0; col < W; col++)
                    {
                        if (!Floor(col - 1, row)) TryCut(col, row, 1, 0, section, cuts);
                        if (!Floor(col, row - 1)) TryCut(col, row, 0, 1, section, cuts);
                    }
                if (cuts.Count == 0) return;
                // Полный порядок (ключ, клетка, направление): одинаковый результат при любой сортировке.
                cuts.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key)
                    : x.Cells[0] != y.Cells[0] ? x.Cells[0].CompareTo(y.Cells[0]) : x.Horizontal.CompareTo(y.Horizontal));
                foreach (var cut in cuts)
                    if (cut.Entrance && quota[cut.Section] > 0 && Open(cut) && BlockDistance(cut.Cells) >= b.Spacing && PlaceCut(cut))
                        quota[cut.Section]--;
                while (true)
                {
                    Cut best = null;
                    int bestDist = -1;
                    foreach (var cut in cuts)
                    {
                        if (quota[cut.Section] <= 0 || !Open(cut)) continue;
                        int d = BlockDistance(cut.Cells);
                        if (d < b.Spacing || d <= bestDist) continue;
                        best = cut;
                        bestDist = d;
                    }
                    if (best == null) break;
                    PlaceCut(best);
                    quota[best.Section]--;
                }
            }

            void TryCut(int col, int row, int dx, int dy, int[] section, List<Cut> cuts)
            {
                var b = m_Rules.Barricades;
                var cells = CutThrough(col, row, dx, dy);
                if (cells == null) return;
                int s = section[cells[0]];
                if (s < 0) return;
                foreach (int c in cells)
                    if (section[c] != s || m_Used[c]) return;
                var area = (DressingArea)m_Area[cells[0]];
                if (b.CellsPerOf(area) <= 0) return;
                bool entrance = false;
                foreach (int c in cells)
                {
                    int cx = m_Map.Col(c), cy = m_Map.Row(c);
                    for (int ny = cy - 2; ny <= cy + 2 && !entrance; ny++)
                        for (int nx = cx - 2; nx <= cx + 2 && !entrance; nx++)
                            if (Floor(nx, ny) && ny < D && m_Area[m_Map.Cell(nx, ny)] >= 0 && m_Area[m_Map.Cell(nx, ny)] != (int)area)
                                entrance = true;
                }
                cuts.Add(new Cut { Cells = cells, Horizontal = dx == 1, Section = s, Area = area, Entrance = entrance, Key = m_Rng.NextUInt() });
            }

            /// <summary>
            /// Сечение прохода через клетку (col, row) вдоль (dx, dy): пролёт пола от стены до стены не шире Barricades.MaxWidth,
            /// по обе стороны которого (поперёк пролёта) проход продолжается. Клетки по возрастанию или null.
            /// </summary>
            int[] CutThrough(int col, int row, int dx, int dy)
            {
                int max = m_Rules.Barricades.MaxWidth;
                if (!Floor(col, row)) return null;
                int x = col, y = row;
                for (int back = 0; Floor(x - dx, y - dy); back++)
                {
                    // Шире предела — это не сечение прохода, а камера или проход вдоль.
                    if (back + 1 >= max) return null;
                    x -= dx;
                    y -= dy;
                }
                var cells = new List<int>(max);
                for (; Floor(x, y); x += dx, y += dy)
                {
                    if (cells.Count == max) return null;
                    cells.Add(m_Map.Cell(x, y));
                }
                bool before = false, after = false;
                foreach (int c in cells)
                {
                    int cx = m_Map.Col(c), cy = m_Map.Row(c);
                    before |= Floor(cx - dy, cy - dx);
                    after |= Floor(cx + dy, cy + dx);
                }
                return before && after ? cells.ToArray() : null;
            }

            /// <summary>Сечения проходов через клетки footprint, которые куча вместе с уже стоящими кучами закроет целиком.</summary>
            List<int[]> ClosedCuts(int[] footprint)
            {
                var result = new List<int[]>();
                foreach (int c in footprint)
                    for (int k = 0; k < 2; k++)
                    {
                        var run = CutThrough(m_Map.Col(c), m_Map.Row(c), 1 - k, k);
                        if (run == null) continue;
                        bool closed = true;
                        foreach (int r in run)
                            if (!m_Used[r] && Array.IndexOf(footprint, r) < 0) { closed = false; break; }
                        if (!closed) continue;
                        bool known = false;
                        foreach (var other in result)
                            if (other.Length == run.Length && other[0] == run[0] && other[other.Length - 1] == run[run.Length - 1]) known = true;
                        if (!known) result.Add(run);
                    }
                return result;
            }

            /// <summary>Наименьшее расстояние (Чебышёв) от клеток до закрытых сечений (<see cref="m_Blocks"/>); нет сечений — int.MaxValue.</summary>
            int BlockDistance(IEnumerable<int> cells)
            {
                int best = int.MaxValue;
                foreach (var block in m_Blocks)
                    foreach (int c in cells)
                        best = Math.Min(best, Chebyshev(block, c));
                return best;
            }

            bool Open(Cut cut)
            {
                foreach (int c in cut.Cells)
                    if (m_Used[c]) return false;
                return true;
            }

            /// <summary>Кучи одного семейства на всё сечение: 2×1 там, где есть вариант и место, иначе 1×1.</summary>
            bool PlaceCut(Cut cut)
            {
                var b = m_Rules.Barricades;
                var ids = cut.Area == DressingArea.MainPassage ? b.MainFamilies : b.Families;
                var pool = new List<DressingFamily>();
                foreach (string id in ids)
                {
                    var f = m_Rules.Family(id);
                    if (f != null && f.IsDestructible && f.Allows(cut.Area) && !pool.Contains(f)) pool.Add(f);
                }
                if (pool.Count == 0) return false;
                var family = WeightedOrder(pool, Weight)[0];
                int placed = 0;
                for (int i = 0; i < cut.Cells.Length;)
                {
                    int c = cut.Cells[i];
                    int wide = cut.Horizontal && cut.Cells.Length - i >= 2 ? PickVariant(family, 2, 1) : -1;
                    int v = wide >= 0 ? wide : PickVariant(family, 1, 1);
                    if (v < 0 || PlacePieceAt(family, v, m_Map.Col(c), m_Map.Row(c)) == 0) break;
                    placed++;
                    i += wide >= 0 ? 2 : 1;
                }
                return placed > 0;
            }

            /// <summary>Вариант семейства с footprint w × h (взвешенно) или −1.</summary>
            int PickVariant(DressingFamily family, int w, int h)
            {
                foreach (int v in VariantOrder(family))
                    if (family.Variants[v].W == w && family.Variants[v].H == h) return v;
                return -1;
            }

            /// <summary>Куча завала: вариант <paramref name="v"/> с якорем (col, row); число занятых клеток или 0.</summary>
            int PlacePieceAt(DressingFamily family, int v, int col, int row)
            {
                var variant = family.Variants[v];
                var cells = FitPiece(col, row, variant.W, variant.H, m_Map.SectorAt(m_Map.Cell(col, row)));
                return cells == null ? 0 : Commit(family, v, col, row, cells, true);
            }

            /// <summary>
            /// Поставить кучу варианта <paramref name="v"/> с якорем (col, row) на клетки <paramref name="cells"/>. Куча, которая
            /// закрывает сечение прохода целиком, — завал: вне шага завалов (<paramref name="barricade"/> = false) она допустима
            /// только не ближе Barricades.Spacing к другим закрытым сечениям. Число занятых клеток или 0.
            /// </summary>
            int Commit(DressingFamily family, int v, int col, int row, int[] cells, bool barricade)
            {
                var closed = ClosedCuts(cells);
                if (closed.Count > 0 && !barricade)
                    foreach (var run in closed)
                        if (BlockDistance(run) < m_Rules.Barricades.Spacing) return 0;
                var variant = family.Variants[v];
                var tier = m_Rules.Tier(family.Tier);
                foreach (int c in cells) m_Used[c] = true;
                m_Blocks.AddRange(closed);
                m_Pieces.Add(new DressingPiece
                {
                    Family = family.Id, Variant = v, Tier = family.Tier, HpFactor = tier.HpFactor, ValueFactor = tier.ValueFactor,
                    Column = col, Row = row, W = variant.W, H = variant.H,
                });
                Count(family.Id);
                return cells.Length;
            }

            /// <summary>Пол итоговой карты (заводы и постоянные препятствия — стены) внутри шахты, без лагеря.</summary>
            bool Floor(int col, int row) => col >= 0 && col < W && row >= 0 && row < D && m_Pass[m_Map.Cell(col, row)];

            /// <summary>Участки: связные группы подходящих клеток одного сектора и одного типа; клетка → номер участка или −1.</summary>
            int[] Sections(out List<List<int>> cellsOf)
            {
                var id = new int[N];
                for (int i = 0; i < N; i++) id[i] = -1;
                cellsOf = new List<List<int>>();
                for (int start = 0; start < W * D; start++)
                {
                    if (id[start] >= 0 || m_Excluded[start] || m_Used[start]) continue;
                    int s = cellsOf.Count, sector = m_Map.SectorAt(start), area = m_Area[start];
                    var cells = new List<int>();
                    cellsOf.Add(cells);
                    int head = 0, tail = 0;
                    m_Queue[tail++] = start;
                    id[start] = s;
                    while (head < tail)
                    {
                        int c = m_Queue[head++];
                        cells.Add(c);
                        foreach (int n in Neighbours4(c))
                        {
                            if (id[n] >= 0 || m_Excluded[n] || m_Used[n] || m_Map.SectorAt(n) != sector || m_Area[n] != area) continue;
                            id[n] = s;
                            m_Queue[tail++] = n;
                        }
                    }
                }
                return id;
            }

            void PlaceSections()
            {
                var seen = new bool[N];
                var cells = new List<int>();
                for (int start = 0; start < W * D; start++)
                {
                    if (seen[start] || m_Excluded[start] || m_Used[start]) continue;
                    // Участок: связная группа подходящих клеток одного сектора и одного типа.
                    cells.Clear();
                    int sector = m_Map.SectorAt(start), area = m_Area[start];
                    int head = 0, tail = 0;
                    m_Queue[tail++] = start;
                    seen[start] = true;
                    while (head < tail)
                    {
                        int c = m_Queue[head++];
                        cells.Add(c);
                        int col = m_Map.Col(c), row = m_Map.Row(c);
                        Visit(col, row - 1);
                        Visit(col - 1, row);
                        Visit(col + 1, row);
                        Visit(col, row + 1);

                        void Visit(int x, int y)
                        {
                            if (x < 0 || x >= W || y < 0 || y >= D) return;
                            int n = m_Map.Cell(x, y);
                            if (seen[n] || m_Excluded[n] || m_Used[n] || m_Map.SectorAt(n) != sector || m_Area[n] != area) return;
                            seen[n] = true;
                            m_Queue[tail++] = n;
                        }
                    }
                    FillSection(cells, (DressingArea)area);
                }
            }

            void FillSection(List<int> cells, DressingArea area)
            {
                var density = m_Rules.DensityOf(area);
                if (density.Sign <= 0) return;
                int m = cells.Count;
                int want = (int)(density * m).RoundHalfUp();
                if (want == 0 && m >= m_Rules.SectionMinCells) want = 1;
                int filled = 0, attempts = 0;
                while (filled < want && attempts < 4 * m)
                {
                    attempts++;
                    int seed = cells[m_Rng.NextInt(m)];
                    if (m_Used[seed]) continue;
                    // Группы не слипаются в сплошную стену: соседство с чужой кучей — только если иначе места нет.
                    if (attempts <= 2 * m && NextToPiece(seed)) continue;
                    var family = PickDestructible(area);
                    if (family == null) return;
                    int got = PlacePiece(family, seed);
                    if (got == 0) continue;
                    filled += got;
                    var cluster = new List<int>(MineDressingAdapter.Footprint(m_Map, m_Pieces[m_Pieces.Count - 1].Column,
                        m_Pieces[m_Pieces.Count - 1].Row, m_Pieces[m_Pieces.Count - 1].W, m_Pieces[m_Pieces.Count - 1].H, family.Id));
                    for (int k = 1; k < m_Rules.ClusterMax && filled < want; k++)
                    {
                        if (m_Rng.NextInt(100) >= m_Rules.ClusterGrowPercent) break;
                        var next = new List<int>();
                        foreach (int c in cluster)
                            foreach (int n in Neighbours4(c))
                                if (!m_Excluded[n] && !m_Used[n] && m_Map.SectorAt(n) == m_Map.SectorAt(seed) && !next.Contains(n)) next.Add(n);
                        if (next.Count == 0) break;
                        next.Sort();
                        int at = next[m_Rng.NextInt(next.Count)];
                        var f2 = m_Rng.NextInt(2) == 0 && family.Allows((DressingArea)m_Area[at]) ? family : PickDestructible((DressingArea)m_Area[at]);
                        if (f2 == null) break;
                        int more = PlacePiece(f2, at);
                        if (more == 0) break;
                        filled += more;
                        var last = m_Pieces[m_Pieces.Count - 1];
                        cluster.AddRange(MineDressingAdapter.Footprint(m_Map, last.Column, last.Row, last.W, last.H, f2.Id));
                    }
                }
            }

            /// <summary>Куча семейства с клеткой <paramref name="cell"/> в footprint (якорь подбирается); число занятых клеток или 0.</summary>
            int PlacePiece(DressingFamily family, int cell)
            {
                foreach (int v in VariantOrder(family))
                {
                    var variant = family.Variants[v];
                    int col = m_Map.Col(cell), row = m_Map.Row(cell);
                    // Якорь — нижняя левая клетка: клетка может быть любой клеткой footprint.
                    for (int dy = 0; dy < variant.H; dy++)
                        for (int dx = 0; dx < variant.W; dx++)
                        {
                            int ac = col - dx, ar = row + dy;
                            var cells = FitPiece(ac, ar, variant.W, variant.H, m_Map.SectorAt(cell));
                            if (cells == null) continue;
                            int got = Commit(family, v, ac, ar, cells, false);
                            if (got > 0) return got;
                        }
                }
                return 0;
            }

            int[] FitPiece(int col, int row, int w, int h, int sector)
            {
                if (col < 0 || col + w > W || row - h + 1 < 0 || row >= D) return null;
                var cells = new int[w * h];
                int k = 0;
                for (int dy = 0; dy < h; dy++)
                    for (int dx = 0; dx < w; dx++)
                    {
                        int c = m_Map.Cell(col + dx, row - dy);
                        if (m_Excluded[c] || m_Used[c] || m_Map.SectorAt(c) != sector) return null;
                        cells[k++] = c;
                    }
                return cells;
            }

            bool NextToPiece(int cell)
            {
                foreach (int n in Neighbours4(cell))
                    if (m_Used[n] && !IsPropCell(n)) return true;
                return false;
            }

            bool IsPropCell(int cell)
            {
                foreach (var cells in m_PropCells)
                    if (Array.IndexOf(cells, cell) >= 0) return true;
                return false;
            }

            DressingFamily PickDestructible(DressingArea area)
            {
                var pool = new List<DressingFamily>();
                foreach (var f in m_Rules.Families)
                    if (f.IsDestructible && f.Allows(area)) pool.Add(f);
                return pool.Count == 0 ? null : WeightedOrder(pool, Weight)[0];
            }

            /// <summary>
            /// Вес семейства с поправкой на уже поставленные: разнообразие без повторов одного вида подряд. Постоянные препятствия и
            /// детали стен штрафуются сильнее — их в шахте единицы.
            /// </summary>
            int Weight(DressingFamily f)
            {
                m_FamilyUse.TryGetValue(f.Id, out int used);
                return f.IsDestructible ? Math.Max(1, f.Weight * 64 / (4 + used)) : Math.Max(1, f.Weight * 64 / (1 + 6 * used));
            }

            /// <summary>Порядок попыток: сначала ещё не поставленные семейства (взвешенно), затем уже поставленные.</summary>
            List<DressingFamily> FamilyOrder(List<DressingFamily> families)
            {
                var fresh = families.FindAll(f => !m_FamilyUse.ContainsKey(f.Id));
                var used = families.FindAll(f => m_FamilyUse.ContainsKey(f.Id));
                var result = WeightedOrder(fresh, Weight);
                result.AddRange(WeightedOrder(used, Weight));
                return result;
            }

            List<int> VariantOrder(DressingFamily family)
            {
                var indices = new List<int>();
                for (int v = 0; v < family.Variants.Count; v++) indices.Add(v);
                return WeightedOrder(indices, v => family.Variants[v].Weight);
            }

            /// <summary>Взвешенная случайная перестановка целыми числами (без плавающей точки — одинаково на всех платформах).</summary>
            List<T> WeightedOrder<T>(List<T> items, Func<T, int> weight)
            {
                var rest = new List<T>(items);
                var result = new List<T>(items.Count);
                while (rest.Count > 0)
                {
                    int total = 0;
                    foreach (var item in rest) total += Math.Max(1, weight(item));
                    int roll = m_Rng.NextInt(total), pick = rest.Count - 1;
                    for (int i = 0; i < rest.Count; i++)
                    {
                        roll -= Math.Max(1, weight(rest[i]));
                        if (roll < 0) { pick = i; break; }
                    }
                    result.Add(rest[pick]);
                    rest.RemoveAt(pick);
                }
                return result;
            }

            void Count(string family)
            {
                m_FamilyUse.TryGetValue(family, out int n);
                m_FamilyUse[family] = n + 1;
            }

            // ───────────── Руда-заполнитель ─────────────

            /// <summary>Семейство руды-заполнителя в снимке (не семейство каталога: вид и HP — камня-шаблона шахты).</summary>
            const string OreFamily = "ore";

            /// <summary>
            /// Руда во всех клетках пола, оставшихся свободными (<see cref="MineDressingRules.OreFill"/>): камень-шаблон — первый
            /// тип из RockTypes среди камней источника шахты, на самом низком уровне. Свободны лагерь, клетки ворот, выход, выходы
            /// труб и вход в сектор за воротами (клетки следующего сектора вплотную к воротам: клетка ворот — не рабочая клетка, без
            /// этого входа руду за открытыми воротами не с чего начать); клетка без соседнего пола (будущей рабочей клетки) и
            /// клетка, недостижимая на итоговой карте, остаются полом. Устье над лагерем тоже заполняется: котики прокапывают шахту
            /// от лагеря.
            /// </summary>
            void PlaceOreFill()
            {
                var fill = m_Rules.OreFill;
                if (fill == null || !fill.Enabled) return;
                MapTarget template = null;
                foreach (string type in fill.RockTypes)
                {
                    foreach (var t in m_Map.Targets)
                        if (t.Kind == 'r' && t.Piece == null && t.TypeId == type &&
                            (template == null || t.SourceLevel < template.SourceLevel || (t.SourceLevel == template.SourceLevel && t.Id < template.Id)))
                            template = t;
                    if (template != null) break;
                }
                if (template == null) return;
                var keep = new bool[N];
                if (m_Map.ExitCell >= 0) keep[m_Map.ExitCell] = true;
                foreach (var f in m_Map.Factories)
                    if (f.OutletCell >= 0) keep[f.OutletCell] = true;
                for (int c = 0; c < W * D; c++)
                {
                    int order = m_Map.CellGateOrder[c];
                    if (order < 0) continue;
                    foreach (int n in Neighbours4(c))
                        if (m_Map.CellGateOrder[n] < 0 && m_Map.SectorAt(n) > order) keep[n] = true;
                }
                var reach = Bfs(m_Map.IntakeCell, m_Pass, null);
                for (int c = 0; c < W * D; c++)
                {
                    if (!m_Pass[c] || keep[c] || m_Used[c] || reach[c] < 0) continue;
                    if (m_Map.InitialOccupant[c] >= 0 || m_Map.CellGateOrder[c] >= 0 || !HasWorkNeighbour(c)) continue;
                    m_Used[c] = true;
                    m_Ores.Add(new DressingPiece
                    {
                        Family = OreFamily, Column = m_Map.Col(c), Row = m_Map.Row(c), HpFactor = fill.HpFactor,
                        RockType = template.TypeId, RockVisual = template.VisualId, RockLevel = template.SourceLevel,
                    });
                }
            }

            /// <summary>У клетки есть соседний пол итоговой карты вне ворот — будущая рабочая клетка (лагерь тоже считается).</summary>
            bool HasWorkNeighbour(int cell)
            {
                int col = m_Map.Col(cell), row = m_Map.Row(cell);
                return Work(col, row - 1) || Work(col - 1, row) || Work(col + 1, row) || Work(col, row + 1);

                bool Work(int x, int y)
                {
                    if (x < 0 || x >= W || y < 0 || y >= m_Map.Rows) return false;
                    int n = m_Map.Cell(x, y);
                    return m_Pass[n] && m_Map.CellGateOrder[n] < 0;
                }
            }

            // ───────────── Снимок и проверка ─────────────

            MineDressing Snapshot(bool withPieces, bool withOre)
            {
                var snapshot = new MineDressing { RulesVersion = m_Rules.Version, ZoneId = m_Map.ZoneId, Seed = SeedOf(m_Map, m_Rules) };
                snapshot.Props.AddRange(m_Props);
                if (!withPieces) return snapshot;
                var pieces = new List<DressingPiece>(m_Pieces);
                if (withOre) pieces.AddRange(m_Ores);
                // ID по клетке якоря: снимок не зависит от порядка генерации внутри шахты.
                pieces.Sort((a, b) => m_Map.Cell(a.Column, a.Row).CompareTo(m_Map.Cell(b.Column, b.Row)));
                int id = -1;
                foreach (var t in m_Map.Targets) id = Math.Max(id, t.Id);
                foreach (var piece in pieces)
                {
                    piece.TargetId = ++id;
                    piece.Branch = NearestBranch(piece);
                }
                snapshot.Pieces.AddRange(pieces);
                return snapshot;
            }

            bool Validate(MineDressing snapshot)
            {
                try
                {
                    var map = MineDressingAdapter.Apply(m_Map, snapshot);
                    return OutpostReachability.Check(map).Passed && FactoriesUnchanged(map);
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            int NearestBranch(DressingPiece piece)
            {
                int cell = m_Map.Cell(piece.Column, piece.Row);
                int sector = m_Map.SectorAt(cell);
                int best = 0, bestDist = int.MaxValue, bestId = int.MaxValue;
                foreach (var t in m_Map.Targets)
                {
                    if (t.Branch <= 0 || t.IsGate || t.Sector != sector) continue;
                    int d = Manhattan(t.Cells, cell);
                    if (d < bestDist || (d == bestDist && t.Id < bestId))
                    {
                        best = t.Branch;
                        bestDist = d;
                        bestId = t.Id;
                    }
                }
                return best;
            }

            // ───────────── Геометрия ─────────────

            int[] FitFloor(int col, int row, int w, int h, DressingFamily family, out int sector)
            {
                sector = -1;
                if (col < 0 || col + w > W || row - h + 1 < 0 || row >= D) return null;
                var cells = new int[w * h];
                int k = 0;
                for (int dy = 0; dy < h; dy++)
                    for (int dx = 0; dx < w; dx++)
                    {
                        int c = m_Map.Cell(col + dx, row - dy);
                        if (m_Excluded[c] || m_Used[c] || !family.Allows((DressingArea)m_Area[c])) return null;
                        int s = m_Map.SectorAt(c);
                        if (sector >= 0 && s != sector) return null;
                        sector = s;
                        cells[k++] = c;
                    }
                return cells;
            }

            bool TouchesWall(int[] cells)
            {
                foreach (int c in cells)
                {
                    int col = m_Map.Col(c), row = m_Map.Row(c);
                    if (Rock(col - 1, row) || Rock(col + 1, row) || Rock(col, row - 1) || Rock(col, row + 1)) return true;
                }
                return false;

                bool Rock(int x, int y) => x < 0 || x >= W || y < 0 || y >= m_Map.Rows || !m_Map.IsPlayable(m_Map.Cell(x, y));
            }

            bool Spaced(int[] cells, int spacing)
            {
                foreach (var other in m_PropCells)
                    foreach (int c in cells)
                        if (Chebyshev(other, c) < spacing) return false;
                return true;
            }

            int SectorOfProp(int[] cells) => cells.Length > 0 ? m_Map.SectorAt(cells[0]) : 0;

            int OpenCells(int cell)
            {
                int col = m_Map.Col(cell), row = m_Map.Row(cell), n = 0;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int x = col + dx, y = row + dy;
                        if (x < 0 || x >= W || y < 0 || y >= D) continue;
                        if (m_Map.IsPlayable(m_Map.Cell(x, y))) n++;
                    }
                return n;
            }

            void ExcludeAround(int cell, int radius)
            {
                int col = m_Map.Col(cell), row = m_Map.Row(cell);
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int x = col + dx, y = row + dy;
                        if (x < 0 || x >= W || y < 0 || y >= D) continue;
                        m_Excluded[m_Map.Cell(x, y)] = true;
                    }
            }

            IEnumerable<int> Neighbours4(int cell)
            {
                int col = m_Map.Col(cell), row = m_Map.Row(cell);
                if (row > 0) yield return cell - W;
                if (col > 0) yield return cell - 1;
                if (col + 1 < W) yield return cell + 1;
                if (row + 1 < D) yield return cell + W;
            }

            int Chebyshev(int a, int b) => Math.Max(Math.Abs(m_Map.Col(a) - m_Map.Col(b)), Math.Abs(m_Map.Row(a) - m_Map.Row(b)));

            int Chebyshev(int[] cells, int b)
            {
                int best = int.MaxValue;
                foreach (int a in cells) best = Math.Min(best, Chebyshev(a, b));
                return best;
            }

            int Manhattan(int[] cells, int b)
            {
                int best = int.MaxValue;
                foreach (int a in cells) best = Math.Min(best, Math.Abs(m_Map.Col(a) - m_Map.Col(b)) + Math.Abs(m_Map.Row(a) - m_Map.Row(b)));
                return best;
            }

            /// <summary>BFS по 4 направлениям от клетки по маске <paramref name="pass"/> (ворота открыты): расстояние или −1.</summary>
            int[] Bfs(int start, bool[] pass, int[] into)
            {
                var dist = into ?? new int[N];
                for (int i = 0; i < N; i++) dist[i] = -1;
                if (start < 0 || start >= N || !pass[start]) return dist;
                int head = 0, tail = 0;
                dist[start] = 0;
                m_Queue[tail++] = start;
                while (head < tail)
                {
                    int c = m_Queue[head++];
                    int col = c % W, row = c / W;
                    if (row > 0) Step(c - W);
                    if (col > 0) Step(c - 1);
                    if (col + 1 < W) Step(c + 1);
                    if (row + 1 < m_Map.Rows) Step(c + W);

                    void Step(int n)
                    {
                        if (dist[n] >= 0 || !pass[n]) return;
                        dist[n] = dist[c] + 1;
                        m_Queue[tail++] = n;
                    }
                }
                return dist;
            }
        }
    }
}
