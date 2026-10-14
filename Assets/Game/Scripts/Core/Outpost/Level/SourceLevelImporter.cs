using System;
using System.Collections.Generic;
using System.Globalization;
using CatMiners.Core.Data;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Разбор Zones[].Grid исходного json-data.json (ТЗ 8.1): токены через запятую, поля через двоеточие,
    /// footprint r/s/x — якорь в нижней левой клетке: occupied = (col + dx, row − dz).
    /// Репозиторий уже проверил SHA-256 источника. Override зоны (ТЗ 8.5) добавляет цели r/s/x на якоря "." до разбора:
    /// их footprint проверяется теми же правилами и не может пересекать исходные объекты, выход e и строки ворот p.
    /// </summary>
    public static class SourceLevelImporter
    {
        public const string CheckpointId = "checkpoint";
        public const string ExitId = "exit";
        public const string CartId = "spawningcart";

        public static SourceZone Import(BalanceRepository repo, int zoneNumber, ZoneOverride zoneOverride = null)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            var dto = repo.Zone(zoneNumber);
            string zoneId = dto.Id;
            int width = dto.WidthCells, depth = dto.DepthCells;
            if (width <= 0 || depth <= 0) throw new OutpostDataException(zoneId, -1, "неверный размер поля " + width + "×" + depth);
            if (string.IsNullOrEmpty(dto.Grid)) throw new OutpostDataException(zoneId, -1, "пустая Grid");
            var raw = dto.Grid.Split(',');
            if (raw.Length != width * depth)
                throw new OutpostDataException(zoneId, -1, "число токенов " + raw.Length + " ≠ " + width + "×" + depth);
            var added = ApplyOverride(zoneId, width, depth, raw, zoneOverride);

            var zone = new SourceZone
            {
                ZoneId = zoneId,
                Number = zoneNumber,
                Width = width,
                Depth = depth,
                DecoSeed = dto.DecoSeed,
                SourceSha256 = repo.Sha256,
                Tokens = new SourceToken[raw.Length],
                Override = added != null ? zoneOverride : null,
            };
            var occupied = new int[raw.Length];
            for (int i = 0; i < occupied.Length; i++) occupied[i] = -1;
            int cartCount = 0, exitCount = 0;

            for (int index = 0; index < raw.Length; index++)
            {
                string token = raw[index];
                if (token == ".") continue;
                var f = token.Split(':');
                if (f.Length == 0 || f[0].Length != 1) throw new OutpostDataException(zoneId, index, "неверный токен \"" + token + "\"");
                char kind = f[0][0];
                int expected = FieldCount(kind);
                if (expected < 0) throw new OutpostDataException(zoneId, index, "неизвестный вид токена \"" + token + "\"");
                if (f.Length != expected)
                    throw new OutpostDataException(zoneId, index, "токен \"" + token + "\": полей " + f.Length + ", ожидалось " + expected);

                var t = new SourceToken
                {
                    Index = index,
                    Kind = kind,
                    TypeId = f[1],
                    Column = index % width,
                    Row = index / width,
                    Cells = new int[0],
                    Added = added != null && added[index],
                };
                if (kind != 'x') t.Level = ParseInt(zoneId, index, f[2], "уровень");
                if (kind == 's')
                {
                    t.RewardId = f[3];
                    t.CardId = f[4];
                    t.CardLevel = ParseInt(zoneId, index, f[5], "уровень карты");
                }
                else if (kind == 'p' || kind == 'e')
                {
                    t.RewardId = f[3];
                }

                int w = 0, d = 0;
                switch (kind)
                {
                    case 'r':
                        if (!repo.Rocks.TryGetValue(t.TypeId, out var rock)) throw new OutpostDataException(zoneId, index, "нет Rocks." + t.TypeId);
                        w = rock.WidthCells;
                        d = rock.DepthCells;
                        t.MimicId = string.IsNullOrEmpty(rock.MimicId) ? null : rock.MimicId;
                        break;
                    case 's':
                        if (!repo.Shafts.TryGetValue(t.TypeId, out var shaft)) throw new OutpostDataException(zoneId, index, "нет MineShafts." + t.TypeId);
                        w = shaft.WidthCells;
                        d = shaft.DepthCells;
                        break;
                    case 'x':
                        if (!repo.Obstructions.TryGetValue(t.TypeId, out var obs)) throw new OutpostDataException(zoneId, index, "нет Obstructions." + t.TypeId);
                        w = obs.WidthCells;
                        d = obs.DepthCells;
                        break;
                    case 'p':
                        if (t.TypeId != CheckpointId) throw new OutpostDataException(zoneId, index, "ворота с типом " + t.TypeId);
                        break;
                    case 'e':
                        if (t.TypeId != ExitId) throw new OutpostDataException(zoneId, index, "выход с типом " + t.TypeId);
                        exitCount++;
                        break;
                    case 'c':
                        if (t.TypeId != CartId) throw new OutpostDataException(zoneId, index, "старт с типом " + t.TypeId);
                        cartCount++;
                        break;
                }

                if (w > 0 || d > 0)
                {
                    if (w <= 0 || d <= 0) throw new OutpostDataException(zoneId, index, "неверный размер " + t.TypeId);
                    var cells = new List<int>(w * d);
                    for (int dx = 0; dx < w; dx++)
                        for (int dz = 0; dz < d; dz++)
                        {
                            int col = t.Column + dx, row = t.Row - dz;
                            if (col < 0 || col >= width || row < 0 || row >= depth)
                                throw new OutpostDataException(zoneId, index, "footprint вне поля (" + col + ", " + row + ")");
                            int cell = row * width + col;
                            if (occupied[cell] >= 0)
                                throw new OutpostDataException(zoneId, index, "footprint пересекается с " + occupied[cell] + " в (" + col + ", " + row + ")" +
                                    (added != null && (added[index] || added[occupied[cell]]) ? " (override " + zoneOverride.Name + ")" : ""));
                            occupied[cell] = index;
                            cells.Add(cell);
                        }
                    cells.Sort();
                    t.Cells = cells.ToArray();
                }
                zone.Tokens[index] = t;
            }
            if (cartCount != 1 || exitCount != 1)
                throw new OutpostDataException(zoneId, -1, "ожидался ровно один c и один e, найдено " + cartCount + " и " + exitCount);
            if (added != null) CheckAddedFootprints(zone, zoneOverride.Name);
            return zone;
        }

        /// <summary>
        /// Подстановка добавленных токенов override на якоря "."; null — override нет или он без геометрии.
        /// Вид, поля и footprint проверяет общий разбор ниже.
        /// </summary>
        static bool[] ApplyOverride(string zoneId, int width, int depth, string[] raw, ZoneOverride ov)
        {
            if (ov == null || !ov.HasGeometry()) return null;
            var added = new bool[raw.Length];
            foreach (var a in ov.AddTokens)
            {
                string where = "override " + ov.Name;
                if (a == null || string.IsNullOrEmpty(a.Token)) throw new OutpostDataException(zoneId, -1, where + ": пустой токен");
                if (a.Column < 0 || a.Column >= width || a.Row < 0 || a.Row >= depth)
                    throw new OutpostDataException(zoneId, -1, where + ": якорь вне поля (" + a.Column + ", " + a.Row + ")");
                int index = a.Row * width + a.Column;
                if (added[index]) throw new OutpostDataException(zoneId, index, where + ": повтор якоря");
                if (raw[index] != ".") throw new OutpostDataException(zoneId, index, where + ": якорь занят исходным токеном \"" + raw[index] + "\"");
                char kind = a.Token[0];
                if (kind != 'r' && kind != 's' && kind != 'x')
                    throw new OutpostDataException(zoneId, index, where + ": допускаются только цели r/s/x, получено \"" + a.Token + "\"");
                raw[index] = a.Token;
                added[index] = true;
            }
            return added;
        }

        /// <summary>Footprint добавленных целей не закрывает выход e и не лежит в строке ворот p (их правила задаёт адаптер).</summary>
        static void CheckAddedFootprints(SourceZone zone, string name)
        {
            var gateRows = new HashSet<int>();
            int exit = -1;
            foreach (var t in zone.Tokens)
            {
                if (t == null) continue;
                if (t.Kind == 'p') gateRows.Add(t.Row);
                else if (t.Kind == 'e') exit = t.Index;
            }
            foreach (var t in zone.Tokens)
            {
                if (t == null || !t.Added) continue;
                foreach (int cell in t.Cells)
                {
                    if (cell == exit) throw new OutpostDataException(zone.ZoneId, t.Index, "override " + name + ": footprint закрывает выход");
                    if (gateRows.Contains(cell / zone.Width))
                        throw new OutpostDataException(zone.ZoneId, t.Index, "override " + name + ": footprint в строке ворот " + cell / zone.Width);
                }
            }
        }

        /// <summary>Число полей токена: r=3, s=6, x=2, p/e=4, c=3; −1 — неизвестный вид.</summary>
        public static int FieldCount(char kind)
        {
            switch (kind)
            {
                case 'r': return 3;
                case 's': return 6;
                case 'x': return 2;
                case 'p':
                case 'e': return 4;
                case 'c': return 3;
                default: return -1;
            }
        }

        static int ParseInt(string zoneId, int index, string text, string what)
        {
            if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v))
                throw new OutpostDataException(zoneId, index, what + ": не целое \"" + text + "\"");
            return v;
        }
    }

    /// <summary>Исходная зона: размеры и токены по линейному индексу (null для ".").</summary>
    public sealed class SourceZone
    {
        public string ZoneId;
        public int Number;
        public int Width;
        public int Depth;
        public int DecoSeed;
        public string SourceSha256;
        public SourceToken[] Tokens;
        /// <summary>Применённый override с геометрией или null (исходная карта).</summary>
        public ZoneOverride Override;
    }

    /// <summary>Токен исходной сетки с восстановленным footprint (линейные индексы исходной сетки).</summary>
    public sealed class SourceToken
    {
        public int Index;
        public char Kind;
        public string TypeId;
        public int Level;
        public string RewardId;
        public string CardId;
        public int CardLevel;
        public int Column;
        public int Row;
        public int[] Cells;
        public string MimicId;
        /// <summary>Цель добавлена override зоны, а не исходной Grid.</summary>
        public bool Added;
    }
}
