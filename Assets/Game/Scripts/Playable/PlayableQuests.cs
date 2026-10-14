using System;
using System.IO;
using System.Numerics;
using System.Text;
using CatMiners.Core.Outpost;
using CatMiners.Core.Save;
using CatMiners.Core.Text;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace CatMiners.Playable
{
    /// <summary>
    /// Цепочка квестов playable (Docs/Playable/PLAYABLE_TZ.md §3.3, reference.md §1, §8 п. 1–2, 5): одна цель за раз из
    /// <see cref="PlayableQuestBook"/>, панель слева сверху (<see cref="PlayableQuestPanel"/>), реплика наставника 3,6 с (ждёт,
    /// пока виден тост HUD) и лапка (<see cref="PlayableGuide"/>). Прогресс — по состоянию игры (<see cref="PlayableQuestRules"/>),
    /// поэтому идёт и во время офлайна; тайм-скип засчитывается по его итогу (PendingSkip). Цель, выполненная в кадр
    /// завершения шахты, засчитывается до перехода к «Clear Mine». Выполненный квест открывает окно «QUEST COMPLETE!», когда
    /// поле свободно (окна HUD закрыты, финальный экран закрыт, шахта не завершена — награда за «Clear Mine» приходит в
    /// начале следующей): крупно монета и сумма на тёплом свечении, шаг «Tap rocks 5/5», золотая «CLAIM» через
    /// <see cref="ClaimDelaySeconds"/> (Back — то же, когда она готова). Награда — золото командой BonusGold
    /// (<see cref="OutpostController.GrantRewardGold"/>) с полётом монет; шахта завершилась при открытом окне — окно закрывается
    /// и откроется в следующей шахте (в завершённую шахту золото не платится). Следующий квест — через
    /// <see cref="NextQuestSeconds"/>. Квест прежней шахты, награда за который пришла в следующей, не отменяет «Clear Mine N»:
    /// после него «Clear Mine N» прежней шахты сразу выполнен и его окно открывается следом (обе награды — в новой шахте).
    /// Окно — окно игрока для автоперехода (<see cref="PlayableDirector"/>). Положение в цепочке
    /// хранится рядом с сохранением режима (CatOutpostV2/playable-quests-*.json): перезагрузка вкладки его не теряет, «Play
    /// again» стирает вместе с сохранением. <see cref="Suppressed"/> (финальный экран) прячет панель, окно и подсказку. Время —
    /// часы контроллера (тесты с ManualClock).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayableQuests : MonoBehaviour
    {
        /// <summary>«CLAIM» становится нажимаемой через столько секунд (защита от случайного тапа, как у референса).</summary>
        public const double ClaimDelaySeconds = 0.9;
        /// <summary>Следующий квест появляется через столько секунд после награды (у референса 0,6–1,2 с).</summary>
        public const double NextQuestSeconds = 0.9;
        /// <summary>Первый квест сессии въезжает через столько секунд после загрузки.</summary>
        public const double FirstQuestSeconds = 0.6;
        /// <summary>Реплика наставника в начале квеста видна столько секунд.</summary>
        public const double LineSeconds = 3.6;
        const double SaveInterval = 2, PossibleInterval = 0.5, PulseSeconds = 0.8;
        const string SavePrefix = "playable-quests-";
        const float Px = PixelUi.Px;
        /// <summary>Блок награды в окне: свечение fx_glow_warm ×2, монета ×2 и сумма FontTitle.</summary>
        const float RewardHeight = 48 * Px;

        [Serializable]
        sealed class SaveData
        {
            public int Version;
            public string Profile;
            public string RunId;
            public int Zone;
            public int Index;
            public bool Done;
            public string Base;
            public int Events;
            /// <summary>Цель счётчика выполненного квеста (панель после перезагрузки).</summary>
            public string Goal;
        }

        OutpostController m_Game;
        PlayableQuestPanel m_Panel;
        PlayableGuide m_Guide;
        OutpostDialog m_Popup;
        Ui.ButtonRefs m_Claim;
        PixelPop m_ClaimPop;
        RectTransform m_RewardRow;
        Image m_RewardCoin;
        TextMeshProUGUI m_RewardText;
        PixelPop m_RewardPop;
        OfflineSummary m_SeenSkip;
        bool m_Suppressed;

        // Положение в цепочке и прогресс.
        string m_ProfileKey;
        int m_Zone, m_Index;
        QuestDef m_Quest;
        string m_RunId;
        BigInteger m_Base, m_Progress, m_Goal;
        int m_Events;
        bool m_Done;

        // Показ.
        bool m_Revealed;
        string m_Line;
        double m_RevealAt, m_LineLeft, m_LastNow = double.NaN, m_PopupAt, m_NextPulse, m_NextPossible;
        /// <summary>Окно награды не раньше (следующее в очереди окно — после паузы, а не в кадр сбора прежнего).</summary>
        double m_PopupNotBefore = double.NegativeInfinity;
        /// <summary>Целей к расчистке в шахте цепочки (пока она идёт): счётчик «Clear Mine N», выданного уже в следующей шахте.</summary>
        BigInteger m_ZoneTargets;
        BigInteger m_Reward;
        bool m_Dirty;
        double m_NextSave;

        /// <summary>Квест начат (ещё не показан: панель въедет через NextQuestSeconds).</summary>
        public event Action<QuestDef> QuestStarted;
        /// <summary>Цель выполнена; окно награды откроется, когда поле свободно.</summary>
        public event Action<QuestDef> QuestCompleted;
        /// <summary>Награда забрана: квест и золото.</summary>
        public event Action<QuestDef, BigInteger> QuestClaimed;

        public QuestDef Current => m_Quest;
        /// <summary>Шахта цепочки текущего квеста и номер в ней.</summary>
        public int Zone => m_Zone;
        public int Index => m_Index;
        public BigInteger Progress => m_Progress;
        public BigInteger Goal => m_Goal;
        /// <summary>Цель выполнена, награда ещё не забрана.</summary>
        public bool CurrentDone => m_Done;
        /// <summary>Квест показан на панели (после задержки появления).</summary>
        public bool Revealed => m_Revealed;
        public bool PopupOpen => m_Popup != null && m_Popup.Visible;
        public bool ClaimReady => PopupOpen && m_Claim != null && m_Claim.Interactable;
        /// <summary>Золото в открытом окне награды.</summary>
        public BigInteger Reward => m_Reward;
        /// <summary>Подпись крупной награды окна («30»).</summary>
        public TextMeshProUGUI RewardText => m_RewardText;

        /// <summary>
        /// Финальный экран открыт: панель, окно награды и подсказка скрыты, окно не открывается (выполненный квест ждёт);
        /// прогресс считается дальше. Снятие — панель въезжает снова.
        /// </summary>
        public bool Suppressed
        {
            get => m_Suppressed;
            set
            {
                if (m_Suppressed == value) return;
                m_Suppressed = value;
                if (!value || m_Panel == null) return;
                HidePopup();
                m_Guide.Hide();
                m_Panel.Hide();
                if (m_Revealed)
                {
                    m_Revealed = false;
                    m_RevealAt = m_Game != null && m_Game.Clock != null ? m_Game.Clock.MonotonicSeconds : 0;
                }
            }
        }
        public Ui.ButtonRefs ClaimButton => m_Claim;
        public OutpostDialog Popup => m_Popup;
        public PlayableQuestPanel Panel => m_Panel;
        public PlayableGuide Guide => m_Guide;

        void Awake() => m_Game = GetComponent<OutpostController>();

        void Start()
        {
            if (m_Game == null || m_Game.Hud == null) return;
            var safe = (RectTransform)m_Game.Hud.transform.Find("SafeArea");
            float top = HudTopUnits(safe) + 2 * PixelUi.Px;
            m_Panel = new PlayableQuestPanel(safe, top);
            m_Guide = new PlayableGuide(m_Game, safe, HudBottomUnits(safe), top + PlayableQuestPanel.Height);
            m_Popup = OutpostDialog.Create(safe, "QuestComplete");
            BuildReward(m_Popup.UseExtra(RewardHeight));
            m_Game.EventPresented += OnEvent;
        }

        /// <summary>Крупная награда окна: тёплое свечение и ряд «монета ×2 + сумма» по центру, «поп» при каждом показе.</summary>
        void BuildReward(RectTransform extra)
        {
            var glow = PixelUi.Icon(extra, "glow", "fx_glow_warm", 2);
            glow.rectTransform.anchoredPosition = PlayableBanner.CenterOffset("fx_glow_warm", 2);
            m_RewardRow = Ui.Rect(extra, "reward");
            m_RewardRow.anchorMin = m_RewardRow.anchorMax = new Vector2(0.5f, 0.5f);
            m_RewardRow.pivot = new Vector2(0.5f, 0.5f);
            m_RewardRow.sizeDelta = new Vector2(100 * Px, 32 * Px);
            m_RewardCoin = PixelUi.Icon(m_RewardRow, "coin", "icon_coin", 2);
            m_RewardCoin.rectTransform.anchorMin = m_RewardCoin.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            m_RewardText = PixelUi.Text(m_RewardRow, "amount", "", PixelUi.FontTitle, PixelUi.TextAccent, TextAlignmentOptions.Left, true, true);
            m_RewardText.textWrappingMode = TextWrappingModes.NoWrap;
            m_RewardText.overflowMode = TextOverflowModes.Overflow;
            m_RewardPop = m_RewardRow.gameObject.AddComponent<PixelPop>();
            m_RewardPop.From = 0.6f;
            m_RewardPop.Duration = 0.25f;
        }

        /// <summary>Ряд награды по центру: монета ×2, зазор 3 пикселя арта, сумма; ширина — по тексту.</summary>
        void LayoutReward(string text)
        {
            Ui.SetText(m_RewardText, text);
            var coin = PixelUi.IconSize("icon_coin", 2);
            float gap = 3 * Px, textW = Mathf.Ceil((m_RewardText.GetPreferredValues(text).x + 0.5f) / Px) * Px;
            float width = coin.x + gap + textW;
            m_RewardRow.sizeDelta = new Vector2(width, coin.y);
            m_RewardCoin.rectTransform.anchoredPosition = new Vector2(coin.x * 0.5f, 0f) + PlayableBanner.CenterOffset("icon_coin", 2);
            m_RewardText.rectTransform.Anchor(0, 0, 0, 1, new Vector2(coin.x + gap, Px), new Vector2(width, Px));
            m_RewardPop.Play();
        }

        void OnDestroy()
        {
            if (m_Game != null) m_Game.EventPresented -= OnEvent;
        }

        /// <summary>Нижний край верхней полосы HUD от верха safe area.</summary>
        static float HudTopUnits(RectTransform safe)
        {
            var bar = safe.Find("TopBar") as RectTransform;
            return bar != null ? -bar.offsetMin.y : 32 * PixelUi.Px;
        }

        /// <summary>Верхний край нижней части HUD (панель и полоса над ней) от низа safe area.</summary>
        static float HudBottomUnits(RectTransform safe)
        {
            var bottom = safe.Find("Bottom") as RectTransform;
            return bottom != null ? bottom.offsetMax.y : 100 * PixelUi.Px;
        }

        void Update()
        {
            if (m_Game == null || m_Game.Hud == null || m_Game.Clock == null || m_Panel == null) return;
            double now = m_Game.Clock.MonotonicSeconds;
            double dt = double.IsNaN(m_LastNow) ? 0 : Math.Max(0, now - m_LastNow);
            m_LastNow = now;
            var hud = m_Game.Hud;
            bool live = m_Game.Sim != null && m_Game.Phase == OutpostControllerPhase.Playing && !m_Game.InTransition && !hud.InTransition;
            if (live)
            {
                if (m_Game.State.Run.Zone == m_Zone && m_Game.Sim.Map != null) m_ZoneTargets = Math.Max(m_Game.Sim.Map.RequiredCount, 1);
                EnsureLoaded(now);
                CountSkip();
                Evaluate(now);
                if (!m_Suppressed) UpdatePopup(now);
            }
            if (m_Suppressed)
            {
                m_Guide.Hide();
                m_Panel.Hide();
            }
            else UpdateView(now, dt, live);
            m_Panel.Tick(FrameTime.Delta);
            if (PopupOpen)
            {
                m_Popup.Relayout();
                // Back у окна награды — «CLAIM», когда она готова (до этого Back поглощается: окно требует решения).
                if (ClaimReady && PlayableCta.BackPressedThisFrame()) Claim();
            }
            if (m_Dirty && now >= m_NextSave) Save(now);
        }

        /// <summary>
        /// Тайм-скип засчитывается по своему итогу (PendingSkip с минутами скипа), а не по запасу: покупка скипа в магазине
        /// не отменяет использование. Итог, который был до начала квеста, не считается.
        /// </summary>
        void CountSkip()
        {
            var skip = m_Game.PendingSkip;
            if (skip == null || ReferenceEquals(skip, m_SeenSkip)) return;
            m_SeenSkip = skip;
            if (m_Quest == null || m_Done || m_Quest.Kind != QuestKind.Skip || skip.SkipMinutes <= 0) return;
            m_Events++;
            m_Dirty = true;
        }

        // ───────────── Цепочка ─────────────

        /// <summary>Профиль загружен или сменился (debug-профиль, «Reset progress»): положение в цепочке — из его файла или с начала шахты.</summary>
        void EnsureLoaded(double now)
        {
            var profile = m_Game.State.Profile;
            string key = (m_Game.IsDebugProfile ? "debug:" : "main:") + profile.Seed;
            if (key == m_ProfileKey) return;
            m_ProfileKey = key;
            HidePopup();
            m_Guide.Hide();
            m_Panel.Hide();
            if (!TryLoad(now)) Begin(m_Game.State.Run.Zone, 0, now, FirstQuestSeconds);
        }

        /// <summary>
        /// Начать квест <paramref name="index"/> цепочки шахты <paramref name="zone"/>, пропуская недостижимые; на завершённой
        /// шахте — сразу «Clear Mine». Опорное значение снимается сейчас, панель въедет через <paramref name="delay"/>.
        /// </summary>
        void Begin(int zone, int index, double now, double delay)
        {
            var run = m_Game.State.Run;
            var list = PlayableQuestBook.ForMine(zone);
            index = Mathf.Clamp(index, 0, list.Count - 1);
            if (run.Phase == RunPhase.ReadyToContinue && zone == run.Zone) index = list.Count - 1;
            while (index < list.Count - 1 && !PlayableQuestRules.IsAvailable(list[index], m_Game)) index++;
            m_Zone = zone;
            m_Index = index;
            m_Quest = list[index];
            m_RunId = run.RunId;
            m_Done = false;
            m_Events = 0;
            m_Base = PlayableQuestRules.Baseline(m_Quest, m_Game);
            PlayableQuestRules.Measure(m_Quest, m_Game, m_Base, m_Events, out m_Progress, out m_Goal);
            m_Revealed = false;
            m_RevealAt = now + delay;
            m_Line = Loc.F(m_Quest.LineKey, m_Quest.Goal);
            m_LineLeft = LineSeconds;
            m_NextPossible = now + PossibleInterval;
            m_SeenSkip = m_Game.PendingSkip;
            m_Panel.Hide();
            m_Guide.Hide();
            Save(now);
            QuestStarted?.Invoke(m_Quest);
        }

        void Evaluate(double now)
        {
            if (m_Quest == null || m_Done) return;
            var run = m_Game.State.Run;
            // Другая шахта (переход без «Clear Mine», загрузка шахты в debug): цепочка этой шахты с начала.
            if (run.Zone != m_Zone)
            {
                Begin(run.Zone, 0, now, NextQuestSeconds);
                return;
            }
            if (run.RunId != m_RunId)
            {
                m_RunId = run.RunId;
                m_Base = PlayableQuestRules.Baseline(m_Quest, m_Game);
                m_Events = 0;
                m_Dirty = true;
            }
            // Сначала прогресс: цель, выполненная в кадр завершения шахты (тайм-скип дочистил шахту), засчитывается.
            BigInteger before = m_Progress, goal = m_Goal;
            PlayableQuestRules.Measure(m_Quest, m_Game, m_Base, m_Events, out m_Progress, out m_Goal);
            if (m_Revealed && (m_Progress != before || m_Goal != goal)) m_Panel.SetCounter(CounterText(), m_Progress > before);
            bool complete = m_Quest.Kind == QuestKind.Clear ? run.Phase == RunPhase.ReadyToContinue : m_Progress >= m_Goal;
            if (complete)
            {
                Complete(now);
                return;
            }
            // Шахта пройдена раньше цели: незавершённый квест уступает место «Clear Mine» этой шахты.
            if (run.Phase == RunPhase.ReadyToContinue && m_Quest.Kind != QuestKind.Clear)
            {
                Begin(m_Zone, PlayableQuestBook.ForMine(m_Zone).Count - 1, now, 0);
                return;
            }
            if (now < m_NextPossible) return;
            m_NextPossible = now + PossibleInterval;
            if (!PlayableQuestRules.StillPossible(m_Quest, m_Game, m_Progress)) Begin(m_Zone, m_Index + 1, now, 0);
        }

        void Complete(double now)
        {
            m_Done = true;
            m_Progress = m_Goal;
            if (m_Revealed)
            {
                m_Panel.SetCounter(CounterText(), true);
                m_Panel.SetChecked(true);
            }
            m_Guide.Hide();
            m_Game.Sound?.Chime();
            Save(now);
            QuestCompleted?.Invoke(m_Quest);
        }

        /// <summary>Засчитать событие квеста (удар, улучшение карточки, сундук магазина).</summary>
        void OnEvent(OutpostEvent e)
        {
            if (m_Quest == null || m_Done) return;
            bool counts;
            switch (m_Quest.Kind)
            {
                case QuestKind.Strike: counts = e.Type == OutpostEventType.TargetStruck && e.Value.Sign > 0; break;
                case QuestKind.Card: counts = e.Type == OutpostEventType.CardUpgraded; break;
                case QuestKind.Chest: counts = e.Type == OutpostEventType.ChestOpened && e.TargetId == OutpostIds.StoreSource; break;
                default: counts = false; break;
            }
            if (!counts) return;
            m_Events++;
            m_Dirty = true;
        }

        // ───────────── Окно награды ─────────────

        void UpdatePopup(double now)
        {
            if (!m_Done) return;
            bool finished = m_Game.State.Run.Phase == RunPhase.ReadyToContinue;
            if (!PopupOpen)
            {
                // Поле свободно: окна HUD и финальный экран закрыты, шахта идёт (награда за «Clear Mine» — уже в следующей шахте).
                var cta = GetComponent<PlayableCta>();
                if (finished || now < m_PopupNotBefore || m_Game.Hud.AnyWindowOpen || m_Game.PendingSkip != null || cta != null && cta.EndCardOpen) return;
                ShowPopup(now);
                return;
            }
            // Шахта завершилась при открытом окне: золото в неё не платится — окно откроется в следующей шахте (награда — по
            // её ценам).
            if (finished)
            {
                HidePopup();
                return;
            }
            bool ready = now - m_PopupAt >= ClaimDelaySeconds - 1e-6;
            if (ready != m_Claim.Interactable)
            {
                m_Claim.SetInteractable(ready);
                m_NextPulse = now;
            }
            if (ready && now >= m_NextPulse)
            {
                m_NextPulse = now + PulseSeconds;
                m_ClaimPop.Play();
            }
        }

        /// <summary>
        /// «QUEST COMPLETE!»: тело — выполненный шаг со счётчиком («Tap rocks 5/5», у «Clear Mine» — без счётчика), под ним
        /// крупная награда (монета и сумма на свечении, «поп»), золотая «CLAIM» (сбор — главное действие окна; зелёный — магазин).
        /// </summary>
        void ShowPopup(double now)
        {
            m_Reward = PlayableQuestRules.RewardFor(m_Quest, NextForReward(), m_Game);
            string done = m_Quest.Kind == QuestKind.Clear ? StepText() : Loc.F("playable.quest.done", StepText(), CounterText());
            m_Popup.Begin(Loc.T("playable.quest.complete"), null, done);
            m_Claim = m_Popup.SetButton(0, Loc.T("playable.quest.claim"), PixelButton.Gold, Claim);
            if (m_ClaimPop == null)
            {
                m_ClaimPop = m_Claim.Rect.gameObject.AddComponent<PixelPop>();
                m_ClaimPop.Duration = 0.2f;
            }
            m_Popup.Show();
            LayoutReward(Loc.F("playable.quest.reward", OutpostUiText.Money(m_Reward)));
            m_Claim.SetInteractable(false);
            m_PopupAt = now;
            m_Guide.Hide();
        }

        /// <summary>Следующий квест цепочки — ориентир награды (его покупку награда и оплачивает); после «Clear Mine» — первый квест новой шахты.</summary>
        QuestDef NextForReward()
        {
            if (m_Quest.Kind == QuestKind.Clear)
            {
                int zone = m_Game.State.Run.Zone != m_Zone ? m_Game.State.Run.Zone : m_Zone + 1;
                return PlayableQuestBook.ForMine(zone)[0];
            }
            var list = PlayableQuestBook.ForMine(m_Zone);
            return m_Index + 1 < list.Count ? list[m_Index + 1] : null;
        }

        /// <summary>
        /// «CLAIM»: золото в кошелёк шахты (BonusGold, сразу запись), монеты летят из окна к кошельку, через
        /// <see cref="NextQuestSeconds"/> — следующий квест. До <see cref="ClaimDelaySeconds"/> нажатие не действует.
        /// </summary>
        public void Claim()
        {
            if (!PopupOpen || m_Game == null) return;
            double now = m_Game.Clock.MonotonicSeconds;
            if (now - m_PopupAt < ClaimDelaySeconds - 1e-6) return;
            // Шахта уже завершена: золото пропало бы — окно откроется в следующей шахте.
            if (m_Game.State.Run.Phase == RunPhase.ReadyToContinue)
            {
                HidePopup();
                return;
            }
            var result = m_Game.GrantRewardGold(m_Reward);
            // «save»: золото уже в состоянии (запись не удалась — HUD предложит повтор), второй раз не начисляем.
            if (!result.Ok && result.Reason != "save") return;
            var coin = m_RewardCoin != null ? m_RewardCoin.rectTransform : null;
            if (coin != null) FlyCoins(coin);
            HidePopup();
            m_Game.Sound?.Payout();
            var claimed = m_Quest;
            var reward = m_Reward;
            var run = m_Game.State.Run;
            if (m_Quest.Kind == QuestKind.Clear) Begin(run.Zone, 0, now, NextQuestSeconds);
            // Награда квеста прежней шахты пришла в новой: «Clear Mine» прежней шахты тоже заработан — его окно следом.
            else if (run.Zone != m_Zone && MineCleared(m_Zone)) BeginClearDone(m_Zone, now);
            else if (run.Zone != m_Zone) Begin(run.Zone, 0, now, NextQuestSeconds);
            else Begin(m_Zone, m_Index + 1, now, NextQuestSeconds);
            QuestClaimed?.Invoke(claimed, reward);
        }

        /// <summary>Шахта <paramref name="zone"/> пройдена в этом профиле (не просто сменена загрузкой шахты в debug).</summary>
        bool MineCleared(int zone)
        {
            var done = m_Game.State.Profile.CompletedZones;
            return done != null && done.Contains(zone);
        }

        /// <summary>
        /// «Clear Mine <paramref name="zone"/>» уже выполнен (шахта пройдена, пока ждала награда другого квеста): панель въедет
        /// с отметкой, окно награды откроется через <see cref="NextQuestSeconds"/>, его «CLAIM» начнёт цепочку текущей шахты.
        /// </summary>
        void BeginClearDone(int zone, double now)
        {
            Begin(zone, PlayableQuestBook.ForMine(zone).Count - 1, now, NextQuestSeconds);
            // Счётчик — по прежней шахте («56/56»), а не по целям текущей.
            if (m_ZoneTargets.Sign > 0) m_Goal = m_ZoneTargets;
            m_PopupNotBefore = now + NextQuestSeconds;
            Complete(now);
        }

        void HidePopup()
        {
            if (m_Popup != null && m_Popup.Visible) m_Popup.Hide();
        }

        /// <summary>Монеты из точки окна к кошельку: экранная точка → мировая перед камерой поля (полёт HUD считает обратно).</summary>
        void FlyCoins(RectTransform from)
        {
            var hud = m_Game.Hud;
            var cam = m_Game.Rig != null && m_Game.Rig.Cam != null ? m_Game.Rig.Cam : Camera.main;
            if (cam == null) return;
            var canvas = hud.GetComponent<Canvas>();
            var uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCam, from.position);
            var world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, cam.nearClipPlane + 1f));
            hud.FlyCoins(world, 6);
        }

        // ───────────── Панель и подсказка ─────────────

        void UpdateView(double now, double dt, bool live)
        {
            if (m_Quest == null) return;
            if (!m_Revealed && live && now >= m_RevealAt)
            {
                m_Revealed = true;
                m_Panel.Show(IconOf(m_Quest), Loc.T(m_Quest.TitleKey), StepText(), CounterText());
                m_Panel.SetChecked(m_Done);
            }
            if (!m_Revealed || !live)
            {
                m_Guide.Hide();
                return;
            }
            // Реплика ждёт, пока виден тост HUD (сообщение основной игры): одна инструкция за раз, её 3,6 с не идут.
            string line = !m_Done && m_LineLeft > 0 && !m_Guide.ToastVisible ? m_Line : null;
            if (m_Guide.Update(m_Quest, m_Done, m_Progress, line, PopupOpen || m_Done, now)) m_LineLeft -= dt;
        }

        string StepText() => Loc.F(m_Quest.StepKey, m_Quest.Kind == QuestKind.Clear ? m_Zone : m_Quest.Goal);

        string CounterText()
        {
            bool money = m_Quest.Kind == QuestKind.Deliver;
            return Loc.F("playable.quest.counter", money ? OutpostUiText.Money(m_Progress) : m_Progress.ToString(),
                money ? OutpostUiText.Money(m_Goal) : m_Goal.ToString());
        }

        static string IconOf(QuestDef q) => !string.IsNullOrEmpty(q.Icon) && PixelArt.Has(q.Icon) ? q.Icon : "icon_star";

        // ───────────── Сохранение ─────────────

        string SavePath => string.IsNullOrEmpty(m_Game.SaveDirectory) ? null :
            Path.Combine(m_Game.SaveDirectory, OutpostSaveService.RootFolder, SavePrefix + (m_Game.IsDebugProfile ? "debug" : "main") + ".json");

        void Save(double now)
        {
            m_Dirty = false;
            m_NextSave = now + SaveInterval;
            string path = SavePath;
            if (path == null || m_Quest == null || m_Game.State == null) return;
            var data = new SaveData
            {
                Version = 1,
                Profile = m_Game.State.Profile.Seed.ToString(),
                RunId = m_RunId,
                Zone = m_Zone,
                Index = m_Index,
                Done = m_Done,
                Base = m_Base.ToString(),
                Events = m_Events,
                Goal = m_Goal.ToString(),
            };
            try
            {
                var fs = OutpostController.FileSystemOverride ?? new RealFileSystem();
                fs.CreateDirectory(Path.GetDirectoryName(path));
                fs.WriteAllBytesDurable(path, Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)));
            }
            catch (Exception e) { Debug.LogWarning("[Playable] Quest save failed: " + e.Message); }
        }

        /// <summary>
        /// Положение из файла профиля: начатый квест этой шахты продолжается (в другом run — с новым опорным значением),
        /// выполненный ждёт награды и в следующей шахте; иначе (другой профиль, прежняя шахта, повреждённый файл) — false.
        /// </summary>
        bool TryLoad(double now)
        {
            string path = SavePath;
            SaveData data = null;
            try
            {
                var fs = OutpostController.FileSystemOverride ?? new RealFileSystem();
                if (path != null && fs.Exists(path)) data = JsonUtility.FromJson<SaveData>(Encoding.UTF8.GetString(fs.ReadAllBytes(path)));
            }
            catch (Exception e) { Debug.LogWarning("[Playable] Quest state ignored: " + e.Message); }
            var run = m_Game.State.Run;
            if (data == null || data.Version != 1 || data.Profile != m_Game.State.Profile.Seed.ToString() || data.Zone < 1) return false;
            var list = PlayableQuestBook.ForMine(data.Zone);
            if (data.Index < 0 || data.Index >= list.Count || data.Zone > run.Zone || (!data.Done && data.Zone != run.Zone)) return false;
            if (!BigInteger.TryParse(data.Base ?? "0", out var baseline)) baseline = BigInteger.Zero;
            m_Zone = data.Zone;
            m_Index = data.Index;
            m_Quest = list[data.Index];
            m_Done = data.Done;
            m_RunId = data.RunId;
            m_Base = baseline;
            m_Events = Math.Max(0, data.Events);
            if (!m_Done && m_RunId != run.RunId)
            {
                m_RunId = run.RunId;
                m_Base = PlayableQuestRules.Baseline(m_Quest, m_Game);
                m_Events = 0;
            }
            if (!m_Done) PlayableQuestRules.Measure(m_Quest, m_Game, m_Base, m_Events, out m_Progress, out m_Goal);
            else
            {
                if (!BigInteger.TryParse(data.Goal ?? "", out m_Goal) || m_Goal.Sign <= 0) m_Goal = BigInteger.One;
                m_Progress = m_Goal;
            }
            m_Revealed = false;
            m_RevealAt = now + FirstQuestSeconds;
            m_Line = Loc.F(m_Quest.LineKey, m_Quest.Goal);
            m_LineLeft = LineSeconds;
            m_NextPossible = now + PossibleInterval;
            m_SeenSkip = m_Game.PendingSkip;
            return true;
        }
    }
}
