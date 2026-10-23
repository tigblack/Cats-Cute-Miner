using System;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    public class CompactFinalTests
    {
        static OutpostConfigSet Configs => OutpostConfigSet.LoadDefault().WithCards(TestData.Balance);

        // Synthetic end phase only: no ordinary mine traversal or campaign simulation.
        static (OutpostGameState State, OutpostMap Map, OutpostConfigSet Configs) Fixture(bool legacy = false, string active = "final")
        {
            var cfg = Configs;
            if (legacy)
            {
                var rules = OutpostJson.DeepCopy(cfg.FinalExcavations);
                rules.Version = "final-excavations-1"; rules.Width = 13; rules.Depth = 3;
                cfg = cfg.WithFinalExcavations(rules);
            }
            int mine = active == "gate" ? 5 : 3;
            var state = StageProgressionService.NewGame(TestData.Balance, cfg, 1, 0, mine);
            // Explicit Apply preserves the legacy fixture before the automatic ForRun migration.
            var ordinary = OutpostLevelAdapter.BuildMine(TestData.Balance, mine, cfg.WithFinalExcavations(null));
            var map = FinalExcavationAdapter.Apply(ordinary, state.Run.MapFinal);
            var run = state.Run;
            var selected = active == "ordinary" ? map.Targets.First(t => t.Kind == 'r' && t.Cells.Length == 1) :
                active == "gate" ? map.Targets.First(t => t.IsGate) : map.Targets.Last();
            foreach (var target in run.Targets)
            {
                var mt = map.Target(target.Id);
                if (mt.IsFinal || mt.Id == selected.Id || active == "approach" && mt.IsFinalApproach) continue;
                target.RemainingHpUnits = 0; target.Destroyed = true;
                target.TotalUnits = 0; target.ReleasedUnits = 0; target.UpdateDamageStage();
            }
            foreach (var factory in run.Factories) factory.Status = FactoryStatus.PendingActivation;
            run.GatesOpened = active == "gate" ? selected.Sector : map.Gates.Length;
            run.Counters.ClearedTargets = run.Targets.Count(t => t.Destroyed);
            run.Levels[(int)UpgradeBranch.Team] = 10;
            run.Workers.Clear();
            int start = active == "approach" ? map.Cell(map.Final.Column, 5) : selected.WorkCells.First();
            for (int i = 1; i <= 12; i++)
                run.Workers.Add(new WorkerState { Id = i, Look = (i - 1) % OutpostSimulation.LookCount,
                    Mode = WorkerMode.ChoosingTask, Cell = start });
            run.NextWorkerId = 13; run.PlanningDirty = true; run.NavVersion++;
            var sim = new OutpostSimulation(state, map, cfg) { RecordEvents = false };
            Assert.That(sim.Submit(OutpostCommand.RewardBonus(RewardedBonusKind.Gold, 1)).Ok, Is.True);
            return (state, map, cfg);
        }

        static void Unique(OutpostSimulation sim)
        {
            var working = sim.Run.Workers.Where(w => w.WorkCell >= 0).ToArray();
            Assert.That(working.Select(w => w.WorkCell).Distinct().Count(), Is.EqualTo(working.Length));
            foreach (var worker in working)
            {
                Assert.That(sim.WorkCellOwner(worker.WorkCell), Is.EqualTo(worker.Id));
                Assert.That(sim.Map.Target(worker.TargetId).WorkCells, Does.Contain(worker.WorkCell));
                if (worker.Mode == WorkerMode.Mining) Assert.That(worker.Cell, Is.EqualTo(worker.WorkCell));
            }
            Assert.That(OutpostInvariants.Check(sim.State, sim.Map, sim.Configs.Equipment, sim.Configs.Cards), Is.Empty);
        }

        [Test]
        public void Approach_RocksPhysicallyBlockFrontRow_AndFinalRemainsLast()
        {
            var f = Fixture(active: "approach");
            f.State.Run.Balance.TapDamageFactor = new Rational(1000000000);
            var sim = new OutpostSimulation(f.State, f.Map, f.Configs) { RecordEvents = false };
            var rocks = f.Map.Targets.Where(t => t.IsFinalApproach).OrderByDescending(t => t.Row).ThenBy(t => t.Id).ToArray();
            Assert.That(sim.StrikeBlockReason(f.Map.Final.TargetId), Is.EqualTo("locked"));
            Assert.That(sim.AssignedTo(f.Map.Final.TargetId), Is.Zero);
            foreach (var target in rocks)
            {
                Assert.That(sim.Submit(OutpostCommand.Strike(target.Id)).Ok, Is.True, "ordinary strip rock " + target.Id);
                Unique(sim);
            }
            Assert.That(sim.CanStrike(f.Map.Final.TargetId), Is.True);
            Assert.That(sim.AssignedTo(f.Map.Final.TargetId), Is.LessThanOrEqualTo(4));
            Assert.That(f.State.Run.Targets.Last().Id, Is.EqualTo(f.Map.Final.TargetId));
            var copy = OutpostJson.DeepCopy(f.State);
            var rebuilt = OutpostLevelAdapter.ForRun(TestData.Balance, copy.Run);
            Assert.That(rebuilt.Targets.Where(t => t.IsFinalApproach).Select(t => t.Cells.Single()),
                Is.EquivalentTo(rocks.Select(t => t.Cells.Single())));
            Assert.That(copy.Run.Targets.Where(t => rebuilt.Target(t.Id).IsFinalApproach).All(t => t.Destroyed), Is.True);
        }

        [TestCase("ordinary")] [TestCase("gate")] [TestCase("final")]
        public void OnePositionOneCat_OnlineOfflineAndReload(string active)
        {
            var f = Fixture(active: active);
            var sim = new OutpostSimulation(f.State, f.Map, f.Configs) { RecordEvents = false };
            Assert.That(f.State.Run.Workers.Count, Is.EqualTo(12));
            Assert.That(f.State.Run.Workers.Count(w => w.WorkCell >= 0), Is.InRange(1, 4));
            Unique(sim);
            for (int i = 0; i < 10; i++) { sim.Advance(sim.Now + sim.Balance.TicksPerSecond); Unique(sim); }
            var saved = OutpostJson.DeepCopy(f.State);
            var rebuilt = OutpostLevelAdapter.ForRun(TestData.Balance, saved.Run);
            var online = new OutpostSimulation(saved, rebuilt, f.Configs) { RecordEvents = false };
            var offline = new OfflineSimulator(rebuilt, f.Configs).Begin(OutpostJson.DeepCopy(saved), 20000);
            offline.RunToEnd();
            online.Advance(online.Now + 20 * online.Balance.TicksPerSecond);
            online.Run.LastProcessedUtcMs = 20000;
            Assert.That(OutpostJson.Serialize(offline.State), Is.EqualTo(OutpostJson.Serialize(online.State)));
            Unique(new OutpostSimulation(offline.State, rebuilt, f.Configs) { RecordEvents = false });
            var repeated = new OfflineSimulator(rebuilt, f.Configs).Begin(offline.State, 20000); repeated.RunToEnd();
            Assert.That(repeated.Summary.AppliedMs, Is.Zero);
            Assert.That(OutpostJson.Serialize(repeated.State), Is.EqualTo(OutpostJson.Serialize(offline.State)));
        }

        static void SeedFinalCargo(OutpostGameState state, int groundCell, int bagWorker = -1)
        {
            var run = state.Run; var final = run.Targets.Last();
            final.RemainingHpUnits = final.MaxHpUnits * 3 / 4; final.ReleasedUnits = final.ExpectedReleasedUnits(); final.UpdateDamageStage();
            final.FirstStrikeTick = 0;
            int bag = bagWorker >= 0 ? 2 : 0, ground = 3;
            for (int i = 0; i < bag; i++) run.Workers[bagWorker].Bag.Add(new CargoUnit { Source = final.Id, Ordinal = i, Value = 5 });
            var batch = new CargoBatch { Seq = run.NextBatchSeq++, Key = "b1", Cell = groundCell };
            for (int i = bag; i < bag + ground; i++) batch.Units.Add(new CargoUnit { Source = final.Id, Ordinal = i, Value = 5 });
            run.Batches.Add(batch);
            run.Counters.ReleasedUnits = final.ReleasedUnits; run.Counters.ReleasedValue = final.ReleasedUnits * 5;
            run.Counters.DeliveredUnits = final.ReleasedUnits - ground - bag;
            run.Counters.DeliveredValue = run.Counters.DeliveredUnits * 5;
            run.TotalCredited = run.Counters.DeliveredValue;
            run.Wallet = run.TotalCredited + run.BonusCredited;
        }

        [Test]
        public void LegacyMigration_PreservesHpCargoCreditsReceiptsAndSafeTasks_ReplansOldWorkRows()
        {
            var f = Fixture(legacy: true); var run = f.State.Run;
            // Put a loaded miner on retained floor; remaining empty miners may be relocated when their floor disappears.
            var worker = run.Workers[0];
            int retained = f.Map.Cell(f.Map.Topology.ExitColumn, 4);
            var positions = new[] { retained, retained + f.Map.Width }.Concat(f.Map.Targets.Last().WorkCells.Where(c => c != retained && c != retained + f.Map.Width)).ToArray();
            for (int i = 0; i < run.Workers.Count; i++)
            {
                var miner = run.Workers[i];
                miner.Cell = miner.WorkCell = positions[i]; miner.Mode = WorkerMode.Mining; miner.Path = null;
                miner.Action = new TimedAction { Kind = ActionKind.Hit, Duration = 1000000, Deadline = 1000000 };
            }
            var safeWorker = run.Workers.Last();
            safeWorker.Cell = f.Map.SpawnCell; safeWorker.WorkCell = -1; safeWorker.TargetId = -1;
            safeWorker.Mode = WorkerMode.Idle; safeWorker.Action = null;
            string safeTask = OutpostJson.Serialize(safeWorker);
            SeedFinalCargo(f.State, f.Map.Cell(f.Map.Topology.ExitColumn, 5), 0);
            Assert.That(OutpostInvariants.Check(f.State, f.Map, f.Configs.Equipment, f.Configs.Cards), Is.Empty);
            f.State.Profile.RewardLog.Add(new RewardRecord { Key = "compact-migration-receipt", Zone = 2, RunId = "earlier" });
            string profile = OutpostJson.Serialize(f.State.Profile), targets = OutpostJson.Serialize(run.Targets);
            string batches = OutpostJson.Serialize(run.Batches), bags = OutpostJson.Serialize(run.Workers.Select(w => w.Bag));
            var wallet = run.Wallet; long hp = run.MapFinal.MaxHp; var now = run.Now; var utc = run.LastProcessedUtcMs;
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, run);
            Assert.That(run.MapFinal.IsCompact, Is.True);
            Assert.That(run.MapFinal.MigratedFromVersion, Is.EqualTo("final-excavations-1"));
            Assert.That(run.MapFinal.ApproachRocks, Is.Empty, "never add fresh mandatory rewards to a started run");
            Assert.That(run.MapFinal.MaxHp, Is.EqualTo(hp));
            Assert.That(OutpostJson.Serialize(run.Targets), Is.EqualTo(targets));
            Assert.That(OutpostJson.Serialize(run.Batches), Is.EqualTo(batches));
            Assert.That(OutpostJson.Serialize(run.Workers.Select(w => w.Bag)), Is.EqualTo(bags));
            Assert.That(OutpostJson.Serialize(f.State.Profile), Is.EqualTo(profile));
            Assert.That(run.Wallet, Is.EqualTo(wallet)); Assert.That(run.Now, Is.EqualTo(now)); Assert.That(run.LastProcessedUtcMs, Is.EqualTo(utc));
            Assert.That(worker.Mode, Is.EqualTo(WorkerMode.Idle)); Assert.That(worker.WorkCell, Is.EqualTo(-1));
            Assert.That(OutpostJson.Serialize(safeWorker), Is.EqualTo(safeTask), "safe tasks are retained");
            Assert.That(OutpostInvariants.Check(f.State, map, f.Configs.Equipment, f.Configs.Cards), Is.Empty);
            var sim = new OutpostSimulation(f.State, map, f.Configs) { RecordEvents = false }; Unique(sim);
            Assert.That(sim.Run.Workers.All(w => w.WorkCell < 0 || map.Target(w.TargetId).WorkCells.Contains(w.WorkCell)), Is.True);
            string migrated = OutpostJson.Serialize(run);
            OutpostLevelAdapter.ForRun(TestData.Balance, run);
            Assert.That(OutpostJson.Serialize(run), Is.EqualTo(migrated), "migration is idempotent");
            var restored = OutpostJson.DeepCopy(f.State);
            Unique(new OutpostSimulation(restored, OutpostLevelAdapter.ForRun(TestData.Balance, restored.Run), f.Configs) { RecordEvents = false });
            var offline = new OfflineSimulator(map, f.Configs).Begin(OutpostJson.DeepCopy(f.State), 10000);
            offline.RunToEnd();
            sim.Advance(sim.Now + 10 * sim.Balance.TicksPerSecond); sim.Run.LastProcessedUtcMs = 10000;
            Assert.That(OutpostJson.Serialize(offline.State), Is.EqualTo(OutpostJson.Serialize(sim.State)));
            Unique(sim);
        }

        [TestCase(false)] [TestCase(true)]
        public void LegacyFileLoad_ValidatesChecksumAndMap_PreservesProgress_AndCanBeSavedAgain(bool defer)
        {
            var f = Fixture(legacy: true);
            SeedFinalCargo(f.State, f.Map.Cell(defer ? f.Map.Final.Column : f.Map.Topology.ExitColumn, 5));
            string targets = OutpostJson.Serialize(f.State.Run.Targets);
            var wallet = f.State.Run.Wallet;
            var fs = new MemoryFileSystemA();
            var saves = new OutpostSaveService(SaveKitA.Root, fs)
            {
                Configs = Configs,
                MapResolver = state => OutpostLevelAdapter.ForRun(TestData.Balance, state.Run),
            };
            Assert.That(saves.Save(f.State, OutpostSaveSlot.Main, f.Map).Ok, Is.True);
            var loaded = saves.Load();
            Assert.That(loaded.State, Is.Not.Null, string.Join("; ", loaded.Messages));
            Assert.That(loaded.State.Run.MapFinal.IsCompact, Is.EqualTo(!defer));
            Assert.That(OutpostJson.Serialize(loaded.State.Run.Targets), Is.EqualTo(targets));
            Assert.That(loaded.State.Run.Wallet, Is.EqualTo(wallet));
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, loaded.State.Run);
            var sim = new OutpostSimulation(loaded.State, map, Configs) { RecordEvents = false };
            Unique(sim);
            Assert.That(saves.Save(sim.State, OutpostSaveSlot.Main, map).Ok, Is.True);
            Assert.That(saves.Load().State, Is.Not.Null);
        }

        [TestCase(false)] [TestCase(true)]
        public void LegacyMigration_DefersWhenCompactionWouldMoveLoot_AndLegacyStillLoads(bool carried)
        {
            var f = Fixture(legacy: true);
            int removed = f.Map.Cell(f.Map.Final.Column, 5);
            if (carried) f.State.Run.Workers[0].Cell = removed;
            SeedFinalCargo(f.State, carried ? f.Map.Cell(f.Map.Topology.ExitColumn, 5) : removed, carried ? 0 : -1);
            string before = OutpostJson.Serialize(f.State);
            Assert.That(FinalExcavationMigration.TryCompact(f.State.Run, f.Map, out string diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain(carried ? "loaded cat" : "ground cargo"));
            Assert.That(OutpostJson.Serialize(f.State), Is.EqualTo(before));
            var restored = OutpostJson.DeepCopy(f.State);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, restored.Run);
            Assert.That(map.Final.Width, Is.EqualTo(13));
            Assert.DoesNotThrow(() => new OutpostSimulation(restored, map, f.Configs));
        }

        [TestCase("duplicate")] [TestCase("second-row")] [TestCase("outside")]
        public void UnsafeSavedWorkCell_IsReleasedBeforeRebuildingOwners(string fault)
        {
            var f = Fixture();
            var owner = f.State.Run.Workers.First(w => w.WorkCell >= 0);
            var duplicate = f.State.Run.Workers.Last();
            duplicate.WorkCell = fault == "outside" ? f.Map.CellCount :
                fault == "second-row" ? owner.WorkCell + f.Map.Width : owner.WorkCell;
            duplicate.TargetId = owner.TargetId;
            duplicate.Mode = WorkerMode.Mining; duplicate.Cell = owner.WorkCell;
            duplicate.Action = new TimedAction { Kind = ActionKind.Hit, Duration = 1000000, Deadline = 1000000 };
            var sim = new OutpostSimulation(f.State, f.Map, f.Configs) { RecordEvents = false };
            Assert.That(sim.WorkCellOwner(owner.WorkCell), Is.EqualTo(owner.Id));
            Unique(sim);
        }

        [Test]
        public void LegacySaveService_LoadMigratesBeforeInvariantValidation_AndCanSaveAgain()
        {
            var f = Fixture(legacy: true);
            var miner = f.State.Run.Workers.Last();
            miner.Cell = miner.WorkCell = f.Map.Cell(f.Map.Topology.ExitColumn, 5);
            miner.Mode = WorkerMode.Mining; miner.Path = null;
            miner.Action = new TimedAction { Kind = ActionKind.Hit, Duration = 1000000, Deadline = 1000000 };
            // Real pre-compact payloads have neither of these fields.
            var json = JObject.Parse(OutpostJson.Serialize(f.State));
            ((JObject)json["Run"]["MapFinal"]).Remove("ApproachRocks");
            ((JObject)json["Run"]["MapFinal"]).Remove("MigratedFromVersion");
            f.State = OutpostJson.Deserialize<OutpostGameState>(json.ToString());
            var fs = new MemoryFileSystemA();
            var service = new OutpostSaveService(SaveKitA.Root, fs)
            {
                Configs = f.Configs, MapResolver = s => OutpostLevelAdapter.ForRun(TestData.Balance, s.Run),
            };
            Assert.That(service.Save(f.State, map: f.Map).Ok, Is.True);
            var result = service.Load();
            Assert.That(result.State, Is.Not.Null, string.Join("; ", result.Messages));
            Assert.That(result.State.Run.MapFinal.IsCompact, Is.True);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, result.State.Run);
            Unique(new OutpostSimulation(result.State, map, f.Configs) { RecordEvents = false });
            Assert.That(service.Save(result.State, map: map).Ok, Is.True);
            var again = service.Load();
            Assert.That(OutpostJson.Serialize(again.State), Is.EqualTo(OutpostJson.Serialize(result.State)));
        }

        [Test]
        public void Geometry_All144MinesHaveFourWideNeckAndDeterministicRockContent()
        {
            var cfg = Configs;
            for (int mine = 3; mine <= 144; mine++)
            {
                var map = OutpostLevelAdapter.BuildMine(TestData.Balance, mine, cfg);
                Assert.That(map.Final.Width, Is.EqualTo(4), "Mine " + mine);
                Assert.That(map.Final.Depth, Is.EqualTo(2));
                Assert.That(map.Targets.Last().WorkCells.Length, Is.EqualTo(4));
                Assert.That(map.Final.ApproachRocks.Count, Is.EqualTo(8));
                Assert.That(map.Targets.Select(t => t.Id).Distinct().Count(), Is.EqualTo(map.Targets.Length));
                for (int row = 1; row < map.Final.AddedRows; row++)
                    Assert.That(Enumerable.Range(row * map.Width, map.Width).Count(map.IsPlayable), Is.EqualTo(4));
                foreach (var piece in map.Final.ApproachRocks)
                    Assert.That(map.InitialOccupant[map.Cell(piece.Column, piece.Row)], Is.EqualTo(piece.TargetId));
                var reachable = OutpostReachability.Check(map);
                Assert.That(reachable.Passed, Is.True, "Mine " + mine + ": " + reachable.Diagnostic);
            }
        }

        [Test]
        public void LegacyMigration_CompletedRunRetainsCompletionReceiptAndDoesNotReopenWork()
        {
            var f = Fixture(legacy: true); var run = f.State.Run;
            var final = run.Targets.Last();
            final.Destroyed = true; final.RemainingHpUnits = 0; final.ReleasedUnits = final.TotalUnits; final.UpdateDamageStage();
            run.Counters.ReleasedUnits = run.Counters.DeliveredUnits = final.TotalUnits;
            run.Counters.ReleasedValue = run.Counters.DeliveredValue = final.TotalUnits * 5;
            run.TotalCredited = run.Counters.DeliveredValue; run.Wallet = run.TotalCredited + run.BonusCredited;
            run.Counters.ClearedTargets = run.Targets.Count; run.Phase = RunPhase.ReadyToContinue;
            run.CompletionRewardKey = "completed-legacy-final";
            f.State.Profile.RewardLog.Add(new RewardRecord { Key = run.CompletionRewardKey, Zone = 3, RunId = run.RunId });
            foreach (var worker in run.Workers)
            {
                worker.Mode = WorkerMode.Idle; worker.Action = null; worker.Path = null;
                worker.WorkCell = -1; worker.TargetId = -1; worker.Cell = f.Map.SpawnCell;
            }
            string profile = OutpostJson.Serialize(f.State.Profile), targets = OutpostJson.Serialize(run.Targets);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, run);
            var sim = new OutpostSimulation(f.State, map, f.Configs) { RecordEvents = false };
            Unique(sim);
            Assert.That(sim.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
            Assert.That(sim.Run.CompletionRewardKey, Is.EqualTo("completed-legacy-final"));
            Assert.That(OutpostJson.Serialize(f.State.Profile), Is.EqualTo(profile));
            Assert.That(OutpostJson.Serialize(run.Targets), Is.EqualTo(targets));
            Assert.That(sim.Run.Workers.All(w => w.WorkCell < 0 && w.Action == null), Is.True);
        }

        [Test]
        public void LegacyMigration_UnsafeCargoPathReleasesReservationWithoutMovingCargo()
        {
            var f = Fixture(legacy: true); var run = f.State.Run;
            int col = f.Map.Topology.ExitColumn, retained = f.Map.Cell(col, 5);
            SeedFinalCargo(f.State, retained);
            var worker = run.Workers.Last();
            worker.Cell = retained; worker.WorkCell = -1; worker.TargetId = -1;
            worker.Mode = WorkerMode.MovingToCargo; worker.CargoBatchSeq = run.Batches.Single().Seq;
            worker.TaskId = run.NextTaskId++;
            worker.Path = Enumerable.Range(1, 4).Select(n => retained - n).Concat(Enumerable.Range(0, 4).Select(n => retained - 3 + n)).ToArray();
            worker.PathIndex = 0;
            worker.Action = new TimedAction { Kind = ActionKind.Edge, Duration = 400000, Deadline = 400000 };
            var unit = run.Batches.Single().Units.First(); unit.ReservedBy = worker.Id; unit.ReservedTask = worker.TaskId;
            Assert.That(OutpostInvariants.Check(f.State, f.Map, f.Configs.Equipment, f.Configs.Cards), Is.Empty);
            string targets = OutpostJson.Serialize(run.Targets);
            var values = run.Batches.Single().Units.Select(u => u.Value).ToArray();
            var ordinals = run.Batches.Single().Units.Select(u => u.Ordinal).ToArray();
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, run);
            Assert.That(run.MapFinal.IsCompact, Is.True);
            Assert.That(worker.Mode, Is.EqualTo(WorkerMode.Idle));
            Assert.That(worker.CargoBatchSeq, Is.EqualTo(-1));
            Assert.That(run.Batches.Single().Cell, Is.EqualTo(retained));
            Assert.That(run.Batches.Single().Units.Select(u => u.Value), Is.EqualTo(values));
            Assert.That(run.Batches.Single().Units.Select(u => u.Ordinal), Is.EqualTo(ordinals));
            Assert.That(run.Batches.Single().Units.All(u => u.ReservedBy == 0 && u.ReservedTask == 0), Is.True);
            Assert.That(OutpostJson.Serialize(run.Targets), Is.EqualTo(targets));
            Unique(new OutpostSimulation(f.State, map, f.Configs) { RecordEvents = false });
        }
    }
}
