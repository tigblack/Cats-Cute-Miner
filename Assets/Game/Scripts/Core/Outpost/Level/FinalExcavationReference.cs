using System;
using System.Numerics;
using CatMiners.Core.Data;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Declared conservative reference fixture, never the entering player's strength. The ordinary mine's base cargo pays
    /// for local ranks under the frozen historical benchmark budget. No factory sales, paid boosts or maximum-rarity collection needed.
    /// Guaranteed Mine2 card L2 + starter pickaxe L1; population card L1 when normally obtainable.
    /// </summary>
    public sealed class FinalExcavationReference
    {
        public OutpostProfile Profile;
        public int[] Levels;
        public BigInteger Income, Spent;
        public TeamStats Stats;
        public int Workers;
        public Rational HiringCostFactor;
        // Fixed purchase policy of the published benchmark, independent of live Base unlock rules.
        // Unlocking more choices for the player must not silently increase a newly spawned final target's HP.
        static readonly int[] ReferencePurchaseMines = { 1, 1, 3, 2, 4, 5, 6 };

        public static FinalExcavationReference Build(OutpostMap ordinary, OutpostConfigSet configs, BalanceRepository repo)
        {
            var cfg=configs.Cards==null?configs.WithCards(repo):configs;
            int z=ordinary.Number;
            var p=StageProgressionService.NewProfile(cfg,1,z);
            p.CompletedZones.Add(1);p.CompletedZones.Add(2);
            ProgressionService.Rescue(p,"mia");ProgressionService.Rescue(p,"bruno");
            p.Cards["ca001"]=new CardProgress {Level=2};
            var population=cfg.Cards.Get("ca038");
            if(population!=null&&z>=population.MineUnlock) p.Cards[population.Id]=new CardProgress {Level=1};
            EquipmentService.Grant(p,cfg.Progression?.EquipmentIntro.Item??"pickaxe.common",cfg);
            p.Equipped[(int)EquipSlot.Pickaxe]=cfg.Progression?.EquipmentIntro.Item??"pickaxe.common";
            var b=cfg.Balance;
            var balances=OutpostLevelBalanceAdapter.ApplyPace(ordinary,b,OutpostLevelBalanceAdapter.Compute(ordinary,b,repo));
            var price=StageProgressionService.DressingPriceFactor(ordinary,balances);
            var levels=new int[OutpostIds.BranchCount];
            var stats=TeamStats.Compute(b,levels,p,cfg.Equipment,z,cfg.Cards);
            BigInteger income=0,spent=0;
            for(int i=0;i<ordinary.Targets.Length;i++)
                income+=stats.CoinValueFor(ordinary.Targets[i].Kind,ordinary.Targets[i].TypeId,balances[i].ValueFactor)*balances[i].TotalCargoUnits;
            int team=b.Branch(UpgradeBranch.Team).Cap+stats.CardTeamCap;
            // Keep the published HP calibration stable. This fixture subsidy never enters live prices.
            var hiring=cfg.FinalExcavations?.ReferenceHiringCostFactor??Rational.One;
            BigInteger fullBill=0;
            for(int l=0;l<team;l++) fullBill+=TeamUpgradeService.NextCost(b,z,UpgradeBranch.Team,l,
                TeamUpgradeService.DiscountFor(stats,UpgradeBranch.Team),stats.CardTeamCap,price).Value;
            // Protect sparse, unsampled mine layouts without changing any ordinary HP or rewards.
            if((hiring*fullBill).Ceil()>income) hiring=new Rational(income*3,fullBill*5);
            for(int l=0;l<team;l++)
            {
                var c=TeamUpgradeService.NextCost(b,z,UpgradeBranch.Team,l,TeamUpgradeService.DiscountFor(stats,UpgradeBranch.Team),stats.CardTeamCap,
                    price*hiring).Value;
                if(spent+c>income)throw new OutpostConfigException("Final reference full crew is not affordable in Mine "+z);
                spent+=c;levels[(int)UpgradeBranch.Team]++;
            }
            int[] goals={team,Math.Min(20,z+3),Math.Min(15,z+2),Math.Min(16,z+2),Math.Min(20,z+2),5,2};
            for(int pass=0;pass<40;pass++)
            {
                bool changed=false;
                foreach(var branch in new[] {UpgradeBranch.Pickaxes,UpgradeBranch.Bags,UpgradeBranch.Boots,UpgradeBranch.Unload,UpgradeBranch.Value,UpgradeBranch.Bays})
                {
                    int i=(int)branch;
                    if(levels[i]>=goals[i])continue;
                    if(z<ReferencePurchaseMines[i])continue;
                    var cost=TeamUpgradeService.NextCost(b,z,branch,levels[i],priceFactor:price);
                    if(!cost.HasValue||spent+cost.Value>income)continue;
                    spent+=cost.Value;levels[i]++;changed=true;
                }
                if(!changed)break;
            }
            stats=TeamStats.Compute(b,levels,p,cfg.Equipment,z,cfg.Cards);
            return new FinalExcavationReference {Profile=p,Levels=levels,Income=income,Spent=spent,Stats=stats,Workers=stats.Workers,HiringCostFactor=hiring};
        }
    }
}
