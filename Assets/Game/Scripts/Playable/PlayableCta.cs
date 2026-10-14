using System;
using System.IO;
using System.Numerics;
using System.Text;
using CatMiners.Core.Outpost;
using CatMiners.Core.Save;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Vector2 = UnityEngine.Vector2;

namespace CatMiners.Playable
{
    /// <summary>
    /// Реклама в playable (Docs/Playable/PLAYABLE_TZ.md §3.5): свой Canvas поверх HUD (<see cref="SortingOffset"/> к его порядку,
    /// те же настройки CanvasScaler, своя safe area) — так баннер не уходит под окна HUD и затемнение перехода. Под баннером —
    /// тёмная полоса во всю ширину резерва (поле не просвечивает на высоких экранах). Баннер <see cref="PlayableBanner"/>
    /// въезжает через <see cref="BannerDelaySeconds"/> после запуска и виден всю игру; пока открыто окно HUD или окно награды
    /// квеста, он притушен (затемнение, без блеска). Финальный экран <see cref="PlayableEndCard"/> открывается, когда
    /// <see cref="PlayableDirector"/> сообщает о конце отрезка (30 минут или шахта 10), один раз на профиль: отметка «показан»
    /// хранится в файле статистики, поэтому перезагрузка вкладки после «Keep playing» его не повторяет, а «Play again» стирает.
    /// Пока экран открыт: автопереход к шахте выключен, Back закрывает его как «Keep playing», баннер, панель квеста, окно
    /// награды, подсказка и надписи скрыты. Золото за сессию — сумма доставленного по всем шахтам из состояния (идёт и в
    /// офлайне), хранится там же (CatOutpostV2/playable-stats-*.json). Время появления — часы контроллера (тесты с ManualClock).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayableCta : MonoBehaviour
    {
        /// <summary>Баннер въезжает через столько секунд после запуска (как у референса).</summary>
        public const double BannerDelaySeconds = 0.8;
        /// <summary>Canvas рекламы — на столько выше HUD (sortingOrder 30000).</summary>
        public const int SortingOffset = 1;
        const double StatsSaveInterval = 2;
        /// <summary>Полоса под баннером заходит за края safe area (вырез, полоса жестов).</summary>
        const float StripBleed = 1000f;
        /// <summary>Нижняя панель HUD стоит над резервом на столько (её собственное поле): полоса доходит до неё.</summary>
        const float HudBarLift = 2 * PixelUi.Px;
        const string StatsPrefix = "playable-stats-";

        [Serializable]
        sealed class StatsData
        {
            public int Version;
            public string Profile;
            public string RunId;
            public string RunDelivered;
            public string Delivered;
            /// <summary>Финальный экран уже показан этому профилю (после перезагрузки не повторяется).</summary>
            public bool EndCardShown;
        }

        OutpostController m_Game;
        PlayableDirector m_Director;
        Canvas m_Canvas;
        PlayableBanner m_Banner;
        PlayableEndCard m_EndCard;
        Image m_Strip;
        double m_BannerAt = double.NaN;
        bool m_EndPending;
        bool m_RestoreAutoContinue, m_RestoreBackPoll;

        // Золото за сессию.
        string m_StatsKey, m_StatsRun;
        BigInteger m_RunDelivered, m_Delivered;
        bool m_StatsDirty;
        double m_NextStatsSave;

        public Canvas Canvas => m_Canvas;
        public PlayableBanner Banner => m_Banner;
        public PlayableEndCard EndCard => m_EndCard;
        public bool EndCardOpen => m_EndCard != null && m_EndCard.Visible;
        /// <summary>Финальный экран уже показан этому профилю (отметка в файле статистики, переживает перезагрузку).</summary>
        public bool EndCardShown { get; private set; }
        /// <summary>Тёмная полоса под баннером во всю ширину резерва HUD.</summary>
        public Image Strip => m_Strip;
        /// <summary>Золото, доставленное за сессию playable (все шахты).</summary>
        public BigInteger GoldDelivered => m_Delivered;

        void Awake()
        {
            m_Game = GetComponent<OutpostController>();
            BindDirector();
        }

        void OnDestroy()
        {
            if (m_Director != null) m_Director.SegmentCompleted -= OnSegmentCompleted;
        }

        /// <summary>Режиссёр на контроллере (заменённый — тестом — подписывается заново).</summary>
        void BindDirector()
        {
            if (m_Director != null) return;
            m_Director = GetComponent<PlayableDirector>();
            if (m_Director != null) m_Director.SegmentCompleted += OnSegmentCompleted;
        }

        void Start()
        {
            if (m_Game == null || m_Game.Hud == null) return;
            var hudCanvas = m_Game.Hud.GetComponent<Canvas>();
            var hudScaler = m_Game.Hud.GetComponent<CanvasScaler>();
            var root = Ui.Rect(transform, "PlayableOverlay");
            m_Canvas = root.gameObject.AddComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.sortingOrder = (hudCanvas != null ? hudCanvas.sortingOrder : 30000) + SortingOffset;
            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            if (hudScaler != null)
            {
                scaler.uiScaleMode = hudScaler.uiScaleMode;
                scaler.referenceResolution = hudScaler.referenceResolution;
                scaler.screenMatchMode = hudScaler.screenMatchMode;
                scaler.matchWidthOrHeight = hudScaler.matchWidthOrHeight;
                scaler.referencePixelsPerUnit = hudScaler.referencePixelsPerUnit;
            }
            root.gameObject.AddComponent<GraphicRaycaster>();
            var safe = Ui.Rect(root, "SafeArea").Anchor(0, 0, 1, 1);
            safe.gameObject.AddComponent<SafeArea>();
            float height = OutpostHud.BottomReserve > 0f ? OutpostHud.BottomReserve : PlayableMode.BannerHeight;
            // Полоса во всю ширину резерва — за края safe area и вниз до края экрана; верх — у нижнего края панели HUD, которая
            // стоит на 2 пикселя арта выше резерва (поле в этом зазоре не просвечивает).
            m_Strip = PixelUi.Panel(safe, "CtaStrip", PixelPanel.Dark);
            m_Strip.raycastTarget = false;
            m_Strip.rectTransform.Anchor(0, 0, 1, 0, new Vector2(-StripBleed, -StripBleed), new Vector2(StripBleed, height + HudBarLift));
            m_Banner = new PlayableBanner(safe, height, OpenStore);
            m_EndCard = new PlayableEndCard(root, OpenStore, KeepPlaying, PlayAgain);
        }

        void OnSegmentCompleted() => m_EndPending = true;

        void Update()
        {
            if (m_Canvas == null || m_Game == null || m_Game.Clock == null) return;
            BindDirector();
            double now = m_Game.Clock.MonotonicSeconds;
            // Сначала статистика профиля (отметка «экран показан»), затем решение о показе.
            TrackDelivered(now);
            bool playing = m_Game.Phase == OutpostControllerPhase.Playing && m_Game.State != null;
            if (double.IsNaN(m_BannerAt) && playing) m_BannerAt = now + BannerDelaySeconds;
            if (m_EndPending && playing && m_StatsKey != null)
            {
                m_EndPending = false;
                if (!EndCardShown) ShowEndCard();
            }
            if (!m_Banner.Visible && !EndCardOpen && !double.IsNaN(m_BannerAt) && now >= m_BannerAt) m_Banner.Show();
            var quests = GetComponent<PlayableQuests>();
            m_Banner.SetDimmed(m_Game.Hud.AnyWindowOpen || quests != null && quests.PopupOpen);
            m_Banner.Tick(FrameTime.Delta);
            if (!EndCardOpen) return;
            m_EndCard.Relayout();
            if (BackPressedThisFrame()) KeepPlaying();
        }

        // ───────────── Финальный экран ─────────────

        /// <summary>
        /// Открыть финальный экран с итогами сессии: отметка «показан» сразу в файле профиля; баннер, панель квеста, окно
        /// награды, подсказка и надписи скрыты, автопереход к шахте и Back HUD выключены.
        /// </summary>
        public void ShowEndCard()
        {
            m_EndPending = false;
            if (m_EndCard == null || m_Game.State == null) return;
            TrackDelivered(m_Game.Clock.MonotonicSeconds);
            bool wasOpen = EndCardOpen;
            if (!EndCardShown)
            {
                EndCardShown = true;
                SaveStats(m_Game.Clock.MonotonicSeconds);
            }
            m_EndCard.Show(CollectStats());
            m_Banner.Hide();
            SetPlayUi(false);
            if (wasOpen) return;
            if (m_Director != null)
            {
                m_RestoreAutoContinue = m_Director.AutoContinue;
                m_Director.AutoContinue = false;
            }
            m_RestoreBackPoll = m_Game.Hud.PollBackButton;
            m_Game.Hud.PollBackButton = false;
            m_Game.Sound?.Chime();
        }

        /// <summary>«Keep playing»: экран закрыт, игра и автопереход идут дальше, баннер на месте.</summary>
        public void KeepPlaying()
        {
            if (!EndCardOpen) return;
            m_EndCard.Hide();
            if (m_Director != null) m_Director.AutoContinue = m_RestoreAutoContinue;
            m_Game.Hud.PollBackButton = m_RestoreBackPoll;
            SetPlayUi(true);
            m_Banner.Show(false);
        }

        /// <summary>Панель квеста, окно награды, подсказка и надписи playable — видны (игра) или скрыты (финальный экран).</summary>
        void SetPlayUi(bool on)
        {
            var quests = GetComponent<PlayableQuests>();
            if (quests != null) quests.Suppressed = !on;
            var juice = GetComponent<PlayableJuice>();
            if (juice != null) juice.Suppressed = !on;
        }

        /// <summary>«Play again»: новая игра с нуля (<see cref="PlayableDirector.Restart"/>).</summary>
        public void PlayAgain()
        {
            if (m_Director != null) m_Director.Restart();
        }

        static void OpenStore() => PlayableStore.Open();

        PlayableEndCard.Stats CollectStats()
        {
            var state = m_Game.State;
            var cards = m_Game.Configs != null ? m_Game.Configs.Cards : null;
            int owned = 0, total = 0;
            if (cards != null)
                foreach (var def in cards.Cards)
                {
                    total++;
                    if (state.Profile.CardLevel(def.Id) >= 1) owned++;
                }
            // Надетое по слотам (Pickaxe, Hat, Backpack, Boots): большая иконка предмета или null — пустой слот с замком.
            var gear = new string[OutpostIds.SlotCount];
            var equipment = m_Game.Configs != null ? m_Game.Configs.Equipment : null;
            var worn = state.Profile.Equipped;
            for (int i = 0; i < gear.Length; i++)
            {
                string id = worn != null && worn.Length > i ? worn[i] : null;
                if (id != null && equipment != null && equipment.TryGet(id, out var def) && state.Profile.ItemLevel(id) >= 1)
                    gear[i] = OutpostUiText.ItemIcon(def, true);
            }
            return new PlayableEndCard.Stats
            {
                Mines = state.Profile.CompletedZones != null ? state.Profile.CompletedZones.Count : 0,
                Cats = state.Run != null ? state.Run.Workers.Count : 0,
                Gold = m_Delivered,
                Cards = owned,
                CardsTotal = total,
                MinesTotal = OutpostIds.LastZone,
                Gear = gear,
            };
        }

        /// <summary>Back (Escape, действие UI/Cancel) — как у HUD, пока он сам Back не опрашивает.</summary>
        internal static bool BackPressedThisFrame()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
            var es = EventSystem.current;
            var module = es != null ? es.currentInputModule as InputSystemUIInputModule : null;
            var cancel = module != null && module.cancel != null ? module.cancel.action : null;
            return cancel != null && cancel.enabled && cancel.WasPressedThisFrame();
        }

