using System;
using System.Collections.Generic;
using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using UnityEngine;
using BigInteger = System.Numerics.BigInteger;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Поле режима CatOutpostV2 (RUNTIME_SPEC §2, §4; ТЗ 5, 7, 8, 16): пол, стены, декор и оформление зоны (логика BoardView),
    /// площадка лагеря (строки Depth..Depth+2) и двор построек ниже, цели, котики, груз, лагерь, барьер ворот, отладочный слой.
    /// Вид только читает <see cref="OutpostSimulation"/> и события <see cref="OutpostEvent"/>: экономику не меняет, анимация
    /// ничего не начисляет. Контракт кадра (как у BoardView): сначала <see cref="OnEvent"/> для каждого события кадра, затем один
    /// <see cref="Sync"/>. Котики, мешочки и полёты — в пулах, обычный кадр не выделяет память.
    /// <para>
    /// Координаты: клетка (col, row) → локально в корне поля (col + 0,5, 0, Depth − row − 0,5) — формула RUNTIME_SPEC §4.1;
    /// лагерь — z = −0,5…−2,5, двор — −3…−6, нижняя стена — −6…−7. Корень поля поднят на <see cref="OriginZ"/> = BaseRows + StripRows,
    /// чтобы низ двора лёг в мировую z = 0: CameraRig считает низ поля нулём, и вызов <c>Rig.SetBoard(Width, CameraDepth, …)</c>
    /// показывает лагерь. Все публичные позиции — мировые (уже с подъёмом); сам OutpostBoardView стоит в начале координат.
    /// </para>
    /// </summary>
    public sealed partial class OutpostBoardView : MonoBehaviour
    {
        const float Px = 1f / PixelArt.CellPixels;
        /// <summary>Строк площадки базы под картой (ТЗ 8.3).</summary>
        public const int BaseRows = OutpostLevelAdapter.BaseRows;
        /// <summary>Строк двора с постройками под площадкой (ART_NAMES §3.2: st0…st2).</summary>
        public const int StripRows = 3;

        // Бюджеты эффектов на кадр (TZ 19): удары многих котиков на ×100 не забивают пулы частиц и текста.
        const int FxPerFrame = 8, FxPerFrameReduced = 4, TextPerFrame = 3;
        // Полёты мешочков (секунды, высота дуги в клетках).
        const float ReleaseFlight = 0.36f, ReleaseArc = 0.6f;
        const float PickupFlight = 0.2f, PickupArc = 0.3f;
        const float DepositFlight = 0.22f, DepositArc = 0.35f;
        /// <summary>Ширина шага очереди у склада (клетки): 13 мест в строке pad2 при ширине 7.</summary>
        const float QueueStep = 0.5f;

        OutpostMap m_Map;
        OutpostSimulation m_Sim;
        BalanceRepository m_Balance;
        Transform m_Root, m_TargetsRoot, m_CatsRoot, m_CargoRoot, m_PoolRoot;
        OutpostTargetView[] m_Targets;
        int[] m_CellTarget;               // клетка карты → индекс цели (footprint и рамка ворот) или −1
        GateBarrierView[] m_Barriers;     // по Order ворот
        int[] m_GateIndex;                // Order ворот → индекс цели
        BaseCampView m_Camp;
        CargoFlights m_Flights;
        PlayerStrikeFx m_Strikes;
        OutpostDebugOverlay m_Debug;
        int m_Gates = -1;
        bool m_Live;                      // первая синхронизация после Build/Refresh прошла: изменения — «вживую»

        // Котики: вид по индексу работника в Run.Workers (Id = индекс + 1), пул — общий для всех шахт.
        readonly List<OutpostCatView> m_Cats = new List<OutpostCatView>();
        readonly Stack<OutpostCatView> m_CatPool = new Stack<OutpostCatView>();
        struct WorkerTrack
        {
            public long SegmentStart, Duration, Deadline;
            public double P0;
            public int LastBag;
            public int MiningTarget;
            public bool GatherArrived;
        }
        WorkerTrack[] m_Track = new WorkerTrack[16];
        long m_LastNow = long.MinValue;
        bool m_Gathering;

        // Груз: вид на клетку, пул.
        readonly Dictionary<int, CargoPileView> m_Piles = new Dictionary<int, CargoPileView>();
        readonly Stack<CargoPileView> m_PilePool = new Stack<CargoPileView>();
        readonly List<int> m_Remove = new List<int>();
        int[] m_CellUnits;
        readonly List<int> m_UnitCells = new List<int>();

        int m_FxFrame = -1, m_FxCount, m_TextCount;
        OutpostSelection m_Selection = OutpostSelection.None;
        int m_SelectedTargetIndex = -1;

        /// <summary>Подсказки звука и отклика (см. <see cref="OutpostViewCue"/>): контроллер подписывается и играет Sfx.</summary>
        public event Action<OutpostViewCueArgs> Cue;

        /// <summary>Эффекты (контроллер задаёт до Build; OnEvent подхватывает переданные).</summary>
        public Effects Fx { get; set; }
        public OutpostMap Map => m_Map;
        public int Width => m_Map != null ? m_Map.Width : 7;
        public int Depth => m_Map != null ? m_Map.Depth : 0;
        /// <summary>Подъём корня поля над мировым нулём: низ двора построек — в мировой z = 0.</summary>
        public float OriginZ => BaseRows + StripRows;
        /// <summary>Глубина поля для <c>CameraRig.SetBoard(Width, CameraDepth, startZ)</c>: карта + площадка + двор.</summary>
        public int CameraDepth => Depth + BaseRows + StripRows;
        /// <summary>Мировая z центра лагеря (кнопка «К базе»: <c>Rig.FocusOnSmooth(BaseFocusZ)</c>; камера сама упрётся в низ поля).</summary>
        public float BaseFocusZ => OriginZ - 3f;
        /// <summary>Мировая z верхней кромки строки 0.</summary>
        public float TopZ => OriginZ + Depth;
        /// <summary>Идёт сбор команды в лагере после завершения шахты (ReadyToContinue).</summary>
        public bool Gathering => m_Gathering;
        public BaseCampView Camp => m_Camp;

        Vector3 Origin => m_Root != null ? m_Root.position : new Vector3(0f, 0f, OriginZ);

        // ───────────── Координаты ─────────────

        /// <summary>Центр клетки (включая строки лагеря) локально в корне поля — формула RUNTIME_SPEC §4.1.</summary>
        public Vector3 CellCenterLocal(int cell) => new Vector3(m_Map.Col(cell) + 0.5f, 0f, m_Map.Depth - m_Map.Row(cell) - 0.5f);

        /// <summary>Центр клетки в мире.</summary>
        public Vector3 CellCenter(int cell) => Origin + CellCenterLocal(cell);

        public Vector3 CellCenter(int col, int row) => Origin + new Vector3(col + 0.5f, 0f, Depth - row - 0.5f);

        /// <summary>Непрерывная сетка (gx — столбцы, gy — строки сверху) → мир.</summary>
        public Vector3 GridToWorld(double gx, double gy) => Origin + new Vector3((float)gx, 0f, (float)(Depth - gy));

        /// <summary>Мир → непрерывная сетка (x — столбец, y — строка сверху); клетка — FloorToInt по обеим осям.</summary>
        public Vector2 WorldToGrid(Vector3 world)
        {
            var o = Origin;
            return new Vector2(world.x - o.x, Depth - (world.z - o.z));
        }

        /// <summary>Клетка навигации (карта и площадка лагеря) под мировой точкой или −1 (двор, стены, вне поля).</summary>
        public int WorldToCell(Vector3 world)
        {
            if (m_Map == null) return -1;
            var g = WorldToGrid(world);
            int col = Mathf.FloorToInt(g.x), row = Mathf.FloorToInt(g.y);
            if (col < 0 || col >= m_Map.Width || row < 0 || row >= m_Map.Rows) return -1;
            int cell = m_Map.Cell(col, row);
            return m_Map.IsPlayable(cell) ? cell : -1;
        }

        /// <summary>Центр лагеря (площадка и двор) в мире.</summary>
        public Vector3 BaseWorldPosition => m_Camp != null ? Origin + m_Camp.CenterLocal : Origin + new Vector3(Width / 2f, 0f, -3f);

        /// <summary>Центр места склада slot (0, 1) в мире.</summary>
        public Vector3 DepotWorldPosition(int slot) => m_Camp != null ? Origin + m_Camp.SlotLocal(slot) : BaseWorldPosition;

        /// <summary>Точка над местом склада: «+N», полёт монет к кошельку (Hud.FlyLoot) — ТЗ 16, зачисление на базе.</summary>
        public Vector3 DepotIndicatorWorldPosition(int slot) => m_Camp != null ? Origin + m_Camp.SlotIndicatorLocal(slot) : BaseWorldPosition;

        /// <summary>Постройка ветки улучшения в мире (вспышка).</summary>
        public Vector3 BranchWorldPosition(UpgradeBranch branch) => m_Camp != null ? Origin + m_Camp.BranchLocal(branch) : BaseWorldPosition;

        /// <summary>Центр footprint цели (у ворот — рамки) в мире.</summary>
        public bool TryGetTargetWorldPosition(int targetId, out Vector3 world)
        {
            var v = TargetViewOf(targetId);
            world = v != null ? Origin + v.CenterLocal : Vector3.zero;
            return v != null;
        }

        /// <summary>Показанная точка котика в мире (центр клетки/ребра со смещением; ноги — на 10 px ниже).</summary>
        public bool TryGetWorkerWorldPosition(int workerId, out Vector3 world)
        {
            var v = CatViewOf(workerId);
            world = v != null ? Origin + v.LocalPosition : Vector3.zero;
            return v != null;
        }

        /// <summary>Центр рамки ворот Order в мире (камера к открытым воротам).</summary>
        public Vector3 GateWorldPosition(int order)
        {
            if (m_GateIndex == null || order < 0 || order >= m_GateIndex.Length) return BaseWorldPosition;
            return Origin + m_Targets[m_GateIndex[order]].CenterLocal;
        }

        public OutpostCatView CatViewOf(int workerId)
        {
            int i = workerId - 1;
            if (i >= 0 && i < m_Cats.Count && m_Cats[i] != null && m_Cats[i].WorkerId == workerId) return m_Cats[i];
            for (int k = 0; k < m_Cats.Count; k++)
                if (m_Cats[k] != null && m_Cats[k].WorkerId == workerId) return m_Cats[k];
            return null;
        }

        public OutpostTargetView TargetViewOf(int targetId)
        {
            if (m_Map == null || m_Targets == null) return null;
            int i = m_Map.TargetIndex(targetId);
            return i >= 0 && i < m_Targets.Length ? m_Targets[i] : null;
        }

        /// <summary>Вид построен для этой симуляции (та же карта). Sync с чужой симуляцией ничего не делает.</summary>
        public bool Matches(OutpostSimulation sim) => sim != null && m_Map != null && ReferenceEquals(sim.Map, m_Map);

        // ───────────── Сборка ─────────────

        /// <summary>
        /// Построить поле шахты: оформление зоны (PixelTheme.Current), пол и стены, декор, затемнение, цели, барьеры ворот,
        /// лагерь; котики и груз — из состояния без анимаций появления. <paramref name="balance"/> — исходный репозиторий
        /// (оформление камней по циклу наград, PresentationCatalog); null — оформление по умолчанию.
        /// Пересборка (переход, смена профиля) возвращает котиков и мешочки в пулы.
        /// </summary>
        public void Build(OutpostMap map, OutpostSimulation sim, BalanceRepository balance = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            m_Map = map;
            m_Sim = sim;
            m_Balance = balance;
            PixelTheme.Current = PresentationCatalog.ThemeOf(map.Number);
            EnsurePoolRoot();
            ReleaseAll();
            if (m_Root != null) Destroy(m_Root.gameObject);
            if (m_FloorMesh != null) Destroy(m_FloorMesh);
            ReleaseTopologyMeshes();
            m_Gates = -1;
            m_GearFrame = -1;
            m_ExitShown = -1;
            m_Live = false;
            m_Gathering = false;
            m_LastNow = long.MinValue;
            m_Selection = OutpostSelection.None;
            m_SelectedTargetIndex = -1;
            m_Root = new GameObject("Outpost_" + map.ZoneId).transform;
            m_Root.SetParent(transform, false);
            // Подъём корня до сборки детей: оверлеи и подписи считают порядок по мировой z.
            m_Root.localPosition = new Vector3(0f, 0f, OriginZ);
            BuildRowSectors();
            BuildFloorAndWalls();
            BuildDecor();
            BuildDressing();
            BuildFog();
            ResetFactories();
            m_TargetsRoot = new GameObject("Targets").transform;
            m_TargetsRoot.SetParent(m_Root, false);
            m_CargoRoot = new GameObject("Cargo").transform;
            m_CargoRoot.SetParent(m_Root, false);
            m_CatsRoot = new GameObject("Cats").transform;
            m_CatsRoot.SetParent(m_Root, false);
            BuildTargets();
            m_Camp = BaseCampView.Create(m_Root, map);
            BuildFactoryNetwork();
            m_Flights = new CargoFlights(PixelWorld.Visual(m_Root, "Flights"));
            m_Strikes = new PlayerStrikeFx(PixelWorld.Visual(m_Root, "Strikes"));
            m_Debug = OutpostDebugOverlay.Create(m_Root);
            m_CellUnits = new int[map.CellCount];
            m_UnitCells.Clear();
            if (sim != null && Matches(sim)) Sync(sim, 0f);
        }

        void BuildTargets()
        {
            var targets = m_Map.Targets;
            m_Targets = new OutpostTargetView[targets.Length];
            m_CellTarget = new int[m_Map.Width * m_Map.Depth];
            for (int i = 0; i < m_CellTarget.Length; i++) m_CellTarget[i] = -1;
            for (int i = 0; i < targets.Length; i++)
            {
                var mt = targets[i];
                var v = OutpostTargetView.Create(m_TargetsRoot, m_Map, mt, i, m_Balance, FactoryGoodOf(mt));
                m_Targets[i] = v;
                AttachFactory(v, i);
                if (mt.IsGate && m_Map.Topology == null)
                {
                    for (int c = v.Column; c < v.Column + v.Width; c++) m_CellTarget[m_Map.Cell(c, mt.Row)] = i;
                }
                else
                    foreach (int cell in mt.Cells) m_CellTarget[cell] = i;
            }
            var gates = m_Map.Gates;
            m_Barriers = new GateBarrierView[gates.Length];
            m_GateIndex = new int[gates.Length];
            for (int k = 0; k < gates.Length; k++)
            {
                int ti = m_Map.TargetIndex(gates[k].TargetId);
                m_GateIndex[k] = ti;
                m_Barriers[k] = GateBarrierView.Create(m_TargetsRoot, m_Map, gates[k], ti >= 0 ? m_Targets[ti] : null);
            }
        }

        void EnsurePoolRoot()
        {
            if (m_PoolRoot != null) return;
            m_PoolRoot = new GameObject("Pool").transform;
            m_PoolRoot.SetParent(transform, false);
            m_PoolRoot.gameObject.SetActive(false);
        }

        /// <summary>Вернуть котиков и мешочки в пулы (перед пересборкой поля).</summary>
        void ReleaseAll()
        {
            for (int i = 0; i < m_Cats.Count; i++)
            {
                var v = m_Cats[i];
                if (v == null) continue;
                v.transform.SetParent(m_PoolRoot, false);
                m_CatPool.Push(v);
            }
            m_Cats.Clear();
            foreach (var kv in m_Piles)
            {
                if (kv.Value == null) continue;
                kv.Value.transform.SetParent(m_PoolRoot, false);
                m_PilePool.Push(kv.Value);
            }
            m_Piles.Clear();
            m_Flights?.Clear();
            m_Strikes?.Clear();
        }

        void OnDestroy()
        {
            if (m_FloorMesh != null) Destroy(m_FloorMesh);
            ReleaseTopologyMeshes();
        }

        /// <summary>
        /// Привести вид к ядру без анимаций (после офлайна, загрузки, ×100 и других изменений без событий): котики встают в точки
        /// ядра без догонялки, груз — без прыжков, полёты гаснут.
        /// </summary>
        public void Refresh()
        {
            m_Live = false;
            m_LastNow = long.MinValue;
            m_Flights?.Clear();
            for (int i = 0; i < m_Cats.Count; i++)
                if (m_Cats[i] != null) m_Cats[i].Snap();
        }

        // ───────────── Выбор ─────────────

        /// <summary>Показать выбор (кольцо под котиком, уголки цели или двора); вызывать при смене Selection.</summary>
        public void SetSelection(OutpostSelection selection)
        {
            m_Selection = selection;
            if (m_Map == null || m_Targets == null) return;
            int ti = selection.Kind == OutpostSelectionKind.Target ? m_Map.TargetIndex(selection.Id) : -1;
            if (ti != m_SelectedTargetIndex)
            {
                if (m_SelectedTargetIndex >= 0 && m_SelectedTargetIndex < m_Targets.Length) m_Targets[m_SelectedTargetIndex].SetSelected(false);
                m_SelectedTargetIndex = ti;
                if (ti >= 0 && ti < m_Targets.Length) m_Targets[ti].SetSelected(true);
            }
            if (m_Camp != null) m_Camp.SetSelected(selection.Kind == OutpostSelectionKind.Base);
            for (int i = 0; i < m_Cats.Count; i++)
                if (m_Cats[i] != null) m_Cats[i].SetSelected(selection.Kind == OutpostSelectionKind.Worker && m_Cats[i].WorkerId == selection.Id);
        }

        /// <summary>
        /// Выбор по клетке без коллайдеров (запасной путь ввода): котик в радиусе 0,45 клетки → цель под точкой (открытый завод
        /// тоже) → склад товаров → двор лагеря. false — пустой пол.
        /// </summary>
        public bool TryPickAtWorld(Vector3 world, out OutpostSelection selection)
        {
            selection = OutpostSelection.None;
            if (m_Map == null || m_Sim == null) return false;
            var local = world - Origin;
            float best = 0.45f * 0.45f;
            for (int i = 0; i < m_Cats.Count; i++)
            {
                var v = m_Cats[i];
                if (v == null) continue;
                var d = v.LocalPosition - local;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq >= best) continue;
                best = sq;
                selection = new OutpostSelection { Kind = OutpostSelectionKind.Worker, Id = v.WorkerId };
            }
            if (selection.Kind != OutpostSelectionKind.None) return true;
            var g = WorldToGrid(world);
            int col = Mathf.FloorToInt(g.x), row = Mathf.FloorToInt(g.y);
            if (col < 0 || col >= m_Map.Width) return false;
            if (row >= m_Map.Rows && row < m_Map.Rows + StripRows)
            {
                // Склад товаров — задний ряд двора (строки st0…st1) в своём столбце.
                bool warehouse = m_Camp != null && m_Camp.HasWarehouse && col == Mathf.FloorToInt(m_Camp.WarehouseLocal.x) && row < m_Map.Rows + 2;
                selection = new OutpostSelection { Kind = warehouse ? OutpostSelectionKind.Warehouse : OutpostSelectionKind.Base, Id = -1 };
                return true;
            }
            if (row < 0 || row >= m_Map.Depth) return false;
            int ti = m_CellTarget[m_Map.Cell(col, row)];
            if (ti < 0 || (m_Sim.Run.Targets[ti].Destroyed && !m_Targets[ti].IsFactory)) return false;
            selection = new OutpostSelection { Kind = OutpostSelectionKind.Target, Id = m_Map.Targets[ti].Id };
            return true;
        }

        // ───────────── Синхронизация ─────────────

        /// <summary>Привести вид к ядру (каждый кадр после OnEvent всех событий кадра). Без выделений памяти.</summary>
        public void Sync(OutpostSimulation sim, float dt)
        {
            if (!Matches(sim)) return;
            m_Sim = sim;
            var run = sim.Run;
            float time = Time.time;
            bool live = m_Live;

            if (m_Gates != run.GatesOpened)
            {
                int before = live ? m_Gates : -1;
                m_Gates = run.GatesOpened;
                ApplyFog(before, m_Gates);
            }
            UpdateFogFade(dt);

            for (int i = 0; i < m_Targets.Length; i++)
            {
                var ts = run.Targets[i];
                int assigned = ts.Kind == 's' ? sim.AssignedTo(ts.Id) : 0;
                m_Targets[i].ApplyState(ts, assigned, time, dt);
                SyncFactoryView(run, i, time, dt);
            }
            for (int k = 0; k < m_Barriers.Length; k++)
            {
                int ti = m_GateIndex[k];
                m_Barriers[k].Sync(sim, ti >= 0 ? m_Targets[ti].HpFraction : 1f, dt);
            }
            UpdateExit(run);

            if (run.Phase == RunPhase.ReadyToContinue && !m_Gathering) StartGather();
            else if (run.Phase != RunPhase.ReadyToContinue && m_Gathering) m_Gathering = false;
            SyncCats(sim, dt, live);
            SyncCargo(run, dt, live);
            m_Flights.Tick(dt);
            m_Camp.Sync(sim, dt);
            SyncDynamite(run);
            SyncFactoryNetwork(run, dt);
            m_Debug.Sync(sim, m_Map);

            UpdateLanterns();
            UpdateGears();
            UpdateGlints(run);
            // Последним: кадр удара кирки отдаёт команду ядру, её события вид покажет в следующем кадре.
            TickStrikes(dt);
            m_Live = true;
        }

        void SyncCats(OutpostSimulation sim, float dt, bool live)
        {
            var run = sim.Run;
            var workers = run.Workers;
            var stats = sim.Stats;
            long now = run.Now;
            long dNow = m_LastNow == long.MinValue ? 0 : Math.Max(0L, now - m_LastNow);
            m_LastNow = now;
            long edgeTicks = Math.Max(1L, stats.EdgeTicks);
            float maxJump = (float)((double)dNow / edgeTicks);
            long tps = Math.Max(1L, run.Balance.TicksPerSecond);
            float windUp = Mathf.Min(0.3f, 0.45f * run.Balance.HitIntervalTicks / (float)tps);
            if (m_Track.Length < workers.Count) Array.Resize(ref m_Track, Mathf.NextPowerOfTwo(workers.Count));

            // Лишние виды (новый run с меньшей командой без пересборки) — в пул.
            for (int i = m_Cats.Count - 1; i >= workers.Count; i--)
            {
                var v = m_Cats[i];
                if (v != null)
                {
                    v.transform.SetParent(m_PoolRoot, false);
                    m_CatPool.Push(v);
                }
                m_Cats.RemoveAt(i);
            }

            bool stepped = false;
            int barrierRow = sim.BarrierRow;
            int barrierOrder = barrierRow > 0 ? PixelWorld.OrderAt(m_Map.Depth - barrierRow - 1f) + 3 : int.MinValue;
            for (int i = 0; i < workers.Count; i++)
            {
                var w = workers[i];
                var view = EnsureCat(i, w, false);
                ref var tr = ref m_Track[i];
                var pose = ComputePose(sim, w, i, ref tr, now, tps, windUp);
                pose.MaxJump = live ? maxJump : float.MaxValue;
                // В строке закрытых ворот (или на ребре в неё) котик рисуется перед створками: он добывает ворота снизу.
                int row = m_Map.Row(w.Cell);
                int nextRow = pose.OnEdge ? m_Map.Row(w.Path[w.PathIndex]) : row;
                pose.MinOrder = m_Map.Topology != null ? TopologyCatMinOrder(w.Cell, w.TargetId)
                    : barrierRow > 0 && (row == barrierRow || nextRow == barrierRow) ? barrierOrder : int.MinValue;
                if (!live) view.Snap();
                if (view.Apply(in pose, dt) && !stepped && live)
                {
                    stepped = true;
                    Emit(OutpostViewCue.Footstep, view.LocalPosition, w.Id);
                }
                int cap = stats.Capacity;
                if (live && w.Bag.Count >= cap && tr.LastBag < cap && w.Bag.Count > 0)
                {
                    var args = OutpostViewCueArgs.Create(OutpostViewCue.BagFull, Origin + view.LocalPosition);
                    args.WorkerId = w.Id;
                    args.Count = w.Bag.Count;
                    Cue?.Invoke(args);
                    if (Fx != null && TakeFx()) Fx.Sparkle(Origin + view.LocalPosition + new Vector3(0f, 0f, 0.35f), 2);
                }
                tr.LastBag = w.Bag.Count;
                tr.MiningTarget = w.Mode == WorkerMode.Mining || w.Mode == WorkerMode.MovingToWork ? w.TargetId : -1;
                if (m_Gathering && !tr.GatherArrived && !view.Moving)
                {
                    tr.GatherArrived = true;
                    if (live) view.PlayJoy();
                }
            }
        }

        OutpostCatView EnsureCat(int index, WorkerState w, bool appear)
        {
            while (m_Cats.Count <= index) m_Cats.Add(null);
            var v = m_Cats[index];
            if (v != null && v.WorkerId == w.Id) return v;
            if (v == null)
            {
                v = m_CatPool.Count > 0 ? m_CatPool.Pop() : OutpostCatView.Create(m_CatsRoot);
                v.transform.SetParent(m_CatsRoot, false);
                m_Cats[index] = v;
            }
            v.Init(w.Id, w.Look);
            v.SetLook(m_Sim?.Profile, m_Sim?.Configs?.Equipment);
            v.SetSelected(m_Selection.Kind == OutpostSelectionKind.Worker && m_Selection.Id == w.Id);
            if (index >= m_Track.Length) Array.Resize(ref m_Track, Mathf.NextPowerOfTwo(index + 1));
            m_Track[index] = new WorkerTrack { SegmentStart = long.MinValue, LastBag = w.Bag.Count, MiningTarget = -1 };
            if (appear && m_Camp != null) v.PlaySpawn(m_Camp.HireExitLocal);
            return v;
        }

        /// <summary>
        /// Поза по ядру (RUNTIME_SPEC §4.2): на ребре — CellCenter(Cell) → CellCenter(Path[PathIndex]) по p = P0 + (Now − SegmentStart)/Duration
        /// (float только для показа), иначе — центр Cell; плюс стабильное смещение по Id. Ожидающие склада стоят в строке pad2,
        /// после завершения шахты — на местах сбора в лагере.
        /// </summary>
        OutpostCatPose ComputePose(OutpostSimulation sim, WorkerState w, int index, ref WorkerTrack tr, long now, long tps, float windUp)
        {
            var run = sim.Run;
            var pose = new OutpostCatPose
            {
                Mode = w.Mode,
                Bag = w.Bag.Count,
                Capacity = sim.Stats.Capacity,
                FaceX = float.NaN,
                TimeToHit = float.PositiveInfinity,
                WindUp = windUp,
                Progress = -1f,
            };
            var a = w.Action;
            if (m_Gathering)
            {
                pose.Position = GatherSpot(index);
                pose.Mode = WorkerMode.Idle;
                pose.FaceX = m_Map.Width / 2f;
                return pose;
            }
            if (w.Mode == WorkerMode.WaitingDepot)
            {
                int q = QueueIndex(run, w.Id);
                pose.Position = QueueSpot(q < 0 ? index : q);
                pose.FaceX = CellCenterLocal(m_Map.IntakeCell).x;
                return pose;
            }
            if (a != null && a.Kind == ActionKind.Edge && w.Path != null && w.PathIndex >= 0 && w.PathIndex < w.Path.Length)
            {
                double p = EdgeProgress(ref tr, a, now);
                var from = CellCenterLocal(w.Cell);
                var to = CellCenterLocal(w.Path[w.PathIndex]);
                pose.Position = Vector3.LerpUnclamped(from, to, (float)p) + WorkerOffset(w.Id);
                pose.OnEdge = true;
            }
            else pose.Position = CellCenterLocal(w.Cell) + WorkerOffset(w.Id);

            switch (w.Mode)
            {
                case WorkerMode.Mining:
                {
                    var tv = TargetViewOf(w.TargetId);
                    if (tv != null) pose.FaceX = tv.CenterLocal.x;
                    if (a != null && a.Kind == ActionKind.Hit) pose.TimeToHit = Mathf.Max(0f, (float)((double)(a.Deadline - now) / tps));
                    break;
                }
                case WorkerMode.PickingUp:
                    if (a != null && a.Kind == ActionKind.Pickup) pose.Progress = (float)a.ProgressAtForDisplay(now);
                    break;
                case WorkerMode.Unloading:
                    if (a != null && a.Kind == ActionKind.Unload) pose.Progress = (float)a.ProgressAtForDisplay(now);
                    if (m_Camp != null) pose.FaceX = m_Camp.DepotCenterLocal.x;
                    break;
            }
            return pose;
        }

        /// <summary>Доля ребра для показа: P0 (точная дробь ядра) переводится в double один раз на отрезок.</summary>
        static double EdgeProgress(ref WorkerTrack tr, TimedAction a, long now)
        {
            if (a.Duration <= 0) return 1.0;
            if (tr.SegmentStart != a.SegmentStart || tr.Duration != a.Duration || tr.Deadline != a.Deadline)
            {
                tr.SegmentStart = a.SegmentStart;
                tr.Duration = a.Duration;
                tr.Deadline = a.Deadline;
                tr.P0 = a.P0.ToDouble();
            }
            double p = tr.P0 + (double)(now - a.SegmentStart) / a.Duration;
            return p < 0.0 ? 0.0 : (p > 1.0 ? 1.0 : p);
        }

        /// <summary>Стабильное смещение котика внутри клетки по Id (±5 px по x, ±3 px по z): котики в одной клетке расходятся.</summary>
        static Vector3 WorkerOffset(int id)
        {
            uint h = BoardView.Hash(id, 31, 7);
            int dx = (int)(h % 11u) - 5;
            int dz = (int)((h / 11u) % 7u) - 3;
            return new Vector3(dx * Px, 0f, dz * Px);
        }

        static int QueueIndex(OutpostRun run, int workerId)
        {
            var q = run.Queue;
            for (int i = 0; i < q.Count; i++)
                if (q[i].WorkerId == workerId) return i;
            return -1;
        }

        /// <summary>
        /// Место в очереди склада (ТЗ 7.4): нижняя строка площадки (pad2) вокруг точки приёма — 0, −½, +½, −1, +1, … клетки;
        /// клетки не блокируются (очередь логическая). Не поместившиеся — вторым рядом чуть выше.
        /// </summary>
        Vector3 QueueSpot(int q)
        {
            var intake = CellCenterLocal(m_Map.IntakeCell);
            int perRow = Mathf.Max(1, Mathf.FloorToInt((m_Map.Width - 0.5f) / QueueStep));
            int k = q % perRow, line = q / perRow;
            int side = (k + 1) / 2;
            float dx = (k & 1) == 1 ? -side * QueueStep : side * QueueStep;
            float x = Mathf.Clamp(intake.x + dx, 0.35f, m_Map.Width - 0.35f);
            float z = -(BaseRows - 0.5f) + line * 0.3f + ((k & 1) == 0 ? 0f : Px);
            return new Vector3(x, 0f, z);
        }

        /// <summary>Место сбора команды в лагере после завершения: строка pad1 от центра в стороны, затем pad0 со сдвигом, затем pad2.</summary>
        Vector3 GatherSpot(int index)
        {
            int width = m_Map.Width;
            float cx = width / 2f;
            int line = index / width, k = index % width;
            int side = (k + 1) / 2;
            float dx = (k & 1) == 1 ? -side : side;
            float shift = line == 1 ? 0.5f : 0f;
            float x = Mathf.Clamp(cx + dx + shift, 0.5f, width - 0.5f);
            float z = line == 0 ? -1.5f : (line == 1 ? -0.5f : -2.5f + 0.25f * (line - 2));
            return new Vector3(x, 0f, z) + WorkerOffset(index + 1) * 0.5f;
        }

        void StartGather()
        {
            m_Gathering = true;
            for (int i = 0; i < m_Track.Length; i++) m_Track[i].GatherArrived = false;
        }

        // ───────────── Груз ─────────────

        /// <summary>Порции на земле по клеткам (сумма партий) → один вид на клетку из пула. Без выделений.</summary>
        void SyncCargo(OutpostRun run, float dt, bool live)
        {
            for (int i = 0; i < m_UnitCells.Count; i++) m_CellUnits[m_UnitCells[i]] = 0;
            m_UnitCells.Clear();
            var batches = run.Batches;
            for (int i = 0; i < batches.Count; i++)
            {
                var b = batches[i];
                int cell = b.Cell;
                if (cell < 0 || cell >= m_CellUnits.Length || b.Units.Count == 0) continue;
                if (m_CellUnits[cell] == 0) m_UnitCells.Add(cell);
                m_CellUnits[cell] += b.Units.Count;
            }
            for (int i = 0; i < m_UnitCells.Count; i++)
            {
                int cell = m_UnitCells[i];
                if (!m_Piles.ContainsKey(cell)) EnsurePile(cell);
            }
            m_Remove.Clear();
            foreach (var kv in m_Piles)
            {
                var v = kv.Value;
                int key = kv.Key;
                v.Tick(key >= 0 && key < m_CellUnits.Length ? m_CellUnits[key] : 0, live, dt);
                if (v.Idle) m_Remove.Add(kv.Key);
            }
            for (int i = 0; i < m_Remove.Count; i++)
            {
                int cell = m_Remove[i];
                var v = m_Piles[cell];
                m_Piles.Remove(cell);
                v.transform.SetParent(m_PoolRoot, false);
                m_PilePool.Push(v);
            }
        }

        CargoPileView EnsurePile(int cell)
        {
            if (m_Piles.TryGetValue(cell, out var v)) return v;
            v = m_PilePool.Count > 0 ? m_PilePool.Pop() : CargoPileView.Create(m_CargoRoot);
            v.transform.SetParent(m_CargoRoot, false);
            v.Place(cell, CellCenterLocal(cell));
            m_Piles[cell] = v;
            return v;
        }

        // ───────────── Бюджеты, подсказки, тексты ─────────────

        void ResetBudget()
        {
            if (m_FxFrame == Time.frameCount) return;
            m_FxFrame = Time.frameCount;
            m_FxCount = 0;
            m_TextCount = 0;
        }

        bool TakeFx()
        {
            ResetBudget();
            return m_FxCount++ < (VisualSettings.ReduceEffects ? FxPerFrameReduced : FxPerFrame);
        }

        bool TakeText()
        {
            ResetBudget();
            return m_TextCount++ < TextPerFrame;
        }

        void Emit(OutpostViewCue cue, Vector3 local, int workerId = -1, int targetId = -1)
        {
            if (Cue == null) return;
            var args = OutpostViewCueArgs.Create(cue, Origin + local);
            args.WorkerId = workerId;
            args.TargetId = targetId;
            Cue(args);
        }

        // Кэш строк стоимости: порции одной цели одинаковы, «+N» и подпись склада не создают строку на каждую сдачу.
        static readonly BigInteger[] s_MoneyKeys = new BigInteger[16];
        static readonly string[] s_MoneyTexts = new string[16];
        static int s_MoneyNext;

        /// <summary>Короткая запись стоимости (MoneyFormat) с кэшем последних 16 значений.</summary>
        public static string MoneyText(BigInteger value)
        {
            for (int i = 0; i < s_MoneyTexts.Length; i++)
                if (s_MoneyTexts[i] != null && s_MoneyKeys[i] == value) return s_MoneyTexts[i];
            string text = MoneyFormat.Format(value);
            s_MoneyKeys[s_MoneyNext] = value;
            s_MoneyTexts[s_MoneyNext] = text;
            s_MoneyNext = (s_MoneyNext + 1) % s_MoneyTexts.Length;
            return text;
        }

        static readonly string[] s_PlusTexts = new string[16];
        static readonly BigInteger[] s_PlusKeys = new BigInteger[16];
        static int s_PlusNext;

        /// <summary>«+N» для всплывающей стоимости сданной порции (кэш последних 16 значений).</summary>
        public static string PlusMoneyText(BigInteger value)
        {
            for (int i = 0; i < s_PlusTexts.Length; i++)
                if (s_PlusTexts[i] != null && s_PlusKeys[i] == value) return s_PlusTexts[i];
            string text = "+" + MoneyFormat.Format(value);
            s_PlusKeys[s_PlusNext] = value;
            s_PlusTexts[s_PlusNext] = text;
            s_PlusNext = (s_PlusNext + 1) % s_PlusTexts.Length;
            return text;
        }
    }
}
