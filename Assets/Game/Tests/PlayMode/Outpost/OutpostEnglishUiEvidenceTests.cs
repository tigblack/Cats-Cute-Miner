using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CatMiners.Core.Outpost;
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
    /// Снимки английского интерфейса для проверки глазами (перевод 12 октября 2026): HUD, «Base», «Equipment», окно предмета
    /// (надет, сравнение с надетым, рюкзак, максимум — в 9:16 и 9:20), «Cards», настройки, «While you were away» (кнопка
    /// рекламы с телевизором, 9:16 и 9:20) и итоги шахты; отдельно — магазин (<see cref="StoreScreens"/>) и заводы с рынком
    /// (<see cref="FactoryScreens"/>, ТЗ 2.2). По каждому снимку
    /// в Docs/Reports/english-ui/text.tsv записываются подписи, обрезанные многоточием (тест падает, если они есть), и
    /// подписи, уменьшенные автоподбором мельче FontSmall (только для сведения).
    /// Генератор артефактов, отдельно: <c>Tools/unity.ps1 -Mode PlayMode -Target Ci -TestFilter OutpostEnglishUiEvidenceTests</c>.
    /// </summary>
    [Explicit("генератор снимков Docs/Reports/english-ui/*.png")]
    public sealed class OutpostEnglishUiEvidenceTests : OutpostCardsFixture
    {
        static string Shots => Path.Combine(PlayModeUtil.ReportsDir(), "english-ui");
        readonly List<string> m_Truncated = new List<string>();
        readonly List<string> m_Small = new List<string>();

        IEnumerator Advance(double seconds)
        {
            while (seconds > 0)
            {
                double step = Math.Min(0.25, seconds);
                Clock.Advance(step);
                seconds -= step;
                yield return null;
            }
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null && t.GetComponent<Canvas>() == null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>Снимок 1080×<paramref name="height"/> вместе с HUD; масштаб Canvas — как у CanvasScaler (match 0,5).</summary>
        IEnumerator Capture(string name, int height = 1920)
        {
            var camera = Game.Rig.Cam;
            var canvas = Game.Hud.GetComponent<Canvas>();
            var scaler = Game.Hud.GetComponent<CanvasScaler>();
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
                yield return PlayModeUtil.WaitReal(0.35f);
                Game.Hud.RefreshNow();
                Canvas.ForceUpdateCanvases();
                Game.Hud.RefreshNow();
                Canvas.ForceUpdateCanvases();
                foreach (var t in Game.Hud.GetComponentsInChildren<TMP_Text>())
                {
                    if (string.IsNullOrEmpty(t.text)) continue;
                    t.ForceMeshUpdate();
                    string line = name + "\t" + PathOf(t.transform) + "\t" + t.fontSize.ToString("0.#") + "\t" + t.text.Replace("\n", "\\n");
                    if (t.isTextTruncated) m_Truncated.Add(line);
                    if (t.fontSize < PixelUi.FontSmall - 0.01f) m_Small.Add(line);
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

        [UnityTest]
        public IEnumerator SettingsScreens()
        {
            Game.Hud.CloseAllWindows();
            Game.State.Profile.PlayTimeMs = 125 * 60_000L;
            Game.Hud.ShowSettings();
            yield return Capture("13-settings-play-time.png");
            yield return Capture("13-settings-play-time-2400.png", 2400);
            Game.Hud.GetComponentsInChildren<Button>().Single(b => b.name == "ResetProgress").onClick.Invoke();
            yield return Capture("14-settings-reset.png");
            yield return Capture("14-settings-reset-2400.png", 2400);
            Assert.That(m_Truncated, Is.Empty, string.Join("\n", m_Truncated));
        }

        [UnityTest]
        public IEnumerator EnglishScreens()
        {
            Directory.CreateDirectory(Shots);
            foreach (string file in Directory.GetFiles(Shots, "*.png")) File.Delete(file);
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Game.Hud.CloseAllWindows();
            yield return Advance(4);
            yield return Capture("01-hud.png");

            Game.Hud.ShowBase();
            yield return Capture("02-base.png");
            Game.Hud.CloseAllWindows();

            // Все 16 предметов и запас деталей; надета обычная кирка ур. 2 (как на снимке пользователя), обычная шляпа — ур. 20.
            Game.GrantTestKit();
            yield return null;
            Assert.That(Game.Equip("pickaxe.common").Ok, Is.True);
            Assert.That(Game.UpgradeItem("pickaxe.common").Ok, Is.True);
            for (int level = 2; level <= Game.Configs.Equipment.MaxItemLevel; level++)
                Assert.That(Game.UpgradeItem("hat.common").Ok, Is.True, "hat.common → ур. " + level);
            Game.Hud.ShowEquipment();
            yield return Capture("03-equipment.png");
            foreach (var (id, file) in new[]
                     {
                         ("pickaxe.common", "04-item-equipped"), ("pickaxe.legendary", "05-item-compare"),
                         ("backpack.rare", "06-item-backpack"), ("hat.common", "07-item-max"),
                     })
            {
                Game.Hud.ShowItem(id);
                Assert.That(Game.Hud.ItemDetailId, Is.EqualTo(id));
                yield return Capture(file + ".png");
                yield return Capture(file + "-2400.png", 2400);
                Game.Hud.HideItem();
            }
            Game.Hud.CloseAllWindows();

            Game.GrantTestCards();
            yield return null;
            Game.Hud.ShowCards();
            yield return Capture("08-cards.png");
            var card = Game.Configs.Cards.Cards.First(d => CardService.Quote(Game.State.Profile, d).CanUpgrade);
            Game.Hud.ShowCard(card.Id);
            yield return Capture("09-card-detail.png");
            Game.Hud.CloseAllWindows();

            Game.Hud.ShowSettings();
            yield return Capture("10-settings.png");
            Game.Hud.CloseAllWindows();

            // «While you were away»: настоящий офлайн — пауза приложения и три часа на часах игры.
            Game.SendMessage("OnApplicationPause", true);
            Clock.Advance(3 * 3600);
            Game.SendMessage("OnApplicationPause", false);
            yield return WaitPlaying();
            for (int frame = 0; frame < 600 && !Game.Hud.OfflineOpen; frame++) yield return null;
            Assert.That(Game.Hud.OfflineOpen, Is.True, "окно «Пока вас не было»");
            yield return Capture("12-offline.png");
            yield return Capture("12-offline-2400.png", 2400);
            Game.DismissOffline();
            yield return null;

            // За три часа офлайна шахта могла завершиться сама — тогда итоги уже открыты.
            if (!Game.Hud.ResultsOpen && Game.State.Run.Phase != RunPhase.ReadyToContinue) Game.CompleteStageNow();
            yield return null;
            yield return WaitPlaying();
            for (int frame = 0; frame < 600 && !Game.Hud.ResultsOpen; frame++)
            {
                // Сундук выхода с новой карточкой — окно перед итогами.
                if (Game.Hud.ChestRewardOpen)
                {
                    yield return Capture("11a-chest-exit.png");
                    Game.Hud.HandleBack();
                }
                yield return null;
            }
            Assert.That(Game.Hud.ResultsOpen, Is.True, "итоги шахты открылись");
            yield return Capture("11-results.png");

            var tsv = new StringBuilder("kind\tshot\tpath\tfontSize\ttext\n");
            foreach (string line in m_Truncated) tsv.Append("truncated\t").Append(line).Append('\n');
            foreach (string line in m_Small) tsv.Append("small\t").Append(line).Append('\n');
            File.WriteAllText(Path.Combine(Shots, "text.tsv"), tsv.ToString());
            Assert.That(m_Truncated, Is.Empty, "подписи, обрезанные многоточием:\n" + string.Join("\n", m_Truncated));
        }

        Transform Named(string name) => Game.Hud.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        Button Tile(string id) => Game.Hud.GetComponentsInChildren<Transform>(true)
            .Single(t => t.name == id && t.parent != null && t.parent.name.StartsWith("grid_"))
            .GetComponentsInChildren<Button>(true).First(b => b.name == "tile");

        Button ChildButton(Transform root, string name) => root.GetComponentsInChildren<Button>(true).First(b => b.name == name);

        /// <summary>
        /// Магазин (ТЗ 10.5): верх (сундуки и тайм-скипы), эликсир, гемы, шансы «i», рекламный сундук, подтверждение,
        /// мифический сундук («+N more»), плитки после покупок (таймер, бонус израсходован) и итог тайм-скипа — 9:16 и 9:20.
        /// Снимки s*.png и store-text.tsv; обрезанные подписи роняют тест.
        /// </summary>
        [UnityTest]
        public IEnumerator StoreScreens()
        {
            Directory.CreateDirectory(Shots);
            foreach (string file in Directory.GetFiles(Shots, "s*.png")) File.Delete(file);
            m_Truncated.Clear();
            m_Small.Clear();
            Game.Hud.CloseAllWindows();
            yield return Advance(4);

            Game.Hud.ShowStore();
            yield return Capture("s01-store-top.png");
            yield return Capture("s01-store-top-2400.png", 2400);
            ChildButton(Named("currencies").Find("elixir"), "plus").onClick.Invoke();
            yield return Capture("s02-store-elixir.png");
            ChildButton(Named("currencies").Find("gems"), "plus").onClick.Invoke();
            yield return Capture("s03-store-gems.png");
            yield return Capture("s03-store-gems-2400.png", 2400);
            Game.Hud.HideStore();

            Game.Hud.ShowStore();
            ChildButton(Tile("chest_iron").transform, "info").onClick.Invoke();
            yield return Capture("s04-drop-rates.png");
            Game.Hud.HandleBack();

            // События команды контроллер показывает в следующем кадре.
            Tile("chest_wooden").onClick.Invoke();
            yield return null;
            Assert.That(Game.Hud.ChestRewardOpen, Is.True, "рекламный сундук открыт");
            yield return Capture("s05-chest-wooden.png");
            yield return Capture("s05-chest-wooden-2400.png", 2400);
            Game.Hud.HandleBack();

            Assert.That(Game.StoreBuy("gems_pile").Ok, Is.True, "тестовый режим платежей");
            Game.Hud.RefreshNow();
            Tile("chest_mythical").onClick.Invoke();
            Assert.That(Game.Hud.ModalOpen, Is.True, "подтверждение траты гемов");
            yield return Capture("s06-confirm.png");
            Named("Modal").GetComponentsInChildren<Button>(true).Where(b => b.name == "btn").ElementAt(1).onClick.Invoke();
            yield return null;
            Assert.That(Game.Hud.ChestRewardOpen, Is.True, "мифический сундук открыт");
            yield return Capture("s07-chest-mythical.png");
            yield return Capture("s07-chest-mythical-2400.png", 2400);
            Game.Hud.HandleBack();
            Game.Hud.HideStore();
            Game.Hud.ShowStore();
            yield return Capture("s08-store-after.png");

            Tile("skip_4h").onClick.Invoke();
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.OfflineCatchUp), "тайм-скип 4 ч из стартового набора");
            yield return WaitPlaying();
            for (int frame = 0; frame < 600 && !Game.Hud.SkipResultOpen; frame++) yield return null;
            Assert.That(Game.Hud.SkipResultOpen, Is.True, "итог тайм-скипа");
            yield return Capture("s09-skip-result.png");
            yield return Capture("s09-skip-result-2400.png", 2400);

            var tsv = new StringBuilder("kind\tshot\tpath\tfontSize\ttext\n");
            foreach (string line in m_Truncated) tsv.Append("truncated\t").Append(line).Append('\n');
            foreach (string line in m_Small) tsv.Append("small\t").Append(line).Append('\n');
            File.WriteAllText(Path.Combine(Shots, "store-text.tsv"), tsv.ToString());
            Assert.That(m_Truncated, Is.Empty, "подписи, обрезанные многоточием:\n" + string.Join("\n", m_Truncated));
        }

        /// <summary>Три формата снимка (ТЗ 2.2 §16.2): 9:16 (1080×1920), 9:19,5 (1080×2340) и 9:20 (1080×2400).</summary>
        IEnumerator Capture3(string name)
        {
            yield return Capture(name + "-1920.png");
            yield return Capture(name + "-2340.png", 2340);
            yield return Capture(name + "-2400.png", 2400);
        }

        Transform MarketWindow => Game.Hud.GetComponentsInChildren<Transform>(true)
            .First(t => t.name == "Market" && t.parent != null && t.parent.name == "SafeArea");

        /// <summary>Выбрать завод заново (DEV-кнопки пересоздают симуляцию и снимают выбор) и подвести к нему камеру.</summary>
        IEnumerator SelectFactory(int id)
        {
            Game.SelectTarget(id);
            if (Game.TryGetFactoryWorldPosition(id, out var world)) Game.Rig.FocusOnSmooth(new Vector2(world.x, world.z));
            yield return PlayModeUtil.WaitReal(0.6f);
            Game.Hud.RefreshNow();
            Assert.That(Game.Hud.InfoVisible, Is.True, "сведения завода " + id);
        }

        /// <summary>
        /// Заводы и рынок (ТЗ 2.2 §6.2–§6.3, §7.1, §8, §16.2; WP5b), f*.png в 9:16, 9:19,5 и 9:20: шахта 140 (15 заводов) —
        /// сведения завода Locked, Working (нижняя панель с Market и красной точкой), Ready с «Deliver», AUTO; окно рынка с 15
        /// товарами (верх и низ списка, после продажи — тост и монеты); окно карточки завода (не получена и получена) и сетка
        /// «Cards»; шахта 2 — рынок с пустым складом (серая «Sell all»), итоги со строкой «Goods sold», сведения Stopped и рынок
    /// завершённой шахты. Обрезанные подписи
        /// пишутся в factory-text.tsv и дописываются в text.tsv; тест падает, если они есть.
        /// </summary>
        [UnityTest]
        public IEnumerator FactoryScreens()
        {
            Directory.CreateDirectory(Shots);
            foreach (string file in Directory.GetFiles(Shots, "f*.png")) File.Delete(file);
            m_Truncated.Clear();
            m_Small.Clear();
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Game.LoadZone(140);
            yield return null;
            Assert.That(Game.State.Run.Zone, Is.EqualTo(140));
            Assert.That(Game.Map.Factories.Length, Is.EqualTo(15), "шахта 140 — 15 заводов");
            Game.Hud.CloseAllWindows();
            yield return Advance(1);
            int id = Game.Map.Factories[0].TargetId;

            yield return SelectFactory(id);
            yield return Capture3("f01-factory-locked");
            Game.ClearSelection();

            // Окно карточки завода (§7.1) до открытия цепей: требуемая карточка ещё не получена — «(not collected)», вторая
            // карточка того же типа называет требуемую.
            var paired = Game.Map.Factories.First(m =>
                Game.Configs.Factories.Get(m.TypeId).UncommonCardId != null && Game.State.Profile.CardLevel(m.RequiredCardId) < m.RequiredCardLevel);
            var pairedType = Game.Configs.Factories.Get(paired.TypeId);
            string other = paired.RequiredCardId == pairedType.CommonCardId ? pairedType.UncommonCardId : pairedType.CommonCardId;
            Game.Hud.ShowCard(paired.RequiredCardId);
            yield return Capture3("f02-card-factory-required");
            Game.Hud.ShowCard(other);
            yield return Capture3("f03-card-factory-other");
            Game.Hud.CloseAllWindows();

            Game.DebugGrantGoods();
            Game.DebugUnlockFactories();
            yield return null;
            yield return Advance(3);
            yield return SelectFactory(id);
            yield return Capture3("f04-factory-working");
            Game.ClearSelection();
            // После сундуков цепей у игрока могли появиться копии: «needs Lv N (you have Lv k)».
            Game.Hud.ShowCard(Game.Map.Factories[0].RequiredCardId);
            yield return Capture3("f05-card-factory-after-chests");
            Game.Hud.CloseAllWindows();

            Game.DebugFinishFactoryCycles();
            yield return null;
            yield return SelectFactory(id);
            yield return Capture3("f06-factory-ready");

            Game.DebugAutomateAll();
            yield return null;
            yield return SelectFactory(id);
            yield return Capture3("f07-factory-auto");
            Game.ClearSelection();

            Game.Hud.ShowMarket();
            Assert.That(Game.Hud.MarketRowCount, Is.EqualTo(15), "окно рынка: 15 товаров шахты 140");
            yield return Capture3("f08-market-140");
            MarketWindow.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition = 0f;
            yield return Capture3("f09-market-140-bottom");
            MarketWindow.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition = 1f;
            Game.Hud.RefreshNow();
            // Рынок — окно кита (решение владельца 15.10.2026): строки в Box/body/viewport.
            ChildButton(MarketWindow.Find("Box/body/viewport/content/good0"), "sell").onClick.Invoke();
            yield return null;
            yield return null;
            yield return Capture("f10-market-sold-1920.png");
            Game.Hud.HideMarket();

            // Полученная карточка завода этой шахты с улучшением (строка сравнения и блок завода), сетка «Cards».
            Game.GrantTestCards();
            yield return null;
            var owned = Game.Configs.Cards.Cards.FirstOrDefault(d => d.IsFactoryOutput && CardService.Quote(Game.State.Profile, d).CanUpgrade &&
                Game.Map.Factories.Any(f => f.TypeId == OutpostCardText.FactoryType(d)))
                ?? Game.Configs.Cards.Cards.First(d => d.IsFactoryOutput && Game.State.Profile.CardLevel(d.Id) > 0);
            Game.Hud.ShowCard(owned.Id);
            yield return Capture3("f11-card-factory-owned");
            Game.Hud.CloseAllWindows();
            Game.Hud.ShowCards();
            yield return Capture3("f12-cards-grid");
            Game.Hud.CloseAllWindows();

            // Шахта 2: продажа, завершение — итоги с «Goods sold», завод Stopped, рынок завершённой шахты.
            Game.LoadZone(2);
            yield return null;
            Game.DebugUnlockFactories();
            Game.DebugGrantGoods();
            yield return null;
            Assert.That(Game.SellGoods(null).Ok, Is.True, "продажа всего склада");
            // Склад пуст: «Sell all» — просто серая кнопка без причины (решение владельца 15.10.2026), окно по одной строке.
            for (int frame = 0; frame < 10 && Game.Hud.ChestRewardOpen; frame++)
            {
                Game.Hud.HandleBack();
                yield return null;
            }
            Game.Hud.ShowMarket();
            yield return Capture3("f17-market-nothing");
            Game.Hud.HideMarket();
            Game.DebugGrantGoods();
            yield return null;
            Game.CompleteStageNow();
            for (int frame = 0; frame < 6000 && !Game.Hud.ResultsOpen; frame++)
            {
                if (Game.Hud.ChestRewardOpen) Game.Hud.HandleBack();
                yield return null;
            }
            Assert.That(Game.Hud.ResultsOpen, Is.True, "итоги шахты 2");
            Assert.That(Game.State.Run.GoodsSoldGold.Sign, Is.GreaterThan(0));
            yield return Capture3("f13-results-goods");
            Game.Hud.CloseAllWindows();
            yield return SelectFactory(Game.Map.Factories[0].TargetId);
            yield return Capture3("f14-factory-stopped");
            Game.ClearSelection();
            Game.Hud.ShowMarket();
            yield return Capture3("f15-market-complete");
            Game.Hud.CloseAllWindows();
            yield return Capture3("f16-hud-complete");

            var tsv = new StringBuilder();
            foreach (string line in m_Truncated) tsv.Append("truncated\t").Append(line).Append('\n');
            foreach (string line in m_Small) tsv.Append("small\t").Append(line).Append('\n');
            File.WriteAllText(Path.Combine(Shots, "factory-text.tsv"), "kind\tshot\tpath\tfontSize\ttext\n" + tsv);
            string common = Path.Combine(Shots, "text.tsv");
            if (!File.Exists(common)) File.WriteAllText(common, "kind\tshot\tpath\tfontSize\ttext\n");
            File.AppendAllText(common, tsv.ToString());
            Assert.That(m_Truncated, Is.Empty, "подписи, обрезанные многоточием:\n" + string.Join("\n", m_Truncated));
        }

        /// <summary>
        /// Сундук шахты с новой карточкой (ТЗ 10.5): свежий debug-профиль, шахта 5, котики работают ×100 до первого такого
        /// сундука — окно награды в 9:16 и 9:20 (m01-chest-mine*.png; StoreScreens удаляет только s*.png). Обрезанные подписи роняют тест.
        /// </summary>
        [UnityTest]
        public IEnumerator MineChestScreens()
        {
            Directory.CreateDirectory(Shots);
            foreach (string file in Directory.GetFiles(Shots, "m*.png")) File.Delete(file);
            m_Truncated.Clear();
            m_Small.Clear();
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Game.LoadZone(5);
            yield return null;
            Assert.That(Game.State.Run.Zone, Is.EqualTo(5));
            Game.SimSpeed = 100;
            for (int frame = 0; frame < 6000 && !Game.Hud.ChestRewardOpen; frame++)
            {
                Clock.Advance(0.25);
                yield return null;
            }
            Game.SimSpeed = 1;
            Assert.That(Game.Hud.ChestRewardOpen, Is.True, "сундук шахты с новой карточкой — окно");
            Assert.That(Game.Hud.ChestRewardTileCount, Is.GreaterThan(0));
            yield return Capture("m01-chest-mine.png");
            yield return Capture("m01-chest-mine-2400.png", 2400);
            Assert.That(Game.Hud.HandleBack(), Is.True);
            Assert.That(Game.Hud.ChestRewardOpen, Is.False);
            Assert.That(m_Truncated, Is.Empty, "подписи, обрезанные многоточием:\n" + string.Join("\n", m_Truncated));
        }
    }
}
