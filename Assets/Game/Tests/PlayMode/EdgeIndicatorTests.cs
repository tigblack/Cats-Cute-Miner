using System.Collections;
using System.Collections.Generic;
using CatMiners.Core;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using CatMiners.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace CatMiners.Tests
{
    /// <summary>
    /// Стрелки у краёв экрана (ТЗ 13.1) и связанные элементы HUD: стрелка ведёт к расчищенной шахте вне кадра,
    /// тап переводит к ней камеру; под окном и экраном конца кампании стрелок нет; подписи бонусов ворот по ТЗ 11.1,
    /// кнопка найма, «Забрать» в панели сундука, экран конца кампании.
    /// </summary>
    public class EdgeIndicatorTests
    {
        /// <summary>
        /// Глубокая зона с шахтами в нижнем (открытом) секторе: в zone2 (13 строк) всё поле помещается в кадр
        /// и шахта не может оказаться за краем, поэтому берётся zone10 (38 строк, шахты нижнего сектора в строках 28–29).
        /// </summary>
        const int Zone = 10;

        string m_SaveDir;
        LogWatcher m_Log;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_SaveDir = PlayModeUtil.UseTempSaveDir();
            CameraRig.OverrideAspect = 1080f / 1920f;
            m_Log = new LogWatcher();
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            m_Log.Dispose();
            GameController.ClockOverride = null;
            CameraRig.OverrideAspect = 0f;
            PlayModeUtil.DropTempSaveDir(m_SaveDir);
            yield break;
        }

        /// <summary>Точка мира внутри видимой полосы поля: доли safe area между панелями HUD.</summary>
        static bool InsideBand(GameController gc, Vector3 world)
        {
            var p = gc.Rig.Cam.WorldToScreenPoint(world);
            var safe = Screen.safeArea;
            float bottom = safe.yMin + gc.Hud.BottomInset * safe.height;
            float top = safe.yMin + (1f - gc.Hud.TopInset) * safe.height;
            return p.y >= bottom && p.y <= top;
        }

        [UnityTest]
        public IEnumerator ArrowLeadsToOffscreenReadyShaft()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            gc.SwitchSlot(SaveSlots.Debug, true, Zone);
            yield return null;
            yield return null;
            var s = gc.Session;
            Assert.AreEqual(Zone, s.Mine.Zone);

            // Самая нижняя шахта открытого сектора.
            int shaft = -1;
            foreach (var sh in s.Layout.Shafts)
                if (s.IsCellOpen(sh.Cells[0]) && (shaft < 0 || sh.Row > s.Layout.Objects[shaft].Row)) shaft = sh.Ordinal;
            Assert.GreaterOrEqual(shaft, 0, "в открытом секторе есть шахта");
            s.DebugCompleteTarget(shaft);
            Assert.AreEqual(ObjectStatus.ReadyToActivate, s.Mine.Objects[shaft].Status);

            var edges = gc.Hud.Edges;
            Assert.IsNotNull(edges, "HUD создал стрелки у краёв");
            var center = gc.Board.ObjectCenter(shaft);

            // Камера у верха карты, далеко от шахты.
            gc.ClearSelection();
            gc.Rig.FocusOn(s.Layout.Depth);
            yield return PlayModeUtil.WaitReal(0.6f);
            Assert.IsFalse(InsideBand(gc, center), "шахта вне кадра");
            Assert.Greater(edges.ActiveArrowCount, 0, "к расчищенной шахте вне кадра ведёт стрелка");

            // Под окном стрелок нет: сброс у края не должен попадать в UI.
            gc.Hud.Modal("Проверка", "Окно поверх поля", new Hud.ModalButton { Label = "Закрыть" });
            yield return null;
            yield return null;
            Assert.AreEqual(0, edges.ActiveArrowCount, "под окном стрелки скрыты");
            var close = PlayModeUtil.FindButton(gc.Hud, "btn");
            Assert.IsNotNull(close);
            PlayModeUtil.Click(close);
            Assert.IsFalse(gc.Hud.ModalOpen);

            // Экран конца кампании тоже скрывает стрелки.
            gc.Hud.ShowCampaignEnd("Кампания пройдена!", "Проверка");
            Assert.IsTrue(gc.Hud.CampaignEndVisible);
            yield return null;
            yield return null;
            Assert.AreEqual(0, edges.ActiveArrowCount, "под экраном конца кампании стрелки скрыты");
            gc.Hud.HideCampaignEnd();
            Assert.IsFalse(gc.Hud.CampaignEndVisible);

            yield return PlayModeUtil.WaitReal(0.5f);
            Assert.Greater(edges.ActiveArrowCount, 0, "стрелка вернулась после закрытия окон");

            // Тап по стрелке переводит камеру: шахта в видимой полосе поля.
            edges.ClickArrow(0);
            yield return null;
            Assert.IsTrue(InsideBand(gc, center), "после тапа по стрелке шахта в кадре");
            yield return PlayModeUtil.WaitReal(0.5f);
            Assert.AreEqual(0, edges.ActiveArrowCount, "цель в кадре — стрелки нет");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>
        /// Стрелка не появляется у цели, которую камера не может показать (прокрутка упирается в край карты):
        /// на 20:9 центр выхода в строке 0 у верхнего упора остаётся под верхней панелью, контейнер на площадке —
        /// под контекстной панелью у нижнего. Каждая показанная стрелка при тапе сдвигает камеру.
        /// </summary>
        [UnityTest]
        public IEnumerator ArrowsOnlyForTargetsCameraCanReveal()
        {
            CameraRig.OverrideAspect = 1080f / 2400f;
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            gc.SwitchSlot(SaveSlots.Debug, true, Zone);
            yield return null;
            yield return null;
            var s = gc.Session;
            var rig = gc.Rig;
            var edges = gc.Hud.Edges;
            Assert.IsNotNull(edges, "HUD создал стрелки у краёв");

            // Пределы прокрутки, по которым стрелки решают, дотянется ли камера, совпадают с упорами CameraRig.
            EdgeIndicators.CameraScrollRange(rig, s.Layout.Depth, out float minZ, out float maxZ);
            rig.FocusOn(-1000f);
            Assert.AreEqual(minZ, rig.FocusZ, 1e-3f, "нижний предел прокрутки как в CameraRig");
            rig.FocusOn(1000f);
            Assert.AreEqual(maxZ, rig.FocusZ, 1e-3f, "верхний предел прокрутки как в CameraRig");

            // Все ворота открыты, выход разрушен, на площадке контейнер бесплатного котика.
            foreach (var gate in s.Layout.Gates)
            {
                if (s.Mine.Objects[gate.Ordinal].Status == ObjectStatus.Intact) s.DebugCompleteTarget(gate.Ordinal);
                gc.OpenGate(gate.Ordinal);
            }
            yield return CloseModals(gc);
            Assert.AreEqual(s.Layout.Gates.Count, s.Mine.GatesOpened, "все ворота открыты");
            int exit = s.Layout.Exit.Ordinal;
            s.DebugCompleteTarget(exit);
            Assert.AreEqual(ObjectStatus.Broken, s.Mine.Objects[exit].Status);
            if (s.OccupiedSlots < s.UnitCap())
            {
                s.Mine.FreeCatReady = true;
                yield return PlayModeUtil.WaitReal(0.3f);
                yield return CloseModals(gc);
                Assert.Greater(s.Mine.Containers.Count, 0, "на площадке появился контейнер бесплатного котика");
            }

            // Верхний упор: выход у края карты.
            gc.ClearSelection();
            yield return EveryArrowMovesCamera(gc, 1000f);

            // Нижний упор с открытой контекстной панелью: площадка у нижнего края карты.
            if (s.Mine.Cats.Count == 0) s.DebugSpawnCat(1);
            Assert.Greater(s.Mine.Cats.Count, 0);
            gc.SelectCat(s.Mine.Cats[0].Id);
            yield return EveryArrowMovesCamera(gc, -1000f);
            gc.ClearSelection();
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>Камера у упора <paramref name="focus"/>: тап по каждой показанной стрелке переводит камеру.</summary>
        static IEnumerator EveryArrowMovesCamera(GameController gc, float focus)
        {
            var edges = gc.Hud.Edges;
            gc.Rig.FocusOn(focus);
            yield return PlayModeUtil.WaitReal(0.5f);
            int count = edges.ActiveArrowCount;
            for (int i = 0; i < count; i++)
            {
                gc.Rig.FocusOn(focus);
                edges.RequestRefresh();
                yield return null;
                yield return null;
                if (i >= edges.ActiveArrowCount) break;
                float before = gc.Rig.FocusZ;
                edges.ClickArrow(i);
                Assert.Greater(Mathf.Abs(gc.Rig.FocusZ - before), 1e-3f, "стрелка " + i + " у упора " + focus + " переводит камеру");
            }
            gc.Rig.FocusOn(focus);
        }

        /// <summary>Закрыть окна (сундуки ворот и т. п.) кнопками, как игрок.</summary>
        static IEnumerator CloseModals(GameController gc)
        {
            yield return null;
            for (int guard = 0; guard < 50 && gc.Hud.ModalOpen; guard++)
            {
                foreach (var b in gc.Hud.GetComponentsInChildren<UnityEngine.UI.Button>(false))
                {
                    if (b.gameObject.name != "btn") continue;
                    PlayModeUtil.Click(b);
                    break;
                }
                yield return null;
            }
            Assert.IsFalse(gc.Hud.ModalOpen, "окна закрыты");
        }

        [UnityTest]
        public IEnumerator HudPanelsFollowTz()
        {
            // Остановленные часы: тест проверяет HUD, а не время. Задержка кадра больше 1 с в batchmode (первая компиляция
            // шейдера) иначе считалась бы офлайном и открывала «Пока вас не было» (ТЗ 12.3, без порога).
            GameController.ClockOverride = new ManualClock(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 100.0);
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            var labels = Hud.GateBonusLabels(gc.Session);
            CollectionAssert.AreEqual(new[]
            {
                "Output of all zone generators ×2",
                "Hire price ÷10",
                "Free cat interval −60s",
                "Cat limit +1",
            }, labels, "подписи бонусов ворот — как в таблице ТЗ 11.1");

            // Кнопка найма всегда показывает уровень и цену (ТЗ 7.2).
            yield return PlayModeUtil.WaitReal(0.25f);
            var hire = PlayModeUtil.FindButton(gc.Hud, "Hire");
            Assert.IsNotNull(hire);
            var s = gc.Session;
            // Раскладка концепта: «Нанять», монетка с ценой, уровень котика и шкала уровня найма.
            var texts = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(hire.GetComponentsInChildren<TMPro.TMP_Text>(true), t => t.text));
            CollectionAssert.Contains(texts, Loc.T("hud.hire.title"));
            CollectionAssert.Contains(texts, NumberFormat.Format(s.HirePrice()), "цена найма на кнопке");
            CollectionAssert.Contains(texts, Loc.F("hud.hire.level", s.HireLevel()), "уровень нанимаемого котика на кнопке");

            // «Забрать» в панели сундука закрывает окно и затем сообщает контроллеру (ТЗ 10.3: очередь неоткрытых).
            Assert.IsFalse(gc.Hud.ModalOpen, "при старте новой игры окон нет");
            bool claimed = false;
            var chest = new ChestResult { GachaId = s.Balance.FreeGachaCycle.GachaIds[0], Elixir = GameNumber.FromDouble(5) };
            gc.Hud.ShowChest(chest, () => claimed = !gc.Hud.ModalOpen);
            Assert.IsTrue(gc.Hud.ModalOpen);
            var take = PlayModeUtil.FindButton(gc.Hud, "btn");
            Assert.IsNotNull(take);
            PlayModeUtil.Click(take);
            Assert.IsTrue(claimed, "onClaim вызван после закрытия окна");

            // Экран конца кампании (ТЗ 11.4): во весь экран, жест над ним — жест на UI, нижние панели скрыты.
            gc.Hud.ShowCampaignEnd("Кампания пройдена!", "Проверка");
            yield return null;
            var es = EventSystem.current;
            var hits = new List<RaycastResult>();
            es.RaycastAll(new PointerEventData(es) { position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) }, hits);
            Assert.Greater(hits.Count, 0, "экран конца кампании ловит лучи UI");
            Assert.IsFalse(hire.gameObject.activeInHierarchy, "нижняя панель скрыта под экраном конца кампании");
            gc.Hud.HideCampaignEnd();
            yield return null;
            Assert.IsTrue(hire.gameObject.activeInHierarchy, "нижняя панель вернулась");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }
    }
}
