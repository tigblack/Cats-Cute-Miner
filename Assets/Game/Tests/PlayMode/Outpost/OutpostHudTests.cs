using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using AudioGroup = CatMiners.Core.Sim.AudioGroup;
using CommandResult = CatMiners.Core.Outpost.CommandResult;
using OfflineSummary = CatMiners.Core.Outpost.OfflineSummary;
using Object = UnityEngine.Object;

namespace CatMiners.Tests
{
    /// <summary>
    /// HUD нового режима (OutpostHud) и обучение (OutpostTutorial) через контракт IOutpostGame без контроллера: настоящее ядро
    /// zone1 за поддельным контроллером. Окна «База», «Экипировка», сведения, настройки, итоги, офлайн, ошибка и модальное
    /// окно открываются и закрываются Back, команды идут в ядро, шаги обучения показываются по событиям, словарь без
    /// пропусков, повторное обновление без изменений не выделяет память.
    /// </summary>
    public class OutpostHudTests
    {
        /// <summary>Поддельный контроллер: команды — сразу в ядро, события ядра — подписчикам после каждого шага.</summary>
        sealed class FakeOutpostGame : IOutpostGame
        {
            readonly List<OutpostEvent> m_Events = new List<OutpostEvent>();
            public int TutorialSaves, Continues, Retries;

            public OutpostSimulation Sim { get; set; }
            public OutpostGameState State => Sim != null ? Sim.State : null;
            public OutpostMap Map => Sim != null ? Sim.Map : null;
            public OutpostConfigSet Configs { get; set; }
            public TeamStats Stats => Sim != null ? Sim.Stats : null;
            public OutpostControllerPhase Phase { get; set; } = OutpostControllerPhase.Playing;
            public bool IsDebugProfile => true;
            public bool InTransition { get; set; }

            public UpgradeQuote Quote(UpgradeBranch branch, BuyMode mode) => Sim.Quote(branch, mode);
            public CommandResult Buy(UpgradeBranch branch, BuyMode mode) => Submit(OutpostCommand.BuyUpgrade(branch, mode));
            public CommandResult Equip(string itemId) => Submit(OutpostCommand.Equip(itemId));
            public CommandResult Unequip(EquipSlot slot) => Submit(OutpostCommand.Unequip(slot));
            public CommandResult EquipBest() => Submit(OutpostCommand.EquipBest());
            public CommandResult UpgradeItem(string itemId) => Submit(OutpostCommand.UpgradeItem(itemId));
            public CommandResult UpgradeCard(string cardId) => Submit(OutpostCommand.UpgradeCard(cardId));
            public CommandResult Strike(int targetId) => Submit(OutpostCommand.Strike(targetId));
            public long UtcNowMs { get; set; } = 1_800_000_000_000L;
            public CommandResult StoreBuy(string itemId) => Submit(OutpostCommand.StoreBuy(itemId, UtcNowMs));
            public int TimeSkips;

            public CommandResult UseTimeSkip(string itemId)
            {
                if (!Configs.Store.TryGet(itemId, out var def)) return CommandResult.Fail("unknown");
                var paid = StoreService.PayTimeSkip(State.Profile, def);
                if (paid.Ok) TimeSkips++;
                return paid;
            }

            public int SkipMinutesInProgress { get; set; }
            public OfflineSummary PendingSkip { get; set; }
            public void DismissSkip() => PendingSkip = null;

            CommandResult Submit(OutpostCommand cmd)
            {
                var r = Sim.Submit(cmd);
                Present();
                return r;
            }

            public OutpostSelection Selection { get; private set; } = OutpostSelection.None;
            public void SelectWorker(int workerId) => Selection = new OutpostSelection { Kind = OutpostSelectionKind.Worker, Id = workerId };
            public void SelectTarget(int targetId) => Selection = new OutpostSelection { Kind = OutpostSelectionKind.Target, Id = targetId };
            public void SelectBase() => Selection = new OutpostSelection { Kind = OutpostSelectionKind.Base, Id = -1 };
            public void ClearSelection() => Selection = OutpostSelection.None;
            public void FocusBase() { }

            Vector3 CellCenter(int cell) => new Vector3(Map.Col(cell) + 0.5f, 0f, Map.Depth - Map.Row(cell) - 0.5f);