        // ───────────── Золото за сессию ─────────────

        /// <summary>
        /// Прирост Counters.DeliveredValue текущей шахты добавляется к сумме; новая шахта (RunId) считается с нуля. Профиль
        /// сменился (debug, «Reset progress») — сумма из его файла.
        /// </summary>
        void TrackDelivered(double now)
        {
            var state = m_Game.State;
            if (state == null || state.Run == null || state.Profile == null) return;
            string key = (m_Game.IsDebugProfile ? "debug:" : "main:") + state.Profile.Seed;
            if (key != m_StatsKey)
            {
                m_StatsKey = key;
                LoadStats();
            }
            var run = state.Run;
            if (run.RunId != m_StatsRun)
            {
                m_StatsRun = run.RunId;
                m_RunDelivered = BigInteger.Zero;
                m_StatsDirty = true;
            }
            var delivered = run.Counters.DeliveredValue;
            if (delivered != m_RunDelivered)
            {
                if (delivered > m_RunDelivered) m_Delivered += delivered - m_RunDelivered;
                m_RunDelivered = delivered;
                m_StatsDirty = true;
            }
            if (m_StatsDirty && now >= m_NextStatsSave) SaveStats(now);
        }

        string StatsPath => string.IsNullOrEmpty(m_Game.SaveDirectory) ? null :
            Path.Combine(m_Game.SaveDirectory, OutpostSaveService.RootFolder, StatsPrefix + (m_Game.IsDebugProfile ? "debug" : "main") + ".json");

