using System;
using System.Collections.Generic;
using System.Globalization;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;

namespace CatMiners.Core.Sim
{
    /// <summary>
    /// Симуляция одной игры: состояние + правила рабочего профиля v1. UI и тесты вызывают одни и те же команды.
    /// Состояние экономики меняется только командами игрока и <see cref="Advance"/> (ТЗ 15.2).
    /// </summary>
    public sealed partial class GameSession
    {
        public const long TicksPerSecond = 1_000_000L;
        public const long MaxOfflineTicks = 8L * 3600L * TicksPerSecond;
        public const int MaxDynamite = 4;
        public const double MinFreeCatIntervalSeconds = 60.0;

        public BalanceRepository Balance { get; }
        public GameState State { get; private set; }
        public ZoneLayout Layout { get; private set; }
        public PlayerProgressState Player => State.Player;
        public MineProgressState Mine => State.Mine;
        public int Rank => Mine.Zone;

        /// <summary>События для UI; потребитель очищает список сам.</summary>
        public readonly List<SimEvent> Events = new List<SimEvent>();
        /// <summary>Офлайн-расчёт: доставки не идут, события ударов не публикуются.</summary>
        public bool OfflineMode { get; set; }

        /// <summary>
        /// Наём приносит бочку (<see cref="ContainerKind.Hire"/>) вместо котика; котик появляется при открытии бочки.
        /// Правило сессии, в сохранение не попадает. По умолчанию выключено (тесты, бот); игра (GameController) включает.
        /// </summary>
        public bool BarrelHires { get; set; }

        /// <summary>
        /// Валютные награды камней выпадают добычей на поле (<see cref="MineProgressState.Loot"/>) и начисляются при сборе
        /// (<see cref="CollectLoot"/>) или автосбором через <see cref="LootAutoCollectSeconds"/>. По умолчанию выключено.
        /// </summary>
        public bool LootPickups { get; set; }

        /// <summary>
        /// Котик, разбивший цель, сам берётся за следующую (ТЗ 7.3): за соседнюю цель, для которой его клетка рабочая, а если
        /// такой нет и камень освободил клетку перед ним — шагает туда и бьёт цель за ней. Офлайн новых назначений нет (12.3).
        /// Правило сессии, в сохранение не попадает. По умолчанию выключено (тесты, бот); игра (GameController) включает.
        /// </summary>
        public bool AutoMining { get; set; }

        /// <summary>Автосбор добычи через столько секунд игрового времени (в том числе офлайн).</summary>
        public const double LootAutoCollectSeconds = 30.0;
        public const long LootAutoCollectTicks = (long)(LootAutoCollectSeconds * TicksPerSecond);

        int[] m_CellCat;
        int[] m_CellContainer;
        readonly Dictionary<int, int> m_OrdinalByIndex = new Dictionary<int, int>();
        readonly Dictionary<string, GeneratorDef> m_GeneratorDefs = new Dictionary<string, GeneratorDef>();
        readonly List<int> m_Tmp = new List<int>(8);
        // Работники завершённой цели (AutoMining): буфер без выделений.
        readonly List<CatState> m_Workers = new List<CatState>(8);

        GameSession(BalanceRepository balance, GameState state)
        {
            Balance = balance;
            State = state;
        }

        public static GameSession NewGame(BalanceRepository balance, ulong seed, string slot = SaveSlots.Main, long nowUtcMs = 0, int startZone = 1)
        {
            var state = new GameState
            {
                BalanceSha256 = balance.Sha256,
                Slot = slot,
                LastProcessedUtcMs = nowUtcMs,
            };
            state.Player.Rng = RngStreams.Create(seed);
            var s = new GameSession(balance, state);
            s.EnterZone(startZone);
            return s;
        }

