using System.Collections;
using System.Linq;
using CatMiners.Core.Outpost;
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
    /// <summary>
    /// Магазин с настоящим контроллером (ТЗ 10.5): нижняя панель Store · Cards · Base · Equipment · Sell Goods, Mia за прилавком, разделы и плитки
    /// (зелёная — бесплатно или своё, синяя — покупка), рекламный сундук с окном карточек и таймером, шансы «i», покупка за гемы
    /// после подтверждения, отказ без гемов, платный тайм-скип с расчётом и итогом.
    /// </summary>
    public sealed class OutpostStoreUiTests : OutpostCardsFixture
    {
        const long HourMs = 3_600_000L;

        Transform Named(string name) => Game.Hud.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        Transform TileRoot(string id) =>
            Game.Hud.GetComponentsInChildren<Transform>(true).Single(t => t.name == id && t.parent != null && t.parent.name.StartsWith("grid_"));

        Button Tile(string id) => TileRoot(id).GetComponentsInChildren<Button>(true).First(b => b.name == "tile");

        static string Sprite(Button b) => b.GetComponent<Image>().sprite.name;

        string Texts(Transform root) => string.Join("\n", root.GetComponentsInChildren<TextMeshProUGUI>().Select(t => t.text));

        Button DialogButton(string window, int index) =>
            Named(window).GetComponentsInChildren<Button>(true).Where(b => b.name == "btn").ElementAt(index);

        long CardCopies() => Game.State.Profile.Cards.Values.Sum(c => c.Level > 0 ? 1 + c.Copies : 0);

        /// <summary>
        /// Решение владельца 16.10.2026 (прежний BottomBar_StoreCardsBaseMarketEquipmentToBase, ТЗ 2.2 §6.2): «To base» убрана,
        /// кнопки в порядке Store · Cards · Base · Equipment · Market, без перекрытий, «Base» ровно по центру панели и не уже
        /// 44 пикселей арта; подписи боковых кнопок не мельче FontSmall и без многоточия. Первая сессия (Docs/Rework/FIRST_SESSION.md,
        /// решение владельца 21.10.2026): все пять кнопок на месте с шахты 1; закрытые Store, Cards, Equipment и Sell Goods — с
        /// маленьким замком, нажимаются и открывают окно требования (Store — «Unlocks in Mine 2 with Cards»); иконка Store прежняя;
        /// Market называется Sell Goods, иконка — ящик с товарами. Открытый раздел — без замка.
        /// </summary>
        [UnityTest]
        public IEnumerator BottomBar_StoreCardsBaseEquipmentMarket()
        {
            yield return null;
            Game.Hud.RefreshNow();
            Canvas.ForceUpdateCanvases();
            var bar = Named("BottomBar");
            Assert.That(bar.GetComponentsInChildren<Button>(true).Where(b => b.transform.parent == bar).Select(b => b.name)
                .OrderBy(n => n).ToArray(), Is.EqualTo(new[] { "Base", "Cards", "Equipment", "Market", "Store" }), "пять кнопок панели");
            AssertBar(bar, new[] { "Store", "Cards", "Base", "Equipment", "Market" });
            Button Bottom(string name) => bar.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
            Assert.That(Texts(Bottom("Store").transform), Does.Contain(Loc.T("outpost.bottom.store")));
            Assert.That(Bottom("Store").transform.Find("content/icon").GetComponent<Image>().sprite.name, Is.EqualTo("icon_store"), "иконка Store прежняя");
            Assert.That(Texts(Bottom("Market").transform), Does.Contain("Sell Goods"));
            Assert.That(Bottom("Market").transform.Find("content/icon").GetComponent<Image>().sprite.name, Is.EqualTo("icon_sell_goods"), "ящик с товарами");
            Assert.That(Game.Hud.FindTarget(OutpostHud.TargetMarket), Is.EqualTo(Bottom("Market").transform));

            // Шахта 1 нового профиля: все четыре раздела закрыты — замок, кнопка нажимается и называет требование.
            var expected = new (string Button, string Feature, string Text)[]
            {
                ("Store", "store", Loc.F("outpost.unlock.with", Loc.F("outpost.unlock.zone", 2), "Cards")),
                ("Cards", "cards", Loc.F("outpost.unlock.zone", 2)),
                ("Equipment", "equipment", Loc.F("outpost.unlock.zone", 3)),
                ("Market", "sellgoods", Loc.F("outpost.unlock.factories", 2)),
            };
            foreach (var (name, feature, text) in expected)
            {
                var button = Bottom(name);
                Assert.That(button.transform.Find("content/lock").gameObject.activeSelf, Is.True, name + ": замок");
                Assert.That(button.interactable, Is.True, name + ": закрытая кнопка объясняет требование по тапу");
                button.onClick.Invoke();
                yield return null;
                Assert.That(Game.Hud.PreviewFeature, Is.EqualTo(feature), name);
                Assert.That(Game.Hud.StoreOpen || Game.Hud.CardsOpen || Game.Hud.EquipmentOpen || Game.Hud.MarketOpen, Is.False, name + ": рабочий экран не открыт");
                Assert.That(Texts(Named("FeaturePreview")), Does.Contain(text), name + ": требование");
                Game.Hud.HidePreview();
            }

            // Разделы открыты (как в шахте 3): замков Store, Cards и Equipment нет, кнопки открывают свои экраны.
            var p = Game.State.Profile;
            foreach (string id in new[] { "cards", "store", "equipment" })
                if (!p.Progression.Unlocked.Contains(id)) p.Progression.Unlocked.Add(id);
            Game.Hud.RefreshNow();
            Canvas.ForceUpdateCanvases();
            foreach (string name in new[] { "Store", "Cards", "Equipment" })
                Assert.That(Bottom(name).transform.Find("content/lock").gameObject.activeSelf, Is.False, name + ": без замка");
            AssertBar(bar, new[] { "Store", "Cards", "Base", "Equipment", "Market" });
            Bottom("Store").onClick.Invoke();
            yield return null;
            Assert.That(Game.Hud.StoreOpen, Is.True);
            Assert.That(Game.Hud.PreviewOpen, Is.False);
        }

        /// <summary>
        /// Mia за прилавком (решение владельца 21.10.2026): пока она в клетке, верх магазина прежний; спасённая Mia — портрет в рамке
        /// и реплика под шапкой «Store», строка валют и список — ниже, без перекрытий.
        /// </summary>
        [UnityTest]
        public IEnumerator Store_MiaAtTheTopAfterRescue()
        {
            yield return null;
            FirstSessionTestUtil.SkipPresentation(Game);
            var p = Game.State.Profile;
            Assert.That(p.Progression.HasCrew("mia"), Is.False);
            Game.Hud.ShowStore();
            yield return null;
            Assert.That(Game.Hud.StoreKeeperVisible, Is.False, "Mia ещё в клетке");
            Assert.That(Game.State.Profile.Progression.WasShown(ProgressionService.StoreWelcomeShown), Is.False);
            Game.Hud.HideStore();

            ProgressionService.Rescue(p, "mia");
            Game.Hud.ShowStore();
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(Game.Hud.StoreKeeperVisible, Is.True, "спасённая Mia сверху магазина");
            var keeper = Named("keeper");
            Assert.That(keeper.Find("frame/portrait").GetComponent<Image>().sprite.name, Does.StartWith("mia_portrait_"));
            Assert.That(Texts(keeper), Does.Contain(Loc.T("outpost.store.keeper")));
            Assert.That(Game.Hud.StoreWelcomeVisible, Is.True, "first greeting is visible");
            Assert.That(Game.State.Profile.Progression.WasShown(ProgressionService.StoreWelcomeShown), Is.True);
            var keeperCorners = new Vector3[4];
            var countsCorners = new Vector3[4];
            ((RectTransform)keeper).GetWorldCorners(keeperCorners);
            ((RectTransform)keeper.parent.Find("currencies")).GetWorldCorners(countsCorners);
            Assert.That(countsCorners[1].y, Is.LessThanOrEqualTo(keeperCorners[0].y + 0.5f), "валюты — под строкой Mia");
            ((RectTransform)keeper.parent.Find("header")).GetWorldCorners(countsCorners);
            Assert.That(keeperCorners[1].y, Is.LessThanOrEqualTo(countsCorners[0].y + 0.5f), "строка Mia — под шапкой");
            yield return PlayModeUtil.WaitReal(1f);
            Game.Hud.RefreshNow();
            Assert.That(Game.Hud.StoreWelcomeVisible, Is.True, "still visible before two seconds");
            yield return PlayModeUtil.WaitReal(1.1f);
            Assert.That(Game.Hud.StoreWelcomeVisible, Is.False, "automatically hides after two real seconds");
            Assert.That(Game.Hud.StoreKeeperVisible, Is.True, "Mia's portrait remains");
            Assert.That(Game.Hud.HandleBack(), Is.True);
            Assert.That(Game.Hud.StoreOpen, Is.False);
            Game.Hud.ShowStore();
            yield return null;
            Assert.That(Game.Hud.StoreWelcomeVisible, Is.False, "reopening does not repeat the greeting");
            Game.Hud.HideStore();
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return WaitPlaying();
            Assert.That(Game.State.Profile.Progression.WasShown(ProgressionService.StoreWelcomeShown), Is.True, "first-show receipt was saved immediately");
            Game.Hud.ShowStore();
            yield return null;
            Assert.That(Game.Hud.StoreKeeperVisible, Is.True);
            Assert.That(Game.Hud.StoreWelcomeVisible, Is.False, "reload does not repeat the greeting");
        }

        [UnityTest]
        public IEnumerator Store_WelcomeClosedEarly_DoesNotRestart()
        {
            FirstSessionTestUtil.SkipPresentation(Game);
            ProgressionService.Rescue(Game.State.Profile, "mia");
            Game.Hud.ShowStore();
            Assert.That(Game.Hud.StoreWelcomeVisible, Is.True);
            Game.Hud.HideStore();
            Game.Hud.ShowStore();
            yield return null;
            Assert.That(Game.Hud.StoreWelcomeVisible, Is.False, "closing early still counts as the first show");
        }

        /// <summary>Видимые кнопки панели слева направо без перекрытий, «Base» по центру и не уже минимума, подписи не мельче FontSmall.</summary>
        static void AssertBar(Transform bar, string[] order)
        {
            float previousRight = float.MinValue;
            var corners = new Vector3[4];
            foreach (string name in order)
            {
                var button = bar.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
                Assert.That(button.gameObject.activeInHierarchy, Is.True, name);
                ((RectTransform)button.transform).GetWorldCorners(corners);
                Assert.That(corners[0].x, Is.GreaterThanOrEqualTo(previousRight - 0.5f), name + " правее предыдущей кнопки и не перекрывает её");
                previousRight = corners[2].x;
                if (name == "Base")
                {
                    Assert.That(((RectTransform)button.transform).rect.width, Is.GreaterThanOrEqualTo(OutpostHud.BaseMinWidth - 0.5f), "ширина «Base»");
                    var barCorners = new Vector3[4];
                    ((RectTransform)bar).GetWorldCorners(barCorners);
                    Assert.That((corners[0].x + corners[2].x) / 2f, Is.EqualTo((barCorners[0].x + barCorners[2].x) / 2f).Within(1f), "«Base» по центру панели");
                }
                else
                {
                    var label = button.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == "label");
                    label.ForceMeshUpdate();
                    Assert.That(label.fontSize, Is.GreaterThanOrEqualTo(PixelUi.FontSmall - 0.01f), name + ": подпись не мельче FontSmall");
                    Assert.That(label.isTextTruncated, Is.False, name + ": подпись без многоточия");
                }
            }
        }

        [UnityTest]
        public IEnumerator Store_SectionsTilesColorsAndBack()
        {
            yield return null;
            // Магазин открывается вместе с карточками (шахта 2); здесь он открыт, как после шахты 1.
            Game.State.Profile.Progression.Unlocked.AddRange(new[] { "cards", "store" });
            Game.Hud.RefreshNow();
            Game.Hud.GetComponentsInChildren<Button>().Single(b => b.name == "Store").onClick.Invoke();
            yield return null;
            Assert.That(Game.Hud.StoreOpen, Is.True);
            var store = Game.Configs.Store;
            foreach (string section in store.Sections) Assert.That(Named("section_" + section), Is.Not.Null, section);
            foreach (var def in store.Items.Where(d => d.Currency != StoreCurrency.Ad)) Assert.That(Tile(def.Id), Is.Not.Null, def.Id);
            foreach (var def in store.Items.Where(d => d.Currency == StoreCurrency.Ad)) Assert.That(Named(def.Id), Is.Null, def.Id);

            // Все тайм-скипы покупаются за гемы, стартового запаса нет.
            var skipIds = new[] { "skip_1h", "skip_4h", "skip_12h" };
            var skipPrices = new[] { 50, 150, 350 };
            for (int i = 0; i < skipIds.Length; i++)
            {
                string id = skipIds[i];
                Assert.That(Game.State.Profile.ConsumableCount(id), Is.Zero, id);
                Assert.That(Sprite(Tile(id)), Is.EqualTo("ui_btn_blue"), id);
                Assert.That(Texts(TileRoot(id)), Does.Contain(skipPrices[i].ToString()).And.Not.Contain(Loc.T("outpost.store.use")), id);
                Assert.That(TileRoot(id).Find("available").gameObject.activeSelf, Is.False, id);
            }
            Assert.That(Sprite(Tile("chest_iron")), Is.EqualTo("ui_btn_blue"));
            // Пакеты гемов без скидки (14 октября 2026): обычное количество и цена, без бейджа «+N%», зачёркнутого числа и «Available».
            foreach (var def in store.InSection("gems"))
            {
                var tile = TileRoot(def.Id);
                Assert.That(Texts(tile), Does.Contain(StoreTileView.FormatAmount(def.Amount)).And.Contain(Loc.F("outpost.store.price_usd", def.PriceUsd)), def.Id);
                foreach (string part in new[] { "bonus", "available" })
                    Assert.That(tile.Find(part).gameObject.activeSelf, Is.False, def.Id + ": " + part);
                Assert.That(tile.GetComponentsInChildren<TextMeshProUGUI>().Any(t => t.name == "oldAmount"), Is.False, def.Id + ": зачёркнутое количество");
            }
            Assert.That(Texts(Named("currencies")), Does.Contain(StoreTileView.FormatAmount(Game.State.Profile.Elixir)));

            Assert.That(Game.Hud.HandleBack(), Is.True);
            Assert.That(Game.Hud.StoreOpen, Is.False);
        }

        [UnityTest]
        public IEnumerator AdChest_RewardWindowDropRatesThenCooldown()
        {
            // Preserve deferred reward coverage without showing ads in ordinary Editor play.
            OutpostController.SimulateRewardedOffersForTests = true;
            yield return null;
            Game.Hud.ShowStore();
            yield return null;

            TileRoot("chest_wooden").GetComponentsInChildren<Button>(true).Single(b => b.name == "info").onClick.Invoke();
            Assert.That(Named("DropRates").gameObject.activeInHierarchy, Is.True);
            Assert.That(Texts(Named("DropRates")), Does.Contain("80%").And.Contain("0.02%"));
            Assert.That(Game.Hud.HandleBack(), Is.True);
            Assert.That(Named("DropRates").gameObject.activeInHierarchy, Is.False);
            Assert.That(Game.Hud.StoreOpen, Is.True);

            long copies = CardCopies();
            int chestsInRun = Game.State.Run.Counters.ChestsOpened;
            Tile("chest_wooden").onClick.Invoke();
            yield return null;
            Assert.That(Game.Hud.ChestRewardOpen, Is.True, "окно выпавших карточек");
            Assert.That(CardCopies(), Is.EqualTo(copies + 1), "шахта 1: обычный сундук — 7 × 0,1 → 1 карточка");
            Assert.That(Game.State.Run.Counters.ChestsOpened, Is.EqualTo(chestsInRun), "сундук магазина — не находка шахты");
            Assert.That(Game.State.Profile.StoreCooldownUntilUtcMs["chest_wooden"], Is.EqualTo(Clock.UtcNowMs + 4 * HourMs));
            Assert.That(Game.Hud.HandleBack(), Is.True);
            Assert.That(Game.Hud.ChestRewardOpen, Is.False);
            Assert.That(Game.Hud.StoreOpen, Is.True);

            Game.Hud.RefreshNow();
            Assert.That(Sprite(Tile("chest_wooden")), Is.EqualTo("ui_btn_dark"), "на таймере — тёмная плитка");
            copies = CardCopies();
            Tile("chest_wooden").onClick.Invoke();
            yield return null;
            Assert.That(Game.Hud.ChestRewardOpen, Is.False);
            Assert.That(CardCopies(), Is.EqualTo(copies));

            Clock.UtcNowMs += 4 * HourMs;
            Game.Hud.RefreshNow();
            Assert.That(Sprite(Tile("chest_wooden")), Is.EqualTo("ui_btn_gold"), "через 4 часа реклама снова доступна — цвет как у Double Income");
        }

        [UnityTest]
        public IEnumerator OrdinaryEditor_NoAdOffersOrRewardShortcuts_PaidStoreRemains()
        {
#if !UNITY_EDITOR
            if (CatMiners.Runtime.Integrations.GameSdkConfiguration.AdsEnabled)
                Assert.Ignore("Applies to platforms/builds without the rewarded-ad provider.");
#endif
            Assert.That(OutpostController.RewardedOffersSupported, Is.False);
            Game.Hud.ShowStore();
            yield return null;
            Assert.That(Named("chest_wooden"), Is.Null, "no chest-for-ad tile");
            Assert.That(Game.Hud.GetComponentsInChildren<Image>().Any(i => i.sprite != null && i.sprite.name == "icon_ad_tv"), Is.False);
            Assert.That(Game.Configs.Store.InSection("gems").Count(), Is.EqualTo(6));
            foreach (var def in Game.Configs.Store.InSection("gems")) Assert.That(Tile(def.Id), Is.Not.Null);
            long copies = CardCopies(), elixir = Game.State.Profile.Elixir, gems = Game.State.Profile.Gems;
            var wallet = Game.State.Run.Wallet;
            Assert.That(Game.StoreBuy("chest_wooden").Ok, Is.False, "hidden placement cannot grant via direct command");
            Assert.That(Game.State.Profile.StoreCooldownUntilUtcMs.ContainsKey("chest_wooden"), Is.False);
            Assert.That(Game.DebugShowBonus(RewardedBonusKind.Gold), Is.False);
            Assert.That(Game.ClaimBonus().Ok, Is.False);
            Assert.That(Game.ClaimOfflineDouble().Ok, Is.False);
            Assert.That(CardCopies(), Is.EqualTo(copies));
            Assert.That(Game.State.Profile.Elixir, Is.EqualTo(elixir));
            Assert.That(Game.State.Profile.Gems, Is.EqualTo(gems));
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(wallet));
            Game.Hud.HideStore();
            OutpostController.BonusTimeScale = 0.01f;
            try
            {
                for (int i = 0; i < 20; i++) { Clock.Advance(0.25); yield return null; }
                Assert.That(Game.BonusOffer.Valid, Is.False, "no scheduled ad bonus");
                Assert.That(Game.Hud.BonusVisible, Is.False);
            }
            finally { OutpostController.BonusTimeScale = 1f; }
            Game.Hud.ShowOffline(new OfflineSummary { GoldDelivered = 100 });
            Assert.That(Game.Hud.OfflineOpen, Is.True);
            Assert.That(Named("Offline").GetComponentsInChildren<Image>().Any(i => i.sprite != null && i.sprite.name == "icon_ad_tv"), Is.False,
                "offline summary has no watch-ad button");
            Assert.That(Named("Offline").GetComponentsInChildren<Button>().Count(), Is.GreaterThan(0), "offline income can still be collected");
        }

        [UnityTest]
        public IEnumerator GemPurchase_ConfirmThenSpend_RefusedWithoutGems()
        {
            yield return null;
            Game.Hud.ShowStore();
            yield return null;
            Assert.That(Game.State.Profile.Gems, Is.Zero);
            Assert.That(Sprite(Tile("elixir_bottle")), Is.EqualTo("ui_btn_blue"));
            Tile("elixir_bottle").onClick.Invoke();
            Assert.That(Game.Hud.ModalOpen, Is.False, "без гемов — отказ без подтверждения");

            // Пакет гемов (тестовый режим платежей): подтверждение, Cancel ничего не меняет, покупка — обычное количество.
            Tile("gems_bag").onClick.Invoke();
            Assert.That(Game.Hud.ModalOpen, Is.True, "покупка за деньги — после подтверждения");
            DialogButton("Modal", 0).onClick.Invoke();
            Assert.That(Game.Hud.ModalOpen, Is.False);
            Assert.That(Game.State.Profile.Gems, Is.Zero, "Cancel ничего не покупает");
            Tile("gems_bag").onClick.Invoke();
            DialogButton("Modal", 1).onClick.Invoke();
            yield return null;
            Assert.That(Game.State.Profile.Gems, Is.EqualTo(1200));
            Game.Hud.RefreshNow();
            Assert.That(Texts(Named("currencies")), Does.Contain("1,200"));

            long elixir = Game.State.Profile.Elixir;
            Tile("elixir_bottle").onClick.Invoke();
            Assert.That(Game.Hud.ModalOpen, Is.True, "трата гемов — после подтверждения");
            DialogButton("Modal", 1).onClick.Invoke();
            yield return null;
            Assert.That(Game.State.Profile.Gems, Is.EqualTo(1200 - 60));
            Assert.That(Game.State.Profile.Elixir, Is.EqualTo(elixir + 1000));
        }

        [UnityTest]
        public IEnumerator TimeSkip_PaidShort_ConfirmsSpendsAndShowsResult()
        {
            yield return null;
            Game.Hud.ShowStore();
            yield return null;
            Game.State.Profile.Gems = 50;
            Game.Hud.RefreshNow();
            var credited = Game.State.Run.TotalCredited;
            Tile("skip_1h").onClick.Invoke();
            Assert.That(Game.Hud.ModalOpen, Is.True, "покупка скипа — после подтверждения");
            Assert.That(Game.State.Profile.Gems, Is.EqualTo(50), "до подтверждения гемы не списаны");
            Assert.That(Game.PendingSkip, Is.Null);
            DialogButton("Modal", 1).onClick.Invoke();
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.OfflineCatchUp));
            Assert.That(Game.SkipMinutesInProgress, Is.EqualTo(10));
            yield return WaitPlaying();
            var skip = Game.PendingSkip;
            Assert.That(skip, Is.Not.Null);
            Assert.That(skip.SkipMinutes, Is.EqualTo(10));
            Assert.That(skip.AppliedMs, Is.EqualTo(HourMs / 6));
            Assert.That(skip.GoldDelivered.Sign, Is.GreaterThan(0), "котики доставили золото за 10 минут");
            Assert.That(Game.State.Run.TotalCredited, Is.GreaterThan(credited));
            Assert.That(Game.State.Profile.Gems, Is.Zero, "списаны 50 гемов");
            Assert.That(Game.State.Profile.ConsumableCount("skip_1h"), Is.Zero);
            Game.Hud.RefreshNow();
            Assert.That(Game.Hud.SkipResultOpen, Is.True);
            Assert.That(Texts(Named("TimeSkip")), Does.Contain(OutpostUiText.Money(skip.GoldDelivered)));
            Assert.That(Game.Hud.HandleBack(), Is.True);
            Assert.That(Game.Hud.SkipResultOpen, Is.False);
            Assert.That(Game.PendingSkip, Is.Null);
            Game.Hud.RefreshNow();
            Assert.That(Sprite(Tile("skip_1h")), Is.EqualTo("ui_btn_blue"));
            Assert.That(Texts(TileRoot("skip_1h")), Does.Contain("50").And.Not.Contain(Loc.T("outpost.store.use")));
            Assert.That(TileRoot("skip_1h").Find("available").gameObject.activeSelf, Is.False);
        }
    }
}
