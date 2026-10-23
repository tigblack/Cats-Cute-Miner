// Newtonsoft.Json owns these models; they are not Unity Inspector serialization contracts.
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Снимок обстановки шахты (Docs/Rework/MINE_DRESSING.md): постоянные препятствия, детали стен и разрушаемый мусор с ID
    /// целей. Генерирует <see cref="MineDressingPlacer"/> при создании новой шахты, run хранит копию
    /// (<see cref="OutpostRun.MapDressing"/>), карта сохранённой шахты строится по ней (<see cref="MineDressingAdapter.Apply"/>) —
    /// разрушенный мусор не появляется снова, правка правил не двигает объекты начатой шахты. Run без снимка (создан до
    /// обстановки) доигрывается на прежней карте.
    /// </summary>
    public sealed class MineDressing
    {
        public const string CurrentVersion = "mine-dressing-1";
        public string Version = CurrentVersion;
        /// <summary>Версия правил (<see cref="MineDressingRules.Version"/>), по которым снимок построен.</summary>
        public string RulesVersion;
        public string ZoneId;
        public ulong Seed;
        public List<DressingProp> Props = new List<DressingProp>();
        public List<DressingPiece> Pieces = new List<DressingPiece>();
        /// <summary>
        /// Спасение персонажа (первая сессия, <see cref="MineRescuePlacer"/>): клетка — постоянное препятствие, замок — куча мусора на
        /// подходе к ней; null — в шахте спасения нет (все шахты, кроме шахты спасения, и снимки до первой сессии).
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public DressingRescue Rescue;
    }

    /// <summary>
    /// Спасение персонажа в шахте (Docs/Rework/FIRST_SESSION.md): клетка — постоянное препятствие <see cref="CageProp"/> (персонаж
    /// внутри, не цель добычи), замок — цель <see cref="LockTargetId"/> на единственном подходе к ней. Маршрут — цели, которые
    /// лежат на самом дешёвом пути от базы к замку: пока клетка закрыта, котики предпочитают их на <see cref="RouteFocus"/> клеток.
    /// </summary>
    public sealed class DressingRescue
    {
        /// <summary>Персонаж ("mia").</summary>
        public string Character;
        /// <summary>Индекс клетки в <see cref="MineDressing.Props"/>.</summary>
        public int CageProp = -1;
        /// <summary>ID цели-замка (куча мусора обстановки).</summary>
        public int LockTargetId = -1;
        /// <summary>Цели на пути от базы к замку, от базы (без замка): шаг «Расчистить путь» — разрушенные из них.</summary>
        public List<int> RouteTargetIds = new List<int>();
        /// <summary>Предпочтение целей маршрута, клеток пути (0 — обычный выбор ближайшей цели).</summary>
        public int RouteFocus;
    }

    /// <summary>Постоянное препятствие (на полу) или деталь стены: семейство, номер спрайта, якорь — нижняя левая клетка footprint.</summary>
    public sealed class DressingProp
    {
        public string Family;
        public int Variant;
        public int Column;
        public int Row;
        public int W = 1;
        public int H = 1;
        /// <summary>Деталь стены: клетка породы, под которой пол; навигацию не меняет.</summary>
        public bool Wall;
        /// <summary>На препятствии можно крепить трубы.</summary>
        public bool PipeMount;
    }

    /// <summary>
    /// Куча разрушаемого мусора — цель <c>x</c> с ID <see cref="TargetId"/>, ярусом прочности и множителями баланса; с
    /// <see cref="RockType"/> — руда-заполнитель, цель <c>r</c> камнем источника (пустых клеток в шахте нет).
    /// </summary>
    public sealed class DressingPiece
    {
        public int TargetId;
        public string Family;
        public int Variant;
        public string Tier;
        /// <summary>Множитель HP к цели <c>x</c> той же площади (ярус).</summary>
        public Rational HpFactor = Rational.One;
        /// <summary>Множитель цены порции к типу <c>x</c> (ярус).</summary>
        public Rational ValueFactor = Rational.One;
        public int Column;
        public int Row;
        public int W = 1;
        public int H = 1;
        /// <summary>Рабочая ветвь для распределения котиков (ближайшая цель того же сектора); 0 — без ветви.</summary>
        public int Branch;
        /// <summary>Руда-заполнитель: тип камня источника (HP по его таблице и уровню); null — куча мусора.</summary>
        public string RockType;
        /// <summary>Вид камня (MimicId или тип), как у камня-шаблона шахты.</summary>
        public string RockVisual;
        /// <summary>Уровень камня в таблице MiningTargetHealths (как у камня-шаблона шахты).</summary>
        public int RockLevel;

        /// <summary>Руда-заполнитель (цель <c>r</c>), а не мусор.</summary>
        [JsonIgnore] public bool IsOre => RockType != null;
    }

    /// <summary>Постоянное препятствие карты: клетки footprint (на полу — непроходимы) и сектор для затемнения.</summary>
    public sealed class MapProp
    {
        public int Index;
        public string Family;
        public int Variant;
        public int Column;
        public int Row;
        public int Width;
        public int Height;
        public int[] Cells;
        public bool Wall;
        public bool PipeMount;
        /// <summary>Сектор клеток (у детали стены — сектор пола под ней).</summary>
        public int Sector;

        /// <summary>Имя спрайта атласа: prop_&lt;семейство&gt;_&lt;вариант&gt;.</summary>
        public string Sprite => "prop_" + Family + "_" + Variant;
    }

    /// <summary>Перенос снимка обстановки на готовую карту (источник + override + топология).</summary>
    public static class MineDressingAdapter
    {
        /// <summary>Префикс TypeId целей-мусора: "debris_planks" и т. п. (не пересекается с ID Obstructions источника).</summary>
        public const string TypePrefix = "debris_";

        /// <summary>Имя спрайта кучи: debris_&lt;семейство&gt;_&lt;вариант&gt;.</summary>
        public static string Sprite(DressingPiece piece) => TypePrefix + piece.Family + "_" + piece.Variant;

        /// <summary>
        /// Карта с обстановкой: клетки постоянных препятствий на полу становятся непроходимыми (не пол навигации), кучи мусора —
        /// цели <c>x</c> с footprint и рабочими клетками, руда-заполнитель — цели <c>r</c> камнем источника; рабочие клетки всех
        /// целей и маршруты труб пересчитываются. Исходная карта не меняется. Нарушение (занятая клетка, ворота, разные секторы,
        /// повтор ID) — исключение.
        /// </summary>
        public static OutpostMap Apply(OutpostMap source, MineDressing dressing)
        {
            if (source == null || dressing == null) throw new ArgumentNullException();
            if (source.Topology == null) throw new ArgumentException("Dressing needs a topology map: " + source.ZoneId);
            if (dressing.Version != MineDressing.CurrentVersion) throw new ArgumentException("Unsupported dressing version " + dressing.Version);
            if (dressing.ZoneId != source.ZoneId) throw new ArgumentException("Dressing zone mismatch: " + dressing.ZoneId);
            var map = new OutpostMap
            {
                ZoneId = source.ZoneId, Number = source.Number, Width = source.Width, Depth = source.Depth, Rows = source.Rows,
                CellCount = source.CellCount, DecoSeed = source.DecoSeed, SourceSha256 = source.SourceSha256,
                Override = source.Override, OverrideName = source.OverrideName, Topology = source.Topology,
                PlayableCells = (bool[])source.PlayableCells.Clone(),
                CellSectors = (int[])source.CellSectors.Clone(),
                CellGateOrder = (int[])source.CellGateOrder.Clone(),
                SpawnCell = source.SpawnCell, IntakeCell = source.IntakeCell, DepotCells = (int[])source.DepotCells.Clone(),
                ExitCell = source.ExitCell, ExitRewardId = source.ExitRewardId, CampCells = (int[])source.CampCells.Clone(),
                Gates = source.Gates, InitialOccupant = (int[])source.InitialOccupant.Clone(),
            };
            // Карта и run держат одну копию снимка; кучи целей ссылаются на её записи.
            dressing = OutpostJson.DeepCopy(dressing);
            map.Dressing = dressing;
            map.PropAt = new int[map.CellCount];
            for (int i = 0; i < map.PropAt.Length; i++) map.PropAt[i] = -1;

            var props = new MapProp[dressing.Props?.Count ?? 0];
            for (int i = 0; i < props.Length; i++)
            {
                var p = dressing.Props[i];
                if (p == null || string.IsNullOrEmpty(p.Family)) throw new ArgumentException("Empty dressing prop");
                var cells = Footprint(map, p.Column, p.Row, p.W, p.H, "prop " + p.Family);
                int sector;
                if (p.Wall)
                {
                    int cell = cells[0];
                    int below = cell + map.Width;
                    if (cells.Length != 1 || map.IsPlayable(cell) || map.Row(cell) + 1 >= map.Depth || !map.IsPlayable(below))
                        throw new ArgumentException("Wall prop not on a wall face: " + p.Family + " at " + cell);
                    sector = map.SectorAt(below);
                }
                else
                {
                    sector = -1;
                    foreach (int cell in cells)
                    {
                        if (!map.IsPlayable(cell) || map.InitialOccupant[cell] >= 0 || map.CellGateOrder[cell] >= 0)
                            throw new ArgumentException("Prop cell not free floor: " + p.Family + " at " + cell);
                        int s = map.SectorAt(cell);
                        if (sector >= 0 && s != sector) throw new ArgumentException("Prop crosses sectors: " + p.Family);
                        sector = s;
                    }
                    foreach (int cell in cells)
                    {
                        map.PlayableCells[cell] = false;
                        map.CellSectors[cell] = -1;
                        map.PropAt[cell] = i;
                    }
                }
                props[i] = new MapProp
                {
                    Index = i, Family = p.Family, Variant = p.Variant, Column = p.Column, Row = p.Row, Width = p.W, Height = p.H,
                    Cells = cells, Wall = p.Wall, PipeMount = p.PipeMount, Sector = sector,
                };
            }
            map.Props = props;

            int maxId = -1;
            foreach (var t in source.Targets) maxId = Math.Max(maxId, t.Id);
            var targets = new List<MapTarget>(source.Targets.Length + (dressing.Pieces?.Count ?? 0));
            foreach (var t in source.Targets)
                targets.Add(new MapTarget
                {
                    Id = t.Id, Kind = t.Kind, TypeId = t.TypeId, VisualId = t.VisualId, Column = t.Column, Row = t.Row,
                    Cells = t.Cells, Sector = t.Sector, Branch = t.Branch, SourceLevel = t.SourceLevel, Added = t.Added,
                    RewardId = t.RewardId, CardId = t.CardId, CardLevel = t.CardLevel,
                });
            var ids = new HashSet<int>();
            foreach (var piece in dressing.Pieces ?? new List<DressingPiece>())
            {
                if (piece == null || string.IsNullOrEmpty(piece.Family)) throw new ArgumentException("Empty dressing piece");
                if (piece.TargetId <= maxId || !ids.Add(piece.TargetId)) throw new ArgumentException("Dressing piece id clash: " + piece.TargetId);
                if (piece.HpFactor.Sign <= 0 || piece.ValueFactor.Sign <= 0) throw new ArgumentException("Dressing piece factors: " + piece.TargetId);
                if (piece.IsOre && piece.RockLevel < 1) throw new ArgumentException("Dressing ore level: " + piece.TargetId);
                var cells = Footprint(map, piece.Column, piece.Row, piece.W, piece.H, "piece " + piece.TargetId);
                int sector = -1;
                foreach (int cell in cells)
                {
                    if (!map.IsPlayable(cell) || map.InitialOccupant[cell] >= 0 || map.CellGateOrder[cell] >= 0)
                        throw new ArgumentException("Dressing piece cell not free floor: " + piece.TargetId + " at " + cell);
                    int s = map.SectorAt(cell);
                    if (sector >= 0 && s != sector) throw new ArgumentException("Dressing piece crosses sectors: " + piece.TargetId);
                    sector = s;
                }
                foreach (int cell in cells) map.InitialOccupant[cell] = piece.TargetId;
                targets.Add(piece.IsOre
                    ? new MapTarget
                    {
                        Id = piece.TargetId, Kind = 'r', TypeId = piece.RockType, VisualId = piece.RockVisual ?? piece.RockType,
                        SourceLevel = piece.RockLevel, Column = piece.Column, Row = piece.Row, Cells = cells, Sector = sector,
                        Branch = piece.Branch, Piece = piece,
                    }
                    : new MapTarget
                    {
                        Id = piece.TargetId, Kind = 'x', TypeId = TypePrefix + piece.Family, VisualId = Sprite(piece),
                        Column = piece.Column, Row = piece.Row, Cells = cells, Sector = sector, Branch = piece.Branch, Piece = piece,
                    });
            }
            targets.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var t in targets)
            {
                t.WorkCells = MineTopologyAdapter.Neighbours(map, t);
                if (t.WorkCells.Length == 0) throw new ArgumentException("No playable work position after dressing: " + t.Id);
            }
            map.Targets = targets.ToArray();
            map.RequiredCount = source.RequiredCount + (dressing.Pieces?.Count ?? 0);
            map.BuildIndex();
            var rescue = dressing.Rescue;
            if (rescue != null)
            {
                if (string.IsNullOrEmpty(rescue.Character)) throw new ArgumentException("Rescue without character");
                if (rescue.CageProp < 0 || rescue.CageProp >= props.Length || props[rescue.CageProp].Wall)
                    throw new ArgumentException("Rescue cage is not a floor prop: " + rescue.CageProp);
                if (!ids.Contains(rescue.LockTargetId)) throw new ArgumentException("Rescue lock is not a dressing piece: " + rescue.LockTargetId);
                if (rescue.RouteTargetIds == null) rescue.RouteTargetIds = new List<int>();
                foreach (int id in rescue.RouteTargetIds)
                    if (map.TargetIndex(id) < 0 || id == rescue.LockTargetId) throw new ArgumentException("Rescue route target: " + id);
            }
            return map;
        }

        /// <summary>Клетки footprint по возрастанию: якорь (Column, Row) — нижняя левая клетка, footprint растёт вверх и вправо.</summary>
        internal static int[] Footprint(OutpostMap map, int column, int row, int w, int h, string what)
        {
            if (w < 1 || h < 1 || w > 2 || h > 2) throw new ArgumentException("Dressing footprint " + w + "×" + h + ": " + what);
            var cells = new int[w * h];
            int k = 0;
            for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                {
                    int c = column + dx, r = row - dy;
                    if (c < 0 || c >= map.Width || r < 0 || r >= map.Depth) throw new ArgumentException("Dressing footprint outside mine: " + what);
                    cells[k++] = map.Cell(c, r);
                }
            Array.Sort(cells);
            return cells;
        }
    }
}
