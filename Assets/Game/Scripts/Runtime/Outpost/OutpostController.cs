using System;
using System.Collections.Generic;
using System.IO;
using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using CommandResult = CatMiners.Core.Outpost.CommandResult;
using OfflineSummary = CatMiners.Core.Outpost.OfflineSummary;

namespace CatMiners.Runtime.Outpost
{
    public sealed partial class OutpostController : MonoBehaviour, IOutpostGame
    {
        public static string SaveDirectoryOverride;
        public static IClock ClockOverride;
        public static IFileSystem FileSystemOverride;
        /// <summary>
        /// Подмена конфигов сразу после загрузки в <see cref="Init"/> (профиль баланса playable-сборки); null — конфиги файлов
        /// без изменений. Ставится до загрузки сцены; тесты сбрасывают в TearDown.
        /// </summary>
        public static Func<OutpostConfigSet, OutpostConfigSet> ConfigsOverride;
        /// <summary>
        /// Офлайн догоняется как обычно, но окно «While you were away» не открывается (playable). false — поведение игры.
        /// </summary>
        public static bool SuppressOfflineSummary;
        public const double SaveIntervalSeconds = 2;
        public OutpostSimulation Sim { get; private set; }
        public OutpostGameState State => Sim?.State;
        public OutpostMap Map => Sim?.Map;
        public OutpostConfigSet Configs { get; private set; }
        public TeamStats Stats => Sim?.Stats;
        public BalanceRepository Balance { get; private set; }
        public OutpostControllerPhase Phase { get; private set; } = OutpostControllerPhase.Loading;
        public bool IsDebugProfile => m_Slot == OutpostSaveSlot.Debug;
        public bool InTransition { get; private set; }
        public bool SaveFailed => m_PendingSave != null;
        public string ErrorText { get; private set; }
        public OutpostSelection Selection { get; private set; } = OutpostSelection.None;
        public OfflineSummary PendingOffline { get; private set; }
        public float OfflineProgress01 => m_Offline == null ? 0 : (float)m_Offline.Progress;
        /// <summary>Итог завершённого тайм-скипа магазина (ТЗ 10.5) для окна; null — окна нет.</summary>
        public OfflineSummary PendingSkip { get; private set; }
        /// <summary>Часы идущего тайм-скипа (0 — идёт обычный офлайн-расчёт или ничего).</summary>
        public int SkipMinutesInProgress => m_Offline != null ? m_Offline.Summary.SkipMinutes : 0;
        /// <summary>UTC для таймеров магазина (рекламный сундук).</summary>
        public long UtcNowMs => Clock.UtcNowMs;
        public bool CanContinue => Phase == OutpostControllerPhase.Playing && !SaveFailed && !InTransition &&
            Sim != null && Sim.Run.Phase == RunPhase.ReadyToContinue && !CampaignComplete;
        public bool CampaignComplete => State?.Profile.CampaignComplete ?? false;
        public IClock Clock { get; private set; }
        public string SaveDirectory { get; private set; }
        public CameraRig Rig { get; private set; }
        public OutpostBoardView Board { get; private set; }
        public OutpostHud Hud { get; private set; }
        public OutpostInput Input { get; private set; }
        public OutpostTutorial Tutorial { get; private set; }
        public Sfx Sound { get; private set; }
        public MusicPlayer Music { get; private set; }
        public Ambience AmbienceSound { get; private set; }
        public Effects Fx { get; private set; }
        public event Action<OutpostEvent> EventPresented;
        public event Action SessionChanged;
        OutpostSaveSlot m_Slot;
        OutpostSaveService m_Saves;
        bool m_SaveBlocked, m_Suspended, m_Paused, m_Unfocused, m_Initialized;
        OfflineJob m_Offline;
        readonly List<OutpostEvent> m_Events = new List<OutpostEvent>(256);
        OutpostAudio m_Audio;
        int m_Speed = 1;
        public int SimSpeed
        {
            get => m_Speed;
            set
            {
                if (!DebugAllowed || !CommandsAllowed) return;
                AdvanceOnline();
                if (Phase != OutpostControllerPhase.Playing) return;
                m_Speed = value == 5 || value == 20 || value == 100 ? value : 1;
                RebaseClock();
            }
        }
        bool DebugAllowed => !AndroidRuntime && IsDebugProfile && (Application.isEditor || Debug.isDebugBuild);
        bool CommandsAllowed => !CatMiners.Runtime.Purchasing.ParentPurchaseGate.IsOpen && !CatMiners.Runtime.Integrations.NeutralAgeFlow.IsOpen && Sim != null && Phase == OutpostControllerPhase.Playing && !m_Suspended && !SaveFailed && !m_SaveBlocked && !InTransition;

