using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using CatMiners.Core.Outpost;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostKitA;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Точная арифметика (ТЗ §12.2, §18; CORE_SPEC §2), большие целые выше 2^53 (ТЗ §20 «Большие целые»)
    /// и контрольная таблица ТЗ §12.5 (значения сверены с controls в outpost_v2_audit.json).
    /// </summary>
    [TestFixture]
    public class OutpostMathTests
    {
        static readonly BigInteger TwoPow53 = BigInteger.Pow(2, 53);

        // ───────────────────────────── Rational / ExactMath ─────────────────────────────

        // ТЗ §12.2: «Десятичные коэффициенты трактовать точно, например 1.20 = 6/5, 1.24 = 31/25».
        [TestCase("1.18", 59, 50)]
        [TestCase("1.20", 6, 5)]
        [TestCase("1.24", 31, 25)]
        [TestCase("1.10", 11, 10)]
        [TestCase("6/5", 6, 5)]
        [TestCase("5", 5, 1)]
        [TestCase("0.005", 1, 200)]
        [TestCase("2.50", 5, 2)]
        [TestCase("0.05", 1, 20)]
        [TestCase("12/8", 3, 2)]
        [TestCase("117/100", 117, 100)]
        [TestCase("0", 0, 1)]
        public void Rational_Parse_IsExactAndReduced(string text, long num, long den)
        {
            AssertExact(Rational.Parse(text), FracA.Of(num, den), text);
        }

        // ТЗ §12.2: формат хранения без локализованных разделителей — разбор не зависит от культуры потока.
        [Test]
        public void Rational_Parse_IgnoresCurrentCulture()
        {
            var saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
                AssertExact(Rational.Parse("1.18"), FracA.Of(59, 50));
                AssertExact(Rational.Parse("0.005"), FracA.Of(1, 200));
                Assert.AreEqual("59/50", Rational.Parse("1.18").ToString());
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("1/0")]
        [TestCase("1/")]
        [TestCase("/2")]
        public void Rational_Parse_Invalid_Throws(string text)
        {
            Assert.That(() => Rational.Parse(text), Throws.Exception, "строка «" + text + "» не является дробью");
        }

        // CORE_SPEC §2: Den > 0, дробь всегда несократимая.
        [Test]
        public void Rational_Constructor_NormalizesSignAndGcd()
        {
            AssertExact(new Rational(6, 4), FracA.Of(3, 2));
            AssertExact(new Rational(3, -6), FracA.Of(-1, 2));
            AssertExact(new Rational(0, 5), FracA.Of(0, 1));
            Assert.That(new Rational(7, -3).Den.Sign, Is.EqualTo(1), "знаменатель всегда положителен");
        }

        [Test]
        public void Rational_Arithmetic_IsExact()
        {
            // 0.1 + 0.2 — классическая ошибка double; здесь ровно 3/10
            AssertExact(R("0.1") + R("0.2"), FracA.Of(3, 10));
            AssertExact(R("6/5") * R("5/6"), FracA.Of(1));
            AssertExact(R("1") - R("59/50"), FracA.Of(-9, 50));
            AssertExact(R("59/50") / R("6/5"), FracA.Of(59, 60));
            Assert.IsTrue(R("0.1") + R("0.2") == R("3/10"));
        }

        [Test]
        public void Rational_Comparison_IsExact()
        {
            Assert.IsTrue(R("59/50") < R("6/5"));
            Assert.IsTrue(R("351/1000") > R("7/20"), "редкая 20 (0.351) больше эпической 1 (0.35)");
            Assert.AreEqual(0, R("63/100").CompareTo(R("0.63")));
            Assert.AreEqual(R("1/2"), R("0.5"));
            Assert.IsTrue(R("-1/3") < R("0"));
        }

        // ТЗ §12.2: «Вычислять степень и произведение до единственного итогового ceil».
        [TestCase("6/5", 0, "1")]
        [TestCase("6/5", 3, "216/125")]
        [TestCase("61/50", 1, "61/50")]
        [TestCase("59/50", 10, "511116753300641401/97656250000000000")]
        [TestCase("11/10", 2, "121/100")]
        public void Rational_Pow_IsExact(string baseText, int exponent, string expected)
        {
            AssertExact(R(baseText).Pow(exponent), FracA.Parse(expected), baseText + "^" + exponent);
        }

        [Test]
        public void Rational_Pow_Zone144Scale_IsExact()
        {
            // S(144) = 1.2^143 = 6^143 / 5^143 — точно, без потери знаков
            var s = R("6/5").Pow(143);
            Assert.AreEqual(BigInteger.Pow(6, 143), s.Num);
            Assert.AreEqual(BigInteger.Pow(5, 143), s.Den);
        }

        [TestCase(7, 2, 4, 3)]
        [TestCase(6, 2, 3, 3)]
        [TestCase(1, 3, 1, 0)]
        [TestCase(0, 1, 0, 0)]
        [TestCase(-7, 2, -3, -4)]
        [TestCase(-1, 3, 0, -1)]
        [TestCase(20, 1, 20, 20)]
        public void Rational_CeilFloor_AreMathematical(long num, long den, long ceil, long floor)
        {
            var r = new Rational(num, den);
            Assert.AreEqual(new BigInteger(ceil), r.Ceil(), "Ceil " + num + "/" + den);
            Assert.AreEqual(new BigInteger(floor), r.Floor(), "Floor " + num + "/" + den);
        }

        // ТЗ §7.1: урон округляется до миллионной доли HP «половины вверх».
        [TestCase(1, 2, 1)]
        [TestCase(3, 2, 2)]
        [TestCase(5, 2, 3)]
        [TestCase(7, 2, 4)]
        [TestCase(2, 5, 0)]
        [TestCase(3, 5, 1)]
        [TestCase(49, 100, 0)]
        [TestCase(51, 100, 1)]
        [TestCase(7, 1, 7)]
        [TestCase(0, 1, 0)]
        public void Rational_RoundHalfUp(long num, long den, long expected)
        {
            Assert.AreEqual(new BigInteger(expected), new Rational(num, den).RoundHalfUp(), num + "/" + den);
        }

        [Test]
        public void Rational_RoundingAbove2Pow53_KeepsUnit()
        {
            // (2·2^53 + 1)/2 = 2^53 + 0.5 → половина вверх 2^53 + 1; double дал бы 2^53
            var half = new Rational(TwoPow53 * 2 + 1, 2);
            Assert.AreEqual(TwoPow53 + 1, half.RoundHalfUp());
            Assert.AreEqual(TwoPow53 + 1, half.Ceil());
            Assert.AreEqual(TwoPow53, half.Floor());
            var third = new Rational(BigInteger.Pow(2, 60) * 3 + 1, 3);
            Assert.AreEqual(BigInteger.Pow(2, 60) + 1, third.Ceil());
        }

        // CORE_SPEC §2: ToString() → "n/d" или "n".
        [TestCase("59/50", "59/50")]
        [TestCase("1.18", "59/50")]
        [TestCase("5", "5")]
        [TestCase("10/2", "5")]
        [TestCase("-3/6", "-1/2")]
        public void Rational_ToString_IsCanonical(string text, string expected)
        {
            Assert.AreEqual(expected, R(text).ToString());
        }

        // ТЗ §12.4: «Использовать целочисленный sqrt/поиск … без переполнения».
        [TestCase("0", "0")]
        [TestCase("1", "1")]
        [TestCase("15", "3")]
        [TestCase("16", "4")]
        [TestCase("17", "4")]
        [TestCase("81129638414606681695789005144064", "9007199254740992")]  // (2^53)^2
        [TestCase("81129638414606681695789005144063", "9007199254740991")]  // (2^53)^2 − 1
        public void ExactMath_ISqrt_IsFloorOfRoot(string n, string expected)
        {
            Assert.AreEqual(BigInteger.Parse(expected, CultureInfo.InvariantCulture),
                ExactMath.ISqrt(BigInteger.Parse(n, CultureInfo.InvariantCulture)));
        }

        // ───────────────────────────── JSON: BigInteger и Rational ─────────────────────────────

        public sealed class BigBoxA
        {
            public BigInteger Value;
            public Rational Ratio;
        }

        // ТЗ §12.2/§18: «формат сохранения — десятичная строка без локализованных разделителей»; round-trip без потери единицы.
        [TestCase("9007199254740993")]          // 2^53 + 1 — double дал бы ...992
        [TestCase("13206982907366054057")]      // верхняя граница золота zone144 из аудита (профиль b4), больше long.MaxValue
        [TestCase("2194357721314998705")]       // полная цена ветки «Оценка» zone144 (профиль b4)
        [TestCase("123456789012345678901234567890")]
        [TestCase("0")]
        public void Json_BigInteger_IsDecimalString_AndRoundTrips(string digits)
        {
            var value = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
            string json = JsonConvert.SerializeObject(new BigBoxA { Value = value, Ratio = R("59/50") }, OutpostJson.Settings);
            var token = JObject.Parse(json)["Value"];
            Assert.AreEqual(JTokenType.String, token.Type, "BigInteger пишется строкой: " + json);
            Assert.AreEqual(digits, (string)token);
            var back = JsonConvert.DeserializeObject<BigBoxA>(json, OutpostJson.Settings);
            Assert.AreEqual(value, back.Value);
        }

        [Test]
        public void Json_BigInteger_IgnoresCurrentCulture()
        {
            var saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
                var value = TwoPow53 * 1000 + 7;
                string json = JsonConvert.SerializeObject(new BigBoxA { Value = value, Ratio = R("1/2") }, OutpostJson.Settings);
                Assert.AreEqual(value.ToString(CultureInfo.InvariantCulture), (string)JObject.Parse(json)["Value"]);
                Assert.AreEqual(value, JsonConvert.DeserializeObject<BigBoxA>(json, OutpostJson.Settings).Value);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        // CORE_SPEC §5: Rational → "n/d".
        [Test]
        public void Json_Rational_IsFractionString_AndRoundTrips()
        {
            string json = JsonConvert.SerializeObject(new BigBoxA { Value = 1, Ratio = R("1.18") }, OutpostJson.Settings);
            Assert.AreEqual("59/50", (string)JObject.Parse(json)["Ratio"]);
            var back = JsonConvert.DeserializeObject<BigBoxA>(json, OutpostJson.Settings);
            AssertExact(back.Ratio, FracA.Of(59, 50));
        }

        // ───────────────────────────── Большие целые (ТЗ §20) ─────────────────────────────

        [Test]
        public void NextCost_Zone144_ValueL39_IsExactAbove2Pow53()
        {
            // Последний уровень «Оценки» профиля b4 (cap 40): ceil(1.2^143 · (1 + 143/10) · 30 · 1.24^39) — выше 2^53;
            // double дал бы около 424792256739076544. На L40 (cap) цены нет.
            var expected = BigInteger.Parse("424792256739078908", CultureInfo.InvariantCulture);
            Assert.AreEqual(expected, ExpectedCost(144, 30, FracA.Of(31, 25), 39), "самопроверка эталона");
            Assert.AreEqual(expected, AuditBig(Audit["controls"]["value_cost_zone144_L39"]), "самопроверка аудита");
            Assert.IsTrue((bool)Audit["controls"]["value_cost_zone144_L39_exceeds_2_pow_53"]);
            // Профиль b6: точность больших целых проверяется на базовой цене аудита (без множителя темпа K(z), решение владельца
            // В4, 15 октября 2026); цена с K — NextCost_MatchesIndependentFormula.
            var actual = NextCost(144, UpgradeBranch.Value, 39, BaseBalance);
            Assert.IsTrue(actual.HasValue);
            Assert.AreEqual(expected, actual.Value);
            Assert.That(actual.Value, Is.GreaterThan(TwoPow53));
            Assert.IsFalse(NextCost(144, UpgradeBranch.Value, 40, BaseBalance).HasValue, "L40 — cap «Оценки», цены нет");
        }

        [Test]
        public void NextCost_Zone144_Pickaxes_IsExact()
        {
            // ceil(1.2^143 · 153/10 · 20 · 1.18^L), L = 0 и 19 (профиль b4).
            var audit = ((JArray)Audit["controls"]["pickaxe_costs_zone144_L0_L19"]).Select(AuditBig).ToArray();
            CollectionAssert.AreEqual(new[] { BigInteger.Parse("64363493905441", CultureInfo.InvariantCulture), BigInteger.Parse("1494162216779089", CultureInfo.InvariantCulture) },
                audit, "самопроверка аудита");
            Assert.AreEqual(ExpectedCost(144, 20, FracA.Of(59, 50), 0), audit[0], "самопроверка эталона");
            Assert.AreEqual(ExpectedCost(144, 20, FracA.Of(59, 50), 19), audit[1], "самопроверка эталона");
            // Профиль b6: базовая цена аудита (без K(z), решение владельца В4); цена с K — NextCost_MatchesIndependentFormula.
            Assert.AreEqual(BigInteger.Parse("64363493905441", CultureInfo.InvariantCulture), NextCost(144, UpgradeBranch.Pickaxes, 0, BaseBalance).Value);
            Assert.AreEqual(BigInteger.Parse("1494162216779089", CultureInfo.InvariantCulture), NextCost(144, UpgradeBranch.Pickaxes, 19, BaseBalance).Value);
        }

        [Test]
        public void NextCost_Zone144_ValueBranchTotal_SumIsExactAbove2Pow53()
        {
            // Σ L = 0..39 (cap 40, профиль b4).
            BigInteger sum = 0, expected = 0;
            // Профиль b6: базовые цены аудита (без K(z), решение владельца В4, 15 октября 2026).
            for (int level = 0; level < 40; level++)
            {
                sum += NextCost(144, UpgradeBranch.Value, level, BaseBalance).Value;
                expected += ExpectedCost(144, 30, FracA.Of(31, 25), level);
            }
            Assert.IsFalse(NextCost(144, UpgradeBranch.Value, 40, BaseBalance).HasValue, "cap 40");
            Assert.AreEqual(BigInteger.Parse("2194357721314998705", CultureInfo.InvariantCulture), expected, "самопроверка эталона");
            Assert.AreEqual(expected, AuditBig(Audit["controls"]["value_branch_total_zone144_L0_L39"]), "самопроверка аудита");
            Assert.AreEqual(BigInteger.Parse("2194357721314998705", CultureInfo.InvariantCulture), sum);
            Assert.That(sum, Is.GreaterThan(TwoPow53));
        }

        // ТЗ §12.4: CargoGoldValue = max(1, ceil(5·S(z)·BaseCoinValueFactor·FinalValue)).
        [TestCase("1", "1051691076887")]
        [TestCase("1/2", "525845538444")]
        [TestCase("2", "2103382153773")]
        public void CoinValue_Zone144_BaseStats(string valueFactor, string expected)
        {
            Assert.AreEqual(BigInteger.Parse(expected, CultureInfo.InvariantCulture), CoinValue(Stats(zone: 144), 144, R(valueFactor)));
        }

        [Test]
        public void CoinValue_Zone144_MaxValueAndLegendaryHat_MatchesAudit()
        {
            // controls.maximum_coin_value_zone144_at_value_cap_and_legendary_hat_L20: s-порция при Value=40 (cap профиля b4)
            // и легендарной шляпе 20: ceil(5 · 1.2^143 · 2 · 1.1^40 · 8 · 2.17).
            Assert.AreEqual(40, Balance.Branches[(int)UpgradeBranch.Value].Cap);
            var p = NewProfile(1);
            Wear(p, "hat.legendary", 20);
            var stats = Stats(LevelsWith(UpgradeBranch.Value, 40), p, zone: 144);
            var expected = AuditBig(Audit["controls"]["maximum_coin_value_zone144_at_value_cap_and_legendary_hat_L20"]);
            Assert.AreEqual(BigInteger.Parse("1652628781501101", CultureInfo.InvariantCulture), expected);
            Assert.AreEqual(ExpectedCoinValue(144, FracA.Of(2), FracA.Of(11, 10).Pow(40) * FracA.Of(8) * FracA.Of(217, 100)), expected, "самопроверка эталона");
            Assert.AreEqual(expected, CoinValue(stats, 144, R("2")));
            Assert.IsFalse((bool)Audit["controls"]["maximum_coin_exceeds_2_pow_53"]);
            // сумма шести таких порций уже выше 2^53 и должна сохранить единицу
            var six = CoinValue(stats, 144, R("2")) * 6;
            Assert.AreEqual(BigInteger.Parse("9915772689006606", CultureInfo.InvariantCulture), six);
            Assert.That(six, Is.GreaterThan(TwoPow53));
        }

        [Test]
        public void Zone144_MaxStageGold_SumIsExactAbove2Pow53()
        {
            // controls.maximum_stage_gold_upper_bound = Σ units · coin(z144, vf) при Value=40 (cap профиля b4) и легендарной шляпе 20;
            // максимум по всем шахтам достигается в zone144.
            var p = NewProfile(1);
            Wear(p, "hat.legendary", 20);
            var stats = Stats(LevelsWith(UpgradeBranch.Value, 40), p, zone: 144);
            BigInteger total = 0;
            foreach (var b in Balances(144)) total += CoinValue(stats, 144, b.ValueFactor) * b.Units;
            var expected = AuditBig(Audit["controls"]["maximum_stage_gold_upper_bound"]);
            Assert.AreEqual("zone144", (string)Audit["controls"]["maximum_stage_gold_upper_bound_zone"]);
            // ТЗ 2.2 §4.2, §16.1: adapter-2 — у цепей заводов груза нет; значение — controls.maximum_stage_gold_upper_bound
            // нового эталона (прежнее 13206982907366054057 — в outpost_v2_audit_2.1.json).
            Assert.AreEqual(BigInteger.Parse("7421129543330699456", CultureInfo.InvariantCulture), expected);
            Assert.AreEqual(expected, total);
            Assert.IsTrue((bool)Audit["controls"]["maximum_stage_gold_upper_bound_exceeds_2_pow_53"]);
            Assert.That(total, Is.GreaterThan(TwoPow53));
        }

        // ───────────────────────────── ТЗ §12.5: контрольная таблица ─────────────────────────────

        // «Стоимость первой Кирки — 20 … следующая цена — 24»; «Цена кирок в zone1 при L=9 / L=10 — 89 / 105».
        [TestCase(0, 20)]
        [TestCase(1, 24)]
        [TestCase(9, 89)]
        [TestCase(10, 105)]
        public void Control_PickaxeCost_Zone1(int level, int expected)
        {
            var audit = ((JArray)Audit["controls"]["pickaxe_costs_zone1_L0_L1_L9_L10"]).Select(t => (int)t).ToArray();
            CollectionAssert.AreEqual(new[] { 20, 24, 89, 105 }, audit, "самопроверка аудита");
            Assert.AreEqual(new BigInteger(expected), NextCost(1, UpgradeBranch.Pickaxes, level).Value);
        }

        // ТЗ §9: «Покупка выше максимума невозможна»; CORE_SPEC §6: «на cap — нет цены».
        [TestCase(UpgradeBranch.Team, 10)]
        [TestCase(UpgradeBranch.Pickaxes, 20)]
        [TestCase(UpgradeBranch.Boots, 15)]
        [TestCase(UpgradeBranch.Bags, 16)]
        [TestCase(UpgradeBranch.Unload, 20)]
        [TestCase(UpgradeBranch.Value, 40)]
        public void Control_Caps_AndNoPriceAtCap(UpgradeBranch branch, int cap)
        {
            Assert.AreEqual(cap, Balance.Branches[(int)branch].Cap, "cap ветки " + branch);
            Assert.IsTrue(NextCost(1, branch, cap - 1).HasValue, "последний уровень до cap ещё продаётся");
            Assert.IsFalse(NextCost(1, branch, cap).HasValue, "на cap цены нет");
        }

        // ТЗ §12.5: «Без экипировки на шахте 1: два котика, урон 5, скорость 2.5, сумка 4, разгрузка 2 порции/с».
        [Test]
        public void Control_StartingStats()
        {
            var s = Stats();
            Assert.AreEqual(2, s.Workers);
            AssertExact(s.FinalDamage, FracA.Of(5), "урон");
            Assert.AreEqual(5_000_000L, s.DamageUnits);
            AssertExact(s.FinalMoveSpeed, FracA.Of(5, 2), "скорость");
            Assert.AreEqual(400_000L, s.EdgeTicks, "ребро = ceil(10^6 / 2.5)");
            Assert.AreEqual(4, s.Capacity);
            AssertExact(s.UnloadRate, FracA.Of(2), "разгрузка");
            Assert.AreEqual(500_000L, s.UnloadTicks, "порция = ceil(10^6 / 2)");
            AssertExact(s.FinalValue, FracA.Of(1), "оценка");
        }

        // ТЗ §12.5: «После покупки: урон 6.1».
        [Test]
        public void Control_Damage_L0_Is5_L1_Is6point1()
        {
            var l0 = Stats(LevelsWith(UpgradeBranch.Pickaxes, 0));
            var l1 = Stats(LevelsWith(UpgradeBranch.Pickaxes, 1));
            AssertExact(l0.FinalDamage, FracA.Of(5));
            AssertExact(l1.FinalDamage, FracA.Of(61, 10));
            Assert.AreEqual(6_100_000L, l1.DamageUnits);
        }

        // ТЗ §12.5: «Множитель кирок L=9 / 10 / 19 / 20 — 1 / 2 / 2 / 4».
        [TestCase(9, 1)]
        [TestCase(10, 2)]
        [TestCase(19, 2)]
        [TestCase(20, 4)]
        public void Control_PickaxeMilestone(int level, int multiplier)
        {
            var s = Stats(LevelsWith(UpgradeBranch.Pickaxes, level));
            var expected = FracA.Of(5) * FracA.Of(61, 50).Pow(level) * multiplier;
            AssertExact(s.LocalDamage, expected, "LocalDamage L" + level);
            AssertExact(s.FinalDamage, expected, "без экипировки Final = Local");
        }

        // ТЗ §12.5 с порогами профиля b4 (10/25/40, cap 40): «Множитель оценки L=9 / 10 / 24 / 25 / 39 / 40 — 1 / 2 / 2 / 4 / 4 / 8».
        [TestCase(9, 1)]
        [TestCase(10, 2)]
        [TestCase(24, 2)]
        [TestCase(25, 4)]
        [TestCase(39, 4)]
        [TestCase(40, 8)]
        public void Control_ValueMilestone(int level, int multiplier)
        {
            var audit = ((JArray)Audit["controls"]["value_milestone_L9_L10_L24_L25_L39_L40"]).Select(t => (int)t).ToArray();
            CollectionAssert.AreEqual(new[] { 1, 2, 2, 4, 4, 8 }, audit, "самопроверка аудита");
            var s = Stats(LevelsWith(UpgradeBranch.Value, level));
            AssertExact(s.LocalValue, FracA.Of(11, 10).Pow(level) * multiplier, "LocalValue L" + level);
        }

        [Test]
        public void Control_PickaxeL20_DamageUnits_RoundHalfUp()
        {
            // 5 · 1.22^20 · 4 ≈ 1067.1528023 (controls.corrected_pickaxe_L20_damage_approx) → 1 067 152 802 долей
            var s = Stats(LevelsWith(UpgradeBranch.Pickaxes, 20));
            Assert.AreEqual(1_067_152_802L, s.DamageUnits);
            Assert.AreEqual((double)Audit["controls"]["corrected_pickaxe_L20_damage_approx"], s.DamageUnits / 1e6, 1e-6);
        }

        // ТЗ §12.5: «x, zone1, площадь 2, rank01=0.5 — 80 HP, 8 порций по 3 монеты при нулевых бонусах».
        [Test]
        public void Control_X_Zone1_Area2_Rank05()
        {
            Assert.AreEqual(80, (int)Audit["controls"]["zone1_rank05_x_area2_hp"], "самопроверка аудита");
            var area2 = Balances(1).Where(b => b.Target.Kind == 'x' && b.Target.Cells.Length == 2).ToList();
            CollectionAssert.AreEquivalent(new[] { 18, 29, 78, 82 }, area2.Select(b => b.Target.Id), "x площади 2 в zone1");
            foreach (var b in area2)
            {
                AssertExact(b.Rank01, FracA.Of(1, 2), "rank01 x");
                Assert.AreEqual(80L, b.MaxHp, "HP x#" + b.Target.Id);
                Assert.AreEqual(8, b.Units, "порции x#" + b.Target.Id);
                Assert.AreEqual(new BigInteger(3), CoinValue(Stats(), 1, b.ValueFactor), "монет за порцию x#" + b.Target.Id);
            }
        }

        // ТЗ §12.5: «Обычный объект r площадью 1 и rank01 = 0 на шахте 1 имеет 20 HP, две порции по 5 монет … Полная сдача приносит 10».
        [Test]
        public void Control_R_Zone1_Rank0_Area1()
        {
            var gold = ((JArray)Audit["controls"]["zone1_rank0_r_hp_units_gold"]).Select(t => (int)t).ToArray();
            CollectionAssert.AreEqual(new[] { 20, 2, 10 }, gold, "самопроверка аудита");
            // zone1 #16 — rockonboardingmine1 уровня 1: наименьший исходный HP зоны (16) → rank01 = 0
            var b = Balances(1).Single(x => x.Target.Id == 16);
            Assert.AreEqual('r', b.Target.Kind);
            AssertExact(b.Rank01, FracA.Of(0));
            Assert.AreEqual(20L, b.MaxHp);
            Assert.AreEqual(2, b.Units);
            var coin = CoinValue(Stats(), 1, b.ValueFactor);
            Assert.AreEqual(new BigInteger(5), coin);
            Assert.AreEqual(new BigInteger(10), coin * b.Units);
        }

        // ТЗ §12.4: «цель zone13 с индексом 78 должна иметь 318 HP, а округлённая промежуточная дробь может дать 319» — при шаге 1/200.
        // Профиль b4 (шаг 1/25): ворота rank 1 → 20 · (1 + 12/25) · 6 · 5/2 = 444 ровно; наивный double даёт 444.00000000000006 → 445.
        [Test]
        public void Control_Zone13_Index78_Is444()
        {
            Assert.AreEqual(444, (int)Audit["controls"]["zone13_grid78_hp"]);
            Assert.AreEqual(444L, ExpectedMaxHp(13, FracA.Of(1), 1, 'p'), "самопроверка эталона");
            var b = Balances(13).Single(x => x.Target.Id == 78);
            Assert.AreEqual('p', b.Target.Kind);
            AssertExact(b.Rank01, FracA.Of(1));
            Assert.AreEqual(444L, b.MaxHp);
        }

        // ТЗ §12.5: «Легендарная шляпа/кирка/обувь уровня 20 — +117%, итоговый множитель предмета 2.17».
        [Test]
        public void Control_LegendaryL20_Bonus117Percent()
        {
            Assert.AreEqual("117", (string)Audit["controls"]["legendary_L20_bonus_percent"]);
            var p = NewProfile(1);
            Wear(p, "pickaxe.legendary", 20);
            Wear(p, "hat.legendary", 20);
            Wear(p, "boots.legendary", 20);
            var s = Stats(null, p);
            AssertExact(s.FinalDamage, FracA.Of(5) * FracA.Of(217, 100), "кирка");
            AssertExact(s.FinalValue, FracA.Of(217, 100), "шляпа");
            AssertExact(s.FinalMoveSpeed, FracA.Of(5, 2) * FracA.Of(217, 100), "обувь");
            Assert.AreEqual(184_332L, s.EdgeTicks, "ceil(10^6 / (2.5 · 2.17))");
            Assert.AreEqual(10_850_000L, s.DamageUnits);
            AssertExact(Catalog.ItemBonus("pickaxe.legendary", 20), FracA.Of(117, 100), "ItemBonus");
        }

        // ТЗ §12.5: «Обычная кирка уровня 1 даёт +8%: стартовый урон 5.4 … Обычный рюкзак уровня 1 — сумка с 4 до 5».
        [Test]
        public void Control_CommonPickaxeAndBackpackL1()
        {
            var p = NewProfile(1);
            Wear(p, "pickaxe.common", 1);
            Wear(p, "backpack.common", 1);
            var s = Stats(null, p);
            AssertExact(s.FinalDamage, FracA.Of(27, 5));
            Assert.AreEqual(5_400_000L, s.DamageUnits);
            Assert.AreEqual(5, s.Capacity);
            // с локальным уровнем 1: 6.1 · 1.08 = 6.588
            Assert.AreEqual(6_588_000L, Stats(LevelsWith(UpgradeBranch.Pickaxes, 1), p).DamageUnits);
        }

        // ТЗ §10.3: «обычный рюкзак уровня 20 даёт +20 порций, легендарный уровня 1 — +10».
        [TestCase("backpack.common", 20, 20)]
        [TestCase("backpack.legendary", 1, 10)]
        [TestCase("backpack.rare", 1, 3)]
        [TestCase("backpack.epic", 5, 10)]
        public void Control_BackpackExtra(string itemId, int level, int extra)
        {
            if (itemId == "backpack.common" && level == 20)
                Assert.AreEqual(20, (int)Audit["controls"]["common_L20_backpack_extra"]);
            var p = NewProfile(1);
            Wear(p, itemId, level);
            Assert.AreEqual(4 + extra, Stats(null, p).Capacity);
        }

        // ТЗ §12.5: «Детали за zone1 / zone4 / zone5 / zone10 / zone144 — 10 / 10 / 15 / 20 / 150».
        [TestCase(1, 10)]
        [TestCase(4, 10)]
        [TestCase(5, 15)]
        [TestCase(10, 20)]
        [TestCase(144, 150)]
        public void Control_CompletionParts(int zone, int parts)
        {
            var audit = ((JArray)Audit["controls"]["completion_parts_z1_z4_z5_z10_z144"]).Select(t => (int)t).ToArray();
            CollectionAssert.AreEqual(new[] { 10, 10, 15, 20, 150 }, audit);
            Assert.AreEqual(parts, Rewards.CompletionParts(zone));
        }

        [Test]
        public void Control_CompletionParts_CampaignTotal()
        {
            int sum = 0;
            for (int z = 1; z <= 144; z++) sum += Rewards.CompletionParts(z);
            Assert.AreEqual((int)Audit["controls"]["guaranteed_completion_parts_total_campaign"], sum);
            Assert.AreEqual(11590, sum);
        }

        // ТЗ §12.5: «Полная стоимость улучшения предмета 1→20 по редкостям — 673 / 1001 / 1665 / 2655».
        [TestCase(0, 673)]
        [TestCase(1, 1001)]
        [TestCase(2, 1665)]
        [TestCase(3, 2655)]
        public void Control_FullItemUpgradeCost(int rarity, int total)
        {
            Assert.AreEqual(total, (int)((JArray)Audit["controls"]["item_upgrade_parts_total_1_to_20"])[rarity]);
            for (int slot = 0; slot < 4; slot++)
            {
                string id = ItemId(slot, rarity);
                long sum = 0;
                for (int q = 1; q < 20; q++) sum += Catalog.UpgradeCost(id, q);
                Assert.AreEqual(total, sum, id);
            }
        }

        // ТЗ §9 / §12.2: эффекты веток на пределах. Профиль b8 (первая сессия): у «Обуви» и «Разгрузки» на cap пройдены пороги 5 и 10 —
        // ещё ×(6/5)², у «Сумок» — +1 место за каждый из двух порогов; «Места разгрузки» на cap 2 — четыре места у склада.
        [Test]
        public void Control_BranchEffectsAtCap()
        {
            Assert.AreEqual(12, Stats(LevelsWith(UpgradeBranch.Team, 10)).Workers, "Команда 10 → 12 котиков");
            var milestones = FracA.Of(6, 5).Pow(2);
            var boots = Stats(LevelsWith(UpgradeBranch.Boots, 15));
            AssertExact(boots.FinalMoveSpeed, FracA.Of(11, 2) * milestones, "2.5 · (1 + 0.08·15) · (6/5)²");
            Assert.AreEqual(126_263L, boots.EdgeTicks, "ceil(1e6 / 7.92)");
            Assert.AreEqual(22, Stats(LevelsWith(UpgradeBranch.Bags, 16)).Capacity, "4 + 16 + 2 места порогов");
            var unload = Stats(LevelsWith(UpgradeBranch.Unload, 20));
            AssertExact(unload.UnloadRate, FracA.Of(10) * milestones, "2 · (1 + 0.2·20) · (6/5)², профиль b8");
            Assert.AreEqual(69_445L, unload.UnloadTicks, "ceil(1e6 / 14.4)");
            // Аудит источника посчитан на профиле b6 (без порогов «Разгрузки»): L20 там — 10, L1 порогов не достигает.
            var audit = ((JArray)Audit["controls"]["unload_rate_L1_L20"]).Select(t => (string)t).ToArray();
            CollectionAssert.AreEqual(new[] { "12/5", "10" }, audit, "самопроверка аудита");
            AssertExact(unload.UnloadRate, FracA.Parse(audit[1]) * milestones, "L20 = значение аудита × пороги b8");
            AssertExact(Stats(LevelsWith(UpgradeBranch.Unload, 1)).UnloadRate, FracA.Parse(audit[0]), "2 · (1 + 0.2)");
            var bays = Stats(LevelsWith(UpgradeBranch.Bays, 2));
            Assert.AreEqual(4, bays.DepotSlots, "2 стартовых места + 2 уровня «Мест разгрузки»");
            Assert.AreEqual(OutpostIds.MaxDepotSlots, bays.DepotSlots);
            Assert.AreEqual(OutpostIds.DepotSlotCount, Stats().DepotSlots, "без покупок — два места");
        }

        // ТЗ §12.2: цены всех веток zone1 L0..3 (точный ceil после степени).
        [TestCase(UpgradeBranch.Team, new long[] { 50, 95, 181, 343 })]
        [TestCase(UpgradeBranch.Pickaxes, new long[] { 20, 24, 28, 33 })]
        [TestCase(UpgradeBranch.Boots, new long[] { 25, 33, 43, 55 })]
        [TestCase(UpgradeBranch.Bags, new long[] { 25, 33, 44, 58 })]
        [TestCase(UpgradeBranch.Unload, new long[] { 20, 26, 33, 42 })]
        [TestCase(UpgradeBranch.Value, new long[] { 30, 38, 47, 58 })]
        public void Control_FirstCosts_Zone1(UpgradeBranch branch, long[] costs)
        {
            for (int level = 0; level < costs.Length; level++)
                Assert.AreEqual(new BigInteger(costs[level]), NextCost(1, branch, level).Value, branch + " L" + level);
        }

        // ТЗ §12.1: S(z) = 1.20^(z−1) и профиль b4: CostZoneFactor(z) = 1 + (z−1)/10 масштабируют цену; сверка с независимым
        // эталоном на нескольких зонах и всех уровнях до cap.
        [TestCase(2)]
        [TestCase(13)]
        [TestCase(50)]
        [TestCase(101)]
        [TestCase(144)]
        public void NextCost_MatchesIndependentFormula(int zone)
        {
            var bases = new[] { FracA.Of(50), FracA.Of(20), FracA.Of(25), FracA.Of(25), FracA.Of(20), FracA.Of(30) };
            var growth = new[] { FracA.Of(19, 10), FracA.Of(59, 50), FracA.Of(13, 10), FracA.Of(33, 25), FracA.Of(32, 25), FracA.Of(31, 25) };
            var caps = new[] { 10, 20, 15, 16, 20, 40 };
            for (int b = 0; b < 6; b++)
            {
                for (int level = 0; level < caps[b]; level++)
                    // Профиль b6 (решение владельца В4, 15 октября 2026): цена × K(z) из PaceScale (paced).
                    Assert.AreEqual(ExpectedCost(zone, bases[b], growth[b], level, paced: true), NextCost(zone, (UpgradeBranch)b, level).Value,
                        "zone" + zone + " " + BranchIds[b] + " L" + level);
                Assert.IsFalse(NextCost(zone, (UpgradeBranch)b, caps[b]).HasValue, "zone" + zone + " " + BranchIds[b] + ": на cap цены нет");
            }
        }
    }
}
