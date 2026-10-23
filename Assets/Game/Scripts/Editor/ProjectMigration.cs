using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CatMiners.Editor
{
    public static class ProjectMigration
    {
        static readonly string[] LegacyAssets =
        {
            "Assets/TextMesh Pro/Fonts/LiberationSans.ttf",
            "Assets/TextMesh Pro/Sprites/EmojiOne.png",
            "Assets/FacebookSDK/Examples/Windows/PurchaseButton.prefab",
            "Assets/FacebookSDK/SDK/Editor/iOS/FBSDK+Internal.h",
            "Assets/FacebookSDK/SDK/Editor/iOS/FBUnityInterface.h",
            "Assets/FacebookSDK/SDK/Editor/iOS/FBUnityInterface.mm",
            "Assets/FacebookSDK/SDK/Editor/iOS/FBUnitySDKDelegate.h",
            "Assets/FacebookSDK/SDK/Editor/iOS/FBUnitySDKDelegate.m",
            "Assets/FacebookSDK/SDK/Editor/iOS/FBUnityUtility.h",
            "Assets/FacebookSDK/SDK/Editor/iOS/FBUnityUtility.mm"
        };

        [MenuItem("Cat Miners/Maintenance/Upgrade imported asset metadata")]
        public static void UpgradeImportedAssets()
        {
            var paths = LegacyAssets.Where(File.Exists).ToArray();
            foreach (string path in paths)
            {
                var importer = AssetImporter.GetAtPath(path);
                if (importer != null) importer.SaveAndReimport();
            }
            AssetDatabase.ForceReserializeAssets(paths,
                ForceReserializeAssetsOptions.ReserializeAssetsAndMetadata);
            AssetDatabase.SaveAssets();
            Debug.Log("[CatMiners] Imported asset metadata upgraded for Unity " + Application.unityVersion);
        }
    }
}
