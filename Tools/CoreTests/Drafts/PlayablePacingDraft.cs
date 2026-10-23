using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Черновик (вне Assets): темп первых 30 минут для playable (Docs/Playable/PLAYABLE_TZ.md §3.2). Кампании шахт 1–12
    /// моделей active и free на вариантах профиля баланса (JSON merge-патчи outpost_balance.json, как Audit_Tune);
    /// между шахтами добавляется время перехода (Transition, с). Итог — сколько шахт пройдено за 30 минут и паузы покупок.
    /// ТЗ 2.2 §14: профиль playable накладывается на b5 (outpost-2.2-b5-playable); заводы и рынок — из того же ядра и
    /// factory_rules.json, модели доставляют и продают товар (§9), цена товара следует за CoinBase профиля. Вариант p3 — числа
    /// PlayableBalance.Overlay (действующий профиль playable), b5 — основная игра.
    /// Запуск: Tools/CoreTests/run.ps1 -Draft PlayablePacingDraft.cs -Filter PlayablePacingDraft -Instance playable.
    /// Отчёт: Docs/Reports/balance-factories/playable/pacing-&lt;variant&gt;-&lt;model&gt;.json и таблица в выводе теста
    /// (прежние замеры b4 — Docs/Reports/playable/).
    /// </summary>
    public class PlayablePacingDraft
    {
        const int LastZone = 12;
        const double Window = 30 * 60;

        static readonly (string Name, double Transition, string Patch)[] s_Variants =
        {
            ("b5", 35, "{}"),
            ("p1", 8, "{\"StartWorkers\":3,\"CoinBase\":\"10\",\"CostZoneStep\":\"0\"}"),
            ("p2", 8, "{\"StartWorkers\":3,\"CoinBase\":\"10\",\"CostZoneStep\":\"0\",\"Branches\":[{\"Growth\":\"3/2\"}]}"),
            ("p3", 8, "{\"StartWorkers\":3,\"CoinBase\":\"10\",\"CostZoneStep\":\"0\",\"TapDamageFactor\":\"1/2\",\"Branches\":[{\"Growth\":\"3/2\"},{\"Milestones\":[5,10]}]}"),
            ("p4", 8, "{\"StartWorkers\":3,\"CoinBase\":\"10\",\"CostZoneStep\":\"0\",\"TapDamageFactor\":\"1/2\",\"BaseMoveSpeed\":\"13/4\",\"Branches\":[{\"Growth\":\"3/2\"},{\"Milestones\":[5,10]}]}"),
            ("p5", 8, "{\"StartWorkers\":4,\"CoinBase\":\"10\",\"CostZoneStep\":\"0\",\"TapDamageFactor\":\"1/2\",\"Branches\":[{\"Growth\":\"3/2\"},{\"Milestones\":[5,10]}]}"),
            ("p6", 8, "{\"StartWorkers\":3,\"CoinBase\":\"10\",\"CostZoneStep\":\"0\",\"TapDamageFactor\":\"1/2\",\"BaseMoveSpeed\":\"13/4\",\"BaseCapacity\":6,\"Branches\":[{\"Growth\":\"3/2\"},{\"Milestones\":[5,10]}]}"),
        };

        static readonly string[] s_Models = { "active", "free" };

        [Test]
        public void FirstThirtyMinutes()
        {
            string res = Path.Combine(TestData.ProjectRoot, "Assets", "Game", "Resources", "Outpost");
            string Read(string f) => File.ReadAllText(Path.Combine(res, f));
            string outDir = Path.Combine(TestData.ProjectRoot, "Docs", "Reports", "balance-factories", "playable");
            Directory.CreateDirectory(outDir);
            var jobs = (from v in s_Variants from m in s_Models select (v, m)).ToList();
            var rows = new string[jobs.Count];
            Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, i =>
            {
                var (v, model) = jobs[i];
                var doc = Newtonsoft.Json.Linq.JObject.Parse(Read("outpost_balance.json"));
                doc.Merge(Newtonsoft.Json.Linq.JObject.Parse(v.Patch),
                    new Newtonsoft.Json.Linq.JsonMergeSettings { MergeArrayHandling = Newtonsoft.Json.Linq.MergeArrayHandling.Merge });
                var repo = BalanceRepository.Load(TestData.BalanceBytes);
                var configs = OutpostConfigSet.Parse(doc.ToString(), Read("equipment_catalog.json"), Read("stage_rewards.json"),
                    Read("level_overrides.json"), Read("card_rules.json"), Read("mine_topology.json"), Read("store_catalog.json"),
                    Read("factory_rules.json")).WithCards(repo);
                var audit = BalanceAudit.RunCampaign(repo, configs, BalanceAudit.Model(model), 1, LastZone);
                double clock = 0;
                int done = 0;
                var mines = new List<object>();
                foreach (var m in audit.Mines)
                {
                    double sec = m.CompletedSec ?? double.NaN;
                    clock += sec + v.Transition;
                    if (clock - v.Transition <= Window) done++;
                    mines.Add(new { m.Zone, Sec = Math.Round(sec, 1), EndClockMin = Math.Round(clock / 60, 2), m.Purchases,
                        m.FirstDeliverySec, m.FirstPurchaseSec, m.FirstHireSec, m.MedianPurchaseGapSec, m.MaxPurchaseGapSec,
                        m.MaxUnaffordableSec, m.DrainSec, m.WorkersStart, m.WorkersEnd, m.Gates, Levels = m.Levels,
                        CreditedBase = Math.Round(m.CreditedBase, 1), GoodsSoldBase = Math.Round(m.GoodsSoldBase, 1), m.ManualDeliveries,
                        Factories = m.Factories.Count, FactoriesAuto = m.Factories.Count(f => f.AutoAtEnd), FactoryCycles = m.Factories.Sum(f => f.Cycles) });
                }
                File.WriteAllText(Path.Combine(outDir, "pacing-" + v.Name + "-" + model + ".json"),
                    OutpostJson.Serialize(new { Variant = v.Name, v.Patch, Model = model, v.Transition, MinesIn30Min = done, Mines = mines }) + "\n");
                var ic = CultureInfo.InvariantCulture;
                var sb = new StringBuilder(string.Format(ic, "{0,-3} {1,-6} done30={2,2} |", v.Name, model, done));
                foreach (var m in audit.Mines) sb.Append(string.Format(ic, " {0:0.0}", (m.CompletedSec ?? double.NaN) / 60));
                double goods = audit.Mines.Sum(m => m.GoodsSoldBase), coins = audit.Mines.Sum(m => m.CreditedBase);
                sb.Append(string.Format(ic, " | medGap {0:0.0}s maxGap {1:0}s maxUnaff {2:0}s | goods {3:0}% of gold",
                    audit.Mines.Average(m => m.MedianPurchaseGapSec), audit.Mines.Max(m => m.MaxPurchaseGapSec),
                    audit.Mines.Max(m => m.MaxUnaffordableSec), 100 * goods / Math.Max(1e-9, goods + coins)));
                rows[i] = sb.ToString();
            });
            TestContext.Progress.WriteLine("variant model done30 | minutes per mine 1.." + LastZone + " | purchase gaps");
            foreach (var r in rows) TestContext.Progress.WriteLine(r);
        }
    }
}
