using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    public class FinalExcavationCalibration
    {
        static readonly int[] Sample = { 3,4,5,6,7,10,16,20,23,30,37,46,49,59,66,70,80,91,105,110,120,130,144 };
        static OutpostConfigSet Cfg => OutpostConfigSet.LoadDefault().WithCards(TestData.Balance);

        // Reproducible earned-income policy: only ordinary mining + normal factory delivery/sales, every 15 seconds.
        // Prioritize full hiring, then a bounded working set. No taps, temporary boosts, debug currency or invented ranks.
        static void Buy(OutpostSimulation sim)
        {
            foreach (var f in sim.Run.Factories.Where(f=>f.Status==FactoryStatus.Ready).ToArray()) sim.Submit(OutpostCommand.DeliverFactory(f.TargetId));
            foreach (var kv in sim.Run.Warehouse.Where(kv=>kv.Value>0).ToArray()) sim.Submit(OutpostCommand.SellGoods(kv.Key));
            int[] goals = { 10, Math.Min(20,sim.Run.Zone+3), Math.Min(15,sim.Run.Zone+2), Math.Min(16,sim.Run.Zone+2), Math.Min(20,sim.Run.Zone+2), 5, 2 };
            for (int round=0; round<25; round++)
            {
                bool changed=false;
                foreach (var br in new[] { UpgradeBranch.Team,UpgradeBranch.Pickaxes,UpgradeBranch.Bags,UpgradeBranch.Boots,UpgradeBranch.Unload,UpgradeBranch.Value,UpgradeBranch.Bays })
                {
                    var q=sim.Quote(br,BuyMode.One);
                    if (sim.Run.Level(br)>=(br==UpgradeBranch.Team?q.Cap:goals[(int)br])) continue;
                    if (q.Levels>0 && sim.Submit(OutpostCommand.BuyUpgrade(br)).Ok) changed=true;
                }
                if(!changed) break;
            }
        }

        static OutpostGameState Reference(int z, OutpostConfigSet cfg)
        {
            var plain=OutpostLevelAdapter.BuildMine(TestData.Balance,z,cfg.WithFinalExcavations(null));
            var reference=FinalExcavationReference.Build(plain,cfg,TestData.Balance);
            var map=OutpostLevelAdapter.BuildMine(TestData.Balance,z,cfg);
            var run=StageProgressionService.CreateRun(reference.Profile,z,map,cfg,TestData.Balance,0);
            var state=new OutpostGameState {Profile=reference.Profile,Run=run};
            run.Levels=(int[])reference.Levels.Clone();
            run.Workers.Clear();run.NextWorkerId=1;
            for(int i=0;i<reference.Workers;i++)run.Workers.Add(new WorkerState {Id=run.NextWorkerId++,Look=i%12,Cell=map.SpawnCell,Mode=WorkerMode.ChoosingTask});
            run.Slots=Enumerable.Range(0,2+run.Level(UpgradeBranch.Bays)).Select(_=>new DepotSlotState()).ToArray();
            foreach(var t in run.Targets)
            {
                if(map.Target(t.Id).IsFinal)continue;
                t.RemainingHpUnits=0;t.Destroyed=true;t.ReleasedUnits=t.TotalUnits;t.UpdateDamageStage();
                run.Counters.ClearedTargets++;run.Counters.ReleasedUnits+=t.TotalUnits;run.Counters.DeliveredUnits+=t.TotalUnits;
            }
            // Explicit post-ordinary fixture: every ordinary cargo unit delivered at its unupgraded real price.
            // It does not model the duration or purchase history of the ordinary phase.
            run.TotalCredited=reference.Income;run.TotalSpent=reference.Spent;run.Wallet=reference.Income-reference.Spent;
            run.Counters.ReleasedValue=reference.Income;run.Counters.DeliveredValue=reference.Income;run.Counters.FirstDeliveryTick=0;
            run.GatesOpened=map.Gates.Length;
            foreach(var f in run.Factories){f.Status=FactoryStatus.Working;f.CycleStart=0;f.CycleEnd=run.FactoryRules.ForType(f.TypeId).CycleTicks;}
            var sim=new OutpostSimulation(state,map,cfg) {RecordEvents=false};
            sim.Submit(OutpostCommand.EquipBest());
            var target=run.Targets.Last();
            for(int i=0;i<100000&&target.FirstStrikeTick<0;i++)Assert.That(sim.AdvanceToNextEvent(),Is.True);
            Assert.That(target.FirstStrikeTick,Is.GreaterThanOrEqualTo(0));
            Assert.That(OutpostInvariants.Check(state,map,cfg.Equipment,cfg.Cards),Is.Empty);
            return state;
        }

        static (double Seconds,double Drain,int MaxMining) Measure(OutpostGameState start,OutpostConfigSet cfg,long hp,bool purchases=false)
        {
            var state=OutpostJson.DeepCopy(start); var run=state.Run; var target=run.Targets.Last();
            // Calibration only, before measuring. Production never changes spawned maximum HP.
            long oldDamage=target.MaxHpUnits-target.RemainingHpUnits;
            target.MaxHpUnits=checked(hp*OutpostIds.HpUnitsPerHp); target.RemainingHpUnits=target.MaxHpUnits-oldDamage;
            run.MapFinal.MaxHp=hp;
            var sim=new OutpostSimulation(state,OutpostLevelAdapter.ForRun(TestData.Balance,run),cfg) { RecordEvents=false };
            int maxMining=0,guard=0; long buyAt=sim.Now;
            while(!target.Destroyed && !run.WatchdogFired && guard++<4000000)
            {
                maxMining=Math.Max(maxMining,run.Workers.Count(w=>w.Mode==WorkerMode.Mining&&w.TargetId==target.Id));
                if(purchases&&sim.Now>=buyAt) { Buy(sim); buyAt=sim.Now+15*sim.Balance.TicksPerSecond; }
                if(!sim.AdvanceToNextEvent())break;
            }
            Assert.That(target.Destroyed,Is.True,"destroy mine "+run.Zone+" "+run.WatchdogDiagnostic);
            long destroyed=target.DestroyedTick;
            while(run.Phase!=RunPhase.ReadyToContinue && !run.WatchdogFired && guard++<4500000) if(!sim.AdvanceToNextEvent())break;
            Assert.That(run.Phase,Is.EqualTo(RunPhase.ReadyToContinue));
            var errors=OutpostInvariants.Check(state,sim.Map,cfg.Equipment,cfg.Cards);
            Assert.That(errors,Is.Empty);
            return ((destroyed-target.FirstStrikeTick)/(double)sim.Balance.TicksPerSecond,(run.Counters.CompletedTick-destroyed)/(double)sim.Balance.TicksPerSecond,maxMining);
        }

        [Test]
        public void CalibrateSample()
        {
            var cfg=Cfg; var report=new List<object>(); var hpTable=new Dictionary<int,long>(); var factors=new Dictionary<int,string>();
            string dir=Path.Combine(TestData.ProjectRoot,"Docs/Reports/final-excavations");Directory.CreateDirectory(dir);
            foreach(int z in Sample)
            {
                var start=Reference(z,cfg); var final=start.Run.MapFinal;
                var stats=TeamStats.Compute(start.Run.Balance,start.Run.Levels,start.Profile,cfg.Equipment,z,cfg.Cards,start.Run.ExtraWorkers);
                Assert.That(start.Run.Workers.Count,Is.EqualTo(start.Run.Balance.StartWorkers+start.Run.Balance.Branch(UpgradeBranch.Team).Cap+stats.CardTeamCap),"Full hiring is affordable in mine "+z);
                long hp=final.MaxHp; var m=Measure(start,cfg,hp);
                for(int i=0;i<3&&Math.Abs(m.Seconds-final.TargetSeconds)>final.TargetSeconds*.005;i++)
                { hp=Math.Max(100,(long)Math.Round(hp*final.TargetSeconds/m.Seconds));m=Measure(start,cfg,hp); }
                hpTable[z]=hp;
                var nominal=stats.DamageUnitsFor('x',"final-excavation")/(double)OutpostIds.HpUnitsPerHp*start.Run.Workers.Count;
                factors[z]=new Rational(hp, (long)Math.Round(nominal*final.TargetSeconds)).ToString();
                var row=new { Mine=z, Workers=start.Run.Workers.Count,Upgrades=start.Run.Levels, Items=start.Profile.Items,Equipped=start.Profile.Equipped,
                    Cards=start.Profile.Cards, MaxHp=hp,EffectiveDps=hp/m.Seconds,TargetSeconds=final.TargetSeconds,MeasuredSeconds=m.Seconds,
                    OutstandingDeliverySeconds=m.Drain,MaxConcurrentMiners=m.MaxMining,Policy="explicit-post-ordinary-fixture; real-price affordable ranks; frozen from first strike",Seed=1, OrdinaryIncome=start.Run.TotalCredited.ToString(),UpgradeSpend=start.Run.TotalSpent.ToString() };
                report.Add(row);TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(row));
                File.WriteAllText(Path.Combine(dir,"timings.json"),Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
                File.WriteAllText(Path.Combine(dir,"hp-calibration.json"),Newtonsoft.Json.JsonConvert.SerializeObject(hpTable,Newtonsoft.Json.Formatting.Indented));
                File.WriteAllText(Path.Combine(dir,"throughput-factors.json"),Newtonsoft.Json.JsonConvert.SerializeObject(factors,Newtonsoft.Json.Formatting.Indented));
                long priorDamage=start.Run.Targets.Last().MaxHpUnits-start.Run.Targets.Last().RemainingHpUnits;
                start.Run.MapFinal.MaxHp=hp;start.Run.Targets.Last().MaxHpUnits=hp*OutpostIds.HpUnitsPerHp;start.Run.Targets.Last().RemainingHpUnits=hp*OutpostIds.HpUnitsPerHp-priorDamage;
                File.WriteAllText(Path.Combine(dir,"reference-mine"+z+".json"),OutpostJson.Serialize(start));
            }
        }
    }
}
