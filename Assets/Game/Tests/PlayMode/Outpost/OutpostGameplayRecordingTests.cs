using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>Документальная запись обычной Main-игры с реальными часами; управление покупками сценарное.</summary>
    public sealed class OutpostGameplayRecordingTests
    {
        const int Fps = 15, Width = 540, Height = 960;
        static readonly UpgradeBranch[] Cycle = { UpgradeBranch.Pickaxes, UpgradeBranch.Team,
            UpgradeBranch.Bags, UpgradeBranch.Boots, UpgradeBranch.Unload, UpgradeBranch.Value };
        string m_Directory;
        int m_NextBranch;
        float m_Origin, m_NextPurchase;
        readonly List<object> m_Actions = new List<object>();
        OutpostController Game => GameBootstrap.OutpostController;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_Directory = Path.Combine(Application.temporaryCachePath, "OutpostRecording", Guid.NewGuid().ToString("N"));
            CameraRig.OverrideAspect = 9f / 16;
            OutpostController.ClockOverride = null; // SystemClock, ordinary speed and delivered gold only.
            OutpostController.SaveDirectoryOverride = m_Directory;
            OutpostController.FileSystemOverride = null;
            GameBootstrap.LegacyMode = false;
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
            Assert.That(Game.IsDebugProfile, Is.False);
            Assert.That(Game.SimSpeed, Is.EqualTo(1));
            // The temp demonstration profile must be audible even if legacy preferences were muted.
            Game.SetVolume(CatMiners.Core.Sim.AudioGroup.Master, 1);
            m_NextBranch = 0;
            m_Actions.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var empty = SceneManager.CreateScene("OutpostRecordingCleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            var scene = SceneManager.GetSceneByName("CoreGame");
            if (scene.IsValid()) yield return SceneManager.UnloadSceneAsync(scene);
            CameraRig.OverrideAspect = 0;
            OutpostController.ClockOverride = null;
            OutpostController.SaveDirectoryOverride = null;
            OutpostController.ConfigsOverride = null;
            OutpostController.SuppressOfflineSummary = false;
            OutpostHud.BottomReserve = 0f;
            OutpostController.FileSystemOverride = null;
            AudioListener.pause = false;
            if (Directory.Exists(m_Directory)) Directory.Delete(m_Directory, true);
        }

        void BuyFromDeliveredWallet()
        {
            if (Time.realtimeSinceStartup < m_NextPurchase || Game.State.Run.Phase != RunPhase.Running) return;
            m_NextPurchase = Time.realtimeSinceStartup + 0.5f;
            for (int i = 0; i < Cycle.Length; i++)
            {
                var branch = Cycle[m_NextBranch];
                m_NextBranch = (m_NextBranch + 1) % Cycle.Length;
                if (!Game.Quote(branch, BuyMode.One).CanBuy) continue;
                Assert.That(Game.Buy(branch, BuyMode.One).Ok, Is.True);
                m_Actions.Add(new { second = Time.realtimeSinceStartup - m_Origin, action = "buy", branch = branch.ToString(),
                    wallet = Game.State.Run.Wallet.ToString(), levels = (int[])Game.State.Run.Levels.Clone() });
                return;
            }
        }

        [UnityTest, Timeout(900000), Explicit("Запись обычного Main: 60–90 с видео со звуком, около 4 минут реальной игры")]
        public IEnumerator RecordRealGameplay()
        {
            var camera = Game.Rig.Cam;
            var canvas = Game.Hud.GetComponent<Canvas>();
            var scaler = Game.Hud.GetComponent<CanvasScaler>();
            var large = new RenderTexture(1080, 1920, 24) { filterMode = FilterMode.Point };
            var small = new RenderTexture(Width, Height, 0) { filterMode = FilterMode.Point };
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var host = new GameObject("OutpostRecordingHost").AddComponent<GameplayRecordingTests.RecordingHost>();
            var tap = camera.gameObject.AddComponent<GameplayRecordingTests.AudioTap>();
            var frames = new List<byte[]>();
            var audio = new List<float>();
            var segments = new List<object>();
            int sampleRate = AudioSettings.outputSampleRate, channels = 0, startFrame = 0;
            float clipOrigin = 0;
            Coroutine capture = null;
            camera.targetTexture = large;
            scaler.enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            m_Origin = Time.realtimeSinceStartup;

            IEnumerator Grab()
            {
                double next = 0;
                while (true)
                {
                    yield return null;
                    double now = Time.realtimeSinceStartup - clipOrigin;
                    if (now < next) continue;
                    Graphics.Blit(large, small);
                    var previous = RenderTexture.active;
                    RenderTexture.active = small;
                    pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                    pixels.Apply(false);
                    RenderTexture.active = previous;
                    var jpg = pixels.EncodeToJPG(80);
                    while (next <= now) { frames.Add(jpg); next += 1d / Fps; }
                }
            }

            void StartClip()
            {
                clipOrigin = Time.realtimeSinceStartup;
                startFrame = frames.Count;
                tap.Begin();
                capture = host.StartCoroutine(Grab());
            }

            void EndClip()
            {
                host.StopCoroutine(capture);
                capture = null;
                tap.End();
                var samples = tap.Samples();
                channels = tap.Channels;
                Assert.That(channels, Is.GreaterThan(0), "AudioListener must supply recorded sound");
                int count = (int)Math.Round((frames.Count - startFrame) / (double)Fps * sampleRate) * channels;
                // Each cut uses its own sound; sample alignment differs by at most one DSP buffer.
                for (int i = 0; i < count; i++) audio.Add(i < samples.Length ? samples[i] : 0f);
                segments.Add(new { playedFromSec = clipOrigin - m_Origin,
                    playedToSec = Time.realtimeSinceStartup - m_Origin,
                    videoFromSec = startFrame / (double)Fps, videoToSec = frames.Count / (double)Fps });
            }

            try
            {
                yield return null;
                StartClip();
                while (Time.realtimeSinceStartup - clipOrigin < 32)
                {
                    float played = Time.realtimeSinceStartup - clipOrigin;
                    if (played >= 13 && played < 27)
                    {
                        if (!Game.Hud.BaseOpen) Game.Hud.ShowBase();
                        BuyFromDeliveredWallet();
                    }
                    else if (played >= 27) Game.Hud.CloseAllWindows();
                    yield return null;
                }
                EndClip();
                // Cut the middle of the same live run; the core still advances at real speed.
                while (Game.State.Run.Phase == RunPhase.Running && Time.realtimeSinceStartup - m_Origin < 600)
                {
                    BuyFromDeliveredWallet();
                    yield return null;
                }
                Assert.That(Game.State.Run.Phase, Is.Not.EqualTo(RunPhase.Running), "ordinary mine completion within ten minutes");
                StartClip();
                float deliveryDeadline = Time.realtimeSinceStartup + 25;
                while (Game.State.Run.Phase != RunPhase.ReadyToContinue && Time.realtimeSinceStartup < deliveryDeadline) yield return null;
                Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
                Assert.That(Game.State.Profile.RewardLog.Count, Is.EqualTo(1));
                // Первая сессия: шахта 1 даёт только детали, экипировка открывается в шахте 3 (Docs/Rework/FIRST_SESSION.md).
                Game.Hud.ShowResults();
                yield return PlayModeUtil.WaitReal(7);
                Game.ContinueToNextMine();
                float transitionDeadline = Time.realtimeSinceStartup + 5;
                while (Game.Phase != OutpostControllerPhase.Playing && Time.realtimeSinceStartup < transitionDeadline) yield return null;
                Assert.That(Game.State.Run.Zone, Is.EqualTo(2));
                Assert.That(Game.State.Run.Levels, Is.EqualTo(new int[OutpostIds.BranchCount]));
                Assert.That(Game.PendingOffline, Is.Null);
                m_Actions.Add(new { second = Time.realtimeSinceStartup - m_Origin, action = "next", zone = 2 });
                while (Time.realtimeSinceStartup - clipOrigin < 43) yield return null;
                EndClip();
                Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment), Is.Empty);
                string directory = Path.Combine(PlayModeUtil.ReportsDir(), "outpost", "recording");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "outpost-gameplay.avi");
                GameplayRecordingTests.AviWriter.Write(path, frames, Width, Height, Fps, audio.ToArray(), channels, sampleRate);
                var check = GameplayRecordingTests.AviWriter.Check(path);
                Assert.That(check.Video, Is.EqualTo(frames.Count));
                Assert.That(check.Index, Is.EqualTo(check.Video + check.Audio));
                double videoSeconds = frames.Count / (double)Fps;
                float peak = audio.Max(Mathf.Abs);
                Assert.That(videoSeconds, Is.InRange(60, 90));
                Assert.That(peak, Is.GreaterThan(0.001f));
                for (int i = 1; i <= 9; i += 4) File.WriteAllBytes(Path.Combine(directory, "frame-" + i + "0pct.jpg"), frames[frames.Count * i / 10]);
                File.WriteAllText(Path.Combine(directory, "recording.json"), OutpostJson.Serialize(new {
                    environment = "Unity 6000.3.11f1 Editor; Main; SystemClock; normal speed",
                    interaction = "scripted UI commands, C purchase cycle; two cuts from the same live run",
                    cheats = false, seed = Game.State.Profile.Seed, balance = Game.Configs.Balance.Version,
                    playedSeconds = Time.realtimeSinceStartup - m_Origin, videoSeconds, width = Width, height = Height,
                    fps = Fps, audioChannels = channels, audioSampleRate = sampleRate, audioPeak = peak,
                    segments, actions = m_Actions, rewardCount = Game.State.Profile.RewardLog.Count,
                    note = "Cuts omit live waiting, without clock acceleration or grants. Android and subjective audio review are separate."
                }) + "\n");
                Debug.Log("[OutpostRecording] " + path + " seconds=" + videoSeconds + " soundPeak=" + peak);
            }
            finally
            {
                if (capture != null) host.StopCoroutine(capture);
                tap.End();
                camera.targetTexture = null;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                scaler.enabled = true;
                large.Release(); small.Release();
                UnityEngine.Object.Destroy(host.gameObject);
                UnityEngine.Object.Destroy(tap);
                UnityEngine.Object.Destroy(large); UnityEngine.Object.Destroy(small); UnityEngine.Object.Destroy(pixels);
            }
        }
    }
}
