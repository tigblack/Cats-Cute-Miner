using System;
using System.Collections.Generic;
using System.Globalization;
using CatMiners.Core.Data;

namespace CatMiners.Core.Grid
{
    public enum ObjectKind
    {
        Rock,
        Shaft,
        Obstruction,
        Gate,
        Exit,
        Cart,
    }

    /// <summary>Объект карты из токена Grid. Ключ экземпляра — ZoneId + Index (линейный индекс якоря).</summary>
    public sealed class ZoneObject
    {
        public int Ordinal;          // позиция в списке объектов зоны
        public int Index;            // линейный индекс якоря в Grid
        public string Token;
        public ObjectKind Kind;
        public string TypeId;        // id камня / шахты / препятствия, "checkpoint", "exit", "spawningcart"
        public int Level;            // уровень цели (r, s, p, e) или кузницы (c)
        public string RewardId;      // s, p, e
        public string CardId;        // s: требуемая manager-карта
        public int CardLevel;        // s: требуемый уровень карты
        public int Col, Row;         // якорь
        public int Width, Depth;     // footprint (+column / −row)
        public int[] Cells;          // занятые клетки (линейные индексы)
        public int GateOrder = -1;   // для ворот: 0 — нижние

        public bool IsTarget => Kind == ObjectKind.Rock || Kind == ObjectKind.Shaft || Kind == ObjectKind.Gate || Kind == ObjectKind.Exit;
    }

    public sealed class LayoutIssue
    {
        public string ZoneId;
        public int TokenIndex;
        public string Message;
        public override string ToString() => ZoneId + (TokenIndex >= 0 ? "[" + TokenIndex.ToString(CultureInfo.InvariantCulture) + "]" : "") + ": " + Message;
    }

    /// <summary>
    /// Логическое поле зоны: разбор Grid в два прохода (ТЗ 6.1–6.4), footprint, сектора.
    /// Неизменяемо; состояние игры хранится отдельно.
    /// </summary>
    public sealed class ZoneLayout
    {
        // Логическая геометрия особых объектов, выведенная из всех 144 карт (Приложение C, G-02…G-04).
        public const int ExitWidth = 3, ExitDepth = 1;
        public const int GateWidth = 5, GateDepth = 1;
        /// <summary>
        /// Кузница 2×3 (ТЗ 6.4): токен c стоит в строке row_c, опора footprint — строкой ниже, клетки (1..2, row_c − 1 … row_c + 1).
        /// В данных под кузницей 2×2 (G-04); третья строка — row_c + 1, во всех зонах свободная, зона мержа над кузницей не задета.
        /// </summary>
        public const int CartWidth = 2, CartDepth = 3;
        /// <summary>Пушка (ТЗ 6.4, 7.2): у правого края, строки row_c − 1 … row_c.</summary>
        public const int CannonWidth = 1, CannonDepth = 2;
        /// <summary>Ширина поля: во всех 144 зонах ровно 7 столбцов, это и максимум сетки по горизонтали (ТЗ 6.4).</summary>
        public const int MaxWidth = 7;

        public string ZoneId { get; private set; }
        public int ZoneNumber { get; private set; }
        public int Width { get; private set; }
        public int Depth { get; private set; }
        public int CellCount => Width * Depth;

        public readonly List<ZoneObject> Objects = new List<ZoneObject>();
        public readonly List<LayoutIssue> Issues = new List<LayoutIssue>();

        /// <summary>Объект, занимающий клетку в исходной карте (Ordinal), или −1.</summary>
        public int[] CellOwner { get; private set; }
        /// <summary>Сектор клетки: 0 — стартовый, −1 — строка ворот.</summary>
        public int[] CellSector { get; private set; }