            public bool TryGetWorkerWorldPosition(int workerId, out Vector3 world)
            {
                var w = Sim != null ? Sim.WorkerById(workerId) : null;
                world = w != null ? CellCenter(w.Cell) : default;
                return w != null;
            }

            public Vector3 BaseWorldPosition => CellCenter(Map.SpawnCell);
            public Vector3 DepotWorldPosition(int slot) => CellCenter(Map.DepotCells[slot]);

            public bool TryGetTargetWorldPosition(int targetId, out Vector3 world)
            {
                var t = Map != null ? Map.Target(targetId) : null;
                world = t != null ? CellCenter(Map.Cell(t.Column, t.Row)) : default;
                return t != null;
            }

            public bool CanContinue => Sim != null && Sim.Run.Phase == RunPhase.ReadyToContinue && !CampaignComplete && !SaveFailed;
            public bool CampaignComplete => State != null && State.Profile.CampaignComplete;
            public bool SaveFailed { get; set; }
            public void ContinueToNextMine() => Continues++;

            public void RetrySave()
            {
                Retries++;
                SaveFailed = false;
            }

            public OfflineSummary PendingOffline { get; set; }
            public float OfflineProgress01 { get; set; }
            public void DismissOffline() => PendingOffline = null;

            public CommandResult ClaimOfflineDouble()
            {
                var pending = PendingOffline;
                if (pending == null) return CommandResult.Fail("unknown");
                var r = pending.GoldDelivered.Sign > 0 ? Submit(OutpostCommand.BonusGold(pending.GoldDelivered)) : new CommandResult { Ok = true };
                if (r.Ok) PendingOffline = null;
                return r;
            }
            public void SetVolume(AudioGroup group, float value) { }
            public void SetVibration(bool on) => State.Profile.Settings.Vibration = on;
            public void SetReduceEffects(bool on) => State.Profile.Settings.ReduceEffects = on;
            public bool CanResetProgress => !SaveFailed && !InTransition && Phase == OutpostControllerPhase.Playing;
            public void ResetProgress() { }
            public void SaveTutorialProgress() => TutorialSaves++;
            public event Action<OutpostEvent> EventPresented;
            public string ErrorText { get; set; }

            public void Advance(double seconds)
            {
                Sim.Advance(Sim.Now + (long)(seconds * OutpostIds.TicksPerSecond));
                Present();
            }

            public void Present()
            {
                m_Events.Clear();
                Sim.DrainEvents(m_Events);
                for (int i = 0; i < m_Events.Count; i++) EventPresented?.Invoke(m_Events[i]);
            }

            /// <summary>Показать подписчикам готовое событие (сундук шахты с заданным составом).</summary>
            public void PresentEvent(OutpostEvent e) => EventPresented?.Invoke(e);
        }

        static Button Find(Component root, string name, int index = 0)
        {
            int n = 0;
            foreach (var b in root.GetComponentsInChildren<Button>(true))
                if (b.gameObject.name == name && n++ == index) return b;
            Assert.Fail("нет кнопки " + name + "[" + index + "]");
            return null;
        }

