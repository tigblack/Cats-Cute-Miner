using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>Intentional final-phase spikes. Never feed these values into ordinary mine pace smoothing.</summary>
    public sealed class FinalExcavationRules
    {
        public string Version = FinalExcavation.CompactVersion;
        public int FromMine = 3;
        public int AddedRows = 8;
        public int Width = 4;
        public int Depth = 2;
        // Frozen reference-fixture budget only; never a player hiring discount.
        public Rational ReferenceHiringCostFactor = Rational.Of(1, 100);
        public List<FinalExcavationBand> Bands = new List<FinalExcavationBand>();
        /// <summary>
        /// Historical fixture effective/nominal throughput factors; deterministic interpolation between sampled mines.
        /// Compact HP caps the nominal crew at four. These factors have not been remeasured for four miners' cargo trips.
        /// </summary>
        public SortedDictionary<int, Rational> ThroughputFactors = new SortedDictionary<int, Rational>();
        public Rational ThroughputFor(int mine)
        {
            if(ThroughputFactors.Count==0)return Rational.Of(19,20);
            int low=-1,high=-1;Rational a=Rational.One,b=Rational.One;
            foreach(var kv in ThroughputFactors)
            {
                if(kv.Key==mine)return kv.Value;
                if(kv.Key<mine){low=kv.Key;a=kv.Value;}
                else{high=kv.Key;b=kv.Value;break;}
            }
            if(low<0)return b;if(high<0)return a;
            return a+(b-a)*Rational.Of(mine-low,high-low);
        }
        public FinalExcavationBand ForMine(int mine)
        {
            FinalExcavationBand found = null;
            foreach (var b in Bands) if (mine >= b.FromMine && (found == null || b.FromMine > found.FromMine)) found = b;
            return found;
        }
        public static FinalExcavationRules Parse(string json)
        {
            var r = OutpostJson.Deserialize<FinalExcavationRules>(json);
            if (r == null || string.IsNullOrEmpty(r.Version) || r.FromMine < 3 || r.AddedRows < r.Depth + 4 || r.Width < 4 || r.Depth < 2 ||
                r.Version == FinalExcavation.CompactVersion && (r.Width != 4 || r.Depth != 2) ||
                r.ReferenceHiringCostFactor.Sign <= 0 || r.ReferenceHiringCostFactor > Rational.One || r.Bands == null || r.Bands.Count == 0)
                throw new OutpostConfigException("Invalid final excavation rules");
            foreach (var b in r.Bands)
                if (b.FromMine < 3 || b.TargetSeconds < 1 || b.RewardUnits < 1 || b.RewardUnits > 10000)
                    throw new OutpostConfigException("Invalid final excavation band");
            foreach(var kv in r.ThroughputFactors)
                if(kv.Key<3||kv.Value.Sign<=0)throw new OutpostConfigException("Invalid final excavation throughput");
            return r;
        }
    }
    public sealed class FinalExcavationBand
    {
        public int FromMine;
        /// <summary>Reference-fixture calibration, not a guaranteed entering-team completion time.</summary>
        public int TargetSeconds;
        public int RewardUnits;
        public string Theme;
    }
    /// <summary>Immutable run snapshot: layout, static HP, finite cargo budget and balance exemption.</summary>
    public sealed class FinalExcavation
    {
        public const string CompactVersion = "final-excavations-2";
        public string Version;
        public string Category = "mandatory-final-excavation";
        public bool IntentionalDurationSpike = true;
        public int TargetId, AddedRows, Column, Width, Depth;
        public int TargetSeconds;
        public string Theme;
        public long MaxHp;
        public int RewardUnits;
        /// <summary>One-cell ordinary rocks in the added neck. Empty for legacy runs; never regenerated on load.</summary>
        public List<DressingPiece> ApproachRocks = new List<DressingPiece>();
        /// <summary>Legacy runs retain their exact HP, reward budget and already open approach.</summary>
        public string MigratedFromVersion;
        [Newtonsoft.Json.JsonIgnore] public bool IsCompact => Version == CompactVersion;
        // Legacy save field. Ignored by live prices; retained so existing run snapshots round-trip unchanged.
        public Rational HiringCostFactor;
    }
}
