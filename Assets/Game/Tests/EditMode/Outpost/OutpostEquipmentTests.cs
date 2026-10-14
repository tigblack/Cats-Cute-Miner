using System;
using System.Collections.Generic;
using System.Linq;
using CatMiners.Core.Outpost;
using Newtonsoft.Json;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostKitA;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Постоянная экипировка (ТЗ §10, §12.3; CORE_SPEC §9): каталог 16 предметов, получение и дубликаты,
    /// «Надеть лучшее» по эффекту, улучшение за детали с cap 20, однократное применение бонусов.
    /// </summary>
    [TestFixture]
    public class OutpostEquipmentTests
    {
        static IEnumerable<string> AllItems() => AllItemIds();

        static EquipmentServiceA Svc() => NewEquipmentService();

        static CommandResult Equip(OutpostProfile p, string id) => Svc().Equip(p, id);
        static CommandResult Unequip(OutpostProfile p, int slot) => Svc().Unequip(p, (EquipSlot)slot);
        static CommandResult EquipBest(OutpostProfile p) => Svc().EquipBest(p);
        static CommandResult Upgrade(OutpostProfile p, string id) => Svc().UpgradeItem(p, id);

        // ───────────────────────────── Каталог ─────────────────────────────

        // ТЗ §10.1: «ровно 16 игровых записей … порядок слотов: кирка, шляпа, рюкзак, обувь; редкости common…legendary».
        [Test]
        public void Catalog_Has16ItemsInSlotTimesRarityOrder()
        {
            var items = Catalog.Defs.ToList();
            CollectionAssert.AreEqual(AllItemIds().ToArray(), items.Select(i => i.Id).ToArray());
            for (int i = 0; i < 16; i++)
            {
                Assert.AreEqual(i / 4, (int)items[i].Slot, items[i].Id);
                Assert.AreEqual(i % 4, (int)items[i].Rarity, items[i].Id);
            }
        }

        [Test]
        public void Enums_SlotAndRarityOrder()
        {
            Assert.AreEqual(0, (int)EquipSlot.Pickaxe);
            Assert.AreEqual(1, (int)EquipSlot.Hat);
            Assert.AreEqual(2, (int)EquipSlot.Backpack);
            Assert.AreEqual(3, (int)EquipSlot.Boots);
            Assert.AreEqual(0, (int)ItemRarity.Common);
            Assert.AreEqual(1, (int)ItemRarity.Rare);
            Assert.AreEqual(2, (int)ItemRarity.Epic);
            Assert.AreEqual(3, (int)ItemRarity.Legendary);
        }

        // ───────────────────────────── Получение и дубликаты ─────────────────────────────

        // ТЗ §10.3: «Первая копия добавляется в коллекцию на уровне 1».
        [TestCaseSource(nameof(AllItems))]
        public void Grant_NewItem_AddedAtLevel1_WithoutParts(string id)
        {
            var p = NewProfile(1);
            var (duplicate, parts) = Svc().Grant(p, id);
            Assert.IsFalse(duplicate);
            Assert.AreEqual(0, parts);
            Assert.AreEqual(1, LevelOf(p, id));
            Assert.AreEqual(0L, p.Parts);
            Assert.AreEqual(1, p.Items.Count);
        }

        // ТЗ §10.3: «Повторное получение того же предмета превращается в постоянные Детали: 5 / 15 / 40 / 120 по редкости».
        [TestCase(0, 5)]
        [TestCase(1, 15)]
        [TestCase(2, 40)]
        [TestCase(3, 120)]
        public void Grant_Duplicate_ConvertsToPartsByRarity(int rarity, int expectedParts)
        {
            for (int slot = 0; slot < 4; slot++)
            {
                string id = ItemId(slot, rarity);
                var p = NewProfile(1);
                Own(p, id, 3);
                p.Parts = 7;
                var (duplicate, parts) = Svc().Grant(p, id);
                Assert.IsTrue(duplicate, id);
                Assert.AreEqual(expectedParts, parts, id);
                Assert.AreEqual(7L + expectedParts, p.Parts, id);
                Assert.AreEqual(3, LevelOf(p, id), "дубликат не повышает уровень: " + id);
                Assert.AreEqual(1, p.Items.Count, "коллекция хранит одну запись предмета");
            }
        }

        // ТЗ §10.3: «Предмет уровня 20 улучшать нельзя; детали за дубликаты продолжают начисляться».
        [Test]
        public void Grant_DuplicateOfMaxLevelItem_StillGivesParts()
        {
            var p = NewProfile(1);
            Own(p, "boots.legendary", 20);
            var (duplicate, parts) = Svc().Grant(p, "boots.legendary");
            Assert.IsTrue(duplicate);
            Assert.AreEqual(120, parts);
            Assert.AreEqual(120L, p.Parts);
            Assert.AreEqual(20, LevelOf(p, "boots.legendary"));
        }

        // ТЗ §10.2: «автоматической смены комплекта при награде нет».
        [Test]
        public void Grant_DoesNotEquip()
        {
            var p = NewProfile(1);
            Wear(p, "pickaxe.common", 1);
            Svc().Grant(p, "pickaxe.legendary");
            Svc().Grant(p, "hat.rare");
            Assert.AreEqual("pickaxe.common", p.Equipped[0]);
            Assert.IsNull(p.Equipped[1]);
        }

        // ───────────────────────────── Надеть / снять ─────────────────────────────

        // ТЗ §12.3: «Два разных предмета одного слота одновременно бонус не дают».
        [Test]
        public void Equip_SameSlotReplaces_OnlyOneBonusApplies()
        {
            var p = NewProfile(1);
            Own(p, "pickaxe.common", 1);
            Own(p, "pickaxe.rare", 1);
            Assert.IsTrue(Equip(p, "pickaxe.common").Ok);
            Assert.IsTrue(Equip(p, "pickaxe.rare").Ok);
            Assert.AreEqual("pickaxe.rare", p.Equipped[0]);
            AssertExact(Stats(null, p).FinalDamage, FracA.Of(5) * FracA.Of(118, 100), "только +18% редкой кирки");
            Assert.AreEqual(2, p.Items.Count, "заменённый предмет остаётся в коллекции");
        }

        // ТЗ §10.3: «Надевание бесплатно».
        [Test]
        public void Equip_IsFree()
        {
            var p = NewProfile(1);
            Own(p, "hat.epic", 4);
            p.Parts = 33;
            Assert.IsTrue(Equip(p, "hat.epic").Ok);
            Assert.AreEqual(33L, p.Parts);
            Assert.AreEqual(4, LevelOf(p, "hat.epic"));
        }

        [Test]
        public void Equip_NotOwned_Rejected()
        {
            var p = NewProfile(1);
            Wear(p, "boots.common", 1);
            var r = Equip(p, "boots.legendary");
            Assert.IsFalse(r.Ok, "нельзя надеть предмет вне коллекции");
            Assert.AreEqual("boots.common", p.Equipped[3]);
            Assert.IsFalse(p.Items.ContainsKey("boots.legendary"));
        }

        // ТЗ §12.3: «Если рюкзак не надет, прибавка равна 0».
        [Test]
        public void Unequip_RemovesBonus_KeepsItem()
        {
            var p = NewProfile(1);
            Wear(p, "backpack.epic", 1);
            Assert.AreEqual(10, Stats(null, p).Capacity);
            Assert.IsTrue(Unequip(p, 2).Ok);
            Assert.IsNull(p.Equipped[2]);
            Assert.AreEqual(4, Stats(null, p).Capacity);
            Assert.AreEqual(1, LevelOf(p, "backpack.epic"));
        }

        // ───────────────────────────── Надеть лучшее ─────────────────────────────

        // ТЗ §10.3: «обычный рюкзак уровня 20 даёт +20 порций, легендарный уровня 1 — +10»: сравнивать эффект, не цвет.
        [Test]
        public void EquipBest_Backpack_ComparesCapacityNotRarity()
        {
            var p = NewProfile(1);
            Own(p, "backpack.common", 20);
            Own(p, "backpack.legendary", 1);
            Assert.IsTrue(EquipBest(p).Ok);
            Assert.AreEqual("backpack.common", p.Equipped[2]);
            Assert.AreEqual(24, Stats(null, p).Capacity);
        }

        // Редкая 20: 0.18·(1+0.95) = 0.351 > эпическая 1: 0.35 — точное сравнение дробей.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void EquipBest_PercentSlots_ComparesBonusWithLevel(int slot)
        {
            var p = NewProfile(1);
            Own(p, ItemId(slot, 1), 20);
            Own(p, ItemId(slot, 2), 1);
            EquipBest(p);
            Assert.AreEqual(ItemId(slot, 1), p.Equipped[slot]);
        }

        // Обычная 20: 0.08·1.95 = 0.156 < редкая 1: 0.18.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void EquipBest_HigherBonusWins(int slot)
        {
            var p = NewProfile(1);
            Own(p, ItemId(slot, 0), 20);
            Own(p, ItemId(slot, 1), 1);
            EquipBest(p);
            Assert.AreEqual(ItemId(slot, 1), p.Equipped[slot]);
        }

        // ТЗ §10.3: «При равном эффекте оставить уже надетый предмет».
        // Эпическая 17: 0.35·(1+0.8) = 0.63 = легендарная 2: 0.60·(1+0.05).
        [TestCase(0, 2)]
        [TestCase(0, 3)]
        [TestCase(1, 2)]
        [TestCase(1, 3)]
        [TestCase(3, 2)]
        [TestCase(3, 3)]
        public void EquipBest_PercentTie_KeepsEquipped(int slot, int equippedRarity)
        {
            var p = NewProfile(1);
            Own(p, ItemId(slot, 2), 17);
            Own(p, ItemId(slot, 3), 2);
            AssertExact(Catalog.ItemBonus(ItemId(slot, 2), 17), FracA.Of(63, 100), "самопроверка равенства");
            AssertExact(Catalog.ItemBonus(ItemId(slot, 3), 2), FracA.Of(63, 100));
            p.Equipped[slot] = ItemId(slot, equippedRarity);
            EquipBest(p);
            Assert.AreEqual(ItemId(slot, equippedRarity), p.Equipped[slot]);
        }

        // Рюкзаки +10: обычный 10 (1+9), редкий 8 (3+7), легендарный 1 (10).
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void EquipBest_BackpackTie_KeepsEquipped(int equippedRarity)
        {
            var p = NewProfile(1);
            Own(p, "backpack.common", 10);
            Own(p, "backpack.rare", 8);
            Own(p, "backpack.legendary", 1);
            p.Equipped[2] = ItemId(2, equippedRarity);
            EquipBest(p);
            Assert.AreEqual(ItemId(2, equippedRarity), p.Equipped[2]);
            Assert.AreEqual(14, Stats(null, p).Capacity);
        }

        // ТЗ §10.3: «иначе выбрать по стабильному ID» — CORE_SPEC §9: первый по порядку каталога.
        [Test]
        public void EquipBest_TieWithEmptySlot_TakesFirstInCatalogOrder()
        {
            var p = NewProfile(1);
            Own(p, "hat.legendary", 2);
            Own(p, "hat.epic", 17);
            Own(p, "backpack.legendary", 1);
            Own(p, "backpack.rare", 8);
            Own(p, "backpack.common", 10);
            EquipBest(p);
            Assert.AreEqual("hat.epic", p.Equipped[1]);
            Assert.AreEqual("backpack.common", p.Equipped[2]);
        }

        [Test]
        public void EquipBest_SlotWithoutItems_StaysEmpty_OtherSlotsFilled()
        {
            var p = NewProfile(1);
            Own(p, "boots.rare", 3);
            EquipBest(p);
            Assert.IsNull(p.Equipped[0]);
            Assert.IsNull(p.Equipped[1]);
            Assert.IsNull(p.Equipped[2]);
            Assert.AreEqual("boots.rare", p.Equipped[3]);
        }

        [Test]
        public void EquipBest_ReplacesWorseEquipped_AllSlots()
        {
            var p = NewProfile(1);
            foreach (int slot in new[] { 0, 1, 2, 3 })
            {
                Wear(p, ItemId(slot, 0), 1);
                Own(p, ItemId(slot, 3), 1);
            }
            EquipBest(p);
            for (int slot = 0; slot < 4; slot++) Assert.AreEqual(ItemId(slot, 3), p.Equipped[slot]);
            Assert.AreEqual(8, p.Items.Count, "надевание ничего не расходует");
        }

        // ───────────────────────────── Улучшение предмета ─────────────────────────────

        // ТЗ §12.3: PartsCost = ceil(6 · 1.17^(q−1) · RarityCostMultiplier).
        [TestCase(0, new long[] { 6, 8, 9, 10, 12, 14, 16, 19, 22, 25, 29, 34, 40, 47, 55, 64, 74, 87, 102 })]
        [TestCase(1, new long[] { 9, 11, 13, 15, 17, 20, 24, 28, 32, 37, 44, 51, 60, 70, 82, 95, 111, 130, 152 })]
        [TestCase(2, new long[] { 15, 18, 21, 25, 29, 33, 39, 46, 53, 62, 73, 85, 99, 116, 136, 159, 185, 217, 254 })]
        [TestCase(3, new long[] { 24, 29, 33, 39, 45, 53, 62, 73, 85, 99, 116, 135, 158, 185, 217, 253, 296, 347, 406 })]
        public void UpgradeCost_PerLevel_MatchesFormula(int rarity, long[] costs)
        {
            for (int slot = 0; slot < 4; slot++)
                for (int q = 1; q < 20; q++)
                {
                    var expected = (FracA.Of(6) * FracA.Of(117, 100).Pow(q - 1) * new[] { FracA.Of(1), FracA.Of(3, 2), FracA.Of(5, 2), FracA.Of(4) }[rarity]).Ceil();
                    Assert.AreEqual((long)expected, costs[q - 1], "самопроверка эталона");
                    Assert.AreEqual(costs[q - 1], Catalog.UpgradeCost(ItemId(slot, rarity), q), ItemId(slot, rarity) + " q" + q);
                }
        }

        [Test]
        public void UpgradeItem_SpendsExactCost_AndRaisesLevel()
        {
            var p = NewProfile(1);
            Own(p, "pickaxe.common", 1);
            p.Parts = 9;
            var r = Upgrade(p, "pickaxe.common");
            Assert.IsTrue(r.Ok, r.Reason);
            Assert.AreEqual(2, LevelOf(p, "pickaxe.common"));
            Assert.AreEqual(3L, p.Parts, "списано ровно 6");
        }

        // CORE_SPEC §9: нет деталей — "parts".
        [Test]
        public void UpgradeItem_InsufficientParts_RejectedWithoutChanges()
        {
            var p = NewProfile(1);
            Own(p, "hat.legendary", 1);
            p.Parts = 23;
            var r = Upgrade(p, "hat.legendary");
            Assert.IsFalse(r.Ok);
            Assert.AreEqual("parts", r.Reason);
            Assert.AreEqual(1, LevelOf(p, "hat.legendary"));
            Assert.AreEqual(23L, p.Parts);
        }

        // ТЗ §10.3: «Максимальный уровень предмета — 20»; CORE_SPEC §9: "max".
        [Test]
        public void UpgradeItem_AtLevel20_RejectedWithMax()
        {
            var p = NewProfile(1);
            Own(p, "boots.epic", 20);
            p.Parts = 100_000;
            var r = Upgrade(p, "boots.epic");
            Assert.IsFalse(r.Ok);
            Assert.AreEqual("max", r.Reason);
            Assert.AreEqual(20, LevelOf(p, "boots.epic"));
            Assert.AreEqual(100_000L, p.Parts);
        }

        [Test]
        public void UpgradeItem_NotOwned_Rejected()
        {
            var p = NewProfile(1);
            p.Parts = 1000;
            var r = Upgrade(p, "pickaxe.epic");
            Assert.IsFalse(r.Ok);
            Assert.IsFalse(p.Items.ContainsKey("pickaxe.epic"));
            Assert.AreEqual(1000L, p.Parts);
        }

        // ТЗ §12.5: полная стоимость 1→20 — 673 / 1001 / 1665 / 2655; ровно столько деталей хватает, после — "max".
        [TestCase(0, 673)]
        [TestCase(1, 1001)]
        [TestCase(2, 1665)]
        [TestCase(3, 2655)]
        public void UpgradeItem_FullPath1To20_ConsumesExactTotal(int rarity, int total)
        {
            string id = ItemId(1, rarity);
            var p = NewProfile(1);
            Own(p, id, 1);
            p.Parts = total;
            for (int q = 1; q < 20; q++) Assert.IsTrue(Upgrade(p, id).Ok, "уровень " + q);
            Assert.AreEqual(20, LevelOf(p, id));
            Assert.AreEqual(0L, p.Parts);
            Assert.AreEqual("max", Upgrade(p, id).Reason);
        }

        [Test]
        public void UpgradeItem_OneShortOfFullPath_StopsAtLevel19()
        {
            var p = NewProfile(1);
            Own(p, "backpack.common", 1);
            p.Parts = 672;
            int ok = 0;
            while (Upgrade(p, "backpack.common").Ok) ok++;
            // 1→19 стоит 673 − 102 = 571; остаётся 101, а шаг 19→20 стоит 102
            Assert.AreEqual(18, ok);
            Assert.AreEqual(19, LevelOf(p, "backpack.common"));
            Assert.AreEqual(101L, p.Parts);
            Assert.AreEqual("parts", Upgrade(p, "backpack.common").Reason);
        }

        // Улучшение надетого предмета сразу меняет итоговую характеристику (ТЗ §18: расчёт централизован).
        [Test]
        public void UpgradeEquippedBackpack_RaisesCapacity()
        {
            var p = NewProfile(1);
            Wear(p, "backpack.common", 1);
            p.Parts = 6;
            Assert.AreEqual(5, Stats(null, p).Capacity);
            Assert.IsTrue(Upgrade(p, "backpack.common").Ok);
            Assert.AreEqual(6, Stats(null, p).Capacity);
        }

        // ───────────────────────────── Однократность бонусов ─────────────────────────────

        // ТЗ §12.3: «Повторная загрузка сцены не добавляет тот же процент заново»; §20 «Один бонус применяется один раз».
        [Test]
        public void Bonuses_RecomputeTwice_SameStats_ProfileUnchanged()
        {
            var p = NewProfile(1);
            Wear(p, "pickaxe.legendary", 20);
            Wear(p, "hat.epic", 7);
            Wear(p, "backpack.rare", 4);
            Wear(p, "boots.common", 12);
            string before = JsonOf(p);
            var a = Stats(null, p);
            var b = Stats(null, p);
            Assert.AreEqual(before, JsonOf(p), "расчёт характеристик не меняет профиль");
            AssertSameStats(a, b);
            AssertExact(a.FinalDamage, FracA.Of(5) * FracA.Of(217, 100));
            AssertExact(a.FinalValue, FracA.Of(1) + FracA.Of(7, 20) * (FracA.Of(1) + FracA.Of(6, 20)));
            Assert.AreEqual(4 + 3 + 3, a.Capacity);
        }

        [Test]
        public void Bonuses_AfterProfileJsonRoundTrip_SameStats()
        {
            var p = NewProfile(1);
            Wear(p, "pickaxe.rare", 5);
            Wear(p, "boots.legendary", 3);
            Wear(p, "backpack.legendary", 20);
            var reloaded = JsonConvert.DeserializeObject<OutpostProfile>(JsonOf(p), OutpostJson.Settings);
            AssertSameStats(Stats(null, p), Stats(null, reloaded));
            Assert.AreEqual(4 + 10 + 19, Stats(null, reloaded).Capacity);
        }

        [Test]
        public void Bonuses_EquipSameItemTwice_AppliedOnce()
        {
            var p = NewProfile(1);
            Own(p, "boots.rare", 1);
            Equip(p, "boots.rare");
            Equip(p, "boots.rare");
            EquipBest(p);
            AssertExact(Stats(null, p).FinalMoveSpeed, FracA.Of(5, 2) * FracA.Of(118, 100));
        }

        static void AssertSameStats(TeamStats a, TeamStats b)
        {
            Assert.AreEqual(a.Workers, b.Workers);
            AssertExact(b.FinalDamage, ToFrac(a.FinalDamage), "урон");
            Assert.AreEqual(a.DamageUnits, b.DamageUnits);
            AssertExact(b.FinalMoveSpeed, ToFrac(a.FinalMoveSpeed), "скорость");
            Assert.AreEqual(a.EdgeTicks, b.EdgeTicks);
            Assert.AreEqual(a.Capacity, b.Capacity);
            Assert.AreEqual(a.UnloadTicks, b.UnloadTicks);
            AssertExact(b.FinalValue, ToFrac(a.FinalValue), "оценка");
        }
    }
}
