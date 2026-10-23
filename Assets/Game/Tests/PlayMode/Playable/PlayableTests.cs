using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using CatMiners.Playable;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BigInteger = System.Numerics.BigInteger;

namespace CatMiners.Tests
{
    /// <summary>
    /// Playable-режим на настоящей сцене CoreGame (Docs/Playable/PLAYABLE_TZ.md §3, §6): <see cref="PlayableMode.Install"/> до
    /// загрузки — профиль «outpost-2.2-b5-playable» (3 котика), своё сохранение, только подсказки квестов, место под баннер,
    /// автопереход после итогов, догон офлайна без окна, конец отрезка, «Play again» без архивов; квесты (панель, удары, окно
    /// награды, золото BonusGold, следующий квест, лапка, пропуск недостижимых целей, перезагрузка), надписи «NEW CAT!» /
    /// «PICKAXES ×2!», баннер CTA под нижней панелью, ссылка магазина и финальный экран (Keep playing / Play again); правки
    /// ревью (урезанный магазин, отметка финального экрана, окно награды как окно, цель в кадр завершения шахты, тайм-скип по
    /// итогу, сундук магазина, награда не в завершённую шахту, «Reset progress», тост и реплика, стрелка к цели) —
    /// PlayableTests.Review.cs. TearDown снимает все крючки.
    /// </summary>
    public sealed partial class PlayableTests : PlayableFixture
    {
        /// <summary>Вариант p3 замера темпа (Tools/CoreTests/Drafts/PlayablePacingDraft.cs) — JSON merge-патч профиля b4.</summary>
        const string P3Patch =
            "{\"StartWorkers\":3,\"CoinBase\":\"10\",\"CostZoneStep\":\"0\",\"TapDamageFactor\":\"1/2\",\"Branches\":[{\"Growth\":\"3/2\"},{\"Milestones\":[5,10]}]}";

        [UnityTest]
        public IEnumerator Install_PlayableBalance_ThreeCats_OwnSave()
        {
            Assert.That(Director, Is.Not.Null, "режиссёр на контроллере после загрузки сцены");
            Assert.That(Game.Configs.Balance.Version, Is.EqualTo(PlayableBalance.Version));
            Assert.That(Game.State.Run.Balance.Version, Is.EqualTo(PlayableBalance.Version), "снимок профиля в шахте");
            Assert.That(Game.State.Run.Balance.StartWorkers, Is.EqualTo(3));
            Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(3));
            Assert.That(Game.SaveDirectory, Is.EqualTo(m_Dir));
            yield return Advance(5);
            // Обучения в игре нет: в HUD только своя подсказка квестов playable.
            Assert.That(Game.Hud.GetComponentsInChildren<TutorialHint>(true).Select(h => h.name).ToArray(), Is.EqualTo(new[] { "PlayableHint" }));
            var saved = new OutpostSaveService(m_Dir) { Configs = Game.Configs }.Load().State;
            Assert.That(saved, Is.Not.Null);
            Assert.That(saved.Run.Balance.Version, Is.EqualTo(PlayableBalance.Version));
        }

        [UnityTest]
        public IEnumerator Overlay_IsMeasuredP3Patch_CatalogUnchanged()
        {
            var b4 = OutpostResources.LoadConfigs().WithCards(Game.Balance);
            Assert.That(b4.Balance.Version, Is.EqualTo(OutpostIds.BalanceVersion));
            var doc = JObject.Parse(b4.Balance.CanonicalJson());
            doc.Merge(JObject.Parse(P3Patch), new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Merge });
            doc["Version"] = PlayableBalance.Version;
            var expected = OutpostBalanceConfig.Parse(doc.ToString());
            var actual = PlayableBalance.Overlay(b4.Balance);
            Assert.That(actual.CanonicalJson(), Is.EqualTo(expected.CanonicalJson()));
            Assert.That(b4.Balance.Version, Is.EqualTo(OutpostIds.BalanceVersion), "исходный профиль не меняется");
            Assert.That(Game.Configs.BalanceHash, Is.EqualTo(actual.ComputeHash()));
            // Экипировка и карточки — как в игре; награды шахт — прежняя таблица (первая сессия в playable выключена).
            Assert.That(Game.Configs.EquipmentHash, Is.EqualTo(b4.EquipmentHash), "экипировка — как в игре");
            Assert.That(Game.Configs.CardRulesHash, Is.EqualTo(b4.CardRulesHash), "карточки — как в игре");
            Assert.That(Game.Configs.RewardsHash, Is.EqualTo(PlayableBalance.Rewards(b4.Rewards).ComputeHash()), "награды шахт — прежние");
            Assert.That(Game.Configs.Progression, Is.Null, "без правил первой сессии");

            // Магазин урезан (ТЗ §3.4): сундук за рекламу и все платные тайм-скипы; гемов, эликсира и сундуков за гемы нет.
            var store = Game.Configs.Store;
            Assert.That(store.Items.Select(d => d.Id), Is.EqualTo(new[] { "chest_wooden", "skip_1h", "skip_4h", "skip_12h" }));
            Assert.That(store.Items.Any(d => d.Kind == StoreItemKind.Gems || d.Kind == StoreItemKind.Elixir), Is.False);
            Assert.That(store.Items.Where(d => d.Kind == StoreItemKind.Chest).All(d => d.Currency == StoreCurrency.Ad), Is.True);
            Assert.That(store.Sections, Is.EqualTo(new[] { "offers", "timeskip" }));
            Assert.That(store.StartGems, Is.Zero);
            Assert.That(store.Version, Is.EqualTo(b4.Store.Version + PlayableBalance.StoreSuffix));
            Assert.That(Game.State.Profile.Gems, Is.Zero);
            Assert.That(store.StarterConsumables, Is.Empty);
            foreach (var def in store.Items.Where(d => d.Kind == StoreItemKind.TimeSkip))
                Assert.That(Game.State.Profile.ConsumableCount(def.Id), Is.Zero, def.Id);
            Assert.That(b4.Store.Items.Any(d => d.Kind == StoreItemKind.Gems), Is.True, "каталог основной игры не меняется");
            Assert.That(PlayableBalance.Store(b4.Store).ComputeHash(), Is.EqualTo(Game.Configs.StoreHash));

