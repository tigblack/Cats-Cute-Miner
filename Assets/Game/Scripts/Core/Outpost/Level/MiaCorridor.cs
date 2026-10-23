using System;

namespace CatMiners.Core.Outpost
{
    /// <summary>Authored Mine 1. Two lanes, a small right debris pocket, and Mia; fifteen targets, no random ore fill.</summary>
    public static class MiaCorridor
    {
        public const string Layout = "mia-two-lane-mine-2";

        public static OutpostMap PlaceRescue(OutpostMap map, MineDressingRules rules, RescueRules rescue)
        {
            if (rescue == null) return map;
            int center = map.Width / 2;
            int id = 0;
            foreach (var target in map.Targets) id = Math.Max(id, target.Id + 1);
            var tier = rules.Tiers[rescue.LockTier];
            var dressing = new MineDressing { ZoneId = map.ZoneId, RulesVersion = Layout };
            dressing.Props.Add(new DressingProp
            {
                Family = rescue.CageFamily, Column = center + 1, Row = 1, W = 1, H = 1, PipeMount = false,
            });
            var light = rules.Tiers["Light"];
            dressing.Pieces.Add(new DressingPiece
            {
                TargetId = id++, Family = "planks", Tier = "Light", HpFactor = light.HpFactor, ValueFactor = light.ValueFactor,
                Column = center + 1, Row = 6, Branch = 1,
            });
            dressing.Pieces.Add(new DressingPiece
            {
                TargetId = id++, Family = "rubble", Tier = "Light", HpFactor = light.HpFactor, ValueFactor = light.ValueFactor,
                Column = center + 1, Row = 5, Branch = 1,
            });
            dressing.Pieces.Add(new DressingPiece
            {
                TargetId = id, Family = rescue.LockFamily, Tier = rescue.LockTier,
                HpFactor = tier.HpFactor, ValueFactor = tier.ValueFactor, Column = center, Row = 1,
            });
            dressing.Rescue = new DressingRescue
            {
                Character = rescue.Character, CageProp = 0, LockTargetId = id, RouteFocus = rescue.RouteFocus,
            };
            var final = MineDressingAdapter.Apply(map, dressing);
            dressing.Rescue.RouteTargetIds = MineRescuePlacer.Route(final, id);
            final = MineDressingAdapter.Apply(map, dressing);
            var reachable = OutpostReachability.Check(final);
            if (!reachable.Passed) throw new OutpostConfigException("Mia corridor: " + reachable.Diagnostic);
            return final;
        }
    }
}
