using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CatMiners.Core.Outpost;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace CatMiners.Tests.Outpost
{
    public class FinalExcavationVerification
    {
        static OutpostConfigSet Cfg=>OutpostConfigSet.LoadDefault().WithCards(TestData.Balance);
        static string Root=>Path.Combine(TestData.ProjectRoot,"Docs/Reports/final-excavations");
        static OutpostGameState Reference(int mine)=>OutpostJson.Deserialize<OutpostGameState>(File.ReadAllText(Path.Combine(Root,"reference-mine"+mine+".json")));
        static OutpostSimulation Sim(OutpostGameState state)=>new OutpostSimulation(state,OutpostLevelAdapter.ForRun(TestData.Balance,state.Run),Cfg){RecordEvents=false};
        static double Finish(OutpostSimulation sim,bool buy=false)
        {
            var t=sim.Run.Targets.Last();long start=sim.Now,nextBuy=sim.Now;
            for(int i=0;!t.Destroyed&&i<2000000;i++)
            {
                if(buy&&sim.Now>=nextBuy)
                {
                    foreach(var br in new[]{UpgradeBranch.Pickaxes,UpgradeBranch.Bags,UpgradeBranch.Boots,UpgradeBranch.Unload})
                        if(sim.Quote(br,BuyMode.One).CanBuy)sim.Submit(OutpostCommand.BuyUpgrade(br));
                    nextBuy=sim.Now+30*sim.Balance.TicksPerSecond;
                }
                Assert.That(sim.AdvanceToNextEvent(),Is.True);
            }
            Assert.That(t.Destroyed,Is.True);
            Assert.That(OutpostInvariants.Check(sim.State,sim.Map,Cfg.Equipment,Cfg.Cards),Is.Empty);
            return(t.DestroyedTick-start)/(double)sim.Balance.TicksPerSecond;
        }

        [Test]
        public void ProductionHpModel_ReplaysAll23ReferenceTimingsWithoutRetuning()
        {
            var rows=new List<object>();
            foreach(var entry in JArray.Parse(File.ReadAllText(Path.Combine(Root,"timings.json"))))
            {
                int mine=(int)entry["Mine"];
                var state=Reference(mine);
                var fresh=OutpostLevelAdapter.BuildMine(TestData.Balance,mine,Cfg);
                long hp=fresh.Final.MaxHp;
                var t=state.Run.Targets.Last();long previous=t.MaxHpUnits-t.RemainingHpUnits;
                state.Run.MapFinal.MaxHp=hp;t.MaxHpUnits=hp*OutpostIds.HpUnitsPerHp;t.RemainingHpUnits=t.MaxHpUnits-previous;
                var sim=Sim(state);double actual=Finish(sim);
                if(mine<6)Assert.That(actual,Is.InRange(fresh.Final.TargetSeconds*.9,fresh.Final.TargetSeconds*1.1));
                else Assert.That(actual,Is.InRange(18000d,21600d));
                long destroyed=t.DestroyedTick;
                for(int guard=0;state.Run.Phase!=RunPhase.ReadyToContinue&&guard<100000;guard++)
                    Assert.That(sim.AdvanceToNextEvent(),Is.True);
                Assert.That(state.Run.Phase,Is.EqualTo(RunPhase.ReadyToContinue));
                double drain=(state.Run.Counters.CompletedTick-destroyed)/(double)sim.Balance.TicksPerSecond;
                rows.Add(new{Mine=mine,MaxHp=hp,Seconds=actual,OutstandingDeliverySeconds=drain,EffectiveDps=hp/actual});
            }
            File.WriteAllText(Path.Combine(Root,"production-verification.json"),Newtonsoft.Json.JsonConvert.SerializeObject(rows,Newtonsoft.Json.Formatting.Indented));
        }

        [TestCase(3)] [TestCase(6)] [TestCase(144)]
        public void WeakerReferenceAndBetterPickaxe_AndRealPurchases(int mine)
        {
            var reference=Reference(mine);long max=reference.Run.Targets.Last().MaxHpUnits;
            double normal=Finish(Sim(OutpostJson.DeepCopy(reference)));
            var weak=OutpostJson.DeepCopy(reference);int remove=weak.Run.Workers.Count/2;
            weak.Run.Workers.RemoveRange(weak.Run.Workers.Count-remove,remove);weak.Run.Levels[(int)UpgradeBranch.Team]-=remove;
            double slower=Finish(Sim(weak));Assert.That(slower,Is.GreaterThan(normal*1.4));
            var better=OutpostJson.DeepCopy(reference);
            // Explicit better account: spends only the normal parts earned by the already-completed Mines1+2.
            better.Profile.Parts=Cfg.Rewards.CompletionParts(1)+Cfg.Rewards.CompletionParts(2);
            var fasterSim=Sim(better);Assert.That(fasterSim.Submit(OutpostCommand.UpgradeItem("pickaxe.common")).Ok,Is.True);
            Assert.That(better.Run.Targets.Last().MaxHpUnits,Is.EqualTo(max));double faster=Finish(fasterSim);Assert.That(faster,Is.LessThan(normal));
            double buying=Finish(Sim(OutpostJson.DeepCopy(reference)),true);Assert.That(buying,Is.LessThanOrEqualTo(normal+5));
            string file=Path.Combine(Root,"comparison-mine"+mine+".json");
            File.WriteAllText(file,Newtonsoft.Json.JsonConvert.SerializeObject(new{Mine=mine,ReferenceSeconds=normal,WeakerSeconds=slower,ImprovedPickaxeSeconds=faster,EarnedIncomePurchasesSeconds=buying},Newtonsoft.Json.Formatting.Indented));
        }

        [TestCase(3)] [TestCase(6)] [TestCase(7)] [TestCase(144)]
        public void Offline_CapRepeatedTimeSaveReloadAndFinalCompletion(int mine)
        {
            var state=Reference(mine);var cfg=Cfg;var map=OutpostLevelAdapter.ForRun(TestData.Balance,state.Run);
            var online=OutpostJson.DeepCopy(state);var onlineSim=Sim(online);
            var job=new OfflineSimulator(map,cfg).Begin(state,86400000);job.RunToEnd();
            long cap=cfg.Balance.ZoneOfflineCapMs(mine);Assert.That(job.Summary.AppliedMs,Is.EqualTo(cap));
            onlineSim.Advance(onlineSim.Now+cap*(onlineSim.Balance.TicksPerSecond/1000));online.Run.LastProcessedUtcMs=86400000;
            Assert.That(OutpostJson.Serialize(job.State),Is.EqualTo(OutpostJson.Serialize(online)));
            state=OutpostJson.DeepCopy(job.State);long remaining=state.Run.Targets.Last().RemainingHpUnits;
            var saves=new OutpostSaveService(Path.Combine(Root,"save-check-"+mine)){Configs=cfg};
            Assert.That(saves.Save(state,OutpostSaveSlot.Main).Ok,Is.True,"save with map omitted and >4 giant miners");
            var loaded=saves.Load();
            Assert.That(loaded.State,Is.Not.Null,string.Join(";",loaded.Messages));
            Assert.That(loaded.State.Run.Targets.Last().RemainingHpUnits,Is.EqualTo(remaining));
            state=loaded.State;map=OutpostLevelAdapter.ForRun(TestData.Balance,state.Run);
            var repeat=new OfflineSimulator(map,cfg).Begin(state,86400000);repeat.RunToEnd();Assert.That(repeat.Summary.AppliedMs,Is.Zero);
            Assert.That(repeat.State.Run.Targets.Last().RemainingHpUnits,Is.EqualTo(remaining));
            var second=new OfflineSimulator(map,cfg).Begin(state,86400000+cap);second.RunToEnd();
            Assert.That(second.State.Run.Phase,Is.EqualTo(RunPhase.ReadyToContinue));
            Assert.That(second.State.Run.Zone,Is.EqualTo(mine));
            Assert.That(second.State.Run.Targets.Last().ReleasedUnits,Is.EqualTo(second.State.Run.Targets.Last().TotalUnits));
            Assert.That(second.State.Profile.CompletedZones.Contains(mine),Is.True);
            Assert.That(OutpostInvariants.Check(second.State,map,cfg.Equipment,cfg.Cards),Is.Empty);
        }

        [Test]
        public void HistoricalOrdinaryHpComparison()
        {
            var before=JArray.Parse(File.ReadAllText(Path.Combine(Root,"ordinary-hp-before.json")));var rows=new List<object>();
            foreach(var mine in before)
            {
                int z=(int)mine["Mine"];var state=StageProgressionService.NewGame(TestData.Balance,Cfg,1,0,z);
                int compared=0,removed=0;
                foreach(var old in mine["Targets"])
                {
                    int id=(int)old["Id"];var t=state.Run.Targets.FirstOrDefault(x=>x.Id==id);
                    Assert.That(t,Is.Not.Null,"ordinary target preserved in Mine "+z+" id "+id);
                    Assert.That(t.MaxHpUnits,Is.EqualTo((long)old["MaxHpUnits"]),"Mine "+z+" target "+id);
                    compared++;
                }
                rows.Add(new{Mine=z,Compared=compared,ReplacedByRescue=removed,AllComparedHpUnchanged=true});
            }
            File.WriteAllText(Path.Combine(Root,"ordinary-hp-comparison.json"),Newtonsoft.Json.JsonConvert.SerializeObject(rows,Newtonsoft.Json.Formatting.Indented));
        }
    }
}
