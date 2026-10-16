using System.Collections;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>
    /// Рынок, сведения завода и окно карточки завода с настоящим контроллером (ТЗ 2.2 §6.2–§6.3, §7.1, §8, §16.2; пакет WP5b):
    /// кнопка Market в шахте 1 неактивна с причиной, окно без заводов; в шахте 2 — красная точка при товаре на складе, строка
    /// товара и «Sell all» продают за золото кошелька (тост «+X gold»), пустой склад — «Nothing to sell» у строки, Back закрывает;
    /// тап по складу открывает рынок; в завершённой шахте — «Mine complete» у строки; сведения завода в состояниях
    /// Locked/Working/Ready/Stopped и «Deliver»; блок завода в окне карточки. Решения владельца 15.10.2026: рынок — окно кита, не
    /// на весь экран; недоступная «Sell all» — серая кнопка без причины и без тоста.
    /// </summary>
    public sealed class OutpostMarketUiTests : OutpostCardsFixture
    {
        Transform Named(string name) => Game.Hud.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        Button BottomButton(string name) =>
            Game.Hud.GetComponentsInChildren<Button>(true).Single(b => b.name == name && b.transform.parent != null && b.transform.parent.name == "BottomBar");

        /// <summary>Окно рынка — ребёнок SafeArea (кнопка панели тоже называется «Market»).</summary>
        Transform MarketWindow => Game.Hud.GetComponentsInChildren<Transform>(true)
            .First(t => t.name == "Market" && t.parent != null && t.parent.name == "SafeArea");

        Button MarketButton(string name) => MarketWindow.GetComponentsInChildren<Button>(true).First(b => b.name == name);

        Button RowSell(int index) => Named("good" + index).GetComponentsInChildren<Button>(true).First(b => b.name == "sell");

        static string Texts(Transform root) => string.Join("\n", root.GetComponentsInChildren<TextMeshProUGUI>(true).Select(t => t.text));

        string ToastText()
        {
            var toast = Game.Hud.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == "Toast");
            return toast.gameObject.activeInHierarchy ? toast.text : null;
        }

        /// <summary>Тап по недоступной кнопке: PointerClick доходит до EventTrigger причины (как касание игрока).</summary>
        static void Tap(Button button) =>
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);

        static string FixedPart(string key) => Loc.T(key).Split('{')[0].Trim();

        /// <summary>Иконка кнопки Market нижней панели: весы или замок (до первой шахты с заводами).</summary>
        string MarketIcon() => BottomButton("Market").transform.Find("content/icon").GetComponent<Image>().sprite.name;

        /// <summary>
        /// Недоступная «Sell all» (решение владельца 15.10.2026): серая кнопка кита с подписью «Sell all» — без причины на ней;
        /// тап не показывает тост.
        /// </summary>
        void AssertSellAllGray(Button sellAll)
        {
            Assert.That(sellAll.interactable, Is.False);
            var label = sellAll.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == "label");
            Assert.That(label.text, Is.EqualTo(Loc.T("outpost.market.sell_all")), "на серой «Sell all» нет причины");
            Assert.That(label.color, Is.EqualTo(PixelUi.TextDim), "подпись выключенной кнопки кита");
            Game.Hud.Toast(null);
            Tap(sellAll);
            Assert.That(ToastText(), Is.Null, "тап по серой «Sell all» — без тоста");
        }

        static Rect WorldRect(Transform t)
        {
            var corners = new Vector3[4];
            ((RectTransform)t).GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        /// <summary>
        /// Рынок — окно, не на весь экран (решение владельца 15.10.2026): затемнение на весь safe area, рамка с полями по бокам;
        /// рамка с планкой-заголовком и крестиком — между верхней полосой HUD и нижней панелью кнопок; «Sell all» — в ряду кнопок
        /// внизу рамки. Холст тестов — Overlay: мировые углы — пиксели экрана.
        /// </summary>
        void AssertMarketIsWindow()
        {
            var root = (RectTransform)MarketWindow;
            var box = (RectTransform)root.Find("Box");
            Assert.That(box, Is.Not.Null, "рамка окна кита");
            Assert.That(root.GetComponent<Image>().raycastTarget, Is.True, "затемнение ловит касания поля");
            Assert.That(box.rect.width, Is.LessThan(root.rect.width * 0.97f), "поля по бокам рамки");
            Assert.That(box.Find("close").GetComponent<Button>(), Is.Not.Null, "крестик закрытия");
            Assert.That(box.Find("buttons/sellAll").GetComponent<Button>(), Is.Not.Null, "«Sell all» внизу рамки");
            var top = WorldRect(Named("TopBar"));
            var bottom = WorldRect(Named("BottomBar"));
            var frame = WorldRect(box);
            Assert.That(WorldRect(box.Find("header")).yMax, Is.LessThanOrEqualTo(top.yMin + 0.5f), "планка окна — под верхней полосой HUD");
            Assert.That(WorldRect(box.Find("close")).yMax, Is.LessThanOrEqualTo(top.yMin + 0.5f), "крестик — под верхней полосой HUD");
            Assert.That(frame.yMin, Is.GreaterThanOrEqualTo(bottom.yMax - 0.5f), "рамка — над нижней панелью кнопок");
        }

        TextMeshProUGUI InfoText(string name) => Named("Info").GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == name);

        IEnumerator DebugMine(int zone)
        {
            Loc.ResetReports();
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Game.LoadZone(zone);
            yield return null;
            Assert.That(Game.State.Run.Zone, Is.EqualTo(zone));
            Game.Hud.RefreshNow();
        }

        [UnityTest]
        public IEnumerator Mine1_MarketDisabledWithReason_WindowWithoutFactories()
        {
            yield return null;
            Game.Hud.RefreshNow();
            Assert.That(Game.Map.Factories.Length, Is.Zero, "в шахте 1 заводов нет");
            var market = BottomButton("Market");
            Assert.That(market.interactable, Is.False);
            // Решение владельца 16.10.2026: до первой шахты с заводами на кнопке замок, тап называет шахту открытия (шахта 2, ТЗ 2.2 §2).
            Assert.That(Game.MarketUnlockZone, Is.EqualTo(2), "заводы начинаются с шахты 2");
            Assert.That(MarketIcon(), Is.EqualTo("icon_lock"));
            Tap(market);
            Assert.That(ToastText(), Is.EqualTo(Loc.F("outpost.market.locked", 2)));

            Game.Hud.ShowMarket();
            yield return null;
            Assert.That(Game.Hud.MarketOpen, Is.True);
            Assert.That(Game.Hud.MarketRowCount, Is.Zero);
            var empty = MarketWindow.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == "empty");
            Assert.That(empty.gameObject.activeInHierarchy, Is.True);
            Assert.That(empty.text, Does.Contain(FactoryText.Reason("no-factories")));
            Assert.That(MarketButton("sellAll").interactable, Is.False);
            AssertSellAllGray(MarketButton("sellAll"));
            AssertMarketIsWindow();
            Assert.That(Game.Hud.HandleBack(), Is.True);
            Assert.That(Game.Hud.MarketOpen, Is.False);
            Assert.That(Game.Hud.AnyWindowOpen, Is.False);
        }

        [UnityTest]
        public IEnumerator Mine2_SellRowAndSellAll_GoldToastAndBack()
        {
            yield return DebugMine(2);
            var market = BottomButton("Market");
            Assert.That(market.interactable, Is.True, "склад пуст — окно открывается, продажа недоступна");
            Assert.That(MarketIcon(), Is.EqualTo("icon_market"), "с шахты 2 замка нет (решение владельца 16.10.2026)");
            var dot = market.transform.Find("badge");
            Assert.That(dot.gameObject.activeSelf, Is.False, "товара нет — точки нет");

            Game.DebugUnlockFactories();
            Game.DebugGrantGoods();
            yield return null;
            Game.Hud.RefreshNow();
            Assert.That(dot.gameObject.activeSelf, Is.True, "товар на складе — красная точка");

            market.onClick.Invoke();
            yield return null;
            Assert.That(Game.Hud.MarketOpen, Is.True);
            // Дополнение теста (ТЗ 2.2 §6.1, решение владельца 15.10.2026): склад называется в интерфейсе Warehouse — шапка окна
            // «Market · Warehouse». Правка теста (решение владельца 15.10.2026 «рынок — не на весь экран, окно»): шапка — планка
            // окна кита Box/header/title (раньше header/title экрана во весь safe area); рамка уже safe area, над ней видно поле.
            Assert.That(MarketWindow.Find("Box/header/title").GetComponent<TextMeshProUGUI>().text,
                Is.EqualTo(Loc.F("factories.market_title", Loc.T("outpost.market.title"), Loc.T("factories.warehouse"))));
            AssertMarketIsWindow();
            var run = Game.State.Run;
            var goods = run.FactoryRules.Goods.Select(g => g.GoodId).Where(g => FactoryQueries.HasGood(run, g)).ToArray();
            Assert.That(Game.Hud.MarketRowCount, Is.EqualTo(goods.Length));
            for (int i = 0; i < goods.Length; i++) Assert.That(Game.Hud.MarketRowGood(i), Is.EqualTo(goods[i]), "строки по номеру типа");
            // Обновление открытого окна без изменений не выделяет память (кэш по значению, цены — один раз на шахту).
            Game.Hud.RefreshNow();
            Game.Hud.RefreshNow();
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) Game.Hud.RefreshNow();
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Debug.Log("[OutpostMarketUiTests] 50 × RefreshNow с открытым рынком: " + allocated + " байт");
            Assert.That(allocated, Is.LessThan(2048), "окно рынка без изменений не выделяет память");

            string good = Game.Hud.MarketRowGood(0);
            long count = Game.GoodsCount(good);
            var price = Game.GoodsPrice(good);
            var wallet = Game.State.Run.Wallet;
            Assert.That(count, Is.EqualTo(OutpostController.DebugGoodsPerPress));
            var row = Named("good0");
            Assert.That(Texts(row), Does.Contain(FactoryText.GoodName(good)).And.Contain(Loc.F("outpost.market.each", OutpostUiText.Money(price))));
            Assert.That(Texts(Named("marketCounts")), Does.Contain(Loc.F("outpost.market.total", OutpostUiText.Money(Game.WarehouseValue(null)))));
            RowSell(0).onClick.Invoke();
            var gold = count * price;
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(wallet + gold), "продажа строки — золото в кошелёк");
            Assert.That(Game.State.Run.GoodsSoldGold, Is.EqualTo(gold));
            Assert.That(Game.GoodsCount(good), Is.Zero);
            string expected = Loc.F("outpost.toast.goods_sold", OutpostUiText.Money(gold));
            for (int frame = 0; frame < 60 && ToastText() != expected; frame++) yield return null;
            Assert.That(ToastText(), Is.EqualTo(expected), "тост продажи");
            Game.Hud.RefreshNow();
            Assert.That(RowSell(0).interactable, Is.False);
            Assert.That(Texts(row), Does.Contain(Loc.F("outpost.market.count", "0")));
            Tap(RowSell(0));
            Assert.That(ToastText(), Is.EqualTo(FactoryText.Reason("no-goods")));

            // «Sell all» — весь склад; после продажи кнопки неактивны с причиной «Nothing to sell».
            Game.DebugGrantGoods();
            yield return null;
            Game.Hud.RefreshNow();
            Assert.That(Game.Hud.MarketOpen, Is.True, "окно остаётся открытым после пересоздания симуляции");
            var total = Game.WarehouseValue(null);
            Assert.That(total.Sign, Is.GreaterThan(0));
            var sellAll = MarketButton("sellAll");
            Assert.That(sellAll.interactable, Is.True);
            Assert.That(Texts(sellAll.transform), Does.Contain(Loc.F("outpost.market.sell_all_value", OutpostUiText.Money(total))));
            wallet = Game.State.Run.Wallet;
            sellAll.onClick.Invoke();
            Assert.That(Game.State.Run.Wallet, Is.EqualTo(wallet + total));
            Assert.That(Game.WarehouseValue(null).IsZero, Is.True);
            Game.Hud.RefreshNow();
            Assert.That(sellAll.interactable, Is.False);
            // Правка теста (решение владельца 15.10.2026): недоступная «Sell all» — просто серая неработающая кнопка «Sell all»,
            // без причины на ней и без тоста по тапу (раньше на кнопке и тостом — «Nothing to sell»). Строки «Sell» — как раньше.
            AssertSellAllGray(sellAll);
            Assert.That(dot.gameObject.activeSelf, Is.False);

            Assert.That(Game.Hud.HandleBack(), Is.True);
            Assert.That(Game.Hud.MarketOpen, Is.False);
            CollectionAssert.IsEmpty(Loc.FormatErrors, string.Join("\n", Loc.FormatErrors));
        }

        [UnityTest]
        public IEnumerator WarehouseTap_OpensMarket()
        {
            yield return DebugMine(2);
            Game.SelectWarehouse();
            yield return null;
            yield return null;
            Assert.That(Game.Hud.MarketOpen, Is.True, "тап по складу (Selection = Warehouse) открывает рынок");
            Assert.That(Game.Selection.Kind, Is.EqualTo(OutpostSelectionKind.None), "выбор снят");
            Game.Hud.HandleBack();
            Assert.That(Game.Hud.MarketOpen, Is.False);
        }

        [UnityTest]
        public IEnumerator FactoryInfo_LockedWorkingReadyDeliver()
        {
            // Шахта 3: Dairy требует ca002 ур. 2 (приложение B, п. 2) — после сундука цепей карточка не выше ур. 1, завод ручной.
            yield return DebugMine(3);
            var f = Game.State.Run.Factories.Single(x => x.TypeId == "amethyst");
            int id = f.TargetId;
            Game.SelectTarget(id);
            Game.Hud.RefreshNow();
            Assert.That(Game.Hud.InfoVisible, Is.True);
            Assert.That(InfoText("title").text, Is.EqualTo(Loc.F("outpost.factory.title", FactoryText.FactoryName(f.GoodId), FactoryText.GoodName(f.GoodId))));
            // Правка теста (ТЗ 2.2 §2 п. 1, решение владельца 15.10.2026): первая строка закрытого завода — «Dairy — locked. Break the
            // chains» (factories.locked_hint) вместо прежнего ключа outpost.factory.locked «Locked · break the chains» (удалён).
            Assert.That(InfoText("body").text, Does.Contain(Loc.F("factories.locked_hint", FactoryText.FactoryName(f.GoodId))).And.Contain(FixedPart("outpost.factory.chains")));
            Assert.That(InfoText("body").text, Does.Contain("Auto delivery: "));
            Assert.That(Named("Info").Find("portrait/icon").GetComponent<Image>().sprite.name, Is.EqualTo("icon_factory"));

            Game.DebugUnlockFactories();
            yield return null;
            Game.SelectTarget(id);
            Game.Hud.RefreshNow();
            f = Game.FactoryOf(id);
            Assert.That(f.Status, Is.EqualTo(FactoryStatus.Working));
            Assert.That(InfoText("body").text, Does.Contain(FixedPart("outpost.factory.next")).And.Contain(FixedPart("outpost.factory.output")));

            Game.DebugFinishFactoryCycles();
            yield return null;
            Game.SelectTarget(id);
            Game.Hud.RefreshNow();
            f = Game.FactoryOf(id);
            Assert.That(f.Automated, Is.False, "Dairy шахты 3 — ручной режим");
            Assert.That(f.Status, Is.EqualTo(FactoryStatus.Ready));
            Assert.That(InfoText("body").text, Does.Contain(FixedPart("outpost.factory.auto_needs")).Or.Contain(FixedPart("outpost.factory.auto_missing")));
            var deliver = Named("Info").GetComponentsInChildren<Button>(true).First(b => b.name == "deliver");
            Assert.That(deliver.gameObject.activeInHierarchy, Is.True, "в Ready — кнопка «Deliver»");
            Assert.That(deliver.interactable, Is.True);
            Assert.That(Texts(deliver.transform), Does.Contain(Loc.F("outpost.factory.deliver", f.ReadyUnits)));
            int manual = Game.State.Run.Counters.ManualDeliveries;
            deliver.onClick.Invoke();
            Assert.That(Game.FactoryOf(id).Status, Is.EqualTo(FactoryStatus.Working), "партия ушла, следующий цикл");
            Assert.That(Game.State.Run.Counters.ManualDeliveries, Is.EqualTo(manual + 1));
            Assert.That(Game.State.Run.PipeBatches.Count, Is.GreaterThan(0));

            // AUTO: строка «Auto delivery: on» и значок у портрета.
            Game.DebugAutomateAll();
            yield return null;
            Game.SelectTarget(id);
            Game.Hud.RefreshNow();
            Assert.That(InfoText("body").text, Does.Contain(Loc.T("outpost.factory.auto_on")));
            Assert.That(Named("Info").Find("portrait/auto").gameObject.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator MineComplete_MarketAndSellDisabled_FactoryStopped()
        {
            yield return DebugMine(2);
            Game.DebugGrantGoods();
            yield return null;
            Game.CompleteStageNow();
            for (int frame = 0; frame < 6000 && (Game.Phase != OutpostControllerPhase.Playing || Game.State.Run.Phase != RunPhase.ReadyToContinue); frame++)
                yield return null;
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
            yield return null;
            Game.Hud.CloseAllWindows();
            Game.Hud.RefreshNow();
            var market = BottomButton("Market");
            Assert.That(market.interactable, Is.False, "шахта завершена — Market неактивна");
            Tap(market);
            Assert.That(ToastText(), Is.EqualTo(Loc.T("outpost.reason.phase")));

            Game.Hud.ShowMarket();
            yield return null;
            Assert.That(Game.GoodsCount(Game.Hud.MarketRowGood(0)), Is.GreaterThan(0), "непроданный товар виден до перехода");
            Assert.That(RowSell(0).interactable, Is.False);
            Tap(RowSell(0));
            Assert.That(ToastText(), Is.EqualTo(Loc.T("outpost.reason.phase")));
            Assert.That(MarketButton("sellAll").interactable, Is.False);
            // Дополнение теста (решение владельца 15.10.2026): и в завершённой шахте — серая «Sell all» без «Mine complete» и без тоста.
            AssertSellAllGray(MarketButton("sellAll"));
            Game.Hud.HideMarket();

            var f = Game.State.Run.Factories[0];
            if (f.Status == FactoryStatus.Stopped)
            {
                Game.SelectTarget(f.TargetId);
                Game.Hud.RefreshNow();
                Assert.That(InfoText("body").text, Does.Contain(Loc.T("outpost.factory.stopped")));
            }
        }

        [UnityTest]
        public IEnumerator CardDetail_FactoryBlockAndMineLine()
        {
            yield return DebugMine(2);
            var cards = Game.Configs.Cards;
            var common = cards.Get("ca002");
            var uncommon = cards.Get("ca014");
            var mf = Game.Map.Factories.Single(m => m.TypeId == "amethyst");
            Assert.That(mf.RequiredCardId, Is.EqualTo("ca002"), "шахта 2: Dairy требует Dairy Foreman (приложение B)");
            Game.Hud.ShowCard(common.Id);
            yield return null;
            var block = Game.Hud.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == "factory");
            Assert.That(block.gameObject.activeInHierarchy, Is.True);
            string dairy = FactoryText.FactoryName("milk");
            Assert.That(block.text, Does.Contain(Loc.F("cards.detail.factory", dairy, FactoryText.GoodName("milk"))));
            int level = Game.State.Profile.CardLevel(common.Id);
            Assert.That(block.text, Does.Contain(OutpostCardText.Effect(common, level, cards.Rules)));
            Assert.That(OutpostCardText.Effect(common, 1, cards.Rules), Is.EqualTo(Loc.F("cards.effect.FactoryOutput", "Milk", "10%")));
            Assert.That(OutpostCardText.ShortEffect(common, 1, cards.Rules), Is.EqualTo(Loc.F("cards.short.FactoryOutput", "Milk", "10%")));
            string mine = level >= mf.RequiredCardLevel ? Loc.F("cards.detail.mine_auto_on", dairy)
                : level < 1 ? Loc.F("cards.detail.mine_auto_missing", dairy, mf.RequiredCardLevel)
                : Loc.F("cards.detail.mine_auto", dairy, mf.RequiredCardLevel, level);
            Assert.That(block.text, Does.Contain(mine));

            Game.Hud.ShowCard(uncommon.Id);
            yield return null;
            block = Game.Hud.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == "factory");
            if (level < mf.RequiredCardLevel)
                Assert.That(block.text, Does.Contain(Loc.F("cards.detail.mine_auto_other", dairy, OutpostCardText.Name(common), mf.RequiredCardLevel)),
                    "требование другой карточки типа называет её");

            // Не карточка завода — блока нет; длинное имя в сетке переносится перед последним словом.
            Game.Hud.ShowCard("ca001");
            yield return null;
            Assert.That(block.gameObject.activeInHierarchy, Is.False);
            Assert.That(CardView.WrapName("Steel Mill Foreman", false), Is.EqualTo("Steel Mill\nForeman"));
            Assert.That(CardView.WrapName("Cat Food Manager", false), Is.EqualTo("Cat Food\nManager"));
            Assert.That(CardView.WrapName("Dairy Foreman", false), Is.EqualTo("Dairy Foreman"));
            Assert.That(CardView.WrapName("Steel Mill Foreman", true), Is.EqualTo("Steel Mill Foreman"));
            Assert.That(OutpostUiText.TargetName(Game.Map.Target(mf.TargetId)), Is.EqualTo(dairy), "имя завода вместо «Deposit»");
            CollectionAssert.IsEmpty(Loc.FormatErrors, string.Join("\n", Loc.FormatErrors));
            CollectionAssert.IsEmpty(Loc.MissingKeys, string.Join("\n", Loc.MissingKeys));
        }
    }
}