        public ZoneObject Exit { get; private set; }
        public ZoneObject Cart { get; private set; }
        /// <summary>
        /// row_c (ТЗ 6.4, 6.6): строка токена кузницы в данных. От неё считаются стартовая площадка и зона мержа
        /// (row_c − 3, row_c − 2); сама кузница 2×3 стоит опорой на строку ниже (<see cref="CartDepth"/>). −1 — кузницы нет.
        /// </summary>
        public int RowC { get; private set; } = -1;
        /// <summary>
        /// Пушка (ТЗ 6.4, 7.2), из которой вылетают бочки с котиками: у правого края поля (столбец Width − 1), якорь — строка
        /// row_c, footprint <see cref="CannonWidth"/>×<see cref="CannonDepth"/> вверх, сразу под зоной мержа. Её клетки
        /// закреплены за кузницей (<see cref="CellOwner"/> = Ordinal кузницы: пушка — часть её хозяйства), поэтому туда не
        /// появляются и не ставятся котики, контейнеры и добыча. −1 — места нет (пушки нет, бочки появляются на месте).
        /// В Objects пушки нет: сохранения и данные не меняются.
        /// </summary>
        public int CannonCol { get; private set; } = -1;
        public int CannonRow { get; private set; } = -1;
        public bool HasCannon => CannonCol >= 0;
        /// <summary>Ворота снизу вверх (GateOrder 0 — нижние).</summary>
        public readonly List<ZoneObject> Gates = new List<ZoneObject>();
        public readonly List<ZoneObject> Shafts = new List<ZoneObject>();
        public int SectorCount => Gates.Count + 1;

        public bool IsValid => Issues.Count == 0;

        public static ZoneLayout Build(BalanceRepository balance, int zoneNumber) => Build(balance, balance.Zone(zoneNumber));

        public static ZoneLayout Build(BalanceRepository balance, ZoneDto zone)
        {
            var layout = new ZoneLayout
            {
                ZoneId = zone.Id,
                ZoneNumber = BalanceRepository.ZoneNumber(zone.Id),
                Width = zone.WidthCells,
                Depth = zone.DepthCells,
            };
            layout.Parse(balance, zone);
            return layout;
        }

        public int CellIndex(int col, int row) => row * Width + col;
        public int Col(int cell) => cell % Width;
        public int Row(int cell) => cell / Width;
        public bool InBounds(int col, int row) => col >= 0 && col < Width && row >= 0 && row < Depth;

        public void Neighbors4(int cell, List<int> result)
        {
            result.Clear();
            int c = Col(cell), r = Row(cell);
            // Порядок: верх, лево, право, низ (ТЗ 6.6).
            if (r - 1 >= 0) result.Add(cell - Width);
            if (c - 1 >= 0) result.Add(cell - 1);
            if (c + 1 < Width) result.Add(cell + 1);
            if (r + 1 < Depth) result.Add(cell + Width);
        }

        /// <summary>Строка целиком свободна от footprint объектов исходной карты (все столбцы).</summary>
        public bool RowFullyFree(int row)
        {
            if (row < 0 || row >= Depth || CellOwner == null) return false;
            for (int c = 0; c < Width; c++)
                if (CellOwner[CellIndex(c, row)] >= 0) return false;
            return true;
        }

        /// <summary>Строки стартовой площадки появления (ТЗ 6.6): row_c − 3 … row_c + 1.</summary>
        public void StartPadRows(out int topRow, out int bottomRow)
        {
            int rc = Cart != null ? RowC : Depth - 4;
            topRow = Math.Max(0, rc - 3);
            bottomRow = Math.Min(Depth - 1, rc + 1);
        }

        /// <summary>Строки посадочной площадки над воротами с порядком <paramref name="gateOrder"/> (ТЗ 6.4): row_p − 3 … row_p − 1.</summary>
        public void GatePadRows(int gateOrder, out int topRow, out int bottomRow)
        {
            int rp = Gates[gateOrder].Row;
            topRow = Math.Max(0, rp - 3);
            bottomRow = rp - 1;
        }

        /// <summary>
        /// Зона мержа (ТЗ 6.6): самая нижняя пара соседних строк площадки [topRow…bottomRow], свободных от объектов
        /// по всей ширине. Перед игроком всегда два свободных ряда по 7 клеток — туда появляются и там объединяются котики.
        /// </summary>
        public bool TryMergeRows(int topRow, int bottomRow, out int upperRow, out int lowerRow)
        {
            for (int r = Math.Min(bottomRow, Depth - 1); r - 1 >= Math.Max(0, topRow); r--)
            {
                if (RowFullyFree(r) && RowFullyFree(r - 1))
                {
                    upperRow = r - 1;
                    lowerRow = r;
                    return true;
                }
            }
            upperRow = lowerRow = -1;
            return false;
        }

        public int SectorOfRow(int row)
        {
            int sector = 0;
            foreach (var g in Gates)
            {
                if (g.Row == row) return -1;
                if (g.Row > row) sector++;
            }
            return sector;
        }

        void Issue(int tokenIndex, string message)
        {
            Issues.Add(new LayoutIssue { ZoneId = ZoneId, TokenIndex = tokenIndex, Message = message });
        }

