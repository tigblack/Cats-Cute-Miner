using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Outpost;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Магазин (ТЗ 10.5, store_catalog.json): каталог и шансы сундуков, однократный стартовый набор, покупки за гемы, рекламу и
    /// деньги (тестовый режим), пакеты гемов без бонуса первой покупки (механизм бонуса — на тестовом каталоге), таймер бесплатного
    /// сундука, сундук командой ядра (детерминированно),
    /// тайм-скип ровно на N часов без лимита офлайна и золото за рекламу мимо учёта доставки.
    /// </summary>
    public class OutpostStoreTests
    {
        static OutpostConfigSet s_Configs;
        static OutpostConfigSet StoreConfigs => s_Configs ?? (s_Configs = Configs.WithCards(TestData.Balance));
        static StoreCatalog Store => StoreConfigs.Store;
        const long Hour = 3_600_000L;

        static StoreItemDef Def(string id)
        {
            Assert.IsTrue(Store.TryGet(id, out var def), id);
            return def;
        }

        static OutpostSimulation Arena(System.Action<OutpostProfile> setup = null, OutpostConfigSet configs = null)
        {
            configs = configs ?? StoreConfigs;
            var map = OutpostLevelAdapter.Build(TestData.Balance, 1, configs.Overrides);
            var st = StageProgressionService.NewGame(TestData.Balance, configs, Seed, StartUtcMs, 1);
            StoreService.MigrateProfile(st.Profile, configs.Store);
            setup?.Invoke(st.Profile);
            return new OutpostSimulation(st, map, configs);
        }

        static List<OutpostEvent> Drain(OutpostSimulation sim)
        {
            var events = new List<OutpostEvent>();
            sim.DrainEvents(events);
            return events;
        }

        [Test]
        public void Catalog_SectionsItemsPrices_ChestsAndDropRates()
        {
            CollectionAssert.AreEqual(new[] { "offers", "timeskip", "elixir", "gems" }, Store.Sections);
            Assert.AreEqual(15, Store.Items.Length);
            Assert.IsTrue(Store.TestPurchases, "реклама и платежи — тестовый режим");
            Assert.AreEqual(StoreCurrency.Ad, Def("chest_wooden").Currency);
            Assert.AreEqual(240, Def("chest_wooden").CooldownMinutes);
            Assert.AreEqual(145, Def("chest_iron").Price);
            Assert.AreEqual(3500, Def("chest_mythical").Price);
            // Шахта — 4–12 минут игры, поэтому скипы — 10/30/60 минут (13 октября 2026); ID плиток прежние (ключи расходников).
            CollectionAssert.AreEqual(new[] { 10, 30, 60 }, Store.InSection("timeskip").Select(d => d.Minutes).ToArray());
            CollectionAssert.AreEqual(new[] { "skip_1h", "skip_4h", "skip_12h" }, Store.InSection("timeskip").Select(d => d.Id).ToArray());
            CollectionAssert.AreEqual(new long[] { 50, 150, 350 }, Store.InSection("timeskip").Select(d => d.Price).ToArray());
            CollectionAssert.AreEqual(new long[] { 1000, 10000, 100000 }, Store.InSection("elixir").Select(d => d.Amount).ToArray());
            CollectionAssert.AreEqual(new long[] { 60, 500, 4500 }, Store.InSection("elixir").Select(d => d.Price).ToArray());
            CollectionAssert.AreEqual(new long[] { 160, 500, 1200, 2500, 6500, 14000 }, Store.InSection("gems").Select(d => d.Amount).ToArray());
            // Скидки на гемы нет (14 октября 2026): ни у одной плитки нет бонуса первой покупки.
            Assert.IsFalse(Store.Items.Any(d => d.HasBonus || d.BonusPercent != 0), "бонус первой покупки");
            Assert.IsEmpty(Store.StarterConsumables, "бесплатные тайм-скипы не выдаются");
            Assert.DoesNotThrow(() => Store.ValidateChests(StoreConfigs.Cards));
            // Шансы редкости (ТЗ 10.5) — единый источник card_rules.json → ChestChances.
            CollectionAssert.AreEqual(new[] { 800000, 150000, 40000, 8000, 1800, 200 }, StoreConfigs.CardRules.ChancesFor("GachaWooden"));
            CollectionAssert.AreEqual(new[] { 700000, 200000, 70000, 20000, 8500, 1500 }, StoreConfigs.CardRules.ChancesFor("GachaIron"));
            CollectionAssert.AreEqual(new[] { 200000, 280000, 250000, 170000, 80000, 20000 }, StoreConfigs.CardRules.ChancesFor("GachaMythical"));
        }

        [Test]
        public void ConfigFile_MatchesEmbeddedDefault_AndHashIsNotInCatalogHash()
        {
            string path = Path.Combine(TestData.ProjectRoot, "Assets", "Game", "Resources", "Outpost", "store_catalog.json");
            Assert.AreEqual(OutpostConfigSet.DefaultStoreJson.Replace("\r\n", "\n"), File.ReadAllText(path).Replace("\r\n", "\n"));
            var cheaper = OutpostConfigSet.Parse(OutpostConfigSet.DefaultBalanceJson, OutpostConfigSet.DefaultEquipmentJson, OutpostConfigSet.DefaultRewardsJson,
                OutpostConfigSet.DefaultOverridesJson, OutpostConfigSet.DefaultCardRulesJson, OutpostConfigSet.DefaultTopologyJson,
                OutpostConfigSet.DefaultStoreJson.Replace("\"Price\": 145", "\"Price\": 99"));
            var standard = OutpostConfigSet.LoadDefault();
            Assert.AreNotEqual(standard.StoreHash, cheaper.StoreHash);
            Assert.AreEqual(standard.CatalogHash, cheaper.CatalogHash, "цены магазина не делают сохранения несовместимыми");
        }

        [Test]
        public void Migration_NewAndPreStoreProfiles_GetNoFreeTimeSkips()
        {
            var p = StageProgressionService.NewProfile(StoreConfigs, Seed, 1);
            Assert.AreEqual(0, p.StoreVersion);
            Assert.IsTrue(StoreService.MigrateProfile(p, Store));
            Assert.AreEqual(StoreService.CurrentProfileVersion, p.StoreVersion);
            Assert.AreEqual(0, p.ConsumableCount("skip_1h"));
            Assert.AreEqual(0, p.ConsumableCount("skip_4h"));
            Assert.AreEqual(0, p.ConsumableCount("skip_12h"));
            Assert.AreEqual(Store.StartGems, p.Gems);
            Assert.IsFalse(StoreService.MigrateProfile(p, Store), "второй раз набор не выдаётся");
            Assert.AreEqual(0, p.ConsumableCount("skip_1h"));

            // Профиль из сохранения до магазина: коллекций нет (null), миграция их создаёт.
            var old = StageProgressionService.NewProfile(StoreConfigs, Seed, 1);
            old.Consumables = null;
            old.StoreCooldownUntilUtcMs = null;
            old.StoreBonusUsed = null;
            Assert.IsTrue(StoreService.MigrateProfile(old, Store));
            Assert.AreEqual(0, old.ConsumableCount("skip_1h"));
            Assert.AreEqual(0, old.ConsumableCount("skip_4h"));
            Assert.IsNotNull(old.StoreCooldownUntilUtcMs);
        }

        [TestCase(3, 1)]
        [TestCase(1, 0)]
        [TestCase(0, 0)]
        public void Migration_RemovesOldStarterSkipsOnce_PreservesOtherProfileData(int shortStock, int mediumStock)
        {
            var p = StageProgressionService.NewProfile(StoreConfigs, Seed, 1);
            p.StoreVersion = 1;
            p.Gems = 777;
            p.Consumables["skip_1h"] = shortStock;
            p.Consumables["skip_4h"] = mediumStock;
            p.Consumables["skip_12h"] = 2;
            p.StoreCooldownUntilUtcMs["chest_wooden"] = StartUtcMs + Hour;
            p.StoreBonusUsed.Add("old_bonus");
            Assert.IsTrue(StoreService.MigrateProfile(p, Store));
            Assert.AreEqual(StoreService.CurrentProfileVersion, p.StoreVersion);
            Assert.AreEqual(0, p.ConsumableCount("skip_1h"));
            Assert.AreEqual(0, p.ConsumableCount("skip_4h"));
            Assert.AreEqual(2, p.ConsumableCount("skip_12h"), "не входит в прежний бесплатный набор");
            Assert.AreEqual(777, p.Gems);
            Assert.AreEqual(StartUtcMs + Hour, p.StoreCooldownUntilUtcMs["chest_wooden"]);
            CollectionAssert.AreEqual(new[] { "old_bonus" }, p.StoreBonusUsed);
            var reloaded = OutpostJson.DeepCopy(p);
            Assert.IsFalse(StoreService.MigrateProfile(reloaded, Store), "повторная загрузка ничего не меняет");
            reloaded.Gems = 49;
            var quote = StoreService.Quote(reloaded, Store, Def("skip_1h"), StartUtcMs);
            Assert.IsFalse(quote.Free);
            Assert.AreEqual(0, quote.Owned);
            Assert.AreEqual("gems", StoreService.PayTimeSkip(reloaded, Def("skip_1h")).Reason);
            Assert.AreEqual(49, reloaded.Gems);
            reloaded.Gems = 50;
            var paid = StoreService.PayTimeSkip(reloaded, Def("skip_1h"));
            Assert.IsTrue(paid.Ok);
            Assert.AreEqual(new BigInteger(50), paid.Cost);
            Assert.AreEqual(0, reloaded.Gems);
        }

        [Test]
        public void ElixirPack_SpendsGems_GrantsElixir_RefusesWithoutGems()
        {
            var sim = Arena(p => p.Gems = 100);
            var p0 = sim.State.Profile;
            long elixir = p0.Elixir;
            var r = sim.Submit(OutpostCommand.StoreBuy("elixir_bottle", StartUtcMs));
            Assert.IsTrue(r.Ok, r.ToString());
            Assert.AreEqual(40, p0.Gems);
            Assert.AreEqual(elixir + 1000, p0.Elixir);
            var e = Drain(sim).Single(x => x.Type == OutpostEventType.StorePurchased);
            Assert.AreEqual("elixir_bottle", e.ItemId);
            Assert.AreEqual((BigInteger)1000, e.Value);
            string before = OutpostJson.Serialize(sim.State.Profile);
            var refused = sim.Submit(OutpostCommand.StoreBuy("elixir_bottle", StartUtcMs));
            Assert.IsFalse(refused.Ok);
            Assert.AreEqual("gems", refused.Reason);
            Assert.AreEqual(before, OutpostJson.Serialize(sim.State.Profile), "отказ не меняет профиль");
            AssertInvariants(sim, "магазин: эликсир");
        }

        [Test]
        public void GemsPack_RegularAmount_NoFirstPurchaseBonus()
        {
            // Решение 14 октября 2026: у пакетов гемов нет бонуса первой покупки — каждая покупка даёт обычное количество.
            var sim = Arena();
            var p = sim.State.Profile;
            long gems = p.Gems;
            var q = StoreService.Quote(p, Store, Def("gems_sack"), StartUtcMs);
            Assert.IsFalse(q.BonusAvailable);
            Assert.AreEqual(500, q.Amount);
            Assert.IsTrue(sim.Submit(OutpostCommand.StoreBuy("gems_sack", StartUtcMs)).Ok);
            Assert.IsTrue(sim.Submit(OutpostCommand.StoreBuy("gems_sack", StartUtcMs)).Ok);
            Assert.IsTrue(sim.Submit(OutpostCommand.StoreBuy("gems_pile", StartUtcMs)).Ok);
            Assert.AreEqual(gems + 500 + 500 + 14000, p.Gems);
            Assert.IsEmpty(p.StoreBonusUsed);
        }

        [Test]
        public void GemsPack_FirstPurchaseBonusOnce_WhenCatalogHasIt()
        {
            // Механизм бонуса остаётся и включается только данными (BonusAmount/BonusPercent в store_catalog.json).
            const string sack = "\"Id\": \"gems_sack\", \"Section\": \"gems\", \"Kind\": \"Gems\", \"Amount\": 500,";
            Assert.That(OutpostConfigSet.DefaultStoreJson, Does.Contain(sack));
            var store = StoreCatalog.Parse(OutpostConfigSet.DefaultStoreJson.Replace(sack, sack + " \"BonusAmount\": 600, \"BonusPercent\": 20,"));
            var configs = StoreConfigs.WithStore(store);
            var sim = Arena(null, configs);
            var p = sim.State.Profile;
            long gems = p.Gems;
            Assert.IsTrue(store.TryGet("gems_sack", out var def));
            var q = StoreService.Quote(p, store, def, StartUtcMs);
            Assert.IsTrue(q.BonusAvailable);
            Assert.AreEqual(600, q.Amount);
            Assert.IsTrue(sim.Submit(OutpostCommand.StoreBuy("gems_sack", StartUtcMs)).Ok);
            Assert.AreEqual(gems + 600, p.Gems);
            CollectionAssert.Contains(p.StoreBonusUsed, "gems_sack");
            Assert.IsFalse(StoreService.Quote(p, store, def, StartUtcMs).BonusAvailable);
            Assert.IsTrue(sim.Submit(OutpostCommand.StoreBuy("gems_sack", StartUtcMs)).Ok);
            Assert.AreEqual(gems + 600 + 500, p.Gems);
            // Без бонуса — всегда обычное количество.
            Assert.IsTrue(sim.Submit(OutpostCommand.StoreBuy("gems_handful", StartUtcMs)).Ok);
            Assert.AreEqual(gems + 1100 + 160, p.Gems);
        }

        [Test]
        public void AdChest_OpensWoodenChestFromStore_ThenWaitsFourHours()
        {
            var sim = Arena();
            var p = sim.State.Profile;
            var q = StoreService.Quote(p, Store, Def("chest_wooden"), StartUtcMs);
            Assert.IsTrue(q.Free, "готовый рекламный сундук — зелёная плитка");
            long copies = p.Cards.Values.Sum(c => c.Level > 0 ? 1 + c.Copies : 0);
            // Состав масштабируется множителями шахты источника (RankMultipliers), как у сундуков шахты: на шахте 1
            // обычный сундук даёт 7 × 0,1 → 1 карточку и 45–55 × 0,35 эликсира.
            CardChestService.Preview(StoreConfigs.Cards, StoreConfigs.Cards.Chest("GachaWooden"), sim.Run.Zone, sim.Stats,
                out int previewCards, out long elixirMin, out long elixirMax);
            Assert.AreEqual(1, previewCards);
            Assert.AreEqual(15, elixirMin);
            Assert.AreEqual(19, elixirMax);
            int chestsInRun = sim.Run.Counters.ChestsOpened;
            Assert.IsTrue(sim.Submit(OutpostCommand.StoreBuy("chest_wooden", StartUtcMs)).Ok);
            var chest = Drain(sim).Single(x => x.Type == OutpostEventType.ChestOpened);
            Assert.AreEqual(OutpostIds.StoreSource, chest.TargetId, "источник — магазин");
            Assert.AreEqual("GachaWooden", chest.ItemId);
            Assert.AreEqual(previewCards, chest.Count);
            Assert.That((long)chest.Value, Is.InRange(elixirMin, elixirMax));
            Assert.AreEqual(chestsInRun, sim.Run.Counters.ChestsOpened, "сундук магазина — не находка шахты");
            Assert.AreEqual(copies + chest.Count, p.Cards.Values.Sum(c => c.Level > 0 ? 1 + c.Copies : 0), "копии карточек — в профиле");
            Assert.AreEqual(StartUtcMs + 4 * Hour, p.StoreCooldownUntilUtcMs["chest_wooden"]);

            var waiting = StoreService.Quote(p, Store, Def("chest_wooden"), StartUtcMs + Hour);
            Assert.IsFalse(waiting.Free);
            Assert.AreEqual(3 * Hour, waiting.CooldownLeftMs);
            var refused = sim.Submit(OutpostCommand.StoreBuy("chest_wooden", StartUtcMs + Hour));
            Assert.AreEqual("cooldown", refused.Reason);
            Assert.IsTrue(sim.Submit(OutpostCommand.StoreBuy("chest_wooden", StartUtcMs + 4 * Hour)).Ok, "через 4 часа сундук снова доступен");
            AssertInvariants(sim, "магазин: рекламный сундук");
        }

        [Test]
        public void GemChest_SameSeed_SameCards()
        {
            string Open()
            {
                var sim = Arena(p => p.Gems = 1000);
                Assert.IsTrue(sim.Submit(OutpostCommand.StoreBuy("chest_iron", StartUtcMs)).Ok);
                Assert.AreEqual(1000 - 145, sim.State.Profile.Gems);
                return Drain(sim).Single(x => x.Type == OutpostEventType.ChestOpened).Detail + "|" + OutpostJson.Serialize(sim.State.Profile);
            }
            Assert.AreEqual(Open(), Open(), "сундук магазина — тот же детерминированный CardRng профиля");
        }

        [Test]
        public void TestPurchasesOff_AdAndMoneyUnavailable_GemsStillWork()
        {
            var off = StoreCatalog.Parse(OutpostConfigSet.DefaultStoreJson.Replace("\"TestPurchases\": true", "\"TestPurchases\": false"));
            var p = StageProgressionService.NewProfile(StoreConfigs, Seed, 1);
            StoreService.MigrateProfile(p, off);
            p.Gems = 1000;
            off.TryGet("chest_wooden", out var ad);
            off.TryGet("gems_pile", out var money);
            off.TryGet("elixir_box", out var elixir);
            Assert.AreEqual("unavailable", StoreService.Quote(p, off, ad, StartUtcMs).BlockReason);
            Assert.AreEqual("unavailable", StoreService.Pay(p, off, money, StartUtcMs).Reason);
            Assert.IsTrue(StoreService.Pay(p, off, elixir, StartUtcMs).Ok);
            Assert.AreEqual(500, p.Gems);
        }

        [Test]
        public void TimeSkip_ByCommand_IsUnknown_TheControllerRunsIt()
        {
            var sim = Arena(p => p.Gems = 1000);
            Assert.AreEqual("unknown", sim.Submit(OutpostCommand.StoreBuy("skip_1h", StartUtcMs)).Reason);
        }

        [Test]
        public void TimeSkip_OwnedFirst_ThenGems_ExactMinutes_NoOfflineCap()
        {
            var sim = Arena(p => { p.Gems = 400; p.Consumables["skip_4h"] = 1; });
            var state = sim.State;
            var simulator = new OfflineSimulator(sim.Map, StoreConfigs);

            var job = simulator.BeginSkip(state, Def("skip_4h"), StartUtcMs);
            long startTick = job.State.Run.Now;
            Assert.AreEqual(startTick + 30 * 60 * job.State.Run.Balance.TicksPerSecond, job.TargetTick, "цель — ровно 30 минут работы шахты");
            Assert.IsTrue(StoreService.PayTimeSkip(job.State.Profile, Def("skip_4h")).Ok);
            Assert.AreEqual(0, job.State.Profile.ConsumableCount("skip_4h"), "сначала — тайм-скип из наличия");
            Assert.AreEqual(400, job.State.Profile.Gems);
            Assert.AreEqual(1, state.Profile.ConsumableCount("skip_4h"), "живое состояние не меняется до записи копии");
            var summary = job.RunToEnd();
            Assert.AreEqual(30, summary.SkipMinutes);
            Assert.AreEqual("skip_4h", summary.SkipItemId);
            Assert.AreEqual(Hour / 2, summary.AppliedMs, "ровно 30 минут");
            Assert.Greater(summary.GoldDelivered.Sign, 0, "котики доставили золото за 30 минут");
            if (summary.StageCompleted) Assert.AreEqual(RunPhase.ReadyToContinue, job.State.Run.Phase, "шахта пройдена — расчёт остановлен");
            else Assert.AreEqual(job.TargetTick, job.State.Run.Now);
            Assert.AreEqual(StartUtcMs, job.State.Run.LastProcessedUtcMs, "реальное время не сдвигается");
            CollectionAssert.IsEmpty(OutpostInvariants.Check(job.State, sim.Map, StoreConfigs.Equipment, StoreConfigs.Cards));

            // Своих 30 минут больше нет — гемы; на час гемов не хватает.
            var paid = simulator.BeginSkip(job.State, Def("skip_4h"), StartUtcMs);
            Assert.IsTrue(StoreService.PayTimeSkip(paid.State.Profile, Def("skip_4h")).Ok);
            Assert.AreEqual(250, paid.State.Profile.Gems);
            var big = simulator.BeginSkip(paid.State, Def("skip_12h"), StartUtcMs);
            Assert.AreEqual(Hour, big.Summary.AppliedMs);
            // Каталог может задать скип длиннее трёх часов: предел офлайна его не обрезает.
            var longSkip = new StoreItemDef { Id = "skip_test", Kind = StoreItemKind.TimeSkip, Minutes = 240 };
            Assert.AreEqual(4 * Hour, simulator.BeginSkip(state, longSkip, StartUtcMs).Summary.AppliedMs, "тайм-скип не ограничен тремя часами офлайна");
            big.State.Profile.Gems = 100;
            Assert.AreEqual("gems", StoreService.PayTimeSkip(big.State.Profile, Def("skip_12h")).Reason);
        }

        [Test]
        public void BonusGold_GoesToWallet_NotToDeliveryLedger()
        {
            var sim = Arena();
            var run = sim.State.Run;
            var wallet = run.Wallet;
            var credited = run.TotalCredited;
            var r = sim.Submit(OutpostCommand.BonusGold(1234));
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(wallet + 1234, run.Wallet);
            Assert.AreEqual((BigInteger)1234, run.BonusCredited);
            Assert.AreEqual(credited, run.TotalCredited, "доставка не меняется");
            Assert.AreEqual((BigInteger)1234, Drain(sim).Single(x => x.Type == OutpostEventType.BonusGold).Value);
            Assert.IsFalse(sim.Submit(OutpostCommand.BonusGold(0)).Ok);
            AssertInvariants(sim, "золото за рекламу");
        }
    }
}
