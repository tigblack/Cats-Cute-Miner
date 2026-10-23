using System;
using System.Collections.Generic;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Outpost;
using Newtonsoft.Json;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostKitA;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Награда первого завершения (ТЗ §10.2, §10.3, §11.3, §12.3; CORE_SPEC §10): гарантии и шахты без предмета (rewards-2,
    /// первая сессия) без продвижения потока 5, случайный слот/редкость из Pcg32, однократность по ключу, детали завершения +
    /// дубликата, конец кампании на zone144.
    /// Эталон потока — собственная копия Pcg32(seed, 5) в тесте.
    /// </summary>
    [TestFixture]
    public class OutpostRewardTests
    {
        static RewardRecord SingleRecord(OutpostGameState s)
        {
            Assert.AreEqual(1, s.Profile.RewardLog.Count, "ровно одна запись награды");
            return s.Profile.RewardLog[0];
        }

        static void AssertSameRng(Pcg32 expected, Pcg32 actual, string message)
        {
            Assert.AreEqual(expected.State, actual.State, message + " (State)");
            Assert.AreEqual(expected.Inc, actual.Inc, message + " (Inc)");
        }

        /// <summary>Первый seed, у которого второй бросок потока 5 (редкость) равен roll.</summary>
        static ulong FindSeedForRoll(int roll, out int slot)
        {
            for (ulong seed = 1; seed < 2_000_000; seed++)
            {
                var rng = new Pcg32(seed, 5);
                int s = rng.NextInt(4);
                if (rng.NextInt(100) == roll)
                {
                    slot = s;
                    return seed;
                }
            }
            throw new InvalidOperationException("не найден seed для броска " + roll);
        }

        // ───────────────────────────── Гарантии ─────────────────────────────

        // ТЗ §10.2: «Гарантии … выбирают предмет напрямую и не продвигают поток»; первая сессия (rewards-2): гарантии шахт
        // 3–5/10/25/50 — обычные шляпа, рюкзак и обувь сдвинуты на шахту позже, кирка шахты 1 убрана (стартовая кирка — знакомство
        // с экипировкой при входе в шахту 3).
        [TestCase(3, "hat.common")]
        [TestCase(4, "backpack.common")]
        [TestCase(5, "boots.common")]
        [TestCase(10, "pickaxe.rare")]
        [TestCase(25, "pickaxe.epic")]
        [TestCase(50, "pickaxe.legendary")]
        public void Guaranteed_GrantsFixedItem_WithoutAdvancingStream5(int zone, string itemId)
        {
            var s = RewardState(zone, 12345);
            var before = s.Profile.RewardRng.Clone();
            ApplyCompletion(s);
            AssertSameRng(before, s.Profile.RewardRng, "гарантия не трогает поток наград");
            var rec = SingleRecord(s);
            Assert.AreEqual(itemId, rec.ItemId);
            Assert.IsTrue(rec.Guaranteed);
            Assert.IsFalse(rec.Duplicate);
            Assert.AreEqual(0, rec.DuplicateParts);
            Assert.AreEqual(CompletionParts(zone), rec.CompletionParts);
            Assert.AreEqual(RewardKey(zone), rec.Key);
            Assert.AreEqual(zone, rec.Zone);
            Assert.AreEqual("run-" + zone, rec.RunId);
            Assert.AreEqual(1, LevelOf(s.Profile, itemId));
            Assert.AreEqual((long)CompletionParts(zone), s.Profile.Parts);
            CollectionAssert.AreEqual(new[] { zone }, s.Profile.CompletedZones);
            Assert.AreEqual(RewardKey(zone), s.Run.CompletionRewardKey);
            Assert.AreEqual(1, s.Profile.RewardRngVersion);
        }

        // Первая сессия (rewards-2, NoItem): шахты 1 и 2 за первое завершение дают только детали — без предмета, без броска потока 5
        // и без конвертации дубликата (имеющийся предмет того же слота не важен); запись награды и ключ — как у остальных шахт.
        [TestCase(1)]
        [TestCase(2)]
        public void NoItemZone_OnlyCompletionParts_WithoutAdvancingStream5(int zone)
        {
            var s = RewardState(zone, 12345);
            Own(s.Profile, "pickaxe.common", 4);
            s.Profile.Parts = 11;
            var before = s.Profile.RewardRng.Clone();
            ApplyCompletion(s);
            AssertSameRng(before, s.Profile.RewardRng, "шахта без предмета не трогает поток наград");
            var rec = SingleRecord(s);
            Assert.IsNull(rec.ItemId, "предмета нет");
            Assert.IsFalse(rec.Guaranteed);
            Assert.IsFalse(rec.Duplicate);
            Assert.AreEqual(0, rec.DuplicateParts);
            Assert.AreEqual(CompletionParts(zone), rec.CompletionParts);
            Assert.AreEqual(RewardKey(zone), rec.Key);
            Assert.AreEqual(zone, rec.Zone);
            Assert.AreEqual("run-" + zone, rec.RunId);
            Assert.AreEqual(11L + CompletionParts(zone), s.Profile.Parts, "только детали завершения");
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { ["pickaxe.common"] = 4 }, s.Profile.Items, "коллекция не меняется");
            CollectionAssert.AreEqual(new[] { zone }, s.Profile.CompletedZones);
            Assert.AreEqual(RewardKey(zone), s.Run.CompletionRewardKey);
        }

        // ───────────────────────────── Случайные награды ─────────────────────────────

        // ТЗ §10.2: «Сначала NextInt(4) выбирает слот, затем NextInt(100) выбирает редкость». Первая сессия (rewards-2): первая
        // случайная шахта — 6 (шахты 1–2 без предмета, 3–5 — гарантии), дальше — шахты после гарантий 10/25/50.
        [Test]
        public void RandomZone_SlotThenRarity_FromStream5(
            [Values(6, 7, 11, 26, 51, 99, 143)] int zone,
            [Values(1UL, 42UL, 987654321UL)] ulong seed)
        {
            var s = RewardState(zone, seed);
            var mirror = new Pcg32(seed, 5);
            string expected = ExpectedRandomItem(mirror);
            ApplyCompletion(s);
            var rec = SingleRecord(s);
            Assert.AreEqual(expected, rec.ItemId);
            Assert.IsFalse(rec.Guaranteed);
            AssertSameRng(mirror, s.Profile.RewardRng, "поток продвинут ровно двумя бросками");
            Assert.AreEqual(1, LevelOf(s.Profile, expected));
            Assert.AreEqual((long)CompletionParts(zone), s.Profile.Parts);
        }

        // ТЗ §10.2: «0–69 обычная, 70–93 редкая, 94–98 эпическая, 99 легендарная».
        [TestCase(0, 0)]
        [TestCase(69, 0)]
        [TestCase(70, 1)]
        [TestCase(93, 1)]
        [TestCase(94, 2)]
        [TestCase(98, 2)]
        [TestCase(99, 3)]
        public void RandomZone_RarityThresholds(int roll, int rarity)
        {
            ulong seed = FindSeedForRoll(roll, out int slot);
            var s = RewardState(7, seed);
            ApplyCompletion(s);
            Assert.AreEqual(ItemId(slot, rarity), SingleRecord(s).ItemId, "seed " + seed + ", бросок " + roll);
        }

        // Поток продолжается между шахтами; гарантии zone5 в начале (rewards-2: обычная обувь) и zone10 посередине его не трогают.
        [Test]
        public void Sequence_5To12_ContinuesStream_GuaranteeSkipsIt()
        {
            const ulong seed = 20261008UL;
            var profile = NewProfile(seed);
            var mirror = new Pcg32(seed, 5);
            for (int zone = 5; zone <= 12; zone++)
            {
                var s = RewardState(zone, seed, profile);
                string expected = GuaranteedItem(zone) ?? ExpectedRandomItem(mirror);
                ApplyCompletion(s);
                var rec = profile.RewardLog.Last();
                Assert.AreEqual(expected, rec.ItemId, "zone" + zone);
                AssertSameRng(mirror, profile.RewardRng, "zone" + zone);
            }
            Assert.AreEqual(8, profile.RewardLog.Count);
        }

        // ───────────────────────────── Однократность ─────────────────────────────

        // ТЗ §10.2: «Награда помечается ключом (modeId, zoneId, firstCompletion); runId не даёт права получать её заново».
        [TestCase(1)]
        [TestCase(7)]
        public void ApplyCompletionTwice_SecondCallGrantsNothing(int zone)
        {
            var s = RewardState(zone, 99);
            ApplyCompletion(s);
            string afterFirst = JsonOf(s.Profile);
            ApplyCompletion(s);
            Assert.AreEqual(afterFirst, JsonOf(s.Profile), "повтор не меняет профиль: предмет, детали, PRNG, журнал");
            s.Run.RunId = "run-other";
            s.Run.CompletionRewardKey = null;
            ApplyCompletion(s);
            Assert.AreEqual(afterFirst, JsonOf(s.Profile), "новый runId не даёт права на повторную награду");
            Assert.AreEqual(1, s.Profile.RewardLog.Count);
            Assert.AreEqual(1, s.Profile.CompletedZones.Count(z => z == zone));
        }

        // ТЗ §11.3: «Повторный тап, загрузка после выдачи … не создают дубликаты» — повтор после save/load профиля.
        [Test]
        public void ApplyCompletion_AfterProfileReload_GrantsNothing()
        {
            var s = RewardState(8, 5);
            ApplyCompletion(s);
            var reloaded = JsonConvert.DeserializeObject<OutpostGameState>(JsonOf(s), OutpostJson.Settings);
            string before = JsonOf(reloaded.Profile);
            ApplyCompletion(reloaded);
            Assert.AreEqual(before, JsonOf(reloaded.Profile));
        }

        // ───────────────────────────── Детали ─────────────────────────────

        // ТЗ §12.3: «Эти детали выдаются вместе с предметом независимо от того, новая это копия или дубликат.
        // Конвертация дубликата добавляется к указанной награде». Обычные предметы — гарантии шахт 3 и 4 (rewards-2).
        [TestCase(3, "hat.common", 5, 10)]
        [TestCase(4, "backpack.common", 5, 10)]
        [TestCase(10, "pickaxe.rare", 15, 20)]
        [TestCase(25, "pickaxe.epic", 40, 35)]
        [TestCase(50, "pickaxe.legendary", 120, 60)]
        public void Duplicate_PartsAddedToCompletionParts(int zone, string itemId, int duplicateParts, int completionParts)
        {
            var s = RewardState(zone, 3);
            Own(s.Profile, itemId, 4);
            s.Profile.Parts = 11;
            ApplyCompletion(s);
            var rec = SingleRecord(s);
            Assert.AreEqual(itemId, rec.ItemId);
            Assert.IsTrue(rec.Duplicate);
            Assert.AreEqual(duplicateParts, rec.DuplicateParts);
            Assert.AreEqual(completionParts, rec.CompletionParts);
            Assert.AreEqual(11L + duplicateParts + completionParts, s.Profile.Parts);
            Assert.AreEqual(4, LevelOf(s.Profile, itemId), "дубликат не меняет уровень");
        }

        // ТЗ §12.5: детали zone1/4/5/10/144 = 10/10/15/20/150 (новый предмет — без деталей дубликата).
        [TestCase(1, 10)]
        [TestCase(4, 10)]
        [TestCase(5, 15)]
        [TestCase(9, 15)]
        [TestCase(10, 20)]
        [TestCase(144, 150)]
        public void CompletionParts_PerZone(int zone, int parts)
        {
            var s = RewardState(zone, 1);
            ApplyCompletion(s);
            var rec = SingleRecord(s);
            Assert.IsFalse(rec.Duplicate);
            Assert.AreEqual(parts, rec.CompletionParts);
            Assert.AreEqual((long)parts, s.Profile.Parts);
        }

        // ТЗ §10.2: «Новый предмет добавляется в коллекцию; … автоматической смены комплекта при награде нет». Первый предмет-награда
        // кампании (rewards-2) — обычная шляпа шахты 3.
        [Test]
        public void Reward_IsNotAutoEquipped()
        {
            var s = RewardState(3, 1);
            ApplyCompletion(s);
            Assert.AreEqual(1, LevelOf(s.Profile, "hat.common"));
            Assert.IsTrue(s.Profile.Equipped.All(e => e == null));
        }

        // ───────────────────────────── Кампания и zone144 ─────────────────────────────

        // ТЗ §11.3: «После zone144 сохрани CampaignComplete … Не создавай zone145».
        [Test]
        public void Zone144_SetsCampaignComplete_AndNoTransition()
        {
            var s = RewardState(144, 77);
            ApplyCompletion(s);
            Assert.IsTrue(s.Profile.CampaignComplete);
            CollectionAssert.Contains(s.Profile.CompletedZones, 144);
            s.Run.Phase = RunPhase.ReadyToContinue;
            string before = JsonOf(s);
            Assert.IsNull(TryPrepareTransition(s), "перехода в zone145 нет");
            Assert.AreEqual(before, JsonOf(s), "отказ перехода ничего не меняет");
            Assert.AreEqual(144, s.Profile.CurrentZone);
            ApplyCompletion(s);
            Assert.AreEqual(1, s.Profile.RewardLog.Count, "награда за последнюю шахту не выдаётся повторно");
        }

        [Test]
        public void Zone143_DoesNotCompleteCampaign()
        {
            var s = RewardState(143, 77);
            ApplyCompletion(s);
            Assert.IsFalse(s.Profile.CampaignComplete);
        }

        // ТЗ §11.3: переход возможен только из ReadyToContinue и создаёт следующую шахту с тем же профилем.
        [Test]
        public void Transition_143To144_CopiesProfile_ResetsRun()
        {
            var s = RewardState(143, 77);
            ApplyCompletion(s);
            Assert.IsNull(TryPrepareTransition(s), "до ReadyToContinue переход запрещён");
            s.Run.Phase = RunPhase.ReadyToContinue;
            var next = TryPrepareTransition(s);
            Assert.IsNotNull(next);
            Assert.AreEqual(144, next.Profile.CurrentZone);
            Assert.AreEqual(144, next.Run.Zone);
            Assert.AreEqual("zone144", next.Run.ZoneId);
            Assert.AreEqual(RunPhase.Running, next.Run.Phase);
            CollectionAssert.AreEqual(new int[OutpostIds.BranchCount], next.Run.Levels, "все семь веток (профиль b8) — 0");
            Assert.AreEqual(2, next.Run.Workers.Count);
            Assert.AreEqual(System.Numerics.BigInteger.Zero, next.Run.Wallet);
            CollectionAssert.AreEquivalent(s.Profile.Items, next.Profile.Items, "предметы и уровни сохранены");
            CollectionAssert.AreEqual(s.Profile.RewardLog.Select(r => r.Key), next.Profile.RewardLog.Select(r => r.Key), "журнал наград сохранён");
            Assert.AreEqual(s.Profile.Parts, next.Profile.Parts, "детали сохранены");
            next.Profile.Items["hat.legendary"] = 20;
            Assert.IsFalse(s.Profile.Items.ContainsKey("hat.legendary"), "глубокая копия профиля");
            Assert.AreEqual(143, s.Profile.CurrentZone, "исходный снимок не изменён");
        }

        // ТЗ §20: «Отдельный последовательный прогон кампании проверяет награды 1–4/10/25/50, конвертацию дубликатов»; первая
        // сессия (rewards-2): шахты 1–2 — только детали, гарантии 3–5/10/25/50, случайный поток начинается с шахты 6.
        [TestCase(1UL)]
        [TestCase(31337UL)]
        public void Campaign_1To144_RewardsMatchMirrorStream(ulong seed)
        {
            var profile = NewProfile(seed);
            var mirror = new Pcg32(seed, 5);
            var owned = new HashSet<string>();
            long expectedParts = 0;
            int completionTotal = 0;
            for (int zone = 1; zone <= 144; zone++)
            {
                var s = RewardState(zone, seed, profile);
                string guaranteed = GuaranteedItem(zone);
                string expected = NoItemZone(zone) ? null : (guaranteed ?? ExpectedRandomItem(mirror));
                bool duplicate = expected != null && !owned.Add(expected);
                int dupParts = duplicate ? DuplicatePartsByRarity[RarityOf(expected)] : 0;
                expectedParts += CompletionParts(zone) + dupParts;
                completionTotal += CompletionParts(zone);

                ApplyCompletion(s);
                var rec = profile.RewardLog.Last();
                string at = "zone" + zone;
                Assert.AreEqual(expected, rec.ItemId, at);
                Assert.AreEqual(guaranteed != null, rec.Guaranteed, at);
                Assert.AreEqual(duplicate, rec.Duplicate, at);
                Assert.AreEqual(dupParts, rec.DuplicateParts, at);
                Assert.AreEqual(expectedParts, profile.Parts, at);
                AssertSameRng(mirror, profile.RewardRng, at);
                Assert.AreEqual(zone == 144, profile.CampaignComplete, at);
            }
            Assert.AreEqual(144, profile.RewardLog.Count);
            CollectionAssert.AreEqual(new[] { 1, 2 }, profile.RewardLog.Where(r => r.ItemId == null).Select(r => r.Zone).ToArray(),
                "без предмета — только шахты 1 и 2");
            CollectionAssert.AreEqual(Enumerable.Range(1, 144).ToArray(), profile.CompletedZones);
            Assert.AreEqual(11590, completionTotal, "controls.guaranteed_completion_parts_total_campaign");
            CollectionAssert.AreEquivalent(owned, profile.Items.Keys);
            Assert.IsTrue(profile.Items.Values.All(q => q == 1), "награды не повышают уровень предметов");
            Assert.AreEqual(144, profile.RewardLog.Select(r => r.Key).Distinct().Count(), "ключи уникальны");
        }

        // ТЗ §10.2: «Seed нового профиля создаётся один раз … отдельный поток с номером 5».
        [Test]
        public void NewProfile_RewardRngIsStream5OfSeed()
        {
            var p = StageProgressionService.NewProfile(Configs, 4242UL);
            var expected = new Pcg32(p.Seed, 5);
            AssertSameRng(expected, p.RewardRng, "RewardRng = Pcg32(Seed, 5)");
            Assert.AreEqual(1, p.RewardRngVersion);
            Assert.AreEqual("CatOutpostV2", p.ModeId);
            // ТЗ 2.2 §10.1, §16.1 (б): новый профиль пишется схемой 3.
            Assert.AreEqual(3, p.SchemaVersion);
            Assert.AreEqual(OutpostIds.SchemaVersion, p.SchemaVersion);
            Assert.AreEqual(1, p.CurrentZone);
            Assert.AreEqual(0L, p.Parts);
            Assert.IsTrue(p.Equipped.All(e => e == null));
        }
    }
}
