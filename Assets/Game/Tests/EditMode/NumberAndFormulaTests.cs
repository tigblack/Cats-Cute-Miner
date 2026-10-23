using System;
using CatMiners.Core;
using NUnit.Framework;

namespace CatMiners.Tests
{
    public class GameNumberTests
    {
        [Test]
        public void ArithmeticAndComparison()
        {
            var a = GameNumber.FromDouble(1500);
            var b = GameNumber.FromDouble(2.5e3);
            Assert.AreEqual(4000.0, (a + b).ToDouble(), 1e-9);
            Assert.AreEqual(-1000.0, (a - b).ToDouble(), 1e-9);
            Assert.AreEqual(3.75e6, (a * b).ToDouble(), 1e-6);
            Assert.AreEqual(0.6, (a / b).ToDouble(), 1e-12);
            Assert.IsTrue(a < b);
            Assert.IsTrue(-b < -a);
            Assert.IsTrue(GameNumber.Zero < a);
            Assert.IsTrue(-a < GameNumber.Zero);
        }

        [Test]
        public void BeyondDoubleRange()
        {
            var big = GameNumber.Pow(1.9, 1200);            // ~1e334
            Assert.IsFalse(big.FitsInDouble);
            Assert.Greater(big.Exponent, 308);
            var twice = big + big;
            Assert.AreEqual(big.Exponent, twice.Exponent);
            Assert.AreEqual(2 * big.Mantissa, twice.Mantissa, 1e-9);
            Assert.IsTrue(big > GameNumber.FromDouble(double.MaxValue));
            var parsed = GameNumber.Parse(big.ToStorageString());
            Assert.AreEqual(big, parsed);
        }

        [Test]
        public void StorageRoundTrip()
        {
            foreach (var v in new[] { 0.0, 1.0, 25.0, 157.5, 1.27e19, 1.68e20, 1e200, 7.39e35, 0.1, -42.0 })
            {
                var g = GameNumber.FromDouble(v);
                Assert.AreEqual(g, GameNumber.Parse(g.ToStorageString()), "value " + v);
            }
        }

        [Test]
        public void FloorCeiling()
        {
            Assert.AreEqual(102.0, GameNumber.FromDouble(102.96).Floor().ToDouble());
            Assert.AreEqual(103.0, GameNumber.FromDouble(102.96).Ceiling().ToDouble());
            Assert.AreEqual(0.0, GameNumber.FromDouble(0.4).Floor().ToDouble());
            Assert.AreEqual(1.0, GameNumber.FromDouble(0.4).Ceiling().ToDouble());
            var huge = GameNumber.Pow(10, 40);
            Assert.AreEqual(huge, huge.Floor());
        }

        [Test]
        public void Formatting()
        {
            Assert.AreEqual("0", NumberFormat.Format(GameNumber.Zero));
            Assert.AreEqual("25", NumberFormat.Format(25));
            Assert.AreEqual("999", NumberFormat.Format(999));
            Assert.AreEqual("1.5", NumberFormat.Format(1.5));
            Assert.AreEqual("1K", NumberFormat.Format(1000));
            Assert.AreEqual("1.23K", NumberFormat.Format(1234));
            Assert.AreEqual("12.3K", NumberFormat.Format(12345));
            Assert.AreEqual("123K", NumberFormat.Format(123456));
            Assert.AreEqual("1.5M", NumberFormat.Format(1.5e6));
            Assert.AreEqual("2B", NumberFormat.Format(2e9));
            Assert.AreEqual("3.14T", NumberFormat.Format(3.14159e12));
            Assert.AreEqual("1aa", NumberFormat.Format(1e15));
            Assert.AreEqual("1ab", NumberFormat.Format(1e18));
            Assert.AreEqual("12.7ab", NumberFormat.Format(1.27e19));
            // ТЗ 15.1: двухбуквенные суффиксы — только в диапазоне double; «1.23e352» — пример научной записи вне его.
            Assert.AreEqual("1.00e352", NumberFormat.Format(GameNumber.Pow(10, 352)));
            Assert.AreEqual("1.23e352", NumberFormat.Format(new GameNumber(1.239, 352)), "мантисса усекается, а не округляется");
            Assert.AreEqual("9.99e400", NumberFormat.Format(new GameNumber(9.9999, 400)));
            var veryBig = new GameNumber(1.23, 5000);
            Assert.AreEqual("1.23e5000", NumberFormat.Format(veryBig));
            // Граница: последний суффикс «dt» (1e306), с 1e308 — научная запись.
            Assert.AreEqual("dt", NumberFormat.SuffixForGroup(102));
            Assert.IsNull(NumberFormat.SuffixForGroup(103));
            Assert.AreEqual("1dt", NumberFormat.Format(GameNumber.Pow(10, 306)));
            Assert.AreEqual("12.3dt", NumberFormat.Format(1.234e307));
            Assert.AreEqual("1.00e308", NumberFormat.Format(1e308));
            Assert.AreEqual("1.79e308", NumberFormat.Format(double.MaxValue));
            Assert.AreEqual("-1.50e320", NumberFormat.Format(-new GameNumber(1.5, 320)));
        }
    }

