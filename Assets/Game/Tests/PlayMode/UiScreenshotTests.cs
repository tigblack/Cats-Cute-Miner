using System;
using System.Collections;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using CatMiners.Runtime;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>
    /// Снимки пиксельного HUD и окон для визуальной приёмки (Docs/Art/PIXEL_STYLE_GUIDE.md, раздел 6):
    /// Docs/Reports/screenshots/ui_*.png в 1080×1920. Основной экран zone1 с котиками, контекстная панель кузницы и шахты,
    /// настройки, окно сундука, «Пока вас не было», подтверждение перехода, экран «Карты» и окно карты, подсказка обучения, экран конца кампании.
    /// Слот Debug, временный каталог сохранений, остановленные часы (таймеры на снимках не плывут), в журнале нет ошибок.
    /// Canvas на время снимка рисуется в текстуру без масштаба: единица Canvas = пиксель снимка, как на эталонном экране.
    /// </summary>
    public class UiScreenshotTests
    {
        static readonly string[] s_TutorialSteps = { "t1", "t1b", "t2", "t2b", "t3", "t3b", "t4", "t4b", "t5", "t5a", "t5b", "t6", "t7", "t7a", "t8", "t8a", "t9", "t10", "t11" };
        static readonly Vector3[] s_CornersA = new Vector3[4], s_CornersB = new Vector3[4];

        string m_SaveDir;
        LogWatcher m_Log;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_SaveDir = PlayModeUtil.UseTempSaveDir();
            CameraRig.OverrideAspect = 1080f / 1920f;
            GameController.ClockOverride = new ManualClock(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 100.0);
            m_Log = new LogWatcher();
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            m_Log.Dispose();
            CameraRig.OverrideAspect = 0f;
            GameController.ClockOverride = null;
            PlayModeUtil.DropTempSaveDir(m_SaveDir);
            yield break;
        }

        /// <summary>Основной экран, контекстная панель кузницы, настройки, сундук, офлайн и подтверждение перехода — zone1.</summary>
        [UnityTest]
        public IEnumerator MainHudContextAndWindows()
        {
            var gc = default(GameController);
            yield return StartDebugZone(1, g => gc = g);
            var s = gc.Session;
            SkipTutorial(s);

            // Пара котиков на площадке: один копает, остальные свободны (кнопка свободных котиков видна).
            // Нанятый котик приезжает в бочке — бочки открываются тапом (OpenContainer).
            s.DebugAddGold(s.HirePrice() * GameNumber.FromDouble(20));
            gc.Hire();
            gc.Hire();
            gc.Hire();
            Assert.AreEqual(3, PlayModeUtil.OpenHireBarrels(gc), "наём ставит бочки найма");
            Assert.GreaterOrEqual(s.Mine.Cats.Count, 2, "наняты котики");
            AssignFirstCat(gc);
            s.DebugAddDynamite(3);
            s.Player.ChestReveals.Add(new ChestResult { OpId = 990001, GachaId = "GachaWooden", Source = "test", Elixir = GameNumber.FromDouble(5) });
            gc.ClearSelection();
            gc.Rig.FocusOn(gc.Rig.HomeFocusZ(s));
            yield return PlayModeUtil.WaitReal(0.3f);
            var hire = PlayModeUtil.FindButton(gc.Hud, "Hire");
            Assert.IsNotNull(hire);
            Assert.IsTrue(hire.gameObject.activeInHierarchy, "кнопка «Нанять» на экране");
            var chests = PlayModeUtil.FindButton(gc.Hud, "ChestsBadge");
            Assert.IsTrue(chests != null && chests.interactable, "кнопка сундуков доступна: в очереди есть сундук");
            yield return Capture(gc, "ui_hud_zone1.png");

            // Контекстная панель кузницы: «+1» доступна.
            int cart = s.Layout.Cart.Ordinal;
            s.DebugAddGold(s.UpgradeCost(cart, s.Mine.Objects[cart].Gen.Level) * GameNumber.FromDouble(3));
            gc.TapObject(cart);
            yield return PlayModeUtil.WaitReal(0.25f);
            var plusOne = PlayModeUtil.FindButton(gc.Hud, "ctx0");
            Assert.IsTrue(plusOne != null && plusOne.gameObject.activeInHierarchy && plusOne.interactable, "кнопка «+1» кузницы доступна");
            yield return Capture(gc, "ui_context_forge.png");
            gc.ClearSelection();

            // Настройки: звук и вибрация.
            gc.Hud.ShowSettings();
            Assert.IsTrue(gc.Hud.SettingsOpen, "окно настроек открыто");
            yield return Capture(gc, "ui_settings.png");
            gc.Hud.HideSettings();
            Assert.IsFalse(gc.Hud.SettingsOpen);

            // Окно сундука: карточки и эликсир строками с иконками.
            var chest = new ChestResult { GachaId = "GachaIron", Source = "test", Elixir = GameNumber.FromDouble(25) };
            int added = 0;
            foreach (var c in s.Balance.CardsInOrder)
            {
                if (c.Rarity >= Rarity.Majestic || added == 3) continue;
                chest.Cards.Add(new CardGrant { CardId = c.Id, Count = 1 + added });
                added++;
            }
            gc.Hud.ShowChest(chest);
            Assert.IsTrue(gc.Hud.ModalOpen, "окно сундука открыто");
            yield return Capture(gc, "ui_chest.png");
            yield return ClickModal(gc, Loc.T("hud.chest.claim"));

            // «Пока вас не было».
            gc.Hud.ShowOffline(new OfflineSummary
            {
                Ticks = 2 * 3600 * GameSession.TicksPerSecond + 17 * 60 * GameSession.TicksPerSecond,
                Gold = GameNumber.FromDouble(12345),
                Elixir = GameNumber.FromDouble(40),
                RocksDestroyed = 14,
                Chests = 1,
                ShaftsCleared = 1,
                ContainersSpawned = 1,
            });
            Assert.IsTrue(gc.Hud.ModalOpen, "окно офлайна открыто");
            yield return Capture(gc, "ui_offline.png");
            yield return ClickModal(gc, Loc.T("hud.offline.ok"));

            // Подтверждение перехода: выход разрушен, ворота открыты, шахты активированы — условия zone1 выполнены.
            int exit = s.Layout.Exit.Ordinal;
            s.DebugCompleteTarget(exit);
            yield return null;
            yield return null;
            yield return CloseModals(gc);
            foreach (var gate in s.Layout.Gates)
            {
                if (s.Mine.Objects[gate.Ordinal].Status == ObjectStatus.Intact) s.DebugCompleteTarget(gate.Ordinal);
                if (s.Mine.Objects[gate.Ordinal].Status == ObjectStatus.Broken) gc.OpenGate(gate.Ordinal);
                yield return CloseModals(gc);
            }
            foreach (var sh in s.Layout.Shafts)
            {
                if (s.Mine.Objects[sh.Ordinal].Status == ObjectStatus.Intact) s.DebugCompleteTarget(sh.Ordinal);
                if (s.Mine.Objects[sh.Ordinal].Status == ObjectStatus.ReadyToActivate) gc.ActivateShaft(sh.Ordinal);
                yield return CloseModals(gc);
            }
            Assert.IsTrue(s.CanCompleteZone(out string why), why);
            gc.RequestNextMine();
            Assert.IsTrue(gc.Hud.ModalOpen, "окно подтверждения перехода открыто");
            yield return Capture(gc, "ui_next_mine.png");
            yield return ClickModal(gc, Loc.T("controller.next_mine.stay"));
            Assert.AreEqual(1, s.Mine.Zone, "«Остаться» не переводит в следующую шахту");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>Контекстная панель активированной шахты zone3, экран «Карты» с сеткой карт и окно карты с улучшением.</summary>
        [UnityTest]
        public IEnumerator ShaftContextAndBlueprints()
        {
            var gc = default(GameController);
            yield return StartDebugZone(3, g => gc = g);
            var s = gc.Session;
            SkipTutorial(s);
            Assert.Greater(s.Layout.Shafts.Count, 0, "в zone3 есть шахты");

            // Шахта открытого сектора: расчищена и активирована (ручной сбор — чертежа ещё нет).
            int shaft = s.Layout.Shafts[0].Ordinal;
            foreach (var sh in s.Layout.Shafts)
                if (s.IsCellOpen(sh.Cells[0])) { shaft = sh.Ordinal; break; }
            s.DebugCompleteTarget(shaft);
            gc.ActivateShaft(shaft);
            yield return null;
            yield return CloseModals(gc);
            Assert.AreEqual(ObjectStatus.Producing, s.Mine.Objects[shaft].Status, "шахта работает");
            s.DebugAddGold(s.UpgradeCost(shaft, s.Mine.Objects[shaft].Gen.Level) * GameNumber.FromDouble(5));
            for (int i = 0; i < 4; i++) s.DebugSpawnCat(3 + i);
            PlayModeUtil.AssignIdleCats(gc);
            gc.Rig.FocusOn(gc.Board.ObjectCenter(shaft).z);
            gc.TapObject(shaft);
            yield return PlayModeUtil.WaitReal(0.25f);
            Assert.AreEqual(shaft, gc.SelectedOrdinal, "шахта выбрана");
            var ctx0 = PlayModeUtil.FindButton(gc.Hud, "ctx0");
            Assert.IsTrue(ctx0 != null && ctx0.gameObject.activeInHierarchy, "у шахты есть кнопка улучшения");
            // Причина ручного сбора называет карту по имени из каталога, а не по ID из токена.
            var shaftObj = s.Layout.Objects[shaft];
            if (!s.Mine.Objects[shaft].Gen.Automated && !string.IsNullOrEmpty(shaftObj.CardId))
            {
                var ctxBody = FindText(gc.Hud, "SafeArea/Context/body");
                Assert.IsNotNull(ctxBody, "текст контекстной панели");
                int have = s.CardLevel(shaftObj.CardId);
                string need = Loc.F("hud.generator.manual_need", PresentationCatalog.CardName(s.Balance, shaftObj.CardId), shaftObj.CardLevel,
                    have > 0 ? Loc.F("core.automation.have_level", have) : Loc.T("core.automation.have_none"));
                StringAssert.Contains(need, ctxBody.text, "причина ручного сбора с именем карты");
            }
            yield return Capture(gc, "ui_context_shaft.png");
            gc.ClearSelection();

            // «Карты» (как на референсе): карты зоны собраны, у карты шахты хватает копий и эликсира на улучшение.
            s.DebugGiveZoneCards();
            s.DebugAddElixir(GameNumber.FromDouble(5000));
            string zoneCard = null;
            foreach (var sh in s.Layout.Shafts) if (!string.IsNullOrEmpty(sh.CardId)) { zoneCard = sh.CardId; break; }
            Assert.IsNotNull(zoneCard, "у шахт zone3 есть карта-управляющий");
            Assert.IsTrue(s.TryGetCardUpgradeCost(zoneCard, out int needCopies, out _), "карту шахты можно улучшать");
            s.CardStateOf(zoneCard).Duplicates = needCopies;
            yield return CloseModals(gc);
            gc.Hud.ToggleBlueprints();
            Assert.IsTrue(gc.Hud.BlueprintsOpen, "экран «Карты» открыт");
            yield return null;
            // Сетка: по ячейке на каждую карту коллекции (редкости 1–5); карта шахты — лицом вверх, с уровнем и зелёной шкалой.
            var grid = gc.Hud.transform.Find("SafeArea/Blueprints/viewport/content");
            Assert.IsNotNull(grid, "сетка карт");
            int collection = 0;
            foreach (var c in s.Balance.CardsInOrder) if (c.Rarity < Rarity.Majestic) collection++;
            int cells = 0;
            foreach (Transform child in grid) if (child.gameObject.activeSelf) cells++;
            Assert.AreEqual(collection, cells, "ячейка на каждую карту коллекции");
            var cell = grid.Find("card_" + zoneCard);
            Assert.IsNotNull(cell, "ячейка карты шахты");
            Assert.IsTrue(cell.Find("title").gameObject.activeSelf, "найденная карта — лицом вверх");
            Assert.IsTrue(cell.Find("arrow").gameObject.activeSelf, "карту можно улучшить — стрелка на шкале");
            Assert.AreEqual(PresentationCatalog.CardShortName(s.Balance, zoneCard), cell.Find("title").GetComponent<TMP_Text>().text, "короткое имя на карте");
            yield return Capture(gc, "ui_blueprints.png");
            // Тап по карте — окно карты с «Улучшить» и ценой в эликсире под подписью.
            cell.GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.IsTrue(gc.Hud.CardDetailOpen, "окно карты открыто");
            Assert.AreEqual(zoneCard, gc.Hud.CardDetailId);
            AssertCardPricesBelowLabels(gc);
            yield return Capture(gc, "ui_card_detail.png");
            int levelBefore = s.CardLevel(zoneCard);
            PlayModeUtil.Click(PlayModeUtil.FindButton(gc.Hud, "upgrade"));
            yield return null;
            Assert.AreEqual(levelBefore + 1, s.CardLevel(zoneCard), "«Улучшить» в окне карты улучшает карту");
            gc.Hud.ToggleBlueprints();
            Assert.IsFalse(gc.Hud.BlueprintsOpen);
            Assert.IsFalse(gc.Hud.CardDetailOpen, "окно карты закрывается вместе с экраном");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>Подсказка обучения (шаг 1 — «Нанять»): пузырь, лапка и рамка фокуса у кнопки найма.</summary>
        [UnityTest]
        public IEnumerator TutorialHintPointsAtHire()
        {
            var gc = default(GameController);
            yield return StartDebugZone(1, g => gc = g);
            var s = gc.Session;
            s.DebugAddGold(s.HirePrice() * GameNumber.FromDouble(2));
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until && (gc.Hud.Hint == null || !gc.Hud.Hint.Visible)) yield return null;
            Assert.IsNotNull(gc.Hud.Hint, "HUD создал подсказку обучения");
            Assert.AreSame(gc.Hud.Hint, gc.Tutorial.Hint, "обучение показывает подсказки через HUD");
            Assert.IsTrue(gc.Hud.Hint.Visible, "подсказка шага «Нанять» видна");
            Assert.AreEqual(Loc.T("tutorial.hint.hire"), gc.Hud.Hint.Text);
            Assert.IsTrue(gc.Hud.Hint.Dim, "шаг «Нанять» затемняет экран вокруг кнопки");
            yield return Capture(gc, "ui_tutorial_hint.png");

            // Под окном настроек подсказки нет (её показ поднимает пузырь поверх окна); после закрытия она возвращается.
            gc.Hud.ShowSettings();
            yield return PlayModeUtil.WaitReal(0.4f);
            Assert.IsFalse(gc.Hud.Hint.Visible, "под окном настроек подсказка скрыта");
            gc.Hud.HideSettings();
            yield return PlayModeUtil.WaitReal(0.4f);
            Assert.IsTrue(gc.Hud.Hint.Visible, "после закрытия настроек подсказка снова видна");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>Экран завершения кампании с кнопкой неоткрытых сундуков.</summary>
        [UnityTest]
        public IEnumerator CampaignEndScreen()
        {
            var gc = default(GameController);
            yield return StartDebugZone(1, g => gc = g);
            var s = gc.Session;
            s.Player.ChestReveals.Add(new ChestResult { OpId = 990002, GachaId = "GachaGolden", Source = "test", Elixir = GameNumber.FromDouble(50) });
            gc.Hud.ShowCampaignEnd(Loc.T("controller.campaign_end.title"),
                Loc.F("controller.campaign_end.body", gc.Balance.ZoneCount, NumberFormat.FormatDuration(37 * 3600 + 25 * 60), 38, 40));
            yield return PlayModeUtil.WaitReal(0.25f);
            Assert.IsTrue(gc.Hud.CampaignEndVisible, "экран конца кампании показан");
            Assert.IsFalse(PlayModeUtil.FindButton(gc.Hud, "Hire").gameObject.activeInHierarchy, "нижняя часть HUD скрыта");
            var openChests = PlayModeUtil.FindButton(gc.Hud, "chests");
            Assert.IsTrue(openChests != null && openChests.gameObject.activeInHierarchy, "кнопка «Открыть сундуки» на финальном экране");
            // Рамка — по содержимому: текст не обрезан, между ним и кнопкой сундуков нет пустой полосы.
            var endText = FindText(gc.Hud, "SafeArea/CampaignEnd/Box/body/text");
            Assert.IsNotNull(endText, "текст экрана конца кампании");
            Canvas.ForceUpdateCanvases();
            Assert.IsFalse(endText.isTextOverflowing, "текст экрана конца кампании помещается");
            var box = (RectTransform)endText.transform.parent.parent;
            endText.rectTransform.GetWorldCorners(s_CornersA);
            ((RectTransform)openChests.transform).GetWorldCorners(s_CornersB);
            float gap = box.InverseTransformPoint(s_CornersA[0]).y - box.InverseTransformPoint(s_CornersB[1]).y;
            Assert.That(gap, Is.InRange(0f, 8 * PixelUi.Px), "текст стоит сразу над кнопкой сундуков");
            yield return Capture(gc, "ui_campaign_end.png");
            gc.Hud.HideCampaignEnd();
            Assert.IsFalse(gc.Hud.CampaignEndVisible);
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        // ───────────── Помощники ─────────────

        /// <summary>Текст по пути от корня HUD (например, «SafeArea/Context/body»); null — нет такого объекта или текста.</summary>
        static TMP_Text FindText(Hud hud, string path)
        {
            var t = hud.transform.Find(path);
            return t != null ? t.GetComponent<TMP_Text>() : null;
        }

        /// <summary>
        /// Окно карты: у кнопки улучшения с ценой в эликсире подпись целиком выше строки цены (иконка капли
        /// не наезжает на текст), и такая кнопка есть.
        /// </summary>
        static void AssertCardPricesBelowLabels(GameController gc)
        {
            Canvas.ForceUpdateCanvases();
            int withPrice = 0;
            foreach (var b in gc.Hud.GetComponentsInChildren<Button>(false))
            {
                if (b.gameObject.name != "upgrade") continue;
                var content = b.transform.Find("content");
                var price = content != null ? content.Find("price") as RectTransform : null;
                var label = content != null ? content.Find("label") as RectTransform : null;
                if (price == null || label == null || !price.gameObject.activeSelf) continue;
                withPrice++;
                price.GetWorldCorners(s_CornersA);
                label.GetWorldCorners(s_CornersB);
                var local = (RectTransform)b.transform;
                float priceTop = local.InverseTransformPoint(s_CornersA[1]).y, labelBottom = local.InverseTransformPoint(s_CornersB[0]).y;
                Assert.GreaterOrEqual(labelBottom, priceTop - 0.01f, b.transform.parent.name + ": подпись кнопки выше цены");
            }
            Assert.Greater(withPrice, 0, "в окне карты есть кнопка улучшения с ценой");
        }

        /// <summary>Сцена CoreGame, новый слот Debug в зоне <paramref name="zone"/>, окна старта закрыты.</summary>
        static IEnumerator StartDebugZone(int zone, Action<GameController> result)
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            Assert.IsNotNull(gc, "GameBootstrap не создал контроллер");
            yield return CloseModals(gc);
            gc.SwitchSlot(SaveSlots.Debug, true, zone);
            yield return null;
            yield return null;
            yield return CloseModals(gc);
            Assert.AreEqual(zone, gc.Session.Mine.Zone);
            Assert.AreEqual(SaveSlots.Debug, gc.Session.State.Slot);
            result(gc);
        }

        /// <summary>Шаги обучения засчитаны: на снимках HUD нет подсказок.</summary>
        static void SkipTutorial(GameSession s)
        {
            var done = s.Player.TutorialDone;
            foreach (var id in s_TutorialSteps)
                if (!done.Contains(id)) done.Add(id);
        }

        /// <summary>Первый котик — на первый камень, который его принимает (через команду UI).</summary>
        static void AssignFirstCat(GameController gc)
        {
            var s = gc.Session;
            var cat = s.Mine.Cats[0];
            for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
            {
                var o = s.Layout.Objects[ord];
                if (o.Kind != ObjectKind.Rock || !s.AcceptsCat(ord, cat.Id)) continue;
                gc.DropCat(cat.Id, s.Layout.Col(o.Cells[0]) + 0.5, s.Layout.Row(o.Cells[0]) + 0.5);
                return;
            }
        }

        static IEnumerator ClickModal(GameController gc, string label)
        {
            var button = PlayModeUtil.FindModalButton(gc.Hud, label);
            Assert.IsNotNull(button, "кнопка окна «" + label + "»");
            PlayModeUtil.Click(button);
            yield return null;
        }

        /// <summary>Закрыть окна (сундуки наград, сообщения) кнопками, как игрок.</summary>
        static IEnumerator CloseModals(GameController gc)
        {
            yield return PlayModeUtil.CloseModals(gc);
            Assert.IsFalse(gc.Hud.ModalOpen, "окна закрыты");
        }

        /// <summary>Снимок 1080×1920 камеры с HUD (PlayModeUtil.CaptureUi): Canvas без масштаба, единица Canvas = пиксель снимка.</summary>
        static IEnumerator Capture(GameController gc, string file) => PlayModeUtil.CaptureUi(gc, file);
    }
}
