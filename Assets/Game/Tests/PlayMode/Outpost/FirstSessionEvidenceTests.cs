using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>
    /// Первая сессия в настоящей сцене (Docs/Rework/FIRST_SESSION.md, решение владельца 21.10.2026): новый Main-профиль проходит
    /// шахты 1–3 на ускоренных часах (ManualClock, шаг 0,5 с игры за кадр; сцены спасения и окна — в реальном времени) с политикой
    /// «активного игрока» (покупка доступной ветки раз в секунду игры, без подсказок). Проверяется: обучение — только спасение Mia
    /// (кадр клетки и окно «Mia is free!»), других окон и подсказок нет; Store, Cards, Equipment и Sell Goods всегда на панели,
    /// закрытые — с замком, тап называет требование; Store открывается вместе с Cards в шахте 2 и встречает игрока портретом Mia;
    /// Equipment — после спасения Bruno и завершения шахты 2, до перехода в шахту 3. Снимки 1080×1920 — Docs/Reports/first-session/*.png, замеры — report.json.
    /// Генератор: <c>Tools/unity-mac.sh PlayMode FirstSessionEvidenceTests</c>.
    /// </summary>
    [Explicit("генератор снимков Docs/Reports/first-session/*.png")]
    public sealed class FirstSessionEvidenceTests : OutpostCardsFixture
    {
        static string Shots => Path.Combine(PlayModeUtil.ReportsDir(), "first-session");
        static readonly UpgradeBranch[] s_Policy =
            { UpgradeBranch.Pickaxes, UpgradeBranch.Team, UpgradeBranch.Bags, UpgradeBranch.Boots, UpgradeBranch.Unload, UpgradeBranch.Value, UpgradeBranch.Bays };
        readonly List<string> m_Log = new List<string>();
        readonly Dictionary<string, object> m_Report = new Dictionary<string, object>();
        readonly List<string> m_Truncated = new List<string>();
        /// <summary>Заголовки окон первой сессии и тексты подсказок вне кадра клетки, замеченные за прогон.</summary>
        readonly List<string> m_Windows = new List<string>(), m_StrayHints = new List<string>();
        double m_NextBuy;
        string m_BuyRun;
        int m_RescueEvents;
        long m_RescueTick = -1;

        OutpostHud Hud => Game.Hud;

        void Note(string line)
        {
            m_Log.Add(line);
            Debug.Log("[FirstSession] " + line);
        }

        double GameSeconds => Game.Sim != null ? Game.Sim.Now / (double)OutpostIds.TicksPerSecond : 0;

        void OnEvent(OutpostEvent e)
        {
            if (e.Type != OutpostEventType.CharacterRescued) return;
            m_RescueEvents++;
            m_RescueTick = e.Tick;
        }

        /// <summary>Окна и подсказки на экране: запомнить заголовок окна первой сессии и любую подсказку вне кадра клетки.</summary>
        void Watch()
        {
            string title = Hud.IntroTitle;
            if (title != null && (m_Windows.Count == 0 || m_Windows[m_Windows.Count - 1] != title)) m_Windows.Add(title);
            if (Hud.GuideHintVisible && !Hud.RescueCalloutActive && !m_StrayHints.Contains(Hud.GuideHintText)) m_StrayHints.Add(Hud.GuideHintText);
        }

        /// <summary>Игра идёт: часы вперёд на <paramref name="seconds"/> с игры шагами по 0,5 с за кадр; активный игрок покупает.</summary>
        IEnumerator Play(double seconds, bool buy, Func<bool> until = null)
        {
            while (seconds > 0)
            {
                if (until != null && until()) yield break;
                double step = Math.Min(0.5, seconds);
                Clock.Advance(step);
                seconds -= step;
                if (buy) BuyAffordable();
                Watch();
                yield return null;
            }
        }

        /// <summary>Активный игрок: раз в секунду игры — первая по порядку доступная покупка ×1 (при открытом окне — ждёт).</summary>
        void BuyAffordable()
        {
            if (Game.Sim == null || Game.State.Run.Phase != RunPhase.Running || Hud.IntroOpen) return;
            // Новая шахта — часы ядра снова с нуля.
            if (m_BuyRun != Game.State.Run.RunId)
            {
                m_BuyRun = Game.State.Run.RunId;
                m_NextBuy = 0;
            }
            if (GameSeconds < m_NextBuy) return;
            m_NextBuy = GameSeconds + 1;
            foreach (var b in s_Policy)
            {
                if (!Game.Quote(b, BuyMode.One).CanBuy) continue;
                Assert.That(Game.Buy(b, BuyMode.One).Ok, Is.True, b.ToString());
                return;
            }
        }

        /// <summary>Ждать (реальное время, часы стоят) условие до <paramref name="seconds"/> с.</summary>
        IEnumerator WaitFor(Func<bool> condition, float seconds, string what)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until)
            {
                Watch();
                yield return null;
            }
            Watch();
            Assert.That(condition(), Is.True, "timeout: " + what + "\n" + Dump());
        }

        /// <summary>Состояние окон и фаз — в сообщение о таймауте.</summary>
        string Dump()
        {
            var run = Game.State?.Run;
            var pr = Game.State?.Profile?.Progression;
            return "phase=" + Game.Phase + " run=" + run?.Phase + " zone=" + run?.Zone + " results=" + Hud.ResultsOpen + " chest=" + Hud.ChestRewardOpen +
                   " intro=" + Hud.IntroOpen + "(" + Hud.IntroTitle + ") preview=" + Hud.PreviewFeature + " base=" + Hud.BaseOpen + " cards=" + Hud.CardsOpen +
                   " equip=" + Hud.EquipmentOpen + " market=" + Hud.MarketOpen + " store=" + Hud.StoreOpen + " modal=" + Hud.ModalOpen +
                   " offline=" + Hud.OfflineOpen + " calc=" + Hud.OfflineCalcOpen + " skip=" + Hud.SkipResultOpen + " settings=" + Hud.SettingsOpen +
                   " any=" + Hud.AnyWindowOpen + " hint=" + Hud.GuideHintText + " scene=" + Game.RescueSceneActive +
                   " transition=" + Game.InTransition + "/" + Hud.InTransition + " saveFailed=" + Game.SaveFailed + " pendingOffline=" + (Game.PendingOffline != null) +
                   " unlocked=[" + (pr != null ? string.Join(",", pr.Unlocked) : "") + "] shown=[" + (pr != null ? string.Join(",", pr.Shown) : "") +
                   "] crew=[" + (pr != null ? string.Join(",", pr.Crew) : "") + "]";
        }

        /// <summary>Кнопка «Continue» окна после спасения.</summary>
        IEnumerator TapIntroButton(string what)
        {
            Assert.That(Hud.IntroOpen, Is.True, "window open: " + what);
            var root = Hud.transform.Find("SafeArea/FeatureIntro");
            Assert.That(root, Is.Not.Null);
            var button = root.GetComponentsInChildren<Button>(false).First(b => b.name == "btn");
            Note("tap '" + button.GetComponentInChildren<TMP_Text>().text + "' for " + what);
            button.onClick.Invoke();
            yield return PlayModeUtil.WaitReal(0.3f);
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null && t.GetComponent<Canvas>() == null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>Снимок 1080×1920 вместе с HUD; масштаб Canvas — как у CanvasScaler (match 0,5). Обрезанные подписи — в отчёт.</summary>
        IEnumerator Capture(string name, float wait = 0.3f, int height = 1920)
        {
            var camera = Game.Rig.Cam;
            var canvas = Hud.GetComponent<Canvas>();
            var scaler = Hud.GetComponent<CanvasScaler>();
            var texture = new RenderTexture(1080, height, 24) { filterMode = FilterMode.Point };
            var previous = camera.targetTexture;
            float previousScale = canvas.scaleFactor;
            try
            {
                camera.targetTexture = texture;
                scaler.enabled = false;
                canvas.scaleFactor = Mathf.Sqrt(height / 1920f);
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                yield return PlayModeUtil.WaitReal(wait);
                Canvas.ForceUpdateCanvases();
                foreach (var t in Hud.GetComponentsInChildren<TMP_Text>())
                {
                    if (string.IsNullOrEmpty(t.text)) continue;
                    t.ForceMeshUpdate();
                    if (t.isTextTruncated) m_Truncated.Add(name + "\t" + PathOf(t.transform) + "\t" + t.text.Replace("\n", "\\n"));
                }
                camera.Render();
                var active = RenderTexture.active;
                RenderTexture.active = texture;
                var pixels = new Texture2D(1080, height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1080, height), 0, 0);
                pixels.Apply();
                RenderTexture.active = active;
                Directory.CreateDirectory(Shots);
                File.WriteAllBytes(Path.Combine(Shots, name), pixels.EncodeToPNG());
                UnityEngine.Object.Destroy(pixels);
                Note("shot " + name + " at game " + GameSeconds.ToString("0.0") + " s, zone " + Game.State.Run.Zone);
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

        Transform Bottom(string buttonName) => Hud.transform.Find("SafeArea/Bottom/BottomBar/" + buttonName);

        string IconOf(string buttonName)
        {
            var icon = Bottom(buttonName)?.Find("content/icon");
            var img = icon != null ? icon.GetComponent<Image>() : null;
            return img != null && img.sprite != null ? img.sprite.name : null;
        }

        bool Shown(string buttonName) => Bottom(buttonName) != null && Bottom(buttonName).gameObject.activeInHierarchy;
        bool Locked(string buttonName) => Bottom(buttonName).Find("content/lock").gameObject.activeSelf;

        bool Badge(string buttonName)
        {
            var badge = Bottom(buttonName).Find("badge");
            return badge != null && badge.gameObject.activeInHierarchy;
        }

        /// <summary>Нажать кнопку нижней панели, как игрок: она видна и нажимается.</summary>
        IEnumerator TapBottom(string buttonName)
        {
            var button = Bottom(buttonName).GetComponent<Button>();
            Assert.That(button.gameObject.activeInHierarchy && button.interactable, Is.True, buttonName + " visible and interactable");
            Note("tap bottom " + buttonName);
            button.onClick.Invoke();
            yield return PlayModeUtil.WaitReal(0.2f);
        }

        /// <summary>Закрытый раздел: тап по кнопке с замком — окно требования с текстом <paramref name="requirement"/>, рабочего экрана нет.</summary>
        IEnumerator TapLocked(string buttonName, string feature, string requirement, string shot)
        {
            Assert.That(Locked(buttonName), Is.True, buttonName + ": lock");
            yield return TapBottom(buttonName);
            Assert.That(Hud.PreviewFeature, Is.EqualTo(feature), buttonName);
            Assert.That(Hud.StoreOpen || Hud.CardsOpen || Hud.EquipmentOpen || Hud.MarketOpen, Is.False, buttonName + ": no working screen");
            string texts = string.Join("\n", Hud.transform.Find("SafeArea/FeaturePreview").GetComponentsInChildren<TMP_Text>().Select(t => t.text));
            Assert.That(texts, Does.Contain(requirement), buttonName + ": requirement");
            yield return Capture(shot);
            Hud.HidePreview();
            yield return PlayModeUtil.WaitReal(0.2f);
        }

        /// <summary>Завершить шахту активным игроком; итоги открываются сами (после окна Mia), «Next mine» — кнопкой итогов.</summary>
        IEnumerator FinishMine(string resultsShot, double limitSeconds)
        {
            yield return Play(limitSeconds, true, () => Game.State.Run.Phase == RunPhase.ReadyToContinue);
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue), "mine " + Game.State.Run.Zone + " completes");
            m_Report["mine" + Game.State.Run.Zone + "CompletedGameSec"] = Math.Round(GameSeconds, 1);
            // Сундуки шахты (если остались) — раньше итогов: закрываем их как игрок.
            for (int i = 0; i < 8 && Hud.ChestRewardOpen; i++)
            {
                Note("close chest reward before results");
                Hud.HideChestReward();
                yield return PlayModeUtil.WaitReal(0.3f);
            }
            yield return WaitFor(() => Hud.ResultsOpen, 5f, "results open");
            if (resultsShot != null) yield return Capture(resultsShot);
            int zone = Game.State.Run.Zone;
            var next = Hud.FindTarget(OutpostHud.TargetResultsNext).GetComponent<Button>();
            Assert.That(next.interactable, Is.True);
            next.onClick.Invoke();
            yield return WaitFor(() => Game.Phase == OutpostControllerPhase.Playing && Game.State.Run.Zone == zone + 1 && !Hud.InTransition, 20f, "next mine");
            yield return PlayModeUtil.WaitReal(0.3f);
            Note("mine " + zone + " done");
        }

        /// <summary>Новая шахта: несколько секунд реального времени — ни окна обучения, ни подсказки.</summary>
        IEnumerator AssertNoTutorial(string where)
        {
            yield return Play(3, true);
            yield return PlayModeUtil.WaitReal(1f);
            Watch();
            Assert.That(Hud.IntroOpen, Is.False, where + ": no tutorial window");
            Assert.That(Hud.GuideHintVisible, Is.False, where + ": no hint");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator MiaTwoLaneMine_ThreeCurrencies_Decor_Rescue()
        {
            Directory.CreateDirectory(Shots);
            yield return WaitPlaying();
            Assert.That(Game.Sim.Map.RequiredCount, Is.EqualTo(15));
            Assert.That(Game.Sim.Map.Topology.Layout, Is.EqualTo(MiaCorridor.Layout));
            var top = Hud.transform.Find("SafeArea/TopBar");
            Assert.That(top.Find("cargo"), Is.Null);
            Assert.That(top.Find("progress"), Is.Null);
            Assert.That(top.Find("gold/icon").GetComponent<Image>().sprite.name, Is.EqualTo("icon_coin"));
            Assert.That(top.Find("elixir/icon").GetComponent<Image>().sprite.name, Is.EqualTo("icon_elixir"));
            Assert.That(top.Find("workers"), Is.Null);
            Assert.That(top.Find("gems/icon").GetComponent<Image>().sprite.name, Is.EqualTo("icon_gem"));
            var coin = top.Find("gold/gold").GetComponent<TMP_Text>();
            var elixir = top.Find("elixir/elixir").GetComponent<TMP_Text>();
            Game.State.Profile.Elixir = 250;
            Hud.RefreshNow();
            Assert.That(elixir.text, Is.EqualTo("250"));
            Game.State.Profile.Elixir = 17;
            Hud.RefreshNow();
            Assert.That(elixir.text, Is.EqualTo("17"));
            Game.State.Profile.Elixir = 0;
            Hud.RefreshNow();
            Assert.That(elixir.text, Is.EqualTo("0"));
            var scenery = Game.Board.GetComponentsInChildren<Transform>().Single(t => t.name == "MiaMineScenery");
            Assert.That(scenery.GetComponentsInChildren<SpriteRenderer>().Where(v => !v.sprite.name.StartsWith("shadow_")).Select(v => v.sprite.name).Distinct().Count(), Is.EqualTo(7));
            Assert.That(scenery.GetComponentsInChildren<Collider>(), Is.Empty, "Scenery has no gameplay interaction");
            var gems = top.Find("gems/gems").GetComponent<TMP_Text>();
            Game.State.Profile.Gems = 37; // Isolated fixture; verify both increasing and decreasing balances.
            Hud.RefreshNow();
            Assert.That(gems.text, Is.EqualTo("37"));
            Game.State.Profile.Gems = 12;
            Hud.RefreshNow();
            Assert.That(gems.text, Is.EqualTo("12"));
            Game.State.Profile.Gems = 0;
            Hud.RefreshNow();
            Assert.That(gems.text, Is.EqualTo("0"));
            Assert.That(coin.text, Is.EqualTo(OutpostUiText.Money(Game.State.Run.Wallet)));
            yield return WaitFor(() => Hud.RescueCalloutActive, 4f, "Mia opening camera");
            yield return Capture("mia-two-lane-opening.png", 0.7f);
            yield return WaitFor(() => !Hud.RescueCalloutActive, 8f, "opening camera ends");
            Game.FocusWorld(Game.Board.CellCenter(6, 5));
            yield return Capture("mia-two-lane-layout.png", 0.8f);
            yield return Capture("mia-two-lane-layout-tall.png", 0.1f, 2400);
            Game.EventPresented += OnEvent;
            try
            {
                yield return Play(1800, true, () => !Game.Sim.RescuePending);
                Assert.That(Game.Sim.RescuePending, Is.False);
                Assert.That(m_RescueEvents, Is.EqualTo(1));
                Hud.RefreshNow();
                Assert.That(coin.text, Is.EqualTo(OutpostUiText.Money(Game.State.Run.Wallet)));
                yield return WaitFor(() => Game.Board.MiaWalking, 3f, "Mia walks out");
                yield return Capture("mia-two-lane-rescued.png", 0.25f);
                yield return WaitFor(() => Hud.IntroOpen, 4f, "Mia is free");
                Assert.That(Hud.IntroTitle, Is.EqualTo("Mia is free!"));
                yield return Capture("mia-two-lane-free.png");
                yield return TapIntroButton("Continue");
                yield return Play(600, true, () => Game.State.Run.Phase == RunPhase.ReadyToContinue);
                Assert.That(Game.State.Run.Targets.Count(t => t.Destroyed), Is.EqualTo(15));
                Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
                Assert.That(m_Truncated, Is.Empty, string.Join("\n", m_Truncated));
                File.WriteAllText(Path.Combine(Shots, "mia-two-lane.json"), OutpostJson.Serialize(new
                {
                    obstacles = Game.Sim.Map.RequiredCount, destroyed = Game.State.Run.Targets.Count(t => t.Destroyed),
                    rescueEvents = m_RescueEvents, rescueGameSeconds = m_RescueTick / (double)OutpostIds.TicksPerSecond,
                    currencies = new[] { "icon_coin", "icon_elixir", "icon_gem" }, truncated = m_Truncated,
                }));
            }
            finally { Game.EventPresented -= OnEvent; }
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Hiring_Mine3_ShowsNormalPrice_PaysItAndKeepsCrewAfterReload()
        {
            Directory.CreateDirectory(Shots);
            yield return WaitPlaying();
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Game.LoadZone(3);
            yield return WaitFor(() => Game.State.Run.Zone == 3 && Game.Phase == OutpostControllerPhase.Playing && !Hud.InTransition,
                20f, "Mine 3 directly");
            FirstSessionTestUtil.SkipPresentation(Game);
            Assert.That(Game.Sim.Submit(OutpostCommand.RewardBonus(RewardedBonusKind.Gold, 100000)).Ok, Is.True);
            Hud.ShowBase();
            Game.FocusBase();
            yield return Capture("hiring-mine3-rank0.png");
            var first = Game.Quote(UpgradeBranch.Team, BuyMode.One).NextCost.Value;
            for (int i = 0; i < 4; i++) Assert.That(Game.Buy(UpgradeBranch.Team, BuyMode.One).Ok, Is.True);
            var expected = (Game.State.Run.Balance.PriceScale(3) * Game.State.Run.PriceFactor * 50 * Rational.Of(19, 10).Pow(4) *
                (Rational.One - Game.Sim.Stats.CardHireDiscount)).Ceil();
            Assert.That(Game.Quote(UpgradeBranch.Team, BuyMode.One).NextCost, Is.EqualTo(expected));
            yield return Capture("hiring-mine3-rank4.png");
            var row = Hud.GetComponentsInChildren<RectTransform>().Single(r => r.name == "branch" + (int)UpgradeBranch.Team);
            Assert.That(row.GetComponentsInChildren<TMP_Text>().Any(t => t.text == OutpostUiText.Money(expected)), Is.True,
                "Base price matches the full core quote");
            var wallet = Game.State.Run.Wallet;
            int crew = Game.State.Run.Workers.Count;
            row.GetComponentInChildren<Button>().onClick.Invoke();
            yield return null;
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(wallet - expected));
            Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(crew + 1));
            Hud.HideBase();
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return WaitPlaying();
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Assert.That(Game.State.Run.Level(UpgradeBranch.Team), Is.EqualTo(5));
            Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(crew + 1));
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(wallet - expected));
            Assert.That(m_Truncated, Is.Empty, string.Join("\n", m_Truncated));
            File.WriteAllText(Path.Combine(Shots, "hiring-mine3.json"), OutpostJson.Serialize(new
            {
                mine = 3, firstHire = first.ToString(), rank4Hire = expected.ToString(),
                crewAfterReload = Game.State.Run.Workers.Count, exactUiPayment = true, truncated = m_Truncated,
            }) + "\n");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Factory_TapActivateChestUpgradeAndReload()
        {
            Directory.CreateDirectory(Shots);
            yield return WaitPlaying();
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Game.LoadZone(3);
            yield return WaitFor(() => Game.State.Run.Zone == 3 && Game.Phase == OutpostControllerPhase.Playing && !Hud.InTransition,
                20f, "Mine 3 directly");
            FirstSessionTestUtil.SkipPresentation(Game);
            var mf = Game.Map.Factories.First(f => f.RequiredCardLevel == 2);
            int id = mf.TargetId;
            Game.State.Profile.Cards.Remove(mf.RequiredCardId);
            Game.State.Profile.Elixir = 10000;
            Game.TryGetFactoryWorldPosition(id, out var at);
            Game.FocusWorld(at);
            long hp = Game.Sim.TargetById(id).RemainingHpUnits;
            Game.Input.Tap(new OutpostSelection { Kind = OutpostSelectionKind.Target, Id = id }, at);
            Assert.That(Hud.FactoryDetailOpen, Is.True, "tap goes directly into the factory manager window");
            Assert.That(Hud.FactoryManagerCardId, Is.EqualTo(mf.RequiredCardId));
            Assert.That(Hud.transform.Find("SafeArea/FactoryDetail").GetComponentsInChildren<TMP_Text>()
                .Any(t => t.text == "0/1"), Is.True, "uncollected manager still shows the owned/needed copies");
            Assert.That(Game.Sim.TargetById(id).RemainingHpUnits, Is.EqualTo(hp), "factory tap does not strike chains");
            yield return Capture("factory-01-locked-manager.png");
            Hud.HideFactory();
            long chests = Game.State.Run.Counters.ChestsOpened;
            Game.DebugUnlockFactories();
            yield return WaitFor(() => Game.FactoryOf(id).Status == FactoryStatus.PendingActivation, 5f, "chains removed");
            Assert.That(Game.State.Run.Counters.ChestsOpened, Is.EqualTo(chests), "chain removal does not claim the chest");
            Assert.That(Game.State.Profile.CardLevel(mf.RequiredCardId), Is.Zero);
            Game.FocusWorld(at);
            yield return PlayModeUtil.WaitReal(0.6f);
            Game.Input.Factories.Refresh();
            Assert.That(Game.Input.Factories.DeliverLabelOf(id), Is.EqualTo(Loc.T("factories.activate")));
            yield return Capture("factory-02-activate.png");
            Game.Input.Tap(new OutpostSelection { Kind = OutpostSelectionKind.Target, Id = id }, at);
            yield return Capture("factory-03-activation-manager.png");
            Button FactoryButton(int index) => Hud.transform.Find("SafeArea/FactoryDetail").GetComponentsInChildren<Button>(false)
                .Where(b => b.name == "btn").ElementAt(index);
            FactoryButton(1).onClick.Invoke();
            yield return WaitFor(() => Hud.FactoryChestOpen, 3f, "full-screen chest");
            yield return Capture("factory-04-opening.png", 0.05f);
            yield return Capture("factory-04-opening-lid.png", 0.65f);
            yield return WaitFor(() => Hud.FactoryChestRewardsVisible, 3f, "chest cards revealed");
            Assert.That(Hud.transform.Find("SafeArea/FactoryChest/rewards/content").GetComponentsInChildren<TMP_Text>()
                .Any(t => t.text == OutpostCardText.Name(Game.Configs.Cards.Get(mf.RequiredCardId))), Is.True, "own factory manager is shown among rewards");
            Assert.That(Game.State.Run.Counters.ChestsOpened, Is.EqualTo(chests + 1));
            yield return Capture("factory-05-rewards.png");
            yield return Capture("factory-05-rewards-tall.png", 0.1f, 2400);
            var def = Game.Configs.Cards.Get(mf.RequiredCardId);
            var quote = CardService.Quote(Game.State.Profile, def);
            Assert.That(quote.Level, Is.EqualTo(1));
            Assert.That(quote.CopiesOwned, Is.GreaterThanOrEqualTo(quote.CopiesNeeded), "early factory chest covers first automation");
            Hud.CloseFactoryChest();
            Hud.HideFactory();
            Game.DebugFinishFactoryCycles();
            yield return WaitFor(() => Game.FactoryOf(id).Status == FactoryStatus.Ready, 3f, "manual goods ready");
            long dispatched = Game.FactoryOf(id).Dispatched;
            Game.Input.Tap(new OutpostSelection { Kind = OutpostSelectionKind.Target, Id = id }, at);
            Assert.That(Hud.FactoryDetailOpen, Is.True);
            Assert.That(Game.FactoryOf(id).Dispatched, Is.EqualTo(dispatched), "tapping ready factory opens its card without dispatching");
            yield return Capture("factory-06-upgrade.png");
            yield return Capture("factory-06-upgrade-tall.png", 0.1f, 2400);
            long elixir = Game.State.Profile.Elixir;
            FactoryButton(0).onClick.Invoke();
            yield return null;
            Assert.That(Game.State.Profile.CardLevel(mf.RequiredCardId), Is.EqualTo(2));
            Assert.That(Game.State.Profile.Elixir, Is.EqualTo(elixir - quote.ElixirNeeded));
            Assert.That(Game.FactoryOf(id).Automated, Is.True);
            yield return Capture("factory-07-automated.png");
            long copies = Game.State.Profile.Cards[mf.RequiredCardId].Copies;
            Hud.HideFactory();
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return WaitPlaying();
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Assert.That(Game.State.Profile.CardLevel(mf.RequiredCardId), Is.EqualTo(2));
            Assert.That(Game.State.Profile.Cards[mf.RequiredCardId].Copies, Is.EqualTo(copies));
            Assert.That(Game.FactoryOf(id).Automated, Is.True);
            Assert.That(Game.ActivateFactory(id).Ok, Is.False, "activation cannot give a second chest after reload");
            Assert.That(Game.State.Run.Counters.ChestsOpened, Is.EqualTo(chests + 1));
            Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment, Game.Configs.Cards), Is.Empty);
            Assert.That(m_Truncated, Is.Empty, string.Join("\n", m_Truncated));
            File.WriteAllText(Path.Combine(Shots, "factory-activation.json"), OutpostJson.Serialize(new
            {
                mine = 3, card = mf.RequiredCardId, requiredLevel = mf.RequiredCardLevel,
                chainBreakChestCount = chests, activatedChestCount = Game.State.Run.Counters.ChestsOpened,
                earlyCopies = quote.CopiesOwned, nextUpgradeCopies = quote.CopiesNeeded, upgradeElixir = quote.ElixirNeeded,
                automationAfterUpgrade = Game.FactoryOf(id).Automated, reloadPreserved = true, truncated = m_Truncated,
            }) + "\n");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator NextMine_AutoChoice_StayReopenAndContinue()
        {
            Directory.CreateDirectory(Shots);
            yield return WaitPlaying();
            FirstSessionTestUtil.SkipPresentation(Game);
            Hud.RefreshNow();
            Assert.That(Hud.ResultsOpen, Is.False, "not ready yet");
            Hud.ShowBase();
            yield return Play(600, true, () => Game.State.Run.Phase == RunPhase.ReadyToContinue);
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
            yield return WaitFor(() => !Game.RescueSceneActive, 8f, "rescue animation ends");
            Hud.RefreshNow();
            Assert.That(Hud.ResultsOpen, Is.False, "choice waits for the player's open Base panel");
            Hud.HideBase();
            yield return WaitFor(() => Hud.ResultsOpen, 3f, "automatic next mine choice");
            Assert.That(Hud.transform.Find("SafeArea/Results").GetComponentsInChildren<TMP_Text>()
                .Any(t => t.text == Loc.T("outpost.results.go_next")), Is.True);
            Game.FocusBase();
            yield return Capture("next-mine-choice.png", 0.6f);
            yield return Capture("next-mine-choice-tall.png", 0.1f, 2400);
            string runId = Game.State.Run.RunId;
            var wallet = Game.State.Run.Wallet;
            int rewards = Game.State.Profile.RewardLog.Count;
            Hud.FindTarget(OutpostHud.TargetResultsStay).GetComponent<Button>().onClick.Invoke();
            yield return PlayModeUtil.WaitReal(0.4f);
            Hud.RefreshNow();
            Assert.That(Hud.ResultsOpen, Is.False, "Stay does not auto-open the choice again");
            Assert.That(Game.State.Run.RunId, Is.EqualTo(runId));
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(wallet));
            yield return Capture("next-mine-stay.png");
            Hud.FindTarget(OutpostHud.TargetStripAction).GetComponent<Button>().onClick.Invoke();
            Assert.That(Hud.ResultsOpen, Is.True, "banner reopens the choice");
            yield return null;
            Assert.That(Hud.HandleBack(), Is.True);
            Hud.RefreshNow();
            Assert.That(Hud.ResultsOpen, Is.False, "Back also stays");
            Assert.That(Game.State.Run.RunId, Is.EqualTo(runId));
            Hud.FindTarget(OutpostHud.TargetStripAction).GetComponent<Button>().onClick.Invoke();
            Assert.That(Hud.ResultsOpen, Is.True);
            Assert.That(Game.State.Profile.RewardLog.Count, Is.EqualTo(rewards), "reopening cannot grant rewards twice");
            Hud.FindTarget(OutpostHud.TargetResultsNext).GetComponent<Button>().onClick.Invoke();
            yield return WaitFor(() => Game.Phase == OutpostControllerPhase.Playing && Game.State.Run.Zone == 2 && !Hud.InTransition,
                20f, "confirmed move to Mine 2");
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.Running));
            Assert.That(Hud.ResultsOpen, Is.False, "old choice closes during transition");
            Assert.That(m_Truncated, Is.Empty, string.Join("\n", m_Truncated));
            Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment, Game.Configs.Cards), Is.Empty);
            File.WriteAllText(Path.Combine(Shots, "next-mine-choice.json"), OutpostJson.Serialize(new
            {
                completedTargets = 15, autoChoice = true, waitsForOpenWindow = true, stayKeepsRun = true,
                bannerReopensChoice = true, backStays = true, confirmedZone = Game.State.Run.Zone,
                rewardsBefore = rewards, rewardsAfter = Game.State.Profile.RewardLog.Count, truncated = m_Truncated,
            }) + "\n");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator SceneryNiches_LockedButtons_DimAndStillOpenPreviews()
        {
            Directory.CreateDirectory(Shots);
            yield return WaitPlaying();
            Game.MarkProgress(ProgressionService.CalloutShown);
            if (Hud.RescueCalloutActive) yield return WaitFor(() => !Hud.RescueCalloutActive, 8f, "opening camera ends");
            Hud.RefreshNow();
            var names = new[] { "Store", "Cards", "Equipment", "Market" };
            var features = new[] { ProgressionConfig.Store, ProgressionConfig.Cards, ProgressionConfig.Equipment, ProgressionConfig.SellGoods };
            for (int i = 0; i < names.Length; i++)
            {
                var root = Bottom(names[i]);
                Assert.That(Locked(names[i]), Is.True);
                Assert.That(root.GetComponent<Image>().color.r, Is.InRange(0.75f, 0.9f), names[i] + " face dimmed");
                var padlock = root.Find("content/lock").GetComponent<Image>();
                Assert.That(padlock.color, Is.EqualTo(Color.white), "bright lock");
                Assert.That(padlock.sprite.name, Is.EqualTo("icon_lock"), "full-size native lock");
                Assert.That(padlock.raycastTarget, Is.False, "lock does not intercept the preview tap");
                Assert.That(padlock.preserveAspect, Is.True);
                Assert.That(padlock.rectTransform.sizeDelta, Is.EqualTo(new Vector2(96f, 96f)));
                Assert.That(padlock.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(0f, 24f)), "centered in the icon area");
                Assert.That(root.Find("content/icon").gameObject.activeSelf, Is.False, "no old icon behind lock");
                Assert.That(root.GetComponent<Button>().interactable, Is.True, "unlock preview is clickable");
                yield return TapBottom(names[i]);
                Assert.That(Hud.PreviewFeature, Is.EqualTo(features[i]));
                Hud.HidePreview();
                yield return null;
            }
            Assert.That(Bottom("Base").GetComponent<Image>().color, Is.EqualTo(Color.white));
            var rebuilt = OutpostLevelAdapter.ForRun(Game.Balance, Game.State.Run);
            CollectionAssert.AreEqual(rebuilt.PlayableCells, Game.Map.PlayableCells, "Decor recesses never change navigation");
            Assert.That(Game.Map.RequiredCount, Is.EqualTo(15));
            Assert.That(Game.Board.GetComponentsInChildren<SpriteRenderer>().Any(v => v.sprite != null && v.sprite.name.StartsWith("prop_salvage_")), Is.False,
                "Use matching native pixel art");
            Game.FocusWorld(Game.Board.CellCenter(6, 5));
            yield return Capture("bottom-large-locks-mine1.png", 0.8f);
            yield return Capture("bottom-large-locks-mine1-tall.png", 0.1f, 2400);
            foreach (string feature in features.Take(3)) Game.State.Profile.Progression.Unlocked.Add(feature);
            Hud.RefreshNow();
            foreach (string name in names.Take(3))
            {
                Assert.That(Locked(name), Is.False);
                Assert.That(Bottom(name).GetComponent<Image>().color, Is.EqualTo(Color.white), name + " normal after unlock");
                Assert.That(Bottom(name).Find("content/icon").gameObject.activeSelf, Is.True, name + " icon restored");
            }
            yield return Capture("bottom-large-locks-unlocked.png");
            Assert.That(m_Truncated, Is.Empty, string.Join("\n", m_Truncated));

            Game.SwitchProfile(true);
            yield return WaitPlaying();
            FirstSessionTestUtil.SkipPresentation(Game);
            Game.LoadZone(3);
            yield return WaitFor(() => Game.Phase == OutpostControllerPhase.Playing && Game.State.Run.Zone == 3 && !Hud.InTransition,
                20f, "Mine 3 loaded directly");
            FirstSessionTestUtil.SkipPresentation(Game);
            Hud.CloseAllWindows();
            Hud.RefreshNow();
            Assert.That(Locked("Market"), Is.False);
            Assert.That(Bottom("Market").Find("content/icon").gameObject.activeSelf, Is.True, "Sell Goods icon restored");
            var equipment = Game.Board.GetComponentsInChildren<Transform>().Single(t => t.name == "EquipmentNiches");
            Assert.That(equipment.GetComponentsInChildren<SpriteRenderer>().Length, Is.GreaterThan(0));
            Assert.That(Game.Board.GetComponentsInChildren<SpriteRenderer>().Any(v => v.sprite != null && v.sprite.name.StartsWith("prop_salvage_")), Is.False);
            Game.FocusWorld(Game.Board.CellCenter(Game.Map.Width / 2, Game.Map.Depth / 2));
            yield return Capture("scenery-integrated-mine3.png", 0.7f);
            Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment, Game.Configs.Cards), Is.Empty);
        }

        [UnityTest]
        public IEnumerator DamageBonus_OfferAndTimer_OnPhone()
        {
            OutpostController.SimulateRewardedOffersForTests = true;
            yield return WaitPlaying();
            FirstSessionTestUtil.SkipPresentation(Game);
            Assert.That(Game.DebugShowBonus(RewardedBonusKind.Damage), Is.True);
            yield return null;
            yield return Capture("damage-bonus-offer.png");
            Game.Hud.BonusRect.GetComponentInChildren<Button>().onClick.Invoke();
            yield return null;
            Assert.That(Game.Sim.DamageBonusActive, Is.True);
            Assert.That(Game.Hud.DamageBonusLabel, Is.EqualTo("×2  05:00"));
            Assert.That(Game.DebugShowBonus(RewardedBonusKind.Gems), Is.True);
            yield return Capture("damage-bonus-active.png");
            yield return Capture("damage-bonus-active-tall.png", 0.1f, 2400);
            Game.Hud.ShowSettings();
            yield return Capture("rewarded-ads-settings.png");
            Assert.That(m_Truncated, Is.Empty, string.Join("\n", m_Truncated));
        }

        [UnityTest]
        public IEnumerator Base_Mine1_AllSevenImmediatelyAvailable()
        {
            Directory.CreateDirectory(Shots);
            yield return WaitPlaying();
            Game.MarkProgress(ProgressionService.CalloutShown);
            if (Hud.RescueCalloutActive) yield return WaitFor(() => !Hud.RescueCalloutActive, 8f, "camera callout ends");
            Assert.That(Game.State.Run.Zone, Is.EqualTo(1));
            Assert.That(Game.State.Run.Counters.FirstDeliveryTick, Is.LessThan(0), "before any mining income");
            Hud.ShowBase();
            Hud.RefreshNow();
            yield return null;
            foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
            {
                Assert.That(Hud.BaseRowVisible(branch), Is.True, branch.ToString());
                Assert.That(Game.Quote(branch, BuyMode.One).BlockReason, Is.Not.EqualTo("locked"), branch.ToString());
            }
            Assert.That(Hud.BasePreviewFeature, Is.Null, "no Next upgrade lock row");
            yield return Capture("base-all-open-mine1-top.png");
            Hud.ScrollBaseTo(UpgradeBranch.Bays);
            yield return Capture("base-all-open-mine1-bottom.png");
            Assert.That(Game.State.Run.Counters.FirstDeliveryTick, Is.LessThan(0));
            Hud.HideBase();
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator FirstSession_Mines1To3()
        {
            Directory.CreateDirectory(Shots);
            foreach (string file in Directory.GetFiles(Shots, "*.png")) File.Delete(file);
            Game.EventPresented += OnEvent;
            try
            {
                yield return WaitPlaying();
                var p = Game.State.Profile;
                var cfg = Game.Configs;
                Assert.That(Game.State.Run.Zone, Is.EqualTo(1));
                Assert.That(p.Progression.Version, Is.EqualTo(ProgressionService.CurrentVersion), "new profile on first-session rules");
                Assert.That(Game.Sim.HasRescue, Is.True, "Mine 1 has Mia's cage");
                int startWorkers = Game.State.Run.Workers.Count;

                // ── Шахта 1, начало: цель видна; все пять кнопок на месте, четыре раздела — с замком; иконки Store и Sell Goods.
                Hud.RefreshNow();
                Assert.That(Hud.ObjectiveVisible, Is.True, "objective widget");
                Assert.That(Hud.ObjectiveStep, Does.StartWith("Clear the route"));
                foreach (string name in new[] { "Store", "Cards", "Base", "Equipment", "Market" }) Assert.That(Shown(name), Is.True, name + " on the bar");
                foreach (string name in new[] { "Store", "Cards", "Equipment", "Market" }) Assert.That(Locked(name), Is.True, name + " locked in Mine 1");
                Assert.That(IconOf("Store"), Is.EqualTo("icon_store"), "Store keeps its icon");
                Assert.That(IconOf("Market"), Is.EqualTo("icon_sell_goods"), "Sell Goods crate icon");

                // Кадр клетки в первые секунды: камера у клетки, реплика Mia — единственная подсказка первой сессии.
                yield return WaitFor(() => Hud.RescueCalloutActive, 4f, "rescue callout");
                Assert.That(Hud.GuideHintText, Does.Contain("Mia is trapped"));
                yield return Capture("01-opening-goal.png", 1.0f);
                yield return Capture("01-opening-goal-2400.png", 0.05f, 2400);
                yield return WaitFor(() => !Hud.RescueCalloutActive, 8f, "callout ends");
                Assert.That(p.Progression.WasShown(ProgressionService.CalloutShown), Is.True);
                yield return Capture("02-mine1-locked-buttons.png", 0.8f);

                // Закрытые разделы объясняют себя: Cards и Store — шахта 2 (Store вместе с Cards), Equipment — шахта 3, Sell Goods — завод.
                string mine2 = Loc.F("outpost.unlock.zone", 2);
                yield return TapLocked("Cards", "cards", mine2, "03-locked-cards.png");
                yield return TapLocked("Store", "store", Loc.F("outpost.unlock.with", mine2, "Cards"), "04-locked-store.png");
                yield return TapLocked("Equipment", "equipment", Loc.T("outpost.unlock.blacksmith"), "05-locked-equipment.png");
                yield return TapLocked("Market", "sellgoods", Loc.F("outpost.unlock.factories", 2), "06-locked-sell-goods.png");

                // All seven Base branches are open before the first delivery.
                Hud.ShowBase();
                yield return PlayModeUtil.WaitReal(0.2f);
                foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch))) Assert.That(Hud.BaseRowVisible(branch), Is.True);
                Assert.That(Hud.BasePreviewFeature, Is.Null);
                yield return Capture("07-base-before-delivery.png");
                Hud.HideBase();

                // Активный игрок без подсказок: первая доставка открывает Mining Power, первая покупка — Hire Cat.
                yield return Play(240, true, () => Game.State.Run.Level(UpgradeBranch.Pickaxes) > 0);
                Assert.That(Game.State.Run.Level(UpgradeBranch.Pickaxes), Is.GreaterThan(0), "Mining Power bought without a paw");
                m_Report["firstMiningPowerGameSec"] = Math.Round(GameSeconds, 1);
                Assert.That(p.Progression.IsUnlocked("branch.team"), Is.True, "Hire Cat after the first Mining Power purchase");

                // Котики сами расчищают маршрут и ломают замок; камера к клетке, когда остался замок.
                yield return Play(900, true, () => !Game.Sim.RescuePending || Game.Sim.RescueProgress().Cleared >= Game.Sim.RescueProgress().Total);
                if (Game.Sim.RescuePending && Game.TryGetRescueCageWorldPosition(out var cage)) Game.FocusWorld(cage);
                yield return Play(600, true, () => !Game.Sim.RescuePending);
                Assert.That(Game.Sim.RescuePending, Is.False, "automated cats break the lock");
                Assert.That(m_RescueEvents, Is.EqualTo(1));
                m_Report["rescueGameSec"] = Math.Round(m_RescueTick / (double)OutpostIds.TicksPerSecond, 1);
                if (Game.TryGetRescueCageWorldPosition(out cage)) Game.FocusWorld(cage);
                yield return Capture("08-rescue-scene.png", 0.5f);
                Assert.That(Game.RescueSceneActive || Game.Board.MiaWalking, Is.True);
                yield return WaitFor(() => Game.Board.MiaWalking, 3f, "Mia walks out");
                yield return Capture("09-mia-walks-out.png", 0.25f);
                yield return WaitFor(() => Hud.IntroOpen, 4f, "Mia is free");
                Assert.That(Hud.IntroTitle, Is.EqualTo("Mia is free!"));
                string window = string.Join("\n", Hud.transform.Find("SafeArea/FeatureIntro").GetComponentsInChildren<TMP_Text>().Select(t => t.text));
                Assert.That(window, Does.Contain("run your Store").And.Contain(Loc.F("outpost.unlock.with", mine2, "Cards")), "she runs the Store from Mine 2");
                yield return Capture("10-mia-free.png");
                yield return TapIntroButton("Continue");
                Assert.That(p.Progression.HasCrew("mia"), Is.True);
                Assert.That(p.Progression.WasShown("reveal.mia"), Is.True);
                Assert.That(Game.State.Run.Workers.Count(w => w.Crew != null), Is.Zero, "Mia is not a mining cat");

                yield return FinishMine("11-results-mine1.png", 1800);
                // ── Шахта 2: Store и Cards открыты (без замка), Equipment закрыта; короткое знакомство Cards ждёт кадра клетки Bruno.
                p = Game.State.Profile;
                yield return WaitFor(() => Hud.RescueCalloutActive, 4f, "Bruno rescue callout");
                Assert.That(Hud.GuideHintText, Does.Contain("Bruno can help you equip better tools"));
                yield return Capture("12-bruno-cage-mine2.png", 0.5f);
                yield return WaitFor(() => !Hud.RescueCalloutActive, 8f, "Bruno callout ends");
                yield return WaitFor(() => Hud.IntroOpen, 3f, "short Cards lesson queued after rescue objective");
                Assert.That(Hud.IntroTitle, Is.EqualTo(Loc.T("outpost.cards.support.title")));
                Assert.That(Hud.GuideHintVisible, Is.False, "no competing arrow/callout");
                yield return TapIntroButton("Cards supporting lesson");
                Assert.That(Hud.CardDetailOpen, Is.True, "real card screen");
                Hud.HideCards();
                Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(cfg.Balance.StartWorkers), "start crew size unchanged");
                Assert.That(Game.State.Run.Workers.Count(w => w.Crew != null), Is.Zero, "Mia stays in the Store");
                Assert.That(p.Progression.HasGrant(ProgressionService.GrantCards), Is.True);
                var introCard = cfg.Cards.Get(cfg.Progression.CardsIntro.Card);
                Assert.That(CardService.Quote(p, introCard).CanUpgrade, Is.True, "starter card with resources for one upgrade");
                Hud.RefreshNow();
                Assert.That(Locked("Store") || Locked("Cards"), Is.False, "Store and Cards open in Mine 2");
                Assert.That(Locked("Equipment"), Is.True, "Equipment still locked");
                Assert.That(Locked("Market"), Is.False, "Sell Goods open in Mine 2");
                Assert.That(Badge("Cards"), Is.True, "red dot: the starter card can be upgraded");
                yield return Capture("12-mine2-hud.png");

                // Магазин: Mia за прилавком сверху.
                yield return TapBottom("Store");
                Assert.That(Hud.StoreOpen, Is.True);
                Assert.That(Hud.StoreKeeperVisible, Is.True, "Mia at the top of the Store");
                yield return Capture("13-store-mia.png");
                yield return Capture("13-store-mia-2400.png", 0.05f, 2400);
                Hud.HideStore();

                // Карточки — обычный экран коллекции, без лапки.
                yield return TapBottom("Cards");
                Assert.That(Hud.CardsOpen, Is.True);
                Assert.That(Hud.GuideHintVisible, Is.False);
                yield return Capture("14-cards-mine2.png");
                Hud.HideCards();

                // Bruno's normal rescue is distinct from Mia; completion still waits for delivery.
                yield return Play(1800, true, () => !Game.Sim.RescuePending);
                Assert.That(Game.Sim.RescuePending, Is.False, "automated crew rescued Bruno");
                Assert.That(m_RescueEvents, Is.EqualTo(2), "one rescue event per character");
                if (Game.TryGetRescueCageWorldPosition(out var brunoCage)) Game.FocusWorld(brunoCage);
                yield return Capture("15-bruno-rescue-scene.png", 0.3f);
                yield return WaitFor(() => Game.Board.RescueCharacterWalking, 3f, "Bruno walks out through the open lock");
                yield return Capture("15-bruno-walks-out.png", 0.2f);
                yield return WaitFor(() => Hud.IntroOpen, 5f, "Bruno reveal");
                Assert.That(Hud.IntroTitle, Is.EqualTo("Bruno is free!"));
                yield return Capture("15-bruno-free.png");
                yield return TapIntroButton("Bruno returns to the base service");
                yield return WaitFor(() => Game.Board.RescueCharacterAtCamp, 45f, "Bruno follows the navigation route to base");
                Assert.That(Game.Board.Camp.BlacksmithServiceVisible, Is.True);
                Assert.That(Game.State.Run.Workers.All(worker => worker.Crew == null), Is.True);
                Game.FocusBase();
                yield return Capture("15-bruno-base-anvil.png");
                yield return Play(2400, true, () => Game.State.Run.Phase == RunPhase.ReadyToContinue);
                Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
                Assert.That(Game.State.Run.Zone, Is.EqualTo(2), "Mine 3 has not loaded");
                Assert.That(p.CompletedZones, Does.Contain(2));
                yield return WaitFor(() => Hud.IntroOpen, 5f, "Equipment introduction before Mine 3");
                Assert.That(Hud.IntroTitle, Is.EqualTo(Loc.T("outpost.equip.intro.title")));
                Assert.That(p.Progression.HasGrant(ProgressionService.GrantEquipment), Is.True);
                Assert.That(p.ItemLevel(cfg.Progression.EquipmentIntro.Item), Is.GreaterThanOrEqualTo(1));
                yield return Capture("15-equipment-unlock-mine2.png");
                yield return TapIntroButton("real equipment action");
                Assert.That(Hud.ItemDetailOpen, Is.True);
                Assert.That(Game.State.Run.Zone, Is.EqualTo(2));
                long damageBefore = Game.Sim.Stats.DamageUnits;
                var equipButton = Hud.transform.Find("SafeArea/Equipment/ItemDetail").GetComponentsInChildren<Button>(false)
                    .First(button => button.GetComponentsInChildren<TMP_Text>().Any(text => text.text == Loc.T("outpost.item.equip")));
                Assert.That(equipButton.interactable, Is.True);
                equipButton.onClick.Invoke();
                yield return null;
                Assert.That(p.Equipped[(int)EquipSlot.Pickaxe], Is.EqualTo(cfg.Progression.EquipmentIntro.Item));
                Assert.That(Game.Sim.Stats.DamageUnits, Is.GreaterThan(damageBefore));
                yield return Capture("15-pickaxe-equipped-mine2.png");
                Hud.HideEquipment();
                yield return FinishMine("15-results-mine2.png", 1);
                // Equipment and its actual equipped item persist into Mine 3, with no second introduction/grant.
                p = Game.State.Profile;
                yield return AssertNoTutorial("Mine 3");
                Hud.RefreshNow();
                Assert.That(Locked("Equipment"), Is.False);
                Assert.That(p.Equipped[(int)EquipSlot.Pickaxe], Is.EqualTo(cfg.Progression.EquipmentIntro.Item));
                yield return Capture("16-mine3-hud.png");
                yield return TapBottom("Equipment");
                yield return Capture("17-equipment-mine3.png");
                Hud.HideEquipment();

                Assert.That(m_Windows, Is.EqualTo(new[] { "Mia is free!", Loc.T("outpost.cards.support.title"), "Bruno is free!", Loc.T("outpost.equip.intro.title") }));
                Assert.That(m_StrayHints, Is.Empty, "no hints besides Mia's line at the cage");
                Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment), Is.Empty);
                m_Report["truncated"] = m_Truncated;
            }
            finally
            {
                Game.EventPresented -= OnEvent;
                m_Report["windows"] = m_Windows;
                m_Report["strayHints"] = m_StrayHints;
                m_Report["log"] = m_Log;
                Directory.CreateDirectory(Shots);
                File.WriteAllText(Path.Combine(Shots, "report.json"), OutpostJson.Serialize(m_Report) + "\n");
            }
        }

        /// <summary>Focused Bruno evidence: one mine only, normal crew simulation and real HUD actions.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator Blacksmith_Mine2_RescueWalkBaseAndEquipBeforeMine3()
        {
            Directory.CreateDirectory(Shots);
            yield return WaitPlaying();
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Game.MarkProgress(ProgressionService.CalloutShown);
            if (Hud.RescueCalloutActive) yield return WaitFor(() => !Hud.RescueCalloutActive, 8f, "previous callout ends");
            Assert.That(Game.CanDebugLoadZone, Is.True);
            Game.LoadZone(2);
            yield return WaitFor(() => Game.Phase == OutpostControllerPhase.Playing && Game.State.Run.Zone == 2 && !Hud.InTransition,
                20f, "Mine 2 loaded");
            var p = Game.State.Profile;
            Assert.That(Game.Board.RescueCharacterId, Is.EqualTo("bruno"));
            Assert.That(Game.Sim.RescuePending, Is.True);
            Assert.That(p.Progression.IsUnlocked(ProgressionConfig.Equipment), Is.False);
            Game.EventPresented += OnEvent;
            try
            {
                yield return WaitFor(() => Hud.RescueCalloutActive, 4f, "Bruno objective camera");
                Assert.That(Hud.GuideHintText, Does.Contain("Bruno can help you equip better tools"));
                yield return Capture("bruno-01-cage.png", 0.9f);
                yield return WaitFor(() => !Hud.RescueCalloutActive, 8f, "camera callout ends");
                yield return WaitFor(() => Hud.IntroOpen, 3f, "queued Cards supporting lesson");
                Assert.That(Hud.IntroTitle, Is.EqualTo(Loc.T("outpost.cards.support.title")));
                Assert.That(Hud.GuideHintVisible, Is.False);
                yield return TapIntroButton("Cards supporting lesson");
                Assert.That(Hud.CardDetailOpen, Is.True);
                // The existing card screen performs the upgrade, with no additional gift popup.
                var upgrade = Hud.transform.Find("SafeArea/Cards/CardDetail").GetComponentsInChildren<Button>(false)
                    .First(button => button.GetComponentsInChildren<TMP_Text>().Any(text => text.text == Loc.T("cards.detail.upgrade")));
                Assert.That(upgrade.interactable, Is.True);
                upgrade.onClick.Invoke();
                yield return null;
                Assert.That(p.CardLevel(Game.Configs.Progression.CardsIntro.Card), Is.EqualTo(2));
                Hud.HideCards();
                yield return Play(2400, true, () => !Game.Sim.RescuePending);
                Assert.That(Game.Sim.RescuePending, Is.False);
                Assert.That(p.Progression.Crew.Count(character => character == "bruno"), Is.EqualTo(1));
                Assert.That(m_RescueEvents, Is.EqualTo(1));
                if (Game.TryGetRescueCageWorldPosition(out var cage)) Game.FocusWorld(cage);
                yield return Capture("bruno-02-door-opening.png", 0.15f);
                yield return WaitFor(() => Game.Board.RescueCharacterWalking, 3f, "Bruno walks out");
                yield return Capture("bruno-03-walk.png", 0.2f);
                yield return WaitFor(() => Hud.IntroOpen, 4f, "Bruno reveal after walking starts");
                Assert.That(Hud.IntroTitle, Is.EqualTo("Bruno is free!"));
                yield return TapIntroButton("Bruno reveal");
                yield return WaitFor(() => Game.Board.RescueCharacterAtCamp, 45f, "valid walk back to base");
                Assert.That(Game.Board.Camp.BlacksmithServiceVisible, Is.True);
                Assert.That(Game.State.Run.Workers.All(worker => worker.Crew == null), Is.True);
                Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(Game.Sim.Stats.Workers));
                Game.FocusBase();
                yield return Capture("bruno-04-base-anvil.png");
                yield return Play(2400, true, () => Game.State.Run.Phase == RunPhase.ReadyToContinue);
                Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
                Assert.That(p.CompletedZones, Does.Contain(2));
                Assert.That(Game.State.Run.Zone, Is.EqualTo(2));
                Assert.That(p.Progression.IsUnlocked(ProgressionConfig.Equipment), Is.True);
                // Existing completion chests stay in the normal queue before the equipment introduction.
                for (int i=0;i<8&&Hud.ChestRewardOpen;i++)
                {
                    Hud.HideChestReward();
                    yield return PlayModeUtil.WaitReal(0.15f);
                }
                yield return WaitFor(() => Hud.IntroOpen, 5f, "Equipment immediately after Mine 2");
                Assert.That(Hud.IntroTitle, Is.EqualTo(Loc.T("outpost.equip.intro.title")));
                yield return Capture("bruno-05-equipment-unlock-mine2.png");
                yield return TapIntroButton("actual Equipment item");
                Assert.That(Hud.ItemDetailOpen, Is.True);
                string starter = Game.Configs.Progression.EquipmentIntro.Item;
                Assert.That(Hud.ItemDetailId, Is.EqualTo(starter));
                long damage = Game.Sim.Stats.DamageUnits;
                var equip = Hud.transform.Find("SafeArea/Equipment/ItemDetail").GetComponentsInChildren<Button>(false)
                    .First(button => button.GetComponentsInChildren<TMP_Text>().Any(text => text.text == Loc.T("outpost.item.equip")));
                Assert.That(equip.interactable, Is.True);
                equip.onClick.Invoke();
                yield return null;
                Assert.That(p.Equipped[(int)EquipSlot.Pickaxe], Is.EqualTo(starter));
                Assert.That(Game.Sim.Stats.DamageUnits, Is.GreaterThan(damage));
                Assert.That(Game.State.Run.Zone, Is.EqualTo(2), "equip happened before any Mine 3 transition");
                Assert.That(p.Progression.Grants.Count(receipt => receipt == ProgressionService.GrantEquipment), Is.EqualTo(1));
                Assert.That(Hud.ResultsOpen, Is.False, "results do not cover the equipment action");
                yield return Capture("bruno-06-equipped-mine2.png");
                Hud.HideEquipment();
                Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment), Is.Empty);
            }
            finally
            {
                Game.EventPresented -= OnEvent;
                File.WriteAllText(Path.Combine(Shots, "report-bruno.json"), OutpostJson.Serialize(new
                {
                    mine = Game.State.Run.Zone, rescued = p.Progression.HasCrew("bruno"),
                    completedMine2 = p.CompletedZones.Contains(2), equipment = p.Progression.IsUnlocked(ProgressionConfig.Equipment),
                    equippedPickaxe = p.Equipped[(int)EquipSlot.Pickaxe], windows = m_Windows, log = m_Log,
                }) + "\n");
            }
        }

        /// <summary>
        /// Loading Bays в шахте 6 (debug-профиль, LoadZone 6): ветка открыта, покупка до предела раскладки, на складе одновременно
        /// разгружаются больше котиков, чем мест без ветки; строка «Базы» на пределе — «Max».
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator LoadingBays_Mine6_ParallelUnloading()
        {
            Directory.CreateDirectory(Shots);
            yield return WaitPlaying();
            Game.SwitchProfile(true);
            yield return WaitFor(() => Game.IsDebugProfile && Game.Phase == OutpostControllerPhase.Playing, 20f, "debug profile");
            Game.LoadZone(6);
            yield return WaitFor(() => Game.State.Run.Zone == 6 && Game.Phase == OutpostControllerPhase.Playing && !Game.InTransition, 20f, "mine 6");
            FirstSessionTestUtil.SkipPresentation(Game);
            Assert.That(Game.State.Profile.Progression.IsUnlocked("branch.bays"), Is.True);
            Assert.That(Game.GrantRewardGold(System.Numerics.BigInteger.Pow(10, 12)).Ok, Is.True);
            for (int i = 0; i < 6; i++) Assert.That(Game.Buy(UpgradeBranch.Team, BuyMode.One).Ok, Is.True);
            int spots = Game.Map.DepotCells.Length;
            Assert.That(Game.Buy(UpgradeBranch.Bays, BuyMode.Max).Ok, Is.True);
            Assert.That(Game.State.Run.Slots.Length, Is.EqualTo(spots));
            Assert.That(Game.Quote(UpgradeBranch.Bays, BuyMode.One).NextCost.HasValue, Is.False, "Max");
            Game.FocusBase();
            int maxBusy = 0;
            for (int frame = 0; frame < 2400 && maxBusy < spots; frame++)
            {
                Clock.Advance(0.25);
                if (Hud.ChestRewardOpen) Hud.HideChestReward();
                yield return null;
                maxBusy = Math.Max(maxBusy, Game.State.Run.Slots.Count(slot => slot.WorkerId != 0));
            }
            m_Report["baysSpots"] = spots;
            m_Report["baysMaxBusy"] = maxBusy;
            Assert.That(maxBusy, Is.GreaterThan(OutpostIds.DepotSlotCount), "parallel unloading on the new bays");
            // Снимок — в момент, когда разгружаются больше двух котиков сразу.
            Game.FocusBase();
            for (int frame = 0; frame < 2400 && Game.State.Run.Slots.Count(slot => slot.WorkerId != 0) <= OutpostIds.DepotSlotCount; frame++)
            {
                Clock.Advance(0.1);
                yield return null;
            }
            yield return Capture("18-loading-bays-mine6.png", 0.05f);
            Hud.ShowBase();
            Hud.ScrollBaseTo(UpgradeBranch.Bays);
            yield return Capture("19-base-loading-bays-max.png");
            Hud.HideBase();
            File.WriteAllText(Path.Combine(Shots, "report-bays.json"), OutpostJson.Serialize(m_Report) + "\n");
        }

        /// <summary>
        /// Sell Goods в шахте 2 (debug-профиль, LoadZone 2, «Goods ×100»): товары ждут продажи — подсказки нет (обучение убрано), на
        /// кнопке красная точка; нажатие открывает прежнее окно продажи и ничего не продаёт.
        /// </summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator SellGoods_WithProducts_NoPrompt_OpenSellsNothing()
        {
            Directory.CreateDirectory(Shots);
            yield return WaitPlaying();
            Game.SwitchProfile(true);
            yield return WaitFor(() => Game.IsDebugProfile && Game.Phase == OutpostControllerPhase.Playing, 20f, "debug profile");
            Game.LoadZone(2);
            yield return WaitFor(() => Game.State.Run.Zone == 2 && Game.Phase == OutpostControllerPhase.Playing && !Game.InTransition, 20f, "mine 2");
            FirstSessionTestUtil.SkipPresentation(Game);
            Assert.That(Game.State.Profile.Progression.IsUnlocked(ProgressionConfig.SellGoods), Is.True);
            Game.DebugGrantGoods();
            yield return PlayModeUtil.WaitReal(1f);
            Hud.RefreshNow();
            Assert.That(Hud.GuideHintVisible, Is.False, "no Sell Goods prompt");
            Assert.That(Badge("Market"), Is.True, "red dot: goods are waiting");
            var wallet = Game.State.Run.Wallet;
            var goods = new Dictionary<string, long>(Game.State.Run.Warehouse);
            yield return TapBottom("Market");
            Assert.That(Hud.MarketOpen, Is.True, "existing sales window");
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(wallet), "opening sells nothing");
            Assert.That(Game.State.Run.Warehouse, Is.EquivalentTo(goods));
            yield return Capture("20-sell-goods-with-products.png");
            Hud.HideMarket();
        }
    }
}
