using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>Реальная сцена нового режима: рендеры portrait и отдельная запись обычной игры без выдач/ускорения.</summary>
    public sealed class OutpostEvidenceTests
    {
        string m_Directory;
        ManualClock m_Clock;
        OutpostController Game => GameBootstrap.OutpostController;
        string Reports => Path.Combine(PlayModeUtil.ReportsDir(), "outpost");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_Directory = Path.Combine(Application.temporaryCachePath, "OutpostEvidence", Guid.NewGuid().ToString("N"));
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
            var cleanup = SceneManager.CreateScene("OutpostEvidenceCleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
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

        IEnumerator Capture(string name)
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
                yield return PlayModeUtil.WaitReal(0.35f);
                Game.Hud.RefreshNow();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                var active = RenderTexture.active;
                RenderTexture.active = texture;
                var pixels = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0);
                pixels.Apply();
                RenderTexture.active = active;
                string directory = Path.Combine(Reports, "screenshots");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name), pixels.EncodeToPNG());
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

        [UnityTest, Explicit("Base upgrade progress UI evidence")]
        public IEnumerator BaseUpgradeProgressPortrait()
        {
            // Кадр всех веток: первая сессия открывает их по шахтам — здесь все отмечены открытыми и знакомыми.
            foreach (var f in Game.Configs.Progression.Features)
                if (!Game.State.Profile.Progression.Unlocked.Contains(f.Id)) Game.State.Profile.Progression.Unlocked.Add(f.Id);
            FirstSessionTestUtil.SkipPresentation(Game);
            Game.Hud.ShowBase();
            yield return Capture("base-upgrade-progress-start.png");

            // Подготовка кадров в отдельном сохранении: заработанное ядром золото и дешёвые тестовые покупки.
            // Перед рендером возвращаем обычный баланс, чтобы на кадре были настоящие цены и эффекты.
            Game.Hud.HideBase();
            var balance = Game.State.Run.Balance;
            var captureBalance = balance.Clone();
            foreach (var branch in captureBalance.Branches)
            {
                branch.BaseCost = Rational.One;
                branch.Growth = Rational.One;
            }
            Game.State.Run.Balance = captureBalance;
            int budget = balance.Branches.Sum(branch => branch.Cap);
            for (int second = 0; second < 900 && Game.State.Run.Wallet < budget; second++) yield return Advance(1);
            Assert.That(Game.State.Run.Wallet >= budget, Is.True, "ядро заработало золото для тестовых покупок");
            int[] levels = { 5, 9, 7, 8, 10, 24 };
            for (int branch = 0; branch < levels.Length; branch++)
                for (int level = 0; level < levels[branch]; level++)
                {
                    var result = Game.Buy((UpgradeBranch)branch, BuyMode.One);
                    Assert.That(result.Ok, Is.True, result + ": " + Game.ErrorText);
                }
            Game.State.Run.Balance = balance;
            Game.Hud.ShowBase();
            yield return Capture("base-upgrade-progress-partial.png");

            Game.Hud.HideBase();
            Game.State.Run.Balance = captureBalance;
            for (int branch = 0; branch < OutpostIds.BranchCount; branch++)
            {
                var kind = (UpgradeBranch)branch;
                var mode = kind == UpgradeBranch.Team ? BuyMode.One : BuyMode.Max;
                while (Game.Quote(kind, mode).CanBuy)
                {
                    var result = Game.Buy(kind, mode);
                    Assert.That(result.Ok, Is.True, result + ": " + Game.ErrorText);
                }
                Assert.That(Game.State.Run.Levels[branch], Is.EqualTo(Game.Quote(kind, BuyMode.One).Cap), "все ветки улучшены до максимума для кадра");
            }
            Game.State.Run.Balance = balance;
            Game.Hud.ShowBase();
            yield return Capture("base-upgrade-progress-max.png");
        }

        [UnityTest, Explicit("Offline gold UI evidence")]
        public IEnumerator OfflineGoldPortrait()
        {
            yield return Advance(5);
            Game.SendMessage("OnApplicationPause", true);
            m_Clock.Advance(24 * 3600);
            Game.SendMessage("OnApplicationPause", false);
            for (int frame = 0; frame < 2000 && Game.Phase != OutpostControllerPhase.Playing; frame++) yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
            Assert.That(Game.PendingOffline, Is.Not.Null);
            Assert.That(Game.PendingOffline.AppliedMs, Is.EqualTo(OutpostIds.MaxOfflineMs));
            Assert.That(Game.PendingOffline.GoldDelivered.Sign, Is.GreaterThan(0));
            yield return Capture("offline-gold-only.png");
            var wallet = Game.State.Run.Wallet;
            var pending = Game.PendingOffline;
            // Тестовый режим рекламы (ТЗ 13): кнопка сразу начисляет доход второй раз и закрывает окно.
            Game.Hud.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<TMPro.TextMeshProUGUI>()?.text.Contains(Loc.T("outpost.offline.double")) == true).onClick.Invoke();
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(wallet + pending.GoldDelivered));
            Assert.That(Game.State.Run.BonusCredited, Is.EqualTo(pending.GoldDelivered));
            Assert.That(Game.PendingOffline, Is.Null);
            Assert.That(Game.Hud.OfflineOpen, Is.False);
            yield return Capture("offline-ad-doubled.png");
        }

        [UnityTest, Explicit("Dev reset UI evidence")]
        public IEnumerator DevResetPortrait()
        {
            if (UnityEngine.Object.FindAnyObjectByType<CatMiners.DevTools.OutpostDevPanel>() == null)
                typeof(CatMiners.DevTools.OutpostDevPanel).GetMethod("Install", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
            Game.Hud.GetComponentsInChildren<Button>(true).Single(b => b.name == "OutpostDevToggle").onClick.Invoke();
            yield return Capture("dev4-reset-menu.png");
            Game.Hud.GetComponentsInChildren<Button>().Single(b => b.name == "OutpostResetProgress").onClick.Invoke();
            Assert.That(Game.Hud.ModalOpen, Is.True);
            yield return Capture("dev4-reset-confirm.png");
            Game.Hud.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<TMPro.TextMeshProUGUI>()?.text == Loc.T("outpost.dev.reset_confirm")).onClick.Invoke();
            yield return Capture("dev4-reset-newgame.png");
            Assert.That(Game.State.Run.Zone, Is.EqualTo(1));
            Assert.That(Game.State.Profile.Cards, Is.Empty);
            Assert.That(Game.State.Profile.Items, Is.Empty);
        }

        [UnityTest]
        public IEnumerator PortraitScene_HudBaseEquipmentCargoAndResults()
        {
            // Знакомства первой сессии снимает FirstSessionEvidenceTests; здесь — обычные экраны.
            FirstSessionTestUtil.SkipPresentation(Game);
            yield return Advance(20);
            yield return Capture("01-hud-delivery.png");
            Game.Hud.ShowBase();
            yield return Capture("02-base-upgrades.png");
            Assert.That(Game.Buy(UpgradeBranch.Pickaxes, BuyMode.One).Ok, Is.True);
            yield return Capture("03-base-after-purchase.png");
            Game.Hud.CloseAllWindows();
            // One purchase only (first session: Mining Power and Hire Cat only in mine 1): branching zone1 with Mia's cage completes
            // in ~1000–1300 simulated seconds. Test time advances in ordinary online steps; no debug profile or resources.
            for (int second = 0; second < 2400 && !Game.Sim.IsEconomyStopped; second++) yield return Advance(1);
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
            Game.Hud.ShowResults();
            yield return Capture("05-mine-results.png");
            Game.Hud.CloseAllWindows();
            // Шахта 1 — без предмета (первая сессия): экипировка открывается в шахте 3 со стартовой киркой.
            Assert.That(StageProgressionService.CompletionReward(Game.State).ItemId, Is.Null);
            yield return Capture("06-next-mine-button.png");
            var next = Game.Hud.GetComponentsInChildren<Button>().Single(b => b.name == "StripAction");
            next.onClick.Invoke();
            next.onClick.Invoke();
            for (int frame = 0; frame < 2000 && Game.Phase != OutpostControllerPhase.Playing; frame++) yield return null;
            Assert.That(Game.State.Run.Zone, Is.EqualTo(2));
            Game.DismissOffline();
            yield return Capture("07-next-mine.png");
            Assert.That(Game.State.Profile.RewardLog.Count, Is.EqualTo(1));
            Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment), Is.Empty);
        }
    }
}
