using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Генераторы аудита баланса (Docs/Rework/BALANCE_AUDIT.md, Docs/Rework/BALANCE_FACTORIES.md). «before» — снимок конфигураций
    /// до правок b4 (Docs/Reports/balance-audit/configs/before), «after» — действующие файлы Assets/Game/Resources/Outpost, которые
    /// читает игра. Одинаковые модели игрока, seed и карты; разбор — Tools/BalanceAudit/analyze.py и factories.py.
    /// ТЗ 2.2 §9: Audit_After пишет замер профиля b5 с заводами в Docs/Reports/balance-factories/b5/ (эталон b4 — прежний
    /// Docs/Reports/balance-audit/after/ — не перезаписывается); у free seed 1 — опыты отсутствия 1 и 3 ч.
    /// </summary>
    [Explicit("Генератор отчёта аудита баланса: несколько минут")]
    public sealed class OutpostBalanceAuditTests
    {
        public static readonly ulong[] LuckSeeds = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        public static readonly int[] SaturationZones = { 1, 10, 25, 50, 75, 100, 125, 144 };
        public static readonly int[] TraceZones = { 1, 2, 5, 10, 25, 50, 75, 100, 125, 144 };

        /// <summary>Опыт отсутствия ТЗ 2.2 §9: 1 и 3 ч с 50 % длительности шахты (free seed 1).</summary>
        public static readonly double[] AbsenceHours = { 1, 3 };

        static string ConfigDir(string name) => Path.Combine(TestData.ProjectRoot, "Docs", "Reports", "balance-audit", "configs", name);
        static string ResourcesDir => Path.Combine(TestData.ProjectRoot, "Assets", "Game", "Resources", "Outpost");
        static string AuditRoot => Path.Combine(TestData.ProjectRoot, "Docs", "Reports", "balance-audit");
        static string FactoriesRoot => Path.Combine(TestData.ProjectRoot, "Docs", "Reports", "balance-factories");

        /// <summary>
        /// Конфигурации каталога dir: шесть файлов b4 и, если есть, store_catalog.json и factory_rules.json (ТЗ 2.2 §3.2); без них —
        /// встроенные копии. Набор с карточками и каталогом заводов.
        /// </summary>
        public static OutpostConfigSet LoadConfigs(string dir, BalanceRepository repo) => ParseConfigs(dir, (file, text) => text, repo);

        static OutpostConfigSet ParseConfigs(string dir, Func<string, string, string> patch, BalanceRepository repo)
        {
            string Read(string file, string fallback = null)
            {
                string path = Path.Combine(dir, file);
                return patch(file, File.Exists(path) || fallback == null ? File.ReadAllText(path) : fallback);
            }
            return OutpostConfigSet.Parse(Read("outpost_balance.json"), Read("equipment_catalog.json"), Read("stage_rewards.json"),
                Read("level_overrides.json"), Read("card_rules.json"), Read("mine_topology.json"),
                Read("store_catalog.json", OutpostConfigSet.DefaultStoreJson), Read("factory_rules.json", OutpostConfigSet.DefaultFactoryRulesJson))
                .WithCards(repo);
        }

        /// <summary>
        /// factory_rules с другим PriceRate по стартовому правилу §3.2: PriceFactor[t] = PriceRate × CycleSec[t] / UnitsPerCycle[t]
        /// (как Tools/Factories/gen_factory_rules.py --price-rate). Перебор калибровки §9.
        /// </summary>
        public static string WithPriceRate(string factoryRulesJson, string priceRate)
        {
            var rules = FactoryRules.Parse(factoryRulesJson);
            var rate = Rational.Parse(priceRate);
            rules.PriceRate = rate;
            foreach (var g in rules.Goods) g.PriceFactor = rate * g.CycleSec / g.UnitsPerCycle;
            rules.Validate();
            return OutpostJson.Serialize(rules);
        }

        /// <summary>
        /// Медиана калибровки §9: медиана длительностей шахт 2–5 в каждом seed, затем медиана по seed (мин); NaN — шахт нет.
        /// </summary>
        public static double CalibrationMedian(IEnumerable<CampaignAudit> free)
        {
            var perSeed = new List<double>();
            foreach (var a in free)
            {
                var m = a.Mines.Where(x => x.Zone >= 2 && x.Zone <= 5 && x.CompletedSec != null).Select(x => x.CompletedSec.Value / 60).ToList();
                if (m.Count == 4) perSeed.Add(Median(m));
            }
            return perSeed.Count > 0 ? Median(perSeed) : double.NaN;
        }

        /// <summary>Медиана как в Tools/BalanceAudit (statistics.median): среднее двух средних при чётном числе.</summary>
        public static double Median(IEnumerable<double> values)
        {
            var v = values.OrderBy(x => x).ToList();
            if (v.Count == 0) return double.NaN;
            return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
        }

        [Test]
        public void Audit_Before() => Generate("before", ConfigDir("before"), AuditRoot, true, null);

        /// <summary>
        /// Действующий профиль (b5 с заводами, ТЗ 2.2 §9) → Docs/Reports/balance-factories/b5/. Эталон b4 в
        /// Docs/Reports/balance-audit/after/ не перезаписывается: ядро 2.1 (цепи с грузом) им больше не воспроизвести.
        /// Профиль b6 (решение владельца В4, 15 октября 2026) — числа b5 и множитель темпа шахт K(z): 24 кампании по ~48 дней
        /// игрового времени этот генератор не уложит в часы, поэтому он меряет профиль без множителя (WithoutPace = числа b5),
        /// а темп b6 — Audit_Pace (шахты из записанного начала).
        /// </summary>
        [Test]
        public void Audit_After()
        {
            // Игра читает Resources; встроенные копии ядра должны совпадать с ними (иначе тесты мерили бы другое).
            var live = LoadConfigs(ResourcesDir, TestData.Balance);
            var embedded = OutpostConfigSet.LoadDefault().WithCards(TestData.Balance);
            Assert.AreEqual(live.BalanceHash, embedded.BalanceHash, "OutpostConfigSet.Defaults.cs устарел: python3 Tools/gen_outpost_config_defaults.py");
            Assert.AreEqual(live.CatalogHash, embedded.CatalogHash, "OutpostConfigSet.Defaults.cs устарел: python3 Tools/gen_outpost_config_defaults.py");
            Assert.AreEqual(live.FactoryRulesHash, embedded.FactoryRulesHash, "OutpostConfigSet.Defaults.cs устарел: python3 Tools/gen_outpost_config_defaults.py");
            Generate("b5", ResourcesDir, FactoriesRoot, false, AbsenceHours, withoutPace: true);
        }

        /// <summary>
        /// Перебор вариантов: Docs/Reports/balance-audit/tune-variants.json — список {Name, Base ("before"|"after"), Scenarios,
        /// Seeds, ToZone, Balance/Cards/Equipment/Rewards/Factories — merge-патчи JSON, PriceRate — правило §3.2}.
        /// Итог — tune/{Name}/campaign-*.json для analyze.py.
        /// </summary>
        [Test]
        public void Audit_Tune() => Tune(AuditRoot);

        /// <summary>
        /// Калибровка PriceRate (ТЗ 2.2 §9): Docs/Reports/balance-factories/tune-variants.json, итог — balance-factories/tune/{Name}/;
        /// в выводе — медиана шахт 2–5 модели free (медиана по шахтам в seed, затем по seed). Разбор —
        /// Tools/BalanceAudit/factories.py calibrate.
        /// </summary>
        [Test]
        public void Audit_TuneFactories() => Tune(FactoriesRoot);

        static void Tune(string root)
        {
            var variants = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(Path.Combine(root, "tune-variants.json")));
            var bytes = TestData.BalanceBytes;
            var jobs = new List<(Newtonsoft.Json.Linq.JObject V, string Scenario, ulong Seed)>();
            foreach (Newtonsoft.Json.Linq.JObject v in variants)
            {
                if (v.Value<bool?>("Skip") == true) continue;
                var scenarios = v["Scenarios"]?.ToObject<string[]>() ?? new[] { "free", "idle", "active" };
                var seeds = v["Seeds"]?.ToObject<ulong[]>() ?? new ulong[] { 1 };
                foreach (var sc in scenarios)
                    foreach (var seed in seeds) jobs.Add((v, sc, seed));
            }
            var failed = new List<string>();
            var free = new Dictionary<string, List<CampaignAudit>>();
            var sw = Stopwatch.StartNew();
            Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, i =>
            {
                var (v, scenario, seed) = jobs[i];
                string name = v.Value<string>("Name");
                string dir = ConfigDir(v.Value<string>("Base") ?? "before");
                if ((v.Value<string>("Base") ?? "before") == "after") dir = ResourcesDir;
                var keys = new Dictionary<string, string>
                {
                    ["outpost_balance.json"] = "Balance", ["equipment_catalog.json"] = "Equipment", ["stage_rewards.json"] = "Rewards",
                    ["level_overrides.json"] = "Overrides", ["card_rules.json"] = "Cards", ["factory_rules.json"] = "Factories",
                };
                string Patched(string file, string text)
                {
                    if (keys.TryGetValue(file, out var key) && v[key] is Newtonsoft.Json.Linq.JObject patch)
                    {
                        var doc = Newtonsoft.Json.Linq.JObject.Parse(text);
                        doc.Merge(patch, new Newtonsoft.Json.Linq.JsonMergeSettings { MergeArrayHandling = Newtonsoft.Json.Linq.MergeArrayHandling.Merge });
                        text = doc.ToString();
                    }
                    // ТЗ 2.2 §9: один параметр PriceRate, PriceFactor всех товаров — по стартовому правилу §3.2.
                    if (file == "factory_rules.json" && v.Value<string>("PriceRate") is string rate) text = WithPriceRate(text, rate);
                    return text;
                }
                var repo = BalanceRepository.Load(bytes);
                var configs = ParseConfigs(dir, Patched, repo);
                if (v.Value<bool?>("NoCards") == true)
                    throw new NotSupportedException("NoCards: с ТЗ 2.2 каталог заводов строится вместе с карточками");
                int toZone = v.Value<int?>("ToZone") ?? OutpostIds.LastZone;
                var audit = BalanceAudit.RunCampaign(repo, configs, BalanceAudit.Model(scenario), seed, toZone);
                if (scenario != "free" || seed != 1)
                    foreach (var m in audit.Mines) m.PurchaseLog = null;
                string outDir = Path.Combine(root, "tune", name);
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "campaign-" + scenario + "-seed" + seed + ".json"), OutpostJson.Serialize(audit) + "\n");
                TestContext.Progress.WriteLine("tune " + name + " " + scenario + "/" + seed + ": " + (audit.Success ? "ok" : "FAILED") + " " +
                    (audit.TotalSec / 3600).ToString("0.00") + " h");
                if (!audit.Success) lock (failed) failed.Add(name + "/" + scenario + "/" + seed);
                if (scenario == "free")
                    lock (free)
                    {
                        if (!free.TryGetValue(name, out var list)) free[name] = list = new List<CampaignAudit>();
                        list.Add(audit);
                    }
            });
            // Калибровка §9: медиана шахт 2–5 (сначала по шахтам в seed, затем по seed), цель 4,22–5,70 мин.
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var kv in free.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var mines = Enumerable.Range(2, 4).Select(z => Median(kv.Value.SelectMany(a => a.Mines).Where(m => m.Zone == z && m.CompletedSec != null)
                    .Select(m => m.CompletedSec.Value / 60))).ToList();
                double maxUnaffordable = kv.Value.SelectMany(a => a.Mines).Select(m => m.MaxUnaffordableSec).DefaultIfEmpty(0).Max();
                TestContext.Progress.WriteLine(string.Format(ic, "calibration {0}: PriceRate {1}, seeds {2}, median 2-5 = {3:0.000} min; mines 2/3/4/5 = {4}; max unaffordable {5:0} s",
                    kv.Key, kv.Value[0].PriceRate, kv.Value.Count, CalibrationMedian(kv.Value),
                    string.Join(" / ", mines.Select(x => x.ToString("0.00", ic))), maxUnaffordable));
            }
            TestContext.Progress.WriteLine("tune wall " + sw.Elapsed);
            Assert.IsEmpty(failed);
        }

        /// <summary>
        /// Калибровка темпа b6 (решение владельца В4, Docs/Rework/BALANCE_FACTORIES.md «Темп b6: 3 цикла»): кампания модели
        /// (по умолчанию free seed 1) на профиле без множителя темпа (K = 1, числа b5) записывает начало шахт выборки; каждая
        /// шахта выборки проходится заново (<see cref="BalanceAudit.RunMineFrom"/>) на действующем профиле или на вариантах
        /// PaceScale из balance-factories/pace/pace-variants.json. Итог — balance-factories/pace/&lt;вариант&gt;.json, разбор —
        /// Tools/BalanceAudit/pace_scale.py report.
        /// </summary>
        [Test]
        public void Audit_Pace()
        {
            string root = Path.Combine(FactoriesRoot, "pace");
            var spec = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(root, "pace-variants.json")));
            var zones = spec["Zones"].ToObject<int[]>();
            ulong seed = spec.Value<ulong?>("Seed") ?? 1;
            string modelName = spec.Value<string>("Model") ?? "free";
            var bytes = TestData.BalanceBytes;
            var baseRepo = BalanceRepository.Load(bytes);
            var baseConfigs = LoadConfigs(ResourcesDir, baseRepo).WithoutPace();
            var starts = new Dictionary<int, OutpostGameState>();
            var sw = Stopwatch.StartNew();
            var baseAudit = BalanceAudit.RunCampaign(baseRepo, baseConfigs, BalanceAudit.Model(modelName), seed, zones.Max(),
                beforeZone: (z, st) => { if (zones.Contains(z)) starts[z] = OutpostJson.DeepCopy(st); });
            double baseWall = sw.Elapsed.TotalSeconds;
            Assert.IsTrue(baseAudit.Success, "кампания K = 1 не прошла");
            TestContext.Progress.WriteLine("pace: base campaign " + modelName + "/" + seed + " to zone " + zones.Max() + " wall " + baseWall.ToString("0.0") + " s");

            var variants = new List<Newtonsoft.Json.Linq.JObject>();
            foreach (Newtonsoft.Json.Linq.JObject v in (Newtonsoft.Json.Linq.JArray)spec["Variants"])
                if (v.Value<bool?>("Skip") != true) variants.Add(v);
            var jobs = new List<(Newtonsoft.Json.Linq.JObject V, int Zone)>();
            foreach (var v in variants)
                foreach (int z in v["Zones"]?.ToObject<int[]>() ?? zones) jobs.Add((v, z));
            var rows = new object[jobs.Count];
            Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, i =>
            {
                var (v, z) = jobs[i];
                var repo = BalanceRepository.Load(bytes);
                var configs = LoadConfigs(ResourcesDir, repo);
                if (v["PaceScale"] is Newtonsoft.Json.Linq.JArray table)
                {
                    var b = configs.Balance.Clone();
                    b.PaceScale = OutpostJson.Deserialize<List<Rational>>(table.ToString());
                    configs = configs.WithBalance(b);
                }
                var mineWatch = Stopwatch.StartNew();
                var report = BalanceAudit.RunMineFrom(starts[z], repo, configs, BalanceAudit.Model(modelName));
                double wall = mineWatch.Elapsed.TotalSeconds;
                rows[i] = new { Variant = v.Value<string>("Name"), Zone = z, K = configs.Balance.ZonePace(z).ToString(), WallSec = wall,
                    BaseSec = baseAudit.Mines[z - 1].CompletedSec, Base = baseAudit.Mines[z - 1], Mine = report };
                TestContext.Progress.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "pace {0} zone {1}: K {2}, {3} in {4:0.00} h (base {5:0.0} min), wall {6:0.0} s", v.Value<string>("Name"), z,
                    configs.Balance.ZonePace(z), report.Outcome, (report.CompletedSec ?? 0) / 3600, (baseAudit.Mines[z - 1].CompletedSec ?? 0) / 60, wall));
            });
            foreach (var v in variants)
            {
                string name = v.Value<string>("Name");
                var mine = jobs.Select((j, i) => (j, i)).Where(x => x.j.V == v).Select(x => rows[x.i]).ToList();
                File.WriteAllText(Path.Combine(root, name + ".json"), OutpostJson.Serialize(new
                {
                    Variant = name, Model = modelName, Seed = seed, BaseBalanceVersion = baseConfigs.Balance.Version,
                    BaseCampaignWallSec = baseWall, Rows = mine,
                }) + "\n");
            }
            TestContext.Progress.WriteLine("pace wall " + sw.Elapsed);
        }

        sealed class Job
        {
            public string Scenario;
            public ulong Seed;
            public bool Detail;
        }

        static void Generate(string tag, string configDir, string root, bool saturationRuns, IReadOnlyList<double> absenceHours,
            bool withoutPace = false)
        {
            OutpostConfigSet Load(BalanceRepository r)
            {
                var c = LoadConfigs(configDir, r);
                return withoutPace ? c.WithoutPace() : c;
            }
            string outDir = Path.Combine(root, tag);
            Directory.CreateDirectory(outDir);
            var jobs = new List<Job>();
            foreach (var s in LuckSeeds) jobs.Add(new Job { Scenario = "free", Seed = s, Detail = s == 1 });
            foreach (var name in new[] { "idle", "active", "eager-rr", "free-cheapest", "free-roundrobin", "free-damage" })
                jobs.Add(new Job { Scenario = name, Seed = 1, Detail = true });
            foreach (var s in new ulong[] { 2, 3, 4 })
            {
                jobs.Add(new Job { Scenario = "idle", Seed = s });
                jobs.Add(new Job { Scenario = "active", Seed = s });
            }
            var bytes = TestData.BalanceBytes;
            var sw = Stopwatch.StartNew();
            var profiles = new Dictionary<int, OutpostProfile>();
            var freeLevels = new Dictionary<int, int[]>();
            var results = new CampaignAudit[jobs.Count];
            Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, i =>
            {
                var job = jobs[i];
                var repo = BalanceRepository.Load(bytes);
                var configs = Load(repo);
                var model = BalanceAudit.Model(job.Scenario);
                Action<int, OutpostGameState> capture = null;
                if (job.Scenario == "free" && job.Seed == 1)
                    capture = (zone, state) =>
                    {
                        if (SaturationZones.Contains(zone))
                            lock (profiles) profiles[zone] = OutpostJson.DeepCopy(state.Profile);
                    };
                // ТЗ 2.2 §9: вклад офлайна — отсутствие 1 и 3 ч с 50 % длительности каждой шахты у модели free (seed 1).
                var away = job.Scenario == "free" && job.Seed == 1 ? absenceHours : null;
                var audit = BalanceAudit.RunCampaign(repo, configs, model, job.Seed, OutpostIds.LastZone, job.Detail ? TraceZones : null, capture, away);
                if (!job.Detail)
                    foreach (var m in audit.Mines) m.PurchaseLog = null;
                results[i] = audit;
                File.WriteAllText(Path.Combine(outDir, "campaign-" + job.Scenario + "-seed" + job.Seed + ".json"), OutpostJson.Serialize(audit) + "\n");
                TestContext.Progress.WriteLine(tag + " " + job.Scenario + " seed " + job.Seed + ": " + (audit.Success ? "ok" : "FAILED") +
                    " mines=" + audit.Mines.Count + " total=" + (audit.TotalSec / 3600).ToString("0.00") + " h");
            });
            var free1 = results[0];
            foreach (var m in free1.Mines) freeLevels[m.Zone] = m.Levels;

            // Насыщение команды: профиль и итоговые уровни шахты из кампании free seed 1.
            var saturation = new SaturationAudit[SaturationZones.Length];
            Parallel.For(0, saturationRuns ? SaturationZones.Length : 0, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, k =>
            {
                int zone = SaturationZones[k];
                if (!profiles.ContainsKey(zone) || !freeLevels.ContainsKey(zone)) return;
                var repo = BalanceRepository.Load(bytes);
                var configs = Load(repo);
                var fixedLevels = (int[])freeLevels[zone].Clone();
                fixedLevels[(int)UpgradeBranch.Team] = 0;
                saturation[k] = BalanceAudit.RunSaturation(repo, configs, profiles[zone], zone, fixedLevels,
                    new[] { 0, 1, 2, 3, 4, 6, 8, 10, 12, 15 }, "free-seed1-before-zone" + zone);
            });
            if (saturationRuns)
                File.WriteAllText(Path.Combine(outDir, "saturation.json"), OutpostJson.Serialize(saturation.Where(s => s != null).ToList()) + "\n");

            var cfg = Load(TestData.Balance);
            File.WriteAllText(Path.Combine(outDir, "meta.json"), OutpostJson.Serialize(new
            {
                tag,
                configDir = Path.GetRelativePath(TestData.ProjectRoot, configDir).Replace('\\', '/'),
                sourceSha256 = TestData.Balance.Sha256,
                balanceVersion = cfg.Balance.Version,
                balanceHash = cfg.BalanceHash,
                catalogHash = cfg.CatalogHash,
                topologyVersion = cfg.Topologies?.Version,
                factoryRulesVersion = cfg.FactoryRules?.Version,
                priceRate = cfg.FactoryRules?.PriceRate.ToString(),
                factoryRulesHash = cfg.FactoryRulesHash,
                absenceHours,
                calibrationMedian2to5 = CalibrationMedian(results.Where((r, i) => r != null && jobs[i].Scenario == "free")),
                jobs = jobs.Select((j, i) => new { j.Scenario, j.Seed, ok = results[i]?.Success, totalSec = results[i]?.TotalSec }).ToList(),
                wallSeconds = sw.Elapsed.TotalSeconds,
            }) + "\n");
            Assert.IsTrue(results.All(r => r != null && r.Success), "есть непройденные кампании: " +
                string.Join(", ", jobs.Where((j, i) => results[i] == null || !results[i].Success).Select(j => j.Scenario + "/" + j.Seed)));
        }
    }
}
