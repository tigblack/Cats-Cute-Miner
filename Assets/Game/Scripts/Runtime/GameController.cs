using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Связывает симуляцию с Unity: время, события, сохранение, команды UI. Экономика живёт только в GameSession.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        public const double SaveIntervalSeconds = 2.0;
        /// <summary>Кадровый dt ограничен 1 с; больший промежуток с прошлого кадра обрабатывается один раз как офлайн (ТЗ 15.2).</summary>
        public const double MaxFrameSeconds = 1.0;
        /// <summary>Повторное нажатие причины перехода за это время ведёт камеру к следующей неактивированной шахте (ТЗ 11.2).</summary>
        public const float ShaftCycleSeconds = 4f;
        /// <summary>Старый ключ PlayerPrefs звука: переносится в настройки игрока один раз (ТЗ 11.3).</summary>
        const string LegacySoundPref = "catminers.sound";

        /// <summary>Каталог сохранений для тестов: тесты не должны трогать persistentDataPath (ТЗ 12.2).</summary>
        public static string SaveDirectoryOverride;

        /// <summary>Часы для тестов (ТЗ 15.2): UTC и монотонное время сеанса. null — системные часы <see cref="SystemClock"/>.</summary>
        public static IClock ClockOverride;

        /// <summary>
        /// Для тестов, написанных до бочек найма и добычи: true — сессии идут по прежним правилам ядра (наём сразу ставит котика,
        /// награда камня начисляется сразу). В игре false: см. <see cref="SetSession"/>.
        /// </summary>
        public static bool ClassicRulesOverride;

        /// <summary>«+N» и полёт к счётчику HUD для добычи, собранной в одном кадре: автосбор многих куч сразу показывает только первые.</summary>
        const int LootPresentPerFrame = 6;

        public BalanceRepository Balance { get; private set; }
        public GameSession Session { get; private set; }
        public BoardView Board { get; private set; }
        public CameraRig Rig { get; private set; }
        public Hud Hud { get; private set; }
        public PointerInput Input { get; private set; }
        public Effects Fx { get; private set; }
        public Sfx Sound { get; private set; }
        /// <summary>Фоновая музыка (два трека, плавная смена) и атмосфера шахты: живут всю игру, окна их не перезапускают.</summary>
        public MusicPlayer Music { get; private set; }
        public Ambience AmbienceSound { get; private set; }
        public TutorialController Tutorial { get; private set; }
        /// <summary>Часы контроллера (ТЗ 15.2): всё «сейчас» берётся отсюда. Задаются в Init из <see cref="ClockOverride"/>.</summary>
        public IClock Clock { get; private set; }
        /// <summary>
        /// Раздача опубликованных событий подписчикам (ТЗ 16): будущие награды города подключаются отдельным
        /// обработчиком завершения зоны через <see cref="SimEventBus.Register"/>.
        /// </summary>
        public SimEventBus Bus { get; } = new SimEventBus();

        public int SelectedOrdinal { get; private set; } = -1;
        /// <summary>
        /// Идёт анимация перехода в следующую шахту (ТЗ 11.2): CompleteZone уже выполнен и записан, на экране — старая карта,
        /// котики уходят в штольню. Ввод заблокирован, HUD заморожен, события новой зоны ждут конца анимации.
        /// </summary>
        public bool InTransition { get; private set; }
        public int SelectedCatId { get; private set; }
        /// <summary>Ускорение инструментов разработки; действует только в слоте Debug (ТЗ 15.2).</summary>
        public double SimSpeed { get; set; } = 1.0;
        public bool SoundOn => Sound != null && Sound.Enabled;
        /// <summary>Исходный токен и стабильный ID в контекстной панели — инструмент разработки: только Editor/Development и слот Debug (ТЗ 17).</summary>
        public bool ShowDebugInfo => (Application.isEditor || Debug.isDebugBuild) && Session != null && Session.IsDebugSlot;
        public float UiScale => Mathf.Max(0.5f, Screen.height / 1920f);
        public string SaveDirectory { get; private set; }

        /// <summary>Вызывается после смены сессии (смена слота инструментами разработки).</summary>
        public event Action SessionChanged;

        SaveService m_Saves;
        bool m_Dirty;
        double m_SaveTimer;
        double m_LastMono;
        int m_IdleCursor;
        string m_FatalError;
        // Сохранение запрещено, пока игрок не подтвердил новую игру после повреждённого или несовместимого файла (ТЗ 12.2).
        bool m_SaveBlocked;
        // Показана панель сундука из очереди неоткрытых (ТЗ 10.3).
        bool m_ChestShowing;
        bool m_Draining;
        // Состояние шахты, для которого построено поле: только его позицию камеры можно записывать (ТЗ 12.1).
        MineProgressState m_BoardMine;
        // Перебор неактивированных шахт по повторному нажатию причины перехода.
        int m_ShaftCursor, m_ShaftRefRow;
        float m_ShaftCycleUntil;
        // Выбор, для которого камера уже решала, поднимать ли его над контекстной панелью.
        int m_RevealOrd = -1, m_RevealCat;
        readonly List<SimEvent> m_Batch = new List<SimEvent>(64);
        readonly List<int> m_ShaftOrder = new List<int>();
        // Индекс показываемого события в m_Batch: награда сбора добычи ищет своё событие LootCollected рядом с собой.
        int m_PresentIndex = -1;
        int m_LootFloatFrame = -1, m_LootFloatCount;
        int m_LootFlyFrame = -1, m_LootFlyCount;

        /// <summary>Текущее UTC, мс (совместимость): по <see cref="ClockOverride"/>, если он задан, иначе системное.</summary>
        public static long UtcNowMs() => ClockOverride != null ? ClockOverride.UtcNowMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public void Init(GameBootstrap boot)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            throw new System.InvalidOperationException("Legacy simulation is disabled on Android; start Outpost through GameBootstrap.");
