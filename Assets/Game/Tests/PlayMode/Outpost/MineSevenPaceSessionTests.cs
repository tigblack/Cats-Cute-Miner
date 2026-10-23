using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Core.Sim;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CatMiners.Tests
{
    /// <summary>
    /// Earn zones 1–6 with the free core model, then measure zone 7 in CoreGame.
    /// ManualClock advances 0.25 s per frame at SimSpeed 100: this is a deterministic
    /// controller session, not a measurement of timed realtime performance at ×100.
    /// </summary>
    public sealed class MineSevenPaceSessionTests
    {
        const int Speed = 100;
        const int FrameLimit = 5000;
        const double ClockStepSec = 0.25;
        const double FrameResolutionSec = ClockStepSec * Speed;
        ManualClock m_Clock;
        string m_Dir;
        OutpostController Game => GameBootstrap.OutpostController;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_Dir = Path.Combine(Application.temporaryCachePath, "MineSevenPaceSession", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Dir);
            m_Clock = new ManualClock(1_800_000_000_000L);
            OutpostController.ClockOverride = m_Clock;
            OutpostController.SaveDirectoryOverride = m_Dir;
            OutpostController.FileSystemOverride = null;
            OutpostController.ConfigsOverride = null;
            GameBootstrap.LegacyMode = false;
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            Assert.That(Game, Is.Not.Null);
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var empty = SceneManager.CreateScene("MineSevenPaceSessionCleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            var loaded = SceneManager.GetSceneByName("CoreGame");
            if (loaded.IsValid()) yield return SceneManager.UnloadSceneAsync(loaded);
            OutpostController.ClockOverride = null;
            OutpostController.SaveDirectoryOverride = null;
            OutpostController.FileSystemOverride = null;
            OutpostController.ConfigsOverride = null;
            OutpostController.SuppressOfflineSummary = false;
            OutpostHud.BottomReserve = 0f;
            GameBootstrap.LegacyMode = false;
            try { if (m_Dir != null && Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true); }
            catch (IOException) { }
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator EarnedFreeProfile_ZoneSevenMiddleTakesAboutSixHours()
        {
            var configs = Game.Configs;
            var repo = Game.Balance;
            var model = BalanceAudit.Model("free");
            var state = StageProgressionService.NewGame(repo, configs, 1UL, m_Clock.UtcNowMs);
            var map = OutpostLevelAdapter.BuildMine(repo, 1, configs);
            var earnedMines = new List<MineAudit>();
            for (int zone = 1; zone <= 6; zone++)
            {
                var sim = new OutpostSimulation(state, map, configs) { RecordEvents = false };
                var audit = BalanceAudit.RunMine(sim, model, "free");
                earnedMines.Add(audit);
                Assert.That(audit.Outcome, Is.EqualTo("Completed"), "core zone " + zone + ": " + audit.Error);
                Assert.That(state.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
                model.BetweenMines(state.Profile, configs);
                var transition = StageProgressionService.PrepareTransition(state, repo, configs, m_Clock.UtcNowMs);
                Assert.That(transition.Ok, Is.True, "core transition after zone " + zone + ": " + transition.Reason);
                state = transition.State;
                map = transition.Map;
            }
            var start7 = state;
            Assert.That(start7.Run.Zone, Is.EqualTo(7));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, start7.Profile.CompletedZones);

            // Both Main and Debug slots belong to this test's temporary save directory.
            Game.SwitchProfile(true);
            for (int i = 0; i < 600 && (!Game.IsDebugProfile || Game.Phase != OutpostControllerPhase.Playing); i++)
            {
                m_Clock.Advance(ClockStepSec);
                yield return null;
            }
            Assert.That(Game.IsDebugProfile, Is.True);
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
            Game.State.Profile = OutpostJson.DeepCopy(start7.Profile);
            // PrepareTransition already allocated run 7; LoadZone must allocate the same run again.
            Game.State.Profile.NextRunSeq--;
            Game.LoadZone(7);
            for (int i = 0; i < 600 && (Game.State.Run.Zone != 7 || Game.Phase != OutpostControllerPhase.Playing || Game.InTransition); i++)
            {
                m_Clock.Advance(ClockStepSec);
                yield return null;
            }
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
            Assert.That(Game.State.Run.Zone, Is.EqualTo(7));
            Assert.That(Game.State.Run.RunId, Is.EqualTo(start7.Run.RunId));
            Assert.That(OutpostJson.Serialize(Game.State.Profile), Is.EqualTo(OutpostJson.Serialize(start7.Profile)),
                "zone 7 must start with the earned profile, without resource grants");
            Game.SimSpeed = Speed;
            Assert.That(Game.SimSpeed, Is.EqualTo(Speed));

            var middleIds = new HashSet<int>(Game.Map.Targets
                .Where(t => t.Row >= 10 && t.Row <= 16 && (t.Kind == 'r' || t.Kind == 'x' || t.Kind == 's'))
                .Select(t => t.Id));
            Assert.That(middleIds.Count, Is.GreaterThan(0), "middle band Row 10..16, kinds r/x/s");
            var middle = Game.State.Run.Targets.Where(t => middleIds.Contains(t.Id)).ToArray();
            Assert.That(middle.Length, Is.EqualTo(middleIds.Count));
            Assert.That(middle.All(t => !t.Destroyed && t.RemainingHpUnits == t.MaxHpUnits), Is.True);
            long startTick = Game.State.Run.Now;
            long tps = Game.State.Run.Balance.TicksPerSecond;
            long nextCheck = startTick + 15 * tps;
            long? firstDamage = null, allMiddleDestroyed = null;
            int frames = 0, purchases = 0;
            while (frames < FrameLimit && Game.State.Run.Phase != RunPhase.ReadyToContinue)
            {
                // Never advance the controller clock by >1 s: that would trigger offline catch-up.
                m_Clock.Advance(ClockStepSec);
                frames++;
                yield return null;
                Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing), "frame " + frames);
                Assert.That(Game.SaveFailed, Is.False, "frame " + frames);
                var run = Game.State.Run;
                middle = run.Targets.Where(t => middleIds.Contains(t.Id)).ToArray();
                if (!firstDamage.HasValue && middle.Any(t => t.RemainingHpUnits < t.MaxHpUnits))
                {
                    firstDamage = run.Now;
                    string checkpointDir = Path.Combine(PlayModeUtil.ReportsDir(), "pace-x100-20261020");
                    Directory.CreateDirectory(checkpointDir);
                    File.WriteAllText(Path.Combine(checkpointDir, "unity-zone7-middle-entry.json"),
                        OutpostSaveService.SerializeState(Game.State));
                }
                if (!allMiddleDestroyed.HasValue && middle.All(t => t.Destroyed)) allMiddleDestroyed = run.Now;
                if (run.Phase == RunPhase.ReadyToContinue || run.Now < nextCheck) continue;
                // Fixed 30 s schedule, rounded up to the next 25 s observation frame.
                do { nextCheck += 30 * tps; } while (nextCheck <= run.Now);
                foreach (var factory in run.Factories.ToArray())
                    if (factory.Status == FactoryStatus.Ready)
                        Assert.That(Game.DeliverFactory(factory.TargetId).Ok, Is.True, "deliver " + factory.TargetId);
                Game.SellGoods(null);
                for (int guard = 0; guard < 200; guard++)
                {
                    UpgradeBranch? best = null;
                    System.Numerics.BigInteger bestCost = 0;
                    foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
                    {
                        var quote = Game.Quote(branch, BuyMode.One);
                        if (quote.CanBuy && (!best.HasValue || quote.Cost < bestCost))
                        {
                            best = branch;
                            bestCost = quote.Cost;
                        }
                    }
                    if (!best.HasValue) break;
                    Assert.That(Game.Buy(best.Value, BuyMode.One).Ok, Is.True, "buy " + best.Value);
                    purchases++;
                }
            }

            var final = Game.State.Run;
            double? middleDurationSec = firstDamage.HasValue && allMiddleDestroyed.HasValue
                ? (allMiddleDestroyed.Value - firstDamage.Value) / (double)tps : (double?)null;
            double? totalCompletedSec = final.Phase == RunPhase.ReadyToContinue
                ? (final.Counters.CompletedTick - startTick) / (double)tps : (double?)null;
            string dir = Path.Combine(PlayModeUtil.ReportsDir(), "pace-x100-20261020");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "unity-zone7.json"), OutpostJson.Serialize(new
            {
                zone = 7,
                frames,
                speed100 = Speed,
                clock = "ManualClock.Advance(0.25) per frame; controller SimSpeed=100; not timed realtime x100",
                frameResolutionSec = FrameResolutionSec,
                middleRows = new[] { 10, 16 },
                middleKinds = "rxs",
                middleTargets = middleIds.Count,
                firstDamageSec = firstDamage.HasValue ? (firstDamage.Value - startTick) / (double)tps : (double?)null,
                allMiddleDestroyedSec = allMiddleDestroyed.HasValue ? (allMiddleDestroyed.Value - startTick) / (double)tps : (double?)null,
                middleDurationSec,
                totalCompletedSec,
                levels = final.Levels,
                purchases,
                profileearnednotcheat = true,
                profileSource = "NewGame seed 1; BalanceAudit.Model(free), RunMine 1..6, BetweenMines, PrepareTransition",
                earnedMines,
                readyToContinue = final.Phase == RunPhase.ReadyToContinue,
                canContinue = Game.CanContinue,
                balanceVersion = final.Balance.Version,
            }) + "\n");
            TestContext.WriteLine("ManualClock x100: frames=" + frames + ", middleSec=" + middleDurationSec + ", completedSec=" + totalCompletedSec);
            Assert.That(firstDamage.HasValue, Is.True, "middle band never took damage");
            Assert.That(allMiddleDestroyed.HasValue, Is.True, "middle band was not destroyed");
            Assert.That(middleDurationSec.Value, Is.InRange(4.8 * 3600 - FrameResolutionSec, 7.2 * 3600 + FrameResolutionSec));
            Assert.That(final.Phase, Is.EqualTo(RunPhase.ReadyToContinue), "zone 7 did not complete within " + frames + " frames");
            Assert.That(totalCompletedSec.Value, Is.LessThanOrEqualTo(9 * 3600));
            Assert.That(Game.CanContinue, Is.True);
        }
    }
}
