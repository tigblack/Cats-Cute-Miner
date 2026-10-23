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
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>
    /// Короткая запись первой сессии (Docs/Rework/FIRST_SESSION.md): новый Main-профиль, шахты 1–3. Кадры 540×960, 15 в секунду,
    /// MJPEG AVI со звуком (если AudioListener его отдаёт). Сцены (кадр клетки, закрытые кнопки, спасение, окно Mia, магазин с
    /// Mia) — в реальном времени; работа котиков между ними — на ускоренных часах ManualClock (×6…×24 к реальному времени), долгие
    /// середины шахт вырезаны (запись на паузе). Нажатия — по кнопкам нижней панели и окон, как у игрока; обучения нет.
    /// Результат: Docs/Reports/first-session/recording/first-session.avi и recording.json.
    /// </summary>
    [Explicit("запись Docs/Reports/first-session/recording/first-session.avi")]
    public sealed class FirstSessionRecordingTests : OutpostCardsFixture
    {
        const int Fps = 15, Width = 540, Height = 960;
        static readonly UpgradeBranch[] s_Policy =
            { UpgradeBranch.Pickaxes, UpgradeBranch.Team, UpgradeBranch.Bags, UpgradeBranch.Boots, UpgradeBranch.Unload, UpgradeBranch.Value, UpgradeBranch.Bays };

        readonly List<byte[]> m_Frames = new List<byte[]>();
        readonly List<float> m_Audio = new List<float>();
        readonly List<object> m_Segments = new List<object>();
        bool m_Recording;
        double m_NextFrame, m_NextBuy;
        string m_BuyRun;
        float m_ClipStart;
        int m_ClipFrame;
        RenderTexture m_Large, m_Small;
        Texture2D m_Pixels;
        GameplayRecordingTests.AudioTap m_Tap;
        int m_Channels;

        OutpostHud Hud => Game.Hud;
        double GameSeconds => Game.Sim != null ? Game.Sim.Now / (double)OutpostIds.TicksPerSecond : 0;

        /// <summary>Кадр: часы игры вперёд на dt × speed (не больше 1 с — без офлайн-расчёта), захват по 15 кадров в секунду.</summary>
        IEnumerator Frame(float speed, bool buy)
        {
            if (speed > 0f) Clock.Advance(Math.Min(1.0, Time.unscaledDeltaTime * speed));
            if (buy) BuyAffordable();
            yield return null;
            if (!m_Recording) yield break;
            double now = Time.realtimeSinceStartup - m_ClipStart;
            if (now < m_NextFrame) yield break;
            Graphics.Blit(m_Large, m_Small);
            var previous = RenderTexture.active;
            RenderTexture.active = m_Small;
            m_Pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            m_Pixels.Apply(false);
            RenderTexture.active = previous;
            var jpg = m_Pixels.EncodeToJPG(82);
            while (m_NextFrame <= now)
            {
                m_Frames.Add(jpg);
                m_NextFrame += 1d / Fps;
            }
        }

        IEnumerator Run(float seconds, float speed, bool buy = false, Func<bool> until = null)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end && (until == null || !until())) yield return Frame(speed, buy);
        }

        void BuyAffordable()
        {
            if (Game.Sim == null || Game.State.Run.Phase != RunPhase.Running || Hud.IntroOpen || Hud.AnyWindowOpen) return;
            if (m_BuyRun != Game.State.Run.RunId)
            {
                m_BuyRun = Game.State.Run.RunId;
                m_NextBuy = 0;
            }
            if (GameSeconds < m_NextBuy) return;
            m_NextBuy = GameSeconds + 1;
            foreach (var b in s_Policy)
                if (Game.Quote(b, BuyMode.One).CanBuy)
                {
                    Game.Buy(b, BuyMode.One);
                    return;
                }
        }

        void StartClip(string what)
        {
            m_Recording = true;
            m_ClipStart = Time.realtimeSinceStartup;
            m_NextFrame = 0;
            m_ClipFrame = m_Frames.Count;
            m_Tap.Begin();
            m_Segments.Add(new { clip = what, videoFromSec = m_ClipFrame / (double)Fps });
        }

        void EndClip()
        {
            if (!m_Recording) return;
            m_Recording = false;
            m_Tap.End();
            var samples = m_Tap.Samples();
            if (m_Tap.Channels > 0) m_Channels = m_Tap.Channels;
            int channels = Math.Max(1, m_Channels);
            int count = (int)Math.Round((m_Frames.Count - m_ClipFrame) / (double)Fps * AudioSettings.outputSampleRate) * channels;
            for (int i = 0; i < count; i++) m_Audio.Add(i < samples.Length ? samples[i] : 0f);
        }

        /// <summary>Нажать кнопку нижней панели и дать прочитать то, что она открыла.</summary>
        IEnumerator TapBottom(string name, float read)
        {
            var button = Hud.transform.Find("SafeArea/Bottom/BottomBar/" + name).GetComponent<Button>();
            Assert.That(button.interactable, Is.True, name);
            yield return Run(0.5f, 1f);
            button.onClick.Invoke();
            yield return Run(read, 1f);
        }

        IEnumerator TapIntro(int index, float read = 1.6f)
        {
            yield return Run(4f, 1f, false, () => Hud.IntroOpen);
            Assert.That(Hud.IntroOpen, Is.True);
            yield return Run(read, 1f);
            var root = Hud.transform.Find("SafeArea/FeatureIntro");
            var buttons = root.GetComponentsInChildren<Button>(false).Where(b => b.name == "btn").ToList();
            buttons[index].onClick.Invoke();
            yield return Run(0.5f, 1f);
        }

        IEnumerator FinishMineOffCamera()
        {
            EndClip();
            for (int guard = 0; guard < 20000 && Game.State.Run.Phase != RunPhase.ReadyToContinue; guard++)
            {
                if (Hud.ChestRewardOpen) Hud.HideChestReward();
                if (Hud.IntroOpen) Hud.HideIntro();
                Clock.Advance(1.0);
                BuyAffordable();
                yield return null;
            }
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
            for (int i = 0; i < 60 && !Hud.ResultsOpen; i++)
            {
                if (Hud.ChestRewardOpen) Hud.HideChestReward();
                if (Hud.IntroOpen) Hud.HideIntro();
                yield return null;
            }
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator RecordFirstSession()
        {
            var camera = Game.Rig.Cam;
            var canvas = Hud.GetComponent<Canvas>();
            var scaler = Hud.GetComponent<CanvasScaler>();
            m_Large = new RenderTexture(1080, 1920, 24) { filterMode = FilterMode.Point };
            m_Small = new RenderTexture(Width, Height, 0) { filterMode = FilterMode.Bilinear };
            m_Pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            m_Tap = camera.gameObject.AddComponent<GameplayRecordingTests.AudioTap>();
            Game.SetVolume(CatMiners.Core.Sim.AudioGroup.Master, 1);
            camera.targetTexture = m_Large;
            scaler.enabled = false;
            canvas.scaleFactor = 1f;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            double rescueAt = -1;
            try
            {
                yield return WaitPlaying();
                // ── Шахта 1: цель и кадр клетки (реальное время); закрытые Cards и Store называют шахту открытия.
                StartClip("mine1-opening");
                yield return Run(8f, 1f, false, () => Hud.RescueCalloutActive);
                yield return Run(6f, 1f, false, () => !Hud.RescueCalloutActive);
                yield return Run(1f, 1f);
                yield return TapBottom("Cards", 2f);
                Hud.HidePreview();
                yield return TapBottom("Store", 2f);
                Hud.HidePreview();
                yield return Run(1f, 1f);
                // Котики расчищают маршрут (ускорено, активный игрок покупает), у замка — камера к клетке и почти реальное время.
                yield return Run(25f, 24f, true, () => Game.Sim.RescueProgress().Cleared >= Game.Sim.RescueProgress().Total || !Game.Sim.RescuePending);
                if (Game.TryGetRescueCageWorldPosition(out var cage)) Game.FocusWorld(cage);
                yield return Run(25f, 6f, true, () => !Game.Sim.RescuePending);
                rescueAt = GameSeconds;
                if (Game.TryGetRescueCageWorldPosition(out cage)) Game.FocusWorld(cage);
                yield return Run(3.2f, 1f);
                yield return TapIntro(0, 2.2f);
                yield return Run(2.5f, 4f, true);
                EndClip();

                // Середина шахты 1 вырезана; итоги и переход.
                yield return FinishMineOffCamera();
                StartClip("mine1-results");
                yield return Run(2.5f, 0f);
                Hud.FindTarget(OutpostHud.TargetResultsNext).GetComponent<Button>().onClick.Invoke();
                yield return Run(10f, 1f, false, () => Game.State.Run.Zone == 2 && Game.Phase == OutpostControllerPhase.Playing && !Hud.InTransition);

                // ── Шахта 2: Store и Cards открыты; в магазине — Mia за прилавком; карточки без обучения.
                yield return Run(1.5f, 1f);
                yield return TapBottom("Store", 3f);
                Hud.HideStore();
                yield return TapBottom("Cards", 1.5f);
                Hud.HideCards();
                yield return Run(1f, 1f);
                EndClip();
                yield return FinishMineOffCamera();
                Hud.FindTarget(OutpostHud.TargetResultsNext).GetComponent<Button>().onClick.Invoke();
                for (int i = 0; i < 600 && !(Game.State.Run.Zone == 3 && Game.Phase == OutpostControllerPhase.Playing && !Hud.InTransition); i++) yield return null;

                // ── Шахта 3: Equipment открыта, стартовая кирка ждёт в разделе.
                StartClip("mine3-equipment");
                yield return Run(1.5f, 1f);
                yield return TapBottom("Equipment", 2f);
                Hud.HideEquipment();
                yield return Run(3f, 4f, true);
                EndClip();

                string directory = Path.Combine(PlayModeUtil.ReportsDir(), "first-session", "recording");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "first-session.avi");
                float peak = m_Audio.Count > 0 ? m_Audio.Max(Mathf.Abs) : 0f;
                bool sound = m_Channels > 0 && peak > 0.0005f;
                GameplayRecordingTests.AviWriter.Write(path, m_Frames, Width, Height, Fps, sound ? m_Audio.ToArray() : null, sound ? m_Channels : 0,
                    AudioSettings.outputSampleRate);
                var check = GameplayRecordingTests.AviWriter.Check(path);
                Assert.That(check.Video, Is.EqualTo(m_Frames.Count));
                File.WriteAllText(Path.Combine(directory, "recording.json"), OutpostJson.Serialize(new
                {
                    environment = "Unity Editor batch PlayMode; Main profile; ManualClock",
                    interaction = "taps on bottom-bar buttons and on the Continue button of Mia's window; active-player purchases between scenes",
                    speed = "scenes x1 real time; mining x6..x24 game time per real second; long mine middles cut",
                    videoSeconds = m_Frames.Count / (double)Fps, width = Width, height = Height, fps = Fps,
                    audio = sound, audioPeak = peak, rescueGameSec = rescueAt, segments = m_Segments,
                }) + "\n");
                Debug.Log("[FirstSessionRecording] " + path + " seconds=" + m_Frames.Count / (double)Fps + " audio=" + sound);
            }
            finally
            {
                m_Tap.End();
                camera.targetTexture = null;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                scaler.enabled = true;
                m_Large.Release();
                m_Small.Release();
                UnityEngine.Object.Destroy(m_Tap);
                UnityEngine.Object.Destroy(m_Large);
                UnityEngine.Object.Destroy(m_Small);
                UnityEngine.Object.Destroy(m_Pixels);
            }
        }
    }
}