        public static GameSession FromState(BalanceRepository balance, GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Player.Rng == null) state.Player.Rng = RngStreams.Create(0x5EEDUL);
            // Сохранение без потока разброса таймеров (ТЗ 15.2): детерминированный поток 4 от фиксированного зерна.
            if (state.Player.Rng.Timers == null) state.Player.Rng.Timers = RngStreams.Create(0x5EEDUL).Timers;
            if (state.Player.Settings == null) state.Player.Settings = new PlayerSettingsState();
            // Сохранение без добычи (до её появления) — пустой список; ID добычи не повторяются.
            if (state.Mine.Loot == null) state.Mine.Loot = new List<LootState>();
            foreach (var l in state.Mine.Loot)
                if (l.Id >= state.Mine.NextLootId) state.Mine.NextLootId = l.Id + 1;
            var s = new GameSession(balance, state);
            s.Layout = ZoneLayout.Build(balance, state.Mine.Zone);
            if (!s.Layout.IsValid) throw new InvalidOperationException("Зона " + state.Mine.Zone + " содержит ошибки разметки");
            if (state.Mine.Objects.Count != s.Layout.Objects.Count)
                throw new InvalidOperationException("Сохранение не соответствует карте зоны " + state.Mine.Zone);
            for (int i = 0; i < s.Layout.Objects.Count; i++)
                if (state.Mine.Objects[i].Index != s.Layout.Objects[i].Index)
                    throw new InvalidOperationException("Сохранение не соответствует карте зоны " + state.Mine.Zone + " (объект " + i + ")");
            s.RebuildCaches();
            s.RelocateFromObjectCells();
            s.ValidateLoadedAssignments();
            // Переходных состояний при загрузке не бывает (ТЗ 7.5).
            s.ClearDrag();
            return s;
        }

        /// <summary>
        /// Старые сохранения: котик или контейнер в клетке, которую теперь занимает объект (пушка у кузницы, ТЗ 7.2),
        /// переносится в клетку появления (6.6) и становится свободным; места нет — остаётся на месте.
        /// </summary>
        void RelocateFromObjectCells()
        {
            foreach (var cat in Mine.Cats)
            {
                if (ObjectAt(cat.Cell) < 0) continue;
                int cell = FindSpawnCell();
                if (cell < 0) continue;
                if (m_CellCat[cat.Cell] == cat.Id) m_CellCat[cat.Cell] = 0;
                cat.Cell = cell;
                cat.TargetIndex = -1;
                cat.PhaseTicks = 0;
                m_CellCat[cell] = cat.Id;
            }
            foreach (var box in Mine.Containers)
            {
                if (ObjectAt(box.Cell) < 0) continue;
                int cell = FindSpawnCell();
                if (cell < 0) continue;
                if (m_CellContainer[box.Cell] == box.Id) m_CellContainer[box.Cell] = 0;
                box.Cell = cell;
                m_CellContainer[cell] = box.Id;
            }
        }

        /// <summary>
        /// ТЗ 7.3: при загрузке пара TargetKey/WorkCell сохраняется, если цель допустима, а WorkCell — её рабочая клетка.
        /// Иначе — Idle. Свободная рабочая клетка не требуется: достаточно допустимой цели.
        /// </summary>
        void ValidateLoadedAssignments()
        {
            foreach (var cat in Mine.Cats)
            {
                if (cat.TargetIndex < 0) continue;
                int ord = OrdinalOf(cat.TargetIndex);
                if (ord >= 0 && IsTargetAvailable(ord) && IsWorkCellOf(ord, cat.Cell)) continue;
                cat.TargetIndex = -1;
                cat.PhaseTicks = 0;
            }
        }

        // ───────────────────────────── Зона ─────────────────────────────

        void EnterZone(int zone)
        {
            Layout = ZoneLayout.Build(Balance, zone);
            if (!Layout.IsValid) throw new InvalidOperationException("Зона " + zone + " содержит ошибки разметки: " + Layout.Issues[0]);
            var mine = new MineProgressState
            {
                Zone = zone,
                Gold = GameNumber.FromDouble(Balance.Props.StartingCoreCurrency),
            };
            foreach (var obj in Layout.Objects)
            {
                var os = new ObjectState { Index = obj.Index, Status = ObjectStatus.Intact };
                if (obj.IsTarget) os.Hp = Balance.TargetHp(obj.TypeId, obj.Level);
                if (obj.Kind == ObjectKind.Cart)
                {
                    os.Status = ObjectStatus.Producing;
                    os.Gen = new GeneratorState { Level = Math.Max(1, obj.Level) };
                }
                mine.Objects.Add(os);
            }
            State.Mine = mine;
            Player.CurrentZone = zone;
            ClearDrag();
            RebuildCaches();
            mine.FreeCatTimerTicks = SecondsToTicks(FreeCatIntervalSeconds());
            UpdateUnlocks();
            Emit(new SimEvent { Type = SimEventType.ZoneEntered, ObjectIndex = -1 });
        }

