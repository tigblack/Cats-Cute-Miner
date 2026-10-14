using System;
using System.Linq;
using CatMiners.Core.Data;

namespace CatMiners.Core.Outpost
{
    /// <summary>Prepare a wide layout on a copy; the caller commits it before displaying it.</summary>
    public static class MineTopologyMigration
    {
        public static bool TryPrepare(OutpostGameState original, BalanceRepository repo, OutpostConfigSet configs,
            out OutpostGameState prepared, out OutpostMap map, out string diagnostic)
        {
            prepared = null;
            map = null;
            diagnostic = null;
            var run = original?.Run;
            if (run == null || configs?.Topologies == null ||
                run.MapTopology?.Version == MineTopologyCatalog.SupportedVersion) return false;
            try
            {
                if (run.SourceSha256 != repo.Sha256 || configs.Topologies.SourceSha256 != repo.Sha256)
                    throw new ArgumentException("Topology migration source checksum mismatch");
                var snapshot = configs.Topologies.For(run.ZoneId);
                if (snapshot == null || snapshot.Version != MineTopologyCatalog.SupportedVersion) return false;
                // Saved content overrides own target identity. Never adopt a changed content catalog here.
                var source = OutpostLevelAdapter.BuildWithOverride(repo, run.Zone, run.MapOverride);
                // Very early saves predate the extra zone-1 content. They can use the same
                // tunnel mask with only their existing IDs; migration never adds rewards.
                if (snapshot.Placements.Length != source.Targets.Length)
                {
                    snapshot = OutpostJson.DeepCopy(snapshot);
                    var ids = new System.Collections.Generic.HashSet<int>(source.Targets.Select(t => t.Id));
                    snapshot.Placements = snapshot.Placements.Where(p => ids.Contains(p.TargetId)).ToArray();
                }
                var candidateMap = MineTopologyAdapter.Apply(source, snapshot);
                var oldMap = OutpostLevelAdapter.ForRun(repo, run);
                if (oldMap.Targets.Length != candidateMap.Targets.Length || run.Targets.Count != candidateMap.Targets.Length)
                    throw new ArgumentException("Topology migration target count mismatch");
                for (int i = 0; i < oldMap.Targets.Length; i++)
                {
                    var a = oldMap.Targets[i];
                    var b = candidateMap.Targets[i];
                    if (a.Id != b.Id || run.Targets[i].Id != b.Id || a.Kind != b.Kind || a.TypeId != b.TypeId ||
                        a.SourceLevel != b.SourceLevel || a.Sector != b.Sector || a.RewardId != b.RewardId ||
                        !a.IsGate && a.Cells.Length != b.Cells.Length)
                        throw new ArgumentException("Topology migration content mismatch: " + a.Id);
                }
                var reachable = OutpostReachability.Check(candidateMap);
                if (!reachable.Passed) throw new ArgumentException("Topology migration route mismatch: " + reachable.Diagnostic);
                var errors = OutpostInvariants.Check(original, oldMap, configs.Equipment, configs.Cards);
                if (errors.Count != 0) throw new ArgumentException(string.Join("; ", errors));
                var candidate = OutpostJson.DeepCopy(original);
                var next = candidate.Run;
                next.MapTopology = OutpostJson.DeepCopy(snapshot);
                // Coordinates and in-flight reservations belong to the old graph. Keep every carried
                // and ground unit, bring them to the entrance, and assign real paths on the new graph.
                next.Queue.Clear();
                foreach (var slot in next.Slots) { slot.WorkerId = 0; slot.TaskId = 0; }
                foreach (var batch in next.Batches)
                {
                    batch.Cell = candidateMap.IntakeCell;
                    foreach (var unit in batch.Units) { unit.ReservedBy = 0; unit.ReservedTask = 0; }
                }
                foreach (var worker in next.Workers)
                {
                    worker.Cell = candidateMap.SpawnCell;
                    worker.Mode = WorkerMode.Idle;
                    worker.Action = null;
                    worker.Path = null;
                    worker.PathIndex = 0;
                    worker.TaskId = 0;
                    worker.TargetId = -1;
                    worker.WorkCell = -1;
                    worker.CargoBatchSeq = -1;
                    worker.DepotSlot = -1;
                    worker.TaskInvalid = false;
                    worker.MoveRetryAttempted = false;
                    worker.LastMoveProgressTick = next.Now;
                    foreach (var unit in worker.Bag) { unit.ReservedBy = 0; unit.ReservedTask = 0; }
                }
                next.NavVersion++;
                next.PlanningDirty = true;
                next.LastChangeTick = next.Now;
                var simulation = new OutpostSimulation(candidate, candidateMap, configs) { RecordEvents = false };
                simulation.PlanNow(); // No time, hits, pickups, unloads, or reward draws are processed.
                errors = OutpostInvariants.Check(candidate, candidateMap, configs.Equipment, configs.Cards);
                if (errors.Count != 0) throw new ArgumentException(string.Join("; ", errors));
                if (OutpostJson.Serialize(candidate.Profile) != OutpostJson.Serialize(original.Profile) ||
                    OutpostJson.Serialize(next.Targets) != OutpostJson.Serialize(run.Targets) ||
                    next.Wallet != run.Wallet || next.TotalCredited != run.TotalCredited || next.TotalSpent != run.TotalSpent ||
                    next.Now != run.Now || next.LastProcessedUtcMs != run.LastProcessedUtcMs || next.Phase != run.Phase ||
                    next.GatesOpened != run.GatesOpened || next.CompletionRewardKey != run.CompletionRewardKey)
                    throw new ArgumentException("Topology migration changed progress");
                prepared = candidate;
                map = candidateMap;
                return true;
            }
            catch (Exception e)
            {
                // An incompatible saved override finishes on its own snapshot; a new mine adopts the catalog.
                diagnostic = e.Message;
                return false;
            }
        }
    }
}
