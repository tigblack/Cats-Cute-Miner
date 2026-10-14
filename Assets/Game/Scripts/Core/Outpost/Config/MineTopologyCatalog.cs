using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>Geometry only. The immutable balance source still owns every target and reward.</summary>
    [Serializable]
    public sealed class MineTopologyCatalog
    {
        public const string SupportedVersion = "wide-branching-mines-2";
        public const string LegacyVersion = "branching-mines-1";
        public static bool SupportsSnapshot(string version) => version == SupportedVersion || version == LegacyVersion;
        public string Version;
        public string SourceSha256;
        public Dictionary<string, MineTopology> Zones = new Dictionary<string, MineTopology>(StringComparer.Ordinal);

        public static MineTopologyCatalog Parse(string json)
        {
            var result = OutpostJson.Deserialize<MineTopologyCatalog>(json);
            if (result == null || !SupportsSnapshot(result.Version) || result.Zones == null || result.Zones.Count != OutpostIds.LastZone)
                throw new ArgumentException("Invalid mine topology catalog");
            for (int z = 1; z <= OutpostIds.LastZone; z++)
            {
                string id = "zone" + z;
                if (!result.Zones.TryGetValue(id, out var mine) || mine == null || mine.ZoneId != id)
                    throw new ArgumentException("Missing mine topology: " + id);
                mine.Validate();
            }
            return result;
        }

        public MineTopology For(string zoneId) => Zones.TryGetValue(zoneId, out var result) ? result : null;
    }

    /// <summary>Full geometry snapshot is saved with a run; config edits never move an active cat or cargo.</summary>
    [Serializable]
    public sealed class MineTopology
    {
        public string Version;
        public string ZoneId;
        public string Layout;
        public int Width;
        public int Depth;
        /// <summary>Path-edge penalty per worker already assigned in the same branch; zero keeps legacy selection.</summary>
        public int BranchCrowdingPenalty;
        /// <summary>'.' = playable, '#' = permanent structural rock. Camp is appended by the adapter.</summary>
        public string[] FloorRows;
        /// <summary>Explicit sector for each playable cell, including gates on the preceding sector's boundary.</summary>
        public string[] SectorRows;
        public int ExitColumn;
        public int ExitRow;
        public MinePlacement[] Placements;
        public MineGatePlacement[] Gates;

        public void Validate()
        {
            if (!MineTopologyCatalog.SupportsSnapshot(Version) || Width < 5 || Width > 96 || Depth < 1 || Depth > 256 ||
                BranchCrowdingPenalty < 0 || BranchCrowdingPenalty > 16 ||
                FloorRows == null || FloorRows.Length != Depth || SectorRows == null || SectorRows.Length != Depth ||
                Placements == null || Gates == null || ExitColumn < 0 || ExitColumn >= Width || ExitRow < 0 || ExitRow >= Depth)
                throw new ArgumentException("Invalid topology snapshot: " + ZoneId);
            for (int r = 0; r < Depth; r++)
            {
                if (FloorRows[r] == null || FloorRows[r].Length != Width || SectorRows[r] == null || SectorRows[r].Length != Width)
                    throw new ArgumentException("Invalid topology row: " + ZoneId + ":" + r);
                for (int c = 0; c < Width; c++)
                    if (FloorRows[r][c] == '#')
                    {
                        if (SectorRows[r][c] != '#') throw new ArgumentException("Sector in structural rock");
                    }
                    else if (FloorRows[r][c] != '.' || SectorRows[r][c] < '0' || SectorRows[r][c] > '9' || SectorRows[r][c] - '0' > Gates.Length)
                        throw new ArgumentException("Invalid playable cell: " + ZoneId);
            }
        }
    }

    [Serializable]
    public sealed class MinePlacement
    {
        public int TargetId;
        public int Column;
        public int Row;
        public int Branch;
    }

    [Serializable]
    public sealed class MineGatePlacement
    {
        public int TargetId;
        public int[] Cells;
    }
}