        void SaveStats(double now)
        {
            m_StatsDirty = false;
            m_NextStatsSave = now + StatsSaveInterval;
            string path = StatsPath;
            if (path == null) return;
            var data = new StatsData
            {
                Version = 1,
                Profile = m_Game.State.Profile.Seed.ToString(),
                RunId = m_StatsRun,
                RunDelivered = m_RunDelivered.ToString(),
                Delivered = m_Delivered.ToString(),
                EndCardShown = EndCardShown,
            };
            try
            {
                var fs = OutpostController.FileSystemOverride ?? new RealFileSystem();
                fs.CreateDirectory(Path.GetDirectoryName(path));
                fs.WriteAllBytesDurable(path, Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)));
            }
            catch (Exception e) { Debug.LogWarning("[Playable] Stats save failed: " + e.Message); }
        }

        /// <summary>Сумма и положение из файла профиля; другой профиль или повреждённый файл — с нуля.</summary>
        void LoadStats()
        {
            m_StatsRun = null;
            m_RunDelivered = m_Delivered = BigInteger.Zero;
            EndCardShown = false;
            string path = StatsPath;
            StatsData data = null;
            try
            {
                var fs = OutpostController.FileSystemOverride ?? new RealFileSystem();
                if (path != null && fs.Exists(path)) data = JsonUtility.FromJson<StatsData>(Encoding.UTF8.GetString(fs.ReadAllBytes(path)));
            }
            catch (Exception e) { Debug.LogWarning("[Playable] Stats ignored: " + e.Message); }
            if (data == null || data.Version != 1 || data.Profile != m_Game.State.Profile.Seed.ToString()) return;
            EndCardShown = data.EndCardShown;
            if (!BigInteger.TryParse(data.Delivered ?? "", out var total) || total.Sign < 0) return;
            if (!BigInteger.TryParse(data.RunDelivered ?? "", out var run) || run.Sign < 0) run = BigInteger.Zero;
            m_Delivered = total;
            m_StatsRun = data.RunId;
            m_RunDelivered = run;
        }
    }
}
