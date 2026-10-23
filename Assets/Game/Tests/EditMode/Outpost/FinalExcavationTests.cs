using System;
using System.Linq;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    public class FinalExcavationTests
    {
        public static readonly int[] Sample = {1,2,3,4,5,6,7,10,16,20,23,30,37,46,49,59,66,70,80,91,105,110,120,130,144};
        static OutpostConfigSet Configs => OutpostConfigSet.LoadDefault().WithCards(TestData.Balance);

        [TestCaseSource(nameof(Sample))]
        public void Layout_OrdinaryContentAndRuntimeHpRemainIdentical(int mine)
        {
            var cfg=Configs;
            var old=StageProgressionService.NewGame(TestData.Balance,cfg.WithFinalExcavations(null),1,0,mine);
            var current=StageProgressionService.NewGame(TestData.Balance,cfg,1,0,mine);
            var map=OutpostLevelAdapter.ForRun(TestData.Balance,current.Run);
            foreach(var t in old.Run.Targets)
            {
                var after=current.Run.Targets.Single(x=>x.Id==t.Id);
                Assert.That(after.MaxHpUnits,Is.EqualTo(t.MaxHpUnits),"ordinary HP "+t.Id);
                Assert.That(after.TotalUnits,Is.EqualTo(t.TotalUnits),"ordinary cargo "+t.Id);
            }
            Assert.That(current.Run.PriceFactor,Is.EqualTo(old.Run.PriceFactor));
            Assert.That(map.Targets.Count(t=>t.IsFinal),Is.EqualTo(mine>=3?1:0));
            Assert.That(current.Run.Targets.Count,Is.EqualTo(old.Run.Targets.Count+(mine>=3?9:0)));
            if(mine<3)return;
            var final=map.Targets.Single(t=>t.IsFinal);
            Assert.That(final.WorkCells.Length,Is.EqualTo(4));
            Assert.That(final.WorkCells.Distinct().Count(),Is.EqualTo(4));
            Assert.That(final.Cells.Length,Is.EqualTo(8));
            Assert.That(map.Targets.Last().Id,Is.EqualTo(final.Id));
            foreach (int cell in final.WorkCells)
            {
                Assert.That(map.Row(cell),Is.EqualTo(3));
                Assert.That(final.Cells.Any(c=>Math.Abs(map.Col(c)-map.Col(cell))+Math.Abs(map.Row(c)-map.Row(cell))==1),Is.True);
            }
            for(int row=1;row<map.Final.AddedRows;row++)
                Assert.That(Enumerable.Range(row*map.Width,map.Width).Count(map.IsPlayable),Is.EqualTo(4),"compact neck row "+row);
            var rocks=map.Targets.Where(t=>t.IsFinalApproach).ToArray();
            Assert.That(rocks.Length,Is.EqualTo(8));
            Assert.That(rocks.All(t=>t.Kind=='r'&&t.Cells.Length==1&&!current.Run.Targets[map.TargetIndex(t.Id)].Destroyed),Is.True);
            var sim=new OutpostSimulation(current,map,cfg);
            Assert.That(sim.StrikeBlockReason(final.Id),Is.EqualTo("locked"));
            Assert.That(sim.Navigation.From(map.IntakeCell).Dist[map.ExitCell],Is.LessThan(0),"exit physically behind final");
            // Snapshot rebuild has exactly the same cells, target IDs and static HP.
            var copy=OutpostJson.DeepCopy(current);var rebuilt=OutpostLevelAdapter.ForRun(TestData.Balance,copy.Run);
            Assert.That(rebuilt.Targets.Single(t=>t.IsFinal).Cells,Is.EqualTo(final.Cells));
            Assert.That(rebuilt.Targets.Select(t=>t.Id),Is.EqualTo(map.Targets.Select(t=>t.Id)));
            Assert.That(OutpostJson.Serialize(rebuilt.Final),Is.EqualTo(OutpostJson.Serialize(map.Final)));
            Assert.That(OutpostInvariants.Check(copy,rebuilt,cfg.Equipment,cfg.Cards),Is.Empty);
        }

        static (OutpostGameState State,OutpostMap Map,OutpostConfigSet Configs) FinalPhase(int mine)
        {
            var cfg=Configs;
            var b=OutpostJson.DeepCopy(cfg.Balance);b.TapDamageFactor=new Rational(1000000000);
            cfg=cfg.WithBalance(b);
            var state=StageProgressionService.NewGame(TestData.Balance,cfg,1,0,mine);
            var map=OutpostLevelAdapter.ForRun(TestData.Balance,state.Run);
            var sim=new OutpostSimulation(state,map,cfg) { RecordEvents=false };
            for(int guard=0;guard<map.Targets.Length+10 && map.Targets.Any(t=>!t.IsFinal&&!state.Run.Targets[map.TargetIndex(t.Id)].Destroyed);guard++)
            {
                bool changed=false;
                foreach(var t in map.Targets)
                    if(!t.IsFinal&&sim.CanStrike(t.Id)){Assert.That(sim.Submit(OutpostCommand.Strike(t.Id)).Ok,Is.True);changed=true;}
                Assert.That(changed,Is.True,"ordinary route reachable");
            }
            Assert.That(state.Run.Targets.Last().Destroyed,Is.False);
            state.Run.Balance.TapDamageFactor=Rational.Of(1,4);
            return(state,map,cfg.WithBalance(state.Run.Balance));
        }

        [TestCase(3)] [TestCase(6)]
        public void Offline_EqualsOnline_SaveReloadAndRepeatedUtcDoNotDuplicateWork(int mine)
        {
            var f=FinalPhase(mine);var cfg=f.Configs;
            var online=OutpostJson.DeepCopy(f.State);
            var sim=new OutpostSimulation(online,f.Map,cfg) {RecordEvents=false};
            var job=new OfflineSimulator(f.Map,cfg).Begin(f.State,3600000);job.RunToEnd();
            sim.Advance(sim.Now+3600*sim.Balance.TicksPerSecond);
            online.Run.LastProcessedUtcMs=3600000;
            Assert.That(OutpostJson.Serialize(job.State),Is.EqualTo(OutpostJson.Serialize(online)));
            var restored=OutpostJson.DeepCopy(job.State);var map=OutpostLevelAdapter.ForRun(TestData.Balance,restored.Run);
            Assert.That(restored.Run.Targets.Last().MaxHpUnits,Is.EqualTo(f.State.Run.Targets.Last().MaxHpUnits));
            var repeated=new OfflineSimulator(map,cfg).Begin(restored,3600000);repeated.RunToEnd();
            Assert.That(repeated.Summary.AppliedMs,Is.Zero);
            Assert.That(OutpostJson.Serialize(repeated.State),Is.EqualTo(OutpostJson.Serialize(restored)));
            var negative=new OfflineSimulator(map,cfg).Begin(restored,3599999);negative.RunToEnd();
            Assert.That(negative.Summary.AppliedMs,Is.Zero);
            Assert.That(OutpostInvariants.Check(restored,map,cfg.Equipment,cfg.Cards),Is.Empty);
        }

        [Test]
        public void FinalZeroHp_ThenCargoDrains_ThenCompletionAndRewardOnce()
        {
            var f=FinalPhase(3);var sim=new OutpostSimulation(f.State,f.Map,f.Configs) {RecordEvents=false};
            var final=f.State.Run.Targets.Last();
            Assert.That(f.State.Run.Phase,Is.EqualTo(RunPhase.Running));
            // A player may speed up the last rock. Destruction never skips outstanding deliveries.
            f.State.Run.Balance.TapDamageFactor=new Rational(1000000000);
            sim=new OutpostSimulation(f.State,f.Map,f.Configs) {RecordEvents=false};
            Assert.That(sim.Submit(OutpostCommand.Strike(final.Id)).Ok,Is.True);
            Assert.That(final.Destroyed,Is.True);Assert.That(final.DamageStage,Is.EqualTo(5));
            Assert.That(final.ReleasedUnits,Is.EqualTo(final.TotalUnits));
            Assert.That(f.State.Run.Phase,Is.EqualTo(RunPhase.DrainingCargo));
            Assert.That(f.State.Profile.CompletedZones.Contains(3),Is.False);
            for(int i=0;i<100000 && sim.Run.Phase!=RunPhase.ReadyToContinue;i++)Assert.That(sim.AdvanceToNextEvent(),Is.True);
            Assert.That(sim.Run.Phase,Is.EqualTo(RunPhase.ReadyToContinue));
            int receipts=f.State.Profile.RewardLog.Count;
            StageProgressionService.ApplyCompletion(f.State,f.Configs);
            Assert.That(f.State.Profile.RewardLog.Count,Is.EqualTo(receipts));
            Assert.That(OutpostInvariants.Check(f.State,f.Map,f.Configs.Equipment,f.Configs.Cards),Is.Empty);
        }

        [Test]
        public void HighHp_RewardRatioUsesSafeArithmeticAndStagesDoNotReleaseTwice()
        {
            var t=new TargetState {MaxHpUnits=long.MaxValue/2,RemainingHpUnits=long.MaxValue/4,TotalUnits=10000};
            Assert.That(t.ExpectedReleasedUnits(),Is.EqualTo(5000));t.UpdateDamageStage();Assert.That(t.DamageStage,Is.EqualTo(2));
            t.ReleasedUnits=t.ExpectedReleasedUnits();var copy=OutpostJson.DeepCopy(t);
            Assert.That(copy.ExpectedReleasedUnits()-copy.ReleasedUnits,Is.Zero);
        }

        [Test]
        public void ExistingStartedMine_DoesNotGainFinalOrLoseProgress()
        {
            var cfg=Configs;var state=StageProgressionService.NewGame(TestData.Balance,cfg.WithFinalExcavations(null),1,0,6);
            var map=OutpostLevelAdapter.ForRun(TestData.Balance,state.Run);var sim=new OutpostSimulation(state,map,cfg) {RecordEvents=false};sim.Advance(120*sim.Balance.TicksPerSecond);
            string before=OutpostJson.Serialize(state);var copy=OutpostJson.DeepCopy(state);
            Assert.That(StageProgressionService.RestartRunIfMapChanged(copy,TestData.Balance,cfg,0,out _),Is.False);
            Assert.That(copy.Run.MapFinal,Is.Null);Assert.That(OutpostJson.Serialize(copy),Is.EqualTo(before));
            Assert.That(OutpostLevelAdapter.ForRun(TestData.Balance,copy.Run).Targets.Any(t=>t.IsFinal),Is.False);
        }

        [Test]
        public void ReferenceBudget_FallsBackWithoutChangingOrdinaryHpOrLivePrices()
        {
            var cfg=Configs;var rules=OutpostJson.DeepCopy(cfg.FinalExcavations);rules.ReferenceHiringCostFactor=Rational.One;
            cfg=cfg.WithFinalExcavations(rules);
            var map=OutpostLevelAdapter.BuildMine(TestData.Balance,3,cfg.WithFinalExcavations(null));
            var reference=FinalExcavationReference.Build(map,cfg,TestData.Balance);
            Assert.That(reference.HiringCostFactor,Is.LessThan(Rational.One));
            Assert.That(reference.Spent,Is.LessThanOrEqualTo(reference.Income));
            Assert.That(reference.Workers,Is.EqualTo(reference.Stats.Workers));
            Assert.That(reference.Levels[(int)UpgradeBranch.Team],Is.EqualTo(cfg.Balance.Branch(UpgradeBranch.Team).Cap+reference.Stats.CardTeamCap));
        }
    }
}
