using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// ТЗ 17.1, группа 11 «Большие числа» (шахта sunstone) и ТЗ 15.1.
    /// Дополняет E11_BigCostsStayFinite (там — сырая формула на 1042/1043 и round-trip строки цены 1199):
    /// здесь цены и «До порога» через сессию (единый метод списания), покупки на 1040–1200,
    /// выход со всеми порогами, показ и save/load этих значений.
    /// </summary>
    public class BigNumberAcceptanceTests
    {
        const long S = GameSession.TicksPerSecond;
        const string Sunstone = "sunstone";

        // Данные sunstone из JSON (MineShafts[sunstone]); проверяются в SunstoneZoneShaftAndThresholdTable.
        const double CostBase = 734039166960333000.0;   // UpgradeCostBase, FT2, M = 0
        const double CostGrowth = 1.9;                  // UpgradeCostGrowth
        const double OutputMultiplier = 7.39e35;        // CurrencyOutputMultiplier (ТЗ 15.1: «базовый выход до 7.39e35»)
        const double CycleSeconds = 28800;              // GenerationDelaySecBase
        const int MaxLevel = 1200;                      // ТЗ 9.3, E-04: последний порог и максимум уровня

        /// <summary>«Быстро и без зависаний»: ТЗ числа не даёт, предел щедрый — ловит зацикливание, а не микросекунды.</summary>
        const long MaxMilliseconds = 2000;

        static int s_SunstoneZone;

        /// <summary>Первая зона, где в разметке есть шахта sunstone (перебор раскладок всех зон).</summary>
        static int SunstoneZone
        {
            get
            {
                if (s_SunstoneZone > 0) return s_SunstoneZone;
                var b = TestData.Balance;
                for (int z = 1; z <= b.ZoneCount; z++)
                {
                    var layout = ZoneLayout.Build(b, z);
                    if (layout.Shafts.Any(sh => sh.TypeId == Sunstone))
                    {
                        s_SunstoneZone = z;
                        return z;
                    }
                }
                Assert.Fail("Ни в одной зоне нет шахты sunstone");
                return -1;
            }
        }

        static string Snapshot(GameSession s) => SaveService.SerializeState(s.State);

        /// <summary>Debug-сессия в зоне с sunstone: шахта расчищена и активирована (производит, уровень 1).</summary>
        static GameSession ProducingSunstone(out ZoneObject shaft)
        {
            var s = GameSession.NewGame(TestData.Balance, 12345UL, SaveSlots.Debug, 1_000_000L, SunstoneZone);
            shaft = s.Layout.Shafts.First(x => x.TypeId == Sunstone);
            s.DebugCompleteTarget(shaft.Ordinal);
            Assert.AreEqual(ObjectStatus.ReadyToActivate, s.Mine.Objects[shaft.Ordinal].Status, "шахта расчищена");
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(shaft.Ordinal), "активация sunstone");
            Assert.IsTrue(s.IsGenerator(shaft.Ordinal), "шахта производит");
            Assert.AreEqual(1, s.Mine.Objects[shaft.Ordinal].Gen.Level, "после активации производственный уровень 1");
            s.Events.Clear();
            return s;
        }

        /// <summary>Пороги T_k = 1 + Σ_{i≤k} ObjectiveCount[i] (E-04) по данным JSON.</summary>
        static int[] Thresholds()
        {
            var oc = TestData.Balance.Objectives[Sunstone].ObjectiveCount;
            var t = new int[oc.Length];
            int sum = 1;
            for (int k = 0; k < oc.Length; k++)
            {
                sum += oc[k];
                t[k] = sum;
            }
            return t;
        }

        static int ReachedAt(int level) => Thresholds().Count(t => t <= level);

        static int NextThresholdAfter(int level) => Thresholds().First(t => t > level);

        /// <summary>Прямо выставить уровень генератора и согласованное число достигнутых порогов.</summary>
        static GeneratorState SetLevel(GameSession s, ZoneObject shaft, int level)
        {
            var gen = s.Mine.Objects[shaft.Ordinal].Gen;
            gen.Level = level;
            gen.ThresholdsReached = ReachedAt(level);
            return gen;
        }

        /// <summary>Золото ровно amount: обнуление и инструмент DebugAddGold.</summary>
        static void SetGold(GameSession s, GameNumber amount)
        {
            s.Mine.Gold = GameNumber.Zero;
            s.DebugAddGold(amount);
            Assert.AreEqual(amount, s.Mine.Gold, "золото выставлено");
        }

        static GameNumber Huge => new GameNumber(1.0, 360);

        /// <summary>log10 цены L→L+1 по E-03: ceil(B·G^(L−1)); для значений больше 2^53 ceil — тождество (F-R).</summary>
        static double ExpectedCostLog10(int level) => Math.Log10(CostBase) + (level - 1) * Math.Log10(CostGrowth);

        /// <summary>log10 Σ_{L=from}^{to−1} B·G^(L−1) = log10(B·G^(from−1)·(G^n − 1)/(G − 1)).</summary>
        static double ExpectedSumLog10(int from, int toExclusive)
        {
            int n = toExclusive - from;
            return ExpectedCostLog10(from) + Math.Log10((Math.Pow(CostGrowth, n) - 1.0) / (CostGrowth - 1.0));
        }

        static bool Approx(GameNumber a, GameNumber b, double rel) => GameNumber.ApproximatelyEqual(a, b, rel);

        /// <summary>Большая форма GameNumber, которая ещё помещается в double: 1e300 ≤ |v| &lt; 1.8e308.</summary>
        static bool InDoubleRangeBigForm(GameNumber v) => v.IsBig && v.FitsInDouble;

        static void AssertFinitePositive(GameNumber v, string what)
        {
            Assert.IsTrue(v.IsPositive, what + ": значение положительно");
            double m = v.Mantissa;
            Assert.IsFalse(double.IsNaN(m) || double.IsInfinity(m), what + ": мантисса конечна");
            Assert.That(m, Is.GreaterThanOrEqualTo(1.0).And.LessThan(10.0), what + ": мантисса нормализована");
            double log = v.Log10();
            Assert.IsFalse(double.IsNaN(log) || double.IsInfinity(log), what + ": десятичный порядок конечен");
        }

        // ───────────── Показ (ТЗ 15.1) ─────────────

        static readonly Regex s_Suffixed = new Regex(@"^(?<num>\d{1,3}(\.\d{1,2})?)(?<suf>[KMBT]|[a-z]{2})?$");
        static readonly Regex s_Scientific = new Regex(@"^(?<num>\d(\.\d{1,2})?)e(?<exp>\d+)$");

        /// <summary>Группа 10^(3·g) суффикса по ТЗ 15.1: K, M, B, T, затем aa, ab, … (по алфавиту).</summary>
        static int SuffixGroup(string suffix)
        {
            switch (suffix)
            {
                case "": return 0;
                case "K": return 1;
                case "M": return 2;
                case "B": return 3;
                case "T": return 4;
                default: return 5 + (suffix[0] - 'a') * 26 + (suffix[1] - 'a');
            }
        }

        static int SignificantDigits(string num) => num.Replace(".", "").TrimStart('0').Length;

        /// <summary>Показ без NaN/∞, три значащие цифры, суффикс или научная запись, соответствует значению.</summary>
        static void AssertDisplay(GameNumber v, string what)
        {
            string text = NumberFormat.Format(v);
            Assert.IsFalse(string.IsNullOrEmpty(text), what + ": пустой показ");
            foreach (var bad in new[] { "NaN", "Infinity", "∞" })
                Assert.IsFalse(text.Contains(bad), what + ": показ '" + text + "' содержит " + bad);
            string num;
            double shownLog;
            var m = s_Suffixed.Match(text);
            if (m.Success)
            {
                num = m.Groups["num"].Value;
                shownLog = Math.Log10(double.Parse(num, CultureInfo.InvariantCulture)) + 3.0 * SuffixGroup(m.Groups["suf"].Value);
            }
            else
            {
                m = s_Scientific.Match(text);
                Assert.IsTrue(m.Success, what + ": показ '" + text + "' — ни суффикс, ни научная запись");
                num = m.Groups["num"].Value;
                shownLog = Math.Log10(double.Parse(num, CultureInfo.InvariantCulture)) + long.Parse(m.Groups["exp"].Value, CultureInfo.InvariantCulture);
            }
            Assert.LessOrEqual(SignificantDigits(num), 3, what + ": не больше трёх значащих цифр в '" + text + "'");
            // Три значащие цифры: показ отличается от значения меньше чем на 1 % (|Δlog10| < 0.005).
            Assert.AreEqual(v.Log10(), shownLog, 0.005, what + ": показ '" + text + "' не соответствует значению " + v.ToStorageString());
        }

        /// <summary>Набор больших значений группы 11: цены +1 на 1040–1199, «До порога» по отрезкам, выход со всеми порогами.</summary>
        static List<KeyValuePair<string, GameNumber>> BigValues(GameSession s, ZoneObject shaft)
        {
            int ord = shaft.Ordinal;
            var list = new List<KeyValuePair<string, GameNumber>>();
            for (int L = 1040; L < MaxLevel; L++)
                list.Add(new KeyValuePair<string, GameNumber>("цена " + L + "→" + (L + 1), s.UpgradeCost(ord, L)));
            foreach (int start in new[] { 1040, 1043, 1080, 1120, 1160, 1199 })
            {
                SetLevel(s, shaft, start);
                SetGold(s, Huge);
                s.PreviewToThreshold(ord, out int levels, out var total);
                Assert.Greater(levels, 0, "«До порога» от " + start);
                list.Add(new KeyValuePair<string, GameNumber>("«До порога» от " + start, total));
            }
            SetLevel(s, shaft, MaxLevel);
            list.Add(new KeyValuePair<string, GameNumber>("выход L1200 со всеми порогами", s.GeneratorOutput(ord)));
            list.Add(new KeyValuePair<string, GameNumber>("золото 1e360", Huge));
            return list;
        }

        // ───────────── Данные и зона ─────────────

        /// <summary>
        /// Зона с sunstone находится перебором раскладок; таблица порогов: 20, 40, 80 … 1200 (ТЗ 9.3, E-04),
        /// произведение множителей 1e154 (ТЗ 15.1). Константы sunstone совпадают с JSON.
        /// </summary>
        [Test]
        public void SunstoneZoneShaftAndThresholdTable()
        {
            var b = TestData.Balance;
            Assert.AreEqual(134, SunstoneZone, "JSON: первый токен s:sunstone — в zone134");
            var data = b.Shafts[Sunstone];
            Assert.AreEqual(CostBase, data.UpgradeCostBase, "JSON UpgradeCostBase");
            Assert.AreEqual(CostGrowth, data.UpgradeCostGrowth, "JSON UpgradeCostGrowth");
            Assert.AreEqual(0.0, data.UpgradeCostMultiplier, "валидатор C.1: M = 0 у цены улучшения");
            Assert.AreEqual(2, data.UpgradeCostFormulaType, "цена улучшения — F-2");
            Assert.AreEqual(OutputMultiplier, data.CurrencyOutputMultiplier, "JSON CurrencyOutputMultiplier");
            Assert.AreEqual(CycleSeconds, data.GenerationDelaySecBase, "JSON GenerationDelaySecBase");

            var t = Thresholds();
            Assert.AreEqual(new[] { 20, 40, 80 }, t.Take(3).ToArray(), "ТЗ 9.3: у sunstone первый элемент 19 → пороги 20, 40, 80…");
            Assert.AreEqual(MaxLevel, t[t.Length - 1], "E-04: последний порог — 1200");
            CollectionAssert.IsSubsetOf(new[] { 1040, 1080, 1120, 1160, 1200 }, t, "пороги на отрезке 1040–1200");
            var product = GameNumber.One;
            foreach (var mult in b.Objectives[Sunstone].CoreCurrencyMultiplier) product *= GameNumber.FromDouble(mult);
            Assert.IsTrue(Approx(GameNumber.Pow(10, 154), product, 1e-12), "ТЗ 15.1: произведение пороговых множителей до 1e154, у sunstone " + product);

            var s = ProducingSunstone(out var shaft);
            Assert.AreEqual(MaxLevel, s.MaxGeneratorLevel(shaft.Ordinal));
            Assert.AreEqual(20, s.NextThresholdLevel(shaft.Ordinal));
        }

        // ───────────── Цена +1 ─────────────

        /// <summary>Цена +1 на уровнях 1040–1199 через сессию: E-03, конечна, растёт ×1.9; граница double ровно 1042/1043; максимум ~1e352.</summary>
        [Test]
        public void PlusOnePriceOnLevels1040To1199FollowsE03AndStaysFinite()
        {
            var s = ProducingSunstone(out var shaft);
            int ord = shaft.Ordinal;
            var sw = Stopwatch.StartNew();
            var prev = GameNumber.Zero;
            for (int L = 1040; L < MaxLevel; L++)
            {
                var c = s.UpgradeCost(ord, L);
                string what = "цена " + L + "→" + (L + 1);
                AssertFinitePositive(c, what);
                Assert.AreEqual(ExpectedCostLog10(L), c.Log10(), 1e-9, what + ": E-03 ceil(B·1.9^(L−1))");
                Assert.AreEqual(L <= 1042, c.FitsInDouble, what + ": ТЗ 15.1 — sunstone выходит за double на переходах начиная с 1043");
                if (L > 1040) Assert.IsTrue(c > prev, what + ": цена растёт");
                prev = c;
            }
            sw.Stop();
            Assert.AreEqual(352.0, Math.Round(prev.Log10()), "ТЗ 15.1: цена улучшения до уровня 1200 достигает ~1e352, получено " + prev);
            Assert.Less(sw.ElapsedMilliseconds, MaxMilliseconds, "цены 1040–1199 считаются быстро");
        }

        /// <summary>
        /// Контроль ТЗ 17.1 п. 11 на цене сессии (единый метод проверки и списания, ТЗ 15.1):
        /// 1042→1043 помещается в double, 1043→1044 — нет; обе конечны в GameNumber и обе покупаются.
        /// </summary>
        [Test]
        public void ControlPrice1042FitsDouble1043DoesNotBothFiniteAndPayable()
        {
            var s = ProducingSunstone(out var shaft);
            int ord = shaft.Ordinal;
            var c1042 = s.UpgradeCost(ord, 1042);
            var c1043 = s.UpgradeCost(ord, 1043);
            var c1044 = s.UpgradeCost(ord, 1044);
            var doubleMax = GameNumber.FromDouble(double.MaxValue);

            Assert.IsTrue(c1042.FitsInDouble, "1042→1043 ещё помещается в double");
            double d1042 = c1042.ToDouble();
            Assert.IsFalse(double.IsNaN(d1042) || double.IsInfinity(d1042));
            Assert.Greater(d1042, 1e308, "1042→1043 ≈ 1.12e308 — у самой границы");
            Assert.IsTrue(c1042 < doubleMax);

            Assert.IsFalse(c1043.FitsInDouble, "1043→1044 уже не помещается в double");
            Assert.IsTrue(double.IsPositiveInfinity(c1043.ToDouble()));
            Assert.IsTrue(c1043 > doubleMax, "в GameNumber цена 1043→1044 больше double.MaxValue и сравнима");

            AssertFinitePositive(c1042, "1042→1043");
            AssertFinitePositive(c1043, "1043→1044");
            Assert.AreEqual(308L, c1042.Exponent);
            Assert.AreEqual(308L, c1043.Exponent);
            Assert.AreEqual(CostGrowth, (c1043 / c1042).ToDouble(), 1e-12, "соседние цены отличаются в 1.9 раза");

            // Обе цены списываются при золоте за пределами double; остаток — половина следующей цены.
            var gen = SetLevel(s, shaft, 1042);
            SetGold(s, c1042 + c1043 + c1044 * GameNumber.FromDouble(0.5));
            Assert.IsFalse(s.Mine.Gold.FitsInDouble);
            var before = s.Mine.Gold;
            Assert.AreEqual(CommandResult.Ok, s.UpgradeGenerator(ord, out int bought), "+1 1042→1043");
            Assert.AreEqual(1, bought);
            Assert.AreEqual(before - c1042, s.Mine.Gold, "списана ровно цена 1042→1043");
            before = s.Mine.Gold;
            Assert.AreEqual(CommandResult.Ok, s.UpgradeGenerator(ord, out bought), "+1 1043→1044");
            Assert.AreEqual(1, bought);
            Assert.AreEqual(1044, gen.Level);
            Assert.AreEqual(before - c1043, s.Mine.Gold, "списана ровно цена 1043→1044");
            Assert.IsTrue(Approx(c1044 * GameNumber.FromDouble(0.5), s.Mine.Gold, 1e-9), "остаток золота " + s.Mine.Gold);
            AssertFinitePositive(s.Mine.Gold, "остаток золота");
        }

        /// <summary>
        /// +1 покупается на каждом уровне 1040→1200: списывается ровно цена, пороги 1080/1120/1160/1200 засчитываются,
        /// эликсир по E-04; на 1200 улучшения недоступны и последний бонус не повторяется (ТЗ 9.3).
        /// </summary>
        [Test]
        public void PlusOneBuysEveryLevelFrom1040To1200WithoutHang()
        {
            var s = ProducingSunstone(out var shaft);
            int ord = shaft.Ordinal;
            var gen = SetLevel(s, shaft, 1040);
            Assert.AreEqual(27, gen.ThresholdsReached, "до 1040 включительно пройдено 27 порогов: 20, 40, 80 … 1040");
            SetGold(s, Huge);
            var elixirBefore = s.Player.Elixir;
            var spent = GameNumber.Zero;
            var sw = Stopwatch.StartNew();
            for (int L = 1040; L < MaxLevel; L++)
            {
                var before = s.Mine.Gold;
                var cost = s.UpgradeCost(ord, gen.Level);
                Assert.AreEqual(CommandResult.Ok, s.UpgradeGenerator(ord, out int bought), "+1 на уровне " + L);
                Assert.AreEqual(1, bought, "+1 покупает один уровень, L=" + L);
                Assert.AreEqual(L + 1, gen.Level);
                Assert.AreEqual(before - cost, s.Mine.Gold, "списана ровно цена +1, L=" + L);
                AssertFinitePositive(s.Mine.Gold, "золото после +1 на " + L);
                Assert.AreEqual(ReachedAt(L + 1), gen.ThresholdsReached, "порог засчитан ровно на T_k, уровень " + (L + 1));
                spent += cost;
            }
            sw.Stop();
            AssertFinitePositive(spent, "сумма цен 1040→1200");
            Assert.AreEqual(ExpectedSumLog10(1040, MaxLevel), spent.Log10(), 1e-9, "Σ B·1.9^(L−1), L = 1040…1199");
            Assert.AreEqual(Thresholds().Length, gen.ThresholdsReached, "на 1200 пройдены все 31 порог");
            // E-04: эликсир за пороги k = 28…31 при GenObjectiveSoftCurrencyMultiplier zone134 = 5 (JSON):
            // floor((1 + 50k) × 5) = 7005 + 7255 + 7505 + 7755.
            Assert.AreEqual(5.0, TestData.Balance.ZoneMultipliers(SunstoneZone).GenObjectiveSoftCurrencyMultiplier);
            Assert.AreEqual(29520.0, (s.Player.Elixir - elixirBefore).ToDouble(), 1e-9, "эликсир за пороги 1080–1200");

            // Уровень 1200: кнопки скрыты — ни +1, ни «До порога»; состояние не меняется.
            s.Events.Clear();
            string snap = Snapshot(s);
            Assert.AreEqual(CommandResult.MaxLevel, s.UpgradeGenerator(ord, out int none));
            Assert.AreEqual(0, none);
            s.PreviewToThreshold(ord, out int levels, out var total);
            Assert.AreEqual(0, levels, "«До порога» на 1200 — 0 уровней");
            Assert.IsTrue(total.IsZero);
            Assert.AreEqual(CommandResult.MaxLevel, s.UpgradeToThreshold(ord, out none));
            Assert.AreEqual(0, none);
            Assert.AreEqual(MaxLevel, s.NextThresholdLevel(ord), "за пределы массива порогов не читаем");
            s.Events.Clear();
            Assert.AreEqual(snap, Snapshot(s), "на 1200 последний бонус не повторяется, золото не списывается");
            Assert.Less(sw.ElapsedMilliseconds, MaxMilliseconds, "160 покупок +1 выполняются быстро");
        }

        // ───────────── «До порога» ─────────────

        /// <summary>
        /// «До порога» на 1040–1200 (ТЗ 9.3): число уровней до ближайшего порога включительно, точная сумма по шагам
        /// (не «цена первого × количество»), покупка ровно этих уровней со списанием суммы.
        /// </summary>
        [Test]
        public void ToThresholdPreviewAndBuyOnLevels1040To1200()
        {
            var s = ProducingSunstone(out var shaft);
            int ord = shaft.Ordinal;
            var sw = Stopwatch.StartNew();
            foreach (int start in new[] { 1040, 1041, 1043, 1079, 1080, 1120, 1159, 1160, 1199 })
            {
                var gen = SetLevel(s, shaft, start);
                int target = NextThresholdAfter(start);
                Assert.AreEqual(target, s.NextThresholdLevel(ord), "ближайший порог от " + start);
                var stepSum = GameNumber.Zero;
                for (int L = start; L < target; L++) stepSum += s.UpgradeCost(ord, L);
                // Золота вдвое больше суммы: хватает на весь отрезок, разность сохраняет точность.
                SetGold(s, stepSum * GameNumber.FromDouble(2.0));

                s.PreviewToThreshold(ord, out int levels, out var total);
                string what = "«До порога» " + start + "→" + target;
                Assert.AreEqual(target - start, levels, what + ": число уровней");
                AssertFinitePositive(total, what);
                Assert.AreEqual(ExpectedSumLog10(start, target), total.Log10(), 1e-9, what + ": сумма геометрического ряда");
                Assert.IsTrue(Approx(stepSum, total, 1e-12), what + ": точная сумма по шагам " + total + " ≠ " + stepSum);
                if (levels > 1)
                    Assert.IsTrue(total > s.UpgradeCost(ord, start) * GameNumber.FromDouble(levels), what + ": не «цена первого × количество»");

                var goldBefore = s.Mine.Gold;
                Assert.AreEqual(CommandResult.Ok, s.UpgradeToThreshold(ord, out int bought), what);
                Assert.AreEqual(levels, bought, what + ": куплено столько, сколько показано");
                Assert.AreEqual(target, gen.Level, what + ": уровень после покупки");
                Assert.AreEqual(ReachedAt(target), gen.ThresholdsReached, what + ": порог засчитан");
                AssertFinitePositive(s.Mine.Gold, what + ": остаток золота");
                Assert.IsTrue(Approx(goldBefore - s.Mine.Gold, total, 1e-9), what + ": списана показанная сумма");
            }
            sw.Stop();
            Assert.Less(sw.ElapsedMilliseconds, MaxMilliseconds, "«До порога» на 1040–1200 считается быстро");
        }

        /// <summary>
        /// «До порога» через границу double: золота хватает ровно на 1041→1044 — покупаются 3 уровня;
        /// если не хватает даже на один, кнопка неактивна и состояние не меняется (ТЗ 9.3).
        /// </summary>
        [Test]
        public void ToThresholdBuysOnlyAffordableLevelsAcrossDoubleLimit()
        {
            var s = ProducingSunstone(out var shaft);
            int ord = shaft.Ordinal;
            var gen = SetLevel(s, shaft, 1041);
            Assert.AreEqual(1080, s.NextThresholdLevel(ord));
            var c1041 = s.UpgradeCost(ord, 1041);
            var c1042 = s.UpgradeCost(ord, 1042);
            var c1043 = s.UpgradeCost(ord, 1043);
            var c1044 = s.UpgradeCost(ord, 1044);
            var three = c1041 + c1042 + c1043;
            var half = c1044 * GameNumber.FromDouble(0.5);
            SetGold(s, three + half);
            Assert.IsFalse(s.Mine.Gold.FitsInDouble);

            s.PreviewToThreshold(ord, out int levels, out var total);
            Assert.AreEqual(3, levels, "хватает на 1041→1044, на 1044→1045 — нет");
            Assert.IsTrue(Approx(three, total, 1e-12), "сумма трёх шагов " + total);
            Assert.IsFalse(total.FitsInDouble, "сумма за пределами double");
            Assert.AreEqual(CommandResult.Ok, s.UpgradeToThreshold(ord, out int bought));
            Assert.AreEqual(3, bought);
            Assert.AreEqual(1044, gen.Level);
            Assert.IsTrue(Approx(half, s.Mine.Gold, 1e-9), "остаток " + s.Mine.Gold);

            s.PreviewToThreshold(ord, out levels, out total);
            Assert.AreEqual(0, levels, "не хватает даже на один уровень");
            Assert.IsTrue(total.IsZero);
            s.Events.Clear();
            string snap = Snapshot(s);
            Assert.AreEqual(CommandResult.NotEnoughGold, s.UpgradeToThreshold(ord, out bought));
            Assert.AreEqual(0, bought);
            Assert.AreEqual(CommandResult.NotEnoughGold, s.UpgradeGenerator(ord, out bought));
            Assert.AreEqual(0, bought);
            s.Events.Clear();
            Assert.AreEqual(snap, Snapshot(s), "отказ не меняет состояние");
        }

        // ───────────── Выход с произведением всех порогов ─────────────

        /// <summary>
        /// Выход sunstone на 1200 со всеми порогами (ТЗ 9.4, E-03, 15.1): F-0 = 7.39e35 × 1200, × 1e154, конечен;
        /// ручная выплата через 28800 с и автоматический доход с этим выходом без NaN и ∞.
        /// </summary>
        [Test]
        public void OutputWithProductOfAllThresholdsIsFiniteAndPaysOut()
        {
            var s = ProducingSunstone(out var shaft);
            int ord = shaft.Ordinal;
            var gen = SetLevel(s, shaft, MaxLevel);
            Assert.AreEqual(31, gen.ThresholdsReached);
            var mods = s.ManagerMultiplier(Sunstone) * s.GlobalIncomeMultiplier();
            var all = s.GeneratorOutput(ord);
            gen.ThresholdsReached = 0;
            var noThresholds = s.GeneratorOutput(ord);
            gen.ThresholdsReached = 31;
            AssertFinitePositive(noThresholds, "выход без порогов");
            AssertFinitePositive(all, "выход со всеми порогами");
            Assert.IsTrue(Approx(GameNumber.FromDouble(OutputMultiplier * MaxLevel), noThresholds / mods, 1e-12), "F-0(0, 7.39e35, 0; x = 1200)");
            Assert.IsTrue(Approx(GameNumber.Pow(10, 154), all / noThresholds, 1e-9), "произведение всех порогов 1e154");
            Assert.IsTrue(Approx(GameNumber.FromDouble(8.868e192), all / mods, 1e-9), "7.39e35 × 1200 × 1e154 = 8.868e192, получено " + all / mods);
            Assert.AreEqual(all, s.GeneratorOutput(ord), "выход детерминирован");

            // Ручной буфер: цикл 28800 с, выплата фиксируется по выходу на момент завершения, сбор зачисляет её.
            long cycle = s.GeneratorCycleTicks(ord);
            Assert.AreEqual((long)CycleSeconds * S, cycle);
            Assert.IsFalse(gen.Automated, "ca060 ур. 11 из токена ещё нет");
            s.Advance(cycle);
            Assert.IsTrue(gen.PayoutReady, "выплата готова через 28800 с");
            Assert.AreEqual(all, gen.PendingPayout, "в буфере — выход со всеми порогами");
            var goldBefore = s.Mine.Gold;
            Assert.AreEqual(CommandResult.Ok, s.CollectPayout(ord));
            AssertFinitePositive(s.Mine.Gold, "золото после сбора");
            Assert.IsTrue(Approx(all, s.Mine.Gold - goldBefore, 1e-12), "сбор зачисляет выплату");

            // Автоматизация: доход в секунду и ставка наград (E-10) с этим выходом конечны.
            s.DebugGiveZoneCards();
            Assert.IsTrue(gen.Automated, "карта из токена включает автоматизацию");
            var allAuto = s.GeneratorOutput(ord);
            var modsAuto = s.ManagerMultiplier(Sunstone) * s.GlobalIncomeMultiplier();
            AssertFinitePositive(allAuto, "выход автоматизированной шахты");
            Assert.IsTrue(Approx(GameNumber.FromDouble(8.868e192), allAuto / modsAuto, 1e-9));
            var rate = s.AutoIncomePerSecond();
            AssertFinitePositive(rate, "автоматический доход в секунду");
            Assert.IsTrue(Approx(allAuto / GameNumber.FromDouble(CycleSeconds), rate, 1e-9), "доход ≈ выход sunstone / 28800 (кузница пренебрежимо мала)");
            Assert.AreEqual(rate, s.GetRewardIncomeRate(), "E-10: ставка наград = автоматический доход (больше 1.5)");
            goldBefore = s.Mine.Gold;
            s.Advance(cycle);
            AssertFinitePositive(s.Mine.Gold, "золото после автоматического цикла");
            Assert.IsTrue(Approx(allAuto, s.Mine.Gold - goldBefore, 1e-9), "за цикл зачислен выход sunstone");
            Assert.IsFalse(gen.PayoutReady, "автоматизированная шахта не копит буфер");
        }

        /// <summary>
        /// Выход sunstone при всех порогах и максимальных модификаторах v1 (E-07, E-08): ca060 ур. 14 (×1 594 323),
        /// ca034 ур. 5 (×3^4), двое ворот zone134 (×2^2) — конечен и равен произведению.
        /// </summary>
        [Test]
        public void OutputWithAllThresholdsAndMaximumModifiersIsFinite()
        {
            var s = ProducingSunstone(out var shaft);
            foreach (var gate in s.Layout.Gates.OrderBy(g => g.GateOrder))
            {
                s.DebugCompleteTarget(gate.Ordinal);
                Assert.AreEqual(CommandResult.Ok, s.OpenGate(gate.Ordinal), "ворота " + gate.GateOrder);
            }
            Assert.AreEqual(2, s.Mine.GatesOpened, "JSON: в zone134 двое ворот");
            Assert.AreEqual(14, TestData.Balance.CardMaxLevel(Rarity.Common));
            Assert.AreEqual(5, TestData.Balance.CardMaxLevel(Rarity.Legendary));
            s.CardStateOf("ca060").Level = TestData.Balance.CardMaxLevel(Rarity.Common);
            s.CardStateOf("ca034").Level = TestData.Balance.CardMaxLevel(Rarity.Legendary);
            SetLevel(s, shaft, MaxLevel);

            var output = s.GeneratorOutput(shaft.Ordinal);
            AssertFinitePositive(output, "максимальный выход sunstone");
            var expected = GameNumber.FromDouble(OutputMultiplier * MaxLevel) * GameNumber.Pow(10, 154)
                           * GameNumber.FromDouble(1594323) * GameNumber.FromDouble(81) * GameNumber.FromDouble(4);
            Assert.IsTrue(Approx(expected, output, 1e-9), "ожидалось " + expected + ", получено " + output);
            AssertDisplay(output, "максимальный выход");
            Assert.AreEqual(output, GameNumber.Parse(output.ToStorageString()), "round-trip максимального выхода");
        }

        // ───────────── Ещё ТЗ 15.1: девять поздних шахт ─────────────

        /// <summary>
        /// ТЗ 15.1: у 9 поздних шахт (diamond … sunstone) цена улучшения выходит за double на переходах
        /// начиная с уровней от 1043 (sunstone) до 1183 (diamond); у остальных генераторов — нет; максимум ~1e352.
        /// </summary>
        [Test]
        public void LateShaftsLeaveDoubleFrom1043SunstoneTo1183Diamond()
        {
            var b = TestData.Balance;
            var firstOverflow = new Dictionary<string, int>();
            double maxLog = 0;
            var sw = Stopwatch.StartNew();
            foreach (var kv in b.Shafts)
            {
                var sh = kv.Value;
                for (int L = 1; L < MaxLevel; L++)
                {
                    var c = Formula.Price(Formula.Evaluate(sh.UpgradeCostFormulaType, sh.UpgradeCostBase, sh.UpgradeCostMultiplier, sh.UpgradeCostGrowth, L));
                    Assert.IsFalse(double.IsNaN(c.Mantissa) || double.IsInfinity(c.Mantissa), kv.Key + " L" + L);
                    maxLog = Math.Max(maxLog, c.Log10());
                    if (!c.FitsInDouble && !firstOverflow.ContainsKey(kv.Key)) firstOverflow[kv.Key] = L;
                    if (firstOverflow.ContainsKey(kv.Key)) Assert.IsFalse(c.FitsInDouble, kv.Key + ": после выхода за double цена не возвращается в него, L" + L);
                }
            }
            var cart = b.Cart;
            for (int L = 1; L < MaxLevel; L++)
                Assert.IsTrue(Formula.Price(Formula.Evaluate(cart.UpgradeCostFormulaType, cart.UpgradeCostBase, cart.UpgradeCostMultiplier, cart.UpgradeCostGrowth, L)).FitsInDouble, "кузница L" + L);
            sw.Stop();

            CollectionAssert.AreEquivalent(
                new[] { "diamond", "garnet", "heliodor", "ruby", "alexandrite", "celestine", "amber", "moonstone", "sunstone" },
                firstOverflow.Keys, "девять поздних шахт diamond … sunstone");
            Assert.AreEqual(1043, firstOverflow["sunstone"], "sunstone — с перехода 1043→1044");
            Assert.AreEqual(1183, firstOverflow["diamond"], "diamond — с перехода 1183→1184");
            Assert.AreEqual(1043, firstOverflow.Values.Min());
            Assert.AreEqual(1183, firstOverflow.Values.Max());
            Assert.AreEqual(352.0, Math.Round(maxLog), "максимум цены ~1e352");
            Assert.Less(sw.ElapsedMilliseconds, MaxMilliseconds);
        }

        // ───────────── Показ и save/load ─────────────

        /// <summary>Показ (ТЗ 15.1) цен +1 на 1040–1199, сумм «До порога» и выхода со всеми порогами: без NaN, ∞ и зависаний.</summary>
        [Test]
        public void FormattingOfBigValuesHasNoNaNInfinityOrHang()
        {
            var s = ProducingSunstone(out var shaft);
            var values = BigValues(s, shaft);
            var sw = Stopwatch.StartNew();
            foreach (var kv in values) AssertDisplay(kv.Value, kv.Key);
            sw.Stop();
            Assert.Less(sw.ElapsedMilliseconds, MaxMilliseconds, "показ больших чисел быстрый");
        }

        /// <summary>
        /// ТЗ 12.1: «Числа сериализуются в InvariantCulture, round-trip»; «сохранить → загрузить → сохранить при неизменном
        /// состоянии даёт тот же payloadSha256». Большая форма в диапазоне double [1e300; 1.8e308) восстанавливается
        /// побитно: цены sunstone 1039→1040 и 1041→1042 и равномерная сетка 10^300…10^308.25; золото, равное цене
        /// 1041→1042, не дрейфует при повторных циклах save/load.
        /// </summary>
        [Test]
        public void BigFormInsideDoubleRangeRoundTripsExactly()
        {
            var sh = TestData.Balance.Shafts[Sunstone];
            foreach (int level in new[] { 1039, 1041 })
            {
                var c = Formula.Price(Formula.Evaluate(sh.UpgradeCostFormulaType, sh.UpgradeCostBase, sh.UpgradeCostMultiplier, sh.UpgradeCostGrowth, level));
                Assert.IsTrue(InDoubleRangeBigForm(c), "цена " + level + "→" + (level + 1) + ": большая форма в диапазоне double");
                Assert.AreEqual(c, GameNumber.Parse(c.ToStorageString()), "round-trip цены " + level + "→" + (level + 1) + " '" + c.ToStorageString() + "'");
            }

            int checkedValues = 0;
            for (int i = 0; i <= 825; i++)
            {
                var v = GameNumber.FromLog10(300.0 + i * 0.01);
                if (!InDoubleRangeBigForm(v)) continue;
                checkedValues++;
                Assert.AreEqual(v, GameNumber.Parse(v.ToStorageString()), "round-trip '" + v.ToStorageString() + "'");
                Assert.AreEqual(-v, GameNumber.Parse((-v).ToStorageString()), "round-trip '" + (-v).ToStorageString() + "'");
            }
            Assert.Greater(checkedValues, 800, "сетка покрывает диапазон [1e300; 1.8e308)");

            // Уровень сохранения: золото = цена 1041→1042; три цикла дают один и тот же payload.
            var price1041 = Formula.Price(Formula.Evaluate(sh.UpgradeCostFormulaType, sh.UpgradeCostBase, sh.UpgradeCostMultiplier, sh.UpgradeCostGrowth, 1041));
            var s = GameSession.NewGame(TestData.Balance, 12345UL, SaveSlots.Debug, 1_000_000L, 1);
            SetGold(s, price1041);
            string j1 = SaveService.SerializeState(s.State);
            var loaded = SaveService.DeserializeState(j1);
            Assert.AreEqual(price1041, loaded.Mine.Gold, "золото после загрузки побитно то же");
            string j2 = SaveService.SerializeState(loaded);
            string j3 = SaveService.SerializeState(SaveService.DeserializeState(j2));
            Assert.AreEqual(j1, j2, "сохранить → загрузить → сохранить: тот же payload");
            Assert.AreEqual(j2, j3, "повторный цикл save/load не дрейфует");
        }

        /// <summary>
        /// Save/load (ТЗ 15.1, 12.1 «числа сериализуются … round-trip», NaN/Infinity в сохранение не попадают):
        /// строковое хранение цен 1040–1199, сумм «До порога» и выхода без NaN/∞ и точное, в том числе для большой формы
        /// в диапазоне double [1e300; 1.8e308); снимок состояния с золотом ~1e360,
        /// уровнем 1200, буфером выплаты и журналом трат восстанавливается без потерь, в том числе через файл.
        /// </summary>
        [Test]
        public void SaveLoadOfBigValuesIsExactWithoutNaNOrInfinity()
        {
            var s = ProducingSunstone(out var shaft);
            int ord = shaft.Ordinal;
            var sw = Stopwatch.StartNew();
            int inDoubleBig = 0;
            foreach (var kv in BigValues(s, shaft))
            {
                string text = kv.Value.ToStorageString();
                Assert.IsFalse(text.Contains("NaN") || text.Contains("Infinity") || text.Contains("∞"), kv.Key + ": '" + text + "'");
                var back = GameNumber.Parse(text);
                AssertFinitePositive(back, kv.Key + ": после загрузки");
                // ТЗ 12.1: round-trip точный и для большой формы, которая ещё помещается в double.
                if (InDoubleRangeBigForm(kv.Value)) inDoubleBig++;
                Assert.AreEqual(kv.Value, back, kv.Key + ": round-trip '" + text + "'");
            }
            Assert.Greater(inDoubleBig, 0, "среди проверенных значений есть большая форма в диапазоне double");

            // Состояние: 1199 → +1 до 1200 при золоте 1e360, затем цикл ручной выплаты со всеми порогами.
            var gen = SetLevel(s, shaft, MaxLevel - 1);
            SetGold(s, Huge);
            Assert.AreEqual(CommandResult.Ok, s.UpgradeGenerator(ord, out _));
            Assert.AreEqual(MaxLevel, gen.Level);
            s.Advance(s.GeneratorCycleTicks(ord));
            Assert.IsTrue(gen.PayoutReady);
            var output = s.GeneratorOutput(ord);
            Assert.AreEqual(output, gen.PendingPayout);
            s.Events.Clear();

            string json = Snapshot(s);
            Assert.IsFalse(json.Contains("NaN"), "в сохранении нет NaN");
            Assert.IsFalse(json.Contains("Infinity") || json.Contains("∞"), "в сохранении нет Infinity");
            StringAssert.Contains("\"" + s.Mine.Gold.ToStorageString() + "\"", json, "золото хранится строкой без потерь");
            StringAssert.Contains("\"" + gen.PendingPayout.ToStorageString() + "\"", json, "буфер выплаты хранится строкой без потерь");

            var restored = GameSession.FromState(TestData.Balance, SaveService.DeserializeState(json));
            var rgen = restored.Mine.Objects[ord].Gen;
            Assert.AreEqual(s.Mine.Gold, restored.Mine.Gold, "золото ~1e360 восстановлено точно");
            Assert.AreEqual(gen.PendingPayout, rgen.PendingPayout, "буфер выплаты восстановлен точно");
            Assert.AreEqual(MaxLevel, rgen.Level);
            Assert.AreEqual(gen.ThresholdsReached, rgen.ThresholdsReached);
            Assert.AreEqual(output, restored.GeneratorOutput(ord), "выход после загрузки тот же");
            foreach (var kv in s.Player.Ledger)
                Assert.AreEqual(kv.Value, restored.Player.Ledger[kv.Key], "журнал начислений: " + kv.Key);
            Assert.AreEqual(json, Snapshot(restored), "повторная сериализация совпадает");
            for (int L = 1040; L < MaxLevel; L++)
                Assert.AreEqual(s.UpgradeCost(ord, L), restored.UpgradeCost(ord, L), "цена после загрузки, L=" + L);
            sw.Stop();
            Assert.Less(sw.ElapsedMilliseconds, MaxMilliseconds, "сериализация больших значений в памяти быстрая");

            // Через файл сохранения (конверт, tmp + Replace) — то же состояние. Время записи на диск без порога (ТЗ 12.2).
            string dir = Path.Combine(Path.GetTempPath(), "catminers-bignum-" + Guid.NewGuid().ToString("N"));
            try
            {
                var svc = new SaveService(dir);
                svc.Save(s.State, 2_000_000L);
                var loaded = svc.Load(SaveSlots.Debug, TestData.Balance.Sha256);
                Assert.AreEqual(LoadStatus.Loaded, loaded.Status, loaded.Message);
                Assert.AreEqual(json, SaveService.SerializeState(loaded.State), "файловый save/load точный");
                Assert.AreEqual(s.Mine.Gold, loaded.State.Mine.Gold);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
