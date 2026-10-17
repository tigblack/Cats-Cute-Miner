using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Outpost;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostKitA;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Конфигурации нового режима (ТЗ §18 «Храни отдельно», §12, §10; CORE_SPEC §3): файлы Resources совпадают со
    /// встроенными строками, hash не зависит от форматирования, значения соответствуют таблицам ТЗ.
    /// </summary>
    [TestFixture]
    public class OutpostConfigTests
    {
        static IEnumerable<string> Files() => ConfigFiles;

        // ───────────────────────────── Resources ⇔ встроенные строки ─────────────────────────────

        // ТЗ §18: «Конфигурации должны загружаться также в Android-плеере» — Runtime читает Resources, ядро — встроенные строки.
        [TestCaseSource(nameof(Files))]
        public void ResourcesFile_Exists_AndEqualsEmbeddedDefault(string fileName)
        {
            Assert.IsTrue(File.Exists(ResourcePath(fileName)), "нет файла " + ResourcePath(fileName));
            var file = JToken.Parse(ResourceText(fileName));
            var embedded = JToken.Parse(EmbeddedJson(fileName));
            Assert.IsTrue(JToken.DeepEquals(file, embedded),
                fileName + " в Resources расходится со встроенной строкой:\n" + file.ToString(Formatting.None) + "\n" + embedded.ToString(Formatting.None));
        }

        [Test]
        public void ResourcesSet_ParsesToSameHashesAsLoadDefault()
        {
            var fromFiles = ParseConfigs(ConfigFiles.Select(ResourceText).ToArray());
            CollectionAssert.AreEqual(Hashes(Configs), Hashes(fromFiles));
            Assert.AreEqual(EnvelopeCatalogHash(Configs), EnvelopeCatalogHash(fromFiles));
        }

        // ───────────────────────────── Hash ─────────────────────────────

        // CORE_SPEC §3: SHA-256 канонической сериализации разобранного объекта.
        [Test]
        public void Hashes_AreSha256Hex_AndDeterministic()
        {
            var a = OutpostConfigSet.LoadDefault();
            var b = OutpostConfigSet.LoadDefault();
            var c = ParseConfigs(DefaultTexts());
            foreach (var h in Hashes(a).Concat(new[] { EnvelopeCatalogHash(a) }))
                Assert.IsTrue(IsSha256Hex(h), "не SHA-256: " + h);
            CollectionAssert.AreEqual(Hashes(a), Hashes(b));
            CollectionAssert.AreEqual(Hashes(a), Hashes(c));
            Assert.AreEqual(4, Hashes(a).Distinct().Count(), "у четырёх разных конфигураций разные hash");
        }

        /// <summary>Тот же JSON в другом оформлении: переводы строк, отступы, порядок полей верхнего уровня.</summary>
        static string Reformat(string json, string variant)
        {
            switch (variant)
            {
                case "crlf": return json.Replace("\r\n", "\n").Replace("\n", "\r\n");
                case "lf": return json.Replace("\r\n", "\n");
                case "minified": return JToken.Parse(json).ToString(Formatting.None);
                case "indented": return JToken.Parse(json).ToString(Formatting.Indented).Replace("\n", "\n    ") + "\n\n";
                case "reordered":
                    {
                        // порядок полей класса задаёт канонический вид, а не порядок в файле
                        var src = JObject.Parse(json);
                        var dst = new JObject();
                        foreach (var p in src.Properties().Reverse()) dst.Add(p.Name, p.Value.DeepClone());
                        return dst.ToString(Formatting.Indented);
                    }
                default: throw new ArgumentException(variant);
            }
        }

        // CORE_SPEC §3: «переводы строк не влияют».
        [Test]
        public void Hashes_IndependentOfWhitespaceLineEndingsAndFieldOrder(
            [ValueSource(nameof(Files))] string fileName,
            [Values("crlf", "lf", "minified", "indented", "reordered")] string variant)
        {
            var edited = ParseConfigsWith(fileName, text => Reformat(text, variant));
            CollectionAssert.AreEqual(Hashes(Configs), Hashes(edited), fileName + " / " + variant);
            Assert.AreEqual(EnvelopeCatalogHash(Configs), EnvelopeCatalogHash(edited));
        }

        [Test]
        public void BalanceHash_ChangesWithCoefficient_OnlyBalanceAffected()
        {
            var edited = ParseConfigsWithBalance(j => j["HpBase"] = "21");
            var before = Hashes(Configs);
            var after = Hashes(edited);
            Assert.AreNotEqual(before[0], after[0], "hash баланса обязан измениться");
            CollectionAssert.AreEqual(before.Skip(1).ToArray(), after.Skip(1).ToArray(), "остальные конфигурации не изменились");
            Assert.AreEqual(EnvelopeCatalogHash(Configs), EnvelopeCatalogHash(edited), "hash каталога/наград не зависит от баланса");
        }

        [Test]
        public void CatalogAndRewardsHash_ChangeWithTheirValues()
        {
            var catalog = ParseConfigsWith("equipment_catalog.json", t => { var j = JObject.Parse(t); j["PartsCostBase"] = "7"; return j.ToString(); });
            Assert.AreNotEqual(Hashes(Configs)[1], Hashes(catalog)[1]);
            Assert.AreNotEqual(EnvelopeCatalogHash(Configs), EnvelopeCatalogHash(catalog));
            var rewards = ParseConfigsWith("stage_rewards.json", t => { var j = JObject.Parse(t); j["CompletionPartsBase"] = 11; return j.ToString(); });
            Assert.AreNotEqual(Hashes(Configs)[2], Hashes(rewards)[2]);
            Assert.AreNotEqual(EnvelopeCatalogHash(Configs), EnvelopeCatalogHash(rewards));
        }

        [Test]
        public void Parse_Garbage_Throws()
        {
            Assert.That(() => ParseConfigsWith("outpost_balance.json", _ => "{ not json"), Throws.Exception);
            Assert.That(() => ParseConfigsWith("stage_rewards.json", _ => ""), Throws.Exception);
        }

        // ТЗ §12.2: «Десятичные коэффициенты трактовать точно» — запись 1.18 в файле равна 59/50.
        // Значения профиля b4 в десятичной записи: шаг HP 0.04, шаг разгрузки 0.2, удорожание по глубине 0.1.
        [Test]
        public void Balance_DecimalSpellingInFile_ParsedExactly()
        {
            var edited = ParseConfigsWithBalance(j =>
            {
                j["ZoneScale"] = "1.20";
                j["Branches"][1]["Growth"] = "1.18";
                j["DamageGrowth"] = "1.22";
                j["ValueGrowth"] = "1.10";
                j["ZoneHpStep"] = "0.04";
                j["UnloadStep"] = "0.2";
                j["CostZoneStep"] = "0.1";
            });
            Assert.AreEqual("6/5", edited.Balance.ZoneScale.ToString());
            Assert.AreEqual("59/50", edited.Balance.Branches[1].Growth.ToString());
            Assert.AreEqual("61/50", edited.Balance.DamageGrowth.ToString());
            Assert.AreEqual("11/10", edited.Balance.ValueGrowth.ToString());
            Assert.AreEqual("1/25", edited.Balance.ZoneHpStep.ToString());
            Assert.AreEqual("1/5", edited.Balance.UnloadStep.ToString());
            Assert.AreEqual("1/10", edited.Balance.CostZoneStep.ToString());
            Assert.AreEqual(Hashes(Configs)[0], Hashes(edited)[0], "десятичная запись тех же значений — тот же профиль");
            foreach (int level in new[] { 0, 1, 9, 10, 19 })
                Assert.AreEqual(NextCost(144, UpgradeBranch.Pickaxes, level), NextCost(144, UpgradeBranch.Pickaxes, level, edited.Balance), "L" + level);
        }

        // ───────────────────────────── Версии ─────────────────────────────

        // ТЗ §17: b3 = b2 с общим пределом офлайна 3 часа; b4 = b3 после аудита баланса (удорожание по глубине, HP, разгрузка,
        // предел «Оценки» 40). ТЗ 2.2 §10.1 и §16.1 (б): b5 = числа b4 с новой версией; схема 3 (заводы; сохранения схемы < 3
        // не переносятся, а сбрасываются по §10.3), правила outpost-2.2-r1, adapter-2 (цепи без груза), эталон аудита —
        // профиль b5; cards-3, factories-1 и топология wide-branching-mines-3. Профиль b6 — b5 и множитель темпа шахт K(z)
        // (решение владельца В4, 15 октября 2026).
        [Test]
        public void Versions_MatchTz()
        {
            Assert.AreEqual("equip-1", Catalog.Version);
            Assert.AreEqual("rewards-1", Rewards.Version);
            Assert.AreEqual("outpost-2.2-b6", (string)Audit["profile_version"], "профиль аудита совпадает с версией баланса");
            Assert.AreEqual("CatOutpostV2", OutpostIds.ModeId);
            Assert.AreEqual(3, OutpostIds.SchemaVersion, "ТЗ 2.2 §10.1: схема 3 — заводы, трубы, склад (ledger платежей — с схемы 2)");
            Assert.AreEqual("outpost-2.2-r1", OutpostIds.RulesVersion);
            Assert.AreEqual("outpost-2.2-b6", OutpostIds.BalanceVersion);
            Assert.AreEqual("adapter-2", OutpostIds.AdapterVersion);
            Assert.AreEqual(1_000_000L, OutpostIds.TicksPerSecond);
            // ТЗ 2.2 §10.1: версии правил карточек, заводов и топологии (новые проверки, файлы — WP3, WP0/WP7, WP2).
            Assert.AreEqual("cards-4", Configs.CardRules.Version);
            Assert.AreEqual(CardRules.CurrentVersion, Configs.CardRules.Version);
            Assert.AreEqual("factories-1", Configs.FactoryRules.Version);
            Assert.AreEqual(FactoryRules.CurrentVersion, Configs.FactoryRules.Version);
            Assert.AreEqual("wide-branching-mines-3", MineTopologyCatalog.SupportedVersion);
            Assert.AreEqual(MineTopologyCatalog.SupportedVersion, OutpostConfigSet.LoadDefault().Topologies.Version);
            // Профиль в файле outpost_balance.json (WP7: «числа b4, меняется только Version»). b6 — решение владельца В4
            // (15 октября 2026): те же числа и множитель темпа шахт PaceScale (Docs/Rework/BALANCE_FACTORIES.md «Темп b6: 3 цикла»).
            Assert.AreEqual("outpost-2.2-b6", Balance.Version, "outpost_balance.json");
        }

        // ───────────────────────────── Баланс: значения ТЗ ─────────────────────────────

        // ТЗ §9 и §12.2: ветки, BaseCost, Growth, caps, пороги; профиль b4: «Оценка» cap 40, пороги 10/25/40.
        [TestCase(UpgradeBranch.Team, "Team", "50", "19/10", 10, new int[0])]
        [TestCase(UpgradeBranch.Pickaxes, "Pickaxes", "20", "59/50", 20, new[] { 10, 20 })]
        [TestCase(UpgradeBranch.Boots, "Boots", "25", "13/10", 15, new int[0])]
        [TestCase(UpgradeBranch.Bags, "Bags", "25", "33/25", 16, new int[0])]
        [TestCase(UpgradeBranch.Unload, "Unload", "20", "32/25", 20, new int[0])]
        [TestCase(UpgradeBranch.Value, "Value", "30", "31/25", 40, new[] { 10, 25, 40 })]
        public void Balance_BranchTable_MatchesTz(UpgradeBranch branch, string id, string baseCost, string growth, int cap, int[] milestones)
        {
            Assert.AreEqual(6, Balance.Branches.Count);
            var b = Balance.Branches[(int)branch];
            Assert.AreEqual(id, b.Id);
            Assert.AreEqual(baseCost, b.BaseCost.ToString());
            Assert.AreEqual(growth, b.Growth.ToString());
            Assert.AreEqual(cap, b.Cap);
            CollectionAssert.AreEqual(milestones, b.Milestones?.ToArray() ?? new int[0]);
        }

        [Test]
        public void Balance_BranchEnumOrder()
        {
            for (int i = 0; i < BranchIds.Length; i++)
                Assert.AreEqual(BranchIds[i], ((UpgradeBranch)i).ToString());
        }

        // ТЗ §6.4, §6.5, §9, §12.1, §13: время, работники, пределы.
        [Test]
        public void Balance_TimeAndTeamParameters_MatchTz()
        {
            Assert.AreEqual(1_000_000L, Balance.TicksPerSecond, "§6.5 микросекунды");
            Assert.AreEqual(1_000_000L, Balance.HitIntervalTicks, "§12.1 удар раз в 1 с");
            Assert.AreEqual(250_000L, Balance.PickupTicksPerUnit, "§6.5 подбор 0.25 с на порцию");
            Assert.AreEqual(3L * 3600 * 1000, Balance.OfflineCapMs, "§13 лимит офлайна 3 реальных часа");
            Assert.AreEqual(2, Balance.StartWorkers, "§9 два котика");
            Assert.AreEqual(4, Balance.MaxWorkersPerTarget, "§6.3 до четырёх на цель");
            Assert.AreEqual(3_000_000L, Balance.MoveStuckTicks, "§6.4 3 с без продвижения");
            Assert.AreEqual(60_000_000L, Balance.WatchdogTicks, "§6.4 60 с без изменений");
            Assert.AreEqual("6/5", Balance.ZoneScale.ToString(), "§12.1 S(z) = 1.20^(z−1)");
            Assert.AreEqual("1/10", Balance.CostZoneStep.ToString(), "профиль b4: CostZoneFactor(z) = 1 + (z−1)/10");
            Assert.AreEqual("2", Balance.MilestoneMultiplier.ToString(), "§12.2 2^count");
        }

        // ТЗ §12.2: эффекты веток без экипировки.
        [Test]
        public void Balance_StatCoefficients_MatchTz()
        {
            Assert.AreEqual("5", Balance.BaseDamage.ToString());
            Assert.AreEqual("61/50", Balance.DamageGrowth.ToString(), "1.22");
            Assert.AreEqual("5/2", Balance.BaseMoveSpeed.ToString(), "2.5 клетки/с");
            Assert.AreEqual("2/25", Balance.MoveSpeedStep.ToString(), "0.08");
            Assert.AreEqual(4, Balance.BaseCapacity);
            Assert.AreEqual(1, Balance.CapacityStep);
            Assert.AreEqual("2", Balance.BaseUnloadRate.ToString());
            Assert.AreEqual("1/5", Balance.UnloadStep.ToString(), "0.2 (профиль b4)");
            Assert.AreEqual("11/10", Balance.ValueGrowth.ToString(), "1.10");
        }

        // ТЗ §12.4: формула HP, груза и таблица типов; профиль b4: ZoneHpFactor = 1 + (z−1)/25.
        [Test]
        public void Balance_HpAndCargoParameters_MatchTz()
        {
            Assert.AreEqual("20", Balance.HpBase.ToString());
            Assert.AreEqual("1/25", Balance.ZoneHpStep.ToString(), "0.04 (профиль b4)");
            Assert.AreEqual("5", Balance.RankSpan.ToString());
            Assert.AreEqual("5/2", Balance.AreaFactorCap.ToString(), "min(2.5, sqrt(n))");
            Assert.AreEqual(7, Balance.AreaCapFromCells, "n ≥ 7 → 2.5");
            Assert.AreEqual(10, Balance.CargoHpDivisor);
            Assert.AreEqual(2, Balance.MinCargoUnits);
            Assert.AreEqual("5", Balance.CoinBase.ToString());
        }

        [TestCase("r", "1", "1")]
        [TestCase("x", "4/5", "1/2")]
        [TestCase("s", "2", "2")]
        [TestCase("p", "5/2", "1")]
        public void Balance_TypeTable_MatchesTz(string kind, string hpFactor, string valueFactor)
        {
            var t = Balance.Types[kind];
            Assert.AreEqual(hpFactor, t.HpFactor.ToString(), "TypeHpFactor " + kind);
            Assert.AreEqual(valueFactor, t.ValueFactor.ToString(), "BaseCoinValueFactor " + kind);
            if (kind == "x") Assert.AreEqual("1/2", t.FixedRank.ToString(), "§12.4: для всех x rank01 = 0.5");
        }

        [Test]
        public void Balance_TypeTable_HasNoCandE()
        {
            // ТЗ §12.4: «c и e не участвуют в этих формулах»
            CollectionAssert.AreEquivalent(new[] { "r", "x", "s", "p" }, Balance.Types.Keys.Select(k => k.ToString()));
        }

        // ───────────────────────────── Каталог экипировки ─────────────────────────────

        // ТЗ §12.3 и §10.3.
        [Test]
        public void Catalog_Coefficients_MatchTz()
        {
            CollectionAssert.AreEqual(new[] { "2/25", "9/50", "7/20", "3/5" }, Catalog.RarityBaseBonus.Select(r => r.ToString()), "0.08/0.18/0.35/0.60");
            Assert.AreEqual("1/20", Catalog.BonusPerLevel.ToString(), "0.05 за уровень");
            CollectionAssert.AreEqual(new[] { 1, 3, 6, 10 }, Catalog.RarityBaseCapacity);
            CollectionAssert.AreEqual(new[] { "1", "3/2", "5/2", "4" }, Catalog.RarityCostMultiplier.Select(r => r.ToString()));
            Assert.AreEqual("6", Catalog.PartsCostBase.ToString());
            Assert.AreEqual("117/100", Catalog.PartsCostGrowth.ToString());
            Assert.AreEqual(20, Catalog.MaxItemLevel);
        }

        // ───────────────────────────── Награды ─────────────────────────────

        // ТЗ §10.2, §12.3.
        [Test]
        public void Rewards_Values_MatchTz()
        {
            Assert.AreEqual(1, Rewards.RewardRngVersion, "rewardRngVersion = 1");
            Assert.AreEqual(5, Rewards.RewardRngStream, "отдельный поток 5");
            var guaranteed = new Dictionary<int, string>();
            foreach (var kv in Rewards.Guaranteed) guaranteed[Convert.ToInt32(kv.Key)] = kv.Value;
            var expected = new Dictionary<int, string>
            {
                [1] = "pickaxe.common", [2] = "hat.common", [3] = "backpack.common", [4] = "boots.common",
                [10] = "pickaxe.rare", [25] = "pickaxe.epic", [50] = "pickaxe.legendary",
            };
            CollectionAssert.AreEquivalent(expected, guaranteed);
            CollectionAssert.AreEqual(new[] { 70, 94, 99, 100 }, Rewards.RarityThresholds, "70% / 24% / 5% / 1%");
            Assert.AreEqual(10, Rewards.CompletionPartsBase);
            Assert.AreEqual(5, Rewards.CompletionPartsPerFive);
            CollectionAssert.AreEqual(DuplicatePartsByRarity, Rewards.DuplicateParts, "5 / 15 / 40 / 120");
        }

        // ───────────────────────────── Overrides ─────────────────────────────

        // ТЗ §8.5: исходной карте overrides не понадобились (level_overrides_required_by_static_check = []); единственный override —
        // осознанное заполнение арены zone1 (ТЗ 8.5, решение владельца продукта), именованный и проверенный аудитом независимо.
        [Test]
        public void Overrides_OnlyNamedZone1Arena_MatchAudit()
        {
            Assert.AreEqual(0, ((JArray)Audit["level_overrides_required_by_static_check"]).Count);
            Assert.AreEqual("overrides-2", Overrides.Version);
            CollectionAssert.AreEquivalent(new[] { "zone1" }, Overrides.Zones.Keys);
            var o = Overrides.For("zone1");
            Assert.AreEqual("zone1-full-arena-1", o.Name);
            Assert.That(o.Reason, Is.Not.Empty);
            var audit = (JObject)Audit["level_overrides"]["zone1"];
            Assert.AreEqual(o.Name, (string)audit["name"], "аудит проверял этот же override");
            Assert.AreEqual((int)audit["added_tokens"], o.AddTokens.Count);
            Assert.AreEqual(36, o.AddTokens.Count);
            Assert.AreEqual((string)Audit["level_overrides_version"], Overrides.Version);
        }
    }
}