            // В открытом магазине — только эти плитки, разделов гемов и эликсира нет.
            Game.Hud.ShowStore();
            yield return null;
            var content = Game.Hud.transform.Find("SafeArea/Store/viewport/content");
            Assert.That(content.Find("grid_gems"), Is.Null);
            Assert.That(content.Find("grid_elixir"), Is.Null);
            Assert.That(content.Find("grid_offers/chest_wooden"), Is.Not.Null);
            Assert.That(content.Find("grid_offers/chest_iron"), Is.Null);
            Assert.That(content.Find("grid_timeskip/skip_1h"), Is.Not.Null);
            // «+» эликсира и гемов скрыты; без запаса и гемов платные тайм-скипы скрыты.
            var currencies = Game.Hud.transform.Find("SafeArea/Store/currencies");
            Assert.That(Game.Hud.StoreHideUnpayable, Is.True);
            Assert.That(currencies.Find("elixir/plus").gameObject.activeSelf, Is.False, "«+» эликсира");
            Assert.That(currencies.Find("gems/plus").gameObject.activeSelf, Is.False, "«+» гемов");
            var profile = Game.State.Profile;
            Game.Hud.RefreshNow();
            yield return null;
            Assert.That(content.Find("grid_timeskip/skip_1h").gameObject.activeSelf, Is.False, "скип за гемы без гемов не показывается");
            Assert.That(content.Find("grid_timeskip/skip_4h").gameObject.activeSelf, Is.False);
            Assert.That(content.Find("grid_timeskip/skip_12h").gameObject.activeSelf, Is.False);
            Assert.That(content.Find("section_timeskip").gameObject.activeSelf, Is.False, "пустой раздел скрыт");
            Assert.That(content.Find("grid_timeskip").gameObject.activeSelf, Is.False);
            Assert.That(content.Find("grid_offers/chest_wooden").gameObject.activeSelf, Is.True);
            Assert.That(content.Find("section_offers").gameObject.activeSelf, Is.True);

