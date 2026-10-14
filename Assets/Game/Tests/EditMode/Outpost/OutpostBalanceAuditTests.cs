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
    /// Генераторы аудита баланса (Docs/Rework/BALANCE_AUDIT.md). «before» — снимок конфигураций до правок
    /// (Docs/Reports/balance-audit/configs/before), «after» — действующие файлы Assets/Game/Resources/Outpost, которые читает игра.
    /// Одинаковые модели игрока, seed и карты; итог — JSON в Docs/Reports/balance-audit/{tag}/, разбор — Tools/BalanceAudit/analyze.py.
    /// </summary>
    [Explicit("Генератор отчёта аудита баланса: несколько минут")]
    public sealed class OutpostBalanceAuditTests
    {
        public static readonly ulong[] LuckSeeds = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        public static readonly int[] SaturationZones = { 1, 10, 25, 50, 75, 100, 125, 144 };
        public static readonly int[] TraceZones = { 1, 2, 5, 10, 25, 50, 75, 100, 125, 144 };

        static string ConfigDir(string name) => Path.Combine(TestData.ProjectRoot, "Docs", "Reports", "balance-audit", "configs", name);
        static string ResourcesDir => Path.Combine(TestData.ProjectRoot, "Assets", "Game", "Resources", "Outpost");

        public static OutpostConfigSet LoadConfigs(string dir, BalanceRepository repo)
        {
            string Read(string file) => File.ReadAllText(Path.Combine(dir, file));
            return OutpostConfigSet.Parse(Read("outpost_balance.json"), Read("equipment_catalog.json"), Read("stage_rewards.json"),
                Read("level_overrides.json"), Read("card_rules.json"), Read("mine_topology.json")).WithCards(repo);
        }

        [Test]
        public void Audit_Before() => Generate("before", ConfigDir("before"));

        [Test]
        public void Audit_After()
        {
            // Игра читает Resources; встроенные копии ядра должны совпадать с ними (иначе тесты мерили бы другое).
            var live = LoadConfigs(ResourcesDir, TestData.Balance);
            var embedded = OutpostConfigSet.LoadDefault().WithCards(TestData.Balance);
            Assert.AreEqual(live.BalanceHash, embedded.BalanceHash, "OutpostConfigSet.Defaults.cs устарел: python3 Tools/gen_outpost_config_defaults.py");
            Assert.AreEqual(live.CatalogHash, embedded.CatalogHash, "OutpostConfigSet.Defaults.cs устарел: python3 Tools/gen_outpost_config_defaults.py");
            Generate("after", ResourcesDir);
        }

        /// <summary>
        /// Перебор вариантов: Docs/Reports/balance-audit/tune-variants.json — список {Name, Base ("before"|"after"), Scenarios,
        /// Seeds, Balance/Cards/Equipment/Rewards — merge-патчи JSON}. Итог — tune/{Name}/campaign-*.json для analyze.py.
        /// </summary>
        [Test]
        public void Audit_Tune()
        {
            string root = Path.Combine(TestData.ProjectRoot, "Docs", "Reports", "balance-audit");
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
            Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, i =>
            {
                var (v, scenario, seed) = jobs[i];
                string name = v.Value<string>("Name");
                string dir = ConfigDir(v.Value<string>("Base") ?? "before");
                if ((v.Value<string>("Base") ?? "before") == "after") dir = ResourcesDir;
                string Patched(string file, string key)
                {
                    var text = File.ReadAllText(Path.Combine(dir, file));
                    if (!(v[key] is Newtonsoft.Json.Linq.JObject patch)) return text;
                    var doc = Newtonsoft.Json.Linq.JObject.Parse(text);
                    doc.Merge(patch, new Newtonsoft.Json.Linq.JsonMergeSettings { MergeArrayHandling = Newtonsoft.Json.Linq.MergeArrayHandling.Merge });
                    return doc.ToString();
                }
                var repo = BalanceRepository.Load(bytes);
                var configs = OutpostConfigSet.Parse(Patched("outpost_balance.json", "Balance"), Patched("equipment_catalog.json", "Equipment"),
                    Patched("stage_rewards.json", "Rewards"), Patched("level_overrides.json", "Overrides"), Patched("card_rules.json", "Cards"),
                    File.ReadAllText(Path.Combine(dir, "mine_topology.json")));
                if (v.Value<bool?>("NoCards") != true) configs = configs.WithCards(repo);
                var audit = BalanceAudit.RunCampaign(repo, configs, BalanceAudit.Model(scenario), seed);
                if (scenario != "free" || seed != 1)
                    foreach (var m in audit.Mines) m.PurchaseLog = null;
                string outDir = Path.Combine(root, "tune", name);
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "campaign-" + scenario + "-seed" + seed + ".json"), OutpostJson.Serialize(audit) + "\n");
                TestContext.Progress.WriteLine("tune " + name + " " + scenario + "/" + seed + ": " + (audit.Success ? "ok" : "FAILED") + " " +
                    (audit.TotalSec / 3600).ToString("0.00") + " h");
                if (!audit.Success) lock (failed) failed.Add(name + "/" + scenario + "/" + seed);
            });
            Assert.IsEmpty(failed);
        }

        sealed class Job
        {
            public string Scenario;
            public ulong Seed;
            public bool Detail;
        }

        static void Generate(string tag, string configDir)
        {
            string outDir = Path.Combine(TestData.ProjectRoot, "Docs", "Reports", "balance-audit", tag);
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
                var configs = LoadConfigs(configDir, repo);
                var model = BalanceAudit.Model(job.Scenario);
                Action<int, OutpostGameState> capture = null;
                if (job.Scenario == "free" && job.Seed == 1)
                    capture = (zone, state) =>
                    {
                        if (SaturationZones.Contains(zone))
                            lock (profiles) profiles[zone] = OutpostJson.DeepCopy(state.Profile);
                    };
                var audit = BalanceAudit.RunCampaign(repo, configs, model, job.Seed, OutpostIds.LastZone, job.Detail ? TraceZones : null, capture);
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
            Parallel.For(0, SaturationZones.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, k =>
            {
                int zone = SaturationZones[k];
                if (!profiles.ContainsKey(zone) || !freeLevels.ContainsKey(zone)) return;
                var repo = BalanceRepository.Load(bytes);
                var configs = LoadConfigs(configDir, repo);
                var fixedLevels = (int[])freeLevels[zone].Clone();
                fixedLevels[(int)UpgradeBranch.Team] = 0;
                saturation[k] = BalanceAudit.RunSaturation(repo, configs, profiles[zone], zone, fixedLevels,
                    new[] { 0, 1, 2, 3, 4, 6, 8, 10, 12, 15 }, "free-seed1-before-zone" + zone);
            });
            File.WriteAllText(Path.Combine(outDir, "saturation.json"), OutpostJson.Serialize(saturation.Where(s => s != null).ToList()) + "\n");

            var cfg = LoadConfigs(configDir, TestData.Balance);
            File.WriteAllText(Path.Combine(outDir, "meta.json"), OutpostJson.Serialize(new
            {
                tag,
                configDir = Path.GetRelativePath(TestData.ProjectRoot, configDir).Replace('\\', '/'),
                sourceSha256 = TestData.Balance.Sha256,
                balanceVersion = cfg.Balance.Version,
                balanceHash = cfg.BalanceHash,
                catalogHash = cfg.CatalogHash,
                topologyVersion = cfg.Topologies?.Version,
                jobs = jobs.Select((j, i) => new { j.Scenario, j.Seed, ok = results[i]?.Success, totalSec = results[i]?.TotalSec }).ToList(),
                wallSeconds = sw.Elapsed.TotalSeconds,
            }) + "\n");
            Assert.IsTrue(results.All(r => r != null && r.Success), "есть непройденные кампании: " +
                string.Join(", ", jobs.Where((j, i) => results[i] == null || !results[i].Success).Select(j => j.Scenario + "/" + j.Seed)));
        }
    }
}
