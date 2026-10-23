using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CatMiners.Core.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// Правила загрузки и валидатора (ТЗ 3.1, 5.1, C.1): ровно один Miners с Id tinygoblin, целые веса,
    /// инвариант «G = 0 у применяемых FT0», ID котиков и валют. JSON портится копией в памяти, загрузка — без проверки hash.
    /// </summary>
    public class DataValidatorFixTests
    {
        static JObject s_Source;

        /// <summary>Исходный JSON деревом: даты не распознаются, дробные — double.</summary>
        static JObject Source
        {
            get
            {
                if (s_Source != null) return s_Source;
                var text = new UTF8Encoding(false, true).GetString(TestData.BalanceBytes);
                using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
                    s_Source = JObject.Load(reader);
                return s_Source;
            }
        }

        static byte[] Mutated(Action<JObject> mutate)
        {
            var root = (JObject)Source.DeepClone();
            mutate(root);
            return Encoding.UTF8.GetBytes(root.ToString(Formatting.None));
        }

        static BalanceRepository LoadMutated(Action<JObject> mutate) => BalanceRepository.Load(Mutated(mutate), requireExpectedHash: false);

        static JObject ById(JObject root, string section, string id, string idField = "Id") =>
            ((JArray)root[section]).Cast<JObject>().First(x => (string)x[idField] == id);

        static List<ValidationIssue> Errors(BalanceRepository b) => BalanceValidator.Validate(b).Issues.Where(i => !i.IsWarning).ToList();

        static void AssertErrorAt(List<ValidationIssue> errors, string path)
        {
            Assert.IsTrue(errors.Any(e => e.Path == path), "нет ошибки по пути " + path + ". Ошибки:\n" + string.Join("\n", errors.Select(e => e.ToString())));
        }

        // ───────────── ТЗ 5.1: Miners — ровно один элемент с Id tinygoblin ─────────────

        [Test]
        public void TwoMinersIsLoadErrorNamingMiners()
        {
            var second = LoadMutatedThrows(root =>
            {
                var copy = (JObject)root["Miners"][0].DeepClone();
                copy["Id"] = "biggoblin";
                ((JArray)root["Miners"]).Add(copy);
            });
            StringAssert.Contains("Miners", second.Message);

            var duplicate = LoadMutatedThrows(root => ((JArray)root["Miners"]).Add(root["Miners"][0].DeepClone()));
            StringAssert.Contains("Miners", duplicate.Message, "два tinygoblin — тоже не ровно один");

            var empty = LoadMutatedThrows(root => root["Miners"] = new JArray());
            StringAssert.Contains("Miners", empty.Message);

            var missing = LoadMutatedThrows(root => root.Remove("Miners"));
            StringAssert.Contains("Miners", missing.Message);

            var other = LoadMutatedThrows(root => root["Miners"][0]["Id"] = "biggoblin");
            StringAssert.Contains("Miners", other.Message);
            StringAssert.Contains(BalanceRepository.MinerId, other.Message, "ошибка называет ожидаемый Id");
        }

        static BalanceLoadException LoadMutatedThrows(Action<JObject> mutate)
        {
            var bytes = Mutated(mutate);
            return Assert.Throws<BalanceLoadException>(() => BalanceRepository.Load(bytes, requireExpectedHash: false));
        }

        [Test]
        public void SourceHasSingleTinygoblinMiner()
        {
            Assert.AreEqual(1, TestData.Balance.Root.Miners.Length);
            Assert.AreEqual(BalanceRepository.MinerId, TestData.Balance.Miner.Id);
            Assert.AreSame(TestData.Balance.Root.Miners[0], TestData.Balance.Miner);
        }

        // ───────────── ТЗ 5.1: целые значения ─────────────

        [Test]
        public void NonWholeWeightsAreValidatorErrorsAtTheirPaths()
        {
            string deliveryId = (string)Source["Deliveries"][0]["Id"];
            string deliveryId2 = (string)Source["Deliveries"][1]["Id"];
            string deliveryId3 = (string)Source["Deliveries"][2]["Id"];
            var b = LoadMutated(root =>
            {
                ById(root, "Deliveries", deliveryId)["Weight"] = 1.5;
                ById(root, "Deliveries", deliveryId2)["Weight"] = -2;
                var g = ById(root, "Gacha", "GachaGolden");
                g["UncommonWeight"] = 2.25;
                g["RareWeight"] = -1;
                g["EventEpicWeight"] = 0.5;
            });
            // NaN и ∞ в JSON по-разному читаются разными рантаймами — подставляются прямо в DTO загруженной копии.
            b.Gacha["GachaGolden"].LegendaryWeight = double.PositiveInfinity;
            b.Deliveries[deliveryId3].Weight = double.NaN;
            var errors = Errors(b);
            AssertErrorAt(errors, "Deliveries." + deliveryId + ".Weight");
            AssertErrorAt(errors, "Deliveries." + deliveryId2 + ".Weight");
            AssertErrorAt(errors, "Deliveries." + deliveryId3 + ".Weight");
            AssertErrorAt(errors, "Gacha.GachaGolden.UncommonWeight");
            AssertErrorAt(errors, "Gacha.GachaGolden.RareWeight");
            AssertErrorAt(errors, "Gacha.GachaGolden.LegendaryWeight");
            AssertErrorAt(errors, "Gacha.GachaGolden.EventEpicWeight");
            Assert.AreEqual(7, errors.Count, "других ошибок порча весов не даёт:\n" + string.Join("\n", errors));
        }

        [Test]
        public void SourceWeightsAreWholeAndNonNegative()
        {
            foreach (var d in TestData.Balance.Deliveries.Values)
                Assert.IsTrue(d.Weight >= 0 && d.Weight == Math.Floor(d.Weight), "Deliveries." + d.Id + ".Weight = " + d.Weight);
            foreach (var g in TestData.Balance.Gacha.Values)
                foreach (var w in new[] { g.UncommonWeight, g.RareWeight, g.LegendaryWeight, g.EventEpicWeight })
                    Assert.IsTrue(w >= 0 && w == Math.Floor(w), "Gacha." + g.Id + ": вес " + w);
        }

        /// <summary>Поля int (счётчики и коды) целость проверяют уже при загрузке: дробный литерал — ошибка с JSON-путём.</summary>
        [Test]
        public void FractionalLiteralInIntFieldIsLoadErrorWithPath()
        {
            var cases = new (Action<JObject> mutate, string path)[]
            {
                (r => r["Deliveries"][0]["MaxDupes"] = 1.5, "Deliveries[0].MaxDupes"),
                (r => r["BalanceProperties"][0]["BaseUnitCap"] = 10.5, "BalanceProperties[0].BaseUnitCap"),
                (r => r["FreeGachaCycle"][0]["MaxFreeGachas"] = 2.5, "FreeGachaCycle[0].MaxFreeGachas"),
                (r => r["GeneratorObjectives"][0]["ObjectiveCount"][0] = 9.5, "GeneratorObjectives[0].ObjectiveCount[0]"),
            };
            foreach (var (mutate, path) in cases)
            {
                var e = LoadMutatedThrows(mutate);
                StringAssert.Contains(path, e.Message, "ошибка загрузки содержит путь поля");
            }
        }

        // ───────────── C.1: G = 0 у всех применяемых в v1 записей FT0 ─────────────

        [Test]
        public void NonZeroGrowthOrNonFt0InAppliedRecordsAreValidatorErrors()
        {
            var b = LoadMutated(root =>
            {
                root["SpawningCart"][0]["CurrencyOutputGrowth"] = 0.5;
                root["SpawningCart"][0]["SpawnDelaySecFormulaType"] = 1;
                root["SpawningCart"][0]["GenerationDelaySecGrowth"] = 0.1;
                root["Reinforcements"][0]["LevelGrowth"] = 0.01;
                ById(root, "Rocks", "rockbasic")["SoftCurrencyGrowth"] = 1;
                ById(root, "GeneratorObjectives", "amethyst", "GeneratorId")["SoftCurrencyRewardGrowth"] = 2;
                ById(root, "MineShafts", "citrine")["GenerationDelaySecGrowth"] = 3;
                root["Miners"][0]["AttackDelaySecGrowth"] = 0.5;
                root["Miners"][0]["CritChanceFormulaType"] = 2;
                root["Spells"][0]["CritPowerGrowth"] = 1;
                var cp = root["CheckPoint"][0];
                cp["ModifierGrowth"][1] = 0.5;
                cp["ModifierFormulaType"][2] = 1;
                cp["ModifierMultiplier"][3] = 1;
                ById(root, "Cards", "ca038")["ModifierGrowth"] = 0.5;   // код 1, FT0 — применяется
                ById(root, "Cards", "ca034")["ModifierGrowth"] = 5;     // код 10, FT1 — G законно не 0
            });
            var errors = Errors(b);
            var expected = new[]
            {
                "SpawningCart[0].CurrencyOutputGrowth",
                "SpawningCart[0].SpawnDelaySecFormulaType",
                "SpawningCart[0].GenerationDelaySecGrowth",
                "Reinforcements[0].LevelGrowth",
                "Rocks.rockbasic.SoftCurrencyGrowth",
                "GeneratorObjectives.amethyst.SoftCurrencyRewardGrowth",
                "MineShafts.citrine.GenerationDelaySecGrowth",
                "Miners.tinygoblin.AttackDelaySecGrowth",
                "Miners.tinygoblin.CritChanceFormulaType",
                "Spells[0].CritPowerGrowth",
                "CheckPoint[0].ModifierGrowth[1]",
                "CheckPoint[0].ModifierFormulaType[2]",
                "CheckPoint[0].ModifierMultiplier[3]",
                "Cards.ca038.ModifierGrowth",
            };
            foreach (var path in expected) AssertErrorAt(errors, path);
            CollectionAssert.AreEquivalent(expected, errors.Select(e => e.Path).ToArray(), "ровно по одной ошибке на испорченное поле");
        }

        /// <summary>Инварианты C.1 №1–2: UpgradeCostMultiplier = 0 у генераторов; B = 0 у Miners.Damage, Reinforcements.Cost и manager-карт.</summary>
        [Test]
        public void UpgradeMultiplierAndZeroBaseInvariantsAreValidatorErrors()
        {
            var b = LoadMutated(root =>
            {
                root["SpawningCart"][0]["UpgradeCostMultiplier"] = 1;
                ById(root, "MineShafts", "amethyst")["UpgradeCostMultiplier"] = 2;
                root["Miners"][0]["DamageBase"] = 1;
                root["Reinforcements"][0]["CostBase"] = 1;
                ById(root, "Cards", "ca001")["ModifierBase"] = 1;
            });
            Assert.IsTrue(b.Cards["ca001"].IsManager, "ca001 — manager-карта");
            var errors = Errors(b);
            var expected = new[]
            {
                "SpawningCart[0].UpgradeCostMultiplier",
                "MineShafts.amethyst.UpgradeCostMultiplier",
                "Miners.tinygoblin.DamageBase",
                "Reinforcements[0].CostBase",
                "Cards.ca001.ModifierBase",
            };
            CollectionAssert.AreEquivalent(expected, errors.Select(e => e.Path).ToArray(), string.Join("\n", errors));
        }

        [Test]
        public void CheckpointArraysMustMatchStatModifierTypeLength()
        {
            var b = LoadMutated(root =>
            {
                var cp = root["CheckPoint"][0];
                ((JArray)cp["ModifierPower"]).RemoveAt(0);
                cp["ModifierRound"] = null;
            });
            var errors = Errors(b);
            AssertErrorAt(errors, "CheckPoint[0].ModifierPower");
            AssertErrorAt(errors, "CheckPoint[0].ModifierRound");
        }

        /// <summary>Допустимые не-FT0 записи (C.1): DeliveryDelaySec — FT2 с M = 1, урон Spells — FT1 с B = 750 — не нарушения.</summary>
        [Test]
        public void LegitimateNonFt0RecordsAreNotFlagged()
        {
            var p = TestData.Balance.Props;
            Assert.AreEqual(2, p.DeliveryDelaySecFormulaType);
            Assert.AreEqual(1.0, p.DeliveryDelaySecMultiplier);
            Assert.AreEqual(1, TestData.Balance.Spell.EffectFormulaType);
            Assert.AreEqual(750.0, TestData.Balance.Spell.EffectBase);
            var report = BalanceValidator.Validate(TestData.Balance);
            Assert.AreEqual(0, report.ErrorCount, report.ToText());
            Assert.IsFalse(report.Issues.Any(i => i.Path.StartsWith("BalanceProperties[0].DeliveryDelaySec", StringComparison.Ordinal) ||
                                                   i.Path.StartsWith("Spells[0].Effect", StringComparison.Ordinal)));
        }

        // ───────────── ТЗ 3.1: поля «в области» читаются и проверяются ─────────────

        [Test]
        public void MinerIdCycleEntriesMustBeMinersIds()
        {
            var b = LoadMutated(root =>
            {
                root["Reinforcements"][0]["MinerIdCycle"] = new JArray("tinygoblin", "nosuchminer");
                root["SpawningCart"][0]["MinerIdCycle"] = new JArray("");
            });
            var errors = Errors(b);
            AssertErrorAt(errors, "Reinforcements[0].MinerIdCycle[1]");
            AssertErrorAt(errors, "SpawningCart[0].MinerIdCycle[0]");
            Assert.IsFalse(errors.Any(e => e.Path == "Reinforcements[0].MinerIdCycle[0]"), "tinygoblin — допустимый Id");

            var empty = Errors(LoadMutated(root => root["SpawningCart"][0]["MinerIdCycle"] = new JArray()));
            AssertErrorAt(empty, "SpawningCart[0].MinerIdCycle");
        }

        [Test]
        public void DeliveryRewardDetailedTypeMustMatchRewardType()
        {
            var deliveries = ((JArray)Source["Deliveries"]).Cast<JObject>().ToList();
            string minerDelivery = (string)deliveries.First(d => (int)d["RewardModel"]["RewardType"] == RewardTypes.Miner)["Id"];
            string currencyDelivery = (string)deliveries.First(d => (int)d["RewardModel"]["RewardType"] == RewardTypes.Currency)["Id"];
            var b = LoadMutated(root =>
            {
                ById(root, "Deliveries", minerDelivery)["RewardModel"]["DetailedType"] = "biggoblin";
                ById(root, "Deliveries", currencyDelivery)["RewardModel"]["DetailedType"] = "Hard";
            });
            var errors = Errors(b);
            AssertErrorAt(errors, "Deliveries." + minerDelivery + ".RewardModel.DetailedType");
            AssertErrorAt(errors, "Deliveries." + currencyDelivery + ".RewardModel.DetailedType");
            Assert.AreEqual(2, errors.Count, string.Join("\n", errors));
        }

        [Test]
        public void CurrencyIdsMustBeGoldAndElixir()
        {
            Assert.AreEqual(BalanceValidator.CoreCurrencyIdExpected, TestData.Balance.Props.CoreCurrencyId);
            Assert.AreEqual(BalanceValidator.SoftCurrencyIdExpected, TestData.Balance.Props.SoftCurrencyId);

            var wrong = Errors(LoadMutated(root =>
            {
                root["BalanceProperties"][0]["CoreCurrencyId"] = "coins";
                root["BalanceProperties"][0]["SoftCurrencyId"] = "";
            }));
            AssertErrorAt(wrong, "BalanceProperties[0].CoreCurrencyId");
            AssertErrorAt(wrong, "BalanceProperties[0].SoftCurrencyId");

            var same = Errors(LoadMutated(root => root["BalanceProperties"][0]["SoftCurrencyId"] = "gold"));
            Assert.IsTrue(same.Any(e => e.Path == "BalanceProperties[0].SoftCurrencyId" && e.Message.Contains("CoreCurrencyId")),
                "одинаковые ID валют — ошибка:\n" + string.Join("\n", same));
        }
    }
}