#endif
            Clock = ClockOverride ?? new SystemClock();
            // Диагностика точки расширения ТЗ 16: в Editor и Development Build каждое завершение зоны пишется в журнал.
            if (Application.isEditor || Debug.isDebugBuild) Bus.Register(new ZoneCompletionLog());
            Materials.Init(boot.LitMaterial, boot.UnlitMaterial);
            PixelArt.Init(boot.SpriteMaterial, boot.UnlitMaterial);
            EnsureSceneBasics();
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
            }
            Rig = CameraRig.Create(cam);
            Fx = Effects.Create(transform, boot.ParticleMaterial);
            // Звук включается по настройкам игрока при выборе сессии (SetSession).
            Sound = Sfx.Create(transform);
            Music = MusicPlayer.Create(transform);
            AmbienceSound = Ambience.Create(transform);
            Board = new GameObject("Board").AddComponent<BoardView>();
            Board.transform.SetParent(transform, false);
            Board.ContainerLanded += OnContainerLanded;
            Hud = Hud.Create(this);
            Hud.OnChestsBadgeTap = ShowPendingChests;
            Input = gameObject.AddComponent<PointerInput>();
            Input.Init(this);
            Tutorial = new TutorialController(this);

            try
            {
                if (boot.BalanceJson == null) throw new BalanceLoadException("В сцене не назначен json-data.json (запустите Cat Miners/Setup Project).");
                Balance = BalanceRepository.Load(boot.BalanceJson.bytes);
            }
            catch (Exception e)
            {
                m_FatalError = e.Message;
                Debug.LogError("[CatMiners] Баланс не загружен: " + e.Message);
                Hud.Modal(Loc.T("controller.config_error.title"), e.Message);
                return;
            }

            SaveDirectory = string.IsNullOrEmpty(SaveDirectoryOverride) ? Application.persistentDataPath : SaveDirectoryOverride;
            m_Saves = new SaveService(SaveDirectory);
            LoadSlot(SaveSlots.Main);
        }

        void EnsureSceneBasics()
        {
            if (FindFirstObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("Directional Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.color = new Color(1f, 0.93f, 0.82f);
                light.shadows = LightShadows.Soft;
                lightGo.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.55f);
            var es = FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                var esGo = new GameObject("EventSystem");
                es = esGo.AddComponent<EventSystem>();
            }
            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module == null) module = es.gameObject.AddComponent<InputSystemUIInputModule>();
            if (module.actionsAsset == null) module.AssignDefaultActions();
        }

        // ───────────── Слоты и загрузка ─────────────

        public void LoadSlot(string slot)
        {
            long now = Clock.UtcNowMs;
            m_SaveBlocked = false;
            var result = m_Saves.Load(slot, Balance.Sha256);
            GameSession session = null;
            string notice = null;
            string restartText = null;
            bool restartUnblocks = false;
            switch (result.Status)
            {
                case LoadStatus.Loaded:
                case LoadStatus.LoadedFromBackup:
                case LoadStatus.RulesChangedMineReset:
                    try
                    {
                        if (result.Status == LoadStatus.RulesChangedMineReset)
                        {
                            int oldRules = result.State.RulesVersion;
                            session = RestoreWithMineReset(result.State, slot, now);
                            Debug.LogWarning("[CatMiners] rulesVersion " + oldRules + " -> " + Formula.RulesVersion + ": zone " + session.Mine.Zone +
                                             " reset, PlayerProgressState kept");
                            notice = Loc.T("controller.save.rules_changed");
                        }
                        else
                        {
                            session = GameSession.FromState(Balance, result.State);
                            if (result.Status == LoadStatus.LoadedFromBackup) notice = Loc.T("controller.save.restored_from_backup");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[CatMiners] Сохранение не подходит к карте: " + e.Message);
                        session = null;
                        notice = null;
                        // Файл не удаляется и не перезаписывается: сначала он откладывается как *.corrupt-* (ТЗ 12.2).
                        bool kept = QuarantineSlotFiles(slot, result.SourcePath);
                        m_SaveBlocked = true;
                        restartUnblocks = kept;
                        restartText = RestartText(Loc.T("controller.save.map_mismatch"), kept, "*.corrupt-*");
                    }
                    break;
                case LoadStatus.IncompatibleBalance:
                {
                    // ТЗ 12.1: файл другого баланса не загружается и откладывается как *.incompatible-*; игроку предлагается новая игра.
                    bool kept = SlotFilesGone(slot);
                    m_SaveBlocked = true;
                    restartUnblocks = kept;
                    restartText = RestartText((string.IsNullOrEmpty(result.Message) ? Loc.T("controller.save.other_balance") : result.Message) + ".",
                        kept, "*.incompatible-*");
                    break;
                }
                case LoadStatus.Corrupted:
                {
                    // ТЗ 12.2: валидного файла нет — диалог «Начать заново» без уничтожения повреждённых файлов.
                    bool kept = SlotFilesGone(slot);
                    m_SaveBlocked = true;
                    restartUnblocks = kept;
                    restartText = RestartText(Loc.T("controller.save.corrupted"), kept, "*.corrupt-*");
                    break;
                }
            }
            // Новая игра создаётся в памяти; при m_SaveBlocked она не пишется, пока игрок не нажмёт «Начать заново».
            if (session == null) session = GameSession.NewGame(Balance, (ulong)now ^ 0x9E3779B97F4A7C15UL, slot, now);
            SetSession(session, true);

            var offline = OfflineProgress.Apply(Session, now);
            // События офлайна при загрузке презентацией не проигрываются: их итог — окно «Пока вас не было» (ТЗ 12.3).
            // Шине ТЗ 16 раздавать нечего: офлайн не активирует шахты, не открывает ворота и не завершает зону, поэтому событий
            // MineCompleted, CheckpointOpened и MineShaftActivated здесь не бывает. Отложенная проверка автоматизации выполнится
            // первым онлайн-шагом в Update, и её ShaftAutomated дойдёт до DrainEvents.
            Session.Events.Clear();
            SaveNow();
            if (restartText != null) OfferRestart(restartText, restartUnblocks);
            if (notice != null) Hud.Modal(Loc.T("controller.save.title"), notice, new Hud.ModalButton { Label = Loc.T("controller.save.ok") });
            // Окно без порога длительности: показывается, если есть что показать (ТЗ 12.3). Сундуки офлайна ждут
            // в очереди неоткрытых и открываются тапом по «Сундуки: N» после окна, без повторного броска (ТЗ 10.3).
            if (offline.HasNews) Hud.ShowOffline(offline);
            // ТЗ 11.4: после конца кампании Play открывает финальный экран; zone1 не запускается.
            if (Session.Player.CampaignCompleted) ShowCampaignEnd();
            // Загрузка не засчитывается как время кадра.
            m_LastMono = Clock.MonotonicSeconds;
        }

        /// <summary>
        /// rulesVersion изменился (ТЗ 12.1): MineProgressState текущей зоны сбрасывается к началу зоны, PlayerProgressState сохраняется.
        /// Старое состояние шахты могло быть построено по другой геометрии и не пройти проверку картой, поэтому оно заменяется
        /// заготовкой той же зоны по текущим данным ещё до FromState; затем RestartCurrentZone строит зону заново.
        /// </summary>
        GameSession RestoreWithMineReset(GameState state, string slot, long now)
        {
            int zone = state.Mine != null ? state.Mine.Zone : state.Player.CurrentZone;
            state.Mine = GameSession.NewGame(Balance, 1UL, slot, now, zone).State.Mine;
            var session = GameSession.FromState(Balance, state);
            session.RestartCurrentZone();
            return session;
        }

        /// <summary>
        /// Отложить файлы слота, не удаляя их (ТЗ 12.2): источник — *.corrupt-*, остальные main/tmp/bak — *.leftover-*, чтобы первые
        /// записи новой игры их не перезаписали. true — все файлы отложены.
        /// </summary>
        bool QuarantineSlotFiles(string slot, string source)
        {
            bool ok = true;
            if (!string.IsNullOrEmpty(source) && File.Exists(source) && m_Saves.Quarantine(source, "corrupt") == null) ok = false;
            string main = m_Saves.MainPath(slot);
            foreach (var path in new[] { main, main + ".tmp", main + ".bak" })
                if (File.Exists(path) && m_Saves.Quarantine(path, "leftover") == null) ok = false;
            return ok;
        }

        /// <summary>Файлов слота (main, tmp, bak) на месте не осталось: SaveService их отложил, новая запись ничего не затрёт.</summary>
        bool SlotFilesGone(string slot)
        {
            string main = m_Saves.MainPath(slot);
            return !File.Exists(main) && !File.Exists(main + ".tmp") && !File.Exists(main + ".bak");
        }

        static string RestartText(string reason, bool kept, string pattern)
        {
            return reason + "\n" + (kept
                ? Loc.F("controller.save.files_kept", pattern)
                : Loc.T("controller.save.files_not_kept"));
        }

        /// <summary>Диалог «Начать заново» (ТЗ 12.2): только после него новая игра пишется в слот (если старые файлы отложены).</summary>
        void OfferRestart(string text, bool unblock)
        {
            var s = Session;
            Hud.Modal(Loc.T("controller.save.title"), text, new Hud.ModalButton
            {
                Label = Loc.T("controller.save.restart"),
                Color = Palette.UiGreen,
                Action = () =>
                {
                    if (!unblock || !ReferenceEquals(Session, s)) return;
                    m_SaveBlocked = false;
                    SaveNow();
                },
            });
        }

        void SetSession(GameSession session, bool restoreCamera)
        {
            // Перенос, начатый в прежней сессии, отменяется с возвратом; вид переноса сбрасывается (ТЗ 7.3).
            Input?.CancelAll();
            ClearDragView();
            // Правила настоящей игры (обучение по образцу Gold & Goblins): нанятый котик приезжает в бочке, награда камня
            // выпадает добычей и ждёт тапа (автосбор — через LootAutoCollectSeconds, в том числе офлайн), котик, разбивший
            // камень, сам берётся за следующий (офлайн — нет, ТЗ 12.3). В ядре переключатели по умолчанию выключены: тесты
            // EditMode и сценарный бот играют по прежним правилам. Включаются до офлайна LoadSlot — добыча, пролежавшая
            // офлайн, собирается по тем же правилам.
            session.BarrelHires = !ClassicRulesOverride;
            session.LootPickups = !ClassicRulesOverride;
            session.AutoMining = !ClassicRulesOverride;
            Session = session;
            // Ускорение — только в слоте Debug (ТЗ 15.2, 17): Main всегда идёт ×1.
            if (!session.IsDebugSlot) SimSpeed = 1.0;
            SelectedOrdinal = -1;
            SelectedCatId = 0;
            m_ChestShowing = false;
            m_ShaftCycleUntil = 0f;
            m_IdleCursor = 0;
            ApplyPlayerSettings();
            RebuildBoard(restoreCamera);
            SessionChanged?.Invoke();
        }

        /// <summary>
        /// Настройки из PlayerProgressState (ТЗ 11.3, 13.1). Старое значение звука из PlayerPrefs переносится в сохранение один раз.
        /// </summary>
        void ApplyPlayerSettings()
        {
            if (PlayerPrefs.HasKey(LegacySoundPref))
            {
                Session.SetSoundOn(PlayerPrefs.GetInt(LegacySoundPref, 1) == 1);
                PlayerPrefs.DeleteKey(LegacySoundPref);
                PlayerPrefs.Save();
                MarkDirty();
            }
            var settings = Session.Player.Settings;
            if (Sound != null) Sound.Enabled = settings == null || settings.SoundOn;
            // Группы громкости, «меньше эффектов».
            AudioMix.Apply(settings);
            VisualSettings.ReduceEffects = settings != null && settings.ReduceEffects;
        }

        /// <summary>Музыка оформления текущей шахты (смена — плавная; тот же трек продолжает играть).</summary>
        void ApplyZoneMusic()
        {
            if (Music != null && Session != null) Music.SetPreferred(PresentationCatalog.MusicOf(PresentationCatalog.ThemeOf(Session.Mine.Zone)));
        }

        /// <summary>Громкость группы звука из окна настроек: сохраняется в настройках игрока и сразу применяется.</summary>
        public void SetVolume(AudioGroup group, float value)
        {
            if (Session == null) return;
            Session.SetVolume(group, value);
            MarkDirty();
            AudioMix.Apply(Session.Player.Settings);
        }

        /// <summary>Уменьшение эффектов (меньше частиц, без импульса камеры): настройка игрока, только вид.</summary>
        public void SetReduceEffects(bool on)
        {
            if (Session == null) return;
            Session.SetReduceEffects(on);
            MarkDirty();
            VisualSettings.ReduceEffects = on;
        }

        /// <summary>Перестроить поле; камера — на стартовую площадку (новая зона, инструменты разработки).</summary>
        public void RebuildBoard() => RebuildBoard(false);

        /// <summary>
        /// Перестроить поле. restoreCamera — загрузка: камера встаёт в сохранённую позицию (ТЗ 13.2), если она задана;
        /// иначе — на стартовую площадку.
        /// </summary>
        public void RebuildBoard(bool restoreCamera)
        {
            Board.Fx = Fx;
            Board.Build(Session);
            // Фон за стенами — цвет глубины оформления шахты (тот же, что у затемнения закрытых секторов).
            if (Rig != null && Rig.Cam != null) Rig.Cam.backgroundColor = PixelTheme.Dark;
            ApplyZoneMusic();
            m_BoardMine = Session.Mine;
            var layout = Session.Layout;
            float saved = Session.Mine.CameraFocusZ;
            bool restore = restoreCamera && saved >= 0f;
            Rig.SetUiInsets(Hud.TopInset, Hud.BottomInset);
            Rig.SetBoard(layout.Width, layout.Depth, restore ? saved : layout.Depth - layout.Cart.Row - 1f);
            // «Домашний» вид: зона мержа и кузница внизу полосы над нижней панелью HUD, впереди видны камни.
            if (!restore) Rig.FocusOn(Rig.HomeFocusZ(Session));
            // Инструменты могут вернуть сессию из конца кампании в игру (DebugGotoZone): финальный экран убирается.
            if (!Session.Player.CampaignCompleted) Hud.HideCampaignEnd();
        }

        /// <summary>Переключить слот (инструменты разработки): Main ↔ Debug.</summary>
        public void SwitchSlot(string slot, bool fresh, int zone = 1)
        {
            if (Session != null) SaveNow();
            if (fresh)
            {
                long now = Clock.UtcNowMs;
                m_SaveBlocked = false;
                SetSession(GameSession.NewGame(Balance, (ulong)now, slot, now, zone), false);
                SaveNow();
                m_LastMono = Clock.MonotonicSeconds;
            }
            else LoadSlot(slot);
            Hud.Toast(slot == SaveSlots.Debug ? Loc.T("controller.slot.debug") : Loc.T("controller.slot.main"));
        }

        // ───────────── Цикл ─────────────

        void Update()
        {
            if (Session == null || m_FatalError != null) return;
            double mono = Clock.MonotonicSeconds;
            double realDt = Math.Max(0.0, mono - m_LastMono);
            m_LastMono = mono;
            long now = Clock.UtcNowMs;
            long gapMs = now - Session.State.LastProcessedUtcMs;
            if (realDt > MaxFrameSeconds || gapMs > 1000)
            {
                // Пауза, сворачивание, точка останова: весь промежуток обрабатывается один раз как офлайн по UTC-метке
                // (ТЗ 15.2, 12.3, лимит 8 ч). Сырой кадровый dt в Advance не идёт: пока UTC позади метки, прогресс не начисляется.
                Input?.CancelAll();
                int offlineStart = Session.Events.Count;
                var summary = OfflineProgress.Apply(Session, now);
                // Как при загрузке: события офлайна презентацией не проигрываются (без залпа звуков, эффектов и «+N»), их итог —
                // окно «Пока вас не было» (ТЗ 12.3). Поставленное в очередь до офлайна остаётся и разбирается ниже.
                Session.Events.RemoveRange(offlineStart, Session.Events.Count - offlineStart);
                SaveNow();
                if (summary.HasNews) Hud.ShowOffline(summary);
            }
            else
            {
                double speed = Session.IsDebugSlot ? SimSpeed : 1.0;
                Session.Advance(GameSession.SecondsToTicks(Math.Min(realDt, MaxFrameSeconds) * speed));
                OfflineProgress.MarkProcessed(Session, now);
            }
            // Во время перехода модель уже в новой зоне, а поле показывает старую: события и вид — после анимации.
            if (!InTransition)
            {
                DrainEvents();
                Board.SelectedCatId = SelectedCatId;
                Board.Sync(Time.deltaTime);
                UpdateContextCamera();
                Tutorial.Update();
            }

            m_SaveTimer += realDt;
            if (m_Dirty && m_SaveTimer >= SaveIntervalSeconds) SaveNow();
        }

        /// <summary>
        /// Транзакции, после которых нужна немедленная запись (ТЗ 12.2): покупка, объединение, сундук, активация шахты, ворота,
        /// создание содержимого доставки или контейнера (в том числе пропуск рекламной доставки), CompleteZone.
        /// </summary>
        static bool IsCommitEvent(SimEventType type)
        {
            switch (type)
            {
                case SimEventType.MineCompleted:
                case SimEventType.CheckpointOpened:
                case SimEventType.MineShaftActivated:
                case SimEventType.ChestOpened:
                case SimEventType.CardUpgraded:
                case SimEventType.ContainerOpened:
                case SimEventType.PayoutCollected:
                case SimEventType.CatSpawned:
                case SimEventType.CatMerged:
                case SimEventType.GeneratorUpgraded:
                case SimEventType.DynamiteUsed:
                case SimEventType.ContainerSpawned:
                case SimEventType.DeliverySkippedAd:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Пачка событий после транзакции (ТЗ 16): сначала запись, затем раздача подписчикам шины, затем показ.
        /// Исключение в показе одного события не мешает остальным и не повторяет пачку в следующем кадре.
        /// </summary>
        void DrainEvents()
        {
            if (m_Draining || Session == null) return;
            var events = Session.Events;
            if (events.Count == 0) return;
            m_Draining = true;
            try
            {
                // Поэлементно: List.AddRange в Mono выделяет временный массив, а пачка разбирается почти каждый кадр (ТЗ 15.3).
                m_Batch.Clear();
                for (int i = 0; i < events.Count; i++) m_Batch.Add(events[i]);
                events.Clear();
                m_Dirty = true;
                bool commit = false;
                for (int i = 0; i < m_Batch.Count && !commit; i++) commit = IsCommitEvent(m_Batch[i].Type);
                if (commit) SaveNow();
                try
                {
                    Bus.Dispatch(Session, m_Batch);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                bool chestOpened = false, campaignEnded = false;
                for (int i = 0; i < m_Batch.Count; i++)
                {
                    var e = m_Batch[i];
                    if (e.Type == SimEventType.ChestOpened) chestOpened = true;
                    else if (e.Type == SimEventType.CampaignCompleted) campaignEnded = true;
                    m_PresentIndex = i;
                    try
                    {
                        Present(e);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
                m_PresentIndex = -1;
                m_Batch.Clear();
                try
                {
                    // Сундук, полученный при свободном экране, открывается сразу; полученный при открытой панели (в том числе за
                    // окном «Пока вас не было») ждёт в очереди неоткрытых и открывается тапом по «Сундуки: N» (ТЗ 10.3).
                    if (chestOpened && !Hud.ModalOpen) ShowPendingChests();
                    // Финальный экран — после панели награды выхода, она видна поверх него (ТЗ 11.4).
                    if (campaignEnded) ShowCampaignEnd();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            finally
            {
                m_Draining = false;
            }
        }

        /// <summary>Показ одного события: вид поля, звук, вибрация, подсказки, сообщения.</summary>
        void Present(SimEvent e)
        {
            Board.OnEvent(e, Fx);
            Tutorial.OnEvent(e);
            switch (e.Type)
            {
                case SimEventType.TargetHit:
                {
                    // Звук удара — оружием котика (по его уровню); кирка звучит по материалу цели (камень, кристалл, дерево, металл).
                    int ord = Session.OrdinalOf(e.ObjectIndex);
                    var material = ord >= 0 ? PresentationCatalog.HitMaterialOf(Balance, Session.Layout.Objects[ord]) : HitMaterial.Stone;
                    var cat = Session.FindCat(e.CatId);
                    Sound.Hit(cat != null ? CatLook.Weapon(cat.Level) : CatLook.Pick, material);
                    break;
                }
                case SimEventType.RockDestroyed:
                    Sound.Break();
                    break;
                case SimEventType.RewardGranted:
                {
                    if (e.Text == null) break;
                    // Награда собранной добычи: «+N» — над кучкой (камня там уже нет); при массовом автосборе — только у первых.
                    int lootCell = LootCellOf(e);
                    // Добыча, досчитанная переходом в другую зону: поле уже другое, итог — в панели награды выхода.
                    if (lootCell >= 0 && e.Zone != Session.Mine.Zone) break;
                    int ord = lootCell >= 0 ? -1 : Session.OrdinalOf(e.ObjectIndex);
                    if (lootCell < 0 && ord < 0) break;
                    if (lootCell < 0 || TakeBudget(ref m_LootFloatFrame, ref m_LootFloatCount))
                    {
                        var color = e.Text == "gold" ? Palette.Gold : (e.Text == "elixir" ? Palette.Elixir : Palette.RockHard);
                        var at = lootCell >= 0 ? Board.LootPos(lootCell) + new Vector3(0f, 0f, 0.3f) : Board.ObjectCenter(ord) + new Vector3(0f, 0f, 0.6f);
                        FloatingText.Spawn(at, "+" + NumberFormat.Format(e.Amount), color);
                    }
                    // Эликсир и кристаллы — мягкий колокольчик, чтобы на слух отличались от золота (частоту ограничивает Sfx).
                    if (e.Text == "gold") Sound.Coin();
                    else Sound.Chime();
                    break;
                }
                case SimEventType.LootDropped:
                    // Облачко пыли — при приземлении кучки (LootView); здесь — тихий звон падения.
                    Sound.LootDrop();
                    break;
                case SimEventType.LootCollected:
                    // Валюта летит к своему счётчику HUD; кучка, искры и монетки — в BoardView. Тап — горсть побольше и лёгкая
                    // вибрация, автосбор (OtherCatId = 1) — скромнее.
                    if (e.Zone != Session.Mine.Zone) break;
                    if (TakeBudget(ref m_LootFlyFrame, ref m_LootFlyCount))
                    {
                        int icons = Hud.LootFlyCount(e.Amount);
                        Hud.FlyLoot(Board.LootPos(e.Cell), e.Text, e.OtherCatId != 0 ? Math.Min(2, icons) : icons);
                    }
                    if (e.OtherCatId == 0) Haptics.Pulse(Session, 15);
                    break;
                case SimEventType.ContainerSpawned:
                    // Бочку с котиком выстрелила пушка у кузницы (BoardView): залп — сейчас, стук приземления — по
                    // Board.ContainerLanded. Ящик доставки появляется молча, как раньше.
                    if (Board.IsFlying(e.CatId)) Sound.Cannon();
                    break;
                case SimEventType.PayoutCollected:
                {
                    int ord = Session.OrdinalOf(e.ObjectIndex);
                    if (ord >= 0) FloatingText.Spawn(Board.ObjectCenter(ord) + new Vector3(0f, 0f, 1.1f), "+" + NumberFormat.Format(e.Amount), Palette.Gold);
                    Sound.Payout();
                    Haptics.Pulse(Session);
                    break;
                }
                case SimEventType.CatSpawned:
                    Sound.Meow();
                    break;
                case SimEventType.CatMerged:
                    Sound.Merge();
                    Haptics.Pulse(Session);
                    break;
                case SimEventType.CheckpointOpened:
                    // Джингл ворот глушит звук сундука-награды ворот из той же пачки (Sfx.ChestOpen) при любом порядке событий.
                    Sound.GateOpen();
                    Rig.Kick(2f);
                    Haptics.Pulse(Session, 40);
                    break;
                case SimEventType.MineShaftActivated:
                    Sound.Unlock();
                    break;
                case SimEventType.ShaftAutomated:
                    Sound.Unlock();
                    Hud.Toast(Loc.T("controller.toast.shaft_automated"));
                    break;
                case SimEventType.GeneratorUpgraded:
                case SimEventType.CardUpgraded:
                    Sound.Upgrade();
                    break;
                case SimEventType.ThresholdReached:
                    // Джингл порога глушит обычное улучшение той же покупки (Sfx.Upgrade).
                    Sound.Unlock();
                    Hud.Toast(Loc.F("controller.toast.threshold", e.Text));
                    break;
                case SimEventType.ChestOpened:
                    Sound.ChestOpen();
                    break;
                case SimEventType.ContainerOpened:
                    Sound.Open();
                    break;
                case SimEventType.DynamiteUsed:
                    Sound.Dynamite();
                    Rig.Kick(3f);
                    break;
                case SimEventType.GateBroken:
                    Sound.Break();
                    Hud.Toast(Loc.T("controller.toast.gate_broken"));
                    break;
                case SimEventType.ExitBroken:
                    Sound.ExitOpen();
                    Rig.Kick(2f);
                    Hud.Toast(Loc.T("controller.toast.exit_broken"));
                    break;
                case SimEventType.CampaignCompleted:
                    Sound.ExitOpen();
                    break;
                case SimEventType.ShaftCleared:
                    Sound.Break();
                    Hud.Toast(Loc.T("controller.toast.shaft_cleared"));
                    break;
                case SimEventType.CommandRejected:
                    Sound.Denied();
                    if (!string.IsNullOrEmpty(e.Text)) Hud.Toast(e.Text, 1.6f);
                    break;
                case SimEventType.ZoneEntered:
                    // Новая зона: перенос и выбор относятся к старой карте; камера — на стартовую площадку (ТЗ 13.2).
                    Input?.CancelAll();
                    ClearDragView();
                    ClearSelection();
                    RebuildBoard(false);
                    Hud.Toast(Loc.F("controller.toast.zone_entered", Session.Mine.Zone));
                    break;
            }
        }

        /// <summary>
        /// Клетка добычи, сбор которой начислил награду <paramref name="reward"/>: событие LootCollected той же операции,
        /// валюты и суммы, ближайшее к награде в пачке. −1 — награда не из добычи (прежний путь: сразу при разрушении камня).
        /// </summary>
        int LootCellOf(SimEvent reward)
        {
            int best = -1, bestDist = int.MaxValue;
            for (int j = 0; j < m_Batch.Count; j++)
            {
                var b = m_Batch[j];
                if (b.Type != SimEventType.LootCollected || b.OpId != reward.OpId || b.Text != reward.Text || b.Amount != reward.Amount) continue;
                int dist = Math.Abs(j - m_PresentIndex);
                if (dist >= bestDist) continue;
                bestDist = dist;
                best = b.Cell;
            }
            return best;
        }

        /// <summary>Счётчик показов в кадре: true, пока в этом кадре показано меньше <see cref="LootPresentPerFrame"/>.</summary>
        static bool TakeBudget(ref int frame, ref int count)
        {
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                count = 0;
            }
            return count++ < LootPresentPerFrame;
        }

        /// <summary>
        /// Открыть первый сундук очереди неоткрытых (ТЗ 10.3; тап по «Сундуки: N»). Результат уже определён и начислен;
        /// из сохраняемой очереди сундук уходит только по «Забрать», поэтому закрытие приложения его не теряет.
        /// </summary>
        public void ShowPendingChests()
        {
            if (m_ChestShowing || Session == null || Hud == null) return;
            var s = Session;
            var chest = s.PeekChestReveal();
            if (chest == null) return;
            m_ChestShowing = true;
            Hud.ShowChest(chest, () =>
            {
                // Слот сменился, пока панель была открыта: сундук остаётся в очереди своего сохранения.
                if (!ReferenceEquals(Session, s)) return;
                if (ReferenceEquals(s.PeekChestReveal(), chest)) s.TakeChestReveal();
                m_ChestShowing = false;
                SaveNow();
            });
        }

        public void MarkDirty() => m_Dirty = true;

        public void SaveNow()
        {
            if (Session == null || m_Saves == null || m_SaveBlocked) return;
            try
            {
                long now = Clock.UtcNowMs;
                OfflineProgress.MarkProcessed(Session, now);
                // Позиция камеры (ТЗ 12.1) — только для зоны, по которой построено поле: сразу после CompleteZone
                // камера ещё смотрит на старую карту, и новая зона начнётся со стартовой площадки.
                if (Rig != null && ReferenceEquals(m_BoardMine, Session.Mine)) Session.Mine.CameraFocusZ = Rig.FocusZ;
                m_Saves.Save(Session.State, now);
                m_Dirty = false;
                m_SaveTimer = 0;
            }
            catch (Exception e)
            {
                Debug.LogError("[CatMiners] Ошибка сохранения: " + e);
            }
        }

        void OnApplicationPause(bool paused)
        {
            // Звук приостанавливается вместе с приложением и продолжается с того же места при возврате.
            AudioListener.pause = paused;
            if (!paused) return;
            Input?.CancelAll();
            CancelCatDrag();
            SaveNow();
        }

        void OnApplicationFocus(bool focus)
        {
            if (focus) return;
            Input?.CancelAll();
            SaveNow();
        }

        void OnApplicationQuit() => SaveNow();

        /// <summary>Бочка из пушки приземлилась в своей клетке (BoardView): деревянный «тук».</summary>
        void OnContainerLanded(int containerId) => Sound.BarrelLand();

        // ───────────── Команды UI ─────────────

        public void Hire()
        {
            if (Session.Hire() == CommandResult.Ok) Sound.Click();
            DrainEvents();
        }

        /// <summary>Начать перенос (ТЗ 7.3, 7.5): в ядре котик переходит в Dragging — не бьёт, фаза не растёт.</summary>
        public void BeginCatDrag(int catId)
        {
            Session.BeginDrag(catId);
            Board.DraggedCatId = catId;
            var cat = Session.FindCat(catId);
            if (cat != null)
            {
                Board.DragWorldPos = Board.CellCenter(cat.Cell);
                Board.HighlightMergeCandidates(cat.Level, true);
            }
            // Контекстная панель на время переноса скрыта: она закрывала бы нижнюю часть поля для сброса.
            SelectedCatId = 0;
            SelectedOrdinal = -1;
            Board.Select(-1);
        }

        public void UpdateCatDrag(Vector3 world)
        {
            Board.DragWorldPos = world + Vector3.up * 0.2f;
        }

        /// <summary>
        /// Отмена переноса с возвратом (пауза, потеря фокуса, отмена касания, отпускание над UI, котик исчез):
        /// котик остаётся на месте с прежним назначением и фазой до переноса (ТЗ 7.3, 7.5).
        /// </summary>
        public void CancelCatDrag()
        {
            if (Board == null) return;
            ClearDragView();
            Session?.CancelDrag();
        }

        /// <summary>Сбросить только вид переноса: котик в руке и подсветку пар.</summary>
        void ClearDragView()
        {
            if (Board == null) return;
            Board.DraggedCatId = 0;
            Board.HighlightMergeCandidates(0, false);
        }

        public void DropCat(int catId, double gx, double gy)
        {
            // Перенос в ядре завершает сам DropCat: при отказе и при сбросе на свою рабочую клетку фаза восстанавливается.
            ClearDragView();
            var outcome = Session.DropCat(catId, gx, gy);
            if (outcome.Result == CommandResult.Ok)
            {
                if (outcome.Merged) SelectedCatId = Session.FindCat(catId) == null ? 0 : catId;
                MarkDirty();
            }
            DrainEvents();
        }

        /// <summary>
        /// Камера и контекстная панель (ТЗ 13.1, 13.2): пока панель открыта, нижний упор камеры ниже на её высоту; новый выбор,
        /// который панель закрыла бы (кузница у нижней панели HUD, котик на площадке), плавно поднимается над ней.
        /// Перевод камеры, уже начатый командой (ворота, неактивированная шахта), важнее. Без выделений памяти.
        /// </summary>
        void UpdateContextCamera()
        {
            int ord = SelectedOrdinal, catId = SelectedCatId;
            bool changed = ord != m_RevealOrd || catId != m_RevealCat;
            m_RevealOrd = ord;
            m_RevealCat = catId;
            bool selected = ord >= 0 || catId != 0;
            // Новый выбор: панель пересобирается сразу, чтобы её высота (с кнопками или без) была уже для него.
            // Без выбора панель скрывается при ближайшем обновлении HUD — камера возвращается, не дожидаясь его.
            if (changed && selected) Hud.Refresh();
            float top = 0f;
            bool open = selected && Hud.TryGetContextTop(out top);
            // Во время переноса панель скрыта, но камера не уезжает от места сброса: упор вернётся после переноса.
            if (!open && Input != null && (Input.IsDraggingCat || Input.IsDraggingDynamite)) return;
            Rig.SetContextTop(open ? top : 0f);
            if (!changed || !open || Rig.IsFocusing) return;
            var layout = Session.Layout;
            if (catId != 0)
            {
                var cat = Session.FindCat(catId);
                if (cat == null) return;
                float z = Board.CellCenter(cat.Cell).z;
                Rig.RevealAbove(z - 0.5f, z + 0.5f, top);
            }
            else if (ord < layout.Objects.Count)
            {
                float depth = layout.Objects[ord].Depth;
                float bottom = Board.ObjectCenter(ord).z - depth / 2f;
                Rig.RevealAbove(bottom, bottom + depth, top);
            }
        }

        public void SelectCat(int catId)
        {
            if (SelectedCatId != catId) Sound.Select();
            SelectedCatId = SelectedCatId == catId ? 0 : catId;
            SelectedOrdinal = -1;
            Board.Select(-1);
        }

        public void ClearSelection()
        {
            SelectedCatId = 0;
            SelectedOrdinal = -1;
            Board.Select(-1);
        }

        public void TapObject(int ord)
        {
            SelectedCatId = 0;
            var obj = Session.Layout.Objects[ord];
            var st = Session.Mine.Objects[ord];
            if (st.Gen != null && st.Gen.PayoutReady && !st.Gen.Automated)
            {
                CollectPayout(ord);
                return;
            }
            if (obj.Kind == ObjectKind.Shaft && st.Status == ObjectStatus.ReadyToActivate)
            {
                ActivateShaft(ord);
            }
            else if (obj.Kind == ObjectKind.Gate && st.Status == ObjectStatus.Broken)
            {
                OpenGate(ord);
                // Открытые ворота не выбираются: их панель закрыла бы новую зону мержа, к которой едет камера (ТЗ 11.1, 13.2).
                if (st.Status == ObjectStatus.Opened) return;
            }
            else if (obj.Kind == ObjectKind.Exit && st.Status == ObjectStatus.Broken)
            {
                // Подтверждение перехода или, если условие 11.2 не выполнено, причина и камера к неактивированной шахте.
                RequestNextMine();
            }
            if (SelectedOrdinal != ord) Sound.Select();
            SelectedOrdinal = SelectedOrdinal == ord ? -1 : ord;
            Board.Select(SelectedOrdinal);
        }

        public void ActivateShaft(int ord)
        {
            Session.ActivateShaft(ord);
            DrainEvents();
        }

        public void OpenGate(int ord)
        {
            if (Session.OpenGate(ord) == CommandResult.Ok)
            {
                // ТЗ 11.1: после транзакции — створки и свет (BoardView), плавный перевод камеры к площадке нового сектора.
                // Новая зона мержа над воротами — внизу полосы над нижней панелью HUD, как «домашний» вид; выбор снимается,
                // чтобы контекстная панель ворот её не закрывала.
                ClearSelection();
                Rig.FocusOnSmooth(Rig.HomeFocusZ(Session));
                var labels = Hud.GateBonusLabels(Session);
                Hud.Toast(labels.Count > 0 ? Loc.F("controller.toast.sector_opened_bonuses", string.Join(", ", labels)) : Loc.T("controller.toast.sector_opened"), 3.5f);
            }
            DrainEvents();
        }

        public void CollectPayout(int ord)
        {
            Session.CollectPayout(ord);
            DrainEvents();
        }

        public void Upgrade(int ord)
        {
            Session.UpgradeGenerator(ord, out _, 1);
            DrainEvents();
        }

        public void UpgradeToThreshold(int ord)
        {
            Session.UpgradeToThreshold(ord, out _);
            DrainEvents();
        }

        public void OpenContainer(int id)
        {
            Session.OpenContainer(id);
            DrainEvents();
        }

        /// <summary>Собрать добычу с поля (тап по кучке): начисление, «+N» и полёт к счётчику — по событиям сбора.</summary>
        public void CollectLoot(int lootId)
        {
            if (Session.CollectLoot(lootId) == CommandResult.Ok) MarkDirty();
            DrainEvents();
        }

        public void UseDynamite(double gx, double gy)
        {
            Session.UseDynamite(gx, gy);
            DrainEvents();
        }

        public void UpgradeCard(string cardId)
        {
            Session.UpgradeCard(cardId);
            DrainEvents();
        }

        public void ClaimFreeChest()
        {
            Session.ClaimFreeChest();
            DrainEvents();
            if (Hud.BlueprintsOpen) Hud.RebuildBlueprints();
        }

        /// <summary>Звук вкл./выкл. — настройка игрока в сохранении слота (ТЗ 11.3, 13.1).</summary>
        public void ToggleSound()
        {
            bool on = !SoundOn;
            if (Session != null)
            {
                Session.SetSoundOn(on);
                MarkDirty();
            }
            if (Sound != null) Sound.Enabled = on;
            AudioMix.Apply(Session != null ? Session.Player.Settings : new PlayerSettingsState { SoundOn = on });
        }

        public void FocusIdleCat()
        {
            var cats = Session.Mine.Cats;
            for (int k = 0; k < cats.Count; k++)
            {
                var cat = cats[(m_IdleCursor + k) % cats.Count];
                if (cat.TargetIndex >= 0) continue;
                m_IdleCursor = (m_IdleCursor + k + 1) % Math.Max(1, cats.Count);
                Rig.FocusOn(Session.Layout.Depth - Session.Layout.Row(cat.Cell) - 0.5f);
                // Выбрать, а не переключить: если перебор вернулся к уже выбранному котику, выбор не снимается.
                SelectedCatId = cat.Id;
                SelectedOrdinal = -1;
                Board.Select(-1);
                return;
            }
        }

        /// <summary>
        /// Камера к неактивированной шахте (ТЗ 11.2): первая — ближайшая к строке, на которую смотрит камера; повторное нажатие
        /// в течение <see cref="ShaftCycleSeconds"/> ведёт к следующей по удалённости от той же строки.
        /// </summary>
        void FocusInactiveShaft()
        {
            var layout = Session.Layout;
            if (Time.unscaledTime < m_ShaftCycleUntil) m_ShaftCursor++;
            else
            {
                m_ShaftCursor = 0;
                m_ShaftRefRow = Mathf.RoundToInt(layout.Depth - Rig.FocusZ);
            }
            int shaft = Session.NearestInactiveShaft(m_ShaftRefRow);
            if (shaft < 0) return;
            if (m_ShaftCursor > 0)
            {
                m_ShaftOrder.Clear();
                foreach (var sh in layout.Shafts)
                    if (Session.Mine.Objects[sh.Ordinal].Status != ObjectStatus.Producing) m_ShaftOrder.Add(sh.Ordinal);
                int refRow = m_ShaftRefRow;
                // Тот же порядок, что у NearestInactiveShaft: по |Row − refRow|, при равенстве — меньший ordinal.
                m_ShaftOrder.Sort((a, b) =>
                {
                    int da = Math.Abs(layout.Objects[a].Row - refRow), db = Math.Abs(layout.Objects[b].Row - refRow);
                    return da != db ? da.CompareTo(db) : a.CompareTo(b);
                });
                shaft = m_ShaftOrder[m_ShaftCursor % m_ShaftOrder.Count];
            }
            m_ShaftCycleUntil = Time.unscaledTime + ShaftCycleSeconds;
            Rig.FocusOnSmooth(Board.ObjectCenter(shaft).z);
        }

        /// <summary>
        /// Переход (ТЗ 11.2). Условие не выполнено — причина и камера к неактивированной шахте. Иначе — подтверждение:
        /// что осталось, что теряется, что сохраняется.
        /// </summary>
        public void RequestNextMine()
        {
            if (Session == null) return;
            if (!Session.CanCompleteZone(out string reason))
            {
                if (!string.IsNullOrEmpty(reason)) Hud.Toast(reason);
                FocusInactiveShaft();
                return;
            }
            int chests = Session.RemainingChestRocks();
            int rocks = Math.Max(0, Session.RemainingRocks() - chests);
            bool last = Session.Mine.Zone >= Balance.ZoneCount;
            string body = Loc.F("controller.next_mine.body", rocks, chests);
            var s = Session;
            Hud.Modal(last ? Loc.T("controller.next_mine.title_last") : Loc.F("controller.next_mine.title", Session.Mine.Zone + 1), body,
                new Hud.ModalButton { Label = Loc.T("controller.next_mine.stay"), Color = Palette.UiPanelLight },
                new Hud.ModalButton
                {
                    Label = last ? Loc.T("controller.next_mine.go_last") : Loc.T("controller.next_mine.go"),
                    Color = Palette.UiGreen,
                    Action = () =>
                    {
                        if (ReferenceEquals(Session, s)) ConfirmCompleteZone();
                    },
                });
        }

        /// <summary>
        /// Переход без окна подтверждения (инструменты разработки: чит уровней). Тот же путь, что у кнопки «Перейти»:
        /// одна транзакция CompleteZone, запись, награда выхода и новая карта. false — условие 11.2 не выполнено (причина в тосте).
        /// </summary>
        public bool CompleteZoneNow()
        {
            if (Session == null) return false;
            if (!Session.CanCompleteZone(out string reason))
            {
                if (!string.IsNullOrEmpty(reason)) Hud.Toast(reason);
                return false;
            }
            ConfirmCompleteZone();
            return true;
        }

        /// <summary>
        /// CompleteZone — одна транзакция и одна запись (ТЗ 11.2): запись сразу после транзакции, анимации, панель награды
        /// и новая карта — после неё.
        /// </summary>
        void ConfirmCompleteZone()
        {
            if (InTransition) return;
            // Пути котиков к выходу — по старой карте, пока состояние ещё не сменилось.
            Board.PrepareExitWalk();
            if (Session.CompleteZone() != CommandResult.Ok)
            {
                DrainEvents();
                return;
            }
            SaveNow();
            ClearSelection();
            StartCoroutine(ZoneTransition());
        }

        /// <summary>Длительности затемнения перед новой картой и после неё.</summary>
        const float TransitionFadeOut = 0.3f, TransitionFadeIn = 0.35f;

        /// <summary>
        /// Анимация перехода (ТЗ 11.2, 14): транзакция и запись уже сделаны. Камера едет к выходу, штольня вспыхивает тёплым
        /// светом, котики по очереди идут к выходу и скрываются в ней; затем затемнение, новая карта (события ZoneEntered,
        /// награда выхода) и проявление. Симуляция новой зоны всё это время идёт (Update), её события ждут конца анимации.
        /// </summary>
        IEnumerator ZoneTransition()
        {
            InTransition = true;
            Input?.CancelAll();
            ClearDragView();
            Hud.BeginTransition();
            var exit = Board.ExitCenter;
            Rig.FocusOnSmooth(exit.z, 0.6f);
            Fx.Glow(exit, PixelPalette.Amber4, 2.5f, 1.6f);
            Fx.Puff(exit);
            Fx.Sparkle(exit, 4);
            Sound.GateOpen();
            float walk = Board.PlayExitWalk();
            for (float t = 0f; t < walk; t += Time.unscaledDeltaTime) yield return null;
            for (float t = 0f; t < TransitionFadeOut; t += Time.unscaledDeltaTime)
            {
                Hud.SetTransitionFade(t / TransitionFadeOut);
                yield return null;
            }
            Hud.SetTransitionFade(1f);
            InTransition = false;
            // ZoneEntered строит новую карту и ставит камеру «домой»; награда выхода открывает окно сундука.
            DrainEvents();
            SaveNow();
            Hud.Refresh();
            for (float t = 0f; t < TransitionFadeIn; t += Time.unscaledDeltaTime)
            {
                Hud.SetTransitionFade(1f - t / TransitionFadeIn);
                yield return null;
            }
            Hud.EndTransition();
        }

        /// <summary>
        /// Финальный экран (ТЗ 11.4): пройдено шахт, общее время по зонам, собранные карточки коллекции (редкость ниже Majestic).
        /// Без кнопки закрытия; на следующих запусках открывается из LoadSlot.
        /// </summary>
        void ShowCampaignEnd()
        {
            var player = Session.Player;
            long ticks = 0;
            foreach (var kv in player.ZoneTimeTicks) ticks += kv.Value;
            int owned = 0, total = 0;
            var cards = Session.Balance.CardsInOrder;
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c.Rarity >= Rarity.Majestic) continue;
                total++;
                if (player.Cards.TryGetValue(c.Id, out var cs) && cs.Level > 0) owned++;
            }
            Hud.ShowCampaignEnd(Loc.T("controller.campaign_end.title"),
                Loc.F("controller.campaign_end.body", player.HighestCompletedZone,
                    NumberFormat.FormatDuration(GameSession.TicksToSeconds(ticks)), owned, total));
        }

        /// <summary>Диагностический обработчик завершения зоны (ТЗ 16): одна запись в журнал на операцию CompleteZone.</summary>
        sealed class ZoneCompletionLog : IZoneCompletionHandler
        {
            public void OnZoneCompleted(GameSession session, int zone, long opId)
            {
                Debug.Log("[CatMiners] Zone completed: zone" + zone + ", opId " + opId + ", slot " + session.State.Slot);
            }
        }
    }
}
