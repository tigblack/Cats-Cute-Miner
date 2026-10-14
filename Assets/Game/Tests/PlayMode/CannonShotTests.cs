using System;
using System.Collections;
using CatMiners.Core;
using CatMiners.Core.Sim;
using CatMiners.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace CatMiners.Tests
{
    /// <summary>
    /// Пушка (ТЗ 6.4, 7.2) в настоящей игре: стоит у правого края под зоной мержа и смотрит влево; кузница рядом с ней
    /// не стоит — она 2×3 слева. Наём — выстрел: пушка откатывается, бочка с котиком летит дугой (поверх поля, тапа
    /// и подписи нет), приземляется в клетке появления, после чего открывается тапом. Все клетки появления левее пушки
    /// или прямо над ней — пушка всё время смотрит влево.
    /// Обучение пройдено (подсказки не закрывают поле на снимках); то, что в полёте подсказок нет, проверяет TutorialFlowTests.
    /// Снимки Docs/Reports/screenshots/cannon_idle.png, cannon_flight.png, cannon_landed.png (1080×1920). В журнале нет ошибок.
    /// </summary>
    public class CannonShotTests
    {
        static readonly string[] s_TutorialSteps =
            { "t1", "t1b", "t2", "t2b", "t3", "t3b", "t4", "t4b", "t5", "t5a", "t5b", "t6", "t7", "t7a", "t8", "t8a", "t9", "t10", "t11" };

        string m_SaveDir;
        LogWatcher m_Log;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_SaveDir = PlayModeUtil.UseTempSaveDir();
            CameraRig.OverrideAspect = 1080f / 1920f;
            // Часы симуляции стоят: кадр дольше секунды в batch-режиме не открывает окно офлайна.
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
            yield return null;
        }

        [UnityTest]
        public IEnumerator HireShootsBarrelFromCannonAtRightEdge()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            gc.SwitchSlot(SaveSlots.Debug, true, 1);
            yield return null;
            yield return PlayModeUtil.CloseModals(gc);
            var s = gc.Session;
            var l = s.Layout;
            s.DebugAddGold(GameNumber.FromDouble(100000));
            foreach (var id in s_TutorialSteps)
                if (!s.Player.TutorialDone.Contains(id)) s.Player.TutorialDone.Add(id);

            var cannon = gc.Board.Cannon;
            Assert.IsNotNull(cannon, "пушка на поле");
            Assert.AreEqual(l.Width - 1, l.CannonCol, "пушка у правого края");
            Assert.AreEqual(l.CannonCol + 0.5f, cannon.transform.position.x, 1e-3f, "пушка — в своём столбце");
            Assert.AreEqual(l.Depth - l.RowC - 1f, cannon.transform.position.z, 1e-3f, "опора пушки — нижняя кромка строки row_c");
            Assert.IsTrue(cannon.FacingLeft, "пушка смотрит влево, на зону мержа");
            var forge = gc.Board.ObjectViewOf(l.Cart.Ordinal);
            Assert.AreEqual(l.Depth - l.RowC - 2f, forge.transform.position.z, 1e-3f, "кузница 2×3 стоит опорой на строку row_c + 1");
            Assert.IsTrue(s.AutoMining, "в игре котик, разбивший камень, сам берётся за следующий");
            yield return PlayModeUtil.CaptureUi(gc, "cannon_idle.png");

            int landed = 0;
            Action<int> onLanded = id => landed++;
            gc.Board.ContainerLanded += onLanded;
            try
            {
                // Первый наём: бочка — в (3, row_c − 2), прямо над пушкой.
                int shots = cannon.Shots;
                gc.Hire();
                var barrels = PlayModeUtil.HireBarrels(s);
                Assert.AreEqual(1, barrels.Count, "наём поставил бочку");
                var barrel = barrels[0];
                Assert.AreEqual(shots + 1, cannon.Shots, "наём — выстрел пушки");
                Assert.IsTrue(gc.Board.IsFlying(barrel.Id), "бочка летит из пушки");
                var view = gc.Board.ContainerViewOf(barrel.Id);
                Assert.IsNotNull(view, "вид бочки создан сразу");
                Assert.IsFalse(view.GetComponent<BoxCollider>().enabled, "летящую бочку не открыть тапом");
                Assert.GreaterOrEqual(view.GetComponent<SortingGroup>().sortingOrder, PixelWorld.FogOrder, "летящая бочка — поверх поля");
                var box = view.transform.Find("visual/body/box").GetComponent<SpriteRenderer>();
                StringAssert.StartsWith("barrel_fly_", box.sprite.name, "в полёте котик машет лапками");
                yield return PlayModeUtil.WaitReal(0.2f);
                Assert.IsTrue(gc.Board.IsFlying(barrel.Id), "полёт длится дольше 0,2 с");

                yield return WaitLanded(gc, barrel.Id);
                Assert.AreEqual(1, landed, "приземление — одно событие (звук «тук»)");
                Assert.IsTrue(view.GetComponent<BoxCollider>().enabled, "после приземления бочку можно открыть тапом");
                Assert.AreEqual("barrel_cat", box.sprite.name, "на земле — обычная бочка");
                Assert.Less(view.GetComponent<SortingGroup>().sortingOrder, PixelWorld.FogOrder, "бочка снова сортируется по своей клетке");
                Assert.AreEqual(gc.Board.CellCenter(barrel.Cell).x, view.transform.position.x, 1e-3f, "бочка в своей клетке");

                // Второй наём — снимок в полёте; третий и четвёртый — (2, row_c − 2) и (4, row_c − 2), тоже левее пушки.
                gc.Hire();
                var second = PlayModeUtil.HireBarrels(s)[1];
                Assert.IsTrue(gc.Board.IsFlying(second.Id));
                yield return PlayModeUtil.CaptureUi(gc, "cannon_flight.png");
                yield return WaitLanded(gc, second.Id);

                gc.Hire();
                var third = PlayModeUtil.HireBarrels(s)[2];
                Assert.Less(l.Col(third.Cell), l.CannonCol, "третья бочка левее пушки");
                Assert.IsTrue(cannon.FacingLeft, "пушка смотрит влево");
                yield return WaitLanded(gc, third.Id);
                gc.Hire();
                var fourth = PlayModeUtil.HireBarrels(s)[3];
                Assert.Less(l.Col(fourth.Cell), l.CannonCol, "четвёртая бочка тоже левее пушки");
                Assert.IsTrue(cannon.FacingLeft, "пушка по-прежнему смотрит влево");
                yield return WaitLanded(gc, fourth.Id);
                Assert.AreEqual(4, landed, "все бочки приземлились");
                Assert.AreEqual(shots + 4, cannon.Shots, "четыре найма — четыре выстрела");
                yield return PlayModeUtil.WaitReal(0.4f);
                yield return PlayModeUtil.CaptureUi(gc, "cannon_landed.png");

                // Тап по приземлившейся бочке открывает её — котик выходит в её клетке.
                var pick = PickAt(gc, gc.Rig.Cam.WorldToScreenPoint(gc.Board.CellCenter(barrel.Cell) + new Vector3(0f, 0f, 0.1f)));
                Assert.IsNotNull(pick, "тап по бочке что-то выбирает");
                Assert.AreEqual(Pickable.PickKind.Container, pick.Kind, "тап по бочке — бочка");
                Assert.AreEqual(barrel.Id, pick.Id);
                gc.OpenContainer(pick.Id);
                yield return null;
                Assert.IsNotNull(s.CatAt(barrel.Cell), "из бочки вышел котик");
                Assert.IsFalse(l.IsCannonCell(barrel.Cell), "бочки не падают на пушку");
            }
            finally
            {
                gc.Board.ContainerLanded -= onLanded;
            }
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        static IEnumerator WaitLanded(GameController gc, int containerId)
        {
            float until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until && gc.Board.IsFlying(containerId)) yield return null;
            Assert.IsFalse(gc.Board.IsFlying(containerId), "бочка приземлилась не дольше чем за 2 с");
        }

        static Pickable PickAt(GameController gc, Vector3 screen)
        {
            Pickable best = null;
            float bestDist = float.MaxValue;
            foreach (var h in Physics.RaycastAll(gc.Rig.Cam.ScreenPointToRay(screen), 200f))
            {
                var p = h.collider.GetComponent<Pickable>();
                if (p == null || p.Kind != Pickable.PickKind.Container || h.distance >= bestDist) continue;
                best = p;
                bestDist = h.distance;
            }
            return best;
        }
    }
}
