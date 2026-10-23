using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Первая сессия (Docs/Rework/FIRST_SESSION.md, progression.json): границы открытия функций по шахтам и событиям, Store — вместе
    /// с Cards, постоянство открытий при сбросе уровней, однократные выдачи при открытии разделов (карточка на одно улучшение,
    /// стартовая кирка) с квитанцией, спасение Mia обычной работой котиков (один раз, онлайн и офлайн, после перезапуска), Mia не
    /// становится котиком команды (она в магазине), миграция прежних профилей, загрузка снимка по новым правилам и Loading Bays
    /// (настоящая параллельная разгрузка). Состояние строится напрямую, без прохождения кампании.
    /// </summary>
    public sealed class FirstSessionTests
    {
        static OutpostConfigSet s_Configs;
        static OutpostConfigSet Configs => s_Configs ?? (s_Configs = OutpostConfigSet.LoadDefault().WithCards(TestData.Balance));
        const long Now = 1_800_000_000_000L;
        const string Mia = "mia";

        static long Ticks(double seconds) => (long)(seconds * OutpostIds.TicksPerSecond);
        static OutpostGameState NewGame(int zone = 1, ulong seed = 11) => StageProgressionService.NewGame(TestData.Balance, Configs, seed, Now, zone);
        static OutpostMap MapOf(OutpostGameState s) => OutpostLevelAdapter.ForRun(TestData.Balance, s.Run);
        static OutpostSimulation Sim(OutpostGameState s, bool events = true) => new OutpostSimulation(s, MapOf(s), Configs) { RecordEvents = events };

        /// <summary>Шахта идёт до условия, но не дольше <paramref name="limitSeconds"/> секунд игры.</summary>
        static void RunUntil(OutpostSimulation sim, Func<bool> done, double limitSeconds)
        {
            long end = sim.Now + Ticks(limitSeconds);
            while (!done() && sim.Now < end && !sim.IsEconomyStopped) sim.Advance(Math.Min(end, sim.Now + Ticks(2)));
        }

        static HashSet<string> Unlocked(OutpostProfile p) => new HashSet<string>(p.Progression.Unlocked);

        // ───────────── Открытие функций ─────────────

        [Test]
        public void NewProfile_Mine1_AllBaseBranchesOpenBeforeFirstDelivery()
        {
            var s = NewGame();
            var sim = Sim(s);
            Assert.AreEqual(ProgressionService.CurrentVersion, s.Profile.Progression.Version);
            Assert.Less(s.Run.Counters.FirstDeliveryTick, 0);
            foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
            {
                Assert.IsTrue(s.Profile.Progression.IsUnlocked(ProgressionConfig.BranchFeature(branch)), branch.ToString());
                Assert.AreNotEqual("locked", sim.Quote(branch, BuyMode.One).BlockReason, branch.ToString());
            }
            Assert.IsFalse(s.Profile.Progression.IsUnlocked("cards"));
            Assert.IsFalse(s.Profile.Progression.IsUnlocked("equipment"));
            RunUntil(sim, () => sim.Quote(UpgradeBranch.Pickaxes, BuyMode.One).CanBuy, 300);
            Assert.IsTrue(sim.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Pickaxes)).Ok);
            int workers = s.Run.Workers.Count;
            RunUntil(sim, () => sim.Quote(UpgradeBranch.Team, BuyMode.One).CanBuy, 600);
            Assert.IsTrue(sim.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Team)).Ok);
            Assert.AreEqual(workers + 1, s.Run.Workers.Count);
        }

        [Test]
        public void BaseBranches_LoadOldMine1_UnlocksAllWithoutReset_AndAllSevenCanBeBought()
        {
            var s = NewGame();
            s.Profile.Progression.Unlocked.Clear(); // Previous staged-unlock profile.
            s.Profile.Progression.Unlocked.Add("branch.pickaxes");
            s.Profile.Progression.Unlocked.Add("branch.team");
            string runBefore = OutpostJson.Serialize(s.Run);
            Assert.IsTrue(ProgressionService.PrepareLoaded(s.Profile, s.Run, Configs));
            Assert.AreEqual(runBefore, OutpostJson.Serialize(s.Run), "wallet, ranks, HP, cargo and workers survive loading");
            var sim = Sim(s);
            sim.Submit(OutpostCommand.BonusGold(BigInteger.Pow(10, 6)));
            foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
            {
                Assert.IsTrue(sim.Quote(branch, BuyMode.One).CanBuy, branch.ToString());
                Assert.IsTrue(sim.Submit(OutpostCommand.BuyUpgrade(branch)).Ok, branch.ToString());
                Assert.AreEqual(1, s.Run.Level(branch));
            }
            Assert.Greater(s.Run.Slots.Length, OutpostIds.DepotSlotCount);
            Assert.IsEmpty(OutpostInvariants.Check(s, sim.Map, Configs.Equipment, Configs.Cards));
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void MineEntry_AllBaseBranchesOpen_SectionsKeepTheirRequirements(int zone)
        {
            var s = NewGame(zone);
            var expected = Enum.GetValues(typeof(UpgradeBranch)).Cast<UpgradeBranch>()
                .Select(ProgressionConfig.BranchFeature).ToList();
            if (zone >= 2) expected.AddRange(new[] { "cards", "store" });
            var unlocked = Unlocked(s.Profile);
            unlocked.Remove(ProgressionConfig.SellGoods);
            CollectionAssert.AreEquivalent(expected, unlocked, "Mine " + zone);
            var sim = Sim(s);
            foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
                Assert.AreNotEqual("locked", sim.Quote(branch, BuyMode.One).BlockReason, branch.ToString());
        }

        [Test]
        public void Unlocks_SurviveMineReset_PurchasedRanksDoNot()
        {
            var s = NewGame(2);
            var sim = Sim(s);
            sim.Submit(OutpostCommand.BonusGold(BigInteger.Pow(10, 6)));
            Assert.IsTrue(sim.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Bags, BuyMode.Ten)).Ok);
            Assert.Greater(s.Run.Level(UpgradeBranch.Bags), 0);
            var profile = OutpostJson.DeepCopy(s.Profile);
            var map = OutpostLevelAdapter.BuildMine(TestData.Balance, 3, Configs);
            var run = StageProgressionService.CreateRun(profile, 3, map, Configs, TestData.Balance, Now);
            CollectionAssert.AreEqual(new int[OutpostIds.BranchCount], run.Levels, "покупки сбрасываются в новой шахте");
            Assert.IsTrue(profile.Progression.IsUnlocked("branch.bags"), "ветка остаётся открытой");
            Assert.IsTrue(profile.Progression.IsUnlocked("branch.pickaxes"));
        }

        [Test]
        public void Store_OpensTogetherWithCards()
        {
            var s = NewGame();
            var p = s.Profile;
            Assert.IsFalse(ProgressionService.IsAvailable(p, Configs, ProgressionConfig.Store), "в шахте 1 магазин закрыт");
            Assert.IsFalse(ProgressionService.IsAvailable(p, Configs, ProgressionConfig.Cards));
            // Карточка, полученная раньше шахты 2 (событие own-cards), открывает карточки — и магазин в том же проходе.
            CardService.Grant(p, Configs.Cards.Get("ca001"), 1);
            var added = ProgressionService.Sync(p, s.Run, Configs);
            CollectionAssert.AreEqual(new[] { "cards", "store" }, added);
            Assert.IsTrue(ProgressionService.IsAvailable(p, Configs, ProgressionConfig.Store));

            var next = NewGame(2);
            Assert.IsTrue(next.Profile.Progression.IsUnlocked("cards") && next.Profile.Progression.IsUnlocked("store"), "шахта 2 — оба раздела");

            // With — только на функцию выше по списку и без своих Zone/Event.
            var cfg = ProgressionConfig.Parse(Configs.Progression.CanonicalJson());
            cfg.Feature("store").With = "equipment";
            Assert.Throws<OutpostConfigException>(() => cfg.Validate());
            cfg.Feature("store").With = "cards";
            cfg.Feature("store").Zone = 2;
            Assert.Throws<OutpostConfigException>(() => cfg.Validate());
        }

        [Test]
        public void ConfigWithoutProgression_EverythingOpenAsBefore()
        {
            var configs = Configs.WithProgression(null);
            var s = StageProgressionService.NewGame(TestData.Balance, configs, 3, Now, 1);
            var sim = new OutpostSimulation(s, OutpostLevelAdapter.ForRun(TestData.Balance, s.Run), configs);
            Assert.AreEqual(0, s.Profile.Progression.Version);
            Assert.IsFalse(sim.HasRescue, "без правил первой сессии клетки нет");
            foreach (var b in new[] { UpgradeBranch.Team, UpgradeBranch.Pickaxes, UpgradeBranch.Bags, UpgradeBranch.Bays })
                Assert.AreNotEqual("locked", sim.Quote(b, BuyMode.One).BlockReason, b.ToString());
        }

        // ───────────── Выдачи при открытии разделов ─────────────

        [Test]
        public void CardsIntro_GrantsExactlyOneRealUpgrade_Once()
        {
            var p = StageProgressionService.NewProfile(Configs, 5);
            var def = Configs.Cards.Get(Configs.Progression.CardsIntro.Card);
            Assert.IsNotNull(def, "стартовая карточка — существующая карточка коллекции");
            ProgressionService.ApplyEntryGrants(p, 1, Configs);
            Assert.AreEqual(0, p.CardLevel(def.Id), "в шахте 1 карточек нет");

            ProgressionService.ApplyEntryGrants(p, 2, Configs);
            Assert.IsTrue(p.Progression.HasGrant(ProgressionService.GrantCards));
            Assert.AreEqual(1, p.CardLevel(def.Id));
            var q = CardService.Quote(p, def);
            Assert.IsTrue(q.CanUpgrade, "копий и эликсира хватает на улучшение");
            Assert.AreEqual(q.CopiesNeeded, q.CopiesOwned, "ровно на одно улучшение (по ценам CardUpgradeCosts)");
            Assert.AreEqual(def.UpgradeElixir[0], p.Elixir);

            // Повтор входа, загрузка снимка и повторный вызов ничего не удваивают.
            var reloaded = OutpostJson.DeepCopy(p);
            ProgressionService.ApplyEntryGrants(reloaded, 2, Configs);
            ProgressionService.ApplyEntryGrants(reloaded, 3, Configs);
            Assert.AreEqual(q.CopiesOwned, CardService.Quote(reloaded, def).CopiesOwned);
            Assert.AreEqual(p.Elixir, reloaded.Elixir);

            Assert.IsNull(CardService.Upgrade(reloaded, def, out _), "настоящее улучшение");
            Assert.AreEqual(2, reloaded.CardLevel(def.Id));
            Assert.IsFalse(CardService.Quote(reloaded, def).CanUpgrade, "выдано только на одно улучшение");
        }

        [Test]
        public void CardsIntro_GrantCountsPlayersInventory()
        {
            var p = StageProgressionService.NewProfile(Configs, 6);
            var def = Configs.Cards.Get(Configs.Progression.CardsIntro.Card);
            // Первая копия активирует карточку, на улучшение 1 → 2 нужны ещё UpgradeCopies[0] копий и UpgradeElixir[0] эликсира.
            CardService.Grant(p, def, def.UpgradeCopies[0]);
            p.Elixir = 1000;
            var need = ProgressionService.CardsIntroGrant(p, Configs);
            Assert.AreEqual((1L, 0L), need, "не хватает одной копии, эликсира достаточно");
            long elixir = p.Elixir;
            ProgressionService.ApplyEntryGrants(p, 2, Configs);
            Assert.AreEqual(elixir, p.Elixir, "эликсир не выдаётся сверх нужного");
            Assert.IsTrue(CardService.Quote(p, def).CanUpgrade, "после выдачи — ровно на улучшение");
            Assert.AreEqual((0L, 0L), ProgressionService.CardsIntroGrant(p, Configs));
            Assert.IsTrue(p.Progression.HasGrant(ProgressionService.GrantCards));
        }

        [Test]
        public void EquipmentIntro_StarterPickaxe_OnlyForEmptySlot_Once()
        {
            var p = StageProgressionService.NewProfile(Configs, 7);
            string item = Configs.Progression.EquipmentIntro.Item;
            ProgressionService.ApplyEntryGrants(p, 2, Configs);
            Assert.AreEqual(0, p.ItemLevel(item), "Mine 2 entry alone does not unlock Equipment");
            ProgressionService.Rescue(p, ProgressionConfig.BlacksmithCharacter);
            p.CompletedZones.Add(2);
            ProgressionService.Sync(p, null, Configs);
            ProgressionService.ApplyEntryGrants(p, 3, Configs);
            Assert.AreEqual(1, p.ItemLevel(item), "starter is granted on actual Mine 2 completion");
            Assert.IsTrue(p.Progression.HasGrant(ProgressionService.GrantEquipment));
            ProgressionService.ApplyEntryGrants(p, 4, Configs);
            Assert.AreEqual(1, p.ItemLevel(item), "без повтора");
            Assert.IsNull(p.Equipped[(int)EquipSlot.Pickaxe], "надевает игрок сам — обычной командой");

            var owner = StageProgressionService.NewProfile(Configs, 8);
            EquipmentService.Grant(owner, "pickaxe.rare", Configs);
            ProgressionService.Rescue(owner, ProgressionConfig.BlacksmithCharacter);
            owner.CompletedZones.Add(2);
            ProgressionService.Sync(owner, null, Configs);
            ProgressionService.ApplyEntryGrants(owner, 3, Configs);
            Assert.AreEqual(0, owner.ItemLevel(item), "своя кирка уже есть — дубликат не выдаётся");
            Assert.IsTrue(owner.Progression.HasGrant(ProgressionService.GrantEquipment));
        }

        [Test]
        public void Mine1_NoCardFinds_CardsArriveInMine2()
        {
            var s = NewGame(1, 23);
            var sim = Sim(s);
            RunUntil(sim, () => s.Run.Phase == RunPhase.ReadyToContinue, 7200);
            Assert.AreEqual(RunPhase.ReadyToContinue, s.Run.Phase);
            var p = s.Profile;
            Assert.IsFalse(p.Progression.IsUnlocked(ProgressionConfig.Cards), "карточки — не случайной находкой шахты 1");
            Assert.AreEqual(0, p.CardLevel(Configs.Progression.CardsIntro.Card), "сундук выхода шахты 1 ждёт открытия карточек");
            Assert.AreEqual(0, p.Elixir);
            Assert.IsFalse(sim.Events.Any(e => e.Type == OutpostEventType.ChestOpened));
            var tr = StageProgressionService.PrepareTransition(s, TestData.Balance, Configs, Now);
            Assert.IsTrue(tr.Ok);
            var def = Configs.Cards.Get(Configs.Progression.CardsIntro.Card);
            Assert.IsTrue(tr.State.Profile.Progression.IsUnlocked(ProgressionConfig.Cards), "шахта 2 открывает карточки");
            Assert.IsTrue(CardService.Quote(tr.State.Profile, def).CanUpgrade, "с ресурсами ровно на одно улучшение");
        }

        [Test]
        public void MineRewards_FirstTwoMines_PartsOnly_StarterFromBlacksmith()
        {
            Assert.IsTrue(Configs.Rewards.NoItemFor(1));
            Assert.IsTrue(Configs.Rewards.NoItemFor(2));
            Assert.IsFalse(Configs.Rewards.NoItemFor(3));
            Assert.Greater(Configs.Rewards.CompletionParts(1), 0);
        }

        // ───────────── Спасение Mia ─────────────

        [Test]
        public void Rescue_AutomatedCats_FreeMiaOnce_ThenMineCompletes()
        {
            var s = NewGame(1, 21);
            var sim = Sim(s);
            Assert.IsTrue(sim.HasRescue);
            var progress = sim.RescueProgress();
            Assert.Greater(progress.Total, 0, "маршрут к клетке — настоящие цели карты");
            int workers = s.Run.Workers.Count;
            // Без покупок и ручных ударов: котики сами расчищают маршрут и ломают замок.
            RunUntil(sim, () => !sim.RescuePending, 1800);
            Assert.IsFalse(sim.RescuePending, "замок сломан обычной работой");
            Assert.AreEqual(1, s.Profile.Progression.Crew.Count(c => c == Mia));
            Assert.AreEqual(1, sim.Events.Count(e => e.Type == OutpostEventType.CharacterRescued));
            Assert.AreEqual(workers, s.Run.Workers.Count, "Mia не становится лишним работником");
            var after = sim.RescueProgress();
            Assert.IsTrue(after.LockOpen);

            RunUntil(sim, () => s.Run.Phase == RunPhase.ReadyToContinue, 7200);
            Assert.AreEqual(RunPhase.ReadyToContinue, s.Run.Phase, "обычное завершение шахты: груз сдан");
            Assert.AreEqual(1, s.Profile.Progression.Crew.Count(c => c == Mia));
            Assert.IsEmpty(OutpostInvariants.Check(s, sim.Map, Configs.Equipment));
        }

        [Test]
        public void Rescue_Offline_NoEvent_StillJoinsOnce_AndSurvivesReload()
        {
            var s = NewGame(1, 22);
            var offline = Sim(s, events: false);
            RunUntil(offline, () => !offline.RescuePending, 1800);
            Assert.IsFalse(offline.RescuePending);
            Assert.IsTrue(s.Profile.Progression.HasCrew(Mia), "офлайн-расчёт тоже спасает");

            var reloaded = OutpostJson.DeepCopy(s);
            OutpostStateUpgrade.Normalize(reloaded);
            var sim = Sim(reloaded);
            Assert.IsFalse(sim.RescuePending, "после загрузки клетка открыта");
            ProgressionService.Sync(reloaded.Profile, reloaded.Run, Configs);
            Assert.AreEqual(1, reloaded.Profile.Progression.Crew.Count(c => c == Mia), "без дубликата");
            Assert.IsFalse(reloaded.Profile.Progression.WasShown(ProgressionService.RevealPrefix + Mia),
                "окно «Mia is free!» ещё впереди (показ отдельно от спасения)");
        }

        [Test]
        public void NextMine_MiaRunsTheStore_NotACrewCat_SameCrewSizeAndStats()
        {
            var withMia = StageProgressionService.NewProfile(Configs, 31, 2);
            ProgressionService.Rescue(withMia, Mia);
            var without = StageProgressionService.NewProfile(Configs, 31, 2);
            var map = OutpostLevelAdapter.BuildMine(TestData.Balance, 2, Configs);
            var a = StageProgressionService.CreateRun(withMia, 2, map, Configs, TestData.Balance, Now);
            var b = StageProgressionService.CreateRun(without, 2, map, Configs, TestData.Balance, Now);
            Assert.IsTrue(ProgressionService.Rescued(withMia, Configs), "Mia свободна — встречает в магазине");
            Assert.AreEqual(Configs.Balance.StartWorkers, a.Workers.Count);
            Assert.AreEqual(b.Workers.Count, a.Workers.Count, "Mia не прибавляет котика");
            Assert.AreEqual(0, a.Workers.Count(w => w.Crew != null), "Mia не работает в шахте");
            var sa = TeamStats.Compute(a.Balance, a.Levels, withMia, Configs.Equipment, 2, Configs.Cards, a.ExtraWorkers);
            var sb = TeamStats.Compute(b.Balance, b.Levels, without, Configs.Equipment, 2, Configs.Cards, b.ExtraWorkers);
            Assert.AreEqual(sb.FinalDamage, sa.FinalDamage, "характеристики те же");
            Assert.AreEqual(sb.Workers, sa.Workers);
        }

        [Test]
        public void Load_ProfileFromEarlierRules_GetsStore_MiaLeavesTheCrew()
        {
            // Снимок прежней сборки: шахта 3 начата, Store ещё не было в правилах, первый котик — в облике Mia.
            var s = NewGame(3, 33);
            var p = s.Profile;
            ProgressionService.Rescue(p, Mia);
            p.Progression.Unlocked.Remove(ProgressionConfig.Store);
            s.Run.Workers[0].Crew = Mia;
            var saved = OutpostJson.DeepCopy(s);
            OutpostStateUpgrade.Normalize(saved);

            Assert.IsTrue(ProgressionService.PrepareLoaded(saved.Profile, saved.Run, Configs));
            Assert.IsTrue(saved.Profile.Progression.IsUnlocked(ProgressionConfig.Store), "Store открыт по новым правилам");
            Assert.AreEqual(0, saved.Run.Workers.Count(w => w.Crew != null), "Mia ушла из шахты в магазин");
            Assert.AreEqual(s.Run.Workers.Count, saved.Run.Workers.Count);
            Assert.IsFalse(ProgressionService.PrepareLoaded(saved.Profile, saved.Run, Configs), "повтор ничего не меняет");
            Assert.IsEmpty(OutpostInvariants.Check(saved, MapOf(saved), Configs.Equipment));

            // Шахта 1 той же сборки: Store остаётся закрытым до шахты 2.
            var mine1 = NewGame(1, 34);
            mine1.Profile.Progression.Unlocked.Remove(ProgressionConfig.Store);
            ProgressionService.PrepareLoaded(mine1.Profile, mine1.Run, Configs);
            Assert.IsFalse(mine1.Profile.Progression.IsUnlocked(ProgressionConfig.Store));
        }

        [Test]
        public void RescueCage_FinalSection_ReachableWithRouteBack()
        {
            var map = OutpostLevelAdapter.BuildMine(TestData.Balance, 1, Configs);
            Assert.IsNotNull(map.Rescue);
            Assert.IsTrue(OutpostReachability.Check(map).Passed, "клетка не отрезает пол, путь к замку и обратно есть");
            var lockTarget = map.Target(map.Rescue.LockTargetId);
            Assert.IsNotNull(lockTarget, "замок — обычная цель");
            Assert.IsFalse(map.Props[map.Rescue.CageProp].PipeMount, "на клетке нет опор труб");
            Assert.AreNotEqual(map.Cell(lockTarget.Column, lockTarget.Row), map.RescueCageCell, "удары — по замку, не по клетке с Mia");
            foreach (int z in new[] { 3, 6 })
                Assert.IsNull(OutpostLevelAdapter.BuildMine(TestData.Balance, z, Configs).Rescue, "rescue cages belong to Mines 1 and 2");
        }

        // ───────────── Прежние профили ─────────────

        [Test]
        public void Migration_ExperiencedPlayer_KeepsAll_GetsMiaOnce_NoBeginnerIntros()
        {
            var s = StageProgressionService.NewGame(TestData.Balance, Configs.WithProgression(null), 41, Now, 4);
            var p = s.Profile;
            Assert.AreEqual(0, p.Progression.Version);
            p.CompletedZones.AddRange(new[] { 1, 2, 3 });
            EquipmentService.Grant(p, "pickaxe.rare", Configs);
            EquipmentService.EquipBest(p, Configs.Equipment);
            var card = Configs.Cards.Get("ca001");
            CardService.Grant(p, card, 10);
            p.Elixir = 500;
            Assert.IsNull(CardService.Upgrade(p, card, out _));
            var items = new Dictionary<string, int>(p.Items);
            long elixir = p.Elixir;

            Assert.IsTrue(ProgressionService.MigrateProfile(p, s.Run, Configs));
            Assert.IsFalse(ProgressionService.MigrateProfile(p, s.Run, Configs), "один раз");
            var pr = p.Progression;
            Assert.AreEqual(ProgressionService.CurrentVersion, pr.Version);
            foreach (string id in new[] { "branch.pickaxes", "branch.team", "cards", "store", "branch.bags", "equipment", "branch.boots", "branch.unload" })
                Assert.IsTrue(pr.IsUnlocked(id), id);
            Assert.IsTrue(pr.IsUnlocked("branch.value"), "Coin Value is immediately available");
            Assert.IsTrue(pr.IsUnlocked("branch.bays"), "Loading Bays is immediately available");
            Assert.IsTrue(pr.HasGrant(ProgressionService.GrantCards) && pr.HasGrant(ProgressionService.GrantEquipment),
                "опытному — без стартовой карточки и кирки");
            Assert.AreEqual(1, pr.Crew.Count(c => c == Mia), "Mia — за пройденную шахту 1");
            CollectionAssert.IsSubsetOf(items.Keys, p.Items.Keys, "ничего не отнято");
            Assert.AreEqual(2, p.CardLevel("ca001"));
            Assert.GreaterOrEqual(p.Elixir, elixir - 0);
        }

        [Test]
        public void Migration_EligibleEmptyInventory_GetsIntroContentOnce()
        {
            var s = StageProgressionService.NewGame(TestData.Balance, Configs.WithProgression(null), 42, Now, 3);
            var p = s.Profile;
            p.CompletedZones.AddRange(new[] { 1, 2 });
            Assert.IsTrue(ProgressionService.MigrateProfile(p, s.Run, Configs));
            Assert.AreEqual(1, p.ItemLevel(Configs.Progression.EquipmentIntro.Item), "стартовая кирка для пустой экипировки");
            Assert.AreEqual(1, p.CardLevel(Configs.Progression.CardsIntro.Card), "стартовая карточка");
            var copy = OutpostJson.DeepCopy(p);
            ProgressionService.ApplyEntryGrants(copy, 3, Configs);
            Assert.AreEqual(p.Elixir, copy.Elixir, "повтор ничего не выдаёт");
        }

        // ───────────── Loading Bays ─────────────

        [Test]
        public void LoadingBays_AddUnloadingSpots_CatsUnloadInParallel()
        {
            var s = NewGame(6, 51);
            var sim = Sim(s);
            Assert.AreEqual(OutpostIds.DepotSlotCount, s.Run.Slots.Length);
            sim.Submit(OutpostCommand.BonusGold(BigInteger.Pow(10, 9)));
            for (int i = 0; i < 6; i++) sim.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Team));
            var q = sim.Quote(UpgradeBranch.Bays, BuyMode.Max);
            int spots = sim.Map.DepotCells.Length;
            Assert.AreEqual(spots - OutpostIds.DepotSlotCount, q.Cap, "предел — места у склада по раскладке лагеря");
            Assert.IsTrue(sim.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Bays, BuyMode.Max)).Ok);
            Assert.AreEqual(spots, s.Run.Slots.Length, "новые места разгрузки");
            Assert.IsFalse(sim.Quote(UpgradeBranch.Bays, BuyMode.One).NextCost.HasValue, "дальше — «Max»");

            int maxBusy = 0;
            long end = sim.Now + Ticks(600);
            while (sim.Now < end && maxBusy < spots)
            {
                sim.Advance(sim.Now + Ticks(0.5));
                maxBusy = Math.Max(maxBusy, s.Run.Slots.Count(slot => slot.WorkerId != 0));
            }
            Assert.Greater(maxBusy, OutpostIds.DepotSlotCount, "одновременно разгружается больше котиков, чем мест без Loading Bays");
            Assert.IsEmpty(OutpostInvariants.Check(s, sim.Map, Configs.Equipment));
            var reloaded = OutpostJson.DeepCopy(s);
            OutpostStateUpgrade.Normalize(reloaded);
            Assert.AreEqual(spots, reloaded.Run.Slots.Length, "места сохраняются");
        }
    }

    /// <summary>
    /// Ограниченная проверка на выборке шахт (не вся кампания): прежняя выборка из 25 шахт MineDressingTests плюс шахты 4 и 6 —
    /// 27 разных шахт. По каждой: открытые разделы при входе, клетка спасения только в шахте 1, места разгрузки у склада, короткий
    /// прогон с покупками (включая Loading Bays до «Max») без нарушений инвариантов и снимок → загрузка.
    /// </summary>
    public sealed class FirstSessionSampleTests
    {
        public static readonly int[] Sample = { 1, 2, 3, 4, 5, 6, 7, 10, 16, 20, 23, 30, 37, 46, 49, 59, 66, 70, 80, 91, 105, 110, 120, 122, 130, 140, 144 };
        static OutpostConfigSet s_Configs;
        static OutpostConfigSet Configs => s_Configs ?? (s_Configs = OutpostConfigSet.LoadDefault().WithCards(TestData.Balance));

        [TestCaseSource(nameof(Sample))]
        public void Sample_EntryUnlocks_Rescue_Depot_ShortRun(int zone)
        {
            var s = StageProgressionService.NewGame(TestData.Balance, Configs, (ulong)(100 + zone), 1_800_000_000_000L, zone);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, s.Run);
            var p = s.Profile;
            foreach (var f in Configs.Progression.Features)
                if (f.Zone > 0) Assert.AreEqual(zone >= f.Zone, p.Progression.IsUnlocked(f.Id), f.Id + " в шахте " + zone);
            Assert.AreEqual(p.Progression.IsUnlocked("cards"), p.Progression.IsUnlocked("store"), "Store — вместе с Cards, шахта " + zone);
            Assert.AreEqual(zone <= 2, map.Rescue != null, "Mia in Mine 1, Bruno in Mine 2");
            Assert.That(map.DepotCells.Length, Is.InRange(OutpostIds.DepotSlotCount, OutpostIds.MaxDepotSlots));
            Assert.AreEqual(map.DepotCells.Length, map.DepotCells.Distinct().Count());
            foreach (int c in map.DepotCells) Assert.IsTrue(map.IsCampCell(c) && map.IsPlayable(c), "место разгрузки — проходимая клетка лагеря");
            Assert.IsTrue(OutpostReachability.Check(map).Passed, "проходимость шахты " + zone);

            var sim = new OutpostSimulation(s, map, Configs);
            sim.Submit(OutpostCommand.BonusGold(System.Numerics.BigInteger.Pow(10, 40)));
            foreach (UpgradeBranch b in Enum.GetValues(typeof(UpgradeBranch)))
            {
                var r = sim.Submit(OutpostCommand.BuyUpgrade(b, b == UpgradeBranch.Team ? BuyMode.One : BuyMode.Max));
                bool open = ProgressionService.BranchAvailable(p, Configs, b);
                Assert.AreEqual(open, r.Ok, b + " в шахте " + zone + ": " + r.Reason);
            }
            Assert.AreEqual(map.DepotCells.Length, s.Run.Slots.Length, "Loading Bays available from Mine 1");
            sim.Advance(sim.Now + (long)(120 * OutpostIds.TicksPerSecond));
            Assert.IsEmpty(OutpostInvariants.Check(s, map, Configs.Equipment), "шахта " + zone);
            var copy = OutpostJson.DeepCopy(s);
            OutpostStateUpgrade.Normalize(copy);
            Assert.IsEmpty(OutpostInvariants.Check(copy, OutpostLevelAdapter.ForRun(TestData.Balance, copy.Run), Configs.Equipment));
        }
    }
}
