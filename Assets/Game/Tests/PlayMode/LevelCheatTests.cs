using System.Collections;
using CatMiners.Core.Sim;
using CatMiners.DevTools;
using CatMiners.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CatMiners.Tests
{
    /// <summary>
    /// Чит уровней (инструменты разработки): переход в любую шахту идёт в слоте Debug, основное сохранение не меняется;
    /// «Пройти шахту» доводит текущую шахту инструментами до условия 11.2 и выполняет обычный переход в следующую.
    /// </summary>
    public class LevelCheatTests
    {
        string m_SaveDir;
        LogWatcher m_Log;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_SaveDir = PlayModeUtil.UseTempSaveDir();
            CameraRig.OverrideAspect = 1080f / 1920f;
            // Остановленные часы: задержка кадра в batchmode не превращается в офлайн с окном «Пока вас не было».
            GameController.ClockOverride = new ManualClock(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 100.0);
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

        /// <summary>Закрыть окна наград («Забрать») и прочие модальные окна кнопками, как игрок.</summary>
        static IEnumerator CloseModals(GameController gc)
        {
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
        public IEnumerator JumpGoesToZoneInDebugSlotAndFinishMovesToNext()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            Assert.IsNotNull(gc);
            yield return CloseModals(gc);
            Assert.AreEqual(SaveSlots.Main, gc.Session.State.Slot, "игра начинается в основном слоте");
            var cheat = LevelCheat.Attach(gc);
            Assert.IsNotNull(cheat);

            // Из основного слота — новый слот Debug сразу с нужной шахты.
            cheat.JumpTo(5);
            yield return null;
            Assert.IsTrue(gc.Session.IsDebugSlot, "чит работает в слоте Debug");
            Assert.AreEqual(5, gc.Session.Mine.Zone);

            // В слоте Debug — переход без смены слота, номер ограничен 1…144.
            cheat.JumpTo(12);
            yield return null;
            Assert.AreEqual(12, gc.Session.Mine.Zone);
            cheat.JumpTo(1000);
            yield return null;
            Assert.AreEqual(gc.Balance.ZoneCount, gc.Session.Mine.Zone, "больше последней шахты не уходит");
            cheat.JumpTo(9);
            yield return null;
            Assert.AreEqual(9, gc.Session.Mine.Zone);

            // «Пройти шахту»: ворота, шахты и выход инструментами, затем обычный переход 11.2 с наградой выхода.
            cheat.FinishCurrentZone();
            for (int i = 0; i < 5; i++) yield return null;
            yield return CloseModals(gc);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(10, gc.Session.Mine.Zone, "переход в следующую шахту");
            Assert.AreEqual(0, gc.Session.Mine.GatesOpened, "новая шахта начинается с закрытыми воротами");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }
    }
}