        void RebuildCaches()
        {
            m_OrdinalByIndex.Clear();
            foreach (var o in Layout.Objects) m_OrdinalByIndex[o.Index] = o.Ordinal;
            m_CellCat = new int[Layout.CellCount];
            m_CellContainer = new int[Layout.CellCount];
            foreach (var c in Mine.Cats) m_CellCat[c.Cell] = c.Id;
            foreach (var c in Mine.Containers) m_CellContainer[c.Cell] = c.Id;
        }

        public static long SecondsToTicks(double seconds) => (long)Math.Round(seconds * TicksPerSecond);
        public static double TicksToSeconds(long ticks) => ticks / (double)TicksPerSecond;

        long NextOp() => State.NextOpId++;

        void Emit(SimEvent e)
        {
            e.Zone = Mine.Zone;
            Events.Add(e);
        }

        public int OrdinalOf(int objectIndex) => m_OrdinalByIndex.TryGetValue(objectIndex, out int o) ? o : -1;
        public ObjectState ObjectStateOf(int ordinal) => Mine.Objects[ordinal];

        // ───────────────────────────── Клетки ─────────────────────────────

        public bool IsSectorOpen(int sector) => sector >= 0 && sector <= Mine.GatesOpened;
        public bool IsCellOpen(int cell) => IsSectorOpen(Layout.CellSector[cell]);

        /// <summary>Ordinal живого объекта в клетке (разрушенные камни клетку освобождают) или −1.</summary>
        public int ObjectAt(int cell)
        {
            int ord = Layout.CellOwner[cell];
            if (ord < 0) return -1;
            if (Layout.Objects[ord].Kind == ObjectKind.Rock && Mine.Objects[ord].Status == ObjectStatus.Destroyed) return -1;
            return ord;
        }

        public CatState CatAt(int cell)
        {
            int id = m_CellCat[cell];
            return id == 0 ? null : FindCat(id);
        }

        public ContainerState ContainerAt(int cell)
        {
            int id = m_CellContainer[cell];
            if (id == 0) return null;
            foreach (var c in Mine.Containers) if (c.Id == id) return c;
            return null;
        }

        public CatState FindCat(int id)
        {
            foreach (var c in Mine.Cats) if (c.Id == id) return c;
            return null;
        }

        /// <summary>Добыча на поле по ID или null.</summary>
        public LootState FindLoot(int id)
        {
            foreach (var l in Mine.Loot) if (l.Id == id) return l;
            return null;
        }

        public bool IsCellFree(int cell) =>
            IsCellOpen(cell) && ObjectAt(cell) < 0 && m_CellCat[cell] == 0 && m_CellContainer[cell] == 0;

        /// <summary>Допустимая цель (ТЗ 7.3): камень, шахта Blocked, ворота/выход Closed; HP &gt; 0; открыта для работы.</summary>
        public bool IsTargetAvailable(int ord)
        {
            if (ord < 0) return false;
            var obj = Layout.Objects[ord];
            var st = Mine.Objects[ord];
            if (!obj.IsTarget || st.Status != ObjectStatus.Intact || !st.Hp.IsPositive) return false;
            int sector = obj.Kind == ObjectKind.Gate ? Layout.SectorOfRow(obj.Row + 1) : Layout.CellSector[obj.Cells[0]];
            return IsSectorOpen(sector);
        }

