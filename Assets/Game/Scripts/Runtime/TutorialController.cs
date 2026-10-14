using System;
using System.Collections.Generic;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Обучение на реальных zone1–zone5 (ТЗ 13.4): шаги срабатывают по реальным событиям и условиям,
    /// симуляцию не останавливают, покупку без ресурсов не навязывают, выполненные шаги сохраняются.
    /// zone1, как в эталоне: t1 — нанять (бочка найма), t1b — открыть бочку, t2 — перетащить котика на камень,
    /// t2b — собрать добычу с камня, t3 — нанять второго котика, t3b — открыть вторую бочку, t4 — объединить пару
    /// (жест переноса котика на котика), t4b — отправить объединённого котика на камень, t5 — улучшить кузницу
    /// (тап по кузнице, затем «+1» в её панели), t5b — путь к выходу расчищен: отправить котиков на дверь выхода,
    /// t6 — выход разрушен: «Следующая шахта» (переход с анимацией ухода котиков в штольню); дальше t7… без изменений.
    /// Подсказки без немедленного действия — «Расчистите путь к выходу» (t5a), «Расчистите шахту» (t7a, zone2),
    /// «Чертёж получен — расчистите шахту» (t8a, zone3) — показываются один раз: ≈5 с или до первого переноса котика.
    /// Шаги 1b, 2b, 3b, 4, 5, 7, 9 и 11 засчитываются и по событиям симуляции (<see cref="OnEvent"/>), все — по состоянию.
    /// Показ — через <see cref="Hint"/> (наставник с пузырём, лапка и рамка фокуса у цели шага, жест переноса);
    /// пока подсказку не назначили — тост HUD.
    /// Под любым окном HUD (модальные окна, «Чертежи», настройки, экран конца кампании) подсказка скрыта и возвращается
    /// после их закрытия.
    /// </summary>
    public sealed class TutorialController
    {
        // Цель подсказки шага: нет, элемент UI (по имени TutorialHint.Target*), объект/клетка поля или перенос между ними.
        enum TargetKind
        {
            None,
            Ui,
            World,
            Drag,
        }

        readonly GameController m_Game;
        string m_Shown;
        float m_NextCheck;
        // Цель, которую выбрал последний Evaluate.
        // У переноса m_TargetWorld — куда (камень), m_TargetFrom — откуда (котик). m_TargetDim — затемнение вокруг цели.
        TargetKind m_TargetKind;
        string m_TargetUi;
        Vector3 m_TargetWorld, m_TargetFrom;
        Vector2 m_TargetCells;
        bool m_TargetDim;
        // Что показано в Hint: текст и цель (подсказка перестраивается только при их смене).
        string m_ViewText;
        TargetKind m_ViewKind;
        string m_ViewUi;
        Vector3 m_ViewWorld, m_ViewFrom;
        Vector2 m_ViewCells;
        bool m_ViewDim;
        // Одноразовые подсказки без немедленного действия — «Расчистите путь к выходу» (t5a), «Расчистите шахту» (t7a),
        // «Чертёж получен — расчистите шахту» (t8a): ≈5 с на экране или до первого переноса котика, потом не показываются.
        // m_OnceShown — сколько секунд подсказка m_OnceId уже была на экране; m_OnceOnScreen — её выбрал последний Evaluate.
        const float OnceHintSeconds = 5f;
        string m_OnceId;
        float m_OnceShown;
        bool m_OnceOnScreen;
        float m_LastCheckTime = -1f;
        // Рабочие клетки камня-кандидата (шаг t2): буфер без выделений.
        readonly List<int> m_Work = new List<int>(8);
        // Сессия, для которой уже выполнен разовый засчёт по старым сохранениям.
        GameSession m_LegacySession;
        // Подсказка шага 8 (путь ca003) для последней шахты.
        string m_BlueprintHintShaft, m_BlueprintHint;

        public TutorialController(GameController game)
        {
            m_Game = game;
        }

        /// <summary>
        /// Вид подсказок (создаёт HUD: TutorialHint.Create(safeArea)). null — подсказки показываются тостом, как раньше.
        /// </summary>
        public TutorialHint Hint { get; set; }

        /// <summary>Текст подсказки, которую обучение показывает сейчас (null — нет).</summary>
        public string CurrentHint => m_Shown;

        bool Done(string id) => m_Game.Session.Player.TutorialDone.Contains(id);

        void Complete(string id)
        {
            var done = m_Game.Session.Player.TutorialDone;
            if (done.Contains(id)) return;
            done.Add(id);
            m_Game.MarkDirty();
        }

        /// <summary>Событие симуляции (вызывает GameController.DrainEvents для каждого события пачки).</summary>
        public void OnEvent(SimEvent e)
        {
            var s = m_Game.Session;
            if (s == null) return;
            switch (e.Type)
            {
                case SimEventType.ContainerOpened:
                    // Шаг 1b — бочка найма, шаг 11 — бочка бесплатного котика (любая зона), шаг 9 — ящик доставки:
                    // завершаются открытием.
                    if (e.Text == "Hire")
                    {
                        // Первая бочка найма — шаг 1b, бочка после второго найма — шаг 3b.
                        if (Done("t3")) Complete("t3b");
                        Complete("t1b");
                    }
                    else Complete(e.Text == "FreeCat" ? "t11" : "t9");
                    break;
                case SimEventType.LootCollected:
                    // Шаг 2b — любая собранная добыча (тапом или автосбором).
                    Complete("t2b");
                    break;
                case SimEventType.PayoutCollected:
                {
                    // Шаг 7 завершается сбором ручной выплаты шахты zone2, а не её готовностью.
                    var shafts = s.Layout.Shafts;
                    if (e.Zone == 2 && s.Mine.Zone == 2 && shafts.Count > 0 && shafts[0].Index == e.ObjectIndex) Complete("t7");
                    break;
                }
                case SimEventType.CatMerged:
                    Complete("t4");
                    break;
                case SimEventType.GeneratorUpgraded:
                    if (e.Zone == s.Mine.Zone && e.ObjectIndex == s.Layout.Cart.Index) Complete("t5");
                    break;
            }
        }

        public void Update()
        {
            if (m_Game.Session == null) return;
            // Окно HUD открылось — подсказка прячется в том же кадре, не дожидаясь проверки шагов (раз в 0,25 с).
            var shown = Hint;
            if (shown != null && shown.Visible && m_Game.Hud != null && m_Game.Hud.AnyWindowOpen)
            {
                shown.Hide();
                m_ViewText = null;
            }
            if (Time.unscaledTime < m_NextCheck) return;
            m_NextCheck = Time.unscaledTime + 0.25f;
            // Время на экране одноразовой подсказки — между проверками, пока она видна (без вида — тостом).
            float since = m_LastCheckTime < 0f ? 0f : Mathf.Min(1f, Time.unscaledTime - m_LastCheckTime);
            m_LastCheckTime = Time.unscaledTime;
            if (m_OnceOnScreen && (shown == null || shown.Visible)) m_OnceShown += since;
            m_OnceOnScreen = false;
            m_TargetKind = TargetKind.None;
            m_TargetUi = null;
            m_TargetCells = Vector2.zero;
            m_TargetDim = false;
            string hint = Evaluate();
            var view = Hint;
            if (view == null)
            {
                if (hint != m_Shown)
                {
                    m_Shown = hint;
                    if (!string.IsNullOrEmpty(hint)) m_Game.Hud.Toast(hint, 4.5f);
                }
                return;
            }
            m_Shown = hint;
            ShowOnView(view, hint);
        }

        /// <summary>Показать подсказку в <paramref name="view"/>: перестраивается только при смене текста или цели.</summary>
        void ShowOnView(TutorialHint view, string hint)
        {
            var hud = m_Game.Hud;
            // Под любым окном HUD (в том числе настройками) подсказка скрыта: иначе её Open (SetAsLastSibling) рисует
            // пузырь и лапку поверх уже открытого окна.
            bool covered = hud != null && hud.AnyWindowOpen;
            if (string.IsNullOrEmpty(hint) || covered)
            {
                if (view.Visible) view.Hide();
                m_ViewText = null;
                return;
            }
            bool sameText = view.Visible && string.Equals(hint, m_ViewText, StringComparison.Ordinal);
            bool sameTarget = m_TargetKind == m_ViewKind && string.Equals(m_TargetUi, m_ViewUi, StringComparison.Ordinal) &&
                m_TargetCells == m_ViewCells && m_TargetDim == m_ViewDim;
            if (sameText && sameTarget)
            {
                // Та же подсказка, цель на поле сдвинулась (котик, другой камень): двигается только указатель.
                bool movedTo = (m_TargetWorld - m_ViewWorld).sqrMagnitude > 1e-4f;
                bool movedFrom = (m_TargetFrom - m_ViewFrom).sqrMagnitude > 1e-4f;
                if (m_TargetKind == TargetKind.World && movedTo)
                {
                    m_ViewWorld = m_TargetWorld;
                    view.MoveWorld(m_TargetWorld);
                }
                else if (m_TargetKind == TargetKind.Drag && (movedTo || movedFrom))
                {
                    m_ViewWorld = m_TargetWorld;
                    m_ViewFrom = m_TargetFrom;
                    view.MoveDrag(m_TargetFrom, m_TargetWorld);
                }
                return;
            }
            m_ViewText = hint;
            m_ViewKind = m_TargetKind;
            m_ViewUi = m_TargetUi;
            m_ViewWorld = m_TargetWorld;
            m_ViewFrom = m_TargetFrom;
            m_ViewCells = m_TargetCells;
            m_ViewDim = m_TargetDim;
            view.Dim = m_TargetDim;
            if (m_TargetKind == TargetKind.World) view.ShowWorld(hint, m_TargetWorld, m_TargetCells);
            else if (m_TargetKind == TargetKind.Drag) view.ShowDrag(hint, m_TargetFrom, m_TargetWorld, m_TargetCells);
            else view.Show(hint, m_TargetKind == TargetKind.Ui ? view.FindTarget(m_TargetUi) : null);
        }

        // ── Цели подсказок: вызываются из Evaluate вместе с return и возвращают текст.

        /// <summary>Указатель на элемент UI; <paramref name="dim"/> — затемнить экран вокруг него (только визуально).</summary>
        string AtUi(string text, string target, bool dim = false)
        {
            m_TargetKind = TargetKind.Ui;
            m_TargetUi = target;
            m_TargetCells = Vector2.zero;
            m_TargetDim = dim;
            return text;
        }

        string AtObject(string text, int ordinal)
        {
            var board = m_Game.Board;
            var layout = m_Game.Session.Layout;
            if (board == null || ordinal < 0 || ordinal >= layout.Objects.Count) return text;
            var obj = layout.Objects[ordinal];
            m_TargetKind = TargetKind.World;
            m_TargetUi = null;
            m_TargetWorld = board.ObjectCenter(ordinal);
            m_TargetCells = new Vector2(obj.Width, obj.Depth);
            return text;
        }

        string AtCell(string text, int cell)
        {
            var board = m_Game.Board;
            if (board == null || cell < 0 || cell >= m_Game.Session.Layout.CellCount) return text;
            m_TargetKind = TargetKind.World;
            m_TargetUi = null;
            m_TargetWorld = board.CellCenter(cell);
            m_TargetCells = Vector2.one;
            return text;
        }

        /// <summary>Кучка добычи в клетке (середина кучки, как у эффектов сбора — BoardView.LootPos).</summary>
        string AtLoot(string text, int cell)
        {
            var board = m_Game.Board;
            if (board == null || cell < 0 || cell >= m_Game.Session.Layout.CellCount) return text;
            m_TargetKind = TargetKind.World;
            m_TargetUi = null;
            m_TargetWorld = board.LootPos(cell);
            m_TargetCells = Vector2.one;
            return text;
        }

        /// <summary>Жест переноса от клетки котика к объекту (рамка фокуса — по footprint объекта).</summary>
        string AtDrag(string text, int fromCell, int toOrdinal)
        {
            var board = m_Game.Board;
            var layout = m_Game.Session.Layout;
            if (board == null || fromCell < 0 || fromCell >= layout.CellCount || toOrdinal < 0 || toOrdinal >= layout.Objects.Count) return text;
            var obj = layout.Objects[toOrdinal];
            m_TargetKind = TargetKind.Drag;
            m_TargetUi = null;
            m_TargetFrom = board.CellCenter(fromCell);
            m_TargetWorld = board.ObjectCenter(toOrdinal);
            m_TargetCells = new Vector2(obj.Width, obj.Depth);
            return text;
        }

        /// <summary>Жест переноса котика на котика (объединение): от клетки к клетке, рамка фокуса — на получателе.</summary>
        string AtDragCells(string text, int fromCell, int toCell)
        {
            var board = m_Game.Board;
            int count = m_Game.Session.Layout.CellCount;
            if (board == null || fromCell < 0 || fromCell >= count || toCell < 0 || toCell >= count) return text;
            m_TargetKind = TargetKind.Drag;
            m_TargetUi = null;
            m_TargetFrom = board.CellCenter(fromCell);
            m_TargetWorld = board.CellCenter(toCell);
            m_TargetCells = Vector2.one;
            return text;
        }

        /// <summary>
        /// Шаг 5 (как в эталоне — «потратьте монеты на кузницу»): кузница не выбрана — лапка на кузнице; выбрана — лапка
        /// на «+1» её контекстной панели (ctx0, TutorialHint.TargetContextAction).
        /// </summary>
        string ForgeUpgradeHint(GameSession s)
        {
            int ord = s.Layout.Cart.Ordinal;
            if (m_Game.SelectedOrdinal == ord) return AtUi(Loc.T("tutorial.hint.upgrade_forge_spend"), TutorialHint.TargetContextAction);
            return AtObject(Loc.T("tutorial.hint.upgrade_forge"), ord);
        }

        /// <summary>
        /// Пара для объединения (шаг 4): два котика одного уровня. Получатель — котик в Idle (результат тоже Idle и его
        /// можно отправить на камень, шаг 4b); если оба свободны — тот, что ближе к камням (меньший row). false — пары нет.
        /// </summary>
        static bool FindMergePair(GameSession s, out int fromCell, out int toCell)
        {
            fromCell = toCell = -1;
            var cats = s.Mine.Cats;
            var layout = s.Layout;
            for (int i = 0; i < cats.Count; i++)
            {
                for (int j = i + 1; j < cats.Count; j++)
                {
                    var a = cats[i];
                    var b = cats[j];
                    if (a.Level != b.Level) continue;
                    bool aIdle = a.TargetIndex < 0, bIdle = b.TargetIndex < 0;
                    CatState receiver;
                    if (aIdle != bIdle) receiver = aIdle ? a : b;
                    else receiver = layout.Row(a.Cell) <= layout.Row(b.Cell) ? a : b;
                    var dragged = ReferenceEquals(receiver, a) ? b : a;
                    fromCell = dragged.Cell;
                    toCell = receiver.Cell;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Клетка первой бочки найма на поле (котик внутри ждёт тапа), иначе −1.</summary>
        static int HireBarrelCell(GameSession s)
        {
            var boxes = s.Mine.Containers;
            for (int i = 0; i < boxes.Count; i++) if (boxes[i].Kind == ContainerKind.Hire) return boxes[i].Cell;
            return -1;
        }

        /// <summary>
        /// Шаг t2: свободный котик и ближайший к нему камень, который можно бить (<see cref="GameSession.IsTargetAvailable"/>)
        /// и у которого есть свободная рабочая клетка (<see cref="GameSession.WorkCells"/>; своя клетка котика свободна —
        /// как в AcceptsCat, но без выделений памяти). false — котика или камня нет.
        /// </summary>
        bool FindDragTarget(GameSession s, out int catCell, out int rockOrdinal, int minLevel = 1)
        {
            catCell = -1;
            rockOrdinal = -1;
            CatState cat = null;
            var cats = s.Mine.Cats;
            for (int i = 0; i < cats.Count && cat == null; i++) if (cats[i].TargetIndex < 0 && cats[i].Level >= minLevel) cat = cats[i];
            if (cat == null) return false;
            var layout = s.Layout;
            int col = layout.Col(cat.Cell), row = layout.Row(cat.Cell);
            float best = float.MaxValue;
            for (int ord = 0; ord < layout.Objects.Count; ord++)
            {
                var o = layout.Objects[ord];
                if (o.Kind != ObjectKind.Rock || !s.IsTargetAvailable(ord)) continue;
                // Центр footprint: от якоря вправо (+столбец) и вверх (−строка).
                float dx = o.Col + (o.Width - 1) * 0.5f - col, dy = o.Row - (o.Depth - 1) * 0.5f - row;
                float d = dx * dx + dy * dy;
                if (d >= best || !HasFreeWorkCell(s, ord, cat.Id)) continue;
                best = d;
                rockOrdinal = ord;
            }
            if (rockOrdinal < 0) return false;
            catCell = cat.Cell;
            return true;
        }

        bool HasFreeWorkCell(GameSession s, int ordinal, int catId)
        {
            s.WorkCells(ordinal, m_Work);
            for (int i = 0; i < m_Work.Count; i++)
            {
                var c = s.CatAt(m_Work[i]);
                if (c == null || c.Id == catId) return true;
            }
            return false;
        }

        string Evaluate()
        {
            var s = m_Game.Session;
            var mine = s.Mine;
            var layout = s.Layout;
            int zone = mine.Zone;

            if (!ReferenceEquals(m_LegacySession, s))
            {
                // Старые сохранения: доставка уже открывалась до засчёта шага 9 по событию.
                m_LegacySession = s;
                if (!Done("t9") && s.Player.DeliveryHistory.ClaimTimes.Count > 0) Complete("t9");
            }

            // ── Засчёт шагов. Все проверки — до любой подсказки: уже выполненный шаг засчитывается сразу.
            int hireBarrel = HireBarrelCell(s);
            var loot = mine.Loot;
            int lootCount = loot != null ? loot.Count : 0;
            if (!Done("t1") && (mine.CatsHired > 0 || zone > 1)) Complete("t1");
            // Шаг 1b: бочек найма не осталось и котик на поле (или открытие бочки — OnEvent).
            if (!Done("t1b") && ((Done("t1") && hireBarrel < 0 && mine.Cats.Count > 0) || zone > 1)) Complete("t1b");
            bool anyAssigned = false;
            foreach (var c in mine.Cats) if (c.TargetIndex >= 0) anyAssigned = true;
            if (!Done("t2") && (anyAssigned || mine.RocksDestroyed > 0 || zone > 1)) Complete("t2");
            // Шаг 2b: добыча собрана; старые сохранения и камни без валютной награды — камень разбит, добычи на поле нет.
            if (!Done("t2b") && (mine.LootCollected > 0 || (mine.RocksDestroyed > 0 && lootCount == 0) || zone > 1)) Complete("t2b");
            if (!Done("t3") && (mine.CatsHired >= 2 || zone > 1)) Complete("t3");
            // Шаг 3b: вторая бочка найма открыта — бочек не осталось (или пара уже объединена).
            if (!Done("t3b") && ((Done("t3") && hireBarrel < 0) || mine.Merges > 0 || zone > 1)) Complete("t3b");
            // Шаги 4 и 5 — zone1+: только настоящее объединение или улучшение (в этой зоне — по счётчику и уровню кузницы).
            if (!Done("t4") && mine.Merges > 0) Complete("t4");
            // Шаг 4b: котик уровня 2+ (результат объединения) назначен на цель.
            if (!Done("t4b") && (zone > 1 || (Done("t4") && AnyMergedCatAssigned(s)))) Complete("t4b");
            var forge = mine.Objects[layout.Cart.Ordinal].Gen;
            if (!Done("t5") && forge != null && forge.Level > Math.Max(1, layout.Cart.Level)) Complete("t5");
            // Шаг 10 (t5b): котик назначен на дверь выхода, выход уже не цел, или зона > 1.
            var exitObj = layout.Exit;
            var exitStatus = mine.Objects[exitObj.Ordinal].Status;
            if (!Done("t5b") && (zone > 1 || exitStatus != ObjectStatus.Intact || AnyCatOn(s, exitObj.Index))) Complete("t5b");
            // Шаг t5a — одноразовая подсказка «Расчистите путь»: не нужна, когда к двери уже можно подойти (t5b) или зона пройдена.
            if (!Done("t5a") && (zone > 1 || Done("t5b"))) Complete("t5a");
            if (!Done("t6") && zone > 1) Complete("t6");
            if (!Done("t7") && (zone > 2 || (zone == 2 && ShaftPayoutAlreadyCollected(s)))) Complete("t7");
            if (!Done("t8") && (zone > 3 || (zone == 3 && AnyShaftAutomated(s)))) Complete("t8");
            // Одноразовые «Расчистите шахту» (t7a) и «Чертёж получен» (t8a) не нужны после своего шага.
            if (!Done("t7a") && (zone > 2 || Done("t7"))) Complete("t7a");
            if (!Done("t8a") && (zone > 3 || Done("t8"))) Complete("t8a");
            if (!Done("t10") && (zone > 5 || (zone == 5 && FirstGateOpened(s)))) Complete("t10");

            // ── Подсказки: по одной; покупки без ресурсов не предлагаются.
            // Бочка летит из пушки (ТЗ 7.2): подсказок нет до приземления — полёт виден целиком, а лапка «Нажмите на бочку»
            // показывает уже на стоящую бочку, а не на пустую клетку.
            if (m_Game.Board != null && m_Game.Board.AnyContainerFlying) return null;
            if (zone == 1)
            {
                // Шаг 1: экран затемнён вокруг «Нанять» (только визуально: тап по полю проходит).
                if (!Done("t1") && s.CanHire() == CommandResult.Ok) return AtUi(Loc.T("tutorial.hint.hire"), TutorialHint.TargetHire, true);
                if (!Done("t1b") && hireBarrel >= 0) return AtCell(Loc.T("tutorial.hint.open_barrel"), hireBarrel);
                if (!Done("t2") && FindDragTarget(s, out int catCell, out int rock)) return AtDrag(Loc.T("tutorial.hint.drag_to_rock"), catCell, rock);
                if (!Done("t2b") && lootCount > 0) return AtLoot(Loc.T("tutorial.hint.collect_loot"), loot[0].Cell);
                if (!Done("t3") && Done("t2b") && s.CanHire() == CommandResult.Ok) return AtUi(Loc.T("tutorial.hint.hire_second"), TutorialHint.TargetHire);
                // Шаг 3b: вторая бочка; дальше — любые бочки найма до первого объединения: котик в бочке пары не составит.
                if (!Done("t4") && hireBarrel >= 0) return AtCell(Loc.T("tutorial.hint.open_barrel"), hireBarrel);
                // Шаг 4: перенос котика на котика того же уровня.
                if (!Done("t4") && Done("t3b") && FindMergePair(s, out int fromCell, out int toCell))
                    return AtDragCells(Loc.T("tutorial.hint.merge"), fromCell, toCell);
                // Шаг 4b: объединённый котик копает быстрее — на ближайший камень.
                if (!Done("t4b") && Done("t4") && FindDragTarget(s, out int mergedCell, out int mergedRock, 2))
                    return AtDrag(Loc.T("tutorial.hint.merged_mine"), mergedCell, mergedRock);
                // Шаг 5: улучшение кузницы — после шага 4b (или если пары не было), только когда хватает золота.
                if (!Done("t5") && (Done("t4b") || !Done("t4")) && forge != null && mine.Gold >= s.UpgradeCost(layout.Cart.Ordinal, forge.Level))
                    return ForgeUpgradeHint(s);
            }
            string hint = ZoneStepHint(s, zone);
            if (hint != null) return hint;
            hint = ContainerHint(s, zone);
            if (hint != null) return hint;
            if (!Done("t4") && FindMergePair(s, out int pairFrom, out int pairTo)) return AtDragCells(Loc.T("tutorial.hint.merge"), pairFrom, pairTo);
            if (!Done("t5") && forge != null && mine.Gold >= s.UpgradeCost(layout.Cart.Ordinal, forge.Level)) return ForgeUpgradeHint(s);
            if (zone == 1 && !Done("t6")) return ExitHint(s);
            return null;
        }

        /// <summary>
        /// Шаги 10–11 zone1 (как в эталоне): путь расчищен — жест переноса котика к двери выхода; выход разрушен —
        /// «Отличная работа!» и лапка на «Следующая шахта». Пока к двери не подойти — «Расчистите путь к выходу».
        /// Котики уже бьют дверь — подсказки нет.
        /// </summary>
        string ExitHint(GameSession s)
        {
            var exit = s.Layout.Exit;
            var status = s.Mine.Objects[exit.Ordinal].Status;
            if (status == ObjectStatus.Broken)
                return s.CanCompleteZone(out _) ? AtUi(Loc.T("tutorial.hint.next_mine"), TutorialHint.TargetNextMine) : null;
            if (status != ObjectStatus.Intact || Done("t5b")) return null;
            if (s.IsTargetAvailable(exit.Ordinal) && FindCatForTarget(s, exit.Ordinal, out int catCell))
                return AtDrag(Loc.T("tutorial.hint.exit_door"), catCell, exit.Ordinal);
            // «Расчистите путь» — один раз, потом не мешает.
            return OnceAtObject("t5a", Loc.T("tutorial.hint.clear_exit"), exit.Ordinal);
        }

        /// <summary>
        /// Одноразовая подсказка <paramref name="id"/> у объекта: видна, пока не наберёт ≈5 с на экране или пока игрок не начнёт
        /// переносить котика; затем шаг засчитывается (сохраняется) и подсказка больше не появляется — null.
        /// </summary>
        string OnceAtObject(string id, string text, int ordinal)
        {
            if (Done(id)) return null;
            if (!string.Equals(m_OnceId, id, StringComparison.Ordinal))
            {
                m_OnceId = id;
                m_OnceShown = 0f;
            }
            var input = m_Game.Input;
            if (m_OnceShown >= OnceHintSeconds || (m_OnceShown > 0f && input != null && input.IsDraggingCat))
            {
                Complete(id);
                return null;
            }
            m_OnceOnScreen = true;
            return AtObject(text, ordinal);
        }

        /// <summary>
        /// Котик для переноса к цели: свободный (среди них — старший уровень), иначе старший из занятых другой целью;
        /// у цели должна быть свободная для него рабочая клетка. false — такого нет (путь ещё не расчищен).
        /// </summary>
        bool FindCatForTarget(GameSession s, int ordinal, out int catCell)
        {
            catCell = -1;
            int targetIndex = s.Layout.Objects[ordinal].Index;
            CatState best = null;
            bool bestIdle = false;
            foreach (var c in s.Mine.Cats)
            {
                if (c.TargetIndex == targetIndex) continue;
                bool idle = c.TargetIndex < 0;
                if (best == null || (idle && !bestIdle) || (idle == bestIdle && c.Level > best.Level))
                {
                    best = c;
                    bestIdle = idle;
                }
            }
            if (best == null || !HasFreeWorkCell(s, ordinal, best.Id)) return false;
            catCell = best.Cell;
            return true;
        }

        /// <summary>Хотя бы один котик назначен на объект с линейным индексом <paramref name="targetIndex"/>.</summary>
        static bool AnyCatOn(GameSession s, int targetIndex)
        {
            var cats = s.Mine.Cats;
            for (int i = 0; i < cats.Count; i++)
                if (cats[i].TargetIndex == targetIndex) return true;
            return false;
        }

        /// <summary>Подсказки шагов своей зоны: 7 (zone2), 8 (zone3), 10 (zone5).</summary>
        string ZoneStepHint(GameSession s, int zone)
        {
            var layout = s.Layout;
            var mine = s.Mine;
            if (zone == 2 && !Done("t7") && layout.Shafts.Count > 0)
            {
                var shaft = layout.Shafts[0];
                var st = mine.Objects[shaft.Ordinal];
                // «Расчистите шахту» — один раз (t7a), пока котики расчищают её — без подсказки.
                if (st.Status == ObjectStatus.Intact) return OnceAtObject("t7a", Loc.T("tutorial.hint.clear_shaft"), shaft.Ordinal);
                if (st.Status == ObjectStatus.ReadyToActivate) return AtObject(Loc.T("tutorial.hint.activate_shaft"), shaft.Ordinal);
                // Шаг засчитывается сбором (OnEvent), а не готовностью выплаты.
                if (st.Gen != null && st.Gen.PayoutReady && !st.Gen.Automated) return AtObject(Loc.T("tutorial.hint.collect_payout"), shaft.Ordinal);
            }
            if (zone == 3 && !Done("t8"))
            {
                // Путь ca003: чертёж шахты уже есть на нужном уровне — шахта станет автоматической сразу после активации.
                foreach (var sh in layout.Shafts)
                {
                    var st = mine.Objects[sh.Ordinal];
                    if (st.Status == ObjectStatus.Producing || s.CardLevel(sh.CardId) < sh.CardLevel) continue;
                    if (st.Status == ObjectStatus.Intact)
                    {
                        // «Чертёж получен — расчистите шахту» — один раз (t8a), дальше подсказка только к активации.
                        if (Done("t8a")) continue;
                        // Строка собирается один раз на шахту: проверка идёт каждые 0,25 с.
                        if (!ReferenceEquals(m_BlueprintHintShaft, sh.TypeId))
                        {
                            m_BlueprintHintShaft = sh.TypeId;
                            m_BlueprintHint = Loc.F("tutorial.hint.blueprint_ready", PresentationCatalog.ShaftName(sh.TypeId));
                        }
                        string once = OnceAtObject("t8a", m_BlueprintHint, sh.Ordinal);
                        if (once != null) return once;
                        continue;
                    }
                    if (st.Status == ObjectStatus.ReadyToActivate) return AtObject(Loc.T("tutorial.hint.activate_automated"), sh.Ordinal);
                }
                // Путь ca002: улучшение управляющего доступно (дубликаты и эликсир есть).
                foreach (var sh in layout.Shafts)
                {
                    var st = mine.Objects[sh.Ordinal];
                    if (st.Status == ObjectStatus.Producing && (st.Gen == null || !st.Gen.Automated) && s.CanUpgradeCard(sh.CardId) == CommandResult.Ok)
                        return AtUi(Loc.T("tutorial.hint.upgrade_manager"), TutorialHint.TargetBlueprints);
                }
            }
            if (zone == 5 && !Done("t10") && layout.Gates.Count > 0 && mine.Objects[layout.Gates[0].Ordinal].Status == ObjectStatus.Broken)
                return AtObject(Loc.T("tutorial.hint.open_gate"), layout.Gates[0].Ordinal);
            return null;
        }

        /// <summary>Шаг 11 — бочка бесплатного котика в любой зоне; шаг 9 — ящик доставки с zone4.</summary>
        string ContainerHint(GameSession s, int zone)
        {
            foreach (var c in s.Mine.Containers)
            {
                if (c.Kind == ContainerKind.FreeCat && !Done("t11")) return AtCell(Loc.T("tutorial.hint.free_cat"), c.Cell);
                if (c.Kind == ContainerKind.Delivery && zone >= 4 && !Done("t9")) return AtCell(Loc.T("tutorial.hint.delivery"), c.Cell);
            }
            return null;
        }

        /// <summary>
        /// Старые сохранения zone2: шахта уже работает, выплата не ждёт сбора, а в журнале начислений есть доход шахт
        /// (автоматизация в zone2 невозможна по данным, поэтому это ручной сбор).
        /// </summary>
        static bool ShaftPayoutAlreadyCollected(GameSession s)
        {
            if (s.Layout.Shafts.Count == 0) return false;
            var st = s.Mine.Objects[s.Layout.Shafts[0].Ordinal];
            if (st.Status != ObjectStatus.Producing || st.Gen == null || st.Gen.PayoutReady) return false;
            return s.Player.Ledger.TryGetValue("gold:shaft", out var gold) && gold.IsPositive;
        }

        static bool AnyShaftAutomated(GameSession s)
        {
            foreach (var sh in s.Layout.Shafts)
            {
                var gen = s.Mine.Objects[sh.Ordinal].Gen;
                if (gen != null && gen.Automated) return true;
            }
            return false;
        }

        static bool FirstGateOpened(GameSession s) =>
            s.Layout.Gates.Count > 0 && s.Mine.Objects[s.Layout.Gates[0].Ordinal].Status == ObjectStatus.Opened;

        /// <summary>Котик уровня 2+ (результат объединения) назначен на цель — шаг 4b выполнен.</summary>
        static bool AnyMergedCatAssigned(GameSession s)
        {
            var cats = s.Mine.Cats;
            for (int i = 0; i < cats.Count; i++)
                if (cats[i].Level >= 2 && cats[i].TargetIndex >= 0) return true;
            return false;
        }
    }
}
