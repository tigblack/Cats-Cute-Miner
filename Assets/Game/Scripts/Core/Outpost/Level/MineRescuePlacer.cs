using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Клетка спасения в шахте первой сессии (Docs/Rework/FIRST_SESSION.md, progression.json → Rescue): шаг после
    /// <see cref="MineDressingPlacer"/> — его ГСЧ и остальные шахты не меняются. Клетка с персонажем — постоянное препятствие на
    /// самой дальней от базы клетке пола (последний участок, на пути к выходу), чьё перекрытие ничего не отрезает; замок — куча
    /// мусора яруса <see cref="RescueRules.LockTier"/> на её подходе со стороны базы (по нему бьют котики, а не по персонажу).
    /// Обстановка на этих двух клетках снимается (освободившиеся клетки кучи 2×1 заново заполняются рудой). Маршрут — цели на
    /// самом дешёвом пути от базы к рабочей клетке замка (пол — 1, клетка цели — 1 + <see cref="TargetStepCost"/>). Итоговая карта
    /// проходит <see cref="OutpostReachability"/>, иначе спасения в шахте нет (персонаж приходит в команду при завершении).
    /// Детерминировано: перебор клеток по возрастанию.
    /// </summary>
    public static class MineRescuePlacer
    {
        /// <summary>Цена прохода через клетку, занятую целью (её надо разрушить), сверх шага.</summary>
        public const int TargetStepCost = 6;

        /// <summary>Сколько лучших мест проверяется целиком (валидатор), пока одно не пройдёт.</summary>
        const int MaxAttempts = 12;

        /// <summary>Снимок обстановки с клеткой и замком; та же копия, если в шахте спасения нет или место не найдено.</summary>
        public static MineDressing Place(OutpostMap map, MineDressing dressing, MineDressingRules dressingRules, RescueRules rescue)
        {
            if (map == null || dressing == null || rescue == null || dressingRules == null || map.Number != rescue.Zone || map.Topology == null)
                return dressing;
            if (dressing.Rescue != null) return dressing;
            if (dressingRules.Tiers == null || !dressingRules.Tiers.TryGetValue(rescue.LockTier, out var tier) || tier == null) return dressing;
            OutpostMap dressed;
            try
            {
                dressed = MineDressingAdapter.Apply(map, dressing);
            }
            catch (ArgumentException)
            {
                return dressing;
            }
            var candidates = Candidates(map, dressed);
            for (int k = 0; k < candidates.Count && k < MaxAttempts; k++)
            {
                var result = Build(map, dressing, tier, rescue, candidates[k].Cage, candidates[k].Approach);
                if (result != null) return result;
            }
            return dressing;
        }

        static MineDressing Build(OutpostMap map, MineDressing dressing, DressingTier tier, RescueRules rescue, int cage, int approach)
        {
            var result = OutpostJson.DeepCopy(dressing);
            if(rescue.Character==ProgressionConfig.BlacksmithCharacter)
            {
                if(!MakeRoomWithoutRemovingTargets(result,map,new[]{cage,approach},cage))return null;
            }
            else RemoveAt(result, map, new[] { cage, approach }, cage);
            int nextId = -1;
            foreach (var t in map.Targets) nextId = Math.Max(nextId, t.Id);
            foreach (var p in result.Pieces) nextId = Math.Max(nextId, p.TargetId);
            int lockId = nextId + 1;
            result.Props.Add(new DressingProp
            {
                Family = rescue.CageFamily, Variant = 0, Column = map.Col(cage), Row = map.Row(cage), W = 1, H = 1, PipeMount = false,
            });
            int cageProp = result.Props.Count - 1;
            result.Pieces.Add(new DressingPiece
            {
                TargetId = lockId, Family = rescue.LockFamily, Variant = 0, Tier = rescue.LockTier,
                HpFactor = tier.HpFactor, ValueFactor = tier.ValueFactor,
                Column = map.Col(approach), Row = map.Row(approach), W = 1, H = 1, Branch = 0,
            });
            result.Pieces.Sort((a, b) => a.TargetId.CompareTo(b.TargetId));
            result.Rescue = new DressingRescue
            {
                Character = rescue.Character, CageProp = cageProp, LockTargetId = lockId, RouteFocus = rescue.RouteFocus,
            };
            OutpostMap final;
            try
            {
                final = MineDressingAdapter.Apply(map, result);
            }
            catch (ArgumentException)
            {
                return null;
            }
            if (!OutpostReachability.Check(final).Passed || !HasEscape(final, approach, cage)) return null;
            if (rescue.ClearRouteOre)
            {
                // Руда на самом дешёвом пути снимается: проход остаётся, на нём — завалы и цели источника.
                var ore = new HashSet<int>();
                foreach (int id in Route(final, lockId))
                {
                    var t = final.Target(id);
                    if (t?.Piece != null && t.Piece.IsOre) ore.Add(id);
                }
                if (ore.Count > 0)
                {
                    result.Pieces.RemoveAll(p => ore.Contains(p.TargetId));
                    try
                    {
                        final = MineDressingAdapter.Apply(map, result);
                    }
                    catch (ArgumentException)
                    {
                        return null;
                    }
                    if (!OutpostReachability.Check(final).Passed) return null;
                }
            }
            result.Rescue.RouteTargetIds = Route(final, lockId);
            return result;
        }

        /// <summary>After normal mining removes targets, the adjacent lock cell has a permanent floor route to base.
        /// The character crosses only cage → lock → that route; neither cage nor door supports pipes.</summary>
        static bool HasEscape(OutpostMap map, int approach, int cage)
        {
            var pass = new bool[map.CellCount];
            for (int c = 0; c < pass.Length; c++) pass[c] = map.IsPlayable(c) && c != cage;
            foreach (var target in map.Targets)
                if (target.IsFactory)
                    foreach (int cell in target.Cells) pass[cell] = false;
            return approach >= 0 && pass[approach] && Bfs(map, map.IntakeCell, pass)[approach] >= 0;
        }

        /// <summary>
        /// Места клетки персонажа по убыванию пригодности: пол итоговой карты (цели сняты, заводы и препятствия стоят), не цель
        /// источника, не выход и не ворота; перекрытие клетки не отрезает от базы ни одной клетки пола. Наибольшее расстояние от
        /// базы, затем тупик (меньше соседей пола), затем меньший номер клетки; подход — сосед пола, ближайший к базе.
        /// </summary>
        static List<(int Cage, int Approach)> Candidates(OutpostMap map, OutpostMap dressed)
        {
            int n = dressed.CellCount;
            var pass = new bool[n];
            for (int c = 0; c < n; c++) pass[c] = dressed.IsPlayable(c);
            foreach (var t in dressed.Targets)
                if (t.IsFactory)
                    foreach (int c in t.Cells) pass[c] = false;
            var dist = Bfs(dressed, dressed.IntakeCell, pass);
            int reachable = 0;
            for (int c = 0; c < n; c++)
                if (pass[c] && dist[c] >= 0) reachable++;
            var scored = new List<(int Dist, int Neighbours, int Cage, int Approach)>();
            for (int c = 0; c < dressed.Depth * dressed.Width; c++)
            {
                if (!Free(map, dressed, pass, c) || dist[c] < 0) continue;
                int neighbours = 0, near = -1;
                foreach (int nb in Neighbours(dressed, c))
                {
                    if (!pass[nb]) continue;
                    neighbours++;
                    if (!Free(map, dressed, pass, nb) || dist[nb] < 0) continue;
                    if (near < 0 || dist[nb] < dist[near] || (dist[nb] == dist[near] && nb < near)) near = nb;
                }
                if (near >= 0) scored.Add((dist[c], neighbours, c, near));
            }
            scored.Sort((a, b) => a.Dist != b.Dist ? b.Dist.CompareTo(a.Dist) : a.Neighbours != b.Neighbours ? a.Neighbours.CompareTo(b.Neighbours) : a.Cage.CompareTo(b.Cage));
            var result = new List<(int Cage, int Approach)>();
            foreach (var s in scored)
            {
                if (result.Count >= MaxAttempts) break;
                // Перекрытие клетки персонажа не должно отрезать ни одной клетки пола.
                pass[s.Cage] = false;
                var after = Bfs(dressed, dressed.IntakeCell, pass);
                pass[s.Cage] = true;
                int still = 0;
                for (int k = 0; k < n; k++)
                    if (pass[k] && k != s.Cage && after[k] >= 0) still++;
                if (still == reachable - 1) result.Add((s.Cage, s.Approach));
            }
            return result;
        }

        /// <summary>Клетка шахты, которую можно занять: пол итоговой карты, не цель источника, не выход, не ворота, не лагерь.</summary>
        static bool Free(OutpostMap map, OutpostMap dressed, bool[] pass, int c) =>
            c >= 0 && c < dressed.Depth * dressed.Width && pass[c] && map.InitialOccupant[c] < 0 && c != dressed.ExitCell &&
            dressed.CellGateOrder[c] < 0;

        /// <summary>
        /// Снять обстановку на клетках персонажа и подхода: кучи и руду, которые их задевают (оставшиеся клетки кучи 2×1 — снова руда
        /// по образцу руды шахты), детали стен над клеткой персонажа (стена над полом, а пол стал препятствием).
        /// </summary>
        static void RemoveAt(MineDressing d, OutpostMap map, int[] cells, int cage)
        {
            var taken = new HashSet<int>(cells);
            DressingPiece ore = null;
            foreach (var p in d.Pieces)
                if (p.IsOre && p.W == 1 && p.H == 1) { ore = p; break; }
            var refill = new List<int>();
            for (int i = d.Pieces.Count - 1; i >= 0; i--)
            {
                var p = d.Pieces[i];
                var fp = MineDressingAdapter.Footprint(map, p.Column, p.Row, p.W, p.H, "rescue");
                bool hit = false;
                foreach (int c in fp)
                    if (taken.Contains(c)) hit = true;
                if (!hit) continue;
                d.Pieces.RemoveAt(i);
                foreach (int c in fp)
                    if (!taken.Contains(c)) refill.Add(c);
            }
            for (int i = d.Props.Count - 1; i >= 0; i--)
            {
                var p = d.Props[i];
                if (p.Wall ? map.Cell(p.Column, p.Row) + map.Width == cage : Overlaps(map, p, taken)) d.Props.RemoveAt(i);
            }
            if (ore == null || refill.Count == 0) return;
            refill.Sort();
            int nextId = -1;
            foreach (var t in map.Targets) nextId = Math.Max(nextId, t.Id);
            foreach (var p in d.Pieces) nextId = Math.Max(nextId, p.TargetId);
            foreach (int c in refill)
                d.Pieces.Add(new DressingPiece
                {
                    TargetId = ++nextId, Family = ore.Family, Variant = ore.Variant, Tier = ore.Tier, HpFactor = ore.HpFactor, ValueFactor = ore.ValueFactor,
                    Column = map.Col(c), Row = map.Row(c), W = 1, H = 1, Branch = ore.Branch, RockType = ore.RockType, RockVisual = ore.RockVisual,
                    RockLevel = ore.RockLevel,
                });
        }

        /// <summary>Bruno's new cage relocates overlapping ordinary pieces intact: same identity, footprint area and HP.</summary>
        static bool MakeRoomWithoutRemovingTargets(MineDressing d,OutpostMap map,int[] cells,int cage)
        {
            var originals=new List<DressingPiece>(d.Pieces);
            var taken=new HashSet<int>(cells);
            var moved=new List<DressingPiece>();
            foreach(var piece in originals)
            {
                foreach(int cell in MineDressingAdapter.Footprint(map,piece.Column,piece.Row,piece.W,piece.H,"rescue"))
                    if(taken.Contains(cell)){moved.Add(piece);break;}
            }
            foreach(var piece in moved)d.Pieces.Remove(piece);
            for(int i=d.Props.Count-1;i>=0;i--)
            {
                var p=d.Props[i];
                if(p.Wall?map.Cell(p.Column,p.Row)+map.Width==cage:Overlaps(map,p,taken))d.Props.RemoveAt(i);
            }
            foreach(var piece in moved)
            {
                var used=new bool[map.CellCount];
                foreach(int c in cells)used[c]=true;
                foreach(var p in d.Pieces)foreach(int c in MineDressingAdapter.Footprint(map,p.Column,p.Row,p.W,p.H,"rescue move"))used[c]=true;
                var propCells=new HashSet<int>();
                foreach(var p in d.Props)if(!p.Wall)foreach(int c in MineDressingAdapter.Footprint(map,p.Column,p.Row,p.W,p.H,"rescue prop"))propCells.Add(c);
                int best=-1,bestDistance=int.MaxValue,sector=map.SectorAt(map.Cell(piece.Column,piece.Row));
                for(int row=piece.H-1;row<map.Depth;row++)
                for(int col=0;col+piece.W<=map.Width;col++)
                {
                    bool free=true;int decorations=0;
                    for(int y=0;y<piece.H&&free;y++)for(int x=0;x<piece.W;x++)
                    {
                        int c=map.Cell(col+x,row-y);
                        if(used[c]||!map.IsPlayable(c)||map.InitialOccupant[c]>=0||map.CellGateOrder[c]>=0||c==map.ExitCell||map.SectorAt(c)!=sector){free=false;break;}
                        if(propCells.Contains(c))decorations++;
                    }
                    // Prefer empty floor; a removable decoration is preferable to deleting ordinary ore.
                    int distance=decorations*map.CellCount+Math.Abs(col-piece.Column)+Math.Abs(row-piece.Row);
                    if(free&&distance<bestDistance){best=map.Cell(col,row);bestDistance=distance;}
                }
                if(best<0)return false;
                var replacement=new HashSet<int>(MineDressingAdapter.Footprint(map,map.Col(best),map.Row(best),piece.W,piece.H,"rescue replacement"));
                d.Props.RemoveAll(p=>!p.Wall&&Overlaps(map,p,replacement));
                piece.Column=map.Col(best);piece.Row=map.Row(best);d.Pieces.Add(piece);
            }
            return true;
        }

        static bool Overlaps(OutpostMap map, DressingProp p, HashSet<int> cells)
        {
            foreach (int c in MineDressingAdapter.Footprint(map, p.Column, p.Row, p.W, p.H, "rescue prop"))
                if (cells.Contains(c)) return true;
            return false;
        }

        /// <summary>
        /// Цели на самом дешёвом пути от базы до рабочей клетки замка (по порядку от базы, без замка и заводов): клетка пола — 1,
        /// клетка живой цели — 1 + <see cref="TargetStepCost"/>; закрытые ворота — тоже цели пути.
        /// </summary>
        public static List<int> Route(OutpostMap map, int lockId)
        {
            var result = new List<int>();
            var lockTarget = map.Target(lockId);
            if (lockTarget == null) return result;
            int n = map.CellCount;
            var cost = new int[n];
            var prev = new int[n];
            for (int c = 0; c < n; c++)
            {
                cost[c] = int.MaxValue;
                prev[c] = -1;
            }
            var goal = new HashSet<int>(lockTarget.WorkCells);
            var open = new SortedSet<(int Cost, int Cell)>();
            cost[map.IntakeCell] = 0;
            open.Add((0, map.IntakeCell));
            int end = -1;
            while (open.Count > 0)
            {
                var top = open.Min;
                open.Remove(top);
                if (top.Cost != cost[top.Cell]) continue;
                if (goal.Contains(top.Cell))
                {
                    end = top.Cell;
                    break;
                }
                foreach (int nb in Neighbours(map, top.Cell))
                {
                    int step = StepCost(map, nb, lockId);
                    if (step < 0) continue;
                    int next = top.Cost + step;
                    if (next >= cost[nb]) continue;
                    if (cost[nb] != int.MaxValue) open.Remove((cost[nb], nb));
                    cost[nb] = next;
                    prev[nb] = top.Cell;
                    open.Add((next, nb));
                }
            }
            if (end < 0) return result;
            var path = new List<int>();
            for (int c = end; c >= 0; c = prev[c]) path.Add(c);
            path.Reverse();
            var seen = new HashSet<int>();
            foreach (int c in path)
            {
                int id = map.InitialOccupant[c];
                if (id < 0 || id == lockId || !seen.Add(id)) continue;
                var t = map.Target(id);
                if (t != null && !t.IsFactory) result.Add(id);
            }
            return result;
        }

        /// <summary>Шаг в клетку: −1 — непроходима навсегда (порода, препятствие, завод, сам замок).</summary>
        static int StepCost(OutpostMap map, int cell, int lockId)
        {
            if (!map.IsPlayable(cell)) return -1;
            int id = map.InitialOccupant[cell];
            if (id >= 0)
            {
                if (id == lockId) return -1;
                var t = map.Target(id);
                if (t == null || t.IsFactory) return -1;
                return 1 + TargetStepCost;
            }
            return 1;
        }

        static IEnumerable<int> Neighbours(OutpostMap map, int cell)
        {
            int col = map.Col(cell), row = map.Row(cell);
            if (row > 0) yield return cell - map.Width;
            if (col > 0) yield return cell - 1;
            if (col + 1 < map.Width) yield return cell + 1;
            if (row + 1 < map.Rows) yield return cell + map.Width;
        }

        static int[] Bfs(OutpostMap map, int source, bool[] pass)
        {
            int n = map.CellCount;
            var dist = new int[n];
            for (int i = 0; i < n; i++) dist[i] = -1;
            var queue = new int[n];
            int head = 0, tail = 0;
            dist[source] = 0;
            queue[tail++] = source;
            while (head < tail)
            {
                int c = queue[head++];
                foreach (int nb in Neighbours(map, c))
                {
                    if (!pass[nb] || dist[nb] >= 0) continue;
                    dist[nb] = dist[c] + 1;
                    queue[tail++] = nb;
                }
            }
            return dist;
        }
    }
}
