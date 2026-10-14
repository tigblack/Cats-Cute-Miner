using System.Collections;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>
    /// Магазин с настоящим контроллером (ТЗ 10.5): нижняя панель Store · Cards · Base · Equipment · To base, разделы и плитки
    /// (зелёная — бесплатно или своё, синяя — покупка), рекламный сундук с окном карточек и таймером, шансы «i», покупка за гемы
    /// после подтверждения, отказ без гемов, тайм-скип из стартового набора с расчётом и итогом.
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

        [UnityTest]
        public IEnumerator BottomBar_StoreCardsBaseEquipmentToBase()
        {
            Game.Tutorial.Skip();
            yield return null;
            Game.Hud.RefreshNow();
            var bar = Named("BottomBar");
            float previousRight = float.MinValue;
            var corners = new Vector3[4];
            foreach (string name in new[] { "Store", "Cards", "Base", "Equipment", "ToBase" })
            {
                var button = bar.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
                Assert.That(button.gameObject.activeInHierarchy, Is.True, name);
                ((RectTransform)button.transform).GetWorldCorners(corners);
                Assert.That(corners[0].x, Is.GreaterThanOrEqualTo(previousRight - 0.5f), name + " правее предыдущей кнопки и не перекрывает её");
                previousRight = corners[2].x;
            }
            Assert.That(Texts(bar.GetComponentsInChildren<Button>(true).Single(b => b.name == "Store").transform), Does.Contain(Loc.T("outpost.bottom.store")));
        }

        [UnityTest]
        public IEnumerator Store_SectionsTilesColorsAndBack()
        {
            Game.Tutorial.Skip();
            yield return null;
            Game.Hud.GetComponentsInChildren<Button>().Single(b => b.name == "Store").onClick.Invoke();
            yield return null;
            Assert.That(Game.Hud.StoreOpen, Is.True);
            var store = Game.Configs.Store;
            foreach (string section in store.Sections) Assert.That(Named("section_" + section), Is.Not.Null, section);
            foreach (var def in store.Items) Assert.That(Tile(def.Id), Is.Not.Null, def.Id);

            // Стартовый набор (3×1h, 1×4h) — зелёные «USE», 12h — синяя за гемы; рекламный сундук готов — зелёный.
            Assert.That(Sprite(Tile("skip_1h")), Is.EqualTo("ui_btn_green"));
            Assert.That(Texts(TileRoot("skip_1h")), Does.Contain(Loc.T("outpost.store.use")).And.Contain(Loc.F("outpost.store.available", 3)));
            Assert.That(Sprite(Tile("skip_4h")), Is.EqualTo("ui_btn_green"));
            Assert.That(Sprite(Tile("skip_12h")), Is.EqualTo("ui_btn_blue"));
            Assert.That(Texts(TileRoot("skip_12h")), Does.Contain("350"));
            Assert.That(Sprite(Tile("chest_wooden")), Is.EqualTo("ui_btn_green"));
            Assert.That(Texts(TileRoot("chest_wooden")), Does.Contain(Loc.T("outpost.store.open")));
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
            Game.Tutorial.Skip();
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
            Assert.That(Sprite(Tile("chest_wooden")), Is.EqualTo("ui_btn_green"), "через 4 часа сундук снова бесплатный");
        }

        [UnityTest]
        public IEnumerator GemPurchase_ConfirmThenSpend_RefusedWithoutGems()
        {
            Game.Tutorial.Skip();
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
        public IEnumerator TimeSkip_OwnedShort_CalculatesAndShowsResult()
        {
            Game.Tutorial.Skip();
            yield return null;
            Game.Hud.ShowStore();
            yield return null;
            var credited = Game.State.Run.TotalCredited;
            Tile("skip_1h").onClick.Invoke();
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.OfflineCatchUp));
            Assert.That(Game.SkipMinutesInProgress, Is.EqualTo(10));
            yield return WaitPlaying();
            var skip = Game.PendingSkip;
            Assert.That(skip, Is.Not.Null);
            Assert.That(skip.SkipMinutes, Is.EqualTo(10));
            Assert.That(skip.AppliedMs, Is.EqualTo(HourMs / 6));
            Assert.That(skip.GoldDelivered.Sign, Is.GreaterThan(0), "котики доставили золото за 10 минут");
            Assert.That(Game.State.Run.TotalCredited, Is.GreaterThan(credited));
            Assert.That(Game.State.Profile.ConsumableCount("skip_1h"), Is.EqualTo(2));
            Game.Hud.RefreshNow();
            Assert.That(Game.Hud.SkipResultOpen, Is.True);
            Assert.That(Texts(Named("TimeSkip")), Does.Contain(OutpostUiText.Money(skip.GoldDelivered)));
            Assert.That(Game.Hud.HandleBack(), Is.True);
            Assert.That(Game.Hud.SkipResultOpen, Is.False);
            Assert.That(Game.PendingSkip, Is.Null);
            Game.Hud.RefreshNow();
            Assert.That(Texts(TileRoot("skip_1h")), Does.Contain(Loc.F("outpost.store.available", 2)));
        }
    }
}