        /// <summary>Рабочие клетки цели (ТЗ 6.5): соседи footprint по стороне, в открытом секторе, без объектов и контейнеров.</summary>
        public void WorkCells(int ord, List<int> result)
        {
            result.Clear();
            var obj = Layout.Objects[ord];
            foreach (int cell in obj.Cells)
            {
                Layout.Neighbors4(cell, m_Tmp);
                foreach (int n in m_Tmp)
                {
                    if (Layout.CellOwner[n] == ord) continue;
                    if (!IsCellOpen(n) || ObjectAt(n) >= 0 || m_CellContainer[n] != 0) continue;
                    if (!result.Contains(n)) result.Add(n);
                }
            }
        }

        public bool IsWorkCellOf(int ord, int cell)
        {
            var list = new List<int>(8);
            WorkCells(ord, list);
            return list.Contains(cell);
        }

        /// <summary>Цель принимает котика: допустима и есть свободная рабочая клетка (своя клетка котика считается свободной).</summary>
        public bool AcceptsCat(int ord, int catId)
        {
            if (!IsTargetAvailable(ord)) return false;
            var list = new List<int>(8);
            WorkCells(ord, list);
            foreach (int c in list)
                if (m_CellCat[c] == 0 || m_CellCat[c] == catId) return true;
            return false;
        }

        /// <summary>
        /// Строки площадки появления SpawnPad (ТЗ 6.6, G-03): три строки над самыми верхними открытыми воротами,
        /// иначе стартовая полоса row_c − 3 … row_c + 1 (клетки кузницы и пушки заняты объектом и не выдаются).
        /// </summary>
        public void SpawnPadRows(out int topRow, out int bottomRow)
        {
            if (Mine.GatesOpened > 0)
            {
                var g = Layout.Gates[Mine.GatesOpened - 1];
                topRow = g.Row - 3;
                bottomRow = g.Row - 1;
            }
            else
            {
                int rc = Layout.RowC;
                topRow = rc - 3;
                bottomRow = Math.Min(Layout.Depth - 1, rc + 1);
            }
            topRow = Math.Max(0, topRow);
        }

        /// <summary>
        /// Зона мержа текущей площадки появления (ТЗ 6.6): два соседних ряда во всю ширину без объектов —
        /// на старте над кузницей, после открытия ворот — над самыми верхними открытыми воротами. false — пары нет (данные с ошибкой).
        /// </summary>
        public bool MergeRows(out int upperRow, out int lowerRow)
        {
            SpawnPadRows(out int topRow, out int bottomRow);
            return Layout.TryMergeRows(topRow, bottomRow, out upperRow, out lowerRow);
        }

        // Буферы поиска клетки появления: HUD спрашивает её при каждом обновлении кнопки найма (ТЗ 15.3 — без выделений).
        bool[] m_SpawnGateRow = Array.Empty<bool>();
        bool[] m_SpawnSeen = Array.Empty<bool>();
        readonly List<int> m_SpawnOrder = new List<int>(64);
        readonly Queue<int> m_SpawnQueue = new Queue<int>(64);