        public void Init(GameBootstrap boot)
        {
            if (m_Initialized) return;
            m_Initialized = true;
            Clock = ClockOverride ?? new SystemClock();
            Materials.Init(boot.LitMaterial, boot.UnlitMaterial);
            PixelArt.Init(boot.SpriteMaterial, boot.UnlitMaterial);
            EnsureSceneBasics();
            var cam = Camera.main;
            if (cam == null) cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            if (FindFirstObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            Rig = CameraRig.Create(cam);
            Fx = Effects.Create(transform, boot.ParticleMaterial);
            Sound = Sfx.Create(transform);
            Music = MusicPlayer.Create(transform);
            AmbienceSound = Ambience.Create(transform);
            m_Audio = new OutpostAudio(Sound, transform);
            Board = new GameObject("OutpostBoard").AddComponent<OutpostBoardView>();
            Board.transform.SetParent(transform, false);
            Board.Fx = Fx;
            Board.Cue += OnBoardCue;
            Board.StrikeHandler = StrikeNow;
            Hud = OutpostHud.Create(this, Rig);
            Input = gameObject.AddComponent<OutpostInput>();
            Input.Init(this);
            Hud.OnWindowOpened += Input.CancelGesture;
            Tutorial = new OutpostTutorial(this, Hud);
            try
            {
                Balance = OutpostResources.LoadSource(boot.BalanceJson);
                // Карточки — из исходного json-data.json (61 запись) и card_rules.json.
                Configs = OutpostResources.LoadConfigs().WithCards(Balance);
                if (ConfigsOverride != null) Configs = ConfigsOverride(Configs) ?? Configs;
                if (AndroidRuntime && Configs.Store != null)
                {
                    var store = OutpostJson.DeepCopy(Configs.Store); store.TestPurchases = false;
                    Configs = Configs.WithStore(store);
                }
                SaveDirectory = string.IsNullOrEmpty(SaveDirectoryOverride) ? Application.persistentDataPath : SaveDirectoryOverride;
                m_Saves = new OutpostSaveService(SaveDirectory, FileSystemOverride) { Configs = Configs, MapResolver = ResolveMap };
                LoadProfile(OutpostSaveSlot.Main);
            }
            catch (Exception e) { Fatal(e.Message); }
        }

        OutpostMap ResolveMap(OutpostGameState state)
        {
            if (state.Run.SourceSha256 != Balance.Sha256 || state.Run.AdapterVersion != OutpostIds.AdapterVersion)
                throw new InvalidDataException("Source or adapter version mismatch");
            // Сохранённая шахта — с копией override из run: изменённый файл override не подменяет её геометрию.
            return OutpostLevelAdapter.ForRun(Balance, state.Run);
        }

        void EnsureSceneBasics()
        {
            if (FindFirstObjectByType<Light>() == null)
            {
                var light = new GameObject("Directional Light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.transform.rotation = Quaternion.Euler(52, -28, 0);
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.55f);
            var es = FindFirstObjectByType<EventSystem>();
            if (es == null) es = new GameObject("EventSystem").AddComponent<EventSystem>();
            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module == null) module = es.gameObject.AddComponent<InputSystemUIInputModule>();
            if (module.actionsAsset == null) module.AssignDefaultActions();
        }

        void Fatal(string message)
        {
            m_SaveBlocked = true;
            Phase = OutpostControllerPhase.Error;
            ErrorText = message;
            Debug.LogError("[Outpost] " + message);
            Hud?.RefreshNow();
        }

        void SetSimulation(OutpostGameState state, OutpostMap map)
        {
            AccumulatePlayTime();
            // Prepared transitions/offline jobs contain an earlier copy of this profile's counter.
            // Loading another save keeps that save's own counter instead.
            if (Phase != OutpostControllerPhase.Loading && State != null)
                state.Profile.PlayTimeMs = State.Profile.PlayTimeMs;
            Sim = new OutpostSimulation(state, map, Configs);
            Selection = OutpostSelection.None;
            m_Events.Clear();
            Board.Build(map, Sim, Balance);
            Rig.Cam.backgroundColor = PixelTheme.Dark;
            // SetBoard fits the new width first, then clamps to its base bound.
            Rig.SetBoard(map.Width, Board.CameraDepth, map.Topology != null ? Board.BaseFocusZ : 0f, map.Topology != null,
                map.Topology != null ? Board.CellCenter(map.SpawnCell).x : map.Width / 2f);
            ApplySettings();
            Music.SetPreferred((state.Run.Zone - 1) % 3 == 1 ? MusicPlayer.Crystal : MusicPlayer.Warm);
            RebaseClock();
            SessionChanged?.Invoke();
            IntegrationLoaded();
        }

        void LoadProfile(OutpostSaveSlot slot)
        {
            InvalidatePurchases();
            m_Slot = slot;
            m_Speed = 1;
            OutpostDebugOverlay.Enabled = false;
            m_SaveBlocked = false;
            ErrorText = null;
            PendingOffline = null;
            PendingSkip = null;
            Hud.CloseAllWindows();
            Phase = OutpostControllerPhase.Loading;
            m_PlayTimeMonoUs = MonoUs;
            m_PlayTimeRemainderUs = 0;
            var loaded = m_Saves.Load(slot);
            if (loaded.Status == OutpostLoadStatus.Corrupt || loaded.Status == OutpostLoadStatus.Incompatible)
            {
                Sim = null;
                Board.gameObject.SetActive(false);
                Fatal(loaded.Status + ": " + string.Join("; ", loaded.Messages));
                return;
            }
            Board.gameObject.SetActive(true);
            if (loaded.State != null)
            {
                // Возвращение карточек: однократная миграция профиля и перенос прежних карточек (с копией сохранения до записи).
                bool cardsChanged = PrepareCards(loaded.State.Profile, slot);
                // Магазин: однократно стартовые гемы и тайм-скипы (ТЗ 10.5), в том числе профилям, созданным до магазина.
                cardsChanged |= StoreService.MigrateProfile(loaded.State.Profile, Configs.Store);
                var map = ResolveMap(loaded.State);
                SetSimulation(loaded.State, map);
                if (!TryMigrateLoadedMine(loaded.State))
                {
                    if (cardsChanged) Commit(loaded.State, map, () => BeginOffline());
                    else BeginOffline();
                }
            }
            else
            {
                m_Saves.ArchiveLegacySaves(SaveDirectory);
                ulong seed = unchecked((ulong)Clock.UtcNowMs ^ ((ulong)(uint)Environment.TickCount << 32));
                var state = StageProgressionService.NewGame(Balance, Configs, seed, Clock.UtcNowMs);
                ImportLegacySettings(state.Profile.Settings);
                ImportLegacyCards(state.Profile);
                StoreService.MigrateProfile(state.Profile, Configs.Store);
                var map = ResolveMap(state);
                SetSimulation(state, map);
                Commit(state, map, () => { Phase = OutpostControllerPhase.Playing; RebaseClock(); });
            }
            Hud.RefreshNow();
        }

        public void SwitchProfile(bool debug)
        {
            if (AndroidRuntime || m_RewardShowing || m_Saves == null || !(Application.isEditor || Debug.isDebugBuild) || m_Suspended || SaveFailed || InTransition || m_Offline != null || (Phase != OutpostControllerPhase.Playing && Phase != OutpostControllerPhase.Error)) return;
            var slot = debug ? OutpostSaveSlot.Debug : OutpostSaveSlot.Main;
            if (slot == m_Slot) return;
            if (!m_SaveBlocked && Sim != null)
            {
                AdvanceOnline();
                if (Phase != OutpostControllerPhase.Playing) return;
                Commit(State, Map, () => LoadProfile(slot));
            }
            else LoadProfile(slot);
        }

        void OnDestroy()
        {
            InvalidatePurchases();
            Tutorial?.Dispose();
            if (Hud != null && Input != null) Hud.OnWindowOpened -= Input.CancelGesture;
            if (Board != null && m_Audio != null) Board.Cue -= OnBoardCue;
            AudioListener.pause = false;
        }
    }
}
