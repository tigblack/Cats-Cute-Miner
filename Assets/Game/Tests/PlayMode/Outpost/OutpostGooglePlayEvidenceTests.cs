using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Runtime;
using Newtonsoft.Json;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    // Uses the fixture's unique temporary save directory; never changes the owner's save.
    [Explicit("Google Play gameplay captures and caption compositions")]
    public sealed class OutpostGooglePlayEvidenceTests : OutpostCardsFixture
    {
        readonly List<object> captures = new List<object>();
        string Output => Path.Combine(PlayModeUtil.ReportsDir(), "google-play-store");

        IEnumerator Capture(string name, string caption)
        {
            Assert.That(Game.IsDebugProfile, Is.False);
            var camera = Game.Rig.Cam;
            var canvas = Game.Hud.GetComponent<Canvas>();
            var scaler = Game.Hud.GetComponent<CanvasScaler>();
            var oldTarget = camera.targetTexture;
            var oldRect = camera.rect;
            var oldScale = canvas.scaleFactor;
            var oldActive = RenderTexture.active;
            var render = new RenderTexture(1080, 1920, 24) { filterMode = FilterMode.Point };
            RenderTexture composition = null;
            GameObject captionRoot = null, captionCameraRoot = null;
            try
            {
                camera.targetTexture = render;
                camera.rect = new Rect(0, 0, 1, 1);
                scaler.enabled = false;
                canvas.scaleFactor = 1;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                yield return PlayModeUtil.WaitReal(0.4f);
                Game.Hud.RefreshNow();
                Canvas.ForceUpdateCanvases();
                var texts = Game.Hud.GetComponentsInChildren<TMP_Text>();
                foreach (var text in texts) text.ForceMeshUpdate();
                Assert.That(texts.Where(t => !string.IsNullOrEmpty(t.text) && t.isTextTruncated).Select(t => t.text), Is.Empty);
                Assert.That(texts.Any(t => t.text.Contains("[DEBUG]")), Is.False);
                camera.Render();
                Directory.CreateDirectory(Output);
                Save(render, name + ".png");

                // Console requests a 9:16 phone image. Keep a complete 9:16 game viewport
                // below a separate 160px title; side margins preserve that viewport's aspect.
                // Both cameras render directly: the caption never covers gameplay or UI.
                composition = new RenderTexture(1080, 1920, 24) { filterMode = FilterMode.Point };
                camera.targetTexture = composition;
                camera.rect = new Rect(45f / 1080, 0, 990f / 1080, 1760f / 1920);
                captionCameraRoot = new GameObject("Store caption camera");
                var headerCamera = captionCameraRoot.AddComponent<Camera>();
                headerCamera.enabled = false;
                headerCamera.cullingMask = 1 << 31;
                headerCamera.clearFlags = CameraClearFlags.SolidColor;
                headerCamera.backgroundColor = new Color32(15, 24, 55, 255);
                headerCamera.targetTexture = composition;
                headerCamera.rect = new Rect(0, 1760f / 1920, 1, 160f / 1920);
                headerCamera.orthographic = true;
                headerCamera.orthographicSize = 80;
                captionRoot = new GameObject("Store caption", typeof(RectTransform), typeof(Canvas));
                captionRoot.layer = 31;
                var headerCanvas = captionRoot.GetComponent<Canvas>();
                headerCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                headerCanvas.worldCamera = headerCamera;
                headerCanvas.planeDistance = 1;
                var label = PixelUi.Text(captionRoot.transform, "Caption", caption, 56,
                    new Color32(255, 222, 136, 255), bold: true);
                label.gameObject.layer = 31;
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(28, 16);
                label.rectTransform.offsetMax = new Vector2(-28, -16);
                Canvas.ForceUpdateCanvases();
                label.ForceMeshUpdate();
                Assert.That(label.isTextTruncated, Is.False, caption);
                foreach (var text in texts) text.ForceMeshUpdate();
                Assert.That(texts.Where(t => !string.IsNullOrEmpty(t.text) && t.isTextTruncated).Select(t => t.text), Is.Empty);
                RenderTexture.active = composition;
                GL.Clear(true, true, headerCamera.backgroundColor);
                camera.Render();
                headerCamera.Render();
                Save(composition, name + "-caption.png");
                captures.Add(new {
                    name, caption, raw = name + ".png", composition = name + "-caption.png",
                    rawWidth = 1080, rawHeight = 1920, compositionWidth = 1080, compositionHeight = 1920,
                    gameplayViewportWidth = 990, gameplayViewportHeight = 1760,
                    source = "actual_unity_editor_gameplay_temporary_main_save",
                    nativeAndroidCapture = false, debugProfile = false, language = "en",
                    captionOutsideGame = true, gameplayUiRetouched = false,
                    qaInventorySeeded = name == "04-equipment"
                });
            }
            finally
            {
                camera.targetTexture = oldTarget;
                camera.rect = oldRect;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                canvas.scaleFactor = oldScale;
                scaler.enabled = true;
                RenderTexture.active = oldActive;
                if (captionRoot != null) UnityEngine.Object.Destroy(captionRoot);
                if (captionCameraRoot != null) UnityEngine.Object.Destroy(captionCameraRoot);
                render.Release();
                UnityEngine.Object.Destroy(render);
                if (composition != null) { composition.Release(); UnityEngine.Object.Destroy(composition); }
            }
        }

        void Save(RenderTexture render, string name)
        {
            var previous = RenderTexture.active;
            var pixels = new Texture2D(render.width, render.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = render;
                pixels.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(Output, name), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(pixels); }
        }

        [UnityTest]
        public IEnumerator CaptureStoreScreens()
        {
            // Mining/cards use normal starter progress; equipment below uses catalog items in this QA save.
            Game.Hud.CloseAllWindows();
            // Advance through real frames: one large clock jump intentionally triggers offline catch-up.
            for (int frame = 0; frame < 48; frame++) { Clock.Advance(0.25); yield return null; }
            if (Game.Hud.OfflineOpen) { Game.DismissOffline(); yield return WaitPlaying(); }
            Game.Hud.CloseAllWindows();
            Assert.That(Game.Hud.OfflineOpen, Is.False);
            yield return Capture("01-cat-crew", "BUILD YOUR CAT CREW");
            Game.Hud.ShowBase();
            yield return Capture("02-upgrades", "UPGRADE YOUR MINERS");
            Game.Hud.CloseAllWindows();
            Game.Hud.ShowCards();
            yield return Capture("03-cards", "COLLECT 62 CARDS");
            Game.Hud.CloseAllWindows();
            // Show the implemented collection rather than an empty starter inventory. These are real
            // level-one catalog items in the fixture's disposable save, without runtime/dev grants.
            foreach (var item in Game.Configs.Equipment.Items)
                EquipmentService.Grant(Game.State.Profile, item, Game.Configs);
            EquipmentService.EquipBest(Game.State.Profile, Game.Configs.Equipment);
            Game.Hud.ShowEquipment();
            yield return Capture("04-equipment", "EQUIP YOUR TEAM");
            File.WriteAllText(Path.Combine(Output, "captures.json"), JsonConvert.SerializeObject(new {
                checkedUtc = DateTime.UtcNow.ToString("o"), unity = Application.unityVersion,
                status = "editor_capture_pending_visual_review_and_android_comparison", captures
            }, Formatting.Indented));
        }
    }
}