    /// <summary>Контрольные значения рабочего профиля v1 (ТЗ, Приложение C). Значения записаны числами.</summary>
    public class FormulaControlValueTests
    {
        static double D(GameNumber g) => g.ToDouble();

        [Test]
        public void E02_CatDamage()
        {
            var m = TestData.Balance.Miner;
            double[] expected = { 1, 2.1, 4.41, 9.261 };
            for (int L = 1; L <= 4; L++)
                Assert.AreEqual(expected[L - 1], D(Formula.Evaluate(m.DamageFormulaType, m.DamageBase, m.DamageMultiplier, m.DamageGrowth, L)), 1e-9, "L" + L);
        }

        [Test]
        public void E03_UpgradeCosts()
        {
            var cart = TestData.Balance.Cart;
            double[] forge = { 72, 103, 148, 211 };
            for (int L = 1; L <= 4; L++)
                Assert.AreEqual(forge[L - 1], D(Formula.Price(Formula.Evaluate(cart.UpgradeCostFormulaType, cart.UpgradeCostBase, cart.UpgradeCostMultiplier, cart.UpgradeCostGrowth, L))), "forge L" + L);
            var am = TestData.Balance.Shafts["amethyst"];
            double[] amethyst = { 512, 748, 1092 };
            for (int L = 1; L <= 3; L++)
                Assert.AreEqual(amethyst[L - 1], D(Formula.Price(Formula.Evaluate(am.UpgradeCostFormulaType, am.UpgradeCostBase, am.UpgradeCostMultiplier, am.UpgradeCostGrowth, L))), "amethyst L" + L);
            Assert.AreEqual(3.0, D(Formula.Evaluate(cart.CurrencyOutputFormulaType, cart.CurrencyOutputBase, cart.CurrencyOutputMultiplier, cart.CurrencyOutputGrowth, 1)));
            Assert.AreEqual(400.0, D(Formula.Evaluate(am.CurrencyOutputFormulaType, am.CurrencyOutputBase, am.CurrencyOutputMultiplier, am.CurrencyOutputGrowth, 1)));
        }

        [Test]
        public void E05_HireCostAndLevel()
        {
            var r = TestData.Balance.Reinforcement;
            double[] cost = { 2, 8, 32, 128, 512, 2048 };
            for (int n = 0; n < cost.Length; n++)
                Assert.AreEqual(cost[n], D(Formula.Price(Formula.Evaluate(r.CostFormulaType, r.CostBase, r.CostMultiplier, r.CostGrowth, n + 1))), "n=" + n);
            double[] afterGate = { 1, 1, 4, 13 };
            for (int n = 0; n < afterGate.Length; n++)
                Assert.AreEqual(afterGate[n], D(Formula.Price(Formula.Evaluate(r.CostFormulaType, r.CostBase, r.CostMultiplier, r.CostGrowth, n + 1) / GameNumber.FromDouble(10))), "gate n=" + n);
            int[] levels = { 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 4 };
            for (int n = 0; n < levels.Length; n++)
                Assert.AreEqual(levels[n], 1 + (int)Math.Floor(D(Formula.Evaluate(r.LevelFormulaType, r.LevelBase, r.LevelMultiplier, r.LevelGrowth, n)) + 1e-9), "n=" + n);
        }

        [Test]
        public void E09_RockCoreSecondsAndSoft()
        {
            var rb = TestData.Balance.Rocks["rockbasic"];
            double[] secs = { 105, 225, 360 };
            for (int L = 1; L <= 3; L++)
                Assert.AreEqual(secs[L - 1], Formula.Ft3(rb.CoreCurrencySecondsBase, rb.CoreCurrencySecondsMultiplier, rb.CoreCurrencySecondsGrowth, rb.CoreCurrencySecondsPower, rb.CoreCurrencySecondsRound, L), 1e-9);
            Assert.AreEqual(157.0, D(Formula.Payout(GameNumber.FromDouble(105 * 1.5))));
            var small = TestData.Balance.Rocks["rocksoftcurrencysmall"];
            var mult = TestData.Balance.ZoneMultipliers(1).MiningSoftCurrencyMultiplier;
            Assert.AreEqual(2.0, D(Formula.Payout(Formula.Ft0(small.SoftCurrencyBase, small.SoftCurrencyMultiplier, small.SoftCurrencyGrowth, 2) * GameNumber.FromDouble(mult))));
        }

