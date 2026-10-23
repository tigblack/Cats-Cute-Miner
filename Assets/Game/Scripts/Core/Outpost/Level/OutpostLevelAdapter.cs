using System;
using System.Collections.Generic;
using CatMiners.Core.Data;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Новые значения токенов (ТЗ 8.2–8.4): площадка базы из трёх строк под картой, ворота как логический барьер,
    /// секторы, рабочие клетки. c и e — проходимые маркеры, старой искусственной занятости нет.
    /// </summary>
    public static class OutpostLevelAdapter
    {
        public const int BaseRows = 3;

        /// <summary>Импорт и адаптация зоны одним вызовом: новая шахта получает текущий override зоны из конфигурации.</summary>
        public static OutpostMap Build(BalanceRepository repo, int zoneNumber, LevelOverrides overrides = null)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            return BuildWithOverride(repo, zoneNumber, overrides?.For(repo.Zone(zoneNumber).Id));
        }

        /// <summary>Карта с заданным override зоны (null — исходная геометрия).</summary>
        public static OutpostMap BuildWithOverride(BalanceRepository repo, int zoneNumber, ZoneOverride zoneOverride) =>
            Build(SourceLevelImporter.Import(repo, zoneNumber, zoneOverride));

        /// <summary>New campaign mine. Source/override owns content; a separate snapshot owns geometry.</summary>
        public static OutpostMap BuildMine(BalanceRepository repo, int zoneNumber, OutpostConfigSet configs)
        {
            if (configs == null) throw new ArgumentNullException(nameof(configs));
            var map = Build(repo, zoneNumber, configs.Overrides);
            if (configs.Topologies == null) return map;
            if (configs.Topologies.SourceSha256 != repo.Sha256)
                throw new ArgumentException("Mine topology source checksum mismatch");
            var topology = configs.Topologies.For(map.ZoneId);
            if (topology == null) throw new ArgumentException("Missing topology: " + map.ZoneId);
            map = MineTopologyAdapter.Apply(map, topology);
            // Обстановка — слой поверх геометрии новой шахты (Docs/Rework/MINE_DRESSING.md); снимок уходит в run.
            var rules = configs.Dressing;
            if (rules == null || zoneNumber < rules.FromZone) return map;
            if (zoneNumber == 1 && topology.Layout == MiaCorridor.Layout)
                return MiaCorridor.PlaceRescue(map, rules, configs.Progression?.RescueForZone(1));
            var dressing = MineDressingPlacer.Generate(map, rules);
            // Первая сессия: клетка с персонажем и замок в последнем участке шахты спасения (Docs/Rework/FIRST_SESSION.md).
            dressing = MineRescuePlacer.Place(map, dressing, rules, configs.Progression?.RescueForZone(zoneNumber));
            return FinalExcavationAdapter.Generate(MineDressingAdapter.Apply(map, dressing), configs, repo);
        }

        /// <summary>
        /// Карта сохранённой шахты: с копией override, с которой run создан (<see cref="OutpostRun.MapOverride"/>), а не с текущим
        /// файлом конфигурации — изменённый override не подменяет геометрию незавершённой шахты.
        /// </summary>
        public static OutpostMap ForRun(BalanceRepository repo, OutpostRun run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            var map = BuildWithOverride(repo, run.Zone, run.MapOverride);
            if (run.MapTopology == null) return map;
            map = MineTopologyAdapter.Apply(map, run.MapTopology);
            // Шахта, начатая до обстановки (снимка нет), доигрывается на прежней карте.
            map = run.MapDressing == null ? map : MineDressingAdapter.Apply(map, run.MapDressing);
            map = FinalExcavationAdapter.Apply(map, run.MapFinal);
            FinalExcavationMigration.TryCompact(run, map, out _);
            return map;
        }

        /// <summary>
        /// Адаптация импортированной зоны. Геометрию override вносит импорт (<see cref="SourceZone.Override"/>);
        /// <paramref name="overrides"/> только сверяется с ним: зона с override не строится из импорта без него.
        /// </summary>
        public static OutpostMap Build(SourceZone src, LevelOverrides overrides = null)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            var expected = overrides?.For(src.ZoneId);
            if (expected != null && expected.HasGeometry() && !ZoneOverride.SameAs(expected, src.Override))
                throw new ArgumentException(src.ZoneId + ": импорт без override " + expected.Name);
            int width = src.Width, depth = src.Depth, rows = depth + BaseRows;
            var map = new OutpostMap
            {
                ZoneId = src.ZoneId,
                Number = src.Number,
                Width = width,
                Depth = depth,
                Rows = rows,
                CellCount = width * rows,
                DecoSeed = src.DecoSeed,
                SourceSha256 = src.SourceSha256,
                Override = src.Override,
                OverrideName = src.Override?.Name,
            };
            int baseCol = width / 2;
            map.SpawnCell = map.Cell(baseCol, depth + 1);
            map.IntakeCell = map.SpawnCell;
            if (baseCol - 1 < 0 || baseCol + 1 >= width) throw new OutpostDataException(src.ZoneId, -1, "поле слишком узкое для базы");
            map.DepotCells = DepotCells(map, baseCol, depth + 1);
            var camp = new int[width * BaseRows];
            for (int r = 0; r < BaseRows; r++)
                for (int c = 0; c < width; c++)
                    camp[r * width + c] = map.Cell(c, depth + r);
            map.CampCells = camp;

            // Ворота: по строке убыв., затем по индексу якоря.
            var gateTokens = new List<SourceToken>();
            var targetTokens = new List<SourceToken>();
            foreach (var t in src.Tokens)
            {
                if (t == null) continue;
                switch (t.Kind)
                {
                    case 'p':
                        gateTokens.Add(t);
                        targetTokens.Add(t);
                        break;
                    case 'r':
                    case 's':
                    case 'x':
                        targetTokens.Add(t);
                        break;
                    case 'e':
                        map.ExitCell = map.Cell(t.Column, t.Row);
                        map.ExitRewardId = string.IsNullOrEmpty(t.RewardId) ? null : t.RewardId;
                        break;
                }
            }
            gateTokens.Sort((a, b) => a.Row != b.Row ? b.Row.CompareTo(a.Row) : a.Index.CompareTo(b.Index));
            var gates = new MapGate[gateTokens.Count];
            for (int k = 0; k < gates.Length; k++)
            {
                if (k > 0 && gateTokens[k].Row == gateTokens[k - 1].Row)
                    throw new OutpostDataException(src.ZoneId, gateTokens[k].Index, "повтор строки ворот " + gateTokens[k].Row);
                gates[k] = new MapGate { TargetId = gateTokens[k].Index, Row = gateTokens[k].Row, Order = k };
            }
            map.Gates = gates;

            // Цели по возрастанию Id (индекс якоря).
            targetTokens.Sort((a, b) => a.Index.CompareTo(b.Index));
            var targets = new MapTarget[targetTokens.Count];
            var occupant = new int[map.CellCount];
            for (int i = 0; i < occupant.Length; i++) occupant[i] = -1;
            var gateOrder = new Dictionary<int, int>();
            foreach (var g in gates) gateOrder[g.TargetId] = g.Order;
            int required = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                var t = targetTokens[i];
                var mt = new MapTarget
                {
                    Id = t.Index,
                    Kind = t.Kind,
                    TypeId = t.TypeId,
                    VisualId = t.MimicId ?? t.TypeId,
                    Column = t.Column,
                    Row = t.Row,
                    Cells = (int[])t.Cells.Clone(),
                    SourceLevel = t.Level,
                    Added = t.Added,
                    RewardId = string.IsNullOrEmpty(t.RewardId) ? null : t.RewardId,
                    CardId = t.Kind == 's' && !string.IsNullOrEmpty(t.CardId) ? t.CardId : null,
                    CardLevel = t.Kind == 's' ? t.CardLevel : 0,
                };
                if (t.Kind == 'p')
                {
                    mt.Sector = gateOrder[t.Index];
                    var wc = new int[width];
                    for (int c = 0; c < width; c++) wc[c] = map.Cell(c, t.Row);
                    mt.WorkCells = wc;
                }
                else
                {
                    int maxRow = int.MinValue;
                    foreach (int cell in mt.Cells)
                    {
                        maxRow = Math.Max(maxRow, cell / width);
                        occupant[cell] = mt.Id;
                    }
                    int sector = 0;
                    foreach (var g in gates)
                        if (g.Row > maxRow) sector++;
                    mt.Sector = sector;
                    mt.WorkCells = NeighbourCells(mt.Cells, width, rows);
                    if (mt.WorkCells.Length == 0) throw new OutpostDataException(src.ZoneId, t.Index, "у цели нет соседних клеток");
                }
                targets[i] = mt;
                required++;
            }
            map.Targets = targets;
            map.RequiredCount = required;
            map.InitialOccupant = occupant;
            map.BuildIndex();
            return map;
        }

        /// <summary>
        /// Места разгрузки в среднем ряду лагеря (строка <paramref name="row"/>): два с начала шахты по сторонам точки приёма
        /// (baseCol ∓ 1), дальше — места ветки «Места разгрузки» (baseCol ∓ 2), пока они помещаются в ширину поля
        /// (<see cref="OutpostIds.MaxDepotSlots"/>). Порядок — порядок мест <see cref="OutpostRun.Slots"/>.
        /// </summary>
        public static int[] DepotCells(OutpostMap map, int baseCol, int row)
        {
            var cells = new List<int>(OutpostIds.MaxDepotSlots) { map.Cell(baseCol - 1, row), map.Cell(baseCol + 1, row) };
            if (baseCol - 2 >= 0) cells.Add(map.Cell(baseCol - 2, row));
            if (baseCol + 2 < map.Width && cells.Count < OutpostIds.MaxDepotSlots) cells.Add(map.Cell(baseCol + 2, row));
            return cells.ToArray();
        }

        /// <summary>Клетки сетки (включая площадку), ортогонально соседние с footprint и не входящие в него, по возрастанию.</summary>
        static int[] NeighbourCells(int[] cells, int width, int rows)
        {
            var own = new HashSet<int>(cells);
            var result = new SortedSet<int>();
            foreach (int cell in cells)
            {
                int col = cell % width, row = cell / width;
                Add(col, row - 1);
                Add(col - 1, row);
                Add(col + 1, row);
                Add(col, row + 1);
            }
            var arr = new int[result.Count];
            result.CopyTo(arr);
            return arr;

            void Add(int c, int r)
            {
                if (c < 0 || c >= width || r < 0 || r >= rows) return;
                int n = r * width + c;
                if (!own.Contains(n)) result.Add(n);
            }
        }
    }

    /// <summary>
    /// Неизменяемая карта режима: сетка навигации Width × (Depth + 3), клетка = row · Width + col
    /// (ID исходных целей сохраняются независимо от новых координат), площадка базы — строки Depth..Depth+2.
    /// </summary>
    public sealed class OutpostMap
    {
        public string ZoneId;
        public int Number;
        public int Width;
        public int Depth;
        public int Rows;
        public int CellCount;
        public int DecoSeed;
        public string SourceSha256;
        /// <summary>Override с геометрией, из которого построена карта, или null; новая шахта копирует его в run.</summary>
        public ZoneOverride Override;
        public string OverrideName;
        public MineTopology Topology;
        /// <summary>Снимок обстановки (постоянные препятствия, детали стен, мусор) или null — шахта без обстановки.</summary>
        public MineDressing Dressing;
        public FinalExcavation Final;
        /// <summary>Спасение персонажа в этой шахте (первая сессия) или null.</summary>
        public DressingRescue Rescue => Dressing?.Rescue;
        /// <summary>Клетка персонажа спасения (постоянное препятствие) или −1.</summary>
        public int RescueCageCell
        {
            get
            {
                var r = Rescue;
                return r != null && Props != null && r.CageProp >= 0 && r.CageProp < Props.Length ? Props[r.CageProp].Cells[0] : -1;
            }
        }
        /// <summary>Постоянные препятствия и детали стен по порядку снимка (пусто без обстановки).</summary>
        public MapProp[] Props = new MapProp[0];
        /// <summary>Клетка → индекс постоянного препятствия на полу (<see cref="Props"/>) или −1; null — без обстановки.</summary>
        public int[] PropAt;
        public bool[] PlayableCells;
        public int[] CellSectors;
        public int[] CellGateOrder;

        public int SpawnCell;
        public int IntakeCell;
        public int[] DepotCells;
        public int ExitCell;
        /// <summary>Запись Gacha сундука выхода (исходный rewardId токена e) или null.</summary>
        public string ExitRewardId;
        /// <summary>Все клетки площадки базы (декоративный сбор и возвращение домой).</summary>
        public int[] CampCells;

        /// <summary>Цели r/s/x/p по возрастанию Id.</summary>
        public MapTarget[] Targets;
        /// <summary>Ворота по Order (0 — нижние).</summary>
        public MapGate[] Gates;
        /// <summary>Число обязательных целей r/s/x + ворот.</summary>
        public int RequiredCount;
        /// <summary>Исходная занятость: клетка → Id живой цели с footprint или −1.</summary>
        public int[] InitialOccupant;
        /// <summary>
        /// Заводы (цели <c>s</c>) по возрастанию TargetId: требование автодоставки, клетка выхода трубы и маршрут до
        /// <see cref="IntakeCell"/> (ТЗ 2.2 §5). Строится вместе с индексом целей (<see cref="FactoryRoutes.Build"/>); не сохраняется.
        /// </summary>
        public MapFactory[] Factories = new MapFactory[0];

        int[] m_IndexById;

        public int Cell(int col, int row) => row * Width + col;
        public int Col(int cell) => cell % Width;
        public int Row(int cell) => cell / Width;
        public bool IsPlayable(int cell) => cell >= 0 && cell < CellCount && (PlayableCells == null || PlayableCells[cell]);
        /// <summary>Клетку занимает постоянное препятствие обстановки (пол под ним непроходим).</summary>
        public bool IsProp(int cell) => PropAt != null && cell >= 0 && cell < CellCount && PropAt[cell] >= 0;
        /// <summary>Пол для оформления: проходимая клетка или пол под постоянным препятствием (не порода).</summary>
        public bool IsFloor(int cell) => IsPlayable(cell) || IsProp(cell);
        public int SectorAt(int cell) => cell >= 0 && cell < CellCount && CellSectors != null ? CellSectors[cell] : 0;

        /// <summary>Индекс цели в <see cref="Targets"/> по Id или −1.</summary>
        public int TargetIndex(int targetId) =>
            targetId >= 0 && targetId < m_IndexById.Length ? m_IndexById[targetId] : -1;

        public MapTarget Target(int targetId)
        {
            int i = TargetIndex(targetId);
            return i < 0 ? null : Targets[i];
        }

        public bool IsCampCell(int cell) => cell >= Depth * Width && cell < CellCount;

        /// <summary>Завод карты по ID цели или null (не завод).</summary>
        public MapFactory Factory(int targetId)
        {
            var list = Factories;
            for (int i = 0; i < list.Length; i++)
                if (list[i].TargetId == targetId) return list[i];
            return null;
        }

        /// <summary>Индекс целей по Id и заводы с маршрутами труб (ТЗ 2.2 §5) — последний шаг построения карты.</summary>
        internal void BuildIndex()
        {
            int size = Width * Depth;
            foreach (var target in Targets) size = Math.Max(size, target.Id + 1);
            m_IndexById = new int[size];
            for (int i = 0; i < m_IndexById.Length; i++) m_IndexById[i] = -1;
            for (int i = 0; i < Targets.Length; i++) m_IndexById[Targets[i].Id] = i;
            Factories = FactoryRoutes.Build(this);
        }
    }

    /// <summary>Цель шахты: r/s/x с footprint или ворота p (без footprint, рабочие клетки — строка ворот).</summary>
    public sealed class MapTarget
    {
        /// <summary>Индекс якоря в исходной сетке.</summary>
        public int Id;
        public char Kind;
        public string TypeId;
        /// <summary>MimicId или TypeId — только для оформления.</summary>
        public string VisualId;
        public int Column;
        public int Row;
        /// <summary>Footprint (пусто у ворот).</summary>
        public int[] Cells;
        public int Sector;
        /// <summary>Authored work branch for balancing new task assignments; zero for gates/legacy layouts.</summary>
        public int Branch;
        /// <summary>Кандидаты рабочих клеток по возрастанию; свободность, барьер и достижимость проверяются в рантайме.</summary>
        public int[] WorkCells;
        public int SourceLevel;
        /// <summary>Цель добавлена override зоны.</summary>
        public bool Added;
        /// <summary>Запись Gacha сундука s/p (исходный rewardId токена) или null.</summary>
        public string RewardId;
        /// <summary>Завод (<c>s</c>): карточка требования автодоставки (поле cardId токена) или null у прочих целей (ТЗ 2.2 §4.4).</summary>
        public string CardId;
        /// <summary>Завод (<c>s</c>): уровень карточки требования (поле cardLevel токена); 0 у прочих целей.</summary>
        public int CardLevel;

        /// <summary>Куча мусора (цель <c>x</c>) или руда-заполнитель (цель <c>r</c>) из <see cref="OutpostMap.Dressing"/>; null у целей источника.</summary>
        public DressingPiece Piece;
        public FinalExcavation Final;
        public bool IsFinal => Final != null;
        /// <summary>Ordinary rocks in the final neck; excluded from the existing mine's price multiplier.</summary>
        public bool IsFinalApproach;

        public bool IsGate => Kind == 'p';
        /// <summary>Цель — завод в цепях (ТЗ 2.2: бывшее месторождение <c>s</c>).</summary>
        public bool IsFactory => Kind == 's';
        /// <summary>Цель — разрушаемый мусор обстановки (Docs/Rework/MINE_DRESSING.md).</summary>
        public bool IsDebris => Piece != null && Kind == 'x';
        /// <summary>Цель — руда-заполнитель обстановки: камень источника в бывшей пустой клетке, только золото, без наград цикла.</summary>
        public bool IsOreFill => Piece != null && Kind == 'r';
    }

    /// <summary>
    /// Завод карты (ТЗ 2.2 §4.1, §5): требование автодоставки из токена и маршрут трубы по итоговой карте. Маршрут — кратчайший
    /// 4-связный путь от <see cref="OutletCell"/> до <see cref="OutpostMap.IntakeCell"/>; стены — непроходимые клетки и footprint
    /// всех заводов, живые цели и ворота не мешают. Вычисляется при построении карты, детерминирован, не сохраняется.
    /// </summary>
    public sealed class MapFactory
    {
        /// <summary>ID цели <c>s</c> (= ID завода в <see cref="FactoryState.TargetId"/>).</summary>
        public int TargetId;
        /// <summary>Тип JSON ("amethyst").</summary>
        public string TypeId;
        /// <summary>Требование автодоставки: карточка (Common или Uncommon этого типа) и её уровень из токена.</summary>
        public string RequiredCardId;
        public int RequiredCardLevel;
        /// <summary>Клетка выхода трубы: свободная соседняя с footprint, ближайшая к IntakeCell, при равенстве — меньший индекс; −1 — маршрута нет.</summary>
        public int OutletCell = -1;
        /// <summary>RouteCells ТЗ 2.2 §5: число шагов (рёбер) маршрута от OutletCell до IntakeCell; −1 — маршрута нет.</summary>
        public int RouteSteps = -1;
        /// <summary>Клетки маршрута от OutletCell до IntakeCell включительно (длина RouteSteps + 1; пусто — маршрута нет). Для вида труб.</summary>
        public int[] RouteCells = new int[0];

        public bool HasRoute => RouteSteps >= 0;
    }

    public sealed class MapGate
    {
        public int TargetId;
        public int Row;
        public int Order;
        /// <summary>Physical passage cells for branching maps; null for the saved legacy row barrier.</summary>
        public int[] BarrierCells;
    }
}
