using System;
using System.Collections;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>Actual Unity scenes loaded from the declared reference fixtures; accelerated real simulation, no visual HP edits.</summary>
    [Explicit("Final excavation gameplay screenshots")]
    public sealed class FinalExcavationEvidenceTests : OutpostCardsFixture
    {
        static string Root=>Path.Combine(PlayModeUtil.ReportsDir(),"final-excavations");
        static string Shots=>Path.Combine(PlayModeUtil.ReportsDir(),"collapsed-mine","shots");
        static string CompactShots => Path.Combine(PlayModeUtil.ReportsDir(), "compact-final");

        [UnityTest, Timeout(180000)]
        public IEnumerator CompactEntrance_NativeArt_FourColumns_AndDistinctMiningCells()
        {
            Directory.CreateDirectory(CompactShots);
            yield return WaitPlaying();
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Game.LoadZone(3);
            for (int i = 0; i < 1200 && (Game.State.Run.Zone != 3 || Game.Phase != OutpostControllerPhase.Playing || Game.Hud.InTransition); i++) yield return null;
            Assert.That(Game.State.Run.Zone, Is.EqualTo(3));
            FirstSessionTestUtil.SkipPresentation(Game);
            var map = Game.Map;
            var final = map.Target(map.Final.TargetId);
            Assert.That(map.Final.Width, Is.EqualTo(4));
            Assert.That(final.WorkCells.Length, Is.EqualTo(4));
            var view = Game.Board.TargetViewOf(final.Id);
            var frame = view.GetComponentsInChildren<SpriteRenderer>().Single(r => r.sprite != null && r.sprite.name == "native_final_frame");
            Assert.That(frame.sprite.bounds.size.x, Is.EqualTo(4f).Within(0.001f));
            Assert.That(frame.sprite.pixelsPerUnit, Is.EqualTo(PixelArt.CellPixels));
            Assert.That(view.GetComponentsInChildren<SpriteRenderer>().Any(r => r.sprite != null && r.sprite.name.StartsWith("final_collapsed_")), Is.False);
            float normalSize = Game.Rig.Cam.orthographicSize;
            Game.Rig.SetExcavationView(true);
            Assert.That(Game.Rig.Cam.orthographicSize, Is.EqualTo(normalSize).Within(0.001f));
            Game.TryGetTargetWorldPosition(final.Id, out var at);
            Game.Rig.FocusOn(new Vector2(at.x, at.z - 2));
            yield return Capture("entrance-blocked.png", CompactShots);

            // Declared post-ordinary fixture: no level playthrough or player-save mutation.
            // All ordinary cargo is accounted as delivered; reference ranks are explicitly granted for this visual test.
            var cfg = Game.Configs;
            var plain = OutpostLevelAdapter.BuildMine(Game.Balance, 3, cfg.WithFinalExcavations(null));
            var reference = FinalExcavationReference.Build(plain, cfg, Game.Balance);
            var run = StageProgressionService.CreateRun(reference.Profile, 3, map, cfg, Game.Balance, Clock.UtcNowMs);
            var state = new OutpostGameState { Profile = reference.Profile, Run = run };
            run.Levels = (int[])reference.Levels.Clone();
            run.Workers.Clear(); run.NextWorkerId = 1;
            for (int i = 0; i < reference.Workers; i++) run.Workers.Add(new WorkerState
                { Id = run.NextWorkerId++, Look = i % 12, Cell = map.SpawnCell, Mode = WorkerMode.ChoosingTask });
            run.Slots = Enumerable.Range(0, 2 + run.Level(UpgradeBranch.Bays)).Select(_ => new DepotSlotState()).ToArray();
            var stats = TeamStats.Compute(run.Balance, run.Levels, state.Profile, cfg.Equipment, 3, cfg.Cards, run.ExtraWorkers);
            System.Numerics.BigInteger income = 0;
            foreach (var target in run.Targets)
            {
                if (target.Id == final.Id) continue;
                target.RemainingHpUnits = 0; target.Destroyed = true; target.DestroyedTick = 0;
                target.ReleasedUnits = target.TotalUnits; target.UpdateDamageStage();
                run.Counters.ClearedTargets++; run.Counters.ReleasedUnits += target.TotalUnits; run.Counters.DeliveredUnits += target.TotalUnits;
                income += stats.CoinValueFor(target.Kind, map.Target(target.Id).TypeId, target.ValueFactor) * target.TotalUnits;
            }
            run.TotalCredited = income; run.Wallet = income;
            run.Counters.ReleasedValue = income; run.Counters.DeliveredValue = income; run.Counters.FirstDeliveryTick = 0;
            run.GatesOpened = map.Gates.Length;
            foreach (var factory in run.Factories) factory.Status = FactoryStatus.PendingActivation;
            var sim = new OutpostSimulation(state, map, cfg) { RecordEvents = false };
            Assert.That(sim.Submit(OutpostCommand.RewardBonus(RewardedBonusKind.Gold, 1)).Ok, Is.True);
            for (int i = 0; i < 5000 && run.Workers.Count(w => w.Mode == WorkerMode.Mining && w.TargetId == final.Id) < 4; i++)
                Assert.That(sim.AdvanceToNextEvent(), Is.True);
            Assert.That(run.Workers.Count(w => w.Mode == WorkerMode.Mining && w.TargetId == final.Id), Is.EqualTo(4));
            Assert.That(OutpostInvariants.Check(state, map, cfg.Equipment, cfg.Cards), Is.Empty);
            Game.SwitchProfile(false); yield return WaitPlaying();
            var saves = new OutpostSaveService(OutpostController.SaveDirectoryOverride) { Configs = cfg };
            Assert.That(saves.Save(state, OutpostSaveSlot.Debug, map, true).Ok, Is.True);
            Game.SwitchProfile(true); yield return WaitPlaying(); yield return null;
            FirstSessionTestUtil.SkipPresentation(Game); Game.Hud.CloseAllWindows();
            Game.TryGetTargetWorldPosition(final.Id, out at);
            Game.Rig.FocusOn(new Vector2(at.x, at.z - 1));
            yield return Capture("entrance-four-miners.png", CompactShots);
            int maximum = 0;
            var targetState = Game.Sim.TargetById(final.Id);
            foreach (int stage in new[] { 2, 4, 5 })
            {
                Game.Sim.RecordEvents = false;
                for (int i = 0; targetState.DamageStage < stage && i < 30000; i++)
                {
                    Assert.That(Game.Sim.AdvanceToNextEvent(), Is.True);
                    var miners = Game.State.Run.Workers.Where(w => w.Mode == WorkerMode.Mining).ToArray();
                    Assert.That(miners.Select(w => w.Cell).Distinct().Count(), Is.EqualTo(miners.Length));
                    Assert.That(miners.All(w => w.Cell == w.WorkCell), Is.True);
                    maximum = Math.Max(maximum, miners.Count(w => w.TargetId == final.Id));
                }
                Game.Sim.RecordEvents = true;
                Assert.That(targetState.DamageStage, Is.GreaterThanOrEqualTo(stage));
                Game.Board.Sync(Game.Sim, 0); Game.Hud.CloseAllWindows();
                yield return Capture("entrance-stage" + stage + ".png", CompactShots);
                Assert.That(OutpostInvariants.Check(Game.State, Game.Map, cfg.Equipment, cfg.Cards), Is.Empty);
            }
            Assert.That(maximum, Is.EqualTo(4));
            File.WriteAllText(Path.Combine(CompactShots, "verification.json"), OutpostJson.Serialize(new
                { mine = 3, width = 4, nativePixelsPerCell = 32, miningPositions = 4, maximumConcurrentMiners = maximum,
                    sharedMiningCells = false, measuredReferenceSeconds = (targetState.DestroyedTick - targetState.FirstStrikeTick) / (double)Game.Sim.Balance.TicksPerSecond, source = "explicit post-ordinary reference fixture; real final-phase simulation", fullLevelRun = false }) + "\n");
        }
        [UnityTest,Timeout(600000)]
        public IEnumerator Formations_RealDamageStagesAndFinalReveal()
        {
            Directory.CreateDirectory(Shots);
            var equipment=new System.Collections.Generic.HashSet<string>();
            foreach(int mine in new[]{3,4,5,6})
            {
                if(Game.IsDebugProfile){Game.SwitchProfile(false);yield return WaitPlaying();}
                var state=OutpostJson.Deserialize<OutpostGameState>(File.ReadAllText(Path.Combine(Root,"reference-mine"+mine+".json")));
                state.Run.LastProcessedUtcMs=Clock.UtcNowMs;
                var p=state.Profile;
                ProgressionService.MarkShown(p,ProgressionService.CalloutShown);
                ProgressionService.MarkShown(p,ProgressionService.CalloutKey("bruno"));
                ProgressionService.MarkShown(p,ProgressionService.RevealPrefix+"mia");
                ProgressionService.MarkShown(p,ProgressionService.RevealPrefix+"bruno");
                ProgressionService.MarkShown(p,ProgressionService.CardsIntroShown);
                ProgressionService.MarkShown(p,ProgressionService.EquipmentIntroShown);
                var map=OutpostLevelAdapter.ForRun(Game.Balance,state.Run);
                var saves=new OutpostSaveService(OutpostController.SaveDirectoryOverride){Configs=Game.Configs};
                Assert.That(saves.Save(state,OutpostSaveSlot.Debug,map,true).Ok,Is.True);
                Game.SwitchProfile(true);yield return WaitPlaying();yield return null;
                FirstSessionTestUtil.SkipPresentation(Game);Game.Hud.CloseAllWindows();
                var final=Game.State.Run.Targets.Last();
                Assert.That(Game.Map.Target(final.Id).IsFinal,Is.True);
                var finalView=Game.Board.TargetViewOf(final.Id);
                Assert.That(finalView.Material,Is.EqualTo(HitMaterial.Stone));
                Assert.That(finalView.GetComponentsInChildren<SpriteRenderer>().Any(r=>r.sprite!=null&&r.sprite.name=="native_final_frame"),Is.True);
                foreach(var renderer in Game.Board.GetComponentsInChildren<SpriteRenderer>())
                    if(renderer.sprite!=null&&renderer.sprite.name.StartsWith("prop_") && !renderer.sprite.name.Contains("cage"))
                    {
                        equipment.Add(renderer.sprite.name);
                        Assert.That(renderer.GetComponent<Collider>(),Is.Null,"stationary decor has no hitbox");
                    }
                Game.Rig.SetExcavationView(true);
                Assert.That(Game.Board.TryGetTargetWorldPosition(final.Id,out var at),Is.True);
                Game.Rig.FocusOn(new Vector2(at.x,at.z));
                yield return Capture("mine"+mine+"-stage0.png");
                int[] stages=mine==3?new[]{1,2,3,4,5}:mine==4?new[]{2}:new[]{2,4};
                foreach(int stage in stages)
                {
                    Game.Sim.RecordEvents=false;
                    for(int frames=0;final.DamageStage<stage&&frames<2000;frames++)
                    {
                        Game.Sim.Advance(Game.Sim.Now+120*Game.Sim.Balance.TicksPerSecond,2500);
                        yield return null;
                    }
                    Game.Sim.RecordEvents=true;
                    Assert.That(final.DamageStage,Is.GreaterThanOrEqualTo(stage));
                    Game.Board.Sync(Game.Sim,0);Game.Hud.CloseAllWindows();
                    yield return Capture("mine"+mine+"-stage"+stage+".png");
                    Assert.That(OutpostInvariants.Check(Game.State,Game.Map,Game.Configs.Equipment,Game.Configs.Cards),Is.Empty);
                }
                // Look back down the ordinary route at the stationary equipment around the map.
                var middle=Game.Board.CellCenter(Game.Map.Width/2,Game.Map.Depth/2);
                Game.Rig.FocusOn(new Vector2(middle.x,middle.z));
                yield return Capture("mine"+mine+"-surroundings.png");
            }
            Assert.That(equipment.Count,Is.GreaterThanOrEqualTo(8),"varied equipment across the four displayed mines");
            File.WriteAllText(Path.Combine(Shots,"equipment-variety.json"),OutpostJson.Serialize(equipment.OrderBy(x=>x).ToArray()));
        }

        IEnumerator Capture(string name, string directory = null)
        {
            var camera=Game.Rig.Cam;var canvas=Game.Hud.GetComponent<Canvas>();var scaler=Game.Hud.GetComponent<CanvasScaler>();
            var texture=new RenderTexture(1080,1920,24){filterMode=FilterMode.Point};var previous=camera.targetTexture;float scale=canvas.scaleFactor;
            try
            {
                camera.targetTexture=texture;scaler.enabled=false;canvas.scaleFactor=1;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
                yield return PlayModeUtil.WaitReal(.25f);Game.Hud.RefreshNow();Game.Hud.CloseAllWindows();Canvas.ForceUpdateCanvases();camera.Render();
                var active=RenderTexture.active;RenderTexture.active=texture;var image=new Texture2D(1080,1920,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1080,1920),0,0);image.Apply();RenderTexture.active=active;
                File.WriteAllBytes(Path.Combine(directory ?? Shots,name),image.EncodeToPNG());UnityEngine.Object.Destroy(image);
            }
            finally
            {
                camera.targetTexture=previous;canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;canvas.scaleFactor=scale;scaler.enabled=true;
                texture.Release();UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
