using System;
using System.Collections;
using System.IO;
using System.Linq;
using CatMiners.Core.Text;
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
    /// Рендеры portrait для проверки глазами (ТЗ 6.6, 8.5): арена zone1 с override и кадры кирки игрока у камня.
    /// Генератор артефактов, отдельно: <c>Tools/unity.ps1 -Mode PlayMode -Target Ci -TestFilter OutpostStrikeEvidenceTests</c>.
    /// </summary>
    [Explicit("генератор рендеров Docs/Reports/outpost/screenshots/strike-*.png")]
    public sealed class OutpostStrikeEvidenceTests
    {
        string m_Directory;
        ManualClock m_Clock;
        OutpostController Game => GameBootstrap.OutpostController;
        static string Shots => Path.Combine(PlayModeUtil.ReportsDir(), "outpost", "screenshots");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_Directory = Path.Combine(Application.temporaryCachePath, "OutpostStrikeEvidence", Guid.NewGuid().ToString("N"));
            CameraRig.OverrideAspect = 9f / 16;
            m_Clock = new ManualClock(1_800_000_000_000L);
            OutpostController.ClockOverride = m_Clock;
            OutpostController.SaveDirectoryOverride = m_Directory;
            OutpostController.FileSystemOverride = null;
            GameBootstrap.LegacyMode = false;
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var cleanup = SceneManager.CreateScene("OutpostStrikeEvidenceCleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
            var scene = SceneManager.GetSceneByName("CoreGame");
            if (scene.IsValid()) yield return SceneManager.UnloadSceneAsync(scene);
            CameraRig.OverrideAspect = 0;
            OutpostController.ClockOverride = null;
            OutpostController.SaveDirectoryOverride = null;
            OutpostController.ConfigsOverride = null;
            OutpostController.SuppressOfflineSummary = false;
            OutpostHud.BottomReserve = 0f;
            AudioListener.pause = false;
            if (Directory.Exists(m_Directory)) Directory.Delete(m_Directory, true);
        }

        IEnumerator Advance(double seconds)
        {
            while (seconds > 0)
            {
                double step = Math.Min(0.25, seconds);
                m_Clock.Advance(step);
                seconds -= step;
                yield return null;
            }
        }

        /// <summary>Снимок текущего кадра 1080×1920 вместе с HUD (без ожидания: кадр анимации не уходит).</summary>
        void Render(string name)
        {
            var camera = Game.Rig.Cam;
            var canvas = Game.Hud.GetComponent<Canvas>();
            var scaler = Game.Hud.GetComponent<CanvasScaler>();
            var texture = new RenderTexture(1080, 1920, 24) { filterMode = FilterMode.Point };
            var previous = camera.targetTexture;
            try
            {
                camera.targetTexture = texture;
                scaler.enabled = false;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                var active = RenderTexture.active;
                RenderTexture.active = texture;
                var pixels = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0);
                pixels.Apply();
                RenderTexture.active = active;
                Directory.CreateDirectory(Shots);
                File.WriteAllBytes(Path.Combine(Shots, name), pixels.EncodeToPNG());
                UnityEngine.Object.Destroy(pixels);
            }
            finally
            {
                camera.targetTexture = previous;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                scaler.enabled = true;
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }
        }

        IEnumerator RenderTutorial()
        {
            var camera = Game.Rig.Cam;
            var canvas = Game.Hud.GetComponent<Canvas>();
            var scaler = Game.Hud.GetComponent<CanvasScaler>();
            var texture = new RenderTexture(1080, 1920, 24) { filterMode = FilterMode.Point };
            var previous = camera.targetTexture;
            bool controllerEnabled = Game.enabled, hudEnabled = Game.Hud.enabled;
            try
            {
                // Сохраняем выбранный шаг при смене технического viewport для рендера; TutorialHint сам перепроецирует указатель.
                Game.enabled = false;
                Game.Hud.enabled = false;
                camera.targetTexture = texture;
                scaler.enabled = false;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                // Подсказка поля проецируется в LateUpdate: ждём перерасчёта рамки и лапки в размере portrait.
                yield return PlayModeUtil.WaitReal(0.4f);
                Game.Hud.RefreshNow();
                Canvas.ForceUpdateCanvases();
                yield return null;
                Render("tutorial-player-strike.png");
            }
            finally
            {
                camera.targetTexture = previous;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                scaler.enabled = true;
                Game.Hud.enabled = hudEnabled;
                Game.enabled = controllerEnabled;
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }
        }

        [UnityTest]
        public IEnumerator Zone1Arena_And_PlayerStrikeFrames()
        {
            Game.State.Profile.Tutorial.Seen.Add(OutpostTutorial.StepAuto);
            yield return PlayModeUtil.WaitReal(1.2f);
            Game.Hud.RefreshNow();
            Assert.That(Game.Tutorial.CurrentHint, Is.EqualTo(Loc.T("outpost.tutorial.strike")));
            Assert.That(Game.Tutorial.StrikeTargetId, Is.GreaterThanOrEqualTo(0));
            yield return RenderTutorial();
            Game.Tutorial.Skip();
            yield return Advance(14);
            // Обзор арены: середина карты zone1.
            Game.Rig.FocusOn(Game.Board.BaseFocusZ + 7f);
            yield return PlayModeUtil.WaitReal(0.4f);
            Render("strike-01-zone1-arena.png");

            // Кирка игрока: камень текущего сектора ближе к базе, кадры замаха, удара и отскока.
            int id = Game.Map.Targets.Where(t => Game.CanStrike(t.Id)).Max(t => t.Id);
            Assert.That(Game.TryGetTargetWorldPosition(id, out var world), Is.True);
            Game.Rig.FocusOn(world.z + 2.5f);
            yield return PlayModeUtil.WaitReal(0.3f);
            long hp = Game.Sim.TargetById(id).RemainingHpUnits;
            // Кадры анимации шагаются вручную по 1/60 с: Update контроллера выключен, иначе рендер 1080×1920 в batchmode
            // занимает реальное время и проматывает анимацию (Time.captureDeltaTime не фиксирует unscaledDeltaTime).
            var shots = new System.Collections.Generic.Dictionary<int, string>
            {
                { 2, "strike-02-raised.png" }, { 5, "strike-03-swing.png" }, { 8, "strike-04-impact.png" }, { 13, "strike-05-recoil.png" },
            };
            Game.enabled = false;
            try
            {
                Assert.That(Game.BeginStrike(id, world + new Vector3(0f, 0f, 0.35f)), Is.True);
                for (int step = 1; step <= 18; step++)
                {
                    Game.Board.Sync(Game.Sim, 1f / 60f);
                    if (step == 8) Assert.That(Game.Board.ActiveStrikes, Is.EqualTo(1), "после кадра удара кирка ещё видна");
                    if (shots.TryGetValue(step, out var name)) Render(name);
                }
                Assert.That(Game.Board.ActiveStrikes, Is.EqualTo(0), "к 0,3 с кирка исчезла");
            }
            finally
            {
                Game.enabled = true;
            }
            yield return null;
            Assert.That(Game.Sim.TargetById(id).RemainingHpUnits, Is.LessThan(hp), "удар нанёс урон");
        }
    }
}
