using System;
using System.Collections;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Core.Sim;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>
    /// Снимки «до/после» обстановки шахт (Docs/Rework/MINE_DRESSING.md) из настоящего вида игры: экран игрока с HUD в начале
    /// шахты и в её середине, обзор всей шахты без HUD. Шахты 2, 30, 59 и 110 из выборки 25 шахт. Вариант «plain» — шахта без
    /// обстановки (как до изменения), «dressed» — по текущим правилам mine_dressing.json. Снимки — Docs/Reports/dressing/shots/
    /// (PNG не синхронизируются с хабом). Генератор артефактов, не проверка критериев.
    /// </summary>
    public abstract class DressingShotsFixture
    {
        protected static readonly int[] Zones = { 2, 30, 59, 110 };
        protected static string Shots => Path.Combine(PlayModeUtil.ReportsDir(), "dressing", "shots");
        string m_Directory;
        protected ManualClock Clock;
        protected OutpostController Game => GameBootstrap.OutpostController;
        protected abstract string Variant { get; }
        protected abstract OutpostConfigSet Adjust(OutpostConfigSet configs);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_Directory = Path.Combine(Application.temporaryCachePath, "DressingShots", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Directory);
            CameraRig.OverrideAspect = 9f / 16;
            Clock = new ManualClock(1_800_000_000_000L);
            OutpostController.ClockOverride = Clock;
            OutpostController.SaveDirectoryOverride = m_Directory;
            OutpostController.FileSystemOverride = null;
            OutpostController.ConfigsOverride = Adjust;
            GameBootstrap.LegacyMode = false;
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var empty = SceneManager.CreateScene("DressingShotsCleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            var loaded = SceneManager.GetSceneByName("CoreGame");
            if (loaded.IsValid()) yield return SceneManager.UnloadSceneAsync(loaded);
            CameraRig.OverrideAspect = 0;
            OutpostController.ClockOverride = null;
            OutpostController.SaveDirectoryOverride = null;
            OutpostController.ConfigsOverride = null;
            OutpostController.FileSystemOverride = null;
            GameBootstrap.LegacyMode = false;
            if (Directory.Exists(m_Directory)) Directory.Delete(m_Directory, true);
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator Shots_StartMiddleOverview()
        {
            Directory.CreateDirectory(Shots);
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            foreach (int zone in Zones)
            {
                Game.LoadZone(zone);
                yield return null;
                yield return WaitPlaying();
                Game.Hud.CloseAllWindows();
                Assert.That(Game.Map.Number, Is.EqualTo(zone));
                yield return Advance(3);
                string prefix = Variant + "-z" + zone.ToString("000");
                yield return CaptureWithHud(prefix + "-start.png");
                // Середина шахты: клетка пола на полпути между базой и выходом по столбцу точки появления.
                var map = Game.Map;
                int row = map.Rows / 2;
                int col = map.Col(map.SpawnCell);
                var middle = Game.Board.CellCenter(col, row);
                Game.Rig.FocusOn(new Vector2(middle.x, middle.z));
                yield return Advance(0.5);
                yield return CaptureWithHud(prefix + "-middle.png");
                yield return CaptureOverview(prefix + "-overview.png");
            }
        }

        protected IEnumerator WaitPlaying()
        {
            for (int frame = 0; frame < 2000 && Game.Phase != OutpostControllerPhase.Playing; frame++) yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }

        protected IEnumerator Advance(double seconds)
        {
            while (seconds > 0)
            {
                double step = Math.Min(0.25, seconds);
                Clock.Advance(step);
                seconds -= step;
                yield return null;
            }
        }

        /// <summary>Снимок 1080×1920 вместе с HUD, как экран игрока (масштаб Canvas — как у CanvasScaler при 1080×1920).</summary>
        protected IEnumerator CaptureWithHud(string name)
        {
            var camera = Game.Rig.Cam;
            var canvas = Game.Hud.GetComponent<Canvas>();
            var scaler = Game.Hud.GetComponent<CanvasScaler>();
            var texture = new RenderTexture(1080, 1920, 24) { filterMode = FilterMode.Point };
            var previous = camera.targetTexture;
            float previousScale = canvas.scaleFactor;
            try
            {
                camera.targetTexture = texture;
                scaler.enabled = false;
                canvas.scaleFactor = 1f;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                yield return PlayModeUtil.WaitReal(0.35f);
                Game.Hud.RefreshNow();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                Save(texture, 1080, 1920, name);
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

        /// <summary>Вся шахта с лагерем снизу, без HUD.</summary>
        protected IEnumerator CaptureOverview(string name)
        {
            var map = Game.Map;
            var rig = Game.Rig;
            var cam = rig.Cam;
            var bottomLeft = Game.Board.CellCenter(0, map.Rows - 1) - new Vector3(0.5f, 0f, 0.5f);
            float width = map.Width, height = map.Rows + 4f;
            var center = bottomLeft + new Vector3(width / 2f, 0f, height / 2f - 3.5f);
            int h = 2048, w = Mathf.Clamp(Mathf.RoundToInt(h * width / height), 256, 2048);
            if (w == 2048) h = Mathf.Clamp(Mathf.RoundToInt(w * height / width), 256, 2048);
            var rt = new RenderTexture(w, h, 24) { filterMode = FilterMode.Point };
            bool rigEnabled = rig.enabled;
            var position = cam.transform.position;
            float size = cam.orthographicSize;
            var target = cam.targetTexture;
            try
            {
                rig.enabled = false;
                yield return null;
                cam.transform.position = new Vector3(center.x, position.y, center.z);
                cam.orthographicSize = height / 2f;
                cam.targetTexture = rt;
                cam.Render();
                Save(rt, w, h, name);
            }
            finally
            {
                cam.targetTexture = target;
                cam.orthographicSize = size;
                cam.transform.position = position;
                rig.enabled = rigEnabled;
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
        }

        static void Save(RenderTexture rt, int w, int h, string name)
        {
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            Directory.CreateDirectory(Shots);
            File.WriteAllBytes(Path.Combine(Shots, name), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }
    }

    [Explicit("снимки шахт без обстановки: Docs/Reports/dressing/shots/plain-*.png")]
    public sealed class OutpostDressingShotsPlain : DressingShotsFixture
    {
        protected override string Variant => "plain";
        protected override OutpostConfigSet Adjust(OutpostConfigSet configs) => configs.WithDressing(null);
    }

    [Explicit("снимки шахт с обстановкой: Docs/Reports/dressing/shots/dressed-*.png")]
    public sealed class OutpostDressingShotsDressed : DressingShotsFixture
    {
        protected override string Variant => "dressed";
        protected override OutpostConfigSet Adjust(OutpostConfigSet configs) => configs;

        /// <summary>
        /// Трубы и шрифт интерфейса без прогона шахты: шахты 30 и 140 — все заводы открыты (DEV), обзор и завод с трубой крупно;
        /// шахта 30 — окно базы с улучшениями и сетка карточек. Время игры не идёт — только кадры отрисовки.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Shots_PipesAndUi()
        {
            Directory.CreateDirectory(Shots);
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            foreach (int zone in new[] { 30, 140 })
            {
                Game.LoadZone(zone);
                yield return null;
                yield return WaitPlaying();
                Game.Hud.CloseAllWindows();
                Game.DebugUnlockFactories();
                for (int k = 0; k < 10; k++) yield return null;
                var layout = Game.Board.Pipes?.Layout;
                Assert.That(layout, Is.Not.Null, "трубы шахты " + zone);
                Assert.That(layout.Pipes.Count, Is.EqualTo(Game.Map.Factories.Length), "у каждого завода труба");
                string prefix = "dressed-z" + zone.ToString("000");
                yield return CaptureOverview(prefix + "-pipes.png");
                var map = Game.Map;
                var factory = map.Factories.Select(f => map.Target(f.TargetId)).Where(t => t != null).OrderBy(t => t.Row).Last();
                var at = Game.Board.CellCenter(factory.Column, factory.Row);
                Game.Rig.FocusOn(new Vector2(at.x, at.z + 2f));
                for (int k = 0; k < 3; k++) yield return null;
                yield return CaptureWithHud(prefix + "-pipe-closeup.png");
            }
            Game.LoadZone(30);
            yield return null;
            yield return WaitPlaying();
            Game.Hud.CloseAllWindows();
            Game.Hud.ShowBase();
            yield return CaptureWithHud("ui-base-upgrades.png");
            Game.Hud.CloseAllWindows();
            Game.Hud.ShowCards();
            yield return CaptureWithHud("ui-cards.png");
            Game.Hud.CloseAllWindows();
        }
    }
}
