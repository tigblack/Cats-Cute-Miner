using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CatMiners.Runtime.Integrations
{
    internal static class NativePrivacyConfiguration
    {
        public static bool MaxMatches(GameSdkConfiguration config)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resources = activity.Call<AndroidJavaObject>("getResources"))
                {
                    int id = resources.Call<int>("getIdentifier", "applovin_settings", "raw", activity.Call<string>("getPackageName"));
                    if (id == 0) return false;
                    using (var stream = resources.Call<AndroidJavaObject>("openRawResource", id))
                    using (var scanner = new AndroidJavaObject("java.util.Scanner", stream, "UTF-8"))
                    using (var delimited = scanner.Call<AndroidJavaObject>("useDelimiter", "\\A"))
                    {
                        var root = JObject.Parse(delimited.Call<string>("next"));
                        var flow = root["consent_flow_settings"];
                        return (string)root["sdk_key"] == config.maxSdkKey && flow != null &&
                            (bool?)flow["consent_flow_enabled"] == true &&
                            (string)flow["consent_flow_privacy_policy"] == config.privacyUrl &&
                            (string)flow["consent_flow_terms_of_service"] == config.termsUrl &&
                            flow["consent_flow_debug_user_geography"] == null;
                    }
                }
            }
            catch { return false; }
#else
            return false;
#endif
        }
        public static bool MetaAutoCollectionDisabled()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var manager = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (var info = manager.Call<AndroidJavaObject>("getApplicationInfo", activity.Call<string>("getPackageName"), 128))
                using (var metadata = info.Get<AndroidJavaObject>("metaData"))
                {
                    return metadata != null && !metadata.Call<bool>("getBoolean", "com.facebook.sdk.AutoInitEnabled", true) &&
                        !metadata.Call<bool>("getBoolean", "com.facebook.sdk.AutoLogAppEventsEnabled", true) &&
                        !metadata.Call<bool>("getBoolean", "com.facebook.sdk.AdvertiserIDCollectionEnabled", true);
                }
            }
            catch { return false; }
#else
            return false;
#endif
        }
    }
}
