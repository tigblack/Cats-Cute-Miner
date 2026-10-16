using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>
    /// Вид заводов на поле (ТЗ 2.2 §4.3, §5, §6.1, §8, §11; пакет WP5a): цепи разбиваются ударом игрока и котиками (разлёт звеньев,
    /// прокладка трубы, тост), кнопка «Deliver ×N» над готовым заводом работает и капсула доходит до склада («+N»), значок AUTO при
    /// нужном уровне карточки, тапы по заводу и складу, стрелка к готовому заводу за краем экрана, кадр без выделений в шахте 140.
    /// </summary>
    public sealed class OutpostFactoryViewTests : OutpostCardsFixture
    {
        IEnumerator Advance(double seconds)
        {
            while (seconds > 0)
            {
                double step = Math.Min(0.25, seconds);
                Clock.Advance(step);
                seconds -= step;
                yield return null;
            }
        }

        IEnumerator LoadMine(int zone)
        {
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Assert.That(Game.IsDebugProfile, Is.True);
            Game.LoadZone(zone);
            yield return WaitPlaying();
            for (int i = 0; i < 300 && Game.State.Run.Zone != zone; i++) yield return null;
            Assert.That(Game.State.Run.Zone, Is.EqualTo(zone));
            yield return null;
        }

        IEnumerator CloseWindows()
        {
            for (int i = 0; i < 20 && Game.Hud.AnyWindowOpen; i++)
            {
                Game.Hud.HandleBack();
                yield return null;
            }
        }

        FactoryOverlay Overlay => Game.Input.Factories;
        FactoryState Factory(int id) => Game.FactoryOf(id);

        string ToastText()
        {
            var bg = Game.Hud.transform.Find("SafeArea/ToastBg");
            return bg != null && bg.gameObject.activeSelf ? bg.GetComponentInChildren<TMP_Text>().text : null;
        }

        IEnumerator FocusOn(Vector3 world)
        {
            Game.Rig.FocusOn(new Vector2(world.x, world.z));
            yield return null;
            yield return null;
        }

        /// <summary>DEV-изменение применяется через запись и пересоздание симуляции: ждём, пока условие не выполнится.</summary>
        IEnumerator WaitFor(Func<bool> done, int frames = 300)
        {
            for (int i = 0; i < frames && !done(); i++) yield return null;
            Assert.That(done(), Is.True, "DEV-изменение не применилось");
            yield return null;
        }

        /// <summary>Ближайший к лагерю завод шахты.</summary>
        int NearestFactory() => Game.Map.Factories.OrderBy(f => f.RouteSteps).First().TargetId;

        [UnityTest]
        public IEnumerator Chains_BreakByStrikeAndCats_PipeLaysAndToast()
        {
            yield return LoadMine(2);
            int id = NearestFactory();
            var tv = Game.Board.TargetViewOf(id);
            var fv = Game.Board.FactoryViewOf(id);
            Assert.That(tv.IsFactory && tv.Chains, Is.True, "закрытый завод — здание в цепях");
            Assert.That(fv, Is.Not.Null);
            Assert.That(fv.Status, Is.EqualTo(FactoryStatus.Locked));
            Assert.That(Game.Board.Pipes.HasPipe(id), Is.True);
            Assert.That(Game.Board.Pipes.IsShown(id), Is.False, "у закрытого завода трубы нет");

            // Удар кирки игрока по цепям: если завод за воротами — котики сначала открывают его сектор (×20).
            Assert.That(Game.TryGetTargetWorldPosition(id, out var center), Is.True);
            Game.SimSpeed = 20;
            for (int frame = 0; frame < 4000 && !Game.CanStrike(id) && Factory(id).Status == FactoryStatus.Locked; frame++)
            {
                if (Game.Hud.AnyWindowOpen) yield return CloseWindows();
                Clock.Advance(0.25);
                yield return null;
            }
            Game.SimSpeed = 1;
            yield return CloseWindows();
            yield return FocusOn(center);
            var ts = Game.Sim.TargetById(id);
            long hp = ts.RemainingHpUnits;
            if (Game.CanStrike(id))
            {
                var cues = new List<OutpostViewCueArgs>();
                Action<OutpostViewCueArgs> onCue = c => cues.Add(c);
                Game.Board.Cue += onCue;
                Assert.That(Game.BeginStrike(id, center), Is.True);
                for (int frame = 0; frame < 120 && Game.Sim.TargetById(id).RemainingHpUnits == hp; frame++) yield return null;
                Game.Board.Cue -= onCue;
                Assert.That(Game.Sim.TargetById(id).RemainingHpUnits, Is.LessThan(hp), "удар игрока снял HP цепей");
                Assert.That(cues.Any(c => c.Cue == OutpostViewCue.PlayerStrike && c.Chains), Is.True, "звук удара по цепям");
            }
            else Debug.Log("[FactoryView] mine 2 factory is not strikeable from the start; cats break the chains");

            // Котики разбивают цепи (×20): событие вживую — разлёт звеньев, прокладка трубы, тост.
            bool unlocked = false;
            string toast = null;
            // Тост ставит FactoryOverlay (подписан раньше теста) — читаем его в момент события, пока окно сундука его не сменило.
            Action<OutpostEvent> onEvent = e =>
            {
                if (e.Type != OutpostEventType.FactoryUnlocked || e.TargetId != id) return;
                unlocked = true;
                toast = ToastText();
            };
            Game.EventPresented += onEvent;
            Game.SimSpeed = 20;
            try
            {
                for (int frame = 0; frame < 4000 && !unlocked; frame++)
                {
                    if (Game.Hud.AnyWindowOpen) yield return CloseWindows();
                    Clock.Advance(0.25);
                    yield return null;
                }
                Game.SimSpeed = 1;
            }
            finally
            {
                Game.EventPresented -= onEvent;
            }
            Assert.That(unlocked, Is.True, "котики разбили цепи");
            Assert.That(toast, Is.EqualTo(Loc.F("factories.toast.unlocked", FactoryText.FactoryName("milk"))));
            Assert.That(tv.Chains, Is.False, "цепей больше нет");
            Assert.That(tv.Destroyed, Is.True);
            Assert.That(tv.GetComponent<OutpostPickable>().isActiveAndEnabled, Is.True, "открытый завод выбирается тапом");
            Assert.That(tv.GetComponent<BoxCollider>().enabled, Is.True);
            Assert.That(Game.Board.Pipes.IsShown(id), Is.True, "труба появилась");
            Assert.That(fv.BreakPlaying || Game.Board.Pipes.RevealProgress(id) < 1f, Is.True, "открытие вживую анимируется");
            float t0 = Time.realtimeSinceStartup;
            while (Game.Board.Pipes.RevealProgress(id) < 1f && Time.realtimeSinceStartup - t0 < 2f) yield return null;
            Assert.That(Game.Board.Pipes.RevealProgress(id), Is.EqualTo(1f));
            Assert.That(Time.realtimeSinceStartup - t0, Is.LessThanOrEqualTo(1.2f), "прокладка трубы — до 1 с");
            Assert.That(fv.Status, Is.Not.EqualTo(FactoryStatus.Locked));
        }

        [UnityTest]
        public IEnumerator Deliver_ButtonAppearsAndWorks_CapsuleReachesWarehouse()
        {
            yield return LoadMine(2);
            int id = NearestFactory();
            Game.DebugUnlockFactories();
            yield return WaitFor(() => Factory(id).Status != FactoryStatus.Locked);
            var f = Factory(id);
            var fv = Game.Board.FactoryViewOf(id);
            Assert.That(f.Status, Is.EqualTo(FactoryStatus.Working));
            Assert.That(f.Automated, Is.False, "без карточки ca002 завод шахты 2 ручной (приложение B, п. 1)");
            Assert.That(Game.Board.Pipes.IsShown(id) && Game.Board.Pipes.RevealProgress(id) == 1f, Is.True, "без событий труба сразу целиком");
            Assert.That(fv.WorkAnimating && fv.BarVisible, Is.True, "Working: кадры работы и полоса цикла");
            Assert.That(fv.AutoBadgeVisible, Is.False, "у ручного завода значка AUTO нет");
            Assert.That(Game.TryGetTargetWorldPosition(id, out var center), Is.True);
            yield return FocusOn(center);
            Assert.That(Overlay.DeliverButtonOf(id), Is.Null, "пока идёт цикл, кнопки нет");

            yield return Advance(f.CycleEnd > 0 ? (f.CycleEnd - Game.Sim.Now) / 1e6 + 0.3 : 9);
            f = Factory(id);
            Assert.That(f.Status, Is.EqualTo(FactoryStatus.Ready));
            yield return FocusOn(center);
            Assert.That(fv.BarVisible && fv.BarFraction == 1f && !fv.WorkAnimating, Is.True, "Ready: полная полоса без анимации");
            Assert.That(Overlay.DeliverButtonOf(id), Is.Not.Null, "кнопка «Deliver» над готовым заводом");
            Assert.That(Overlay.DeliverLabelOf(id), Is.EqualTo(Loc.F("factories.deliver", f.ReadyUnits)));
            int units = f.ReadyUnits;

            // Кадр с кнопкой на экране — без выделений памяти (вид поля и кнопки, ТЗ 2.2 §15).
            var sim = Game.Sim;
            for (int i = 0; i < 10; i++)
            {
                Game.Board.Sync(sim, 0.016f);
                Overlay.Refresh();
            }
            long probe = GC.GetTotalMemory(false);
            var junk = new byte[64 * 1024];
            if (GC.GetTotalMemory(false) - probe >= 32 * 1024 && junk.Length > 0)
            {
                long before = GC.GetTotalMemory(false);
                for (int i = 0; i < 1000; i++)
                {
                    Game.Board.Sync(sim, 0.016f);
                    Overlay.Refresh();
                }
                long grown = GC.GetTotalMemory(false) - before;
                Assert.That(grown, Is.LessThan(8 * 1024), "кадр с кнопкой «Deliver» выделяет память: +" + grown + " байт за 1000 кадров");
            }
            Assert.That(Overlay.DeliverButtonOf(id), Is.Not.Null);

            Assert.That(Overlay.ClickDeliver(id), Is.True);
            yield return null;
            Assert.That(Factory(id).Status, Is.EqualTo(FactoryStatus.Working), "после доставки — новый цикл");
            Assert.That(Game.State.Run.PipeBatches.Count, Is.EqualTo(1));
            Assert.That(Overlay.DeliverButtonOf(id), Is.Null);
            yield return Advance(0.25);
            Assert.That(Game.Board.Pipes.VisibleCapsules(id), Is.EqualTo(1), "капсула в трубе");

            long transit = Game.Sim.FactoryTransitTicks(id);
            int popups = 0;
            for (double waited = 0; waited < transit / 1e6 + 1 && Game.GoodsCount("milk") == 0; waited += 0.25)
            {
                Clock.Advance(0.25);
                yield return null;
                popups = Math.Max(popups, Game.Board.GoodsPopupsActive);
            }
            Assert.That(Game.GoodsCount("milk"), Is.EqualTo(units), "партия пришла на склад");
            yield return null;
            popups = Math.Max(popups, Game.Board.GoodsPopupsActive);
            Assert.That(popups, Is.GreaterThan(0), "«+N» у склада");
            Assert.That(Game.Board.Pipes.VisibleCapsules(id), Is.EqualTo(0), "капсула исчезла у склада");
            Assert.That(Game.Board.Camp.HasWarehouse, Is.True);
        }

        [UnityTest]
        public IEnumerator Auto_AppearsWhenCardReachesLevel()
        {
            yield return LoadMine(2);
            int id = NearestFactory();
            Game.DebugUnlockFactories();
            yield return WaitFor(() => Factory(id).Status != FactoryStatus.Locked);
            var fv = Game.Board.FactoryViewOf(id);
            Assert.That(fv.AutoBadgeVisible, Is.False);
            var req = Game.Map.Factory(id);
            Assert.That(Game.State.Profile.CardLevel(req.RequiredCardId), Is.LessThan(req.RequiredCardLevel));

            var events = new List<OutpostEvent>();
            Action<OutpostEvent> onEvent = e => events.Add(e);
            Game.EventPresented += onEvent;
            try
            {
                Game.GrantTestCards();
                yield return WaitFor(() => Factory(id).Automated);
                yield return Advance(0.5);
            }
            finally
            {
                Game.EventPresented -= onEvent;
            }
            Assert.That(Game.State.Profile.CardLevel(req.RequiredCardId), Is.GreaterThanOrEqualTo(req.RequiredCardLevel));
            Assert.That(Factory(id).Automated, Is.True);
            fv = Game.Board.FactoryViewOf(id);
            Assert.That(fv.AutoBadgeVisible, Is.True, "значок AUTO у полосы");
            if (events.Any(e => e.Type == OutpostEventType.FactoryAutomated))
                Assert.That(ToastText(), Is.EqualTo(Loc.F("factories.toast.automated", FactoryText.FactoryName("milk"))));

            // Автоматический завод: партии уходят сами, кнопки «Deliver» нет.
            Assert.That(Game.TryGetTargetWorldPosition(id, out var center), Is.True);
            yield return FocusOn(center);
            yield return Advance(9);
            Assert.That(Overlay.DeliverButtonOf(id), Is.Null);
            Assert.That(Factory(id).Dispatched, Is.GreaterThan(0), "партия ушла в трубу без нажатия");
        }

        [UnityTest]
        public IEnumerator MineComplete_FactoriesStop_NoBarAnimationOrButton()
        {
            yield return LoadMine(2);
            int id = NearestFactory();
            Game.DebugUnlockFactories();
            yield return WaitFor(() => Factory(id).Status != FactoryStatus.Locked);
            yield return Advance(1);
            Assert.That(Game.Board.FactoryViewOf(id).WorkAnimating, Is.True);
            Game.CompleteStageNow();
            for (int frame = 0; frame < 3000 && Game.State.Run.Phase != RunPhase.ReadyToContinue; frame++) yield return null;
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
            yield return WaitPlaying();
            yield return null;
            var fv = Game.Board.FactoryViewOf(id);
            Assert.That(Factory(id).Status, Is.EqualTo(FactoryStatus.Stopped));
            Assert.That(fv.Status, Is.EqualTo(FactoryStatus.Stopped));
            Assert.That(fv.WorkAnimating || fv.BarVisible || fv.AutoBadgeVisible, Is.False, "Stopped: без анимации, полосы и AUTO");
            Assert.That(Overlay.VisibleButtons, Is.EqualTo(0));
            Assert.That(Game.Board.Pipes.ActiveCapsules, Is.EqualTo(0), "в завершённой шахте партии не движутся");
            Assert.That(Game.Board.Pipes.IsShown(id), Is.True, "труба остаётся");
        }

        [UnityTest]
        public IEnumerator Taps_LockedStrikes_ReadyDelivers_WarehouseHasPriorityOverBase()
        {
            yield return LoadMine(2);
            int id = NearestFactory();
            Assert.That(Game.TryGetTargetWorldPosition(id, out var center), Is.True);
            yield return FocusOn(center);
            var target = new OutpostSelection { Kind = OutpostSelectionKind.Target, Id = id };
            if (Game.CanStrike(id))
            {
                Game.Input.Tap(target, center);
                Assert.That(Game.Board.ActiveStrikes, Is.GreaterThan(0), "тап по закрытому заводу — удар по цепям");
                yield return PlayModeUtil.WaitReal(0.6f);
            }

            Game.DebugUnlockFactories();
            yield return WaitFor(() => Factory(id).Status != FactoryStatus.Locked);
            var f = Factory(id);
            yield return Advance((f.CycleEnd - Game.Sim.Now) / 1e6 + 0.3);
            Assert.That(Factory(id).Status, Is.EqualTo(FactoryStatus.Ready));
            Game.Input.Tap(target, center);
            yield return null;
            Assert.That(Game.State.Run.PipeBatches.Count, Is.EqualTo(1), "тап по готовому заводу — доставка");
            Game.Input.Tap(target, center);
            yield return null;
            Assert.That(Game.Selection.Kind == OutpostSelectionKind.Target && Game.Selection.Id == id, Is.True, "тап по работающему заводу — сведения");
            Game.ClearSelection();

            // Склад во дворе: свой коллайдер с приоритетом выше «Базы»; тап — выбор склада (HUD открывает рынок).
            var camp = Game.Board.Camp;
            Assert.That(camp.HasWarehouse, Is.True);
            var at = Game.Board.WarehouseCenterWorldPosition;
            Assert.That(OutpostPickable.TryPickBest(new Ray(at + Vector3.up * 50f, Vector3.down), out var picked), Is.True);
            Assert.That(picked.Kind, Is.EqualTo(OutpostPickKind.Warehouse));
            Assert.That(picked.ToSelection().Kind, Is.EqualTo(OutpostSelectionKind.Warehouse));
            var basePoint = Game.Board.BranchWorldPosition(UpgradeBranch.Team);
            Assert.That(OutpostPickable.TryPickBest(new Ray(basePoint + Vector3.up * 50f, Vector3.down), out var other), Is.True);
            Assert.That(other.Kind, Is.EqualTo(OutpostPickKind.Base), "остальные постройки двора — «База»");
            Assert.That(Game.Board.TryPickAtWorld(at, out var fallback) && fallback.Kind == OutpostSelectionKind.Warehouse, Is.True, "запасной выбор по клетке");
            Game.Input.Tap(picked.ToSelection(), at);
            yield return null;
            Assert.That(Game.Hud.MarketOpen, Is.True, "тап по складу — рынок");
            yield return CloseWindows();
        }

        [UnityTest]
        public IEnumerator EdgeArrow_PointsToReadyFactoryOffscreen_TapFocusesCamera()
        {
            yield return LoadMine(2);
            int id = Game.Map.Factories.OrderByDescending(f => f.RouteSteps).First().TargetId;
            Game.DebugUnlockFactories();
            yield return WaitFor(() => Factory(id).Status != FactoryStatus.Locked);
            var f = Factory(id);
            yield return Advance((f.CycleEnd - Game.Sim.Now) / 1e6 + 0.3);
            Assert.That(Factory(id).Status, Is.EqualTo(FactoryStatus.Ready));
            Assert.That(Game.TryGetTargetWorldPosition(id, out var center), Is.True);
            // Камера в дальний от завода конец поля.
            float far = Mathf.Abs(Game.Rig.MinZ - center.z) > Mathf.Abs(Game.Rig.MaxZ - center.z) ? Game.Rig.MinZ : Game.Rig.MaxZ;
            Game.Rig.FocusOn(new Vector2(center.x, far));
            yield return null;
            yield return null;
            if (Overlay.DeliverButtonOf(id) != null) Assert.Ignore("Завод шахты 2 виден из любой точки прокрутки — стрелке некуда указывать.");
            int side = Overlay.ArrowSideOf(id);
            Assert.That(side, Is.GreaterThanOrEqualTo(0), "стрелка у края к готовому заводу");
            Assert.That(side, Is.EqualTo(far < center.z ? 0 : 1), "стрелка с той стороны, где завод");
            Overlay.ClickArrow(side);
            yield return PlayModeUtil.WaitReal(1f);
            Assert.That(Overlay.DeliverButtonOf(id), Is.Not.Null, "камера подошла к заводу — кнопка на экране");
            Assert.That(Overlay.ArrowSideOf(id), Is.EqualTo(-1));
        }

        [UnityTest]
        public IEnumerator InfoPanelOpen_DeliverButtonsAndArrowsStayAboveIt()
        {
            // Запрос WP5b, ворота G3 (ТЗ 2.2 §8): при открытых сведениях завода стрелки у края и кнопки «Deliver» других готовых
            // заводов не ложатся на панель сведений и её кнопку «Deliver» — нижняя граница полосы поля поднимается до панели.
            yield return LoadMine(140);
            Game.DebugUnlockFactories();
            yield return WaitFor(() => Game.State.Run.Factories.All(x => x.Status != FactoryStatus.Locked));
            yield return CloseWindows();
            Game.DebugFinishFactoryCycles();
            yield return WaitFor(() => Game.State.Run.Factories.Count(x => x.Status == FactoryStatus.Ready && !x.Automated) > 1);
            yield return CloseWindows();
            int id = Game.Map.Factories.OrderByDescending(f => f.RouteSteps).First().TargetId;
            Assert.That(Factory(id).Status, Is.EqualTo(FactoryStatus.Ready));
            Game.SelectTarget(id);
            Assert.That(Game.TryGetFactoryWorldPosition(id, out var at), Is.True);
            yield return FocusOn(at);
            yield return PlayModeUtil.WaitReal(0.6f);
            Game.Hud.RefreshNow();
            yield return null;
            yield return null;
            Assert.That(Game.Hud.TryGetInfoTop(out float infoTop), Is.True, "сведения завода открыты");
            int shown = 0;
            var corners = new Vector3[4];
            foreach (Transform child in Overlay.transform)
            {
                var rt = (RectTransform)child;
                bool deliver = rt.name.StartsWith("deliver", StringComparison.Ordinal);
                if (!rt.gameObject.activeSelf || !(deliver || rt.name.StartsWith("factoryArrow", StringComparison.Ordinal))) continue;
                shown++;
                // Overlay-канвас: мировые углы — пиксели экрана. У стрелки проверяется и знак направления (ребёнок «arrow»).
                foreach (var part in deliver ? new[] { rt } : new[] { rt, (RectTransform)rt.Find("arrow") })
                {
                    part.GetWorldCorners(corners);
                    Assert.That(corners[0].y, Is.GreaterThanOrEqualTo(infoTop - 1f), part.name + " (" + rt.name + ") лежит на панели сведений");
                }
            }
            Assert.That(shown, Is.GreaterThan(0), "на экране есть кнопки «Deliver» или стрелки к другим готовым заводам");
        }

        /// <summary>
        /// Поле и HUD рисуются в портретную текстуру 1080×<paramref name="height"/>, как на телефоне: экран партии тестов — 640×480
        /// (так же снимает генератор OutpostFactoryScreensEvidence; масштаб холста — как у CanvasScaler). Вернуть — <see cref="EndPortrait"/>.
        /// </summary>
        RenderTexture BeginPortrait(int height)
        {
            var camera = Game.Rig.Cam;
            var canvas = Game.Hud.GetComponent<Canvas>();
            var texture = new RenderTexture(1080, height, 24);
            CameraRig.OverrideAspect = 1080f / height;
            camera.targetTexture = texture;
            Game.Hud.GetComponent<CanvasScaler>().enabled = false;
            canvas.scaleFactor = Mathf.Sqrt(height / 1920f);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            return texture;
        }

        void EndPortrait(RenderTexture texture)
        {
            var canvas = Game.Hud.GetComponent<Canvas>();
            Game.Rig.Cam.targetTexture = null;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            Game.Hud.GetComponent<CanvasScaler>().enabled = true;
            CameraRig.OverrideAspect = 9f / 16;
            if (texture == null) return;
            texture.Release();
            UnityEngine.Object.Destroy(texture);
        }

        /// <summary>Шахта 140: все цепи разбиты, циклы закончены — ручные заводы в Ready.</summary>
        IEnumerator Mine140AllReady()
        {
            yield return LoadMine(140);
            Game.DebugUnlockFactories();
            yield return WaitFor(() => Game.State.Run.Factories.All(x => x.Status != FactoryStatus.Locked));
            yield return CloseWindows();
            Game.DebugFinishFactoryCycles();
            yield return WaitFor(() => Game.State.Run.Factories.Count(x => x.Status == FactoryStatus.Ready && !x.Automated) > 2);
            yield return CloseWindows();
            Game.ClearSelection();
        }

        [UnityTest]
        public IEnumerator DeliverButtons_AdjacentReadyFactories_DoNotOverlap()
        {
            // Ворота G3 (ТЗ 2.2 §8, снимки шахт 30, 80, 140, 144 в 9:16 и 9:20): здание завода — два столбца, а кнопка «Deliver ×N»
            // шире, и у трёх готовых заводов подряд средняя кнопка пряталась под соседней, а стрелка у края ложилась на кнопку.
            // Правка теста — решение владельца 15.10.2026 «сокращать в тесноте» (кнопки «Deliver»): вместо «ёлочки» на 2–3 ряда
            // кнопки соседей сокращаются до иконки и «×N» и стоят над своим заводом — не выше одного ряда клеток над ним, без
            // наложений; стрелка у края не ложится ни на кнопки, ни на чужие заводы (полосу цикла, колесо, здание).
            // Прежние проверки «кнопки соседей пересекаются по ширине» (признак «ёлочки») больше нет.
            yield return Mine140AllReady();
            var at = new List<Vector3>();
            foreach (var mf in Game.Map.Factories)
                if (Game.TryGetFactoryWorldPosition(mf.TargetId, out var p)) at.Add(p);
            // Завод с наибольшим числом соседей в том же ряду (не дальше трёх столбцов).
            var row = at.OrderByDescending(a => at.Count(b => Mathf.Abs(b.z - a.z) < 0.6f && Mathf.Abs(b.x - a.x) < 3.5f)).First();
            Assert.That(at.Count(b => Mathf.Abs(b.z - row.z) < 0.6f && Mathf.Abs(b.x - row.x) < 3.5f), Is.GreaterThan(1), "в шахте 140 есть соседние заводы");

            DeliverLayout layout;
            RenderTexture texture = null;
            try
            {
                texture = BeginPortrait(1920);
                yield return FocusOn(row);
                yield return PlayModeUtil.WaitReal(0.6f);
                yield return null;
                layout = DeliverLayout.Measure(Game, "9:16, ряд заводов");
            }
            finally
            {
                EndPortrait(texture);
            }
            Assert.That(layout.Buttons.Count, Is.GreaterThan(1), "на экране кнопки соседних готовых заводов");
            Assert.That(layout.Problems, Is.Empty, string.Join("\n", layout.Problems));
            Assert.That(Overlay.DroppedButtons, Is.Zero, "каждый готовый завод на экране с кнопкой");
            Assert.That(layout.Short, Is.GreaterThanOrEqualTo(2), "кнопки соседних готовых заводов — короткие «иконка ×N»");
            foreach (var b in layout.Buttons)
            {
                int units = Factory(b.Id).ReadyUnits;
                Assert.That(b.Label, Is.EqualTo(b.Short ? Loc.F("factories.deliver_count", units) : Loc.F("factories.deliver", units)), "подпись кнопки " + b.Id);
            }
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator DeliverButtons_TopEdgeAndCameraSweep_StayOverOwnFactory()
        {
            // Решение владельца 15.10.2026 «сокращать в тесноте» (ТЗ 2.2 §8): прежняя «ёлочка» у верхнего края полосы поля
            // прижимала кнопку поверх соседней. Камера проходит все камеры заводов шахты 140 (15 готовых заводов, карта 51 столбец):
            // по ширине — против каждого завода и со сдвигом на 4,5 клетки (завод у бокового края), по высоте — с шагом в полклетки
            // от «завод за верхним краем» до «завод за нижним», в 9:16 и 9:20. На каждом кадре кнопки не накладываются, стоят над
            // своим зданием не выше одного ряда клеток (у верхнего края — ниже, на своём здании); стрелки у края не ложатся на
            // кнопки и чужие заводы.
            yield return Mine140AllReady();
            var rig = Game.Rig;
            var centers = new List<Vector3>();
            foreach (var mf in Game.Map.Factories)
                if (Game.TryGetFactoryWorldPosition(mf.TargetId, out var p)) centers.Add(p);
            var candidates = centers.SelectMany(c => new[] { c.x - 4.5f, c.x, c.x + 4.5f })
                .Select(x => Mathf.Round(Mathf.Clamp(x, rig.MinX, rig.MaxX) * 2f) / 2f).Distinct().OrderBy(x => x);
            // Не чаще чем через полторы клетки (у четырёх заводов подряд центры через две).
            var xs = new List<float>();
            foreach (float x in candidates)
                if (xs.Count == 0 || x - xs[xs.Count - 1] >= 1.5f) xs.Add(x);
            var problems = new List<string>();
            int frames = 0, withButtons = 0, topEdge = 0, shortButtons = 0, fullButtons = 0, dropped = 0, arrows = 0, blocked = 0;
            foreach (int height in new[] { 1920, 2400 })
            {
                RenderTexture texture = null;
                try
                {
                    texture = BeginPortrait(height);
                    yield return null;
                    // По высоте — вокруг заводов, видимых при этом x (поле — 10 столбцов): от «завод за верхним краем» до «за нижним».
                    float reach = rig.VisibleGroundDepth() * 0.5f + 2f;
                    foreach (float x in xs)
                    {
                        var near = centers.Where(c => Mathf.Abs(c.x - x) <= 6.5f).ToList();
                        if (near.Count == 0) continue;
                        float z0 = Mathf.Max(rig.MinZ, near.Min(c => c.z) - reach), z1 = Mathf.Min(rig.MaxZ, near.Max(c => c.z) + reach);
                        for (float z = z0; z <= z1 + 0.01f; z += 0.5f)
                        {
                            // Камера ставится сразу; раскладку пересчитывает LateUpdate следующего кадра.
                            rig.FocusOn(new Vector2(x, z));
                            yield return null;
                            var layout = DeliverLayout.Measure(Game, (height == 1920 ? "9:16" : "9:20") + ", камера " + x.ToString("0.#") + "/" + z.ToString("0.#"));
                            frames++;
                            if (layout.Buttons.Count > 0) withButtons++;
                            topEdge += layout.AtTopEdge;
                            shortButtons += layout.Short;
                            fullButtons += layout.Buttons.Count - layout.Short;
                            dropped += Overlay.DroppedButtons;
                            arrows += layout.Arrows.Count;
                            blocked += layout.ArrowBlocked;
                            if (problems.Count < 40) problems.AddRange(layout.Problems);
                        }
                    }
                }
                finally
                {
                    EndPortrait(texture);
                }
            }
            Debug.Log("[FactoryView] camera sweep: frames " + frames + ", with buttons " + withButtons + ", at top edge " + topEdge + ", short " + shortButtons +
                      ", full " + fullButtons + ", dropped " + dropped + ", arrows " + arrows + ", arrows without free spot " + blocked);
            Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(40)));
            Assert.That(withButtons, Is.GreaterThan(0), "камера прошла готовые заводы");
            Assert.That(topEdge, Is.GreaterThan(0), "был кадр с готовым заводом у верхнего края полосы поля (кнопка ниже верха полосы)");
            Assert.That(shortButtons, Is.GreaterThan(0), "соседние готовые заводы — короткие кнопки");
            Assert.That(fullButtons, Is.GreaterThan(0), "одиночный готовый завод — полная кнопка «Deliver ×N»");
            Assert.That(dropped, Is.Zero, "кнопке каждого готового завода на экране нашлось место");
            Assert.That(arrows, Is.GreaterThan(0), "были стрелки к готовым заводам за краем");
        }

        [UnityTest]
        public IEnumerator Mine140_AllFactoriesAutomated_PipesCapsulesAndNoFrameAllocations()
        {
            yield return LoadMine(140);
            Assert.That(Game.Map.Factories.Length, Is.EqualTo(15));
            Game.DebugUnlockFactories();
            yield return WaitFor(() => Game.State.Run.Factories.All(x => x.Status != FactoryStatus.Locked));
            Game.DebugAutomateAll();
            yield return WaitFor(() => Game.State.Run.Factories.All(x => x.Automated));
            // Все циклы заканчиваются сейчас: 15 партий уходят в трубы.
            Game.DebugFinishFactoryCycles();
            yield return WaitFor(() => Game.State.Run.PipeBatches.Count > 0);
            yield return Advance(1);
            var pipes = Game.Board.Pipes;
            Assert.That(Game.Map.Factories.All(mf => pipes.IsShown(mf.TargetId)), Is.True, "трубы всех 15 заводов");
            Assert.That(pipes.Layout.Bands.Count, Is.GreaterThan(0), "15 труб в проходе — жгут");
            Assert.That(pipes.Layout.Couplings.Count, Is.GreaterThan(0), "муфты на входе в жгут");
            foreach (var p in pipes.Layout.Pipes)
            {
                Assert.That(p.Segments.Count, Is.GreaterThan(0), "труба " + p.TargetId);
                foreach (var s in p.Segments) Assert.That(s.A.x == s.B.x || s.A.y == s.B.y, Is.True, "отрезки трубы по осям");
            }
            Assert.That(Game.Board.FactoryViews.All(v => v.AutoBadgeVisible), Is.True);
            Assert.That(pipes.ActiveCapsules, Is.InRange(1, PipeNetworkView.CapsulePool));
            foreach (var mf in Game.Map.Factories) Assert.That(pipes.VisibleCapsules(mf.TargetId), Is.LessThanOrEqualTo(PipeNetworkView.CapsulesPerPipe));

            // Обычный кадр вида без выделений памяти (ТЗ 2.2 §15): прогрев, проба счётчика кучи, затем 2000 синхронизаций — даже
            // 8 байт на вызов дали бы 16 КБ роста. Счётчик потока (GetAllocatedBytesForCurrentThread) в Mono Unity не работает.
            var sim = Game.Sim;
            for (int i = 0; i < 10; i++) Game.Board.Sync(sim, 0.016f);
            long probe = GC.GetTotalMemory(false);
            var junk = new byte[64 * 1024];
            bool counter = GC.GetTotalMemory(false) - probe >= 32 * 1024 && junk.Length > 0;
            if (!counter) Assert.Ignore("Счётчик кучи недоступен в этой среде.");
            long before = GC.GetTotalMemory(false);
            for (int i = 0; i < 2000; i++) Game.Board.Sync(sim, 0.016f);
            long grown = GC.GetTotalMemory(false) - before;
            Assert.That(grown, Is.LessThan(8 * 1024), "OutpostBoardView.Sync выделяет память: +" + grown + " байт за 2000 вызовов");
        }
    }

    /// <summary>
    /// Раскладка кнопок «Deliver» и стрелок у края в пикселях экрана — проверка решения владельца 15.10.2026 «сокращать в
    /// тесноте» (OutpostFactoryViewTests и layout.txt генератора OutpostFactoryScreensEvidence). Холст — Overlay или
    /// ScreenSpaceCamera с камерой поля (портретная текстура): углы UI переводятся в пиксели той же камеры, что и заводы.
    /// Завод — от ног footprint до верха полосы цикла (<see cref="OutpostBoardView.TryGetFactoryWorldBounds"/>).
    /// </summary>
    internal sealed class DeliverLayout
    {
        public struct Item
        {
            public int Id;
            public Rect Rect, Body;
            public bool Short;
            /// <summary>Ряд клеток на экране, пиксели.</summary>
            public float RowPx;
            public string Label;
        }

        public struct EdgeArrow
        {
            public int Side, Target;
            public Rect Rect;
            public bool Blocked;
        }

        public readonly List<Item> Buttons = new List<Item>();
        public readonly List<EdgeArrow> Arrows = new List<EdgeArrow>();
        public readonly List<KeyValuePair<int, Rect>> Bodies = new List<KeyValuePair<int, Rect>>();
        /// <summary>Нарушения правила (пустой — раскладка верна); стрелки без свободного места (<see cref="FactoryOverlay.ArrowBlocked"/>) сюда не входят.</summary>
        public readonly List<string> Problems = new List<string>();
        /// <summary>
        /// Пары кнопок внахлёст; кнопки выше одного ряда клеток над своим заводом; кнопки не над своим зданием; кнопки у верхнего края
        /// полосы поля (низ ниже верха полосы своего завода); стрелки на кнопках и на чужих заводах (все, в том числе без места);
        /// стрелки без свободного места.
        /// </summary>
        public int Overlaps, TooHigh, NotOverOwn, AtTopEdge, ArrowOverButton, ArrowOverFactory, ArrowBlocked;
        public int Short => Buttons.Count(b => b.Short);

        static Rect ScreenRect(RectTransform rt, Camera uiCam, Vector3[] corners)
        {
            rt.GetWorldCorners(corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(uiCam, corners[0]), b = RectTransformUtility.WorldToScreenPoint(uiCam, corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static Rect Union(Rect a, Rect b) =>
            Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        static string R(Rect r) => "(" + r.xMin.ToString("0") + "…" + r.xMax.ToString("0") + ", " + r.yMin.ToString("0") + "…" + r.yMax.ToString("0") + ")";

        public static DeliverLayout Measure(OutpostController game, string context)
        {
            var layout = new DeliverLayout();
            var overlay = game.Input.Factories;
            var canvas = game.Hud.GetComponent<Canvas>();
            var uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var cam = game.Rig.Cam;
            var corners = new Vector3[4];
            foreach (var mf in game.Map.Factories)
            {
                if (!game.Board.TryGetFactoryWorldBounds(mf.TargetId, out var low, out var high)) continue;
                Vector3 a = cam.WorldToScreenPoint(low), b = cam.WorldToScreenPoint(high);
                var body = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                layout.Bodies.Add(new KeyValuePair<int, Rect>(mf.TargetId, body));
                var button = overlay.DeliverButtonOf(mf.TargetId);
                if (button == null) continue;
                layout.Buttons.Add(new Item
                {
                    Id = mf.TargetId, Rect = ScreenRect((RectTransform)button.transform, uiCam, corners), Body = body,
                    Short = overlay.DeliverShortOf(mf.TargetId), Label = overlay.DeliverLabelOf(mf.TargetId),
                    RowPx = Mathf.Abs(cam.WorldToScreenPoint(high + Vector3.forward).y - b.y),
                });
            }
            for (int side = 0; side < 4; side++)
            {
                var rt = overlay.transform.Find("factoryArrow" + side) as RectTransform;
                if (rt == null || !rt.gameObject.activeSelf) continue;
                int s = side;
                int target = game.Map.Factories.Select(m => m.TargetId).Where(id => overlay.ArrowSideOf(id) == s).DefaultIfEmpty(-1).First();
                var rect = Union(ScreenRect(rt, uiCam, corners), ScreenRect((RectTransform)rt.Find("arrow"), uiCam, corners));
                layout.Arrows.Add(new EdgeArrow { Side = side, Target = target, Rect = rect, Blocked = overlay.ArrowBlocked(side) });
            }

            var items = layout.Buttons;
            for (int i = 0; i < items.Count; i++)
            {
                var b = items[i];
                if (b.Rect.yMax > b.Body.yMax + b.RowPx + 1f)
                {
                    layout.TooHigh++;
                    layout.Problems.Add(context + ": кнопка " + b.Id + " " + R(b.Rect) + " выше ряда над заводом " + R(b.Body) + ", ряд " + b.RowPx.ToString("0"));
                }
                if (b.Rect.xMax <= b.Body.xMin || b.Rect.xMin >= b.Body.xMax)
                {
                    layout.NotOverOwn++;
                    layout.Problems.Add(context + ": кнопка " + b.Id + " " + R(b.Rect) + " не над своим заводом " + R(b.Body));
                }
                if (b.Rect.yMin < b.Body.yMax - 1f) layout.AtTopEdge++;
                for (int j = i + 1; j < items.Count; j++)
                    if (b.Rect.Overlaps(items[j].Rect))
                    {
                        layout.Overlaps++;
                        layout.Problems.Add(context + ": кнопки " + b.Id + " " + R(b.Rect) + " и " + items[j].Id + " " + R(items[j].Rect) + " накладываются");
                    }
            }
            foreach (var a in layout.Arrows)
            {
                if (a.Blocked) layout.ArrowBlocked++;
                foreach (var b in items)
                    if (a.Rect.Overlaps(b.Rect))
                    {
                        layout.ArrowOverButton++;
                        if (!a.Blocked) layout.Problems.Add(context + ": стрелка " + a.Side + " " + R(a.Rect) + " на кнопке " + b.Id + " " + R(b.Rect));
                    }
                foreach (var body in layout.Bodies)
                    if (body.Key != a.Target && a.Rect.Overlaps(body.Value))
                    {
                        layout.ArrowOverFactory++;
                        if (!a.Blocked) layout.Problems.Add(context + ": стрелка " + a.Side + " " + R(a.Rect) + " на заводе " + body.Key + " " + R(body.Value));
                    }
            }
            return layout;
        }
    }

    /// <summary>
    /// Снимки вида заводов для проверки глазами (WP5a): шахта 2 (закрытый завод, Ready с кнопкой «Deliver», капсула в трубе, склад)
    /// и шахта 140 (15 заводов на автомате, жгут, обзор всей карты). Docs/Reports/factories/view-wp5a/*.png.
    /// Отдельно: <c>Tools/unity.ps1 -Mode PlayMode -Target Ci -TestFilter OutpostFactoryViewEvidence</c>.
    /// </summary>
    [Explicit("генератор снимков Docs/Reports/factories/view-wp5a/*.png")]
    public sealed class OutpostFactoryViewEvidence : OutpostCardsFixture
    {
        static string Shots => Path.Combine(PlayModeUtil.ReportsDir(), "factories", "view-wp5a");

        IEnumerator Advance(double seconds)
        {
            while (seconds > 0)
            {
                double step = Math.Min(0.25, seconds);
                Clock.Advance(step);
                seconds -= step;
                yield return null;
            }
        }

        IEnumerator Capture(string name, int height = 1920, bool settle = true)
        {
            var camera = Game.Rig.Cam;
            var canvas = Game.Hud.GetComponent<Canvas>();
            var scaler = Game.Hud.GetComponent<CanvasScaler>();
            var texture = new RenderTexture(1080, height, 24) { filterMode = FilterMode.Point };
            var previous = camera.targetTexture;
            float previousScale = canvas.scaleFactor;
            try
            {
                camera.targetTexture = texture;
                scaler.enabled = false;
                canvas.scaleFactor = Mathf.Sqrt(height / 1920f);
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                if (settle) yield return PlayModeUtil.WaitReal(0.35f);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                Save(texture, name, 1080, height);
            }
            finally
            {
                camera.targetTexture = previous;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                canvas.scaleFactor = previousScale;
                scaler.enabled = true;
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }
        }

        /// <summary>Вся карта сверху без HUD: 2 пикселя снимка на пиксель арта.</summary>
        void CaptureOverview(string name)
        {
            var camera = Game.Rig.Cam;
            var board = Game.Board;
            float w = board.Width + 1f, h = board.CameraDepth + 1f;
            int pw = Mathf.RoundToInt(w * 64f), ph = Mathf.Min(8192, Mathf.RoundToInt(h * 64f));
            var texture = new RenderTexture(pw, ph, 24) { filterMode = FilterMode.Point };
            var previous = camera.targetTexture;
            var pos = camera.transform.position;
            var rot = camera.transform.rotation;
            float size = camera.orthographicSize;
            try
            {
                camera.targetTexture = texture;
                camera.aspect = pw / (float)ph;
                camera.orthographicSize = ph / 64f / 2f;
                camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                camera.transform.position = new Vector3(board.Width / 2f, 60f, board.TopZ - ph / 64f / 2f + 0.5f);
                camera.Render();
                Save(texture, name, pw, ph);
            }
            finally
            {
                camera.targetTexture = previous;
                camera.ResetAspect();
                camera.orthographicSize = size;
                camera.transform.SetPositionAndRotation(pos, rot);
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }
        }

        static void Save(RenderTexture texture, string name, int w, int h)
        {
            var active = RenderTexture.active;
            RenderTexture.active = texture;
            var pixels = new Texture2D(w, h, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            pixels.Apply();
            RenderTexture.active = active;
            Directory.CreateDirectory(Shots);
            File.WriteAllBytes(Path.Combine(Shots, name), pixels.EncodeToPNG());
            UnityEngine.Object.Destroy(pixels);
        }

        IEnumerator Focus(Vector3 world, float dz = 0f)
        {
            Game.Rig.FocusOn(new Vector2(world.x, world.z + dz));
            yield return null;
            yield return null;
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator FactoryScreens()
        {
            Directory.CreateDirectory(Shots);
            foreach (string file in Directory.GetFiles(Shots, "*.png")) File.Delete(file);
            Game.SwitchProfile(true);
            yield return WaitPlaying();

            // Шахта 2: закрытый завод, трещины цепей, открытие вживую, Ready с кнопкой, стрелка, капсула и склад.
            Game.LoadZone(2);
            yield return WaitPlaying();
            for (int i = 0; i < 300 && Game.State.Run.Zone != 2; i++) yield return null;
            int id = Game.Map.Factories[0].TargetId;
            Assert.That(Game.TryGetTargetWorldPosition(id, out var center), Is.True);
            yield return Focus(center, -1f);
            Game.SelectTarget(id);
            yield return Advance(0.5);
            yield return Capture("m002-1-locked.png");
            Game.ClearSelection();

            bool unlocked = false;
            Action<OutpostEvent> onEvent = e => unlocked |= e.Type == OutpostEventType.FactoryUnlocked && e.TargetId == id;
            Game.EventPresented += onEvent;
            bool cracked = false;
            Game.SimSpeed = 20;
            for (int frame = 0; frame < 4000 && !unlocked; frame++)
            {
                for (int w = 0; w < 10 && Game.Hud.AnyWindowOpen; w++)
                {
                    Game.Hud.HandleBack();
                    yield return null;
                }
                var ts = Game.Sim.TargetById(id);
                if (!cracked && !ts.Destroyed && ts.RemainingHpUnits * 10 < ts.MaxHpUnits * 4)
                {
                    cracked = true;
                    Game.SimSpeed = 1;
                    Game.SelectTarget(id);
                    yield return Focus(center, -1f);
                    yield return Capture("m002-1b-cracked.png");
                    Game.ClearSelection();
                    Game.SimSpeed = 20;
                }
                Clock.Advance(0.25);
                yield return null;
            }
            Game.SimSpeed = 1;
            Game.EventPresented -= onEvent;
            Game.Rig.FocusOn(new Vector2(center.x, center.z - 1f));
            yield return Capture("m002-1c-unlock.png", 1920, false);
            for (int w = 0; w < 10 && Game.Hud.AnyWindowOpen; w++)
            {
                Game.Hud.HandleBack();
                yield return null;
            }
            yield return Advance(3);
            yield return Focus(center, -1f);
            yield return Capture("m002-2-working.png");
            var f = Game.FactoryOf(id);
            yield return Advance((f.CycleEnd - Game.Sim.Now) / 1e6 + 0.3);
            Game.FocusBase();
            yield return PlayModeUtil.WaitReal(0.8f);
            yield return Capture("m002-3b-arrow.png");
            yield return Focus(center, -1f);
            yield return Capture("m002-3-ready.png");
            yield return Capture("m002-3-ready-2400.png", 2400);
            Game.Input.Factories.ClickDeliver(id);
            yield return Advance(1);
            Game.FocusBase();
            yield return PlayModeUtil.WaitReal(0.8f);
            yield return Capture("m002-4-capsule-base.png");
            yield return Advance(Game.Sim.FactoryTransitTicks(id) / 1e6);
            yield return Capture("m002-5-arrived.png");
            CaptureOverview("m002-overview.png");

            // Шахта 140: 15 заводов, все открыты и на автомате.
            Game.LoadZone(140);
            yield return WaitPlaying();
            for (int i = 0; i < 300 && Game.State.Run.Zone != 140; i++) yield return null;
            Game.DebugUnlockFactories();
            for (int i = 0; i < 300 && Game.State.Run.Factories.Any(x => x.Status == FactoryStatus.Locked); i++) yield return null;
            Game.DebugAutomateAll();
            for (int i = 0; i < 300 && Game.State.Run.Factories.Any(x => !x.Automated); i++) yield return null;
            Game.DebugFinishFactoryCycles();
            yield return Advance(1.5);
            Game.FocusBase();
            yield return PlayModeUtil.WaitReal(0.8f);
            yield return Capture("m140-1-base.png");
            yield return Capture("m140-1-base-2400.png", 2400);
            var mid = Game.Map.Factories.OrderBy(m => m.RouteSteps).ElementAt(7).TargetId;
            Assert.That(Game.TryGetTargetWorldPosition(mid, out var midAt), Is.True);
            yield return Focus(midAt);
            yield return Capture("m140-2-mid.png");
            CaptureOverview("m140-overview.png");
            var layout = Game.Board.Pipes.Layout;
            File.WriteAllText(Path.Combine(Shots, "m140-layout.txt"),
                "pipes " + layout.Pipes.Count + ", max per edge " + layout.MaxShared + ", bands " + layout.Bands.Count + ", couplings " + layout.Couplings.Count + "\n");
        }
    }
}