        [Test]
        public void E07_E16_CardsAndDynamite()
        {
            Assert.AreEqual(1.0, D(Formula.Ft1(0, 1, 3, 1)));
            Assert.AreEqual(3.0, D(Formula.Ft1(0, 1, 3, 2)));
            Assert.AreEqual(1594323.0, D(Formula.Ft1(0, 1, 3, 14)), 1e-3);
            Assert.AreEqual(4194304.0, D(Formula.Ft1(0, 1, 4, 12)), 1e-3);
            var sp = TestData.Balance.Spell;
            double[] dyn = { 1050, 1380, 2073 };
            for (int L = 1; L <= 3; L++)
                Assert.AreEqual(dyn[L - 1], D(Formula.Evaluate(sp.EffectFormulaType, sp.EffectBase, sp.EffectMultiplier, sp.EffectGrowth, L)), 1e-6);
        }

        [Test]
        public void E15_DeliveryIntervalAndQuantity()
        {
            var p = TestData.Balance.Props;
            double[] expected = { 22.0, 36.82, 69.85 };
            int[] claims = { 0, 10, 30 };
            for (int i = 0; i < claims.Length; i++)
                Assert.AreEqual(expected[i], D(Formula.Evaluate(p.DeliveryDelaySecFormulaType, p.DeliveryDelaySecBase, p.DeliveryDelaySecMultiplier, p.DeliveryDelaySecGrowth, claims[i] + 1)), 0.01);
            var d15 = TestData.Balance.Deliveries["15"];
            Assert.AreEqual(60.0, Formula.Ft3(d15.QuantityBase, d15.QuantityMultiplier, d15.QuantityGrowth, d15.QuantityPower, d15.QuantityRound, 4));
            Assert.AreEqual(300.0, Formula.Ft3(d15.QuantityBase, d15.QuantityMultiplier, d15.QuantityGrowth, d15.QuantityPower, d15.QuantityRound, 10));
            var d3 = TestData.Balance.Deliveries["3"];
            Assert.AreEqual(-2.0, Formula.Ft3(d3.QuantityBase, d3.QuantityMultiplier, d3.QuantityGrowth, d3.QuantityPower, d3.QuantityRound, 4));
        }

        [Test]
        public void RoundingRules()
        {
            Assert.AreEqual(105.0, Formula.RoundToMultiple(109.9, 15));
            Assert.AreEqual(-2.0, Formula.RoundToMultiple(-2.0, -1));
            Assert.AreEqual(5, Formula.RoundHalfUp(4.5));
            Assert.AreEqual(7, Formula.RoundHalfUp(6.5));
            Assert.AreEqual(53, Formula.RoundHalfUp(52.5));
            Assert.AreEqual(1, Formula.RoundHalfUp(0.7));
            Assert.AreEqual(1.0, D(Formula.Price(GameNumber.FromDouble(0.2))));
            Assert.AreEqual(1.0, D(Formula.Payout(GameNumber.FromDouble(0.3))));
            Assert.AreEqual(0.0, D(Formula.Payout(GameNumber.Zero)));
        }

        [Test]
        public void E11_BigCostsStayFinite()
        {
            var s = TestData.Balance.Shafts["sunstone"];
            var c1042 = Formula.Evaluate(s.UpgradeCostFormulaType, s.UpgradeCostBase, s.UpgradeCostMultiplier, s.UpgradeCostGrowth, 1042);
            var c1043 = Formula.Evaluate(s.UpgradeCostFormulaType, s.UpgradeCostBase, s.UpgradeCostMultiplier, s.UpgradeCostGrowth, 1043);
            Assert.IsTrue(c1042.FitsInDouble);
            Assert.IsFalse(c1043.FitsInDouble);
            var c1199 = Formula.Evaluate(s.UpgradeCostFormulaType, s.UpgradeCostBase, s.UpgradeCostMultiplier, s.UpgradeCostGrowth, 1199);
            Assert.Greater(c1199.Exponent, 340);
            Assert.AreEqual(c1199, GameNumber.Parse(c1199.ToStorageString()));
        }
    }
}
