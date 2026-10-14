using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Черновик (вне Assets): миграция настоящего сохранения пользователя к карточкам — на КОПИИ файлов во временной папке,
    /// оригиналы только читаются. Повторяет порядок контроллера: Load → RestartRunIfMapChanged → MigrateProfile → перенос
    /// прежних карточек (новейший save_main.json по savedAtUtcMs) → инварианты → 60 с симуляции → запись и повторная загрузка.
    /// Запуск: Tools/CoreTests/run.ps1 -Draft UserSaveMigrationDraft.cs -Filter FullyQualifiedName~UserSaveMigrationDraft.
    /// </summary>
    public class UserSaveMigrationDraft
    {
        const string Root = @"C:\Users\arutt\AppData\LocalLow\CatMiners\Cats Cute Miner";

        [Test]
        public void UserMainSave_MigratesToCards_OnCopy()
        {
            if (!File.Exists(Path.Combine(Root, "CatOutpostV2", "main", "state.json"))) Assert.Ignore("нет сохранения пользователя");
            string tmp = Path.Combine(Path.GetTempPath(), "catminers-cards-migration-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var rel in new[] { @"CatOutpostV2\main\state.json", @"CatOutpostV2\main\state.json.bak", "save_main.json",
                             @"CatOutpostV2\legacy-archive\save_main.json" })
                {
                    string src = Path.Combine(Root, rel);
                    if (!File.Exists(src)) continue;
                    string dst = Path.Combine(tmp, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(src, dst);
                }
                var repo = TestData.Balance;
                var configs = OutpostConfigSet.LoadDefault().WithCards(repo);
                var saves = new OutpostSaveService(tmp) { Configs = configs, MapResolver = s => OutpostLevelAdapter.ForRun(repo, s.Run) };
                var loaded = saves.Load();
                TestContext.Progress.WriteLine("load: " + loaded.Status + ", catalogChanged=" + loaded.CatalogChanged + ", commit=" + loaded.CommitSequence);
                Assert.AreEqual(OutpostLoadStatus.Loaded, loaded.Status);
                var state = loaded.State;
                var p = state.Profile;
                var run = state.Run;
                TestContext.Progress.WriteLine("before: zone=" + run.Zone + " phase=" + run.Phase + " wallet=" + run.Wallet + " workers=" + run.Workers.Count +
                    " levels=[" + string.Join(",", run.Levels) + "] destroyed=" + run.Targets.Count(t => t.RemainingHpUnits == 0) + "/" + run.Targets.Count +
                    " items=" + p.Items.Count + " parts=" + p.Parts + " cardsVersion=" + p.CardsVersion + " cards=" + (p.Cards?.Count ?? 0) + " elixir=" + p.Elixir);
                var walletBefore = run.Wallet;
                int destroyedBefore = run.Targets.Count(t => t.RemainingHpUnits == 0);
                bool restarted = StageProgressionService.RestartRunIfMapChanged(state, repo, configs, StartUtcMs, out _);
                TestContext.Progress.WriteLine("restartIfMapChanged=" + restarted);

                bool migrated = CardService.MigrateProfile(p, configs.CardRules);
                // Перенос прежних карточек — как OutpostController.ImportLegacyCards (новейший файл по savedAtUtcMs).
                CatMiners.Core.Sim.GameState best = null;
                long bestAt = long.MinValue;
                foreach (string path in new[] { Path.Combine(tmp, "save_main.json"), Path.Combine(tmp, "CatOutpostV2", "legacy-archive", "save_main.json") })
                {
                    if (!File.Exists(path)) continue;
                    var root = JObject.Parse(File.ReadAllText(path));
                    var payload = root["payload"] ?? root["Payload"];
                    var legacy = CatMiners.Core.Save.SaveService.DeserializeState((string)payload);
                    long at = (long?)(root["savedAtUtcMs"] ?? root["SavedAtUtcMs"]) ?? 0;
                    if (legacy?.Player != null && at > bestAt) { best = legacy; bestAt = at; }
                }
                var cards = new Dictionary<string, (int Level, long Duplicates)>(StringComparer.Ordinal);
                long elixir = 0;
                if (best != null)
                {
                    foreach (var kv in best.Player.Cards)
                        if (kv.Value != null && kv.Value.Level > 0) cards[kv.Key] = (kv.Value.Level, kv.Value.Duplicates);
                    elixir = (long)Math.Floor(best.Player.Elixir.ToDouble());
                }
                TestContext.Progress.WriteLine("legacy: " + string.Join(", ", cards.Select(kv => kv.Key + " L" + kv.Value.Level + " +" + kv.Value.Duplicates)) + "; elixir " + elixir);
                bool imported = CardService.ImportLegacy(p, configs.Cards, cards, elixir, out int importedCards);
                TestContext.Progress.WriteLine("migrated=" + migrated + " imported=" + imported + " (" + importedCards + " cards)");
                TestContext.Progress.WriteLine("after: zone=" + run.Zone + " highest=" + p.HighestZone + " cardsVersion=" + p.CardsVersion + " cards=" +
                    string.Join(", ", p.Cards.Select(kv => kv.Key + " L" + kv.Value.Level + " +" + kv.Value.Copies)) + " elixir=" + p.Elixir +
                    " keys=[" + string.Join(", ", p.CardKeys) + "]");
                Assert.AreEqual(walletBefore, state.Run.Wallet, "кошелёк не меняется");
                Assert.AreEqual(destroyedBefore, state.Run.Targets.Count(t => t.RemainingHpUnits == 0), "расчищенное остаётся");

                var map = OutpostLevelAdapter.ForRun(repo, state.Run);
                CollectionAssert.IsEmpty(OutpostInvariants.Check(state, map, configs.Equipment, configs.Cards));
                var sim = new OutpostSimulation(state, map, configs);
                long target = sim.Now + 60 * S;
                sim.Advance(target);
                TestContext.Progress.WriteLine("60 s later: wallet=" + sim.Run.Wallet + " delivered=" + sim.Run.Counters.DeliveredValue + " chests=" + sim.Run.Counters.ChestsOpened +
                    " cardsGained=" + sim.Run.Counters.CardsGained + " elixirGained=" + sim.Run.Counters.ElixirGained + " elixir=" + sim.Profile.Elixir);
                CollectionAssert.IsEmpty(OutpostInvariants.Check(sim.State, map, configs.Equipment, configs.Cards));

                // Повторный запуск: миграция и перенос не повторяются.
                Assert.IsFalse(CardService.MigrateProfile(sim.Profile, configs.CardRules));
                Assert.IsFalse(CardService.ImportLegacy(sim.Profile, configs.Cards, cards, elixir, out _));
                var commit = saves.Save(sim.State, OutpostSaveSlot.Main, map);
                Assert.IsTrue(commit.Ok, commit.Error);
                TestContext.Progress.WriteLine("save on copy: " + commit.Ok + " " + commit.Error);
                var reloaded = saves.Load();
                Assert.AreEqual(OutpostLoadStatus.Loaded, reloaded.Status);
                Assert.AreEqual(sim.Profile.Elixir, reloaded.State.Profile.Elixir);
                Assert.AreEqual(sim.Profile.CardLevel("ca001"), reloaded.State.Profile.CardLevel("ca001"));
            }
            finally
            {
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            }
        }
    }
}