        void Parse(BalanceRepository balance, ZoneDto zone)
        {
            if (Width <= 0 || Depth <= 0)
            {
                Issue(-1, "некорректный размер " + Width + "×" + Depth);
                return;
            }
            string[] tokens = (zone.Grid ?? string.Empty).Split(',');
            if (tokens.Length != Width * Depth)
            {
                Issue(-1, "Grid: " + tokens.Length + " токенов, ожидалось " + (Width * Depth));
                return;
            }

            // Проход 1: якоря.
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (token == ".") continue;
                var obj = ParseToken(balance, token, i);
                if (obj == null) continue;
                obj.Ordinal = Objects.Count;
                Objects.Add(obj);
            }

            // Проход 2: footprint.
            CellOwner = new int[Width * Depth];
            for (int i = 0; i < CellOwner.Length; i++) CellOwner[i] = -1;
            foreach (var obj in Objects)
            {
                var cells = new List<int>(obj.Width * obj.Depth);
                for (int dx = 0; dx < obj.Width; dx++)
                {
                    for (int dz = 0; dz < obj.Depth; dz++)
                    {
                        int c = obj.Col + dx, r = obj.Row - dz;
                        if (!InBounds(c, r))
                        {
                            Issue(obj.Index, obj.Token + ": footprint выходит за карту в клетке (" + c + "," + r + ")");
                            continue;
                        }
                        int cell = CellIndex(c, r);
                        if (CellOwner[cell] >= 0)
                        {
                            Issue(obj.Index, obj.Token + ": пересечение с " + Objects[CellOwner[cell]].Token + " в клетке (" + c + "," + r + ")");
                            continue;
                        }
                        CellOwner[cell] = obj.Ordinal;
                        cells.Add(cell);
                    }
                }
                obj.Cells = cells.ToArray();
            }

            // Особые объекты и сектора.
            foreach (var obj in Objects)
            {
                switch (obj.Kind)
                {
                    case ObjectKind.Exit:
                        if (Exit != null) Issue(obj.Index, "второй выход");
                        Exit = obj;
                        break;
                    case ObjectKind.Cart:
                        if (Cart != null) Issue(obj.Index, "вторая кузница");
                        Cart = obj;
                        break;
                    case ObjectKind.Gate:
                        Gates.Add(obj);
                        break;
                    case ObjectKind.Shaft:
                        Shafts.Add(obj);
                        break;
                }
            }
            if (Exit == null) Issue(-1, "нет выхода e");
            if (Cart == null) Issue(-1, "нет кузницы c");
            else PlaceCannon();
            Gates.Sort((a, b) => b.Row.CompareTo(a.Row));
            for (int k = 0; k < Gates.Count; k++) Gates[k].GateOrder = k;

            CellSector = new int[Width * Depth];
            for (int r = 0; r < Depth; r++)
            {
                int s = SectorOfRow(r);
                for (int c = 0; c < Width; c++) CellSector[CellIndex(c, r)] = s;
            }
        }

        /// <summary>Пушка у правого края в строках row_c − 1 … row_c, если обе клетки на карте и свободны от footprint объектов.</summary>
        void PlaceCannon()
        {
            int col = Width - CannonWidth;
            for (int dz = 0; dz < CannonDepth; dz++)
            {
                int r = RowC - dz;
                if (!InBounds(col, r) || CellOwner[CellIndex(col, r)] >= 0) return;
            }
            for (int dz = 0; dz < CannonDepth; dz++) CellOwner[CellIndex(col, RowC - dz)] = Cart.Ordinal;
            CannonCol = col;
            CannonRow = RowC;
        }

        /// <summary>Клетка — под пушкой (ТЗ 7.2).</summary>
        public bool IsCannonCell(int cell) =>
            HasCannon && Col(cell) >= CannonCol && Col(cell) < CannonCol + CannonWidth && Row(cell) <= CannonRow && Row(cell) > CannonRow - CannonDepth;

