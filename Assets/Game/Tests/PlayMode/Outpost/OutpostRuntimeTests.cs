using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CatMiners.Core.Outpost;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using CatMiners.DevTools;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CatMiners.Tests
{
    public sealed partial class OutpostRuntimeTests
    {
        sealed class FaultFiles : IFileSystem
        {
            readonly RealFileSystem m_Real = new RealFileSystem();
            public bool FailWrites;
            public bool FailBackupWrites;
            public bool FailTopologyArchive;
            public string AttemptPayload;
            public readonly System.Collections.Generic.List<string> AttemptPayloads = new System.Collections.Generic.List<string>();
            public bool Exists(string p) => m_Real.Exists(p);
            public byte[] ReadAllBytes(string p) => m_Real.ReadAllBytes(p);
            public void WriteAllBytesDurable(string p, byte[] bytes)
            {
                AttemptPayload = (string)JObject.Parse(Encoding.UTF8.GetString(bytes))["Payload"];
                AttemptPayloads.Add(AttemptPayload);
                if (FailWrites) throw new IOException("Injected runtime write failure");
                if (FailBackupWrites && p.EndsWith(".bak", StringComparison.Ordinal)) throw new IOException("Injected reset backup write failure");
                m_Real.WriteAllBytesDurable(p, bytes);
            }
            public void Replace(string a, string b, string c) => m_Real.Replace(a, b, c);
            public void Move(string a, string b) => m_Real.Move(a, b);
            public void Copy(string a, string b)
            {
                if (FailTopologyArchive && b.Contains("pre-wide-topology-")) throw new IOException("Injected topology archive failure");
                m_Real.Copy(a, b);
            }
            public void Delete(string p) => m_Real.Delete(p);
            public void CreateDirectory(string p) => m_Real.CreateDirectory(p);
        }
        ManualClock m_Clock;
        FaultFiles m_Files;
        string m_Dir;
        OutpostController Game => GameBootstrap.OutpostController;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_Dir = Path.Combine(Application.temporaryCachePath, "OutpostRuntimeTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Dir);
            m_Clock = new ManualClock(1_800_000_000_000L);
            m_Files = new FaultFiles();
            OutpostController.ClockOverride = m_Clock;
            OutpostController.SaveDirectoryOverride = m_Dir;
            OutpostController.FileSystemOverride = m_Files;
            OutpostController.SimulateRewardedOffersForTests = false;
            GameBootstrap.LegacyMode = false;
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            Assert.That(Game, Is.Not.Null);
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var empty = SceneManager.CreateScene("OutpostRuntimeCleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            var loaded = SceneManager.GetSceneByName("CoreGame");
            if (loaded.IsValid()) yield return SceneManager.UnloadSceneAsync(loaded);
            OutpostController.ClockOverride = null;
            OutpostController.SaveDirectoryOverride = null;
            OutpostController.ConfigsOverride = null;
            OutpostController.SuppressOfflineSummary = false;
            OutpostHud.BottomReserve = 0f;
            OutpostController.FileSystemOverride = null;
            OutpostController.SimulateRewardedOffersForTests = false;
            GameBootstrap.LegacyMode = false;
            AudioListener.pause = false;
            try { Directory.Delete(m_Dir, true); } catch (IOException) { }
        }
        IEnumerator Advance(double seconds)
        {
            int frames = (int)Math.Ceiling(seconds / 0.5);
            for (int i = 0; i < frames; i++) { m_Clock.Advance(0.5); yield return null; }
        }
        IEnumerator WaitPlaying()
        {
            for (int frame = 0; frame < 2000 && Game.Phase != OutpostControllerPhase.Playing; frame++) yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }

        Transform OpenLevelCheats()
        {
            Assert.That(OutpostDevPanel.Attach(Game), Is.Not.Null);
            var panel = Game.Hud.transform.Find("SafeArea/OutpostDevPanel");
            if (!panel.gameObject.activeSelf)
                Game.Hud.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b => b.name == "OutpostDevToggle").onClick.Invoke();
            return panel;
        }

        static UnityEngine.UI.Button LevelButton(Transform panel, string name) =>
            panel.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b => b.name == name);

        IEnumerator WaitForLevel(Transform panel, int zone)
        {
            for (int frame = 0; frame < 2000 &&
                (Game.Phase != OutpostControllerPhase.Playing || Game.State.Run.Zone != zone || !LevelButton(panel, "+1").interactable); frame++)
                yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
            Assert.That(Game.State.Run.Zone, Is.EqualTo(zone));
            Assert.That(LevelButton(panel, "+1").interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator DevLevelCheats_RelativeButtonsLoadImmediately_KeepMainProgress()
        {
            var panel = OpenLevelCheats();
            Game.State.Profile.Elixir = 123;
            LevelButton(panel, "+1").onClick.Invoke();
            yield return WaitForLevel(panel, 2);
            Assert.That(Game.IsDebugProfile, Is.True);
            string mainPath = new OutpostSaveService(m_Dir).MainPath(OutpostSaveSlot.Main);
            byte[] savedMain = File.ReadAllBytes(mainPath);
            LevelButton(panel, "+10").onClick.Invoke();
            yield return WaitForLevel(panel, 12);
            LevelButton(panel, "-1").onClick.Invoke();
            yield return WaitForLevel(panel, 11);
            LevelButton(panel, "-10").onClick.Invoke();
            yield return WaitForLevel(panel, 1);
            LevelButton(panel, "-10").onClick.Invoke();
            yield return WaitForLevel(panel, 1);
            CollectionAssert.AreEqual(savedMain, File.ReadAllBytes(mainPath));
            LevelButton(panel, "Main").onClick.Invoke();
            yield return WaitForLevel(panel, 1);
            Assert.That(Game.IsDebugProfile, Is.False);
            Assert.That(Game.State.Profile.Elixir, Is.EqualTo(123));
        }

        [UnityTest]
        public IEnumerator DevLevelCheats_NumberLoadsWithButtonOrEnter_RejectsInvalidNumbers()
        {
            var panel = OpenLevelCheats();
            var input = panel.GetComponentInChildren<TMPro.TMP_InputField>();
            foreach (string invalid in new[] { "", "0", "145", "-1" })
            {
                input.SetTextWithoutNotify(invalid);
                LevelButton(panel, "Load").onClick.Invoke();
                Assert.That(Game.IsDebugProfile, Is.False);
                Assert.That(Game.State.Run.Zone, Is.EqualTo(1));
            }
            input.SetTextWithoutNotify("57");
            LevelButton(panel, "Load").onClick.Invoke();
            yield return WaitForLevel(panel, 57);
            input.SetTextWithoutNotify("144");
            input.onSubmit.Invoke(input.text);
            yield return WaitForLevel(panel, 144);
            LevelButton(panel, "+10").onClick.Invoke();
            yield return WaitForLevel(panel, 144);
            // Relative jumps use the running mine, even when the field contains an unsubmitted edit.
            input.SetTextWithoutNotify("5");
            LevelButton(panel, "-1").onClick.Invoke();
            yield return WaitForLevel(panel, 143);
            Assert.That(input.text, Is.EqualTo("143"));
        }

        [UnityTest]
        public IEnumerator DevLevelCheats_PointerFocusesNumberField_KeyboardSubmitsLevel()
        {
            var panel = OpenLevelCheats();
            var input = panel.GetComponentInChildren<TMPro.TMP_InputField>();
            var background = InputSystem.settings.backgroundBehavior;
            var editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var mouse = InputSystem.AddDevice<Mouse>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return null;
                Canvas.ForceUpdateCanvases();
                var rect = (RectTransform)input.transform;
                Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
                yield return null;
                Assert.That(input.isFocused, Is.True);
                input.text = "";
                input.ProcessEvent(new Event { type = EventType.KeyDown, character = '5', keyCode = KeyCode.Alpha5 });
                input.ProcessEvent(new Event { type = EventType.KeyDown, character = '7', keyCode = KeyCode.Alpha7 });
                Assert.That(input.text, Is.EqualTo("57"));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                yield return WaitForLevel(panel, 57);
                Assert.That(Game.IsDebugProfile, Is.True);
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior = background;
                InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            }
        }

        [UnityTest]
        public IEnumerator DevLevelCheats_ExistingDebugProfileWaitsForOfflineCatchUp()
        {
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Game.LoadZone(40);
            Game.SwitchProfile(false);
            yield return WaitPlaying();
            m_Clock.Advance(10);
            var panel = OpenLevelCheats();
            LevelButton(panel, "+10").onClick.Invoke();
            yield return WaitForLevel(panel, 11);
            Assert.That(Game.IsDebugProfile, Is.True);
            Assert.That(Game.PendingOffline, Is.Null);
        }

        [UnityTest]
        public IEnumerator DevLevelCheats_FailedLoadKeepsBoardUntilSaveRetry()
        {
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            var panel = OpenLevelCheats();
            var original = Game.Sim;
            m_Files.FailWrites = true;
            LevelButton(panel, "+10").onClick.Invoke();
            Assert.That(Game.SaveFailed, Is.True);
            Assert.That(Game.Sim, Is.SameAs(original));
            Assert.That(Game.State.Run.Zone, Is.EqualTo(1));
            m_Files.FailWrites = false;
            Game.RetrySave();
            yield return WaitForLevel(panel, 11);
            Assert.That(Game.Sim, Is.Not.SameAs(original));
            Assert.That(panel.GetComponentInChildren<TMPro.TMP_InputField>().text, Is.EqualTo("11"));
        }

        [UnityTest]
        public IEnumerator DevLevelCheats_FailedMainSaveCanBeRetriedBeforeJumping()
        {
            var panel = OpenLevelCheats();
            m_Files.FailWrites = true;
            LevelButton(panel, "+1").onClick.Invoke();
            Assert.That(Game.SaveFailed, Is.True);
            Assert.That(Game.IsDebugProfile, Is.False);
            Assert.That(Game.State.Run.Zone, Is.EqualTo(1));
            m_Files.FailWrites = false;
            Game.RetrySave();
            yield return WaitPlaying();
            LevelButton(panel, "+1").onClick.Invoke();
            yield return WaitForLevel(panel, 2);
            Assert.That(Game.IsDebugProfile, Is.True);
        }

        [UnityTest]
        public IEnumerator PlayTime_ForegroundHitch_CountsRealTimeOnce()
        {
            m_Clock.Advance(2.5);
            yield return null;
            yield return WaitPlaying();
            Assert.That(Game.State.Profile.PlayTimeMs, Is.EqualTo(2500), "Длинный кадр активной игры не теряется");
            yield return null;
            Assert.That(Game.State.Profile.PlayTimeMs, Is.EqualTo(2500), "Офлайн-расчёт не добавляет то же время повторно");
        }

        [UnityTest]
        public IEnumerator PlayTime_ActiveMenus_ExcludesBackgroundAndSkips_PersistsAcrossProfiles()
        {
            Assert.That(Game.State.Profile.PlayTimeMs, Is.Zero);
            Game.Hud.ShowSettings();
            yield return Advance(2.5);
            Assert.That(Game.State.Profile.PlayTimeMs, Is.EqualTo(2500));
            Game.Hud.CloseAllWindows();
            Game.State.Profile.Gems = 50;
            Assert.That(Game.UseTimeSkip("skip_1h").Ok, Is.True);
            yield return WaitPlaying();
            Assert.That(Game.State.Profile.PlayTimeMs, Is.EqualTo(2500), "10 минут скипа не являются сыгранным временем");
            Game.DismissSkip();
            Game.SendMessage("OnApplicationFocus", false);
            m_Clock.Advance(60);
            Game.SendMessage("OnApplicationFocus", true);
            yield return WaitPlaying();
            Assert.That(Game.State.Profile.PlayTimeMs, Is.EqualTo(2500), "Фоновая минута не учитывается");
            Game.DismissOffline();
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Assert.That(Game.State.Profile.PlayTimeMs, Is.Zero, "У другого профиля свой счётчик");
            Game.SimSpeed = 100;
            yield return Advance(0.5);
            Assert.That(Game.State.Profile.PlayTimeMs, Is.EqualTo(500), "Ускорение симуляции не ускоряет счётчик");
            Game.SwitchProfile(false);
            yield return WaitPlaying();
            Assert.That(Game.State.Profile.PlayTimeMs, Is.EqualTo(2500));
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return WaitPlaying();
            Assert.That(Game.State.Profile.PlayTimeMs, Is.EqualTo(2500), "Счётчик загружается после перезапуска");
        }

        [UnityTest]
        public IEnumerator SettingsReset_Main_CancelThenPersistsFreshProgressAndPlayTime()
        {
            Assert.That(Game.IsDebugProfile, Is.False);
            Game.State.Profile.PlayTimeMs = 125 * 60_000L + 59_500;
            Game.State.Profile.Settings.Music = 0.25f;
            Game.State.Profile.Elixir = 120;
            EquipmentService.Grant(Game.State.Profile, "pickaxe.common", Game.Configs);
            yield return Advance(0.5);
            long played = Game.State.Profile.PlayTimeMs;
            Game.Hud.ShowSettings();
            Assert.That(Game.Hud.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Any(t => t.text == "2h 6m"), Is.True);
            var reset = Game.Hud.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b => b.name == "ResetProgress");
            Assert.That(reset.interactable, Is.True);
            string before = OutpostSaveService.SerializeState(Game.State);
            reset.onClick.Invoke();
            Assert.That(Game.Hud.ModalOpen, Is.True);
            yield return null;
            Game.Hud.HandleBack();
            Assert.That(Game.Hud.ModalOpen, Is.False);
            Assert.That(OutpostSaveService.SerializeState(Game.State), Is.EqualTo(before));
            reset.onClick.Invoke();
            var confirm = Game.Hud.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b =>
                b.GetComponentInChildren<TMPro.TextMeshProUGUI>()?.text == Loc.T("outpost.dev.reset_confirm"));
            confirm.onClick.Invoke();
            AssertFreshProgress();
            Assert.That(Game.State.Profile.PlayTimeMs, Is.EqualTo(played));
            Assert.That(Game.State.Profile.Settings.Music, Is.EqualTo(0.25f));
            Assert.That(Game.Hud.SettingsOpen, Is.False);
            var saves = new OutpostSaveService(m_Dir) { Configs = Game.Configs };
            Assert.That(saves.Load().State.Profile.PlayTimeMs, Is.EqualTo(played));
            CollectionAssert.AreEqual(File.ReadAllBytes(saves.MainPath(OutpostSaveSlot.Main)),
                File.ReadAllBytes(saves.BakPath(OutpostSaveSlot.Main)));
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return WaitPlaying();
            AssertFreshProgress();
            Assert.That(Game.State.Profile.PlayTimeMs, Is.EqualTo(played));
        }

        OutpostGameState SeedNarrowDebugMine()
        {
            var topology = OutpostJson.Deserialize<MineTopology>(File.ReadAllText(Path.Combine(Application.dataPath,
                "Game", "Tests", "Fixtures", "Levels", "mine_topology_v1_zone1.json")));
            var catalog = new MineTopologyCatalog { Version = MineTopologyCatalog.LegacyVersion, SourceSha256 = Game.Balance.Sha256 };
            catalog.Zones.Add("zone1", topology);
            // The narrow layout predates mine dressing; seed the saved content from that revision.
            var state = StageProgressionService.NewGame(Game.Balance, Game.Configs.WithTopologies(catalog).WithDressing(null), 31337, m_Clock.UtcNowMs);
            state.Profile.CardKeys.Add(CardService.LegacyImportKey);
            var map = OutpostLevelAdapter.ForRun(Game.Balance, state.Run);
            new OutpostSimulation(state, map, Game.Configs).Advance(20 * OutpostIds.TicksPerSecond);
            var saves = new OutpostSaveService(m_Dir, m_Files) { Configs = Game.Configs };
            Assert.That(saves.Save(state, OutpostSaveSlot.Debug, map).Ok, Is.True);
            return state;
        }

        [UnityTest]
        public IEnumerator LoadingNarrowMine_ArchivesAndMigratesExistingProgress_InActualScene()
        {
            var old = SeedNarrowDebugMine();
            var saves = new OutpostSaveService(m_Dir);
            byte[] original = File.ReadAllBytes(saves.MainPath(OutpostSaveSlot.Debug));
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Assert.That(Game.Map.Topology.Version, Is.EqualTo(MineTopologyCatalog.SupportedVersion));
            Assert.That(Game.Rig.FreeExploration, Is.True);
            Assert.That(Game.Map.Width, Is.GreaterThanOrEqualTo(25));
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(old.Run.Wallet));
            Assert.That(Game.State.Run.Levels, Is.EqualTo(old.Run.Levels));
            Assert.That(OutpostJson.Serialize(Game.State.Run.Targets), Is.EqualTo(OutpostJson.Serialize(old.Run.Targets)));
            // Загрузка применяет миграцию магазина; остальной профиль не меняется.
            Assert.That(StoreService.MigrateProfile(old.Profile, Game.Configs.Store), Is.True);
            Assert.That(OutpostJson.Serialize(Game.State.Profile), Is.EqualTo(OutpostJson.Serialize(old.Profile)));
            string archive = Directory.GetDirectories(saves.SlotDirectory(OutpostSaveSlot.Debug), "pre-wide-topology-*").Single();
            CollectionAssert.AreEqual(original, File.ReadAllBytes(Path.Combine(archive, "state.json")));
            Assert.That(saves.Load(OutpostSaveSlot.Debug).State.Run.MapTopology.Version, Is.EqualTo(MineTopologyCatalog.SupportedVersion));
            Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment, Game.Configs.Cards), Is.Empty);
        }

        [UnityTest]
        public IEnumerator LoadingNarrowMine_FailedMigrationSave_RetryKeepsOnePreparedSnapshot()
        {
            var old = SeedNarrowDebugMine();
            Action failAfterLoading = () => { if (Game.IsDebugProfile && Game.Map.Topology.Version == MineTopologyCatalog.LegacyVersion) m_Files.FailWrites = true; };
            Game.SessionChanged += failAfterLoading;
            Game.SwitchProfile(true);
            Game.SessionChanged -= failAfterLoading;
            Assert.That(Game.SaveFailed, Is.True);
            Assert.That(Game.Map.Topology.Version, Is.EqualTo(MineTopologyCatalog.LegacyVersion));
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(old.Run.Wallet));
            string prepared = m_Files.AttemptPayload;
            m_Files.FailWrites = false;
            Game.RetrySave();
            Assert.That(m_Files.AttemptPayload, Is.EqualTo(prepared), "Retry commits the same migration, without reprocessing progress");
            yield return WaitPlaying();
            Assert.That(Game.Map.Topology.Version, Is.EqualTo(MineTopologyCatalog.SupportedVersion));
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(old.Run.Wallet));
            Assert.That(OutpostJson.Serialize(Game.State.Run.Targets), Is.EqualTo(OutpostJson.Serialize(old.Run.Targets)));
            Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment, Game.Configs.Cards), Is.Empty);
        }

        [UnityTest]
        public IEnumerator LoadingNarrowMine_FailedArchive_PreservesGeometryAndProgress()
        {
            var old = SeedNarrowDebugMine();
            m_Files.FailTopologyArchive = true;
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Assert.That(Game.SaveFailed, Is.False);
            Assert.That(Game.Map.Topology.Version, Is.EqualTo(MineTopologyCatalog.LegacyVersion));
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(old.Run.Wallet));
            Assert.That(OutpostJson.Serialize(Game.State.Run.Targets), Is.EqualTo(OutpostJson.Serialize(old.Run.Targets)));
            Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment, Game.Configs.Cards), Is.Empty);
        }

        [UnityTest]
        public IEnumerator LoadingNarrowMine_CardsSaveRetry_AfterArchiveFailure_CompletesLoading()
        {
            var old = SeedNarrowDebugMine();
            old.Profile.CardsVersion = 0;
            var saves = new OutpostSaveService(m_Dir, m_Files) { Configs = Game.Configs };
            Assert.That(saves.Save(old, OutpostSaveSlot.Debug, OutpostLevelAdapter.ForRun(Game.Balance, old.Run)).Ok, Is.True);
            m_Files.FailTopologyArchive = true;
            Action failAfterLoading = () => { if (Game.IsDebugProfile && Game.Map.Topology.Version == MineTopologyCatalog.LegacyVersion) m_Files.FailWrites = true; };
            Game.SessionChanged += failAfterLoading;
            Game.SwitchProfile(true);
            Game.SessionChanged -= failAfterLoading;
            Assert.That(Game.SaveFailed, Is.True);
            Assert.That(Game.State.Profile.CardsVersion, Is.EqualTo(1));
            m_Files.FailWrites = false;
            Game.RetrySave();
            yield return WaitPlaying();
            Assert.That(Game.SaveFailed, Is.False);
            Assert.That(Game.Map.Topology.Version, Is.EqualTo(MineTopologyCatalog.LegacyVersion));
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(old.Run.Wallet));
            Assert.That(OutpostJson.Serialize(Game.State.Run.Targets), Is.EqualTo(OutpostJson.Serialize(old.Run.Targets)));
            Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment, Game.Configs.Cards), Is.Empty);
        }
        [UnityTest]
        public IEnumerator DefaultRoute_AutonomousDelivery_WindowsDoNotPause()
        {
            Assert.That(GameBootstrap.Controller, Is.Null);
            Assert.That(UnityEngine.Object.FindAnyObjectByType<GameController>(), Is.Null);
            Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(2));
            Game.Hud.ShowBase();
            yield return Advance(20);
            Assert.That(Game.Hud.BaseOpen, Is.True);
            Assert.That(Game.State.Run.TotalCredited.Sign, Is.GreaterThan(0));
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(Game.State.Run.TotalCredited - Game.State.Run.TotalSpent));
            Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment), Is.Empty);
        }
        [UnityTest]
        public IEnumerator GroundTapAndSelection_DoNotCreditCargo()
        {
            yield return Advance(4);
            var before = Game.State.Run.Wallet;
            Game.SelectWorker(1);
            Game.SelectTarget(Game.Map.Targets[0].Id);
            Game.SelectBase();
            Game.ClearSelection();
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(before));
            Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(2));
        }
        [UnityTest]
        public IEnumerator FailedPurchase_RetryWritesIdenticalSnapshot_WithoutSecondSpend()
        {
            yield return Advance(20);
            var quote = Game.Quote(UpgradeBranch.Pickaxes, BuyMode.One);
            Assert.That(quote.CanBuy, Is.True);
            m_Files.FailWrites = true;
            var result = Game.Buy(UpgradeBranch.Pickaxes, BuyMode.One);
            Assert.That(result.Ok, Is.False);
            Assert.That(Game.SaveFailed, Is.True);
            string snapshot = m_Files.AttemptPayload;
            var state = OutpostSaveService.DeserializeState(snapshot);
            m_Clock.Advance(0.5);
            yield return null;
            Assert.That(Game.State.Run.Levels[(int)UpgradeBranch.Pickaxes], Is.EqualTo(1));
            Assert.That(Game.Buy(UpgradeBranch.Pickaxes, BuyMode.One).Ok, Is.False);
            m_Files.FailWrites = false;
            int retryIndex = m_Files.AttemptPayloads.Count;
            Game.RetrySave();
            // Other saves may follow the successful retry; the retried economic commit itself must be byte-for-byte identical.
            Assert.That(m_Files.AttemptPayloads[retryIndex], Is.EqualTo(snapshot));
            Assert.That(Game.State.Run.TotalSpent, Is.EqualTo(state.Run.TotalSpent));
            Assert.That(Game.SaveFailed, Is.False);
        }
        [UnityTest]
        public IEnumerator RepeatedPauseFocusCallbacks_ApplyOfflineOnce()
        {
            yield return Advance(5);
            Game.SendMessage("OnApplicationPause", true);
            Game.SendMessage("OnApplicationFocus", false);
            var before = OutpostJson.DeepCopy(Game.State);
            m_Clock.Advance(60);
            var expected = new OfflineSimulator(Game.Map, Game.Configs).Begin(before, m_Clock.UtcNowMs);
            expected.RunToEnd();
            Game.SendMessage("OnApplicationPause", false);
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
            Game.SendMessage("OnApplicationFocus", true);
            Game.SendMessage("OnApplicationFocus", true);
            yield return WaitPlaying();
            Assert.That(Game.State.Run.TotalCredited, Is.EqualTo(expected.State.Run.TotalCredited));
            Assert.That(Game.State.Run.Now, Is.EqualTo(expected.State.Run.Now));
            Assert.That(Game.PendingOffline, Is.Not.Null);
            var saves = new OutpostSaveService(m_Dir) { Configs = Game.Configs };
            Assert.That(saves.Load().State.Run.LastProcessedUtcMs, Is.EqualTo(m_Clock.UtcNowMs));
            Game.DismissOffline();
            Game.SendMessage("OnApplicationPause", false);
            Game.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.That(Game.PendingOffline, Is.Null);
        }
        [UnityTest]
        public IEnumerator SavedEightHourRun_ReopenAfterDay_AppliesThreeRealHours_WithoutResettingBed()
        {
            yield return Advance(5);
            Game.State.Run.Balance.Version = "outpost-2.1-b2";
            Game.State.Run.Balance.OfflineCapMs = 8L * 3600 * 1000;
            Game.State.Profile.Cards["ca051"] = new CardProgress { Level = 7, Copies = 41 };
            string savedBalance = Game.State.Run.Balance.CanonicalJson();
            Game.SendMessage("OnApplicationPause", true);
            var before = OutpostJson.DeepCopy(Game.State);
            m_Clock.Advance(24 * 3600);
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return WaitPlaying();
            Assert.That(Game.PendingOffline, Is.Not.Null);
            Assert.That(Game.PendingOffline.ElapsedMs, Is.EqualTo(24L * 3600 * 1000));
            Assert.That(Game.PendingOffline.AppliedMs, Is.EqualTo(3L * 3600 * 1000));
            Assert.That(Game.State.Run.Now, Is.EqualTo(before.Run.Now + 3L * 3600 * 1_000_000));
            Assert.That(Game.State.Run.Balance.CanonicalJson(), Is.EqualTo(savedBalance));
            Assert.That(Game.State.Profile.Cards["ca051"].Level, Is.EqualTo(7));
            Assert.That(Game.State.Profile.Cards["ca051"].Copies, Is.GreaterThanOrEqualTo(41));
            Assert.That(Game.Stats.CardUnload, Is.GreaterThanOrEqualTo(Rational.Of(7, 20)));
            Assert.That(Game.State.Run.Zone, Is.EqualTo(before.Run.Zone));
            Game.DismissOffline();
            long processedTick = Game.State.Run.Now;
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return WaitPlaying();
            Assert.That(Game.PendingOffline, Is.Null, "повторный запуск не начисляет отброшенные 21 час");
            Assert.That(Game.State.Run.Now, Is.EqualTo(processedTick));
            Assert.That(Game.State.Run.LastProcessedUtcMs, Is.EqualTo(m_Clock.UtcNowMs));
        }
        [UnityTest]
        public IEnumerator LongFrame_OfflineResultHiddenUntilCommit_RetrySameSnapshot()
        {
            var online = Game.Sim;
            m_Files.FailWrites = true;
            m_Clock.Advance(30);
            yield return null;
            for (int i = 0; i < 1000 && !Game.SaveFailed; i++) yield return null;
            Assert.That(Game.SaveFailed, Is.True);
            Assert.That(Game.Sim, Is.SameAs(online));
            Assert.That(Game.PendingOffline, Is.Null);
            string snapshot = m_Files.AttemptPayload;
            m_Files.FailWrites = false;
            Game.RetrySave();
            Assert.That(m_Files.AttemptPayload, Is.EqualTo(snapshot));
            Assert.That(Game.Sim, Is.Not.SameAs(online));
            Assert.That(Game.PendingOffline.GoldDelivered.Sign, Is.GreaterThan(0));
        }
        [UnityTest]
        public IEnumerator MainRejectsEveryDebugMutation_DebugNeverChangesMainSave()
        {
            var mainState = OutpostSaveService.SerializeState(Game.State);
            Game.SimSpeed = 100;
            Game.LoadZone(50); Game.GrantTestKit(); Game.CompleteStageNow(); Game.RestartDebugCampaign();
            Assert.That(Game.SimSpeed, Is.EqualTo(1));
            Assert.That(OutpostSaveService.SerializeState(Game.State), Is.EqualTo(mainState));
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            string mainPath = new OutpostSaveService(m_Dir).MainPath(OutpostSaveSlot.Main);
            byte[] savedMain = File.ReadAllBytes(mainPath);
            Game.LoadZone(50); Game.GrantTestKit(); Game.SimSpeed = 100;
            yield return Advance(2);
            Assert.That(File.ReadAllBytes(mainPath), Is.EqualTo(savedMain));
            Assert.That(Game.State.Run.Zone, Is.EqualTo(50));
            Game.SwitchProfile(false);
            yield return WaitPlaying();
            Assert.That(Game.State.Run.Zone, Is.EqualTo(1));
            Assert.That(Game.State.Profile.Items.Count, Is.EqualTo(0));
            Assert.That(Game.SimSpeed, Is.EqualTo(1));
        }
        [UnityTest]
        public IEnumerator CompletionAndTransition_AreCommittedBeforeUi_KeepEquipmentResetLocal()
        {
            // Separate Debug fixture processes real core logistics without fabricated rewards.
            Game.SwitchProfile(true);
            Game.CompleteStageNow();
            yield return WaitPlaying();
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
            Assert.That(Game.State.Profile.RewardLog.Count, Is.EqualTo(1));
            // Первая сессия: шахта 1 даёт только детали (stage_rewards.json NoItem); экипировка между шахтами проверяется на
            // предмете, полученном debug-набором.
            Assert.That(StageProgressionService.CompletionReward(Game.State).ItemId, Is.Null, "шахта 1 — без предмета");
            EquipmentService.Grant(Game.State.Profile, "pickaxe.common", Game.Configs);
            Assert.That(Game.Equip("pickaxe.common").Ok, Is.True);
            long parts = Game.State.Profile.Parts;
            m_Files.FailWrites = true;
            Game.ContinueToNextMine();
            Assert.That(Game.State.Run.Zone, Is.EqualTo(1));
            Assert.That(Game.SaveFailed, Is.True);
            string snapshot = m_Files.AttemptPayload;
            Game.ContinueToNextMine();
            Assert.That(m_Files.AttemptPayload, Is.EqualTo(snapshot));
            m_Files.FailWrites = false;
            Game.RetrySave();
            Assert.That(m_Files.AttemptPayload, Is.EqualTo(snapshot));
            yield return WaitPlaying();
            Assert.That(Game.State.Run.Zone, Is.EqualTo(2));
            Assert.That(Game.State.Run.Levels, Is.EqualTo(new int[OutpostIds.BranchCount]));
            Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(2));
            Assert.That(Game.State.Profile.Equipped[(int)EquipSlot.Pickaxe], Is.EqualTo("pickaxe.common"));
            Assert.That(Game.State.Profile.Parts, Is.EqualTo(parts));
            Assert.That(Game.State.Profile.RewardLog.Count, Is.EqualTo(1));
        }
        [UnityTest]
        public IEnumerator NormalTransition_DoesNotOpenOfflineSummary()
        {
            Game.SwitchProfile(true);
            Game.CompleteStageNow();
            yield return WaitPlaying();
            Game.ContinueToNextMine();
            for (int frame = 0; frame < 1000 && Game.Phase != OutpostControllerPhase.Playing; frame++)
            {
                m_Clock.Advance(0.02);
                yield return null;
            }
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
            Assert.That(Game.State.Run.Zone, Is.EqualTo(2));
            Assert.That(Game.State.Run.Now, Is.GreaterThan(0));
            Assert.That(Game.PendingOffline, Is.Null, "обычное затемнение не является отсутствием игрока");
        }

        /// <summary>
        /// Крючки playable-сборки по умолчанию выключены: профиль b4 с двумя котиками, safe area без резерва снизу, доли панелей
        /// как прежде, окно «While you were away» после отсутствия, режиссёра playable на контроллере нет; магазин целиком (с «+»
        /// валют и плитками не по карману); подсказок с лапкой нет — обучения в игре нет.
        /// </summary>
        [UnityTest]
        public IEnumerator PlayableHooks_OffByDefault_MainBalanceLayoutOfflineUnchanged()
        {
            Assert.That(OutpostController.ConfigsOverride, Is.Null);
            Assert.That(OutpostController.SuppressOfflineSummary, Is.False);
            Assert.That(OutpostHud.BottomReserve, Is.Zero);
            Assert.That(Game.Configs.Balance.Version, Is.EqualTo(OutpostIds.BalanceVersion));
            Assert.That(Game.Configs.Balance.StartWorkers, Is.EqualTo(2));
            Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(2));
            Assert.That(Game.GetComponents<MonoBehaviour>().Any(b => b.GetType().Namespace == "CatMiners.Playable"), Is.False);
            // Магазин основной игры целиком, «Reset progress» — обычный сброс, подсказки обучения нет.
            Assert.That(Game.Configs.Store.Items.Any(d => d.Kind == StoreItemKind.Gems), Is.True);
            Assert.That(Game.Configs.Store.Version, Is.EqualTo(OutpostResources.LoadConfigs().Store.Version));
            Assert.That(Game.Hud.ResetProgressOverride, Is.Null);
            Assert.That(Game.Hud.GetComponentsInChildren<TutorialHint>(true).Where(h => h.name != "GuideHint"), Is.Empty,
                "в HUD нет подсказки прежнего обучения (указатель первой сессии — GuideHint)");
            Assert.That(Game.Hud.StoreHideUnpayable, Is.False);
            yield return Advance(0.5);
            // Магазин основной игры: «+» эликсира и гемов и все плитки на месте (в том числе те, что не по карману).
            Game.Hud.ShowStore();
            yield return null;
            var store = Game.Hud.transform.Find("SafeArea/Store");
            Assert.That(store.Find("currencies/elixir/plus").gameObject.activeSelf, Is.True);
            Assert.That(store.Find("currencies/gems/plus").gameObject.activeSelf, Is.True);
            var content = store.Find("viewport/content");
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);
                Assert.That(child.gameObject.activeSelf, Is.True, child.name);
                if (child.name.StartsWith("grid_"))
                    for (int j = 0; j < child.childCount; j++) Assert.That(child.GetChild(j).gameObject.activeSelf, Is.True, child.GetChild(j).name);
            }
            Game.Hud.HideStore();
            yield return null;
            var safe = (RectTransform)Game.Hud.transform.Find("SafeArea");
            Assert.That(safe.offsetMin, Is.EqualTo(Vector2.zero));
            var bottom = (RectTransform)safe.Find("Bottom");
            Assert.That(Game.Hud.BottomInset, Is.EqualTo(Mathf.Clamp(bottom.rect.height / safe.rect.height, 0f, 0.45f)).Within(1e-4f));
            Game.SendMessage("OnApplicationFocus", false);
            m_Clock.Advance(60);
            Game.SendMessage("OnApplicationFocus", true);
            yield return WaitPlaying();
            Assert.That(Game.PendingOffline, Is.Not.Null, "итог отсутствия показывается");
        }

        [UnityTest]
        public IEnumerator OfflineSaveRetry_AccumulatesOriginalSummary()
        {
            m_Files.FailWrites = true;
            // Allow the wider routes to finish; this test checks retry accounting, not a 15-minute layout budget.
            const int absenceSeconds = 1800;
            m_Clock.Advance(absenceSeconds);
            yield return null;
            for (int frame = 0; frame < 1000 && !Game.SaveFailed; frame++) yield return null;
            Assert.That(Game.SaveFailed, Is.True);
            m_Clock.Advance(10);
            m_Files.FailWrites = false;
            Game.RetrySave();
            yield return WaitPlaying();
            Assert.That(Game.PendingOffline.ElapsedMs, Is.EqualTo((absenceSeconds + 10) * 1000L));
            Assert.That(Game.PendingOffline.GoldDelivered, Is.EqualTo(Game.State.Run.TotalCredited));
            Assert.That(Game.PendingOffline.StageCompleted, Is.True);
            Assert.That(Game.PendingOffline.ChestsOpened, Is.EqualTo(Game.State.Run.Counters.ChestsOpened));
            Assert.That(Game.PendingOffline.CardsGained, Is.EqualTo(Game.State.Run.Counters.CardsGained));
            Assert.That(Game.PendingOffline.ElixirGained, Is.EqualTo(Game.State.Run.Counters.ElixirGained));
        }

        [UnityTest]
        public IEnumerator TimeSpentInCatchUp_IsIncludedInSubsequentOnlineAdvance()
        {
            Game.SwitchProfile(true);
            Game.LoadZone(133);
            long initialTick = Game.Sim.Now;
            m_Clock.Advance(1200);
            double originMono = m_Clock.MonotonicSeconds;
            yield return null;
            for (int frame = 0; frame < 2000 && Game.Phase != OutpostControllerPhase.Playing; frame++)
            {
                m_Clock.Advance(0.25);
                yield return null;
            }
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
            m_Clock.Advance(0.1);
            yield return null;
            long expected = initialTick + 1200L * 1000000 + (long)Math.Floor((m_Clock.MonotonicSeconds - originMono) * 1000000);
            Assert.That(Math.Abs(Game.Sim.Now - expected), Is.LessThanOrEqualTo(1), "остаток микросекунды часов");
            Assert.That(Game.State.Run.LastProcessedUtcMs, Is.EqualTo(m_Clock.UtcNowMs));
        }

        [UnityTest]
        public IEnumerator DebugFinish_BlocksRestartUntilItCompletes()
        {
            Game.SwitchProfile(true);
            string runId = Game.State.Run.RunId;
            Game.CompleteStageNow();
            Game.RestartDebugCampaign();
            Assert.That(Game.State.Run.RunId, Is.EqualTo(runId));
            yield return WaitPlaying();
            Assert.That(Game.State.Run.RunId, Is.EqualTo(runId));
            Assert.That(Game.State.Profile.RewardLog.Count, Is.EqualTo(1));
        }

        void AssertFreshProgress()
        {
            var state = Game.State;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
            Assert.That(state.Run.Zone, Is.EqualTo(1));
            Assert.That(state.Profile.CurrentZone, Is.EqualTo(1));
            Assert.That(state.Profile.HighestZone, Is.EqualTo(1));
            Assert.That(state.Profile.CampaignComplete, Is.False);
            Assert.That(state.Profile.CompletedZones, Is.Empty);
            Assert.That(state.Profile.RewardLog, Is.Empty);
            Assert.That(state.Profile.Cards, Is.Empty);
            Assert.That(state.Profile.Elixir, Is.Zero);
            Assert.That(state.Profile.Items, Is.Empty);
            Assert.That(state.Profile.Equipped.All(id => id == null), Is.True);
            Assert.That(state.Profile.Parts, Is.Zero);
            Assert.That(state.Run.Levels, Is.EqualTo(new int[OutpostIds.BranchCount]));
            Assert.That(state.Run.Workers.Count, Is.EqualTo(2));
            Assert.That(state.Run.Wallet.IsZero, Is.True);
            Assert.That(state.Run.TotalCredited.IsZero, Is.True);
            Assert.That(state.Run.TotalSpent.IsZero, Is.True);
            Assert.That(state.Run.Targets.All(t => !t.Destroyed), Is.True);
            Assert.That(Game.SimSpeed, Is.EqualTo(1));
            Assert.That(Game.PendingOffline, Is.Null);
            Assert.That(state.Profile.CardKeys, Does.Contain(CardService.LegacyImportKey));
            Assert.That(OutpostInvariants.Check(state, Game.Map, Game.Configs.Equipment, Game.Configs.Cards), Is.Empty);
        }

        [UnityTest]
        public IEnumerator DevReset_MainConfirmation_CancelThenStartsFreshAndPersists()
        {
            Game.SwitchProfile(true);
            Game.LoadZone(10);
            Game.GrantTestKit();
            Game.GrantTestCards();
            Game.CompleteStageNow();
            yield return WaitPlaying();
            var progressed = OutpostJson.DeepCopy(Game.State);
            progressed.Profile.Settings.Music = 0.25f;
            var saves = new OutpostSaveService(m_Dir) { Configs = Game.Configs };
            Assert.That(saves.Save(progressed, OutpostSaveSlot.Main, Game.Map).Ok, Is.True);
            Game.SwitchProfile(false);
            yield return WaitPlaying();
            Assert.That(Game.State.Profile.Cards.Count, Is.GreaterThan(0));
            Assert.That(Game.State.Profile.Items.Count, Is.GreaterThan(0));
            byte[] debugSave = File.ReadAllBytes(saves.MainPath(OutpostSaveSlot.Debug));
            string before = OutpostSaveService.SerializeState(Game.State);

            // RuntimeInitialize installs the panel on app launch; a test loads CoreGame later.
            if (UnityEngine.Object.FindAnyObjectByType<CatMiners.DevTools.OutpostDevPanel>() == null)
                typeof(CatMiners.DevTools.OutpostDevPanel).GetMethod("Install", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
            var toggle = Game.Hud.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b => b.name == "OutpostDevToggle");
            toggle.onClick.Invoke();
            var reset = Game.Hud.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b => b.name == "OutpostResetProgress");
            Assert.That(reset.interactable, Is.True, "Main тоже можно явно сбросить через Dev");
            reset.onClick.Invoke();
            Assert.That(Game.Hud.ModalOpen, Is.True);
            Assert.That(Game.Hud.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Any(t => t.text.Contains("profile Main")), Is.True);
            yield return null;
            Game.Hud.HandleBack();
            Assert.That(Game.Hud.ModalOpen, Is.False);
            Assert.That(OutpostSaveService.SerializeState(Game.State), Is.EqualTo(before), "Отмена не меняет прогресс");

            reset.onClick.Invoke();
            var confirm = Game.Hud.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b => b.GetComponentInChildren<TMPro.TextMeshProUGUI>()?.text == Loc.T("outpost.dev.reset_confirm"));
            confirm.onClick.Invoke();
            AssertFreshProgress();
            Assert.That(Game.State.Profile.Settings.Music, Is.EqualTo(0.25f));
            Assert.That(Game.Hud.ModalOpen, Is.False);
            Assert.That(Game.Hud.transform.Find("SafeArea/OutpostDevPanel").gameObject.activeSelf, Is.False);
            Assert.That(File.ReadAllBytes(saves.MainPath(OutpostSaveSlot.Debug)), Is.EqualTo(debugSave));
            Assert.That(File.ReadAllBytes(saves.MainPath(OutpostSaveSlot.Main)), Is.EqualTo(File.ReadAllBytes(saves.BakPath(OutpostSaveSlot.Main))));
            Assert.That(Directory.GetDirectories(saves.SlotDirectory(OutpostSaveSlot.Main), "restart-*").Length, Is.EqualTo(1));

            var legacy = new CatMiners.Core.Sim.GameState();
            legacy.Player.Cards["ca003"] = new CardState { Level = 5, Duplicates = 50 };
            File.WriteAllText(Path.Combine(m_Dir, "save_main.json"), new JObject
            {
                ["Payload"] = SaveService.SerializeState(legacy),
                ["savedAtUtcMs"] = m_Clock.UtcNowMs
            }.ToString());
            // Reload the actual scene against the same files, rather than only checking in-memory state.
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return WaitPlaying();
            AssertFreshProgress();
            Assert.That(Game.State.Profile.Settings.Music, Is.EqualTo(0.25f));
            Assert.That(File.ReadAllBytes(saves.MainPath(OutpostSaveSlot.Debug)), Is.EqualTo(debugSave));
        }

        IEnumerator ResetFailure(bool backupFailure)
        {
            yield return Advance(20);
            var old = Game.Sim;
            Assert.That(Game.State.Run.Wallet.Sign, Is.GreaterThan(0));
            m_Files.FailWrites = !backupFailure;
            m_Files.FailBackupWrites = backupFailure;
            Game.ResetProgress();
            Assert.That(Game.SaveFailed, Is.True);
            Assert.That(Game.Sim, Is.SameAs(old), "новая игра появляется только после успешной записи");
            Assert.That(Game.CanResetProgress, Is.False);
            string snapshot = m_Files.AttemptPayload;
            int attempts = m_Files.AttemptPayloads.Count;
            Game.ResetProgress();
            Assert.That(m_Files.AttemptPayloads.Count, Is.EqualTo(attempts), "повторный тап не создаёт другой reset");
            m_Clock.Advance(0.5);
            m_Files.FailWrites = false;
            m_Files.FailBackupWrites = false;
            Game.RetrySave();
            Assert.That(m_Files.AttemptPayload, Is.EqualTo(snapshot));
            Assert.That(Game.SaveFailed, Is.False);
            AssertFreshProgress();
            Assert.That(Game.Sim, Is.Not.SameAs(old));
            var saves = new OutpostSaveService(m_Dir);
            Assert.That(File.ReadAllBytes(saves.MainPath(OutpostSaveSlot.Main)), Is.EqualTo(File.ReadAllBytes(saves.BakPath(OutpostSaveSlot.Main))));
        }

        [UnityTest]
        public IEnumerator DevReset_SaveFailure_KeepsPreviousGameUntilIdenticalRetry() => ResetFailure(false);

        [UnityTest]
        public IEnumerator DevReset_BackupFailure_KeepsPreviousGameUntilIdenticalRetry() => ResetFailure(true);

        [UnityTest]
        public IEnumerator PointerTapSelectsWorker_BackgroundDragScrolls_ModalsCancelGesture()
        {
            // A batch Editor has no focused Game View. Route synthetic devices to the
            // runtime for this test, then restore the project's normal focus policy.
            var background = InputSystem.settings.backgroundBehavior;
            var editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                // Base/worker views sync in Update; a newly loaded board starts with a spawn catch-up pose.
                yield return null;
                Assert.That(Game.TryGetWorkerWorldPosition(1, out var world), Is.True);
                Game.Rig.FocusOn(new Vector2(world.x, world.z));
                yield return null;
                Vector2 point = Game.Rig.Cam.WorldToScreenPoint(world);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
                yield return null;
                Assert.That(Game.Selection.Kind, Is.EqualTo(OutpostSelectionKind.Worker));
                Assert.That(Game.State.Run.Wallet.IsZero, Is.True);
                Game.ClearSelection();
                Game.Rig.FocusOn((Game.Rig.MinZ + Game.Rig.MaxZ) / 2);
                float focus = Game.Rig.FocusZ;
                Game.Hud.FieldBandPixels(out float bottom, out float top);
                point = new Vector2(Screen.width / 2f, (bottom + top) / 2);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
                yield return null;
                point.y += 80;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
                yield return null;
                Assert.That(Game.Rig.FocusZ, Is.Not.EqualTo(focus));
                Game.Hud.ShowBase();
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
                yield return null;
                Assert.That(Game.Selection.Kind, Is.EqualTo(OutpostSelectionKind.None));
                Assert.That(Game.Hud.BaseOpen, Is.True);
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.settings.backgroundBehavior = background;
                InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            }
        }
        [UnityTest]
        public IEnumerator ResumeAfterInterruptedOfflineSlice_UsesFullAbsence_StopsAtCurrentMine()
        {
            m_Clock.Advance(10);
            yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.OfflineCatchUp));
            Game.SendMessage("OnApplicationPause", true);
            var before = OutpostJson.DeepCopy(Game.State);
            m_Clock.Advance(9 * 3600);
            var expected = new OfflineSimulator(Game.Map, Game.Configs).Begin(before, m_Clock.UtcNowMs);
            expected.RunToEnd();
            Game.SendMessage("OnApplicationPause", false);
            yield return WaitPlaying();
            Assert.That(Game.State.Run.TotalCredited, Is.EqualTo(expected.State.Run.TotalCredited));
            Assert.That(Game.State.Run.Now, Is.EqualTo(expected.State.Run.Now));
            Assert.That(Game.PendingOffline.AppliedMs, Is.EqualTo(3 * 3600 * 1000));
            Assert.That(Game.State.Run.Zone, Is.EqualTo(1));
            Assert.That(Game.State.Profile.RewardLog.Count, Is.EqualTo(1));
            Game.DismissOffline();
            long marker = Game.State.Run.LastProcessedUtcMs;
            Game.SendMessage("OnApplicationFocus", false);
            m_Clock.UtcNowMs -= 10000;
            Game.SendMessage("OnApplicationFocus", true);
            yield return WaitPlaying();
            Assert.That(Game.State.Run.LastProcessedUtcMs, Is.EqualTo(marker));
            Assert.That(Game.State.Profile.RewardLog.Count, Is.EqualTo(1));
        }
        [UnityTest]
        public IEnumerator RapidStrikes_GlobalRealCooldown_SwitchingTargetsDoesNotQueueDamage()
        {
            var ids = Game.Map.Targets.Where(t => Game.CanStrike(t.Id)).OrderByDescending(t => t.Id).Take(2).Select(t => t.Id).ToArray();
            Assert.That(ids.Length, Is.EqualTo(2));
            Assert.That(Game.TryGetTargetWorldPosition(ids[0], out var first), Is.True);
            Assert.That(Game.TryGetTargetWorldPosition(ids[1], out var second), Is.True);
            var a = Game.Sim.TargetById(ids[0]);
            var b = Game.Sim.TargetById(ids[1]);
            long hpA = a.RemainingHpUnits, hpB = b.RemainingHpUnits, damage = Game.Stats.TapDamageUnits;
            Game.enabled = false; // Анимацию шагаем вручную, границы реального интервала задают ManualClock.
            try
            {
                Assert.That(Game.BeginStrike(ids[0], first), Is.True);
                for (int i = 0; i < 100; i++)
                    Assert.That(Game.BeginStrike(ids[i % 2], i % 2 == 0 ? first : second), Is.True);
                Assert.That(Game.Board.ActiveStrikes, Is.EqualTo(1), "сто касаний не создают несколько кирок");
                Game.Board.Sync(Game.Sim, 0.27f);
                Assert.That(a.RemainingHpUnits, Is.EqualTo(hpA - damage));
                Assert.That(b.RemainingHpUnits, Is.EqualTo(hpB));
                Assert.That(Game.Board.ActiveStrikes, Is.Zero);

                m_Clock.Advance(0.2);
                for (int i = 0; i < 100; i++) Assert.That(Game.BeginStrike(ids[1], second), Is.True);
                Game.Board.Sync(Game.Sim, 0.27f);
                Assert.That(Game.Board.ActiveStrikes, Is.Zero, "смена цели после анимации не отменяет паузу 0,4 с");
                Assert.That(b.RemainingHpUnits, Is.EqualTo(hpB));
                m_Clock.MonotonicSeconds = 0.399999;
                Assert.That(Game.BeginStrike(ids[1], second), Is.True);
                Assert.That(Game.Board.ActiveStrikes, Is.Zero, "за 1 мкс до границы удар ещё закрыт");

                m_Clock.MonotonicSeconds = 0.4;
                m_Clock.UtcNowMs += 200;
                Assert.That(Game.BeginStrike(ids[1], second), Is.True);
                Assert.That(Game.Board.ActiveStrikes, Is.EqualTo(1), "лишние нажатия не продлили паузу и не встали в очередь");
                Game.Board.Sync(Game.Sim, 0.27f);
                Assert.That(b.RemainingHpUnits, Is.EqualTo(hpB - damage));
                Assert.That(a.RemainingHpUnits, Is.EqualTo(hpA - damage));
                Assert.That(Game.Board.ActiveStrikes, Is.Zero);
            }
            finally { Game.enabled = true; }
            yield return null;
        }

        [UnityTest]
        public IEnumerator MouseAndTouch_ShareStrikeCooldown_ExtraTapsDoNotOpenInfoOrDealDamage()
        {
            var background = InputSystem.settings.backgroundBehavior;
            var editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var mouse = InputSystem.AddDevice<Mouse>();
            var touch = InputSystem.AddDevice<Touchscreen>();
            try
            {
                yield return null;
                var ids = Game.Map.Targets.Where(t => Game.CanStrike(t.Id)).OrderByDescending(t => t.Id).Take(2).Select(t => t.Id).ToArray();
                Assert.That(ids.Length, Is.EqualTo(2));
                Assert.That(Game.TryGetTargetWorldPosition(ids[0], out var first), Is.True);
                Assert.That(Game.TryGetTargetWorldPosition(ids[1], out var second), Is.True);
                Game.Rig.FocusOn((first.z + second.z) / 2f);
                yield return null;
                Vector2 a = Game.Rig.Cam.WorldToScreenPoint(first), b = Game.Rig.Cam.WorldToScreenPoint(second);
                var targetA = Game.Sim.TargetById(ids[0]);
                var targetB = Game.Sim.TargetById(ids[1]);
                long hpA = targetA.RemainingHpUnits, hpB = targetB.RemainingHpUnits, damage = Game.Stats.TapDamageUnits;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = a }.WithButton(MouseButton.Left));
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = a });
                yield return null;
                Assert.That(Game.Board.ActiveStrikes, Is.EqualTo(1));
                for (int i = 1; i <= 5; i++)
                {
                    InputSystem.QueueStateEvent(touch, new TouchState { touchId = i, position = b, phase = UnityEngine.InputSystem.TouchPhase.Began });
                    yield return null;
                    InputSystem.QueueStateEvent(touch, new TouchState { touchId = i, position = b, phase = UnityEngine.InputSystem.TouchPhase.Ended });
                    yield return null;
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = a }.WithButton(MouseButton.Left));
                    yield return null;
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = a });
                    yield return null;
                    Assert.That(Game.Board.ActiveStrikes, Is.LessThanOrEqualTo(1));
                }
                yield return new WaitForSecondsRealtime(0.3f);
                Assert.That(targetA.RemainingHpUnits, Is.EqualTo(hpA - damage), "мышь нанесла только первый удар");
                Assert.That(targetB.RemainingHpUnits, Is.EqualTo(hpB), "тачскрин не обошёл общий интервал");
                Assert.That(Game.Selection.Kind, Is.EqualTo(OutpostSelectionKind.None));
                Assert.That(Game.Hud.AnyWindowOpen, Is.False, "частые тапы не открывают сведения");
                Assert.That(Game.Board.ActiveStrikes, Is.Zero, "нет отложенных ударов");

                m_Clock.Advance(0.4);
                yield return null;
                InputSystem.QueueStateEvent(touch, new TouchState { touchId = 20, position = b, phase = UnityEngine.InputSystem.TouchPhase.Began });
                yield return null;
                InputSystem.QueueStateEvent(touch, new TouchState { touchId = 20, position = b, phase = UnityEngine.InputSystem.TouchPhase.Ended });
                yield return null;
                Assert.That(Game.Board.ActiveStrikes, Is.EqualTo(1), "новый тап после паузы принят");
                yield return new WaitForSecondsRealtime(0.3f);
                Assert.That(targetB.RemainingHpUnits, Is.EqualTo(hpB - damage));
            }
            finally
            {
                InputSystem.RemoveDevice(touch);
                InputSystem.RemoveDevice(mouse);
                InputSystem.settings.backgroundBehavior = background;
                InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            }
        }

        [UnityTest]
        public IEnumerator PointerTapOnRock_StrikesWithPickaxe_LongPressShowsInfo()
        {
            // ТЗ 6.6: тап/клик по камню текущего сектора — кирка игрока из воздуха, урон в кадр удара, золото не меняется;
            // удержание — сведения о цели без удара.
            var background = InputSystem.settings.backgroundBehavior;
            var editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var mouse = InputSystem.AddDevice<Mouse>();
            // Кадр клетки первой сессии (камера к клетке Mia) этому тесту ввода не нужен.
            FirstSessionTestUtil.SkipPresentation(Game);
            try
            {
                yield return null;
                // Исходные цели — без деталей обстановки (мусор, руда-заполнитель dressing-3, замок клетки первой сессии).
                Assert.That(Game.Map.Targets.Count(t => t.Piece == null), Is.EqualTo(56), "исходные цели zone1 с override арены");
                // Ближайшая к базе цель под удар: нижний ряд завалов zone1.
                Vector3 world = default;
                float best = float.MaxValue;
                int id = -1;
                foreach (var t in Game.Map.Targets)
                {
                    if (t.IsGate || t.IsDebris || !Game.CanStrike(t.Id) || !Game.TryGetTargetWorldPosition(t.Id, out var p)) continue;
                    float d = (p - Game.BaseWorldPosition).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    id = t.Id;
                    world = p;
                }
                Assert.That(id, Is.GreaterThanOrEqualTo(0), "камень под удар у базы");
                Game.Rig.FocusOn(new Vector2(world.x, world.z));
                yield return null;
                Vector2 point = Game.Rig.Cam.WorldToScreenPoint(world);
                var target = Game.Sim.TargetById(id);
                long hp = target.RemainingHpUnits;
                var wallet = Game.State.Run.Wallet;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
                yield return null;
                Assert.That(Game.Board.ActiveStrikes, Is.EqualTo(1), "кирка появилась");
                Assert.That(Game.Selection.Kind, Is.EqualTo(OutpostSelectionKind.None), "тап по камню — удар, а не сведения");
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.That(Game.Board.ActiveStrikes, Is.EqualTo(0), "анимация закончилась");
                Assert.That(target.RemainingHpUnits, Is.EqualTo(hp - Game.Stats.TapDamageUnits), "урон нанесён один раз в кадр удара");
                Assert.That(Game.State.Run.Wallet, Is.EqualTo(wallet), "удар не начисляет золото");
                Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment), Is.Empty);

                hp = target.RemainingHpUnits;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
                yield return new WaitForSecondsRealtime(OutpostInput.LongPressSeconds + 0.2f);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
                yield return null;
                yield return null;
                Assert.That(Game.Selection.Kind, Is.EqualTo(OutpostSelectionKind.Target), "удержание — сведения");
                Assert.That(Game.Selection.Id, Is.EqualTo(id));
                Assert.That(Game.Board.ActiveStrikes, Is.EqualTo(0));
                Assert.That(target.RemainingHpUnits, Is.EqualTo(hp), "удержание не бьёт");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.settings.backgroundBehavior = background;
                InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            }
        }

        [UnityTest]
        public IEnumerator LegacyZone1Save_MigratesGeometryAndKeepsProgress()
        {
            // A run predating the content override adopts wide tunnels with only its original content.
            var repo = Game.Balance;
            var configs = Game.Configs;
            // Выгрузить запущенную игру: её автосохранение не должно перекрыть подготовленный файл.
            var prep = SceneManager.CreateScene("OutpostLegacyPrep-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(prep);
            yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByName("CoreGame"));
            var profile = StageProgressionService.NewProfile(configs, 7, 1);
            profile.Parts = 42;
            profile.Items["hat.rare"] = 2;
            var legacyMap = OutpostLevelAdapter.Build(repo, 1);
            var legacy = new OutpostGameState { Profile = profile, Run = StageProgressionService.CreateRun(profile, 1, legacyMap, configs, repo, m_Clock.UtcNowMs) };
            Assert.That(legacy.Run.MapOverride, Is.Null);
            var legacySim = new OutpostSimulation(legacy, legacyMap, configs);
            legacySim.Advance(20 * OutpostIds.TicksPerSecond);
            legacy.Run.LastProcessedUtcMs = m_Clock.UtcNowMs;
            string runId = legacy.Run.RunId;
            var credited = legacy.Run.TotalCredited;
            var hp = legacy.Run.Targets.Select(t => t.RemainingHpUnits).ToArray();
            var service = new OutpostSaveService(m_Dir) { Configs = configs, MapResolver = s => OutpostLevelAdapter.ForRun(repo, s.Run) };
            foreach (var path in new[] { service.MainPath(OutpostSaveSlot.Main), service.BakPath(OutpostSaveSlot.Main), service.TmpPath(OutpostSaveSlot.Main) })
                if (File.Exists(path)) File.Delete(path);
            Assert.That(service.Save(legacy, OutpostSaveSlot.Main, legacyMap).Ok, Is.True);

            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return WaitPlaying();
            Assert.That(Game.State.Run.MapOverride, Is.Null);
            Assert.That(Game.State.Run.MapTopology.Version, Is.EqualTo(MineTopologyCatalog.SupportedVersion));
            Assert.That(Game.Map.RequiredCount, Is.EqualTo(legacyMap.RequiredCount));
            Assert.That(Game.Map.Width, Is.GreaterThanOrEqualTo(25));
            Assert.That(Game.State.Run.TotalCredited, Is.EqualTo(credited));
            CollectionAssert.AreEqual(hp, Game.State.Run.Targets.Select(t => t.RemainingHpUnits).ToArray());
            Assert.That(Game.State.Profile.Parts, Is.EqualTo(42));
            Assert.That(Game.State.Profile.ItemLevel("hat.rare"), Is.EqualTo(2));
            Assert.That(Game.State.Run.RunId, Is.EqualTo(runId));
            var reread = new OutpostSaveService(m_Dir) { Configs = configs, MapResolver = s => OutpostLevelAdapter.ForRun(repo, s.Run) }.Load(OutpostSaveSlot.Main);
            Assert.That(reread.State, Is.Not.Null);
            Assert.That(reread.State.Run.MapOverride, Is.Null);
            Assert.That(reread.State.Run.MapTopology.Version, Is.EqualTo(MineTopologyCatalog.SupportedVersion));
            Assert.That(Directory.GetDirectories(service.SlotDirectory(OutpostSaveSlot.Main), "pre-wide-topology-*").Length, Is.EqualTo(1));
        }

        /// <summary>
        /// ТЗ 2.2 §10.3, §16.2 (ворота G3): сохранение схемы 2 (до заводов) при запуске игры — не Corrupt/Incompatible и не окно
        /// ошибки, а архив &lt;слот&gt;/pre-factories/ и новая кампания схемы 3 с тостом «The game was updated: progress started
        /// over». Настройки и сыгранное время переносятся, прогресс (шахты, гемы, детали, предметы) — нет.
        /// </summary>
        [UnityTest]
        public IEnumerator Schema2Save_LoadStartsNewCampaign_NoError_ResetToast()
        {
            yield return Advance(20);
            Assert.That(Game.State.Run.Wallet.Sign, Is.GreaterThan(0), "прежняя игра успела заработать");
            var configs = Game.Configs;
            var old = OutpostJson.DeepCopy(Game.State);
            var oldMap = Game.Map;
            old.Profile.Settings.Music = 0.25f;
            old.Profile.PlayTimeMs = 4_321_000L;
            long oldNow = old.Run.Now;
            Assert.That(oldNow, Is.GreaterThan(0L), "прежний run шёл 20 с");
            // Выгрузить игру: её автосохранение не должно перекрыть подготовленный файл.
            var prep = SceneManager.CreateScene("OutpostSchema2Prep-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(prep);
            yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByName("CoreGame"));
            var service = new OutpostSaveService(m_Dir) { Configs = configs };
            string main = service.MainPath(OutpostSaveSlot.Main);
            foreach (var path in new[] { main, service.BakPath(OutpostSaveSlot.Main), service.TmpPath(OutpostSaveSlot.Main) })
                if (File.Exists(path)) File.Delete(path);
            Assert.That(service.Save(old, OutpostSaveSlot.Main, oldMap).Ok, Is.True);

            // Файл прежней версии: конверт и профиль схемы 2, правила 2.1, adapter-1, топология v2, без полей заводов ТЗ 2.2.
            var env = JObject.Parse(File.ReadAllText(main));
            var payload = JObject.Parse((string)env["Payload"]);
            env["SchemaVersion"] = 2;
            env["RulesVersion"] = "outpost-2.1-r1";
            env["BalanceVersion"] = "outpost-2.1-b4";
            var profile = (JObject)payload["Profile"];
            profile["SchemaVersion"] = 2;
            profile["Gems"] = 999;
            profile["Parts"] = 42;
            profile["CurrentZone"] = 4;
            profile["CompletedZones"] = new JArray(1, 2, 3);
            profile["Items"] = new JObject { ["pickaxe.rare"] = 3 };
            var run = (JObject)payload["Run"];
            run["RulesVersion"] = "outpost-2.1-r1";
            run["AdapterVersion"] = "adapter-1";
            if (run["Balance"] is JObject balance) balance["Version"] = "outpost-2.1-b4";
            if (run["MapTopology"] is JObject topology) topology["Version"] = "wide-branching-mines-2";
            foreach (var field in new[] { "FactoryRules", "Factories", "PipeBatches", "NextPipeBatchSeq", "Warehouse", "GoodsArrived", "GoodsSold", "GoodsSoldGold", "DebugGoods" })
                run.Remove(field);
            string text = payload.ToString(Newtonsoft.Json.Formatting.None);
            env["Payload"] = text;
            env["PayloadSha256"] = OutpostJson.Sha256Hex(text);
            File.WriteAllText(main, env.ToString(Newtonsoft.Json.Formatting.None));
            byte[] oldBytes = File.ReadAllBytes(main);
            Assert.That(service.Load(OutpostSaveSlot.Main).Status, Is.EqualTo(OutpostLoadStatus.Outdated), "ядро узнаёт схему 2");

            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return WaitPlaying();
            Assert.That(Game.Phase, Is.Not.EqualTo(OutpostControllerPhase.Error));
            Assert.That(Game.ErrorText, Is.Null.Or.Empty);
            string toast = Loc.T("outpost.toast.save_reset");
            var toastText = Game.Hud.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).First(t => t.name == "Toast");
            for (int frame = 0; frame < 60 && !(toastText.gameObject.activeInHierarchy && toastText.text == toast); frame++) yield return null;
            Assert.That(toastText.gameObject.activeInHierarchy, Is.True, "тост сброса показан");
            Assert.That(toastText.text, Is.EqualTo(toast));

            var state = Game.State;
            Assert.That(state.Profile.SchemaVersion, Is.EqualTo(OutpostIds.SchemaVersion));
            // RunId новой кампании снова «run-1» (NextRunSeq начинается заново), поэтому новый run узнаётся по времени шахты:
            // часы теста стоят, новый run не прожил прежние 20 с.
            Assert.That(state.Run.Now, Is.LessThan(oldNow), "новая кампания: run начат заново");
            Assert.That(state.Run.Zone, Is.EqualTo(1));
            Assert.That(state.Profile.CompletedZones, Is.Empty);
            Assert.That(state.Profile.Gems, Is.Not.EqualTo(999), "прогресс не переносится");
            Assert.That(state.Profile.Parts, Is.Zero);
            Assert.That(state.Profile.ItemLevel("pickaxe.rare"), Is.Zero);
            Assert.That(state.Profile.Settings.Music, Is.EqualTo(0.25f), "настройки переносятся");
            Assert.That(state.Profile.PlayTimeMs, Is.GreaterThanOrEqualTo(4_321_000L), "сыгранное время переносится");
            Assert.That(OutpostInvariants.Check(state, Game.Map, Game.Configs.Equipment, Game.Configs.Cards), Is.Empty);

            string archived = Path.Combine(service.PreFactoriesDirectory(OutpostSaveSlot.Main), Path.GetFileName(main));
            Assert.That(File.Exists(archived), Is.True, "архив pre-factories/");
            Assert.That(File.ReadAllBytes(archived), Is.EqualTo(oldBytes), "архив — прежний файл байт в байт");
            Assert.That((int)JObject.Parse(File.ReadAllText(main))["SchemaVersion"], Is.EqualTo(OutpostIds.SchemaVersion), "записана схема 3");
            Assert.That(File.ReadAllBytes(main), Is.EqualTo(File.ReadAllBytes(service.BakPath(OutpostSaveSlot.Main))), "resetBackup: прежний main не остался в bak");
            var repo = Game.Balance;
            var reread = new OutpostSaveService(m_Dir) { Configs = Game.Configs, MapResolver = st => OutpostLevelAdapter.ForRun(repo, st.Run) }.Load(OutpostSaveSlot.Main);
            Assert.That(reread.Status, Is.EqualTo(OutpostLoadStatus.Loaded), string.Join("; ", reread.Messages));
            Assert.That(reread.State.Run.RunId, Is.EqualTo(state.Run.RunId));
        }

        [UnityTest]
        public IEnumerator InvalidProfile_ShowsError_PreservesOriginal_NoReset()
        {
            var service = new OutpostSaveService(m_Dir);
            File.WriteAllText(service.MainPath(OutpostSaveSlot.Main), "invalid-profile");
            if (File.Exists(service.BakPath(OutpostSaveSlot.Main))) File.WriteAllText(service.BakPath(OutpostSaveSlot.Main), "invalid-backup");
            LogAssert.Expect(LogType.Error, new Regex("\\[Outpost\\] Corrupt"));
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Error));
            Assert.That(Game.State, Is.Null);
            Game.RetrySave();
            m_Clock.Advance(10);
            yield return null;
            Assert.That(File.ReadAllText(service.MainPath(OutpostSaveSlot.Main)), Is.EqualTo("invalid-profile"));
        }
    }
}
