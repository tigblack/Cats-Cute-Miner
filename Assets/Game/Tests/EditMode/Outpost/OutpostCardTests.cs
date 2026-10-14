using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CatMiners.Core;
using CatMiners.Core.Outpost;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Возвращение карточек (задание «Cat Miners возвращение карточек», CARD_EFFECT_MAPPING.md): каталог 61 записи источника,
    /// открытие по максимальной достигнутой шахте, выдача и улучшение по CardUpgradeCosts, сундуки, эффекты в автоматическом
    /// ядре (каждый один раз), учебная карта, миграция профиля и перенос прежних карточек, детерминизм онлайн/офлайн.
    /// </summary>
    public class OutpostCardTests
    {
        static OutpostConfigSet s_Cards;
        static OutpostConfigSet CardConfigs => s_Cards ?? (s_Cards = Configs.WithCards(TestData.Balance));
        static CardCatalog Cat => CardConfigs.Cards;

        static OutpostProfile Profile(int highest = 1, long elixir = 0)
        {
            var p = StageProgressionService.NewProfile(CardConfigs, Seed, 1);
            p.HighestZone = highest;
            p.Elixir = elixir;
            return p;
        }

        static void Give(OutpostProfile p, string id, int level, long copies = 0) =>
            p.Cards[id] = new CardProgress { Level = level, Copies = copies };

        // ───────────────────────────── Каталог ─────────────────────────────

        [Test]
        public void Catalog_All61SourceCards_RarityCounts_Managers_MineUnlock_NoInventedIds()
        {
            Assert.AreEqual(61, Cat.Cards.Count);
            Assert.IsNull(Cat.Get("ca061"), "ca061 в источнике нет — ID не генерируются диапазоном");
            Assert.IsNotNull(Cat.Get("ca062"));
            Assert.AreEqual(40, Cat.Cards.Count(c => c.IsManager));
            var byRarity = Cat.Cards.GroupBy(c => c.Rarity).ToDictionary(g => g.Key, g => g.Count());
            CollectionAssert.AreEquivalent(new Dictionary<int, int> { { 1, 21 }, { 2, 19 }, { 3, 9 }, { 4, 6 }, { 5, 5 }, { 6, 1 } }, byRarity);
            Assert.AreEqual(1, Cat.Cards.Min(c => c.MineUnlock));
            Assert.AreEqual(138, Cat.Cards.Max(c => c.MineUnlock));
            Assert.AreEqual(10, Cat.Cards.Count(c => c.MineUnlock == 1), "на первой шахте доступны десять карточек");
            Assert.AreEqual(3, Cat.Get("ca003").MineUnlock);
            Assert.AreEqual(10, Cat.Get("ca007").MineUnlock);
            Assert.AreEqual(138, Cat.Get("ca059").MineUnlock);
            CollectionAssert.AreEqual(new[] { "amethyst" }, Cat.Get("ca002").TargetIds);
            CollectionAssert.AreEqual(new[] { "spawningcart" }, Cat.Get("ca001").TargetIds);
            var maxLevels = new Dictionary<int, int> { { 1, 14 }, { 2, 12 }, { 3, 9 }, { 4, 7 }, { 5, 5 }, { 6, 5 } };
            foreach (var c in Cat.Cards)
            {
                Assert.AreEqual(maxLevels[c.Rarity], c.MaxLevel, c.Id);
                Assert.IsNotNull(c.Effect, c.Id + ": у каждой карточки есть реализованный эффект");
                Assert.IsTrue(c.Effect.ValueAt(1).Sign > 0, c.Id + ": эффект на уровне 1 ненулевой");
            }
            // Специалисты сохраняют TargetIds; 21 не-manager — 21 разный код, у каждого свой эффект.
            Assert.AreEqual(21, Cat.Cards.Where(c => !c.IsManager).Select(c => c.StatModifierType).Distinct().Count());
            Assert.AreEqual(CardEffectKind.TargetValue, Cat.Get("ca002").Effect.Kind);
            Assert.AreEqual(CardEffectKind.TargetDamage, Cat.Get("ca014").Effect.Kind);
            Assert.AreEqual(CardEffectKind.UnloadRate, Cat.Get("ca001").Effect.Kind, "spawningcart → склад базы");
        }

        [Test]
        public void Art_EveryCardHasOwnIllustration_NoSharedPlaceholders_MajesticFrame()
        {
            // Атлас (PixelAtlasData.g.cs) склеивает одинаковые спрайты в один прямоугольник: разные прямоугольники 61 карточки —
            // это 61 разная иллюстрация, без общей заглушки.
            string atlas = System.IO.File.ReadAllText(System.IO.Path.Combine(TestData.ProjectRoot, "Assets/Game/Scripts/Runtime/Pixel/PixelAtlasData.g.cs"));
            var rects = new Dictionary<string, string>();
            foreach (var c in Cat.Cards)
            {
                var m = System.Text.RegularExpressions.Regex.Match(atlas, "new PixelSpriteDef\\(\"card_art_" + c.Id + "\", (\\d+), (\\d+), (\\d+), (\\d+)");
                Assert.IsTrue(m.Success, "нет арта card_art_" + c.Id);
                string rect = m.Groups[1].Value + "," + m.Groups[2].Value;
                Assert.IsFalse(rects.ContainsKey(rect), c.Id + " делит прямоугольник атласа с " + (rects.TryGetValue(rect, out var other) ? other : "?"));
                rects[rect] = c.Id;
                Assert.Greater(int.Parse(m.Groups[3].Value) * int.Parse(m.Groups[4].Value), 600, c.Id + ": иллюстрация, а не пустой спрайт");
            }
            StringAssert.Contains("\"card_bg_majestic\"", atlas);
            StringAssert.Contains("\"ui_card_frame_majestic\"", atlas);
            StringAssert.DoesNotContain("\"card_art_ca061\"", atlas);
        }

        // ───────────────────────────── Открытие по прогрессу ─────────────────────────────

        [Test]
        public void Unlock_ByHighestReachedMine_DoesNotGrantCopies()
        {
            var p = Profile(highest: 2);
            var ca003 = Cat.Get("ca003");
            Assert.IsFalse(CardService.IsUnlocked(p, ca003));
            Assert.AreEqual(CardViewState.Locked, CardService.StateOf(p, ca003));
            CardService.ReachZone(p, 3);
            Assert.IsTrue(CardService.IsUnlocked(p, ca003));
            Assert.AreEqual(CardViewState.Available, CardService.StateOf(p, ca003));
            Assert.AreEqual(0, p.CardLevel("ca003"), "снятие замка не выдаёт копию");
            CardService.ReachZone(p, 1);
            Assert.AreEqual(3, p.HighestZone, "возврат на меньшую шахту не снижает прогресс");
            Assert.AreEqual(10, Cat.Cards.Count(c => CardService.IsUnlocked(Profile(highest: 1), c)));
            Assert.AreEqual(61, Cat.Cards.Count(c => CardService.IsUnlocked(Profile(highest: 138), c)), "шахта 138 открывает весь каталог");
            // Уже полученная карточка остаётся активной даже ниже MineUnlock (перенос не блокирует её повторно).
            var low = Profile(highest: 1);
            Give(low, "ca059", 2);
            Assert.AreEqual(CardViewState.Active, CardService.StateOf(low, Cat.Get("ca059")));
        }

        // ───────────────────────────── Выдача и улучшение ─────────────────────────────

        [Test]
        public void Grant_FirstCopyActivates_UpgradeUsesCostsAtLevelMinusOne_AtomicAndMax()
        {
            var p = Profile(elixir: 1000);
            var def = Cat.Get("ca002");
            Assert.IsTrue(CardService.Grant(p, def, 3), "первая копия активирует карточку");
            Assert.AreEqual(1, p.CardLevel("ca002"));
            Assert.AreEqual(2, p.Cards["ca002"].Copies, "остальные — копии для улучшения");
            Assert.IsFalse(CardService.Grant(p, def, 1));
            var q = CardService.Quote(p, def);
            Assert.AreEqual(2, q.CopiesNeeded, "переход 1 → 2: Duplicates[0]");
            Assert.AreEqual(50, q.ElixirNeeded, "SoftCurrency[0] Common");
            Assert.IsTrue(q.CanUpgrade);
            Assert.IsNull(CardService.Upgrade(p, def, out _));
            Assert.AreEqual(2, p.CardLevel("ca002"));
            Assert.AreEqual(1, p.Cards["ca002"].Copies);
            Assert.AreEqual(950, p.Elixir);
            // Не хватает копий: переход 2 → 3 — Duplicates[1] = 5.
            string before = Json(new OutpostGameState { Profile = p });
            Assert.AreEqual("copies", CardService.Upgrade(p, def, out var q2));
            Assert.AreEqual(5, q2.CopiesNeeded);
            AssertSameJson(before, Json(new OutpostGameState { Profile = p }), "отказ ничего не меняет");
            p.Cards["ca002"].Copies = 100;
            p.Elixir = 10;
            Assert.AreEqual("elixir", CardService.Upgrade(p, def, out _), "SoftCurrency[1] = 100 эликсира");
            Assert.AreEqual("not-owned", CardService.Quote(p, Cat.Get("ca003")).BlockReason);
            Give(p, "ca034", 5, 50);
            Assert.AreEqual("max", CardService.Quote(p, Cat.Get("ca034")).BlockReason);
            Assert.AreEqual(CardViewState.Max, CardService.StateOf(p, Cat.Get("ca034")));
        }

        // ───────────────────────────── Сундуки ─────────────────────────────

        [Test]
        public void Chest_ScriptedExit_GuaranteedMinimums_TypeTwoWithoutZoneMultiplier()
        {
            var p = Profile();
            var r = CardChestService.Open(p, Cat, Cat.Chest("scg_m001_exit"), 1, null);
            Assert.AreEqual(1, r.TotalCopies, "scg_m001_exit: ca001 × GuaranteedCardMin 1");
            Assert.AreEqual("ca001:1", r.Detail());
            Assert.AreEqual(17, r.Elixir, "нижняя граница 17, GachaType 2 без множителя зоны");
            Assert.AreEqual(1, p.CardLevel("ca001"));
            Assert.AreEqual(17, p.Elixir);
            CollectionAssert.AreEqual(new[] { "ca001" }, r.NewCards);
        }

        [Test]
        public void Chest_RandomCards_ZoneScaledCount_OnlyUnlockedPool_Deterministic()
        {
            var a = Profile(highest: 1);
            var b = Profile(highest: 1);
            var ra = CardChestService.Open(a, Cat, Cat.Chest("GachaCrusher"), 1, null);
            var rb = CardChestService.Open(b, Cat, Cat.Chest("GachaCrusher"), 1, null);
            Assert.AreEqual(ra.Detail(), rb.Detail(), "тот же seed — тот же сундук");
            Assert.AreEqual(4, ra.TotalCopies, "35 × 0.1 = 3.5 → RoundHalfUp 4");
            Assert.AreEqual(87, ra.Elixir, "250 × 0.35 (GachaSoftCurrencyMultNormal zone1)");
            foreach (var g in ra.Cards) Assert.AreEqual(1, Cat.Get(g.CardId).MineUnlock, g.CardId + ": только карточки, открытые к шахте 1");
            // Много сундуков: все шесть редкостей реально выпадают из открытого пула; карточки выше прогресса — никогда.
            var p = Profile(highest: 7);
            var seen = new HashSet<int>();
            for (int i = 0; i < 400; i++)
                foreach (var g in CardChestService.Open(p, Cat, Cat.Chest("GachaCrusher"), 144, null).Cards)
                {
                    var def = Cat.Get(g.CardId);
                    Assert.LessOrEqual(def.MineUnlock, 7, def.Id);
                    seen.Add(def.Rarity);
                }
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5, 6 }, seen, "Majestic ca062 открыта на шахте 7 и тоже выпадает");
        }

        [Test]
        public void Chest_CardBonuses_LuckElixirExtraCards()
        {
            var plain = Profile(highest: 144);
            var lucky = Profile(highest: 144);
            Give(lucky, "ca053", 7);   // «Шахтёрский фонарь» +70 % эликсира
            Give(lucky, "ca062", 5);   // «Карта сокровищ» +3 карты
            var stats = TeamStats.Compute(CardConfigs.Balance, new int[6], lucky, Catalog(), 1, Cat);
            Assert.AreEqual(3, stats.CardChestCards);
            var r0 = CardChestService.Open(plain, Cat, Cat.Chest("scg_m001_exit"), 1, TeamStats.Compute(CardConfigs.Balance, new int[6], plain, Catalog(), 1, Cat));
            var r1 = CardChestService.Open(lucky, Cat, Cat.Chest("scg_m001_exit"), 1, stats);
            Assert.AreEqual(17, r0.Elixir);
            Assert.AreEqual(28, r1.Elixir, "floor(17 × 1.7)");
            Assert.AreEqual(1, r0.TotalCopies);
            Assert.AreEqual(4, r1.TotalCopies, "гарантированная + 3 случайные");
        }

        static EquipmentCatalog Catalog() => CardConfigs.Equipment;

        // ───────────────────────────── Эффекты ─────────────────────────────

        [Test]
        public void Effects_TeamStats_EachAppliedOnce_NoCardsMeansUnchanged()
        {
            var b = CardConfigs.Balance;
            var none = TeamStats.Compute(b, new int[6], Profile(), Catalog(), 1, Cat);
            var baseStats = TeamStats.Compute(b, new int[6], Profile(), Catalog(), 1);
            Assert.AreEqual(baseStats.DamageUnits, none.DamageUnits);
            Assert.AreEqual(baseStats.EdgeTicks, none.EdgeTicks);
            Assert.AreEqual(baseStats.UnloadTicks, none.UnloadTicks);
            Assert.AreEqual(baseStats.Capacity, none.Capacity);
            Assert.AreEqual(250_000, none.PickupTicks);
            Assert.AreEqual(0, none.CritChancePpm);

            var p = Profile(highest: 144);
            Give(p, "ca001", 1);   // разгрузка +5 %
            Give(p, "ca027", 1);   // скорость +3 %
            Give(p, "ca028", 5);   // сумка +2
            Give(p, "ca030", 1);   // подбор +5 %
            Give(p, "ca033", 1);   // урон +5 %
            Give(p, "ca034", 1);   // цена всех порций +10 %
            Give(p, "ca031", 9);   // крит 25 %
            Give(p, "ca035", 5);   // множитель крита 2 + 1.25
            Give(p, "ca038", 2);   // предел «Команды» +2
            Give(p, "ca050", 1);   // удар игрока +25 %
            var s = TeamStats.Compute(b, new int[6], p, Catalog(), 1, Cat);
            Assert.AreEqual(Rational.Of(21, 10), s.UnloadRate, "2 × 1.05");
            Assert.AreEqual(Rational.Of(103, 40), s.FinalMoveSpeed, "2.5 × 1.03");
            Assert.AreEqual(6, s.Capacity, "4 + floor(1 + 4/4)");
            Assert.AreEqual(238_096, s.PickupTicks, "ceil(250000 / 1.05)");
            Assert.AreEqual(5_250_000, s.DamageUnits, "5 × 1.05");
            Assert.AreEqual(Rational.Of(11, 10), s.FinalValue, "оценка ×1.1");
            Assert.AreEqual(250_000, s.CritChancePpm);
            Assert.AreEqual(Rational.Of(13, 4), s.CritMultiplier);
            Assert.AreEqual(2, s.CardTeamCap);
            Assert.AreEqual(1_640_625, s.TapDamageUnits, "5.25 × 1/4 × 1.25");

            var spec = Profile(highest: 144);
            Give(spec, "ca002", 3);   // знаток аметиста: +20 % цены
            Give(spec, "ca014", 1);   // мастер аметиста: +15 % урона
            Give(spec, "ca032", 1);   // ворота +20 %
            Give(spec, "ca037", 1);   // цена r +15 %
            Give(spec, "ca054", 1);   // урон x +20 %
            var t = TeamStats.Compute(b, new int[6], spec, Catalog(), 1, Cat);
            Assert.AreEqual(5_750_000, t.DamageUnitsFor('s', "amethyst"), "специалист усиливает только свой ресурс");
            Assert.AreEqual(5_000_000, t.DamageUnitsFor('s', "citrine"));
            Assert.AreEqual(6_000_000, t.DamageUnitsFor('p', null), "ворота");
            Assert.AreEqual(6_000_000, t.DamageUnitsFor('x', "block1x1v1"), "завалы");
            Assert.AreEqual(5_000_000, t.DamageUnitsFor('r', "rockbasic"));
            Assert.AreEqual(new BigInteger(12), t.CoinValueFor('s', "amethyst", Rational.Of(2, 1)), "ceil(5 × 2 × 1.2)");
            Assert.AreEqual(new BigInteger(10), t.CoinValueFor('s', "citrine", Rational.Of(2, 1)));
            Assert.AreEqual(new BigInteger(6), t.CoinValueFor('r', "rockbasic", Rational.One), "ceil(5 × 1.15)");
        }

        [Test]
        public void Effects_Discounts_TeamCap_StartWorkers_Parts_BedUnload()
        {
            var b = CardConfigs.Balance;
            var p = Profile(highest: 144);
            Give(p, "ca029", 1);   // найм −5 %
            Give(p, "ca043", 1);   // улучшения −3 %
            Give(p, "ca038", 1);   // предел +1
            Give(p, "ca036", 5);   // 2 котика сразу (floor(1 + 4/4)), из предела «Команды»
            Give(p, "ca026", 1);   // детали +10 %
            Give(p, "ca051", 2);   // разгрузка +10%, длительность офлайна не меняется
            var stats = TeamStats.Compute(b, new int[6], p, Catalog(), 1, Cat);
            Assert.AreEqual(new BigInteger(48), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Team, 0, stats.CardHireDiscount, stats.CardTeamCap), "ceil(50 × 0.95)");
            Assert.AreEqual(new BigInteger(20), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 0, stats.CardBranchDiscount), "ceil(20 × 0.97)");
            Assert.AreEqual(new BigInteger(23), TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Pickaxes, 1, stats.CardBranchDiscount), "ceil(23.6 × 0.97) = ceil(22.892)");
            Assert.IsNotNull(TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Team, 10, Rational.Zero, stats.CardTeamCap), "11-й найм возможен с «Золотой каской»");
            Assert.IsNull(TeamUpgradeService.NextCost(b, 1, UpgradeBranch.Team, 11, Rational.Zero, stats.CardTeamCap));

            var map = Map(1);
            var run = StageProgressionService.CreateRun(p, 1, map, CardConfigs, TestData.Balance, StartUtcMs);
            Assert.AreEqual(2, run.ExtraWorkers);
            Assert.AreEqual(4, run.Workers.Count, "2 + 2 котика «Плаката найма»");
            // Котики «Плаката» приходят из предела «Команды»: покупаемых уровней 10 + 1 − 2 = 9, всего котиков не больше 2 + 2 + 9 = 13.
            var team = TeamUpgradeService.Quote(run, UpgradeBranch.Team, BuyMode.One, stats);
            Assert.AreEqual(9, team.Cap);
            Assert.AreEqual(new BigInteger(48), team.NextCost, "первый покупной найм по цене уровня 0");
            run.Levels[(int)UpgradeBranch.Team] = 8;
            Assert.IsNotNull(TeamUpgradeService.Quote(run, UpgradeBranch.Team, BuyMode.One, stats).NextCost);
            run.Levels[(int)UpgradeBranch.Team] = 9;
            Assert.AreEqual("cap", TeamUpgradeService.Quote(run, UpgradeBranch.Team, BuyMode.One, stats).BlockReason);
            run.Levels[(int)UpgradeBranch.Team] = 0;
            var state = new OutpostGameState { Profile = p, Run = run };
            CollectionAssert.IsEmpty(OutpostInvariants.Check(state, map, Catalog(), Cat));
            var sim = new OutpostSimulation(state, map, CardConfigs);
            Assert.AreEqual(4, sim.Stats.Workers);

            var job = new OfflineSimulator(map, CardConfigs).Begin(state, StartUtcMs + 20 * HourMs);
            Assert.AreEqual(3 * HourMs, job.Summary.AppliedMs, "«Лежанка» не продлевает офлайн сверх 3 часов");
            Assert.AreEqual(Rational.Of(1, 10), sim.Stats.CardUnload, "L2: +10% к разгрузке");
            Assert.AreEqual(Rational.Of(11, 5), sim.Stats.UnloadRate, "2 порции/с × 1.1");

            p.RewardLog.Clear();
            var completion = new OutpostGameState { Profile = p, Run = run };
            var reward = StageProgressionService.ApplyCompletion(completion, CardConfigs);
            Assert.AreEqual(11, reward.CompletionParts, "floor(10 × 1.1)");
        }

        [TestCase(1, 5)]
        [TestCase(7, 35)]
        public void Bed_UnloadBonusStacksWithCart_PreservesCopies_AndCannotExtendOffline(int level, int percent)
        {
            var profile = Profile(highest: 144);
            Give(profile, "ca051", level, 41);
            Give(profile, "ca001", 1);
            var state = new OutpostGameState
            {
                Profile = profile,
                Run = StageProgressionService.CreateRun(profile, 1, Map(1), CardConfigs, TestData.Balance, StartUtcMs)
            };
            Assert.AreEqual(CardEffectKind.UnloadRate, Cat.Get("ca051").Effect.Kind);
            var stats = TeamStats.Compute(state.Run.Balance, new int[6], profile, Catalog(), 1, Cat);
            Assert.AreEqual(Rational.Of(percent + 5, 100), stats.CardUnload, "бонус лежанки складывается с ca001 один раз");
            Assert.AreEqual(Rational.Of(2 * (100 + percent + 5), 100), stats.UnloadRate);
            var job = new OfflineSimulator(Map(1), CardConfigs).Begin(state, StartUtcMs + 24 * HourMs);
            job.RunToEnd();
            Assert.AreEqual(3 * HourMs, job.Summary.AppliedMs);
            Assert.AreEqual(level, job.State.Profile.Cards["ca051"].Level);
            Assert.GreaterOrEqual(job.State.Profile.Cards["ca051"].Copies, 41, "сохранённые копии не расходуются при смене эффекта");
        }

        // ───────────────────────────── Ядро с карточками ─────────────────────────────

        static OutpostSimulation Arena(OutpostConfigSet configs, Action<OutpostProfile> setup = null)
        {
            var map = OutpostLevelAdapter.Build(TestData.Balance, 1, configs.Overrides);
            var st = StageProgressionService.NewGame(TestData.Balance, configs, Seed, StartUtcMs, 1);
            setup?.Invoke(st.Profile);
            return new OutpostSimulation(st, map, configs);
        }

        [Test]
        public void Zone1_NewProfile_TutorialCardAtFirstDelivery_RockElixir_ExitChest_GoldUnchanged()
        {
            var sim = Arena(CardConfigs);
            var events = new List<OutpostEvent>();
            RunToReadyOrFail(sim, "zone1 с карточками", null, (s, ev) =>
            {
                events.AddRange(ev);
                AssertInvariants(s, "zone1 с карточками");
                CollectionAssert.IsEmpty(OutpostInvariants.Check(s.State, s.Map, Catalog(), Cat));
            });
            var p = sim.State.Profile;
            var firstDeposit = events.First(e => e.Type == OutpostEventType.CargoDeposited);
            var tutorial = events.Single(e => e.Type == OutpostEventType.ChestOpened && e.ItemId == "tutorial");
            Assert.AreEqual(firstDeposit.Tick, tutorial.Tick, "учебная карта — при первой доставке");
            Assert.AreEqual("ca001:1", tutorial.Detail);
            Assert.Contains(CardService.TutorialKey, p.CardKeys);
            var exit = events.Single(e => e.Type == OutpostEventType.ChestOpened && e.ItemId == "scg_m001_exit");
            Assert.AreEqual(17, (long)exit.Value);
            Assert.AreEqual(2, events.Count(e => e.Type == OutpostEventType.ElixirFound), "два камня rocksoftcurrencysmall:2 арены");
            Assert.IsTrue(events.Where(e => e.Type == OutpostEventType.ElixirFound).All(e => e.Value == 2), "floor(21 × 0.1)");
            Assert.AreEqual(1, p.CardLevel("ca001"));
            Assert.AreEqual(1, p.Cards["ca001"].Copies, "учебная + копия из сундука выхода");
            Assert.AreEqual(21, p.Elixir, "2 + 2 + 17");
            Assert.AreEqual(2, sim.State.Run.Counters.CardsGained);
            Assert.AreEqual(21, sim.State.Run.Counters.ElixirGained);
            var audit = (Newtonsoft.Json.Linq.JObject)Audit["level_overrides"]["zone1"]["effective"];
            Assert.AreEqual(AuditBig(audit["base_gold"]), sim.State.Run.TotalCredited, "карточка склада не меняет сумму золота");
        }

        [Test]
        public void Zone2_ShaftChest_GrantsItsScriptedCards_AndManagerBonusRaisesThatResource()
        {
            var configs = CardConfigs;
            var map = OutpostLevelAdapter.Build(TestData.Balance, 2, configs.Overrides);
            var st = StageProgressionService.NewGame(TestData.Balance, configs, Seed, StartUtcMs, 2);
            var sim = new OutpostSimulation(st, map, configs);
            var chests = new List<OutpostEvent>();
            RunToReadyOrFail(sim, "zone2", null, (s, ev) => chests.AddRange(ev.Where(e => e.Type == OutpostEventType.ChestOpened)));
            var shaft = chests.Single(e => e.ItemId == "scg_m002_s01");
            Assert.AreEqual("ca001:2", shaft.Detail, "месторождение аметиста: GuaranteedCardCounts ca001 × 2");
            Assert.AreEqual(33, (long)shaft.Value);
            Assert.IsTrue(chests.Any(e => e.ItemId == "scg_m002_exit"));

            // Знаток аметиста: новые порции месторождения аметиста дороже, остальные — без изменений.
            var amethyst = map.Targets.Single(t => t.Kind == 's' && t.TypeId == "amethyst");
            var plain = new OutpostSimulation(StageProgressionService.NewGame(TestData.Balance, configs, Seed, StartUtcMs, 2), map, configs);
            var boostedState = StageProgressionService.NewGame(TestData.Balance, configs, Seed, StartUtcMs, 2);
            boostedState.Profile.Cards["ca002"] = new CardProgress { Level = 14 };
            var boosted = new OutpostSimulation(boostedState, map, configs);
            Assert.AreEqual(new BigInteger(12), plain.Stats.CoinValueFor('s', "amethyst", Rational.Of(2, 1)), "5 × S(2) 6/5 × 2");
            Assert.AreEqual(new BigInteger(21), boosted.Stats.CoinValueFor('s', "amethyst", Rational.Of(2, 1)), "ceil(12 × 1.75)");
            Assert.AreEqual(plain.Stats.CoinValueFor('r', "rockonboardingmine2", Rational.One), boosted.Stats.CoinValueFor('r', "rockonboardingmine2", Rational.One));
            Assert.IsNotNull(amethyst.RewardId);
        }

        [Test]
        public void UpgradeCard_Command_RecomputesStats_AllowedAfterCompletion_RejectionUnchanged()
        {
            var sim = Arena(CardConfigs, p =>
            {
                p.Cards["ca034"] = new CardProgress { Level = 1, Copies = 2 };
                p.Elixir = 5000;
            });
            Assert.AreEqual(Rational.Of(11, 10), sim.Stats.FinalValue);
            var r = sim.Submit(OutpostCommand.UpgradeCard("ca034"));
            Assert.IsTrue(r.Ok, r.Reason);
            Assert.AreEqual(new BigInteger(5000), r.Cost, "Legendary: SoftCurrency[0] = 5000");
            Assert.AreEqual(2, sim.State.Profile.CardLevel("ca034"));
            Assert.AreEqual(Rational.Of(6, 5), sim.Stats.FinalValue, "фаза 7 пересчитала цену порций");
            Assert.AreEqual(0, sim.State.Profile.Elixir);
            string before = Json(sim.State);
            Assert.AreEqual("copies", sim.Submit(OutpostCommand.UpgradeCard("ca034")).Reason);
            Assert.AreEqual("unknown", sim.Submit(OutpostCommand.UpgradeCard("ca061")).Reason);
            AssertSameJson(before, Json(sim.State), "отказы ничего не меняют");
            RunToReadyOrFail(sim, "zone1");
            sim.State.Profile.Cards["ca034"].Copies = 5;
            sim.State.Profile.Elixir = 20000;
            Assert.IsTrue(sim.Submit(OutpostCommand.UpgradeCard("ca034")).Ok, "улучшение доступно и в итогах шахты");
        }

        [Test]
        public void Crits_DeterministicPerWorkerAndTick_SameOnlineOfflineAndAfterReload()
        {
            void Setup(OutpostProfile p)
            {
                p.Cards["ca031"] = new CardProgress { Level = 9 };
                p.Cards["ca035"] = new CardProgress { Level = 5 };
            }
            var a = Arena(CardConfigs, Setup);
            var hits = new List<OutpostEvent>();
            RunToReady(a, null, 120 * S, EventLimit, (s, ev) => hits.AddRange(ev.Where(e => e.Type == OutpostEventType.TargetHit)));
            Assert.Greater(hits.Count(h => h.Extra > 0), 0, "криты случаются");
            Assert.Greater(hits.Count(h => h.Extra == 0), 0, "и не на каждом ударе");

            var online = Arena(CardConfigs, Setup);
            online.Advance(300 * S);
            online.State.Run.LastProcessedUtcMs = StartUtcMs + 300_000;
            var start = Arena(CardConfigs, Setup).State;
            var offline = new OfflineSimulator(online.Map, CardConfigs).Begin(start, StartUtcMs + 300_000);
            offline.RunToEnd();
            AssertSameJson(Json(online.State), Json(offline.State), "офлайн = онлайн с картами, критами и сундуками");
            var reloaded = new OutpostSimulation(FromJson(Json(online.State)), online.Map, CardConfigs);
            reloaded.Advance(600 * S);
            online.Advance(600 * S);
            AssertSameJson(Json(online.State), Json(reloaded.State), "save/load не меняет броски");
        }

        [Test]
        public void EmptyCollection_AutomaticLoopWorks_TransitionKeepsCardsAndElixir()
        {
            var sim = Arena(CardConfigs);
            RunToReadyOrFail(sim, "zone1");
            var next = StageProgressionService.PrepareTransition(sim.State, TestData.Balance, CardConfigs, StartUtcMs + HourMs);
            Assert.IsTrue(next.Ok);
            Assert.AreEqual(2, next.State.Profile.HighestZone, "вход в шахту 2 поднимает прогресс");
            Assert.AreEqual(sim.State.Profile.CardLevel("ca001"), next.State.Profile.CardLevel("ca001"));
            Assert.AreEqual(sim.State.Profile.Elixir, next.State.Profile.Elixir);
            Assert.AreEqual(0, next.State.Run.Levels.Sum(), "местные улучшения сброшены");
            CollectionAssert.IsEmpty(OutpostInvariants.Check(next.State, next.Map, Catalog(), Cat));
            var noCards = Arena(Configs);
            RunToReadyOrFail(noCards, "без каталога карточек");
            Assert.AreEqual(0, noCards.State.Profile.Cards.Count, "без каталога ничего не выдаётся");
        }

        // ───────────────────────────── Миграция ─────────────────────────────

        [Test]
        public void Migration_OldProfile_OnceAndLegacyImportOnce_KeepsNewerData()
        {
            var p = new OutpostProfile { Seed = 77, CurrentZone = 3, CompletedZones = { 1, 2 } };
            Assert.AreEqual(0, p.CardsVersion);
            Assert.IsTrue(CardService.MigrateProfile(p, CardConfigs.CardRules));
            Assert.AreEqual(1, p.CardsVersion);
            Assert.AreEqual(3, p.HighestZone);
            Assert.IsNotNull(p.CardRng);
            Assert.Contains(CardService.TutorialKey, p.CardKeys, "у старого профиля первая доставка уже была");
            Assert.IsFalse(CardService.MigrateProfile(p, CardConfigs.CardRules), "повторно не выполняется");

            Give(p, "ca002", 4, 1);
            var legacy = new Dictionary<string, (int Level, long Duplicates)>
            {
                { "ca001", (1, 2) },
                { "ca002", (2, 9) },     // в профиле уже есть более новые данные — не перезаписываем
                { "ca999", (3, 1) },     // неизвестный ID — пропуск
            };
            Assert.IsTrue(CardService.ImportLegacy(p, Cat, legacy, 56, out int imported));
            Assert.AreEqual(1, imported);
            Assert.AreEqual(1, p.CardLevel("ca001"));
            Assert.AreEqual(2, p.Cards["ca001"].Copies);
            Assert.AreEqual(4, p.CardLevel("ca002"));
            Assert.AreEqual(1, p.Cards["ca002"].Copies);
            Assert.AreEqual(56, p.Elixir);
            Assert.IsFalse(CardService.ImportLegacy(p, Cat, legacy, 56, out _), "перенос не суммируется повторно");
            Assert.AreEqual(56, p.Elixir);
        }
    }
}
