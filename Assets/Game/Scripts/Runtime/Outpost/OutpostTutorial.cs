using System;
using System.Collections.Generic;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Обучение режима CatOutpostV2 (ТЗ 15, RUNTIME_SPEC §6) на реальной первой шахте — десять шагов по фактическим событиям
    /// ядра (IOutpostGame.EventPresented) и состоянию, через существующий TutorialHint (пузырь наставника, лапка, рамка):
    /// (1) старт — автодобыча; (2) ручной удар — указатель на доступный камень, завершение по TargetStruck;
    /// (3) первый выпуск груза — сумка; (4) первая сдача — доставленное золото; (5) «Кирки» и рост урона команды;
    /// (6) найм; (7) автоматический выбор целей; (8) награда и экипировка; (9) сброс перед переходом; (10) сохранённый бонус.
    /// Шаги хранятся в Profile.Tutorial (Seen — id пройденных, Step — номер последнего, Dismissed — обучение закрыто) и
    /// записываются через IOutpostGame.SaveTutorialProgress. Подсказка не ловит касания и ничего не останавливает: добыча и
    /// доставка идут, обязательных кликов нет. Под окнами HUD подсказка скрыта, кроме шагов, указывающих в верхнее окно.
    /// Проверка шагов — 4 раза в секунду; указатель на движущегося котика двигается каждый кадр без выделений.
    /// </summary>
    public sealed class OutpostTutorial : IDisposable
    {
        public const string StepAuto = "o1_auto";
        public const string StepStrike = "o10_strike";
        public const string StepBag = "o2_bag";
        public const string StepGold = "o3_gold";
        public const string StepPickaxes = "o4_pickaxes";
        public const string StepTeam = "o5_team";
        public const string StepTargets = "o6_targets";
        public const string StepReward = "o7_reward";
        public const string StepReset = "o8_reset";
        public const string StepKept = "o9_kept";

        /// <summary>Шаги в порядке ТЗ 15 (номер шага = индекс + 1).</summary>
        public static readonly IReadOnlyList<string> Steps = new[]
            { StepAuto, StepStrike, StepBag, StepGold, StepPickaxes, StepTeam, StepTargets, StepReward, StepReset, StepKept };

        const float CheckInterval = 0.25f;
        /// <summary>Разовая подсказка остаётся на экране столько секунд (время под окнами не считается).</summary>
        const float OnceSeconds = 5.5f;
        /// <summary>Ручной удар предлагается 8 видимых секунд, затем обучение продолжает остальные подсказки.</summary>
        const float StrikePromptSeconds = 8f;
        /// <summary>Сообщение после покупки «Кирок».</summary>
        const float PickaxeMessageSeconds = 4.5f;

        enum TargetKind
        {
            None,
            Ui,
            Worker,
            Target,
        }

        readonly IOutpostGame m_Game;
        readonly OutpostHud m_Hud;
        int m_Frame = -1;
        float m_NextCheck, m_LastCheck = -1f;
        // События текущей сессии (после перезапуска шаги засчитываются и по состоянию ядра).
        int m_BagWorker = -1;
        bool m_Deposited, m_Destroyed;
        string m_PickaxeMessage;
        float m_PickaxeMessageUntil;
        // Разовая подсказка: id шага и накопленное время показа.
        string m_OnceId;
        float m_OnceShown;
        // Цель подсказки, выбранная Evaluate, и показанная.
        TargetKind m_Kind, m_ViewKind;
        string m_TargetUi, m_ViewUi, m_ViewText;
        int m_TargetWorker = -1, m_ViewWorker = -1;
        int m_StrikeTarget = -1, m_TargetRock = -1, m_ViewRock = -1;
        float m_StrikeShown;
        bool m_StrikeFocused;
        string m_StrikeRunId;
        Vector3 m_ViewWorld;
        readonly int[] m_LevelsBuf = new int[OutpostIds.BranchCount];
        string m_KeptText;
        bool m_KeptWithPickaxe;

        /// <summary>Обучение на HUD: подписывается на события контракта и подключается к HUD (HUD сам зовёт <see cref="Update"/>).</summary>
        public OutpostTutorial(IOutpostGame game, OutpostHud hud)
        {
            m_Game = game ?? throw new ArgumentNullException(nameof(game));
            m_Hud = hud;
            m_Game.EventPresented += OnEvent;
            if (hud != null) hud.AttachTutorial(this);
        }

        public void Dispose()
        {
            m_Game.EventPresented -= OnEvent;
        }

        /// <summary>Вид подсказок (создаёт HUD).</summary>
        public TutorialHint Hint => m_Hud != null ? m_Hud.Hint : null;

        /// <summary>Текст подсказки, которую обучение показывает сейчас (null — нет).</summary>
        public string CurrentHint { get; private set; }

        /// <summary>Камень, на который сейчас указывает подсказка ручного удара; −1, если подсказка не показана.</summary>
        public int StrikeTargetId => m_ViewKind == TargetKind.Target && Hint != null && Hint.Visible ? m_ViewRock : -1;

        TutorialState State
        {
            get
            {
                var p = m_Game.State != null ? m_Game.State.Profile : null;
                if (p == null) return null;
                if (p.Tutorial == null) p.Tutorial = new TutorialState();
                if (p.Tutorial.Seen == null) p.Tutorial.Seen = new List<string>();
                return p.Tutorial;
            }
        }

        /// <summary>Обучение включено (не закрыто игроком).</summary>
        public bool Enabled
        {
            get
            {
                var st = State;
                return st != null && !st.Dismissed;
            }
        }

        /// <summary>Обучение включено и пройдено не до конца.</summary>
        public bool Active
        {
            get
            {
                var st = State;
                return st != null && !st.Dismissed && st.Seen.Count < Steps.Count && !AllDone(st);
            }
        }

        static bool AllDone(TutorialState st)
        {
            for (int i = 0; i < Steps.Count; i++)
                if (!st.Seen.Contains(Steps[i])) return false;
            return true;
        }

        /// <summary>Шаг пройден.</summary>
        public bool IsDone(string id)
        {
            var st = State;
            return st != null && st.Seen.Contains(id);
        }

        /// <summary>«Пропустить обучение» (ТЗ 15): закрыть насовсем и записать.</summary>
        public void Skip() => SetEnabled(false);

        /// <summary>Включить или закрыть обучение (настройки). Пройденные шаги сохраняются.</summary>
        public void SetEnabled(bool on)
        {
            var st = State;
            if (st == null || st.Dismissed == !on) return;
            st.Dismissed = !on;
            m_Game.SaveTutorialProgress();
            if (!on) HideView();
            m_NextCheck = 0f;
        }

        void Complete(string id, bool save = true)
        {
            var st = State;
            if (st == null || st.Seen.Contains(id)) return;
            st.Seen.Add(id);
            for (int i = 0; i < Steps.Count; i++)
                if (Steps[i] == id && st.Step < i + 1) st.Step = i + 1;
            if (save) m_Game.SaveTutorialProgress();
        }

        /// <summary>События ядра: ручной удар, первый выпуск груза, сдача, разрушение, покупки и экипировка.</summary>
        void OnEvent(OutpostEvent e)
        {
            switch (e.Type)
            {
                case OutpostEventType.CargoReleased:
                    if (m_BagWorker < 0 && e.WorkerId > 0) m_BagWorker = e.WorkerId;
                    break;
                case OutpostEventType.CargoDeposited:
                    m_Deposited = true;
                    break;
                case OutpostEventType.TargetDestroyed:
                    m_Destroyed = true;
                    break;
                case OutpostEventType.TargetStruck:
                    if (Enabled && m_Game.Sim?.Run.Zone == 1 && e.Value.Sign > 0)
                    {
                        Complete(StepStrike);
                        m_NextCheck = 0f;
                    }
                    break;
                case OutpostEventType.TeamUpgradePurchased:
                    if (e.Branch == (int)UpgradeBranch.Pickaxes && Enabled && !IsDone(StepPickaxes)) OnPickaxesBought(e.Count);
                    break;
                case OutpostEventType.WorkerHired:
                    Complete(StepTeam);
                    break;
                case OutpostEventType.EquipmentChanged:
                    // Награда завершённой шахты: смена комплекта до завершения её не засчитывает.
                    var sim = m_Game.Sim;
                    if (sim != null && sim.Run != null && sim.Run.Phase == RunPhase.ReadyToContinue) Complete(StepReward);
                    break;
            }
        }

        /// <summary>После покупки «Кирок»: урон команды до и после — из TeamStats ядра.</summary>
        void OnPickaxesBought(int levels)
        {
            var sim = m_Game.Sim;
            Complete(StepPickaxes);
            if (sim == null) return;
            var run = sim.Run;
            for (int i = 0; i < m_LevelsBuf.Length; i++) m_LevelsBuf[i] = run.Levels[i];
            m_LevelsBuf[(int)UpgradeBranch.Pickaxes] = Mathf.Max(0, m_LevelsBuf[(int)UpgradeBranch.Pickaxes] - Mathf.Max(1, levels));
            var before = TeamStats.Compute(run.Balance, m_LevelsBuf, sim.Profile, m_Game.Configs != null ? m_Game.Configs.Equipment : null, run.Zone,
                m_Game.Configs?.Cards, run.ExtraWorkers);
            var after = TeamStats.Compute(run.Balance, run.Levels, sim.Profile, m_Game.Configs != null ? m_Game.Configs.Equipment : null, run.Zone,
                m_Game.Configs?.Cards, run.ExtraWorkers);
            m_PickaxeMessage = Loc.F("outpost.tutorial.pickaxes_done", OutpostUiText.Num(before.FinalDamage), OutpostUiText.Num(after.FinalDamage));
            m_PickaxeMessageUntil = Time.unscaledTime + PickaxeMessageSeconds;
            m_NextCheck = 0f;
        }

        /// <summary>Кадр обучения (HUD зовёт сам; повторный вызов в том же кадре ничего не делает).</summary>
        public void Update()
        {
            if (m_Frame == Time.frameCount) return;
            m_Frame = Time.frameCount;
            var hint = Hint;
            if (hint == null) return;
            // Окно открылось — подсказка прячется в том же кадре, если её цель не в этом окне.
            if (hint.Visible && Covered(hint)) HideView();
            float now = Time.unscaledTime;
            if (now < m_NextCheck)
            {
                FollowWorld(hint);
                return;
            }
            m_NextCheck = now + CheckInterval;
            float since = m_LastCheck < 0f ? 0f : Mathf.Min(1f, now - m_LastCheck);
            m_LastCheck = now;
            if (m_OnceId != null && hint.Visible) m_OnceShown += since;
            if (hint.Visible && m_ViewKind == TargetKind.Target) m_StrikeShown += since;
            m_Kind = TargetKind.None;
            m_TargetUi = null;
            m_TargetWorker = -1;
            m_TargetRock = -1;
            string text = Evaluate();
            CurrentHint = text;
            ShowOnView(hint, text);
        }

        bool Covered(TutorialHint hint)
        {
            if (m_Hud == null || !m_Hud.AnyWindowOpen) return false;
            return !(m_ViewKind == TargetKind.Ui && m_Hud.IsInTopWindow(hint.FindTarget(m_ViewUi)));
        }

        void HideView()
        {
            var hint = Hint;
            if (hint != null && hint.Visible) hint.Hide();
            m_ViewText = null;
            m_ViewKind = TargetKind.None;
        }

        string Evaluate()
        {
            var st = State;
            var sim = m_Game.Sim;
            if (st == null || st.Dismissed || sim == null || sim.Run == null) return null;
            if (m_Game.Phase != OutpostControllerPhase.Playing || m_Game.InTransition || (m_Hud != null && m_Hud.InTransition)) return null;
            var run = sim.Run;
            int zone = run.Zone;
            if (m_StrikeRunId != run.RunId)
            {
                m_StrikeRunId = run.RunId;
                m_StrikeTarget = -1;
                m_StrikeShown = 0f;
                m_StrikeFocused = false;
            }

            // Шаги первой шахты на следующих шахтах засчитываются молча (профиль начат не с начала или обучение было выключено).
            if (zone >= 2)
            {
                bool changed = false;
                for (int i = 0; i < Steps.Count - 1; i++)
                {
                    if (IsDone(Steps[i])) continue;
                    Complete(Steps[i], false);
                    changed = true;
                }
                if (changed) m_Game.SaveTutorialProgress();
            }
            if (zone >= 3 && !IsDone(StepKept)) Complete(StepKept);

            if (zone == 2)
            {
                if (IsDone(StepKept) || run.Phase != RunPhase.Running) return null;
                // Местные уровни снова 0, но постоянный бонус кирки уже в уроне команды.
                var p = sim.Profile;
                bool pickaxe = p.Equipped != null && p.Equipped.Length > (int)EquipSlot.Pickaxe && p.Equipped[(int)EquipSlot.Pickaxe] != null;
                // Текст собирается один раз на состояние (надета ли кирка), а не на каждой проверке.
                if (m_KeptText == null || m_KeptWithPickaxe != pickaxe)
                {
                    m_KeptWithPickaxe = pickaxe;
                    m_KeptText = pickaxe
                        ? Loc.F("outpost.tutorial.kept_bonus", OutpostUiText.Num(sim.Stats.FinalDamage), OutpostUiText.Num(sim.Stats.LocalDamage))
                        : Loc.T("outpost.tutorial.kept_bonus_equip");
                }
                return Once(StepKept, m_KeptText, pickaxe ? OutpostHud.TargetBase : OutpostHud.TargetEquipment);
            }
            if (zone != 1) return null;

            if (run.Phase == RunPhase.ReadyToContinue)
            {
                m_PickaxeMessage = null;
                if (!IsDone(StepReward))
                {
                    // Гарантированная кирка уже в коллекции — к «Экипировке» из итогов.
                    if (m_Hud != null && m_Hud.EquipmentOpen) Complete(StepReward);
                    else if (m_Hud != null && m_Hud.ResultsOpen) return AtUi(Loc.T("outpost.tutorial.reward"), OutpostHud.TargetResultsEquipment);
                    else return AtUi(Loc.T("outpost.tutorial.reward"), OutpostHud.TargetEquipment);
                }
                if (!IsDone(StepReset))
                {
                    // Перед переходом — что сбросится и что останется.
                    if (m_Hud != null && m_Hud.ResultsOpen) return Once(StepReset, Loc.T("outpost.tutorial.reset"), OutpostHud.TargetResultsNext);
                    if (m_Hud == null || !m_Hud.AnyWindowOpen) return Once(StepReset, Loc.T("outpost.tutorial.reset"), OutpostHud.TargetStripAction);
                }
                return null;
            }

            if (m_PickaxeMessage != null)
            {
                if (Time.unscaledTime < m_PickaxeMessageUntil) return m_PickaxeMessage;
                m_PickaxeMessage = null;
            }

            if (!IsDone(StepAuto))
            {
                var first = run.Workers.Count > 0 ? run.Workers[0].Id : -1;
                return OnceAtWorker(StepAuto, Loc.T("outpost.tutorial.auto"), first);
            }
            if (!IsDone(StepStrike) && run.Phase == RunPhase.Running && m_StrikeShown < StrikePromptSeconds)
            {
                m_StrikeTarget = FindStrikeTarget(sim);
                if (m_StrikeTarget >= 0)
                {
                    m_Kind = TargetKind.Target;
                    m_TargetRock = m_StrikeTarget;
                    return Loc.T("outpost.tutorial.strike");
                }
            }
            if (!IsDone(StepBag))
            {
                if (m_BagWorker < 0 || sim.WorkerById(m_BagWorker) == null) m_BagWorker = WorkerWithCargo(run);
                if (m_BagWorker < 0) return null;
                return OnceAtWorker(StepBag, Loc.T("outpost.tutorial.bag"), m_BagWorker);
            }
            if (!IsDone(StepGold))
            {
                if (!m_Deposited && run.TotalCredited.Sign <= 0) return null;
                return Once(StepGold, Loc.T("outpost.tutorial.gold"), OutpostHud.TargetGold);
            }
            if (!IsDone(StepTargets) && (m_Destroyed || run.Counters.ClearedTargets > 0))
                return Once(StepTargets, Loc.T("outpost.tutorial.auto_targets"), null);
            if (!IsDone(StepPickaxes))
            {
                if (run.Phase == RunPhase.Running && m_Game.Quote(UpgradeBranch.Pickaxes, BuyMode.One).CanBuy)
                {
                    if (m_Hud != null && m_Hud.BaseOpen) return AtUi(Loc.T("outpost.tutorial.pickaxes_buy"), OutpostHud.BranchTarget(UpgradeBranch.Pickaxes));
                    return AtUi(Loc.T("outpost.tutorial.pickaxes_open"), OutpostHud.TargetBase);
                }
                return null;
            }
            if (!IsDone(StepTeam) && run.Phase == RunPhase.Running && m_Game.Quote(UpgradeBranch.Team, BuyMode.One).CanBuy)
            {
                // Только пока найм по карману; потратили иначе — подсказка уходит и вернётся сама.
                if (m_Hud != null && m_Hud.BaseOpen) return AtUi(Loc.T("outpost.tutorial.team_buy"), OutpostHud.BranchTarget(UpgradeBranch.Team));
                return AtUi(Loc.T("outpost.tutorial.team_open"), OutpostHud.TargetBase);
            }
            return null;
        }

        int FindStrikeTarget(OutpostSimulation sim)
        {
            var camera = m_Hud != null && m_Hud.Rig != null ? m_Hud.Rig.Cam : Camera.main;
            float bottom = 0f, top = Screen.height;
            if (m_Hud != null) m_Hud.FieldBandPixels(out bottom, out top);
            int best = -1;
            float bestScore = float.MaxValue;
            int nearest = -1;
            float nearestScore = float.MaxValue;
            Vector3 nearestWorld = default;
            for (int i = 0; i < sim.Map.Targets.Length; i++)
            {
                var target = sim.Map.Targets[i];
                if (target.IsGate || !sim.CanStrike(target.Id) || !m_Game.TryGetTargetWorldPosition(target.Id, out var world)) continue;
                float distance = (world - m_Game.BaseWorldPosition).sqrMagnitude;
                if (distance < nearestScore)
                {
                    nearest = target.Id;
                    nearestScore = distance;
                    nearestWorld = world;
                }
                float score;
                if (camera != null)
                {
                    var screen = camera.WorldToScreenPoint(world);
                    if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width || screen.y < bottom || screen.y > top) continue;
                    // Сохраняем указатель на прежнем живом камне; при разрушении выбираем видимый рядом с центром поля.
                    if (target.Id == m_StrikeTarget) return target.Id;
                    score = new Vector2(screen.x - Screen.width * 0.5f, screen.y - (bottom + top) * 0.5f).sqrMagnitude;
                }
                else score = (world - m_Game.BaseWorldPosition).sqrMagnitude;
                if (score >= bestScore) continue;
                bestScore = score;
                best = target.Id;
            }
            // На старте камера может показывать только лагерь: один раз плавно подводим доступный камень к игроку.
            if (best < 0 && nearest >= 0 && !m_StrikeFocused && m_Hud?.Rig != null && !m_Hud.AnyWindowOpen)
            {
                m_StrikeFocused = true;
                m_Hud.Rig.FocusOnSmooth(new Vector2(nearestWorld.x, nearestWorld.z));
            }
            return best;
        }

        Vector2 TargetFootprint(int id)
        {
            var map = m_Game.Map;
            var target = map?.Target(id);
            if (target?.Cells == null || target.Cells.Length == 0) return Vector2.one;
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int i = 0; i < target.Cells.Length; i++)
            {
                int x = map.Col(target.Cells[i]), y = map.Row(target.Cells[i]);
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            return new Vector2(maxX - minX + 1, maxY - minY + 1);
        }

        static int WorkerWithCargo(OutpostRun run)
        {
            for (int i = 0; i < run.Workers.Count; i++)
                if (run.Workers[i].Bag.Count > 0) return run.Workers[i].Id;
            return -1;
        }

        string AtUi(string text, string target)
        {
            m_Kind = target != null ? TargetKind.Ui : TargetKind.None;
            m_TargetUi = target;
            return text;
        }

        /// <summary>Разовая подсказка: держится <see cref="OnceSeconds"/> с на экране, затем шаг засчитывается.</summary>
        string Once(string id, string text, string uiTarget)
        {
            if (!BeginOnce(id)) return null;
            return AtUi(text, uiTarget);
        }

        string OnceAtWorker(string id, string text, int workerId)
        {
            if (!BeginOnce(id)) return null;
            if (workerId > 0)
            {
                m_Kind = TargetKind.Worker;
                m_TargetWorker = workerId;
            }
            return text;
        }

        bool BeginOnce(string id)
        {
            if (m_OnceId != id)
            {
                m_OnceId = id;
                m_OnceShown = 0f;
            }
            if (m_OnceShown < OnceSeconds) return true;
            Complete(id);
            m_OnceId = null;
            m_NextCheck = 0f;
            return false;
        }

        void ShowOnView(TutorialHint hint, string text)
        {
            var uiTarget = m_Kind == TargetKind.Ui ? hint.FindTarget(m_TargetUi) : null;
            bool inTopWindow = uiTarget != null && m_Hud != null && m_Hud.IsInTopWindow(uiTarget);
            bool covered = m_Hud != null && m_Hud.AnyWindowOpen && !inTopWindow;
            Vector3 world = default;
            bool hasWorld = m_Kind == TargetKind.Worker ? m_Game.TryGetWorkerWorldPosition(m_TargetWorker, out world) :
                m_Kind == TargetKind.Target && m_Game.TryGetTargetWorldPosition(m_TargetRock, out world);
            if (string.IsNullOrEmpty(text) || covered)
            {
                HideView();
                return;
            }
            bool same = hint.Visible && string.Equals(text, m_ViewText, StringComparison.Ordinal) && m_Kind == m_ViewKind &&
                string.Equals(m_TargetUi, m_ViewUi, StringComparison.Ordinal) && m_TargetWorker == m_ViewWorker && m_TargetRock == m_ViewRock;
            if (same)
            {
                if (hasWorld && (world - m_ViewWorld).sqrMagnitude > 1e-4f)
                {
                    m_ViewWorld = world;
                    hint.MoveWorld(world);
                }
                return;
            }
            m_ViewText = text;
            m_ViewKind = m_Kind;
            m_ViewUi = m_TargetUi;
            m_ViewWorker = m_TargetWorker;
            m_ViewRock = m_TargetRock;
            m_ViewWorld = world;
            hint.Dim = false;
            if (hasWorld) hint.ShowWorld(text, world, m_Kind == TargetKind.Target ? TargetFootprint(m_TargetRock) : Vector2.one);
            else hint.Show(text, uiTarget);
            if (inTopWindow) m_Hud.BringHintToFront();
        }

        /// <summary>Указатель следует за объектом поля между проверками шагов (без перестройки текста).</summary>
        void FollowWorld(TutorialHint hint)
        {
            if (!hint.Visible) return;
            Vector3 world;
            bool found;
            if (m_ViewKind == TargetKind.Worker) found = m_Game.TryGetWorkerWorldPosition(m_ViewWorker, out world);
            else if (m_ViewKind == TargetKind.Target) found = m_Game.TryGetTargetWorldPosition(m_ViewRock, out world);
            else return;
            if (!found) return;
            if ((world - m_ViewWorld).sqrMagnitude <= 1e-4f) return;
            m_ViewWorld = world;
            hint.MoveWorld(world);
        }
    }
}
