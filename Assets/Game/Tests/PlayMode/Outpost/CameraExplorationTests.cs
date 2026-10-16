using System;
using System.Collections;
using System.IO;
using System.Linq;
using CatMiners.Core.Sim;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CatMiners.Tests
{
    public sealed class CameraExplorationTests
    {
        CameraRig m_Rig;
        float m_PreviousAspect;

        [SetUp]
        public void SetUp()
        {
            m_PreviousAspect = CameraRig.OverrideAspect;
            CameraRig.OverrideAspect = 9f / 16f;
            var camera = new GameObject("ExplorationTestCamera").AddComponent<Camera>();
            camera.aspect = CameraRig.OverrideAspect;
            m_Rig = CameraRig.Create(camera);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(m_Rig.gameObject);
            CameraRig.OverrideAspect = m_PreviousAspect;
        }

        [TestCase(9f / 16f)]
        [TestCase(16f / 9f)]
        public void WideBoard_LocalViewportAndBounds_KeepBothEdgesReachable(float aspect)
        {
            CameraRig.OverrideAspect = aspect;
            m_Rig.Cam.aspect = aspect;
            m_Rig.SetBoard(81, 120, 40f, true, 40.5f);
            Assert.That(2f * m_Rig.Cam.orthographicSize * aspect, Is.EqualTo(10f).Within(0.001f));
            Assert.That(m_Rig.FocusX, Is.EqualTo(40.5f));
            m_Rig.ScrollWorld(new Vector2(-1000f, -1000f));
            Assert.That(m_Rig.FocusX, Is.EqualTo(m_Rig.MinX));
            Assert.That(m_Rig.FocusZ, Is.EqualTo(m_Rig.MinZ));
            Assert.That(m_Rig.FocusX - 5f, Is.EqualTo(-CameraRig.WallMargin).Within(0.001f));
            m_Rig.ScrollWorld(new Vector2(1000f, 1000f));
            Assert.That(m_Rig.FocusX, Is.EqualTo(m_Rig.MaxX));
            Assert.That(m_Rig.FocusZ, Is.EqualTo(m_Rig.MaxZ));
            Assert.That(m_Rig.FocusX + 5f, Is.EqualTo(81f + CameraRig.WallMargin).Within(0.001f));
            float x = m_Rig.Cam.transform.position.x * PixelArt.CellPixels;
            Assert.That(x, Is.EqualTo(Mathf.Round(x)).Within(0.001f), "Horizontal motion retains pixel snapping");
            float horizontal = m_Rig.FocusX;
            m_Rig.ScrollByPixels(10f);
            Assert.That(m_Rig.FocusX, Is.EqualTo(horizontal), "Legacy float API only moves z");
        }

        [Test]
        public void DefaultBoard_StillFitsWidth_AndIgnoresHorizontalInput()
        {
            m_Rig.SetBoard(81, 120, 40f);
            Assert.That(m_Rig.FreeExploration, Is.False);
            Assert.That(2f * m_Rig.Cam.orthographicSize * m_Rig.Cam.aspect, Is.GreaterThanOrEqualTo(81f));
            m_Rig.ScrollByPixels(new Vector2(200f, 30f));
            Assert.That(m_Rig.FocusX, Is.EqualTo(40.5f));
            m_Rig.FocusOn(new Vector2(10f, 50f));
            Assert.That(m_Rig.FocusX, Is.EqualTo(40.5f));
            m_Rig.SetBoard(7, 30, 0f, true);
            Assert.That(m_Rig.MinX, Is.EqualTo(m_Rig.MaxX), "Narrow exploration boards stay centered");
        }

        [UnityTest]
        public IEnumerator SmoothFocus_FlingAndPanelReturn_PreserveBothAxes()
        {
            m_Rig.SetBoard(81, 120, 60f, true, 60f);
            m_Rig.FocusOnSmooth(new Vector2(20f, 30f), 0.1f);
            yield return new WaitForSecondsRealtime(0.16f);
            Assert.That(m_Rig.FocusX, Is.EqualTo(20f).Within(0.001f));
            Assert.That(m_Rig.FocusZ, Is.EqualTo(30f).Within(0.001f));
            m_Rig.Fling(new Vector2(-400f, -400f));
            yield return null;
            yield return null;
            Assert.That(m_Rig.FocusX, Is.GreaterThan(20f));
            Assert.That(m_Rig.FocusZ, Is.GreaterThan(30f));
            m_Rig.StopFling();
            float x = m_Rig.FocusX, z = m_Rig.FocusZ;
            yield return null;
            Assert.That(m_Rig.FocusX, Is.EqualTo(x));
            Assert.That(m_Rig.FocusZ, Is.EqualTo(z));
            m_Rig.SetContextTop(m_Rig.Cam.pixelHeight * 0.5f);
            m_Rig.ScrollWorld(-1000f);
            Assert.That(m_Rig.FocusZ, Is.LessThan(m_Rig.MinZ));
            m_Rig.SetContextTop(0f);
            yield return new WaitForSecondsRealtime(CameraRig.RevealSeconds + 0.1f);
            Assert.That(m_Rig.FocusZ, Is.EqualTo(m_Rig.MinZ).Within(0.001f));
            Assert.That(m_Rig.FocusX, Is.EqualTo(x), "Panel reveal/return preserves horizontal exploration");
        }
    }

    public sealed class OutpostExplorationInputTests
    {
        string m_Directory;
        Mouse m_Mouse;
        Touchscreen m_Touch;
        InputSettings.BackgroundBehavior m_Background;
        InputSettings.EditorInputBehaviorInPlayMode m_EditorInput;
        OutpostController Game => GameBootstrap.OutpostController;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_Directory = Path.Combine(Application.temporaryCachePath, "OutpostExplorationInputTests", Guid.NewGuid().ToString("N"));
            OutpostController.SaveDirectoryOverride = m_Directory;
            OutpostController.ClockOverride = new ManualClock(1_800_000_000_000L);
            OutpostController.FileSystemOverride = null;
            GameBootstrap.LegacyMode = false;
            CameraRig.OverrideAspect = 0f;
            m_Background = InputSystem.settings.backgroundBehavior;
            m_EditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            m_Mouse = InputSystem.AddDevice<Mouse>();
            m_Touch = InputSystem.AddDevice<Touchscreen>();
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
            Assert.That(Game.Map.Topology, Is.Not.Null);
            Assert.That(Game.Rig.FreeExploration, Is.True);
            Assert.That(Game.Map.Width, Is.GreaterThan(11));
            Game.enabled = false; // Freeze simulation; pointer input and camera still receive real frame updates.
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var cleanup = SceneManager.CreateScene("ExplorationCleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
            var scene = SceneManager.GetSceneByName("CoreGame");
            if (scene.IsValid()) yield return SceneManager.UnloadSceneAsync(scene);
            InputSystem.RemoveDevice(m_Touch);
            InputSystem.RemoveDevice(m_Mouse);
            InputSystem.settings.backgroundBehavior = m_Background;
            InputSystem.settings.editorInputBehaviorInPlayMode = m_EditorInput;
            OutpostController.SaveDirectoryOverride = null;
            OutpostController.ConfigsOverride = null;
            OutpostController.SuppressOfflineSummary = false;
            OutpostHud.BottomReserve = 0f;
            OutpostController.ClockOverride = null;
            OutpostController.FileSystemOverride = null;
            CameraRig.OverrideAspect = 0f;
            AudioListener.pause = false;
            if (Directory.Exists(m_Directory)) Directory.Delete(m_Directory, true);
        }

        Vector2 CenterField()
        {
            Game.Hud.FieldBandPixels(out float bottom, out float top);
            return new Vector2(Screen.width / 2f, (bottom + top) / 2f);
        }

        [UnityTest]
        public IEnumerator SideBranches_ExistInFloorMesh_AndCanBeBroughtIntoView()
        {
            var map = Game.Map;
            var cells = Enumerable.Range(0, map.Width * map.Depth).Where(map.IsPlayable).ToArray();
            int left = cells.OrderBy(map.Col).First(), right = cells.OrderByDescending(map.Col).First();
            Assert.That(map.Col(right) - map.Col(left), Is.GreaterThan(10), "Branches extend beyond the local viewport");
            var floor = Game.Board.GetComponentsInChildren<MeshFilter>()
                .First(f => f.gameObject.name == "TopologyFloorAndRock");
            var vertices = floor.sharedMesh.vertices;
            foreach (int cell in new[] { left, right })
            {
                Vector3 world = Game.Board.CellCenter(cell);
                Vector3 local = floor.transform.InverseTransformPoint(world);
                Assert.That(vertices.Any(v => Mathf.Abs(v.x - (local.x - 0.5f)) < 0.001f &&
                    Mathf.Abs(v.z - (local.z - 0.5f)) < 0.001f), Is.True, "Offscreen side branch has floor geometry");
                Game.Rig.FocusOn(new Vector2(world.x, world.z));
                yield return null;
                Vector3 screen = Game.Rig.Cam.WorldToScreenPoint(world);
                Assert.That(screen.x, Is.InRange(Screen.safeArea.xMin, Screen.safeArea.xMax));
                Game.Hud.FieldBandPixels(out float bottom, out float top);
                Assert.That(screen.y, Is.InRange(bottom, top));
            }
        }

        [UnityTest]
        public IEnumerator RealMouse_DiagonalDragFromRock_DoesNotStrike_AndBaseReturnsBothAxes()
        {
            var target = Game.Map.Targets.Where(t => Game.CanStrike(t.Id))
                .OrderBy(t => Mathf.Abs(Game.Board.CellCenter(t.Cells[0]).x - Game.Map.Width / 2f)).First();
            Assert.That(Game.TryGetTargetWorldPosition(target.Id, out var world), Is.True);
            Game.Rig.FocusOn(new Vector2(world.x, world.z));
            yield return null;
            Vector2 point = Game.Rig.Cam.WorldToScreenPoint(world);
            long hp = Game.Sim.TargetById(target.Id).RemainingHpUnits;
            float x = Game.Rig.FocusX, z = Game.Rig.FocusZ;
            InputSystem.QueueStateEvent(m_Mouse, new MouseState { position = point });
            yield return null;
            InputSystem.QueueStateEvent(m_Mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
            yield return null;
            point += new Vector2(60f, -60f);
            InputSystem.QueueStateEvent(m_Mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
            yield return null;
            Assert.That(Game.Rig.FocusX, Is.LessThan(x));
            Assert.That(Game.Rig.FocusZ, Is.GreaterThan(z));
            InputSystem.QueueStateEvent(m_Mouse, new MouseState { position = point });
            yield return null;
            Assert.That(Game.Board.ActiveStrikes, Is.Zero);
            Assert.That(Game.Sim.TargetById(target.Id).RemainingHpUnits, Is.EqualTo(hp));
            Assert.That(Game.Selection.Kind, Is.EqualTo(OutpostSelectionKind.None));
            Game.Rig.FocusOn(new Vector2(Game.Rig.MaxX, Game.Rig.MaxZ));
            Game.FocusBase();
            yield return new WaitForSecondsRealtime(0.7f);
            Assert.That(Game.Rig.FocusX, Is.EqualTo(Game.Board.CellCenter(Game.Map.SpawnCell).x).Within(0.001f));
            Assert.That(Game.Rig.FocusZ, Is.EqualTo(Game.Rig.MinZ).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator RealTouch_DiagonalSwipeAndCancellation_DoNotTapOrContinueMotion()
        {
            Game.Rig.FocusOn(new Vector2(Game.Map.Width / 2f, (Game.Rig.MinZ + Game.Rig.MaxZ) / 2f));
            var hp = Game.State.Run.Targets.Select(t => t.RemainingHpUnits).ToArray();
            Vector2 point = CenterField();
            float x = Game.Rig.FocusX, z = Game.Rig.FocusZ;
            InputSystem.QueueStateEvent(m_Touch, new TouchState { touchId = 1, position = point, phase = UnityEngine.InputSystem.TouchPhase.Began });
            yield return null;
            point += new Vector2(-60f, -60f);
            InputSystem.QueueStateEvent(m_Touch, new TouchState { touchId = 1, position = point, phase = UnityEngine.InputSystem.TouchPhase.Moved });
            yield return null;
            Assert.That(Game.Rig.FocusX, Is.GreaterThan(x));
            Assert.That(Game.Rig.FocusZ, Is.GreaterThan(z));
            InputSystem.QueueStateEvent(m_Touch, new TouchState { touchId = 1, position = point, phase = UnityEngine.InputSystem.TouchPhase.Ended });
            yield return null;
            Assert.That(Game.Board.ActiveStrikes, Is.Zero);
            CollectionAssert.AreEqual(hp, Game.State.Run.Targets.Select(t => t.RemainingHpUnits).ToArray());
            Game.Rig.StopFling();
            point = CenterField();
            InputSystem.QueueStateEvent(m_Touch, new TouchState { touchId = 2, position = point, phase = UnityEngine.InputSystem.TouchPhase.Began });
            yield return null;
            point += new Vector2(40f, 40f);
            InputSystem.QueueStateEvent(m_Touch, new TouchState { touchId = 2, position = point, phase = UnityEngine.InputSystem.TouchPhase.Moved });
            yield return null;
            InputSystem.QueueStateEvent(m_Touch, new TouchState { touchId = 2, position = point, phase = UnityEngine.InputSystem.TouchPhase.Canceled });
            yield return null;
            x = Game.Rig.FocusX; z = Game.Rig.FocusZ;
            yield return null;
            yield return null;
            Assert.That(Game.Rig.FocusX, Is.EqualTo(x));
            Assert.That(Game.Rig.FocusZ, Is.EqualTo(z));
            Assert.That(Game.Selection.Kind, Is.EqualTo(OutpostSelectionKind.None));
        }

        [UnityTest]
        public IEnumerator RealWheel_TwoAxes_AndWindowCancelsHeldGesture()
        {
            Game.Rig.FocusOn(new Vector2(Game.Map.Width / 2f, (Game.Rig.MinZ + Game.Rig.MaxZ) / 2f));
            Vector2 point = CenterField();
            float x = Game.Rig.FocusX, z = Game.Rig.FocusZ;
            InputSystem.QueueStateEvent(m_Mouse, new MouseState { position = point });
            yield return null;
            InputSystem.QueueStateEvent(m_Mouse, new MouseState { position = point, scroll = new Vector2(120f, 120f) });
            yield return null;
            Assert.That(Game.Rig.FocusX, Is.GreaterThan(x));
            Assert.That(Game.Rig.FocusZ, Is.GreaterThan(z));
            InputSystem.QueueStateEvent(m_Mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
            yield return null;
            point += new Vector2(40f, -40f);
            InputSystem.QueueStateEvent(m_Mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
            yield return null;
            Game.Hud.ShowBase();
            InputSystem.QueueStateEvent(m_Mouse, new MouseState { position = point });
            yield return null;
            x = Game.Rig.FocusX; z = Game.Rig.FocusZ;
            yield return null;
            Assert.That(Game.Rig.FocusX, Is.EqualTo(x));
            Assert.That(Game.Rig.FocusZ, Is.EqualTo(z));
            Assert.That(Game.Board.ActiveStrikes, Is.Zero);
            Assert.That(Game.Hud.BaseOpen, Is.True);
        }
    }
}
