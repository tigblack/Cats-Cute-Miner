using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CatMiners.Runtime.Integrations
{
    // Client identifiers only. Never put management credentials in this asset.
    [Serializable]
    public sealed class GameSdkConfiguration
    {
        public const string Package = "com.multicastgames.catscuteminer";
        // Rewarded advertising is restored for Android. Web and ordinary desktop previews stay provider-free.
        public static bool AdsEnabled
        {
            get {
#if UNITY_ANDROID && CATMINERS_MAX
                return true;
#else
                return false;
#endif
            }
        }
        public string packageName;
        public string privacyUrl, termsUrl;
        // Registered purchase event token for this Adjust app; deliberately blank until service registration.
        public string adjustPurchaseEventToken = "";
        [Serializable] public sealed class AdjustEventBinding { public string name, token; }
        public AdjustEventBinding[] adjustEvents = new AdjustEventBinding[0];
        public static bool EventTokenValid(string token) => Real(token) && token.Length == 6 && Regex.IsMatch(token, "^[a-z0-9]{6}$");
        public string EventToken(string name)
        {
            if (name == "purchase" || name == "ad_revenue" || name == "playtime_seconds_event" || adjustEvents == null) return null;
            string found = null;
            foreach (var binding in adjustEvents)
            {
                if (binding == null || binding.name != name) continue;
                // Ambiguous mappings and reuse of the revenue token fail closed.
                if (found != null || !EventTokenValid(binding.token) || binding.token == adjustPurchaseEventToken) return null;
                found = binding.token;
            }
            if (found != null)
                foreach (var binding in adjustEvents)
                    if (binding != null && binding.name != name && binding.token == found) return null;
            return found;
        }
        public string appMetricaKey, adjustToken, metaAppId, metaClientToken, maxSdkKey, rewardedAdUnit;
        // Own Android application ID for the Google UMP consent flow, even without Google ad mediation.
        public string admobAndroidAppId;
        public bool AdMobAppIdValid => !string.IsNullOrEmpty(admobAndroidAppId) &&
            Regex.IsMatch(admobAndroidAppId, "^ca-app-pub-[0-9]{16}~[0-9]{10}$") &&
            !admobAndroidAppId.StartsWith("ca-app-pub-3940256099942544~", StringComparison.Ordinal);
        public static GameSdkConfiguration Load()
        {
            var asset = Resources.Load<TextAsset>("Integrations/cats-cute-miner-sdk");
            try { return asset == null ? new GameSdkConfiguration() : JsonUtility.FromJson<GameSdkConfiguration>(asset.text) ?? new GameSdkConfiguration(); }
            catch { return new GameSdkConfiguration(); }
        }
        static bool Real(string value) => !string.IsNullOrWhiteSpace(value) &&
            !Regex.IsMatch(value, "dummy|example|placeholder|replace|test|devour|your", RegexOptions.IgnoreCase);
        public bool IdentityValid => packageName == Package;
        // Only this game's canonical documents; other tenants, query strings and swapped roles fail closed.
        static bool GameUrl(string value, string document) =>
            value == "https://sites.aimulticast.org/sargsyan/cats-cute-miner-legal/" + document + "/" ||
            value == "https://multicastgames.com/cats-cute-miner/" + document + "/";
        public bool LegalValid => GameUrl(privacyUrl, "privacy") && GameUrl(termsUrl, "terms");
        public bool AppMetricaValid => IdentityValid && Real(appMetricaKey) && Guid.TryParse(appMetricaKey, out var id) && id != Guid.Empty;
        public bool AdjustValid => IdentityValid && Real(adjustToken) && Regex.IsMatch(adjustToken, "^[a-z0-9]{12}$");
        public bool AdjustPurchaseValid => AdjustValid && EventTokenValid(adjustPurchaseEventToken);
        public bool MetaValid => IdentityValid && Real(metaAppId) && Regex.IsMatch(metaAppId, "^[0-9]{6,}$") &&
            Real(metaClientToken) && Regex.IsMatch(metaClientToken, "^[a-fA-F0-9]{32}$");
        public bool MaxValid => AdsEnabled && IdentityValid && AdMobAppIdValid && Real(maxSdkKey) && maxSdkKey.Length >= 60 &&
            Real(rewardedAdUnit) && Regex.IsMatch(rewardedAdUnit, "^[a-fA-F0-9]{16}$");
        public IEnumerable<string> Missing()
        {
            if (!IdentityValid) yield return "own package identity";
            if (!LegalValid) yield return "published game-specific legal URLs";
            if (!AppMetricaValid) yield return "own AppMetrica key";
            if (!AdjustValid) yield return "own Adjust production token";
            if (!MetaValid) yield return "own Meta app ID/client token";
            if (AdsEnabled && !MaxValid) yield return "MAX SDK key/own rewarded unit";
            if (AdsEnabled && !AdMobAppIdValid) yield return "own AdMob Android app ID for Google UMP";
        }
    }
    public enum ProviderState { Off, WaitingForConfiguration, Starting, Initialized, Failed }
    public enum GameEvent { GameLoaded, ConsentChosen, MineStarted, MineCompleted, SectorOpened, UpgradePurchased,
        CardUpgraded, ChestOpened, StorePurchased, RewardOffered, RewardEarned, RewardDelivered, RewardClosed, RewardFailed,
        TimeSpent, SessionStarted, SessionEnded, AdRequested, AdLoaded, AdLoadFailed, AdShow,
        AdDisplayed, AdDisplayFailed, AdShowFailed, AdClicked, AdHidden, AdCompleted, AdIncomplete, AdRewardSaved,
        StoreOpened, PurchaseStarted, PurchasePending, PurchaseValidationStarted, PurchaseValidated,
        PurchaseGranted, PurchaseCancelled, PurchaseFailed, PurchaseRestore, PurchaseConfirmed,
        WorkerHired, EquipmentChanged, ItemUpgraded, FactoryUnlocked, FactoryAutomated, GoodsSold,
        BonusGranted, DynamiteLaunched, DynamiteExploded, ElixirFound, CurrencyEarned, CurrencySpent,
        AdRevenueUnavailable,
        // Append only: persisted diagnostics and existing provider bindings retain their ordinals.
        PlaytimeMinutes, StoreChestOpened, ArenaChestOpened, TutorialCardGranted }
}
