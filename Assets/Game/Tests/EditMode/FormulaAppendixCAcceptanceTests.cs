using System;
using System.Collections.Generic;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// ТЗ 17.1, группа 4: контрольные значения Приложения C (E-01…E-20, G-01…G-06, F-0…F-3, F-R), записанные числами.
    /// Дополняет FormulaControlValueTests: там значения проверяются через Formula, здесь — через реальные пути
    /// GameSession / GachaService (найм, улучшения, сундуки, доставки, таймеры). Уже проверенное не повторяется.
    /// Уровни карт выставляются через публичный CardStateOf(id): отдельной отладочной команды «карта уровня L» нет.
    /// </summary>
    public class FormulaAppendixCAcceptanceTests
    {
        const long S = GameSession.TicksPerSecond;
        const long Hour = 3600L * S;

        static BalanceRepository B => TestData.Balance;

        static double D(GameNumber g) => g.ToDouble();

        static GameSession NewSession(int zone = 1, string slot = SaveSlots.Main, ulong seed = 12345UL) =>
            GameSession.NewGame(TestData.Balance, seed, slot, 1_000_000L, zone);

        static GameSession DebugSession(int zone, ulong seed = 12345UL) => NewSession(zone, SaveSlots.Debug, seed);

        static int OrdAt(GameSession s, int col, int row) => s.ObjectAt(s.Layout.CellIndex(col, row));

        static (int col, int row) ColRow(ZoneLayout l, int cell) => (l.Col(cell), l.Row(cell));

        /// <summary>Число полученных копий карты (первая копия — уровень 1, остальные — дубликаты), пока карта не улучшалась.</summary>
        static int Copies(GameSession s, string cardId) =>
            s.Player.Cards.TryGetValue(cardId, out var cs) && cs.Level > 0 ? 1 + cs.Duplicates : 0;

        static void SetCardLevel(GameSession s, string cardId, int level) => s.CardStateOf(cardId).Level = level;

        static void ClearAndActivate(GameSession s, ZoneObject shaft)
        {
            s.DebugCompleteTarget(shaft.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(shaft.Ordinal), "активация " + shaft.TypeId);
        }

        static void BreakAndOpenGate(GameSession s, ZoneObject gate)
        {
            s.DebugCompleteTarget(gate.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.OpenGate(gate.Ordinal), "открытие ворот " + gate.GateOrder);
        }

        /// <summary>Объединяет котиков одного уровня, пока есть пары: освобождает места под следующие покупки.</summary>
        static void MergeEqualLevels(GameSession s)
        {
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < s.Mine.Cats.Count && !merged; i++)
                {
                    for (int j = i + 1; j < s.Mine.Cats.Count && !merged; j++)
                    {
                        var a = s.Mine.Cats[i];
                        var b = s.Mine.Cats[j];
                        if (a.Level != b.Level) continue;
                        var r = s.DropCat(a.Id, s.Layout.Col(b.Cell) + 0.5, s.Layout.Row(b.Cell) + 0.5);
                        Assert.IsTrue(r.Merged, "объединение котиков уровня " + a.Level);
                        merged = true;
                    }
                }
            }
        }

        static void HireWithMerges(GameSession s, int purchases)
        {
            for (int i = 0; i < purchases; i++)
            {
                Assert.AreEqual(CommandResult.Ok, s.Hire(), "покупка " + (i + 1));
                MergeEqualLevels(s);
            }
        }

        static void UpgradeTo(GameSession s, int ord, int level)
        {
            var gen = s.Mine.Objects[ord].Gen;
            if (gen.Level >= level) return;
            Assert.AreEqual(CommandResult.Ok, s.UpgradeGenerator(ord, out _, level - gen.Level));
            Assert.AreEqual(level, gen.Level);
        }

        static int CountRarity(ChestResult r, int rarity) => r.Cards.Where(c => B.Cards[c.CardId].Rarity == rarity).Sum(c => c.Count);

        static int TotalCards(ChestResult r) => r.Cards.Sum(c => c.Count);

        static int DeliveryBoxes(GameSession s) => s.Mine.Containers.Count(c => c.Kind == ContainerKind.Delivery);

        // ───────────────────────────── C.1: формы формул и F-R ─────────────────────────────

        [Test]
        public void F0_QuadraticTermAndFR_HalfAwayFromZero()
        {
            // F-0 = B + M·x + G·x². Квадратичный член есть только у неприменяемых карт, например ca027 (0; 0.3; 0.1).
            var c = B.Cards["ca027"];
            Assert.AreEqual(0, c.ModifierFormulaType);
            Assert.AreEqual(0.3 * 9 + 0.1 * 81, D(Formula.Evaluate(c.ModifierFormulaType, c.ModifierBase, c.ModifierMultiplier, c.ModifierGrowth, 9)), 1e-12, "ca027 на уровне 9 = 10.8");
            Assert.AreEqual(10.8, D(Formula.Ft0(0, 0.3, 0.1, 9)), 1e-12);

            // F-R: R(v, r) — к ближайшему кратному |r|, половина от нуля; r = 0 — без округления.
            Assert.AreEqual(30.0, Formula.RoundToMultiple(22.5, 15));
            Assert.AreEqual(-30.0, Formula.RoundToMultiple(-22.5, 15), "половина от нуля для отрицательных");
            Assert.AreEqual(-15.0, Formula.RoundToMultiple(-7.5, 15));
            Assert.AreEqual(15.0, Formula.RoundToMultiple(7.5, -15), "используется |r|");
            Assert.AreEqual(109.9, Formula.RoundToMultiple(109.9, 0), "r = 0 — без округления");
        }

        /// <summary>
        /// F-R: «для значений больше 2^53 округление — тождественная операция GameNumber»; ниже 2^53 цена — ceil(v),
        /// выдача — max(1, floor(v)). Допуск округления гасит только шум double и не сдвигает значение на целые единицы.
        /// </summary>
        [Test]
        public void FR_RoundingIsIdentityAbove2Pow53AndNeverMovesWholeUnits()
        {
            const double TwoPow53 = 9007199254740992.0;
            foreach (double raw in new[] { TwoPow53 + 2, 1e16, 1e20, 1e40, 1.234567e200, 1e299 })
            {
                var v = GameNumber.FromDouble(raw);
                Assert.IsFalse(v.IsBig, raw + ": значение в малой форме (до 1e300)");
                Assert.AreEqual(v, Formula.Price(v), "цена больше 2^53 не меняется: " + raw);
                Assert.AreEqual(v, Formula.Payout(v), "выдача больше 2^53 не меняется: " + raw);
            }

            // Ниже 2^53 целое значение округлением не сдвигается (раньше Price(1e13) = 1e13 − 10).
            foreach (double raw in new[] { 1e12, 1e13, 2199023255552.0, 4503599627370496.0, 9e15 })
            {
                var v = GameNumber.FromDouble(raw);
                Assert.AreEqual(raw, D(Formula.Price(v)), "цена целого " + raw + " — само значение");
                Assert.AreEqual(raw, D(Formula.Payout(v)), "выдача целого " + raw + " — само значение");
            }

            // Настоящая дробная часть не теряется: цена вверх, выдача вниз.
            Assert.AreEqual(219902325556.0, D(Formula.Price(GameNumber.FromDouble(219902325555.2))), "ceil(219902325555.2)");
            Assert.AreEqual(219902325555.0, D(Formula.Payout(GameNumber.FromDouble(219902325555.8))), "floor(219902325555.8)");
            Assert.AreEqual(1000000000001.0, D(Formula.Price(GameNumber.FromDouble(1000000000000.5))), "ceil(1e12 + 0.5)");
            Assert.AreEqual(1000000000000.0, D(Formula.Payout(GameNumber.FromDouble(1000000000000.5))), "floor(1e12 + 0.5)");

            // Шум double по-прежнему гасится: 2048.0000000001 → 2048, 156.99999999999 → 157.
            Assert.AreEqual(2048.0, D(Formula.Price(GameNumber.FromDouble(2048.0000000001))));
            Assert.AreEqual(157.0, D(Formula.Payout(GameNumber.FromDouble(156.99999999999))));

            // В игре: цена кузницы L92→93 (72·1.43^91 ≈ 9.84e15 > 2^53) совпадает с сырой формулой E-03.
            var cart = B.Cart;
            var raw92 = Formula.Evaluate(cart.UpgradeCostFormulaType, cart.UpgradeCostBase, cart.UpgradeCostMultiplier, cart.UpgradeCostGrowth, 92);
            Assert.Greater(D(raw92), TwoPow53, "цена кузницы L92 больше 2^53");
            Assert.AreEqual(raw92, Formula.Price(raw92), "цена кузницы L92→93 = сырая формула");
            var s = NewSession(1);
            int forge = s.Layout.Objects.First(o => o.Kind == ObjectKind.Cart).Ordinal;
            Assert.AreEqual(raw92, s.UpgradeCost(forge, 92), "GameSession.UpgradeCost кузницы L92→93 = сырая формула");
        }

        // ───────────────────────────── E-01 ─────────────────────────────

        [Test]
        public void E01_RankMineUnlockAndCheckPointUnlock()
        {
            // Rank = номер текущей зоны: zone1 → 1.
            Assert.AreEqual(1, NewSession(1).Rank);
            Assert.AreEqual(7, NewSession(7).Rank);

            // MineUnlock ≤ Rank: в zone1 случайный сундук даёт только карты с MineUnlock = 1, с zone3 доступна ca003 (MineUnlock 3).
            var wooden = B.Gacha["GachaWooden"];
            var ctx1 = NewSession(1).BuildGachaContext();
            var ctx3 = NewSession(3).BuildGachaContext();
            bool sawUnlock3 = false;
            for (ulong seed = 1; seed <= 300; seed++)
            {
                foreach (var g in GachaService.Open(B, wooden, ctx1, new Pcg32(seed, 1)).Cards)
                    Assert.AreEqual(1, B.Cards[g.CardId].MineUnlock, "zone1: выпала " + g.CardId);
                foreach (var g in GachaService.Open(B, wooden, ctx3, new Pcg32(seed, 1)).Cards)
                {
                    Assert.LessOrEqual(B.Cards[g.CardId].MineUnlock, 3, "zone3: выпала " + g.CardId);
                    if (B.Cards[g.CardId].MineUnlock == 3) sawUnlock3 = true;
                }
            }
            Assert.IsTrue(sawUnlock3, "в zone3 карта с MineUnlock = 3 должна выпадать");

            // CheckPointUnlock = N: в zone9 (одни ворота) записи доставок 16, 18, 21 (CheckPointUnlock 1) — только после открытия ворот.
            var s9 = DebugSession(9);
            Assert.AreEqual(1, s9.Layout.Gates.Count);
            var before = s9.DeliveryPool().Select(d => d.Id).ToList();
            foreach (var id in new[] { "16", "18", "21" }) CollectionAssert.DoesNotContain(before, id, "ворота закрыты");
            CollectionAssert.Contains(before, "17", "CheckPointUnlock 0 доступна сразу");
            BreakAndOpenGate(s9, s9.Layout.Gates[0]);
            var after = s9.DeliveryPool().Select(d => d.Id).ToList();
            foreach (var id in new[] { "16", "18", "21" }) CollectionAssert.Contains(after, id, "после открытия одних ворот");

            // Основания [Р]: MineUnlock = первая зона с требованием карты у 36 из 39 manager-карт, у трёх меньше; ранговые поля ≤ 144.
            var firstZone = new Dictionary<string, int>();
            for (int n = 1; n <= B.ZoneCount; n++)
                foreach (var sh in ZoneLayout.Build(B, n).Shafts)
                    if (!firstZone.ContainsKey(sh.CardId)) firstZone[sh.CardId] = n;
            Assert.AreEqual(39, firstZone.Count);
            Assert.AreEqual(36, firstZone.Count(kv => B.Cards[kv.Key].MineUnlock == kv.Value));
            Assert.AreEqual(3, firstZone.Count(kv => B.Cards[kv.Key].MineUnlock < kv.Value));
            Assert.LessOrEqual(B.CardsInOrder.Max(c => c.MineUnlock), 144);
            Assert.LessOrEqual(B.Root.Deliveries.Max(d => d.RankUnlock), 144);
        }

        // ───────────────────────────── E-02 ─────────────────────────────

        [Test]
        public void E02_CatParametersInSession()
        {
            var s = DebugSession(1);
            double[] damage = { 1, 2.1, 4.41, 9.261 };
            for (int L = 1; L <= 4; L++)
            {
                Assert.AreEqual(damage[L - 1], D(s.CatDamage(L)), 1e-9, "урон L" + L);
                Assert.AreEqual(1 * S, s.AttackDelayTicks(L), "задержка атаки 1 с, L" + L);
                Assert.AreEqual(0.0, s.CritChance(L), "шанс крита 0, L" + L);
                Assert.AreEqual(2.0, s.CritPower(L), "множитель крита ×2, L" + L);
            }
            Assert.AreEqual(1.0, D(s.CatDps(1)), 1e-12);

            // Камень L1 (HP 16) — 16 ударов котика L1.
            int ord = OrdAt(s, 2, 4);
            Assert.AreEqual(1, s.Layout.Objects[ord].Level);
            Assert.AreEqual(16.0, D(s.Mine.Objects[ord].Hp));
            Assert.IsTrue(s.DebugSpawnCat(1));
            var cat = s.Mine.Cats[0];
            Assert.AreEqual(CommandResult.Ok, s.DropCat(cat.Id, 2.5, 4.5).Result);
            Assert.AreEqual(16.0, s.EtaSeconds(ord), 1e-9, "оценка: 16 с");
            s.Advance(15 * S);
            Assert.AreEqual(1.0, D(s.Mine.Objects[ord].Hp), 1e-9, "после 15 ударов остаётся 1 HP");
            s.Advance(1 * S);
            Assert.AreEqual(ObjectStatus.Destroyed, s.Mine.Objects[ord].Status, "16-й удар разрушает камень");

            // F-R: HP и урон не округляются — котик L2 по камню L2 (HP 60) оставляет 57.9.
            var s2 = DebugSession(1);
            s2.DebugCompleteTarget(OrdAt(s2, 3, 3));
            int rockL2 = OrdAt(s2, 3, 2);
            Assert.AreEqual(2, s2.Layout.Objects[rockL2].Level);
            Assert.AreEqual(60.0, D(s2.Mine.Objects[rockL2].Hp));
            Assert.IsTrue(s2.DebugSpawnCat(2));
            Assert.AreEqual(CommandResult.Ok, s2.DropCat(s2.Mine.Cats[0].Id, 3.5, 2.5).Result);
            s2.Advance(1 * S);
            Assert.AreEqual(57.9, D(s2.Mine.Objects[rockL2].Hp), 1e-9);
        }

        // ───────────────────────────── E-03 ─────────────────────────────

        [Test]
        public void E03_ForgeAndAmethystInSession()
        {
            // Кузница: выход L1 = 3 за 2 с; цены 72; 103; 148; 211. Слот Main, без отладочных команд.
            var s = NewSession(1);
            int cart = s.Layout.Cart.Ordinal;
            Assert.AreEqual(3.0, D(s.GeneratorOutput(cart)));
            Assert.AreEqual(2 * S, s.GeneratorCycleTicks(cart));
            double[] forge = { 72, 103, 148, 211 };
            for (int L = 1; L <= 4; L++) Assert.AreEqual(forge[L - 1], D(s.UpgradeCost(cart, L)), "кузница L" + L + "→" + (L + 1));
            s.Advance(32 * S);
            Assert.AreEqual(25.0 + 16 * 3, D(s.Mine.Gold), "16 циклов по 3 золота");
            Assert.AreEqual(CommandResult.Ok, s.UpgradeGenerator(cart, out int bought));
            Assert.AreEqual(1, bought);
            Assert.AreEqual(1.0, D(s.Mine.Gold), "списано ровно 72");
            Assert.AreEqual(6.0, D(s.GeneratorOutput(cart)), "выход 3 × L");
            Assert.AreEqual(3.0, D(s.GetRewardIncomeRate()), 1e-12, "E-10: кузница L2 — 3/с");

            // amethyst: выход L1 = 400 за 8 с; цены 512; 748; 1092.
            var s2 = DebugSession(2);
            var am = s2.Layout.Shafts.Single();
            ClearAndActivate(s2, am);
            Assert.IsFalse(s2.Mine.Objects[am.Ordinal].Gen.Automated, "ca002 у игрока нет — шахта ручная");
            Assert.AreEqual(400.0, D(s2.GeneratorOutput(am.Ordinal)));
            Assert.AreEqual(8 * S, s2.GeneratorCycleTicks(am.Ordinal));
            double[] amethyst = { 512, 748, 1092 };
            for (int L = 1; L <= 3; L++) Assert.AreEqual(amethyst[L - 1], D(s2.UpgradeCost(am.Ordinal, L)), "amethyst L" + L + "→" + (L + 1));
            s2.Advance(8 * S);
            Assert.IsTrue(s2.Mine.Objects[am.Ordinal].Gen.PayoutReady);
            Assert.AreEqual(400.0, D(s2.Mine.Objects[am.Ordinal].Gen.PendingPayout));
        }

        // ───────────────────────────── E-04 ─────────────────────────────

        [Test]
        public void E04_ForgeThresholds()
        {
            var s = DebugSession(1);
            int cart = s.Layout.Cart.Ordinal;
            var def = s.DefFor(s.Layout.Cart);
            CollectionAssert.AreEqual(new[] { 9, 10, 10, 10 }, def.Objectives.ObjectiveCount.Take(4).ToArray());
            CollectionAssert.AreEqual(new[] { 2.0, 2.0, 2.0, 10.0 }, def.Objectives.CoreCurrencyMultiplier.Take(4).ToArray());
            CollectionAssert.AreEqual(new[] { 10, 20, 30, 40 }, def.Thresholds.Take(4).ToArray());
            Assert.AreEqual(10, s.NextThresholdLevel(cart));

            s.DebugAddGold(GameNumber.FromDouble(1e15));
            // Множители порогов перемножаются: ×2, ×2, ×2, ×10 на уровнях 10–40.
            var checks = new (int level, double output, int next)[]
            {
                (9, 27, 10), (10, 60, 20), (19, 114, 20), (20, 240, 30), (29, 348, 30), (30, 720, 40), (39, 936, 40), (40, 9600, 50),
            };
            foreach (var (level, output, next) in checks)
            {
                UpgradeTo(s, cart, level);
                Assert.AreEqual(output, D(s.GeneratorOutput(cart)), 1e-9, "выход кузницы L" + level);
                Assert.AreEqual(next, s.NextThresholdLevel(cart), "следующий порог на L" + level);
            }
            // Эликсир за порог k: max(1, floor((0.5 + 0.125·k) × 1)) = 1 для k = 1…4 в zone1.
            Assert.AreEqual(4.0, D(s.Player.Elixir), "по 1 эликсиру за каждый из 4 порогов");
        }

        [Test]
        public void E04_AmethystThresholdsAndElixirInZone2()
        {
            var s = DebugSession(2);
            var am = s.Layout.Shafts.Single();
            ClearAndActivate(s, am);
            int ord = am.Ordinal;
            var def = s.DefFor(am);
            CollectionAssert.AreEqual(new[] { 9, 10, 20, 20 }, def.Objectives.ObjectiveCount.Take(4).ToArray());
            CollectionAssert.AreEqual(new[] { 2.0, 2.0, 2.0, 200.0 }, def.Objectives.CoreCurrencyMultiplier.Take(4).ToArray());
            CollectionAssert.AreEqual(new[] { 10, 20, 40, 60 }, def.Thresholds.Take(4).ToArray());

            s.DebugAddGold(GameNumber.FromDouble(1e15));
            UpgradeTo(s, ord, 9);
            Assert.AreEqual(3600.0, D(s.GeneratorOutput(ord)));
            var elixirBefore = s.Player.Elixir;
            UpgradeTo(s, ord, 10);
            // Эликсир amethyst при k = 1 в zone2: floor(2.0 × 1.025) = 2.
            Assert.AreEqual(2.0, D(s.Player.Elixir - elixirBefore), "эликсир первого порога");
            Assert.AreEqual(8000.0, D(s.GeneratorOutput(ord)), "×2 на уровне 10");
            Assert.AreEqual(20, s.NextThresholdLevel(ord));
            UpgradeTo(s, ord, 20);
            Assert.AreEqual(40, s.NextThresholdLevel(ord));
            Assert.AreEqual(400.0 * 20 * 4, D(s.GeneratorOutput(ord)));
            UpgradeTo(s, ord, 40);
            Assert.AreEqual(60, s.NextThresholdLevel(ord));
            UpgradeTo(s, ord, 59);
            Assert.AreEqual(400.0 * 59 * 8, D(s.GeneratorOutput(ord)));
            UpgradeTo(s, ord, 60);
            Assert.AreEqual(400.0 * 60 * 1600, D(s.GeneratorOutput(ord)), "×200 на уровне 60");
        }

        [Test]
        public void E04_LastThresholdIsMaxLevel1200()
        {
            // Σ ObjectiveCount = 1199 у всех 21 генератора [Д] → последний порог 1200 (валидатор об этом только предупреждает).
            Assert.AreEqual(21, B.Objectives.Count);
            foreach (var o in B.Objectives.Values)
                Assert.AreEqual(1199, o.ObjectiveCount.Sum(), o.GeneratorId);

            var s = DebugSession(1);
            int cart = s.Layout.Cart.Ordinal;
            Assert.AreEqual(1200, s.MaxGeneratorLevel(cart));
            s.DebugAddGold(GameNumber.Pow(10, 200));
            Assert.AreEqual(CommandResult.Ok, s.UpgradeGenerator(cart, out int bought, 5000));
            Assert.AreEqual(1199, bought, "куплено ровно до уровня 1200");
            var gen = s.Mine.Objects[cart].Gen;
            Assert.AreEqual(1200, gen.Level);
            Assert.AreEqual(s.DefFor(s.Layout.Cart).Thresholds.Length, gen.ThresholdsReached, "все пороги достигнуты, последний — 1200");
            Assert.AreEqual(1200, s.NextThresholdLevel(cart));
            Assert.AreEqual(CommandResult.MaxLevel, s.UpgradeGenerator(cart, out _));
            Assert.AreEqual(CommandResult.MaxLevel, s.UpgradeToThreshold(cart, out _));
            Assert.AreEqual(1200, gen.Level);

            var s2 = DebugSession(2);
            var am = s2.Layout.Shafts.Single();
            ClearAndActivate(s2, am);
            Assert.AreEqual(1200, s2.MaxGeneratorLevel(am.Ordinal));
        }

        // ───────────────────────────── E-05 ─────────────────────────────

        [Test]
        public void E05_HirePriceAndLevelBySession()
        {
            var s = DebugSession(1);
            s.DebugAddGold(GameNumber.FromDouble(1e14));
            double[] prices = { 2, 8, 32, 128, 512, 2048 };
            for (int purchase = 1; purchase <= 21; purchase++)
            {
                // Покупки 1–5 → 1, 6–13 → 2, 14–21 → 3.
                int expected = purchase <= 5 ? 1 : purchase <= 13 ? 2 : 3;
                Assert.AreEqual(expected, s.HireLevel(), "уровень покупки " + purchase);
                if (purchase <= prices.Length) Assert.AreEqual(prices[purchase - 1], D(s.HirePrice()), "цена покупки " + purchase);
                var gold = s.Mine.Gold;
                var price = s.HirePrice();
                Assert.AreEqual(CommandResult.Ok, s.Hire());
                Assert.AreEqual(D(gold - price), D(s.Mine.Gold), 1e-6, "списана цена покупки " + purchase);
                Assert.AreEqual(expected, s.Mine.Cats[s.Mine.Cats.Count - 1].Level, "уровень котика покупки " + purchase);
                MergeEqualLevels(s);
            }
            Assert.AreEqual(21, s.Mine.HireCount);
            Assert.AreEqual(4, s.HireLevel(), "22-я покупка: 1 + floor(0.4 + 0.125·21) = 4");
        }

        [Test]
        public void E05_PricesAfterOneGate()
        {
            var s = DebugSession(5);
            Assert.AreEqual(2.0, D(s.HirePrice()));
            BreakAndOpenGate(s, s.Layout.Gates[0]);
            Assert.AreEqual(10.0, D(s.HireDivisor()));
            // После одних ворот цены 1; 1; 4; 13 (стартового золота 25 хватает).
            double[] prices = { 1, 1, 4, 13 };
            for (int i = 0; i < prices.Length; i++)
            {
                Assert.AreEqual(prices[i], D(s.HirePrice()), "покупка " + (i + 1));
                Assert.AreEqual(CommandResult.Ok, s.Hire());
            }
            Assert.AreEqual(25.0 - 19.0, D(s.Mine.Gold));
        }

        [Test]
        public void E05_FreeCatsDoNotCountAndZoneResets()
        {
            // Бесплатный котик n не меняет.
            var s = NewSession(1);
            s.Advance(2400 * S);
            var box = s.Mine.Containers.Single(c => c.Kind == ContainerKind.FreeCat);
            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(box.Id));
            Assert.AreEqual(1, s.Mine.Cats.Count);
            Assert.AreEqual(0, s.Mine.HireCount);
            Assert.AreEqual(2.0, D(s.HirePrice()));
            Assert.AreEqual(1, s.HireLevel());

            // Сброс при переходе зоны.
            var s2 = DebugSession(2);
            s2.DebugAddGold(GameNumber.FromDouble(1000));
            Assert.AreEqual(CommandResult.Ok, s2.Hire());
            Assert.AreEqual(CommandResult.Ok, s2.Hire());
            Assert.AreEqual(32.0, D(s2.HirePrice()));
            ClearAndActivate(s2, s2.Layout.Shafts.Single());
            s2.DebugCompleteTarget(s2.Layout.Exit.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s2.CompleteZone());
            Assert.AreEqual(3, s2.Rank, "E-01: Rank = номер новой зоны");
            Assert.AreEqual(0, s2.Mine.HireCount);
            Assert.AreEqual(2.0, D(s2.HirePrice()), "цена найма сброшена");
            Assert.AreEqual(1, s2.HireLevel());
        }

        // ───────────────────────────── E-06 ─────────────────────────────

        [Test]
        public void E06_FreeCatIntervalAndLevel()
        {
            // 2400 с: контейнер появляется ровно через 40 минут.
            var s = NewSession(1);
            Assert.AreEqual(2400.0, s.FreeCatIntervalSeconds());
            s.Advance(2400 * S - 1);
            Assert.AreEqual(0, s.Mine.Containers.Count, "за тик до 2400 с контейнера нет");
            s.Advance(1);
            Assert.AreEqual(1, s.Mine.Containers.Count(c => c.Kind == ContainerKind.FreeCat), "ровно в 2400 с");

            // После одних ворот 2340 с.
            var s5 = DebugSession(5);
            Assert.AreEqual(2400.0, s5.FreeCatIntervalSeconds());
            BreakAndOpenGate(s5, s5.Layout.Gates[0]);
            Assert.AreEqual(2340.0, s5.FreeCatIntervalSeconds());
            // Ворота и ca029 вместе: 2400 − 60 − 60·2 = 2220.
            SetCardLevel(s5, "ca029", 2);
            Assert.AreEqual(2220.0, s5.FreeCatIntervalSeconds());
            Assert.AreEqual(60.0, GameSession.MinFreeCatIntervalSeconds, "интервал не меньше 60 с");
            // Нижняя граница с валидными данными недостижима (ca029 Rare — максимум 9); уровень 40 вне таблицы
            // выставлен только чтобы проверить max(60, 2400 − 60 − 2400).
            SetCardLevel(s5, "ca029", 40);
            Assert.AreEqual(60.0, s5.FreeCatIntervalSeconds());

            // Уровень = max(1, L_next − 1): при L_next = 3 котик уровня 2.
            var s3 = DebugSession(1);
            s3.DebugAddGold(GameNumber.FromDouble(1e9));
            HireWithMerges(s3, 13);
            Assert.AreEqual(3, s3.HireLevel());
            s3.Advance(2400 * S);
            var box = s3.Mine.Containers.Single(c => c.Kind == ContainerKind.FreeCat);
            Assert.AreEqual(2, box.CatLevel);
            int hires = s3.Mine.HireCount;
            Assert.AreEqual(CommandResult.Ok, s3.OpenContainer(box.Id));
            Assert.AreEqual(hires, s3.Mine.HireCount, "бесплатный котик n не меняет");
        }

        // ───────────────────────────── E-07 ─────────────────────────────

        [Test]
        public void E07_ManagerMultipliersInSession()
        {
            var s = DebugSession(1);
            int cart = s.Layout.Cart.Ordinal;
            Assert.AreEqual(1.0, D(s.ManagerMultiplier(BalanceRepository.CartId)), "карты нет — множитель 1");
            SetCardLevel(s, "ca001", 1);
            Assert.AreEqual(1.0, D(s.ManagerMultiplier(BalanceRepository.CartId)), "Common L1 ×1");
            SetCardLevel(s, "ca001", 2);
            Assert.AreEqual(3.0, D(s.ManagerMultiplier(BalanceRepository.CartId)), "Common L2 ×3");
            SetCardLevel(s, "ca001", 14);
            Assert.AreEqual(1594323.0, D(s.ManagerMultiplier(BalanceRepository.CartId)), 1e-3, "Common L14 ×1 594 323");
            Assert.AreEqual(3.0 * 1594323.0, D(s.GeneratorOutput(cart)), 1e-3, "выход кузницы L1 с ca001 L14");

            var s2 = DebugSession(2);
            SetCardLevel(s2, "ca014", 12);
            Assert.AreEqual(4194304.0, D(s2.ManagerMultiplier("amethyst")), 1e-3, "Uncommon L12 ×4 194 304");

            // Несколько карт на один генератор перемножаются: ca002 L2 (×3) и ca014 L2 (×4) → ×12.
            var s3 = DebugSession(2);
            SetCardLevel(s3, "ca002", 2);
            SetCardLevel(s3, "ca014", 2);
            var am = s3.Layout.Shafts.Single();
            ClearAndActivate(s3, am);
            Assert.IsTrue(s3.Mine.Objects[am.Ordinal].Gen.Automated, "ca002 L2 ≥ требуемого 1");
            Assert.AreEqual(12.0, D(s3.ManagerMultiplier("amethyst")), 1e-9);
            Assert.AreEqual(4800.0, D(s3.GeneratorOutput(am.Ordinal)), 1e-9);
            // E-10: ставка = кузница 1.5 + автоматизированная шахта 4800 / 8.
            Assert.AreEqual(601.5, D(s3.GetRewardIncomeRate()), 1e-9);
        }

        // ───────────────────────────── E-08 ─────────────────────────────

        [Test]
        public void E08_TwoGatesBonuses()
        {
            var s = DebugSession(10);
            Assert.AreEqual(2, s.Layout.Gates.Count);
            Assert.AreEqual(2.0, D(s.CheckpointValue(10)));
            Assert.AreEqual(10.0, D(s.CheckpointValue(2)));
            Assert.AreEqual(60.0, D(s.CheckpointValue(7)));
            Assert.AreEqual(1.0, D(s.CheckpointValue(1)));
            BreakAndOpenGate(s, s.Layout.Gates[0]);
            BreakAndOpenGate(s, s.Layout.Gates[1]);
            Assert.AreEqual(2, s.Mine.GatesOpened);
            // Предусловие: сундуки ворот не дали карт кодов 10, 7, 1 (иначе к бонусам ворот добавится эффект карты).
            foreach (var id in new[] { "ca034", "ca029", "ca038" })
                Assert.AreEqual(0, s.CardLevel(id), "предусловие seed: нет карты " + id);
            // После 2 открытых ворот: выход ×4, цена ÷100, интервал 2280 с, лимит 12.
            Assert.AreEqual(4.0, D(s.GlobalIncomeMultiplier()));
            Assert.AreEqual(12.0, D(s.GeneratorOutput(s.Layout.Cart.Ordinal)), "кузница L1: 3 × 4");
            Assert.AreEqual(6.0, D(s.GetRewardIncomeRate()), 1e-12);
            Assert.AreEqual(100.0, D(s.HireDivisor()));
            Assert.AreEqual(1.0, D(s.HirePrice()), "max(1, ceil(2 / 100))");
            Assert.AreEqual(2280.0, s.FreeCatIntervalSeconds());
            Assert.AreEqual(12, s.UnitCap());
        }

        [Test]
        public void E08_CardCodes()
        {
            var s = DebugSession(1);
            int cart = s.Layout.Cart.Ordinal;
            // Код 10: ca034 = F-1(0, 1, 3; x = L) — множитель выхода всех генераторов.
            SetCardLevel(s, "ca034", 1);
            Assert.AreEqual(1.0, D(s.GlobalIncomeMultiplier()));
            SetCardLevel(s, "ca034", 2);
            Assert.AreEqual(3.0, D(s.GlobalIncomeMultiplier()));
            Assert.AreEqual(9.0, D(s.GeneratorOutput(cart)));
            SetCardLevel(s, "ca034", 3);
            Assert.AreEqual(9.0, D(s.GlobalIncomeMultiplier()));
            // Код 7: ca029 — интервал бесплатного котика −60·L с.
            SetCardLevel(s, "ca029", 1);
            Assert.AreEqual(2340.0, s.FreeCatIntervalSeconds());
            SetCardLevel(s, "ca029", 2);
            Assert.AreEqual(2280.0, s.FreeCatIntervalSeconds());
            // Код 1: ca038 — лимит котиков +L.
            SetCardLevel(s, "ca038", 1);
            Assert.AreEqual(11, s.UnitCap());
            SetCardLevel(s, "ca038", 3);
            Assert.AreEqual(13, s.UnitCap());

            // Остальные коды в v1 не применяются: ca026 (код 12) на уровне 5 ничего не меняет, улучшение заблокировано.
            var s2 = DebugSession(1);
            Assert.AreEqual(12, B.Cards["ca026"].StatModifierType);
            SetCardLevel(s2, "ca026", 5);
            Assert.AreEqual(1.0, D(s2.GlobalIncomeMultiplier()));
            Assert.AreEqual(2400.0, s2.FreeCatIntervalSeconds());
            Assert.AreEqual(10, s2.UnitCap());
            Assert.AreEqual(3.0, D(s2.GeneratorOutput(s2.Layout.Cart.Ordinal)));
            Assert.AreEqual(CommandResult.EffectNotAvailable, s2.CanUpgradeCard("ca026"));
        }

        // ───────────────────────────── E-09 ─────────────────────────────

        [Test]
        public void E09_RockRewardsInSession()
        {
            var s = DebugSession(1);
            Assert.AreEqual(1.5, D(s.GetRewardIncomeRate()), 1e-12);
            // Камень L1: 105 с × 1.5 = 157 золота; L2: floor(225 × 1.5) = 337.
            int rockL1 = OrdAt(s, 2, 4);
            Assert.AreEqual(1, s.Layout.Objects[rockL1].Level);
            var gold = s.Mine.Gold;
            s.DebugCompleteTarget(rockL1);
            Assert.AreEqual(157.0, D(s.Mine.Gold - gold), "камень L1");
            int rockL2 = OrdAt(s, 3, 2);
            Assert.AreEqual(2, s.Layout.Objects[rockL2].Level);
            gold = s.Mine.Gold;
            s.DebugCompleteTarget(rockL2);
            Assert.AreEqual(337.0, D(s.Mine.Gold - gold), "камень L2");
            // Эликсир rocksoftcurrencysmall L2 в zone1 = max(1, floor(21 × 0.1)) = 2.
            int soft = OrdAt(s, 3, 1);
            Assert.AreEqual("rocksoftcurrencysmall", s.Layout.Objects[soft].TypeId);
            Assert.AreEqual(2, s.Layout.Objects[soft].Level);
            var elixir = s.Player.Elixir;
            gold = s.Mine.Gold;
            s.DebugCompleteTarget(soft);
            Assert.AreEqual(2.0, D(s.Player.Elixir - elixir));
            Assert.AreEqual(D(gold), D(s.Mine.Gold), "Soft не даёт золота");

            // Hard = Quantity: rockhardcurrencysmall — 3.
            var s4 = DebugSession(4);
            var hard = s4.Layout.Objects.First(o => o.TypeId == "rockhardcurrencysmall");
            s4.DebugCompleteTarget(hard.Ordinal);
            Assert.AreEqual(3.0, D(s4.Player.Hard));

            // BattlePass не выдаётся: только учёт исключённой награды.
            var s7 = DebugSession(7);
            var bp = s7.Layout.Objects.Single(o => o.TypeId == "rockbattlepasscurrency");
            var gold7 = s7.Mine.Gold;
            s7.DebugCompleteTarget(bp.Ordinal);
            Assert.AreEqual(1.0, s7.Player.ExcludedRewards["BattlePass"]);
            Assert.AreEqual(D(gold7), D(s7.Mine.Gold));
            Assert.IsTrue(s7.Player.Elixir.IsZero);
            Assert.IsTrue(s7.Player.Hard.IsZero);
        }

        // ───────────────────────────── E-10 ─────────────────────────────

        [Test]
        public void E10_RewardIncomeRate()
        {
            Assert.AreEqual(1.5, B.Props.NoGeneratorsCurrencyRate);
            var s = DebugSession(2);
            Assert.AreEqual(1.5, D(s.GetRewardIncomeRate()), 1e-12, "только кузница L1");
            var am = s.Layout.Shafts.Single();
            ClearAndActivate(s, am);
            Assert.AreEqual(1.5, D(s.GetRewardIncomeRate()), 1e-12, "ручная шахта в ставку не входит");
            s.DebugGiveZoneCards();
            Assert.IsTrue(s.Mine.Objects[am.Ordinal].Gen.Automated);
            Assert.AreEqual(1.5 + 400.0 / 8.0, D(s.GetRewardIncomeRate()), 1e-9, "кузница + автоматизированная amethyst");
        }

        // ───────────────────────────── E-11 ─────────────────────────────

        [Test]
        public void E11_ChestCardCountsAtZone44()
        {
            var ctx = NewSession(44).BuildGachaContext();
            const int runs = 400;
            int woodenWithExtra = 0;
            for (ulong seed = 1; seed <= runs; seed++)
            {
                // floor(N / Weight) плюс ещё одна с вероятностью дробной части: Wooden 24/7 = 3.43 → 3 или 4 Uncommon.
                var wooden = GachaService.Open(B, B.Gacha["GachaWooden"], ctx, new Pcg32(seed, 1));
                Assert.AreEqual(24, TotalCards(wooden), "Wooden: 24 карты");
                int u = CountRarity(wooden, Rarity.Uncommon);
                Assert.That(u, Is.InRange(3, 4), "Wooden: не меньше 3 Uncommon");
                if (u == 4) woodenWithExtra++;
                var iron = GachaService.Open(B, B.Gacha["GachaIron"], ctx, new Pcg32(seed, 2));
                Assert.AreEqual(48, TotalCards(iron), "Iron: 48 карт");
                Assert.That(CountRarity(iron, Rarity.Uncommon), Is.InRange(6, 7), "Iron: не меньше 6 Uncommon");
                var golden = GachaService.Open(B, B.Gacha["GachaGolden"], ctx, new Pcg32(seed, 3));
                Assert.AreEqual(102, TotalCards(golden), "Golden: 102 карты");
                Assert.That(CountRarity(golden, Rarity.Uncommon), Is.InRange(25, 26), "Golden: не меньше 25 Uncommon");
                Assert.That(CountRarity(golden, Rarity.Rare), Is.InRange(2, 3), "Golden: не меньше 2 Rare");
            }
            // Доля сундуков с 4-й Uncommon ≈ дробная часть 24/7 = 0.43 (фиксированные seed, широкий допуск).
            Assert.AreEqual(3.0 / 7.0, woodenWithExtra / (double)runs, 0.1);
        }

        [Test]
        public void E11_AtLeastOneCardWhenBaseNumCardsPositive()
        {
            // N не меньше 1 при BaseNumCards > 0: 1 × 0.1 (Normal, zone1) = 0.1 → 0 → 1.
            var g = new GachaDto
            {
                Id = "test_one_normal",
                GachaType = 0,
                BaseNumCards = 1,
                GuaranteedCardIds = new string[0],
                GuaranteedCardMin = new int[0],
                GuaranteedCardCounts = new int[0],
            };
            var ctx = NewSession(1).BuildGachaContext();
            Assert.AreEqual(0.1, ctx.Multipliers.GachaCardsMultNormal);
            for (ulong seed = 1; seed <= 20; seed++)
                Assert.AreEqual(1, TotalCards(GachaService.Open(B, g, ctx, new Pcg32(seed, 1))));
        }

        [Test]
        public void E11_HalvesRoundUpAndZone1Wooden()
        {
            var ctx2 = NewSession(2).BuildGachaContext();
            var ctx5 = NewSession(5).BuildGachaContext();
            var ctx14 = NewSession(14).BuildGachaContext();
            var ctx1 = NewSession(1).BuildGachaContext();
            int minElixir = int.MaxValue, maxElixir = int.MinValue;
            for (ulong seed = 1; seed <= 300; seed++)
            {
                // Половины: zone2 Golden 4.5 → 5; zone5 Forged 6.5 → 7; zone14 Crusher 52.5 → 53.
                Assert.AreEqual(5, TotalCards(GachaService.Open(B, B.Gacha["GachaGolden"], ctx2, new Pcg32(seed, 1))));
                Assert.AreEqual(7, TotalCards(GachaService.Open(B, B.Gacha["GachaForged"], ctx5, new Pcg32(seed, 1))));
                Assert.AreEqual(53, TotalCards(GachaService.Open(B, B.Gacha["GachaCrusher"], ctx14, new Pcg32(seed, 1))));
                // zone1 Wooden: 1 карта, эликсир 15–19.
                var w = GachaService.Open(B, B.Gacha["GachaWooden"], ctx1, new Pcg32(seed, 1));
                Assert.AreEqual(1, TotalCards(w));
                int e = (int)D(w.Elixir);
                Assert.AreEqual(e, D(w.Elixir), "эликсир целый");
                minElixir = Math.Min(minElixir, e);
                maxElixir = Math.Max(maxElixir, e);
            }
            Assert.AreEqual(15, minElixir, "floor(45 × 0.35)");
            Assert.AreEqual(19, maxElixir, "floor(55 × 0.35)");
        }

        [Test]
        public void E11_MultiplierClassByGachaType()
        {
            // 1 → Premium, 3 → Rare, 2 → без множителей (zone44: Premium 1.2675, Rare 1.415, Normal 3.4).
            var ctx = NewSession(44).BuildGachaContext();
            Assert.AreEqual(1, B.Gacha["GachaRoyal"].GachaType);
            Assert.AreEqual(3, B.Gacha["GachaRare"].GachaType);
            Assert.AreEqual(2, B.Gacha["GachaLegendary"].GachaType);
            for (ulong seed = 1; seed <= 50; seed++)
            {
                var royal = GachaService.Open(B, B.Gacha["GachaRoyal"], ctx, new Pcg32(seed, 1));
                Assert.AreEqual(127, TotalCards(royal), "100 × 1.2675 = 126.75 → 127");
                Assert.That(D(royal.Elixir), Is.InRange(1154.0, 1275.0), "[950; 1050] × 1.215, вниз");
                var rare = GachaService.Open(B, B.Gacha["GachaRare"], ctx, new Pcg32(seed, 2));
                Assert.AreEqual(10, TotalCards(rare), "7 × 1.415 = 9.905 → 10");
                Assert.AreEqual(10, CountRarity(rare, Rarity.Rare));
                var legendary = GachaService.Open(B, B.Gacha["GachaLegendary"], ctx, new Pcg32(seed, 3));
                Assert.AreEqual(1, TotalCards(legendary), "тип 2 — без множителей");
                Assert.AreEqual(1, CountRarity(legendary, Rarity.Legendary));
            }
        }

        // ───────────────────────────── E-12 ─────────────────────────────

        [Test]
        public void E12_Stacks()
        {
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, B.Props.CardStackCounts);
            CollectionAssert.AreEqual(new[] { 7, 40 }, B.Props.CommonStackSplitAt);
            // Common: ниже порога 7 — одна стопка, 24 → 2 (порог 7), от 40 → 3.
            Assert.AreEqual(1, GachaService.StackCount(B, Rarity.Common, 6));
            Assert.AreEqual(2, GachaService.StackCount(B, Rarity.Common, 7));
            Assert.AreEqual(2, GachaService.StackCount(B, Rarity.Common, 24));
            Assert.AreEqual(3, GachaService.StackCount(B, Rarity.Common, 40));
            // Uncommon [7, 30, 50]; Rare [4, 7, 10]; Epic [4, 8]; Legendary — всегда одна.
            Assert.AreEqual(4, GachaService.StackCount(B, Rarity.Uncommon, 50));
            Assert.AreEqual(3, GachaService.StackCount(B, Rarity.Uncommon, 30));
            Assert.AreEqual(1, GachaService.StackCount(B, Rarity.Rare, 3));
            Assert.AreEqual(4, GachaService.StackCount(B, Rarity.Rare, 10));
            Assert.AreEqual(3, GachaService.StackCount(B, Rarity.Epic, 8));
            Assert.AreEqual(1, GachaService.StackCount(B, Rarity.Legendary, 100));

            // Common 24 шт. → 2 разные карты по 12: сундук с одной Common-редкостью, без множителей.
            var common24 = new GachaDto
            {
                Id = "test_common24",
                GachaType = 2,
                BaseNumCards = 24,
                GuaranteedCardIds = new string[0],
                GuaranteedCardMin = new int[0],
                GuaranteedCardCounts = new int[0],
            };
            var ctx = NewSession(44).BuildGachaContext();
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var r = GachaService.Open(B, common24, ctx, new Pcg32(seed, 1));
                Assert.AreEqual(2, r.Cards.Count, "две стопки");
                Assert.AreNotEqual(r.Cards[0].CardId, r.Cards[1].CardId, "разные ID");
                Assert.AreEqual(12, r.Cards[0].Count);
                Assert.AreEqual(12, r.Cards[1].Count);
                Assert.IsTrue(r.Cards.All(c => B.Cards[c.CardId].Rarity == Rarity.Common));
            }
        }

        // ───────────────────────────── E-13 ─────────────────────────────

        [Test]
        public void E13_ExitRewardLowerBound()
        {
            // zone2: ca001×1, ca002×1, 30 эликсира.
            var s = DebugSession(2);
            ClearAndActivate(s, s.Layout.Shafts.Single());
            s.DebugCompleteTarget(s.Layout.Exit.Ordinal);
            var elixir = s.Player.Elixir;
            int ca001 = Copies(s, "ca001"), ca002 = Copies(s, "ca002");
            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            Assert.AreEqual(30.0, D(s.Player.Elixir - elixir));
            Assert.AreEqual(1, Copies(s, "ca001") - ca001);
            Assert.AreEqual(1, Copies(s, "ca002") - ca002);
            var exit2 = s.Player.ChestReveals.Last(r => r.Source == "exit");
            Assert.AreEqual("scg_m002_exit", exit2.GachaId);
            Assert.AreEqual(2, TotalCards(exit2));

            // zone3: ca002×3, ca014×1, 50 эликсира.
            var s3 = DebugSession(3);
            foreach (var sh in s3.Layout.Shafts) ClearAndActivate(s3, sh);
            s3.DebugCompleteTarget(s3.Layout.Exit.Ordinal);
            elixir = s3.Player.Elixir;
            ca002 = Copies(s3, "ca002");
            int ca014 = Copies(s3, "ca014");
            Assert.AreEqual(CommandResult.Ok, s3.CompleteZone());
            Assert.AreEqual(50.0, D(s3.Player.Elixir - elixir));
            Assert.AreEqual(3, Copies(s3, "ca002") - ca002);
            Assert.AreEqual(1, Copies(s3, "ca014") - ca014);
            Assert.AreEqual(4, TotalCards(s3.Player.ChestReveals.Last(r => r.Source == "exit")));

            // GachaCrusher (с zone11) — обычный сундук: zone14 — 35 × 1.5 = 52.5 → 53 карты, эликсир 250 × 1.2 = 300.
            var s14 = DebugSession(14);
            Assert.AreEqual("GachaCrusher", s14.Layout.Exit.RewardId);
            foreach (var g in s14.Layout.Gates) BreakAndOpenGate(s14, g);
            foreach (var sh in s14.Layout.Shafts) ClearAndActivate(s14, sh);
            s14.DebugCompleteTarget(s14.Layout.Exit.Ordinal);
            elixir = s14.Player.Elixir;
            Assert.AreEqual(CommandResult.Ok, s14.CompleteZone());
            var crusher = s14.Player.ChestReveals.Last(r => r.Source == "exit");
            Assert.AreEqual("GachaCrusher", crusher.GachaId);
            Assert.AreEqual(53, TotalCards(crusher));
            Assert.AreEqual(300.0, D(crusher.Elixir));
            Assert.AreEqual(300.0, D(s14.Player.Elixir - elixir));
        }

        // ───────────────────────────── E-14 ─────────────────────────────

        [Test]
        public void E14_CardUpgradeCostsAndMaxLevels()
        {
            Assert.AreEqual(1, Rarity.Common);
            Assert.AreEqual(2, Rarity.Uncommon);
            Assert.AreEqual(3, Rarity.Rare);
            Assert.AreEqual(4, Rarity.Epic);
            Assert.AreEqual(5, Rarity.Legendary);
            Assert.AreEqual(6, Rarity.Majestic);

            // Common 1→2: 2 дубликата и 50 эликсира.
            var s = DebugSession(1);
            Assert.AreEqual(CommandResult.NotEnoughDuplicates, s.CanUpgradeCard("ca002"), "карты нет");
            var cs = s.CardStateOf("ca002");
            cs.Level = 1;
            cs.Duplicates = 1;
            s.DebugAddElixir(GameNumber.FromDouble(50));
            Assert.AreEqual(CommandResult.NotEnoughDuplicates, s.CanUpgradeCard("ca002"));
            cs.Duplicates = 2;
            Assert.AreEqual(CommandResult.Ok, s.UpgradeCard("ca002"));
            Assert.AreEqual(2, cs.Level);
            Assert.AreEqual(0, cs.Duplicates);
            Assert.IsTrue(s.Player.Elixir.IsZero, "списано ровно 50");
            cs.Duplicates = 5;
            s.DebugAddElixir(GameNumber.FromDouble(99));
            Assert.AreEqual(CommandResult.NotEnoughElixir, s.CanUpgradeCard("ca002"), "2→3 стоит 100 эликсира");

            // Common: максимум 14 (13→14: 8000 дубликатов и 72000 эликсира).
            cs.Level = 13;
            cs.Duplicates = 8000;
            s.DebugAddElixir(GameNumber.FromDouble(72000 - 99));
            Assert.AreEqual(CommandResult.Ok, s.UpgradeCard("ca002"));
            Assert.AreEqual(14, cs.Level);
            Assert.AreEqual(0, cs.Duplicates);
            Assert.IsTrue(s.Player.Elixir.IsZero);
            cs.Duplicates = 1_000_000;
            s.DebugAddElixir(GameNumber.FromDouble(1e9));
            Assert.AreEqual(CommandResult.MaxLevel, s.CanUpgradeCard("ca002"));

            // Uncommon: максимум 12 (11→12: 2000 дубликатов и 96000 эликсира).
            var u = s.CardStateOf("ca014");
            u.Level = 11;
            u.Duplicates = 2000;
            var elixir = s.Player.Elixir;
            Assert.AreEqual(CommandResult.Ok, s.UpgradeCard("ca014"));
            Assert.AreEqual(12, u.Level);
            Assert.AreEqual(96000.0, D(elixir - s.Player.Elixir));
            u.Duplicates = 1_000_000;
            Assert.AreEqual(CommandResult.MaxLevel, s.CanUpgradeCard("ca014"));
        }

        // ───────────────────────────── E-15 ─────────────────────────────

        [Test]
        public void E15_DeliveriesInSession()
        {
            // Rank 3: две шахты активированы, но доставки и бесплатный сундук не включаются.
            var s3 = DebugSession(3);
            foreach (var sh in s3.Layout.Shafts) ClearAndActivate(s3, sh);
            Assert.AreEqual(2, s3.Mine.ActivatedShafts);
            Assert.IsFalse(s3.Mine.DeliveriesEnabled, "Rank 3 < 4");
            Assert.IsFalse(s3.Player.FreeChest.Enabled, "Rank 3 < 4");

            var s = DebugSession(4);
            s.DebugAddGold(GameNumber.FromDouble(1e9));
            HireWithMerges(s, 13);
            Assert.AreEqual(3, s.HireLevel(), "L_next = 3");
            ClearAndActivate(s, s.Layout.Shafts[0]);
            Assert.IsFalse(s.Mine.DeliveriesEnabled, "одной шахты мало");
            ClearAndActivate(s, s.Layout.Shafts[1]);
            Assert.IsTrue(s.Mine.DeliveriesEnabled);
            Assert.AreEqual(1.5, D(s.GetRewardIncomeRate()), 1e-12, "ручные шахты в ставку не входят");
            Assert.AreEqual(22.0, s.NextDeliveryIntervalSeconds(false), 1e-9, "интервал без разброса: 22 с");

            // Первая доставка — через 20 с.
            s.Advance(20 * S - 1);
            Assert.AreEqual(0, DeliveryBoxes(s));
            s.Advance(1);
            Assert.AreEqual(1, DeliveryBoxes(s));

            bool sawCat = false, sawCore15 = false;
            int claims = 0;
            for (int guard = 0; guard < 20000 && (claims < 30 || !sawCat || !sawCore15); guard++)
            {
                var box = s.Mine.Containers.FirstOrDefault(c => c.Kind == ContainerKind.Delivery);
                if (box == null)
                {
                    s.Advance(S);
                    continue;
                }
                if (box.RewardType == RewardTypes.Miner)
                {
                    Assert.AreEqual(1, box.CatLevel, "котик при L_next = 3: max(1, 3 − 2) = 1");
                    sawCat = true;
                }
                if (box.DeliveryId == "15")
                {
                    Assert.AreEqual(90.0, D(box.Amount), "Core id15 на ранге 4: 60 с × 1.5");
                    sawCore15 = true;
                }
                int hires = s.Mine.HireCount;
                Assert.AreEqual(CommandResult.Ok, s.OpenContainer(box.Id));
                Assert.AreEqual(hires, s.Mine.HireCount, "котик доставки n не меняет");
                claims++;
                if (claims == 10) Assert.AreEqual(36.82, s.NextDeliveryIntervalSeconds(false), 0.01, "после 10 получений");
                if (claims == 30) Assert.AreEqual(69.85, s.NextDeliveryIntervalSeconds(false), 0.01, "после 30 получений");
            }
            Assert.GreaterOrEqual(claims, 30);
            Assert.IsTrue(sawCat, "доставка с котиком встречается");
            Assert.IsTrue(sawCore15, "доставка id15 встречается");
        }

        // ───────────────────────────── E-16 ─────────────────────────────

        [Test]
        public void E16_DynamiteInSession()
        {
            var s = DebugSession(5);
            var gate = s.Layout.Gates[0];
            Assert.AreEqual(20800.0, D(s.Mine.Objects[gate.Ordinal].Hp));
            // Котиков нет — уровень 1, урон 1050.
            Assert.AreEqual(1, s.DynamiteLevel());
            Assert.AreEqual(1050.0, D(s.DynamiteDamage()), 1e-9);
            s.DebugAddDynamite(1);
            Assert.AreEqual(CommandResult.Ok, s.UseDynamite(3.5, gate.Row + 0.5));
            Assert.AreEqual(20800.0 - 1050.0, D(s.Mine.Objects[gate.Ordinal].Hp), 1e-9);
            // Уровень = максимальный уровень котика на поле: L2 → 1380, L3 → 2073.
            Assert.IsTrue(s.DebugSpawnCat(2));
            Assert.AreEqual(2, s.DynamiteLevel());
            Assert.AreEqual(1380.0, D(s.DynamiteDamage()), 1e-9);
            Assert.IsTrue(s.DebugSpawnCat(3));
            Assert.IsTrue(s.DebugSpawnCat(1));
            Assert.AreEqual(3, s.DynamiteLevel());
            Assert.AreEqual(2073.0, D(s.DynamiteDamage()), 1e-6);
            s.DebugAddDynamite(1);
            Assert.AreEqual(CommandResult.Ok, s.UseDynamite(3.5, gate.Row + 0.5));
            Assert.AreEqual(20800.0 - 1050.0 - 2073.0, D(s.Mine.Objects[gate.Ordinal].Hp), 1e-6);

            // Запас не больше 4: при 4 зарядах доставки динамита выпадают из пула.
            Assert.AreEqual(4, GameSession.MaxDynamite);
            var s7 = DebugSession(7);
            s7.DebugAddDynamite(3);
            Assert.IsTrue(s7.DeliveryPool().Any(d => d.RewardModel.RewardType == RewardTypes.Spell), "3 заряда — динамит в пуле");
            s7.DebugAddDynamite(1);
            Assert.AreEqual(4, s7.Player.Dynamite);
            Assert.IsFalse(s7.DeliveryPool().Any(d => d.RewardModel.RewardType == RewardTypes.Spell), "4 заряда — динамита в пуле нет");
        }

        // ───────────────────────────── E-17 ─────────────────────────────

        [Test]
        public void E17_FreeChestTimerContentsAndPersistence()
        {
            var fg = B.FreeGachaCycle;
            Assert.AreEqual(4.0, fg.Hours);
            Assert.AreEqual(2, fg.MaxFreeGachas);
            Assert.AreEqual("scg_free_01", fg.GachaIds[0]);
            Assert.AreEqual("scg_free_02", fg.GachaIds[1]);

            var s = DebugSession(4);
            Assert.AreEqual(CommandResult.NotAllowed, s.ClaimFreeChest(), "до включения сундука нет");
            ClearAndActivate(s, s.Layout.Shafts[0]);
            Assert.IsFalse(s.Player.FreeChest.Enabled);
            ClearAndActivate(s, s.Layout.Shafts[1]);
            var fc = s.Player.FreeChest;
            Assert.IsTrue(fc.Enabled, "включение в t0");
            Assert.AreEqual(0, fc.Stock, "сразу сундук не выдаётся");
            Assert.AreEqual(CommandResult.NotAllowed, s.ClaimFreeChest());
            // t0 + 4 ч, t0 + 8 ч; в t0 + 12 ч запас 2.
            s.Advance(4 * Hour - 1);
            Assert.AreEqual(0, fc.Stock);
            s.Advance(1);
            Assert.AreEqual(1, fc.Stock, "t0 + 4 ч");
            s.Advance(4 * Hour - 1);
            Assert.AreEqual(1, fc.Stock);
            s.Advance(1);
            Assert.AreEqual(2, fc.Stock, "t0 + 8 ч");
            s.Advance(4 * Hour);
            Assert.AreEqual(2, fc.Stock, "t0 + 12 ч: запас не больше 2");

            // scg_free_01: ca002, ca015, 19 эликсира.
            var elixir = s.Player.Elixir;
            int ca002 = Copies(s, "ca002"), ca015 = Copies(s, "ca015"), ca001 = Copies(s, "ca001");
            Assert.AreEqual(CommandResult.Ok, s.ClaimFreeChest());
            Assert.AreEqual(19.0, D(s.Player.Elixir - elixir));
            Assert.AreEqual(1, Copies(s, "ca002") - ca002);
            Assert.AreEqual(1, Copies(s, "ca015") - ca015);
            Assert.AreEqual(1, fc.Stock);
            // При полном запасе таймер стоял; после открытия — снова 4 ч.
            s.Advance(4 * Hour - 1);
            Assert.AreEqual(1, fc.Stock);
            s.Advance(1);
            Assert.AreEqual(2, fc.Stock);

            // scg_free_02: ca001×2, 22 эликсира.
            elixir = s.Player.Elixir;
            Assert.AreEqual(CommandResult.Ok, s.ClaimFreeChest());
            Assert.AreEqual(22.0, D(s.Player.Elixir - elixir));
            Assert.AreEqual(2, Copies(s, "ca001") - ca001);
            Assert.AreEqual(2, fc.NextIndex);

            // После конца GachaIds — с начала: снова scg_free_01.
            fc.NextIndex = fg.GachaIds.Length;
            elixir = s.Player.Elixir;
            ca015 = Copies(s, "ca015");
            Assert.AreEqual(CommandResult.Ok, s.ClaimFreeChest());
            Assert.AreEqual("scg_free_01", s.Player.ChestReveals.Last().GachaId);
            Assert.AreEqual(19.0, D(s.Player.Elixir - elixir));
            Assert.AreEqual(1, Copies(s, "ca015") - ca015);

            // Флаг постоянный и между зонами не сбрасывается (доставки — сбрасываются).
            s.Advance(4 * Hour);
            int stock = fc.Stock;
            int index = fc.NextIndex;
            foreach (var sh in s.Layout.Shafts)
                if (s.Mine.Objects[sh.Ordinal].Status != ObjectStatus.Producing) ClearAndActivate(s, sh);
            s.DebugCompleteTarget(s.Layout.Exit.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            Assert.AreEqual(5, s.Rank);
            Assert.IsFalse(s.Mine.DeliveriesEnabled, "доставки ждут условий в новой зоне");
            Assert.IsTrue(s.Player.FreeChest.Enabled, "флаг бесплатного сундука сохраняется");
            Assert.AreEqual(stock, s.Player.FreeChest.Stock);
            Assert.AreEqual(index, s.Player.FreeChest.NextIndex);
        }

        // ───────────────────────────── E-19 ─────────────────────────────

        [Test]
        public void E19_OfflineLimit8Hours()
        {
            Assert.AreEqual(8L * 3600L * S, GameSession.MaxOfflineTicks);
            var s = NewSession(1);
            long start = s.State.LastProcessedUtcMs;
            var summary = OfflineProgress.Apply(s, start + 24L * 3600L * 1000L);
            Assert.IsTrue(summary.Capped);
            Assert.AreEqual(28800.0, summary.Seconds);
            // Кузница за 8 ч: 14 400 циклов по 3 золота.
            Assert.AreEqual(25.0 + 14400 * 3, D(s.Mine.Gold));
            Assert.AreEqual(43200.0, D(summary.Gold));
        }

        // ───────────────────────────── E-20 ─────────────────────────────

        [Test]
        public void E20_MimicRockUsesOwnRewardCycle()
        {
            var own = B.Rocks["rockonboardingmine2"];
            Assert.AreEqual("rockbasic", own.MimicId);
            Assert.AreEqual(8, own.RewardCycle.Length);
            Assert.AreNotEqual(B.Rocks["rockbasic"].RewardCycle.Length, own.RewardCycle.Length);
            Assert.AreEqual(RewardTypes.Gacha, B.Rocks["rockbasic"].RewardCycle[3].RewardType, "у rockbasic 4-я запись — сундук");

            var s = DebugSession(2);
            var rocks = s.Layout.Objects.Where(o => o.TypeId == "rockonboardingmine2").ToList();
            Assert.AreEqual(11, rocks.Count);
            for (int i = 0; i < rocks.Count; i++)
            {
                var gold = s.Mine.Gold;
                var elixir = s.Player.Elixir;
                s.DebugCompleteTarget(rocks[i].Ordinal);
                bool soft = own.RewardCycle[i % 8].DetailedType == "Soft";
                Assert.AreEqual(i == 3, soft, "в цикле rockonboardingmine2 эликсир — только 4-я запись");
                if (soft)
                {
                    Assert.AreEqual(1.0, D(s.Player.Elixir - elixir), "камень " + (i + 1) + ": эликсир");
                    Assert.AreEqual(D(gold), D(s.Mine.Gold));
                }
                else
                {
                    double expected = rocks[i].Level == 1 ? 157 : 337;
                    Assert.AreEqual(expected, D(s.Mine.Gold - gold), "камень " + (i + 1) + ": золото");
                    Assert.AreEqual(D(elixir), D(s.Player.Elixir));
                }
            }
            Assert.AreEqual(11L, s.Player.RewardCycle["rockonboardingmine2"]);
            Assert.IsFalse(s.Player.RewardCycle.ContainsKey("rockbasic"), "счётчик ведётся по Rocks.Id, не по MimicId");
            Assert.AreEqual(0, s.Player.ChestReveals.Count, "сундуков из цикла rockbasic нет");
        }

        // ───────────────────────────── C.2: геометрия ─────────────────────────────

        [Test]
        public void G02_G03_ExitAndGateSidesAndWorkRows()
        {
            int gates = 0;
            for (int n = 1; n <= B.ZoneCount; n++)
            {
                var l = ZoneLayout.Build(B, n);
                // Выход: (1, 0) и (5, 0) — препятствия во всех зонах.
                foreach (int col in new[] { 1, 5 })
                {
                    int owner = l.CellOwner[l.CellIndex(col, 0)];
                    Assert.GreaterOrEqual(owner, 0, l.ZoneId + " (" + col + ", 0)");
                    Assert.AreEqual(ObjectKind.Obstruction, l.Objects[owner].Kind, l.ZoneId + " (" + col + ", 0)");
                }
                // Ворота: (0, row_p) и (6, row_p) — препятствия во всех 280 случаях.
                foreach (var g in l.Gates)
                {
                    gates++;
                    foreach (int col in new[] { 0, 6 })
                    {
                        int owner = l.CellOwner[l.CellIndex(col, g.Row)];
                        Assert.GreaterOrEqual(owner, 0, l.ZoneId + " ворота (" + col + ", " + g.Row + ")");
                        Assert.AreEqual(ObjectKind.Obstruction, l.Objects[owner].Kind, l.ZoneId + " ворота (" + col + ", " + g.Row + ")");
                    }
                }
            }
            Assert.AreEqual(280, gates);

            // Рабочая строка выхода — 1; в zone1 две рабочие клетки, (4, 1) занята препятствием.
            var s1 = DebugSession(1);
            s1.DebugCompleteTarget(OrdAt(s1, 2, 1));
            s1.DebugCompleteTarget(OrdAt(s1, 3, 1));
            var work = new List<int>();
            s1.WorkCells(s1.Layout.Exit.Ordinal, work);
            CollectionAssert.AreEquivalent(new[] { (2, 1), (3, 1) }, work.Select(c => ColRow(s1.Layout, c)).ToList());
            var s2 = DebugSession(2);
            foreach (int col in new[] { 2, 3, 4 }) s2.DebugCompleteTarget(OrdAt(s2, col, 1));
            s2.WorkCells(s2.Layout.Exit.Ordinal, work);
            CollectionAssert.AreEquivalent(new[] { (2, 1), (3, 1), (4, 1) }, work.Select(c => ColRow(s2.Layout, c)).ToList());

            // Рабочая строка ворот — только row_p + 1. zone5: ворота в строке 11; в строке 12 после расчистки камней
            // свободны (2..4, 12); (1, 12) — footprint block2x3vL с якорем (0, 14), (5, 12) — block2x1. Строка 10 — закрытый сектор.
            var s5 = DebugSession(5);
            var gate = s5.Layout.Gates[0];
            Assert.AreEqual(11, gate.Row);
            foreach (int col in new[] { 2, 3, 4 }) s5.DebugCompleteTarget(OrdAt(s5, col, gate.Row + 1));
            s5.WorkCells(gate.Ordinal, work);
            CollectionAssert.AreEquivalent(new[] { (2, 12), (3, 12), (4, 12) }, work.Select(c => ColRow(s5.Layout, c)).ToList());
            Assert.AreEqual(ObjectKind.Obstruction, s5.Layout.Objects[s5.Layout.CellOwner[s5.Layout.CellIndex(1, 12)]].Kind);
        }

        [Test]
        public void G04_CartFootprintPadAndNotATarget()
        {
            int depthMinus4 = 0;
            for (int n = 1; n <= B.ZoneCount; n++)
            {
                var l = ZoneLayout.Build(B, n);
                var cart = l.Cart;
                int rc = l.RowC;
                if (n <= 2) Assert.AreEqual(l.Depth - 3, rc, l.ZoneId);
                else if (rc == l.Depth - 4) depthMinus4++;
                // Кузница 2×3 (ТЗ 6.4): клетки (1..2, row_c − 1 … row_c + 1).
                CollectionAssert.AreEquivalent(new[] { (1, rc - 1), (2, rc - 1), (1, rc), (2, rc), (1, rc + 1), (2, rc + 1) },
                    cart.Cells.Select(c => ColRow(l, c)).ToList(), l.ZoneId);
                // Строки row_c − 3 … row_c + 1 свободны во всех 7 столбцах, кроме клеток кузницы.
                for (int row = rc - 3; row <= rc + 1; row++)
                    for (int col = 0; col < l.Width; col++)
                    {
                        int owner = l.CellOwner[l.CellIndex(col, row)];
                        Assert.IsTrue(owner < 0 || owner == cart.Ordinal, l.ZoneId + " (" + col + ", " + row + ")");
                    }
            }
            Assert.AreEqual(142, depthMinus4, "строка Depth − 4 в 142 зонах");

            // Кузница — не цель: HP-таблицы нет, котика и динамит туда не поставить.
            Assert.IsFalse(B.HasHpTable(BalanceRepository.CartId));
            var s = DebugSession(1);
            Assert.IsFalse(s.Layout.Cart.IsTarget);
            Assert.IsTrue(s.DebugSpawnCat(1));
            var cat = s.Mine.Cats[0];
            int origin = cat.Cell;
            int rcz = s.Layout.RowC;
            Assert.AreNotEqual(CommandResult.Ok, s.DropCat(cat.Id, 1.5, rcz + 0.5).Result);
            Assert.AreEqual(origin, cat.Cell);
            s.DebugAddDynamite(1);
            Assert.AreNotEqual(CommandResult.Ok, s.UseDynamite(1.5, rcz + 0.5));
            Assert.AreEqual(1, s.Player.Dynamite, "заряд не тратится");
        }

        [Test]
        public void G05_WorkCellsAreFourNeighbours()
        {
            int col0 = 0, col5 = 0;
            for (int n = 1; n <= B.ZoneCount; n++)
                foreach (var sh in ZoneLayout.Build(B, n).Shafts)
                {
                    if (sh.Col == 0) col0++;
                    else if (sh.Col == 5) col5++;
                }
            Assert.AreEqual(622, col0, "шахты в столбцах 0–1");
            Assert.AreEqual(610, col5, "шахты в столбцах 5–6");

            var work = new List<int>();
            for (int n = 1; n <= 10; n++)
            {
                var s = DebugSession(n);
                var l = s.Layout;
                foreach (var o in l.Objects.Where(o => o.Kind == ObjectKind.Rock || o.Kind == ObjectKind.Shaft))
                {
                    s.WorkCells(o.Ordinal, work);
                    // Камень 1×1 — до 4 работников, шахта 2×3 — до 7 (стоит у края карты).
                    int max = o.Kind == ObjectKind.Shaft ? 7 : 2 * (o.Width + o.Depth);
                    if (o.Kind == ObjectKind.Rock && o.Width == 1 && o.Depth == 1) Assert.AreEqual(4, max);
                    Assert.LessOrEqual(work.Count, max, l.ZoneId + " " + o.Token);
                    foreach (int w in work)
                    {
                        bool side = o.Cells.Any(c => Math.Abs(l.Col(c) - l.Col(w)) + Math.Abs(l.Row(c) - l.Row(w)) == 1);
                        Assert.IsTrue(side, l.ZoneId + " " + o.Token + ": клетка " + ColRow(l, w) + " не соседствует по стороне");
                        Assert.IsFalse(o.Cells.Contains(w));
                    }
                }
            }

            // zone2: после расчистки всех камней у шахты amethyst 2×3 (0..1, 3..5) четыре рабочие клетки.
            var s2 = DebugSession(2);
            foreach (var r in s2.Layout.Objects.Where(o => o.Kind == ObjectKind.Rock).ToList()) s2.DebugCompleteTarget(r.Ordinal);
            var am = s2.Layout.Shafts.Single();
            s2.WorkCells(am.Ordinal, work);
            CollectionAssert.AreEquivalent(new[] { (2, 3), (2, 4), (2, 5), (1, 6) }, work.Select(c => ColRow(s2.Layout, c)).ToList());
        }

        [Test]
        public void G06_SpawnCellOrder()
        {
            // Без ворот: BFS от (3, row_c − 2) — нижний ряд зоны мержа; соседи — верх, лево, право, низ; сначала зона мержа.
            var s = DebugSession(1);
            int rc = s.Layout.RowC;
            Assert.AreEqual(9, rc);
            s.DebugAddGold(GameNumber.FromDouble(1000));
            var expected = new[] { (3, rc - 2), (3, rc - 3), (2, rc - 2), (4, rc - 2) };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(CommandResult.Ok, s.Hire());
                Assert.AreEqual(expected[i], ColRow(s.Layout, s.Mine.Cats[i].Cell), "котик " + (i + 1));
            }

            // С открытыми воротами: площадка row_p − 3 … row_p − 1, зона мержа row_p − 2, row_p − 1, старт (3, row_p − 1).
            var s5 = DebugSession(5);
            var gate = s5.Layout.Gates[0];
            BreakAndOpenGate(s5, gate);
            Assert.AreEqual((3, gate.Row - 1), ColRow(s5.Layout, s5.FindSpawnCell()));
        }
    }
}
