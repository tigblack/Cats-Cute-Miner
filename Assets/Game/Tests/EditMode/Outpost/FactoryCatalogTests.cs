using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// ТЗ 2.2 (заводы вместо месторождений), контракты пакета WP0: factory_rules.json и его встроенная копия, каталог заводов
    /// (20 типов MineShafts, товары приложения A, 39 карточек заводов, проверки §3.1), 1 232 требования автодоставки токенов
    /// <c>s</c>, формулы выпуска/цены/времени в пути (приложение B), заводы карты с маршрутами труб, сериализация нового
    /// состояния шахты и отказы команд DeliverFactory/SellGoods без заводов. Ожидания — из ТЗ 2.2 и исходного JSON, не из кода.
    /// </summary>
    public class FactoryCatalogTests
    {
        static OutpostConfigSet s_Configs;
        static OutpostConfigSet Configs => s_Configs ?? (s_Configs = OutpostConfigSet.LoadDefault().WithCards(TestData.Balance));
        static FactoryCatalog Cat => Configs.Factories;

        /// <summary>Приложение A: тип, товар, первая шахта, заводов в кампании, цикл (с), Common, Uncommon.</summary>
        static readonly (string Type, string Good, int First, int Count, long Cycle, string Common, string Uncommon)[] AppendixA =
        {
            ("amethyst", "milk", 2, 83, 8, "ca002", "ca014"),
            ("citrine", "wood", 3, 86, 15, "ca003", "ca015"),
            ("agate", "fish", 4, 95, 30, "ca004", "ca016"),
            ("topaz", "yarn", 5, 94, 60, "ca005", "ca017"),
            ("opal", "catfood", 7, 80, 120, "ca006", "ca018"),
            ("jade", "boxes", 10, 83, 240, "ca007", "ca019"),
            ("onyx", "bricks", 14, 83, 360, "ca008", "ca020"),
            ("sapphire", "steel", 19, 80, 600, "ca009", "ca021"),
            ("tourmaline", "glass", 24, 79, 1200, "ca010", "ca022"),
            ("aquamarine", "catnip", 31, 74, 1800, "ca011", "ca023"),
            ("emerald", "toys", 38, 69, 2700, "ca012", "ca024"),
            ("diamond", "scratchers", 47, 66, 3600, "ca013", "ca025"),
            ("garnet", "cushions", 57, 57, 5400, "ca039", "ca040"),
            ("heliodor", "bells", 68, 51, 7200, "ca041", "ca042"),
            ("ruby", "collars", 80, 46, 10800, "ca044", "ca045"),
            ("alexandrite", "gears", 93, 38, 14400, "ca046", "ca047"),
            ("celestine", "lanterns", 104, 26, 18000, "ca048", "ca049"),
            ("amber", "honey", 114, 20, 21600, "ca056", "ca057"),
            ("moonstone", "silk", 123, 14, 25200, "ca058", "ca059"),
            ("sunstone", "caviar", 134, 8, 28800, "ca060", null),
        };

        /// <summary>ТЗ 2.2 §4.1: шахты, где топология wide-branching-mines-2 со стоящими заводами не проходит проверку.</summary>
        static readonly int[] BlockedByStandingFactories =
        {
            6, 7, 13, 17, 23, 37, 49, 54, 59, 66, 68, 71, 82, 84, 85, 91, 96, 102, 104, 105, 110, 112, 115, 118, 119, 122, 123, 126,
            128, 129, 132, 133, 136, 139, 140, 142, 143, 144,
        };

        static string ResourcePath(string name) => Path.Combine(TestData.ProjectRoot, "Assets", "Game", "Resources", "Outpost", name);

        // ───────────────────────────── factory_rules.json ─────────────────────────────

        [Test]
        public void Rules_FileMatchesEmbeddedCopy_Version_PipesAndPrices()
        {
            string file = File.ReadAllText(ResourcePath("factory_rules.json")).Replace("\r\n", "\n");
            Assert.AreEqual(OutpostConfigSet.DefaultFactoryRulesJson.Replace("\r\n", "\n"), file, "встроенная копия = Resources (gen_outpost_config_defaults.py)");
            Assert.That(OutpostConfigSet.ResourcePaths, Does.Contain("Outpost/factory_rules"));

            var rules = OutpostConfigSet.LoadDefault().FactoryRules;
            Assert.IsNotNull(rules, "правила заводов есть и без источника");
            Assert.AreEqual("factories-1", rules.Version);
            Assert.AreEqual(FactoryRules.CurrentVersion, rules.Version);
            // ТЗ 2.2 §9, приложение B п. 6 (WP7): PriceRate = 4 — итог калибровки (ориентир §3.2 был 5/16), Docs/Rework/BALANCE_FACTORIES.md.
            Assert.AreEqual(Rational.FromLong(4), rules.PriceRate);
            Assert.AreEqual(Rational.FromLong(6), rules.PipeSpeedCellsPerSec);
            Assert.AreEqual(Rational.One, rules.MinTransitSec);
            Assert.AreEqual(20, rules.Goods.Count);
            foreach (var g in rules.Goods)
            {
                Assert.AreEqual(20, g.UnitsPerCycle, g.TypeId + ": стартовый UnitsPerCycle");
                Assert.AreEqual(rules.PriceRate * g.CycleSec / g.UnitsPerCycle, g.PriceFactor, g.TypeId + ": PriceFactor = PriceRate × CycleSec / UnitsPerCycle");
            }
            Assert.AreEqual(Rational.Of(8, 5), rules.ForGood("milk").PriceFactor, "приложение B п. 6: 4 × 8 / 20 = 8/5");
            Assert.AreEqual(Rational.FromLong(120), rules.ForGood("steel").PriceFactor, "приложение B п. 6: 4 × 600 / 20 = 120");
            Assert.AreEqual(1, rules.NumberOfGood("milk"));
            Assert.AreEqual(20, rules.NumberOfGood("caviar"));
            Assert.AreEqual(0, rules.NumberOfGood("gold"));

            var parsed = OutpostConfigSet.ParseResources(OutpostConfigSet.ResourcePaths
                .Select(p => File.ReadAllText(Path.Combine(TestData.ProjectRoot, "Assets", "Game", "Resources", p + ".json")).Replace("\n", "\r\n"))
                .ToArray());
            Assert.AreEqual(OutpostConfigSet.LoadDefault().FactoryRulesHash, parsed.FactoryRulesHash, "hash не зависит от переводов строк");
            Assert.AreEqual(OutpostConfigSet.LoadDefault().CatalogHash, parsed.CatalogHash);
        }

        [Test]
        public void Rules_Validate_RejectsBrokenRows()
        {
            string ok = OutpostConfigSet.DefaultFactoryRulesJson;
            void Broken(string from, string to, string why)
            {
                Assert.That(ok, Does.Contain(from), why);
                Assert.Throws<OutpostConfigException>(() => FactoryRules.Parse(ok.Replace(from, to)), why);
            }
            Broken("\"GoodId\": \"wood\"", "\"GoodId\": \"milk\"", "повтор GoodId");
            Broken("\"TypeId\": \"citrine\"", "\"TypeId\": \"amethyst\"", "повтор TypeId");
            Broken("\"CycleSec\": 8,", "\"CycleSec\": 0,", "CycleSec < 1");
            // ТЗ 2.2 §9 (WP7): строка молока после калибровки PriceRate = 4 — PriceFactor 8/5 (было 1/8).
            Broken("\"UnitsPerCycle\": 20, \"PriceFactor\": \"8/5\"", "\"UnitsPerCycle\": 0, \"PriceFactor\": \"8/5\"", "UnitsPerCycle < 1");
            Broken("\"PriceFactor\": \"8/5\"", "\"PriceFactor\": \"0\"", "PriceFactor ≤ 0");
            Broken("\"Color\": \"cream4\"", "\"Color\": \"cream\"", "тон без индекса");
            Broken("\"Color\": \"cream4\"", "\"Color\": \"#ffffff\"", "не имя тона");
            Broken("\"PipeSpeedCellsPerSec\": \"6\"", "\"PipeSpeedCellsPerSec\": \"0\"", "скорость трубы 0");
            Broken("\"Version\": \"factories-1\"", "\"Version\": \"\"", "нет версии");
        }

        [Test]
        public void Rules_Colors_AreTonesOfPixelArtRamps()
        {
            string pxl = FindUp(Path.Combine("Tools", "PixelArt", "pxl.py"));
            Assert.IsNotNull(pxl, "Tools/PixelArt/pxl.py не найден выше " + TestData.ProjectRoot);
            var ramps = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(File.ReadAllText(pxl), @"^\s*'([a-z_]+)'\s*:\s*\[([^\]]*)\]", RegexOptions.Multiline))
                ramps[m.Groups[1].Value] = Regex.Matches(m.Groups[2].Value, "'#[0-9a-fA-F]{6}'").Count;
            Assert.That(ramps.Count, Is.GreaterThanOrEqualTo(10), "RAMPS разобраны");
            foreach (var g in Configs.FactoryRules.Goods)
            {
                Assert.IsTrue(FactoryRules.TrySplitTone(g.Color, out string ramp, out int index), g.GoodId + ": " + g.Color);
                Assert.IsTrue(ramps.ContainsKey(ramp), g.GoodId + ": рампы " + ramp + " нет в RAMPS");
                Assert.That(index, Is.InRange(0, ramps[ramp] - 1), g.GoodId + ": индекс " + g.Color + " вне рампы");
            }
            Assert.AreEqual("cream4", Configs.FactoryRules.ForGood("milk").Color, "приложение A: Milk — cream4");
            Assert.IsTrue(Configs.FactoryRules.ForGood("wood").Color.StartsWith("wood", StringComparison.Ordinal), "Wood — тон рампы wood");
            Assert.IsTrue(Configs.FactoryRules.ForGood("fish").Color.StartsWith("navy", StringComparison.Ordinal), "Fish — тон рампы navy");
        }

        // ───────────────────────────── Каталог ─────────────────────────────

        [Test]
        public void Catalog_20Types_InMineShaftsOrder_GoodsAndCyclesOfAppendixA()
        {
            Assert.IsNull(OutpostConfigSet.LoadDefault().Factories, "без источника каталога нет");
            Assert.IsNotNull(Cat);
            Assert.AreEqual(FactoryCatalog.ExpectedTypeCount, Cat.Types.Count);
            Assert.AreEqual(TestData.Balance.Sha256, Cat.SourceSha256);
            for (int i = 0; i < AppendixA.Length; i++)
            {
                var a = AppendixA[i];
                var t = Cat.Types[i];
                Assert.AreEqual(i + 1, t.Number);
                Assert.AreEqual(a.Type, t.TypeId);
                Assert.AreEqual(a.Good, t.GoodId);
                Assert.AreSame(t, Cat.Get(a.Type));
                Assert.AreSame(t, Cat.ByGood(a.Good));
                Assert.AreEqual(a.Cycle, t.CycleSec, a.Type + ": цикл приложения A");
                Assert.AreEqual((long)TestData.Balance.Shafts[a.Type].GenerationDelaySecBase, t.CycleSec, a.Type + ": CycleSec = MineShafts.GenerationDelaySecBase (Р1)");
                Assert.AreEqual(a.Cycle * 1_000_000L, t.CycleTicks);
                Assert.AreEqual(2, t.WidthCells);
                Assert.AreEqual(3, t.DepthCells);
                Assert.AreEqual(20, t.UnitsPerCycle);
            }
            Assert.AreEqual(8_000_000L, Cat.Get("amethyst").CycleTicks, "приложение B п. 4");
            Assert.AreEqual(15_000_000L, Cat.Get("citrine").CycleTicks);
            Assert.AreEqual(28_800_000_000L, Cat.Get("sunstone").CycleTicks);
            Assert.IsNull(Cat.Get("spawningcart"));
            Assert.IsNull(Cat.ByGood("gold"));
        }

        [Test]
        public void Catalog_39FactoryCards_SunstoneWithoutUncommon_Ca001IsNotFactory()
        {
            Assert.AreEqual(39, Cat.CardIds.Count);
            Assert.AreEqual(39, Cat.CardIds.Distinct().Count());
            int common = 0, uncommon = 0;
            foreach (var a in AppendixA)
            {
                var t = Cat.Get(a.Type);
                Assert.AreEqual(a.Common, t.CommonCardId, a.Type);
                Assert.AreEqual(a.Uncommon, t.UncommonCardId, a.Type);
                Assert.AreEqual(14, t.CommonMaxLevel, a.Type + ": Common — максимум 14");
                Assert.AreEqual(a.Uncommon != null ? 12 : 0, t.UncommonMaxLevel, a.Type + ": Uncommon — максимум 12");
                Assert.AreEqual(1, Configs.Cards.Get(t.CommonCardId).Rarity);
                Assert.AreSame(t, Cat.ByCard(t.CommonCardId));
                common++;
                if (t.UncommonCardId != null)
                {
                    Assert.AreEqual(2, Configs.Cards.Get(t.UncommonCardId).Rarity);
                    Assert.AreSame(t, Cat.ByCard(t.UncommonCardId));
                    uncommon++;
                }
            }
            Assert.AreEqual(20, common);
            Assert.AreEqual(19, uncommon);
            Assert.IsNull(Cat.Get("sunstone").UncommonCardId, "у sunstone нет Uncommon");
            Assert.IsFalse(Cat.IsFactoryCard("ca001"), "ca001 (spawningcart) — не завод");
            Assert.IsNull(Cat.ByCard("ca001"));
            Assert.IsTrue(Configs.Cards.Get("ca001").IsManager, "ca001 — manager-карточка, но не карточка завода");
            Assert.AreEqual(40, Configs.Cards.Cards.Count(c => c.IsManager), "40 manager-карточек = 39 заводов + ca001");
            foreach (var c in Configs.Cards.Cards)
                Assert.AreEqual(c.IsManager && c.Id != "ca001", Cat.IsFactoryCard(c.Id), c.Id);
            // Порядок CardIds: по номеру типа, Common перед Uncommon.
            Assert.AreEqual(new[] { "ca002", "ca014", "ca003", "ca015" }, Cat.CardIds.Take(4).ToArray());
            Assert.AreEqual("ca060", Cat.CardIds.Last());
        }

        [Test]
        public void Catalog_Build_RejectsCycleMismatch_MissingType_AndWrongFootprint()
        {
            var repo = TestData.Balance;
            FactoryRules Edit(string from, string to)
            {
                Assert.That(OutpostConfigSet.DefaultFactoryRulesJson, Does.Contain(from));
                return FactoryRules.Parse(OutpostConfigSet.DefaultFactoryRulesJson.Replace(from, to));
            }
            var ex = Assert.Throws<OutpostConfigException>(() => FactoryCatalog.Build(repo, Edit("\"CycleSec\": 8,", "\"CycleSec\": 9,")));
            Assert.That(ex.Message, Does.Contain("amethyst").And.Contain("GenerationDelaySecBase"), "CycleSec сверяется с JSON (Р1)");
            Assert.Throws<OutpostConfigException>(() => FactoryCatalog.Build(repo, Edit("\"TypeId\": \"sunstone\"", "\"TypeId\": \"sunstone2\"")),
                "товар неизвестного типа и тип без товара");
            var rules = FactoryRules.Parse(OutpostConfigSet.DefaultFactoryRulesJson);
            rules.Goods.RemoveAt(19);
            Assert.Throws<OutpostConfigException>(() => FactoryCatalog.Build(repo, rules), "у sunstone нет товара");

            var fresh = OutpostKitA.FreshRepo();
            fresh.Shafts["jade"].WidthCells = 3;
            Assert.Throws<OutpostConfigException>(() => FactoryCatalog.Build(fresh, Configs.FactoryRules), "footprint 2×3");
            fresh = OutpostKitA.FreshRepo();
            fresh.Shafts["onyx"].GenerationDelaySecGrowth = 1;
            Assert.Throws<OutpostConfigException>(() => FactoryCatalog.Build(fresh, Configs.FactoryRules), "GenerationDelaySecGrowth = 0");
        }

        /// <summary>
        /// Ревью этапа 1 (ТЗ 2.2 §3.2, §6.3): номер типа — позиция в MineShafts (<see cref="FactoryType.Number"/>), а
        /// <see cref="FactoryRules.NumberOfGood"/> (порядок строк рынка) — позиция в Goods. Набор типов полный, форма правил верна,
        /// но строки Goods переставлены — каталог отвергает такие правила с номером строки и ожидаемым типом.
        /// </summary>
        [Test]
        public void Catalog_Build_RejectsGoodsOutOfMineShaftsOrder()
        {
            foreach (var t in Cat.Types)
                Assert.AreEqual(t.Number, Configs.FactoryRules.NumberOfGood(t.GoodId), t.TypeId + ": номер типа каталога = номер строки Goods");

            foreach (var (a, b) in new[] { (0, 1), (18, 19), (4, 11) })
            {
                var rules = FactoryRules.Parse(OutpostConfigSet.DefaultFactoryRulesJson);
                var swap = rules.Goods[a];
                rules.Goods[a] = rules.Goods[b];
                rules.Goods[b] = swap;
                Assert.DoesNotThrow(() => rules.Validate(), "форма правил верна — дело только в порядке");
                Assert.AreEqual(a + 1, rules.NumberOfGood(rules.Goods[a].GoodId), "без проверки номер товара разошёлся бы с номером типа");
                var ex = Assert.Throws<OutpostConfigException>(() => FactoryCatalog.Build(TestData.Balance, rules), "Goods[" + a + "] ↔ Goods[" + b + "]");
                string expectedType = TestData.Balance.Root.MineShafts[a].Id;
                Assert.That(ex.Message, Does.Contain("Goods[" + a + "]").And.Contain(expectedType).And.Contain("порядок"), ex.Message);
            }
        }

        // ───────────────────────────── Токены s ─────────────────────────────

        [Test]
        public void Tokens_1232Requirements_ParseAndValidate()
        {
            var tokens = FactoryCatalog.ScanTokens(TestData.Balance);
            Assert.AreEqual(1232, tokens.Count, "1 232 токена s");
            int common = 0, uncommon = 0;
            int minC = int.MaxValue, maxC = 0, minU = int.MaxValue, maxU = 0;
            foreach (var t in tokens)
            {
                string where = "zone" + t.Zone + " #" + t.Index + " " + t.TypeId;
                Assert.IsNull(Cat.RequirementError(t.TypeId, t.CardId, t.CardLevel), where);
                Assert.IsNotNull(Configs.Cards.Chest(t.RewardId), where + ": сундук " + t.RewardId);
                var type = Cat.Get(t.TypeId);
                if (t.CardId == type.CommonCardId)
                {
                    common++;
                    minC = Math.Min(minC, t.CardLevel);
                    maxC = Math.Max(maxC, t.CardLevel);
                }
                else
                {
                    Assert.AreEqual(type.UncommonCardId, t.CardId, where);
                    uncommon++;
                    minU = Math.Min(minU, t.CardLevel);
                    maxU = Math.Max(maxU, t.CardLevel);
                }
            }
            Assert.AreEqual(649, common, "649 требований Common");
            Assert.AreEqual(583, uncommon, "583 требования Uncommon");
            Assert.AreEqual((1, 14), (minC, maxC), "Common 1–14");
            Assert.AreEqual((3, 12), (minU, maxU), "Uncommon 3–12");

            var byZone = tokens.GroupBy(t => t.Zone).ToDictionary(g => g.Key, g => g.ToList());
            Assert.IsFalse(byZone.ContainsKey(1), "в шахте 1 заводов нет");
            for (int z = 2; z <= 144; z++)
            {
                Assert.IsTrue(byZone.ContainsKey(z), "zone" + z + ": есть завод");
                var list = byZone[z];
                Assert.That(list.Count, Is.InRange(1, 15), "zone" + z);
                Assert.AreEqual(list.Count, list.Select(t => t.TypeId).Distinct().Count(), "zone" + z + ": тип не больше одного раза");
            }
            Assert.AreEqual(new[] { 130, 140 }, byZone.Where(kv => kv.Value.Count == 15).Select(kv => kv.Key).OrderBy(z => z).ToArray());
            Assert.AreEqual(new[] { 110, 120, 133, 144 }, byZone.Where(kv => kv.Value.Count == 14).Select(kv => kv.Key).OrderBy(z => z).ToArray());
            foreach (var a in AppendixA)
            {
                Assert.AreEqual(a.First, tokens.Where(t => t.TypeId == a.Type).Min(t => t.Zone), a.Type + ": первая шахта");
                Assert.AreEqual(a.Count, tokens.Count(t => t.TypeId == a.Type), a.Type + ": заводов в кампании");
            }

            // Приложение B, контрольные токены.
            var m2 = byZone[2].Single();
            Assert.AreEqual(("amethyst", 1, "scg_m002_s01", "ca002", 1), (m2.TypeId, m2.Level, m2.RewardId, m2.CardId, m2.CardLevel));
            var m3 = byZone[3].ToDictionary(t => t.TypeId);
            Assert.AreEqual(("scg_m003_s01", "ca002", 2), (m3["amethyst"].RewardId, m3["amethyst"].CardId, m3["amethyst"].CardLevel));
            Assert.AreEqual((2, "scg_m003_s02", "ca003", 1), (m3["citrine"].Level, m3["citrine"].RewardId, m3["citrine"].CardId, m3["citrine"].CardLevel));
        }

        [Test]
        public void Requirement_RejectsForeignCard_ZeroAndTooHighLevel()
        {
            Assert.IsNull(Cat.RequirementError("amethyst", "ca002", 1));
            Assert.IsNull(Cat.RequirementError("amethyst", "ca002", 14));
            Assert.IsNull(Cat.RequirementError("amethyst", "ca014", 12));
            Assert.IsNotNull(Cat.RequirementError("amethyst", "ca003", 1), "карточка другого типа");
            Assert.IsNotNull(Cat.RequirementError("amethyst", "ca001", 1), "ca001 — не карточка завода");
            Assert.IsNotNull(Cat.RequirementError("amethyst", "ca026", 1), "глобальная карточка");
            Assert.IsNotNull(Cat.RequirementError("amethyst", null, 1));
            Assert.IsNotNull(Cat.RequirementError("amethyst", "ca002", 0), "уровень ≥ 1");
            Assert.IsNotNull(Cat.RequirementError("amethyst", "ca002", 15), "Common — до 14");
            Assert.IsNotNull(Cat.RequirementError("amethyst", "ca014", 13), "Uncommon — до 12");
            Assert.IsNotNull(Cat.RequirementError("ruby2", "ca044", 1), "неизвестный тип");
            Assert.IsNotNull(Cat.RequirementError("sunstone", "ca061", 1), "у sunstone нет Uncommon");
        }

        // ───────────────────────────── Формулы (приложение B) ─────────────────────────────

        /// <summary>
        /// Ревью этапа 1 (ТЗ 2.2 §4.4): правило выпуска floor(UnitsPerCycle × (1 + Σ Output(L))) — одна функция ядра
        /// <see cref="FactoryMath.UnitsPerCycle"/>; Σ Output(L) — только <see cref="CardService.FactoryOutputBonus"/>. Код игры
        /// (Assets/Game/Scripts: ядро, Runtime, Playable, DevTools, Editor) не вызывает арифметику FactoryMath.Units и сумму бонуса
        /// в обход неё и не заводит своих копий (прежние TeamStats.CardFactoryOutput/FactoryOutputFor/FactoryUnits удалены).
        /// Комментарии не считаются.
        /// </summary>
        [Test]
        public void Units_SingleRuleFunction_NoCopiesInGameCode()
        {
            string scripts = Path.Combine(TestData.ProjectRoot, "Assets", "Game", "Scripts");
            Assert.IsTrue(Directory.Exists(scripts), scripts);
            var offenders = new List<string>();
            int files = 0;
            foreach (string file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                files++;
                string name = Path.GetFileName(file);
                string code = Regex.Replace(File.ReadAllText(file), @"//[^\n]*", "");
                string where = file.Substring(scripts.Length + 1).Replace('\\', '/');
                if (name != "FactoryMath.cs" && Regex.IsMatch(code, @"\bFactoryMath\s*\.\s*Units\s*\("))
                    offenders.Add(where + ": FactoryMath.Units( — выпуск считать через FactoryMath.UnitsPerCycle");
                if (name != "FactoryMath.cs" && name != "CardService.cs" && Regex.IsMatch(code, @"\bFactoryOutputBonus\s*\("))
                    offenders.Add(where + ": FactoryOutputBonus( — бонус выпуска только внутри FactoryMath.UnitsPerCycle");
                if (Regex.IsMatch(code, @"\b(CardFactoryOutput|FactoryOutputFor|FactoryUnits)\b"))
                    offenders.Add(where + ": копия правила выпуска (CardFactoryOutput/FactoryOutputFor/FactoryUnits)");
            }
            Assert.That(files, Is.GreaterThan(100), "просмотрен весь код игры");
            CollectionAssert.IsEmpty(offenders, string.Join("\n", offenders));
        }

        [Test]
        public void Math_UnitsPriceTransit_ControlValues()
        {
            // Output(L) = 1/10 + 1/20·(L − 1) — стартовые числа cards-3 (§7.1); сумму по карточкам считает CardService.FactoryOutputBonus,
            // выпуск завода для профиля — FactoryMath.UnitsPerCycle (единственная функция правила).
            Rational Output(int level) => level < 1 ? Rational.Zero : Rational.Of(1, 10) + Rational.Of(1, 20) * (level - 1);
            Assert.AreEqual(20, FactoryMath.Units(20, Rational.Zero));
            Assert.AreEqual(22, FactoryMath.Units(20, Output(1)), "Common ур. 1");
            Assert.AreEqual(26, FactoryMath.Units(20, Output(3) + Output(1)), "Common ур. 3 + Uncommon ур. 1");
            Assert.AreEqual(48, FactoryMath.Units(20, Output(14) + Output(12)), "Common 14 + Uncommon 12");
            for (int level = 1; level < 14; level++)
                Assert.That(FactoryMath.Units(20, Output(level + 1)), Is.GreaterThan(FactoryMath.Units(20, Output(level))), "каждое улучшение +1 единица");
            Assert.AreEqual(25, FactoryMath.Units(17, Rational.Of(1, 2)), "floor(17 × 3/2) = 25");
            Assert.Throws<ArgumentOutOfRangeException>(() => FactoryMath.Units(20, Rational.Of(-1, 10)));

            var b = Configs.Balance;
            Assert.AreEqual(Rational.FromLong(5), b.CoinBase);
            Assert.AreEqual(BigInteger.One, FactoryMath.Price(b, 2, Rational.Of(1, 8)), "молоко в шахте 2: ceil(5 × 6/5 × 1/8) = 1");
            Assert.AreEqual(new BigInteger(57), FactoryMath.Price(b, 2, Rational.Of(75, 8)), "сталь в шахте 2: ceil(5 × 6/5 × 75/8) = ceil(56,25)");
            Assert.AreEqual(BigInteger.One, FactoryMath.Price(b, 1, Rational.Of(1, 8)), "ceil(5/8) = 1");

            var rules = Configs.FactoryRules;
            Assert.AreEqual(4_000_000L, FactoryMath.TransitTicks(rules, 23), "max(1, ceil(23/6)) = 4 с");
            Assert.AreEqual(1_000_000L, FactoryMath.TransitTicks(rules, 0), "не меньше MinTransitSec");
            Assert.AreEqual(1_000_000L, FactoryMath.TransitTicks(rules, 6));
            Assert.AreEqual(2_000_000L, FactoryMath.TransitTicks(rules, 7));
            Assert.AreEqual(1_000_000L, FactoryMath.TransitTicks(rules, -1), "нет маршрута → как 0 шагов");
        }

        // ───────────────────────────── Карта: заводы и трубы ─────────────────────────────

        [Test]
        public void Map_Zone2_FactoryHasRequirement_OutletAndRouteToIntake()
        {
            var map = OutpostLevelAdapter.BuildMine(TestData.Balance, 2, Configs);
            Assert.AreEqual(1, map.Factories.Length);
            var f = map.Factories[0];
            var target = map.Target(f.TargetId);
            Assert.IsTrue(target.IsFactory);
            Assert.AreEqual(("amethyst", "ca002", 1), (f.TypeId, f.RequiredCardId, f.RequiredCardLevel), "приложение B п. 1");
            Assert.AreEqual(("ca002", 1), (target.CardId, target.CardLevel), "требование в MapTarget сохраняется топологией");
            Assert.AreSame(f, map.Factory(f.TargetId));
            Assert.IsTrue(f.HasRoute);
            AssertRoute(map, f);

            var zone1 = OutpostLevelAdapter.BuildMine(TestData.Balance, 1, Configs);
            Assert.AreEqual(0, zone1.Factories.Length, "в шахте 1 заводов нет");
            Assert.IsNull(zone1.Factory(0));
            foreach (var t in zone1.Targets)
                Assert.AreEqual(((string)null, 0), (t.CardId, t.CardLevel), "у целей r/x/p требования нет");
        }

        [Test]
        public void Map_AllZones_RoutesDeterministic_AndExistOutsideMinesListedInTz41()
        {
            var listed = new HashSet<int>(BlockedByStandingFactories);
            int factories = 0, withRoute = 0;
            var withoutRoute = new SortedSet<int>();
            for (int z = 1; z <= 144; z++)
            {
                var map = OutpostLevelAdapter.BuildMine(TestData.Balance, z, Configs);
                var again = OutpostLevelAdapter.BuildMine(TestData.Balance, z, Configs);
                Assert.AreEqual(map.Targets.Count(t => t.IsFactory), map.Factories.Length, "zone" + z);
                CollectionAssert.AreEqual(map.Factories.Select(f => f.TargetId).OrderBy(id => id).ToArray(), map.Factories.Select(f => f.TargetId).ToArray(), "zone" + z + ": по TargetId");
                for (int i = 0; i < map.Factories.Length; i++)
                {
                    var f = map.Factories[i];
                    var g = again.Factories[i];
                    Assert.AreEqual((f.TargetId, f.OutletCell, f.RouteSteps), (g.TargetId, g.OutletCell, g.RouteSteps), "zone" + z + ": детерминизм");
                    CollectionAssert.AreEqual(f.RouteCells, g.RouteCells);
                    Assert.IsNull(Cat.RequirementError(f.TypeId, f.RequiredCardId, f.RequiredCardLevel), "zone" + z + " #" + f.TargetId);
                    factories++;
                    if (f.HasRoute)
                    {
                        withRoute++;
                        AssertRoute(map, f);
                    }
                    else
                    {
                        withoutRoute.Add(z);
                        Assert.AreEqual((-1, 0), (f.OutletCell, f.RouteCells.Length), "zone" + z + ": без маршрута — пустые поля");
                    }
                }
            }
            Assert.AreEqual(1232, factories, "все заводы кампании в картах");
            TestContext.WriteLine("Заводов с маршрутом: " + withRoute + " из " + factories + "; шахты без маршрута у завода: " + string.Join(", ", withoutRoute));
            foreach (int z in withoutRoute)
                Assert.IsTrue(listed.Contains(z), "zone" + z + ": завод без трубы вне списка шахт ТЗ 2.2 §4.1");
            // ТЗ 2.2 §4.1, §5, §16.1 «маршруты всех 1 232 заводов существуют»: ужесточено после wide-branching-mines-3 (WP2) —
            // трубы есть у всех заводов, шахт без маршрута нет.
            Assert.AreEqual(factories, withRoute, "маршрут есть у всех заводов; без маршрута: " + string.Join(", ", withoutRoute));
            CollectionAssert.IsEmpty(withoutRoute);
        }

        static void AssertRoute(OutpostMap map, MapFactory f)
        {
            var target = map.Target(f.TargetId);
            Assert.IsFalse(target.Cells.Contains(f.OutletCell));
            Assert.IsTrue(target.Cells.Any(c => Math.Abs(map.Col(c) - map.Col(f.OutletCell)) + Math.Abs(map.Row(c) - map.Row(f.OutletCell)) == 1),
                "выход соседствует с footprint");
            Assert.AreEqual(f.RouteSteps + 1, f.RouteCells.Length);
            Assert.AreEqual(f.OutletCell, f.RouteCells[0]);
            Assert.AreEqual(map.IntakeCell, f.RouteCells[f.RouteCells.Length - 1]);
            var walls = new HashSet<int>(map.Targets.Where(t => t.IsFactory).SelectMany(t => t.Cells));
            for (int i = 0; i < f.RouteCells.Length; i++)
            {
                int c = f.RouteCells[i];
                Assert.IsTrue(map.IsPlayable(c), "труба по проходимым клеткам");
                Assert.IsFalse(walls.Contains(c), "труба не проходит через заводы");
                if (i > 0)
                    Assert.AreEqual(1, Math.Abs(map.Col(c) - map.Col(f.RouteCells[i - 1])) + Math.Abs(map.Row(c) - map.Row(f.RouteCells[i - 1])), "4-связный путь");
            }
        }

        // ───────────────────────────── Состояние и команды ─────────────────────────────

        [Test]
        public void State_FactoryFields_RoundTripByteIdentical()
        {
            var state = StageProgressionService.NewGame(TestData.Balance, Configs, 7, 0, 2);
            var run = state.Run;
            run.FactoryRules = OutpostJson.DeepCopy(Configs.FactoryRules);
            // ТЗ 2.2 §10.2 (WP1): CreateRun создаёт завод шахты 2 в цепях; сериализацию проверяем на заводах со всеми полями.
            Assert.AreEqual(1, run.Factories.Count, "шахта 2: один завод из токена s");
            Assert.AreEqual(FactoryStatus.Locked, run.Factories[0].Status, "новая шахта — завод в цепях");
            run.Factories.Clear();
            // Ревью этапа 1 (ТЗ 2.2 §4.3, CORE_SPEC §16.4): цикл есть только у Working, у Ready CycleStart = CycleEnd = −1 и нет
            // автодоставки — прежний один завод «Ready + Automated + цикл» был невозможным состоянием; поля цикла и готовая партия
            // теперь на двух заводах в допустимых состояниях.
            run.Factories.Add(new FactoryState
            {
                TargetId = 42, TypeId = "amethyst", GoodId = "milk", Status = FactoryStatus.Working, Automated = true,
                CycleStart = 1_000_000, CycleEnd = 9_000_000, Produced = 42, Dispatched = 42, Cycles = 2,
            });
            run.Factories.Add(new FactoryState
            {
                TargetId = 43, TypeId = "citrine", GoodId = "wood", Status = FactoryStatus.Ready, ReadyUnits = 22, Produced = 22, Cycles = 1,
            });
            run.PipeBatches.Add(new PipeBatch { Seq = 2, FactoryId = 42, GoodId = "milk", Units = 20, DepartTick = 5, ArriveTick = 4_000_005 });
            run.NextPipeBatchSeq = 3;
            run.Warehouse["milk"] = 22;
            run.GoodsArrived["milk"] = 22;
            run.GoodsSold["milk"] = 0;
            run.GoodsSoldGold = BigInteger.Parse("123456789012345678901234567890");
            run.Counters.ManualDeliveries = 2;
            string json = OutpostJson.Serialize(state);
            StringAssert.Contains("\"Status\":\"Ready\"", json, "состояние завода — именем");
            var back = OutpostJson.Deserialize<OutpostGameState>(json);
            Assert.AreEqual(json, OutpostJson.Serialize(back), "save/load без нового округления");
            Assert.AreEqual((FactoryStatus.Working, 1_000_000L, 9_000_000L, true), (back.Run.Factories[0].Status, back.Run.Factories[0].CycleStart,
                back.Run.Factories[0].CycleEnd, back.Run.Factories[0].Automated));
            Assert.AreEqual((FactoryStatus.Ready, -1L, -1L, 22), (back.Run.Factories[1].Status, back.Run.Factories[1].CycleStart,
                back.Run.Factories[1].CycleEnd, back.Run.Factories[1].ReadyUnits));
            // ТЗ 2.2 §9, приложение B п. 6 (WP7): цены после калибровки PriceRate = 4.
            Assert.AreEqual(Rational.Of(8, 5), back.Run.FactoryRules.ForGood("milk").PriceFactor);
            Assert.AreEqual(new BigInteger(10), FactoryQueries.Price(back.Run, "milk"), "цена из снимков run (шахта 2): ceil(5 × 6/5 × 8/5) = 10");
            Assert.AreEqual(new BigInteger(220), FactoryQueries.StockValue(back.Run), "22 × 10");
            Assert.IsTrue(FactoryQueries.AnyStock(back.Run));
        }

        [Test]
        public void Commands_DeliverAndSell_RefuseWithoutFactories_StateUnchanged()
        {
            var state = StageProgressionService.NewGame(TestData.Balance, Configs, 7, 0, 1);
            var map = OutpostLevelAdapter.BuildMine(TestData.Balance, 1, Configs);
            var sim = new OutpostSimulation(state, map, Configs);
            string before = OutpostJson.Serialize(sim.State);
            Assert.AreEqual("unknown", sim.Submit(OutpostCommand.DeliverFactory(5)).Reason);
            Assert.AreEqual("no-factories", sim.Submit(OutpostCommand.SellGoods(null)).Reason);
            Assert.AreEqual("no-factories", sim.Submit(OutpostCommand.SellGoods("milk")).Reason);
            Assert.AreEqual(before, OutpostJson.Serialize(sim.State), "отказ ничего не меняет");
            Assert.IsNull(sim.FactoryById(5));
            Assert.AreEqual(0, sim.GoodsCount("milk"));
            Assert.AreEqual(BigInteger.Zero, sim.WarehouseValue());
            Assert.AreEqual("DeliverFactory t5", OutpostCommand.DeliverFactory(5).ToString());
            Assert.AreEqual("SellGoods all", OutpostCommand.SellGoods(null).ToString());
            Assert.AreEqual("SellGoods milk", OutpostCommand.SellGoods("milk").ToString());

            // Причины по состоянию (общие для ядра, контроллера и UI).
            var run = OutpostJson.DeepCopy(state.Run);
            run.FactoryRules = OutpostJson.DeepCopy(Configs.FactoryRules);
            run.Factories.Add(new FactoryState { TargetId = 9, TypeId = "amethyst", GoodId = "milk", Status = FactoryStatus.Locked });
            Assert.AreEqual("locked", FactoryQueries.DeliverBlockReason(run, 9));
            run.Factories[0].Status = FactoryStatus.Working;
            Assert.AreEqual("not-ready", FactoryQueries.DeliverBlockReason(run, 9));
            run.Factories[0].Status = FactoryStatus.Ready;
            run.Factories[0].ReadyUnits = 20;
            Assert.IsNull(FactoryQueries.DeliverBlockReason(run, 9));
            Assert.AreEqual("no-goods", FactoryQueries.SellBlockReason(run, null));
            Assert.AreEqual("no-goods", FactoryQueries.SellBlockReason(run, "milk"));
            Assert.AreEqual("unknown", FactoryQueries.SellBlockReason(run, "steel"));
            run.Warehouse["milk"] = 5;
            Assert.IsNull(FactoryQueries.SellBlockReason(run, null));
            Assert.IsNull(FactoryQueries.SellBlockReason(run, "milk"));
            run.Phase = RunPhase.ReadyToContinue;
            Assert.AreEqual("phase", FactoryQueries.SellBlockReason(run, null));
            Assert.AreEqual("phase", FactoryQueries.DeliverBlockReason(run, 9));
        }

        [Test]
        public void Readers_UnitsPerCycleAndTransit_FromRunSnapshotAndMapRoute()
        {
            var state = StageProgressionService.NewGame(TestData.Balance, Configs, 7, 0, 2);
            var sim = new OutpostSimulation(state, OutpostLevelAdapter.ForRun(TestData.Balance, state.Run), Configs);
            var f = sim.Map.Factories.Single();
            // ТЗ 2.2 §3.2 (WP1): CreateRun кладёт снимок правил в run; без снимка — 0: значения идущей шахты берутся только из него.
            Assert.IsNotNull(sim.Run.FactoryRules, "снимок factory_rules при создании шахты");
            Assert.AreEqual(Configs.FactoryRules.CanonicalJson(), sim.Run.FactoryRules.CanonicalJson());
            sim.Run.FactoryRules = null;
            Assert.AreEqual((0, 0L), (sim.FactoryUnitsPerCycle(f.TargetId), sim.FactoryTransitTicks(f.TargetId)));
            sim.Run.FactoryRules = OutpostJson.DeepCopy(Configs.FactoryRules);
            Assert.AreEqual(20, sim.FactoryUnitsPerCycle(f.TargetId), "UnitsPerCycle снимка; без копий карточек бонуса нет");
            Assert.IsTrue(f.HasRoute);
            Assert.AreEqual(FactoryMath.TransitTicks(Configs.FactoryRules, f.RouteSteps), sim.FactoryTransitTicks(f.TargetId));
            Assert.That(sim.FactoryTransitTicks(f.TargetId), Is.GreaterThanOrEqualTo(OutpostIds.TicksPerSecond), "не меньше MinTransitSec");
            int notFactory = sim.Map.Targets.First(t => !t.IsFactory).Id;
            Assert.AreEqual((0, 0L), (sim.FactoryUnitsPerCycle(notFactory), sim.FactoryTransitTicks(notFactory)), "не завод");
        }

        static string FindUp(string relative)
        {
            var dir = new DirectoryInfo(TestData.ProjectRoot);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
