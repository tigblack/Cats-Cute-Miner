using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace CatMiners.Editor.GooglePlay
{
    public static class GooglePlayIcons
    {
        const string IconPath = "Assets/Game/Art/Store/CatsCuteMinerIcon.png";
        const string BackgroundPath = "Assets/Game/Art/Store/CatsCuteMinerIconBackground.png";

        public static void Apply()
        {
            if (!File.Exists(IconPath)) throw new InvalidOperationException("Game launcher icon is missing.");
            // A separate opaque background fills Android adaptive masks. The full key art is the foreground.
            if (!File.Exists(BackgroundPath))
            {
                var background = new Texture2D(16, 16, TextureFormat.RGB24, false);
                background.SetPixels(Enumerable.Repeat(new Color32(15, 24, 55, 255), 256).Select(c => (Color)c).ToArray());
                background.Apply();
                File.WriteAllBytes(BackgroundPath, background.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(background);
            }
            AssetDatabase.Refresh();
            foreach (var path in new[] { IconPath, BackgroundPath })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.maxTextureSize = 1024;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            var backgroundIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            var target = NamedBuildTarget.Android;
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(target))
            {
                var slots = PlayerSettings.GetPlatformIcons(target, kind);
                foreach (var slot in slots)
                {
                    var textures = Enumerable.Repeat(icon, Math.Max(1, slot.minLayerCount)).ToArray();
                    if (textures.Length > 1) textures[0] = backgroundIcon;
                    slot.SetTextures(textures);
                }
                PlayerSettings.SetPlatformIcons(target, kind, slots);
            }
            AssetDatabase.SaveAssets();
            var readback = PlayerSettings.GetSupportedIconKinds(target)
                .SelectMany(kind => PlayerSettings.GetPlatformIcons(target, kind)).ToArray();
            if (readback.Length == 0 || readback.Any(slot => slot.GetTextures().Count(t => t != null) < slot.minLayerCount))
                throw new InvalidOperationException("Android launcher icon readback has empty required layers.");
            Directory.CreateDirectory("release/google-play/evidence");
            File.WriteAllText("release/google-play/evidence/launcher-icons.json", JsonConvert.SerializeObject(new {
                checkedUtc = DateTime.UtcNow.ToString("o"),
                status = "unity_settings_assigned_artifact_and_device_verification_pending",
                icon = IconPath,
                defaultIconAssigned = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any).Any(t => t == icon),
                slots = readback.Select(slot => new {
                    width = slot.width, height = slot.height, requiredLayers = slot.minLayerCount,
                    textures = slot.GetTextures().Select(AssetDatabase.GetAssetPath).ToArray()
                }).ToArray()
            }, Formatting.Indented));
        }
    }
}
