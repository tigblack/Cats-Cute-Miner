using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using UnityEditor;
using UnityEngine;

namespace CatMiners.Editor
{
    // Uses the same deterministic simulation and earned campaign profile as the runtime.
    public static class QuickPaceAudit
    {
        public static void VerifyOfflineAndSkip()
        {
            try
            {
                var repo = BalanceRepository.Load(File.ReadAllBytes(ProjectSetup.BalancePath));
                var configs = OutpostConfigSet.ParseResources(OutpostConfigSet.ResourcePaths.Select(p =>
                    File.ReadAllText("Assets/Game/Resources/" + p + ".json")).ToArray()).WithCards(repo);
                string dir = Path.Combine(EditorPaths.ReportsDir(), "pace-x100-20261020");
                var state = OutpostSaveService.DeserializeState(File.ReadAllText(Path.Combine(dir, "unity-zone7-middle-entry.json")));
                var map = OutpostLevelAdapter.ForRun(repo, state.Run);
                var ids = new System.Collections.Generic.HashSet<int>(map.Targets.Where(t => t.Row >= 10 && t.Row <= 16 && "rxs".IndexOf(t.Kind) >= 0).Select(t => t.Id));
                long Remaining(OutpostGameState s) => s.Run.Targets.Where(t => ids.Contains(t.Id)).Sum(t => t.RemainingHpUnits);
                long before = Remaining(state);
                long returnUtc = state.Run.LastProcessedUtcMs + 21600000;
                var offline = new OfflineSimulator(map, configs).Begin(state, returnUtc);
                offline.RunToEnd();
                var returned = OutpostSaveService.DeserializeState(OutpostSaveService.SerializeState(offline.State));
                var repeat = new OfflineSimulator(map, configs).Begin(returned, returnUtc);
                repeat.RunToEnd();
                if (offline.Summary.AppliedMs != 21600000 || repeat.Summary.AppliedMs != 0 || Remaining(returned) >= before)
                    throw new InvalidOperationException("Offline progress/cap/replay check failed");
                if (!configs.Store.TryGet("skip_12h", out var def)) throw new InvalidOperationException("Missing 1h skip");
                var skipStart = OutpostJson.DeepCopy(state);
                // Isolated QA fixture only: exercise the real gem debit, not a simulated external money purchase.
                skipStart.Profile.Gems = def.Price;
                var skip = new OfflineSimulator(map, configs).BeginSkip(skipStart, def, state.Run.LastProcessedUtcMs);
                if (!StoreService.PayTimeSkip(skip.State.Profile, def).Ok) throw new InvalidOperationException("Skip payment rejected");
                skip.RunToEnd();
                if (skip.State.Profile.Gems != 0 || skip.Summary.AppliedMs != def.Minutes * 60000L || Remaining(skip.State) >= before)
                    throw new InvalidOperationException("Skip progress/debit check failed");
                File.WriteAllText(Path.Combine(dir, "offline-and-skip.json"), OutpostJson.Serialize(new {
                    success = true, offlineHours = 6, appliedMs = offline.Summary.AppliedMs,
                    middleHpRemovedPercent = 100d * (before - Remaining(returned)) / before,
                    middleTargetsDestroyedAfterReturn = returned.Run.Targets.Count(t => ids.Contains(t.Id) && t.Destroyed),
                    middleTargets = ids.Count, repeatAppliedMs = repeat.Summary.AppliedMs,
                    skipMinutes = def.Minutes, skipPriceGems = def.Price,
                    skipHpRemovedPercent = 100d * (before - Remaining(skip.State)) / before,
                    externalIapTested = false, source = "Checkpoint from actual earned-profile Unity x100 session"
                }));
                UnityEngine.Debug.Log("[QuickOffline] Six hours, no replay, and paid-in-gems 1h time skip verified");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { UnityEngine.Debug.LogError("[QuickOffline] " + e); EditorApplication.Exit(1); }
        }
        static BotRunReport RunLeanMine(OutpostSimulation sim)
        {
            var buyer = new OutpostBot(BotPolicy.C) { ManageFactories = false };
            System.Numerics.BigInteger lastWallet = -1;
            System.Numerics.BigInteger? nextAffordable = 0;
            int lastCopies = -1;
            long limit = BalanceAudit.TickLimit * OutpostBot.PaceLimitFactor(sim.Run);
            void Act()
            {
                OutpostBot.TendFactories(sim);
                // No purchase can become affordable without money or a newly received card bonus.
                // This removes expensive no-op quotes on every hit, without skipping simulation events.
                bool cardsChanged = sim.Run.Counters.CardsGained != lastCopies;
                if (cardsChanged || (sim.Run.Wallet != lastWallet && nextAffordable.HasValue && sim.Run.Wallet >= nextAffordable.Value))
                {
                    buyer.ApplyPolicy(sim);
                    lastWallet = sim.Run.Wallet; lastCopies = sim.Run.Counters.CardsGained;
                    nextAffordable = null;
                    foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
                    {
                        var cost = sim.Quote(branch, BuyMode.One).NextCost;
                        if (cost.HasValue && (!nextAffordable.HasValue || cost.Value < nextAffordable.Value)) nextAffordable = cost;
                    }
                }
            }
            Act();
            while (!sim.IsEconomyStopped)
            {
                if (sim.Run.WatchdogFired || sim.NextEventTick() == long.MaxValue || sim.NextEventTick() > limit)
                    throw new InvalidOperationException("Campaign stopped in mine " + sim.Run.Zone);
                sim.Advance(sim.NextEventTick());
                Act();
            }
            var r = sim.Run;
            return new BotRunReport {
                Zone = r.Zone, ZoneId = r.ZoneId, Policy = BotPolicy.C, Seed = sim.Profile.Seed,
                Outcome = BotOutcome.Completed, BalanceVersion = r.Balance.Version,
                CompletedSec = r.Counters.CompletedTick / (double)r.Balance.TicksPerSecond,
                Events = sim.ProcessedEvents, Credited = r.TotalCredited.ToString(), Spent = r.TotalSpent.ToString(),
                FinalLevels = (int[])r.Levels.Clone(), Purchases = buyer.Purchases, Workers = r.Workers.Count,
                Factories = r.Factories.Count, GoodsSoldGold = r.GoodsSoldGold.ToString(),
                GoodsSoldUnits = r.GoodsSold.Values.Sum(), ManualDeliveries = r.Counters.ManualDeliveries
            };
        }
        public static void CompareLeanRunner()
        {
            try
            {
                var repo = BalanceRepository.Load(File.ReadAllBytes(ProjectSetup.BalancePath));
                var configs = OutpostConfigSet.ParseResources(OutpostConfigSet.ResourcePaths.Select(p =>
                    File.ReadAllText("Assets/Game/Resources/" + p + ".json")).ToArray()).WithCards(repo);
                string dir = Path.Combine(EditorPaths.ReportsDir(), "pace-x100-20261020");
                var fixture = OutpostSaveService.DeserializeState(File.ReadAllText(Path.Combine(dir, "unity-zone7-middle-entry.json")));
                var map = OutpostLevelAdapter.ForRun(repo, fixture.Run);
                var original = new OutpostSimulation(OutpostJson.DeepCopy(fixture), map, configs) { RecordEvents = false };
                var timer = Stopwatch.StartNew();
                var baseline = new OutpostBot(BotPolicy.C).Run(original);
                double referenceSeconds = timer.Elapsed.TotalSeconds;
                if (!baseline.Success) throw new InvalidOperationException("Reference bot failed: " + baseline);
                var optimized = new OutpostSimulation(OutpostJson.DeepCopy(fixture), map, configs) { RecordEvents = false };
                timer.Restart();
                RunLeanMine(optimized);
                double optimizedSeconds = timer.Elapsed.TotalSeconds;
                if (OutpostJson.Serialize(original.State) != OutpostJson.Serialize(optimized.State))
                    throw new InvalidOperationException("Optimized campaign policy changed gameplay state");
                File.WriteAllText(Path.Combine(dir, "campaign-runner-equivalence.json"), OutpostJson.Serialize(new {
                    byteIdenticalState = true, referenceSeconds, optimizedSeconds, mine = 7,
                    coreEventsSkipped = false, onlyUnaffordableNoopQuotesSkipped = true
                }));
                UnityEngine.Debug.Log("[QuickCampaign] Runner equivalence passed");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { UnityEngine.Debug.LogError("[QuickCampaign] " + e); EditorApplication.Exit(1); }
        }
        public static void RunFastCampaign()
        {
            try
            {
                var repo = BalanceRepository.Load(File.ReadAllBytes(ProjectSetup.BalancePath));
                var configs = OutpostConfigSet.ParseResources(OutpostConfigSet.ResourcePaths.Select(p =>
                    File.ReadAllText("Assets/Game/Resources/" + p + ".json")).ToArray()).WithCards(repo);
                string dir = Path.Combine(EditorPaths.ReportsDir(), "pace-x100-20261020", "campaign-b7-verified");
                Directory.CreateDirectory(dir);
                var reports = new System.Collections.Generic.List<BotRunReport>();
                var meta = BalanceAudit.Model("free");
                var state = StageProgressionService.NewGame(repo, configs, 1, 0);
                var map = OutpostLevelAdapter.BuildMine(repo, 1, configs);
                while (true)
                {
                    var sim = new OutpostSimulation(state, map, configs) { RecordEvents = false };
                    sim.Submit(OutpostCommand.EquipBest());
                    var report = RunLeanMine(sim);
                    reports.Add(report);
                    File.WriteAllText(Path.Combine(dir, "mine-" + report.Zone + ".json"), OutpostJson.Serialize(report));
                    UnityEngine.Debug.Log("[QuickCampaign] mine=" + report.Zone + " outcome=" + report.Outcome + " seconds=" + report.CompletedSec);
                    if (!report.Success) throw new InvalidOperationException(report.ToString());
                    var errors = OutpostInvariants.Check(state, map, configs.Equipment, configs.Cards);
                    if (errors.Count != 0) throw new InvalidOperationException(string.Join(";", errors));
                    var loaded = OutpostSaveService.DeserializeState(OutpostSaveService.SerializeState(state));
                    if (OutpostJson.Serialize(loaded) != OutpostJson.Serialize(state)) throw new InvalidOperationException("Save/reload mismatch");
                    if (state.Run.Zone == OutpostIds.LastZone) break;
                    meta.BetweenMines(state.Profile, configs);
                    var next = StageProgressionService.PrepareTransition(state, repo, configs, 0);
                    if (!next.Ok) throw new InvalidOperationException("transition:" + next.Reason);
                    state = next.State; map = next.Map;
                }
                File.WriteAllText(Path.Combine(dir, "summary.json"), OutpostJson.Serialize(new {
                    Success = true, completedMines = reports.Count, totalSeconds = reports.Sum(r => r.CompletedSec ?? 0),
                    method = "Event-driven full campaign, earned profile, no paid boosts; Unity x100 validated separately on mine7", reports
                }));
                EditorApplication.Exit(0);
            }
            catch (Exception e) { UnityEngine.Debug.LogError("[QuickCampaign] " + e); EditorApplication.Exit(1); }
        }
        public static void TuneZone7()
        {
            try
            {
                var repo = BalanceRepository.Load(File.ReadAllBytes(ProjectSetup.BalancePath));
                var configs = OutpostConfigSet.ParseResources(OutpostConfigSet.ResourcePaths.Select(p =>
                    File.ReadAllText("Assets/Game/Resources/" + p + ".json")).ToArray()).WithCards(repo);
                string dir = Path.Combine(EditorPaths.ReportsDir(), "pace-x100-20261020", "tuning");
                Directory.CreateDirectory(dir);
                var start = OutpostJson.Deserialize<OutpostGameState>(File.ReadAllText(Path.Combine(EditorPaths.ReportsDir(),
                    "pace-x100-20261020", "baseline", "start-zone7.json")));
                var map = OutpostLevelAdapter.BuildMine(repo, 7, configs);
                var ids = new System.Collections.Generic.HashSet<int>(map.Targets.Where(t => t.Row >= 10 && t.Row <= 16 &&
                    "rxs".IndexOf(t.Kind) >= 0).Select(t => t.Id));
                long factor = 1000;
                for (int i = 0; i < 6; i++)
                {
                    var balance = configs.Balance.Clone();
                    balance.TargetPaceBands = new System.Collections.Generic.List<TargetPaceBand> {
                        new TargetPaceBand { Name = "mine7-middle-six-hour-return", Zone = 7, FromRow = 10, ToRow = 16,
                            Kinds = "rxs", HpFactor = Rational.Of(factor, 1), OfflineCapMs = 21600000 }
                    };
                    var cfg = configs.WithBalance(balance);
                    var report = BalanceAudit.RunMineFrom(start, repo, cfg, BalanceAudit.Model("free"), true);
                    File.WriteAllText(Path.Combine(dir, "factor-" + factor + ".json"), OutpostJson.Serialize(report));
                    if (report.Outcome != "Completed")
                    {
                        UnityEngine.Debug.Log("[QuickPaceTune] factor=" + factor + " outcome=" + report.Outcome);
                        factor = Math.Max(1, factor / 2); continue;
                    }
                    var timings = report.TargetTimings.Where(t => ids.Contains(t.Id)).ToArray();
                    double begin = timings.Min(t => t.FirstDamageSec), end = timings.Max(t => t.DestroyedSec);
                    double middle = end - begin;
                    var measured = new { factor, middleTargets = ids.Count, measuredTargets = timings.Length,
                        middleStartSec = begin, middleEndSec = end, middleDurationSec = middle,
                        report.CompletedSec, report.Factories, report.Levels, withinTarget = middle >= 5.4 * 3600 && middle <= 6.6 * 3600 };
                    File.WriteAllText(Path.Combine(dir, "latest.json"), OutpostJson.Serialize(measured));
                    UnityEngine.Debug.Log("[QuickPaceTune] factor=" + factor + " middleHours=" + (middle / 3600).ToString("F3") + " totalHours=" + (report.CompletedSec.Value / 3600).ToString("F3"));
                    if (measured.withinTarget) { EditorApplication.Exit(0); return; }
                    factor = Math.Max(1, (long)Math.Round(factor * 21600d / Math.Max(1, middle)));
                }
                throw new InvalidOperationException("Six-hour target not reached in calibration; inspect measured results");
            }
            catch (Exception e) { UnityEngine.Debug.LogError("[QuickPaceTune] " + e); EditorApplication.Exit(1); }
        }
        static string Option(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        public static void Run()
        {
            try
            {
                var repo = BalanceRepository.Load(File.ReadAllBytes(ProjectSetup.BalancePath));
                var texts = OutpostConfigSet.ResourcePaths.Select(p => File.ReadAllText("Assets/Game/Resources/" + p + ".json")).ToArray();
                var configs = OutpostConfigSet.ParseResources(texts).WithCards(repo);
                string label = Option("-paceLabel", "baseline");
                if (!System.Text.RegularExpressions.Regex.IsMatch(label, "^[a-z0-9-]{1,40}$")) throw new ArgumentException("Unsafe label");
                int last = int.Parse(Option("-paceLast", "144"));
                string dir = Path.Combine(EditorPaths.ReportsDir(), "pace-x100-20261020", label);
                Directory.CreateDirectory(dir);
                var timer = Stopwatch.StartNew();
                var audit = BalanceAudit.RunCampaign(repo, configs, BalanceAudit.Model("free"), 1, last,
                    beforeZone: (zone, state) => {
                        if (zone <= 8) File.WriteAllText(Path.Combine(dir, "start-zone" + zone + ".json"), OutpostJson.Serialize(state));
                        if (zone == 7)
                        {
                            var map = OutpostLevelAdapter.ForRun(repo, state.Run);
                            File.WriteAllText(Path.Combine(dir, "zone7-targets.json"), OutpostJson.Serialize(map.Targets.Select((t, i) => new {
                                t.Id, t.Kind, t.TypeId, t.Row, t.Column, t.Sector, t.SourceLevel, t.IsFactory,
                                hp = state.Run.Targets[i].MaxHpUnits / (double)OutpostIds.HpUnitsPerHp,
                                cargo = state.Run.Targets[i].TotalUnits
                            })));
                        }
                        if (zone % 12 == 0) UnityEngine.Debug.Log("[QuickPace] entered mine " + zone + "/" + last);
                    });
                File.WriteAllText(Path.Combine(dir, "campaign.json"), OutpostJson.Serialize(audit));
                File.WriteAllText(Path.Combine(dir, "summary.json"), OutpostJson.Serialize(new {
                    audit.Success, completedMines = audit.Mines.Count, audit.TotalSec,
                    cpuSeconds = timer.Elapsed.TotalSeconds, speedEquivalent = 100,
                    method = "Event-driven core, earned free-player profile, not a timed Unity x100 rendering run",
                    mines = audit.Mines.Select(m => new { m.Zone, m.Outcome, m.CompletedSec, m.LongestTargetSec, m.SlowTargets, m.Factories, m.Error })
                }));
                UnityEngine.Debug.Log("[QuickPace] complete " + audit.Mines.Count + " mines, success=" + audit.Success + ", CPU seconds=" + timer.Elapsed.TotalSeconds);
                EditorApplication.Exit(audit.Success ? 0 : 1);
            }
            catch (Exception e) { UnityEngine.Debug.LogError("[QuickPace] " + e); EditorApplication.Exit(1); }
        }
    }
}
