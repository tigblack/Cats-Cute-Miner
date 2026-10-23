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
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    public abstract class OutpostCardsFixture
    {
        string m_Directory;
        protected ManualClock Clock;
        protected OutpostController Game => GameBootstrap.OutpostController;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_Directory = Path.Combine(Application.temporaryCachePath, "OutpostCards", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Directory);
            CameraRig.OverrideAspect = 9f / 16;
            Clock = new ManualClock(1_800_000_000_000L);
            OutpostController.ClockOverride = Clock;
            OutpostController.SaveDirectoryOverride = m_Directory;
            OutpostController.FileSystemOverride = null;
            OutpostController.SimulateRewardedOffersForTests = false;
            GameBootstrap.LegacyMode = false;
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var empty = SceneManager.CreateScene("OutpostCardsCleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            var loaded = SceneManager.GetSceneByName("CoreGame");
            if (loaded.IsValid()) yield return SceneManager.UnloadSceneAsync(loaded);
            CameraRig.OverrideAspect = 0;
            OutpostController.ClockOverride = null;
            OutpostController.SaveDirectoryOverride = null;
            OutpostController.ConfigsOverride = null;
            OutpostController.SuppressOfflineSummary = false;
            OutpostController.SimulateRewardedOffersForTests = false;
            OutpostHud.BottomReserve = 0f;
            OutpostController.FileSystemOverride = null;
            GameBootstrap.LegacyMode = false;
            AudioListener.pause = false;
            if (Directory.Exists(m_Directory)) Directory.Delete(m_Directory, true);
        }
        protected Button UpgradeButton => Game.Hud.GetComponentsInChildren<Button>().Single(b => b.name == "cardUpgrade");
        protected IEnumerator WaitPlaying()
        {
            for (int frame = 0; frame < 2000 && Game.Phase != OutpostControllerPhase.Playing; frame++) yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }
    }
    public sealed class OutpostCardsUiTests : OutpostCardsFixture
    {
        [UnityTest]
        public IEnumerator Collection_AllPortraitsAndFreshUnlocks()
        {
            Game.Hud.ShowCards();
            yield return null;
            var views = Game.Hud.GetComponentsInChildren<CardView>();
            Assert.That(views.Length, Is.EqualTo(62));
            Assert.That(views.Count(v => v.State != CardViewState.Locked), Is.EqualTo(11));
            Assert.That(views.Count(v => v.State == CardViewState.Locked), Is.EqualTo(51));
            foreach (var view in views)
            {
                Assert.That(view.Art.sprite, Is.Not.Null, view.Def.Id);
                Assert.That(view.Art.sprite.name, Is.EqualTo("card_art_" + view.Def.Id));
                Assert.That(view.UpgradeArrow.gameObject.activeSelf, Is.False, "fresh collection: " + view.Def.Id);
                if (view.State == CardViewState.Locked)
                    Assert.That(view.Status.text, Is.EqualTo(Loc.F("cards.state.locked", view.Def.MineUnlock)));
            }
        }
        [UnityTest]
        public IEnumerator LockedCard_TapOpensUnlockDetails()
        {
            Game.Hud.ShowCards();
            var view = Game.Hud.GetComponentsInChildren<CardView>().First(v => v.State == CardViewState.Locked);
            view.GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.That(Game.Hud.CardDetailOpen, Is.True);
            var detail = Game.Hud.GetComponentsInChildren<CardView>().Single(v => v.Def.Id == view.Def.Id && v.GetComponent<Button>() == null);
            Assert.That(detail.Status.text, Is.EqualTo(Loc.F("cards.state.locked", view.Def.MineUnlock)));
            Assert.That(detail.Status.gameObject.activeInHierarchy, Is.True);
            Assert.That(Game.Hud.GetComponentsInChildren<Button>().Any(b => b.name == "cardUpgrade"), Is.False);
        }
        [UnityTest]
        public IEnumerator DetailsUpgrade_CommitsLevelCopiesAndElixir()
        {
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Game.GrantTestCards();
            var def = Game.Configs.Cards.Cards.First(d => CardService.Quote(Game.State.Profile, d).CanUpgrade);
            var quote = CardService.Quote(Game.State.Profile, def);
            long elixir = Game.State.Profile.Elixir;
            Game.Hud.ShowCard(def.Id);
            Assert.That(UpgradeButton.interactable, Is.True);
            var views = Game.Hud.GetComponentsInChildren<CardView>().Where(v => v.Def.Id == def.Id).ToArray();
            Assert.That(views.Length, Is.EqualTo(2), "collection and detail");
            foreach (var view in views)
            {
                Assert.That(view.UpgradeArrow.gameObject.activeInHierarchy, Is.True);
                Assert.That(view.UpgradeArrow.sprite.name, Is.EqualTo("icon_upgrade"));
                Assert.That(view.UpgradeArrow.raycastTarget, Is.False, "indicator must allow tapping the card");
            }
            UpgradeButton.onClick.Invoke();
            yield return null;
            Assert.That(Game.State.Profile.CardLevel(def.Id), Is.EqualTo(quote.Level + 1));
            Assert.That(Game.State.Profile.Elixir, Is.EqualTo(elixir - quote.ElixirNeeded));
            Assert.That(CardService.ProgressOf(Game.State.Profile, def.Id).Copies, Is.EqualTo(quote.CopiesOwned - quote.CopiesNeeded));
            foreach (var view in Game.Hud.GetComponentsInChildren<CardView>())
                Assert.That(view.UpgradeArrow.gameObject.activeSelf,
                    Is.EqualTo(CardService.Quote(Game.State.Profile, view.Def).CanUpgrade), view.Def.Id);
        }
        [UnityTest]
        public IEnumerator CardsScreen_RegistersAndClosesWithBack()
        {
            Game.Hud.ShowCards();
            yield return null;
            Assert.That(Game.Hud.AnyWindowOpen, Is.True);
            Game.Hud.HandleBack();
            Assert.That(Game.Hud.CardsOpen, Is.False);
            Assert.That(Game.Hud.AnyWindowOpen, Is.False);
        }
    }

    [Explicit("Portrait render evidence")]
    public sealed class OutpostCardsEvidenceTests : OutpostCardsFixture
    {
        IEnumerator Capture(string name, bool fast = false, Action renderedLayout = null, string outputDirectory = null, int renderHeight = 1920)
        {
            var camera = Game.Rig.Cam;
            var canvas = Game.Hud.GetComponent<Canvas>();
            var scaler = Game.Hud.GetComponent<CanvasScaler>();
            var texture = new RenderTexture(1080, renderHeight, 24) { filterMode = FilterMode.Point };
            var previous = camera.targetTexture;
            float previousScale = canvas.scaleFactor;
            try
            {
                camera.targetTexture = texture;
                scaler.enabled = false;
                canvas.scaleFactor = Mathf.Sqrt(renderHeight / 1920f);
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                if (!fast) yield return PlayModeUtil.WaitReal(0.35f);
                else yield return null;
                Game.Hud.RefreshNow();
                Canvas.ForceUpdateCanvases();
                Game.Hud.RefreshNow();
                Canvas.ForceUpdateCanvases();
                renderedLayout?.Invoke();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                var active = RenderTexture.active;
                RenderTexture.active = texture;
                var pixels = new Texture2D(1080, renderHeight, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1080, renderHeight), 0, 0);
                pixels.Apply();
                RenderTexture.active = active;
                string directory = outputDirectory ?? Path.Combine(PlayModeUtil.ReportsDir(), "outpost", "screenshots");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name), pixels.EncodeToPNG());
                UnityEngine.Object.Destroy(pixels);
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

        [Serializable]
        public sealed class PagesMetadata
        {
            public float H, V;
            public int x, y, width, height;
            public float[] offsets;
            public string[] cardIds;
        }
        [Serializable]
        public sealed class RecordingFrame
        {
            public string file, chapter, toast;
            public int durationMs, level;
            public long copies;
            public string elixir;
        }
        [Serializable]
        public sealed class RecordingMetadata { public List<RecordingFrame> frames = new List<RecordingFrame>(); }

        [UnityTest]
        public IEnumerator CardsCollectionPages()
        {
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            foreach (string scenario in new[] { "zone1", "zone3", "zone10", "states" })
            {
                Game.LoadZone(scenario == "states" ? 138 : int.Parse(scenario.Substring(4)));
                yield return WaitPlaying();
                if (scenario == "states")
                {
                    Game.GrantTestCards();
                    // Replenish through the debug API; never mutate the live profile.
                    foreach (var def in Game.Configs.Cards.Cards.OrderBy(d => d.MaxLevel).Take(2))
                        for (int i = 0; i < def.MaxLevel - 1; i++)
                        {
                            while (!CardService.Quote(Game.State.Profile, def).CanUpgrade) Game.GrantTestCards();
                            Assert.That(Game.UpgradeCard(def.Id).Ok, Is.True);
                        }
                    var active = Game.Configs.Cards.Cards.OrderByDescending(d => d.MaxLevel).First();
                    while (CardService.Quote(Game.State.Profile, active).CanUpgrade)
                        Assert.That(Game.UpgradeCard(active.Id).Ok, Is.True);
                    Assert.That(CardService.StateOf(Game.State.Profile, active), Is.EqualTo(CardViewState.Active));
                }
                Game.Hud.CloseAllWindows();
                Game.Hud.Toast("");
                Game.Hud.ShowCards();
                yield return PlayModeUtil.WaitReal(0.4f);
                var scroll = Game.Hud.GetComponentsInChildren<ScrollRect>().Single();
                var metadata = new PagesMetadata();
                Action measure = () =>
                {
                    Game.Hud.Toast("");
                    LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                    metadata.H = scroll.content.rect.height;
                    metadata.V = scroll.viewport.rect.height;
                    var corners = new Vector3[4];
                    scroll.viewport.GetWorldCorners(corners);
                    // Canvas-local coordinates avoid stale camera projection matrices before Camera.Render.
                    var canvasRect = (RectTransform)Game.Hud.GetComponent<Canvas>().transform;
                    var bottom = canvasRect.InverseTransformPoint(corners[0]);
                    var top = canvasRect.InverseTransformPoint(corners[2]);
                    float sx = 1080 / canvasRect.rect.width, sy = 1920 / canvasRect.rect.height;
                    metadata.x = Mathf.RoundToInt((bottom.x - canvasRect.rect.xMin) * sx);
                    metadata.y = Mathf.RoundToInt((canvasRect.rect.yMax - top.y) * sy);
                    metadata.width = Mathf.RoundToInt((top.x - bottom.x) * sx);
                    metadata.height = Mathf.RoundToInt((top.y - bottom.y) * sy);
                    Assert.That(metadata.x, Is.GreaterThanOrEqualTo(0));
                    Assert.That(metadata.y + metadata.height, Is.LessThanOrEqualTo(1920));
                    metadata.offsets = Enumerable.Range(0, Mathf.CeilToInt(metadata.H / metadata.V))
                        .Select(k => Mathf.Min(k * metadata.V, metadata.H - metadata.V)).ToArray();
                    metadata.cardIds = scroll.content.GetComponentsInChildren<CardView>().Select(v => v.Def.Id).ToArray();
                    Assert.That(metadata.cardIds.Length, Is.EqualTo(62));
                    scroll.StopMovement();
                    scroll.verticalNormalizedPosition = 1;
                };
                yield return Capture("cards-page-" + scenario + "-0.png", false, measure);
                for (int k = 1; k < metadata.offsets.Length; k++)
                {
                    float offset = metadata.offsets[k];
                    yield return Capture("cards-page-" + scenario + "-" + k + ".png", false, () =>
                    {
                        scroll.StopMovement();
                        scroll.verticalNormalizedPosition = 1 - offset / (metadata.H - metadata.V);
                    });
                }
                File.WriteAllText(Path.Combine(PlayModeUtil.ReportsDir(), "outpost", "screenshots", "cards-pages-" + scenario + ".json"), JsonUtility.ToJson(metadata, true));
            }
        }

        [UnityTest]
        public IEnumerator CardStatesCa003()
        {
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            foreach (string state in new[] { "locked", "available", "owned" })
            {
                Game.LoadZone(state == "locked" ? 2 : 3);
                yield return WaitPlaying();
                if (state == "owned") Game.GrantTestCards();
                Game.Hud.CloseAllWindows();
                Game.Hud.ShowCard("ca003");
                yield return Capture("card-ca003-state-" + state + ".png");
            }
        }

        [UnityTest]
        public IEnumerator CardsObtainUpgradeRecording()
        {
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Game.SimSpeed = 1;
            string directory = Path.Combine(PlayModeUtil.ReportsDir(), "outpost", "recording", "cards");
            Directory.CreateDirectory(directory);
            foreach (string file in Directory.GetFiles(directory, "frame_*.png")) File.Delete(file);
            var recording = new RecordingMetadata();
            yield return RecordFrame(recording, directory, "start", 700);
            int afterDelivery = 0;
            for (int i = 0; i < 600 && afterDelivery < 15; i++)
            {
                Clock.Advance(0.2);
                yield return null;
                if (Game.State.Profile.CardLevel("ca001") > 0) afterDelivery++;
                // Five FPS around the tutorial delivery, sparse lead-in if logistics takes longer.
                if (i < 30 || afterDelivery > 0 || i % 5 == 0)
                    yield return RecordFrame(recording, directory, "first-delivery", afterDelivery == 1 ? 1800 : 200);
            }
            Assert.That(Game.State.Profile.CardLevel("ca001"), Is.GreaterThan(0), "real tutorial delivery");
            Game.SimSpeed = 20;
            for (int i = 0; i < 2000 && Game.State.Run.Phase != RunPhase.ReadyToContinue; i++)
            {
                Clock.Advance(0.25);
                yield return null;
                if (i % 3 == 0) yield return RecordFrame(recording, directory, "mining-x20", 100);
            }
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
            Game.SimSpeed = 1;
            yield return PlayModeUtil.WaitReal(0.15f);
            for (int i = 0; i < 4; i++)
            {
                yield return RecordFrame(recording, directory, "exit-chest-results", 1000);
                yield return PlayModeUtil.WaitReal(0.08f);
            }
            // Сундук выхода с новой карточкой показывается окном; итоги шахты открываются после него.
            if (Game.Hud.ChestRewardOpen)
            {
                Assert.That(Game.Hud.ResultsOpen, Is.False, "итоги ждут окна сундука");
                Assert.That(Game.Hud.HandleBack(), Is.True);
                Game.Hud.RefreshNow();
                yield return RecordFrame(recording, directory, "exit-chest-results", 1000);
            }
            Assert.That(Game.Hud.ResultsOpen, Is.True);
            Game.Hud.CloseAllWindows();
            Game.Hud.ShowCards();
            yield return PlayModeUtil.WaitReal(0.4f);
            yield return RecordFrame(recording, directory, "collection-owned", 1700);
            Game.Hud.ShowCard("ca001");
            yield return PlayModeUtil.WaitReal(0.4f);
            yield return RecordFrame(recording, directory, "owned-detail", 1700);
            Game.GrantTestCards();
            yield return RecordFrame(recording, directory, "debug-grant-upgrade-ready", 2000);
            var quote = CardService.Quote(Game.State.Profile, Game.Configs.Cards.Get("ca001"));
            var elixir = Game.State.Profile.Elixir;
            Assert.That(UpgradeButton.interactable, Is.True);
            UpgradeButton.onClick.Invoke();
            for (int i = 0; i < 8; i++)
            {
                yield return RecordFrame(recording, directory, "upgrade", i == 7 ? 2500 : 100);
                yield return PlayModeUtil.WaitReal(0.04f);
            }
            Assert.That(Game.State.Profile.CardLevel("ca001"), Is.EqualTo(quote.Level + 1));
            Assert.That(Game.State.Profile.Elixir, Is.EqualTo(elixir - quote.ElixirNeeded));
            Assert.That(CardService.ProgressOf(Game.State.Profile, "ca001").Copies, Is.EqualTo(quote.CopiesOwned - quote.CopiesNeeded));
            File.WriteAllText(Path.Combine(directory, "frames.json"), JsonUtility.ToJson(recording, true));
        }

        IEnumerator RecordFrame(RecordingMetadata metadata, string directory, string chapter, int duration)
        {
            string file = "frame_" + metadata.frames.Count.ToString("D3") + ".png";
            yield return Capture(file, true, null, directory);
            var progress = CardService.ProgressOf(Game.State.Profile, "ca001");
            metadata.frames.Add(new RecordingFrame
            {
                file = file, chapter = chapter, durationMs = duration,
                level = progress?.Level ?? 0, copies = progress?.Copies ?? 0,
                elixir = Game.State.Profile.Elixir.ToString(),
                toast = Game.Hud.GetComponentsInChildren<TextMeshProUGUI>().FirstOrDefault(t => t.name == "Toast")?.text ?? ""
            });
        }

        [UnityTest]
        public IEnumerator CardsPortraitsAndUpgrade()
        {
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            foreach (int zone in new[] { 1, 2, 3, 10, 138 })
            {
                Game.LoadZone(zone);
                yield return WaitPlaying();
                Game.Hud.CloseAllWindows();
                Game.Hud.ShowCards();
                Game.Hud.GetComponentsInChildren<ScrollRect>().Single().verticalNormalizedPosition = 1;
                yield return Capture("cards-zone" + zone + ".png");
                if (zone == 1)
                {
                    // Закрытые карты идут после доступных и не попадают в первый экран коллекции.
                    var scroll = Game.Hud.GetComponentsInChildren<ScrollRect>().Single();
                    scroll.verticalNormalizedPosition = 0.5f;
                    yield return Capture("cards-zone1-locked.png");
                    scroll.verticalNormalizedPosition = 1;
                }
                if (zone == 2)
                {
                    Game.Hud.ShowCard("ca003");
                    yield return Capture("card-ca003-locked-detail.png");
                    Game.Hud.HideCardDetail();
                }
                if (zone == 3)
                {
                    Game.Hud.ShowCard("ca003");
                    yield return Capture("card-ca003-available.png");
                    Game.Hud.HideCardDetail();
                }
                if (zone == 138)
                {
                    Game.Hud.GetComponentsInChildren<ScrollRect>().Single().verticalNormalizedPosition = 0;
                    yield return Capture("cards-zone138-end.png");
                }
            }
            Game.GrantTestCards();
            var def = Game.Configs.Cards.Cards.First(d => CardService.Quote(Game.State.Profile, d).CanUpgrade);
            Game.Hud.ShowCard(def.Id);
            yield return Capture("card-upgrade-before.png");
            UpgradeButton.onClick.Invoke();
            yield return Capture("card-upgrade-after.png");

            // Обзор всех 61 окон: проверяем крупные подписи в обычном и узком портретных форматах.
            string directory = Path.Combine(PlayModeUtil.ReportsDir(), "card-detail-layout");
            Directory.CreateDirectory(directory);
            var layout = new List<string>();
            foreach (int height in new[] { 1920, 2400 })
            {
                foreach (var card in Game.Configs.Cards.Cards)
                {
                    Game.Hud.ShowCard(card.Id);
                    Action inspect = () =>
                    {
                        var detail = Game.Hud.GetComponentsInChildren<CardView>().Single(v => v.GetComponent<Button>() == null);
                        foreach (var text in detail.GetComponentsInChildren<TextMeshProUGUI>())
                        {
                            text.ForceMeshUpdate();
                            layout.Add(height + "\t" + card.Id + "\t" + text.name + "\t" + text.fontSize + "\t" +
                                text.isTextTruncated + "\t" + text.text.Replace('\n', ' '));
                        }
                    };
                    // Отдельные снимки разных единиц бонуса; остальные окна замеряются без выгрузки PNG.
                    if (new[] { "ca001", "ca028", "ca035", "ca036", "ca038", "ca060", "ca062" }.Contains(card.Id))
                        yield return Capture("card-detail-" + card.Id + "-" + height + ".png", true, inspect, directory, height);
                    else
                        yield return Capture("card-detail-measure.png", true, inspect, directory, height);
                }
            }
            File.WriteAllLines(Path.Combine(directory, "layout.tsv"), layout);
            var master = Game.Configs.Cards.Get("ca001");
            CardService.ProgressOf(Game.State.Profile, master.Id).Level = master.MaxLevel;
            Game.Hud.ShowCard(master.Id);
            yield return Capture("card-detail-max.png", false, null, directory);
            CardService.ProgressOf(Game.State.Profile, master.Id).Level = 1;
            CardService.ProgressOf(Game.State.Profile, master.Id).Copies = 0;
            Game.State.Profile.Elixir = 50;
            Game.Hud.ShowCard(master.Id);
            yield return Capture("card-detail-copies-needed.png", false, null, directory);
        }
    }
}
