using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    public class FinalExcavationBaseline
    {
        [Test]
        public void CaptureOrdinaryHp()
        {
            var cfg = OutpostConfigSet.LoadDefault().WithCards(TestData.Balance);
            var rows = new List<object>();
            foreach (int z in new[] { 1,2,3,4,5,6,7,10,16,20,23,30,37,46,49,59,66,70,80,91,105,110,120,130,144 })
            {
                var map = OutpostLevelAdapter.BuildMine(TestData.Balance, z, cfg);
                var state = StageProgressionService.NewGame(TestData.Balance, cfg, 1, 0, z);
                var run = state.Run;
                BigInteger total = 0, team = 0;
                foreach (var t in run.Targets) total += t.TotalUnits * (t.ValueFactor * run.Balance.CoinBase * run.Balance.ZoneMoneyScale(z)).Ceil();
                for (int l = 0; l < run.Balance.Branch(UpgradeBranch.Team).Cap; l++)
                    team += TeamUpgradeService.NextCost(run.Balance,z,UpgradeBranch.Team,l,priceFactor:run.PriceFactor).Value;
                rows.Add(new { Mine=z,Targets=run.Targets.Select(t=>new {t.Id,t.Kind,t.MaxHpUnits,t.TotalUnits}).ToArray(), Gross=total.ToString(), TeamCost=team.ToString() });
                TestContext.WriteLine("Mine "+z+" targets="+run.Targets.Count+" gross="+total+" fullCrewCost="+team);
            }
            string dir=Path.Combine(TestData.ProjectRoot,"Docs/Reports/final-excavations"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir,"ordinary-hp-before.json"),Newtonsoft.Json.JsonConvert.SerializeObject(rows,Newtonsoft.Json.Formatting.Indented));
        }
    }
}