        /// <summary>
        /// Клетка появления (ТЗ 6.6, G-06): BFS (верх, лево, право, низ) от (3, нижний ряд зоны мержа) — на старте (3, row_c − 2),
        /// после ворот (3, row_p − 1); без зоны мержа — от (3, средняя строка SpawnPad). Обход идёт по всем клеткам открытых
        /// секторов, включая занятые; строки уже открытых ворот он проходит насквозь (по всем столбцам), поэтому доходит и до
        /// нижних открытых секторов; сами клетки строк ворот не выдаются. Закрытые сектора и строка следующих закрытых ворот
        /// обход останавливают. Результат — первая свободная клетка в порядке обхода: сначала в строках зоны мержа, затем
        /// в остальной SpawnPad, затем во всей открытой области; −1 — места нет.
        /// </summary>
        public int FindSpawnCell()
        {
            SpawnPadRows(out int topRow, out int bottomRow);
            bool merge = Layout.TryMergeRows(topRow, bottomRow, out int mergeTop, out int mergeBottom);
            int startRow = merge ? mergeBottom : (topRow + bottomRow) / 2;
            int start = Layout.CellIndex(Math.Min(3, Layout.Width - 1), startRow);

            if (m_SpawnGateRow.Length != Layout.Depth) m_SpawnGateRow = new bool[Layout.Depth];
            else Array.Clear(m_SpawnGateRow, 0, m_SpawnGateRow.Length);
            foreach (var g in Layout.Gates)
                if (g.GateOrder >= 0 && g.GateOrder < Mine.GatesOpened && g.Row >= 0 && g.Row < Layout.Depth) m_SpawnGateRow[g.Row] = true;

            var order = m_SpawnOrder;
            order.Clear();
            if (m_SpawnSeen.Length != Layout.CellCount) m_SpawnSeen = new bool[Layout.CellCount];
            else Array.Clear(m_SpawnSeen, 0, m_SpawnSeen.Length);
            if (IsCellOpen(start) || m_SpawnGateRow[Layout.Row(start)])
            {
                var queue = m_SpawnQueue;
                queue.Clear();
                queue.Enqueue(start);
                m_SpawnSeen[start] = true;
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    order.Add(cur);
                    Layout.Neighbors4(cur, m_Tmp);
                    foreach (int n in m_Tmp)
                    {
                        if (m_SpawnSeen[n] || !(IsCellOpen(n) || m_SpawnGateRow[Layout.Row(n)])) continue;
                        m_SpawnSeen[n] = true;
                        queue.Enqueue(n);
                    }
                }
            }
            if (merge)
            {
                foreach (int cell in order)
                {
                    int r = Layout.Row(cell);
                    if (r >= mergeTop && r <= mergeBottom && IsCellFree(cell)) return cell;
                }
            }
            foreach (int cell in order)
            {
                int r = Layout.Row(cell);
                if (r >= topRow && r <= bottomRow && IsCellFree(cell)) return cell;
            }
            foreach (int cell in order)
                if (IsCellFree(cell)) return cell;
            return -1;
        }

        // ───────────────────────────── Модификаторы (E-07, E-08) ─────────────────────────────

        public CardState CardStateOf(string cardId)
        {
            if (!Player.Cards.TryGetValue(cardId, out var cs))
            {
                cs = new CardState();
                Player.Cards[cardId] = cs;
            }
            return cs;
        }

        public int CardLevel(string cardId) => Player.Cards.TryGetValue(cardId, out var cs) ? cs.Level : 0;

        /// <summary>Применяемые в v1 коды карточек.</summary>
        public static bool IsAppliedCardCode(int code) => code == 9 || code == 10 || code == 7 || code == 1;

        /// <summary>Значение эффекта карты при её уровне; карты нет — null (эффекта нет).</summary>
        public GameNumber? CardEffectValue(CardDto c)
        {
            int level = CardLevel(c.Id);
            if (level < 1) return null;
            return CardEffectAtLevel(c, level);
        }

        /// <summary>Значение эффекта карты на уровне <paramref name="level"/> ≥ 1 (окно карты: «на следующем уровне»).</summary>
        public static GameNumber CardEffectAtLevel(CardDto c, int level) =>
            Formula.Evaluate(c.ModifierFormulaType, c.ModifierBase, c.ModifierMultiplier, c.ModifierGrowth, level, c.ModifierPower, c.ModifierRound);

        /// <summary>Значение бонуса ворот с кодом (за одни ворота).</summary>
        public GameNumber CheckpointValue(int code)
        {
            var cp = Balance.CheckPoint;
            var sum = GameNumber.Zero;
            for (int i = 0; i < cp.StatModifierType.Length; i++)
            {
                if (cp.StatModifierType[i] != code) continue;
                sum += Formula.Evaluate(cp.ModifierFormulaType[i], cp.ModifierBase[i], cp.ModifierMultiplier[i], cp.ModifierGrowth[i], 1, cp.ModifierPower[i], cp.ModifierRound[i]);
            }
            return sum;
        }

        /// <summary>Код 10: множитель выхода всех генераторов зоны.</summary>
        public GameNumber GlobalIncomeMultiplier()
        {
            var m = GameNumber.One;
            var perGate = CheckpointValue(10);
            if (Mine.GatesOpened > 0 && perGate.IsPositive) m *= perGate.Pow(Mine.GatesOpened);
            foreach (var c in Balance.CardsInOrder)
            {
                if (c.StatModifierType != 10) continue;
                var v = CardEffectValue(c);
                if (v.HasValue && v.Value.IsPositive) m *= v.Value;
            }
            return m;
        }

        /// <summary>Код 9: множитель выхода генератора от его manager-карт.</summary>
        public GameNumber ManagerMultiplier(string generatorId)
        {
            var m = GameNumber.One;
            foreach (var c in Balance.CardsInOrder)
            {
                if (!c.IsManager || c.StatModifierType != 9 || c.TargetIds == null || Array.IndexOf(c.TargetIds, generatorId) < 0) continue;
                var v = CardEffectValue(c);
                if (v.HasValue && v.Value.IsPositive) m *= v.Value;
            }
            return m;
        }

        /// <summary>Код 1: лимит котиков.</summary>
        public int UnitCap()
        {
            double cap = Balance.Props.BaseUnitCap + CheckpointValue(1).ToDouble() * Mine.GatesOpened;
            foreach (var c in Balance.CardsInOrder)
            {
                if (c.StatModifierType != 1) continue;
                var v = CardEffectValue(c);
                if (v.HasValue) cap += v.Value.ToDouble();
            }
            return (int)Math.Floor(cap + 1e-9);
        }

        /// <summary>Код 2: делитель цены найма.</summary>
        public GameNumber HireDivisor()
        {
            var perGate = CheckpointValue(2);
            if (Mine.GatesOpened == 0 || !perGate.IsPositive) return GameNumber.One;
            return perGate.Pow(Mine.GatesOpened);
        }

        /// <summary>Код 7: интервал бесплатного котика, секунды.</summary>
        public double FreeCatIntervalSeconds()
        {
            var cart = Balance.Cart;
            double s = Formula.Evaluate(cart.SpawnDelaySecFormulaType, cart.SpawnDelaySecBase, cart.SpawnDelaySecMultiplier, cart.SpawnDelaySecGrowth, 1).ToDouble();
            s -= CheckpointValue(7).ToDouble() * Mine.GatesOpened;
            foreach (var c in Balance.CardsInOrder)
            {
                if (c.StatModifierType != 7) continue;
                var v = CardEffectValue(c);
                if (v.HasValue) s -= v.Value.ToDouble();
            }
            return Math.Max(MinFreeCatIntervalSeconds, s);
        }

        public int OccupiedSlots
        {
            get
            {
                int n = Mine.Cats.Count;
                foreach (var c in Mine.Containers) if (c.HoldsCat) n++;
                return n;
            }
        }

        // ───────────────────────────── Котики: параметры ─────────────────────────────

        public GameNumber CatDamage(int level)
        {
            var m = Balance.Miner;
            return Formula.Evaluate(m.DamageFormulaType, m.DamageBase, m.DamageMultiplier, m.DamageGrowth, level);
        }

        public long AttackDelayTicks(int level)
        {
            var m = Balance.Miner;
            double s = Formula.Evaluate(m.AttackDelaySecFormulaType, m.AttackDelaySecBase, m.AttackDelaySecMultiplier, m.AttackDelaySecGrowth, level).ToDouble();
            return Math.Max(1L, SecondsToTicks(s));
        }

        public double CritChance(int level)
        {
            var m = Balance.Miner;
            return Formula.Evaluate(m.CritChanceFormulaType, m.CritChanceBase, m.CritChanceMultiplier, m.CritChanceGrowth, level).ToDouble();
        }

        public double CritPower(int level)
        {
            var m = Balance.Miner;
            return Formula.Evaluate(m.CritPowerFormulaType, m.CritPowerBase, m.CritPowerMultiplier, m.CritPowerGrowth, level).ToDouble();
        }

        /// <summary>Урон в секунду котика (без учёта критов, шанс которых в v1 равен 0).</summary>
        public GameNumber CatDps(int level) => CatDamage(level) / GameNumber.FromDouble(TicksToSeconds(AttackDelayTicks(level)));

        /// <summary>Уровень следующего найма (E-05): 1 + floor(F-0(Level*; x = n)).</summary>
        public int HireLevel()
        {
            var r = Balance.Reinforcement;
            double v = Formula.Evaluate(r.LevelFormulaType, r.LevelBase, r.LevelMultiplier, r.LevelGrowth, Mine.HireCount).ToDouble();
            return 1 + (int)Math.Floor(v + 1e-9);
        }

        /// <summary>Заполнение шкалы уровня найма (дробная часть), 0..1.</summary>
        public double HireGauge()
        {
            var r = Balance.Reinforcement;
            double v = Formula.Evaluate(r.LevelFormulaType, r.LevelBase, r.LevelMultiplier, r.LevelGrowth, Mine.HireCount).ToDouble();
            return v - Math.Floor(v + 1e-9);
        }

        /// <summary>Цена следующего найма (E-05).</summary>
        public GameNumber HirePrice()
        {
            var r = Balance.Reinforcement;
            var raw = Formula.Evaluate(r.CostFormulaType, r.CostBase, r.CostMultiplier, r.CostGrowth, Mine.HireCount + 1);
            return Formula.Price(raw / HireDivisor());
        }

        // ───────────────────────────── Генераторы ─────────────────────────────

        public sealed class GeneratorDef
        {
            public string Id;
            public int DelayFt; public double DelayB, DelayM, DelayG;
            public int OutFt; public double OutB, OutM, OutG;
            public int CostFt; public double CostB, CostM, CostG;
            public GeneratorObjectiveDto Objectives;
            public int[] Thresholds;          // уровни порогов T_k
            public GameNumber[] Products;     // Products[k] — произведение первых k множителей
            public int MaxLevel;
        }

        public GeneratorDef DefFor(ZoneObject obj)
        {
            string id = obj.Kind == ObjectKind.Cart ? BalanceRepository.CartId : obj.TypeId;
            if (m_GeneratorDefs.TryGetValue(id, out var def)) return def;
            def = new GeneratorDef { Id = id };
            if (obj.Kind == ObjectKind.Cart)
            {
                var c = Balance.Cart;
                def.DelayFt = c.GenerationDelaySecFormulaType; def.DelayB = c.GenerationDelaySecBase; def.DelayM = c.GenerationDelaySecMultiplier; def.DelayG = c.GenerationDelaySecGrowth;
                def.OutFt = c.CurrencyOutputFormulaType; def.OutB = c.CurrencyOutputBase; def.OutM = c.CurrencyOutputMultiplier; def.OutG = c.CurrencyOutputGrowth;
                def.CostFt = c.UpgradeCostFormulaType; def.CostB = c.UpgradeCostBase; def.CostM = c.UpgradeCostMultiplier; def.CostG = c.UpgradeCostGrowth;
            }
            else
            {
                var s = Balance.Shafts[id];
                def.DelayFt = s.GenerationDelaySecFormulaType; def.DelayB = s.GenerationDelaySecBase; def.DelayM = s.GenerationDelaySecMultiplier; def.DelayG = s.GenerationDelaySecGrowth;
                def.OutFt = s.CurrencyOutputFormulaType; def.OutB = s.CurrencyOutputBase; def.OutM = s.CurrencyOutputMultiplier; def.OutG = s.CurrencyOutputGrowth;
                def.CostFt = s.UpgradeCostFormulaType; def.CostB = s.UpgradeCostBase; def.CostM = s.UpgradeCostMultiplier; def.CostG = s.UpgradeCostGrowth;
            }
            def.Objectives = Balance.Objectives[id];
            var oc = def.Objectives.ObjectiveCount;
            def.Thresholds = new int[oc.Length];
            def.Products = new GameNumber[oc.Length + 1];
            def.Products[0] = GameNumber.One;
            int t = 1;
            for (int k = 0; k < oc.Length; k++)
            {
                t += oc[k];
                def.Thresholds[k] = t;
                def.Products[k + 1] = def.Products[k] * GameNumber.FromDouble(def.Objectives.CoreCurrencyMultiplier[k]);
            }
            def.MaxLevel = t;
            m_GeneratorDefs[id] = def;
            return def;
        }

        public bool IsGenerator(int ord)
        {
            var st = Mine.Objects[ord];
            return st.Status == ObjectStatus.Producing && st.Gen != null;
        }

        /// <summary>Выход генератора за цикл (ТЗ 9.4).</summary>
        public GameNumber GeneratorOutput(int ord)
        {
            var obj = Layout.Objects[ord];
            var gen = Mine.Objects[ord].Gen;
            var def = DefFor(obj);
            var baseOut = Formula.Evaluate(def.OutFt, def.OutB, def.OutM, def.OutG, gen.Level);
            int reached = Math.Min(gen.ThresholdsReached, def.Thresholds.Length);
            return baseOut * def.Products[reached] * ManagerMultiplier(def.Id) * GlobalIncomeMultiplier();
        }

        public long GeneratorCycleTicks(int ord)
        {
            var obj = Layout.Objects[ord];
            var gen = Mine.Objects[ord].Gen;
            var def = DefFor(obj);
            double s = Formula.Evaluate(def.DelayFt, def.DelayB, def.DelayM, def.DelayG, gen.Level).ToDouble();
            return Math.Max(1L, SecondsToTicks(s));
        }

        public GameNumber UpgradeCost(int ord, int level)
        {
            var def = DefFor(Layout.Objects[ord]);
            return Formula.Price(Formula.Evaluate(def.CostFt, def.CostB, def.CostM, def.CostG, level));
        }

        public int NextThresholdLevel(int ord)
        {
            var def = DefFor(Layout.Objects[ord]);
            var gen = Mine.Objects[ord].Gen;
            return gen.ThresholdsReached < def.Thresholds.Length ? def.Thresholds[gen.ThresholdsReached] : def.MaxLevel;
        }

        public int MaxGeneratorLevel(int ord) => DefFor(Layout.Objects[ord]).MaxLevel;

        /// <summary>Генератор участвует в автоматическом доходе: кузница или автоматизированная шахта.</summary>
        public bool IsAutoGenerator(int ord)
        {
            if (!IsGenerator(ord)) return false;
            return Layout.Objects[ord].Kind == ObjectKind.Cart || Mine.Objects[ord].Gen.Automated;
        }

        /// <summary>Скорость автоматического дохода: кузница + автоматизированные шахты, золото/с.</summary>
        public GameNumber AutoIncomePerSecond()
        {
            var sum = GameNumber.Zero;
            for (int ord = 0; ord < Layout.Objects.Count; ord++)
            {
                if (!IsAutoGenerator(ord)) continue;
                sum += GeneratorOutput(ord) / GameNumber.FromDouble(TicksToSeconds(GeneratorCycleTicks(ord)));
            }
            return sum;
        }

        /// <summary>Ставка для наград «секунды дохода» (E-10), не ниже NoGeneratorsCurrencyRate.</summary>
        public GameNumber GetRewardIncomeRate()
        {
            var floor = GameNumber.FromDouble(Balance.Props.NoGeneratorsCurrencyRate);
            return GameNumber.Max(AutoIncomePerSecond(), floor);
        }

        // ───────────────────────────── Запросы для UI ─────────────────────────────

        /// <summary>
        /// Оценка времени до завершения цели по назначенным работникам; +∞ — работников нет.
        /// Переносимый котик не бьёт и не учитывается, поэтому оценка меняется во время переноса (ТЗ 8.2).
        /// </summary>
        public double EtaSeconds(int ord)
        {
            var st = Mine.Objects[ord];
            if (!st.Hp.IsPositive) return 0;
            var dps = GameNumber.Zero;
            int index = Layout.Objects[ord].Index;
            foreach (var c in Mine.Cats)
                if (c.TargetIndex == index && c.Id != m_DragCatId) dps += CatDps(c.Level);
            if (dps.IsZero) return double.PositiveInfinity;
            return (st.Hp / dps).ToDouble();
        }

        /// <summary>Число работающих на цели котиков, без переносимого.</summary>
        public int WorkersOn(int ord)
        {
            int index = Layout.Objects[ord].Index;
            int n = 0;
            foreach (var c in Mine.Cats) if (c.TargetIndex == index && c.Id != m_DragCatId) n++;
            return n;
        }

        public string ZoneId => Layout.ZoneId;

        public static string ZoneKey(int zone) => "zone" + zone.ToString(CultureInfo.InvariantCulture);
    }
}
