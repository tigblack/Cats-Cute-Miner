using System.Collections;
using System.Collections.Generic;
using CatMiners.Core.Sim;
using CatMiners.DevTools;
using CatMiners.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>
    /// Витрина визуальной приёмки (Docs/Visual/VISUAL_QA.md): зоны 1, 2, 5, 10, 50, 101 и 144 (все три оформления окружения)
    /// в пропорциях 9:16, 9:19,5 и 9:20 — снимки 1080×1920, 1080×2340 и 1080×2400 с HUD в Docs/Reports/visual/. В каждой
    /// зоне четыре котика добывают, два ждут в зоне мержа; уровни растут с номером зоны (ступени экипировки). Проверяется,
    /// что на поле и в HUD нет «пропавших» спрайтов (missing:) и журнал без ошибок. Снимки смотрит человек.
    /// </summary>
    public class VisualShowcaseTests
    {
        static readonly int[] s_Zones = { 1, 2, 5, 10, 50, 101, 144 };
        static readonly int[] s_Heights = { 1920, 2340, 2400 };
        static readonly string[] s_Tags = { "9x16", "9x19.5", "9x20" };

        string m_SaveDir;
        LogWatcher m_Log;
        ManualClock m_Clock;

        /// <summary>
        /// Часы игры в тесте стоят (задержка кадра в batchmode не превращается в офлайн), симуляция идёт, только когда
        /// тест их двигает: <paramref name="seconds"/> реального времени кадрами, не больше 0,05 с часов на кадр.
        /// </summary>
        IEnumerator RunSim(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                m_Clock.Advance(Mathf.Min(0.05f, Mathf.Max(0.001f, Time.unscaledDeltaTime)));
                yield return null;
            }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_SaveDir = PlayModeUtil.UseTempSaveDir();
            CameraRig.OverrideAspect = 1080f / 1920f;
            m_Clock = new ManualClock(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 100.0);
            GameController.ClockOverride = m_Clock;
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

        /// <summary>Уровни котиков витрины по зоне: от первых ступеней в начале до последней в глубине.</summary>
        static int[] Levels(int zone)
        {
            if (zone <= 1) return new[] { 1, 1, 2, 2, 3, 1 };
            if (zone <= 2) return new[] { 2, 3, 3, 4, 5, 2 };
            if (zone <= 5) return new[] { 4, 5, 6, 7, 8, 6 };
            if (zone <= 10) return new[] { 8, 10, 11, 12, 14, 10 };
            if (zone <= 50) return new[] { 15, 16, 18, 20, 22, 17 };
            return new[] { 22, 24, 26, 28, 30, 25 };
        }

        [UnityTest]
        public IEnumerator ZonesAtThreeAspects()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            Assert.IsNotNull(gc);
            yield return PlayModeUtil.CloseModals(gc);
            var cheat = LevelCheat.Attach(gc);
            var missing = new List<string>();
            foreach (int zone in s_Zones)
            {
                // За время симуляции прошлой зоны могло открыться окно награды: чит переходит только без окон.
                yield return PlayModeUtil.CloseModals(gc);
                cheat.JumpTo(zone);
                yield return PlayModeUtil.WaitReal(0.3f);
                yield return PlayModeUtil.CloseModals(gc);
                Assert.AreEqual(zone, gc.Session.Mine.Zone);
                var s = gc.Session;
                var levels = Levels(zone);
                for (int i = 0; i < 4; i++) s.DebugSpawnCat(levels[i]);
                PlayModeUtil.AssignIdleCats(gc);
                for (int i = 4; i < levels.Length; i++) s.DebugSpawnCat(levels[i]);
                gc.Hud.Toast("", 0f);
                // Симуляция идёт 2 с: котики бьют, у камней появляются стадии повреждения.
                yield return RunSim(2f);
                yield return PlayModeUtil.CloseModals(gc);
                for (int a = 0; a < s_Heights.Length; a++)
                {
                    CameraRig.OverrideAspect = 1080f / s_Heights[a];
                    yield return null;
                    yield return null;
                    yield return PlayModeUtil.CaptureUi(gc, "zone" + zone + "_" + s_Tags[a] + ".png", s_Heights[a], "visual");
                }
                CameraRig.OverrideAspect = 1080f / 1920f;
                CollectMissing(gc, zone, missing);
            }
            Assert.IsEmpty(missing, "пропавшие спрайты: " + string.Join(", ", missing));
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        static void CollectMissing(GameController gc, int zone, List<string> missing)
        {
            foreach (var sr in gc.Board.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.sprite != null && sr.sprite.name.StartsWith("missing:")) missing.Add("zone" + zone + " " + sr.sprite.name);
            foreach (var img in gc.Hud.GetComponentsInChildren<Image>(true))
                if (img.sprite != null && img.sprite.name.StartsWith("missing:")) missing.Add("zone" + zone + " HUD " + img.sprite.name);
        }
    }
}