        /// <summary>Кнопка <paramref name="name"/>[<paramref name="index"/>] внутри окна-корня <paramref name="window"/> (порядок слоёв меняется при показе).</summary>
        static Button FindIn(Component root, string window, string name, int index = 0)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == window) return Find(t, name, index);
            Assert.Fail("нет окна " + window);
            return null;
        }

        /// <summary>Неизменная часть шаблона словаря до первой подстановки: «Gold earned\n{0}» → «Gold earned».</summary>
        static string FixedPart(string key) => Loc.T(key).Split('{')[0].Trim();

        /// <summary>
        /// HUD проверяется без сцены игры: сцена CoreGame, которую оставили загруженной предыдущие тесты (CoreGameSmokeTests
        /// её не выгружают), выгружается — её камера и объекты иначе мешают подсказке ручного удара.
        /// </summary>
        [UnitySetUp]
        public IEnumerator UnloadLeftoverGameScene()
        {
            var leftover = SceneManager.GetSceneByName("CoreGame");
            if (!leftover.IsValid() || !leftover.isLoaded) yield break;
            SceneManager.SetActiveScene(SceneManager.CreateScene("OutpostHudTests-" + Guid.NewGuid().ToString("N")));
            yield return SceneManager.UnloadSceneAsync(leftover);
        }

        static IEnumerator WaitReal(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        [UnityTest]
        public IEnumerator HudWindowsCommandsAndTutorialWork()
        {
            Loc.ResetReports();
            var repo = BalanceRepository.LoadFile(Path.Combine(Application.dataPath, "Game/Data/Source/json-data.json"));
            var configs = OutpostConfigSet.LoadDefault();
            var state = StageProgressionService.NewGame(repo, configs, 7UL, 0L, 1);
            var map = OutpostLevelAdapter.ForRun(repo, state.Run);
            var game = new FakeOutpostGame { Configs = configs, Sim = new OutpostSimulation(state, map, configs) };

            var camGo = new GameObject("OutpostHudTestCamera", typeof(Camera));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 12f;
            camGo.transform.SetPositionAndRotation(new Vector3(map.Width * 0.5f, 30f, map.Depth * 0.5f - 6f), Quaternion.Euler(90f, 0f, 0f));

            var hud = OutpostHud.Create(game);
            var tutorial = new OutpostTutorial(game, hud);
            int opened = 0;
            hud.OnWindowOpened += () => opened++;
            float insetTop = -1f;
            hud.InsetsChanged += (top, bottom) => insetTop = top;
            try
            {
                yield return null;
                yield return null;
                hud.RefreshNow();
                Assert.IsFalse(hud.AnyWindowOpen);
                Assert.Greater(hud.TopInset, 0f);
                Assert.Greater(hud.BottomInset, 0f);
                Assert.IsNotNull(tutorial.Hint);
                Assert.AreEqual(Loc.T("outpost.tutorial.auto"), tutorial.CurrentHint, "шаг 1: автодобыча");
                state.Profile.Tutorial.Seen.Add(OutpostTutorial.StepAuto);
                yield return WaitReal(0.35f);
                Assert.AreEqual(Loc.T("outpost.tutorial.strike"), tutorial.CurrentHint, "отдельный шаг ручного удара");
                Assert.GreaterOrEqual(tutorial.StrikeTargetId, 0, "указатель на доступном камне");
                game.Advance(3);
                Assert.IsFalse(tutorial.IsDone(OutpostTutorial.StepStrike), "удары котиков не засчитывают шаг игрока");
                int targetId = tutorial.StrikeTargetId;
                long hp = game.Sim.TargetById(targetId).RemainingHpUnits;
                int tutorialSaves = game.TutorialSaves;
                Assert.IsTrue(game.Strike(targetId).Ok);
                Assert.Less(game.Sim.TargetById(targetId).RemainingHpUnits, hp);
                Assert.IsTrue(tutorial.IsDone(OutpostTutorial.StepStrike));
                Assert.Greater(game.TutorialSaves, tutorialSaves, "ручной удар сохраняет пройденный шаг");
                Assert.Contains(OutpostTutorial.StepStrike, OutpostJson.DeepCopy(state.Profile).Tutorial.Seen);

                for (int i = 0; i < 40; i++)
                {
                    game.Advance(0.5);
                    yield return null;
                }
                hud.RefreshNow();
                Assert.Greater(insetTop, 0f, "доли панелей переданы подписчику");
                hud.FlyCoins(game.DepotWorldPosition(0), 3);

                // «База»: режимы ×1/×10/«Макс», покупка через кнопку строки, Back закрывает.
                state.Run.Wallet += 1000000;
                hud.ShowBase();
                Assert.IsTrue(hud.BaseOpen);
                Assert.IsTrue(hud.AnyWindowOpen);
                Assert.Greater(opened, 0, "открытие окна вызывает OnWindowOpened");
                hud.RefreshNow();
                Find(hud, "mode1").onClick.Invoke();
                Assert.AreEqual(BuyMode.Ten, hud.BuyMode);
                Find(hud, "mode2").onClick.Invoke();
                Assert.AreEqual(BuyMode.Max, hud.BuyMode);
                Find(hud, "buy", (int)UpgradeBranch.Pickaxes).onClick.Invoke();
                Assert.Greater(state.Run.Levels[(int)UpgradeBranch.Pickaxes], 1, "«Макс» купил больше одного уровня");
                Find(hud, "buy", (int)UpgradeBranch.Team).onClick.Invoke();
                Assert.AreEqual(1, state.Run.Levels[(int)UpgradeBranch.Team], "«Команда» — только ×1");
                Find(hud, "mode0").onClick.Invoke();
                hud.RefreshNow();
                yield return null;
                Assert.IsTrue(hud.HandleBack());
                Assert.IsFalse(hud.BaseOpen);
                yield return null;

                // «Экипировка»: окно предмета, «Надеть», «Улучшить», «Надеть лучшее»; Back — сначала окно предмета.
                EquipmentService.Grant(state.Profile, "pickaxe.common", configs);
                EquipmentService.Grant(state.Profile, "backpack.rare", configs);
                state.Profile.Parts = 500;
                hud.ShowEquipment();
                yield return null;
                hud.RefreshNow();
                hud.ShowItem("pickaxe.common");
                Assert.IsTrue(hud.ItemDetailOpen);
                Find(hud, "equip").onClick.Invoke();
                Assert.AreEqual("pickaxe.common", state.Profile.Equipped[(int)EquipSlot.Pickaxe]);
                Find(hud, "upgrade").onClick.Invoke();
                Assert.AreEqual(2, state.Profile.ItemLevel("pickaxe.common"));
                yield return null;
                Assert.IsTrue(hud.HandleBack());
                Assert.IsFalse(hud.ItemDetailOpen);
                Assert.IsTrue(hud.EquipmentOpen);
                Find(hud, "best").onClick.Invoke();
                Assert.AreEqual("backpack.rare", state.Profile.Equipped[(int)EquipSlot.Backpack]);
                hud.RefreshNow();
                yield return null;
                hud.HandleBack();
                Assert.IsFalse(hud.EquipmentOpen);
                yield return null;

                // Сведения о котике и цели; Back снимает выбор.
                game.SelectWorker(state.Run.Workers[0].Id);
                hud.RefreshNow();
                Assert.IsTrue(hud.InfoVisible);
                game.SelectTarget(map.Targets[map.Targets.Length - 1].Id);
                hud.RefreshNow();
                Assert.IsTrue(hud.InfoVisible);
                Assert.IsTrue(hud.HandleBack());
                Assert.AreEqual(OutpostSelectionKind.None, game.Selection.Kind);
                yield return null;

                // Тап по постройке (Selection = Base) открывает «Базу».
                game.SelectBase();
                yield return null;
                Assert.IsTrue(hud.BaseOpen);
                hud.HideBase();

                hud.ShowSettings();
                hud.RefreshNow();
                Assert.IsTrue(hud.SettingsOpen);
                yield return null;
                hud.HandleBack();
                Assert.IsFalse(hud.SettingsOpen);

                // Завершение шахты: итоги открываются сами, обучение показывает награду (шаг 7).
                for (int i = 0; i < 400 && state.Run.Phase != RunPhase.ReadyToContinue; i++)
                {
                    state.Run.Wallet += 100000000;
                    for (int b = 0; b < OutpostIds.BranchCount; b++) game.Buy((UpgradeBranch)b, b == 0 ? BuyMode.One : BuyMode.Max);
                    game.Advance(10);
                    if (i % 20 == 0) yield return null;
                }
                Assert.AreEqual(RunPhase.ReadyToContinue, state.Run.Phase, "zone1 завершена");
                hud.RefreshNow();
                Assert.IsTrue(hud.ResultsOpen, "итоги шахты открылись сами");
                yield return WaitReal(0.4f);
                Assert.AreEqual(Loc.T("outpost.tutorial.reward"), tutorial.CurrentHint, "шаг 7: награда и «Экипировка»");
                hud.HideResults();
                hud.RefreshNow();
                Assert.IsFalse(hud.ResultsOpen, "итоги не открываются повторно сами");
                Find(hud, "StripAction").onClick.Invoke();
                Assert.AreEqual(1, game.Continues, "кнопка перехода в HUD зовёт контроллер");

                // Офлайн: шкала расчёта (Back не закрывает), затем «Пока вас не было» (Back = «Отлично»).
                game.Phase = OutpostControllerPhase.OfflineCatchUp;
                game.OfflineProgress01 = 0.5f;
                hud.RefreshNow();
                Assert.IsTrue(hud.OfflineCalcOpen);
                yield return null;
                Assert.IsTrue(hud.HandleBack());
                Assert.IsTrue(hud.OfflineCalcOpen);
                game.Phase = OutpostControllerPhase.Playing;
                game.PendingOffline = new OfflineSummary { ElapsedMs = 9 * 3600000L, AppliedMs = 3 * 3600000L, GoldDelivered = 1234, TargetsCleared = 3, SectorsOpened = 1, StageCompleted = true, CardsGained = 4, ElixirGained = 9 };
                hud.RefreshNow();
                Assert.IsFalse(hud.OfflineCalcOpen);
                Assert.IsTrue(hud.OfflineOpen);
                var offline = Array.Find(hud.GetComponentsInChildren<Transform>(true), t => t.name == "Offline");
                string offlineText = string.Join("\n", Array.ConvertAll(offline.GetComponentsInChildren<TMPro.TextMeshProUGUI>(), t => t.text));
                StringAssert.Contains(FixedPart("outpost.offline.gold"), offlineText);
                StringAssert.Contains(OutpostUiText.Money(game.PendingOffline.GoldDelivered), offlineText);
                StringAssert.DoesNotContain(FixedPart("outpost.offline.elapsed"), offlineText);
                StringAssert.DoesNotContain(FixedPart("outpost.offline.capped"), offlineText);
                StringAssert.DoesNotContain(FixedPart("outpost.offline.cleared"), offlineText);
                StringAssert.DoesNotContain(FixedPart("outpost.offline.sectors"), offlineText);
                StringAssert.DoesNotContain(FixedPart("outpost.offline.completed"), offlineText);
                StringAssert.DoesNotContain(FixedPart("cards.offline"), offlineText);
                var wallet = game.State.Run.Wallet;
                var pending = game.PendingOffline;
                var adButton = FindIn(hud, "Offline", "btn", 0);
                string adText = string.Join("\n", Array.ConvertAll(adButton.GetComponentsInChildren<TMPro.TextMeshProUGUI>(), t => t.text));
                StringAssert.Contains(Loc.T("outpost.offline.double"), adText);
                StringAssert.Contains(Loc.T("outpost.offline.ad"), adText);
                Assert.IsTrue(Array.Exists(adButton.GetComponentsInChildren<Image>(), i => i.sprite != null && i.sprite.name == "icon_ad_tv"),
                    "на кнопке рекламы — иконка телевизора");
                adButton.onClick.Invoke();
                Assert.AreEqual(wallet + pending.GoldDelivered, game.State.Run.Wallet, "тестовый режим: реклама сразу удваивает доход");
                Assert.IsNull(game.PendingOffline, "удвоение забирается один раз");
                Assert.IsFalse(hud.OfflineOpen);
                yield return null;

                // Back подтверждает сводку без удвоения.
                game.PendingOffline = new OfflineSummary { ElapsedMs = 3600000L, AppliedMs = 3600000L, GoldDelivered = 77 };
                hud.RefreshNow();
                Assert.IsTrue(hud.OfflineOpen);
                hud.HandleBack();
                Assert.IsNull(game.PendingOffline);
                Assert.IsFalse(hud.OfflineOpen);
                Assert.AreEqual(wallet + pending.GoldDelivered, game.State.Run.Wallet);

                // Нулевой доход также показывается суммой; правая кнопка подтверждает сводку.
                game.PendingOffline = new OfflineSummary { ElapsedMs = 5000, AppliedMs = 5000 };
                hud.RefreshNow();
                offlineText = string.Join("\n", Array.ConvertAll(offline.GetComponentsInChildren<TMPro.TextMeshProUGUI>(), t => t.text));
                StringAssert.Contains(Loc.F("outpost.offline.gold", "0"), System.Text.RegularExpressions.Regex.Replace(offlineText, "<[^>]+>", ""));
                FindIn(hud, "Offline", "btn", 1).onClick.Invoke();
                Assert.IsNull(game.PendingOffline);
                Assert.IsFalse(hud.OfflineOpen);

                // Ошибка записи: окно с «Повторить», после исправления закрывается.
                game.Phase = OutpostControllerPhase.Error;
                game.ErrorText = "save failed";
                game.SaveFailed = true;
                hud.RefreshNow();
                Assert.IsTrue(hud.ErrorOpen);
                FindIn(hud, "Error", "btn", 0).onClick.Invoke();
                Assert.AreEqual(1, game.Retries);
                game.Phase = OutpostControllerPhase.Playing;
                hud.RefreshNow();
                Assert.IsFalse(hud.ErrorOpen);

                // Модальное окно контроллера: Back нажимает кнопку отмены.
                bool cancelled = false;
                hud.Modal("title", "body", new OutpostHud.ModalButton { Label = "ok", Style = PixelButton.Green, Cancel = true, Action = () => cancelled = true });
                Assert.IsTrue(hud.ModalOpen);
                yield return null;
                hud.HandleBack();
                Assert.IsTrue(cancelled);
                Assert.IsFalse(hud.ModalOpen);
                Assert.IsFalse(hud.AnyWindowOpen);

                // Старый профиль уже прошёл первую шахту до добавления ручного удара: новый урок не возвращает его назад.
                state.Profile.Tutorial.Seen.Remove(OutpostTutorial.StepStrike);
                if (!state.Profile.Tutorial.Seen.Contains(OutpostTutorial.StepReset)) state.Profile.Tutorial.Seen.Add(OutpostTutorial.StepReset);
                var transition = StageProgressionService.PrepareTransition(state, repo, configs, 0);
                Assert.IsTrue(transition.Ok);
                game.Sim = new OutpostSimulation(transition.State, transition.Map, configs);
                hud.RefreshNow();
                yield return WaitReal(0.35f);
                Assert.IsTrue(tutorial.IsDone(OutpostTutorial.StepStrike), "пропущенный урок первой шахты завершён молча на шахте 2");
                Assert.AreNotEqual(Loc.T("outpost.tutorial.strike"), tutorial.CurrentHint);
                tutorial.Skip();
                Assert.IsTrue(game.State.Profile.Tutorial.Dismissed);
                Assert.Greater(game.TutorialSaves, 0);

                // Обновление без изменений состояния не выделяет память (кэш по значению).
                hud.RefreshNow();
                hud.RefreshNow();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 50; i++) hud.RefreshNow();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Debug.Log("[OutpostHudTests] 50 × RefreshNow без изменений: " + allocated + " байт");
                Assert.Less(allocated, 2048, "обновление HUD без изменений не выделяет память");

                CollectionAssert.IsEmpty(Loc.MissingKeys, "нет ключей: " + string.Join(", ", Loc.MissingKeys));
                CollectionAssert.IsEmpty(Loc.FormatErrors, string.Join("\n", Loc.FormatErrors));
            }
            finally
            {
                tutorial.Dispose();
                Object.Destroy(hud.gameObject);
                Object.Destroy(camGo);
            }
        }

        /// <summary>
        /// Окно награды сундука (ТЗ 10.5): сундук шахты с новой карточкой — окно с картинками, копиями и NEW; пока оно открыто,
        /// следующие сундуки шахты добавляются в него; без новой карточки — тост; окно сундука магазина сундук шахты не подменяет;
        /// итоги шахты ждут закрытия окна (сундук выхода приходит вместе с завершением).
        /// </summary>
        [UnityTest]
        public IEnumerator MineChestWindow_NewCardsMerge_ToastOtherwise_ResultsWait()
        {
            Loc.ResetReports();
            var repo = BalanceRepository.LoadFile(Path.Combine(Application.dataPath, "Game/Data/Source/json-data.json"));
            var configs = OutpostConfigSet.LoadDefault().WithCards(repo);
            var state = StageProgressionService.NewGame(repo, configs, 7UL, 0L, 1);
            var map = OutpostLevelAdapter.ForRun(repo, state.Run);
            var game = new FakeOutpostGame { Configs = configs, Sim = new OutpostSimulation(state, map, configs) };
            var camGo = new GameObject("OutpostHudChestCamera", typeof(Camera));
            camGo.tag = "MainCamera";
            var hud = OutpostHud.Create(game);
            OutpostEvent Chest(string chestId, string detail, int copies, int newCards, long elixir)
            {
                var e = OutpostEvent.Create(OutpostEventType.ChestOpened, game.Sim.Now);
                e.TargetId = 5;
                e.ItemId = chestId;
                e.Detail = detail;
                e.Count = copies;
                e.Extra = newCards;
                e.Value = elixir;
                return e;
            }
            string Texts(Component root) => string.Join("|", Array.ConvertAll(root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(), t => t.text));
            try
            {
                yield return null;
                yield return null;
                hud.RefreshNow();
                Assert.IsFalse(hud.AnyWindowOpen);

                game.PresentEvent(Chest("GachaWooden", "ca002:2", 2, 1, 15));
                Assert.IsTrue(hud.ChestRewardOpen, "сундук шахты с новой карточкой — окно");
                Assert.AreEqual(1, hud.ChestRewardChests);
                Assert.AreEqual(1, hud.ChestRewardTileCount);
                Assert.AreEqual("ca002", hud.ChestRewardTile(0).CardId);
                Assert.IsTrue(hud.ChestRewardTile(0).IsNew);
                StringAssert.Contains(Loc.F("outpost.chest.copies", 2), Texts(hud.ChestRewardTile(0).Root));
                StringAssert.Contains(Loc.T("catalog.gacha.GachaWooden"), Texts(Array.Find(hud.GetComponentsInChildren<Transform>(), t => t.name == "ChestReward")), "заголовок — имя сундука");

                // Пока окно открыто, следующий сундук шахты — в то же окно; копии одной карточки складываются.
                game.PresentEvent(Chest("GachaIron", "ca002:1,ca003:1", 2, 1, 5));
                Assert.AreEqual(2, hud.ChestRewardChests, "второй сундук — в то же окно");
                Assert.AreEqual(2, hud.ChestRewardTileCount);
                Assert.AreEqual("ca002", hud.ChestRewardTile(0).CardId, "новые первыми, затем больше копий");
                StringAssert.Contains(Loc.F("outpost.chest.copies", 3), Texts(hud.ChestRewardTile(0).Root));
                Assert.AreEqual("ca003", hud.ChestRewardTile(1).CardId);
                yield return null; // Back обрабатывается раз в кадр.
                Assert.IsTrue(hud.HandleBack());
                Assert.IsFalse(hud.ChestRewardOpen);

                game.PresentEvent(Chest("GachaWooden", "ca002:1", 1, 0, 3));
                Assert.IsFalse(hud.ChestRewardOpen, "сундук без новой карточки — тостом");

                Assert.IsTrue(game.StoreBuy("chest_wooden").Ok, "рекламный сундук магазина (тестовый режим)");
                Assert.IsTrue(hud.ChestRewardOpen);
                Assert.AreEqual(1, hud.ChestRewardTileCount, "шахта 1: Wooden — одна карточка");
                string storeCard = hud.ChestRewardTile(0).CardId;
                game.PresentEvent(Chest("GachaWooden", "ca004:1", 1, 1, 3));
                Assert.AreEqual(1, hud.ChestRewardChests, "окно магазина сундук шахты не подменяет");
                Assert.AreEqual(storeCard, hud.ChestRewardTile(0).CardId);
                yield return null; // Back обрабатывается раз в кадр.
                Assert.IsTrue(hud.HandleBack());

                game.PresentEvent(Chest("GachaCrusher", "ca005:1", 1, 1, 9));
                Assert.IsTrue(hud.ChestRewardOpen);
                state.Run.Phase = RunPhase.ReadyToContinue;
                hud.RefreshNow();
                Assert.IsFalse(hud.ResultsOpen, "итоги — после окна сундука");
                yield return null; // Back обрабатывается раз в кадр.
                Assert.IsTrue(hud.HandleBack());
                hud.RefreshNow();
                Assert.IsTrue(hud.ResultsOpen, "окно сундука закрыто — итоги открылись сами");

                CollectionAssert.IsEmpty(Loc.MissingKeys, "нет ключей: " + string.Join(", ", Loc.MissingKeys));
                CollectionAssert.IsEmpty(Loc.FormatErrors, string.Join("\n", Loc.FormatErrors));
            }
            finally
            {
                Object.Destroy(hud.gameObject);
                Object.Destroy(camGo);
            }
        }
    }
}
