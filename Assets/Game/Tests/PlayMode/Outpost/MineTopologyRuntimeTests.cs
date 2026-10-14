using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Core.Sim;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    public sealed class MineTopologyRuntimeTests
    {
        string m_Directory;
        ManualClock m_Clock;
        OutpostController Game => GameBootstrap.OutpostController;
        static string Reports => Path.Combine(PlayModeUtil.ReportsDir(), "topology-wide", "screenshots");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_Directory = Path.Combine(Application.temporaryCachePath, "MineTopologyTests", Guid.NewGuid().ToString("N"));
            CameraRig.OverrideAspect = 9f / 16;
            m_Clock = new ManualClock(1_800_000_000_000L);
            OutpostController.ClockOverride = m_Clock;
            OutpostController.SaveDirectoryOverride = m_Directory;
            OutpostController.FileSystemOverride = null;
            GameBootstrap.LegacyMode = false;
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
            Game.Tutorial.Skip();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Game != null) Game.enabled = true;
            var cleanup = SceneManager.CreateScene("MineTopologyCleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
            var scene = SceneManager.GetSceneByName("CoreGame");
            if (scene.IsValid()) yield return SceneManager.UnloadSceneAsync(scene);
            CameraRig.OverrideAspect = 0;
            OutpostController.ClockOverride = null;
            OutpostController.SaveDirectoryOverride = null;
            OutpostController.ConfigsOverride = null;
            OutpostController.SuppressOfflineSummary = false;
            OutpostHud.BottomReserve = 0f;
            OutpostController.FileSystemOverride = null;
            AudioListener.pause = false;
            if (Directory.Exists(m_Directory)) Directory.Delete(m_Directory, true);
        }

        IEnumerator LoadMine(int zone)
        {
            if (!Game.IsDebugProfile) Game.SwitchProfile(true);
            Game.enabled = true;
            Game.LoadZone(zone);
            yield return null;
            Assert.That(Game.State.Run.Zone, Is.EqualTo(zone));
            Assert.That(Game.Map.Topology, Is.Not.Null);
            Game.Tutorial.Skip();
            Game.enabled = false;
        }

        void Present()
        {
            Game.Sim.Events.Clear();
            Game.Board.Sync(Game.Sim, 1f / 30);
            Game.Hud.RefreshNow();
        }

        [UnityTest]
        public IEnumerator EarlyMiddleLate_MiningRoutesPickingGatesAndCameraWorkInScene()
        {
            foreach (int zone in new[] { 1, 72, 144 })
            {
                yield return LoadMine(zone);
                var map = Game.Map;
                int wall = Enumerable.Range(0, map.Width * map.Depth).First(c => !map.IsPlayable(c));
                Assert.That(Game.Board.WorldToCell(Game.Board.CellCenter(wall)), Is.EqualTo(-1), "Structural rock is not a gameplay cell");
                Assert.That(Game.Sim.Navigation.Passable(wall), Is.False);
                var bot = new OutpostBot(BotPolicy.C);
                var worked = new HashSet<int>();
                for (int i = 0; i < 40; i++)
                {
                    Game.Sim.Advance(Game.Sim.Now + 5 * OutpostIds.TicksPerSecond);
                    bot.ApplyPolicy(Game.Sim);
                    foreach (var worker in Game.State.Run.Workers)
                    {
                        Assert.That(map.IsPlayable(worker.Cell), Is.True);
                        if (worker.TargetId >= 0) worked.Add(worker.TargetId);
                        if (worker.Path != null)
                            foreach (int cell in worker.Path) Assert.That(map.IsPlayable(cell), Is.True);
                    }
                    Present();
                    yield return null;
                }
                Assert.That(Game.State.Run.Counters.DeliveredUnits, Is.GreaterThan(0));
                Assert.That(worked.Count, Is.GreaterThan(3), "Workers independently choose accessible targets");
                var target = map.Targets.First(t => Game.CanStrike(t.Id));
                var view = Game.Board.TargetViewOf(target.Id);
                Assert.That(view, Is.Not.Null);
                Assert.That(Game.TryGetTargetWorldPosition(target.Id, out var targetWorld), Is.True);
                Assert.That(Game.Board.TryPickAtWorld(Game.Board.CellCenter(target.Cells[0]), out var picked), Is.True);
                Assert.That(picked.Id, Is.EqualTo(target.Id));
                long hp = Game.Sim.TargetById(target.Id).RemainingHpUnits;
                Assert.That(Game.Sim.Submit(OutpostCommand.Strike(target.Id)).Ok, Is.True);
                Assert.That(Game.Sim.TargetById(target.Id).RemainingHpUnits, Is.LessThan(hp));
                var completed = new OutpostBot(BotPolicy.C) { CheckInvariantsEveryTick = true }.Run(Game.Sim);
                Assert.That(completed.Success, Is.True, completed.Error ?? completed.WatchdogDiagnostic);
                Game.Board.Refresh();
                Present();
                Assert.That(Game.State.Run.GatesOpened, Is.EqualTo(map.Gates.Length));
                Assert.That(Game.Sim.Navigation.Passable(wall), Is.False, "Walls remain after complete clearance");
                Assert.That(Game.State.Run.Batches, Is.Empty);
                Assert.That(Game.State.Run.Counters.DeliveredUnits, Is.EqualTo(Game.State.Run.Counters.ReleasedUnits));
                Assert.That(OutpostInvariants.Check(Game.State, map, Game.Configs.Equipment, Game.Configs.Cards), Is.Empty);
                Game.Rig.FocusOn(map.Depth);
                Game.FocusBase();
                yield return PlayModeUtil.WaitReal(0.6f);
                Assert.That(Game.Rig.FocusZ, Is.LessThanOrEqualTo(Game.Rig.MinZ + 0.1f), "Return to base works at the wider bounds");
            }
        }

        [UnityTest, Explicit("Unity screenshot generator for the branching mine campaign")]
        public IEnumerator Screenshots_EarlyMiddleLate_ActiveAndFullyClearedGeometry()
        {
            Directory.CreateDirectory(Reports);
            var metadata = new List<object>();
            foreach (int zone in new[] { 1, 48, 144 })
            {
                yield return LoadMine(zone);
                var bot = new OutpostBot(BotPolicy.C);
                for (int i = 0; i < 12; i++)
                {
                    Game.Sim.Advance(Game.Sim.Now + 5 * OutpostIds.TicksPerSecond);
                    bot.ApplyPolicy(Game.Sim);
                    Present();
                    yield return null;
                }
                Game.Rig.FocusOn(Game.Board.BaseFocusZ + Game.Rig.BandWorldHeight() / 2f + 2f);
                yield return PlayModeUtil.WaitReal(0.35f);
                Capture("mine-" + zone.ToString("000") + "-active.png", false);
                Capture("mine-" + zone.ToString("000") + "-active-map.png", true);
                // Normal play scale at both side branches demonstrates horizontal exploration.
                var floors = Enumerable.Range(0, Game.Map.Width * Game.Map.Depth).Where(Game.Map.IsPlayable).ToArray();
                foreach (bool right in new[] { false, true })
                {
                    int cell = right ? floors.OrderByDescending(Game.Map.Col).First() : floors.OrderBy(Game.Map.Col).First();
                    Vector3 position = Game.Board.CellCenter(cell);
                    Game.Rig.FocusOn(new Vector2(position.x, position.z));
                    yield return null;
                    Capture("mine-" + zone.ToString("000") + (right ? "-right.png" : "-left.png"), false);
                }
                var completed = new OutpostBot(BotPolicy.C).Run(Game.Sim);
                Assert.That(completed.Success, Is.True, completed.Error ?? completed.WatchdogDiagnostic);
                Game.Board.Refresh();
                Present();
                Game.Hud.CloseAllWindows();
                yield return null;
                Capture("mine-" + zone.ToString("000") + "-cleared.png", true);
                metadata.Add(new { zone, topology = Game.Map.Topology.Layout, width = Game.Map.Width, depth = Game.Map.Depth,
                    profile = "isolated Debug", simulatedTime = true, policy = "C", resourceGrants = false,
                    freeExploration = Game.Rig.FreeExploration, viewportColumns = CameraRig.ExplorationVisibleColumns,
                    deliveredUnits = Game.State.Run.Counters.DeliveredUnits, requiredTargets = Game.Map.RequiredCount,
                    clearedTargets = Game.State.Run.Counters.ClearedTargets, gatesOpened = Game.State.Run.GatesOpened,
                    completedSeconds = completed.CompletedSec });
            }
            File.WriteAllText(Path.Combine(Reports, "screenshots.json"), OutpostJson.Serialize(metadata));
        }

        void Capture(string name, bool wholeMine)
        {
            var camera = Game.Rig.Cam;
            var canvas = Game.Hud.GetComponent<Canvas>();
            var scaler = Game.Hud.GetComponent<CanvasScaler>();
            int width = 1080;
            float worldHeight = Game.Map.Depth + 12f;
            int height = wholeMine ? Mathf.CeilToInt(worldHeight / (Game.Map.Width + 2f) * width) : 1920;
            var texture = new RenderTexture(width, height, 24) { filterMode = FilterMode.Point };
            var previous = camera.targetTexture;
            var position = camera.transform.position;
            float size = camera.orthographicSize, aspect = camera.aspect;
            bool canvasEnabled = canvas.enabled;
            try
            {
                camera.targetTexture = texture;
                // Each capture supplies its own aspect: whole-map shots must
                // not leave the next portrait camera using the Editor window's ratio.
                camera.aspect = width / (float)height;
                scaler.enabled = false;
                if (wholeMine)
                {
                    canvas.enabled = false;
                    camera.orthographicSize = worldHeight / 2f;
                    camera.transform.position = new Vector3(Game.Map.Width / 2f, position.y, (Game.Board.TopZ + 0f) / 2f);
                }
                else
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 1;
                    Canvas.ForceUpdateCanvases();
                }
                camera.Render();
                var active = RenderTexture.active;
                RenderTexture.active = texture;
                var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                RenderTexture.active = active;
                File.WriteAllBytes(Path.Combine(Reports, name), pixels.EncodeToPNG());
                UnityEngine.Object.Destroy(pixels);
            }
            finally
            {
                camera.targetTexture = previous;
                camera.aspect = aspect;
                camera.orthographicSize = size;
                camera.transform.position = position;
                canvas.enabled = canvasEnabled;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                scaler.enabled = true;
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