            // Гемы появились (debug) — цена снова платёжеспособна, плитка видна.
            profile.Gems = 1000;
            Game.Hud.RefreshNow();
            yield return null;
            foreach (var def in store.Items.Where(d => d.Kind == StoreItemKind.TimeSkip))
                Assert.That(content.Find("grid_timeskip/" + def.Id).gameObject.activeSelf, Is.True, def.Id);
            Assert.That(content.Find("section_timeskip").gameObject.activeSelf, Is.True);
            profile.Gems = 0;
            Game.Hud.HideStore();
        }

        [UnityTest]
        public IEnumerator BottomReserve_LiftsHudAboveBanner_InsetsFromWholeSafeArea()
        {
            yield return Advance(0.5);
            var safe = (RectTransform)Game.Hud.transform.Find("SafeArea");
            Assert.That(safe.offsetMin.y, Is.EqualTo(PlayableMode.BannerHeight).Within(0.01f));
            float full = safe.rect.height + safe.offsetMin.y;
            var bottom = (RectTransform)safe.Find("Bottom");
            float plain = Mathf.Clamp(bottom.rect.height / full, 0f, 0.45f);
            Assert.That(Game.Hud.BottomInset, Is.EqualTo(Mathf.Clamp((bottom.rect.height + PlayableMode.BannerHeight) / full, 0f, 0.45f)).Within(1e-4f));
            Assert.That(Game.Hud.BottomInset, Is.GreaterThan(plain), "нижняя доля включает баннер");

            // Нижняя панель стоит над баннером, полоса поля для камеры и ввода начинается над панелью.
            var corners = new Vector3[4];
            bottom.GetWorldCorners(corners);
            float scale = Game.Hud.GetComponent<Canvas>().scaleFactor;
            var area = Screen.safeArea;
            Assert.That(corners[0].y, Is.EqualTo(area.yMin + PlayableMode.BannerHeight * scale).Within(1f));
            Game.Hud.FieldBandPixels(out float bandBottom, out _);
            Assert.That(bandBottom, Is.GreaterThan(area.yMin + PlayableMode.BannerHeight * scale));
            // Без предела доли 0,45 (низкий экран редактора) полоса поля начинается ровно у верха нижней части HUD.
            if ((bottom.rect.height + PlayableMode.BannerHeight) / full < 0.45f)
                Assert.That(bandBottom, Is.EqualTo(corners[1].y).Within(1f));
        }

        /// <summary>
        /// Окно сундука поверх завершённой шахты при закрытом магазине (как сундук выхода — у отладочного «пройти шахту» его
        /// нет) закрывается само; итоги, затем следующая шахта. Сундук из открытого магазина — PlayableTests.Review.cs.
        /// </summary>
        [UnityTest]
        public IEnumerator CompletedMine_ChestWindowClosesItself_ResultsThenNextMine()
        {
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(3));
            Game.CompleteStageNow();
            yield return WaitPlaying();
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
            Assert.That(Game.CanContinue, Is.True);

            // Окно сундука поверх завершённой шахты (магазин закрыт) закрывается само, как сундук выхода.
            Assert.That(Game.Hud.StoreOpen, Is.False);
            Assert.That(Game.StoreBuy("chest_wooden").Ok, Is.True);
            yield return null;
            yield return null;
            Assert.That(Game.Hud.ChestRewardOpen, Is.True);
            double chestAt = m_Clock.MonotonicSeconds;
            for (int i = 0; i < 40 && Game.Hud.ChestRewardOpen; i++) { m_Clock.Advance(0.25); yield return null; }
            Assert.That(Game.Hud.ChestRewardOpen, Is.False);
            Assert.That(m_Clock.MonotonicSeconds - chestAt, Is.InRange(PlayableDirector.ExitChestSeconds - 0.01, PlayableDirector.ExitChestSeconds + 0.6));
            Assert.That(Game.State.Run.Zone, Is.EqualTo(1));

            // Итоги видны ~2,5 с, затем переход сам.
            double readyAt = m_Clock.MonotonicSeconds;
            bool resultsSeen = false;
            for (int i = 0; i < 40 && !Game.InTransition && Game.State.Run.Zone == 1; i++)
            {
                resultsSeen |= Game.Hud.ResultsOpen;
                m_Clock.Advance(0.25);
                yield return null;
            }
            Assert.That(resultsSeen, Is.True, "итоги шахты показаны до перехода");
            Assert.That(Game.InTransition || Game.State.Run.Zone == 2, Is.True, "автопереход");
            Assert.That(m_Clock.MonotonicSeconds - readyAt, Is.InRange(PlayableDirector.ResultsSeconds - 0.01, PlayableDirector.ResultsSeconds + 0.8));
            yield return WaitPlaying();
            Assert.That(Game.State.Run.Zone, Is.EqualTo(2));
            Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(3));
            Assert.That(Game.State.Run.Balance.Version, Is.EqualTo(PlayableBalance.Version));
            Assert.That(Game.PendingOffline, Is.Null);
            Assert.That(Game.Hud.ResultsOpen, Is.False);
        }

        [UnityTest]
        public IEnumerator Absence_CatchesUpSilently_NoWhileAwayWindow()
        {
            long before = Game.State.Run.LastProcessedUtcMs;
            Game.SendMessage("OnApplicationFocus", false);
            m_Clock.Advance(60);
            Game.SendMessage("OnApplicationFocus", true);
            yield return WaitPlaying();
            yield return Advance(0.5);
            Assert.That(Game.State.Run.LastProcessedUtcMs - before, Is.GreaterThanOrEqualTo(59_000), "офлайн догнан");
            Assert.That(Game.PendingOffline, Is.Null);
            Assert.That(Game.Hud.OfflineOpen, Is.False);
        }

        [UnityTest]
        public IEnumerator Segment_CompletesAfterMineTenOrThirtyMinutes_Once()
        {
            var director = Director;
            int raised = 0;
            director.SegmentCompleted += () => raised++;
            yield return Advance(0.5);
            Assert.That(director.SegmentComplete, Is.False);
            Game.State.Profile.CompletedZones.Add(PlayableDirector.SegmentLastMine);
            yield return null;
            Assert.That(director.SegmentComplete, Is.True, "пройдена шахта 10");
            yield return null;
            Assert.That(raised, Is.EqualTo(1));

            Game.State.Profile.CompletedZones.Clear();
            UnityEngine.Object.Destroy(director);
            yield return null;
            var second = Game.gameObject.AddComponent<PlayableDirector>();
            Game.State.Profile.PlayTimeMs = PlayableDirector.SegmentPlayTimeMs - 5_000;
            yield return Advance(0.5);
            Assert.That(second.SegmentComplete, Is.False);
            Game.State.Profile.PlayTimeMs = PlayableDirector.SegmentPlayTimeMs;
            yield return null;
            Assert.That(second.SegmentComplete, Is.True, "30 минут игры");
        }

        [UnityTest]
        public IEnumerator Restart_WipesPlayableSaves_FreshGame_NoArchives()
        {
            const long marker = 123_457;
            Game.State.Profile.Elixir = marker;
            Game.State.Profile.PlayTimeMs = 20 * 60_000L;
            yield return Advance(2.5, 0.5);
            var saves = new OutpostSaveService(m_Dir) { Configs = Game.Configs };
            Assert.That(saves.Load().State.Profile.Elixir, Is.EqualTo(marker), "автосохранение записало прогресс");

            var old = Game;
            Director.Restart();
            for (int frame = 0; frame < 600 && (Game == null || ReferenceEquals(Game, old) || Game.Phase != OutpostControllerPhase.Playing); frame++)
                yield return null;
            Assert.That(Game, Is.Not.Null);
            Assert.That(ReferenceEquals(Game, old), Is.False, "сцена загружена заново");
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
            Assert.That(Director, Is.Not.Null);
            Assert.That(Game.State.Profile.Elixir, Is.Not.EqualTo(marker));
            Assert.That(Game.State.Profile.PlayTimeMs, Is.LessThan(60_000));
            Assert.That(Game.State.Run.Zone, Is.EqualTo(1));
            Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(3));
            Assert.That(saves.Load().State.Profile.Elixir, Is.Not.EqualTo(marker));
            Assert.That(Directory.GetDirectories(m_Dir, "restart-*", SearchOption.AllDirectories), Is.Empty, "без архивов ResetProgress");
        }

        [UnityTest]
        public IEnumerator InstallUninstall_HooksAndDefaultSaveDirectory()
        {
            PlayableMode.Uninstall();
            Assert.That(PlayableMode.Installed, Is.False);
            Assert.That(OutpostController.ConfigsOverride, Is.Null);
            Assert.That(OutpostController.SuppressOfflineSummary, Is.False);
            Assert.That(OutpostHud.BottomReserve, Is.Zero);
            Assert.That(OutpostController.SaveDirectoryOverride, Is.EqualTo(m_Dir), "каталог теста не снимается");

            OutpostController.SaveDirectoryOverride = null;
            PlayableMode.Install();
            PlayableMode.Install();
            Assert.That(PlayableMode.Installed, Is.True);
            Assert.That(OutpostController.SaveDirectoryOverride, Is.EqualTo(Path.Combine(Application.persistentDataPath, PlayableMode.SaveFolder)));
            Assert.That(OutpostController.ConfigsOverride, Is.Not.Null);
            Assert.That(OutpostController.SuppressOfflineSummary, Is.True);
            Assert.That(OutpostHud.BottomReserve, Is.EqualTo(PlayableMode.BannerHeight));
            PlayableMode.Uninstall();
            Assert.That(OutpostController.SaveDirectoryOverride, Is.Null, "свой каталог режим снимает сам");
            Assert.That(OutpostHud.BottomReserve, Is.Zero);

            OutpostController.SaveDirectoryOverride = m_Dir;
            PlayableMode.Install();
            yield return null;
        }

        // ───────────── Квесты (ТЗ playable 3.3) ─────────────

        IEnumerator CompleteStrikeQuest()
        {
            var quests = Quests;
            Assert.That(quests.Current.Kind, Is.EqualTo(QuestKind.Strike));
            for (int i = 0; i < 16 && !quests.CurrentDone; i++) yield return StrikeOnce();
            Assert.That(quests.CurrentDone, Is.True, "пять ударов — квест выполнен");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Quests_FirstQuestInTracker_StrikesAdvanceIt()
        {
            var quests = Quests;
            Assert.That(quests, Is.Not.Null, "квесты — вместе с режиссёром");
            Assert.That(Juice, Is.Not.Null);
            yield return Advance(PlayableQuests.FirstQuestSeconds + 0.25);
            Assert.That(quests.Zone, Is.EqualTo(1));
            Assert.That(quests.Index, Is.Zero);
            Assert.That(quests.Current.Kind, Is.EqualTo(QuestKind.Strike));
            var panel = quests.Panel;
            Assert.That(panel.Visible, Is.True, "панель квеста въехала");
            Assert.That(panel.Title, Is.EqualTo(Loc.T("playable.quest.strike.title")));
            Assert.That(panel.Step, Is.EqualTo(Loc.T("playable.quest.strike.step")));
            Assert.That(panel.Counter, Is.EqualTo("0/5"));
            yield return WaitFrames(() => false, 15, 0);

            // Под верхней полосой HUD и выше тоста.
            var safe = (RectTransform)Game.Hud.transform.Find("SafeArea");
            var corners = new Vector3[4];
            panel.Root.GetWorldCorners(corners);
            float panelTop = corners[1].y, panelBottom = corners[0].y, panelLeft = corners[0].x;
            ((RectTransform)safe.Find("TopBar")).GetWorldCorners(corners);
            Assert.That(panelTop, Is.LessThanOrEqualTo(corners[0].y + 0.5f), "ниже верхней полосы");
            var toast = (RectTransform)safe.Find("ToastBg");
            var toastTop = toast.TransformPoint(new Vector3(0f, toast.rect.yMax, 0f)).y;
            Assert.That(panelBottom, Is.GreaterThanOrEqualTo(toastTop - 0.5f), "выше тоста");
            safe.GetWorldCorners(corners);
            Assert.That(panelLeft, Is.GreaterThanOrEqualTo(corners[0].x), "въехала целиком");
            float safeWidth = corners[2].x - corners[0].x;

            // Ширина по содержимому, не меньше половины; счётчик FontLarge сразу за шагом.
            panel.Root.GetWorldCorners(corners);
            Assert.That(corners[2].x - corners[0].x, Is.GreaterThanOrEqualTo(safeWidth * PlayableQuestPanel.MinWidthFraction - 1f));
            Assert.That(panel.CounterText.fontSize, Is.EqualTo(PixelUi.FontLarge));
            float stepRight = panel.StepText.rectTransform.offsetMax.x, counterLeft = panel.CounterText.rectTransform.offsetMin.x;
            Assert.That(counterLeft - stepRight, Is.InRange(0f, 4 * PixelUi.Px + 0.01f), "счётчик сразу за шагом");
            Assert.That(panel.StepText.isTextTruncated, Is.False);

            // Реплика наставника своей подсказкой квестов и лапка на камне с рудой под удар.
            if (!quests.Guide.ToastVisible)
            {
                Assert.That(quests.Guide.LineVisible, Is.True);
                Assert.That(quests.Guide.Hint.Text, Is.EqualTo(Loc.T("playable.quest.strike.line")));
            }
            yield return WaitFrames(() => quests.Guide.PawVisible);
            Assert.That(quests.Guide.PawTarget, Is.GreaterThanOrEqualTo(0), "лапка на камне");
            Assert.That(Game.CanStrike(quests.Guide.PawTarget), Is.True);
            Assert.That(Game.Map.Target(quests.Guide.PawTarget).Kind, Is.EqualTo('r'), "камень с рудой, не препятствие");

            int struck = 0;
            Action<OutpostEvent> count = e => { if (e.Type == OutpostEventType.TargetStruck && e.Value.Sign > 0) struck++; };
            Game.EventPresented += count;
            try
            {
                yield return StrikeOnce();
                yield return StrikeOnce();
            }
            finally { Game.EventPresented -= count; }
            Assert.That(struck, Is.EqualTo(2));
            Assert.That(quests.Progress, Is.EqualTo((BigInteger)2));
            Assert.That(panel.Counter, Is.EqualTo("2/5"));
            Assert.That(quests.CurrentDone, Is.False);
            yield return WaitFrames(() => quests.Guide.PawTarget < 0, 30);
            Assert.That(quests.Guide.PawTarget, Is.EqualTo(-1), "лапка на камне — только до первого удара");
        }

        [UnityTest]
        public IEnumerator Quests_CompletePopup_ClaimGrantsGold_NextQuestGuidesToBase()
        {
            var quests = Quests;
            yield return Advance(PlayableQuests.FirstQuestSeconds + 0.25);
            yield return CompleteStrikeQuest();
            Assert.That(quests.Panel.Checked, Is.True);
            Assert.That(quests.Panel.Counter, Is.EqualTo("5/5"));
            Assert.That(quests.PopupOpen, Is.True, "«QUEST COMPLETE!» на свободном поле");
            Assert.That(quests.Popup.Window.Title.text, Is.EqualTo(Loc.T("playable.quest.complete")));
            Assert.That(quests.ClaimButton.Label.text, Is.EqualTo(Loc.T("playable.quest.claim")));
            Assert.That(quests.ClaimButton.Style, Is.EqualTo(PixelButton.Gold), "сбор — золотая кнопка, зелёная — магазин");
            Assert.That(quests.Popup.Window.BodyText.text, Is.EqualTo(Loc.F("playable.quest.done", Loc.T("playable.quest.strike.step"), "5/5")));
            Assert.That(quests.ClaimReady, Is.False, "«CLAIM» — не сразу");
            Assert.That(quests.Guide.Hint.Visible, Is.False, "подсказка под окном награды скрыта");
            var expected = PlayableQuestRules.Nice((PlayableQuestRules.NextCost(Game, UpgradeBranch.Pickaxes) * 150 + 99) / 100);
            Assert.That(quests.Reward, Is.EqualTo(expected), "полторы цены уровня «Кирок» — цели следующего квеста");
            Assert.That(quests.RewardText.text, Is.EqualTo(OutpostUiText.Money(expected)), "награда крупно, без «+» (в шрифте — точка)");
            Assert.That(quests.RewardText.fontSize, Is.EqualTo(PixelUi.FontTitle));
            Assert.That(Director.AutoContinue, Is.True);

            var run = Game.State.Run;
            var wallet = run.Wallet;
            quests.Claim();
            Assert.That(quests.PopupOpen, Is.True, "до 0,9 с нажатие не действует");
            Assert.That(run.Wallet, Is.EqualTo(wallet));
            yield return Advance(PlayableQuests.ClaimDelaySeconds);
            Assert.That(quests.ClaimReady, Is.True);

            run = Game.State.Run;
            wallet = run.Wallet;
            var bonus = run.BonusCredited;
            var reward = quests.Reward;
            quests.ClaimButton.Button.onClick.Invoke();
            Assert.That(quests.PopupOpen, Is.False);
            Assert.That(Game.State.Run.Wallet - wallet, Is.EqualTo(reward), "золото награды — в кошелёк шахты");
            Assert.That(Game.State.Run.BonusCredited - bonus, Is.EqualTo(reward));
            Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment, Game.Configs.Cards), Is.Empty);
            var saved = new OutpostSaveService(m_Dir) { Configs = Game.Configs }.Load().State;
            Assert.That(saved.Run.BonusCredited, Is.EqualTo(Game.State.Run.BonusCredited), "награда записана сразу");

            // Следующий квест — через 0,9 с; тоста «Income doubled» нет.
            Assert.That(quests.Current.Text, Is.EqualTo("pickaxes"));
            Assert.That(quests.Revealed, Is.False);
            yield return Advance(PlayableQuests.NextQuestSeconds + 0.25);
            Assert.That(quests.Revealed, Is.True);
            Assert.That(quests.Panel.Title, Is.EqualTo(Loc.T("playable.quest.pickaxes.title")));
            Assert.That(quests.Panel.Counter, Is.EqualTo("0/2"));
            Assert.That(quests.Panel.Checked, Is.False);
            var toast = Game.Hud.transform.Find("SafeArea/ToastBg");
            if (toast.gameObject.activeSelf)
                Assert.That(toast.GetComponentInChildren<TMP_Text>().text, Is.Not.EqualTo(Loc.F("outpost.store.toast.double", OutpostUiText.Money(reward))));

            // Покупка по карману: лапка на «Базе», в открытой панели — на строке «Кирок»; касание у правого нижнего угла кнопки.
            yield return WaitFrames(() => quests.Guide.PawUi != null);
            var baseButton = Game.Hud.FindTarget(OutpostHud.TargetBase);
            Assert.That(quests.Guide.PawUi, Is.SameAs(baseButton));
            // Кнопка нижней панели: лапка сверху пальцем вниз — над полем, соседние кнопки не закрыты.
            Assert.That(quests.Guide.Hint.TipAnchor, Is.EqualTo(PlayableGuide.BarTip));
            Assert.That(quests.Guide.Hint.PointDown, Is.True);
            var hand = (RectTransform)quests.Guide.Hint.Rect.Find("hand");
            yield return null;
            Assert.That(hand.localScale.y, Is.LessThan(0f), "лапка отражена: палец вниз");
            Assert.That(ScreenRect(hand).center.y, Is.GreaterThan(ScreenRect(baseButton).yMax), "лапка над кнопкой");
            Game.Hud.ShowBase();
            var row = Game.Hud.FindTarget(OutpostHud.BranchTarget(UpgradeBranch.Pickaxes));
            yield return WaitFrames(() => quests.Guide.PawUi == row);
            Assert.That(quests.Guide.PawUi, Is.SameAs(row), "лапка внутри окна «База»");
            Assert.That(quests.Guide.Hint.TipAnchor, Is.EqualTo(PlayableGuide.ButtonTip), "лапка у угла: подпись и цена видны");
            Assert.That(quests.Guide.Hint.PointDown, Is.False, "в окне — обычная лапка под пальцем");

            // Два уровня — квест выполнен; окно награды ждёт, пока «База» открыта.
            for (int i = 0; i < 2; i++)
            {
                GrantGold(PlayableQuestRules.NextCost(Game, UpgradeBranch.Pickaxes));
                Assert.That(Game.Buy(UpgradeBranch.Pickaxes, BuyMode.One).Ok, Is.True);
            }
            yield return Advance(0.25);
            Assert.That(quests.CurrentDone, Is.True);
            Assert.That(quests.Panel.Counter, Is.EqualTo("2/2"));
            Assert.That(quests.PopupOpen, Is.False, "окно награды — после закрытия «Базы»");
            Game.Hud.HideBase();
            yield return null;
            yield return null;
            Assert.That(quests.PopupOpen, Is.True);
        }

        [UnityTest]
        public IEnumerator Quests_SkipUnreachableGoals()
        {
            yield return Advance(PlayableQuests.FirstQuestSeconds + 0.25);
            // Шахта 1: ворот нет — квест ворот недостижим; первый квест цепочки — достижим.
            var gate = PlayableQuestBook.All().First(q => q.Kind == QuestKind.Gate);
            Assert.That(PlayableQuestRules.IsAvailable(gate, Game), Is.False);
            Assert.That(PlayableQuestRules.IsAvailable(PlayableQuestBook.ForMine(1)[0], Game), Is.True);
            Assert.That(PlayableQuestRules.IsAvailable(PlayableQuestBook.All().First(q => q.Kind == QuestKind.Equip), Game), Is.False, "надевать нечего");
            foreach (var zone in Enumerable.Range(1, PlayableQuestBook.MineCount + 1))
                Assert.That(PlayableQuestBook.ForMine(zone).Last().Kind, Is.EqualTo(QuestKind.Clear), "шахта " + zone + " заканчивается «Clear Mine»");

            // Шахта 5 в debug-профиле без предметов: «Full Kit» пропущен, текущая цель достижима.
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Game.LoadZone(5);
            yield return WaitPlaying();
            yield return Advance(1.5);
            var quests = Quests;
            var chain = PlayableQuestBook.ForMine(5);
            Assert.That(quests.Zone, Is.EqualTo(5));
            Assert.That(chain[0].Kind, Is.EqualTo(QuestKind.Equip));
            Assert.That(quests.Index, Is.GreaterThan(0), "«Full Kit» без предметов пропущен");
            for (int i = 0; i < quests.Index; i++) Assert.That(PlayableQuestRules.IsAvailable(chain[i], Game), Is.False, chain[i].Text);
            Assert.That(quests.Current.Kind == QuestKind.Clear || PlayableQuestRules.IsAvailable(quests.Current, Game), Is.True);
            Assert.That(quests.Panel.Visible, Is.True);
        }

        [UnityTest]
        public IEnumerator Quests_ChainPositionSurvivesReload()
        {
            var quests = Quests;
            yield return Advance(PlayableQuests.FirstQuestSeconds + 0.25);
            yield return CompleteStrikeQuest();
            yield return Advance(PlayableQuests.ClaimDelaySeconds);
            quests.Claim();
            Assert.That(quests.PopupOpen, Is.False);
            Assert.That(quests.Current.Text, Is.EqualTo("pickaxes"));
            var old = Game;
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            Assert.That(ReferenceEquals(Game, old), Is.False, "сцена загружена заново");
            yield return WaitPlaying();
            yield return Advance(PlayableQuests.FirstQuestSeconds + 0.25);
            Assert.That(Quests, Is.Not.Null);
            Assert.That(Quests.Zone, Is.EqualTo(1));
            Assert.That(Quests.Index, Is.EqualTo(1));
            Assert.That(Quests.Current.Text, Is.EqualTo("pickaxes"));
            Assert.That(Quests.CurrentDone, Is.False);
            Assert.That(Quests.Panel.Visible, Is.True);
            Assert.That(Quests.Panel.Title, Is.EqualTo(Loc.T("playable.quest.pickaxes.title")));
        }

        [UnityTest]
        public IEnumerator Quests_EveryChainTextIsRegistered()
        {
            foreach (var q in PlayableQuestBook.All())
            {
                Assert.That(Loc.Has(q.TitleKey), Is.True, q.TitleKey);
                Assert.That(Loc.Has(q.StepKey), Is.True, q.StepKey);
                Assert.That(Loc.Has(q.LineKey), Is.True, q.LineKey);
                Assert.That(PixelArt.Has(q.Icon), Is.True, q.Icon);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Juice_NewCatOnHire_PowerOnPickaxesMilestone()
        {
            var juice = Juice;
            yield return Advance(0.5);
            Assert.That(juice.Visible, Is.False);
            GrantGold(PlayableQuestRules.NextCost(Game, UpgradeBranch.Team));
            Assert.That(Game.Buy(UpgradeBranch.Team, BuyMode.One).Ok, Is.True);
            for (int i = 0; i < 20 && !juice.Visible; i++) yield return Advance(0.02, 0.02);
            Assert.That(juice.Text, Is.EqualTo(Loc.T("playable.juice.new_cat")), "котик вышел из лагеря");
            yield return Advance(PlayableJuice.ShowSeconds + 0.25);
            Assert.That(juice.Visible, Is.False);

            var balance = Game.State.Run.Balance;
            int milestone = balance.Branch(UpgradeBranch.Pickaxes).Milestones[0];
            while (Game.State.Run.Levels[(int)UpgradeBranch.Pickaxes] < milestone)
            {
                GrantGold(PlayableQuestRules.NextCost(Game, UpgradeBranch.Pickaxes));
                Assert.That(Game.Buy(UpgradeBranch.Pickaxes, BuyMode.One).Ok, Is.True);
            }
            for (int i = 0; i < 20 && !juice.Visible; i++) yield return Advance(0.02, 0.02);
            Assert.That(juice.Text, Is.EqualTo(Loc.F("playable.juice.power", OutpostUiText.Num(balance.MilestoneMultiplier))));
            Assert.That(juice.Text, Is.EqualTo("PICKAXES ×2!"));
            Assert.That(juice.Root.Find("panel"), Is.Null, "без таблички тоста");
            var glow = (RectTransform)juice.Root.Find("glow");
            Assert.That(glow.GetComponent<UnityEngine.UI.Image>().sprite, Is.SameAs(PixelArt.Ui("fx_glow_warm")));
            // Свечение ×2 по центру иконки (не под текстом); тёмная полоса — первой, позади, шире текста.
            yield return null;
            Rect glowRect = ScreenRect(glow), iconRect = ScreenRect((RectTransform)juice.Root.Find("icon")), textRect = ScreenRect((RectTransform)juice.Root.Find("text"));
            float k = juice.Root.lossyScale.x;
            Assert.That(glowRect.width, Is.EqualTo(PixelUi.IconSize("fx_glow_warm", 2).x * k).Within(1f));
            Assert.That(Vector2.Distance(glowRect.center, iconRect.center), Is.LessThan(PixelUi.Px * k + 0.5f), "свечение по центру иконки");
            var back = (RectTransform)juice.Root.Find("back");
            Assert.That(back.GetSiblingIndex(), Is.Zero, "полоса позади всего");
            Rect backRect = ScreenRect(back);
            var textSize = juice.Root.Find("text").GetComponent<TMP_Text>().GetPreferredValues(juice.Text);
            Assert.That(backRect.xMin, Is.LessThanOrEqualTo(textRect.xMin - 2 * PixelUi.Px * k));
            Assert.That(backRect.xMax, Is.GreaterThanOrEqualTo(textRect.xMin + textSize.x * k + 2 * PixelUi.Px * k));
            Assert.That(backRect.height, Is.GreaterThanOrEqualTo(textSize.y * k));
            Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment, Game.Configs.Cards), Is.Empty);
        }
    

        // ───────────── Баннер, финальный экран, магазин (ТЗ playable 3.5) ─────────────

        static Rect ScreenRect(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        /// <summary>Верхний элемент UI под точкой экрана (все Canvas сцены, как у EventSystem).</summary>
        static GameObject TopUiAt(Vector2 screen)
        {
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, results);
            return results.Count > 0 ? results[0].gameObject : null;
        }

        static void Tap(GameObject target)
        {
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler);
        }

        string StatsPath => Path.Combine(m_Dir, OutpostSaveService.RootFolder, "playable-stats-main.json");

        [UnityTest]
        public IEnumerator StoreUrl_FromPageQuery_OnlyWebLinks()
        {
            Assert.That(PlayableStore.UrlFromPage(""), Is.EqualTo(PlayableStore.DefaultUrl));
            Assert.That(PlayableStore.UrlFromPage("https://tigblack.github.io/Cat-Miners/playable/"), Is.EqualTo(PlayableStore.DefaultUrl));
            Assert.That(PlayableStore.UrlFromPage("https://x.io/p/?store=https%3A%2F%2Fplay.google.com%2Fstore%2Fapps%2Fdetails%3Fid%3Dcom.cat%26hl%3Den"),
                Is.EqualTo("https://play.google.com/store/apps/details?id=com.cat&hl=en"));
            Assert.That(PlayableStore.UrlFromPage("https://x.io/p/?net=fb&store=https://apps.apple.com/app/id1#top"), Is.EqualTo("https://apps.apple.com/app/id1"));
            Assert.That(PlayableStore.UrlFromPage("https://x.io/p/?store="), Is.EqualTo(PlayableStore.DefaultUrl));
            Assert.That(PlayableStore.UrlFromPage("https://x.io/p/?store=javascript%3Aalert(1)"), Is.EqualTo(PlayableStore.DefaultUrl), "только http/https");
            Assert.That(PlayableStore.UrlFromPage("https://x.io/p/?store=data:text/html,hi"), Is.EqualTo(PlayableStore.DefaultUrl));
            Assert.That(PlayableStore.UrlFromPage("https://x.io/p/?restore=https://a.b/"), Is.EqualTo(PlayableStore.DefaultUrl));
            Assert.That(PlayableStore.Url, Is.EqualTo(PlayableStore.UrlFromPage(Application.absoluteURL)));
            Assert.That(PlayableStore.Network, Is.EqualTo("editor"));
            yield return null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// JS-плагин магазина — только веб-сборка, не редактор; define constraints у .jslib не работают, поэтому вне сборки
        /// playable его исключает делегат BuildWeb (ShouldIncludeInBuild = false).
        /// </summary>
        [UnityTest]
        public IEnumerator StorePlugin_WebGlOnly_ExcludedOutsidePlayableBuild()
        {
            const string path = "Assets/Game/Scripts/Playable/Plugins/CatMinersPlayable.jslib";
            var plugin = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.PluginImporter;
            Assert.That(plugin, Is.Not.Null, "jslib импортирован как плагин");
            Assert.That(plugin.GetCompatibleWithPlatform(UnityEditor.BuildTarget.WebGL), Is.True);
            Assert.That(plugin.GetCompatibleWithEditor(), Is.False);
            Assert.That(plugin.GetCompatibleWithAnyPlatform(), Is.False);
            yield return null;
            Assert.That(plugin.ShouldIncludeInBuild(), Is.False, "основная сборка плагин не получает");
        }
#endif

        [UnityTest]
        public IEnumerator Banner_SlidesInBelowBottomBar_TapOpensStore()
        {
            var cta = Cta;
            Assert.That(cta, Is.Not.Null, "баннер — вместе с режиссёром");
            yield return Advance(0.5);
            var banner = cta.Banner;
            Assert.That(banner, Is.Not.Null);
            Assert.That(banner.Visible, Is.False, "баннер въезжает через 0,8 с");
            yield return Advance(PlayableCta.BannerDelaySeconds);
            Assert.That(banner.Visible, Is.True);
            yield return WaitFrames(() => banner.Settled, 60, 0);
            Assert.That(banner.Settled, Is.True);
            yield return null;

            // Свой Canvas поверх HUD с теми же настройками масштаба.
            var hudCanvas = Game.Hud.GetComponent<Canvas>();
            Assert.That(cta.Canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(cta.Canvas.sortingOrder, Is.GreaterThan(hudCanvas.sortingOrder));
            Assert.That(cta.Canvas.scaleFactor, Is.EqualTo(hudCanvas.scaleFactor).Within(1e-4f));

            // Под нижней панелью HUD, в резерве, внутри safe area.
            var safe = (RectTransform)Game.Hud.transform.Find("SafeArea");
            var bar = (RectTransform)safe.Find("Bottom/BottomBar");
            Assert.That(bar, Is.Not.Null);
            Rect b = ScreenRect(banner.Panel), hudBar = ScreenRect(bar), area = Screen.safeArea;
            float scale = hudCanvas.scaleFactor;
            Assert.That(b.yMax, Is.LessThanOrEqualTo(hudBar.yMin + 0.5f), "баннер не заходит под нижнюю панель");
            Assert.That(hudBar.yMin - b.yMax, Is.InRange(2 * PixelUi.Px * scale - 0.5f, 4 * PixelUi.Px * scale + 0.5f), "зазор 2–4 пикселя арта");
            // Тёмная полоса во всю ширину резерва: от края экрана до нижней части HUD.
            Rect strip = ScreenRect(cta.Strip.rectTransform);
            Assert.That(strip.xMin, Is.LessThanOrEqualTo(0f));
            Assert.That(strip.xMax, Is.GreaterThanOrEqualTo(Screen.width));
            Assert.That(strip.yMin, Is.LessThanOrEqualTo(0f));
            Assert.That(strip.yMax, Is.EqualTo(hudBar.yMin).Within(1f), "полоса до нижней панели HUD: поле в зазоре не видно");
            // «PLAY FREE» по центру таблички по высоте, на 2 пикселя арта внутри полей (не касается рамки).
            Rect play = ScreenRect(banner.Play.Rect);
            Assert.That(play.yMin - b.yMin, Is.EqualTo(b.yMax - play.yMax).Within(1f), "кнопка по центру");
            Assert.That(play.yMin - b.yMin, Is.GreaterThanOrEqualTo(6 * PixelUi.Px * scale - 0.5f));
            Assert.That(b.xMax - play.xMax, Is.EqualTo(7 * PixelUi.Px * scale).Within(1f), "поле справа больше рамки 6");
            Assert.That(b.yMax, Is.LessThanOrEqualTo(area.yMin + PlayableMode.BannerHeight * scale + 0.5f), "в резерве HUD");
            Assert.That(b.yMin, Is.GreaterThanOrEqualTo(area.yMin - 0.5f));
            Assert.That(b.xMin, Is.GreaterThanOrEqualTo(area.xMin - 0.5f));
            Assert.That(b.xMax, Is.LessThanOrEqualTo(area.xMax + 0.5f));
            Assert.That(b.height, Is.GreaterThan(PlayableMode.BannerHeight * scale * 0.8f));

            // Содержимое: «иконка игры», название, пять звёзд спрайтами, золотая «PLAY FREE» с искрой и пульсом.
            Assert.That(banner.Title.text, Is.EqualTo(Loc.T("playable.cta.title")));
            Assert.That(banner.Panel.Find("stars").GetComponentsInChildren<UnityEngine.UI.Image>().Count(i => i.sprite == PixelArt.Ui("icon_star")),
                Is.EqualTo(PlayableBanner.Stars));
            Assert.That(banner.Panel.Find("appIcon/cat").GetComponent<UnityEngine.UI.Image>().sprite, Is.SameAs(PixelArt.Ui("icon_cat_big")));
            Assert.That(banner.Play.Label.text, Is.EqualTo(Loc.T("playable.cta.play")));
            Assert.That(banner.Play.Style, Is.EqualTo(PixelButton.Green), "магазин — зелёная кнопка, золотые — навигация игры");
            Assert.That(banner.Play.Rect.GetComponent<PixelPop>(), Is.Not.Null, "пульс");
            Assert.That(banner.Shine, Is.Not.Null);
            yield return WaitFrames(() => banner.Shine.Sparkling, 150, 0);
            Assert.That(banner.Shine.Sparkling, Is.True, "искра на кнопке");

            // Нажимаются только иконка игры и «PLAY FREE»: табличка ловит тап (поле под ней не получает), но магазин не открывает.
            Assert.That(TopUiAt(b.center)?.transform.IsChildOf(banner.Root), Is.True, "баннер ловит тап поверх всего");
            Assert.That(TopUiAt(hudBar.center)?.transform.IsChildOf(banner.Root), Is.False, "нижняя панель HUD нажимается");
            Assert.That(banner.Panel.GetComponent<UnityEngine.UI.Button>(), Is.Null);
            m_StoreOpened.Clear();
            Tap(banner.Panel.gameObject);
            Assert.That(m_StoreOpened, Is.Empty, "тап по табличке мимо кнопок — не магазин");
            Tap(banner.Tap.gameObject);
            Assert.That(m_StoreOpened, Is.EqualTo(new[] { PlayableStore.Url }), "иконка игры");
            Assert.That(PlayableStore.Url, Is.EqualTo(PlayableStore.DefaultUrl), "в редакторе адреса страницы нет — полная веб-версия");
            Tap(banner.Play.Rect.gameObject);
            Assert.That(m_StoreOpened.Count, Is.EqualTo(2), "«PLAY FREE»");
            Assert.That(banner.Dimmed, Is.False);

            // Окно HUD баннер не прячет: он над затемнением окна, притушен (без блеска), «PLAY FREE» по-прежнему нажимается.
            Game.Hud.ShowBase();
            yield return null;
            Assert.That(banner.Visible, Is.True);
            Assert.That(banner.Dimmed, Is.True, "притушен под окном");
            Assert.That(banner.Shine.enabled, Is.False);
            Assert.That(TopUiAt(b.center)?.transform.IsChildOf(banner.Root), Is.True);
            Assert.That(TopUiAt(ScreenRect(banner.Play.Rect).center)?.transform.IsChildOf(banner.Play.Rect), Is.True);
            Game.Hud.HideBase();
            yield return null;
            Assert.That(banner.Dimmed, Is.False);
            Assert.That(banner.Shine.enabled, Is.True);
            Assert.That(cta.EndCardOpen, Is.False);
        }

        [UnityTest]
        public IEnumerator EndCard_AfterThirtyMinutes_StatsStore_KeepPlayingCloses()
        {
            var cta = Cta;
            var director = Director;
            // Котики успевают доставить золото: оно и есть «Gold delivered» сессии.
            yield return Advance(20, 0.5);
            yield return null;
            Assert.That(cta.Banner.Visible, Is.True);
            Assert.That(Game.State.Run.Counters.DeliveredValue.Sign, Is.GreaterThan(0), "первые доставки");
            Assert.That(cta.GoldDelivered, Is.EqualTo(Game.State.Run.Counters.DeliveredValue), "одна шахта — сумма её доставок");
            Assert.That(cta.EndCardOpen, Is.False);

            Game.State.Profile.PlayTimeMs = PlayableDirector.SegmentPlayTimeMs;
            yield return null;
            yield return null;
            Assert.That(director.SegmentComplete, Is.True);
            Assert.That(cta.EndCardOpen, Is.True, "финальный экран после 30 минут игры");
            Assert.That(cta.Banner.Visible, Is.False, "баннер скрыт под финальным экраном");
            Assert.That(director.AutoContinue, Is.False, "автопереход стоит, пока экран открыт");
            Assert.That(Game.Hud.PollBackButton, Is.False, "Back закрывает финальный экран, а не окна HUD под ним");
            Assert.That(cta.EndCard.Shade.color.a, Is.EqualTo(PlayableEndCard.ShadeAlpha).Within(1e-4f), "затемнение как у конца кампании");
            Assert.That(Quests.Panel.Visible, Is.False, "панель квеста под финальным экраном скрыта");
            Assert.That(Quests.Guide.Hint.Visible, Is.False);
            Assert.That(Quests.Suppressed, Is.True);
            Assert.That(Juice.Suppressed, Is.True);

            // Итоги и закрытый контент.
            var card = cta.EndCard;
            var cards = Game.Configs.Cards.Cards;
            int owned = cards.Count(c => Game.State.Profile.CardLevel(c.Id) >= 1);
            Assert.That(card.Title.text, Is.EqualTo(Loc.T("playable.cta.title")));
            Assert.That(card.Line.text, Is.EqualTo(Loc.T("playable.end.line.none")), "шахт ещё нет");
            Assert.That(card.StatValue(0), Is.EqualTo("0"));
            Assert.That(card.StatValue(1), Is.EqualTo(Game.State.Run.Workers.Count.ToString()));
            Assert.That(card.StatValue(2), Is.EqualTo(OutpostUiText.Money(cta.GoldDelivered)));
            Assert.That(card.StatValue(3), Is.EqualTo(owned + "/" + cards.Count));
            Assert.That(card.Teaser.text, Is.EqualTo(OutpostIds.LastZone + " mines • " + cards.Count + " cards • legendary gear"));
            Assert.That(card.Play.Label.text, Is.EqualTo(Loc.T("playable.cta.play")));
            Assert.That(card.Keep.Label.text, Is.EqualTo(Loc.T("playable.end.keep")));
            Assert.That(card.Again.Label.text, Is.EqualTo(Loc.T("playable.end.again")));
            Assert.That(card.Title.fontSize, Is.EqualTo(PixelUi.FontTitle));
            Assert.That(card.Play.Style, Is.EqualTo(PixelButton.Green));
            Assert.That(card.Keep.Style, Is.EqualTo(PixelButton.Dark), "«Keep playing» не спорит с PLAY FREE");
            Assert.That(card.Again.Style, Is.EqualTo(PixelButton.Dark));
            Assert.That(card.Hero.sprite, Is.SameAs(PixelArt.Ui("icon_cat_big")), "знак игры — котик баннера");
            if (card.GearVisible)
                for (int i = 0; i < OutpostIds.SlotCount; i++)
                {
                    string worn = Game.State.Profile.Equipped[i];
                    if (worn == null) Assert.That(card.GearIcon(i), Is.EqualTo("icon_lock"), "пустой слот — замок");
                    else Assert.That(card.GearIcon(i), Does.StartWith("icon_item_"));
                }

            // Окно целиком на экране, поверх всего; в магазин — только по тапу.
            yield return WaitFrames(() => false, 15, 0);
            Rect box = ScreenRect(card.Box), area = Screen.safeArea;
            Assert.That(box.xMin, Is.GreaterThanOrEqualTo(area.xMin - 0.5f));
            Assert.That(box.xMax, Is.LessThanOrEqualTo(area.xMax + 0.5f));
            Assert.That(box.yMin, Is.GreaterThanOrEqualTo(area.yMin - 0.5f));
            Assert.That(box.yMax, Is.LessThanOrEqualTo(area.yMax + 0.5f));
            Assert.That(TopUiAt(box.center)?.transform.IsChildOf(card.Root), Is.True);
            Assert.That(TopUiAt(ScreenRect(cta.Banner.Panel).center)?.transform.IsChildOf(card.Root), Is.True, "баннер под затемнением");
            Assert.That(m_StoreOpened, Is.Empty, "без автоперехода в магазин");
            Tap(card.Play.Rect.gameObject);
            Assert.That(m_StoreOpened, Is.EqualTo(new[] { PlayableStore.Url }));
            Assert.That(cta.EndCardOpen, Is.True, "после магазина экран остаётся");

            // «Keep playing»: экран закрыт, баннер, панель квеста и автопереход вернулись; второй раз экран не открывается.
            Tap(card.Keep.Rect.gameObject);
            Assert.That(cta.EndCardOpen, Is.False);
            Assert.That(cta.Banner.Visible, Is.True);
            Assert.That(director.AutoContinue, Is.True);
            Assert.That(Game.Hud.PollBackButton, Is.True);
            Assert.That(Quests.Suppressed, Is.False);
            yield return Advance(2);
            Assert.That(cta.EndCardOpen, Is.False);
            Assert.That(cta.EndCardShown, Is.True);
            Assert.That(cta.Banner.Visible, Is.True);
            Assert.That(Quests.Panel.Visible, Is.True, "панель квеста снова видна");
            Assert.That((bool)JObject.Parse(File.ReadAllText(StatsPath))["EndCardShown"], Is.True, "отметка — в файле профиля");
        }

        [UnityTest]
        public IEnumerator EndCard_AfterMineTen_PlayAgainStartsFreshGame()
        {
            var cta = Cta;
            yield return Advance(PlayableCta.BannerDelaySeconds + 0.5);
            for (int zone = 1; zone <= PlayableDirector.SegmentLastMine; zone++) Game.State.Profile.CompletedZones.Add(zone);
            yield return null;
            yield return null;
            Assert.That(cta.EndCardOpen, Is.True, "финальный экран после шахты 10");
            Assert.That(cta.EndCard.Line.text, Is.EqualTo(Loc.Plural("playable.end.line", PlayableDirector.SegmentLastMine)));
            Assert.That(cta.EndCard.StatValue(0), Is.EqualTo(PlayableDirector.SegmentLastMine.ToString()));
            Game.State.Profile.Elixir = 4242;
            yield return Advance(2.5, 0.5);
            Assert.That(File.Exists(StatsPath), Is.True, "золото сессии записано рядом с сохранением режима");

            var old = Game;
            Tap(cta.EndCard.Again.Rect.gameObject);
            for (int frame = 0; frame < 600 && (Game == null || ReferenceEquals(Game, old) || Game.Phase != OutpostControllerPhase.Playing); frame++)
                yield return null;
            Assert.That(ReferenceEquals(Game, old), Is.False, "сцена загружена заново");
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
            Assert.That(Game.State.Run.Zone, Is.EqualTo(1));
            Assert.That(Game.State.Profile.CompletedZones, Is.Empty, "новый профиль");
            Assert.That(Game.State.Profile.Elixir, Is.Not.EqualTo(4242));
            Assert.That(Game.State.Profile.PlayTimeMs, Is.LessThan(60_000));
            Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(3));
            yield return Advance(PlayableCta.BannerDelaySeconds + 0.25);
            yield return null;
            Assert.That(Cta, Is.Not.Null);
            Assert.That(Director.SegmentComplete, Is.False);
            Assert.That(Cta.EndCardOpen, Is.False);
            Assert.That(Cta.Banner.Visible, Is.True, "баннер в новой игре");
            Assert.That(Cta.GoldDelivered, Is.EqualTo(Game.State.Run.Counters.DeliveredValue), "золото сессии — заново");
        }

        [UnityTest]
        public IEnumerator SessionGold_SumsMinesAndSurvivesReload()
        {
            yield return Advance(1);
            // Файл прошлой шахты этого профиля: 12345 золота за прежние шахты.
            var seed = Game.State.Profile.Seed.ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(StatsPath));
            File.WriteAllText(StatsPath, "{\"Version\":1,\"Profile\":\"" + seed + "\",\"RunId\":\"old-run\",\"RunDelivered\":\"500\",\"Delivered\":\"12345\"}");
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return WaitPlaying();
            yield return Advance(20, 0.5);
            yield return null;
            var run = Game.State.Run.Counters.DeliveredValue;
            Assert.That(run.Sign, Is.GreaterThan(0));
            Assert.That(Cta.GoldDelivered, Is.EqualTo(new BigInteger(12345) + run), "прежние шахты + доставки текущей");

            // Чужой профиль в файле не считается.
            File.WriteAllText(StatsPath, "{\"Version\":1,\"Profile\":\"1\",\"RunId\":\"old-run\",\"RunDelivered\":\"0\",\"Delivered\":\"999999\"}");
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return WaitPlaying();
            yield return Advance(0.5);
            yield return null;
            Assert.That(Cta.GoldDelivered, Is.EqualTo(Game.State.Run.Counters.DeliveredValue));
            yield return Advance(2.5, 0.5);
            var saved = JObject.Parse(File.ReadAllText(StatsPath));
            Assert.That((string)saved["Profile"], Is.EqualTo(seed));
            Assert.That((string)saved["RunId"], Is.EqualTo(Game.State.Run.RunId));
        }
    }
}
