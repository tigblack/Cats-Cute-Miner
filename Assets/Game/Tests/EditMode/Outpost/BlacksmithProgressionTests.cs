using System;
using System.Linq;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    /// <summary>Focused Mine 2 rescue/completion and compatible save migration; no campaign traversal.</summary>
    public sealed class BlacksmithProgressionTests
    {
        const long Now = 1_800_000_000_000L;
        const string Bruno = ProgressionConfig.BlacksmithCharacter;
        static OutpostConfigSet s_Configs;
        static OutpostConfigSet Configs => s_Configs ?? (s_Configs = OutpostConfigSet.LoadDefault().WithCards(TestData.Balance));
        static OutpostGameState NewMine2() => StageProgressionService.NewGame(TestData.Balance, Configs, 57, Now, 2);
        static OutpostSimulation Sim(OutpostGameState state) => new OutpostSimulation(state,
            OutpostLevelAdapter.ForRun(TestData.Balance, state.Run), Configs);

        [Test]
        public void Config_CompletionGateCannotFallBackToMineEntry_AndIdentityIsDistinct()
        {
            var cfg = ProgressionConfig.Parse(Configs.Progression.CanonicalJson());
            Assert.AreEqual(ProgressionConfig.EventEquipmentReady, cfg.Feature(ProgressionConfig.Equipment).Event);
            Assert.AreEqual(0, cfg.Feature(ProgressionConfig.Equipment).Zone);
            Assert.AreEqual("mia", cfg.RescueForZone(1).Character);
            Assert.AreEqual(Bruno, cfg.RescueForZone(2).Character);
            Assert.IsNull(cfg.RescueForZone(3));
            cfg.Feature(ProgressionConfig.Equipment).Zone = 3;
            Assert.Throws<OutpostConfigException>(() => cfg.Validate());
        }

        [Test]
        public void Equipment_RequiresRescueAndActualCompletion_NotMineEntryOrPendingDelivery()
        {
            var state = NewMine2();
            var p = state.Profile;
            Assert.AreEqual(Bruno, state.Run.MapDressing.Rescue.Character);
            Assert.IsFalse(ProgressionService.IsAvailable(p, Configs, ProgressionConfig.Equipment));
            p.HighestZone = 3; // A debug jump/entry is not a completion receipt.
            ProgressionService.Sync(p, state.Run, Configs);
            ProgressionService.ApplyEntryGrants(p, 3, Configs);
            Assert.IsFalse(p.Progression.IsUnlocked(ProgressionConfig.Equipment));
            Assert.AreEqual(0, p.ItemLevel("pickaxe.common"));
            ProgressionService.Rescue(p, Bruno);
            state.Run.Phase = RunPhase.DrainingCargo;
            ProgressionService.Sync(p, state.Run, Configs);
            Assert.IsFalse(p.Progression.IsUnlocked(ProgressionConfig.Equipment), "delivery must finish first");
            p.Progression.Crew.Remove(Bruno);
            p.CompletedZones.Add(2);
            ProgressionService.Sync(p, state.Run, Configs);
            Assert.IsFalse(p.Progression.IsUnlocked(ProgressionConfig.Equipment), "a new-layout cage still requires rescue");
            ProgressionService.Rescue(p, Bruno);
            ProgressionService.Sync(p, state.Run, Configs);
            Assert.IsTrue(p.Progression.IsUnlocked(ProgressionConfig.Equipment));
            Assert.AreEqual(2, state.Run.Zone, "usable before Mine 3 loads");
            Assert.AreEqual(1, p.ItemLevel("pickaxe.common"));
        }

        [Test]
        public void Starter_CompletionReloadAndOldMine3Path_GrantExactlyOnce_NoDuplicateParts()
        {
            var state = NewMine2();
            ProgressionService.Rescue(state.Profile, Bruno);
            state.Profile.CompletedZones.Add(2);
            ProgressionService.Sync(state.Profile, state.Run, Configs);
            long parts = state.Profile.Parts;
            var copy = OutpostJson.DeepCopy(state);
            ProgressionService.PrepareLoaded(copy.Profile, copy.Run, Configs);
            ProgressionService.Sync(copy.Profile, copy.Run, Configs);
            ProgressionService.ApplyEntryGrants(copy.Profile, 3, Configs);
            ProgressionService.ApplyEntryGrants(copy.Profile, 4, Configs);
            Assert.AreEqual(1, copy.Profile.ItemLevel("pickaxe.common"));
            Assert.AreEqual(parts, copy.Profile.Parts);
            Assert.AreEqual(1, copy.Profile.Progression.Grants.Count(x => x == ProgressionService.GrantEquipment));
            Assert.AreEqual(1, copy.Profile.Progression.Grants.Count(x => x == ProgressionService.GrantBlacksmithService));
            Assert.IsNull(copy.Profile.Equipped[(int)EquipSlot.Pickaxe], "real equip is the player's action");
        }

        [Test]
        public void Starter_ReusesBetterOwnedPickaxe_AndRepairsMissingStarterReceipt()
        {
            var state = NewMine2();
            var p = state.Profile;
            EquipmentService.Grant(p, "pickaxe.rare", Configs);
            p.Items["pickaxe.rare"] = 3;
            p.Progression.Grants.Add(ProgressionService.GrantEquipment);
            ProgressionService.Rescue(p, Bruno);
            p.CompletedZones.Add(2);
            ProgressionService.Sync(p, state.Run, Configs);
            Assert.AreEqual("pickaxe.rare", ProgressionService.StarterItem(p, Configs));
            Assert.AreEqual(3, p.ItemLevel("pickaxe.rare"));
            Assert.AreEqual(0, p.ItemLevel("pickaxe.common"));
            p.Items.Clear(); // An old receipt without a functional item must not strand the player.
            Assert.IsTrue(ProgressionService.EnsureStarter(p, Configs));
            Assert.AreEqual(1, p.ItemLevel("pickaxe.common"));
            Assert.IsFalse(ProgressionService.EnsureStarter(p, Configs));
        }

        [TestCase(0, true, false)]
        [TestCase(1, true, false)]
        [TestCase(1, false, true)]
        public void Migration_CompletedMine2OrExistingOwner_KeepsSaveAndServiceWithoutTutorial(int version, bool completed, bool owner)
        {
            var state = NewMine2();
            var p = state.Profile;
            p.Progression.Version = version;
            if (completed) p.CompletedZones.AddRange(new[] { 1, 2 });
            if (owner) { EquipmentService.Grant(p, "pickaxe.rare", Configs); p.Items["pickaxe.rare"] = 4; }
            state.Run.Targets[0].RemainingHpUnits /= 2;
            string runBefore = OutpostJson.Serialize(state.Run);
            Assert.IsTrue(ProgressionService.PrepareLoaded(p, state.Run, Configs));
            Assert.AreEqual(runBefore, OutpostJson.Serialize(state.Run), "no reset of started mine, cargo or workers");
            Assert.IsTrue(p.Progression.HasCrew(Bruno));
            Assert.IsTrue(p.Progression.IsUnlocked(ProgressionConfig.Equipment));
            Assert.IsTrue(p.Progression.WasShown(ProgressionService.RevealPrefix + Bruno));
            Assert.IsTrue(p.Progression.WasShown(ProgressionService.EquipmentIntroShown));
            Assert.Greater(p.ItemLevel(ProgressionService.StarterItem(p, Configs)), 0);
            if (owner) Assert.AreEqual(4, p.ItemLevel("pickaxe.rare"));
            string once = OutpostJson.Serialize(state);
            Assert.IsFalse(ProgressionService.PrepareLoaded(p, state.Run, Configs));
            Assert.AreEqual(once, OutpostJson.Serialize(state));
        }

        [Test]
        public void Migration_StartedMine2WithoutCage_PreservesLayoutAndReceivesServiceAtCompletion()
        {
            var state = NewMine2();
            state.Profile.Progression.Version = 1;
            state.Run.MapDressing.Rescue = null; // A pre-Bruno snapshot is left intact.
            string run = OutpostJson.Serialize(state.Run);
            ProgressionService.PrepareLoaded(state.Profile, state.Run, Configs);
            Assert.IsFalse(state.Profile.Progression.HasCrew(Bruno));
            Assert.AreEqual(run, OutpostJson.Serialize(state.Run));
            state.Profile.CompletedZones.Add(2);
            ProgressionService.Sync(state.Profile, state.Run, Configs);
            Assert.IsTrue(state.Profile.Progression.HasCrew(Bruno));
            Assert.IsTrue(state.Profile.Progression.IsUnlocked(ProgressionConfig.Equipment));
            Assert.AreEqual(1, state.Profile.ItemLevel("pickaxe.common"));
        }

        [Test]
        public void Rescue_Mine2LateApproachAndReturn_AutomatedCats_ThenEquipBeforeMine3()
        {
            var state = NewMine2();
            var sim = Sim(state);
            var map = sim.Map;
            Assert.IsTrue(sim.HasRescue);
            Assert.AreEqual(Bruno, map.Rescue.Character);
            Assert.IsTrue(OutpostReachability.Check(map).Passed);
            Assert.IsFalse(map.Props[map.Rescue.CageProp].PipeMount);
            var lockTarget = map.Target(map.Rescue.LockTargetId);
            Assert.AreNotEqual(map.RescueCageCell, map.Cell(lockTarget.Column, lockTarget.Row));
            Assert.Less(map.Row(map.RescueCageCell), map.Depth / 2, "late half of mine, furthest from base");
            int workers = state.Run.Workers.Count;
            long end = 7_200L * OutpostIds.TicksPerSecond;
            while (sim.RescuePending && sim.Now < end && !sim.IsEconomyStopped)
                sim.Advance(Math.Min(end, sim.Now + 2 * OutpostIds.TicksPerSecond));
            Assert.IsFalse(sim.RescuePending, "normal cats free Bruno without manual damage/purchases");
            Assert.AreEqual(1, state.Profile.Progression.Crew.Count(x => x == Bruno));
            Assert.AreEqual(workers, state.Run.Workers.Count, "Bruno is a service, never a free worker");
            int from = map.Cell(lockTarget.Column, lockTarget.Row);
            Assert.IsNotNull(sim.Navigation.BuildPath(from, map.SpawnCell), "real route back after lock destruction");
            while (state.Run.Phase != RunPhase.ReadyToContinue && sim.Now < end && !sim.IsEconomyStopped)
                sim.Advance(Math.Min(end, sim.Now + 2 * OutpostIds.TicksPerSecond));
            Assert.AreEqual(RunPhase.ReadyToContinue, state.Run.Phase);
            Assert.Contains(2, state.Profile.CompletedZones);
            Assert.IsTrue(state.Profile.Progression.IsUnlocked(ProgressionConfig.Equipment));
            var before = sim.Stats.DamageUnits;
            var equip = sim.Submit(OutpostCommand.Equip("pickaxe.common"));
            Assert.IsTrue(equip.Ok, "actual equip command is allowed in ReadyToContinue: " + equip.Reason);
            Assert.Greater(sim.Stats.DamageUnits, before);
            Assert.AreEqual(2, state.Run.Zone);
            Assert.AreEqual(1, sim.Events.Count(x => x.Type == OutpostEventType.CharacterRescued && x.ItemId == Bruno));
        }

        [Test]
        public void SeparateRescueIdentities_AndMiaStillShopkeeper()
        {
            var mia = OutpostLevelAdapter.BuildMine(TestData.Balance, 1, Configs);
            var bruno = OutpostLevelAdapter.BuildMine(TestData.Balance, 2, Configs);
            Assert.AreEqual("mia", mia.Rescue.Character);
            Assert.AreEqual(Bruno, bruno.Rescue.Character);
            Assert.AreNotEqual(ProgressionService.CalloutKey("mia"), ProgressionService.CalloutKey(Bruno));
            var state = NewMine2();
            ProgressionService.Rescue(state.Profile, "mia");
            Assert.IsTrue(ProgressionService.Rescued(state.Profile, Configs));
            Assert.IsFalse(ProgressionService.BlacksmithRescued(state.Profile, Configs));
            Assert.IsTrue(state.Run.Workers.All(x => x.Crew == null));
        }
    }
}