        ZoneObject ParseToken(BalanceRepository balance, string token, int index)
        {
            string[] f = token.Split(':');
            var obj = new ZoneObject { Token = token, Index = index, Col = index % Width, Row = index / Width };
            switch (f[0])
            {
                case "r":
                    if (!CheckFields(f, 3, token, index)) return null;
                    obj.Kind = ObjectKind.Rock;
                    obj.TypeId = f[1];
                    obj.Level = ParseInt(f[2], token, index);
                    if (!balance.Rocks.TryGetValue(obj.TypeId, out var rock)) { Issue(index, "неизвестный камень " + obj.TypeId); return null; }
                    obj.Width = rock.WidthCells; obj.Depth = rock.DepthCells;
                    CheckHp(balance, obj, index);
                    return obj;
                case "s":
                    if (!CheckFields(f, 6, token, index)) return null;
                    obj.Kind = ObjectKind.Shaft;
                    obj.TypeId = f[1];
                    obj.Level = ParseInt(f[2], token, index);
                    obj.RewardId = f[3];
                    obj.CardId = f[4];
                    obj.CardLevel = ParseInt(f[5], token, index);
                    if (!balance.Shafts.TryGetValue(obj.TypeId, out var shaft)) { Issue(index, "неизвестная шахта " + obj.TypeId); return null; }
                    obj.Width = shaft.WidthCells; obj.Depth = shaft.DepthCells;
                    if (!balance.Gacha.ContainsKey(obj.RewardId)) Issue(index, "неизвестная награда " + obj.RewardId);
                    if (!balance.Cards.ContainsKey(obj.CardId)) Issue(index, "неизвестная карточка " + obj.CardId);
                    CheckHp(balance, obj, index);
                    return obj;
                case "x":
                    if (!CheckFields(f, 2, token, index)) return null;
                    obj.Kind = ObjectKind.Obstruction;
                    obj.TypeId = f[1];
                    if (!balance.Obstructions.TryGetValue(obj.TypeId, out var ob)) { Issue(index, "неизвестное препятствие " + obj.TypeId); return null; }
                    obj.Width = ob.WidthCells; obj.Depth = ob.DepthCells;
                    return obj;
                case "p":
                    if (!CheckFields(f, 4, token, index)) return null;
                    obj.Kind = ObjectKind.Gate;
                    obj.TypeId = f[1];
                    obj.Level = ParseInt(f[2], token, index);
                    obj.RewardId = f[3];
                    obj.Width = GateWidth; obj.Depth = GateDepth;
                    if (!balance.Gacha.ContainsKey(obj.RewardId)) Issue(index, "неизвестная награда " + obj.RewardId);
                    CheckHp(balance, obj, index);
                    return obj;
                case "e":
                    if (!CheckFields(f, 4, token, index)) return null;
                    obj.Kind = ObjectKind.Exit;
                    obj.TypeId = f[1];
                    obj.Level = ParseInt(f[2], token, index);
                    obj.RewardId = f[3];
                    obj.Width = ExitWidth; obj.Depth = ExitDepth;
                    if (!balance.Gacha.ContainsKey(obj.RewardId)) Issue(index, "неизвестная награда " + obj.RewardId);
                    CheckHp(balance, obj, index);
                    return obj;
                case "c":
                    if (!CheckFields(f, 3, token, index)) return null;
                    obj.Kind = ObjectKind.Cart;
                    obj.TypeId = f[1];
                    obj.Level = ParseInt(f[2], token, index);
                    // Строка токена — row_c; footprint 2×3 по общему правилу от опоры строкой ниже: row_c − 1 … row_c + 1.
                    RowC = obj.Row;
                    obj.Row += CartDepth - 2;
                    obj.Width = CartWidth; obj.Depth = CartDepth;
                    if (obj.TypeId != BalanceRepository.CartId) Issue(index, "неизвестная кузница " + obj.TypeId);
                    return obj;
                default:
                    Issue(index, "неизвестный токен '" + token + "'");
                    return null;
            }
        }

        bool CheckFields(string[] f, int expected, string token, int index)
        {
            if (f.Length == expected) return true;
            Issue(index, "токен '" + token + "': " + f.Length + " полей, ожидалось " + expected);
            return false;
        }

        int ParseInt(string s, string token, int index)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return v;
            Issue(index, "токен '" + token + "': не число '" + s + "'");
            return 0;
        }

        void CheckHp(BalanceRepository balance, ZoneObject obj, int index)
        {
            if (!balance.Root.MiningTargetHealths.TryGetValue(obj.TypeId, out var table))
                Issue(index, "нет HP-таблицы для " + obj.TypeId);
            else if (obj.Level < 1 || obj.Level > table.Length)
                Issue(index, "уровень " + obj.Level + " вне HP-таблицы " + obj.TypeId + " (1.." + table.Length + ")");
        }
    }
}
