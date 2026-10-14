using System;
using System.IO;
using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using UnityEditor;
using UnityEngine;

namespace CatMiners.Editor
{
    /// <summary>Read-only data validation followed by idempotent scene setup. Never touches user profiles.</summary>
    public static class OutpostSetup
    {
        [MenuItem("Cat Miners/Outpost/Setup Runtime")]
        public static void Run()
        {
            ValidateData();
            ProjectSetup.RunFromMenu();
        }
        public static void ValidateData()
        {
            var repo = BalanceRepository.Load(File.ReadAllBytes(ProjectSetup.BalancePath));
            var texts = new string[OutpostConfigSet.ResourcePaths.Length];
            for (int i = 0; i < texts.Length; i++)
                texts[i] = File.ReadAllText("Assets/Game/Resources/" + OutpostConfigSet.ResourcePaths[i] + ".json");
            var configs = OutpostConfigSet.Parse(texts[0], texts[1], texts[2], texts[3], texts[4], texts[5], texts[6]).WithCards(repo);
            if (repo.ZoneCount != OutpostIds.LastZone) throw new InvalidDataException("Expected 144 source mines");
            if (configs.Cards.Cards.Count != 61) throw new InvalidDataException("Expected 61 source cards, got " + configs.Cards.Cards.Count);
            for (int zone = 1; zone <= repo.ZoneCount; zone++)
            {
                var map = OutpostLevelAdapter.BuildMine(repo, zone, configs);
                var report = OutpostReachability.Check(map);
                if (!report.Passed) throw new InvalidDataException(report.Diagnostic);
            }
            Debug.Log("[Outpost] Source checksum, configs, 61 cards and 144 maps validated; saves untouched");
        }
    }
}
