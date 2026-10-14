using System;
using System.Collections.Generic;
using System.IO;
using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using UnityEditor;
using UnityEngine;

namespace CatMiners.Editor
{
    public static class OutpostValidatorCli
    {
        const ulong Seed = 2101001;
        static readonly int[] StrategyZones = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 50, 93, 101, 144 };
        [MenuItem("Cat Miners/Outpost/Validate All Mines")]
        public static void RunFromMenu()
        {
            int errors = Validate();
            Debug.Log("[Outpost] Validation errors: " + errors);
        }
        public static void Run()
        {
            int errors;
            try { errors = Validate(); }
            catch (Exception e) { Debug.LogError("[Outpost] Validator failed: " + e); errors = 1; }
            EditorApplication.Exit(errors == 0 ? 0 : 1);
        }
        public static int Validate()
        {
            var repo = BalanceRepository.Load(File.ReadAllBytes(ProjectSetup.BalancePath));
            var texts = new string[OutpostConfigSet.ResourcePaths.Length];
            for (int i = 0; i < texts.Length; i++) texts[i] = File.ReadAllText("Assets/Game/Resources/" + OutpostConfigSet.ResourcePaths[i] + ".json");
            // Same topology/configuration and card catalog as the game.
            var configs = OutpostConfigSet.Parse(texts[0], texts[1], texts[2], texts[3], texts[4], texts[5], texts[6]).WithCards(repo);
            Debug.Log("[Outpost] Validating topology " + configs.Topologies.Version + ": " + repo.ZoneCount + " mines");
            string dir = Path.Combine(EditorPaths.ReportsDir(), "outpost");
            Directory.CreateDirectory(dir);
            int errors = repo.ZoneCount == 144 ? 0 : 1;
            var geometry = new List<ReachabilityReport>();
            var isolated = new List<BotRunReport>();
            var strategies = new List<BotRunReport>();
            var campaign = new List<BotRunReport>();
            for (int z = 1; z <= repo.ZoneCount; z++)
            {
                var map = OutpostLevelAdapter.BuildMine(repo, z, configs);
                var report = OutpostReachability.Check(map);
                geometry.Add(report);
                if (!report.Passed) errors++;
                var run = OutpostBot.RunZone(repo, configs, z, BotPolicy.A, Seed, setup: bot => bot.CheckInvariantsEveryTick = true);
                isolated.Add(run);
                if (!run.Success) errors++;
                if (z % 24 == 0) Debug.Log("[Outpost] Geometry + isolated A: " + z + "/" + repo.ZoneCount + ", errors=" + errors);
            }
            Write(dir, "geometry.json", geometry);
            Write(dir, "isolated-A-empty.json", isolated);
            // Capture equipment earned by a real sequential C campaign, before each selected stage.
            // No parts are spent on equipment upgrades; records retain seed, reward IDs and full ledger.
            var earned = new Dictionary<int, OutpostProfile>();
            var state = StageProgressionService.NewGame(repo, configs, Seed, 0);
            var campaignMap = OutpostLevelAdapter.BuildMine(repo, 1, configs);
            while (true)
            {
                var sim = new OutpostSimulation(state, campaignMap, configs) { RecordEvents = false };
                sim.Submit(OutpostCommand.EquipBest());
                earned[state.Run.Zone] = OutpostJson.DeepCopy(state.Profile);
                var bot = new OutpostBot(BotPolicy.C) { CheckInvariantsEveryTick = true };
                var report = bot.Run(sim, "earned-C-seed" + Seed + "-parts-spent0");
                campaign.Add(report);
                if (!report.Success) { errors++; break; }
                if (state.Run.Zone % 24 == 0) Debug.Log("[Outpost] Campaign C: " + state.Run.Zone + "/" + repo.ZoneCount + ", errors=" + errors);
                var restored = OutpostSaveService.DeserializeState(OutpostSaveService.SerializeState(state));
                errors += OutpostInvariants.Check(restored, campaignMap, configs.Equipment, configs.Cards).Count;
                if (state.Run.Zone == repo.ZoneCount) break;
                var next = StageProgressionService.PrepareTransition(restored, repo, configs, 0);
                if (!next.Ok) { errors++; break; }
                state = next.State; campaignMap = next.Map;
            }
            Write(dir, "campaign-C.json", campaign);
            Write(dir, "earned-equipment.json", earned);
            foreach (int zone in StrategyZones)
            {
                foreach (BotPolicy policy in new[] { BotPolicy.A, BotPolicy.B, BotPolicy.C })
                {
                    var empty = OutpostBot.RunZone(repo, configs, zone, policy, Seed, setup: bot => bot.CheckInvariantsEveryTick = true);
                    strategies.Add(empty);
                    if (!empty.Success) errors++;
                    if (!earned.TryGetValue(zone, out var gear)) { errors++; continue; }
                    var equipped = OutpostBot.RunZone(repo, configs, zone, policy, Seed, gear, "earned-C-seed" + Seed + "-parts-spent0",
                        bot => bot.CheckInvariantsEveryTick = true);
                    strategies.Add(equipped);
                    if (!equipped.Success) errors++;
                }
            }
            Write(dir, "strategies.json", strategies);
            Write(dir, "summary.json", new
            {
                mode = OutpostIds.ModeId, rules = OutpostIds.RulesVersion,
                balance = configs.Balance.Version, configs.BalanceHash, configs.CatalogHash,
                overrides = configs.Overrides.Version, configs.OverridesHash,
                overrideZones = new List<string>(configs.Overrides.Zones.Keys),
                cards = configs.CardRules.Version, configs.CardRulesHash, cardCount = configs.Cards.Cards.Count,
                topology = configs.Topologies.Version, topologySha256 = OutpostJson.Sha256Hex(texts[5]),
                sourceSha256 = repo.Sha256, seed = Seed, errors,
                isolatedMines = isolated.Count, campaignMines = campaign.Count, strategies = strategies.Count,
                invariantChecks = "after every processed event tick and campaign state round-trip",
                environment = "Unity Editor pure core; not Android or manual gameplay"
            });
            File.WriteAllText(Path.Combine(dir, "README.md"),
                "# Outpost validation\n\nIsolated A: two workers, no purchases or gear, 24h / 2,000,000 events per mine. " +
                "Cards use the source catalog and card_rules.json: normal chest/tutorial rewards are enabled, no card upgrades. " +
                "Sequential C retains cards and reward progression and equips the best earned gear; no parts spent on equipment. " +
                "Strategies A/B/C copy only gear from that campaign; each isolated run starts with an empty card collection and receives normal rewards. " +
                "All new runs use the branching-mine walkability mask, physical passage gates and saved topology snapshots. " +
                "Timeouts/watchdogs fail.\n\n" +
                "Errors: " + errors + ". Device performance, input and audio require Player checks.\n");
            return errors;
        }
        static void Write(string dir, string file, object value) => File.WriteAllText(Path.Combine(dir, file), OutpostJson.Serialize(value) + "\n");
    }
}
